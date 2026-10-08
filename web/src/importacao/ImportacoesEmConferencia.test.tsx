// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { ImportacoesEmConferencia } from './ImportacoesEmConferencia'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'

afterEach(cleanup)

const RASCUNHO = {
  id: 5,
  nomeDoArquivo: 'bom-chassi.xlsx',
  criadoPor: 'Maria PCP',
  criadoEm: '2026-10-02T08:00:00-03:00',
  atualizadoEm: '2026-10-02T09:30:00-03:00',
}

/** Mock roteado por método: o `fetchPorRota` de testes/api.ts não separa o GET do DELETE. */
function montarFetch(opcoes: {
  listas: unknown[][]
  falhaNaLista?: boolean
  respostaDoDelete?: () => Response
}) {
  let leituras = 0
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    if (caminho === '/api/agrupamentos/21/importacoes' && metodo === 'GET') {
      if (opcoes.falhaNaLista) return Promise.resolve(respostaJson({}, 500))
      const lista = opcoes.listas[Math.min(leituras, opcoes.listas.length - 1)]
      leituras += 1
      return Promise.resolve(respostaJson(lista))
    }
    if (caminho === '/api/importacoes/5' && metodo === 'DELETE') {
      return Promise.resolve(opcoes.respostaDoDelete?.() ?? new Response(null, { status: 204 }))
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${metodo} ${url}`))
  })
}

function renderizar(versao = 0) {
  return render(
    <MemoryRouter initialEntries={['/agrupamentos/21']}>
      <Routes>
        <Route path="/agrupamentos/21" element={<ImportacoesEmConferencia agrupamentoId={21} versao={versao} />} />
        <Route path="/importacoes/:id" element={<p>Tela de conferência</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ImportacoesEmConferencia', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra o estado de carregando enquanto busca', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('lista vazia não renderiza nada: nem o título nem o estado vazio', async () => {
    const fetchMock = montarFetch({ listas: [[]] })
    vi.stubGlobal('fetch', fetchMock)

    const { container } = renderizar()

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByRole('status')).toBeNull())
    expect(container.innerHTML).toBe('')
  })

  it('erro de carga cai em BannerDeErro com mensagemDeErro', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [], falhaNaLista: true }))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('mostra arquivo, autor e a data de atualização de cada rascunho', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[RASCUNHO]] }))

    renderizar()

    const lista = await screen.findByRole('list', { name: 'Importações em conferência' })
    expect(within(lista).getByText('bom-chassi.xlsx')).toBeTruthy()
    expect(within(lista).getByText(/Maria PCP/)).toBeTruthy()
    expect(within(lista).getByText(/atualizado em 02\/10\/2026 09:30/)).toBeTruthy()
  })

  it('Continuar_aponta_para_a_conferencia', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[RASCUNHO]] }))

    renderizar()

    const link = await screen.findByRole('link', { name: /^Continuar/ })
    expect(link.getAttribute('href')).toBe('/importacoes/5')
    fireEvent.click(link)
    expect(await screen.findByText('Tela de conferência')).toBeTruthy()
  })

  it('Descartar_pede_confirmacao_e_recarrega', async () => {
    const fetchMock = montarFetch({ listas: [[RASCUNHO], []] })
    vi.stubGlobal('fetch', fetchMock)

    const { container } = renderizar()
    fireEvent.click(await screen.findByRole('button', { name: /^Descartar/ }))

    // Pede confirmação, e nada foi apagado ainda.
    const dialogo = screen.getByRole('dialog')
    expect(within(dialogo).getByText(/bom-chassi\.xlsx/)).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toBe(false)

    fireEvent.click(within(dialogo).getByRole('button', { name: 'Descartar' }))

    await waitFor(() => expect(container.innerHTML).toBe(''))
    const delete_ = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')!
    expect(delete_[0]).toBe('/api/importacoes/5')
  })

  it('cancelar a confirmação não descarta nada', async () => {
    const fetchMock = montarFetch({ listas: [[RASCUNHO]] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: /^Descartar/ }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => (c[1] as RequestInit | undefined)?.method === 'DELETE')).toBe(false)
    expect(screen.getByText('bom-chassi.xlsx')).toBeTruthy()
  })

  it('falha ao descartar mostra o erro e mantém o rascunho na lista', async () => {
    vi.stubGlobal('fetch', montarFetch({
      listas: [[RASCUNHO]],
      respostaDoDelete: () => respostaJson({}, 403),
    }))

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: /^Descartar/ }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Descartar' }))

    expect((await screen.findByRole('alert')).textContent).toBe('Seu perfil não tem permissão para esta ação.')
    expect(screen.getByText('bom-chassi.xlsx')).toBeTruthy()
  })

  it('mudar a versão relê a lista', async () => {
    const fetchMock = montarFetch({ listas: [[], [RASCUNHO]] })
    vi.stubGlobal('fetch', fetchMock)

    const { rerender } = renderizar(0)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))

    rerender(
      <MemoryRouter initialEntries={['/agrupamentos/21']}>
        <Routes>
          <Route path="/agrupamentos/21" element={<ImportacoesEmConferencia agrupamentoId={21} versao={1} />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText('bom-chassi.xlsx')).toBeTruthy()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })
})
