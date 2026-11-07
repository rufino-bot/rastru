// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeNaoRepor } from './PainelDeNaoRepor'
import { ErroDeApi } from '../api/erros'

afterEach(cleanup)

function renderizar(naoRepor: (j: string) => Promise<unknown> = async () => ({})) {
  const aoConcluir = vi.fn()
  const aoFechar = vi.fn()
  render(<PainelDeNaoRepor naoRepor={naoRepor} aoConcluir={aoConcluir} aoFechar={aoFechar} />)
  const form = screen.getByRole('form', { name: 'Não repor' })
  return { aoConcluir, aoFechar, form }
}

describe('PainelDeNaoRepor', () => {
  it('trava o envio sem justificativa e limita o texto a 500', () => {
    const { form } = renderizar()
    const enviar = within(form).getByRole('button', { name: 'Não repor' }) as HTMLButtonElement
    expect(enviar.disabled).toBe(true)
    const campo = screen.getByLabelText('Justificativa') as HTMLTextAreaElement
    expect(campo.maxLength).toBe(500)
    fireEvent.change(campo, { target: { value: '   ' } })
    expect(enviar.disabled).toBe(true)
    fireEvent.change(campo, { target: { value: 'cliente aceitou' } })
    expect(enviar.disabled).toBe(false)
  })

  it('grava a justificativa aparada, avisa quem abriu e fecha', async () => {
    const naoRepor = vi.fn(async () => ({}))
    const { form, aoConcluir, aoFechar } = renderizar(naoRepor)
    fireEvent.change(screen.getByLabelText('Justificativa'), { target: { value: '  sem custo  ' } })
    fireEvent.submit(form)
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    expect(naoRepor).toHaveBeenCalledWith('sem custo')
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('duplo toque no envio faz uma requisição só', async () => {
    let resolver: () => void = () => {}
    const naoRepor = vi.fn(() => new Promise<unknown>((r) => { resolver = () => r({}) }))
    const { form, aoFechar } = renderizar(naoRepor)
    fireEvent.change(screen.getByLabelText('Justificativa'), { target: { value: 'x' } })
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(naoRepor).toHaveBeenCalledTimes(1)
    expect((within(form).getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    resolver()
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
  })

  it('mostra a frase do servidor dentro do painel e mantém o digitado', async () => {
    const naoRepor = vi.fn(async () => { throw new ErroDeApi(409, 'x', 'Já tem retrabalho.', 'JaReposto') })
    const { form, aoFechar } = renderizar(naoRepor)
    fireEvent.change(screen.getByLabelText('Justificativa'), { target: { value: 'motivo' } })
    fireEvent.submit(form)
    expect(await within(form).findByText('Já tem retrabalho.')).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
    expect((screen.getByLabelText('Justificativa') as HTMLTextAreaElement).value).toBe('motivo')
  })
})
