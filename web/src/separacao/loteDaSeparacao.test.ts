import { describe, it, expect } from 'vitest'
import type { MaterialPendente, PendenciaDeSeparacaoDto } from '../api/separacao'
import {
  alternar, camposDoNo, camposInvalidos, digitar, lerCampo, marcadosDoLote, marcarTodos, nosDoLote, reconciliar,
  todosMarcados, type LoteDaSeparacao,
} from './loteDaSeparacao'

const PARAFUSO: MaterialPendente = {
  materialId: 7, codigo: 'PAR-M8', descricao: 'Parafuso M8', unidadeMedida: 'UN', planejado: 10, separado: 8, pendencia: 2,
}
const ELETRODO: MaterialPendente = {
  materialId: 9, codigo: 'ELE-01', descricao: 'Eletrodo', unidadeMedida: 'KG', planejado: 1, separado: 0.5, pendencia: 0.5,
}

function pendencia(id: number, materiais: MaterialPendente[]): PendenciaDeSeparacaoDto {
  return {
    no: { id, descricao: `Nó ${id}`, codigoDoComponente: null, agrupamentoId: 1, agrupamentoCodigo: 'AGR-1' },
    pedido: { id: 1, numero: 'PED-1', cliente: 'Cliente', dataEntrega: '2026-10-20', atrasado: false, pausa: null },
    destino: null,
    materiais,
  }
}

const QUADRO = pendencia(30, [PARAFUSO, ELETRODO])
const TAMPA = pendencia(40, [PARAFUSO])
const CHAPA = pendencia(50, [ELETRODO])

describe('lerCampo', () => {
  it('vazio e zero escrito de qualquer jeito não vão; número vai; texto que não é número é erro', () => {
    for (const texto of ['', '  ', '0', '0,0', '00.00']) expect(lerCampo(texto), texto).toEqual({ tipo: 'nao-vai' })
    expect(lerCampo('0,5')).toEqual({ tipo: 'vai', valor: 0.5 })
    // Acima do planejado não é erro (D9): o campo não tem máximo.
    expect(lerCampo('999')).toEqual({ tipo: 'vai', valor: 999 })
    expect(lerCampo('abc').tipo).toBe('erro')
  })
})

describe('campos de um nó', () => {
  it('o campo intocado mostra a pendência; o mexido mostra o texto', () => {
    const campos = camposDoNo(QUADRO, { 9: '0,2' })

    expect(campos.map((c) => c.texto)).toEqual(['2', '0,2'])
  })

  it('campo vazio ou zero não vai; com todos vazios ou zero, ou com algum erro, os campos são inválidos', () => {
    const umVazio = camposDoNo(QUADRO, { 7: '' })
    expect(nosDoLote([{ item: QUADRO, campos: umVazio }])).toEqual([
      { estruturaItemId: 30, linhas: [{ materialId: 9, quantidade: 0.5 }] },
    ])
    expect(camposInvalidos(umVazio)).toBe(false)

    expect(camposInvalidos(camposDoNo(QUADRO, { 7: '', 9: '0' }))).toBe(true)
    expect(camposInvalidos(camposDoNo(QUADRO, { 7: 'dois' }))).toBe(true)
  })
})

describe('seleção do lote', () => {
  it('marcar não traz texto; desmarcar tira o cartão inteiro', () => {
    let lote: LoteDaSeparacao = {}
    lote = alternar(lote, 30, true)
    expect(lote).toEqual({ 30: {} })
    lote = digitar(lote, 30, 7, '1')
    expect(alternar(lote, 30, true)).toBe(lote)   // marcar de novo não apaga o digitado
    expect(alternar(lote, 30, false)).toEqual({})
    // Digitar num cartão que não está marcado não o marca.
    expect(digitar({}, 40, 7, '1')).toEqual({})
  })

  it('"Marcar todos" marca só os visíveis, sem reescrever o digitado; com todos marcados, desmarca só eles', () => {
    const comOculto = digitar(alternar({}, 30, true), 30, 7, '1')   // o Quadro marcado, e o filtro vai escondê-lo

    const marcados = marcarTodos(digitar(alternar(comOculto, 40, true), 40, 7, '3'), [TAMPA, CHAPA])
    expect(marcados).toEqual({ 30: { 7: '1' }, 40: { 7: '3' }, 50: {} })
    expect(todosMarcados(marcados, [TAMPA, CHAPA])).toBe(true)

    expect(marcarTodos(marcados, [TAMPA, CHAPA])).toEqual({ 30: { 7: '1' } })
    expect(todosMarcados({}, [])).toBe(false)
  })

  it('a recarga tira da seleção só o que saiu da lista inteira, e não reescreve o texto dos que ficam', () => {
    const lote: LoteDaSeparacao = { 30: { 7: '1' }, 40: {} }

    expect(reconciliar(lote, [QUADRO, TAMPA])).toEqual({ lote, saiu: false })
    expect(reconciliar(lote, [QUADRO, CHAPA])).toEqual({ lote: { 30: { 7: '1' } }, saiu: true })
    expect(reconciliar(lote, [CHAPA])).toEqual({ lote: {}, saiu: true })
  })

  it('o corpo do lote segue a ordem da lista, não a da marcação', () => {
    const lote = alternar(alternar({}, 40, true), 30, true)

    expect(nosDoLote(marcadosDoLote(lote, [QUADRO, TAMPA, CHAPA])).map((n) => n.estruturaItemId)).toEqual([30, 40])
  })
})
