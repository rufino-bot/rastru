// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { CaixaDeLote } from './CaixaDeLote'

afterEach(cleanup)

describe('CaixaDeLote', () => {
  it('é uma caixa com o nome acessível que a tela deu', () => {
    render(<CaixaDeLote nome="Marcar Suporte" marcada={false} aoMudar={vi.fn()} />)

    expect((screen.getByRole('checkbox', { name: 'Marcar Suporte' }) as HTMLInputElement).checked).toBe(false)
  })

  it('reflete o estado marcado', () => {
    render(<CaixaDeLote nome="Marcar Suporte" marcada aoMudar={vi.fn()} />)

    expect((screen.getByRole('checkbox', { name: 'Marcar Suporte' }) as HTMLInputElement).checked).toBe(true)
  })

  it('clicar na caixa avisa com o valor novo', () => {
    const aoMudar = vi.fn()
    render(<CaixaDeLote nome="Marcar Suporte" marcada={false} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('checkbox', { name: 'Marcar Suporte' }))

    expect(aoMudar).toHaveBeenCalledWith(true)
  })

  it('clicar na área de toque em volta da caixa também marca', () => {
    // A área é o <label> que envolve a caixa: o toque que cai fora dos 20 px dela, mas dentro dos 44 x 44, marca.
    const aoMudar = vi.fn()
    const { container } = render(<CaixaDeLote nome="Marcar Suporte" marcada={false} aoMudar={aoMudar} />)

    fireEvent.click(container.querySelector('label')!)

    expect(aoMudar).toHaveBeenCalledWith(true)
  })

  it('a área de toque tem pelo menos 44 x 44 px', () => {
    // jsdom não calcula layout; o teste prende a DECLARAÇÃO: `min-h-11` e `min-w-11` são 44 px no Tailwind.
    const { container } = render(<CaixaDeLote nome="Marcar Suporte" marcada={false} aoMudar={vi.fn()} />)

    const classes = container.querySelector('label')!.className.split(/\s+/)
    expect(classes).toContain('min-h-11')
    expect(classes).toContain('min-w-11')
  })

  it('sem rótulo, não há texto visível ao lado da caixa', () => {
    const { container } = render(<CaixaDeLote nome="Marcar Suporte" marcada={false} aoMudar={vi.fn()} />)

    expect(container.textContent).toBe('')
  })

  it('com rótulo, mostra o texto ao lado e o nome acessível continua o da tela', () => {
    render(<CaixaDeLote nome="Levar Suporte" rotulo="Levar" marcada={false} aoMudar={vi.fn()} />)

    expect(screen.getByText('Levar')).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: 'Levar Suporte' })).toBeTruthy()
  })

  it('desabilitada, a caixa fica desabilitada', () => {
    render(<CaixaDeLote nome="Marcar Suporte" marcada={false} desabilitada aoMudar={vi.fn()} />)

    expect((screen.getByRole('checkbox', { name: 'Marcar Suporte' }) as HTMLInputElement).disabled).toBe(true)
  })
})
