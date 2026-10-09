# Fase 3B — Kit e montagem — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer o Kit entrar na Solda só em conjuntos completos, mostrar ao Movimentador os "Kits montáveis" e
os "Kits incompletos", marcar o nó de Kit na Fila com a pílula azul, recusar filho acrescentado a nó já
iniciado e pôr o Pedido no título da página do Agrupamento.

**Architecture:** `Setor.UtilizaKit` entra pelo caminho Database First. A calculadora da execução
(`CalculadoraDeExecucao`) passa a saber quais nós são de Agrupamento Kit e quais Setores têm `UtilizaKit`, e ganha
`RecebeEmConjunto`, `ConjuntosAEspera`, `TetoDeEntrada` e `KitsDaColeta`. A entrega (`EntregaUseCase`) valida o
conjunto completo com essas mesmas funções, numa classe nova (`ConjuntoCompleto`). `GET /tarefas` passa a devolver
um objeto com os grupos de hoje e os Kits. O front ganha os cartões de Kit nas Tarefas, a pílula `kit` na Fila, a
caixa "Utiliza Kit" em Setores e o título com link no Agrupamento.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), React Router,
Vitest + Testing Library + jsdom, Tailwind.

**Spec:** `docs/superpowers/specs/2026-10-09-fase-3b-kit-e-montagem-design.md`, aprovada em 2026-10-09 (decisões
D1–D9). Branch: `fase-3b`, sobre `32f8fbc`. A spec está em `2729e3c`.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de
  Critical/Important → re-review). Dispensa prevista só para a **Task 12**, a conferência no navegador, que é **do
  usuário** e cujo produto é relatório. A justificativa vai escrita **antes**, no ledger e no relatório.
- **A review de branch inteira só é despachada depois que o usuário fizer a Task 12** (regra do usuário de
  2026-10-07, no `CLAUDE.md`). O controlador para e avisa quando chegar nela.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com
  `scripts/review-package BASE HEAD`, nunca com `HEAD~1`. Ledger do plano em `.superpowers/sdd/2026-10-09-fase-3b/`;
  artefatos com nome escopado por este plano (os números de task colidem com os de planos anteriores).
- **Schema muda pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-fase-3b.sql`
  idempotente (formato de `db/alter-fase-3d.sql`, aplicado com `-b -f 65001`), depois o mapeamento EF. Nada de
  `Add-Migration` nem `EnsureCreated`.
- **Onde vale o conjunto completo (D3):** o pai é de Agrupamento `Kit`, tem filhos, e o Setor do **primeiro passo** dele
  tem `UtilizaKit`. Só a entrega com destino `AguardandoMontagem` é agrupada; a que vai para `NoSetor` (próximo passo)
  segue como hoje.
- **Conjuntos à espera (D5):** o maior, entre os filhos diretos, de `⌈AguardandoMontagem(c) ÷ QuantidadePorPai(c)⌉`,
  somando só os Setores com `UtilizaKit`. **Teto:** `max(0, Quantidade − TotalMontado − ConjuntosÀEspera)`. No
  redirecionamento, a quantidade que sai de `AguardandoMontagem` de um Setor com `UtilizaKit` sai da conta da espera.
- **Códigos novos:** `ConjuntoIncompleto` (400, `TipoDeErro.Validacao`), `AlemDoQueOPaiPrecisa` (409,
  `TipoDeErro.Conflito`), `PaiJaIniciado` (409, `TipoDeErro.Conflito`).
- **Pílula `kit`:** fundo `--color-kit: #1D4ED8` com texto `text-superficie` (branco), 6,70:1. Só na Fila do Setor. É
  cor de **identidade de categoria**, nunca estado.
- Rótulos de tela, exatos: seção **"Kits montáveis"**; seção recolhida **"Kits incompletos (N)"**; campo
  **"Conjuntos"**; caixa **"Levar"**; pílula **"Kit"**; caixa e pílula de Setor **"Utiliza Kit"**; botão da Fila
  **"Levar o Kit para {Setor}"**; botão de entrega **"Entregar"**, "Entregar 1 Kit", "Entregar 2 Kits", "Entregar 3
  itens", "Entregar 2 Kits e 3 itens".
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês.
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa. Comentário de código **não cita** ledger,
  brief nem "Task N": o repositório é público. Decisões podem ser citadas como "D*n* da spec da Fase 3B" ou "decisão
  P*n* do plano da Fase 3B".
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx
  -m:1`. Teste de banco **escopa a asserção** nas linhas que ele inseriu.
- Front: `npm test -- --run` **e** `npm run build` (o Vitest não faz checagem de tipo). Primitivas de
  `web/src/components/`, cores só por token, `// @vitest-environment jsdom` + `afterEach(cleanup)`, mocks por
  `web/src/testes/api.ts`. Teste acha elemento por papel e nome acessível.
- Texto de tela em português **com** acento. Mensagem de erro do backend: siga o arquivo vizinho (os `.cs` da
  `Execucao` escrevem as frases **com** acento; os comentários, sem).
- Edite fonte com Edit/Write, nunca com `Set-Content` do PowerShell 5.1 (corrompe UTF-8 e a suíte fica verde assim
  mesmo).
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em chamada
  separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm ci && npm test -- --run && npm run build
```

Banco desta máquina: o `db/alter-fase-3b.sql` (Task 1) é aplicado com o comando do bloco "Fase 3B" que a Task 1
acrescenta ao `CLAUDE.md`. Sem ele, a suíte de banco falha na coluna `UtilizaKit`.

**Baseline:** o ledger registra, medido em 2026-10-09 na `bf0882d`, backend **1270** (Api 350 · Application 706 ·
Infrastructure 214) e, na `c492efd`, front **1354 testes / 77 arquivos**. **O pré-flight remede na `2729e3c` antes da
Task 1** e escreve o número no ledger do plano. Cada implementer reporta o **delta** da própria task, nunca um total
previsto por este plano.

## Decisões deste plano (onde a spec deixou a escolha ou o código pediu um ajuste)

- **P1 — `ConjuntoIncompleto` é 400.** A spec deixou o tipo ao plano. É o mesmo critério do `DestinoIndevido`: o
  corpo da entrega não forma o que a regra pede, seja qual for o saldo. O teto (`AlemDoQueOPaiPrecisa`) depende do
  estado e é 409, como o `SaldoInsuficiente`.
- **P2 — Os dados de Kit entram pelo `IExecucaoRepository`.** Duas leituras novas (`ListarNosDeKitAsync`,
  `ListarSetoresComKitAsync`), chamadas pelo `LeitorDeEstado`, e não um `ISetorRepository` a mais nos três casos de
  uso: o leitor já é o único caminho da calculadora até o banco.
- **P3 — `NoDoCalculo.DeKit` e o conjunto de Setores entram como parâmetros opcionais** (`bool DeKit = false` e
  `IReadOnlySet<int>? setoresComKit = null`). Toda construção existente continua compilando e continua Avulso.
- **P4 — A entrega carrega os irmãos.** Para conferir "todos os filhos na lista", o `EntregaUseCase` lê os filhos de
  cada pai dos itens (`ListarFilhosAsync`) e os põe no estado, sem travá-los: só os nós da lista são escritos, e o
  conjunto completo obriga a lista a trazer todos eles, então duas entregas do mesmo Kit travam os mesmos nós.
- **P5 — No filtro das Tarefas, a unidade do Kit é o pai e os filhos prontos.** A spec dizia "avaliados pelo pai".
  O Material mora nos filhos, e a Fila já trata o cartão de montagem assim (`nosDoCartao`, em `filtroDaDemanda.ts`):
  filtrar Kit só pelo pai esconderia o Kit de quem filtra pela chapa do filho.
- **P6 — A seção recolhida é uma primitiva nova, `SecaoRecolhivel`**, em `web/src/components/`, com teste próprio
  (regra do `CLAUDE.md`: faltou primitiva, cria-se lá).
- **P7 — Conjuntos são inteiros e a multiplicação arredonda a quatro casas** (`quantidadeDoConjunto`), para
  `3 × 0,1` não virar `0,30000000000000004` no corpo da entrega, que o servidor compararia com `0,3` e recusaria.
- **P8 — `AgrupamentoDto` ganha `PedidoNumero` em toda resposta**, não só no `GET /agrupamentos/{id}`: é um DTO só, e
  as três outras rotas já sabem o Pedido.
- **P9 — A pílula "Kit" e o redirecionamento por Kit usam o tipo do Agrupamento no resumo do nó (`agrupamentoTipo`)
  e a flag `conjuntoCompleto` do grupo de montagem**, ambos calculados no servidor.

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-fase-3b.sql` | 1 | `Setor.UtilizaKit` |
| `src/Rastreamento.Domain/Entities/Setor.cs`, `Application/Cadastros/{Dtos,CadastroDeSetorUseCase}.cs` | 1 | cadastro |
| `Application/Estrutura/MontagemDeEstruturaUseCase.cs`, `Application/Execucao/CodigosDaExecucao.cs` | 2 | `PaiJaIniciado` |
| `web/src/api/estrutura.ts` | 2 | o 409 novo chega ao painel |
| `Domain/Abstractions/IExecucaoRepository.cs`, `Infrastructure/Persistence/ExecucaoRepository.cs` | 3, 5 | dados de Kit, tipo do Agrupamento |
| `Application/Execucao/{LeitorDeEstado,CalculadoraDeExecucao}.cs` | 3 | a regra do Kit, pura |
| `Application/Execucao/{ConjuntoCompleto,EntregaUseCase}.cs` | 4 | validação da entrega |
| `Application/Execucao/{ConsultaDeExecucaoUseCase,ExecucaoDtos}.cs`, `Api/Controllers/EntregaController.cs` | 5 | leituras |
| `Application/Cadastros/CadastroDeAgrupamentoUseCase.cs`, `web/src/components/Pagina.tsx`, `AgrupamentoDetalhePage.tsx` | 6 | C2 |
| `web/src/pages/SetoresPage.tsx`, `web/src/api/cadastros.ts` | 7 | caixa e pílula de Setor |
| `web/src/index.css`, `components/Pilula.tsx`, `execucao/conjuntos.ts`, `execucao/FormularioDeQuantidade.tsx`, `pages/FilaDoSetorPage.tsx` | 8 | Fila |
| `components/SecaoRecolhivel.tsx`, `execucao/CartaoDeKit.tsx`, `execucao/filtroDaDemanda.ts`, `pages/TarefasPage.tsx`, `api/execucao.ts`, `api/erros.ts` | 9 | Tarefas |
| `specs/01`, `04`, `06`, `CLAUDE.md`, spec do Kit | 10 | documentos |
| `db/massa-conferencia-fase-3b.sql` | 11 | massa da conferência |

---

### Task 1: `Setor.UtilizaKit` — schema, migração e cadastro no backend

**Modelo:** sonnet. Molde mecânico: o commit `e3007b4` fez o mesmo caminho para `Setor.Atividade`.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `dbo.Setor`)
- Create: `db/alter-fase-3b.sql`
- Modify: `src/Rastreamento.Domain/Entities/Setor.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`SetorDto`, `NovoSetorDto`)
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeSetorUseCaseTests.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/SetorUtilizaKitMapeamentoTests.cs`
- Test: `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`
- Modify: `specs/05-api-endpoints.md` (seção de Setores), `CLAUDE.md` (bloco do alter)

**Interfaces:**
- Produces: `Setor.UtilizaKit` (`bool`); `SetorDto(int Id, string Nome, bool Ativo, string? Atividade, bool UtilizaKit)`;
  `NovoSetorDto(string Nome, string? Atividade = null, bool UtilizaKit = false)`; JSON `utilizaKit`.

- [ ] **Step 1: Escrever os testes que falham (Application)**

Acrescente ao fim de `CadastroDeSetorUseCaseTests`:

```csharp
  [Fact]
  public async Task Cadastrar_grava_UtilizaKit()
  {
    var repo = new FakeSetorRepo();
    var r = await new CadastroDeSetorUseCase(repo).Cadastrar(new NovoSetorDto("Solda", null, true), CancellationToken.None);

    Assert.True(r.Valor!.UtilizaKit);
    Assert.True(Assert.Single(await new CadastroDeSetorUseCase(repo).Listar(false, CancellationToken.None)).UtilizaKit);
  }

  [Fact]
  public async Task Cadastrar_sem_UtilizaKit_grava_falso()
  {
    var r = await new CadastroDeSetorUseCase(new FakeSetorRepo()).Cadastrar(new NovoSetorDto("Corte"), CancellationToken.None);

    Assert.False(r.Valor!.UtilizaKit);
  }

  [Fact]
  public async Task Editar_liga_e_desliga_UtilizaKit()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true });
    var uc = new CadastroDeSetorUseCase(repo);

    Assert.True((await uc.Editar(1, new NovoSetorDto("Solda", null, true), CancellationToken.None)).Valor!.UtilizaKit);
    Assert.False((await uc.Editar(1, new NovoSetorDto("Solda", null, false), CancellationToken.None)).Valor!.UtilizaKit);
  }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CadastroDeSetorUseCaseTests"`
Expected: FAIL de compilação (`NovoSetorDto` não tem o terceiro parâmetro; `SetorDto` não tem `UtilizaKit`).

- [ ] **Step 3: Schema, migração e entidade**

Em `specs/02-modelo-de-dados.sql`, na `dbo.Setor`, logo depois de `Atividade`:

```sql
    -- Regra 25: o Setor onde o Kit é montado (a Solda). Só a filhos de Agrupamento Kit que vão à montagem do pai
    -- num Setor marcado se exige conjunto completo (spec da Fase 3B, D3).
    UtilizaKit      BIT                 NOT NULL CONSTRAINT DF_Setor_UtilizaKit DEFAULT (0),
```

Crie `db/alter-fase-3b.sql`:

```sql
-- Migracao idempotente da Fase 3B para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: o bloco confere antes de agir. Nao ha dado a transformar: todo Setor
-- existente nasce sem UtilizaKit. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. Setor.UtilizaKit (spec da Fase 3B, secao 3) ----------------------------------------------------- */
IF COL_LENGTH('dbo.Setor', 'UtilizaKit') IS NULL
    ALTER TABLE dbo.Setor ADD UtilizaKit BIT NOT NULL CONSTRAINT DF_Setor_UtilizaKit DEFAULT (0);
GO
```

Em `Setor.cs`, depois de `Atividade`:

```csharp
  /// <summary>
  /// Regra 25: o Setor onde o Kit e montado. Filho de Agrupamento Kit que vai a montagem do pai num Setor
  /// marcado so entra em conjunto completo (spec da Fase 3B, D3). Sem HasDefaultValue no EF: o default vive no .sql.
  /// </summary>
  public bool UtilizaKit { get; set; }
```

Aplique o alter no banco local:

```bash
MSYS_NO_PATHCONV=1 docker compose cp db/alter-fase-3b.sql sqlserver:/tmp/alter-fase-3b.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-fase-3b.sql
```

Rode o mesmo comando uma segunda vez e confirme que sai com 0 (idempotência).

- [ ] **Step 4: DTOs e caso de uso**

Em `Dtos.cs`:

```csharp
public sealed record SetorDto(int Id, string Nome, bool Ativo, string? Atividade, bool UtilizaKit);
```

```csharp
public sealed record NovoSetorDto(
    [MaxLength(100)] string Nome, [MaxLength(40)] string? Atividade = null, bool UtilizaKit = false);
```

Acrescente ao `<remarks>` do `NovoSetorDto`: "`UtilizaKit` ausente grava falso; como o `PUT` é substituição inteira,
quem edita manda o valor atual."

Em `CadastroDeSetorUseCase`: no `Cadastrar`, `new Setor { Nome = nome, Ativo = true, Atividade = NormalizarOpcional(novo.Atividade), UtilizaKit = novo.UtilizaKit }`;
no `Editar`, `setor.UtilizaKit = alterado.UtilizaKit;` ao lado de `setor.Atividade = ...`; e as **três** construções de
`SetorDto` passam a terminar em `, setor.UtilizaKit)` (no `Listar`, `s.UtilizaKit`).

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CadastroDeSetorUseCaseTests"`
Expected: PASS.

