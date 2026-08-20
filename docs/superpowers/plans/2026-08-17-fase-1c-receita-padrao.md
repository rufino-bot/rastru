# Fase 1C — Receita padrão do Componente — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar a receita padrão do Componente — filhos, materiais e roteiro — pelo backend e por uma tela, para que a Fase 2 tenha o que copiar ao criar `EstruturaItem`.

**Architecture:** Três sub-recursos sob `/componentes/{id}`, cada um com `GET` (lê) e `POST` (**substitui a receita inteira**). Um repositório único (`IReceitaPadraoRepository`) porque as três tabelas têm a mesma forma de acesso e a detecção de ciclo precisa do grafo inteiro. Um caso de uso (`ReceitaPadraoUseCase`) com três pares de métodos. No front, uma página de detalhe `/componentes/:id` com três seções independentes, cada uma com rascunho local e botão Salvar próprio.

**Tech Stack:** .NET 10 (ASP.NET Core, EF Core Database First, xUnit), React 19 + TypeScript (Vite, Vitest, Testing Library), SQL Server em Docker.

**Spec:** `docs/superpowers/specs/2026-08-17-fase-1c-receita-padrao-design.md` — em conflito entre este plano e a spec, **a spec ganha**; pare e pergunte.

---

## Global Constraints

Valem para **todas** as tasks. Não repetidas em cada uma.

- **Database First.** `specs/02-modelo-de-dados.sql` é a fonte de verdade do schema. **Nunca** `Add-Migration`, **nunca** `EnsureCreated`.
- **Build em 0 warnings:** `dotnet build Rastreamento.slnx -warnaserror`.
- **Nomes de domínio em português** espelhando o DDL (`ComponenteFilhoPadrao`, `QuantidadePadrao`); nomes técnicos em inglês (`Repository`, `UseCase`, `Dto`).
- **Front — cores só por token** (`text-tinta`, `bg-acao`, `border-borda`). `text-gray-*` e afins são reprovados por `web/src/tema/semCorForaDaPaleta.test.ts`.
- **Front — teste de tela** declara `// @vitest-environment jsdom` no topo, usa `web/src/testes/api.ts` (`respostaJson`, `fetchPorRota`) e `afterEach(cleanup)` explícito. Este projeto **não** usa `globals: true`.
- **Front — `npm run build` faz parte do ciclo**, não só `npm test`: erro de tipo em `.test.tsx` quebra o build sem quebrar a suíte.
- **Nada de `min-h-screen`** em tela; quem faz isso é o `AppShell`. Tela nova começa por `<Pagina titulo="…">`.
- **Escrever fonte com Write/Edit, nunca com `Set-Content` do PowerShell** — ele corrompe UTF-8 e a suíte fica verde mesmo assim.
- **Commit a cada task.** Mensagem em português, prefixo convencional (`feat(fase1c):`, `test(fase1c):`, `docs(fase1c):`).

### Baseline MEDIDA em 2026-08-17 (não herdada)

```
BACKEND : 338  =  129 Application  +  38 Infrastructure  +  171 Api
FRONT   : 317 passed / 28 arquivos
BUILD   : -warnaserror, 0 avisos
BANCO   : as 3 tabelas da receita EXISTEM (nenhum ALTER necessário).
          Catálogo atual: 2 Componente, 0 Material, 4 Setor, 2 Usuario.
```

**Cada task declara seu delta, não o total.** Ao fechar uma task, se o total medido divergir do previsto, **corrija a previsão das tasks seguintes na mesma passada** — total absoluto herdado propaga erro task a task.

### Uma extensão deliberada da spec, declarada aqui

A spec §2.3 diz "componente-filho ou **material** inativo" não pode entrar na receita, e **não menciona Setor**. Este plano **também barra Setor inativo no roteiro**: é a mesma classe de problema e `dbo.Setor` tem `Ativo`. Se isso estiver errado, é a spec que decide — pare e pergunte antes de implementar a Task 4.

---

## File Structure

**Backend — criar:**

| Arquivo | Responsabilidade |
|---|---|
| `src/Rastreamento.Domain/Entities/ComponenteFilhoPadrao.cs` | Linha da receita de filhos |
| `src/Rastreamento.Domain/Entities/ComponenteMaterialPadrao.cs` | Linha da receita de materiais |
| `src/Rastreamento.Domain/Entities/ComponenteRoteiroPadrao.cs` | Linha do roteiro |
| `src/Rastreamento.Domain/Abstractions/IReceitaPadraoRepository.cs` | Contrato de acesso às 3 tabelas |
| `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteFilhoPadraoConfiguration.cs` | Mapeamento EF |
| `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteMaterialPadraoConfiguration.cs` | Mapeamento EF |
| `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteRoteiroPadraoConfiguration.cs` | Mapeamento EF |
| `src/Rastreamento.Infrastructure/Persistence/ReceitaPadraoRepository.cs` | Leitura + substituição atômica |
| `src/Rastreamento.Application/Cadastros/ReceitaPadraoUseCase.cs` | Validações e regras (ciclo, ordem, duplicata) |
| `src/Rastreamento.Api/Controllers/ReceitaPadraoController.cs` | 6 ações (3 GET + 3 POST) |

**Backend — modificar:** `RastreamentoDbContext.cs` (3 `DbSet`) · `Application/Cadastros/Dtos.cs` (DTOs novos) · `Api/Program.cs` (2 registros de DI).

**Testes — criar:** `tests/Rastreamento.Infrastructure.Tests/Persistence/ReceitaPadraoMapeamentoTests.cs` · `tests/Rastreamento.Infrastructure.Tests/Persistence/ReceitaPadraoRepositoryTests.cs` · `tests/Rastreamento.Application.Tests/Cadastros/ReceitaPadraoUseCaseTests.cs` · `tests/Rastreamento.Api.Tests/ReceitaPadraoEndpointsTests.cs`.

> **Corrigido depois da review da Task 1** (o plano original dizia a raiz do projeto de teste): os sete testes de mapeamento e de repositório deste projeto vivem em `Persistence/`, e **todos os sete** limpam as linhas que criam num `try/finally`. A prosa deste plano já mandava "siga o padrão dos vizinhos"; os blocos de código exemplo abaixo é que omitiam as duas coisas. **A prosa governa.**
**Testes — modificar:** `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs` (3 entradas).

**Front — criar:** `web/src/components/SeletorComBusca.tsx` (+ `.test.tsx`) · `web/src/api/receitaPadrao.ts` (+ `.test.ts`) · `web/src/pages/ComponenteDetalhePage.tsx` (+ `.test.tsx`).
**Front — modificar:** `web/src/App.tsx` (rota) · `web/src/pages/ComponentesPage.tsx` (link) · `web/src/auth/permissoes.ts` · `web/src/auth/permissoesEspelhamOBackend.test.ts`.

**Banco/docs — criar:** `db/seed-demo.sql`. **Modificar:** `specs/05-api-endpoints.md` · `specs/06-roadmap-mvp.md` · `specs/01-dominio-e-regras-de-negocio.md` · `CLAUDE.md`.

---

## Task 1: Entidades e mapeamento EF das 3 tabelas

**Files:**
- Create: `src/Rastreamento.Domain/Entities/ComponenteFilhoPadrao.cs`
- Create: `src/Rastreamento.Domain/Entities/ComponenteMaterialPadrao.cs`
- Create: `src/Rastreamento.Domain/Entities/ComponenteRoteiroPadrao.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteFilhoPadraoConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteMaterialPadraoConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteRoteiroPadraoConfiguration.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ReceitaPadraoMapeamentoTests.cs`

**Interfaces:**
- Consumes: nada (primeira task).
- Produces: `ComponenteFilhoPadrao { int Id; int ComponentePaiId; int ComponenteFilhoId; decimal QuantidadePadrao; }` · `ComponenteMaterialPadrao { int Id; int ComponenteId; int MaterialId; decimal QuantidadePadrao; }` · `ComponenteRoteiroPadrao { int Id; int ComponenteId; int SetorId; int Ordem; }` · `RastreamentoDbContext.FilhosPadrao`, `.MateriaisPadrao`, `.RoteirosPadrao`.

**PRÉ-REQUISITO:** `docker compose up -d`. Esta task roda contra o SQL Server **real** — é o que prova o Database First.

- [ ] **Step 1: Escreva o teste que falha**

Crie `tests/Rastreamento.Infrastructure.Tests/ReceitaPadraoMapeamentoTests.cs`. Siga o padrão de fixture já usado pelos outros testes de mapeamento deste projeto — abra um arquivo vizinho em `tests/Rastreamento.Infrastructure.Tests/` e copie a forma de obter o `RastreamentoDbContext` (connection string, `[Collection]`, limpeza). **Não invente uma fixture nova.**

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Tests;

/// <summary>
/// Mapeamento das 3 tabelas de receita padrao contra o SQL Server REAL. Banco em memoria nao
/// serviria: o que se prova aqui e que os nomes de coluna, a precisao do DECIMAL(18,4) e as FKs
/// batem com `specs/02-modelo-de-dados.sql` — a fonte de verdade do schema.
/// </summary>
public class ReceitaPadraoMapeamentoTests
{
  [Fact]
  public async Task Filho_padrao_grava_e_le_com_a_quantidade_intacta()
  {
    await using var db = NovoContexto();
    var (pai, filho) = await DoisComponentes(db);

    db.FilhosPadrao.Add(new ComponenteFilhoPadrao
    {
      ComponentePaiId = pai.Id,
      ComponenteFilhoId = filho.Id,
      QuantidadePadrao = 2.5m,
    });
    await db.SaveChangesAsync();

    var lida = await db.FilhosPadrao.AsNoTracking()
        .SingleAsync(f => f.ComponentePaiId == pai.Id);

    Assert.Equal(filho.Id, lida.ComponenteFilhoId);
    Assert.Equal(2.5m, lida.QuantidadePadrao);
  }

  /// <summary>
  /// A precisao NAO e detalhe: DECIMAL(18,4) tem 4 casas, e sem `HasPrecision(18, 4)` o EF assume
  /// o default dele e trunca em silencio. 0.0001 e o menor valor representavel — se voltar 0, o
  /// mapeamento esta errado.
  /// </summary>
  [Fact]
  public async Task Quantidade_preserva_as_quatro_casas_decimais()
  {
    await using var db = NovoContexto();
    var (pai, filho) = await DoisComponentes(db);

    db.FilhosPadrao.Add(new ComponenteFilhoPadrao
    {
      ComponentePaiId = pai.Id,
      ComponenteFilhoId = filho.Id,
      QuantidadePadrao = 0.0001m,
    });
    await db.SaveChangesAsync();

    var lida = await db.FilhosPadrao.AsNoTracking()
        .SingleAsync(f => f.ComponentePaiId == pai.Id);

    Assert.Equal(0.0001m, lida.QuantidadePadrao);
  }

  [Fact]
  public async Task Material_padrao_grava_e_le()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var material = await UmMaterial(db);

    db.MateriaisPadrao.Add(new ComponenteMaterialPadrao
    {
      ComponenteId = componente.Id,
      MaterialId = material.Id,
      QuantidadePadrao = 3m,
    });
    await db.SaveChangesAsync();

    var lida = await db.MateriaisPadrao.AsNoTracking()
        .SingleAsync(m => m.ComponenteId == componente.Id);

    Assert.Equal(material.Id, lida.MaterialId);
    Assert.Equal(3m, lida.QuantidadePadrao);
  }

  [Fact]
  public async Task Roteiro_padrao_grava_e_le_a_ordem()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var setor = await UmSetor(db);

    db.RoteirosPadrao.Add(new ComponenteRoteiroPadrao
    {
      ComponenteId = componente.Id,
      SetorId = setor.Id,
      Ordem = 1,
    });
    await db.SaveChangesAsync();

    var lida = await db.RoteirosPadrao.AsNoTracking()
        .SingleAsync(r => r.ComponenteId == componente.Id);

    Assert.Equal(setor.Id, lida.SetorId);
    Assert.Equal(1, lida.Ordem);
  }

  /// <summary>
  /// O MESMO setor duas vezes no roteiro e PERMITIDO — o UQ e (ComponenteId, Ordem), NAO
  /// (ComponenteId, SetorId). Isso e deliberado no schema e significa RETORNO AO SETOR (a peca
  /// volta a usinagem depois da solda). Este teste existe para que ninguem "corrija" isso.
  /// </summary>
  [Fact]
  public async Task Mesmo_setor_pode_repetir_no_roteiro_em_ordens_diferentes()
  {
    await using var db = NovoContexto();
    var componente = await UmComponente(db);
    var setor = await UmSetor(db);

    db.RoteirosPadrao.AddRange(
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setor.Id, Ordem = 1 },
        new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setor.Id, Ordem = 2 });

    // Nao lanca: o banco aceita, e a aplicacao nao pode inventar restricao que o schema nao tem.
    await db.SaveChangesAsync();

    var linhas = await db.RoteirosPadrao.AsNoTracking()
        .Where(r => r.ComponenteId == componente.Id).ToListAsync();

    Assert.Equal(2, linhas.Count);
    Assert.All(linhas, l => Assert.Equal(setor.Id, l.SetorId));
  }
}
```

Escreva os helpers `NovoContexto()`, `UmComponente(db)`, `UmMaterial(db)`, `UmSetor(db)` e `DoisComponentes(db)` **dentro deste arquivo**, criando linhas com sufixo único (ex.: `$"RP-{Guid.NewGuid():N}"[..12]`) para não colidir com `UQ_Componente_Codigo` entre execuções.

- [ ] **Step 2: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Esperado: **FALHA de compilação** — `ComponenteFilhoPadrao` não existe, `db.FilhosPadrao` não existe.

- [ ] **Step 3: Crie as 3 entidades**

`src/Rastreamento.Domain/Entities/ComponenteFilhoPadrao.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha da receita padrao de FILHOS: "o componente Pai leva N unidades do componente Filho".
/// Sem propriedade de navegacao de proposito — o resto do projeto tambem mapeia so as FKs, e
/// navegacao aqui convidaria a carregar o grafo inteiro por acidente.
/// </summary>
public class ComponenteFilhoPadrao
{
  public int Id { get; set; }
  public int ComponentePaiId { get; set; }
  public int ComponenteFilhoId { get; set; }
  public decimal QuantidadePadrao { get; set; }
}
```

`src/Rastreamento.Domain/Entities/ComponenteMaterialPadrao.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>Uma linha da receita padrao de MATERIAIS: "o Componente consome N do Material".</summary>
public class ComponenteMaterialPadrao
{
  public int Id { get; set; }
  public int ComponenteId { get; set; }
  public int MaterialId { get; set; }
  public decimal QuantidadePadrao { get; set; }
}
```

`src/Rastreamento.Domain/Entities/ComponenteRoteiroPadrao.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Um passo do roteiro padrao: "o Componente passa pelo Setor na posicao Ordem".
/// `Ordem` e 1-based e QUEM A ATRIBUI E O CASO DE USO, nunca o cliente — ver Task 4.
/// O mesmo Setor pode aparecer mais de uma vez (retorno ao setor): o UQ e (ComponenteId, Ordem).
/// </summary>
public class ComponenteRoteiroPadrao
{
  public int Id { get; set; }
  public int ComponenteId { get; set; }
  public int SetorId { get; set; }
  public int Ordem { get; set; }
}
```

- [ ] **Step 4: Crie as 3 configurations**

`src/Rastreamento.Infrastructure/Persistence/Configurations/ComponenteFilhoPadraoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteFilhoPadraoConfiguration : IEntityTypeConfiguration<ComponenteFilhoPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteFilhoPadrao> b)
  {
    b.ToTable("ComponenteFilhoPadrao");
    b.HasKey(x => x.Id);
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.QuantidadePadrao).HasPrecision(18, 4);
  }
}
```

`ComponenteMaterialPadraoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteMaterialPadraoConfiguration
    : IEntityTypeConfiguration<ComponenteMaterialPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteMaterialPadrao> b)
  {
    b.ToTable("ComponenteMaterialPadrao");
    b.HasKey(x => x.Id);
    b.Property(x => x.QuantidadePadrao).HasPrecision(18, 4);
  }
}
```

`ComponenteRoteiroPadraoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class ComponenteRoteiroPadraoConfiguration
    : IEntityTypeConfiguration<ComponenteRoteiroPadrao>
{
  public void Configure(EntityTypeBuilder<ComponenteRoteiroPadrao> b)
  {
    b.ToTable("ComponenteRoteiroPadrao");
    b.HasKey(x => x.Id);
    // Sem HasPrecision: Ordem e INT. Sem HasDefaultValue: Database First, default so no .sql.
  }
}
```

- [ ] **Step 5: Registre os 3 `DbSet`**

Em `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`, logo abaixo de `public DbSet<Componente> Componentes => Set<Componente>();`:

```csharp
  public DbSet<ComponenteFilhoPadrao> FilhosPadrao => Set<ComponenteFilhoPadrao>();
  public DbSet<ComponenteMaterialPadrao> MateriaisPadrao => Set<ComponenteMaterialPadrao>();
  public DbSet<ComponenteRoteiroPadrao> RoteirosPadrao => Set<ComponenteRoteiroPadrao>();
