import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  obterExpedicao, obterHistoricoDaExpedicao, listarRemessasRecentes, listarLiberacoesPendentes,
  aferir, contar, mandarParaConserto, pedirLiberacao, responderLiberacao, expedir,
  estornarAfericao, estornarContagem, estornarLiberacao, estornarRemessa, estornarRegistroDaExpedicao,
  type RegistroDaExpedicaoDto,
} from './expedicao'
import { aoMudarOLivro } from './sinalDoLivro'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi, mensagemDeErro } from './erros'
import { respostaJson } from '../testes/api'

function registro(tipo: RegistroDaExpedicaoDto['tipo'], id: number): RegistroDaExpedicaoDto {
  return {
    tipo, id, dataHora: '2026-10-20T10:00:00-03:00', usuarioId: 20, usuarioNome: 'Qualidade',
    resultado: null, conformes: null, naoConformes: null, quantidade: null, setorNome: null, texto: null,
    estornada: false, estornavel: true,
  }
}

describe('expedicao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // Caminho EXATO, com o prefixo que o `rota()` aplica: escrever `/api/...` no call site duplicaria.
  it.each([
    ['obterExpedicao', () => obterExpedicao(), '/api/expedicao'],
    ['obterHistoricoDaExpedicao', () => obterHistoricoDaExpedicao(42), '/api/estrutura/42/expedicao'],
    ['listarRemessasRecentes', () => listarRemessasRecentes(), '/api/remessas/recentes'],
    ['listarLiberacoesPendentes', () => listarLiberacoesPendentes(), '/api/liberacoes/pendentes'],
  ])('%s faz GET no caminho do contrato', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson([]))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    expect(fetchMock.mock.calls[0][0]).toBe(caminho)
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it.each([
    ['aferir', () => aferir(42, 'NaoConforme'), '/api/estrutura/42/afericoes', { resultado: 'NaoConforme' }],
    ['contar', () => contar(42, 4, 2.5), '/api/estrutura/42/contagens', { conformes: 4, naoConformes: 2.5 }],
    ['mandarParaConserto', () => mandarParaConserto(42, 2, 1), '/api/estrutura/42/consertos',
      { setorId: 2, quantidade: 1 }],
    ['pedirLiberacao', () => pedirLiberacao(42, 2, 'A aba fica fora.'), '/api/estrutura/42/pedidos-de-liberacao',
      { quantidade: 2, justificativa: 'A aba fica fora.' }],
    ['responderLiberacao', () => responderLiberacao(9, 1, null), '/api/liberacoes/9/resposta',
      { quantidadeLiberada: 1, observacao: null }],
  ])('%s faz POST do corpo do contrato', async (_nome, chamar, caminho, corpo) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual(corpo)
  })

  it('expedir manda POST /remessas com os itens e devolve remessa e avisos', async () => {
    const resultado = {
      remessa: { id: 3, linhas: [] },
      avisos: [{ pecaId: 42, tipo: 'NaoConformeSemLiberacao', quantidade: 2 }],
    }
    const fetchMock = vi.fn().mockResolvedValue(respostaJson(resultado, 201))
    vi.stubGlobal('fetch', fetchMock)
    const itens = [{ estruturaItemId: 42, quantidade: 9 }, { estruturaItemId: 43, quantidade: 1.5 }]

    const devolvido = await expedir(itens)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/remessas')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ itens })
    expect(devolvido).toEqual(resultado)
  })

  it.each([
    ['estornarAfericao', () => estornarAfericao(4), '/api/afericoes/4/estorno'],
    ['estornarContagem', () => estornarContagem(7), '/api/contagens/7/estorno'],
    ['estornarLiberacao', () => estornarLiberacao(9), '/api/liberacoes/9/estorno'],
    ['estornarRemessa', () => estornarRemessa(3), '/api/remessas/3/estorno'],
  ])('%s faz POST sem corpo na rota do registro', async (_nome, chamar, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson([], 201))
    vi.stubGlobal('fetch', fetchMock)

    await chamar()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('estornarRegistroDaExpedicao escolhe a rota pelo tipo', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(respostaJson([], 201)))
    vi.stubGlobal('fetch', fetchMock)

    await estornarRegistroDaExpedicao(registro('Afericao', 4))
    await estornarRegistroDaExpedicao(registro('Contagem', 7))
    // No `Conserto` o `id` é o da linha do livro, e o estorno é o genérico da movimentação.
    await estornarRegistroDaExpedicao(registro('Conserto', 55))
    await estornarRegistroDaExpedicao(registro('PedidoDeLiberacao', 9))
    await estornarRegistroDaExpedicao(registro('RespostaDeLiberacao', 10))
    await estornarRegistroDaExpedicao(registro('Remessa', 3))

    expect(fetchMock.mock.calls.map((c) => c[0])).toEqual([
      '/api/afericoes/4/estorno',
      '/api/contagens/7/estorno',
      '/api/movimentacoes/55/estorno',
      '/api/liberacoes/9/estorno',
      '/api/liberacoes/10/estorno',
      '/api/remessas/3/estorno',
    ])
  })

  it('falha de escrita vira ErroDeApi com o codigo', async () => {
    const frase = 'Só há 6 de D no local de expedição.'
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409)))

    const erro = await expedir([{ estruturaItemId: 42, quantidade: 9 }]).catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(409)
    expect((erro as ErroDeApi).codigo).toBe('SaldoInsuficiente')
    expect((erro as ErroDeApi).detalhe).toBe(frase)
    expect(mensagemDeErro(erro, 'x')).toBe(frase)
  })

  it('um 403 Proibido do estorno vem com o código; sem frase, a tradução diz quem pode', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'Proibido' }, 403)))

    const erro = await estornarRemessa(3).catch((e: unknown) => e)

    expect((erro as ErroDeApi).codigo).toBe('Proibido')
    expect(mensagemDeErro(erro, 'x')).toBe('Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.')
  })

  it('um 404 sem corpo útil vira ErroDeApi sem código', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))

    const erro = await obterExpedicao().catch((e: unknown) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(404)
    expect((erro as ErroDeApi).codigo).toBeUndefined()
  })

  describe('o aviso de que o livro mudou', () => {
    async function quantosAvisos(chamar: () => Promise<unknown>, resposta: Response): Promise<number> {
      const aviso = vi.fn()
      const parar = aoMudarOLivro(aviso)
      try {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(resposta))
        await chamar().catch(() => {})
      } finally {
        parar()
      }
      return aviso.mock.calls.length
    }

    it.each([
      ['contar', () => contar(42, 1, 0)],
      ['mandarParaConserto', () => mandarParaConserto(42, 2, 1)],
      ['pedirLiberacao', () => pedirLiberacao(42, 1, 'x')],
      ['responderLiberacao', () => responderLiberacao(9, 1, null)],
      ['expedir', () => expedir([{ estruturaItemId: 42, quantidade: 1 }])],
      ['estornarContagem', () => estornarContagem(7)],
      ['estornarLiberacao', () => estornarLiberacao(9)],
      ['estornarRemessa', () => estornarRemessa(3)],
    ])('%s avisa no sucesso: mexe no livro', async (_nome, chamar) => {
      expect(await quantosAvisos(chamar, respostaJson([], 201))).toBe(1)
    })

    it('uma escrita do livro avisa também no 409: o que estava na tela ficou velho', async () => {
      const conflito = respostaJson({ erro: 'SaldoInsuficiente', mensagem: 'x' }, 409)

      expect(await quantosAvisos(() => expedir([{ estruturaItemId: 42, quantidade: 1 }]), conflito)).toBe(1)
    })

    it.each([
      ['aferir', () => aferir(42, 'Conforme')],
      ['estornarAfericao', () => estornarAfericao(4)],
    ])('%s não avisa: a aferição não é movimento do livro', async (_nome, chamar) => {
      expect(await quantosAvisos(chamar, respostaJson({}, 201))).toBe(0)
      expect(await quantosAvisos(chamar, respostaJson({ erro: 'AfericaoJaRegistrada', mensagem: 'x' }, 409))).toBe(0)
    })
  })
})
