import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  darBaixa, preverBaixa, descartarAgrupamento, descartarPedido, preverDescarteDoAgrupamento,
  preverDescarteDoPedido, estornarBaixa, listarBaixasDoNo, listarBaixasDoPedido, ROTULO_DO_MOTIVO,
  type NovaBaixa,
} from './baixas'
import { aoMudarOLivro } from './sinalDoLivro'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi, mensagemDeErro } from './erros'
import { respostaJson } from '../testes/api'

const NOVA: NovaBaixa = {
  motivo: 'MortaEmProcesso', observacao: 'caiu', tudo: false, incluirTopo: false,
  origens: [{ posicao: 'NoSetor', setorId: 3, ordem: 2, quantidade: 1 }],
}

describe('baixas', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it.each([
    ['darBaixa', () => darBaixa(42, NOVA), '/api/estrutura/42/baixas', NOVA],
    ['preverBaixa', () => preverBaixa(42, NOVA), '/api/estrutura/42/baixas/previa', NOVA],
    ['descartarAgrupamento', () => descartarAgrupamento(5, 'obra cancelada'), '/api/agrupamentos/5/descarte',
      { observacao: 'obra cancelada' }],
    ['descartarPedido', () => descartarPedido(6), '/api/pedidos/6/descarte', { observacao: null }],
  ])('%s faz POST do corpo do contrato', async (_nome, chamar, caminho, corpo) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual(corpo)
  })

  it.each([
    ['preverDescarteDoAgrupamento', () => preverDescarteDoAgrupamento(5), '/api/agrupamentos/5/descarte/previa'],
    ['preverDescarteDoPedido', () => preverDescarteDoPedido(6), '/api/pedidos/6/descarte/previa'],
  ])('%s faz POST sem corpo', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ linhas: [], viramSobra: [], travaNoTopo: null }))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('estornarBaixa faz POST sem corpo e devolve os estornos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson([{ id: 1 }]))
    vi.stubGlobal('fetch', fetchMock)

    const r = await estornarBaixa(7)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/baixas/7/estorno')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
    expect(r).toEqual([{ id: 1 }])
  })

  it.each([
    ['listarBaixasDoNo', () => listarBaixasDoNo(42), '/api/estrutura/42/baixas'],
    ['listarBaixasDoPedido', () => listarBaixasDoPedido(6), '/api/pedidos/6/baixas'],
  ])('%s faz GET no caminho do contrato', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson([]))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    expect(fetchMock.mock.calls[0][0]).toBe(caminho)
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it('preverBaixa repassa o sinal de cancelamento ao fetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ linhas: [], viramSobra: [], travaNoTopo: null }))
    vi.stubGlobal('fetch', fetchMock)
    const controle = new AbortController()

    await preverBaixa(42, NOVA, controle.signal)

    expect((fetchMock.mock.calls[0][1] as RequestInit).signal).toBe(controle.signal)
  })

  it('os rótulos dos motivos são os da spec', () => {
    expect(ROTULO_DO_MOTIVO).toEqual({
      PerdaArmazem: 'Sumiu no armazém', MortaEmProcesso: 'Morreu num processo', Descarte: 'Descarte',
    })
  })

  it('a baixa e o estorno avisam o contador de Tarefas, a prévia não', async () => {
    const avisos = vi.fn()
    const sair = aoMudarOLivro(avisos)
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(respostaJson({}, 201))))

    await preverBaixa(42, NOVA)
    await preverDescarteDoPedido(6)
    expect(avisos).not.toHaveBeenCalled()

    await darBaixa(42, NOVA)
    await estornarBaixa(7)
    expect(avisos).toHaveBeenCalledTimes(2)
    sair()
  })

  it('409 da prévia vira ErroDeApi com o código e a frase do servidor', async () => {
    const frase = 'Só há 1 de D em produção.'
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409)))

    const erro = await preverBaixa(42, NOVA).catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(409)
    expect((erro as ErroDeApi).codigo).toBe('SaldoInsuficiente')
    expect(mensagemDeErro(erro, 'x')).toBe(frase)
  })
})
