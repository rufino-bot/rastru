# Fase 2 — Estrutura recursiva: plano de implementação

> **Para quem executa com agentes:** SUB-SKILL OBRIGATÓRIA — use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para implementar task a task. Os passos usam checkbox (`- [ ]`) para acompanhamento.

**Goal:** fazer nascer a árvore `EstruturaItem` — copiada da receita padrão de um `Componente` ou montada à mão — e a tela que a monta, deixando o critério de pronto do roadmap satisfeito: dá para montar visualmente a árvore completa de uma Peça complexa.

**Architecture:** três entidades EF novas espelhando o lado do catálogo (`EstruturaItem`/`EstruturaMaterial`/`EstruturaRoteiro` ↔ `Componente`/`ComponenteMaterialPadrao`/`ComponenteRoteiroPadrao`). A cópia recursiva é separada em duas metades: um **planejador puro**, sem I/O, que transforma o grafo de receita numa árvore de nós planejados (e é onde moram a guarda de ciclo, o teto e a multiplicação de quantidade), e um **caso de uso** que persiste esse plano numa transação. O front ganha uma rota `/agrupamentos/:id` com uma primitiva de árvore própria.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core Database First contra SQL Server, xUnit; React + TypeScript (Vite), Vitest + Testing Library, Tailwind com tokens próprios.

**Spec:** `docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`. Em conflito entre este plano e a spec, **a spec ganha** — e o conflito é achado a registrar, não a resolver em silêncio.

**Branch:** `fase-2-estrutura-recursiva`, já criada a partir de `9ec40e6` e empurrada. A spec é o único arquivo dela hoje.

---

## Global Constraints

Valem para **toda** task; nenhuma as repete.

- **Database First.** Schema nasce em `specs/02-modelo-de-dados.sql`, depois o mapeamento EF, depois `01-dominio-e-regras-de-negocio.md` se virar regra. **Nunca `Add-Migration` / `EnsureCreated`.**
- **Nomes de domínio em português**, espelhando o DDL (`EstruturaItem`, `EstruturaMaterial`, `EstruturaRoteiro`). Nomes técnicos em inglês (`Repository`, `UseCase`, `DTO`, `Controller`).
- **`dotnet build Rastreamento.slnx -warnaserror` tem de ficar em 0 warnings.** Não é opcional.
- **`npm run build` faz parte do ciclo do front**, não só `npm test`: erro de tipo em `.test.tsx` quebra o build sem quebrar a suíte (o Vitest não faz typecheck).
- **Rota de API nunca escreve `/api`** no call site do front — quem prefixa é o `rota()` de `web/src/api/client.ts`. No backend, nenhuma rota declara o prefixo: o `UsePathBase` cuida disso.
- **Cores só por token** de `web/src/index.css`. `text-gray-*` e afins são reprovados por `web/src/tema/semCorForaDaPaleta.test.ts`.
- **Verde (`positivo`) e vermelho (`negativo`) são reservados a estado** (aprovado/ativo, reprovado/perda/erro). Nó ad-hoc, nó de catálogo e nó folha se distinguem por forma, peso ou rótulo — **nunca** por essas duas cores.
- **Teste de tela** usa `web/src/testes/api.ts` (`respostaJson`, `fetchPorRota`), declara `// @vitest-environment jsdom` no topo e chama `afterEach(cleanup)` explicitamente — este projeto não usa `globals: true`.
- **Tela nova começa por `<Pagina titulo="…">`** dentro da rota de layout do `AppShell`. Nada de `min-h-screen` numa tela.
- **Não escreva campo, botão, banner, item de lista, pílula, paginação, estado vazio ou de carregando à mão** — as primitivas estão em `web/src/components/`.
- **Toda tela que busca dados tem os três estados** (carregando, vazio com texto que distingue "não achei" de "não há nada", erro via `mensagemDeErro`), **cada um com teste que morre se o estado sumir**.
- **Paralelismo do xUnit contra um banco só:** teste novo que escreve numa tabela já escrita por outra classe entra na mesma `[Collection]` daquela tabela. **Asserção sobre contagem global de tabela compartilhada é flaky por construção** — escope por prefixo próprio do teste, ou afirme só o que é monótono.
- **Delta de teste, nunca total absoluto.** Cada task declara quantos testes ela acrescenta. Nenhuma task escreve "a suíte fica com N testes" — total absoluto propaga erro task a task.

---

## Task 0: Medir a bancada (antes de qualquer código)

Não produz código. Existe porque a baseline da spec é de **antes** da primeira linha da fase, e copiar número velho para dentro de um plano é dívida composta.

- [ ] **Passo 1: subir o banco**

```bash
docker compose up -d
```

- [ ] **Passo 2: medir o backend**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
```

Esperado: build com `0 Aviso(s), 0 Erro(s)`; suíte verde. **Anote os três números por assembly** (Application, Infrastructure, Api) — eles são a base dos deltas das tasks seguintes.

- [ ] **Passo 3: medir o front**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: suíte verde, build sem erro de tipo. **Anote testes e arquivos.**

- [ ] **Passo 4: registrar**

Escreva os números medidos no relatório da task e no ledger. Se divergirem da spec §0, **o medido ganha** e as tasks seguintes usam o medido.

---

## File Structure

O que nasce e o que muda, com a responsabilidade de cada arquivo.

**Domínio** (`src/Rastreamento.Domain/`)
- `Entities/EstruturaItem.cs` — o nó da árvore. Autorreferenciado; sem comportamento.
- `Entities/EstruturaMaterial.cs` — material de um nó.
- `Entities/EstruturaRoteiro.cs` — passo de setor de um nó, com `Ordem`.
- `Abstractions/IEstruturaRepository.cs` — contrato de leitura da receita e de gravação da árvore.
- `Abstractions/IAgrupamentoRepository.cs` — **modificado**: o comentário de `TemEstruturaAsync` perde a promessa cumprida.

**Aplicação** (`src/Rastreamento.Application/Estrutura/`)
- `PlanejadorDeCopia.cs` — **puro, sem I/O.** Grafo de receita → árvore planejada, ou erro. É onde vivem ciclo-por-caminho, teto e multiplicação de quantidade. Testável sem fake nenhum.
- `MontagemDeEstruturaUseCase.cs` — orquestra: lê a receita pelo repositório, chama o planejador, persiste em transação. Também acrescenta sub-Item, edita e exclui nó.
- `EstruturaDtos.cs` — DTOs de entrada e saída.

**Infraestrutura** (`src/Rastreamento.Infrastructure/Persistence/`)
- `Configurations/EstruturaItemConfiguration.cs`, `EstruturaMaterialConfiguration.cs`, `EstruturaRoteiroConfiguration.cs`
- `EstruturaRepository.cs` — implementação.
- `RastreamentoDbContext.cs` — **modificado**: três `DbSet`.
- `AgrupamentoRepository.cs` — **modificado**: `TemEstruturaAsync` vira LINQ.

**API** (`src/Rastreamento.Api/Controllers/`)
- `EstruturaController.cs` — os cinco endpoints. Controller próprio, como `ReceitaPadraoController`.

**Front** (`web/src/`)
- `api/estrutura.ts` — cliente e tipos da árvore. **Arquivo próprio**, não dentro de `cadastros.ts`, que já serve cinco cadastros.
- `components/ArvoreDeEstrutura.tsx` + teste — a primitiva de lista indentada.
- `pages/AgrupamentoDetalhePage.tsx` + teste — a tela.
- `App.tsx` — **modificado**: rota `/agrupamentos/:id`.
- `pages/PedidoDetalhePage.tsx` — **modificado**: o item da lista vira link.
- `auth/permissoes.ts` e `auth/permissoesEspelhamOBackend.test.ts` — **modificados**: o `Recurso` novo.

**Specs e docs** — `02-modelo-de-dados.sql`, `05-api-endpoints.md`, `06-roadmap-mvp.md`, `01-dominio-e-regras-de-negocio.md`, `CLAUDE.md`.

---

## Task 1: Schema, entidades EF, e o consumidor que estava esperando

**Files:**
- Modify: `specs/02-modelo-de-dados.sql:210-213`
- Create: `src/Rastreamento.Domain/Entities/EstruturaItem.cs`, `EstruturaMaterial.cs`, `EstruturaRoteiro.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/EstruturaItemConfiguration.cs`, `EstruturaMaterialConfiguration.cs`, `EstruturaRoteiroConfiguration.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs:20`
- Modify: `src/Rastreamento.Infrastructure/Persistence/AgrupamentoRepository.cs:37-51`
- Modify: `src/Rastreamento.Domain/Abstractions/IAgrupamentoRepository.cs:22-28`
- Modify: `CLAUDE.md` (bloco de `ALTER` idempotente)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaItemMapeamentoTests.cs`

