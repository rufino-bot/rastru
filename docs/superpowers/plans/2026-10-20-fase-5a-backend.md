# Fase 5A — Aferição, expedição e fechamento — Plano 1 de 2 (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao backend a aferição dimensional, a contagem das não conformes, o conserto, a liberação da Gestão, a
remessa (com lote) e o fechamento automático de Peça, Agrupamento e Pedido, tudo no livro de movimentações.

**Architecture:** Oito posições e seis tipos novos em `dbo.Movimentacao` (C1 da spec), com três agrupadores novos
(`dbo.ContagemDimensional`, `dbo.LiberacaoDimensional`, `dbo.Remessa`) apontados por colunas novas do livro, e
`dbo.RelatorioDimensional` reescrita como registro só de inclusão das aferições. Uma pasta nova
`Application/Expedicao/` reúne as duas contas puras (`CalculoDaExpedicao`, `CalculoDoFechamento`), o serviço
`FechamentoDoPedido` e os casos de uso; o repositório novo `IExpedicaoRepository` divide o `DbContext` com o
`IExecucaoRepository`, então tudo corre na transação SERIALIZABLE de `ExecutarAsync`. Entrega, estorno, término e
as edições de estrutura ganham o recálculo do fechamento.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md`, aprovada em
2026-10-20 (D1–D10 do usuário, C1–C20 do desenho). Branch: `claude/brainstorm-fase-5-4otppt`, spec em `17e9c30`,
sobre `141ae3c`. O plano 2 (front, `docs/superpowers/plans/2026-10-20-fase-5a-front.md`) consome a seção "Contrato
JSON novo" daqui.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass
  de Critical/Important → re-review). Nenhuma task deste plano tem dispensa prevista.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com
  `scripts/review-package BASE HEAD`, nunca com `HEAD~1`. Ledger do plano em `.superpowers/sdd/2026-10-20-fase-5a/`;
  artefatos com nome escopado por este plano (`backend-task-N-…`).
- **A review de branch inteira só é despachada depois que o usuário fizer a conferência no navegador**, a última task
  do plano 2 (regra do usuário de 2026-10-07, no `CLAUDE.md`).
- **Schema pelo caminho do `CLAUDE.md`:** `specs/02-modelo-de-dados.sql` primeiro, depois `db/alter-fase-5a.sql`
  idempotente (formato do `db/alter-fase-4.sql`, aplicado com `-b -f 65001`), depois o mapeamento EF. Nada de
  `Add-Migration` nem `EnsureCreated`.
- **O livro continua só de inclusão**, e os quatro registros novos também: nenhum `UPDATE` nem `DELETE` em
  `Movimentacao`, `RelatorioDimensional`, `ContagemDimensional`, `LiberacaoDimensional` ou `Remessa` fora da limpeza
  de teste. "Estornado" é sempre calculado (existe estorno apontando).
- **Só Peça** entra nas posições novas. Item nunca chega ao local de expedição.
- **A remessa tira nesta ordem, e só desta** (C10): `Liberada`, `Conforme`, `NaExpedicao`, `NaoConforme`.
  `AguardandoLiberacao` não sai.
- **Avisar e deixar expedir** (D8): nenhuma situação de aferição recusa a remessa.
- **Pedido `Concluido` recusa toda escrita**, com `409 PedidoFechado`, **menos o estorno de remessa** (C13). Pedido
  `Cancelado` recusa toda escrita, estorno de remessa inclusive.
- **Perfis, exatos** (spec 5.2): expedição (aferir, contar, conserto, pedir liberação, expedir)
  `Qualidade,Administrador`; resposta de liberação `Gestao,Administrador`; estornos compostos
  `Qualidade,Gestao,PCP,Administrador` (o autor, ou PCP/Administrador, decidido pelo caso de uso com `403 Proibido`);
  o estorno genérico de movimento ganha `Qualidade`; terminar conserto `Operador,Administrador`; entregar conserto
  `Movimentador,Administrador` (as rotas de hoje); editar `RequerRelatorioDimensional` `PCP,Administrador` (a rota de
  editar nó de hoje). Leituras: qualquer autenticado.
- **Recusa que nomeia uma Peça usa `EstadoDeExecucao.Nome`** (a regra 19: descrição do nó, senão a do Componente),
  nunca `no {id}` nem `item {id}`. Lição da Fase 4: todo cenário de teste que afirma uma frase com nome tem **uma Peça
  copiada do catálogo, sem descrição própria**.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês.
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa. Comentário de código **não cita** ledger,
  brief nem "Task N": o repositório é público. Decisões citadas como "D*n*"/"C*n* da spec da Fase 5A" ou "decisão
  P*n* do plano de backend da Fase 5A".
- `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`.
  Teste de banco **escopa a asserção** nas linhas que ele inseriu.
- Mensagens de erro dos `.cs` novos **com** acento; comentários sem acento, como os vizinhos.
- `git pull` antes de todo commit, nos dois repositórios. No ledger, `git add` por caminho explícito, em chamada
  separada do `task-brief`/`review-package`.

## Review Focus

Entradas que a spec implica e que nenhum teste das tasks exercitaria sem esta lista (cada uma tem teste na task dona,
marcado "Review Focus"):

1. **Duas remessas simultâneas da mesma Peça** que, somadas, passam do expedível — uma `201`, a outra `409
   SaldoInsuficiente`; nunca `500`, nunca saldo negativo (Task 9).
2. **A Gestão responde a um pedido de liberação que a Qualidade estornou no mesmo instante** — `409
   LiberacaoNaoPendente`; nunca uma resposta pendurada num pedido estornado (Task 10, porque precisa do estorno).
3. **Setor do conserto inativado com a peça lá dentro** — o término e a entrega de volta continuam; só **mandar** para
   Setor inativo é recusado (Task 7).
4. **Pedido pausado** — a pausa recusa só o início (regra 31): aferir, contar, conserto, liberação e remessa
   continuam (Task 9).
5. **Peça copiada do catálogo, sem descrição própria** — o nome do Componente nas recusas de lote e na lista da
   expedição, nunca `no {id}` (Tasks 9 e 11).

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem         # na nuvem; na máquina local: docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
```

Banco de máquina local que já existe: aplicar o `db/alter-fase-5a.sql` (Task 1) com o bloco que a Task 1 acrescenta
ao `CLAUDE.md`, e reaplicar o `db/seed.sql` (traz `qualidade` e `gestao`). Na nuvem, `backend-na-nuvem` cria o banco
do `.sql` e do seed.

**Baseline:** o `CLAUDE.md` registra backend **1.475** (Api 391 · Application 842 · Infrastructure 242) em `4cca2f4`.
**O pré-flight remede na base da Task 1** e escreve o número no ledger do plano. Cada implementer reporta o **delta**
da própria task.

## Decisões deste plano (onde a spec deixou a escolha)

- **P1 — Três agrupadores com coluna própria no livro.** `Movimentacao` ganha `ContagemId`, `LiberacaoId` e
  `RemessaId` (FK, nulas), no molde de `MontagemId`: a linha e o estorno dela apontam o registro.
  `CK_Movimentacao_RegistroCoerente` amarra cada coluna aos tipos dela e proíbe duas preenchidas.
- **P2 — `dbo.LiberacaoDimensional` é uma tabela só, com `Tipo` `Pedido` | `Resposta`.** A resposta aponta o pedido
  (`PedidoDeLiberacaoId`); `Quantidade` é a pedida no pedido e a liberada na resposta (zero = recusa total); `Texto`
  é a justificativa (obrigatória) ou a observação (opcional), `NVARCHAR(500)`. Um pedido está **pendente** quando a
  linha `PedidoDeLiberacao` dele não foi estornada e não há resposta com linha não estornada.
- **P3 — A aferição não tem coluna de "estornada".** `dbo.RelatorioDimensional` tem `Tipo` `Afericao` | `Estorno`
  e `EstornoDeId` com índice único filtrado, no molde de `MaterialSeparacao`. A **válida** é a `Afericao` mais
  recente sem estorno.
- **P4 — O recálculo do fechamento só roda quando um movimento toca o fim do caminho.** `Posicoes.DoFimDoCaminho`
  (as cinco do local de expedição, as três do conserto e `Expedido`); um movimento "toca" quando a origem ou o
  destino está no conjunto. Entrega e estorno genérico travam os Pedidos (`TravarPedidosDosNosAsync`, depois dos
  nós, a ordem fixa da spec da Fase 3, seção 8.1) **só** quando algum nó da lista é Peça.
- **P5 — O aviso da remessa é calculado no backend e devolvido no `201`**, e o front mostra a prévia com uma cópia da
  ordem C10 (plano 2). O backend não pede confirmação: o D8 é "não trava".
- **P6 — Terminar conserto tem rota individual e entra no lote de término.** A individual é
  `POST /estrutura/{id}/terminos-de-conserto` (o `TerminoDto` carrega `Ordem`, que o conserto não tem). No lote,
  `ItemDeTerminoDto` ganha `bool Conserto = false`: com `true`, o item vem **sem** `Ordem` (com `Ordem` é `400
  OrigemInvalida`) e termina `EmConserto` → `ConsertoPronto` no Setor do lote; o mesmo `POST /terminos` mistura itens
  normais e de conserto, tudo ou nada. Pedido do usuário em 2026-10-20: o operador termina o conserto como termina o
  resto, pela caixa e pela barra do lote.
- **P7 — Quatro controllers novos**, um por conjunto de perfis (a guarda de espelhamento do front compara um arquivo
  com UMA entrada de `permissoes.ts`): `ExpedicaoController` (`expedicao`), `LiberacaoController`
  (`liberacao`), `EstornoDaExpedicaoController` (`estornoDaExpedicao`) e as leituras sem `Roles` nos mesmos arquivos.
  O `EstornoController` ganha `Qualidade`.
