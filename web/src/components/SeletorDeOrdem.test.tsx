// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { SeletorDeOrdem, type OpcaoDeOrdem } from './SeletorDeOrdem'

afterEach(cleanup)

type Ordem = 'recentes' | 'codigo' | 'descricao'

const OPCOES: readonly OpcaoDeOrdem<Ordem>[] = [
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'codigo', rotulo: 'Código (A→Z)' },
  { valor: 'descricao', rotulo: 'Descrição (A→Z)' },
]

describe('SeletorDeOrdem', () => {
  it('tem o rotulo Ordenar por', () => {
    render(<SeletorDeOrdem opcoes={OPCOES} valor="recentes" aoMudar={() => {}} />)

    expect(screen.getByLabelText('Ordenar por').tagName).toBe('SELECT')
  })

  it('lista as opcoes na ordem dada', () => {
    render(<SeletorDeOrdem opcoes={OPCOES} valor="recentes" aoMudar={() => {}} />)

    const rotulos = screen.getAllByRole('option').map((o) => o.textContent)
    expect(rotulos).toEqual(['Mais recentes', 'Código (A→Z)', 'Descrição (A→Z)'])
  })

  it('marca o valor corrente', () => {
    render(<SeletorDeOrdem opcoes={OPCOES} valor="codigo" aoMudar={() => {}} />)

    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('codigo')
    expect((screen.getByRole('option', { name: 'Código (A→Z)' }) as HTMLOptionElement).selected).toBe(true)
  })

  it('escolher chama aoMudar com o valor e nao com o rotulo', () => {
    const aoMudar = vi.fn()
    render(<SeletorDeOrdem opcoes={OPCOES} valor="recentes" aoMudar={aoMudar} />)

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'descricao' } })

    expect(aoMudar).toHaveBeenCalledTimes(1)
    expect(aoMudar).toHaveBeenCalledWith('descricao')
  })
})
