# Fase 5B-2 — Reposição — Plano 1 de 2 (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao backend o Pedido de Retrabalho (aberto da baixa, da reclamação ou avulso, como cópia da Peça), o nó
pronto pelo "Usar a sobra", a reclamação do cliente, o "não repor" e a contagem do que falta repor.

**Architecture:** Dois tipos novos no livro (`Pronto`, `Devolucao`), um motivo novo de baixa (`Reaproveitado`) e três
registros novos (`dbo.Reclamacao`, `dbo.DispensaDeReposicao`, colunas de origem em `EstruturaItem` e `Perda`). Uma
pasta nova `Application/Retrabalho/` reúne a cópia pura da Peça (`CopiaDaPeca`), a conta pura do reaproveitamento
(`CalculoDoReaproveitamento`) e os casos de uso (`RetrabalhoUseCase`, `ReaproveitamentoUseCase`, `ReclamacaoUseCase`,
`DispensaUseCase`, `ConsultaDeReposicao`). A calculadora da execução passa a contar o pronto do pai como montado (C17)
e a aceitar o conjunto em formação do Kit (D8). Toda escrita corre na transação SERIALIZABLE de `ExecutarAsync`.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-05-fase-5b2-reposicao-design.md`, aprovada em 2026-11-06 (D1–D9 e A4 do
usuário, C1–C20 do desenho). Branch: `claude/stoic-faraday-cg985a`, sobre `d86e7dc` (os planos foram escritos na `claude/eloquent-mendel-7s7ocs`, que não chegou ao GitHub). O plano 2 (front,
`docs/superpowers/plans/2026-11-06-fase-5b2-front.md`) consome a seção "Contrato JSON novo" daqui. **Antes da Task 1,
a spec e os dois planos têm de estar publicados na branch do repositório do código** (a sessão que os escreveu só
tinha leitura dele; cópia da spec no ledger, `sdd/2026-11-05-fase-5b2/spec-rascunho.md`).

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de
  Critical/Important → re-review). Base de cada task registrada **antes** de despachar o implementer; pacote de review
  com `scripts/review-package BASE HEAD`. Ledger do plano em `.superpowers/sdd/2026-11-06-fase-5b2/`, artefatos
  `backend-task-N-…`.
- **A review de branch inteira só depois da conferência do usuário no navegador** (última task do plano 2).
- **Schema pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-fase-5b2.sql`
  idempotente (formato do `db/alter-fase-5b1.sql`, `-b -f 65001`), depois o mapeamento EF. Nada de `Add-Migration`.
- **O livro continua só de inclusão.** `Reclamacao` e `DispensaDeReposicao` se estornam marcando `EstornadaEm` e
  `EstornadaPorUsuarioId` (molde de `Montagem.EstornadaEm`); nenhum outro `UPDATE`, nenhum `DELETE`.
- **Motivos de Retrabalho, exatos:** `ReprovacaoDimensional`, `ErroInterno`, `SolicitacaoCliente`, `Perda`.
  **Motivos de baixa:** os três da 5B-1 mais `Reaproveitado`, que **só** nasce pelo "Usar a sobra" (o "Dar baixa"
  o recusa com `MotivoInvalido`). Destinos da reclamação: `Consertar`, `Refazer`.
- **Perfis, exatos:** abrir Retrabalho e registrar/estornar reclamação `Qualidade,PCP,Administrador`; "Usar a sobra",
  "não repor" e desfazê-lo `PCP,Administrador`; o estorno da reclamação de outro autor exige PCP/Administrador (403
  `Proibido`, decidido no caso de uso). Leituras: qualquer autenticado.
- **"A repor" conta unidades de Peça** (C1, seção 4.1 da spec); o servidor calcula, a tela não.
- Recusa que nomeia nó usa `EstadoDeExecucao.Nome` (regra 19). Todo cenário com frase de nome tem **um nó copiado do
  catálogo, sem descrição própria**. Data de entrega de teste que não deve atrasar: `"2099-12-31"`.
- Nomes de domínio em português; técnicos em inglês. Citação por nome, nunca `arquivo.ext:NN`. Comentário não cita
  ledger, brief nem "Task N"; decisões como "D*n*/C*n* da spec da Fase 5B-2" ou "decisão P*n* do plano de backend da
  Fase 5B-2". Mensagens de erro **com** acento; comentários sem acento.
- `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; `dotnet test Rastreamento.slnx -m:1`. Teste de
  banco escopa a asserção nas linhas que inseriu.
- `git pull` antes de todo commit, nos dois repositórios.

## Review Focus

1. **Duas aberturas simultâneas da mesma baixa** — uma `201`, a outra `409 JaReposto`; nunca dois Retrabalhos ligados
   à mesma baixa (Task 5).
2. **"Não repor" e "Abrir retrabalho" ao mesmo tempo no mesmo alvo** — um ganha; o outro `409` (`JaReposto` ou
   `ReposicaoDispensada`) (Task 6).
3. **"Usar a sobra" enquanto alguém descarta a mesma sobra no original** — uma escrita passa, a outra `409`
   (`SaldoInsuficiente` ou `AlemDaSobra`); nunca saldo negativo (Task 8).
4. **Roteiro do nó do Retrabalho editado antes do "Usar a sobra"** — a posição da sobra já não existe nele: `400
   PosicaoDoProntoInexistente`, nomeando o nó; o nó acrescentado à mão (sem origem) não aparece na prévia (Task 8).
5. **Reclamação no limite do expedido, em Pedido `Concluido` de várias remessas** — igual ao expedido passa; a seguinte
   `409 SaldoInsuficiente`; estornar uma remessa que deixaria o expedido abaixo do reclamado `409 AbaixoDoReclamado`
   (Tasks 4 e 9).

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem         # na nuvem; na máquina local: docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
```

