# Fase 1B — CRUD de `Componente` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar o CRUD de `Componente` (catálogo de peças) ponta a ponta — API com busca e paginação no servidor, e tela que consome as duas.

**Architecture:** Mesmo caminho de camadas de `Material` (`Domain` → `Infrastructure` → `Application` → `Api` → front), com uma adição: a listagem passa a ser paginada e filtrada no banco, devolvendo um `PaginaDto<T>` genérico novo. Nenhuma mudança de schema — `dbo.Componente` já existe na fonte de verdade.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core (Database First) sobre SQL Server, xUnit; React 19 + TypeScript (Vite), Vitest, e `@testing-library/react` — que **entra nesta fase**.

## Global Constraints

Estes valem para **todas** as tasks. Em conflito entre este plano e o adendo `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md`, **o adendo ganha**.

- **Spec de origem:** `docs/superpowers/specs/2026-08-04-fase-1b-componente-design.md`. Leia antes da sua task.
- **Adendo obrigatório:** `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md` (B1–B14, F1–F6). Leia inteiro.
- **Baseline a confirmar ANTES de tocar em nada:** backend **263** (101 Application + 27 Infrastructure + 135 Api) · front **45** (3 arquivos) · `dotnet build Rastreamento.slnx -warnaserror` em **0 warnings**. Se divergir, **pare e reporte** — contagem de brief desatualizada já mordeu três tasks neste projeto.
- **Banco:** o container é o do checkout principal, `tcc-sqlserver-1`, e já está no ar. **NUNCA rode `docker compose up -d` de dentro da worktree** — o nome do projeto Compose sai do nome do diretório e cria um stack paralelo, com banco vazio, colidindo na porta 1433. Custou um container em 2026-08-04.
- **O banco foi regenerado em 2026-08-04** a partir de `specs/02-modelo-de-dados.sql` + `db/seed.sql`. `dbo.Componente` está **vazia** e tem 7 colunas. Nenhum `ALTER` é necessário nesta fase.
- **`web/node_modules` não existe em worktree nova** (é gitignored). Antes de qualquer coisa no front: `cd web && npm ci`.
- **Nomes de domínio em português**, espelhando o DDL (`Componente`, `CadastroDeComponenteUseCase`); nomes técnicos em inglês (`Repository`, `UseCase`, `Controller`, `DTO`).
- **Critério de pronto nunca é "os testes passam"** — é *"antes a mutação não matava nada, depois mata, e mata só o esperado"*. Cada task lista as mutações que ela precisa matar; meça-as.
- **Mutar `[Authorize(Roles = ...)]` em verbo de escrita MEXE NO BANCO** (B10). Confira `SELECT COUNT(*) FROM dbo.Componente` antes e depois, e deixe o banco como encontrou.
- **`git status` tem sujeira alheia e permanente:** `.claude/settings.local.json` modificado, `.github/`/`.vs/` untracked. **Não commite nenhuma delas.**
- **O repositório é PÚBLICO.** Varrer segredo antes de qualquer push é passo obrigatório.
- **Não edite fonte com `Set-Content` do PowerShell 5.1** — ele corrompe UTF-8 (acentuação) e a suíte fica verde mesmo assim. Use as ferramentas de edição de arquivo.

### Valores fixos desta fase (copie exatamente)

- Perfis de escrita: `"Administrador,PCP"`.
- Tipos válidos: `Bruto`, `Fabricado`, `Montagem`.
- `MaxLength`: `Codigo` 50, `Descricao` 200, `Tipo` 20 — **sem `[property:]`**.
- Paginação: `pagina` 1-based, default 1; `tamanho` default **20**, teto **100**.
- Rota: `componentes` (o prefixo `/api` é aplicado pelo `UsePathBase`; nos testes de endpoint a URL literal **inclui** `/api`, no front **não**).

---

### Task 1: Domínio e persistência de `Componente`

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Componente.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IComponenteRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/ComponenteRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` (acrescentar o `DbSet`)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ComponenteMappingTests.cs`

**Interfaces:**
- Consumes: `RastreamentoDbContext`, `TesteComBanco` (já existem).
- Produces: `Componente` (entidade com `Id`, `Codigo`, `Descricao`, `Tipo`, `Ativo`); `FiltroDeComponente(string? Busca, bool IncluirInativos, int Pagina, int Tamanho)`; `IComponenteRepository` com `ObterPorIdAsync`, `ObterPorCodigoAsync`, `ListarAsync` (devolve `(IReadOnlyList<Componente> Itens, int Total)`), `AdicionarAsync`, `SalvarAlteracoesAsync`; `RastreamentoDbContext.Componentes`.

- [ ] **Step 1: Confirmar a baseline**

Run: `dotnet test Rastreamento.slnx`
Expected: `Aprovado: 101`, `Aprovado: 27`, `Aprovado: 135` — 263 no total. Se divergir, pare e reporte.

- [ ] **Step 2: Criar a entidade**

`src/Rastreamento.Domain/Entities/Componente.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Componente
{
  public int Id { get; set; }
  public string Codigo { get; set; } = string.Empty;
  public string Descricao { get; set; } = string.Empty;

  /// <summary>
  /// Lista fechada no DDL (CK_Componente_Tipo): Bruto | Fabricado | Montagem. Quem valida e o
  /// caso de uso, nao o CHECK — excecao de CHECK sobe como SqlException e vira 500, e o cliente
  /// merece 400. Mesmo criterio de Agrupamento.Tipo.
  /// </summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>Catalogo nao se exclui, se inativa: EstruturaItem aponta para o Componente.</summary>
  public bool Ativo { get; set; }

  // ArquivoSolido e ArquivoFoto existem em dbo.Componente e NAO sao mapeadas aqui de proposito:
  // upload e a regra 18 (solido obrigatorio por Peca de Pedido) sao trabalho da Fase 2. Colunas
  // nullable, entao o INSERT do EF sem elas e valido.
}
```

- [ ] **Step 3: Criar o contrato do repositório**

`src/Rastreamento.Domain/Abstractions/IComponenteRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Filtro e faixa de uma pagina do catalogo. <c>Pagina</c> e 1-based. Vive junto da interface
/// porque faz parte do contrato dela — quem implementa precisa dos quatro campos.
/// </summary>
public sealed record FiltroDeComponente(
    string? Busca, bool IncluirInativos, int Pagina, int Tamanho);

public interface IComponenteRepository
{
  /// <summary>
  /// Retorna o componente RASTREADO (sem <c>AsNoTracking</c>): <c>Editar</c> e
  /// <c>DefinirAtivo</c> mutam a entidade e contam com o change tracking.
  /// </summary>
  Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct);

  /// <summary>
  /// Existe para o caso de uso detectar duplicidade ANTES do insert e devolver erro de negocio,
  /// em vez de deixar a violacao de <c>UQ_Componente_Codigo</c> estourar como excecao ate a API.
  /// </summary>
  Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

  /// <summary>
  /// Devolve a pagina pedida e o total que casa com o MESMO filtro. O total vem separado porque
  /// sem ele o front nao sabe quantas paginas existem; contado com os mesmos criterios porque um
  /// total sem filtro faria a tela oferecer paginas que nao existem.
  /// </summary>
  Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct);

  Task AdicionarAsync(Componente componente, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Mapear no EF e acrescentar o `DbSet`**

`src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteConfiguration : IEntityTypeConfiguration<Componente>
{
  public void Configure(EntityTypeBuilder<Componente> b)
  {
    b.ToTable("Componente");
    b.HasKey(c => c.Id);
    b.Property(c => c.Codigo).HasMaxLength(50).IsRequired();
    b.Property(c => c.Descricao).HasMaxLength(200).IsRequired();
    b.Property(c => c.Tipo).HasMaxLength(20).IsRequired();
    // Sem HasDefaultValue para Ativo: Database First — o default vive so no .sql.
  }
}
```

Em `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`, acrescente a linha
abaixo logo depois de `public DbSet<Material> Materiais => Set<Material>();`:

```csharp
  public DbSet<Componente> Componentes => Set<Componente>();
```

- [ ] **Step 5: Escrever os testes de mapeamento e de consulta paginada (falhando)**

`tests/Rastreamento.Infrastructure.Tests/Persistence/ComponenteMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>Requer o SQL Server no ar (docker compose up -d) com o schema aplicado.</summary>
public class ComponenteMappingTests : TesteComBanco
{
  /// <summary>
  /// Prefixo unico por teste: as consultas de listagem filtram por ele, entao as assercoes de
  /// paginacao nao dependem de a tabela estar vazia nem de outros testes rodando em paralelo.
  /// </summary>
  private static string NovoPrefixo() => $"cmp-{Guid.NewGuid():N}";

  private static Componente Peca(string codigo, string descricao = "Suporte lateral") =>
      new() { Codigo = codigo, Descricao = descricao, Tipo = "Fabricado", Ativo = true };