**Interfaces:**
- Consumes: nada de tasks anteriores.
- Produces: `EstruturaItem { int Id; int AgrupamentoId; int? ComponenteId; string? Descricao; int? EstruturaPaiId; string NivelHierarquico; decimal Quantidade; bool RequerRelatorioDimensional; }`; `EstruturaMaterial { int Id; int EstruturaItemId; int MaterialId; decimal Quantidade; }`; `EstruturaRoteiro { int Id; int EstruturaItemId; int SetorId; int Ordem; }`. `DbSet`s: `Estruturas`, `EstruturaMateriais`, `EstruturaRoteiros`.

**Delta de teste desta task: +4.**

- [ ] **Passo 1: a constraint sai de comentário no `.sql`**

Em `specs/02-modelo-de-dados.sql`, dentro de `CREATE TABLE dbo.EstruturaItem`, o bloco de comentário que hoje começa em `-- PENDENTE (Fase 2, decidido em 2026-08-04 ...` perde a parte "Constraint a acrescentar nesta tabela" e as duas linhas comentadas viram DDL de verdade, junto das outras constraints ao fim da tabela:

```sql
    CONSTRAINT CK_EstruturaItem_PecaTemComponente
        CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)
```

O comentário acima de `ComponenteId` fica, reescrito para o presente — ele explica **por que** a coluna é nullable, o que continua verdadeiro:

```sql
    -- Nullable: item 100% ad-hoc, sem base no catalogo. So um Item (no com pai) pode ser ad-hoc --
    -- uma Peca sempre referencia um Componente, senao o solido (que mora em Componente) nao tem
    -- onde ser pendurado. Ver regra 18 em 01, e CK_EstruturaItem_PecaTemComponente abaixo.
```

- [ ] **Passo 2: aplicar a constraint no banco desta bancada**

O banco foi regenerado em 2026-08-04 a partir deste `.sql`, quando a constraint ainda era comentário — **ela não está lá**. Este `ALTER` é de verdade, não no-op:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EstruturaItem_PecaTemComponente') ALTER TABLE dbo.EstruturaItem ADD CONSTRAINT CK_EstruturaItem_PecaTemComponente CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL);"
```

Esperado: sem erro. Rodar de novo também não deve dar erro (é idempotente).

- [ ] **Passo 3: acrescentar esse bloco ao `CLAUDE.md`**

Na seção "Comandos", junto dos outros `ALTER` idempotentes, com uma frase que diga **por que este é diferente**:

```markdown
Na Fase 2 entra a constraint que fecha a regra 18 no schema. **Diferente dos quatro blocos acima,
este NÃO é no-op nesta máquina:** o banco foi regenerado em 2026-08-04 a partir do `.sql`, e naquela
data a constraint ainda era comentário.
```

Seguido do bloco `bash` do Passo 2.

- [ ] **Passo 4: escrever as três entidades**

`src/Rastreamento.Domain/Entities/EstruturaItem.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// A arvore REAL usada num Agrupamento — copiada do catalogo (`Componente`) e customizavel.
/// Recursiva numa tabela so: no sem pai e uma PECA, no com pai e um ITEM. Nao existem tabelas
/// separadas (ver `01-dominio-e-regras-de-negocio.md`, regra 3).
///
/// `Quantidade` e o lote AGREGADO e ABSOLUTO daquele no — nao a razao por unidade do pai. Uma Peca
/// de 10 cuja receita diz "4 por unidade" gera filho com 40. A Fase 3 aponta setor por
/// EstruturaItem, e o operador movimenta 40 suportes, nao "4 por pai".
/// </summary>
public class EstruturaItem
{
  public int Id { get; set; }
  public int AgrupamentoId { get; set; }

  /// <summary>
  /// NULL so em Item ad-hoc. Peca sempre referencia um Componente —
  /// `CK_EstruturaItem_PecaTemComponente` garante o gancho no banco.
  /// </summary>
  public int? ComponenteId { get; set; }

  /// <summary>Nome proprio do no. NULL herda a descricao do Componente (regra 19).</summary>
  public string? Descricao { get; set; }

  /// <summary>Self-FK. NULL = Peca (topo da arvore dentro do Agrupamento).</summary>
  public int? EstruturaPaiId { get; set; }

  /// <summary>Peca | Item — denormalizado para consulta rapida.</summary>
  public string NivelHierarquico { get; set; } = string.Empty;

  public decimal Quantidade { get; set; }