**Baseline** (medida em `5c1dbad`, 2026-11-05): backend **1.887** (Api 486 · Application 1.086 · Infrastructure 315).
O pré-flight remede na base da Task 1 e escreve no ledger; cada implementer reporta o **delta** da própria task.

## Decisões deste plano (onde a spec deixou a escolha)

- **P1 — Quatro controllers novos, um por conjunto de perfis** (a guarda de `permissoesEspelhamOBackend.test.ts`
  compara um recurso com UM arquivo): `RetrabalhoController` (abrir; leitura da lista), `ReaproveitamentoController`
  (prévia e "Usar a sobra"), `ReclamacaoController` (registrar, listar, estornar), `DispensaController` (não repor e
  desfazer). `PerfisDeEscritaDeclaradosTests.TabelaAprovada` ganha as linhas.
- **P2 — A cascata do pronto é uma baixa à parte, ligada à do reaproveitamento.** Os filhos a iniciar que o pronto
  dispensa (C18) saem numa segunda `Perda`, no **Retrabalho**, motivo `Descarte`, `Tudo = 0`, com a coluna nova
  `Perda.PerdaDeOrigemId` apontando a baixa `Reaproveitado` (que é do original: uma `Perda` é de um Pedido só). O
  estorno da `Reaproveitado` estorna a ligada; o estorno direto da ligada é `409 EstornoImpossivel`.
- **P3 — O pronto entra na calculadora por uma entrada nova**: `IReadOnlyDictionary<int, decimal>? prontos` (o líquido
  das linhas `Pronto`, menos os estornos) e `IReadOnlySet<int>? comProntoEmMontagem` (filhos com `Pronto` líquido cujo
  destino é `AguardandoMontagem`). `TotalMontado(id)` = montagens + `Pronto(id)` **quando o nó tem filhos** (C17).
- **P4 — O conjunto em formação (D8) vale só para o pai que recebe em conjunto e tem filho em `comProntoEmMontagem`.**
  `ConjuntosEmFormacao(pai) = ConjuntosAEspera(pai) − min por filho de ⌊EmEsperaNoKit(filho) ÷ razão⌋`;
  `ComplementoDoConjunto(pai, filho) = max(0, ConjuntosAEspera(pai) × razão − EmEsperaNoKit(filho))` quando há conjunto
  em formação, senão 0. O Pedido de fabricação nunca tem `Pronto`, então nada muda para ele.
- **P5 — A abertura trava o Pedido original** (`TravarPedidoAsync`) e relê o alvo depois da trava: é o que serializa
  duas aberturas, a abertura e o "não repor", e a reclamação e o estorno da remessa.
- **P6 — O "Usar a sobra" trava, em ordem crescente de `Id`, a união dos nós da árvore da Peça de origem e dos nós do
  Retrabalho, e depois os dois Pedidos**, em ordem de `Id`. O estorno da `Reaproveitado` trava o mesmo.
- **P7 — O número sugerido vem do servidor** na leitura da lista de Retrabalhos (`numeroSugerido`): `<número>-R{k}`,
  o menor k ≥ 1 sem Pedido com esse número.
- **P8 — A sobra viva por posição** (C19): em `AguardandoColeta(s, o)`, `SobraDaColeta(no, s, o)`; em
  `AguardandoMontagem`, `ExcessoEmMontagem(no)` repartido pelos Setores em ordem de `Id`, até o saldo de cada um.
- **P9 — `ListarPedidosComInicioAsync` conta também a `Devolucao`** líquida (C13): o Retrabalho de consertar tem início.
  O `Pronto` não conta (C11).

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-fase-5b2.sql`, `CLAUDE.md` (bloco do alter); `Domain/Entities/{Posicoes,Movimentacao,Perda,EstruturaItem}.cs`, `Domain/Entities/{Reclamacao,DispensaDeReposicao}.cs`, as configurações EF e `RastreamentoDbContext.cs` | 1 | schema e mapeamento |
| `Application/Execucao/{CalculadoraDeExecucao,LeitorDeEstado,ConjuntoCompleto}.cs`, `IExecucaoRepository`/`ExecucaoRepository` | 2 | C17, D8, P9 |
| `Application/Retrabalho/{CopiaDaPeca,RetrabalhoDtos,CodigosDoRetrabalho}.cs` | 3 | a cópia pura |
| `Application/Retrabalho/ReclamacaoUseCase.cs`, `Api/Controllers/ReclamacaoController.cs`, `IReposicaoRepository`/`ReposicaoRepository` | 4 | reclamação |
| `Application/Retrabalho/RetrabalhoUseCase.cs`, `Api/Controllers/RetrabalhoController.cs`, `IEstruturaRepository.NoParaGravar` | 5 | abrir (três portas) |
| `Application/Retrabalho/{DispensaUseCase,ConsultaDeReposicao}.cs`, `Api/Controllers/DispensaController.cs`, `Cadastros/{Dtos,CadastroDePedidoUseCase}.cs`, `Baixa/{BaixaDtos,BaixaUseCase}.cs` | 6 | não repor e "a repor" |
| `Application/Retrabalho/CalculoDoReaproveitamento.cs`, `Baixa/CalculoDaBaixa.cs` | 7 | a conta pura da sobra |
| `Application/Retrabalho/ReaproveitamentoUseCase.cs`, `Api/Controllers/ReaproveitamentoController.cs`, `Baixa/BaixaUseCase.Estorno.cs` | 8 | usar a sobra e estorná-la |
| `Baixa/BaixaUseCase{,.Estorno}.cs`, `Execucao/EstornoUseCase.cs`, `Expedicao/RemessaUseCase.cs` | 9 | guardas de estorno (C20) |
| `specs/00`, `01`, `04`, `05`, `06`, errata da spec do Kit, `CLAUDE.md`, `README.md` | 10 | documentos |

## Contrato JSON novo

```jsonc
// POST /api/pedidos/{id}/retrabalhos   ({id} = o original)  -> 201 PedidoDto (o Retrabalho)
{ "perdaId": 7 | null, "reclamacaoId": 3 | null,            // no máximo um; nenhum = avulsa
  "estruturaItemId": 812 | null, "quantidade": 2 | null,    // só na avulsa (Peça do original)
  "destino": "Consertar" | "Refazer" | null, "setorId": 4 | null,   // destino só da reclamação; setor só no Consertar
  "numero": "P-1020-R1", "dataEntrega": "2026-12-01", "motivoRetrabalho": "Perda" }

