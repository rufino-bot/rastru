# CI/CD, parte 2 — a aplicação pronta para container — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma imagem única do Rastru (API + SPA) que sobe num Docker Compose com SQL Server Express e Caddy, cria ou migra o banco do ambiente sozinha, nunca deixa usuário com a senha do repositório, e passa a verificação C13 — tudo verificável sem VPS.

**Architecture:** A API passa a servir o build do Vite quando a pasta `wwwroot` tem `index.html` (modo SPA), sem mexer no modo só-API dos testes e do dev; ganha `GET /api/saude`, `ForwardedHeaders` restrito a uma sub-rede de configuração e três comandos de linha (`banco preparar`, `definir-senha`, `conferir-senhas`) que rodam da própria imagem. O trabalho de banco (executar scripts com `GO`, migrar com `dbo.MigracaoAplicada`, backup, criar banco novo de forma atômica, login SQL do ambiente, senhas) mora em `Rastreamento.Infrastructure/Banco/`, com SqlClient, e é testado contra o SQL Server real em bancos descartáveis. O `deploy/` traz o `compose.yaml`, o `Caddyfile`, um `subir-local.sh` que sobe tudo com segredos aleatórios e um `verificar.sh` (C13), que um job novo do CI roda em todo PR.

**Tech Stack:** .NET 10 (ASP.NET Core, Microsoft.Data.SqlClient que já vem com o EF), Node 22 / Vite, Docker multi-stage (`mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0`, `node:22-bookworm-slim`), Docker Compose, SQL Server 2022 Express, Caddy 2, bash, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-25-ci-cd-design.md` — §8 item 2 (escopo), §5 (imagem e API), §6 (banco), §10 (riscos e "ao builder"); as chamadas C4–C17 e a D11.

## Global Constraints

- Imagem: multi-stage, runtime `aspnet` **com ICU** (nada de `InvariantGlobalization` nem *chiseled* sem `-extra`); sem `tzdata`; usuário não-root; expõe só a porta HTTP interna (8080); **nenhum segredo dentro dela** (C7, C8, §5.3).
- A imagem leva `specs/02-modelo-de-dados.sql`, `db/seed.sql`, `db/seed-demo.sql`, `db/massa-vitrine.sql` e `db/migracoes/` — o deploy usa os scripts **da versão que está subindo** (§5.3).
- `.dockerignore` deixa fora `.superpowers/`, `node_modules`, `bin/obj`, `tests/` e `db/massa-*` **exceto** `db/massa-vitrine.sql` (§5.3).
- Tag de imagem nunca `latest` no deploy (C8): o Compose exige a imagem por variável e falha se ela faltar.
- SQL Server **Express** (`MSSQL_PID=Express`), `MSSQL_MEMORY_LIMIT_MB=2048`, porta 1433 **não** publicada fora da rede do Compose (C4).
- Bancos `Rastreamento_HML` e `Rastreamento_PRD` (C3); logins `rastru_hml` e `rastru_prd`, cada um dono só do próprio banco; `sa` só para criar banco e migrar (C17).
- Usuários da HML com os nomes do `db/seed.sql`: `admin`, `pcp`, `operador`, `movimentador`, `separador`, `qualidade`, `gestao`; hash gerado com o `BCryptPasswordHasher` de produção; senha lida de configuração/variável, **nunca de argumento** (C12, §6.3).
- `ForwardedHeaders` com `KnownNetworks` **só** a sub-rede fixa da rede do Compose; nunca listas limpas, nunca `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; desligado em Development e nos testes (C15, §5.2).
- `SigningKey` e connection string só por variável (`Jwt__SigningKey`, `ConnectionStrings__Rastreamento`); o `appsettings.json` commitado **não muda** (C16).
- `GET /api/saude`: anônima, 200 com banco alcançável, 503 sem, **sem detalhe do erro no corpo** (C14).
- Caddy: TLS automático, `encode zstd gzip`, sem limite de corpo (C5).
- Migrações: `db/migracoes/NNNN-descricao.sql`, em ordem de nome, anotadas em `dbo.MigracaoAplicada (Nome NVARCHAR(200) PK, AplicadaEm DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME())`; backup antes da primeira pendente, guardando os **3 últimos** (C9, §6.1). Banco novo nasce do `02` e anota **todas** as migrações como aplicadas (C11).
- Mudança de schema = `specs/02-modelo-de-dados.sql` **e** um script em `db/migracoes/` (C10); os `db/alter-*.sql` ficam como estão.
- Scripts SQL de shell com `-b -f 65001`; backend `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1`; front `npm test -- --run --maxWorkers=2` e `npm run build`.
- Nomes de domínio em português; comentário e prosa citam pelo nome, nunca `arquivo:NN` (seção «Convenção de citação» do `CLAUDE.md`). Saída dos comandos em português e **nunca** imprime senha, hash ou connection string.
- Teste novo que fala com o SQL Server cria e apaga o **próprio** banco (e o próprio login) descartável — nunca escreve nos usuários do banco `Rastreamento` compartilhado.

## Review Focus

1. **Asset que não existe mais** (`/assets/index-velho.js` de um cliente aberto antes do deploy) responde **404**, não o `index.html` com 200 — senão o navegador recebe HTML como script e a tela fica branca sem erro útil. `/API/setores` continua 404 no modo SPA. (Task 1: `Spa_asset_inexistente_responde_404`, `Spa_prefixo_em_maiuscula_continua_404`.)
2. **Banco novo interrompido no meio** (segredo errado, script da massa quebrado, container morto) nunca deixa um banco com o **nome final** e senhas do repositório; a execução seguinte recomeça limpa. (Task 6: `Criacao_interrompida_nao_deixa_banco_com_o_nome_final`.)
3. **Segredo de um dos sete usuários ausente ou vazio** aborta **antes** de criar qualquer coisa — nada de banco "quase pronto". (Task 6: `Senha_faltando_aborta_antes_de_criar`.)
4. **Migração que falha no meio** não é anotada, desfaz o que ela mesma fez, deixa o backup feito e sai ≠ 0 — o deploy para antes de trocar o container. (Task 4: `Script_que_falha_nao_anota_e_desfaz`, `Backup_vem_antes_da_primeira_pendente`.)
5. **`X-Forwarded-For` vindo de fora da sub-rede** (alguém mandando o cabeçalho direto na porta da API) é ignorado: não dá ao atacante uma partição nova do rate limit por requisição. (Task 3: `Cabecalho_de_fora_da_rede_e_ignorado`.)

## Antes de começar (o controlador)

- Base: `main` em `511cccc`; a base registrada da Task 1 é o commit deste plano. Branch do código e do ledger: `claude/rastru-cicd-parte-2-plan-mz8oqt`.
- Backend e banco na nuvem: `bash scripts/backend-na-nuvem` (CLAUDE.md, «Sessão na nuvem»).
- **Docker build na nuvem:** processos dentro de container não alcançam o proxy nem confiam na CA dele (`/root/.ccr/README.md`, seção «docker build / docker run»). Por isso o Dockerfile da Task 8 aceita uma CA extra **opcional** por *build secret*, só nos estágios de build; no CI ela não é passada e não muda nada. Na nuvem: `docker build --network host --build-arg HTTPS_PROXY --build-arg HTTP_PROXY --build-arg NO_PROXY --secret id=ca_extra,src=/root/.ccr/ca-bundle.crt …`.
- Esta parte **não tem conferência do usuário no navegador** (a §8 a põe na parte 3): a review de branch inteira não espera por ela.

