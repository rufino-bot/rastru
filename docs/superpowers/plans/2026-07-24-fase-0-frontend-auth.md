# Frontend de Autenticação (Fase 0) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar o front React que fecha a autenticação da Fase 0 ponta a ponta: login com usuário/senha, sessão sustentada por access token em memória + refresh token em cookie httpOnly, e uma tela protegida que consome `/me`.

**Architecture:** Uma camada de rede sem React (`api/client.ts`) concentra toda a lógica perigosa — anexar o Bearer, o refresh single-flight, a retentativa única após 401, o init-refresh do boot — recebendo dependências injetadas no bootstrap. Um `AuthContext` é a única ponte entre essa camada e o React: guarda o estado da sessão (tri-estado), o access token (em `useRef`, memória), e fia o `client` uma vez. Telas finas (`LoginPage`, `HomePage`, `ProtectedRoute`) só consomem o contexto.

**Tech Stack:** React + TypeScript + Vite, Tailwind CSS v4 (plugin `@tailwindcss/vite`), React Router (v7), Vitest (só na lógica de auth, ambiente node, sem jsdom). Mobile-first.

**Spec de origem:** `docs/superpowers/specs/2026-07-24-fase-0-frontend-auth-design.md`. Este plano cobre as tarefas T9–T11 daquela spec (Task 9 = scaffold; Task 10 = núcleo de auth; Task 11 = telas).

## Prerequisites (fora do escopo dos commits, mas necessários para executar)

- **Node.js LTS + npm instalados.** Hoje **não estão** nesta máquina — instalar antes de começar a Task 9. Verificar com `node --version` e `npm --version`.
- **Backend rodável para a Task 11:** `docker compose up -d` + schema/seed aplicados (ver `CLAUDE.md`), e a API no ar com `dotnet run --project src/Rastreamento.Api` (perfil http → `http://localhost:5169`). Usuário de teste: `admin` / `Admin@123`.

## Global Constraints

Valores project-wide, válidos para **todas** as tarefas (copiados da spec):

- **Access token só em memória** (`useRef`), nunca `localStorage`/`sessionStorage`. O refresh token nunca é lido pelo JS do front — vive só no cookie httpOnly + Secure + SameSite=Strict, emitido/limpo pelo backend.
- **Mesma origem front↔API.** Dev usa proxy do Vite (`server.proxy`) encaminhando `/auth` e `/me` para `http://localhost:5169`. URLs sempre **relativas** (`/auth/login`, `/me`). **Sem `.env`, sem `VITE_API_BASE_URL`.**
- **Toda requisição usa `credentials: 'include'`** (o cookie de refresh precisa acompanhar `/auth/refresh` e `/auth/logout`).
- **Não-oráculo na borda.** Falha de login/refresh vira mensagem única e genérica **"Usuário ou senha inválidos."**; nunca distinguir usuário inexistente / senha errada / conta inativa.
- **JSON no fio é camelCase** (padrão do ASP.NET Core MVC; verificado em `AuthEndpointsTests.cs`). Enviar e ler em camelCase.
- **Contratos da API (verificados no código):**
  - `POST /auth/login` body `{ nomeUsuario, senha }` → 200 `{ accessToken, accessTokenExpiraEm, usuario }` (+ `Set-Cookie: refreshToken`); 401 `{ erro }`.
  - `POST /auth/refresh` (sem body, usa cookie) → 200 mesma forma do login; 401 `{ erro }`.
  - `POST /auth/logout` (sem body) → **sempre 204**, idempotente.
  - `GET /me` (header `Authorization: Bearer <accessToken>`) → 200 `UsuarioDto`; 401 se token ausente/inválido.
  - `UsuarioDto` = `{ usuarioId: number, nomeUsuario: string, nomeCompleto: string, perfil: string }`.
- **Nomenclatura:** entidades/dados de domínio em português espelhando a API (`Usuario`, `nomeUsuario`); técnica em inglês onde já é padrão React (`AuthProvider`, `ProtectedRoute`).

---

## File Structure