  private static async Task LimparAsync(string prefixo)
  {
    await using var db = NovoContexto();
    db.Componentes.RemoveRange(
        await db.Componentes.Where(c => c.Codigo.StartsWith(prefixo)).ToListAsync());
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Mapeia_componente_com_round_trip()
  {
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var componente = Peca($"{prefixo}-a", "Suporte lateral esquerdo");

    db.Componentes.Add(componente);
    await db.SaveChangesAsync();
    var id = componente.Id;

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Id == id);

      Assert.Equal($"{prefixo}-a", carregado.Codigo);
      Assert.Equal("Suporte lateral esquerdo", carregado.Descricao);
      Assert.Equal("Fabricado", carregado.Tipo);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Componente_nasce_ativo_pelo_default_do_banco()
  {
    // INSERT cru omitindo `Ativo` de proposito: e o unico jeito de provar DF_Componente_Ativo,
    // porque um INSERT feito pelo EF sempre manda a coluna (Database First: o default so vive
    // no .sql, ComponenteConfiguration nao declara HasDefaultValue).
    var prefixo = NovoPrefixo();
    await using var db = NovoContexto();
    var codigo = $"{prefixo}-def";

    await db.Database.ExecuteSqlInterpolatedAsync(
        $"INSERT INTO dbo.Componente (Codigo, Descricao, Tipo) VALUES ({codigo}, 'Teste', 'Bruto')");

    try
    {
      await using var dbLeitura = NovoContexto();
      var carregado = await dbLeitura.Componentes.AsNoTracking().SingleAsync(c => c.Codigo == codigo);
      Assert.True(carregado.Ativo);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Pagina_em_ordem_de_codigo_independente_da_ordem_de_insercao()
  {
    // A mutacao que este teste existe para matar: apagar o OrderBy(c => c.Codigo) do repositorio.
    // Sem ordem TOTAL, Skip/Take devolve linhas em ordem arbitraria — a mesma linha pode aparecer
    // em duas paginas e outra em nenhuma. Por isso os codigos sao inseridos FORA de ordem
    // alfabetica: se o repositorio devolvesse na ordem de insercao, a pagina 1 viria "-c", "-a".
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-c"), Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var pagina1 = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 2), CancellationToken.None);
      var pagina2 = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 2, 2), CancellationToken.None);

      Assert.Equal(
          new[] { $"{prefixo}-a", $"{prefixo}-b" }, pagina1.Itens.Select(c => c.Codigo).ToArray());
      Assert.Equal(new[] { $"{prefixo}-c" }, pagina2.Itens.Select(c => c.Codigo).ToArray());
      Assert.Equal(3, pagina1.Total);
      Assert.Equal(3, pagina2.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Busca_casa_no_codigo_e_na_descricao()
  {
    // Mata duas mutacoes: buscar so no Codigo (o caso "descricao" morre) e buscar so na Descricao
    // (o caso "codigo" morre).
    var prefixo = NovoPrefixo();
    var marcador = $"m{Guid.NewGuid():N}";
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(
          Peca($"{prefixo}-{marcador}", "Sem o marcador na descricao"),
          Peca($"{prefixo}-outro", $"Descricao com {marcador} dentro"),
          Peca($"{prefixo}-terceiro", "Nada a ver"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var achados = await repo.ListarAsync(
          new FiltroDeComponente(marcador, false, 1, 20), CancellationToken.None);

      Assert.Equal(2, achados.Total);
      Assert.Equal(2, achados.Itens.Count);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Total_respeita_o_filtro_de_inativos()
  {
    // Mata a mutacao "contar o total sem os filtros": com 1 de 3 ativo, o total padrao e 1, nao 3.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(
          Peca($"{prefixo}-a"),
          new Componente { Codigo = $"{prefixo}-b", Descricao = "X", Tipo = "Bruto", Ativo = false },
          new Componente { Codigo = $"{prefixo}-c", Descricao = "Y", Tipo = "Bruto", Ativo = false });
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var soAtivos = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 20), CancellationToken.None);
      var comInativos = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, true, 1, 20), CancellationToken.None);

      Assert.Equal(1, soAtivos.Total);
      Assert.Equal(3, comInativos.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Pagina_alem_do_fim_devolve_lista_vazia_e_o_total_verdadeiro()
  {
    // Fim de lista nao e erro: e 200 com itens vazios. Tambem mata o off-by-one de trocar
    // Skip((pagina - 1) * tamanho) por Skip(pagina * tamanho), que faria a pagina 1 pular a
    // primeira linha.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var primeira = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 1, 20), CancellationToken.None);
      var longe = await repo.ListarAsync(
          new FiltroDeComponente(prefixo, false, 99, 20), CancellationToken.None);

      Assert.Equal(
          new[] { $"{prefixo}-a", $"{prefixo}-b" }, primeira.Itens.Select(c => c.Codigo).ToArray());
      Assert.Empty(longe.Itens);
      Assert.Equal(2, longe.Total);
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }

  [Fact]
  public async Task Busca_em_branco_nao_filtra_nada()
  {
    // Prova que `string.IsNullOrWhiteSpace` cobre tambem a string so de espacos: se o repositorio
    // testasse apenas `!= null`, um `busca: "   "` viraria `Contains("   ")` e devolveria zero.
    var prefixo = NovoPrefixo();
    await using (var db = NovoContexto())
    {
      db.Componentes.AddRange(Peca($"{prefixo}-a"), Peca($"{prefixo}-b"));
      await db.SaveChangesAsync();
    }

    try
    {
      await using var db = NovoContexto();
      var repo = new ComponenteRepository(db);

      var todos = await repo.ListarAsync(
          new FiltroDeComponente("   ", false, 1, 500), CancellationToken.None);

      Assert.True(todos.Total >= 2);
      Assert.Contains(todos.Itens, c => c.Codigo == $"{prefixo}-a");
    }
    finally
    {
      await LimparAsync(prefixo);
    }
  }
}
```

- [ ] **Step 6: Rodar e confirmar que falha por falta do repositório**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests`
Expected: erro de compilação — `ComponenteRepository` não existe.

- [ ] **Step 7: Implementar o repositório**

`src/Rastreamento.Infrastructure/Persistence/ComponenteRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ComponenteRepository : IComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ComponenteRepository(RastreamentoDbContext db) => _db = db;

  // Sem AsNoTracking de proposito nos dois Obter*: ver o contrato da interface (Editar e
  // DefinirAtivo mutam a entidade devolvida e dependem do change tracking).
  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) =>
      _db.Componentes.SingleOrDefaultAsync(c => c.Id == id, ct);

  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
      _db.Componentes.SingleOrDefaultAsync(c => c.Codigo == codigo, ct);

  public async Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct)
  {
    var consulta = _db.Componentes.AsNoTracking()
        .Where(c => filtro.IncluirInativos || c.Ativo);

    if (!string.IsNullOrWhiteSpace(filtro.Busca))
    {
      // Sem ToLower(): a comparacao ja e case-insensitive pela collation da coluna, e ToLower()
      // na consulta impediria o uso de indice. Trim pelo mesmo motivo que o resto do projeto:
      // " CH " e " CH" tem que buscar a mesma coisa.
      var busca = filtro.Busca.Trim();
      consulta = consulta.Where(c => c.Codigo.Contains(busca) || c.Descricao.Contains(busca));
    }

    // Contado ANTES do Skip/Take e com o MESMO filtro: e o numero de paginas que o front usa.
    var total = await consulta.CountAsync(ct);

    // OrderBy obrigatorio e por Codigo de proposito: UQ_Componente_Codigo garante ordem TOTAL.
    // Sem ordem total, Skip/Take repete e pula linhas entre paginas.
    var itens = await consulta
        .OrderBy(c => c.Codigo)
        .Skip((filtro.Pagina - 1) * filtro.Tamanho)
        .Take(filtro.Tamanho)
        .ToListAsync(ct);

    return (itens, total);
  }

  public async Task AdicionarAsync(Componente componente, CancellationToken ct) =>
      await _db.Componentes.AddAsync(componente, ct);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 8: Rodar os testes**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests`
Expected: `Aprovado: 34` (27 da baseline + 7 novos), 0 falhas.

- [ ] **Step 9: Medir as mutações**

Aplique uma de cada vez, rode `dotnet test tests/Rastreamento.Infrastructure.Tests`, **reverta**, e anote quantos testes morreram:

1. Apagar `.OrderBy(c => c.Codigo)` → esperado: `Pagina_em_ordem_de_codigo_independente_da_ordem_de_insercao` morre.
2. Trocar `Skip((filtro.Pagina - 1) * filtro.Tamanho)` por `Skip(filtro.Pagina * filtro.Tamanho)` → esperado: pelo menos `Pagina_em_ordem...` e `Pagina_alem_do_fim...` morrem.
3. Mover o `CountAsync` para antes do `if` da busca (total sem filtro) → esperado: `Busca_casa_no_codigo_e_na_descricao` e `Total_respeita_o_filtro_de_inativos` morrem.
4. Trocar `c.Codigo.Contains(busca) || c.Descricao.Contains(busca)` por só `c.Codigo.Contains(busca)` → esperado: `Busca_casa_no_codigo_e_na_descricao` morre.
5. Trocar `!string.IsNullOrWhiteSpace` por `filtro.Busca != null` → esperado: `Busca_em_branco_nao_filtra_nada` morre.

Se alguma mutação **não** matar nada, o teste correspondente não discrimina — conserte o teste antes de seguir.

- [ ] **Step 10: Build sem warnings e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
git add src/Rastreamento.Domain/Entities/Componente.cs \
        src/Rastreamento.Domain/Abstractions/IComponenteRepository.cs \
        src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteConfiguration.cs \
        src/Rastreamento.Infrastructure/Persistence/ComponenteRepository.cs \
        src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs \
        tests/Rastreamento.Infrastructure.Tests/Persistence/ComponenteMappingTests.cs
git commit -m "feat(dominio): Componente com listagem paginada e busca no banco"
```

---

### Task 2: `PaginaDto<T>`, DTOs e `CadastroDeComponenteUseCase`

**Files:**
- Create: `src/Rastreamento.Application/Common/PaginaDto.cs`
- Create: `src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (acrescentar ao fim)
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (acrescentar ao fim)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeComponenteUseCaseTests.cs`

**Interfaces:**
- Consumes: `IComponenteRepository`, `FiltroDeComponente`, `Componente` (Task 1); `Result<T>`, `TipoDeErro`, `ValorDuplicadoDto` (já existem).
- Produces: `PaginaDto<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho)`; `ComponenteDto(int Id, string Codigo, string Descricao, string Tipo, bool Ativo)`; `NovoComponenteDto(string Codigo, string Descricao, string Tipo)`; `CadastroDeComponenteUseCase` com `Cadastrar`, `Editar`, `Listar`, `DefinirAtivo`, `LocalizarDuplicado`, e as constantes `TamanhoDePaginaPadrao = 20` / `TamanhoDePaginaMaximo = 100`.

- [ ] **Step 1: Criar o `PaginaDto<T>` genérico**

`src/Rastreamento.Application/Common/PaginaDto.cs`:

```csharp
namespace Rastreamento.Application.Common;

/// <summary>
/// Uma pagina de resultado. Generico desde o comeco, mesmo com um consumidor so (`Componente`):
/// o que nao pode acontecer e a paginacao nascer com formato ad-hoc e o sistema acumular tres
/// jeitos incompativeis de paginar ate a Fase 6. `Setor` e `Material` NAO migram nesta fase —
/// eles nao tem o problema de volume que motivou isto; migrar depois e preencher, nao redesenhar.
/// <para>
/// `Total` e a contagem sob o MESMO filtro da pagina, e nao o tamanho de `Itens`: e dele que sai
/// o numero de paginas na tela.
/// </para>
/// </summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);
```

- [ ] **Step 2: Acrescentar os DTOs**

No fim de `src/Rastreamento.Application/Cadastros/Dtos.cs`:

```csharp
// ---------------------------------------------------------------------------
// Componente
// ---------------------------------------------------------------------------

/// <remarks>
/// Sem `ArquivoSolido`/`ArquivoFoto`: as colunas existem em `dbo.Componente`, mas upload e a
/// regra 18 sao trabalho da Fase 2, e a entidade da 1B nao as mapeia.
/// </remarks>
public sealed record ComponenteDto(
    int Id, string Codigo, string Descricao, string Tipo, bool Ativo);

/// <remarks>
/// Os `MaxLength` espelham `dbo.Componente`: NVARCHAR(50), (200) e (20). Mesma regra de alvo do
/// `NovoSetorDto` — atributo SEM `[property:]`, no parametro do construtor primario, que e onde a
/// validacao de modelo do MVC le em record posicional. `Tipo` NAO ganha `[RegularExpression]`: a
/// lista fechada (Bruto | Fabricado | Montagem) e regra do use case, porque o CK_Componente_Tipo
/// do banco subiria como 500 em vez de 400. Campo so de espacos continua sendo regra do use case.
/// </remarks>
public sealed record NovoComponenteDto(
    [MaxLength(50)] string Codigo,
    [MaxLength(200)] string Descricao,
    [MaxLength(20)] string Tipo);
```

- [ ] **Step 3: Acrescentar o fake do repositório**

No fim de `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs`:

```csharp
public class FakeComponenteRepo : IComponenteRepository
{
  private readonly List<Componente> _linhas;
  private int _proximoId;

  public FakeComponenteRepo(params Componente[] existentes)
  {
    _linhas = existentes.ToList();
    _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(c => c.Id) + 1;
  }

  /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
  public int Saves { get; private set; }

  /// <summary>O ultimo filtro que o caso de uso mandou — prova o que ele TRADUZIU, nao o retorno.</summary>
  public FiltroDeComponente? UltimoFiltro { get; private set; }

  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(c => c.Id == id));

  /// <summary>
  /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
  /// producao (`ComponenteRepository` real, `WHERE Codigo = @p`) e de `UQ_Componente_Codigo`.
  /// Duplicado-por-caixa (ex.: "sup-01" vs "SUP-01") nao e coberto neste nivel — precisa de um
  /// teste ponta a ponta contra o banco real. NAO torne o fake case-insensitive: isso simularia
  /// o banco e esconderia a lacuna.
  /// </summary>
  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
      Task.FromResult(_linhas.SingleOrDefault(c => c.Codigo == codigo));

  /// <summary>
  /// Espelha o repositorio real o suficiente para o caso de uso ser testavel, mas a fidelidade
  /// para aqui: a prova de que a paginacao acontece no SQL vive em `ComponenteMappingTests`.
  /// </summary>
  public Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct)
  {
    UltimoFiltro = filtro;

    var consulta = _linhas.Where(c => filtro.IncluirInativos || c.Ativo);
    if (!string.IsNullOrWhiteSpace(filtro.Busca))
    {
      var busca = filtro.Busca.Trim();
      consulta = consulta.Where(c => c.Codigo.Contains(busca) || c.Descricao.Contains(busca));
    }

    var filtradas = consulta.OrderBy(c => c.Codigo).ToList();
    var pagina = filtradas
        .Skip((filtro.Pagina - 1) * filtro.Tamanho)
        .Take(filtro.Tamanho)
        .ToList();

    return Task.FromResult<(IReadOnlyList<Componente>, int)>((pagina, filtradas.Count));
  }

