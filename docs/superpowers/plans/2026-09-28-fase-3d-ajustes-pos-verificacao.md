# Fase 3D — Ajustes pós-verificação — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Iniciar vira o verbo único (o pai começa consumindo os filhos), o Setor ganha uma atividade que dá nome aos botões, o destino do filho pronto passa a ser calculado, a fila ganha estorno rápido e o Pedido pode ser pausado.

**Architecture:** Mesma arquitetura da Fase 3 — Database First (`specs/02-modelo-de-dados.sql` + `db/alter-fase-3d.sql` idempotente), regra em funções puras da `CalculadoraDeExecucao`, casos de uso em `Rastreamento.Application/Execucao`, controllers finos sobre `ExecucaoControllerBase`, e o front React consumindo o contrato JSON desta página. Cada fatia de backend vem antes da fatia de front que a consome.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), Vitest + Testing Library + jsdom.

**Spec:** `docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md` (aprovada em 2026-09-28). Referência de fundo: `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md` ("spec da Fase 3").

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 10**, verificação manual, com a justificativa escrita **antes** no ledger e no relatório (produto é relatório, não código).
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com `scripts/review-package BASE HEAD` — nunca `HEAD~1`.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês (`CLAUDE.md`, "Convenções de nomenclatura").
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa, em código e em prosa (`CLAUDE.md`, "Convenção de citação"). Comentário de código **não cita** ledger, brief nem "item N do plano": o repositório é público.
- O livro (`dbo.Movimentacao`) é **só de inclusão**: correção é estorno. `dbo.PedidoPausa` é só de inclusão, exceto o fecho do intervalo (`RetomadoEm`/`RetomadoPorUsuarioId`, gravados uma vez).
- Schema: mudar `specs/02-modelo-de-dados.sql` primeiro, depois o mapeamento EF; nunca `Add-Migration`. Banco anterior se atualiza por `db/alter-fase-3d.sql` (idempotente, carregado com `-b -f 65001`).
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1` (o `-m:1` não é opcional: processos de teste disputam o mesmo banco).
- Front: `npm test -- --run` **e** `npm run build` (o Vitest não faz typecheck; erro de tipo em `.test.tsx` só aparece no build). Telas: primitivas de `web/src/components/`, cores só por token, três estados com teste, `// @vitest-environment jsdom` + `afterEach(cleanup)`.
- **Cor de estado nunca decora:** a pílula "Pausado" é tom `neutro` (o padrão da `Pilula`), nunca `negativo`.
- `git pull` antes de todo commit, nos dois repositórios (código e ledger). No ledger, `git add` por caminho explícito, em chamada separada do `task-brief`/`review-package`.
- `web/src/auth/permissoes.ts` espelha os `[Authorize(Roles)]`: rota nova de escrita entra na `TabelaAprovada` de `PerfisDeEscritaDeclaradosTests` **e** em `permissoes.ts` + `CONTROLLERS_POR_RECURSO` de `permissoesEspelhamOBackend.test.ts` **na mesma task**.

## Bancada

```bash
bash scripts/estado
docker compose up -d
# Aplicar o alter da fase (idempotente) no banco de dev, sempre que uma task mexer nele:
MSYS_NO_PATHCONV=1 docker compose cp db/alter-fase-3d.sql sqlserver:/tmp/alter-fase-3d.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-fase-3d.sql
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm test -- --run && npm run build
```

Na nuvem: `bash scripts/backend-na-nuvem` antes (cria o banco a partir do `02`, que já terá as mudanças).

**Baseline:** as últimas medidas são backend **785** (App 403 · Infra 115 · Api 267, em `44b1d54`) e front **780 / 57 arquivos** (em `60c88e4`). **O controlador remede as duas na base da Task 1**, antes de despachá-la, e registra no ledger. Este plano **não** dá totais absolutos por task — cada implementer mede o delta da própria task e o escreve no relatório. (Total absoluto em plano propaga erro task a task.)

## Contrato JSON novo (fonte para o front)

Só o que muda. Nomes em camelCase, como o ASP.NET serializa.

```jsonc
// GET /setores — cada item
{ "id": 3, "nome": "Solda", "ativo": true, "atividade": "solda" }          // atividade: string | null

// POST /setores, PUT /setores/{id} — corpo
{ "nome": "Solda", "atividade": "solda" }                                     // atividade opcional; "   " vira null

// GET /setores/{id}/fila
{
  "setorId": 3, "setorNome": "Solda", "setorAtividade": "solda",             // NOVO
  "aIniciar":   [ { "no": NoResumo, "ordem": 1, "quantidade": 10, "estornaveis": [] } ],   // só nós SEM filhos
  "emTrabalho": [ { "no": NoResumo, "ordem": 1, "quantidade": 4,  "estornaveis": [Estornavel] } ],
  "aguardandoColeta": [ { "no": NoResumo, "ordem": 1, "quantidade": 4, "destino": Destino, "estornaveis": [Estornavel] } ],
  "aguardandoMontagem": [ {
      "pai": NoResumo, "faltaMontar": 10, "daParaMontar": 2,
      "iniciaAqui": true,                                                     // NOVO: este Setor é o 1º passo do pai
      "primeiroPassoDoPai": { "id": 3, "nome": "Solda" },                     // NOVO: null se o pai não tem Roteiro
      "filhos": [ FilhoNaMontagem ]
  } ],
  "sobra": [ { "no": NoResumo, "origem": "UltimoPasso", "ordem": 2, "quantidade": 5, "emMaisDeUmSetor": false, "estornaveis": [] } ]
}

// NoResumo ganha "pausa": null | { "desde": "2026-09-28T10:14:00-03:00", "porUsuarioNome": "PCP", "motivo": "PED-9 urgente" }

// Estornavel
{ "tipo": "Inicio" | "Termino" | "Montagem", "id": 41, "quantidade": 5,
  "usuarioId": 12, "usuarioNome": "Operador do Corte", "dataHora": "2026-09-28T10:14:00-03:00" }
// "Montagem" é o início de um pai (consumiu os filhos): estorna-se por POST /montagens/{id}/estorno.

// Destino (Tarefas e "Aguardando coleta") — SAEM sugestaoSetorId e setoresPossiveis
{ "tipo": "ProximoPasso" | "Expedicao" | "Montagem", "setorId": 3, "setorNome": "Solda", "ordem": null,
  "paiId": 2, "paiSemRoteiro": false }
// Montagem: setorId/setorNome = 1º passo do pai; ordem sempre null; paiSemRoteiro => setorId null.

// POST /entregas — cada item: "destinoSetorId" deixa de ser mandado (se vier não nulo: 400 DestinoIndevido)
{ "estruturaItemId": 7, "origem": { "posicao": "AguardandoColeta", "setorId": 1, "ordem": 1 }, "quantidade": 4 }

// POST /estrutura/{id}/inicios — aceita nó com filhos; resposta: o movimento de Início (montagemId preenchido no pai)
// POST /estrutura/{id}/montagens — REMOVIDA (404)

// GET /pedidos, GET /pedidos/{id} — cada Pedido ganha "pausa": null | { "desde", "porUsuarioNome", "motivo" }

// POST /pedidos/{id}/pausas  { "motivo": "texto opcional" }  -> 201 Pausa
// POST /pedidos/{id}/retomada (sem corpo)                    -> 200 Pausa
// Pausa
{ "id": 5, "pedidoId": 1, "pausadoEm": "...", "pausadoPorUsuarioId": 12, "pausadoPorNome": "PCP", "motivo": null,
  "retomadoEm": null, "retomadoPorUsuarioId": null, "retomadoPorNome": null }
```

Códigos de erro: **entram** `PedidoPausado`, `PedidoJaPausado`, `PedidoNaoPausado`, `RedirecionamentoSemEfeito`, `MotivoLongoDemais`; **saem** `SemFilhos`, `MontagemAcimaDoQueFalta`, `DestinoForaDoRoteiroDoPai`.

## Desvios da spec, decididos neste plano

- **D1 — Resposta do Iniciar.** A spec §5.1 diz que a resposta traz "o movimento e, se houve consumo, a montagem com as baixas". O plano devolve **só o movimento de Início** (`MovimentacaoDto`), com `montagemId` preenchido quando houve consumo — a montagem está no livro do nó. Motivo: o front não usa o corpo da resposta, e um envelope novo mudaria o contrato das folhas sem ganho. A Task 9 ajusta a §5.1 da spec.
- **D2 — `destinoSetorId` fica no contrato só para recusar** (a spec §4.3 deixou a escolha ao plano). Tirá-lo do DTO faria o `System.Text.Json` **ignorar em silêncio** um cliente antigo que o mandasse; mantê-lo anulável e recusar não-nulo com `DestinoIndevido` falha alto.
- **D3 — `MontagemPendenteDto` ganha `IniciaAqui` e `PrimeiroPassoDoPai`.** A spec §6.1 diz "Iniciar quando este Setor é o primeiro passo dele; senão, Levar ao Setor P1", mas a fila não trazia o primeiro passo do pai. Os dois campos são o mínimo para a tela decidir sem outra requisição (o `FormularioDeRedirecionamento`, que buscava o Roteiro do pai, deixa de existir).
- **D4 — Estornáveis de "Aguardando coleta" e de "Sobra" do último passo.** A mesma posição `AguardandoColeta(S, k)` pode aparecer nas duas seções (tarefa + sobra). Os registros vão para a linha de "Aguardando coleta" quando ela existe, e para a de "Sobra" só quando não há tarefa — nunca para as duas.
- **D5 — A atividade não é forçada a minúscula.** A spec §3.2 a descreve como "substantivo em minúscula"; o plano só apara espaços, porque forçar minúscula quebraria siglas ("Iniciar CNC"). O texto de ajuda do campo orienta a minúscula.
- **D6 — Motivo longo demais tem código.** O `[MaxLength(200)]` do DTO já dá 400 pela validação do ASP.NET; o caso de uso repete a guarda (`MotivoLongoDemais`, 400) para quem o chama direto, como os demais cadastros fazem com os próprios limites.

---

### Task 1: O Setor ganha atividade, e os botões da fila usam o nome dela

Fatia vertical: schema → EF → cadastro de Setor → fila (`setorAtividade`) → tela de Setores e rótulos da fila.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `dbo.Setor`)
- Create: `db/alter-fase-3d.sql`
- Modify: `src/Rastreamento.Domain/Entities/Setor.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/Configurations/SetorConfiguration.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`SetorDto`, `NovoSetorDto`)
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`FilaDoSetorDto`)
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (`FilaAsync`)
- Modify: `specs/05-api-endpoints.md` (seção "Catálogo", Setores; seção "Execução / Rastreamento", fila)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeSetorUseCaseTests.cs`
- Test: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs`, `ConsultaDeExecucaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/SetorAtividadeMapeamentoTests.cs` (novo)
- Test: `tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`
- Modify: `web/src/api/cadastros.ts` (`SetorDto`, `criarSetor`, `editarSetor` novo)
- Modify: `web/src/api/execucao.ts` (`FilaDoSetorDto.setorAtividade`)
- Modify: `web/src/execucao/formatacao.ts` (`rotuloDaAcao` novo)
- Modify: `web/src/pages/SetoresPage.tsx`
- Modify: `web/src/pages/FilaDoSetorPage.tsx` (rótulos de Iniciar/Terminar)
- Test: `web/src/testes/execucao.ts` (`fila()` ganha `setorAtividade: null`)
- Test: `web/src/execucao/formatacao.test.ts`, `web/src/pages/SetoresPage.test.tsx`, `web/src/pages/FilaDoSetorPage.test.tsx`, `web/src/api/cadastros.test.ts`
- Test (fixture): todo `SetorDto` literal dos testes do front ganha `atividade: null` — `grep -rn "nome: '.*', ativo:" web/src --include=*.test.tsx` achou **10** em 5 arquivos (medido em 2026-09-28).

**Interfaces:**
- Produces: `Setor.Atividade : string?`; `SetorDto(int Id, string Nome, bool Ativo, string? Atividade)`; `NovoSetorDto(string Nome, string? Atividade = null)`; `FilaDoSetorDto(int SetorId, string SetorNome, string? SetorAtividade, …)`; TS `SetorDto.atividade: string | null`, `FilaDoSetorDto.setorAtividade: string | null`, `rotuloDaAcao(verbo: 'Iniciar' | 'Terminar', atividade: string | null): string`, `editarSetor(id, { nome, atividade })`.

- [ ] **Step 1: Schema.** Em `specs/02-modelo-de-dados.sql`, `dbo.Setor` passa a:

```sql
CREATE TABLE dbo.Setor (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Nome            NVARCHAR(100)       NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Setor_Ativo DEFAULT (1),
    -- Substantivo que nomeia os botões da fila: 'montagem' -> "Iniciar montagem" / "Terminar montagem".
    -- NULL = "Iniciar" / "Terminar" (spec da Fase 3D, seção 2.3).
    Atividade       NVARCHAR(40)        NULL,
    CONSTRAINT PK_Setor PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Setor_Nome UNIQUE (Nome)
);
```

Crie `db/alter-fase-3d.sql`:

```sql
-- Migracao idempotente da Fase 3D para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md, "Comandos"
-- (o mesmo comando do db/alter-fase-3.sql, com este arquivo).
SET NOCOUNT ON;
GO

/* 1. Setor.Atividade (spec da Fase 3D, secao 2.3) ------------------------------------------------ */
IF COL_LENGTH('dbo.Setor', 'Atividade') IS NULL
    ALTER TABLE dbo.Setor ADD Atividade NVARCHAR(40) NULL;
GO
```

Aplique no banco de dev (bloco da Bancada) **duas vezes** e confira que a segunda não muda nada.

- [ ] **Step 2: Entidade e mapeamento.** `Setor.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Setor
{
  public int Id { get; set; }
  public string Nome { get; set; } = string.Empty;

  /// <summary>Catalogo nao se exclui, se inativa: linhas de historico apontam para o Setor.</summary>
  public bool Ativo { get; set; }

  /// <summary>
  /// Substantivo que da nome aos botoes da fila ("montagem" -> "Iniciar montagem"). Nulo: os botoes
  /// ficam "Iniciar" e "Terminar" (spec da Fase 3D, secao 2.3).
  /// </summary>
  public string? Atividade { get; set; }
}
```

`SetorConfiguration.Configure` ganha, depois do `Nome`:

```csharp
    b.Property(s => s.Atividade).HasMaxLength(40);
```

- [ ] **Step 3: Teste de mapeamento que falha.** Crie `tests/Rastreamento.Infrastructure.Tests/Persistence/SetorAtividadeMapeamentoTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>`dbo.Setor.Atividade` (spec da Fase 3D, secao 3.2) faz a volta completa, nula ou nao.</summary>
public class SetorAtividadeMapeamentoTests : TesteComBanco
{
  [Theory]
  [InlineData("montagem")]
  [InlineData(null)]
  public async Task Atividade_faz_a_volta_completa(string? atividade)
  {
    var nome = $"setor-atv-{Guid.NewGuid():N}"[..30];
    int id;
    await using (var db = NovoContexto())
    {
      var setor = new Setor { Nome = nome, Ativo = true, Atividade = atividade };
      db.Setores.Add(setor);
      await db.SaveChangesAsync();
      id = setor.Id;
    }
    try
    {
      await using var leitura = NovoContexto();
      Assert.Equal(atividade, (await leitura.Setores.AsNoTracking().SingleAsync(s => s.Id == id)).Atividade);
    }
    finally
    {
      await using var limpeza = NovoContexto();
      await limpeza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {id}");
    }
  }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter SetorAtividadeMapeamentoTests`
Expected: PASS se o Step 1 e o Step 2 estão aplicados; **para provar que o teste morde**, rode-o uma vez com a linha `HasMaxLength(40)` e a propriedade removidas (não compila) — ou, mais barato, com o `ALTER` ainda não aplicado num banco novo: FAIL com `Invalid column name 'Atividade'`. Registre qual das duas provas fez.

- [ ] **Step 4: DTOs.** Em `Dtos.cs`, seção Setor:

```csharp
public sealed record SetorDto(int Id, string Nome, bool Ativo, string? Atividade);

// (manter o XML doc existente de NovoSetorDto e acrescentar a frase abaixo)
/// `Atividade` e opcional: ausente ou so com espacos grava NULL. `MaxLength` espelha o NVARCHAR(40)
/// de `dbo.Setor.Atividade`.
public sealed record NovoSetorDto([MaxLength(100)] string Nome, [MaxLength(40)] string? Atividade = null);
```

- [ ] **Step 5: Testes do caso de uso que falham.** Em `CadastroDeSetorUseCaseTests.cs`, acrescente (siga o estilo do arquivo; `FakeSetorRepo` vem de `Fakes.cs`):

```csharp
  [Fact]
  public async Task Cadastrar_grava_a_atividade_aparada()
  {
    var repo = new FakeSetorRepo();
    var r = await new CadastroDeSetorUseCase(repo).Cadastrar(new NovoSetorDto("Solda", "  solda "), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal("solda", r.Valor!.Atividade);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task Atividade_ausente_ou_em_branco_grava_nula(string? atividade)
  {
    var repo = new FakeSetorRepo();
    var r = await new CadastroDeSetorUseCase(repo).Cadastrar(new NovoSetorDto("Solda", atividade), CancellationToken.None);

    Assert.Null(r.Valor!.Atividade);
  }

  [Fact]
  public async Task Editar_troca_e_limpa_a_atividade()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true, Atividade = "solda" });
    var uc = new CadastroDeSetorUseCase(repo);

    Assert.Equal("montagem", (await uc.Editar(1, new NovoSetorDto("Solda", "montagem"), CancellationToken.None)).Valor!.Atividade);
    Assert.Null((await uc.Editar(1, new NovoSetorDto("Solda", null), CancellationToken.None)).Valor!.Atividade);
  }

  [Fact]
  public async Task Listar_traz_a_atividade()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true, Atividade = "solda" });

    Assert.Equal("solda", Assert.Single(await new CadastroDeSetorUseCase(repo).Listar(false, CancellationToken.None)).Atividade);
  }
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter CadastroDeSetorUseCaseTests`
Expected: FAIL — `Atividade` sempre nula (o caso de uso ainda não a grava).

- [ ] **Step 6: Caso de uso.** Em `CadastroDeSetorUseCase`:
  - `Cadastrar`: `var setor = new Setor { Nome = nome, Ativo = true, Atividade = NormalizarOpcional(novo.Atividade) };`
  - `Editar`: depois de `setor.Nome = nome;`, `setor.Atividade = NormalizarOpcional(alterado.Atividade);` — o `PUT` é substituição inteira: sem atividade no corpo, ela é limpa (o front sempre manda as duas).
  - Todas as projeções: `new SetorDto(s.Id, s.Nome, s.Ativo, s.Atividade)` (três lugares: `Cadastrar`, `Editar`, `Listar`).
  - Acrescente, junto de `Normalizar`:

```csharp
  /// <summary>
  /// Texto opcional: aparado, e vazio vira nulo — "   " gravado como atividade faria o botao da fila
  /// dizer "Iniciar " com um espaco sobrando. Nao forca minuscula: siglas ("CNC") sao legitimas.
  /// </summary>
  private static string? NormalizarOpcional(string? valor)
  {
    var aparado = valor?.Trim();
    return string.IsNullOrEmpty(aparado) ? null : aparado;
  }
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter CadastroDeSetorUseCaseTests` → PASS.

- [ ] **Step 7: A fila traz a atividade — teste que falha.** Em `CenarioDeExecucao`, o Setor Solda nasce com atividade (os demais continuam sem):

```csharp
      new Setor { Id = Solda, Nome = "Solda", Ativo = true, Atividade = "solda" },
```

Em `ConsultaDeExecucaoUseCaseTests`:

```csharp
  [Fact]
  public async Task A_fila_traz_a_atividade_do_Setor_ou_nula()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal("solda", (await c.Consulta().Fila(Solda, Ct)).Valor!.SetorAtividade);
    Assert.Null((await c.Consulta().Fila(Corte, Ct)).Valor!.SetorAtividade);
  }
```

Expected: não compila (`SetorAtividade` não existe).

- [ ] **Step 8: DTO e consulta.** `FilaDoSetorDto`:

```csharp
public sealed record FilaDoSetorDto(
    int SetorId, string SetorNome, string? SetorAtividade,
    IReadOnlyList<LinhaDaFilaDto> AIniciar,
    IReadOnlyList<LinhaDaFilaDto> EmTrabalho,
    IReadOnlyList<LinhaAguardandoColetaDto> AguardandoColeta,
    IReadOnlyList<MontagemPendenteDto> AguardandoMontagem,
    IReadOnlyList<LinhaDeSobraDto> Sobra);
```

Em `FilaAsync`, o `return` passa a `new FilaDoSetorDto(setorId, setor.Nome, setor.Atividade, aIniciar, …)`. Run o teste do Step 7 → PASS; rode `dotnet test tests/Rastreamento.Application.Tests` inteiro.

- [ ] **Step 9: API — teste que falha e passa.** Em `SetoresEndpointsTests`:

```csharp
  [Fact]
  public async Task Cadastrar_e_editar_com_atividade_e_ela_volta_na_lista()
  {
    var admin = ClienteComo("Administrador");
    var nome = NomeUnico();
    var criado = await admin.PostAsJsonAsync("/api/setores", new { nome, atividade = " montagem " });
    var id = (await criado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    var editado = await admin.PutAsJsonAsync($"/api/setores/{id}", new { nome, atividade = "solda" });
    var lista = await admin.GetFromJsonAsync<JsonElement>("/api/setores");

    Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
    Assert.Equal("solda", lista.EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == id)
        .GetProperty("atividade").GetString());
  }

  [Fact]
  public async Task Atividade_com_mais_de_40_caracteres_da_400()
  {
    var resposta = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/setores", new { nome = NomeUnico(), atividade = new string('a', 41) });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }
```

(Acrescente `using System.Text.Json;` se o arquivo não tiver.) Run: `dotnet test tests/Rastreamento.Api.Tests --filter SetoresEndpointsTests` → PASS.

- [ ] **Step 10: Front — tipos e helper, teste que falha.** Em `web/src/execucao/formatacao.test.ts`:

```ts
describe('rotuloDaAcao', () => {
  it('compõe o verbo com a atividade do Setor', () => {
    expect(rotuloDaAcao('Iniciar', 'montagem')).toBe('Iniciar montagem')
    expect(rotuloDaAcao('Terminar', 'solda')).toBe('Terminar solda')
  })

  it('sem atividade, fica só o verbo', () => {
    expect(rotuloDaAcao('Iniciar', null)).toBe('Iniciar')
    expect(rotuloDaAcao('Terminar', '   ')).toBe('Terminar')
  })
})
```

Em `formatacao.ts`:

```ts
/**
 * O rótulo de um botão da fila: "Iniciar montagem" quando o Setor tem atividade, "Iniciar" quando não
 * tem (spec da Fase 3D, §2.3). O verbo vem fixo — início ou fim —, e a atividade só completa: um verbo
 * por Setor ("Soldar") não diria se o botão começa ou termina.
 */
export function rotuloDaAcao(verbo: 'Iniciar' | 'Terminar', atividade: string | null): string {
  const a = atividade?.trim()
  return a ? `${verbo} ${a}` : verbo
}
```

`web/src/api/execucao.ts`, em `FilaDoSetorDto`, depois de `setorNome`:

```ts
  /** Substantivo que completa os botões ("Iniciar montagem"); `null` = "Iniciar"/"Terminar". */
  setorAtividade: string | null
```

`web/src/testes/execucao.ts`, `fila()`: acrescente `setorAtividade: null,` depois de `setorNome: 'Corte',`.

- [ ] **Step 11: Rótulos na fila.** Em `FilaDoSetorPage.tsx`, importe `rotuloDaAcao` de `../execucao/formatacao`. No começo de `SecoesDaFila`, depois da desestruturação de `acoes`:

```tsx
  const rotuloDeIniciar = rotuloDaAcao('Iniciar', fila.setorAtividade)
  const rotuloDeTerminar = rotuloDaAcao('Terminar', fila.setorAtividade)
```

Troque, na seção "A iniciar aqui", `'Iniciar'` por `rotuloDeIniciar` no `botao(...)` e no `rotulo` do `FormularioDeQuantidade`; na seção "Em trabalho", `'Terminar'` por `rotuloDeTerminar` nos mesmos dois lugares. (O `Montar` fica para a Task 4.)

Teste em `FilaDoSetorPage.test.tsx`, bloco de leitura ou de ações:

```tsx
  it('os botões levam a atividade do Setor quando ele tem uma', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        setorAtividade: 'corte',
        aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }],
        emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 2 }],
      })),
    }))

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar corte SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Terminar corte SUP-01 — Suporte' })).toBeTruthy()
  })
```

(Os `aria-label` dos testes existentes, "Iniciar SUP-01 — Suporte", continuam valendo: a `fila()` padrão tem `setorAtividade: null`.)

- [ ] **Step 12: Cadastro de Setor no front.** `web/src/api/cadastros.ts`:

```ts
export interface SetorDto {
  id: number
  nome: string
  ativo: boolean
  /** Completa os botões da fila ("Iniciar montagem"); `null` = "Iniciar"/"Terminar". */
  atividade: string | null
}

export interface NovoSetor {
  nome: string
  /** Opcional; em branco, o servidor grava nula. */
  atividade: string | null
}

export function criarSetor(s: NovoSetor): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch('/setores', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s),
  }).then(lerOuFalhar<SetorDto>)
}