- **P8 — `PedidoDto` e `AgrupamentoDto` ganham `DataConclusao`** (`DateTime?`, último parâmetro, padrão `null`),
  para a tela mostrar "Concluído em".
- **P9 — "Remessas recentes" são as dos últimos 30 dias** (`ConsultaDaExpedicaoUseCase.DiasDeRemessasRecentes =
  30`), contados de `DateTime.UtcNow`.
- **P10 — O conserto vai a Setor ativo** (`ISetorRepository.ObterPorIdAsync` + `Ativo`); inexistente ou inativo →
  `400 SetorInvalido`. Depois de mandado, nada mais confere o Setor (Review Focus 3).

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `specs/02-modelo-de-dados.sql`, `db/alter-fase-5a.sql`, `db/seed.sql` | 1 | schema, migração, usuários `qualidade` e `gestao` |
| `Domain/Entities/{Posicoes,Movimentacao,RelatorioDimensional,ContagemDimensional,LiberacaoDimensional,Remessa}.cs`, `Infrastructure/Persistence/Configurations/*`, `RastreamentoDbContext.cs`, `Application/Execucao/Local.cs` | 1 | entidades, constantes, mapeamento |
| `Application/Expedicao/{CalculoDaExpedicao,CalculoDoFechamento,ExpedicaoDtos,CodigosDaExpedicao}.cs` | 2 | as regras puras |
| `Domain/Abstractions/{IExpedicaoRepository,IExecucaoRepository}.cs`, `Infrastructure/Persistence/{ExpedicaoRepository,ExecucaoRepository}.cs` | 3 | acesso a dados |
| `Application/Expedicao/FechamentoDoPedido.cs`, `Application/Execucao/{EntregaUseCase,EstornoUseCase}.cs`, `Application/Cadastros/{Dtos,CadastroDePedidoUseCase,CadastroDeAgrupamentoUseCase}.cs` | 4 | fechamento e ganchos |
| `Application/Estrutura/{MontagemDeEstruturaUseCase,EstruturaDtos}.cs`, `Application/Importacao/ImportacaoDeEstruturaUseCase.Confirmacao.cs` | 5 | C14, C15 |
| `Application/Expedicao/AfericaoUseCase.cs`, `Api/Controllers/ExpedicaoController.cs` | 6 | aferir, contar, conserto |
| `Application/Execucao/{EntregaUseCase,ApontamentoUseCase,ConsultaDeExecucaoUseCase,ExecucaoDtos}.cs`, `Api/Controllers/ApontamentoController.cs` | 7 | conserto no chão |
| `Application/Expedicao/LiberacaoUseCase.cs`, `Api/Controllers/LiberacaoController.cs` | 8 | liberação |
| `Application/Expedicao/RemessaUseCase.cs`, `Api/Controllers/{ExpedicaoController,EstornoDaExpedicaoController}.cs` | 9 | remessa e estorno dela |
| `Application/Expedicao/EstornoDaExpedicaoUseCase.cs`, `Api/Controllers/{EstornoDaExpedicaoController,EstornoController}.cs` | 10 | estornos compostos |
| `Application/Expedicao/ConsultaDaExpedicaoUseCase.cs` | 11 | lista, histórico, pendentes, remessas recentes |
| `Api/Program.cs` | 3–11 | DI |
| `specs/00`, `01`, `05`, `06`, `CLAUDE.md`, `README.md` | 12 | documentos |

## Contrato JSON novo

camelCase. Decimais como número. `dataHora` ISO 8601 com `-03:00`; `dataEntrega` `"yyyy-MM-dd"`. Erros
`{ "erro", "mensagem" }`; 404 sem corpo.

```jsonc
// POST /estrutura/{id}/afericoes  (Qualidade)          → 201 AfericaoDto
{ "resultado": "Conforme" }                             // ou "NaoConforme"
// AfericaoDto
{ "id": 4, "resultado": "NaoConforme", "dataHora": "…", "usuarioId": 20, "usuarioNome": "Qualidade",
  "estornada": false, "estornavel": true }

// POST /estrutura/{id}/contagens  (Qualidade)          → 201 ContagemDto
{ "conformes": 4, "naoConformes": 2 }
// ContagemDto
{ "id": 7, "conformes": 4, "naoConformes": 2, "dataHora": "…", "usuarioId": 20, "usuarioNome": "Qualidade",
  "estornada": false, "estornavel": true }

// POST /estrutura/{id}/consertos  (Qualidade)          → 201 MovimentacaoDto (tipo "Conserto")
{ "setorId": 2, "quantidade": 1 }

// POST /estrutura/{id}/pedidos-de-liberacao  (Qualidade) → 201 LiberacaoDto
{ "quantidade": 2, "justificativa": "Aba da dobra 1,5 mm fora; o cliente não usa." }
// POST /liberacoes/{id}/resposta  (Gestao)             → 201 LiberacaoDto (a resposta)
{ "quantidadeLiberada": 2, "observacao": null }         // 0 = recusa
// LiberacaoDto
{ "id": 9, "tipo": "Pedido", "pedidoDeLiberacaoId": null, "estruturaItemId": 42, "quantidade": 2,
  "texto": "…", "dataHora": "…", "usuarioId": 20, "usuarioNome": "Qualidade", "estornada": false, "estornavel": true }
// GET /liberacoes/pendentes                            → LiberacaoPendenteDto[] (mais antigo primeiro)
[ { "id": 9, "quantidade": 2, "justificativa": "…", "dataHora": "…", "usuarioNome": "Qualidade",
    "peca": { "id": 42, "descricao": "Chapa dobrada 3 mm", "codigoDoComponente": "CHP-220" },
    "pedido": { "id": 5, "numero": "PED-2041", "cliente": "…", "dataEntrega": "2026-10-22", "atrasado": true } } ]

// POST /remessas  (Qualidade)                          → 201 ResultadoDaRemessaDto
{ "itens": [ { "estruturaItemId": 42, "quantidade": 9 } ] }
// ResultadoDaRemessaDto
{ "remessa": { /* RemessaDto */ },
  "avisos": [ { "pecaId": 42, "tipo": "NaoConformeSemLiberacao", "quantidade": 2 } ] }   // ou "SemAfericao" | "SemMedicao"
// RemessaDto  (GET /remessas/recentes devolve a lista, mais recente primeiro)
{ "id": 3, "dataHora": "…", "usuarioId": 20, "usuarioNome": "Qualidade", "estornada": false, "estornavel": true,
  "linhas": [ { "pecaId": 42, "pecaDescricao": "Chapa dobrada 3 mm", "codigoDoComponente": "CHP-220",
                "pedidoId": 5, "pedidoNumero": "PED-2041", "pedidoConcluido": false,   // o status de AGORA
                "origem": "Conforme", "quantidade": 7 } ] }

// GET /expedicao                                       → ExpedicaoDto
{ "pedidos": [ { "id": 5, "numero": "PED-2041", "cliente": "…", "dataEntrega": "2026-10-22", "atrasado": true,
    "pausa": null,
    "pecas": [ { "id": 42, "descricao": "Chapa dobrada 3 mm", "codigoDoComponente": "CHP-220",
        "agrupamentoId": 3, "agrupamentoCodigo": "AG-01", "quantidade": 10, "requerRelatorioDimensional": true,
        "situacao": "NaoConforme",                     // SemRelatorio | Pendente | Conforme | NaoConforme
        "afericao": { /* AfericaoDto */ },              // ou null
        "posicoes": { "naExpedicao": 0, "conforme": 7, "naoConforme": 2, "aguardandoLiberacao": 0, "liberada": 0,
                      "emConserto": 1, "expedido": 0 },  // emConserto = soma das três posições do conserto
        "consertos": [ { "setorId": 2, "setorNome": "Dobra", "posicao": "EmConserto", "quantidade": 1 } ],
        "liberacaoPendente": null,                      // ou { id, quantidade, justificativa, dataHora, usuarioNome }
        "expedivel": 9 } ] } ] }

// GET /estrutura/{id}/expedicao                        → RegistroDaExpedicaoDto[] (mais recente primeiro)
{ "tipo": "Contagem",   // Afericao | Contagem | Conserto | PedidoDeLiberacao | RespostaDeLiberacao | Remessa
  "id": 7, "dataHora": "…", "usuarioId": 20, "usuarioNome": "Qualidade",
  "resultado": null, "conformes": 4, "naoConformes": 2, "quantidade": null, "setorNome": null, "texto": null,
  "estornada": false, "estornavel": true }       // Remessa: sempre estornavel=false aqui (C20)

// Estornos (todos → 201 com o registro de estorno ou as linhas dele)
// POST /afericoes/{id}/estorno → AfericaoDto (tipo do estorno)
// POST /contagens/{id}/estorno → MovimentacaoDto[]
// POST /liberacoes/{id}/estorno → MovimentacaoDto[]   (pedido ou resposta)
// POST /remessas/{id}/estorno  → MovimentacaoDto[]
// POST /movimentacoes/{id}/estorno (existe) estorna o Conserto, e recusa linha com contagemId/liberacaoId/remessaId

// POST /estrutura/{id}/terminos-de-conserto  (Operador) → 201 MovimentacaoDto
{ "setorId": 2, "quantidade": 1 }
// POST /terminos (existe): item novo de conserto, sem ordem
{ "setorId": 2, "itens": [ { "estruturaItemId": 42, "ordem": null, "quantidade": 1, "conserto": true } ] }
// POST /entregas (existe): origem nova { "posicao": "AguardandoConserto" | "ConsertoPronto", "setorId": 2, "ordem": null }

// GET /setores/{id}/fila: campo novo no fim
"conserto": [ { "no": { /* NoResumoDto */ }, "posicao": "EmConserto", "quantidade": 1, "estornaveis": [] } ]
// GET /tarefas: campo novo no fim
"consertos": [ { "no": { /* NoResumoDto */ }, "origem": { /* LocalDto */ }, "destino": { /* LocalDto */ }, "quantidade": 1 } ]

// PUT /estrutura/{id} (existe): campo novo, opcional
{ "descricao": null, "quantidade": 10, "quantidadePorPai": null, "requerRelatorioDimensional": true }  // null = mantém
// PedidoDto e AgrupamentoDto: campo novo "dataConclusao": null | "…"
// MovimentacaoDto: campos novos "contagemId", "liberacaoId", "remessaId" (null quando não há)
```

