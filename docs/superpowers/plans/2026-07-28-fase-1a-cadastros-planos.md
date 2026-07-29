# Fase 1A — Cadastros Planos (Setor, Material, Pedido, Agrupamento)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sair de um sistema que só autentica para um onde o Administrador cadastra Setor e Material e o PCP abre Pedido com Agrupamentos, via tela.

**Architecture:** Continuação da Clean Architecture da Fase 0 (`Domain` / `Application` / `Infrastructure` / `Api`), Database First. A Task 2–5 estabelece o molde de vertical slice em `Setor`; as entidades seguintes o repetem. Front React + TypeScript, telas de lista + formulário, sem gating de navegação por perfil (fora de escopo).

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core (Database First), SQL Server, xUnit + `Microsoft.AspNetCore.Mvc.Testing`, React 19 + TypeScript + Vite + Tailwind 4, vitest.

**Spec:** `docs/superpowers/specs/2026-07-28-fase-1-cadastros-basicos-design.md` (aprovada). 1B (`Componente` + receita) recebe plano próprio depois desta fechar.

## Global Constraints

Valem para **todas** as tarefas:

- **Database First.** Schema nasce em `specs/02-modelo-de-dados.sql`. Nunca `Add-Migration` do zero.
- **Build em 0 warnings:** `dotnet build Rastreamento.slnx -warnaserror`.
- **Banco no ar** para Infrastructure.Tests e Api.Tests: `docker compose up -d`, com `specs/02-modelo-de-dados.sql` e `db/seed.sql` aplicados.
- **Nomenclatura:** domínio em português espelhando o DDL (`Setor`, `Material`, `Pedido`, `Agrupamento`); padrões técnicos em inglês (`Repository`, `UseCase`, `Controller`, `Dto`). Siglas de 3+ letras em PascalCase.
- **Use case:** uma classe por entidade, `CadastroDe<Entidade>UseCase`. Métodos `Cadastrar` / `Editar` / `Listar`, mais `DefinirAtivo` (só catálogo) ou `Excluir` (só `Agrupamento`).
- **Autorização:** escrita de catálogo `[Authorize(Roles = "Administrador")]`; escrita de Pedido/Agrupamento `[Authorize(Roles = "PCP,Administrador")]`; leitura `[Authorize]` puro.
- **`Application` não conhece `HttpContext`.** O `usuarioId` da autoria é lido da claim `sub` **pelo controller** e passado como parâmetro.
- **Comentários e nomes de teste em português sem acentos**, como o código já existente. Nome de teste no formato `Faz_alguma_coisa_em_tal_situacao`.
- **`Result` / `Result<T>` são os da Fase 0** (`src/Rastreamento.Application/Common/Result.cs`). Não criar outro tipo de resultado. `TipoDeErro.Conflito` → 409, `NaoEncontrado` → 404, `Validacao` → 400.
- **DI:** registrar use cases e repositórios como `Scoped` em `Program.cs`, junto dos existentes.

### Decisão de divergência (registrada de propósito)

Os use cases de auth são registrados por interface (`IAutenticarUsuarioUseCase`). Os de cadastro **não terão interface** — nada os substitui por fake (os testes de `Application` fakeiam o *repositório*, não o use case), e 4 interfaces sem segundo implementador seriam a mesma cerimônia que a decisão "uma classe por entidade" já rejeitou. Registro é da classe concreta: `builder.Services.AddScoped<CadastroDeSetorUseCase>()`. **Se o revisor preferir simetria com auth, esta é a decisão a reverter — e é barata.**

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| `specs/02-modelo-de-dados.sql` | **Modificar** — colunas de autoria em `Pedido` e `Agrupamento` |
| `specs/05-api-endpoints.md` | **Modificar** — verbos novos (`PUT`, `PATCH /ativo`, `DELETE`) |
| `src/Rastreamento.Domain/Entities/{Setor,Material,Pedido,Agrupamento}.cs` | Entidades mapeadas, sem comportamento (modelo anêmico, como a Fase 0) |
| `src/Rastreamento.Domain/Abstractions/I{Setor,Material,Pedido,Agrupamento}Repository.cs` | Contratos de persistência |
| `src/Rastreamento.Application/Cadastros/Dtos.cs` | DTOs de entrada e saída dos quatro cadastros + `ValorDuplicadoDto` |
| `src/Rastreamento.Application/Cadastros/CadastroDe{Setor,Material,Pedido,Agrupamento}UseCase.cs` | Regras: duplicidade antes do insert, autoria, guarda do delete |
| `src/Rastreamento.Infrastructure/Persistence/Configurations/*Configuration.cs` | Mapeamento EF contra o DDL |
| `src/Rastreamento.Infrastructure/Persistence/*Repository.cs` | Implementações EF |
| `src/Rastreamento.Api/Controllers/CadastroControllerBase.cs` | O que os quatro controllers fazem igual: `Result` → status HTTP, corpo do 409 de duplicidade, leitura da claim `sub`. Métodos `virtual` |
| `src/Rastreamento.Api/Controllers/{Setores,Materiais,Pedidos,Agrupamentos}Controller.cs` | Rotas, autorização e o que é específico de cada recurso; herdam de `CadastroControllerBase` |
| `web/src/api/cadastros.ts` | Funções de acesso aos endpoints novos |
| `web/src/pages/{Setores,Materiais,Pedidos,PedidoDetalhe}Page.tsx` | Telas de lista + formulário |
| `web/vite.config.ts` | **Modificar** — proxy das rotas novas |

---

## Task 1: Schema — colunas de autoria

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (bloco `CREATE TABLE dbo.Pedido`, ~linha 142; `CREATE TABLE dbo.Agrupamento`, ~linha 170)
- Modify: `CLAUDE.md` (seção "Pré-requisito externo dos testes" — somar o novo `ALTER` idempotente)

**Interfaces:**
- Produces: colunas `Pedido.CriadoPorUsuarioId`, `Agrupamento.CriadoPorUsuarioId`, `Agrupamento.CriadoEm` — consumidas pelas Tasks 8 e 10.

**Contexto:** `Pedido` **não** ganha `CriadoEm` — `DataAbertura DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME())` já é exatamente isso. As duas tabelas estão vazias (nenhuma funcionalidade as escreve ainda), então `NOT NULL` sem default entra sem backfill.

- [ ] **Step 1: Adicionar a coluna de autoria em `dbo.Pedido`**

Em `specs/02-modelo-de-dados.sql`, dentro do `CREATE TABLE dbo.Pedido`, após a linha `DataConclusao       DATETIME2           NULL,`:

```sql
    CriadoPorUsuarioId  INT                 NOT NULL,  -- autoria: responde "quem abriu este pedido"
```

E, junto das outras constraints da mesma tabela, após `CONSTRAINT FK_Pedido_PedidoOrigem ...`:

```sql
    CONSTRAINT FK_Pedido_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
```

- [ ] **Step 2: Adicionar autoria e timestamp em `dbo.Agrupamento`**

Dentro do `CREATE TABLE dbo.Agrupamento`, após `DataConclusao   DATETIME2           NULL, ...`:

```sql
    CriadoPorUsuarioId INT                 NOT NULL,
    CriadoEm        DATETIME2           NOT NULL
        CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT (SYSUTCDATETIME()),
```

E junto das constraints da tabela:

```sql
    CONSTRAINT FK_Agrupamento_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
```

- [ ] **Step 3: Aplicar no banco de dev (idempotente)**

Run:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Pedido','CriadoPorUsuarioId') IS NULL ALTER TABLE dbo.Pedido ADD CriadoPorUsuarioId INT NOT NULL CONSTRAINT FK_Pedido_CriadoPorUsuario FOREIGN KEY REFERENCES dbo.Usuario(Id); IF COL_LENGTH('dbo.Agrupamento','CriadoPorUsuarioId') IS NULL ALTER TABLE dbo.Agrupamento ADD CriadoPorUsuarioId INT NOT NULL CONSTRAINT FK_Agrupamento_CriadoPorUsuario FOREIGN KEY REFERENCES dbo.Usuario(Id), CriadoEm DATETIME2 NOT NULL CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT (SYSUTCDATETIME());"
```

Expected: sem erro. Rodar duas vezes seguidas também não deve dar erro (é o teste da idempotência).

- [ ] **Step 4: Verificar que as colunas existem**

Run:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME IN ('CriadoPorUsuarioId','CriadoEm') AND TABLE_NAME IN ('Pedido','Agrupamento') ORDER BY TABLE_NAME, COLUMN_NAME;"
```

Expected: 3 linhas — `Agrupamento/CriadoEm`, `Agrupamento/CriadoPorUsuarioId`, `Pedido/CriadoPorUsuarioId`.

- [ ] **Step 5: Documentar o `ALTER` no `CLAUDE.md`**

Na seção "Pré-requisito externo dos testes", logo abaixo do bloco do `ALTER` de lockout, acrescentar:

```markdown
Na Fase 1A entram as colunas de autoria, também por `ALTER` idempotente em banco pré-existente:

​```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Pedido','CriadoPorUsuarioId') IS NULL ALTER TABLE dbo.Pedido ADD CriadoPorUsuarioId INT NOT NULL CONSTRAINT FK_Pedido_CriadoPorUsuario FOREIGN KEY REFERENCES dbo.Usuario(Id);"
​```
```

- [ ] **Step 6: Commit**

```bash
git add specs/02-modelo-de-dados.sql CLAUDE.md
git commit -m "feat(schema): autoria em Pedido e Agrupamento"
```

---