/** `PUT` é substituição inteira: nome e atividade vão sempre juntos. */
export function editarSetor(id: number, s: NovoSetor): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch(`/setores/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s),
  }).then(lerOuFalhar<SetorDto>)
}
```

`criarSetor` muda de assinatura (`nome` → `NovoSetor`): conserte os chamadores (`SetoresPage`) e os testes de `cadastros.test.ts` que o chamam (acrescente um teste de URL/método/corpo para `editarSetor`, no molde do teste de `criarSetor` — adendo F4: função nova de API nasce com prova de URL).

`SetoresPage.tsx`: o formulário ganha o campo **Atividade (opcional)** e um **modo de edição**. Estado novo:

```tsx
  const [atividade, setAtividade] = useState('')
  const [editando, setEditando] = useState<SetorDto | null>(null)
```

`salvar` passa a:

```tsx
  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    const corpo = { nome, atividade: atividade.trim() === '' ? null : atividade }
    try {
      const resultado = editando ? await editarSetor(editando.id, corpo) : await criarSetor(corpo)
      if (ehConflito(resultado)) {
        if (!editando && resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      limparFormulario()
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o setor.'))
    } finally {
      setEnviando(false)
    }
  }

  function limparFormulario() {
    setNome('')
    setAtividade('')
    setEditando(null)
  }

  function editar(setor: SetorDto) {
    setErro(null)
    setIdReativavel(null)
    setEditando(setor)
    setNome(setor.nome)
    setAtividade(setor.atividade ?? '')
  }
```

(`reativar` continua chamando `setNome('')`; troque por `limparFormulario()`.) O formulário, dentro do `podeEscrever &&`:

```tsx
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Nome do setor">
              {(id) => (
                <input id={id} value={nome} onChange={(e) => setNome(e.target.value)} required className={CLASSES_DE_CONTROLE} />
              )}
            </Campo>
            <Campo
              rotulo="Atividade (opcional)"
              dica="Completa os botões da fila: com “montagem”, eles ficam “Iniciar montagem” e “Terminar montagem”."
            >
              {(id, idDaDica) => (
                <input
                  id={id}
                  value={atividade}
                  onChange={(e) => setAtividade(e.target.value)}
                  maxLength={40}
                  aria-describedby={idDaDica}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <div className="flex flex-wrap gap-2">
            <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…">
              {editando ? 'Salvar alterações' : 'Adicionar'}
            </Botao>
            {editando && <Botao variante="secundario" onClick={limparFormulario}>Cancelar</Botao>}
          </div>
        </form>
```

Na lista, cada `ItemDeCadastro` mostra a atividade e ganha "Editar" ao lado de "Inativar/Reativar":

```tsx
            <ItemDeCadastro
              key={s.id}
              ativo={s.ativo}
              acao={podeEscrever && (
                <div className="flex flex-wrap gap-2">
                  <Botao variante="secundario" aria-label={`Editar ${s.nome}`} onClick={() => editar(s)}>Editar</Botao>
                  <Botao variante="secundario" onClick={() => alternarAtivo(s)}>
                    {s.ativo ? 'Inativar' : 'Reativar'}
                  </Botao>
                </div>
              )}
            >
              {s.nome}
              {s.atividade && <span className="ml-2 text-sm text-tinta-fraca">{`· ${s.atividade}`}</span>}
            </ItemDeCadastro>
```

Confira `Campo` (`web/src/components/Campo.tsx`) — ele já aceita `dica` e passa `idDaDica` à render prop (a `TarefasPage` o usa assim).

Testes em `SetoresPage.test.tsx` (no molde dos existentes): (a) criar manda `{ nome, atividade }` com atividade nula quando o campo fica vazio e com o texto quando preenchido; (b) "Editar" carrega nome e atividade no formulário, "Salvar alterações" faz `PUT /api/setores/:id` com o corpo e recarrega; (c) "Cancelar" volta a "Adicionar" com o formulário vazio; (d) a lista mostra "· montagem" ao lado do nome; (e) quem não escreve não vê "Editar". Acrescente `atividade: null` aos `SetorDto` literais dos 5 arquivos que o `grep` da seção Files lista.

- [ ] **Step 13: Documentação.** `specs/05-api-endpoints.md`: em "Catálogo" (Setores), o corpo de `POST /setores` e `PUT /setores/{id}` passa a `{ nome, atividade? }` e a resposta ganha `atividade`; em "Execução / Rastreamento", a resposta de `GET /setores/{id}/fila` ganha `setorAtividade`. Cite a spec da Fase 3D, §2.3.

- [ ] **Step 14: Verificação.**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm test -- --run && npm run build
```

Expected: 0 warnings; tudo verde. Registre o delta de testes (backend e front) no relatório.

- [ ] **Step 15: Commit.**

```bash
git add specs/02-modelo-de-dados.sql db/alter-fase-3d.sql specs/05-api-endpoints.md src tests web/src
git commit -m "feat(setor): atividade opcional nomeia os botões da fila (Iniciar montagem / Terminar montagem)"
```

---

### Task 2: Iniciar é o verbo único — iniciar um nó com filhos consome os filhos (backend)

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (`CK_Movimentacao_MontagemSoNaBaixa`, índice novo, comentário de `dbo.Montagem`)
- Modify: `db/alter-fase-3d.sql` (bloco 2)
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`ObterInicioDaMontagemAsync`)
- Modify: `src/Rastreamento.Domain/Entities/Montagem.cs` (XML doc)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Application/Execucao/ApontamentoUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/EstornoUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (sai `MontagemNovaDto`; `MontagemPendenteDto` ganha dois campos)
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs` (saem `SemFilhos`, `MontagemAcimaDoQueFalta`)
- Modify: `src/Rastreamento.Application/Execucao/Quantidades.cs` (XML doc de `FormatarExato` cita `ApontamentoUseCase.Iniciar`)
- Modify: `src/Rastreamento.Api/Controllers/ApontamentoController.cs` (sai `Montar`)
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (regras 24 e 28), `specs/05-api-endpoints.md`
- Test: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `ApontamentoUseCaseTests.cs`, `EstornoUseCaseTests.cs`, `ConsultaDeExecucaoUseCaseTests.cs`, `ConservacaoTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/LivroMapeamentoTests.cs`, `ExecucaoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs`, `ExecucaoEndpointsTests.cs`, `ExecucaoEndpointsTests.Comportamento.cs`, `PerfisDeEscritaDeclaradosTests.cs`

**Interfaces:**
- Consumes: `CalculadoraDeExecucao.PrimeiroPasso`, `AguardandoMontagem`, `Saldo`, `TemFilhos` (existentes).
- Produces: `IExecucaoRepository.ObterInicioDaMontagemAsync(int montagemId, CancellationToken) : Task<Movimentacao?>`; `MontagemPendenteDto(NoResumoDto Pai, decimal FaltaMontar, decimal DaParaMontar, bool IniciaAqui, SetorResumoDto? PrimeiroPassoDoPai, IReadOnlyList<FilhoNaMontagemDto> Filhos)`; `ApontamentoUseCase.Iniciar` aceita nó com filhos; `ApontamentoUseCase.Montar` e `MontagemNovaDto` **deixam de existir**.

**Invariante nova, que os testes afirmam:** para todo nó com filhos, **o que saiu de `AIniciar` é igual ao total montado** (`calc.SaidoDeAIniciar(id) == calc.TotalMontado(id)`), porque o pai só entra em produção consumindo os filhos.

- [ ] **Step 1: Schema.** Em `specs/02-modelo-de-dados.sql`, na tabela `dbo.Movimentacao`, `CK_Movimentacao_MontagemSoNaBaixa` passa a:

```sql
    -- MontagemId: obrigatorio na baixa de filho; opcional no Inicio (so o inicio de um pai, que
    -- consumiu os filhos, aponta a Montagem — spec da Fase 3D, secao 3.3); livre no Estorno.
    CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo IN ('Inicio', 'Estorno'))
            OR (Tipo NOT IN ('Montagem', 'Inicio', 'Estorno') AND MontagemId IS NULL)),
```

Na lista de índices (junto de `UX_Movimentacao_EstornoDe`):

```sql
CREATE UNIQUE INDEX UX_Movimentacao_UmInicioPorMontagem
    ON dbo.Movimentacao (MontagemId) WHERE Tipo = 'Inicio' AND MontagemId IS NOT NULL;
```

O comentário de cabeçalho de `dbo.Montagem` passa a:

```sql
-- O inicio de um no com filhos (regra 24; spec da Fase 3D, secao 2.1): iniciar N do pai consome
-- N x QuantidadePorPai de cada filho direto presente no Setor. Esta linha registra o consumo; a
-- baixa de CADA filho fica em dbo.Movimentacao (Tipo = 'Montagem', MontagemId = esta linha) e o
-- Inicio do pai tambem a aponta. O total montado do no e a soma de Quantidade das montagens nao
-- estornadas. Editar a razao depois nao reescreve o passado.
```

`db/alter-fase-3d.sql`, bloco 2:

```sql
/* 2. O Inicio de um pai aponta a Montagem (spec da Fase 3D, secao 3.3) -------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Movimentacao_MontagemSoNaBaixa' AND definition NOT LIKE '%''Inicio''%')
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_MontagemSoNaBaixa')
    ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo IN ('Inicio', 'Estorno'))
            OR (Tipo NOT IN ('Montagem', 'Inicio', 'Estorno') AND MontagemId IS NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Movimentacao_UmInicioPorMontagem')
    CREATE UNIQUE INDEX UX_Movimentacao_UmInicioPorMontagem
        ON dbo.Movimentacao (MontagemId) WHERE Tipo = 'Inicio' AND MontagemId IS NOT NULL;
GO
```

Aplique duas vezes. **Confira a guarda do `DROP` no banco:** `SELECT definition FROM sys.check_constraints WHERE name = 'CK_Movimentacao_MontagemSoNaBaixa'` — o SQL Server normaliza o texto (parênteses, aspas); o `LIKE '%''Inicio''%'` tem de casar com a definição **nova** e não casar com a **antiga**. Se a normalização quebrar o `LIKE`, ajuste o padrão e registre o texto real no relatório. (Detector que devolve zero por não casar nada falha em verde.)

- [ ] **Step 2: Testes de banco que falham.** Em `LivroMapeamentoTests`, junto dos testes de restrição:

```csharp
  [Fact]
  public Task Inicio_de_pai_que_aponta_a_Montagem_e_aceito() => NoCenarioAsync(async c =>
  {
    var montagemId = await NovaMontagemAsync(c);
    var inicio = Inicio(c);
    inicio.MontagemId = montagemId;

    var id = await GravarAsync(inicio);

    await using var leitura = NovoContexto();
    Assert.Equal(montagemId, (await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == id)).MontagemId);
  });

  [Fact]
  public Task Segundo_inicio_na_mesma_Montagem_e_recusado_pelo_indice_unico() => NoCenarioAsync(async c =>
  {
    var montagemId = await NovaMontagemAsync(c);
    var primeiro = Inicio(c);
    primeiro.MontagemId = montagemId;
    await GravarAsync(primeiro);
    var segundo = Inicio(c);
    segundo.MontagemId = montagemId;

    await AfirmarRecusaAsync(segundo, "UX_Movimentacao_UmInicioPorMontagem");
  });

  [Fact]
  public Task Termino_que_aponta_Montagem_e_recusado() => NoCenarioAsync(async c =>
  {
    var montagemId = await NovaMontagemAsync(c);
    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Termino, Quantidade = 1m, MontagemId = montagemId,
      OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
      DestinoPosicao = Posicoes.AguardandoColeta, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_MontagemSoNaBaixa");
  });

  private async Task<int> NovaMontagemAsync(Cenario c)
  {
    await using var db = NovoContexto();
    var montagem = new Montagem
    {
      EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };
    db.Montagens.Add(montagem);
    await db.SaveChangesAsync();
    return montagem.Id;
  }
```

Run **antes** do alter (num banco em que o bloco 2 ainda não rodou) ou revertendo-o à mão: os dois primeiros FAIL (o CHECK antigo recusa `Inicio` com `MontagemId`; o índice não existe). Depois do alter: PASS. Registre a prova de que eles mordem.

Em `ExecucaoRepositoryTests` (usa `NoCenarioAsync`, `Mov` e `GravarAsync` do próprio arquivo):

```csharp
  [Fact]
  public Task Inicio_da_montagem_e_o_do_pai_nunca_a_baixa_e_nulo_sem_ele() => NoCenarioAsync(async c =>
  {
    var naSolda = Local.AguardandoMontagem(c.Solda);
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Corte, 1), 2m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, Local.NoSetor(c.Corte, 1), Local.AguardandoColeta(c.Corte, 1), 2m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(c.Corte, 1), naSolda, 2m));
    var comInicio = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 2m, montagemId: comInicio));
    var inicio = await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 1m,
        montagemId: comInicio));
    var semInicio = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    Assert.Equal(inicio, (await repo.ObterInicioDaMontagemAsync(comInicio, CancellationToken.None))!.Id);
    Assert.Null(await repo.ObterInicioDaMontagemAsync(semInicio, CancellationToken.None));
  });
```

- [ ] **Step 3: Repositório.** `IExecucaoRepository`, junto de `ListarBaixasAsync`:

```csharp
  /// <summary>
  /// O `Inicio` do pai que esta montagem gravou (spec da Fase 3D, secao 3.3), ou nulo — montagem
  /// gravada antes da Fase 3D nao tem. `UX_Movimentacao_UmInicioPorMontagem` garante no maximo um.
  /// </summary>
  Task<Movimentacao?> ObterInicioDaMontagemAsync(int montagemId, CancellationToken ct);
```

`ExecucaoRepository`:

```csharp
  public Task<Movimentacao?> ObterInicioDaMontagemAsync(int montagemId, CancellationToken ct) =>
      _db.Movimentacoes.AsNoTracking()
          .SingleOrDefaultAsync(m => m.Tipo == TiposDeMovimentacao.Inicio && m.MontagemId == montagemId, ct);
```

`FakeExecucaoRepo`:

```csharp
  public Task<Movimentacao?> ObterInicioDaMontagemAsync(int montagemId, CancellationToken ct) =>
      Task.FromResult(Movimentacoes.SingleOrDefault(m => m.Tipo == TiposDeMovimentacao.Inicio && m.MontagemId == montagemId));
```

- [ ] **Step 4: Testes do Iniciar que falham.** Em `ApontamentoUseCaseTests`, **apague a seção `// ---- montar` inteira** (os oito testes `Montar_*`) e ponha no lugar (mantenha o helper `PaiComDoisFilhos`):

```csharp
  // ------------------------------------------------------------------ iniciar um no com filhos

  /// <summary>Filhos 2 (razao 2) e 3 (razao 1) entregues para a montagem de 1 na Solda.</summary>
  private static CenarioDeExecucao ComFilhosNaSolda(decimal presentes2 = 6m, decimal presentes3 = 3m, decimal quantidadeDoPai = 10m)
  {
    var c = PaiComDoisFilhos(quantidadeDoPai);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), presentes2);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), presentes3);
    return c;
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_consome_os_filhos_e_poe_o_pai_no_primeiro_passo()
  {
    var c = ComFilhosNaSolda();

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Inicio, r.Valor!.Tipo);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Solda, "Solda", 1), r.Valor.Destino);
    var montagem = Assert.Single(c.Execucao.Montagens);
    Assert.Equal((1, Solda, 3m), (montagem.EstruturaItemId, montagem.SetorId, montagem.Quantidade));
    Assert.Equal(montagem.Id, r.Valor.MontagemId);
    var baixas = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Montagem).ToList();
    Assert.Equal(new[] { (2, 6m), (3, 3m) }, baixas.Select(b => (b.EstruturaItemId, b.Quantidade)).ToArray());
    Assert.All(baixas, b => Assert.Equal(montagem.Id, b.MontagemId));
    var estado = c.Calcular();
    Assert.Equal(7m, estado.Saldo(1, Local.AIniciar));
    Assert.Equal(3m, estado.Saldo(1, Local.NoSetor(Solda, 1)));
    Assert.Equal(3m, estado.TotalMontado(1));
    Assert.Equal(estado.TotalMontado(1), estado.SaidoDeAIniciar(1));   // a invariante nova
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(new[] { new[] { 1 }, new[] { 2, 3 } }, c.Execucao.Travas.Select(t => t.ToArray()).ToArray());
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_sem_nenhum_presente_da_FilhosInsuficientes_e_nao_grava_nada()
  {
    var c = PaiComDoisFilhos();

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Empty(c.Execucao.Montagens);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Iniciar_no_com_filho_insuficiente_nomeia_o_filho_e_nao_grava_nada()
  {
    var c = ComFilhosNaSolda(presentes2: 6m, presentes3: 1m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Equal("No 3: 1 aqui, 3 necessários.", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_acima_do_a_iniciar_da_SaldoInsuficiente()
  {
    // O pai de 2 ja iniciou 1 (com os filhos); os filhos presentes dariam para mais 2, mas so resta 1.
    var c = ComFilhosNaSolda(presentes2: 6m, presentes3: 3m, quantidadeDoPai: 2m);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Solda, 1), 1m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 2m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 1 de No 1 a iniciar", r.Detalhe);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_fora_do_primeiro_passo_do_pai_da_NaoEhOPrimeiroPasso()
  {
    // Os filhos foram deixados na Pintura, que e o SEGUNDO passo do pai: la o pai nao comeca.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 5m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Pintura, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.NaoEhOPrimeiroPasso, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_recusa_quando_N_vezes_a_razao_passa_de_quatro_casas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 5m, 0.5m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 0.0001m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Contains("No 2", r.Detalhe);
    // pt-BR e SEM arredondar para quatro casas: e a quinta casa que faz o valor nao caber na coluna.
    Assert.Contains("0,00005", r.Detalhe);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_em_Pedido_fechado_da_PedidoFechado()
  {
    var c = ComFilhosNaSolda();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter ApontamentoUseCaseTests` → FAIL (o Iniciar ainda trata o pai como folha: grava sem consumir).

- [ ] **Step 5: `ApontamentoUseCase`.** Troque o XML doc da classe e o método `Iniciar`; **apague `Montar`**:

```csharp
/// <summary>
/// O que o Operador registra no Setor: iniciar (regra 28) e terminar (regra 22) — spec da Fase 3,
/// secoes 4.1 e 4.2, com a emenda da Fase 3D, secao 4.1: iniciar um no COM filhos consome os filhos
/// presentes no Setor, e e o unico jeito de o pai entrar em producao. Cada escrita: valida a
/// entrada, abre a transacao, trava os nos, le o estado e valida com a calculadora, e so entao grava.
/// </summary>
public sealed class ApontamentoUseCase
{
  // (campos e construtor como estao)

  public async Task<Result<MovimentacaoDto>> Iniciar(int noId, InicioDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var no = await _execucao.TravarNosAsync([noId], ct);
      if (no.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      // Os filhos nasceram depois do pai, entao tem Id maior: travar o no e depois eles mantem a
      // ordem crescente de Id da spec da Fase 3, secao 8.1.
      var filhos = await _execucao.ListarFilhosAsync(noId, ct);
      IReadOnlyList<EstruturaItem> travados = filhos.Count == 0
          ? []
          : await _execucao.TravarNosAsync(filhos.Select(f => f.Id), ct);
      // UPDLOCK (nao o ObterPedidoDoNoAsync comum dos outros metodos): este caminho ESCREVE no Pedido
      // a seguir (MarcarPedidoEmProducaoAsync). Ver o XML doc de
      // `ExecucaoRepository.ObterPedidoDoNoParaEscritaAsync` para o deadlock que isto evita.
      var pedido = await _execucao.ObterPedidoDoNoParaEscritaAsync(noId, ct);
      if (Falhas.EstaFechado(pedido)) return Falhas.PedidoFechado<MovimentacaoDto>();

      var estado = await _leitor.CarregarAsync([.. no, .. travados], ct);
      var nome = estado.Nome(noId);
      if (estado.Calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SemRoteiro,
            $"{nome} não tem Roteiro: o PCP precisa definir os passos antes da primeira entrada.");
      if (primeiro.SetorId != dto.SetorId)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.NaoEhOPrimeiroPasso,
            $"O primeiro passo de {nome} não é no Setor {setor.Nome}.");

      var disponivel = estado.Calc.Saldo(noId, Local.AIniciar);
      if (dto.Quantidade > disponivel)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(disponivel)} de {nome} a iniciar.");

      var destino = Local.NoSetor(dto.SetorId, primeiro.Ordem);
      int? montagemId = null;
      if (travados.Count > 0)
      {
        var baixas = new List<(int FilhoId, decimal Quantidade)>();
        var insuficientes = new List<string>();
        foreach (var filho in travados)
        {
          var necessario = dto.Quantidade * (filho.QuantidadePorPai ?? 0m);
          if (!Quantidades.CabeNaColuna(necessario))
            return Falhas.Validacao<MovimentacaoDto>(CodigosDaExecucao.QuantidadeInvalida,
                $"{Quantidades.Formatar(dto.Quantidade)} × {Quantidades.Formatar(filho.QuantidadePorPai ?? 0m)} de "
                + $"{estado.Nome(filho.Id)} dá {Quantidades.FormatarExato(necessario)}, que não cabe em quatro casas decimais.");
          var presente = estado.Calc.AguardandoMontagem(filho.Id, dto.SetorId);
          if (necessario > presente)
            insuficientes.Add($"{estado.Nome(filho.Id)}: {Quantidades.Formatar(presente)} aqui, "
                + $"{Quantidades.Formatar(necessario)} necessários");
          baixas.Add((filho.Id, necessario));
        }
        if (insuficientes.Count > 0)
          return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.FilhosInsuficientes, string.Join("; ", insuficientes) + ".");

        var montagem = new Montagem
        {
          EstruturaItemId = noId, SetorId = dto.SetorId, Quantidade = dto.Quantidade,
          DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
        };
        _execucao.Adicionar(montagem);
        await _execucao.SalvarAlteracoesAsync(ct);   // a baixa e o Inicio precisam do Id da Montagem
        montagemId = montagem.Id;

        foreach (var (filhoId, quantidade) in baixas)
          _execucao.Adicionar(NovoMovimento.De(filhoId, TiposDeMovimentacao.Montagem, quantidade,
              Local.AguardandoMontagem(dto.SetorId), Local.Montado, usuarioId, montagemId: montagemId));
      }

      var inicio = NovoMovimento.De(noId, TiposDeMovimentacao.Inicio, dto.Quantidade,
          Local.AIniciar, destino, usuarioId, montagemId: montagemId);
      _execucao.Adicionar(inicio);
      // Regra 28: o primeiro Inicio de qualquer no poe o Pedido em producao, e o status nao volta.
      await _execucao.MarcarPedidoEmProducaoAsync(pedido!.PedidoId, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([inicio], ct)).Single());
    }, ct);
  }

  // Terminar: sem mudanca.
}
```

Atenção à ordem das travas: o `ObterPedidoDoNoParaEscritaAsync` passa a vir **depois** das travas dos filhos — nós primeiro, Pedido depois, como em todo outro caso de uso. Não inverta.

`ExecucaoDtos.cs`: apague `MontagemNovaDto`. `CodigosDaExecucao`: apague `SemFilhos` e `MontagemAcimaDoQueFalta`. `Quantidades.FormatarExato`: o XML doc passa a citar "a mensagem de N x razao de `ApontamentoUseCase.Iniciar`". `ApontamentoController`: apague a action `Montar`; o XML doc da classe passa a "iniciar e terminar"; o do `PerfisDeEscrita` também.

Run: `dotnet test tests/Rastreamento.Application.Tests --filter ApontamentoUseCaseTests` → PASS.

- [ ] **Step 6: Estorno — testes que falham.** Em `EstornoUseCaseTests`, `MontadoAsync` passa a iniciar o pai:

```csharp
  private static async Task<(CenarioDeExecucao C, MovimentacaoDto InicioDoPai, Montagem Montagem)> MontadoAsync()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(2, 1, 20m, 2m, Corte);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    var inicio = (await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct)).Valor!;
    return (c, inicio, c.Execucao.Montagens.Single());
  }
```

Reescreva os testes da seção `// ---- montagem` para o novo retorno (`montagem.Id` em vez de `montagem.Valor.Id`; as baixas vêm de `c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Montagem)`), e acrescente:

```csharp
  [Fact]
  public async Task Estornar_montagem_desfaz_as_baixas_e_o_inicio_do_pai()
  {
    var (c, inicio, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3, r.Valor!.Count);   // duas baixas e o Inicio do pai
    Assert.Contains(r.Valor, e => e.EstornoDeId == inicio.Id && e.EstruturaItemId == 1);
    var estado = c.Calcular();
    Assert.Equal(10m, estado.Saldo(1, Local.AIniciar));
    Assert.Equal(0m, estado.Saldo(1, Local.NoSetor(Solda, 1)));
    Assert.Equal(0m, estado.TotalMontado(1));
    Assert.Equal(6m, estado.AguardandoMontagem(2, Solda));
    Assert.Equal(3m, estado.AguardandoMontagem(3, Solda));
  }

  [Fact]
  public async Task Estornar_montagem_com_o_pai_ja_terminado_da_EstornoImpossivel()
  {
    var (c, _, montagem) = await MontadoAsync();
    await c.Apontamento().Terminar(1, new TerminoDto(Solda, 1, 3m), Operador, Ct);

    var r = await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains("No 1", r.Detalhe);
    Assert.Null(c.Execucao.Montagens.Single().EstornadaEm);
  }

  [Fact]
  public async Task Estornar_o_inicio_do_pai_avulso_da_EstornoImpossivel_apontando_a_montagem()
  {
    var (c, inicio, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(inicio.Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains($"montagem {montagem.Id}", r.Detalhe);
    Assert.Contains("início", r.Detalhe);
  }

  [Fact]
  public async Task Montagem_gravada_antes_da_3D_sem_inicio_do_pai_estorna_so_as_baixas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 2m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });
    c.Execucao.Semear(new Movimentacao
    {
      EstruturaItemId = 2, Tipo = TiposDeMovimentacao.Montagem, Quantidade = 2m, MontagemId = 900,
      OrigemPosicao = Posicoes.AguardandoMontagem, OrigemSetorId = Solda, DestinoPosicao = Posicoes.Montado,
      DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    var r = await c.Estorno().EstornarMontagem(900, Operador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(2, Assert.Single(r.Valor!).EstruturaItemId);
  }
```

(O teste `Estornar_baixa_de_montagem_avulsa_da_EstornoImpossivel_apontando_a_montagem` continua, com a baixa lida do livro.) Run → FAIL.

- [ ] **Step 7: `EstornoUseCase`.** Em `EstornarMovimentacao`, a guarda de `MontagemId` passa a:

```csharp
    if (original.MontagemId is int montagemId)
      return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
          original.Tipo == TiposDeMovimentacao.Inicio
              ? $"Este início consumiu os filhos na montagem {montagemId}: estorne a montagem inteira."
              : $"Esta baixa faz parte da montagem {montagemId}: estorne a montagem inteira.");
```

`EstornarMontagem`, o corpo da transação:

```csharp
    return await _execucao.ExecutarAsync(async () =>
    {
      var baixas = await _execucao.ListarBaixasAsync([montagemId], ct);
      // Montagem gravada antes da Fase 3D nao tem o Inicio do pai: estorna so as baixas, como antes.
      var inicioDoPai = await _execucao.ObterInicioDaMontagemAsync(montagemId, ct);
      var nos = await _execucao.TravarNosAsync(baixas.Select(b => b.EstruturaItemId).Append(montagem.EstruturaItemId), ct);
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(montagem.EstruturaItemId, ct)))
        return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();
      var atual = await _execucao.ObterMontagemAsync(montagemId, ct);   // relida depois da trava
      if (atual!.EstornadaEm is not null)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta montagem já foi estornada.");

      var estado = await _leitor.CarregarAsync(nos, ct);
      // O pai precisa ainda estar onde esta montagem o pos: se ja terminou ou andou, desfazer o
      // inicio deixaria aquela posicao negativa (spec da Fase 3D, secao 4.2).
      if (inicioDoPai is not null)
      {
        var ali = estado.Calc.Saldo(montagem.EstruturaItemId, Local.DoDestino(inicioDoPai));
        if (ali < inicioDoPai.Quantidade)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
              $"Só há {Quantidades.Formatar(ali)} de {estado.Nome(montagem.EstruturaItemId)} onde esta montagem o pôs: "
              + "ele já andou. Estorne primeiro o que veio depois.");
      }
      foreach (var baixa in baixas)
        // (o comentario do cinto de seguranca existente continua aqui, sem mudanca)
        if (estado.Calc.Saldo(baixa.EstruturaItemId, Local.Montado) < baixa.Quantidade)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
              $"{estado.Nome(baixa.EstruturaItemId)} não tem mais o que esta montagem baixou.");

      var estornos = baixas.Select(b => NovoMovimento.De(b.EstruturaItemId, TiposDeMovimentacao.Estorno, b.Quantidade,
          Local.Montado, Local.DaOrigem(b), usuarioId, montagemId: montagemId, estornoDeId: b.Id)).ToList();
      if (inicioDoPai is not null)
        estornos.Add(NovoMovimento.De(inicioDoPai.EstruturaItemId, TiposDeMovimentacao.Estorno, inicioDoPai.Quantidade,
            Local.DoDestino(inicioDoPai), Local.DaOrigem(inicioDoPai), usuarioId,
            montagemId: montagemId, estornoDeId: inicioDoPai.Id));
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.MarcarMontagemEstornadaAsync(montagemId, usuarioId, DateTime.UtcNow, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
```

XML doc da classe: acrescente "estornar a montagem desfaz tambem o Inicio do pai que ela gravou (Fase 3D)". Run → PASS.

- [ ] **Step 8: Fila — teste que falha.** Em `ConsultaDeExecucaoUseCaseTests`:

```csharp
  [Fact]
  public async Task A_iniciar_nao_lista_no_com_filhos()
  {
    var c = Kit();   // pai 1 na Solda, filhos no Corte

    var solda = (await c.Consulta().Fila(Solda, Ct)).Valor!;
    var corte = (await c.Consulta().Fila(Corte, Ct)).Valor!;

    Assert.Empty(solda.AIniciar);
    Assert.Equal(new[] { 2, 3 }, corte.AIniciar.Select(l => l.No.Id).ToArray());
  }

  [Fact]
  public async Task Aguardando_montagem_diz_se_o_pai_inicia_aqui_e_qual_e_o_primeiro_passo_dele()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null, Solda, Pintura);
    c.No(2, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 1m);

    var naSolda = Assert.Single((await c.Consulta().Fila(Solda, Ct)).Valor!.AguardandoMontagem);
    var naPintura = Assert.Single((await c.Consulta().Fila(Pintura, Ct)).Valor!.AguardandoMontagem);

    Assert.True(naSolda.IniciaAqui);
    Assert.False(naPintura.IniciaAqui);
    Assert.Equal(new SetorResumoDto(Solda, "Solda"), naPintura.PrimeiroPassoDoPai);
  }

  [Fact]
  public async Task Pai_sem_Roteiro_nao_inicia_em_lugar_nenhum()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null);
    c.No(2, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);

    var grupo = Assert.Single((await c.Consulta().Fila(Solda, Ct)).Valor!.AguardandoMontagem);

    Assert.False(grupo.IniciaAqui);
    Assert.Null(grupo.PrimeiroPassoDoPai);
  }
```

O teste `Livro_do_no_traz_os_movimentos_com_a_marca_de_estornado_e_as_montagens_do_pai` iniciava o pai sem filhos presentes — agora isso é `FilhosInsuficientes`. Reescreva o arranjo: entregue 3 do nó 2 na Solda, **inicie** o pai 1 com 2 (consome 2 do filho), estorne a **montagem** (em vez do Início), entregue de novo e inicie 1. Afirme: o livro do nó 1 tem `(Inicio, estornado: true)`, `(Estorno, false)`, `(Inicio, false)` nessa ordem de Id, e duas montagens (a primeira `estornada`).

`Aguardando_montagem_agrupa_por_pai_com_da_para_montar_e_falta` e os demais com `MontagemPendenteDto` continuam; ajuste só construção/desconstrução posicional se houver.

- [ ] **Step 9: `ConsultaDeExecucaoUseCase` e DTO.** `ExecucaoDtos.cs`:

```csharp
/// <summary>
/// Um pai com filhos aguardando neste Setor. `IniciaAqui`: este Setor e o primeiro passo do pai, onde
/// iniciar o pai consome os filhos (spec da Fase 3D, secao 2.1). `PrimeiroPassoDoPai`: para onde levar
/// os filhos quando nao e aqui; nulo se o pai nao tem Roteiro.
/// </summary>
public sealed record MontagemPendenteDto(
    NoResumoDto Pai, decimal FaltaMontar, decimal DaParaMontar, bool IniciaAqui, SetorResumoDto? PrimeiroPassoDoPai,
    IReadOnlyList<FilhoNaMontagemDto> Filhos);
```

Em `FilaAsync`, a condição de "a iniciar aqui" ganha `!calc.TemFilhos(no.Id) &&` no começo, com o comentário `// No com filhos inicia pelo card de montagem, consumindo os filhos (spec da Fase 3D, secao 6.1).`. A montagem do `MontagemPendenteDto`:

```csharp
        .Select(paiId =>
        {
          var m = calc.CalcularMontabilidade(paiId, setorId);
          var primeiro = calc.PrimeiroPasso(paiId);
          return new MontagemPendenteDto(resumos[paiId], m.FaltaMontar, m.DaParaMontar,
              primeiro?.SetorId == setorId,
              primeiro is PassoDoCalculo p ? new SetorResumoDto(p.SetorId, nomes.GetValueOrDefault(p.SetorId, string.Empty)) : null,
              m.Filhos.Select(f => new FilhoNaMontagemDto(
                  resumos[f.FilhoId], f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToList());
        })
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter ConsultaDeExecucaoUseCaseTests` → PASS.

- [ ] **Step 10: Conservação.** Em `ConservacaoTests`:
  - o dicionário `sucessos`: a chave `"montar"` vira `"iniciar pai"`;
  - o `case 7` inteiro passa a:

```csharp
        case 7:
        {
          var pai = sorteio.Next(2) == 0 ? 1 : 2;
          var primeiro = calc.PrimeiroPasso(pai)!.Value;
          operacao = "iniciar pai";
          ok = (await apontamento.Iniciar(pai, new InicioDto(primeiro.SetorId, 1m), Operador, Ct)).Sucesso;
          break;
        }
```

  - em `AfirmarRegra9`, depois da asserção de `TotalMontado <= Quantidade`:

```csharp
      // Fase 3D: o pai so entra em producao consumindo os filhos, entao o que saiu de "a iniciar" e
      // exatamente o total montado — e e isso que faz a trava da regra 24 valer por construcao.
      if (calc.TemFilhos(id))
        Assert.True(calc.SaidoDeAIniciar(id) == calc.TotalMontado(id),
            $"passo {passo} ({operacao}): no {id} saiu {calc.SaidoDeAIniciar(id)} de a iniciar e montou {calc.TotalMontado(id)}");
```

  - o XML doc da classe troca "montar" por "iniciar (inclusive o pai, que consome os filhos)".

Run: `dotnet test tests/Rastreamento.Application.Tests --filter ConservacaoTests`. Se a asserção final ("a semente nunca exercitou 'iniciar pai' com sucesso") falhar, **não a retire**: troque a semente (`new Random(...)`) pela primeira que a satisfaça e registre no relatório as sementes tentadas. Se a nova invariante falhar, é defeito do caso de uso — pare e investigue.

- [ ] **Step 11: API.**
  - `CriterioDeProntoDaFase3Tests`: as duas linhas que iniciam A e montam A viram uma só — `POST /api/estrutura/{c.A}/inicios` com `{ setorId = c.Solda, quantidade = 10m }` —, e acrescente depois das asserções de `Montado` a de A: `await AfirmarPosicoesAsync(c, c.A, ("NoSetor", c.Solda, 1, 10m));`. O XML doc da classe troca "montar" por "iniciar A consumindo B e C".
  - `ExecucaoEndpointsTests.Comportamento.cs`: `Montar_e_estornar_a_montagem` vira:

```csharp
  [Fact]
  public async Task Iniciar_o_pai_consome_os_filhos_e_estornar_a_montagem_desfaz_tudo()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 6m);
    await LevarParaAMontagemAsync(c, c.C, c.Dobra, 3m);

    var inicio = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 3m });
    var montagemId = (await CorpoAsync(inicio)).GetProperty("montagemId").GetInt32();
    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila"));
    var estorno = await c.Como(c.Operador).PostAsync($"/api/montagens/{montagemId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, inicio.StatusCode);
    Assert.Empty(fila.GetProperty("aguardandoMontagem").EnumerateArray());   // consumiu tudo o que havia
    Assert.Equal(c.A, Assert.Single(fila.GetProperty("emTrabalho").EnumerateArray()).GetProperty("no").GetProperty("id").GetInt32());
    Assert.Empty(fila.GetProperty("aIniciar").EnumerateArray().Where(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.A));
    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    Assert.Equal(3, (await CorpoAsync(estorno)).GetArrayLength());   // duas baixas e o Inicio do pai
  }

  [Fact]
  public async Task A_rota_de_montar_nao_existe_mais()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/montagens", new { setorId = c.Solda, quantidade = 1m });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }
```

  (A asserção de `aIniciar` usa `.Where` sobre a lista global: a fila é global, o banco de dev é compartilhado — escope pelo Id do cenário.)
  - `Roteiro_marca_o_alcancado_e_recusa_mexer_nele`: antes de iniciar A, `await LevarParaAMontagemAsync(c, c.B, c.Corte, 2m); await LevarParaAMontagemAsync(c, c.C, c.Dobra, 1m);` — sem os filhos o Iniciar do pai agora é `FilhosInsuficientes`.
  - `ExecucaoEndpointsTests.Perfil_sem_a_acao_recebe_403`: a linha `[InlineData("Gestao", "POST", "/api/estrutura/999999/montagens")]` vira `[InlineData("Gestao", "POST", "/api/estrutura/999999/inicios")]`.
  - `PerfisDeEscritaDeclaradosTests.TabelaAprovada`: apague a entrada `["POST estrutura/{id:int}/montagens"]`.

- [ ] **Step 12: Documentação.**
  - `specs/01-dominio-e-regras-de-negocio.md`, **regra 24**: o primeiro bullet ("Montar é registro próprio…") passa a dizer que **montar não é mais uma ação**: o registro de montagem nasce quando o operador **inicia** o nó com filhos no primeiro passo do Roteiro dele, consumindo `N × QuantidadePorPai` de cada filho direto presente; os tetos continuam (filhos presentes; o que ainda falta, que agora é o próprio saldo "a iniciar" do nó). O bullet da **saída limitada ao total montado** passa a dizer que ela vale **para todo nó por construção** desde a Fase 3D (o pai só entra em produção consumindo os filhos), e que o que sobra à 3B é o conjunto completo (regra 25) e a tarefa Kit pronto. **Regra 28**: acrescente que o início de um nó com filhos é o consumo dos filhos (regra 24). Cite a spec da Fase 3D, §2.1, nas duas. **Releia a seção inteira depois de editar** — a contradição mora entre bullets vizinhos (a frase de abertura da regra 24 ainda diz "montar é registro de todo nó").
  - `specs/05-api-endpoints.md`, "Execução / Rastreamento": `POST /estrutura/{id}/inicios` aceita nó com filhos (consome os filhos; resposta = o Início, com `montagemId`); `POST /estrutura/{id}/montagens` **sai**; `POST /montagens/{id}/estorno` desfaz também o Início do pai; `GET /setores/{id}/fila` — "a iniciar" sem nós com filhos, `iniciaAqui`/`primeiroPassoDoPai` no grupo de montagem. "Contrato de erro da Execução": saem `SemFilhos` e `MontagemAcimaDoQueFalta`.

- [ ] **Step 13: Verificação.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` (verde). `grep -rn "Montar\b\|MontagemNovaDto\|SemFilhos\|MontagemAcimaDoQueFalta" src tests --include=*.cs` → **nenhuma** ocorrência fora de nomes que não sejam a ação removida (confira cada linha). O front não é tocado nesta task (continua compilando: a função `montar` do cliente só some na Task 4).

- [ ] **Step 14: Commit.**

```bash
git add specs db src tests
git commit -m "feat(execucao): iniciar um nó com filhos consome os filhos; montar deixa de ser ação"
```

---

### Task 3: O destino do filho pronto é calculado — o primeiro passo do pai (backend)

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs` (`DestinoCalculado`, `DestinoDaColeta`; saem `SugestaoDeMontagem` e — se nenhum outro chamador sobrar — `SetoresDoRoteiro`)
- Modify: `src/Rastreamento.Application/Execucao/EntregaUseCase.cs` (`Destino`, `ParaAMontagem`)
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`DestinoDto`)
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (`Destino`)
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs` (sai `DestinoForaDoRoteiroDoPai`, entra `RedirecionamentoSemEfeito`)
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (regra 29), `specs/05-api-endpoints.md`
- Test: `tests/Rastreamento.Application.Tests/Execucao/CalculadoraDeExecucaoTests.cs`, `EntregaUseCaseTests.cs`, `ConsultaDeExecucaoUseCaseTests.cs`, `ConservacaoTests.cs`
- Test: `tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs`, `ExecucaoEndpointsTests.Comportamento.cs`

**Interfaces:**
- Produces: `DestinoCalculado(TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId)` com `PaiSemRoteiro => Tipo == Montagem && Passo is null`; no destino `Montagem`, `Passo` é o **primeiro passo do pai**. `DestinoDto(string Tipo, int? SetorId, string? SetorNome, int? Ordem, int? PaiId, bool PaiSemRoteiro)`. Código novo `RedirecionamentoSemEfeito`.

- [ ] **Step 1: Calculadora — testes que falham.** Em `CalculadoraDeExecucaoTests`, apague `Item_no_ultimo_passo_vai_para_a_montagem_do_pai_com_os_Setores_do_Roteiro_dele`, `Sugestao_e_o_Setor_onde_o_pai_esta_em_trabalho_o_de_maior_saldo`, `Sem_pai_em_trabalho_a_sugestao_e_o_primeiro_passo_nao_alcancado` e `Pai_sem_Roteiro_nao_tem_sugestao_nem_Setores`, e ponha:

```csharp
  [Fact]
  public void Item_no_ultimo_passo_vai_para_o_primeiro_passo_do_pai()
  {
    var calc = Calcular([No(1, null, 10m, null, Solda, Pintura, Solda), No(2, 1, 40m, 4m, Corte)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Equal(1, destino.PaiId);
    Assert.Equal<PassoDoCalculo?>(new PassoDoCalculo(Solda, 1), destino.Passo);
    Assert.False(destino.PaiSemRoteiro);
  }

  [Fact]
  public void Pai_sem_Roteiro_nao_tem_destino()
  {
    var calc = Calcular([No(1, null, 10m, null), No(2, 1, 40m, 4m, Dobra)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Null(destino.Passo);
    Assert.True(destino.PaiSemRoteiro);
  }
```

Run → não compila.

- [ ] **Step 2: Calculadora.**

```csharp
/// <summary>
/// Para onde vai o que aguarda coleta (spec da Fase 3, secao 7.3, com a emenda da Fase 3D, secao 2.2).
/// `Passo`: em `ProximoPasso`, o passo seguinte; em `Montagem`, o PRIMEIRO passo do pai — onde o pai
/// comeca consumindo os filhos, sem escolha. `PaiId` so em `Montagem`. Pai sem Roteiro: `Passo` nulo —
/// a entrega e recusada com `PaiSemRoteiro`, e o item aparece assim mesmo (desvio D7 do plano 2 da Fase 3).
/// </summary>
public sealed record DestinoCalculado(TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId)
{
  public bool PaiSemRoteiro => Tipo == TipoDeDestino.Montagem && Passo is null;
}
```

`DestinoDaColeta`:

```csharp
  public DestinoCalculado DestinoDaColeta(int id, int ordem)
  {
    if (ProximoPasso(id, ordem) is PassoDoCalculo proximo)
      return new DestinoCalculado(TipoDeDestino.ProximoPasso, proximo, null);

    if (_nos[id].PaiId is not int paiId)
      return new DestinoCalculado(TipoDeDestino.Expedicao, null, null);

    return new DestinoCalculado(TipoDeDestino.Montagem, PrimeiroPassoDoPai(paiId), paiId);
  }

  /// <summary>O pai precisa estar entre os nos recebidos (o "Contrato de uso" da calculadora); se nao estiver, sem destino.</summary>
  private PassoDoCalculo? PrimeiroPassoDoPai(int paiId) =>
      _nos.TryGetValue(paiId, out var pai) && pai.Roteiro.Count > 0 ? pai.Roteiro[0] : null;
```

Apague `SugestaoDeMontagem`. Rode `grep -rn "SetoresDoRoteiro\|SugestaoDeMontagem" src tests --include=*.cs`: depois do Step 3 e do Step 5, `SetoresDoRoteiro` só tem o próprio corpo — apague-o também. (`PassosAlcancados` fica: `RoteiroDoNoUseCase` o usa — confira com `grep` antes.)

- [ ] **Step 3: Entrega — testes que falham.** Em `EntregaUseCaseTests`:
  - `Item_no_ultimo_passo_vai_aguardar_montagem_no_Setor_escolhido` vira:

```csharp
  [Fact]
  public async Task Item_no_ultimo_passo_vai_aguardar_no_primeiro_passo_do_pai_sem_escolha()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), Assert.Single(r.Valor!).Destino);
  }
```

  - `Item_no_ultimo_passo_sem_destino_da_DestinoIndevido` vira `Item_no_ultimo_passo_com_destino_mandado_da_DestinoIndevido` (manda `Solda` — até o Setor certo é recusado, porque não se escolhe).
  - `Setor_fora_do_Roteiro_do_pai_da_DestinoForaDoRoteiroDoPai`: **apague** (o código sai).
  - `Pai_sem_Roteiro_da_PaiSemRoteiro`: continua igual — as duas chamadas (com `Solda` e com `null`) dão `PaiSemRoteiro`, porque a checagem do pai vem primeiro.
  - `Redirecionar_o_que_aguarda_montagem_para_outro_Setor_do_pai` vira:

```csharp
  [Fact]
  public async Task Redirecionar_leva_ao_primeiro_passo_do_pai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Pintura), null, 6m)), Movimentador, Ct);

    var mov = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Pintura, "Pintura", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), mov.Destino);
  }
