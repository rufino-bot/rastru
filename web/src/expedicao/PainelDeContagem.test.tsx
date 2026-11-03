// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act } from '@testing-library/react'
import { PainelDeContagem } from './PainelDeContagem'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao, posicoes } from '../testes/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/estrutura/30/contagens'
const CONTAGEM = {
  id: 9, conformes: 4, naoConformes: 2, dataHora: '2026-10-20T10:00:00-03:00', usuarioId: 12,
  usuarioNome: 'Qualidade do turno', estornada: false, estornavel: true,
}

// Aferição não conforme, 6 a medir no local de expedição.
const PECA = pecaNaExpedicao({ situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naExpedicao: 6 }) })

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar(peca = PECA) {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDeContagem peca={peca} aoFechar={aoFechar} aoConcluir={aoConcluir} />)
  return { aoFechar, aoConcluir }
}

const campo = (nome: string) => screen.getByRole('textbox', { name: nome }) as HTMLInputElement
const registrar = () => screen.getByRole('button', { name: 'Registrar contagem' }) as HTMLButtonElement

describe('PainelDeContagem', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra a dica "{a} a medir."', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar(pecaNaExpedicao({ situacao: 'NaoConforme', posicoes: posicoes({ naExpedicao: 2.5 }) }))

    expect(screen.getByRole('form', { name: 'Contar as unidades' })).toBeTruthy()
    expect(screen.getByText('2,5 a medir.')).toBeTruthy()
    expect(campo('Conformes').value).toBe('')
    expect(campo('Não conformes').value).toBe('')
    // O foco inicial vai ao primeiro campo, não ao Cancelar.
    expect(document.activeElement).toBe(campo('Conformes'))
  })

  it('manda as duas quantidades, recarrega e fecha', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(CONTAGEM, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(campo('Conformes'), { target: { value: '4' } })
    fireEvent.change(campo('Não conformes'), { target: { value: '2' } })
    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ conformes: 4, naoConformes: 2 }])
  })

  it('Review Focus: aceita vírgula e manda 0.5', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(CONTAGEM, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    // O campo vazio é zero: "medi uma unidade e meia não conforme" não pede digitar 0 nas conformes.
    fireEvent.change(campo('Não conformes'), { target: { value: '0,5' } })
    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(corpos(fetchMock)).toEqual([{ conformes: 0, naoConformes: 0.5 }])
  })

  it('nada contado trava o envio', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(CONTAGEM, 201) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()

    expect(registrar().disabled).toBe(true)
    fireEvent.change(campo('Conformes'), { target: { value: '0' } })
    expect(registrar().disabled).toBe(true)

    fireEvent.submit(screen.getByRole('form', { name: 'Contar as unidades' }))
    await act(async () => {})
    expect(corpos(fetchMock)).toEqual([])
  })

  it('a soma acima do a medir trava o envio e diz o máximo', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(campo('Conformes'), { target: { value: '4' } })
    fireEvent.change(campo('Não conformes'), { target: { value: '3' } })

    expect(screen.getByText('As duas somam 7; há 6 a medir.')).toBeTruthy()
    expect(registrar().disabled).toBe(true)
  })

  it('campo ilegível mostra o erro nele e trava o envio', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(campo('Conformes'), { target: { value: 'abc' } })

    expect(campo('Conformes').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText('Digite um número com no máximo quatro casas decimais.')).toBeTruthy()
    expect(registrar().disabled).toBe(true)
  })

  it('409 mostra a frase do servidor dentro do painel e recarrega', async () => {
    const frase = 'Só há 3 de QD-01 — Quadro a medir no local de expedição.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409),
    }))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(campo('Conformes'), { target: { value: '4' } })
    fireEvent.click(registrar())

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain(frase)
    expect(screen.getByRole('form', { name: 'Contar as unidades' }).contains(alerta)).toBe(true)
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('o duplo toque grava uma contagem só', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.change(campo('Conformes'), { target: { value: '6' } })
    const form = screen.getByRole('form', { name: 'Contar as unidades' })
    fireEvent.submit(form)
    fireEvent.submit(form)
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Registrando…' })).toHaveProperty('disabled', true)
    await act(async () => { responder(respostaJson(CONTAGEM, 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
  })
})
