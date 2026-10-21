import type { PecaNaExpedicaoDto, PedidoNaExpedicaoDto, SituacaoDaAfericao } from '../api/expedicao'
import { casaComFiltro, type Faceta, type Selecao } from '../components/FiltroDeDemanda'

/** As chaves da query string (`?pedido=2&afericao=Pendente`), que também são as chaves das facetas. */
export const CHAVES_DA_EXPEDICAO = ['pedido', 'afericao'] as const

/**
 * Os textos da situação da aferição, os mesmos das pílulas do cartão da Peça (seção 6.5 da spec da Fase 5A).
 * Na ordem da tabela 3.2, que é também a ordem das opções da faceta.
 */
export const ROTULOS_DA_AFERICAO: Record<SituacaoDaAfericao, string> = {
  SemRelatorio: 'Sem relatório',
  Pendente: 'Aferição pendente',
  Conforme: 'Conforme',
  NaoConforme: 'Não conforme',
}

const ORDEM_DA_AFERICAO = Object.keys(ROTULOS_DA_AFERICAO) as SituacaoDaAfericao[]

/** A Peça filtrada junto do Pedido dela: a faceta de Pedido é do Pedido, a de Aferição é da Peça. */
const valoresDaPeca = (pedido: PedidoNaExpedicaoDto, peca: PecaNaExpedicaoDto): Record<string, string[]> => ({
  pedido: [String(pedido.id)],
  afericao: [peca.situacao],
})

const casa = (pedido: PedidoNaExpedicaoDto, peca: PecaNaExpedicaoDto, selecao: Selecao) =>
  casaComFiltro(valoresDaPeca(pedido, peca), selecao)

const porTexto = (a: string, b: string) => a.localeCompare(b, 'pt-BR', { numeric: true })

/**
 * As facetas da expedição: Pedido e Aferição, cada Peça contada uma vez. Mesmo critério da Fila, das Tarefas e
 * da Separação: as opções são as da lista inteira, para não sumirem enquanto se marca, e a contagem de uma opção
 * troca a seleção DA PRÓPRIA faceta por ela e mantém a da outra. Sem `selecao`, a contagem é a da lista inteira.
 */
export function facetasDaExpedicao(pedidos: PedidoNaExpedicaoDto[], selecao: Selecao = {}): Faceta[] {
  const situacoes = new Set<SituacaoDaAfericao>()
  for (const pedido of pedidos) for (const peca of pedido.pecas) situacoes.add(peca.situacao)

  const contar = (chave: string, valor: string) => pedidos.reduce(
    (total, pedido) => total + pedido.pecas.filter((peca) => casa(pedido, peca, { ...selecao, [chave]: [valor] })).length,
    0,
  )

  return [
    {
      chave: 'pedido',
      titulo: 'Pedido',
      opcoes: [...pedidos]
        .sort((a, b) => porTexto(a.numero, b.numero))
        .map((p) => ({
          valor: String(p.id),
          rotulo: `${p.numero} · ${p.cliente}`,
          rotuloCurto: `Pedido ${p.numero}`,
          contagem: contar('pedido', String(p.id)),
        })),
    },
    {
      chave: 'afericao',
      titulo: 'Aferição',
      opcoes: ORDEM_DA_AFERICAO
        .filter((s) => situacoes.has(s))
        .map((s) => ({ valor: s, rotulo: ROTULOS_DA_AFERICAO[s], contagem: contar('afericao', s) })),
    },
  ]
}

/**
 * Os Pedidos que sobram sob a seleção, cada um só com as Peças que casam; o Pedido sem nenhuma sai. A ordem é a
 * que o servidor mandou (prazo de entrega), dos Pedidos e das Peças. A lista de entrada não é alterada.
 */
export function filtrarExpedicao(pedidos: PedidoNaExpedicaoDto[], selecao: Selecao): PedidoNaExpedicaoDto[] {
  return pedidos.flatMap((pedido) => {
    const pecas = pedido.pecas.filter((peca) => casa(pedido, peca, selecao))
    if (pecas.length === 0) return []
    return [pecas.length === pedido.pecas.length ? pedido : { ...pedido, pecas }]
  })
}
