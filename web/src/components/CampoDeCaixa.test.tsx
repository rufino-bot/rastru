// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { CampoDeCaixa } from './CampoDeCaixa'

afterEach(cleanup)

describe('CampoDeCaixa', () => {
  it('nomeia a caixa pelo texto e mostra o estado marcado', () => {
    render(<CampoDeCaixa marcado aoMudar={() => {}}>Dar baixa também delas</CampoDeCaixa>)

    const caixa = screen.getByRole('checkbox', { name: 'Dar baixa também delas' }) as HTMLInputElement
    expect(caixa.checked).toBe(true)
  })

  it('devolve o novo estado por aoMudar', () => {
    const aoMudar = vi.fn()
    render(<CampoDeCaixa marcado aoMudar={aoMudar}>Dar baixa também delas</CampoDeCaixa>)

    fireEvent.click(screen.getByRole('checkbox'))

    expect(aoMudar).toHaveBeenCalledWith(false)
  })

  it('desabilitada não aceita o toque', () => {
    render(<CampoDeCaixa marcado={false} aoMudar={() => {}} desabilitado>Texto</CampoDeCaixa>)

    expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
  })

  it('mostra a dica abaixo do texto, ligada à caixa', () => {
    render(<CampoDeCaixa marcado aoMudar={() => {}} dica="Só 2 ainda estão a iniciar.">Texto</CampoDeCaixa>)

    expect(screen.getByRole('checkbox', { description: 'Só 2 ainda estão a iniciar.' })).toBeTruthy()
    expect(screen.getByText('Só 2 ainda estão a iniciar.')).toBeTruthy()
  })
})
