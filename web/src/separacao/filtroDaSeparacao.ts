import type { PendenciaDeSeparacaoDto } from '../api/separacao'
import { casaComFiltro, type Faceta, type Selecao } from '../components/FiltroDeDemanda'

/** As chaves da query string (`?destino=4&pedido=42&material=3,5`), que também são as chaves das facetas. */
export const CHAVES_DA_SEPARACAO = ['destino', 'pedido', 'material'] as const

/** O valor da opção do nó sem Roteiro na faceta "Setor de destino": não há `setorId` para ele. */
export const DESTINO_SEM_ROTEIRO = 'sem-roteiro'

const valorDoDestino = (item: PendenciaDeSeparacaoDto) =>
  item.destino === null ? DESTINO_SEM_ROTEIRO : String(item.destino.setorId)

function valoresDoItem(item: PendenciaDeSeparacaoDto): Record<string, string[]> {
  return {
    destino: [valorDoDestino(item)],
    pedido: [String(item.pedido.id)],
    material: item.materiais.map((m) => String(m.materialId)),
  }
}

const casa = (item: PendenciaDeSeparacaoDto, selecao: Selecao) => casaComFiltro(valoresDoItem(item), selecao)

const porTexto = (a: string, b: string) => a.localeCompare(b, 'pt-BR', { numeric: true })

/**
 * As facetas da lista do Separador: Setor de destino, Pedido e Material, cada item contado uma vez por
 * opção (o nó com dois materiais pendentes não conta duas vezes). Mesmo critério da Fila e das Tarefas: as
 * opções são as da lista inteira, para não sumirem enquanto se marca, e a contagem de uma opção troca a
 * seleção DA PRÓPRIA faceta por ela e mantém as outras.
 *
 * O nó sem Roteiro entra na faceta de destino com o valor `sem-roteiro`, depois dos Setores.
 */
export function facetasDaSeparacao(itens: PendenciaDeSeparacaoDto[], selecao: Selecao): Faceta[] {
  const destinos = new Map<number, string>()
  let haSemRoteiro = false
  const pedidos = new Map<number, { numero: string; cliente: string }>()
  const materiais = new Map<number, { codigo: string; descricao: string }>()
  for (const item of itens) {
    if (item.destino === null) haSemRoteiro = true
    else destinos.set(item.destino.setorId, item.destino.nome)
    pedidos.set(item.pedido.id, { numero: item.pedido.numero, cliente: item.pedido.cliente })
    for (const m of item.materiais) materiais.set(m.materialId, { codigo: m.codigo, descricao: m.descricao })
  }

  const contar = (chave: string, valor: string) =>
    itens.filter((item) => casa(item, { ...selecao, [chave]: [valor] })).length

  const opcoesDeDestino = [...destinos]
    .sort(([, a], [, b]) => porTexto(a, b))
    .map(([id, nome]) => ({ valor: String(id), rotulo: nome, contagem: contar('destino', String(id)) }))
  if (haSemRoteiro) {
    opcoesDeDestino.push({ valor: DESTINO_SEM_ROTEIRO, rotulo: 'sem Roteiro', contagem: contar('destino', DESTINO_SEM_ROTEIRO) })
  }

  return [
    { chave: 'destino', titulo: 'Setor de destino', opcoes: opcoesDeDestino },
    {
      chave: 'pedido',
      titulo: 'Pedido',
      opcoes: [...pedidos]
        .sort(([, a], [, b]) => porTexto(a.numero, b.numero))
        .map(([id, p]) => ({
          valor: String(id),
          rotulo: `${p.numero} · ${p.cliente}`,
          rotuloCurto: `Pedido ${p.numero}`,
          contagem: contar('pedido', String(id)),
        })),
    },
    {
      chave: 'material',
      titulo: 'Material',
      opcoes: [...materiais]
        .sort(([, a], [, b]) => porTexto(a.descricao, b.descricao) || porTexto(a.codigo, b.codigo))
        .map(([id, m]) => ({
          valor: String(id), rotulo: m.descricao, detalhe: m.codigo, contagem: contar('material', String(id)),
        })),
    },
  ]
}

/** Os itens que sobram sob a seleção, na ordem em que o servidor os mandou (data de entrega, Pedido, nó). */
export function filtrarSeparacao(itens: PendenciaDeSeparacaoDto[], selecao: Selecao): PendenciaDeSeparacaoDto[] {
  return itens.filter((item) => casa(item, selecao))
}
