# Fase 0 — Frontend de Autenticação (Spec de Implementação)

> **Para o `writing-plans`:** este documento é a **spec** do frontend da Fase 0 (tarefas
> T9–T11). Consuma-o para gerar o plano bite-sized/TDD. O backend da Fase 0 já está
> concluído e mergeado (`master`); esta spec descreve **só o front** que consome aquela API.
> As specs de domínio (`specs/00-06`) e a spec do walking skeleton
> (`2026-07-23-fase-0-walking-skeleton-design.md`) são a fonte da verdade do "porquê", do
> schema e das `Global Constraints`; **não** as reescreva — referencie. Toda escolha
> concreta necessária para não deixar placeholders já está resolvida aqui.

**Goal:** Entregar o front React que fecha a autenticação da Fase 0 ponta a ponta: login
com usuário/senha, sessão sustentada por access token em memória + refresh token em cookie
httpOnly, e uma tela protegida que consome `/me`. A Fase 0 encerra a autenticação por
completo — não há funcionalidade de domínio aqui, só a fiação de auth provada de ponta a
ponta.

**Architecture:** Uma camada de rede sem React (`api/client.ts`) que concentra toda a
lógica perigosa — anexar o Bearer, o refresh **single-flight**, a retentativa única após
401, o init-refresh do boot — e recebe suas dependências injetadas no bootstrap. Um
`AuthContext` que é a única ponte entre essa camada e o React: guarda o estado da sessão
(tri-estado), o access token (em `useRef`, memória), e fia o `client` uma vez. Telas finas
(`LoginPage`, `HomePage`, `ProtectedRoute`) que só consomem o contexto.

**Tech Stack:** React + TypeScript + Vite + Tailwind CSS, React Router, Vitest (só na lógica
de auth). Mobile-first (uso em Android via navegador, sem PWA no MVP).

---

## Global Constraints

Valem as `Global Constraints` da spec do walking skeleton
(`2026-07-23-fase-0-walking-skeleton-design.md`) — não repetir aqui. As que o front **toca
diretamente** e não pode violar:

- **Access token só em memória** (nunca `localStorage`/`sessionStorage`). Refresh token
  **nunca** é visto pelo JS do front — vive só no cookie httpOnly + Secure + SameSite=Strict,
  emitido/limpo pelo backend.
- **Mesma origem front↔API.** Em dev, o Vite usa `server.proxy` encaminhando `/auth` e `/me`
  para a API — sem requisição cross-site, sem CORS. Em produção, o build estático é servido
  pela própria API. **Consequência de design:** URLs são sempre relativas (`/auth/login`,
  `/me`); **não há `VITE_API_BASE_URL` nem arquivo `.env`** (a spec do walking skeleton
  previa `web/.env`; esta spec o remove de propósito — ver "Desvios da spec anterior").
- **Não-oráculo na borda.** O backend unifica toda falha de login/refresh em `401` idêntico.
  O front tem que honrar isso: nunca distinguir usuário inexistente de senha errada de conta
  inativa. Mensagem de erro única e genérica.
- **Datas em GMT-3 na borda.** A API já serializa timestamps em ISO 8601 com offset `-03:00`
  (via `HorarioDeBrasiliaJsonConverter`). O front apenas exibe/consome nesse fuso; não
  converte nada por conta própria.

---

## Contratos reais da API (verificados no código-fonte)

Fonte: `src/Rastreamento.Api/Controllers/AuthController.cs` e `MeController.cs`. **Não
inferir — estes são os contratos exatos.** O JSON no fio é **camelCase** (padrão do ASP.NET
Core MVC; `Program.cs` não define `PropertyNamingPolicy`, e os testes em
`AuthEndpointsTests.cs` leem `accessToken`, `accessTokenExpiraEm`, `nomeUsuario`). A
desserialização de entrada é *case-insensitive* (default do MVC), mas o front deve **enviar
e ler em camelCase** — a forma canônica do fio.

