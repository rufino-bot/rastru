// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { PainelDeEscrita } from './PainelDeEscrita'

afterEach(cleanup)

function painel(props: Partial<React.ComponentProps<typeof PainelDeEscrita>> = {}) {
  return (
    <PainelDeEscrita titulo="Novo setor" aoEnviar={() => {}} aoFechar={() => {}} {...props}>
      <label>
        Nome
        <input />
      </label>
    </PainelDeEscrita>
  )
}

describe('PainelDeEscrita', () => {
  it('o titulo e o h2 e da nome acessivel ao form', () => {
    render(painel())

    expect(screen.getByRole('form', { name: 'Novo setor' })).toBeTruthy()
    expect(screen.getByRole('heading', { level: 2, name: 'Novo setor' })).toBeTruthy()
  })

  it('mostra o subtitulo quando existe e nao deixa paragrafo vazio quando nao existe', () => {
    const { container, rerender } = render(painel({ subtitulo: 'Usinagem' }))

    expect(screen.getByText('Usinagem').tagName).toBe('P')

    rerender(painel())

    expect(container.querySelector('p')).toBeNull()
  })

  it('Cancelar chama aoFechar e nao envia o form', () => {
    const aoFechar = vi.fn()
    const aoEnviar = vi.fn()
    render(painel({ aoFechar, aoEnviar }))

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoFechar).toHaveBeenCalledTimes(1)
    expect(aoEnviar).not.toHaveBeenCalled()
  })

  it('com a escrita em voo, Cancelar fica desabilitado e nao chama aoFechar', () => {
    const aoFechar = vi.fn()
    const { rerender } = render(painel({ aoFechar, enviando: true }))
    const cancelar = screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement

    expect(cancelar.disabled).toBe(true)
    fireEvent.click(cancelar)
    expect(aoFechar).not.toHaveBeenCalled()

    // A escrita respondeu: o Cancelar volta a funcionar.
    rerender(painel({ aoFechar, enviando: false }))
    expect(cancelar.disabled).toBe(false)
    fireEvent.click(cancelar)
    expect(aoFechar).toHaveBeenCalledTimes(1)
  })

  it('sem a prop enviando, Cancelar fica habilitado', () => {
    render(painel())

    expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('submeter chama aoEnviar', () => {
    const aoEnviar = vi.fn((e: React.FormEvent) => e.preventDefault())
    render(painel({ aoEnviar }))

    fireEvent.submit(screen.getByRole('form', { name: 'Novo setor' }))

    expect(aoEnviar).toHaveBeenCalledTimes(1)
  })

  it('ao abrir, o foco vai ao primeiro campo do conteudo, e nao ao Cancelar', () => {
    render(painel())

    expect(document.activeElement).toBe(screen.getByLabelText('Nome'))
  })

  it('pula controle desabilitado ao escolher o foco', () => {
    render(
      <PainelDeEscrita titulo="Novo setor" aoEnviar={() => {}} aoFechar={() => {}}>
        <input disabled aria-label="Bloqueado" />
        <select aria-label="Tipo">
          <option>A</option>
        </select>
      </PainelDeEscrita>,
    )

    expect(document.activeElement).toBe(screen.getByLabelText('Tipo'))
  })

  it('repassa o testId ao form', () => {
    render(painel({ testId: 'painel-de-escrita' }))

    expect(screen.getByTestId('painel-de-escrita').tagName).toBe('FORM')
  })

  it('sem testId nao poe o atributo', () => {
    render(painel())

    expect(screen.getByRole('form', { name: 'Novo setor' }).hasAttribute('data-testid')).toBe(false)
  })
})