```
web/
  index.html            # <title>Rastru</title>, <div id="root">
  package.json
  vite.config.ts        # plugin react + tailwind + server.proxy (/auth, /me)
  tsconfig.json         # (gerado pelo template react-ts)
  src/
    main.tsx            # BrowserRouter > AuthProvider > App
    App.tsx             # <Routes>
    index.css           # @import "tailwindcss";
    api/
      tipos.ts          # UsuarioDto, LoginResponse (espelham o JSON camelCase)
      client.ts         # SEM React: apiFetch, login, logout, tentarRestaurarSessao, single-flight
    auth/
      estadoDaSessao.ts # EstadoSessao (tri-estado) + estadoDaSessao() — puro, sem React
      AuthContext.tsx   # AuthProvider, useAuth — fia o client e guarda o estado
      ProtectedRoute.tsx
    pages/
      LoginPage.tsx
      HomePage.tsx
    components/
      TelaCarregando.tsx  # spinner + "Restaurando sessão…"
  src/api/client.test.ts     # Vitest: single-flight, 401→refresh→retry, retry falha, restaurar
  src/auth/estadoDaSessao.test.ts  # Vitest: mapeamento puro do init-refresh
```

Responsabilidades (cada arquivo, uma responsabilidade):
- `api/client.ts` — toda a rede e a lógica de token. Não importa React.
- `auth/AuthContext.tsx` — estado React + fiação do client. Não faz `fetch` direto.
- Telas — só consomem `useAuth()` e `apiFetch`. Sem lógica de token.

---

## Task 9: Scaffold + Tailwind v4 + proxy do Vite

**Objetivo:** projeto `web/` que builda e serve uma página em branco atrás do proxy. (Spec T9.)

**Files:**
- Create: `web/` inteiro via template Vite `react-ts`
- Modify: `web/vite.config.ts`, `web/src/index.css`, `web/index.html`
- Delete: boilerplate do template (`web/src/App.css`, `web/src/assets/react.svg`, conteúdo demo de `App.tsx`)

**Interfaces:**
- Consumes: nada (primeira tarefa).
- Produces: projeto Vite com scripts `dev`/`build`/`test`; proxy de `/auth` e `/me` → `http://localhost:5169`; Tailwind v4 ativo.

- [ ] **Step 1: Criar o projeto Vite (template react-ts)**

A partir da raiz do repositório:

```bash
npm create vite@latest web -- --template react-ts
cd web
npm install
```

- [ ] **Step 2: Instalar Tailwind v4 e Vitest**

```bash
npm install tailwindcss @tailwindcss/vite
npm install -D vitest
```

- [ ] **Step 3: Configurar `vite.config.ts` (plugin tailwind + proxy)**

Substituir `web/vite.config.ts` por:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Proxy: front (localhost:5173) e API (localhost:5169) na mesma origem do ponto de vista
// do navegador. O cookie de refresh e Secure, mas navegadores tratam localhost como
// contexto seguro mesmo em http, entao o cookie e aceito em dev.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/auth': { target: 'http://localhost:5169', changeOrigin: true },
      '/me': { target: 'http://localhost:5169', changeOrigin: true },
    },
  },
})
```

- [ ] **Step 4: Ativar Tailwind no CSS**

Substituir todo o conteúdo de `web/src/index.css` por:

```css
@import "tailwindcss";
```

- [ ] **Step 5: Limpar o boilerplate do template**

```bash
rm web/src/App.css web/src/assets/react.svg
```

Substituir `web/src/App.tsx` por um placeholder mínimo que prova o build e o Tailwind:

```tsx
export default function App() {
  return (
    <div className="min-h-screen flex items-center justify-center text-2xl font-semibold">
      Rastru
    </div>
  )
}
```

Garantir que `web/src/main.tsx` importe `./index.css` (o template já faz) e não referencie `App.css`.

- [ ] **Step 6: Ajustar `index.html` (título)**

Em `web/index.html`, trocar o `<title>` para:

```html
<title>Rastru</title>
```

- [ ] **Step 7: Adicionar o script de teste ao `package.json`**

No `web/package.json`, no bloco `"scripts"`, adicionar:

```json
"test": "vitest run",
"test:watch": "vitest"
```

- [ ] **Step 8: Verificar o build**

Run: `cd web && npm run build`
Expected: build conclui sem erro (tsc + vite build), gera `web/dist`.

- [ ] **Step 9: Verificar o dev server (manual)**

Run: `cd web && npm run dev`
Expected: serve em `http://localhost:5173`, mostra "Rastru" centralizado (Tailwind aplicado). Encerrar com Ctrl+C. (O proxy só será exercitado de verdade na Task 11, com a API no ar.)

- [ ] **Step 10: Commit**

