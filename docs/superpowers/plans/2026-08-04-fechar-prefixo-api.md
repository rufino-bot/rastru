# Fechar o prefixo `/api` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** fazer os caminhos nus (`/setores`, `/auth/login`, `/me`, …) pararem de responder, de modo
que a API só exista sob `/api` — fechando a colisão de caminho entre as rotas do SPA e os endpoints.

**Architecture:** `app.UsePathBase("/api")` **não é branch**: ele retira o prefixo quando existe e
deixa passar quando não existe, então hoje a API responde nos dois caminhos. O fechamento é um
middleware logo depois dele que devolve **404** quando `Request.PathBase` vem vazio — três linhas,
pipeline intacto, e diretamente matável por mutação. A alternativa (`app.Map("/api", …)`) exigiria
remontar rate limiter, autenticação, autorização e roteamento dentro do branch: mais peça móvel
para o mesmo efeito. **Decisão do usuário em 2026-08-04.**

**Tech Stack:** ASP.NET Core (.NET 10), xUnit + `WebApplicationFactory`, SQL Server em Docker.

## Global Constraints

- **Baseline de partida, medida em 2026-08-04 nesta worktree:** backend **259** (101 Application +
  27 Infrastructure + 131 Api) · front **45** · `dotnet build -warnaserror` em **0 warnings**.
- **Baseline de chegada: backend 260.** A Task 2 troca 5 testes de prefixo por 6 (detalhe e
  contagem na própria task). Qualquer outro número é regressão, não arredondamento.
- **Pré-requisito de ambiente:** o SQL Server precisa estar no ar — ~104 testes batem em banco real.
  **NÃO rodar `docker compose up -d` dentro da worktree:** o nome do projeto Compose sai do nome do
  diretório, então isso cria um stack paralelo com banco **vazio** e colide na porta 1433. O
  container certo é o do checkout principal, `tcc-sqlserver-1`, e ele já responde em `localhost:1433`.
  Conferir com `docker ps`; se estiver parado, subir a partir do checkout principal.
- **Nomes de domínio em português**, espelhando o DDL (convenção do `CLAUDE.md`).
- **Não** adicionar `@testing-library/react`, **não** reindentar nada, **não** renomear
  `Pedido.Numero`, **não** mexer no front: `web/src/api/client.ts` já aplica o prefixo num lugar só
  (`rota()`) e os call sites passam o caminho **sem** prefixo — está correto e fica como está.
- **Atalho proibido:** um `DelegatingHandler` que prefixasse toda requisição de teste zeraria o
  diff, mas os testes deixariam de provar a URL real — o oposto exato da convenção F4. Foi recusado
  de propósito; não reabrir.

---

## File Structure

| Arquivo | Responsabilidade nesta mudança |
|---|---|
| `src/Rastreamento.Api/Program.cs` | Ganha o middleware que recusa `PathBase` vazio, logo após o `UsePathBase`, antes do rate limiter. Comentário de bloco deixa de descrever transição. |
| `src/Rastreamento.Api/Controllers/AuthController.cs` | Só o comentário do `Path` do cookie: a derivação continua, a justificativa muda. |
| `tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs` | **A prova do fechamento.** O teste "os dois prefixos respondem" é invertido para "o caminho nu não responde". |
| `tests/Rastreamento.Api.Tests/*EndpointsTests.cs` + `LockoutTests.cs` + `ReusoDeRefreshTokenTests.cs` + `RateLimitTests.cs` + `CorridaNaQueimaDeFamiliaTests.cs` | 127 URLs literais passam a carregar o prefixo. Mecânico. |
| `specs/05-api-endpoints.md` | A seção "Estado de transição" deixa de ser verdade e vira a regra definitiva. |
| `CLAUDE.md` | A seção "Prefixo `/api` — em transição…" idem. |

**Ordem das tasks é obrigatória e não é estética:** a reescrita das URLs vem **antes** da guarda.
Invertido, a Task 1 deixaria a suíte vermelha em ~127 pontos e ninguém saberia dizer se o vermelho é
a guarda funcionando ou um erro de digitação de URL. Reescrevendo primeiro — enquanto os dois
caminhos ainda respondem — cada task fecha verde por si.

---

### Task 1: Reescrever as 127 URLs nuas dos testes de endpoint

