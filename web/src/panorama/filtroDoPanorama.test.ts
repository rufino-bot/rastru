import { describe, it, expect } from 'vitest'
import type { PecaDoPanoramaDto, SegmentoDto } from '../api/panorama'
import { CHAVES_DO_PANORAMA, facetasDoPanorama, filtrarPanorama } from './filtroDoPanorama'

interface Opcoes {
  pedidoId?: number; numero?: string; prazo?: string; atrasado?: boolean; pausa?: boolean
  codigo?: string; descricao?: string; itensEm?: [number, string][]; segmentos?: SegmentoDto[]
  exige?: boolean; vale?: boolean; podeMarcar?: boolean
}
const passo = (setorId: number, nome: string): SegmentoDto =>
  ({ tipo: 'Passo', setorId, setorNome: nome, ordem: 1, quantidade: 1, aguardandoColeta: 0 })

function peca(id: number, o: Opcoes = {}): PecaDoPanoramaDto {
  return {
    estruturaItemId: id, agrupamentoId: 1, quantidade: 1,
    pedido: {
      id: o.pedidoId ?? 1, numero: o.numero ?? '100', dataEntrega: o.prazo ?? '2026-11-10', atrasado: o.atrasado ?? false,
      retrabalho: false, pausa: o.pausa ? { desde: 'x', porUsuarioNome: 'u', motivo: null } : null,
    },
    componente: { id, codigo: o.codigo ?? 'C-1', descricao: o.descricao ?? 'Chapa', miniaturaVersao: null },
    itensEm: (o.itensEm ?? []).map(([setorId, nome]) => ({ setorId, nome })),
    distribuicao: o.segmentos ?? [],
    afericao: { exigeRelatorio: o.exige ?? false, situacao: 'SemRelatorio' },
    embalagem: { vale: o.vale ?? false, podeMarcar: o.podeMarcar ?? false, emProducao: 0, ultima: null },
  }
}
const ids = (l: PecaDoPanoramaDto[]) => l.map((p) => p.estruturaItemId)
type Facetas = ReturnType<typeof facetasDoPanorama>
const faceta = (f: Facetas, chave: string) => f.find((x) => x.chave === chave)!
const contagens = (f: Facetas, chave: string) =>
  Object.fromEntries(faceta(f, chave).opcoes.map((o) => [o.valor, o.contagem]))

const vazio = { selecao: {}, busca: '', de: '', ate: '' }

describe('CHAVES_DO_PANORAMA', () => {
  it('são as cinco facetas, na ordem', () => {
    expect([...CHAVES_DO_PANORAMA]).toEqual(['pedido', 'setor', 'situacao', 'relatorio', 'embalado'])
    expect(facetasDoPanorama([peca(1)], {}).map((f) => f.chave)).toEqual([...CHAVES_DO_PANORAMA])
  })
})