// GET /api/pedidos/{id}/retrabalhos -> 200
{ "numeroSugerido": "P-1020-R2",
  "retrabalhos": [ { "id": 51, "numero": "P-1020-R1", "motivoRetrabalho": "Perda", "status": "Aberto",
                     "dataEntrega": "2026-12-01", "origem": { "tipo": "Baixa" | "Reclamacao" | "Avulsa", "id": 7 | null } } ] }

// PedidoDto ganha (no fim, com default):
"pedidoOrigem": { "id": 40, "numero": "P-1020" } | null, "motivoRetrabalho": "Perda" | null, "aRepor": 0

// BaixaDto ganha: "pedidoRetrabalho": { "id": 51, "numero": "P-1020-R1" } | null,
//                 "dispensa": DispensaDto | null, "aRepor": 0      // unidades de Peça (C1)
// DispensaDto: { "id": 2, "justificativa": "...", "dataHora": "...", "usuarioId": 3, "usuarioNome": "..." }

// POST /api/pedidos/{id}/reclamacoes  { "estruturaItemId": 812, "quantidade": 1, "descricao": "..." } -> 201 ReclamacaoDto
// GET  /api/pedidos/{id}/reclamacoes -> 200 ReclamacaoDto[]  (mais recente primeiro)
// POST /api/reclamacoes/{id}/estorno -> 200 ReclamacaoDto
// ReclamacaoDto: { "id", "pecaId", "descricao" /*nome da Peça*/, "codigo", "quantidade", "texto" /*do cliente*/,
//                  "dataHora", "usuarioId", "usuarioNome", "estornada", "estornavel",
//                  "pedidoRetrabalho": {...} | null, "dispensa": DispensaDto | null, "aRepor": 0 }

// POST /api/baixas/{id}/dispensa e POST /api/reclamacoes/{id}/dispensa  { "justificativa": "..." } -> 201 DispensaDto
// POST /api/dispensas/{id}/estorno -> 204

// GET /api/pedidos/{id}/sobra-reaproveitavel   ({id} = o Retrabalho) -> 200
{ "itens": [ { "estruturaItemId": 77, "descricao": "...", "codigo": "...", "posicao": "AguardandoColeta",
               "setorId": 3, "setorNome": "Dobra", "ordem": 2, "sobra": 2, "precisa": 4, "sugerido": 2 } ] }
// POST /api/pedidos/{id}/sobra-reaproveitada
{ "itens": [ { "estruturaItemId": 77, "posicao": "AguardandoColeta", "setorId": 3, "ordem": 2, "quantidade": 2 } ] }
//  -> 201 BaixaDto (a baixa `Reaproveitado`, no original)
```

Erros novos (`CodigosDoRetrabalho`): `JaReposto`, `ReposicaoDispensada`, `SemPecaARepor`, `ReposicaoResolvida`,
`AbaixoDoReclamado`, `AlemDaSobra`, `ProntoJaAndou` (409); `PosicaoDoProntoInexistente`, `DestinoInvalido`,
`PortaInvalida` (400); `RetrabalhoForaDeAberto` (409). Reusados: `SaldoInsuficiente`, `NaoEhPeca`, `PedidoFechado`,
`JaEstornado`, `EstornoImpossivel`, `TextoLongoDemais`, `MotivoInvalido`, `SetorInvalido`, `AlemDoQueOPaiPrecisa`,
`JustificativaObrigatoria` (o da liberação, `CodigosDaExpedicao`), `Proibido` (403). Número repetido: o 409 que o
`CadastroDePedidoUseCase` já devolve.

---

### Task 1: Schema, migração e mapeamento

**Files:** os da linha 1 do mapa. Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ReposicaoMapeamentoTests.cs`.

**Interfaces:**
- Produces: `TiposDeMovimentacao.Pronto = "Pronto"`, `TiposDeMovimentacao.Devolucao = "Devolucao"`;
  `MotivosDePerda.Reaproveitado = "Reaproveitado"`, `MotivosDePerda.Todos` (os quatro) e `MotivosDePerda.DaBaixa` (os
  três que o "Dar baixa" aceita); `Movimentacao.ReclamacaoId` (`int?`); `EstruturaItem.OrigemEstruturaItemId` (`int?`);
  `Perda.PerdaDeOrigemId` (`int?`); entidades `Reclamacao { Id, PedidoId, EstruturaItemId, Quantidade, Descricao,
  PedidoRetrabalhoId?, DataHora, UsuarioId, EstornadaEm?, EstornadaPorUsuarioId? }` e `DispensaDeReposicao { Id,
  PerdaId?, ReclamacaoId?, Justificativa, DataHora, UsuarioId, EstornadaEm?, EstornadaPorUsuarioId? }`;
  `DestinosDaReclamacao.Consertar`/`Refazer`; `MotivosDeRetrabalho` com as quatro constantes e `Todos`. `DbSet`s
  `Reclamacoes` e `DispensasDeReposicao`.