---

### Task 1: Schema, migração, seed e mapeamento

**Modelo:** sonnet. Molde: a Task 1 da Fase 4 (`MaterialSeparacao`) e `MovimentacaoConfiguration`.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (`dbo.Movimentacao`; sai `dbo.Expedicao`, `dbo.RelatorioDimensionalAvaliacao`;
  `dbo.RelatorioDimensional` reescrita; entram `dbo.ContagemDimensional`, `dbo.LiberacaoDimensional`, `dbo.Remessa`)
- Create: `db/alter-fase-5a.sql`
- Modify: `db/seed.sql` (usuários `qualidade` e `gestao`)
- Modify: `src/Rastreamento.Domain/Entities/{Posicoes,Movimentacao}.cs`
- Create: `src/Rastreamento.Domain/Entities/{RelatorioDimensional,ContagemDimensional,LiberacaoDimensional,Remessa}.cs`
- Modify/Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/` (as quatro novas e a do livro),
  `RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Application/Execucao/Local.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs` (`LimparAsync`),
  `tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs` (limpeza e usuário `Qualidade`)
- Modify: `tests/Rastreamento.Infrastructure.Tests/Security/SeedUsuariosDePerfilSenhaTests.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/LivroDaExpedicaoMapeamentoTests.cs`
- Modify: `CLAUDE.md` (bloco "Fase 5A — `db/alter-fase-5a.sql`" em "Comandos", depois do da Fase 4)

**Interfaces:**
- Produces:
  - `Posicoes`: `AguardandoConserto`, `EmConserto`, `ConsertoPronto`, `Conforme`, `NaoConforme`,
    `AguardandoLiberacao`, `Liberada`, `Expedido`; `Todas` na ordem `AIniciar, NoSetor, AguardandoColeta,
    AguardandoMontagem, AguardandoConserto, EmConserto, ConsertoPronto, NaExpedicao, Conforme, NaoConforme,
    AguardandoLiberacao, Liberada, Montado, Expedido`; `IReadOnlySet<string> NoLocalDeExpedicao` (as cinco),
    `IReadOnlySet<string> DoConserto` (as três), `IReadOnlySet<string> DoFimDoCaminho` (as nove: as oito e
    `Expedido`), e `static bool TocaOFimDoCaminho(Movimentacao m)`.
  - `TiposDeMovimentacao`: `Medicao`, `Conserto`, `PedidoDeLiberacao`, `Liberacao`, `Recusa`, `Expedicao` (o
    comentário "A Fase 5 acrescenta `Pronto`" passa a dizer que o `Pronto` é da Fase 5B).
  - `Movimentacao.ContagemId`, `.LiberacaoId`, `.RemessaId` (`int?`).
  - `RelatorioDimensional { Id, EstruturaItemId, Tipo, Resultado (string?), EstornoDeId (int?), DataHora, UsuarioId }`,
    com `TiposDeAfericao.Afericao`/`.Estorno` e `ResultadosDeAfericao.Conforme`/`.NaoConforme` no mesmo arquivo.
  - `ContagemDimensional { Id, EstruturaItemId, Conformes, NaoConformes, DataHora, UsuarioId }`.
  - `LiberacaoDimensional { Id, EstruturaItemId, Tipo, PedidoDeLiberacaoId (int?), Quantidade, Texto (string?),
    DataHora, UsuarioId }`, com `TiposDeLiberacao.Pedido`/`.Resposta`.
  - `Remessa { Id, DataHora, UsuarioId }`.
  - `DbSet`s: `Afericoes`, `Contagens`, `Liberacoes`, `Remessas`.
  - `Local`: `Conforme`, `NaoConforme`, `AguardandoLiberacao`, `Liberada`, `Expedido` (propriedades, sem Setor) e
    `AguardandoConserto(int setorId)`, `EmConserto(int setorId)`, `ConsertoPronto(int setorId)`.

- [ ] **Step 1: Schema no `02`.** Exatamente a seção 3 da spec, com estas formas:
  - `Movimentacao`: `Tipo` aceita os seis novos; as duas `CK_*Posicao` aceitam as oito novas; as `CK_*Coerente`
    põem `Conforme, NaoConforme, AguardandoLiberacao, Liberada, Expedido` no grupo "sem Setor, sem passo" e
    `AguardandoConserto, EmConserto, ConsertoPronto` no grupo de `AguardandoMontagem` (Setor, sem passo);
    `CK_Movimentacao_Transicao` ganha as linhas da tabela 3.3 da spec, com `OrigemSetorId = DestinoSetorId` em
    `Entrega AguardandoConserto→EmConserto` e em `Termino EmConserto→ConsertoPronto`; as colunas `ContagemId`,
    `LiberacaoId`, `RemessaId` com FK; e
    `CK_Movimentacao_RegistroCoerente`: `Medicao` exige `ContagemId`; `PedidoDeLiberacao`, `Liberacao`, `Recusa` exigem
    `LiberacaoId`; `Expedicao` exige `RemessaId`; cada coluna só aparece no seu tipo **ou** em `Estorno`; no máximo uma
    das três preenchida.
  - `RelatorioDimensional`: `Tipo IN ('Afericao','Estorno')`, `Resultado IN ('Conforme','NaoConforme')` nulo só no
    `Estorno`, `EstornoDeId` só no `Estorno` (FK para a própria tabela), `UX_RelatorioDimensional_EstornoDe` filtrado,
    `IX_RelatorioDimensional_EstruturaItem`.
  - `ContagemDimensional`: `Conformes >= 0`, `NaoConformes >= 0`, `Conformes + NaoConformes > 0`, `DECIMAL(18,4)`.
  - `LiberacaoDimensional`: `Tipo IN ('Pedido','Resposta')`; `Pedido` com `PedidoDeLiberacaoId IS NULL`,
    `Quantidade > 0`, `Texto IS NOT NULL`; `Resposta` com `PedidoDeLiberacaoId IS NOT NULL` (FK para a própria
    tabela), `Quantidade >= 0`; `Texto NVARCHAR(500)`; `IX_LiberacaoDimensional_EstruturaItem`.
  - `Remessa`: `Id`, `DataHora`, `UsuarioId` (FK).
  - Comentários de tabela no molde do da `MaterialSeparacao` (só inclusão, correção por estorno).

- [ ] **Step 2: `db/alter-fase-5a.sql`**, idempotente, nesta ordem:
  1. Aborto: se `dbo.Expedicao` existir com linha, ou `dbo.RelatorioDimensionalAvaliacao` existir com linha, ou
     `dbo.RelatorioDimensional` existir **sem** a coluna `Tipo` e com linha → `THROW 50002, '<qual tabela> tem linhas
     na forma antiga: migre-as a mao antes de rodar este script.', 1`.
  2. `DROP` de `RelatorioDimensionalAvaliacao`, da forma antiga de `RelatorioDimensional` (detectada por
     `COL_LENGTH(…,'Tipo') IS NULL`) e de `Expedicao`, cada um com `IF OBJECT_ID … IS NOT NULL`.
  3. `CREATE` das quatro tabelas novas (`IF OBJECT_ID … IS NULL`), com os índices.
  4. `Movimentacao`: as três colunas com FK (`IF COL_LENGTH … IS NULL`), e as sete constraints recriadas (`DROP` se
     existir, `ADD`) — a recriação é o que torna o script rodável de novo.

- [ ] **Step 3: `db/seed.sql`.** Dois blocos `IF NOT EXISTS` no padrão do `separador`: `qualidade` / `Qualidade@123`
  / `N'Qualidade e Expedição'` / perfil `Qualidade`, e `gestao` / `Gestao@123` / `N'Gestão'` / perfil `Gestao`, cada
  um com hash BCrypt custo 11 **gerado** por `new BCryptPasswordHasher().Hash(...)` (teste descartável fora do commit).

- [ ] **Step 4: Aplicar no banco** (`alter-fase-5a.sql`, depois `seed.sql`, com `-b -f 65001`) e rodar o `alter` uma
  segunda vez: sem erro e sem mudança.

- [ ] **Step 5: Testes que falham.**

```csharp
// SeedUsuariosDePerfilSenhaTests — mais dois InlineData no Theory existente
[InlineData("qualidade", "Qualidade@123", "Qualidade")]
[InlineData("gestao", "Gestao@123", "Gestao")]

// LivroDaExpedicaoMapeamentoTests : TesteComBanco — cenário por ArvoreDeTesteNoBanco (uma Peça de 10, um Setor)
[Fact] Afericao_e_estorno_fazem_a_volta_completa              // relê os 7 campos de cada
[Fact] Segundo_estorno_da_mesma_afericao_viola_UX_RelatorioDimensional_EstornoDe
[Fact] Afericao_sem_resultado_viola_o_CHECK                    // a mensagem inner cita a constraint pelo nome
[Fact] Contagem_vazia_viola_o_CHECK                            // 0 e 0
[Fact] Resposta_sem_pedido_viola_o_CHECK
[Fact] Pedido_de_liberacao_sem_texto_viola_o_CHECK
[Theory] Transicao_nova_aceita(string tipo, string de, int? deSetor, string para, int? paraSetor)
  // uma linha por transição da tabela 3.3 da spec, gravada com o agrupador que o tipo exige
[Theory] Transicao_nova_recusada(...)
  // Medicao NaExpedicao→Liberada; Expedicao AguardandoLiberacao→Expedido; Termino EmConserto(2)→ConsertoPronto(3);
  // Conserto NaoConforme→EmConserto; Entrega ConsertoPronto→Conforme
[Fact] Medicao_sem_ContagemId_viola_CK_Movimentacao_RegistroCoerente
[Fact] Duas_colunas_de_registro_violam_CK_Movimentacao_RegistroCoerente
[Fact] Expedido_entra_em_Livro_SomarSaldos_igual_ao_banco
  // grava Entrega até NaExpedicao e Expedicao até Expedido; ListarSaldosAsync == Livro.SomarSaldos do mesmo livro
```