```

  - `Redirecionar_para_o_mesmo_Setor_da_DestinoIndevido` vira:

```csharp
  [Fact]
  public async Task Redirecionar_o_que_ja_esta_no_primeiro_passo_do_pai_da_RedirecionamentoSemEfeito()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), null, 6m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.RedirecionamentoSemEfeito, TipoDeErro.Conflito);
    Assert.Contains("Solda", r.Detalhe);
  }

  [Fact]
  public async Task Redirecionar_com_destino_mandado_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Pintura), Solda, 6m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }
```

  - `Lista_e_tudo_ou_nada`: o segundo item passa a `new ItemDaEntregaDto(3, Coleta(Corte, 1), Dobra, 10m)` recusado com **`DestinoIndevido`** (`TipoDeErro.Validacao`); o resto do teste igual.
  - `Nao_ha_teto_de_sobra_na_entrega` e `Trava_todos_os_nos_da_lista_de_uma_vez`: o `Solda` de `destinoSetorId` vira `null`.

Run → FAIL.

- [ ] **Step 4: `EntregaUseCase`.** `Destino`, no ramo `AguardandoColeta`, o `default:` passa a `return ParaAMontagem(estado, item, calculado.PaiId!.Value, origemEmMontagem: null, nomeDoSetor);`. O ramo de redirecionamento (depois do `if` de `AguardandoColeta`) passa a:

```csharp
    // Redirecionamento: o que aguarda montagem fora do primeiro passo do pai vai para ele.
    if (estado.Calc.No(id).PaiId is not int paiId)
      return (null, new Recusa(CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao,
          $"{nome} é uma Peça: não aguarda montagem de ninguém."));
    return ParaAMontagem(estado, item, paiId, origem, nomeDoSetor);
```

`ParaAMontagem`:

```csharp
  /// <summary>
  /// O destino da montagem e o primeiro passo do pai, sem escolha (spec da Fase 3D, secao 2.2). A
  /// ordem das checagens e fixa: pai sem Roteiro sai primeiro, porque e a recusa que diz o que fazer;
  /// depois o destino mandado (o contrato o aceita so para recusa-lo alto, em vez de o JSON o ignorar
  /// em silencio); por ultimo o redirecionamento que nao sairia do lugar.
  /// </summary>
  private static (Local? Destino, Recusa? Recusa) ParaAMontagem(
      EstadoDeExecucao estado, ItemDaEntregaDto item, int paiId, Local? origemEmMontagem, Func<int, string> nomeDoSetor)
  {
    var pai = estado.Nome(paiId);
    if (estado.Calc.PrimeiroPasso(paiId) is not PassoDoCalculo primeiro)
      return (null, new Recusa(CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito,
          $"{pai} não tem Roteiro: o PCP precisa defini-lo antes de receber os filhos."));

    if (item.DestinoSetorId is not null)
      return (null, Indevido(
          $"{estado.Nome(item.EstruturaItemId)} vai para {nomeDoSetor(primeiro.SetorId)}, o primeiro passo de {pai}; esse destino não se escolhe."));

    if (origemEmMontagem?.SetorId == primeiro.SetorId)
      return (null, new Recusa(CodigosDaExecucao.RedirecionamentoSemEfeito, TipoDeErro.Conflito,
          $"{estado.Nome(item.EstruturaItemId)} já aguarda montagem em {nomeDoSetor(primeiro.SetorId)}, o primeiro passo de {pai}."));

    return (Local.AguardandoMontagem(primeiro.SetorId), null);
  }
```

O `nomeDoSetor` precisa conhecer o Setor do primeiro passo do pai, que pode não estar em `setorIds` (hoje ele junta origens e `DestinoSetorId`). Em `Entregar`, acrescente ao `setorIds` os primeiros passos dos pais:

```csharp
      var setorIds = origens.Select(o => o.SetorId!.Value)
          .Concat(travados.Where(n => n.EstruturaPaiId is not null)
              .Select(n => estado.Calc.PrimeiroPasso(n.EstruturaPaiId!.Value)?.SetorId)
              .OfType<int>())
          .Distinct().ToList();
```

(Isto substitui o `Concat` de `DestinoSetorId`, que deixa de apontar destino válido.) `CodigosDaExecucao`: sai `DestinoForaDoRoteiroDoPai`; entra `public const string RedirecionamentoSemEfeito = "RedirecionamentoSemEfeito";`. O XML doc de `ItemDaEntregaDto` ganha: "`DestinoSetorId` fica no contrato só para ser recusado com `DestinoIndevido` quando vier preenchido (desvio D2 do plano da Fase 3D)."

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "EntregaUseCaseTests|CalculadoraDeExecucaoTests"` → PASS.

