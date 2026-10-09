# Data de entrega do Pedido — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar a todo Pedido uma data de entrega obrigatória, editável na página do Pedido, com a pílula
"Atrasado" decidida no servidor, a lista de Pedidos aberta por prazo e a seção "Prazos de entrega" na
Home.

**Architecture:** A coluna `DataEntrega DATE NOT NULL` entra em `dbo.Pedido` pelo caminho Database First
(`02` → `db/alter-data-entrega.sql` → mapeamento EF como `DateOnly`). A regra de atraso e o "hoje de
Brasília" moram numa classe estática da `Application` (`PrazoDeEntrega`), alimentada por um `TimeProvider`
injetado no `CadastroDePedidoUseCase`; o `PedidoDto` sai com `DataEntrega` e `Atrasado` prontos. O
repositório ganha a ordem `Entrega` (padrão) e a consulta dos cinco mais urgentes. No front, um tom de
pílula novo (`atraso`, roxo), a data na `LinhaDePedido`, o campo no cadastro, a edição na
`PedidoDetalhePage` e a Home com "Prazos de entrega".

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), React
Router, Vitest + Testing Library + jsdom, Tailwind.

**Spec:** `docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`, aprovada em 2026-10-08
(decisões D1–D13). Branch: `data-de-entrega`, sobre `b35cae8`. A spec está em `dac2a90`, e os ajustes de
`specs/06` e `CLAUDE.md` que oficializam a ordem (seção 9 da spec, "antes do plano") estão em `39844df`.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review →
  fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 7**, conferência no
  navegador, que é **do usuário** e cujo produto é relatório. A justificativa vai escrita **antes** no
  ledger e no relatório.
- **A review de branch inteira só é despachada depois que o usuário fizer a Task 7** (regra do usuário de
  2026-10-07, no `CLAUDE.md`). O controlador para e avisa quando chegar nela.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com
  `scripts/review-package BASE HEAD`, nunca com `HEAD~1`. Ledger do plano em
  `.superpowers/sdd/2026-10-08-data-de-entrega/`; artefatos com nome escopado por este plano (os números de
  task colidem com os de planos anteriores).
- **Schema muda pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois
  `db/alter-data-entrega.sql` idempotente (formato de `db/alter-fase-3d.sql`: `-b -f 65001`), depois o
  mapeamento EF. Nada de `Add-Migration` nem `EnsureCreated`.
- `DataEntrega` é **`DATE`** no banco, **`DateOnly`** no C# e **`"aaaa-mm-dd"`** no JSON — nunca
  `DateTime`, que passaria pelo `HorarioDeBrasiliaJsonConverter` e deslocaria o dia.
- "Hoje" é a data em **GMT-3 fixo**, o mesmo offset do `HorarioDeBrasiliaJsonConverter` — nunca
  `TimeZoneInfo.FindSystemTimeZoneById`.
- Regra de atraso (regra 33 do `01`, escrita na Task 2): `DataEntrega < hoje` **e** status fora de
  `Concluido`/`Cancelado`. Vence hoje **não** está atrasado.
- Ordem `entrega` (Task 3): não encerrados primeiro, por `DataEntrega` crescente, desempate `DataAbertura`
  crescente e `Id` crescente; depois `Concluido`/`Cancelado`, por `DataEntrega` **decrescente**, desempate
  `Id` decrescente.
- Tom `atraso`: `--color-atraso-texto: #6B21A8` sobre `--color-atraso-fundo: #F3E8FF` (7,39:1).
- Rótulos de tela, exatos: seção da Home **"Prazos de entrega"**; opção de ordem **"Prazo de entrega"**;
  campo **"Data de entrega"**; pílula **"Atrasado"**; botão **"Editar pedido"**; linha
  **"entrega em dd/mm/aaaa · aberto em dd/mm/aaaa hh:mm"**.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês.
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa. Comentário de código **não cita**
  ledger, brief nem "Task N": o repositório é público. Decisões podem ser citadas como "D*n* da spec da data
  de entrega" ou "decisão P*n* do plano da data de entrega".
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com
  `dotnet test Rastreamento.slnx -m:1`. Teste de banco **escopa a asserção** nas linhas que ele inseriu.
- Front: `npm test -- --run` **e** `npm run build` (o Vitest não faz checagem de tipo). Primitivas de
  `web/src/components/`, cores só por token, `// @vitest-environment jsdom` + `afterEach(cleanup)`, mocks por
  `web/src/testes/api.ts`. Teste acha elemento por papel e nome acessível.
- Texto de tela em português **com** acento. Mensagens de erro do backend sem acento, como no arquivo
  vizinho.
- Edite fonte com Edit/Write, nunca com `Set-Content` do PowerShell 5.1 (corrompe UTF-8 e a suíte fica
  verde assim mesmo).
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em
  chamada separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm ci && npm test -- --run && npm run build
```

**Baseline:** o ledger registra, medido em 2026-10-08 na máquina local, backend **1241** (Api 345 ·
Application 685 · Infrastructure 211) e front **1330 testes / 76 arquivos**. **O pré-flight remede antes da
Task 1** e escreve o número no ledger do plano. Cada implementer reporta o **delta** da própria task, nunca um
total previsto por este plano.

## Decisões deste plano (onde a spec deixou a escolha ou o código pediu um ajuste)

- **P1 — Fakes da `Application` não ganham data à toa.** A seção 7.1 da spec diz que os `new Pedido`
  montados direto "passam a informar a data". Este plano cumpre isso nos que vão ao **banco**
  (`Infrastructure.Tests` e `Api.Tests`), onde um `0001-01-01` viraria o Pedido mais atrasado da tabela
  compartilhada. Nos 16 de `Application.Tests`, que rodam contra fake, a data só entra onde o teste é sobre
  ela: lá o padrão do `DateOnly` não toca em nada além do próprio teste.
- **P2 — A edição usa a resposta do `PUT`.** A seção 6.5 da spec diz que, no sucesso, o Pedido "é
  recarregado". O `PUT /pedidos/{id}` já devolve o `PedidoDto` completo (com `pausa` e `atrasado`), o mesmo
  do `GET`; a tela o aplica direto, sem segunda requisição e sem recarregar os Agrupamentos.
- **P3 — O repositório conhece os encerrados.** A ordem `Entrega` precisa separar `Concluido`/`Cancelado`
  dentro do SQL, e a `Infrastructure` não referencia a `Application`. O repositório declara a própria lista
  (`StatusEncerrados`), com comentário apontando o `CK_Pedido_Status`. A `Application` continua sendo a dona
  da lista que vai para `ListarMaisUrgentesAsync` (`PrazoDeEntrega.StatusEncerrados`).
- **P4 — Escopo dos testes de banco.** A ordem `entrega` é testada escopada pela busca do cliente único do
  teste, com a página de 100 do helper existente (o teste cria cinco linhas). `ListarMaisUrgentesAsync`
  segue o padrão do teste que já existia para `ListarMaisAntigosAsync` (seis candidatos próprios, `quantos`
  = 5, e a prova de que nenhuma linha própria que devia entrar ficou abaixo da quinta), em vez do
  `quantos = int.MaxValue` da seção 7.1 da spec: o padrão existente é robusto a linhas de terceiros,
  inclusive às de `0001-01-01`, e ainda prova o corte.
- **P5 — `entrega` vem primeiro na frase do 400** de ordem desconhecida (`Aceitas: entrega, recentes,
  numero, cliente.`), porque é a padrão.
- **P6 — O relógio entra pelo construtor** do `CadastroDePedidoUseCase` (`TimeProvider`), registrado como
  `TimeProvider.System` no `Program.cs`. Nos testes, `RelogioFixo`, uma subclasse escrita à mão.
- **P7 — Na página do Pedido, o prazo vai na mesma linha das pílulas**, no mesmo formato da
  `LinhaDePedido` ("entrega em … · aberto em …"), em vez de um "Entrega em" isolado.

---

### Task 1: Schema, migração e mapeamento da `DataEntrega`

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `dbo.Pedido`)
- Create: `db/alter-data-entrega.sql`
- Modify: `src/Rastreamento.Domain/Entities/Pedido.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/PedidoMappingTests.cs`
- Modify (data no `new Pedido`): `AgrupamentoMappingTests.cs`, `ArvoreDeTesteNoBanco.cs`,
  `EstruturaItemMapeamentoTests.cs`, `EstruturaRepositoryTests.cs`, `PedidoRepositoryTests.cs`,
  `RetryDeDeadlockEmLerAsyncTests.cs`, `RetryDeDeadlockEmTransacaoAsyncTests.cs` (todos em
  `tests/Rastreamento.Infrastructure.Tests/Persistence/`) e `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`
- Modify: `CLAUDE.md` (bloco de comando do alter)

**Interfaces:**
- Produces: `Pedido.DataEntrega` (`DateOnly`); coluna `dbo.Pedido.DataEntrega DATE NOT NULL`;
  `PedidoRepositoryTests.NovoPedidoAsync(db, numero, cliente, status = "Aberto", DateTime? dataAbertura = null, DateOnly? dataEntrega = null)`;
  `PedidosEndpointsTests.GravarPedidoAsync(cliente, status, numero = null, DateTime? dataAbertura = null, DateOnly? dataEntrega = null)`.

- [ ] **Step 1: Escrever o teste de mapeamento que falha**

Em `PedidoMappingTests.cs`, acrescente o teste abaixo (a classe já tem `IdDoAdmin` e `NovoContexto`):

```csharp
  [Fact]
  public async Task Data_de_entrega_vai_e_volta_da_coluna_date_sem_deslocar_o_dia()
  {
    // `DateOnly` contra `DATE`: o dia gravado e o dia lido. Um mapeamento por `DateTime`, com
    // conversao de fuso no caminho, devolveria o dia anterior — e e isso que este teste pega. O tipo e
    // a nulidade da coluna saem do catalogo do banco, nao do modelo do EF.
    await using var db = NovoContexto();
    var autor = await IdDoAdmin(db);
    var pedido = new Pedido
    {
      Numero = $"ent-{Guid.NewGuid():N}"[..25], Cliente = "Teste", Tipo = "Fabricacao", Status = "Aberto",
      DataAbertura = DateTime.UtcNow, DataEntrega = new DateOnly(2026, 10, 22), CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    try
    {
      await using var dbLeitura = NovoContexto();
      var lido = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedido.Id);
      Assert.Equal(new DateOnly(2026, 10, 22), lido.DataEntrega);

      var tipo = await dbLeitura.Database.SqlQuery<string>(
          $"SELECT DATA_TYPE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Pedido' AND COLUMN_NAME = 'DataEntrega'")
          .SingleAsync();
      var anulavel = await dbLeitura.Database.SqlQuery<string>(
          $"SELECT IS_NULLABLE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Pedido' AND COLUMN_NAME = 'DataEntrega'")
          .SingleAsync();
      Assert.Equal("date", tipo);
      Assert.Equal("NO", anulavel);
    }
    finally
    {
      await using var dbLimpeza = NovoContexto();
      await dbLimpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {pedido.Id}");
    }
  }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build Rastreamento.slnx -warnaserror`
Expected: FAIL — `'Pedido' does not contain a definition for 'DataEntrega'`.

- [ ] **Step 3: Schema na fonte de verdade**

Em `specs/02-modelo-de-dados.sql`, na tabela `dbo.Pedido`, logo depois da linha de `DataAbertura`:

```sql
    DataEntrega         DATE                NOT NULL,  -- prazo combinado com o cliente; dia, sem hora nem fuso
```

- [ ] **Step 4: Migração idempotente**

Crie `db/alter-data-entrega.sql`:

```sql
-- Migracao idempotente da data de entrega do Pedido para banco criado ANTES dela. A fonte de verdade
-- continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo
-- ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md,
-- "Comandos" (o mesmo comando do db/alter-fase-3d.sql, com este arquivo).
SET NOCOUNT ON;
GO