---

### Task 1: O SPA servido pela API (§5.1)

**Files:**
- Modify: `src/Rastreamento.Api/Program.cs` (o bloco da guarda do `/api` e o comentário dela)
- Create: `src/Rastreamento.Api/Spa/SpaDaApi.cs`
- Modify: `tests/Rastreamento.Api.Tests/AuthEndpointsTests.PrefixoDeApi.cs`
- Modify: `CLAUDE.md` (seção «Prefixo `/api`», incluindo a «Restrição de ordem de pipeline»), `specs/03-arquitetura-tecnica.md` (o bullet do Frontend em «Hospedagem»: o caminho escolhido é a API servindo o build)

**Interfaces:**
- Produces: `public static class SpaDaApi` com `public static bool Disponivel(IWebHostEnvironment ambiente)` (verdadeiro quando `ambiente.WebRootFileProvider.GetFileInfo("index.html").Exists`) e `public static void UsarSpa(this WebApplication app)`, que registra o ramo do SPA **antes** da guarda. A Task 8 põe o build do Vite em `/app/wwwroot`.

Comportamento no modo SPA (o modo só-API, sem `wwwroot/index.html`, fica exatamente como hoje):

| Requisição | Resposta |
|---|---|
| `Path` começa com `/api` (ordinal) | pipeline da API, como hoje |
| `Path` começa com `/api` só ignorando caixa (`/API/x`) | 404 |
| `GET`/`HEAD` de arquivo que existe em `wwwroot` | o arquivo; sob `/assets/`: `Cache-Control: public, max-age=31536000, immutable`; fora dele: `no-cache` |
| `GET`/`HEAD` de caminho cujo último segmento **não tem extensão** e não é arquivo | `index.html`, `Cache-Control: no-cache` |
| `GET`/`HEAD` de caminho **com extensão** que não existe | 404 (refino da §5.1, ver Review Focus 1) |
| outro método fora de `/api` | 404 |

Implementação: `app.MapWhen(c => !c.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal), ramo => …)` com `UseStaticFiles` (cabeçalhos no `OnPrepareResponse`) e um terminal que serve o `index.html` pelo `WebRootFileProvider` nos casos da tabela. Fica antes da guarda; a guarda continua lá (no modo SPA ela só é alcançada por quem já começa com `/api`). Reescrever o comentário da guarda: a «Restrição de ordem de pipeline» deixa de ser hipótese e passa a dizer onde o SPA entra e por quê.

- [ ] **Step 1: Escrever os testes do modo SPA**, em `AuthEndpointsTests.PrefixoDeApi.cs`, com uma fábrica `FabricaComSpa()` que faz `UseWebRoot(pastaTemporaria)` com `index.html` (contendo `<div id="root"></div><!--spa-de-teste-->`), `assets/index-abc123.js` e `favicon.svg`:
  - `Spa_GET_da_raiz_devolve_o_index_sem_cache` — `/` → 200, `text/html`, corpo com `spa-de-teste`, `Cache-Control` contém `no-cache`.
  - `Spa_rota_do_spa_devolve_o_index` — `[Theory]` `/pedidos`, `/pedidos/5`, `/setores` → 200 com `spa-de-teste` (o F5).
  - `Spa_HEAD_da_rota_responde_200`.
  - `Spa_asset_com_hash_tem_cache_longo` — `/assets/index-abc123.js` → 200, `Cache-Control` contém `max-age=31536000` e `immutable`.
  - `Spa_asset_inexistente_responde_404` — `/assets/index-velho.js` → 404.
  - `Spa_prefixo_em_maiuscula_continua_404` — `/API/setores` → 404; e `/api/setores` → 401 (a API ainda responde).
  - `Spa_POST_fora_do_api_responde_404` — `[Theory]` `POST /pedidos` e `POST /auth/login` → 404.
  - Modo só-API: `Sem_wwwroot_a_raiz_responde_404` — fábrica padrão, `GET /` → 404 (os testes antigos do arquivo continuam valendo como estão).

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~AuthEndpointsTests"`. Expected: os `Spa_*` vermelhos (hoje tudo fora de `/api` é 404); `Sem_wwwroot_a_raiz_responde_404` verde.

- [ ] **Step 3: Implementar `SpaDaApi`** e chamar `if (SpaDaApi.Disponivel(app.Environment)) app.UsarSpa();` logo antes da guarda. Se a `UseWebRoot` do `WithWebHostBuilder` não chegar ao `app.Environment` com o hosting mínimo, o teste mostra; registrar no relatório como foi resolvido.

- [ ] **Step 4: Rodar e ver passar** — o mesmo filtro. Expected: todos verdes.

- [ ] **Step 5: Prosa** — `CLAUDE.md` «Prefixo `/api`» (os dois modos, a tabela em uma frase por linha, o porquê do 404 em caminho com extensão; a «Restrição de ordem de pipeline» passa a "cumprida assim"), e o bullet do Frontend em `specs/03` «Hospedagem».

- [ ] **Step 6: Suíte da Api e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test tests/Rastreamento.Api.Tests
git add src/Rastreamento.Api tests/Rastreamento.Api.Tests CLAUDE.md specs/03-arquitetura-tecnica.md
git commit -m "feat(api): servir o SPA do wwwroot antes da guarda do /api"
```

---

### Task 2: A rota de saúde (C14)

**Files:**
- Create: `src/Rastreamento.Api/Controllers/SaudeController.cs`
- Create: `tests/Rastreamento.Api.Tests/SaudeEndpointsTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs` (entrada em `IsentosDeAutenticacaoPorDesenho`)
- Modify: `specs/05-api-endpoints.md` (a rota nova)

**Interfaces:**
- Produces: `GET /api/saude` → 200 `{"status":"ok"}` quando `RastreamentoDbContext.Database.CanConnectAsync()` é verdadeiro; 503 `{"status":"indisponivel"}` quando não é ou lança. `[AllowAnonymous]`, sem `[EnableRateLimiting]`. A Task 9 a usa.

- [ ] **Step 1: Testes** em `SaudeEndpointsTests`:
  - `Saude_com_banco_responde_200_sem_token` — 200, corpo `status` = `ok`.
  - `Saude_sem_banco_responde_503_sem_detalhe` — fábrica com `ConnectionStrings:Rastreamento` = `Server=localhost,1;Database=Rastreamento;User Id=sa;Password=x;TrustServerCertificate=True;Connect Timeout=2` → 503, corpo `status` = `indisponivel`, e o corpo **não** contém `localhost`, `SqlException` nem `Exception`.
  - Em `PerfisDeEscritaDeclaradosTests`, a isenção `["GET saude"]` com o motivo ("o orquestrador e a verificação do deploy chamam sem sessão; não lê nem devolve dado") — o teste de autenticação fica vermelho sem ela.

- [ ] **Step 2: Ver falhar** — `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~SaudeEndpointsTests|FullyQualifiedName~PerfisDeEscritaDeclarados"`. Expected: 404 nos dois de saúde.

- [ ] **Step 3: Implementar o `SaudeController`** (rota `saude`, uma ação `GET`). A exceção do `CanConnectAsync` vira 503 e é logada com `ILogger` (o log pode ter o detalhe; o corpo não).