```bash
git add web
git commit -m "feat(web): scaffold Vite+React+TS+Tailwind v4 com proxy para a API"
```

---

## Task 10: Núcleo de auth — `client.ts` + `AuthContext` + testes

**Objetivo:** a camada de rede sem React (single-flight, retry único, os cinco métodos), o contexto tri-estado, e os testes Vitest. É a tarefa com peso de review. (Spec T10.)

**Files:**
- Create: `web/src/api/tipos.ts`, `web/src/api/client.ts`, `web/src/api/client.test.ts`
- Create: `web/src/auth/estadoDaSessao.ts`, `web/src/auth/estadoDaSessao.test.ts`, `web/src/auth/AuthContext.tsx`

**Interfaces:**
- Consumes: nada além do scaffold da Task 9.
- Produces (nomes/assinaturas que a Task 11 consome):
  - `api/tipos.ts`: `interface UsuarioDto { usuarioId: number; nomeUsuario: string; nomeCompleto: string; perfil: string }`; `interface LoginResponse { accessToken: string; accessTokenExpiraEm: string; usuario: UsuarioDto }`.
  - `api/client.ts`: `inicializar(deps: DependenciasDoClient): void`; `apiFetch(path: string, init?: RequestInit): Promise<Response>`; `login(nomeUsuario: string, senha: string): Promise<UsuarioDto>` (lança `ErroDeLogin` em 401); `logout(): Promise<void>` (sempre resolve); `tentarRestaurarSessao(): Promise<UsuarioDto | null>`; `_resetParaTeste(): void`; `class ErroDeLogin extends Error`. `interface DependenciasDoClient { getToken(): string | null; setToken(t: string | null): void; onSessionLost(): void }`.
  - `auth/estadoDaSessao.ts`: `type EstadoSessao = { status: 'carregando' } | { status: 'autenticado'; usuario: UsuarioDto } | { status: 'anonimo'; motivo?: 'sessao-expirada' }`; `function estadoDaSessao(usuario: UsuarioDto | null): EstadoSessao`.
  - `auth/AuthContext.tsx`: `AuthProvider({ children })`; `useAuth(): { estado: EstadoSessao; login(nomeUsuario, senha): Promise<void>; logout(): Promise<void> }`.

**Nota de cobertura:** a spec citava um teste de transição `logout → anonimo`. No design final essa transição é um `setEstado({ status: 'anonimo' })` inline no `AuthProvider` (não uma função pura), então não há unidade pura a testar sem renderizar React — que a spec deliberadamente adiou. A parte pura (o mapeamento do init-refresh, que decide `autenticado` vs `anonimo`) está coberta por `estadoDaSessao`; `logout → anonimo` e `login → autenticado` são validados pela aceitação end-to-end da Task 11 (Step 9).

- [ ] **Step 1: Criar os tipos (`api/tipos.ts`)**

```ts
export interface UsuarioDto {
  usuarioId: number
  nomeUsuario: string
  nomeCompleto: string
  perfil: string
}

// accessTokenExpiraEm chega em ISO 8601 com offset -03:00; na Fase 0 nao alimenta logica
// nenhuma (o refresh e reativo ao 401, sem timer proativo). Guardado por fidelidade ao contrato.
export interface LoginResponse {
  accessToken: string
  accessTokenExpiraEm: string
  usuario: UsuarioDto
}
```

- [ ] **Step 2: Escrever o `client.test.ts` (os quatro testes) — deve falhar**

Criar `web/src/api/client.test.ts`:

```ts
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
      resp(200, { accessToken: 't', accessTokenExpiraEm: 'x', usuario: { usuarioId: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' } })))

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
```

- [ ] **Step 3: Rodar os testes e confirmar que falham**

Run: `cd web && npm run test`
Expected: FAIL — `client.ts` ainda não exporta `apiFetch`/`login`/etc. (erro de import/undefined).

- [ ] **Step 4: Implementar o `client.ts`**

Criar `web/src/api/client.ts`:

```ts
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
```

- [ ] **Step 5: Rodar os testes do client e confirmar que passam**

Run: `cd web && npm run test`
Expected: PASS — os cinco casos de `client.test.ts` verdes. (O teste de `estadoDaSessao` ainda não existe.)

- [ ] **Step 6: Escrever o `estadoDaSessao.test.ts` — deve falhar**

