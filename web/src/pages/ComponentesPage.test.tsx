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
})