- [ ] **Step 4: Ver passar** — mesmo filtro. Expected: verdes.

- [ ] **Step 5: `specs/05`** — a rota, anônima, os dois status, o porquê de não ter detalhe.

- [ ] **Step 6: Commit**

```bash
git add src/Rastreamento.Api tests/Rastreamento.Api.Tests specs/05-api-endpoints.md
git commit -m "feat(api): GET /api/saude, anonima, confere o banco"
```

---

### Task 3: `ForwardedHeaders` restrito à rede do proxy (C15)

**Files:**
- Create: `src/Rastreamento.Api/Configuration/ProxyReversoOptions.cs`, `src/Rastreamento.Api/Configuration/ProxyReversoOptionsValidator.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (bind + `ValidateOnStart`; `UseForwardedHeaders` como **primeiro** middleware, antes do SPA e da guarda, só quando `Rede` está preenchida; o comentário do rate limit que manda "configurar ForwardedHeaders")
- Create: `tests/Rastreamento.Api.Tests/ProxyReversoTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs`
- Modify: `CLAUDE.md` («Rate limit atrás de proxy reverso»), `specs/03-arquitetura-tecnica.md` («Pontos em aberto», item 2: resolvido na aplicação; o Compose da Task 9 fornece a rede)

**Interfaces:**
- Produces: seção de configuração `ProxyReverso`, chave `Rede` (CIDR, ex.: `172.30.0.0/24`; env `ProxyReverso__Rede`). Vazia ou ausente = middleware **não registrado**. Preenchida: `ForwardedHeaders = XForwardedFor | XForwardedProto`, a rede em `KnownIPNetworks` (ou `KnownNetworks`, se o SDK não marcar obsoleto — `-warnaserror` decide), `ForwardLimit = 1`. A Task 9 a preenche com a sub-rede do Compose.

- [ ] **Step 1: Testes** em `ProxyReversoTests`, com um `IStartupFilter` que fixa `Connection.RemoteIpAddress` (no molde do `PathBaseDeSubAplicacao`) e `RateLimit:PermitLimit=1`; usuário inexistente no login, como em `RateLimitTests`:
  - `Clientes_atras_do_proxy_tem_particoes_proprias` — remoto `172.30.0.5`, `Rede=172.30.0.0/24`: login com `X-Forwarded-For: 203.0.113.7` → 401; com `203.0.113.8` → 401 (não 429); de novo com `203.0.113.7` → 429.
  - `Cabecalho_de_fora_da_rede_e_ignorado` — remoto `198.51.100.9`, mesma rede: `X-Forwarded-For` `203.0.113.7` → 401, `203.0.113.8` → 429.
  - `Sem_rede_configurada_o_cabecalho_e_ignorado` — remoto `172.30.0.5`, sem `Rede`: o mesmo par → 401, 429.
  - Em `ConfiguracaoDeStartupTests`: `Aplicacao_nao_sobe_com_rede_do_proxy_invalida` — `[Theory]` `"nao-e-cidr"`, `"172.30.0.0/99"`, `"0.0.0.0/0"` → `OptionsValidationException` citando `ProxyReverso`. (`0.0.0.0/0` e `::/0` confiariam em todo mundo: é a "lista limpa" por outro caminho.)

- [ ] **Step 2: Ver falhar** — `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ProxyReverso|FullyQualifiedName~ConfiguracaoDeStartup"`. Expected: `Clientes_atras_do_proxy_…` (429 no segundo) e os de validação vermelhos.

- [ ] **Step 3: Implementar** `ProxyReversoOptions { string? Rede }`, o validador (parse com `System.Net.IPNetwork.TryParse`; recusa prefixo 0) e o registro em `Program.cs`.

- [ ] **Step 4: Ver passar** — mesmo filtro. Expected: verdes; `RateLimitTests` continua verde.

- [ ] **Step 5: Prosa** — `CLAUDE.md` «Rate limit atrás de proxy reverso» e `specs/03` item 2 (o que ficou feito, a sub-rede vem do Compose, e o que o Caddy manda: `X-Forwarded-For` e `X-Forwarded-Proto`).

- [ ] **Step 6: Commit**

```bash
git add src/Rastreamento.Api tests/Rastreamento.Api.Tests CLAUDE.md specs/03-arquitetura-tecnica.md
git commit -m "feat(api): ForwardedHeaders so da sub-rede do proxy, por configuracao"
```

---

### Task 4: Scripts com `GO`, `dbo.MigracaoAplicada`, migrador e backup (C9, C10, §6.1)

**Files:**
- Create: `src/Rastreamento.Infrastructure/Banco/LotesDeSql.cs`, `ExecutorDeSql.cs`, `ScriptsDoBanco.cs`, `BackupDoBanco.cs`, `MigradorDeBanco.cs`
- Create: `db/migracoes/0001-migracao-aplicada.sql`
- Modify: `specs/02-modelo-de-dados.sql` (a tabela, no fim)
- Create: `tests/Rastreamento.Infrastructure.Tests/Banco/LotesDeSqlTests.cs`, `BancoDescartavel.cs`, `MigradorDeBancoTests.cs`, `BackupDoBancoTests.cs`
- Modify: `CLAUDE.md` («Banco de dados — regra importante» e «Comandos»: a convenção C10 e como aplicar a 0001 num banco de dev, no formato dos `alter-*.sql`)

**Interfaces:**
- Produces (namespace `Rastreamento.Infrastructure.Banco`):
  - `public static class LotesDeSql { public static IReadOnlyList<string> Separar(string script); }` — separa nas linhas que são só `GO` (sem diferenciar caixa, espaços em volta); lote vazio some; `GO <n>` lança `FormatException` ("GO com contagem não é suportado").
  - `public static class ExecutorDeSql { public static Task ExecutarAsync(SqlConnection conexao, SqlTransaction? transacao, string script, CancellationToken ct); }` — lote a lote, `CommandTimeout` de 600 s. Lê o arquivo como UTF-8 quem o chama (é o `-f 65001` deste caminho).
  - `public sealed record ScriptsDoBanco(string Raiz)` — `Modelo` = `{Raiz}/specs/02-modelo-de-dados.sql`, `Seed` = `{Raiz}/db/seed.sql`, `SeedDemo`, `MassaVitrine`, `PastaDeMigracoes` = `{Raiz}/db/migracoes`; `IReadOnlyList<string> Migracoes()` — nomes de arquivo em ordem ordinal; arquivo `.sql` fora do padrão `^\d{4}-[a-z0-9-]+\.sql$` lança. A raiz é o repositório nos testes e `/app/sql` na imagem (Task 8 copia com o mesmo layout).
  - `public interface IBackupDoBanco { Task<string> FazerAsync(string conexaoAdministrativa, string banco, CancellationToken ct); }` e `public sealed class BackupDoBanco : IBackupDoBanco` — `BACKUP DATABASE … TO DISK … WITH INIT, FORMAT` (sem `COMPRESSION`: o Express não tem) em `SERVERPROPERTY('InstanceDefaultBackupPath')`, num de três arquivos `{banco}-1.bak`…`-3.bak`: o que nunca foi usado ou, se todos já foram, o de backup mais antigo segundo `msdb.dbo.backupset`/`backupmediafamily`. Devolve o caminho.
  - `public sealed record ResultadoDaMigracao(IReadOnlyList<string> Aplicadas, string? Backup);`
  - `public sealed class MigradorDeBanco(IBackupDoBanco backup) { public Task<ResultadoDaMigracao> MigrarAsync(string conexaoAdministrativa, string banco, ScriptsDoBanco scripts, CancellationToken ct); }` — aplicadas = linhas de `dbo.MigracaoAplicada` (tabela ausente = nenhuma); sem pendente, não faz backup e devolve lista vazia; com pendente, **um** backup antes da primeira, depois cada pendente numa transação própria com o `INSERT` do nome; a primeira que falhar desfaz a própria transação e propaga a exceção (as seguintes não rodam). Migração que não roda em transação não é suportada, e a falha é alta.
