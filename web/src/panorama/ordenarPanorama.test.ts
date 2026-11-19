import { describe, it, expect } from 'vitest'
import type { PecaDoPanoramaDto } from '../api/panorama'
import { ordenarPanorama, ordemDaUrl, ordemParaUrl, type ColunaDoPanorama } from './ordenarPanorama'

function peca(
  id: number,
  o: { os?: string; codigo?: string; descricao?: string; qtd?: number; prazo?: string; parado?: string | null } = {},
): PecaDoPanoramaDto {
  return {
    estruturaItemId: id, agrupamentoId: 1, quantidade: o.qtd ?? 1, itensEm: [], distribuicao: [],
    pedido: {
      id: 1, numero: o.os ?? '1', dataEntrega: o.prazo ?? '2026-11-01', atrasado: false, retrabalho: false, pausa: null,
    },
    componente: { id, codigo: o.codigo ?? 'A', descricao: o.descricao ?? 'a', miniaturaVersao: null },
    afericao: { exigeRelatorio: false, situacao: 'SemRelatorio' },
    embalagem: { vale: false, podeMarcar: false, emProducao: 0, ultima: null },
    paradoDesde: o.parado ?? null, complicacoes: [],
  }
}
const ids = (l: PecaDoPanoramaDto[]) => l.map((p) => p.estruturaItemId)

describe('ordenarPanorama — parado há', () => {
  const antigo = '2026-11-01T08:00:00-03:00'
  const meio = '2026-11-10T08:00:00-03:00'
  const novo = '2026-11-18T08:00:00-03:00'

  it('parado_crescente_poe_o_mais_antigo_primeiro', () => {
    const lista = [peca(1, { parado: meio }), peca(2, { parado: novo }), peca(3, { parado: antigo })]
    expect(ids(ordenarPanorama(lista, { coluna: 'parado', direcao: 'asc' }))).toEqual([3, 1, 2])
    expect(ids(ordenarPanorama(lista, { coluna: 'parado', direcao: 'desc' }))).toEqual([2, 1, 3])
  })

  it('sem_parado_vai_para_o_fim_nos_dois_sentidos', () => {
    const lista = [peca(1), peca(2, { parado: novo }), peca(3, { parado: antigo }), peca(4)]
    expect(ids(ordenarPanorama(lista, { coluna: 'parado', direcao: 'asc' }))).toEqual([3, 2, 1, 4])
    expect(ids(ordenarPanorama(lista, { coluna: 'parado', direcao: 'desc' }))).toEqual([2, 3, 1, 4])
  })

  it('a URL aceita parado-asc e parado-desc', () => {
    expect(ordemDaUrl('parado-asc')).toEqual({ coluna: 'parado', direcao: 'asc' })
    expect(ordemParaUrl({ coluna: 'parado', direcao: 'desc' })).toBe('parado-desc')
  })
})

describe('ordenarPanorama', () => {
  it('null devolve a lista como veio', () => {
    const lista = [peca(3), peca(1), peca(2)]
    expect(ids(ordenarPanorama(lista, null))).toEqual([3, 1, 2])
  })

  const casos: [ColunaDoPanorama, PecaDoPanoramaDto[], number[]][] = [
    ['pedido', [peca(1, { os: '10' }), peca(2, { os: '9' }), peca(3, { os: '100' })], [2, 1, 3]],
    ['codigo', [peca(1, { codigo: 'B-10' }), peca(2, { codigo: 'B-9' }), peca(3, { codigo: 'A-1' })], [3, 2, 1]],
    ['descricao', [peca(1, { descricao: 'Zinco' }), peca(2, { descricao: 'árvore' }), peca(3, { descricao: 'Barra' })], [2, 3, 1]],
    ['qtd', [peca(1, { qtd: 10 }), peca(2, { qtd: 2 }), peca(3, { qtd: 5 })], [2, 3, 1]],
    ['prazo', [peca(1, { prazo: '2026-12-01' }), peca(2, { prazo: '2026-11-03' }), peca(3, { prazo: '2026-11-20' })], [2, 3, 1]],
  ]

  it.each(casos)('coluna %s: asc e desc', (coluna, lista, asc) => {
    expect(ids(ordenarPanorama(lista, { coluna, direcao: 'asc' }))).toEqual(asc)
    expect(ids(ordenarPanorama(lista, { coluna, direcao: 'desc' }))).toEqual([...asc].reverse())
  })

  it('empate preserva a ordem original, nos dois sentidos', () => {
    const lista = [peca(5, { qtd: 2 }), peca(4, { qtd: 1 }), peca(3, { qtd: 2 }), peca(2, { qtd: 1 })]
    expect(ids(ordenarPanorama(lista, { coluna: 'qtd', direcao: 'asc' }))).toEqual([4, 2, 5, 3])
    expect(ids(ordenarPanorama(lista, { coluna: 'qtd', direcao: 'desc' }))).toEqual([5, 3, 4, 2])
  })

  it('não altera a lista de entrada', () => {
    const lista = [peca(2, { qtd: 2 }), peca(1, { qtd: 1 })]
    ordenarPanorama(lista, { coluna: 'qtd', direcao: 'asc' })
    expect(ids(lista)).toEqual([2, 1])
  })
})

describe('ordem na URL', () => {
  it('ida e volta', () => {
    expect(ordemParaUrl({ coluna: 'prazo', direcao: 'asc' })).toBe('prazo-asc')
    expect(ordemParaUrl({ coluna: 'qtd', direcao: 'desc' })).toBe('qtd-desc')
    expect(ordemDaUrl('prazo-asc')).toEqual({ coluna: 'prazo', direcao: 'asc' })
    expect(ordemDaUrl('descricao-desc')).toEqual({ coluna: 'descricao', direcao: 'desc' })
  })

  it('a ordem original não vai à URL', () => {
    expect(ordemParaUrl(null)).toBeNull()
    expect(ordemDaUrl(null)).toBeNull()
  })

  it.each(['lixo', 'prazo', 'prazo-up', 'x-asc', '', 'prazo-asc-asc'])('valor desconhecido %j vira null', (bruto) => {
    expect(ordemDaUrl(bruto)).toBeNull()
  })
})
