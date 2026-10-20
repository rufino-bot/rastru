import { describe, it, expect } from 'vitest'
import { CHAVES_DA_SEPARACAO, facetasDaSeparacao, filtrarSeparacao } from './filtroDaSeparacao'
import type { MaterialPendente, PendenciaDeSeparacaoDto } from '../api/separacao'
import type { Faceta } from '../components/FiltroDeDemanda'

const PARAFUSO: MaterialPendente = {
  materialId: 7, codigo: 'PAR-M8', descricao: 'Parafuso M8', unidadeMedida: 'UN', planejado: 10, separado: 8, pendencia: 2,
}
const ELETRODO: MaterialPendente = {
  materialId: 9, codigo: 'ELE-01', descricao: 'Eletrodo 2,5 mm', unidadeMedida: 'KG', planejado: 1, separado: 0.5, pendencia: 0.5,
}

function pendencia(
  id: number,
  pedido: { id: number; numero: string; cliente: string },
  destino: PendenciaDeSeparacaoDto['destino'],
  materiais: MaterialPendente[],
): PendenciaDeSeparacaoDto {
  return {
    no: { id, descricao: `Nó ${id}`, codigoDoComponente: null, agrupamentoId: 1, agrupamentoCodigo: 'AGR-1' },
    pedido: { ...pedido, dataEntrega: '2026-10-22', atrasado: false, pausa: null },
    destino,
    materiais,
  }
}

const PED_1 = { id: 1, numero: 'PED-2026-01', cliente: 'Metalúrgica Alfa' }
const PED_2 = { id: 2, numero: 'PED-2026-02', cliente: 'Beta Máquinas' }
const SOLDA = { setorId: 4, nome: 'Solda' }
const EXPEDICAO = { setorId: 8, nome: 'Expedição' }

// Kit do Pedido 1 na Solda (parafuso), Peça do Pedido 1 na Expedição (parafuso e eletrodo), nó sem Roteiro do
// Pedido 2 (eletrodo) e outro do Pedido 2 na Solda (parafuso).
const KIT = pendencia(11, PED_1, SOLDA, [PARAFUSO])
const PECA = pendencia(12, PED_1, EXPEDICAO, [PARAFUSO, ELETRODO])
const SEM_ROTEIRO = pendencia(21, PED_2, null, [ELETRODO])
const OUTRO_KIT = pendencia(22, PED_2, SOLDA, [PARAFUSO])
const TODOS = [KIT, PECA, SEM_ROTEIRO, OUTRO_KIT]

function faceta(facetas: Faceta[], chave: string): Faceta {
  const achada = facetas.find((f) => f.chave === chave)
  if (!achada) throw new Error(`faceta ${chave} ausente`)
  return achada
}

const contagens = (f: Faceta) => Object.fromEntries(f.opcoes.map((o) => [o.valor, o.contagem]))
const ids = (itens: PendenciaDeSeparacaoDto[]) => itens.map((i) => i.no.id)

describe('CHAVES_DA_SEPARACAO', () => {
  it('são as três facetas, com as chaves que vão para a URL', () => {
    expect([...CHAVES_DA_SEPARACAO]).toEqual(['destino', 'pedido', 'material'])
  })
})

describe('facetasDaSeparacao', () => {
  it('as facetas são Setor de destino, Pedido e Material, com contagem por item', () => {
    const facetas = facetasDaSeparacao(TODOS, {})

    expect(facetas.map((f) => [f.chave, f.titulo])).toEqual([
      ['destino', 'Setor de destino'], ['pedido', 'Pedido'], ['material', 'Material'],
    ])
    expect(faceta(facetas, 'destino').opcoes).toEqual([
      { valor: '8', rotulo: 'Expedição', contagem: 1 },
      { valor: '4', rotulo: 'Solda', contagem: 2 },
      { valor: 'sem-roteiro', rotulo: 'sem Roteiro', contagem: 1 },
    ])
    expect(faceta(facetas, 'pedido').opcoes).toEqual([
      { valor: '1', rotulo: 'PED-2026-01 · Metalúrgica Alfa', rotuloCurto: 'Pedido PED-2026-01', contagem: 2 },
      { valor: '2', rotulo: 'PED-2026-02 · Beta Máquinas', rotuloCurto: 'Pedido PED-2026-02', contagem: 2 },
    ])
    // A Peça tem os dois materiais e conta UMA vez em cada: a contagem é de itens, não de linhas.
    expect(faceta(facetas, 'material').opcoes).toEqual([
      { valor: '9', rotulo: 'Eletrodo 2,5 mm', detalhe: 'ELE-01', contagem: 2 },
      { valor: '7', rotulo: 'Parafuso M8', detalhe: 'PAR-M8', contagem: 3 },
    ])
  })

  it('a contagem de uma faceta respeita a seleção das outras, e a lista de opções não encolhe', () => {
    const facetas = facetasDaSeparacao(TODOS, { pedido: ['2'] })

    expect(contagens(faceta(facetas, 'destino'))).toEqual({ 4: 1, 8: 0, 'sem-roteiro': 1 })
    expect(contagens(faceta(facetas, 'material'))).toEqual({ 7: 1, 9: 1 })
    // A da própria faceta troca a seleção dela pela opção: o Pedido 1 continua contando os seus.
    expect(contagens(faceta(facetas, 'pedido'))).toEqual({ 1: 2, 2: 2 })
  })

  it('o nó sem Roteiro casa com a opção "sem Roteiro" (valor sem-roteiro)', () => {
    expect(ids(filtrarSeparacao(TODOS, { destino: ['sem-roteiro'] }))).toEqual([21])
    expect(ids(filtrarSeparacao(TODOS, { destino: ['4'] }))).toEqual([11, 22])
    // Sem nó sem Roteiro na lista, a opção não aparece.
    expect(faceta(facetasDaSeparacao([KIT, PECA], {}), 'destino').opcoes.map((o) => o.valor)).toEqual(['8', '4'])
  })
})

describe('filtrarSeparacao', () => {
  it('OU dentro da faceta, E entre facetas, e faceta vazia não restringe', () => {
    expect(ids(filtrarSeparacao(TODOS, { destino: ['4', '8'] }))).toEqual([11, 12, 22])
    expect(ids(filtrarSeparacao(TODOS, { destino: ['4'], pedido: ['2'] }))).toEqual([22])
    expect(ids(filtrarSeparacao(TODOS, { material: ['9'], pedido: ['1'] }))).toEqual([12])
    expect(ids(filtrarSeparacao(TODOS, { material: [] }))).toEqual([11, 12, 21, 22])
    expect(filtrarSeparacao(TODOS, {})).toEqual(TODOS)
  })
})