  public Task AdicionarAsync(Componente componente, CancellationToken ct)
  {
    componente.Id = _proximoId++;
    _linhas.Add(componente);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    return Task.CompletedTask;
  }
}
```

- [ ] **Step 4: Escrever os testes do caso de uso (falhando)**

`tests/Rastreamento.Application.Tests/Cadastros/CadastroDeComponenteUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeComponenteUseCaseTests
{
  private static NovoComponenteDto Suporte(string codigo = "SUP-001", string tipo = "Fabricado") =>
      new(codigo, "Suporte lateral", tipo);

  private static Componente Linha(int id, string codigo, bool ativo = true) =>
      new() { Id = id, Codigo = codigo, Descricao = "Suporte", Tipo = "Fabricado", Ativo = ativo };

  [Fact]
  public async Task Cadastra_componente_novo_ativo()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("SUP-001", resultado.Valor!.Codigo);
    Assert.Equal("Suporte lateral", resultado.Valor.Descricao);
    Assert.Equal("Fabricado", resultado.Valor.Tipo);
    Assert.True(resultado.Valor.Ativo);
    Assert.Equal(1, repo.Saves);
  }

  [Theory]
  [InlineData("Bruto")]
  [InlineData("Fabricado")]
  [InlineData("Montagem")]
  public async Task Aceita_os_tres_tipos_do_check(string tipo)
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(tipo: tipo), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(tipo, resultado.Valor!.Tipo);
  }

  [Theory]
  [InlineData("Qualquer")]
  [InlineData("bruto")]
  [InlineData("")]
  public async Task Tipo_fora_da_lista_e_erro_de_validacao(string tipo)
  {
    // "bruto" minusculo entra de proposito: a lista e comparada com == (ordinal), entao caixa
    // errada e recusa. Se um dia isso virar comparacao case-insensitive, este caso morre e
    // obriga a decisao a ser explicita em vez de silenciosa.
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(tipo: tipo), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Theory]
  [InlineData("", "Suporte", "Fabricado")]
  [InlineData("SUP-001", "  ", "Fabricado")]
  public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(
      string codigo, string descricao, string tipo)
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(
        new NovoComponenteDto(codigo, descricao, tipo), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Codigo_duplicado_e_conflito_e_nao_escreve_nada()
  {
    var repo = new FakeComponenteRepo(Linha(3, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_componente_inexistente_e_nao_encontrado()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(99, Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_mantendo_o_proprio_codigo_nao_e_conflito_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(
        1, new NovoComponenteDto("SUP-001", "Suporte reforcado", "Montagem"),
        CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Suporte reforcado", resultado.Valor!.Descricao);
    Assert.Equal("Montagem", resultado.Valor.Tipo);
    // Unico teste que prova a escrita do Editar (adendo B2): sem isto, um Editar que muta a
    // entidade e esquece o SalvarAlteracoesAsync passa em todos os outros.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_codigo_de_outro_componente_e_conflito()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"), Linha(2, "SUP-002"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(2, Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_com_tipo_invalido_e_erro_de_validacao()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(1, Suporte(tipo: "Errado"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Definir_ativo_false_inativa_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    var soAtivos = await useCase.Listar(null, false, 1, 20, CancellationToken.None);
    var comInativos = await useCase.Listar(null, true, 1, 20, CancellationToken.None);
    Assert.Equal(0, soAtivos.Valor!.Total);
    Assert.Equal(1, comInativos.Valor!.Total);
  }

  [Fact]
  public async Task Definir_ativo_true_reativa_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001", ativo: false));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, true, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    var soAtivos = await useCase.Listar(null, false, 1, 20, CancellationToken.None);
    Assert.Equal(1, soAtivos.Valor!.Total);
  }

  [Fact]
  public async Task Definir_ativo_em_componente_inexistente_e_nao_encontrado()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(99, true, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Theory]
  [InlineData(0, 20)]
  [InlineData(-1, 20)]
  [InlineData(1, 0)]
  [InlineData(1, -5)]
  [InlineData(1, 101)]
  public async Task Faixa_de_paginacao_invalida_e_erro_de_validacao(int pagina, int tamanho)
  {
    // 101 entra porque o teto e 100: sem ele, `?tamanho=100000` devolveria o catalogo inteiro.
    // O banco NAO tem rede de seguranca nenhuma para isto (nao ha CHECK de faixa), entao pelo
    // adendo B14 a mesma propriedade tambem ganha teste no nivel HTTP na Task 3.
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Listar(null, false, pagina, tamanho, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Faixa_de_paginacao_no_limite_e_aceita()
  {
    // Controle de escopo do teste acima: 100 exato PASSA. Sem este par, trocar `> 100` por
    // `>= 100` ficaria verde.
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    var resultado = await useCase.Listar(null, false, 1, 100, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(100, resultado.Valor!.Tamanho);
  }

  [Fact]
  public async Task Listar_repassa_o_filtro_ao_repositorio_e_ecoa_a_faixa()
  {
    // Prova o que o caso de uso TRADUZ, nao o que o fake devolve: sem isto, ignorar `busca` ou
    // trocar `pagina` por 1 fixo passaria despercebido.
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"), Linha(2, "SUP-002"), Linha(3, "SUP-003"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Listar("SUP", true, 2, 2, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("SUP", repo.UltimoFiltro!.Busca);
    Assert.True(repo.UltimoFiltro.IncluirInativos);
    Assert.Equal(2, repo.UltimoFiltro.Pagina);
    Assert.Equal(2, repo.UltimoFiltro.Tamanho);
    Assert.Equal(2, resultado.Valor!.Pagina);
    Assert.Equal(2, resultado.Valor.Tamanho);
    Assert.Equal(3, resultado.Valor.Total);
    Assert.Single(resultado.Valor.Itens);
    Assert.Equal("SUP-003", resultado.Valor.Itens[0].Codigo);
  }

  [Fact]
  public async Task Localiza_duplicado_inativo_apontando_o_campo_codigo()
  {
    var repo = new FakeComponenteRepo(Linha(9, "SUP-001", ativo: false));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var duplicado = await useCase.LocalizarDuplicado("SUP-001", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("codigo", duplicado!.Campo);
    Assert.True(duplicado.ExisteInativo);
    Assert.Equal(9, duplicado.IdExistente);
  }

  [Fact]
  public async Task Localiza_duplicado_devolve_nulo_quando_codigo_e_livre()
  {
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    Assert.Null(await useCase.LocalizarDuplicado("SUP-001", CancellationToken.None));
  }

  [Fact]
  public async Task Localiza_duplicado_com_codigo_nulo_nao_lanca()
  {
    // Adendo B9: o `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON
    // entrega null mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina
    // de codigo — trocar `Normalizar(codigo)` por `codigo.Trim()` pelado nao quebraria nada.
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    Assert.Null(await useCase.LocalizarDuplicado(null!, CancellationToken.None));
  }
}
```

- [ ] **Step 5: Rodar e confirmar que falha**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: erro de compilação — `CadastroDeComponenteUseCase` não existe.

- [ ] **Step 6: Implementar o caso de uso**

`src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Componente (catalogo): criar, editar, listar e (in)ativar. Componente nao se
/// exclui — linhas de EstruturaItem, ComponenteFilhoPadrao, ComponenteMaterialPadrao e
/// ComponenteRoteiroPadrao apontam para ele (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDeComponenteUseCase
{
  /// <summary>Quantas linhas a listagem devolve quando o cliente nao pede tamanho.</summary>
  public const int TamanhoDePaginaPadrao = 20;

  /// <summary>
  /// Teto de linhas por pagina. Existe para `?tamanho=100000` nao virar negacao de servico
  /// trivial. Nao ha CHECK equivalente no banco: esta guarda e a unica defesa (adendo B14).
  /// </summary>
  public const int TamanhoDePaginaMaximo = 100;

  private static readonly string[] TiposValidos = ["Bruto", "Fabricado", "Montagem"];

  private const string ErroDeCampoObrigatorio = "Codigo, descricao e tipo sao obrigatorios.";

  private const string ErroDeTipoInvalido = "Tipo deve ser Bruto, Fabricado ou Montagem.";

  private const string ErroDeCodigoDuplicado = "Ja existe um Componente com este codigo.";

  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";

  private const string ErroDeFaixaInvalida =
      "Pagina deve ser 1 ou maior e tamanho deve estar entre 1 e 100.";

  private readonly IComponenteRepository _repositorio;

  public CadastroDeComponenteUseCase(IComponenteRepository repositorio) =>
      _repositorio = repositorio;

  public async Task<Result<ComponenteDto>> Cadastrar(NovoComponenteDto novo, CancellationToken ct)
  {
    var (codigo, descricao, tipo) = Normalizar(novo);
    var invalido = Validar(codigo, descricao, tipo);
    if (invalido is not null) return Result<ComponenteDto>.Falha(invalido, TipoDeErro.Validacao);

    // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Componente_Codigo
    // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
    if (await _repositorio.ObterPorCodigoAsync(codigo, ct) is not null)
      return Result<ComponenteDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    var componente = new Componente
    {
      Codigo = codigo,
      Descricao = descricao,
      Tipo = tipo,
      Ativo = true,
    };
    await _repositorio.AdicionarAsync(componente, ct);
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<ComponenteDto>.Ok(Projetar(componente));
  }

  public async Task<Result<ComponenteDto>> Editar(
      int id, NovoComponenteDto alterado, CancellationToken ct)
  {
    var (codigo, descricao, tipo) = Normalizar(alterado);
    var invalido = Validar(codigo, descricao, tipo);
    if (invalido is not null) return Result<ComponenteDto>.Falha(invalido, TipoDeErro.Validacao);

    var componente = await _repositorio.ObterPorIdAsync(id, ct);
    if (componente is null)
      return Result<ComponenteDto>.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    // So e conflito se o codigo pertencer a OUTRA linha: manter o proprio codigo e no-op.
    var homonimo = await _repositorio.ObterPorCodigoAsync(codigo, ct);
    if (homonimo is not null && homonimo.Id != id)
      return Result<ComponenteDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    componente.Codigo = codigo;
    componente.Descricao = descricao;
    componente.Tipo = tipo;
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<ComponenteDto>.Ok(Projetar(componente));
  }

  /// <summary>
  /// Devolve `Result` — e nao a pagina direto — porque a faixa pedida pode ser invalida, e isso
  /// e 400, nao 200 com lista vazia. Pagina ALEM do fim, por outro lado, e sucesso com itens
  /// vazios: fim de lista nao e pedido invalido.
  /// </summary>
  public async Task<Result<PaginaDto<ComponenteDto>>> Listar(
      string? busca, bool incluirInativos, int pagina, int tamanho, CancellationToken ct)
  {
    if (pagina < 1 || tamanho < 1 || tamanho > TamanhoDePaginaMaximo)
      return Result<PaginaDto<ComponenteDto>>.Falha(ErroDeFaixaInvalida, TipoDeErro.Validacao);

    var (itens, total) = await _repositorio.ListarAsync(
        new FiltroDeComponente(busca, incluirInativos, pagina, tamanho), ct);

    return Result<PaginaDto<ComponenteDto>>.Ok(
        new PaginaDto<ComponenteDto>(itens.Select(Projetar).ToList(), total, pagina, tamanho));
  }

  /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /componentes/{id}/ativo`.</summary>
  public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
  {
    var componente = await _repositorio.ObterPorIdAsync(id, ct);
    if (componente is null)
      return Result.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    componente.Ativo = ativo;
    await _repositorio.SalvarAlteracoesAsync(ct);
    return Result.Ok();
  }

  /// <summary>
  /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo da
  /// segunda leitura nao entra no caminho feliz. O campo e `codigo` porque a unicidade e do
  /// Codigo (`UQ_Componente_Codigo`); a Descricao pode repetir a vontade.
  /// </summary>
  public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string codigo, CancellationToken ct)
  {
    var existente = await _repositorio.ObterPorCodigoAsync(Normalizar(codigo), ct);
    return existente is null
        ? null
        : new ValorDuplicadoDto("codigo", !existente.Ativo, existente.Id);
  }

  /// <summary>
  /// Devolve a mensagem do primeiro problema, ou null se estiver tudo certo. `Tipo` e validado
  /// aqui, e nao pelo CK_Componente_Tipo: excecao de CHECK subiria como 500 em vez de 400.
  /// </summary>
  private static string? Validar(string codigo, string descricao, string tipo)
  {
    if (codigo.Length == 0 || descricao.Length == 0 || tipo.Length == 0)
      return ErroDeCampoObrigatorio;
    if (!TiposValidos.Contains(tipo)) return ErroDeTipoInvalido;
    return null;
  }

  private static (string Codigo, string Descricao, string Tipo) Normalizar(NovoComponenteDto d) =>
      (Normalizar(d.Codigo), Normalizar(d.Descricao), Normalizar(d.Tipo));

  /// <summary>
  /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
  /// " SUP-001 " colidir com "SUP-001" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre
  /// o null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao
  /// de nulabilidade nao e garantia em tempo de execucao.
  /// </summary>
  private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

  private static ComponenteDto Projetar(Componente c) =>
      new(c.Id, c.Codigo, c.Descricao, c.Tipo, c.Ativo);
}
```

- [ ] **Step 7: Rodar os testes**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: `Aprovado: 128` — 101 da baseline + **27** casos novos. A conta: 14 `[Fact]` mais os
`[Theory]` (`Aceita_os_tres_tipos` 3, `Tipo_fora_da_lista` 3, `Campo_obrigatorio` 2,
`Faixa_de_paginacao_invalida` 5) = 14 + 13 = 27. Se divergir, **refaça a conta a partir dos
atributos antes de concluir que há erro** — contagem de brief desatualizada já mordeu três tasks
neste projeto.

- [ ] **Step 8: Medir as mutações**

Uma de cada vez, com reversão depois:

1. Remover `if (!TiposValidos.Contains(tipo)) return ErroDeTipoInvalido;` → esperado: os 3 casos de `Tipo_fora_da_lista...` e o `Editar_com_tipo_invalido...` morrem.
2. Trocar `tamanho > TamanhoDePaginaMaximo` por `tamanho > 1000` → esperado: o caso `(1, 101)` morre.
3. Trocar `tamanho > TamanhoDePaginaMaximo` por `tamanho >= TamanhoDePaginaMaximo` → esperado: `Faixa_de_paginacao_no_limite_e_aceita` morre.
4. Trocar `Normalizar(codigo)` por `codigo.Trim()` em `LocalizarDuplicado` → esperado: `Localiza_duplicado_com_codigo_nulo_nao_lanca` morre.
5. Remover `await _repositorio.SalvarAlteracoesAsync(ct);` do `Editar` → esperado: `Editar_mantendo_o_proprio_codigo_nao_e_conflito_e_persiste` morre.
6. Ignorar `busca` no `Listar` (passar `null` ao filtro) → esperado: `Listar_repassa_o_filtro_ao_repositorio_e_ecoa_a_faixa` morre.

- [ ] **Step 9: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
git add src/Rastreamento.Application/Common/PaginaDto.cs \
        src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs \
        src/Rastreamento.Application/Cadastros/Dtos.cs \
        tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs \
        tests/Rastreamento.Application.Tests/Cadastros/CadastroDeComponenteUseCaseTests.cs
git commit -m "feat(aplicacao): CadastroDeComponenteUseCase com PaginaDto generico"
```

---

### Task 3: `ComponentesController`, DI e testes de endpoint

**Files:**
- Create: `src/Rastreamento.Api/Controllers/ComponentesController.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (registrar o par no DI, junto dos demais cadastros)
- Modify: `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs` (dois `[InlineData]`)
- Modify: `specs/05-api-endpoints.md` (contrato de `/componentes`)
- Test: `tests/Rastreamento.Api.Tests/ComponentesEndpointsTests.cs`

**Interfaces:**
- Consumes: `CadastroDeComponenteUseCase`, `ComponenteDto`, `NovoComponenteDto` (Task 2); `CadastroControllerBase` com `TraduzirFalha`, `TraduzirResultado`, `MontarConflito`, `LocalizadorDeDuplicado`; `TokenDeTeste.Emitir(factory, perfil)`.
- Produces: os endpoints `GET/POST /componentes`, `PUT /componentes/{id}`, `PATCH /componentes/{id}/ativo`.

- [ ] **Step 1: Escrever os testes de endpoint (falhando)**

`tests/Rastreamento.Api.Tests/ComponentesEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Componente, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa o que criou — UQ_Componente_Codigo nao perdoa sobra de execucao anterior.
/// </summary>
public class ComponentesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _prefixosCriados = [];

  public ComponentesEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    foreach (var prefixo in _prefixosCriados)
      db.Componentes.RemoveRange(
          await db.Componentes.Where(c => c.Codigo.StartsWith(prefixo)).ToListAsync());
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// Prefixo unico por chamada. As consultas de listagem filtram por ele (`?busca=`), entao as
  /// assercoes de paginacao valem mesmo com o catalogo cheio de linhas de outros testes.
  /// </summary>
  private string NovoPrefixo()
  {
    var prefixo = $"cmp{Guid.NewGuid():N}";
    _prefixosCriados.Add(prefixo);
    return prefixo;
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private static object CorpoValido(string codigo) =>
      new { codigo, descricao = "Suporte lateral", tipo = "Fabricado" };

  private static async Task<int> IdDaResposta(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
          .RootElement.GetProperty("id").GetInt32();

  private async Task CriarAsync(HttpClient cliente, string codigo)
  {
    var resposta = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
  }

  [Theory]
  [InlineData("Administrador")]
  [InlineData("PCP")]
  public async Task Perfis_de_escrita_cadastram_componente(string perfil)
  {
    // Os DOIS perfis, nao so o Administrador: /componentes e a primeira entidade de catalogo com
    // dois perfis de escrita, e um teste so do Administrador deixaria remover o "PCP" da string
    // de roles sem quebrar nada.
    var resposta = await ClienteComo(perfil)
        .PostAsJsonAsync("/api/componentes", CorpoValido($"{NovoPrefixo()}-a"));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
  }

  [Theory]
  [InlineData("POST", "/api/componentes")]
  [InlineData("PUT", "/api/componentes/999999")]
  [InlineData("PATCH", "/api/componentes/999999/ativo")]
  public async Task Operador_nao_escreve_em_componente(string metodo, string rota)
  {
    // Cobrir os TRES verbos, e nao so o POST, e o que impede apagar o [Authorize(Roles)] do PUT
    // ou do PATCH em silencio (adendo B5). O filtro de autorizacao roda ANTES do model binding,
    // entao um id inexistente (999999) ainda responde 403 aqui, nunca 404 — por isso o Theory
    // nao precisa cadastrar Componente nenhum, e nada e escrito no banco.
    object corpo = metodo == "PATCH"
        ? new { ativo = false }
        : new { codigo = "operador-nao-pode", descricao = "Suporte", tipo = "Fabricado" };
    var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota)
    {
      Content = JsonContent.Create(corpo)
    };

    var resposta = await ClienteComo("Operador").SendAsync(requisicao);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Operador_le_a_lista_de_componentes()
  {
    var resposta = await ClienteComo("Operador").GetAsync("/api/componentes");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_a_lista()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/componentes");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Listagem_sem_parametros_usa_pagina_1_e_tamanho_20()
  {
    // Mata a mutacao de trocar os defaults da assinatura do controller: sem isto, `tamanho = 5`
    // por engano passaria despercebido.
    var corpo = await ClienteComo("Administrador").GetStringAsync("/api/componentes");
    var raiz = JsonDocument.Parse(corpo).RootElement;

    Assert.Equal(1, raiz.GetProperty("pagina").GetInt32());
    Assert.Equal(20, raiz.GetProperty("tamanho").GetInt32());
  }

  [Fact]
  public async Task Pagina_e_total_respeitam_a_busca()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    await CriarAsync(cliente, $"{prefixo}-c");
    await CriarAsync(cliente, $"{prefixo}-a");
    await CriarAsync(cliente, $"{prefixo}-b");

    var pagina1 = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={prefixo}&pagina=1&tamanho=2")).RootElement;
    var pagina2 = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={prefixo}&pagina=2&tamanho=2")).RootElement;

    Assert.Equal(3, pagina1.GetProperty("total").GetInt32());
    Assert.Equal(2, pagina1.GetProperty("itens").GetArrayLength());
    Assert.Equal($"{prefixo}-a", pagina1.GetProperty("itens")[0].GetProperty("codigo").GetString());
    Assert.Equal($"{prefixo}-b", pagina1.GetProperty("itens")[1].GetProperty("codigo").GetString());
    Assert.Equal(1, pagina2.GetProperty("itens").GetArrayLength());
    Assert.Equal($"{prefixo}-c", pagina2.GetProperty("itens")[0].GetProperty("codigo").GetString());
  }

  [Fact]
  public async Task Busca_casa_na_descricao_tambem()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    var marcador = $"desc{Guid.NewGuid():N}";
    await cliente.PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{prefixo}-a", descricao = $"Peca {marcador} especial", tipo = "Bruto" });

    var corpo = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes?busca={marcador}")).RootElement;

    Assert.Equal(1, corpo.GetProperty("total").GetInt32());
    Assert.Equal($"{prefixo}-a", corpo.GetProperty("itens")[0].GetProperty("codigo").GetString());
  }

  [Theory]
  [InlineData("pagina=0")]
  [InlineData("tamanho=0")]
  [InlineData("tamanho=101")]
  public async Task Faixa_de_paginacao_invalida_responde_400(string query)
  {
    // Adendo B14: a faixa NAO tem CHECK equivalente no banco, entao a guarda da aplicacao e a
    // unica defesa e merece a prova mais forte (HTTP), nao so a de Application.
    var resposta = await ClienteComo("Administrador").GetAsync($"/api/componentes?{query}");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Tipo_invalido_responde_400_e_nao_500()
  {
    // Prova que a lista fechada e validada no use case, ANTES de o CK_Componente_Tipo estourar
    // como SqlException — a diferenca entre 400 e 500.
    var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{NovoPrefixo()}-a", descricao = "Suporte", tipo = "Qualquer" });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Codigo_duplicado_inativo_responde_409_indicando_reativacao()
  {
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { ativo = false });

    var resposta = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
    Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
    Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Componente_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
  {
    var cliente = ClienteComo("Administrador");
    var prefixo = NovoPrefixo();
    var codigo = $"{prefixo}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    await cliente.PatchAsJsonAsync($"/api/componentes/{id}/ativo", new { ativo = false });

    var padrao = await cliente.GetStringAsync($"/api/componentes?busca={prefixo}");
    var comInativos = await cliente.GetStringAsync(
        $"/api/componentes?busca={prefixo}&incluirInativos=true");

    Assert.DoesNotContain(codigo, padrao);
    Assert.Contains(codigo, comInativos);
  }

  [Fact]
  public async Task Editar_altera_descricao_e_tipo()
  {
    var cliente = ClienteComo("Administrador");
    var codigo = $"{NovoPrefixo()}-a";
    var criado = await cliente.PostAsJsonAsync("/api/componentes", CorpoValido(codigo));
    var id = await IdDaResposta(criado);

    var resposta = await cliente.PutAsJsonAsync(
        $"/api/componentes/{id}",
        new { codigo, descricao = "Suporte reforcado", tipo = "Montagem" });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("Suporte reforcado", corpo.GetProperty("descricao").GetString());
    Assert.Equal("Montagem", corpo.GetProperty("tipo").GetString());
  }

  [Fact]
  public async Task Editar_componente_inexistente_responde_404()
  {
    // Adendo B7: sem este teste nada exercita o `resultado.Sucesso ? Ok(...) : TraduzirFalha(...)`
    // do controller — trocar o corpo do Editar por `return Ok(resultado.Valor);` ficaria verde.
    var resposta = await ClienteComo("Administrador")
        .PutAsJsonAsync("/api/componentes/999999", CorpoValido($"{NovoPrefixo()}-a"));

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Definir_ativo_em_componente_inexistente_responde_404()
  {
    var resposta = await ClienteComo("Administrador")
        .PatchAsJsonAsync("/api/componentes/999999/ativo", new { ativo = false });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Descricao_em_branco_responde_400()
  {
    var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
        "/api/componentes",
        new { codigo = $"{NovoPrefixo()}-a", descricao = " ", tipo = "Fabricado" });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Theory]
  [InlineData("codigo", 51)]
  [InlineData("descricao", 201)]
  public async Task Campo_maior_que_a_coluna_responde_400_e_nao_500(string campo, int tamanho)
  {
    // Um caractere alem de NVARCHAR(50)/(200) de dbo.Componente. Prova que o [MaxLength] de cada
    // parametro de NovoComponenteDto pega ANTES de o insert estourar SqlException.
    //
    // `tipo` fica FORA deste Theory de proposito (adendo B8, "prova falsa em campo de lista
    // fechada"): o [MaxLength(20)] dele nao e testavel neste nivel, porque um valor de 21
    // caracteres tambem esta fora de Bruto|Fabricado|Montagem — o 400 chegaria pela validacao do
    // use case e o teste passaria com o atributo removido. Escrever o InlineData de `tipo` daria
    // ao revisor a impressao de que o atributo esta coberto quando nao esta. O atributo continua
    // no DTO como defesa em profundidade e espelho da coluna, sem teste que o falsifique.
    var valores = new Dictionary<string, object>
    {
      ["codigo"] = $"{NovoPrefixo()}-a",
      ["descricao"] = "Suporte lateral",
      ["tipo"] = "Fabricado",
    };
    valores[campo] = new string('x', tamanho);

    var resposta = await ClienteComo("Administrador").PostAsJsonAsync("/api/componentes", valores);

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter FullyQualifiedName~ComponentesEndpointsTests`
Expected: todos falham com 404 (a rota não existe).

- [ ] **Step 3: Implementar o controller**

`src/Rastreamento.Api/Controllers/ComponentesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("componentes")]
[Authorize]
public class ComponentesController : CadastroControllerBase
{
  /// <summary>
  /// Primeira entidade de CATALOGO com dois perfis de escrita: na 1A, catalogo era so
  /// Administrador e PCP so aparecia em Pedido/Agrupamento. Decisao do usuario em 2026-08-04 —
  /// quem planeja a producao e quem conhece as pecas, e depender do Administrador para cada peca
  /// nova travaria o cadastro.
  /// </summary>
  private const string PerfisDeEscrita = "Administrador,PCP";

  private readonly CadastroDeComponenteUseCase _cadastro;

  public ComponentesController(CadastroDeComponenteUseCase cadastro) => _cadastro = cadastro;

  /// <summary>
  /// Unica falha possivel aqui e faixa de paginacao invalida (400) — por isso a traducao e direta
  /// em vez de passar pelo `TraduzirFalha`, que existe para o 409 de duplicidade. Pagina alem do
  /// fim NAO e falha: sai 200 com `itens` vazio e o `total` verdadeiro.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> Listar(
      [FromQuery] string? busca = null,
      [FromQuery] bool incluirInativos = false,
      [FromQuery] int pagina = 1,
      [FromQuery] int tamanho = CadastroDeComponenteUseCase.TamanhoDePaginaPadrao,
      CancellationToken ct = default)
  {
    var resultado = await _cadastro.Listar(busca, incluirInativos, pagina, tamanho, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : BadRequest(new { erro = resultado.Erro });
  }

  [HttpPost]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Cadastrar(
      [FromBody] NovoComponenteDto novo, CancellationToken ct)
  {
    var resultado = await _cadastro.Cadastrar(novo, ct);
    if (resultado.Sucesso)
      return CreatedAtAction(nameof(Listar), new { id = resultado.Valor!.Id }, resultado.Valor);

    return await TraduzirFalha(resultado.TipoDoErro, resultado.Erro, Duplicado(novo.Codigo), ct);
  }

  [HttpPut("{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Editar(
      int id, [FromBody] NovoComponenteDto alterado, CancellationToken ct)
  {
    var resultado = await _cadastro.Editar(id, alterado, ct);
    return resultado.Sucesso
        ? Ok(resultado.Valor)
        : await TraduzirFalha(
            resultado.TipoDoErro, resultado.Erro, Duplicado(alterado.Codigo), ct);
  }

  [HttpPatch("{id:int}/ativo")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> DefinirAtivo(
      int id, [FromBody] DefinirAtivoDto corpo, CancellationToken ct) =>
      TraduzirResultado(await _cadastro.DefinirAtivo(id, corpo.Ativo, ct));

  /// <summary>Como Componente pergunta pelo duplicado: por codigo (UQ_Componente_Codigo).</summary>
  private LocalizadorDeDuplicado Duplicado(string codigo) =>
      ct => _cadastro.LocalizarDuplicado(codigo, ct);
}
```

- [ ] **Step 4: Registrar no DI**

Em `src/Rastreamento.Api/Program.cs`, logo depois das duas linhas de `Material` (por volta da
linha 111), acrescente:

```csharp
builder.Services.AddScoped<IComponenteRepository, ComponenteRepository>();
builder.Services.AddScoped<CadastroDeComponenteUseCase>();
```

- [ ] **Step 5: Acrescentar o par ao `RegistroDeDependenciasTests` (adendo B4)**

Em `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`, depois de
`[InlineData(typeof(CadastroDeAgrupamentoUseCase))]`:

```csharp
  [InlineData(typeof(IComponenteRepository))]
  [InlineData(typeof(CadastroDeComponenteUseCase))]
```

Este teste é o que prova que `Editar`/`DefinirAtivo` compartilham o mesmo `DbContext` do
`SaveChanges` — sem a linha, um registro `Transient` passaria em silêncio.

- [ ] **Step 6: Rodar a suíte de API**

Run: `dotnet test tests/Rastreamento.Api.Tests`
Expected: `Aprovado: 159` — 135 da baseline + **22** casos de `ComponentesEndpointsTests` + **2**
`[InlineData]` novos do registro de dependências. A conta dos 22: 12 `[Fact]` mais os `[Theory]`
(`Perfis_de_escrita` 2, `Operador_nao_escreve` 3, `Faixa_de_paginacao_invalida` 3,
`Campo_maior_que_a_coluna` 2) = 12 + 10. Refaça a conta a partir dos atributos antes de concluir
que há erro.

- [ ] **Step 7: Medir as mutações — CONFIRA O BANCO ANTES E DEPOIS**

Antes de começar:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Componente;"
```

Rode a partir do **checkout principal** (`C:/Users/gabriel.santos/Desktop/hihi/repositorios/TCC`),
não da worktree — `docker compose` na worktree cria um stack paralelo.

Mutações, uma de cada vez, com reversão:

1. Trocar `PerfisDeEscrita` por `"Administrador"` → esperado: o caso `PCP` de
   `Perfis_de_escrita_cadastram_componente` morre.
2. Remover `[Authorize(Roles = PerfisDeEscrita)]` do `PUT` → esperado: o caso `PUT` de
   `Operador_nao_escreve_em_componente` morre. **Esta pode escrever no banco** (adendo B10) —
   confira a contagem depois e apague o que sobrou.
3. Trocar o default `tamanho = ...TamanhoDePaginaPadrao` por `tamanho = 5` → esperado:
   `Listagem_sem_parametros_usa_pagina_1_e_tamanho_20` morre.
4. Trocar o corpo do `Editar` por `return Ok(resultado.Valor);` → esperado:
   `Editar_componente_inexistente_responde_404` morre (adendo B7).
5. Remover `[MaxLength(50)]` de `NovoComponenteDto.Codigo` → esperado: o caso `codigo` de
   `Campo_maior_que_a_coluna_responde_400_e_nao_500` morre (adendo B8).
6. **Controle do B8:** remover `[MaxLength(20)]` de `NovoComponenteDto.Tipo` → esperado:
   **nenhum teste morre**. Isso confirma que a decisão de não escrever o `[InlineData]` de `tipo`
   estava certa. Anote o resultado no relatório.

Depois de tudo, confira a contagem do banco de novo e deixe-o como encontrou.

- [ ] **Step 8: Atualizar `specs/05-api-endpoints.md`**

Substitua a linha `- \`GET/POST /componentes\`` pelo contrato real (mantenha as três linhas dos
sub-recursos de receita como estão, e acrescente a nota de que ficaram para a 1C):

```markdown
- `GET /componentes` — `?busca=` (casa em código **ou** descrição), `?incluirInativos=false`,
  `?pagina=1`, `?tamanho=20` (teto 100) *(qualquer perfil autenticado)*. Responde
  `{ itens, total, pagina, tamanho }`; `total` é contado com os mesmos filtros da página.
  Faixa fora do permitido responde 400; página além do fim responde 200 com `itens` vazio.
- `POST /componentes` *(Administrador, PCP)* — `{ codigo, descricao, tipo }`, `tipo` em
  `Bruto | Fabricado | Montagem`
- `PUT /componentes/{id}` *(Administrador, PCP)* — idem
- `PATCH /componentes/{id}/ativo` *(Administrador, PCP)* — `{ ativo }`
  Não existe `DELETE`: catálogo se inativa, não se exclui.
- `GET/POST /componentes/{id}/filhos-padrao` — **Fase 1C**, não implementado
- `GET/POST /componentes/{id}/materiais-padrao` — **Fase 1C**, não implementado
- `GET/POST /componentes/{id}/roteiro-padrao` — **Fase 1C**, não implementado
```

- [ ] **Step 9: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
git add src/Rastreamento.Api/Controllers/ComponentesController.cs \
        src/Rastreamento.Api/Program.cs \
        tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs \
        tests/Rastreamento.Api.Tests/ComponentesEndpointsTests.cs \
        specs/05-api-endpoints.md
git commit -m "feat(api): endpoints de Componente com busca e paginacao"
```

---

### Task 4: Módulo de API do front

**Files:**
- Modify: `web/src/api/cadastros.ts` (acrescentar ao fim)
- Test: `web/src/api/cadastros.test.ts` (acrescentar ao fim do `describe`)

**Interfaces:**
- Consumes: `apiFetch`, `lerOuFalhar`, `ConflitoDeCadastro`, `ehConflito` (já existem em `cadastros.ts`).
- Produces: `ComponenteDto`, `TipoDeComponente`, `NovoComponente`, `PaginaDe<T>`, `FiltroDeComponentes`, `listarComponentes(f)`, `criarComponente(c)`, `definirAtivoComponente(id, ativo)`.

- [ ] **Step 1: Instalar as dependências do front (worktree nova)**

```bash
cd web && npm ci
npm test -- --run
```
Expected: `Tests 45 passed (45)`, `Test Files 3 passed (3)`.

- [ ] **Step 2: Escrever os testes (falhando)**

No fim do `describe('cadastros', ...)` de `web/src/api/cadastros.test.ts`, e acrescentando
`listarComponentes, criarComponente, definirAtivoComponente` ao `import` do topo:

```ts
  // Adendo F4: toda funcao nova do modulo tem DOIS testes — URL/metodo/corpo e comportamento em
  // erro. O primeiro assere `fetchMock.mock.calls[0][0]`, nao o retorno: o retorno vem do stub e
  // nao prova nada sobre o que foi pedido.
  it('monta a URL de componentes com os quatro parametros', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarComponentes({ busca: 'sup', incluirInativos: false, pagina: 2, tamanho: 50 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/componentes?busca=sup&incluirInativos=false&pagina=2&tamanho=50',
    )
  })

  // Par obrigatorio do teste acima (adendo F3): hardcodar `incluirInativos=false` na URL passaria
  // com so o caso `false`, e o checkbox "Mostrar inativos" quebraria em silencio.
  it('monta a URL de componentes incluindo inativos quando pedido', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ itens: [], total: 0, pagina: 1, tamanho: 20 }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await listarComponentes({ busca: '', incluirInativos: true, pagina: 1, tamanho: 20 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/componentes?busca=&incluirInativos=true&pagina=1&tamanho=20',
    )
  })

  it('devolve a pagina de componentes', async () => {
    const pagina = {
      itens: [{ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Fabricado', ativo: true }],
      total: 1,
      pagina: 1,
      tamanho: 20,
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify(pagina), { status: 200 }),
    ))

    const resultado = await listarComponentes(
      { busca: '', incluirInativos: false, pagina: 1, tamanho: 20 },
    )

    expect(resultado.total).toBe(1)
    expect(resultado.itens[0].tipo).toBe('Fabricado')
  })

  // Corpo JSON nao-vazio de proposito (adendo F6): com `''` o `.json()` lancaria sozinho e o
  // teste passaria mesmo com a guarda `if (!resp.ok) throw` removida.
  it('lanca quando listar componentes falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status: 500 })))

    await expect(
      listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 20 }),
    ).rejects.toThrow()
  })

  it('cria componente com metodo, URL e corpo corretos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto', ativo: true }),
        { status: 201 },
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    await criarComponente({ codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/componentes')
    expect(fetchMock.mock.calls[0][1].method).toBe('POST')
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
      codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto',
    })
  })

  it('devolve o conflito quando o codigo do componente ja existe inativo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 7 }),
        { status: 409 },
      ),
    ))

    const resultado = await criarComponente(
      { codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Bruto' },
    )

    expect(ehConflito(resultado)).toBe(true)
    expect((resultado as ConflitoDeCadastro).idExistente).toBe(7)
  })

  it('define ativo do componente com metodo, URL e corpo corretos', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await definirAtivoComponente(4, false)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/componentes/4/ativo')
    expect(fetchMock.mock.calls[0][1].method).toBe('PATCH')
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ ativo: false })
  })

  // `definirAtivoComponente` bate num endpoint [Authorize(Roles)] e nao chama `.json()`, entao o
  // corpo vazio aqui e inofensivo (excecao registrada no F6). O teste existe para o `try/catch`
  // da tela (F2) nao ser decorativo: se a funcao nao lancasse, o catch nunca dispararia.
  it('lanca quando definir ativo do componente falha', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))

    await expect(definirAtivoComponente(4, false)).rejects.toThrow()
  })