- [ ] **Step 6: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Infrastructure.Tests --filter
  "FullyQualifiedName~LivroDaExpedicaoMapeamento|FullyQualifiedName~SeedUsuariosDePerfil"`. Esperado: falha de
  compilação.

- [ ] **Step 7: Entidades, constantes, `Local` e mapeamento.** Configurações no molde de
  `MaterialSeparacaoConfiguration` (`HasMaxLength(20)`, `HasPrecision(18, 4)`, `DataHora` `ValueGeneratedOnAdd()`).
  `ArvoreDeTesteNoBanco.LimparAsync` e a limpeza do `CenarioDaFase3NaApi` apagam, antes dos movimentos, nada; depois
  dos movimentos (estornos primeiro, como hoje), as linhas das quatro tabelas do Agrupamento/usuários do cenário
  (estornos de aferição antes das aferições; respostas antes dos pedidos). `CenarioDaFase3NaApi` ganha
  `UsuarioDeTeste Qualidade`.

- [ ] **Step 8: Rodar** os projetos de Infrastructure e Api. Esperado: verde. Build com 0 warnings.

- [ ] **Step 9: `CLAUDE.md`** — bloco "**Fase 5A — `db/alter-fase-5a.sql`.**" no formato do da Fase 4: o que ele
  leva, que aborta com linha nas tabelas antigas, que não precisa regenerar o banco, que depois dele se reaplica o
  `db/seed.sql` (traz `qualidade` e `gestao`), os dois comandos. No parágrafo dos usuários de perfil, `qualidade` e
  `gestao` entram ao lado dos outros, com a mesma frase sobre senha e hash próprios.

- [ ] **Step 10: Commit** — `feat(fase-5a): schema do fim do caminho no livro, afericao, liberacao e remessa`.

### Task 2: As regras puras — `CalculoDaExpedicao` e `CalculoDoFechamento`

**Modelo:** opus. São as regras da fase (tabela 3.2, C10, C11, C12, C16); as mutações da review atacam estes testes.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/{CalculoDaExpedicao,CalculoDoFechamento,CodigosDaExpedicao}.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/{CalculoDaExpedicaoTests,CalculoDoFechamentoTests}.cs`

**Interfaces:**
- Consumes: `Posicoes`, `Local`, `RelatorioDimensional` (Task 1).
- Produces:
```csharp
public enum SituacaoDaAfericao { SemRelatorio, Pendente, Conforme, NaoConforme }
public sealed record PosicoesDaPeca(decimal NaExpedicao, decimal Conforme, decimal NaoConforme,
    decimal AguardandoLiberacao, decimal Liberada, decimal EmConserto, decimal Expedido)
{ public decimal NoLocalDeExpedicao { get; } public decimal Expedivel { get; } }   // Expedivel = Liberada+Conforme+NaExpedicao+NaoConforme
public sealed record Tirada(string Posicao, decimal Quantidade);
public sealed record AvisoDeExpedicao(string Tipo, decimal Quantidade);  // TiposDeAviso.SemAfericao | SemMedicao | NaoConformeSemLiberacao
public static class CalculoDaExpedicao
{
  public static RelatorioDimensional? AfericaoValida(IEnumerable<RelatorioDimensional> registrosDaPeca);
  public static SituacaoDaAfericao Situacao(bool requerRelatorio, RelatorioDimensional? valida);
  public static PosicoesDaPeca Posicoes(IEnumerable<(Local Local, decimal Quantidade)> saldosDaPeca);  // EmConserto soma as tres
  public static IReadOnlyList<Tirada>? Tirar(PosicoesDaPeca p, decimal quantidade);   // null se quantidade > Expedivel
  public static IReadOnlyList<AvisoDeExpedicao> Avisos(SituacaoDaAfericao s, IReadOnlyList<Tirada> tiradas);
}
public sealed record PecaNoFechamento(int PecaId, int AgrupamentoId, decimal Quantidade, decimal Expedido, decimal NoLocalDeExpedicao);
public static class CalculoDoFechamento
{
  public static FechamentoCalculado Calcular(int pedidoId, string statusAtual, IReadOnlyList<PecaNoFechamento> pecas);
}
```
  `FechamentoCalculado(int PedidoId, string Status, bool PedidoConcluido, IReadOnlyList<AgrupamentoNoFechamento>
  Agrupamentos)` e `AgrupamentoNoFechamento(int AgrupamentoId, bool Concluido)` moram em
  `Domain/Abstractions/IExecucaoRepository.cs` (o repositório os grava, Task 3). `CodigosDaExpedicao`: `NaoEhPeca`,
  `SemRelatorioDimensional`, `ResultadoInvalido`, `AfericaoJaRegistrada`, `SemAfericaoNaoConforme`,
  `ContagemVazia`, `SetorInvalido`, `JustificativaObrigatoria`, `TextoLongoDemais`, `LiberacaoEmAberto`,
  `LiberacaoNaoPendente`, `QuantidadeAcimaDaPedida`, `ComAfericao`; `TamanhoMaximoDoTexto = 500`.

- [ ] **Step 1: Testes que falham** (`CalculoDaExpedicaoTests`):

```csharp
[Fact] Afericao_valida_e_a_mais_recente_sem_estorno
  // A1 (NaoConforme), estorno de A1, A2 (Conforme) → A2; só A1 com estorno → null
[Theory] // a tabela 3.2 da spec
[InlineData(false, null,          SituacaoDaAfericao.SemRelatorio)]
[InlineData(false, "NaoConforme", SituacaoDaAfericao.SemRelatorio)]   // marca desligada vence
[InlineData(true,  null,          SituacaoDaAfericao.Pendente)]
[InlineData(true,  "Conforme",    SituacaoDaAfericao.Conforme)]
[InlineData(true,  "NaoConforme", SituacaoDaAfericao.NaoConforme)]
void Situacao_segue_a_tabela(bool requer, string? resultado, SituacaoDaAfericao esperada)
[Fact] Posicoes_soma_as_tres_do_conserto_em_EmConserto_e_ignora_as_de_producao
[Fact] Tirar_segue_Liberada_Conforme_NaExpedicao_NaoConforme
  // L=1, C=2, N=3, NC=4; Tirar(6) == [Liberada 1, Conforme 2, NaExpedicao 3]; Tirar(10) termina com NaoConforme 4
[Fact] Tirar_nunca_tira_AguardandoLiberacao     // AL=5 e nada mais: Expedivel==0, Tirar(1)==null
[Fact] Tirar_acima_do_expedivel_devolve_null
[Theory] // o aviso por situação
[InlineData(SituacaoDaAfericao.SemRelatorio, "NaExpedicao", null)]
[InlineData(SituacaoDaAfericao.Pendente,     "NaExpedicao", "SemAfericao")]
[InlineData(SituacaoDaAfericao.Conforme,     "NaExpedicao", null)]
[InlineData(SituacaoDaAfericao.NaoConforme,  "NaExpedicao", "SemMedicao")]
[InlineData(SituacaoDaAfericao.NaoConforme,  "NaoConforme", "NaoConformeSemLiberacao")]
[InlineData(SituacaoDaAfericao.NaoConforme,  "Liberada",    null)]
void Aviso_por_situacao_e_origem(SituacaoDaAfericao s, string origem, string? aviso)
```

  `CalculoDoFechamentoTests`:

```csharp
[Fact] Tudo_expedido_conclui_Pedido_e_Agrupamentos        // Status "Concluido", PedidoConcluido, cada Agrupamento Concluido
[Fact] Tudo_que_falta_no_local_de_expedicao_e_AguardandoExpedicao
[Fact] Uma_unidade_fora_do_local_de_expedicao_e_EmProducao   // inclui unidade em conserto
[Fact] Agrupamento_concluido_com_outro_aberto_nao_conclui_o_Pedido
[Fact] Agrupamento_sem_Peca_nao_segura_o_Pedido            // C16: não aparece em `pecas`; o Pedido conclui
[Fact] Pedido_sem_Peca_nenhuma_nao_muda                    // lista vazia: Status == statusAtual, nada concluído
[Theory, InlineData("Aberto"), InlineData("Cancelado")] Status_Aberto_e_Cancelado_nao_mudam(string status)
[Fact] Concluido_que_perdeu_uma_remessa_volta_a_AguardandoExpedicao   // C13
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test tests/Rastreamento.Application.Tests --filter
  "FullyQualifiedName~Expedicao"`. Esperado: falha de compilação.