- **`POST /auth/login`** — corpo `{ "nomeUsuario": string, "senha": string }`.
  - `200` → `{ "accessToken": string, "accessTokenExpiraEm": string(ISO -03:00), "usuario": UsuarioDto }`.
    O refresh token **não** vem no corpo — só no cookie `Set-Cookie: refreshToken=…`.
  - `401` → `{ "erro": string }`. Cobre usuário inexistente, senha errada e conta inativa —
    indistinguíveis de propósito.
- **`POST /auth/refresh`** — sem corpo; usa o cookie `refreshToken`. Precisa de
  `credentials: 'include'`.
  - `200` → mesma forma do login (novo access token + novo cookie rotacionado).
  - `401` → `{ "erro": string }` (cookie ausente, token desconhecido, expirado, revogado —
    todos iguais).
- **`POST /auth/logout`** — sem corpo; usa o cookie. **Sempre `204`**, idempotente (cookie
  ausente/desconhecido/expirado/revogado respondem igual). Apaga o cookie.
- **`GET /me`** — exige `Authorization: Bearer <accessToken>`.
  - `200` → `UsuarioDto`. **Sem ida ao banco** — responde a partir das claims do token.
  - `401` → token ausente/inválido, ou claim faltando.
- **`UsuarioDto`** = `{ "usuarioId": number, "nomeUsuario": string, "nomeCompleto": string, "perfil": string }`
  (camelCase confirmado; `AuthEndpointsTests.cs` lê `nomeUsuario` da resposta de `/me`).

**Fato de design decorrente:** `/me` devolve exatamente o que o login já devolveu (mesmas
claims). A `HomePage` não descobre nada novo sobre o usuário — o valor dela é ser a primeira
tela que exercita o caminho autenticado de ponta a ponta (token na memória → header anexado
→ 401→refresh→retry). É tela de fiação, não de conteúdo.

---

## Estrutura de arquivos

```
web/
  index.html
  package.json  vite.config.ts  tsconfig.json
  tailwind.config.js  postcss.config.js
  vitest.config.ts (ou config em vite.config.ts)
  src/
    main.tsx            # BrowserRouter + AuthProvider na raiz
    App.tsx             # definição das rotas
    api/
      tipos.ts          # LoginResponse, UsuarioDto — espelham o JSON da API
      client.ts         # fetch + single-flight do refresh — SEM React
    auth/
      AuthContext.tsx   # estado da sessão (tri-estado) + fiação do client
      ProtectedRoute.tsx
    pages/
      LoginPage.tsx
      HomePage.tsx
    components/
      TelaCarregando.tsx  # spinner + "Restaurando sessão…"
```

> **Sem `.env`.** URLs relativas + proxy tornam `VITE_API_BASE_URL` desnecessária e a
> classe de bug junto (apontar para origem errada quebra o cookie `SameSite=Strict` de um
> jeito difícil de diagnosticar).

---

## Design detalhado

### A máquina de estados da sessão (tri-estado)

A sessão tem **três** estados, não dois:

| Estado | Quando | UI |
|---|---|---|
| `carregando` | bootstrap: tentando `POST /auth/refresh` | `TelaCarregando` — spinner + texto, **nunca tela em branco** |
| `autenticado` | há access token + usuário | rotas protegidas liberadas |
| `anonimo` | sem sessão, ou refresh falhou | redireciona para `/login` |

O `carregando` existe por causa do F5: o access token vive só em memória e some no reload,
mas o cookie de refresh sobrevive. Sem esse estado, o `ProtectedRoute` avaliaria "sem token"
antes do init-refresh terminar e chutaria o usuário pro login por um instante — flash
visível a cada recarga.

**Transições:**
- **montagem** → `carregando` → init-refresh → `200` vira `autenticado`, `401` vira `anonimo`.
- **login** → `200` vira `autenticado`; `401` continua `anonimo` e a tela mostra o erro.
- **logout** → `anonimo` **sempre**, inclusive se a chamada de rede falhar (senão uma queda
  de rede prenderia o usuário numa sessão que ele mandou encerrar).