  /// <summary>Vale para Peca; o cliente exige no cadastro do Pedido (regra 10).</summary>
  public bool RequerRelatorioDimensional { get; set; }
}
```

`EstruturaMaterial.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>Material de um no da arvore. Copiado de `ComponenteMaterialPadrao` na criacao.</summary>
public class EstruturaMaterial
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public int MaterialId { get; set; }
  public decimal Quantidade { get; set; }
}
```

`EstruturaRoteiro.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Passo de setor de um no. Copiado de `ComponenteRoteiroPadrao`, com a `Ordem` PRESERVADA:
/// setor repetido e RETORNO ao mesmo setor, nao duplicata (regra 21), e a unicidade do schema e
/// (EstruturaItemId, Ordem) — da posicao, nao do Setor. Reindexar a Ordem na copia perderia o
/// retorno.
/// </summary>
public class EstruturaRoteiro
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public int SetorId { get; set; }
  public int Ordem { get; set; }
}
```

- [ ] **Passo 5: as três configurations**

`EstruturaItemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaItemConfiguration : IEntityTypeConfiguration<EstruturaItem>
{
  public void Configure(EntityTypeBuilder<EstruturaItem> b)
  {
    b.ToTable("EstruturaItem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Descricao).HasMaxLength(200);
    b.Property(x => x.NivelHierarquico).HasMaxLength(10).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}
```

`EstruturaMaterialConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaMaterialConfiguration : IEntityTypeConfiguration<EstruturaMaterial>
{
  public void Configure(EntityTypeBuilder<EstruturaMaterial> b)
  {
    b.ToTable("EstruturaMaterial");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}
```

`EstruturaRoteiroConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class EstruturaRoteiroConfiguration : IEntityTypeConfiguration<EstruturaRoteiro>
{
  public void Configure(EntityTypeBuilder<EstruturaRoteiro> b)
  {
    b.ToTable("EstruturaRoteiro");
    b.HasKey(x => x.Id);
  }
}
```

- [ ] **Passo 6: os três `DbSet`**

Em `RastreamentoDbContext.cs`, depois de `Agrupamentos`:

```csharp
  public DbSet<EstruturaItem> Estruturas => Set<EstruturaItem>();
  public DbSet<EstruturaMaterial> EstruturaMateriais => Set<EstruturaMaterial>();
  public DbSet<EstruturaRoteiro> EstruturaRoteiros => Set<EstruturaRoteiro>();
```

- [ ] **Passo 7: escrever os quatro testes de mapeamento — e vê-los falhar**

`tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaItemMapeamentoTests.cs`. Siga o molde de `ComponenteMappingTests` para obter o `DbContext` (mesma fixture, mesma connection string). **Cada teste cria os próprios dados com um prefixo único e afirma só sobre eles** — nunca sobre contagem global.

Os quatro:

1. `Peca_e_Item_gravam_e_leem_com_a_autorreferencia` — grava uma Peça (`EstruturaPaiId = null`, `NivelHierarquico = "Peca"`) e um Item filho dela; relê e confere que `EstruturaPaiId` do Item aponta para o `Id` da Peça.
2. `Peca_sem_Componente_e_recusada_pelo_banco` — tenta gravar `NivelHierarquico = "Peca"` com `ComponenteId = null` e espera `DbUpdateException`. **Este é o teste da constraint do Passo 1.**
3. `Item_sem_Componente_e_aceito` — o ad-hoc. Mesma coisa com `NivelHierarquico = "Item"` e um pai válido: grava sem exceção.
4. `Roteiro_preserva_ordem_com_setor_repetido` — grava dois `EstruturaRoteiro` para o mesmo nó, com o **mesmo `SetorId`** e `Ordem` 1 e 3; relê ordenado por `Ordem` e confere que os dois vieram, na ordem. É a regra 21 no mapeamento.

Rode:

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests --filter EstruturaItemMapeamentoTests
```

Esperado: **falha de compilação** (as entidades ainda não existem) se você escrever o teste antes dos Passos 4-6, ou falha de execução se escrever depois. Se o teste 2 passar **antes** do Passo 2 ter sido aplicado, pare: significa que a constraint não está no banco e o teste está afirmando outra coisa.

- [ ] **Passo 8: rodar e ver passar**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests --filter EstruturaItemMapeamentoTests
```

Esperado: 4 aprovados.

- [ ] **Passo 9: `TemEstruturaAsync` vira LINQ**

Em `AgrupamentoRepository.cs`, o método inteiro (e o `<remarks>` acima dele) vira:

```csharp
  public Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct) =>
      _db.Estruturas.AnyAsync(e => e.AgrupamentoId == agrupamentoId, ct);
```

Em `IAgrupamentoRepository.cs`, o `<summary>` de `TemEstruturaAsync` perde a promessa já cumprida. Fica:

```csharp
  /// <summary>
  /// Existe alguma EstruturaItem apontando para este Agrupamento? E a guarda do DELETE.
  /// Ate a Fase 2 isto era SQL direto, porque `EstruturaItem` nao tinha entidade mapeada; a Fase 2
  /// mapeou, e a implementacao virou LINQ sem o contrato mudar — que era exatamente o previsto.
  /// </summary>
  Task<bool> TemEstruturaAsync(int agrupamentoId, CancellationToken ct);
```

- [ ] **Passo 10: provar que o teste que já existia continua verde**

O teste de `TemEstruturaAsync` existe desde a 1B. Ache-o e rode:

```bash
grep -rn "TemEstrutura" tests/
dotnet test Rastreamento.slnx --no-build --filter "FullyQualifiedName~Agrupamento"
```

Esperado: verde. **Este é o ponto da task:** o mapeamento nasceu com verificação de um contrato que já existia, sem teste novo.

- [ ] **Passo 11: build limpo e suíte inteira**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
```

Esperado: 0 warnings; delta de **+4** sobre o número da Task 0.

- [ ] **Passo 12: commit**

```bash
git add specs/02-modelo-de-dados.sql CLAUDE.md src/Rastreamento.Domain src/Rastreamento.Infrastructure tests/Rastreamento.Infrastructure.Tests
git commit -m "feat(fase-2): mapeia EstruturaItem e fecha a regra 18 no schema"
```

---

## Task 2: `PlanejadorDeCopia` — a cópia recursiva, pura e sem I/O

O coração da fase, e de propósito **sem banco, sem repositório, sem fake**: recebe o grafo de receita já lido e devolve a árvore planejada ou um erro. É aqui que a review vai bater.

**Files:**
- Create: `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`
- Test: `tests/Rastreamento.Application.Tests/Estrutura/PlanejadorDeCopiaTests.cs`

**Interfaces:**
- Consumes: nada — puro.
- Produces:

```csharp
public sealed record NoPlanejado(
    int? ComponenteId,
    string? Descricao,
    decimal Quantidade,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoPlanejado> Filhos);

public sealed record ReceitaDoCatalogo(
    ILookup<int, (int FilhoId, decimal QuantidadePadrao)> Filhos,
    ILookup<int, (int MaterialId, decimal QuantidadePadrao)> Materiais,
    ILookup<int, (int SetorId, int Ordem)> Roteiro);

public sealed record PlanoDeCopia(NoPlanejado? Raiz, string? Erro, string? CodigoDoErro);

public static class PlanejadorDeCopia
{
  public const int ProfundidadeMaxima = 20;
  public const int NosMaximos = 500;
  public const string CodigoDeCiclo = "CicloNaReceita";
  public const string CodigoDeProfundidade = "EstruturaProfundaDemais";
  public const string CodigoDeTamanho = "EstruturaGrandeDemais";

  public static PlanoDeCopia Planejar(
      ReceitaDoCatalogo receita, int componenteRaizId, decimal quantidadeDaRaiz);
}
```

**Delta de teste desta task: +9.**

- [ ] **Passo 1: escrever os nove testes, e vê-los falhar**

`tests/Rastreamento.Application.Tests/Estrutura/PlanejadorDeCopiaTests.cs`. Um helper local monta a receita:

```csharp
private static ReceitaDoCatalogo Receita(
    (int Pai, int Filho, decimal Qtd)[] filhos,
    (int Comp, int Material, decimal Qtd)[]? materiais = null,
    (int Comp, int Setor, int Ordem)[]? roteiro = null) =>
    new(filhos.ToLookup(f => f.Pai, f => (f.Filho, f.Qtd)),
        (materiais ?? []).ToLookup(m => m.Comp, m => (m.Material, m.Qtd)),
        (roteiro ?? []).ToLookup(r => r.Comp, r => (r.Setor, r.Ordem)));
```

Os nove:

1. **`Raiz_sem_receita_gera_um_no_so`**

```csharp
var plano = PlanejadorDeCopia.Planejar(Receita([]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

Assert.Null(plano.Erro);
Assert.Equal(1, plano.Raiz!.ComponenteId);
Assert.Equal(10m, plano.Raiz.Quantidade);
Assert.Empty(plano.Raiz.Filhos);
```

2. **`Quantidade_do_filho_e_multiplicada_pela_do_pai`** — a D3, e a mutação mais óbvia a matar.

```csharp
var plano = PlanejadorDeCopia.Planejar(
    Receita([(1, 2, 4m)]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

Assert.Equal(40m, plano.Raiz!.Filhos.Single().Quantidade);
```

3. **`A_multiplicacao_acumula_nivel_a_nivel`** — 10 × 4 × 3 = 120. Sem isto, uma implementação que multiplicasse só o primeiro nível passaria no teste 2.

```csharp
var plano = PlanejadorDeCopia.Planejar(
    Receita([(1, 2, 4m), (2, 3, 3m)]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

Assert.Equal(120m, plano.Raiz!.Filhos.Single().Filhos.Single().Quantidade);
```

4. **`Diamante_e_aceito_o_mesmo_componente_sob_dois_pais`** — **o teste que morre se a guarda virar "já visto"**. Não é caso hipotético: o `seed-demo` gravado tem exatamente isto (medido em 2026-08-29, 1 Componente sob mais de um pai).

```csharp
// 1 -> 2, 1 -> 3, e tanto 2 quanto 3 usam o componente 4.
var plano = PlanejadorDeCopia.Planejar(
    Receita([(1, 2, 1m), (1, 3, 1m), (2, 4, 5m), (3, 4, 7m)]), 1, 1m);

Assert.Null(plano.Erro);
var ramos = plano.Raiz!.Filhos.OrderBy(f => f.ComponenteId).ToList();
Assert.Equal(5m, ramos[0].Filhos.Single().Quantidade);
Assert.Equal(7m, ramos[1].Filhos.Single().Quantidade);
```

5. **`Ciclo_direto_e_recusado`** — 1 → 2 → 1.

```csharp
var plano = PlanejadorDeCopia.Planejar(Receita([(1, 2, 1m), (2, 1, 1m)]), 1, 1m);

Assert.Null(plano.Raiz);
Assert.Equal(PlanejadorDeCopia.CodigoDeCiclo, plano.CodigoDoErro);
```

6. **`Ciclo_indireto_e_recusado_e_a_mensagem_nomeia_o_caminho`** — 1 → 2 → 3 → 2. A mensagem tem de conter os ids do trecho cíclico; sem isso não há como consertar.

```csharp
var plano = PlanejadorDeCopia.Planejar(Receita([(1, 2, 1m), (2, 3, 1m), (3, 2, 1m)]), 1, 1m);

Assert.Equal(PlanejadorDeCopia.CodigoDeCiclo, plano.CodigoDoErro);
Assert.Contains("2", plano.Erro);
Assert.Contains("3", plano.Erro);
```

7. **`Profundidade_acima_do_teto_e_recusada`** — corrente 1 → 2 → … → 22, mais funda que `ProfundidadeMaxima`.

```csharp
var corrente = Enumerable.Range(1, 22).Select(i => (i, i + 1, 1m)).ToArray();
var plano = PlanejadorDeCopia.Planejar(Receita(corrente), 1, 1m);

Assert.Equal(PlanejadorDeCopia.CodigoDeProfundidade, plano.CodigoDoErro);
```

8. **`Corrente_dentro_do_teto_e_aceita`** — o par do anterior. Uma corrente de profundidade 5 passa. Sem este teste, um teto de 1 passaria no teste 7.

```csharp
var corrente = Enumerable.Range(1, 5).Select(i => (i, i + 1, 1m)).ToArray();
var plano = PlanejadorDeCopia.Planejar(Receita(corrente), 1, 1m);

Assert.Null(plano.Erro);
```

9. **`Materiais_e_roteiro_sao_copiados_em_cada_no_com_a_ordem_preservada`** — inclui **setor repetido** (regra 21).

Duas armadilhas de propósito neste teste. **O material também multiplica**: o nó tem 3 unidades e cada uma pede 1,5 kg, então o nó pede **4,5** — não 1,5. E **o roteiro entra fora de ordem**, senão o `OrderBy` da implementação não é load-bearing e removê-lo não mataria teste nenhum.

```csharp
var plano = PlanejadorDeCopia.Planejar(
    Receita([(1, 2, 3m)],
            materiais: [(2, 90, 1.5m)],
            roteiro: [(2, 7, 3), (2, 8, 2), (2, 7, 1)]),   // FORA de ordem, de proposito
    1, 1m);

var filho = plano.Raiz!.Filhos.Single();
Assert.Equal(3m, filho.Quantidade);
Assert.Equal((90, 4.5m), filho.Materiais.Single());        // 3 x 1,5 — o material multiplica
Assert.Equal([(7, 1), (8, 2), (7, 3)], filho.Roteiro);     // reordenado pela Ordem
```

- [ ] **Passo 2: rodar e ver falhar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter PlanejadorDeCopiaTests
```

Esperado: falha de compilação — `PlanejadorDeCopia` não existe.

- [ ] **Passo 3: implementar o planejador**

`src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`. A guarda de ciclo é **por caminho**: um `HashSet<int>` com os componentes da raiz até o nó atual, e o componente sai dele ao subir. É isso que separa diamante (aceito) de ciclo (recusado) — um `HashSet` global de "já visto" recusaria o diamante.

```csharp
namespace Rastreamento.Application.Estrutura;

public sealed record NoPlanejado(
    int? ComponenteId,
    string? Descricao,
    decimal Quantidade,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoPlanejado> Filhos);

public sealed record ReceitaDoCatalogo(
    ILookup<int, (int FilhoId, decimal QuantidadePadrao)> Filhos,
    ILookup<int, (int MaterialId, decimal QuantidadePadrao)> Materiais,
    ILookup<int, (int SetorId, int Ordem)> Roteiro);

public sealed record PlanoDeCopia(NoPlanejado? Raiz, string? Erro, string? CodigoDoErro);

/// <summary>
/// Transforma a receita do catalogo na arvore que sera gravada. PURO: sem I/O, sem repositorio.
/// Quem le o grafo e o caso de uso; aqui so entra dado ja lido, e por isso todo caso de borda desta
/// fase e testavel sem fake nenhum.
///
/// A guarda de ciclo e POR CAMINHO, nao por "ja visto em qualquer lugar", e isso NAO e detalhe: no
/// `seed-demo` gravado ha um Componente pendurado em DOIS pais (medido em 2026-08-29). Uma guarda de
/// "ja visto" recusaria a receita MT-1000 que esta no banco. Diamante vale; ciclo nao. O teste que
/// morre se alguem trocar isso e `Diamante_e_aceito_o_mesmo_componente_sob_dois_pais` — nao o de
/// ciclo, que continuaria verde.
///
/// O teto NAO e regra de negocio, e por isso nao esta em `01-dominio-e-regras-de-negocio.md`: e
/// para-quedas contra transacao desgovernada. O numero nao saiu do `seed-demo` de proposito — o demo
/// tem 3 niveis e 15 nos, o que da PISO e nunca teto, e ele mede o demo, nao a fabrica. Fica alto o
/// bastante para nunca recusar estrutura plausivel; pode DESCER com dado real, porque subir um teto
/// que ja recusou trabalho de cliente e o erro caro.
/// </summary>
public static class PlanejadorDeCopia
{
  public const int ProfundidadeMaxima = 20;
  public const int NosMaximos = 500;

  public const string CodigoDeCiclo = "CicloNaReceita";
  public const string CodigoDeProfundidade = "EstruturaProfundaDemais";
  public const string CodigoDeTamanho = "EstruturaGrandeDemais";

  public static PlanoDeCopia Planejar(
      ReceitaDoCatalogo receita, int componenteRaizId, decimal quantidadeDaRaiz)
  {
    var caminho = new List<int>();
    var noCaminho = new HashSet<int>();
    var nos = 0;

    try
    {
      var raiz = Descer(receita, componenteRaizId, quantidadeDaRaiz, caminho, noCaminho, ref nos);
      return new PlanoDeCopia(raiz, null, null);
    }
    catch (CopiaRecusadaException e)
    {
      return new PlanoDeCopia(null, e.Message, e.Codigo);
    }
  }

  private static NoPlanejado Descer(
      ReceitaDoCatalogo receita,
      int componenteId,
      decimal quantidade,
      List<int> caminho,
      HashSet<int> noCaminho,
      ref int nos)
  {
    if (!noCaminho.Add(componenteId))
    {
      // O ciclo e o trecho do caminho que comeca no no reencontrado, fechado nele mesmo — e o que
      // diz ONDE consertar, descartando os ramos inocentes ja percorridos.
      var trecho = caminho.Skip(caminho.IndexOf(componenteId)).Append(componenteId);
      throw new CopiaRecusadaException(
          CodigoDeCiclo,
          $"A receita tem um ciclo: {string.Join(" -> ", trecho)}. "
              + "Corrija a receita do catalogo antes de criar a Peca.");
    }

    caminho.Add(componenteId);

    if (caminho.Count > ProfundidadeMaxima)
      throw new CopiaRecusadaException(
          CodigoDeProfundidade,
          $"A receita passa de {ProfundidadeMaxima} niveis de profundidade.");

    if (++nos > NosMaximos)
      throw new CopiaRecusadaException(
          CodigoDeTamanho, $"A receita gera mais de {NosMaximos} itens.");

    var filhos = receita.Filhos[componenteId]
        .Select(f => Descer(receita, f.FilhoId, quantidade * f.QuantidadePadrao,
                            caminho, noCaminho, ref nos))
        .ToList();

    caminho.RemoveAt(caminho.Count - 1);
    noCaminho.Remove(componenteId);

    return new NoPlanejado(
        ComponenteId: componenteId,
        Descricao: null,   // NULL herda a descricao do Componente (regra 19)
        Quantidade: quantidade,
        Materiais: receita.Materiais[componenteId]
            .Select(m => (m.MaterialId, quantidade * m.QuantidadePadrao)).ToList(),
        Roteiro: receita.Roteiro[componenteId]
            .OrderBy(r => r.Ordem).Select(r => (r.SetorId, r.Ordem)).ToList(),
        Filhos: filhos);
  }

  private sealed class CopiaRecusadaException(string codigo, string mensagem)
      : Exception(mensagem)
  {
    public string Codigo { get; } = codigo;
  }
}
```

> **Nota para quem implementa:** `ref int nos` não atravessa uma lambda. Se o compilador reclamar do `Select` acima, troque por um `foreach` acumulando numa `List<NoPlanejado>`. **Não** troque o contador por um campo estático — o planejador tem de ser seguro para chamadas concorrentes.

- [ ] **Passo 4: rodar e ver passar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter PlanejadorDeCopiaTests
```

Esperado: 9 aprovados.

- [ ] **Passo 5: matar as próprias guardas, uma a uma**

Não é opcional, e o resultado vai no relatório. Para cada mutação: aplique, rode a suíte da task, **anote quais testes morrem**, reverta.

| Mutação | Tem de morrer |
|---|---|
| `noCaminho.Remove(componenteId)` removido (vira "já visto") | `Diamante_e_aceito_...` — e **só** ele |
| `quantidade * f.QuantidadePadrao` → `f.QuantidadePadrao` | testes 2 e 3 |
| `quantidade * f.QuantidadePadrao` → multiplicar só no 1º nível | teste 3, **não** o 2 |
| `ProfundidadeMaxima` → 1000 | teste 7, **não** o 8 |
| `.OrderBy(r => r.Ordem)` removido | teste 9 — e ele só mata porque a entrada do teste está fora de ordem |
| `Roteiro` reindexado (`Select((r, i) => (r.SetorId, i + 1))`) | teste 9 |
| material **não** multiplicado (`m.QuantidadePadrao` em vez de `quantidade * m.QuantidadePadrao`) | teste 9 |

**Se alguma mutação não matar nada, o teste correspondente está encenando cobertura** — conserte o teste antes de seguir. `[[mutacao-do-autor-confirma-o-desenho]]`

- [ ] **Passo 6: build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
git add src/Rastreamento.Application tests/Rastreamento.Application.Tests
git commit -m "feat(fase-2): planejador da copia recursiva, com guarda de ciclo por caminho"
```

---

## Task 3: Repositório e o caso de uso que cria a Peça

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs`
- Create: `src/Rastreamento.Application/Estrutura/EstruturaDtos.cs`
- Create: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (registro no DI, junto dos outros repositórios e use cases)
- Test: `tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs`

**Interfaces:**
- Consumes: `PlanejadorDeCopia.Planejar`, `NoPlanejado`, `ReceitaDoCatalogo`, `PlanoDeCopia` (Task 2); `IPedidoRepository.ObterPorIdAsync`, `IAgrupamentoRepository.ObterPorIdAsync` (já existem).
- Produces:

```csharp
Task<Result<EstruturaItemDto>> CriarPeca(int agrupamentoId, NovaPecaDto nova, CancellationToken ct);
Task<Result<IReadOnlyList<EstruturaItemDto>>> ObterArvore(int agrupamentoId, CancellationToken ct);

public sealed record NovaPecaDto(int ComponenteId, decimal Quantidade, bool RequerRelatorioDimensional);
public sealed record EstruturaItemDto(
    int Id, int? ComponenteId, string? CodigoDoComponente, string Descricao,
    decimal Quantidade, string NivelHierarquico, bool RequerRelatorioDimensional,
    IReadOnlyList<MaterialDoNoDto> Materiais, IReadOnlyList<PassoDoRoteiroDto> Roteiro,
    IReadOnlyList<EstruturaItemDto> Filhos);
public sealed record MaterialDoNoDto(int MaterialId, string Nome, decimal Quantidade);
public sealed record PassoDoRoteiroDto(int SetorId, string Nome, int Ordem);
```

> `Descricao` no DTO **já vem resolvida**: `EstruturaItem.Descricao` quando não-nula, senão a descrição do `Componente` (regra 19). O front não faz esse fallback — se fizesse, cada consumidor novo teria de lembrar dele.

**Delta de teste desta task: +6.**

- [ ] **Passo 1: o contrato do repositório**

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IEstruturaRepository
{
  /// <summary>
  /// A receita do catalogo INTEIRA, em tres lookups. E leitura solta e larga de proposito: a
  /// alternativa e uma consulta por no durante a descida, que faz N+1 numa arvore que pode ter
  /// centenas de nos. O catalogo e pequeno (54 Componentes na massa de demonstracao) e cresce por
  /// cadastro humano, nao por producao.
  /// </summary>
  Task<(IReadOnlyList<(int Pai, int Filho, decimal Qtd)> Filhos,
        IReadOnlyList<(int Comp, int Material, decimal Qtd)> Materiais,
        IReadOnlyList<(int Comp, int Setor, int Ordem)> Roteiro)>
      LerReceitaCompletaAsync(CancellationToken ct);

  /// <summary>Grava a arvore inteira numa transacao. Arvore toda ou nada.</summary>
  Task GravarArvoreAsync(
      int agrupamentoId, int? estruturaPaiId, NoParaGravar raiz, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct);
  Task<EstruturaItem?> ObterPorIdAsync(int id, CancellationToken ct);
  Task<IReadOnlyList<EstruturaMaterial>> ListarMateriaisAsync(IReadOnlyList<int> itemIds, CancellationToken ct);
  Task<IReadOnlyList<EstruturaRoteiro>> ListarRoteiroAsync(IReadOnlyList<int> itemIds, CancellationToken ct);
  Task SalvarAlteracoesAsync(CancellationToken ct);
}

/// <summary>Espelho de `NoPlanejado` na fronteira do dominio, para a Application nao vazar tipo.</summary>
public sealed record NoParaGravar(
    int? ComponenteId, string? Descricao, decimal Quantidade, bool RequerRelatorioDimensional,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoParaGravar> Filhos);
```

- [ ] **Passo 2: escrever os seis testes com fake, e vê-los falhar**

`tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs`, com um `IEstruturaRepository` fake em memória (siga o molde dos fakes já usados em `Application.Tests`).

1. `Peca_e_criada_e_a_arvore_da_receita_vem_junto` — Componente 1 com filho 2 (qtd 4); criar Peça com quantidade 10 grava dois nós, o filho com 40.
2. `Peca_de_Componente_sem_receita_grava_um_no_so`.
3. `Ciclo_na_receita_recusa_com_409_e_nao_grava_nada` — afirma `TipoDoErro == TipoDeErro.Conflito`, `Erro` contendo `CicloNaReceita`, **e que o fake não recebeu nenhuma gravação**.
4. `Agrupamento_inexistente_da_404`.
5. `Criar_Peca_em_Pedido_EM_PRODUCAO_e_PERMITIDO` — **a informação de domínio de 2026-08-29**: cliente pede alteração de projeto com o Pedido rodando; acrescentar é o comportamento padrão. Monte o Pedido com `Status = "EmProducao"` e afirme `Sucesso`.
6. `Nivel_hierarquico_da_raiz_e_Peca_e_o_do_filho_e_Item`.

```bash
dotnet test tests/Rastreamento.Application.Tests --filter CriarPecaTests
```

Esperado: falha de compilação.

- [ ] **Passo 3: implementar o caso de uso**

`MontagemDeEstruturaUseCase.CriarPeca`: valida `Quantidade > 0`; confere que o Agrupamento existe (404); lê a receita; chama `PlanejadorDeCopia.Planejar`; se `plano.Erro` não é nulo, devolve `Result<EstruturaItemDto>.Falha(plano.CodigoDoErro!, TipoDeErro.Conflito)`; senão converte `NoPlanejado` em `NoParaGravar` (a raiz recebe `RequerRelatorioDimensional` do DTO; os filhos recebem `false`, porque a marca é **por Peça**, regra 10) e grava.

**Nenhuma checagem de `Pedido.Status`.** Escreva o comentário que diz por quê:

```csharp
    // SEM guarda de status, e isso e decisao de negocio, nao omissao: cliente grande pede alteracao
    // de projeto com o Pedido JA em execucao, e acrescentar peca nova ao pedido rodando e o
    // comportamento PADRAO (informacao do usuario, 2026-08-29). O simetrico — o que sai do projeto
    // "so para de ser produzido" — NAO e desta fase: e a Fase 3, onde "Pedido em execucao" passa a
    // existir de fato (hoje nenhum Pedido sai de `Aberto`). Ver §2.3 da spec.
    //
    // A regra 18 tem uma segunda metade que TAMBEM nao e cobrada aqui: `Componente.ArquivoSolido`
    // preenchido. Sem a Fase 2B nao existe upload, entao ninguem consegue preencher pela interface,
    // e cobrar agora travaria a verificacao manual (os 54 Componentes do seed-demo nao tem solido).
    // Quem fecha isso e a 2B.
```

- [ ] **Passo 4: implementar o repositório com transação**

`EstruturaRepository.GravarArvoreAsync` desce a árvore gravando pai antes de filho (precisa do `Id` do pai), tudo dentro de uma transação, no mesmo padrão de `ReceitaPadraoRepository`:

```csharp
    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    await GravarNo(agrupamentoId, estruturaPaiId, raiz, ct);
    await tx.CommitAsync(ct);
```

`GravarNo` insere o `EstruturaItem`, dá `SaveChangesAsync` para obter o `Id`, insere `EstruturaMaterial`/`EstruturaRoteiro` daquele nó, e recorre nos filhos passando o `Id` recém-obtido como `EstruturaPaiId`. `NivelHierarquico` é `"Peca"` quando `estruturaPaiId is null`, senão `"Item"`.

- [ ] **Passo 5: registrar no DI**

Em `Program.cs`, junto dos outros:

```csharp
builder.Services.AddScoped<IEstruturaRepository, EstruturaRepository>();
builder.Services.AddScoped<MontagemDeEstruturaUseCase>();
```

- [ ] **Passo 6: rodar, ver passar, build, commit**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter CriarPecaTests
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
git add src tests
git commit -m "feat(fase-2): cria Peca copiando a receita padrao, em transacao"
```

---

## Task 4: Sub-Item, edição e exclusão de nó

**Files:**
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs`
- Modify: `src/Rastreamento.Application/Estrutura/EstruturaDtos.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`
- Test: `tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs`

**Interfaces:**
- Consumes: tudo da Task 3.
- Produces:

```csharp
Task<Result<EstruturaItemDto>> AcrescentarFilho(int paiId, NovoFilhoDto novo, CancellationToken ct);
Task<Result<EstruturaItemDto>> EditarNo(int id, EdicaoDeNoDto edicao, CancellationToken ct);
Task<Result> ExcluirNo(int id, CancellationToken ct);

/// <summary>`ComponenteId` nulo = no ad-hoc, e ai `Descricao` e OBRIGATORIA (regra 19).</summary>
public sealed record NovoFilhoDto(int? ComponenteId, string? Descricao, decimal Quantidade);
public sealed record EdicaoDeNoDto(string? Descricao, decimal Quantidade);
```

**Delta de teste desta task: +8.**

- [ ] **Passo 1: escrever os oito testes, e vê-los falhar**

1. `Filho_de_Componente_copia_a_receita_dele` — mesma máquina da Task 2, agora pendurada num nó existente.
2. `Filho_ad_hoc_sem_Componente_exige_descricao` — `ComponenteId` nulo e `Descricao` vazia → `TipoDeErro.Validacao`. Sem isto o nó chega anônimo à tela do operador, que é exatamente o que a regra 19 existe para impedir.
3. `Filho_ad_hoc_com_descricao_e_criado_com_NivelHierarquico_Item`.
4. `Editar_quantidade_da_Peca_NAO_mexe_nos_filhos` — **a D5**. Crie Peça 10 com filho 40; edite a Peça para 20; afirme que o filho continua **40**.
5. `Editar_descricao_para_vazio_volta_a_herdar_do_Componente` — grava `null`, e o DTO relido traz a descrição do Componente.
6. `Excluir_no_leva_a_subarvore_junto` — três níveis; excluir o do meio remove ele e o neto.
7. `Excluir_com_Pedido_fora_de_Aberto_recusa_com_PedidoNaoAberto` — **a D6**. `Status = "EmProducao"` → `TipoDeErro.Conflito`, `Erro == "PedidoNaoAberto"`, e **nada foi removido**.
8. `Excluir_com_Pedido_Aberto_e_permitido` — o par do anterior. Sem ele, uma implementação que recusasse **sempre** passaria no teste 7.

- [ ] **Passo 2: implementar**

`ExcluirNo` busca o nó → sobe até a Peça para achar o `AgrupamentoId` → obtém o Agrupamento → obtém o Pedido → **se `pedido.Status != "Aberto"`, devolve `Result.Falha("PedidoNaoAberto", TipoDeErro.Conflito)`**, reusando o mesmo código de erro que `CadastroDeAgrupamentoUseCase` já emite. A remoção desce a subárvore e apaga `EstruturaMaterial`/`EstruturaRoteiro` de cada nó antes dos nós (as FKs exigem).

Comentário obrigatório, porque a distinção é o ponto da decisão:

```csharp
    // EXCLUIR e CORRECAO DE MONTAGEM, nao descarte — sao operacoes diferentes e tem palavras
    // diferentes. Correcao so existe enquanto nada foi produzido, isto e, Pedido `Aberto`; e a
    // mesma fronteira que `CadastroDeAgrupamentoUseCase.Excluir` ja usa, entao a Fase 2 estende um
    // precedente em vez de inventar regra. DESCARTE ("saiu do projeto, para de ser produzido")
    // preserva a historia e nasce na Fase 3 — ver §2.4 e §6 da spec.
```

- [ ] **Passo 3: rodar, mutar, commitar**

```bash
dotnet test tests/Rastreamento.Application.Tests --filter EditarEExcluirNoTests
```

Mutações a medir e anotar: (a) cascatear a quantidade nos filhos ao editar — tem de matar o teste 4; (b) aceitar `DELETE` em qualquer status — tem de matar o 7 e **não** o 8; (c) excluir só o nó sem a subárvore — tem de matar o 6.

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
git add src tests
git commit -m "feat(fase-2): sub-Item ad-hoc, edicao de no e exclusao com a subarvore"
```

---

## Task 5: Endpoints, perfis, e o espelho no front

**Files:**
- Create: `src/Rastreamento.Api/Controllers/EstruturaController.cs`
- Modify: `web/src/auth/permissoes.ts`
- Modify: `web/src/auth/permissoesEspelhamOBackend.test.ts:36-41`
- Test: `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`

**Interfaces:**
- Consumes: `MontagemDeEstruturaUseCase` inteiro (Tasks 3 e 4).
- Produces: as cinco rotas; `Recurso` novo `'estrutura'` no front.

> **Esta task quebra a suíte do FRONT se você parar no meio.** `permissoesEspelhamOBackend.test.ts` enumera **todo `.cs`** de `src/Rastreamento.Api/Controllers/` e exige que cada arquivo esteja mapeado ou explicitamente isento. Criar `EstruturaController.cs` sem tocar o mapa deixa `npm test` vermelho. Os dois lados vão na mesma task de propósito.

**Delta de teste desta task: +7 no backend, +0 no front** (a guarda do front é `it.each` sobre o mapa: ela ganha um caso pela linha nova, sem `it(` novo).

- [ ] **Passo 1: escrever os sete testes de endpoint, e vê-los falhar**

`tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`, no molde dos outros testes de endpoint (mesma factory, mesma autenticação). **Nenhuma URL literal escreve `/api`** — siga o que `AuthEndpointsTests.PrefixoDeApi.cs` fixou.

1. `POST_cria_a_Peca_e_devolve_201_com_a_arvore`
2. `GET_devolve_a_arvore_aninhada_do_Agrupamento`
3. `POST_com_ciclo_na_receita_devolve_409_CicloNaReceita`
4. `DELETE_em_Pedido_nao_Aberto_devolve_409_PedidoNaoAberto`
5. `POST_de_filho_ad_hoc_sem_descricao_devolve_400`
6. `Perfil_sem_escrita_recebe_403_no_POST` — autentique como `Operador`.
7. `Perfil_sem_escrita_recebe_200_no_GET` — leitura é de todos. Sem este, um `[Authorize(Roles)]` na classe inteira passaria no teste 6.

- [ ] **Passo 2: implementar o controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Estrutura;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// A arvore real de um Agrupamento. Controller PROPRIO, no mesmo precedente de
/// `ReceitaPadraoController`: as acoes de criacao e leitura sao aninhadas sob Agrupamento e as de
/// no sao de topo, entao cada acao declara a propria rota, sem `[Route]` de classe.
///
/// Herda de ControllerBase e nao de CadastroControllerBase: nao ha 409 de DUPLICIDADE a montar
/// aqui (os 409 desta fase sao de ciclo, de teto e de `PedidoNaoAberto`), entao
/// TraduzirFalha/LocalizadorDeDuplicado nao serviriam para nada.
/// </summary>
[ApiController]
[Authorize]
public class EstruturaController : ControllerBase
{
  /// <summary>Mesmos perfis do Agrupamento: quem monta o pedido monta a arvore dele.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly MontagemDeEstruturaUseCase _montagem;

  public EstruturaController(MontagemDeEstruturaUseCase montagem) => _montagem = montagem;

  [HttpGet("agrupamentos/{agrupamentoId:int}/estrutura")]
  public async Task<IActionResult> Obter(int agrupamentoId, CancellationToken ct) =>
      Traduzir(await _montagem.ObterArvore(agrupamentoId, ct));

  [HttpPost("agrupamentos/{agrupamentoId:int}/estrutura")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> CriarPeca(
      int agrupamentoId, [FromBody] NovaPecaDto nova, CancellationToken ct) =>
      Traduzir(await _montagem.CriarPeca(agrupamentoId, nova, ct), criado: true);

  [HttpPost("estrutura/{id:int}/filhos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> AcrescentarFilho(
      int id, [FromBody] NovoFilhoDto novo, CancellationToken ct) =>
      Traduzir(await _montagem.AcrescentarFilho(id, novo, ct), criado: true);

  [HttpPut("estrutura/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Editar(
      int id, [FromBody] EdicaoDeNoDto edicao, CancellationToken ct) =>
      Traduzir(await _montagem.EditarNo(id, edicao, ct));

  [HttpDelete("estrutura/{id:int}")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Excluir(int id, CancellationToken ct)
  {
    var r = await _montagem.ExcluirNo(id, ct);
    if (r.Sucesso) return NoContent();
    return r.TipoDoErro switch
    {
      TipoDeErro.NaoEncontrado => NotFound(),
      TipoDeErro.Conflito => Conflict(new { erro = r.Erro }),
      _ => BadRequest(new { erro = r.Erro }),
    };
  }

  private IActionResult Traduzir<T>(Result<T> r, bool criado = false)
  {
    if (r.Sucesso) return criado ? StatusCode(StatusCodes.Status201Created, r.Valor) : Ok(r.Valor);
    return r.TipoDoErro switch
    {
      TipoDeErro.NaoEncontrado => NotFound(),
      TipoDeErro.Conflito => Conflict(new { erro = r.Erro }),
      _ => BadRequest(new { erro = r.Erro }),
    };
  }
}
```

- [ ] **Passo 3: o espelho no front**

Em `web/src/auth/permissoes.ts`, o tipo ganha `'estrutura'` e a tabela ganha a linha:

```ts
export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos' | 'estrutura'
```

```ts
  // A árvore real de um Agrupamento. `Recurso` PRÓPRIO, e não carona em `agrupamentos`: os dois têm
  // os mesmos perfis hoje, mas são conceitos separados — criar um Agrupamento vazio e montar a
  // árvore dele são ações distintas, e a primeira vez que os perfis divergirem (a Fase 3 mexe em
  // quem aponta setor) a carona seria descoberta como bug, não como decisão.
  estrutura: ['PCP', 'Administrador'],
```

Em `permissoesEspelhamOBackend.test.ts`, o mapa ganha:

```ts
  estrutura: ['EstruturaController.cs'],
```

- [ ] **Passo 4: rodar os dois lados**

```bash
dotnet test tests/Rastreamento.Api.Tests --filter EstruturaEndpointsTests
cd web && npm test -- --run && npm run build
```

Esperado: 7 aprovados no backend; front verde. **Se o front estiver vermelho em `permissoesEspelhamOBackend`, o Passo 3 não foi feito** — é exatamente o alarme que essa guarda existe para dar.

- [ ] **Passo 5: matar a guarda de perfil**

Tire o `[Authorize(Roles = PerfisDeEscrita)]` do `POST` e rode. **Tem de morrer o teste 6.** Reverta. Depois mova o `[Authorize(Roles)]` para a classe e rode: **tem de morrer o teste 7**. Reverta. Anote as duas no relatório.

- [ ] **Passo 6: build e commit**

```bash
dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx --no-build
git add src tests web/src/auth
git commit -m "feat(fase-2): endpoints da estrutura e o Recurso novo no espelho de perfis"
```

---

## Task 6: Cliente de API no front

**Files:**
- Create: `web/src/api/estrutura.ts`
- Test: `web/src/api/estrutura.test.ts`

**Interfaces:**
- Consumes: as cinco rotas da Task 5.
- Produces:

```ts
export type NivelHierarquico = 'Peca' | 'Item'
export interface MaterialDoNo { materialId: number; nome: string; quantidade: number }
export interface PassoDoRoteiro { setorId: number; nome: string; ordem: number }
export interface NoDaEstrutura {
  id: number
  componenteId: number | null
  codigoDoComponente: string | null
  descricao: string
  quantidade: number
  nivelHierarquico: NivelHierarquico
  requerRelatorioDimensional: boolean
  materiais: MaterialDoNo[]
  roteiro: PassoDoRoteiro[]
  filhos: NoDaEstrutura[]
}
export type ResultadoDeEstrutura = 'ok' | 'PedidoNaoAberto' | 'CicloNaReceita'
  | 'EstruturaProfundaDemais' | 'EstruturaGrandeDemais' | 'NaoEncontrado'

export function obterEstrutura(agrupamentoId: number): Promise<NoDaEstrutura[]>
export function criarPeca(agrupamentoId: number, p: NovaPeca): Promise<NoDaEstrutura | ConflitoDeEstrutura>
export function acrescentarFilho(paiId: number, f: NovoFilho): Promise<NoDaEstrutura | ConflitoDeEstrutura>
export function editarNo(id: number, e: EdicaoDeNo): Promise<NoDaEstrutura | ConflitoDeEstrutura>
export function excluirNo(id: number): Promise<ResultadoDeEstrutura>
```

**Delta de teste desta task: +5.**

- [ ] **Passo 1: escrever os cinco testes com `fetchPorRota`, e vê-los falhar**

1. `obterEstrutura chama /agrupamentos/:id/estrutura sem escrever /api à mão` — afirma a URL **exata** recebida pelo fetch, incluindo o prefixo aplicado pelo `rota()`. É a guarda contra duplicar o prefixo.
2. `criarPeca devolve o nó criado no 201`.
3. `criarPeca devolve o código do conflito no 409` — corpo `{ erro: 'CicloNaReceita' }`.
4. `excluirNo devolve 'PedidoNaoAberto' no 409`.
5. `excluirNo devolve 'NaoEncontrado' no 404, sem lançar` — o 404 é desfecho, não exceção, no mesmo padrão de `excluirAgrupamento`.

- [ ] **Passo 2: implementar**, seguindo o formato de `cadastros.ts` (mesmo `ehConflito`, mesmo tratamento de status). **Arquivo próprio**: `cadastros.ts` já serve cinco cadastros e a árvore não é um cadastro.

- [ ] **Passo 3: rodar, buildar, commitar**

```bash
cd web && npm test -- --run estrutura && npm run build
git add web/src/api
git commit -m "feat(fase-2): cliente de API da estrutura"
```

---

## Task 7: A primitiva `ArvoreDeEstrutura`

**Files:**
- Create: `web/src/components/ArvoreDeEstrutura.tsx`
- Test: `web/src/components/ArvoreDeEstrutura.test.tsx`

**Interfaces:**
- Consumes: `NoDaEstrutura` (Task 6).
- Produces:

```tsx
interface Props {
  nos: NoDaEstrutura[]
  podeEscrever: boolean
  onAcrescentarFilho?: (paiId: number) => void
  onEditar?: (no: NoDaEstrutura) => void
  onExcluir?: (no: NoDaEstrutura) => void
}
export function ArvoreDeEstrutura(props: Props): JSX.Element
```

**Delta de teste desta task: +7.**

Layout: **lista indentada, uma coluna** — escolhido pelo usuário em 2026-08-29 contra árvore-com-painel e cartões aninhados. Um nó por linha; a indentação (recuo à esquerda proporcional ao nível) carrega a hierarquia; materiais e roteiro abrem **embaixo da própria linha** quando o nó está expandido.

- [ ] **Passo 1: escrever os sete testes, e vê-los falhar**

1. `renderiza um nó por linha, em profundidade` — três níveis, três linhas.
2. `o recuo cresce com o nível` — afirma o recuo do neto **maior** que o do filho, e o do filho maior que o da Peça. Não afirme o valor absoluto: isso amarra o teste ao pixel.
3. `mostra a quantidade de TODO nó` — é a mitigação escrita da D5: sem cascata, a inconsistência só fica à vista se toda quantidade aparecer. Afirme a presença dos três números.
4. `nó ad-hoc é distinguível sem usar cor de estado` — afirme o rótulo/marca textual **e** que a linha não tem classe `text-positivo` nem `text-negativo`.
5. `expandir um nó revela materiais e roteiro`.
6. `roteiro com setor repetido mostra os dois passos, na ordem` — regra 21 chegando à tela.
7. `sem permissão de escrita, as ações não são renderizadas` — `podeEscrever={false}` e nenhum botão de acrescentar/editar/excluir no documento.

- [ ] **Passo 2: implementar**, usando `Botao` e `Pilula` de `web/src/components/`; **nada escrito à mão**. Cores só por token.

- [ ] **Passo 3: rodar, buildar, commitar**

```bash
cd web && npm test -- --run ArvoreDeEstrutura && npm run build
git add web/src/components
git commit -m "feat(fase-2): primitiva ArvoreDeEstrutura, lista indentada"
```

---

## Task 8: A tela `AgrupamentoDetalhePage`

**Files:**
- Create: `web/src/pages/AgrupamentoDetalhePage.tsx`
- Create: `web/src/pages/AgrupamentoDetalhePage.test.tsx`
- Modify: `web/src/App.tsx:30` (rota nova, dentro da rota de layout do `AppShell`)
- Modify: `web/src/pages/PedidoDetalhePage.tsx:169` (o item da lista vira link)

**Interfaces:**
- Consumes: `ArvoreDeEstrutura` (Task 7), `web/src/api/estrutura.ts` (Task 6), `SeletorComBusca` e `usePodeEscrever` (já existem).
- Produces: rota `/agrupamentos/:id`.

**Delta de teste desta task: +8** (7 na tela nova, 1 na `PedidoDetalhePage`).

- [ ] **Passo 1: escrever os oito testes, e vê-los falhar**

Na tela nova:

1. `mostra o estado de carregando enquanto busca`
2. `mostra a árvore quando há estrutura`
3. `estado vazio distingue "ainda não há estrutura" de "não achei"`
4. `erro de rede cai em BannerDeErro com mensagemDeErro`
5. `criar Peça usa SeletorComBusca para escolher o Componente` — o gatilho é catálogo paginado; um `<select>` com a lista inteira não escala.
6. `409 de ciclo mostra a mensagem que nomeia o caminho, não um erro genérico` — é o que dá ao usuário como consertar.
7. `perfil sem escrita não vê o formulário, mas vê a árvore` — o gating vai na **ação**, não no link.

Na `PedidoDetalhePage`:

8. `cada agrupamento da lista é um link para /agrupamentos/:id`

- [ ] **Passo 2: implementar**

A tela começa por `<Pagina titulo={…}>`. **Nada de `min-h-screen`** — quem faz isso é o `AppShell`. O `try/catch` em volta das chamadas de escrita continua obrigatório: o 403 é a fronteira real.

Rota, em `App.tsx`, junto das outras dentro do `AppShell`:

```tsx
        <Route path="/agrupamentos/:id" element={<AgrupamentoDetalhePage />} />
```

- [ ] **Passo 3: rodar, buildar, commitar**

```bash
cd web && npm test -- --run && npm run build
git add web/src
git commit -m "feat(fase-2): tela de detalhe do Agrupamento com a arvore"
```

---

## Task 9: Documentos

Produto é texto, não código. **Delta de teste: 0.** Não pule a review por causa disso — a Fase 1E mediu que a família "prosa que encena precisão" apareceu **dez vezes** e que o passe que a fechava escreveu duas ocorrências novas.

**Files:** `specs/05-api-endpoints.md`, `specs/06-roadmap-mvp.md`, `specs/01-dominio-e-regras-de-negocio.md`, `CLAUDE.md`

- [ ] **Passo 1: `05-api-endpoints.md`** ganha as cinco rotas da Task 5, com os perfis e a tabela de erros (`CicloNaReceita`, `EstruturaProfundaDemais`, `EstruturaGrandeDemais`, `PedidoNaoAberto`), no molde das seções existentes.

- [ ] **Passo 2: `06-roadmap-mvp.md`** — a seção "Fase 2" perde o bullet de upload de sólido, que vira uma **Fase 2B** própria logo abaixo, herdando a segunda metade da regra 18. O bullet dos `ALTER` de `ArquivoSolido`/`ArquivoFoto` vai junto para a 2B; o de `Descricao` fica na 2.

- [ ] **Passo 3: `01-dominio-e-regras-de-negocio.md`**, seção "Pontos ainda em aberto", ganha o achado da §6 da spec:

```markdown
- **Descontinuar uma Peça trava o fechamento do Pedido.** O comportamento padrão quando um cliente
  altera o projeto de um Pedido em execução é o descartado **parar de ser produzido**, não ser
  apagado. Mas pela regra 13 uma Peça só conclui quando **toda** a quantidade virou expedido ou
  perdido — e a quantidade que nunca entrou em produção não é nem uma coisa nem outra. A Peça nunca
  conclui, o Agrupamento nunca conclui, e o Pedido nunca fecha. Descontinuar precisa de um **bucket
  terminal** próprio ou de uma exceção explícita na regra 13. Levantado na Fase 2 e deliberadamente
  não decidido lá: tem efeito na Fase 5 (fechamento), não só na 3.
```

- [ ] **Passo 4: `CLAUDE.md`** — se `ArvoreDeEstrutura` for virar padrão para tela nova, a seção "Interface" ganha a linha; se não for, **não escreva que é**. Uma primitiva com um consumidor é uma primitiva com um consumidor.

- [ ] **Passo 5: reler o entorno, não só a frase**

Cada edição acima muda um documento onde vizinhos falam do mesmo assunto. **O escopo da releitura é a seção inteira**, não a linha alterada — é assim que se acha a contradição entre bullets vizinhos. `[[edicao-pontual-em-prosa-estraga-o-entorno]]`

- [ ] **Passo 6: commit**

```bash
git add specs CLAUDE.md
git commit -m "docs(fase-2): endpoints, Fase 2B no roadmap e o ponto em aberto da descontinuacao"
```

---

## Task 10: Verificação manual em navegador

Produto é evidência, não código. **Delta de teste: 0.** A justificativa para não abrir gate de review aqui **tem de ser escrita** no ledger e no relatório, ou é indistinguível de deriva.

Esta task existe porque o navegador achou o defeito que a suíte verde não achou **duas vezes** neste projeto: na 1C, descumprimento de spec com 362 testes verdes; na 1E, cor de estado decorando contagem, com 395.

- [ ] **Passo 1: banco com massa**

```bash
docker compose up -d
```

Confira que `db/seed-demo.sql` está carregado (54 Componentes, 3 receitas). **`MT-1000` é a receita mais funda** — 3 níveis, 15 nós — e é a única que exercita a cópia de verdade.

- [ ] **Passo 2: criar o usuário sem escrita**

Ele **não** está em `db/seed.sql`, por decisão registrada. Sem ele não há como provar o V5:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'operador') INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo) SELECT 'operador', (SELECT SenhaHash FROM dbo.Usuario WHERE NomeUsuario = 'admin'), 'Operador de Chao', (SELECT Id FROM dbo.Perfil WHERE Nome = 'Operador'), 1;"
```

- [ ] **Passo 3: as seis verificações**

| # | O que provar | Como |
|---|---|---|
| V1 | A cópia recursiva funciona | Criar Peça de `MT-1000`, quantidade 10; a árvore aparece com 3 níveis |
| V2 | A multiplicação está certa | Conferir **na tela** que a quantidade do filho é 10 × a `QuantidadePadrao` da receita, e a do neto acumula |
| V3 | A não-cascata é visível | Editar a Peça de 10 para 20; os filhos **continuam** no número antigo, e isso fica à vista |
| V4 | O ad-hoc chega nomeado | Acrescentar sub-Item sem Componente, com descrição; ele aparece com o nome, não anônimo |
| V5 | O gating é na ação | Entrar como `operador`; a árvore aparece, as ações de escrita **não** |
| V6 | O caminho morto ligou | Tentar excluir o Agrupamento que agora tem estrutura → `AgrupamentoNaoVazio` na tela |

- [ ] **Passo 4: registrar**

Print ou descrição por verificação, no relatório. **Achado em navegador é achado**: se algo divergir, abre-se correção antes de fechar a fase — não se anota como observação.

---

## Auto-revisão do plano

Feita antes de entregar, e o que ela achou está corrigido acima.

**1. Cobertura da spec.** Percorridas as seções da spec contra as tasks:

| Spec | Task |
|---|---|
| §3.1 schema e constraint | 1 |
| §3.2 camadas e `TemEstruturaAsync` | 1 |
| §3.3 cópia, ciclo por caminho, teto | 2 (puro) + 3 (persistência) |
| §3.4 contrato | 5 |
| §3.5 erros | 5 |
| §1.3 edição e exclusão | 4 |
| §2.3 sem guarda de status | 3 (teste 5 + comentário) |
| §2.4 exclusão só com `Aberto` | 4 (testes 7 e 8) |
| §4.1 rota e layout | 8 |
| §4.2 primitivas | 7 e 8 |
| §4.3 estados e gating | 8 |
| §4.4 cor | 7 (teste 4) + guarda de paleta |
| §4.5 caminho morto que liga | 10 (V6) |
| §5.1 documentos | 9 |
| §6 achado em aberto | 9 (passo 3) |
| §7.2 o que o demo não cobre | 2 (receita sintética nos 9 testes) |
| §7.3 mutações | 2 (passo 5), 4 (passo 3), 5 (passo 5) |
| §7.4 medir, não herdar | 0 |

Nenhuma seção ficou sem task.

**2. Placeholders.** Varrido: não há "TBD", "adicionar tratamento de erro apropriado" nem "escreva testes para o acima". As duas frases que mais se aproximam são deliberadas e nomeadas: a nota do `ref int nos` no Passo 3 da Task 2 (dá a alternativa concreta) e o Passo 4 da Task 9 (que manda **não** escrever se não for verdade).

**3. Consistência de tipos.** `NoPlanejado` (Task 2) → `NoParaGravar` (Task 3) → `EstruturaItemDto` (Task 3) → `NoDaEstrutura` (Task 6) — os três saltos são conversões declaradas, não o mesmo tipo com nomes diferentes. `Quantidade` é `decimal` em todo o backend e `number` no front. `PedidoNaoAberto` é a mesma string literal na Task 4, na Task 5 e no `ResultadoDeEstrutura` da Task 6.

**4. Deltas somados, e as fórmulas recalculadas.** Backend **+34** (4 + 9 + 6 + 8 + 7); front **+20** (5 + 7 + 8). Cada número bate com a contagem dos testes nomeados na própria task — nenhuma task declara delta que ela não lista. **Nenhum total absoluto aparece neste plano**: a Task 0 mede a base e os deltas se somam a ela.

**Três correções que a auto-revisão forçou**, todas na Task 2, e as duas primeiras eram defeitos meus de verdade:

- **O teste 9 afirmava `1.5m` no material.** A implementação multiplica o material pela quantidade do nó (3 × 1,5 = **4,5**), e ela está certa: se o nó tem 3 unidades e cada uma pede 1,5 kg, o nó pede 4,5. O teste estava errado, não o código — e teria falhado na primeira execução, gastando um round de fix pass.
- **A mutação "remover o `OrderBy`" não mataria nada.** A entrada do teste 9 estava **já ordenada**, então o `OrderBy` não era load-bearing e removê-lo passaria verde. A entrada agora entra fora de ordem de propósito. Era exatamente uma guarda prometendo mais do que mata.
- A mutação do teto ganhou o par explícito ("teste 7, **não** o 8"), no mesmo padrão das outras.

**Uma quarta correção, de escopo:** a Task 5 nasceu tocando só o backend. A guarda `permissoesEspelhamOBackend.test.ts` enumera o diretório de controllers pelo **disco**, então criar `EstruturaController.cs` deixaria `npm test` vermelho numa task que nem abre a pasta `web/`. Os dois lados foram unidos na mesma task, com o aviso em destaque.
