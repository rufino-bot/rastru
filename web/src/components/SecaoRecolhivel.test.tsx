// @vitest-environment jsdom
import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
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

  it('a moldura (borda, raio, fundo e respiro dos cartões da tela) está no elemento de fora', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={1}><p>dentro</p></SecaoRecolhivel>)

    const fora = screen.getByText('Kits incompletos (1)').closest('details')!
    expect(fora.className).toContain('border-borda')
    expect(fora.className).toContain('rounded-lg')
    expect(fora.className).toContain('bg-superficie')
    expect(fora.className).toContain('px-4')
    expect(fora.className).toContain('py-3')
  })

  it('o conteúdo mora dentro do details, não ao lado', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={1}><p>dentro</p></SecaoRecolhivel>)

    expect(screen.getByText('dentro').closest('details')).toBe(screen.getByText('Kits incompletos (1)').closest('details'))
  })

  describe('controlada pela tela', () => {
    it('aberta={true} abre a seção sem clique, e aberta={false} a fecha', () => {
      const { rerender } = render(
        <SecaoRecolhivel titulo="Em Solda" contagem={2} aberta><p>dentro</p></SecaoRecolhivel>,
      )
      expect(screen.getByText('Em Solda (2)').closest('details')?.open).toBe(true)

      rerender(<SecaoRecolhivel titulo="Em Solda" contagem={2} aberta={false}><p>dentro</p></SecaoRecolhivel>)
      expect(screen.getByText('Em Solda (2)').closest('details')?.open).toBe(false)
    })

    it('o clique no título devolve o novo estado por aoAlternar', async () => {
      const aoAlternar = vi.fn()
      render(
        <SecaoRecolhivel titulo="Em Solda" contagem={2} aberta={false} aoAlternar={aoAlternar}><p>dentro</p></SecaoRecolhivel>,
      )

      fireEvent.click(screen.getByText('Em Solda (2)'))

      await waitFor(() => expect(aoAlternar).toHaveBeenCalledWith(true))
    })

    it('o nome acessível do controle é o título com a contagem', () => {
      render(<SecaoRecolhivel titulo="Em Solda" contagem={2} aberta={false} aoAlternar={() => {}}><p>dentro</p></SecaoRecolhivel>)

      expect(screen.getByText('Em Solda (2)').tagName).toBe('SUMMARY')
    })
  })
})
