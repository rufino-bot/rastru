import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { listarPendenciasDeSeparacao, type PendenciaDeSeparacaoDto } from '../api/separacao'
import { formatarData } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { CHAVES_DA_SEPARACAO, facetasDaSeparacao, filtrarSeparacao } from '../separacao/filtroDaSeparacao'
import { PainelDeSeparacao } from '../separacao/PainelDeSeparacao'
import { usePermissoesDaSeparacao } from '../separacao/usePermissoesDaSeparacao'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'

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

  const visiveis = useMemo(() => (itens ? filtrarSeparacao(itens, selecao) : []), [itens, selecao])
  const facetas = useMemo(() => (itens ? facetasDaSeparacao(itens, selecao) : []), [itens, selecao])
  const vazia = itens !== null && itens.length === 0
  const vaziaPeloFiltro = itens !== null && itens.length > 0 && visiveis.length === 0

  return (
    <Pagina titulo={<span ref={titulo} tabIndex={-1}>Separação</span>}>
      <BannerDeErro mensagem={erro} />
      {itens !== null && itens.length > 0 && (
        <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />
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
          {visiveis.map((item) => (
            <CartaoDeSeparacao
              key={item.no.id}
              item={item}
              acao={podeSeparar && aberto !== item.no.id && (
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
              painel={podeSeparar && aberto === item.no.id && (
                <PainelDeSeparacao pendencia={item} aoFechar={() => fechar(item.no.id)} aoSalvar={carregar} />
              )}
            />
          ))}
        </ListaDeCadastro>
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