/* 1. A coluna nasce nula, para os Pedidos que ja existem (spec da data de entrega, secao 3.2) ------ */
IF COL_LENGTH('dbo.Pedido', 'DataEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD DataEntrega DATE NULL;
GO

/* 2. Preenche os existentes com a data de abertura em Brasilia (GMT-3 fixo). E um valor inventado:
      esses Pedidos nao tem prazo de verdade, e com ele todo Pedido antigo ainda aberto aparece como
      atrasado — o que mostra que o prazo precisa ser revisto. ------------------------------------- */
UPDATE dbo.Pedido
   SET DataEntrega = CAST(DATEADD(HOUR, -3, DataAbertura) AS DATE)
 WHERE DataEntrega IS NULL;
GO

/* 3. NOT NULL, como no 02 --------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Pedido') AND name = 'DataEntrega' AND is_nullable = 1)
    ALTER TABLE dbo.Pedido ALTER COLUMN DataEntrega DATE NOT NULL;
GO
```

- [ ] **Step 5: Entidade e mapeamento**

Em `Pedido.cs`, depois de `DataAbertura`:

```csharp
  /// <summary>
  /// Prazo combinado com o cliente: um dia, sem hora nem fuso (`DATE`). `DateOnly`, e nao `DateTime`,
  /// para nao passar pela conversao de fuso da borda da API, que deslocaria o dia.
  /// </summary>
  public DateOnly DataEntrega { get; set; }
```

Em `PedidoConfiguration.cs`, depois da linha de `Status`:

```csharp
    b.Property(p => p.DataEntrega).HasColumnType("date");
```

- [ ] **Step 6: Aplicar a migração no banco de dev**

```bash
MSYS_NO_PATHCONV=1 docker compose cp db/alter-data-entrega.sql sqlserver:/tmp/alter-data-entrega.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-data-entrega.sql
```

Rode o mesmo comando **de novo** e confira que sai com código 0 (idempotência). Confira a coluna:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -Q "SELECT DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Pedido' AND COLUMN_NAME = 'DataEntrega'; SELECT COUNT(*) AS SemData FROM dbo.Pedido WHERE DataEntrega IS NULL;"
```

Expected: `date`, `NO`, `SemData` = 0.

- [ ] **Step 7: Rodar o teste novo**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~PedidoMappingTests"`
Expected: o teste novo PASSA; `Data_de_abertura_e_status_nascem_pelos_defaults_do_banco` FALHA (o `INSERT`
cru omite `DataEntrega`, e a coluna é `NOT NULL`).

- [ ] **Step 8: Os testes que gravam Pedido no banco passam a informar a data**

1. No `INSERT` cru de `Data_de_abertura_e_status_nascem_pelos_defaults_do_banco`, a coluna entra:

```csharp
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, DataEntrega, CriadoPorUsuarioId) VALUES ({numero}, 'Teste', 'Fabricacao', '2026-10-22', {autor})");
```

e o comentário do teste ganha uma frase: "`DataEntrega` vai no `INSERT` porque nao tem `DEFAULT` (D2 da spec
da data de entrega)."

2. Em cada `new Pedido { ... }` dos arquivos de `Infrastructure.Tests` listados em **Files** (inclusive o
`Mapeia_pedido_com_a_coluna_de_autoria` de `PedidoMappingTests`), acrescente
`DataEntrega = new DateOnly(2026, 10, 22),` ao inicializador. Confira que não sobrou nenhum:

```bash
grep -rn "new Pedido\b" -A6 tests/Rastreamento.Infrastructure.Tests tests/Rastreamento.Api.Tests --include=*.cs | grep -c "DataEntrega"
grep -rn "new Pedido\b" tests/Rastreamento.Infrastructure.Tests tests/Rastreamento.Api.Tests --include=*.cs | wc -l
```

Os dois números têm de ser iguais.

3. Em `PedidoRepositoryTests`, o helper ganha o parâmetro:

```csharp
  /// <summary>Pedido solto (sem Agrupamento); a limpeza dele e <see cref="ApagarPedidosAsync"/>.</summary>
  private static async Task<int> NovoPedidoAsync(
      RastreamentoDbContext db, string numero, string cliente, string status = "Aberto", DateTime? dataAbertura = null,
      DateOnly? dataEntrega = null)
  {
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
    var pedido = new Pedido
    {
      Numero = numero, Cliente = cliente, Tipo = "Fabricacao", Status = status,
      DataAbertura = dataAbertura ?? DateTime.UtcNow, DataEntrega = dataEntrega ?? new DateOnly(2026, 10, 22),
      CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    return pedido.Id;
  }
```

4. Em `PedidosEndpointsTests`, o `GravarPedidoAsync` ganha o mesmo parâmetro e o mesmo padrão:

```csharp
  private async Task<int> GravarPedidoAsync(
      string cliente, string status, string? numero = null, DateTime? dataAbertura = null, DateOnly? dataEntrega = null)
```

com `DataEntrega = dataEntrega ?? new DateOnly(2026, 10, 22),` no inicializador.

- [ ] **Step 9: `CLAUDE.md` — o comando do alter**

Em "Comandos", logo depois do bloco do `db/alter-importacao-bom.sql`, acrescente:

```markdown
**Data de entrega do Pedido — `db/alter-data-entrega.sql`.** Mesmo formato dos anteriores (idempotente,
`-b -f 65001`). Leva um banco anterior até o `02-modelo-de-dados.sql`: a coluna `Pedido.DataEntrega`
(`DATE NOT NULL`), em três passos — nasce nula, os Pedidos existentes recebem a **data de abertura em
Brasília** (um valor inventado, que faz todo Pedido antigo ainda aberto aparecer como atrasado; seção 3.2
da spec `docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`) e vira `NOT NULL`. Não
precisa regenerar o banco.
```

seguido do bloco `bash` com os dois comandos do Step 6.

- [ ] **Step 10: Suíte e build**

Run: `dotnet build Rastreamento.slnx -warnaserror` e `dotnet test Rastreamento.slnx -m:1`
Expected: 0 warnings; tudo verde. O `POST /pedidos` ainda não manda data (Task 2): o Pedido nasce com
`0001-01-01`, que é um `DATE` válido — esperado nesta task.

- [ ] **Step 11: Commit**

```bash
git add specs/02-modelo-de-dados.sql db/alter-data-entrega.sql src/Rastreamento.Domain/Entities/Pedido.cs src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs tests/Rastreamento.Infrastructure.Tests tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs CLAUDE.md
git commit -m "feat(pedido): coluna DataEntrega (DATE NOT NULL), alter idempotente e mapeamento DateOnly"
```

---

### Task 2: Regra de atraso, DTOs, cadastro e edição

**Files:**
- Create: `src/Rastreamento.Application/Cadastros/PrazoDeEntrega.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`PedidoDto`, `NovoPedidoDto`)
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Create: `tests/Rastreamento.Application.Tests/Common/RelogioFixo.cs`
- Create: `tests/Rastreamento.Application.Tests/Cadastros/PrazoDeEntregaTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDePedidoUseCaseTests.cs`
- Modify: todo `POST`/`PUT` de Pedido em `tests/Rastreamento.Api.Tests/` (lista no Step 9)
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (regra 33), `specs/05-api-endpoints.md`

**Interfaces:**
- Consumes: `Pedido.DataEntrega` (Task 1).
- Produces:
  - `PrazoDeEntrega.StatusEncerrados` (`IReadOnlyList<string>`: `Concluido`, `Cancelado`);
    `PrazoDeEntrega.HojeEmBrasilia(TimeProvider) → DateOnly`;
    `PrazoDeEntrega.EstaAtrasado(DateOnly dataEntrega, string status, DateOnly hoje) → bool`.
  - `PedidoDto(int Id, string Numero, string Cliente, string Tipo, string Status, DateTime DataAbertura, DateOnly DataEntrega, bool Atrasado, int CriadoPorUsuarioId, PausaResumoDto? Pausa)`.
  - `NovoPedidoDto(string Numero, string Cliente, DateOnly? DataEntrega)`.
  - `CadastroDePedidoUseCase(IPedidoRepository repositorio, TimeProvider relogio)`.
  - `RelogioFixo(DateTimeOffset agora) : TimeProvider` (teste).
  - JSON: `dataEntrega` (`"aaaa-mm-dd"`) e `atrasado` em todo `PedidoDto`; corpo de `POST`/`PUT` com `dataEntrega`.

- [ ] **Step 1: O relógio de teste**

Crie `tests/Rastreamento.Application.Tests/Common/RelogioFixo.cs`:

```csharp
namespace Rastreamento.Application.Tests.Common;

/// <summary>
/// `TimeProvider` parado num instante, para o teste fixar o "hoje". Escrito a mao em vez do
/// `FakeTimeProvider` do pacote de testes da Microsoft: so `GetUtcNow` e usado, e um pacote novo nao
/// se paga por uma linha.
/// </summary>
public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
  public override DateTimeOffset GetUtcNow() => agora;
}
```

- [ ] **Step 2: Os testes da regra, que falham**

Crie `tests/Rastreamento.Application.Tests/Cadastros/PrazoDeEntregaTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Tests.Common;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class PrazoDeEntregaTests
{
  private static readonly DateOnly Hoje = new(2026, 10, 8);

  [Theory]
  [InlineData("Aberto")]
  [InlineData("EmProducao")]
  [InlineData("AguardandoExpedicao")]
  public void Prazo_de_ontem_num_Pedido_nao_encerrado_esta_atrasado(string status) =>
      Assert.True(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(-1), status, Hoje));

  [Fact]
  public void Prazo_de_hoje_ainda_nao_esta_atrasado() =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje, "Aberto", Hoje));

  [Fact]
  public void Prazo_futuro_nao_esta_atrasado() =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(1), "Aberto", Hoje));

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public void Pedido_encerrado_nunca_esta_atrasado(string status) =>
      Assert.False(PrazoDeEntrega.EstaAtrasado(Hoje.AddDays(-30), status, Hoje));

  [Fact]
  public void Os_encerrados_sao_Concluido_e_Cancelado() =>
      Assert.Equal(["Cancelado", "Concluido"], PrazoDeEntrega.StatusEncerrados.Order());

  // A virada do dia em Brasilia (GMT-3 fixo) e as 03h00 UTC. As duas pontas: um "hoje" calculado em UTC
  // erraria a primeira, e um offset trocado de sinal erraria as duas.
  [Fact]
  public void As_02h59_UTC_ainda_e_o_dia_anterior_em_Brasilia() =>
      Assert.Equal(
          new DateOnly(2026, 10, 7),
          PrazoDeEntrega.HojeEmBrasilia(new RelogioFixo(new DateTimeOffset(2026, 10, 8, 2, 59, 59, TimeSpan.Zero))));

  [Fact]
  public void As_03h00_UTC_ja_e_o_dia_seguinte_em_Brasilia() =>
      Assert.Equal(
          new DateOnly(2026, 10, 8),
          PrazoDeEntrega.HojeEmBrasilia(new RelogioFixo(new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero))));
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~PrazoDeEntregaTests"`
Expected: FAIL de compilação — `The name 'PrazoDeEntrega' does not exist`.

- [ ] **Step 4: A regra**

Crie `src/Rastreamento.Application/Cadastros/PrazoDeEntrega.cs`:

```csharp
namespace Rastreamento.Application.Cadastros;

/// <summary>
/// A regra de atraso do Pedido (regra 33 do `01`) e o "hoje" contra o qual ela compara. Mora num lugar
/// so: a lista, a Home e a pagina do Pedido leem o `Atrasado` que sai daqui, e nenhuma tela o recalcula.
/// </summary>
public static class PrazoDeEntrega
{
  /// <summary>
  /// GMT-3 fixo, o mesmo offset da borda de fuso da API (`HorarioDeBrasiliaJsonConverter`), e pelo mesmo
  /// motivo: a busca do fuso por nome lanca num host sem ICU.
  /// </summary>
  private static readonly TimeSpan OffsetDeBrasilia = TimeSpan.FromHours(-3);

  /// <summary>Status em que o Pedido acabou: nao fica atrasado e nao entra nos mais urgentes.</summary>
  public static readonly IReadOnlyList<string> StatusEncerrados = ["Concluido", "Cancelado"];

  public static DateOnly HojeEmBrasilia(TimeProvider relogio) =>
      DateOnly.FromDateTime(relogio.GetUtcNow().ToOffset(OffsetDeBrasilia).DateTime);

  /// <summary>Vence hoje ainda nao e atraso: so o prazo anterior a hoje conta.</summary>
  public static bool EstaAtrasado(DateOnly dataEntrega, string status, DateOnly hoje) =>
      dataEntrega < hoje && !StatusEncerrados.Contains(status);
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~PrazoDeEntregaTests"`
Expected: PASS (9 casos).

- [ ] **Step 6: DTOs**

Em `Dtos.cs`, o `PedidoDto` e o `NovoPedidoDto` passam a ser:

```csharp
/// <remarks>
/// `DataAbertura` sai daqui em UTC; quem converte para GMT-3 e o HorarioDeBrasiliaJsonConverter,
/// registrado uma vez em Program.cs — nenhum endpoint precisa converter na mao. `DataEntrega` e
/// `DateOnly` e nao passa por ele: sai como "aaaa-mm-dd". `Atrasado` e decidido aqui, no servidor
/// (`PrazoDeEntrega`), e a tela so desenha a pilula.
/// </remarks>
public sealed record PedidoDto(
    int Id,
    string Numero,
    string Cliente,
    string Tipo,
    string Status,
    DateTime DataAbertura,
    DateOnly DataEntrega,
    bool Atrasado,
    int CriadoPorUsuarioId,
    PausaResumoDto? Pausa);
```

```csharp
/// <remarks>
/// So `Numero`, `Cliente` e `DataEntrega`: `Tipo` e `Status` sao decididos pelo use case, e o autor vem
/// da claim da sessao. Nenhum dos tres se aceita do cliente. Os `MaxLength` espelham `dbo.Pedido`.
/// `DataEntrega` e anulavel SO aqui, para a ausencia virar o 400 de campo obrigatorio em vez de um
/// `0001-01-01` silencioso; a coluna e `NOT NULL`.
/// </remarks>
public sealed record NovoPedidoDto(
    [MaxLength(30)] string Numero,
    [MaxLength(200)] string Cliente,
    DateOnly? DataEntrega);
```

- [ ] **Step 7: Os testes do caso de uso, que falham**

Em `CadastroDePedidoUseCaseTests.cs`:

1. Acrescente `using Rastreamento.Application.Tests.Common;` e, no topo da classe:

```csharp
  /// <summary>Prazo de todo Pedido de teste que nao e sobre a data.</summary>
  private static readonly DateOnly Prazo = new(2026, 10, 22);

  /// <summary>"Hoje" de todos os testes desta classe: 2026-10-08 em Brasilia.</summary>
  private static readonly RelogioFixo Relogio = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));

  private static CadastroDePedidoUseCase NovoUseCase(FakePedidoRepo repo) => new(repo, Relogio);
```

2. Troque **todo** `new CadastroDePedidoUseCase(X)` por `NovoUseCase(X)` (27 ocorrências em 2026-10-08, todas
neste arquivo) e todo `new NovoPedidoDto(numero, cliente)` por `new NovoPedidoDto(numero, cliente, Prazo)`.

3. Acrescente:

```csharp
  [Fact]
  public async Task Cadastrar_sem_data_de_entrega_e_erro_de_validacao()
  {
    var repo = new FakePedidoRepo();

    var resultado = await NovoUseCase(repo).Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X", null), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal("Numero, cliente e data de entrega sao obrigatorios.", resultado.Erro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_sem_data_de_entrega_e_erro_de_validacao()
  {
    var repo = new FakePedidoRepo(new Pedido { Id = 1, Numero = "PED-001", Cliente = "Y", Status = "Aberto" });

    var resultado = await NovoUseCase(repo).Editar(
        1, new NovoPedidoDto("PED-001", "Y", null), CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Cadastra_com_prazo_no_passado_e_ja_nasce_atrasado()
  {
    // Qualquer data vale (D3 da spec da data de entrega): um Pedido pode chegar ao sistema atrasado.
    var repo = new FakePedidoRepo();

    var resultado = await NovoUseCase(repo).Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X", new DateOnly(2020, 1, 1)), UsuarioDaSessao, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(new DateOnly(2020, 1, 1), resultado.Valor!.DataEntrega);
    Assert.True(resultado.Valor.Atrasado);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_grava_a_data_de_entrega_mesmo_num_Pedido_concluido()
  {
    // Sem guarda de status (D8 da spec da data de entrega): o Pedido e documento e se corrige por edicao.
    var repo = new FakePedidoRepo(new Pedido
    {
      Id = 1, Numero = "PED-001", Cliente = "Y", Tipo = "Fabricacao", Status = "Concluido",
      DataEntrega = new DateOnly(2026, 9, 1), CriadoPorUsuarioId = 7,
    });

    var resultado = await NovoUseCase(repo).Editar(
        1, new NovoPedidoDto("PED-001", "Y", new DateOnly(2026, 9, 15)), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(new DateOnly(2026, 9, 15), resultado.Valor!.DataEntrega);
    Assert.False(resultado.Valor.Atrasado);   // encerrado nunca esta atrasado
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task O_atraso_usa_o_relogio_injetado_e_nao_o_do_sistema()
  {
    // Relogio parado em 2000: um prazo de 2010 nao esta atrasado para ele, e estaria para o relogio
    // real. Um use case que lesse `DateTime.UtcNow` ou `TimeProvider.System` erraria aqui.
    var repo = new FakePedidoRepo(new Pedido
    {
      Id = 1, Numero = "PED-001", Cliente = "Y", Tipo = "Fabricacao", Status = "Aberto",
      DataEntrega = new DateOnly(2010, 1, 1),
    });
    var useCase = new CadastroDePedidoUseCase(repo, new RelogioFixo(new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero)));

    var resultado = await useCase.Obter(1, CancellationToken.None);

    Assert.False(resultado.Valor!.Atrasado);
  }
```

- [ ] **Step 8: O caso de uso**

Em `CadastroDePedidoUseCase.cs`:

1. A constante de erro vira:

```csharp
  private const string ErroDeCampoObrigatorio = "Numero, cliente e data de entrega sao obrigatorios.";
```

2. Remova o campo `StatusEncerrados` privado da classe (a lista passa a ser `PrazoDeEntrega.StatusEncerrados`)
e troque o uso dele em `Resumo` por `PrazoDeEntrega.StatusEncerrados`.

3. Construtor e campo:

```csharp
  private readonly IPedidoRepository _repositorio;
  private readonly TimeProvider _relogio;

  /// <summary>O relogio e de onde sai o "hoje" da regra de atraso (`PrazoDeEntrega.HojeEmBrasilia`).</summary>
  public CadastroDePedidoUseCase(IPedidoRepository repositorio, TimeProvider relogio)
  {
    _repositorio = repositorio;
    _relogio = relogio;
  }
```

4. Em `Cadastrar`, a validação e a entidade:

```csharp
    var (numero, cliente) = Normalizar(novo);
    if (numero.Length == 0 || cliente.Length == 0 || novo.DataEntrega is not { } dataEntrega)
      return Result<PedidoDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);
```

com `DataEntrega = dataEntrega,` no inicializador do `new Pedido`, e o retorno `Projetar(pedido, null, Hoje())`.

5. Em `Editar`, a mesma validação (`alterado.DataEntrega is not { } dataEntrega`), a gravação
`pedido.DataEntrega = dataEntrega;` junto de `Numero` e `Cliente`, o retorno
`Projetar(pedido, pausas.GetValueOrDefault(id), Hoje())`, e o `<remarks>` do método reescrito:

```csharp
  /// <remarks>
  /// Editar nao toca em `CriadoPorUsuarioId`: autoria e do momento da criacao. Nao ha guarda por status,
  /// por decisao (D8 da spec da data de entrega): o Pedido e documento e se corrige por edicao em
  /// qualquer status, inclusive o prazo de um Pedido ja concluido.
  /// </remarks>
```

6. `Obter` passa `Hoje()` ao `Projetar`; `ProjetarComPausas` calcula `var hoje = Hoje();` uma vez e passa a
cada item. A projeção e o helper:

```csharp
  /// <summary>Uma vez por requisicao: todos os Pedidos de uma resposta comparam contra o mesmo dia.</summary>
  private DateOnly Hoje() => PrazoDeEntrega.HojeEmBrasilia(_relogio);

  private static PedidoDto Projetar(Pedido p, PausaAberta? pausa, DateOnly hoje) =>
      new(p.Id, p.Numero, p.Cliente, p.Tipo, p.Status, p.DataAbertura, p.DataEntrega,
          PrazoDeEntrega.EstaAtrasado(p.DataEntrega, p.Status, hoje),
          p.CriadoPorUsuarioId, PausaResumoDto.De(pausa));
```

- [ ] **Step 9: DI e os testes de API**

Em `Program.cs`, logo antes do bloco "Cadastros":

```csharp
// O "hoje" da regra de atraso do Pedido (PrazoDeEntrega). Injetado, e nao `DateTime.UtcNow` no use
// case, para o teste fixar o dia.
builder.Services.AddSingleton(TimeProvider.System);
```

Nos testes de API, todo corpo de `POST /api/pedidos` e de `PUT /api/pedidos/{id}` passa a levar
`dataEntrega = "2026-10-22"`. Os lugares, medidos em 2026-10-08 (confira com
`grep -rn "/api/pedidos" tests/Rastreamento.Api.Tests --include=*.cs` e leia cada um — um `POST` ocupa mais
de uma linha, e o literal também aparece em `GET`):

- os helpers de criar Pedido de `AgrupamentosEndpointsTests`, `CenarioDaFase3NaApi`,
  `ConfirmacaoDeImportacaoTests`, `EstruturaEndpointsTests`, `ExecucaoEndpointsTests` e
  `ImportacaoEndpointsTests`;
- em `PedidosEndpointsTests`, todos os `PostAsJsonAsync("/api/pedidos", ...)` e `PutAsJsonAsync(...)`,
  **menos** o `Cadastrar_com_token_sem_a_claim_sub_responde_401` e o `Qualidade_nao_escreve_em_pedido`, que
  respondem antes do model binding; no `Campo_maior_que_a_coluna_responde_400_e_nao_500`, o dicionário
  ganha `["dataEntrega"] = "2026-10-22"` (sem ela o 400 viria da data, e o teste deixaria de provar o
  `MaxLength`).

Acrescente a `PedidosEndpointsTests`:

```csharp
  [Fact]
  public async Task Cadastrar_sem_data_de_entrega_responde_400()
  {
    var resposta = await ClienteComo("PCP")
        .PostAsJsonAsync("/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Data_de_entrega_sai_como_dia_sem_hora_nem_fuso()
  {
    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        "/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X", dataEntrega = "2099-12-31" });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var texto = await resposta.Content.ReadAsStringAsync();
    // Texto exato, e nao `GetDateTime()`: e a guarda contra a data passar pelo
    // `HorarioDeBrasiliaJsonConverter`, que a escreveria com hora e offset -03:00.
    Assert.Contains("\"dataEntrega\":\"2099-12-31\"", texto);
    Assert.False(JsonDocument.Parse(texto).RootElement.GetProperty("atrasado").GetBoolean());
  }

  [Fact]
  public async Task Prazo_no_passado_e_aceito_e_sai_atrasado()
  {
    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        "/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X", dataEntrega = "2000-01-01" });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.True(corpo.GetProperty("atrasado").GetBoolean());
  }

  [Fact]
  public async Task Editar_altera_a_data_de_entrega()
  {
    var cliente = ClienteComo("PCP");
    var numero = NumeroUnico();
    var criado = await cliente.PostAsJsonAsync(
        "/api/pedidos", new { numero, cliente = "Cliente X", dataEntrega = "2026-10-22" });
    var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PutAsJsonAsync(
        $"/api/pedidos/{id}", new { numero, cliente = "Cliente X", dataEntrega = "2099-01-15" });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Contains("\"dataEntrega\":\"2099-01-15\"", await resposta.Content.ReadAsStringAsync());
    Assert.Contains("\"dataEntrega\":\"2099-01-15\"", await cliente.GetStringAsync($"/api/pedidos/{id}"));
  }
```

- [ ] **Step 10: Rodar e ver passar**

Run: `dotnet build Rastreamento.slnx -warnaserror` e `dotnet test Rastreamento.slnx -m:1`
Expected: 0 warnings; tudo verde.

- [ ] **Step 11: Specs**

1. Em `specs/01-dominio-e-regras-de-negocio.md`, depois da regra 32 e antes de "## Pontos ainda em aberto":

```markdown
*A regra 33 foi decidida em 2026-10-08, na spec da data de entrega do Pedido
(`docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`, decisões D2, D3, D4 e D12).*

33. **Todo Pedido tem data de entrega, e o atraso é derivado dela.**
    - **Obrigatória e livre (D2, D3).** A `DataEntrega` é informada no cadastro e pode ser editada em
      qualquer status. Aceita qualquer dia, inclusive anterior à abertura: um Pedido pode chegar ao
      sistema já atrasado, e um Retrabalho pode ser aberto pelo cliente bem depois de as peças terem
      sido expedidas. É um dia, sem hora nem fuso.
    - **Atrasado (D12).** Um Pedido está **atrasado** quando a `DataEntrega` é anterior a **hoje** e o
      status não é `Concluido` nem `Cancelado`. "Hoje" é a data corrente no horário de Brasília (GMT-3
      fixo, o mesmo da borda de fuso da aplicação). Um Pedido que vence hoje ainda **não** está atrasado.
      O atraso não é gravado: é calculado a cada leitura, no servidor.
    - **Retrabalho (D4, para a Fase 5).** O formulário do Pedido de Retrabalho traz, por padrão, a
      `DataEntrega` do Pedido de origem, e o campo é editável no cadastro.
```

2. Em `specs/05-api-endpoints.md`, seção "Pedido / Agrupamento":
   - `POST /pedidos`: o corpo vira `{ numero, cliente, dataEntrega }`, com `dataEntrega` em `"aaaa-mm-dd"`,
     obrigatória (ausente → 400), qualquer dia aceito.
   - `PUT /pedidos/{id}`: o corpo vira `{ numero, cliente, dataEntrega }`, substituição inteira, em
     qualquer status.
   - Em `GET /pedidos` (a frase que descreve cada item), acrescente: cada item traz `dataEntrega`
     (`"aaaa-mm-dd"`, sem hora nem fuso) e `atrasado` (regra 33, calculado no servidor); o mesmo vale para
     `GET /pedidos/{id}` e para as respostas de `POST` e `PUT`.

- [ ] **Step 12: Commit**

```bash
git add src/Rastreamento.Application src/Rastreamento.Api/Program.cs tests/Rastreamento.Application.Tests tests/Rastreamento.Api.Tests specs/01-dominio-e-regras-de-negocio.md specs/05-api-endpoints.md
git commit -m "feat(pedido): data de entrega no cadastro e na edicao, e o atraso decidido no servidor"
```

---

### Task 3: Ordem `entrega` como padrão e os cinco mais urgentes

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs`
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`ResumoDePedidosDto`)
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (`FakePedidoRepo`)
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDePedidoUseCaseTests.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/PedidoRepositoryTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`
- Modify: `specs/05-api-endpoints.md`

**Interfaces:**
- Consumes: `PrazoDeEntrega.StatusEncerrados` (Task 2); `NovoPedidoAsync`/`GravarPedidoAsync` com `dataEntrega` (Task 1).
- Produces:
  - `enum OrdemDePedidos { Recentes, Numero, Cliente, Entrega }`; `FiltroDePedidos(..., OrdemDePedidos Ordem = OrdemDePedidos.Entrega)`.
  - `IPedidoRepository.ListarMaisUrgentesAsync(IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct)` (substitui `ListarMaisAntigosAsync`).
  - `ResumoDePedidosDto(IReadOnlyList<ContagemDeStatusDto> PorStatus, IReadOnlyList<PedidoDto> MaisUrgentes)`.
  - HTTP: `GET /pedidos` sem `ordem` → `entrega`; `?ordem=entrega|recentes|numero|cliente`; resumo com `maisUrgentes`.

- [ ] **Step 1: Os testes de banco que falham**

Em `PedidoRepositoryTests.cs`:

1. Substitua o teste `Mais_antigos_deixa_encerrados_de_fora_e_para_no_limite` inteiro por:

```csharp
  [Fact]
  public async Task Mais_urgentes_deixa_encerrados_de_fora_ordena_por_prazo_e_para_no_limite()
  {
    var ids = new List<int>();
    await using var db = NovoContexto();
    try
    {
      // `ListarMaisUrgentesAsync` nao tem como ser escopado: le a tabela inteira. Por isso a asercao
      // vale com QUALQUER linha de terceiros no banco (inclusive sobra de execucao interrompida, ou um
      // Pedido gravado sem data, que vale 0001-01-01) e so compara as linhas deste teste com o que a
      // consulta devolveu.
      var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
      var chaves = new Dictionary<int, (DateOnly Prazo, DateTime Abertura)>();
      async Task<int> CriarAsync(string status, DateOnly prazo)
      {
        var id = await NovoPedidoAsync(db, "U-" + Unico(), "Cliente", status, abertura, prazo);
        ids.Add(id);   // entra na limpeza logo apos criar: nao ha janela de vazamento
        chaves[id] = (prazo, abertura);
        return id;
      }

      var abertos = new List<int>();
      var statusDosAbertos = new[] { "Aberto", "EmProducao", "AguardandoExpedicao", "Aberto", "EmProducao", "Aberto" };
      for (var i = 0; i < statusDosAbertos.Length; i++)
        abertos.Add(await CriarAsync(statusDosAbertos[i], new DateOnly(1990, 1, 1 + i)));
      // Encerrados com o prazo MAIS ANTIGO de todos: entrariam primeiro se nao fossem excluidos.
      var concluido = await CriarAsync("Concluido", new DateOnly(1989, 1, 1));
      var cancelado = await CriarAsync("Cancelado", new DateOnly(1989, 1, 2));

      var achados = await new PedidoRepository(db).ListarMaisUrgentesAsync(
          ["Concluido", "Cancelado"], 5, CancellationToken.None);

      // Para no limite: exatamente cinco, com seis candidatos nossos.
      Assert.Equal(5, achados.Count);
      // Prazo crescente, depois abertura, depois Id.
      Assert.Equal(
          achados.OrderBy(p => p.DataEntrega).ThenBy(p => p.DataAbertura).ThenBy(p => p.Id).Select(p => p.Id),
          achados.Select(p => p.Id));
      Assert.DoesNotContain(achados, p => p.Status is "Concluido" or "Cancelado");
      Assert.DoesNotContain(achados, p => p.Id == concluido || p.Id == cancelado);
      // Nenhuma linha nossa que devia entrar ficou de fora: as nossas ausentes rankeiam depois da quinta.
      var quinto = achados[^1];
      foreach (var id in abertos.Where(id => achados.All(p => p.Id != id)))
        Assert.True(
            (chaves[id].Prazo, chaves[id].Abertura, id).CompareTo((quinto.DataEntrega, quinto.DataAbertura, quinto.Id)) >= 0,
            $"Pedido {id} (aberto, prazo {chaves[id].Prazo:O}) devia ter entrado antes do quinto ({quinto.Id}, {quinto.DataEntrega:O}).");
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }
```

2. Acrescente:

```csharp
  [Fact]
  public async Task Entrega_poe_os_abertos_por_prazo_e_depois_os_encerrados_do_prazo_mais_recente()
  {
    // Cada mutacao da ordem troca o resultado: o encerrado de prazo MAIS ANTIGO de todos (ordenar so pela
    // data o poria no topo), dois encerrados (prazo crescente entre eles inverteria o par) e dois abertos
    // de mesmo prazo (o desempate por abertura decide). Escopado pela busca do cliente unico do teste.
    var cliente = $"cli-{Unico()}";
    var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var concluidoAntigo = await NovoPedidoAsync(db, "E1-" + Unico(), cliente, "Concluido", abertura, new DateOnly(2020, 1, 1));
      var canceladoRecente = await NovoPedidoAsync(db, "E2-" + Unico(), cliente, "Cancelado", abertura, new DateOnly(2020, 6, 1));
      var abertoTarde = await NovoPedidoAsync(db, "A1-" + Unico(), cliente, "Aberto", abertura, new DateOnly(2026, 12, 1));
      var empateAbertoDepois = await NovoPedidoAsync(db, "A2-" + Unico(), cliente, "EmProducao", abertura.AddHours(2), new DateOnly(2026, 11, 1));
      var empateAbertoAntes = await NovoPedidoAsync(db, "A3-" + Unico(), cliente, "Aberto", abertura.AddHours(1), new DateOnly(2026, 11, 1));
      ids.AddRange([concluidoAntigo, canceladoRecente, abertoTarde, empateAbertoDepois, empateAbertoAntes]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, ordem: OrdemDePedidos.Entrega), CancellationToken.None);

      Assert.Equal(
          [empateAbertoAntes, empateAbertoDepois, abertoTarde, canceladoRecente, concluidoAntigo],
          itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }

  [Fact]
  public async Task Entrega_desempata_abertos_por_Id_crescente_e_encerrados_por_Id_decrescente()
  {
    // Mesmo prazo e mesma abertura em cada par: so o Id decide, e em direcoes opostas.
    var cliente = $"cli-{Unico()}";
    var abertura = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var prazo = new DateOnly(2026, 11, 1);
    await using var db = NovoContexto();
    var ids = new List<int>();
    try
    {
      var aberto1 = await NovoPedidoAsync(db, "A1-" + Unico(), cliente, "Aberto", abertura, prazo);
      var aberto2 = await NovoPedidoAsync(db, "A2-" + Unico(), cliente, "Aberto", abertura, prazo);
      var encerrado1 = await NovoPedidoAsync(db, "E1-" + Unico(), cliente, "Concluido", abertura, prazo);
      var encerrado2 = await NovoPedidoAsync(db, "E2-" + Unico(), cliente, "Concluido", abertura, prazo);
      ids.AddRange([aberto1, aberto2, encerrado1, encerrado2]);

      var (itens, _) = await new PedidoRepository(db).ListarAsync(
          Filtro(busca: cliente, ordem: OrdemDePedidos.Entrega), CancellationToken.None);

      Assert.Equal([aberto1, aberto2, encerrado2, encerrado1], itens.Select(p => p.Id));
    }
    finally
    {
      await ApagarPedidosAsync(ids);
    }
  }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build Rastreamento.slnx -warnaserror`
Expected: FAIL — `OrdemDePedidos` não tem `Entrega` e `PedidoRepository` não tem `ListarMaisUrgentesAsync`.

- [ ] **Step 3: Domínio**

Em `IPedidoRepository.cs`:

```csharp
/// <summary>
/// Ordem de uma pagina de Pedidos. <c>Entrega</c> e a padrao (D10 da spec da data de entrega): nao
/// encerrados por prazo crescente, depois os encerrados por prazo decrescente. <c>Recentes</c> e
/// <c>DataAbertura</c> decrescente, depois <c>Id</c> decrescente.
/// </summary>
public enum OrdemDePedidos { Recentes, Numero, Cliente, Entrega }
```

O parâmetro padrão de `FiltroDePedidos` vira `OrdemDePedidos Ordem = OrdemDePedidos.Entrega`. O
`<summary>` de `ListarAsync` ganha a descrição de `Entrega` (a de **Global Constraints**). E
`ListarMaisAntigosAsync` é substituído por:

```csharp
  /// <summary>
  /// Os `quantos` Pedidos mais urgentes cujo status NAO esta em `foraDosStatus`: `DataEntrega`
  /// crescente (o mais atrasado primeiro), depois `DataAbertura` e `Id` crescentes.
  /// </summary>
  Task<IReadOnlyList<Pedido>> ListarMaisUrgentesAsync(
      IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct);
```

- [ ] **Step 4: Repositório**

Em `PedidoRepository.cs`, acrescente o campo:

```csharp
  // Os status em que o Pedido acabou, como no `CK_Pedido_Status`. A ordem `Entrega` os separa dentro do
  // SQL, e a Infrastructure nao referencia a Application, dona da lista da regra de atraso
  // (decisao P3 do plano da data de entrega).
  private static readonly string[] StatusEncerrados = ["Concluido", "Cancelado"];
```

O `switch` de `ListarAsync` ganha o caso, antes do `_`:

```csharp
      // Abertos por prazo crescente (o mais atrasado no topo), com abertura e Id crescentes no empate;
      // depois os encerrados por prazo DECRESCENTE e Id decrescente. Ordenar so pela data poria os
      // Concluidos antigos no topo. Cada chave condicional vale NULL no grupo em que nao se aplica,
      // entao nao interfere nele.
      OrdemDePedidos.Entrega => consulta
          .OrderBy(p => StatusEncerrados.Contains(p.Status) ? 1 : 0)
          .ThenBy(p => StatusEncerrados.Contains(p.Status) ? (DateOnly?)null : p.DataEntrega)
          .ThenByDescending(p => StatusEncerrados.Contains(p.Status) ? p.DataEntrega : (DateOnly?)null)
          .ThenBy(p => StatusEncerrados.Contains(p.Status) ? (DateTime?)null : p.DataAbertura)
          .ThenBy(p => StatusEncerrados.Contains(p.Status) ? -p.Id : p.Id),
```

e o comentário acima do `switch` deixa de dizer que `Recentes` é a padrão. `ListarMaisAntigosAsync` é
substituído por:

```csharp
  public async Task<IReadOnlyList<Pedido>> ListarMaisUrgentesAsync(
      IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct) =>
      await _db.Pedidos.AsNoTracking()
          .Where(p => !foraDosStatus.Contains(p.Status))
          .OrderBy(p => p.DataEntrega)
          .ThenBy(p => p.DataAbertura)
          .ThenBy(p => p.Id)
          .Take(quantos)
          .ToListAsync(ct);
```

- [ ] **Step 5: Rodar os testes de banco**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~PedidoRepositoryTests"`
Expected: os três testes novos PASSAM (o build da solução ainda falha nos testes de Application, que usam o
fake — o Step 6 resolve; se o `dotnet test` do projeto não compilar por isso, siga para o Step 6 e rode os
dois juntos no Step 8).

- [ ] **Step 6: Caso de uso, DTO e fake**

1. `Dtos.cs`:

```csharp
/// <remarks>
/// `PorStatus` traz SEMPRE os cinco status, na ordem do `CK_Pedido_Status`, zeros inclusive.
/// `MaisUrgentes` sao ate cinco Pedidos ainda nao encerrados, do prazo mais antigo ao mais novo (o mais
/// atrasado primeiro).
/// </remarks>
public sealed record ResumoDePedidosDto(
    IReadOnlyList<ContagemDeStatusDto> PorStatus,
    IReadOnlyList<PedidoDto> MaisUrgentes);
```

2. `CadastroDePedidoUseCase.cs`: o dicionário de ordens e a padrão:

```csharp
  // A ordem das chaves e a ordem da frase de erro de `Listar`; a padrao vem primeiro.
  private static readonly IReadOnlyDictionary<string, OrdemDePedidos> OrdensAceitas =
      new Dictionary<string, OrdemDePedidos>(StringComparer.Ordinal)
      {
        ["entrega"] = OrdemDePedidos.Entrega,
        ["recentes"] = OrdemDePedidos.Recentes,
        ["numero"] = OrdemDePedidos.Numero,
        ["cliente"] = OrdemDePedidos.Cliente,
      };
```

`var ordemDaConsulta = OrdemDePedidos.Recentes;` vira `OrdemDePedidos.Entrega`. `QuantosMaisAntigos` vira
`QuantosMaisUrgentes`. `Resumo` passa a chamar
`_repositorio.ListarMaisUrgentesAsync(PrazoDeEntrega.StatusEncerrados, QuantosMaisUrgentes, ct)` e a
montar `new ResumoDePedidosDto(..., await ProjetarComPausas(maisUrgentes, ct))`.

3. `Fakes.cs`, no `FakePedidoRepo`: `MaisAntigos`, `MaisAntigosForaDosStatus` e `MaisAntigosQuantos` viram
`MaisUrgentes`, `MaisUrgentesForaDosStatus` e `MaisUrgentesQuantos`; o método vira
`ListarMaisUrgentesAsync`; e o `switch` de `ListarAsync` ganha, antes do `_`, a mesma ordem do repositório real:

```csharp
      OrdemDePedidos.Entrega => _linhas
          .OrderBy(p => Encerrado(p) ? 1 : 0)
          .ThenBy(p => Encerrado(p) ? DateOnly.MinValue : p.DataEntrega)
          .ThenByDescending(p => Encerrado(p) ? p.DataEntrega : DateOnly.MinValue)
          .ThenBy(p => Encerrado(p) ? DateTime.MinValue : p.DataAbertura)
          .ThenBy(p => Encerrado(p) ? -p.Id : p.Id),
```

com o helper `private static bool Encerrado(Pedido p) => p.Status is "Concluido" or "Cancelado";`.

- [ ] **Step 7: Testes de Application e de API**

Em `CadastroDePedidoUseCaseTests.cs`:

1. `Listar_sem_ordem_pede_Recentes` vira `Listar_sem_ordem_pede_Entrega`, com
`Assert.Equal(OrdemDePedidos.Entrega, repo.UltimoFiltro!.Ordem);`.
2. `Listar_traduz_cada_ordem` ganha `[InlineData("entrega", OrdemDePedidos.Entrega)]`.
3. Em `Listar_com_ordem_desconhecida_e_Validacao_e_nao_consulta`, a frase esperada vira
`$"Ordem '{ordem}' desconhecida. Aceitas: entrega, recentes, numero, cliente."`.
4. `Resumo_pede_os_mais_antigos_fora_dos_encerrados_e_no_maximo_cinco` vira
`Resumo_pede_os_mais_urgentes_fora_dos_encerrados_e_no_maximo_cinco`, lendo `repo.MaisUrgentes`,
`repo.MaisUrgentesForaDosStatus`, `repo.MaisUrgentesQuantos` e `resumo.MaisUrgentes`.

Em `PedidosEndpointsTests.cs`:

1. `Listagem_sem_ordem_mantem_a_ordem_por_data_de_abertura` vira:

```csharp
  [Fact]
  public async Task Ordem_recentes_ordena_por_data_de_abertura()
  {
    // Numeros em ordem CONTRARIA a das datas: so a ordem por DataAbertura decrescente acerta.
    var cliente = $"cli-{Guid.NewGuid():N}";
    var tok = Guid.NewGuid().ToString("N")[..16];
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var antigo = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-a", t1);
    var recente = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-c", t1.AddHours(2));
    var meio = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-b", t1.AddHours(1));

    Assert.Equal([recente, meio, antigo], await IdsListadosAsync(cliente, "&ordem=recentes"));
  }

  [Fact]
  public async Task Listagem_sem_ordem_ordena_por_prazo_de_entrega()
  {
    // Prazos em ordem CONTRARIA a das aberturas: a ordem antiga (Recentes) devolveria o inverso.
    var cliente = $"cli-{Guid.NewGuid():N}";
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var prazoTarde = await GravarPedidoAsync(cliente, "Aberto", dataAbertura: t1, dataEntrega: new DateOnly(2026, 12, 1));
    var prazoMeio = await GravarPedidoAsync(cliente, "Aberto", dataAbertura: t1.AddHours(1), dataEntrega: new DateOnly(2026, 11, 1));
    var prazoCedo = await GravarPedidoAsync(cliente, "Aberto", dataAbertura: t1.AddHours(2), dataEntrega: new DateOnly(2026, 10, 1));

    Assert.Equal([prazoCedo, prazoMeio, prazoTarde], await IdsListadosAsync(cliente, ""));
    Assert.Equal([prazoCedo, prazoMeio, prazoTarde], await IdsListadosAsync(cliente, "&ordem=entrega"));
  }
```

2. Em `Ordem_desconhecida_responde_400_nomeando_o_valor`, a frase vira
`"Ordem 'Numero' desconhecida. Aceitas: entrega, recentes, numero, cliente."`.
3. Em `Resumo_conta_todos_os_Pedidos_alem_do_tamanho_de_pagina`, a última asserção vira
`Assert.True(corpo.GetProperty("maisUrgentes").GetArrayLength() <= 5);` e entra
`Assert.False(corpo.TryGetProperty("maisAntigosAbertos", out _));`.

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet build Rastreamento.slnx -warnaserror` e `dotnet test Rastreamento.slnx -m:1`
Expected: 0 warnings; tudo verde.

- [ ] **Step 9: `05`**

Em `specs/05-api-endpoints.md`:
- `GET /pedidos`: `?ordem=entrega|recentes|numero|cliente`; o bullet `ordem` passa a começar por
  "`entrega` (a padrão — também quando ausente, vazia ou só espaços)" com a descrição de **Global
  Constraints**; `recentes` deixa de ser "a padrão"; a frase do 400 vira `Aceitas: entrega, recentes, numero,
  cliente.`.
- `GET /pedidos/resumo`: `{ porStatus, maisUrgentes }`; `maisUrgentes` são até 5 Pedidos fora de
  `Concluido` e `Cancelado`, por `DataEntrega` crescente (desempate `DataAbertura` e `Id`) — o que a Home
  mostra em "Prazos de entrega". Registre que o campo se chamava `maisAntigosAbertos` até 2026-10-08.

- [ ] **Step 10: Commit**

```bash
git add src tests specs/05-api-endpoints.md
git commit -m "feat(pedido): ordem por prazo de entrega como padrao e os cinco mais urgentes no resumo"
```

---

### Task 4: Tom de atraso, tipos da API, `LinhaDePedido` e Home

**Files:**
- Modify: `web/src/index.css`, `web/src/tema/contraste.test.ts`
- Modify: `web/src/components/Pilula.tsx`, `web/src/components/Pilula.test.tsx`
- Modify: `web/src/api/cadastros.ts`, `web/src/api/cadastros.test.ts`
- Modify: `web/src/pedidos/LinhaDePedido.tsx`, `web/src/pedidos/LinhaDePedido.test.tsx`
- Modify: `web/src/pages/HomePage.tsx`, `web/src/pages/HomePage.test.tsx`
- Modify (fixtures de Pedido): `web/src/pages/PedidosPage.test.tsx`, `web/src/pages/PedidoDetalhePage.test.tsx`,
  `web/src/pedidos/ControleDePausa.test.tsx`
- Modify: `web/src/components/FiltroDeDemanda.test.tsx`, `web/src/execucao/BarraDoLote.test.tsx`
- Modify: `CLAUDE.md` (regra de cor)

**Interfaces:**
- Consumes: JSON `dataEntrega`, `atrasado` (Task 2) e `maisUrgentes` (Task 3).
- Produces:
  - `TomDePilula = 'neutro' | 'positivo' | 'negativo' | 'atencao' | 'atraso'`.
  - `PedidoDto.dataEntrega: string` e `PedidoDto.atrasado: boolean`; `ResumoDePedidosDto.maisUrgentes: PedidoDto[]`.
  - `formatarData(iso: string): string`.
  - `OrdemDePedidos = 'entrega' | 'recentes' | 'numero' | 'cliente'` (o tipo só; quem usa `'entrega'` é a Task 5).

- [ ] **Step 1: Os testes que falham**

1. `contraste.test.ts`: em `PARES`, depois das duas linhas de `atencao-texto`:

```ts
  { frente: 'atraso-texto', fundo: 'atraso-fundo', minimo: TEXTO, onde: 'texto da pílula de atraso do Pedido' },
  { frente: 'atraso-texto', fundo: 'superficie', minimo: TEXTO, onde: 'rótulo de atraso sobre cartão' },
```

e, na lista de `declara todos os tokens que o plano da fase fixou`, `'atraso-texto', 'atraso-fundo',` depois
de `'atencao-fundo',`.

2. `Pilula.test.tsx`:

```tsx
  it('o tom atraso usa o par roxo declarado, sem verde, vermelho, ambar nem a tinta de acao', () => {
    // Token a token, pelo mesmo motivo do teste do tom neutro. Roxo e o quarto estado reservado: so
    // Pedido atrasado (D5 da spec da data de entrega).
    render(<Pilula tom="atraso">Atrasado</Pilula>)

    const classes = screen.getByText('Atrasado').className.split(/\s+/)
    expect(classes).toContain('bg-atraso-fundo')
    expect(classes).toContain('text-atraso-texto')
    expect(classes.some((c) => /positivo-|negativo-|atencao-|acao/.test(c))).toBe(false)
    expect(classes.some((c) => c.includes('/'))).toBe(false)
  })
```

3. `cadastros.test.ts`: importe `formatarData` e acrescente:

```ts
  it('formatarData corta a string e nao passa por Date, que em Brasilia cairia no dia anterior', () => {
    const fusoAnterior = process.env.TZ
    process.env.TZ = 'America/Sao_Paulo'
    try {
      // Controle positivo: neste fuso, o caminho por `Date` erra o dia. Sem ele, o teste passaria numa
      // máquina em UTC mesmo com a implementação trocada por `new Date(...)`.
      expect(new Date('2026-10-22').getDate()).toBe(21)
      expect(formatarData('2026-10-22')).toBe('22/10/2026')
    } finally {
      if (fusoAnterior === undefined) delete process.env.TZ
      else process.env.TZ = fusoAnterior
    }
  })
```

No `obterResumoDePedidos le /pedidos/resumo`, `maisAntigosAbertos: []` vira `maisUrgentes: []`.

4. `LinhaDePedido.test.tsx`: o `PEDIDO` ganha `dataEntrega: '2026-10-22', atrasado: false,` e entram:

```tsx
  it('mostra o prazo de entrega antes da data de abertura', () => {
    renderizar(PEDIDO)

    expect(screen.getByText(/entrega em 22\/10\/2026 · aberto em 01\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra a pilula Atrasado so quando o servidor diz que esta atrasado', () => {
    // A tela nao recalcula o atraso: um Pedido de prazo vencido com `atrasado: false` (encerrado) nao
    // ganha pilula.
    renderizar({ ...PEDIDO, dataEntrega: '2020-01-01', atrasado: false })
    expect(screen.queryByText('Atrasado')).toBeNull()
    cleanup()

    renderizar({ ...PEDIDO, atrasado: true })
    expect(screen.getByText('Atrasado')).toBeTruthy()
  })

  it('a pilula Atrasado usa o tom roxo, e nao o vermelho nem o ambar', () => {
    renderizar({ ...PEDIDO, atrasado: true })

    const classes = screen.getByText('Atrasado').className.split(/\s+/)
    expect(classes).toContain('bg-atraso-fundo')
    expect(classes).toContain('text-atraso-texto')
    expect(classes.some((c) => /negativo-|atencao-/.test(c))).toBe(false)
  })
```

5. `HomePage.test.tsx`:
   - o helper `pedido(...)` devolve também `dataEntrega: '2026-10-22', atrasado: false`;
   - `resumoCom` passa a receber `maisUrgentes` e devolvê-lo com esse nome;
   - `MAIS_ANTIGOS` vira `MAIS_URGENTES`, e o comentário dela passa a dizer "Na ordem em que o servidor os
     manda: do prazo mais antigo ao mais novo, só os não encerrados.";
   - todo `{ name: 'Pedidos abertos há mais tempo' }` vira `{ name: 'Prazos de entrega' }`;
   - o teste `mostra os mais antigos na ordem em que o resumo os manda` vira `mostra os mais urgentes na
     ordem em que o resumo os manda`, e o comentário acima dele cita
     `PedidoRepositoryTests.Mais_urgentes_deixa_encerrados_de_fora_ordena_por_prazo_e_para_no_limite`;
   - acrescente:

```tsx
  it('a secao de pedidos se chama Prazos de entrega', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByRole('heading', { name: 'Prazos de entrega' })).toBeTruthy()
    expect(screen.queryByText('Pedidos abertos há mais tempo')).toBeNull()
  })
```

6. Fixtures: em `PedidosPage.test.tsx`, `PedidoDetalhePage.test.tsx` e `ControleDePausa.test.tsx`, todo objeto
de Pedido (os que têm `dataAbertura:`) ganha `dataEntrega: '2026-10-22', atrasado: false,`. Confira:

```bash
grep -rln "dataAbertura:" web/src --include=*.test.ts --include=*.test.tsx
```

Todo arquivo listado precisa ter `dataEntrega` em cada fixture de Pedido.

7. `FiltroDeDemanda.test.tsx` (`pilula de filtro nao usa cor de estado`) e `BarraDoLote.test.tsx` (`barra nao usa
cor de estado`): a regex de cor de estado ganha `atraso` (`/positivo|negativo|atencao|atraso/`).

- [ ] **Step 2: Rodar e ver falhar**

Run: `cd web && npm test -- --run`
Expected: FAIL — token `atraso-*` ausente, `formatarData` inexistente, a linha sem "entrega em", a Home sem
"Prazos de entrega".

- [ ] **Step 3: Tema e `Pilula`**

`index.css`, dentro do `@theme`, logo depois de `--color-atencao-fundo`:

```css
  --color-atraso-texto: #6B21A8;     /* rótulo de estado de atraso do Pedido (roxo escuro): 7,39:1 sobre a tinta */
  --color-atraso-fundo: #F3E8FF;     /* tinta da pílula de atraso (lilás claro); valor FIXO, não composto */
```

`Pilula.tsx`: o tipo ganha `'atraso'`, o mapa ganha `atraso: 'bg-atraso-fundo text-atraso-texto',`, o
comentário do mapa diz "Os cinco tons", e o JSDoc termina em: "Verde, vermelho, âmbar e roxo ficam
reservados a estado de verdade: aprovado ou ativo, reprovado, perda ou erro, atenção (`atencao` — pede
cuidado sem ser erro nem perda) e atraso do Pedido (`atraso`)."

- [ ] **Step 4: API**

Em `cadastros.ts`:

```ts
export interface PedidoDto {
  id: number
  numero: string
  cliente: string
  tipo: string
  status: string
  /** ISO 8601 com offset -03:00 — a API ja converteu (HorarioDeBrasiliaJsonConverter). */
  dataAbertura: string
  /** Dia do prazo, `aaaa-mm-dd`, sem hora nem fuso (`DateOnly` no servidor). Exibir com `formatarData`. */
  dataEntrega: string
  /** Decidido no servidor com o "hoje" de Brasília (regra 33 do `01`); a tela só desenha a pílula. */
  atrasado: boolean
  criadoPorUsuarioId: number
  /** `null` quando o Pedido não está pausado. */
  pausa: PausaResumoDto | null
}
```

```ts
/**
 * `"2026-10-22"` → `22/10/2026`, cortando a string. NÃO passa por `Date`: `new Date("2026-10-22")` é
 * lido como meia-noite UTC e, num navegador em Brasília, cai no dia 21.
 */
export function formatarData(iso: string): string {
  const [ano, mes, dia] = iso.split('-')
  return `${dia}/${mes}/${ano}`
}
```

`OrdemDePedidos` vira `'entrega' | 'recentes' | 'numero' | 'cliente'` (o comentário dela e o de
`FiltroDePedidos.ordem` não mudam nesta task: a padrão da tela muda na Task 5). O `ResumoDePedidosDto`:

```ts
/**
 * O que a Home mostra dos Pedidos, contado no servidor sobre TODOS eles: `porStatus` traz sempre os
 * cinco status, na ordem do `CK_Pedido_Status`, zeros inclusive; `maisUrgentes` são até cinco Pedidos
 * fora de `Concluido`/`Cancelado`, do prazo mais antigo ao mais novo (o mais atrasado primeiro).
 */
export interface ResumoDePedidosDto {
  porStatus: ContagemDeStatusDto[]
  maisUrgentes: PedidoDto[]
}
```

- [ ] **Step 5: `LinhaDePedido` e `HomePage`**

`LinhaDePedido.tsx`: importe `formatarData`, e o `<span>` de baixo vira:

```tsx
      <span className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
        <Pilula tom={tomDoStatus(pedido.status)}>{rotuloDoStatus(pedido.status)}</Pilula>
        {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
        {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
        entrega em {formatarData(pedido.dataEntrega)} · aberto em {formatarDataHora(pedido.dataAbertura)}
      </span>
```

e o JSDoc da função passa a dizer "número, cliente, status, prazo de entrega e data de abertura" e "a seção
"Prazos de entrega" da `HomePage` (os cinco mais urgentes)".

`HomePage.tsx`: `maisAntigos` vira `urgentes`, lido de `resumo?.maisUrgentes ?? null`; o comentário dele
passa a dizer que a regra (só os não encerrados, do prazo mais antigo ao mais novo, no máximo cinco) é do
servidor; o `<h2>` e o `rotulo` da `ListaDeCadastro` viram `Prazos de entrega`; todos os comentários que
citam `maisAntigos` passam a citar `urgentes`.

- [ ] **Step 6: Rodar e ver passar**

Run: `cd web && npm test -- --run` e `npm run build`
Expected: tudo verde; build limpo.

- [ ] **Step 7: `CLAUDE.md`**

Na seção "Interface", o bullet "Cor de identidade nunca significa estado; cor de estado nunca decora" passa a
listar o quarto estado: "Verde (`positivo`), vermelho (`negativo`), âmbar (`atencao`) e roxo (`atraso`) são
reservados a aprovado/ativo, reprovado/perda/erro, atenção (hoje, Pedido pausado) e Pedido atrasado (desde a
data de entrega, D5 da spec `docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`: a mesma
linha pode estar reprovada **e** atrasada, e as duas em vermelho não se distinguiriam)." No bullet do
`FiltroDeDemanda`, "(`positivo`, `negativo`, `atencao`)" vira "(`positivo`, `negativo`, `atencao`,
`atraso`)".

- [ ] **Step 8: Commit**

```bash
git add web/src CLAUDE.md
git commit -m "feat(web): pilula Atrasado em tom proprio, prazo na linha do Pedido e Prazos de entrega na Home"
```

---

### Task 5: Cadastro com data e a lista aberta por prazo

**Files:**
- Modify: `web/src/api/cadastros.ts`, `web/src/api/cadastros.test.ts`
- Modify: `web/src/pages/PedidosPage.tsx`, `web/src/pages/PedidosPage.test.tsx`
- Modify: `CLAUDE.md` (bullet do `SeletorDeOrdem`)
- Modify: `docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md` (nota de emenda)

**Interfaces:**
- Consumes: `OrdemDePedidos` com `'entrega'` (Task 4); `POST /pedidos` com `dataEntrega` (Task 2); `GET /pedidos` com padrão `entrega` (Task 3).
- Produces: `NovoPedido = { numero: string; cliente: string; dataEntrega: string }`; `listarPedidos` não manda `ordem` quando ela é `'entrega'`.

- [ ] **Step 1: Os testes da API que falham**

Em `cadastros.test.ts`:
1. `listarPedidos nao manda ordem quando e a padrao`: o caso `ordem: 'recentes'` vira `ordem: 'entrega'`, e o
   comentário passa a dizer que a padrão é `entrega` desde a data de entrega.
2. Acrescente:

```ts
  it('listarPedidos manda ordem=recentes, que deixou de ser a padrao', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 })))
    vi.stubGlobal('fetch', fetchMock)

    await listarPedidos({ busca: '', status: [], material: [], pagina: 1, tamanho: 20, ordem: 'recentes' })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos?busca=&pagina=1&tamanho=20&ordem=recentes')
  })
