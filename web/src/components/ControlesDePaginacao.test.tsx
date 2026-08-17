// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ControlesDePaginacao } from './ControlesDePaginacao'

afterEach(cleanup)

describe('ControlesDePaginacao', () => {
  it('mostra a posição e o total', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByText(/Página 2 de 5/)).toBeTruthy()
    expect(screen.getByText(/97/)).toBeTruthy()
  })

  it('avança uma página no "Próxima"', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect(aoMudarPagina).toHaveBeenCalledWith(3)
  })

  it('recua uma página no "Anterior"', () => {
    // Na Fase 1B este botão nunca foi clicado por teste nenhum: mutá-lo para `pagina + 1`
    // sobrevivia, e a paginação só andaria para frente sem ninguém ver (achado I3 da Task 6).
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect(aoMudarPagina).toHaveBeenCalledWith(1)
  })

  it('desabilita "Anterior" na primeira página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={1} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect((screen.getByText('Anterior') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('desabilita "Próxima" na última página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={5} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect((screen.getByText('Próxima') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('não aparece quando há uma página só', () => {
    // Controles de paginação numa lista de 3 itens são ruído, e "Página 1 de 1" não informa nada.
    const { container } = render(
      <ControlesDePaginacao pagina={1} totalDePaginas={1} total={3} aoMudarPagina={() => {}} />,
    )

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como navegação', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByRole('navigation', { name: 'Paginação' })).toBeTruthy()
  })

  it('não põe peso primário em nenhum dos dois botões', () => {
    // A spec §3 barra dois botões com o mesmo peso visual na mesma tela, e é por isso que o `Botao`
    // tem variantes. Sem esta prova, mutar qualquer um dos dois para `primario` deixava a suíte
    // verde — a decisão que dá sentido às variantes não era tocada por nenhuma mutação do plano.
    // Token a token: `bg-acao-fundo` (hover do secundário) CONTÉM `bg-acao`, então uma comparação
    // por substring não discriminaria as duas variantes.
    const { container } = render(
      <ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />,
    )

    for (const botao of Array.from(container.querySelectorAll('button'))) {
      const classes = botao.className.split(/\s+/)
      expect(classes).toContain('border-borda-campo')
      expect(classes).not.toContain('bg-acao')
    }
  })
})