- `db/migracoes/0001-migracao-aplicada.sql`: `IF OBJECT_ID('dbo.MigracaoAplicada') IS NULL CREATE TABLE …` igual à do `02`, sem `GO` (roda na transação do migrador; num banco sem a tabela é ela que a cria, e o migrador anota a si mesma).

- [ ] **Step 1: `LotesDeSqlTests`** (puros, sem banco): `Separa_nas_linhas_que_sao_so_GO` (com `go` minúsculo e espaços), `Nao_separa_GO_dentro_de_palavra` (`GOTO`, `CATEGORY`, `-- GO aqui`), `Lote_vazio_some`, `GO_com_contagem_lanca`, `O_modelo_inteiro_separa_no_numero_de_lotes_medido` (lê `specs/02-modelo-de-dados.sql` do repositório; hoje são 9 `GO` → 10 lotes, e a tabela nova entra no último lote, sem `GO` novo — o número do teste é medido depois do Step 6 e o comentário diz de onde veio).

- [ ] **Step 2: Ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~LotesDeSql"`. Expected: não compila (`LotesDeSql` não existe).

- [ ] **Step 3: Implementar `LotesDeSql`, `ExecutorDeSql`, `ScriptsDoBanco`** e ver os testes do Step 1 passarem.

- [ ] **Step 4: `BancoDescartavel`** (apoio de teste, `IAsyncDisposable`, com a própria constante de conexão `sa` em `localhost,1433`, catálogo `master` — sem depender do `TesteComBanco`, porque a Task 5 o liga também na `Api.Tests`): cria `RastruTeste_{guid:N}` vazio, expõe `Nome` e `ConexaoAdministrativa`, e no `DisposeAsync` faz `ALTER DATABASE … SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE` se existir (também o `_criando` da Task 6). Testes de `MigradorDeBancoTests` com scripts de mentira numa pasta temporária `db/migracoes/` e um `IBackupDoBanco` falso que só anota a chamada:
  - `Aplica_em_ordem_de_nome_e_anota` — `0002-b`, `0001-migracao-aplicada` (copiada do repositório), `0010-c` → aplicadas na ordem 0001, 0002, 0010; tabela com as três.
  - `Pula_as_ja_aplicadas` — segunda execução: `Aplicadas` vazia, backup **não** chamado.
  - `Backup_vem_antes_da_primeira_pendente` — o falso registra a ordem: backup, depois a 0001.
  - `Script_que_falha_nao_anota_e_desfaz` — `0002` cria `dbo.Parcial` e depois faz `SELECT 1/0`; `0003` cria `dbo.Depois`: exceção propagada; `dbo.Parcial` e `dbo.Depois` não existem; a tabela só tem a 0001.
  - `Arquivo_fora_do_padrao_lanca` — `migracao.sql` na pasta.
  - `O_modelo_tem_a_tabela_de_migracoes` — banco descartável com o `02` aplicado pelo `ExecutorDeSql`: `dbo.MigracaoAplicada` existe com `Nome` PK `NVARCHAR(200)` e `AplicadaEm` com default.

- [ ] **Step 5: `BackupDoBancoTests`** (classe única, banco de nome **fixo** `RastruTesteDoBackup`, recriado a cada teste, para os `.bak` não se acumularem entre execuções): `Faz_o_backup_e_devolve_o_caminho` (o arquivo aparece em `msdb` com o tamanho > 0), `Quarto_backup_reusa_o_slot_mais_antigo` (quatro backups → três caminhos distintos, e o quarto é igual ao primeiro).

- [ ] **Step 6: Ver falhar e implementar** `BackupDoBanco`, `MigradorDeBanco`, a 0001 e a tabela no `02`. Ver passar: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~Banco"`.

- [ ] **Step 7: Prosa** — `CLAUDE.md`: a regra C10 em «Banco de dados — regra importante» (passo novo entre o 1 e o 2) e, em «Comandos», o bloco da 0001 para banco de dev existente (mesmo formato dos `alter-*.sql`). O banco do `scripts/banco-de-teste` nasce do `02` e já tem a tabela, vazia — dizer isso.

- [ ] **Step 8: Suítes e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx -m:1
git add src/Rastreamento.Infrastructure tests/Rastreamento.Infrastructure.Tests db/migracoes specs/02-modelo-de-dados.sql CLAUDE.md
git commit -m "feat(banco): migracoes versionadas com dbo.MigracaoAplicada e backup antes de migrar"
```

---

### Task 5: Senhas fora do repositório e a linha de comando (§6.3, C13)

**Files:**
- Create: `src/Rastreamento.Infrastructure/Banco/SenhasDoSeed.cs`, `SenhasDosUsuarios.cs`
- Create: `src/Rastreamento.Api/Comandos/LinhaDeComando.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (o desvio para a linha de comando antes do `CreateBuilder`; `return 0` depois do `app.Run()`)
- Create: `tests/Rastreamento.Infrastructure.Tests/Banco/SenhasDosUsuariosTests.cs`, `tests/Rastreamento.Api.Tests/LinhaDeComandoTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/Rastreamento.Api.Tests.csproj` (o `BancoDescartavel` da Task 4 entra por `<Compile Include="..\Rastreamento.Infrastructure.Tests\Banco\BancoDescartavel.cs" Link="Apoio\BancoDescartavel.cs" />`: um arquivo, dois projetos)

**Interfaces:**
- Consumes: `BancoDescartavel` e `ExecutorDeSql` (Task 4), `IPasswordHasher`/`BCryptPasswordHasher`.
- Produces:
  - `public static class SenhasDoSeed { public static IReadOnlyList<(string Usuario, string Senha)> Todas { get; } }` — os sete pares públicos do `db/seed.sql`: `admin`/`Admin@123`, `pcp`/`Pcp@123`, `operador`/`Operador@123`, `movimentador`/`Movimentador@123`, `separador`/`Separador@123`, `qualidade`/`Qualidade@123`, `gestao`/`Gestao@123`.
  - `public sealed class SenhasDosUsuarios(IPasswordHasher hasher)`:
    - `Task DefinirAsync(SqlConnection conexao, SqlTransaction? transacao, string usuario, string senha, CancellationToken ct)` — recusa senha vazia ou em branco e a senha do seed **daquele** usuário (`ArgumentException`); grava `SenhaHash`, zera `FalhasConsecutivas` e `BloqueadoAte` (dá também o desbloqueio que o `CLAUDE.md` diz faltar); usuário inexistente → `InvalidOperationException`.
    - `Task<IReadOnlyList<string>> ComSenhaDoSeedAsync(SqlConnection conexao, CancellationToken ct)` — os nomes dos sete que existem no banco com hash que verifica a senha do seed; não chama o login.
  - `public static class LinhaDeComando`:
    - `public static bool EhComando(string[] args)` — `args[0]` em `banco`, `definir-senha`, `conferir-senhas`.
    - `public static Task<int> ExecutarAsync(string[] args)` — monta a configuração (`appsettings.json` da pasta do binário, `appsettings.{DOTNET_ENVIRONMENT|ASPNETCORE_ENVIRONMENT}.json`, variáveis de ambiente) e chama a sobrecarga abaixo.
    - `public static Task<int> ExecutarAsync(string[] args, IConfiguration configuracao, TextWriter saida, TextWriter erro)` — códigos: 0 sucesso, 1 falha, 2 uso errado.
    - `definir-senha <usuario>`: senha em `Senhas:<usuario>` (env `Senhas__<usuario>`), conexão `ConnectionStrings:Rastreamento`.
    - `conferir-senhas`: conexão `ConnectionStrings:Rastreamento`; 0 com "Nenhum dos sete usuários do seed tem a senha do repositório."; 1 listando os nomes.
    - `banco preparar`: entra na Task 6; aqui devolve 2 com "subcomando desconhecido" para qualquer `banco …`.