```

(use o mesmo stub de `fetch` e a mesma inicialização dos testes vizinhos de `listarPedidos`).
3. Os `criarPedido({ numero: 'PED-001', cliente: 'Cliente X' })` ganham `dataEntrega: '2026-10-22'`, e o teste
   que afirma o corpo do `POST` afirma também `dataEntrega: '2026-10-22'`.

- [ ] **Step 2: Os testes da tela que falham**

Em `PedidosPage.test.tsx`:

1. `preencherEEnviar` ganha a data:

```tsx
function preencherEEnviar(numero: string, cliente: string, dataEntrega = '2026-10-22') {
  fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: numero } })
  fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: cliente } })
  fireEvent.change(screen.getByLabelText('Data de entrega'), { target: { value: dataEntrega } })
  fireEvent.click(screen.getByRole('button', { name: 'Abrir pedido' }))
}
```

2. Acrescente:

```tsx
  it('o painel pede a data de entrega, obrigatoria, e a manda no corpo do POST', async () => {
    const fetchMock = apiComPost(() => respostaJson({ ...PEDIDO, id: 2, numero: 'PED-002' }, 201))
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await screen.findByText('PED-001')
    await abrirNovoPedido()
    const campo = screen.getByLabelText('Data de entrega') as HTMLInputElement
    expect(campo.type).toBe('date')
    expect(campo.required).toBe(true)
    preencherEEnviar('PED-002', 'Fábrica Beta', '2026-11-30')

    await waitFor(() => expect(aposOPost(fetchMock)).toHaveLength(1))
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(JSON.parse(String(post[1]!.body))).toEqual({ numero: 'PED-002', cliente: 'Fábrica Beta', dataEntrega: '2026-11-30' })
  })

  it('a lista abre por prazo de entrega, sem ordem na URL nem na requisicao', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await screen.findByText('PED-001')

    const seletor = screen.getByLabelText('Ordenar por') as HTMLSelectElement
    expect(seletor.selectedOptions[0].textContent).toBe('Prazo de entrega')
    expect(localizacao()).toBe('/pedidos')
    for (const url of listagens(fetchMock)) expect(url.searchParams.has('ordem')).toBe(false)
  })

  it('escolher Mais recentes poe ordem=recentes na URL e na requisicao', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await screen.findByText('PED-001')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'recentes' } })

    await waitFor(() => expect(localizacao()).toBe('/pedidos?ordem=recentes'))
    await waitFor(() => expect(listagens(fetchMock).at(-1)!.searchParams.get('ordem')).toBe('recentes'))
  })
