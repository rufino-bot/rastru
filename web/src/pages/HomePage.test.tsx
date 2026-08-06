// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// A `HomePage` usa `useAuth()` só para o `logout`. Como o `AuthProvider` de verdade dispara um
// init-refresh no mount (rede), aqui ele é substituído por um provider mínimo — o que se testa é a
// tela, não a sessão. O `useAuth` real tem sua prova em `estadoDaSessao.test.ts` e no LoginPage.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: { status: 'anonimo' }, login: async () => {}, logout: async () => {} }),
}))

const USUARIO = { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Administrador do Sistema', perfil: 'Administrador' }

describe('HomePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra quem está logado depois de carregar /me', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Administrador do Sistema')).toBeTruthy()
    expect(screen.getByText('Administrador')).toBeTruthy()
  })

  it('mostra "Carregando…" antes da resposta e o esconde depois', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getByText('Carregando…')).toBeTruthy()
    await screen.findByText('Administrador do Sistema')
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('mostra erro quando /me falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar seus dados.')).toBeTruthy()
  })
})
