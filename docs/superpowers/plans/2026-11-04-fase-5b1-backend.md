# Fase 5B-1 — Perda e descarte — Plano 1 de 2 (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao backend a baixa (perda e descarte) de qualquer nó, de parte ou de tudo, o "Descartar tudo" do
Agrupamento e do Pedido, o estorno inteiro da baixa e o fechamento que conta o perdido.

**Architecture:** Posição terminal nova `Perdido` e tipo `Perda` em `dbo.Movimentacao`, com `dbo.Perda` reescrita
como agrupador da baixa (molde de `dbo.Remessa`), apontada por `Movimentacao.PerdaId`. Uma pasta nova
`Application/Baixa/` reúne a conta pura (`CalculoDaBaixa`: o que sai, os filhos que saem junto, o que vira sobra e o
que trava no topo), o caso de uso de escrita e prévia (`BaixaUseCase`) e o de leitura (`ConsultaDeBaixasUseCase`). A
calculadora da execução passa a descontar o perdido de "a iniciar" do pai e a ignorar o filho que saiu do projeto; o
fechamento passa a contar o perdido. Tudo corre na transação SERIALIZABLE de `ExecutarAsync`.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-fase-5b1-perda-e-descarte-design.md`, aprovada em 2026-11-04 (D1–D13 do
usuário, C1–C27 do desenho). Branch: `claude/happy-wright-14yv9r`, spec em `7668edc`, sobre `bb8626e`. O plano 2
(front, `docs/superpowers/plans/2026-11-04-fase-5b1-front.md`) consome a seção "Contrato JSON novo" daqui.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de
  Critical/Important → re-review). Nenhuma task deste plano tem dispensa prevista.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA); pacote de review com
  `scripts/review-package BASE HEAD`, nunca `HEAD~1`. Ledger do plano em `.superpowers/sdd/2026-11-04-fase-5b1/`;
  artefatos com nome escopado (`backend-task-N-…`).
- **A review de branch inteira só é despachada depois que o usuário fizer a conferência no navegador**, a última task
  do plano 2 (regra do usuário de 2026-10-07, no `CLAUDE.md`).
- **Schema pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-fase-5b1.sql`
  idempotente (formato do `db/alter-fase-5a.sql`, `-b -f 65001`), depois o mapeamento EF. Nada de `Add-Migration`.
- **O livro continua só de inclusão**, e `dbo.Perda` também: nenhum `UPDATE` nem `DELETE` fora da limpeza de teste.
  "Estornada" é calculado (existe `Estorno` apontando a linha).
- **De onde se dá baixa** (seção 4.2 da spec): `AIniciar`, `NoSetor`, `AguardandoColeta`, `AguardandoMontagem`,
  `AguardandoConserto`, `EmConserto`, `ConsertoPronto`, `NaExpedicao`, `Conforme`, `NaoConforme`, `Liberada`.
  **Nunca** `AguardandoLiberacao`, `Montado`, `Expedido`, `Perdido`.
- **Motivos, exatos:** `PerdaArmazem`, `MortaEmProcesso`, `Descarte`.
- **Perfis, exatos:** escrever e estornar baixa `Qualidade,PCP,Administrador` (o estorno: o autor, ou PCP/
  Administrador, decidido pelo caso de uso com `403 Proibido`). Leituras: qualquer autenticado.
- **Recusa que nomeia um nó usa `EstadoDeExecucao.Nome`** (regra 19), nunca `no {id}`. Todo cenário que afirma frase
  com nome tem **um nó copiado do catálogo, sem descrição própria** (lição da Fase 4).
- **Data de entrega de teste que não deve atrasar é `"2099-12-31"`** (lição da 5A).
- Nomes de domínio em português, espelhando o DDL; técnicos em inglês. **Citação por nome**, nunca `arquivo.ext:NN`.
  Comentário de código **não cita** ledger, brief nem "Task N"; decisões citadas como "D*n*/C*n* da spec da Fase
  5B-1" ou "decisão P*n* do plano de backend da Fase 5B-1".
- `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`.
  Teste de banco **escopa a asserção** nas linhas que ele inseriu.
- Mensagens de erro dos `.cs` novos **com** acento; comentários sem acento, como os vizinhos.
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em chamada
  separada do `task-brief`/`review-package`.

## Review Focus

Entradas que a spec implica e que nenhum teste das tasks exercitaria sem esta lista (cada uma tem teste na task dona,
marcado "Review Focus"):

1. **Duas baixas simultâneas da mesma posição** que, somadas, passam do saldo — uma `201`, a outra `409
   SaldoInsuficiente`; nunca `500`, nunca saldo negativo (Task 5).
2. **Baixa e início do pai ao mesmo tempo** sobre o mesmo "a iniciar" — as duas travam o nó; quem chega depois vê o
   saldo novo e recusa (`SaldoInsuficiente`), nunca o pai consome filhos que a baixa já tirou (Task 5).
