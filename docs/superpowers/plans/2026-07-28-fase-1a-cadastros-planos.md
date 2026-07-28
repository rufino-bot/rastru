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
| `src/Rastreamento.Api/Controllers/{Setores,Materiais,Pedidos,Agrupamentos}Controller.cs` | Rotas, autorização, tradução `Result` → status HTTP |
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
namespace Rastreamento.Application.Cadastros;

public sealed record SetorDto(int Id, string Nome, bool Ativo);

public sealed record NovoSetorDto(string Nome);

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
- Create: `src/Rastreamento.Api/Controllers/SetoresController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`
- Test: `tests/Rastreamento.Api.Tests/TokenDeTeste.cs`

**Interfaces:**
- Consumes: `CadastroDeSetorUseCase` (Task 3)
- Produces:
  - `TokenDeTeste.Emitir(WebApplicationFactory<Program>, string perfil)` → `string` — access token assinado com a chave da configuração de teste, usado por todos os testes de autorização das Tasks 4, 7, 9 e 11
  - Rotas `GET/POST /setores`, `PUT /setores/{id}`, `PATCH /setores/{id}/ativo`

**Contexto:** o helper de token existe porque os testes precisam de um usuário de cada perfil sem criar 6 linhas em `Usuario` por teste. Ele assina um JWT com as mesmas `JwtOptions` que a API valida, com a claim `role` do perfil desejado.

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
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~SetoresEndpointsTests`
Expected: FALHA — 404 em todas as rotas (o controller não existe).

- [ ] **Step 4: Criar o controller**

Create `src/Rastreamento.Api/Controllers/SetoresController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("setores")]
[Authorize]
public class SetoresController : ControllerBase
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

        return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, novo.Nome, ct);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Editar(
        int id, [FromBody] NovoSetorDto alterado, CancellationToken ct)
    {
        var resultado = await _cadastro.Editar(id, alterado, ct);
        return resultado.Sucesso
            ? Ok(resultado.Valor)
            : await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, alterado.Nome, ct);
    }

    [HttpPatch("{id:int}/ativo")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> DefinirAtivo(
        int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct)
    {
        var resultado = await _cadastro.DefinirAtivo(id, corpo.Ativo, ct);
        if (resultado.Sucesso) return NoContent();

        return resultado.TipoDoErro == TipoDeErro.NaoEncontrado
            ? NotFound()
            : BadRequest(new { erro = resultado.Erro });
    }

    /// <summary>
    /// Conflito vira 409 COM o detalhe de duplicidade — e o que permite a tela oferecer "reativar
    /// o existente" em vez de so dizer "nome em uso" (UQ_Setor_Nome nao e filtrado por Ativo).
    /// </summary>
    private async Task<IActionResult> TraduzirFalha(
        TipoDeErro? tipo, string? erro, string nome, CancellationToken ct) => tipo switch
    {
        TipoDeErro.NaoEncontrado => NotFound(),
        TipoDeErro.Conflito => Conflict(await MontarConflito(nome, erro, ct)),
        _ => BadRequest(new { erro }),
    };

    private async Task<object> MontarConflito(string nome, string? erro, CancellationToken ct)
    {
        var duplicado = await _cadastro.LocalizarDuplicado(nome, ct);
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
Expected: PASS — 9 testes.

- [ ] **Step 8: Rodar a suíte inteira e o build**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; todos os testes passando (123 anteriores + os novos).

- [ ] **Step 9: Commit**

```bash
git add src/Rastreamento.Api/Controllers/SetoresController.cs src/Rastreamento.Api/Program.cs src/Rastreamento.Application/Cadastros/Dtos.cs tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs tests/Rastreamento.Api.Tests/TokenDeTeste.cs
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

---

## ⚠️ ESTE PLANO ESTÁ INCOMPLETO — Tasks 6 a 12 ainda não foram escritas

**Não execute como se estivesse pronto.** As Tasks 1–5 estão completas e executáveis: elas
entregam o schema de autoria e o vertical slice inteiro de `Setor` (backend + tela), que é o
**molde** do qual as demais entidades são cópia.

Falta escrever, na mesma granularidade e com o código completo (a regra "sem placeholders" do
`writing-plans` proíbe "igual à Task N" — o código tem que ser repetido, porque quem executa
uma task não lê as outras):

| Task | Escopo | Delta em relação ao molde de `Setor` |
|---|---|---|
| 6 | `Material` — backend completo | Soma `Codigo`, `Descricao`, `UnidadeMedida`; duplicidade é por `Codigo` (`UQ_Material_Codigo`), então `ValorDuplicadoDto.Campo` = `"codigo"` |
| 7 | `Material` — tela | Formulário de 3 campos em vez de 1 |
| 8 | `Pedido` — backend completo | **Autoria**: `Cadastrar` recebe `int usuarioId`, lido da claim `sub` **pelo controller**. `Tipo` fixo em `'Fabricacao'`, `Status` nasce `'Aberto'`. Sem `Ativo`, sem `DefinirAtivo`, sem `DELETE` |
| 9 | `Pedido` — tela | Lista + formulário (`numero`, `cliente`) |
| 10 | `Agrupamento` — backend completo | Aninhado sob Pedido (`POST /pedidos/{id}/agrupamentos`); autoria + `CriadoEm`; `DELETE /agrupamentos/{id}` **guardado**: 409 `AgrupamentoNaoVazio` se tiver `EstruturaItem`, 409 `PedidoNaoAberto` se o Pedido não estiver `Aberto`. `UQ_Agrupamento_PedidoCodigo` é composta — a duplicidade é por (PedidoId, Codigo) |
| 11 | `Agrupamento` — tela | `PedidoDetalhePage`: cabeçalho do Pedido + lista de Agrupamentos + formulário |
| 12 | Fechamento | Atualizar `specs/05-api-endpoints.md` com os verbos novos (`PUT`, `PATCH /{id}/ativo`, `DELETE /agrupamentos/{id}`); rodar `dotnet build Rastreamento.slnx -warnaserror` + `dotnet test Rastreamento.slnx` + `cd web && npm run build && npm test`; validar o critério de pronto ponta a ponta pela tela |

**Ponto de atenção para a Task 10** (o único desenho ainda não resolvido): a guarda do `DELETE`
precisa consultar `EstruturaItem`, que é tabela da **Fase 2** e ainda não tem entidade nem
repositório. Duas saídas — decidir ao escrever a task: (a) `IAgrupamentoRepository` expõe
`Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct)` implementado por SQL
direto/`ExecuteScalar` sem mapear a entidade; ou (b) mapear `EstruturaItem` minimamente já em 1A.
A opção (a) mantém 1A fechada no seu escopo e é a recomendada — a (b) puxa Fase 2 para dentro
da Fase 1.

O `writing-plans` também pede um **self-review** do plano contra a spec ao final; ele ainda não
foi feito, porque só faz sentido com as 12 tasks escritas.
