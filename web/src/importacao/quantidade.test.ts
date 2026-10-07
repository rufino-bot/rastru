import { describe, expect, it } from 'vitest'
import { lerQuantidadeDaConferencia } from './quantidade'

describe('lerQuantidadeDaConferencia', () => {
  it.each([
    ['1,5', 1.5],
    ['2.25', 2.25],
    ['0,0001', 0.0001],
    ['99999999999999,9999', 99999999999999.9999],
    ['0099999999999999', 99999999999999],
  ])('aceita %s', (texto, valor) => {
    expect(lerQuantidadeDaConferencia(texto)).toEqual({ valor, motivo: null })
  })

  it.each([
    ['1,23456', 'Digite um número com no máximo quatro casas decimais.'],
    ['abc', 'Digite um número com no máximo quatro casas decimais.'],
    ['-1', 'Digite um número com no máximo quatro casas decimais.'],
    ['1e3', 'Digite um número com no máximo quatro casas decimais.'],
    ['', 'Digite um número com no máximo quatro casas decimais.'],
    ['0', 'A quantidade precisa ser maior que zero.'],
    ['0,0000', 'A quantidade precisa ser maior que zero.'],
    ['100000000000000', 'No máximo 99.999.999.999.999,9999.'],
  ])('recusa %s', (texto, motivo) => {
    expect(lerQuantidadeDaConferencia(texto)).toEqual({ valor: null, motivo })
  })
})