3. **Estornar duas vezes a mesma baixa, em paralelo** — uma `200`, a outra `409 JaEstornado` (Task 7).
4. **Pedido pausado** — a baixa e o "Descartar tudo" continuam (regra 31, C13) (Task 6).
5. **Nó de três níveis com lote dividido** — a baixa de parte da Peça desce a C6 dois níveis, e o estorno devolve os
   dois (Tasks 3 e 7).

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem         # na nuvem; na máquina local: docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
```

Banco de máquina local que já existe: aplicar o `db/alter-fase-5b1.sql` (Task 1) com o bloco que a Task 1 acrescenta
ao `CLAUDE.md`. **Baseline:** o `CLAUDE.md` registra backend **1.736** em `4a0fca4` (2026-10-22); a 5A mesclada fechou
Api 414 · Application 1.036 · Infrastructure 286. **O pré-flight remede na base da Task 1** e escreve no ledger.
Cada implementer reporta o **delta** da própria task.

## Decisões deste plano (onde a spec deixou a escolha)

- **P1 — Um controller novo, `BaixaController`**, recurso `baixa` em `permissoes.ts` (a guarda de espelhamento do
  front compara um arquivo com UMA entrada). Todas as escritas (dar baixa, descartar Agrupamento/Pedido, as prévias e
  o estorno) ficam nele sob `Qualidade,PCP,Administrador`; as duas leituras, no mesmo arquivo, sem `Roles`.
  `PerfisDeEscritaDeclaradosTests.TabelaAprovada` ganha a linha.
- **P2 — A calculadora recebe duas entradas novas, opcionais**: `IReadOnlyDictionary<int, decimal>?
  perdidosDeAIniciar` (o líquido das linhas `Perda` com origem `AIniciar`, menos os estornos delas) e
  `IReadOnlySet<int>? saidosDoProjeto` (C22). `FaltaMontar(pai) = Quantidade − TotalMontado − perdidosDeAIniciar`.
  Método novo `FilhosQueContam(paiId)`, que é `Filhos(paiId)` sem os `saidosDoProjeto`; ele substitui `Filhos` em
  `CalcularMontabilidade`, `ConjuntosAEspera`, `ConjuntoCompleto` e no consumo de `ApontamentoUseCase.IniciarNoAsync`.
  `Filhos` continua para o resto (a árvore da tela).
- **P3 — "Saiu do projeto" é só o próprio nó** (C22): `saidosDoProjeto` são os nós com baixa `Tudo = 1`, motivo
  `Descarte`, não estornada, e `Perda.EstruturaItemId` igual ao nó. O "ancestral que não seja o pai" da seção 4.5 da
  spec é dispensável: o descarte total de um ancestral leva o pai junto, e não há montagem a calcular.
- **P4 — A conta da baixa é pura e a mesma para gravar e para a prévia.** `CalculoDaBaixa.Planejar` recebe a
  calculadora (já com a árvore toda do nó carregada) e o pedido, e devolve `PlanoDaBaixa` ou a falha. A escrita chama
  dentro da transação, depois das travas; a prévia chama fora dela, sem travas, e nada grava.
- **P5 — A árvore carregada é a do nó até as folhas, mais a cadeia até a Peça do topo** (para `incluirTopo`, D13).
  Repositório novo `ListarSubarvoreAsync(noId)` (o nó e os descendentes, ordem de `Id`) e o existente
  `ListarCadeiaAteOTopoAsync` se houver; se não houver, `ListarAncestraisAsync(noId)`. Trava: todos os nós da
  subárvore e da cadeia, em ordem crescente de `Id` (spec da Fase 3, seção 8.1), depois o Pedido.
- **P6 — O "Descartar tudo" de Agrupamento e Pedido** carrega a floresta das Peças do escopo e planeja um `Tudo` por
  Peça com algo em produção; uma `Perda` só, `Tudo = 1`, motivo `Descarte`, `AgrupamentoId` ou nenhum dos dois.
- **P7 — `CalculoDoFechamento.Calcular` ganha `bool teveInicio`**, e `PecaNoFechamento` ganha `decimal Perdido`.
  `Aberto` deixa de sair cedo; sai cedo só `Cancelado` e Pedido sem Peça. Sem concluir, `Aberto` continua `Aberto`
  quando `!teveInicio`, e o `Concluido` reaberto volta a `Aberto` quando `!teveInicio` (C23). Repositório:
  `PedidosComInicioAsync(pedidoIds)` (há linha `Inicio` líquida de estorno num nó do Pedido).
- **P8 — A Fila do Setor lê a sobra de Pedido concluído** (C25) por `ListarNosEmProducaoAsync(bool
  incluirConcluidos = false)`; a `ConsultaDeExecucaoUseCase` pede `true` e põe os nós de Pedido `Concluido` **só** na
  seção "Sobra". As Tarefas continuam com o padrão.
- **P9 — A lista do Separador e o resumo de material da Fila** filtram o nó perdido ou descartado por inteiro (C26):
  o saldo dele em `Perdido`, pela calculadora, é igual à quantidade do nó. O nó todo montado ou expedido, e o perdido
  só em parte, continuam na lista.
- **P10 — `incluirTopo` grava a Peça do topo de `AIniciar`**, com `min(travaNoTopo, AIniciar(topo))`. O
  `travaNoTopo` é calculado subindo a cadeia (seção 4.6 da spec) **depois** de aplicar a baixa do Item ao estado.

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-fase-5b1.sql`, `CLAUDE.md` (bloco do alter) | 1 | schema e migração |
| `Domain/Entities/{Posicoes,Movimentacao,Perda}.cs`, `Infrastructure/Persistence/Configurations/{PerdaConfiguration,MovimentacaoConfiguration}.cs`, `RastreamentoDbContext.cs`, `Application/Execucao/Local.cs` | 1 | entidades e mapeamento |
| `Application/Execucao/{CalculadoraDeExecucao,LeitorDeEstado,ConjuntoCompleto,ApontamentoUseCase}.cs`, `Domain/Abstractions/IExecucaoRepository.cs`, `Infrastructure/Persistence/ExecucaoRepository.cs` | 2 | C5, C22 na execução |
| `Application/Baixa/{CalculoDaBaixa,BaixaDtos,CodigosDaBaixa}.cs` | 3 | a conta pura |
| `Application/Expedicao/{CalculoDoFechamento,FechamentoDoPedido}.cs` | 4 | fechamento com perdido |
| `Domain/Abstractions/IBaixaRepository.cs`, `Infrastructure/Persistence/BaixaRepository.cs`, `Application/Baixa/BaixaUseCase.cs`, `Api/Controllers/BaixaController.cs`, `Application/Execucao/EstornoUseCase.cs`, `Api/Program.cs` | 5 | baixa de um nó, prévia, recusa na rota genérica |
| `Application/Baixa/BaixaUseCase.cs`, `Api/Controllers/BaixaController.cs` | 6 | "Descartar tudo" |
| `Application/Baixa/{BaixaUseCase,ConsultaDeBaixasUseCase}.cs`, `Api/Controllers/BaixaController.cs` | 7 | estorno e leituras |
| `Application/Estrutura/MontagemDeEstruturaUseCase.cs`, `Application/Execucao/ConsultaDeExecucaoUseCase.cs`, `Application/Separacao/ConsultaDeSeparacaoUseCase.cs`, `Infrastructure/Persistence/ExecucaoRepository.cs` | 8 | D12, C24–C26 |
| `specs/00`, `01`, `04`, `05`, `06`, `CLAUDE.md`, `README.md` | 9 | documentos |