```

3. Ajuste os testes que fixavam "Mais recentes" como padrão:
   - `salvar com sucesso limpa a URL inteira e a consulta volta ao padrao` vira `salvar com sucesso zera a
     consulta e poe a lista em Mais recentes`: espera `localizacao()` igual a `'/pedidos?ordem=recentes'`
     (nos dois lugares), `ultima.searchParams.get('ordem')` igual a `'recentes'` (no lugar de
     `has('ordem')` falso) e o seletor em `'Mais recentes'`; o comentário acima do teste passa a citar
     D11 da spec da data de entrega.
   - `depois de salvar, uma nova ordem e uma nova faceta ainda chegam a URL e a requisicao`: a primeira
     espera de localização vira `'/pedidos?ordem=recentes'`; as seguintes não mudam.
   - `salvar com sucesso na consulta padrao ainda recarrega, uma vez` vira `salvar com sucesso ja em Mais
     recentes ainda recarrega, uma vez`, com `renderizar('/pedidos?ordem=recentes')`: é o caso em que nada
     da consulta muda e a recarga tem de ser forçada.
   - `ordem desconhecida na URL nao vai ao servidor`: o seletor espera `'Prazo de entrega'`.
   - `escolher Mais recentes tira ordem da URL` vira `escolher Prazo de entrega tira ordem da URL`, com
     `value: 'entrega'` no `fireEvent.change`.
   - `escolher Cliente poe ordem=cliente na URL sem criar entrada de historico`: a lista de opções esperada
     vira `['Prazo de entrega', 'Mais recentes', 'Número (A→Z)', 'Cliente (A→Z)']`.
   - `Cancelar fecha o painel e descarta o digitado` e `limpa o formulário e recarrega a lista depois de abrir
     um pedido`: afirmam também que `Data de entrega` volta vazia.

- [ ] **Step 3: Rodar e ver falhar**

Run: `cd web && npm test -- --run src/api/cadastros.test.ts src/pages/PedidosPage.test.tsx`
Expected: FAIL — sem campo "Data de entrega", a padrão ainda "Mais recentes".

- [ ] **Step 4: API**

Em `cadastros.ts`:

```ts
export interface NovoPedido {
  numero: string
  cliente: string
  /** `aaaa-mm-dd`, o valor do `<input type="date">`; vazio só no formulário em branco. */
  dataEntrega: string
}
```

```ts
/** Ordem de `GET /pedidos`; `'entrega'` é a padrão do servidor e não vai na URL (D10 da spec da data de entrega). */
export type OrdemDePedidos = 'entrega' | 'recentes' | 'numero' | 'cliente'
```

Em `FiltroDePedidos`, o comentário de `ordem` vira "Ausente ou `'entrega'`: o parâmetro não vai, e o servidor
aplica a padrão." Em `listarPedidos`:

```ts
  if (f.ordem !== undefined && f.ordem !== 'entrega') params.set('ordem', f.ordem)
