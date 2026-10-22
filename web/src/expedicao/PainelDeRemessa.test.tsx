// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act, within } from '@testing-library/react'
import { PainelDeRemessa } from './PainelDeRemessa'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao, posicoes } from '../testes/expedicao'
import type { PecaNaExpedicaoDto } from '../api/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const ROTA = '/api/remessas'
const RESULTADO = {
  remessa: { id: 3, dataHora: '2026-10-20T15:00:00-03:00', usuarioId: 12, usuarioNome: 'Qualidade do turno',
    estornada: false, estornavel: true, linhas: [] },
  avisos: [],
}

// Não conforme: 4 conformes medidas e 2 não conformes aguardando decisão. O expedível é 6.
const MEDIDA = pecaNaExpedicao({
  situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ conforme: 4, naoConforme: 2 }), expedivel: 6,
})

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar(peca: PecaNaExpedicaoDto = MEDIDA) {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  render(<PainelDeRemessa peca={peca} aoFechar={aoFechar} aoConcluir={aoConcluir} />)
  return { aoFechar, aoConcluir }
}

const formulario = () => screen.getByRole('form', { name: 'Expedir remessa' })
const quantidade = () => screen.getByRole('textbox', { name: 'Quantidade' }) as HTMLInputElement
const avisos = () => screen.queryByRole('list', { name: 'Avisos da remessa' })

describe('PainelDeRemessa', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('abre com o expedível preenchido', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar(pecaNaExpedicao({ requerRelatorioDimensional: false, situacao: 'SemRelatorio',
      posicoes: posicoes({ naExpedicao: 2.5 }), expedivel: 2.5 }))

    expect(formulario().textContent).toContain('QD-01 — Quadro')
    expect(quantidade().value).toBe('2,5')
    expect(document.activeElement).toBe(quantidade())
    expect(screen.getByRole('button', { name: 'Expedir 2,5' })).toBeTruthy()
    // Sem relatório, nada a avisar.
    expect(avisos()).toBeNull()
  })

  it('mostra a prévia dos avisos antes de confirmar', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    // Tira 4 das conformes e 2 das não conformes (C10): as 2 saem sem liberação.
    const lista = avisos()!
    const linhas = within(lista).getAllByRole('listitem')
    expect(linhas.map((l) => l.textContent)).toEqual(['2 não conformes sem liberação'])
    expect(linhas[0].className).toContain('text-negativo')
    expect(screen.getByText('Dá para expedir assim mesmo; fica registrado.')).toBeTruthy()

    // Só as conformes: nada a avisar.
    fireEvent.change(quantidade(), { target: { value: '4' } })
    expect(avisos()).toBeNull()
    expect(screen.queryByText('Dá para expedir assim mesmo; fica registrado.')).toBeNull()
    expect(screen.getByRole('button', { name: 'Expedir 4' })).toBeTruthy()
  })

  it('sem aferição e sem medição avisam em texto neutro', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar(pecaNaExpedicao({
      situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naExpedicao: 3 }), expedivel: 3,
    }))

    const [semMedicao] = within(avisos()!).getAllByRole('listitem')
    expect(semMedicao.textContent).toBe('3 sem medição')
    expect(semMedicao.className).not.toMatch(/negativo|positivo|atencao|atraso/)

    cleanup()
    renderizar(pecaNaExpedicao({ situacao: 'Pendente', posicoes: posicoes({ naExpedicao: 6 }), expedivel: 6 }))
    const [semAfericao] = within(avisos()!).getAllByRole('listitem')
    expect(semAfericao.textContent).toBe('6 sem aferição')
    expect(semAfericao.className).not.toMatch(/negativo|positivo|atencao|atraso/)
  })

  it('não trava pelo aviso: expede e manda a Peça e a quantidade', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(RESULTADO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.click(screen.getByRole('button', { name: 'Expedir 6' }))

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ itens: [{ estruturaItemId: 30, quantidade: 6 }] }])
  })

  it('acima do expedível trava o envio e não mostra prévia', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(quantidade(), { target: { value: '7' } })

    expect(screen.getByText('No máximo 6.')).toBeTruthy()
    expect(quantidade().getAttribute('aria-invalid')).toBe('true')
    expect((screen.getByRole('button', { name: 'Expedir' }) as HTMLButtonElement).disabled).toBe(true)
    expect(avisos()).toBeNull()
  })

  it('Review Focus: duplo toque grava uma remessa só', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    // Dois `submit` no mesmo quadro, antes de o React redesenhar o botão desabilitado.
    fireEvent.submit(formulario())
    fireEvent.submit(formulario())
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Expedindo…' })).toHaveProperty('disabled', true)
    expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)

    await act(async () => { responder(respostaJson(RESULTADO, 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(corpos(fetchMock)).toHaveLength(1)
  })

  it('409 SaldoInsuficiente aparece dentro do painel', async () => {
    const frase = 'Só há 4 de QD-01 — Quadro para expedir.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409),
    }))
    const { aoFechar, aoConcluir } = renderizar()

    fireEvent.click(screen.getByRole('button', { name: 'Expedir 6' }))

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toContain(frase)
    expect(formulario().contains(alerta)).toBe(true)
    // Tela velha: quem abriu recarrega, e o painel fica aberto com o digitado.
    expect(aoConcluir).toHaveBeenCalledTimes(1)
    expect(aoFechar).not.toHaveBeenCalled()
    expect(quantidade().value).toBe('6')
  })
})