## Contrato JSON novo

```jsonc
// POST /api/estrutura/{id}/baixas  e  POST /api/estrutura/{id}/baixas/previa
{ "motivo": "PerdaArmazem" | "MortaEmProcesso" | "Descarte",
  "observacao": "texto?",                 // aparada; <= 500
  "tudo": false,
  "origens": [ { "posicao": "NoSetor", "setorId": 3, "ordem": 2, "quantidade": 1 } ],  // vazia/ausente com tudo
  "incluirTopo": false }                  // só baixa de parte de Item (D13)

// POST /api/agrupamentos/{id}/descarte[/previa]  e  POST /api/pedidos/{id}/descarte[/previa]
{ "observacao": "texto?" }

// 201 das escritas: BaixaDto
{ "id": 7, "motivo": "Descarte", "observacao": null,
  "escopo": { "tipo": "No" | "Agrupamento" | "Pedido", "id": 812 },
  "tudo": false, "dataHora": "2026-11-04T14:20:00-03:00", "usuarioId": 2, "usuarioNome": "Planejamento e Controle",
  "estornada": false, "estornavel": true,
  "linhas": [ { "estruturaItemId": 812, "descricao": "Suporte lateral", "codigo": "SUP-114",
                "origemPosicao": "AIniciar", "origemSetorId": null, "origemSetorNome": null, "origemOrdem": null,
                "quantidade": 3 } ] }

// 200 das prévias: PreviaDaBaixaDto
{ "linhas": [ /* LinhaDaBaixaDto, como acima */ ],
  "viramSobra": [ { "estruturaItemId": 830, "descricao": "Chapa dobrada", "quantidade": 2 } ],
  "travaNoTopo": { "pecaId": 812, "descricao": "Suporte lateral", "quantidade": 1, "aIniciar": 3 } | null }

// POST /api/baixas/{id}/estorno -> 200 MovimentacaoDto[]   (os estornos, como o da remessa)
// GET /api/estrutura/{id}/baixas e GET /api/pedidos/{id}/baixas -> 200 BaixaDto[] (mais recente primeiro)
```

Erros (corpo `{ erro, mensagem }`, como os da execução): `MotivoInvalido`, `OrigemInvalida`, `ItemRepetido`,
`TextoLongoDemais`, `QuantidadeInvalida`, `LoteGrandeDemais` (400); `SaldoInsuficiente`, `NadaEmProducao`,
`LiberacaoEmAberto`, `PedidoFechado`, `JaEstornado`, `EstornoImpossivel`, `DiminuirPelaBaixa` (409); `Proibido`
(403); 404 sem corpo de código. A posição `Perdido` passa a aparecer em todo `SaldoDto` e em todo `LocalDto`.

---

### Task 1: Schema, migração e mapeamento

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (`dbo.Perda` e as constraints de `dbo.Movimentacao`), `CLAUDE.md` (bloco do
  alter, no molde do da 5A)
- Create: `db/alter-fase-5b1.sql`
- Modify: `src/Rastreamento.Domain/Entities/{Posicoes,Movimentacao}.cs`; Create: `src/Rastreamento.Domain/Entities/Perda.cs`
- Create/Modify: `src/Rastreamento.Infrastructure/Persistence/Configurations/PerdaConfiguration.cs`, a configuração de
  `Movimentacao`, `RastreamentoDbContext.cs` (`DbSet<Perda> Perdas`)
- Modify: `src/Rastreamento.Application/Execucao/Local.cs` (`Local.Perdido`)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/PerdaMapeamentoTests.cs`

**Interfaces:**
- Produces: `Posicoes.Perdido = "Perdido"` (em `Todas`, depois de `Expedido`); `Posicoes.DeOndeSeDaBaixa`
  (`IReadOnlySet<string>`, as onze da Global Constraints); `TiposDeMovimentacao.Perda = "Perda"`;
  `Movimentacao.PerdaId` (`int?`); `Local.Perdido`; entidade `Perda { Id, Motivo, Observacao, PedidoId,
  AgrupamentoId?, EstruturaItemId?, Tudo, DataHora, UsuarioId, PedidoRetrabalhoId? }`; `MotivosDePerda` com as três
  constantes e `Todos`.

- [ ] **Step 1: Escrever os testes de mapeamento** — `Perda_grava_e_le_de_volta_com_as_linhas_do_livro` (grava uma
  `Perda` e duas `Movimentacao` `Perda` apontando-a, de `AIniciar` e `NoSetor` para `Perdido`, e as lê de volta);
  `Linha_Perda_sem_PerdaId_e_recusada_pelo_banco` (`DbUpdateException`); `Perda_com_AgrupamentoId_e_EstruturaItemId_e_recusada`
  (`CK_Perda_Escopo`); `Motivo_fora_dos_tres_e_recusado`; `Estorno_com_PerdaId_e_RemessaId_e_recusado`.
- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter PerdaMapeamento`;
  falha de compilação (`Perda` não existe).