```

Não mexa em `OnModelCreating`: `ApplyConfigurationsFromAssembly` já pega as 3 configurations novas.

- [ ] **Step 6: Rode e veja passar**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Esperado: **38 + 5 = 43 aprovados**, 0 falhas.

- [ ] **Step 7: Prove por mutação que a precisão importa**

Comente `b.Property(x => x.QuantidadePadrao).HasPrecision(18, 4);` na `ComponenteFilhoPadraoConfiguration` e rode de novo. Esperado: `Quantidade_preserva_as_quatro_casas_decimais` **FALHA**. **Restaure a linha** e reconfirme 43 verdes.

Se o teste **passar** sem a linha, registre isso no relatório: significa que o default do EF já bate com `DECIMAL(18,4)` nesta versão, e o teste não discrimina — não invente que discriminou.

- [ ] **Step 8: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Domain/Entities src/Rastreamento.Infrastructure/Persistence tests/Rastreamento.Infrastructure.Tests/ReceitaPadraoMapeamentoTests.cs
git commit -m "feat(fase1c): entidades e mapeamento EF das 3 tabelas de receita padrao"
```

**Delta previsto: Infrastructure 38 → 43 (+5).**

---

## Task 2: Repositório com substituição atômica

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IReceitaPadraoRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/ReceitaPadraoRepository.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ReceitaPadraoRepositoryTests.cs`

**OBRIGATÓRIO, e o bloco de código do Step 1 abaixo NÃO mostra:** cada `[Fact]` deste arquivo **tem de** envolver suas asserções em `try/finally` e apagar, no `finally`, as linhas que criou (`Componente`, `Material`, `Setor` e as de receita). É o que os **sete** arquivos vizinhos de `Persistence/` fazem, sem exceção, e sem isso cada execução contra o banco de dev deixa linha órfã que se acumula sem limite. Abra `Persistence/ComponenteMappingTests.cs` e copie a forma. A Task 1 foi entregue sem isso e teve de ser corrigida em passe separado — não repita.

**Interfaces:**
- Consumes: as 3 entidades e os 3 `DbSet` da Task 1.
- Produces: `IReceitaPadraoRepository` com os métodos abaixo — as Tasks 3, 4 e 5 dependem destas assinaturas **exatas**.

- [ ] **Step 1: Escreva o teste que falha**

Crie `tests/Rastreamento.Infrastructure.Tests/ReceitaPadraoRepositoryTests.cs`, usando os mesmos helpers de fixture da Task 1 (copie-os para este arquivo ou extraia para um helper compartilhado — **decida uma vez e siga**).

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests;

public class ReceitaPadraoRepositoryTests
{
  /// <summary>
  /// A substituicao e o coracao do contrato: POST significa "a receita passa a ser EXATAMENTE
  /// estas linhas". As antigas somem, as novas entram, num SaveChanges so.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_apaga_as_linhas_antigas_e_grava_as_novas()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filhoA = await UmComponente(db);
    var filhoB = await UmComponente(db);

    await repo.SubstituirFilhosAsync(pai.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = pai.Id, ComponenteFilhoId = filhoA.Id, QuantidadePadrao = 1m,
      },
    ], CancellationToken.None);

    await repo.SubstituirFilhosAsync(pai.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = pai.Id, ComponenteFilhoId = filhoB.Id, QuantidadePadrao = 7m,
      },
    ], CancellationToken.None);

    var linhas = await repo.ListarFilhosAsync(pai.Id, CancellationToken.None);

    var unica = Assert.Single(linhas);
    Assert.Equal(filhoB.Id, unica.ComponenteFilhoId);
    Assert.Equal(7m, unica.QuantidadePadrao);
  }

  /// <summary>Lista vazia APAGA — e o unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Substituir_filhos_com_lista_vazia_apaga_tudo()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var pai = await UmComponente(db);
    var filho = await UmComponente(db);

    await repo.SubstituirFilhosAsync(pai.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = pai.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
      },
    ], CancellationToken.None);

    await repo.SubstituirFilhosAsync(pai.Id, [], CancellationToken.None);

    Assert.Empty(await repo.ListarFilhosAsync(pai.Id, CancellationToken.None));
  }

  /// <summary>
  /// A substituicao e ESCOPADA ao componente: mexer na receita de A nao pode tocar na de B.
  /// Sem o `Where(ComponentePaiId == id)` no delete, este teste morre.
  /// </summary>
  [Fact]
  public async Task Substituir_filhos_nao_toca_na_receita_de_outro_componente()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    await repo.SubstituirFilhosAsync(paiB.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = paiB.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 4m,
      },
    ], CancellationToken.None);

    await repo.SubstituirFilhosAsync(paiA.Id, [], CancellationToken.None);

    Assert.Single(await repo.ListarFilhosAsync(paiB.Id, CancellationToken.None));
  }

  /// <summary>O roteiro sai ORDENADO por Ordem — a tela depende disso para desenhar a sequencia.</summary>
  [Fact]
  public async Task Listar_roteiro_devolve_ordenado_por_ordem()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var componente = await UmComponente(db);
    var setorA = await UmSetor(db);
    var setorB = await UmSetor(db);

    // Inseridos FORA de ordem de proposito: se o repositorio nao ordenar, o teste pega.
    await repo.SubstituirRoteiroAsync(componente.Id, [
      new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorB.Id, Ordem = 2 },
      new ComponenteRoteiroPadrao { ComponenteId = componente.Id, SetorId = setorA.Id, Ordem = 1 },
    ], CancellationToken.None);

    var linhas = await repo.ListarRoteiroAsync(componente.Id, CancellationToken.None);

    Assert.Equal([setorA.Id, setorB.Id], linhas.Select(l => l.SetorId));
  }

  /// <summary>
  /// A deteccao de ciclo (Task 5) precisa do grafo INTEIRO, nao so das linhas de um componente.
  /// </summary>
  [Fact]
  public async Task Listar_todas_as_arestas_traz_linhas_de_componentes_diferentes()
  {
    await using var db = NovoContexto();
    var repo = new ReceitaPadraoRepository(db);
    var paiA = await UmComponente(db);
    var paiB = await UmComponente(db);
    var filho = await UmComponente(db);

    await repo.SubstituirFilhosAsync(paiA.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = paiA.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
      },
    ], CancellationToken.None);
    await repo.SubstituirFilhosAsync(paiB.Id, [
      new ComponenteFilhoPadrao
      {
        ComponentePaiId = paiB.Id, ComponenteFilhoId = filho.Id, QuantidadePadrao = 1m,
      },
    ], CancellationToken.None);

    var arestas = await repo.ListarTodasAsArestasAsync(CancellationToken.None);

    Assert.Contains(arestas, a => a.ComponentePaiId == paiA.Id);
    Assert.Contains(arestas, a => a.ComponentePaiId == paiB.Id);
  }
}
```

- [ ] **Step 2: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Esperado: **FALHA de compilação** — `ReceitaPadraoRepository` não existe.

- [ ] **Step 3: Crie a interface**

`src/Rastreamento.Domain/Abstractions/IReceitaPadraoRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Acesso as tres tabelas de receita padrao de um Componente.
///
/// UM repositorio para as tres, e nao tres, porque elas tem a MESMA forma de acesso — ler por
/// ComponenteId, substituir por ComponenteId — e porque a deteccao de ciclo precisa caminhar o
/// grafo de filhos por VARIOS componentes, nao so pelo da linha sendo gravada. Num repositorio
/// dedicado a uma tabela, esse caminhamento ficaria separado de onde a regra vive.
/// </summary>
public interface IReceitaPadraoRepository
{
  /// <summary>Null quando nao existe — o caso de uso traduz para 404.</summary>
  Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(int componenteId, CancellationToken ct);
  Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(int componenteId, CancellationToken ct);

  /// <summary>Ja vem ordenado por `Ordem`: a tela desenha a sequencia direto.</summary>
  Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(int componenteId, CancellationToken ct);

  /// <summary>
  /// Os Componentes referenciados pelos ids pedidos. Devolve SO os que existem — o caso de uso
  /// descobre os ausentes pela diferenca, e assim uma consulta responde "existe?" e "esta ativo?"
  /// de uma vez.
  /// </summary>
  Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// TODAS as arestas pai-filho do catalogo, para a deteccao de ciclo (Task 5).
  ///
  /// Fronteira declarada: isto le a tabela inteira. E aceitavel porque ComponenteFilhoPadrao e
  /// tabela de CATALOGO — cresce com o numero de pecas cadastradas, nao com a producao. Se um dia
  /// doer, a substituicao e um CTE recursivo no banco, nao um cache aqui.
  /// </summary>
  Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(CancellationToken ct);

  /// <summary>
  /// Apaga as linhas do componente e grava as novas num UNICO SaveChanges — ou seja, numa unica
  /// transacao implicita do EF. Meio-termo (apagou e nao gravou) nao e estado alcancavel.
  /// </summary>
  Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct);

  Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct);

  Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct);
}
```

- [ ] **Step 4: Implemente o repositório**

`src/Rastreamento.Infrastructure/Persistence/ReceitaPadraoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ReceitaPadraoRepository : IReceitaPadraoRepository
{
  private readonly RastreamentoDbContext _db;

  public ReceitaPadraoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
      _db.Componentes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(
      int componenteId, CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking()
          .Where(f => f.ComponentePaiId == componenteId)
          .OrderBy(f => f.Id)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(
      int componenteId, CancellationToken ct) =>
      await _db.MateriaisPadrao.AsNoTracking()
          .Where(m => m.ComponenteId == componenteId)
          .OrderBy(m => m.Id)
          .ToListAsync(ct);

  // OrderBy(Ordem) e contrato, nao conveniencia: a tela desenha a sequencia na ordem que chega.
  public async Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(
      int componenteId, CancellationToken ct) =>
      await _db.RoteirosPadrao.AsNoTracking()
          .Where(r => r.ComponenteId == componenteId)
          .OrderBy(r => r.Ordem)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Componentes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Materiais.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Setores.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(
      CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking().ToListAsync(ct);

  public Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct) =>
      Substituir(_db.FilhosPadrao, f => f.ComponentePaiId == componenteId, novas, ct);

  public Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct) =>
      Substituir(_db.MateriaisPadrao, m => m.ComponenteId == componenteId, novas, ct);

  public Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct) =>
      Substituir(_db.RoteirosPadrao, r => r.ComponenteId == componenteId, novas, ct);

  /// <summary>
  /// Remove + adiciona + UM SaveChanges. O EF envolve o SaveChanges numa transacao sozinho,
  /// entao "apagou e nao gravou" nao e estado alcancavel — e essa e a propriedade que o teste
  /// `Substituir_filhos_apaga_as_linhas_antigas_e_grava_as_novas` protege.
  ///
  /// Nao usa ExecuteDeleteAsync: ele emite um DELETE FORA da transacao do SaveChanges, o que
  /// reabriria exatamente o meio-termo que este desenho fecha.
  /// </summary>
  private async Task Substituir<T>(
      DbSet<T> tabela,
      System.Linq.Expressions.Expression<Func<T, bool>> doComponente,
      IReadOnlyList<T> novas,
      CancellationToken ct) where T : class
  {
    var antigas = await tabela.Where(doComponente).ToListAsync(ct);
    tabela.RemoveRange(antigas);
    tabela.AddRange(novas);
    await _db.SaveChangesAsync(ct);
  }
}
```

- [ ] **Step 5: Rode e veja passar**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Esperado: **43 + 5 = 48 aprovados**, 0 falhas.

- [ ] **Step 6: Prove por mutação que o escopo do delete importa**

Troque, no `Substituir`, `tabela.Where(doComponente)` por `tabela` (apagando **tudo**). Esperado: `Substituir_filhos_nao_toca_na_receita_de_outro_componente` **FALHA**. Reverta e reconfirme 48 verdes.

- [ ] **Step 7: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Domain/Abstractions src/Rastreamento.Infrastructure/Persistence/ReceitaPadraoRepository.cs tests/Rastreamento.Infrastructure.Tests/ReceitaPadraoRepositoryTests.cs
git commit -m "feat(fase1c): repositorio da receita padrao com substituicao atomica"
```

**Delta previsto: Infrastructure 43 → 48 (+5).**

**MEDIDO, e diferente do previsto: Infrastructure 43 → 57 (+14).** A task entregou os +5 previstos
(`fa7fa8c`), e a review dela achou que 6 dos 12 métodos do repositório não tinham teste nenhum —
defeito de dimensionamento **deste plano**, não do executor, que entregou exatamente os cinco testes
especificados. O fix pass (`33c0a65`) somou +9: cobertura dos métodos órfãos, atomicidade sob falha,
concorrência, e escopo do delete nas três tabelas (não só em filhos). As Tasks 3 a 6 abaixo herdam
**Infrastructure 57**, não 48.

---

## Task 3: Caso de uso — materiais-padrão (o molde)

Esta task estabelece o molde que as Tasks 4 e 5 copiam: validação, projeção, substituição. Materiais vem primeiro por ser o caso **sem** regra exclusiva (sem ordem, sem ciclo).

**Files:**
- Create: `src/Rastreamento.Application/Cadastros/ReceitaPadraoUseCase.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (acrescentar no fim)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/ReceitaPadraoUseCaseTests.cs`

**Interfaces:**
- Consumes: `IReceitaPadraoRepository` (Task 2), `Result<T>` / `TipoDeErro` (`Application/Common`).
- Produces:
  - `MaterialPadraoDto(int Id, int MaterialId, string Codigo, string Descricao, string UnidadeMedida, decimal QuantidadePadrao)`
  - `LinhaDeMaterialPadraoDto(int MaterialId, decimal QuantidadePadrao)`
  - `ReceitaDeMateriaisDto(IReadOnlyList<LinhaDeMaterialPadraoDto>? Linhas)`
  - `ReceitaPadraoUseCase.ListarMateriais(int, CancellationToken)` → `Result<IReadOnlyList<MaterialPadraoDto>>`
  - `ReceitaPadraoUseCase.SubstituirMateriais(int, IReadOnlyList<LinhaDeMaterialPadraoDto>, CancellationToken)` → `Result<IReadOnlyList<MaterialPadraoDto>>`

- [ ] **Step 1: Acrescente os DTOs**

No fim de `src/Rastreamento.Application/Cadastros/Dtos.cs`:

```csharp
// ---------------------------------------------------------------------------
// Receita padrao do Componente (Fase 1C)
// ---------------------------------------------------------------------------

/// <summary>Uma linha de material-padrao, como ela sai na leitura (ja com dados do Material).</summary>
public sealed record MaterialPadraoDto(
    int Id,
    int MaterialId,
    string Codigo,
    string Descricao,
    string UnidadeMedida,
    decimal QuantidadePadrao);

/// <summary>Uma linha de material-padrao, como ela ENTRA. So id + quantidade.</summary>
public sealed record LinhaDeMaterialPadraoDto(int MaterialId, decimal QuantidadePadrao);

/// <remarks>
/// `[Required]` num `IReadOnlyList<T>?` ANULAVEL, e nao numa lista nao-anulavel: o desserializador
/// entrega `null` para campo ausente mesmo em propriedade nao-anulavel, e sem o `[Required]` um
/// corpo `{}` vincularia `Linhas = null`. Como lista VAZIA significa "apague a receita" (§2.2 da
/// spec), tratar `null` como vazio faria `POST {}` LIMPAR a receita em silencio — a mesma classe
/// de bug que o `DefinirAtivoDto` ja pagou com `bool?` (ver o remarks dele acima). Com
/// `[Required]`, campo ausente vira 400 do proprio [ApiController] e `[]` continua sendo o
/// comando explicito de apagar.
///
/// Atributo SEM `[property:]`, no parametro do construtor primario — e onde a validacao de modelo
/// do MVC le em record posicional. Com `[property:]` o MVC lanca InvalidOperationException e a
/// requisicao vira 500.
/// </remarks>
public sealed record ReceitaDeMateriaisDto([Required] IReadOnlyList<LinhaDeMaterialPadraoDto>? Linhas);
```

- [ ] **Step 2: Escreva os testes que falham**

Crie `tests/Rastreamento.Application.Tests/Cadastros/ReceitaPadraoUseCaseTests.cs` (a pasta `Cadastros/` e o fake em `Cadastros/Fakes.cs` sao o padrao ja estabelecido pelos vizinhos — o plano original dizia a raiz do projeto de teste, como ja acontecera na Task 1). Antes de escrever o fake, **abra um arquivo de teste vizinho em `tests/Rastreamento.Application.Tests/`** e confira o estilo de fake usado lá; se houver um padrão estabelecido, siga-o em vez do esqueleto abaixo.

```csharp
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests;