- [ ] **Step 6: Mapeamento EF (Infrastructure)**

Crie `SetorUtilizaKitMapeamentoTests.cs`, no molde do `SetorAtividadeMapeamentoTests`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>`dbo.Setor.UtilizaKit` (spec da Fase 3B, secao 3) faz a volta completa, e o default do .sql e falso.</summary>
public class SetorUtilizaKitMapeamentoTests : TesteComBanco
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task UtilizaKit_faz_a_volta_completa(bool utilizaKit)
  {
    var nome = $"setor-kit-{Guid.NewGuid():N}"[..30];
    int id;
    await using (var db = NovoContexto())
    {
      var setor = new Setor { Nome = nome, Ativo = true, UtilizaKit = utilizaKit };
      db.Setores.Add(setor);
      await db.SaveChangesAsync();
      id = setor.Id;
    }
    try
    {
      await using var leitura = NovoContexto();
      Assert.Equal(utilizaKit, (await leitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id)).UtilizaKit);
    }
    finally
    {
      await using var limpeza = NovoContexto();
      await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {id}");
    }
  }

  [Fact]
  public async Task Insert_sem_a_coluna_grava_falso()
  {
    var nome = $"setor-kit-{Guid.NewGuid():N}"[..30];
    await using var db = NovoContexto();
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.Setor (Nome) VALUES ({nome})");
    try
    {
      Assert.False((await db.Setores.AsNoTracking().SingleAsync(s => s.Nome == nome)).UtilizaKit);
    }
    finally
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Nome = {nome}");
    }
  }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~SetorUtilizaKitMapeamentoTests"`
Expected: PASS (o EF mapeia `bool` para `BIT` sem configuração; `SetorConfiguration` não muda).

- [ ] **Step 7: Endpoint (Api)**

Acrescente ao fim de `SetoresEndpointsTests`:

```csharp
  [Fact]
  public async Task Cadastrar_e_editar_com_UtilizaKit_e_ele_volta_na_lista()
  {
    var admin = ClienteComo("Administrador");
    var nome = NomeUnico();
    var criado = await admin.PostAsJsonAsync("/api/setores", new { nome, utilizaKit = true });
    var id = (await criado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    var antes = (await admin.GetFromJsonAsync<JsonElement>("/api/setores")).EnumerateArray()
        .Single(s => s.GetProperty("id").GetInt32() == id).GetProperty("utilizaKit").GetBoolean();
    var editado = await admin.PutAsJsonAsync($"/api/setores/{id}", new { nome, utilizaKit = false });
    var depois = (await admin.GetFromJsonAsync<JsonElement>("/api/setores")).EnumerateArray()
        .Single(s => s.GetProperty("id").GetInt32() == id).GetProperty("utilizaKit").GetBoolean();

    Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
    Assert.True(antes);
    Assert.False(depois);
  }
```

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~SetoresEndpointsTests"`
Expected: PASS.

- [ ] **Step 8: Documentos da task**

- `specs/05-api-endpoints.md`, seção de Setores: `utilizaKit` (booleano) no corpo de `POST /setores` e `PUT
  /setores/{id}` (opcional, padrão falso; o `PUT` é substituição inteira) e na resposta de `GET /setores`. Uma frase:
  "marca o Setor onde o Kit é montado (regra 25)".
- `CLAUDE.md`, seção "Comandos", depois do bloco "Data de entrega do Pedido — `db/alter-data-entrega.sql`", um bloco
  novo no mesmo formato:

```markdown
**Fase 3B — `db/alter-fase-3b.sql`.** Mesmo formato dos anteriores (idempotente, `-b -f 65001`). Leva um banco
anterior até o `02-modelo-de-dados.sql`: a coluna `Setor.UtilizaKit` (`BIT NOT NULL`, default `0`). Todo Setor
existente nasce sem a marca; a Solda é marcada pela tela de Setores. Não precisa regenerar o banco.

```bash
MSYS_NO_PATHCONV=1 docker compose cp db/alter-fase-3b.sql sqlserver:/tmp/alter-fase-3b.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-fase-3b.sql
```
```

- [ ] **Step 9: Build, suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` (verde).

```bash
git add specs/02-modelo-de-dados.sql db/alter-fase-3b.sql src/Rastreamento.Domain/Entities/Setor.cs \
  src/Rastreamento.Application/Cadastros/Dtos.cs src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs \
  tests/Rastreamento.Application.Tests/Cadastros/CadastroDeSetorUseCaseTests.cs \
  tests/Rastreamento.Infrastructure.Tests/Persistence/SetorUtilizaKitMapeamentoTests.cs \
  tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs specs/05-api-endpoints.md CLAUDE.md
git commit -m "feat(setor): UtilizaKit marca o Setor onde o Kit e montado"
```

---

### Task 2: Guarda `PaiJaIniciado` — acrescentar filho a nó já iniciado

**Modelo:** sonnet. Uma guarda, no molde da `QuantidadeAbaixoDoMovimentado` do mesmo caso de uso.

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` (`AcrescentarFilho`)
- Test: `tests/Rastreamento.Application.Tests/Execucao/EdicoesNaExecucaoTests.cs`
- Test: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`
- Modify: `web/src/api/estrutura.ts` (`CodigoDeConflitoDeEstrutura`, `codigosDeConflito`)
- Test: `web/src/api/estrutura.test.ts`, `web/src/pages/AgrupamentoDetalhePage.test.tsx`
- Modify: `specs/05-api-endpoints.md` (`POST /estrutura/{id}/filhos`)

**Interfaces:**
- Produces: `CodigosDaExecucao.PaiJaIniciado = "PaiJaIniciado"`; `POST /estrutura/{id}/filhos` responde 409
  `{ erro: "PaiJaIniciado", mensagem }`.

- [ ] **Step 1: Testes que falham (Application)**

Acrescente a `EdicoesNaExecucaoTests`:

```csharp
  [Fact]
  public async Task Acrescentar_filho_a_no_ja_iniciado_da_PaiJaIniciado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Estrutura().AcrescentarFilho(1, new NovoFilhoDto(null, "Arruela", 20m, 2m), Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.PaiJaIniciado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("No 1", r.Detalhe);
    Assert.DoesNotContain(c.Estruturas.Itens, i => i.EstruturaPaiId == 1);
  }

  [Fact]
  public async Task Acrescentar_filho_a_no_que_nao_saiu_de_a_iniciar_e_aceito_e_trava_o_pai()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);

    var r = await c.Estrutura().AcrescentarFilho(1, new NovoFilhoDto(null, "Arruela", 20m, 2m), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Depois_de_estornar_todo_inicio_acrescentar_filho_volta_a_ser_aceito()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    var inicio = c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    c.Mover(1, TiposDeMovimentacao.Estorno, Local.NoSetor(Corte, 1), Local.AIniciar, 6m).EstornoDeId = inicio.Id;

    var r = await c.Estrutura().AcrescentarFilho(1, new NovoFilhoDto(null, "Arruela", 20m, 2m), Ct);

    Assert.True(r.Sucesso);
  }
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EdicoesNaExecucaoTests"`
Expected: FAIL de compilação (`CodigosDaExecucao.PaiJaIniciado` não existe).

- [ ] **Step 2: O código e a guarda**

Em `CodigosDaExecucao`, depois de `QuantidadeAbaixoDoMovimentado`:

```csharp
  public const string PaiJaIniciado = "PaiJaIniciado";
```

E acrescente ao `<summary>` da classe: "`PaiJaIniciado` é da guarda de acrescentar filho (spec da Fase 3B, D1)."

Em `MontagemDeEstruturaUseCase.AcrescentarFilho`, troque o comentário que começa em "Mesmo motivo de `CriarPeca`"
e o início do bloco do `ExecutarAsync` por:

```csharp
    // Mesmo motivo de `CriarPeca`: gravar e ler de volta na transacao da execucao (retry de 1205, 409
    // no esgotamento), com o catalogo e o planejamento FORA dela. Desde a Fase 3B o pai e TRAVADO e o
    // livro dele e lido antes de gravar: acrescentar filho a no que ja saiu de "a iniciar" e recusado
    // (spec da Fase 3B, D1) — as unidades ja iniciadas nao consumiriam o filho novo e sairiam acima do
    // montado. A trava serializa com o Iniciar, que trava o mesmo no.
    //
    // Residual conhecido, nao consertado: o pai e lido FORA da transacao para planejar a copia. Um
    // `DELETE /estrutura/{id}` concorrente do pai, entre essa leitura e a trava, faz `TravarNosAsync` nao o
    // achar, e a escrita devolve 404 em vez de esbarrar na FK.
    return await _execucao.ExecutarAsync(async () =>
    {
      var travado = await _execucao.TravarNosAsync([paiId], ct);
      if (travado.Count == 0)
        return Result<EstruturaItemDto>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);
      var estado = await _leitor.CarregarAsync(travado, ct);
      if (estado.Calc.SaidoDeAIniciar(paiId) > 0m)
        return Result<EstruturaItemDto>.Falha(CodigosDaExecucao.PaiJaIniciado, TipoDeErro.Conflito,
            $"{estado.Nome(paiId)} já entrou em produção: não se acrescenta filho a um nó já iniciado.");

      var novoId = await _estruturas.GravarArvoreAsync(pai.AgrupamentoId, paiId, paraGravar, ct);
```

(O resto do bloco — `MontarAsync`, `BuscarNo`, o `Ok` — fica igual.)

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EdicoesNaExecucaoTests|FullyQualifiedName~MontagemDeEstrutura"`
Expected: PASS. Se um teste existente de `AcrescentarFilho` afirmava **não** travar, ele mudou de premissa: ajuste-o e
diga no relatório qual foi.

- [ ] **Step 3: Endpoint (Api)**

Acrescente a `ExecucaoEndpointsTests.Comportamento.cs`:

```csharp
  [Fact]
  public async Task Acrescentar_filho_a_no_ja_iniciado_da_409_PaiJaIniciado()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 2m }));

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)null, descricao = "Arruela", quantidade = 40m, quantidadePorPai = 2m });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    Assert.Equal("PaiJaIniciado", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }
```

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~Acrescentar_filho_a_no_ja_iniciado"`
Expected: PASS (`EstruturaController.Recusar` já devolve 409 com `erro` e `mensagem` para `TipoDeErro.Conflito`).

- [ ] **Step 4: Front — o 409 chega ao painel**

Em `web/src/api/estrutura.ts`, acrescente `| 'PaiJaIniciado'` a `CodigoDeConflitoDeEstrutura` e `'PaiJaIniciado'` a
`codigosDeConflito`; no comentário acima do tipo, uma frase: "Desde a Fase 3B, `acrescentarFilho` também responde
`PaiJaIniciado` (D1 da spec da Fase 3B)."

Em `estrutura.test.ts`, um teste novo:

```ts
  it('acrescentarFilho devolve o conflito PaiJaIniciado com a frase do servidor', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'PaiJaIniciado', mensagem: 'frase do servidor' }), { status: 409 }),
    ))

    const resultado = await acrescentarFilho(7, { componenteId: null, descricao: 'Arruela', quantidade: 4, quantidadePorPai: 2 })

    expect(resultado).toEqual({ erro: 'PaiJaIniciado', mensagem: 'frase do servidor' })
  })
```

(Confira a assinatura de `acrescentarFilho` em `estrutura.ts` e o import no topo do teste; se o corpo tiver outro
formato, use o dele.)

Em `AgrupamentoDetalhePage.test.tsx`, ao lado de *"409 de ciclo ao acrescentar mostra a mensagem que nomeia o
caminho"*:

```tsx
  it('acrescentar filho a nó já iniciado mostra a frase do servidor no painel', async () => {
    const frase = 'Chassi já entrou em produção: não se acrescenta filho a um nó já iniciado.'
    vi.stubGlobal('fetch', montarFetch({
      estruturaInicial: [PECA],
      respostaFilhos: { status: 409, corpo: { erro: 'PaiJaIniciado', mensagem: frase } },
    }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    fireEvent.click(screen.getByRole('button', { name: 'Acrescentar filho' }))
    const painel = screen.getByTestId('painel-de-escrita')
    fireEvent.click(within(painel).getByRole('combobox'))
    const listbox = await within(painel).findByRole('listbox')
    fireEvent.click(await within(listbox).findByText('CH-100'))
    fireEvent.change(within(painel).getByLabelText('Quantidade'), { target: { value: '3' } })
    fireEvent.change(within(painel).getByLabelText('Quantidade por pai'), { target: { value: '2' } })
    fireEvent.click(within(painel).getByRole('button', { name: 'Acrescentar' }))

    const banner = await screen.findByText(frase)
    expect(banner.closest('form')).toBe(screen.getByTestId('painel-de-escrita'))
  })
```

Run: `cd web && npx vitest run src/api/estrutura.test.ts src/pages/AgrupamentoDetalhePage.test.tsx`
Expected: PASS. **Mutação a medir:** tire `'PaiJaIniciado'` de `codigosDeConflito` e confirme que os dois testes
novos ficam vermelhos; devolva.

- [ ] **Step 5: Documento e commit**

