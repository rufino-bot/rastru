import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  obterFila, listarTarefas, contarTarefas, obterPosicoes, obterLivroDoNo, obterRoteiroDoNo,
  iniciar, terminar, montar, entregar, estornarMovimentacao, estornarMontagem, substituirRoteiroDoNo,
  ehConflito,
} from './execucao'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi, mensagemDeErro } from './erros'
import { respostaJson } from '../testes/api'

describe('execucao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // Caminho EXATO, com o prefixo que o `rota()` aplica: escrever `/api/...` no call site duplicaria.
  it.each([
    ['obterFila', () => obterFila(3), '/api/setores/3/fila'],
    ['listarTarefas', () => listarTarefas(), '/api/tarefas'],
    ['obterPosicoes', () => obterPosicoes(21), '/api/agrupamentos/21/posicoes'],
    ['obterLivroDoNo', () => obterLivroDoNo(7), '/api/estrutura/7/movimentacoes'],
    ['obterRoteiroDoNo', () => obterRoteiroDoNo(7), '/api/estrutura/7/roteiro'],
  ])('%s faz GET no caminho do contrato', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    expect(fetchMock.mock.calls[0][0]).toBe(caminho)
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it.each([
    ['iniciar', () => iniciar(7, { setorId: 1, quantidade: 4 }), '/api/estrutura/7/inicios', 'POST',
      { setorId: 1, quantidade: 4 }],
    ['terminar', () => terminar(7, { setorId: 1, ordem: 2, quantidade: 4 }), '/api/estrutura/7/terminos', 'POST',
      { setorId: 1, ordem: 2, quantidade: 4 }],
    ['montar', () => montar(2, { setorId: 4, quantidade: 2 }), '/api/estrutura/2/montagens', 'POST',
      { setorId: 4, quantidade: 2 }],
    ['entregar', () => entregar([{
      estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 },
      destinoSetorId: null, quantidade: 4,
    }]), '/api/entregas', 'POST', {
      itens: [{
        estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 },
        destinoSetorId: null, quantidade: 4,
      }],
    }],
    ['substituirRoteiroDoNo', () => substituirRoteiroDoNo(7, [1, 3, 1]), '/api/estrutura/7/roteiro', 'PUT',
      { passos: [1, 3, 1] }],
  ])('%s envia o corpo do contrato', async (_nome, chamar, caminho, metodo, corpo) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe(metodo)
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
    expect(JSON.parse(init.body as string)).toEqual(corpo)
  })

  it.each([
    ['estornarMovimentacao', () => estornarMovimentacao(41), '/api/movimentacoes/41/estorno'],
    ['estornarMontagem', () => estornarMontagem(5), '/api/montagens/5/estorno'],
  ])('%s faz POST sem corpo', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('contarTarefas devolve só o número', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ total: 3 })))

    expect(await contarTarefas()).toBe(3)
  })

  it('a recusa carrega o código e a frase do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ erro: 'SaldoInsuficiente', mensagem: 'Só há 6 de Suporte no Corte (passo 2).' }, 409),
    ))

    const erro = await iniciar(7, { setorId: 1, quantidade: 9 }).catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect(erro).toMatchObject({ status: 409, codigo: 'SaldoInsuficiente', detalhe: 'Só há 6 de Suporte no Corte (passo 2).' })
    // A frase do servidor é o que a tela mostra — o front não reconstrói "Só há 6 de ...".
    expect(mensagemDeErro(erro, 'x')).toBe('Só há 6 de Suporte no Corte (passo 2).')
  })

  it('a recusa sem frase cai na tradução do código', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'JaEstornado' }, 409)))

    const erro = await estornarMovimentacao(41).catch((e: unknown) => e)

    expect(mensagemDeErro(erro, 'x')).toBe('Este registro já foi estornado.')
  })

  it('o 404 com ProblemDetails no corpo não vira código', async () => {
    // O `NotFound()` do ASP.NET pode pôr `{ type, title, status }` no corpo (Global Constraints do
    // plano 2): nenhum dos dois campos existe ali, então não há código nem frase inventados.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5', title: 'Not Found', status: 404 }, 404),
    ))

    const erro = await obterFila(99).catch((e: unknown) => e)

    expect(erro).toMatchObject({ status: 404, codigo: undefined, detalhe: undefined })
    expect(mensagemDeErro(erro, 'x')).toBe('Este registro não existe mais.')
  })

  it('corpo que não é JSON não substitui o erro original', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>gateway</html>', { status: 502 })))

    await expect(listarTarefas()).rejects.toMatchObject({ name: 'ErroDeApi', status: 502 })
  })

  it('ehConflito reconhece só o 409 da API', () => {
    expect(ehConflito(new ErroDeApi(409, 'x'))).toBe(true)
    expect(ehConflito(new ErroDeApi(400, 'x'))).toBe(false)
    expect(ehConflito(new TypeError('Failed to fetch'))).toBe(false)
  })
})