- [ ] **Step 3: Schema.** Em `02-modelo-de-dados.sql`: `dbo.Perda` como a tabela da seção 3.2 da spec (`Observacao
  NVARCHAR(500) NULL`, `Tudo BIT NOT NULL CONSTRAINT DF_Perda_Tudo DEFAULT (0)`, `DataHora` com default
  `SYSUTCDATETIME()`, FKs `FK_Perda_Pedido`, `FK_Perda_Agrupamento`, `FK_Perda_EstruturaItem`, `FK_Perda_Usuario`,
  `FK_Perda_PedidoRetrabalho`, `CK_Perda_Motivo`, `CK_Perda_Escopo`), criada **antes** de `dbo.Movimentacao` (a FK
  aponta para ela); as constraints do livro da seção 3.1 da spec; `Movimentacao.PerdaId` com `FK_Movimentacao_Perda`
  e `IX_Movimentacao_Perda` filtrado.
- [ ] **Step 4: `db/alter-fase-5b1.sql`** no formato do `alter-fase-5a.sql`: (1) `THROW 50003` se `dbo.Perda` tem a
  coluna `MotivoPerda` **e** alguma linha; (2) `DROP` da `dbo.Perda` antiga se tem `MotivoPerda` (vazia, por 1) e
  `CREATE` da nova se não existe; (3) `Movimentacao.PerdaId` + FK + índice, se não existem; (4) `DROP` se existir e
  `ADD` das constraints do livro. Aplicar duas vezes no banco de dev e conferir que a segunda não muda nada. Acrescentar
  o bloco ao `CLAUDE.md`, depois do da 5A.
- [ ] **Step 5: Entidades e mapeamento** (as do Interfaces). `Perda` mapeada para `dbo.Perda`, `PerdaId` em
  `Movimentacao`.
- [ ] **Step 6: Rodar** os testes do Step 1 e a suíte de Infrastructure inteira: verde.
- [ ] **Step 7: Commit** — `feat(fase-5b1): schema da baixa (Perdido, Perda como agrupador) e migracao`.

### Task 2: A execução desconta o perdido do pai e ignora o filho que saiu do projeto

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/{CalculadoraDeExecucao,LeitorDeEstado,ConjuntoCompleto,ApontamentoUseCase}.cs`
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Test: `tests/Rastreamento.Application.Tests/Execucao/CalculadoraComPerdaTests.cs`; Api: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs` (um teste)

**Interfaces:**
- Consumes: Task 1 (`Posicoes.Perdido`, `TiposDeMovimentacao.Perda`, `Perda`).
- Produces: construtor de `CalculadoraDeExecucao(..., IReadOnlySet<int>? setoresComKit = null,
  IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null, IReadOnlySet<int>? saidosDoProjeto = null)`;
  `decimal PerdidoDeAIniciar(int id)`; `bool SaiuDoProjeto(int id)`; `IReadOnlyList<NoDoCalculo>
  FilhosQueContam(int paiId)`; `decimal EmProducao(int id)` (soma dos saldos nas posições em produção: todas menos
  `Montado`, `Expedido`, `Perdido`). Repositório: `Task<IReadOnlyDictionary<int, decimal>>
  ListarPerdidosDeAIniciarAsync(IReadOnlyCollection<int> ids, CancellationToken ct)` e `Task<IReadOnlySet<int>>
  ListarSaidosDoProjetoAsync(IReadOnlyCollection<int> ids, CancellationToken ct)` (P3).

- [ ] **Step 1: Testes da calculadora** (dados em memória):
  - `FaltaMontar_desconta_o_perdido_de_a_iniciar` — pai 10, montado 2, perdido de "a iniciar" 4 → `FaltaMontar == 4`,
    e `Precisa(filho de razão 2) == 8 − aguardando montagem`.
  - `Perda_do_pai_depois_de_iniciado_nao_muda_o_que_falta_montar` — pai 10, montado 10, perda em `NoSetor` (não entra
    em `perdidosDeAIniciar`) → `FaltaMontar == 0`.
  - `Filho_que_saiu_do_projeto_nao_limita_a_montabilidade` — dois filhos, um em `saidosDoProjeto` com zero aguardando
    → `CalcularMontabilidade(...).DaParaMontar` limitado só pelo outro, e a lista de filhos dela sem o que saiu.
  - `Filho_que_saiu_do_projeto_nao_conta_nos_conjuntos_a_espera` — Kit; `ConjuntosAEspera` ignora o saído.
  - `EmProducao_nao_soma_montado_expedido_nem_perdido`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** P2 na calculadora; `LeitorDeEstado` lê as duas consultas novas (nove consultas, nunca uma
  por nó) e as passa; `ConjuntoCompleto` e `IniciarNoAsync` usam `FilhosQueContam` (no início, o filho que saiu não é
  travado como insuficiente nem baixado: o pai monta sem ele). Repositório: `ListarPerdidosDeAIniciarAsync` soma
  `Quantidade` das linhas `Perda` com `OrigemPosicao = AIniciar` e subtrai as dos `Estorno` com `DestinoPosicao =
  AIniciar` e `PerdaId` não nulo; `ListarSaidosDoProjetoAsync` é P3.
- [ ] **Step 4: Teste de Api** `Iniciar_pai_com_filho_descartado_por_inteiro_monta_sem_ele` — grava a `Perda` (`Tudo`,
  `Descarte`, escopo o filho) e a linha do livro direto pelo `DbContext` do cenário (a rota é da Task 5), e o
  `POST /estrutura/{pai}/inicios` passa com só o outro filho presente; a `Montagem` não tem baixa do filho descartado.
- [ ] **Step 5: Rodar** Application e Api inteiras: verde, sem regressão (os testes da Fase 3/3B continuam: sem perda,
  nada muda).
- [ ] **Step 6: Commit** — `feat(fase-5b1): o que falta montar desconta o perdido e o filho que saiu do projeto`.