Criar `web/src/auth/estadoDaSessao.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { estadoDaSessao } from './estadoDaSessao'

describe('estadoDaSessao — mapeamento do init-refresh', () => {
  it('usuario presente -> autenticado', () => {
    const u = { usuarioId: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' }
    expect(estadoDaSessao(u)).toEqual({ status: 'autenticado', usuario: u })
  })

  it('null -> anonimo SEM motivo (boot sem sessao nao e "expirada")', () => {
    expect(estadoDaSessao(null)).toEqual({ status: 'anonimo' })
  })
})
```

- [ ] **Step 7: Rodar e confirmar que falha**

Run: `cd web && npm run test`
Expected: FAIL — `estadoDaSessao.ts` ainda não existe / não exporta `estadoDaSessao`.

- [ ] **Step 8: Implementar o `estadoDaSessao.ts` (puro, sem React)**

Criar `web/src/auth/estadoDaSessao.ts`:

```ts
import type { UsuarioDto } from '../api/tipos'

export type EstadoSessao =
  | { status: 'carregando' }
  | { status: 'autenticado'; usuario: UsuarioDto }
  | { status: 'anonimo'; motivo?: 'sessao-expirada' }

// Mapeamento puro do resultado do init-refresh -> estado. Testavel sem React.
// null vira anonimo SEM motivo: nao ter sessao no boot e diferente de "sessao expirou no uso".
export function estadoDaSessao(usuario: UsuarioDto | null): EstadoSessao {
  return usuario ? { status: 'autenticado', usuario } : { status: 'anonimo' }
}
```

- [ ] **Step 9: Rodar o teste de `estadoDaSessao` e confirmar que passa**

Run: `cd web && npm run test`
Expected: PASS — `estadoDaSessao.test.ts` (2 casos) verde; `client.test.ts` segue verde.

- [ ] **Step 10: Implementar o `AuthContext.tsx`**

Criar `web/src/auth/AuthContext.tsx`:

```tsx
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import * as client from '../api/client'
import { estadoDaSessao, type EstadoSessao } from './estadoDaSessao'

interface ContextoAuth {
  estado: EstadoSessao
  login: (nomeUsuario: string, senha: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<ContextoAuth | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoSessao>({ status: 'carregando' })
  const tokenRef = useRef<string | null>(null)
  const jaIniciou = useRef(false)

  useEffect(() => {
    // Guarda contra o double-mount do StrictMode em dev: sem isso, o init-refresh dispararia
    // DOIS /auth/refresh no boot; como o backend rotaciona o token, o segundo chegaria com o
    // token ja revogado -> 401 -> a sessao morreria logo ao abrir. Roda exatamente uma vez.
    if (jaIniciou.current) return
    jaIniciou.current = true

    client.inicializar({
      getToken: () => tokenRef.current,
      setToken: (t) => { tokenRef.current = t },
      // Refresh falhou no meio do uso: volta pro login SINALIZANDO que expirou (rotina normal).
      onSessionLost: () => setEstado({ status: 'anonimo', motivo: 'sessao-expirada' }),
    })

    client.tentarRestaurarSessao().then((usuario) => setEstado(estadoDaSessao(usuario)))
  }, [])

  async function login(nomeUsuario: string, senha: string) {
    const usuario = await client.login(nomeUsuario, senha) // lanca ErroDeLogin em 401
    setEstado({ status: 'autenticado', usuario })
  }

  async function logout() {
    await client.logout()
    setEstado({ status: 'anonimo' }) // logout voluntario: sem motivo, nao mostra aviso de expirada
  }

  return <AuthContext.Provider value={{ estado, login, logout }}>{children}</AuthContext.Provider>
}

export function useAuth(): ContextoAuth {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth precisa estar dentro de <AuthProvider>')
  return ctx
}
```

- [ ] **Step 11: Rodar toda a suíte e confirmar verde**

Run: `cd web && npm run test`
Expected: PASS — `client.test.ts` (5 casos) + `estadoDaSessao.test.ts` (2 casos).

- [ ] **Step 12: Confirmar que o build ainda passa**

Run: `cd web && npm run build`
Expected: build sem erro de tipos.

- [ ] **Step 13: Commit**

```bash
git add web/src/api web/src/auth
git commit -m "feat(web): nucleo de auth — client single-flight + AuthContext tri-estado (testado)"
```

---

## Task 11: Telas + fiação de rota

**Objetivo:** `ProtectedRoute`, `LoginPage` (com aviso de "Sessão expirada"), `HomePage`, `TelaCarregando`, `App` com rotas e `main.tsx` montando os providers. Validação end-to-end contra a API real. (Spec T11.)

