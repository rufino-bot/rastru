# Fase 4 — Separação de materiais — Plano 1 de 2 (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao backend o "Fica em" do Material, o planejado editável de cada nó, o livro de separação
(separar e estornar), a lista do Separador, o resumo de material na Fila e o perfil Separador.

**Architecture:** `dbo.MaterialSeparacao` é reescrita como livro só de inclusão e `Material` ganha
`SetorDeEstoqueId`, pelo caminho Database First. Uma pasta nova `Application/Separacao/` reúne a conta pura
(`CalculoDaSeparacao`), três casos de uso (`MateriaisDoNoUseCase`, `SeparacaoUseCase`,
`ConsultaDeSeparacaoUseCase`) e um repositório próprio (`ISeparacaoRepository`). Três controllers novos, um por
conjunto de perfis. A Fila (`ConsultaDeExecucaoUseCase`) lê o mesmo repositório para o resumo `{ completos,
total }`.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-16-fase-4-separacao-de-materiais-design.md`, aprovada em 2026-10-16
(decisões D1–D17). Branch: `claude/nice-babbage-wsvl9v`, spec em `b629bd0`, sobre `17890b2`. O plano 2 (front,
`docs/superpowers/plans/2026-10-16-fase-4-front.md`) consome a seção "Contrato JSON novo" daqui.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass
  de Critical/Important → re-review). Nenhuma task deste plano tem dispensa prevista.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com
  `scripts/review-package BASE HEAD`, nunca com `HEAD~1`. Ledger do plano em `.superpowers/sdd/2026-10-16-fase-4/`;
  artefatos com nome escopado por este plano (`backend-task-N-…`), porque o plano 2 numera as tasks de novo.
- **A review de branch inteira só é despachada depois que o usuário fizer a conferência no navegador**, que é a
  última task do plano 2 (regra do usuário de 2026-10-07, no `CLAUDE.md`).
- **Schema pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-fase-4.sql`
  idempotente (formato de `db/alter-fase-3b.sql`, aplicado com `-b -f 65001`), depois o mapeamento EF. Nada de
  `Add-Migration` nem `EnsureCreated`.
- **O livro de separação é só de inclusão** (regra 34, D10, D13): nenhum `UPDATE` nem `DELETE` em
  `dbo.MaterialSeparacao` fora da limpeza de teste. Separado = Σ `Separacao` − Σ `Estorno`, sempre calculado.
- **Pendência só do almoxarifado** (D6): material com `SetorDeEstoqueId` preenchido nunca tem pendência e nunca
  entra na lista do Separador nem no resumo da Fila, mas **pode** ser separado se estiver no planejado (D16).
- **Só material do planejado** (D9): fora dele, `409 MaterialForaDoPlanejado`; acima dele, aceita.
- **Perfis, exatos** (spec 5.3): separar `Separador,Administrador`; estornar separação `Separador,PCP,Administrador`
  (o Separador só a própria, `403 Proibido` do caso de uso); materiais do nó `PCP,Administrador`; "Fica em"
  `Administrador`. Leituras: qualquer autenticado.
- **Códigos novos** (`CodigosDaSeparacao`): `MaterialForaDoPlanejado` (409), `JaEstornada` (409),
  `EstornoDeEstorno` (409), `SeparacaoVazia` (400), `MaterialRepetido` (400), `MaterialInvalido` (400),
  `SetorDeEstoqueInvalido` (400); `NoComSeparacao` (409) em `ExcluirNo`. Reusados de `CodigosDaExecucao`:
  `QuantidadeInvalida` (400) e `Proibido` (403).
- O perfil se chama **`Separador`** em todo lugar novo; o nome `Almoxarifado` some do código e dos testes.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês.
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa. Comentário de código **não cita** ledger,
  brief nem "Task N": o repositório é público. Decisões citadas como "D*n* da spec da Fase 4" ou "decisão P*n* do
  plano de backend da Fase 4".
- `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`.
  Teste de banco **escopa a asserção** nas linhas que ele inseriu; teste novo que escreve em tabela já escrita por
  outra classe segue a regra de `[Collection]` do `CLAUDE.md`.
- Mensagens de erro dos `.cs` novos **com** acento, como os da `Execucao`; comentários sem acento, como o vizinho.
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em chamada
  separada do `task-brief`/`review-package`.

## Review Focus

Entradas que a spec implica e que nenhum teste das tasks exercitaria sem esta lista (cada uma tem teste na task
dona, marcado "Review Focus"):

1. **Nó apagado depois de ter separação** — `DELETE /estrutura/{id}` num Pedido `Aberto` cuja subárvore tem linha
   em `MaterialSeparacao` daria 500 pela FK. Esperado: `409 NoComSeparacao`, nada apagado (Task 9).
2. **Estorno de uma separação de material que saiu do plano** — o PCP tirou o material do planejado; o estorno da
   linha antiga tem de funcionar, e o material continua no `GET` com `noPlanejado: false` (Task 6).
3. **Dois estornos simultâneos da mesma linha** — um `201` e um `409 JaEstornada`, nunca `500` (Task 6).
4. **Banco com o perfil antigo e o seed reaplicado antes do `alter`** — `Almoxarifado` e `Separador` coexistem; o
   `alter` move os usuários e apaga o antigo, sem violar `UQ_Perfil_Nome` (Task 1).
