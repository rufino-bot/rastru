# Fase 0 — Walking Skeleton Autenticado (Spec de Implementação)

> **Para o `writing-plans`:** este documento é a **spec** da Fase 0. Consuma-o para
> gerar o plano bite-sized/TDD. As specs de domínio (`specs/00-06`) são a fonte da
> verdade do "porquê" e do schema; **não** as reescreva — referencie. Toda escolha
> concreta necessária para não deixar placeholders já está resolvida aqui (ver
> "Decisões resolvidas").

**Goal:** Entregar um esqueleto ponta a ponta autenticado — solution .NET em 4 camadas,
banco SQL Server mapeado via EF Core Database First, login com JWT (access + refresh) e um
front React que loga e consome um endpoint protegido.

**Architecture:** Backend em Clean Architecture simplificada (`Domain` / `Application` /
`Infrastructure` / `Api`), com regras de negócio isoladas de EF Core e ASP.NET. Autenticação
por login próprio: senha com BCrypt, access token JWT curto (memória no front) e refresh
token opaco rotacionado, com hash guardado no banco (revogável) e entregue ao front por
cookie httpOnly. Front em React + TypeScript (Vite) + Tailwind, mobile-first.

**Tech Stack:** .NET 10 (LTS), ASP.NET Core Web API, EF Core (Database First), SQL Server,
BCrypt.Net-Next, JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`), xUnit +
`Microsoft.AspNetCore.Mvc.Testing`, React + TypeScript + Vite + Tailwind CSS.

---

## Global Constraints

Requisitos válidos para **todas** as tarefas do plano (valores literais):

- **Target framework:** `net10.0`. Nunca gerar schema via `Add-Migration` do zero — o
  banco nasce de `specs/02-modelo-de-dados.sql` (Database First).
- **Banco:** SQL Server. Dev local via Docker (`mcr.microsoft.com/mssql/server`).
- **Hash de senha:** BCrypt (pacote `BCrypt.Net-Next`). Nunca armazenar senha em claro.
- **Refresh token:** valor opaco aleatório (≥ 256 bits, base64url). O banco guarda **apenas
  o hash** (SHA-256), nunca o valor em claro. Rotação a cada uso; revogável no logout.
- **Tokens (lifetimes):** access token = **15 minutos**; refresh token = **7 dias**.
- **Access token no front:** somente em memória (nunca `localStorage`). Refresh token só em
  **cookie httpOnly + Secure + SameSite=Strict**, definido/limpo pelo backend.
- **Mesma origem front↔API:** para o cookie `SameSite=Strict` funcionar, front e API rodam na
  mesma origem — em **produção** o build estático do front é servido pela própria API/IIS
  (ver `specs/03`); em **dev**, o Vite usa **proxy** (`server.proxy`) encaminhando `/auth` e
  `/me` para a API. Assim não há requisição cross-site. `CORS` com `AllowCredentials` fica
  como fallback documentado, só necessário se um dia o deploy for cross-origin.
- **Nomenclatura:** entidades de domínio em português espelhando o DDL (`Usuario`, `Perfil`,
  `RefreshToken`); padrões técnicos em inglês (`Repository`, `UseCase`, `Controller`, `Dto`).
- **Perfis (claim `role`):** `Operador`, `Almoxarifado`, `PCP`, `Qualidade`, `Gestao`,
  `Administrador` (ver `specs/00-visao-geral.md`).
- **Datas — armazenar em UTC, apresentar em GMT-3:** persistência sempre em UTC
  (`SYSUTCDATETIME()` no banco, `DateTime.UtcNow` no código) — mantém o DDL atual e os KPIs
  de tempo corretos. A conversão para **GMT-3 (America/Sao_Paulo, offset fixo `-03:00`)**
  acontece só na **borda**: respostas da API serializam timestamps em **ISO 8601 com offset
  `-03:00`**; o front exibe e recebe horários em GMT-3. Nenhuma tabela guarda horário local.

---

## Escopo da Fase 0

**Entra:**
- Solution .NET com os 4 projetos + 2 projetos de teste.
- Schema aplicado em SQL Server local; entidades EF Core mapeadas por Database First para
  `Usuario`, `Perfil` e a nova `RefreshToken`.
- Seed mínimo: 6 `Perfil` + 1 usuário `Administrador` para testar o login.
- Endpoints `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `GET /me`.
- App React (Vite + Tailwind): tela de login, contexto de auth (access em memória, refresh
  por cookie, auto-refresh em 401) e uma tela protegida que chama `GET /me`.
