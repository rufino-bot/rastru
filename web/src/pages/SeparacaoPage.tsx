import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { listarPendenciasDeSeparacao, separarEmLote, type PendenciaDeSeparacaoDto } from '../api/separacao'
import { formatarData } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { BarraDoLote } from '../execucao/BarraDoLote'
import { CHAVES_DA_SEPARACAO, facetasDaSeparacao, filtrarSeparacao } from '../separacao/filtroDaSeparacao'
import {
  alternar, camposDoNo, camposInvalidos, dicaDoCampo, digitar, estaMarcado, marcadosDoLote, marcarTodos, nosDoLote,
  reconciliar, SAIU_DO_LOTE, todosMarcados, type LoteDaSeparacao,
} from '../separacao/loteDaSeparacao'
import { PainelDeSeparacao } from '../separacao/PainelDeSeparacao'
import { usePermissoesDaSeparacao } from '../separacao/usePermissoesDaSeparacao'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'

/** "QD-01 — Quadro (PED-1)": o nó e o Pedido, porque o mesmo nó (o mesmo código) aparece em Pedidos diferentes. */
const nomeDoCartao = (item: PendenciaDeSeparacaoDto) => `${rotuloDoNo(item.no)} (${item.pedido.numero})`

/**
 * `/separacao` — a lista do Separador (spec da Fase 4, seção 6.1, D11): todo nó com material do
 * almoxarifado pendente, de Pedido aberto, na ordem da data de entrega. Pensada para o celular: um cartão
 * por nó, com o Pedido e o prazo, o destino (o Setor do primeiro passo do Roteiro, ou "sem Roteiro") e uma
 * linha por material pendente.
 *
 * A lista chega inteira e o filtro é no cliente, como a Fila e as Tarefas (`FiltroDeDemanda`, seleção na
 * URL). Ela não se atualiza sozinha: recarrega depois de cada registro, e no 409 do painel.
 *
 * "Separar" fica sob `usePermissoesDaSeparacao` (gating na ação; a leitura é de todos) e abre o
 * `PainelDeSeparacao` dentro do cartão escolhido, um por vez. Ao fechar, o foco volta ao "Separar" daquele
 * cartão depois da recarga; se o cartão saiu da lista (não falta mais nada), volta ao título.
 *
 * O lote, no molde da Fila e das Tarefas (`loteDaSeparacao`, `BarraDoLote`): a caixa marca o cartão
 * inteiro, e o cartão marcado mostra um campo por material pendente, preenchido com a pendência. Um
 * `POST /separacoes` leva todos os marcados, tudo ou nada. Marcar ou desmarcar fecha o painel individual
 * aberto, e o "Separar" de cada cartão continua. Sucesso limpa a seleção e recarrega; recusa recarrega e
 * mantém a seleção, com a frase do servidor na barra. A recarga que tira da lista um cartão marcado o tira
 * da seleção, com aviso, e o filtro não conta como sair: o marcado oculto vai no lote.
 */