### Task 3: A conta pura da baixa — `CalculoDaBaixa`

**Files:**
- Create: `src/Rastreamento.Application/Baixa/{CalculoDaBaixa,BaixaDtos,CodigosDaBaixa}.cs`
- Test: `tests/Rastreamento.Application.Tests/Baixa/CalculoDaBaixaTests.cs`

**Interfaces:**
- Consumes: Task 2 (`CalculadoraDeExecucao` com `EmProducao`, `FaltaMontar`, `Saldos`, `Filhos`).
- Produces:
  - `public sealed record OrigemDaBaixa(Local Local, decimal Quantidade);`
  - `public sealed record PedidoDeBaixa(int NoId, string Motivo, bool Tudo, IReadOnlyList<OrigemDaBaixa> Origens,
    bool IncluirTopo, IReadOnlyList<int> CadeiaAteOTopo);` (`CadeiaAteOTopo`: do pai do nó até a Peça, vazia se o nó é
    Peça)
  - `public sealed record LinhaPlanejada(int NoId, Local Origem, decimal Quantidade);`
  - `public sealed record PlanoDaBaixa(IReadOnlyList<LinhaPlanejada> Linhas, IReadOnlyList<(int NoId, decimal
    Quantidade)> ViramSobra, TravaNoTopo? Trava);` com `public sealed record TravaNoTopo(int PecaId, decimal
    Quantidade, decimal AIniciar);`
  - `public static Result<PlanoDaBaixa> CalculoDaBaixa.Planejar(CalculadoraDeExecucao calc, PedidoDeBaixa pedido,
    Func<int, string> nome)` — as recusas desta conta: `OrigemInvalida` (posição fora de `Posicoes.DeOndeSeDaBaixa`;
    `incluirTopo` em Peça ou com `Tudo`), `ItemRepetido`, `SaldoInsuficiente` (nomeando a posição por
    `rotulo` como o de `rotuloDoSaldo`), `NadaEmProducao` (o `Tudo` sem nada). Motivo e texto são validados pelo caso
    de uso (Task 5), não aqui.
  - `CodigosDaBaixa`: `MotivoInvalido`, `OrigemInvalida`, `NadaEmProducao`, `DiminuirPelaBaixa`; reusa os de
    `CodigosDaExecucao` para os outros.

O algoritmo, porque a assinatura não o determina:

```text
Tudo:  para o nó e cada descendente (ordem de Id), uma linha por posição de DeOndeSeDaBaixa com saldo > 0.
       Saldo em AguardandoLiberacao > 0 em algum nó -> falha LiberacaoEmAberto (C15).
Parte: as Origens como linhas do nó (validadas). Depois, a cascata C6, por nível, começando no nó:
         perdidoAIniciar(n) = soma das linhas de n com origem AIniciar
         para cada filho f de n (Filhos, não FilhosQueContam):
           deve(f)      = max(0, FaltaMontar(n) - perdidoAIniciar(n)) * razao(f)
           excesso(f)   = min(AIniciar(f), max(0, EmProducao(f) - deve(f)))
           se excesso(f) > 0: linha (f, AIniciar, excesso(f)); f entra no próximo nível
           sobra(f)     = max(0, EmProducao(f) - excesso(f) - deve(f));  se > 0: ViramSobra += (f, sobra(f))
IncluirTopo (só Parte de Item): aplica as linhas ao saldo e sobe CadeiaAteOTopo:
         trava = 0 no primeiro pai p acima do nó:
           unidadesSemFilho(p) = max(0, FaltaMontar(p) - floor(EmProducaoDepois(filhoNaCadeia) / razao(filhoNaCadeia)))
         sobe multiplicando: no pai seguinte q, a trava vira ceil(trava / razao(p)) ... até a Peça
         Trava = (Peça, trava, AIniciar(Peça)) se trava > 0
         Com IncluirTopo: Linhas += (Peça, AIniciar, min(trava, AIniciar(Peça))) e a cascata C6 roda de novo a partir da Peça
```

- [ ] **Step 1: Testes** (cada um com uma árvore montada à mão na calculadora):
  - `Parte_de_uma_folha_gera_so_as_linhas_pedidas`.
  - `Tudo_leva_o_no_e_os_descendentes_em_todas_as_posicoes` — Peça 10 (3 `AIniciar`, 4 `NoSetor`, 3
    `AguardandoColeta`) com filho 20 (8 `NoSetor`, 12 `AIniciar`) → cinco linhas, nada em `ViramSobra`.
  - `Parte_do_pai_tira_os_filhos_a_iniciar_alem_do_que_ele_deve` — Peça 10 com 3 `AIniciar`, 4 `NoSetor` e 3
    `AguardandoColeta` (montado 7); filho 20, razão 2, com 14 montados, 4 `AIniciar` e 2 `NoSetor`. Baixa de 3
    `AIniciar` + 1 `NoSetor` da Peça → `FaltaMontar` depois = 0, `deve(filho) = 0`; linhas: Peça `AIniciar` 3, Peça
    `NoSetor` 1, filho `AIniciar` 4; `ViramSobra = [(filho, 2)]`.
  - `Parte_do_pai_so_de_unidades_iniciadas_nao_mexe_nos_filhos` — a mesma árvore, baixa de 1 `NoSetor` da Peça: uma
    linha só, `ViramSobra` vazio.
  - `Cascata_desce_tres_niveis` (Review Focus 5).
  - `Origem_AguardandoLiberacao_e_OrigemInvalida`; `Tudo_com_AguardandoLiberacao_e_LiberacaoEmAberto`;
    `Origem_Montado_e_OrigemInvalida`; `Origem_repetida_e_ItemRepetido`; `Acima_do_saldo_e_SaldoInsuficiente_nomeando_a_posicao`
    (frase com o nome do Componente, nó sem descrição); `Tudo_sem_nada_em_producao_e_NadaEmProducao`.
  - `Perda_de_item_que_trava_um_pai_calcula_a_trava_no_topo` (dois níveis) e
    `IncluirTopo_leva_a_Peca_de_a_iniciar_e_desce_a_cascata`; `IncluirTopo_com_menos_a_iniciar_leva_o_que_ha`;
    `IncluirTopo_em_Peca_e_OrigemInvalida`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** `Planejar` com o algoritmo acima.
