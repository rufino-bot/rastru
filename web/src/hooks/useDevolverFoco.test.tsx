// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { useEffect, useRef, useState } from 'react'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { useDevolverFoco } from './useDevolverFoco'

afterEach(cleanup)

/**
 * Uma tela mínima com a forma das telas reais: o botão do cabeçalho some com o painel aberto, e o
 * painel some com o `Cancelar` que tem o foco.
 */
function Tela({ pronto = true, comOutroPainel = false, alvoFixo = false }: {
  pronto?: boolean
  comOutroPainel?: boolean
  /** O alvo é um botão que nunca sai do DOM, para o teste ver um foco que não deveria acontecer. */
  alvoFixo?: boolean
}) {
  const [aberto, setAberto] = useState(false)
  const [outro, setOutro] = useState(false)
  const botao = useRef<HTMLButtonElement>(null)
  const fixo = useRef<HTMLButtonElement>(null)
  useDevolverFoco(aberto, () => (alvoFixo ? fixo.current : botao.current), pronto)
  return (
    <div>
      {!aberto && <button ref={botao} onClick={() => setAberto(true)}>Novo</button>}
      <button ref={fixo}>Outra coisa</button>
      {aberto && (
        <div>
          <button onClick={() => setAberto(false)}>Cancelar</button>
          {comOutroPainel && (
            <button onClick={() => { setAberto(false); setOutro(true) }}>Trocar de painel</button>
          )}
        </div>
      )}
      {outro && <OutroPainel />}
    </div>
  )
}

/** Um painel que dá o foco ao próprio campo num efeito de montagem, como o `PainelDeEscrita`. */
function OutroPainel() {
  const campo = useRef<HTMLInputElement>(null)
  useEffect(() => { campo.current?.focus() }, [])
  return <input aria-label="Campo do outro" ref={campo} />
}

function abrirECancelar() {
  fireEvent.click(screen.getByRole('button', { name: 'Novo' }))
  const cancelar = screen.getByRole('button', { name: 'Cancelar' })
  cancelar.focus()
  fireEvent.click(cancelar)
}

describe('useDevolverFoco', () => {
  it('ao fechar, devolve o foco a quem abriu', () => {
    render(<Tela />)

    abrirECancelar()

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Novo' }))
  })

  it('na montagem, com o painel fechado, nao mexe no foco', () => {
    render(<Tela />)

    expect(document.activeElement).toBe(document.body)
  })

  it('nao rouba o foco que ja esta em outro lugar', () => {
    render(<Tela comOutroPainel />)
    fireEvent.click(screen.getByRole('button', { name: 'Novo' }))
    const trocar = screen.getByRole('button', { name: 'Trocar de painel' })
    trocar.focus()

    fireEvent.click(trocar)

    expect(document.activeElement).toBe(screen.getByLabelText('Campo do outro'))
  })

  it('espera pronto para devolver o foco', () => {
    const { rerender } = render(<Tela pronto />)
    rerender(<Tela pronto={false} />)
    abrirECancelar()

    expect(document.activeElement).toBe(document.body)

    rerender(<Tela pronto />)

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Novo' }))
  })

  it('reabrir antes de pronto descarta o pedido pendente', () => {
    const { rerender } = render(<Tela pronto={false} alvoFixo />)
    abrirECancelar()
    // Reaberto: o fechamento anterior não pede mais foco. `fireEvent.click` não move o foco, que
    // continua no `<body>` — a condição em que um pedido pendente esquecido focaria o alvo.
    fireEvent.click(screen.getByRole('button', { name: 'Novo' }))
    expect(document.activeElement).toBe(document.body)

    rerender(<Tela pronto alvoFixo />)

    expect(document.activeElement).toBe(document.body)
  })
})