- Documento de deploy manual (backend + front) — sem CI/CD.

**Não entra (fases posteriores — não implementar agora):**
- CRUD de catálogo (Setor, Material, Componente) → Fase 1.
- Pedido/Kit, estrutura recursiva, apontamento de setor, separação, dimensional, KPIs →
  Fases 1–6.
- Tela de administração de usuários/perfis (só o seed é necessário aqui).
- Refresh token multi-dispositivo avançado, PWA/offline, pipeline de CI/CD.

**Nota de fronteira:** backend (T1–T8) e frontend (T9–T11) são separáveis. O `writing-plans`
pode gerar um único plano ou dois (um backend, um frontend) — ambos válidos; se dividir,
o plano de frontend depende dos contratos da seção "Contratos".

---

## File Structure

Cada arquivo tem uma responsabilidade. Caminhos exatos (relativos à raiz do repo):

**Backend — `src/`**
- `Rastreamento.sln` — solution agregando os 6 projetos.
- `src/Rastreamento.Domain/Entities/Usuario.cs` — entidade `Usuario`.
- `src/Rastreamento.Domain/Entities/Perfil.cs` — entidade `Perfil`.
- `src/Rastreamento.Domain/Entities/RefreshToken.cs` — entidade `RefreshToken`.
- `src/Rastreamento.Domain/Abstractions/IPasswordHasher.cs` — contrato de hash de senha.
- `src/Rastreamento.Domain/Abstractions/ITokenHasher.cs` — contrato de hash do refresh token.
- `src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs` — leitura de usuário por nome.
- `src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs` — persistência do refresh.
- `src/Rastreamento.Application/Auth/Dtos.cs` — DTOs de request/response de auth.
- `src/Rastreamento.Application/Auth/IAccessTokenGenerator.cs` — contrato de emissão do JWT.
- `src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs` — login.
- `src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs` — refresh + rotação.
- `src/Rastreamento.Application/Auth/RevogarTokenUseCase.cs` — logout.
- `src/Rastreamento.Application/Common/Result.cs` — resultado de caso de uso (sucesso/erro).
- `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` — DbContext.
- `src/Rastreamento.Infrastructure/Persistence/Configurations/*.cs` — mapeamentos Fluent API.
- `src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs` — impl. do repositório.
- `src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs` — impl. do repo.
- `src/Rastreamento.Infrastructure/Security/BCryptPasswordHasher.cs` — impl. BCrypt.
- `src/Rastreamento.Infrastructure/Security/Sha256TokenHasher.cs` — impl. SHA-256.
- `src/Rastreamento.Infrastructure/Security/JwtAccessTokenGenerator.cs` — emissão do JWT.
- `src/Rastreamento.Infrastructure/Security/JwtOptions.cs` — POCO de config do JWT.
- `src/Rastreamento.Api/Controllers/AuthController.cs` — endpoints de auth + `/me`.
- `src/Rastreamento.Api/Program.cs` — DI, auth JWT, CORS, pipeline.
- `src/Rastreamento.Api/appsettings.json` — connection string + seção `Jwt`.

**Backend — `tests/`**
- `tests/Rastreamento.Domain.Tests/` — testes de hash/entidade (sem I/O).
- `tests/Rastreamento.Application.Tests/` — testes dos use cases com fakes de repositório.

