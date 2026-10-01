import { describe, it, expect } from 'vitest'
import { ordenarCadastro } from './ordenarCadastro'

interface Item { id: number; nome: string }

describe('ordenarCadastro', () => {
  it('nulo ordena por id decrescente', () => {
    const itens: Item[] = [{ id: 2, nome: 'B' }, { id: 9, nome: 'A' }, { id: 5, nome: 'C' }]

    expect(ordenarCadastro(itens, null).map((i) => i.id)).toEqual([9, 5, 2])
  })

  it('texto ordena crescente em pt-BR', () => {
    const itens: Item[] = [
      { id: 1, nome: 'Faia' }, { id: 2, nome: 'Ébano' }, { id: 3, nome: 'Delta' }, { id: 4, nome: 'Z' },
    ]

    // Sem `localeCompare`, o `É` (U+00C9) cairia depois de `Z`.
    expect(ordenarCadastro(itens, (i) => i.nome).map((i) => i.nome)).toEqual(['Delta', 'Ébano', 'Faia', 'Z'])
  })

  it('empate no texto desempata por id decrescente', () => {
    const itens: Item[] = [{ id: 1, nome: 'Igual' }, { id: 7, nome: 'Igual' }, { id: 3, nome: 'Igual' }]

    expect(ordenarCadastro(itens, (i) => i.nome).map((i) => i.id)).toEqual([7, 3, 1])
  })

  it('nao muta o array recebido', () => {
    const itens: Item[] = [{ id: 1, nome: 'B' }, { id: 2, nome: 'A' }]
    const copia = [...itens]

    const resultado = ordenarCadastro(itens, (i) => i.nome)

    expect(resultado).not.toBe(itens)
    expect(itens).toEqual(copia)
  })
})
