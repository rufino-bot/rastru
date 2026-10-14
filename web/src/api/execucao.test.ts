import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  obterFila, listarTarefas, contarTarefas, obterPosicoes, obterLivroDoNo, obterRoteiroDoNo,
  iniciar, terminar, iniciarEmLote, terminarEmLote, entregar, estornarMovimentacao, estornarMontagem, estornar, substituirRoteiroDoNo,
  ehConflito, pausarPedido, retomarPedido, type Estornavel,
} from './execucao'
import { aoMudarOLivro } from './sinalDoLivro'
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
    ['iniciarEmLote', () => iniciarEmLote(1, [{ estruturaItemId: 7, quantidade: 4 }, { estruturaItemId: 2, quantidade: 1 }]),
      '/api/inicios', 'POST', {
        setorId: 1, itens: [{ estruturaItemId: 7, quantidade: 4 }, { estruturaItemId: 2, quantidade: 1 }],
      }],
    ['terminarEmLote', () => terminarEmLote(1, [{ estruturaItemId: 7, ordem: 1, quantidade: 4 }]),
      '/api/terminos', 'POST', { setorId: 1, itens: [{ estruturaItemId: 7, ordem: 1, quantidade: 4 }] }],
    ['entregar', () => entregar([{
      estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4,
    }]), '/api/entregas', 'POST', {
      itens: [{
        estruturaItemId: 7, origem: { posicao: 'AguardandoColeta', setorId: 1, ordem: 1 }, quantidade: 4,
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

  const REGISTRO: Estornavel = {
    tipo: 'Termino', id: 41, quantidade: 5, usuarioId: 12, usuarioNome: 'Operador do Corte',
    dataHora: '2026-09-28T10:14:00-03:00',
  }

  it.each([
    ['um Término', REGISTRO, '/api/movimentacoes/41/estorno'],
    ['um Início', { ...REGISTRO, tipo: 'Inicio' as const }, '/api/movimentacoes/41/estorno'],
    ['o início de um pai (Montagem)', { ...REGISTRO, tipo: 'Montagem' as const, id: 9 }, '/api/montagens/9/estorno'],
  ])('estornar %s faz POST no caminho certo, sem corpo', async (_nome, registro, caminho) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await estornar(registro)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(caminho)
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
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

  it.each([
    ['com motivo', 'urgente', { motivo: 'urgente' }],
    ['sem motivo', null, { motivo: null }],
  ])('pausarPedido %s faz POST /pedidos/{id}/pausas com o motivo no corpo', async (_nome, motivo, corpo) => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}, 201))
    vi.stubGlobal('fetch', fetchMock)

    await pausarPedido(7, motivo)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/pedidos/7/pausas')
    expect(init.method).toBe('POST')
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json')
    expect(JSON.parse(init.body as string)).toEqual(corpo)
  })

  it('retomarPedido faz POST /pedidos/{id}/retomada, sem corpo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(respostaJson({}))
    vi.stubGlobal('fetch', fetchMock)

    await retomarPedido(7)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/pedidos/7/retomada')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })

  it('a recusa da pausa carrega o código e a frase do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      respostaJson({ erro: 'PedidoJaPausado', mensagem: 'O Pedido PED-9 já está pausado.' }, 409),
    ))

    const erro = await pausarPedido(9, null).catch((e: unknown) => e)

    expect(erro).toMatchObject({ status: 409, codigo: 'PedidoJaPausado', detalhe: 'O Pedido PED-9 já está pausado.' })
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

  it('listarTarefas devolve o objeto com os grupos e os Kits', async () => {
    const pai = { id: 2, descricao: 'Chassi' }
    const corpo = {
      grupos: [{ setorId: 1, setorNome: 'Corte', itens: [] }],
      kitsMontaveis: [{ pai, destino: { id: 4, nome: 'Solda' }, conjuntos: 2, filhos: [] }],
      kitsIncompletos: [{ pai, destino: { id: 4, nome: 'Solda' }, conjuntos: 0, filhos: [] }],
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson(corpo)))

    expect(await listarTarefas()).toEqual(corpo)
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

  describe('o aviso de que o livro mudou', () => {
    it('toda escrita aceita avisa quem escuta, uma vez', async () => {
      // Uma `Response` nova por chamada: o corpo de uma resposta só se lê uma vez.
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({}, 201))))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await iniciar(7, { setorId: 1, quantidade: 4 })
      await estornarMovimentacao(41)
      // PUT também: editar o Roteiro muda o destino calculado, e com ele as Tarefas.
      await substituirRoteiroDoNo(7, [1, 2])

      expect(ouvinte).toHaveBeenCalledTimes(3)
      parar()
    })

    it('lote aceito avisa quem escuta', async () => {
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson([], 201))))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await iniciarEmLote(1, [{ estruturaItemId: 7, quantidade: 4 }])
      await terminarEmLote(1, [{ estruturaItemId: 7, ordem: 1, quantidade: 4 }])

      expect(ouvinte).toHaveBeenCalledTimes(2)
      parar()
    })

    it('escrita aceita com corpo ilegível avisa assim mesmo: o servidor já gravou', async () => {
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('não é json', { status: 201 }))))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await expect(terminar(7, { setorId: 1, ordem: 1, quantidade: 4 })).rejects.toBeTruthy()

      expect(ouvinte).toHaveBeenCalledTimes(1)
      parar()
    })

    it('recusa comum não avisa: nada mudou', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'SaldoInsuficiente' }, 400)))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await expect(terminar(7, { setorId: 1, ordem: 1, quantidade: 4 })).rejects.toBeInstanceOf(ErroDeApi)

      expect(ouvinte).not.toHaveBeenCalled()
      parar()
    })

    it('409 avisa: o que estava na tela ficou velho', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ erro: 'ConflitoDeConcorrencia' }, 409)))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await expect(entregar([])).rejects.toBeInstanceOf(ErroDeApi)

      expect(ouvinte).toHaveBeenCalledTimes(1)
      parar()
    })

    it('leitura não avisa, e quem parou de escutar não é mais chamado', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({ total: 0 })))
      const ouvinte = vi.fn()
      const parar = aoMudarOLivro(ouvinte)

      await contarTarefas()
      parar()
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 201)))
      await iniciar(7, { setorId: 1, quantidade: 1 })

      expect(ouvinte).not.toHaveBeenCalled()
    })
  })
})