- **sessão perdida** (refresh falhou no meio do uso) → `anonimo`.

### `api/client.ts` (sem React)

Módulo que não conhece React. Recebe três dependências no bootstrap e trabalha só com elas:

```ts
interface DependenciasDoClient {
  getToken: () => string | null;         // access token atual (lido a cada chamada)
  setToken: (t: string | null) => void;  // guarda o token renovado
  onSessionLost: () => void;             // refresh falhou → AuthContext vira 'anonimo'
}
```

**Motivo de não ter React:** o single-flight é a parte com mais chance de bug sutil, e tem
que ser testável com Vitest puro, sem renderizar componente. Num hook, cada teste precisaria
de `renderHook` e o timing ficaria abafado por re-renders.

**Superfície pública:**

```ts
inicializar(deps: DependenciasDoClient): void
apiFetch(path: string, init?: RequestInit): Promise<Response>
login(nomeUsuario: string, senha: string): Promise<UsuarioDto>   // POST /auth/login
logout(): Promise<void>                                          // POST /auth/logout — sempre resolve
tentarRestaurarSessao(): Promise<UsuarioDto | null>              // POST /auth/refresh no boot
```

**Fluxo do `apiFetch` (o coração):**
1. Anexa `Authorization: Bearer <getToken()>` e `credentials: 'include'` (o cookie de
   refresh precisa acompanhar).
2. Se a resposta **não** é `401` → devolve como veio.
3. Se é `401` → chama o refresh **single-flight**, pega o token novo via `setToken`, e refaz
   a requisição original **uma única vez**.
4. Se o refresh falha, **ou** se a retentativa toma `401` de novo → `onSessionLost()` e
   propaga o `401`. **Sem segunda retentativa** — se o 401 volta com token recém-renovado, o
   problema é a sessão, não o token; insistir não muda nada.

**Single-flight do refresh** — resolve a corrida: dois `apiFetch` em paralelo tomam `401`
quase juntos. Sem proteção, ambos disparam `POST /auth/refresh`; como o backend **rotaciona**
o token, o segundo chega com um token já rotacionado e falha, derrubando sessão válida. A
trava é uma promise compartilhada:

```ts
let refreshEmVoo: Promise<string> | null = null;

function renovarToken(): Promise<string> {
  if (refreshEmVoo) return refreshEmVoo;           // pega carona no que já está voando
  refreshEmVoo = fazerRefresh().finally(() => { refreshEmVoo = null; });
  return refreshEmVoo;
}
```

Quem chegar enquanto há um refresh em voo espera **a mesma** promise; só o primeiro toca a
rede. É o teste-âncora do Vitest.

**Limite conhecido do single-flight (documentado, não corrigido no front):** a trava é
memória do processo — **não atravessa reload**. Cada F5 é um runtime novo com
`refreshEmVoo = null`. Numa conexão lenta (wifi de chão de fábrica), recarregar no meio do
init-refresh pode até **derrubar** a sessão: o F5 #1 dispara refresh com T0, o backend
rotaciona (revoga T0, emite T1, `Set-Cookie: T1`), mas o usuário recarrega antes da resposta
chegar — a navegação aborta o fetch, o `Set-Cookie: T1` nunca é aplicado, o cookie continua
T0; o F5 #2 manda refresh com T0 já revogado → `401` → login. Pior caso: um login a mais,
não perda de dado. **Mitigação no front:** atacar a causa do reload — o estado `carregando`
mostra spinner + texto, então o usuário não sente a tela morta e não recarrega. **Conserto
de verdade:** janela de tolerância na rotação (backend) — já está na lista de dívida
consciente do backend, **fora do escopo** desta fase.

### `auth/AuthContext.tsx`

Único módulo que conhece React **e** o `client.ts`. Faz três coisas:

**1. Guarda o estado.**
```ts
type EstadoSessao =
  | { status: 'carregando' }
  | { status: 'autenticado'; usuario: UsuarioDto }
  | { status: 'anonimo' };
```
O **access token NÃO entra** nesse union — é um `useRef`, não `useState`. Motivo: o
`client.ts` lê o token via `getToken()` a cada `apiFetch`; se fosse `state`, o closure
injetado no bootstrap congelaria no primeiro valor. `useRef` dá sempre o valor atual sem
re-renderizar. O `usuario` é `state`, porque a UI reage a ele.

**2. Fia o client, uma vez** (`useEffect` de montagem):
```ts
client.inicializar({
  getToken: () => tokenRef.current,
  setToken: (t) => { tokenRef.current = t; },
  onSessionLost: () => setEstado({ status: 'anonimo' }),
});
```

**3. Dispara o init-refresh** logo depois, ainda no boot:
```ts
const usuario = await client.tentarRestaurarSessao();
setEstado(usuario ? { status: 'autenticado', usuario } : { status: 'anonimo' });
```

O contexto expõe à árvore: `estado`, `login(u, s)`, `logout()`. **`login`/`logout` são
finos** — delegam ao `client.ts` e só traduzem o resultado em `setEstado`. Toda a lógica de
rede (single-flight, retry, cookies) fica no `client.ts`; o contexto é só a ponte pro React.

### `auth/ProtectedRoute.tsx`

```tsx
if (estado.status === 'carregando') return <TelaCarregando />;      // spinner + "Restaurando sessão…"
if (estado.status === 'anonimo')    return <Navigate to="/login" replace />;
return children;                                                    // autenticado
```
`carregando` **antes** de `anonimo` é o que impede o flash de login no F5. `replace` para não
empilhar `/login` no histórico (senão o "voltar" do Android vira labirinto).

### Rotas (`App.tsx`)

- `/login` → `LoginPage`
- `/` → `ProtectedRoute` → `HomePage`
- qualquer outra → redireciona para `/`

### `pages/LoginPage.tsx`

Estado controlado + `required` nativo (decisão registrada).
- Dois campos (`NomeUsuario`, `Senha`); botão desabilitado enquanto envia.
- `onSubmit` → `login(u, s)` do contexto. Sucesso → `estado` vira `autenticado` e um
  `<Navigate to="/" />` reativo leva à Home.
- Erro (`401`) → mensagem **única e genérica**: **"Usuário ou senha inválidos."** Nunca
  distinguir os casos — honra o não-oráculo do backend.
- Se já `autenticado` e alguém navega para `/login` na mão → redireciona para `/`.

> **NOTA DE DESIGN a implementar em T11 — aviso de "Sessão expirada".** Quando o usuário
> chega ao login por **sessão perdida** (o `onSessionLost` disparou: refresh/retry falhou no
> meio do uso), a `LoginPage` deve mostrar um aviso discreto **"Sessão expirada"**, para o
> operador entender que **não** foi erro dele — é rotina normal de autenticação. Navegação
> limpa para `/login` (primeiro acesso, logout voluntário) **não** mostra esse aviso.
> Mecanismo sugerido: o `onSessionLost` navega para `/login` carregando um motivo (via
> `state` do React Router, ex.: `navigate('/login', { state: { motivo: 'sessao-expirada' } })`,
> ou um campo equivalente no contexto); a `LoginPage` lê esse motivo e decide exibir. O
> conteúdo/estilo fino do aviso pode ser ajustado na implementação; o requisito é a
> **distinção** entre "expirou (rotina)" e "acesso normal".

### `pages/HomePage.tsx`

Escopo: dados do `/me` + recarregar + logout (decisão registrada). É tela de **fiação da
autenticação**; o conteúdo real será definido depois (fora do escopo da Fase 0).
- No mount, `apiFetch('/me')` e mostra `nomeUsuario`, `nomeCompleto`, `perfil`.
- Botão **Recarregar** → refaz o `/me`. Prova, na prática, que o Bearer está sendo aceito e
  que o `apiFetch` cola o header certo.