- [ ] **Step 3: Implementar.** Funções puras, sem I/O. `Avisos` agrega por tipo (uma linha por tipo, quantidade
  somada) e não devolve tipo com zero.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): regras puras da expedicao e do fechamento`.

### Task 3: Repositórios — `IExpedicaoRepository` e o fechamento no `IExecucaoRepository`

**Modelo:** sonnet.

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IExpedicaoRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/ExpedicaoRepository.cs`
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Create: `tests/Rastreamento.Application.Tests/Expedicao/FakeExpedicaoRepo.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/{FakeExecucaoRepo,CenarioDeExecucao}.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExpedicaoRepositoryTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IExpedicaoRepository
{
  void Adicionar(RelatorioDimensional afericao);
  void Adicionar(ContagemDimensional contagem);
  void Adicionar(LiberacaoDimensional liberacao);
  void Adicionar(Remessa remessa);
  Task<IReadOnlyList<RelatorioDimensional>> ListarAfericoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);
  Task<RelatorioDimensional?> ObterAfericaoAsync(int id, CancellationToken ct);
  Task<IReadOnlyList<ContagemDimensional>> ListarContagensAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);
  Task<ContagemDimensional?> ObterContagemAsync(int id, CancellationToken ct);
  Task<IReadOnlyList<LiberacaoDimensional>> ListarLiberacoesAsync(IReadOnlyCollection<int> pecaIds, CancellationToken ct);
  Task<IReadOnlyList<LiberacaoDimensional>> ListarPedidosDeLiberacaoAsync(CancellationToken ct);   // todos os Tipo=Pedido de Pedido nao encerrado
  Task<LiberacaoDimensional?> ObterLiberacaoAsync(int id, CancellationToken ct);
  Task<Remessa?> ObterRemessaAsync(int id, CancellationToken ct);
  Task<IReadOnlyList<Remessa>> ListarRemessasDesdeAsync(DateTime desdeUtc, CancellationToken ct);
  // As linhas do livro de cada registro, com os estornos delas (o estorno aponta o mesmo registro).
  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasContagensAsync(IReadOnlyCollection<int> contagemIds, CancellationToken ct);
  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasLiberacoesAsync(IReadOnlyCollection<int> liberacaoIds, CancellationToken ct);
  Task<IReadOnlyList<Movimentacao>> ListarLinhasDasRemessasAsync(IReadOnlyCollection<int> remessaIds, CancellationToken ct);
  Task<IReadOnlySet<int>> ListarAfericoesEstornadasAsync(IReadOnlyCollection<int> afericaoIds, CancellationToken ct);
}
// IExecucaoRepository, novos:
public sealed record PecaDoPedido(int PedidoId, int AgrupamentoId, int PecaId, decimal Quantidade);
Task<IReadOnlyList<PecaDoPedido>> ListarPecasDosPedidosAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);
/// Conjuntista: Status; DataConclusao = COALESCE(DataConclusao, agora) quando concluido, NULL quando nao.
Task GravarFechamentoAsync(FechamentoCalculado fechamento, DateTime agora, CancellationToken ct);
```
  `SalvarAlteracoesAsync` continua sendo o do `IExecucaoRepository` (o `DbContext` é o mesmo, escopado): o
  `ExpedicaoRepository` **não** tem `Salvar` próprio. Comentário no XML doc dele dizendo isso.

- [ ] **Step 1: Testes de Infrastructure que falham** (cenário de `ArvoreDeTesteNoBanco`, asserções escopadas):

```csharp
[Fact] Listar_afericoes_traz_as_da_peca_com_os_estornos
[Fact] Afericoes_estornadas_sao_as_com_estorno_apontando
[Fact] Linhas_da_contagem_incluem_o_estorno_dela
[Fact] Pedidos_de_liberacao_nao_traz_resposta_nem_Pedido_concluido
[Fact] Remessas_desde_respeita_a_data                     // uma de 31 dias atrás não volta
[Fact] Pecas_dos_pedidos_traz_so_as_raizes_com_Agrupamento
[Fact] Gravar_fechamento_preserva_a_DataConclusao_ja_gravada
  // conclui em T1; grava de novo concluído em T2 → DataConclusao continua T1; grava não concluído → NULL
[Fact] Gravar_fechamento_grava_o_status_e_a_data_dos_Agrupamentos   // AguardandoExpedicao e um Agrupamento concluído; relê os dois
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** `GravarFechamentoAsync` por `ExecuteUpdateAsync` em `Pedido` e em cada
  `Agrupamento` do `FechamentoCalculado`. `FakeExpedicaoRepo` em memória, no molde do `FakeSeparacaoRepo`, ligado ao
  `FakeExecucaoRepo` (as linhas do livro são as de `Execucao.Movimentacoes`); o fake implementa os dois métodos
  novos do `IExecucaoRepository` sobre `Agrupamentos` e `StatusDoPedido`, e guarda `DataConclusaoDoPedido` e
  `DataConclusaoDoAgrupamento`. `CenarioDeExecucao` ganha `FakeExpedicaoRepo Expedicao` e os usuários
  `Qualidade = 13`, `Gestao = 14`. DI no `Program.cs`.
- [ ] **Step 4: Rodar.** Esperado: verde; 0 warnings.
- [ ] **Step 5: Commit** — `feat(fase-5a): repositorio da expedicao e gravacao do fechamento`.

### Task 4: O fechamento e os ganchos no livro que já existe

**Modelo:** opus. É o ponto em que a fase mexe em código da Fase 3.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/FechamentoDoPedido.cs`
- Modify: `src/Rastreamento.Application/Execucao/{EntregaUseCase,EstornoUseCase}.cs`
- Modify: `src/Rastreamento.Application/Cadastros/{Dtos,CadastroDePedidoUseCase,CadastroDeAgrupamentoUseCase}.cs` (P8)
- Test: `tests/Rastreamento.Application.Tests/Expedicao/FechamentoDoPedidoTests.cs`,
  `tests/Rastreamento.Application.Tests/Execucao/{EntregaUseCaseTests,EstornoUseCaseTests}.cs`,
  `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`

**Interfaces:**
- Consumes: `CalculoDoFechamento`, `CalculoDaExpedicao.Posicoes` (Task 2); `ListarPecasDosPedidosAsync`,
  `GravarFechamentoAsync` (Task 3).
- Produces: `internal sealed class FechamentoDoPedido(IExecucaoRepository execucao)` com
  `Task RecalcularAsync(IReadOnlyCollection<int> pedidoIds, DateTime agora, CancellationToken ct)`. **Contrato:** quem
  chama já travou os Pedidos (`TravarPedidosDosNosAsync`) na mesma transação e já salvou os movimentos; o serviço lê o
  status por `TravarPedidoAsync` (a trava já é dele, não espera), lê Peças e saldos, calcula e grava.
  `PedidoDto(..., DateTime? DataConclusao = null)`, `AgrupamentoDto(..., DateTime? DataConclusao = null)`.

- [ ] **Step 1: Testes que falham.**

```csharp
// FechamentoDoPedidoTests (CenarioDeExecucao, Pedido EmProducao, uma Peça de 4 com Roteiro de um passo)
[Fact] Entrega_que_leva_a_ultima_unidade_ao_local_de_expedicao_poe_AguardandoExpedicao
[Fact] Estorno_dessa_entrega_volta_a_EmProducao
[Fact] Entrega_de_Item_para_montagem_nao_trava_Pedido
  // Execucao.TravasDePedido vazio depois de entregar só Item (P4)
// EstornoUseCaseTests
[Fact] Linha_de_contagem_nao_se_estorna_pela_rota_generica       // EstornoImpossivel, mensagem cita "contagem"
[Fact] Linha_de_liberacao_nao_se_estorna_pela_rota_generica       // idem, "liberação"
[Fact] Linha_de_remessa_nao_se_estorna_pela_rota_generica         // idem, "remessa"
// PedidosEndpointsTests
[Fact] Pedido_traz_dataConclusao_nula_enquanto_aberto
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
  - `EntregaUseCase.Entregar`: depois de `TravarNosAsync`, se algum nó travado é Peça, `TravarPedidosDosNosAsync`
    desses nós; depois do `SalvarAlteracoesAsync`, se algum movimento `TocaOFimDoCaminho`, `RecalcularAsync` dos
    Pedidos travados.
  - `EstornoUseCase.EstornarMovimentacao`: a recusa C17 (`original.ContagemId`/`LiberacaoId`/`RemessaId` não nulo →
    `409 EstornoImpossivel`, "Esta linha faz parte da contagem {id}: estorne a contagem inteira." e as irmãs para
    liberação e remessa), **antes** da transação, junto da recusa de montagem; dentro dela, a trava de Pedido e o
    recálculo, com o mesmo critério do P4.
  - `CadastroDePedidoUseCase` e `CadastroDeAgrupamentoUseCase` preenchem `DataConclusao`.
- [ ] **Step 4: Rodar** Application e Api. Esperado: verde, sem mudança nos testes da Fase 3 além dos novos.
- [ ] **Step 5: Commit** — `feat(fase-5a): fechamento do Pedido recalculado pelo livro`.

### Task 5: Estrutura — `RequerRelatorioDimensional` editável e Pedido concluído fechado

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Application/Estrutura/{MontagemDeEstruturaUseCase,EstruturaDtos}.cs`
- Modify: `src/Rastreamento.Application/Importacao/ImportacaoDeEstruturaUseCase.Confirmacao.cs`
- Test: `tests/Rastreamento.Application.Tests/Estrutura/MontagemDeEstruturaUseCaseTests.cs` (ou o arquivo de testes
  de edição que já cobre `EditarNo`), `tests/Rastreamento.Api.Tests/ConfirmacaoDeImportacaoTests.cs`

**Interfaces:**
- Consumes: `FechamentoDoPedido` (Task 4), `IExpedicaoRepository.ListarAfericoesAsync` (Task 3).
- Produces: `EdicaoDeNoDto(string? Descricao, decimal Quantidade, decimal? QuantidadePorPai = null,
  bool? RequerRelatorioDimensional = null)`; `MontagemDeEstruturaUseCase` recebe `IExpedicaoRepository` no
  construtor (último parâmetro).

