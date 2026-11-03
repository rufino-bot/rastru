import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { expedir, obterExpedicao, type PedidoNaExpedicaoDto } from '../api/expedicao'
import { formatarData, listarSetores, type SetorDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { BarraDoLote } from '../execucao/BarraDoLote'
import { CartaoDaPeca } from '../expedicao/CartaoDaPeca'
import { CHAVES_DA_EXPEDICAO, facetasDaExpedicao, filtrarExpedicao } from '../expedicao/filtroDaExpedicao'
import {
  alternar, camposInvalidos, digitar, estaMarcado, itensDoLote, marcadosDoLote, marcarTodos, pecasDosPedidos,
  podeEntrarNoLote, reconciliar, SAIU_DO_LOTE, todosMarcados, type LoteDaExpedicao,
} from '../expedicao/loteDaExpedicao'
import { RemessasRecentes } from '../expedicao/RemessasRecentes'
import { usePermissoesDaExpedicao } from '../expedicao/usePermissoesDaExpedicao'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'

/**
 * `/expedicao` — o fim do caminho da Peça (spec da Fase 5A, seção 6.1): o que está no local de expedição, por Pedido,
 * na ordem do prazo de entrega que o servidor manda. Um cartão por Peça (`CartaoDaPeca`), com os painéis de aferir,
 * contar, mandar para conserto, pedir liberação e expedir, e, no fim da tela, as "Remessas recentes", de onde se
 * estorna uma remessa (C20).
 *
 * A lista chega inteira e o filtro (Pedido e situação da aferição) é no cliente, como a Fila, as Tarefas e a
 * Separação (`FiltroDeDemanda`, seleção na URL). Ela não se atualiza sozinha: recarrega depois de cada escrita de
 * um cartão, do lote e do estorno de uma remessa, e no 409 de um painel.
 *
 * As escritas ficam sob `usePermissoesDaExpedicao` (gating na ação; a leitura é de todos): a Gestão abre a tela e
 * lê tudo, sem botão de registro.
 *
 * O lote (C9, `loteDaExpedicao`, `BarraDoLote`): a caixa marca a Peça, e a Peça marcada mostra o campo de
 * quantidade, com o expedível. Um `POST /remessas` leva todas as marcadas, tudo ou nada. Sucesso limpa a seleção e
 * recarrega; recusa recarrega e mantém a seleção, com a frase do servidor na barra. A recarga que tira da lista uma
 * Peça marcada a tira da seleção, com aviso, e o filtro não conta como sair: a marcada oculta vai no lote.
 *
 * O foco: o painel de um cartão devolve o foco ao botão dele depois da recarga (é o `CartaoDaPeca`). A remessa que
 * expede a Peça toda a tira da lista, e com ela o cartão: quem devolve o foco então é esta tela, ao título.
 */
export function ExpedicaoPage() {
  const [pedidos, setPedidos] = useState<PedidoNaExpedicaoDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const sequencia = useRef(0)
  const { registrar } = usePermissoesDaExpedicao()
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DA_EXPEDICAO)

  // Os Setores do cadastro, só para o "Mandar para conserto" de quem registra.
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [erroDosSetores, setErroDosSetores] = useState<string | null>(null)

  // Muda a cada escrita: a seção "Remessas recentes" recarrega a lista dela.
  const [versaoDasRemessas, setVersaoDasRemessas] = useState(0)

  // A Peça do último painel aberto, para o foco voltar ao título se a recarga a tirar da lista.
  const ultimoCartao = useRef<number | null>(null)
  const titulo = useRef<HTMLSpanElement>(null)

  const [lote, setLote] = useState<LoteDaExpedicao>({})
  const [erroDoLote, setErroDoLote] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviandoLote, setEnviandoLote] = useState(false)
  // O toque no mesmo quadro, antes de o React redesenhar o botão desabilitado — mesmo padrão do lote da Separação.
  const enviandoLoteRef = useRef(false)

  // A resposta que chega depois de uma mais nova é descartada: duas recargas seguidas não podem
  // terminar com a lista velha na tela.
  const carregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    try {
      const lida = await obterExpedicao()
      if (minha !== sequencia.current) return
      setPedidos(lida.pedidos)
      setErro(null)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErro(mensagemDeErro(e, 'Não foi possível carregar a expedição.'))
    } finally {
      if (minha === sequencia.current) setRecarregando(false)
    }
  }, [])

  useEffect(() => { carregar() }, [carregar])

  useEffect(() => {
    if (!registrar) return
    let ativo = true
    listarSetores(false)
      .then((lidos) => { if (ativo) setSetores(lidos) })
      .catch((e) => { if (ativo) setErroDosSetores(mensagemDeErro(e, 'Não foi possível carregar os Setores.')) })
    return () => { ativo = false }
  }, [registrar])

  /** Uma escrita mudou a Peça (ou um 409 disse que a tela estava velha): a lista e as remessas recarregam. */
  const atualizar = useCallback(() => {
    carregar()
    setVersaoDasRemessas((v) => v + 1)
  }, [carregar])

  const todas = useMemo(() => (pedidos ? pecasDosPedidos(pedidos) : []), [pedidos])

  // A recarga tirou da lista uma Peça marcada: ela sai da seleção, com aviso. O texto das que ficam não muda.
  useEffect(() => {
    if (pedidos === null) return
    const { lote: depois, saiu } = reconciliar(lote, todas)
    if (!saiu) return
    setLote(depois)
    setAviso(SAIU_DO_LOTE)
  }, [pedidos, todas, lote])

  const visiveis = useMemo(() => (pedidos ? filtrarExpedicao(pedidos, selecao) : []), [pedidos, selecao])
  const facetas = useMemo(() => (pedidos ? facetasDaExpedicao(pedidos, selecao) : []), [pedidos, selecao])
  const pecasVisiveis = useMemo(() => pecasDosPedidos(visiveis), [visiveis])

  // O cartão do último painel não está mais na tela e o foco se perdeu com ele: vai ao título. Se o cartão
  // continua na tela, quem devolve o foco é ele.
  useEffect(() => {
    if (recarregando || pedidos === null || ultimoCartao.current === null) return
    if (pecasVisiveis.some((p) => p.id === ultimoCartao.current)) return
    ultimoCartao.current = null
    const focado = document.activeElement
    if (focado === null || focado === document.body) titulo.current?.focus()
  }, [recarregando, pedidos, pecasVisiveis])

  const vazia = pedidos !== null && pedidos.length === 0
  const vaziaPeloFiltro = pedidos !== null && pedidos.length > 0 && visiveis.length === 0

  /** Mexer na seleção apaga a recusa e o aviso. O cartão fecha o próprio painel quando a marca dele muda. */
  function mudarLote(depois: LoteDaExpedicao) {
    setLote(depois)
    setErroDoLote(null)
    setAviso(null)
  }

  // A MESMA lista de marcadas alimenta a contagem da barra, o `invalido` e o corpo do envio: um lote parcial
  // (sem a Peça de campo ruim) nunca sai.
  const marcadas = marcadosDoLote(lote, todas)
  const invalido = camposInvalidos(marcadas)
  const idsVisiveis = new Set(pecasVisiveis.map((p) => p.id))
  const ocultas = marcadas.filter(({ peca }) => !idsVisiveis.has(peca.id)).length

  async function enviarLote() {
    if (invalido || enviandoLoteRef.current) return
    enviandoLoteRef.current = true
    setEnviandoLote(true)
    setErroDoLote(null)
    setAviso(null)
    try {
      await expedir(itensDoLote(marcadas))
      setLote({})
      atualizar()
    } catch (e) {
      setErroDoLote(mensagemDeErro(e, 'Não foi possível expedir.'))
      atualizar()
    } finally {
      enviandoLoteRef.current = false
      setEnviandoLote(false)
    }
  }

  return (
    <Pagina titulo={<span ref={titulo} tabIndex={-1}>Expedição</span>}>
      <p className="text-sm text-tinta-fraca">O que está no local de expedição, por prazo de entrega.</p>
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={erroDosSetores} />
      <BannerDeErro mensagem={aviso} />
      {/* A recusa do lote mora na barra. Se a recarga que veio com ela esvaziou a seleção, a barra sumiu, e a
          frase do servidor sobe para cá em vez de se perder. */}
      <BannerDeErro mensagem={marcadas.length === 0 ? erroDoLote : null} />
      {pedidos !== null && pedidos.length > 0 && (
        <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />
      )}
      {registrar && pecasVisiveis.some(podeEntrarNoLote) && (
        <Botao variante="secundario" onClick={() => mudarLote(marcarTodos(lote, pecasVisiveis))} className="self-start">
          {todosMarcados(lote, pecasVisiveis) ? 'Desmarcar todos' : 'Marcar todos'}
        </Botao>
      )}
      {pedidos === null && erro === null && <EstadoCarregando />}
      {vazia && <EstadoVazio titulo="Nada no local de expedição." />}
      {vaziaPeloFiltro && (
        <EstadoVazio
          titulo="Nenhuma Peça com esses filtros."
          acao={<Botao variante="secundario" onClick={limpar}>Limpar filtros</Botao>}
        />
      )}
      {visiveis.map((pedido) => (
        <section key={pedido.id} aria-labelledby={`pedido-${pedido.id}`} className="flex flex-col gap-3">
          <header className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <h2 id={`pedido-${pedido.id}`} className="text-lg font-medium text-tinta">
              {`${pedido.numero} · ${pedido.cliente}`}
            </h2>
            <span className="text-sm text-tinta-fraca">{`entrega ${formatarData(pedido.dataEntrega)}`}</span>
            {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
            {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
          </header>
          <ListaDeCadastro rotulo={`Peças do Pedido ${pedido.numero}`}>
            {pedido.pecas.map((peca) => (
              <CartaoDaPeca
                key={peca.id}
                peca={peca}
                pedidoNumero={pedido.numero}
                setores={setores}
                aoRegistrar={atualizar}
                recarregando={recarregando}
                aoAbrirPainel={() => { ultimoCartao.current = peca.id }}
                lote={registrar ? {
                  marcado: estaMarcado(lote, peca.id),
                  texto: estaMarcado(lote, peca.id) ? lote[peca.id] : null,
                  desabilitado: enviandoLote,
                  aoMarcar: (marcado) => mudarLote(alternar(lote, peca.id, marcado)),
                  aoDigitar: (texto) => setLote((atual) => digitar(atual, peca.id, texto)),
                } : undefined}
              />
            ))}
          </ListaDeCadastro>
        </section>
      ))}
      <RemessasRecentes aoEstornar={carregar} versao={versaoDasRemessas} />
      {registrar && marcadas.length > 0 && (
        <BarraDoLote
          rotulo="Expedir"
          rotuloCarregando="Expedindo…"
          ocultos={ocultas}
          invalido={invalido}
          enviando={enviandoLote}
          erro={erroDoLote}
          aoEnviar={enviarLote}
          aoLimpar={() => mudarLote({})}
        />
      )}
    </Pagina>
  )
}
