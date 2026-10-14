// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { BarraDoLote } from './BarraDoLote'

afterEach(cleanup)

type Props = Parameters<typeof BarraDoLote>[0]

function barra(parcial: Partial<Props> = {}) {
  const props: Props = {
    rotulo: 'Iniciar 5 itens', rotuloCarregando: 'Registrando…', ocultos: 0, invalido: false, enviando: false, erro: null,
    aoEnviar: vi.fn(), aoLimpar: vi.fn(),
    ...parcial,
  }
  return { props, ...render(<BarraDoLote {...props} />) }
}

describe('BarraDoLote', () => {
  it('o botao escreve o rotulo que quem usa a barra manda', () => {
    const { unmount } = barra({ rotulo: 'Iniciar 5 itens' })
    expect(screen.getByRole('button', { name: 'Iniciar 5 itens' })).toBeTruthy()
    unmount()

    barra({ rotulo: 'Entregar 2 Kits e 3 itens' })
    expect(screen.getByRole('button', { name: 'Entregar 2 Kits e 3 itens' })).toBeTruthy()
  })

  it('o botao chama aoEnviar', () => {
    const { props } = barra()

    fireEvent.click(screen.getByRole('button', { name: 'Iniciar 5 itens' }))

    expect(props.aoEnviar).toHaveBeenCalledTimes(1)
  })

  it('avisa os marcados ocultos pelo filtro', () => {
    const { unmount } = barra({ ocultos: 1 })
    expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()
    unmount()

    const dois = barra({ ocultos: 2 })
    expect(screen.getByText('2 marcados ocultos pelo filtro')).toBeTruthy()
    dois.unmount()

    barra({ ocultos: 0 })
    expect(screen.queryByText(/oculto/)).toBeNull()
  })

  it('botao desabilitado com quantidade invalida', () => {
    barra({ invalido: true })

    expect((screen.getByRole('button', { name: 'Iniciar 5 itens' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('o botao fica habilitado com tudo valido', () => {
    barra()

    expect((screen.getByRole('button', { name: 'Iniciar 5 itens' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('enquanto envia, troca pelo rotulo de carregando que veio de fora e desabilita', () => {
    const { unmount } = barra({ enviando: true })
    expect((screen.getByRole('button', { name: 'Registrando…' }) as HTMLButtonElement).disabled).toBe(true)
    unmount()

    barra({ enviando: true, rotuloCarregando: 'Entregando…' })
    expect((screen.getByRole('button', { name: 'Entregando…' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('mostra a recusa no banner', () => {
    barra({ erro: 'Um item foi marcado duas vezes.' })

    expect(screen.getByRole('alert').textContent).toBe('Um item foi marcado duas vezes.')
  })

  it('sem recusa, nao ha banner', () => {
    barra()

    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('Limpar selecao chama aoLimpar', () => {
    const { props } = barra()

    fireEvent.click(screen.getByRole('button', { name: 'Limpar seleção' }))

    expect(props.aoLimpar).toHaveBeenCalledTimes(1)
  })

  it('barra nao usa cor de estado', () => {
    const { container } = barra({ ocultos: 2, erro: 'Recusado.' })
    const banner = screen.getByRole('alert')

    for (const el of Array.from(container.querySelectorAll('*'))) {
      if (el === banner) continue
      expect(el.className, el.outerHTML).not.toMatch(/positivo|negativo|atencao|atraso/)
    }
  })
})