```

- [ ] **Step 5: A tela**

Em `PedidosPage.tsx`:

```tsx
const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '', dataEntrega: '' }
```

```tsx
// A padrão vem primeiro (D10 da spec da data de entrega): a lista abre por prazo.
const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<OrdemDePedidos>[] = [
  { valor: 'entrega', rotulo: 'Prazo de entrega' },
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'numero', rotulo: 'Número (A→Z)' },
  { valor: 'cliente', rotulo: 'Cliente (A→Z)' },
]
```

`ordemDaUrl` devolve `'entrega'` no lugar de `'recentes'`. Em `mudarOrdem`, `if (nova === 'recentes')
proxima.delete('ordem')` vira `if (nova === 'entrega') proxima.delete('ordem')`, e o comentário acima diz
"escolher "Prazo de entrega" apaga o parâmetro". `concluirComSucesso`:

```tsx
  // Desfecho de sucesso: fecha o painel, zera busca, página e facetas e põe a lista em "Mais recentes",
  // para o pedido novo aparecer no topo — não na padrão, por prazo, em que um prazo distante o mandaria
  // para o fim (D11 da spec da data de entrega, emenda da decisão 7 da 1F). O router aplica a mudança de
  // URL em `startTransition`, e a seleção e a ordem saem da URL; se o `voltarAoInicio` ficasse de fora,
  // o estado do hook (urgente) commitaria antes da URL: a lista buscaria duas vezes, e o efeito do hook
  // que copia a consulta para a URL partiria da URL ainda antiga e a reescreveria. Os dois juntos na
  // mesma transição commitam num render só: uma requisição, com a URL já zerada.
  function concluirComSucesso() {
    fecharPainel()
    startTransition(() => {
      setParams(new URLSearchParams({ ordem: 'recentes' }), { replace: true })
      lista.voltarAoInicio()
    })
  }
