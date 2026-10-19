import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  obterMateriaisDoNo, substituirMateriaisDoNo, separar, obterHistoricoDeSeparacoes, estornarSeparacao,
  listarPendenciasDeSeparacao, separarEmLote,
} from './separacao'
import { aoMudarOLivro } from './sinalDoLivro'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi, mensagemDeErro } from './erros'
import { respostaJson } from '../testes/api'

const LINHAS = [{ materialId: 7, quantidade: 2 }, { materialId: 9, quantidade: 0.5 }]

describe('separacao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // Caminho EXATO, com o prefixo que o `rota()` aplica: escrever `/api/...` no call site duplicaria.
  it.each([
    ['obterMateriaisDoNo', () => obterMateriaisDoNo(42), '/api/estrutura/42/materiais'],
    ['obterHistoricoDeSeparacoes', () => obterHistoricoDeSeparacoes(42), '/api/estrutura/42/separacoes'],
    ['listarPendenciasDeSeparacao', () => listarPendenciasDeSeparacao(), '/api/separacoes/pendencias'],
  ])('%s faz GET no caminho do contrato', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson([]))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    expect(fetchMock.mock.calls[0][0]).toBe(caminho)
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it.each([
    ['substituirMateriaisDoNo', () => substituirMateriaisDoNo(42, LINHAS), '/api/estrutura/42/materiais', 'PUT'],
    ['separar', () => separar(42, LINHAS), '/api/estrutura/42/separacoes', 'POST'],
  ])('%s manda o corpo { linhas } no verbo do contrato', async (_nome, chamar, caminho, metodo) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe(metodo)
    expect(JSON.parse(init.body as string)).toEqual({ linhas: LINHAS })
  })

  it('separarEmLote faz POST em /separacoes com o corpo { nos } e devolve uma entrada por nó', async () => {
    const resposta = [{ estruturaItemId: 42, separacoes: [], materiais: [] }]
    const fetchMock = vi.fn().mockResolvedValue(respostaJson(resposta, 201))
    vi.stubGlobal('fetch', fetchMock)
    const nos = [{ estruturaItemId: 42, linhas: LINHAS }, { estruturaItemId: 43, linhas: [LINHAS[0]] }]

    const lido = await separarEmLote(nos)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/separacoes')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ nos })
    expect(lido).toEqual(resposta)
  })

  it('substituirMateriaisDoNo com lista vazia manda { linhas: [] }, não corpo vazio', async () => {
    // `[]` limpa o planejado; corpo sem `linhas` seria recusado pelo servidor.
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}))
    vi.stubGlobal('fetch', fetchMock)

    await substituirMateriaisDoNo(42, [])

    expect((fetchMock.mock.calls[0][1] as RequestInit).body).toBe(JSON.stringify({ linhas: [] }))
  })

  it('estornarSeparacao faz POST sem corpo na rota do estorno', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({ id: 92 }, 201))
    vi.stubGlobal('fetch', fetchMock)

    await estornarSeparacao(91)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/separacoes/91/estorno')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('devolve o corpo lido no sucesso (201 do POST de separação)', async () => {
    const resultado = { separacoes: [{ id: 91 }], materiais: [{ materialId: 7 }] }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson(resultado, 201)))

    await expect(separar(42, LINHAS)).resolves.toEqual(resultado)
  })

  it('num 409 lança ErroDeApi com codigo e detalhe, e a tela mostra a frase do servidor', async () => {
    const frase = 'PAR-M8 (Parafuso M8) não está nos materiais do nó Quadro soldado.'
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ erro: 'MaterialForaDoPlanejado', mensagem: frase }, 409),
    ))

    const erro = await separar(42, LINHAS).catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(409)
    expect((erro as ErroDeApi).codigo).toBe('MaterialForaDoPlanejado')
    expect((erro as ErroDeApi).detalhe).toBe(frase)
    expect(mensagemDeErro(erro, 'x')).toBe(frase)
  })

  it('um 403 Proibido do estorno vem com o código; sem frase, a tradução diz quem pode', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'Proibido' }, 403)))

    const erro = await estornarSeparacao(91).catch((e: unknown) => e)

    expect((erro as ErroDeApi).codigo).toBe('Proibido')
    expect(mensagemDeErro(erro, 'x')).toBe('Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.')
  })

  it('um 404 sem corpo útil vira ErroDeApi sem código', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))

    const erro = await obterMateriaisDoNo(42).catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(404)
    expect((erro as ErroDeApi).codigo).toBeUndefined()
  })

  it('as escritas da separação não avisam o contador de Tarefas: não mexem no livro de movimentações', async () => {
    const aviso = vi.fn()
    const parar = aoMudarOLivro(aviso)
    try {
      vi.stubGlobal('fetch', vi.fn()
        .mockResolvedValueOnce(respostaJson({ separacoes: [], materiais: [] }, 201))
        .mockResolvedValueOnce(respostaJson({ erro: 'JaEstornada', mensagem: 'x' }, 409))
        .mockResolvedValueOnce(respostaJson({ estruturaItemId: 42, materiais: [] })))

      await separar(42, LINHAS)
      await estornarSeparacao(91).catch(() => {})
      await substituirMateriaisDoNo(42, LINHAS)

      expect(aviso).not.toHaveBeenCalled()
    } finally {
      parar()
    }
  })
})