**Files:**
- Create: `web/src/components/TelaCarregando.tsx`, `web/src/auth/ProtectedRoute.tsx`, `web/src/pages/LoginPage.tsx`, `web/src/pages/HomePage.tsx`
- Modify: `web/src/App.tsx`, `web/src/main.tsx`

**Interfaces:**
- Consumes: de `auth/AuthContext` → `AuthProvider`, `useAuth`, `EstadoSessao`; de `api/client` → `apiFetch`; de `api/tipos` → `UsuarioDto`.
- Produces: app navegável (rotas `/login`, `/`, catch-all).

- [ ] **Step 1: Instalar o React Router**

```bash
cd web && npm install react-router-dom
```

- [ ] **Step 2: `TelaCarregando` (spinner + texto — nunca tela em branco)**

Criar `web/src/components/TelaCarregando.tsx`:

```tsx
// Estado 'carregando' da sessao. Texto explicito de proposito: numa conexao lenta (wifi de
// chao de fabrica) uma tela em branco faz o usuario recarregar, e o reload no meio do
// init-refresh pode derrubar a sessao. Mostrar que algo acontece tira o incentivo de recarregar.
export function TelaCarregando() {
  return (
    <div className="min-h-screen flex flex-col items-center justify-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-gray-300 border-t-gray-700" />
      <p className="text-gray-600">Restaurando sessão…</p>
    </div>
  )
}
```

- [ ] **Step 3: `ProtectedRoute`**

Criar `web/src/auth/ProtectedRoute.tsx`:

```tsx
import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from './AuthContext'
import { TelaCarregando } from '../components/TelaCarregando'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { estado } = useAuth()
  // 'carregando' ANTES de 'anonimo' e o que impede o flash de login no F5: enquanto o
  // init-refresh nao volta, mostramos o spinner em vez de redirecionar pro login.
  if (estado.status === 'carregando') return <TelaCarregando />
  if (estado.status === 'anonimo') return <Navigate to="/login" replace />
  return <>{children}</>
}
```

- [ ] **Step 4: `LoginPage` (não-oráculo + aviso de sessão expirada)**

Criar `web/src/pages/LoginPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function LoginPage() {
  const { estado, login } = useAuth()
  const [nomeUsuario, setNomeUsuario] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (estado.status === 'autenticado') return <Navigate to="/" replace />

  // Aviso discreto quando o usuario chegou aqui por sessao perdida (onSessionLost), nao por
  // acesso normal. Comunica que foi rotina de autenticacao, nao erro dele.
  const sessaoExpirada = estado.status === 'anonimo' && estado.motivo === 'sessao-expirada'

  async function aoEnviar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      await login(nomeUsuario, senha)
    } catch {
      // Mensagem unica e generica: honra o nao-oraculo do backend (401 identico para todos os casos).
      setErro('Usuário ou senha inválidos.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <form onSubmit={aoEnviar} className="w-full max-w-sm flex flex-col gap-4">
        <h1 className="text-2xl font-semibold text-center">Rastru</h1>

        {sessaoExpirada && (
          <p className="rounded bg-amber-100 text-amber-800 text-sm px-3 py-2 text-center">
            Sessão expirada. Entre novamente.
          </p>
        )}

        <label className="flex flex-col gap-1">
          <span className="text-sm">Usuário</span>
          <input
            className="border rounded px-3 py-2"
            value={nomeUsuario}
            onChange={(e) => setNomeUsuario(e.target.value)}
            required
            autoComplete="username"
          />
        </label>

        <label className="flex flex-col gap-1">
          <span className="text-sm">Senha</span>
          <input
            type="password"
            className="border rounded px-3 py-2"
            value={senha}
            onChange={(e) => setSenha(e.target.value)}
            required
            autoComplete="current-password"
          />
        </label>

        {erro && <p className="text-red-600 text-sm">{erro}</p>}

        <button
          type="submit"
          disabled={enviando}
          className="bg-gray-800 text-white rounded px-3 py-2 disabled:opacity-50"
        >
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </div>
  )
}
```

- [ ] **Step 5: `HomePage` (dados do /me + recarregar + sair)**

