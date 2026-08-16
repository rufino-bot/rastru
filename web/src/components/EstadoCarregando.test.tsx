// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { EstadoCarregando } from './EstadoCarregando'

afterEach(cleanup)

describe('EstadoCarregando', () => {
  it('mostra o texto de carregando', () => {
    render(<EstadoCarregando />)

    expect(screen.getByText('Carregando…')).toBeTruthy()
  })

  it('tem o papel status, para leitor de tela anunciar', () => {
    render(<EstadoCarregando />)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })
})