- [ ] **Step 1: Testes que falham.**

```csharp
[Fact] Editar_liga_RequerRelatorioDimensional_da_Peca
[Fact] Editar_RequerRelatorioDimensional_em_Item_e_recusado      // 400, o código de "razão na Peça" tem irmão: RelatorioSoNaPeca
[Fact] Editar_RequerRelatorioDimensional_com_afericao_valida_e_recusado   // 409 ComAfericao
[Fact] Editar_com_RequerRelatorioDimensional_nulo_mantem_a_marca
[Theory, InlineData("CriarPeca"), InlineData("AcrescentarFilho"), InlineData("EditarNo")]
void Escrita_de_estrutura_em_Pedido_Concluido_e_recusada(string operacao)   // 409 PedidoFechado
[Fact] Criar_Peca_em_Pedido_AguardandoExpedicao_volta_a_EmProducao     // C15
[Fact] Baixar_a_quantidade_da_Peca_ate_o_expedido_conclui               // C15: DataConclusao preenchida
// ConfirmacaoDeImportacaoTests
[Fact] Confirmar_import_em_Pedido_Concluido_da_409_PedidoFechado
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** As três escritas e a confirmação do import leem o status do Pedido **depois** da
  trava, como já leem; `Concluido` → `PedidoFechado` (reusa `Falhas.PedidoFechado`). `CriarPeca`, `EditarNo` de Peça
  e a confirmação travam o Pedido e chamam `RecalcularAsync` quando o status não é `Aberto`. Código novo
  `RelatorioSoNaPeca` (400) ao lado de `ErroDeRazaoNaPeca`.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): marca de relatorio editavel e Pedido concluido fechado para estrutura`.

### Task 6: Aferir, contar e mandar para conserto

**Modelo:** opus.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/{AfericaoUseCase,ExpedicaoDtos}.cs`
- Create: `src/Rastreamento.Api/Controllers/ExpedicaoController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/AfericaoUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/ExpedicaoEndpointsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4; `LeitorDeEstado`, `ProjetorDoLivro`, `Falhas.ExecutarAsync`.
- Produces: `AfericaoUseCase` com
  `Task<Result<AfericaoDto>> Aferir(int pecaId, NovaAfericaoDto dto, int usuarioId, CancellationToken ct)`,
  `Task<Result<ContagemDto>> Contar(int pecaId, NovaContagemDto dto, int usuarioId, CancellationToken ct)`,
  `Task<Result<MovimentacaoDto>> MandarParaConserto(int pecaId, NovoConsertoDto dto, int usuarioId, CancellationToken ct)`.
  DTOs (em `ExpedicaoDtos.cs`): `NovaAfericaoDto(string? Resultado)`, `NovaContagemDto(decimal Conformes, decimal
  NaoConformes)`, `NovoConsertoDto(int SetorId, decimal Quantidade)`, `AfericaoDto`, `ContagemDto` (o "Contrato JSON
  novo"). `MovimentacaoDto` ganha `int? ContagemId, int? LiberacaoId, int? RemessaId` no fim (padrão `null`), e o
  `ProjetorDoLivro` os preenche. Rotas `POST estrutura/{id}/afericoes`, `…/contagens`, `…/consertos`, com `[Authorize(Roles =
  "Qualidade,Administrador")]`.

- [ ] **Step 1: Testes que falham** (Application, `CenarioDeExecucao` com uma Peça de 6 que exige relatório, 6 em
  `NaExpedicao`):

```csharp
[Fact] Aferir_conforme_grava_e_devolve_a_afericao
[Fact] Aferir_Item_e_recusado                         // 400 NaoEhPeca
[Fact] Aferir_Peca_sem_relatorio_e_recusado           // 409 SemRelatorioDimensional
[Fact] Aferir_com_resultado_desconhecido_e_recusado   // 400 ResultadoInvalido
[Fact] Segunda_afericao_sem_estornar_a_primeira_e_recusada   // 409 AfericaoJaRegistrada
[Fact] Aferir_em_Pedido_Concluido_e_recusado          // 409 PedidoFechado
[Fact] Contar_grava_duas_linhas_Medicao_com_a_mesma_contagem
  // 4 Conforme + 2 NaoConforme a partir de NaExpedicao; as duas com ContagemId == contagem.Id
[Fact] Contar_so_conformes_grava_uma_linha               // 6 e 0
[Fact] Contar_sem_afericao_nao_conforme_e_recusado      // aferição Conforme → 409 SemAfericaoNaoConforme
[Fact] Contar_mais_que_o_que_esta_na_expedicao_e_recusado   // 5 e 2 → 409 SaldoInsuficiente
[Fact] Contagem_zero_e_zero_e_recusada                   // 400 ContagemVazia
[Fact] Conserto_move_de_NaoConforme_para_AguardandoConserto_no_Setor
[Fact] Conserto_em_Setor_inativo_e_recusado              // 400 SetorInvalido
[Fact] Conserto_acima_do_NaoConforme_e_recusado          // 409 SaldoInsuficiente, a frase nomeia a Peça
[Fact] Conserto_tira_o_Pedido_de_AguardandoExpedicao     // o fechamento recalcula
```

  Api (`ExpedicaoEndpointsTests`, sobre `CenarioDaFase3NaApi` levado até o local de expedição):
  `Qualidade_afere_conta_e_manda_para_conserto` (os três `201` e o `GET /estrutura/{id}/posicoes` com os saldos
  esperados) e `Operador_nao_afere` (`403` do `[Authorize]`).

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Toda escrita: `ExecutarAsync` → `TravarNosAsync([pecaId])` → Pedido fechado →
  validações na ordem dos testes → grava → `SalvarAlteracoesAsync` → (Conserto e Contagem) trava de Pedido feita
  logo depois da do nó e `RecalcularAsync`. A contagem grava o `ContagemDimensional` primeiro (para ter o Id) e as
  linhas no mesmo `SalvarAlteracoesAsync`, pela navegação ou por dois `Salvar` dentro da transação — o
  implementador escolhe; o teste exige só o vínculo.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): aferir, contar e mandar para conserto`.

### Task 7: O conserto no chão — entrega, término, Fila e Tarefas

**Modelo:** sonnet.

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/{EntregaUseCase,ApontamentoUseCase,ConsultaDeExecucaoUseCase,ExecucaoDtos}.cs`
- Modify: `src/Rastreamento.Api/Controllers/ApontamentoController.cs`
- Test: `tests/Rastreamento.Application.Tests/Execucao/{EntregaUseCaseTests,ApontamentoUseCaseTests,ConsultaDeExecucaoUseCaseTests,LoteDeApontamentoTests}.cs`,
  `tests/Rastreamento.Api.Tests/ExpedicaoEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 6 (para ter o que está em `AguardandoConserto`), `FechamentoDoPedido`.
- Produces: `TerminoDeConsertoDto(int SetorId, decimal Quantidade)`; `ItemDeTerminoDto(int EstruturaItemId, int? Ordem,
  decimal Quantidade, bool Conserto = false)` (o `Ordem` passa a anulável; item normal sem `Ordem` é `400
  OrigemInvalida`);
  `ApontamentoUseCase.TerminarConserto(int noId, TerminoDeConsertoDto dto, int usuarioId, CancellationToken ct) →
  Task<Result<MovimentacaoDto>>`; rota `POST estrutura/{id}/terminos-de-conserto` (`Operador,Administrador`);
  `LinhaDeConsertoDto(NoResumoDto No, string Posicao, decimal Quantidade, IReadOnlyList<EstornavelDto> Estornaveis)`;
  `TarefaDeConsertoDto(NoResumoDto No, LocalDto Origem, LocalDto Destino, decimal Quantidade)`;
  `FilaDoSetorDto` ganha `IReadOnlyList<LinhaDeConsertoDto> Conserto` no fim; `TarefasDto` ganha
  `IReadOnlyList<TarefaDeConsertoDto> Consertos` no fim; `ContagemDeTarefasDto.Total` conta os consertos.

- [ ] **Step 1: Testes que falham.**

```csharp
// EntregaUseCaseTests
[Fact] Entrega_leva_do_AguardandoConserto_ao_EmConserto_no_mesmo_Setor
[Fact] Entrega_traz_do_ConsertoPronto_ao_NaExpedicao
[Fact] Entrega_de_conserto_com_destino_mandado_e_recusada          // 400 DestinoIndevido
[Fact] Entrega_de_conserto_com_ordem_e_recusada                    // 400 OrigemInvalida
[Fact] Review_Focus_conserto_em_Setor_inativado_continua           // inativa o Setor depois do Conserto; entrega, término e volta passam
// ApontamentoUseCaseTests
[Fact] Terminar_conserto_move_de_EmConserto_para_ConsertoPronto
[Fact] Terminar_conserto_acima_do_saldo_e_recusado                 // 409 SaldoInsuficiente
[Fact] Terminar_conserto_em_Pedido_pausado_passa                   // a pausa recusa só o início
// LoteDeApontamentoTests
[Fact] Lote_de_termino_mistura_item_normal_e_conserto              // as duas linhas gravadas numa transação
[Fact] Item_de_conserto_com_ordem_e_recusado                       // 400 OrigemInvalida
[Fact] Item_normal_sem_ordem_e_recusado                            // 400 OrigemInvalida
[Fact] Lote_com_conserto_acima_do_saldo_nao_grava_nada             // tudo ou nada; a frase nomeia a Peça
// ConsultaDeExecucaoUseCaseTests
[Fact] Fila_mostra_EmConserto_e_ConsertoPronto_na_lista_conserto
[Fact] Tarefas_mostram_a_ida_e_a_volta_do_conserto_com_origem_e_destino
[Fact] Contagem_de_tarefas_inclui_os_consertos
```

  Api: `Conserto_ida_e_volta_pela_API` (Qualidade manda, Movimentador entrega, Operador termina, Movimentador traz;
  `NaExpedicao` volta a ter a unidade).

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** `OrigemValida` aceita `AguardandoConserto` e `ConsertoPronto` com `SetorId` e sem
  `Ordem`; o `Destino` dessas duas é fixo, com `DestinoIndevido` se vier `DestinoSetorId`. `TerminarConserto` no
  molde do `Terminar`, sem Roteiro e sem passo; pelo P4 ele recalcula o fechamento, que aqui nunca muda o status. A Fila percorre `calc.Saldos` como hoje e manda
  `EmConserto`/`ConsertoPronto` do Setor para `Conserto`; as Tarefas listam `AguardandoConserto` (origem: a
  expedição, destino: `EmConserto` do Setor) e `ConsertoPronto` (origem: o Setor, destino: `NaExpedicao`).
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): conserto no chao de fabrica`.