`specs/05-api-endpoints.md`, em `POST /estrutura/{id}/filhos`: a linha do 409 `PaiJaIniciado` ("o nó já saiu de a
iniciar; spec da Fase 3B, D1"). Se a seção diz que acrescentar filho é livre mesmo a nó iniciado, corrija a frase.

Run: build + suítes (backend `-m:1`, front `npm test -- --run` e `npm run build`).

```bash
git add src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs \
  src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs \
  tests/Rastreamento.Application.Tests/Execucao/EdicoesNaExecucaoTests.cs \
  tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs \
  web/src/api/estrutura.ts web/src/api/estrutura.test.ts web/src/pages/AgrupamentoDetalhePage.test.tsx \
  specs/05-api-endpoints.md
git commit -m "feat(estrutura): recusa filho acrescentado a no ja iniciado (PaiJaIniciado)"
```

---

### Task 3: O Kit no estado da execução e na calculadora

**Modelo:** opus. É a regra da fase em fórmulas (D3, D5) e a função que a tela e a recusa vão compartilhar; um
arredondamento ou um filtro de Setor errado aqui passa por todas as tasks seguintes.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Application/Execucao/LeitorDeEstado.cs`
- Modify: `src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `CenarioDeExecucao.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/KitsDaColetaTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`

**Interfaces:**
- Produces (repositório): `Task<IReadOnlySet<int>> ListarNosDeKitAsync(IReadOnlyCollection<int> ids, CancellationToken ct)`;
  `Task<IReadOnlySet<int>> ListarSetoresComKitAsync(CancellationToken ct)`.
- Produces (calculadora): `NoDoCalculo(..., bool DeKit = false)`; construtor com `IReadOnlySet<int>? setoresComKit = null`;
  `bool RecebeEmConjunto(int paiId)`; `bool SetorUtilizaKit(int setorId)`;
  `decimal ConjuntosAEspera(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null)`;
  `decimal TetoDeEntrada(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null)`;
  `IReadOnlyList<KitDaColeta> KitsDaColeta()`; records `FilhoDoKit` e `KitDaColeta` (código abaixo).
- Produces (testes): `FakeExecucaoRepo.AgrupamentosKit`, `FakeExecucaoRepo.SetoresComKit` (`HashSet<int>`);
  `CenarioDeExecucao.Kit()`.

- [ ] **Step 1: Contrato do repositório e o fake**

Em `IExecucaoRepository`, depois de `ListarPassosAlcancadosAsync`:

```csharp
  /// <summary>Dos nos pedidos, os que vivem num Agrupamento `Kit` (regra 25). No inexistente nao volta.</summary>
  Task<IReadOnlySet<int>> ListarNosDeKitAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Todo Setor com `UtilizaKit`, inativos inclusive: o que ja aguarda montagem num Setor inativado continua
  /// contando nos conjuntos a espera (spec da Fase 3B, D5).
  /// </summary>
  Task<IReadOnlySet<int>> ListarSetoresComKitAsync(CancellationToken ct);
```

Em `FakeExecucaoRepo`, ao lado de `Agrupamentos`:

```csharp
  /// <summary>AgrupamentoIds de Agrupamento `Kit`. Arranjo do teste; o padrao e Avulso.</summary>
  public HashSet<int> AgrupamentosKit { get; } = new();

  /// <summary>SetorIds com `UtilizaKit`. Arranjo do teste.</summary>
  public HashSet<int> SetoresComKit { get; } = new();
```

e as implementações, ao lado de `ListarPassosAlcancadosAsync`:

```csharp
  public Task<IReadOnlySet<int>> ListarNosDeKitAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlySet<int>>(_estruturas.Itens
          .Where(i => ids.Contains(i.Id) && AgrupamentosKit.Contains(i.AgrupamentoId))
          .Select(i => i.Id).ToHashSet());

  public Task<IReadOnlySet<int>> ListarSetoresComKitAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlySet<int>>(SetoresComKit.ToHashSet());
```

Em `CenarioDeExecucao`, depois de `Material`:

```csharp
  /// <summary>
  /// O Agrupamento do cenario vira Kit e a Solda ganha `UtilizaKit` (spec da Fase 3B, D3). A `Setor` do fake
  /// tambem e marcada, para quem le o catalogo ver o mesmo que a execucao.
  /// </summary>
  public void Kit(int agrupamento = AgrupamentoId)
  {
    Execucao.AgrupamentosKit.Add(agrupamento);
    Execucao.SetoresComKit.Add(Solda);
    foreach (var s in Catalogo.Setores.Where(s => s.Id == Solda)) s.UtilizaKit = true;
  }
```

e, em `Calcular()`, o `new NoDoCalculo(...)` passa a terminar em
`, Execucao.AgrupamentosKit.Contains(i.AgrupamentoId))` e o construtor da calculadora recebe um quinto argumento,
`Execucao.SetoresComKit`.

- [ ] **Step 2: Escrever os testes da calculadora (falham)**

Crie `KitsDaColetaTests.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// O Kit na calculadora (spec da Fase 3B, secoes 2 e 4.4). Cenario: Peca 1 de 10 que comeca na Solda; Item 2 de
/// 40 (razao 4) que termina no Corte; Item 3 de 10 (razao 1) que termina na Dobra.
/// </summary>
public class KitsDaColetaTests
{
  private static CenarioDeExecucao Cenario(bool kit = true, int setorDoPai = Solda)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, setorDoPai);
    c.No(2, 1, 40m, 4m, Corte);
    c.No(3, 1, 10m, 1m, Dobra);
    if (kit) c.Kit();
    return c;
  }

  private static void Pronto(CenarioDeExecucao c, int item, int setor, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);

  private static void EmEspera(CenarioDeExecucao c, int item, int origem, int setor, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(origem, 1), Local.AguardandoMontagem(setor), quantidade);

  [Fact]
  public void Kit_com_filhos_para_dois_conjuntos_e_montavel_com_dois()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 3m);

    var kit = Assert.Single(c.Calcular().KitsDaColeta());

    Assert.Equal(1, kit.PaiId);
    Assert.Equal(Solda, kit.SetorDeDestinoId);
    Assert.Equal(2m, kit.Conjuntos);
    Assert.True(kit.Montavel);
    Assert.Equal(new[] { (2, 8m, false), (3, 3m, false) }, kit.Filhos.Select(f => (f.FilhoId, f.Pronto, f.JaNoDestino)));
  }

  [Fact]
  public void Kit_que_nao_fecha_um_conjunto_e_incompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    var kit = Assert.Single(c.Calcular().KitsDaColeta());

    Assert.Equal(0m, kit.Conjuntos);
    Assert.False(kit.Montavel);
  }

  [Fact]
  public void Kit_sem_nenhum_filho_pronto_nao_aparece()
  {
    Assert.Empty(Cenario().Calcular().KitsDaColeta());
  }

  [Fact]
  public void Conjuntos_a_espera_contam_pelo_filho_mais_adiantado()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 12m);   // 3 conjuntos
    EmEspera(c, 3, Dobra, Solda, 2m);    // 2 conjuntos

    var calc = c.Calcular();

    Assert.Equal(3m, calc.ConjuntosAEspera(1));
    Assert.Equal(7m, calc.TetoDeEntrada(1));
  }

  [Fact]
  public void Conjunto_que_perdeu_parte_continua_a_espera()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 7m);    // 1,75 conjunto: arredonda para cima, 2
    EmEspera(c, 3, Dobra, Solda, 1m);    // 1 conjunto: o 2 e o mais adiantado so se arredondar para cima

    Assert.Equal(2m, c.Calcular().ConjuntosAEspera(1));
  }

  [Fact]
  public void Teto_desconta_o_total_montado()
  {
    var c = Cenario();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 4m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    Assert.Equal(6m, c.Calcular().TetoDeEntrada(1));
  }

  [Fact]
  public void Espera_em_Setor_sem_UtilizaKit_nao_conta()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Pintura, 8m);

    Assert.Equal(0m, c.Calcular().ConjuntosAEspera(1));
  }

  [Fact]
  public void O_que_esta_saindo_da_espera_sai_da_conta()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 8m);
    EmEspera(c, 3, Dobra, Solda, 2m);

    var saindo = new Dictionary<int, decimal> { [2] = 8m, [3] = 2m };

    Assert.Equal(0m, c.Calcular().ConjuntosAEspera(1, saindo));
    Assert.Equal(10m, c.Calcular().TetoDeEntrada(1, saindo));
  }

  [Fact]
  public void Kit_sem_teto_nao_aparece()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 40m);
    EmEspera(c, 3, Dobra, Solda, 10m);
    Pronto(c, 2, Corte, 4m);

    Assert.Empty(c.Calcular().KitsDaColeta());
  }

  [Fact]
  public void Conjuntos_nao_passam_do_teto()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 36m);   // 9 conjuntos a espera; teto 1
    EmEspera(c, 3, Dobra, Solda, 9m);
    Pronto(c, 2, Corte, 8m);             // os prontos fecham 2: so o teto segura em 1
    Pronto(c, 3, Dobra, 2m);

    Assert.Equal(1m, Assert.Single(c.Calcular().KitsDaColeta()).Conjuntos);
  }

  [Fact]
  public void Filho_que_termina_no_Setor_do_pai_esta_no_destino()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Solda);
    c.Kit();
    Pronto(c, 2, Solda, 3m);

    Assert.True(Assert.Single(Assert.Single(c.Calcular().KitsDaColeta()).Filhos).JaNoDestino);
  }

  [Fact]
  public void Filho_de_Kit_no_ultimo_passo_sai_do_Item_pronto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    Assert.DoesNotContain(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
  }

  [Fact]
  public void Filho_de_Avulso_continua_Item_pronto()
  {
    var c = Cenario(kit: false);
    Pronto(c, 2, Corte, 8m);

    Assert.Contains(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
    Assert.Empty(c.Calcular().KitsDaColeta());
  }

  [Fact]
  public void Pai_de_Kit_que_comeca_fora_de_Setor_com_UtilizaKit_nao_recebe_em_conjunto()
  {
    var c = Cenario(setorDoPai: Pintura);
    Pronto(c, 2, Corte, 8m);
    var calc = c.Calcular();

    Assert.False(calc.RecebeEmConjunto(1));
    Assert.Empty(calc.KitsDaColeta());
    Assert.Contains(calc.ColetasPendentes(), p => p.EstruturaItemId == 2);
  }

  [Fact]
  public void Filho_de_Kit_num_passo_intermediario_continua_Item_pronto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 40m, 4m, Corte, Dobra);
    c.Kit();
    Pronto(c, 2, Corte, 8m);   // passo 1 de 2: vai para a Dobra, nao para a montagem

    Assert.Contains(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
  }
}
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~KitsDaColetaTests"`
Expected: FAIL de compilação (`KitsDaColeta`, `RecebeEmConjunto`, `ConjuntosAEspera`, `TetoDeEntrada` não existem).

- [ ] **Step 3: A calculadora**

Em `CalculadoraDeExecucao.cs`, o record do nó passa a:

```csharp
/// <summary>
/// Um no como a calculadora o ve. `Roteiro` em ordem de `Ordem`. `DeKit`: o no vive num Agrupamento Kit
/// (regra 25); o padrao falso mantem Avulso toda construcao que nao o diz.
/// </summary>
public sealed record NoDoCalculo(
    int Id, int? PaiId, decimal Quantidade, decimal? QuantidadePorPai, IReadOnlyList<PassoDoCalculo> Roteiro,
    bool DeKit = false)
{
  public bool EhPeca => PaiId is null;
}
```

e, depois de `ColetaPendente`:

```csharp
/// <summary>
/// Um filho direto num Kit (spec da Fase 3B, secao 4.4). `Pronto`: o que aguarda coleta no ultimo passo dele.
/// `UltimoPasso` nulo: filho sem Roteiro, que nunca aguarda coleta. `JaNoDestino`: o ultimo passo e no Setor
/// onde o pai comeca (D2 da spec da Fase 3B).
/// </summary>
public sealed record FilhoDoKit(int FilhoId, decimal QuantidadePorPai, PassoDoCalculo? UltimoPasso, decimal Pronto, bool JaNoDestino);

/// <summary>`Conjuntos`: quantos conjuntos completos da para levar agora, sem passar do `Teto` (zero = incompleto).</summary>
public sealed record KitDaColeta(int PaiId, int SetorDeDestinoId, decimal Teto, decimal Conjuntos, IReadOnlyList<FilhoDoKit> Filhos)
{
  public bool Montavel => Conjuntos >= 1m;
}
```

Na classe: campo `private readonly IReadOnlySet<int> _setoresComKit;`; o construtor ganha o último parâmetro
`IReadOnlySet<int>? setoresComKit = null` e a linha `_setoresComKit = setoresComKit ?? new HashSet<int>();`. No
`<summary>` da classe, acrescente: "Recebe tambem quais Setores tem `UtilizaKit`, para a regra do Kit (spec da Fase 3B)."

Funções novas, depois de `CalcularMontabilidade`:

```csharp
  public bool SetorUtilizaKit(int setorId) => _setoresComKit.Contains(setorId);

  /// <summary>
  /// Regra 25 (D3 da spec da Fase 3B): o pai de Agrupamento Kit, com filhos, cujo PRIMEIRO passo e num Setor com
  /// `UtilizaKit` recebe os filhos so em conjuntos completos.
  /// </summary>
  public bool RecebeEmConjunto(int paiId) =>
      _nos.TryGetValue(paiId, out var pai) && pai.DeKit && TemFilhos(paiId)
      && PrimeiroPasso(paiId) is PassoDoCalculo primeiro && _setoresComKit.Contains(primeiro.SetorId);

  /// <summary>O que o filho aguarda montagem em Setores com `UtilizaKit`, menos o que esta saindo de la.</summary>
  private decimal EmEsperaNoKit(int filhoId, decimal saindo) =>
      LiquidoDo(filhoId)
          .Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem && _setoresComKit.Contains(kv.Key.SetorId!.Value))
          .Sum(kv => kv.Value) - saindo;

  /// <summary>
  /// Os conjuntos que entraram e ainda nao foram montados (regra 25), contados pelo filho mais adiantado: o maior
  /// `ceil(espera / razao)` entre os filhos diretos (D5 da spec da Fase 3B). Arredondar para cima e o que mantem
  /// contado o conjunto que perdeu parte dentro da Solda. `saindo` (filho -> quantidade) e o que um
  /// redirecionamento tira da espera de um Setor com `UtilizaKit`: ja estava contado, nao conta de novo.
  /// </summary>
  public decimal ConjuntosAEspera(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null)
  {
    var maior = 0m;
    foreach (var filho in Filhos(paiId))
    {
      var razao = Razao(filho);
      if (razao <= 0m) continue;
      var espera = EmEsperaNoKit(filho.Id, saindo?.GetValueOrDefault(filho.Id) ?? 0m);
      if (espera > 0m) maior = Math.Max(maior, Math.Ceiling(espera / razao));
    }
    return maior;
  }

  /// <summary>Quantos conjuntos o pai ainda precisa receber (regra 25): quantidade, menos o montado, menos a espera.</summary>
  public decimal TetoDeEntrada(int paiId, IReadOnlyDictionary<int, decimal>? saindo = null) =>
      Math.Max(0m, FaltaMontar(paiId) - ConjuntosAEspera(paiId, saindo));

  private PassoDoCalculo? UltimoPasso(int id) => _nos[id].Roteiro.Count == 0 ? null : _nos[id].Roteiro[^1];

  /// <summary>O filho de Kit que aguarda coleta no ULTIMO passo vai para o cartao do Kit, nao para o "Item pronto".</summary>
  private bool VaiNoKit(int id, int ordem) =>
      _nos[id].PaiId is int paiId && ProximoPasso(id, ordem) is null && RecebeEmConjunto(paiId);

  /// <summary>
  /// Os Kits que as Tarefas mostram (regra 23; spec da Fase 3B, secao 4.4): todo pai que recebe em conjunto, com
  /// teto maior que zero e algum filho pronto. `Conjuntos` = min(teto, min por filho de floor(pronto / razao)).
  /// </summary>
  public IReadOnlyList<KitDaColeta> KitsDaColeta()
  {
    var lista = new List<KitDaColeta>();
    foreach (var pai in Nos)
    {
      if (!RecebeEmConjunto(pai.Id)) continue;
      var teto = TetoDeEntrada(pai.Id);
      if (teto <= 0m) continue;
      var destino = PrimeiroPasso(pai.Id)!.Value.SetorId;
      var filhos = Filhos(pai.Id).Select(c =>
      {
        var ultimo = UltimoPasso(c.Id);
        var pronto = ultimo is PassoDoCalculo u ? Math.Max(0m, Saldo(c.Id, Local.AguardandoColeta(u.SetorId, u.Ordem))) : 0m;
        return new FilhoDoKit(c.Id, Razao(c), ultimo, pronto, ultimo?.SetorId == destino);
      }).ToList();
      if (filhos.All(f => f.Pronto <= 0m)) continue;

      var conjuntos = Math.Floor(teto);
      foreach (var f in filhos)
        conjuntos = Math.Min(conjuntos, f.QuantidadePorPai <= 0m ? 0m : Math.Floor(f.Pronto / f.QuantidadePorPai));
      lista.Add(new KitDaColeta(pai.Id, destino, teto, conjuntos, filhos));
    }
    return lista;
  }
```

Em `ColetasPendentes`, logo depois de `if (local.Posicao != Posicoes.AguardandoColeta) continue;`:

```csharp
        if (VaiNoKit(id, local.Ordem!.Value)) continue;
```

e acrescente ao `<summary>` dela: "Sem os filhos de Kit no ultimo passo, que vao no cartao do Kit (`KitsDaColeta`)."

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~KitsDaColetaTests"`
Expected: PASS. Estes testes usam `c.Calcular()`, que monta a calculadora direto dos fakes (Step 1) e não passa
pelo `LeitorDeEstado`; a fiação do leitor é o Step 4.

- [ ] **Step 4: O leitor carrega o Kit**

Em `LeitorDeEstado.CarregarAsync`, depois de `alcancados`:

```csharp
    var deKit = await _execucao.ListarNosDeKitAsync(ids, ct);
    var setoresComKit = await _execucao.ListarSetoresComKitAsync(ct);
```

o `new NoDoCalculo(...)` ganha `deKit.Contains(n.Id)` como último argumento, e o construtor da calculadora ganha
`setoresComKit` depois de `alcancados`. No `<summary>` da classe, "em cinco consultas" passa a "em sete consultas", e
acrescente "o tipo do Agrupamento de cada no e os Setores com `UtilizaKit`".

Escreva um teste em `KitsDaColetaTests` que passe pelo leitor, para provar a fiação:

```csharp
  [Fact]
  public async Task Consulta_le_o_Kit_pelo_repositorio()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);

    // A contagem conta o Kit montavel; sem a fiacao do leitor, o Kit nao existiria e o 2 e o 3 seriam Item pronto.
    Assert.Equal(0, (await c.Consulta().Tarefas(CancellationToken.None)).Valor!.SelectMany(g => g.Itens).Count());
  }
```

(Este teste vale até a Task 5, que muda a forma de `Tarefas`; a Task 5 o reescreve.)

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS, com a suíte inteira da Application verde (nenhum teste antigo é de Kit).

- [ ] **Step 5: O repositório real**

Em `ExecucaoRepository`, ao lado de `ListarTotaisMontadosAsync`:

```csharp
  /// <summary>Valor de `CK_Agrupamento_Tipo` que liga o conjunto completo (regra 25).</summary>
  private const string TipoKit = "Kit";

  public async Task<IReadOnlySet<int>> ListarNosDeKitAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    if (ids.Count == 0) return new HashSet<int>();
    var lista = ids.ToList();
    var achados = await (from e in _db.Estruturas.AsNoTracking()
                         join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                         where lista.Contains(e.Id) && a.Tipo == TipoKit
                         select e.Id).ToListAsync(ct);
    return achados.ToHashSet();
  }

  public async Task<IReadOnlySet<int>> ListarSetoresComKitAsync(CancellationToken ct) =>
      (await _db.Setores.AsNoTracking().Where(s => s.UtilizaKit).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
```

Teste em `ExecucaoRepositoryTests` (usa o `NoCenarioAsync` do arquivo):

```csharp
  [Fact]
  public Task Nos_de_Kit_e_Setores_com_UtilizaKit_vem_do_banco() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    var antes = await repo.ListarNosDeKitAsync(c.Nos, CancellationToken.None);
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Agrupamento SET Tipo = 'Kit' WHERE Id = {c.Arvore.AgrupamentoId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = {c.Solda}");
    var depois = await repo.ListarNosDeKitAsync(c.Nos, CancellationToken.None);
    var setores = await repo.ListarSetoresComKitAsync(CancellationToken.None);

    Assert.Empty(antes);
    Assert.Equal(c.Nos.OrderBy(i => i), depois.OrderBy(i => i));
    Assert.Contains(c.Solda, setores);
    Assert.DoesNotContain(c.Corte, setores);
  });
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~Nos_de_Kit_e_Setores"`
Expected: PASS.

- [ ] **Step 6: Mutações a medir (escreva no relatório o que morreu e o que não)**

1. Em `EmEsperaNoKit`, tire o filtro de `_setoresComKit` → `Espera_em_Setor_sem_UtilizaKit_nao_conta` fica vermelho.
2. Troque `Math.Ceiling` por `Math.Floor` em `ConjuntosAEspera` → `Conjunto_que_perdeu_parte_continua_a_espera` fica
   vermelho.
3. Troque `Math.Max` por `Math.Min` em `ConjuntosAEspera` → `Conjuntos_a_espera_contam_pelo_filho_mais_adiantado` fica
   vermelho.
4. Tire o `if (VaiNoKit(...)) continue;` → `Filho_de_Kit_no_ultimo_passo_sai_do_Item_pronto` fica vermelho.
5. Tire `pai.DeKit &&` de `RecebeEmConjunto` → `Filho_de_Avulso_continua_Item_pronto` fica vermelho.

Devolva cada mutação antes da próxima.

- [ ] **Step 7: Build, suíte e commit**

```bash
git add src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs \
  src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs \
  src/Rastreamento.Application/Execucao/LeitorDeEstado.cs src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs \
  tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs \
  tests/Rastreamento.Application.Tests/Execucao/KitsDaColetaTests.cs \
  tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs
git commit -m "feat(execucao): a calculadora conhece o Kit (teto, conjuntos a espera, KitsDaColeta)"
```

---

### Task 4: Conjunto completo na entrega

**Modelo:** opus. É a fronteira real da regra 25 e roda dentro da transação da entrega, com o redirecionamento
como caso de borda (D5).

**Files:**
- Create: `src/Rastreamento.Application/Execucao/ConjuntoCompleto.cs`
- Modify: `src/Rastreamento.Application/Execucao/EntregaUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/ConjuntoCompletoNaEntregaTests.cs`

**Interfaces:**
- Consumes: `RecebeEmConjunto`, `SetorUtilizaKit`, `TetoDeEntrada(paiId, saindo)`, `Filhos`, `No` (Task 3).
- Produces: `CodigosDaExecucao.ConjuntoIncompleto`, `CodigosDaExecucao.AlemDoQueOPaiPrecisa`.

- [ ] **Step 1: Testes que falham**

Crie `ConjuntoCompletoNaEntregaTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Regra 25 na entrega (spec da Fase 3B, secao 4.3). Cenario Kit: Peca 1 de 10 que comeca na Solda (UtilizaKit);
/// Item 2 de 40 (razao 4) que termina no Corte; Item 3 de 10 (razao 1) que termina na Dobra.
/// </summary>
public class ConjuntoCompletoNaEntregaTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario(bool kit = true, int setorDoPai = Solda, int ultimoDo2 = Corte)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, setorDoPai);
    c.No(2, 1, 40m, 4m, ultimoDo2);
    c.No(3, 1, 10m, 1m, Dobra);
    if (kit) c.Kit();
    return c;
  }

  private static void Pronto(CenarioDeExecucao c, int item, int setor, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);

  private static ItemDaEntregaDto DaColeta(int item, int setor, decimal quantidade) =>
      new(item, new OrigemDaEntregaDto(Posicoes.AguardandoColeta, setor, 1), null, quantidade);

  private static ItemDaEntregaDto DaMontagem(int item, int setor, decimal quantidade) =>
      new(item, new OrigemDaEntregaDto(Posicoes.AguardandoMontagem, setor, null), null, quantidade);

  private static Task<Result<IReadOnlyList<MovimentacaoDto>>> Entregar(CenarioDeExecucao c, params ItemDaEntregaDto[] itens) =>
      c.Entrega().Entregar(new EntregaDto(itens), Movimentador, Ct);

  private static void AfirmarRecusa(Result<IReadOnlyList<MovimentacaoDto>> r, string codigo, TipoDeErro tipo, CenarioDeExecucao c, int movimentosAntes)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
    Assert.Equal(movimentosAntes, c.Execucao.Movimentacoes.Count);
  }

  [Fact]
  public async Task Conjunto_completo_entra()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 2m));

    Assert.True(r.Sucesso);
    Assert.All(r.Valor!, m => Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), m.Destino));
  }

  [Fact]
  public async Task Filho_faltando_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m));

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
    Assert.Contains("No 3", r.Detalhe);
  }

  [Fact]
  public async Task Quantidade_que_nao_fecha_conjunto_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 6m), DaColeta(3, Dobra, 2m));   // 6 / 4 = 1,5

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }

  [Fact]
  public async Task Numeros_de_conjuntos_diferentes_da_ConjuntoIncompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 3m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 3m));   // 2 e 3 conjuntos

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }

  [Fact]
  public async Task Alem_do_que_o_pai_precisa_da_AlemDoQueOPaiPrecisa()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 36m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Dobra, 1), Local.AguardandoMontagem(Solda), 9m);
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(3, Dobra, 2m));   // teto 1, leva 2

    AfirmarRecusa(r, CodigosDaExecucao.AlemDoQueOPaiPrecisa, TipoDeErro.Conflito, c, antes);
    Assert.Contains("1", r.Detalhe);
  }

  [Fact]
  public async Task Lista_mista_com_item_solto_entra_inteira()
  {
    var c = Cenario();
    c.NoDoAgrupamento(AgrupamentoId2, 10, null, 5m, null, Corte, Pintura);
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);
    Pronto(c, 10, Corte, 5m);

    var r = await Entregar(c, DaColeta(2, Corte, 8m), DaColeta(10, Corte, 5m), DaColeta(3, Dobra, 2m));

    Assert.True(r.Sucesso);
    Assert.Equal(3, r.Valor!.Count);
  }

  [Fact]
  public async Task Avulso_nao_exige_conjunto()
  {
    var c = Cenario(kit: false);
    Pronto(c, 2, Corte, 8m);

    Assert.True((await Entregar(c, DaColeta(2, Corte, 8m))).Sucesso);
  }

  [Fact]
  public async Task Pai_que_comeca_em_Setor_sem_UtilizaKit_nao_exige_conjunto()
  {
    var c = Cenario(setorDoPai: Pintura);
    Pronto(c, 2, Corte, 8m);

    Assert.True((await Entregar(c, DaColeta(2, Corte, 8m))).Sucesso);
  }

  [Fact]
  public async Task Folha_de_Kit_que_vai_ao_proximo_passo_nao_e_conjunto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 40m, 4m, Corte, Solda);   // passa pela Solda no proprio Roteiro
    c.No(3, 1, 10m, 1m, Dobra);
    c.Kit();
    Pronto(c, 2, Corte, 4m);

    var r = await Entregar(c, DaColeta(2, Corte, 4m));

    Assert.True(r.Sucesso);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Solda, "Solda", 2), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Filho_que_termina_na_Solda_entra_no_conjunto_com_os_irmaos()
  {
    var c = Cenario(ultimoDo2: Solda);
    Pronto(c, 2, Solda, 4m);
    Pronto(c, 3, Dobra, 1m);

    var r = await Entregar(c, DaColeta(2, Solda, 4m), DaColeta(3, Dobra, 1m));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Redirecionamento_de_Kit_conta_a_espera_uma_vez_so()
  {
    // A Dobra tambem tem UtilizaKit e era o primeiro passo do pai; o PCP o mudou para a Solda. A Peca tem 2, e os 2
    // conjuntos que esperam na Dobra ocupam o teto inteiro (2 - 0 - 2 = 0); sem tira-los da conta, a entrega que
    // os leva a Solda seria recusada como alem do necessario.
    var c = new CenarioDeExecucao();
    c.No(1, null, 2m, null, Solda);
    c.No(2, 1, 8m, 4m, Corte);
    c.No(3, 1, 2m, 1m, Corte);
    c.Kit();
    c.Execucao.SetoresComKit.Add(Dobra);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Dobra), 8m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Dobra), 2m);

    var r = await Entregar(c, DaMontagem(2, Dobra, 8m), DaMontagem(3, Dobra, 2m));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Redirecionamento_de_Kit_incompleto_da_ConjuntoIncompleto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 2m, null, Solda);
    c.No(2, 1, 8m, 4m, Corte);
    c.No(3, 1, 2m, 1m, Corte);
    c.Kit();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 8m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 2m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await Entregar(c, DaMontagem(2, Pintura, 8m));

    AfirmarRecusa(r, CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, c, antes);
  }
}
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ConjuntoCompletoNaEntregaTests"`
Expected: FAIL de compilação (códigos novos).

- [ ] **Step 2: Os códigos**

Em `CodigosDaExecucao`:

```csharp
  public const string ConjuntoIncompleto = "ConjuntoIncompleto";
  public const string AlemDoQueOPaiPrecisa = "AlemDoQueOPaiPrecisa";
```

e no `<summary>`: "`ConjuntoIncompleto` e `AlemDoQueOPaiPrecisa` sao da regra 25 na entrega (spec da Fase 3B, secao 4.3)."

- [ ] **Step 3: `ConjuntoCompleto`**

Crie `src/Rastreamento.Application/Execucao/ConjuntoCompleto.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Regra 25 na entrega (spec da Fase 3B, secao 4.3). Os itens cujo destino e `AguardandoMontagem` de um pai que
/// recebe em conjunto (`RecebeEmConjunto`) sao agrupados pelo pai, e cada grupo tem de trazer TODOS os filhos
/// diretos, com o mesmo numero inteiro de conjuntos, sem passar do teto. Valida com as mesmas funcoes que as
/// Tarefas mostram (`TetoDeEntrada`), e roda depois de o destino de cada item estar calculado.
/// </summary>
internal static class ConjuntoCompleto
{
  public sealed record ItemDestinado(int EstruturaItemId, Local Origem, Local Destino, decimal Quantidade);

  public sealed record Recusa(string Codigo, TipoDeErro Tipo, string Mensagem);

  public static Recusa? Conferir(EstadoDeExecucao estado, IReadOnlyList<ItemDestinado> itens)
  {
    var calc = estado.Calc;
    var porPai = itens
        .Where(i => i.Destino.Posicao == Posicoes.AguardandoMontagem
                    && calc.No(i.EstruturaItemId).PaiId is int paiId && calc.RecebeEmConjunto(paiId))
        .GroupBy(i => calc.No(i.EstruturaItemId).PaiId!.Value)
        .OrderBy(g => g.Key);

    foreach (var grupo in porPai)
    {
      var paiId = grupo.Key;
      var pai = estado.Nome(paiId);
      var somas = grupo.GroupBy(i => i.EstruturaItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));

      decimal? conjuntos = null;
      foreach (var filho in calc.Filhos(paiId))
      {
        var nome = estado.Nome(filho.Id);
        if (!somas.TryGetValue(filho.Id, out var soma))
          return Incompleto($"Falta {nome} no conjunto de {pai}: um Kit só entra com todos os filhos juntos.");

        var razao = filho.QuantidadePorPai ?? 0m;
        var destes = razao > 0m ? soma / razao : 0m;
        if (destes < 1m || destes != decimal.Truncate(destes))
          return Incompleto($"{Quantidades.Formatar(soma)} de {nome} não fecha conjuntos completos de {pai}: "
              + $"cada conjunto leva {Quantidades.Formatar(razao)}.");
        if (conjuntos is decimal anteriores && anteriores != destes)
          return Incompleto($"Os filhos de {pai} não formam o mesmo número de conjuntos: {nome} dá "
              + $"{Quantidades.Formatar(destes)}, os anteriores dão {Quantidades.Formatar(anteriores)}.");
        conjuntos = destes;
      }

      var saindo = grupo
          .Where(i => i.Origem.Posicao == Posicoes.AguardandoMontagem && calc.SetorUtilizaKit(i.Origem.SetorId!.Value))
          .GroupBy(i => i.EstruturaItemId)
          .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
      var teto = Math.Floor(calc.TetoDeEntrada(paiId, saindo));
      if (conjuntos > teto)
        return new Recusa(CodigosDaExecucao.AlemDoQueOPaiPrecisa, TipoDeErro.Conflito,
            $"{pai} só precisa receber {Quantidades.Formatar(teto)} conjunto(s); a entrega leva {Quantidades.Formatar(conjuntos!.Value)}.");
    }
    return null;
  }

  private static Recusa Incompleto(string mensagem) =>
      new(CodigosDaExecucao.ConjuntoIncompleto, TipoDeErro.Validacao, mensagem);
}
```

- [ ] **Step 4: A entrega usa a conferência**

Em `EntregaUseCase.Entregar`, troque a montagem do estado por (os irmãos entram no estado, decisão P4 do plano):

```csharp
      // Os pais e os irmaos entram no estado, mas nao sao travados: a entrega so le o Roteiro dos pais e, para o
      // conjunto completo, os filhos de cada pai (spec da Fase 3B, secao 4.3). So os nos da lista sao escritos, e
      // o conjunto completo obriga a lista a trazer todos os filhos: duas entregas do mesmo Kit travam os mesmos nos.
      var paiIds = travados.Where(n => n.EstruturaPaiId is not null).Select(n => n.EstruturaPaiId!.Value).Distinct().ToList();
      var pais = await _execucao.ListarNosAsync(paiIds.Except(ids).ToList(), ct);
      var irmaos = new List<EstruturaItem>();
      foreach (var paiId in paiIds) irmaos.AddRange(await _execucao.ListarFilhosAsync(paiId, ct));
      var estado = await _leitor.CarregarAsync([.. travados, .. pais, .. irmaos], ct);
```

(Os travados vêm primeiro: `CarregarAsync` faz `DistinctBy(Id)` e fica com a primeira ocorrência, a travada.)

No laço, depois de `consumido[chave] = ...`, guarde o item com o destino:

```csharp
        destinados.Add(new ConjuntoCompleto.ItemDestinado(item.EstruturaItemId, origem, destino!.Value, item.Quantidade));
```

com `var destinados = new List<ConjuntoCompleto.ItemDestinado>();` declarado ao lado de `movimentos`. Depois do laço,
antes do `foreach (var movimento in movimentos) _execucao.Adicionar(movimento);`:

```csharp
      if (ConjuntoCompleto.Conferir(estado, destinados) is { } recusaDoConjunto)
        return Result<IReadOnlyList<MovimentacaoDto>>.Falha(recusaDoConjunto.Codigo, recusaDoConjunto.Tipo, recusaDoConjunto.Mensagem);
```

No `<summary>` da classe, troque "A lista ja serve a 3B, cujo conjunto completo exige que os filhos entrem juntos." por
"Filhos de Kit que vao a montagem num Setor com `UtilizaKit` entram em conjunto completo (`ConjuntoCompleto`)."

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ConjuntoCompletoNaEntregaTests|FullyQualifiedName~EntregaUseCaseTests"`
Expected: PASS, inclusive os testes antigos de entrega (todos Avulso).

- [ ] **Step 5: Mutações a medir**

1. Tire o `if (!somas.TryGetValue(...)) return Incompleto(...)` (trocando por `soma = 0m` não basta: apague a
   checagem e use `somas.GetValueOrDefault`) → `Filho_faltando_da_ConjuntoIncompleto` fica vermelho.
2. Tire `|| destes != decimal.Truncate(destes)` → `Quantidade_que_nao_fecha_conjunto_da_ConjuntoIncompleto` fica
   vermelho.
3. Passe `null` em vez de `saindo` para `TetoDeEntrada` → `Redirecionamento_de_Kit_conta_a_espera_uma_vez_so` fica
   vermelho.
4. Tire a chamada de `ConjuntoCompleto.Conferir` → os cinco testes de recusa ficam vermelhos.

- [ ] **Step 6: Build, suíte e commit**

```bash
git add src/Rastreamento.Application/Execucao/ConjuntoCompleto.cs src/Rastreamento.Application/Execucao/EntregaUseCase.cs \
  src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs \
  tests/Rastreamento.Application.Tests/Execucao/ConjuntoCompletoNaEntregaTests.cs
git commit -m "feat(entrega): Kit entra na Solda so em conjunto completo e sem passar do teto"
```

---

### Task 5: Leituras — Kits nas Tarefas, tipo do Agrupamento no resumo e o grupo de montagem

**Modelo:** sonnet. Projeção de DTO sobre funções já testadas na Task 3, e a ponta a ponta na API.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`ContextoDoNo`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs` (`ListarNosEmProducaoAsync`)
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs`, `ConsultaDeExecucaoUseCase.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs` (`ListarNosEmProducaoAsync`)
- Test: `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs`, `KitsDaColetaTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs`
- Create: `tests/Rastreamento.Api.Tests/KitNaApiTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`, `CriterioDeProntoDaFase3Tests.cs`
- Modify: `specs/05-api-endpoints.md`

**Interfaces:**
- Consumes: `KitsDaColeta`, `RecebeEmConjunto` (Task 3).
- Produces (C# e JSON):

```csharp
public sealed record NoResumoDto(
    int Id, string Descricao, string? CodigoDoComponente, int PedidoId, string PedidoNumero,
    string PedidoCliente, int AgrupamentoId, string AgrupamentoCodigo, string AgrupamentoTipo, int? PaiId, string? PaiDescricao,
    PausaResumoDto? Pausa, IReadOnlyList<MaterialResumoDto> Materiais);

public sealed record MontagemPendenteDto(
    NoResumoDto Pai, decimal FaltaMontar, decimal DaParaMontar, bool IniciaAqui, SetorResumoDto? PrimeiroPassoDoPai,
    IReadOnlyList<FilhoNaMontagemDto> Filhos, bool ConjuntoCompleto);

public sealed record FilhoDoKitDto(NoResumoDto No, decimal QuantidadePorPai, SetorResumoDto? Origem, int? Ordem, decimal Pronto, bool JaNoDestino);

public sealed record KitDto(NoResumoDto Pai, SetorResumoDto Destino, decimal Conjuntos, IReadOnlyList<FilhoDoKitDto> Filhos);

public sealed record TarefasDto(IReadOnlyList<TarefasDoSetorDto> Grupos, IReadOnlyList<KitDto> KitsMontaveis, IReadOnlyList<KitDto> KitsIncompletos);
```

  `GET /tarefas` → `{ grupos, kitsMontaveis, kitsIncompletos }`; `ConsultaDeExecucaoUseCase.Tarefas` →
  `Task<Result<TarefasDto>>`.

- [ ] **Step 1: Testes que falham (Application)**

Em `KitsDaColetaTests`, **reescreva** `Consulta_le_o_Kit_pelo_repositorio` (a forma de `Tarefas` muda):

```csharp
  [Fact]
  public async Task Tarefas_trazem_o_Kit_montavel_e_os_filhos_saem_dos_grupos()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);

    var tarefas = (await c.Consulta().Tarefas(CancellationToken.None)).Valor!;
    var contagem = (await c.Consulta().ContagemDeTarefas(CancellationToken.None)).Valor!;

    Assert.Empty(tarefas.Grupos);
    var kit = Assert.Single(tarefas.KitsMontaveis);
    Assert.Empty(tarefas.KitsIncompletos);
    Assert.Equal(1, kit.Pai.Id);
    Assert.Equal(new SetorResumoDto(Solda, "Solda"), kit.Destino);
    Assert.Equal(2m, kit.Conjuntos);
    Assert.Equal(new[] { (2, 8m, "Corte"), (3, 2m, "Dobra") },
        kit.Filhos.Select(f => (f.No.Id, f.Pronto, f.Origem!.Nome)));
    Assert.Equal(1, contagem.Total);
  }

  [Fact]
  public async Task Kit_incompleto_vai_para_os_incompletos_e_nao_conta()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    var tarefas = (await c.Consulta().Tarefas(CancellationToken.None)).Valor!;

    Assert.Empty(tarefas.KitsMontaveis);
    Assert.Equal(1, Assert.Single(tarefas.KitsIncompletos).Pai.Id);
    Assert.Equal(0, (await c.Consulta().ContagemDeTarefas(CancellationToken.None)).Valor!.Total);
  }

  [Fact]
  public async Task Resumo_do_no_traz_o_tipo_do_Agrupamento()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    var kit = Assert.Single((await c.Consulta().Tarefas(CancellationToken.None)).Valor!.KitsIncompletos);

    Assert.Equal("Kit", kit.Pai.AgrupamentoTipo);
  }

  [Fact]
  public async Task Grupo_de_montagem_diz_se_recebe_em_conjunto()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 4m);

    var grupo = Assert.Single((await c.Consulta().Fila(Solda, ComoPcp, CancellationToken.None)).Valor!.AguardandoMontagem);

    Assert.True(grupo.ConjuntoCompleto);
  }
```

Em `ConsultaDeExecucaoUseCaseTests`, os testes que leem `Tarefas(Ct)).Valor!` como lista passam a ler
`.Valor!.Grupos` (são os testes *"Tarefas_trazem_os_materiais_do_no"*, *"Tarefas_agrupam_por_Setor_de_origem_e_a_contagem_bate"*
e os outros que o compilador apontar). No teste parametrizado que chama `Desmontar(await c.Consulta().Tarefas(Ct))`,
o tipo muda junto; ajuste o `Desmontar` se ele for genérico no `T` do `Result`.

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~KitsDaColetaTests|FullyQualifiedName~ConsultaDeExecucaoUseCaseTests"`
Expected: FAIL de compilação.

- [ ] **Step 2: Contexto do nó e DTOs**

`ContextoDoNo` ganha `string AgrupamentoTipo` depois de `AgrupamentoCodigo`. Em `ExecucaoRepository.ListarNosEmProducaoAsync`,
o `select new { ..., a.Codigo }` passa a `select new { ..., a.Codigo, AgrupamentoTipo = a.Tipo }`, e o `new ContextoDoNo(...)`
recebe `l.AgrupamentoTipo` depois de `l.Codigo`. No `FakeExecucaoRepo.ListarNosEmProducaoAsync`, o `new ContextoDoNo(...)`
recebe, no mesmo lugar, `AgrupamentosKit.Contains(item.AgrupamentoId) ? "Kit" : "Avulso"` (use o nome da variável do
laço do fake).

Em `ExecucaoDtos.cs`: `NoResumoDto` e `MontagemPendenteDto` como na seção **Interfaces**; os records novos `FilhoDoKitDto`,
`KitDto` e `TarefasDto` ao lado de `TarefasDoSetorDto`, com estes comentários:

```csharp
/// <summary>
/// Um filho no cartao do Kit (spec da Fase 3B, secao 5.1). `Origem` e `Ordem`: o ultimo passo do filho, onde ele
/// aguarda coleta; nulos quando ele nao tem Roteiro. `JaNoDestino`: esse passo e no Setor onde o pai comeca.
/// </summary>

/// <summary>
/// Um Kit nas Tarefas (regra 23). `Conjuntos`: quantos conjuntos completos da para levar agora; zero nos incompletos.
/// A quantidade de cada filho na entrega e `Conjuntos x QuantidadePorPai`, e quem a calcula e o front.
/// </summary>

/// <summary>`GET /tarefas`: os "Item pronto" por Setor de origem, e os Kits (spec da Fase 3B, secao 4.5).</summary>
```

E no `<summary>` do `MontagemPendenteDto`, acrescente: "`ConjuntoCompleto`: o pai recebe os filhos so em conjunto
completo (regra 25); a Fila oferece levar o Kit inteiro em vez de filho a filho."

- [ ] **Step 3: A consulta**

Em `ConsultaDeExecucaoUseCase`:
- `CarregarEmProducaoAsync`: o `new NoResumoDto(...)` recebe `x.AgrupamentoTipo` depois de `x.AgrupamentoCodigo`.
- `FilaAsync`: o `new MontagemPendenteDto(...)` recebe `calc.RecebeEmConjunto(paiId)` como último argumento.
- `Tarefas` e `TarefasAsync` passam a `Result<TarefasDto>`:

```csharp
  public Task<Result<TarefasDto>> Tarefas(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => TarefasAsync(ct), ct);

  private async Task<Result<TarefasDto>> TarefasAsync(CancellationToken ct)
  {
    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;

    var grupos = calc.ColetasPendentes()
        .GroupBy(p => p.SetorId)
        .OrderBy(g => g.Key)
        .Select(g => new TarefasDoSetorDto(g.Key, nomes.GetValueOrDefault(g.Key, string.Empty),
            g.Select(p => new TarefaDto(resumos[p.EstruturaItemId], p.Ordem, p.Tarefa,
                Destino(calc.DestinoDaColeta(p.EstruturaItemId, p.Ordem), nomes))).ToList()))
        .ToList();

    SetorResumoDto Setor(int id) => new(id, nomes.GetValueOrDefault(id, string.Empty));
    var kits = calc.KitsDaColeta().Select(k => new KitDto(
        resumos[k.PaiId], Setor(k.SetorDeDestinoId), k.Conjuntos,
        k.Filhos.Select(f => new FilhoDoKitDto(
            resumos[f.FilhoId], f.QuantidadePorPai,
            f.UltimoPasso is PassoDoCalculo u ? Setor(u.SetorId) : null,
            f.UltimoPasso?.Ordem, f.Pronto, f.JaNoDestino)).ToList())).ToList();

    return Result<TarefasDto>.Ok(new TarefasDto(
        grupos, kits.Where(k => k.Conjuntos >= 1m).ToList(), kits.Where(k => k.Conjuntos < 1m).ToList()));
  }
```

- `ContagemDeTarefas`: `estado.Calc.ColetasPendentes().Count + estado.Calc.KitsDaColeta().Count(k => k.Montavel)`, e
  no `<summary>` dela: "Cada Kit montavel conta como uma tarefa; os incompletos nao contam (spec da Fase 3B, secao 4.5)."

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS.

- [ ] **Step 4: API — cenário Kit e testes**

Em `CenarioDaFase3NaApi.CriarAsync`, acrescente o parâmetro `bool kit = false`: com ele, o Setor "Solda" nasce com
`UtilizaKit = kit` (no `new Setor { ... }` da lista, `UtilizaKit = kit && n == "Solda"`), e o Agrupamento é criado com
`tipo = kit ? "Kit" : "Avulso"`. Atualize o `<summary>` da classe ("Um Pedido Avulso, ou Kit com a Solda marcada, ...").

Nos dois helpers que leem `/api/tarefas` como lista (o de `ExecucaoEndpointsTests.Comportamento.cs` e o de
`CriterioDeProntoDaFase3Tests.cs`), `tarefas.EnumerateArray()` passa a `tarefas.GetProperty("grupos").EnumerateArray()`.

Crie `KitNaApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O Kit ponta a ponta (spec da Fase 3B): A (10; Solda -> Pintura), B (20, razao 2; Corte), C (10, razao 1; Dobra),
/// num Agrupamento Kit com a Solda marcada. Toda asercao escopada no proprio cenario.
/// </summary>
public class KitNaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public KitNaApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task ProntoAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
  }

  private static object DaColeta(int no, int setor, decimal quantidade) =>
      new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade };

  private static async Task<JsonElement> TarefasAsync(CenarioDaFase3NaApi c) =>
      await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));

  private static JsonElement? KitDe(JsonElement tarefas, string secao, int paiId) =>
      tarefas.GetProperty(secao).EnumerateArray()
          .Where(k => k.GetProperty("pai").GetProperty("id").GetInt32() == paiId)
          .Select(k => (JsonElement?)k).SingleOrDefault();

  [Fact]
  public async Task Entrega_de_Kit_sem_todos_os_filhos_da_400_ConjuntoIncompleto()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 2m);

    var resposta = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new { itens = new[] { DaColeta(c.B, c.Corte, 4m) } });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("ConjuntoIncompleto", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Kit_montavel_aparece_e_some_quando_levado()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 2m);

    var antes = await TarefasAsync(c);
    var entrega = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas",
        new { itens = new[] { DaColeta(c.B, c.Corte, 4m), DaColeta(c.C, c.Dobra, 2m) } });
    var depois = await TarefasAsync(c);

    var kit = KitDe(antes, "kitsMontaveis", c.A);
    Assert.NotNull(kit);
    Assert.Equal(2m, kit!.Value.GetProperty("conjuntos").GetDecimal());
    Assert.Equal(c.Solda, kit.Value.GetProperty("destino").GetProperty("id").GetInt32());
    Assert.DoesNotContain(antes.GetProperty("grupos").EnumerateArray().SelectMany(g => g.GetProperty("itens").EnumerateArray()),
        i => i.GetProperty("no").GetProperty("id").GetInt32() is var id && (id == c.B || id == c.C));
    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    Assert.Null(KitDe(depois, "kitsMontaveis", c.A));
  }

  [Fact]
  public async Task Kit_sem_conjunto_fechado_aparece_nos_incompletos()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);

    var tarefas = await TarefasAsync(c);

    Assert.Null(KitDe(tarefas, "kitsMontaveis", c.A));
    Assert.NotNull(KitDe(tarefas, "kitsIncompletos", c.A));
  }

  [Fact]
  public async Task Fila_traz_o_tipo_do_Agrupamento_no_resumo()
  {
    await using var c = await CriarAsync(_factory, kit: true);

    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Corte}/fila"));

    var linha = fila.GetProperty("aIniciar").EnumerateArray().Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    Assert.Equal("Kit", linha.GetProperty("no").GetProperty("agrupamentoTipo").GetString());
  }
}
```

(Confira o nome do campo do construtor da fixture e o `IClassFixture` usado por `ExecucaoEndpointsTests`; se o arquivo
usa outra fixture, use a mesma.)

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~KitNaApiTests|FullyQualifiedName~ExecucaoEndpointsTests|FullyQualifiedName~CriterioDeProntoDaFase3Tests"`
Expected: PASS.

- [ ] **Step 5: Documento e commit**

`specs/05-api-endpoints.md`: a nova forma de `GET /tarefas` (`{ grupos, kitsMontaveis, kitsIncompletos }`, com os campos de
`KitDto` e `FilhoDoKitDto`); `GET /tarefas/contagem` somando cada Kit montável como uma tarefa; `agrupamentoTipo` no
resumo de nó da Fila e das Tarefas; `conjuntoCompleto` no grupo de `aguardandoMontagem` da Fila; e, em `POST /entregas`,
`ConjuntoIncompleto` (400) e `AlemDoQueOPaiPrecisa` (409), com uma frase cada sobre a regra 25.

Run: build + suíte backend `-m:1`. **O front quebra em tempo de execução a partir daqui** (a `TarefasPage` lê a lista
antiga) até a Task 9; a suíte do front continua verde, porque ela usa mocks. Registre no relatório.

```bash
git add src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs \
  src/Rastreamento.Application/Execucao/ExecucaoDtos.cs src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs \
  tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs \
  tests/Rastreamento.Application.Tests/Execucao/KitsDaColetaTests.cs tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs \
  tests/Rastreamento.Api.Tests/KitNaApiTests.cs tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs \
  tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs specs/05-api-endpoints.md
git commit -m "feat(tarefas): Kits montaveis e incompletos, tipo do Agrupamento no resumo"
```

---

### Task 6: C2 — o Pedido no título do Agrupamento

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`AgrupamentoDto`), `CadastroDeAgrupamentoUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeAgrupamentoUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/AgrupamentosEndpointsTests.cs`
- Modify: `web/src/api/cadastros.ts` (`AgrupamentoDto`), `web/src/components/Pagina.tsx`, `web/src/pages/AgrupamentoDetalhePage.tsx`
- Test: `web/src/components/Pagina.test.tsx` (se existir; senão, crie), `web/src/pages/AgrupamentoDetalhePage.test.tsx`
- Modify: `specs/05-api-endpoints.md` (Agrupamento)

**Interfaces:**
- Produces: `AgrupamentoDto(int Id, int PedidoId, string PedidoNumero, string Codigo, string Tipo, DateTime CriadoEm, int CriadoPorUsuarioId)`;
  JSON `pedidoNumero`; `Pagina` com `titulo: ReactNode`.

- [ ] **Step 1: Backend, teste primeiro**

Em `CadastroDeAgrupamentoUseCaseTests`, um teste para `Obter` e um para `ListarPorPedido` afirmando
`PedidoNumero` igual ao `Numero` do Pedido do fake (use o arranjo de Pedido que o arquivo já tem). Rode e veja falhar.

Mude o record para `AgrupamentoDto(int Id, int PedidoId, string PedidoNumero, string Codigo, string Tipo, DateTime CriadoEm, int CriadoPorUsuarioId)`
e o `Projetar` para `Projetar(Agrupamento agrupamento, string pedidoNumero)`. Em cada chamador: `Cadastrar` já tem o
Pedido carregado; `Editar` e `Obter` leem `await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct)` (o Pedido existe
pela FK; se o método do repositório tiver outro nome, use o dele); `ListarPorPedido` lê o Pedido uma vez e usa o número
em todos. Comentário no `Projetar`: "`PedidoNumero` serve ao titulo da pagina do Agrupamento (C2 da spec da Fase 3B);
vai em toda resposta porque o DTO e um so (decisao P8 do plano da Fase 3B)."

Teste da API em `AgrupamentosEndpointsTests`: o `GET /api/agrupamentos/{id}` de um Agrupamento criado no teste traz
`pedidoNumero` igual ao número do Pedido criado no teste.

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CadastroDeAgrupamento"` e
`dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~AgrupamentosEndpointsTests"`
Expected: PASS.

- [ ] **Step 2: `Pagina` aceita o link**

Em `Pagina.tsx`, `titulo: string` vira `titulo: ReactNode`, com o comentário: "Texto, ou texto com link (o Pedido no
título do Agrupamento, C2 da spec da Fase 3B). O `<h1>` é um só; quem passa o link não escreve outro cabeçalho."