- [ ] **Step 1: `SenhasDosUsuariosTests`** (banco descartável com `02` + `db/seed.sql`):
  - `A_lista_do_seed_bate_com_o_seed` — no banco recém-semeado, `ComSenhaDoSeedAsync` devolve os **sete** (a lista não apodrece quando o seed muda).
  - `Definir_troca_o_hash_e_tira_o_usuario_da_lista` — define `admin` com `Outra-Senha-forte-1`: `Verificar` da nova é verdadeiro, `admin` sai da lista, os outros seis ficam.
  - `Definir_zera_a_trava` — com `FalhasConsecutivas = 5` e `BloqueadoAte` no futuro, depois de definir: 0 e nulo.
  - `Definir_recusa_a_senha_do_seed_e_a_vazia` — `[Theory]` `Admin@123`, `""`, `"   "`.
  - `Definir_usuario_inexistente_lanca`.

- [ ] **Step 2: `LinhaDeComandoTests`** (sem subir a API; configuração em memória, `StringWriter`):
  - `Comando_desconhecido_sai_2` — `["banco", "xyz"]` e `["definir-senha"]` (sem usuário).
  - `Conferir_no_banco_de_teste_acusa_os_sete` — `ConnectionStrings:Rastreamento` do banco `Rastreamento` compartilhado (que tem o seed): sai 1 e a saída cita os sete nomes. Só lê.
  - `Definir_sem_a_senha_configurada_sai_1` — banco descartável; mensagem cita `Senhas__admin`.
  - `Definir_e_depois_conferir` — banco descartável com o seed: define os sete por `Senhas:*` e `conferir-senhas` sai 0.
  - `Saida_nunca_tem_a_senha_nem_a_conexao` — nos casos acima, `saida + erro` não contém a senha usada, `Password=` nem `$2a$`.

- [ ] **Step 3: Ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~SenhasDosUsuarios"` e `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~LinhaDeComando"`. Expected: não compila.

- [ ] **Step 4: Implementar** `SenhasDoSeed`, `SenhasDosUsuarios` (SQL parametrizado, sem EF), `LinhaDeComando` e o desvio no topo de `Program.cs`: `if (LinhaDeComando.EhComando(args)) return await LinhaDeComando.ExecutarAsync(args);`.

- [ ] **Step 5: Ver passar** — os dois filtros; depois `dotnet test tests/Rastreamento.Api.Tests` inteira (o `WebApplicationFactory<Program>` tem de continuar subindo com o `Program` que agora devolve `int`).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(api): comandos definir-senha e conferir-senhas, com o hasher de producao"
```

---

### Task 6: Banco novo atômico, login do ambiente e `banco preparar` (C11, C17, §6.2)

**Files:**
- Create: `src/Rastreamento.Infrastructure/Banco/OpcoesDoBanco.cs`, `LoginDoAmbiente.cs`, `CriadorDeBanco.cs`, `PreparadorDeBanco.cs`
- Modify: `src/Rastreamento.Api/Comandos/LinhaDeComando.cs` (`banco preparar`)
- Create: `tests/Rastreamento.Infrastructure.Tests/Banco/CriadorDeBancoTests.cs`, `LoginDoAmbienteTests.cs`, `PreparadorDeBancoTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/LinhaDeComandoTests.cs`

**Interfaces:**
- Consumes: Task 4 (`ExecutorDeSql`, `ScriptsDoBanco`, `MigradorDeBanco`, `IBackupDoBanco`), Task 5 (`SenhasDosUsuarios`, `SenhasDoSeed`).
- Produces:
  - `public sealed class OpcoesDoBanco` (seção `Banco`): `string ConexaoAdministrativa` (login `sa`, catálogo `master`), `string Nome` (ex.: `Rastreamento_HML`), `string Login` (ex.: `rastru_hml`), `string SenhaDoLogin`, `bool Vitrine`, `string RaizDosScripts` (padrão `/app/sql`). Senhas dos usuários na seção `Senhas` (chave = nome do usuário).
  - `public sealed class LoginDoAmbiente { Task GarantirAsync(string conexaoAdministrativa, string banco, string login, string senha, CancellationToken ct); }` — cria o login se falta, senão troca a senha para a atual; `DEFAULT_DATABASE = master`; no banco, `CREATE USER … FOR LOGIN` se falta e `db_owner`. Nome do login validado por `^[a-z_][a-z0-9_]*$`; o T-SQL dinâmico monta nome e senha com `QUOTENAME(@x)` / `QUOTENAME(@senha, '''')` **dentro do T-SQL**, nunca por concatenação em C#.
  - `public sealed class CriadorDeBanco(SenhasDosUsuarios senhas) { Task CriarAsync(string conexaoAdministrativa, string nome, bool vitrine, IReadOnlyDictionary<string, string> senhasDosUsuarios, ScriptsDoBanco scripts, CancellationToken ct); }`. Ordem:
    1. Confere que há senha não vazia para cada um dos sete de `SenhasDoSeed` — falta → `InvalidOperationException` citando os nomes, **antes** de tocar no servidor.
    2. Apaga `{nome}_criando` se sobrou de uma execução interrompida.
    3. `CREATE DATABASE [{nome}_criando]` com os arquivos com o **nome final** (`{nome}.mdf`/`{nome}_log.ldf` em `SERVERPROPERTY('InstanceDefaultDataPath')`).
    4. Aplica o `02`.
    5. Numa transação só: o `db/seed.sql` inteiro **e** `DefinirAsync` para os sete — o hash do repositório nunca fica gravado fora dela.
    6. Se `vitrine`: `db/seed-demo.sql` e depois `db/massa-vitrine.sql` (a massa nasce na Task 7; até lá, `vitrine = true` com o arquivo ausente lança).
    7. Anota **todas** as `scripts.Migracoes()` em `dbo.MigracaoAplicada`.
    8. `SqlConnection.ClearAllPools()` e `ALTER DATABASE [{nome}_criando] MODIFY NAME = [{nome}]` — só aqui o banco ganha o nome final.
  - `public sealed record ResultadoDoPreparo(bool BancoNovo, IReadOnlyList<string> MigracoesAplicadas, string? Backup);`
  - `public sealed class PreparadorDeBanco(CriadorDeBanco criador, MigradorDeBanco migrador, LoginDoAmbiente login) { Task<ResultadoDoPreparo> PrepararAsync(OpcoesDoBanco opcoes, IReadOnlyDictionary<string, string> senhas, CancellationToken ct); }` — banco não existe → cria; existe → migra; nos dois casos, depois, garante o login.
  - `banco preparar` em `LinhaDeComando`: lê `Banco` e `Senhas`, chama o preparador, imprime "banco novo criado" ou "N migração(ões) aplicada(s); backup em …" ou "nada a migrar", e "login {login} garantido". Exceção → 1 com a mensagem (sem connection string).

