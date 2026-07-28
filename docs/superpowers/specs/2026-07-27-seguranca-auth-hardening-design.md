# Hardening de autenticação: reuso de refresh token + rate limit + lockout + logging

**Data:** 2026-07-27
**Tipo:** endurecimento de segurança sobre a auth da Fase 0 (já mergeada na `main`)
**Branch:** `seguranca-auth-hardening` (a partir da `main`)
**Motivação:** fechar itens da "dívida consciente" de segurança do backend de auth. Três
deles saem da dívida agora: detecção de reuso de refresh token, proteção contra
brute-force no login (rate limit + lockout de conta) e logging de eventos de auth.

## Contexto

O backend de auth da Fase 0 está limpo e defendido: login com **trabalho constante**
(BCrypt roda sempre, mesmo para usuário inexistente/inativo — sem oráculo de timing),
falha sempre em **401 genérico** (`AutenticarUsuarioUseCase.cs`), e rotação de refresh
token **atômica** (`EmissorDeSessao.RotacionarAsync`). O que falta:

1. O campo `RefreshToken.SubstituidoPorTokenHash` é **gravado mas nunca lido** — não há
   reação ao sinal de roubo de token.
2. O `Program.cs` não tem `UseRateLimiter`, e não há `ILogger` no caminho de auth.

Este design NÃO reabre decisões fechadas (login próprio + JWT, lote agregado, etc.) e
mantém as invariantes de segurança existentes (sem oráculo, 401 genérico, trabalho
constante, refresh só em cookie httpOnly, hash de token nunca em claro).

### Independência do PR #2 (domínio)

Esta branch sai da `main` e toca **apenas** a auth (tabela `Usuario` + código de auth). O
PR #2 (alterações de domínio) edita o `02-modelo-de-dados.sql` em **outras tabelas**
(`Kit`/`Agrupamento`/`EstruturaItem`/dimensional), sem sobreposição. Para não editar a
fonte-da-verdade do schema em duas branches paralelas, **recomenda-se mergear o PR #2
primeiro** e, se possível, rebasear/recriar esta branch sobre a `main` atualizada antes de
executar a parte de schema. Como as edições são em tabelas distintas, a ordem de merge não
quebra nenhuma das duas — é só higiene.

---

## Seção A — Detecção de reuso de refresh token

### Comportamento

Fluxo novo no `RenovarTokenUseCase`:

1. Buscar o token **por hash, em qualquer estado** (`ObterPorHashAsync` — método novo; o
   atual `ObterAtivoPorHashAsync` filtra `RevogadoEm IS NULL` e nunca enxerga um token
   revogado).
2. Decidir:
   - **Não encontrado** → 401 genérico.
   - **Encontrado, `RevogadoEm != null`** → **REUSO DETECTADO.** Revoga **todos** os refresh
     tokens ativos daquele `UsuarioId` (`RevogarTodosAtivosDoUsuarioAsync`,
     `RevogadoEm = agora`, num único `SaveChanges`), loga Warning (Seção D) e devolve o
     **mesmo 401 genérico**. Não emite sessão.
   - **Encontrado e ativo, mas expirado (`ExpiraEm <= agora`) ou `!Usuario.Ativo`** → 401
     genérico, **sem** queimar a família (expiração/inatividade não é sinal de roubo).
   - **Encontrado, ativo e válido** → rotaciona normalmente (`RotacionarAsync`).

### Racional da resposta a reuso

