// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { ListaRecolhivel } from './ListaRecolhivel'

afterEach(cleanup)

function detalhes() {
  return screen.getByText(/^Filhos \(\d+\)$/).closest('details')!
}

describe('ListaRecolhivel', () => {
  it('nasce recolhida', () => {
    render(<ListaRecolhivel contagem={3} temAcao={false}><p>dentro</p></ListaRecolhivel>)

    expect(detalhes().open).toBe(false)
  })

  it('o resumo é o nome do controle e traz a contagem', () => {
    render(<ListaRecolhivel contagem={37} temAcao={false}><p>dentro</p></ListaRecolhivel>)

    const resumo = screen.getByText('Filhos (37)')
    expect(resumo.tagName).toBe('SUMMARY')
    expect(resumo.closest('details')).toBe(detalhes())
  })

  it('o conteúdo mora dentro do details, não ao lado', () => {
    render(<ListaRecolhivel contagem={1} temAcao={false}><p>dentro</p></ListaRecolhivel>)

    expect(screen.getByText('dentro').closest('details')).toBe(detalhes())
  })

  it('abre ao clicar no resumo', () => {
    render(<ListaRecolhivel contagem={1} temAcao={false}><p>dentro</p></ListaRecolhivel>)

    fireEvent.click(screen.getByText('Filhos (1)'))

    expect(detalhes().open).toBe(true)
  })

  it('nasce aberta quando algum item tem ação', () => {
    render(<ListaRecolhivel contagem={2} temAcao><p>dentro</p></ListaRecolhivel>)

    expect(detalhes().open).toBe(true)
  })

  it('abre quando a ação aparece depois, com a lista fechada', () => {
    const { rerender } = render(<ListaRecolhivel contagem={2} temAcao={false}><p>dentro</p></ListaRecolhivel>)
    expect(detalhes().open).toBe(false)

    rerender(<ListaRecolhivel contagem={2} temAcao><p>dentro</p></ListaRecolhivel>)

    expect(detalhes().open).toBe(true)
  })

  it('o usuário fecha a lista com ação e ela continua fechada enquanto a ação continua', async () => {
    const { rerender } = render(<ListaRecolhivel contagem={2} temAcao><p>dentro</p></ListaRecolhivel>)

    fireEvent.click(screen.getByText('Filhos (2)'))
    await waitFor(() => expect(detalhes().open).toBe(false))

    rerender(<ListaRecolhivel contagem={3} temAcao><p>dentro</p></ListaRecolhivel>)

    expect(detalhes().open).toBe(false)
  })

  it('depois de fechada pelo usuário, só reabre numa nova passagem de falso para verdadeiro', async () => {
    const { rerender } = render(<ListaRecolhivel contagem={2} temAcao><p>dentro</p></ListaRecolhivel>)
    fireEvent.click(screen.getByText('Filhos (2)'))
    await waitFor(() => expect(detalhes().open).toBe(false))

    rerender(<ListaRecolhivel contagem={2} temAcao={false}><p>dentro</p></ListaRecolhivel>)
    expect(detalhes().open).toBe(false)

    rerender(<ListaRecolhivel contagem={2} temAcao><p>dentro</p></ListaRecolhivel>)
    expect(detalhes().open).toBe(true)
  })

  it('o anel de foco do resumo é só por token', () => {
    render(<ListaRecolhivel contagem={1} temAcao={false}><p>dentro</p></ListaRecolhivel>)

    const classes = screen.getByText('Filhos (1)').className
    expect(classes).toContain('focus-visible:outline-acao')
  })
})