**Frontend — `web/`**
- `web/index.html`, `web/vite.config.ts`, `web/tailwind.config.js`, `web/tsconfig.json`.
- `web/src/main.tsx` — bootstrap React.
- `web/src/api/client.ts` — fetch com `credentials: 'include'` + interceptor de refresh.
- `web/src/auth/AuthContext.tsx` — access token em memória, `login`/`logout`/`refresh`.
- `web/src/auth/ProtectedRoute.tsx` — barra rotas sem sessão.
- `web/src/pages/LoginPage.tsx` — formulário de login (Tailwind).
- `web/src/pages/HomePage.tsx` — tela protegida que exibe dados de `GET /me`.
- `web/.env` — `VITE_API_BASE_URL`.

**Docs**
- `docs/deploy-manual.md` — passo a passo de publicação (backend + front).

---

## Mudança de schema (aplicar em `specs/02-modelo-de-dados.sql` — passo 1 do plano)

Conforme o processo do `CLAUDE.md`, a alteração de schema vai **primeiro** no DDL, depois o
mapeamento EF. Adicionar, na seção "USUÁRIOS E PERFIS", a tabela abaixo (segue as convenções
do arquivo: `INT IDENTITY` PK clustered, `DATETIME2` + `SYSUTCDATETIME()`, prefixos de
constraint):

```sql
CREATE TABLE dbo.RefreshToken (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    UsuarioId               INT                 NOT NULL,
    TokenHash               NVARCHAR(200)       NOT NULL,   -- SHA-256 do refresh token (nunca em claro)
    ExpiraEm                DATETIME2           NOT NULL,
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RefreshToken_CriadoEm DEFAULT (SYSUTCDATETIME()),
    RevogadoEm              DATETIME2           NULL,       -- NULL = ativo; preenchido no logout ou na rotação
    SubstituidoPorTokenHash NVARCHAR(200)       NULL,       -- rastro de rotação (auditoria)
    CONSTRAINT PK_RefreshToken PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RefreshToken_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_RefreshToken_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_RefreshToken_ExpiraAposCriado CHECK (ExpiraEm > CriadoEm)
);
GO
CREATE INDEX IX_RefreshToken_Usuario ON dbo.RefreshToken (UsuarioId);
GO
```

`01-dominio-e-regras-de-negocio.md` **não** precisa mudar: refresh token é detalhe de
infraestrutura de sessão, não regra de negócio.

---

## Contratos / Interfaces

Assinaturas exatas para o `writing-plans` não inventar nomes/tipos.

### Endpoints (todos sob `/auth`, exceto `/me`)

- `POST /auth/login`
  - Request body: `{ "nomeUsuario": string, "senha": string }`
  - `200`: body `{ "accessToken": string, "accessTokenExpiraEm": string(ISO), "usuario": UsuarioDto }`
    e header `Set-Cookie: refreshToken=<opaco>; HttpOnly; Secure; SameSite=Strict; Path=/auth; Max-Age=604800`.
  - `401`: credenciais inválidas ou usuário inativo (mensagem genérica, sem revelar qual campo).
- `POST /auth/refresh`
  - Lê o refresh token do cookie (sem body). Valida hash + não expirado + não revogado.
  - `200`: novo `accessToken` (+ `UsuarioDto`) e **novo** cookie de refresh (rotação; o antigo
    é marcado `RevogadoEm` e `SubstituidoPorTokenHash`).
  - `401`: cookie ausente/expirado/revogado.
- `POST /auth/logout`
  - Revoga o refresh token do cookie (`RevogadoEm`) e limpa o cookie. `204`.
- `GET /me` *(requer `[Authorize]`)*
  - `200`: `UsuarioDto` do usuário do token. `401` sem token válido.

### DTOs (Application — `Auth/Dtos.cs`)

