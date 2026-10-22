// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act } from '@testing-library/react'
import { PainelDeAfericao } from './PainelDeAfericao'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao } from '../testes/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/estrutura/30/afericoes'

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar() {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDeAfericao peca={pecaNaExpedicao()} aoFechar={aoFechar} aoConcluir={aoConcluir} />)
  return { aoFechar, aoConcluir }
}

const registrar = () => screen.getByRole('button', { name: 'Registrar aferição' })

describe('PainelDeAfericao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('abre com as duas opções, a dica e o submit travado até escolher', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar()

    const form = screen.getByRole('form', { name: 'Aferir' })
    expect(form.textContent).toContain('QD-01 — Quadro')
    expect(screen.getByRole('radio', { name: 'Conforme' })).toBeTruthy()
    expect(screen.getByRole('radio', { name: 'Não conforme' })).toBeTruthy()
    expect(screen.getByText('Uma unidade medida decide o lote.')).toBeTruthy()
    expect((registrar() as HTMLButtonElement).disabled).toBe(true)
  })

  it('Não conforme manda resultado NaoConforme, recarrega e fecha', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(afericao(), 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.click(screen.getByRole('radio', { name: 'Não conforme' }))
    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ resultado: 'NaoConforme' }])
  })

  it('Conforme manda resultado Conforme', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(afericao({ resultado: 'Conforme' }), 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.click(screen.getByRole('radio', { name: 'Conforme' }))
    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(corpos(fetchMock)).toEqual([{ resultado: 'Conforme' }])
  })

  it('409 mostra a frase do servidor dentro do painel, recarrega e não fecha', async () => {
    const frase = 'QD-01 — Quadro já tem uma aferição conforme. Para registrar outra, estorne essa primeiro.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'AfericaoJaRegistrada', mensagem: frase }, 409),
    }))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.click(screen.getByRole('radio', { name: 'Conforme' }))
    fireEvent.click(registrar())

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain(frase)
    expect(screen.getByRole('form', { name: 'Aferir' }).contains(alerta)).toBe(true)
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('o duplo toque grava uma aferição só, e o Cancelar trava durante o envio', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.click(screen.getByRole('radio', { name: 'Conforme' }))
    // Dois `submit` no mesmo quadro, antes de o React redesenhar o botão desabilitado.
    const form = screen.getByRole('form', { name: 'Aferir' })
    fireEvent.submit(form)
    fireEvent.submit(form)
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Registrando…' })).toHaveProperty('disabled', true)
    expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    await act(async () => { responder(respostaJson(afericao({ resultado: 'Conforme' }), 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
  })
})