- [ ] **Step 5: `DestinoDto` e consulta.** `ExecucaoDtos.cs`:

```csharp
/// <summary>
/// `Tipo`: ProximoPasso | Expedicao | Montagem. Em `Montagem`, `SetorId`/`SetorNome` sao o primeiro
/// passo do pai (sem `Ordem`: aguardar montagem nao tem passo) e `PaiSemRoteiro` diz quando nao ha
/// destino possivel.
/// </summary>
public sealed record DestinoDto(string Tipo, int? SetorId, string? SetorNome, int? Ordem, int? PaiId, bool PaiSemRoteiro);
```

`ConsultaDeExecucaoUseCase.Destino`:

```csharp
  private static DestinoDto Destino(DestinoCalculado destino, IReadOnlyDictionary<int, string> nomes) =>
      destino.Tipo switch
      {
        TipoDeDestino.ProximoPasso => new DestinoDto(
            "ProximoPasso", destino.Passo!.Value.SetorId, nomes.GetValueOrDefault(destino.Passo.Value.SetorId),
            destino.Passo.Value.Ordem, null, false),
        TipoDeDestino.Expedicao => new DestinoDto("Expedicao", null, null, null, null, false),
        _ => new DestinoDto(
            "Montagem", destino.Passo?.SetorId,
            destino.Passo is PassoDoCalculo p ? nomes.GetValueOrDefault(p.SetorId) : null,
            null, destino.PaiId, destino.PaiSemRoteiro),
      };
```

`SetorResumoDto` continua (o `MontagemPendenteDto` o usa). Em `ConsultaDeExecucaoUseCaseTests`, `Aguardando_coleta_mostra_a_tarefa_com_destino_e_a_sobra_a_parte` troca as duas linhas de sugestão por `Assert.Equal((Solda, "Solda"), (coleta.Destino.SetorId, coleta.Destino.SetorNome));`, e `Item_pronto_de_pai_sem_Roteiro_aparece_com_paiSemRoteiro` troca as duas por `Assert.Null(tarefa.Destino.SetorId);`.

- [ ] **Step 6: Conservação.** Em `ConservacaoTests`, o `case 4 or 5 or 6` perde o sorteio de destino: o `int? destino = null; if (...) { ... }` inteiro sai, e o item passa `null` como destino. Run → PASS (se a cobertura de "iniciar pai" cair a zero, mesma regra do Step 10 da Task 2).

- [ ] **Step 7: API.** Em todo corpo de `/api/entregas` dos testes de API, `destinoSetorId = (int?)c.Solda` vira `destinoSetorId = (int?)null` (`CriterioDeProntoDaFase3Tests`, `LevarParaAMontagemAsync`). `Entrega_em_lista_devolve_201_e_as_tarefas_acompanham` passa a entregar **um** item de 20 (sem Pintura) e a afirmar `Assert.Equal(c.Solda, tarefa.GetProperty("destino").GetProperty("setorId").GetInt32());` no lugar de `sugestaoSetorId`. Acrescente:

```csharp
  [Fact]
  public async Task Entrega_com_destino_mandado_da_400_DestinoIndevido()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 2m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 2m }));

    var resposta = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 2m } },
    });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("DestinoIndevido", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }
```

- [ ] **Step 8: Documentação.** `01`, **regra 29**, bullet Item: "a montagem do pai, **no primeiro passo do Roteiro dele**, levada pelo Movimentador **sem escolha**; entregue antes de o PCP mudar o primeiro passo, redireciona-se com outra entrega" (spec da Fase 3D, §2.2 — revê a decisão 2.4 da Fase 3). **Releia a regra 24 e a regra 29 juntas** depois da edição. `05`: `POST /entregas` — `destinoSetorId` não se manda (recusado com `DestinoIndevido`); o destino de montagem é o primeiro passo do pai; `GET /tarefas` — `destino` perde `sugestaoSetorId`/`setoresPossiveis`. Contrato de erro: sai `DestinoForaDoRoteiroDoPai`, entra `RedirecionamentoSemEfeito` (409).

- [ ] **Step 9: Verificação e commit.** Build 0 warnings; `dotnet test Rastreamento.slnx -m:1` verde; `grep -rn "Sugestao\|SetoresPossiveis\|DestinoForaDoRoteiroDoPai" src tests --include=*.cs` → nada.

```bash
git add specs src tests
git commit -m "feat(entrega): o filho pronto vai ao primeiro passo do pai, sem escolha"
```

---

### Task 4: Front — o pai inicia no card de montagem; o destino vem calculado

**Files:**
- Modify: `web/src/api/execucao.ts` (`DestinoDto`, `GrupoAguardandoMontagem`, `ItemDaEntrega`; sai `montar`)
- Modify: `web/src/api/erros.ts` (`CodigoDeErroDaExecucao`, `TRADUCAO_DOS_CODIGOS`)
- Modify: `web/src/execucao/formatacao.ts` (`descreverDestino`)
- Modify: `web/src/pages/FilaDoSetorPage.tsx` (reescrita — arquivo inteiro abaixo)
- Delete: `web/src/execucao/FormularioDeRedirecionamento.tsx`
- Modify: `web/src/pages/TarefasPage.tsx`
- Modify: `web/src/execucao/FormularioDeQuantidade.tsx` (só o XML doc de `rotulo` e de `children`)
- Test: `web/src/testes/execucao.ts`, `web/src/pages/FilaDoSetorPage.test.tsx`, `web/src/pages/TarefasPage.test.tsx`, `web/src/execucao/formatacao.test.ts`, `web/src/api/erros.test.ts`, `web/src/api/execucao.test.ts`

**Interfaces:**
- Consumes: contrato da Task 2 (`iniciaAqui`, `primeiroPassoDoPai`) e da Task 3 (`DestinoDto` novo); `rotuloDaAcao` da Task 1.
- Produces: TS `GrupoAguardandoMontagem.iniciaAqui: boolean`, `.primeiroPassoDoPai: SetorResumidoDto | null`; `DestinoDto` sem `sugestaoSetorId`/`setoresPossiveis`; `ItemDaEntrega` sem `destinoSetorId`.

- [ ] **Step 1: Tipos.** Em `api/execucao.ts`:

```ts
/**
 * `ProximoPasso` traz `setorId`/`setorNome`/`ordem`; `Expedicao` não traz nada; `Montagem` traz
 * `paiId` e o Setor do PRIMEIRO passo do pai em `setorId`/`setorNome` (sem `ordem`), porque é lá que o
 * pai começa consumindo os filhos (spec da Fase 3D, §2.2). `paiSemRoteiro` só é `true` em `Montagem`,
 * e aí `setorId` é `null`.
 */
export interface DestinoDto {
  tipo: TipoDeDestino
  setorId: number | null
  setorNome: string | null
  ordem: number | null
  paiId: number | null
  paiSemRoteiro: boolean
}

export interface GrupoAguardandoMontagem {
  pai: NoResumoDto
  faltaMontar: number
  daParaMontar: number
  /** Este Setor é o primeiro passo do pai: é aqui que "Iniciar" o pai consome os filhos. */
  iniciaAqui: boolean
  /** Para onde levar os filhos quando não é aqui; `null` se o pai não tem Roteiro. */
  primeiroPassoDoPai: SetorResumidoDto | null
  /** TODOS os filhos diretos do pai, inclusive os ausentes deste Setor (`presente` 0). */
  filhos: FilhoNaMontagem[]
}

export interface ItemDaEntrega {
  estruturaItemId: number
  origem: OrigemDaEntrega
  quantidade: number
}
```

Apague a função `montar` (e o import de `MontagemDto` que só ela usava, se houver — `MontagemDto` continua exportado, o histórico o usa). O comentário de `ItemDaEntrega` que falava de `destinoSetorId` sai com o campo: o destino é sempre calculado (spec da Fase 3D, §2.2).

`testes/execucao.ts`:

```ts
export function destino(parcial: Partial<DestinoDto> = {}): DestinoDto {
  return {
    tipo: 'ProximoPasso', setorId: 3, setorNome: 'Dobra', ordem: 2, paiId: null, paiSemRoteiro: false,
    ...parcial,
  }
}

export const DESTINO_MONTAGEM = destino({ tipo: 'Montagem', setorId: 4, setorNome: 'Solda', ordem: null, paiId: 2 })
```

- [ ] **Step 2: Códigos de erro.** `api/erros.ts`: na união `CodigoDeErroDaExecucao`, saem `'SemFilhos'`, `'MontagemAcimaDoQueFalta'` e `'DestinoForaDoRoteiroDoPai'`, entra `'RedirecionamentoSemEfeito'`. Em `TRADUCAO_DOS_CODIGOS`, as três linhas saem, e entram:

```ts
  RedirecionamentoSemEfeito: 'Este item já está no Setor onde o pai começa.',
```

e `FilhosInsuficientes` passa a `'Não há filhos suficientes aqui para iniciar essa quantidade.'`. Conserte `erros.test.ts` onde ele enumerar os códigos.

- [ ] **Step 3: `descreverDestino` — teste que falha e implementação.** Em `formatacao.test.ts`, os casos de `Montagem` passam a:

```ts
  it('montagem nomeia o pai e o Setor onde ele começa', () => {
    expect(descreverDestino(DESTINO_MONTAGEM, SUPORTE)).toBe('Montagem de Chassi em Solda')
  })

  it('pai sem Roteiro diz que não há para onde levar', () => {
    expect(descreverDestino(destino({ tipo: 'Montagem', setorId: null, setorNome: null, ordem: null, paiId: 2, paiSemRoteiro: true }), SUPORTE))
      .toBe('Montagem de Chassi — o pai não tem Roteiro')
  })
```

(apague os casos que afirmavam "(sugestão: …)"). Em `formatacao.ts`:

```ts
/**
 * Para onde vai o que aguarda coleta (spec da Fase 3, §7.3, com a emenda da Fase 3D, §2.2). O nome
 * do pai vem do NÓ, não do destino: o `DestinoDto` só traz `paiId`, e a linha da fila e da tarefa já
 * carrega `paiDescricao`.
 */
export function descreverDestino(destino: DestinoDto, no: NoResumoDto): string {
  if (destino.tipo === 'ProximoPasso') return `${destino.setorNome} (passo ${destino.ordem})`
  if (destino.tipo === 'Expedicao') return 'Local de expedição'
  const pai = no.paiDescricao ?? 'o pai'
  if (destino.paiSemRoteiro) return `Montagem de ${pai} — o pai não tem Roteiro`
  return `Montagem de ${pai} em ${destino.setorNome}`
}
```

- [ ] **Step 4: A fila, reescrita.** Substitua `web/src/pages/FilaDoSetorPage.tsx` inteiro por:

```tsx
import { useEffect, useId, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterFila, iniciar, terminar, entregar, ehConflito,
  type FilaDoSetorDto, type GrupoAguardandoMontagem, type LinhaDeSobra, type NoResumoDto,
} from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { caminhoDoNo, descreverDestino, formatarQuantidade, rotuloDaAcao, rotuloDoNo } from '../execucao/formatacao'
import { lembrarSetor } from '../execucao/setorLembrado'
import { usePermissoesDaExecucao } from '../execucao/usePermissoesDaExecucao'
import { FormularioDeQuantidade } from '../execucao/FormularioDeQuantidade'
import type { EstadoDaEscolhaDeSetor } from './FilaPage'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ItemComAcao } from '../components/ItemComAcao'
import { Botao } from '../components/Botao'

const ESCOLHER: EstadoDaEscolhaDeSetor = { escolher: true }

function TrocarDeSetor() {
  return (
    <Link
      to="/fila"
      state={ESCOLHER}
      className="text-sm font-medium rounded underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      Trocar de Setor
    </Link>
  )
}

/** `/fila/:setorId` — a fila de um Setor (spec da Fase 3, §6.1, com a emenda da Fase 3D, §6.1). */
export function FilaDoSetorPage() {
  const { setorId } = useParams<{ setorId: string }>()
  const id = Number(setorId)
  if (!Number.isInteger(id) || id <= 0) {
    return (
      <Pagina titulo="Fila do Setor" acao={<TrocarDeSetor />}>
        <BannerDeErro mensagem="Este Setor não existe." />
      </Pagina>
    )
  }
  // `key`: trocar de Setor desmonta a fila anterior inteira — formulário aberto incluído.
  return <FilaDoSetor key={id} setorId={id} />
}

/**
 * A ação aberta, pela CHAVE da linha que a abriu. Uma por vez: um formulário de quantidade aberto
 * por tela é o que cabe num celular, e é o que deixa "Cancelar" e "Quantidade" sem ambiguidade.
 */
const chaveDeIniciar = (noId: number, ordem: number) => `iniciar:${noId}:${ordem}`
const chaveDeTerminar = (noId: number, ordem: number) => `terminar:${noId}:${ordem}`
const chaveDeIniciarPai = (paiId: number) => `iniciar-pai:${paiId}`
const chaveDeLevar = (paiId: number, filhoId: number) => `levar:${paiId}:${filhoId}`

/** Toda ação que a fila de agora ainda oferece — a que sumiu não pode continuar aberta. */
function chavesDaFila(fila: FilaDoSetorDto): Set<string> {
  const chaves = new Set<string>()
  for (const l of fila.aIniciar) chaves.add(chaveDeIniciar(l.no.id, l.ordem))
  for (const l of fila.emTrabalho) chaves.add(chaveDeTerminar(l.no.id, l.ordem))
  for (const g of fila.aguardandoMontagem) {
    if (g.iniciaAqui && g.daParaMontar > 0) chaves.add(chaveDeIniciarPai(g.pai.id))
    if (!g.iniciaAqui && g.primeiroPassoDoPai !== null) {
      for (const f of g.filhos) if (f.presente > 0) chaves.add(chaveDeLevar(g.pai.id, f.no.id))
    }
  }
  return chaves
}

const SAIU_DA_FILA = 'O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.'

function FilaDoSetor({ setorId }: { setorId: number }) {
  const { dados: fila, carregando, erro, recarregar } = useCargaPeriodica(
    () => obterFila(setorId), setorId, INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar a fila.',
  )
  const [aberta, setAberta] = useState<string | null>(null)
  const [erroDaAcao, setErroDaAcao] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)

  // Lembra só o Setor cuja fila CARREGOU: um Id digitado na barra que dá 404 não vira lembrança.
  useEffect(() => { if (fila?.setorId === setorId) lembrarSetor(setorId) }, [fila, setorId])

  // A atualização (periódica, ou a recarga depois de um 409) tirou da fila a linha cujo formulário
  // está aberto. O formulário fecha, e o aviso — com a recusa do servidor, se foi ela — sobe para o
  // topo da tela, porque o painel onde ele estava deixou de existir.
  useEffect(() => {
    if (fila === null || aberta === null || chavesDaFila(fila).has(aberta)) return
    setAviso(erroDaAcao ?? SAIU_DA_FILA)
    setErroDaAcao(null)
    setAberta(null)
  }, [fila, aberta, erroDaAcao])

  function abrir(chave: string) {
    setAberta(chave)
    setErroDaAcao(null)
    setAviso(null)
  }

  function fechar() {
    setAberta(null)
    setErroDaAcao(null)
  }

  /**
   * Toda escrita passa por aqui: sucesso fecha o formulário e recarrega na hora; recusa mostra a
   * mensagem no próprio formulário e, se for 409, recarrega também — o 409 quase sempre quer dizer
   * que a tela ficou velha (spec da Fase 3, §8.3). O `throw` devolve a recusa ao formulário, que só
   * destrava o botão.
   */
  async function registrar(fazer: () => Promise<unknown>) {
    setErroDaAcao(null)
    try {
      await fazer()
      setAberta(null)
      await recarregar()
    } catch (e) {
      setErroDaAcao(mensagemDeErro(e, 'Não foi possível registrar.'))
      if (ehConflito(e)) await recarregar()
      throw e
    }
  }

  const titulo = fila ? `Fila — ${fila.setorNome}` : 'Fila do Setor'

  return (
    <Pagina titulo={titulo} acao={<TrocarDeSetor />}>
      {/* Com a fila já na tela, este banner é de uma ATUALIZAÇÃO que falhou: a lista abaixo é a da
          última carga boa (a decisão "falha de atualização mantém os dados" de `useCargaPeriodica`). */}
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={aviso} />
      {carregando && <EstadoCarregando />}
      {fila && (
        <SecoesDaFila
          fila={fila}
          acoes={{ aberta, erroDaAcao, abrir, fechar, registrar, setorId }}
        />
      )}
    </Pagina>
  )
}

interface AcoesDaFila {
  aberta: string | null
  erroDaAcao: string | null
  abrir: (chave: string) => void
  fechar: () => void
  registrar: (fazer: () => Promise<unknown>) => Promise<void>
  setorId: number
}

function SecoesDaFila({ fila, acoes }: { fila: FilaDoSetorDto; acoes: AcoesDaFila }) {
  const { apontar, entregar: podeEntregar } = usePermissoesDaExecucao()
  const { aberta, erroDaAcao, abrir, fechar, registrar, setorId } = acoes
  const rotuloDeIniciar = rotuloDaAcao('Iniciar', fila.setorAtividade)
  const rotuloDeTerminar = rotuloDaAcao('Terminar', fila.setorAtividade)

  /** O painel de uma linha: a recusa do servidor, e o formulário embaixo dela. */
  function painel(chave: string, formulario: ReactNode) {
    if (aberta !== chave) return undefined
    return (
      <>
        <BannerDeErro mensagem={erroDaAcao} />
        {formulario}
      </>
    )
  }

  /** O botão que abre a ação; some enquanto ela está aberta (o formulário tem "Cancelar"). */
  function botao(chave: string, rotulo: string, no: NoResumoDto) {
    if (aberta === chave) return undefined
    return (
      <Botao variante="secundario" aria-label={`${rotulo} ${rotuloDoNo(no)}`} onClick={() => abrir(chave)}>
        {rotulo}
      </Botao>
    )
  }

  const vazia = fila.aIniciar.length === 0 && fila.emTrabalho.length === 0
    && fila.aguardandoColeta.length === 0 && fila.aguardandoMontagem.length === 0 && fila.sobra.length === 0
  if (vazia) {
    return (
      <EstadoVazio
        titulo="Nada neste Setor agora"
        descricao="O que chegar para iniciar, trabalhar, coletar ou montar aqui aparece nesta tela."
      />
    )
  }

  return (
    <>
      {fila.aIniciar.length > 0 && (
        <Secao titulo="A iniciar aqui">
          {fila.aIniciar.map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={apontar && botao(chaveDeIniciar(l.no.id, l.ordem), rotuloDeIniciar, l.no)}
              painel={painel(chaveDeIniciar(l.no.id, l.ordem), (
                <FormularioDeQuantidade
                  rotulo={rotuloDeIniciar}
                  maximo={l.quantidade}
                  aoConfirmar={(q) => registrar(() => iniciar(l.no.id, { setorId, quantidade: q }))}
                  aoCancelar={fechar}
                />
              ))}
            >
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} a iniciar · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.emTrabalho.length > 0 && (
        <Secao titulo="Em trabalho">
          {fila.emTrabalho.map((l) => (
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={apontar && botao(chaveDeTerminar(l.no.id, l.ordem), rotuloDeTerminar, l.no)}
              painel={painel(chaveDeTerminar(l.no.id, l.ordem), (
                <FormularioDeQuantidade
                  rotulo={rotuloDeTerminar}
                  maximo={l.quantidade}
                  aoConfirmar={(q) => registrar(() => terminar(l.no.id, { setorId, ordem: l.ordem, quantidade: q }))}
                  aoCancelar={fechar}
                />
              ))}
            >
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} em trabalho · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoColeta.length > 0 && (
        <Secao titulo="Aguardando coleta">
          {fila.aguardandoColeta.map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} aguardando coleta · passo ${l.ordem}`}</Detalhe>
              <Detalhe>{`Destino: ${descreverDestino(l.destino, l.no)}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoMontagem.length > 0 && (
        <Secao titulo="Aguardando montagem">
          {fila.aguardandoMontagem.map((g) => {
            const filhoAberto = g.filhos.find((f) => chaveDeLevar(g.pai.id, f.no.id) === aberta)
            const levarPara = g.primeiroPassoDoPai
            return (
              <ItemComAcao
                key={g.pai.id}
                // O pai começa aqui consumindo os filhos (spec da Fase 3D, §2.1). "Dá para iniciar 0"
                // não oferece o botão: o backend recusaria qualquer N.
                acao={apontar && g.iniciaAqui && g.daParaMontar > 0
                  && botao(chaveDeIniciarPai(g.pai.id), rotuloDeIniciar, g.pai)}
                painel={
                  painel(chaveDeIniciarPai(g.pai.id), (
                    <FormularioDeQuantidade
                      rotulo={rotuloDeIniciar}
                      maximo={g.daParaMontar}
                      aoConfirmar={(q) => registrar(() => iniciar(g.pai.id, { setorId, quantidade: q }))}
                      aoCancelar={fechar}
                    />
                  ))
                  ?? (filhoAberto && levarPara && painel(chaveDeLevar(g.pai.id, filhoAberto.no.id), (
                    <FormularioDeQuantidade
                      rotulo="Levar"
                      maximo={filhoAberto.presente}
                      aoConfirmar={(q) => registrar(() => entregar([{
                        estruturaItemId: filhoAberto.no.id,
                        origem: { posicao: 'AguardandoMontagem', setorId, ordem: null },
                        quantidade: q,
                      }]))}
                      aoCancelar={fechar}
                    />
                  )))
                }
              >
                <GrupoDeMontagem
                  grupo={g}
                  acaoDoFilho={(f) => (podeEntregar && !g.iniciaAqui && levarPara && f.presente > 0
                    ? botao(chaveDeLevar(g.pai.id, f.no.id), `Levar para ${levarPara.nome}`, f.no)
                    : undefined)}
                />
              </ItemComAcao>
            )
          })}
        </Secao>
      )}
      {fila.sobra.length > 0 && (
        <Secao titulo="Sobra">
          {fila.sobra.map((s) => (
            <ItemComAcao key={`${s.no.id}-${s.origem}-${s.ordem ?? ''}`}>
              <CabecalhoDoNo no={s.no} />
              <DetalheDaSobra sobra={s} />
            </ItemComAcao>
          ))}
        </Secao>
      )}
    </>
  )
}

function Secao({ titulo, children }: { titulo: string; children: ReactNode }) {
  const id = useId()
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <h2 id={id} className="text-lg font-medium text-tinta">{titulo}</h2>
      <ListaDeCadastro rotulo={titulo}>{children}</ListaDeCadastro>
    </section>
  )
}

function CabecalhoDoNo({ no }: { no: NoResumoDto }) {
  return (
    <>
      <span className="font-medium text-tinta">{rotuloDoNo(no)}</span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(no)}</span>
    </>
  )
}

function Detalhe({ children }: { children: ReactNode }) {
  return <span className="text-sm text-tinta">{children}</span>
}

/**
 * "Dá para iniciar N; falta iniciar M" (spec da Fase 3, §7.6). O "falta" por filho é o que ele precisa
 * para a unidade N+1, e só existe enquanto há próxima unidade — a API manda `null` quando não há.
 * Fora do primeiro passo do pai, o card diz para onde levar os filhos (spec da Fase 3D, §4.4).
 */
function GrupoDeMontagem({ grupo, acaoDoFilho }: {
  grupo: GrupoAguardandoMontagem
  acaoDoFilho: (filho: GrupoAguardandoMontagem['filhos'][number]) => ReactNode
}) {
  return (
    <>
      <CabecalhoDoNo no={grupo.pai} />
      <Detalhe>
        {`Dá para iniciar ${formatarQuantidade(grupo.daParaMontar)}; falta iniciar ${formatarQuantidade(grupo.faltaMontar)}.`}
      </Detalhe>
      {!grupo.iniciaAqui && (
        <span className="text-xs text-tinta-fraca">
          {grupo.primeiroPassoDoPai
            ? `${grupo.pai.descricao} começa em ${grupo.primeiroPassoDoPai.nome}: leve os filhos para lá.`
            : `${grupo.pai.descricao} não tem Roteiro. Peça ao PCP para cadastrá-lo.`}
        </span>
      )}
      <ul aria-label={`Filhos de ${grupo.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {grupo.filhos.map((f) => (
          <li key={f.no.id} className="flex flex-wrap items-center justify-between gap-2">
            <span>
              {`${rotuloDoNo(f.no)}: ${formatarQuantidade(f.presente)} aqui, ${formatarQuantidade(f.quantidadePorPai)} por unidade`}
              {f.faltaParaProxima !== null && f.faltaParaProxima > 0 && f.necessarioParaProxima !== null
                && ` — falta ${formatarQuantidade(f.faltaParaProxima)} de ${formatarQuantidade(f.necessarioParaProxima)} para a próxima`}
            </span>
            {acaoDoFilho(f)}
          </li>
        ))}
      </ul>
    </>
  )
}

/**
 * A sobra (spec da Fase 3, §7.5, regra 30) é só informada: o descarte é registrado na Fase 5, pelo
 * ator da perda. Quando o filho aguarda montagem em mais de um Setor, o texto diz que não dá para
 * saber em qual está a unidade a mais, em vez de escolher um por conta própria.
 */
