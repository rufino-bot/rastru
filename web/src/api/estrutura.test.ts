import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  obterEstrutura, criarPeca, acrescentarFilho, editarNo, excluirNo,
  ehConflitoDeEstrutura,
} from './estrutura'
import { inicializar, _resetParaTeste } from './client'

describe('estrutura', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // Teste 1: URL EXATA, com o prefixo aplicado pelo rota() — a guarda contra escrever /api à mão
  // no call site (o que duplicaria o prefixo). Prova por mutação: '/api/agrupamentos/...' escrito
  // direto no call site mataria este teste, porque rota() aplicaria um segundo /api.
  it('obterEstrutura chama /agrupamentos/:id/estrutura sem escrever /api à mão', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('[]', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await obterEstrutura(4)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/agrupamentos/4/estrutura')
    expect((fetchMock.mock.calls[0][1] as RequestInit | undefined)?.method).toBeUndefined()
  })

  it('obterEstrutura devolve a arvore', async () => {
    const arvore = [{
      id: 1, componenteId: 10, codigoDoComponente: 'SUP-001', descricao: 'Suporte',
      quantidade: 5, nivelHierarquico: 'Peca', requerRelatorioDimensional: true,
      materiais: [{ materialId: 3, nome: 'Chapa', quantidade: 2 }],
      roteiro: [{ setorId: 1, nome: 'Corte', ordem: 1 }],
      filhos: [],
    }]
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(arvore), { status: 200 })))

    const resultado = await obterEstrutura(4)

    expect(resultado).toEqual(arvore)
  })

  it('obterEstrutura lanca quando a resposta nao e ok', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 404 })))

    await expect(obterEstrutura(999)).rejects.toMatchObject({ name: 'ErroDeApi', status: 404 })
  })

  // Teste 2: criarPeca devolve o no criado no 201.
  it('criarPeca devolve o no criado no 201', async () => {
    const noCriado = {
      id: 7, componenteId: 10, codigoDoComponente: 'SUP-001', descricao: 'Suporte',
      quantidade: 5, nivelHierarquico: 'Peca', requerRelatorioDimensional: true,
      materiais: [], roteiro: [], filhos: [],
    }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(noCriado), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    const resultado = await criarPeca(4, { componenteId: 10, quantidade: 5, requerRelatorioDimensional: true })

    expect(resultado).toEqual(noCriado)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/agrupamentos/4/estrutura')
    expect(init.method).toBe('POST')
    expect((init.headers as Headers).get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ componenteId: 10, quantidade: 5, requerRelatorioDimensional: true }))
  })

  // Teste 3 — O PONTO da task: o 409 de ciclo carrega DOIS campos, e os dois têm de sobreviver ao
  // cliente. `erro` é o código por onde o front comuta; `mensagem` é a frase que nomeia o caminho
  // do ciclo — a única informação que permite ao operador consertar a receita. Um cliente que lesse
  // só `erro` e descartasse `mensagem` mataria, no último elo, uma cadeia que levou três rodadas de
  // review e um fix pass inteiro para existir (ver EstruturaController.Recusar e
  // MontagemDeEstruturaUseCase.PlanejarCopiaDoCatalogo). Prova por mutação: um cliente que
  // devolvesse `{ erro: corpo.erro }` sem `mensagem` mataria SÓ este teste.
  it('criarPeca devolve o codigo E a mensagem do conflito no 409', async () => {
    const corpoDoConflito = {
      erro: 'CicloNaReceita',
      mensagem: 'A receita tem um ciclo: 2 -> 3 -> 2. Corrija a receita do catalogo antes de criar a Peca.',
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(corpoDoConflito), { status: 409 })))

    const resultado = await criarPeca(4, { componenteId: 2, quantidade: 5, requerRelatorioDimensional: false })

    expect(ehConflitoDeEstrutura(resultado)).toBe(true)
    expect(ehConflitoDeEstrutura(resultado) && resultado.erro).toBe('CicloNaReceita')
    expect(ehConflitoDeEstrutura(resultado) && resultado.mensagem).toBe(
      'A receita tem um ciclo: 2 -> 3 -> 2. Corrija a receita do catalogo antes de criar a Peca.',
    )
  })

  // Ponto 1 dos "pontos que o brief nao tem como saber": PedidoNaoAberto viaja SEM mensagem — a
  // ausencia nao pode quebrar o cliente, e o cliente nao pode inventar texto no lugar.
  it('excluirNo devolve PedidoNaoAberto no 409', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'PedidoNaoAberto' }), { status: 409 }),
    ))

    expect(await excluirNo(7)).toBe('PedidoNaoAberto')
  })

  // Teste 5: o 404 e DESFECHO, nao excecao — mesmo padrao de excluirAgrupamento. Prova por mutacao:
  // trocar o `return 'NaoEncontrado'` por um `throw` mataria este teste (ele falharia porque a
  // promise rejeitaria em vez de resolver).
  it('excluirNo devolve NaoEncontrado no 404, sem lancar', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))

    await expect(excluirNo(999)).resolves.toBe('NaoEncontrado')
  })

  it('excluirNo devolve ok no 204', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    expect(await excluirNo(7)).toBe('ok')
  })

  it('excluirNo lanca quando o backend responde um status nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(excluirNo(7)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })
  })

  // Teste 6: URL EXATA de novo — mesmo motivo do teste 1, endpoint diferente (aninhado sob o PAI,
  // nao sob o Agrupamento).
  it('acrescentarFilho posta em /estrutura/:id/filhos e devolve o no criado', async () => {
    const filhoCriado = {
      id: 12, componenteId: null, codigoDoComponente: null, descricao: 'Sub-item ad-hoc',
      quantidade: 3, nivelHierarquico: 'Item', requerRelatorioDimensional: false,
      materiais: [], roteiro: [], filhos: [],
    }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(filhoCriado), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    const resultado = await acrescentarFilho(7, { componenteId: null, descricao: 'Sub-item ad-hoc', quantidade: 3 })

    expect(resultado).toEqual(filhoCriado)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/estrutura/7/filhos')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ componenteId: null, descricao: 'Sub-item ad-hoc', quantidade: 3 }))
  })

  it('acrescentarFilho devolve o conflito no 409', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'EstruturaProfundaDemais', mensagem: 'A receita passa de 20 niveis.' }), { status: 409 }),
    ))

    const resultado = await acrescentarFilho(7, { componenteId: 2, descricao: null, quantidade: 3 })

    expect(ehConflitoDeEstrutura(resultado)).toBe(true)
    expect(ehConflitoDeEstrutura(resultado) && resultado.erro).toBe('EstruturaProfundaDemais')
  })

  // Teste 7: editarNo faz PUT em /estrutura/:id e devolve o no atualizado.
  it('editarNo faz PUT em /estrutura/:id e devolve o no atualizado', async () => {
    const noEditado = {
      id: 7, componenteId: 10, codigoDoComponente: 'SUP-001', descricao: 'Suporte revisado',
      quantidade: 8, nivelHierarquico: 'Peca', requerRelatorioDimensional: true,
      materiais: [], roteiro: [], filhos: [],
    }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(noEditado), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const resultado = await editarNo(7, { descricao: 'Suporte revisado', quantidade: 8 })

    expect(resultado).toEqual(noEditado)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/estrutura/7')
    expect(init.method).toBe('PUT')
    expect(init.body).toBe(JSON.stringify({ descricao: 'Suporte revisado', quantidade: 8 }))
  })

  it('editarNo lanca quando o backend responde erro nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 400 })))

    await expect(editarNo(7, { descricao: null, quantidade: 8 })).rejects.toMatchObject({ name: 'ErroDeApi', status: 400 })
  })

  it('lanca quando o 409 nao esta no formato de conflito de estrutura', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ algo: 'inesperado' }), { status: 409 }),
    ))

    await expect(criarPeca(4, { componenteId: 2, quantidade: 5, requerRelatorioDimensional: false }))
      .rejects.toMatchObject({ name: 'ErroDeApi', status: 409 })
  })
})