- [ ] **Step 1: `CriadorDeBancoTests`** (nome descartável; `vitrine = false` exceto onde dito; senhas fortes geradas no teste):
  - `Banco_novo_tem_schema_perfis_e_os_sete_usuarios_com_a_senha_do_segredo` — os sete existem, cada um verifica a senha do segredo, `ComSenhaDoSeedAsync` vazia, sete perfis.
  - `Banco_novo_anota_todas_as_migracoes_como_aplicadas` — `dbo.MigracaoAplicada` = `scripts.Migracoes()`.
  - `Banco_novo_sem_vitrine_nao_tem_pedido`.
  - `Senha_faltando_aborta_antes_de_criar` — `[Theory]` sem `gestao` e com `gestao = ""`: lança citando `gestao`; nem `{nome}` nem `{nome}_criando` existem.
  - `Criacao_interrompida_nao_deixa_banco_com_o_nome_final` — raiz de scripts temporária igual à do repositório mas com uma `massa-vitrine.sql` que faz `THROW 50000, 'quebrada', 1`, `vitrine = true`: lança; `{nome}` não existe. Depois, com a raiz do repositório e `vitrine = false`, `CriarAsync` funciona (o `_criando` que sobrou é apagado).
- [ ] **Step 2: `LoginDoAmbienteTests`** (login `rastru_teste_{guid:N}`, apagado no `DisposeAsync`; dois bancos descartáveis): `O_login_alcanca_so_o_proprio_banco` (conecta com o login no banco A e lê `dbo.Usuario`; no banco B a conexão é recusada), `Garantir_de_novo_troca_a_senha` (a senha antiga deixa de conectar, a nova conecta), `Nome_de_login_invalido_lanca` (`[Theory]` `"x]; DROP"`, `"Rastru"`).
- [ ] **Step 3: `PreparadorDeBancoTests`**: `Banco_inexistente_e_criado_e_o_login_garantido`, `Banco_existente_e_migrado_sem_recriar` (com uma `0002-teste.sql` numa raiz temporária: aplicada, backup feito, dados de antes preservados — um Setor inserido entre as duas execuções continua lá).
- [ ] **Step 4: `LinhaDeComandoTests`**: `Banco_preparar_cria_e_conferir_passa` — configuração com `Banco:*` de um nome descartável, `Banco:RaizDosScripts` = raiz do repositório, `Senhas:*` e `ConnectionStrings:Rastreamento` com o login do ambiente: `banco preparar` sai 0; `conferir-senhas` (pela conexão do login) sai 0; segunda execução de `banco preparar` sai 0 com "nada a migrar".
- [ ] **Step 5: Ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~Banco"` e o filtro `LinhaDeComando` da Api. Expected: não compila.
- [ ] **Step 6: Implementar** as quatro classes e o subcomando.
- [ ] **Step 7: Ver passar** — os mesmos filtros.
- [ ] **Step 8: Suítes e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx -m:1
git add src tests
git commit -m "feat(banco): banco novo atomico, login do ambiente e o comando banco preparar"
```

---

### Task 7: A massa de vitrine (D11)

**Files:**
- Create: `db/massa-vitrine.sql`
- Create: `tests/Rastreamento.Api.Tests/MassaDaVitrineTests.cs`
- Modify: `CLAUDE.md` («Comandos»: o parágrafo da massa, no molde do `db/massa-carga-6a.sql`; e o "a suíte ignora o `seed-demo.sql`", que deixa de ser verdade ao pé da letra: o `CriadorDeBanco` e este teste o carregam num banco **próprio** — o determinismo continua, porque o banco compartilhado não ganha nada)

**Interfaces:**
- Consumes: `CriadorDeBanco` (Task 6) com `vitrine = true`; `TokenDeTeste`.
- Produces: `db/massa-vitrine.sql` — roda depois do `seed-demo.sql`, usa os Setores e Componentes dele por nome/código, autor `admin` (`THROW` se faltar), datas relativas ao `SYSUTCDATETIME()` da carga, idempotente por número de Pedido (o Pedido que existe não é recriado, e o livro só é gerado para os criados na execução), tudo numa transação, `SET XACT_ABORT ON`, `N'…'` em todo texto, carregável com `-b -f 65001`.

Os Pedidos (números e clientes fictícios, genéricos; prazos em dias a partir da carga):

| Número | Cliente | Estágio que a tela mostra | Exigência |
|---|---|---|---|
| `PED-1041` | Mineração Serra Alta | atrasado | `EmProducao`, entrega −6 d, Peças entre Corte a Laser e Dobra |
| `PED-1042` | Agroindustrial Vale Verde | pausado | `EmProducao`, `PedidoPausa` aberta há 2 d |
| `PED-1043` | Cimentos Planalto | meio da produção | Peças divididas entre ≥ 3 Setores e ≥ 1 lote `AguardandoColeta`, idades variadas (2 a 9 d) |
| `PED-1044` | Portos do Sul Logística | Kit na Solda | Agrupamento `Kit`; Solda com `UtilizaKit = 1` (a massa marca, se o `seed-demo` não marcar); um conjunto montado e filhos de um conjunto incompleto em `AguardandoMontagem` |
| `PED-1045` | Usina Santa Clara | aguardando expedição | `AguardandoExpedicao` (C11 da 5A: todo o não expedido no local de expedição); Peça com relatório, aferições `Conforme` e uma `NaoConforme`; uma remessa parcial já feita |
| `PED-1046` | Fertilizantes Cerrado | perda e reposição | uma baixa (`dbo.Perda` + linhas `Perda`) por `MortaEmProcesso`; o Retrabalho dela aberto (`Tipo = 'Retrabalho'`, `MotivoRetrabalho = 'Perda'`, número no formato que `NumeroDoRetrabalho` sugere) e ligado como a 5B-2 liga |
| `PED-1047` | Metalúrgica Horizonte | recém-aberto | `Aberto`, nada iniciado, entrega +25 d |
| `PED-1048` | Papel e Celulose Ribeira | concluído | `Concluido`, tudo expedido em remessas |

Só movimentos que a API faria (os CHECKs do livro e as regras 9 e 22–40 de `specs/01`). Molde por estágio: `db/massa-carga-6a.sql` (montagem e Roteiro), `db/massa-conferencia-fase-3b.sql` (Kit), `…-fase-5a.sql` (aferição e remessa), `…-fase-5b1.sql` (baixa), `…-fase-5b2.sql` (reposição). As massas de conferência são **molde**, não fonte: nada de `CONF-*`, de entrega em 2099 nem de depender delas.

- [ ] **Step 1: `MassaDaVitrineTests`** (uma fixture de classe cria o banco descartável com o `CriadorDeBanco`, `vitrine = true`, e uma `WebApplicationFactory` apontada para ele com `sa`; o `DisposeAsync` apaga o banco):
  - `A_vitrine_tem_os_oito_pedidos` — os números da tabela.
  - `Todo_no_fecha_a_conservacao` — SQL sobre o livro: para cada nó e cada posição (posição, Setor, ordem), o saldo (entradas − saídas, e `AIniciar` começando na `Quantidade`) nunca é negativo — a regra 9 com o perdido como termo.
  - `Cada_estagio_existe` — `[Theory]` por número: o atrasado tem `DataEntrega < hoje` e status aberto; o pausado tem pausa aberta; o 1043 tem ≥ 3 Setores com saldo e saldo em `AguardandoColeta`; o 1044 é `Kit` e tem saldo em `AguardandoMontagem` na Solda com `UtilizaKit = 1`; o 1045 está `AguardandoExpedicao` com `Conforme` e `NaoConforme`; o 1046 tem `dbo.Perda` e um Pedido `Retrabalho` com `PedidoOrigemId` = ele; o 1047 está `Aberto` sem linha no livro; o 1048 está `Concluido`.
  - `A_api_le_a_vitrine` — com token de `Administrador`: `GET /api/panorama` 200 e, para cada Pedido, `GET /api/pedidos/{id}` e `GET /api/pedidos/{id}/andamento` 200.
  - `Carregar_de_novo_nao_duplica` — reexecuta `db/massa-vitrine.sql` pelo `ExecutorDeSql`: contagens de Pedido, nó e livro iguais.
- [ ] **Step 2: Ver falhar** — `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~MassaDaVitrine"`. Expected: o `CriadorDeBanco` lança (arquivo ausente).
- [ ] **Step 3: Escrever `db/massa-vitrine.sql`**, um Pedido por vez, rodando o filtro a cada um. Cabeçalho no molde do `massa-carga-6a.sql` (propósito, forma, limites, como carregar).
- [ ] **Step 4: Ver passar** — mesmo filtro; depois carregar à mão no banco de dev (`docker compose cp` + `sqlcmd -b -f 65001`, depois do `seed-demo.sql`) e medir `LEN(Cliente)` de `PED-1041` (`Mineração Serra Alta` = **20**; lido como Latin-1 daria 22).
- [ ] **Step 5: Prosa e commit**