/// <summary>
/// Fake em memoria do repositorio de receita. Sem banco: o que se prova aqui e REGRA, e regra que
/// so passa com banco no ar e regra que ninguem roda.
/// </summary>
internal sealed class ReceitaPadraoRepositorioFake : IReceitaPadraoRepository
{
  public List<Componente> Componentes { get; } = [];
  public List<Material> Materiais { get; } = [];
  public List<Setor> Setores { get; } = [];
  public List<ComponenteFilhoPadrao> Filhos { get; } = [];
  public List<ComponenteMaterialPadrao> MateriaisPadrao { get; } = [];
  public List<ComponenteRoteiroPadrao> Roteiro { get; } = [];

  /// <summary>Quantas vezes uma substituicao chegou ao "banco". Prova que NAO gravou em falha.</summary>
  public int Substituicoes { get; private set; }

  public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
      Task.FromResult(Componentes.SingleOrDefault(c => c.Id == id));

  public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(int componenteId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ComponenteFilhoPadrao>>(
          Filhos.Where(f => f.ComponentePaiId == componenteId).ToList());

  public Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(int componenteId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ComponenteMaterialPadrao>>(
          MateriaisPadrao.Where(m => m.ComponenteId == componenteId).ToList());

  public Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(int componenteId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ComponenteRoteiroPadrao>>(
          Roteiro.Where(r => r.ComponenteId == componenteId).OrderBy(r => r.Ordem).ToList());

  public Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Componente>>(Componentes.Where(c => ids.Contains(c.Id)).ToList());

  public Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Material>>(Materiais.Where(m => ids.Contains(m.Id)).ToList());

  public Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Setor>>(Setores.Where(s => ids.Contains(s.Id)).ToList());

  public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ComponenteFilhoPadrao>>(Filhos.ToList());

  public Task SubstituirFilhosAsync(int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct)
  {
    Substituicoes++;
    Filhos.RemoveAll(f => f.ComponentePaiId == componenteId);
    Filhos.AddRange(novas);
    return Task.CompletedTask;
  }

  public Task SubstituirMateriaisAsync(int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct)
  {
    Substituicoes++;
    MateriaisPadrao.RemoveAll(m => m.ComponenteId == componenteId);
    MateriaisPadrao.AddRange(novas);
    return Task.CompletedTask;
  }

  public Task SubstituirRoteiroAsync(int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct)
  {
    Substituicoes++;
    Roteiro.RemoveAll(r => r.ComponenteId == componenteId);
    Roteiro.AddRange(novas);
    return Task.CompletedTask;
  }
}

public class ReceitaPadraoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>Monta um fake com 1 componente (id 1), 2 materiais (10, 11) e 2 setores (20, 21).</summary>
  private static ReceitaPadraoRepositorioFake FakeComCatalogo()
  {
    var f = new ReceitaPadraoRepositorioFake();
    f.Componentes.Add(new Componente { Id = 1, Codigo = "PAI", Descricao = "Pai", Tipo = "Montagem", Ativo = true });
    f.Materiais.Add(new Material { Id = 10, Codigo = "CH-3", Descricao = "Chapa 3mm", UnidadeMedida = "KG", Ativo = true });
    f.Materiais.Add(new Material { Id = 11, Codigo = "PA-1", Descricao = "Parafuso", UnidadeMedida = "UN", Ativo = true });
    f.Setores.Add(new Setor { Id = 20, Nome = "Corte", Ativo = true });
    f.Setores.Add(new Setor { Id = 21, Nome = "Solda", Ativo = true });
    return f;
  }

  [Fact]
  public async Task Substituir_materiais_grava_e_projeta_os_dados_do_material()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 2.5m)], Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    Assert.Equal(10, linha.MaterialId);
    Assert.Equal("CH-3", linha.Codigo);
    Assert.Equal("Chapa 3mm", linha.Descricao);
    Assert.Equal("KG", linha.UnidadeMedida);
    Assert.Equal(2.5m, linha.QuantidadePadrao);
  }

  /// <summary>Lista vazia APAGA — unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Substituir_materiais_com_lista_vazia_apaga_a_receita()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    var r = await caso.SubstituirMateriais(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
    Assert.Empty(fake.MateriaisPadrao);
  }

  [Fact]
  public async Task Componente_inexistente_e_nao_encontrado()
  {
    var caso = new ReceitaPadraoUseCase(FakeComCatalogo());

    var r = await caso.SubstituirMateriais(999, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public async Task Quantidade_nao_positiva_e_recusada(decimal quantidade)
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, quantidade)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    // NAO gravou: recusa que grava metade e pior que recusa nenhuma.
    Assert.Equal(0, fake.Substituicoes);
  }

  [Fact]
  public async Task Material_repetido_na_mesma_lista_e_recusado()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1, [new LinhaDeMaterialPadraoDto(10, 1m), new LinhaDeMaterialPadraoDto(10, 2m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>A mensagem nomeia QUAL id nao existe — senao o usuario adivinha qual linha corrigir.</summary>
  [Fact]
  public async Task Material_inexistente_e_recusado_nomeando_o_id()
  {
    var caso = new ReceitaPadraoUseCase(FakeComCatalogo());

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(777, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Contains("777", r.Erro);
  }

  [Fact]
  public async Task Material_inativo_nao_pode_entrar_na_receita()
  {
    var fake = FakeComCatalogo();
    fake.Materiais.Single(m => m.Id == 11).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(11, 1m)], Ct);

    Assert.False(r.Sucesso);
    // Mensagem INTEIRA, nao substring: "11" casaria com "110", "211" etc. Se a mensagem mudar de
    // texto, este teste tem de morrer — e a mensagem E o contrato com o usuario aqui.
    Assert.Equal("O material 11 esta inativo e nao pode entrar na receita.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Inativar um item DEPOIS nao pode corromper receita ja gravada — catalogo se inativa, nao se
  /// exclui, e a leitura tem que continuar mostrando o que esta la.
  /// </summary>
  [Fact]
  public async Task Linha_existente_sobrevive_a_inativacao_do_material()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    fake.Materiais.Single(m => m.Id == 10).Ativo = false;

    var r = await caso.ListarMateriais(1, Ct);

    Assert.True(r.Sucesso);
    Assert.Single(r.Valor!);
  }

  [Fact]
  public async Task Listar_materiais_de_componente_inexistente_e_nao_encontrado()
  {
    var caso = new ReceitaPadraoUseCase(FakeComCatalogo());

    var r = await caso.ListarMateriais(999, Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }
}
```

- [ ] **Step 3: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **FALHA de compilação** — `ReceitaPadraoUseCase` não existe.

- [ ] **Step 4: Implemente o caso de uso (parte de materiais)**

`src/Rastreamento.Application/Cadastros/ReceitaPadraoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Os tres sub-recursos vivem no MESMO caso de uso porque compartilham quatro das cinco
/// validacoes (componente pai existe, ids existem, ids estao ativos, substituicao e atomica) —
/// so o ciclo e exclusivo dos filhos. Tres casos de uso duplicariam essas quatro ou exigiriam um
/// helper compartilhado que teria a mesma forma deste arquivo, com um nivel a mais de indirecao.
///
/// Toda gravacao SUBSTITUI a receita inteira: "a receita deste componente passa a ser EXATAMENTE
/// estas N linhas". Lista vazia apaga — e o unico caminho de remocao que existe.
/// </summary>
public sealed class ReceitaPadraoUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  private readonly IReceitaPadraoRepository _repositorio;

  public ReceitaPadraoUseCase(IReceitaPadraoRepository repositorio) => _repositorio = repositorio;

  // ------------------------------------------------------------------ materiais

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> ListarMateriais(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> SubstituirMateriais(
      int componenteId, IReadOnlyList<LinhaDeMaterialPadraoDto> linhas, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    var ids = linhas.Select(l => l.MaterialId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          $"O material {repetido} aparece mais de uma vez na lista.");

    var materiais = await _repositorio.ObterMateriaisPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, materiais.ToDictionary(m => m.Id, m => m.Ativo), "material", "materiais");
    if (problema is not null) return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(problema);

    await _repositorio.SubstituirMateriaisAsync(componenteId, linhas.Select(l =>
        new ComponenteMaterialPadrao
        {
          ComponenteId = componenteId,
          MaterialId = l.MaterialId,
          QuantidadePadrao = l.QuantidadePadrao,
        }).ToList(), ct);

    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  private async Task<IReadOnlyList<MaterialPadraoDto>> ProjetarMateriais(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarMateriaisAsync(componenteId, ct);
    var materiais = (await _repositorio.ObterMateriaisPorIdAsync(
        linhas.Select(l => l.MaterialId).Distinct().ToList(), ct)).ToDictionary(m => m.Id);

    // Sem filtro por Ativo: linha ja gravada SOBREVIVE a inativacao do material. Inativar catalogo
    // nao pode corromper receita que ja existe.
    return linhas.Select(l =>
    {
      var m = materiais[l.MaterialId];
      return new MaterialPadraoDto(
          l.Id, l.MaterialId, m.Codigo, m.Descricao, m.UnidadeMedida, l.QuantidadePadrao);
    }).ToList();
  }

  // ------------------------------------------------------------------ comum

  /// <summary>O primeiro id que aparece duas vezes, ou null. Ordem estavel: a lista dita.</summary>
  private static int? PrimeiroRepetido(IReadOnlyList<int> ids)
  {
    var vistos = new HashSet<int>();
    foreach (var id in ids)
      if (!vistos.Add(id)) return id;
    return null;
  }

  /// <summary>
  /// Uma mensagem para "nao existe" e outra para "esta inativo", NOMEANDO os ids — sem o id, o
  /// usuario com 12 linhas na tela nao sabe qual corrigir.
  /// </summary>
  private static string? ConferirExistenciaEAtividade(
      IReadOnlyList<int> idsPedidos,
      IReadOnlyDictionary<int, bool> ativoPorId,
      string singular,
      string plural)
  {
    var ausentes = idsPedidos.Where(id => !ativoPorId.ContainsKey(id)).Distinct().ToList();
    if (ausentes.Count > 0)
      return ausentes.Count == 1
          ? $"O {singular} {ausentes[0]} nao existe."
          : $"Os {plural} {string.Join(", ", ausentes)} nao existem.";

    var inativos = idsPedidos.Where(id => !ativoPorId[id]).Distinct().ToList();
    if (inativos.Count > 0)
      return inativos.Count == 1
          ? $"O {singular} {inativos[0]} esta inativo e nao pode entrar na receita."
          : $"Os {plural} {string.Join(", ", inativos)} estao inativos e nao podem entrar na receita.";

    return null;
  }
}
```

- [ ] **Step 5: Rode e veja passar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **129 + 10 = 139 aprovados** (o `[Theory]` de quantidade conta 2), 0 falhas.

> **MEDIDO na execução: 142 (+13), não 139 (+10).** Três testes a mais do que o esqueleto acima,
> todos porque a prova por mutação do Step 6 mostrou guarda faltando — ver
> `.superpowers/sdd/task-3-report.md`:
> `Receita_de_um_componente_nao_vaza_para_outro` (sem ela, `componenteId` trocado por `1` literal
> sobrevive em três lugares — é o achado B11 da Fase 1A repetido), e os dois ramos **plurais** de
> `ConferirExistenciaEAtividade`, que é helper **compartilhado** pelas Tasks 4 e 5.
>
> **E depois o fix pass da review: Application 142 → 150 (+8) e Infrastructure 57 → 58 (+1)** — ver
> `.superpowers/sdd/task-3-fix-report.md`. Entraram a substituição bem-sucedida com 2 linhas
> (afirmando a projeção inteira **em ordem**, que é o que faz a Task 4 conseguir provar `Ordem`), o
> `[Theory]` de precedência das validações, dois casos de escala de `DECIMAL(18,4)`, a propagação do
> `ct`, a tradução de conflito de concorrência para 409 e o teste dela contra o SQL Server. O
> `[Theory]` de quantidade mudou de nome para `Quantidade_invalida_e_recusada` e a mutação do Step 6
> passa a citá-lo. **As previsões das Tasks 4, 5 e 6 abaixo já estão corrigidas para esta base
> (150 / 58 / 171).**

- [ ] **Step 6: Prove por mutação que a recusa não grava**

Mova o `await _repositorio.SubstituirMateriaisAsync(...)` para **antes** da validação de quantidade. Esperado: `Quantidade_invalida_e_recusada` **FALHA** (`Substituicoes` vira 1). Reverta e reconfirme 142 — 150 depois do fix pass.

- [ ] **Step 7: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Application/Cadastros tests/Rastreamento.Application.Tests/Cadastros
git commit -m "feat(fase1c): caso de uso da receita padrao — materiais"
```

**Delta previsto: Application 129 → 139 (+10). MEDIDO: 129 → 142 (+13).**

---

## Task 4: Caso de uso — roteiro-padrão (numeração no servidor)

**Files:**
- Modify: `src/Rastreamento.Application/Cadastros/ReceitaPadraoUseCase.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/ReceitaPadraoUseCaseTests.cs`

**Interfaces:**
- Consumes: `ReceitaPadraoUseCase` e os helpers `PrimeiroRepetido` / `ConferirExistenciaEAtividade` (Task 3).
- Produces:
  - `RoteiroPadraoDto(int Id, int SetorId, string Nome, int Ordem)`
  - `LinhaDeRoteiroPadraoDto(int SetorId)`
  - `ReceitaDeRoteiroDto(IReadOnlyList<LinhaDeRoteiroPadraoDto>? Linhas)`
  - `ReceitaPadraoUseCase.ListarRoteiro` / `.SubstituirRoteiro`

**ATENÇÃO — a regra que mais se erra nesta task:** `Ordem` **nunca** vem do cliente. O servidor numera `1..N` pela ordem do array recebido. E **setor repetido é PERMITIDO** — o `UQ` é `(ComponenteId, Ordem)`, não `(ComponenteId, SetorId)`.

- [ ] **Step 1: Acrescente os DTOs**

No fim de `src/Rastreamento.Application/Cadastros/Dtos.cs`:

```csharp
/// <summary>Um passo do roteiro, como ele sai na leitura (com o nome do Setor).</summary>
public sealed record RoteiroPadraoDto(int Id, int SetorId, string Nome, int Ordem);

/// <remarks>
/// SO `SetorId`, sem `Ordem`: a ordem e a POSICAO no array, e quem a atribui e o caso de uso.
/// Aceitar `Ordem` do cliente reabriria buraco e duplicata na sequencia — que virariam violacao
/// do UQ_ComponenteRoteiroPadrao e sairiam como erro de banco, nao como 400 legivel.
/// </remarks>
public sealed record LinhaDeRoteiroPadraoDto(int SetorId);

/// <remarks>`[Required]` pelo mesmo motivo do `ReceitaDeMateriaisDto` — ver o remarks dele.</remarks>
public sealed record ReceitaDeRoteiroDto([Required] IReadOnlyList<LinhaDeRoteiroPadraoDto>? Linhas);
```

- [ ] **Step 2: Escreva os testes que falham**

Acrescente ao fim do `describe` de `ReceitaPadraoUseCaseTests` (dentro da classe):

```csharp
  [Fact]
  public async Task Roteiro_numera_a_ordem_de_um_ate_n_pela_posicao_do_array()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)], Ct);

    Assert.True(r.Sucesso);
    // A ordem sai da POSICAO, nao do id: 21 veio primeiro, entao 21 e a Ordem 1.
    Assert.Equal([(21, 1), (20, 2)], r.Valor!.Select(l => (l.SetorId, l.Ordem)));
  }

  /// <summary>
  /// O MESMO setor duas vezes e PERMITIDO: significa RETORNO AO SETOR (a peca volta a usinagem
  /// depois da solda). Este teste prova o PERMITIDO, nao o proibido — sem ele, alguem "corrige"
  /// acrescentando validacao de setor unico, a suite fica verde, e o retorno ao setor some.
  /// </summary>
  [Fact]
  public async Task Mesmo_setor_repetido_no_roteiro_e_aceito()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1,
        [new LinhaDeRoteiroPadraoDto(20), new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)],
        Ct);

    Assert.True(r.Sucesso);
    Assert.Equal([(20, 1), (21, 2), (20, 3)], r.Valor!.Select(l => (l.SetorId, l.Ordem)));
  }

  [Fact]
  public async Task Roteiro_com_lista_vazia_apaga()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(20)], Ct);

    var r = await caso.SubstituirRoteiro(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
  }

  [Fact]
  public async Task Setor_inexistente_no_roteiro_e_recusado_nomeando_o_id()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(888)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    // Mensagem INTEIRA, nao substring: "888" casaria com "8888" e "18880".
    Assert.Equal("O setor 888 nao existe.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Extensao deliberada da spec §2.3, que so nomeia componente-filho e material: Setor tem
  /// `Ativo` e e a mesma classe de problema. Ver "Global Constraints" do plano.
  /// </summary>
  [Fact]
  public async Task Setor_inativo_nao_pode_entrar_no_roteiro()
  {
    var fake = FakeComCatalogo();
    fake.Setores.Single(s => s.Id == 21).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(21)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("O setor 21 esta inativo e nao pode entrar na receita.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  [Fact]
  public async Task Listar_roteiro_projeta_a_sequencia_inteira_com_o_nome_do_setor()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)], Ct);

    var r = await caso.ListarRoteiro(1, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(
        [(500, 21, "Solda", 1), (501, 20, "Corte", 2)],
        r.Valor!.Select(l => (l.Id, l.SetorId, l.Nome, l.Ordem)));
  }
```

- [ ] **Step 3: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **FALHA de compilação** — `SubstituirRoteiro` não existe.

- [ ] **Step 4: Implemente a parte de roteiro**

Acrescente em `ReceitaPadraoUseCase`, entre a seção de materiais e a de "comum":

```csharp
  // ------------------------------------------------------------------ roteiro

  public async Task<Result<IReadOnlyList<RoteiroPadraoDto>>> ListarRoteiro(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<RoteiroPadraoDto>>.Ok(await ProjetarRoteiro(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<RoteiroPadraoDto>>> SubstituirRoteiro(
      int componenteId, IReadOnlyList<LinhaDeRoteiroPadraoDto> linhas, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    // SEM checagem de repetido, de proposito: o mesmo Setor pode aparecer varias vezes — e o
    // RETORNO AO SETOR, permitido pelo schema (UQ e (ComponenteId, Ordem)). Nao "conserte" isto.
    var ids = linhas.Select(l => l.SetorId).ToList();
    var setores = await _repositorio.ObterSetoresPorIdAsync(ids.Distinct().ToList(), ct);
    var problema = ConferirExistenciaEAtividade(
        ids, setores.ToDictionary(s => s.Id, s => s.Ativo), "setor", "setores");
    if (problema is not null) return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha(problema);

    // A Ordem sai da POSICAO no array: 1-based, densa por construcao. Nao ha como o cliente
    // produzir buraco nem duplicata na sequencia.
    await _repositorio.SubstituirRoteiroAsync(componenteId, linhas.Select((l, i) =>
        new ComponenteRoteiroPadrao
        {
          ComponenteId = componenteId,
          SetorId = l.SetorId,
          Ordem = i + 1,
        }).ToList(), ct);

    return Result<IReadOnlyList<RoteiroPadraoDto>>.Ok(await ProjetarRoteiro(componenteId, ct));
  }

  private async Task<IReadOnlyList<RoteiroPadraoDto>> ProjetarRoteiro(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarRoteiroAsync(componenteId, ct);
    var setores = (await _repositorio.ObterSetoresPorIdAsync(
        linhas.Select(l => l.SetorId).Distinct().ToList(), ct)).ToDictionary(s => s.Id);

    return linhas
        .Select(l => new RoteiroPadraoDto(l.Id, l.SetorId, setores[l.SetorId].Nome, l.Ordem))
        .ToList();
  }
```

- [ ] **Step 5: Rode e veja passar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **160 aprovados**, 0 falhas — MEDIDO. (A previsao original era 156; ver o delta no fim do Step 7.)

- [ ] **Step 6: As DUAS mutações desta task**

**Mutação A (numeração):** troque `Ordem = i + 1` por `Ordem = i`. Esperado: `Roteiro_numera_a_ordem_de_um_ate_n_pela_posicao_do_array` e `Mesmo_setor_repetido_no_roteiro_e_aceito` **FALHAM**. Reverta.

**Mutação B (a que prova o permitido):** acrescente, antes da gravação do roteiro, uma validação de setor único:

```csharp
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<RoteiroPadraoDto>>.Falha($"Setor {repetido} repetido.");
```

Esperado: `Mesmo_setor_repetido_no_roteiro_e_aceito` **FALHA**. **Isto é o ponto da task** — se ela ficar verde, o teste do permitido não discrimina e precisa ser reescrito. Reverta e reconfirme 160.

- [ ] **Step 7: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Application/Cadastros tests/Rastreamento.Application.Tests/Cadastros
git commit -m "feat(fase1c): caso de uso da receita padrao — roteiro, com ordem atribuida pelo servidor"
```

**Delta MEDIDO: Application 150 → 160 (+10).** A previsao era +6; os 4 testes a mais sao o 409 do roteiro (que o Step 4 do plano nao pedia), o vazamento entre componentes, o 404 nos dois metodos e o id inexistente repetido — ver `.superpowers/sdd/task-4-report.md`.

**Fix pass da review da Task 4 (I1, I2, I3, N1, N2, N4, N5 — mais P1/P2/P3 no plano da Task 5, abaixo):
Application 160 → 161 (+1).** I1, I2 e I3 sao edicoes de token em testes ja existentes (+0). O +1 e
`Falha_na_releitura_apos_gravar_o_roteiro_nao_vira_conflito` (N1): prova que o `try` de
`SubstituirRoteiro` e ESTREITO — envolve so a gravacao, nao a releitura — fazendo o fake lancar de
`ListarRoteiroAsync` e afirmando que a excecao sobe crua, sem virar `TipoDeErro.Conflito`. Ver
`.superpowers/sdd/task-4-fix-report.md`. **As previsoes das Tasks 5, 6 e 7 abaixo ja estao corrigidas
para esta base (161).**

---

## Task 5: Caso de uso — filhos-padrão e a detecção de ciclo

A task mais delicada da fase. **Leia §1.3 da spec antes de começar.**

**Files:**
- Modify: `src/Rastreamento.Application/Cadastros/ReceitaPadraoUseCase.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs`
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/ReceitaPadraoUseCaseTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 3 e 4.
- Produces:
  - `FilhoPadraoDto(int Id, int ComponenteFilhoId, string Codigo, string Descricao, decimal QuantidadePadrao)`
  - `LinhaDeFilhoPadraoDto(int ComponenteFilhoId, decimal QuantidadePadrao)`
  - `ReceitaDeFilhosDto(IReadOnlyList<LinhaDeFilhoPadraoDto>? Linhas)`
  - `ReceitaPadraoUseCase.ListarFilhos` / `.SubstituirFilhos`

**A regra, por escrito:** a verificação é sobre o grafo **como ele ficaria depois da substituição** — arestas atuais **menos** as do componente sendo editado, **mais** as linhas novas. Validar contra o grafo **atual** deixaria o usuário preso: consertar um ciclo preexistente seria recusado, e a única saída seria SQL na mão.

- [ ] **Step 1: Acrescente os DTOs**

No fim de `src/Rastreamento.Application/Cadastros/Dtos.cs`:

```csharp
/// <summary>Uma linha de filho-padrao, como ela sai na leitura (com dados do Componente filho).</summary>
public sealed record FilhoPadraoDto(
    int Id, int ComponenteFilhoId, string Codigo, string Descricao, decimal QuantidadePadrao);

/// <remarks>
/// Sem `ComponentePaiId`: ele vem da rota (`POST /componentes/{id}/filhos-padrao`), nao do corpo —
/// assim nao existe a possibilidade de os dois discordarem. Mesmo motivo do `NovoAgrupamentoDto`.
/// </remarks>
public sealed record LinhaDeFilhoPadraoDto(int ComponenteFilhoId, decimal QuantidadePadrao);

/// <remarks>`[Required]` pelo mesmo motivo do `ReceitaDeMateriaisDto` — ver o remarks dele.</remarks>
public sealed record ReceitaDeFilhosDto([Required] IReadOnlyList<LinhaDeFilhoPadraoDto>? Linhas);
```

- [ ] **Step 2: Escreva os testes que falham**

Acrescente à classe `ReceitaPadraoUseCaseTests`. Note o helper novo, que dá ao fake um catálogo com **quatro** componentes para montar cadeias:

```csharp
  /// <summary>Fake com componentes 1..4 ativos, para montar cadeias de filhos.</summary>
  private static ReceitaPadraoRepositorioFake FakeComQuatroComponentes()
  {
    var f = FakeComCatalogo();
    foreach (var id in new[] { 2, 3, 4 })
      f.Componentes.Add(new Componente
      {
        Id = id, Codigo = $"C{id}", Descricao = $"Componente {id}", Tipo = "Fabricado", Ativo = true,
      });
    return f;
  }

  [Fact]
  public async Task Substituir_filhos_grava_e_projeta_os_dados_do_componente_filho()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 3m)], Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    Assert.Equal(2, linha.ComponenteFilhoId);
    Assert.Equal("C2", linha.Codigo);
    Assert.Equal(3m, linha.QuantidadePadrao);
    // GRAVOU, e uma vez so — mesmo formato de `Substituir_materiais_grava_e_projeta_os_dados_do_material`.
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  [Fact]
  public async Task Auto_referencia_e_recusada_com_mensagem_propria()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(1, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// O CK do banco so pega A -> A. Este e A -> B -> A, que o banco ACEITA e que faria a copia
  /// recursiva da Fase 2 girar para sempre.
  /// </summary>
  [Fact]
  public async Task Ciclo_de_dois_niveis_e_recusado()
  {
    var fake = FakeComQuatroComponentes();
    // Ja existe: 2 -> 1.  Agora tentamos 1 -> 2, que fecharia 1 -> 2 -> 1.
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 100, ComponentePaiId = 2, ComponenteFilhoId = 1, QuantidadePadrao = 1m,
    });
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>Profundidade 3: 1 -> 2 -> 3 -> 1. Prova que a busca nao para no primeiro nivel.</summary>
  [Fact]
  public async Task Ciclo_de_tres_niveis_e_recusado()
  {
    var fake = FakeComQuatroComponentes();
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 100, ComponentePaiId = 2, ComponenteFilhoId = 3, QuantidadePadrao = 1m,
    });
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 101, ComponentePaiId = 3, ComponenteFilhoId = 1, QuantidadePadrao = 1m,
    });
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Diamante: 1 -> 2, 1 -> 3, 2 -> 4, 3 -> 4. O componente 4 e alcancavel por DOIS caminhos, e
  /// isso NAO e ciclo. Se a busca confundir "ja visitei" com "e ciclo", este teste pega.
  ///
  /// A projecao inteira e afirmada como tupla EM ORDEM, e nao so a contagem (mesmo formato de
  /// `Substituir_materiais_com_duas_linhas_projeta_as_duas_em_ordem`): `Assert.Equal(2,
  /// r.Valor!.Count)` nao discrimina `Take(1)` na montagem, projecao invertida nem
  /// `componentes.Values.First()` ligando toda linha ao mesmo componente filho.
  /// </summary>
  [Fact]
  public async Task Grafo_em_diamante_nao_e_ciclo()
  {
    var fake = FakeComQuatroComponentes();
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 100, ComponentePaiId = 2, ComponenteFilhoId = 4, QuantidadePadrao = 1m,
    });
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 101, ComponentePaiId = 3, ComponenteFilhoId = 4, QuantidadePadrao = 1m,
    });
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(2, 1m), new LinhaDeFilhoPadraoDto(3, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(
        [(500, 2, "C2", "Componente 2", 1m), (501, 3, "C3", "Componente 3", 1m)],
        r.Valor!.Select(l => (l.Id, l.ComponenteFilhoId, l.Codigo, l.Descricao, l.QuantidadePadrao)));
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// O TESTE QUE DEFINE A REGRA (§1.3 da spec). O grafo ja tem o ciclo 1 -> 2 -> 1. O usuario
  /// manda a receita de 1 SEM o filho 2 — ou seja, ele esta CONSERTANDO o ciclo. Isso tem que ser
  /// ACEITO: validar contra o grafo ATUAL o deixaria preso, sem saida pela API.
  /// </summary>
  [Fact]
  public async Task Substituicao_que_desfaz_um_ciclo_preexistente_e_aceita()
  {
    var fake = FakeComQuatroComponentes();
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 100, ComponentePaiId = 1, ComponenteFilhoId = 2, QuantidadePadrao = 1m,
    });
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 101, ComponentePaiId = 2, ComponenteFilhoId = 1, QuantidadePadrao = 1m,
    });
    var caso = new ReceitaPadraoUseCase(fake);

    // A receita de 1 passa a ser so o filho 3 — a aresta 1 -> 2 SOME, e o ciclo com ela.
    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(3, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3, Assert.Single(r.Valor!).ComponenteFilhoId);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Um ciclo preexistente em OUTRA parte do grafo (3 -> 4 -> 3) nao pode nem travar a edicao de
  /// 1, nem fazer a busca girar para sempre. Se este teste PENDURAR, falta o conjunto de
  /// visitados na travessia.
  /// </summary>
  [Fact]
  public async Task Ciclo_preexistente_em_outro_ramo_nao_impede_editar_este_componente()
  {
    var fake = FakeComQuatroComponentes();
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 100, ComponentePaiId = 3, ComponenteFilhoId = 4, QuantidadePadrao = 1m,
    });
    fake.Filhos.Add(new ComponenteFilhoPadrao
    {
      Id = 101, ComponentePaiId = 4, ComponenteFilhoId = 3, QuantidadePadrao = 1m,
    });
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  [Fact]
  public async Task Filho_repetido_na_mesma_lista_e_recusado()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(2, 1m), new LinhaDeFilhoPadraoDto(2, 5m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(0, fake.Substituicoes);
  }

  [Fact]
  public async Task Componente_filho_inativo_nao_pode_entrar_na_receita()
  {
    var fake = FakeComQuatroComponentes();
    fake.Componentes.Single(c => c.Id == 2).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    // Mensagem INTEIRA, nao `Assert.Contains("2", ...)`: a substring "2" casa com "12", "20" e ate
    // com o proprio texto da mensagem. E o mesmo defeito que a review da Task 11 da Fase 1D achou
    // (`toContain('1')` casando com "41") — nao repetir.
    Assert.Equal("O componente 2 esta inativo e nao pode entrar na receita.", r.Erro);
  }

  [Fact]
  public async Task Filhos_com_lista_vazia_apaga()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    var r = await caso.SubstituirFilhos(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
    // Duas gravacoes, nao tres: a do arranjo e a que apaga.
    Assert.Equal(2, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// OUTRO teste do PERMITIDO (§1.4 da spec): o `Tipo` do Componente NAO restringe a receita.
  /// Um `Bruto` pode ter filhos e uma `Montagem` pode nao ter nenhum — o tipo e rotulo descritivo.
  /// Sem este teste, alguem acrescenta "so Montagem tem filhos" achando que corrige o modelo, a
  /// suite fica verde, e uma regra que o usuario DESCARTOU explicitamente entra pela porta dos
  /// fundos.
  /// </summary>
  [Fact]
  public async Task Componente_do_tipo_Bruto_pode_ter_filhos()
  {
    var fake = FakeComQuatroComponentes();
    fake.Componentes.Single(c => c.Id == 1).Tipo = "Bruto";
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Single(r.Valor!);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Mesma traducao do 409 dos materiais e do roteiro, agora em filhos. Exige uma mudanca em
  /// `Fakes.cs`: `SubstituirFilhosAsync` ainda NAO honra `ConflitoNaProximaSubstituicao` — os
  /// outros dois `Substituir*Async` ja tem `if (ConflitoNaProximaSubstituicao) throw new
  /// ConflitoDeConcorrenciaException(new Exception("simulado"));` logo no INICIO do metodo (depois
  /// de incrementar o contador e anotar o `ct`); acrescente a mesma linha, no mesmo lugar, em
  /// `SubstituirFilhosAsync`.
  /// </summary>
  [Fact]
  public async Task Conflito_de_concorrencia_na_gravacao_dos_filhos_vira_erro_de_conflito()
  {
    var fake = FakeComQuatroComponentes();
    fake.ConflitoNaProximaSubstituicao = true;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(
        "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.", r.Erro);
  }
```

- [ ] **Step 3: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **FALHA de compilação** — `SubstituirFilhos` não existe.

- [ ] **Step 4: Implemente a parte de filhos**

Acrescente em `ReceitaPadraoUseCase`, antes da seção "comum". As duas constantes novas vão junto das outras, no topo da classe:

```csharp
  private const string ErroDeAutoReferencia =
      "Um componente nao pode ser filho de si mesmo.";

  private const string ErroDeCiclo =
      "Esta receita criaria um ciclo: o componente apareceria dentro da propria estrutura.";
```

```csharp
  // ------------------------------------------------------------------ filhos

  public async Task<Result<IReadOnlyList<FilhoPadraoDto>>> ListarFilhos(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<FilhoPadraoDto>>.Ok(await ProjetarFilhos(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<FilhoPadraoDto>>> SubstituirFilhos(
      int componenteId, IReadOnlyList<LinhaDeFilhoPadraoDto> linhas, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    // Auto-referencia ANTES do ciclo, para a mensagem ser especifica. A travessia tambem pegaria,
    // mas diria "criaria um ciclo" onde "nao pode ser filho de si mesmo" e mais util.
    if (linhas.Any(l => l.ComponenteFilhoId == componenteId))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeAutoReferencia);

    var ids = linhas.Select(l => l.ComponenteFilhoId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          $"O componente {repetido} aparece mais de uma vez na lista.");

    var filhos = await _repositorio.ObterComponentesPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, filhos.ToDictionary(c => c.Id, c => c.Ativo), "componente", "componentes");
    if (problema is not null) return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(problema);

    if (await FechariaCiclo(componenteId, ids, ct))
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(ErroDeCiclo);

    // Mesma traducao de conflito dos materiais e do roteiro: a transacao SERIALIZABLE e do
    // repositorio, entao quem grava filhos tambem pode ser o perdedor derrubado pelo banco.
    // 409, nao 500 — Conflito_de_concorrencia_na_gravacao_dos_filhos_vira_erro_de_conflito.
    try
    {
      await _repositorio.SubstituirFilhosAsync(componenteId, linhas.Select(l =>
          new ComponenteFilhoPadrao
          {
            ComponentePaiId = componenteId,
            ComponenteFilhoId = l.ComponenteFilhoId,
            QuantidadePadrao = l.QuantidadePadrao,
          }).ToList(), ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Result<IReadOnlyList<FilhoPadraoDto>>.Falha(
          ErroDeConflitoDeGravacao, TipoDeErro.Conflito);
    }

    return Result<IReadOnlyList<FilhoPadraoDto>>.Ok(await ProjetarFilhos(componenteId, ct));
  }

  /// <summary>
  /// A pergunta e sobre o grafo COMO ELE FICARA depois da substituicao, nao sobre o atual: como o
  /// POST substitui a receita inteira, ele pode REMOVER uma aresta, e uma substituicao que desfaz
  /// um ciclo preexistente tem de ser ACEITA. Validar contra o grafo atual deixaria o usuario
  /// preso — a unica saida para consertar um ciclo seria SQL na mao. (§1.3 da spec.)
  ///
  /// So arestas SAINDO de `componenteId` mudam, entao basta perguntar se `componenteId` volta a
  /// si mesmo no grafo resultante.
  /// </summary>
  private async Task<bool> FechariaCiclo(
      int componenteId, IReadOnlyList<int> filhosNovos, CancellationToken ct)
  {
    var resultante = (await _repositorio.ListarTodasAsArestasAsync(ct))
        .Where(a => a.ComponentePaiId != componenteId)          // as antigas deste pai SOMEM
        .Select(a => (Pai: a.ComponentePaiId, Filho: a.ComponenteFilhoId))
        .Concat(filhosNovos.Select(f => (Pai: componenteId, Filho: f)))  // as novas ENTRAM
        .ToLookup(a => a.Pai, a => a.Filho);

    // `visitados` nao e otimizacao: sem ele, um ciclo PREEXISTENTE em outro ramo faria esta
    // travessia girar para sempre. Coberto por
    // `Ciclo_preexistente_em_outro_ramo_nao_impede_editar_este_componente`.
    var visitados = new HashSet<int>();
    var pilha = new Stack<int>(resultante[componenteId]);

    while (pilha.Count > 0)
    {
      var atual = pilha.Pop();
      if (atual == componenteId) return true;
      if (!visitados.Add(atual)) continue;
      foreach (var filho in resultante[atual]) pilha.Push(filho);
    }

    return false;
  }

  private async Task<IReadOnlyList<FilhoPadraoDto>> ProjetarFilhos(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarFilhosAsync(componenteId, ct);
    var componentes = (await _repositorio.ObterComponentesPorIdAsync(
        linhas.Select(l => l.ComponenteFilhoId).Distinct().ToList(), ct)).ToDictionary(c => c.Id);

    return linhas.Select(l =>
    {
      var c = componentes[l.ComponenteFilhoId];
      return new FilhoPadraoDto(l.Id, l.ComponenteFilhoId, c.Codigo, c.Descricao, l.QuantidadePadrao);
    }).ToList();
  }
```

- [ ] **Step 5: Rode e veja passar**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Esperado: **161 + 12 = 173 aprovados**, 0 falhas. (Baseline corrigida: a Task 4 fechou em 160 MEDIDOS, nao 156, e o fix pass da review da Task 4 somou mais +1 — 161. O +12, e nao +11, inclui `Conflito_de_concorrencia_na_gravacao_dos_filhos_vira_erro_de_conflito` — ver P1 do fix pass da review da Task 4, `.superpowers/sdd/task-4-fix-report.md`.)

- [ ] **Step 6: As CINCO mutações desta task**

**Mutação A (a validação de ciclo existe?):** apague a chamada `if (await FechariaCiclo(...))`. Esperado: `Ciclo_de_dois_niveis_e_recusado` e `Ciclo_de_tres_niveis_e_recusado` **FALHAM**. Reverta.

**Mutação B (grafo atual vs. resultante — a mais traiçoeira):** troque `.Where(a => a.ComponentePaiId != componenteId)` por `.Where(a => true)` — ou seja, valide contra o grafo **atual mais** as novas. Esperado: `Substituicao_que_desfaz_um_ciclo_preexistente_e_aceita` **FALHA**, e **todos os testes de ciclo proibido continuam verdes**. É isso que a torna traiçoeira: sem aquele teste, esta mutação passa despercebida. Reverta.

**Mutação C (profundidade):** faça `FechariaCiclo` olhar só o primeiro nível (`return resultante[componenteId].Contains(componenteId);`). Esperado: `Ciclo_de_dois_niveis_e_recusado` e `Ciclo_de_tres_niveis_e_recusado` **FALHAM**. Reverta.

**Mutação D (lista vazia deixa de apagar):** acrescente, no topo de `SubstituirFilhos`, um atalho `if (linhas.Count == 0) return Result<IReadOnlyList<FilhoPadraoDto>>.Ok(await ProjetarFilhos(componenteId, ct));` — ou seja, lista vazia vira no-op em vez de apagar. Esperado: `Filhos_com_lista_vazia_apaga` **FALHA**. É a mutação 3 da lista da spec §4.3, e ela merece passe próprio porque "não fazer nada" é o modo de falha mais fácil de introduzir sem perceber. Reverta.

**Mutação E (tipo restringe):** acrescente uma recusa para `Tipo != "Montagem"`. Esperado: `Componente_do_tipo_Bruto_pode_ter_filhos` **FALHA**. Reverta e reconfirme 173.

- [ ] **Step 7: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Application/Cadastros tests/Rastreamento.Application.Tests/Cadastros
git commit -m "feat(fase1c): caso de uso da receita padrao — filhos, com deteccao de ciclo"
```

**Delta previsto: Application 161 → 173 (+12).** (Baseline corrigida pela medicao da Task 4 e pelo fix
pass da review dela (160 → 161); +12 em vez de +11 inclui o teste de conflito de concorrência dos
filhos — P1 do fix pass da review da Task 4.)

---

## Task 6: Controller, DI e a guarda de perfis

**Files:**
- Create: `src/Rastreamento.Api/Controllers/ReceitaPadraoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (2 registros de DI, junto dos outros)
- Modify: `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs` (3 entradas)
- Test: `tests/Rastreamento.Api.Tests/ReceitaPadraoEndpointsTests.cs`

**Interfaces:**
- Consumes: `ReceitaPadraoUseCase` (Tasks 3-5), `IReceitaPadraoRepository` / `ReceitaPadraoRepository` (Task 2), `TokenDeTeste.Emitir(factory, perfil)` (já existe em `Api.Tests`).
- Produces: as 6 rotas. A Task 9 depende dos caminhos **exatos**: `componentes/{componenteId:int}/filhos-padrao`, `.../materiais-padrao`, `.../roteiro-padrao`.

- [ ] **Step 1: Escreva os testes que falham**

Crie `tests/Rastreamento.Api.Tests/ReceitaPadraoEndpointsTests.cs`. Siga o molde de `SetoresEndpointsTests.cs`: `IClassFixture<WebApplicationFactory<Program>>`, `ClienteComo(perfil)` com `TokenDeTeste.Emitir`, limpeza no `DisposeAsync`, e **URLs com `/api`**.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta da receita padrao, contra o SQL Server real (docker compose up -d).
/// Cada teste cria os proprios Componentes e apaga tudo que criou no DisposeAsync.
/// </summary>
public class ReceitaPadraoEndpointsTests
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<int> _componentesCriados = [];

  public ReceitaPadraoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    // As linhas de receita ANTES dos Componentes: as FKs apontam para eles.
    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => _componentesCriados.Contains(f.ComponentePaiId)
                 || _componentesCriados.Contains(f.ComponenteFilhoId)).ToListAsync());
    db.MateriaisPadrao.RemoveRange(await db.MateriaisPadrao
        .Where(m => _componentesCriados.Contains(m.ComponenteId)).ToListAsync());
    db.RoteirosPadrao.RemoveRange(await db.RoteirosPadrao
        .Where(r => _componentesCriados.Contains(r.ComponenteId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(await db.Componentes
        .Where(c => _componentesCriados.Contains(c.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private async Task<int> NovoComponente()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"RP-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste da receita",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  [Fact]
  public async Task Sem_token_a_leitura_e_401()
  {
    var id = await NovoComponente();

    var resposta = await _factory.CreateClient()
        .GetAsync($"/api/componentes/{id}/filhos-padrao");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  /// <summary>Leitura e de QUALQUER autenticado — Operador inclusive.</summary>
  [Fact]
  public async Task Operador_le_a_receita()
  {
    var id = await NovoComponente();

    var resposta = await ClienteComo("Operador").GetAsync($"/api/componentes/{id}/filhos-padrao");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  /// <summary>Escrita e so de Administrador e PCP. Esta e a fronteira REAL — esconder botao nao e.</summary>
  [Fact]
  public async Task Operador_nao_grava_receita()
  {
    var id = await NovoComponente();
    var filho = await NovoComponente();

    var resposta = await ClienteComo("Operador").PostAsJsonAsync(
        $"/api/componentes/{id}/filhos-padrao",
        new { linhas = new[] { new { componenteFilhoId = filho, quantidadePadrao = 1m } } });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task PCP_grava_filhos_e_a_leitura_devolve_o_que_foi_gravado()
  {
    var pai = await NovoComponente();
    var filho = await NovoComponente();

    var post = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/filhos-padrao",
        new { linhas = new[] { new { componenteFilhoId = filho, quantidadePadrao = 2m } } });
    Assert.Equal(HttpStatusCode.OK, post.StatusCode);

    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<FilhoLido>>($"/api/componentes/{pai}/filhos-padrao");

    Assert.Equal(filho, Assert.Single(lidas!).ComponenteFilhoId);
  }

  [Fact]
  public async Task Componente_inexistente_e_404()
  {
    var resposta = await ClienteComo("PCP").GetAsync("/api/componentes/99999999/filhos-padrao");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Corpo SEM o campo `linhas` e 400, NAO "apagar a receita". Lista vazia e comando explicito de
  /// apagar; campo ausente e requisicao malformada. Sem o `[Required]` no DTO, `POST {}` limparia
  /// a receita em silencio — mesma classe de bug que o `DefinirAtivoDto` ja pagou.
  /// </summary>
  [Fact]
  public async Task Corpo_sem_o_campo_linhas_e_400()
  {
    var id = await NovoComponente();

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{id}/filhos-padrao", new { });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Lista_vazia_apaga_a_receita_e_responde_200()
  {
    var pai = await NovoComponente();
    var filho = await NovoComponente();
    await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/filhos-padrao",
        new { linhas = new[] { new { componenteFilhoId = filho, quantidadePadrao = 1m } } });

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/filhos-padrao", new { linhas = Array.Empty<object>() });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<FilhoLido>>($"/api/componentes/{pai}/filhos-padrao");
    Assert.Empty(lidas!);
  }

  /// <summary>O ciclo sai como 400, nao 409: o 409 do projeto ja significa "codigo duplicado".</summary>
  [Fact]
  public async Task Ciclo_responde_400_ponta_a_ponta()
  {
    var a = await NovoComponente();
    var b = await NovoComponente();

    await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{b}/filhos-padrao",
        new { linhas = new[] { new { componenteFilhoId = a, quantidadePadrao = 1m } } });

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{a}/filhos-padrao",
        new { linhas = new[] { new { componenteFilhoId = b, quantidadePadrao = 1m } } });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  /// <summary>
  /// Cria os PROPRIOS setores em vez de pescar os que estiverem no banco. Ler
  /// `db.Setores.Take(2)` amarraria este teste a massa ambiente — e o `db/seed.sql` (o unico seed
  /// obrigatorio) NAO tem setor nenhum: os que existem hoje sao resquicio de teste manual, e a
  /// massa da Task 7 e explicitamente proibida de sustentar teste automatizado.
  /// </summary>
  [Fact]
  public async Task PCP_grava_roteiro_e_a_ordem_vem_do_servidor()
  {
    var id = await NovoComponente();
    var (setorA, setorB) = await DoisSetores();

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{id}/roteiro-padrao",
        new { linhas = new[] { new { setorId = setorB }, new { setorId = setorA } } });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<RoteiroLido>>($"/api/componentes/{id}/roteiro-padrao");
    // A ordem sai da POSICAO no array: setorB veio primeiro, entao setorB e a Ordem 1.
    Assert.Equal([(setorB, 1), (setorA, 2)], lidas!.Select(l => (l.SetorId, l.Ordem)));
  }

  private sealed record FilhoLido(int Id, int ComponenteFilhoId, decimal QuantidadePadrao);
  private sealed record RoteiroLido(int Id, int SetorId, string Nome, int Ordem);
}
```

Escreva também o helper `DoisSetores()` neste arquivo, no mesmo molde de `NovoComponente()`: cria dois `Setor` com nome único (`$"rp-{Guid.NewGuid():N}"[..12]`, para não colidir com `UQ_Setor_Nome`), registra os ids numa lista `_setoresCriados` e os apaga no `DisposeAsync` **depois** das linhas de roteiro (a FK aponta para eles). Devolve `(int, int)` com os dois ids.

- [ ] **Step 2: Rode e veja falhar**

```bash
dotnet test tests/Rastreamento.Api.Tests
```

Esperado: os 10 testes novos **FALHAM com 404** (as rotas não existem). A guarda `PerfisDeEscritaDeclaradosTests` continua verde — ainda não há controller novo.

- [ ] **Step 3: Crie o controller**

`src/Rastreamento.Api/Controllers/ReceitaPadraoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Controller PROPRIO, e nao acoes novas no ComponentesController, que iria de 4 para 10 acoes. O
/// precedente de sub-recurso em controller separado ja existe: AgrupamentosController atende
/// `pedidos/{pedidoId:int}/agrupamentos` E `agrupamentos/{id:int}`, declarando o caminho completo
/// por acao em vez de um [Route] de classe. Mesmo molde aqui.
///
/// Herda de ControllerBase, e nao de CadastroControllerBase: nao ha 409 de duplicidade a montar,
/// entao TraduzirFalha/LocalizadorDeDuplicado nao servem para nada aqui — e herdar de uma base
/// so pelo parentesco convidaria acao herdada a rotear por este controller sem ninguem notar.
/// </summary>
[ApiController]
[Authorize]
public class ReceitaPadraoController : ControllerBase
{
  /// <summary>
  /// Mesmos perfis do proprio Componente: quem cadastra a peca e quem conhece a receita dela.
  /// Leitura fica liberada a qualquer autenticado, como nos outros catalogos.
  /// </summary>
  private const string PerfisDeEscrita = "Administrador,PCP";

  private readonly ReceitaPadraoUseCase _receita;

  public ReceitaPadraoController(ReceitaPadraoUseCase receita) => _receita = receita;

  // ---------------------------------------------------------------- filhos

  [HttpGet("componentes/{componenteId:int}/filhos-padrao")]
  public async Task<IActionResult> ListarFilhos(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarFilhos(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/filhos-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirFilhos(
      int componenteId, [FromBody] ReceitaDeFilhosDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirFilhos(componenteId, corpo.Linhas!, ct));

  // ---------------------------------------------------------------- materiais

  [HttpGet("componentes/{componenteId:int}/materiais-padrao")]
  public async Task<IActionResult> ListarMateriais(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarMateriais(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/materiais-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirMateriais(
      int componenteId, [FromBody] ReceitaDeMateriaisDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirMateriais(componenteId, corpo.Linhas!, ct));

  // ---------------------------------------------------------------- roteiro

  [HttpGet("componentes/{componenteId:int}/roteiro-padrao")]
  public async Task<IActionResult> ListarRoteiro(int componenteId, CancellationToken ct) =>
      Traduzir(await _receita.ListarRoteiro(componenteId, ct));

  [HttpPost("componentes/{componenteId:int}/roteiro-padrao")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> SubstituirRoteiro(
      int componenteId, [FromBody] ReceitaDeRoteiroDto corpo, CancellationToken ct) =>
      Traduzir(await _receita.SubstituirRoteiro(componenteId, corpo.Linhas!, ct));

  // ----------------------------------------------------------------

  /// <summary>
  /// 200 no sucesso, 404 so para "componente pai nao existe", 400 para o resto.
  ///
  /// 200 e nao 201 no POST: ele nao cria um recurso novo enderecavel, substitui o conteudo de um
  /// sub-recurso que ja tem endereco. Nada de CreatedAtAction — nao ha "o" recurso criado.
  ///
  /// O `corpo.Linhas!` das acoes acima e seguro porque `[Required]` + [ApiController] barram o
  /// campo ausente com 400 ANTES de a acao rodar. Lista VAZIA continua chegando aqui: e o comando
  /// explicito de apagar a receita.
  /// </summary>
  private IActionResult Traduzir<T>(Result<T> resultado) =>
      resultado.Sucesso
          ? Ok(resultado.Valor)
          : resultado.TipoDoErro == TipoDeErro.NaoEncontrado
              ? NotFound(new { erro = resultado.Erro })
              : BadRequest(new { erro = resultado.Erro });
}
```

- [ ] **Step 4: Registre no DI**

Em `src/Rastreamento.Api/Program.cs`, logo abaixo de `builder.Services.AddScoped<CadastroDeComponenteUseCase>();`:

```csharp
builder.Services.AddScoped<IReceitaPadraoRepository, ReceitaPadraoRepository>();
builder.Services.AddScoped<ReceitaPadraoUseCase>();
```

- [ ] **Step 5: Rode e veja a GUARDA falhar — este passo é o ponto da task**

```bash
dotnet test tests/Rastreamento.Api.Tests
```

Esperado: os 10 testes novos **PASSAM**, e `PerfisDeEscritaDeclaradosTests` **FALHA**, nomeando as três rotas novas — algo como *"Controller com [Authorize(Roles)] fora da tabela aprovada: POST componentes/{componenteId:int}/filhos-padrao, …"*.

**Isto é a guarda funcionando, não um bug.** A 1C é o primeiro recurso novo desde que ela foi reescrita sobre o `EndpointDataSource`; esta é a primeira vez que ela faz o trabalho para o qual foi construída. **Copie as identidades exatas da mensagem de falha** para o próximo passo — não as digite de memória.

Se a guarda **não** falhar, **pare e investigue**: ou o controller não foi descoberto, ou a guarda regrediu. Não siga em frente.

- [ ] **Step 6: Acrescente as 3 entradas na tabela aprovada**

Em `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs`, no `TabelaAprovada`, depois do bloco de `componentes`:

```csharp
    ["POST componentes/{componenteId:int}/filhos-padrao"] = ["Administrador", "PCP"],
    ["POST componentes/{componenteId:int}/materiais-padrao"] = ["Administrador", "PCP"],
    ["POST componentes/{componenteId:int}/roteiro-padrao"] = ["Administrador", "PCP"],
```

- [ ] **Step 7: Rode tudo e veja passar**

```bash
dotnet test Rastreamento.slnx
```

Esperado: **Application 173**, **Infrastructure 58**, **Api 171 + 10 = 181**. Total **412**.

- [ ] **Step 8: Prove por mutação que a guarda protege as rotas novas**

Troque `PerfisDeEscrita` do `ReceitaPadraoController` para `"Administrador,PCP,Qualidade"`. Esperado: `PerfisDeEscritaDeclaradosTests` **FALHA** dizendo que o roteamento exige `[Administrador, PCP, Qualidade]` contra a tabela `[Administrador, PCP]`. Reverta.

Segunda mutação: **apague** o `[Authorize(Roles = PerfisDeEscrita)]` de `SubstituirMateriais`. Esperado: a guarda **FALHA** pela asserção de verbo de escrita (rota de escrita fora da tabela e fora dos isentos), **e** `Operador_nao_grava_receita` continua verde (ele testa filhos, não materiais) — ou seja, **quem pega essa é a guarda, não o teste de endpoint**. Reverta e reconfirme 412.

- [ ] **Step 9: Build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror
```

```bash
git add src/Rastreamento.Api tests/Rastreamento.Api.Tests
git commit -m "feat(fase1c): endpoints da receita padrao, com as 3 rotas na guarda de perfis"
```

**Delta previsto: Api 171 → 181 (+10). Backend total 338 → 412** (era 385 no plano original; a
Task 2 fechou em Infrastructure 57 em vez de 48, a Task 3 em Application 142 em vez de 139, e o fix
pass da review da Task 3 levou Application a 150 e Infrastructure a 58, e a Task 4 a 160 em vez de
156 — ver o bloco MEDIDO em cada uma. O fix pass da review da Task 4 somou mais duas vezes: +0 em
código (I1/I2/I3) e +1 em teste novo (N1, catch estreito) — Application 160 → 161 — e +1 na previsão
da Task 5 (P1, conflito de concorrência dos filhos) — 172 → 173, não 171. Ver
`.superpowers/sdd/task-4-fix-report.md`).

---

## Task 7: Massa de demonstração e regeneração do banco

Autorizada pelo usuário em 2026-08-17 (spec §4.5). **O banco de dev é descartável.**

**Files:**
- Create: `db/seed-demo.sql`

**Interfaces:**
- Consumes: `specs/02-modelo-de-dados.sql`, `db/seed.sql`.
- Produces: catálogo com volume, para as Tasks 8 e 12 poderem verificar a busca de verdade.

**Por que agora:** medido em 2026-08-17, o banco tem **2 Componentes, 0 Materiais, 4 Setores**. Com zero materiais a seção de materiais-padrão não é nem exercitável na tela, e com 2 componentes a busca do `SeletorComBusca` não prova nada.

- [ ] **Step 1: Escreva o `db/seed-demo.sql`**

Regras deste arquivo:
- **Idempotente** (`IF NOT EXISTS` por código), no mesmo padrão do `admin` em `db/seed.sql`.
- **Nomes plausíveis de fábrica metalúrgica**, não `Teste 1`/`Teste 2` — o ponto é conferir a tela com dado que parece real.
- **Volume que faz a paginação valer:** pelo menos **45 Componentes** (mais que os 20 do tamanho de página padrão, para haver 3 páginas), **12 Materiais**, **8 Setores**.
- Alguns **códigos com prefixo comum** (`CH-`, `PA-`, `EX-`) para a busca por código ter o que filtrar, e descrições com palavras repetidas para a busca por **nome** também ter.
- Duas ou três **receitas padrão de exemplo** já montadas (filhos, materiais e roteiro), incluindo **uma com o mesmo setor repetido** — assim o retorno ao setor aparece na tela e ninguém o "conserta" por achar que é bug.
- **Nenhum ciclo.**

Cabeçalho obrigatório do arquivo:

```sql
-- =====================================================================
-- MASSA DE DEMONSTRACAO — conveniencia de desenvolvimento, NAO requisito.
--
-- NAO confundir com db/seed.sql, que e o MINIMO para o sistema funcionar
-- (perfis + usuarios) e continua sendo o unico obrigatorio.
--
-- NENHUM teste automatizado pode depender deste arquivo. Api.Tests e
-- Infrastructure.Tests criam a propria massa, e e isso que os torna
-- deterministicos numa maquina qualquer. A dependencia e de mao unica:
-- a verificacao MANUAL usa este demo, a suite ignora que ele existe.
--
-- Idempotente: rodar duas vezes nao duplica linha.
-- =====================================================================
```

- [ ] **Step 2: Regenere o banco do zero**

```bash
docker compose up -d
```

Depois, no container, nesta ordem: `DROP DATABASE Rastreamento` (se existir) → `CREATE DATABASE Rastreamento` → `specs/02-modelo-de-dados.sql` → `db/seed.sql` → `db/seed-demo.sql`. Use o mesmo padrão de invocação de `CLAUDE.md`, com `MSYS_NO_PATHCONV=1`.

- [ ] **Step 3: Confirme a massa**

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -Q "SELECT 'Componente' t, COUNT(*) n FROM dbo.Componente UNION ALL SELECT 'Material', COUNT(*) FROM dbo.Material UNION ALL SELECT 'Setor', COUNT(*) FROM dbo.Setor UNION ALL SELECT 'Usuario', COUNT(*) FROM dbo.Usuario UNION ALL SELECT 'FilhoPadrao', COUNT(*) FROM dbo.ComponenteFilhoPadrao UNION ALL SELECT 'RoteiroPadrao', COUNT(*) FROM dbo.ComponenteRoteiroPadrao;"
```

Esperado: Componente ≥ 45, Material ≥ 12, Setor ≥ 8, Usuario = 2, e as receitas de exemplo populadas.

- [ ] **Step 4: Rode o `seed-demo.sql` DE NOVO e confirme que nada duplicou**

Repita o Step 3. Os números têm de ser **idênticos**. Se cresceram, o arquivo não é idempotente — corrija antes de seguir.

- [ ] **Step 5: A suíte inteira continua verde com o banco novo**

```bash
dotnet test Rastreamento.slnx
```

Esperado: **412**, exatamente como no fim da Task 6. Se algum teste passou a depender da massa de demo, ele **quebrou a regra de mão única** — conserte o teste, não o seed.

- [ ] **Step 6: Commit**

```bash
git add db/seed-demo.sql
git commit -m "feat(fase1c): massa de demonstracao, separada do seed minimo"
```

**Delta previsto: ZERO testes.** Esta task não acrescenta prova automatizada — ela viabiliza a verificação manual da Task 12.

---

## Task 8: Primitiva `SeletorComBusca`

**Files:**
- Create: `web/src/components/SeletorComBusca.tsx`
- Test: `web/src/components/SeletorComBusca.test.tsx`

**Interfaces:**
- Consumes: `useBuscaPaginada` (`web/src/hooks/`), `listarComponentes` + `ComponenteDto` (`web/src/api/cadastros.ts`), `CLASSES_DE_CONTROLE` + `Campo` (`web/src/components/Campo.tsx`), `mensagemDeErro` (`web/src/api/erros.ts`).
- Produces: `<SeletorComBusca rotulo={string} valorSelecionado={ComponenteDto | null} aoSelecionar={(c: ComponenteDto) => void} />`

**A primitiva mora em `web/src/components/` com teste próprio — não embutida na tela.** É regra do `CLAUDE.md`.

- [ ] **Step 1: Escreva os testes que falham**

`web/src/components/SeletorComBusca.test.tsx`:

```tsx
// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SeletorComBusca } from './SeletorComBusca'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

/** Três componentes, o suficiente para navegar com o teclado e provar a seleção. */
const PAGINA = {
  itens: [
    { id: 1, codigo: 'CH-100', descricao: 'Chapa lateral', tipo: 'Fabricado', ativo: true },
    { id: 2, codigo: 'CH-200', descricao: 'Chapa frontal', tipo: 'Fabricado', ativo: true },
    { id: 3, codigo: 'PA-010', descricao: 'Parafuso M8', tipo: 'Bruto', ativo: true },
  ],
  total: 3,
  pagina: 1,
  tamanho: 20,
}

describe('SeletorComBusca', () => {
  it('mostra as opções que a busca devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)

    expect(await screen.findByText('CH-100')).toBeTruthy()
    expect(screen.getByText('Parafuso M8')).toBeTruthy()
  })

  it('seleciona com Enter depois de navegar com a seta para baixo', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    await screen.findByText('CH-100')

    const campo = screen.getByRole('combobox')
    await userEvent.click(campo)
    await userEvent.keyboard('{ArrowDown}{ArrowDown}{Enter}')

    expect(aoSelecionar).toHaveBeenCalledWith(expect.objectContaining({ id: 2 }))
  })

  it('seleciona com clique', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )

    await userEvent.click(await screen.findByText('PA-010'))

    expect(aoSelecionar).toHaveBeenCalledWith(expect.objectContaining({ id: 3 }))
  })

  it('Esc fecha a lista sem selecionar', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    await screen.findByText('CH-100')

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.keyboard('{Escape}')

    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull())
    expect(aoSelecionar).not.toHaveBeenCalled()
  })

  it('anuncia o estado do combobox por ARIA', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    await screen.findByText('CH-100')

    const campo = screen.getByRole('combobox')
    await userEvent.click(campo)

    expect(campo.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('listbox')).toBeTruthy()
  })

  /** Estado VAZIO: texto que distingue "não achei" de "não há nada cadastrado". */
  it('mostra estado vazio quando a busca não achou nada', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)

    expect(await screen.findByText(/nenhum componente encontrado/i)).toBeTruthy()
  })

  /** Estado de ERRO: sem ele, falha de rede vira lista vazia silenciosa. */
  it('mostra o erro quando a busca falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/componentes': () => new Response(null, { status: 500 }),
    }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)

    expect(await screen.findByRole('alert')).toBeTruthy()
  })

  it('mostra o rótulo do item já selecionado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca
        rotulo="Componente filho"
        valorSelecionado={PAGINA.itens[0]}
        aoSelecionar={vi.fn()}
      />,
    )

    expect(await screen.findByDisplayValue(/CH-100/)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Rode e veja falhar**

```bash
cd web && npm test -- --run SeletorComBusca
```

Esperado: **FALHA** — o módulo `./SeletorComBusca` não existe.

- [ ] **Step 3: Implemente a primitiva**

`web/src/components/SeletorComBusca.tsx`. Exigências que **não** são negociáveis:

- **Usa `useBuscaPaginada`** com `buscar: listarComponentes` — debounce, cancelamento por sequência e clamp já estão resolvidos lá, e a regra do projeto proíbe refazer isso com `useState` + `useEffect` à mão.
- `role="combobox"` no input, com `aria-expanded`, `aria-controls` apontando para o id do listbox e `aria-activedescendant` apontando para o id da opção destacada.
- Lista com `role="listbox"`; cada item com `role="option"` e `aria-selected`.
- Teclado: `ArrowDown` / `ArrowUp` movem o destaque, `Enter` seleciona o destacado, `Escape` fecha sem selecionar.
- **Os três estados dentro do painel:** carregando (`EstadoCarregando`), vazio (`Nenhum componente encontrado.`) e erro (`BannerDeErro` com `mensagemDeErro(busca.erro)`, que já rende `role="alert"`).
- **Cores só por token.** `border-borda`, `bg-superficie`, `text-tinta`, `bg-acao` no item destacado — confira os nomes reais em `web/src/index.css` antes de escrever.
- Input usa `CLASSES_DE_CONTROLE` e é rotulado por `<Campo rotulo={rotulo}>{(id) => <input id={id} … />}</Campo>`.
- Fechar ao perder o foco (`onBlur`) precisa de um pequeno atraso ou de checagem de `relatedTarget`, senão o clique numa opção fecha a lista **antes** de o clique registrar. Se resolver com atraso, escreva no comentário **por que** ele existe — senão o próximo leitor o remove.

- [ ] **Step 4: Rode e veja passar**

```bash
cd web && npm test -- --run SeletorComBusca
```

Esperado: **8 aprovados**.

- [ ] **Step 5: Suíte inteira e build**

```bash
cd web && npm test -- --run
```

Esperado: **317 + 8 = 325 / 29 arquivos**.

```bash
cd web && npm run build
```

Esperado: build limpo. **`npm test` não faz typecheck — o build é a verificação de tipo.**

- [ ] **Step 6: Prove por mutação que o teclado é testado de verdade**

Troque o handler de `Enter` para não chamar `aoSelecionar`. Esperado: `seleciona com Enter depois de navegar com a seta para baixo` **FALHA**. Reverta.

Segunda mutação: apague o ramo de erro (deixe o painel vazio quando `busca.erro` existir). Esperado: `mostra o erro quando a busca falha` **FALHA**. Reverta e reconfirme 325.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/SeletorComBusca.tsx web/src/components/SeletorComBusca.test.tsx
git commit -m "feat(fase1c): primitiva SeletorComBusca, combobox sobre useBuscaPaginada"
```

**Delta previsto: front 317 → 325 (+8), 28 → 29 arquivos.**

---

## Task 9: Cliente de API e tabela de permissões do front

**Files:**
- Create: `web/src/api/receitaPadrao.ts`
- Test: `web/src/api/receitaPadrao.test.ts`
- Modify: `web/src/auth/permissoes.ts`
- Modify: `web/src/auth/permissoesEspelhamOBackend.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (`web/src/api/client.ts`), `ErroDeApi` (`web/src/api/erros.ts`), as rotas da Task 6.
- Produces:
  - `FilhoPadraoDto { id, componenteFilhoId, codigo, descricao, quantidadePadrao }`
  - `MaterialPadraoDto { id, materialId, codigo, descricao, unidadeMedida, quantidadePadrao }`
  - `RoteiroPadraoDto { id, setorId, nome, ordem }`
  - `listarFilhosPadrao(componenteId)` / `salvarFilhosPadrao(componenteId, linhas)`
  - `listarMateriaisPadrao` / `salvarMateriaisPadrao` · `listarRoteiroPadrao` / `salvarRoteiroPadrao`
  - `Recurso` passa a incluir `'receitaPadrao'`

- [ ] **Step 1: Escreva o teste que falha**

`web/src/api/receitaPadrao.test.ts`. **A URL é o que este arquivo prova** — é a lição do adendo F4 (`criarSetor`/`criarMaterial` ficaram 5 tasks sem prova de URL):

```ts
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  listarFilhosPadrao, salvarFilhosPadrao,
  listarMateriaisPadrao, salvarMateriaisPadrao,
  listarRoteiroPadrao, salvarRoteiroPadrao,
} from './receitaPadrao'
import { respostaJson } from '../testes/api'

afterEach(() => vi.unstubAllGlobals())

describe('receitaPadrao', () => {
  it('lê os filhos-padrão pela rota do sub-recurso', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await listarFilhosPadrao(7)

    expect(String(fetchFalso.mock.calls[0][0])).toContain('/api/componentes/7/filhos-padrao')
  })

  it('grava os filhos-padrão como POST com o corpo em { linhas }', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await salvarFilhosPadrao(7, [{ componenteFilhoId: 3, quantidadePadrao: 2 }])

    const [, init] = fetchFalso.mock.calls[0]
    expect(init.method).toBe('POST')
    // O envelope { linhas } NÃO é decorativo: corpo sem ele é 400 no backend, de propósito.
    expect(JSON.parse(init.body)).toEqual({
      linhas: [{ componenteFilhoId: 3, quantidadePadrao: 2 }],
    })
  })

  it('grava lista vazia sem inventar atalho — é o comando de apagar', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await salvarFilhosPadrao(7, [])

    expect(JSON.parse(fetchFalso.mock.calls[0][1].body)).toEqual({ linhas: [] })
  })

  it('lê os materiais-padrão pela rota do sub-recurso', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await listarMateriaisPadrao(7)

    expect(String(fetchFalso.mock.calls[0][0])).toContain('/api/componentes/7/materiais-padrao')
  })

  it('grava os materiais-padrão como POST em { linhas }', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await salvarMateriaisPadrao(7, [{ materialId: 5, quantidadePadrao: 1.5 }])

    expect(JSON.parse(fetchFalso.mock.calls[0][1].body)).toEqual({
      linhas: [{ materialId: 5, quantidadePadrao: 1.5 }],
    })
  })

  it('lê o roteiro-padrão pela rota do sub-recurso', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await listarRoteiroPadrao(7)

    expect(String(fetchFalso.mock.calls[0][0])).toContain('/api/componentes/7/roteiro-padrao')
  })

  /** O corpo do roteiro NÃO leva `ordem`: quem numera é o servidor, pela posição do array. */
  it('grava o roteiro mandando só setorId, na ordem do array', async () => {
    const fetchFalso = vi.fn(() => Promise.resolve(respostaJson([])))
    vi.stubGlobal('fetch', fetchFalso)

    await salvarRoteiroPadrao(7, [{ setorId: 20 }, { setorId: 21 }])

    expect(JSON.parse(fetchFalso.mock.calls[0][1].body)).toEqual({
      linhas: [{ setorId: 20 }, { setorId: 21 }],
    })
  })

  it('erro de rede vira ErroDeApi com o status', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 500 }))))

    await expect(listarFilhosPadrao(7)).rejects.toMatchObject({ status: 500 })
  })
})
```

- [ ] **Step 2: Rode e veja falhar**

```bash
cd web && npm test -- --run receitaPadrao
```

Esperado: **FALHA** — o módulo não existe.

- [ ] **Step 3: Implemente o cliente**

`web/src/api/receitaPadrao.ts`. **Caminhos SEM o prefixo `/api`** — quem o aplica é o `rota()` de `client.ts`; escrever `/api/...` no call site duplicaria:

```ts
import { apiFetch } from './client'
import { ErroDeApi } from './erros'