- [ ] **Step 4: Rodar** os testes: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): a conta pura da baixa (cascata, tudo e trava no topo)`.

### Task 4: O fechamento conta o perdido

**Files:**
- Modify: `src/Rastreamento.Application/Expedicao/{CalculoDoFechamento,FechamentoDoPedido}.cs`,
  `IExecucaoRepository.cs`, `ExecucaoRepository.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/CalculoDoFechamentoTests.cs` (casos novos)

**Interfaces:**
- Produces: `PecaNoFechamento(int PecaId, int AgrupamentoId, decimal Quantidade, decimal Expedido, decimal
  NoLocalDeExpedicao, decimal Perdido)`; `CalculoDoFechamento.Calcular(int pedidoId, string statusAtual, bool
  teveInicio, IReadOnlyList<PecaNoFechamento> pecas)`; `PosicoesDaPeca.Perdido`; repositório
  `Task<IReadOnlySet<int>> ListarPedidosComInicioAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct)`.

- [ ] **Step 1: Testes:** `Peca_conclui_com_expedido_mais_perdido`; `AguardandoExpedicao_desconta_o_perdido`;
  `Aberto_com_todas_as_Pecas_perdidas_conclui` (C11); `Aberto_sem_concluir_continua_Aberto`;
  `Agrupamento_de_Pedido_Aberto_conclui_com_as_Pecas` (C23); `Concluido_reaberto_sem_inicio_volta_a_Aberto` (C23);
  `Concluido_reaberto_com_inicio_volta_a_EmProducao`; os testes da 5A continuam (atualizar a chamada com
  `teveInicio: true` onde o status não é `Aberto`).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** P7; `FechamentoDoPedido` lê `ListarPedidosComInicioAsync` e o `Perdido` dos saldos;
  `GravarFechamentoAsync` não muda.
- [ ] **Step 4: Rodar** Application e Api: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): o fechamento conta o perdido e o Pedido Aberto conclui`.

### Task 5: Dar baixa num nó, com prévia, e a rota genérica recusando a linha

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IBaixaRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/BaixaRepository.cs`,
  `src/Rastreamento.Application/Baixa/BaixaUseCase.cs`, `src/Rastreamento.Api/Controllers/BaixaController.cs`
- Modify: `src/Rastreamento.Application/Execucao/EstornoUseCase.cs` (recusa de `PerdaId`), `src/Rastreamento.Api/Program.cs`,
  `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs`
- Test: `tests/Rastreamento.Api.Tests/BaixaEndpointsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces: `IBaixaRepository { void Adicionar(Perda p); Task<Perda?> ObterAsync(int id, CancellationToken ct);
  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasPerdasAsync(IReadOnlyCollection<int> perdaIds, CancellationToken ct);
  Task<IReadOnlyList<int>> ListarSubarvoreAsync(int noId, CancellationToken ct); Task<IReadOnlyList<int>>
  ListarCadeiaAteOTopoAsync(int noId, CancellationToken ct); }` (divide o `DbContext` com o `IExecucaoRepository`);
  DTOs `NovaBaixaDto(string? Motivo, string? Observacao, bool Tudo, IReadOnlyList<OrigemDto>? Origens, bool
  IncluirTopo)`, `OrigemDto(string Posicao, int? SetorId, int? Ordem, decimal Quantidade)`, `BaixaDto`, `LinhaDaBaixaDto`,
  `PreviaDaBaixaDto` (formas da seção "Contrato JSON novo"); `BaixaUseCase.DarBaixa(int noId, NovaBaixaDto dto, int
  usuarioId, CancellationToken ct) -> Task<Result<BaixaDto>>` e `BaixaUseCase.PreverBaixa(int noId, NovaBaixaDto dto,
  CancellationToken ct) -> Task<Result<PreviaDaBaixaDto>>`; `internal Task<IReadOnlyList<BaixaDto>>
  ProjetarAsync(IReadOnlyList<Perda>, QuemLe, CancellationToken)` (usada na Task 7).