Cenário de roubo: atacante rouba o token A, usa **primeiro** → recebe B (sessão válida). O
legítimo depois tenta A → hoje volta 401 mas a sessão B do atacante **sobrevive**. Queimar
todos os tokens ativos do usuário no momento em que o token A (já rotacionado) reaparece
derruba a sessão B do atacante (que só tem o refresh, não a senha) e força o legítimo a
refazer login. É a recomendação padrão do OWASP ("reuse detected → invalidate token
family"); optou-se por **queimar todos os tokens do usuário**, não só a cadeia, por ser
mais simples/robusto e porque num roubo confirmado se quer derrubar tudo.

### Propriedades preservadas

- **Sem oráculo:** todos os caminhos de falha devolvem o mesmo 401 genérico; o caminho de
  reuso é indistinguível de "token inválido" para quem chama. A queima é efeito colateral
  só no banco.
- **Atomicidade:** revogação em massa num único `SaveChanges`, mesmo padrão Scoped/DbContext
  atual.

### Limite conhecido (inerente ao mecanismo)

Só detecta quando o token **antigo** (já rotacionado) é reapresentado. Se o atacante roubar
o token **atual** e o legítimo nunca replayar o antigo, não há sinal de reuso — a defesa
nesse caso é a expiração natural do refresh. Documentado, não é falha do design.

### Arquivos

- `IRefreshTokenRepository` + `RefreshTokenRepository`: `ObterPorHashAsync(hash, ct)`
  (retorna por hash em qualquer estado, com `Usuario`+`Perfil` incluídos) e
  `RevogarTodosAtivosDoUsuarioAsync(usuarioId, revogadoEm, ct)`.
- `RenovarTokenUseCase`: reestrutura a decisão descrita acima.
- Sem schema. `RefreshToken` já tem os campos.

---

## Seção B — Lockout de conta

### Schema (Database First — `02-modelo-de-dados.sql`, tabela `Usuario`)

- `FalhasConsecutivas INT NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0)`
- `BloqueadoAte DATETIME2 NULL` — nulo = não bloqueado.

### Config (`LockoutOptions`, validada no startup)

- `MaxFalhas` (default **5**) — falhas consecutivas até trancar.
- `DuracaoMinutos` (default **15**) — trava temporária, auto-expira (sem lockout permanente
  que exija admin).

### Lógica (`AutenticarUsuarioUseCase`, mantendo o padrão oracle-safe)

- **Trabalho constante preservado:** o BCrypt roda SEMPRE (usuário inexistente/inativo usa
  `HashFicticio`), como já é hoje.
- Decisão:
  - Usuário existe, ativo, **não** trancado (`BloqueadoAte` nulo ou no passado) e **senha
    confere** → **sucesso**: zera `FalhasConsecutivas`, limpa `BloqueadoAte`, salva, emite
    sessão.
  - Usuário existe, ativo, **trancado** (`BloqueadoAte > agora`) → **falha** mesmo com senha
    correta; não incrementa (não estende a trava). 401 genérico.
  - Usuário existe, ativo, não trancado, **senha errada** → incrementa `FalhasConsecutivas`;
    se atingiu `MaxFalhas`, seta `BloqueadoAte = agora + DuracaoMinutos` e zera o contador
    (recomeça limpo após a trava). 401 genérico.
  - Usuário inexistente/inativo → 401 genérico, sem tocar contador.

### Propriedades e resíduos (conscientes)

- **Sem oráculo no corpo:** toda falha — inclusive conta trancada e "senha certa mas
  trancada" — devolve o **mesmo** 401 genérico. Atacante não distingue trancada de senha
  errada.
- **Resíduo de timing aceito:** o incremento/trava é um `UPDATE` que só ocorre para usuário
  **existente com senha errada**; inexistente não escreve. É uma diferença de timing de
  escrita, muito menor e mais difícil de explorar que o oráculo de ~100ms de BCrypt já
  fechado. O corpo continua idêntico.
- **Lockout-DoS aceito:** trava **temporária** que expira sozinha em `DuracaoMinutos` — um
  atacante trancar um operador só atrasa o acesso dele por X min, sem intervenção de admin.
- **Concorrência:** duas tentativas erradas simultâneas podem competir pelo contador
  (off-by-one no pior caso). Aceitável no MVP.

### Arquivos

- `02-modelo-de-dados.sql` (`Usuario`: 2 colunas), `UsuarioConfiguration` (mapeamento EF),
  entidade `Usuario` (2 props), `IUsuarioRepository`/`UsuarioRepository` (método de save;
  garantir que `ObterPorNomeUsuarioAsync` retorne entidade **rastreada**),
  `AutenticarUsuarioUseCase`, nova `LockoutOptions` + validador, `Program.cs` (registro +
  `ValidateOnStart`).

---

## Seção C — Rate limiting

### Política

- Middleware nativo do ASP.NET Core (`AddRateLimiter` + `UseRateLimiter` no `Program.cs`),
  sem dependência externa.
- **Escopo: só `/auth/login`.** `/auth/refresh` fica **de fora** — é legítimo e frequente
  (a cada ~15 min por usuário + retries), e throttlar puniria o operador em wifi lento (o
  single-flight do front já mitiga concorrência); refresh token é opaco de 256 bits, então
  brute-force nele é inviável. O alvo real é o login.
- **Partição por IP** (`RemoteIpAddress`), **janela fixa**, configurável via
  `RateLimitOptions`: default **10 tentativas / 60s por IP**.
- Rejeição → **429 Too Many Requests** com `Retry-After`, e log (Seção D).

### Camadas complementares

Rate limit é o teto grosso **por IP** (barra flood rápido de uma origem); lockout (Seção B)
é a trava fina **por conta**. Um não substitui o outro.

### Notas

- **Resíduo de front:** o front mapeia falha de auth pro erro genérico; um 429 cai nesse
  caminho. Como só dispara sob abuso, o operador legítimo não encosta. Mensagem dedicada
  ("muitas tentativas, tente em X") é polish de front, **fora deste escopo**.
- **Deploy atrás de proxy:** se entrar um proxy reverso, configurar `ForwardedHeaders` para
  a partição usar o IP real do cliente (senão todos compartilham o IP do proxy = throttle
  global acidental). Flag de deploy.

### Arquivos

- `Program.cs` (rate limiter + pipeline), nova `RateLimitOptions` + validação. Sem schema.

---

## Seção D — Logging de auth

### Abordagem

Via `ILogger` (log estruturado da aplicação), **não** tabela de auditoria persistente
(feature maior; anotada como evolução futura).

### Eventos e níveis

- Login **sucesso** → Information (usuário, IP).
- Login **falha** → Warning (usuário tentado, IP).
- **Conta trancada** (atingiu o limite) → Warning (usuário, IP, até quando).
- **Reuso de refresh detectado** → Warning (userId) — sinal de roubo, o mais importante.
- **Rejeição por rate limit** (429) → Warning (IP).

**Nunca logar:** senha, refresh token (plano ou hash), access token.

### Fronteira de camadas

- `AuthController` loga o desfecho grosso de login/refresh + IP (tem `HttpContext`).
- Os casos de uso (`AutenticarUsuarioUseCase`, `RenovarTokenUseCase`) logam os eventos de
  segurança específicos (lockout, reuso) com userId/usuário, **sem** `HttpContext` — a
  camada Application continua sem depender dele.
- O 429 é logado no `OnRejected` do rate limiter.
- Níveis controlados pela seção `Logging` padrão do appsettings.

### Arquivos

- `AuthController`, `AutenticarUsuarioUseCase`, `RenovarTokenUseCase`, `Program.cs`; pacote
  `Microsoft.Extensions.Logging.Abstractions` no projeto Application.

---

## Cross-cutting

### Config

Duas novas options — `LockoutOptions` (MaxFalhas=5, DuracaoMinutos=15) e `RateLimitOptions`
(PermitLimit=10, WindowSeconds=60) — bindadas do appsettings e **validadas no startup**,
mesmo padrão do `JwtOptionsValidator` (`IValidateOptions<T>` + `ValidateOnStart`). Defaults
no `appsettings.json`.

### Testes

- **Application (fakes):** reuso queima todos os tokens; expirado/inativo **não** queima;
  lockout incrementa, tranca no limite, trancada devolve 401 **mesmo com senha certa**,
  destranca após expirar; trabalho constante preservado (BCrypt sempre).
- **Infrastructure (banco real):** `ObterPorHashAsync` acha token revogado;
  `RevogarTodosAtivosDoUsuarioAsync` revoga todos os ativos do usuário; mapeamento das novas
  colunas de `Usuario`.
- **Api (ponta-a-ponta):** 429 após N tentativas no `/auth/login`; reuso e lockout fim-a-fim.
- **Não-oráculo:** reuso ≡ token inválido (corpo idêntico); trancada ≡ senha errada (corpo
  idêntico).

### Fora de escopo (deferido, consciente)

- Logout invalidando o access token (denylist / validação por request) — tradeoff
  arquitetural, aceito no MVP.
- Tabela de auditoria persistente.
- Limpeza de linhas `RefreshToken` expiradas.
- Segredos de deploy (`SigningKey` como segredo de ambiente, `UseHttpsRedirection`).
- Mensagem de 429 no front.

## Pontos em aberto

Nenhum. As quatro seções e o cross-cutting estão fechados e prontos para virar plano de
implementação.
