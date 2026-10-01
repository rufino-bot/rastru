# Filtros da demanda — Plano 2 (Lote) — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Iniciar, terminar e iniciar o pai de vários itens da fila do Setor numa ação só, com seleção por checkbox, trava de seção e "Marcar todos"; e "Marcar todos" nas Tarefas — com o lote gravado no livro numa transação só, tudo ou nada.

**Architecture:** Escreve no livro, sob SERIALIZABLE. No backend, o corpo de `Iniciar` e `Terminar` do `ApontamentoUseCase` vira um núcleo por item, chamado pela rota individual (que não muda de contrato) e pelas duas rotas novas de lote (`POST /inicios`, `POST /terminos`). O lote trava todos os nós de uma vez, em ordem crescente de Id, e roda o núcleo item a item na mesma transação; a transação da execução passa a **desfazer** quando o caso de uso devolve falha, e é isso que torna o lote tudo ou nada. No front, a lógica do lote é pura (`loteDaFila.ts`), a fila ganha checkbox por linha, "Marcar todos" por seção e uma barra do lote; as Tarefas ganham "Marcar todos".

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), React Router, Vitest + Testing Library + jsdom.

**Spec:** `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md` (aprovada em 2026-09-29). Este plano cobre **só o Plano 2** da seção 9 dela. O Plano 1 (filtros) foi mesclado em `6d05c77` ([rufino-bot/rastru#25](https://github.com/rufino-bot/rastru/pull/25)) e está implementado no código: `FiltroDeDemanda`, `useSelecaoNaUrl`, `filtrarFila`, `filtrarTarefas`.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 7**, verificação manual, com a justificativa escrita **antes** no ledger e no relatório (o produto é relatório, não código). **Este plano escreve no livro**: a review das Tasks 1 e 2 lê o diff procurando meio lote gravado, trava fora de ordem e regra de conservação duplicada, além da conformidade com a spec.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com `scripts/review-package BASE HEAD`, nunca com `HEAD~1`.
- **Sem mudança de schema** (spec, seção 1): `specs/02-modelo-de-dados.sql` não muda e não há `db/alter-*.sql`.
- **O livro é só de inclusão** (`CLAUDE.md`, invariantes): o lote só acrescenta linhas, as mesmas que a ação individual acrescentaria (spec, seção 6.4). Nenhum `UPDATE`/`DELETE` em `dbo.Movimentacao`.
- **Uma regra, dois pontos de entrada** (spec, seção 6.2): a validação de iniciar e terminar existe **uma vez**, no núcleo por item. O lote não recalcula saldo, não valida Roteiro e não conta filhos por conta própria.
- **Rotas individuais sem mudança de contrato:** `POST estrutura/{id}/inicios` e `POST estrutura/{id}/terminos` mantêm corpo, status, código e frase. Os testes existentes delas continuam verdes **sem edição**.
- Perfis das rotas novas: `Operador,Administrador` — a constante `PerfisDeEscrita` do `ApontamentoController`, que já existe. Nenhuma linha nova em `web/src/auth/permissoes.ts` (recurso `apontamento`).
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês (`CLAUDE.md`, "Convenções de nomenclatura").
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa, em código e em prosa. Comentário de código **não cita** ledger, brief nem "Task N do plano": o repositório é público. As decisões D1–D12 abaixo podem ser citadas como "desvio Dn do plano 2 dos filtros", como o código já faz com os planos da Fase 3.
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`. Teste de banco novo escopa a asserção nas linhas que ele mesmo criou (`ArvoreDeTesteNoBanco`); contagem global de tabela compartilhada é flaky por construção. Teste de banco que escreve no livro entra na `[Collection(ColecaoQueEscreveEmComponente.Nome)]`, como `CorridaNoIniciarTests`.
- Front: `npm test -- --run` **e** `npm run build`. Primitivas de `web/src/components/` (sem `<button>` cru: "Marcar todos" e "Limpar seleção" são `Botao variante="secundario"`); o checkbox é o `<input type="checkbox" className="size-5 accent-acao">` dentro de `<label>`, como o "Levar" das Tarefas; cores só por token; `// @vitest-environment jsdom` + `afterEach(cleanup)`; mocks por `web/src/testes/api.ts`.
- **Cor de estado nunca decora:** a barra do lote, a dica de trava e a contagem de marcados usam `tinta`/`tinta-fraca`/`superficie`/`borda`, nunca `positivo`, `negativo` ou `atencao`.
- `git pull` antes de todo commit, nos dois repositórios (código e ledger). No ledger, `git add` por caminho explícito, em chamada separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem          # na nuvem: SDK, dockerd, SQL Server, banco com schema e seed (idempotente)
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm ci && npm test -- --run && npm run build
```

**Baseline, medida em 2026-09-30 no commit `6d05c77` (base deste plano, HEAD de `filtros-e-lote-plano-2`), na nuvem:** backend **896** (Api 292 · Application 461 · Infrastructure 143), build 0 warnings, suíte verde na primeira execução; front **952 testes / 62 arquivos**, build ok (só o aviso antigo de chunk > 500 kB). Este plano **não** dá totais absolutos por task: cada implementer mede o delta da própria task e o escreve no relatório.

## Contrato JSON novo (fonte para o front)

camelCase, como o ASP.NET serializa. Nenhum contrato existente muda.

```jsonc
// POST /inicios — "A iniciar aqui" e "Iniciar o pai" (spec, seção 6.1)
{ "setorId": 1, "itens": [ { "estruturaItemId": 7, "quantidade": 4 }, { "estruturaItemId": 2, "quantidade": 1 } ] }
// POST /terminos — "Em trabalho"
{ "setorId": 1, "itens": [ { "estruturaItemId": 7, "ordem": 1, "quantidade": 4 } ] }

// 201: [ MovimentacaoDto, ... ] — um por item, na ordem dos itens. No pai, é o Inicio dele (com montagemId),
//      o mesmo que POST estrutura/{id}/inicios devolve; as baixas dos filhos ficam no livro, como hoje.

// 400 { "erro": "LoteVazio", "mensagem": "..." }          itens ausente ou []
// 400 { "erro": "LoteGrandeDemais", "mensagem": "..." }   mais de 100 itens
// 400 { "erro": "QuantidadeInvalida", "mensagem": "..." } a mesma frase da rota individual
// 400 { "erro": "ItemRepetido", "mensagem": "..." }       mesmo nó (inícios) ou mesmo nó + ordem (términos)
// 404 (sem corpo)                                         Setor inexistente, ou algum nó inexistente
// 409 { "erro": <o código da rota individual>, "mensagem": "..." }  a PRIMEIRA recusa de negócio; nada gravado
```

## Decisões deste plano (onde a spec deixou a escolha)

- **D1 — Falha nunca commita.** Hoje `IExecucaoRepository.EmTransacaoAsync` commita mesmo quando o `trabalho` devolve um `Result` de falha, e isso só é inofensivo porque todo caso de uso da execução valida tudo antes da primeira escrita (é o que o XML doc da interface registra). O lote quebra essa premissa: o núcleo grava o item 1 (inclusive um `SalvarAlteracoesAsync`, porque a baixa dos filhos precisa do Id da `Montagem`) antes de validar o item 2. A interface ganha uma sobrecarga `EmTransacaoAsync(trabalho, confirmar, ct)`, que commita só se `confirmar(resultado)` for verdadeiro e, se não for, desfaz a transação e limpa o change tracker. `Falhas.ExecutarAsync` passa a usá-la com `r => r.Sucesso` para **todo** caso de uso da execução. A regra fica global ("falha não commita") em vez de valer só para o lote, e para os caminhos existentes o comportamento não muda (falha deles não escreve nada). A sobrecarga antiga continua existindo, com `_ => true`, para os testes de Infrastructure que a chamam direto.
- **D2 — Onde o núcleo mora.** Continua no `ApontamentoUseCase`, como dois métodos privados que rodam **dentro** de uma transação já aberta: `IniciarNoAsync` e `TerminarNoAsync`, com o corpo exato das lambdas de hoje. O lote fica na mesma classe (`IniciarEmLote`, `TerminarEmLote`), sem DI nova e sem classe nova. O núcleo devolve a entidade `Movimentacao` e não o DTO: a forma individual projeta um, e o lote projeta todos numa chamada só de `ProjetorDoLivro.ProjetarMovimentacoesAsync`, no fim.
- **D3 — Ordem das travas do lote** (spec da Fase 3, seção 8.1: ordem fixa de aquisição). Antes do primeiro item, o lote lê os filhos de cada item (leitura comum, sem trava), trava **todos** os nós (itens e filhos) numa chamada só de `TravarNosAsync`, que já trava em ordem crescente de Id, e, no Iniciar, trava as linhas de `Pedido` dos itens em ordem crescente de Id, por um método novo (`TravarPedidosDosNosAsync`) que acha o `PedidoId` pela junção `EstruturaItem`×`Agrupamento` **sem ler a linha do Pedido antes do UPDLOCK**, porque um S seguido de U na mesma linha é o deadlock `PK_Pedido` que a Fase 3 mediu. Todo `EstruturaItem` vem antes de todo `Pedido`, a mesma ordem da rota individual (nó, filhos, Pedido). Depois, o núcleo de cada item trava de novo o que precisa, o que não custa nada porque a transação já tem a trava. O Terminar não trava Pedido (não escreve nele), como o individual.
- **D4 — Teto e ordem das recusas de entrada.** `ApontamentoUseCase.TamanhoMaximoDoLote = 100`, o mesmo `TamanhoDePaginaMaximo` que `GET /pedidos` usa. Uma fila de piloto tem dezenas de linhas, então o teto é folgado, e um lote de 100 itens é medido na Task 2 (Review Focus 5). A ordem é fixa, e tudo roda **antes** de abrir a transação: lista vazia → teto → quantidade de cada item → repetido → Setor inexistente (404).
- **D5 — A recusa nomeia o item** (spec, seção 6.3). Três códigos do núcleo já têm frase que nomeia o nó marcado: `SemRoteiro`, `NaoEhOPrimeiroPasso` e `SaldoInsuficiente`. Eles passam **iguais** aos da rota individual. **Toda outra** recusa que o núcleo devolve ganha o prefixo `"{nome}: "`, com o nome da regra 19 (`EstadoDeExecucao.Nome`). Hoje são `PedidoPausado` ("O Pedido PED-01 está pausado."), `PedidoFechado` ("O Pedido deste item…"), `FilhosInsuficientes` e a `QuantidadeInvalida` do produto por filho (as duas últimas nomeiam os filhos, não o pai marcado). A lista é a das que **não** levam prefixo, e não o contrário, para um código novo do núcleo nascer nomeado em vez de anônimo. Para ter o nome, o lote carrega o estado dos nós uma vez, antes do laço (`LeitorDeEstado.CarregarAsync`), e só o usa para isso. O 404 de nó inexistente continua sem corpo, como na rota individual, e o `ConflitoDeConcorrencia` continua com a frase genérica, porque é do lote inteiro.
- **D6 — `ItemRepetido` diz qual.** A frase leva o Id do nó ("O nó 7 aparece mais de uma vez no lote.") e, nos términos, o passo ("O nó 7 no passo 2 aparece mais de uma vez no lote."). O cliente que manda o repetido é o front, que nunca o faz: o Id basta para um erro de programação.
- **D7 — Lote e ação individual não coexistem na tela.** Com pelo menos um item marcado, os botões individuais de Iniciar, Terminar e Iniciar o pai somem da fila inteira (o "Levar" do filho e o "Estornar" continuam). Marcar um item fecha o formulário individual aberto, se houver. A spec (seção 5.6) mantém o botão individual, e ele continua: só não aparece enquanto há lote, porque uma linha com o campo do lote e o formulário individual abertos juntos teria dois "Quantidade" e duas confirmações.
- **D8 — O que acontece com o marcado quando a fila muda.** A chave do lote é a chave que a fila já usa para a ação da linha (`chaveDeIniciar`, `chaveDeTerminar`, `chaveDeIniciarPai`). Linha que **sumiu da resposta inteira** sai da seleção, com o aviso `SAIU_DO_LOTE` (spec, seção 4.5). Linha que continua na resposta mas **ficou bloqueada** (o Pedido foi pausado; o "dá para iniciar" caiu a 0) **continua marcada**, o campo dela mostra o motivo, e o botão da barra fica desabilitado até ela ser desmarcada. É o precedente do `paiSemRoteiro` nas Tarefas, cujo comentário em `erroDaEscolha` registra por quê: tirar em silêncio esconderia o motivo, e travar o checkbox de quem já marcou prenderia a seleção sem saída. Linha **oculta pelo filtro** continua marcada e vai no lote (spec, seção 4.5; o filtro não é "outra pessoa agiu").
- **D9 — Quantidade.** O campo nasce com `quantidadeParaCampo(maximo)`: o saldo da linha, e no pai o `daParaMontar` (spec, seção 2.9). O máximo é relido a cada atualização, e o texto digitado **não** é reescrito: se a atualização baixar o máximo abaixo dele, o campo fica inválido pelo `lerQuantidade` e o botão desabilita.
- **D10 — "Marcar todos" / "Desmarcar todos".** Age só sobre as linhas **visíveis depois do filtro** e **não bloqueadas** da seção (spec, seção 5.3). Vira "Desmarcar todos" quando todas elas estão marcadas, e aí desmarca só elas (marcado oculto pelo filtro continua marcado). Marcar não reescreve o texto de quem já estava marcado. Sem linha visível não bloqueada, o botão não aparece. Nas Tarefas é a mesma regra, sobre todos os grupos visíveis, pulando os `paiSemRoteiro`.
- **D11 — Recusa do lote na tela.** Toda recusa (não só o 409) recarrega a fila, mostra o banner na barra do lote com `mensagemDeErro` e mantém a seleção. A reconciliação da D8 tira dela só o que saiu da resposta. A spec (seção 5.5) diz "a fila recarrega" sem restringir ao 409, e um 404 de lote quer dizer que um nó sumiu, caso em que a recarga é o conserto.
- **D12 — O verbo da barra é o verbo puro.** "Iniciar 5 itens" / "Terminar 1 item", sem a atividade do Setor que o botão da linha usa (`rotuloDaAcao`): "Iniciar solda 5 itens" não se lê. O aviso de ocultos usa a frase que as Tarefas já usam ("1 marcado oculto pelo filtro" / "N marcados ocultos pelo filtro"), e não a variante da seção 4.5 da spec ("3 marcados, 1 oculto pelo filtro"), para as duas telas dizerem o mesmo.

**Minors herdados do Plano 1** (`Fix wave: parked` do ledger dele): entra **só o m-6**, porque os dois testes dele (*"filtrar nao dispara o aviso de outra pessoa ter movido o item"* e *"filtrar nao tira item da selecao nem mostra o aviso de saiu da lista"*) estão nos arquivos que as Tasks 4 e 5 tocam: eles avançam 30 s e nunca afirmam que a recarga houve. m-1 (`useBuscaPaginada.test.tsx`), m-4 e m-5 (teste de acento de Pedidos) ficam **de fora por escopo**: este plano não toca esses arquivos, e a regra do ledger é "na próxima task que tocar esses testes".

## Review Focus

Condições que a spec implica e que um usuário vai encontrar, sem que um teste "óbvio" da spec as exercite. Cada uma tem teste na task dona.

1. **Dois lotes ao mesmo tempo sobre os mesmos nós, em ordem inversa** (dois operadores, "Marcar todos" na mesma fila): um espera o outro ou recebe o 409 limpo, e nenhum dos dois grava meio lote nem passa da quantidade. Teste na Task 2 (`Dois_lotes_em_ordem_inversa_sobre_os_mesmos_nos_nunca_gravam_meio_lote`).
2. **Recusa no segundo item depois de o primeiro ter gravado de verdade no banco**, inclusive a `Montagem` do pai e o `EmProducao` do Pedido: nada fica. O fake não prova isto, só o SQL Server. Teste na Task 2 (`Lote_recusado_no_segundo_item_nao_deixa_nada_do_primeiro`) e, no nível da transação, na Task 1.
3. **Pedido pausado depois de o operador marcar a linha** (outra pessoa pausou, e a atualização periódica traz a linha bloqueada): continua marcada, o motivo aparece no campo, o botão desabilita, e desmarcá-la destrava. Teste na Task 4 (`linha marcada que fica bloqueada continua marcada e trava o botao`).
4. **A atualização periódica baixa o saldo abaixo do que foi digitado** (outro operador iniciou parte): o campo fica inválido e o botão desabilita, sem o texto ser reescrito. Teste na Task 4 (`saldo que cai abaixo do digitado invalida o campo sem reescreve-lo`).
5. **O "Marcar todos" de uma fila cheia chega ao teto**: 100 itens gravam, numa transação só, em tempo que o operador tolera, e 101 recebem 400 antes de abrir a transação. Teste na Task 2 (`Lote_no_teto_grava_os_cem_itens`, com o tempo anotado no relatório) e `Lote_acima_do_teto_da_LoteGrandeDemais_sem_abrir_transacao`.

---

### Task 1: A transação da execução desfaz quando o caso de uso falha (backend)

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (sobrecarga de `EmTransacaoAsync`; o XML doc da original deixa de dizer que falha commita)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Application/Execucao/Falhas.cs` (`ExecutarAsync`)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`
- Test: `tests/Rastreamento.Application.Tests/Execucao/ApontamentoUseCaseTests.cs`

**Interfaces:**
- Produces (Domain): `Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, Func<T, bool> confirmar, CancellationToken ct)`: commita só se `confirmar(resultado)`; senão, `RollbackAsync` e `ChangeTracker.Clear()`, e devolve o resultado mesmo assim. A de dois parâmetros vira `=> EmTransacaoAsync(trabalho, _ => true, ct)`. O retry de 1205 envolve a tentativa inteira, como hoje.
- Produces (fake): `FakeExecucaoRepo.Commits` e `FakeExecucaoRepo.Desfeitas` (`int`). O fake tira um retrato de `Movimentacoes`, `Montagens`, `Pausas` e `StatusDoPedido` ao abrir a transação e o restaura quando `confirmar` recusa. A Task 2 depende disso para provar tudo ou nada no fake.

- [ ] **Step 1: Testes de banco que falham** (`ExecucaoRepositoryTests`, com `ArvoreDeTesteNoBanco`):
  - `Transacao_que_nao_confirma_desfaz_o_que_foi_salvo` — dentro de `EmTransacaoAsync(trabalho, _ => false, ct)`: `Adicionar` um `Inicio` da Peça da árvore e `SalvarAlteracoesAsync`, depois `MarcarPedidoEmProducaoAsync`. Depois dela, num contexto novo: zero `Movimentacao` do nó e o Pedido ainda `Aberto`.
  - `Transacao_que_nao_confirma_limpa_o_change_tracker` — depois dela, `db.ChangeTracker.Entries()` vazio, e um `SalvarAlteracoesAsync` seguinte no mesmo contexto não grava nada.
  - `Transacao_sem_confirmar_continua_commitando` — a sobrecarga de dois parâmetros com o mesmo trabalho deixa a linha e o `EmProducao`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~ExecucaoRepositoryTests"` → não compila (sobrecarga inexistente).
- [ ] **Step 3: Implementar** a sobrecarga no repositório real e no fake, e `Falhas.ExecutarAsync` passando `r => r.Sucesso`. O XML doc da interface troca "um `Result` de falha devolvido de dentro dele commita uma transação sem escrita nenhuma" pela regra nova, com o motivo (o lote grava antes de validar o item seguinte).
- [ ] **Step 4: Testes de ligação que falham** (`ApontamentoUseCaseTests`, no fake):
  - `Iniciar_recusado_desfaz_a_transacao` — `Iniciar` sem Roteiro → `Desfeitas == 1`, `Commits == 0`.
  - `Iniciar_aceito_commita` — `Commits == 1`, `Desfeitas == 0`.
- [ ] **Step 5: Rodar e ver passar.** Os dois filtros acima → PASS; as suítes de Application e Infrastructure inteiras verdes **sem editar teste existente** (a regra nova não muda caminho existente nenhum).
- [ ] **Step 6: Suíte e build.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` → verde.
- [ ] **Step 7: Commit.**

```bash
git add src/ tests/
git commit -m "fix(execucao): transacao desfaz quando o caso de uso devolve falha"
```

---

### Task 2: Lote no backend — núcleo único, `POST /inicios` e `POST /terminos`

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ApontamentoUseCase.cs`
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (DTOs do lote)
- Modify: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs` (`LoteVazio`, `LoteGrandeDemais`, `ItemRepetido`)
- Modify: `src/Rastreamento.Application/Execucao/Falhas.cs` (`Repassar`)
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs` (`TravarPedidosDosNosAsync`)
- Modify: `src/Rastreamento.Api/Controllers/ApontamentoController.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `CenarioDeExecucao.cs`
- Test (novo): `tests/Rastreamento.Application.Tests/Execucao/LoteDeApontamentoTests.cs`
- Test (novo): `tests/Rastreamento.Infrastructure.Tests/Persistence/LoteNoBancoTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs`, `ExecucaoEndpointsTests.Comportamento.cs`, `PerfisDeEscritaDeclaradosTests.cs`

**Interfaces:**
- Consumes: a sobrecarga `EmTransacaoAsync(trabalho, confirmar, ct)` e o rollback do fake (Task 1).
- Produces (Application):

```csharp
public sealed record ItemDeInicioDto(int EstruturaItemId, decimal Quantidade);
public sealed record LoteDeInicioDto(int SetorId, IReadOnlyList<ItemDeInicioDto>? Itens);
public sealed record ItemDeTerminoDto(int EstruturaItemId, int Ordem, decimal Quantidade);
public sealed record LoteDeTerminoDto(int SetorId, IReadOnlyList<ItemDeTerminoDto>? Itens);

// ApontamentoUseCase
public const int TamanhoMaximoDoLote = 100;
public Task<Result<IReadOnlyList<MovimentacaoDto>>> IniciarEmLote(LoteDeInicioDto dto, int usuarioId, CancellationToken ct);
public Task<Result<IReadOnlyList<MovimentacaoDto>>> TerminarEmLote(LoteDeTerminoDto dto, int usuarioId, CancellationToken ct);
private Task<Result<Movimentacao>> IniciarNoAsync(int noId, decimal quantidade, Setor setor, int usuarioId, CancellationToken ct);   // dentro da transação
private Task<Result<Movimentacao>> TerminarNoAsync(int noId, int ordem, decimal quantidade, Setor setor, int usuarioId, CancellationToken ct);

// Falhas
public static Result<TPara> Repassar<TPara, TDe>(Result<TDe> falha, string? detalhe = null);   // mesmo código e tipo; detalhe trocado quando dado
```

- Produces (Domain): `Task<IReadOnlyList<int>> TravarPedidosDosNosAsync(IReadOnlyCollection<int> estruturaItemIds, CancellationToken ct)`, com os PedidoIds distintos pela junção `EstruturaItem`×`Agrupamento`, cada linha de `Pedido` travada com `UPDLOCK, HOLDLOCK, ROWLOCK`, uma a uma, em ordem crescente. Lança fora de transação, como `TravarNosAsync`.
- Produces (fake): `FakeExecucaoRepo.TravasDePedido` (`List<IReadOnlyList<int>>`), no formato de `Travas`.
- Produces (HTTP): `[HttpPost("inicios")]` e `[HttpPost("terminos")]` no `ApontamentoController`, `[Authorize(Roles = PerfisDeEscrita)]`, `Traduzir(..., criado: true)`. O contrato está no cabeçalho.

Frases fixas: `LoteVazio` → "Marque pelo menos um item."; `LoteGrandeDemais` → $"Um lote tem no máximo {TamanhoMaximoDoLote} itens."; `ItemRepetido` → a da D6.

- [ ] **Step 1: Testes que falham — equivalência** (`LoteDeApontamentoTests`, no `CenarioDeExecucao`; o cenário ganha um segundo Pedido, `PedidoId2 = 2` / `AgrupamentoId2 = 2` / "PED-02", e `No(...)` um parâmetro opcional `agrupamento`). O teste central da spec (seção 8) monta **dois cenários idênticos** e compara o livro: `Movimentacoes` projetadas como `(EstruturaItemId, Tipo, Quantidade, Origem, Destino, MontagemId is not null, UsuarioId)` na ordem de gravação, `Montagens` como `(EstruturaItemId, SetorId, Quantidade)`, e `StatusDoPedido`.
  - `Lote_de_inicios_produz_o_mesmo_livro_que_os_mesmos_inicios_um_a_um` — Solda: a Peça 10 a iniciar (Roteiro Solda) e o pai 1 com os filhos 2 (razão 2) e 3 (razão 1) aguardando montagem na Solda. O lote `[10 × 4, 1 × 2]` contra `Iniciar(10, 4)` e depois `Iniciar(1, 2)`.
  - `Lote_de_terminos_produz_o_mesmo_livro_que_os_mesmos_terminos_um_a_um`.
  - `Terminar_o_mesmo_no_em_dois_passos_no_mesmo_lote_vale` — Roteiro Corte, Corte, com saldo nos passos 1 e 2: `[(n, 1, 3), (n, 2, 2)]` → dois Términos, cada um do seu passo.
  - `Resposta_vem_na_ordem_dos_itens` — o `IReadOnlyList<MovimentacaoDto>` na ordem do corpo, não na de Id.
- [ ] **Step 2: Testes que falham — recusas** (mesmo arquivo):
  - `Recusa_no_meio_do_lote_nao_grava_nada` — `[1 × 2, 10 × 99]`: o pai grava `Montagem`, baixas e Início (com um `SalvarAlteracoesAsync` no meio), e a Peça 10 é recusada com `SaldoInsuficiente`. Depois: `Movimentacoes` e `Montagens` iguais às de antes, `StatusDoPedido[PedidoId] == "Aberto"`, `Desfeitas == 1`.
  - `Lote_vazio_da_LoteVazio` — `[Theory]` com `Itens` nulo e `[]` → `Validacao`, `Transacoes == 0`.
  - `Lote_acima_do_teto_da_LoteGrandeDemais_sem_abrir_transacao` — 101 itens → `LoteGrandeDemais`, `Transacoes == 0`. E `Lote_no_teto_nao_e_recusado_pelo_teto` — 100 itens de nós inexistentes → `NaoEncontrado` (não `LoteGrandeDemais`).
  - `Item_repetido_da_ItemRepetido_com_o_no` — inícios `[7, 7]` → `Detalhe == "O nó 7 aparece mais de uma vez no lote."`; términos `[(7, 2), (7, 2)]` → `"O nó 7 no passo 2 aparece mais de uma vez no lote."`; `Transacoes == 0` nos dois.
  - `Quantidade_invalida_em_um_item_da_QuantidadeInvalida_sem_abrir_transacao`.
  - `Setor_inexistente_da_404` e `No_inexistente_no_lote_da_404_sem_gravar`.
  - `Pedido_pausado_no_lote_nomeia_o_item` — `Detalhe == "No 1: O Pedido PED-01 está pausado."` (D5).
  - `Pedido_fechado_no_lote_nomeia_o_item` — `"No 1: O Pedido deste item já foi concluído ou cancelado."`.
  - `Filhos_insuficientes_no_lote_nomeia_o_pai` — `Detalhe` começa com `"No 1: "` e continua com a frase da rota individual.
  - `Recusa_que_ja_nomeia_o_item_tem_a_frase_da_rota_individual` — `[Theory]` com `SemRoteiro`, `NaoEhOPrimeiroPasso` e `SaldoInsuficiente`: código, tipo e `Detalhe` do lote de um item iguais aos de `Iniciar` no mesmo cenário.
- [ ] **Step 3: Teste que falha — travas** (mesmo arquivo). O fake ganha um `Eventos` (`List<string>`) comum a `TravarNosAsync` ("nos:1,2,3"), `TravarPedidosDosNosAsync` ("pedidos:…") e `SalvarAlteracoesAsync` ("save"), com os Ids como chegaram. `Lote_trava_todos_os_nos_e_os_Pedidos_antes_do_primeiro_item` — inícios `[20 (Pedido 2), 1 (pai com os filhos 2 e 3, Pedido 1), 10 (Pedido 1)]`: o primeiro evento é **uma** trava de nós com o conjunto `{1, 2, 3, 10, 20}`, o segundo é **uma** trava de Pedidos com os nós dos itens `{1, 10, 20}`, e só depois vem o primeiro "save". A ordem crescente é do repositório real, que ordena dentro de `TravarNosAsync` (já é assim) e de `TravarPedidosDosNosAsync` (Step 7), e não do fake. E `Terminar_em_lote_nao_trava_Pedido` (nenhum evento "pedidos:").
- [ ] **Step 4: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~LoteDeApontamentoTests"` → não compila.
- [ ] **Step 5: Implementar o caso de uso.** Extrair o corpo das duas lambdas de hoje para `IniciarNoAsync`/`TerminarNoAsync` **sem mudar uma linha da lógica**. `Iniciar`/`Terminar` validam a entrada como hoje e chamam o núcleo dentro de `ExecutarAsync`, projetando o resultado. Os lotes seguem D4 (entrada), D3 (travas), D5 (nomes), e um laço que chama o núcleo item a item e devolve a primeira falha por `Falhas.Repassar`. A D1 desfaz o que os itens anteriores gravaram.
- [ ] **Step 6: Rodar e ver passar.** O filtro acima → PASS; `ApontamentoUseCaseTests` inteiro verde **sem edição** (as rotas individuais não mudaram, inclusive a asserção `Travas` de `Iniciar_leva_de_a_iniciar_para_o_primeiro_passo_e_poe_o_Pedido_em_producao`).
- [ ] **Step 7: Testes de banco que falham.**
  - Em `ExecucaoRepositoryTests`: `TravarPedidosDosNosAsync_devolve_os_Pedidos_distintos_em_ordem_crescente` (duas árvores; nós dos dois Pedidos passados em ordem decrescente, dois nós do mesmo Pedido → os dois Ids, crescentes, sem repetição), `TravarPedidosDosNosAsync_segura_a_linha_do_Pedido` (no molde de `Iniciar_espera_a_trava_do_no_e_o_timeout_vira_ConflitoDeConcorrencia`: com a trava tomada num contexto, o `TravarPedidoAsync` do mesmo Pedido noutro contexto, com `LOCK_TIMEOUT 300`, dá `ConflitoDeConcorrenciaException`) e `TravarPedidosDosNosAsync_fora_de_transacao_lanca`.
  - Em `LoteNoBancoTests` (`: TesteComBanco`, `[Collection(ColecaoQueEscreveEmComponente.Nome)]`, caso de uso com os repositórios reais, como `CorridaNoIniciarTests.CasoDeUso`):
    - `Lote_recusado_no_segundo_item_nao_deixa_nada_do_primeiro` — pai P com um filho presente aguardando montagem no Corte (levado até lá pelos casos de uso reais: `Iniciar`, `Terminar` e `Entregar` do filho; nada de `INSERT` direto no livro), e a Peça Q com saldo 2. `[P × 1, Q × 5]` → `SaldoInsuficiente`. Num contexto novo: nenhuma `Montagem` de P, nenhuma `Movimentacao` de P, do filho nem de Q, e o Pedido ainda `Aberto`. *(Review Focus 2.)*
    - `Dois_lotes_em_ordem_inversa_sobre_os_mesmos_nos_nunca_gravam_meio_lote` — Peças A e B com quantidade 1 cada; dois contextos, `Task.WhenAll` de `IniciarEmLote([A, B])` e `IniciarEmLote([B, A])`. Afirma **exatamente um** `Sucesso`; o outro é `SaldoInsuficiente` ou `ConflitoDeConcorrencia`; A e B com **exatamente um** `Inicio` cada. A propriedade vale haja ou não sobreposição, então o teste não fica intermitente (o mesmo argumento de `CorridaNoIniciarTests`). *(Review Focus 1.)*
    - `Lote_no_teto_grava_os_cem_itens` — 100 Peças da árvore, Roteiro Corte, `[cada × 1]` → `Sucesso`, 100 Inícios. O implementer mede o tempo do `IniciarEmLote` (Stopwatch, três execuções) e o anota no relatório. **Não** é asserção. *(Review Focus 5.)*
- [ ] **Step 8: Implementar `TravarPedidosDosNosAsync` e ver passar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~LoteNoBancoTests|FullyQualifiedName~ExecucaoRepositoryTests"` → PASS.
- [ ] **Step 9: Rotas e testes de API.**
  - `PerfisDeEscritaDeclaradosTests.TabelaAprovada` ganha `["POST inicios"]` e `["POST terminos"]` = `["Operador", "Administrador"]`.
  - `Perfil_sem_a_acao_recebe_403` ganha `("Movimentador", "POST", "/api/inicios")`, `("Gestao", "POST", "/api/inicios")` e `("PCP", "POST", "/api/terminos")`.
  - Em `ExecucaoEndpointsTests.Comportamento.cs`, com o `CenarioDaFase3NaApi` (o PCP troca o Roteiro de C para `[Corte]` antes de qualquer movimento, para B e C começarem no mesmo Setor):
    - `Inicios_em_lote_devolve_201_com_um_movimento_por_item_na_ordem` — `[C × 2, B × 4]` → 201, array de 2 com `estruturaItemId` C e depois B.
    - `Terminos_em_lote_devolve_201`.
    - `Lote_recusado_devolve_409_com_codigo_e_mensagem_e_nao_grava` — `[B × 4, C × 99]` → 409 `SaldoInsuficiente` com `mensagem`; `GET /api/estrutura/{B}/movimentacoes` sem `Inicio`.
    - `Lote_invalido_devolve_400_com_codigo` — `[Theory]`: `{ itens: [] }` → `LoteVazio`; B repetido → `ItemRepetido`; quantidade 0 → `QuantidadeInvalida`.
- [ ] **Step 10: Suíte e build.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` → verde. `npm test -- --run` também: `permissoesEspelhamOBackend.test.ts` lê o `ApontamentoController.cs` e tem de continuar verde sem mudança em `permissoes.ts`.
- [ ] **Step 11: Commit.**

```bash
git add src/ tests/
git commit -m "feat(execucao): POST /inicios e POST /terminos em lote, tudo ou nada, pelo mesmo nucleo da acao individual"
```

---

### Task 3: A lógica do lote e a barra, puras (front)

**Files:**
- Modify: `web/src/api/execucao.ts` (`iniciarEmLote`, `terminarEmLote`, tipos)
- Modify: `web/src/api/erros.ts` (`CodigoDeErroDaExecucao`, `TRADUCAO_DOS_CODIGOS`)
- Create: `web/src/execucao/loteDaFila.ts`, `web/src/execucao/loteDaFila.test.ts`
- Create: `web/src/execucao/BarraDoLote.tsx`, `web/src/execucao/BarraDoLote.test.tsx`
- Test: `web/src/api/execucao.test.ts`, `web/src/api/erros.test.ts`

**Interfaces:**
- Consumes: o contrato JSON do cabeçalho (Task 2); `lerQuantidade`, `quantidadeParaCampo` (`web/src/execucao/quantidade.ts`).
- Produces:

```ts
// execucao.ts — os dois passam pelo `enviar` da execução (avisa o contador de Tarefas, lê { erro, mensagem })
export interface ItemDeInicioEmLote { estruturaItemId: number; quantidade: number }
export interface ItemDeTerminoEmLote { estruturaItemId: number; ordem: number; quantidade: number }
export function iniciarEmLote(setorId: number, itens: ItemDeInicioEmLote[]): Promise<MovimentacaoDto[]>    // POST /inicios
export function terminarEmLote(setorId: number, itens: ItemDeTerminoEmLote[]): Promise<MovimentacaoDto[]>  // POST /terminos

// loteDaFila.ts
export type SecaoDoLote = 'emTrabalho' | 'aIniciar' | 'aguardandoMontagem'
export const TITULO_DA_SECAO: Record<SecaoDoLote, string>   // 'Em trabalho' | 'A iniciar aqui' | 'Aguardando montagem'
export const chaveDeIniciar: (noId: number, ordem: number) => string      // movidas da FilaDoSetorPage, mesmo formato
export const chaveDeTerminar: (noId: number, ordem: number) => string
export const chaveDeIniciarPai: (paiId: number) => string
export const BLOQUEIO_PEDIDO_PAUSADO = 'O Pedido está pausado: nada dele começa até alguém retomá-lo.'
export const BLOQUEIO_SEM_FILHOS = 'Não há filhos suficientes aqui para iniciar.'
export const SAIU_DO_LOTE = 'Um item que você tinha marcado não está mais nesta fila: outra pessoa o moveu. Confira a seleção.'
export interface LinhaDoLote { chave: string; secao: SecaoDoLote; noId: number; ordem: number | null; maximo: number; bloqueio: string | null }
export interface Lote { secao: SecaoDoLote; quantidades: Record<string, string> }   // chave -> texto do campo
export function linhasDoLote(fila: FilaDoSetorDto): LinhaDoLote[]
export function erroDaLinha(linha: LinhaDoLote, texto: string): string | null
export function alternar(lote: Lote | null, linha: LinhaDoLote, marcado: boolean): Lote | null
export function marcarTodos(lote: Lote | null, secao: SecaoDoLote, visiveis: LinhaDoLote[]): Lote | null
export function todosMarcados(lote: Lote | null, secao: SecaoDoLote, visiveis: LinhaDoLote[]): boolean
export function reconciliar(lote: Lote, linhas: LinhaDoLote[]): { lote: Lote | null; saiu: boolean }
export function itensDeInicio(lote: Lote, linhas: LinhaDoLote[]): ItemDeInicioEmLote[]
export function itensDeTermino(lote: Lote, linhas: LinhaDoLote[]): ItemDeTerminoEmLote[]

// BarraDoLote.tsx
export function BarraDoLote(props: {
  secao: SecaoDoLote; marcados: number; ocultos: number; invalido: boolean; enviando: boolean; erro: string | null
  aoEnviar: () => void; aoLimpar: () => void
}): JSX.Element
```

Regras que as funções fixam (spec, seções 5.1 a 5.4; D8 a D10):
- `linhasDoLote`, na ordem das seções da tela e, dentro de cada uma, na ordem da resposta: "Em trabalho" (`maximo` = `quantidade`, sem bloqueio), "A iniciar aqui" (`maximo` = `quantidade`; pausada → `BLOQUEIO_PEDIDO_PAUSADO`) e "Aguardando montagem" **só com `iniciaAqui`** (`noId` = pai, `ordem` = `null`, `maximo` = `daParaMontar`; pai pausado → `BLOQUEIO_PEDIDO_PAUSADO`; `daParaMontar` 0 → `BLOQUEIO_SEM_FILHOS`). Cartão de outro primeiro passo não entra: a ação dele é o "Levar".
- `alternar` marca com `quantidadeParaCampo(maximo)`, recusa (devolve o lote como veio) linha de outra seção que não a do lote e linha bloqueada não marcada, e devolve `null` quando a seleção esvazia (é isso que solta a trava).
- `erroDaLinha`: o `bloqueio`, senão o erro de `lerQuantidade(texto, maximo)`.
- `reconciliar`: tira as chaves que não estão em `linhas` (a resposta **inteira**, não a filtrada) e diz se tirou alguma. Não reescreve texto nenhum (D9).

- [ ] **Step 1: Testes que falham — API e códigos.** `execucao.test.ts`: `iniciarEmLote` e `terminarEmLote` entram no `it.each` *"%s envia o corpo do contrato"*, ao lado de `iniciar`, `terminar` e `entregar` (caminho `/api/inicios` e `/api/terminos`, `POST`, corpo `{ setorId, itens }`); e, no bloco *"o aviso de que o livro mudou"*, `lote aceito avisa quem escuta` (um `iniciarEmLote` e um `terminarEmLote` → o ouvinte chamado duas vezes). Os dois passam pelo `enviar` da execução, então o 409 e a recusa comum já estão cobertos pelos testes desse bloco, que são do `enviar`. `erros.test.ts`: os três códigos novos têm tradução (o `Record` já não compila sem ela, e o teste afirma o texto): `LoteVazio` → "Marque pelo menos um item.", `LoteGrandeDemais` → "O lote passou do máximo de itens. Divida a seleção.", `ItemRepetido` → "Um item foi marcado duas vezes. Atualize a tela e tente de novo."
- [ ] **Step 2: Testes que falham — `loteDaFila.test.ts`** (massa de `web/src/testes/execucao.ts`):
  - `linhas do lote cobrem em trabalho, a iniciar e o pai que inicia aqui, nessa ordem`.
  - `cartao que nao inicia aqui nao entra no lote`.
  - `pausado e da para iniciar zero entram bloqueados com o motivo`.
  - `marcar preenche o saldo e o pai com o da para iniciar`.
  - `marcar em outra secao e marcar bloqueada nao mudam o lote` (a trava de seção).
  - `desmarcar o ultimo devolve null`.
  - `marcar todos marca so as visiveis nao bloqueadas e preserva o texto ja digitado` e `desmarcar todos desmarca so as visiveis` (um marcado fora de `visiveis` continua).
  - `todos marcados ignora as bloqueadas`.
  - `erro da linha e o bloqueio antes da quantidade`.
  - `reconciliar tira o que saiu da resposta e mantem o que ficou bloqueado` (D8).
  - `reconciliar nao reescreve o texto quando o maximo cai` e `erro da linha acusa o texto acima do maximo novo` (D9).
  - `itens de inicio e de termino saem na ordem das linhas com a quantidade lida`.
- [ ] **Step 3: Testes que falham — `BarraDoLote.test.tsx`:**
  - `mostra o verbo da secao e a contagem` — "Iniciar 5 itens" (`aIniciar` e `aguardandoMontagem`), "Terminar 1 item" (`emTrabalho`) (D12).
  - `avisa os marcados ocultos pelo filtro` — `ocultos: 1` → "1 marcado oculto pelo filtro"; `2` → "2 marcados ocultos pelo filtro"; `0` → nada.
  - `botao desabilitado com quantidade invalida`, `mostra a recusa no banner` e `Limpar selecao chama aoLimpar`.
  - `barra nao usa cor de estado` — nenhuma classe `positivo`, `negativo` ou `atencao` fora do `BannerDeErro`.
- [ ] **Step 4: Rodar e ver falhar.** `cd web && npx vitest run src/api/execucao.test.ts src/api/erros.test.ts src/execucao/loteDaFila.test.ts src/execucao/BarraDoLote.test.tsx` → FAIL.
- [ ] **Step 5: Implementar.** As chaves saem da `FilaDoSetorPage` para `loteDaFila.ts`, e a página passa a importá-las (a mudança da página para aí nesta task). A `BarraDoLote` é um `<div>` `sticky bottom-0` com `bg-superficie border-t border-borda`, com o `BannerDeErro`, o `Botao` primário (`carregando`/`rotuloCarregando` "Registrando…"), o aviso de ocultos em `text-tinta-fraca` e o `Botao variante="secundario"` "Limpar seleção".
- [ ] **Step 6: Rodar e ver passar.** Mesmo comando → PASS.
- [ ] **Step 7: Suíte e build.** `npm test -- --run` e `npm run build` → verdes (inclusive as guardas de tema, que varrem os arquivos novos).
- [ ] **Step 8: Commit.**

```bash
git add web/src/
git commit -m "feat(web): logica do lote da fila, barra do lote e cliente de POST /inicios e /terminos"
```

---

### Task 4: O lote na fila do Setor (front)

**Files:**
- Modify: `web/src/pages/FilaDoSetorPage.tsx`
- Test: `web/src/pages/FilaDoSetorPage.test.tsx`

**Interfaces:**
- Consumes: tudo de `loteDaFila.ts`, `BarraDoLote`, `iniciarEmLote`, `terminarEmLote` (Task 3); `filtrarFila` e `useSelecaoNaUrl` (Plano 1).

Regras da tela (spec, seções 5.1 a 5.5; D7, D8, D11):
- O estado da página ganha `lote: Lote | null`, e as linhas do lote são `linhasDoLote(fila)` sobre a resposta **inteira**. A cada resposta nova, `reconciliar`; se saiu algo, o aviso `SAIU_DO_LOTE` sobe no topo, como o `SAIU_DA_FILA` de hoje.
- Checkbox só para quem `apontar`. Fica no `acao` da linha, `<label>` com o texto visível "Marcar" e nome acessível `Marcar {rotuloDoNo(no)}, passo {ordem}, para iniciar` / `… para terminar`; no pai, `Marcar {rotuloDoNo(pai)} para iniciar`. Desabilitado quando a linha é bloqueada e não está marcada, ou quando o lote é de outra seção.
- Linha marcada: o `painel` mostra o `Campo` "Quantidade" (`CLASSES_DE_CONTROLE`, `inputMode="decimal"`), com a dica `erroDaLinha(...)`, senão "Disponível: {maximo}" (no pai, "Dá para iniciar: {maximo}"), e `aria-invalid`. Os pausados de "A iniciar aqui" passam a ter checkbox (bloqueado) e, marcados antes da pausa, o campo.
- Cabeçalho de cada seção com lote (o `Secao` ganha `acao` e `dica` opcionais): `Botao variante="secundario"` "Marcar todos"/"Desmarcar todos" sobre as linhas **visíveis** (as de `filtrarFila`) daquela seção, só para quem `apontar`. Nas seções travadas fica desabilitado, e a `dica` diz `Conclua ou limpe a seleção de ${TITULO_DA_SECAO[lote.secao]}.`
- Com lote: `BarraDoLote` no fim da página, com `ocultos` = marcados cujo nó não aparece na fila filtrada, `invalido` = alguma linha marcada com `erroDaLinha`. Enviar chama `iniciarEmLote` (seções `aIniciar` e `aguardandoMontagem`) ou `terminarEmLote`; sucesso limpa o lote e recarrega; recusa segue a D11. Os botões individuais somem (D7), e marcar fecha o `aberta`.

- [ ] **Step 1: Testes que falham** (`FilaDoSetorPage.test.tsx`; render como os testes de hoje, com o perfil Operador):
  - `mostra o checkbox nas tres secoes que tem lote e nao no cartao que nao inicia aqui`.
  - `quem nao aponta nao ve checkbox nem Marcar todos` (perfil Movimentador).
  - `marcar abre o campo com o saldo e o pai com o da para iniciar`.
  - `marcar numa secao trava as outras com a dica` — marcar em "A iniciar aqui" → os checkboxes e o "Marcar todos" de "Em trabalho" desabilitados, e o texto "Conclua ou limpe a seleção de A iniciar aqui."; desmarcar tudo solta a trava.
  - `Marcar todos marca so as linhas visiveis e habilitadas da secao` — com filtro de Pedido ativo e um pausado na seção: só as visíveis não pausadas ficam marcadas; o botão vira "Desmarcar todos".
  - `a barra mostra o verbo e a contagem` — "Iniciar 2 itens"; em "Em trabalho", "Terminar 1 item".
  - `marcado oculto pelo filtro continua no lote e vai na requisicao` — marcar dois, filtrar escondendo um: "1 marcado oculto pelo filtro", e o corpo do `POST /api/inicios` leva os dois.
  - `enviar o lote manda uma requisicao e recarrega` — corpo `{ setorId: 1, itens: [...] }` na ordem das linhas; depois, a fila recarregada e sem barra.
  - `iniciar o pai em lote vai para /inicios com o pai`.
  - `quantidade invalida desabilita o botao da barra`.
  - `recusa do lote recarrega a fila, mostra o banner e mantem a selecao` — 409 com `mensagem` "No 1: O Pedido PED-01 está pausado." → banner com a frase, a fila buscada de novo, os marcados que continuam na resposta ainda marcados.
  - `item marcado que sai da fila sai da selecao com aviso` — a atualização periódica sem a linha → `SAIU_DO_LOTE` e a contagem da barra cai.
  - `linha marcada que fica bloqueada continua marcada e trava o botao` — a atualização traz a linha pausada: checkbox ainda marcado e habilitado, o campo com `BLOQUEIO_PEDIDO_PAUSADO`, o botão da barra desabilitado; desmarcar destrava. *(Review Focus 3.)*
  - `saldo que cai abaixo do digitado invalida o campo sem reescreve-lo` — digitar "5"; a atualização traz `quantidade: 3` → o campo ainda diz "5", com "No máximo 3.", e o botão desabilitado. *(Review Focus 4.)*
  - `com lote os botoes individuais somem e marcar fecha o formulario aberto` (D7) — abrir "Iniciar" numa linha e marcar outra: o formulário some, e nenhum botão "Iniciar"/"Terminar" de linha fica na tela; o "Estornar" continua.
  - **m-6:** o teste existente *"filtrar nao dispara o aviso de outra pessoa ter movido o item"* passa a afirmar que a recarga aconteceu: o número de chamadas a `/api/setores/1/fila` cresce depois do avanço, e o avanço usa `INTERVALO_DA_EXECUCAO_MS` em vez do literal `30_000`.
- [ ] **Step 2: Rodar e ver falhar.** `npx vitest run src/pages/FilaDoSetorPage.test.tsx` → FAIL nos novos; os existentes continuam verdes.
- [ ] **Step 3: Implementar** as regras acima.
- [ ] **Step 4: Rodar e ver passar.** Mesmo comando → PASS, **com os testes existentes da fila sem edição** fora o m-6 (o fluxo individual não muda sem lote).
- [ ] **Step 5: Suíte e build.** `npm test -- --run` e `npm run build` → verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/
git commit -m "feat(web): iniciar, terminar e iniciar o pai em lote na fila do Setor"
```

---

### Task 5: "Marcar todos" nas Tarefas (front)

**Files:**
- Modify: `web/src/pages/TarefasPage.tsx`
- Test: `web/src/pages/TarefasPage.test.tsx`

**Interfaces:**
- Consumes: `filtrarTarefas` (Plano 1); `quantidadeParaCampo`. Nada da Task 3: o "Levar" já é lote e não muda (spec, seção 5.7).

Regra (D10): um `Botao variante="secundario"` logo abaixo do filtro, só para quem `entregar` e só quando há item visível não bloqueado. Marca todo item **visível** (`filtrarTarefas`) de todos os grupos que não seja `paiSemRoteiro`, com `quantidadeParaCampo(item.quantidade)`, e preserva o texto de quem já estava marcado. Vira "Desmarcar todos" quando todos esses estão marcados, e aí desmarca só eles.

- [ ] **Step 1: Testes que falham** (`TarefasPage.test.tsx`):
  - `Marcar todos marca os itens visiveis de todos os grupos e pula o pai sem Roteiro`.
  - `Marcar todos respeita o filtro` — com filtro de Pedido, só os daquele Pedido; o botão diz "Entregar N itens".
  - `Marcar todos preserva a quantidade ja digitada`.
  - `Desmarcar todos desmarca so os visiveis` — um marcado oculto pelo filtro continua marcado, e "1 marcado oculto pelo filtro" continua na tela.
  - `sem permissao de entregar nao ha Marcar todos`.
  - **m-6:** o teste existente *"filtrar nao tira item da selecao nem mostra o aviso de saiu da lista"* passa a afirmar que a recarga aconteceu (chamadas a `/api/tarefas` crescem) e usa `INTERVALO_DA_EXECUCAO_MS`.
- [ ] **Step 2: Rodar e ver falhar.** `npx vitest run src/pages/TarefasPage.test.tsx` → FAIL nos novos.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar e ver passar.** Mesmo comando → PASS.
- [ ] **Step 5: Suíte e build.** `npm test -- --run` e `npm run build` → verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/
git commit -m "feat(web): Marcar todos nas Tarefas"
```

---

### Task 6: Documentos que mudam junto (com review)

**Files:**
- Modify: `specs/05-api-endpoints.md` (seção "Execução / Rastreamento": `POST /inicios` e `POST /terminos`, com corpo, perfis, `201` na ordem dos itens, os `400` com os três códigos novos, o `404` e a regra "a primeira recusa aborta tudo, com o código da rota individual e a frase nomeando o item"; a tabela de códigos de erro da execução ganha `LoteVazio`, `LoteGrandeDemais`, `ItemRepetido`)
- Modify: `specs/04-fluxos-de-usuario.md` (seção "2. Apontamento em Setor": agir em lote, com checkbox, trava de seção, "Marcar todos", barra, recusa tudo ou nada; o "Marcar todos" das Tarefas)
- Modify: `specs/06-roadmap-mvp.md` (seção "Filtros da demanda e ação em lote": o Plano 2 entregue; a posição **filtros → 1F → 3B** mantida)
- **Não** muda: `specs/03-arquitetura-tecnica.md`. Medido em 2026-09-30 com `grep -n "EmTransacaoAsync\|SERIALIZABLE\|commita" specs/*.md`: nenhuma prosa de spec descreve a transação da execução nem a regra que a D1 troca (as ocorrências são da `SigningKey` e da descrição do Plano 2 no roadmap). A regra nova fica documentada no XML doc de `IExecucaoRepository`, onde a antiga morava (Task 1).

Só a parte do Plano 2 da seção 10 da spec. O revisor **mede cada afirmação** contra o código do HEAD (a documentação vira texto do TCC): nomes de rota, corpo, códigos, status, o teto (100), a ordem das recusas da D4, as frases — e toda contagem que o texto der, com o comando e a data, na forma "medido em … com …" que o `CLAUDE.md` já usa.

- [ ] **Step 1: Escrever** as mudanças acima, citando por nome, sem número de linha.
- [ ] **Step 2: Conferir** cada nome citado com `grep -rn` no HEAD, e o contrato contra os testes de API da Task 2.
- [ ] **Step 3: Rodar** `npm test -- --run` e `dotnet build Rastreamento.slnx -warnaserror`.
- [ ] **Step 4: Commit.**

```bash
git add specs/
git commit -m "docs: acao em lote na fila e nas Tarefas nos specs"
```

---

### Task 7: Verificação manual no celular (dispensa de review escrita antes)

O produto é um **relatório**, não código: é a primeira classe de dispensa do `CLAUDE.md` ("Task cujo produto não é código"). A justificativa vai no ledger **e** no relatório **antes** de a verificação começar.

Feita pelo usuário, no celular, na sessão local (o celular não alcança o container da nuvem). Bancada: da raiz do código, `bash .superpowers/sdd/2026-09-29-filtros-plano-1/prepara-banco-verificacao-filtros.sh --regenerar` e depois `massa-filtros-busca-na-faceta.sql` (mesma pasta; `docker compose cp` + `sqlcmd -b -f 65001 -i`). API e front pelo `.claude/launch.json` (`api`, `web`). O IP do Wi-Fi muda entre religamentos: meça com `ipconfig`. O controlador confere, **antes**, que a massa tem pelo menos uma fila com 3 ou mais linhas em "A iniciar aqui", uma com linhas em "Em trabalho", um cartão de montagem que inicia no Setor com "dá para iniciar" > 0, e um Pedido pausável. O que faltar, acrescenta pela API ou pelas telas, e registra no relatório.

Roteiro:
1. Fila: marcar duas linhas de "A iniciar aqui"; ver o campo com o saldo, a trava das outras seções e a dica; "Iniciar 2 itens"; conferir no livro de cada nó.
2. "Marcar todos" com um filtro ativo: só as visíveis; esconder uma marcada pelo filtro e ver o aviso de oculto; enviar leva as duas.
3. "Em trabalho": "Terminar" em lote, diminuindo a quantidade de uma linha.
4. "Aguardando montagem": iniciar o pai em lote; as baixas dos filhos aparecem no livro, e o estorno continua por linha.
5. Recusa: pausar o Pedido de uma linha marcada (outra aba, como PCP), esperar a atualização e ver a linha bloqueada, marcada e com o botão travado; desmarcar e enviar o resto.
6. Tarefas: "Marcar todos" e "Desmarcar todos" com filtro.
7. Celular em pé: checkbox e botão na mesma linha cabem? A barra do lote tapa a última linha? (Seção 5.6 da spec: se pesar, é aqui que se revê.)

Registro: no relatório da task e no ledger, com as palavras do usuário para cada achado. Achado que vira código entra como task nova no plano, com o gate.