**Files:**
- Modify: `tests/Rastreamento.Api.Tests/AgrupamentosEndpointsTests.cs` (29 URLs)
- Modify: `tests/Rastreamento.Api.Tests/AuthEndpointsTests.cs` (20 URLs + 2 asserts de `path=`)
- Modify: `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs` (18)
- Modify: `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs` (18)
- Modify: `tests/Rastreamento.Api.Tests/MateriaisEndpointsTests.cs` (18)
- Modify: `tests/Rastreamento.Api.Tests/ReusoDeRefreshTokenTests.cs` (8)
- Modify: `tests/Rastreamento.Api.Tests/LockoutTests.cs` (7)
- Modify: `tests/Rastreamento.Api.Tests/RateLimitTests.cs` (2)
- Modify: `tests/Rastreamento.Api.Tests/CorridaNaQueimaDeFamiliaTests.cs` (1)
- **NÃO tocar:** `tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs` — é da Task 2, e
  as URLs nuas dele são deliberadas até lá.

**Interfaces:**
- Consumes: nada.
- Produces: toda a suíte de endpoints passa a exercitar `/api/...`. A Task 2 depende disso para
  poder ligar a guarda sem quebrar 127 pontos.

- [ ] **Step 1: Medir a baseline antes de tocar em nada**

```bash
dotnet test Rastreamento.slnx 2>&1 | grep -E "Aprovado|Com falha"
```

Esperado: `101`, `27` e `131` aprovados, 0 falhas. Se não bater, **pare** — o problema é ambiente
(banco fora do ar), não o plano.

- [ ] **Step 2: Aplicar a reescrita mecânica nos 9 arquivos**

A forma das URLs é uniforme (`"/x…"` e `$"/x…"`), então um `sed` resolve. O `"/me"` é tratado à
parte porque não compartilha prefixo com os outros e é o que a primeira contagem deixou escapar.

```bash
cd tests/Rastreamento.Api.Tests
for f in AgrupamentosEndpointsTests.cs AuthEndpointsTests.cs SetoresEndpointsTests.cs \
         PedidosEndpointsTests.cs MateriaisEndpointsTests.cs ReusoDeRefreshTokenTests.cs \
         LockoutTests.cs RateLimitTests.cs CorridaNaQueimaDeFamiliaTests.cs; do
  sed -i \
    -e 's|"/\(auth\|setores\|materiais\|pedidos\|agrupamentos\|usuarios\)|"/api/\1|g' \
    -e 's|"/me"|"/api/me"|g' \
    "$f"
done
```

Por que é seguro: o padrão exige a **aspa** antes da barra, então `"path=/auth"` (que não começa
com `"/`) não é tocado, e `$"/pedidos/{id}"` é, porque a aspa vem logo antes da barra. Nenhum
desses 9 arquivos contém `"/api` hoje — conferido — então não há como duplicar prefixo.

- [ ] **Step 3: Conferir a contagem, em vez de confiar no `sed`**

```bash
cd ../..
grep -rhoE '"\$?/[a-zA-Z][^"]*"|\$"/[^"]*"' tests/ --include=*.cs | grep -vc '"/api'
```

Esperado: **4** — e são exatamente as 4 URLs nuas de `AuthEndpointsTests.PrefixoDeApi.cs`
(`"/setores"`, `"/auth/login"` e as duas de `path=`), que a Task 2 resolve. Qualquer número maior
significa URL que o `sed` não pegou.

- [ ] **Step 4: Corrigir os dois asserts de `Path` do cookie**

Estes não casam com o padrão do `sed` de propósito (não começam com `"/`), e **quebrariam agora**:
o login passou a ser feito em `/api/auth/login`, então o cookie sai com `Path=/api/auth` e
`Contains("path=/auth")` deixa de ser verdade — `"path=/auth"` não é substring de `"path=/api/auth"`.

Em `tests/Rastreamento.Api.Tests/AuthEndpointsTests.cs`, linha 67:

```csharp
    Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
```

E linha 305 (o cookie de remoção do logout):