function DetalheDaSobra({ sobra }: { sobra: LinhaDeSobra }) {
  const q = formatarQuantidade(sobra.quantidade)
  if (sobra.origem === 'UltimoPasso') {
    return <Detalhe>{`${q} a mais no passo ${sobra.ordem}: o pai já tem o que precisa.`}</Detalhe>
  }
  return (
    <>
      <Detalhe>{`${q} a mais aguardando montagem do que o pai precisa.`}</Detalhe>
      {sobra.emMaisDeUmSetor && (
        <span className="text-xs text-tinta-fraca">
          Este item aguarda montagem em mais de um Setor; não dá para saber em qual está a unidade a mais.
        </span>
      )}
    </>
  )
}
```

Apague `web/src/execucao/FormularioDeRedirecionamento.tsx` (`grep -rn "FormularioDeRedirecionamento" web/src` → nada depois). Em `FormularioDeQuantidade.tsx`, o XML doc de `rotulo` passa a `"Iniciar", "Terminar" (com a atividade do Setor), "Levar"` e o de `children` a `Campos a mais, antes da quantidade.`

- [ ] **Step 5: Testes da fila.** Em `FilaDoSetorPage.test.tsx`:
  - `FILA_CHEIA`: o grupo ganha `iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' },`. Todo outro grupo literal de `aguardandoMontagem` no arquivo ganha os dois campos (os que testam Iniciar: `iniciaAqui: true`; os que testam Levar: `iniciaAqui: false, primeiroPassoDoPai: { id: 4, nome: 'Solda' }`).
  - Textos "Dá para montar N; falta montar M." viram "Dá para iniciar N; falta iniciar M.".
  - `montar oferece o que dá para montar, e monta o pai` vira:

```tsx
  it('o pai inicia no card de montagem com o que dá para iniciar, e a escrita é o inicios do pai', async () => {
    const { fetchMock } = montarFetch([FILA_CHEIA], { '/api/estrutura/2/inicios': () => respostaJson({}, 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '2')
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/estrutura/2/inicios')).toEqual({ setorId: 1, quantidade: 2 }))
  })

  it('com atividade no Setor, o botão do pai também a leva', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({ ...FILA_CHEIA, setorAtividade: 'montagem' })]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar montagem CH-01 — Chassi' })).toBeTruthy()
  })
```

  - O teste do `key={id}` (troca de Setor com o mesmo pai) passa a abrir `'Iniciar CH-01 — Chassi'`; o comentário dele troca `chaveDeMontar` por `chaveDeIniciarPai`.
  - `"dá para montar 0" não oferece Montar` vira `"dá para iniciar 0" não oferece Iniciar` (botão `'Iniciar CH-01 — Chassi'` ausente).
  - Os três testes de "Levar para outro Setor" (o do `<select>`, o de "Roteiro só neste Setor" e o de recusa) são substituídos por:

```tsx
  const FORA_DO_PRIMEIRO_PASSO = fila({
    aguardandoMontagem: [{
      pai: CHASSI, faltaMontar: 10, daParaMontar: 2, iniciaAqui: false, primeiroPassoDoPai: { id: 4, nome: 'Solda' },
      filhos: [
        { no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 },
        { no: PARAFUSO, quantidadePorPai: 1, presente: 0, necessarioParaProxima: 3, faltaParaProxima: 3 },
      ],
    }],
  })

  it('fora do primeiro passo do pai, não há Iniciar e o card diz onde o pai começa', async () => {
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()

    expect(await screen.findByText('Chassi começa em Solda: leve os filhos para lá.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeNull()
  })

  it('o Movimentador leva o filho para o primeiro passo do pai, sem escolher Setor', async () => {
    const { fetchMock } = montarFetch([FORA_DO_PRIMEIRO_PASSO], { '/api/entregas': () => respostaJson([], 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' }))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Levar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/entregas')).toEqual({
      itens: [{ estruturaItemId: 7, origem: { posicao: 'AguardandoMontagem', setorId: 1, ordem: null }, quantidade: 5 }],
    }))
    expect(screen.queryByLabelText('Setor de destino')).toBeNull()
  })

  it('filho ausente não ganha Levar', async () => {
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()
    await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' })

    expect(screen.queryByRole('button', { name: 'Levar para Solda Parafuso' })).toBeNull()
  })

  it('pai sem Roteiro avisa e não oferece Levar', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{ ...FORA_DO_PRIMEIRO_PASSO.aguardandoMontagem[0], primeiroPassoDoPai: null }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByText('Chassi não tem Roteiro. Peça ao PCP para cadastrá-lo.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Levar para/ })).toBeNull()
  })
```

  - O teste de perfil que confere que o Operador não vê "Levar…" e o Movimentador não vê "Montar" (procure por `perfil = 'Operador'` / `'Movimentador'`) passa a usar `'Iniciar CH-01 — Chassi'` e `FORA_DO_PRIMEIRO_PASSO` com `'Levar para Solda SUP-01 — Suporte'`.

Run: `npm test -- --run src/pages/FilaDoSetorPage.test.tsx` → verde.

- [ ] **Step 6: Tarefas sem `<select>`.** Em `TarefasPage.tsx`:
  - `interface Escolha` fica só com `quantidade: string` (o comentário do campo sai).
  - `erroDaEscolha` perde a linha `if (item.destino.tipo === 'Montagem' && escolha.destinoSetorId === null) …`.
  - `alternar`: `return { ...atuais, [chave]: { quantidade: quantidadeParaCampo(item.quantidade) } }` (a linha de `destinoSetorId` sai).
  - `enviar`: o objeto de cada item perde `destinoSetorId`.
  - `EscolhaDoItem`: o bloco `{item.destino.tipo === 'Montagem' && ( … <select> … )}` sai inteiro; sobra o `Campo` de quantidade. `CLASSES_DE_CONTROLE` continua importado (o `<input>` usa).
  - O XML doc de `TarefasPage` ganha: "O destino de montagem vem calculado — o primeiro passo do pai (spec da Fase 3D, §2.2) —, então o Movimentador não escolhe Setor."

Em `TarefasPage.test.tsx`: apague `o Movimentador pode trocar o Setor de montagem sugerido` e `sem sugestão, o Setor de montagem começa vazio e trava a entrega até ser escolhido`; `entrega vários itens numa requisição só, com o Setor de montagem na sugestão, e recarrega` vira `entrega vários itens numa requisição só, sem escolher Setor, e recarrega`, com o corpo esperado sem `destinoSetorId` e uma asserção de que `screen.queryByLabelText('Setor de montagem')` é `null`; o teste de agrupamento confere o destino `'Destino: Montagem de Chassi em Solda'`.

- [ ] **Step 7: Verificação e commit.**

```bash
cd web && npm test -- --run && npm run build
grep -rn "montar(\|sugestaoSetorId\|setoresPossiveis\|destinoSetorId\|FormularioDeRedirecionamento" src
```

Expected: verde; build limpo; o `grep` só pode achar texto em comentário que narre a mudança (confira cada linha; o ideal é nada).

```bash
git add web/src
git commit -m "feat(web): o pai inicia no card de montagem e o destino do filho vem calculado"
```

---

### Task 5: Estornáveis na fila (backend)

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`RegistrosDoSetor`, `ListarRegistrosEstornaveisDoSetorAsync`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`QuemLe`, `EstornavelDto`; `LinhaDaFilaDto`, `LinhaAguardandoColetaDto`, `LinhaDeSobraDto` ganham `Estornaveis`)
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (`Fila`, `FilaAsync`, classe interna `Estornaveis`)
- Modify: `src/Rastreamento.Api/Controllers/ApontamentoController.cs` (`Fila`)
- Modify: `specs/05-api-endpoints.md`
- Test: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `CenarioDeExecucao.cs`, `ConsultaDeExecucaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`

**Interfaces:**
- Produces: `QuemLe(int UsuarioId, bool VeRegistrosDeTodos)`; `EstornavelDto(string Tipo, int Id, decimal Quantidade, int UsuarioId, string UsuarioNome, DateTime DataHora)` — `Tipo`: `Inicio` | `Termino` | `Montagem`; `ConsultaDeExecucaoUseCase.Fila(int setorId, QuemLe quem, CancellationToken ct)`; `RegistrosDoSetor(IReadOnlyList<Movimentacao> Movimentos, IReadOnlyList<Montagem> Montagens)`.

- [ ] **Step 1: Repositório — teste de banco que falha.** Em `ExecucaoRepositoryTests` (usa `NoCenarioAsync`, `Mov` e `GravarAsync` do próprio arquivo; asserção **por Id**, nunca por contagem global):

```csharp
  [Fact]
  public Task Registros_estornaveis_do_Setor_sao_inicios_e_terminos_nao_estornados_e_montagens_validas() => NoCenarioAsync(async c =>
  {
    var corte1 = Local.NoSetor(c.Corte, 1);
    var coleta1 = Local.AguardandoColeta(c.Corte, 1);
    var naSolda = Local.AguardandoMontagem(c.Solda);
    var inicioA = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 5m));
    var terminoA = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, corte1, coleta1, 2m));
    var estornado = await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 3m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Estorno, corte1, Local.AIniciar, 3m, estornoDeId: estornado));
    var entrega = await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, coleta1, naSolda, 2m));   // destino Solda, e e Entrega
    var valida = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    var inicioDoPai = await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 1m,
        montagemId: valida));
    var estornada = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      EstornadaEm = DateTime.UtcNow, EstornadaPorUsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var noCorte = await repo.ListarRegistrosEstornaveisDoSetorAsync(c.Corte, c.Nos, CancellationToken.None);
    var naSoldaRegistros = await repo.ListarRegistrosEstornaveisDoSetorAsync(c.Solda, c.Nos, CancellationToken.None);

    Assert.Equal(new[] { inicioA, terminoA }, noCorte.Movimentos.Select(m => m.Id).ToArray());   // sem o estornado
    Assert.Empty(noCorte.Montagens);
    Assert.DoesNotContain(naSoldaRegistros.Movimentos, m => m.Id == entrega || m.Id == inicioDoPai);   // Entrega e Inicio com Montagem
    Assert.Equal(new[] { valida }, naSoldaRegistros.Montagens.Select(g => g.Id).ToArray());   // sem a estornada
    Assert.DoesNotContain(naSoldaRegistros.Montagens, g => g.Id == estornada);
  });
```

Run → não compila.

- [ ] **Step 2: Repositório.** `IExecucaoRepository.cs`, junto dos records do topo:

```csharp
/// <summary>A materia-prima do estorno rapido da fila (spec da Fase 3D, secao 2.4).</summary>
public sealed record RegistrosDoSetor(IReadOnlyList<Movimentacao> Movimentos, IReadOnlyList<Montagem> Montagens);
```

e na interface:

```csharp
  /// <summary>
  /// Dos nos pedidos: os `Inicio` (sem Montagem — o inicio de um pai se estorna pela montagem) e os
  /// `Termino` com destino neste Setor que ainda nao foram estornados, e as montagens feitas neste
  /// Setor que ainda valem. Se cabe no saldo e quem pode ver, decide o caso de uso.
  /// </summary>
  Task<RegistrosDoSetor> ListarRegistrosEstornaveisDoSetorAsync(int setorId, IReadOnlyCollection<int> ids, CancellationToken ct);
```

`ExecucaoRepository`:

```csharp
  public async Task<RegistrosDoSetor> ListarRegistrosEstornaveisDoSetorAsync(
      int setorId, IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    if (ids.Count == 0) return new RegistrosDoSetor([], []);
    var lista = ids.ToList();
    var movimentos = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId) && m.DestinoSetorId == setorId && m.MontagemId == null
            && (m.Tipo == TiposDeMovimentacao.Inicio || m.Tipo == TiposDeMovimentacao.Termino)
            && !_db.Movimentacoes.Any(e => e.EstornoDeId == m.Id))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
    var montagens = await _db.Montagens.AsNoTracking()
        .Where(g => lista.Contains(g.EstruturaItemId) && g.SetorId == setorId && g.EstornadaEm == null)
        .OrderBy(g => g.Id)
        .ToListAsync(ct);
    return new RegistrosDoSetor(movimentos, montagens);
  }
```

`FakeExecucaoRepo`:

```csharp
  public Task<RegistrosDoSetor> ListarRegistrosEstornaveisDoSetorAsync(
      int setorId, IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var estornados = Movimentacoes.Where(m => m.EstornoDeId is not null).Select(m => m.EstornoDeId!.Value).ToHashSet();
    var movimentos = Movimentacoes
        .Where(m => ids.Contains(m.EstruturaItemId) && m.DestinoSetorId == setorId && m.MontagemId is null
            && (m.Tipo == TiposDeMovimentacao.Inicio || m.Tipo == TiposDeMovimentacao.Termino)
            && !estornados.Contains(m.Id))
        .OrderBy(m => m.Id).ToList();
    var montagens = Montagens
        .Where(g => ids.Contains(g.EstruturaItemId) && g.SetorId == setorId && g.EstornadaEm is null)
        .OrderBy(g => g.Id).ToList();
    return Task.FromResult(new RegistrosDoSetor(movimentos, montagens));
  }
```

Run o teste do Step 1 → PASS.

- [ ] **Step 3: DTOs.** `ExecucaoDtos.cs`:

```csharp
/// <summary>Quem le a fila: o autor ve os proprios registros estornaveis; PCP e Administrador, todos.</summary>
public sealed record QuemLe(int UsuarioId, bool VeRegistrosDeTodos);

/// <summary>
/// Um registro por tras de uma linha da fila que ainda da para estornar (spec da Fase 3D, secao 2.4).
/// `Tipo`: Inicio | Termino | Montagem — `Montagem` e o inicio de um pai, que consumiu os filhos, e se
/// estorna pela rota da montagem.
/// </summary>
public sealed record EstornavelDto(string Tipo, int Id, decimal Quantidade, int UsuarioId, string UsuarioNome, DateTime DataHora);

public sealed record LinhaDaFilaDto(NoResumoDto No, int Ordem, decimal Quantidade, IReadOnlyList<EstornavelDto> Estornaveis);

public sealed record LinhaAguardandoColetaDto(
    NoResumoDto No, int Ordem, decimal Quantidade, DestinoDto Destino, IReadOnlyList<EstornavelDto> Estornaveis);

/// <summary>`Origem`: UltimoPasso (com `Ordem`) | Montagem (sem `Ordem`; excesso no nivel do no).</summary>
public sealed record LinhaDeSobraDto(
    NoResumoDto No, string Origem, int? Ordem, decimal Quantidade, bool EmMaisDeUmSetor, IReadOnlyList<EstornavelDto> Estornaveis);
```

- [ ] **Step 4: Testes da consulta que falham.** Em `CenarioDeExecucao`:

```csharp
  /// <summary>Quem le a fila nos testes: o Operador ve so os seus registros; o PCP ve todos.</summary>
  public static readonly QuemLe ComoOperador = new(Operador, false);
  public static readonly QuemLe ComoPcp = new(Pcp, true);
```

Todas as chamadas de `Fila` passam a levar quem lê:

```bash
sed -i -E 's/\.Fila\(([A-Za-z0-9]+), Ct\)/.Fila(\1, ComoOperador, Ct)/g' tests/Rastreamento.Application.Tests/Execucao/*.cs
grep -rn "\.Fila(" tests/Rastreamento.Application.Tests | grep -v ComoOperador
```

(o `grep` final tem de devolver nada). Acrescente em `ConsultaDeExecucaoUseCaseTests`:

```csharp
  [Fact]
  public async Task Em_trabalho_traz_os_inicios_do_operador_do_mais_recente_ao_mais_antigo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var primeiro = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Operador, Ct)).Valor!;
    var segundo = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 3m), Operador, Ct)).Valor!;

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.Equal(new[] { (segundo.Id, 3m), (primeiro.Id, 2m) }, linha.Estornaveis.Select(e => (e.Id, e.Quantidade)).ToArray());
    Assert.All(linha.Estornaveis, e => Assert.Equal((TiposDeMovimentacao.Inicio, "Operador do Corte"), (e.Tipo, e.UsuarioNome)));
  }

  [Fact]
  public async Task O_que_nao_cabe_mais_no_saldo_nao_e_estornavel()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 5m), Operador, Ct);
    await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 4m), Operador, Ct);   // sobra 1 em trabalho

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.Empty(linha.Estornaveis);   // o Inicio de 5 nao cabe no 1 que restou
  }

  [Fact]
  public async Task Registro_alheio_so_aparece_para_PCP()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Movimentador, Ct);   // autor que nao e quem le

    Assert.Empty(Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho).Estornaveis);
    Assert.Single(Assert.Single((await c.Consulta().Fila(Corte, ComoPcp, Ct)).Valor!.EmTrabalho).Estornaveis);
  }

  [Fact]
  public async Task Aguardando_coleta_traz_os_terminos_e_a_sobra_do_mesmo_passo_nao_os_repete()
  {
    var c = Kit();   // 3 (razao 4) de 45 sob pai de 10: 40 e tarefa, 5 e sobra
    await c.Apontamento().Iniciar(3, new InicioDto(Corte, 45m), Operador, Ct);
    var termino = (await c.Apontamento().Terminar(3, new TerminoDto(Corte, 1, 45m), Operador, Ct)).Valor!;

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Equal(termino.Id, Assert.Single(Assert.Single(fila.AguardandoColeta).Estornaveis).Id);
    Assert.Empty(Assert.Single(fila.Sobra).Estornaveis);
  }

  [Fact]
  public async Task Sobra_sem_tarefa_traz_os_terminos()
  {
    var c = Kit();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 10m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });   // o pai ja nao precisa de nada: tudo o que o 3 terminar e sobra
    await c.Apontamento().Iniciar(3, new InicioDto(Corte, 5m), Operador, Ct);
    var termino = (await c.Apontamento().Terminar(3, new TerminoDto(Corte, 1, 5m), Operador, Ct)).Valor!;

    var fila = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!;

    Assert.Empty(fila.AguardandoColeta);
    Assert.Equal(termino.Id, Assert.Single(Assert.Single(fila.Sobra).Estornaveis).Id);
  }

  [Fact]
  public async Task Em_trabalho_do_pai_traz_as_montagens_e_nao_o_inicio_avulso()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    var linha = Assert.Single((await c.Consulta().Fila(Solda, ComoOperador, Ct)).Valor!.EmTrabalho);

    var estornavel = Assert.Single(linha.Estornaveis);
    Assert.Equal(("Montagem", c.Execucao.Montagens.Single().Id, 3m), (estornavel.Tipo, estornavel.Id, estornavel.Quantidade));
  }

  [Fact]
  public async Task Estornado_nao_volta_a_aparecer()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var inicio = (await c.Apontamento().Iniciar(1, new InicioDto(Corte, 2m), Operador, Ct)).Valor!;
    await c.Apontamento().Iniciar(1, new InicioDto(Corte, 3m), Operador, Ct);
    await c.Estorno().EstornarMovimentacao(inicio.Id, Operador, false, Ct);

    var linha = Assert.Single((await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.EmTrabalho);

    Assert.DoesNotContain(linha.Estornaveis, e => e.Id == inicio.Id);
  }
```

Run → não compila.

- [ ] **Step 5: A consulta.** Em `ConsultaDeExecucaoUseCase`:

```csharp
  public Task<Result<FilaDoSetorDto>> Fila(int setorId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => FilaAsync(setorId, quem, ct), ct);

  private async Task<Result<FilaDoSetorDto>> FilaAsync(int setorId, QuemLe quem, CancellationToken ct)
  {
    var setor = await _setores.ObterPorIdAsync(setorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<FilaDoSetorDto>();

    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;
    var estornaveis = await EstornaveisAsync(setorId, calc, quem, ct);

    var aIniciar = new List<LinhaDaFilaDto>();
    var emTrabalho = new List<LinhaDaFilaDto>();
    var coleta = new List<LinhaAguardandoColetaDto>();
    var sobra = new List<LinhaDeSobraDto>();

    foreach (var no in calc.Nos)
    {
      // No com filhos inicia pelo card de montagem, consumindo os filhos (spec da Fase 3D, secao 6.1).
      if (!calc.TemFilhos(no.Id)
          && calc.PrimeiroPasso(no.Id) is PassoDoCalculo primeiro && primeiro.SetorId == setorId
          && calc.Saldo(no.Id, Local.AIniciar) is var aIniciarAqui && aIniciarAqui > 0m)
        aIniciar.Add(new LinhaDaFilaDto(resumos[no.Id], primeiro.Ordem, aIniciarAqui, []));

      foreach (var (local, quantidade) in calc.Saldos(no.Id))
      {
        if (local.SetorId != setorId || quantidade <= 0m) continue;
        if (local.Posicao == PosicaoConst.NoSetor)
          emTrabalho.Add(new LinhaDaFilaDto(resumos[no.Id], local.Ordem!.Value, quantidade,
              estornaveis.EmTrabalho(no.Id, local, quantidade)));
        else if (local.Posicao == PosicaoConst.AguardandoColeta)
        {
          // D8 do plano 2 da Fase 3: a tarefa aqui, a sobra na secao dela — somadas, dao o saldo do
          // passo. Os Terminos por tras da posicao vao para UMA das duas linhas so: a de coleta quando
          // ela existe, a de sobra quando nao ha tarefa (desvio D4 do plano da Fase 3D).
          var ordem = local.Ordem!.Value;
          var tarefa = calc.Tarefa(no.Id, setorId, ordem);
          var registros = estornaveis.DoLocal(no.Id, local, quantidade);
          if (tarefa > 0m)
            coleta.Add(new LinhaAguardandoColetaDto(resumos[no.Id], ordem, tarefa,
                Destino(calc.DestinoDaColeta(no.Id, ordem), nomes), registros));
          var sobraDaColeta = calc.SobraDaColeta(no.Id, setorId, ordem);
          if (sobraDaColeta > 0m)
            sobra.Add(new LinhaDeSobraDto(resumos[no.Id], UltimoPasso, ordem, sobraDaColeta, false,
                tarefa > 0m ? [] : registros));
        }
        else if (local.Posicao == PosicaoConst.AguardandoMontagem && calc.ExcessoEmMontagem(no.Id) is var excesso && excesso > 0m)
          sobra.Add(new LinhaDeSobraDto(resumos[no.Id], EmMontagem, null, excesso,
              calc.SetoresOndeAguardaMontagem(no.Id).Count > 1, []));
      }
    }

    // (o bloco `var montagem = calc.Nos...` da Task 2 continua igual)

    return Result<FilaDoSetorDto>.Ok(new FilaDoSetorDto(
        setorId, setor.Nome, setor.Atividade, aIniciar, emTrabalho, coleta, montagem,
        sobra.OrderBy(s => s.No.Id).ThenBy(s => s.Ordem ?? int.MaxValue).ToList()));
  }

  private async Task<Estornaveis> EstornaveisAsync(int setorId, CalculadoraDeExecucao calc, QuemLe quem, CancellationToken ct)
  {
    var registros = await _execucao.ListarRegistrosEstornaveisDoSetorAsync(setorId, calc.Nos.Select(n => n.Id).ToList(), ct);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(
        registros.Movimentos.Select(m => m.UsuarioId).Concat(registros.Montagens.Select(g => g.UsuarioId)).Distinct().ToList(), ct);
    return new Estornaveis(calc, registros, autores, quem);
  }

  /// <summary>
  /// Os registros por tras das linhas da fila que ainda da para estornar (spec da Fase 3D, secao 2.4):
  /// nao estornados (o repositorio ja filtra), com a quantidade cabendo no saldo da posicao — o mesmo
  /// corte de `EstornoUseCase` —, e so os que quem le pode estornar: o autor ve os seus; PCP e
  /// Administrador, todos. Do mais recente ao mais antigo.
  /// </summary>
  private sealed class Estornaveis(
      CalculadoraDeExecucao calc, RegistrosDoSetor registros, IReadOnlyDictionary<int, string> autores, QuemLe quem)
  {
    private bool Visivel(int autorId) => quem.VeRegistrosDeTodos || autorId == quem.UsuarioId;

    public IReadOnlyList<EstornavelDto> DoLocal(int noId, Local local, decimal saldo) =>
        registros.Movimentos
            .Where(m => m.EstruturaItemId == noId && Local.DoDestino(m) == local && m.Quantidade <= saldo && Visivel(m.UsuarioId))
            .OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id)
            .Select(m => new EstornavelDto(m.Tipo, m.Id, m.Quantidade, m.UsuarioId,
                autores.GetValueOrDefault(m.UsuarioId, string.Empty), m.DataHora))
            .ToList();

    /// <summary>
    /// Numa folha, os Inicios daquele passo. Num pai, so no PRIMEIRO passo, as montagens — o inicio
    /// dele e a montagem inteira (spec da Fase 3D, secao 4.2). Nos passos seguintes quem pos a
    /// quantidade ali foi uma Entrega, que se estorna pelo historico do no.
    /// </summary>
    public IReadOnlyList<EstornavelDto> EmTrabalho(int noId, Local local, decimal saldo)
    {
      if (!calc.TemFilhos(noId)) return DoLocal(noId, local, saldo);
      if (calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro || primeiro.Ordem != local.Ordem) return [];
      return registros.Montagens
          .Where(g => g.EstruturaItemId == noId && g.SetorId == local.SetorId && g.Quantidade <= saldo && Visivel(g.UsuarioId))
          .OrderByDescending(g => g.DataHora).ThenByDescending(g => g.Id)
          .Select(g => new EstornavelDto("Montagem", g.Id, g.Quantidade, g.UsuarioId,
              autores.GetValueOrDefault(g.UsuarioId, string.Empty), g.DataHora))
          .ToList();
    }
  }
```

Run: `dotnet test tests/Rastreamento.Application.Tests` → PASS.

- [ ] **Step 6: Controller e API.** `ApontamentoController.Fila`:

```csharp
  [HttpGet("setores/{id:int}/fila")]
  public async Task<IActionResult> Fila(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _consulta.Fila(id, new QuemLe(usuarioId, EhPcpOuAdministrador()), ct));
  }
```

