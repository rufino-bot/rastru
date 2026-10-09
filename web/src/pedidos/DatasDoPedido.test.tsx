// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { DatasDoPedido } from './DatasDoPedido'

afterEach(cleanup)

describe('DatasDoPedido', () => {
  it('mostra o prazo e a abertura em dois blocos, com o separador no fim do primeiro', () => {
    render(<DatasDoPedido dataEntrega="2026-10-22" dataAbertura="2026-08-01T09:30:00-03:00" />)

    expect(screen.getByText('entrega em 22/10/2026 ·')).toBeTruthy()
    expect(screen.getByText('aberto em 01/08/2026 09:30')).toBeTruthy()
  })

  it('impede a quebra por dentro de cada bloco', () => {
    // O jsdom não faz layout, então este teste não mede a quebra: afirma só a classe que a impede.
    // Sem ela, a hora do "aberto em" desce sozinha, separada da data, quando a linha não cabe.
    render(<DatasDoPedido dataEntrega="2026-10-22" dataAbertura="2026-08-01T09:30:00-03:00" />)

    const blocos = [
      screen.getByText('entrega em 22/10/2026 ·'),
      screen.getByText('aberto em 01/08/2026 09:30'),
    ]
    for (const bloco of blocos) {
      expect(bloco.className.split(/\s+/)).toContain('whitespace-nowrap')
    }
  })
})
