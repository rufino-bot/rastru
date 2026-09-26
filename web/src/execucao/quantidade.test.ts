import { describe, it, expect } from 'vitest'
import { lerQuantidade, quantidadeParaCampo } from './quantidade'

describe('lerQuantidade', () => {
  it('aceita vírgula e ponto', () => {
    expect(lerQuantidade('2,5', 10)).toEqual({ valor: 2.5, erro: null })
    expect(lerQuantidade('2.5', 10)).toEqual({ valor: 2.5, erro: null })
    expect(lerQuantidade(' 4 ', 10)).toEqual({ valor: 4, erro: null })
  })

  it('aceita exatamente o máximo', () => {
    expect(lerQuantidade('10', 10)).toEqual({ valor: 10, erro: null })
  })

  it('aceita quatro casas e recusa cinco', () => {
    expect(lerQuantidade('0,0001', 10)).toEqual({ valor: 0.0001, erro: null })
    expect(lerQuantidade('0,00001', 10).erro).toBe('Digite um número com no máximo quatro casas decimais.')
  })

  it('recusa zero, vazio, negativo, expoente e milhar', () => {
    expect(lerQuantidade('0', 10).erro).toBe('A quantidade precisa ser maior que zero.')
    expect(lerQuantidade('0,0', 10).erro).toBe('A quantidade precisa ser maior que zero.')
    for (const texto of ['', '-1', '1e2', '1.234,5', 'abc', '2,']) {
      expect(lerQuantidade(texto, 99999).erro, texto).toBe('Digite um número com no máximo quatro casas decimais.')
    }
  })

  it('recusa mais do que o disponível, dizendo quanto há', () => {
    expect(lerQuantidade('7', 6.5).erro).toBe('No máximo 6,5.')
  })
})

describe('quantidadeParaCampo', () => {
  it('escreve com vírgula e sem separador de milhar', () => {
    expect(quantidadeParaCampo(2.5)).toBe('2,5')
    expect(quantidadeParaCampo(1234)).toBe('1234')
    // Ida e volta: o que o campo mostra, `lerQuantidade` lê de volta igual.
    expect(lerQuantidade(quantidadeParaCampo(1234.5678), 1234.5678)).toEqual({ valor: 1234.5678, erro: null })
  })
})