Em `ExecucaoEndpointsTests.Comportamento.cs`:

```csharp
  [Fact]
  public async Task A_fila_traz_ao_autor_o_inicio_que_ele_pode_estornar_e_a_Gestao_nao_o_ve()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var inicio = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 2m });
    var inicioId = (await CorpoAsync(inicio)).GetProperty("id").GetInt32();

    JsonElement LinhaDeB(JsonElement fila) => fila.GetProperty("emTrabalho").EnumerateArray()
        .Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    var doOperador = LinhaDeB(await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Corte}/fila")));
    var daGestao = LinhaDeB(await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Corte}/fila")));

    var estornavel = Assert.Single(doOperador.GetProperty("estornaveis").EnumerateArray());
    Assert.Equal(("Inicio", inicioId), (estornavel.GetProperty("tipo").GetString(), estornavel.GetProperty("id").GetInt32()));
    Assert.Empty(daGestao.GetProperty("estornaveis").EnumerateArray());
  }
```

- [ ] **Step 7: Documentação e commit.** `05`, "Execução / Rastreamento", `GET /setores/{id}/fila`: as linhas de "em trabalho", "aguardando coleta" e "sobra" trazem `estornaveis`, filtrados por quem lê (o autor vê os seus; PCP e Administrador, todos) e cortados pelo saldo da posição; cite a spec da Fase 3D, §2.4, e o desvio D4. Build 0 warnings, `dotnet test Rastreamento.slnx -m:1` verde.

```bash
git add specs src tests
git commit -m "feat(fila): cada linha traz os registros que quem lê ainda pode estornar"
```

---

### Task 6: Front — estorno rápido na fila

**Files:**
- Modify: `web/src/api/execucao.ts` (`Estornavel`, `estornaveis` nas linhas, `estornar`)
- Modify: `web/src/execucao/formatacao.ts` (`rotuloDoEstornavel`, `mensagemDoEstorno`)
- Create: `web/src/execucao/ListaDeEstornaveis.tsx`, `web/src/execucao/ListaDeEstornaveis.test.tsx`
- Modify: `web/src/pages/FilaDoSetorPage.tsx`
- Test: `web/src/pages/FilaDoSetorPage.test.tsx`, `web/src/execucao/formatacao.test.ts`, `web/src/api/execucao.test.ts`

**Interfaces:**
- Consumes: contrato da Task 5.
- Produces: TS `Estornavel { tipo: 'Inicio' | 'Termino' | 'Montagem'; id; quantidade; usuarioId; usuarioNome; dataHora }`; `LinhaDaFila.estornaveis`, `LinhaDeSobra.estornaveis`; `estornar(e: Estornavel)`; `ListaDeEstornaveis({ estornaveis, aoEscolher, aoCancelar })`.

- [ ] **Step 1: Tipos e função de API.** Em `api/execucao.ts`:

```ts
/**
 * Um registro por trás de uma linha da fila que ainda dá para estornar (spec da Fase 3D, §2.4). O
 * servidor já filtrou: não estornado, cabe no saldo da posição, e quem lê pode estorná-lo.
 * `Montagem` é o início de um pai, que consumiu os filhos: estorna-se pela rota da montagem.
 */
export interface Estornavel {
  tipo: 'Inicio' | 'Termino' | 'Montagem'
  id: number
  quantidade: number
  usuarioId: number
  usuarioNome: string
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`. */
  dataHora: string
}
```

`LinhaDaFila` ganha `estornaveis: Estornavel[]` (e `LinhaAguardandoColeta`, que a estende, herda); `LinhaDeSobra` ganha `estornaveis: Estornavel[]`. E:

```ts
/** O estorno certo para o registro da fila: o início de um pai é a montagem inteira. */
export function estornar(e: Estornavel): Promise<MovimentacaoDto | MovimentacaoDto[]> {
  return e.tipo === 'Montagem' ? estornarMontagem(e.id) : estornarMovimentacao(e.id)
}
```

Em `api/execucao.test.ts`, dois testes de URL: `estornar({ tipo: 'Termino', id: 41, … })` faz `POST /api/movimentacoes/41/estorno`; `estornar({ tipo: 'Montagem', id: 9, … })` faz `POST /api/montagens/9/estorno`.

Em `FilaDoSetorPage.test.tsx` e em qualquer outro literal de linha de fila (o `grep` da Task 1 contou **9** linhas `{ no: X, ordem: N, quantidade: Q }` e **6** de coleta no arquivo da fila, em 2026-09-28; remeça), acrescente `estornaveis: []`; às sobras literais também.

- [ ] **Step 2: Formatação — teste que falha.** Em `formatacao.test.ts`:

```ts
const TERMINO: Estornavel = {
  tipo: 'Termino', id: 41, quantidade: 5, usuarioId: 12, usuarioNome: 'Operador do Corte', dataHora: '2026-09-28T10:14:00-03:00',
}

describe('rotuloDoEstornavel', () => {
  it('diz o que é, quanto, quem e quando', () => {
    expect(rotuloDoEstornavel(TERMINO)).toBe('Término de 5 · Operador do Corte · 28/09/2026 10:14')
    expect(rotuloDoEstornavel({ ...TERMINO, tipo: 'Inicio' })).toBe('Início de 5 · Operador do Corte · 28/09/2026 10:14')
    expect(rotuloDoEstornavel({ ...TERMINO, tipo: 'Montagem' })).toBe('Início com os filhos de 5 · Operador do Corte · 28/09/2026 10:14')
  })
})

describe('mensagemDoEstorno', () => {
  it('confirma o registro e diz o que acontece', () => {
    expect(mensagemDoEstorno(TERMINO))
      .toBe('Estornar o término de 5, registrado por Operador do Corte em 28/09/2026 10:14? O movimento inverso fica no histórico.')
  })

  it('no início de um pai, avisa que os filhos voltam', () => {
    expect(mensagemDoEstorno({ ...TERMINO, tipo: 'Montagem' }))
      .toBe('Estornar o início com os filhos de 5, registrado por Operador do Corte em 28/09/2026 10:14? Os filhos voltam a aguardar montagem, e o estorno fica no histórico.')
  })
})
```

Em `formatacao.ts` (importe `formatarDataHora` de `../api/cadastros` e o tipo `Estornavel`):

```ts
const NOME_DO_ESTORNAVEL: Record<Estornavel['tipo'], string> = {
  Inicio: 'início',
  Termino: 'término',
  Montagem: 'início com os filhos',
}

/** Uma linha da lista curta de estorno: "Término de 5 · Fulano · 28/09/2026 10:14". */
export function rotuloDoEstornavel(e: Estornavel): string {
  const nome = NOME_DO_ESTORNAVEL[e.tipo]
  return `${nome[0].toUpperCase()}${nome.slice(1)} de ${formatarQuantidade(e.quantidade)} · ${e.usuarioNome} · ${formatarDataHora(e.dataHora)}`
}

/** A pergunta da confirmação: o que se desfaz, e o que acontece depois (spec da Fase 3D, §2.4). */
export function mensagemDoEstorno(e: Estornavel): string {
  const oQue = `Estornar o ${NOME_DO_ESTORNAVEL[e.tipo]} de ${formatarQuantidade(e.quantidade)}, `
    + `registrado por ${e.usuarioNome} em ${formatarDataHora(e.dataHora)}?`
  return e.tipo === 'Montagem'
    ? `${oQue} Os filhos voltam a aguardar montagem, e o estorno fica no histórico.`
    : `${oQue} O movimento inverso fica no histórico.`
}
```

- [ ] **Step 3: A lista curta — teste que falha.** `web/src/execucao/ListaDeEstornaveis.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ListaDeEstornaveis } from './ListaDeEstornaveis'
import type { Estornavel } from '../api/execucao'

afterEach(cleanup)

const DOIS: Estornavel[] = [
  { tipo: 'Termino', id: 42, quantidade: 3, usuarioId: 12, usuarioNome: 'Ana', dataHora: '2026-09-28T10:20:00-03:00' },
  { tipo: 'Termino', id: 41, quantidade: 5, usuarioId: 12, usuarioNome: 'Ana', dataHora: '2026-09-28T10:14:00-03:00' },
]

describe('ListaDeEstornaveis', () => {
  it('lista cada registro na ordem recebida, com um Estornar próprio', () => {
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={() => {}} aoCancelar={() => {}} />)

    expect(screen.getAllByRole('button', { name: /^Estornar / }).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Estornar Término de 3 · Ana · 28/09/2026 10:20',
      'Estornar Término de 5 · Ana · 28/09/2026 10:14',
    ])
  })

  it('escolher entrega o registro ao chamador', () => {
    const aoEscolher = vi.fn()
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={aoEscolher} aoCancelar={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Estornar Término de 5 · Ana · 28/09/2026 10:14' }))

    expect(aoEscolher).toHaveBeenCalledWith(DOIS[1])
  })

  it('cancelar avisa o chamador', () => {
    const aoCancelar = vi.fn()
    render(<ListaDeEstornaveis estornaveis={DOIS} aoEscolher={() => {}} aoCancelar={aoCancelar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(aoCancelar).toHaveBeenCalledOnce()
  })
})
```

`web/src/execucao/ListaDeEstornaveis.tsx`:

```tsx
import type { Estornavel } from '../api/execucao'
import { Botao } from '../components/Botao'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { rotuloDoEstornavel } from './formatacao'

interface Props {
  estornaveis: Estornavel[]
  aoEscolher: (e: Estornavel) => void
  aoCancelar: () => void
}

/**
 * A lista curta do estorno rápido (spec da Fase 3D, §2.4): uma linha da fila é um SALDO, que pode
 * somar vários registros, e o estorno desfaz um movimento inteiro — então o operador escolhe qual.
 * Quem abre esta lista é a fila, e só quando há mais de um; com um só, ela vai direto à confirmação.
 */
export function ListaDeEstornaveis({ estornaveis, aoEscolher, aoCancelar }: Props) {
  return (
    <div className="flex flex-col gap-3 border-t border-borda pt-3">
      <p className="text-sm text-tinta">Qual registro você quer estornar?</p>
      <ListaDeCadastro rotulo="Registros que dá para estornar">
        {estornaveis.map((e) => (
          <ItemComAcao
            key={`${e.tipo}-${e.id}`}
            acao={(
              <Botao variante="secundario" aria-label={`Estornar ${rotuloDoEstornavel(e)}`} onClick={() => aoEscolher(e)}>
                Estornar
              </Botao>
            )}
          >
            <span className="text-sm text-tinta">{rotuloDoEstornavel(e)}</span>
          </ItemComAcao>
        ))}
      </ListaDeCadastro>
      <Botao variante="secundario" onClick={aoCancelar} className="self-start">Cancelar</Botao>
    </div>
  )
}
```

- [ ] **Step 4: A fila com Estornar.** Em `FilaDoSetorPage.tsx`:

Imports: acrescente `estornar, type Estornavel` ao import de `../api/execucao`, `mensagemDoEstorno` ao de `../execucao/formatacao`, `useRef` ao de `react`, e `import { ListaDeEstornaveis } from '../execucao/ListaDeEstornaveis'` e `import { Confirmacao } from '../components/Confirmacao'`.

Chave e `chavesDaFila`:

```tsx
const chaveDeEstornar = (secao: string, noId: number, ordem: number | null) => `estornar:${secao}:${noId}:${ordem ?? ''}`
```

e, em `chavesDaFila`, a lista curta só existe com mais de um registro:

```tsx
  const comLista = (secao: string, noId: number, ordem: number | null, estornaveis: Estornavel[]) => {
    if (estornaveis.length > 1) chaves.add(chaveDeEstornar(secao, noId, ordem))
  }
  for (const l of fila.emTrabalho) comLista('em-trabalho', l.no.id, l.ordem, l.estornaveis)
  for (const l of fila.aguardandoColeta) comLista('coleta', l.no.id, l.ordem, l.estornaveis)
  for (const s of fila.sobra) comLista('sobra', s.no.id, s.ordem, s.estornaveis)
```

Em `FilaDoSetor`, estado e ação do estorno:

```tsx
  const [aConfirmar, setAConfirmar] = useState<Estornavel | null>(null)
  const [estornando, setEstornando] = useState(false)
  // O toque no mesmo quadro, antes do React redesenhar o botão desabilitado — mesmo padrão de
  // `FormularioDeQuantidade` e do histórico do nó.
  const estornandoRef = useRef(false)

  /** Com um registro só, direto à confirmação; com vários, a lista curta abre na linha. */
  function pedirEstorno(chave: string, estornaveis: Estornavel[]) {
    setAviso(null)
    if (estornaveis.length === 1) setAConfirmar(estornaveis[0])
    else abrir(chave)
  }

  /**
   * O estorno não tem formulário na linha (a confirmação é um diálogo), então a recusa sobe como aviso
   * no topo. Sucesso e 409 recarregam; o contador de Tarefas reconta porque toda escrita da execução
   * avisa que o livro mudou.
   */
  async function confirmarEstorno() {
    if (!aConfirmar || estornandoRef.current) return
    estornandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    try {
      await estornar(alvo)
      setAberta(null)
      await recarregar()
    } catch (e) {
      setAberta(null)
      setAviso(mensagemDeErro(e, 'Não foi possível estornar.'))
      if (ehConflito(e)) await recarregar()
    } finally {
      estornandoRef.current = false
      setEstornando(false)
    }
  }
```

O `acoes` passado a `SecoesDaFila` ganha `pedirEstorno, escolherEstorno: setAConfirmar, estornando` (acrescente os três à interface `AcoesDaFila`: `pedirEstorno: (chave: string, estornaveis: Estornavel[]) => void`, `escolherEstorno: (e: Estornavel) => void`, `estornando: boolean`). Depois do `{fila && <SecoesDaFila … />}`:

```tsx
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && mensagemDoEstorno(aConfirmar)}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do livro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmarEstorno}
        aoCancelar={() => setAConfirmar(null)}
      />
```

Em `SecoesDaFila`, pegue `podeEstornar` de `usePermissoesDaExecucao()` e acrescente:

```tsx
  /**
   * "Estornar" da linha: só os registros que a sessão pode estornar (o servidor já filtrou por autor;
   * isto repete a regra para o 403 nunca ser o primeiro aviso). Some enquanto a lista curta dela
   * está aberta, como os outros botões.
   */
  function botaoDeEstorno(chave: string, estornaveis: Estornavel[], no: NoResumoDto) {
    const meus = estornaveis.filter((e) => podeEstornar(e.usuarioId))
    if (meus.length === 0 || aberta === chave) return undefined
    return (
      <Botao
        variante="secundario"
        aria-label={`Estornar ${rotuloDoNo(no)}`}
        disabled={estornando}
        onClick={() => pedirEstorno(chave, meus)}
      >
        Estornar
      </Botao>
    )
  }

  function listaDeEstorno(chave: string, estornaveis: Estornavel[]) {
    return painel(chave, (
      <ListaDeEstornaveis
        estornaveis={estornaveis.filter((e) => podeEstornar(e.usuarioId))}
        aoEscolher={escolherEstorno}
        aoCancelar={fechar}
      />
    ))
  }
```

E nas três seções:

```tsx
      {/* Em trabalho */}
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={(
                <>
                  {apontar && botao(chaveDeTerminar(l.no.id, l.ordem), rotuloDeTerminar, l.no)}
                  {botaoDeEstorno(chaveDeEstornar('em-trabalho', l.no.id, l.ordem), l.estornaveis, l.no)}
                </>
              )}
              painel={
                painel(chaveDeTerminar(l.no.id, l.ordem), ( /* o FormularioDeQuantidade de Terminar, igual */ ))
                ?? listaDeEstorno(chaveDeEstornar('em-trabalho', l.no.id, l.ordem), l.estornaveis)
              }
            >

      {/* Aguardando coleta */}
            <ItemComAcao
              key={`${l.no.id}-${l.ordem}`}
              acao={botaoDeEstorno(chaveDeEstornar('coleta', l.no.id, l.ordem), l.estornaveis, l.no)}
              painel={listaDeEstorno(chaveDeEstornar('coleta', l.no.id, l.ordem), l.estornaveis)}
            >

      {/* Sobra */}
            <ItemComAcao
              key={`${s.no.id}-${s.origem}-${s.ordem ?? ''}`}
              acao={botaoDeEstorno(chaveDeEstornar('sobra', s.no.id, s.ordem), s.estornaveis, s.no)}
              painel={listaDeEstorno(chaveDeEstornar('sobra', s.no.id, s.ordem), s.estornaveis)}
            >
```

(Escreva o `FormularioDeQuantidade` de Terminar por extenso no lugar do comentário — ele não muda.) O `ItemComAcao` só desenha o contêiner de `acao` quando ela é verdadeira: um fragmento vazio é verdadeiro. Se isso deixar um `div` vazio na linha, troque o fragmento por `apontar || … ? <>…</> : undefined` — confira no DOM do teste que a linha sem ação nenhuma continua sem `div` de ação.

- [ ] **Step 5: Testes da fila.** Em `FilaDoSetorPage.test.tsx`, um bloco novo:

```tsx
const ESTORNAVEL = (id: number, quantidade: number, usuarioId = 12): Estornavel => ({
  tipo: 'Termino', id, quantidade, usuarioId, usuarioNome: 'Operador do Corte', dataHora: '2026-09-28T10:14:00-03:00',
})