## Task 2: `Setor` — entidade, mapeamento EF e repositório

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Setor.cs`
- Create: `src/Rastreamento.Domain/Abstractions/ISetorRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/SetorConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/SetorRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/SetorMappingTests.cs`

**Interfaces:**
- Produces:
  - `Rastreamento.Domain.Entities.Setor` — `int Id`, `string Nome`, `bool Ativo`
  - `ISetorRepository` — `Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct)`, `Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct)`, `Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct)`, `Task AdicionarAsync(Setor setor, CancellationToken ct)`, `Task SalvarAlteracoesAsync(CancellationToken ct)`
  - `RastreamentoDbContext.Setores`

**Contexto:** `ObterPorNomeAsync` existe porque a duplicidade é checada no use case **antes** do insert (`specs/03-arquitetura-tecnica.md:25-27`: regra de `CHECK`/`UNIQUE` também validada na aplicação, com erro de negócio claro). Os `Obter*` retornam entidade **rastreada** (sem `AsNoTracking`) porque `Editar` e `DefinirAtivo` mutam a entidade e contam com o change tracking — mesmo contrato de `IUsuarioRepository`.

- [ ] **Step 1: Escrever o teste de mapeamento (vai falhar)**

Create `tests/Rastreamento.Infrastructure.Tests/Persistence/SetorMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class SetorMappingTests
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    [Fact]
    public async Task Mapeia_setor_com_round_trip()
    {
        await using var db = NovoContexto();
        var setor = new Setor { Nome = $"setor-{Guid.NewGuid():N}"[..40], Ativo = true };

        db.Setores.Add(setor);
        await db.SaveChangesAsync();
        var id = setor.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id);

            Assert.Equal(setor.Nome, carregado.Nome);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Setor_nasce_ativo_pelo_default_do_banco()
    {
        // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Setor_Ativo.
        // SetorConfiguration nao declara HasDefaultValue (Database First: o default vive so no
        // .sql), entao um INSERT feito pelo EF sempre manda a coluna e nunca exercitaria o DEFAULT.
        await using var db = NovoContexto();
        var nome = $"default-{Guid.NewGuid():N}"[..40];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.Setor (Nome) VALUES ({nome})");

        var id = await db.Database
            .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Setor WHERE Nome = {nome}").SingleAsync();

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Setores.RemoveRange(await dbLimpeza.Setores.Where(s => s.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 2: Rodar o teste e ver falhar**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~SetorMappingTests`
Expected: FALHA de compilação — `Setor` e `RastreamentoDbContext.Setores` não existem.

- [ ] **Step 3: Criar a entidade**

Create `src/Rastreamento.Domain/Entities/Setor.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Setor
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>Catalogo nao se exclui, se inativa: linhas de historico apontam para o Setor.</summary>
    public bool Ativo { get; set; }
}
```

- [ ] **Step 4: Criar o contrato do repositório**

Create `src/Rastreamento.Domain/Abstractions/ISetorRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface ISetorRepository
{
    /// <summary>Entidade RASTREADA: `Editar` e `DefinirAtivo` mutam e contam com o change tracking.</summary>
    Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Existe para o use case detectar duplicidade ANTES do insert e devolver erro de negocio, em
    /// vez de deixar a violacao de UQ_Setor_Nome estourar como excecao ate a API.
    /// </summary>
    Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct);

    Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct);

    Task AdicionarAsync(Setor setor, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Criar o mapeamento EF**

Create `src/Rastreamento.Infrastructure/Persistence/Configurations/SetorConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class SetorConfiguration : IEntityTypeConfiguration<Setor>
{
    public void Configure(EntityTypeBuilder<Setor> b)
    {
        b.ToTable("Setor");
        b.HasKey(s => s.Id);
        b.Property(s => s.Nome).HasMaxLength(100).IsRequired();
        // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
    }
}
```

- [ ] **Step 6: Criar o repositório**

Create `src/Rastreamento.Infrastructure/Persistence/SetorRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class SetorRepository : ISetorRepository
{
    private readonly RastreamentoDbContext _db;

    public SetorRepository(RastreamentoDbContext db) => _db = db;

    public Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Setores.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct) =>
        _db.Setores.SingleOrDefaultAsync(s => s.Nome == nome, ct);

    public async Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        await _db.Setores.AsNoTracking()
            .Where(s => incluirInativos || s.Ativo)
            .OrderBy(s => s.Nome)
            .ToListAsync(ct);

    public async Task AdicionarAsync(Setor setor, CancellationToken ct) =>
        await _db.Setores.AddAsync(setor, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 7: Registrar o `DbSet`**

Modify `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` — após a linha `public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();`:

```csharp
    public DbSet<Setor> Setores => Set<Setor>();
```

- [ ] **Step 8: Rodar o teste e ver passar**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~SetorMappingTests`
Expected: PASS — 2 testes.

- [ ] **Step 9: Verificar o build sem warnings**

Run: `dotnet build Rastreamento.slnx -warnaserror`
Expected: `Build succeeded`, 0 warnings.

- [ ] **Step 10: Commit**

```bash
git add src/Rastreamento.Domain/Entities/Setor.cs src/Rastreamento.Domain/Abstractions/ISetorRepository.cs src/Rastreamento.Infrastructure/Persistence/Configurations/SetorConfiguration.cs src/Rastreamento.Infrastructure/Persistence/SetorRepository.cs src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs tests/Rastreamento.Infrastructure.Tests/Persistence/SetorMappingTests.cs
git commit -m "feat(setor): entidade, mapeamento EF e repositorio"
```

---

## Task 3: `CadastroDeSetorUseCase` — as regras

**Files:**
- Create: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Create: `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeSetorUseCaseTests.cs`

**Interfaces:**
- Consumes: `ISetorRepository` (Task 2)
- Produces:
  - `SetorDto(int Id, string Nome, bool Ativo)`
  - `NovoSetorDto(string Nome)`
  - `ValorDuplicadoDto(string Campo, bool ExisteInativo, int IdExistente)`
  - `CadastroDeSetorUseCase` — `Task<Result<SetorDto>> Cadastrar(NovoSetorDto, CancellationToken)`, `Task<Result<SetorDto>> Editar(int id, NovoSetorDto, CancellationToken)`, `Task<IReadOnlyList<SetorDto>> Listar(bool incluirInativos, CancellationToken)`, `Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken)`, `Task<ValorDuplicadoDto?> LocalizarDuplicado(string nome, CancellationToken)`

**Contexto — por que `LocalizarDuplicado` é método separado:** o corpo do 409 precisa de `existeInativo`/`idExistente` para a tela oferecer "reativar o existente", e `Result<T>` só carrega `string Erro` + `TipoDeErro` (de propósito: o comentário em `Result.cs` avisa contra controller comparando mensagem). Em vez de inchar `Result` — que auth usa —, o controller chama `LocalizarDuplicado` **só no caminho de erro**. Custa uma segunda query num caminho raro.

- [ ] **Step 1: Escrever os fakes**

Create `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs`:

```csharp
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

public class FakeSetorRepo : ISetorRepository
{
    private readonly List<Setor> _linhas;
    private int _proximoId;

    public FakeSetorRepo(params Setor[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(s => s.Id) + 1;
    }

    /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
    public int Saves { get; private set; }

    public Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(s => s.Id == id));

    public Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(s => s.Nome == nome));

    public Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Setor>>(
            _linhas.Where(s => incluirInativos || s.Ativo).OrderBy(s => s.Nome).ToList());

    public Task AdicionarAsync(Setor setor, CancellationToken ct)
    {
        setor.Id = _proximoId++;
        _linhas.Add(setor);
        return Task.CompletedTask;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Escrever os testes do use case (vão falhar)**

Create `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeSetorUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeSetorUseCaseTests
{
    [Fact]
    public async Task Cadastra_setor_novo_ativo()
    {
        var repo = new FakeSetorRepo();
        var useCase = new CadastroDeSetorUseCase(repo);

        var resultado = await useCase.Cadastrar(new NovoSetorDto("Solda"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("Solda", resultado.Valor!.Nome);
        Assert.True(resultado.Valor.Ativo);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task Nome_duplicado_e_conflito_e_nao_escreve_nada()
    {
        var repo = new FakeSetorRepo(new Setor { Id = 7, Nome = "Solda", Ativo = true });
        var useCase = new CadastroDeSetorUseCase(repo);

        var resultado = await useCase.Cadastrar(new NovoSetorDto("Solda"), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Localiza_duplicado_inativo_para_a_tela_oferecer_reativacao()
    {
        var repo = new FakeSetorRepo(new Setor { Id = 7, Nome = "Solda", Ativo = false });
        var useCase = new CadastroDeSetorUseCase(repo);

        var duplicado = await useCase.LocalizarDuplicado("Solda", CancellationToken.None);

        Assert.NotNull(duplicado);
        Assert.Equal("nome", duplicado!.Campo);
        Assert.True(duplicado.ExisteInativo);
        Assert.Equal(7, duplicado.IdExistente);
    }

    [Fact]
    public async Task Localiza_duplicado_devolve_nulo_quando_nome_e_livre()
    {
        var useCase = new CadastroDeSetorUseCase(new FakeSetorRepo());

        Assert.Null(await useCase.LocalizarDuplicado("Solda", CancellationToken.None));
    }

    [Fact]
    public async Task Editar_setor_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDeSetorUseCase(new FakeSetorRepo());

        var resultado = await useCase.Editar(99, new NovoSetorDto("Solda"), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_para_nome_de_outro_setor_e_conflito()
    {
        var repo = new FakeSetorRepo(
            new Setor { Id = 1, Nome = "Solda", Ativo = true },
            new Setor { Id = 2, Nome = "Pintura", Ativo = true });
        var useCase = new CadastroDeSetorUseCase(repo);

        var resultado = await useCase.Editar(2, new NovoSetorDto("Solda"), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_mantendo_o_proprio_nome_nao_e_conflito()
    {
        // Renomear "Solda" para "Solda" acha a si mesmo no ObterPorNome: so e conflito se for outro Id.
        var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true });
        var useCase = new CadastroDeSetorUseCase(repo);

        var resultado = await useCase.Editar(1, new NovoSetorDto("Solda"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public async Task Definir_ativo_false_inativa_e_persiste()
    {
        var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true });
        var useCase = new CadastroDeSetorUseCase(repo);

        var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, repo.Saves);
        Assert.Empty(await useCase.Listar(incluirInativos: false, CancellationToken.None));
        Assert.Single(await useCase.Listar(incluirInativos: true, CancellationToken.None));
    }

    [Fact]
    public async Task Nome_em_branco_e_erro_de_validacao()
    {
        var useCase = new CadastroDeSetorUseCase(new FakeSetorRepo());

        var resultado = await useCase.Cadastrar(new NovoSetorDto("   "), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeSetorUseCaseTests`
Expected: FALHA de compilação — `CadastroDeSetorUseCase`, `NovoSetorDto`, `SetorDto` não existem.

- [ ] **Step 4: Criar os DTOs**

Create `src/Rastreamento.Application/Cadastros/Dtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Rastreamento.Application.Cadastros;

public sealed record SetorDto(int Id, string Nome, bool Ativo);

/// <remarks>
/// `MaxLength` espelha o NVARCHAR(100) de `dbo.Setor.Nome`: nome longo demais vira 400 do proprio
/// ASP.NET (ValidationProblemDetails, por causa do [ApiController]), em vez de SqlException virando
/// 500. O alvo `property:` e obrigatorio — em record posicional um atributo sem alvo pousa no
/// parametro, e a validacao de modelo le os atributos da PROPRIEDADE. Nome so de espacos continua
/// sendo regra do use case: o atributo nao enxerga isso.
/// </remarks>
public sealed record NovoSetorDto([property: MaxLength(100)] string Nome);

/// <summary>
/// Detalhe do 409 de duplicidade. `ExisteInativo` e o que permite a tela oferecer "reativar o
/// existente" em vez de travar o usuario — indice UNIQUE nao filtrado nao perdoa nome repetido,
/// nem de linha inativa (ver a spec da Fase 1, secao "Politica de exclusao").
/// </summary>
public sealed record ValorDuplicadoDto(string Campo, bool ExisteInativo, int IdExistente);
```

- [ ] **Step 5: Implementar o use case**

Create `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Setor: criar, editar, listar e (in)ativar. Setor nao se exclui — linhas de
/// EstruturaSetorHistorico apontam para ele (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDeSetorUseCase
{
    private readonly ISetorRepository _repositorio;

    public CadastroDeSetorUseCase(ISetorRepository repositorio) => _repositorio = repositorio;

    public async Task<Result<SetorDto>> Cadastrar(NovoSetorDto novo, CancellationToken ct)
    {
        var nome = novo.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0)
            return Result<SetorDto>.Falha("Nome e obrigatorio.", TipoDeErro.Validacao);

        // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Setor_Nome
        // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
        if (await _repositorio.ObterPorNomeAsync(nome, ct) is not null)
            return Result<SetorDto>.Falha("Ja existe um Setor com este nome.", TipoDeErro.Conflito);

        var setor = new Setor { Nome = nome, Ativo = true };
        await _repositorio.AdicionarAsync(setor, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<SetorDto>.Ok(new SetorDto(setor.Id, setor.Nome, setor.Ativo));
    }

    public async Task<Result<SetorDto>> Editar(int id, NovoSetorDto alterado, CancellationToken ct)
    {
        var nome = alterado.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0)
            return Result<SetorDto>.Falha("Nome e obrigatorio.", TipoDeErro.Validacao);

        var setor = await _repositorio.ObterPorIdAsync(id, ct);
        if (setor is null)
            return Result<SetorDto>.Falha("Setor nao encontrado.", TipoDeErro.NaoEncontrado);

        // So e conflito se o nome pertencer a OUTRA linha: renomear para o proprio nome e no-op.
        var homonimo = await _repositorio.ObterPorNomeAsync(nome, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<SetorDto>.Falha("Ja existe um Setor com este nome.", TipoDeErro.Conflito);

        setor.Nome = nome;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<SetorDto>.Ok(new SetorDto(setor.Id, setor.Nome, setor.Ativo));
    }

    public async Task<IReadOnlyList<SetorDto>> Listar(bool incluirInativos, CancellationToken ct)
    {
        var setores = await _repositorio.ListarAsync(incluirInativos, ct);
        return setores.Select(s => new SetorDto(s.Id, s.Nome, s.Ativo)).ToList();
    }

    /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /setores/{id}/ativo`.</summary>
    public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
    {
        var setor = await _repositorio.ObterPorIdAsync(id, ct);
        if (setor is null)
            return Result.Falha("Setor nao encontrado.", TipoDeErro.NaoEncontrado);

        setor.Ativo = ativo;
        await _repositorio.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo
    /// da segunda leitura nao entra no caminho feliz.
    /// </summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string nome, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorNomeAsync(nome.Trim(), ct);
        return existente is null
            ? null
            : new ValorDuplicadoDto("nome", !existente.Ativo, existente.Id);
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeSetorUseCaseTests`
Expected: PASS — 9 testes.

- [ ] **Step 7: Commit**

```bash
git add src/Rastreamento.Application/Cadastros/ tests/Rastreamento.Application.Tests/Cadastros/
git commit -m "feat(setor): CadastroDeSetorUseCase com deteccao de duplicidade"
```

---

## Task 4: `SetoresController` — rotas, autorização e contrato de erro

**Files:**
- Create: `src/Rastreamento.Api/Controllers/CadastroControllerBase.cs`
- Create: `src/Rastreamento.Api/Controllers/SetoresController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`
- Test: `tests/Rastreamento.Api.Tests/TokenDeTeste.cs`

**Interfaces:**
- Consumes: `CadastroDeSetorUseCase` (Task 3)
- Produces:
  - `TokenDeTeste.Emitir(WebApplicationFactory<Program>, string perfil, int usuarioId = 1)` → `string` — access token assinado com a chave da configuração de teste, usado por todos os testes de autorização das Tasks 4, 6, 8 e 10
  - `CadastroControllerBase` — base dos quatro controllers de cadastro, com os membros `protected virtual`:
    - `delegate Task<ValorDuplicadoDto?> LocalizadorDeDuplicado(CancellationToken ct)`
    - `Task<IActionResult> TraduzirFalha(TipoDeErro? tipo, string? erro, LocalizadorDeDuplicado localizar, CancellationToken ct)`
    - `Task<object> MontarConflito(LocalizadorDeDuplicado localizar, string? erro, CancellationToken ct)`
    - `IActionResult TraduzirResultado(Result resultado)`
    - `int? UsuarioDaSessao()`
  - Rotas `GET/POST /setores`, `PUT /setores/{id}`, `PATCH /setores/{id}/ativo`

**Contexto:** o helper de token existe porque os testes precisam de um usuário de cada perfil sem criar 6 linhas em `Usuario` por teste. Ele assina um JWT com as mesmas `JwtOptions` que a API valida, com a claim `role` do perfil desejado.

**Por que a base usa delegate e não método abstrato:** a pergunta "existe duplicado?" muda de forma por entidade — `Setor` procura por nome, `Material` por código, `Agrupamento` por `(PedidoId, Codigo)`. Um método abstrato de assinatura fixa não cobriria os três; o delegate deixa cada controller **fechar sobre** os valores que já tem em mãos e a base só precisa saber que dá para perguntar. Tudo é `virtual`: quem precisar de um desfecho diferente sobrescreve um método sem tocar nos outros.

- [ ] **Step 1: Escrever o helper de token**

Create `tests/Rastreamento.Api.Tests/TokenDeTeste.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Application.Auth;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Emite um access token valido para um perfil arbitrario, assinando com as MESMAS JwtOptions que
/// a API valida. Evita criar uma linha em Usuario por perfil so para testar `[Authorize(Roles)]`.
/// </summary>
public static class TokenDeTeste
{
    public static string Emitir(WebApplicationFactory<Program> factory, string perfil, int usuarioId = 1)
    {
        using var escopo = factory.Services.CreateScope();
        var jwt = escopo.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims:
            [
                new Claim("sub", usuarioId.ToString()),
                new Claim("unique_name", $"teste-{perfil}"),
                new Claim("nome_completo", $"Usuario de Teste {perfil}"),
                new Claim("role", perfil),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 2: Escrever os testes de endpoint (vão falhar)**

Create `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Setor, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa os Setores que criou — UQ_Setor_Nome nao perdoa sobra de execucao anterior.
/// </summary>
public class SetoresEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _nomesCriados = [];

    public SetoresEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        db.Setores.RemoveRange(await db.Setores.Where(s => _nomesCriados.Contains(s.Nome)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private string NomeUnico()
    {
        var nome = $"setor-{Guid.NewGuid():N}"[..40];
        _nomesCriados.Add(nome);
        return nome;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
        return cliente;
    }

    [Fact]
    public async Task Administrador_cadastra_setor()
    {
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/setores", new { nome = NomeUnico() });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Operador_nao_cadastra_setor()
    {
        var resposta = await ClienteComo("Operador")
            .PostAsJsonAsync("/setores", new { nome = NomeUnico() });

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Operador_le_a_lista_de_setores()
    {
        var resposta = await ClienteComo("Operador").GetAsync("/setores");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/setores");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Nome_duplicado_ativo_responde_409_sem_reativacao()
    {
        var cliente = ClienteComo("Administrador");
        var nome = NomeUnico();
        await cliente.PostAsJsonAsync("/setores", new { nome });

        var resposta = await cliente.PostAsJsonAsync("/setores", new { nome });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("nome", corpo.GetProperty("campo").GetString());
        Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
    }

    [Fact]
    public async Task Nome_duplicado_inativo_responde_409_indicando_reativacao()
    {
        var cliente = ClienteComo("Administrador");
        var nome = NomeUnico();
        var criado = await cliente.PostAsJsonAsync("/setores", new { nome });
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/setores/{id}/ativo", new { ativo = false });

        var resposta = await cliente.PostAsJsonAsync("/setores", new { nome });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
        Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
    }

    [Fact]
    public async Task Setor_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
    {
        var cliente = ClienteComo("Administrador");
        var nome = NomeUnico();
        var criado = await cliente.PostAsJsonAsync("/setores", new { nome });
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/setores/{id}/ativo", new { ativo = false });

        var padrao = await cliente.GetStringAsync("/setores");
        var comInativos = await cliente.GetStringAsync("/setores?incluirInativos=true");

        Assert.DoesNotContain(nome, padrao);
        Assert.Contains(nome, comInativos);
    }

    [Fact]
    public async Task Editar_setor_inexistente_responde_404()
    {
        var resposta = await ClienteComo("Administrador")
            .PutAsJsonAsync("/setores/999999", new { nome = NomeUnico() });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Nome_em_branco_responde_400()
    {
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/setores", new { nome = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Nome_maior_que_a_coluna_responde_400_e_nao_500()
    {
        // 101 caracteres contra o NVARCHAR(100) de UQ_Setor_Nome. Prova que o [property: MaxLength]
        // de NovoSetorDto pega ANTES de o insert estourar SqlException — e o unico teste que
        // exercita o atributo, e vale para o molde inteiro (Material, Pedido, Agrupamento o copiam).
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/setores", new { nome = new string('x', 101) });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~SetoresEndpointsTests`
Expected: FALHA — 404 em todas as rotas (o controller não existe).

- [ ] **Step 4: Criar a base dos controllers de cadastro**

Create `src/Rastreamento.Api/Controllers/CadastroControllerBase.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que os quatro controllers de cadastro fazem igual: traduzir <see cref="Result"/> em status
/// HTTP, montar o corpo do 409 de duplicidade e ler o usuario da sessao. Tudo <c>virtual</c> —
/// quem precisar de um desfecho diferente sobrescreve um metodo sem reescrever os outros.
/// </summary>
/// <remarks>
/// Classe abstrata nao entra na descoberta de controllers do ASP.NET, entao ela nao carrega
/// <c>[ApiController]</c> nem <c>[Route]</c>: rota e autorizacao continuam declaradas por recurso.
/// </remarks>
public abstract class CadastroControllerBase : ControllerBase
{
    /// <summary>
    /// Como ESTE recurso pergunta pelo detalhe da duplicidade. E um delegate, e nao um metodo
    /// abstrato, porque a pergunta muda de forma por entidade: `Setor` procura por nome, `Material`
    /// por codigo, `Agrupamento` por (PedidoId, Codigo). Assim cada controller fecha sobre os
    /// valores que ja tem em maos, e a base so precisa saber que da para perguntar.
    /// </summary>
    protected delegate Task<ValorDuplicadoDto?> LocalizadorDeDuplicado(CancellationToken ct);

    /// <summary>
    /// Falha de operacao que devolve valor (POST/PUT). Conflito vira 409 COM o detalhe de
    /// duplicidade — e o que permite a tela oferecer "reativar o existente" em vez de so dizer
    /// "nome em uso", ja que os indices UNIQUE nao sao filtrados por `Ativo`.
    /// </summary>
    protected virtual async Task<IActionResult> TraduzirFalha(
        TipoDeErro? tipo, string? erro, LocalizadorDeDuplicado localizar, CancellationToken ct) =>
        tipo switch
        {
            TipoDeErro.NaoEncontrado => NotFound(),
            TipoDeErro.Conflito => Conflict(await MontarConflito(localizar, erro, ct)),
            _ => BadRequest(new { erro }),
        };

    /// <summary>
    /// Corpo do 409 de duplicidade. A busca pelo duplicado acontece so aqui, no caminho de erro:
    /// o custo da segunda leitura nunca entra no caminho feliz.
    /// </summary>
    protected virtual async Task<object> MontarConflito(
        LocalizadorDeDuplicado localizar, string? erro, CancellationToken ct)
    {
        var duplicado = await localizar(ct);
        return duplicado is null
            ? new { erro }
            : new
            {
                erro = "ValorDuplicado",
                campo = duplicado.Campo,
                existeInativo = duplicado.ExisteInativo,
                idExistente = duplicado.IdExistente,
            };
    }

    /// <summary>
    /// Operacao sem valor de retorno (`PATCH /{id}/ativo`, `DELETE /agrupamentos/{id}`): 204 no
    /// sucesso. No conflito o `Erro` e repassado como veio — no DELETE de Agrupamento ele e um
    /// CODIGO ("AgrupamentoNaoVazio" / "PedidoNaoAberto"), que e o que o contrato da spec define.
    /// </summary>
    protected virtual IActionResult TraduzirResultado(Result resultado)
    {
        if (resultado.Sucesso) return NoContent();

        return resultado.TipoDoErro switch
        {
            TipoDeErro.NaoEncontrado => NotFound(),
            TipoDeErro.Conflito => Conflict(new { erro = resultado.Erro }),
            _ => BadRequest(new { erro = resultado.Erro }),
        };
    }

    /// <summary>
    /// Id do usuario da sessao, a partir da claim `sub` — a fronteira onde `HttpContext` para.
    /// `Application` recebe o valor por parametro e nunca conhece o ASP.NET. Token assinado por
    /// nos mas sem a claim e falha de autenticacao (401), nao 500 — mesmo criterio do MeController.
    /// </summary>
    protected virtual int? UsuarioDaSessao() =>
        int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}
```

Create `src/Rastreamento.Api/Controllers/SetoresController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("setores")]
[Authorize]
public class SetoresController : CadastroControllerBase
{
    private readonly CadastroDeSetorUseCase _cadastro;

    public SetoresController(CadastroDeSetorUseCase cadastro) => _cadastro = cadastro;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] bool incluirInativos = false, CancellationToken ct = default) =>
        Ok(await _cadastro.Listar(incluirInativos, ct));

    [HttpPost]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Cadastrar([FromBody] NovoSetorDto novo, CancellationToken ct)
    {
        var resultado = await _cadastro.Cadastrar(novo, ct);
        if (resultado.Sucesso)
            return CreatedAtAction(nameof(Listar), new { id = resultado.Valor!.Id }, resultado.Valor);

        return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Nome), ct);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoSetorDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Nome), ct);
    }

    [HttpPatch("{id:int}/ativo")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> DefinirAtivo(
        int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
        TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo, ct));

    /// <summary>Como Setor pergunta pelo duplicado: por nome (UQ_Setor_Nome).</summary>
    private LocalizadorDeDuplicado Duplicado(string nome) =>
        ct => _cadastro.LocalizarDuplicado(nome, ct);
}
```

- [ ] **Step 5: Criar o DTO do PATCH**

Modify `src/Rastreamento.Application/Cadastros/Dtos.cs` — acrescentar ao fim:

```csharp
/// <summary>Corpo de `PATCH /{recurso}/{id}/ativo`. Cobre inativar e reativar.</summary>
public sealed record DefinirAtivoDto(bool Ativo);
```

- [ ] **Step 6: Registrar as dependências**

Modify `src/Rastreamento.Api/Program.cs` — após a linha `builder.Services.AddScoped<IRevogarTokenUseCase, RevogarTokenUseCase>();`:

```csharp
// Cadastros (Fase 1A). Sem interface de use case de proposito: nada os substitui por fake — os
// testes de Application fakeiam o repositorio. Ver a decisao registrada no plano da Fase 1A.
builder.Services.AddScoped<ISetorRepository, SetorRepository>();
builder.Services.AddScoped<CadastroDeSetorUseCase>();
```

E acrescentar o `using` no topo, junto dos outros:

```csharp
using Rastreamento.Application.Cadastros;
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~SetoresEndpointsTests`
Expected: PASS — 10 testes.

- [ ] **Step 8: Rodar a suíte inteira e o build**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; todos os testes passando (123 anteriores + os novos).

- [ ] **Step 9: Commit**

```bash
git add src/Rastreamento.Api/Controllers/CadastroControllerBase.cs src/Rastreamento.Api/Controllers/SetoresController.cs src/Rastreamento.Api/Program.cs src/Rastreamento.Application/Cadastros/Dtos.cs tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs tests/Rastreamento.Api.Tests/TokenDeTeste.cs
git commit -m "feat(setor): endpoints com autorizacao por perfil e 409 reativavel"
```

---

## Task 5: Tela de Setores

**Files:**
- Create: `web/src/api/cadastros.ts`
- Create: `web/src/pages/SetoresPage.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/pages/HomePage.tsx`
- Modify: `web/vite.config.ts`
- Test: `web/src/api/cadastros.test.ts`

**Interfaces:**
- Consumes: rotas da Task 4; `apiFetch` de `web/src/api/client.ts`
- Produces:
  - `listarSetores(incluirInativos: boolean): Promise<SetorDto[]>`
  - `criarSetor(nome: string): Promise<SetorDto | ConflitoDeCadastro>`
  - `editarSetor(id: number, nome: string): Promise<SetorDto | ConflitoDeCadastro>`
  - `definirAtivoSetor(id: number, ativo: boolean): Promise<void>`
  - tipos `SetorDto`, `ConflitoDeCadastro`

**Contexto:** o proxy do Vite só encaminha `/auth` e `/me` — sem a entrada nova, a tela quebra em dev com 404 do próprio Vite. Não há `@testing-library/react` instalado: os testes ficam no nível do módulo de API, com `fetch` stubado, como `web/src/api/client.test.ts` já faz.

- [ ] **Step 1: Adicionar as rotas ao proxy**

Modify `web/vite.config.ts` — dentro de `server.proxy`, junto de `/auth` e `/me`:

```ts
      '/setores': { target: 'http://localhost:5169', changeOrigin: true },
      '/materiais': { target: 'http://localhost:5169', changeOrigin: true },
      '/pedidos': { target: 'http://localhost:5169', changeOrigin: true },
      '/agrupamentos': { target: 'http://localhost:5169', changeOrigin: true },
```

- [ ] **Step 2: Escrever os testes do módulo de API (vão falhar)**

Create `web/src/api/cadastros.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { listarSetores, criarSetor, ehConflito } from './cadastros'
import { inicializar, _resetParaTeste } from './client'

describe('cadastros', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('lista setores ativos por padrão', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([{ id: 1, nome: 'Solda', ativo: true }]), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const setores = await listarSetores(false)

    expect(setores).toEqual([{ id: 1, nome: 'Solda', ativo: true }])
    expect(fetchMock.mock.calls[0][0]).toBe('/setores?incluirInativos=false')
  })

  it('devolve o conflito quando o nome já existe inativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarSetor('Solda')

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(true)
    expect(ehConflito(resultado) && resultado.idExistente).toBe(7)
  })

  it('lança quando a resposta é erro não tratado', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })))

    await expect(criarSetor('Solda')).rejects.toThrow()
  })
})
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `cd web && npm test`
Expected: FALHA — `./cadastros` não existe.

- [ ] **Step 4: Criar o módulo de API**

Create `web/src/api/cadastros.ts`:

```ts
import { apiFetch } from './client'

export interface SetorDto {
  id: number
  nome: string
  ativo: boolean
}

/** Corpo do 409 de duplicidade. `existeInativo` habilita o botão de reativar. */
export interface ConflitoDeCadastro {
  erro: 'ValorDuplicado'
  campo: string
  existeInativo: boolean
  idExistente: number
}

export function ehConflito(r: unknown): r is ConflitoDeCadastro {
  return typeof r === 'object' && r !== null && (r as ConflitoDeCadastro).erro === 'ValorDuplicado'
}

async function lerOuFalhar<T>(resp: Response): Promise<T | ConflitoDeCadastro> {
  if (resp.status === 409) return (await resp.json()) as ConflitoDeCadastro
  if (!resp.ok) throw new Error(`Falha na requisição (${resp.status}).`)
  return (await resp.json()) as T
}

export async function listarSetores(incluirInativos: boolean): Promise<SetorDto[]> {
  const resp = await apiFetch(`/setores?incluirInativos=${incluirInativos}`)
  if (!resp.ok) throw new Error(`Falha ao listar setores (${resp.status}).`)
  return (await resp.json()) as SetorDto[]
}

export function criarSetor(nome: string): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch('/setores', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nome }),
  }).then(lerOuFalhar<SetorDto>)
}

export function editarSetor(id: number, nome: string): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch(`/setores/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nome }),
  }).then(lerOuFalhar<SetorDto>)
}

export async function definirAtivoSetor(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/setores/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new Error(`Falha ao alterar o setor (${resp.status}).`)
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `cd web && npm test`
Expected: PASS — 3 testes novos, mais os que já existiam.

- [ ] **Step 6: Criar a tela**

Create `web/src/pages/SetoresPage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [nome, setNome] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
    } catch {
      setErro('Não foi possível carregar os setores.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: React.FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    try {
      const resultado = await criarSetor(nome)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      setNome('')
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível salvar o setor.')
    }
  }

  async function alternarAtivo(setor: SetorDto) {
    await definirAtivoSetor(setor.id, !setor.ativo)
    await carregar(incluirInativos)
  }

  async function reativar(id: number) {
    await definirAtivoSetor(id, true)
    setErro(null)
    setIdReativavel(null)
    setNome('')
    await carregar(incluirInativos)
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Setores</h1>

      <form onSubmit={salvar} className="flex gap-2">
        <input
          value={nome}
          onChange={(e) => setNome(e.target.value)}
          placeholder="Nome do setor"
          className="border rounded px-3 py-2 flex-1"
        />
        <button type="submit" className="border rounded px-3 py-2">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}
      {idReativavel !== null && (
        <button onClick={() => reativar(idReativavel)} className="border rounded px-3 py-2 self-start">
          Reativar o existente
        </button>
      )}

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
        />
        Mostrar inativos
      </label>

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {setores.map((s) => (
            <li key={s.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={s.ativo ? '' : 'text-gray-400 line-through'}>{s.nome}</span>
              <button onClick={() => alternarAtivo(s)} className="text-sm border rounded px-2 py-1">
                {s.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
```

- [ ] **Step 7: Registrar a rota e o link**

Modify `web/src/App.tsx`:

```tsx
import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { SetoresPage } from './pages/SetoresPage'
import { ProtectedRoute } from './auth/ProtectedRoute'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<ProtectedRoute><HomePage /></ProtectedRoute>} />
      <Route path="/setores" element={<ProtectedRoute><SetoresPage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
```

Modify `web/src/pages/HomePage.tsx` — acrescentar o `import { Link } from 'react-router-dom'` no topo e, dentro da `div` de botões (antes de "Recarregar"):

```tsx
        <Link to="/setores" className="border rounded px-3 py-2">Setores</Link>
```

> Sem gating por perfil de propósito (fora do escopo de 1A): o link aparece para todos e o 403 do backend é a fronteira real.

- [ ] **Step 8: Verificar build e lint do front**

Run: `cd web && npm run build && npm run lint && npm test`
Expected: build sem erro de TypeScript, lint limpo, testes passando.

- [ ] **Step 9: Commit**

```bash
git add web/src/api/cadastros.ts web/src/api/cadastros.test.ts web/src/pages/SetoresPage.tsx web/src/App.tsx web/src/pages/HomePage.tsx web/vite.config.ts
git commit -m "feat(setor): tela de cadastro com reativacao de inativo"
```

---

## Task 6: `Material` — backend completo

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Material.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IMaterialRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/MaterialConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/MaterialRepository.cs`
- Create: `src/Rastreamento.Application/Cadastros/CadastroDeMaterialUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/MateriaisController.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (acrescentar `FakeMaterialRepo`)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeMaterialUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/MaterialMappingTests.cs`
- Test: `tests/Rastreamento.Api.Tests/MateriaisEndpointsTests.cs`

**Interfaces:**
- Consumes: `Result` / `Result<T>` / `TipoDeErro` (Fase 0), `ValorDuplicadoDto` e `DefinirAtivoDto` (Tasks 3 e 4), `CadastroControllerBase` e `TokenDeTeste.Emitir` (Task 4)
- Produces:
  - `Rastreamento.Domain.Entities.Material` — `int Id`, `string Codigo`, `string Descricao`, `string UnidadeMedida`, `bool Ativo`
  - `IMaterialRepository` — `Task<Material?> ObterPorIdAsync(int id, CancellationToken ct)`, `Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct)`, `Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct)`, `Task AdicionarAsync(Material material, CancellationToken ct)`, `Task SalvarAlteracoesAsync(CancellationToken ct)`
  - `MaterialDto(int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo)`
  - `NovoMaterialDto(string Codigo, string Descricao, string UnidadeMedida)`
  - `CadastroDeMaterialUseCase` — `Cadastrar`, `Editar`, `Listar`, `DefinirAtivo`, `LocalizarDuplicado` (mesmas assinaturas do de `Setor`, trocando `SetorDto`/`NovoSetorDto` por `MaterialDto`/`NovoMaterialDto`)
  - Rotas `GET/POST /materiais`, `PUT /materiais/{id}`, `PATCH /materiais/{id}/ativo`
  - `RastreamentoDbContext.Materiais`

**Contexto:** `UQ_Material_Codigo` é sobre `Codigo`, não sobre `Descricao` — então `ValorDuplicadoDto.Campo` vale `"codigo"` e a tela reativa pelo código. `UnidadeMedida` é `NVARCHAR(10)` livre no DDL (o comentário cita `UN, M, KG, M2` como exemplo, sem `CHECK`): a aplicação exige apenas que não venha em branco, e **não** inventa uma lista fechada que o schema não tem.

- [ ] **Step 1: Acrescentar o fake do repositório**

Modify `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` — acrescentar ao fim do arquivo:

```csharp
public class FakeMaterialRepo : IMaterialRepository
{
    private readonly List<Material> _linhas;
    private int _proximoId;

    public FakeMaterialRepo(params Material[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(m => m.Id) + 1;
    }

    public int Saves { get; private set; }

    public Task<Material?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(m => m.Id == id));

    public Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(m => m.Codigo == codigo));

    public Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Material>>(
            _linhas.Where(m => incluirInativos || m.Ativo).OrderBy(m => m.Codigo).ToList());

    public Task AdicionarAsync(Material material, CancellationToken ct)
    {
        material.Id = _proximoId++;
        _linhas.Add(material);
        return Task.CompletedTask;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Escrever os testes do use case (vão falhar)**

Create `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeMaterialUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeMaterialUseCaseTests
{
    private static NovoMaterialDto Chapa(string codigo = "CH-001") =>
        new(codigo, "Chapa de aco 3mm", "KG");

    [Fact]
    public async Task Cadastra_material_novo_ativo()
    {
        var repo = new FakeMaterialRepo();
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Cadastrar(Chapa(), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("CH-001", resultado.Valor!.Codigo);
        Assert.Equal("Chapa de aco 3mm", resultado.Valor.Descricao);
        Assert.Equal("KG", resultado.Valor.UnidadeMedida);
        Assert.True(resultado.Valor.Ativo);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task Codigo_duplicado_e_conflito_e_nao_escreve_nada()
    {
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 3, Codigo = "CH-001", Descricao = "Outra coisa", UnidadeMedida = "UN", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Cadastrar(Chapa(), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Descricao_duplicada_nao_e_conflito()
    {
        // UQ_Material_Codigo cobre so o Codigo: dois materiais com a mesma descricao sao validos.
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 3, Codigo = "CH-001", Descricao = "Chapa de aco 3mm", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Cadastrar(Chapa("CH-002"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public async Task Localiza_duplicado_inativo_apontando_o_campo_codigo()
    {
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 9, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = false,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var duplicado = await useCase.LocalizarDuplicado("CH-001", CancellationToken.None);

        Assert.NotNull(duplicado);
        Assert.Equal("codigo", duplicado!.Campo);
        Assert.True(duplicado.ExisteInativo);
        Assert.Equal(9, duplicado.IdExistente);
    }

    [Theory]
    [InlineData("", "Chapa", "KG")]
    [InlineData("CH-001", "  ", "KG")]
    [InlineData("CH-001", "Chapa", "")]
    public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(
        string codigo, string descricao, string unidade)
    {
        var useCase = new CadastroDeMaterialUseCase(new FakeMaterialRepo());

        var resultado = await useCase.Cadastrar(
            new NovoMaterialDto(codigo, descricao, unidade), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_material_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDeMaterialUseCase(new FakeMaterialRepo());

        var resultado = await useCase.Editar(99, Chapa(), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_mantendo_o_proprio_codigo_nao_e_conflito()
    {
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 1, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Editar(
            1, new NovoMaterialDto("CH-001", "Chapa de aco 3mm", "KG"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("Chapa de aco 3mm", resultado.Valor!.Descricao);
    }

    [Fact]
    public async Task Editar_para_codigo_de_outro_material_e_conflito()
    {
        var repo = new FakeMaterialRepo(
            new Material { Id = 1, Codigo = "CH-001", Descricao = "A", UnidadeMedida = "KG", Ativo = true },
            new Material { Id = 2, Codigo = "CH-002", Descricao = "B", UnidadeMedida = "KG", Ativo = true });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Editar(2, Chapa(), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Definir_ativo_false_inativa_e_persiste()
    {
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 1, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, repo.Saves);
        Assert.Empty(await useCase.Listar(incluirInativos: false, CancellationToken.None));
        Assert.Single(await useCase.Listar(incluirInativos: true, CancellationToken.None));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeMaterialUseCaseTests`
Expected: FALHA de compilação — `Material`, `IMaterialRepository`, `CadastroDeMaterialUseCase`, `NovoMaterialDto` não existem.

- [ ] **Step 4: Criar a entidade e o contrato do repositório**

Create `src/Rastreamento.Domain/Entities/Material.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Material
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Texto livre no DDL (ex.: UN, M, KG, M2) — sem CHECK, entao sem lista fechada aqui.</summary>
    public string UnidadeMedida { get; set; } = string.Empty;

    /// <summary>Catalogo nao se exclui, se inativa: EstruturaMaterial aponta para o Material.</summary>
    public bool Ativo { get; set; }
}
```

Create `src/Rastreamento.Domain/Abstractions/IMaterialRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IMaterialRepository
{
    /// <summary>Entidade RASTREADA: `Editar` e `DefinirAtivo` mutam e contam com o change tracking.</summary>
    Task<Material?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Existe para o use case detectar duplicidade ANTES do insert e devolver erro de negocio, em
    /// vez de deixar a violacao de UQ_Material_Codigo estourar como excecao ate a API.
    /// </summary>
    Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

    Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct);

    Task AdicionarAsync(Material material, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Criar os DTOs**

Modify `src/Rastreamento.Application/Cadastros/Dtos.cs` — acrescentar ao fim:

```csharp
public sealed record MaterialDto(
    int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo);

/// <remarks>Os `MaxLength` espelham `dbo.Material`: NVARCHAR(50), (200) e (10).</remarks>
public sealed record NovoMaterialDto(
    [property: MaxLength(50)] string Codigo,
    [property: MaxLength(200)] string Descricao,
    [property: MaxLength(10)] string UnidadeMedida);
```

- [ ] **Step 6: Implementar o use case**

Create `src/Rastreamento.Application/Cadastros/CadastroDeMaterialUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Material: criar, editar, listar e (in)ativar. Material nao se exclui — linhas de
/// EstruturaMaterial e ComponenteMaterialPadrao apontam para ele.
/// </summary>
public sealed class CadastroDeMaterialUseCase
{
    private readonly IMaterialRepository _repositorio;

    public CadastroDeMaterialUseCase(IMaterialRepository repositorio) => _repositorio = repositorio;

    public async Task<Result<MaterialDto>> Cadastrar(NovoMaterialDto novo, CancellationToken ct)
    {
        var (codigo, descricao, unidade) = Normalizar(novo);
        if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
            return Result<MaterialDto>.Falha(
                "Codigo, descricao e unidade de medida sao obrigatorios.", TipoDeErro.Validacao);

        // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Material_Codigo
        // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
        if (await _repositorio.ObterPorCodigoAsync(codigo, ct) is not null)
            return Result<MaterialDto>.Falha(
                "Ja existe um Material com este codigo.", TipoDeErro.Conflito);

        var material = new Material
        {
            Codigo = codigo, Descricao = descricao, UnidadeMedida = unidade, Ativo = true,
        };
        await _repositorio.AdicionarAsync(material, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<MaterialDto>.Ok(Projetar(material));
    }

    public async Task<Result<MaterialDto>> Editar(
        int id, NovoMaterialDto alterado, CancellationToken ct)
    {
        var (codigo, descricao, unidade) = Normalizar(alterado);
        if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
            return Result<MaterialDto>.Falha(
                "Codigo, descricao e unidade de medida sao obrigatorios.", TipoDeErro.Validacao);

        var material = await _repositorio.ObterPorIdAsync(id, ct);
        if (material is null)
            return Result<MaterialDto>.Falha("Material nao encontrado.", TipoDeErro.NaoEncontrado);

        // So e conflito se o codigo pertencer a OUTRA linha: manter o proprio codigo e no-op.
        var homonimo = await _repositorio.ObterPorCodigoAsync(codigo, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<MaterialDto>.Falha(
                "Ja existe um Material com este codigo.", TipoDeErro.Conflito);

        material.Codigo = codigo;
        material.Descricao = descricao;
        material.UnidadeMedida = unidade;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<MaterialDto>.Ok(Projetar(material));
    }

    public async Task<IReadOnlyList<MaterialDto>> Listar(bool incluirInativos, CancellationToken ct)
    {
        var materiais = await _repositorio.ListarAsync(incluirInativos, ct);
        return materiais.Select(Projetar).ToList();
    }

    /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /materiais/{id}/ativo`.</summary>
    public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
    {
        var material = await _repositorio.ObterPorIdAsync(id, ct);
        if (material is null)
            return Result.Falha("Material nao encontrado.", TipoDeErro.NaoEncontrado);

        material.Ativo = ativo;
        await _repositorio.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro. O campo e
    /// `codigo` porque a unicidade e do Codigo — a Descricao pode repetir a vontade.
    /// </summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string codigo, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorCodigoAsync(codigo.Trim(), ct);
        return existente is null
            ? null
            : new ValorDuplicadoDto("codigo", !existente.Ativo, existente.Id);
    }

    private static (string Codigo, string Descricao, string Unidade) Normalizar(NovoMaterialDto d) =>
        (d.Codigo?.Trim() ?? string.Empty,
         d.Descricao?.Trim() ?? string.Empty,
         d.UnidadeMedida?.Trim() ?? string.Empty);

    private static MaterialDto Projetar(Material m) =>
        new(m.Id, m.Codigo, m.Descricao, m.UnidadeMedida, m.Ativo);
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeMaterialUseCaseTests`
Expected: PASS — 11 testes (o `[Theory]` conta 3).

- [ ] **Step 8: Escrever o teste de mapeamento (vai falhar)**

Create `tests/Rastreamento.Infrastructure.Tests/Persistence/MaterialMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class MaterialMappingTests
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    [Fact]
    public async Task Mapeia_material_com_round_trip()
    {
        await using var db = NovoContexto();
        var material = new Material
        {
            Codigo = $"mat-{Guid.NewGuid():N}"[..40],
            Descricao = "Chapa de aco 3mm",
            UnidadeMedida = "KG",
            Ativo = true,
        };

        db.Materiais.Add(material);
        await db.SaveChangesAsync();
        var id = material.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Materiais.AsNoTracking().SingleAsync(m => m.Id == id);

            Assert.Equal(material.Codigo, carregado.Codigo);
            Assert.Equal("Chapa de aco 3mm", carregado.Descricao);
            Assert.Equal("KG", carregado.UnidadeMedida);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Materiais.RemoveRange(
                await dbLimpeza.Materiais.Where(m => m.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Material_nasce_ativo_pelo_default_do_banco()
    {
        // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Material_Ativo,
        // porque um INSERT feito pelo EF sempre manda a coluna (Database First: o default so vive
        // no .sql, MaterialConfiguration nao declara HasDefaultValue).
        await using var db = NovoContexto();
        var codigo = $"def-{Guid.NewGuid():N}"[..40];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.Material (Codigo, Descricao, UnidadeMedida) VALUES ({codigo}, 'Teste', 'UN')");

        var id = await db.Database
            .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Material WHERE Codigo = {codigo}")
            .SingleAsync();

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Materiais.AsNoTracking().SingleAsync(m => m.Id == id);
            Assert.True(carregado.Ativo);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Materiais.RemoveRange(
                await dbLimpeza.Materiais.Where(m => m.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~MaterialMappingTests`
Expected: FALHA de compilação — `RastreamentoDbContext.Materiais` não existe.

- [ ] **Step 9: Criar o mapeamento, o repositório e o `DbSet`**

Create `src/Rastreamento.Infrastructure/Persistence/Configurations/MaterialConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> b)
    {
        b.ToTable("Material");
        b.HasKey(m => m.Id);
        b.Property(m => m.Codigo).HasMaxLength(50).IsRequired();
        b.Property(m => m.Descricao).HasMaxLength(200).IsRequired();
        b.Property(m => m.UnidadeMedida).HasMaxLength(10).IsRequired();
        // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
    }
}
```

Create `src/Rastreamento.Infrastructure/Persistence/MaterialRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class MaterialRepository : IMaterialRepository
{
    private readonly RastreamentoDbContext _db;

    public MaterialRepository(RastreamentoDbContext db) => _db = db;

    public Task<Material?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Materiais.SingleOrDefaultAsync(m => m.Id == id, ct);

    public Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
        _db.Materiais.SingleOrDefaultAsync(m => m.Codigo == codigo, ct);

    public async Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        await _db.Materiais.AsNoTracking()
            .Where(m => incluirInativos || m.Ativo)
            .OrderBy(m => m.Codigo)
            .ToListAsync(ct);

    public async Task AdicionarAsync(Material material, CancellationToken ct) =>
        await _db.Materiais.AddAsync(material, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

Modify `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` — após `public DbSet<Setor> Setores => Set<Setor>();`:

```csharp
    public DbSet<Material> Materiais => Set<Material>();
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~MaterialMappingTests`
Expected: PASS — 2 testes.

- [ ] **Step 10: Escrever os testes de endpoint (vão falhar)**

Create `tests/Rastreamento.Api.Tests/MateriaisEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Material, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa o que criou — UQ_Material_Codigo nao perdoa sobra de execucao anterior.
/// </summary>
public class MateriaisEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _codigosCriados = [];

    public MateriaisEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        db.Materiais.RemoveRange(
            await db.Materiais.Where(m => _codigosCriados.Contains(m.Codigo)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private string CodigoUnico()
    {
        var codigo = $"mat-{Guid.NewGuid():N}"[..40];
        _codigosCriados.Add(codigo);
        return codigo;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
        return cliente;
    }

    private object CorpoValido(string codigo) =>
        new { codigo, descricao = "Chapa de aco 3mm", unidadeMedida = "KG" };

    [Fact]
    public async Task Administrador_cadastra_material()
    {
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/materiais", CorpoValido(CodigoUnico()));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Almoxarifado_nao_cadastra_material_mas_le_a_lista()
    {
        var cliente = ClienteComo("Almoxarifado");

        var escrita = await cliente.PostAsJsonAsync("/materiais", CorpoValido(CodigoUnico()));
        var leitura = await cliente.GetAsync("/materiais");

        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/materiais");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Codigo_duplicado_inativo_responde_409_indicando_reativacao()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/materiais/{id}/ativo", new { ativo = false });

        var resposta = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
        Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
        Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
    }

    [Fact]
    public async Task Material_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/materiais/{id}/ativo", new { ativo = false });

        var padrao = await cliente.GetStringAsync("/materiais");
        var comInativos = await cliente.GetStringAsync("/materiais?incluirInativos=true");

        Assert.DoesNotContain(codigo, padrao);
        Assert.Contains(codigo, comInativos);
    }

    [Fact]
    public async Task Editar_altera_descricao_e_unidade()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var resposta = await cliente.PutAsJsonAsync(
            $"/materiais/{id}", new { codigo, descricao = "Chapa de aco 5mm", unidadeMedida = "UN" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Chapa de aco 5mm", corpo.GetProperty("descricao").GetString());
        Assert.Equal("UN", corpo.GetProperty("unidadeMedida").GetString());
    }

    [Fact]
    public async Task Unidade_de_medida_em_branco_responde_400()
    {
        var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
            "/materiais", new { codigo = CodigoUnico(), descricao = "Chapa", unidadeMedida = " " });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
```

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~MateriaisEndpointsTests`
Expected: FALHA — 404 em todas as rotas (o controller não existe).

- [ ] **Step 11: Criar o controller e registrar as dependências**

Create `src/Rastreamento.Api/Controllers/MateriaisController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("materiais")]
[Authorize]
public class MateriaisController : CadastroControllerBase
{
    private readonly CadastroDeMaterialUseCase _cadastro;

    public MateriaisController(CadastroDeMaterialUseCase cadastro) => _cadastro = cadastro;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] bool incluirInativos = false, CancellationToken ct = default) =>
        Ok(await _cadastro.Listar(incluirInativos, ct));

    [HttpPost]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Cadastrar([FromBody] NovoMaterialDto novo, CancellationToken ct)
    {
        var resultado = await _cadastro.Cadastrar(novo, ct);
        if (resultado.Sucesso)
            return CreatedAtAction(nameof(Listar), new { id = resultado.Valor!.Id }, resultado.Valor);

        return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Codigo), ct);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoMaterialDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(
                resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Codigo), ct);
    }

    [HttpPatch("{id:int}/ativo")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> DefinirAtivo(
        int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
        TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo, ct));

    /// <summary>Como Material pergunta pelo duplicado: por codigo (UQ_Material_Codigo).</summary>
    private LocalizadorDeDuplicado Duplicado(string codigo) =>
        ct => _cadastro.LocalizarDuplicado(codigo, ct);
}
```

Modify `src/Rastreamento.Api/Program.cs` — logo abaixo do registro de `CadastroDeSetorUseCase`:

```csharp
builder.Services.AddScoped<IMaterialRepository, MaterialRepository>();
builder.Services.AddScoped<CadastroDeMaterialUseCase>();
```

- [ ] **Step 12: Rodar tudo**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; toda a suíte passando (os 7 novos de Api, 11 de Application, 2 de Infrastructure, mais os anteriores).

- [ ] **Step 13: Commit**

```bash
git add src/Rastreamento.Domain/Entities/Material.cs src/Rastreamento.Domain/Abstractions/IMaterialRepository.cs src/Rastreamento.Infrastructure/Persistence/Configurations/MaterialConfiguration.cs src/Rastreamento.Infrastructure/Persistence/MaterialRepository.cs src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs src/Rastreamento.Application/Cadastros/ src/Rastreamento.Api/Controllers/MateriaisController.cs src/Rastreamento.Api/Program.cs tests/
git commit -m "feat(material): cadastro completo com 409 reativavel por codigo"
```

---

## Task 7: Tela de Materiais

**Files:**
- Modify: `web/src/api/cadastros.ts`
- Modify: `web/src/api/cadastros.test.ts`
- Create: `web/src/pages/MateriaisPage.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/pages/HomePage.tsx`

**Interfaces:**
- Consumes: rotas da Task 6; `apiFetch`, `ehConflito`, `lerOuFalhar` de `web/src/api/cadastros.ts` (Task 5)
- Produces:
  - `MaterialDto { id: number; codigo: string; descricao: string; unidadeMedida: string; ativo: boolean }`
  - `listarMateriais(incluirInativos: boolean): Promise<MaterialDto[]>`
  - `criarMaterial(m: NovoMaterial): Promise<MaterialDto | ConflitoDeCadastro>`
  - `editarMaterial(id: number, m: NovoMaterial): Promise<MaterialDto | ConflitoDeCadastro>`
  - `definirAtivoMaterial(id: number, ativo: boolean): Promise<void>`
  - `NovoMaterial { codigo: string; descricao: string; unidadeMedida: string }`

**Contexto:** o proxy do Vite já ganhou `/materiais` na Task 5, Step 1 — nada a fazer nele aqui. O formulário passa de 1 para 3 campos, e é o único delta real em relação à tela de Setores.

- [ ] **Step 1: Escrever os testes do módulo de API (vão falhar)**

Modify `web/src/api/cadastros.test.ts` — acrescentar o import de `listarMateriais` e `criarMaterial` na primeira linha de import (`import { listarSetores, criarSetor, ehConflito, listarMateriais, criarMaterial } from './cadastros'`) e o bloco abaixo, dentro do `describe('cadastros', ...)`:

```ts
  it('lista materiais ativos por padrão', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG', ativo: true },
        ]),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const materiais = await listarMateriais(false)

    expect(materiais).toHaveLength(1)
    expect(materiais[0].unidadeMedida).toBe('KG')
    expect(fetchMock.mock.calls[0][0]).toBe('/materiais?incluirInativos=false')
  })

  it('manda os três campos do material no corpo do POST', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ id: 5, codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG', ativo: true }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarMaterial({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })

    const corpo = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(corpo).toEqual({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })
  })

  it('devolve o conflito quando o código do material já existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 4 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarMaterial({ codigo: 'CH-001', descricao: 'Chapa', unidadeMedida: 'KG' })

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.campo).toBe('codigo')
  })
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `cd web && npm test`
Expected: FALHA — `listarMateriais` e `criarMaterial` não são exportados por `./cadastros`.

- [ ] **Step 3: Acrescentar as funções ao módulo de API**

Modify `web/src/api/cadastros.ts` — acrescentar ao fim do arquivo:

```ts
export interface MaterialDto {
  id: number
  codigo: string
  descricao: string
  unidadeMedida: string
  ativo: boolean
}

export interface NovoMaterial {
  codigo: string
  descricao: string
  unidadeMedida: string
}

export async function listarMateriais(incluirInativos: boolean): Promise<MaterialDto[]> {
  const resp = await apiFetch(`/materiais?incluirInativos=${incluirInativos}`)
  if (!resp.ok) throw new Error(`Falha ao listar materiais (${resp.status}).`)
  return (await resp.json()) as MaterialDto[]
}

export function criarMaterial(m: NovoMaterial): Promise<MaterialDto | ConflitoDeCadastro> {
  return apiFetch('/materiais', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(m),
  }).then(lerOuFalhar<MaterialDto>)
}

export function editarMaterial(
  id: number,
  m: NovoMaterial,
): Promise<MaterialDto | ConflitoDeCadastro> {
  return apiFetch(`/materiais/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(m),
  }).then(lerOuFalhar<MaterialDto>)
}

export async function definirAtivoMaterial(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/materiais/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new Error(`Falha ao alterar o material (${resp.status}).`)
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `cd web && npm test`
Expected: PASS — 3 testes novos, mais os que já existiam.

- [ ] **Step 5: Criar a tela**

Create `web/src/pages/MateriaisPage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setMateriais(await listarMateriais(comInativos))
    } catch {
      setErro('Não foi possível carregar os materiais.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: React.FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um material "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um material com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch {
      setErro('Não foi possível salvar o material.')
    }
  }

  async function alternarAtivo(material: MaterialDto) {
    await definirAtivoMaterial(material.id, !material.ativo)
    await carregar(incluirInativos)
  }

  async function reativar(id: number) {
    await definirAtivoMaterial(id, true)
    setErro(null)
    setIdReativavel(null)
    setForm(FORMULARIO_VAZIO)
    await carregar(incluirInativos)
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Materiais</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código"
          className="border rounded px-3 py-2"
        />
        <input
          value={form.descricao}
          onChange={(e) => setForm({ ...form, descricao: e.target.value })}
          placeholder="Descrição"
          className="border rounded px-3 py-2"
        />
        <input
          value={form.unidadeMedida}
          onChange={(e) => setForm({ ...form, unidadeMedida: e.target.value })}
          placeholder="Unidade (UN, KG, M…)"
          className="border rounded px-3 py-2"
        />
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}
      {idReativavel !== null && (
        <button onClick={() => reativar(idReativavel)} className="border rounded px-3 py-2 self-start">
          Reativar o existente
        </button>
      )}

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
        />
        Mostrar inativos
      </label>

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {materiais.map((m) => (
            <li key={m.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={m.ativo ? '' : 'text-gray-400 line-through'}>
                <strong>{m.codigo}</strong> — {m.descricao} ({m.unidadeMedida})
              </span>
              <button onClick={() => alternarAtivo(m)} className="text-sm border rounded px-2 py-1">
                {m.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
```

- [ ] **Step 6: Registrar a rota e o link**

Modify `web/src/App.tsx` — acrescentar o import e a rota:

```tsx
import { MateriaisPage } from './pages/MateriaisPage'
```

```tsx
      <Route path="/materiais" element={<ProtectedRoute><MateriaisPage /></ProtectedRoute>} />
```

Modify `web/src/pages/HomePage.tsx` — na `div` de botões, ao lado do link de Setores:

```tsx
        <Link to="/materiais" className="border rounded px-3 py-2">Materiais</Link>
```

- [ ] **Step 7: Verificar build, lint e testes do front**

Run: `cd web && npm run build && npm run lint && npm test`
Expected: build sem erro de TypeScript, lint limpo, testes passando.

- [ ] **Step 8: Commit**

```bash
git add web/src/api/cadastros.ts web/src/api/cadastros.test.ts web/src/pages/MateriaisPage.tsx web/src/App.tsx web/src/pages/HomePage.tsx
git commit -m "feat(material): tela de cadastro com reativacao de inativo"
```

---

## Task 8: `Pedido` — backend completo, com autoria

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Pedido.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs`
- Create: `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/PedidosController.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (acrescentar `FakePedidoRepo`)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDePedidoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/PedidoMappingTests.cs`
- Test: `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`

**Interfaces:**
- Consumes: colunas de autoria da Task 1; `ValorDuplicadoDto` (Task 3); `CadastroControllerBase` e `TokenDeTeste.Emitir` (Task 4)
- Produces:
  - `Rastreamento.Domain.Entities.Pedido` — `int Id`, `string Numero`, `string Cliente`, `string Tipo`, `int? PedidoOrigemId`, `string? MotivoRetrabalho`, `string Status`, `DateTime DataAbertura`, `DateTime? DataConclusao`, `int CriadoPorUsuarioId`
  - `IPedidoRepository` — `Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct)`, `Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct)`, `Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct)`, `Task AdicionarAsync(Pedido pedido, CancellationToken ct)`, `Task SalvarAlteracoesAsync(CancellationToken ct)`
  - `PedidoDto(int Id, string Numero, string Cliente, string Tipo, string Status, DateTime DataAbertura, int CriadoPorUsuarioId)`
  - `NovoPedidoDto(string Numero, string Cliente)`
  - `CadastroDePedidoUseCase` — `Task<Result<PedidoDto>> Cadastrar(NovoPedidoDto novo, int usuarioId, CancellationToken ct)`, `Task<Result<PedidoDto>> Editar(int id, NovoPedidoDto alterado, CancellationToken ct)`, `Task<IReadOnlyList<PedidoDto>> Listar(CancellationToken ct)`, `Task<Result<PedidoDto>> Obter(int id, CancellationToken ct)`, `Task<ValorDuplicadoDto?> LocalizarDuplicado(string numero, CancellationToken ct)`
  - Rotas `GET/POST /pedidos`, `GET /pedidos/{id}`, `PUT /pedidos/{id}`
  - `RastreamentoDbContext.Pedidos`

**Contexto — três coisas que mudam em relação ao molde do catálogo:**

1. **Autoria.** `Cadastrar` recebe `int usuarioId` como parâmetro; quem lê a claim `sub` é o
   controller (`Application` não conhece `HttpContext`). O padrão de leitura é o do
   `MeController`: `int.TryParse(User.FindFirst("sub")?.Value, ...)`, e claim ausente/ilegível
   vira **401**, não 500.
2. **Sem `Ativo`, sem `DefinirAtivo`, sem `DELETE`** — `Pedido` é documento (ver a spec, "Política
   de exclusão"). Corrige-se por `PUT`, não some.
3. **Campos que a Fase 1 não escreve:** `Tipo` nasce fixo em `'Fabricacao'` e `Status` em
   `'Aberto'`; `PedidoOrigemId`, `MotivoRetrabalho` e `DataConclusao` ficam nulos. Retrabalho é
   Fase 5, transição de status é Fase 3. As propriedades existem na entidade porque o mapeamento
   EF precisa cobrir a tabela, mas nada nesta fase as preenche.

**Desvio consciente da spec (endpoint table):** a spec previa `GET /pedidos` "com contagem de
Agrupamentos" e `GET /pedidos/{id}` "inclui os Agrupamentos". Aqui os dois devolvem **só o
Pedido**, e os Agrupamentos saem por `GET /pedidos/{id}/agrupamentos` (Task 10). Motivo: contar ou
embutir Agrupamento exigiria, nesta task, ou mapear a entidade antes da hora ou escrever SQL cru
que a Task 10 substituiria em seguida — churn puro. A tela de detalhe (Task 11) faz as duas
chamadas, que é o desenho REST usual de sub-recurso. **Nada se perde do critério de pronto**: os
Agrupamentos continuam visíveis a partir do Pedido.

- [ ] **Step 1: Acrescentar o fake do repositório**

Modify `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` — acrescentar ao fim:

```csharp
public class FakePedidoRepo : IPedidoRepository
{
    private readonly List<Pedido> _linhas;
    private int _proximoId;

    public FakePedidoRepo(params Pedido[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(p => p.Id) + 1;
    }

    public int Saves { get; private set; }

    public Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(p => p.Id == id));

    public Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(p => p.Numero == numero));

    public Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Pedido>>(
            _linhas.OrderByDescending(p => p.DataAbertura).ToList());

    public Task AdicionarAsync(Pedido pedido, CancellationToken ct)
    {
        pedido.Id = _proximoId++;
        _linhas.Add(pedido);
        return Task.CompletedTask;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Escrever os testes do use case (vão falhar)**

Create `tests/Rastreamento.Application.Tests/Cadastros/CadastroDePedidoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDePedidoUseCaseTests
{
    private const int UsuarioDaSessao = 42;

    [Fact]
    public async Task Cadastra_pedido_aberto_de_fabricacao()
    {
        var repo = new FakePedidoRepo();
        var useCase = new CadastroDePedidoUseCase(repo);

        var resultado = await useCase.Cadastrar(
            new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("PED-001", resultado.Valor!.Numero);
        Assert.Equal("Fabricacao", resultado.Valor.Tipo);
        Assert.Equal("Aberto", resultado.Valor.Status);
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task Cadastra_gravando_o_autor_recebido_por_parametro()
    {
        // A autoria vem de FORA do use case: quem le a claim `sub` e o controller.
        var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

        var resultado = await useCase.Cadastrar(
            new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

        Assert.Equal(UsuarioDaSessao, resultado.Valor!.CriadoPorUsuarioId);
    }

    [Fact]
    public async Task Data_de_abertura_nasce_em_utc()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

        var resultado = await useCase.Cadastrar(
            new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

        Assert.InRange(resultado.Valor!.DataAbertura, antes, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task Numero_duplicado_e_conflito_e_nao_escreve_nada()
    {
        var repo = new FakePedidoRepo(new Pedido { Id = 1, Numero = "PED-001", Cliente = "Y" });
        var useCase = new CadastroDePedidoUseCase(repo);

        var resultado = await useCase.Cadastrar(
            new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Duplicado_de_pedido_nunca_e_reativavel()
    {
        // Pedido nao tem coluna Ativo: `existeInativo` e sempre false, e a tela nao oferece
        // "reativar o existente" — o caminho de correcao e editar o Pedido que ja existe.
        var repo = new FakePedidoRepo(new Pedido { Id = 8, Numero = "PED-001", Cliente = "Y" });
        var useCase = new CadastroDePedidoUseCase(repo);

        var duplicado = await useCase.LocalizarDuplicado("PED-001", CancellationToken.None);

        Assert.NotNull(duplicado);
        Assert.Equal("numero", duplicado!.Campo);
        Assert.False(duplicado.ExisteInativo);
        Assert.Equal(8, duplicado.IdExistente);
    }

    [Theory]
    [InlineData("", "Cliente X")]
    [InlineData("PED-001", "   ")]
    public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(string numero, string cliente)
    {
        var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

        var resultado = await useCase.Cadastrar(
            new NovoPedidoDto(numero, cliente), UsuarioDaSessao, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_pedido_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

        var resultado = await useCase.Editar(
            99, new NovoPedidoDto("PED-001", "Cliente X"), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_nao_troca_o_autor()
    {
        // Autoria e do momento da criacao: editar nao reescreve quem abriu o Pedido.
        var repo = new FakePedidoRepo(new Pedido
        {
            Id = 1, Numero = "PED-001", Cliente = "Y", CriadoPorUsuarioId = 7,
            Tipo = "Fabricacao", Status = "Aberto",
        });
        var useCase = new CadastroDePedidoUseCase(repo);

        var resultado = await useCase.Editar(
            1, new NovoPedidoDto("PED-001", "Cliente Z"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal("Cliente Z", resultado.Valor!.Cliente);
        Assert.Equal(7, resultado.Valor.CriadoPorUsuarioId);
    }

    [Fact]
    public async Task Editar_para_numero_de_outro_pedido_e_conflito()
    {
        var repo = new FakePedidoRepo(
            new Pedido { Id = 1, Numero = "PED-001", Cliente = "A" },
            new Pedido { Id = 2, Numero = "PED-002", Cliente = "B" });
        var useCase = new CadastroDePedidoUseCase(repo);

        var resultado = await useCase.Editar(
            2, new NovoPedidoDto("PED-001", "B"), CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Obter_pedido_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

        var resultado = await useCase.Obter(99, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDePedidoUseCaseTests`
Expected: FALHA de compilação — `Pedido`, `IPedidoRepository`, `CadastroDePedidoUseCase`, `NovoPedidoDto` não existem.

- [ ] **Step 4: Criar a entidade e o contrato do repositório**

Create `src/Rastreamento.Domain/Entities/Pedido.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Documento: nao tem `Ativo` e nao se exclui (ver a spec da Fase 1, "Politica de exclusao").
/// Os campos de retrabalho e de conclusao existem porque o mapeamento cobre a tabela inteira;
/// nada na Fase 1 os preenche (retrabalho e Fase 5, transicao de status e Fase 3).
/// </summary>
public class Pedido
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int? PedidoOrigemId { get; set; }
    public string? MotivoRetrabalho { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DataAbertura { get; set; }
    public DateTime? DataConclusao { get; set; }

    /// <summary>Autoria: responde "quem abriu este pedido". FK para dbo.Usuario.</summary>
    public int CriadoPorUsuarioId { get; set; }
}
```

Create `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IPedidoRepository
{
    /// <summary>Entidade RASTREADA: `Editar` muta e conta com o change tracking.</summary>
    Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Existe para o use case detectar duplicidade ANTES do insert e devolver erro de negocio, em
    /// vez de deixar a violacao de UQ_Pedido_Numero estourar como excecao ate a API.
    /// </summary>
    Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct);

    /// <summary>Sem filtro de ativo/inativo: Pedido nao tem essa coluna — a lista e completa.</summary>
    Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct);

    Task AdicionarAsync(Pedido pedido, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Criar os DTOs**

Modify `src/Rastreamento.Application/Cadastros/Dtos.cs` — acrescentar ao fim:

```csharp
/// <remarks>
/// `DataAbertura` sai daqui em UTC; quem converte para GMT-3 e o HorarioDeBrasiliaJsonConverter,
/// registrado uma vez em Program.cs — nenhum endpoint precisa converter na mao.
/// </remarks>
public sealed record PedidoDto(
    int Id,
    string Numero,
    string Cliente,
    string Tipo,
    string Status,
    DateTime DataAbertura,
    int CriadoPorUsuarioId);

/// <remarks>
/// So `Numero` e `Cliente`: `Tipo` e `Status` sao decididos pelo use case, e o autor vem da claim
/// da sessao. Nenhum dos tres se aceita do cliente. Os `MaxLength` espelham `dbo.Pedido`.
/// </remarks>
public sealed record NovoPedidoDto(
    [property: MaxLength(30)] string Numero,
    [property: MaxLength(200)] string Cliente);
```

- [ ] **Step 6: Implementar o use case**

Create `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Pedido: criar, editar, listar e obter. Nao ha inativacao nem exclusao — documento
/// se corrige por edicao (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDePedidoUseCase
{
    /// <summary>Fase 1 so abre Pedido de fabricacao; Retrabalho e Fase 5.</summary>
    private const string TipoFabricacao = "Fabricacao";

    /// <summary>Todo Pedido nasce Aberto; quem muda o status e o primeiro apontamento (Fase 3).</summary>
    private const string StatusAberto = "Aberto";

    private readonly IPedidoRepository _repositorio;

    public CadastroDePedidoUseCase(IPedidoRepository repositorio) => _repositorio = repositorio;

    public async Task<Result<PedidoDto>> Cadastrar(
        NovoPedidoDto novo, int usuarioId, CancellationToken ct)
    {
        var (numero, cliente) = Normalizar(novo);
        if (numero.Length == 0 || cliente.Length == 0)
            return Result<PedidoDto>.Falha("Numero e cliente sao obrigatorios.", TipoDeErro.Validacao);

        if (await _repositorio.ObterPorNumeroAsync(numero, ct) is not null)
            return Result<PedidoDto>.Falha("Ja existe um Pedido com este numero.", TipoDeErro.Conflito);

        var pedido = new Pedido
        {
            Numero = numero,
            Cliente = cliente,
            Tipo = TipoFabricacao,
            Status = StatusAberto,
            // Em UTC, como todo o resto do sistema. O DEFAULT do banco existe, mas o EF sempre
            // manda a coluna no INSERT — entao quem define o valor de verdade e esta linha.
            DataAbertura = DateTime.UtcNow,
            CriadoPorUsuarioId = usuarioId,
        };

        await _repositorio.AdicionarAsync(pedido, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<PedidoDto>.Ok(Projetar(pedido));
    }

    /// <remarks>
    /// Editar nao toca em `CriadoPorUsuarioId`: autoria e do momento da criacao. Tambem nao ha
    /// guarda por status — na Fase 1 todo Pedido esta Aberto, porque nada transiciona status
    /// ainda. Quando a Fase 3 introduzir a transicao, a guarda de "so edita Pedido Aberto"
    /// pertence a ela, nao a esta.
    /// </remarks>
    public async Task<Result<PedidoDto>> Editar(
        int id, NovoPedidoDto alterado, CancellationToken ct)
    {
        var (numero, cliente) = Normalizar(alterado);
        if (numero.Length == 0 || cliente.Length == 0)
            return Result<PedidoDto>.Falha("Numero e cliente sao obrigatorios.", TipoDeErro.Validacao);

        var pedido = await _repositorio.ObterPorIdAsync(id, ct);
        if (pedido is null)
            return Result<PedidoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado);

        var homonimo = await _repositorio.ObterPorNumeroAsync(numero, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<PedidoDto>.Falha("Ja existe um Pedido com este numero.", TipoDeErro.Conflito);

        pedido.Numero = numero;
        pedido.Cliente = cliente;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<PedidoDto>.Ok(Projetar(pedido));
    }

    public async Task<IReadOnlyList<PedidoDto>> Listar(CancellationToken ct)
    {
        var pedidos = await _repositorio.ListarAsync(ct);
        return pedidos.Select(Projetar).ToList();
    }

    public async Task<Result<PedidoDto>> Obter(int id, CancellationToken ct)
    {
        var pedido = await _repositorio.ObterPorIdAsync(id, ct);
        return pedido is null
            ? Result<PedidoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado)
            : Result<PedidoDto>.Ok(Projetar(pedido));
    }

    /// <summary>
    /// Detalhe do 409. `ExisteInativo` e sempre false — Pedido nao tem coluna `Ativo`, entao nao
    /// existe "reativar o existente" aqui; o caminho de correcao e editar o Pedido que ja existe.
    /// </summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string numero, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorNumeroAsync(numero.Trim(), ct);
        return existente is null ? null : new ValorDuplicadoDto("numero", false, existente.Id);
    }

    private static (string Numero, string Cliente) Normalizar(NovoPedidoDto d) =>
        (d.Numero?.Trim() ?? string.Empty, d.Cliente?.Trim() ?? string.Empty);

    private static PedidoDto Projetar(Pedido p) =>
        new(p.Id, p.Numero, p.Cliente, p.Tipo, p.Status, p.DataAbertura, p.CriadoPorUsuarioId);
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDePedidoUseCaseTests`
Expected: PASS — 11 testes (o `[Theory]` conta 2).

- [ ] **Step 8: Criar o mapeamento, o repositório e o `DbSet`**

Create `src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("Pedido");
        b.HasKey(p => p.Id);
        b.Property(p => p.Numero).HasMaxLength(30).IsRequired();
        b.Property(p => p.Cliente).HasMaxLength(200).IsRequired();
        b.Property(p => p.Tipo).HasMaxLength(20).IsRequired();
        b.Property(p => p.MotivoRetrabalho).HasMaxLength(30);
        b.Property(p => p.Status).HasMaxLength(20).IsRequired();
        // Sem HasDefaultValue: Database First — os DEFAULT vivem so no .sql, e o use case e quem
        // define Status, Tipo e DataAbertura no insert.
    }
}
```

Create `src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class PedidoRepository : IPedidoRepository
{
    private readonly RastreamentoDbContext _db;

    public PedidoRepository(RastreamentoDbContext db) => _db = db;

    public Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Pedidos.SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct) =>
        _db.Pedidos.SingleOrDefaultAsync(p => p.Numero == numero, ct);

    public async Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct) =>
        await _db.Pedidos.AsNoTracking()
            .OrderByDescending(p => p.DataAbertura)
            .ToListAsync(ct);

    public async Task AdicionarAsync(Pedido pedido, CancellationToken ct) =>
        await _db.Pedidos.AddAsync(pedido, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

Modify `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` — após `public DbSet<Material> Materiais => Set<Material>();`:

```csharp
    public DbSet<Pedido> Pedidos => Set<Pedido>();
```

- [ ] **Step 9: Provar o mapeamento contra o banco — autoria e o default de `DataAbertura`**

Create `tests/Rastreamento.Infrastructure.Tests/Persistence/PedidoMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Requer o SQL Server no ar (docker compose up -d) com o schema e o db/seed.sql aplicados —
/// e o unico lugar que prova as colunas de autoria da Task 1 contra o DDL de verdade.
/// </summary>
public class PedidoMappingTests
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    /// <summary>FK_Pedido_CriadoPorUsuario nao aceita autor inventado: o Id sai do banco.</summary>
    private static async Task<int> IdDoAdmin(RastreamentoDbContext db) =>
        (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;

    [Fact]
    public async Task Mapeia_pedido_com_a_coluna_de_autoria()
    {
        await using var db = NovoContexto();
        var autor = await IdDoAdmin(db);
        var pedido = new Pedido
        {
            Numero = $"map-{Guid.NewGuid():N}"[..25],
            Cliente = "Cliente de teste",
            Tipo = "Fabricacao",
            Status = "Aberto",
            DataAbertura = DateTime.UtcNow,
            CriadoPorUsuarioId = autor,
        };

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        var id = pedido.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);

            Assert.Equal(pedido.Numero, carregado.Numero);
            Assert.Equal("Fabricacao", carregado.Tipo);
            Assert.Equal("Aberto", carregado.Status);
            Assert.Equal(autor, carregado.CriadoPorUsuarioId);
            Assert.Null(carregado.PedidoOrigemId);
            Assert.Null(carregado.MotivoRetrabalho);
            Assert.Null(carregado.DataConclusao);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Pedidos.RemoveRange(await dbLimpeza.Pedidos.Where(p => p.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Data_de_abertura_e_status_nascem_pelos_defaults_do_banco()
    {
        // INSERT cru omitindo Status e DataAbertura: e o unico jeito de provar DF_Pedido_Status e
        // DF_Pedido_DataAbertura, porque o EF sempre manda as colunas (Database First — os DEFAULT
        // vivem so no .sql, e o use case e quem define os valores no caminho normal).
        await using var db = NovoContexto();
        var autor = await IdDoAdmin(db);
        var numero = $"def-{Guid.NewGuid():N}"[..25];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, CriadoPorUsuarioId) VALUES ({numero}, 'Teste', 'Fabricacao', {autor})");

        var id = await db.Database
            .SqlQuery<int>($"SELECT Id AS [Value] FROM dbo.Pedido WHERE Numero = {numero}").SingleAsync();

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Pedidos.AsNoTracking().SingleAsync(p => p.Id == id);

            Assert.Equal("Aberto", carregado.Status);
            // SYSUTCDATETIME(): a data do banco e UTC, nao o horario local do servidor.
            Assert.InRange(carregado.DataAbertura, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Pedidos.RemoveRange(await dbLimpeza.Pedidos.Where(p => p.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~PedidoMappingTests`
Expected: PASS — 2 testes. Se `Data_de_abertura...` falhar com erro de coluna inexistente, a Task 1 não foi aplicada neste banco: rodar o `ALTER` idempotente do Step 3 dela.

- [ ] **Step 10: Escrever os testes de endpoint (vão falhar)**

Create `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Pedido, contra o SQL Server real (docker compose up -d).
/// </summary>
public class PedidosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _numerosCriados = [];

    public PedidosEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        db.Pedidos.RemoveRange(
            await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private string NumeroUnico()
    {
        var numero = $"ped-{Guid.NewGuid():N}"[..25];
        _numerosCriados.Add(numero);
        return numero;
    }

    /// <summary>
    /// Id de um Usuario que EXISTE (o `admin` do db/seed.sql). O token precisa apontar para uma
    /// linha real porque FK_Pedido_CriadoPorUsuario nao aceita autor inventado — nos testes de
    /// catalogo isso nao importava, aqui importa. O perfil vem do parametro; o Id, do banco.
    /// </summary>
    private int IdDeUsuarioReal()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        return db.Usuarios.Single(u => u.NomeUsuario == "admin").Id;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenDeTeste.Emitir(_factory, perfil, IdDeUsuarioReal()));
        return cliente;
    }

    [Fact]
    public async Task Pcp_cadastra_pedido_aberto_de_fabricacao_com_autor()
    {
        var resposta = await ClienteComo("PCP")
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Aberto", corpo.GetProperty("status").GetString());
        Assert.Equal("Fabricacao", corpo.GetProperty("tipo").GetString());
        Assert.Equal(IdDeUsuarioReal(), corpo.GetProperty("criadoPorUsuarioId").GetInt32());
    }

    [Fact]
    public async Task Administrador_tambem_cadastra_pedido()
    {
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Qualidade_nao_cadastra_pedido_mas_le_a_lista()
    {
        var cliente = ClienteComo("Qualidade");

        var escrita = await cliente.PostAsJsonAsync(
            "/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });
        var leitura = await cliente.GetAsync("/pedidos");

        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task Numero_duplicado_responde_409_nao_reativavel()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });

        var resposta = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente Y" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("numero", corpo.GetProperty("campo").GetString());
        Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
    }

    [Fact]
    public async Task Obter_pedido_devolve_o_que_foi_cadastrado()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        var criado = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var resposta = await cliente.GetAsync($"/pedidos/{id}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(numero, corpo.GetProperty("numero").GetString());
        Assert.Equal("Cliente X", corpo.GetProperty("cliente").GetString());
    }

    [Fact]
    public async Task Obter_pedido_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP").GetAsync("/pedidos/999999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Editar_altera_o_cliente_sem_trocar_o_autor()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        var criado = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        var corpoCriado = JsonDocument.Parse(await criado.Content.ReadAsStringAsync()).RootElement;
        var id = corpoCriado.GetProperty("id").GetInt32();
        var autor = corpoCriado.GetProperty("criadoPorUsuarioId").GetInt32();

        var resposta = await cliente.PutAsJsonAsync(
            $"/pedidos/{id}", new { numero, cliente = "Cliente Z" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Cliente Z", corpo.GetProperty("cliente").GetString());
        Assert.Equal(autor, corpo.GetProperty("criadoPorUsuarioId").GetInt32());
    }

    [Fact]
    public async Task Cliente_em_branco_responde_400()
    {
        var resposta = await ClienteComo("PCP")
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/pedidos");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
```

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~PedidosEndpointsTests`
Expected: FALHA — 404 em todas as rotas (o controller não existe).

- [ ] **Step 11: Criar o controller e registrar as dependências**

Create `src/Rastreamento.Api/Controllers/PedidosController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("pedidos")]
[Authorize]
public class PedidosController : CadastroControllerBase
{
    private readonly CadastroDePedidoUseCase _cadastro;

    public PedidosController(CadastroDePedidoUseCase cadastro) => _cadastro = cadastro;

    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await _cadastro.Listar(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id, CancellationToken ct)
    {
        var resultado = await _cadastro.Obter(id, ct);
        return resultado.Sucesso ? Ok(resultado.Valor) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Cadastrar([FromBody] NovoPedidoDto novo, CancellationToken ct)
    {
        // UsuarioDaSessao vem da base: e a unica leitura de HttpContext do cadastro de Pedido.
        var usuarioId = UsuarioDaSessao();
        if (usuarioId is null) return Unauthorized();

        var resultado = await _cadastro.Cadastrar(novo, usuarioId.Value, ct);
        if (resultado.Sucesso)
            return CreatedAtAction(nameof(Obter), new { id = resultado.Valor!.Id }, resultado.Valor);

        return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Numero), ct);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoPedidoDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(
                resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Numero), ct);
    }

    /// <summary>
    /// Como Pedido pergunta pelo duplicado: por numero (UQ_Pedido_Numero). O `existeInativo` que
    /// volta e sempre false — Pedido nao tem coluna `Ativo`, entao a tela nao oferece reativacao.
    /// </summary>
    private LocalizadorDeDuplicado Duplicado(string numero) =>
        ct => _cadastro.LocalizarDuplicado(numero, ct);
}
```

Modify `src/Rastreamento.Api/Program.cs` — logo abaixo do registro de `CadastroDeMaterialUseCase`:

```csharp
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<CadastroDePedidoUseCase>();
```

- [ ] **Step 12: Rodar tudo**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; toda a suíte passando (9 novos de Api, 11 de Application, 2 de Infrastructure, mais os anteriores).

- [ ] **Step 13: Commit**

```bash
git add src/Rastreamento.Domain/Entities/Pedido.cs src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs src/Rastreamento.Application/Cadastros/ src/Rastreamento.Api/Controllers/PedidosController.cs src/Rastreamento.Api/Program.cs tests/
git commit -m "feat(pedido): cadastro com autoria a partir da claim da sessao"
```

---

## Task 9: Tela de Pedidos

**Files:**
- Modify: `web/src/api/cadastros.ts`
- Modify: `web/src/api/cadastros.test.ts`
- Create: `web/src/pages/PedidosPage.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/pages/HomePage.tsx`

**Interfaces:**
- Consumes: rotas da Task 8
- Produces:
  - `PedidoDto { id: number; numero: string; cliente: string; tipo: string; status: string; dataAbertura: string; criadoPorUsuarioId: number }`
  - `NovoPedido { numero: string; cliente: string }`
  - `listarPedidos(): Promise<PedidoDto[]>`
  - `obterPedido(id: number): Promise<PedidoDto>`
  - `criarPedido(p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro>`
  - `formatarDataHora(isoComOffset: string): string` — usada também pela Task 11

**Contexto:** `dataAbertura` chega como ISO 8601 **já em GMT-3** (o `HorarioDeBrasiliaJsonConverter` é a borda de fuso). Formatar com `new Date(x).toLocaleString()` reconverteria para o fuso do aparelho — num tablet configurado fora do fuso da fábrica o horário mudaria de lugar. Daí `formatarDataHora` ler o texto ISO direto, sem passar por `Date`.

- [ ] **Step 1: Escrever os testes do módulo de API (vão falhar)**

Modify `web/src/api/cadastros.test.ts` — acrescentar `listarPedidos`, `criarPedido` e `formatarDataHora` ao import de `./cadastros` e o bloco abaixo, dentro do `describe`:

```ts
  it('lista pedidos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([{
          id: 1, numero: 'PED-001', cliente: 'Cliente X', tipo: 'Fabricacao',
          status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }]),
        { status: 200 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const pedidos = await listarPedidos()

    expect(pedidos[0].numero).toBe('PED-001')
    expect(fetchMock.mock.calls[0][0]).toBe('/pedidos')
  })

  it('devolve o conflito quando o número do pedido já existe', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 3 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarPedido({ numero: 'PED-001', cliente: 'Cliente X' })

    expect(ehConflito(resultado)).toBe(true)
    expect(ehConflito(resultado) && resultado.existeInativo).toBe(false)
  })

  it('formata a data no fuso que a API entregou, sem reconverter pelo aparelho', () => {
    expect(formatarDataHora('2026-07-28T09:30:00-03:00')).toBe('28/07/2026 09:30')
  })
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `cd web && npm test`
Expected: FALHA — `listarPedidos`, `criarPedido` e `formatarDataHora` não são exportados.

- [ ] **Step 3: Acrescentar as funções ao módulo de API**

Modify `web/src/api/cadastros.ts` — acrescentar ao fim:

```ts
export interface PedidoDto {
  id: number
  numero: string
  cliente: string
  tipo: string
  status: string
  /** ISO 8601 com offset -03:00 — a API ja converteu (HorarioDeBrasiliaJsonConverter). */
  dataAbertura: string
  criadoPorUsuarioId: number
}

export interface NovoPedido {
  numero: string
  cliente: string
}

/**
 * Formata o ISO que a API mandou SEM passar por `Date`: a data ja vem em GMT-3, e
 * `new Date(x).toLocaleString()` a reconverteria para o fuso do aparelho — num tablet fora do
 * fuso da fabrica o horario apareceria deslocado.
 */
export function formatarDataHora(isoComOffset: string): string {
  const [data, hora] = isoComOffset.split('T')
  const [ano, mes, dia] = data.split('-')
  return `${dia}/${mes}/${ano} ${hora.slice(0, 5)}`
}

export async function listarPedidos(): Promise<PedidoDto[]> {
  const resp = await apiFetch('/pedidos')
  if (!resp.ok) throw new Error(`Falha ao listar pedidos (${resp.status}).`)
  return (await resp.json()) as PedidoDto[]
}

export async function obterPedido(id: number): Promise<PedidoDto> {
  const resp = await apiFetch(`/pedidos/${id}`)
  if (!resp.ok) throw new Error(`Falha ao carregar o pedido (${resp.status}).`)
  return (await resp.json()) as PedidoDto
}

export function criarPedido(p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch('/pedidos', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}

export function editarPedido(
  id: number,
  p: NovoPedido,
): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch(`/pedidos/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `cd web && npm test`
Expected: PASS — 3 testes novos, mais os que já existiam.

- [ ] **Step 5: Criar a tela**

Create `web/src/pages/PedidosPage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarPedidos, criarPedido, ehConflito, formatarDataHora,
  type PedidoDto, type NovoPedido,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

export function PedidosPage() {
  const [pedidos, setPedidos] = useState<PedidoDto[]>([])
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    try {
      setPedidos(await listarPedidos())
    } catch {
      setErro('Não foi possível carregar os pedidos.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  async function salvar(e: React.FormEvent) {
    e.preventDefault()
    setErro(null)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido nao tem reativacao (nao ha coluna Ativo): o caminho e abrir o que ja existe.
        setErro('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch {
      setErro('Não foi possível salvar o pedido.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Pedidos</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.numero}
          onChange={(e) => setForm({ ...form, numero: e.target.value })}
          placeholder="Número do pedido"
          className="border rounded px-3 py-2"
        />
        <input
          value={form.cliente}
          onChange={(e) => setForm({ ...form, cliente: e.target.value })}
          placeholder="Cliente"
          className="border rounded px-3 py-2"
        />
        <button type="submit" className="border rounded px-3 py-2 self-start">Abrir pedido</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {pedidos.map((p) => (
            <li key={p.id} className="border rounded px-3 py-2">
              <Link to={`/pedidos/${p.id}`} className="flex flex-col gap-1">
                <span className="font-medium">{p.numero} — {p.cliente}</span>
                <span className="text-sm text-gray-500">
                  {p.status} · aberto em {formatarDataHora(p.dataAbertura)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
```

- [ ] **Step 6: Registrar a rota e o link**

Modify `web/src/App.tsx` — acrescentar o import e a rota:

```tsx
import { PedidosPage } from './pages/PedidosPage'
```

```tsx
      <Route path="/pedidos" element={<ProtectedRoute><PedidosPage /></ProtectedRoute>} />
```

Modify `web/src/pages/HomePage.tsx` — na `div` de botões:

```tsx
        <Link to="/pedidos" className="border rounded px-3 py-2">Pedidos</Link>
```

- [ ] **Step 7: Verificar build, lint e testes do front**

Run: `cd web && npm run build && npm run lint && npm test`
Expected: build sem erro de TypeScript, lint limpo, testes passando.

> O link `/pedidos/{id}` já aponta para a tela de detalhe, que só nasce na Task 11. Até lá o
> clique cai no `<Route path="*">` e volta para a Home — comportamento visível e inofensivo,
> resolvido na Task 11.

- [ ] **Step 8: Commit**

```bash
git add web/src/api/cadastros.ts web/src/api/cadastros.test.ts web/src/pages/PedidosPage.tsx web/src/App.tsx web/src/pages/HomePage.tsx
git commit -m "feat(pedido): tela de abertura e listagem"
```

---

## Task 10: `Agrupamento` — backend completo, com `DELETE` guardado

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Agrupamento.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IAgrupamentoRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/AgrupamentoConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/AgrupamentoRepository.cs`
- Create: `src/Rastreamento.Application/Cadastros/CadastroDeAgrupamentoUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/AgrupamentosController.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (acrescentar `FakeAgrupamentoRepo`)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeAgrupamentoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/AgrupamentoMappingTests.cs`
- Test: `tests/Rastreamento.Api.Tests/AgrupamentosEndpointsTests.cs`

**Interfaces:**
- Consumes: `IPedidoRepository` (Task 8); `CadastroControllerBase` (Task 4); colunas de autoria (Task 1)
- Produces:
  - `Rastreamento.Domain.Entities.Agrupamento` — `int Id`, `int PedidoId`, `string Codigo`, `decimal Quantidade`, `string Tipo`, `DateTime? DataConclusao`, `int CriadoPorUsuarioId`, `DateTime CriadoEm`
  - `IAgrupamentoRepository` — `Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct)`, `Task<Agrupamento?> ObterPorPedidoECodigoAsync(int pedidoId, string codigo, CancellationToken ct)`, `Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(int pedidoId, CancellationToken ct)`, `Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct)`, `Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct)`, `Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct)`, `Task SalvarAlteracoesAsync(CancellationToken ct)`
  - `AgrupamentoDto(int Id, int PedidoId, string Codigo, decimal Quantidade, string Tipo, DateTime CriadoEm, int CriadoPorUsuarioId)`
  - `NovoAgrupamentoDto(string Codigo, decimal Quantidade, string Tipo)`
  - `CadastroDeAgrupamentoUseCase` — `Cadastrar(int pedidoId, NovoAgrupamentoDto novo, int usuarioId, ct)`, `Editar(int id, NovoAgrupamentoDto alterado, ct)`, `ListarPorPedido(int pedidoId, ct)`, `Obter(int id, ct)`, `Excluir(int id, ct)`, `LocalizarDuplicado(int pedidoId, string codigo, ct)`
  - Rotas `GET/POST /pedidos/{pedidoId}/agrupamentos`, `GET/PUT/DELETE /agrupamentos/{id}`

**Contexto — o ponto de desenho que a spec deixou em aberto, agora resolvido:** a guarda do
`DELETE` precisa saber se o Agrupamento tem `EstruturaItem`, tabela da **Fase 2**, sem entidade
nem repositório. A saída adotada é `IAgrupamentoRepository.TemEstruturaAsync`, implementada por
**SQL direto** contra `dbo.EstruturaItem`, sem mapear a entidade. Assim 1A fecha no próprio
escopo; mapear `EstruturaItem` aqui puxaria a Fase 2 para dentro da Fase 1 — e um mapeamento
criado só para servir de guarda envelheceria errado quando a Fase 2 desenhasse a árvore de
verdade. Quando `EstruturaItem` for mapeado, este método vira uma query LINQ e o contrato não muda.

**Os dois 409 de regra de negócio** (`AgrupamentoNaoVazio`, `PedidoNaoAberto`) viajam como
**código**, não como frase, no `Erro` do `Result` — é o que a spec define no corpo da resposta. O
controller apenas repassa; **não** compara a string nem deriva comportamento dela (que é contra o
que o comentário de `Result.cs` avisa). Quem traduz código em texto é a tela.

**Duplicidade é composta:** `UQ_Agrupamento_PedidoCodigo` é `(PedidoId, Codigo)`. O mesmo código
pode existir em Pedidos diferentes; `ExisteInativo` é sempre `false` (não há coluna `Ativo`).

- [ ] **Step 1: Acrescentar o fake do repositório**

Modify `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` — acrescentar ao fim:

```csharp
public class FakeAgrupamentoRepo : IAgrupamentoRepository
{
    private readonly List<Agrupamento> _linhas;
    private int _proximoId;

    public FakeAgrupamentoRepo(params Agrupamento[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(a => a.Id) + 1;
    }

    public int Saves { get; private set; }

    /// <summary>Ids que o teste quer fazer passar por "tem EstruturaItem" (tabela da Fase 2).</summary>
    public HashSet<int> ComEstrutura { get; } = [];

    public Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(a => a.Id == id));

    public Task<Agrupamento?> ObterPorPedidoECodigoAsync(
        int pedidoId, string codigo, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(a => a.PedidoId == pedidoId && a.Codigo == codigo));

    public Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(int pedidoId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Agrupamento>>(
            _linhas.Where(a => a.PedidoId == pedidoId).OrderBy(a => a.Codigo).ToList());

    public Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct)
    {
        agrupamento.Id = _proximoId++;
        _linhas.Add(agrupamento);
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct)
    {
        _linhas.Remove(agrupamento);
        return Task.CompletedTask;
    }

    public Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct) =>
        Task.FromResult(ComEstrutura.Contains(agrupamentoId));

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Escrever os testes do use case (vão falhar)**

Create `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeAgrupamentoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeAgrupamentoUseCaseTests
{
    private const int UsuarioDaSessao = 42;

    private static Pedido PedidoAberto(int id = 1) =>
        new() { Id = id, Numero = $"PED-{id:000}", Cliente = "X", Tipo = "Fabricacao", Status = "Aberto" };

    private static NovoAgrupamentoDto Kit(string codigo = "AG-01") => new(codigo, 10m, "Kit");

    [Fact]
    public async Task Cadastra_agrupamento_no_pedido_com_autoria_e_timestamp()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        var repo = new FakeAgrupamentoRepo();
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, resultado.Valor!.PedidoId);
        Assert.Equal(10m, resultado.Valor.Quantidade);
        Assert.Equal(UsuarioDaSessao, resultado.Valor.CriadoPorUsuarioId);
        Assert.InRange(resultado.Valor.CriadoEm, antes, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(1, repo.Saves);
    }

    [Fact]
    public async Task Cadastrar_em_pedido_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDeAgrupamentoUseCase(new FakeAgrupamentoRepo(), new FakePedidoRepo());

        var resultado = await useCase.Cadastrar(99, Kit(), UsuarioDaSessao, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Codigo_repetido_no_MESMO_pedido_e_conflito()
    {
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Codigo_repetido_em_OUTRO_pedido_nao_e_conflito()
    {
        // UQ_Agrupamento_PedidoCodigo e composta: "AG-01" pode existir uma vez por Pedido.
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 2, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        var useCase = new CadastroDeAgrupamentoUseCase(
            repo, new FakePedidoRepo(PedidoAberto(), PedidoAberto(2)));

        var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

        Assert.True(resultado.Sucesso);
    }

    [Theory]
    [InlineData("", 10, "Kit")]
    [InlineData("AG-01", 0, "Kit")]
    [InlineData("AG-01", -1, "Kit")]
    [InlineData("AG-01", 10, "Conjunto")]
    public async Task Entrada_invalida_e_erro_de_validacao(string codigo, decimal qtd, string tipo)
    {
        // Tipo fora de Kit|Avulso e barrado aqui, e nao pelo CK_Agrupamento_Tipo: excecao de CHECK
        // subiria como 500 em vez de 400 (specs/03-arquitetura-tecnica.md:25-27).
        var useCase = new CadastroDeAgrupamentoUseCase(
            new FakeAgrupamentoRepo(), new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Cadastrar(
            1, new NovoAgrupamentoDto(codigo, qtd, tipo), UsuarioDaSessao, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Excluir_agrupamento_vazio_de_pedido_aberto_apaga_de_verdade()
    {
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Excluir(5, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, repo.Saves);
        Assert.Empty(await useCase.ListarPorPedido(1, CancellationToken.None));
    }

    [Fact]
    public async Task Excluir_agrupamento_com_estrutura_e_bloqueado()
    {
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        repo.ComEstrutura.Add(5);
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Excluir(5, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal("AgrupamentoNaoVazio", resultado.Erro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Excluir_agrupamento_de_pedido_nao_aberto_e_bloqueado()
    {
        var pedido = PedidoAberto();
        pedido.Status = "EmProducao";
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(pedido));

        var resultado = await useCase.Excluir(5, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
        Assert.Equal("PedidoNaoAberto", resultado.Erro);
        Assert.Equal(0, repo.Saves);
    }

    [Fact]
    public async Task Excluir_agrupamento_inexistente_e_nao_encontrado()
    {
        var useCase = new CadastroDeAgrupamentoUseCase(new FakeAgrupamentoRepo(), new FakePedidoRepo());

        var resultado = await useCase.Excluir(99, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    }

    [Fact]
    public async Task Editar_nao_troca_o_pedido_nem_o_autor()
    {
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
            CriadoPorUsuarioId = 7, CriadoEm = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var resultado = await useCase.Editar(
            5, new NovoAgrupamentoDto("AG-01", 20m, "Avulso"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(20m, resultado.Valor!.Quantidade);
        Assert.Equal("Avulso", resultado.Valor.Tipo);
        Assert.Equal(1, resultado.Valor.PedidoId);
        Assert.Equal(7, resultado.Valor.CriadoPorUsuarioId);
    }

    [Fact]
    public async Task Localiza_duplicado_nunca_e_reativavel()
    {
        var repo = new FakeAgrupamentoRepo(new Agrupamento
        {
            Id = 5, PedidoId = 1, Codigo = "AG-01", Quantidade = 3m, Tipo = "Kit",
        });
        var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

        var duplicado = await useCase.LocalizarDuplicado(1, "AG-01", CancellationToken.None);

        Assert.NotNull(duplicado);
        Assert.Equal("codigo", duplicado!.Campo);
        Assert.False(duplicado.ExisteInativo);
        Assert.Equal(5, duplicado.IdExistente);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeAgrupamentoUseCaseTests`
Expected: FALHA de compilação — `Agrupamento`, `IAgrupamentoRepository`, `CadastroDeAgrupamentoUseCase`, `NovoAgrupamentoDto` não existem.

- [ ] **Step 4: Criar a entidade e o contrato do repositório**

Create `src/Rastreamento.Domain/Entities/Agrupamento.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Documento, com a UNICA excecao de hard delete do sistema: um Agrupamento vazio, em Pedido
/// Aberto, pode ser apagado de verdade — e so `Codigo` + `Quantidade` + `Tipo`, sem historico a
/// preservar (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public class Agrupamento
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public string Codigo { get; set; } = string.Empty;

    /// <summary>DECIMAL(18,4). A partir da Fase 3 conversa com a conservacao de quantidade.</summary>
    public decimal Quantidade { get; set; }

    /// <summary>Kit | Avulso — descritivo (Kit vai para solda; Avulso nao).</summary>
    public string Tipo { get; set; } = string.Empty;

    public DateTime? DataConclusao { get; set; }

    /// <summary>Autoria: responde "quem criou este agrupamento". FK para dbo.Usuario.</summary>
    public int CriadoPorUsuarioId { get; set; }

    public DateTime CriadoEm { get; set; }
}
```

Create `src/Rastreamento.Domain/Abstractions/IAgrupamentoRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IAgrupamentoRepository
{
    /// <summary>Entidade RASTREADA: `Editar` e `Excluir` contam com o change tracking.</summary>
    Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Duplicidade e por (PedidoId, Codigo) — UQ_Agrupamento_PedidoCodigo e composta, entao o
    /// mesmo codigo pode existir uma vez em cada Pedido.
    /// </summary>
    Task<Agrupamento?> ObterPorPedidoECodigoAsync(int pedidoId, string codigo, CancellationToken ct);

    Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(int pedidoId, CancellationToken ct);

    Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct);

    /// <summary>Hard delete — permitido so pela guarda do use case (vazio + Pedido Aberto).</summary>
    Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct);

    /// <summary>
    /// Existe alguma EstruturaItem apontando para este Agrupamento? E a guarda do DELETE.
    /// `EstruturaItem` e tabela da FASE 2 e ainda nao tem entidade mapeada de proposito — mapear
    /// aqui puxaria a Fase 2 para dentro da Fase 1, e um mapeamento feito so para servir de guarda
    /// envelheceria errado. A implementacao usa SQL direto; quando a Fase 2 mapear a entidade,
    /// isto vira LINQ e o contrato nao muda.
    /// </summary>
    Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Criar os DTOs**

Modify `src/Rastreamento.Application/Cadastros/Dtos.cs` — acrescentar ao fim:

```csharp
public sealed record AgrupamentoDto(
    int Id,
    int PedidoId,
    string Codigo,
    decimal Quantidade,
    string Tipo,
    DateTime CriadoEm,
    int CriadoPorUsuarioId);

/// <remarks>
/// Sem `PedidoId`: ele vem da rota (`POST /pedidos/{pedidoId}/agrupamentos`), nao do corpo — assim
/// nao existe a possibilidade de os dois discordarem. `MaxLength` espelha `dbo.Agrupamento`.
/// </remarks>
public sealed record NovoAgrupamentoDto(
    [property: MaxLength(50)] string Codigo,
    decimal Quantidade,
    [property: MaxLength(20)] string Tipo);
```

- [ ] **Step 6: Implementar o use case**

Create `src/Rastreamento.Application/Cadastros/CadastroDeAgrupamentoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Agrupamento, sempre sob um Pedido. Unico cadastro da Fase 1A com exclusao fisica —
/// e ela e guardada: so Agrupamento vazio, em Pedido Aberto.
/// </summary>
public sealed class CadastroDeAgrupamentoUseCase
{
    private static readonly string[] TiposValidos = ["Kit", "Avulso"];
    private const string StatusAberto = "Aberto";

    private readonly IAgrupamentoRepository _repositorio;
    private readonly IPedidoRepository _pedidos;

    public CadastroDeAgrupamentoUseCase(
        IAgrupamentoRepository repositorio, IPedidoRepository pedidos)
    {
        _repositorio = repositorio;
        _pedidos = pedidos;
    }

    public async Task<Result<AgrupamentoDto>> Cadastrar(
        int pedidoId, NovoAgrupamentoDto novo, int usuarioId, CancellationToken ct)
    {
        var codigo = novo.Codigo?.Trim() ?? string.Empty;
        var tipo = novo.Tipo?.Trim() ?? string.Empty;

        var invalido = Validar(codigo, novo.Quantidade, tipo);
        if (invalido is not null) return Result<AgrupamentoDto>.Falha(invalido, TipoDeErro.Validacao);

        if (await _pedidos.ObterPorIdAsync(pedidoId, ct) is null)
            return Result<AgrupamentoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado);

        if (await _repositorio.ObterPorPedidoECodigoAsync(pedidoId, codigo, ct) is not null)
            return Result<AgrupamentoDto>.Falha(
                "Ja existe um Agrupamento com este codigo neste Pedido.", TipoDeErro.Conflito);

        var agrupamento = new Agrupamento
        {
            PedidoId = pedidoId,
            Codigo = codigo,
            Quantidade = novo.Quantidade,
            Tipo = tipo,
            CriadoPorUsuarioId = usuarioId,
            CriadoEm = DateTime.UtcNow,
        };

        await _repositorio.AdicionarAsync(agrupamento, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    /// <remarks>
    /// Nao troca `PedidoId` (mover Agrupamento de Pedido nao e operacao de cadastro), nem autoria,
    /// nem `CriadoEm`. Editar `Quantidade` e inocuo na Fase 1; a partir da Fase 3 ela conversa com
    /// a conservacao de quantidade, e a guarda correspondente pertence aquela fase.
    /// </remarks>
    public async Task<Result<AgrupamentoDto>> Editar(
        int id, NovoAgrupamentoDto alterado, CancellationToken ct)
    {
        var codigo = alterado.Codigo?.Trim() ?? string.Empty;
        var tipo = alterado.Tipo?.Trim() ?? string.Empty;

        var invalido = Validar(codigo, alterado.Quantidade, tipo);
        if (invalido is not null) return Result<AgrupamentoDto>.Falha(invalido, TipoDeErro.Validacao);

        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        if (agrupamento is null)
            return Result<AgrupamentoDto>.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado);

        var homonimo = await _repositorio.ObterPorPedidoECodigoAsync(agrupamento.PedidoId, codigo, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<AgrupamentoDto>.Falha(
                "Ja existe um Agrupamento com este codigo neste Pedido.", TipoDeErro.Conflito);

        agrupamento.Codigo = codigo;
        agrupamento.Quantidade = alterado.Quantidade;
        agrupamento.Tipo = tipo;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    public async Task<IReadOnlyList<AgrupamentoDto>> ListarPorPedido(
        int pedidoId, CancellationToken ct)
    {
        var agrupamentos = await _repositorio.ListarPorPedidoAsync(pedidoId, ct);
        return agrupamentos.Select(Projetar).ToList();
    }

    public async Task<Result<AgrupamentoDto>> Obter(int id, CancellationToken ct)
    {
        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        return agrupamento is null
            ? Result<AgrupamentoDto>.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado)
            : Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    /// <summary>
    /// Exclusao fisica guardada. As duas recusas viajam como CODIGO no `Erro` ("AgrupamentoNaoVazio",
    /// "PedidoNaoAberto") porque e isso que o contrato de 409 da spec define no corpo; o controller
    /// so repassa e nao deriva comportamento da string. Ordem: existe -> Pedido Aberto -> vazio.
    /// </summary>
    public async Task<Result> Excluir(int id, CancellationToken ct)
    {
        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        if (agrupamento is null)
            return Result.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado);

        var pedido = await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct);
        if (pedido is null || pedido.Status != StatusAberto)
            return Result.Falha("PedidoNaoAberto", TipoDeErro.Conflito);

        if (await _repositorio.TemEstruturaAsync(id, ct))
            return Result.Falha("AgrupamentoNaoVazio", TipoDeErro.Conflito);

        await _repositorio.RemoverAsync(agrupamento, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Detalhe do 409. `ExisteInativo` sempre false: Agrupamento nao tem `Ativo`.</summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(
        int pedidoId, string codigo, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorPedidoECodigoAsync(pedidoId, codigo.Trim(), ct);
        return existente is null ? null : new ValorDuplicadoDto("codigo", false, existente.Id);
    }

    /// <summary>
    /// Devolve a mensagem do primeiro problema, ou null se estiver tudo certo. `Tipo` e validado
    /// aqui, e nao pelo CK_Agrupamento_Tipo: excecao de CHECK subiria como 500 em vez de 400.
    /// </summary>
    private static string? Validar(string codigo, decimal quantidade, string tipo)
    {
        if (codigo.Length == 0) return "Codigo e obrigatorio.";
        if (quantidade <= 0) return "Quantidade deve ser maior que zero.";
        if (!TiposValidos.Contains(tipo)) return "Tipo deve ser Kit ou Avulso.";
        return null;
    }

    private static AgrupamentoDto Projetar(Agrupamento a) =>
        new(a.Id, a.PedidoId, a.Codigo, a.Quantidade, a.Tipo, a.CriadoEm, a.CriadoPorUsuarioId);
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter FullyQualifiedName~CadastroDeAgrupamentoUseCaseTests`
Expected: PASS — 14 testes (o `[Theory]` conta 4).

- [ ] **Step 8: Criar o mapeamento, o repositório e o `DbSet`**

Create `src/Rastreamento.Infrastructure/Persistence/Configurations/AgrupamentoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class AgrupamentoConfiguration : IEntityTypeConfiguration<Agrupamento>
{
    public void Configure(EntityTypeBuilder<Agrupamento> b)
    {
        b.ToTable("Agrupamento");
        b.HasKey(a => a.Id);
        b.Property(a => a.Codigo).HasMaxLength(50).IsRequired();
        b.Property(a => a.Tipo).HasMaxLength(20).IsRequired();
        // DECIMAL(18,4) explicito: sem isso o EF assume decimal(18,2) e trunca a quarta casa em
        // silencio — o que na Fase 3 desalinharia a conservacao de quantidade.
        b.Property(a => a.Quantidade).HasPrecision(18, 4);
    }
}
```

Create `src/Rastreamento.Infrastructure/Persistence/AgrupamentoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class AgrupamentoRepository : IAgrupamentoRepository
{
    private readonly RastreamentoDbContext _db;

    public AgrupamentoRepository(RastreamentoDbContext db) => _db = db;

    public Task<Agrupamento?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Agrupamentos.SingleOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agrupamento?> ObterPorPedidoECodigoAsync(
        int pedidoId, string codigo, CancellationToken ct) =>
        _db.Agrupamentos.SingleOrDefaultAsync(
            a => a.PedidoId == pedidoId && a.Codigo == codigo, ct);

    public async Task<IReadOnlyList<Agrupamento>> ListarPorPedidoAsync(
        int pedidoId, CancellationToken ct) =>
        await _db.Agrupamentos.AsNoTracking()
            .Where(a => a.PedidoId == pedidoId)
            .OrderBy(a => a.Codigo)
            .ToListAsync(ct);

    public async Task AdicionarAsync(Agrupamento agrupamento, CancellationToken ct) =>
        await _db.Agrupamentos.AddAsync(agrupamento, ct);

    public Task RemoverAsync(Agrupamento agrupamento, CancellationToken ct)
    {
        _db.Agrupamentos.Remove(agrupamento);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// SQL direto contra dbo.EstruturaItem, que e tabela da FASE 2 e nao tem entidade mapeada —
    /// ver o contrato em IAgrupamentoRepository. `SqlQuery` com interpolacao vira consulta
    /// parametrizada (nao e concatenacao de string), e `TOP 1` para em quanto acha a primeira.
    /// </remarks>
    public async Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct)
    {
        var achou = await _db.Database
            .SqlQuery<int>(
                $"SELECT TOP 1 1 AS [Value] FROM dbo.EstruturaItem WHERE AgrupamentoId = {agrupamentoId}")
            .ToListAsync(ct);

        return achou.Count > 0;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

Modify `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` — após `public DbSet<Pedido> Pedidos => Set<Pedido>();`:

```csharp
    public DbSet<Agrupamento> Agrupamentos => Set<Agrupamento>();
```

- [ ] **Step 9: Provar o mapeamento contra o banco — autoria, `CriadoEm` e as 4 casas decimais**

Create `tests/Rastreamento.Infrastructure.Tests/Persistence/AgrupamentoMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema e o db/seed.sql aplicados.</summary>
public class AgrupamentoMappingTests
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    /// <summary>Abre um Pedido real: FK_Agrupamento_Pedido nao aceita PedidoId inventado.</summary>
    private static async Task<(int PedidoId, int Autor)> NovoPedido(RastreamentoDbContext db)
    {
        var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;
        var pedido = new Pedido
        {
            Numero = $"agr-{Guid.NewGuid():N}"[..25],
            Cliente = "Cliente de teste",
            Tipo = "Fabricacao",
            Status = "Aberto",
            DataAbertura = DateTime.UtcNow,
            CriadoPorUsuarioId = autor,
        };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return (pedido.Id, autor);
    }

    /// <summary>Agrupamento ANTES de Pedido — FK_Agrupamento_Pedido nao aceita a ordem inversa.</summary>
    private static async Task Limpar(int pedidoId)
    {
        await using var db = NovoContexto();
        db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.PedidoId == pedidoId).ToListAsync());
        await db.SaveChangesAsync();
        db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pedidoId).ToListAsync());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Mapeia_agrupamento_com_autoria_e_quatro_casas_decimais()
    {
        await using var db = NovoContexto();
        var (pedidoId, autor) = await NovoPedido(db);

        try
        {
            var agrupamento = new Agrupamento
            {
                PedidoId = pedidoId,
                Codigo = "AG-01",
                // 4 casas de proposito: se AgrupamentoConfiguration esquecesse HasPrecision(18,4),
                // o EF assumiria decimal(18,2) e este valor voltaria truncado, em silencio.
                Quantidade = 10.1234m,
                Tipo = "Kit",
                CriadoPorUsuarioId = autor,
                CriadoEm = DateTime.UtcNow,
            };
            db.Agrupamentos.Add(agrupamento);
            await db.SaveChangesAsync();
            var id = agrupamento.Id;

            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Agrupamentos.AsNoTracking().SingleAsync(a => a.Id == id);

            Assert.Equal(pedidoId, carregado.PedidoId);
            Assert.Equal("AG-01", carregado.Codigo);
            Assert.Equal(10.1234m, carregado.Quantidade);
            Assert.Equal("Kit", carregado.Tipo);
            Assert.Equal(autor, carregado.CriadoPorUsuarioId);
            Assert.Null(carregado.DataConclusao);
        }
        finally
        {
            await Limpar(pedidoId);
        }
    }

    [Fact]
    public async Task CriadoEm_nasce_pelo_default_do_banco()
    {
        // INSERT cru omitindo CriadoEm: e o unico jeito de provar DF_Agrupamento_CriadoEm, porque
        // o EF sempre manda a coluna no caminho normal.
        await using var db = NovoContexto();
        var (pedidoId, autor) = await NovoPedido(db);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Quantidade, Tipo, CriadoPorUsuarioId) VALUES ({pedidoId}, 'AG-DEF', 1, 'Kit', {autor})");

            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Agrupamentos.AsNoTracking()
                .SingleAsync(a => a.PedidoId == pedidoId && a.Codigo == "AG-DEF");

            // SYSUTCDATETIME(): UTC, nao o horario local do servidor.
            Assert.InRange(carregado.CriadoEm, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        }
        finally
        {
            await Limpar(pedidoId);
        }
    }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter FullyQualifiedName~AgrupamentoMappingTests`
Expected: PASS — 2 testes.

- [ ] **Step 10: Escrever os testes de endpoint (vão falhar)**

Create `tests/Rastreamento.Api.Tests/AgrupamentosEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Agrupamento, contra o SQL Server real (docker compose up -d).
/// A limpeza apaga Agrupamento ANTES de Pedido — FK_Agrupamento_Pedido nao aceita a ordem inversa.
/// </summary>
public class AgrupamentosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _numerosCriados = [];

    public AgrupamentosEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

        var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
        var ids = pedidos.Select(p => p.Id).ToList();

        // EstruturaItem (Fase 2, sem entidade) sai por SQL: um teste insere uma linha de proposito.
        foreach (var id in ids)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId IN (SELECT Id FROM dbo.Agrupamento WHERE PedidoId = {id})");

        db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => ids.Contains(a.PedidoId)).ToListAsync());
        await db.SaveChangesAsync();

        db.Pedidos.RemoveRange(pedidos);
        await db.SaveChangesAsync();
    }

    private int IdDeUsuarioReal()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        return db.Usuarios.Single(u => u.NomeUsuario == "admin").Id;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenDeTeste.Emitir(_factory, perfil, IdDeUsuarioReal()));
        return cliente;
    }

    /// <summary>Abre um Pedido pela propria API e devolve o Id — a base de todos os casos daqui.</summary>
    private async Task<int> NovoPedido(HttpClient cliente)
    {
        var numero = $"ped-{Guid.NewGuid():N}"[..25];
        _numerosCriados.Add(numero);
        var resposta = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();
    }

    private static object Kit(string codigo = "AG-01") =>
        new { codigo, quantidade = 10, tipo = "Kit" };

    [Fact]
    public async Task Pcp_cria_agrupamento_no_pedido_com_autoria_e_timestamp()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(pedidoId, corpo.GetProperty("pedidoId").GetInt32());
        Assert.Equal(IdDeUsuarioReal(), corpo.GetProperty("criadoPorUsuarioId").GetInt32());
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("criadoEm").GetString()));
    }

    [Fact]
    public async Task Operador_nao_cria_agrupamento_mas_le_a_lista()
    {
        var pcp = ClienteComo("PCP");
        var pedidoId = await NovoPedido(pcp);
        var operador = ClienteComo("Operador");

        var escrita = await operador.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());
        var leitura = await operador.GetAsync($"/pedidos/{pedidoId}/agrupamentos");

        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task Codigo_repetido_no_mesmo_pedido_responde_409()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    }

    [Fact]
    public async Task Mesmo_codigo_em_outro_pedido_e_aceito()
    {
        var cliente = ClienteComo("PCP");
        var primeiro = await NovoPedido(cliente);
        var segundo = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{primeiro}/agrupamentos", Kit());

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{segundo}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Tipo_invalido_responde_400()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync(
            $"/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", quantidade = 10, tipo = "Conjunto" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Criar_agrupamento_em_pedido_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP")
            .PostAsJsonAsync("/pedidos/999999/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Excluir_agrupamento_vazio_responde_204_e_repetir_responde_404()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var criado = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var primeira = await cliente.DeleteAsync($"/agrupamentos/{id}");
        var segunda = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, segunda.StatusCode);
    }

    [Fact]
    public async Task Excluir_agrupamento_com_estrutura_responde_409_AgrupamentoNaoVazio()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var criado = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        // EstruturaItem e da Fase 2 e nao tem entidade: a linha entra por SQL, que e exatamente o
        // que TemEstruturaAsync consulta. ComponenteId nulo = item ad-hoc, permitido pelo DDL.
        using (var escopo = _factory.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.EstruturaItem (AgrupamentoId, NivelHierarquico, Quantidade) VALUES ({id}, 'Peca', 1)");
        }

        var resposta = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("AgrupamentoNaoVazio", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Excluir_agrupamento_de_pedido_nao_aberto_responde_409_PedidoNaoAberto()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var criado = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        // Nenhum endpoint da Fase 1 transiciona status (isso e Fase 3): o teste muda a linha
        // direto, que e o unico jeito de exercitar a guarda hoje.
        using (var escopo = _factory.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
            pedido.Status = "EmProducao";
            await db.SaveChangesAsync();
        }

        var resposta = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PedidoNaoAberto", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Listar_agrupamentos_do_pedido_devolve_os_criados()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit("AG-01"));
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit("AG-02"));

        var lista = await cliente.GetStringAsync($"/pedidos/{pedidoId}/agrupamentos");

        Assert.Contains("AG-01", lista);
        Assert.Contains("AG-02", lista);
    }
}
```

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~AgrupamentosEndpointsTests`
Expected: FALHA — 404 nas rotas de agrupamento (o controller não existe).

- [ ] **Step 11: Criar o controller e registrar as dependências**

Create `src/Rastreamento.Api/Controllers/AgrupamentosController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

/// <remarks>
/// Sem `[Route]` de classe: as rotas de criacao e listagem sao aninhadas sob Pedido
/// (`/pedidos/{pedidoId}/agrupamentos`) e as de item sao de topo (`/agrupamentos/{id}`), entao
/// cada acao declara a sua.
/// </remarks>
[ApiController]
[Authorize]
public class AgrupamentosController : CadastroControllerBase
{
    private readonly CadastroDeAgrupamentoUseCase _cadastro;

    public AgrupamentosController(CadastroDeAgrupamentoUseCase cadastro) => _cadastro = cadastro;

    [HttpGet("pedidos/{pedidoId:int}/agrupamentos")]
    public async Task<IActionResult> ListarDoPedido(int pedidoId, CancellationToken ct) =>
        Ok(await _cadastro.ListarPorPedido(pedidoId, ct));

    [HttpGet("agrupamentos/{id:int}")]
    public async Task<IActionResult> Obter(int id, CancellationToken ct)
    {
        var resultado = await _cadastro.Obter(id, ct);
        return resultado.Sucesso ? Ok(resultado.Valor) : NotFound();
    }

    [HttpPost("pedidos/{pedidoId:int}/agrupamentos")]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Cadastrar(
        int pedidoId, [FromBody] NovoAgrupamentoDto novo, CancellationToken ct)
    {
        var usuarioId = UsuarioDaSessao();
        if (usuarioId is null) return Unauthorized();

        var resultado = await _cadastro.Cadastrar(pedidoId, novo, usuarioId.Value, ct);
        if (resultado.Sucesso)
            return CreatedAtAction(nameof(Obter), new { id = resultado.Valor!.Id }, resultado.Valor);

        return await TraduzirFalha(
            resultado.TipoDoErro, resultado.Erro, Duplicado(pedidoId, novo.Codigo), ct);
    }

    [HttpPut("agrupamentos/{id:int}")]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoAgrupamentoDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(
                resultado.TipoDoErro, resultado.Erro, DuplicadoNoPedidoDe(id, alterado.Codigo), ct);
    }

    /// <summary>
    /// Unica exclusao fisica do sistema, e guardada pelo use case: 409 com codigo
    /// (`AgrupamentoNaoVazio` / `PedidoNaoAberto`) quando a guarda barra. `TraduzirResultado`
    /// repassa o codigo como veio — quem traduz para texto e a tela.
    /// </summary>
    [HttpDelete("agrupamentos/{id:int}")]
    [Authorize(Roles = "PCP,Administrador")]
    public async Task<IActionResult> Excluir(int id, CancellationToken ct) =>
        TraduzirResultado(await _cadastro.Excluir(id, ct));

    /// <summary>
    /// Como Agrupamento pergunta pelo duplicado: por (PedidoId, Codigo) — UQ_Agrupamento_PedidoCodigo
    /// e composta. E o caso que faz a base receber um delegate em vez de um metodo de assinatura fixa.
    /// </summary>
    private LocalizadorDeDuplicado Duplicado(int pedidoId, string codigo) =>
        ct => _cadastro.LocalizarDuplicado(pedidoId, codigo, ct);

    /// <summary>
    /// Na edicao o Pedido e o do proprio Agrupamento, e nao vem da rota — daí a busca extra. Ela
    /// so acontece se houver conflito: o delegate e invocado unicamente no caminho de erro.
    /// </summary>
    private LocalizadorDeDuplicado DuplicadoNoPedidoDe(int agrupamentoId, string codigo) =>
        async ct =>
        {
            var atual = await _cadastro.Obter(agrupamentoId, ct);
            return atual.Sucesso
                ? await _cadastro.LocalizarDuplicado(atual.Valor!.PedidoId, codigo, ct)
                : null;
        };
}
```

Modify `src/Rastreamento.Api/Program.cs` — logo abaixo do registro de `CadastroDePedidoUseCase`:

```csharp
builder.Services.AddScoped<IAgrupamentoRepository, AgrupamentoRepository>();
builder.Services.AddScoped<CadastroDeAgrupamentoUseCase>();
```

- [ ] **Step 12: Rodar tudo**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; toda a suíte passando (10 novos de Api, 14 de Application, 2 de Infrastructure, mais os anteriores).

- [ ] **Step 13: Commit**

```bash
git add src/Rastreamento.Domain/Entities/Agrupamento.cs src/Rastreamento.Domain/Abstractions/IAgrupamentoRepository.cs src/Rastreamento.Infrastructure/Persistence/Configurations/AgrupamentoConfiguration.cs src/Rastreamento.Infrastructure/Persistence/AgrupamentoRepository.cs src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs src/Rastreamento.Application/Cadastros/ src/Rastreamento.Api/Controllers/AgrupamentosController.cs src/Rastreamento.Api/Program.cs tests/
git commit -m "feat(agrupamento): cadastro aninhado e exclusao guardada por vazio e status"
```

---

## Task 11: Tela de detalhe do Pedido, com os Agrupamentos

**Files:**
- Modify: `web/src/api/cadastros.ts`
- Modify: `web/src/api/cadastros.test.ts`
- Create: `web/src/pages/PedidoDetalhePage.tsx`
- Modify: `web/src/App.tsx`

**Interfaces:**
- Consumes: rotas da Task 10; `obterPedido` e `formatarDataHora` (Task 9)
- Produces:
  - `AgrupamentoDto { id: number; pedidoId: number; codigo: string; quantidade: number; tipo: string; criadoEm: string; criadoPorUsuarioId: number }`
  - `NovoAgrupamento { codigo: string; quantidade: number; tipo: 'Kit' | 'Avulso' }`
  - `listarAgrupamentos(pedidoId: number): Promise<AgrupamentoDto[]>`
  - `criarAgrupamento(pedidoId: number, a: NovoAgrupamento): Promise<AgrupamentoDto | ConflitoDeCadastro>`
  - `excluirAgrupamento(id: number): Promise<ResultadoExclusao>` com `type ResultadoExclusao = 'ok' | 'AgrupamentoNaoVazio' | 'PedidoNaoAberto' | 'NaoEncontrado'`

**Contexto:** é aqui que a rota `/pedidos/:id`, já linkada pela Task 9, passa a existir. A tela faz **duas** chamadas (cabeçalho do Pedido e lista de Agrupamentos) — ver o desvio consciente registrado na Task 8. `excluirAgrupamento` traduz o 409 em união de strings porque a tela precisa dizer *por que* não deu: "tem estrutura" e "pedido não está aberto" pedem mensagens diferentes.

- [ ] **Step 1: Escrever os testes do módulo de API (vão falhar)**

Modify `web/src/api/cadastros.test.ts` — acrescentar `listarAgrupamentos`, `criarAgrupamento` e `excluirAgrupamento` ao import e o bloco abaixo, dentro do `describe`:

```ts
  it('cria agrupamento na rota aninhada do pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          id: 1, pedidoId: 4, codigo: 'AG-01', quantidade: 10, tipo: 'Kit',
          criadoEm: '2026-07-28T09:30:00-03:00', criadoPorUsuarioId: 1,
        }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarAgrupamento(4, { codigo: 'AG-01', quantidade: 10, tipo: 'Kit' })

    expect(fetchMock.mock.calls[0][0]).toBe('/pedidos/4/agrupamentos')
  })

  it('lista os agrupamentos do pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('[]', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    expect(await listarAgrupamentos(4)).toEqual([])
    expect(fetchMock.mock.calls[0][0]).toBe('/pedidos/4/agrupamentos')
  })

  it('traduz o 409 da exclusão no código que a tela precisa mostrar', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'AgrupamentoNaoVazio' }), { status: 409 }),
    ))

    expect(await excluirAgrupamento(7)).toBe('AgrupamentoNaoVazio')
  })

  it('trata 204 e 404 da exclusão', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 404 })))

    expect(await excluirAgrupamento(7)).toBe('ok')
    expect(await excluirAgrupamento(7)).toBe('NaoEncontrado')
  })
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `cd web && npm test`
Expected: FALHA — `listarAgrupamentos`, `criarAgrupamento` e `excluirAgrupamento` não são exportados.

- [ ] **Step 3: Acrescentar as funções ao módulo de API**

Modify `web/src/api/cadastros.ts` — acrescentar ao fim:

```ts
export interface AgrupamentoDto {
  id: number
  pedidoId: number
  codigo: string
  quantidade: number
  tipo: string
  /** ISO 8601 com offset -03:00, como `PedidoDto.dataAbertura`. */
  criadoEm: string
  criadoPorUsuarioId: number
}

export interface NovoAgrupamento {
  codigo: string
  quantidade: number
  tipo: 'Kit' | 'Avulso'
}

/** Desfechos do DELETE. A tela precisa distinguir os dois 409 para explicar o que houve. */
export type ResultadoExclusao = 'ok' | 'AgrupamentoNaoVazio' | 'PedidoNaoAberto' | 'NaoEncontrado'

export async function listarAgrupamentos(pedidoId: number): Promise<AgrupamentoDto[]> {
  const resp = await apiFetch(`/pedidos/${pedidoId}/agrupamentos`)
  if (!resp.ok) throw new Error(`Falha ao listar agrupamentos (${resp.status}).`)
  return (await resp.json()) as AgrupamentoDto[]
}

export function criarAgrupamento(
  pedidoId: number,
  a: NovoAgrupamento,
): Promise<AgrupamentoDto | ConflitoDeCadastro> {
  return apiFetch(`/pedidos/${pedidoId}/agrupamentos`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(a),
  }).then(lerOuFalhar<AgrupamentoDto>)
}

export async function excluirAgrupamento(id: number): Promise<ResultadoExclusao> {
  const resp = await apiFetch(`/agrupamentos/${id}`, { method: 'DELETE' })
  if (resp.status === 204) return 'ok'
  if (resp.status === 404) return 'NaoEncontrado'
  if (resp.status === 409) {
    const corpo = (await resp.json()) as { erro?: string }
    return corpo.erro === 'PedidoNaoAberto' ? 'PedidoNaoAberto' : 'AgrupamentoNaoVazio'
  }
  throw new Error(`Falha ao excluir o agrupamento (${resp.status}).`)
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `cd web && npm test`
Expected: PASS — 4 testes novos, mais os que já existiam.

- [ ] **Step 5: Criar a tela de detalhe**

Create `web/src/pages/PedidoDetalhePage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', quantidade: 1, tipo: 'Kit' }

const MOTIVO_DA_RECUSA: Record<string, string> = {
  AgrupamentoNaoVazio: 'Este agrupamento já tem estrutura e não pode mais ser excluído.',
  PedidoNaoAberto: 'O pedido não está mais aberto: não dá para excluir agrupamentos dele.',
  NaoEncontrado: 'Este agrupamento já não existe mais.',
}

export function PedidoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const pedidoId = Number(id)

  const [pedido, setPedido] = useState<PedidoDto | null>(null)
  const [agrupamentos, setAgrupamentos] = useState<AgrupamentoDto[]>([])
  const [form, setForm] = useState<NovoAgrupamento>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    try {
      // Duas chamadas de proposito: o Pedido e o sub-recurso de Agrupamentos sao rotas separadas.
      const [p, a] = await Promise.all([obterPedido(pedidoId), listarAgrupamentos(pedidoId)])
      setPedido(p)
      setAgrupamentos(a)
    } catch {
      setErro('Não foi possível carregar o pedido.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [pedidoId])

  async function salvar(e: React.FormEvent) {
    e.preventDefault()
    setErro(null)
    try {
      const resultado = await criarAgrupamento(pedidoId, form)
      if (ehConflito(resultado)) {
        setErro('Já existe um agrupamento com este código neste pedido.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch {
      setErro('Não foi possível salvar o agrupamento.')
    }
  }

  async function excluir(agrupamentoId: number) {
    setErro(null)
    try {
      const desfecho = await excluirAgrupamento(agrupamentoId)
      if (desfecho !== 'ok') setErro(MOTIVO_DA_RECUSA[desfecho])
      await carregar()
    } catch {
      setErro('Não foi possível excluir o agrupamento.')
    }
  }

  if (carregando) return <p className="p-6 text-gray-600">Carregando…</p>

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/pedidos" className="text-sm text-gray-500">&larr; Pedidos</Link>

      {pedido && (
        <header className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold">{pedido.numero}</h1>
          <p className="text-gray-600">{pedido.cliente}</p>
          <p className="text-sm text-gray-500">
            {pedido.tipo} · {pedido.status} · aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
        </header>
      )}

      <h2 className="text-lg font-medium mt-2">Agrupamentos</h2>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código do agrupamento"
          className="border rounded px-3 py-2"
        />
        <input
          type="number"
          min="0"
          step="0.0001"
          value={form.quantidade}
          onChange={(e) => setForm({ ...form, quantidade: Number(e.target.value) })}
          placeholder="Quantidade"
          className="border rounded px-3 py-2"
        />
        <select
          value={form.tipo}
          onChange={(e) => setForm({ ...form, tipo: e.target.value as NovoAgrupamento['tipo'] })}
          className="border rounded px-3 py-2"
        >
          <option value="Kit">Kit</option>
          <option value="Avulso">Avulso</option>
        </select>
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}

      <ul className="flex flex-col gap-2">
        {agrupamentos.map((a) => (
          <li key={a.id} className="flex items-center justify-between border rounded px-3 py-2">
            <span>
              <strong>{a.codigo}</strong> — {a.quantidade} ({a.tipo})
            </span>
            <button onClick={() => excluir(a.id)} className="text-sm border rounded px-2 py-1">
              Excluir
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
```

- [ ] **Step 6: Registrar a rota**

Modify `web/src/App.tsx` — acrescentar o import e a rota (**antes** do `path="*"`):

```tsx
import { PedidoDetalhePage } from './pages/PedidoDetalhePage'
```

```tsx
      <Route
        path="/pedidos/:id"
        element={<ProtectedRoute><PedidoDetalhePage /></ProtectedRoute>}
      />
```

- [ ] **Step 7: Verificar build, lint e testes do front**

Run: `cd web && npm run build && npm run lint && npm test`
Expected: build sem erro de TypeScript, lint limpo, testes passando.

- [ ] **Step 8: Commit**

```bash
git add web/src/api/cadastros.ts web/src/api/cadastros.test.ts web/src/pages/PedidoDetalhePage.tsx web/src/App.tsx
git commit -m "feat(agrupamento): tela de detalhe do pedido com criacao e exclusao"
```

---

## Task 12: Fechamento da Fase 1A

**Files:**
- Modify: `specs/05-api-endpoints.md`
- Modify: `specs/06-roadmap-mvp.md` (marcar a Fase 1A)

**Interfaces:**
- Consumes: tudo das Tasks 1–11
- Produces: nada de código — esta task fecha a fase e prova o critério de pronto

**Contexto:** `05-api-endpoints.md` lista hoje só `GET/POST` para estes recursos; a Fase 1A acrescentou `PUT`, `PATCH /{id}/ativo` e `DELETE /agrupamentos/{id}`, além do contrato de 409. Atualizar o arquivo faz parte do plano (a spec exige) — o rascunho de endpoints é documento vivo, não histórico.

- [ ] **Step 1: Atualizar a seção "Catálogo" de `specs/05-api-endpoints.md`**

Substituir as duas primeiras linhas da seção `## Catálogo`:

```markdown
- `GET/POST /setores`
- `GET/POST /materiais`
```

por:

```markdown
- `GET /setores` — `?incluirInativos=false` por padrão *(qualquer perfil autenticado)*
- `POST /setores` *(Administrador)* — `{ nome }`
- `PUT /setores/{id}` *(Administrador)* — `{ nome }`
- `PATCH /setores/{id}/ativo` *(Administrador)* — `{ ativo }`; cobre inativar **e** reativar.
  Não existe `DELETE`: catálogo se inativa, não se exclui (ver a política de exclusão na spec da
  Fase 1)
- `GET /materiais` — `?incluirInativos=false` por padrão *(qualquer perfil autenticado)*
- `POST /materiais` *(Administrador)* — `{ codigo, descricao, unidadeMedida }`
- `PUT /materiais/{id}` *(Administrador)* — idem
- `PATCH /materiais/{id}/ativo` *(Administrador)* — `{ ativo }`
```

- [ ] **Step 2: Atualizar a seção "Pedido / Agrupamento"**

Substituir:

```markdown
- `GET/POST /pedidos`
- `GET /pedidos/{id}`
```

por:

```markdown
- `GET /pedidos` *(qualquer perfil autenticado)*
- `POST /pedidos` *(PCP, Administrador)* — `{ numero, cliente }`. `Tipo` nasce `Fabricacao`,
  `Status` nasce `Aberto` e o autor vem da claim `sub` da sessão — nenhum dos três se aceita do
  cliente
- `GET /pedidos/{id}` — só o cabeçalho; os Agrupamentos saem pelo sub-recurso abaixo
- `PUT /pedidos/{id}` *(PCP, Administrador)* — `{ numero, cliente }`. Não existe `DELETE`:
  Pedido é documento e se corrige por edição
```

E substituir:

```markdown
- `GET/POST /pedidos/{id}/agrupamentos`
- `GET /agrupamentos/{id}`
```

por:

```markdown
- `GET /pedidos/{id}/agrupamentos` *(qualquer perfil autenticado)*
- `POST /pedidos/{id}/agrupamentos` *(PCP, Administrador)* — `{ codigo, quantidade, tipo }`,
  `tipo ∈ Kit | Avulso`
- `GET /agrupamentos/{id}`
- `PUT /agrupamentos/{id}` *(PCP, Administrador)* — `{ codigo, quantidade, tipo }`
- `DELETE /agrupamentos/{id}` *(PCP, Administrador)* — 204. **A única exclusão física do
  sistema**, e é guardada: 409 `{ "erro": "AgrupamentoNaoVazio" }` se já houver `EstruturaItem`,
  409 `{ "erro": "PedidoNaoAberto" }` se o Pedido não estiver `Aberto`
```

- [ ] **Step 3: Documentar o contrato de erro dos cadastros**

Acrescentar em `specs/05-api-endpoints.md`, logo após a seção `## Pedido / Agrupamento`:

```markdown
## Contrato de erro dos cadastros (Fase 1A)

- **400** — validação de formato (`MaxLength` do DTO) ou de regra simples (campo em branco,
  quantidade não positiva, `tipo` fora do domínio). Formato do ASP.NET.
- **403** — perfil sem permissão, do `[Authorize(Roles)]`.
- **404** — id inexistente.
- **409 duplicidade** — viola `UQ_Setor_Nome`, `UQ_Material_Codigo`, `UQ_Pedido_Numero` ou
  `UQ_Agrupamento_PedidoCodigo`:
  ```json
  { "erro": "ValorDuplicado", "campo": "nome", "existeInativo": true, "idExistente": 12 }
  ```
  `existeInativo: true` só acontece em catálogo (`Setor`, `Material`) e é o que permite a tela
  oferecer "reativar o existente" — os índices `UNIQUE` não são filtrados por `Ativo`, então um
  nome ocupado por linha inativa continua ocupado. Em `Pedido` e `Agrupamento` é sempre `false`.
- **409 regra de negócio** — só no `DELETE /agrupamentos/{id}`:
  `{ "erro": "AgrupamentoNaoVazio" }` ou `{ "erro": "PedidoNaoAberto" }`.

A duplicidade é verificada **no use case, antes do insert**; o índice `UNIQUE` permanece como rede
de segurança para a corrida entre a verificação e a escrita.
```

- [ ] **Step 4: Marcar a Fase 1A no roadmap**

Modify `specs/06-roadmap-mvp.md` — na descrição da Fase 1, acrescentar a nota:

```markdown
> **1A concluída** (`Setor`, `Material`, `Pedido`, `Agrupamento` — CRUD pela tela, com
> autorização por perfil no backend). Falta **1B**: `Componente` + receita padrão
> (`ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao`), que recebe
> plano próprio. Dívidas rastreadas de 1A: camada global de erro de API no front e gating de
> navegação por perfil.
```

- [ ] **Step 5: Rodar a suíte inteira, backend e front**

Run:

```bash
docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
cd web && npm run build && npm run lint && npm test
```

Expected: build em 0 warnings; toda a suíte .NET verde — os 123 da Fase 0 mais os acrescentados aqui: **45 em Application** (9 Setor + 11 Material + 11 Pedido + 14 Agrupamento), **36 em Api** (10 + 7 + 9 + 10) e **8 em Infrastructure** (2 por entidade). Front compilando, lint limpo e vitest verde.

- [ ] **Step 6: Validar o critério de pronto pela tela**

Subir a API e o front (`dotnet run --project src/Rastreamento.Api` e `cd web && npm run dev`) e
percorrer, no navegador, o critério declarado em `06-roadmap-mvp.md:23`:

1. Logar como `admin` / `Admin@123`.
2. Em **Setores**, cadastrar um Setor. Inativá-lo, tentar cadastrar o mesmo nome de novo e conferir
   que a tela oferece **"Reativar o existente"** — e que reativar funciona.
3. Em **Materiais**, cadastrar um Material com código, descrição e unidade.
4. Em **Pedidos**, abrir um Pedido. Conferir que ele nasce `Aberto` e com a data de abertura em
   horário de Brasília.
5. Abrir o Pedido e criar **dois** Agrupamentos vazios nele. Conferir que os dois aparecem na lista.
6. Excluir um deles: some da lista (204). Excluir de novo pela mesma tela não é possível — a linha
   já não existe.
7. Conferir no banco que a autoria foi gravada:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "SELECT TOP 5 p.Numero, p.Status, p.CriadoPorUsuarioId, u.NomeUsuario FROM dbo.Pedido p JOIN dbo.Usuario u ON u.Id = p.CriadoPorUsuarioId ORDER BY p.Id DESC;"
```

Expected: o Pedido recém-criado, com `Status = Aberto` e `NomeUsuario = admin`.

> **Comportamento esperado, não defeito:** logado como `Operador`, os links de Setores, Materiais
> e Pedidos continuam visíveis e o botão de salvar devolve erro genérico (o 403 do backend, sem
> tratamento amigável). É a dívida de front aceita conscientemente — a fronteira de autorização
> real está no `[Authorize(Roles)]`, que é o que acabou de responder 403.

- [ ] **Step 7: Commit**

```bash
git add specs/05-api-endpoints.md specs/06-roadmap-mvp.md
git commit -m "docs(specs): verbos e contrato de erro da Fase 1A"
```

---

## Estado do plano

**Completo — Tasks 1 a 12 escritas**, com self-review feito contra a spec
`docs/superpowers/specs/2026-07-28-fase-1-cadastros-basicos-design.md`.

Decisões tomadas ao escrever as tasks que **não** estavam na spec, todas registradas no corpo da
task correspondente:

| Onde | Decisão | Motivo |
|---|---|---|
| Tasks 3–10 | `[property: MaxLength]` nos DTOs de entrada | Sem isso, texto maior que a coluna vira `SqlException` → 500 em vez de 400. Um teste no molde (Task 4) prova o comportamento |
| Task 8 | `GET /pedidos` sem contagem e `GET /pedidos/{id}` sem os Agrupamentos embutidos | Contar/embutir exigiria mapear `Agrupamento` antes da hora ou escrever SQL cru que a Task 10 substituiria. Os Agrupamentos saem por sub-recurso, e a tela de detalhe faz as duas chamadas |
| Task 10 | `TemEstruturaAsync` por SQL direto, sem mapear `EstruturaItem` | Resolve o ponto que a spec deixou explicitamente em aberto. Mantém 1A fechada no seu escopo; quando a Fase 2 mapear a entidade, o método vira LINQ e o contrato não muda |
| Task 10 | Os dois 409 de regra viajam como **código** no `Result.Erro` | É o que o contrato de 409 da spec define no corpo. O controller repassa e não deriva comportamento da string — o que `Result.cs` proíbe é *comparar* |
| Task 9 | `formatarDataHora` lê o ISO direto, sem `Date` | A API já entrega GMT-3; `toLocaleString` reconverteria pelo fuso do aparelho e deslocaria o horário num tablet mal configurado |

**`CadastroControllerBase` (decisão do usuário, revisada):** a primeira versão do plano repetia
`TraduzirFalha` / `MontarConflito` nos quatro controllers e aceitava a duplicação, com o argumento
de que uma base exigiria um `LocalizarDuplicado` de assinatura uniforme — que `Agrupamento` não
tem, porque a unicidade dele é composta. O argumento estava errado: o que a base precisa não é da
*assinatura* da busca, e sim de **poder disparar a busca**. Um `delegate LocalizadorDeDuplicado`
resolve isso — cada controller fecha sobre os valores que já tem (`nome`, `codigo`, ou
`pedidoId + codigo`) e entrega uma função de um argumento só. A base nasce na Task 4, junto com o
molde, com todos os métodos `virtual`, e os quatro controllers herdam dela. Ganho colateral: a
tradução de `PATCH /{id}/ativo` e a do `DELETE /agrupamentos/{id}` viraram o mesmo
`TraduzirResultado`, e a leitura da claim `sub` deixou de existir em duas cópias.

**O que o self-review pegou e foi corrigido inline:** faltavam os testes de mapeamento EF de
`Pedido` e `Agrupamento` — justamente as entidades onde as colunas novas da Task 1 moram. Sem
eles, `CriadoPorUsuarioId`, `CriadoEm` e o `DECIMAL(18,4)` de `Quantidade` nunca seriam
confrontados com o DDL de verdade. Entraram como Step 9 das Tasks 8 e 10.

**1B (`Componente` + receita) recebe plano próprio depois desta fase fechar** — é o que a spec
determina, e a receita é a parte com regra densa (detecção de ciclo, substituição transacional do
roteiro).