- Botão **Sair** → `logout()` do contexto → estado vira `anonimo` → cai no login.

---

## Plano de testes (Vitest — só na lógica de auth)

Decisão registrada: Vitest só na lógica de auth. Os testes batem no `client.ts` com `fetch`
mockado — sem renderizar componente, sem `jsdom` para tela.

1. **Single-flight** (âncora) — dois `apiFetch` concorrentes tomam `401` juntos; afirma que
   `/auth/refresh` foi chamado **uma vez só** e que ambos refizeram a requisição com o token
   novo. Protege a rotação do backend.
2. **`401` → refresh → retry** — um `apiFetch` toma `401`, o refresh devolve token novo, a
   requisição original é refeita **uma vez**, e a segunda resposta é a que volta ao chamador.
3. **Retry falha → desiste** — refresh dá `401` (sessão morta); afirma que `onSessionLost()`
   foi chamado, que **não** houve segunda retentativa, e que o `401` propagou.
4. **`tentarRestaurarSessao`** — `200` devolve o `UsuarioDto`; `401` devolve `null`. Cobre os
   dois ramos do init-refresh (o do F5).

Fora do `client.ts`: um teste leve das transições do `AuthContext` (lógica pura, sem árvore
React): `carregando → autenticado`, `carregando → anonimo`, `logout → anonimo`.

**Fora de escopo de teste agora** (decisão de adiar teste de UI): renderização das telas,
roteamento, `ProtectedRoute` via DOM. A regra de negócio da auth está no `client.ts` e nas
transições — é lá que os bugs perigosos moram e onde os testes ficam.

---

## Quebra em tarefas (T9–T11)

Sequencial, como foi o backend. T9 destrava T10 e T11; T10 destrava T11.

- **T9 — Andaime + build.** `web/` com Vite/React/TS/Tailwind; `vite.config.ts` com o proxy
  de `/auth` e `/me`; `tsconfig`; `main.tsx` que sobe.
  **Critério:** `npm run build` passa; `npm run dev` serve uma página em branco atrás do
  proxy, com `/me`/`/auth` alcançando a API.

- **T10 — Núcleo de auth (o miolo testável).** `api/tipos.ts`, `api/client.ts` (single-flight,
  retry único, os cinco métodos), `auth/AuthContext.tsx` (tri-estado, fiação, init-refresh) e
  os testes Vitest acima.
  **Critério:** os quatro testes do `client.ts` + os de transição passam. É a tarefa com peso
  de review (como foram as de auth no backend).

- **T11 — Telas + fiação de rota.** `ProtectedRoute`, `LoginPage` (incl. a nota do aviso
  "Sessão expirada"), `HomePage`, `TelaCarregando`, `App.tsx` com as rotas.
  **Critério:** rodando contra a API real (`docker compose up`), dá para logar com
  `admin`/`Admin@123`, ver o `/me`, recarregar, sair; e o **F5 numa sessão viva não chuta pro
  login** (init-refresh restaura a sessão).

---

## Desvios da spec anterior (registrados de propósito)

- **Remoção do `web/.env` / `VITE_API_BASE_URL`.** A spec do walking skeleton
  (`2026-07-23-fase-0-walking-skeleton-design.md`) previa `web/.env` com
  `VITE_API_BASE_URL`. Esta spec o **remove**: com proxy em dev e build servido pela própria
  API em prod, as URLs são sempre relativas — não há base URL a configurar, e some junto a
  classe de bug de apontar para a origem errada (que quebra o cookie `SameSite=Strict`).

---

## Fora de escopo (Fase 0)

- Conteúdo real da `HomePage` (definido depois, com a dupla de TCC).
- Testes de UI/DOM (renderização de telas, roteamento).
- React Query / camada de cache (Fase 1 — decisão registrada: fetch puro agora).
- Janela de tolerância na rotação de refresh no backend (dívida consciente do backend).
- Qualquer funcionalidade de domínio (Pedido, Kit, Setor, etc.) — Fase 1 em diante.