Criar `web/src/pages/HomePage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import type { UsuarioDto } from '../api/tipos'

export function HomePage() {
  const { logout } = useAuth()
  const [usuario, setUsuario] = useState<UsuarioDto | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  async function carregarMe() {
    setCarregando(true)
    setErro(null)
    try {
      const resp = await apiFetch('/me')
      if (!resp.ok) {
        setErro('Não foi possível carregar seus dados.')
        return
      }
      setUsuario((await resp.json()) as UsuarioDto)
    } catch {
      setErro('Não foi possível carregar seus dados.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregarMe() }, [])

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">Rastru</h1>

      {carregando && <p className="text-gray-600">Carregando…</p>}
      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      {usuario && (
        <dl className="flex flex-col gap-2">
          <div><dt className="text-sm text-gray-500">Usuário</dt><dd>{usuario.nomeUsuario}</dd></div>
          <div><dt className="text-sm text-gray-500">Nome</dt><dd>{usuario.nomeCompleto}</dd></div>
          <div><dt className="text-sm text-gray-500">Perfil</dt><dd>{usuario.perfil}</dd></div>
        </dl>
      )}

      <div className="flex gap-3 mt-4">
        <button onClick={carregarMe} className="border rounded px-3 py-2">Recarregar</button>
        <button onClick={logout} className="border rounded px-3 py-2">Sair</button>
      </div>
    </div>
  )
}
```

- [ ] **Step 6: `App.tsx` (rotas)**

Substituir `web/src/App.tsx` por:

```tsx
import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { ProtectedRoute } from './auth/ProtectedRoute'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<ProtectedRoute><HomePage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
```

- [ ] **Step 7: `main.tsx` (montar os providers)**

Substituir `web/src/main.tsx` por:

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import App from './App'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
```

- [ ] **Step 8: Confirmar build e testes**

Run: `cd web && npm run build && npm run test`
Expected: build sem erro; os 7 casos de teste seguem verdes.

- [ ] **Step 9: Aceitação end-to-end (manual, contra a API real)**

Pré: `docker compose up -d` + schema/seed aplicados; API no ar (`dotnet run --project src/Rastreamento.Api`, perfil http → `http://localhost:5169`); `cd web && npm run dev` (→ `http://localhost:5173`).

Verificar, no navegador:
1. Abrir `http://localhost:5173/` → cai em `/login` (spinner some rápido; sem sessão prévia).
2. Logar com `admin` / `Admin@123` → vai pra Home e mostra `nomeUsuario`, `nomeCompleto`, `perfil`.
3. Clicar **Recarregar** → dados do `/me` recarregam sem erro (prova o Bearer + `apiFetch`).
4. **F5 numa sessão viva** → mostra o spinner "Restaurando sessão…" e volta pra Home **sem** passar pelo login (init-refresh restaurou).
5. Clicar **Sair** → volta pro login **sem** o aviso de "Sessão expirada" (logout voluntário).
6. Senha errada → mensagem "Usuário ou senha inválidos." e permanece no login.

Expected: todos os passos conforme descrito.

- [ ] **Step 10: Commit**

```bash
git add web/src
git commit -m "feat(web): telas de login e home + rotas protegidas (fecha a auth da Fase 0)"
```

---

## Notas de execução

- **StrictMode + init-refresh:** a guarda `jaIniciou` no `AuthProvider` (Task 10, Step 8) é essencial — sem ela o double-mount do dev dispara dois refresh no boot e a rotação do backend derruba a sessão. Não remover "por limpeza".
- **Cookie Secure em dev:** funciona sobre `http://localhost` porque navegadores tratam localhost como contexto seguro. Se o teste manual da Task 11 mostrar que o cookie não persiste (F5 caindo no login), verificar no DevTools → Application → Cookies se `refreshToken` foi gravado; se não, é sinal de que o navegador não está tratando a origem como segura (improvável em localhost).
- **`accessTokenExpiraEm` não é usado** na Fase 0 (refresh é reativo ao 401, sem timer proativo) — guardado no tipo por fidelidade ao contrato; um timer proativo é candidato de Fase 1, não agora (YAGNI).

## Fora de escopo (não implementar aqui)

- Testes de UI/DOM (renderização de telas, roteamento via jsdom) — decisão de adiar.
- React Query / camada de cache — Fase 1.
- Conteúdo real da HomePage — definido depois com a dupla de TCC.
- Janela de tolerância na rotação de refresh (backend) — dívida consciente do backend.
- Servir o build estático pela API em produção — concern de deploy, fora da Fase 0.