```bash
dotnet test tests/Rastreamento.Api.Tests
git add db/massa-vitrine.sql tests/Rastreamento.Api.Tests CLAUDE.md
git commit -m "feat(banco): massa de vitrine da HML, com oito Pedidos nos estagios das telas"
```

---

### Task 8: O Dockerfile (C7, C8, §5.3)

**Files:**
- Create: `Dockerfile`, `.dockerignore`

**Interfaces:**
- Consumes: Task 1 (`wwwroot` liga o modo SPA), Task 6 (`Banco:RaizDosScripts` padrão `/app/sql`).
- Produces: imagem que, sem argumentos, sobe a API em `:8080` (`ASPNETCORE_HTTP_PORTS=8080`) e, com argumentos, roda a `LinhaDeComando` (`ENTRYPOINT ["dotnet", "Rastreamento.Api.dll"]`, então `docker run IMAGEM banco preparar`). Layout: `/app` (publish), `/app/wwwroot` (o `dist` do Vite), `/app/sql/specs/02-modelo-de-dados.sql`, `/app/sql/db/{seed.sql,seed-demo.sql,massa-vitrine.sql}`, `/app/sql/db/migracoes/`. `USER $APP_UID`.

Estágios: `front` (`node:22-bookworm-slim`: `npm ci` com só `package.json`/`package-lock.json` copiados antes, depois `npm run build`); `api` (`mcr.microsoft.com/dotnet/sdk:10.0`: copia os quatro `.csproj` de `src/`, `dotnet restore src/Rastreamento.Api/Rastreamento.Api.csproj`, copia `src/`, `dotnet publish -c Release -o /app --no-restore`); final `mcr.microsoft.com/dotnet/aspnet:10.0`. CA extra opcional só nos dois estágios de build: `RUN --mount=type=secret,id=ca_extra,required=false …` exporta `NODE_EXTRA_CA_CERTS`/`SSL_CERT_FILE` para aquele `RUN` quando o secret existe; o estágio final não a vê. Comentário no topo diz para que serve e que o CI não a passa.

- [ ] **Step 1: Escrever** `Dockerfile` e `.dockerignore` (a lista das Global Constraints, mais `.git`, `web/dist`, `TestResults`, `deploy/local`).
- [ ] **Step 2: Build** — `docker build -t rastru:local .` (na nuvem, com as opções de «Antes de começar»). Expected: termina; `docker image inspect rastru:local --format '{{.Config.User}}'` não é vazio nem `root`/`0`.
- [ ] **Step 3: Conferir o conteúdo** — `docker run --rm --entrypoint sh rastru:local -c 'ls /app/wwwroot/index.html /app/sql/specs/02-modelo-de-dados.sql /app/sql/db/massa-vitrine.sql /app/sql/db/migracoes/0001-migracao-aplicada.sql && ! ls /app/sql/db/massa-carga-6a.sql 2>/dev/null && ldconfig -p | grep -q libicuuc && echo ok'`. Expected: `ok`.
- [ ] **Step 4: Nenhum segredo** — `docker history --no-trunc rastru:local` e `docker run --rm --entrypoint sh rastru:local -c 'env'` não mostram `ca_extra`, `HTTPS_PROXY` nem senha; `/app/appsettings.json` é o do repositório (o placeholder da `SigningKey`, que derruba a API se ninguém o trocar).
- [ ] **Step 5: Cai como deve** — `docker run --rm rastru:local` sem `Jwt__SigningKey`: sai ≠ 0 com `OptionsValidationException` citando `SigningKey` (a subida de verdade é da Task 9); `docker run --rm rastru:local banco nada` sai 2.
- [ ] **Step 6: Commit**

```bash
git add Dockerfile .dockerignore
git commit -m "feat(deploy): imagem unica multi-stage com o SPA e os scripts do banco"
```

---

### Task 9: O Compose, o Caddyfile e a verificação local (C2, C4, C5, C13, C15)

**Files:**
- Create: `deploy/compose.yaml`, `deploy/Caddyfile`, `deploy/subir-local.sh`, `deploy/verificar.sh`, `deploy/exemplos/{sqlserver,hml,banco-hml}.env.exemplo`
- Modify: `.gitignore` (`deploy/local/`)