export function SeparacaoPage() {
  const [itens, setItens] = useState<PendenciaDeSeparacaoDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [recarregando, setRecarregando] = useState(false)
  const sequencia = useRef(0)
  const { separar: podeSeparar } = usePermissoesDaSeparacao()
  const { selecao, mudarSelecao, limpar } = useSelecaoNaUrl(CHAVES_DA_SEPARACAO)

  // O nó cujo painel está aberto; um por vez.
  const [aberto, setAberto] = useState<number | null>(null)
  // O último nó aberto, para o foco voltar ao "Separar" dele quando o painel fechar.
  const ultimoAberto = useRef<number | null>(null)
  const botoes = useRef(new Map<number, HTMLButtonElement>())
  const titulo = useRef<HTMLSpanElement>(null)

  const [lote, setLote] = useState<LoteDaSeparacao>({})
  const [erroDoLote, setErroDoLote] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [enviandoLote, setEnviandoLote] = useState(false)
  // O toque no mesmo quadro, antes de o React redesenhar o botão desabilitado — mesmo padrão do lote da Fila.
  const enviandoLoteRef = useRef(false)

  // A resposta que chega depois de uma mais nova é descartada: duas recargas seguidas não podem
  // terminar com a lista velha na tela.
  const carregar = useCallback(async () => {
    const minha = ++sequencia.current
    setRecarregando(true)
    try {
      const lista = await listarPendenciasDeSeparacao()
      if (minha !== sequencia.current) return
      setItens(lista)
      setErro(null)
    } catch (e) {
      if (minha !== sequencia.current) return
      setErro(mensagemDeErro(e, 'Não foi possível carregar a lista de separação.'))
    } finally {
      if (minha === sequencia.current) setRecarregando(false)
    }
  }, [])

  useEffect(() => { carregar() }, [carregar])

  // A recarga tirou da lista um cartão marcado: ele sai da seleção, com aviso. O texto dos que ficam não muda.
  useEffect(() => {
    if (itens === null) return
    const { lote: depois, saiu } = reconciliar(lote, itens)
    if (!saiu) return
    setLote(depois)
    setAviso(SAIU_DO_LOTE)
  }, [itens, lote])

  useDevolverFoco(
    aberto !== null,
    () => (ultimoAberto.current !== null ? botoes.current.get(ultimoAberto.current) : undefined) ?? titulo.current,
    !recarregando,
  )

  function abrir(noId: number) {
    ultimoAberto.current = noId
    setAberto(noId)
  }

  // Fecha só se o painel deste nó ainda for o aberto: a resposta de um envio que chega depois de o
  // Separador abrir outro cartão não fecha o painel novo.
  function fechar(noId: number) {
    setAberto((atual) => (atual === noId ? null : atual))
  }

  /** Mexer na seleção fecha o painel individual aberto (desvio D7 do plano 2 dos filtros) e apaga a recusa e o aviso. */
  function mudarLote(depois: LoteDaSeparacao) {
    setLote(depois)
    setAberto(null)
    setErroDoLote(null)
    setAviso(null)
  }

  const visiveis = useMemo(() => (itens ? filtrarSeparacao(itens, selecao) : []), [itens, selecao])
  const facetas = useMemo(() => (itens ? facetasDaSeparacao(itens, selecao) : []), [itens, selecao])
  const vazia = itens !== null && itens.length === 0
  const vaziaPeloFiltro = itens !== null && itens.length > 0 && visiveis.length === 0

  // A MESMA lista de marcados alimenta a contagem da barra, o `invalido` e o corpo do envio: um lote parcial
  // (sem o cartão que não tem nada a mandar) nunca sai.
  const marcados = marcadosDoLote(lote, itens ?? [])
  const invalido = marcados.some(({ campos }) => camposInvalidos(campos))
  const idsVisiveis = new Set(visiveis.map((v) => v.no.id))
  const ocultos = marcados.filter(({ item }) => !idsVisiveis.has(item.no.id)).length

  async function enviarLote() {
    if (marcados.length === 0 || invalido || enviandoLoteRef.current) return
    enviandoLoteRef.current = true
    setEnviandoLote(true)
    setErroDoLote(null)
    setAviso(null)
    try {
      await separarEmLote(nosDoLote(marcados))
      setLote({})
      await carregar()
    } catch (e) {
      setErroDoLote(mensagemDeErro(e, 'Não foi possível registrar a separação.'))
      await carregar()
    } finally {
      enviandoLoteRef.current = false
      setEnviandoLote(false)
    }
  }

  return (
    <Pagina titulo={<span ref={titulo} tabIndex={-1}>Separação</span>}>
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {/* A recusa do lote mora na barra. Se a recarga que veio com ela esvaziou a seleção, a barra sumiu, e a
          frase do servidor sobe para cá em vez de se perder. */}
      <BannerDeErro mensagem={marcados.length === 0 ? erroDoLote : null} />
      {itens !== null && itens.length > 0 && (
        <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />
      )}
      {podeSeparar && visiveis.length > 0 && (
        <Botao variante="secundario" onClick={() => mudarLote(marcarTodos(lote, visiveis))} className="self-start">
          {todosMarcados(lote, visiveis) ? 'Desmarcar todos' : 'Marcar todos'}
        </Botao>
      )}
      {itens === null && erro === null && <EstadoCarregando />}
      {vazia && <EstadoVazio titulo="Nada a separar." />}
      {vaziaPeloFiltro && (
        <EstadoVazio
          titulo="Nenhum item com esses filtros."
          acao={<Botao variante="secundario" onClick={limpar}>Limpar filtros</Botao>}
        />
      )}
      {visiveis.length > 0 && (
        <ListaDeCadastro rotulo="Itens a separar">
          {visiveis.map((item) => {
            const marcado = estaMarcado(lote, item.no.id)
            return (
              <CartaoDeSeparacao
                key={item.no.id}
                item={item}
                acao={podeSeparar && (
                  <>
                    <label className="flex items-center gap-2 text-sm text-tinta">
                      <input
                        type="checkbox"
                        checked={marcado}
                        onChange={(e) => mudarLote(alternar(lote, item.no.id, e.target.checked))}
                        aria-label={`Marcar ${nomeDoCartao(item)}`}
                        className="size-5 accent-acao"
                      />
                      Marcar
                    </label>
                    {aberto !== item.no.id && (
                      <Botao
                        ref={(el) => {
                          if (el) botoes.current.set(item.no.id, el)
                          else botoes.current.delete(item.no.id)
                        }}
                        variante="secundario"
                        aria-label={`Separar ${rotuloDoNo(item.no)}`}
                        onClick={() => abrir(item.no.id)}
                      >
                        Separar
                      </Botao>
                    )}
                  </>
                )}
                painel={podeSeparar && (marcado || aberto === item.no.id) && (
                  <>
                    {marcado && (
                      <CamposDoLote
                        item={item}
                        textos={lote[item.no.id]}
                        aoDigitar={(materialId, texto) => setLote((atual) => digitar(atual, item.no.id, materialId, texto))}
                        desabilitado={enviandoLote}
                      />
                    )}
                    {aberto === item.no.id && (
                      <PainelDeSeparacao pendencia={item} aoFechar={() => fechar(item.no.id)} aoSalvar={carregar} />
                    )}
                  </>
                )}
              />
            )
          })}
        </ListaDeCadastro>
      )}
      {podeSeparar && marcados.length > 0 && (
        <BarraDoLote
          rotulo={`Separar ${marcados.length} ${marcados.length === 1 ? 'item' : 'itens'}`}
          rotuloCarregando="Registrando…"
          ocultos={ocultos}
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

function CartaoDeSeparacao({ item, acao, painel }: {
  item: PendenciaDeSeparacaoDto
  acao: ReactNode
  painel: ReactNode
}) {
  const { pedido, destino } = item
  return (
    <ItemComAcao acao={acao} painel={painel}>
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        {rotuloDoNo(item.no)}
        {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
        {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
      </span>
      <span className="text-sm text-tinta-fraca">
        {`${pedido.numero} · ${pedido.cliente} · entrega ${formatarData(pedido.dataEntrega)}`}
      </span>
      <span className="text-sm text-tinta">{destino ? `→ ${destino.nome}` : 'sem Roteiro'}</span>
      <ul aria-label="Materiais pendentes" className="flex flex-col gap-0.5">
        {item.materiais.map((m) => {
          const un = m.unidadeMedida
          return (
            <li key={m.materialId} className="text-sm text-tinta">
              {`${m.codigo} ${m.descricao} · falta ${formatarQuantidade(m.pendencia)} ${un} `
                + `(${formatarQuantidade(m.separado)} de ${formatarQuantidade(m.planejado)})`}
            </li>
          )
        })}
      </ul>
    </ItemComAcao>
  )
}

/**
 * Os campos do cartão marcado, um por material pendente, com a mesma leitura e a mesma dica do painel
 * individual. O rótulo visível é o do material; o nome acessível acrescenta o cartão (nó e Pedido), para
 * ser único na tela com vários cartões marcados, e começa pelo rótulo visível.
 */
function CamposDoLote({ item, textos, aoDigitar, desabilitado }: {
  item: PendenciaDeSeparacaoDto
  textos: Record<number, string>
  aoDigitar: (materialId: number, texto: string) => void
  desabilitado: boolean
}) {
  return (
    <div className="flex flex-col gap-3 border-t border-borda pt-3">
      {camposDoNo(item, textos).map(({ material: m, texto, leitura }) => {
        const rotulo = `${m.codigo} ${m.descricao} (${m.unidadeMedida})`
        return (
          <Campo key={m.materialId} rotulo={rotulo} dica={dicaDoCampo(m, leitura)}>
            {(id, idDaDica) => (
              <input
                id={id}
                type="text"
                inputMode="decimal"
                autoComplete="off"
                value={texto}
                onChange={(e) => aoDigitar(m.materialId, e.target.value)}
                aria-label={`${rotulo} — ${nomeDoCartao(item)}`}
                aria-describedby={idDaDica}
                aria-invalid={leitura.tipo === 'erro'}
                disabled={desabilitado}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        )
      })}
    </div>
  )
}
