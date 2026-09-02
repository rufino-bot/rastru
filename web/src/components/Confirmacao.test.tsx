// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { Confirmacao } from './Confirmacao'

afterEach(cleanup)

describe('Confirmacao', () => {
  it('aberto=false não renderiza nada', () => {
    render(
      <Confirmacao
        aberto={false}
        mensagem="Excluir?"
        rotuloConfirmar="Excluir"
        aoConfirmar={() => {}}
        aoCancelar={() => {}}
      />,
    )

    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('aberto=true mostra a mensagem e os dois botões', () => {
    render(
      <Confirmacao
        aberto
        mensagem="Excluir o nó CH-100?"
        rotuloConfirmar="Excluir"
        aoConfirmar={() => {}}
        aoCancelar={() => {}}
      />,
    )

    const dialogo = screen.getByRole('dialog')
    expect(dialogo).toBeTruthy()
    expect(screen.getByText('Excluir o nó CH-100?')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Excluir' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeTruthy()
  })

  it('clicar no botão de confirmar chama aoConfirmar, e só ele', () => {
    const aoConfirmar = vi.fn()
    const aoCancelar = vi.fn()
    render(
      <Confirmacao
        aberto
        mensagem="Excluir?"
        rotuloConfirmar="Excluir"
        aoConfirmar={aoConfirmar}
        aoCancelar={aoCancelar}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Excluir' }))

    expect(aoConfirmar).toHaveBeenCalledTimes(1)
    expect(aoCancelar).not.toHaveBeenCalled()
  })

  it('clicar em cancelar chama aoCancelar, e só ele', () => {
    const aoConfirmar = vi.fn()
    const aoCancelar = vi.fn()
    render(
      <Confirmacao
        aberto
        mensagem="Excluir?"
        rotuloConfirmar="Excluir"
        aoConfirmar={aoConfirmar}
        aoCancelar={aoCancelar}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoCancelar).toHaveBeenCalledTimes(1)
    expect(aoConfirmar).not.toHaveBeenCalled()
  })

  // A variante padrão é `perigo` (vermelho — convenção de Botao para ação destrutiva, não cor de
  // ESTADO de negócio). Classe a classe, não a string inteira: mesmo motivo do M14 de
  // `Botao.test.tsx` (`hover:bg-acao-fundo` contém `bg-acao` como substring).
  it('usa a variante perigo no botão de confirmar por padrão', () => {
    render(
      <Confirmacao
        aberto
        mensagem="Excluir?"
        rotuloConfirmar="Excluir"
        aoConfirmar={() => {}}
        aoCancelar={() => {}}
      />,
    )

    const classes = screen.getByRole('button', { name: 'Excluir' }).className.split(/\s+/)
    expect(classes).toContain('bg-negativo')
  })

  it('aceita rótulo de cancelar e variante de confirmar customizados', () => {
    render(
      <Confirmacao
        aberto
        mensagem="Prosseguir?"
        rotuloConfirmar="Prosseguir"
        rotuloCancelar="Voltar"
        varianteConfirmar="primario"
        aoConfirmar={() => {}}
        aoCancelar={() => {}}
      />,
    )

    expect(screen.getByRole('button', { name: 'Voltar' })).toBeTruthy()
    const classes = screen.getByRole('button', { name: 'Prosseguir' }).className.split(/\s+/)
    expect(classes).toContain('bg-acao')
    expect(classes).not.toContain('bg-negativo')
  })
})