5. **Separação em nó de Pedido pausado** — a pausa recusa só o início (regra 31); separar continua (Task 6).

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem         # na nuvem; na máquina local: docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
```

Banco de máquina local que já existe: aplicar o `db/alter-fase-4.sql` (Task 1) com o bloco "Fase 4" que a Task 1
acrescenta ao `CLAUDE.md`, e depois reaplicar o `db/seed.sql`. Na nuvem, `backend-na-nuvem` cria o banco do `.sql`
e do seed, sem o `alter`.

**Baseline:** o ledger registra backend **1331** (Api 359 · Application 751 · Infrastructure 221) em `17890b2`. **O
pré-flight remede na `b629bd0` antes da Task 1** e escreve o número no ledger do plano. Cada implementer reporta o
**delta** da própria task, nunca um total previsto por este plano.

## Decisões deste plano (onde a spec deixou a escolha ou o código pediu um ajuste)

- **P1 — Repositório próprio, sem a transação da execução.** `ISeparacaoRepository` não usa
  `IExecucaoRepository.EmTransacaoAsync`: separar grava todas as linhas num `SaveChanges` só (já atômico), o `PUT`
  dos materiais apaga e insere num `SaveChanges` só, e a corrida de estorno é fechada pelo índice único
  `UX_MaterialSeparacao_EstornoDe`, traduzido pelo repositório (`GravarEstornoAsync` devolve `false`). Nada aqui
  precisa de SERIALIZABLE.
- **P2 — Nó com qualquer linha de separação não se apaga** (`409 NoComSeparacao`, inclusive linha já estornada).
  A spec não tratou da exclusão; com separação antecipada (D11) ela pode existir em Pedido `Aberto`, e apagar o nó
  exigiria apagar linhas de um livro só de inclusão. É o mesmo efeito que um movimento tem hoje.
- **P3 — `ContextoDoNo` ganha `PedidoDataEntrega` e `PedidoStatus`** (parâmetros no fim, com valor padrão), para
  a lista do Separador decidir o atraso com `PrazoDeEntrega.EstaAtrasado` sem uma consulta por Pedido.
- **P4 — O resumo da Fila vai na LINHA, não no `NoResumoDto`:** `LinhaDaFilaDto.Material` (preenchido só em
  `AIniciar`) e `MontagemPendenteDto.Material` (preenchido só quando `IniciaAqui`). O `NoResumoDto` é o mesmo nó em
  todo Setor; "inicia aqui" depende do Setor da Fila.
- **P5 — `acimaDoPlanejado` vai em todo item de material do nó**, não só na resposta do `POST`: o painel do nó
  também o mostra, e é um campo só.
- **P6 — Três controllers**: `MateriaisDoNoController` (`materiaisDoNo`), `SeparacaoController` (`separacao`) e
  `EstornoDeSeparacaoController` (`estornoDeSeparacao`). A guarda de espelhamento do front compara todo
  `[Authorize(Roles)]` de um arquivo com UMA entrada de `permissoes.ts`.
- **P7 — "Fica em" valida o Setor no cadastro de Material** pelo `ISetorRepository` (inexistente ou inativo →
  `400 SetorDeEstoqueInvalido`), e o `MaterialDto` traz `FicaEm` com o nome do Setor, inativo inclusive.
- **P8 — Ordem das validações de `POST /estrutura/{id}/separacoes`:** nó inexistente (404) → lista vazia (400
  `SeparacaoVazia`) → quantidade (400 `QuantidadeInvalida`) → repetido (400 `MaterialRepetido`) → fora do planejado
  (409 `MaterialForaDoPlanejado`). Do estorno: linha inexistente (404) → não é autor nem PCP/Administrador (403
  `Proibido`) → é estorno (409 `EstornoDeEstorno`) → já estornada (409 `JaEstornada`).

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-fase-4.sql`, `db/seed.sql` | 1 | schema, migração, perfil, usuário `separador` |
| `Domain/Entities/{Material,MaterialSeparacao}.cs`, `Infrastructure/Persistence/Configurations/{Material,MaterialSeparacao}Configuration.cs`, `RastreamentoDbContext.cs` | 1 | mapeamento |
| `Application/Cadastros/{Dtos,CadastroDeMaterialUseCase}.cs` | 2 | "Fica em" |
| `Application/Separacao/{SeparacaoDtos,CalculoDaSeparacao,CodigosDaSeparacao}.cs` | 3 | a regra 34, pura |
| `Domain/Abstractions/ISeparacaoRepository.cs`, `Infrastructure/Persistence/SeparacaoRepository.cs`, `ErrosDoSqlServer.cs` | 4 | acesso a dados |
| `Application/Separacao/MateriaisDoNoUseCase.cs`, `Api/Controllers/MateriaisDoNoController.cs` | 5 | planejado do nó |
| `Application/Separacao/{SeparacaoUseCase,ConsultaDeSeparacaoUseCase}.cs`, `Api/Controllers/{SeparacaoController,EstornoDeSeparacaoController}.cs` | 6, 7 | separar, estornar, histórico, pendências |
| `Domain/Abstractions/IExecucaoRepository.cs` (`ContextoDoNo`), `Infrastructure/Persistence/ExecucaoRepository.cs` | 7 | prazo e status no contexto |
| `Application/Execucao/{ConsultaDeExecucaoUseCase,ExecucaoDtos}.cs` | 8 | resumo da Fila |
| `Application/Estrutura/MontagemDeEstruturaUseCase.cs`, `Domain/Abstractions/IEstruturaRepository.cs`, `Infrastructure/Persistence/EstruturaRepository.cs` | 9 | `NoComSeparacao` |
| `Api/Program.cs` | 4–7 | registro de DI |
| `specs/00`, `01`, `03`, `04`, `05`, `06`, `CLAUDE.md`, `README.md`, spec da Fase 3 | 10 | documentos |

## Contrato JSON novo

camelCase, como o resto da API. Decimais como número. `dataHora` em ISO 8601 com offset `-03:00`
(`HorarioDeBrasiliaJsonConverter`). `dataEntrega` como `"yyyy-MM-dd"` (o `DateOnly` do `PedidoDto`).

```jsonc
// GET /materiais  (MaterialDto, item)
{ "id": 7, "codigo": "PAR-M8", "descricao": "Parafuso M8", "unidadeMedida": "UN", "ativo": true,
  "ficaEm": null }                                  // ou { "setorId": 1, "nome": "Corte a Laser" }
// POST /materiais e PUT /materiais/{id}  (corpo)
{ "codigo": "CH-3", "descricao": "Chapa 3 mm", "unidadeMedida": "KG", "setorDeEstoqueId": 1 }   // ausente/null = almoxarifado

// GET e PUT /estrutura/{id}/materiais  (MateriaisDoNoDto)
{ "estruturaItemId": 42,
  "materiais": [                                     // ordem por código
    { "materialId": 7, "codigo": "PAR-M8", "descricao": "Parafuso M8", "unidadeMedida": "UN",
      "ficaEm": null, "planejado": 10, "separado": 8, "pendencia": 2,
      "noPlanejado": true, "acimaDoPlanejado": false } ] }
// PUT /estrutura/{id}/materiais  (corpo; substitui a lista inteira; [] limpa)
{ "linhas": [ { "materialId": 7, "quantidade": 10 } ] }

// POST /estrutura/{id}/separacoes  (corpo)
{ "linhas": [ { "materialId": 7, "quantidade": 2 } ] }
// 201  (ResultadoDaSeparacaoDto)
{ "separacoes": [ /* SeparacaoDto */ ], "materiais": [ /* item de MateriaisDoNoDto, só os tocados */ ] }

// SeparacaoDto  (GET /estrutura/{id}/separacoes devolve a lista, mais recente primeiro)
{ "id": 91, "tipo": "Separacao",                     // ou "Estorno"
  "materialId": 7, "materialCodigo": "PAR-M8", "materialDescricao": "Parafuso M8", "unidadeMedida": "UN",
  "quantidade": 2, "dataHora": "2026-10-16T09:12:00-03:00", "usuarioId": 15, "usuarioNome": "Separador",
  "estornoDeId": null, "estornavel": true }
// POST /separacoes/{id}/estorno  → 201 com o SeparacaoDto do estorno

// GET /separacoes/pendencias  (PendenciaDeSeparacaoDto[], ordem: dataEntrega, pedido.id, no.id)
[ { "no": { "id": 42, "descricao": "Quadro soldado", "codigoDoComponente": "QD-01",
            "agrupamentoId": 3, "agrupamentoCodigo": "AG-01" },
    "pedido": { "id": 5, "numero": "PED-77", "cliente": "Cliente X", "dataEntrega": "2026-10-20",
                "atrasado": false, "pausa": null },  // pausa: PausaResumoDto { desde, porUsuarioNome, motivo }
    "destino": { "setorId": 3, "nome": "Solda" },   // null = sem Roteiro
    "materiais": [ { "materialId": 7, "codigo": "PAR-M8", "descricao": "Parafuso M8", "unidadeMedida": "UN",
                     "planejado": 10, "separado": 8, "pendencia": 2 } ] } ]

// GET /setores/{id}/fila  — campo novo em cada LinhaDaFila e em cada grupo de aguardandoMontagem
"material": { "completos": 2, "total": 3 }           // ou null
```

Erros: `{ "erro": "<código>", "mensagem": "<frase>" }` (o `ExecucaoControllerBase.Traduzir`); 404 sem corpo.

---

### Task 1: Schema, migração, perfil Separador e mapeamento