```csharp
public record LoginRequest(string NomeUsuario, string Senha);
public record UsuarioDto(int Id, string NomeUsuario, string NomeCompleto, string Perfil);
public record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiraEm,
    string RefreshTokenPlano,      // devolvido ao Controller p/ setar o cookie; não vai no JSON
    DateTime RefreshTokenExpiraEm,
    UsuarioDto Usuario);
```

### Contratos de domínio/aplicação

```csharp
// Domain/Abstractions
public interface IPasswordHasher {
    string Hash(string senhaPlano);
    bool Verificar(string senhaPlano, string senhaHash);
}
public interface ITokenHasher {          // SHA-256 do refresh token opaco
    string Hash(string tokenPlano);
}
public interface IUsuarioRepository {
    Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct);
    Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct);
}
public interface IRefreshTokenRepository {
    Task AdicionarAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct);
    Task SalvarAlteracoesAsync(CancellationToken ct);
}

// Application/Auth
public interface IAccessTokenGenerator {
    // retorna o JWT assinado e o instante de expiração (UTC)
    (string token, DateTime expiraEm) Gerar(Usuario usuario);
}
public interface IAutenticarUsuarioUseCase {
    Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct);
}
public interface IRenovarTokenUseCase {
    Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}
public interface IRevogarTokenUseCase {
    Task ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}
```

### Claims do JWT

`sub` = `Usuario.Id`; `unique_name` = `NomeUsuario`; `role` = `Perfil.Nome`; `exp`/`iss`/`aud`
padrão. Autorização por perfil nos controllers via `[Authorize(Roles = "...")]` (usada só a
partir da Fase 1; na Fase 0 basta `[Authorize]` em `/me`).

### Config (`appsettings.json` → `JwtOptions`)

```json
"Jwt": {
  "Issuer": "rastreamento-api",
  "Audience": "rastreamento-web",
  "SigningKey": "<chave-forte-por-ambiente-nao-commitar-a-real>",
  "AccessTokenMinutes": 15,
  "RefreshTokenDays": 7
}
```

---

## Acceptance Criteria (testáveis)

1. Rodar `specs/02-modelo-de-dados.sql` (com a tabela `RefreshToken`) num SQL Server limpo
   cria todas as tabelas sem erro.
2. Com o seed aplicado, `POST /auth/login` com credenciais do admin retorna `200`, um
   `accessToken` válido e um cookie `refreshToken` httpOnly.
3. `POST /auth/login` com senha errada retorna `401` sem vazar qual campo falhou.
4. `GET /me` sem token → `401`; com o access token do login → `200` e o `UsuarioDto` correto.
5. `POST /auth/refresh` com o cookie válido retorna novo access token e **novo** cookie; o
   refresh token anterior deixa de funcionar (rotação — segundo uso do antigo dá `401`).
6. `POST /auth/logout` revoga o refresh; `POST /auth/refresh` seguinte com aquele cookie → `401`.
7. Refresh token expirado (via manipulação de `ExpiraEm` no teste) → `401`.
8. No front: tela de login autentica, guarda o access token só em memória, e a Home exibe
   `NomeCompleto`/`Perfil` vindos de `GET /me`. Recarregar a página dispara refresh via cookie
   e mantém a sessão sem novo login (enquanto o refresh for válido).
9. `docs/deploy-manual.md` descreve publicar backend e front num servidor limpo.
10. Timestamps nas respostas da API vêm em ISO 8601 com offset `-03:00` (ex.:
    `accessTokenExpiraEm`), e a Home exibe qualquer data em GMT-3 — enquanto o banco continua
    persistindo em UTC.

---

## Testing approach

- **Domain.Tests (xUnit, sem I/O):** `BCryptPasswordHasher` (hash ≠ senha; `Verificar` true/false);
  `Sha256TokenHasher` (determinístico, mesmo input → mesmo hash).
- **Application.Tests (xUnit + fakes de repositório):** `AutenticarUsuarioUseCase`
  (sucesso, senha errada, usuário inativo); `RenovarTokenUseCase` (token válido rotaciona;
  token revogado/expirado falha); `RevogarTokenUseCase` (marca `RevogadoEm`). `IAccessTokenGenerator`
  pode ser fake nesses testes.