### Task 8: Liberação — pedir e responder

**Modelo:** opus.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/LiberacaoUseCase.cs`, `src/Rastreamento.Api/Controllers/LiberacaoController.cs`
- Modify: `src/Rastreamento.Api/Controllers/ExpedicaoController.cs` (o pedido é da Qualidade), `src/Rastreamento.Application/Expedicao/ExpedicaoDtos.cs`, `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/LiberacaoUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/ExpedicaoEndpointsTests.cs`

**Interfaces:**
- Produces: `NovoPedidoDeLiberacaoDto(decimal Quantidade, string? Justificativa)`,
  `RespostaDeLiberacaoDto(decimal QuantidadeLiberada, string? Observacao)`, `LiberacaoDto`;
  `LiberacaoUseCase.Pedir(int pecaId, NovoPedidoDeLiberacaoDto dto, int usuarioId, CancellationToken ct) →
  Task<Result<LiberacaoDto>>` e `Responder(int pedidoId, RespostaDeLiberacaoDto dto, int usuarioId, CancellationToken ct)
  → Task<Result<LiberacaoDto>>`; `static bool EstaPendente(LiberacaoDimensional pedido, IReadOnlyList<LiberacaoDimensional>
  respostas, IReadOnlySet<int> linhasEstornadas, ILookup<int, Movimentacao> linhasPorLiberacao)` (ou forma
  equivalente, `internal`, usada também pela Task 11). Rotas: `POST estrutura/{id}/pedidos-de-liberacao` no
  `ExpedicaoController`; `POST liberacoes/{id}/resposta` no `LiberacaoController` (`Gestao,Administrador`).

- [ ] **Step 1: Testes que falham.**

```csharp
[Fact] Pedir_move_de_NaoConforme_para_AguardandoLiberacao_com_a_justificativa
[Fact] Pedir_sem_justificativa_e_recusado                 // "   " → 400 JustificativaObrigatoria
[Fact] Pedir_com_texto_de_501_caracteres_e_recusado       // 400 TextoLongoDemais
[Fact] Pedir_com_outro_em_aberto_na_mesma_Peca_e_recusado // 409 LiberacaoEmAberto (C18)
[Fact] Pedir_acima_do_NaoConforme_e_recusado              // 409 SaldoInsuficiente
[Fact] Responder_tudo_libera_tudo                          // 2 de 2: uma linha Liberacao; 0 em AguardandoLiberacao
[Fact] Responder_em_parte_libera_e_devolve_o_resto         // 1 de 2: Liberacao 1 e Recusa 1, as duas com LiberacaoId da resposta
[Fact] Responder_zero_e_recusa_total                       // só a linha Recusa
[Fact] Responder_acima_do_pedido_e_recusado                // 400 QuantidadeAcimaDaPedida
[Fact] Responder_duas_vezes_e_recusado                     // 409 LiberacaoNaoPendente
[Fact] Responder_pedido_estornado_e_recusado               // 409 LiberacaoNaoPendente
[Fact] Responder_uma_resposta_e_404                        // o id de uma Resposta não é pedido
```

  Api: `Gestao_responde_o_pedido_da_Qualidade`, `Qualidade_nao_responde` (403), e **Review Focus 2**:
  `Resposta_e_estorno_do_pedido_ao_mesmo_tempo_nao_deixam_resposta_pendurada` — dispara as duas requisições juntas
  (`Task.WhenAll`); uma é `201`, a outra `409` (`LiberacaoNaoPendente` ou `EstornoImpossivel`), e no banco nunca há
  resposta com linha válida apontando um pedido estornado. Ele precisa do estorno do pedido, que é da Task 10, e por
  isso **entra na Task 10** (pular teste é proibido no projeto).

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** "Pendente" relido depois da trava da Peça (a trava serializa pedido, resposta e
  estorno do mesmo pedido). A resposta grava a `LiberacaoDimensional` e as linhas na transação, com o mesmo cuidado
  de Id da Task 6.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): pedido e resposta de liberacao`.

### Task 9: A remessa — expedir, lote e estorno da remessa

**Modelo:** opus.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/RemessaUseCase.cs`, `src/Rastreamento.Api/Controllers/EstornoDaExpedicaoController.cs`
- Modify: `src/Rastreamento.Api/Controllers/ExpedicaoController.cs`, `src/Rastreamento.Application/Expedicao/ExpedicaoDtos.cs`, `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/RemessaUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/RemessaEndpointsTests.cs`

**Interfaces:**
- Produces: `ItemDaRemessaDto(int EstruturaItemId, decimal Quantidade)`, `NovaRemessaDto(IReadOnlyList<ItemDaRemessaDto>? Itens)`,
  `LinhaDaRemessaDto`, `RemessaDto`, `AvisoDaPecaDto(int PecaId, string Tipo, decimal Quantidade)`,
  `ResultadoDaRemessaDto(RemessaDto Remessa, IReadOnlyList<AvisoDaPecaDto> Avisos)`;
  `RemessaUseCase.Expedir(NovaRemessaDto dto, int usuarioId, CancellationToken ct) → Task<Result<ResultadoDaRemessaDto>>`,
  `RemessaUseCase.Estornar(int remessaId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct) →
  Task<Result<IReadOnlyList<MovimentacaoDto>>>`, e `internal Task<IReadOnlyList<RemessaDto>> ProjetarAsync(IReadOnlyList<Remessa>, QuemLe, CancellationToken)`
  (usado pela Task 11). Rotas: `POST remessas` (`ExpedicaoController`); `POST remessas/{id}/estorno`
  (`EstornoDaExpedicaoController`, `Qualidade,Gestao,PCP,Administrador`).

- [ ] **Step 1: Testes que falham.**

```csharp
[Fact] Expedir_tira_na_ordem_C10_e_grava_uma_linha_por_origem
  // L 1, C 2, NC 2 (situação NaoConforme): expedir 4 → linhas Liberada 1, Conforme 2, NaoConforme 1, todas com RemessaId
[Fact] Expedir_devolve_os_avisos_da_situacao
  // a mesma: avisos == [NaoConformeSemLiberacao 1]; Peça Pendente com 3 em NaExpedicao → [SemAfericao 3]
[Fact] Expedir_Peca_sem_relatorio_nao_avisa
[Fact] Expedir_acima_do_expedivel_e_recusado_e_cita_o_que_aguarda_liberacao   // 409 SaldoInsuficiente
[Fact] Lote_de_duas_Pecas_de_dois_Pedidos_e_uma_remessa_so
[Fact] Lote_com_a_mesma_Peca_duas_vezes_e_recusado      // 400 ItemRepetido
[Fact] Lote_vazio_e_recusado                            // 400 LoteVazio
[Fact] Lote_com_um_item_ruim_nao_grava_nada              // tudo ou nada; a recusa nomeia a Peça
[Fact] Review_Focus_recusa_nomeia_Peca_copiada_do_catalogo   // Descricao nula: a frase traz a descrição do Componente
[Fact] Review_Focus_Pedido_pausado_expede
[Fact] Ultima_remessa_conclui_Peca_Agrupamento_e_Pedido
[Fact] Estornar_a_remessa_que_concluiu_reabre_o_Pedido       // C13: aceito com o Pedido Concluido; volta a AguardandoExpedicao, DataConclusao nula
[Fact] Estornar_remessa_duas_vezes_e_recusado                // 409 JaEstornado
[Fact] Estornar_remessa_alheia_sem_ser_PCP_e_proibido        // 403 Proibido
[Fact] Estornar_remessa_de_Pedido_Cancelado_e_recusado       // 409 PedidoFechado
[Fact] Expedir_em_Pedido_Concluido_e_recusado                // 409 PedidoFechado
```

  Api (`RemessaEndpointsTests`): `Qualidade_expede_e_estorna_a_remessa`, `Movimentador_nao_expede` (403), e
  **Review Focus 1**: `Duas_remessas_ao_mesmo_tempo_nunca_passam_do_expedivel` — 10 no local de expedição, duas
  remessas de 6 em `Task.WhenAll`: um `201` e um `409 SaldoInsuficiente` (ou `ConflitoDeConcorrencia`), e
  `Expedido == 6` no banco.

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** `Expedir`: validações de lote (as de `ApontamentoUseCase` para `LoteVazio`,
  `LoteGrandeDemais`, `ItemRepetido`, `QuantidadeInvalida`, com as mesmas `Falhas`) → `ExecutarAsync` →
  `TravarNosAsync(ids)` → `TravarPedidosDosNosAsync(ids)` → por item: existe, é Peça, Pedido não fechado,
  `CalculoDaExpedicao.Tirar` não nulo → grava a `Remessa` e as linhas → recalcula os Pedidos. `Estornar`: autor →
  Pedido `Cancelado` recusa e `Concluido` **não** → `JaEstornado` relido depois da trava → uma linha `Estorno` por
  linha da remessa (`Expedido` → origem dela, com `RemessaId`) → recalcula.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): remessa, lote e estorno da remessa`.