- [ ] **Step 1: Testes de Api:**
  - `Baixa_de_parte_grava_uma_Perda_e_as_linhas_e_devolve_201` (motivo `MortaEmProcesso`, origem `NoSetor`; confere a
    `BaixaDto` e que o saldo do nó em `Perdido` é a quantidade).
  - `Baixa_de_parte_do_pai_tira_os_filhos_a_iniciar_na_mesma_Perda`.
  - `Baixa_tudo_de_uma_Peca_leva_a_arvore_e_conclui_a_Peca` (o `GET` do Pedido mostra o fechamento).
  - `Previa_devolve_o_mesmo_que_a_baixa_grava_e_nao_grava` (compara as linhas; conta as `Perda` antes e depois).
  - `IncluirTopo_grava_a_Peca_do_topo_na_mesma_Perda`.
  - Cada código da tabela de erros que esta rota emite: `MotivoInvalido`, `OrigemInvalida`, `ItemRepetido`,
    `TextoLongoDemais` (501 caracteres), `QuantidadeInvalida`, `LoteGrandeDemais`, `SaldoInsuficiente`,
    `NadaEmProducao`, `PedidoFechado` (Pedido `Cancelado`), 404, 403 para `Operador` e `Movimentador`.
  - `Estorno_generico_recusa_linha_de_baixa` (`POST /movimentacoes/{id}/estorno` → 409 `EstornoImpossivel`, frase
    "Esta linha faz parte da baixa {id}: estorne a baixa inteira.").
  - **Review Focus 1:** `Duas_baixas_simultaneas_da_mesma_posicao_uma_passa_outra_e_SaldoInsuficiente` (duas tarefas em
    paralelo sobre `NoSetor` com saldo 3, pedindo 2 cada).
  - **Review Focus 2:** `Baixa_e_inicio_do_pai_simultaneos_nao_consomem_o_mesmo_a_iniciar`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** `DarBaixa`: valida motivo, observação (aparada, ≤ 500), lote (`TamanhoMaximoDoLote` de
  `ApontamentoUseCase`) e quantidades antes da transação; dentro de `ExecutarAsync`: lê subárvore e cadeia, trava os
  nós (P5), trava o Pedido (`TravarPedidosDosNosAsync`), recusa `Cancelado` (`PedidoFechado`) — a regra do `Concluido`
  é da Task 8 —, carrega o estado, chama `CalculoDaBaixa.Planejar`, grava a `Perda` (`EstruturaItemId = noId`, `Tudo`),
  salva, grava uma `Movimentacao` `Perda` por `LinhaPlanejada` (destino `Local.Perdido`, `PerdaId`), salva e recalcula
  o fechamento. `PreverBaixa`: o mesmo sem travas e sem gravar. `BaixaController` (P1): `POST estrutura/{id}/baixas`
  (`201`), `POST estrutura/{id}/baixas/previa` (`200`). `EstornoUseCase`: a recusa por `PerdaId`, ao lado da de
  `RemessaId`. DI em `Program.cs`. `TabelaAprovada` com `BaixaController` = `Qualidade,PCP,Administrador`.
- [ ] **Step 4: Rodar** a suíte inteira com `-m:1`: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): dar baixa num no, com previa, e a rota generica recusando a linha`.

### Task 6: "Descartar tudo" do Agrupamento e do Pedido

**Files:**
- Modify: `src/Rastreamento.Application/Baixa/BaixaUseCase.cs`, `src/Rastreamento.Api/Controllers/BaixaController.cs`,
  `IBaixaRepository`/`BaixaRepository` (`ListarPecasDoEscopoAsync`)
- Test: `tests/Rastreamento.Api.Tests/BaixaEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 5.
- Produces: `BaixaUseCase.DescartarAgrupamento(int agrupamentoId, DescarteDto dto, int usuarioId, CancellationToken ct)`,
  `DescartarPedido(int pedidoId, ...)`, `PreverDescarteDoAgrupamento(int, CancellationToken)`,
  `PreverDescarteDoPedido(int, CancellationToken)`; `DescarteDto(string? Observacao)`;
  `Task<IReadOnlyList<int>> ListarPecasDoEscopoAsync(int pedidoId, int? agrupamentoId, CancellationToken ct)`.

- [ ] **Step 1: Testes:** `Descartar_tudo_do_Pedido_leva_todas_as_Pecas_numa_Perda_e_conclui_o_Pedido` (três Peças, uma
  já toda expedida, as outras em várias posições; a expedida não gera linha; o expedido continua); 
  `Descartar_tudo_do_Agrupamento_conclui_o_Agrupamento_e_nao_mexe_no_outro`;
  `Descartar_tudo_de_Pedido_Aberto_conclui_o_Pedido` (C11); `Descartar_tudo_com_aguardando_liberacao_e_LiberacaoEmAberto_nomeando_a_Peca`
  (C15; Peça do catálogo sem descrição); `Descartar_tudo_sem_nada_em_producao_e_NadaEmProducao`;
  `Previa_do_descarte_do_Pedido_bate_com_o_gravado`; **Review Focus 4:** `Pedido_pausado_aceita_baixa_e_descarte`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** P6: uma `Perda` (`Tudo = 1`, `Descarte`, `AgrupamentoId` ou nenhum), um `Planejar` com
  `Tudo` por Peça com `EmProducao` da árvore > 0, todas as linhas na mesma `Perda`; travas na ordem de `Id` de todos os
  nós das Peças. Rotas `POST agrupamentos/{id}/descarte[/previa]` e `POST pedidos/{id}/descarte[/previa]`.
- [ ] **Step 4: Rodar** a suíte: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): descartar tudo do Agrupamento e do Pedido`.

### Task 7: Estorno da baixa e as leituras

**Files:**
- Create: `src/Rastreamento.Application/Baixa/ConsultaDeBaixasUseCase.cs`
- Modify: `BaixaUseCase.cs`, `BaixaController.cs`, `IBaixaRepository`/`BaixaRepository`
  (`ListarPerdasDosNosAsync`, `ListarPerdasDoPedidoAsync`)
- Test: `tests/Rastreamento.Api.Tests/BaixaEndpointsTests.cs`

**Interfaces:**
- Produces: `BaixaUseCase.Estornar(int perdaId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct) ->
  Task<Result<IReadOnlyList<MovimentacaoDto>>>`; `ConsultaDeBaixasUseCase.DoNo(int noId, QuemLe quem, CancellationToken)`
  e `DoPedido(int pedidoId, QuemLe quem, CancellationToken)` → `Result<IReadOnlyList<BaixaDto>>`.

- [ ] **Step 1: Testes:** `Estorno_devolve_cada_linha_a_origem_e_marca_estornada` (inclusive os filhos da cascata;
  Review Focus 5 com três níveis); `Estorno_de_baixa_que_concluiu_reabre_o_Pedido`;
  `Estorno_do_descarte_de_Pedido_que_nunca_comecou_volta_a_Aberto` (C23); `Estorno_de_outro_autor_por_Qualidade_e_403`;
  `Estorno_por_PCP_de_baixa_alheia_passa`; `Estorno_em_Pedido_Cancelado_e_PedidoFechado`;
  **Review Focus 3:** `Dois_estornos_simultaneos_um_passa_outro_e_JaEstornado`;
  `Leitura_do_no_traz_baixas_dele_e_dos_descendentes_e_a_do_Pedido`; `Leitura_do_Pedido_traz_todas_mais_recente_primeiro`;
  `Estornavel_falso_para_estornada_e_para_quem_nao_e_autor_nem_PCP`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** no molde de `RemessaUseCase.Estornar` (seção 4.9 da spec: existe → 403 → `Cancelado` →
  `JaEstornado` relido depois da trava; um `Estorno` por linha, de `Local.Perdido` à origem, `PerdaId` e `EstornoDeId`;
  recalcula o fechamento). Projeção com `estornada`/`estornavel` como a da remessa. Rotas `POST baixas/{id}/estorno`
  (`200`), `GET estrutura/{id}/baixas`, `GET pedidos/{id}/baixas` (sem `Roles`).
- [ ] **Step 4: Rodar** a suíte: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): estorno inteiro da baixa e as leituras`.