**Modelo:** sonnet. Molde: o caminho de `Setor.UtilizaKit` (commit da Task 1 da 3B) e `MovimentacaoConfiguration`.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (`dbo.Material`, `dbo.MaterialSeparacao`, comentário de `dbo.Perfil.Nome`)
- Create: `db/alter-fase-4.sql`
- Modify: `db/seed.sql` (perfil `Separador`, usuário `separador`)
- Modify: `src/Rastreamento.Domain/Entities/Material.cs`
- Create: `src/Rastreamento.Domain/Entities/MaterialSeparacao.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/Configurations/MaterialConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/MaterialSeparacaoConfiguration.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs` (`LimparAsync`)
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Security/SeedUsuariosDePerfilSenhaTests.cs`
- Modify: `tests/Rastreamento.Api.Tests/MateriaisEndpointsTests.cs` (o perfil `Almoxarifado` vira `Separador`)
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/MaterialSeparacaoMapeamentoTests.cs`
- Modify: `CLAUDE.md` (bloco "Fase 4 — `db/alter-fase-4.sql`" em "Comandos", depois do da Fase 3B)

**Interfaces:**
- Produces: `Material.SetorDeEstoqueId` (`int?`); entidade `MaterialSeparacao { Id, EstruturaItemId, MaterialId,
  Tipo, Quantidade, EstornoDeId, DataHora, UsuarioId }`; `TiposDeSeparacao.Separacao = "Separacao"`,
  `TiposDeSeparacao.Estorno = "Estorno"` (no mesmo arquivo da entidade); `DbSet<MaterialSeparacao> Separacoes`.

- [ ] **Step 1: Schema.** No `02`, o `SetorDeEstoqueId` e a FK `FK_Material_SetorDeEstoque` no `CREATE TABLE
  dbo.Material`, e a `dbo.MaterialSeparacao` trocada pela da seção 3.2 da spec, **verbatim** (colunas,
  constraints, `UX_MaterialSeparacao_EstornoDe`, `IX_MaterialSeparacao_EstruturaItem`). O comentário de
  `Perfil.Nome` passa a `Operador | Separador | Movimentador | PCP | Qualidade | Gestao | Administrador`.

- [ ] **Step 2: `db/alter-fase-4.sql`**, idempotente, no formato do `alter-fase-3b.sql`, em três blocos:
  1. `IF COL_LENGTH('dbo.Material','SetorDeEstoqueId') IS NULL` → `ADD SetorDeEstoqueId INT NULL CONSTRAINT
     FK_Material_SetorDeEstoque FOREIGN KEY REFERENCES dbo.Setor(Id)`.
  2. `IF COL_LENGTH('dbo.MaterialSeparacao','Responsavel') IS NOT NULL`: se `EXISTS (SELECT 1 FROM
     dbo.MaterialSeparacao)`, `THROW 50001, 'dbo.MaterialSeparacao tem linhas na forma antiga: migre-as a mao
     antes de rodar este script.', 1`; senão `DROP TABLE` e `CREATE TABLE` na forma nova, com os dois índices.
  3. Perfil: se existem `Almoxarifado` **e** `Separador`, `UPDATE dbo.Usuario SET PerfilId = <Separador> WHERE
     PerfilId = <Almoxarifado>` e `DELETE` do `Almoxarifado`; se só `Almoxarifado`, `UPDATE dbo.Perfil SET Nome =
     'Separador'`; se nenhum, `INSERT ('Separador')`.

- [ ] **Step 3: `db/seed.sql`.** `'Almoxarifado'` vira `'Separador'` no `MERGE` dos perfis, e um bloco
  `IF NOT EXISTS … 'separador'` no padrão do `movimentador`: senha **`Separador@123`**, `NomeCompleto`
  `N'Separador de Materiais'`, perfil `Separador`, hash BCrypt custo 11 **gerado** com
  `new BCryptPasswordHasher().Hash("Separador@123")` (por exemplo, num teste descartável que não fica no commit) e
  colado no script. O comentário do bloco segue o do `operador`/`movimentador`.

- [ ] **Step 4: aplicar no banco local** (`alter-fase-4.sql` e depois `seed.sql`, com `-b -f 65001`) e rodar o
  `alter` uma segunda vez: tem de sair sem erro e sem mudar nada.

- [ ] **Step 5: Escrever os testes que falham.**

```csharp
// SeedUsuariosDePerfilSenhaTests — mais um InlineData no Theory existente
[InlineData("separador", "Separador@123", "Separador")]

// DbContextMappingTests — o teste de perfis continua com 7, e ganha a troca de nome
[Fact]
public async Task O_perfil_Almoxarifado_virou_Separador()
{
  await using var db = NovoContexto();
  Assert.True(await db.Perfis.AnyAsync(p => p.Nome == "Separador"));
  Assert.False(await db.Perfis.AnyAsync(p => p.Nome == "Almoxarifado"));
}

// MaterialSeparacaoMapeamentoTests : TesteComBanco — cenário por ArvoreDeTesteNoBanco (uma Peça, um Material)
[Fact] Separacao_faz_a_volta_completa
  // grava Tipo=Separacao, Quantidade=2.5025m, DataHora explícita, UsuarioId=AutorId; relê e confere os 8 campos
[Fact] Estorno_aponta_a_separacao_original
  // grava a separação e um Estorno com EstornoDeId = original.Id; relê o estorno com EstornoDeId certo
[Fact] Segundo_estorno_da_mesma_linha_viola_UX_MaterialSeparacao_EstornoDe
  // DbUpdateException cuja mensagem (inner) contém "UX_MaterialSeparacao_EstornoDe"
[Fact] Estorno_sem_EstornoDeId_viola_CK_MaterialSeparacao_EstornoApontaOriginal
[Fact] Separacao_com_EstornoDeId_viola_CK_MaterialSeparacao_EstornoApontaOriginal
[Fact] Quantidade_zero_viola_CK_MaterialSeparacao_QuantidadePositiva
[Theory, InlineData(true), InlineData(false)] SetorDeEstoqueId_do_Material_faz_a_volta_completa(bool noSetor)
  // noSetor: aponta um Setor do cenário; senão null. Relê e compara.
```

- [ ] **Step 6: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter
  "FullyQualifiedName~MaterialSeparacaoMapeamento|FullyQualifiedName~DbContextMapping|FullyQualifiedName~SeedUsuariosDePerfil"`.
  Esperado: falha de compilação (`MaterialSeparacao`, `SetorDeEstoqueId`, `Separacoes` não existem).

- [ ] **Step 7: Entidades e mapeamento.** `Material.SetorDeEstoqueId`; `MaterialSeparacao` com XML doc no molde
  do de `Movimentacao` (só inclusão, correção por estorno); `MaterialSeparacaoConfiguration`: tabela
  `MaterialSeparacao`, `Tipo` `HasMaxLength(20)`, `Quantidade` `HasPrecision(18, 4)`, `DataHora`
  `ValueGeneratedOnAdd()` (mesmo motivo do comentário de `MovimentacaoConfiguration`); `DbSet Separacoes`.
  `ArvoreDeTesteNoBanco.LimparAsync` apaga, antes da `EstruturaMaterial`, as linhas de `MaterialSeparacao` do
  Agrupamento: primeiro as com `EstornoDeId IS NOT NULL`, depois o resto. `MateriaisEndpointsTests`: os dois
  testes do `Almoxarifado` passam a se chamar `Separador_nao_escreve_em_material` e
  `Separador_le_a_lista_de_materiais`, com `ClienteComo("Separador")` e o comentário ajustado.

- [ ] **Step 8: Rodar a suíte de Infrastructure e a de Api** — `dotnet test tests/Rastreamento.Infrastructure.Tests`
  e `dotnet test tests/Rastreamento.Api.Tests`. Esperado: tudo verde. `dotnet build Rastreamento.slnx
  -warnaserror`: 0 warnings.

- [ ] **Step 9: `CLAUDE.md`** — bloco "**Fase 4 — `db/alter-fase-4.sql`.**" no formato do da 3B: o que ele leva
  (a coluna, a tabela reescrita só se vazia, a troca do perfil), que aborta se a tabela antiga tiver linha, que
  depois dele se reaplica o `db/seed.sql` para ter o `separador`, e os dois comandos `cp`/`sqlcmd`.

- [ ] **Step 10: Commit** — `feat(fase-4): schema da separacao, Fica em do Material e perfil Separador`.

### Task 2: "Fica em" no cadastro de Material

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`MaterialDto`, `NovoMaterialDto`, record novo
  `SetorDeEstoqueDto`)
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeMaterialUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (só se a assinatura do construtor exigir)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeMaterialUseCaseTests.cs`
- Test: `tests/Rastreamento.Api.Tests/MateriaisEndpointsTests.cs`