- [ ] **Step 1: Testes de mapeamento:** `Reclamacao_grava_e_le_de_volta`; `Linha_Devolucao_sem_ReclamacaoId_e_recusada`;
  `Linha_Pronto_sem_PerdaId_e_recusada`; `Perda_Reaproveitado_sem_PedidoRetrabalhoId_e_recusada`
  (`CK_Perda_ReaproveitadoTemRetrabalho`); `Perda_Descarte_com_PedidoRetrabalhoId_e_recusada`
  (`CK_Perda_DescarteSemRetrabalho`); `Dispensa_com_os_dois_alvos_e_recusada` (`CK_DispensaDeReposicao_Alvo`);
  `Duas_dispensas_vigentes_da_mesma_baixa_sao_recusadas` (índice único filtrado) e `Dispensa_estornada_nao_impede_outra`.
- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter ReposicaoMapeamento`.
- [ ] **Step 3: Schema** em `02-modelo-de-dados.sql`, seções 3.1 a 3.5 da spec mais `Perda.PerdaDeOrigemId` (FK para
  `dbo.Perda`, P2): `dbo.Reclamacao` (`CK_Reclamacao_Quantidade > 0`, `Descricao NVARCHAR(500) NOT NULL`, FKs para
  Pedido, EstruturaItem, Usuario ×2 e `FK_Reclamacao_PedidoRetrabalho`), criada antes de `dbo.Movimentacao`;
  `dbo.DispensaDeReposicao` com `UX_DispensaDeReposicao_PerdaVigente` e `UX_DispensaDeReposicao_ReclamacaoVigente`
  (únicos, `WHERE … IS NOT NULL AND EstornadaEm IS NULL`); `Movimentacao.ReclamacaoId` + `FK_Movimentacao_Reclamacao` +
  `IX_Movimentacao_Reclamacao` filtrado; `CK_Movimentacao_Tipo` e `CK_Movimentacao_RegistroCoerente` como a seção 3.1
  da spec; `CK_Perda_Motivo` com os quatro.
- [ ] **Step 4: `db/alter-fase-5b2.sql`** no formato do `alter-fase-5b1.sql` (tabelas e colunas se não existem;
  constraints `DROP` se existir e `ADD`, sempre). Aplicar duas vezes no banco de dev: a segunda não muda nada. Bloco
  no `CLAUDE.md`, depois do da 5B-1.
- [ ] **Step 5: Entidades e mapeamento** (as do Interfaces).
- [ ] **Step 6: Rodar** os testes do Step 1 e a Infrastructure inteira: verde.
- [ ] **Step 7: Commit** — `feat(fase-5b2): schema da reposicao (Pronto, Devolucao, Reclamacao, Dispensa) e migracao`.

### Task 2: A calculadora conta o pronto como montado e aceita o conjunto em formação

**Files:** a linha 2 do mapa. Test: `tests/Rastreamento.Application.Tests/Execucao/CalculadoraComProntoTests.cs`,
`tests/Rastreamento.Application.Tests/Execucao/ConjuntoCompletoComProntoTests.cs`; Infra: um teste em
`ExecucaoRepositoryTests` para `ListarProntosAsync` e um para P9.

**Interfaces:**
- Consumes: Task 1.
- Produces: construtor de `CalculadoraDeExecucao(..., IReadOnlySet<int>? saidosDoProjeto = null,
  IReadOnlyDictionary<int, decimal>? prontos = null, IReadOnlySet<int>? comProntoEmMontagem = null)`;
  `decimal Pronto(int id)`; `decimal ConjuntosEmFormacao(int paiId)`; `decimal ComplementoDoConjunto(int paiId, int
  filhoId)` (P3, P4). Repositório: `Task<IReadOnlyList<(int EstruturaItemId, string DestinoPosicao, decimal
  Quantidade)>> ListarProntosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)` (líquido de estorno).

- [ ] **Step 1: Testes da calculadora** (em memória):
  - `Pronto_de_no_com_filhos_soma_ao_total_montado` — pai 3, pronto 1 → `TotalMontado == 1`, `FaltaMontar == 2`,
    `Precisa(filho de razão 4) == 8 − aguardando montagem`.
  - `Pronto_de_folha_nao_mexe_no_total_montado`.
  - `Pronto_em_montagem_de_Kit_forma_conjunto_em_formacao` — pai Kit de 1 na Solda (`UtilizaKit`), filhos A e B de
    razão 1, A pronto em `AguardandoMontagem(Solda)` → `ConjuntosEmFormacao == 1`, `ComplementoDoConjunto(pai, B) == 1`,
    `ComplementoDoConjunto(pai, A) == 0`.
  - `Sem_pronto_nao_ha_conjunto_em_formacao` — mesmo estado, A chegou por `Entrega` (sem `comProntoEmMontagem`) →
    `ConjuntosEmFormacao == 0` (o caso da 5C não muda).
  - `ColetasPendentes_oferece_o_complemento_e_o_cartao_do_Kit_nao_conta_ele` — B aguardando coleta no último passo →
    uma `ColetaPendente` de B com `Tarefa == 1`, e `KitsDaColeta` sem B pronto.
- [ ] **Step 2: Testes do `ConjuntoCompleto`:** `Entrega_que_completa_o_conjunto_em_formacao_passa` (só B, 1 →
  `null`); `Entrega_alem_do_complemento_exige_conjunto_completo` (B 2 com pai de 2 e só A pronto em 1 → resto 1 sem A:
  `ConjuntoIncompleto`); `Pedido_de_fabricacao_continua_exigindo_o_conjunto` (sem pronto: só B → `ConjuntoIncompleto`).
- [ ] **Step 3: Rodar e ver falhar.**
- [ ] **Step 4: Implementar** P3 e P4. `ConjuntoCompleto.Conferir`: por grupo, tira primeiro de cada filho
  `min(soma, ComplementoDoConjunto)`; se o resto é zero em todos, o grupo passa; senão a regra de hoje vale sobre o
  resto. `ColetasPendentes`/`KitsDaColeta` como o último teste do Step 1. `LeitorDeEstado` lê `ListarProntosAsync` (uma
  consulta a mais, nunca uma por nó) e monta `prontos` e `comProntoEmMontagem`. `ListarPedidosComInicioAsync` aceita
  `Devolucao` líquida (P9).
- [ ] **Step 5: Rodar** Application, Infrastructure e Api inteiras: verde, sem regressão da 3B e da 5B-1.
- [ ] **Step 6: Commit** — `feat(fase-5b2): o pronto conta como montado e completa o conjunto do Kit`.

### Task 3: A cópia pura da Peça — `CopiaDaPeca`

**Files:** `src/Rastreamento.Application/Retrabalho/{CopiaDaPeca,RetrabalhoDtos,CodigosDoRetrabalho}.cs`,
`src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs` (`NoParaGravar` ganha `int? OrigemEstruturaItemId =
null`, gravado por `GravarArvoreAsync`). Test: `tests/Rastreamento.Application.Tests/Retrabalho/CopiaDaPecaTests.cs`.

**Interfaces:**
- Produces: `public sealed record NoDaCopia(int Id, int? PaiId, int? ComponenteId, string? Descricao, decimal
  Quantidade, decimal? QuantidadePorPai, bool RequerRelatorioDimensional, IReadOnlyList<(int MaterialId, decimal
  Quantidade)> Materiais, IReadOnlyList<(int SetorId, int Ordem)> Roteiro);`
  `public static Result<NoParaGravar> CopiaDaPeca.Planejar(IReadOnlyList<NoDaCopia> arvore, int pecaId, decimal
  quantidade, IReadOnlySet<int> saidosDoProjeto, bool soAPeca)`; `CodigosDoRetrabalho` com os códigos do contrato.

- [ ] **Step 1: Testes:** `Copia_escala_cada_no_pela_razao_ate_a_Peca` (Peça 10 → 1: filho de razão 4 vira 4, neto de
  razão 2 vira 8); `Materiais_se_reescalam_pela_mesma_razao` (material 5 num nó de 40 que vira 4 → 0,5);
  `Copia_guarda_a_origem_de_cada_no`; `No_que_saiu_do_projeto_nao_entra_com_a_arvore_abaixo`;
  `Filho_sem_QuantidadePorPai_usa_a_razao_das_quantidades` (filho 20 de pai 10 → razão 2);
  `So_a_Peca_copia_sem_filhos_e_sem_materiais` (consertar, seção 4.7 da spec; Roteiro copiado);
  `Quantidade_fora_da_faixa_da_coluna_e_recusada` (400, a mesma guarda de `PlanejadorDeCopia`);
  `Mais_nos_que_o_teto_e_recusado` (`PlanejadorDeCopia.NosMaximos`).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** A Peça e cada nó copiam `ComponenteId`, `Descricao`, `QuantidadePorPai`, `Roteiro`; só a
  Peça copia `RequerRelatorioDimensional`. Quantidade do filho = quantidade nova do pai × razão; material = quantidade ×
  (quantidade nova do nó ÷ quantidade original do nó), arredondado como a coluna (4 casas).
- [ ] **Step 4: Rodar:** verde. **Step 5: Commit** — `feat(fase-5b2): copia pura da Peca para o Retrabalho`.

### Task 4: A reclamação do cliente

**Files:** `Application/Retrabalho/ReclamacaoUseCase.cs`, `Api/Controllers/ReclamacaoController.cs`,
`Domain/Abstractions/IReposicaoRepository.cs`, `Infrastructure/Persistence/ReposicaoRepository.cs`, `Api/Program.cs`.
Test: `tests/Rastreamento.Api.Tests/ReclamacaoEndpointsTests.cs`.

**Interfaces:**
- Produces: `ReclamacaoUseCase.Registrar(int pedidoId, NovaReclamacaoDto dto, int usuarioId, CancellationToken ct) ->
  Task<Result<ReclamacaoDto>>`, `Listar(int pedidoId, QuemLe quem, CancellationToken ct) ->
  Task<Result<IReadOnlyList<ReclamacaoDto>>>`, `Estornar(int id, int usuarioId, bool podeEstornarDeOutros,
  CancellationToken ct) -> Task<Result<ReclamacaoDto>>`; `IReposicaoRepository` com `Adicionar(Reclamacao)`,
  `ObterReclamacaoAsync`, `ListarReclamacoesAsync(int pedidoId)`, `SomarReclamadoAsync(int pecaId)` (não estornadas),
  `Adicionar(DispensaDeReposicao)`, `ObterDispensaAsync`, `ListarDispensasVigentesAsync(perdaIds, reclamacaoIds)`. O
  `ReclamacaoDto` do contrato, com `PedidoRetrabalho`, `Dispensa` e `ARepor` (preenchidos pela Task 6; aqui nulos e
  `ARepor = Quantidade` quando vigente).

- [ ] **Step 1: Testes de Api:** `Registrar_reclamacao_de_Peca_expedida_de_Pedido_concluido` (201; o Pedido continua
  `Concluido`, o livro não ganha linha); `Reclamacao_alem_do_expedido_e_409_SaldoInsuficiente` e o caso do Review
  Focus 5 (igual ao expedido passa, a seguinte recusa); `Reclamacao_de_Item_e_400_NaoEhPeca`;
  `Descricao_vazia_e_400` e `Descricao_de_501_e_400_TextoLongoDemais`; `Operador_e_403`;
  `Estornar_a_propria_reclamacao` e `Estornar_a_de_outro_sem_ser_PCP_e_403_Proibido`; `Estornar_duas_vezes_e_409_JaEstornado`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Registrar trava o Pedido (P5) e relê o expedido (a soma das linhas `Expedicao` líquidas
  da Peça) depois da trava. O estorno recusado com Retrabalho ou dispensa vigente é da Task 6 (ela tem o dado).
- [ ] **Step 4: Rodar:** verde. **Step 5: Commit** — `feat(fase-5b2): reclamacao do cliente registrada no original`.

### Task 5: Abrir o Retrabalho — as três portas

**Files:** `Application/Retrabalho/RetrabalhoUseCase.cs`, `Api/Controllers/RetrabalhoController.cs`, `Api/Program.cs`,
`Infrastructure/Persistence/{EstruturaRepository,ReposicaoRepository,PedidoRepository}.cs`. Test:
`tests/Rastreamento.Api.Tests/RetrabalhoEndpointsTests.cs`, `PerfisDeEscritaDeclaradosTests.TabelaAprovada`.

**Interfaces:**
- Consumes: Tasks 1–4 (`CopiaDaPeca.Planejar`, `Reclamacao`, `MarcarPedidoEmProducaoAsync` existente).
- Produces: `RetrabalhoUseCase.Abrir(int pedidoOrigemId, NovoRetrabalhoDto dto, int usuarioId, CancellationToken ct)
  -> Task<Result<PedidoDto>>`; `Listar(int pedidoId, CancellationToken ct) -> Task<Result<RetrabalhosDoPedidoDto>>`;
  `RetrabalhosDoPedidoDto(string NumeroSugerido, IReadOnlyList<RetrabalhoResumoDto> Retrabalhos)`.

- [ ] **Step 1: Testes de Api:**
  - `Abrir_da_baixa_copia_a_Peca_para_as_unidades_perdidas` — baixa de 1 Item com `incluirTopo` (1 Peça) → 201; o
    Retrabalho `Tipo = Retrabalho`, `PedidoOrigemId`, motivo, `Aberto`, um Agrupamento com o nome e o Tipo do
    original, a árvore escalada para 1, cada nó com `OrigemEstruturaItemId`; `Perda.PedidoRetrabalhoId` preenchido.
  - `Abrir_da_baixa_sem_Peca_e_409_SemPecaARepor` (perda de Item sem `incluirTopo`); `…_de_Descarte_e_409_SemPecaARepor`;
    `…_estornada_e_409_SemPecaARepor`; `Abrir_duas_vezes_da_mesma_baixa_e_409_JaReposto` e a corrida do Review Focus 1
    (duas em paralelo).
  - `Abrir_da_reclamacao_para_consertar` — o Retrabalho tem só a Peça, `Devolucao` de N para
    `AguardandoConserto(Setor)`, status `EmProducao`; a Fila do Setor e as Tarefas mostram o conserto como o da 5A.
  - `Abrir_da_reclamacao_para_refazer` — cópia inteira, a iniciar. `Consertar_sem_setor_e_400_DestinoInvalido`;
    `Reclamacao_sem_destino_e_400_DestinoInvalido`; `Setor_inativo_e_400_SetorInvalido`.
  - `Abrir_avulso_com_motivo_ErroInterno`; `Avulso_com_Item_e_400_NaoEhPeca`; `Duas_portas_no_corpo_e_400_PortaInvalida`.
  - `Numero_repetido_e_409` (o código do cadastro); `Motivo_fora_dos_quatro_e_400_MotivoInvalido`;
    `Operador_e_403`; `Listar_traz_o_numero_sugerido_R1_e_depois_R2` (P7).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** a seção 4.2 da spec com P5: valida o corpo antes da transação; dentro dela, trava o
  original, relê o alvo, carrega a árvore da Peça (`NoDaCopia`, com `ListarSaidosDoProjetoAsync`), cria Pedido e
  Agrupamento, `GravarArvoreAsync`, grava o vínculo e, no consertar, a `Devolucao` (`ReclamacaoId`) e
  `MarcarPedidoEmProducaoAsync`; recalcula o fechamento do Retrabalho. Rotas no `RetrabalhoController`
  (`Roles = "Qualidade,PCP,Administrador"` na escrita).
- [ ] **Step 4: Rodar** Api e a guarda de perfis: verde. **Step 5: Commit** — `feat(fase-5b2): abrir o Pedido de Retrabalho`.

### Task 6: Não repor, e o que falta repor nos DTOs

**Files:** `Application/Retrabalho/{DispensaUseCase,ConsultaDeReposicao}.cs`, `Api/Controllers/DispensaController.cs`,
`Application/Cadastros/{Dtos,CadastroDePedidoUseCase}.cs`, `Application/Baixa/{BaixaDtos,BaixaUseCase}.cs`
(`ProjetarAsync`), `ReclamacaoUseCase` (estorno e projeção). Test: `tests/Rastreamento.Api.Tests/DispensaEndpointsTests.cs`,
`tests/Rastreamento.Application.Tests/Retrabalho/ConsultaDeReposicaoTests.cs`.

**Interfaces:**
- Produces: `DispensaUseCase.DispensarBaixa(int perdaId, NovaDispensaDto dto, int usuarioId, CancellationToken ct)`,
  `DispensarReclamacao(int reclamacaoId, …)` → `Task<Result<DispensaDto>>`; `Estornar(int dispensaId, int usuarioId,
  CancellationToken ct) -> Task<Result>`; `ConsultaDeReposicao.ARepor(IReadOnlyCollection<int> pedidoIds,
  CancellationToken ct) -> Task<IReadOnlyDictionary<int, decimal>>` (seção 4.1 da spec, em lote) e
  `PorBaixa`/`PorReclamacao` (os campos `PedidoRetrabalho`, `Dispensa`, `ARepor` de cada linha).

- [ ] **Step 1: Testes:** `Nao_repor_tira_a_baixa_do_a_repor` (o `aRepor` do Pedido cai de 1 para 0 em `GET
  /pedidos/{id}` e na lista); `Desfazer_o_nao_repor_devolve_o_a_repor`; `Justificativa_vazia_e_400_JustificativaObrigatoria`;
  `Nao_repor_baixa_ja_reposta_e_409_JaReposto`; `Abrir_retrabalho_de_baixa_dispensada_e_409_ReposicaoDispensada`; a
  corrida do Review Focus 2; `Qualidade_nao_repor_e_403`; `Estornar_reclamacao_com_retrabalho_e_409_ReposicaoResolvida`;
  `A_repor_soma_baixas_e_reclamacoes_em_unidades_de_Peca` (conta pura: uma baixa de Item que levou 2 Peças + uma
  reclamação de 1 → 3; a perda de Item sem Peça → 0; `Descarte` → 0; `Reaproveitado` → 0).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** As escritas travam o Pedido do alvo (P5). `PedidoDto` ganha, no fim e com default,
  `PedidoOrigemResumoDto? PedidoOrigem`, `string? MotivoRetrabalho`, `decimal ARepor`, preenchidos em `Obter` e
  `Listar` por uma consulta em lote. `BaixaDto` e `ReclamacaoDto` ganham os três campos do contrato.
- [ ] **Step 4: Rodar** Api e Application: verde. **Step 5: Commit** — `feat(fase-5b2): nao repor e o que falta repor`.

### Task 7: A conta pura do reaproveitamento — `CalculoDoReaproveitamento`

**Files:** `Application/Retrabalho/CalculoDoReaproveitamento.cs`, `Application/Baixa/CalculoDaBaixa.cs`. Test:
`tests/Rastreamento.Application.Tests/Retrabalho/CalculoDoReaproveitamentoTests.cs`.

**Interfaces:**
- Consumes: Task 2 (calculadora com `Pronto`), `CalculoDaBaixa.Rascunho.Cascata` (5B-1).
- Produces: `public sealed record SobraDisponivel(int NoDoRetrabalhoId, int NoDeOrigemId, Local Local, decimal Sobra);`
  `public sealed record ItemReaproveitado(int NoDoRetrabalhoId, Local Local, decimal Quantidade);`
  `CalculoDoReaproveitamento.Disponivel(CalculadoraDeExecucao original, IReadOnlyDictionary<int, int> origemDe) ->
  IReadOnlyList<SobraDisponivel>` (P8); `Sugerir(CalculadoraDeExecucao retrabalho, IReadOnlyList<SobraDisponivel>
  disponivel) -> IReadOnlyList<(SobraDisponivel, decimal Precisa, decimal Sugerido)>`; `Conferir(CalculadoraDeExecucao
  retrabalho, IReadOnlyList<SobraDisponivel> disponivel, IReadOnlyList<ItemReaproveitado> pedido, Func<int, string>
  nome) -> Result<IReadOnlyList<ItemReaproveitado>>`; em `CalculoDaBaixa`, `public static IReadOnlyList<LinhaPlanejada>
  PlanejarDispensaDoPronto(CalculadoraDeExecucao calcComPronto, IReadOnlyList<int> nosComPronto)`.

- [ ] **Step 1: Testes** — o exemplo da seção 4.4 da spec (A ← B ← C ← D, 4 D por C; o Retrabalho precisa de 4 D, a
  sobra tem 2 D):
  - `Sugere_o_minimo_entre_a_sobra_e_o_que_o_no_precisa` → D: `Precisa 4`, `Sugerido 2`.
  - `Sugestao_desce_do_topo_e_o_pronto_do_pai_reduz_o_do_filho` — C pronto 1 sugerido → D `Precisa 0`, `Sugerido 0`.
  - `Sobra_em_montagem_reparte_por_Setor` (P8).
  - `Pedir_alem_da_sobra_e_409_AlemDaSobra` (C19); `Pedir_alem_do_que_o_pai_ainda_pede_e_409_AlemDoQueOPaiPrecisa`;
    `Posicao_que_nao_existe_no_Roteiro_do_Retrabalho_e_400_PosicaoDoProntoInexistente` (nomeando o nó);
    `No_sem_origem_nao_aparece_no_disponivel`.
  - `Dispensa_do_pronto_tira_os_filhos_a_iniciar_que_o_pai_pronto_nao_pede` — C pronto 1 → as 4 D a iniciar saem;
    `Dispensa_desce_dois_niveis`.
- [ ] **Step 2: Rodar e ver falhar.** **Step 3: Implementar.** **Step 4: Rodar:** verde.
- [ ] **Step 5: Commit** — `feat(fase-5b2): conta pura do reaproveitamento da sobra`.

### Task 8: "Usar a sobra" e o estorno dele

**Files:** `Application/Retrabalho/ReaproveitamentoUseCase.cs`, `Api/Controllers/ReaproveitamentoController.cs`,
`Application/Baixa/BaixaUseCase.Estorno.cs`, `Api/Program.cs`. Test: `tests/Rastreamento.Api.Tests/ReaproveitamentoEndpointsTests.cs`.

**Interfaces:**
- Consumes: Tasks 2, 5, 7.
- Produces: `ReaproveitamentoUseCase.Prever(int retrabalhoId, CancellationToken ct) ->
  Task<Result<PreviaDoReaproveitamentoDto>>`; `Reaproveitar(int retrabalhoId, NovoReaproveitamentoDto dto, int
  usuarioId, CancellationToken ct) -> Task<Result<BaixaDto>>`.

- [ ] **Step 1: Testes de Api:**
  - `Usar_a_sobra_grava_a_baixa_no_original_e_o_pronto_no_retrabalho` — a baixa `Reaproveitado` (Pedido original,
    `PedidoRetrabalhoId`), as linhas `Perda` no original e as `Pronto` no Retrabalho na **mesma posição física**; o
    Retrabalho continua `Aberto` (C11); as Tarefas mostram o pronto aguardando coleta.
  - `Pronto_de_pai_dispensa_os_filhos_numa_baixa_ligada` (P2: `Descarte`, `PerdaDeOrigemId`).
  - `Sobra_ja_na_Solda_de_um_Kit_vira_pronto_e_o_irmao_completa` (D8) — entrega só do irmão passa, e o iniciar do pai
    monta.
  - `Usar_a_sobra_com_Retrabalho_em_producao_e_409_RetrabalhoForaDeAberto`; `PCP_sim_Qualidade_403`;
    `Original_concluido_aceita_o_reaproveitamento` (C9); a corrida do Review Focus 3 e o caso do Review Focus 4.
  - `Estornar_o_reaproveitamento_desfaz_as_tres_coisas` — a baixa do original, os `Pronto` e a baixa ligada, numa
    transação; `Estornar_com_pronto_que_andou_e_409_ProntoJaAndou`; `Estornar_a_baixa_ligada_direto_e_409_EstornoImpossivel`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** a seção 4.4 da spec com P2, P6: grava a `Perda` `Reaproveitado` (`EstruturaItemId` = a
  Peça de origem), as linhas `Perda` e os `Pronto` (`PerdaId`), relê o estado do Retrabalho, chama
  `PlanejarDispensaDoPronto` e, se houver linhas, grava a baixa ligada; recalcula o fechamento dos dois Pedidos. O
  estorno da `Reaproveitado` em `BaixaUseCase.Estornar` confere "andou" pelo saldo do nó do Retrabalho no `Local` de
  cada `Pronto`.
- [ ] **Step 4: Rodar** Api inteira: verde. **Step 5: Commit** — `feat(fase-5b2): usar a sobra como pronto no Retrabalho`.

### Task 9: Os estornos que tirariam o chão (C20) e as recusas dos tipos novos

**Files:** `Application/Baixa/{BaixaUseCase,BaixaUseCase.Estorno}.cs`, `Application/Execucao/EstornoUseCase.cs`,
`Application/Expedicao/RemessaUseCase.cs`. Test: `BaixaEndpointsTests`, `RemessaEndpointsTests`,
`ExecucaoEndpointsTests` (um teste cada caso).

- [ ] **Step 1: Testes:** `Estornar_baixa_reposta_e_409_ReposicaoResolvida`; `Estornar_baixa_dispensada_e_409_ReposicaoResolvida`
  (desfeita a dispensa, passa); `Estornar_remessa_abaixo_do_reclamado_e_409_AbaixoDoReclamado`;
  `Rota_generica_recusa_estornar_Pronto_com_EstornoImpossivel`; `Rota_generica_estorna_a_Devolucao_que_nao_andou` (o
  Retrabalho volta a `Aberto`, C23); `Dar_baixa_com_motivo_Reaproveitado_e_400_MotivoInvalido`.
- [ ] **Step 2: Rodar e ver falhar.** **Step 3: Implementar** (as guardas leem o dado **depois** da trava).
- [ ] **Step 4: Rodar** a suíte inteira com `-m:1`: verde. **Step 5: Commit** — `feat(fase-5b2): estornos que desfariam a reposicao`.

### Task 10: Documentos

**Files:** a última linha do mapa.

- [ ] **Step 1:** Os ajustes da seção 10 da spec: `specs/01` (regras 12, 14, 17, 24, 25, 27, 28, 38 e a regra nova 39),
  `specs/02` (conferir contra a Task 1), `specs/04` fluxo 5 reescrito, `specs/05` (as rotas e os erros do contrato
  daqui), `specs/06` (a 5B-2 com spec e planos; a 5C ganha a estrutura alterada no meio da produção, D6), a errata da
  spec do Kit, o `CLAUDE.md` (o comando do alter, a regra de cor `atencao` — que o plano 2 também toca — e as contagens
  remedidas, com a métrica dita) e o `README.md`.
- [ ] **Step 2:** `dotnet build -warnaserror` e a suíte inteira, para as contagens: verde.
- [ ] **Step 3: Commit** — `docs(fase-5b2): dominio, fluxos, rotas e roadmap com a reposicao`.

## Self-review (feito ao escrever este plano)

- **Cobertura da spec:** 2.1 D1 (Tasks 3, 5), D2 (7, 8), D3 (6), D4/D5 (4, 5), D6 (10), D7 (os dois planos), D8 (2, 8),
  D9 (5: uma abertura por alvo), A4 (6, e o plano 2); C1–C20 nas Tasks 2 a 9; seção 3 inteira na Task 1 (mais P2);
  seção 4.1–4.8 nas Tasks 2–9; seção 10 na Task 10. O âmbar na tela é do plano 2.
- **Tipos:** `NoParaGravar.OrigemEstruturaItemId` (Task 3) é o que a Task 5 grava e a Task 7 lê por
  `EstruturaItem.OrigemEstruturaItemId`; `ListarProntosAsync` (Task 2) alimenta a calculadora das Tasks 7 e 8.
- **Proporção:** sem corpos de função; os algoritmos que a assinatura não determina (P4, P8, a sugestão de cima para
  baixo) estão em uma linha cada.