```csharp
    Assert.Contains("path=/api/auth", cookieDeRemocao, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 5: Rodar a suíte inteira**

```bash
dotnet test Rastreamento.slnx 2>&1 | grep -E "Aprovado|Com falha"
```

Esperado: **259 aprovados, 0 falhas** — o mesmo número do Step 1. A API ainda responde nos dois
caminhos, então mudar o lado do cliente não muda resultado nenhum. É justamente isso que torna esta
task segura de fazer isolada.

- [ ] **Step 6: Commit**

```bash
git add tests/Rastreamento.Api.Tests
git commit -m "test(api): endpoints passam a exercitar as URLs sob /api"
```

---

### Task 2: A guarda — o caminho nu deixa de responder

**Files:**
- Modify: `src/Rastreamento.Api/Program.cs:147-161` (bloco de comentário + `UsePathBase`)
- Modify: `tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs`

**Interfaces:**
- Consumes: a suíte já sob `/api` (Task 1).
- Produces: nada que outra task consuma. É o fim funcional do trabalho; a Task 3 é registro.

**Contagem de testes, prevista:** hoje o arquivo tem 5 testes (Theory de 2 casos "os dois prefixos
respondem" + Theory de 2 casos do cookie + 1 Fact de refresh). Passa a ter 6 (Theory de 3 casos do
404 + 1 Fact do prefixo vivo + 1 Fact do cookie + 1 Fact de refresh). **Baseline vai de 259 para 260.**

- [ ] **Step 1: Escrever os testes que falham**

Substituir, em `tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs`, os dois primeiros
métodos (`Os_dois_prefixos_respondem_enquanto_durar_a_transicao` e
`Cookie_de_refresh_acompanha_o_prefixo_que_atendeu`) por:

```csharp
  /// <summary>
  /// O fechamento da transicao. <c>UsePathBase</c> sozinho NAO ramifica — ele tira o prefixo
  /// quando existe e deixa passar quando nao existe, entao ate aqui a API respondia nos DOIS
  /// caminhos e a colisao com as rotas do SPA (/setores, /pedidos, /pedidos/:id) continuava de pe.
  /// 404, e nao 401, e o discriminador: 401 significaria que a rota casou e so faltou token.
  /// </summary>
  [Theory]
  [InlineData("/setores")]
  [InlineData("/auth/login")]
  [InlineData("/me")]
  public async Task Caminho_sem_o_prefixo_nao_responde(string caminho)
  {
    var resposta = await _factory.CreateClient().GetAsync(caminho);

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// O par do teste acima: sem ele, apagar o <c>UsePathBase</c> inteiro deixaria a suite verde
  /// (tudo 404) e o 404 pareceria sucesso.
  /// </summary>
  [Fact]
  public async Task Caminho_sob_o_prefixo_responde()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/setores");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  /// <summary>
  /// O <c>Path</c> do cookie de refresh acompanha o <c>PathBase</c>. Com prefixo unico isso da
  /// sempre <c>/api/auth</c>, mas a derivacao continua sendo o que impede o cookie de ser gravado
  /// fora do alcance do <c>/api/auth/refresh</c> — trocar por <c>"/auth"</c> literal quebra o
  /// teste seguinte.
  /// </summary>
  [Fact]
  public async Task Cookie_de_refresh_acompanha_o_prefixo()
  {
    var resposta = await NovoCliente().PostAsJsonAsync("/api/auth/login", Credenciais);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Contains("path=/api/auth", CookieDeRefresh(resposta), StringComparison.OrdinalIgnoreCase);
  }
```

O terceiro método do arquivo (`Refresh_sob_o_prefixo_novo_recebe_o_cookie_de_volta`) **fica como
está** — e é ele que prova, ponta a ponta, que o cookie volta.

Atualizar também o comentário de classe, que hoje diz que o resto da suíte bate nos caminhos nus:

```csharp
/// <summary>
/// Guardas do prefixo <c>/api</c> (<c>UsePathBase</c> + a recusa de <c>PathBase</c> vazio, em
/// Program.cs). Vivem aqui, e nao numa classe propria, porque o <c>DisposeAsync</c> de
/// <see cref="AuthEndpointsTests"/> ja limpa os <c>RefreshToken</c> de <c>admin</c> — uma classe
/// nova deixaria linha orfa no banco.
///
/// Por que estes testes existem: o resto da suite exercita URLs sob <c>/api</c> e ficaria verde
/// mesmo que o caminho nu voltasse a responder. Sem estas guardas, a colisao com as rotas do SPA
/// poderia ser reaberta sem que nada acusasse.
/// </summary>
```

- [ ] **Step 2: Rodar e ver falhar**

```bash
dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~PrefixoDeApi" 2>&1 | grep -E "Aprovado|Com falha"
```

Esperado: **falha nos 3 casos de `Caminho_sem_o_prefixo_nao_responde`**, com `401` recebido onde se
esperava `404` — a guarda ainda não existe. Os demais passam.

- [ ] **Step 3: Escrever a guarda**

Em `src/Rastreamento.Api/Program.cs`, logo depois de `app.UsePathBase("/api");` (linha 161) e
**antes** de `app.UseRateLimiter();`:

```csharp
// UsePathBase sozinho nao fecha nada: ele TIRA o prefixo quando existe e deixa passar quando nao
// existe, entao sem esta guarda a API responderia tambem em /setores, /auth/login, /me — os mesmos
// caminhos das rotas do SPA. Requisicao sem PathBase e requisicao que nao passou por /api.
// Vem antes do rate limiter de proposito: recusar o caminho errado nao deve custar nem particao.
app.Use(async (contexto, proximo) =>
{
  if (!contexto.Request.PathBase.HasValue)
  {
    contexto.Response.StatusCode = StatusCodes.Status404NotFound;
    return;
  }

  await proximo();
});
```

- [ ] **Step 4: Rodar a suíte inteira**

```bash
dotnet build Rastreamento.slnx -warnaserror 2>&1 | tail -3
dotnet test Rastreamento.slnx 2>&1 | grep -E "Aprovado|Com falha"
```

Esperado: 0 warnings e **260 aprovados** (101 + 27 + **132**), 0 falhas.

- [ ] **Step 5: Provar a guarda por mutação — obrigatório, não opcional**

Neste projeto todo achado grande veio de apagar uma guarda e ver a suíte seguir verde. Comentar o
bloco `app.Use(...)` do Step 3 e rodar:

```bash
dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~PrefixoDeApi" 2>&1 | grep -E "Aprovado|Com falha"
```

Esperado: **os 3 casos de `Caminho_sem_o_prefixo_nao_responde` morrem, e só eles.** Se morrer menos,
a guarda não está sendo exercitada; se morrer mais, algum outro teste depende do caminho nu e isso
precisa ser entendido antes de seguir. Restaurar o bloco depois (edição direta — **não**
`git checkout`, que levaria junto o que ainda não está commitado) e reconfirmar o verde.

- [ ] **Step 6: Commit**

```bash
git add src/Rastreamento.Api/Program.cs tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs
git commit -m "feat(api): caminho sem o prefixo /api deixa de responder"
```

---

### Task 3: O registro — comentários e specs deixam de descrever uma transição que acabou

**Files:**
- Modify: `src/Rastreamento.Api/Program.cs:147-160` (bloco de comentário acima do `UsePathBase`)
- Modify: `src/Rastreamento.Api/Controllers/AuthController.cs:143-149` (comentário do `Path`)
- Modify: `specs/05-api-endpoints.md:18-26`
- Modify: `CLAUDE.md` (seção "Prefixo `/api` — em transição…")

**Interfaces:**
- Consumes: o comportamento fechado pela Task 2.
- Produces: nada em código.

Sem esta task, três documentos passam a mentir — e este projeto já foi mordido três vezes por
divergência entre registro e disco. É task de valor próprio, não "limpeza".

- [ ] **Step 1: `Program.cs` — o comentário acima do `UsePathBase`**

Substituir os parágrafos que descrevem o serviço duplo (hoje linhas 153-160, do "UsePathBase nao e
branch" até "Passo seguinte, em commit proprio.") por:

```csharp
// O prefixo e a UNICA forma de alcancar a API: a guarda logo abaixo recusa quem chega sem
// PathBase. Ate 2026-08-04 os dois caminhos respondiam, para o front migrar sem reescrever as 127
// URLs literais dos testes de endpoint de uma vez; essa transicao acabou.
```

Os parágrafos anteriores (a colisão de caminho, o `SameSite=Strict`) continuam válidos e ficam.

- [ ] **Step 2: `AuthController.cs` — a justificativa da derivação do `Path`**

Substituir o comentário de `Path = Request.PathBase.Add("/auth").Value` por:

```csharp
    // Derivado do PathBase, e nao literal: o cookie tem que ser gravado sob o mesmo prefixo em que
    // /auth/refresh atende, senao o navegador nao o reenvia e a sessao morre no primeiro refresh.
    // Com prefixo unico isso da sempre "/api/auth" — trocar pela literal "/api/auth" seria
    // equivalente HOJE e nenhum teste acusaria; a derivacao sobrevive por acompanhar o PathBase se
    // ele mudar no deploy, nao por estar sendo discriminada por teste. Ja a literal "/auth" quebra
    // `Refresh_sob_o_prefixo_novo_recebe_o_cookie_de_volta`.
    Path = Request.PathBase.Add("/auth").Value,
```

- [ ] **Step 3: `specs/05-api-endpoints.md`**

Trocar o parágrafo "**Estado de transição (não é o destino).**" (linhas 18-22) e o parágrafo
seguinte sobre o `Path` do cookie (linhas 24-26) por:

```markdown
**Fechado em 2026-08-04.** O prefixo entra por `UsePathBase`, que retira `/api` quando presente e
deixaria passar quando ausente — sozinho ele faria a API responder também nos caminhos nus. Por
isso há uma guarda logo depois dele: requisição sem `PathBase` recebe **404**. Os caminhos nus
(`/setores`, `/auth/login`, `/me`) **não respondem**, e é isso que fecha a colisão com as rotas do
SPA. Provado por `AuthEndpointsTests.PrefixoDeApi.cs`.

O `Path` do cookie de refresh é derivado do `PathBase` (`/api/auth`), e não literal: o cookie
precisa ser gravado sob o mesmo prefixo em que `/auth/refresh` atende, senão o navegador não o
reenvia e a sessão morre no primeiro refresh.
```

- [ ] **Step 4: `CLAUDE.md`**

A seção hoje se chama "Prefixo `/api` — em transição, e a metade que falta é obrigatória antes do
deploy". Renomear para "Prefixo `/api`" e substituir os dois parágrafos sobre o que "ainda NÃO está
feito" e sobre o `Path` do cookie por:

```markdown
A API é servida **somente** sob `/api` (`app.UsePathBase("/api")` em `Program.cs`, mais a guarda
logo abaixo que devolve 404 para requisição sem `PathBase`). O front nunca escreve o prefixo à mão:
quem o aplica é o `rota()` de `web/src/api/client.ts`, e chamada nova passa o caminho **sem**
prefixo (`/setores`) — escrever `/api/...` no call site duplicaria.

Existe por uma colisão real: as rotas do SPA (`/setores`, `/materiais`, `/pedidos`, `/pedidos/:id`)
têm os mesmos caminhos dos endpoints, e sem prefixo dar F5 numa dessas telas faz o navegador pedir o
**documento** à API, que responde 401 (navegação não carrega `Authorization: Bearer`). Aconteceu no
e2e da Fase 1A.

**Fechado em 2026-08-04.** Até então `UsePathBase` respondia nos dois caminhos — ele não é branch,
tira o prefixo quando existe e deixa passar quando não existe — e enquanto os caminhos nus
respondessem a colisão de produção continuava de pé. A guarda fechou isso e as 127 URLs literais dos
testes de endpoint foram reescritas junto. O `Path` do cookie de refresh acompanha o `PathBase`
(`/api/auth`) em vez de ser literal, para o cookie ser gravado sob o mesmo prefixo em que o refresh
atende. Coberto por `AuthEndpointsTests.PrefixoDeApi.cs`.
```

Remover também, da seção de pré-requisitos de teste, qualquer frase que ainda diga que os testes
batem nos caminhos nus — conferir com `grep -n "caminho nu\|caminhos nus\|nus" CLAUDE.md`.

- [ ] **Step 5: Confirmar que nenhum documento ainda promete o serviço duplo**

```bash
grep -rn "dois prefixos\|dois caminhos\|servico duplo\|serviço duplo\|transicao\|transição" \
  CLAUDE.md specs/05-api-endpoints.md src/Rastreamento.Api/Program.cs \
  src/Rastreamento.Api/Controllers/AuthController.cs
```

Esperado: só ocorrências que falam da transição **no passado** ("até 2026-08-04", "essa transição
acabou"). Nenhuma no presente.

- [ ] **Step 6: Suíte inteira e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror 2>&1 | tail -3
dotnet test Rastreamento.slnx 2>&1 | grep -E "Aprovado|Com falha"
git add CLAUDE.md specs/05-api-endpoints.md src/Rastreamento.Api
git commit -m "docs: prefixo /api deixa de ser transicao e vira a regra"
```

Esperado: 0 warnings, 260 aprovados, 0 falhas.

---

## Fora do escopo, de propósito

- **Front.** Já correto: `rota()` prefixa num lugar só e os call sites passam caminho nu para o
  `apiFetch`. `web/vite.config.ts` já tem uma entrada de proxy só (`/api`) e nenhum `bypass`.
  A suíte do front (45) não deve ser afetada; se for, é sinal de que algo saiu do escopo.
- **`UseHttpsRedirection`, `SigningKey` como segredo de ambiente, limpeza de `RefreshToken`
  expirado.** Continuam em aberto — são itens de deploy, e este plano não é sobre deploy.
- **Fase 1B (`Componente` + receita padrão).** Ganha plano próprio, depois deste.
