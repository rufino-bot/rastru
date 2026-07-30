import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito,
  listarMateriais, criarMaterial, definirAtivoMaterial,
  listarPedidos, criarPedido, obterPedido, formatarDataHora,
} from './cadastros'
import { inicializar, _resetParaTeste } from './client'

describe('cadastros', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('lista setores ativos por padrao', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([{ id: 1, nome: 'Solda', ativo: true }]), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const setores = await listarSetores(false)

    expect(setores).toEqual([{ id: 1, nome: 'Solda', ativo: true }])
    expect(fetchMock.mock.calls[0][0]).toBe('/setores?incluirInativos=false')
  })

  it('devolve o conflito quando o nome ja existe inativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(true)
    expect(ehConflito(resultado) && resultado.idExistente).toBe(7)
  })

  // O ramo que decide NAO oferecer "Reativar o existente": o homonimo esta ativo, entao
  // reativar nao e opcao — a tela so avisa que o nome esta em uso.
  it('devolve o conflito com existeInativo falso quando o homonimo esta ativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: false, idExistente: 3 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(false)
  })

  // A tela chama isto em dois lugares (inativar e reativar) e e a unica chamada com PATCH.
  // Assertar so "nao lancou" nao provaria nada: URL errada passaria igual.
  it('definirAtivoSetor manda PATCH na rota do id com o corpo do ativo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoSetor(7, false)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/setores/7/ativo')
    expect(init.method).toBe('PATCH')
    expect(init.body).toBe(JSON.stringify({ ativo: false }))
  })

  // PATCH /setores/{id}/ativo e [Authorize(Roles = "Administrador")] e o link aparece para todos
  // os perfis: o 403 e caminho esperado, e precisa LANCAR para a tela poder mostrar o erro.
  it('definirAtivoSetor lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(definirAtivoSetor(4, false)).rejects.toThrow()
  })

  it('lanca quando a resposta e erro nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })))

    await expect(criarSetor('Solda')).rejects.toThrow()
  })

  it('lista setores incluindo inativos quando pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([{ id: 1, nome: 'Solda', ativo: false }]), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarSetores(true)

    expect(fetchMock.mock.calls[0][0]).toBe('/setores?incluirInativos=true')
  })

  // TraduzirResultado (ex.: futuro DELETE /agrupamentos/{id}) devolve 409 num formato pelado
  // (so { erro: "<codigo>" }, sem campo/existeInativo/idExistente) que nao e ConflitoDeCadastro.
  // lerOuFalhar precisa lancar nesse caso, nao devolver o corpo como se fosse um DTO criado.
  it('lanca quando o 409 nao esta no formato ValorDuplicado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'AgrupamentoNaoVazio' }), { status: 409 }),
    ))

    await expect(criarSetor('Solda')).rejects.toThrow()
  })

  it('lista materiais ativos por padrao', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG', ativo: true },
        ]),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const materiais = await listarMateriais(false)

    expect(materiais).toHaveLength(1)
    expect(materiais[0].unidadeMedida).toBe('KG')
    expect(fetchMock.mock.calls[0][0]).toBe('/materiais?incluirInativos=false')
  })

  // Par obrigatorio do teste acima: hardcodar `incluirInativos=false` na URL passaria com so o
  // caso `false`, e o checkbox "Mostrar inativos" quebraria em silencio. Assere a URL, nao o
  // retorno — o retorno vem do stub e nao prova nada sobre o que foi pedido.
  it('lista materiais incluindo inativos quando pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG', ativo: false },
        ]),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarMateriais(true)

    expect(fetchMock.mock.calls[0][0]).toBe('/materiais?incluirInativos=true')
  })

  it('manda os tres campos do material no corpo do POST', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ id: 5, codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG', ativo: true }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarMaterial({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })

    const corpo = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(corpo).toEqual({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })
  })

  // UQ_Material_Codigo e sobre Codigo, entao o 409 vem com campo "codigo" — e e pelo codigo que a
  // tela oferece reativar o inativo. Descricao duplicada nao e conflito.
  it('devolve o conflito quando o codigo do material ja existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 4 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarMaterial({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.campo).toBe('codigo')
    expect(ehConflito(resultado) && resultado.idExistente).toBe(4)
  })

  // Mesma razao do par de definirAtivoSetor: a tela chama isto em dois lugares (inativar e
  // reativar) e "nao lancou" nao provaria URL nem corpo.
  it('definirAtivoMaterial manda PATCH na rota do id com o corpo do ativo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoMaterial(4, true)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/materiais/4/ativo')
    expect(init.method).toBe('PATCH')
    expect(init.body).toBe(JSON.stringify({ ativo: true }))
  })

  // PATCH /materiais/{id}/ativo e [Authorize(Roles = "Administrador")] e o link aparece para todos
  // os perfis: o 403 e caminho esperado, e precisa LANCAR para a tela poder mostrar o erro.
  it('definirAtivoMaterial lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(definirAtivoMaterial(4, false)).rejects.toThrow()
  })

  it('lista pedidos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([{
          id: 1, numero: 'PED-001', cliente: 'Cliente X', tipo: 'Fabricacao',
          status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }]),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const pedidos = await listarPedidos()

    expect(pedidos[0].numero).toBe('PED-001')
    expect(fetchMock.mock.calls[0][0]).toBe('/pedidos')
  })

  // GET /pedidos e so [Authorize] (nao role-protected), mas o par URL/erro e o molde do F4 mesmo
  // assim: uma resposta nao-ok tem que lancar, nao devolver undefined/array vazio em silencio.
  it('lanca quando listar pedidos falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })))

    await expect(listarPedidos()).rejects.toThrow()
  })

  it('devolve o conflito quando o numero do pedido ja existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 3 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarPedido({ numero: 'PED-001', cliente: 'Cliente X' })

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(false)
  })

  // Molde de 'manda os tres campos do material no corpo do POST' (linha 146): sem isto, um POST
  // na URL errada, com metodo errado ou com corpo errado passaria verde so com o teste de 409
  // acima, que devolve 409 independente dos argumentos da chamada.
  it('manda os dois campos do pedido no corpo do POST', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1, numero: 'PED-001', cliente: 'Cliente X', tipo: 'Fabricacao',
          status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarPedido({ numero: 'PED-001', cliente: 'Cliente X' })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/pedidos')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ numero: 'PED-001', cliente: 'Cliente X' }))
  })

  // POST /pedidos e [Authorize(Roles = "PCP,Administrador")] e o link aparece para todos os
  // perfis: o 403 e caminho esperado, e precisa LANCAR para a tela poder mostrar o erro (senao o
  // catch do salvar() vira decorativo).
  it('criarPedido lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(criarPedido({ numero: 'PED-001', cliente: 'Cliente X' })).rejects.toThrow()
  })

  // obterPedido nasce nesta task sem chamador (a tela de detalhe e da Task 11), mas ainda precisa
  // dos dois testes do F4 como qualquer funcao nova do modulo.
  it('obterPedido busca a rota do id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 9, numero: 'PED-009', cliente: 'Cliente Y', tipo: 'Fabricacao',
          status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const pedido = await obterPedido(9)

    expect(pedido.numero).toBe('PED-009')
    expect(fetchMock.mock.calls[0][0]).toBe('/pedidos/9')
  })

  it('obterPedido lanca quando a resposta nao e ok', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })))

    await expect(obterPedido(999)).rejects.toThrow()
  })

  it('formata a data no fuso que a API entregou, sem reconverter pelo aparelho', () => {
    expect(formatarDataHora('2026-07-28T09:30:00-03:00')).toBe('28/07/2026 09:30')
  })

  // O wire real do HorarioDeBrasiliaJsonConverter: um DateTimeOffset serializado pelo
  // System.Text.Json sai COM fracoes de segundo (DataAbertura nasce de UtcNow/SYSUTCDATETIME(),
  // ambos com 7 digitos fracionarios). O caso acima sozinho nao prova que a funcao aguenta o
  // formato de verdade.
  it('formata a data mesmo com fracoes de segundo no ISO', () => {
    expect(formatarDataHora('2026-07-28T09:30:00.1234567-03:00')).toBe('28/07/2026 09:30')
  })
})
