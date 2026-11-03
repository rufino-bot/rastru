// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act, within } from '@testing-library/react'
import { PainelDeConserto } from './PainelDeConserto'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao, posicoes } from '../testes/expedicao'
import type { SetorDto } from '../api/cadastros'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/estrutura/30/consertos'

const setor = (id: number, nome: string, ativo = true): SetorDto =>
  ({ id, nome, ativo, atividade: null, utilizaKit: false })
const SETORES = [setor(1, 'Corte'), setor(2, 'Dobra', false), setor(3, 'Solda')]

// Não conforme com 3 aguardando decisão.
const PECA = pecaNaExpedicao({
  situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naExpedicao: 1, naoConforme: 3 }),
})

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar(peca = PECA) {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDeConserto peca={peca} setores={SETORES} aoFechar={aoFechar} aoConcluir={aoConcluir} />)
  return { aoFechar, aoConcluir }
}

const formulario = () => screen.getByRole('form', { name: 'Mandar para conserto' })
const seletor = () => screen.getByRole('combobox', { name: 'Setor' }) as HTMLSelectElement
const quantidade = () => screen.getByRole('textbox', { name: 'Quantidade' }) as HTMLInputElement
const enviar = () => within(formulario()).getByRole('button', { name: 'Mandar para conserto' }) as HTMLButtonElement
const MOVIMENTO = { id: 77 }

describe('PainelDeConserto', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('oferece só os Setores ativos e abre com as não conformes aguardando decisão', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar()

    const opcoes = within(seletor()).getAllByRole('option').map((o) => o.textContent)
    expect(opcoes).toEqual(['Escolha o Setor', 'Corte', 'Solda'])
    expect(quantidade().value).toBe('3')
    // Sem Setor escolhido, não envia.
    expect(enviar().disabled).toBe(true)
  })

  it('manda o Setor e a quantidade, recarrega e fecha', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(MOVIMENTO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(seletor(), { target: { value: '3' } })
    fireEvent.change(quantidade(), { target: { value: '1,5' } })
    fireEvent.click(enviar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ setorId: 3, quantidade: 1.5 }])
  })

  it('quantidade acima das não conformes trava o envio', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(seletor(), { target: { value: '1' } })
    fireEvent.change(quantidade(), { target: { value: '4' } })

    expect(screen.getByText('No máximo 3.')).toBeTruthy()
    expect(quantidade().getAttribute('aria-invalid')).toBe('true')
    expect(enviar().disabled).toBe(true)
  })

  it('a recusa mostra a frase do servidor dentro do painel', async () => {
    const frase = 'O conserto vai a um Setor ativo: escolha outro.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'SetorInvalido', mensagem: frase }, 400),
    }))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(seletor(), { target: { value: '1' } })
    fireEvent.click(enviar())

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain(frase)
    expect(formulario().contains(alerta)).toBe(true)
    // 400 não é tela velha: não recarrega.
    expect(aoConcluir).not.toHaveBeenCalled()
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('409 recarrega, com a frase dentro do painel', async () => {
    const frase = 'Só há 1 de QD-01 — Quadro não conforme no local de expedição.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409),
    }))
    const { aoConcluir } = renderizar()

    fireEvent.change(seletor(), { target: { value: '1' } })
    fireEvent.click(enviar())

    expect((await screen.findByRole('alert')).textContent).toContain(frase)
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('o duplo toque manda um conserto só', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.change(seletor(), { target: { value: '1' } })
    fireEvent.submit(formulario())
    fireEvent.submit(formulario())
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    await act(async () => { responder(respostaJson(MOVIMENTO, 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
  })
})
