import type { PecaDoPanoramaDto } from '../api/panorama'

export type ColunaDoPanorama = 'pedido' | 'codigo' | 'descricao' | 'qtd' | 'prazo' | 'parado'

/** `null` é a ordem original, a do servidor. */
export type OrdemDoPanorama = { coluna: ColunaDoPanorama; direcao: 'asc' | 'desc' } | null

const COLUNAS: readonly ColunaDoPanorama[] = ['pedido', 'codigo', 'descricao', 'qtd', 'prazo', 'parado']

const comparar = (coluna: ColunaDoPanorama, a: PecaDoPanoramaDto, b: PecaDoPanoramaDto): number => {
  switch (coluna) {
    case 'pedido': return a.pedido.numero.localeCompare(b.pedido.numero, 'pt-BR', { numeric: true })
    case 'codigo': return a.componente.codigo.localeCompare(b.componente.codigo, 'pt-BR', { numeric: true })
    case 'descricao': return a.componente.descricao.localeCompare(b.componente.descricao, 'pt-BR')
    case 'qtd': return a.quantidade - b.quantidade
    case 'parado': return Date.parse(a.paradoDesde ?? '') - Date.parse(b.paradoDesde ?? '')
    case 'prazo': return a.pedido.dataEntrega < b.pedido.dataEntrega ? -1 : a.pedido.dataEntrega > b.pedido.dataEntrega ? 1 : 0
  }
}

/**
 * Ordena por uma coluna só. O empate cai na posição original, nos dois sentidos: o desempate é pelo índice de
 * entrada, não pelo reverso da ordem. A lista de entrada não é alterada.
 */
export function ordenarPanorama(pecas: PecaDoPanoramaDto[], ordem: OrdemDoPanorama): PecaDoPanoramaDto[] {
  if (ordem === null) return pecas
  const sinal = ordem.direcao === 'asc' ? 1 : -1
  // Na coluna "Parado há" quem não tem `paradoDesde` vai para o fim nos dois sentidos, e crescente é o mais antigo primeiro.
  const semParado = (p: PecaDoPanoramaDto) => ordem.coluna === 'parado' && !p.paradoDesde
  return pecas
    .map((peca, indice) => ({ peca, indice }))
    .sort((a, b) => {
      const ausenteA = semParado(a.peca)
      const ausenteB = semParado(b.peca)
      if (ausenteA !== ausenteB) return ausenteA ? 1 : -1
      if (ausenteA) return a.indice - b.indice
      return sinal * comparar(ordem.coluna, a.peca, b.peca) || a.indice - b.indice
    })
    .map(({ peca }) => peca)
}

/** Lê `prazo-asc`; o que não for uma coluna e uma direção conhecidas vira a ordem original. */
export function ordemDaUrl(bruto: string | null): OrdemDoPanorama {
  if (bruto === null) return null
  const partes = bruto.split('-')
  if (partes.length !== 2) return null
  const [coluna, direcao] = partes
  if (!COLUNAS.includes(coluna as ColunaDoPanorama)) return null
  if (direcao !== 'asc' && direcao !== 'desc') return null
  return { coluna: coluna as ColunaDoPanorama, direcao }
}

/** A ordem original não vai à URL. */
export function ordemParaUrl(ordem: OrdemDoPanorama): string | null {
  return ordem === null ? null : `${ordem.coluna}-${ordem.direcao}`
}