### Task 8: Pedido concluído, sobra na Fila, separação e editar quantidade

**Files:**
- Modify: `BaixaUseCase.cs` (C12, C24), `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (C25),
  `ExecucaoRepository.ListarNosEmProducaoAsync` (P8), `src/Rastreamento.Application/Separacao/ConsultaDeSeparacaoUseCase.cs`
  e o resumo de material da Fila (C26), `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` (D12)
- Test: `BaixaEndpointsTests.cs`, `ExecucaoEndpointsTests.cs`, `SeparacaoEndpointsTests.cs`, `EstruturaEndpointsTests.cs`

**Interfaces:**
- Produces: `ListarNosEmProducaoAsync(bool incluirConcluidos = false, CancellationToken ct = default)` (atualizar os
  chamadores); código `DiminuirPelaBaixa` com a mensagem `"Com o Pedido em produção, a quantidade de {nome} só
  aumenta: para diminuir, use \"Dar baixa\"."`.

- [ ] **Step 1: Testes:** `Pedido_concluido_aceita_descarte_de_sobra_de_Item_em_qualquer_posicao` (C24);
  `Pedido_concluido_recusa_perda_de_Item_com_PedidoFechado`; `Pedido_concluido_recusa_baixa_de_Peca`;
  `Fila_mostra_a_sobra_de_Pedido_concluido_so_na_secao_Sobra` (C25); `Tarefas_nao_mostram_Pedido_concluido`;
  `Separador_nao_lista_no_descartado_por_inteiro` e `Separador_continua_listando_no_todo_montado_e_no_perdido_em_parte` (C26); `Editar_quantidade_para_menos_com_Pedido_rodando_e_DiminuirPelaBaixa`
  (D12; nó do catálogo sem descrição, frase com o nome do Componente); `Editar_quantidade_para_mais_com_Pedido_rodando_passa`;
  `Editar_quantidade_para_menos_com_Pedido_Aberto_passa`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** C12/C24 em `DarBaixa` e no descarte: com `Concluido`, só `Descarte` e só Item;
  `DescartarPedido` num `Concluido` planeja o `Tudo` só dos Itens com algo em produção. P8, P9 e D12 como descritos.
- [ ] **Step 4: Rodar** a suíte: verde.
- [ ] **Step 5: Commit** — `feat(fase-5b1): Pedido concluido, sobra na Fila, separacao e editar quantidade`.

### Task 9: Documentos

**Files:** `specs/00-visao-geral.md` (se citar perda), `specs/01-dominio-e-regras-de-negocio.md`,
`specs/04-fluxos-de-usuario.md`, `specs/05-api-endpoints.md`, `specs/06-roadmap-mvp.md`, `CLAUDE.md`, `README.md`.

- [ ] **Step 1:** Aplicar a seção 10 da spec, item por item: as regras 9, 13, 17, 27 e 30; a regra nova da baixa (com
  as seções 4.1 a 4.10 da spec, as decisões P2, P3, P7 e P10 deste plano onde mudam a regra); o ponto em aberto
  «Descontinuar uma Peça trava o fechamento do Pedido» sai da lista com uma nota de fechamento; os fluxos 5 e 6; as rotas
  e a tabela de erros (`POST /pecas/{id}/perdas` sai; `DiminuirPelaBaixa` no contrato da Estrutura); no `06`, a 5B
  partida em 5B-1 e 5B-2 e a 5C sem a perda do próprio pai (C21); no `CLAUDE.md`, a ordem de implementação, o invariante
  da regra 9 com o perdido e os invariantes de "registro composto" com a baixa.
- [ ] **Step 2:** Remedir as contagens de teste do `CLAUDE.md` executando a suíte, e escrever data, commit e métrica.
- [ ] **Step 3: Commit** — `docs(fase-5b1): specs, roadmap e CLAUDE.md com a baixa`.

## Self-review (feito ao escrever este plano)

- **Cobertura da spec:** seção 3 → Task 1; 4.1–4.3 e 4.6–4.7 → Task 3 (conta) e 5 (rota); 4.4–4.5 → Task 2; 4.8 → Task 4
  e 8; 4.9 → Task 7; 4.10 → Task 8; 5.1–5.4 → Tasks 5–8; C25/C26 → Task 8; seção 10 → Task 9. Seção 6 (front) → plano 2.
  O "matar" (4.7, C16) é a rota da Task 5 com origem `NaoConforme`: o teste `Baixa_de_parte_...` cobre a rota, e o
  plano 2 cobre a tela.
- **Tipos:** `PecaNoFechamento` e `Calcular` mudam de assinatura na Task 4, e os chamadores são só `FechamentoDoPedido`
  e os testes. `ListarNosEmProducaoAsync` muda na Task 8 com parâmetro opcional.
- **Proporção:** o algoritmo da Task 3 é o único bloco de lógica; o resto é assinatura e nome de teste.