**Interfaces:**
- Consumes: Tasks 2, 3, 5, 6, 8.
- Produces (a parte 3 escreve os mesmos arquivos de segredo na VPS e chama os mesmos scripts):
  - `deploy/compose.yaml` — rede `rastru` com sub-rede fixa `172.30.0.0/24`; `sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`, `MSSQL_PID=Express`, `MSSQL_MEMORY_LIMIT_MB=2048`, `ACCEPT_EULA=Y`, `env_file` `sqlserver.env`, volume `mssql`, **sem `ports`**, `healthcheck` com o `sqlcmd` do container); `api-hml` (`image: ${RASTRU_IMAGEM:?defina RASTRU_IMAGEM}`, `env_file` `hml.env`, `environment: ProxyReverso__Rede: 172.30.0.0/24` ao lado da rede que ela descreve, `depends_on` sqlserver saudável, `restart: unless-stopped`); `banco-hml` (mesma imagem, `profiles: [ferramentas]`, `env_file` `hml.env` + `banco-hml.env`, `command: ["banco", "preparar"]`); `caddy` (`caddy:2`, portas `${RASTRU_PORTA_HTTP:-80}:80` e `${RASTRU_PORTA_HTTPS:-443}:443`, `Caddyfile` montado, volumes `caddy_data` e `caddy_config` — o certificado não é pedido de novo a cada deploy). Os `env_file` ficam em `${RASTRU_SEGREDOS:-.}/` (ao lado do `compose.yaml` na VPS; `local/` aqui).
  - Arquivos de segredo: `sqlserver.env` (`MSSQL_SA_PASSWORD`); `hml.env` (`ConnectionStrings__Rastreamento` com `rastru_hml` e `Rastreamento_HML`, `Jwt__SigningKey`); `banco-hml.env` (`Banco__ConexaoAdministrativa`, `Banco__Nome=Rastreamento_HML`, `Banco__Login=rastru_hml`, `Banco__SenhaDoLogin`, `Banco__Vitrine=true`, `Senhas__admin` … `Senhas__gestao`). Os `.exemplo` trazem as chaves com valor `troque-…`.
  - `deploy/Caddyfile` — um site `{$RASTRU_DOMINIO_HML}` com `encode zstd gzip` e `reverse_proxy api-hml:8080`; nada de limite de corpo. Com `hml.localhost` o Caddy usa a CA interna dele sozinho.
  - `deploy/verificar.sh <url-base> <servico-do-banco>` — a C13 e mais duas: `GET /api/saude` 200 (até 20 tentativas, 5 s entre elas); `GET /` e `GET /pedidos` 200 com `<div id="root">`; `GET /api/setores` 401 (a API atrás do proxy); `GET /API/setores` 404; `docker compose run --rm -T <servico> conferir-senhas` sai 0. Para no primeiro que falha, com mensagem em português e código ≠ 0. Respeita `CURL_CA_BUNDLE` (nativo do curl).
  - `deploy/subir-local.sh [--derrubar]` — gera `deploy/local/*.env` com segredos aleatórios (`openssl rand`; a senha `sa` e a do login cumprem a política de complexidade do SQL Server) só se faltarem; `RASTRU_IMAGEM=rastru:local` (constrói se não existe), `RASTRU_DOMINIO_HML=hml.localhost`, `RASTRU_SEGREDOS=local`; sobe `sqlserver`, roda `banco-hml`, sobe `api-hml` e `caddy`, copia a raiz da CA local do Caddy para `deploy/local/caddy-raiz.crt`, exporta `CURL_CA_BUNDLE` e `NO_PROXY=hml.localhost,localhost,127.0.0.1`, e chama `verificar.sh https://hml.localhost banco-hml`. `--derrubar`: `docker compose down -v` e apaga `deploy/local/`.

- [ ] **Step 1: Escrever** os arquivos. `bash -n` nos dois scripts; `docker compose -f deploy/compose.yaml config` com `RASTRU_IMAGEM` vazia falha citando a variável, e com ela definida passa.
- [ ] **Step 2: Subir do zero** — `bash deploy/subir-local.sh`. Expected: termina com a verificação inteira verde.
- [ ] **Step 3: Conferir o que a verificação não olha**:
  - `docker compose -f deploy/compose.yaml port sqlserver 1433` não devolve nada (a porta não sai da rede).
  - `curl -sI -H 'Accept-Encoding: gzip' https://hml.localhost/assets/<o .js do index.html>` traz `Content-Encoding: gzip` (fecha a pendência "resposta sem compressão" da 6A) e o `Cache-Control` longo da Task 1.
  - Login de `admin` com a senha de `deploy/local/banco-hml.env` → 200 e cookie de refresh; com `Admin@123` → 401.
  - Log da `api-hml` do login: o IP é o do cliente vindo pelo Caddy, não `172.30.0.x` do Caddy (a C15 na prática).
- [ ] **Step 4: Os dados sobrevivem a um "deploy"** (D6) — criar um Setor pela API, `docker compose … up -d --force-recreate api-hml`, rodar `banco-hml` de novo (sai 0, "nada a migrar"), e o Setor continua lá; `verificar.sh` verde de novo.
- [ ] **Step 5: Derrubar** — `bash deploy/subir-local.sh --derrubar`; nada sobra em `docker ps -a`/`docker volume ls` do projeto.
- [ ] **Step 6: Commit**

```bash
git add deploy .gitignore
git commit -m "feat(deploy): compose com SQL Server Express e Caddy, subida local e a verificacao C13"
```

---

### Task 10: O job `imagem` no CI e a prosa da parte 2

**Files:**
- Modify: `.github/workflows/ci.yml` (job novo `imagem`)
- Modify: `CLAUDE.md` (a linha **CI/CD** de «Stack», a seção «CI»), `specs/03-arquitetura-tecnica.md` («Hospedagem»: container + Caddy decidido; «CI/CD»; «Pontos em aberto»: itens 1 e 4 resolvidos pelo Caddy no desenho, 3 com o procedimento pronto no Compose — o deploy público em si é da parte 3), `specs/06-roadmap-mvp.md` (a lista da parte 2 ganha "comando de senha" e `Caddyfile`; o estado da parte 2)

**Interfaces:**
- Consumes: `deploy/subir-local.sh` (Task 9).
- Produces: job `imagem` (`ubuntu-24.04`, `timeout-minutes: 30`, `permissions: contents: read`, sem segredo): checkout → `bash deploy/subir-local.sh` → em falha, `docker compose -f deploy/compose.yaml logs` → sempre `bash deploy/subir-local.sh --derrubar`. Roda em paralelo com `backend` e `front`; **não** é check obrigatório (tornar é decisão do usuário, como os outros dois).

- [ ] **Step 1: Escrever o job** e conferir o YAML (`python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"`). O comentário do topo do `ci.yml` passa a falar dos três jobs e de que só `backend` e `front` são os candidatos a obrigatório.
- [ ] **Step 2: Prosa** — os quatro arquivos da lista. No `CLAUDE.md`, «CI» diz o que o `imagem` prova (a imagem constrói, o banco novo nasce com a vitrine, a C13 passa) e o tempo medido na primeira execução, quando houver.
- [ ] **Step 3: Suítes inteiras** — `dotnet build Rastreamento.slnx -warnaserror`; `dotnet test Rastreamento.slnx -m:1`; em `web/`, `npm test -- --run --maxWorkers=2` e `npm run build`. Registrar os números (Api, Application, Infrastructure, front) no relatório.
- [ ] **Step 4: Commit e push**

```bash
git add .github/workflows/ci.yml CLAUDE.md specs/03-arquitetura-tecnica.md specs/06-roadmap-mvp.md
git commit -m "ci: job imagem sobe o compose local e roda a verificacao C13"
git push -u origin claude/rastru-cicd-parte-2-plan-mz8oqt
```

- [ ] **Step 5: Parar e avisar.** O CI só roda em PR para a `main` (C22): a primeira execução do job `imagem` acontece quando o usuário abrir o PR (ou autorizar o controlador a abrir). Depois dela: review de branch inteira no modelo mais capaz, fix wave, re-review; merge é do usuário.