describe('FilaDoSetorPage — estorno rápido', () => {
  beforeEach(() => {
    perfil = 'Operador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  const COLETA = (estornaveis: Estornavel[]) => fila({
    aguardandoColeta: [{ no: SUPORTE, ordem: 1, quantidade: 8, destino: destino(), estornaveis }],
  })

  it('com um registro só, vai direto à confirmação e estorna o movimento', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COLETA([ESTORNAVEL(41, 5)]), fila()], {
      '/api/movimentacoes/41/estorno': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    expect(screen.getByText(/^Estornar o término de 5, registrado por Operador do Corte/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('Nada neste Setor agora')).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/movimentacoes/41/estorno')).toBe(true)
    expect(getsDaFila()).toBe(2)
  })

  it('com vários, abre a lista curta e estorna o escolhido', async () => {
    const { fetchMock } = montarFetch([COLETA([ESTORNAVEL(42, 3), ESTORNAVEL(41, 5)])], {
      '/api/movimentacoes/41/estorno': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar Término de 5 · Operador do Corte · 28/09/2026 10:14' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/movimentacoes/41/estorno')).toBe(true))
  })

  it('o início de um pai estorna pela rota da montagem', async () => {
    const { fetchMock } = montarFetch([fila({
      emTrabalho: [{ no: CHASSI, ordem: 1, quantidade: 3, estornaveis: [{ ...ESTORNAVEL(9, 3), tipo: 'Montagem' }] }],
    })], { '/api/montagens/9/estorno': () => respostaJson([], 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar CH-01 — Chassi' }))
    expect(screen.getByText(/Os filhos voltam a aguardar montagem/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/montagens/9/estorno')).toBe(true))
  })

  it('recusa do servidor vira aviso no topo e, no 409, recarrega', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COLETA([ESTORNAVEL(41, 5)])], {
      '/api/movimentacoes/41/estorno': () => respostaJson({ erro: 'EstornoImpossivel', mensagem: 'A quantidade já andou.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('A quantidade já andou.')).toBeTruthy()
    expect(getsDaFila()).toBe(2)
  })

  it('sem registro que a sessão possa estornar, não há botão', async () => {
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(41, 5, 99)])]).fetchMock)   // autor 99, sessão 12, Operador

    renderizar()
    await screen.findByText('8 aguardando coleta · passo 1')

    expect(screen.queryByRole('button', { name: 'Estornar SUP-01 — Suporte' })).toBeNull()
  })

  it('cancelar a confirmação não estorna', async () => {
    const { fetchMock } = montarFetch([COLETA([ESTORNAVEL(41, 5)])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/estorno'))).toBe(false)
  })
})
```

(Importe o tipo `Estornavel`. O `vi.mock` de `useAuth` do arquivo usa `id: 12`, que é o autor de `ESTORNAVEL` por padrão.)

- [ ] **Step 6: Verificação e commit.** `cd web && npm test -- --run && npm run build` → verde, build limpo.

```bash
git add web/src
git commit -m "feat(web): estorno rápido na fila, com lista curta e atalho para um registro só"
```

---

### Task 7: Pausa de Pedido (backend, e a permissão no front)

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `dbo.PedidoPausa` e índice), `db/alter-fase-3d.sql` (bloco 3)
- Create: `src/Rastreamento.Domain/Entities/PedidoPausa.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/PedidoPausaConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/PausasAbertas.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs` (`PedidoPausas`)
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`PausaAberta`, `PedidoTravado`; `PedidoDoNo` e `ContextoDoNo` ganham campo; métodos de pausa)
- Modify: `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs` (`ListarPausasAbertasAsync`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`, `PedidoRepository.cs`
- Create: `src/Rastreamento.Application/Execucao/PausaDePedidoUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`PausaResumoDto`, `PausaDto`, `NovaPausaDto`; `NoResumoDto.Pausa`)
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs`, `ApontamentoUseCase.cs` (Iniciar recusa), `ConsultaDeExecucaoUseCase.cs` (resumo e ordem de "a iniciar")
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`PedidoDto.Pausa`), `CadastroDePedidoUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/PausaDePedidoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (DI)
- Modify: `web/src/auth/permissoes.ts`, `web/src/auth/permissoesEspelhamOBackend.test.ts`, `web/src/auth/permissoes.test.ts`
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (regra 31), `specs/05-api-endpoints.md`, `specs/04-fluxos-de-usuario.md`
- Test: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `CenarioDeExecucao.cs`, `PausaDePedidoUseCaseTests.cs` (novo), `ApontamentoUseCaseTests.cs`, `ConsultaDeExecucaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (`FakePedidoRepo`), `CadastroDePedidoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs` (limpeza), `PedidoPausaMapeamentoTests.cs` (novo), `ExecucaoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs` (limpeza), `PausaDePedidoEndpointsTests.cs` (novo), `ExecucaoEndpointsTests.cs`, `PerfisDeEscritaDeclaradosTests.cs`

**Interfaces:**
- Produces: entidade `PedidoPausa`; `PausaAberta(int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo)`; `PedidoTravado(int Id, string Numero, string Status)`; `PedidoDoNo(int PedidoId, string Status, bool Pausado)`; `ContextoDoNo(…, PausaAberta? Pausa)`; `PausaResumoDto(DateTime Desde, string PorUsuarioNome, string? Motivo)` com `static PausaResumoDto? De(PausaAberta?)`; `NoResumoDto(…, PausaResumoDto? Pausa)`; `PedidoDto(…, PausaResumoDto? Pausa)`; `PausaDto(int Id, int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo, DateTime? RetomadoEm, int? RetomadoPorUsuarioId, string? RetomadoPorNome)`; `NovaPausaDto(string? Motivo)`; `PausaDePedidoUseCase.Pausar(int pedidoId, NovaPausaDto dto, int usuarioId, CancellationToken)` e `.Retomar(int pedidoId, int usuarioId, CancellationToken)`, ambos `Task<Result<PausaDto>>`; códigos `PedidoPausado`, `PedidoJaPausado`, `PedidoNaoPausado`, `MotivoLongoDemais`; rotas `POST /pedidos/{id}/pausas` (201) e `POST /pedidos/{id}/retomada` (200), perfis `PCP,Gestao,Administrador`; TS `Recurso` `'pausa'`.

- [ ] **Step 1: Schema.** Em `specs/02-modelo-de-dados.sql`, depois de `dbo.Pedido`/`dbo.Agrupamento` (antes da seção de execução), a tabela e o índice da spec da Fase 3D, §3.1, **exatamente** como estão lá (`dbo.PedidoPausa`, `CK_PedidoPausa_RetomadaCompleta`, `CK_PedidoPausa_RetomadaAposPausa`, `UX_PedidoPausa_UmaAbertaPorPedido`), com o comentário de cabeçalho da spec. O índice vai junto dos demais índices do fim do arquivo.

`db/alter-fase-3d.sql`, bloco 3:

```sql
/* 3. dbo.PedidoPausa (spec da Fase 3D, secao 3.1) ------------------------------------------------ */
IF OBJECT_ID('dbo.PedidoPausa') IS NULL
    CREATE TABLE dbo.PedidoPausa (
        Id                    INT IDENTITY(1,1)  NOT NULL,
        PedidoId              INT                 NOT NULL,
        PausadoEm             DATETIME2           NOT NULL CONSTRAINT DF_PedidoPausa_PausadoEm DEFAULT (SYSUTCDATETIME()),
        PausadoPorUsuarioId   INT                 NOT NULL,
        Motivo                NVARCHAR(200)       NULL,
        RetomadoEm            DATETIME2           NULL,
        RetomadoPorUsuarioId  INT                 NULL,
        CONSTRAINT PK_PedidoPausa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PedidoPausa_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
        CONSTRAINT FK_PedidoPausa_PausadoPorUsuario FOREIGN KEY (PausadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_PedidoPausa_RetomadoPorUsuario FOREIGN KEY (RetomadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_PedidoPausa_RetomadaCompleta
            CHECK ((RetomadoEm IS NULL AND RetomadoPorUsuarioId IS NULL)
                OR (RetomadoEm IS NOT NULL AND RetomadoPorUsuarioId IS NOT NULL)),
        CONSTRAINT CK_PedidoPausa_RetomadaAposPausa CHECK (RetomadoEm IS NULL OR RetomadoEm >= PausadoEm)
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PedidoPausa_UmaAbertaPorPedido')
    CREATE UNIQUE INDEX UX_PedidoPausa_UmaAbertaPorPedido
        ON dbo.PedidoPausa (PedidoId) WHERE RetomadoEm IS NULL;
GO
```

Aplique duas vezes.

- [ ] **Step 2: Entidade, mapeamento, limpeza dos testes de banco.** `PedidoPausa.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Um intervalo em que o Pedido ficou pausado (spec da Fase 3D, secao 2.5). Pausado = existe linha
/// com `RetomadoEm` nulo; `UX_PedidoPausa_UmaAbertaPorPedido` garante no maximo uma. So de inclusao,
/// exceto o fecho do intervalo (`RetomadoEm`/`RetomadoPorUsuarioId`), gravado uma vez — o mesmo
/// desenho de `Montagem.EstornadaEm`. A pausa recusa so o Iniciar.
/// </summary>
public class PedidoPausa
{
  public int Id { get; set; }
  public int PedidoId { get; set; }
  public DateTime PausadoEm { get; set; }
  public int PausadoPorUsuarioId { get; set; }
  public string? Motivo { get; set; }
  public DateTime? RetomadoEm { get; set; }
  public int? RetomadoPorUsuarioId { get; set; }
}
```

`PedidoPausaConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PedidoPausaConfiguration : IEntityTypeConfiguration<PedidoPausa>
{
  public void Configure(EntityTypeBuilder<PedidoPausa> b)
  {
    b.ToTable("PedidoPausa");
    b.HasKey(x => x.Id);
    b.Property(x => x.Motivo).HasMaxLength(200);
    // Mesmo motivo de MontagemConfiguration: sem isto, um caso de uso que esquecer PausadoEm grava
    // 0001-01-01 e DF_PedidoPausa_PausadoEm nunca dispara.
    b.Property(x => x.PausadoEm).ValueGeneratedOnAdd();
  }
}
```

`RastreamentoDbContext`: `public DbSet<PedidoPausa> PedidoPausas => Set<PedidoPausa>();` (confira se as configurações são aplicadas por `ApplyConfigurationsFromAssembly`; se forem registradas uma a uma, registre esta). Em `ArvoreDeTesteNoBanco.LimparAsync` e em `CenarioDaFase3NaApi.DisposeAsync`, **antes** do `DELETE FROM dbo.Pedido`:

```csharp
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.PedidoPausa WHERE PedidoId = {PedidoId}");
```

`PedidoPausaMapeamentoTests.cs` (novo, `TesteComBanco` + `ArvoreDeTesteNoBanco`, limpeza no `finally`): (a) a volta completa com motivo, e a retomada gravada; (b) segunda pausa **aberta** do mesmo Pedido recusada por `UX_PedidoPausa_UmaAbertaPorPedido`, e uma segunda depois da primeira **fechada** aceita; (c) `RetomadoEm` sem `RetomadoPorUsuarioId` recusado por `CK_PedidoPausa_RetomadaCompleta`; (d) `RetomadoEm < PausadoEm` recusado por `CK_PedidoPausa_RetomadaAposPausa`. No molde de `LivroMapeamentoTests` (`DbUpdateException` e o nome da restrição na mensagem).

- [ ] **Step 3: Contratos do repositório.** `IExecucaoRepository.cs`:

```csharp
/// <summary>A pausa aberta de um Pedido, com o nome de quem pausou (spec da Fase 3D, secao 2.5).</summary>
public sealed record PausaAberta(int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo);

public sealed record ContextoDoNo(
    EstruturaItem No, int PedidoId, string PedidoNumero, int AgrupamentoId, string AgrupamentoCodigo, PausaAberta? Pausa);

/// <summary>`Pausado`: o Pedido tem pausa aberta — o Iniciar a recusa (spec da Fase 3D, secao 4.1).</summary>
public sealed record PedidoDoNo(int PedidoId, string Status, bool Pausado);

public sealed record PedidoTravado(int Id, string Numero, string Status);
```

e na interface:

```csharp
  /// <summary>
  /// Trava a linha do Pedido com UPDLOCK — a mesma que o Iniciar trava por
  /// <see cref="ObterPedidoDoNoParaEscritaAsync"/> —, para pausar e iniciar se serializarem (spec da
  /// Fase 3D, secao 4.5). So vale dentro de <see cref="EmTransacaoAsync"/>.
  /// </summary>
  Task<PedidoTravado?> TravarPedidoAsync(int pedidoId, CancellationToken ct);

  Task<PedidoPausa?> ObterPausaAbertaAsync(int pedidoId, CancellationToken ct);

  /// <summary>Fecha o intervalo: conjuntista e condicionado a `RetomadoEm IS NULL`, nunca sobrescreve.</summary>
  Task FecharPausaAsync(int pausaId, int usuarioId, DateTime em, CancellationToken ct);

  void Adicionar(PedidoPausa pausa);
```

`IPedidoRepository`:

```csharp
  /// <summary>A pausa aberta de cada Pedido pedido que tem uma; os demais nao aparecem.</summary>
  Task<IReadOnlyDictionary<int, PausaAberta>> ListarPausasAbertasAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);
```

- [ ] **Step 4: Implementações.** `PausasAbertas.cs` (uma consulta só, para os dois repositórios não divergirem):

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>A consulta de pausa aberta, compartilhada por `ExecucaoRepository` e `PedidoRepository`.</summary>
internal static class PausasAbertas
{
  public static async Task<IReadOnlyDictionary<int, PausaAberta>> ListarAsync(
      RastreamentoDbContext db, IReadOnlyCollection<int> pedidoIds, CancellationToken ct)
  {
    if (pedidoIds.Count == 0) return new Dictionary<int, PausaAberta>();
    var lista = pedidoIds.ToList();
    return await (from pa in db.PedidoPausas.AsNoTracking()
                  join u in db.Usuarios.AsNoTracking() on pa.PausadoPorUsuarioId equals u.Id
                  where pa.RetomadoEm == null && lista.Contains(pa.PedidoId)
                  select new PausaAberta(pa.PedidoId, pa.PausadoEm, pa.PausadoPorUsuarioId, u.NomeCompleto, pa.Motivo))
        .ToDictionaryAsync(p => p.PedidoId, ct);
  }
}
```

`PedidoRepository`:

```csharp
  public Task<IReadOnlyDictionary<int, PausaAberta>> ListarPausasAbertasAsync(
      IReadOnlyCollection<int> pedidoIds, CancellationToken ct) =>
      PausasAbertas.ListarAsync(_db, pedidoIds, ct);
```

`ExecucaoRepository`:

```csharp
  public async Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct)
  {
    var linhas = await (from e in _db.Estruturas.AsNoTracking()
                        join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                        join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
                        where p.Status != StatusConcluido && p.Status != StatusCancelado
                        orderby e.Id
                        select new { No = e, PedidoId = p.Id, p.Numero, AgrupamentoId = a.Id, a.Codigo })
        .ToListAsync(ct);
    var pausas = await PausasAbertas.ListarAsync(_db, linhas.Select(l => l.PedidoId).Distinct().ToList(), ct);
    return linhas.Select(l => new ContextoDoNo(
        l.No, l.PedidoId, l.Numero, l.AgrupamentoId, l.Codigo, pausas.GetValueOrDefault(l.PedidoId))).ToList();
  }

  public async Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct)
  {
    var pedido = await (from e in _db.Estruturas.AsNoTracking()
                        join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                        join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
                        where e.Id == estruturaItemId
                        select new { p.Id, p.Status })
        .SingleOrDefaultAsync(ct);
    return pedido is null ? null : new PedidoDoNo(pedido.Id, pedido.Status, await PausadoAsync(pedido.Id, ct));
  }

  // ObterPedidoDoNoParaEscritaAsync: o return passa a
  //   return pedido is null ? null : new PedidoDoNo(pedido.Id, pedido.Status, await PausadoAsync(pedido.Id, ct));
  // Sob a transacao Serializable do Iniciar, esta leitura tambem trava a faixa da pausa daquele
  // Pedido: um Pausar concorrente espera o Iniciar terminar (e ja esperaria pelo UPDLOCK no Pedido).

  private Task<bool> PausadoAsync(int pedidoId, CancellationToken ct) =>
      _db.PedidoPausas.AsNoTracking().AnyAsync(p => p.PedidoId == pedidoId && p.RetomadoEm == null, ct);

  public async Task<PedidoTravado?> TravarPedidoAsync(int pedidoId, CancellationToken ct)
  {
    if (_db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
          "TravarPedidoAsync so vale dentro de EmTransacaoAsync: fora de transacao a trava acaba no fim do SELECT.");
    var pedido = await _db.Pedidos
        .FromSql($"SELECT * FROM dbo.Pedido WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE Id = {pedidoId}")
        .AsNoTracking()
        .SingleOrDefaultAsync(ct);
    return pedido is null ? null : new PedidoTravado(pedido.Id, pedido.Numero, pedido.Status);
  }

  public Task<PedidoPausa?> ObterPausaAbertaAsync(int pedidoId, CancellationToken ct) =>
      _db.PedidoPausas.AsNoTracking().SingleOrDefaultAsync(p => p.PedidoId == pedidoId && p.RetomadoEm == null, ct);

  public Task FecharPausaAsync(int pausaId, int usuarioId, DateTime em, CancellationToken ct) =>
      _db.PedidoPausas.Where(p => p.Id == pausaId && p.RetomadoEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(p => p.RetomadoEm, (DateTime?)em)
              .SetProperty(p => p.RetomadoPorUsuarioId, (int?)usuarioId), ct);

  public void Adicionar(PedidoPausa pausa) => _db.PedidoPausas.Add(pausa);
```

O XML doc do `IExecucaoRepository` (topo) diz que as "duas únicas atualizações" são conjuntistas: passam a ser **três** (`FecharPausaAsync`). Conserte a frase.

`FakeExecucaoRepo`:

```csharp
  public List<PedidoPausa> Pausas { get; } = new();
  private readonly List<PedidoPausa> _pausasPendentes = new();

  // ListarNosEmProducaoAsync: o ContextoDoNo ganha PausaDoPedido(ag.PedidoId) no fim.
  // ObterPedidoDoNoAsync: new PedidoDoNo(ag.PedidoId, StatusDoPedido[ag.PedidoId], PausaDoPedido(ag.PedidoId) is not null)

  private PausaAberta? PausaDoPedido(int pedidoId) =>
      Pausas.Where(p => p.PedidoId == pedidoId && p.RetomadoEm is null)
          .Select(p => new PausaAberta(p.PedidoId, p.PausadoEm, p.PausadoPorUsuarioId,
              Usuarios.GetValueOrDefault(p.PausadoPorUsuarioId, string.Empty), p.Motivo))
          .SingleOrDefault();

  public Task<PedidoTravado?> TravarPedidoAsync(int pedidoId, CancellationToken ct)
  {
    if (!_emTransacao) throw new InvalidOperationException("TravarPedidoAsync fora de EmTransacaoAsync.");
    if (!StatusDoPedido.TryGetValue(pedidoId, out var status)) return Task.FromResult<PedidoTravado?>(null);
    var numero = Agrupamentos.Values.First(a => a.PedidoId == pedidoId).PedidoNumero;
    return Task.FromResult<PedidoTravado?>(new PedidoTravado(pedidoId, numero, status));
  }

  public Task<PedidoPausa?> ObterPausaAbertaAsync(int pedidoId, CancellationToken ct) =>
      Task.FromResult(Pausas.SingleOrDefault(p => p.PedidoId == pedidoId && p.RetomadoEm is null));

  public Task FecharPausaAsync(int pausaId, int usuarioId, DateTime em, CancellationToken ct)
  {
    var pausa = Pausas.Single(p => p.Id == pausaId);
    if (pausa.RetomadoEm is null)
    {
      pausa.RetomadoEm = em;
      pausa.RetomadoPorUsuarioId = usuarioId;
    }
    return Task.CompletedTask;
  }

  public void Adicionar(PedidoPausa pausa) => _pausasPendentes.Add(pausa);
```

Em `SalvarAlteracoesAsync` do fake, grave as pausas pendentes (Id novo, `Pausas.Add`) e limpe a lista; em `EmTransacaoAsync`, o `finally` limpa `_pausasPendentes` junto das outras. `FakePedidoRepo` (`Cadastros/Fakes.cs`): `public Dictionary<int, PausaAberta> PausasAbertas { get; } = new();` e `ListarPausasAbertasAsync` devolvendo as dos Ids pedidos.

`ExecucaoRepositoryTests`: (a) `ObterPedidoDoNoAsync` devolve `Pausado` `true` com pausa aberta e `false` depois de fechada; (b) `ListarNosEmProducaoAsync` traz a `PausaAberta` (com o nome de quem pausou) nos nós do Pedido pausado e `null` nos de outro — escopado nos Ids da árvore do teste.

- [ ] **Step 5: DTOs e códigos.** `ExecucaoDtos.cs`:

```csharp
/// <summary>A pausa aberta como as telas a mostram: desde quando, por quem, e o motivo.</summary>
public sealed record PausaResumoDto(DateTime Desde, string PorUsuarioNome, string? Motivo)
{
  public static PausaResumoDto? De(PausaAberta? pausa) =>
      pausa is null ? null : new PausaResumoDto(pausa.PausadoEm, pausa.PausadoPorNome, pausa.Motivo);
}

public sealed record PausaDto(
    int Id, int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo,
    DateTime? RetomadoEm, int? RetomadoPorUsuarioId, string? RetomadoPorNome);

/// <summary>`MaxLength` espelha o NVARCHAR(200) de `dbo.PedidoPausa.Motivo`.</summary>
public sealed record NovaPausaDto([MaxLength(200)] string? Motivo);
```

(`using System.ComponentModel.DataAnnotations;` se o arquivo não tiver.) `NoResumoDto` ganha `PausaResumoDto? Pausa` como último parâmetro; em `ConsultaDeExecucaoUseCase.CarregarEmProducaoAsync`, o `new NoResumoDto(...)` passa `PausaResumoDto.De(x.Pausa)` no fim. `Cadastros/Dtos.cs`: `PedidoDto` ganha `PausaResumoDto? Pausa` no fim (com `using Rastreamento.Application.Execucao;`).

`CodigosDaExecucao`:

```csharp
  public const string PedidoPausado = "PedidoPausado";
  public const string PedidoJaPausado = "PedidoJaPausado";
  public const string PedidoNaoPausado = "PedidoNaoPausado";
  public const string MotivoLongoDemais = "MotivoLongoDemais";
```

- [ ] **Step 6: Caso de uso da pausa — testes que falham.** `CenarioDeExecucao` ganha `public PausaDePedidoUseCase Pausa() => new(Execucao);`. `PausaDePedidoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Pausar e retomar (spec da Fase 3D, secao 4.5): um teste por codigo de erro.</summary>
public class PausaDePedidoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  [Fact]
  public async Task Pausar_grava_a_pausa_aberta_com_o_motivo_aparado()
  {
    var c = new CenarioDeExecucao();

    var r = await c.Pausa().Pausar(PedidoId, new NovaPausaDto("  PED-9 urgente "), Pcp, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(("PED-9 urgente", "PCP", (DateTime?)null), (r.Valor!.Motivo, r.Valor.PausadoPorNome, r.Valor.RetomadoEm));
    var gravada = Assert.Single(c.Execucao.Pausas);
    Assert.Equal((PedidoId, Pcp), (gravada.PedidoId, gravada.PausadoPorUsuarioId));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("   ")]
  public async Task Motivo_ausente_ou_em_branco_grava_nulo(string? motivo)
  {
    var c = new CenarioDeExecucao();

    Assert.Null((await c.Pausa().Pausar(PedidoId, new NovaPausaDto(motivo), Pcp, Ct)).Valor!.Motivo);
  }

  [Fact]
  public async Task Motivo_com_mais_de_200_caracteres_da_MotivoLongoDemais_sem_abrir_transacao()
  {
    var c = new CenarioDeExecucao();

    var r = await c.Pausa().Pausar(PedidoId, new NovaPausaDto(new string('a', 201)), Pcp, Ct);

    AfirmarFalha(r, CodigosDaExecucao.MotivoLongoDemais, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Pausar_o_que_ja_esta_pausado_da_PedidoJaPausado()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct), CodigosDaExecucao.PedidoJaPausado, TipoDeErro.Conflito);
    Assert.Single(c.Execucao.Pausas);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Pausar_Pedido_fechado_da_PedidoFechado(string status)
  {
    var c = new CenarioDeExecucao();
    c.Execucao.StatusDoPedido[PedidoId] = status;

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct), CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Pedido_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Pausa().Pausar(77, new NovaPausaDto(null), Pcp, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Pausa().Retomar(77, Pcp, Ct)).TipoDoErro);
  }

  [Fact]
  public async Task Retomar_fecha_o_intervalo_com_quem_e_quando()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    var r = await c.Pausa().Retomar(PedidoId, Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.NotNull(r.Valor!.RetomadoEm);
    Assert.Equal((Operador, "Operador do Corte"), (r.Valor.RetomadoPorUsuarioId, r.Valor.RetomadoPorNome));
    Assert.NotNull(Assert.Single(c.Execucao.Pausas).RetomadoEm);
  }

  [Fact]
  public async Task Retomar_sem_pausa_aberta_da_PedidoNaoPausado()
  {
    var c = new CenarioDeExecucao();

    AfirmarFalha(await c.Pausa().Retomar(PedidoId, Pcp, Ct), CodigosDaExecucao.PedidoNaoPausado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Pausar_de_novo_depois_de_retomar_abre_outro_intervalo()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);
    await c.Pausa().Retomar(PedidoId, Pcp, Ct);

    Assert.True((await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct)).Sucesso);
    Assert.Equal(2, c.Execucao.Pausas.Count);
  }

  [Fact]
  public async Task Conflito_na_transacao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.ConflitoNaProximaTransacao = true;

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct),
        CodigosDaExecucao.ConflitoDeConcorrencia, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
```

Run → não compila.

- [ ] **Step 7: `PausaDePedidoUseCase`.**

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Pausar e retomar um Pedido (spec da Fase 3D, secoes 2.5 e 4.5). A pausa recusa so o Iniciar — os
/// outros registros sao fatos que ja aconteceram no chao — e cada pausa e um intervalo em
/// `dbo.PedidoPausa`, para a Fase 6 poder descontar o tempo pausado.
/// </summary>
public sealed class PausaDePedidoUseCase
{
  public const int TamanhoMaximoDoMotivo = 200;

  private readonly IExecucaoRepository _execucao;

  public PausaDePedidoUseCase(IExecucaoRepository execucao) => _execucao = execucao;

  public async Task<Result<PausaDto>> Pausar(int pedidoId, NovaPausaDto dto, int usuarioId, CancellationToken ct)
  {
    var motivo = dto.Motivo?.Trim();
    if (string.IsNullOrEmpty(motivo)) motivo = null;
    if (motivo is { Length: > TamanhoMaximoDoMotivo })
      return Falhas.Validacao<PausaDto>(CodigosDaExecucao.MotivoLongoDemais,
          $"O motivo tem {motivo.Length} caracteres; o limite é {TamanhoMaximoDoMotivo}.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var pedido = await _execucao.TravarPedidoAsync(pedidoId, ct);
      if (pedido is null) return Falhas.NaoEncontrado<PausaDto>();
      if (pedido.Status is "Concluido" or "Cancelado") return Falhas.PedidoFechado<PausaDto>();
      if (await _execucao.ObterPausaAbertaAsync(pedidoId, ct) is not null)
        return Falhas.Conflito<PausaDto>(CodigosDaExecucao.PedidoJaPausado, $"O Pedido {pedido.Numero} já está pausado.");

      var pausa = new PedidoPausa
      {
        PedidoId = pedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = usuarioId, Motivo = motivo,
      };
      _execucao.Adicionar(pausa);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<PausaDto>.Ok(await ProjetarAsync(pausa, ct));
    }, ct);
  }

  public async Task<Result<PausaDto>> Retomar(int pedidoId, int usuarioId, CancellationToken ct) =>
      await _execucao.ExecutarAsync(async () =>
      {
        var pedido = await _execucao.TravarPedidoAsync(pedidoId, ct);
        if (pedido is null) return Falhas.NaoEncontrado<PausaDto>();
        // Retomar um Pedido que fechou com a pausa aberta e permitido: so fecha o intervalo.
        var pausa = await _execucao.ObterPausaAbertaAsync(pedidoId, ct);
        if (pausa is null)
          return Falhas.Conflito<PausaDto>(CodigosDaExecucao.PedidoNaoPausado, $"O Pedido {pedido.Numero} não está pausado.");

        var agora = DateTime.UtcNow;
        await _execucao.FecharPausaAsync(pausa.Id, usuarioId, agora, ct);
        pausa.RetomadoEm = agora;
        pausa.RetomadoPorUsuarioId = usuarioId;
        return Result<PausaDto>.Ok(await ProjetarAsync(pausa, ct));
      }, ct);

  private async Task<PausaDto> ProjetarAsync(PedidoPausa p, CancellationToken ct)
  {
    var ids = new[] { p.PausadoPorUsuarioId }.Concat(p.RetomadoPorUsuarioId is int r ? [r] : []).Distinct().ToList();
    var nomes = await _execucao.ListarNomesDeUsuariosAsync(ids, ct);
    return new PausaDto(p.Id, p.PedidoId, p.PausadoEm, p.PausadoPorUsuarioId,
        nomes.GetValueOrDefault(p.PausadoPorUsuarioId, string.Empty), p.Motivo, p.RetomadoEm, p.RetomadoPorUsuarioId,
        p.RetomadoPorUsuarioId is int quem ? nomes.GetValueOrDefault(quem, string.Empty) : null);
  }
}
```

Run: `dotnet test tests/Rastreamento.Application.Tests --filter PausaDePedidoUseCaseTests` → PASS.

- [ ] **Step 8: O Iniciar recusa; o resto passa — testes que falham e implementação.** Em `ApontamentoUseCaseTests`:

```csharp
  [Fact]
  public async Task Iniciar_em_Pedido_pausado_da_PedidoPausado_e_nao_grava_nada()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto("urgente"), Pcp, Ct);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoPausado, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
  }

  [Fact]
  public async Task Iniciar_um_pai_em_Pedido_pausado_tambem_e_recusado()
  {
    var c = ComFilhosNaSolda();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    AfirmarFalha(await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.PedidoPausado, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Terminar_em_Pedido_pausado_continua_valendo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 3m);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    Assert.True((await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 3m), Operador, Ct)).Sucesso);
  }
```

E um teste em `EntregaUseCaseTests` (`Entregar_em_Pedido_pausado_continua_valendo`) e um em `EstornoUseCaseTests` (`Estornar_em_Pedido_pausado_continua_valendo`), no mesmo molde. Em `ApontamentoUseCase.Iniciar`, depois da checagem de `PedidoFechado`:

```csharp
      // A pausa recusa so o que COMECA (spec da Fase 3D, secao 2.5): terminar, entregar e estornar
      // registram algo que ja aconteceu no chao, e recusa-los deixaria o livro mentindo.
      if (pedido!.Pausado)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.PedidoPausado,
            "O Pedido deste item está pausado: nada dele começa até alguém retomá-lo.");
```

- [ ] **Step 9: Fila, tarefas e Pedido mostram a pausa — testes que falham e implementação.** Em `ConsultaDeExecucaoUseCaseTests`:

```csharp
  [Fact]
  public async Task A_fila_marca_a_pausa_e_poe_os_pausados_no_fim_do_a_iniciar()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.Agrupamentos[2] = ("AG-02", 2, "PED-02");
    c.Execucao.StatusDoPedido[2] = "Aberto";
    c.No(1, null, 10m, null, Corte);                        // Pedido 1 (sera pausado)
    c.Estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 2, Descricao = "No 2", NivelHierarquico = "Peca", Quantidade = 5m,
    });
    c.Estruturas.Roteiros.Add(new EstruturaRoteiro { Id = 201, EstruturaItemId = 2, SetorId = Corte, Ordem = 1 });
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto("PED-02 urgente"), Pcp, Ct);

    var aIniciar = (await c.Consulta().Fila(Corte, ComoOperador, Ct)).Valor!.AIniciar;

    Assert.Equal(new[] { 2, 1 }, aIniciar.Select(l => l.No.Id).ToArray());
    Assert.Null(aIniciar[0].No.Pausa);
    Assert.Equal(("PED-02 urgente", "PCP"), (aIniciar[1].No.Pausa!.Motivo, aIniciar[1].No.Pausa!.PorUsuarioNome));
  }
```

Em `FilaAsync`, antes do `return`: `aIniciar = aIniciar.OrderBy(l => l.No.Pausa is null ? 0 : 1).ToList();` com o comentário `// O que e de Pedido pausado vai para o fim, e a tela o agrupa em "Pausados" (spec da Fase 3D, secao 2.5). OrderBy e estavel: dentro de cada grupo, a ordem de antes.` (troque `var aIniciar = new List<…>` por uma lista que aceite a reatribuição, ou ordene na construção do DTO).

Em `CadastroDePedidoUseCaseTests`, dois testes: `Listar` e `Obter` trazem a pausa aberta (`FakePedidoRepo.PausasAbertas[id] = new PausaAberta(...)`) e `null` sem ela. Em `CadastroDePedidoUseCase`:

```csharp
  public async Task<IReadOnlyList<PedidoDto>> Listar(CancellationToken ct)
  {
    var pedidos = await _repositorio.ListarAsync(ct);
    var pausas = await _repositorio.ListarPausasAbertasAsync(pedidos.Select(p => p.Id).ToList(), ct);
    return pedidos.Select(p => Projetar(p, pausas.GetValueOrDefault(p.Id))).ToList();
  }
```