export interface FilhoPadraoDto {
  id: number
  componenteFilhoId: number
  codigo: string
  descricao: string
  quantidadePadrao: number
}

export interface MaterialPadraoDto {
  id: number
  materialId: number
  codigo: string
  descricao: string
  unidadeMedida: string
  quantidadePadrao: number
}

export interface RoteiroPadraoDto {
  id: number
  setorId: number
  nome: string
  ordem: number
}

export interface LinhaDeFilho {
  componenteFilhoId: number
  quantidadePadrao: number
}

export interface LinhaDeMaterial {
  materialId: number
  quantidadePadrao: number
}

/**
 * Só `setorId`: a `ordem` é a POSIÇÃO no array e quem a atribui é o servidor. Mandar `ordem` daqui
 * reabriria buraco e duplicata na sequência.
 */
export interface LinhaDeRoteiro {
  setorId: number
}

async function ler<T>(resp: Response, oQue: string): Promise<T> {
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao ${oQue} (${resp.status}).`)
  return (await resp.json()) as T
}

/**
 * O envelope `{ linhas }` não é decorativo: corpo sem o campo é 400 no backend, de propósito —
 * lista VAZIA significa "apague a receita", e campo AUSENTE significa requisição malformada. Se o
 * cliente achatasse o array direto no corpo, `POST []` viraria ambíguo.
 */
function gravar<T>(caminho: string, linhas: unknown[], oQue: string): Promise<T> {
  return apiFetch(caminho, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ linhas }),
  }).then((r) => ler<T>(r, oQue))
}

export function listarFilhosPadrao(componenteId: number): Promise<FilhoPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/filhos-padrao`)
    .then((r) => ler<FilhoPadraoDto[]>(r, 'carregar os componentes filhos'))
}