- **Api (integração, `WebApplicationFactory`):** fluxo login → `/me` → refresh → logout,
  cobrindo os critérios 2–7. Usar SQL Server em Docker de teste ou base dedicada.
- **Frontend:** teste manual guiado pelos critérios 8 (automação de UI fica fora do MVP).

Cada tarefa do plano segue o ciclo TDD: escrever teste que falha → rodar e ver falhar →
implementar o mínimo → rodar e ver passar → commit.

---

## Task outline (o `writing-plans` refina em passos bite-sized)

- **T1 — Scaffold da solution.** Criar `Rastreamento.sln` + 4 projetos + 2 de teste
  (`net10.0`), referências entre camadas, `dotnet build` verde.
- **T2 — Schema.** Adicionar `RefreshToken` a `specs/02-modelo-de-dados.sql`; subir SQL Server
  em Docker; rodar o DDL; script de seed (6 perfis + 1 admin com senha BCrypt).
- **T3 — EF Core Database First.** `RastreamentoDbContext` + configurations Fluent API para
  `Usuario`, `Perfil`, `RefreshToken` mapeando o schema existente (sem migrations criadoras).
- **T4 — Password hashing.** `IPasswordHasher` + `BCryptPasswordHasher` (TDD no Domain.Tests).
- **T5 — Token services.** `ITokenHasher`/`Sha256TokenHasher` + `IAccessTokenGenerator`/
  `JwtAccessTokenGenerator` + `JwtOptions` (TDD onde aplicável).
- **T6 — Login.** `AutenticarUsuarioUseCase` (TDD) + `AuthController.Login` setando o cookie.
- **T7 — Refresh + logout.** `RenovarTokenUseCase`/`RevogarTokenUseCase` (TDD) + endpoints,
  com rotação e revogação.
- **T8 — Autorização + `/me`.** Configurar JWT bearer no `Program.cs` (CORS com credenciais só
  como fallback); `GET /me` com `[Authorize]`; teste de integração dos critérios 2–7.
- **T9 — Scaffold do front.** Vite + React + TS + Tailwind; proxy do Vite (`server.proxy`) de
  `/auth` e `/me` para a API (mesma origem); `api/client.ts` com `credentials: 'include'`.
- **T10 — Auth no front.** `AuthContext` (access em memória), `ProtectedRoute`, `LoginPage`,
  interceptor de 401 → `/auth/refresh` → retry.
- **T11 — Home protegida + deploy.** `HomePage` consumindo `GET /me`; `docs/deploy-manual.md`.

---

## Decisões resolvidas (não reabrir sem justificativa nova)

Estas escolhas fecham os "pontos em aberto" que `specs/03-arquitetura-tecnica.md` deixou para
a Fase 0:

| Tema | Decisão |
|---|---|
| Hash de senha | **BCrypt** (`BCrypt.Net-Next`), não ASP.NET Core Identity |
| Versão .NET | **.NET 10 (LTS)** — `net10.0` |
| UI do frontend | **Tailwind + componentes próprios** (não MUI) |
| Sessão | **Access token (memória) + refresh token (cookie httpOnly)** |
| Refresh token | **Opaco, hash SHA-256 na tabela `RefreshToken`**, com rotação e revogação |

---

## Referências (specs de domínio — fonte da verdade)

- `specs/00-visao-geral.md` — objetivo, perfis, stack.
- `specs/01-dominio-e-regras-de-negocio.md` — glossário e regras (perfis restringem telas).
- `specs/02-modelo-de-dados.sql` — DDL (fonte da verdade do schema; recebe `RefreshToken`).
- `specs/03-arquitetura-tecnica.md` — camadas, EF Database First, JWT.
- `specs/06-roadmap-mvp.md` — Fase 0 no contexto das demais fases.
