// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act } from '@testing-library/react'
import { PainelDeSeparacao } from './PainelDeSeparacao'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { MaterialPendente, PendenciaDeSeparacaoDto } from '../api/separacao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const PARAFUSO: MaterialPendente = {
  materialId: 7, codigo: 'PAR-M8', descricao: 'Parafuso M8', unidadeMedida: 'UN', planejado: 10, separado: 8, pendencia: 2,
}
const ELETRODO: MaterialPendente = {
  materialId: 9, codigo: 'ELE-01', descricao: 'Eletrodo', unidadeMedida: 'KG', planejado: 1, separado: 0.5, pendencia: 0.5,
}

function pendencia(materiais: MaterialPendente[] = [PARAFUSO, ELETRODO]): PendenciaDeSeparacaoDto {
  return {
    no: { id: 30, descricao: 'Quadro', codigoDoComponente: 'QD-01', agrupamentoId: 1, agrupamentoCodigo: 'AGR-1' },
    pedido: { id: 1, numero: 'PED-1', cliente: 'Alfa', dataEntrega: '2026-10-22', atrasado: false, pausa: null },
    destino: { setorId: 4, nome: 'Solda' },
    materiais,
  }
}

const RESULTADO = { separacoes: [], materiais: [] }
const ROTA = '/api/estrutura/30/separacoes'

function corpos(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]) === ROTA)
    .map((c) => JSON.parse((c[1] as RequestInit).body as string))
}

function renderizar(p = pendencia()) {
  const aoFechar = vi.fn()
  const aoSalvar = vi.fn()
  render(<PainelDeSeparacao pendencia={p} aoFechar={aoFechar} aoSalvar={aoSalvar} />)
  return { aoFechar, aoSalvar }
}

const campo = (nome: RegExp) => screen.getByRole('textbox', { name: nome }) as HTMLInputElement
const registrar = () => screen.getByRole('button', { name: 'Registrar separação' })