```

No painel, depois do `Campo` "Cliente", dentro do mesmo `grid`:

```tsx
            <Campo rotulo="Data de entrega">
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={form.dataEntrega}
                  onChange={(e) => setForm({ ...form, dataEntrega: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
```

- [ ] **Step 6: Rodar e ver passar**

Run: `cd web && npm test -- --run` e `npm run build`
Expected: tudo verde; build limpo.

- [ ] **Step 7: `CLAUDE.md` e a nota na spec da 1F**

1. No bullet "Ordenar uma lista de cadastro usa `SeletorDeOrdem`" do `CLAUDE.md`:
   - "**A padrão é "Mais recentes" nas quatro telas**" vira "**A padrão é "Mais recentes" em três telas, e
     "Prazo de entrega" em Pedidos**" — com a explicação: Pedidos abre por prazo desde a data de entrega
     (D10 da spec `docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`).
   - "**Salvar com sucesso devolve a consulta ao padrão**" ganha a emenda: em Pedidos, salvar zera busca,
     filtros e página e põe a ordem em "Mais recentes", escrita na URL (`?ordem=recentes`), para o Pedido
     novo aparecer no topo (D11 daquela spec).
   - "a padrão não é escrita lá (`/pedidos` limpa é "Mais recentes")" vira "(`/pedidos` limpa é "Prazo de
     entrega")".
2. Na spec da 1F, logo depois da lista "### De 2026-10-01 (releitura)" (antes de "## O desenho — painel sob
   demanda"), acrescente:

```markdown
> **Emenda de 2026-10-08 (só para Pedidos).** As decisões 7 e 9 foram emendadas pela spec da data de
> entrega (`docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`, D10 e D11): a lista de
> Pedidos abre por "Prazo de entrega", e salvar a põe em "Mais recentes" em vez de devolvê-la à padrão. As
> outras três telas não mudam. O texto acima fica como foi decidido.
```

- [ ] **Step 8: Commit**

```bash
git add web/src CLAUDE.md docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md
git commit -m "feat(web): data de entrega no cadastro de Pedido e a lista aberta por prazo"
```

---

### Task 6: Prazo e edição na página do Pedido

**Files:**
- Modify: `web/src/api/cadastros.ts`, `web/src/api/cadastros.test.ts`
- Modify: `web/src/pages/PedidoDetalhePage.tsx`, `web/src/pages/PedidoDetalhePage.test.tsx`
- Modify: `CLAUDE.md` (contagens de `<PainelDeEscrita` e `useDevolverFoco(`)

**Interfaces:**
- Consumes: `NovoPedido` com `dataEntrega` (Task 5); `formatarData` e o tom `atraso` (Task 4); `PUT /pedidos/{id}` com `dataEntrega` (Task 2).
- Produces: `editarPedido(id: number, p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro>`.

- [ ] **Step 1: O teste da API que falha**

Em `cadastros.test.ts`, importe `editarPedido` e acrescente (com o stub de `fetch` e a inicialização dos
testes vizinhos de `criarPedido`):

```ts
  it('editarPedido faz PUT na rota do id com os tres campos', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(respostaJson({ id: 7 })))
    vi.stubGlobal('fetch', fetchMock)

    await editarPedido(7, { numero: 'PED-007', cliente: 'Cliente X', dataEntrega: '2026-11-30' })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos/7')
    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.method).toBe('PUT')
    expect(JSON.parse(String(init.body))).toEqual({ numero: 'PED-007', cliente: 'Cliente X', dataEntrega: '2026-11-30' })
  })

  it('editarPedido devolve o conflito de numero duplicado', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson(
      { erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 3 }, 409))))

    const resultado = await editarPedido(7, { numero: 'PED-003', cliente: 'Cliente X', dataEntrega: '2026-11-30' })

    expect(ehConflito(resultado)).toBe(true)
  })
