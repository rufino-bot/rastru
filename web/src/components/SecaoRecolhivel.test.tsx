// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { SecaoRecolhivel } from './SecaoRecolhivel'

afterEach(cleanup)

describe('SecaoRecolhivel', () => {
  it('nasce recolhida, com a contagem no título', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={23}><p>dentro</p></SecaoRecolhivel>)

    const resumo = screen.getByText('Kits incompletos (23)')
    expect(resumo.tagName).toBe('SUMMARY')
    expect(resumo.closest('details')?.open).toBe(false)
  })

  it('abre ao clicar no título', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={1}><p>dentro</p></SecaoRecolhivel>)

    fireEvent.click(screen.getByText('Kits incompletos (1)'))

    expect(screen.getByText('Kits incompletos (1)').closest('details')?.open).toBe(true)
  })

  it('o conteúdo mora dentro do details, não ao lado', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={1}><p>dentro</p></SecaoRecolhivel>)

    expect(screen.getByText('dentro').closest('details')).toBe(screen.getByText('Kits incompletos (1)').closest('details'))
  })
})