export function salvarFilhosPadrao(
  componenteId: number,
  linhas: LinhaDeFilho[],
): Promise<FilhoPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/filhos-padrao`, linhas, 'salvar os componentes filhos')
}

export function listarMateriaisPadrao(componenteId: number): Promise<MaterialPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/materiais-padrao`)
    .then((r) => ler<MaterialPadraoDto[]>(r, 'carregar os materiais'))
}

export function salvarMateriaisPadrao(
  componenteId: number,
  linhas: LinhaDeMaterial[],
): Promise<MaterialPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/materiais-padrao`, linhas, 'salvar os materiais')
}

export function listarRoteiroPadrao(componenteId: number): Promise<RoteiroPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/roteiro-padrao`)
    .then((r) => ler<RoteiroPadraoDto[]>(r, 'carregar o roteiro'))
}

export function salvarRoteiroPadrao(
  componenteId: number,
  linhas: LinhaDeRoteiro[],
): Promise<RoteiroPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/roteiro-padrao`, linhas, 'salvar o roteiro')
}
```

- [ ] **Step 4: Acrescente o recurso na tabela de permissões**

Em `web/src/auth/permissoes.ts`:

```ts
export type Recurso =
  | 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos' | 'receitaPadrao'
```

e, no objeto `ESCRITA`:

```ts
  // Entrada PRÓPRIA, e não reúso de `componentes`, mesmo com perfis idênticos: o mapa de
  // `permissoesEspelhamOBackend` é POR CONTROLLER, e recurso apontando para o controller errado é
  // exatamente o que essa guarda existe para pegar.
  receitaPadrao: ['Administrador', 'PCP'],
```

Em `web/src/auth/permissoesEspelhamOBackend.test.ts`, no `CONTROLLER_POR_RECURSO`:

```ts
  receitaPadrao: 'ReceitaPadraoController.cs',
```

- [ ] **Step 5: Rode e veja passar**

```bash
cd web && npm test -- --run
```

Esperado: **325 + 8 = 333 / 30 arquivos** (8 de `receitaPadrao.test.ts`; a guarda de espelhamento ganha uma linha no `it.each` já existente, então o número dela sobe 1 — **confirme o número real e ajuste a previsão das tasks seguintes se divergir**).

```bash
cd web && npm run build
```

- [ ] **Step 6: Prove por mutação que o espelhamento pega a divergência**

Troque, em `permissoes.ts`, `receitaPadrao: ['Administrador', 'PCP']` por `['Administrador']`. Esperado: `permissoesEspelhamOBackend` **FALHA** para `receitaPadrao`. Reverta e reconfirme.