```

- [ ] **Step 2: Os testes da tela que falham**

Em `PedidoDetalhePage.test.tsx`, acrescente um mock que separa o `PUT` do `GET` (os dois caem em
`/api/pedidos/7`, e o `fetchPorRota` casa só por caminho):

```tsx
// O PUT de editar e o GET do cabeçalho caem no mesmo caminho: o mock separa pelo método.
function apiComPut(put: () => Response | Promise<Response>) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/pedidos/7') return Promise.resolve(init?.method === 'PUT' ? put() : respostaJson(PEDIDO))
    if (caminho === '/api/pedidos/7/agrupamentos') return Promise.resolve(respostaJson([AGRUPAMENTO]))
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
}
```

e os testes:

```tsx
  it('mostra o prazo de entrega e a pilula Atrasado so quando o servidor diz que esta atrasado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson({ ...PEDIDO, atrasado: true }),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText(/entrega em 22\/10\/2026/)).toBeTruthy()
    const pilula = screen.getByText('Atrasado')
    expect(pilula.className.split(/\s+/)).toContain('text-atraso-texto')
    cleanup()

    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))
    renderizarDetalhe()
    await screen.findByText(/entrega em 22\/10\/2026/)
    expect(screen.queryByText('Atrasado')).toBeNull()
  })

  describe('editar o pedido', () => {
    it('quem nao escreve pedidos nao ve o Editar pedido', async () => {
      perfil = 'Operador'
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))

      renderizarDetalhe()
      await screen.findByText('Fábrica Alfa')

      expect(screen.queryByRole('button', { name: 'Editar pedido' })).toBeNull()
    })

    it('abre o painel com numero, cliente e prazo preenchidos', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))

      const painel = screen.getByRole('form', { name: 'Editar pedido' })
      expect((within(painel).getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('PED-001')
      expect((within(painel).getByLabelText('Cliente') as HTMLInputElement).value).toBe('Fábrica Alfa')
      expect((within(painel).getByLabelText('Data de entrega') as HTMLInputElement).value).toBe('2026-10-22')
    })

    it('salvar manda o PUT com os tres campos, aplica a resposta e devolve o foco ao Editar pedido', async () => {
      const editado = { ...PEDIDO, numero: 'PED-001-A', dataEntrega: '2026-12-01', atrasado: false }
      const fetchMock = apiComPut(() => respostaJson(editado))
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001-A' } })
      fireEvent.change(screen.getByLabelText('Data de entrega'), { target: { value: '2026-12-01' } })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      await waitFor(() => expect(screen.queryByRole('form', { name: 'Editar pedido' })).toBeNull())
      const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT')!
      expect(JSON.parse(String(put[1]!.body))).toEqual({ numero: 'PED-001-A', cliente: 'Fábrica Alfa', dataEntrega: '2026-12-01' })
      expect(screen.getByText(/entrega em 01\/12\/2026/)).toBeTruthy()
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar pedido' }))
      // A resposta do PUT é o Pedido novo: sem segundo GET do cabeçalho (decisão P2 do plano).
      expect(fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/pedidos/7' && init?.method !== 'PUT'))
        .toHaveLength(1)
    })

    it('numero duplicado mantem o painel aberto com a mensagem dentro dele', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(
        { erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 3 }, 409)))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-003' } })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      const painel = await screen.findByRole('form', { name: 'Editar pedido' })
      expect(await within(painel).findByText('Já existe um pedido com este número.')).toBeTruthy()
      expect((within(painel).getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('PED-003')
    })

    it('com a edicao em voo, Cancelar fica desabilitado', async () => {
      vi.stubGlobal('fetch', apiComPut(() => new Promise<Response>(() => {})))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      await screen.findByText('Salvando…')
      expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('Cancelar fecha sem salvar e devolve o foco ao Editar pedido', async () => {
      const fetchMock = apiComPut(() => respostaJson(PEDIDO))
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

      expect(screen.queryByRole('form', { name: 'Editar pedido' })).toBeNull()
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar pedido' }))
    })
  })
