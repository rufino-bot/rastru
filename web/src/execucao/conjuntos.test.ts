import { describe, it, expect } from 'vitest'
import { conjuntosPresentes, lerConjuntos, quantidadeDoConjunto } from './conjuntos'

describe('lerConjuntos', () => {
  it('aceita inteiro entre 1 e o máximo', () => {
    expect(lerConjuntos('2', 3)).toEqual({ valor: 2, erro: null })
  })
  it.each([['0'], ['1,5'], ['abc'], ['']])('recusa %s', (texto) => {
    expect(lerConjuntos(texto, 3).valor).toBeNull()
  })
  it('recusa acima do máximo', () => {
    expect(lerConjuntos('4', 3)).toEqual({ valor: null, erro: 'No máximo 3.' })
  })
})

describe('quantidadeDoConjunto', () => {
  it('multiplica e arredonda a quatro casas', () => {
    expect(quantidadeDoConjunto(3, 0.1)).toBe(0.3)
    expect(quantidadeDoConjunto(2, 4)).toBe(8)
  })
})

describe('conjuntosPresentes', () => {
  it('é o menor número de conjuntos entre os filhos', () => {
    expect(conjuntosPresentes([{ presente: 8, quantidadePorPai: 4 }, { presente: 3, quantidadePorPai: 1 }])).toBe(2)
  })
  it('é zero quando um filho não tem nada', () => {
    expect(conjuntosPresentes([{ presente: 8, quantidadePorPai: 4 }, { presente: 0, quantidadePorPai: 1 }])).toBe(0)
  })
})