- [ ] **Step 7: Commit**

```bash
git add web/src/api/receitaPadrao.ts web/src/api/receitaPadrao.test.ts web/src/auth
git commit -m "feat(fase1c): cliente de API da receita padrao e recurso na tabela de permissoes"
```

**Delta previsto: front 325 → ~334.** Meça e propague.

---

## Task 10: `ComponenteDetalhePage` — leitura e os três estados

**Files:**
- Create: `web/src/pages/ComponenteDetalhePage.tsx`
- Test: `web/src/pages/ComponenteDetalhePage.test.tsx`
- Modify: `web/src/App.tsx` (rota)
- Modify: `web/src/pages/ComponentesPage.tsx` (link por item)

**Interfaces:**
- Consumes: tudo da Task 9, `Pagina` / `EstadoCarregando` / `EstadoVazio` / `BannerDeErro` / `ListaDeCadastro` (`web/src/components/`), `mensagemDeErro`.
- Produces: rota `/componentes/:id`. A Task 11 acrescenta escrita **nesta mesma tela**.

**Escopo desta task: SÓ LEITURA.** Nada de formulário, nada de botão Salvar — isso é a Task 11. O corte existe para que um revisor possa aprovar a leitura e reprovar a escrita separadamente.

- [ ] **Step 1: Escreva os testes que falham**

`web/src/pages/ComponenteDetalhePage.test.tsx`. Abra `web/src/pages/PedidoDetalhePage.test.tsx` primeiro e **copie a forma de montar a rota com parâmetro** (`MemoryRouter` + `Routes`) em vez de inventar outra.

```tsx
// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

const COMPONENTE = { id: 7, codigo: 'CH-100', descricao: 'Chapa lateral', tipo: 'Fabricado', ativo: true }
const FILHOS = [{ id: 1, componenteFilhoId: 3, codigo: 'PA-010', descricao: 'Parafuso M8', quantidadePadrao: 4 }]
const MATERIAIS = [{ id: 2, materialId: 5, codigo: 'CH-3', descricao: 'Chapa 3mm', unidadeMedida: 'KG', quantidadePadrao: 1.5 }]
const ROTEIRO = [
  { id: 3, setorId: 20, nome: 'Corte', ordem: 1 },
  { id: 4, setorId: 21, nome: 'Solda', ordem: 2 },
  { id: 5, setorId: 20, nome: 'Corte', ordem: 3 },
]

/** Todas as 4 chamadas que a tela faz, no caminho feliz. */
function apiCompleta() {
  return fetchPorRota({
    '/componentes/7/filhos-padrao': () => respostaJson(FILHOS),
    '/componentes/7/materiais-padrao': () => respostaJson(MATERIAIS),
    '/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
    '/componentes/7': () => respostaJson(COMPONENTE),
  })
}

describe('ComponenteDetalhePage — leitura', () => {
  it('mostra as três seções com o que a API devolveu', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText('PA-010')).toBeTruthy()
    expect(await screen.findByText('CH-3')).toBeTruthy()
    expect(await screen.findByText('Solda')).toBeTruthy()
  })

  /**
   * O MESMO setor em duas posições do roteiro tem que aparecer DUAS vezes — é retorno ao setor,
   * não duplicata. Uma tela que "deduplica por id de setor" some com a segunda passagem.
   */
  it('mostra o mesmo setor duas vezes quando o roteiro tem retorno ao setor', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findAllByText('Corte')).toHaveLength(2)
  })

  it('mostra o carregando antes de a API responder', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText(/carregando/i)).toBeTruthy()
  })

  /** Texto que distingue "não achei" de "não há nada": o vazio aqui é "receita não montada". */
  it('mostra estado vazio por seção quando a receita está vazia', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/componentes/7/filhos-padrao': () => respostaJson([]),
      '/componentes/7/materiais-padrao': () => respostaJson([]),
      '/componentes/7/roteiro-padrao': () => respostaJson([]),
      '/componentes/7': () => respostaJson(COMPONENTE),
    }))
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText(/nenhum componente filho/i)).toBeTruthy()
    expect(screen.getByText(/nenhum material/i)).toBeTruthy()
    expect(screen.getByText(/nenhum setor no roteiro/i)).toBeTruthy()
  })

  it('mostra o erro quando a carga falha', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 500 }))))
    renderizarNaRota('/componentes/7')

    expect(await screen.findByRole('alert')).toBeTruthy()
  })

  /**
   * Erro e vazio são MUTUAMENTE EXCLUSIVOS. Sem a guarda `erro === null &&`, uma falha na carga
   * mostra "Nenhum componente filho" JUNTO do banner de erro — que é o Critical que a Task 8 da
   * Fase 1D já pagou uma vez, e a Task 10 dela pagou de novo. Não é para pagar uma terceira.
   */
  it('não mostra estado vazio junto com o banner de erro', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 500 }))))
    renderizarNaRota('/componentes/7')
    await screen.findByRole('alert')

    expect(screen.queryByText(/nenhum componente filho/i)).toBeNull()
  })

  it('mostra o código e a descrição do componente no cabeçalho', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText(/CH-100/)).toBeTruthy()
  })
})
```

Escreva `renderizarNaRota(caminho)` **neste arquivo**, montando `MemoryRouter initialEntries={[caminho]}` com a rota `/componentes/:id` — copie a forma de `PedidoDetalhePage.test.tsx`. Se a tela precisar do contexto de auth para renderizar, envolva com o mesmo provedor que `PedidoDetalhePage.test.tsx` usa.

- [ ] **Step 2: Rode e veja falhar**

```bash
cd web && npm test -- --run ComponenteDetalhePage
```

Esperado: **FALHA** — o módulo não existe.

- [ ] **Step 3: Implemente a tela (só leitura)**

`web/src/pages/ComponenteDetalhePage.tsx`. Exigências:

- Começa por `<Pagina titulo={…}>`. **Sem** container próprio, **sem** `min-h-screen`.
- Lê o `id` da rota com `useParams`, converte para número e trata `id` inválido.
- Faz **4 buscas**: o componente (para o cabeçalho) e as 3 receitas. Cada seção guarda **seu próprio** `carregando` / `erro` — assim uma falha em materiais não apaga a lista de filhos que carregou bem.
- Cada seção renderiza, nesta ordem de guarda:
  1. `carregando` → `<EstadoCarregando />`
  2. `erro !== null` → `<BannerDeErro mensagem={mensagemDeErro(erro)} />`
  3. `erro === null && linhas.length === 0` → `<EstadoVazio texto="Nenhum componente filho na receita." />` (e os equivalentes de material e roteiro)
  4. senão, a lista
- **A guarda do vazio inclui `erro === null`.** É o molde de Setores/Materiais/Pedidos, e é o que o teste `não mostra estado vazio junto com o banner de erro` protege.
- O roteiro sai **na ordem que a API mandou** (já vem ordenado por `ordem`), e **não deduplica** por setor.
- Cores só por token.

Esqueleto da seção, para as três seguirem a mesma forma:

O invólucro das três seções, completo. **As quatro guardas são mutuamente exclusivas por construção** — é isso que impede vazio e erro de aparecerem juntos:

```tsx
interface PropsDaSecao {
  titulo: string
  carregando: boolean
  erro: unknown
  /** Quantas linhas a seção tem. Zero + sem erro = estado vazio. */
  quantidade: number
  /** Texto do estado vazio. Distingue "não montei ainda" de "não achei". */
  vazio: string
  children: ReactNode
}

function Secao({ titulo, carregando, erro, quantidade, vazio, children }: PropsDaSecao) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-medium text-tinta">{titulo}</h2>
      {carregando && <EstadoCarregando />}
      {!carregando && erro !== null && <BannerDeErro mensagem={mensagemDeErro(erro)} />}
      {/* `erro === null &&` NÃO é redundante com o `!carregando`: sem ele, uma falha na carga
          mostra o estado vazio JUNTO do banner de erro. É o Critical que a Fase 1D pagou duas
          vezes (Tasks 8 e 10). Não remova. */}
      {!carregando && erro === null && quantidade === 0 && <EstadoVazio texto={vazio} />}
      {!carregando && erro === null && quantidade > 0 && children}
    </section>
  )
}
```

- [ ] **Step 4: Acrescente a rota**

Em `web/src/App.tsx`, logo abaixo de `<Route path="/componentes" element={<ComponentesPage />} />`:

```tsx
        <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
```

- [ ] **Step 5: Acrescente o link na lista de componentes**

Em `web/src/pages/ComponentesPage.tsx`, cada item da lista passa a linkar para `/componentes/${c.id}` — **copie a forma de `PedidosPage.tsx`**, que já faz isso para `/pedidos/:id`, em vez de inventar outra.

Acrescente **um** teste em `web/src/pages/ComponentesPage.test.tsx` provando que o link existe e aponta para o id certo:

```tsx
  it('cada componente linka para a própria página de detalhe', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/componentes': () => respostaJson({
        itens: [{ id: 7, codigo: 'CH-100', descricao: 'Chapa lateral', tipo: 'Fabricado', ativo: true }],
        total: 1,
        pagina: 1,
        tamanho: 20,
      }),
    }))
    renderizarComponentes()

    const link = await screen.findByRole('link', { name: /CH-100/ })
    expect(link.getAttribute('href')).toBe('/componentes/7')
  })
```