```

(importe `within` de `@testing-library/react`.)

- [ ] **Step 3: Rodar e ver falhar**

Run: `cd web && npm test -- --run src/api/cadastros.test.ts src/pages/PedidoDetalhePage.test.tsx`
Expected: FAIL — `editarPedido` inexistente, sem "Editar pedido", sem "entrega em".

- [ ] **Step 4: API**

Em `cadastros.ts`, substitua o comentário "Sem editarPedido aqui, de proposito: …" por:

```ts
/** `PUT` é substituição inteira: número, cliente e data de entrega vão sempre juntos. */
export function editarPedido(id: number, p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch(`/pedidos/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}
```

- [ ] **Step 5: A tela**

Em `PedidoDetalhePage.tsx`:

1. Imports: `useRef` de `react`; `editarPedido`, `formatarData` e `type NovoPedido` de `../api/cadastros`;
`useDevolverFoco` de `../hooks/useDevolverFoco`; `PainelDeEscrita` de `../components/PainelDeEscrita`.

2. Estado e handlers, logo depois de `const podeEscrever = usePodeEscrever('agrupamentos')`:

```tsx
  // A edição do Pedido é do recurso `pedidos` (PCP e Administrador), não do `agrupamentos` da lista abaixo.
  const podeEditarPedido = usePodeEscrever('pedidos')
  const [edicaoAberta, setEdicaoAberta] = useState(false)
  const [formDoPedido, setFormDoPedido] = useState<NovoPedido>({ numero: '', cliente: '', dataEntrega: '' })
  const [erroDeEdicao, setErroDeEdicao] = useState<string | null>(null)
  const [salvandoPedido, setSalvandoPedido] = useState(false)

  // O "Editar pedido" some com o painel aberto; ao fechar, o foco volta a ele.
  const botaoEditar = useRef<HTMLButtonElement>(null)
  useDevolverFoco(edicaoAberta, () => botaoEditar.current)

  function abrirEdicao() {
    if (!pedido) return
    setFormDoPedido({ numero: pedido.numero, cliente: pedido.cliente, dataEntrega: pedido.dataEntrega })
    setErroDeEdicao(null)
    setEdicaoAberta(true)
  }

  function fecharEdicao() {
    setEdicaoAberta(false)
    setErroDeEdicao(null)
  }

  // A resposta do PUT é o Pedido inteiro (o mesmo DTO do GET, com pausa e atraso): a tela a aplica
  // direto, sem recarregar o cabeçalho nem os Agrupamentos.
  async function salvarPedido(e: FormEvent) {
    e.preventDefault()
    setErroDeEdicao(null)
    setSalvandoPedido(true)
    try {
      const resultado = await editarPedido(pedidoId, formDoPedido)
      if (ehConflito(resultado)) {
        // Pedido não tem reativação (não há coluna Ativo): o caminho é corrigir o número.
        setErroDeEdicao('Já existe um pedido com este número.')
        return
      }
      setPedido(resultado)
      fecharEdicao()
    } catch (e) {
      setErroDeEdicao(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
    } finally {
      setSalvandoPedido(false)
    }
  }
```

3. O `<Pagina>` ganha a ação, e o painel entra logo no começo do conteúdo, antes do cartão do cabeçalho:

```tsx
    <Pagina
      titulo={pedido ? pedido.numero : 'Pedido'}
      acao={podeEditarPedido && pedido && !edicaoAberta && (
        <Botao ref={botaoEditar} variante="secundario" onClick={abrirEdicao}>Editar pedido</Botao>
      )}
    >
      {podeEditarPedido && edicaoAberta && (
        <PainelDeEscrita titulo="Editar pedido" aoEnviar={salvarPedido} aoFechar={fecharEdicao} enviando={salvandoPedido}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do pedido">
              {(id) => (
                <input
                  id={id}
                  value={formDoPedido.numero}
                  onChange={(e) => setFormDoPedido({ ...formDoPedido, numero: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Cliente">
              {(id) => (
                <input
                  id={id}
                  value={formDoPedido.cliente}
                  onChange={(e) => setFormDoPedido({ ...formDoPedido, cliente: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
            <Campo rotulo="Data de entrega">
              {(id) => (
                <input
                  id={id}
                  type="date"
                  value={formDoPedido.dataEntrega}
                  onChange={(e) => setFormDoPedido({ ...formDoPedido, dataEntrega: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <BannerDeErro mensagem={erroDeEdicao} />
          <Botao type="submit" carregando={salvandoPedido} rotuloCarregando="Salvando…" className="self-start">
            Salvar
          </Botao>
        </PainelDeEscrita>
      )}
```

4. A linha das pílulas do cabeçalho (decisão P7):

```tsx
          <p className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
            <Pilula>{pedido.tipo}</Pilula>
            <Pilula>{rotuloDoStatus(pedido.status)}</Pilula>
            {pedido.pausa && <Pilula tom="atencao">Pausado</Pilula>}
            {pedido.atrasado && <Pilula tom="atraso">Atrasado</Pilula>}
            entrega em {formatarData(pedido.dataEntrega)} · aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
```

- [ ] **Step 6: Rodar e ver passar**

Run: `cd web && npm test -- --run` e `npm run build`
Expected: tudo verde; build limpo. Se algum teste antigo da página achar dois "Cliente" ou dois
"Código do pedido", é porque abriu o painel: os testes antigos não abrem, então isso não deve acontecer —
se acontecer, pare e reporte.

- [ ] **Step 7: `CLAUDE.md` — as contagens que esta task muda**

Remeça, com os comandos que o próprio `CLAUDE.md` registra, e atualize os números e a data (2026-10-08 ou a
data da medição), dizendo o que mudou e por quê (a `PedidoDetalhePage` ganhou um painel):

```bash
grep -rn "<PainelDeEscrita" web/src --include=*.tsx | grep -v "\.test\."
grep -rn "useDevolverFoco(" web/src --include=*.tsx | grep -v "\.test\."
```

Esperado: 8 linhas de `<PainelDeEscrita` (eram 7), 7 telas consumidoras (eram 6) e 9 painéis que a tela pode
abrir (eram 8); 8 chamadas de `useDevolverFoco(` (eram 7), em 7 telas (eram 6). Se o número medido for
outro, escreva o medido e reporte a diferença. No bullet do `PainelDeEscrita`, acrescente a `PedidoDetalhePage`
à lista de telas e ao parágrafo da origem do foco ("na `PedidoDetalhePage`, o "Editar pedido"").

- [ ] **Step 8: Commit**

```bash
git add web/src CLAUDE.md
git commit -m "feat(web): prazo e edicao de numero, cliente e data na pagina do Pedido"
```

---

### Task 7: Conferência no navegador (do usuário)

**Dispensa de review, escrita antes:** o produto desta task é o relatório da conferência e as decisões do
usuário, não código. É a primeira classe de dispensa do `CLAUDE.md` ("task cujo produto não é código"). O
controlador escreve esta justificativa no ledger e no relatório **antes** de começar.

**Quem faz é o usuário** (regra de 2026-10-07). O controlador prepara e para:

- [ ] **Step 1: Preparar a bancada**

Peça `request_keep_awake` antes de subir os servidores (o PC suspende e derruba os dev servers). Suba a API e
o front pelo `preview_start` (`.claude/launch.json`). Confira que o banco tem a coluna (Task 1, Step 6) e que
o usuário `operador` existe (seção "Pré-requisito externo dos testes" do `CLAUDE.md`; recrie à mão se não
existir).

- [ ] **Step 2: Entregar o roteiro ao usuário e parar**

Roteiro (seção 7.4 da spec), para ele seguir e anotar o que vir:

1. Como `pcp`, abrir um Pedido com prazo **vencido** (ex.: 01/10/2026) e outro com prazo futuro. Conferir a
   pílula "Atrasado" em roxo só no vencido, na lista e na página do Pedido.
2. A Home: a seção "Prazos de entrega", com o vencido primeiro.
3. A lista: abre por "Prazo de entrega"; trocar para "Mais recentes" e para um filtro de status com
   Concluído; salvar um Pedido novo e ver a lista ir para "Mais recentes" com ele no topo.
4. Na página do Pedido: "Editar pedido", trocar número, cliente e data; tentar o número de outro Pedido (409
   dentro do painel).
5. Como `operador`: o "Editar pedido" não aparece; a lista e a pílula, sim.
6. **No celular Android**: abrir o seletor de data do cadastro (primeiro `input type="date"` do projeto) e
   conferir que cabe e funciona.

- [ ] **Step 3: Registrar**

O relatório vai para `.superpowers/sdd/2026-10-08-data-de-entrega/conferencia/` com o que o usuário aprovou,
o que pediu para mudar e as decisões dele. Só depois disso a review de branch inteira é despachada.

---

## Depois das tasks

1. Conferência do usuário (Task 7) e as decisões dele.
2. Review de branch inteira, no modelo mais capaz, sobre `b35cae8..HEAD`, com o pacote de
   `scripts/review-package`.
3. Fix wave dos Critical/Important, com re-review.
4. PR (`gh pr create`), aberto por nós; o merge é do usuário.