**Interfaces:**
- Consumes: `Material.SetorDeEstoqueId` (Task 1); `ISetorRepository.ObterPorIdAsync`, `ListarAsync(true, ct)`.
- Produces: `public sealed record SetorDeEstoqueDto(int SetorId, string Nome);`
  `MaterialDto(int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo, SetorDeEstoqueDto? FicaEm)`;
  `NovoMaterialDto(…, int? SetorDeEstoqueId = null)` (parâmetro no fim, com o remark de "ausente = almoxarifado;
  o `PUT` é substituição inteira" no molde do `UtilizaKit` do `NovoSetorDto`);
  `CadastroDeMaterialUseCase(IMaterialRepository repositorio, ISetorRepository setores)`.

- [ ] **Step 1: Testes que falham** (Application, com os fakes de `Cadastros/Fakes.cs`):

```csharp
[Fact] Cadastrar_com_setor_ativo_grava_e_devolve_FicaEm
  // FicaEm == new SetorDeEstoqueDto(setor.Id, "Corte"); repositório guardou SetorDeEstoqueId == setor.Id
[Fact] Cadastrar_sem_setor_fica_no_almoxarifado          // FicaEm == null
[Fact] Cadastrar_com_setor_inexistente_e_recusado         // TipoDoErro == Validacao, Erro == "SetorDeEstoqueInvalido", nada salvo
[Fact] Cadastrar_com_setor_inativo_e_recusado             // idem
[Fact] Editar_troca_o_setor_e_volta_ao_almoxarifado_com_null
[Fact] Listar_traz_o_nome_do_setor_mesmo_inativo          // setor inativado depois de apontado: FicaEm.Nome continua
```

  Api: `Administrador_cadastra_material_com_Fica_em_e_le_de_volta` (POST com `setorDeEstoqueId`, GET traz
  `ficaEm.setorId`) e `Setor_inexistente_em_Fica_em_da_400` (`erro == "SetorDeEstoqueInvalido"`). O Setor do teste
  de Api é criado direto no banco e apagado no `DisposeAsync`, **depois** do Material.

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Application.Tests --filter
  CadastroDeMaterialUseCaseTests`. Esperado: falha de compilação.

- [ ] **Step 3: Implementar.** A validação do Setor vem **depois** das de campo obrigatório e **antes** do
  duplicado de código. `Listar` monta os nomes com uma leitura de `ListarAsync(incluirInativos: true)` do
  `ISetorRepository`, não uma por material. A falha do Setor é `Result.Falha("SetorDeEstoqueInvalido",
  TipoDeErro.Validacao)`; o `CadastroControllerBase.TraduzirFalha` já devolve `Validacao` como 400 `{ erro }`, então
  o controller não muda.

- [ ] **Step 4: Rodar** — os dois projetos de teste. Esperado: verde. Build com 0 warnings.

- [ ] **Step 5: Commit** — `feat(fase-4): Fica em no cadastro de Material`.

### Task 3: A regra 34, pura — `CalculoDaSeparacao`

**Modelo:** opus. É a regra da fase; os testes desta task são os que as mutações da seção 7.3 da spec atacam.

**Files:**
- Create: `src/Rastreamento.Application/Separacao/CalculoDaSeparacao.cs`
- Create: `src/Rastreamento.Application/Separacao/CodigosDaSeparacao.cs`
- Create: `src/Rastreamento.Application/Separacao/SeparacaoDtos.cs`
- Test: `tests/Rastreamento.Application.Tests/Separacao/CalculoDaSeparacaoTests.cs`

**Interfaces:**
- Consumes: `EstruturaMaterial`, `MaterialSeparacao`, `TiposDeSeparacao` (Task 1).
- Produces:

```csharp
public sealed record ResumoDoMaterial(
    int EstruturaItemId, int MaterialId, decimal Planejado, decimal Separado, bool NoPlanejado, bool DoAlmoxarifado)
{
  public decimal Pendencia => DoAlmoxarifado && NoPlanejado ? Math.Max(0m, Planejado - Separado) : 0m;
  public bool AcimaDoPlanejado => Separado > Planejado;
}

public static class CalculoDaSeparacao
{
  /// Separacoes menos estornos, de UM no e UM material.
  public static decimal Separado(IEnumerable<MaterialSeparacao> linhas);

  /// Um ResumoDoMaterial por (no, material) que esta no planejado OU tem linha no livro. Material fora do
  /// planejado com linha: NoPlanejado = false, Planejado = 0. Ordem: no, depois MaterialId.
  public static IReadOnlyList<ResumoDoMaterial> Resumir(
      IEnumerable<EstruturaMaterial> planejado, IEnumerable<MaterialSeparacao> linhas,
      IReadOnlySet<int> materiaisDoAlmoxarifado);

  /// O resumo da Fila de UM no: so os materiais no planejado E do almoxarifado. Null quando nao ha nenhum.
  public static ResumoDeMaterialDto? ResumoDaFila(IEnumerable<ResumoDoMaterial> doNo);
}

public static class CodigosDaSeparacao
{
  public const string MaterialForaDoPlanejado = nameof(MaterialForaDoPlanejado);
  public const string JaEstornada = nameof(JaEstornada);
  public const string EstornoDeEstorno = nameof(EstornoDeEstorno);
  public const string SeparacaoVazia = nameof(SeparacaoVazia);
  public const string MaterialRepetido = nameof(MaterialRepetido);
  public const string MaterialInvalido = nameof(MaterialInvalido);
  public const string NoComSeparacao = nameof(NoComSeparacao);
}
```

  `SeparacaoDtos.cs` traz os DTOs do contrato JSON (nomes exatos, um record por objeto):
  `MaterialDoNoComSeparacaoDto(int MaterialId, string Codigo, string Descricao, string UnidadeMedida,
  SetorDeEstoqueDto? FicaEm, decimal Planejado, decimal Separado, decimal Pendencia, bool NoPlanejado, bool
  AcimaDoPlanejado)`, `MateriaisDoNoDto(int EstruturaItemId, IReadOnlyList<MaterialDoNoComSeparacaoDto> Materiais)`,
  `LinhaDeMaterialDto(int MaterialId, decimal Quantidade)`, `MateriaisNovosDoNoDto([Required]
  IReadOnlyList<LinhaDeMaterialDto>? Linhas)`, `SeparacaoNovaDto([Required] IReadOnlyList<LinhaDeMaterialDto>?
  Linhas)`, `SeparacaoDto(int Id, string Tipo, int MaterialId, string MaterialCodigo, string MaterialDescricao,
  string UnidadeMedida, decimal Quantidade, DateTime DataHora, int UsuarioId, string UsuarioNome, int? EstornoDeId,
  bool Estornavel)`, `ResultadoDaSeparacaoDto(IReadOnlyList<SeparacaoDto> Separacoes,
  IReadOnlyList<MaterialDoNoComSeparacaoDto> Materiais)`, `ResumoDeMaterialDto(int Completos, int Total)`,
  `NoDaPendenciaDto(int Id, string Descricao, string? CodigoDoComponente, int AgrupamentoId, string
  AgrupamentoCodigo)`, `PedidoDaPendenciaDto(int Id, string Numero, string Cliente, DateOnly DataEntrega, bool
  Atrasado, PausaResumoDto? Pausa)`, `MaterialPendenteDto(int MaterialId, string Codigo, string Descricao, string
  UnidadeMedida, decimal Planejado, decimal Separado, decimal Pendencia)`, `PendenciaDeSeparacaoDto(NoDaPendenciaDto
  No, PedidoDaPendenciaDto Pedido, SetorResumoDto? Destino, IReadOnlyList<MaterialPendenteDto> Materiais)`.
  `SetorDeEstoqueDto` vem da Task 2; `SetorResumoDto` e `PausaResumoDto`, de `ExecucaoDtos`.

- [ ] **Step 1: Testes que falham** (dados montados à mão, sem fake de repositório):

```csharp
[Fact] Separado_e_separacoes_menos_estornos
  // linhas: Separacao 5, Separacao 3, Estorno 3  →  5
[Fact] Pendencia_e_o_planejado_menos_o_separado
  // planejado 10, separado 8, almoxarifado  →  Pendencia 2, AcimaDoPlanejado false
[Fact] Pendencia_nunca_fica_negativa_e_acima_e_marcado
  // planejado 10, separado 12  →  Pendencia 0, AcimaDoPlanejado true
[Fact] Material_que_fica_num_Setor_nao_tem_pendencia
  // planejado 10, separado 0, material fora de materiaisDoAlmoxarifado  →  Pendencia 0
[Fact] Material_que_saiu_do_plano_com_historico_continua_no_resumo
  // planejado vazio, uma Separacao 4  →  um resumo NoPlanejado false, Planejado 0, Separado 4, Pendencia 0
[Fact] Material_sem_plano_e_sem_linha_nao_aparece
[Fact] Resumir_separa_os_nos
  // o mesmo material em dois nós: dois resumos, cada um com o próprio separado
[Fact] ResumoDaFila_conta_materiais_completos_e_nao_quantidades
  // três materiais do almoxarifado no plano: pendências 0, 0, 2  →  Completos 2, Total 3
[Fact] ResumoDaFila_ignora_material_de_Setor_e_fora_do_plano
  // um do almoxarifado completo, um de Setor, um fora do plano  →  Completos 1, Total 1
[Fact] ResumoDaFila_sem_material_do_almoxarifado_e_nulo
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Application.Tests --filter
  CalculoDaSeparacaoTests`. Esperado: falha de compilação.

- [ ] **Step 3: Implementar** os três métodos e os DTOs. `Separado` compara `Tipo` com `TiposDeSeparacao`, não com
  literal.

- [ ] **Step 4: Rodar** — esperado: verde. **Mutação, medida e escrita no relatório:** trocar o `-` do estorno por
  `+` em `Separado`, e tirar o `DoAlmoxarifado &&` da `Pendencia`; cada uma tem de deixar vermelho ao menos um teste
  nomeado acima.

- [ ] **Step 5: Commit** — `feat(fase-4): calculo da separacao (regra 34)`.

### Task 4: `ISeparacaoRepository` e `SeparacaoRepository`

**Modelo:** sonnet.

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/ISeparacaoRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/SeparacaoRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/ErrosDoSqlServer.cs` (`EhViolacaoDeUnicidade`: 2601 ou 2627)
- Modify: `src/Rastreamento.Api/Program.cs` (`AddScoped<ISeparacaoRepository, SeparacaoRepository>()`)
- Create: `tests/Rastreamento.Application.Tests/Separacao/FakeSeparacaoRepo.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/SeparacaoRepositoryTests.cs`

**Interfaces:**
- Produces:

```csharp
/// Um Material como a separacao o mostra: com onde ele fica e o nome do Setor (inativo inclusive).
public sealed record MaterialDaSeparacao(
    int Id, string Codigo, string Descricao, string UnidadeMedida, bool Ativo, int? SetorDeEstoqueId, string? SetorDeEstoqueNome);

public interface ISeparacaoRepository
{
  Task<bool> NoExisteAsync(int estruturaItemId, CancellationToken ct);
  Task<IReadOnlyList<EstruturaMaterial>> ListarPlanejadoAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);
  Task<IReadOnlyList<MaterialSeparacao>> ListarLinhasAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);
  Task<MaterialSeparacao?> ObterLinhaAsync(int id, CancellationToken ct);
  Task<bool> FoiEstornadaAsync(int separacaoId, CancellationToken ct);
  Task<IReadOnlyList<MaterialDaSeparacao>> ListarMateriaisAsync(IReadOnlyCollection<int> materialIds, CancellationToken ct);
  /// Dos nos pedidos, os que tem ao menos um material do almoxarifado (SetorDeEstoqueId nulo) no planejado.
  Task<IReadOnlySet<int>> ListarNosComMaterialDoAlmoxarifadoAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct);
  /// Apaga o planejado do no e grava `linhas`, num SaveChanges so.
  Task SubstituirPlanejadoAsync(int estruturaItemId, IReadOnlyList<(int MaterialId, decimal Quantidade)> linhas, CancellationToken ct);
  /// Grava as separacoes num SaveChanges so (tudo ou nada) e preenche os Ids.
  Task GravarAsync(IReadOnlyList<MaterialSeparacao> linhas, CancellationToken ct);
  /// Grava o estorno; false quando outro estorno da mesma linha chegou antes (UX_MaterialSeparacao_EstornoDe).
  Task<bool> GravarEstornoAsync(MaterialSeparacao estorno, CancellationToken ct);
}
```

  `FakeSeparacaoRepo` implementa a interface em memória, com `List<EstruturaMaterial> Planejado`,
  `List<MaterialSeparacao> Linhas`, `Dictionary<int, MaterialDaSeparacao> Materiais`, `HashSet<int> Nos`,
  `int Gravacoes` e `bool EstornoConcorrenteNaProximaGravacao` (faz `GravarEstornoAsync` devolver `false` uma vez).
  **Pode** receber o `FakeEstruturaRepo` do `CenarioDeExecucao` e ler `Planejado` de `Estruturas.Materiais`, para os
  testes da Fila verem o mesmo planejado que o resto do cenário: o implementer escolhe e escreve a escolha no XML doc.

- [ ] **Step 1: Testes que falham** (`SeparacaoRepositoryTests : TesteComBanco`, cenário `ArvoreDeTesteNoBanco`):

```csharp
[Fact] GravarAsync_grava_todas_as_linhas_e_preenche_os_ids
[Fact] ListarLinhasAsync_traz_so_os_nos_pedidos
[Fact] SubstituirPlanejadoAsync_troca_a_lista_inteira
  // planejado {M1: 2}; substitui por {M2: 3}  →  ListarPlanejadoAsync == [{M2, 3}]
[Fact] SubstituirPlanejadoAsync_com_lista_vazia_limpa
[Fact] GravarEstornoAsync_devolve_false_no_segundo_estorno_da_mesma_linha
  // dois contextos; o segundo GravarEstornoAsync devolve false e não lança
[Fact] FoiEstornadaAsync_ve_o_estorno
[Fact] ListarMateriaisAsync_traz_o_nome_do_setor_de_estoque
[Fact] ListarNosComMaterialDoAlmoxarifadoAsync_ignora_no_so_com_material_de_Setor
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter
  SeparacaoRepositoryTests`. Esperado: falha de compilação.

- [ ] **Step 3: Implementar.** Leituras `AsNoTracking` e com `Contains`, no molde de `MateriaisDosNosAsync` do
  `ExecucaoRepository`. `GravarEstornoAsync`: `try { SaveChangesAsync } catch (DbUpdateException e) when
  (ErrosDoSqlServer.EhViolacaoDeUnicidade(e))` → desanexa a entidade (`Entry(estorno).State = Detached`) e devolve
  `false`. `EhViolacaoDeUnicidade` segue o padrão de `TemNumero` (2601 **ou** 2627).

- [ ] **Step 4: Rodar** — esperado: verde; build 0 warnings.

- [ ] **Step 5: Commit** — `feat(fase-4): repositorio da separacao`.

### Task 5: Materiais do nó — `GET`/`PUT /estrutura/{id}/materiais`

**Modelo:** sonnet.

**Files:**
- Create: `src/Rastreamento.Application/Separacao/MateriaisDoNoUseCase.cs`
- Create: `src/Rastreamento.Api/Controllers/MateriaisDoNoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Modify: `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs` (`TabelaAprovada`)
- Test: `tests/Rastreamento.Application.Tests/Separacao/MateriaisDoNoUseCaseTests.cs`
- Create: `tests/Rastreamento.Api.Tests/SeparacaoEndpointsTests.cs` (classe que as Tasks 5–8 estendem)

**Interfaces:**
- Consumes: `ISeparacaoRepository` (Task 4), `CalculoDaSeparacao.Resumir` (Task 3).
- Produces: `MateriaisDoNoUseCase(ISeparacaoRepository separacao)` com
  `Task<Result<MateriaisDoNoDto>> Obter(int noId, CancellationToken ct)` e
  `Task<Result<MateriaisDoNoDto>> Substituir(int noId, MateriaisNovosDoNoDto dto, CancellationToken ct)`;
  `internal static class ProjecaoDaSeparacao` (no mesmo arquivo ou em `Separacao/ProjecaoDaSeparacao.cs`) com
  `MaterialDoNoComSeparacaoDto Projetar(ResumoDoMaterial r, MaterialDaSeparacao m)`, reusada pelas Tasks 6 e 7.
  Rota `estrutura/{id:int}/materiais`, `PerfisDeEscrita = "PCP,Administrador"` no `PUT`.

- [ ] **Step 1: Testes que falham** (Application):

```csharp
[Fact] Obter_no_inexistente_e_404
[Fact] Obter_traz_planejado_separado_pendencia_e_FicaEm_por_codigo
[Fact] Obter_traz_o_material_que_saiu_do_plano_com_historico   // Review Focus 2: NoPlanejado false
[Fact] Substituir_troca_a_lista_inteira_e_devolve_o_resumo_novo
[Fact] Substituir_com_lista_vazia_limpa_o_planejado
[Theory, InlineData(0), InlineData(-1), InlineData(0.00001)] Substituir_com_quantidade_invalida_e_400_sem_gravar
  // Erro == "QuantidadeInvalida"; fake.Gravacoes == 0
[Fact] Substituir_com_material_repetido_e_400   // "MaterialRepetido"
[Fact] Substituir_com_material_inexistente_ou_inativo_e_400   // "MaterialInvalido", mensagem nomeia o Id ou o código
[Fact] Substituir_no_inexistente_e_404_antes_de_validar_o_corpo
```

  Api (`SeparacaoEndpointsTests`, cenário `CenarioDaFase3NaApi` mais Materiais criados no banco): `Pcp_substitui_e_le_os_materiais_do_no`;
  `Operador_e_Separador_nao_substituem_materiais_do_no` (403); `Separador_le_os_materiais_do_no` (200). A tabela
  aprovada ganha `["PUT estrutura/{id:int}/materiais"] = ["PCP", "Administrador"]`. A limpeza do
  `CenarioDaFase3NaApi.DisposeAsync` passa a apagar `MaterialSeparacao` (estornos primeiro) e `EstruturaMaterial`
  antes da árvore, e os Materiais que o cenário criar.

- [ ] **Step 2: Rodar e ver falhar.** Esperado: falha de compilação.

- [ ] **Step 3: Implementar.** Ordem: 404 → quantidade (`Quantidades.CabeNaColuna`, que já recusa ≤ 0 e escala) →
  repetido → existência/atividade (por `ListarMateriaisAsync`) → `SubstituirPlanejadoAsync` → relê e projeta.
  Toda validação antes da única escrita. O controller herda `ExecucaoControllerBase` e usa `Traduzir`.

- [ ] **Step 4: Rodar** Application e Api — verde; build 0 warnings.

- [ ] **Step 5: Commit** — `feat(fase-4): materiais do no editaveis pelo PCP`.

### Task 6: Separar, estornar e o histórico do nó

**Modelo:** opus. Autoria, corrida de estorno e a recusa de fora do planejado.

**Files:**
- Create: `src/Rastreamento.Application/Separacao/SeparacaoUseCase.cs`
- Create: `src/Rastreamento.Application/Separacao/ConsultaDeSeparacaoUseCase.cs` (só `Historico` nesta task)
- Create: `src/Rastreamento.Api/Controllers/SeparacaoController.cs`
- Create: `src/Rastreamento.Api/Controllers/EstornoDeSeparacaoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs`
- Test: `tests/Rastreamento.Application.Tests/Separacao/SeparacaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Api.Tests/SeparacaoEndpointsTests.cs`

**Interfaces:**
- Consumes: Tasks 3–5; `IExecucaoRepository.ListarNomesDeUsuariosAsync` e `ObterPedidoDoNoAsync`; `QuemLe`,
  `Falhas` (internal, mesmo assembly).
- Produces:
  `SeparacaoUseCase(ISeparacaoRepository separacao, IExecucaoRepository execucao)` com
  `Task<Result<ResultadoDaSeparacaoDto>> Separar(int noId, SeparacaoNovaDto dto, int usuarioId, CancellationToken ct)` e
  `Task<Result<SeparacaoDto>> Estornar(int separacaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)`;
  `ConsultaDeSeparacaoUseCase(ISeparacaoRepository separacao, IExecucaoRepository execucao, IEstruturaRepository
  estruturas, IReceitaPadraoRepository catalogo, TimeProvider relogio)` com
  `Task<Result<IReadOnlyList<SeparacaoDto>>> Historico(int noId, QuemLe quem, CancellationToken ct)`.
  Rotas: `POST estrutura/{id:int}/separacoes` (`"Separador,Administrador"`, 201), `GET estrutura/{id:int}/separacoes`
  em `SeparacaoController`; `POST separacoes/{id:int}/estorno` (`"Separador,PCP,Administrador"`, 201) em
  `EstornoDeSeparacaoController`, que passa `EhPcpOuAdministrador()` como `podeEstornarDeOutros`.

- [ ] **Step 1: Testes que falham** (Application; o cenário é um `FakeSeparacaoRepo` com um nó e materiais):

```csharp
[Fact] Separar_grava_uma_linha_por_material_com_autor_e_devolve_o_resumo
  // r.Valor.Separacoes.Count == 2; todas Tipo "Separacao", UsuarioId == usuario; Materiais só dos 2 tocados
[Fact] Separar_acima_do_planejado_aceita_e_marca
  // planejado 10, separa 12  →  Sucesso; Materiais[0].AcimaDoPlanejado true; Pendencia 0
[Fact] Separar_material_fora_do_planejado_e_409_e_nao_grava_nada
  // duas linhas, a segunda fora do plano  →  Erro "MaterialForaDoPlanejado", TipoDeErro.Conflito,
  // Detalhe contém o código do material; fake.Gravacoes == 0
[Fact] Separar_material_que_fica_num_Setor_e_esta_no_planejado_e_aceito   // D16
[Fact] Separar_com_lista_vazia_e_400                    // "SeparacaoVazia"
[Fact] Separar_com_quantidade_invalida_e_400            // "QuantidadeInvalida"
[Fact] Separar_com_material_repetido_e_400              // "MaterialRepetido"
[Fact] Separar_no_inexistente_e_404
[Fact] Separar_em_Pedido_pausado_e_aceito               // Review Focus 5
[Fact] Estornar_pelo_autor_grava_o_inverso_apontando_a_original
  // Tipo "Estorno", Quantidade/Material/Nó iguais, EstornoDeId == original.Id
[Fact] Estornar_pelo_PCP_registro_de_outro_e_aceito
[Fact] Estornar_registro_de_outro_sem_ser_PCP_e_403     // "Proibido", TipoDeErro.Proibido, nada gravado
[Fact] Estornar_um_estorno_e_409                        // "EstornoDeEstorno"
[Fact] Estornar_duas_vezes_e_409                        // "JaEstornada"
[Fact] Estornar_quando_outro_estorno_chega_antes_e_409  // fake.EstornoConcorrenteNaProximaGravacao  →  "JaEstornada"
[Fact] Estornar_separacao_de_material_que_saiu_do_plano_e_aceito   // Review Focus 2
[Fact] Historico_mais_recente_primeiro_com_nome_do_autor_e_estornavel
  // estornavel: true para a separação do próprio leitor ainda não estornada; false para a já estornada e para
  // o estorno; com QuemLe(outro, false), false em todas; com QuemLe(pcp, true), true na não estornada
```

  Api: `Separador_separa_e_le_o_historico` (201 + GET), `Operador_nao_separa` (403), `Movimentador_nao_estorna_separacao`
  (403), `Separador_nao_estorna_separacao_de_outro_Separador` (403 com `erro == "Proibido"`),
  `Pcp_estorna_separacao_de_outro` (201), e **Review Focus 3**,
  `Dois_estornos_simultaneos_da_mesma_separacao_dao_um_201_e_um_409`: dois `PostAsync` disparados com `Task.WhenAll`,
  status ordenados `[201, 409]`, o 409 com `erro == "JaEstornada"`, e no banco exatamente uma linha com
  `EstornoDeId == original`. O cenário ganha dois usuários `Separador` (`UsuarioDeTeste.CriarAsync(…, "Separador")`).
  A tabela aprovada ganha `POST estrutura/{id:int}/separacoes` e `POST separacoes/{id:int}/estorno` com os perfis
  acima.

- [ ] **Step 2: Rodar e ver falhar.** Esperado: falha de compilação.

- [ ] **Step 3: Implementar** com a ordem de validação da decisão P8. `Separar`: valida tudo, monta as
  `MaterialSeparacao` (`DataHora = DateTime.UtcNow`), `GravarAsync` uma vez, relê planejado e linhas do nó e
  projeta só os materiais tocados. `Estornar`: `ObterLinhaAsync` → checagens → `FoiEstornadaAsync` →
  `GravarEstornoAsync`, e `false` vira `JaEstornada`. A mensagem do `MaterialForaDoPlanejado` nomeia o material
  (código e descrição) e diz que o PCP o acrescenta nos materiais do nó.

- [ ] **Step 4: Rodar** Application e Api — verde; build 0 warnings. **Mutação, escrita no relatório:** tirar a
  recusa de fora do planejado e permitir estorno de registro alheio sem `podeEstornarDeOutros`; cada uma derruba
  um teste nomeado acima.

- [ ] **Step 5: Commit** — `feat(fase-4): separar, estornar e historico de separacao`.

### Task 7: A lista do Separador — `GET /separacoes/pendencias`

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`ContextoDoNo`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs` (`ListarNosEmProducaoAsync`)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs` (`DataDeEntregaDoPedido`)
- Modify: `src/Rastreamento.Application/Separacao/ConsultaDeSeparacaoUseCase.cs` (`Pendencias`)
- Modify: `src/Rastreamento.Api/Controllers/SeparacaoController.cs` (`GET separacoes/pendencias`)
- Test: `tests/Rastreamento.Application.Tests/Separacao/PendenciasDeSeparacaoTests.cs`
- Test: `tests/Rastreamento.Api.Tests/SeparacaoEndpointsTests.cs`

**Interfaces:**
- Consumes: `IExecucaoRepository.ListarNosEmProducaoAsync`, `LeitorDeEstado` (nome do nó com o fallback da regra
  19, código do Componente e `Calc.PrimeiroPasso`), `IReceitaPadraoRepository.ObterSetoresPorIdAsync`,
  `PrazoDeEntrega.HojeEmBrasilia`/`EstaAtrasado`, Tasks 3–4.
- Produces: `ContextoDoNo(…, IReadOnlyList<MaterialDoNo> Materiais, DateOnly PedidoDataEntrega = default, string
  PedidoStatus = "Aberto")`; `Task<Result<IReadOnlyList<PendenciaDeSeparacaoDto>>> Pendencias(CancellationToken ct)`,
  sob `ConsultarAsync`.

- [ ] **Step 1: Testes que falham** (Application; `CenarioDeExecucao` + `FakeSeparacaoRepo`, `RelogioFixo`):

```csharp
[Fact] Lista_so_no_com_pendencia_em_material_do_almoxarifado
  // nó A com parafuso pendente; nó B só com chapa de Setor; nó C com parafuso completo  →  só A
[Fact] Ordena_pela_data_de_entrega_depois_pedido_depois_no
[Fact] Destino_e_o_primeiro_passo_do_Roteiro_e_nulo_sem_Roteiro
[Fact] Pedido_pausado_continua_na_lista_com_a_pausa
[Fact] Pedido_atrasado_vem_marcado
  // dataEntrega ontem em Brasília  →  Atrasado true; hoje  →  false
[Fact] Pedido_concluido_ou_cancelado_nao_entra
  // o fake de execução já filtra por status; o teste o prova montando um Pedido "Concluido" com pendência
[Fact] Materiais_da_pendencia_sao_so_os_pendentes_do_almoxarifado
```

  Api: `Pendencias_traz_o_no_do_cenario_com_destino_e_prazo` (um nó com Material do almoxarifado no planejado, sem
  separação: aparece com `destino.nome` do primeiro passo e `pedido.dataEntrega` do cenário) e
  `Pendencias_nao_traz_no_de_Pedido_concluido` (o mesmo cenário com `UPDATE dbo.Pedido SET Status = 'Concluido'`
  direto no banco: o nó some). É este o teste que mata a mutação "tirar o filtro de Pedido aberto" da seção 7.3 da
  spec, porque o filtro mora no `ExecucaoRepository` real, que o fake da Application não exercita.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar.** `ExecucaoRepository.ListarNosEmProducaoAsync` projeta `p.DataEntrega` e `p.Status`
  nos campos novos; o fake os lê de `DataDeEntregaDoPedido`/`StatusDoPedido`. `Pendencias`: contexto → filtra os
  nós com `ListarNosComMaterialDoAlmoxarifadoAsync` → carrega o `LeitorDeEstado` só desses → planejado, linhas e
  materiais deles → `Resumir` → descarta o nó sem pendência → monta e ordena.

- [ ] **Step 4: Rodar** Application e Api (a suíte de Execução inteira também, porque `ContextoDoNo` mudou) —
  verde; build 0 warnings.

- [ ] **Step 5: Commit** — `feat(fase-4): lista do Separador`.

### Task 8: O resumo de material na Fila do Setor

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`LinhaDaFilaDto`, `MontagemPendenteDto`)
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs` (`Separacao` e `Consulta()`)
- Test: `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Api.Tests/SeparacaoEndpointsTests.cs`

**Interfaces:**
- Consumes: `CalculoDaSeparacao.ResumoDaFila` (Task 3), `ISeparacaoRepository` (Task 4).
- Produces: `LinhaDaFilaDto(…, IReadOnlyList<EstornavelDto> Estornaveis, ResumoDeMaterialDto? Material = null)`;
  `MontagemPendenteDto(…, bool ConjuntoCompleto, ResumoDeMaterialDto? Material = null)`;
  `ConsultaDeExecucaoUseCase(…, IReceitaPadraoRepository catalogo, ISeparacaoRepository separacao)`;
  `CenarioDeExecucao.Separacao` (`FakeSeparacaoRepo`).

- [ ] **Step 1: Testes que falham:**

```csharp
[Fact] Folha_a_iniciar_aqui_traz_o_resumo_de_material
  // folha com Roteiro [Corte], dois materiais do almoxarifado, um completo  →  aIniciar[0].Material == (1, 2)
[Fact] Pai_que_inicia_aqui_traz_o_resumo_no_cartao_de_montagem
  // Kit com Roteiro [Solda], filhos aguardando na Solda  →  AguardandoMontagem[0].Material == (0, 1)
[Fact] Cartao_de_montagem_que_nao_inicia_aqui_vem_sem_resumo
  // os mesmos filhos aguardando num Setor que não é o primeiro passo do pai  →  Material null
[Fact] Em_trabalho_vem_sem_resumo
[Fact] No_so_com_material_de_Setor_vem_sem_resumo
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar.** Na `FilaAsync`, uma leitura de planejado, linhas e materiais para os nós de
  `aIniciar` e dos pais com `IniciaAqui`, e o `Material` só nessas duas posições. Os `new LinhaDaFilaDto` de
  `emTrabalho` continuam sem o argumento. `Program.cs` não muda (o DI resolve o parâmetro novo).

- [ ] **Step 4: Rodar** Application e Api — verde; build 0 warnings. **Mutação, escrita no relatório:** preencher
  o resumo também no cartão que não inicia aqui; `Cartao_de_montagem_que_nao_inicia_aqui_vem_sem_resumo` morre.

- [ ] **Step 5: Commit** — `feat(fase-4): resumo de material na Fila do Setor`.

### Task 9: Nó com separação não se apaga (`NoComSeparacao`)

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs` (`TemSeparacaoAsync`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` (`ExcluirNo`)
- Modify: `tests/Rastreamento.Application.Tests/Estrutura/Fakes.cs` (`FakeEstruturaRepo.NosComSeparacao`) e todo
  outro fake de `IEstruturaRepository` em `tests/`
- Test: `tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs`
- Test: `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`

**Interfaces:**
- Produces: `Task<bool> TemSeparacaoAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)`; em
  `ExcluirNo`, depois do `PedidoNaoAberto`, `Result<bool>.Falha(CodigosDaSeparacao.NoComSeparacao,
  TipoDeErro.Conflito, "<frase>")`, com a frase "Este item tem separação de material registrada e não pode ser
  excluído: o histórico de separação não se apaga."

- [ ] **Step 1: Testes que falham** — **Review Focus 1**:
  `Excluir_no_cuja_subarvore_tem_separacao_e_409_e_nao_apaga` (Application: `NosComSeparacao` contém um
  descendente; `Erro == "NoComSeparacao"`; a árvore continua inteira) e
  `Excluir_no_com_separacao_estornada_continua_409` (P2); Api:
  `Delete_de_no_com_separacao_responde_409_NoComSeparacao` (separação gravada no banco antes do `DELETE`; status 409,
  `erro == "NoComSeparacao"`, o nó continua no banco).

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar** a consulta (`AnyAsync` com `Contains` sobre `MaterialSeparacao.EstruturaItemId`) e a
  guarda, dentro da mesma transação, depois de travar a subárvore.

- [ ] **Step 4: Rodar** Application e Api — verde; build 0 warnings.

- [ ] **Step 5: Commit** — `feat(fase-4): no com separacao nao se exclui`.

### Task 10: Documentos

**Modelo:** sonnet. Produto é prosa: a review lê contra a spec e mede cada contagem que o texto afirmar.

**Files:**
- Modify: `specs/00-visao-geral.md`, `specs/01-dominio-e-regras-de-negocio.md`, `specs/03-arquitetura-tecnica.md`,
  `specs/04-fluxos-de-usuario.md`, `specs/05-api-endpoints.md`, `specs/06-roadmap-mvp.md`, `CLAUDE.md`, `README.md`
- Modify: `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md` (nota na seção 2.2)

- [ ] **Step 1: Aplicar a seção 9 da spec, item por item**, mais o que este plano decidiu: no `01`, as regras 4 e 5
  reescritas, a regra 34 (com o texto da seção 4 da spec) e uma linha para P2 ("nó com separação não se exclui");
  no `05`, as rotas do contrato JSON deste plano, o bloco "Roteiro e Materiais do nó depois da cópia" atualizado, a
  tabela "Contrato de erro da Separação" com os códigos das Global Constraints (inclui `NoComSeparacao` e
  `SetorDeEstoqueInvalido`), e o `NoComSeparacao` na lista de recusas do `DELETE /estrutura/{id}`; no `00`, `03`,
  `CLAUDE.md` (seção Stack) e `README.md`, `Almoxarifado` vira `Separador`.
- [ ] **Step 2: Medir** — `grep -rn "Almoxarifado" specs/ CLAUDE.md README.md src/ tests/ db/` devolve só
  menções históricas explicadas (a nota da troca de nome no `00`/`01` e o bloco do `alter` no `CLAUDE.md`), e o
  relatório lista cada linha que sobrou e por quê.
- [ ] **Step 3: Commit** — `docs(fase-4): specs, roadmap e CLAUDE.md da separacao`.

---

## Self-review (feito ao escrever este plano)

- **Cobertura da spec:** 3.1–3.4 → Task 1; 3.5 (nada muda) é respeitado por todas; 4 → Tasks 3, 6 e 10; 5.1 → Tasks
  1, 3; 5.2 → Tasks 2, 5, 6, 7, 8; 5.3 → Tasks 5, 6 (tabela aprovada) e 2 (Administrador); 5.4 → Tasks 3, 10; 7.1 →
  Tasks 1–9; 7.3 → mutações nas Tasks 3, 6, 8, mais a do filtro de Pedido aberto, que o teste de Api
  `Pendencias_nao_traz_no_de_Pedido_concluido` da Task 7 cobre; 8 (riscos) → registrado no `05` e no `01` pela Task 10;
  9 → Task 10. As seções 6 e 7.2 são do plano 2.
- **Tipos:** `ResumoDoMaterial`, `ResumoDeMaterialDto`, `MaterialDaSeparacao`, `SetorDeEstoqueDto` e
  `MaterialDoNoComSeparacaoDto` têm um nome só em todas as tasks.
