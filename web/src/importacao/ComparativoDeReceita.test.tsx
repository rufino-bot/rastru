// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import { ComparativoDeReceita } from './ComparativoDeReceita'
import type { LinhaDoComparativoDto } from '../api/importacao'

afterEach(cleanup)

const LINHAS: LinhaDoComparativoDto[] = [
  { codigo: 'PA-300', descricao: 'Parafuso', noCatalogo: 8, noBom: 8, situacao: 'Igual' },
  { codigo: 'AR-400', descricao: 'Arruela', noCatalogo: 2, noBom: 4, situacao: 'QuantidadeMuda' },
  { codigo: 'CA-500', descricao: 'Calço', noCatalogo: null, noBom: 1.5, situacao: 'Entra' },
  { codigo: 'PI-600', descricao: 'Pino', noCatalogo: 3, noBom: null, situacao: 'Sai' },
]

describe('ComparativoDeReceita', () => {
  it('tem o cabeçalho Filho / Catálogo hoje / BOM / Situação', () => {
    render(<ComparativoDeReceita linhas={LINHAS} />)

    const tabela = screen.getByRole('table', { name: 'Comparativo da receita' })
    expect(within(tabela).getAllByRole('columnheader').map((c) => c.textContent))
      .toEqual(['Filho', 'Catálogo hoje', 'BOM', 'Situação'])
  })

  it('as quatro situações saem com texto legível, e a quantidade ausente vira travessão', () => {
    render(<ComparativoDeReceita linhas={LINHAS} />)

    const linha = (codigo: string) => screen.getByRole('row', { name: new RegExp(codigo) })
    expect(within(linha('PA-300')).getByText('Igual')).toBeTruthy()
    expect(within(linha('AR-400')).getByText('Quantidade diferente')).toBeTruthy()
    expect(within(linha('CA-500')).getByText('Só no BOM')).toBeTruthy()
    expect(within(linha('PI-600')).getByText('Só no catálogo')).toBeTruthy()

    // Quantidade com vírgula decimal e o lado que não tem a linha vazio, não "0" nem "null".
    const celulas = (codigo: string) => within(linha(codigo)).getAllByRole('cell').map((c) => c.textContent)
    expect(celulas('CA-500')).toEqual(['CA-500 Calço', '—', '1,5', 'Só no BOM'])
    expect(celulas('PI-600')).toEqual(['PI-600 Pino', '3', '—', 'Só no catálogo'])
    expect(celulas('AR-400')).toEqual(['AR-400 Arruela', '2', '4', 'Quantidade diferente'])
  })

  it('sem linhas, não desenha a tabela', () => {
    render(<ComparativoDeReceita linhas={[]} />)

    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByText('Nenhum filho de nenhum dos dois lados.')).toBeTruthy()
  })

  it('código em branco ou repetido não colapsa as linhas nem avisa de chave duplicada', () => {
    const erro = vi.spyOn(console, 'error').mockImplementation(() => {})
    render(<ComparativoDeReceita linhas={[
      { codigo: '', descricao: 'Sem código A', noCatalogo: 1, noBom: 1, situacao: 'Igual' },
      { codigo: '', descricao: 'Sem código B', noCatalogo: 2, noBom: 2, situacao: 'Igual' },
    ]} />)

    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(erro).not.toHaveBeenCalled()
    erro.mockRestore()
  })
})
