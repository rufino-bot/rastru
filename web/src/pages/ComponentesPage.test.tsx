// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ComponentesPage } from './ComponentesPage'
import { inicializar, _resetParaTeste } from '../api/client'

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

describe('ComponentesPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('lista os componentes que a API devolveu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('SUP-001')).toBeTruthy()
  })

  // ESTE e o teste que justifica ter adotado @testing-library/react nesta fase. Sem ele, o reset
  // de pagina e comportamento sem prova nenhuma: buscar algo que cabe em 2 paginas estando na
  // pagina 7 mostraria lista vazia, com cara de bug, e nada quebraria.
  it('volta para a pagina 1 quando a busca muda', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('busca=sup')
      expect(ultima).toContain('pagina=1')
    })
  })

  // Ao contrario do teste anterior (que sempre passa pela pagina 2 antes de mudar a busca), este
  // comeca e permanece na pagina 1: se `busca` sair do array de dependencias do useEffect, o
  // efeito so re-executaria por causa do setPagina(1) que mudarBusca tambem chama — e aqui pagina
  // JA e 1, entao esse setState e um no-op (Object.is) e nao dispara o efeito sozinho. Sem esta
  // prova, remover `busca` das dependencias sobrevive (medido por mutacao — ver o relatorio).
  it('atualiza a URL de busca mesmo ja estando na pagina 1', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('busca=sup')
    })
  })

  it('volta para a pagina 1 quando o tamanho da pagina muda', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.change(screen.getByLabelText('Por página'), { target: { value: '50' } })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('tamanho=50')
      expect(ultima).toContain('pagina=1')
    })
  })

  // Mesmo raciocinio do teste de busca isolado na pagina 1: o teste acima sempre passa pela
  // pagina 2 antes de trocar o tamanho, entao a transicao pagina 2->1 dispara o efeito de
  // qualquer jeito (o `pagina` do array de dependencias muda) e mascara uma eventual remocao de
  // `tamanho` do array — medido por mutacao (ver o relatorio). Este comeca e fica na pagina 1.
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

  it('desabilita Anterior na primeira pagina e Proxima na ultima', async () => {
    // Com total 1 e tamanho 20 existe UMA pagina: os dois botoes ficam desabilitados. Mata a
    // mutacao de deixar "Proxima" sempre habilitada, que levaria a uma pagina vazia.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    expect((screen.getByRole('button', { name: 'Anterior' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Próxima' }) as HTMLButtonElement).disabled).toBe(true)
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

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Suporte' } })
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

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Suporte' } })
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

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'MON-001' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Montagem X' } })
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
      expect((screen.getByPlaceholderText('Código') as HTMLInputElement).value).toBe('')
      expect((screen.getByPlaceholderText('Descrição') as HTMLInputElement).value).toBe('')
      expect((screen.getByLabelText('Tipo') as HTMLSelectElement).value).toBe('Fabricado')
    })

    // a lista recarregou: houve um GET depois do POST
    const gets = fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method !== 'POST')
    expect(gets.length).toBeGreaterThanOrEqual(2)
  })

  // I2: o terceiro handler de reset de pagina (mudarInativos) nao tinha par — o teste existente do
  // checkbox comeca e fica na pagina 1, entao a asserção de pagina=1 nele e degenerada (passa com ou
  // sem o setPagina(1)). Este PASSA pela pagina 2 antes de mexer no checkbox, igual ao molde usado
  // para `busca` e `tamanho`.
  it('volta para a pagina 1 quando o filtro de inativos muda', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('incluirInativos=true')
      expect(ultima).toContain('pagina=1')
    })
  })

  // I3: corrida de resposta fora de ordem. A carga inicial (1a requisicao) fica pendurada; a
  // requisicao disparada pela busca (2a, mais recente) resolve PRIMEIRO. A guarda de sequencia tem
  // que garantir que o resultado exibido e o da requisicao mais RECENTE ENVIADA, e que a resposta
  // atrasada da 1a, quando finalmente chega, e descartada em vez de sobrescrever a tela.
  it('mantem o resultado da requisicao mais recente quando respostas chegam fora de ordem', async () => {
    let resolverPrimeira!: (r: Response) => void
    let resolverSegunda!: (r: Response) => void
    let chamadas = 0

    const fetchMock = vi.fn().mockImplementation(() => {
      chamadas += 1
      if (chamadas === 1) return new Promise<Response>((resolve) => { resolverPrimeira = resolve })
      return new Promise<Response>((resolve) => { resolverSegunda = resolve })
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    // a SEGUNDA requisicao (a mais recente) responde primeiro
    resolverSegunda(new Response(
      JSON.stringify({
        itens: [{ id: 2, codigo: 'SUP-RECENTE', descricao: 'Recente', tipo: 'Bruto', ativo: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
      { status: 200 },
    ))
    await screen.findByText('SUP-RECENTE')

    // a PRIMEIRA requisicao (atrasada, da carga inicial) so responde DEPOIS
    resolverPrimeira(new Response(
      JSON.stringify({
        itens: [{ id: 1, codigo: 'SUP-ANTIGO', descricao: 'Antigo', tipo: 'Bruto', ativo: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
      { status: 200 },
    ))
    // da tempo para a resposta atrasada ser processada, se a guarda nao a descartar
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(screen.queryByText('SUP-ANTIGO')).toBeNull()
    expect(screen.getByText('SUP-RECENTE')).toBeTruthy()
  })

  // I3, guarda do `catch`: uma requisicao desatualizada que FALHA depois de uma mais recente ter
  // tido SUCESSO nao pode pintar erro por cima de dados frescos. Isolado da guarda de
  // `setComponentes`/`setTotal` (testada acima): aqui a 1a requisicao rejeita, nao resolve com
  // dado velho.
  it('nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso', async () => {
    let rejeitarPrimeira!: (e: unknown) => void
    let resolverSegunda!: (r: Response) => void
    let chamadas = 0

    const fetchMock = vi.fn().mockImplementation(() => {
      chamadas += 1
      if (chamadas === 1) {
        return new Promise<Response>((_resolve, reject) => { rejeitarPrimeira = reject })
      }
      return new Promise<Response>((resolve) => { resolverSegunda = resolve })
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    // a 2a requisicao (a mais recente) tem sucesso primeiro
    resolverSegunda(new Response(
      JSON.stringify({
        itens: [{ id: 2, codigo: 'SUP-FRESCO', descricao: 'Fresco', tipo: 'Bruto', ativo: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
      { status: 200 },
    ))
    await screen.findByText('SUP-FRESCO')

    // a 1a requisicao (desatualizada) so FALHA depois
    rejeitarPrimeira(new Error('rede caiu'))
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(screen.queryByText('Não foi possível carregar os componentes.')).toBeNull()
    expect(screen.getByText('SUP-FRESCO')).toBeTruthy()
  })

  // I3, guarda do `finally`: uma resposta desatualizada nao pode desligar o indicador de
  // carregando enquanto a requisicao mais RECENTE ainda esta em voo — senao a tela mostra lista
  // vazia (sem "Carregando…") como se tivesse terminado, quando na verdade so a resposta velha
  // chegou.
  it('mantem o indicador de carregando enquanto a requisicao mais recente ainda nao respondeu', async () => {
    let resolverPrimeira!: (r: Response) => void
    let resolverSegunda!: (r: Response) => void
    let chamadas = 0

    const fetchMock = vi.fn().mockImplementation(() => {
      chamadas += 1
      if (chamadas === 1) return new Promise<Response>((resolve) => { resolverPrimeira = resolve })
      return new Promise<Response>((resolve) => { resolverSegunda = resolve })
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    expect(screen.getByText('Carregando…')).toBeTruthy()

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))

    // a 1a requisicao (desatualizada) responde, mas a 2a (a atual) continua pendente
    resolverPrimeira(new Response(
      JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
      { status: 200 },
    ))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.getByText('Carregando…')).toBeTruthy()

    resolverSegunda(new Response(
      JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
      { status: 200 },
    ))
    await waitFor(() => expect(screen.queryByText('Carregando…')).toBeNull())
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

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    expect(await screen.findByText('Já existe um componente com este código.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Reativar o existente' })).toBeNull()
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

    expect(await screen.findByText('Não foi possível alterar o componente.')).toBeTruthy()
  })

  // I6, metade 2: falha no GET da carga. Sem o setErro do catch, uma queda de wifi durante a carga
  // produz uma lista vazia MUDA — indistinguivel de "nao ha componentes cadastrados".
  it('mostra mensagem de erro quando a carga da lista falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 500 }))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar os componentes.')).toBeTruthy()
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
    expect(screen.getByText('INA-001').closest('span')?.className).toContain('line-through')
  })

  // Minor: `Math.max(1, …)` sem prova — com total=0 o rodape tem que mostrar "Pagina 1", nao
  // "Pagina 0" (que o Math.ceil(0/20) produziria sozinho).
  it('mostra Página 1 de 1 quando o total e zero', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(
      JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
      { status: 200 },
    ))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('Página 1 de 1 — 0 no total')).toBeTruthy()
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

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'SUP-001' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Suporte' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))
    await screen.findByRole('button', { name: 'Reativar o existente' })

    fireEvent.change(screen.getByPlaceholderText('Código'), { target: { value: 'SUP-002' } })
    fireEvent.change(screen.getByPlaceholderText('Descrição'), { target: { value: 'Outro' } })
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar' }))

    await waitFor(() => {
      expect((screen.getByPlaceholderText('Código') as HTMLInputElement).value).toBe('')
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
    await screen.findByText('Não foi possível alterar o componente.')

    fireEvent.click(screen.getByRole('button', { name: 'Inativar' }))

    await waitFor(() => {
      expect(screen.queryByText('Não foi possível alterar o componente.')).toBeNull()
    })
  })
})
