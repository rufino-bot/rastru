// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import App from './App'
import { inicializar, _resetParaTeste } from './api/client'
import { fetchPorRota, respostaJson } from './testes/api'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

vi.mock('./auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 7, nomeUsuario: 'g', nomeCompleto: 'G', perfil: 'Gestao' } },
    login: async () => {},
    logout: async () => {},
  }),
}))

describe('App', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('o link "Liberações" do menu abre a tela de Liberações, e não a Home', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/tarefas/contagem': () => respostaJson({ total: 0 }),
      '/api/expedicao': () => respostaJson({ pedidos: [] }),
      '/api/remessas/recentes': () => respostaJson([]),
      '/api/liberacoes/pendentes': () => respostaJson([]),
    }))

    render(<MemoryRouter initialEntries={['/expedicao']}><App /></MemoryRouter>)
    expect(await screen.findByRole('heading', { level: 1, name: 'Expedição' })).toBeTruthy()

    const barra = screen.getByRole('navigation', { name: 'Principal' })
    fireEvent.click(within(barra).getByRole('link', { name: 'Liberações' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Liberações' })).toBeTruthy()
    expect(await screen.findByText('Nenhuma liberação pendente.')).toBeTruthy()
  })
})
