// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { IntervaloDeDatas } from './IntervaloDeDatas'

afterEach(cleanup)

describe('IntervaloDeDatas', () => {
  it('os dois campos de data tem nome acessivel pelo rotulo', () => {
    render(<IntervaloDeDatas rotulo="Prazo de entrega" de="" ate="" aoMudar={() => {}} />)

    expect(screen.getByLabelText('Prazo de entrega: de')).toBeTruthy()
    expect(screen.getByLabelText('Prazo de entrega: até')).toBeTruthy()
  })

  it('mudar o "de" devolve os dois lados, com o "ate" como estava', () => {
    const aoMudar = vi.fn()
    render(<IntervaloDeDatas rotulo="Prazo" de="" ate="2026-12-31" aoMudar={aoMudar} />)

    fireEvent.change(screen.getByLabelText('Prazo: de'), { target: { value: '2026-12-01' } })

    expect(aoMudar).toHaveBeenCalledWith({ de: '2026-12-01', ate: '2026-12-31' })
  })

  it('mudar o "ate" devolve os dois lados, com o "de" como estava', () => {
    const aoMudar = vi.fn()
    render(<IntervaloDeDatas rotulo="Prazo" de="2026-12-01" ate="" aoMudar={aoMudar} />)

    fireEvent.change(screen.getByLabelText('Prazo: até'), { target: { value: '2026-12-15' } })

    expect(aoMudar).toHaveBeenCalledWith({ de: '2026-12-01', ate: '2026-12-15' })
  })

  it('"Limpar" zera os dois lados', () => {
    const aoMudar = vi.fn()
    render(<IntervaloDeDatas rotulo="Prazo" de="2026-12-01" ate="2026-12-15" aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Limpar' }))

    expect(aoMudar).toHaveBeenCalledWith({ de: '', ate: '' })
  })

  it('"Limpar" aparece com so um lado preenchido e some com os dois vazios', () => {
    const { rerender } = render(<IntervaloDeDatas rotulo="Prazo" de="" ate="2026-12-15" aoMudar={() => {}} />)
    expect(screen.getByRole('button', { name: 'Limpar' })).toBeTruthy()

    rerender(<IntervaloDeDatas rotulo="Prazo" de="" ate="" aoMudar={() => {}} />)
    expect(screen.queryByRole('button', { name: 'Limpar' })).toBeNull()
  })

  it('nao troca os dois lados quando "de" passa de "ate"', () => {
    const aoMudar = vi.fn()
    render(<IntervaloDeDatas rotulo="Prazo" de="" ate="2026-12-01" aoMudar={aoMudar} />)

    fireEvent.change(screen.getByLabelText('Prazo: de'), { target: { value: '2026-12-20' } })

    expect(aoMudar).toHaveBeenCalledWith({ de: '2026-12-20', ate: '2026-12-01' })
  })
})