```

Acrescente também `type ConflitoDeCadastro` ao import se ainda não estiver lá.

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `cd web && npm test -- --run`
Expected: falha de tipo/import — `listarComponentes` não existe.

- [ ] **Step 4: Implementar as funções**

No fim de `web/src/api/cadastros.ts`:

```ts
export interface ComponenteDto {
  id: number
  codigo: string
  descricao: string
  tipo: string
  ativo: boolean
}

/** Lista fechada de `CK_Componente_Tipo` — diferente de `unidadeMedida`, que é texto livre. */
export type TipoDeComponente = 'Bruto' | 'Fabricado' | 'Montagem'

export interface NovoComponente {
  codigo: string
  descricao: string
  tipo: TipoDeComponente
}

/** Espelha o `PaginaDto<T>` do backend. `total` é sob o mesmo filtro, não o tamanho de `itens`. */
export interface PaginaDe<T> {
  itens: T[]
  total: number
  pagina: number
  tamanho: number
}

export interface FiltroDeComponentes {
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
}

/**
 * A montagem da URL mora aqui, e não no componente, porque é isto que a torna provável em teste
 * (adendo F4 — a lição de `criarSetor`/`criarMaterial`, que ficaram 5 tasks sem prova de URL).
 * `URLSearchParams` preserva a ordem de inserção, então a URL é determinística e asserível.
 */