### Task 10: Estornos da aferição, da contagem e da liberação

**Modelo:** sonnet.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/EstornoDaExpedicaoUseCase.cs`
- Modify: `src/Rastreamento.Api/Controllers/{EstornoDaExpedicaoController,EstornoController}.cs`, `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/EstornoDaExpedicaoUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/ExpedicaoEndpointsTests.cs`

**Interfaces:**
- Produces: `EstornoDaExpedicaoUseCase` com `EstornarAfericao(int id, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  → Task<Result<AfericaoDto>>`, `EstornarContagem(...) → Task<Result<IReadOnlyList<MovimentacaoDto>>>`,
  `EstornarLiberacao(...) → Task<Result<IReadOnlyList<MovimentacaoDto>>>`. Rotas `POST afericoes/{id}/estorno`,
  `contagens/{id}/estorno`, `liberacoes/{id}/estorno`. O `EstornoController` passa a declarar
  `"Operador,Movimentador,Qualidade,PCP,Administrador"` (o Conserto da Qualidade se estorna pela rota genérica).

- [ ] **Step 1: Testes que falham.**

```csharp
[Fact] Estornar_afericao_conforme_libera_uma_nova
[Fact] Estornar_afericao_nao_conforme_com_contagem_valida_e_recusado      // 409 EstornoImpossivel
[Fact] Estornar_afericao_nao_conforme_depois_de_estornar_a_contagem_passa
[Fact] Estornar_contagem_devolve_as_duas_linhas_a_NaExpedicao
[Fact] Estornar_contagem_cuja_nao_conforme_ja_foi_para_conserto_e_recusado   // 409 EstornoImpossivel
[Fact] Estornar_pedido_de_liberacao_devolve_a_NaoConforme
[Fact] Estornar_pedido_com_resposta_valida_e_recusado                     // 409 EstornoImpossivel, "estorne a resposta"
[Fact] Estornar_resposta_devolve_o_pedido_a_pendente
[Fact] Estornar_resposta_cuja_liberada_ja_saiu_e_recusado                 // 409 EstornoImpossivel
[Fact] Estorno_alheio_sem_ser_PCP_e_proibido                              // 403 Proibido, nos três
[Fact] Estorno_de_estorno_de_afericao_e_recusado                          // 409 EstornoImpossivel
[Fact] Conserto_da_Qualidade_se_estorna_pela_rota_generica
```

  Api: `Qualidade_estorna_a_propria_contagem`, `Gestao_estorna_a_propria_resposta`, e o teste de **Review Focus 2**
  descrito na Task 8.

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** no molde de `EstornoUseCase.EstornarMontagem`: autor fora da transação; dentro dela,
  trava da Peça, Pedido fechado, "já estornado" relido, saldo de cada destino, estornos com o mesmo agrupador,
  recálculo.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): estornos da afericao, da contagem e da liberacao`.

### Task 11: As leituras — lista da expedição, histórico, pendentes e remessas recentes

**Modelo:** sonnet.

**Files:**
- Create: `src/Rastreamento.Application/Expedicao/ConsultaDaExpedicaoUseCase.cs`
- Modify: `src/Rastreamento.Api/Controllers/{ExpedicaoController,LiberacaoController}.cs`, `src/Rastreamento.Application/Expedicao/ExpedicaoDtos.cs`, `src/Rastreamento.Api/Program.cs`
- Test: `tests/Rastreamento.Application.Tests/Expedicao/ConsultaDaExpedicaoUseCaseTests.cs`, `tests/Rastreamento.Api.Tests/ExpedicaoEndpointsTests.cs`

**Interfaces:**
- Produces: `ConsultaDaExpedicaoUseCase` com `Lista(QuemLe, CancellationToken) → Task<Result<ExpedicaoDto>>`,
  `Historico(int pecaId, QuemLe, CancellationToken) → Task<Result<IReadOnlyList<RegistroDaExpedicaoDto>>>`,
  `LiberacoesPendentes(CancellationToken) → Task<Result<IReadOnlyList<LiberacaoPendenteDto>>>`,
  `RemessasRecentes(QuemLe, CancellationToken) → Task<Result<IReadOnlyList<RemessaDto>>>`;
  `public const int DiasDeRemessasRecentes = 30`. Rotas `GET expedicao`, `GET estrutura/{id}/expedicao`,
  `GET remessas/recentes` (`ExpedicaoController`), `GET liberacoes/pendentes` (`LiberacaoController`), sem `Roles`.
  Os DTOs da lista no formato do "Contrato JSON novo".

- [ ] **Step 1: Testes que falham.**

```csharp
[Fact] Lista_traz_so_Pecas_com_algo_no_local_de_expedicao_ou_em_conserto
[Fact] Lista_agrupa_por_Pedido_na_ordem_da_data_de_entrega
[Fact] Lista_traz_situacao_posicoes_consertos_e_expedivel
[Fact] Lista_traz_a_liberacao_pendente
[Fact] Lista_nao_traz_Pedido_concluido
[Fact] Review_Focus_lista_nomeia_Peca_copiada_do_catalogo        // Descricao nula: a do Componente
[Fact] Historico_traz_todos_os_registros_mais_recente_primeiro
[Fact] Historico_marca_estornavel_so_para_o_autor_ou_PCP          // e Remessa sempre false
[Fact] Pendentes_traz_so_o_pedido_sem_resposta_valida
[Fact] Remessas_recentes_traz_as_dos_ultimos_30_dias_com_as_linhas
[Fact] Remessas_recentes_marca_estornavel_e_estornada
[Fact] Remessas_recentes_marca_o_Pedido_que_a_remessa_concluiu     // pedidoConcluido verdadeiro na linha
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** A lista parte de `ListarNosEmProducaoAsync` (Pedidos não encerrados, com prazo,
  status e pausa), filtra as raízes, carrega o estado pelo `LeitorDeEstado` só das Peças e lê aferições e liberações
  em lote (nunca uma consulta por Peça). `Atrasado` por `PrazoDeEntrega.EstaAtrasado` com o dia de Brasília, como a
  lista do Separador. Leituras por `ConsultarAsync`.
- [ ] **Step 4: Rodar.** Esperado: verde.
- [ ] **Step 5: Commit** — `feat(fase-5a): leituras da expedicao`.

### Task 12: Documentos

**Modelo:** sonnet. Produto é texto; tem review como as outras (não cai em nenhuma das exceções do `CLAUDE.md`).

**Files:**
- Modify: `specs/00-visao-geral.md`, `specs/01-dominio-e-regras-de-negocio.md`, `specs/05-api-endpoints.md`,
  `specs/06-roadmap-mvp.md`, `README.md`, `CLAUDE.md`

- [ ] **Step 1: `01`** — as regras 10, 11, 12, 13 e 16 e as novas 35, 36 e 37, e o glossário, como a seção 4 da spec.
  As regras citam a spec da Fase 5A pelo caminho, no padrão das outras.
- [ ] **Step 2: `00`** — o perfil Qualidade (afere e expede), a Gestão (libera não conforme), a frase "não é
  amostragem sobre 100%" reescrita, o objetivo do relatório.
- [ ] **Step 3: `05`** — todas as rotas do "Contrato JSON novo", com perfis; a tabela de erros ganha os códigos de
  `CodigosDaExpedicao` e a recusa C17 em `EstornoImpossivel`; `PedidoFechado` diz que vale também para estrutura em
  Pedido concluído (C15) e não vale para estorno de remessa (C13).
- [ ] **Step 4: `06`** — a seção da Fase 5 dividida em 5A, 5B e 5C, com o conteúdo de cada uma (o "Descartar Peça ou
  Item em Pedido rodando" vai para a 5B **com** a marca "obrigatório antes do primeiro uso real"); a 5A "em
  execução".
- [ ] **Step 5: `CLAUDE.md`** — "Ordem de implementação": a Fase 5 em três (5A, 5B, 5C), a 5A em execução; "Invariantes
  de negócio": a conservação passa a citar o expedido como posição do livro e o conserto como em produção. `README.md`:
  a fase atual.
- [ ] **Step 6: Remedir** a suíte inteira (`dotnet test Rastreamento.slnx -m:1`) e anotar o total no ledger do plano
  (não no `CLAUDE.md`, que é remedido no fim do plano 2).
- [ ] **Step 7: Commit** — `docs(fase-5a): regras, rotas e roadmap da afericao e da expedicao`.

## Self-review (feito ao escrever este plano)

- **Cobertura da spec:** seção 3 → Tasks 1 e 3; 3.2/C10/avisos → 2 e 9; 3.4 → 1, 6, 10; 3.5/C8/C9/C13 → 9; 3.6/C5/C6/C18
  → 8 e 10; 3.7/C11/C12/C16 → 2 e 4; C14/C15 → 5; C17 → 4 e 10; C19 → 6 e 7; C20 → 11 (backend) e plano 2; 5.1 →
  6–11; 5.2 → Global Constraints e os controllers; 5.3 → `CodigosDaExpedicao` e os testes de recusa; 9 → 12 e o
  plano 2 (`04`, contagens do `CLAUDE.md`).
- **Tipos conferidos entre tasks:** `PosicoesDaPeca`, `Tirada`, `AvisoDeExpedicao`, `FechamentoCalculado`,
  `PecaDoPedido`, `FechamentoDoPedido.RecalcularAsync`, `MovimentacaoDto` com os três Ids, `RemessaUseCase.ProjetarAsync`.
- **Fora daqui, no plano 2:** a tela, os painéis, a Fila e as Tarefas do front, `permissoes.ts`, o `04`, a
  conferência no navegador.
