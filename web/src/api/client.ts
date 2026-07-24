import type { LoginResponse, UsuarioDto } from './tipos'

export interface DependenciasDoClient {
  getToken: () => string | null
  setToken: (t: string | null) => void
  onSessionLost: () => void
}

export class ErroDeLogin extends Error {
  constructor() {
    super('Usuário ou senha inválidos.')
    this.name = 'ErroDeLogin'
  }
}

let deps: DependenciasDoClient | null = null
let refreshEmVoo: Promise<string | null> | null = null

export function inicializar(d: DependenciasDoClient): void {
  deps = d
}

// So para os testes: zera o estado de modulo entre casos.
export function _resetParaTeste(): void {
  deps = null
  refreshEmVoo = null
}

function exigirDeps(): DependenciasDoClient {
  if (!deps) throw new Error('client nao inicializado — chame inicializar() no bootstrap')
  return deps
}

function fetchComToken(path: string, init: RequestInit, token: string | null): Promise<Response> {
  const headers = new Headers(init.headers)
  if (token) headers.set('Authorization', `Bearer ${token}`)
  return fetch(path, { ...init, headers, credentials: 'include' })
}

async function fazerRefresh(): Promise<string | null> {
  const resp = await fetch('/auth/refresh', { method: 'POST', credentials: 'include' })
  if (!resp.ok) return null
  const corpo = (await resp.json()) as LoginResponse
  return corpo.accessToken
}

// Single-flight: concorrentes pegam carona na mesma promise em voo. So o primeiro toca a rede.
// Protege a rotacao de refresh token do backend (dois refresh simultaneos derrubariam a sessao).
function renovarToken(): Promise<string | null> {
  if (refreshEmVoo) return refreshEmVoo
  refreshEmVoo = fazerRefresh().finally(() => { refreshEmVoo = null })
  return refreshEmVoo
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const d = exigirDeps()
  const resposta = await fetchComToken(path, init, d.getToken())
  if (resposta.status !== 401) return resposta

  const novoToken = await renovarToken()
  if (!novoToken) {
    d.onSessionLost()
    return resposta // propaga o 401 original
  }
  d.setToken(novoToken)

  // Refaz UMA vez. Se voltar 401 mesmo com token novo, o problema e a sessao — nao retenta.
  const resposta2 = await fetchComToken(path, init, novoToken)
  if (resposta2.status === 401) d.onSessionLost()
  return resposta2
}

export async function login(nomeUsuario: string, senha: string): Promise<UsuarioDto> {
  const d = exigirDeps()
  const resp = await fetch('/auth/login', {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nomeUsuario, senha }),
  })
  if (!resp.ok) throw new ErroDeLogin()
  const corpo = (await resp.json()) as LoginResponse
  d.setToken(corpo.accessToken)
  return corpo.usuario
}

export async function logout(): Promise<void> {
  const d = exigirDeps()
  try {
    await fetch('/auth/logout', { method: 'POST', credentials: 'include' })
  } catch {
    // Rede caiu: o logout local acontece de qualquer jeito (o estado vira anonimo).
  }
  d.setToken(null)
}

export async function tentarRestaurarSessao(): Promise<UsuarioDto | null> {
  const d = exigirDeps()
  const resp = await fetch('/auth/refresh', { method: 'POST', credentials: 'include' })
  if (!resp.ok) return null
  const corpo = (await resp.json()) as LoginResponse
  d.setToken(corpo.accessToken)
  return corpo.usuario
}
