// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LoginPage } from './LoginPage'
import { AuthProvider } from '../auth/AuthContext'
import { _resetParaTeste, apiFetch } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Harness diferente das outras telas, de propósito: a `LoginPage` só existe dentro do
// `AuthProvider` de VERDADE — é ele que chama `client.inicializar` e o init-refresh. Por isso
// aqui NÃO se chama `inicializar()` à mão (o provider chama) e o mock precisa responder
// `/api/auth/refresh`, que dispara no mount antes de qualquer interação.
describe('LoginPage', () => {
  beforeEach(() => { _resetParaTeste() })
  afterEach(() => { vi.unstubAllGlobals() })

  function renderizarLogin() {
    return render(
      <MemoryRouter><AuthProvider><LoginPage /></AuthProvider></MemoryRouter>,
    )
  }

  it('mostra os dois campos e o botão de entrar', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
    }))

    renderizarLogin()

    expect(await screen.findByLabelText('Usuário')).toBeTruthy()
    expect(screen.getByLabelText('Senha')).toBeTruthy()
    expect(screen.getByText('Entrar')).toBeTruthy()
  })

  it('mostra a mensagem genérica quando o login é recusado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
      '/api/auth/login': () => respostaJson({ erro: 'nao importa' }, 401),
    }))

    renderizarLogin()
    fireEvent.change(await screen.findByLabelText('Usuário'), { target: { value: 'admin' } })
    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'errada' } })
    fireEvent.click(screen.getByText('Entrar'))

    // Texto ÚNICO e genérico: honra o não-oráculo do backend, que responde o mesmo 401 para
    // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
    // desfaria no front a defesa que o backend paga BCrypt para manter.
    expect(await screen.findByText('Usuário ou senha inválidos.')).toBeTruthy()
  })

  it('avisa que a sessão expirou quando a volta ao login veio de sessão perdida', async () => {
    // O init-refresh responde 200 (sessão restaurada), e a primeira chamada autenticada devolve
    // 401 duas vezes seguidas -> `onSessionLost` -> estado anônimo com motivo 'sessao-expirada'.
    // É o único caminho que acende este aviso, e nenhum teste o cobria.
    let refreshes = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => {
        refreshes += 1
        return refreshes === 1
          ? respostaJson({
              accessToken: 't',
              accessTokenExpiraEm: '2026-08-06T10:00:00-03:00',
              usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' },
            })
          : respostaJson({ erro: 'expirou' }, 401)
      },
      '/api/me': () => respostaJson({ erro: 'nao autorizado' }, 401),
    }))

    renderizarLogin()
    // O init-refresh #1 responde 200 -> status vira 'autenticado' -> LoginPage devolve <Navigate>
    // e 'Entrar' SAI da tela. É a saída dele, não a presença, que prova que o init terminou:
    // 'Entrar' está na tela desde o primeiro paint (status inicial 'carregando'), então esperar
    // pela presença não espera nada.
    await waitFor(() => expect(screen.queryByText('Entrar')).toBeNull())
    await apiFetch('/me')

    expect(await screen.findByText('Sessão expirada. Entre novamente.')).toBeTruthy()
  })
})
