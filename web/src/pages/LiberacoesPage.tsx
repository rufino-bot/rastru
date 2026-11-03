import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import {
  listarLiberacoesPendentes, responderLiberacao, type LiberacaoPendenteDto,
} from '../api/expedicao'
import { formatarData, formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { ehConflito } from '../api/execucao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pagina } from '../components/Pagina'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { Pilula } from '../components/Pilula'
import { CampoDeQuantidade } from '../expedicao/escritaDoPainel'
import { usePermissoesDaExpedicao } from '../expedicao/usePermissoesDaExpedicao'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { useDevolverFoco } from '../hooks/useDevolverFoco'

/** A coluna `Texto` de `dbo.LiberacaoDimensional` é `NVARCHAR(500)`; o servidor recusa acima disso. */
const TAMANHO_MAXIMO_DA_OBSERVACAO = 500

interface PropsDoPainel {
  item: LiberacaoPendenteDto
  nome: string
  aoFechar: () => void
  /** A resposta foi gravada: quem abriu o painel recarrega a lista. */
  aoResponder: () => void
  /** O servidor disse 409 (outra pessoa já respondeu, ou o pedido foi estornado): a frase e a recarga são de quem abriu. */
  aoConflito: (mensagem: string) => void
}

/**
 * "Liberar para expedir" (D7, C6 da spec da Fase 5A): a Gestão responde ao pedido da Qualidade. O campo abre com
 * tudo o que foi pedido, e "Liberar {q}" manda a quantidade escolhida; "Recusar tudo" é liberar zero e não depende
 * do campo. A observação é opcional e vai sem os espaços das pontas, ou nula quando fica em branco.
 *
 * O `ref` guarda a função inteira contra o segundo toque no mesmo quadro (qualquer um dos dois botões), antes de o
 * React redesenhar os botões desabilitados. Na recusa, a frase do servidor fica dentro do painel, com o digitado;
 * no 409 a frase sobe também para a tela, porque a recarga tira o cartão da lista e o painel vai com ele.
 */
function PainelDeResposta({ item, nome, aoFechar, aoResponder, aoConflito }: PropsDoPainel) {
  const [texto, setTexto] = useState(() => quantidadeParaCampo(item.quantidade))
  const [observacao, setObservacao] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const enviandoRef = useRef(false)

  const leitura = lerQuantidade(texto, item.quantidade)

  async function responder(quantidadeLiberada: number) {
    if (enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await responderLiberacao(item.id, quantidadeLiberada, observacao.trim() === '' ? null : observacao.trim())
      aoResponder()
      aoFechar()
    } catch (falha) {
      const frase = mensagemDeErro(falha, 'Não foi possível responder à liberação.')
      setErro(frase)
      if (ehConflito(falha)) aoConflito(frase)
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (leitura.erro !== null) return
    responder(leitura.valor)
  }

  return (
    <PainelDeEscrita
      titulo="Liberar para expedir"
      subtitulo={nome}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <CampoDeQuantidade
        rotulo="Quantidade liberada"
        texto={texto}
        erro={leitura.erro}
        aoMudar={setTexto}
        desabilitado={enviando}
      />
      <Campo rotulo="Observação (opcional)">
        {(id) => (
          <textarea
            id={id}
            value={observacao}
            onChange={(e) => setObservacao(e.target.value)}
            maxLength={TAMANHO_MAXIMO_DA_OBSERVACAO}
            rows={3}
            disabled={enviando}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <BannerDeErro mensagem={erro} />
      <div className="flex flex-wrap gap-2">
        <Botao
          type="submit"
          carregando={enviando}
          rotuloCarregando="Registrando…"
          disabled={leitura.erro !== null}
        >
          {leitura.erro === null ? `Liberar ${formatarQuantidade(leitura.valor)}` : 'Liberar'}
        </Botao>
        <Botao variante="secundario" onClick={() => responder(0)} disabled={enviando}>
          Recusar tudo
        </Botao>
      </div>
    </PainelDeEscrita>
  )
}

interface PropsDoCartao {
  item: LiberacaoPendenteDto
  responder: boolean
  /** A lista está recarregando: o foco de um painel que fechou espera a lista nova para saber aonde voltar. */
  recarregando: boolean
  aoAbrirPainel: () => void
  aoResponder: () => void
  aoConflito: (mensagem: string) => void
}

/** Um pedido de liberação: a Peça, o Pedido, o que foi pedido e por quê, e quem pediu e quando. */
function CartaoDeLiberacao({ item, responder, recarregando, aoAbrirPainel, aoResponder, aoConflito }: PropsDoCartao) {
  const [aberto, setAberto] = useState(false)
  const botao = useRef<HTMLButtonElement>(null)
  const titulo = useRef<HTMLSpanElement>(null)

  useDevolverFoco(aberto, () => botao.current ?? titulo.current, !recarregando)

  const nome = `${rotuloDoNo(item.peca)} (${item.pedido.numero})`

  return (
    <ItemComAcao
      acao={responder && !aberto ? (
        <Botao
          ref={botao}
          variante="secundario"
          aria-label={`Responder ${nome}`}
          onClick={() => { setAberto(true); aoAbrirPainel() }}
        >
          Responder
        </Botao>
      ) : null}
      painel={responder && aberto ? (
        <PainelDeResposta
          item={item}
          nome={nome}
          aoFechar={() => setAberto(false)}
          aoResponder={aoResponder}
          aoConflito={aoConflito}
        />
      ) : null}
    >
      <span ref={titulo} tabIndex={-1} className="font-medium text-tinta">{rotuloDoNo(item.peca)}</span>
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="text-sm text-tinta">{`${item.pedido.numero} · ${item.pedido.cliente}`}</span>
        <span className="text-sm text-tinta-fraca">{`entrega ${formatarData(item.pedido.dataEntrega)}`}</span>
        {item.pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
      </span>
      <span className="text-sm font-medium text-tinta">{`Quantidade pedida: ${formatarQuantidade(item.quantidade)}`}</span>
      <span className="text-sm text-tinta">{item.justificativa}</span>
      <span className="text-xs text-tinta-fraca">{`${item.usuarioNome} em ${formatarDataHora(item.dataHora)}`}</span>
    </ItemComAcao>
  )
}

/**
 * `/liberacoes` — os pedidos de liberação que a Gestão ainda não respondeu (spec da Fase 5A, seção 6.4), do mais
 * antigo ao mais recente, na ordem que o servidor manda. Um cartão por pedido, com a Peça, o Pedido (com
 * "Atrasado" quando vier), a quantidade pedida, a justificativa e quem pediu e quando.
 *
 * "Responder" fica sob `usePermissoesDaExpedicao().responder` (gating na ação; a leitura é de todos): a Qualidade
 * abre a tela e vê a fila sem botão. A lista chega inteira e não se atualiza sozinha: recarrega depois de cada
 * resposta e no 409 (a frase do servidor sobe para a tela, porque o pedido respondido sai da lista com o painel).
 *
 * O foco: o painel de um cartão devolve o foco ao "Responder" dele se o cartão continua na tela. A resposta tira o
 * pedido da lista, e com ele o cartão: quem devolve o foco então é esta tela, ao título.
 */
export function LiberacoesPage() {
  const [itens, setItens] = useState<LiberacaoPendenteDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const sequencia = useRef(0)
  const { responder } = usePermissoesDaExpedicao()

  // O pedido do último painel aberto, para o foco voltar ao título se a recarga o tirar da lista.
  const ultimoCartao = useRef<number | null>(null)
  const titulo = useRef<HTMLSpanElement>(null)

  // A resposta que chega depois de uma mais nova é descartada: duas recargas seguidas não podem
  // terminar com a lista velha na tela.
  const carregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    try {
      const lidas = await listarLiberacoesPendentes()
      if (minha !== sequencia.current) return
      setItens(lidas)
      setErro(null)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErro(mensagemDeErro(e, 'Não foi possível carregar as liberações.'))
    } finally {
      if (minha === sequencia.current) setRecarregando(false)
    }
  }, [])

  useEffect(() => { carregar() }, [carregar])

  // O cartão do último painel não está mais na tela e o foco se perdeu com ele: vai ao título.
  useEffect(() => {
    if (recarregando || itens === null || ultimoCartao.current === null) return
    if (itens.some((i) => i.id === ultimoCartao.current)) return
    ultimoCartao.current = null
    const focado = document.activeElement
    if (focado === null || focado === document.body) titulo.current?.focus()
  }, [recarregando, itens])

  return (
    <Pagina titulo={<span ref={titulo} tabIndex={-1}>Liberações</span>}>
      <p className="text-sm text-tinta-fraca">Unidades não conformes que a Qualidade pediu para expedir.</p>
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {itens === null && erro === null && <EstadoCarregando />}
      {itens !== null && itens.length === 0 && <EstadoVazio titulo="Nenhuma liberação pendente." />}
      {itens !== null && itens.length > 0 && (
        <ListaDeCadastro rotulo="Liberações pendentes">
          {itens.map((item) => (
            <CartaoDeLiberacao
              key={item.id}
              item={item}
              responder={responder}
              recarregando={recarregando}
              aoAbrirPainel={() => { ultimoCartao.current = item.id; setAviso(null) }}
              aoResponder={() => { setAviso(null); carregar() }}
              aoConflito={(frase) => { setAviso(frase); carregar() }}
            />
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