export async function listarComponentes(
  f: FiltroDeComponentes,
): Promise<PaginaDe<ComponenteDto>> {
  const params = new URLSearchParams({
    busca: f.busca,
    incluirInativos: String(f.incluirInativos),
    pagina: String(f.pagina),
    tamanho: String(f.tamanho),
  })
  const resp = await apiFetch(`/componentes?${params}`)
  if (!resp.ok) throw new Error(`Falha ao listar componentes (${resp.status}).`)
  return (await resp.json()) as PaginaDe<ComponenteDto>
}

/** O único 409 possível aqui é `ValorDuplicado` sobre `codigo` (UQ_Componente_Codigo). */
export function criarComponente(
  c: NovoComponente,
): Promise<ComponenteDto | ConflitoDeCadastro> {
  return apiFetch('/componentes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(c),
  }).then(lerOuFalhar<ComponenteDto>)
}

export async function definirAtivoComponente(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/componentes/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new Error(`Falha ao alterar o componente (${resp.status}).`)
}

// Sem editarComponente aqui, de proposito, pelo mesmo motivo do editarPedido acima: o
// PUT /componentes/{id} existe e esta testado no backend (Task 3), mas a tela de Componentes nao
// tem UI de edicao — exportar a funcao sem chamador seria codigo morto. Ela nasce junto com a
// tela que a usar.
```

- [ ] **Step 5: Rodar os testes**

Run: `cd web && npm test -- --run`
Expected: `Tests 53 passed (53)` (45 da baseline + 8 novos), 0 falhas.

- [ ] **Step 6: Medir as mutações**

Uma de cada vez, com reversão:

1. Trocar `/componentes?${params}` por `/componentes-errado?${params}` → esperado: os dois testes
   de URL morrem.
2. Trocar `method: 'POST'` por `'PUT'` em `criarComponente` → esperado:
   `cria componente com metodo, URL e corpo corretos` morre.
3. Remover `if (!resp.ok) throw` de `listarComponentes` → esperado:
   `lanca quando listar componentes falha` morre. **Se não morrer, o mock está mascarando** —
   confira que o corpo é `'{}'` e não `''` (adendo F6).
4. Remover `if (!resp.ok) throw` de `definirAtivoComponente` → esperado:
   `lanca quando definir ativo do componente falha` morre.
5. Trocar a ordem dos campos em `URLSearchParams` → esperado: os dois testes de URL morrem
   (confirma que a URL é determinística; **reverta**, a ordem escrita é a que os testes fixam).

- [ ] **Step 7: Lint e commit**

```bash
cd web && npm run lint
```
Expected: **1 warning pré-existente e alheio** (`web/src/auth/AuthContext.tsx:48`,
`react/only-export-components`, da Fase 0). Não é seu, não corrija.

```bash
git add web/src/api/cadastros.ts web/src/api/cadastros.test.ts
git commit -m "feat(front): modulo de API de Componente com busca e paginacao"
```

---

### Task 5: Harness de teste de componente (`@testing-library/react`)

Esta task **não** entrega tela. Entrega a capacidade de testar componente, provada por um teste
de fumaça sobre uma tela que já existe. Separada de propósito: se a configuração do ambiente
brigar (jsdom, `Response` global, cleanup sem `globals: true`), o problema fica isolado e o
revisor consegue aprovar ou rejeitar "o harness roda" sem isso se misturar com "a tela está certa".

**Files:**
- Modify: `web/package.json` (duas devDependencies)
- Test: `web/src/pages/MateriaisPage.test.tsx`

**Interfaces:**
- Consumes: `MateriaisPage`, `listarMateriais` (já existem).
- Produces: a capacidade de escrever `*.test.tsx` com `render`/`screen`; o padrão do docblock
  `// @vitest-environment jsdom` e do `afterEach(cleanup)` explícito, que a Task 6 copia.

