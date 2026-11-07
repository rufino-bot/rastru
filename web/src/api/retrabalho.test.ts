import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  abrirRetrabalho, listarRetrabalhos, registrarReclamacao, listarReclamacoes, estornarReclamacao, naoReporBaixa,
  naoReporReclamacao, desfazerNaoRepor, preverReaproveitamento, reaproveitar,
  ROTULO_DO_MOTIVO_DE_RETRABALHO, ROTULO_DO_DESTINO, type NovoRetrabalho,
} from './retrabalho'
import { ROTULO_DO_MOTIVO } from './baixas'
import { aoMudarOLivro } from './sinalDoLivro'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi } from './erros'
import { respostaJson } from '../testes/api'

const NOVO: NovoRetrabalho = {
  perdaId: 7, reclamacaoId: null, estruturaItemId: null, quantidade: null, destino: null, setorId: null,
  numero: 'P-1020-R1', dataEntrega: '2026-12-01', motivoRetrabalho: 'Perda',
}

describe('retrabalho', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it.each([
    ['abrirRetrabalho', () => abrirRetrabalho(40, NOVO), '/api/pedidos/40/retrabalhos', NOVO],
    ['registrarReclamacao', () => registrarReclamacao(40, { estruturaItemId: 812, quantidade: 1, descricao: 'trincou' }),
      '/api/pedidos/40/reclamacoes', { estruturaItemId: 812, quantidade: 1, descricao: 'trincou' }],
    ['naoReporBaixa', () => naoReporBaixa(9, 'sem prazo'), '/api/baixas/9/dispensa', { justificativa: 'sem prazo' }],
    ['naoReporReclamacao', () => naoReporReclamacao(3, 'cliente aceitou'), '/api/reclamacoes/3/dispensa',
      { justificativa: 'cliente aceitou' }],
    ['reaproveitar', () => reaproveitar(51, { itens: [{ estruturaItemId: 77, posicao: 'AguardandoColeta', setorId: 3, ordem: 2, quantidade: 2 }] }),
      '/api/pedidos/51/sobra-reaproveitada',
      { itens: [{ estruturaItemId: 77, posicao: 'AguardandoColeta', setorId: 3, ordem: 2, quantidade: 2 }] }],
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
    ['estornarReclamacao', () => estornarReclamacao(3), '/api/reclamacoes/3/estorno'],
    ['desfazerNaoRepor', () => desfazerNaoRepor(2), '/api/dispensas/2/estorno'],
  ])('%s faz POST sem corpo', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('desfazerNaoRepor aceita o 204 sem corpo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(desfazerNaoRepor(2)).resolves.toBeUndefined()
  })

  it.each([
    ['listarRetrabalhos', () => listarRetrabalhos(40), '/api/pedidos/40/retrabalhos'],
    ['listarReclamacoes', () => listarReclamacoes(40), '/api/pedidos/40/reclamacoes'],
    ['preverReaproveitamento', () => preverReaproveitamento(51), '/api/pedidos/51/sobra-reaproveitavel'],
  ])('%s faz GET', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ itens: [] }))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit | undefined]
    expect(url).toBe(caminho)
    expect(init?.method ?? 'GET').toBe('GET')
  })

  it('preverReaproveitamento repassa o sinal de cancelamento e devolve a prévia', async () => {
    const previa = { itens: [{ estruturaItemId: 77, sugerido: 2 }] }
    const fetchMock = vi.fn().mockResolvedValue(respostaJson(previa))
    vi.stubGlobal('fetch', fetchMock)
    const controle = new AbortController()

    const r = await preverReaproveitamento(51, controle.signal)

    expect((fetchMock.mock.calls[0] as [string, RequestInit])[1].signal).toBe(controle.signal)
    expect(r).toEqual(previa)
  })

  it('as escritas avisam o contador de Tarefas e a leitura e a prévia não', async () => {
    const aviso = vi.fn()
    const parar = aoMudarOLivro(aviso)
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => respostaJson({}, 201)))

    await listarRetrabalhos(40)
    await preverReaproveitamento(51)
    expect(aviso).not.toHaveBeenCalled()

    await abrirRetrabalho(40, NOVO)
    await naoReporBaixa(9, 'x')
    expect(aviso).toHaveBeenCalledTimes(2)
    parar()
  })

  it('a recusa vira ErroDeApi com código e frase do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ erro: 'JaReposto', mensagem: 'Já tem retrabalho.' }, 409)))

    const e = await abrirRetrabalho(40, NOVO).catch((x: unknown) => x)

    expect(e).toBeInstanceOf(ErroDeApi)
    expect(e).toMatchObject({ status: 409, codigo: 'JaReposto', detalhe: 'Já tem retrabalho.' })
  })

  it('os rótulos são os da spec', () => {
    expect(ROTULO_DO_MOTIVO_DE_RETRABALHO).toEqual({
      ReprovacaoDimensional: 'Reprovação dimensional', ErroInterno: 'Erro interno',
      SolicitacaoDoCliente: 'Solicitação do cliente', Perda: 'Perda',
    })
    expect(ROTULO_DO_DESTINO).toEqual({ Consertar: 'Consertar', Refazer: 'Refazer' })
    expect(ROTULO_DO_MOTIVO.Reaproveitado).toBe('Reaproveitado')
  })
})
