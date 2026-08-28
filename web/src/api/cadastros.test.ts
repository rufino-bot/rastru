import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito,
  listarMateriais, criarMaterial, definirAtivoMaterial,
  listarPedidos, criarPedido, obterPedido, formatarDataHora,
  listarAgrupamentos, criarAgrupamento, excluirAgrupamento,
  listarComponentes, criarComponente, definirAtivoComponente, obterComponente,
  type ConflitoDeCadastro, type PedidoDto,
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
    expect(fetchMock.mock.calls[0][0]).toBe('/api/setores?incluirInativos=false')
  })

  // Elevado ao molde do F4 (fix pass da review de branch, item A2): criarSetor e da Task 5,
  // anterior ao nascimento do F4 na Task 7, e ninguem varreu para tras — nenhum dos testes desta
  // funcao asseria URL/metodo/corpo. Prova por mutacao (antes deste teste): URL errada
  // ('/setores-mutado'), metodo errado ('PUT') e corpo errado ('nomeErrado' no lugar de 'nome')
  // cada uma matava 0/41. Elevado este teste em vez de criar um novo redundante, porque ja
  // exercitava criarSetor de ponta a ponta.
  it('devolve o conflito quando o nome ja existe inativo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(true)
    expect(ehConflito(resultado) && resultado.idExistente).toBe(7)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/setores')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ nome: 'Solda' }))
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
    expect(url).toBe('/api/setores/7/ativo')
    expect(init.method).toBe('PATCH')
    expect(init.body).toBe(JSON.stringify({ ativo: false }))
  })

  // D1/retroativo da review da Task 4: a mesma mutacao de C1 (`{ ativo }` -> `{ ativo: false }`)
  // aplicada aqui tambem sobrevivia — o unico teste desta funcao usava sempre `false`. Par
  // obrigatorio com valor OPOSTO, id diferente. A tela de Setores ja tem botao de reativar HOJE
  // (nao e so risco futuro da Task 6): sem isto, "Reativar" mandaria `{ativo:false}` e o
  // componente continuaria inativo apos o 204.
  it('definirAtivoSetor manda ativo=true quando reativando', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoSetor(9, true)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/setores/9/ativo')
    expect(init.body).toBe(JSON.stringify({ ativo: true }))
  })

  // PATCH /setores/{id}/ativo e [Authorize(Roles = "Administrador")] e o link aparece para todos
  // os perfis: o 403 e caminho esperado, e precisa LANCAR para a tela poder mostrar o erro.
  it('definirAtivoSetor lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(definirAtivoSetor(4, false)).rejects.toThrow()
  })

  // Corpo tem que ser JSON valido e nao-vazio: um corpo vazio faria o .json() de lerOuFalhar
  // lancar por conta propria (SyntaxError de parse), e o teste passaria mesmo sem a guarda
  // `if (!resp.ok) throw` — provado por mutacao na review da Task 9. Ver F6 no adendo.
  it('lanca quando a resposta e erro nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(criarSetor('Solda')).rejects.toThrow()
  })

  it('lista setores incluindo inativos quando pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([{ id: 1, nome: 'Solda', ativo: false }]), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarSetores(true)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/setores?incluirInativos=true')
  })

  // Lacuna do F4 herdada da Task 5: listarSetores tem `if (!resp.ok) throw` (cadastros.ts:40) mas
  // nunca teve teste de throw. Corpo JSON nao-vazio: ver nota em 'lanca quando a resposta e erro
  // nao tratado'.
  it('lanca quando listar setores falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(listarSetores(false)).rejects.toThrow()
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
    expect(fetchMock.mock.calls[0][0]).toBe('/api/materiais?incluirInativos=false')
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

    expect(fetchMock.mock.calls[0][0]).toBe('/api/materiais?incluirInativos=true')
  })

  // Lacuna do F4 herdada da Task 7: listarMateriais tem `if (!resp.ok) throw` (cadastros.ts:81) mas
  // nunca teve teste de throw. Corpo JSON nao-vazio: ver nota em 'lanca quando a resposta e erro
  // nao tratado'.
  it('lanca quando listar materiais falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(listarMateriais(false)).rejects.toThrow()
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
  //
  // Elevado ao molde do F4 (fix pass da review de branch, item A2): criarMaterial e da Task 6,
  // anterior ao nascimento do F4 na Task 7, e ninguem varreu para tras — nenhum teste desta
  // funcao asseria URL/metodo (o corpo ja esta coberto por 'manda os tres campos do material no
  // corpo do POST', acima). Prova por mutacao (antes deste teste): URL errada
  // ('/materiais-mutado') e metodo errado ('PUT') cada uma matava 0/41.
  it('devolve o conflito quando o codigo do material ja existe', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 4 }),
        { status: 409 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const resultado = await criarMaterial({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.campo).toBe('codigo')
    expect(ehConflito(resultado) && resultado.idExistente).toBe(4)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/materiais')
    expect(init.method).toBe('POST')
  })

  // Mesma razao do par de definirAtivoSetor: a tela chama isto em dois lugares (inativar e
  // reativar) e "nao lancou" nao provaria URL nem corpo.
  it('definirAtivoMaterial manda PATCH na rota do id com o corpo do ativo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoMaterial(4, true)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/materiais/4/ativo')
    expect(init.method).toBe('PATCH')
    expect(init.body).toBe(JSON.stringify({ ativo: true }))
  })

  // D1/retroativo da review da Task 4: mesma lacuna de definirAtivoSetor, so que aqui o unico
  // teste existente usava sempre `true` — par obrigatorio com `false`, id diferente.
  it('definirAtivoMaterial manda ativo=false quando inativando', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoMaterial(11, false)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/materiais/11/ativo')
    expect(init.body).toBe(JSON.stringify({ ativo: false }))
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
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos')
  })

  // GUARDA DE DÍVIDA — Fase 1E. NÃO é teste de comportamento novo: é o alarme que dispara no dia
  // em que `/pedidos` for paginado, como `/componentes` já foi na 1B.
  //
  // A `HomePage` deriva TRÊS coisas do array que esta função devolve: a contagem de "pedidos
  // abertos", o resumo pelos 5 status e a lista "abertos há mais tempo". As três só são verdadeiras
  // porque a resposta traz o conjunto INTEIRO. Se `listarPedidos` passar a devolver uma página, as
  // três viram meia-verdade — "contagem da primeira página", "os mais antigos dos 20 primeiros" —
  // e nenhuma delas fica vermelha sozinha, porque continuam sendo números plausíveis.
  //
  // Este teste morre de DOIS jeitos, de propósito: `Array.isArray` mata a troca do tipo de retorno
  // em tempo de execução, e a anotação de tipo da variável `contrato` mata a mesma troca em
  // `tsc -b`.
  //
  // Quando ele ficar vermelho, o conserto NÃO é apagá-lo: é decidir o que a Home passa a mostrar
  // (endpoint de resumo no backend, ou pedir `tamanho` grande explicitamente) e só então
  // reescrever esta guarda.
  it('devolve o conjunto inteiro de pedidos, nao uma pagina — a HomePage depende disso', async () => {
    const vinteECinco = Array.from({ length: 25 }, (_, i) => ({
      id: i + 1, numero: `PED-${String(i + 1).padStart(3, '0')}`, cliente: 'Cliente X',
      tipo: 'Fabricacao', status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00',
      criadoPorUsuarioId: 1,
    }))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify(vinteECinco), { status: 200 }),
    ))

    // A anotação de tipo É a guarda de compilação, e a chamada abaixo é a de runtime — as duas
    // na mesma linha, sem variável de enfeite. Se `listarPedidos` virar
    // `Promise<PaginaDe<PedidoDto>>`, esta atribuição para de compilar e `npm run build` reprova
    // (o Vitest não faz typecheck: `npm test` verde não prova que compila).
    const contrato: () => Promise<PedidoDto[]> = listarPedidos
    const pedidos = await contrato()

    // Vinte e cinco, e não vinte: 20 é o tamanho de página padrão de `listarComponentes`. Se
    // alguém paginar `/pedidos` copiando aquele default, esta asserção é a que fica vermelha.
    expect(Array.isArray(pedidos)).toBe(true)
    expect(pedidos).toHaveLength(25)
  })

  // GET /pedidos e so [Authorize] (nao role-protected), mas o par URL/erro e o molde do F4 mesmo
  // assim: uma resposta nao-ok tem que lancar, nao devolver undefined/array vazio em silencio.
  // Corpo JSON nao-vazio (nao ''): ver nota em 'lanca quando a resposta e erro nao tratado'.
  it('lanca quando listar pedidos falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

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
    expect(url).toBe('/api/pedidos')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ numero: 'PED-001', cliente: 'Cliente X' }))
  })

  // POST /pedidos e [Authorize(Roles = "PCP,Administrador")] e o link aparece para todos os
  // perfis: o 403 e caminho esperado, e precisa LANCAR para a tela poder mostrar o erro (senao o
  // catch do salvar() vira decorativo).
  // Corpo JSON nao-vazio (nao ''): ver nota em 'lanca quando a resposta e erro nao tratado'.
  it('criarPedido lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 403 })))

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
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos/9')
  })

  // Corpo JSON nao-vazio (nao ''): ver nota em 'lanca quando a resposta e erro nao tratado'.
  it('obterPedido lanca quando a resposta nao e ok', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 404 })))

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

  // Corrigido do plano (F4 / G2): o teste do brief so conferia a URL — metodo errado ou corpo
  // errado passariam verdes do mesmo jeito. Molde: 'manda os dois campos do pedido no corpo do
  // POST' (linha ~266). Prova por mutacao: 'POST' -> 'PUT' so mata este teste.
  it('manda os dois campos do agrupamento no corpo do POST, na rota aninhada do pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1, pedidoId: 4, codigo: 'AG-01', tipo: 'Kit',
          criadoEm: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarAgrupamento(4, { codigo: 'AG-01', tipo: 'Kit' })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/pedidos/4/agrupamentos')
    expect(init.method).toBe('POST')
    expect((init.headers as Headers).get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ codigo: 'AG-01', tipo: 'Kit' }))
  })

  // POST /pedidos/{pedidoId}/agrupamentos e [Authorize(Roles = "PCP,Administrador")] (G3): sem
  // este teste, se lerOuFalhar parasse de lancar, o catch da tela viraria decorativo — o mesmo
  // defeito ja achado em Setor (Task 7), Material (Task 6) e Pedido (Task 9).
  // Corpo JSON nao-vazio (nao ''): ver nota em 'lanca quando a resposta e erro nao tratado'.
  it('criarAgrupamento lanca quando o backend responde 403', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 403 })))

    await expect(criarAgrupamento(4, { codigo: 'AG-01', tipo: 'Kit' }))
      .rejects.toThrow()
  })

  it('lista os agrupamentos do pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('[]', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    expect(await listarAgrupamentos(4)).toEqual([])
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos/4/agrupamentos')
  })

  // G4: a guarda `if (!resp.ok) throw` de listarAgrupamentos nao tinha teste. Corpo JSON
  // nao-vazio (nao ''): listarAgrupamentos chama .json(), entao um corpo vazio faria o proprio
  // .json() lancar SyntaxError e o rejects.toThrow() passaria pelo motivo errado — ver F6.
  it('lanca quando listar agrupamentos falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(listarAgrupamentos(4)).rejects.toThrow()
  })

  // A1 do fix pass da review de branch: os 5 testes abaixo cobrem so a TRADUCAO dos status
  // (204/404/409/403 -> resultado) e nenhum assere para onde a chamada vai. URL errada leva a
  // 404, e excluirAgrupamento traduz 404 em 'NaoEncontrado' — a tela mostra "Este agrupamento ja
  // nao existe mais.", recarrega a lista, e o agrupamento CONTINUA la: falha silenciosa que MENTE
  // para o usuario, na unica exclusao fisica do sistema. Nao ha corpo num DELETE, entao so
  // URL + metodo. Prova por mutacao: URL errada (`/agrupamentos-mutado/${id}`) ou metodo errado
  // ('POST' no lugar de 'DELETE') cada uma matava 0/40 antes deste teste.
  it('excluirAgrupamento manda DELETE na rota do id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await excluirAgrupamento(7)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/agrupamentos/7')
    expect(init.method).toBe('DELETE')
  })

  // Ramo else/fallback de excluirAgrupamento: o 409 mais especifico (AgrupamentoNaoVazio).
  it('traduz o 409 de estrutura no codigo que a tela precisa mostrar', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'AgrupamentoNaoVazio' }), { status: 409 }),
    ))

    expect(await excluirAgrupamento(7)).toBe('AgrupamentoNaoVazio')
  })

  // G1 — o achado principal: sem este teste, trocar o ternario inteiro por
  // `return 'AgrupamentoNaoVazio'` deixava a suite verde. A ordem de guarda do Excluir no
  // backend e existe -> Pedido Aberto -> vazio, entao PedidoNaoAberto e o codigo que chega
  // PRIMEIRO na pratica: um Agrupamento com estrutura num Pedido nao Aberto responde
  // PedidoNaoAberto, nunca AgrupamentoNaoVazio. Prova por mutacao: reverter o ternario para
  // `return 'AgrupamentoNaoVazio'` mata SO este teste.
  it('traduz o 409 de pedido nao aberto no codigo que a tela precisa mostrar', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'PedidoNaoAberto' }), { status: 409 }),
    ))

    expect(await excluirAgrupamento(7)).toBe('PedidoNaoAberto')
  })

  it('trata 204 e 404 da exclusao', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 404 })))

    expect(await excluirAgrupamento(7)).toBe('ok')
    expect(await excluirAgrupamento(7)).toBe('NaoEncontrado')
  })

  // G5: o throw final de excluirAgrupamento (status fora de 204/404/409) tambem nao tinha teste.
  // Um Operador clicando "Excluir" toma 403 e cai neste caminho. Ao contrario do teste de
  // listarAgrupamentos acima, este ramo NAO chama .json() — corpo vazio e inofensivo aqui, e
  // "corrigir" o mock para ter corpo seria ruido (mesmo caso de definirAtivoSetor). Prova por
  // mutacao: remover este `throw` mata so este teste.
  it('excluirAgrupamento lanca quando o backend responde um status nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))

    await expect(excluirAgrupamento(7)).rejects.toThrow()
  })

  // Adendo F4: toda funcao nova do modulo tem DOIS testes — URL/metodo/corpo e comportamento em
  // erro. O primeiro assere `fetchMock.mock.calls[0][0]`, nao o retorno: o retorno vem do stub e
  // nao prova nada sobre o que foi pedido.
  it('monta a URL de componentes com os quatro parametros', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarComponentes({ busca: 'sup', incluirInativos: false, pagina: 2, tamanho: 50 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/componentes?busca=sup&incluirInativos=false&pagina=2&tamanho=50',
    )
  })

  // Par obrigatorio do teste acima (adendo F3): hardcodar `incluirInativos=false` na URL passaria
  // com so o caso `false`, e o checkbox "Mostrar inativos" quebraria em silencio.
  it('monta a URL de componentes incluindo inativos quando pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarComponentes({ busca: '', incluirInativos: true, pagina: 1, tamanho: 20 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/componentes?busca=&incluirInativos=true&pagina=1&tamanho=20',
    )
  })

  // I4 da review da Task 4: os dois testes acima usam 'sup' e '', ambos invariantes a encoding —
  // nenhum prova a ESCOLHA de `URLSearchParams` em vez de concatenacao crua. `busca` e input de
  // texto livre do usuario (casa em codigo ou descricao); sem escape, `&` injeta um parametro
  // falso e trunca a busca, `%` vira escape invalido (400 do ASP.NET) e `+` decodifica como
  // espaco no servidor — devolvendo o conjunto errado de resultados, sem erro nenhum.
  it('codifica caracteres especiais da busca na URL', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarComponentes({ busca: 'A&B 100%', incluirInativos: false, pagina: 1, tamanho: 20 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/componentes?busca=A%26B+100%25&incluirInativos=false&pagina=1&tamanho=20',
    )
  })

  // I3 da review da Task 4: a fixture original tinha `total: 1` com exatamente 1 item, entao
  // `total` era indistinguivel de `itens.length` — trocar o retorno por
  // `{ ...corpo, total: corpo.itens.length }` sobrevivia. `total` agora e DIFERENTE do tamanho de
  // `itens` (o invariante do doc comment de `PaginaDe<T>`: "total e sob o mesmo filtro, nao o
  // tamanho de itens"), entao so pode vir do corpo da resposta. Consequencia real se isto
  // regredir: a Task 6 calcula `Math.ceil(total / tamanho)` para a paginacao — com total
  // colapsado, o paginador mostraria "1 de 1" para sempre.
  it('devolve a pagina de componentes', async () => {
    const pagina = {
      itens: [{ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Fabricado', ativo: true }],
      total: 37,
      pagina: 1,
      tamanho: 20,
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify(pagina), { status: 200 }),
    ))

    const resultado = await listarComponentes(
      { busca: '', incluirInativos: false, pagina: 1, tamanho: 20 },
    )

    expect(resultado.total).toBe(37)
    expect(resultado.itens[0].tipo).toBe('Fabricado')
  })

  // Corpo JSON nao-vazio de proposito (adendo F6): com `''` o `.json()` lancaria sozinho e o
  // teste passaria mesmo com a guarda `if (!resp.ok) throw` removida.
  it('lanca quando listar componentes falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(
      listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 20 }),
    ).rejects.toThrow()
  })

  // `obterComponente` nasce sem chamador (a tela de detalhe e a Task 10), mas leva os dois testes
  // do F4 como qualquer funcao nova do modulo — foi por pular isso que `criarSetor`/`criarMaterial`
  // ficaram 5 tasks sem prova de URL.
  it('obterComponente busca a rota do id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ id: 7, codigo: 'SUP-007', descricao: 'Suporte', tipo: 'Montagem', ativo: false }),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const componente = await obterComponente(7)

    // O id 7 aparece na URL e no corpo: um id preso em literal quebra a primeira asserção, e um
    // GET que ignore o corpo quebra as outras. `ativo: false` de proposito — o backend responde
    // 200 para inativo, e a funcao nao pode filtrar nada. O `method` indefinido e o que ancora o
    // verbo: `apiFetch` sempre entrega um `init` (ele injeta `headers`/`credentials`), entao o
    // que distingue GET de POST aqui e a AUSENCIA de `method`.
    expect(fetchMock.mock.calls[0][0]).toBe('/api/componentes/7')
    expect((fetchMock.mock.calls[0][1] as RequestInit).method).toBeUndefined()
    expect(componente.codigo).toBe('SUP-007')
    expect(componente.tipo).toBe('Montagem')
    expect(componente.ativo).toBe(false)
  })

  // Corpo JSON nao-vazio (adendo F6): com `''` o `.json()` lancaria sozinho e o teste passaria
  // mesmo sem a guarda `if (!resp.ok) throw`.
  it('obterComponente propaga o status quando a resposta nao e ok', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 404 })))

    await expect(obterComponente(999)).rejects.toMatchObject({ name: 'ErroDeApi', status: 404 })
  })

  // I2 da review da Task 4: o estilo solto (`fetchMock.mock.calls[0][1].method`) nao dava o
  // `init` tipado na mao e nao provava Content-Type — sem ele o ASP.NET responde 415 e o botao
  // "Salvar" da tela de Componentes fica inerte. Convertido para o estilo desestruturado que o
  // resto do arquivo usa (linhas 52, 82, 219, 232, 304, 376, 424), molde de Content-Type copiado
  // de 'manda os dois campos do agrupamento...' (linha ~379).
  it('cria componente com metodo, URL, corpo e Content-Type corretos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto', ativo: true }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarComponente({ codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' })

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/componentes')
    expect(init.method).toBe('POST')
    expect((init.headers as Headers).get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' }))
  })

  // Lacuna do brief (achada por mutacao): faltava o par obrigatorio do F4 — o brief so tinha o
  // teste de URL/metodo/corpo e o de conflito, nenhum provando que um erro NAO-409 (ex.: 403 de
  // Operador batendo em POST /componentes, que e [Authorize(Roles = "Administrador,PCP")]) faz
  // criarComponente lancar. Prova por mutacao: substituir `.then(lerOuFalhar<ComponenteDto>)` por
  // `.then(r => r.json())` deixava os 53 testes verdes — nada detectava a perda do `if (!resp.ok)
  // throw` dentro de lerOuFalhar no call site de Componente. Corpo JSON nao-vazio (nao ''): ver
  // nota em 'lanca quando a resposta e erro nao tratado' (F6).
  it('criarComponente lanca quando o backend responde erro nao tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(
      criarComponente({ codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' }),
    ).rejects.toThrow()
  })

  it('devolve o conflito quando o codigo do componente ja existe inativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarComponente(
      { codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' },
    )

    expect(ehConflito(resultado)).toBe(true)
    expect((resultado as ConflitoDeCadastro).idExistente).toBe(7)
  })

  it('define ativo do componente com metodo, URL e corpo corretos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoComponente(4, false)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/componentes/4/ativo')
    expect(fetchMock.mock.calls[0][1].method).toBe('PATCH')
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ ativo: false })
  })

  // C1/I1/I2-H da review da Task 4: o teste acima so usa id=4 e ativo=false, entao um `ativo`
  // preso em `false` ou um id preso em `4` passariam verdes do mesmo jeito. Par obrigatorio com
  // id e valor DIFERENTES, mais o Content-Type que nenhum dos dois testes desta funcao provava —
  // molde do Content-Type copiado de 'manda os dois campos do agrupamento...' (linha ~379).
  // Consequencia real se isto regredir: o botao "Reativar" da Task 6 manda `true` — com o valor
  // preso em `false`, o backend gravaria Ativo=false, devolveria 204 e a tela mentiria para o
  // usuario dizendo que reativou.
  it('define ativo do componente com id e valor diferentes, e Content-Type', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoComponente(7, true)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/componentes/7/ativo')
    expect(init.method).toBe('PATCH')
    expect((init.headers as Headers).get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ ativo: true }))
  })

  // `definirAtivoComponente` bate num endpoint [Authorize(Roles)] e nao chama `.json()`, entao o
  // corpo vazio aqui e inofensivo (excecao registrada no F6). O teste existe para o `try/catch`
  // da tela (F2) nao ser decorativo: se a funcao nao lancasse, o catch nunca dispararia.
  it('lanca quando definir ativo do componente falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))
    await expect(definirAtivoComponente(4, false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))
    await expect(definirAtivoComponente(4, false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 500 })
  })

  describe('status nas falhas de API', () => {
    // Sem estes testes, trocar `resp.status` por um literal (ex.: `404`, `500`, `403`) em qualquer
    // um dos três `throw` pinados aqui passaria verde: os `rejects.toThrow()` que já existem não
    // olham o status, só o fato de ter lançado. E é o status que a tela usa para escolher a
    // mensagem. Três call sites — não três formatos de `throw`: a linha
    // `if (!resp.ok) throw new ErroDeApi(resp.status, ...)` é byte-a-byte a mesma em 10 dos 12
    // sites do arquivo, incluindo `listarSetores` (abaixo) e `definirAtivoComponente` (acima, em
    // 'lanca quando definir ativo do componente falha'); `lerOuFalhar` (criarSetor, abaixo) chega
    // nessa mesma linha, só que depois de tratar 409 à parte. A variedade real dos três sites é de
    // função: helper compartilhado com desvio de 409 (`lerOuFalhar`), GET que desserializa o corpo
    // (`listarSetores`) e PATCH que não desserializa (`definirAtivoComponente`).
    it('listarSetores propaga o status da resposta', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ erro: 'x' }), { status: 404 }),
      ))
      await expect(listarSetores(false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 404 })

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ erro: 'x' }), { status: 409 }),
      ))
      await expect(listarSetores(false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 409 })
    })

    it('lerOuFalhar propaga o status da resposta', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ erro: 'x' }), { status: 500 }),
      ))
      await expect(criarSetor('Solda')).rejects.toMatchObject({ name: 'ErroDeApi', status: 500 })

      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ erro: 'x' }), { status: 502 }),
      ))
      await expect(criarSetor('Solda')).rejects.toMatchObject({ name: 'ErroDeApi', status: 502 })
    })
  })
})