describe('PainelDeSeparacao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('abre com um campo por material pendente, preenchido com a pendência', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))

    renderizar()

    const form = screen.getByRole('form', { name: 'Separar material' })
    expect(form.textContent).toContain('QD-01 — Quadro')
    expect(campo(/PAR-M8/).value).toBe('2')
    expect(campo(/ELE-01/).value).toBe('0,5')
    expect(screen.getAllByRole('textbox')).toHaveLength(2)
    // O foco inicial vai ao primeiro campo, e não ao Cancelar do cabeçalho.
    expect(document.activeElement).toBe(campo(/PAR-M8/))
  })

  it('campo acima da pendência mostra "acima do planejado em 2 UN" e ainda envia', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(RESULTADO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoSalvar, aoFechar } = renderizar()

    expect(screen.queryByText(/acima do planejado/)).toBeNull()
    fireEvent.change(campo(/PAR-M8/), { target: { value: '4' } })

    const aviso = screen.getByText('acima do planejado em 2 UN')
    // Texto neutro, não erro: nem alerta, nem campo inválido, nem cor de estado.
    expect(aviso.className).toContain('text-tinta-fraca')
    expect(aviso.className).not.toMatch(/negativo|atencao|atraso|positivo/)
    expect(campo(/PAR-M8/).getAttribute('aria-invalid')).toBe('false')
    expect(screen.queryByRole('alert')).toBeNull()

    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(aoSalvar).toHaveBeenCalledTimes(1)
    expect(corpos(fetchMock)).toEqual([{ linhas: [{ materialId: 7, quantidade: 4 }, { materialId: 9, quantidade: 0.5 }] }])
  })

  it('aceita fração com vírgula e manda número', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(RESULTADO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar(pendencia([ELETRODO]))

    fireEvent.click(registrar())
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    fireEvent.change(campo(/ELE-01/), { target: { value: '0,25' } })
    fireEvent.click(registrar())
    await waitFor(() => expect(corpos(fetchMock)).toHaveLength(2))

    expect(corpos(fetchMock)).toEqual([
      { linhas: [{ materialId: 9, quantidade: 0.5 }] },
      { linhas: [{ materialId: 9, quantidade: 0.25 }] },
    ])
  })

  it('campo vazio ou zero não vai no corpo, e tudo vazio não envia', async () => {
    const fetchMock = fetchPorRota({ [ROTA]: () => respostaJson(RESULTADO, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    fireEvent.change(campo(/PAR-M8/), { target: { value: '' } })
    fireEvent.change(campo(/ELE-01/), { target: { value: '0' } })
    expect(registrar()).toHaveProperty('disabled', true)
    // Enter num campo chega ao submit sem passar pelo botão: a guarda é do envio, não só do botão.
    fireEvent.submit(screen.getByRole('form', { name: 'Separar material' }))
    await act(async () => {})
    expect(corpos(fetchMock)).toEqual([])

    fireEvent.change(campo(/ELE-01/), { target: { value: '0,0' } })
    fireEvent.change(campo(/PAR-M8/), { target: { value: '3' } })
    fireEvent.click(registrar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    expect(corpos(fetchMock)).toEqual([{ linhas: [{ materialId: 7, quantidade: 3 }] }])
  })

  it('campo inválido mostra o erro no próprio campo e trava o envio', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    renderizar()

    fireEvent.change(campo(/PAR-M8/), { target: { value: '2,12345' } })

    expect(campo(/PAR-M8/).getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByText('Digite um número com no máximo quatro casas decimais.')).toBeTruthy()
    expect(registrar()).toHaveProperty('disabled', true)
  })

  it('envia uma vez só com dois toques', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoFechar } = renderizar()

    // Dois `submit` no mesmo quadro, antes de o React redesenhar o botão desabilitado.
    const form = screen.getByRole('form', { name: 'Separar material' })
    fireEvent.submit(form)
    fireEvent.submit(form)
    await act(async () => {})

    expect(corpos(fetchMock)).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Registrando…' })).toHaveProperty('disabled', true)

    await act(async () => { responder(respostaJson(RESULTADO, 201)) })
    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(corpos(fetchMock)).toHaveLength(1)
  })

  it('409 MaterialForaDoPlanejado mostra a frase dentro do painel e chama aoSalvar para recarregar', async () => {
    const frase = 'O material PAR-M8 não está nos materiais do item QD-01 — Quadro.'
    vi.stubGlobal('fetch', fetchPorRota({
      [ROTA]: () => respostaJson({ erro: 'MaterialForaDoPlanejado', mensagem: frase }, 409),
    }))
    const { aoSalvar, aoFechar } = renderizar()

    fireEvent.click(registrar())

    const alerta = await screen.findByRole('alert')
    expect(alerta.textContent).toBe(frase)
    expect(screen.getByRole('form', { name: 'Separar material' }).contains(alerta)).toBe(true)
    expect(aoSalvar).toHaveBeenCalledTimes(1)
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('erro que não é 409 fica no painel e não recarrega', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ [ROTA]: () => respostaJson({}, 500) }))
    const { aoSalvar, aoFechar } = renderizar()

    fireEvent.click(registrar())

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(aoSalvar).not.toHaveBeenCalled()
    expect(aoFechar).not.toHaveBeenCalled()
    // Destravou: dá para tentar de novo.
    expect(registrar()).toHaveProperty('disabled', false)
  })

  it('a pendência que muda com o painel aberto tira o material que saiu e guarda o que foi digitado', () => {
    vi.stubGlobal('fetch', fetchPorRota({}))
    const aoFechar = vi.fn()
    const aoSalvar = vi.fn()
    const { rerender } = render(<PainelDeSeparacao pendencia={pendencia()} aoFechar={aoFechar} aoSalvar={aoSalvar} />)

    fireEvent.change(campo(/ELE-01/), { target: { value: '0,75' } })
    rerender(<PainelDeSeparacao pendencia={pendencia([ELETRODO])} aoFechar={aoFechar} aoSalvar={aoSalvar} />)

    expect(screen.queryByRole('textbox', { name: /PAR-M8/ })).toBeNull()
    expect(campo(/ELE-01/).value).toBe('0,75')
  })

  it('Cancelar fica desabilitado enquanto envia', async () => {
    let responder: (r: Response) => void = () => {}
    vi.stubGlobal('fetch', fetchPorRota({ [ROTA]: () => new Promise<Response>((r) => { responder = r }) }))
    const { aoFechar } = renderizar()

    const cancelar = screen.getByRole('button', { name: 'Cancelar' })
    expect(cancelar).toHaveProperty('disabled', false)
    fireEvent.click(registrar())

    await waitFor(() => expect(cancelar).toHaveProperty('disabled', true))
    fireEvent.click(cancelar)
    expect(aoFechar).not.toHaveBeenCalled()

    await act(async () => { responder(respostaJson({ erro: 'QuantidadeInvalida' }, 400)) })
    await waitFor(() => expect(cancelar).toHaveProperty('disabled', false))
    fireEvent.click(cancelar)
    expect(aoFechar).toHaveBeenCalledTimes(1)
  })
})