- [ ] **Step 1: Instalar as dependências**

```bash
cd web && npm install --save-dev @testing-library/react jsdom
```

Só estas duas. **Não** instale `@testing-library/jest-dom` (os `getBy*` já lançam quando não
acham, então os matchers extras não são necessários) nem `@testing-library/user-event` (o
`fireEvent` do próprio RTL cobre o que esta fase precisa). Menos dependência, menos superfície.

- [ ] **Step 2: Escrever o teste de fumaça**

`web/src/pages/MateriaisPage.test.tsx`:

```tsx
// @vitest-environment jsdom
//
// O ambiente e declarado POR ARQUIVO, e nao em `vite.config.ts`, de proposito: os 45 testes que
// ja existem rodam em ambiente `node` e usam `new Response(...)`; trocar o ambiente global para
// jsdom arriscaria mexer nos globals de que eles dependem, sem ganho nenhum. Teste de componente
// e a excecao, entao a excecao fica no arquivo.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MateriaisPage } from './MateriaisPage'
import { inicializar, _resetParaTeste } from '../api/client'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que so existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha, o segundo teste do arquivo renderiza por cima do primeiro e os `getBy*` falham
// com "found multiple elements".
afterEach(cleanup)

describe('MateriaisPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os materiais que a API devolveu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: 1, codigo: 'CH-001', descricao: 'Chapa de aco 3mm', unidadeMedida: 'KG', ativo: true },
        ]),
        { status: 200 },
      ),
    ))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
  })
})
```

