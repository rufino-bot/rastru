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
  it('dois 401 concorrentes disparam UM unico /auth/refresh', async () => {
    const deps = depsFake()
    client.inicializar(deps)

    let refreshCount = 0
    let liberarRefresh!: () => void
    const refreshPendente = new Promise<void>((r) => { liberarRefresh = r })

    const fetchMock = vi.fn(async (url: string) => {
      if (url === '/auth/refresh') {
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
      if (url === '/auth/refresh') return resp(200, { accessToken: 'novo', accessTokenExpiraEm: 'x', usuario: {} })
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
      if (url === '/auth/refresh') return resp(401, { erro: 'x' })
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

describe('tentarRestaurarSessao', () => {
  it('200 devolve o usuario e guarda o token', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    vi.stubGlobal('fetch', vi.fn(async () =>
      resp(200, { accessToken: 't', accessTokenExpiraEm: 'x', usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' } })))

    const u = await client.tentarRestaurarSessao()
    expect(u?.nomeUsuario).toBe('admin')
    expect(deps.token).toBe('t')
  })

  it('401 devolve null', async () => {
    const deps = depsFake()
    client.inicializar(deps)
    vi.stubGlobal('fetch', vi.fn(async () => resp(401, { erro: 'x' })))
    expect(await client.tentarRestaurarSessao()).toBeNull()
  })
})
