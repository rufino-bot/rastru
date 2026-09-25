// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { FormularioDeQuantidade } from './FormularioDeQuantidade'

afterEach(cleanup)

function renderizar(props: Partial<Parameters<typeof FormularioDeQuantidade>[0]> = {}) {
  const aoConfirmar = props.aoConfirmar ?? vi.fn().mockResolvedValue(undefined)
  const aoCancelar = props.aoCancelar ?? vi.fn()
  render(<FormularioDeQuantidade rotulo="Iniciar" maximo={10} {...props} aoConfirmar={aoConfirmar} aoCancelar={aoCancelar} />)
  return { aoConfirmar, aoCancelar }
}

describe('FormularioDeQuantidade', () => {
  it('nasce com todo o disponível', () => {
    renderizar({ maximo: 2.5 })

    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '2,5')
    expect(screen.getByText('Disponível: 2,5')).toBeTruthy()
  })

  it('confirma a quantidade digitada, com vírgula', async () => {
    const { aoConfirmar } = renderizar()

    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '3,5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    await waitFor(() => expect(aoConfirmar).toHaveBeenCalledWith(3.5))
  })

  it('não deixa confirmar acima do disponível, e diz o limite', () => {
    const { aoConfirmar } = renderizar()

    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '11' } })

    expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', true)
    expect(screen.getByText('No máximo 10.')).toBeTruthy()
    expect(screen.getByLabelText('Quantidade').getAttribute('aria-invalid')).toBe('true')
    fireEvent.submit(screen.getByLabelText('Quantidade').closest('form')!)
    expect(aoConfirmar).not.toHaveBeenCalled()
  })

  it('não deixa confirmar com cinco casas decimais', () => {
    renderizar()

    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '0,00001' } })

    expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', true)
  })

  it('toque duplo envia uma vez só', async () => {
    // Spec §8.1: o botão fica desabilitado enquanto envia. Dois `submit` seguidos, com o primeiro
    // ainda em voo — o segundo não chama de novo.
    let resolver: () => void = () => {}
    const aoConfirmar = vi.fn(() => new Promise<void>((r) => { resolver = r }))
    renderizar({ aoConfirmar })

    const form = screen.getByLabelText('Quantidade').closest('form')!
    fireEvent.submit(form)
    fireEvent.submit(form)

    expect(aoConfirmar).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Enviando…' })).toHaveProperty('disabled', true)
    resolver()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', false))
  })

  it('destrava depois de uma recusa, para tentar de novo', async () => {
    const aoConfirmar = vi.fn().mockRejectedValueOnce(new Error('409')).mockResolvedValueOnce(undefined)
    renderizar({ aoConfirmar })

    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', false))
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    await waitFor(() => expect(aoConfirmar).toHaveBeenCalledTimes(2))
  })

  it('campos a mais incompletos desabilitam o confirmar', () => {
    renderizar({ completo: false, children: <p>escolha o Setor</p> })

    expect(screen.getByText('escolha o Setor')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', true)
  })

  it('cancelar chama quem abriu', () => {
    const { aoCancelar } = renderizar()

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoCancelar).toHaveBeenCalledTimes(1)
  })
})