describe('facetas do Panorama', () => {
  it('pedido: id como valor e número como rótulo', () => {
    const f = facetasDoPanorama([peca(1, { pedidoId: 7, numero: '293' }), peca(2, { pedidoId: 7, numero: '293' })])
    expect(faceta(f, 'pedido').opcoes).toHaveLength(1)
    expect(faceta(f, 'pedido').opcoes[0]).toMatchObject({ valor: '7', rotulo: '293', contagem: 2 })
  })

  it('setor: passo e conserto da Peça mais os de Itens em, sem contar a Peça duas vezes', () => {
    const conserto: SegmentoDto =
      { tipo: 'Conserto', setorId: 4, setorNome: 'Solda', ordem: null, quantidade: 1, aguardandoColeta: 0 }
    const p = peca(1, { segmentos: [passo(3, 'Corte'), conserto], itensEm: [[4, 'Solda'], [5, 'Pintura']] })
    const f = facetasDoPanorama([p, peca(2, { itensEm: [[5, 'Pintura']] })])
    expect(contagens(f, 'setor')).toEqual({ 3: 1, 4: 1, 5: 2 })
    expect(faceta(f, 'setor').opcoes.map((o) => o.rotulo).sort()).toEqual(['Corte', 'Pintura', 'Solda'])
    expect(ids(filtrarPanorama([p, peca(2)], { ...vazio, selecao: { setor: ['5'] } }))).toEqual([1])
  })

  it('situacao: Atrasado ou No prazo, mais Pausado', () => {
    const lista = [peca(1, { atrasado: true }), peca(2), peca(3, { pausa: true }), peca(4, { atrasado: true, pausa: true })]
    const f = facetasDoPanorama(lista)
    expect(contagens(f, 'situacao')).toEqual({ Atrasado: 2, NoPrazo: 2, Pausado: 2 })
    expect(faceta(f, 'situacao').opcoes.map((o) => o.rotulo)).toEqual(['Atrasado', 'No prazo', 'Pausado'])
    expect(ids(filtrarPanorama(lista, { ...vazio, selecao: { situacao: ['Pausado'] } }))).toEqual([3, 4])
  })

  it('relatorio: ExigeRelatorio e SemRelatorio', () => {
    const lista = [peca(1, { exige: true }), peca(2), peca(3)]
    expect(contagens(facetasDoPanorama(lista), 'relatorio')).toEqual({ ExigeRelatorio: 1, SemRelatorio: 2 })
    expect(ids(filtrarPanorama(lista, { ...vazio, selecao: { relatorio: ['ExigeRelatorio'] } }))).toEqual([1])
  })

  it('embalado: Embalado, Pode embalar e Em produção', () => {
    const lista = [
      peca(1, { vale: true, podeMarcar: true }), // vale vence
      peca(2, { podeMarcar: true }),
      peca(3),
      peca(4, { vale: true }),
    ]
    const f = facetasDoPanorama(lista)
    expect(contagens(f, 'embalado')).toEqual({ Embalado: 2, PodeEmbalar: 1, EmProducao: 1 })
    expect(faceta(f, 'embalado').opcoes.map((o) => o.rotulo)).toEqual(['Embalado', 'Pode embalar', 'Em produção'])
    expect(ids(filtrarPanorama(lista, { ...vazio, selecao: { embalado: ['PodeEmbalar'] } }))).toEqual([2])
    expect(ids(filtrarPanorama(lista, { ...vazio, selecao: { embalado: ['EmProducao'] } }))).toEqual([3])
  })

  it('a contagem troca a seleção da própria faceta e mantém as outras', () => {
    const lista = [peca(1, { exige: true, atrasado: true }), peca(2, { exige: true }), peca(3, { atrasado: true })]
    const f = facetasDoPanorama(lista, { situacao: ['Atrasado'], relatorio: ['ExigeRelatorio'] })
    // situação: troca a própria por cada opção e mantém relatório = exige (Peças 1 e 2)
    expect(contagens(f, 'situacao')).toEqual({ Atrasado: 1, NoPrazo: 1 })
    // relatório: mantém situação = atrasado (Peças 1 e 3)
    expect(contagens(f, 'relatorio')).toEqual({ ExigeRelatorio: 1, SemRelatorio: 1 })
  })
})

describe('filtrarPanorama', () => {
  const lista = [
    peca(1, { numero: '293146', codigo: 'MT-100', descricao: 'Suporte de aço', prazo: '2026-11-05' }),
    peca(2, { numero: '555', codigo: 'XY-9', descricao: 'Tampa', prazo: '2026-11-10' }),
    peca(3, { numero: '777', codigo: 'ZZ-1', descricao: 'Eixo', prazo: '2026-11-15' }),
  ]

  it('busca sem caixa nem acento, em número, código e descrição', () => {
    expect(ids(filtrarPanorama(lista, { ...vazio, busca: 'ACO' }))).toEqual([1])
    expect(ids(filtrarPanorama(lista, { ...vazio, busca: '2931' }))).toEqual([1])
    expect(ids(filtrarPanorama(lista, { ...vazio, busca: 'xy-9' }))).toEqual([2])
    expect(ids(filtrarPanorama(lista, { ...vazio, busca: '  ' }))).toEqual([1, 2, 3])
  })

  it('intervalo inclusivo, com cada lado opcional', () => {
    expect(ids(filtrarPanorama(lista, { ...vazio, de: '2026-11-10', ate: '2026-11-15' }))).toEqual([2, 3])
    expect(ids(filtrarPanorama(lista, { ...vazio, de: '2026-11-10' }))).toEqual([2, 3])
    expect(ids(filtrarPanorama(lista, { ...vazio, ate: '2026-11-05' }))).toEqual([1])
  })

  it('de maior que ate: nada casa', () => {
    expect(filtrarPanorama(lista, { ...vazio, de: '2026-11-15', ate: '2026-11-05' })).toEqual([])
  })

  it('combina seleção e intervalo (E) e preserva a ordem', () => {
    const l = [
      peca(1, { atrasado: true, prazo: '2026-11-05' }),
      peca(2, { atrasado: true, prazo: '2026-12-05' }),
      peca(3, { prazo: '2026-11-05' }),
    ]
    expect(ids(filtrarPanorama(l, { selecao: { situacao: ['Atrasado'] }, busca: '', de: '', ate: '2026-11-30' }))).toEqual([1])
    expect(ids(filtrarPanorama(l, vazio))).toEqual([1, 2, 3])
  })
})