`renderizarComponentes()` já existe neste arquivo — reuse o helper que os testes vizinhos usam, com o nome que ele tem lá; se o nome for outro, use o de lá em vez de criar um segundo.

- [ ] **Step 6: Rode e veja passar**

```bash
cd web && npm test -- --run
```

Esperado: **~334 + 8 = ~342 / 31 arquivos**. **Meça o número real.**

```bash
cd web && npm run build
```

- [ ] **Step 7: Prove por mutação a guarda do vazio**

Remova `erro === null &&` da guarda do estado vazio da seção de filhos. Esperado: `não mostra estado vazio junto com o banner de erro` **FALHA**. Reverta e reconfirme.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages web/src/App.tsx
git commit -m "feat(fase1c): pagina de detalhe do Componente com a receita padrao em leitura"
```

---

## Task 11: `ComponenteDetalhePage` — escrita, rascunho e gating

**Files:**
- Modify: `web/src/pages/ComponenteDetalhePage.tsx`
- Modify: `web/src/pages/ComponenteDetalhePage.test.tsx`

**Interfaces:**
- Consumes: a tela da Task 10, `SeletorComBusca` (Task 8), `salvar*Padrao` (Task 9), `usePodeEscrever` (`web/src/auth/usePermissao.ts`), `Botao` / `Campo` (`web/src/components/`).
- Produces: nada para tasks seguintes.

**O desenho, por escrito:** cada seção mantém **rascunho local** e tem **botão Salvar próprio**, que manda a lista inteira daquela seção. Não salva a cada linha. Botão desabilitado quando não há alteração pendente **e** durante a mutação.

**Fronteira declarada (spec §3.2):** sair da página com rascunho não salvo **perde o rascunho**. Não implemente bloqueio de navegação — está fora de escopo, com gatilho escrito na spec §6. Escreva essa fronteira num comentário no topo do componente, para o próximo leitor não achar que é esquecimento.

- [ ] **Step 1: Escreva os testes que falham**

Acrescente ao `ComponenteDetalhePage.test.tsx`, num `describe` novo:

```tsx
describe('ComponenteDetalhePage — escrita', () => {
  it('Salvar manda a lista inteira da seção, não só a linha nova', async () => {
    const posts: unknown[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')

    await adicionarFilho('CH-200', 2)
    await userEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    // A linha que JÁ existia continua no corpo: POST substitui a receita INTEIRA.
    expect(posts[0]).toEqual({
      linhas: [
        { componenteFilhoId: 3, quantidadePadrao: 4 },
        { componenteFilhoId: 2, quantidadePadrao: 2 },
      ],
    })
  })

  it('remover a última linha e salvar manda lista vazia', async () => {
    const posts: unknown[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')

    await userEvent.click(screen.getByRole('button', { name: /remover PA-010/i }))
    await userEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    // Lista vazia é o ÚNICO caminho de remoção que existe — não há DELETE.
    expect(posts[0]).toEqual({ linhas: [] })
  })

  it('Salvar começa desabilitado e habilita quando há alteração pendente', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')

    const salvar = screen.getByRole('button', { name: /salvar componentes filhos/i })
    expect(salvar.hasAttribute('disabled')).toBe(true)

    await adicionarFilho('CH-200', 2)
    expect(salvar.hasAttribute('disabled')).toBe(false)
  })

  it('desabilita Salvar enquanto a gravação está em voo', async () => {
    let liberar: (r: Response) => void = () => {}
    vi.stubGlobal('fetch', fetchComPostPendente((r) => { liberar = r }))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    const salvar = screen.getByRole('button', { name: /salvar componentes filhos/i })
    await userEvent.click(salvar)
    // Em voo: `sujo` ainda é true, então o que desabilita AQUI só pode ser `salvando`.
    expect(salvar.hasAttribute('disabled')).toBe(true)

    // Depois de responder, o botão continua desabilitado — mas agora por `!sujo`. Como as duas
    // causas produzem o mesmo atributo, o que se afirma no fim é que a resposta foi PROCESSADA:
    // a linha nova aparece na lista.
    liberar(respostaJson([
      { id: 1, componenteFilhoId: 3, codigo: 'PA-010', descricao: 'Parafuso M8', quantidadePadrao: 4 },
      { id: 9, componenteFilhoId: 2, codigo: 'CH-200', descricao: 'Chapa frontal', quantidadePadrao: 2 },
    ]))
    expect(await screen.findByText('CH-200')).toBeTruthy()
  })

  it('o erro de gravação aparece e a lista da tela não some', async () => {
    vi.stubGlobal('fetch', fetchComPostQueFalha(400, 'Esta receita criaria um ciclo.'))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    await userEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByText('PA-010')).toBeTruthy()
  })

  /** Gating na AÇÃO, não no link: Operador LÊ a receita, não a edita. */
  it('Operador vê a receita mas não vê formulário nem Salvar', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7', 'Operador')

    expect(await screen.findByText('PA-010')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /salvar/i })).toBeNull()
  })

  /**
   * O 403 é a fronteira REAL — esconder botão não é segurança. Se o backend recusar mesmo com o
   * botão visível, a tela mostra a recusa em vez de quebrar.
   */
  it('403 na gravação vira mensagem, não exceção', async () => {
    vi.stubGlobal('fetch', fetchComPostQueFalha(403, ''))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    await userEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    expect(await screen.findByRole('alert')).toBeTruthy()
  })

  it('o roteiro é salvo na ordem da tela, só com setorId', async () => {
    const posts: unknown[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7', 'PCP')
    await screen.findByText('Solda')

    await userEvent.click(screen.getByRole('button', { name: /remover.*Solda/i }))
    await userEvent.click(screen.getByRole('button', { name: /salvar roteiro/i }))

    // Sem `ordem` no corpo: quem numera é o servidor, pela posição.
    expect(posts[0]).toEqual({ linhas: [{ setorId: 20 }, { setorId: 20 }] })
  })
})
```

Escreva os helpers `fetchPorRotaGravando`, `fetchComPostPendente`, `fetchComPostQueFalha` e `adicionarFilho` **neste arquivo**, sobre `fetchPorRota` de `web/src/testes/api.ts`. `renderizarNaRota` ganha um segundo parâmetro com o **perfil**, para o gating ser testável — veja como `SetoresPage.test.tsx` monta o contexto de auth por perfil e siga.

- [ ] **Step 2: Rode e veja falhar**

```bash
cd web && npm test -- --run ComponenteDetalhePage
```

Esperado: os 8 testes novos **FALHAM** (não há botão Salvar).

- [ ] **Step 3: Implemente a escrita**

Exigências:

- Cada seção tem `rascunho` (array local, inicializado com o que a API devolveu) e `sujo` (rascunho ≠ carregado).
- **Filhos:** linha nova = `<SeletorComBusca>` + campo de quantidade + botão "Adicionar". Cada linha existente tem botão "Remover **{código}**" — o nome acessível **inclui o código**, senão a tela com 10 linhas tem 10 botões chamados "Remover".
- **Materiais:** `<select>` nativo de materiais (lista curta) + quantidade. Carregue os materiais com `listarMateriais(false)` de `cadastros.ts`.
- **Roteiro:** `<select>` nativo de setores + botão "Adicionar passo". **Sem campo de ordem** — a posição na lista é a ordem. Remover uma linha do meio não renumera nada na tela: o servidor renumera ao salvar.
- Cada seção: `<Botao>` "Salvar componentes filhos" / "Salvar materiais" / "Salvar roteiro", `disabled={!sujo || salvando}`.
- Erro de gravação em `BannerDeErro` **da seção**, via `mensagemDeErro`, **sem** apagar a lista já carregada.
- Todo `salvar*` dentro de `try/catch` — o `403` é a fronteira real.
- `usePodeEscrever('receitaPadrao')` esconde formulário e botões de Salvar. A **lista continua visível**.

- [ ] **Step 4: Rode e veja passar**

```bash
cd web && npm test -- --run
```

Esperado: **~342 + 8 = ~350 / 31 arquivos**. **Meça e propague.**

```bash
cd web && npm run build
```

- [ ] **Step 5: As três mutações desta task**

**A (substituição):** faça o Salvar de filhos mandar **só as linhas novas**. Esperado: `Salvar manda a lista inteira da seção` **FALHA**.
**B (gating):** troque `usePodeEscrever('receitaPadrao')` por `true` fixo. Esperado: `Operador vê a receita mas não vê formulário nem Salvar` **FALHA**.
**C (ordem do roteiro):** faça o Salvar do roteiro mandar `{ setorId, ordem }`. Esperado: `o roteiro é salvo na ordem da tela, só com setorId` **FALHA**.

Reverta as três e reconfirme o total.

- [ ] **Step 6: Commit**

```bash
git add web/src/pages
git commit -m "feat(fase1c): escrita da receita padrao com rascunho por secao e gating de perfil"
```

---

## Task 12: Verificação manual no navegador

Só aqui a fase prova que funciona **de verdade**. A massa da Task 7 é o que torna isto possível.

**Files:** nenhum (a menos que a verificação ache defeito).

- [ ] **Step 1: Suba tudo**

Docker, API .NET e Vite. Use `.claude/launch.json`, que já existe.

- [ ] **Step 2: V1 — a busca do `SeletorComBusca` com catálogo de verdade**

Logue como `pcp`, abra um componente e, na seção de filhos, digite **parte de um código** e depois **parte de uma descrição**. Confirme: as duas buscas filtram; a lista não pisca com resultado de busca antiga; teclado (`↓`, `Enter`) seleciona.

- [ ] **Step 3: V2 — o ciclo é recusado na tela**

Monte `A → B` e depois tente `B → A`. Confirme que a mensagem de ciclo aparece **na seção**, legível, e que a lista de B não sumiu.

- [ ] **Step 4: V3 — retorno ao setor sobrevive à ida e volta**

Monte um roteiro com o mesmo setor duas vezes, salve, recarregue com F5. Confirme que as **duas** passagens continuam lá, na ordem certa.

- [ ] **Step 5: V4 — remoção pela lista vazia**

Remova todas as linhas de uma seção, salve, F5. Confirme que a receita está vazia e que o estado vazio aparece com o texto certo.

- [ ] **Step 6: V5 — gating por perfil**

Logue como um perfil sem escrita e confirme: a receita **aparece**, e o formulário e os Salvar **não**.

- [ ] **Step 7: V6 — viewport**

`320`, `767`, `768` e `1280 px`. Confirme que **nenhuma** parte da tela rola na horizontal. Use o mesmo snippet localizador da Fase 1D (spec §11 daquela fase), que já existe.

- [ ] **Step 8: Registre o que NÃO conseguiu verificar**

Se algum passo não puder ser feito (painel de composição indisponível, por exemplo), **escreva isso no relatório**. **Não fabrique evidência visual que você não tem** — a Task 12 da Fase 1D deixou V3/V5 como parciais e declarou; foi a atitude certa.

- [ ] **Step 9: Commit (só se a verificação achou defeito)**

Se achou, corrija **com teste que morre sem a correção**, e commite. Se não achou, não há commit nesta task — o produto dela é o relatório.

---

## Task 13: Documentação

**Files:**
- Modify: `specs/05-api-endpoints.md` · `specs/06-roadmap-mvp.md` · `specs/01-dominio-e-regras-de-negocio.md` · `CLAUDE.md`

- [ ] **Step 1: `specs/05-api-endpoints.md`**

Substitua as três linhas "**Fase 1C**, não implementado" pelo contrato real: corpo `{ linhas: [...] }`, perfis *(Administrador, PCP)* na escrita e leitura para qualquer autenticado, `200` no POST (não `201`), e as três notas do contrato — **ordem atribuída pelo servidor**, **setor pode repetir**, **lista vazia apaga**.

- [ ] **Step 2: `specs/06-roadmap-mvp.md`**

O bloco "**Falta 1C**" vira "**1C concluída**", com o que ela entregou e a data. **Não** invente que a Fase 2 está destravada além do que é verdade: ela passa a ter o que copiar, e é isso.

- [ ] **Step 3: `specs/01-dominio-e-regras-de-negocio.md`**

Duas regras novas, numeradas no padrão do arquivo:
1. **A receita padrão não pode conter ciclo** em nenhuma profundidade — com a razão (a Fase 2 copia recursivamente) e a nuance de que a verificação é sobre o grafo **resultante**, para que consertar um ciclo seja possível.
2. **Setor repetido no roteiro é permitido e significa retorno ao setor.** Esta é a regra que impede alguém de "corrigir" o comportamento no futuro.

- [ ] **Step 4: `CLAUDE.md`**

- Bloco de pré-requisito dos testes: acrescente `db/seed-demo.sql`, **com a distinção** entre ele e o `db/seed.sql` escrita — senão o próximo leitor vai supor que a suíte depende do demo.
- Registre que **o banco de dev é descartável** (autorização de 2026-08-17) e que a restrição antiga "não derrubar o SQL Server" **não vale mais**.
- Se o `SeletorComBusca` virou o padrão para escolher item de catálogo grande, acrescente uma linha na seção de interface, **com o gatilho** (catálogo paginado).

- [ ] **Step 5: Confira que nenhuma frase promete mais do que foi medido**

Releia o que escreveu procurando frases de fechamento absolutas ("nenhum X escapa", "todos os casos"). Se houver, **ou** meça o escopo que ela declara, **ou** troque por uma frase que descreva o que de fato se verificou. Este projeto já pagou esse vício duas vezes.

- [ ] **Step 6: Commit**

```bash
git add specs CLAUDE.md
git commit -m "docs(fase1c): contrato da receita padrao nas specs e no CLAUDE.md"
```

---

## Task 14: Varredura de segredo e PR

- [ ] **Step 1: Suíte inteira, nas duas pontas**

```bash
dotnet test Rastreamento.slnx
```

```bash
cd web && npm test -- --run && npm run build
```

```bash
dotnet build Rastreamento.slnx -warnaserror
```

Registre os **números medidos**. Se divergirem das previsões deste plano, **o número medido ganha** — anote a divergência no relatório em vez de a esconder.

- [ ] **Step 2: Varredura de segredo sobre o HISTÓRICO**

**O histórico, não a árvore** — o repositório é **público**, e o push publica o histórico inteiro. É a forma estabelecida pela Task 13 da Fase 1D:

```bash
git log -p origin/main..HEAD | grep -nEi "password|senha|secret|token|api[_-]?key|connectionstring|bearer" | head -60
```

Triar cada casamento. `Your_strong_Pass123` e `Admin@123` são credenciais de **desenvolvimento local já versionadas** no repositório (`docker-compose.yml`, `db/seed.sql`, `CLAUDE.md`) — não são vazamento novo, mas **confirme** que nada além disso apareceu, em especial no `db/seed-demo.sql` novo.

- [ ] **Step 3: Push e PR**

```bash
git push -u origin fase-1c-receita-padrao
```

Abra o PR com `gh pr create`, descrevendo: as 6 decisões da spec, os deltas de teste **medidos**, as mutações que foram derrubadas, e o que ficou **fora de escopo com gatilho**. Commit, push e abrir PR são nossos; **aprovar e mesclar é do usuário**.

---

## Resumo dos deltas previstos

| Task | Projeto | De → Para | Δ |
|---|---|---|---|
| 1 | Infrastructure | 38 → 43 | +5 |
| 2 | Infrastructure | 43 → **57** (medido; previsto 48) | **+14** |
| 3 | Application | 129 → **142** (medido; previsto 139) | **+13** |
| 3-fix | Application + Infrastructure | 142 → **150** e 57 → **58** (fix pass da review) | **+9** |
| 4 | Application | 150 → **160** (medido; previsto 156) | **+10** |
| 4-fix | Application | 160 → **161** (fix pass da review) | **+1** |
| 5 | Application | 161 → 173 | +12 |
| 6 | Api | 171 → 181 | +10 |
| 7 | — | — | 0 |
| 8 | front | 317 → 325 | +8 |
| 9 | front | 325 → ~334 | ~+9 |
| 10 | front | ~334 → ~342 | ~+8 |
| 11 | front | ~342 → ~350 | ~+8 |

**Backend: 338 → 412** (o plano original dizia 385; a Task 2 fechou +9 acima do previsto, a Task 3 +3, o
fix pass da review da Task 3 somou +9, a Task 4 +4, o fix pass da review da Task 4 somou +1 em código
(N1, 160 → 161) e mais +1 na previsão da Task 5 (P1, 172 → 173) — os
deltas estão corrigidos nas linhas acima e o total já reflete isso). **Front: 317 → ~350.**

Os números do front a partir da Task 9 são **aproximados de propósito**: a guarda `permissoesEspelhamOBackend` usa `it.each` sobre o mapa de recursos, então acrescentar um recurso muda a contagem por um caminho que este plano não consegue prever com exatidão sem rodar. **Meça na Task 9 e corrija as previsões das Tasks 10 e 11 na mesma passada** — total absoluto herdado propaga erro task a task.