- [ ] **Step 3: Rodar**

Run: `cd web && npm test -- --run`
Expected: `Tests 54 passed (54)` (53 da Task 4 + 1), `Test Files 4 passed (4)`.

**Se falhar por `Response` ou `fetch` indefinidos sob jsdom:** não improvise configuração. Copie a
mensagem de erro exata para o relatório e pare a task — a decisão de como configurar o ambiente é
do revisor. O risco existe porque o ambiente jsdom substitui os globals do Node, e os 53 testes já
existentes dependem de `new Response(...)`; foi justamente para não arriscá-los que o ambiente é
declarado por arquivo em vez de em `vite.config.ts`.

- [ ] **Step 4: Medir a mutação**

Troque `listarMateriais(comInativos)` por `Promise.resolve([])` em `MateriaisPage.tsx` →
esperado: `mostra os materiais que a API devolveu` morre. **Reverta.** Isso prova que o teste de
componente realmente observa a renderização, e não passa por acidente.

- [ ] **Step 5: Lint e commit**

```bash
cd web && npm run lint
git add web/package.json web/package-lock.json web/src/pages/MateriaisPage.test.tsx
git commit -m "chore(front): harness de teste de componente com @testing-library/react"
```

---

### Task 6: `ComponentesPage`

**Files:**
- Create: `web/src/pages/ComponentesPage.tsx`
- Create: `web/src/pages/ComponentesPage.test.tsx`
- Modify: `web/src/App.tsx` (rota `/componentes`)
- Modify: `web/src/pages/HomePage.tsx` (link)

**Interfaces:**
- Consumes: `listarComponentes`, `criarComponente`, `definirAtivoComponente`, `ehConflito`,
  `ComponenteDto`, `NovoComponente`, `TipoDeComponente` (Task 4); o padrão de teste de componente
  (Task 5).
- Produces: `ComponentesPage`.

- [ ] **Step 1: Escrever os testes de componente (falhando)**

`web/src/pages/ComponentesPage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ComponentesPage } from './ComponentesPage'
import { inicializar, _resetParaTeste } from '../api/client'

afterEach(cleanup)

/**
 * Uma resposta NOVA a cada chamada. Isto nao e detalhe: `Response` tem corpo de uso unico, e
 * `mockResolvedValue(paginaComTotal(...))` devolveria a MESMA instancia em todo fetch — o segundo
 * `.json()` estouraria "Body has already been read". Os testes desta tela chamam a API varias
 * vezes (carga inicial, troca de pagina, troca de busca), entao o mock e `mockImplementation`.
 */
function paginaComTotal(total: number, pagina = 1, tamanho = 20) {
  return new Response(
    JSON.stringify({
      itens: [{ id: 1, codigo: 'SUP-001', descricao: 'Suporte', tipo: 'Fabricado', ativo: true }],
      total,
      pagina,
      tamanho,
    }),
    { status: 200 },
  )
}

describe('ComponentesPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('lista os componentes que a API devolveu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    expect(await screen.findByText('SUP-001')).toBeTruthy()
  })

  // ESTE e o teste que justifica ter adotado @testing-library/react nesta fase. Sem ele, o reset
  // de pagina e comportamento sem prova nenhuma: buscar algo que cabe em 2 paginas estando na
  // pagina 7 mostraria lista vazia, com cara de bug, e nada quebraria.
  it('volta para a pagina 1 quando a busca muda', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.change(screen.getByPlaceholderText('Buscar por código ou descrição'), {
      target: { value: 'sup' },
    })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('busca=sup')
      expect(ultima).toContain('pagina=1')
    })
  })

  it('volta para a pagina 1 quando o tamanho da pagina muda', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(100)))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.at(-1)![0]).toContain('pagina=2')
    })

    fireEvent.change(screen.getByLabelText('Por página'), { target: { value: '50' } })

    await waitFor(() => {
      const ultima = fetchMock.mock.calls.at(-1)![0] as string
      expect(ultima).toContain('tamanho=50')
      expect(ultima).toContain('pagina=1')
    })
  })

  it('desabilita Anterior na primeira pagina e Proxima na ultima', async () => {
    // Com total 1 e tamanho 20 existe UMA pagina: os dois botoes ficam desabilitados. Mata a
    // mutacao de deixar "Proxima" sempre habilitada, que levaria a uma pagina vazia.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(1))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
    await screen.findByText('SUP-001')

    expect((screen.getByRole('button', { name: 'Anterior' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Próxima' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('mostra o total e a contagem de paginas', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(paginaComTotal(41))))

    render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

    // 41 itens em paginas de 20 = 3 paginas (arredonda para cima, nao para baixo).
    expect(await screen.findByText('Página 1 de 3 — 41 no total')).toBeTruthy()
  })
})
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `cd web && npm test -- --run`
Expected: falha de import — `ComponentesPage` não existe.

- [ ] **Step 3: Implementar a tela**

`web/src/pages/ComponentesPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoComponente = { codigo: '', descricao: '', tipo: 'Fabricado' }

/** As três opções de `CK_Componente_Tipo`. Lista fechada, ao contrário de `unidadeMedida`. */
const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/** Dentro do teto de 100 do backend, de propósito: um valor acima viraria 400. */
const TAMANHOS = [20, 50, 100]