Teste (no `Pagina.test.tsx` existente, ou crie com o cabeçalho padrão de teste de tela):

```tsx
  it('aceita um título com link dentro do h1', () => {
    render(<MemoryRouter><Pagina titulo={<><Link to="/pedidos/4">Pedido P-1</Link> — AG-01</>}>conteúdo</Pagina></MemoryRouter>)

    const h1 = screen.getByRole('heading', { level: 1 })
    expect(within(h1).getByRole('link', { name: 'Pedido P-1' }).getAttribute('href')).toBe('/pedidos/4')
  })
```

- [ ] **Step 3: O título do Agrupamento**

Em `web/src/api/cadastros.ts`, `AgrupamentoDto` ganha `pedidoNumero: string` (comentário: "Número do Pedido, para o
título da página do Agrupamento.").

Em `AgrupamentoDetalhePage.tsx`, troque a constante `titulo` por:

```tsx
  // C2 da spec da Fase 3B: o Pedido no título, com link, para quem altera o Agrupamento ver de qual Pedido ele é.
  const titulo = agrupamento
    ? (
      <>
        <Link to={`/pedidos/${agrupamento.pedidoId}`} className="text-acao underline underline-offset-4 hover:text-acao-forte">
          {`Pedido ${agrupamento.pedidoNumero}`}
        </Link>
        {` — ${agrupamento.codigo} — ${agrupamento.tipo}`}
      </>
    )
    : `Agrupamento ${agrupamentoId}`
```

(com `import { Link } from 'react-router-dom'`, se ainda não houver; e se o arquivo já tiver um estilo de link de
texto em outra tela, use o mesmo.)

Teste em `AgrupamentoDetalhePage.test.tsx`, com o `respostaAgrupamento` do arquivo levando `pedidoNumero`:

```tsx
  it('o título traz o Pedido com link, o código e o tipo', async () => {
    vi.stubGlobal('fetch', montarFetch({ estruturaInicial: [PECA] }))

    renderizarDetalhe()
    await screen.findByText('Chassi')

    const h1 = screen.getByRole('heading', { level: 1 })
    const link = within(h1).getByRole('link', { name: /^Pedido / })
    expect(link.getAttribute('href')).toMatch(/^\/pedidos\/\d+$/)
    expect(h1.textContent).toMatch(/^Pedido .+ — .+ — (Kit|Avulso)$/)
  })
```

Run: `cd web && npm test -- --run` e `npm run build`. O build aponta todo literal de `AgrupamentoDto` nos testes sem
`pedidoNumero`: acrescente o campo em cada um (valores de massa, como `'PED-2026-01'`).
Expected: verde.

- [ ] **Step 4: Documento e commit**

`specs/05-api-endpoints.md`: `pedidoNumero` na resposta de Agrupamento.

```bash
git add src/Rastreamento.Application/Cadastros tests/Rastreamento.Application.Tests/Cadastros/CadastroDeAgrupamentoUseCaseTests.cs \
  tests/Rastreamento.Api.Tests/AgrupamentosEndpointsTests.cs web/src specs/05-api-endpoints.md
git commit -m "feat(agrupamento): Pedido com link no titulo da pagina (C2)"
```

(Antes do `git add web/src`, rode `git status` e confirme que só entraram os arquivos desta task.)

---

### Task 7: Setores — a caixa "Utiliza Kit"

**Modelo:** sonnet. Molde: a "Atividade" da 3D na mesma tela.

**Files:**
- Modify: `web/src/api/cadastros.ts` (`SetorDto`, `NovoSetor`)
- Modify: `web/src/pages/SetoresPage.tsx`
- Test: `web/src/pages/SetoresPage.test.tsx`, `web/src/api/cadastros.test.ts`

**Interfaces:**
- Consumes: `utilizaKit` em `GET/POST/PUT /setores` (Task 1).
- Produces: `SetorDto.utilizaKit: boolean`, `NovoSetor.utilizaKit: boolean`.

- [ ] **Step 1: Testes que falham**

Em `SetoresPage.test.tsx`:
- *"cadastrar com Utiliza Kit marcado manda utilizaKit verdadeiro"*: abre "Novo setor", preenche o nome, marca
  `getByRole('checkbox', { name: 'Utiliza Kit' })`, salva, e o corpo do `POST /api/setores` tem `utilizaKit: true`.
- *"editar abre com a caixa no valor do setor"*: um setor com `utilizaKit: true` na lista; "Editar" abre o painel com a
  caixa marcada; desmarcar e salvar manda `utilizaKit: false` no `PUT`.
- *"setor com Utiliza Kit mostra a pílula neutra na lista"*: `getByText('Utiliza Kit')` dentro do item do setor, com as
  classes `bg-acao-fundo` e `text-acao` (token a token, `className.split(/\s+/)`, como `Pilula.test.tsx` faz).

Escreva os três no estilo dos testes de "Atividade" do mesmo arquivo (mesmo `montarFetch`, mesma forma de achar o
corpo do `POST`/`PUT`). Rode e veja falhar.

- [ ] **Step 2: Implementação**

- `cadastros.ts`: `SetorDto.utilizaKit: boolean` ("Marca o Setor onde o Kit é montado (regra 25)."); `NovoSetor.utilizaKit: boolean`.
- `SetoresPage.tsx`: estado `const [utilizaKit, setUtilizaKit] = useState(false)`, zerado onde `setAtividade('')` é
  chamado e preenchido com `setor.utilizaKit` onde `setAtividade(setor.atividade ?? '')` é chamado; o corpo passa a
  `{ nome, atividade: ..., utilizaKit }`; no painel, depois do `Campo` da atividade e fora do grid:

```tsx
          <label className="flex items-center gap-2 text-sm text-tinta">
            <input
              type="checkbox"
              checked={utilizaKit}
              onChange={(e) => setUtilizaKit(e.target.checked)}
              className="size-4 accent-acao"
            />
            Utiliza Kit
          </label>
          <p className="text-xs text-tinta-fraca">
            Marque o Setor onde o Kit é montado: lá, os filhos de um Kit só entram em conjunto completo.
          </p>
```

  e, no item da lista, depois da atividade: `{s.utilizaKit && <span className="ml-2"><Pilula>Utiliza Kit</Pilula></span>}`
  (importe `Pilula`).

- O build aponta os literais de `SetorDto` nos testes sem `utilizaKit`: acrescente `utilizaKit: false` em cada um.

Run: `cd web && npm test -- --run` e `npm run build`. Expected: verde.

- [ ] **Step 3: Commit**

```bash
git add web/src
git commit -m "feat(setores): caixa e pilula Utiliza Kit"
```

(Confira com `git status` que só entraram arquivos desta task.)

---

### Task 8: Fila do Setor — pílula "Kit" e levar o Kit inteiro

**Modelo:** sonnet. A regra está pronta no servidor; aqui é a tela, um token e uma função pequena de conjuntos.

**Files:**
- Modify: `web/src/index.css`, `web/src/tema/contraste.test.ts`
- Modify: `web/src/components/Pilula.tsx`, `Pilula.test.tsx`
- Create: `web/src/execucao/conjuntos.ts`, `conjuntos.test.ts`
- Modify: `web/src/execucao/FormularioDeQuantidade.tsx`, `FormularioDeQuantidade.test.tsx`
- Modify: `web/src/api/execucao.ts` (`NoResumoDto`, `GrupoAguardandoMontagem`), `web/src/testes/execucao.ts`
- Modify: `web/src/pages/FilaDoSetorPage.tsx`, `FilaDoSetorPage.test.tsx`

**Interfaces:**
- Consumes: `agrupamentoTipo`, `conjuntoCompleto` (Task 5).
- Produces (usados pela Task 9):

```ts
// web/src/execucao/conjuntos.ts
export function lerConjuntos(texto: string, maximo: number): LeituraDeQuantidade
export function quantidadeDoConjunto(conjuntos: number, quantidadePorPai: number): number
export function conjuntosPresentes(filhos: { presente: number; quantidadePorPai: number }[]): number
```

  `FormularioDeQuantidade` com `campo?: string` (padrão `'Quantidade'`) e `inteiro?: boolean`; `TomDePilula` com `'kit'`.

- [ ] **Step 1: Token, contraste e pílula**

`index.css`, depois do par de atraso, num bloco próprio:

```css
  /* Identidade de categoria — NUNCA estado. O Kit (spec da Fase 3B, D8): preenchimento cheio com texto branco,
     6,70:1, para se separar das pílulas de estado, que são tingidas. Só na Fila do Setor. */
  --color-kit: #1D4ED8;
```

`contraste.test.ts`: em `PARES`, `{ frente: 'superficie', fundo: 'kit', minimo: TEXTO, onde: 'texto branco da pílula de Kit' }`;
na lista de *"declara todos os tokens que o plano da fase fixou"*, `'kit'`.

`Pilula.tsx`: `TomDePilula` ganha `'kit'`; `POR_TOM.kit = 'bg-kit text-superficie'`; no comentário da função, acrescente:
"O tom `kit` é identidade de categoria (o nó é de um Kit), não estado: cheio, para não se confundir com os quatro
estados, que são tingidos."

`Pilula.test.tsx`:

```tsx
  it('o tom kit é azul cheio com texto branco, sem cor de estado nem a tinta de ação', () => {
    render(<Pilula tom="kit">Kit</Pilula>)

    const classes = screen.getByText('Kit').className.split(/\s+/)
    expect(classes).toContain('bg-kit')
    expect(classes).toContain('text-superficie')
    expect(classes.some((c) => /positivo|negativo|atencao|atraso|acao/.test(c))).toBe(false)
  })
```

- [ ] **Step 2: `conjuntos.ts`**

Teste primeiro (`conjuntos.test.ts`, `// @vitest-environment` não é preciso: é função pura):

```ts
import { describe, it, expect } from 'vitest'
import { conjuntosPresentes, lerConjuntos, quantidadeDoConjunto } from './conjuntos'

describe('lerConjuntos', () => {
  it('aceita inteiro entre 1 e o máximo', () => {
    expect(lerConjuntos('2', 3)).toEqual({ valor: 2, erro: null })
  })
  it.each([['0'], ['1,5'], ['abc'], ['']])('recusa %s', (texto) => {
    expect(lerConjuntos(texto, 3).valor).toBeNull()
  })
  it('recusa acima do máximo', () => {
    expect(lerConjuntos('4', 3)).toEqual({ valor: null, erro: 'No máximo 3.' })
  })
})

describe('quantidadeDoConjunto', () => {
  it('multiplica e arredonda a quatro casas', () => {
    expect(quantidadeDoConjunto(3, 0.1)).toBe(0.3)
    expect(quantidadeDoConjunto(2, 4)).toBe(8)
  })
})

describe('conjuntosPresentes', () => {
  it('é o menor número de conjuntos entre os filhos', () => {
    expect(conjuntosPresentes([{ presente: 8, quantidadePorPai: 4 }, { presente: 3, quantidadePorPai: 1 }])).toBe(2)
  })
  it('é zero quando um filho não tem nada', () => {
    expect(conjuntosPresentes([{ presente: 8, quantidadePorPai: 4 }, { presente: 0, quantidadePorPai: 1 }])).toBe(0)
  })
})
```

Implementação:

```ts
import type { LeituraDeQuantidade } from './quantidade'

const INTEIRO = /^\d+$/

/**
 * Lê o número de conjuntos de um Kit: inteiro, de 1 ao máximo. Um Kit só se move em conjunto completo (regra 25),
 * então meio conjunto não existe — recusado aqui, antes da rede, como o servidor recusaria com `ConjuntoIncompleto`.
 */
export function lerConjuntos(texto: string, maximo: number): LeituraDeQuantidade {
  const t = texto.trim()
  if (!INTEIRO.test(t)) return { valor: null, erro: 'Digite um número inteiro de conjuntos.' }
  const valor = Number(t)
  if (valor < 1) return { valor: null, erro: 'Leve pelo menos um conjunto.' }
  if (valor > maximo) return { valor: null, erro: `No máximo ${maximo}.` }
  return { valor, erro: null }
}

/**
 * Quanto de um filho vai em N conjuntos: N × razão, arredondado a quatro casas (a coluna é DECIMAL(18,4)). Sem o
 * arredondamento, 3 × 0,1 iria como 0,30000000000000004, que o servidor não reconheceria como três conjuntos
 * (decisão P7 do plano da Fase 3B).
 */
export function quantidadeDoConjunto(conjuntos: number, quantidadePorPai: number): number {
  return Math.round(conjuntos * quantidadePorPai * 10000) / 10000
}

/** Quantos conjuntos completos os filhos presentes formam: o menor, entre eles, de ⌊presente ÷ razão⌋. */
export function conjuntosPresentes(filhos: { presente: number; quantidadePorPai: number }[]): number {
  if (filhos.length === 0) return 0
  return Math.min(...filhos.map((f) => (f.quantidadePorPai > 0 ? Math.floor(f.presente / f.quantidadePorPai) : 0)))
}
```

- [ ] **Step 3: `FormularioDeQuantidade` em modo inteiro**

Props novas: `campo?: string` ("Rótulo do campo; padrão 'Quantidade'.") e `inteiro?: boolean` ("Conjuntos de Kit: só
inteiros, lidos por `lerConjuntos`."). A leitura passa a `const leitura = inteiro ? lerConjuntos(texto, maximo) : lerQuantidade(texto, maximo)`,
o estado inicial a `inteiro ? String(maximo) : quantidadeParaCampo(maximo)`, e o `Campo` recebe `rotulo={campo}`; com
`inteiro`, o `inputMode` é `numeric`. Teste novo em `FormularioDeQuantidade.test.tsx`: com `inteiro` e `campo="Conjuntos"`,
o campo se chama "Conjuntos", nasce com o máximo, e "1,5" desabilita o confirmar.

- [ ] **Step 4: Tipos, massa de teste e a Fila**

`api/execucao.ts`: `NoResumoDto.agrupamentoTipo: 'Kit' | 'Avulso'`; `GrupoAguardandoMontagem.conjuntoCompleto: boolean`
("O pai recebe os filhos só em conjunto completo (regra 25): a Fila leva o Kit inteiro, não filho a filho.").
`testes/execucao.ts`: `no()` com `agrupamentoTipo: 'Avulso'` no padrão. O build aponta os grupos de montagem montados à
mão nos testes sem `conjuntoCompleto`: acrescente `conjuntoCompleto: false`.

`FilaDoSetorPage.tsx`:
- `CabecalhoDoNo`: depois do nome, `{no.agrupamentoTipo === 'Kit' && <Pilula tom="kit">Kit</Pilula>}`, antes da pílula de pausa.
- Chave nova `const chaveDeLevarKit = (paiId: number) => \`levar-kit:${paiId}\``; em `chavesDaFila`, dentro do
  `if (!g.iniciaAqui && g.primeiroPassoDoPai !== null)`, quando `g.conjuntoCompleto`, acrescente
  `chaveDeLevarKit(g.pai.id)` se `conjuntosPresentes(g.filhos) >= 1`, **em vez** das chaves por filho.
- No cartão de montagem: com `g.conjuntoCompleto && !g.iniciaAqui && levarPara`, o `acaoDoFilho` devolve `undefined`
  para todos os filhos, e a `acao` do `ItemComAcao` ganha
  `podeEntregar && conjuntosPresentes(g.filhos) >= 1 ? botao(chaveDeLevarKit(g.pai.id), \`Levar o Kit para ${levarPara.nome}\`, g.pai) : undefined`;
  o `painel` ganha, na cadeia de `??`:

```tsx
                  ?? (g.conjuntoCompleto && levarPara && painel(chaveDeLevarKit(g.pai.id), (
                    <FormularioDeQuantidade
                      rotulo="Levar"
                      campo="Conjuntos"
                      inteiro
                      maximo={conjuntosPresentes(g.filhos)}
                      aoConfirmar={(n) => registrar(() => entregar(g.filhos.map((f) => ({
                        estruturaItemId: f.no.id,
                        origem: { posicao: 'AguardandoMontagem', setorId, ordem: null },
                        quantidade: quantidadeDoConjunto(n, f.quantidadePorPai),
                      }))))}
                      aoCancelar={fechar}
                    />
                  )))
```

  Comentário acima: "Kit que começa num Setor com `UtilizaKit`: o redirecionamento leva conjuntos inteiros, todos os
  filhos juntos (regra 25; spec da Fase 3B, secao 5.2)."

Testes em `FilaDoSetorPage.test.tsx`:
- *"nó de Kit mostra a pílula Kit"*: uma linha `aIniciar` com `no({ agrupamentoTipo: 'Kit' })` → `getByText('Kit')` com
  `bg-kit`; e uma Avulso sem a pílula.
- *"grupo de Kit fora do primeiro passo leva o Kit inteiro"*: grupo com `conjuntoCompleto: true`, `iniciaAqui: false`,
  `primeiroPassoDoPai: { id: 4, nome: 'Solda' }`, filhos `presente` 8 (razão 4) e 3 (razão 1); não há botão "Levar
  para Solda" por filho; o botão "Levar o Kit para Solda" abre o campo "Conjuntos" com 2; confirmar 2 manda um
  `POST /api/entregas` com os dois filhos, 8 e 2, origem `AguardandoMontagem` do Setor da fila.
- *"grupo de Kit sem conjunto completo presente não oferece levar"*: filho com `presente` 0 → sem o botão.

Run: `cd web && npm test -- --run` e `npm run build`. Expected: verde.

- [ ] **Step 5: Mutação e commit**

Mutação: troque `'bg-kit text-superficie'` por `'bg-acao-fundo text-acao'` → o teste do tom `kit` fica vermelho;
apague o par `kit` de `PARES` → o teste *"não deixa entrar tom novo sem medição"* fica vermelho. Devolva.

```bash
git add web/src
git commit -m "feat(fila): pilula Kit e levar o Kit inteiro no redirecionamento"
```

---

### Task 9: Tarefas — "Kits montáveis" e "Kits incompletos"

**Modelo:** opus. A seleção da tela passa a ter duas espécies de item (o item solto, com quantidade, e o Kit, com
conjuntos) na mesma entrega, no mesmo "Marcar todos", no mesmo aviso de "saiu da lista" e no mesmo filtro — a parte
desta tela que já deu Important em review (a seleção que sobrevive à recarga).

**Files:**
- Modify: `web/src/api/execucao.ts` (`TarefasDto`, `KitDto`, `FilhoDoKitDto`, `listarTarefas`), `web/src/api/erros.ts`
- Create: `web/src/components/SecaoRecolhivel.tsx`, `SecaoRecolhivel.test.tsx`
- Create: `web/src/execucao/CartaoDeKit.tsx`, `CartaoDeKit.test.tsx`
- Modify: `web/src/execucao/filtroDaDemanda.ts`, `filtroDaDemanda.test.ts`
- Modify: `web/src/pages/TarefasPage.tsx`, `TarefasPage.test.tsx`
- Modify: `web/src/api/execucao.test.ts`

**Interfaces:**
- Consumes: `GET /tarefas` novo (Task 5); `lerConjuntos`, `quantidadeDoConjunto` (Task 8).
- Produces:

```ts
export interface FilhoDoKitDto {
  no: NoResumoDto
  quantidadePorPai: number
  origem: SetorResumidoDto | null
  ordem: number | null
  pronto: number
  jaNoDestino: boolean
}
export interface KitDto { pai: NoResumoDto; destino: SetorResumidoDto; conjuntos: number; filhos: FilhoDoKitDto[] }
export interface TarefasDto { grupos: TarefasDoSetorDto[]; kitsMontaveis: KitDto[]; kitsIncompletos: KitDto[] }
export function listarTarefas(): Promise<TarefasDto>
```

- [ ] **Step 1: API e códigos de erro**

Tipos acima em `api/execucao.ts` (comentários curtos: "Um Kit nas Tarefas (regra 23). `conjuntos` é zero nos
incompletos."). `listarTarefas` passa a `ler<TarefasDto>('/tarefas', 'carregar as tarefas')`. Em `api/erros.ts`,
`CodigoDeErroDaExecucao` ganha `'ConjuntoIncompleto' | 'AlemDoQueOPaiPrecisa'`, e `TRADUCAO_DOS_CODIGOS`:

```ts
  ConjuntoIncompleto: 'Um Kit só entra na Solda com todos os filhos juntos, em conjuntos completos.',
  AlemDoQueOPaiPrecisa: 'O Kit não precisa de tantos conjuntos. Atualize a tela e tente de novo.',
```

`api/execucao.test.ts`: o teste de `listarTarefas` passa a devolver e afirmar o objeto.

- [ ] **Step 2: `SecaoRecolhivel`**

Teste primeiro:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { SecaoRecolhivel } from './SecaoRecolhivel'

afterEach(cleanup)

describe('SecaoRecolhivel', () => {
  it('nasce recolhida, com a contagem no título', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={23}><p>dentro</p></SecaoRecolhivel>)

    const resumo = screen.getByText('Kits incompletos (23)')
    expect(resumo.closest('details')?.open).toBe(false)
  })

  it('abre ao clicar no título', () => {
    render(<SecaoRecolhivel titulo="Kits incompletos" contagem={1}><p>dentro</p></SecaoRecolhivel>)

    fireEvent.click(screen.getByText('Kits incompletos (1)'))

    expect(screen.getByText('Kits incompletos (1)').closest('details')?.open).toBe(true)
  })
})
```

Implementação:

```tsx
import type { ReactNode } from 'react'

interface Props {
  titulo: string
  contagem: number
  children: ReactNode
}

/**
 * Seção informativa que nasce recolhida, com a contagem no título: "Kits incompletos (23)". Existe para a lista
 * que pode crescer muito e não pede ação (spec da Fase 3B, D7). `<details>` nativo: abre no teclado e no leitor de
 * tela sem código. Não é para seção com ação — o que se faz não fica escondido.
 */
export function SecaoRecolhivel({ titulo, contagem, children }: Props) {
  return (
    <details className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
      <summary className="cursor-pointer text-lg font-medium text-tinta">{`${titulo} (${contagem})`}</summary>
      <div className="mt-3 flex flex-col gap-3">{children}</div>
    </details>
  )
}
```

(Se `rounded-lg`/`p-4` destoarem das seções vizinhas, use as classes da `ListaDeCadastro`.)

- [ ] **Step 3: `CartaoDeKit`**

`web/src/execucao/CartaoDeKit.tsx` exporta dois componentes. Código:

```tsx
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { ItemComAcao } from '../components/ItemComAcao'
import { Pilula } from '../components/Pilula'
import type { KitDto } from '../api/execucao'
import { caminhoDoNo, formatarQuantidade, rotuloDoNo } from './formatacao'
import { lerConjuntos, quantidadeDoConjunto } from './conjuntos'

/** A origem de um filho no cartão: onde ele aguarda coleta, ou "já está" no Setor do pai (D2 da spec da Fase 3B). */
function origemDoFilho(f: KitDto['filhos'][number], destino: string): string {
  if (f.jaNoDestino) return `já está em ${destino}`
  if (f.origem) return `de ${f.origem.nome}, passo ${f.ordem}`
  return 'sem Roteiro'
}

/**
 * Um Kit montável nas Tarefas (regra 23; spec da Fase 3B, secao 5.1). O Movimentador escolhe QUANTOS conjuntos,
 * nunca quanto de cada filho: a tela não consegue compor um conjunto incompleto. `conjuntos` é o texto do campo,
 * `undefined` quando o Kit não está marcado.
 */
export function CartaoDeKitMontavel({ kit, podeEntregar, conjuntos, aoAlternar, aoMudar }: {
  kit: KitDto
  podeEntregar: boolean
  conjuntos: string | undefined
  aoAlternar: (marcado: boolean) => void
  aoMudar: (texto: string) => void
}) {
  const leitura = conjuntos === undefined ? null : lerConjuntos(conjuntos, kit.conjuntos)
  const n = leitura?.valor ?? kit.conjuntos
  return (
    <ItemComAcao
      acao={podeEntregar && (
        <label className="flex items-center gap-2 text-sm text-tinta">
          <input
            type="checkbox"
            checked={conjuntos !== undefined}
            onChange={(e) => aoAlternar(e.target.checked)}
            aria-label={`Levar o Kit ${rotuloDoNo(kit.pai)}`}
            className="size-5 accent-acao"
          />
          Levar
        </label>
      )}
      painel={conjuntos !== undefined && (
        <div className="flex flex-col gap-3 border-t border-borda pt-3">
          <Campo rotulo="Conjuntos" dica={leitura?.erro ?? `Dá para levar: ${formatarQuantidade(kit.conjuntos)}`}>
            {(id, idDaDica) => (
              <input
                id={id}
                type="text"
                inputMode="numeric"
                autoComplete="off"
                value={conjuntos}
                onChange={(e) => aoMudar(e.target.value)}
                aria-describedby={idDaDica}
                aria-invalid={leitura?.erro != null}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
      )}
    >
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        {rotuloDoNo(kit.pai)}
        {kit.pai.pausa && <Pilula tom="atencao">Pausado</Pilula>}
      </span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(kit.pai)}</span>
      <span className="text-sm text-tinta">{`Destino: ${kit.destino.nome} (início de ${kit.pai.descricao})`}</span>
      <span className="text-sm text-tinta">{`Dá para levar ${formatarQuantidade(kit.conjuntos)} conjunto(s)`}</span>
      <ul aria-label={`Filhos do Kit ${kit.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {kit.filhos.map((f) => (
          <li key={f.no.id}>
            {`${rotuloDoNo(f.no)}: ${formatarQuantidade(quantidadeDoConjunto(n, f.quantidadePorPai))} (${origemDoFilho(f, kit.destino.nome)})`}
          </li>
        ))}
      </ul>
    </ItemComAcao>
  )
}

/** Um Kit que ainda não fecha um conjunto: informativo, sem caixa de marcar (spec da Fase 3B, D7). */
export function CartaoDeKitIncompleto({ kit }: { kit: KitDto }) {
  return (
    <ItemComAcao>
      <span className="font-medium text-tinta">{rotuloDoNo(kit.pai)}</span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(kit.pai)}</span>
      <ul aria-label={`Filhos do Kit ${kit.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {kit.filhos.map((f) => {
          const falta = Math.max(0, f.quantidadePorPai - f.pronto)
          return (
            <li key={f.no.id}>
              {f.pronto > 0
                ? `${rotuloDoNo(f.no)}: pronto ${formatarQuantidade(f.pronto)}${falta > 0 ? `, falta ${formatarQuantidade(falta)} para 1 conjunto` : ''}`
                : `${rotuloDoNo(f.no)}: nenhum pronto, falta ${formatarQuantidade(f.quantidadePorPai)} para 1 conjunto`}
            </li>
          )
        })}
      </ul>
    </ItemComAcao>
  )
}
```

`CartaoDeKit.test.tsx` (jsdom, `cleanup`): o montável mostra "Dá para levar 2 conjunto(s)", os filhos com "8 (de Corte,
passo 1)" e "já está em Solda" quando `jaNoDestino`; marcar chama `aoAlternar(true)`; com `conjuntos="1"`, a linha do
filho de razão 4 diz "4"; com `conjuntos="1,5"`, o campo fica `aria-invalid`. O incompleto mostra "pronto 8" e "falta 1
para 1 conjunto" e não tem checkbox. Use uma massa local `kit()` no próprio arquivo de teste, montada com `no()` de
`testes/execucao.ts`.

- [ ] **Step 4: Filtro**

Em `filtroDaDemanda.ts`, com a decisão P5 (unidade = pai e filhos prontos):

```ts
/** O pai e os filhos prontos: o Material mora nos filhos (decisão P5 do plano da Fase 3B; mesmo critério de `nosDoCartao`). */
function nosDoKit(k: KitDto): Unidade {
  return [k.pai, ...k.filhos.filter((f) => f.pronto > 0).map((f) => f.no)]
}

export function facetasDasTarefas(tarefas: TarefasDto, selecao: Selecao): Faceta[] {
  return facetasDeUnidades([
    ...tarefas.grupos.flatMap((g) => g.itens.map((i) => [i.no])),
    ...tarefas.kitsMontaveis.map(nosDoKit),
    ...tarefas.kitsIncompletos.map(nosDoKit),
  ], selecao)
}

export function filtrarKits(kits: KitDto[], selecao: Selecao): KitDto[] {
  return kits.filter((k) => casa(nosDoKit(k), selecao))
}
```

`filtrarTarefas(grupos, selecao)` não muda. Ajuste os testes de `facetasDasTarefas` em `filtroDaDemanda.test.ts` à
assinatura nova e acrescente: um Kit cujo filho tem o Material X aparece sob o filtro X, e um Kit sem ele não.

- [ ] **Step 5: A página**

`TarefasPage.tsx`:
- `useCargaPeriodica(listarTarefas, ...)` devolve `dados: TarefasDto | null`; `const grupos = dados?.grupos ?? null`,
  `const montaveis = dados?.kitsMontaveis ?? []`, `const incompletos = dados?.kitsIncompletos ?? []`.
- Estado novo `const [conjuntos, setConjuntos] = useState<Record<number, string>>({})` (por `pai.id`).
- O efeito de "saiu da lista" também poda `conjuntos` de Kit que não está mais em `montaveis`, com o mesmo aviso.
- `alternarKit(kit, marcado)`: marca com `String(kit.conjuntos)`, desmarca removendo a chave; `setAviso(null)`.
- "Marcar todos" alcança também os Kits montáveis **visíveis depois do filtro**, cada um com `String(kit.conjuntos)`;
  `todosMarcados` exige itens e Kits visíveis marcados; o botão só some quando não há nem item nem Kit marcável.
- Validação: `algumInvalido` também é verdadeiro se algum Kit marcado tem `lerConjuntos(texto, kit.conjuntos).valor === null`.
- `enviar()`: os itens de hoje mais, para cada Kit marcado, um item por filho:

```ts
    const doKit: ItemDaEntrega[] = kitsMarcados.flatMap(({ kit, n }) => kit.filhos.map((f) => ({
      estruturaItemId: f.no.id,
      origem: { posicao: 'AguardandoColeta', setorId: f.origem!.id, ordem: f.ordem! },
      quantidade: quantidadeDoConjunto(n, f.quantidadePorPai),
    })))
```

  (Kit montável sempre tem `origem` em todo filho: `conjuntos >= 1` exige pronto em todos, e pronto exige Roteiro.)
  No sucesso, zera `escolhas` e `conjuntos`.
- Rótulo do botão por uma função local:

```ts
function rotuloDoEntregar(kits: number, itens: number): string {
  const k = kits === 1 ? '1 Kit' : `${kits} Kits`
  const i = itens === 1 ? '1 item' : `${itens} itens`
  if (kits > 0 && itens > 0) return `Entregar ${k} e ${i}`
  if (kits > 0) return `Entregar ${k}`
  if (itens > 0) return `Entregar ${i}`
  return 'Entregar'
}
```

- Ordem na tela: banners, filtro, "Marcar todos", carregando; **"Kits montáveis"** (uma `<section>` com `<h2>` "Kits
  montáveis" e uma `ListaDeCadastro rotulo="Kits montáveis"` de `CartaoDeKitMontavel`, só quando há Kit montável visível);
  os grupos "Em {Setor}"; o bloco de Entregar; e, por último, `{incompletos.length > 0 && <SecaoRecolhivel titulo="Kits incompletos" contagem={visiveisIncompletos.length}>...}`
  com `CartaoDeKitIncompleto` (filtrados por `filtrarKits`).
- Estados vazios: `vazia` passa a "sem grupos e sem Kit montável" (os incompletos continuam aparecendo abaixo do
  vazio); `vaziaPeloFiltro`, "havia grupos ou Kits montáveis, e o filtro escondeu todos".
- O bloco de Entregar aparece quando há grupo **ou** Kit montável.
- O "ocultos pelo filtro" conta também Kits marcados que o filtro esconde.
- Atualize o comentário da função `TarefasPage` com duas frases: os Kits vêm em cartões próprios, um por pai, e entram
  na mesma entrega; os filhos de Kit não aparecem nos grupos (regra 23, spec da Fase 3B, D7).

`TarefasPage.test.tsx`: `montarFetch` passa a receber `TarefasDto[]`; um helper `tarefas(grupos, montaveis = [], incompletos = [])`
monta o objeto, e as chamadas antigas passam a `tarefas(TAREFAS)` (ou `tarefas([])`). Testes novos:
1. *"Kit montável aparece em Kits montáveis"*: `getByRole('heading', { name: 'Kits montáveis' })`, o cartão e "Dá para levar 2 conjunto(s)".
2. *"marcar o Kit e entregar manda todos os filhos com N × razão"*: marca `Levar o Kit Chassi`, troca Conjuntos para 1,
   clica "Entregar 1 Kit"; o corpo tem os dois filhos, `quantidade` 4 e 1, origem `AguardandoColeta` com o Setor e o
   passo de cada filho.
3. *"Kit e item juntos numa entrega só"*: marca um item e um Kit; o botão diz "Entregar 1 Kit e 1 item"; um `POST` só,
   com três itens.
4. *"Marcar todos marca os Kits montáveis"*.
5. *"Conjuntos inválido trava o Entregar"*: "1,5" deixa o botão desabilitado.
6. *"Kits incompletos vem recolhida com a contagem"*: `getByText('Kits incompletos (1)').closest('details').open === false`.
7. *"Kit que sumiu na recarga sai da seleção com aviso"*: primeira resposta com o Kit, segunda sem; avança o relógio
   `INTERVALO_DA_EXECUCAO_MS`; o aviso de "saiu da lista" aparece e o botão volta a "Entregar".
8. *"filtro por Material do filho mostra o Kit"*.
9. *"só Kits incompletos: mostra o vazio e a seção recolhida"*.

Mutações a medir: (a) troque `quantidadeDoConjunto(n, ...)` por `n` no `enviar` → o teste 2 fica vermelho; (b) tire a
poda de `conjuntos` do efeito de "saiu da lista" → o teste 7 fica vermelho; (c) tire os Kits do "Marcar todos" → o 4 fica
vermelho.

Run: `cd web && npm test -- --run` e `npm run build`. Expected: verde.

- [ ] **Step 6: Commit**

```bash
git add web/src
git commit -m "feat(tarefas): Kits montaveis e incompletos na tela do Movimentador"
```

(Confira com `git status` que só entraram arquivos desta task.)

---

### Task 10: Documentos do domínio

**Modelo:** opus. É prosa que vira texto do TCC; nas fases anteriores, cada passe de conserto de prosa escreveu o
defeito seguinte, e a review em opus foi o que pegou.

**Files:**
- Modify: `specs/01-dominio-e-regras-de-negocio.md`, `specs/04-fluxos-de-usuario.md`, `specs/06-roadmap-mvp.md`, `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md` (errata)

- [ ] **Step 1: `01`**

- **Regra 23:** o "Kit pronto para montagem" aparece nas Tarefas como **"Kits montáveis"**, um cartão por pai, em que o
  Movimentador escolhe quantos conjuntos leva; o Kit que ainda não fecha um conjunto aparece em **"Kits incompletos"**,
  recolhida e informativa; o filho de Kit no último passo **não** aparece como "Item pronto" (ele está no cartão do Kit).
- **Regra 24:** apague a "Exceção conhecida" e diga que, desde a Fase 3B, acrescentar filho a nó que já saiu de "a
  iniciar" é recusado (`PaiJaIniciado`), então a saída limitada ao total montado vale **sem exceção**; "o que sobra à 3B"
  sai do texto. Uma frase: "a guarda é a resposta da 3B; o tratamento de estrutura alterada no meio da produção fica para
  a Fase 5".
- **Regra 25:** os conjuntos à espera são contados pelo filho mais adiantado (o maior `⌈espera ÷ razão⌉` entre os filhos,
  só nos Setores com `UtilizaKit`); o conjunto completo vale para a entrega com destino "aguardando montagem", inclusive
  o redirecionamento, e não para a folha que passa pela Solda no próprio Roteiro; a frase "(Como a perda do próprio nó
  entra nesta conta é decisão da spec da Fase 3B.)" passa a "(Como a perda do próprio nó entra nesta conta é decisão da
  Fase 5.)".
- **"Pontos ainda em aberto":** a nota "Atualizado em 2026-09-28" que diz "o nó que ganha filho depois de iniciado é a
  exceção, da 3B" ganha uma frase: desde a Fase 3B, é recusado.
- Uma linha em itálico antes da regra 22 ou depois da 33, no formato das existentes: "*A Fase 3B (spec
  `docs/superpowers/specs/2026-10-09-fase-3b-kit-e-montagem-design.md`) emendou as regras 23, 24 e 25.*"

- [ ] **Step 2: `04` e `06`**

- `04`: no fluxo do Movimentador (Tarefas), os cartões de Kit, o campo Conjuntos e a seção recolhida; no fluxo do
  operador (Fila), a pílula "Kit" e o "Levar o Kit para {Setor}"; no fluxo do Administrador em Setores, a caixa "Utiliza
  Kit". Leia a seção inteira de cada fluxo antes de editar: a contradição mora entre itens vizinhos.
- `06`: a seção "Fase 3B — Kit e montagem" ganha, no topo, "**Concluída em {data do merge}**" — **deixe a data para o
  pós-merge**, escreva agora "implementada na branch `fase-3b`"; o critério de pronto troca "a saída acima do montado —
  restrita ao nó que ganhou filho depois de iniciado" por "o filho acrescentado a nó já iniciado". A seção "Fase 5"
  ganha dois itens: **a perda nos tetos do Kit** (D5 da spec da Fase 3B) e **estrutura alterada no meio da produção**
  (D1: hoje recusada; o usuário tem uma ideia para a Fase 5).
- `CLAUDE.md`:
  - "Ordem de implementação": "a próxima da ordem é a **Fase 3B**" passa a dizer que a 3B está em implementação na
    branch `fase-3b` (o pós-merge troca para concluída).
  - Bullet "Cor de identidade nunca significa estado": acrescente que o azul `kit` é a primeira cor de **categoria** —
    identidade do Kit na Fila do Setor —, cheio para se separar das pílulas de estado.
  - "Invariantes de negócio": no bullet do início que consome os filhos, troque a "Exceção conhecida" pela guarda.

- [ ] **Step 3: Errata da spec do Kit**

No fim de `2026-09-15-kit-montagem-e-movimentacao-design.md`, depois da errata de 2026-09-24, uma errata nova no mesmo
formato ("Anexada sem reescrever o texto acima"), "— 2026-10-09, spec da Fase 3B", fechando um a um os itens da §9
"Fase 3B": trocar Tipo ou `UtilizaKit` (D4: livre); filho que conclui na mesma Solda (D2: entrega como os outros, no
conjunto); qual ação o limite trava (D1/D3: nenhuma validação nova); entrada num Setor `UtilizaKit` que não é para a
montagem (D3: a posição separa); como a perda entra nos tetos (D5: Fase 5); e se a folha que entra para o próprio passo
conta como à espera (D3: não conta, porque vai para `NoSetor`).

- [ ] **Step 4: Releitura e commit**

Releia cada **seção** editada inteira (não só a frase). Confira que nenhuma citação nova usa `arquivo.ext:NN`.

```bash
git add specs/01-dominio-e-regras-de-negocio.md specs/04-fluxos-de-usuario.md specs/06-roadmap-mvp.md CLAUDE.md \
  docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md
git commit -m "docs(fase-3b): regras 23 a 25, fluxos, roadmap e errata da spec do Kit"
```

---

### Task 11: Massa da conferência

**Modelo:** sonnet.

**Files:**
- Create: `db/massa-conferencia-fase-3b.sql`
- Create: `.superpowers/sdd/2026-10-09-fase-3b/conferencia/roteiro.md` (ledger, não o repositório de código)

- [ ] **Step 1: O script**

`db/massa-conferencia-fase-3b.sql`, idempotente e chaveado pelo número do Pedido (`CONF-3B`), fora do
`db/seed-demo.sql`. Cria, se não existir: a marca `UtilizaKit = 1` no Setor de nome `Solda` (o do `seed-demo`; se ele
não existir, o script para com `RAISERROR` dizendo para carregar o `seed-demo.sql` antes); um Pedido `CONF-3B` (cliente
"Conferência da 3B", tipo `Fabricacao`, `Aberto`, data de entrega daqui a 15 dias, autor `admin`); um Agrupamento
`KIT-01` tipo `Kit`; e uma estrutura de três níveis — Peça **A** (5; Roteiro Solda → Pintura), Item **B** (5, razão 1;
Roteiro Solda) filho de A, Itens **D** (20, razão 4; Roteiro Corte) e **E** (10, razão 2; Roteiro Dobra) filhos de B, e
Item **C** (10, razão 2; Roteiro Corte → Dobra) filho de A. Os nós são ad-hoc (descrição, sem Componente), menos a Peça,
que usa um Componente com sólido do `seed-demo` (o script escolhe o primeiro Componente ativo com `ArquivoSolidoId`
não nulo). Nada de movimentação: o usuário faz o fluxo na tela.

Cabeçalho do script com: o que ele cria, que é idempotente, que exige o `seed-demo.sql` e o `alter-fase-3b.sql`, e o
comando de carga (`docker compose cp` + `sqlcmd -b -f 65001`, como os alters).

Rode duas vezes contra o banco local e confira, com `SELECT`, que a segunda não duplicou nada.

- [ ] **Step 2: O roteiro de conferência (no ledger)**

`.superpowers/sdd/2026-10-09-fase-3b/conferencia/roteiro.md`, com os passos para o usuário, cada um com o que olhar:
1. Setores: a Solda com a pílula "Utiliza Kit"; editar e ver a caixa marcada.
2. Agrupamento `KIT-01`: título "Pedido CONF-3B — KIT-01 — Kit", com o link para o Pedido.
3. Fila do Corte: D e C com a pílula azul "Kit"; iniciar e terminar 8 de D.
4. Tarefas: B em "Kits incompletos (1)", recolhida, com "D: pronto 8" e "E: nenhum pronto".
5. Dobra: iniciar e terminar 4 de E. Tarefas: B em "Kits montáveis", "Dá para levar 2 conjunto(s)".
6. Tentar, pelo campo, 3 conjuntos (recusado na tela); levar 1; conferir na Fila da Solda o B com "Dá para iniciar 1".
7. Iniciar 1 de B na Solda (consome 4 D e 2 E); terminar B; levar B e C juntos para A (C precisa passar Corte → Dobra).
8. Tentar acrescentar um filho em B pela página do Agrupamento: o painel mostra a recusa de nó já iniciado.
9. No celular (ou na emulação de 375 px), a pílula azul e os cartões de Kit sem rolagem horizontal.

- [ ] **Step 3: Commit**

```bash
git add db/massa-conferencia-fase-3b.sql
git commit -m "chore(fase-3b): massa idempotente para a conferencia no navegador"
```

No ledger, commit separado com o roteiro, por caminho explícito.

---

### Task 12: Conferência no navegador — **do usuário**

**Dispensa de review, escrita antes:** o produto desta task não é código; é o relatório da conferência e as decisões do
usuário (primeira classe do `CLAUDE.md`). A justificativa vai no ledger e no relatório **antes** de a task começar.

- [ ] **Step 1:** O controlador sobe API e front (pedir `request_keep_awake` antes: a suspensão derruba os servidores),
  carrega `db/massa-conferencia-fase-3b.sql`, e **para**, avisando o usuário de que chegou na conferência.
- [ ] **Step 2:** O usuário segue `conferencia/roteiro.md`. O controlador pode capturar telas para apoiar, mas a captura
  **não** substitui a conferência.
- [ ] **Step 3:** O controlador escreve `conferencia/relatorio.md` com o que o usuário aprovou, o que pediu de ajuste e
  as decisões dele. Ajuste pedido vira task nova com gate próprio.
- [ ] **Step 4:** Só depois: a review de branch inteira (opus), o fix wave, e o PR.
