// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { FilaPage } from './FilaPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear() })

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil: 'Operador' } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const SETORES = [{ id: 1, nome: 'Corte', ativo: true }, { id: 3, nome: 'Dobra', ativo: true }]

function renderizar(estado: unknown = null) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/fila', state: estado }]}>
      <Routes>
        <Route path="/fila" element={<FilaPage />} />
        <Route path="/fila/:setorId" element={<p>fila do setor aberta</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('FilaPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes da lista chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('sem Setor lembrado, lista os Setores ativos com link para a fila de cada um', async () => {
    const fetchMock = fetchPorRota({ '/api/setores': () => respostaJson(SETORES) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()

    const link = await screen.findByRole('link', { name: 'Dobra' })
    expect(link.getAttribute('href')).toBe('/fila/3')
    // Só os ativos: a fila de um Setor inativo não recebe nada novo.
    expect(String(fetchMock.mock.calls[0][0])).toBe('/api/setores?incluirInativos=false')
  })

  it('com Setor lembrado neste aparelho, abre direto na fila dele', async () => {
    localStorage.setItem('rastru.fila.setorId', '3')
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson(SETORES) }))

    renderizar()

    expect(await screen.findByText('fila do setor aberta')).toBeTruthy()
  })

  it('Setor lembrado que não está mais ativo: mostra a lista e esquece a lembrança', async () => {
    // Review Focus 5: o Setor foi inativado desde a última visita.
    localStorage.setItem('rastru.fila.setorId', '9')
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson(SETORES) }))

    renderizar()

    expect(await screen.findByRole('link', { name: 'Corte' })).toBeTruthy()
    expect(localStorage.getItem('rastru.fila.setorId')).toBeNull()
  })

  it('"Trocar de Setor" mostra a lista mesmo com um Setor lembrado', async () => {
    localStorage.setItem('rastru.fila.setorId', '3')
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson(SETORES) }))

    renderizar({ escolher: true })

    expect(await screen.findByRole('link', { name: 'Corte' })).toBeTruthy()
    expect(screen.queryByText('fila do setor aberta')).toBeNull()
    // Escolher de novo não apaga a lembrança: ela só muda quando outra fila carregar.
    expect(localStorage.getItem('rastru.fila.setorId')).toBe('3')
  })

  it('sem Setor ativo nenhum, diz isso', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([]) }))

    renderizar()

    expect(await screen.findByText('Nenhum Setor ativo cadastrado')).toBeTruthy()
  })

  it('falha ao listar vira banner, sem lista nem estado vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => Promise.reject(new TypeError('Failed to fetch')) }))

    renderizar()

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
    expect(screen.queryByText('Nenhum Setor ativo cadastrado')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
  })
})
