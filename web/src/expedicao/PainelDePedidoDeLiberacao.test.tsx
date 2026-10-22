// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act, within } from '@testing-library/react'
import { PainelDePedidoDeLiberacao } from './PainelDePedidoDeLiberacao'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao, posicoes } from '../testes/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/estrutura/30/pedidos-de-liberacao'
const LIBERACAO = {
  id: 5, tipo: 'Pedido', pedidoDeLiberacaoId: null, estruturaItemId: 30, quantidade: 2, texto: 'Sobra de dobra',
  dataHora: '2026-10-20T11:00:00-03:00', usuarioId: 12, usuarioNome: 'Qualidade do turno', estornada: false,
  estornavel: true,
}

const PECA = pecaNaExpedicao({
  situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naoConforme: 2 }),
})

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar() {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDePedidoDeLiberacao peca={PECA} aoFechar={aoFechar} aoConcluir={aoConcluir} />)
  return { aoFechar, aoConcluir }
}

const formulario = () => screen.getByRole('form', { name: 'Pedir liberação' })
const quantidade = () => screen.getByRole('textbox', { name: 'Quantidade' }) as HTMLInputElement
const justificativa = () => screen.getByRole('textbox', { name: 'Justificativa' }) as HTMLTextAreaElement
const enviar = () => within(formulario()).getByRole('button', { name: 'Pedir liberação' }) as HTMLButtonElement

describe('PainelDePedidoDeLiberacao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('abre com as não conformes aguardando decisão e pede justificativa', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar()

    expect(quantidade().value).toBe('2')
    expect(justificativa().value).toBe('')
    expect(justificativa().maxLength).toBe(500)
    // A justificativa é obrigatória (C6): em branco, ou só espaços, não envia.
    expect(enviar().disabled).toBe(true)
    fireEvent.change(justificativa(), { target: { value: '   ' } })
    expect(enviar().disabled).toBe(true)
  })

  it('manda a quantidade e a justificativa sem os espaços das pontas, recarrega e fecha', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(LIBERACAO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(quantidade(), { target: { value: '1,5' } })
    fireEvent.change(justificativa(), { target: { value: '  Sobra de dobra não atrapalha a montagem  ' } })
    fireEvent.click(enviar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ quantidade: 1.5, justificativa: 'Sobra de dobra não atrapalha a montagem' }])
  })

  it('quantidade acima das não conformes trava o envio', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(justificativa(), { target: { value: 'Sobra' } })
    fireEvent.change(quantidade(), { target: { value: '3' } })

    expect(screen.getByText('No máximo 2.')).toBeTruthy()
    expect(enviar().disabled).toBe(true)
  })

  it('409 LiberacaoEmAberto mostra a frase dentro do painel e recarrega', async () => {
    const frase = 'QD-01 — Quadro já tem um pedido de liberação em aberto.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'LiberacaoEmAberto', mensagem: frase }, 409),
    }))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.change(justificativa(), { target: { value: 'Sobra' } })
    fireEvent.click(enviar())

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain(frase)
    expect(formulario().contains(alerta)).toBe(true)
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('o duplo toque manda um pedido só', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.change(justificativa(), { target: { value: 'Sobra' } })
    fireEvent.submit(formulario())
    fireEvent.submit(formulario())
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    await act(async () => { responder(respostaJson(LIBERACAO, 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
  })
})