export function ComponentesPage() {
  const [componentes, setComponentes] = useState<ComponenteDto[]>([])
  const [total, setTotal] = useState(0)
  const [busca, setBusca] = useState('')
  const [pagina, setPagina] = useState(1)
  const [tamanho, setTamanho] = useState(20)
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  async function carregar() {
    setCarregando(true)
    try {
      const resposta = await listarComponentes({ busca, incluirInativos, pagina, tamanho })
      setComponentes(resposta.itens)
      setTotal(resposta.total)
    } catch {
      setErro('Não foi possível carregar os componentes.')
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [busca, incluirInativos, pagina, tamanho])

  // Trocar a busca, o tamanho de pagina ou o filtro de inativos VOLTA para a pagina 1. Sem isto,
  // buscar algo que cabe em 2 paginas estando na pagina 7 mostra lista vazia, com cara de bug.
  function mudarBusca(valor: string) {
    setPagina(1)
    setBusca(valor)
  }

  function mudarTamanho(valor: number) {
    setPagina(1)
    setTamanho(valor)
  }

  function mudarInativos(valor: boolean) {
    setPagina(1)
    setIncluirInativos(valor)
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    try {
      const resultado = await criarComponente(form)
      if (ehConflito(resultado)) {
        // O conflito e sempre sobre o codigo (UQ_Componente_Codigo); descricao repetida passa.
        if (resultado.existeInativo) {
          setErro(`Já existe um componente com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um componente com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch {
      setErro('Não foi possível salvar o componente.')
    }
  }

  // O 403 do backend e a fronteira de perfil (o link aparece para todos de proposito, e
  // PATCH /componentes/{id}/ativo e [Authorize(Roles = "Administrador,PCP")]), entao aqui e onde
  // um usuario sem permissao descobre isso — sem try/catch viraria uma promise rejeitada sem
  // tratamento e a tela nao diria nada.
  async function alternarAtivo(componente: ComponenteDto) {
    try {
      await definirAtivoComponente(componente.id, !componente.ativo)
      setErro(null)
      await carregar()
    } catch {
      setErro('Não foi possível alterar o componente.')
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch {
      setErro('Não foi possível reativar o componente.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Componentes</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código"
          required
          className="border rounded px-3 py-2"
        />
        <input
          value={form.descricao}
          onChange={(e) => setForm({ ...form, descricao: e.target.value })}
          placeholder="Descrição"
          required
          className="border rounded px-3 py-2"
        />
        {/* Lista fechada (CK_Componente_Tipo): select, nao input livre. */}
        <select
          value={form.tipo}
          onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
          aria-label="Tipo"
          className="border rounded px-3 py-2"
        >
          {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}
      {idReativavel !== null && (
        <button onClick={() => reativar(idReativavel)} className="border rounded px-3 py-2 self-start">
          Reativar o existente
        </button>
      )}

      <input
        value={busca}
        onChange={(e) => mudarBusca(e.target.value)}
        placeholder="Buscar por código ou descrição"
        className="border rounded px-3 py-2"
      />

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => mudarInativos(e.target.checked)}
        />
        Mostrar inativos
      </label>

      <label className="flex items-center gap-2 text-sm text-gray-600">
        Por página
        <select
          value={tamanho}
          onChange={(e) => mudarTamanho(Number(e.target.value))}
          className="border rounded px-2 py-1"
        >
          {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
      </label>

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {componentes.map((c) => (
            <li key={c.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={c.ativo ? '' : 'text-gray-400 line-through'}>
                <strong>{c.codigo}</strong> — {c.descricao} ({c.tipo})
              </span>
              <button onClick={() => alternarAtivo(c)} className="text-sm border rounded px-2 py-1">
                {c.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="flex items-center gap-3 text-sm">
        <button
          onClick={() => setPagina(pagina - 1)}
          disabled={pagina <= 1}
          className="border rounded px-3 py-1 disabled:opacity-40"
        >
          Anterior
        </button>
        <span className="text-gray-600">
          Página {pagina} de {totalDePaginas} — {total} no total
        </span>
        <button
          onClick={() => setPagina(pagina + 1)}
          disabled={pagina >= totalDePaginas}
          className="border rounded px-3 py-1 disabled:opacity-40"
        >
          Próxima
        </button>
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Registrar a rota e o link**

Em `web/src/App.tsx`, acrescente o import junto dos outros e a rota depois da de `/materiais`:

```tsx
import { ComponentesPage } from './pages/ComponentesPage'
```
```tsx
      <Route path="/componentes" element={<ProtectedRoute><ComponentesPage /></ProtectedRoute>} />
```

Em `web/src/pages/HomePage.tsx`, acrescente o link depois do de Materiais (linha 49):

```tsx
        <Link to="/componentes" className="border rounded px-3 py-2">Componentes</Link>
```

- [ ] **Step 5: Rodar os testes**

Run: `cd web && npm test -- --run`
Expected: `Tests 59 passed (59)` (54 da Task 5 + 5 novos), `Test Files 5 passed (5)`.

- [ ] **Step 6: Medir as mutações**

Uma de cada vez, com reversão:

1. Remover `setPagina(1)` de `mudarBusca` → esperado: `volta para a pagina 1 quando a busca muda`
   morre. **Esta é a mutação que justifica a task inteira** — antes de `@testing-library/react`
   ela não matava nada.
2. Remover `setPagina(1)` de `mudarTamanho` → esperado: o teste de tamanho morre.
3. Trocar `Math.ceil` por `Math.floor` em `totalDePaginas` → esperado:
   `mostra o total e a contagem de paginas` morre (41/20 daria 2 em vez de 3).
4. Trocar `disabled={pagina >= totalDePaginas}` por `disabled={false}` → esperado:
   `desabilita Anterior na primeira pagina e Proxima na ultima` morre.
5. Remover `busca` do array de dependências do `useEffect` → esperado: o teste de busca morre
   (a URL nunca chega a mudar).

- [ ] **Step 7: Build de tipos, lint e commit**

```bash
cd web && npm run build
npm run lint
```
Expected: `tsc -b` sem erros; lint com o **1 warning alheio** de sempre.

```bash
git add web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx \
        web/src/App.tsx web/src/pages/HomePage.tsx
git commit -m "feat(front): tela de Componentes com busca e paginacao"
```

---

### Task 7: Specs, `CLAUDE.md` e varredura da convenção nova

**Files:**
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `CLAUDE.md`
- Create: `docs/superpowers/plans/2026-08-04-fase-1b-relatorio.md`

**Interfaces:**
- Consumes: tudo o que as Tasks 1–6 entregaram.
- Produces: o registro. Nenhum código.

- [ ] **Step 1: Rodar a suíte inteira e anotar os números reais**

```bash
dotnet test Rastreamento.slnx
cd web && npm test -- --run
cd .. && dotnet build Rastreamento.slnx -warnaserror
```

Anote os três números **medidos** — não copie os esperados deste plano. Se divergirem, a
divergência é o achado mais importante do relatório.

- [ ] **Step 2: Atualizar `specs/06-roadmap-mvp.md`**

**Não mexa na seção "Fora das fases — importar a estrutura a partir do CAD"**, no fim do arquivo:
ela já foi escrita em 2026-08-04, registra decisão do usuário, e não é trabalho desta fase. Os dois
trechos abaixo são os únicos a mudar.

Substitua o bloco de citação da Fase 1 (linhas 25–29) por:

```markdown
> **1A concluída** (`Setor`, `Material`, `Pedido`, `Agrupamento` — CRUD pela tela, com
> autorização por perfil no backend).
>
> **1B concluída**: `Componente` (catálogo) — CRUD pela tela, escrita para Administrador e PCP,
> com **busca e paginação no servidor** (`?busca=`, `?pagina=`, `?tamanho=`, teto 100). Primeira
> listagem paginada do sistema; o contrato é o `PaginaDto<T>` genérico de
> `Application/Common`. `Setor` e `Material` **não** foram migrados para ele — dívida rastreada,
> e não item esquecido: eles não têm o volume que motivou a paginação.
>
> **Falta 1C**: a receita padrão (`ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`,
> `ComponenteRoteiroPadrao`), que recebe plano próprio. **A Fase 2 depende dela** — "criar
> `EstruturaItem` copiando a receita" não tem o que copiar enquanto a 1C não existir.
>
> Dívidas rastreadas de 1A: camada global de erro de API no front e gating de navegação por
> perfil.
```

E, no item de `ALTER` da Fase 2 (linhas 49–51), acrescente ao fim do parágrafo:

```markdown
  **Não se aplica a um banco regenerado:** o banco de dev foi recriado em 2026-08-04 a partir
  deste `.sql`, então as colunas já vieram no `CREATE`. Vale só para instalação anterior a essa
  data.
```

- [ ] **Step 3: Atualizar `CLAUDE.md`**

Na seção "Pré-requisito externo dos testes", acrescente depois do bloco de `docker compose up -d`:

```markdown
**Banco regenerado em 2026-08-04.** Ele foi recriado do zero (`DROP DATABASE` +
`specs/02-modelo-de-dados.sql` + `db/seed.sql`) porque estava em desacordo com a fonte de
verdade: `dbo.Componente` não tinha `ArquivoSolido`/`ArquivoFoto`, que nasceram no schema depois
de o banco ter sido criado. Consequência: **os quatro blocos de `ALTER` idempotente abaixo viraram
no-op nesta máquina.** Eles continuam corretos, e necessários, para quem tiver banco anterior a
essa data — não os remova.
```

- [ ] **Step 4: Varrer o código anterior contra a convenção nova**

A lição do adendo — *"toda vez que uma convenção nova nascer, o passo seguinte obrigatório é
varrer as funções anteriores contra ela"* — vale para as duas convenções que esta fase criou:

1. **Paginação (`PaginaDto<T>`).** Varra `listarSetores`, `listarMateriais`, `listarPedidos` e
   `listarAgrupamentos`. **Resultado esperado: nenhuma migração nesta fase** — é decisão registrada
   na spec, não omissão. Registre no relatório que a varredura foi feita e por que o resultado foi
   "não migrar".
2. **Teste de componente (`@testing-library/react`).** Varra as telas existentes: `SetoresPage`,
   `MateriaisPage`, `PedidosPage`, `PedidoDetalhePage`. **Não escreva os testes agora** — não é
   escopo da 1B. Registre no relatório a **lista** do que passou a ser testável e não era, com
   destaque para os três itens que o ledger nomeia como indefensáveis até aqui: o caminho
   "Cancelar" do modal de `PedidoDetalhePage`, o `autoFocus`, e o `required` (F1).

- [ ] **Step 5: Escrever o relatório**

`docs/superpowers/plans/2026-08-04-fase-1b-relatorio.md`, contendo:

- números **medidos** das três suítes, antes e depois;
- por task: o par implementer/reviewer usado (o ledger cobra isso em **toda** entrada — foi por
  não registrar nas Tasks 6 e 7 da 1A que a decisão da Task 8 teve de ser feita por argumento em
  vez de por dado);
- a tabela de mutações medidas: qual mutação, quantos testes morreram, e se bateu com o esperado;
- em especial, o resultado da **mutação de controle do B8** (remover `[MaxLength(20)]` de `Tipo`
  não deve matar nada) e da **mutação do reset de página** (deve matar, e é o que justifica ter
  adotado a `@testing-library/react`);
- o resultado das duas varreduras do Step 4;
- o que ficou de fora e para onde foi.

- [ ] **Step 6: Commit**

```bash
git add specs/06-roadmap-mvp.md CLAUDE.md docs/superpowers/plans/2026-08-04-fase-1b-relatorio.md
git commit -m "docs: fecha a Fase 1B -- roadmap, CLAUDE.md e relatorio com mutacoes medidas"
```

---

## Fechamento da fase

Depois da Task 7, a branch `fase-1b-componente-receita-padrao` está pronta para review de branch
inteira (Opus — é integração entre tasks) e, aprovada, para **Pull Request**. O usuário pediu
explicitamente que o rastro de PR por fase seja mantido: a 1A entrou por push direto e ele
registrou isso como algo a não repetir.

**Números esperados ao fim** (confirme, não confie — estes vieram de contar os atributos deste
plano, não de uma execução): backend **321** (128 Application + 34 Infrastructure + 159 Api);
front **59** em 5 arquivos; `-warnaserror` em 0 warnings.
