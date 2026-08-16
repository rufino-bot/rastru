// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within, cleanup, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ComponentesPage } from './ComponentesPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

afterEach(cleanup)

/**
 * Uma resposta NOVA a cada chamada. Isto nao e detalhe: `Response` tem corpo de uso unico, e
 * `mockResolvedValue(paginaComTotal(...))` devolveria a MESMA instancia em todo fetch — o segundo
 * `.json()` estouraria "Body has already been read". Os testes desta tela chamam a API varias
 * vezes (carga inicial, troca de pagina, troca de busca), entao o mock e `mockImplementation`.
 */
function paginaComTotal(total: number, pagina = 1, tamanho = 20) {
  return new Response(
    JSON.stringify({
      itens: [{ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Fabricado', ativo: true }],
      total,
      pagina,
      tamanho,
    }),
    { status: 200 },
  )
}

// A busca agora e debounced (useBuscaPaginada). So os testes que digitam nela precisam disto —
// ligar timers falsos no arquivo inteiro obrigaria reescrever os ~30 que nao tem nada com
// debounce. `vi.useFakeTimers()` fica dentro do `it`, e `vi.useRealTimers()` no `afterEach` que
// ja existe, para nao vazar para o teste seguinte.
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}

describe('ComponentesPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers() })

  // I1 (achado da review de branco da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('lista os componentes que a API devolveu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('SUP-001')).toBeTruthy()
  })

  // O reset de pagina ao trocar o tamanho agora vive no hook (useBuscaPaginada.test.tsx prova
  // isso). O que fica aqui, e so aqui, e a integracao: e o unico ancoradouro do <select> "Por
  // pagina" nesta tela — sem ele, o select pode ficar inerte (onChange sem efeito) sem que nada
  // quebre. Matador da M14 (D8 do pre-flight da Task 9B).
  it('atualiza a URL de tamanho mesmo ja estando na pagina 1', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.change(screen.getByLabelText('Por página'), { target: { value: '50' } })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('tamanho=50')
    })
  })

  // Adaptado ao comportamento real de `ControlesDePaginacao` (colisao registrada no brief da
  // Task 9B): `if (totalDePaginas <= 1) return null` — com total 1 e tamanho 20 existe UMA
  // pagina, entao a primitiva nao renderiza NENHUM botao, nao um par de botoes desabilitados.
  // A propriedade "nao aparece com uma pagina so" ja tem dono proprio
  // (`ControlesDePaginacao.test.tsx:56`); o que este teste prova e que ESTA tela, com um total
  // que cabe numa pagina so, realmente chega nesse estado — nenhuma navegacao de paginacao visivel.
  it('não mostra nenhum botão de paginação quando há uma página só', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    expect(screen.queryByRole('navigation', { name: 'Paginação' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Anterior' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Próxima' })).toBeNull()
  })

  it('mostra o total e a contagem de paginas', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(41))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    // 41 itens em paginas de 20 = 3 paginas (arredonda para cima, nao para baixo).
    expect(await screen.findByText('Página 1 de 3 — 41 no total')).toBeTruthy()
  })

  it('oferece reativar quando o codigo colide com um componente inativo', async () => {
    // O 409 de POST /componentes e Duplicado-backed (formato ConflitoDeCadastro), diferente do
    // 409 pelado de PATCH /{id}/ativo. Aqui o fetch alterna: a carga inicial (GET) devolve pagina
    // vazia, e o POST do formulario devolve o conflito com existeInativo=true.
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    expect(await screen.findByRole('button', { name: 'Reativar o existente' })).toBeTruthy()
    expect(screen.getByText('Já existe um componente com o código "SUP-001" inativo.')).toBeTruthy()
  })

  // A tela tem DOIS chamadores de definirAtivoComponente com valores OPOSTOS: "Inativar" manda
  // !componente.ativo, "Reativar o existente" manda o literal true. Um teste so em cima do outro
  // nao discrimina os dois call sites — se algum deles for trocado pelo valor do outro, o mutante
  // so morre se cada um tiver prova propria. Este cobre o botao "Inativar" (item ativo).
  it('Inativar manda ativo=false para um componente ativo', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))

    await waitFor(() => {
      const chamada = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH')
      expect(chamada).toBeTruthy()
      const [url, init] = chamada as [string, RequestInit]
      expect(url).toContain('/componentes/1/ativo')
      expect(JSON.parse(init.body as string)).toEqual({ ativo: false })
    })
  })

  // V3 (achado do coordenador, varredura das recargas): o teste acima para em asserir o `PATCH` e
  // nunca olha o que vem DEPOIS dele — a mesma metade que faltava no `salvar` antes do teste de
  // 201 (C2). Sem o `await carregar(...)` de `alternarAtivo`, o backend inativa de verdade mas a
  // lista nunca recarrega: o item continua na tela como se nada tivesse acontecido. A prova tem
  // que discriminar de a carga inicial (que tambem e um GET) do reload — por isso confere que ha
  // um GET DEPOIS do indice do PATCH na lista de chamadas do mock, nao so que "houve algum GET".
  it('recarrega a lista depois de alternar o ativo com sucesso', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))

    await waitFor(() => {
      const indicePatch = fetchMock.mock.calls.findIndex(
        ([, init]) => (init as RequestInit | undefined)?.method === 'PATCH',
      )
      expect(indicePatch).toBeGreaterThanOrEqual(0)
      const houveGetDepoisDoPatch = fetchMock.mock.calls
        .slice(indicePatch + 1)
        .some(([, init]) => (init as RequestInit | undefined)?.method === undefined)
      expect(houveGetDepoisDoPatch).toBe(true)
    })
  })

  // Contraparte do teste acima: "Reativar o existente" (o botao que aparece apos o 409 de
  // conflito) manda o literal true, chamada distinta de "Inativar". Se o valor deste call site
  // fosse trocado por false, o backend responderia 204 e a tela recarregaria mostrando o
  // componente ainda inativo — mentira silenciosa que so este teste pega.
  it('Reativar o existente manda ativo=true', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      const chamada = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PATCH')
      expect(chamada).toBeTruthy()
      const [url, init] = chamada as [string, RequestInit]
      expect(url).toContain('/componentes/7/ativo')
      expect(JSON.parse(init.body as string)).toEqual({ ativo: true })
    })
  })

  // V2 (achado da varredura do controlador, fora da lista original): o `setIdReativavel(null)` de
  // DENTRO de `reativar` (linha 116) nao tinha prova — diferente do `setIdReativavel(null)` do
  // INICIO de `salvar`, que ja tem teste proprio ("cadastro com sucesso apos um conflito anterior
  // esconde o botao Reativar o existente", chamado quando o usuario troca de codigo e cadastra de
  // novo). Este cobre o OUTRO call site: clicar no proprio botao "Reativar o existente" e ver o
  // PATCH suceder. Sem o reset, o botao continua na tela apontando para o mesmo idExistente, e o
  // usuario nao sabe se a acao funcionou.
  it('reativar com sucesso esconde o botao Reativar o existente', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Reativar o existente' })).toBeNull()
    })
  })

  // V4 (achado do coordenador, varredura das recargas): mesma lacuna do V3, no outro call site de
  // `carregar`. Sem o `await carregar(...)` de `reativar`, o PATCH tem sucesso, o formulario limpa
  // e o botao some — mas o componente reativado nunca aparece na lista, porque ela nao recarregou.
  // Mesma discriminacao do V3: GET DEPOIS do indice do PATCH, nao so "algum GET".
  it('recarrega a lista depois de reativar com sucesso', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      const indicePatch = fetchMock.mock.calls.findIndex(
        ([, init]) => (init as RequestInit | undefined)?.method === 'PATCH',
      )
      expect(indicePatch).toBeGreaterThanOrEqual(0)
      const houveGetDepoisDoPatch = fetchMock.mock.calls
        .slice(indicePatch + 1)
        .some(([, init]) => (init as RequestInit | undefined)?.method === undefined)
      expect(houveGetDepoisDoPatch).toBe(true)
    })
  })

  // Achado proprio da varredura final (pos-await ainda sem prova): o `try/catch` de `reativar` e
  // um TERCEIRO call site da familia do F2/I6 — distinto do de `alternarAtivo` (ja fechado) e do
  // de `carregar` (ja fechado) — e nao tinha teste nenhum. `PATCH /componentes/{id}/ativo` e
  // `[Authorize(Roles = "Administrador,PCP")]` tambem no caminho de reativar; sem esta prova, um
  // 403 ali (ou uma queda de rede) deixaria a promise rejeitada sem tratamento, e clicar em
  // "Reativar o existente" nao diria nada ao usuario. Esvaziar o `catch` (`grep -c "Não foi
  // possível reativar o componente"` 1→0) deixava a suite 31/31 verde antes deste teste.
  it('mostra mensagem de erro quando reativar falha', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response('{}', { status: 403 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  // Achado proprio da varredura final (pos-await ainda sem prova): o `try/catch` de `salvar`
  // (linhas 93-94) e o QUARTO call site desta familia — distinto do 409 de conflito (que e um
  // retorno tratado, nao uma excecao) e dos try/catch de `alternarAtivo`/`reativar`/`carregar` (ja
  // fechados). Nao tinha teste nenhum: os unicos dois testes que submetem o formulario mockavam
  // 409 (que NAO lanca) ou 201 (sucesso). Um 500 de verdade em `POST /componentes` faz
  // `criarComponente` lancar (via `lerOuFalhar`), e sem este catch a promise ficaria rejeitada sem
  // tratamento. Esvaziar o `catch` (`grep -c "Não foi possível salvar o componente"` 1→0) deixava
  // a suite 32/32 verde antes deste teste.
  it('mostra mensagem de erro quando salvar falha com um erro que nao e conflito', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') return Promise.resolve(new Response('{}', { status: 500 }))
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'NOV-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Novo' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
  })

  // Mata a mutacao de trocar `incluirInativos` pelo literal oposto (ex.: sempre false): sem este
  // teste, o checkbox "Mostrar inativos" poderia parar de afetar a URL em silencio.
  it('inclui incluirInativos=true na URL quando o checkbox e marcado', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('incluirInativos=true')
      expect(ultima).toContain('pagina=1')
    })
  })

  // C1 + C2 (review, Task 6 fix pass): fecha os dois de uma vez. Ate aqui os unicos dois testes que
  // disparam POST mockavam 409 — nenhum provava nem o CORPO do POST nem o caminho de SUCESSO.
  // O <select> de Tipo e mudado para um valor DIFERENTE do default ('Fabricado'), senao a mutacao
  // `criarComponente({ ...form, tipo: 'Fabricado' })` sobreviveria por coincidencia com o form vazio.
  it('cadastra com sucesso: envia o corpo digitado (inclusive o tipo escolhido), limpa o formulario e recarrega a lista', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ id: 9, codigo: 'MON-001', descricao: 'Montagem X', tipo: 'Montagem', ativo: true }),
          { status: 201 },
        ))
      }
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'MON-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Montagem X' } })
    fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'Montagem' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    await waitFor(() => {
      const chamada = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'POST')
      expect(chamada).toBeTruthy()
      const [, init] = chamada as [string, RequestInit]
      expect(JSON.parse(init.body as string)).toEqual({
        codigo: 'MON-001', descricao: 'Montagem X', tipo: 'Montagem',
      })
    })

    // formulario voltou ao vazio (o default de Tipo, 'Fabricado', reaparece no select)
    await waitFor(() => {
      expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
      expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
      expect((screen.getByLabelText('Tipo') as HTMLSelectElement).value).toBe('Fabricado')
    })

    // a lista recarregou: houve um GET depois do POST
    const gets = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method !== 'POST')
    expect(gets.length).toBeGreaterThanOrEqual(2)
  })

  // I4: "Anterior" nunca era clicado por teste nenhum — so se provava que ele fica `disabled` na
  // pagina 1, nunca o que ele FAZ quando habilitado. Vai para a pagina 2 e volta.
  it('Anterior volta para a pagina anterior quando habilitado', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Anterior' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=1')
    })
  })

  // I5: so existia teste para existeInativo=true. Sem cobertura para false a mensagem generica e a
  // AUSENCIA do botao "Reativar o existente" — a metade que diz ao usuario "escolha outro codigo" —
  // ficam sem rede.
  it('conflito com codigo ja ATIVO mostra mensagem generica e nao oferece reativar', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 7 }),
          { status: 409 },
        ))
      }
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    expect(await screen.findByText('Já existe um componente com este código.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Reativar o existente' })).toBeNull()
  })

  // V1 (achado da varredura do controlador, fora da lista original): o `return` que fecha o ramo
  // de conflito em `salvar` nao tinha prova. Sem ele, um 409 cai direto no ramo de SUCESSO logo
  // abaixo: o formulario e limpo e a lista recarrega por cima da mensagem de erro — o usuario ve o
  // erro e perde tudo que digitou. O teste de sucesso (201) prova o lado de dentro do ramo de
  // sucesso; os testes de 409 provam a mensagem; nenhum, ate aqui, provava que os dois ramos NAO se
  // misturam.
  it('conflito no cadastro nao limpa o formulario nem recarrega a lista', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 7 }),
          { status: 409 },
        ))
      }
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const chamadasAntesDoEnvio = fetchMock.mock.calls.length

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    await screen.findByText('Já existe um componente com este código.')

    // o formulario continua com o que foi digitado — nao foi limpo pelo ramo de sucesso
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('SUP-001')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('Suporte')

    // so o POST foi disparado depois da carga inicial — nenhum GET extra (a lista nao recarregou)
    expect(fetchMock.mock.calls.length).toBe(chamadasAntesDoEnvio + 1)
  })

  // I6, metade 1: falha no PATCH de alternarAtivo. Sem o try/catch a promise rejeitada nao teria
  // tratamento e a tela nao diria nada (o bug que o F2 existe para impedir — ex.: 403 de um
  // Operador sem permissao).
  it('mostra mensagem de erro quando alternar o ativo falha', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') return Promise.resolve(new Response('{}', { status: 403 }))
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  // I6, metade 2: falha no GET da carga. Sem o setErro do catch, uma queda de wifi durante a carga
  // produz uma lista vazia MUDA — indistinguivel de "nao ha componentes cadastrados".
  it('mostra mensagem de erro quando a carga da lista falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 500 }))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
  })

  // I7: nenhum teste renderizava um componente INATIVO — nem o rotulo condicional do botao
  // ("Reativar" em vez de "Inativar"), nem a classe de riscado. Sem isso, o item inativo aparece
  // igual ao ativo e o botao "Inativar" nele na verdade REATIVA (manda !ativo = true).
  it('exibe um componente inativo com o botao Reativar e o texto riscado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(
      JSON.stringify({
        itens: [{ id: 3, codigo: 'INA-001', descricao: 'Inativo', tipo: 'Bruto', ativo: false }],
        total: 1, pagina: 1, tamanho: 20,
      }),
      { status: 200 },
    ))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('INA-001')

    expect(screen.getByRole('button', { name: 'Reativar' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Inativar' })).toBeNull()
    // `closest('span')` devolve o proprio span do codigo (monoespacado, peso semibold); o
    // `line-through` mora no span PAI, o wrapper que o ItemDeCadastro poe em volta de children.
    expect(screen.getByText('INA-001').closest('span')?.parentElement?.className).toContain('line-through')
  })

  // Minor: reset de `erro`/`idReativavel` no inicio de `salvar`. Sem ele, depois de um 409 com
  // existeInativo o usuario troca o codigo e cadastra com sucesso — e o botao "Reativar o
  // existente" continua na tela, apontando para o idExistente ANTIGO.
  it('cadastro com sucesso apos um conflito anterior esconde o botao Reativar o existente', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        const corpo = JSON.parse((init.body as string) ?? '{}') as { codigo?: string }
        if (corpo.codigo === 'SUP-001') {
          return Promise.resolve(new Response(
            JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
            { status: 409 },
          ))
        }
        return Promise.resolve(new Response(
          JSON.stringify({ id: 9, codigo: corpo.codigo, descricao: 'Novo', tipo: 'Fabricado', ativo: true }),
          { status: 201 },
        ))
      }
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))
    await screen.findByRole('button', { name: 'Reativar o existente' })

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-002' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Outro' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    await waitFor(() => {
      expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
    })
    expect(screen.queryByRole('button', { name: 'Reativar o existente' })).toBeNull()
  })

  // Achado fora da lista do brief, por mutacao propria (instrucao final do brief): o `setErro(null)`
  // do caminho de SUCESSO de `alternarAtivo` (linha 105) nao tinha teste — removendo-o a suite
  // inteira seguia verde. Sem ele, uma falha (ex.: 403 de um Operador) deixa a mensagem de erro na
  // tela; se o MESMO usuario (ou outro com permissao) tentar de novo e conseguir, a mensagem velha
  // continua exibida por cima de uma acao que na verdade funcionou.
  it('limpa uma mensagem de erro anterior quando alternar o ativo tem sucesso depois de uma falha', async () => {
    let chamadasPatch = 0
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') {
        chamadasPatch += 1
        if (chamadasPatch === 1) return Promise.resolve(new Response('{}', { status: 403 }))
        return Promise.resolve(new Response(null, { status: 204 }))
      }
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))
    await screen.findByText('Seu perfil não tem permissão para esta ação.')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))

    await waitFor(() => {
      expect(screen.queryByText('Seu perfil não tem permissão para esta ação.')).toBeNull()
    })
  })

  // Sweep A (varredura pedida pelo coordenador apos V1/V2): o `setErro(null)` do INICIO de
  // `salvar` (linha 77) e distinto do `setIdReativavel(null)` vizinho (esse ja tinha teste, ver o
  // Minor "cadastro com sucesso apos um conflito anterior..." acima) — mutando so este,
  // isoladamente, a suite seguia 26/26 verde. Sem ele, uma mensagem de erro de uma acao ANTERIOR
  // (ex.: um "Inativar" que deu 403) continua na tela por cima de um cadastro que acabou de dar
  // certo.
  it('cadastro com sucesso limpa uma mensagem de erro anterior de outra acao', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'PATCH') return Promise.resolve(new Response('{}', { status: 403 }))
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ id: 9, codigo: 'NOV-001', descricao: 'Novo', tipo: 'Fabricado', ativo: true }),
          { status: 201 },
        ))
      }
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))
    await screen.findByText('Seu perfil não tem permissão para esta ação.')

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'NOV-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Novo' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    await waitFor(() => {
      expect(screen.queryByText('Seu perfil não tem permissão para esta ação.')).toBeNull()
    })
  })

  // Sweep C (mesma varredura): o `setErro(null)` de DENTRO de `reativar` (linha 115) e distinto do
  // `setErro(null)` de `alternarAtivo` (ja fechado como achado proprio) e do `setIdReativavel(null)`
  // vizinho (V2, acima) — mutando so este, isoladamente, a suite seguia 26/26 verde. Sem ele, a
  // mensagem de conflito ("Já existe um componente com o código... inativo.") continua na tela
  // depois de o usuario clicar "Reativar o existente" e a acao dar certo.
  it('reativar com sucesso limpa a mensagem de conflito', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))
    await screen.findByText('Já existe um componente com o código "SUP-001" inativo.')

    fireEvent.click(screen.getByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      expect(screen.queryByText('Já existe um componente com o código "SUP-001" inativo.')).toBeNull()
    })
  })

  // I1 (achado da review de branch): `carregar` escreve `erro` no `catch` mas nunca o limpa no
  // caminho de sucesso. A carga inicial falha (mensagem na tela); a busca dispara uma recarga que
  // da certo — a lista nova tem que aparecer E a mensagem de erro tem que sumir. As outras funcoes
  // da tela (salvar/alternarAtivo/reativar) ja fazem isso; carregar era a unica que faltava.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    // Digita na busca: precisa de timers falsos e do `avancar` desde a 9B (a busca e debounced
    // agora). MEDIDO: este teste passava sem timers falsos, custando ~330-450 ms de relogio real —
    // o `waitFor`/`findBy*` tem timeout padrao de 1000 ms > 300 ms do debounce, entao passava por
    // sorte, nao por prova.
    vi.useFakeTimers()
    let chamadas = 0
    const fetchMock = vi.fn().mockImplementation(() => {
      chamadas += 1
      if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
      return Promise.resolve(paginaComTotal(1))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await avancar(0)
    expect(screen.getByText('Não foi possível carregar os componentes.')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })
    await avancar(300)

    expect(screen.getByText('SUP-001')).toBeTruthy()
    expect(screen.queryByText('Não foi possível carregar os componentes.')).toBeNull()
  })

  // Sweep D (mesma varredura): o `setForm(FORMULARIO_VAZIO)` de DENTRO de `reativar` (linha 117) e
  // distinto do `setForm(FORMULARIO_VAZIO)` do sucesso de `salvar` (ja provado pelo teste de
  // cadastro 201) — mutando so este, isoladamente, a suite seguia 26/26 verde. Sem ele, depois de
  // "Reativar o existente" dar certo o formulario continua com o que foi digitado para o cadastro
  // que colidiu — campos preenchidos com um codigo que nao tem nada a ver com o componente que
  // acabou de ser reativado.
  it('reativar com sucesso limpa o formulario', async () => {
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
          { status: 409 },
        ))
      }
      if (init?.method === 'PATCH') return Promise.resolve(new Response(null, { status: 204 }))
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))
    await screen.findByRole('button', { name: 'Reativar o existente' })

    fireEvent.click(screen.getByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
      expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
    })
  })

  it('manda o formulário inteiro no POST, com o tipo escolhido', async () => {
    // C1 da review da Task 6: `criarComponente(form)` -> `criarComponente({...form, tipo: 'Bruto'})`
    // matava ZERO **na época daquela review**. O usuário escolhia "Montagem" e o sistema gravava
    // "Bruto", em silêncio — e `tipo` é justamente o campo que governa a Receita Padrão da 1C.
    //
    // ⚠️ CORRIGIDO no pré-flight da 9A (2026-08-14): hoje o C1 JÁ TEM DONO. O teste
    // `cadastra com sucesso: envia o corpo digitado (inclusive o tipo escolhido), limpa o formulario e
    // recarrega a lista` (hoje `:435`) foi escrito exatamente para isso — o comentário dele (hoje
    // `:433-434`), diz que troca o `<select>` de Tipo para um valor diferente do default "senao a
    // mutacao … sobreviveria por coincidencia".
    // MEDIDO: a M1 mata esse mesmo teste acima — o `cadastra com sucesso:` nomeado por extenso quatro
    // linhas acima, hoje `:435` — mesmo SEM
    // este teste, e a M2 mata ele e `cadastro com sucesso apos um conflito
    // anterior esconde o botao Reativar o existente` (hoje `:786`). Ou seja, este teste NÃO acrescenta
    // nenhuma morte de mutação — é o achado nº 5 do relatório de pré-flight.
    //
    // DECIDIDO pelo usuário em 2026-08-14: ele **FICA**, e a composição não muda por causa dele.
    // Razão: prova o **corpo exato** do POST (`toBe(JSON.stringify(...))`), o que o teste acima faz
    // de forma mais frouxa; e tirar um teste barato e correto para economizar uma linha de conta custaria
    // propagar uma baseline nova para a 9B — a dívida que mais caro saiu nesta fase. Não o remova
    // "para limpar duplicação": a redundância aqui é deliberada e está registrada.
    const fetchMock = fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'CMP-1' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'Montagem' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const post = fetchMock.mock.calls.find((c) => (c[1] as RequestInit)?.method === 'POST')!
    expect((post[1] as RequestInit).body)
      .toBe(JSON.stringify({ codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Montagem' }))
  })

  it('distingue "nada corresponde à busca" de "catálogo vazio"', async () => {
    // Reescrito na 9B: a busca agora e debounced, entao precisa de timers falsos e do `avancar`.
    // Matador da M6' (trocar `buscando` pelo literal `false`, agora sobre `lista.textoDaBusca`).
    vi.useFakeTimers()
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await avancar(0)
    expect(screen.getByText('Nenhum componente cadastrado')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Buscar por código ou descrição'), { target: { value: 'XPTO' } })
    await avancar(300)

    expect(screen.getByText('Nenhum componente encontrado')).toBeTruthy()
    expect(screen.getByText('Nada corresponde a "XPTO".')).toBeTruthy()
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({
        itens: [{ id: 1, codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Bruto', ativo: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('CMP-1')).toBeTruthy()
    expect(screen.queryByLabelText('Código')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })

  it('não afirma "nenhum componente cadastrado" quando a carga da lista falhou', async () => {
    // DECISÃO U1. A sonda que o pré-flight de 2026-08-13 rodou contra o bloco original do Step 2 (o
    // que dizia só `itens.length === 0`): com o GET em 500, `queryByText('Nenhum componente
    // cadastrado')` NÃO era nulo — a tela mostrava o banner de erro E o estado vazio ao mesmo tempo.
    // É a mesma forma do Critical que o fix pass da Task 8 pagou. Sem este teste o conserto do
    // Step 2 entra sem guarda, e a Task 8 já provou que essa forma volta.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'Falha' }, 500),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByText('Nenhum componente cadastrado')).toBeNull()
    expect(screen.queryByText('Nenhum componente encontrado')).toBeNull()
  })

  it('mostra o formulário para o perfil PCP, que escreve em componentes mas não em setores', async () => {
    // MATADOR DA M9, e o único possível. MEDIDO no pré-flight: `permissoes.ts:19-23` dá
    // `componentes: ['Administrador','PCP']` e `setores: ['Administrador']`; a suíte só usava
    // `Administrador` (escreve nos dois) e `Operador` (não escreve em nenhum), e nenhum dos dois
    // separa os recursos — por isso trocar `('componentes')` por `('setores')` matava ZERO.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByLabelText('Código')).toBeTruthy()
  })

  it('mostra o tipo de cada componente na lista', async () => {
    // MATADOR DA M16, e fecha o X3 do pré-flight da 9A (decisão do usuário, 2026-08-14). MEDIDO
    // naquele passe: apagar `<Pilula>{c.tipo}</Pilula>` do item deixava os 39 testes VERDES — o tipo
    // do componente sumia da lista inteira, em silêncio, e `tipo` é justamente o campo que governa a
    // Receita Padrão da 1C. A `Pilula` tem teste próprio (`Pilula.test.tsx`), mas ele prova a
    // PRIMITIVA; ninguém provava que ESTA tela a usa para mostrar o tipo.
    //
    // `within(item)` e não `screen.getByText('Montagem')` direto: com o formulário na tela, o
    // `<select>` de Tipo também tem uma `<option>Montagem</option>`, e a busca global acharia DUAS
    // ocorrências e estouraria. O escopo no `<li>` é o que torna a asserção sobre a lista, e não
    // sobre o formulário — molde de `PedidosPage.test.tsx:62` (`closest('li')!`).
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({
        itens: [{ id: 1, codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Montagem', ativo: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    const item = (await screen.findByText('CMP-1')).closest('li')!
    expect(within(item).getByText('Montagem')).toBeTruthy()
  })

  it('dá à tela o título "Componentes" no h1', async () => {
    // MATADOR DA M17, e fecha o X6 do pré-flight da 9A (decisão do usuário, 2026-08-14). MEDIDO
    // naquele passe: trocar `titulo="Componentes"` por `titulo="XXX"` deixava os 39 testes VERDES —
    // o `<h1>` da tela podia dizer qualquer coisa e nenhum deles lia o título.
    //
    // `Pagina.test.tsx:9` prova que a PRIMITIVA põe o título no `<h1>`; este prova que ESTA tela
    // passa o título certo para ela. São duas propriedades diferentes, e só a segunda é da 9A.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    }))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect((await screen.findByRole('heading', { level: 1 })).textContent).toBe('Componentes')
  })

  it('agrupa as teclas da busca numa requisição só — o debounce está LIGADO na tela', async () => {
    // Fecha o D14 do pré-flight: MEDIDO, `useBuscaPaginada({ buscar, atrasoDoDebounce: 0 })` — o
    // debounce efetivamente desligado — deixava a suíte inteira verde, 0 mortes. O debounce é metade
    // do título desta task e não tinha prova nenhuma no nível da tela. Molde: o
    // `faz UMA requisição para três teclas digitadas em sequência`, de `useBuscaPaginada.test.tsx`,
    // aplicado à tela em vez de ao Hospedeiro.
    vi.useFakeTimers()
    const fetchMock = fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await avancar(0)
    expect(fetchMock).toHaveBeenCalledTimes(1)   // só a carga da montagem

    const campo = screen.getByLabelText('Buscar por código ou descrição')
    fireEvent.change(campo, { target: { value: 'S' } })
    fireEvent.change(campo, { target: { value: 'SU' } })
    fireEvent.change(campo, { target: { value: 'SUP' } })

    await avancar(299)
    expect(fetchMock).toHaveBeenCalledTimes(1)   // o debounce ainda não venceu

    await avancar(1)
    expect(fetchMock).toHaveBeenCalledTimes(2)   // UMA requisição para as TRÊS teclas
  })

  it('mantém a mensagem de código duplicado depois da recarga da lista', async () => {
    // O defeito mais provável desta task — o `erroDeEscrita ?? erroDeLeitura` existe para evitá-lo.
    //
    // O esboço do plano original não provava o que o título promete: só a PRECEDÊNCIA de
    // `erroDeEscrita` sobre `erroDeLeitura` (já morta várias vezes pela M5). Para provar
    // sobrevivência de verdade, depois de a mensagem de conflito aparecer é preciso disparar uma
    // recarga de VERDADE — aqui, marcar "Mostrar inativos" — e só então afirmar que a mensagem
    // continua lá. `mudarInativos` não é debounced (só a busca é), então não precisa de timers
    // falsos.
    //
    // Respostas DIFERENTES por método: 409 no POST, 200 no GET — `fetchPorRota` roteia só por
    // caminho, então aqui é um mock manual, no molde dos testes de conflito vizinhos.
    const fetchMock = vi.fn().mockImplementation((_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(new Response(
          JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 7 }),
          { status: 409 },
        ))
      }
      return Promise.resolve(new Response(
        JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
        { status: 200 },
      ))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())

    fireEvent.change(screen.getByLabelText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    expect(await screen.findByText('Já existe um componente com este código.')).toBeTruthy()

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('incluirInativos=true')
    })
    expect(screen.getByText('Já existe um componente com este código.')).toBeTruthy()
  })
})