`Obter` e `Editar` leem `ListarPausasAbertasAsync([id], ct)`; `Cadastrar` projeta com `null` (Pedido novo nunca está pausado). `Projetar`:

```csharp
  private static PedidoDto Projetar(Pedido p, PausaAberta? pausa) =>
      new(p.Id, p.Numero, p.Cliente, p.Tipo, p.Status, p.DataAbertura, p.CriadoPorUsuarioId, PausaResumoDto.De(pausa));
```

- [ ] **Step 10: Controller, DI, perfis.** `PausaDePedidoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Pausar e retomar um Pedido (spec da Fase 3D, secao 2.5). Controller proprio, e nao acao do
/// `PedidosController`: a guarda do front compara todo `[Authorize(Roles)]` de um arquivo com UMA
/// entrada de `permissoes.ts`, e os perfis daqui (com a Gestao) nao sao os do cadastro de Pedido.
/// </summary>
[ApiController]
[Authorize]
public class PausaDePedidoController : ExecucaoControllerBase
{
  /// <summary>Planejamento decide prioridade: PCP e Gestao — a primeira escrita da Gestao no sistema.</summary>
  private const string PerfisDeEscrita = "PCP,Gestao,Administrador";

  private readonly PausaDePedidoUseCase _pausa;

  public PausaDePedidoController(PausaDePedidoUseCase pausa) => _pausa = pausa;

  [HttpPost("pedidos/{id:int}/pausas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Pausar(int id, [FromBody] NovaPausaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _pausa.Pausar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("pedidos/{id:int}/retomada")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Retomar(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _pausa.Retomar(id, usuarioId, ct));
  }
}
```

`Program.cs`: `builder.Services.AddScoped<PausaDePedidoUseCase>();` junto dos casos de uso da execução. `PerfisDeEscritaDeclaradosTests.TabelaAprovada`, no bloco da execução:

```csharp
    // Pausa de Pedido (Fase 3D): PCP e Gestao — prioridade e decisao de planejamento.
    ["POST pedidos/{id:int}/pausas"] = ["PCP", "Gestao", "Administrador"],
    ["POST pedidos/{id:int}/retomada"] = ["PCP", "Gestao", "Administrador"],
```

`ExecucaoEndpointsTests.Perfil_sem_a_acao_recebe_403`: acrescente `[InlineData("Operador", "POST", "/api/pedidos/999999/pausas")]` e `[InlineData("Movimentador", "POST", "/api/pedidos/999999/retomada")]`. Se `RegistroDeDependenciasTests` enumerar os casos de uso, acrescente o novo.

Front, **nesta task** (a guarda de espelhamento lê os controllers e ficaria vermelha): `permissoes.ts` — `Recurso` ganha `| 'pausa'` e `ESCRITA` ganha

```ts
  // Pausar e retomar um Pedido (Fase 3D): PCP e Gestão — a primeira escrita da Gestão. Não é
  // `pedidos`: os perfis divergem (o cadastro de Pedido é só do PCP).
  pausa: ['PCP', 'Gestao', 'Administrador'],
```

`permissoesEspelhamOBackend.test.ts` — `CONTROLLERS_POR_RECURSO` ganha `pausa: ['PausaDePedidoController.cs'],` e o texto do isento `ExecucaoControllerBase.cs` troca "para os quatro" por "para os cinco". `permissoes.test.ts` — o primeiro teste inclui `'pausa'` na lista; acrescente `it('Fase 3D: pausar é do PCP e da Gestão, e de mais ninguém do chão', …)` afirmando `true` para PCP e Gestao e `false` para Operador, Movimentador, Almoxarifado e Qualidade. Rode `cd web && npm test -- --run src/auth`.

- [ ] **Step 11: API ponta a ponta.** `PausaDePedidoEndpointsTests.cs` (novo, `IClassFixture<WebApplicationFactory<Program>>`, `await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory)`):

```csharp
  [Fact]
  public async Task Gestao_pausa_o_operador_nao_inicia_mas_termina_e_depois_de_retomar_inicia()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 5m }));

    var pausa = await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "outro pedido urgente" });
    var iniciarPausado = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m });
    var terminarPausado = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 5m });
    var pedido = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}"));
    var retomada = await c.Como(c.Gestao).PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null);
    var iniciarDepois = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m });

    Assert.Equal(HttpStatusCode.Created, pausa.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, iniciarPausado.StatusCode);
    Assert.Equal("PedidoPausado", (await CorpoAsync(iniciarPausado)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Created, terminarPausado.StatusCode);
    Assert.Equal("outro pedido urgente", pedido.GetProperty("pausa").GetProperty("motivo").GetString());
    Assert.Equal(HttpStatusCode.OK, retomada.StatusCode);
    Assert.Equal(HttpStatusCode.Created, iniciarDepois.StatusCode);
  }

  [Fact]
  public async Task Pausar_duas_vezes_e_retomar_sem_pausa_dao_409_com_o_codigo()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);

    var retomarSemPausa = await pcp.PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null);
    await Garantir(await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = (string?)null }));
    var pausarDeNovo = await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = (string?)null });

    Assert.Equal("PedidoNaoPausado", (await CorpoAsync(retomarSemPausa)).GetProperty("erro").GetString());
    Assert.Equal("PedidoJaPausado", (await CorpoAsync(pausarDeNovo)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task A_fila_marca_o_no_de_Pedido_pausado()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "urgente" }));

    var fila = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Corte}/fila"));
    var linha = fila.GetProperty("aIniciar").EnumerateArray().Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);

    Assert.Equal("urgente", linha.GetProperty("no").GetProperty("pausa").GetProperty("motivo").GetString());
  }
```

- [ ] **Step 12: Documentação.**
  - `01`: **regra 31, nova**, depois da 30, com uma nota de data como a das regras 28 a 30: "**Um Pedido pode ser pausado.** PCP ou Gestão pausam um Pedido quando outro mais urgente precisa passar na frente, com um motivo opcional. A pausa recusa **só o início** de qualquer nó do Pedido — inclusive o de um pai, que consome os filhos (regra 24) —; terminar, entregar e estornar continuam, porque registram o que já aconteceu no chão. Cada pausa é um intervalo guardado (quem pausou e retomou, e quando), para o tempo por Pedido poder descontá-la (Fase 6). A pausa não muda o status do Pedido." Cite a spec da Fase 3D, §2.5.
  - `05`: as duas rotas, perfis, corpo e resposta; `GET /pedidos` e `GET /pedidos/{id}` ganham `pausa`; `NoResumo` da fila e das tarefas ganha `pausa`; contrato de erro: `PedidoPausado`, `PedidoJaPausado`, `PedidoNaoPausado`, `MotivoLongoDemais`.
  - `04`: no fluxo "1. Cadastro de Pedido" (ou onde o PCP acompanha o Pedido), um passo: PCP ou Gestão pausam e retomam; no fluxo "2. Apontamento em Setor", o operador vê o que é de Pedido pausado no fim de "A iniciar aqui", sem Iniciar.

- [ ] **Step 13: Verificação e commit.** Build 0 warnings; `dotnet test Rastreamento.slnx -m:1` verde; `cd web && npm test -- --run && npm run build` verde (só `src/auth` mudou, mas a guarda de espelhamento lê o disco).

```bash
git add specs db src tests web/src/auth
git commit -m "feat(pedido): pausar e retomar Pedido — a pausa recusa só o Iniciar"
```

---

### Task 8: Front — pausar, retomar e ver o que está pausado

**Files:**
- Modify: `web/src/api/cadastros.ts` (`PausaResumoDto`, `PedidoDto.pausa`)
- Modify: `web/src/api/execucao.ts` (`NoResumoDto.pausa`, `PausaDto`, `pausarPedido`, `retomarPedido`)
- Modify: `web/src/api/erros.ts` (quatro códigos)
- Create: `web/src/pedidos/ControleDePausa.tsx`, `web/src/pedidos/ControleDePausa.test.tsx`
- Modify: `web/src/pages/PedidoDetalhePage.tsx`, `web/src/pedidos/LinhaDePedido.tsx`
- Modify: `web/src/pages/FilaDoSetorPage.tsx` (grupo "Pausados", pílula), `web/src/pages/TarefasPage.tsx` (pílula)
- Test: `web/src/testes/execucao.ts` (`no()` com `pausa: null`), `web/src/pages/FilaDoSetorPage.test.tsx`, `web/src/pages/TarefasPage.test.tsx`, `web/src/pages/PedidoDetalhePage.test.tsx`, `web/src/pedidos/LinhaDePedido.test.tsx`, `web/src/api/execucao.test.ts`
- Test (fixture): todo `PedidoDto` literal ganha `pausa: null` — `grep -rn "criadoPorUsuarioId" web/src --include=*.test.ts --include=*.test.tsx` achou **18** em 6 arquivos (2026-09-28; remeça).

**Interfaces:**
- Consumes: contrato da Task 7; `usePodeEscrever('pausa')`.
- Produces: TS `PausaResumoDto { desde: string; porUsuarioNome: string; motivo: string | null }`; `PedidoDto.pausa`, `NoResumoDto.pausa: PausaResumoDto | null`; `pausarPedido(pedidoId, motivo)`, `retomarPedido(pedidoId)`; `ControleDePausa({ pedido, aoMudar })`.

- [ ] **Step 1: Tipos, API e códigos.** `api/cadastros.ts`:

```ts
/** A pausa aberta de um Pedido (spec da Fase 3D, §2.5). `desde`: ISO 8601 com offset -03:00. */
export interface PausaResumoDto {
  desde: string
  porUsuarioNome: string
  motivo: string | null
}
```

e `PedidoDto` ganha `pausa: PausaResumoDto | null`. `api/execucao.ts`: `NoResumoDto` ganha `pausa: PausaResumoDto | null` (importe o tipo de `./cadastros`), e:

```ts
export interface PausaDto {
  id: number
  pedidoId: number
  pausadoEm: string
  pausadoPorUsuarioId: number
  pausadoPorNome: string
  motivo: string | null
  retomadoEm: string | null
  retomadoPorUsuarioId: number | null
  retomadoPorNome: string | null
}

/**
 * Pausar e retomar passam pelo `enviar` da execução (mesmo `{ erro, mensagem }`); o aviso de que o
 * livro mudou, que ele dá, só faz o contador de Tarefas recontar — inofensivo aqui.
 */
export function pausarPedido(pedidoId: number, motivo: string | null): Promise<PausaDto> {
  return enviar(`/pedidos/${pedidoId}/pausas`, 'POST', { motivo }, 'pausar o pedido')
}

export function retomarPedido(pedidoId: number): Promise<PausaDto> {
  return enviar(`/pedidos/${pedidoId}/retomada`, 'POST', undefined, 'retomar o pedido')
}
```

`api/execucao.test.ts`: prova de URL, método e corpo das duas. `testes/execucao.ts`, `no()`: `pausa: null,` depois de `paiDescricao`. `api/erros.ts`: a união e a tradução ganham

```ts
  PedidoPausado: 'O pedido deste item está pausado: nada dele começa até alguém retomá-lo.',
  PedidoJaPausado: 'Este pedido já está pausado.',
  PedidoNaoPausado: 'Este pedido não está pausado.',
  MotivoLongoDemais: 'O motivo passa de 200 caracteres.',
```

Acrescente `pausa: null` aos `PedidoDto` literais que o `grep` da seção Files lista.

- [ ] **Step 2: `ControleDePausa` — testes que falham.** `web/src/pedidos/ControleDePausa.test.tsx` (mocke `useAuth` como os testes de tela fazem, com o perfil numa variável; use `fetchPorRota`/`respostaJson`):

  - com o Pedido pausado, todo perfil vê o aviso "Pausado desde 28/09/2026 10:14 por PCP" e o motivo;
  - PCP, Gestao e Administrador veem "Pausar" (Pedido não pausado, não encerrado) ou "Retomar" (pausado); Operador não vê nenhum dos dois;
  - "Pausar" abre o campo "Motivo (opcional)" e "Confirmar pausa"; confirmar faz `POST /api/pedidos/1/pausas` com `{ motivo: 'urgente' }` (ou `{ motivo: null }` com o campo vazio) e chama `aoMudar`; "Cancelar" fecha sem requisição;
  - "Retomar" faz `POST /api/pedidos/1/retomada` e chama `aoMudar`;
  - Pedido `Concluido` ou `Cancelado` não oferece "Pausar";
  - recusa do servidor (409 `PedidoJaPausado` com `mensagem`) aparece num `BannerDeErro` e ainda chama `aoMudar` (a tela ficou velha);
  - toque duplo em "Confirmar pausa" envia uma vez só.

- [ ] **Step 3: `ControleDePausa`.**

```tsx
import { useRef, useState, type FormEvent } from 'react'
import { formatarDataHora, type PedidoDto } from '../api/cadastros'
import { pausarPedido, retomarPedido, ehConflito } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { ENCERRADOS } from './statusDoPedido'

interface Props {
  pedido: PedidoDto
  /** A pausa mudou (ou a tela ficou velha): o chamador recarrega o Pedido. */
  aoMudar: () => Promise<void>
}

/**
 * Pausar e retomar um Pedido (spec da Fase 3D, §2.5), com o aviso da pausa aberta. Todo perfil vê o
 * aviso; os botões são do PCP e da Gestão (`usePodeEscrever('pausa')`), e o 403 do backend continua
 * sendo a fronteira real. A pausa recusa só o Iniciar — o aviso diz isso, para ninguém achar que o
 * Pedido inteiro travou.
 */
export function ControleDePausa({ pedido, aoMudar }: Props) {
  const podePausar = usePodeEscrever('pausa')
  const [pedindoMotivo, setPedindoMotivo] = useState(false)
  const [motivo, setMotivo] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

  const encerrado = (ENCERRADOS as readonly string[]).includes(pedido.status)

  async function executar(acao: () => Promise<unknown>) {
    if (enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await acao()
      setPedindoMotivo(false)
      setMotivo('')
      await aoMudar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar a pausa do pedido.'))
      if (ehConflito(e)) await aoMudar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  function confirmarPausa(e: FormEvent) {
    e.preventDefault()
    const texto = motivo.trim()
    void executar(() => pausarPedido(pedido.id, texto === '' ? null : texto))
  }

  return (
    <div className="flex flex-col gap-3">
      {pedido.pausa && (
        <p className="text-sm text-tinta">
          {`Pausado desde ${formatarDataHora(pedido.pausa.desde)} por ${pedido.pausa.porUsuarioNome}`}
          {pedido.pausa.motivo && ` — ${pedido.pausa.motivo}`}
          {'. Nada dele começa até ser retomado; o que já está em trabalho continua.'}
        </p>
      )}
      <BannerDeErro mensagem={erro} />
      {podePausar && pedido.pausa && (
        <Botao variante="secundario" onClick={() => executar(() => retomarPedido(pedido.id))}
          carregando={enviando} rotuloCarregando="Retomando…" className="self-start">
          Retomar
        </Botao>
      )}
      {podePausar && !pedido.pausa && !encerrado && !pedindoMotivo && (
        <Botao variante="secundario" onClick={() => setPedindoMotivo(true)} className="self-start">Pausar</Botao>
      )}
      {podePausar && !pedido.pausa && !encerrado && pedindoMotivo && (
        <form onSubmit={confirmarPausa} className="flex flex-col gap-3 border-t border-borda pt-3">
          <Campo rotulo="Motivo (opcional)">
            {(id) => (
              <input id={id} value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={200}
                className={CLASSES_DE_CONTROLE} />
            )}
          </Campo>
          <div className="flex flex-wrap gap-2">
            <Botao type="submit" carregando={enviando} rotuloCarregando="Pausando…">Confirmar pausa</Botao>
            <Botao variante="secundario" onClick={() => { setPedindoMotivo(false); setMotivo('') }}>Cancelar</Botao>
          </div>
        </form>
      )}
    </div>
  )
}
```

Confira os nomes de `ENCERRADOS` (é `as const` em `statusDoPedido.ts`) e de `Botao` (`carregando`, `rotuloCarregando` — a `SetoresPage` os usa). Run o teste do Step 2 → verde.

- [ ] **Step 4: Detalhe e lista de Pedidos.** `PedidoDetalhePage.tsx`, no cartão do cabeçalho: ao lado das pílulas de tipo e status, `{pedido.pausa && <Pilula>Pausado</Pilula>}`; embaixo do parágrafo das pílulas, `<ControleDePausa pedido={pedido} aoMudar={() => carregar(pedidoId)} />`. `LinhaDePedido.tsx`: depois da pílula de status, `{pedido.pausa && <Pilula>Pausado</Pilula>}`. Testes: o detalhe mostra a pílula e o aviso de um Pedido pausado; a linha mostra "Pausado" só quando `pausa` não é nula. A pílula é `neutro` (o padrão) — **não** passe `tom`.

- [ ] **Step 5: Fila e Tarefas.** Em `FilaDoSetorPage.tsx`:
  - `CabecalhoDoNo` mostra a pausa:

```tsx
function CabecalhoDoNo({ no }: { no: NoResumoDto }) {
  return (
    <>
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        {rotuloDoNo(no)}
        {no.pausa && <Pilula>Pausado</Pilula>}
      </span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(no)}</span>
      {no.pausa?.motivo && <span className="text-xs text-tinta-fraca">{`Pausa: ${no.pausa.motivo}`}</span>}
    </>
  )
}
```

  - "A iniciar aqui" separa os liberados dos pausados (o servidor já manda os pausados no fim): os liberados como hoje; se houver pausados, depois deles, dentro da mesma seção, um `<li>` de título e as linhas **sem** `acao`:

```tsx
      {fila.aIniciar.length > 0 && (
        <Secao titulo="A iniciar aqui">
          {fila.aIniciar.filter((l) => l.no.pausa === null).map((l) => ( /* o ItemComAcao de hoje, igual */ ))}
          {fila.aIniciar.some((l) => l.no.pausa !== null) && (
            <li className="pt-2 text-sm font-medium text-tinta-fraca" aria-hidden="true">Pausados</li>
          )}
          {fila.aIniciar.filter((l) => l.no.pausa !== null).map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} a iniciar · passo ${l.ordem} · Pedido pausado`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
```

  O título "Pausados" é `aria-hidden` porque cada linha já diz "Pedido pausado" e traz a pílula — um `<li>` de título seria lido como item da lista. (Escreva o `ItemComAcao` dos liberados por extenso no lugar do comentário.)
  - O card de montagem não oferece Iniciar a pai de Pedido pausado: a condição do `acao` ganha `&& g.pai.pausa === null`.
  - `chavesDaFila`: `for (const l of fila.aIniciar) if (l.no.pausa === null) chaves.add(...)` e, no grupo, `g.iniciaAqui && g.daParaMontar > 0 && g.pai.pausa === null`.
  - Importe `Pilula`.

  `TarefasPage.tsx`, no item: depois do `rotuloDoNo`, `{item.no.pausa && <Pilula>Pausado</Pilula>}` (a entrega continua permitida).

  Testes da fila: (a) pausado aparece depois de "Pausados", com a pílula e o motivo, sem "Iniciar"; (b) liberado continua com "Iniciar"; (c) pai de Pedido pausado no card de montagem não tem "Iniciar"; (d) linha de "Em trabalho" de Pedido pausado continua com "Terminar" e a pílula. Tarefas: item de Pedido pausado mostra a pílula e continua marcável.

- [ ] **Step 6: Verificação e commit.** `cd web && npm test -- --run && npm run build` → verde, build limpo.

```bash
git add web/src
git commit -m "feat(web): pausar e retomar Pedido, e o pausado marcado na fila, nas tarefas e na lista"
```

---

### Task 9: Documentos que fecham a fase

Documentação pura, **com review** (spec vira texto do TCC: afirmação falsa aqui é texto que o usuário defende). O revisor recebe a instrução de **medir cada afirmação verificável** contra o código da branch, não de lê-la.

**Files:**
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md` (nota no topo)
- Modify: `docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md` (§5.1: desvio D1; §4.3: desvio D2 resolvido)
- Modify: `specs/04-fluxos-de-usuario.md` (fluxo 2, passo 5)
- Modify: `CLAUDE.md` (seção "Invariantes de negócio", bullet do Kit)
- Modify (se o `grep` achar): `README.md`

- [ ] **Step 1: Roadmap.** Em `06`, uma seção **"Fase 3D — Ajustes pós-verificação da Fase 3"** logo depois da "Fase 3", com a nota "executada antes da 3B, por decisão de 2026-09-28" e os bullets: Iniciar como verbo único (o pai consome os filhos); atividade do Setor nos botões; destino do filho pronto calculado; estorno rápido na fila; pausa de Pedido (recusa só o Iniciar). Critério de pronto: o da spec da Fase 3D, §9 (verificação manual). Na **Fase 3B**, o bullet "Trava de montagem por nó…" passa a dizer que a trava ficou **estrutural desde a 3D** (o pai só entra em produção consumindo os filhos) e que a 3B fica com `Setor.UtilizaKit`, o conjunto completo e a tarefa Kit pronto; o critério de pronto da 3B perde "saída acima do montado" ou o reescreve como algo que já vale. **Releia a 3B inteira depois** — a contradição mora entre bullets vizinhos.

- [ ] **Step 2: Nota na spec da Fase 3.** No topo, depois do título, um bloco:

```markdown
> **Emendada em 2026-09-28 pela spec da Fase 3D**
> (`docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`): montar deixou de ser
> ação — iniciar um nó com filhos consome os filhos —, o destino do filho pronto passou a ser o
> primeiro passo do pai, e entraram o estorno rápido na fila e a pausa de Pedido. As seções 2.4, 4.1,
> 4.3, 4.4, 4.5, 4.8, 6.1, 6.2, 7.3, 7.6 e 8.2 abaixo são o registro da decisão da época; onde divergem
> da 3D, vale a 3D.
```

- [ ] **Step 3: A spec da 3D registra os desvios do plano.** §5.1: a resposta do Iniciar é o movimento de Início, com `montagemId` (desvio D1, com o motivo). §4.3: "A decidir no plano" vira a decisão tomada (desvio D2). Sem reescrever o resto.

- [ ] **Step 4: Fluxos, `CLAUDE.md`, README.** `04`, fluxo 2, passo 5: o operador do primeiro passo do pai vê "dá para iniciar N" e **inicia** o pai, consumindo os filhos; não há mais "montar". `CLAUDE.md`, "Invariantes de negócio", no bullet do Kit: "Montar é registro de **todo** nó com filhos" passa a "O início de **todo** nó com filhos consome os filhos presentes (a montagem); desde a Fase 3D a saída limitada ao total montado vale para todo nó por construção; o conjunto completo continua só do Kit". `grep -n "montar\|Montar" README.md` — conserte o que descrever a ação antiga.

- [ ] **Step 5: Medir o que a documentação afirma.** Para cada número ou nome que a edição escreveu, rode o comando que o prova e cole no relatório (ex.: `grep -rn "POST.*montagens" src tests web/src` → nada; os códigos citados existem em `CodigosDaExecucao`). Sem medição, a frase sai.

- [ ] **Step 6: Commit.**

```bash
git add specs docs CLAUDE.md README.md
git commit -m "docs: a Fase 3D no roadmap, a 3B encolhida e as emendas à spec da Fase 3"
```

---

### Task 10: Verificação manual no celular

**Produto é relatório, não código — dispensa de review.** A justificativa vai **escrita antes**, no ledger e no relatório da task, como manda o `CLAUDE.md` ("Pular a review exige justificativa ESCRITA"). Os achados viram tasks novas (com o gate inteiro), não correção dentro desta.

- [ ] **Step 1: Bancada.** Peça `request_keep_awake` antes (a suspensão derruba os dev servers). Regenere o banco (descartável): `DROP DATABASE`, `specs/02-modelo-de-dados.sql`, `db/seed.sql`, `db/seed-demo.sql` (`-f 65001`), e os scripts do workspace do plano 3 da Fase 3 no ledger (`prepara-banco-verificacao-manual.sh`, `catalogo-abc.sql`). Suba a API e o front; no celular, IP do Wi-Fi + porta 5173 (o refresh não funciona por HTTP fora de localhost — sessão cai em F5/15 min, esperado).

- [ ] **Step 2: Roteiro.** Com o usuário, no celular, registrando cada passo como **passou / não passou / observação**:
  1. Setores: dar a atividade "montagem" à Solda (ou ao Setor de montagem do catálogo A-B-C); a fila dela mostra "Iniciar montagem"/"Terminar montagem"; um Setor sem atividade continua "Iniciar"/"Terminar".
  2. Pedido A ← B, C: iniciar e terminar B e C; nas Tarefas o destino diz "Montagem de A em Solda", sem escolher Setor; entregar.
  3. Na Solda, A **não** está em "A iniciar aqui"; o card de montagem oferece "Iniciar montagem"; iniciar parte; A aparece em "Em trabalho"; B e C baixam.
  4. Estorno rápido: em "Em trabalho" de A, "Estornar" vai direto à confirmação (uma montagem) e devolve os filhos; iniciar de novo; terminar B duas vezes e ver a lista curta em "Aguardando coleta"; estornar um.
  5. Pausa: o PCP pausa o Pedido com motivo; a lista de Pedidos e o detalhe mostram "Pausado"; o operador vê o nó no fim de "A iniciar aqui", sem Iniciar; "Terminar" continua; o Movimentador continua entregando; a Gestão retoma; iniciar volta a funcionar.
  6. Um usuário `operador` não vê "Pausar"; um usuário da Gestão vê.

- [ ] **Step 3: Registro.** O relatório vai para o workspace do plano no ledger, com a lista de achados; a linha da fase no cartão do ledger é atualizada. Achado de código abre task nova; achado de desenho volta ao usuário.
