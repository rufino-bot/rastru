import { afterEach, describe, expect, it, vi } from 'vitest'
import * as client from './client'

// Helper: monta uma Response de teste com status e corpo JSON.
function resp(status: number, corpo?: unknown): Response {
  return new Response(corpo === undefined ? null : JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function depsFake() {
  let token: string | null = null
  return {
    getToken: () => token,
    setToken: (t: string | null) => { token = t },
    onSessionLost: vi.fn(),
    get token() { return token },
  }
}

afterEach(() => {
  client._resetParaTeste()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('apiFetch — refresh single-flight', () => {
  it('dois 401 concorrentes disparam UM unico /api/auth/refresh', async () => {
    const deps = depsFake()
    client.inicializar(deps)

    let refreshCount = 0
    let liberarRefresh!: () => void
    const refreshPendente = new Promise<void>((r) => { liberarRefresh = r })

    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/auth/refresh') {
        refreshCount++
        await refreshPendente // segura o refresh ate os dois chamarem
        return resp(200, { accessToken: 'novo', accessTokenExpiraEm: 'x', usuario: {} })
      }
      // /me: 401 na primeira, 200 depois do refresh
      return deps.token === 'novo' ? resp(200, { ok: true }) : resp(401, { erro: 'x' })
    })
    vi.stubGlobal('fetch', fetchMock)

    const a = client.apiFetch('/me')
    const b = client.apiFetch('/me')
    // setTimeout(0) e um macrotask: so roda depois que TODA a fila de microtasks drena, entao
    // os dois apiFetch ja chegaram ao renovarToken() (e ao await do refresh pendente) antes de
    // liberar. Um unico `await Promise.resolve()` nao garantiria isso — daria flakiness.
    await new Promise((r) => setTimeout(r, 0))
    liberarRefresh()
    const [ra, rb] = await Promise.all([a, b])

    expect(refreshCount).toBe(1)
    expect(ra.status).toBe(200)
    expect(rb.status).toBe(200)
  })
})

describe('apiFetch — 401 -> refresh -> retry', () => {
  it('refaz a requisicao uma vez com o token novo e devolve a segunda resposta', async () => {
    const deps = depsFake()
    client.inicializar(deps)

    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/auth/refresh') return resp(200, { accessToken: 'novo', accessTokenExpiraEm: 'x', usuario: {} })
      return deps.token === 'novo' ? resp(200, { dado: 1 }) : resp(401, { erro: 'x' })
    })
    vi.stubGlobal('fetch', fetchMock)

    const r = await client.apiFetch('/me')
    expect(r.status).toBe(200)
    expect(deps.onSessionLost).not.toHaveBeenCalled()
  })
})

describe('apiFetch — retry ainda falha -> desiste', () => {
  it('se o refresh falha, chama onSessionLost, NAO retenta, e propaga o 401', async () => {
    const deps = depsFake()
    client.inicializar(deps)

    let meCount = 0
    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/api/auth/refresh') return resp(401, { erro: 'x' })
      meCount++
      return resp(401, { erro: 'x' })
    })
    vi.stubGlobal('fetch', fetchMock)

    const r = await client.apiFetch('/me')
    expect(r.status).toBe(401)
    expect(deps.onSessionLost).toHaveBeenCalledOnce()
    expect(meCount).toBe(1) // so a requisicao original; refresh falhou, sem retry
  })
})

// A3 do fix pass da review de branch: login, logout e tentarRestaurarSessao chamam fetch(rota(...))
// diretamente (nao passam por apiFetch), e nenhum dos tres tinha teste que provasse o prefixo
// `/api`. Hoje o backend responde nos dois caminhos (com e sem `/api`) — transicao deliberada —
// entao um call site que escapasse do prefixo passaria em silencio ate o dia em que os caminhos
// nus forem fechados, e viraria 404 em producao com a suite verde. Prova por mutacao: apagar o
// prefixo de cada call site (login, logout, tentarRestaurarSessao) individualmente matava 0/41
// antes destes testes.
describe('login', () => {
  it('manda POST na rota de login com usuario e senha, e guarda o token no caminho feliz', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    const fetchMock = vi.fn().mockResolvedValue(resp(200, {
      accessToken: 'novo-token',
      accessTokenExpiraEm: 'x',
      usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    const usuario = await client.login('admin', 'Admin@123')

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/auth/login')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ nomeUsuario: 'admin', senha: 'Admin@123' }))
    expect(usuario.nomeUsuario).toBe('admin')
    expect(deps.token).toBe('novo-token')
  })

  // Corpo do 401 nao importa: login traduz QUALQUER resposta nao-ok em ErroDeLogin generico
  // (401 sempre generico, ver CLAUDE.md) — nao ha corpo especifico a espelhar aqui.
  it('lanca ErroDeLogin quando o backend rejeita as credenciais', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    vi.stubGlobal('fetch', vi.fn(async () => resp(401, { erro: 'CredenciaisInvalidas' })))

    await expect(client.login('admin', 'senha-errada')).rejects.toThrow(client.ErroDeLogin)
    expect(deps.token).toBeNull()
  })
})

describe('logout', () => {
  it('manda POST na rota de logout e zera o token local', async () => {
    const deps = depsFake()
    deps.setToken('token-antigo')
    client.inicializar(deps)
    const fetchMock = vi.fn().mockResolvedValue(resp(204))
    vi.stubGlobal('fetch', fetchMock)

    await client.logout()

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/auth/logout')
    expect(init.method).toBe('POST')
    expect(deps.token).toBeNull()
  })

  // Comportamento decidido, nao bug: o try/catch de logout engole falha de rede de proposito (o
  // estado local vira anonimo de qualquer jeito). Nao "consertar" — so provar que continua assim.
  it('zera o token local mesmo quando a chamada de rede falha', async () => {
    const deps = depsFake()
    deps.setToken('token-antigo')
    client.inicializar(deps)
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('rede caiu') }))

    await expect(client.logout()).resolves.toBeUndefined()
    expect(deps.token).toBeNull()
  })
})

describe('tentarRestaurarSessao', () => {
  it('200 devolve o usuario e guarda o token', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    const fetchMock = vi.fn().mockResolvedValue(
      resp(200, { accessToken: 't', accessTokenExpiraEm: 'x', usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' } }))
    vi.stubGlobal('fetch', fetchMock)

    const u = await client.tentarRestaurarSessao()
    expect(u?.nomeUsuario).toBe('admin')
    expect(deps.token).toBe('t')
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/auth/refresh')
    expect(init.method).toBe('POST')
  })

  it('401 devolve null', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    const fetchMock = vi.fn().mockResolvedValue(resp(401, { erro: 'x' }))
    vi.stubGlobal('fetch', fetchMock)

    expect(await client.tentarRestaurarSessao()).toBeNull()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/auth/refresh')
  })
})
