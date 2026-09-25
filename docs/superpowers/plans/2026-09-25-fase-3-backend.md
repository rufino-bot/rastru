# Fase 3 — plano 2 (backend) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** implementar a Fase 3 no backend — o livro de movimentações, a razão `QuantidadePorPai`, a
calculadora de fila e tarefas, os casos de uso de iniciar, terminar, entregar, montar, estornar e editar
o Roteiro do nó, as leituras (fila, tarefas, posições, livro, Roteiro) e as rotas HTTP com seus perfis.

**Architecture:** o saldo de cada posição sai de uma tabela só de inclusão (`dbo.Movimentacao`), somada
em SQL; uma calculadora pura na `Application` (`CalculadoraDeExecucao`) transforma nós, saldos e totais
montados em fila, tarefas, sobra e "dá para montar", e os casos de uso de escrita validam **com as mesmas
funções**, dentro de uma transação `Serializable` que trava as linhas de `EstruturaItem` em ordem
crescente de Id. Um controller por conjunto de perfis, porque a guarda de espelhamento do front exige
isso (desvio D1).

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10 (Database First), SQL Server 2022 (Docker),
xUnit 2.9; o front só entra em `web/src/auth/` (Vitest).

**Spec:** `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md` — seções 3 a 9, e
a 10.1. Os textos de `specs/01`, `04`, `05` e `06` já foram aplicados pelo plano 1
(`docs/superpowers/plans/2026-09-24-fase-3-docs.md`, mesclado em `80e11a0`). Leia também, na raiz do
ledger, `sdd/fase3-plano2-preparo.md` — os achados da leitura do código que este plano carrega.

**Bancada:** este plano **não roda** no container de nuvem (sem `dotnet`, Docker sem resposta). Precisa
de .NET 10 SDK, Docker com o SQL Server do `docker-compose.yml` e Node 22 (a Task 10 mexe em
`web/src/auth/`). Branch nova a partir da `main` que já tenha este plano mesclado.

**Ordem de merge:** este plano mescla **antes** do plano 3 (front). A última task do plano 3 (gating por
perfil) depende das chaves de `permissoes.ts` que a Task 10 daqui cria.

## Global Constraints

- Nomes de domínio em português, espelhando o DDL (`Movimentacao`, `Montagem`, `EstruturaItem`); nomes
  técnicos em inglês (`Repository`, `UseCase`, `Dto`, `Controller`).
- Database First: `specs/02-modelo-de-dados.sql` muda **antes** do mapeamento EF. Nunca `Add-Migration`
  nem `EnsureCreated`.
- `dotnet build Rastreamento.slnx -warnaserror` termina com **0 warnings** em toda task.
- O livro é **só de inclusão**: nenhum código de `src/` faz `UPDATE` ou `DELETE` em `dbo.Movimentacao`.
  (Testes limpam as próprias linhas — a limpeza de teste não é código de produção.)
- Toda escrita de execução roda em `IExecucaoRepository.EmTransacaoAsync` (Serializable) e começa por
  `TravarNosAsync` (UPDLOCK, Id crescente). 1205/1222 → `ConflitoDeConcorrenciaException` → 409
  `ConflitoDeConcorrencia`.
- Corpo de erro da Execução: `{ "erro": "<código>", "mensagem": "<frase>" }`; `mensagem` omitida quando
  não há frase. 404 sem `erro` (o controller devolve `NotFound()`, precedente de `EstruturaController`; o
  ASP.NET pode pôr um ProblemDetails no corpo). Códigos exatamente os da spec §8.2,
  mais `OrigemInvalida` (desvio D3).
- Perfis sempre com `Administrador` junto.
- Quantidade válida numa escrita de execução: `>= 0,0001`, `<= 99.999.999.999.999,9999` e **no máximo
  4 casas decimais** (`DECIMAL(18,4)`); na montagem, também `N × QuantidadePorPai` de cada filho.
- Mensagens para o operador com acento (é texto de tela); comentários de `src/` sem acento, como o
  código ao redor.
- Citação em comentário e prosa **pelo nome** (teste, método, regra), nunca por `arquivo:NN` nem por
  distância ("acima", "o teste anterior").
- Teste de banco: asserção escopada pelos nós do próprio teste, nunca contagem global; classe que escreve
  em `dbo.Componente` entra em `[Collection(ColecaoQueEscreveEmComponente.Nome)]`.
- A cada task: `dotnet build Rastreamento.slnx -warnaserror` e `dotnet test Rastreamento.slnx` com o
  SQL Server no ar e `db/alter-fase-3.sql` aplicado. Na Task 10 também `cd web && npm test -- --run &&
  npm run build`.

## Desvios da spec decididos neste plano

Cada um está aqui para o revisor não o ler como erro; cada task que o aplica cita a letra.

- **D1 — quatro chaves em `permissoes.ts`, não três (spec §6.4).** A guarda
  `permissoesEspelhamOBackend.test.ts` compara **todo** `[Authorize(Roles)]` de um arquivo de
  `Controllers/` com **uma** entrada da tabela do front. O estorno tem um conjunto de perfis próprio
  (Operador, Movimentador, PCP), então vive num controller próprio, e esse controller precisa de chave:
  `estorno`. As outras três são as da spec (`apontamento`, `entrega`, `roteiro`).
- **D2 — os dois estornos declaram os mesmos perfis.** `POST /montagens/{id}/estorno` declara
  `Operador,Movimentador,PCP,Administrador`, como o de movimentação, porque os dois vivem no mesmo
  controller (D1). O efeito é o do `05`: o Movimentador nunca é autor de uma `Montagem`, então cai no 403
  `Proibido` do caso de uso.
- **D3 — código `OrigemInvalida` (400).** A entrega com `origem.posicao` fora de
  `AguardandoColeta`/`AguardandoMontagem`, ou com `setorId`/`ordem` incoerentes com a posição, não tem
  código na spec. A Task 10 o acrescenta ao contrato do `05`.
- **D4 — migração de banco antigo em arquivo, `db/alter-fase-3.sql`,** e não em linhas `sqlcmd -Q` no
  `CLAUDE.md`: são duas tabelas com dez `CHECK`, ilegíveis numa linha. Idempotente, como os blocos que
  já estão lá.
- **D5 — `DELETE /estrutura/{id}` trava a subárvore inteira**, não só o nó da rota (spec §8.1). Travar
  só o nó deixaria um "iniciar" num **descendente** correr contra o apagar e voltar 500 pela FK.
- **D6 — formato das respostas.** A spec diz "201, o movimento", "as seções da fila"… O formato exato
  está na seção "Contrato JSON" abaixo, e é ele que o plano 3 consome.
- **D7 — Item pronto cujo pai não tem Roteiro entra em Tarefas**, com `destino.paiSemRoteiro = true` e
  `setoresPossiveis` vazio. A spec só diz que a entrega é recusada (`PaiSemRoteiro`) e que a pendência
  aparece ao PCP; esconder o item faria o Movimentador achar que não há nada a levar.
- **D8 — na fila, "Aguardando coleta" mostra só a parte que é tarefa;** a parte que é sobra aparece na
  seção "Sobra". Somadas, as duas dão o saldo `AguardandoColeta` do passo — nenhuma unidade aparece duas
  vezes.

## Review Focus

As cinco entradas que a spec implica e que nenhum teste de exemplo pegaria sozinho; cada uma tem o teste
que a fixa na task dona do código.

1. **O mesmo nó duas vezes na mesma lista de entrega**, com quantidades que, somadas, passam do saldo — a
   lista inteira é recusada (`SaldoInsuficiente`), nunca gravada pela metade nem aceita duas vezes.
   Teste: `Mesmo_no_duas_vezes_na_lista_soma_contra_o_saldo` (Task 6).
2. **Quantidade com mais de quatro casas**, digitada ou produzida por `N × QuantidadePorPai` — 400
   `QuantidadeInvalida`; o banco nunca arredonda um valor que a validação aceitou. Testes:
   `Quantidade_com_mais_de_quatro_casas_e_recusada` e
   `Montar_recusa_quando_N_vezes_a_razao_passa_de_quatro_casas` (Task 5).
3. **Setor repetido no Roteiro** (regra 21: Corte → Dobra → Corte) — destino, tarefa e "é o último
   passo" são pelo **passo** (`Ordem`), nunca pelo Setor. Testes:
   `Destino_segue_o_passo_mesmo_com_setor_repetido` (Task 3) e
   `Terminar_no_Corte_do_passo_um_nao_e_o_ultimo_passo` (Task 5).
4. **Setor inativado com peça dentro** — o que já está nele continua andando (spec §4.6): terminar
   funciona; só o Roteiro novo recusa o Setor inativo. Testes:
   `Terminar_num_Setor_inativado_continua_valendo` (Task 5) e
   `Setor_inativo_ou_inexistente_nao_entra_no_Roteiro` e
   `Setor_inativado_no_trecho_alcancado_continua_no_Roteiro` (Task 8).
5. **Razão editada depois de uma montagem** — a baixa gravada não muda; o que falta e a sobra usam a razão
   nova. Teste: `Razao_editada_depois_da_montagem_so_muda_o_futuro` (Task 3).

---

## Mapa de arquivos

**Novos**
- `db/alter-fase-3.sql` — migração idempotente de banco anterior à Fase 3 (D4).
- `src/Rastreamento.Domain/Entities/Posicoes.cs` — constantes de posição e de tipo de movimento.
- `src/Rastreamento.Domain/Entities/Movimentacao.cs`, `Montagem.cs` — entidades do livro.
- `src/Rastreamento.Domain/Abstractions/SaldoLiquido.cs` — o saldo líquido que o repositório devolve.
- `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` — transação, trava, leituras e escrita
  do livro.
- `src/Rastreamento.Infrastructure/Persistence/Configurations/MovimentacaoConfiguration.cs`,
  `MontagemConfiguration.cs`.
- `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`.
- `src/Rastreamento.Infrastructure/Persistence/ErrosDoSqlServer.cs` — o reconhecimento de 1205/1222,
  extraído de `ReceitaPadraoRepository`.
- `src/Rastreamento.Application/Execucao/` — `Local.cs`, `Livro.cs`, `CalculadoraDeExecucao.cs`,
  `CodigosDaExecucao.cs`, `Quantidades.cs`, `Falhas.cs`, `ExecucaoDtos.cs`, `LeitorDeEstado.cs`,
  `ProjetorDoLivro.cs`, `ApontamentoUseCase.cs`, `EntregaUseCase.cs`, `EstornoUseCase.cs`,
  `RoteiroDoNoUseCase.cs`, `ConsultaDeExecucaoUseCase.cs`.
- `src/Rastreamento.Api/Controllers/ExecucaoControllerBase.cs`, `ApontamentoController.cs`,
  `EntregaController.cs`, `EstornoController.cs`, `RoteiroDoNoController.cs`.
- Testes: `tests/Rastreamento.Application.Tests/Execucao/*`,
  `tests/Rastreamento.Application.Tests/Estrutura/QuantidadePorPaiTests.cs`,
  `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs`,
  `QuantidadePorPaiMapeamentoTests.cs`, `LivroMapeamentoTests.cs`, `ExecucaoRepositoryTests.cs`,
  `CorridaNoIniciarTests.cs`, `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs`,
  `ExecucaoEndpointsTests.Comportamento.cs`, `CenarioDaFase3NaApi.cs`, `CriterioDeProntoDaFase3Tests.cs`.

**Modificados**
- `specs/02-modelo-de-dados.sql`, `db/seed.sql`, `CLAUDE.md`, `specs/05-api-endpoints.md`.
- `src/Rastreamento.Domain/Entities/EstruturaItem.cs`; `IEstruturaRepository.cs` (`NoParaGravar`).
- `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`, `EstruturaRepository.cs`,
  `ReceitaPadraoRepository.cs`, `Configurations/EstruturaItemConfiguration.cs`.
- `src/Rastreamento.Application/Estrutura/` — `PlanejadorDeCopia.cs`, `EstruturaDtos.cs`,
  `MontadorDeArvoreDeEstrutura.cs`, `MontagemDeEstruturaUseCase.cs`;
  `Common/Result.cs` (`TipoDeErro.Proibido`); `Cadastros/CadastroDeSetorUseCase.cs` (comentário).
- `src/Rastreamento.Api/Program.cs`, `Controllers/EstruturaController.cs`.
- `web/src/auth/permissoes.ts`, `permissoes.test.ts`, `permissoesEspelhamOBackend.test.ts`.
- Testes existentes listados em cada task.

## Contrato JSON (D6) — o que o plano 3 consome

Serialização padrão do ASP.NET (camelCase; `DateTime` em horário de Brasília pelo
`HorarioDeBrasiliaJsonConverter`). Os records C# estão em `ExecucaoDtos.cs` (Task 5 cria os de escrita,
Task 9 os de leitura); os nomes abaixo são os campos JSON.

**Local** — `{ "posicao": "AguardandoColeta", "setorId": 3, "setorNome": "Dobra", "ordem": 2 }`.
`setorId`, `setorNome` e `ordem` são `null` quando a posição não os tem (tabela da spec §3.1).
Posições: `AIniciar`, `NoSetor`, `AguardandoColeta`, `AguardandoMontagem`, `NaExpedicao`, `Montado`.

**Movimentação** (`MovimentacaoDto`):
```json
{ "id": 41, "estruturaItemId": 7, "tipo": "Termino", "quantidade": 6.0,
  "origem": { "posicao": "NoSetor", "setorId": 3, "setorNome": "Dobra", "ordem": 2 },
  "destino": { "posicao": "AguardandoColeta", "setorId": 3, "setorNome": "Dobra", "ordem": 2 },
  "montagemId": null, "estornoDeId": null, "dataHora": "2026-09-25T10:14:00-03:00",
  "usuarioId": 12, "usuarioNome": "Operador da Dobra", "estornada": false }
```
`tipo`: `Inicio`, `Termino`, `Entrega`, `Montagem`, `Estorno`.

**Montagem** (`MontagemDto`):
```json
{ "id": 5, "estruturaItemId": 2, "setorId": 4, "setorNome": "Solda", "quantidade": 3.0,
  "dataHora": "…", "usuarioId": 12, "usuarioNome": "…", "estornada": false,
  "baixas": [ /* MovimentacaoDto de tipo Montagem, uma por filho direto */ ] }
```

**Escrita**

| Rota | Corpo | Sucesso |
|---|---|---|
| `POST /estrutura/{id}/inicios` | `{ "setorId": 1, "quantidade": 4 }` | 201, `MovimentacaoDto` |
| `POST /estrutura/{id}/terminos` | `{ "setorId": 1, "ordem": 1, "quantidade": 4 }` | 201, `MovimentacaoDto` |
| `POST /estrutura/{id}/montagens` | `{ "setorId": 4, "quantidade": 2 }` | 201, `MontagemDto` |
| `POST /entregas` | `{ "itens": [ { "estruturaItemId": 7, "origem": { "posicao": "AguardandoColeta", "setorId": 1, "ordem": 1 }, "destinoSetorId": null, "quantidade": 4 } ] }` | 201, `MovimentacaoDto[]` na ordem da lista |
| `POST /movimentacoes/{id}/estorno` | — | 201, `MovimentacaoDto` (o estorno) |
| `POST /montagens/{id}/estorno` | — | 201, `MovimentacaoDto[]` (um estorno por baixa) |
| `PUT /estrutura/{id}/roteiro` | `{ "passos": [1, 3, 1] }` (SetorIds em ordem) | 200, `RoteiroDoNoDto` |

**Leitura**

`GET /estrutura/{id}/roteiro` → `RoteiroDoNoDto`:
```json
{ "estruturaItemId": 7, "passos": [ { "setorId": 1, "nome": "Corte", "ordem": 1, "alcancado": true } ] }
```

`GET /estrutura/{id}/movimentacoes` → `LivroDoNoDto`:
```json
{ "movimentacoes": [ /* MovimentacaoDto do nó, por Id */ ],
  "montagens": [ /* MontagemDto em que o nó é o PAI montado, por Id */ ] }
```

`GET /agrupamentos/{id}/posicoes` → `PosicoesDoNoDto[]`, um por nó do Agrupamento, por Id:
```json
[ { "estruturaItemId": 7,
    "saldos": [ { "posicao": "AIniciar", "setorId": null, "setorNome": null, "ordem": null, "quantidade": 4 },
                { "posicao": "NoSetor", "setorId": 1, "setorNome": "Corte", "ordem": 1, "quantidade": 6 } ],
    "totalMontado": null } ]
```
`saldos` só traz posição com quantidade diferente de zero, na ordem `AIniciar`, `NoSetor`,
`AguardandoColeta`, `AguardandoMontagem`, `NaExpedicao`, `Montado`, e dentro dela por `ordem` e
`setorId`. `totalMontado` é número nos nós com filhos (0 inclusive) e `null` nos sem filhos.

**Nó resumido** (`NoResumoDto`), usado pela fila e pelas tarefas:
```json
{ "id": 7, "descricao": "Suporte", "codigoDoComponente": "SUP-01",
  "pedidoId": 1, "pedidoNumero": "PED-2026-01", "agrupamentoId": 3, "agrupamentoCodigo": "AG-01",
  "paiId": 2, "paiDescricao": "Chassi" }
```
`descricao` já com o fallback do Componente (regra 19). Na Peça, `paiId` e `paiDescricao` são `null`.
O front monta o caminho "Pedido › Agrupamento › pai" com esses campos.

**Destino** (`DestinoDto`):
```json
{ "tipo": "Montagem", "setorId": null, "setorNome": null, "ordem": null,
  "paiId": 2, "sugestaoSetorId": 4,
  "setoresPossiveis": [ { "id": 4, "nome": "Solda" }, { "id": 6, "nome": "Montagem final" } ],
  "paiSemRoteiro": false }
```
`tipo`: `ProximoPasso` (com `setorId`, `setorNome`, `ordem` do próximo passo), `Expedicao` (tudo `null`,
lista vazia) ou `Montagem` (com `paiId`, `sugestaoSetorId` — pode ser `null` — e `setoresPossiveis`, os
Setores distintos do Roteiro do pai em ordem de passo). `paiSemRoteiro` só é `true` em `Montagem` com a
lista vazia (D7).

`GET /setores/{id}/fila` → `FilaDoSetorDto`:
```json
{ "setorId": 1, "setorNome": "Corte",
  "aIniciar":   [ { "no": NoResumoDto, "ordem": 1, "quantidade": 10 } ],
  "emTrabalho": [ { "no": NoResumoDto, "ordem": 1, "quantidade": 4 } ],
  "aguardandoColeta": [ { "no": NoResumoDto, "ordem": 1, "quantidade": 4, "destino": DestinoDto } ],
  "aguardandoMontagem": [ {
      "pai": NoResumoDto, "faltaMontar": 10, "daParaMontar": 2,
      "filhos": [ { "no": NoResumoDto, "quantidadePorPai": 4, "presente": 9,
                    "necessarioParaProxima": 12, "faltaParaProxima": 3 } ] } ],
  "sobra": [ { "no": NoResumoDto, "origem": "UltimoPasso", "ordem": 2, "quantidade": 5,
               "emMaisDeUmSetor": false } ] }
```
- `aIniciar`: nós cujo **primeiro** passo é este Setor, com saldo `AIniciar` > 0; `ordem` é a do
  primeiro passo.
- `emTrabalho`: saldo `NoSetor` neste Setor, uma linha por (nó, passo).
- `aguardandoColeta`: por (nó, passo), só a parte que é **tarefa** (D8); linha com tarefa zero não sai.
- `aguardandoMontagem`: um grupo por pai que tem algum filho com saldo `AguardandoMontagem` neste Setor;
  `filhos` traz **todos** os filhos diretos do pai (inclusive os ausentes, com `presente` 0).
  `necessarioParaProxima` e `faltaParaProxima` são `null` quando não há próxima unidade a montar
  (`daParaMontar + 1 > faltaMontar`).
- `sobra`: `origem` `UltimoPasso` (a parte do `AguardandoColeta` do último passo que não é tarefa; tem
  `ordem`) ou `Montagem` (o excesso em `AguardandoMontagem`, no nível do nó; `ordem` `null`;
  `emMaisDeUmSetor` `true` quando o filho aguarda montagem em mais de um Setor — a tela diz que não dá
  para saber em qual está a unidade a mais).
- Todas as listas na ordem de Id do nó e, dentro dele, de `ordem`.

`GET /tarefas` → `TarefasDoSetorDto[]`, um por Setor de origem com alguma tarefa, por `setorId`:
```json
[ { "setorId": 1, "setorNome": "Corte",
    "itens": [ { "no": NoResumoDto, "ordem": 1, "quantidade": 4, "destino": DestinoDto } ] } ]
```
`GET /tarefas/contagem` → `{ "total": 3 }` — o número de itens de `GET /tarefas` (cada par nó+passo
conta um).

Escopo de fila e tarefas: nós de Pedidos que **não** estão `Concluido` nem `Cancelado`.

**Erros** — ver "Global Constraints". Status por código: 400 `QuantidadeInvalida`, `DestinoIndevido`,
`EntregaVazia`, `RoteiroInvalido`, `OrigemInvalida`; 403 `Proibido`; 404 sem `erro`; 409 os demais.

---

### Task 1: `QuantidadePorPai` na superfície da Fase 2 (regra 26)

A coluna nova tem `CHECK` que recusa Item sem razão — então ela entra **junto** com tudo que grava Item
(cópia da receita, filho acrescentado à mão, edição do nó), senão a suíte existente quebra na primeira
linha. Esta task também põe `semRoteiro` em cada nó da árvore (spec §5.3).

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `dbo.EstruturaItem`)
- Create: `db/alter-fase-3.sql`
- Modify: `CLAUDE.md` (seção "Comandos", bloco novo antes de "O schema **não** é criado pelo EF")
- Modify: `src/Rastreamento.Domain/Entities/EstruturaItem.cs`
- Modify: `src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs` (`NoParaGravar`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/Configurations/EstruturaItemConfiguration.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs` (`GravarNo`)
- Modify: `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`
- Modify: `src/Rastreamento.Application/Estrutura/EstruturaDtos.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontadorDeArvoreDeEstrutura.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs`
- Modify: `tests/Rastreamento.Application.Tests/Estrutura/Fakes.cs` (`FakeEstruturaRepo.GravarNo`)
- Modify (testes existentes que criam Item): `tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs`,
  `CriarPecaTests.cs`; `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaItemMapeamentoTests.cs`,
  `EstruturaRepositoryTests.cs`; `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Estrutura/QuantidadePorPaiTests.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/QuantidadePorPaiMapeamentoTests.cs`

**Interfaces:**
- Produces: `EstruturaItem.QuantidadePorPai : decimal?`; `NoParaGravar(..., decimal? QuantidadePorPai = null)`
  (parâmetro opcional no **fim**); `NoPlanejado(..., decimal? QuantidadePorPai = null)`;
  `NovoFilhoDto(int? ComponenteId, string? Descricao, decimal Quantidade, decimal? QuantidadePorPai = null)`;
  `EdicaoDeNoDto(string? Descricao, decimal Quantidade, decimal? QuantidadePorPai = null)`;
  `EstruturaItemDto(..., IReadOnlyList<EstruturaItemDto> Filhos, decimal? QuantidadePorPai, bool SemRoteiro)`;
  `ArvoreDeTesteNoBanco` (helper de teste de Infra, estendido na Task 2).

- [ ] **Step 1: Schema — coluna e `CHECK` em `specs/02-modelo-de-dados.sql`**

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
    Quantidade                  DECIMAL(18,4)       NOT NULL,   -- lote agregado (divisível por quantidades livres)
````

por:

````markdown
    Quantidade                  DECIMAL(18,4)       NOT NULL,   -- lote agregado (divisível por quantidades livres)
    QuantidadePorPai            DECIMAL(18,4)       NULL,       -- regra 26: quantos entram em UMA unidade do pai (só Item)
````

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
    CONSTRAINT CK_EstruturaItem_PecaTemComponente
        CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)
);
````

por:

````markdown
    CONSTRAINT CK_EstruturaItem_PecaTemComponente
        CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL),
    -- Regra 26 (Fase 3): a montagem baixa N x QuantidadePorPai de cada filho. Peça não tem pai.
    CONSTRAINT CK_EstruturaItem_QuantidadePorPai
        CHECK ((NivelHierarquico = 'Peca' AND QuantidadePorPai IS NULL)
            OR (NivelHierarquico = 'Item' AND QuantidadePorPai IS NOT NULL AND QuantidadePorPai > 0))
);
````

- [ ] **Step 2: Migração de banco antigo — criar `db/alter-fase-3.sql` (D4)**

```sql
-- Migracao idempotente da Fase 3 para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. EstruturaItem.QuantidadePorPai (regra 26) ------------------------------------------------ */
IF COL_LENGTH('dbo.EstruturaItem', 'QuantidadePorPai') IS NULL
    ALTER TABLE dbo.EstruturaItem ADD QuantidadePorPai DECIMAL(18,4) NULL;
GO

-- Preenche os Itens que ja existiam com Quantidade / Quantidade do pai (spec da Fase 3, secao 3.4).
-- Aproximacao aceita: o banco de dev e descartavel (autorizacao do dono do projeto, 2026-08-17) e
-- nao ha banco de producao. O piso de 0,0001 impede o CHECK abaixo de recusar uma razao que o
-- arredondamento levaria a zero.
UPDATE f
   SET f.QuantidadePorPai = CASE WHEN f.Quantidade / p.Quantidade < 0.0001 THEN 0.0001
                                 ELSE CAST(f.Quantidade / p.Quantidade AS DECIMAL(18,4)) END
  FROM dbo.EstruturaItem f
  JOIN dbo.EstruturaItem p ON p.Id = f.EstruturaPaiId
 WHERE f.NivelHierarquico = 'Item' AND f.QuantidadePorPai IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EstruturaItem_QuantidadePorPai')
    ALTER TABLE dbo.EstruturaItem ADD CONSTRAINT CK_EstruturaItem_QuantidadePorPai
        CHECK ((NivelHierarquico = 'Peca' AND QuantidadePorPai IS NULL)
            OR (NivelHierarquico = 'Item' AND QuantidadePorPai IS NOT NULL AND QuantidadePorPai > 0));
GO
```

- [ ] **Step 3: `CLAUDE.md` — como aplicar a migração**

Trocar em `CLAUDE.md`:

````markdown
O schema **não** é criado pelo EF (nada de `Add-Migration`/`EnsureCreated`): é Database
First, o `.sql` é a fonte de verdade.
````

por:

````markdown
**Fase 3 — `db/alter-fase-3.sql`.** Daqui em diante a migração de banco anterior vive num arquivo
idempotente versionado, e não em linhas de `sqlcmd -Q` como os blocos acima: são tabelas inteiras com
vários `CHECK`, e uma linha só com elas seria impossível de revisar. Ele leva um banco anterior à Fase 3
até o `02-modelo-de-dados.sql` — hoje, a coluna `EstruturaItem.QuantidadePorPai`, com o preenchimento
da seção 3.4 da spec da Fase 3. Rodar de novo não muda nada. `-b` aborta no primeiro erro, e `-f 65001`
pelo mesmo motivo do `seed-demo.sql`:

```bash
MSYS_NO_PATHCONV=1 docker compose cp db/alter-fase-3.sql sqlserver:/tmp/alter-fase-3.sql
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/alter-fase-3.sql
```

O schema **não** é criado pelo EF (nada de `Add-Migration`/`EnsureCreated`): é Database
First, o `.sql` é a fonte de verdade.
````

- [ ] **Step 4: Aplicar a migração no banco da bancada e conferir**

Run: os dois comandos do bloco acima, e depois
`MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -Q "SELECT COL_LENGTH('dbo.EstruturaItem','QuantidadePorPai') AS Coluna, (SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_EstruturaItem_QuantidadePorPai') AS Check_"`
Expected: `Coluna` = 9, `Check_` = 1. Rode o arquivo **uma segunda vez**: termina sem erro (idempotente).

- [ ] **Step 5: Entidade e mapeamento**

Em `src/Rastreamento.Domain/Entities/EstruturaItem.cs`, depois da propriedade `Quantidade`:

```csharp
  /// <summary>
  /// Regra 26: quantas unidades deste no entram em UMA unidade do pai. NULL na Peca, obrigatoria e
  /// positiva no Item (`CK_EstruturaItem_QuantidadePorPai`). A montagem baixa `N x QuantidadePorPai`
  /// de cada filho (Fase 3); editar a razao nao reescreve baixa ja gravada.
  /// </summary>
  public decimal? QuantidadePorPai { get; set; }
```

Em `EstruturaItemConfiguration.Configure`, depois da linha de `Quantidade`:

```csharp
    b.Property(x => x.QuantidadePorPai).HasPrecision(18, 4);
```

- [ ] **Step 6: Escrever os testes de Application que falham**

Criar `tests/Rastreamento.Application.Tests/Estrutura/QuantidadePorPaiTests.cs`:

```csharp
using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// Regra 26 na superficie da Fase 2 (plano 2 da Fase 3, Task 1): a razao nasce na copia da receita,
/// e obrigatoria no Item acrescentado ou editado a mao, proibida na Peca; e a arvore marca
/// `SemRoteiro` (spec da Fase 3, secao 5.3).
/// </summary>
public class QuantidadePorPaiTests
{
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas) Montar()
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentos = new FakeAgrupamentoRepo(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Avulso" });
    var catalogo = new FakeReceitaPadraoRepo();
    catalogo.Componentes.Add(new Componente
    {
      Id = 1, Codigo = "CHS", Descricao = "Chassi", Tipo = "Montagem", Ativo = true, ArquivoSolidoId = 701,
    });
    catalogo.Componentes.Add(new Componente { Id = 2, Codigo = "SUP", Descricao = "Suporte", Tipo = "Fabricado", Ativo = true });
    catalogo.Componentes.Add(new Componente { Id = 3, Codigo = "PAR", Descricao = "Parafuso", Tipo = "Fabricado", Ativo = true });
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo, new FakePedidoRepo());
    return (useCase, estruturas);
  }

  private static EstruturaItem Gravado(FakeEstruturaRepo estruturas, int id) =>
      estruturas.Itens.Single(i => i.Id == id);

  [Fact]
  public async Task Copia_da_receita_grava_a_razao_de_cada_filho_e_nenhuma_na_Peca()
  {
    var (useCase, estruturas) = Montar();
    estruturas.ReceitaFilhos.Add((1, 2, 4m));     // um Chassi leva 4 Suportes
    estruturas.ReceitaFilhos.Add((2, 3, 2.5m));   // um Suporte leva 2,5 Parafusos

    var r = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Null(r.Valor!.QuantidadePorPai);
    var suporte = Assert.Single(r.Valor.Filhos);
    Assert.Equal(4m, suporte.QuantidadePorPai);
    Assert.Equal(40m, suporte.Quantidade);
    Assert.Equal(2.5m, Assert.Single(suporte.Filhos).QuantidadePorPai);
    Assert.Null(Gravado(estruturas, r.Valor.Id).QuantidadePorPai);
    Assert.Equal(4m, Gravado(estruturas, suporte.Id).QuantidadePorPai);
  }

  [Fact]
  public async Task Filho_ad_hoc_grava_a_razao_informada()
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(null, "Calço", 20m, QuantidadePorPai: 2m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(2m, r.Valor!.QuantidadePorPai);
    Assert.Equal(2m, Gravado(estruturas, r.Valor.Id).QuantidadePorPai);
  }

  [Fact]
  public async Task Filho_de_Componente_usa_a_razao_do_corpo_no_topo_e_a_da_receita_abaixo()
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);
    // Receita semeada DEPOIS da Peca: ela nasce sem filhos, e so o filho acrescentado copia a receita.
    estruturas.ReceitaFilhos.Add((2, 3, 2.5m));

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(2, null, 30m, QuantidadePorPai: 3m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(3m, r.Valor!.QuantidadePorPai);
    Assert.Equal(2.5m, Assert.Single(r.Valor.Filhos).QuantidadePorPai);
    Assert.Equal(75m, r.Valor.Filhos[0].Quantidade);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00001")]
  public async Task Filho_sem_razao_valida_e_recusado_e_nada_e_gravado(string? razao)
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);
    decimal? valor = razao is null ? null : decimal.Parse(razao, CultureInfo.InvariantCulture);

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(null, "Calço", 20m, QuantidadePorPai: valor), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(1, estruturas.GravacoesDeArvore);   // so a Peca
  }

  [Fact]
  public async Task Editar_Item_troca_a_razao()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 40m, QuantidadePorPai = 4m,
    });

    var r = await useCase.EditarNo(2, new EdicaoDeNoDto(null, 50m, QuantidadePorPai: 5m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(5m, Gravado(estruturas, 2).QuantidadePorPai);
    Assert.Equal(5m, r.Valor!.QuantidadePorPai);
  }

  [Fact]
  public async Task Editar_Item_sem_razao_e_recusado()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 40m, QuantidadePorPai = 4m,
    });

    var r = await useCase.EditarNo(2, new EdicaoDeNoDto(null, 50m), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(4m, Gravado(estruturas, 2).QuantidadePorPai);
    Assert.Equal(0, estruturas.Saves);
  }

  [Fact]
  public async Task Editar_Peca_com_razao_e_recusado()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });

    var r = await useCase.EditarNo(1, new EdicaoDeNoDto(null, 10m, QuantidadePorPai: 1m), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(0, estruturas.Saves);
  }

  [Fact]
  public async Task Arvore_marca_sem_Roteiro_so_o_no_que_nao_tem_passo()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, Descricao = "Calço", EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 20m, QuantidadePorPai = 2m,
    });
    estruturas.Roteiros.Add(new EstruturaRoteiro { Id = 90, EstruturaItemId = 1, SetorId = 5, Ordem = 1 });

    var r = await useCase.ObterArvore(1, CancellationToken.None);

    var raiz = Assert.Single(r.Valor!);
    Assert.False(raiz.SemRoteiro);
    Assert.True(Assert.Single(raiz.Filhos).SemRoteiro);
  }
}
```

- [ ] **Step 7: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~QuantidadePorPaiTests"`
Expected: FAIL na compilação — `NovoFilhoDto` e `EdicaoDeNoDto` não têm `QuantidadePorPai`,
`EstruturaItemDto` não tem `QuantidadePorPai` nem `SemRoteiro`.

- [ ] **Step 8: Records da fronteira**

Em `src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs`, o `NoParaGravar` passa a ser:

```csharp
/// <summary>
/// Espelho de `NoPlanejado` na fronteira do dominio, para a Application nao vazar tipo.
/// `QuantidadePorPai` e opcional e fica no FIM de proposito: a raiz de uma Peca nao tem razao, e
/// assim as construcoes posicionais que ja existiam continuam compilando.
/// </summary>
public sealed record NoParaGravar(
    int? ComponenteId, string? Descricao, decimal Quantidade, bool RequerRelatorioDimensional,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoParaGravar> Filhos,
    decimal? QuantidadePorPai = null);
```

Em `src/Rastreamento.Application/Estrutura/EstruturaDtos.cs`:

```csharp
/// <summary>
/// `Descricao` ja vem RESOLVIDA: `EstruturaItem.Descricao` quando nao-nula, senao a descricao do
/// `Componente` (regra 19). O front nao faz esse fallback — se fizesse, cada consumidor novo teria
/// de lembrar dele. `QuantidadePorPai` e nula na Peca (regra 26); `SemRoteiro` e a pendencia que o
/// PCP ve (regra 28): sem passo, o no nunca tem a primeira entrada.
/// </summary>
public sealed record EstruturaItemDto(
    int Id, int? ComponenteId, string? CodigoDoComponente, string Descricao,
    decimal Quantidade, string NivelHierarquico, bool RequerRelatorioDimensional,
    IReadOnlyList<MaterialDoNoDto> Materiais, IReadOnlyList<PassoDoRoteiroDto> Roteiro,
    IReadOnlyList<EstruturaItemDto> Filhos, decimal? QuantidadePorPai, bool SemRoteiro);
```

e, no mesmo arquivo, os dois DTOs de entrada ganham o parâmetro opcional no fim (os XML docs que já
existem ficam, com uma frase a mais: "`QuantidadePorPai` obrigatória no Item e proibida na Peça (regra
26)"):

```csharp
public sealed record NovoFilhoDto(int? ComponenteId, string? Descricao, decimal Quantidade, decimal? QuantidadePorPai = null);

public sealed record EdicaoDeNoDto(string? Descricao, decimal Quantidade, decimal? QuantidadePorPai = null);
```

- [ ] **Step 9: A razão desce na cópia da receita**

Em `PlanejadorDeCopia.cs`, o `NoPlanejado` ganha o parâmetro no fim:

```csharp
public sealed record NoPlanejado(
    int? ComponenteId,
    string? Descricao,
    decimal Quantidade,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoPlanejado> Filhos,
    decimal? QuantidadePorPai = null);
```

`Descer` recebe a razão da aresta que levou até o nó. Trocar em `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`:

````markdown
      var raiz = Descer(receita, componenteRaizId, quantidadeDaRaiz, caminho, noCaminho, ref nos);
````

por:

````markdown
      // A raiz nao tem aresta de pai: razao nula. Quem pendura a raiz sob um no (AcrescentarFilho)
      // poe a razao informada no corpo por cima (regra 26).
      var raiz = Descer(receita, componenteRaizId, quantidadeDaRaiz, null, caminho, noCaminho, ref nos);
````

Trocar em `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`:

````markdown
      int componenteId,
      decimal quantidade,
      List<int> caminho,
````

por:

````markdown
      int componenteId,
      decimal quantidade,
      decimal? quantidadePorPai,
      List<int> caminho,
````

Trocar em `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`:

````markdown
      filhos.Add(Descer(receita, f.FilhoId, quantidade * f.QuantidadePadrao, caminho, noCaminho, ref nos));
````

por:

````markdown
      filhos.Add(Descer(
          receita, f.FilhoId, quantidade * f.QuantidadePadrao, f.QuantidadePadrao, caminho, noCaminho, ref nos));
````

Trocar em `src/Rastreamento.Application/Estrutura/PlanejadorDeCopia.cs`:

````markdown
            .OrderBy(r => r.Ordem).Select(r => (r.SetorId, r.Ordem)).ToList(),
        Filhos: filhos);
````

por:

````markdown
            .OrderBy(r => r.Ordem).Select(r => (r.SetorId, r.Ordem)).ToList(),
        Filhos: filhos,
        QuantidadePorPai: quantidadePorPai);
````

- [ ] **Step 10: Gravar e ler a razão**

Em `EstruturaRepository.GravarNo` e em `FakeEstruturaRepo.GravarNo`
(`tests/Rastreamento.Application.Tests/Estrutura/Fakes.cs`), no inicializador do `EstruturaItem`, depois
de `Quantidade = no.Quantidade,`:

```csharp
      QuantidadePorPai = no.QuantidadePorPai,
```

Em `MontadorDeArvoreDeEstrutura.MontarAsync`, o `return new EstruturaItemDto(...)` passa a ser:

```csharp
      return new EstruturaItemDto(
          item.Id, item.ComponenteId, codigo, descricao, item.Quantidade,
          item.NivelHierarquico, item.RequerRelatorioDimensional, listaDeMateriais, listaDeRoteiro, filhos,
          item.QuantidadePorPai, SemRoteiro: listaDeRoteiro.Count == 0);
```

- [ ] **Step 11: Validação no caso de uso**

Em `MontagemDeEstruturaUseCase`, junto das outras constantes de erro:

```csharp
  /// <summary>
  /// Regra 26: todo Item tem razao, e ela cabe na coluna `DECIMAL(18,4)` — mesmo piso e mesmo teto da
  /// quantidade, pelo mesmo motivo (abaixo do piso a coluna arredonda para zero, e o CHECK recusa).
  /// Frase no `erro`, como as outras validacoes deste caso de uso (contrato de erro da Estrutura).
  /// </summary>
  private const string ErroDeRazaoInvalida =
      "Quantidade por pai e obrigatoria num Item e deve ficar entre 0,0001 e o maximo da coluna (regra 26).";

  private const string ErroDeRazaoNaPeca =
      "Peca nao tem pai: quantidade por pai so se informa num Item (regra 26).";

  private static bool RazaoValida(decimal? razao) =>
      razao is decimal r
      && r >= PlanejadorDeCopia.QuantidadeMinimaDaColuna
      && r <= PlanejadorDeCopia.QuantidadeMaximaDaColuna;
```

Em `AcrescentarFilho`, logo depois da checagem de `novo.Quantidade`:

```csharp
    if (!RazaoValida(novo.QuantidadePorPai))
      return Result<EstruturaItemDto>.Falha(ErroDeRazaoInvalida, TipoDeErro.Validacao);
```

No mesmo método, no ramo de Componente, logo depois de
`paraGravar = ConverterParaGravar(plano!.Raiz!, ehRaiz: false, requerRelatorioDaRaiz: false);`:

```csharp
      // O topo do filho acrescentado usa a razao do CORPO; os nos abaixo dele ja trazem a da receita.
      paraGravar = paraGravar with { QuantidadePorPai = novo.QuantidadePorPai };
```

e, no ramo ad-hoc, o `new NoParaGravar(...)` ganha `QuantidadePorPai: novo.QuantidadePorPai` depois de
`Filhos: []`.

`ConverterParaGravar` passa a levar a razão:

```csharp
  private static NoParaGravar ConverterParaGravar(NoPlanejado no, bool ehRaiz, bool requerRelatorioDaRaiz) =>
      new(
          ComponenteId: no.ComponenteId,
          Descricao: no.Descricao,
          Quantidade: no.Quantidade,
          RequerRelatorioDimensional: ehRaiz && requerRelatorioDaRaiz,
          Materiais: no.Materiais,
          Roteiro: no.Roteiro,
          Filhos: no.Filhos.Select(f => ConverterParaGravar(f, ehRaiz: false, requerRelatorioDaRaiz)).ToList(),
          QuantidadePorPai: no.QuantidadePorPai);
```

Em `EditarNo`, logo depois do `if (no is null) return ...NaoEncontrado`:

```csharp
    // Regra 26: a razao acompanha o nivel do no — obrigatoria no Item, proibida na Peca.
    if (no.NivelHierarquico == "Item" && !RazaoValida(edicao.QuantidadePorPai))
      return Result<EstruturaItemDto>.Falha(ErroDeRazaoInvalida, TipoDeErro.Validacao);
    if (no.NivelHierarquico == "Peca" && edicao.QuantidadePorPai is not null)
      return Result<EstruturaItemDto>.Falha(ErroDeRazaoNaPeca, TipoDeErro.Validacao);
```

e, junto de `no.Quantidade = edicao.Quantidade;`:

```csharp
    no.QuantidadePorPai = edicao.QuantidadePorPai;
```

- [ ] **Step 12: Rodar os testes novos de Application**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~QuantidadePorPaiTests"`
Expected: PASS (11 testes: 7 `[Fact]` + a `[Theory]` com 4 casos).

- [ ] **Step 13: Testes existentes de Application que criam ou editam Item**

Eles passam a precisar da razão, senão falham pela razão e não pelo que afirmam. Em
`EditarEExcluirNoTests.cs` e `CriarPecaTests.cs`, **todo** `new NovoFilhoDto(...)` ganha
`QuantidadePorPai: 1m` como último argumento — inclusive os que esperam outra recusa (piso de quantidade,
descrição obrigatória): a razão válida é o que garante que eles continuam recusados **pelo motivo que
afirmam**. Mecânico e conferível:

```bash
python3 - <<'EOF'
import re
for p in ['tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs',
          'tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs']:
    s = open(p, encoding='utf-8').read()
    novo, n = re.subn(r'new NovoFilhoDto\(([^)]*)\)',
                      lambda m: m.group(0) if 'QuantidadePorPai' in m.group(1)
                      else f'new NovoFilhoDto({m.group(1)}, QuantidadePorPai: 1m)', s)
    open(p, 'w', encoding='utf-8').write(novo)
    print(p, n)
EOF
```

Expected: 10 trocas em `EditarEExcluirNoTests.cs` e 1 em `CriarPecaTests.cs`.

No mesmo `EditarEExcluirNoTests.cs`, o único `EditarNo` sobre um **Item** (o nó ad-hoc que espera a
recusa por descrição obrigatória) também precisa da razão. Trocar em `tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs`:

````markdown
    var resultado = await useCase.EditarNo(2, new EdicaoDeNoDto("", 3m), CancellationToken.None);
````

por:

````markdown
    var resultado = await useCase.EditarNo(2, new EdicaoDeNoDto("", 3m, QuantidadePorPai: 1m), CancellationToken.None);
````

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS, todos.

- [ ] **Step 14: Helper de banco para as tasks seguintes**

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Pedido + Agrupamento + Componente reais, com nos, Setores e Roteiro criados sob demanda, e a
/// limpeza na ordem das FKs. Existe porque as tasks da Fase 3 criam a mesma arvore em varias classes
/// de teste de banco; cada teste cria a SUA (prefixo unico), e toda asercao fica escopada nos Ids
/// dela — nunca contagem global (licao do flaky de 2026-08-22, CLAUDE.md).
/// </summary>
internal sealed class ArvoreDeTesteNoBanco
{
  private readonly string _prefixo;

  public int AutorId { get; private init; }
  public int PedidoId { get; private init; }
  public int AgrupamentoId { get; private init; }
  public int ComponenteId { get; private init; }
  public List<int> SetorIds { get; } = [];

  private ArvoreDeTesteNoBanco(string prefixo) => _prefixo = prefixo;

  public static async Task<ArvoreDeTesteNoBanco> CriarAsync(RastreamentoDbContext db, string rotulo)
  {
    var prefixo = $"{rotulo}-{Guid.NewGuid():N}"[..14];
    var autor = (await db.Usuarios.AsNoTracking().SingleAsync(u => u.NomeUsuario == "admin")).Id;

    var pedido = new Pedido
    {
      Numero = prefixo, Cliente = "Cliente de teste", Tipo = "Fabricacao", Status = "Aberto",
      DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = autor,
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();

    var agrupamento = new Agrupamento
    {
      PedidoId = pedido.Id, Codigo = "AG-01", Tipo = "Avulso", CriadoPorUsuarioId = autor, CriadoEm = DateTime.UtcNow,
    };
    db.Agrupamentos.Add(agrupamento);

    var componente = new Componente
    {
      Codigo = prefixo, Descricao = "Componente de teste da Fase 3", Tipo = "Fabricado", Ativo = true,
    };
    db.Componentes.Add(componente);
    await db.SaveChangesAsync();

    return new ArvoreDeTesteNoBanco(prefixo)
    {
      AutorId = autor, PedidoId = pedido.Id, AgrupamentoId = agrupamento.Id, ComponenteId = componente.Id,
    };
  }

  public async Task<int> NovoSetorAsync(RastreamentoDbContext db, bool ativo = true)
  {
    var setor = new Setor { Nome = $"{_prefixo}-S{SetorIds.Count + 1}", Ativo = ativo };
    db.Setores.Add(setor);
    await db.SaveChangesAsync();
    SetorIds.Add(setor.Id);
    return setor.Id;
  }

  public async Task<int> NovaPecaAsync(RastreamentoDbContext db, decimal quantidade)
  {
    var peca = new EstruturaItem
    {
      AgrupamentoId = AgrupamentoId, ComponenteId = ComponenteId, NivelHierarquico = "Peca", Quantidade = quantidade,
    };
    db.Estruturas.Add(peca);
    await db.SaveChangesAsync();
    return peca.Id;
  }

  public async Task<int> NovoItemAsync(RastreamentoDbContext db, int paiId, decimal quantidade, decimal quantidadePorPai)
  {
    var item = new EstruturaItem
    {
      AgrupamentoId = AgrupamentoId, Descricao = $"Item de {paiId}", EstruturaPaiId = paiId,
      NivelHierarquico = "Item", Quantidade = quantidade, QuantidadePorPai = quantidadePorPai,
    };
    db.Estruturas.Add(item);
    await db.SaveChangesAsync();
    return item.Id;
  }

  /// <summary>Roteiro do no: um passo por Setor, `Ordem` 1, 2, 3... na ordem dada.</summary>
  public async Task RoteiroAsync(RastreamentoDbContext db, int estruturaItemId, params int[] setorIds)
  {
    for (var i = 0; i < setorIds.Length; i++)
      db.EstruturaRoteiros.Add(new EstruturaRoteiro { EstruturaItemId = estruturaItemId, SetorId = setorIds[i], Ordem = i + 1 });
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// Contexto PROPRIO, para a limpeza nao herdar entidade rastreada nem transacao do teste. Uma unica
  /// instrucao `DELETE` por tabela resolve a FK autorreferenciada de `EstruturaItem` (o SQL Server
  /// confere a FK no fim da instrucao) — mesmo criterio de `EstruturaEndpointsTests.DisposeAsync`.
  /// </summary>
  public async Task LimparAsync(Func<RastreamentoDbContext> novoContexto)
  {
    await using var db = novoContexto();
    var ag = AgrupamentoId;
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {ag}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {PedidoId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Componente WHERE Id = {ComponenteId}");
    foreach (var setorId in SetorIds)
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {setorId}");
  }
}
```

- [ ] **Step 15: Testes de banco da razão**

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/QuantidadePorPaiMapeamentoTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `EstruturaItem.QuantidadePorPai` contra o SQL Server real: o mapeamento guarda as quatro casas, e
/// `CK_EstruturaItem_QuantidadePorPai` recusa Item sem razao, razao zero e Peca com razao (regra 26).
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class QuantidadePorPaiMapeamentoTests : TesteComBanco
{
  [Fact]
  public async Task Item_grava_e_le_a_razao_com_quatro_casas()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "qpp");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var item = await arvore.NovoItemAsync(db, peca, 25.025m, 2.5025m);

      await using var leitura = NovoContexto();
      var lido = await leitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == item);
      var pecaLida = await leitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == peca);
      Assert.Equal(2.5025m, lido.QuantidadePorPai);
      Assert.Null(pecaLida.QuantidadePorPai);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Theory]
  [InlineData("Item", null)]
  [InlineData("Item", "0")]
  [InlineData("Peca", "1")]
  public async Task Banco_recusa_razao_incoerente_com_o_nivel(string nivel, string? razao)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "qpp");
    try
    {
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await using var escrita = NovoContexto();
      escrita.Estruturas.Add(new EstruturaItem
      {
        AgrupamentoId = arvore.AgrupamentoId,
        ComponenteId = arvore.ComponenteId,
        EstruturaPaiId = nivel == "Item" ? peca : null,
        NivelHierarquico = nivel,
        Quantidade = 5m,
        QuantidadePorPai = razao is null ? null : decimal.Parse(razao, System.Globalization.CultureInfo.InvariantCulture),
      });

      var erro = await Assert.ThrowsAsync<DbUpdateException>(() => escrita.SaveChangesAsync());
      Assert.Contains("CK_EstruturaItem_QuantidadePorPai", erro.InnerException!.Message);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}
```

- [ ] **Step 16: Testes de banco existentes que criam Item**

Em `EstruturaItemMapeamentoTests.cs`, os dois `new EstruturaItem { ... NivelHierarquico = "Item", ... }`
ganham a razão. Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaItemMapeamentoTests.cs`:

````markdown
        NivelHierarquico = "Item",
        Quantidade = 40,
      };
````

por:

````markdown
        NivelHierarquico = "Item",
        Quantidade = 40,
        QuantidadePorPai = 4,
      };
````

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaItemMapeamentoTests.cs`:

````markdown
        NivelHierarquico = "Item",
        Quantidade = 5,
        Descricao = "Item ad-hoc, sem base no catalogo",
````

por:

````markdown
        NivelHierarquico = "Item",
        Quantidade = 5,
        QuantidadePorPai = 0.5m,
        Descricao = "Item ad-hoc, sem base no catalogo",
````

Em `EstruturaRepositoryTests.cs`, todo `NoParaGravar` **filho** ganha a razão. Os dois de forma
compacta:

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
          Filhos: [new NoParaGravar(componenteFilho, null, 40m, false, [], [], [])]);
````

por:

````markdown
          Filhos: [new NoParaGravar(componenteFilho, null, 40m, false, [], [], [], QuantidadePorPai: 4m)]);
````

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
          Filhos: [new NoParaGravar(componenteB, null, 1m, false, [], [], [])]);
````

por:

````markdown
          Filhos: [new NoParaGravar(componenteB, null, 1m, false, [], [], [], QuantidadePorPai: 1m)]);
````

e os de forma nomeada. No teste de atomicidade
(`GravarArvoreAsync_e_atomico_erro_no_meio_da_arvore_nao_deixa_nada_gravado`), **isto importa**: sem a
razão, o filho seria recusado pelo `CK_EstruturaItem_QuantidadePorPai` **antes** de o material
inexistente estourar a FK, e o teste passaria verde pelo motivo errado. Por isso a troca vem junto de uma
asserção sobre **qual** restrição recusou.

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
                Materiais: [(materialInexistente, 1m)], Roteiro: [], Filhos: [])
````

por:

````markdown
                Materiais: [(materialInexistente, 1m)], Roteiro: [], Filhos: [], QuantidadePorPai: 4m)
````

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
      await Assert.ThrowsAsync<DbUpdateException>(
          () => repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None));
````

por:

````markdown
      var erro = await Assert.ThrowsAsync<DbUpdateException>(
          () => repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None));
      // Recusado pela FK do material, e nao pelo CK da razao: e a FK no MEIO da arvore que prova a
      // atomicidade (o filho ja estava gravado quando ela estourou).
      Assert.Contains("FK_EstruturaMaterial_Material", erro.InnerException!.Message);
````

No teste de remoção da subárvore
(`RemoverSubarvoreAsync_apaga_material_roteiro_e_subarvore_sem_violar_FK`), o nó do meio (40 sob 10) e a
folha (5 sob 40). Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
                Materiais: [(material.Id, 1m)], Roteiro: [(setor.Id, 1)],
````

por:

````markdown
                Materiais: [(material.Id, 1m)], Roteiro: [(setor.Id, 1)], QuantidadePorPai: 4m,
````

Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
                      Materiais: [(material.Id, 2m)], Roteiro: [(setor.Id, 1)], Filhos: [])
````

por:

````markdown
                      Materiais: [(material.Id, 2m)], Roteiro: [(setor.Id, 1)], Filhos: [], QuantidadePorPai: 0.125m)
````

No teste do ciclo (`RemoverSubarvoreAsync_com_ciclo_nos_dados_lanca_em_vez_de_travar`), o nó que vira Item
por fora da aplicação precisa da razão. Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`:

````markdown
      linhaA.NivelHierarquico = "Item";
````

por:

````markdown
      linhaA.NivelHierarquico = "Item";
      linhaA.QuantidadePorPai = 1m;   // CK_EstruturaItem_QuantidadePorPai: todo Item tem razao
````

- [ ] **Step 17: Testes de endpoint**

Em `EstruturaEndpointsTests.cs`, os dois `POST .../filhos` ganham a razão. Trocar em `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`:

````markdown
        new { componenteId = (int?)null, descricao = "Sub-item ad-hoc", quantidade = 2m });
````

por:

````markdown
        new { componenteId = (int?)null, descricao = "Sub-item ad-hoc", quantidade = 2m, quantidadePorPai = 2m });
````

Trocar em `tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs`:

````markdown
        new { componenteId = (int?)null, descricao = (string?)null, quantidade = 1m });
````

por:

````markdown
        new { componenteId = (int?)null, descricao = (string?)null, quantidade = 1m, quantidadePorPai = 1m });
````

E acrescente, na seção "criacao e leitura" da mesma classe:

```csharp
  [Fact]
  public async Task Filho_leva_quantidadePorPai_e_a_arvore_mostra_semRoteiro()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId, 10m));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var filho = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = "Calço", quantidade = 25m, quantidadePorPai = 2.5m });
    Assert.Equal(HttpStatusCode.Created, filho.StatusCode);

    var arvore = JsonDocument.Parse(await (await cliente.GetAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura")).Content.ReadAsStringAsync()).RootElement;
    var raiz = Assert.Single(arvore.EnumerateArray());
    Assert.Equal(JsonValueKind.Null, raiz.GetProperty("quantidadePorPai").ValueKind);
    Assert.True(raiz.GetProperty("semRoteiro").GetBoolean());   // Componente de teste sem roteiro padrao
    var item = Assert.Single(raiz.GetProperty("filhos").EnumerateArray());
    Assert.Equal(2.5m, item.GetProperty("quantidadePorPai").GetDecimal());
    Assert.True(item.GetProperty("semRoteiro").GetBoolean());
  }

  [Fact]
  public async Task Filho_sem_quantidadePorPai_devolve_400_com_a_regra_26()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos", new { componenteId = (int?)null, descricao = "Calço", quantidade = 1m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Contains("regra 26", corpo.GetProperty("erro").GetString());
  }
```

- [ ] **Step 18: Suíte inteira**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: build com 0 warnings; todos os testes PASS.

- [ ] **Step 19: Commit**

```bash
git add specs/02-modelo-de-dados.sql db/alter-fase-3.sql CLAUDE.md \
  src/Rastreamento.Domain src/Rastreamento.Infrastructure src/Rastreamento.Application/Estrutura \
  tests/Rastreamento.Application.Tests/Estrutura tests/Rastreamento.Infrastructure.Tests/Persistence \
  tests/Rastreamento.Api.Tests/EstruturaEndpointsTests.cs
git commit -m "feat(estrutura): QuantidadePorPai (regra 26) e semRoteiro na arvore"
```

### Task 2: O livro — `dbo.Montagem`, `dbo.Movimentacao` e o perfil Movimentador

Schema, entidades, mapeamento EF e a prova, contra o banco real, de que cada `CHECK` recusa o que diz
recusar. Nenhum caso de uso ainda: a Task 4 lê e escreve no livro.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql`, `db/seed.sql`, `db/alter-fase-3.sql`, `CLAUDE.md`
- Create: `src/Rastreamento.Domain/Entities/Posicoes.cs`, `Movimentacao.cs`, `Montagem.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/MovimentacaoConfiguration.cs`,
  `MontagemConfiguration.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs` (comentário)
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs`,
  `ArvoreDeTesteNoBanco.cs`
- Modify: `web/src/auth/permissoes.test.ts` (só o comentário que conta os perfis do seed)
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/LivroMapeamentoTests.cs`

**Interfaces:**
- Consumes: `ArvoreDeTesteNoBanco` (Task 1).
- Produces: `Posicoes` (`AIniciar`, `NoSetor`, `AguardandoColeta`, `AguardandoMontagem`, `NaExpedicao`,
  `Montado`, `Todas`); `TiposDeMovimentacao` (`Inicio`, `Termino`, `Entrega`, `Montagem`, `Estorno`);
  entidades `Movimentacao` e `Montagem` com as colunas do DDL; `RastreamentoDbContext.Movimentacoes` e
  `.Montagens`; `ArvoreDeTesteNoBanco.LimparAsync` passa a apagar o livro antes da árvore.

- [ ] **Step 1: Schema em `specs/02-modelo-de-dados.sql`**

O comentário de `dbo.Perfil.Nome` passa a listar os sete perfis (spec §3.5):

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
    Nome    NVARCHAR(30)      NOT NULL, -- Operador | Qualidade | PCP | Gestao | Administrador
````

por:

````markdown
    Nome    NVARCHAR(30)      NOT NULL, -- Operador | Almoxarifado | Movimentador | PCP | Qualidade | Gestao | Administrador
````

A `EstruturaSetorHistorico` sai e, no lugar dela (mesmo cabeçalho "EXECUÇÃO / RASTREAMENTO"), entram
`dbo.Montagem` e `dbo.Movimentacao` — texto da spec §3.2 e §3.3; `Montagem` antes, porque
`Movimentacao` tem FK para ela:

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
CREATE TABLE dbo.EstruturaSetorHistorico (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    SetorId             INT                 NOT NULL,
    QuantidadeMovimentada DECIMAL(18,4)     NOT NULL,
    DataEntrada         DATETIME2           NOT NULL,   -- chegada no setor (pode ficar em fila)
    DataInicioExecucao  DATETIME2           NULL,       -- início real do trabalho (opcional, p/ KPI de fila x capacidade)
    DataSaida           DATETIME2           NULL,       -- NULL = ainda está nesse setor (várias passagens abertas por item são permitidas: lote divisível)
    CONSTRAINT PK_EstruturaSetorHistorico PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaSetorHistorico_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaSetorHistorico_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT CK_EstruturaSetorHistorico_InicioAposEntrada
        CHECK (DataInicioExecucao IS NULL OR DataInicioExecucao >= DataEntrada),
    CONSTRAINT CK_EstruturaSetorHistorico_SaidaAposEntrada
        CHECK (DataSaida IS NULL OR DataSaida >= DataEntrada)
);
````

por:

````markdown
-- Registro de "montei N" de um nó com filhos (regra 24). O total montado do nó é a soma de
-- Quantidade das montagens não estornadas. A baixa de CADA filho fica em dbo.Movimentacao
-- (Tipo = 'Montagem', MontagemId = esta linha), com N × QuantidadePorPai gravado: editar a
-- razão depois não reescreve o passado.
CREATE TABLE dbo.Montagem (
    Id                     INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId        INT                 NOT NULL, -- o pai montado (nó com filhos)
    SetorId                INT                 NOT NULL, -- onde foi montado
    Quantidade             DECIMAL(18,4)       NOT NULL, -- N unidades do pai
    DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Montagem_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId              INT                 NOT NULL,
    EstornadaEm            DATETIME2           NULL,     -- NULL = vale; preenchida = estornada (spec da Fase 3, seção 4.5)
    EstornadaPorUsuarioId  INT                 NULL,
    CONSTRAINT PK_Montagem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Montagem_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Montagem_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Montagem_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT FK_Montagem_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Montagem_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Montagem_EstornoCompleto
        CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
            OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL)),
    CONSTRAINT CK_Montagem_EstornoAposMontagem CHECK (EstornadaEm IS NULL OR EstornadaEm >= DataHora)
);

-- Livro de movimentações: cada linha move Quantidade de um nó de uma posição para outra.
-- SÓ INSERÇÃO: não se edita nem se apaga; correção é um Estorno (movimento inverso que aponta o
-- original). Saldo de uma posição = Σ Quantidade onde ela é destino − Σ onde ela é origem;
-- AIniciar = EstruturaItem.Quantidade − Σ onde ela é origem + Σ onde ela é destino (estorno).
-- Conservação (regra 9) por construção: todo movimento tira de uma posição e põe em outra.
CREATE TABLE dbo.Movimentacao (
    Id               INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId  INT                 NOT NULL,
    Tipo             NVARCHAR(20)        NOT NULL, -- Inicio | Termino | Entrega | Montagem | Estorno
    Quantidade       DECIMAL(18,4)       NOT NULL,
    OrigemPosicao    NVARCHAR(20)        NOT NULL,
    OrigemSetorId    INT                 NULL,
    OrigemOrdem      INT                 NULL,     -- passo do Roteiro do próprio nó
    DestinoPosicao   NVARCHAR(20)        NOT NULL,
    DestinoSetorId   INT                 NULL,
    DestinoOrdem     INT                 NULL,
    MontagemId       INT                 NULL,     -- baixa de filho (e o estorno dela)
    EstornoDeId      INT                 NULL,     -- só no Estorno: o movimento que ele desfaz
    DataHora         DATETIME2           NOT NULL CONSTRAINT DF_Movimentacao_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId        INT                 NOT NULL, -- autor
    CONSTRAINT PK_Movimentacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Movimentacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Movimentacao_OrigemSetor FOREIGN KEY (OrigemSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_DestinoSetor FOREIGN KEY (DestinoSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_Montagem FOREIGN KEY (MontagemId) REFERENCES dbo.Montagem (Id),
    CONSTRAINT FK_Movimentacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.Movimentacao (Id),
    CONSTRAINT FK_Movimentacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Movimentacao_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Movimentacao_Tipo
        CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno')),
    CONSTRAINT CK_Movimentacao_OrigemPosicao
        CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    CONSTRAINT CK_Movimentacao_DestinoPosicao
        CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    -- Setor e passo combinam com a posição (tabela da seção 3.1 da spec da Fase 3)
    CONSTRAINT CK_Movimentacao_OrigemCoerente
        CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
            OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
            OR (OrigemPosicao = 'AguardandoMontagem' AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL)),
    CONSTRAINT CK_Movimentacao_DestinoCoerente
        CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
            OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
            OR (DestinoPosicao = 'AguardandoMontagem' AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL)),
    -- Cada tipo só faz as transições dele; o Estorno é o inverso de um dos outros
    CONSTRAINT CK_Movimentacao_Transicao
        CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
            OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                                  AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                                  AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                                  AND DestinoPosicao = 'AguardandoMontagem')
            OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
            OR (Tipo = 'Estorno')),
    CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo = 'Estorno')
            OR (Tipo NOT IN ('Montagem', 'Estorno') AND MontagemId IS NULL)),
    CONSTRAINT CK_Movimentacao_EstornoApontaOriginal
        CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
            OR (Tipo <> 'Estorno' AND EstornoDeId IS NULL))
);
````

O comentário de cabeçalho de `dbo.Perda` (spec §3.5):

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
-- Bucket terminal: em setores + expedido + perdido = total da Peça.
````

por:

````markdown
-- Bucket terminal. Regra 9, para todo nó (Peça ou Item): a iniciar + nos Setores + aguardando
-- coleta ou montagem + no local de expedição + montado dentro do pai + expedido + perdido = total do nó.
````

Os índices: saem os dois da `EstruturaSetorHistorico`, entram os da spec §3.3 e um índice em
`Montagem.EstruturaItemId`, que a spec não lista mas que a soma do total montado por nó (Task 4) usa em
toda leitura de fila:

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
CREATE INDEX IX_EstruturaSetorHistorico_Item ON dbo.EstruturaSetorHistorico (EstruturaItemId, DataEntrada);
CREATE INDEX IX_EstruturaSetorHistorico_Setor ON dbo.EstruturaSetorHistorico (SetorId, DataEntrada);
````

por:

````markdown
CREATE INDEX IX_Movimentacao_EstruturaItem ON dbo.Movimentacao (EstruturaItemId);
CREATE INDEX IX_Movimentacao_DestinoSetor ON dbo.Movimentacao (DestinoSetorId) WHERE DestinoSetorId IS NOT NULL;
CREATE INDEX IX_Movimentacao_OrigemSetor ON dbo.Movimentacao (OrigemSetorId) WHERE OrigemSetorId IS NOT NULL;
-- Um movimento se estorna uma vez só: JaEstornado garantido pelo banco, não só pela aplicação.
CREATE UNIQUE INDEX UX_Movimentacao_EstornoDe ON dbo.Movimentacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
CREATE INDEX IX_Montagem_EstruturaItem ON dbo.Montagem (EstruturaItemId);
````

As consultas de KPI de exemplo viram a nota da spec §3.5:

Trocar em `specs/02-modelo-de-dados.sql`:

````markdown
-- Tempo de liberação por setor (tempo médio que cada setor leva)
-- SELECT SetorId, AVG(DATEDIFF(MINUTE, DataEntrada, DataSaida)) AS MediaMinutos
-- FROM dbo.EstruturaSetorHistorico
-- WHERE DataSaida IS NOT NULL
-- GROUP BY SetorId;

-- Tempo total, tempo em fila e tempo de produção por pedido
-- SELECT
--     p.Id,
--     p.DataAbertura,
--     inicio_producao.InicioReal,
--     p.DataConclusao,
--     DATEDIFF(DAY, p.DataAbertura, inicio_producao.InicioReal)   AS DiasEmFila,
--     DATEDIFF(DAY, inicio_producao.InicioReal, p.DataConclusao) AS DiasProducao,
--     DATEDIFF(DAY, p.DataAbertura, p.DataConclusao)              AS DiasTotal
-- FROM dbo.Pedido p
-- CROSS APPLY (
--     SELECT MIN(esh.DataEntrada) AS InicioReal
--     FROM dbo.EstruturaSetorHistorico esh
--     JOIN dbo.EstruturaItem ei ON ei.Id = esh.EstruturaItemId
--     JOIN dbo.Agrupamento a ON a.Id = ei.AgrupamentoId
--     WHERE a.PedidoId = p.Id
-- ) inicio_producao
-- WHERE p.DataConclusao IS NOT NULL;
````

por:

````markdown
-- As duas consultas de exemplo que viviam aqui liam dbo.EstruturaSetorHistorico, que saiu na
-- Fase 3 (spec 2026-09-24-fase-3-rastreamento-de-setor-design.md, seção 2.7). Reescrever na Fase 6
-- sobre dbo.Movimentacao, pareando entradas e saídas de cada Setor por ordem de chegada (FIFO):
--   * tempo de liberação por setor: da chegada (destino NoSetor) à saída (origem AguardandoColeta);
--   * tempo total, em fila e em produção por pedido: o início real é o MIN(DataHora) dos
--     movimentos Inicio dos nós do Pedido (regra 14).
````

- [ ] **Step 2: Seed e migração**

Trocar em `db/seed.sql`:

````markdown
USING (VALUES ('Operador'),('Almoxarifado'),('PCP'),('Qualidade'),('Gestao'),('Administrador')) AS origem(Nome)
````

por:

````markdown
USING (VALUES ('Operador'),('Almoxarifado'),('Movimentador'),('PCP'),('Qualidade'),('Gestao'),('Administrador')) AS origem(Nome)
````

Acrescente ao **fim** de `db/alter-fase-3.sql`:

```sql
/* 2. Livro de movimentacoes (spec da Fase 3, secoes 3.2 e 3.3) --------------------------------- */
-- dbo.EstruturaSetorHistorico nunca teve codigo que a usasse; sai com os dois indices dela.
IF OBJECT_ID('dbo.EstruturaSetorHistorico') IS NOT NULL
    DROP TABLE dbo.EstruturaSetorHistorico;
GO

-- Registro de "montei N" de um nó com filhos (regra 24). O total montado do nó é a soma de
-- Quantidade das montagens não estornadas. A baixa de CADA filho fica em dbo.Movimentacao
-- (Tipo = 'Montagem', MontagemId = esta linha), com N × QuantidadePorPai gravado: editar a
-- razão depois não reescreve o passado.
IF OBJECT_ID('dbo.Montagem') IS NULL
BEGIN
    CREATE TABLE dbo.Montagem (
        Id                     INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId        INT                 NOT NULL, -- o pai montado (nó com filhos)
        SetorId                INT                 NOT NULL, -- onde foi montado
        Quantidade             DECIMAL(18,4)       NOT NULL, -- N unidades do pai
        DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Montagem_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId              INT                 NOT NULL,
        EstornadaEm            DATETIME2           NULL,     -- NULL = vale; preenchida = estornada (spec da Fase 3, seção 4.5)
        EstornadaPorUsuarioId  INT                 NULL,
        CONSTRAINT PK_Montagem PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Montagem_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Montagem_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Montagem_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_Montagem_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_Montagem_QuantidadePositiva CHECK (Quantidade > 0),
        CONSTRAINT CK_Montagem_EstornoCompleto
            CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
                OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL)),
        CONSTRAINT CK_Montagem_EstornoAposMontagem CHECK (EstornadaEm IS NULL OR EstornadaEm >= DataHora)
    );
END;
GO

-- Livro de movimentações: cada linha move Quantidade de um nó de uma posição para outra.
-- SÓ INSERÇÃO: não se edita nem se apaga; correção é um Estorno (movimento inverso que aponta o
-- original). Saldo de uma posição = Σ Quantidade onde ela é destino − Σ onde ela é origem;
-- AIniciar = EstruturaItem.Quantidade − Σ onde ela é origem + Σ onde ela é destino (estorno).
-- Conservação (regra 9) por construção: todo movimento tira de uma posição e põe em outra.
IF OBJECT_ID('dbo.Movimentacao') IS NULL
BEGIN
    CREATE TABLE dbo.Movimentacao (
        Id               INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId  INT                 NOT NULL,
        Tipo             NVARCHAR(20)        NOT NULL, -- Inicio | Termino | Entrega | Montagem | Estorno
        Quantidade       DECIMAL(18,4)       NOT NULL,
        OrigemPosicao    NVARCHAR(20)        NOT NULL,
        OrigemSetorId    INT                 NULL,
        OrigemOrdem      INT                 NULL,     -- passo do Roteiro do próprio nó
        DestinoPosicao   NVARCHAR(20)        NOT NULL,
        DestinoSetorId   INT                 NULL,
        DestinoOrdem     INT                 NULL,
        MontagemId       INT                 NULL,     -- baixa de filho (e o estorno dela)
        EstornoDeId      INT                 NULL,     -- só no Estorno: o movimento que ele desfaz
        DataHora         DATETIME2           NOT NULL CONSTRAINT DF_Movimentacao_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId        INT                 NOT NULL, -- autor
        CONSTRAINT PK_Movimentacao PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Movimentacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Movimentacao_OrigemSetor FOREIGN KEY (OrigemSetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Movimentacao_DestinoSetor FOREIGN KEY (DestinoSetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Movimentacao_Montagem FOREIGN KEY (MontagemId) REFERENCES dbo.Montagem (Id),
        CONSTRAINT FK_Movimentacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.Movimentacao (Id),
        CONSTRAINT FK_Movimentacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_Movimentacao_QuantidadePositiva CHECK (Quantidade > 0),
        CONSTRAINT CK_Movimentacao_Tipo
            CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno')),
        CONSTRAINT CK_Movimentacao_OrigemPosicao
            CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
        CONSTRAINT CK_Movimentacao_DestinoPosicao
            CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
        -- Setor e passo combinam com a posição (tabela da seção 3.1 da spec da Fase 3)
        CONSTRAINT CK_Movimentacao_OrigemCoerente
            CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
                OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
                OR (OrigemPosicao = 'AguardandoMontagem' AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL)),
        CONSTRAINT CK_Movimentacao_DestinoCoerente
            CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
                OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
                OR (DestinoPosicao = 'AguardandoMontagem' AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL)),
        -- Cada tipo só faz as transições dele; o Estorno é o inverso de um dos outros
        CONSTRAINT CK_Movimentacao_Transicao
            CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
                OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                                      AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
                OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                                      AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
                OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                                      AND DestinoPosicao = 'AguardandoMontagem')
                OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
                OR (Tipo = 'Estorno')),
        CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
            CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
                OR (Tipo = 'Estorno')
                OR (Tipo NOT IN ('Montagem', 'Estorno') AND MontagemId IS NULL)),
        CONSTRAINT CK_Movimentacao_EstornoApontaOriginal
            CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
                OR (Tipo <> 'Estorno' AND EstornoDeId IS NULL))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_EstruturaItem')
    CREATE INDEX IX_Movimentacao_EstruturaItem ON dbo.Movimentacao (EstruturaItemId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_DestinoSetor')
    CREATE INDEX IX_Movimentacao_DestinoSetor ON dbo.Movimentacao (DestinoSetorId) WHERE DestinoSetorId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_OrigemSetor')
    CREATE INDEX IX_Movimentacao_OrigemSetor ON dbo.Movimentacao (OrigemSetorId) WHERE OrigemSetorId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Movimentacao_EstornoDe')
    CREATE UNIQUE INDEX UX_Movimentacao_EstornoDe ON dbo.Movimentacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Montagem_EstruturaItem')
    CREATE INDEX IX_Montagem_EstruturaItem ON dbo.Montagem (EstruturaItemId);
GO

/* 3. Perfil Movimentador (mesmo MERGE de db/seed.sql, para quem nao roda o seed de novo) -------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Perfil WHERE Nome = 'Movimentador')
    INSERT INTO dbo.Perfil (Nome) VALUES ('Movimentador');
GO
```

Trocar em `CLAUDE.md`:

````markdown
até o `02-modelo-de-dados.sql` — hoje, a coluna `EstruturaItem.QuantidadePorPai`, com o preenchimento
da seção 3.4 da spec da Fase 3. Rodar de novo não muda nada. `-b` aborta no primeiro erro, e `-f 65001`
pelo mesmo motivo do `seed-demo.sql`:
````

por:

````markdown
até o `02-modelo-de-dados.sql`: a coluna `EstruturaItem.QuantidadePorPai` (com o preenchimento da seção
3.4 da spec da Fase 3), a saída de `dbo.EstruturaSetorHistorico`, o livro (`dbo.Montagem`,
`dbo.Movimentacao` e os índices) e o perfil `Movimentador`. Rodar de novo não muda nada. `-b` aborta no
primeiro erro, e `-f 65001` pelo mesmo motivo do `seed-demo.sql`:
````

- [ ] **Step 3: Aplicar e conferir**

Run: os dois comandos do bloco "Fase 3 — `db/alter-fase-3.sql`" do `CLAUDE.md`; depois
`MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -Q "SELECT OBJECT_ID('dbo.EstruturaSetorHistorico') AS Historico, (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.Movimentacao'), OBJECT_ID('dbo.Montagem'))) AS Checks, (SELECT COUNT(*) FROM dbo.Perfil) AS Perfis"`
Expected: `Historico` NULL, `Checks` 12 (nove em `Movimentacao`, três em `Montagem`), `Perfis` 7. Rodar o arquivo de novo termina sem erro.

- [ ] **Step 4: Escrever os testes de banco que falham**

Em `DbContextMappingTests.cs`, o teste que conta os perfis do seed passa a sete. Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs`:

````markdown
  public async Task Mapeia_seis_perfis_seedados()
  {
    await using var db = NovoContexto();
    var total = await db.Perfis.CountAsync();
    Assert.Equal(6, total);
  }
````

por:

````markdown
  public async Task Mapeia_sete_perfis_seedados()
  {
    await using var db = NovoContexto();
    var total = await db.Perfis.CountAsync();
    Assert.Equal(7, total);   // o Movimentador entrou na Fase 3
    Assert.True(await db.Perfis.AnyAsync(p => p.Nome == "Movimentador"));
  }
````

Em `ArvoreDeTesteNoBanco.LimparAsync`, o livro sai **antes** da árvore — estornos primeiro (a FK
`FK_Movimentacao_EstornoDe` aponta para o original), depois os demais movimentos, depois `Montagem` (a
baixa aponta para ela). É limpeza de teste, não código de produção: o "só inclusão" vale para `src/`.
Trocar em `tests/Rastreamento.Infrastructure.Tests/Persistence/ArvoreDeTesteNoBanco.cs`:

````markdown
    var ag = AgrupamentoId;
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
````

por:

````markdown
    var ag = AgrupamentoId;
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Movimentacao WHERE EstornoDeId IS NOT NULL AND EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Movimentacao WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.Montagem WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
````

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/LivroMapeamentoTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `dbo.Movimentacao` e `dbo.Montagem` contra o SQL Server real (spec da Fase 3, secao 9.2): o
/// mapeamento faz a volta completa, e cada restricao da secao 3 recusa um caso. Cada caso viola UMA
/// restricao so, sempre que o DDL permite; quando uma posicao ou tipo fora da lista viola tambem a
/// restricao de coerencia ou de transicao (que so enumeram valores validos), o teste aceita as duas e
/// diz isso no nome — medir so uma daria teste que passa pelo motivo errado quando a outra disparar
/// primeiro, e a ordem em que o SQL Server avalia CHECKs nao e garantida.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class LivroMapeamentoTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int SetorId);

  private static async Task<Cenario> NovoCenarioAsync(RastreamentoDbContext db)
  {
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "livro");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var setor = await arvore.NovoSetorAsync(db);
    return new Cenario(arvore, peca, setor);
  }

  private static Movimentacao Inicio(Cenario c, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Inicio, Quantidade = quantidade,
    OrigemPosicao = Posicoes.AIniciar,
    DestinoPosicao = Posicoes.NoSetor, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync(Movimentacao m)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private async Task AfirmarRecusaAsync(Movimentacao m, params string[] restricoesAceitas)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    var mensagem = erro.InnerException?.Message ?? erro.Message;
    Assert.True(restricoesAceitas.Any(r => mensagem.Contains(r)),
        $"esperava {string.Join(" ou ", restricoesAceitas)}; o banco disse: {mensagem}");
  }

  private async Task AfirmarRecusaAsync(Montagem m, string restricao)
  {
    await using var db = NovoContexto();
    db.Montagens.Add(m);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Contains(restricao, erro.InnerException?.Message ?? erro.Message);
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var c = await NovoCenarioAsync(db);
    try { await corpo(c); }
    finally { await c.Arvore.LimparAsync(NovoContexto); }
  }

  [Fact]
  public Task Movimentacao_faz_a_volta_completa() => NoCenarioAsync(async c =>
  {
    var id = await GravarAsync(Inicio(c, 2.5025m));

    await using var leitura = NovoContexto();
    var lida = await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == id);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(TiposDeMovimentacao.Inicio, lida.Tipo);
    Assert.Equal(2.5025m, lida.Quantidade);
    Assert.Equal(Posicoes.AIniciar, lida.OrigemPosicao);
    Assert.Null(lida.OrigemSetorId);
    Assert.Equal(Posicoes.NoSetor, lida.DestinoPosicao);
    Assert.Equal(c.SetorId, lida.DestinoSetorId);
    Assert.Equal(1, lida.DestinoOrdem);
    Assert.Null(lida.MontagemId);
    Assert.Null(lida.EstornoDeId);
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
  });

  [Fact]
  public Task Montagem_faz_a_volta_completa_e_a_baixa_aponta_para_ela() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var montagem = new Montagem
    {
      EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 3m,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };
    db.Montagens.Add(montagem);
    await db.SaveChangesAsync();
    var item = await c.Arvore.NovoItemAsync(db, c.PecaId, 12m, 4m);
    var baixa = await GravarAsync(new Movimentacao
    {
      EstruturaItemId = item, Tipo = TiposDeMovimentacao.Montagem, Quantidade = 12m,
      OrigemPosicao = Posicoes.AguardandoMontagem, OrigemSetorId = c.SetorId,
      DestinoPosicao = Posicoes.Montado, MontagemId = montagem.Id,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    await using var leitura = NovoContexto();
    var lida = await leitura.Montagens.AsNoTracking().SingleAsync(m => m.Id == montagem.Id);
    Assert.Equal(3m, lida.Quantidade);
    Assert.Null(lida.EstornadaEm);
    Assert.Null(lida.EstornadaPorUsuarioId);
    Assert.Equal(montagem.Id, (await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == baixa)).MontagemId);
  });

  [Fact]
  public Task Quantidade_zero_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Inicio(c, 0m), "CK_Movimentacao_QuantidadePositiva"));

  [Fact]
  public Task Tipo_fora_da_lista_e_recusado_pelo_tipo_ou_pela_transicao() => NoCenarioAsync(c =>
  {
    var m = Inicio(c);
    m.Tipo = "Pronto";   // tipo da Fase 5, ainda nao existe
    return AfirmarRecusaAsync(m, "CK_Movimentacao_Tipo", "CK_Movimentacao_Transicao");
  });

  [Fact]
  public Task Posicao_de_origem_fora_da_lista_e_recusada_pela_lista_ou_pela_coerencia() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = "Expedido", DestinoPosicao = Posicoes.AIniciar,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_OrigemPosicao", "CK_Movimentacao_OrigemCoerente");
  });

  [Fact]
  public Task Posicao_de_destino_fora_da_lista_e_recusada_pela_lista_ou_pela_coerencia() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1, DestinoPosicao = "Perdido",
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_DestinoPosicao", "CK_Movimentacao_DestinoCoerente");
  });

  [Fact]
  public Task Origem_no_Setor_sem_Setor_e_recusada() => NoCenarioAsync(c =>
      // Termino com OrigemSetorId nulo: a transicao compara OrigemSetorId = DestinoSetorId, que da
      // UNKNOWN com nulo — e CHECK com UNKNOWN passa. Sobra so a coerencia da origem para recusar.
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Termino, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AguardandoColeta, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_OrigemCoerente"));

  [Fact]
  public Task Aguardando_montagem_com_passo_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Entrega, Quantidade = 1m,
        OrigemPosicao = Posicoes.AguardandoColeta, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AguardandoMontagem, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_DestinoCoerente"));

  [Fact]
  public Task Inicio_que_nao_sai_de_a_iniciar_e_recusado_pela_transicao() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Inicio, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.NoSetor, DestinoSetorId = c.SetorId, DestinoOrdem = 2,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_Transicao"));

  [Fact]
  public Task Baixa_de_montagem_sem_Montagem_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Montagem, Quantidade = 1m,
        OrigemPosicao = Posicoes.AguardandoMontagem, OrigemSetorId = c.SetorId,
        DestinoPosicao = Posicoes.Montado,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_MontagemSoNaBaixa"));

  [Fact]
  public Task Estorno_sem_original_e_recusado() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AIniciar,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_EstornoApontaOriginal"));

  [Fact]
  public Task Segundo_estorno_do_mesmo_movimento_e_recusado_pelo_indice_unico() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    Movimentacao Estorno() => new()
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
      DestinoPosicao = Posicoes.AIniciar,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };
    await GravarAsync(Estorno());

    await AfirmarRecusaAsync(Estorno(), "UX_Movimentacao_EstornoDe");
  });

  [Fact]
  public Task Montagem_de_quantidade_zero_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 0m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Montagem_QuantidadePositiva"));

  [Fact]
  public Task Montagem_estornada_sem_autor_do_estorno_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 1m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId, EstornadaEm = DateTime.UtcNow,
      }, "CK_Montagem_EstornoCompleto"));

  [Fact]
  public Task Montagem_estornada_antes_de_existir_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 1m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
        EstornadaEm = DateTime.UtcNow.AddHours(-1), EstornadaPorUsuarioId = c.Arvore.AutorId,
      }, "CK_Montagem_EstornoAposMontagem"));
}
```

- [ ] **Step 5: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~LivroMapeamentoTests|FullyQualifiedName~Mapeia_sete_perfis"`
Expected: FAIL na compilação — `Movimentacao`, `Montagem`, `Posicoes`, `TiposDeMovimentacao` e os
`DbSet` não existem.

- [ ] **Step 6: Constantes e entidades**

Criar `src/Rastreamento.Domain/Entities/Posicoes.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// As posicoes da quantidade de um no (spec da Fase 3, secao 3.1). Os valores sao os de
/// `CK_Movimentacao_OrigemPosicao` e `CK_Movimentacao_DestinoPosicao` — mudar um lado sem o outro faz o
/// banco recusar. Nome no plural para nao colidir com a propriedade `Posicao` dos records da
/// Application.
/// </summary>
public static class Posicoes
{
  public const string AIniciar = "AIniciar";
  public const string NoSetor = "NoSetor";
  public const string AguardandoColeta = "AguardandoColeta";
  public const string AguardandoMontagem = "AguardandoMontagem";
  public const string NaExpedicao = "NaExpedicao";
  public const string Montado = "Montado";

  /// <summary>A ordem em que as telas listam as posicoes de um no.</summary>
  public static readonly IReadOnlyList<string> Todas =
      [AIniciar, NoSetor, AguardandoColeta, AguardandoMontagem, NaExpedicao, Montado];
}

/// <summary>Os tipos de `CK_Movimentacao_Tipo`. A Fase 5 acrescenta `Pronto`.</summary>
public static class TiposDeMovimentacao
{
  public const string Inicio = "Inicio";
  public const string Termino = "Termino";
  public const string Entrega = "Entrega";
  public const string Montagem = "Montagem";
  public const string Estorno = "Estorno";
}
```

Criar `src/Rastreamento.Domain/Entities/Movimentacao.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha do livro de movimentacoes: move `Quantidade` de um no de uma posicao para outra (spec da
/// Fase 3, secao 3.3). SO INCLUSAO — nenhum codigo edita nem apaga uma linha; correcao e um Estorno
/// que aponta o original (`EstornoDeId`). Setor e passo (`Ordem` do Roteiro do proprio no) so existem
/// nas posicoes que os tem; os CHECKs do banco garantem a coerencia.
/// </summary>
public class Movimentacao
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public string Tipo { get; set; } = string.Empty;
  public decimal Quantidade { get; set; }
  public string OrigemPosicao { get; set; } = string.Empty;
  public int? OrigemSetorId { get; set; }
  public int? OrigemOrdem { get; set; }
  public string DestinoPosicao { get; set; } = string.Empty;
  public int? DestinoSetorId { get; set; }
  public int? DestinoOrdem { get; set; }

  /// <summary>So na baixa de filho de uma montagem, e no estorno dela.</summary>
  public int? MontagemId { get; set; }

  /// <summary>So no Estorno: o movimento que ele desfaz (`UX_Movimentacao_EstornoDe`: uma vez so).</summary>
  public int? EstornoDeId { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor — quem pode estornar sem ser PCP ou Administrador (spec secao 4.5).</summary>
  public int UsuarioId { get; set; }
}
```

Criar `src/Rastreamento.Domain/Entities/Montagem.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

/// <summary>
/// "Montei N" de um no com filhos (regra 24). O total montado do no e a soma das montagens nao
/// estornadas. A baixa de cada filho mora em `Movimentacao` (Tipo = Montagem, `MontagemId` = esta),
/// com `N x QuantidadePorPai` gravado. Estornar e marcar `EstornadaEm`/`EstornadaPorUsuarioId` — a
/// unica escrita que uma linha desta tabela recebe depois de nascer — e gravar um Estorno por baixa.
/// </summary>
public class Montagem
{
  public int Id { get; set; }

  /// <summary>O pai montado.</summary>
  public int EstruturaItemId { get; set; }

  public int SetorId { get; set; }
  public decimal Quantidade { get; set; }
  public DateTime DataHora { get; set; }
  public int UsuarioId { get; set; }
  public DateTime? EstornadaEm { get; set; }
  public int? EstornadaPorUsuarioId { get; set; }
}
```

- [ ] **Step 7: Mapeamento EF**

Criar `src/Rastreamento.Infrastructure/Persistence/Configurations/MovimentacaoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MovimentacaoConfiguration : IEntityTypeConfiguration<Movimentacao>
{
  public void Configure(EntityTypeBuilder<Movimentacao> b)
  {
    b.ToTable("Movimentacao");
    b.HasKey(x => x.Id);
    b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
    b.Property(x => x.OrigemPosicao).HasMaxLength(20).IsRequired();
    b.Property(x => x.DestinoPosicao).HasMaxLength(20).IsRequired();
    // Espelha DECIMAL(18,4) do .sql. Sem isto o EF usa o default dele e trunca em silencio.
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}
```

Criar `src/Rastreamento.Infrastructure/Persistence/Configurations/MontagemConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class MontagemConfiguration : IEntityTypeConfiguration<Montagem>
{
  public void Configure(EntityTypeBuilder<Montagem> b)
  {
    b.ToTable("Montagem");
    b.HasKey(x => x.Id);
    b.Property(x => x.Quantidade).HasPrecision(18, 4);
  }
}
```

Em `RastreamentoDbContext`, depois de `EstruturaRoteiros`:

```csharp
  public DbSet<Movimentacao> Movimentacoes => Set<Movimentacao>();
  public DbSet<Montagem> Montagens => Set<Montagem>();
```

- [ ] **Step 8: Comentários que citavam a tabela que saiu**

Trocar em `src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs`:

````markdown
/// Cadastro de Setor: criar, editar, listar e (in)ativar. Setor nao se exclui — linhas de
/// EstruturaSetorHistorico apontam para ele (ver a spec da Fase 1, "Politica de exclusao").
````

por:

````markdown
/// Cadastro de Setor: criar, editar, listar e (in)ativar. Setor nao se exclui — linhas de
/// Movimentacao, Montagem e EstruturaRoteiro apontam para ele (ver a spec da Fase 1, "Politica de
/// exclusao"; a EstruturaSetorHistorico que a spec cita saiu na Fase 3).
````

Trocar em `web/src/auth/permissoes.test.ts`:

````markdown
    // CONFERIDO no pré-flight de 2026-08-10: `db/seed.sql:3` traz os 6 perfis sem acento.
````

por:

````markdown
    // CONFERIDO no pré-flight de 2026-08-10, remedido na Fase 3 (2026-09-25): o `MERGE` de perfis de
    // `db/seed.sql` traz os 7 perfis sem acento — o Movimentador entrou na Fase 3.
````

- [ ] **Step 9: Rodar os testes de banco**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~LivroMapeamentoTests|FullyQualifiedName~Mapeia_sete_perfis|FullyQualifiedName~QuantidadePorPaiMapeamentoTests"`
Expected: PASS (15 de `LivroMapeamentoTests`, 1 de perfis, 4 de razão).

Mutação de conferência, a registrar no relatório: desligue `CK_Movimentacao_Transicao` no banco
(`ALTER TABLE dbo.Movimentacao NOCHECK CONSTRAINT CK_Movimentacao_Transicao`) e rode de novo —
`Inicio_que_nao_sai_de_a_iniciar_e_recusado_pela_transicao` tem de falhar. Religue com
`ALTER TABLE dbo.Movimentacao WITH CHECK CHECK CONSTRAINT CK_Movimentacao_Transicao`.

- [ ] **Step 10: Suíte inteira**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx && (cd web && npm test -- --run)`
Expected: 0 warnings; tudo PASS.

- [ ] **Step 11: Commit**

```bash
git add specs/02-modelo-de-dados.sql db/seed.sql db/alter-fase-3.sql CLAUDE.md \
  src/Rastreamento.Domain/Entities src/Rastreamento.Infrastructure/Persistence \
  src/Rastreamento.Application/Cadastros/CadastroDeSetorUseCase.cs web/src/auth/permissoes.test.ts \
  tests/Rastreamento.Infrastructure.Tests/Persistence
git commit -m "feat(execucao): livro de movimentacoes, Montagem e perfil Movimentador no schema e no EF"
```

### Task 3: A calculadora — saldo, destino, tarefa, sobra e "dá para montar"

Funções puras, sem repositório (spec §7.2). Leitura e escrita usam as mesmas: o que a tela oferece é o
que a API aceita, por construção. Esta task não toca banco nem API.

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/SaldoLiquido.cs`
- Create: `src/Rastreamento.Application/Execucao/Local.cs`
- Create: `src/Rastreamento.Application/Execucao/Livro.cs`
- Create: `src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/LivroTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/CalculadoraDeExecucaoTests.cs`

**Interfaces:**
- Consumes: `Posicoes`, `Movimentacao` (Task 2).
- Produces (namespace `Rastreamento.Application.Execucao`, salvo indicação):
  - `Rastreamento.Domain.Abstractions.SaldoLiquido(int EstruturaItemId, string Posicao, int? SetorId, int? Ordem, decimal Quantidade)`
  - `readonly record struct Local(string Posicao, int? SetorId, int? Ordem)` com `Local.AIniciar`,
    `Local.NaExpedicao`, `Local.Montado`, `Local.NoSetor(s, k)`, `Local.AguardandoColeta(s, k)`,
    `Local.AguardandoMontagem(s)`, `Local.DaOrigem(Movimentacao)`, `Local.DoDestino(Movimentacao)`
  - `static class Livro`: `IReadOnlyList<SaldoLiquido> SomarSaldos(IEnumerable<Movimentacao>)`,
    `int OrdemDaPosicao(string)`
  - `readonly record struct PassoDoCalculo(int SetorId, int Ordem)`
  - `record NoDoCalculo(int Id, int? PaiId, decimal Quantidade, decimal? QuantidadePorPai, IReadOnlyList<PassoDoCalculo> Roteiro)` com `bool EhPeca`
  - `enum TipoDeDestino { ProximoPasso, Expedicao, Montagem }`
  - `record DestinoCalculado(TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId, int? SugestaoSetorId, IReadOnlyList<int> SetoresPossiveis)` com `bool PaiSemRoteiro`
  - `record FilhoNaMontagem(int FilhoId, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima)`
  - `record Montabilidade(int PaiId, int SetorId, decimal FaltaMontar, decimal DaParaMontar, IReadOnlyList<FilhoNaMontagem> Filhos)`
  - `record ColetaPendente(int EstruturaItemId, int SetorId, int Ordem, decimal Tarefa)`
  - `sealed class CalculadoraDeExecucao(IEnumerable<NoDoCalculo>, IEnumerable<SaldoLiquido>, IReadOnlyDictionary<int, decimal> totaisMontados, IEnumerable<(int EstruturaItemId, int Ordem)> passosAlcancados)` com:
    `Nos`, `Conhece(id)`, `No(id)`, `Filhos(paiId)`, `TemFilhos(id)`, `Saldo(id, Local)`,
    `SaidoDeAIniciar(id)`, `Saldos(id)`, `TotalMontado(id)`, `FaltaMontar(paiId)`,
    `AguardandoMontagem(filhoId, setorId)`, `AguardandoMontagemTotal(filhoId)`,
    `SetoresOndeAguardaMontagem(filhoId)`, `Precisa(filhoId)`, `PrimeiroPasso(id)`,
    `ProximoPasso(id, ordem)`, `PassosAlcancados(id)`, `SetoresDoRoteiro(id)`,
    `DestinoDaColeta(id, ordem)`, `SugestaoDeMontagem(paiId)`, `Tarefa(id, setorId, ordem)`,
    `SobraDaColeta(id, setorId, ordem)`, `ExcessoEmMontagem(filhoId)`,
    `CalcularMontabilidade(paiId, setorId)`, `ColetasPendentes()`.
  **Contrato de uso:** a calculadora só enxerga os nós que recebe. Quem pergunta o destino ou a tarefa
  de um Item tem de passar também o **pai** (Roteiro, quantidade, total montado) — sem ele o destino sai
  como "pai sem Roteiro".

- [ ] **Step 1: Escrever os testes que falham**

Criar `tests/Rastreamento.Application.Tests/Execucao/LivroTests.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

public class LivroTests
{
  private static Movimentacao Mov(int item, string tipo, Local de, Local para, decimal quantidade) => new()
  {
    EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
  };

  [Fact]
  public void Soma_destino_e_subtrai_origem_por_no_e_posicao()
  {
    var saldos = Livro.SomarSaldos(
    [
      Mov(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 6m),
      Mov(1, TiposDeMovimentacao.Termino, Local.NoSetor(7, 1), Local.AguardandoColeta(7, 1), 4m),
      Mov(2, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 2m),
    ]);

    Assert.Equal(
        new[]
        {
          new SaldoLiquido(1, Posicoes.AIniciar, null, null, -6m),
          new SaldoLiquido(1, Posicoes.NoSetor, 7, 1, 2m),
          new SaldoLiquido(1, Posicoes.AguardandoColeta, 7, 1, 4m),
          new SaldoLiquido(2, Posicoes.AIniciar, null, null, -2m),
          new SaldoLiquido(2, Posicoes.NoSetor, 7, 1, 2m),
        },
        saldos);
  }

  [Fact]
  public void Estorno_zera_o_par_e_posicao_zerada_nao_aparece()
  {
    var saldos = Livro.SomarSaldos(
    [
      Mov(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(7, 1), 6m),
      Mov(1, TiposDeMovimentacao.Estorno, Local.NoSetor(7, 1), Local.AIniciar, 6m),
    ]);

    Assert.Empty(saldos);
  }
}
```

Criar `tests/Rastreamento.Application.Tests/Execucao/CalculadoraDeExecucaoTests.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A calculadora da spec da Fase 3, secao 7, por tabela. Os numeros da spec do Kit (C de 10, D de 45
/// com razao 4) entram como casos, inclusive os contraexemplos (a) e (b) da review de branch do Kit.
/// </summary>
public class CalculadoraDeExecucaoTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList());

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido>? saldos = null,
      IReadOnlyDictionary<int, decimal>? montados = null,
      IEnumerable<(int, int)>? alcancados = null) =>
      new(nos, saldos ?? Array.Empty<SaldoLiquido>(), montados ?? new Dictionary<int, decimal>(),
          alcancados ?? Array.Empty<(int, int)>());

  [Fact]
  public void Saldo_a_iniciar_e_a_quantidade_menos_o_que_saiu()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte)],
        [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Corte, 1), 4m)]);

    Assert.Equal(6m, calc.Saldo(1, Local.AIniciar));
    Assert.Equal(4m, calc.Saldo(1, Local.NoSetor(Corte, 1)));
    Assert.Equal(4m, calc.SaidoDeAIniciar(1));
    Assert.Equal(new[] { (Local.AIniciar, 6m), (Local.NoSetor(Corte, 1), 4m) }, calc.Saldos(1));
  }

  [Fact]
  public void Destino_segue_o_passo_mesmo_com_setor_repetido()
  {
    var calc = Calcular([No(1, null, 10m, null, Corte, Dobra, Corte)]);

    Assert.Equal(TipoDeDestino.ProximoPasso, calc.DestinoDaColeta(1, 1).Tipo);
    Assert.Equal<PassoDoCalculo?>(new PassoDoCalculo(Dobra, 2), calc.DestinoDaColeta(1, 1).Passo);
    Assert.Equal<PassoDoCalculo?>(new PassoDoCalculo(Corte, 3), calc.DestinoDaColeta(1, 2).Passo);
    Assert.Equal(TipoDeDestino.Expedicao, calc.DestinoDaColeta(1, 3).Tipo);   // Peca no fim: expedicao
  }

  [Fact]
  public void Item_no_ultimo_passo_vai_para_a_montagem_do_pai_com_os_Setores_do_Roteiro_dele()
  {
    var calc = Calcular([No(1, null, 10m, null, Corte, Solda, Pintura, Solda), No(2, 1, 40m, 4m, Corte)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Equal(1, destino.PaiId);
    Assert.Equal(new[] { Corte, Solda, Pintura }, destino.SetoresPossiveis);   // distintos, em ordem de passo
    Assert.False(destino.PaiSemRoteiro);
  }

  [Fact]
  public void Sugestao_e_o_Setor_onde_o_pai_esta_em_trabalho_o_de_maior_saldo()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Solda, Pintura), No(2, 1, 40m, 4m, Dobra)],
        [Em(1, Local.NoSetor(Solda, 2), 3m), Em(1, Local.NoSetor(Pintura, 3), 5m)]);

    Assert.Equal(Pintura, calc.DestinoDaColeta(2, 1).SugestaoSetorId);
  }

  [Fact]
  public void Sem_pai_em_trabalho_a_sugestao_e_o_primeiro_passo_nao_alcancado()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Solda, Pintura), No(2, 1, 40m, 4m, Dobra)],
        alcancados: [(1, 1)]);

    Assert.Equal(Solda, calc.DestinoDaColeta(2, 1).SugestaoSetorId);
  }

  [Fact]
  public void Pai_sem_Roteiro_nao_tem_sugestao_nem_Setores()
  {
    var calc = Calcular([No(1, null, 10m, null), No(2, 1, 40m, 4m, Dobra)]);

    var destino = calc.DestinoDaColeta(2, 1);

    Assert.Equal(TipoDeDestino.Montagem, destino.Tipo);
    Assert.Null(destino.SugestaoSetorId);
    Assert.Empty(destino.SetoresPossiveis);
    Assert.True(destino.PaiSemRoteiro);
  }

  [Fact]
  public void Peca_no_ultimo_passo_e_tarefa_inteira_para_a_expedicao()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte)],
        [Em(1, Local.AIniciar, -10m), Em(1, Local.AguardandoColeta(Corte, 1), 10m)]);

    Assert.Equal(10m, calc.Tarefa(1, Corte, 1));
    Assert.Equal(0m, calc.SobraDaColeta(1, Corte, 1));
  }

  [Fact]
  public void Contraexemplo_b_da_spec_do_Kit_a_sobra_de_D_nao_vira_tarefa()
  {
    // P de 10; C de 10 (razao 1) e D de 45 (razao 4) terminaram o ultimo passo, no Corte.
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.AguardandoColeta(Corte, 1), 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.AguardandoColeta(Corte, 1), 45m),
        ]);

    Assert.Equal(10m, calc.Tarefa(2, Corte, 1));
    Assert.Equal(40m, calc.Tarefa(3, Corte, 1));
    Assert.Equal(5m, calc.SobraDaColeta(3, Corte, 1));
    Assert.Equal(0m, calc.SobraDaColeta(2, Corte, 1));
  }

  [Fact]
  public void Depois_de_montar_tudo_o_que_sobrou_nao_e_tarefa_nem_da_para_montar()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.Montado, 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.Montado, 40m), Em(3, Local.AguardandoColeta(Corte, 1), 5m),
        ],
        montados: new Dictionary<int, decimal> { [1] = 10m });

    Assert.Equal(0m, calc.FaltaMontar(1));
    Assert.Equal(0m, calc.Precisa(3));
    Assert.Equal(0m, calc.Tarefa(3, Corte, 1));
    Assert.Equal(5m, calc.SobraDaColeta(3, Corte, 1));
    Assert.Equal(0m, calc.CalcularMontabilidade(1, Solda).DaParaMontar);
  }

  [Fact]
  public void Contraexemplo_a_o_excesso_em_montagem_aparece_no_nivel_do_no()
  {
    // O Movimentador levou as 45 D para a Solda (a entrega nao tem teto, spec secao 4.3).
    var calc = Calcular(
        [No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte), No(3, 1, 45m, 4m, Corte)],
        [
          Em(2, Local.AIniciar, -10m), Em(2, Local.AguardandoMontagem(Solda), 10m),
          Em(3, Local.AIniciar, -45m), Em(3, Local.AguardandoMontagem(Solda), 45m),
        ]);

    Assert.Equal(5m, calc.ExcessoEmMontagem(3));
    Assert.Equal(0m, calc.ExcessoEmMontagem(2));
    Assert.Equal(0m, calc.Precisa(3));

    var montavel = calc.CalcularMontabilidade(1, Solda);
    Assert.Equal(10m, montavel.DaParaMontar);            // min(10, 10/1, floor(45/4) = 11)
    Assert.All(montavel.Filhos, f => Assert.Null(f.FaltaParaProxima));   // 11 > o que falta montar
  }

  [Fact]
  public void Da_para_montar_N_e_falta_X_de_Y_para_a_proxima()
  {
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 5m), Em(3, Local.AguardandoMontagem(Solda), 1m)]);

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(1m, montavel.DaParaMontar);   // min(5, floor(5/2) = 2, floor(1/1) = 1)
    Assert.Equal(5m, montavel.FaltaMontar);
    var b = montavel.Filhos.Single(f => f.FilhoId == 2);
    var c = montavel.Filhos.Single(f => f.FilhoId == 3);
    Assert.Equal((5m, 4m, 0m), (b.Presente, b.NecessarioParaProxima!.Value, b.FaltaParaProxima!.Value));
    Assert.Equal((1m, 2m, 1m), (c.Presente, c.NecessarioParaProxima!.Value, c.FaltaParaProxima!.Value));
  }

  [Fact]
  public void Filho_ausente_do_Setor_aparece_com_presente_zero()
  {
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 4m)]);

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(0m, montavel.DaParaMontar);
    var c = montavel.Filhos.Single(f => f.FilhoId == 3);
    Assert.Equal(0m, c.Presente);
    Assert.Equal(1m, c.FaltaParaProxima);
  }

  [Fact]
  public void Falta_nao_aparece_quando_nao_ha_proxima_unidade_a_montar()
  {
    var calc = Calcular(
        [No(1, null, 2m, null, Solda), No(2, 1, 2m, 1m, Corte)],
        [Em(2, Local.AguardandoMontagem(Solda), 3m)],
        montados: new Dictionary<int, decimal> { [1] = 1m });

    var montavel = calc.CalcularMontabilidade(1, Solda);

    Assert.Equal(1m, montavel.DaParaMontar);
    Assert.Null(Assert.Single(montavel.Filhos).NecessarioParaProxima);
  }

  [Fact]
  public void Razao_editada_depois_da_montagem_so_muda_o_futuro()
  {
    // P de 5 com 2 ja montadas; a razao de C foi editada de 4 para 3 depois disso. As baixas gravadas
    // (8 = 2 x 4) nao entram na conta: o que falta receber usa a razao NOVA sobre o que falta montar.
    var calc = Calcular(
        [No(1, null, 5m, null, Solda), No(2, 1, 20m, 3m, Corte)],
        [Em(2, Local.AIniciar, -18m), Em(2, Local.Montado, 8m), Em(2, Local.AguardandoMontagem(Solda), 10m)],
        montados: new Dictionary<int, decimal> { [1] = 2m });

    Assert.Equal(3m, calc.FaltaMontar(1));
    Assert.Equal(0m, calc.Precisa(2));             // max(0, 3 x 3 - 10)
    Assert.Equal(1m, calc.ExcessoEmMontagem(2));   // 10 - 3 x 3
  }

  [Fact]
  public void Coletas_pendentes_listam_so_tarefa_positiva()
  {
    var calc = Calcular(
        [No(1, null, 10m, null, Corte, Dobra), No(2, 1, 5m, 1m, Corte)],
        [
          Em(1, Local.AIniciar, -4m), Em(1, Local.AguardandoColeta(Corte, 1), 4m),
          Em(2, Local.AIniciar, -5m), Em(2, Local.AguardandoColeta(Corte, 1), 5m),
        ],
        montados: new Dictionary<int, decimal> { [1] = 10m });   // o pai ja foi todo montado

    var pendentes = calc.ColetasPendentes();

    Assert.Equal(new ColetaPendente(1, Corte, 1, 4m), Assert.Single(pendentes));
  }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~Execucao"`
Expected: FAIL na compilação — `Rastreamento.Application.Execucao` não existe.

- [ ] **Step 3: `SaldoLiquido` e `Local`**

Criar `src/Rastreamento.Domain/Abstractions/SaldoLiquido.cs`:

```csharp
namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Saldo LIQUIDO de uma posicao de um no, lido do livro: soma de `Quantidade` onde ela e destino menos
/// a soma onde e origem (spec da Fase 3, secao 7.1). Na posicao AIniciar ele e zero ou negativo — o
/// saldo de verdade soma a `Quantidade` do no, e quem soma e a calculadora da Application. Vem so
/// posicao com liquido diferente de zero.
/// </summary>
public sealed record SaldoLiquido(int EstruturaItemId, string Posicao, int? SetorId, int? Ordem, decimal Quantidade);
```

Criar `src/Rastreamento.Application/Execucao/Local.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Uma posicao concreta da quantidade de um no: a posicao, e o Setor e o passo (`Ordem` do Roteiro do
/// proprio no) quando ela os tem — spec da Fase 3, secao 3.1. `record struct` de proposito: e chave de
/// dicionario de saldo, e a igualdade por valor e o que faz `AguardandoColeta(3, 2)` achar o saldo
/// gravado com os mesmos tres valores.
/// </summary>
public readonly record struct Local(string Posicao, int? SetorId, int? Ordem)
{
  public static Local AIniciar => new(Posicoes.AIniciar, null, null);
  public static Local NaExpedicao => new(Posicoes.NaExpedicao, null, null);
  public static Local Montado => new(Posicoes.Montado, null, null);
  public static Local NoSetor(int setorId, int ordem) => new(Posicoes.NoSetor, setorId, ordem);
  public static Local AguardandoColeta(int setorId, int ordem) => new(Posicoes.AguardandoColeta, setorId, ordem);
  public static Local AguardandoMontagem(int setorId) => new(Posicoes.AguardandoMontagem, setorId, null);

  public static Local DaOrigem(Movimentacao m) => new(m.OrigemPosicao, m.OrigemSetorId, m.OrigemOrdem);
  public static Local DoDestino(Movimentacao m) => new(m.DestinoPosicao, m.DestinoSetorId, m.DestinoOrdem);
}
```

- [ ] **Step 4: `Livro`**

Criar `src/Rastreamento.Application/Execucao/Livro.cs`:

```csharp
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// A soma do livro em C#: por movimento, +Quantidade no destino e -Quantidade na origem, agrupado por
/// no e posicao. E a MESMA conta que `ExecucaoRepository.ListarSaldosAsync` faz em SQL (spec secao
/// 7.1), e existe para duas coisas: o fake de teste somar igual ao banco, e
/// `Saldos_do_banco_batem_com_a_soma_em_CSharp` provar que as duas somam igual. O estorno nao tem
/// filtro especial: e um movimento inverso, e a soma o absorve.
/// </summary>
public static class Livro
{
  public static IReadOnlyList<SaldoLiquido> SomarSaldos(IEnumerable<Movimentacao> movimentos)
  {
    var soma = new Dictionary<(int Item, Local Local), decimal>();
    foreach (var m in movimentos)
    {
      Somar(soma, (m.EstruturaItemId, Local.DoDestino(m)), m.Quantidade);
      Somar(soma, (m.EstruturaItemId, Local.DaOrigem(m)), -m.Quantidade);
    }

    return soma
        .Where(kv => kv.Value != 0m)
        .OrderBy(kv => kv.Key.Item)
        .ThenBy(kv => OrdemDaPosicao(kv.Key.Local.Posicao))
        .ThenBy(kv => kv.Key.Local.Ordem ?? 0)
        .ThenBy(kv => kv.Key.Local.SetorId ?? 0)
        .Select(kv => new SaldoLiquido(
            kv.Key.Item, kv.Key.Local.Posicao, kv.Key.Local.SetorId, kv.Key.Local.Ordem, kv.Value))
        .ToList();
  }

  /// <summary>A posicao na ordem de `Posicoes.Todas` — a ordem em que as telas a listam.</summary>
  public static int OrdemDaPosicao(string posicao)
  {
    for (var i = 0; i < Posicoes.Todas.Count; i++)
      if (Posicoes.Todas[i] == posicao) return i;
    return int.MaxValue;
  }

  private static void Somar(Dictionary<(int Item, Local Local), decimal> soma, (int Item, Local Local) chave, decimal valor) =>
      soma[chave] = soma.GetValueOrDefault(chave) + valor;
}
```

- [ ] **Step 5: `CalculadoraDeExecucao`**

Criar `src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs`:

```csharp
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

public readonly record struct PassoDoCalculo(int SetorId, int Ordem);

/// <summary>Um no como a calculadora o ve. `Roteiro` em ordem de `Ordem`.</summary>
public sealed record NoDoCalculo(
    int Id, int? PaiId, decimal Quantidade, decimal? QuantidadePorPai, IReadOnlyList<PassoDoCalculo> Roteiro)
{
  public bool EhPeca => PaiId is null;
}

public enum TipoDeDestino { ProximoPasso, Expedicao, Montagem }

/// <summary>
/// Para onde vai o que aguarda coleta (spec secao 7.3). `Passo` so em `ProximoPasso`; `PaiId`,
/// `SugestaoSetorId` e `SetoresPossiveis` so em `Montagem`. Pai sem Roteiro: lista vazia, sem sugestao
/// — a entrega e recusada com `PaiSemRoteiro`, e o item aparece assim mesmo (desvio D7 do plano 2).
/// </summary>
public sealed record DestinoCalculado(
    TipoDeDestino Tipo, PassoDoCalculo? Passo, int? PaiId, int? SugestaoSetorId, IReadOnlyList<int> SetoresPossiveis)
{
  public bool PaiSemRoteiro => Tipo == TipoDeDestino.Montagem && SetoresPossiveis.Count == 0;
}

/// <summary>Um filho direto na conta de "da para montar" (spec secao 7.6).</summary>
public sealed record FilhoNaMontagem(
    int FilhoId, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima);

public sealed record Montabilidade(
    int PaiId, int SetorId, decimal FaltaMontar, decimal DaParaMontar, IReadOnlyList<FilhoNaMontagem> Filhos);

/// <summary>Um "Item pronto" (regra 23): o que aguarda coleta no passo e ainda tem para onde ir.</summary>
public sealed record ColetaPendente(int EstruturaItemId, int SetorId, int Ordem, decimal Tarefa);

/// <summary>
/// A regra da fila e das tarefas (spec da Fase 3, secao 7), em funcoes puras. Recebe os nos, os saldos
/// liquidos do livro, os totais montados e os passos ja alcancados; nao le nada. As escritas validam
/// com as MESMAS funcoes que a leitura mostra — e isso que fecha, por construcao, a tela oferecer
/// "montar 3" e a API recusar 3 (secao 7.2).
///
/// Enxerga so os nos que recebe: o destino e a tarefa de um Item precisam do PAI na entrada. Ver o
/// "Contrato de uso" do plano 2, Task 3.
/// </summary>
public sealed class CalculadoraDeExecucao
{
  private static readonly IReadOnlyDictionary<Local, decimal> SemMovimento = new Dictionary<Local, decimal>();

  private readonly Dictionary<int, NoDoCalculo> _nos;
  private readonly ILookup<int, NoDoCalculo> _filhos;
  private readonly Dictionary<int, Dictionary<Local, decimal>> _liquido = new();
  private readonly IReadOnlyDictionary<int, decimal> _totaisMontados;
  private readonly Dictionary<int, HashSet<int>> _alcancados = new();

  public CalculadoraDeExecucao(
      IEnumerable<NoDoCalculo> nos,
      IEnumerable<SaldoLiquido> saldos,
      IReadOnlyDictionary<int, decimal> totaisMontados,
      IEnumerable<(int EstruturaItemId, int Ordem)> passosAlcancados)
  {
    _nos = nos.ToDictionary(n => n.Id);
    _filhos = _nos.Values.Where(n => n.PaiId is not null).OrderBy(n => n.Id).ToLookup(n => n.PaiId!.Value);

    foreach (var s in saldos)
    {
      if (!_liquido.TryGetValue(s.EstruturaItemId, out var doNo))
        _liquido[s.EstruturaItemId] = doNo = new Dictionary<Local, decimal>();
      var local = new Local(s.Posicao, s.SetorId, s.Ordem);
      doNo[local] = doNo.GetValueOrDefault(local) + s.Quantidade;
    }

    _totaisMontados = totaisMontados;

    foreach (var (item, ordem) in passosAlcancados)
    {
      if (!_alcancados.TryGetValue(item, out var doNo))
        _alcancados[item] = doNo = new HashSet<int>();
      doNo.Add(ordem);
    }
  }

  public IEnumerable<NoDoCalculo> Nos => _nos.Values.OrderBy(n => n.Id);

  public bool Conhece(int id) => _nos.ContainsKey(id);

  public NoDoCalculo No(int id) => _nos[id];

  public IReadOnlyList<NoDoCalculo> Filhos(int paiId) => _filhos[paiId].ToList();

  public bool TemFilhos(int id) => _filhos[id].Any();

  private IReadOnlyDictionary<Local, decimal> LiquidoDo(int id) =>
      _liquido.TryGetValue(id, out var doNo) ? doNo : SemMovimento;

  /// <summary>AIniciar soma a quantidade do no; as demais posicoes sao so o liquido do livro.</summary>
  public decimal Saldo(int id, Local local) =>
      (local == Local.AIniciar ? _nos[id].Quantidade : 0m) + LiquidoDo(id).GetValueOrDefault(local);

  /// <summary>O que ja saiu de "a iniciar", descontado o que voltou por estorno.</summary>
  public decimal SaidoDeAIniciar(int id) => -LiquidoDo(id).GetValueOrDefault(Local.AIniciar);

  /// <summary>Toda posicao com saldo diferente de zero, na ordem de `Posicoes.Todas`, passo e Setor.</summary>
  public IReadOnlyList<(Local Local, decimal Quantidade)> Saldos(int id) =>
      LiquidoDo(id).Keys.Append(Local.AIniciar).Distinct()
          .Select(l => (Local: l, Quantidade: Saldo(id, l)))
          .Where(x => x.Quantidade != 0m)
          .OrderBy(x => Livro.OrdemDaPosicao(x.Local.Posicao))
          .ThenBy(x => x.Local.Ordem ?? 0)
          .ThenBy(x => x.Local.SetorId ?? 0)
          .ToList();

  public decimal TotalMontado(int id) => _totaisMontados.GetValueOrDefault(id);

  public decimal FaltaMontar(int paiId) => _nos[paiId].Quantidade - TotalMontado(paiId);

  public decimal AguardandoMontagem(int filhoId, int setorId) => Saldo(filhoId, Local.AguardandoMontagem(setorId));

  public decimal AguardandoMontagemTotal(int filhoId) =>
      LiquidoDo(filhoId).Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem).Sum(kv => kv.Value);

  public IReadOnlyList<int> SetoresOndeAguardaMontagem(int filhoId) =>
      LiquidoDo(filhoId)
          .Where(kv => kv.Key.Posicao == Posicoes.AguardandoMontagem && kv.Value > 0m)
          .Select(kv => kv.Key.SetorId!.Value)
          .OrderBy(s => s)
          .ToList();

  /// <summary>
  /// Razao nula so existiria com dado incoerente, que `CK_EstruturaItem_QuantidadePorPai` nao deixa
  /// existir; conta como zero em vez de estourar.
  /// </summary>
  private static decimal Razao(NoDoCalculo no) => no.QuantidadePorPai ?? 0m;

  /// <summary>
  /// O que o pai ainda precisa receber deste filho (spec secao 7.4):
  /// max(0, (Quantidade(P) - totalMontado(P)) x QuantidadePorPai(c) - soma de AguardandoMontagem(c)).
  /// </summary>
  public decimal Precisa(int filhoId)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return 0m;
    return Math.Max(0m, FaltaMontar(paiId) * Razao(filho) - AguardandoMontagemTotal(filhoId));
  }

  public PassoDoCalculo? PrimeiroPasso(int id) => _nos[id].Roteiro.Count == 0 ? null : _nos[id].Roteiro[0];

  /// <summary>O passo depois de `ordem` — por passo, nao por Setor (regra 21: o Setor pode repetir).</summary>
  public PassoDoCalculo? ProximoPasso(int id, int ordem)
  {
    foreach (var passo in _nos[id].Roteiro)
      if (passo.Ordem > ordem) return passo;
    return null;
  }

  public IReadOnlySet<int> PassosAlcancados(int id) =>
      _alcancados.TryGetValue(id, out var doNo) ? doNo : new HashSet<int>();

  public IReadOnlyList<int> SetoresDoRoteiro(int id) =>
      _nos.TryGetValue(id, out var no) ? no.Roteiro.Select(p => p.SetorId).Distinct().ToList() : Array.Empty<int>();

  public DestinoCalculado DestinoDaColeta(int id, int ordem)
  {
    if (ProximoPasso(id, ordem) is PassoDoCalculo proximo)
      return new DestinoCalculado(TipoDeDestino.ProximoPasso, proximo, null, null, Array.Empty<int>());

    if (_nos[id].PaiId is not int paiId)
      return new DestinoCalculado(TipoDeDestino.Expedicao, null, null, null, Array.Empty<int>());

    return new DestinoCalculado(
        TipoDeDestino.Montagem, null, paiId, SugestaoDeMontagem(paiId), SetoresDoRoteiro(paiId));
  }

  /// <summary>
  /// Spec secao 7.3, item 3: o Setor onde o pai esta em trabalho (o de maior saldo; empate, o de menor
  /// Id); senao o do primeiro passo do pai ainda nao alcancado; senao nenhuma.
  /// </summary>
  public int? SugestaoDeMontagem(int paiId)
  {
    if (!_nos.TryGetValue(paiId, out var pai) || pai.Roteiro.Count == 0) return null;

    var emTrabalho = LiquidoDo(paiId)
        .Where(kv => kv.Key.Posicao == Posicoes.NoSetor && kv.Value > 0m)
        .GroupBy(kv => kv.Key.SetorId!.Value)
        .Select(g => (SetorId: g.Key, Quantidade: g.Sum(kv => kv.Value)))
        .OrderByDescending(x => x.Quantidade)
        .ThenBy(x => x.SetorId)
        .ToList();
    if (emTrabalho.Count > 0) return emTrabalho[0].SetorId;

    var alcancados = PassosAlcancados(paiId);
    foreach (var passo in pai.Roteiro)
      if (!alcancados.Contains(passo.Ordem)) return passo.SetorId;
    return null;
  }

  /// <summary>
  /// A tarefa do que aguarda coleta no passo (spec secao 7.4): inteira se ainda ha passo ou se e Peca;
  /// no ultimo passo de um Item, limitada ao que o pai ainda precisa receber.
  /// </summary>
  public decimal Tarefa(int id, int setorId, int ordem)
  {
    var saldo = Saldo(id, Local.AguardandoColeta(setorId, ordem));
    if (saldo <= 0m) return 0m;
    if (ProximoPasso(id, ordem) is not null || _nos[id].EhPeca) return saldo;
    return Math.Min(saldo, Precisa(id));
  }

  /// <summary>A parte do que aguarda coleta que nao e tarefa — so existe no ultimo passo de um Item.</summary>
  public decimal SobraDaColeta(int id, int setorId, int ordem) =>
      Math.Max(0m, Saldo(id, Local.AguardandoColeta(setorId, ordem)) - Tarefa(id, setorId, ordem));

  /// <summary>O que aguarda montagem alem do que o pai precisa (spec secao 7.5), no nivel do no.</summary>
  public decimal ExcessoEmMontagem(int filhoId)
  {
    var filho = _nos[filhoId];
    if (filho.PaiId is not int paiId || !_nos.ContainsKey(paiId)) return 0m;
    return Math.Max(0m, AguardandoMontagemTotal(filhoId) - FaltaMontar(paiId) * Razao(filho));
  }

  /// <summary>
  /// "Da para montar N; falta X de Y" no Setor (spec secao 7.6):
  /// N = min(falta montar, min por filho de floor(presente / razao)); o "falta" e o que cada filho
  /// precisa para a unidade N+1, so enquanto N+1 cabe no que falta montar.
  /// </summary>
  public Montabilidade CalcularMontabilidade(int paiId, int setorId)
  {
    var falta = Math.Max(0m, FaltaMontar(paiId));
    var filhos = Filhos(paiId);
    if (filhos.Count == 0) return new Montabilidade(paiId, setorId, falta, 0m, Array.Empty<FilhoNaMontagem>());

    var n = falta;
    foreach (var c in filhos)
    {
      var razao = Razao(c);
      var possivel = razao == 0m ? 0m : Math.Floor(AguardandoMontagem(c.Id, setorId) / razao);
      n = Math.Min(n, possivel);
    }

    var proxima = n + 1m;
    var haProxima = proxima <= falta;
    var lista = filhos.Select(c =>
    {
      var razao = Razao(c);
      var presente = AguardandoMontagem(c.Id, setorId);
      decimal? necessario = haProxima ? proxima * razao : null;
      decimal? faltaParaProxima = haProxima ? Math.Max(0m, proxima * razao - presente) : null;
      return new FilhoNaMontagem(c.Id, razao, presente, necessario, faltaParaProxima);
    }).ToList();

    return new Montabilidade(paiId, setorId, falta, n, lista);
  }

  /// <summary>Todo (no, passo) que aguarda coleta com tarefa positiva, por Setor, no e passo.</summary>
  public IReadOnlyList<ColetaPendente> ColetasPendentes()
  {
    var lista = new List<ColetaPendente>();
    foreach (var (id, locais) in _liquido)
    {
      if (!_nos.ContainsKey(id)) continue;
      foreach (var local in locais.Keys)
      {
        if (local.Posicao != Posicoes.AguardandoColeta) continue;
        var tarefa = Tarefa(id, local.SetorId!.Value, local.Ordem!.Value);
        if (tarefa > 0m) lista.Add(new ColetaPendente(id, local.SetorId.Value, local.Ordem.Value, tarefa));
      }
    }

    return lista.OrderBy(c => c.SetorId).ThenBy(c => c.EstruturaItemId).ThenBy(c => c.Ordem).ToList();
  }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~Execucao"`
Expected: PASS (2 de `LivroTests`, 15 de `CalculadoraDeExecucaoTests`).

Mutação de conferência, a registrar no relatório: em `Tarefa`, troque `Math.Min(saldo, Precisa(id))`
por `saldo` — `Contraexemplo_b_da_spec_do_Kit_a_sobra_de_D_nao_vira_tarefa` tem de falhar. Em
`ProximoPasso`, compare por `SetorId` em vez de `Ordem` — `Destino_segue_o_passo_mesmo_com_setor_repetido`
tem de falhar. Desfaça as duas.

- [ ] **Step 7: Build e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror`
Expected: 0 warnings.

```bash
git add src/Rastreamento.Domain/Abstractions/SaldoLiquido.cs src/Rastreamento.Application/Execucao \
  tests/Rastreamento.Application.Tests/Execucao
git commit -m "feat(execucao): calculadora de saldo, destino, tarefa, sobra e montagem"
```

### Task 4: O repositório da execução — transação, trava e leitura do livro

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/ErrosDoSqlServer.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/ReceitaPadraoRepository.cs` (`EhConflitoDeConcorrencia`
  passa a delegar)
- Create: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `src/Rastreamento.Api/Program.cs` (registro)
- Modify: `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`

**Interfaces:**
- Consumes: `Movimentacao`, `Montagem`, `Posicoes`, `TiposDeMovimentacao` (Task 2); `SaldoLiquido`,
  `Livro`, `Local` (Task 3); `FakeEstruturaRepo` (existente).
- Produces: `IExecucaoRepository` (abaixo, na íntegra); `ContextoDoNo(EstruturaItem No, int PedidoId, string PedidoNumero, string PedidoStatus, int AgrupamentoId, string AgrupamentoCodigo)`;
  `PedidoDoNo(int PedidoId, string Status)`; `ExecucaoRepository`; `FakeExecucaoRepo(FakeEstruturaRepo)`
  com `Movimentacoes`, `Montagens`, `Agrupamentos` (AgrupamentoId → `(Codigo, PedidoId, PedidoNumero)`),
  `StatusDoPedido`, `Usuarios`, `Travas`, `Transacoes`, `Saves`, `ConflitoNaProximaTransacao`,
  `Semear(Movimentacao)`.

- [ ] **Step 1: A interface**

Criar `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Um no com o Pedido e o Agrupamento em que vive — o que fila e tarefas precisam para mostrar o
/// caminho "Pedido > Agrupamento > pai" sem uma consulta por linha.
/// </summary>
public sealed record ContextoDoNo(
    EstruturaItem No, int PedidoId, string PedidoNumero, string PedidoStatus, int AgrupamentoId, string AgrupamentoCodigo);

public sealed record PedidoDoNo(int PedidoId, string Status);

/// <summary>
/// O livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele (spec da Fase 3,
/// secoes 7 e 8). Toda leitura devolve dado SOLTO (sem change tracking); as escritas sao
/// `Adicionar` + `SalvarAlteracoesAsync`, e as duas unicas atualizacoes (`MarcarPedidoEmProducaoAsync`,
/// `MarcarMontagemEstornadaAsync`) sao conjuntistas — nenhuma toca `Movimentacao`, que e so de inclusao.
/// </summary>
public interface IExecucaoRepository
{
  /// <summary>
  /// Roda `trabalho` numa transacao SERIALIZABLE e commita. Deadlock e lock timeout (1205/1222) sobem
  /// como <see cref="ConflitoDeConcorrenciaException"/>. O `trabalho` so escreve depois de validar
  /// tudo: um `Result` de falha devolvido de dentro dele commita uma transacao sem escrita nenhuma.
  /// </summary>
  Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct);

  /// <summary>
  /// Trava (UPDLOCK, HOLDLOCK) as linhas de `EstruturaItem`, UMA A UMA, em ordem crescente de Id, e as
  /// devolve. Id inexistente simplesmente nao volta. So vale dentro de <see cref="EmTransacaoAsync"/>:
  /// fora dela a trava acabaria no fim do SELECT, e o metodo lanca.
  /// </summary>
  Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct);

  /// <summary>O proprio no e todos os descendentes; vazio se o no nao existe.</summary>
  Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct);

  /// <summary>Todo no de Pedido que nao esta `Concluido` nem `Cancelado` — o escopo de fila e tarefas.</summary>
  Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct);

  Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct);

  /// <summary>`Aberto` passa a `EmProducao`; qualquer outro status fica como esta (regra 28).</summary>
  Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct);

  /// <summary>Saldo liquido por no e posicao (spec secao 7.1), a mesma conta de `Livro.SomarSaldos`.</summary>
  Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>Soma das montagens NAO estornadas, por no pai. No sem montagem nao aparece.</summary>
  Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>Toda `Ordem` que aparece como origem ou destino no livro do no (spec secao 4.6).</summary>
  Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct);

  Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct);

  /// <summary>Dos movimentos pedidos, os que ja tem um Estorno apontando para eles.</summary>
  Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct);

  Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct);

  /// <summary>As baixas de filho (Tipo = Montagem) das montagens pedidas — sem os estornos delas.</summary>
  Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct);

  Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct);

  Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

  /// <summary>
  /// Apaga os passos do Roteiro do no com `Ordem` maior que `ultimaOrdemTravada` (todos, se nula) e
  /// grava `novos`. Passo alcancado e historico e nao sai (spec secao 4.6).
  /// </summary>
  Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct);

  void Adicionar(Movimentacao movimentacao);

  void Adicionar(Montagem montagem);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 2: O fake, que os casos de uso das Tasks 5 a 9 usam**

Criar `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Fake em memoria de <see cref="IExecucaoRepository"/>. Compartilha `Itens` e `Roteiros` com o
/// <see cref="FakeEstruturaRepo"/> que recebe, porque o caso de uso le a arvore por um e o livro pelo
/// outro. Soma saldo com `Livro.SomarSaldos` — a mesma conta que o teste de banco
/// `Saldos_do_banco_batem_com_a_soma_em_CSharp` prova igual ao SQL. `Adicionar` so vira linha no
/// `SalvarAlteracoesAsync`, como no EF; o que ficou pendente ao fim da transacao e descartado.
/// </summary>
public class FakeExecucaoRepo : IExecucaoRepository
{
  private readonly FakeEstruturaRepo _estruturas;
  private readonly List<Movimentacao> _movimentosPendentes = new();
  private readonly List<Montagem> _montagensPendentes = new();
  private int _proximoId = 5000;
  private bool _emTransacao;

  public FakeExecucaoRepo(FakeEstruturaRepo estruturas) => _estruturas = estruturas;

  public List<Movimentacao> Movimentacoes { get; } = new();
  public List<Montagem> Montagens { get; } = new();

  /// <summary>AgrupamentoId -> (Codigo, PedidoId, PedidoNumero). Arranjo do teste.</summary>
  public Dictionary<int, (string Codigo, int PedidoId, string PedidoNumero)> Agrupamentos { get; } = new();

  public Dictionary<int, string> StatusDoPedido { get; } = new();
  public Dictionary<int, string> Usuarios { get; } = new();

  /// <summary>Os Ids de cada `TravarNosAsync`, como chegaram — prova de QUEM o caso de uso trava.</summary>
  public List<IReadOnlyList<int>> Travas { get; } = new();

  public int Transacoes { get; private set; }
  public int Saves { get; private set; }

  /// <summary>A proxima transacao sobe o que o repositorio real sobe num deadlock.</summary>
  public bool ConflitoNaProximaTransacao { get; set; }

  public async Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct)
  {
    if (ConflitoNaProximaTransacao)
    {
      ConflitoNaProximaTransacao = false;
      throw new ConflitoDeConcorrenciaException(new InvalidOperationException("deadlock simulado"));
    }

    Transacoes++;
    _emTransacao = true;
    try
    {
      return await trabalho();
    }
    finally
    {
      _emTransacao = false;
      _movimentosPendentes.Clear();
      _montagensPendentes.Clear();
    }
  }

  public Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    if (!_emTransacao) throw new InvalidOperationException("TravarNosAsync fora de EmTransacaoAsync.");
    var pedidos = ids.ToList();
    Travas.Add(pedidos);
    return Task.FromResult<IReadOnlyList<EstruturaItem>>(
        _estruturas.Itens.Where(i => pedidos.Contains(i.Id)).OrderBy(i => i.Id).ToList());
  }

  public Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaItem>>(
          _estruturas.Itens.Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToList());

  public Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<EstruturaItem>>(
          _estruturas.Itens.Where(i => i.EstruturaPaiId == paiId).OrderBy(i => i.Id).ToList());

  public Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = _estruturas.Itens.Where(i => i.Id == id).Select(i => i.Id).ToList();
    while (fronteira.Count > 0)
    {
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = _estruturas.Itens
          .Where(i => i.EstruturaPaiId is int pai && atual.Contains(pai)).Select(i => i.Id).ToList();
    }
    return Task.FromResult<IReadOnlyList<int>>(ids);
  }

  public Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct)
  {
    var lista = new List<ContextoDoNo>();
    foreach (var item in _estruturas.Itens.OrderBy(i => i.Id))
    {
      if (!Agrupamentos.TryGetValue(item.AgrupamentoId, out var ag)) continue;
      var status = StatusDoPedido[ag.PedidoId];
      if (status is "Concluido" or "Cancelado") continue;
      lista.Add(new ContextoDoNo(item, ag.PedidoId, ag.PedidoNumero, status, item.AgrupamentoId, ag.Codigo));
    }
    return Task.FromResult<IReadOnlyList<ContextoDoNo>>(lista);
  }

  public Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct)
  {
    var item = _estruturas.Itens.SingleOrDefault(i => i.Id == estruturaItemId);
    if (item is null || !Agrupamentos.TryGetValue(item.AgrupamentoId, out var ag))
      return Task.FromResult<PedidoDoNo?>(null);
    return Task.FromResult<PedidoDoNo?>(new PedidoDoNo(ag.PedidoId, StatusDoPedido[ag.PedidoId]));
  }

  public Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct)
  {
    if (StatusDoPedido.GetValueOrDefault(pedidoId) == "Aberto") StatusDoPedido[pedidoId] = "EmProducao";
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult(Livro.SomarSaldos(Movimentacoes.Where(m => ids.Contains(m.EstruturaItemId))));

  public Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, decimal>>(Montagens
          .Where(g => ids.Contains(g.EstruturaItemId) && g.EstornadaEm is null)
          .GroupBy(g => g.EstruturaItemId)
          .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade)));

  public Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var passos = new SortedSet<(int EstruturaItemId, int Ordem)>();
    foreach (var m in Movimentacoes.Where(m => ids.Contains(m.EstruturaItemId)))
    {
      if (m.OrigemOrdem is int origem) passos.Add((m.EstruturaItemId, origem));
      if (m.DestinoOrdem is int destino) passos.Add((m.EstruturaItemId, destino));
    }
    return Task.FromResult<IReadOnlyList<(int EstruturaItemId, int Ordem)>>(passos.ToList());
  }

  public Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct) =>
      Task.FromResult(Movimentacoes.SingleOrDefault(m => m.Id == id));

  public Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(
          Movimentacoes.Where(m => m.EstruturaItemId == estruturaItemId).OrderBy(m => m.Id).ToList());

  public Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlySet<int>>(Movimentacoes
          .Where(m => m.EstornoDeId is int original && movimentacaoIds.Contains(original))
          .Select(m => m.EstornoDeId!.Value)
          .ToHashSet());

  public Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct) =>
      Task.FromResult(Montagens.SingleOrDefault(g => g.Id == id));

  public Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Montagem>>(
          Montagens.Where(g => g.EstruturaItemId == paiId).OrderBy(g => g.Id).ToList());

  public Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<Movimentacao>>(Movimentacoes
          .Where(m => m.Tipo == TiposDeMovimentacao.Montagem && m.MontagemId is int g && montagemIds.Contains(g))
          .OrderBy(m => m.Id)
          .ToList());

  public Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct)
  {
    var montagem = Montagens.Single(g => g.Id == montagemId);
    if (montagem.EstornadaEm is null)
    {
      montagem.EstornadaEm = em;
      montagem.EstornadaPorUsuarioId = usuarioId;
    }
    return Task.CompletedTask;
  }

  public Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      Task.FromResult<IReadOnlyDictionary<int, string>>(
          Usuarios.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

  public Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct)
  {
    _estruturas.Roteiros.RemoveAll(r =>
        r.EstruturaItemId == estruturaItemId && (ultimaOrdemTravada is null || r.Ordem > ultimaOrdemTravada));
    foreach (var (setorId, ordem) in novos)
      _estruturas.Roteiros.Add(new EstruturaRoteiro
      {
        Id = _proximoId++, EstruturaItemId = estruturaItemId, SetorId = setorId, Ordem = ordem,
      });
    return Task.CompletedTask;
  }

  public void Adicionar(Movimentacao movimentacao) => _movimentosPendentes.Add(movimentacao);

  public void Adicionar(Montagem montagem) => _montagensPendentes.Add(montagem);

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Saves++;
    foreach (var montagem in _montagensPendentes)
    {
      montagem.Id = _proximoId++;
      Montagens.Add(montagem);
    }
    foreach (var movimento in _movimentosPendentes)
    {
      movimento.Id = _proximoId++;
      Movimentacoes.Add(movimento);
    }
    _montagensPendentes.Clear();
    _movimentosPendentes.Clear();
    return Task.CompletedTask;
  }

  /// <summary>Arranjo de teste: grava direto no livro, sem caso de uso.</summary>
  public Movimentacao Semear(Movimentacao movimentacao)
  {
    movimentacao.Id = _proximoId++;
    Movimentacoes.Add(movimentacao);
    return movimentacao;
  }
}
```

- [ ] **Step 3: Escrever os testes de banco que falham**

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `ExecucaoRepository` contra o SQL Server real (spec da Fase 3, secao 9.2): a soma do livro em SQL
/// bate com a de C#, a trava de linha segura a segunda transacao e vira conflito, e as leituras de
/// apoio devolvem o que prometem. Toda asercao escopada nos nos do proprio cenario.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ExecucaoRepositoryTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int Peca, int ItemA, int ItemB, int Corte, int Solda)
  {
    public int[] Nos => [Peca, ItemA, ItemB];
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "exec");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var solda = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      var a = await arvore.NovoItemAsync(db, peca, 20m, 2m);
      var b = await arvore.NovoItemAsync(db, peca, 10m, 1m);
      await arvore.RoteiroAsync(db, peca, solda);
      await arvore.RoteiroAsync(db, a, corte);
      await arvore.RoteiroAsync(db, b, corte);
      await corpo(new Cenario(arvore, peca, a, b, corte, solda));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  private static Movimentacao Mov(
      Cenario c, int item, string tipo, Local de, Local para, decimal quantidade,
      int? montagemId = null, int? estornoDeId = null) => new()
  {
    EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
    MontagemId = montagemId, EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync(Movimentacao m)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private async Task<int> GravarAsync(Montagem m)
  {
    await using var db = NovoContexto();
    db.Montagens.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  /// <summary>Iniciar, terminar, entregar, estornar e montar A e B sob a Peca — todo tipo de movimento.</summary>
  private async Task<(int EntregaEstornada, int Montagem)> PovoarAsync(Cenario c)
  {
    var corte1 = Local.NoSetor(c.Corte, 1);
    var coleta1 = Local.AguardandoColeta(c.Corte, 1);
    var naSolda = Local.AguardandoMontagem(c.Solda);

    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 20m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino, corte1, coleta1, 12m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Entrega, coleta1, naSolda, 10m));

    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Inicio, Local.AIniciar, corte1, 10m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Termino, corte1, coleta1, 10m));
    var entregaB = await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Entrega, coleta1, naSolda, 10m));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Estorno, naSolda, coleta1, 10m, estornoDeId: entregaB));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Entrega, coleta1, naSolda, 6m));

    var montagem = await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 3m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 6m, montagemId: montagem));
    await GravarAsync(Mov(c, c.ItemB, TiposDeMovimentacao.Montagem, naSolda, Local.Montado, 3m, montagemId: montagem));

    await GravarAsync(Mov(c, c.Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Solda, 1), 4m));
    return (entregaB, montagem);
  }

  [Fact]
  public Task Saldos_do_banco_batem_com_a_soma_em_CSharp() => NoCenarioAsync(async c =>
  {
    await PovoarAsync(c);

    await using var db = NovoContexto();
    var doBanco = await new ExecucaoRepository(db).ListarSaldosAsync(c.Nos, CancellationToken.None);
    var movimentos = await db.Movimentacoes.AsNoTracking().Where(m => c.Nos.Contains(m.EstruturaItemId)).ToListAsync();
    var emCSharp = Livro.SomarSaldos(movimentos);

    Assert.Equal(emCSharp, doBanco);
    Assert.Contains(new SaldoLiquido(c.ItemA, Posicoes.AguardandoMontagem, c.Solda, null, 4m), doBanco);   // 10 - 6
    Assert.Contains(new SaldoLiquido(c.ItemB, Posicoes.AguardandoMontagem, c.Solda, null, 3m), doBanco);   // 6 - 3
    Assert.Contains(new SaldoLiquido(c.ItemB, Posicoes.AguardandoColeta, c.Corte, 1, 4m), doBanco);        // 10 - 10 + 10 - 6
  });

  [Fact]
  public Task Estornadas_baixas_e_livro_do_no_voltam_escopados() => NoCenarioAsync(async c =>
  {
    var (entregaEstornada, montagem) = await PovoarAsync(c);

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var livroDeB = await repo.ListarMovimentacoesDoNoAsync(c.ItemB, CancellationToken.None);
    var estornadas = await repo.ListarEstornadasAsync(livroDeB.Select(m => m.Id).ToList(), CancellationToken.None);
    var baixas = await repo.ListarBaixasAsync([montagem], CancellationToken.None);

    Assert.Equal(6, livroDeB.Count);
    Assert.True(livroDeB.Select(m => m.Id).SequenceEqual(livroDeB.Select(m => m.Id).Order()));
    Assert.Equal(new[] { entregaEstornada }, estornadas.ToArray());
    Assert.Equal(new[] { c.ItemA, c.ItemB }, baixas.Select(b => b.EstruturaItemId).ToArray());
  });

  [Fact]
  public Task Totais_montados_ignoram_montagem_estornada() => NoCenarioAsync(async c =>
  {
    await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 3m, DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });
    await GravarAsync(new Montagem
    {
      EstruturaItemId = c.Peca, SetorId = c.Solda, Quantidade = 2m, DataHora = DateTime.UtcNow.AddMinutes(-5),
      UsuarioId = c.Arvore.AutorId, EstornadaEm = DateTime.UtcNow, EstornadaPorUsuarioId = c.Arvore.AutorId,
    });

    await using var db = NovoContexto();
    var totais = await new ExecucaoRepository(db).ListarTotaisMontadosAsync(c.Nos, CancellationToken.None);

    Assert.Equal(3m, totais[c.Peca]);
    Assert.False(totais.ContainsKey(c.ItemA));
  });

  [Fact]
  public Task Passos_alcancados_juntam_origem_e_destino_sem_repetir() => NoCenarioAsync(async c =>
  {
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(c.Corte, 1), 5m));
    await GravarAsync(Mov(c, c.ItemA, TiposDeMovimentacao.Termino,
        Local.NoSetor(c.Corte, 1), Local.AguardandoColeta(c.Corte, 1), 5m));

    await using var db = NovoContexto();
    var passos = await new ExecucaoRepository(db).ListarPassosAlcancadosAsync(c.Nos, CancellationToken.None);

    Assert.Equal(new[] { (c.ItemA, 1) }, passos);
  });

  [Fact]
  public Task Trava_segura_a_segunda_transacao_e_o_timeout_vira_conflito() => NoCenarioAsync(async c =>
  {
    await using var dbA = NovoContexto();
    await using var dbB = NovoContexto();
    var repoA = new ExecucaoRepository(dbA);
    var repoB = new ExecucaoRepository(dbB);
    // Conexao de B aberta a mao, para o SET valer na MESMA conexao que a transacao dela vai usar.
    await dbB.Database.OpenConnectionAsync();
    await dbB.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 300");

    await repoA.EmTransacaoAsync(async () =>
    {
      await repoA.TravarNosAsync([c.Peca], CancellationToken.None);
      await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => repoB.EmTransacaoAsync(async () =>
      {
        await repoB.TravarNosAsync([c.Peca], CancellationToken.None);
        return 0;
      }, CancellationToken.None));
      return 0;
    }, CancellationToken.None);
  });

  [Fact]
  public Task Travar_fora_de_transacao_e_erro_de_programacao() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => new ExecucaoRepository(db).TravarNosAsync([c.Peca], CancellationToken.None));
  });

  [Fact]
  public Task Travar_devolve_so_os_nos_que_existem() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    var travados = await repo.EmTransacaoAsync(
        () => repo.TravarNosAsync([c.ItemB, int.MaxValue, c.Peca], CancellationToken.None), CancellationToken.None);

    Assert.Equal(new[] { c.Peca, c.ItemB }, travados.Select(n => n.Id).ToArray());
  });

  [Fact]
  public Task Pedido_Aberto_passa_a_EmProducao_e_outro_status_nao_muda() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    Assert.Equal(new PedidoDoNo(c.Arvore.PedidoId, "EmProducao"), await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None));

    await db.Pedidos.Where(p => p.Id == c.Arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido"));
    await repo.MarcarPedidoEmProducaoAsync(c.Arvore.PedidoId, CancellationToken.None);
    Assert.Equal("Concluido", (await repo.ObterPedidoDoNoAsync(c.ItemA, CancellationToken.None))!.Status);
  });

  [Fact]
  public Task Nos_em_producao_deixam_de_fora_Pedido_cancelado() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);

    var antes = (await repo.ListarNosEmProducaoAsync(CancellationToken.None)).Where(x => c.Nos.Contains(x.No.Id)).ToList();
    await db.Pedidos.Where(p => p.Id == c.Arvore.PedidoId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Cancelado"));
    var depois = (await repo.ListarNosEmProducaoAsync(CancellationToken.None)).Where(x => c.Nos.Contains(x.No.Id)).ToList();

    Assert.Equal(3, antes.Count);
    Assert.All(antes, x => Assert.Equal("AG-01", x.AgrupamentoCodigo));
    Assert.Empty(depois);
  });

  [Fact]
  public Task Subarvore_traz_o_no_e_os_descendentes() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var ids = await new ExecucaoRepository(db).ListarIdsDaSubarvoreAsync(c.Peca, CancellationToken.None);

    Assert.Equal(c.Nos.Order().ToArray(), ids.Order().ToArray());
  });

  [Fact]
  public Task Substituir_passos_preserva_os_travados_e_grava_os_novos() => NoCenarioAsync(async c =>
  {
    await using var escrita = NovoContexto();
    var itemC = await c.Arvore.NovoItemAsync(escrita, c.Peca, 10m, 1m);
    await c.Arvore.RoteiroAsync(escrita, itemC, c.Corte, c.Solda, c.Corte);

    await using var db = NovoContexto();
    var repo = new ExecucaoRepository(db);
    await repo.EmTransacaoAsync(async () =>
    {
      await repo.SubstituirPassosNaoAlcancadosAsync(itemC, 1, [(c.Solda, 2)], CancellationToken.None);
      return 0;
    }, CancellationToken.None);

    await using var leitura = NovoContexto();
    var roteiro = await leitura.EstruturaRoteiros.AsNoTracking()
        .Where(r => r.EstruturaItemId == itemC).OrderBy(r => r.Ordem).Select(r => new { r.SetorId, r.Ordem }).ToListAsync();
    Assert.Equal(new[] { (c.Corte, 1), (c.Solda, 2) }, roteiro.Select(r => (r.SetorId, r.Ordem)).ToArray());
  });
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~ExecucaoRepositoryTests"`
Expected: FAIL na compilação — `ExecucaoRepository` não existe.

- [ ] **Step 5: Extrair o reconhecimento de conflito**

Criar `src/Rastreamento.Infrastructure/Persistence/ErrosDoSqlServer.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Os numeros de erro do SQL Server que os repositorios traduzem. Extraido de
/// `ReceitaPadraoRepository.EhConflitoDeConcorrencia` quando o segundo consumidor chegou
/// (`ExecucaoRepository`, Fase 3): o criterio e o mesmo, e o comentario de la continua valendo.
/// </summary>
internal static class ErrosDoSqlServer
{
  /// <summary>
  /// 1205 = vitima de deadlock; 1222 = lock timeout. Percorre a CADEIA de inner exceptions, porque a
  /// profundidade varia com o caminho (SqlException pelado numa consulta, embrulhado em
  /// DbUpdateException num SaveChanges). Qualquer outro numero passa cru de proposito.
  /// </summary>
  public static bool EhConflitoDeConcorrencia(Exception e)
  {
    for (Exception? atual = e; atual is not null; atual = atual.InnerException)
      if (atual is SqlException sql && sql.Number is 1205 or 1222) return true;

    return false;
  }
}
```

Em `ReceitaPadraoRepository`, o corpo de `EhConflitoDeConcorrencia` (o XML doc dele fica) passa a:

```csharp
  private static bool EhConflitoDeConcorrencia(Exception e) => ErrosDoSqlServer.EhConflitoDeConcorrencia(e);
```

Se o `using Microsoft.Data.SqlClient;` de `ReceitaPadraoRepository` ficar sem uso, remova-o (o build é
`-warnaserror`).

- [ ] **Step 6: O repositório**

Criar `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`:

```csharp
using System.Data;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele. Ver o XML doc de
/// <see cref="IExecucaoRepository"/> para o contrato; aqui, o que so a implementacao explica.
/// </summary>
public class ExecucaoRepository : IExecucaoRepository
{
  private const string StatusAberto = "Aberto";
  private const string StatusEmProducao = "EmProducao";
  private const string StatusConcluido = "Concluido";
  private const string StatusCancelado = "Cancelado";

  private readonly RastreamentoDbContext _db;

  public ExecucaoRepository(RastreamentoDbContext db) => _db = db;

  /// <summary>
  /// SERIALIZABLE pelo mesmo motivo de `ReceitaPadraoRepository.Substituir`: a validacao le saldo e o
  /// range lock impede que outro escritor insira na faixa lida antes do commit. O desfecho legitimo
  /// de duas escritas no mesmo no (um espera o outro, ou um e derrubado) sobe como
  /// `ConflitoDeConcorrenciaException`, que o caso de uso traduz para 409.
  /// </summary>
  public async Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct)
  {
    try
    {
      await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
      var resultado = await trabalho();
      await tx.CommitAsync(ct);
      return resultado;
    }
    catch (Exception e) when (ErrosDoSqlServer.EhConflitoDeConcorrencia(e))
    {
      // O que o trabalho deixou no change tracker nao foi gravado (a transacao voltou); sem limpar,
      // o proximo SaveChanges deste contexto tentaria grava-lo de novo.
      _db.ChangeTracker.Clear();
      throw new ConflitoDeConcorrenciaException(e);
    }
  }

  /// <summary>
  /// Uma linha por comando, em ordem crescente de Id: a ordem fixa de aquisicao e o que torna deadlock
  /// raro em vez de rotineiro (spec secao 8.1). Um `WHERE Id IN (...)` so deixaria a ordem por conta do
  /// plano de execucao. `HOLDLOCK` segura a trava ate o fim da transacao; `UPDLOCK` deixa leitores
  /// passarem e faz o segundo escritor esperar.
  /// </summary>
  public async Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    if (_db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
          "TravarNosAsync so vale dentro de EmTransacaoAsync: fora de transacao a trava acaba no fim do SELECT.");

    var travados = new List<EstruturaItem>();
    foreach (var id in ids.Distinct().Order())
    {
      var linha = await _db.Estruturas
          .FromSql($"SELECT * FROM dbo.EstruturaItem WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE Id = {id}")
          .AsNoTracking()
          .SingleOrDefaultAsync(ct);
      if (linha is not null) travados.Add(linha);
    }
    return travados;
  }

  public async Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Estruturas.AsNoTracking().Where(e => lista.Contains(e.Id)).OrderBy(e => e.Id).ToListAsync(ct);
  }

  public async Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct) =>
      await _db.Estruturas.AsNoTracking().Where(e => e.EstruturaPaiId == paiId).OrderBy(e => e.Id).ToListAsync(ct);

  public async Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = await _db.Estruturas.AsNoTracking().Where(e => e.Id == id).Select(e => e.Id).ToListAsync(ct);
    var visitados = new HashSet<int>();
    while (fronteira.Count > 0)
    {
      // Mesma defesa de `EstruturaRepository.RemoverSubarvoreAsync` contra ciclo nos dados.
      foreach (var visto in fronteira)
        if (!visitados.Add(visto))
          throw new SubarvoreCiclicaException(
              $"A subarvore a partir do no {id} tem um ciclo em EstruturaPaiId: o no {visto} reaparece.");
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = await _db.Estruturas.AsNoTracking()
          .Where(e => e.EstruturaPaiId != null && atual.Contains(e.EstruturaPaiId!.Value))
          .Select(e => e.Id)
          .ToListAsync(ct);
    }
    return ids;
  }

  public async Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct) =>
      await (from e in _db.Estruturas.AsNoTracking()
             join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
             join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
             where p.Status != StatusConcluido && p.Status != StatusCancelado
             orderby e.Id
             select new ContextoDoNo(e, p.Id, p.Numero, p.Status, a.Id, a.Codigo))
          .ToListAsync(ct);

  public Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      (from e in _db.Estruturas.AsNoTracking()
       join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
       join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
       where e.Id == estruturaItemId
       select new PedidoDoNo(p.Id, p.Status))
          .SingleOrDefaultAsync(ct);

  /// <summary>Conjuntista, com a condicao no WHERE: dois inicios paralelos nao disputam uma leitura.</summary>
  public Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct) =>
      _db.Pedidos.Where(p => p.Id == pedidoId && p.Status == StatusAberto)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StatusEmProducao), ct);

  /// <summary>
  /// Dois GROUP BY no banco (destinos e origens) e a subtracao em memoria — a soma pesada fica no SQL.
  /// A ordem de saida e a de `Livro.SomarSaldos`, e e por isso que
  /// `Saldos_do_banco_batem_com_a_soma_em_CSharp` compara as duas listas inteiras.
  /// </summary>
  public async Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    if (ids.Count == 0) return [];
    var lista = ids.ToList();

    var entradas = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId))
        .GroupBy(m => new { m.EstruturaItemId, m.DestinoPosicao, m.DestinoSetorId, m.DestinoOrdem })
        .Select(g => new
        {
          g.Key.EstruturaItemId, Posicao = g.Key.DestinoPosicao, SetorId = g.Key.DestinoSetorId,
          Ordem = g.Key.DestinoOrdem, Soma = g.Sum(m => m.Quantidade),
        })
        .ToListAsync(ct);
    var saidas = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId))
        .GroupBy(m => new { m.EstruturaItemId, m.OrigemPosicao, m.OrigemSetorId, m.OrigemOrdem })
        .Select(g => new
        {
          g.Key.EstruturaItemId, Posicao = g.Key.OrigemPosicao, SetorId = g.Key.OrigemSetorId,
          Ordem = g.Key.OrigemOrdem, Soma = g.Sum(m => m.Quantidade),
        })
        .ToListAsync(ct);

    var liquido = new Dictionary<(int Item, string Posicao, int? SetorId, int? Ordem), decimal>();
    foreach (var e in entradas)
      Somar(liquido, (e.EstruturaItemId, e.Posicao, e.SetorId, e.Ordem), e.Soma);
    foreach (var s in saidas)
      Somar(liquido, (s.EstruturaItemId, s.Posicao, s.SetorId, s.Ordem), -s.Soma);

    return liquido
        .Where(kv => kv.Value != 0m)
        .OrderBy(kv => kv.Key.Item)
        .ThenBy(kv => Livro.OrdemDaPosicao(kv.Key.Posicao))
        .ThenBy(kv => kv.Key.Ordem ?? 0)
        .ThenBy(kv => kv.Key.SetorId ?? 0)
        .Select(kv => new SaldoLiquido(kv.Key.Item, kv.Key.Posicao, kv.Key.SetorId, kv.Key.Ordem, kv.Value))
        .ToList();
  }

  private static void Somar(
      Dictionary<(int Item, string Posicao, int? SetorId, int? Ordem), decimal> soma,
      (int Item, string Posicao, int? SetorId, int? Ordem) chave, decimal valor) =>
      soma[chave] = soma.GetValueOrDefault(chave) + valor;

  public async Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Montagens.AsNoTracking()
        .Where(g => lista.Contains(g.EstruturaItemId) && g.EstornadaEm == null)
        .GroupBy(g => g.EstruturaItemId)
        .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Quantidade) })
        .ToDictionaryAsync(x => x.Id, x => x.Total, ct);
  }

  public async Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    var origens = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId) && m.OrigemOrdem != null)
        .Select(m => new { m.EstruturaItemId, Ordem = m.OrigemOrdem!.Value })
        .Distinct().ToListAsync(ct);
    var destinos = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId) && m.DestinoOrdem != null)
        .Select(m => new { m.EstruturaItemId, Ordem = m.DestinoOrdem!.Value })
        .Distinct().ToListAsync(ct);

    return origens.Concat(destinos)
        .Select(x => (x.EstruturaItemId, x.Ordem))
        .Distinct()
        .OrderBy(x => x.EstruturaItemId).ThenBy(x => x.Ordem)
        .ToList();
  }

  public Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct) =>
      _db.Movimentacoes.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      await _db.Movimentacoes.AsNoTracking()
          .Where(m => m.EstruturaItemId == estruturaItemId).OrderBy(m => m.Id).ToListAsync(ct);

  public async Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct)
  {
    var lista = movimentacaoIds.ToList();
    var ids = await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.EstornoDeId != null && lista.Contains(m.EstornoDeId!.Value))
        .Select(m => m.EstornoDeId!.Value)
        .ToListAsync(ct);
    return ids.ToHashSet();
  }

  public Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct) =>
      _db.Montagens.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);

  public async Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct) =>
      await _db.Montagens.AsNoTracking().Where(g => g.EstruturaItemId == paiId).OrderBy(g => g.Id).ToListAsync(ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct)
  {
    var lista = montagemIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.Tipo == TiposDeMovimentacao.Montagem && m.MontagemId != null && lista.Contains(m.MontagemId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  /// <summary>
  /// Conjuntista e condicionado a `EstornadaEm IS NULL`: e a unica escrita que uma `Montagem` recebe
  /// depois de nascer, e nunca sobrescreve um estorno anterior.
  /// </summary>
  public Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct) =>
      _db.Montagens.Where(g => g.Id == montagemId && g.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(g => g.EstornadaEm, (DateTime?)em)
              .SetProperty(g => g.EstornadaPorUsuarioId, (int?)usuarioId), ct);

  public async Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Usuarios.AsNoTracking()
        .Where(u => lista.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
  }

  public async Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct)
  {
    // DELETE antes do INSERT: `UQ_EstruturaRoteiro (EstruturaItemId, Ordem)` recusaria um passo novo
    // com a Ordem de um que ainda nao saiu.
    await _db.EstruturaRoteiros
        .Where(r => r.EstruturaItemId == estruturaItemId && (ultimaOrdemTravada == null || r.Ordem > ultimaOrdemTravada))
        .ExecuteDeleteAsync(ct);
    _db.EstruturaRoteiros.AddRange(novos.Select(p => new EstruturaRoteiro
    {
      EstruturaItemId = estruturaItemId, SetorId = p.SetorId, Ordem = p.Ordem,
    }));
    await _db.SaveChangesAsync(ct);
  }

  public void Adicionar(Movimentacao movimentacao) => _db.Movimentacoes.Add(movimentacao);

  public void Adicionar(Montagem montagem) => _db.Montagens.Add(montagem);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

Em `Program.cs`, depois do bloco "Estrutura real (Fase 2)":

```csharp
// Execucao (Fase 3): o livro de movimentacoes e o que as escritas precisam em volta dele.
builder.Services.AddScoped<IExecucaoRepository, ExecucaoRepository>();
```

Em `RegistroDeDependenciasTests.Servico_e_registrado_como_Scoped`, acrescente
`[InlineData(typeof(IExecucaoRepository))]` à lista.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~ExecucaoRepositoryTests"`
Expected: PASS (11 testes).

Mutações de conferência, a registrar no relatório: tire o `HOLDLOCK` do `FromSql` —
`Trava_segura_a_segunda_transacao_e_o_timeout_vira_conflito` tem de falhar (a segunda transação passa
sem esperar); troque `m.DestinoOrdem` por `m.OrigemOrdem` no `GroupBy` das entradas —
`Saldos_do_banco_batem_com_a_soma_em_CSharp` tem de falhar. Desfaça as duas.

- [ ] **Step 8: Suíte inteira e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS.

```bash
git add src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs src/Rastreamento.Infrastructure/Persistence \
  src/Rastreamento.Api/Program.cs tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs \
  tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs \
  tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs
git commit -m "feat(execucao): repositorio do livro com transacao serializable e trava por no"
```

### Task 5: Iniciar, terminar e montar — `ApontamentoUseCase`

Nasce aqui o que as Tasks 6 a 9 reusam: os códigos de erro, a regra da quantidade, os DTOs do livro, o
carregador de estado e o projetor. E nasce a prova de concorrência da spec §9.2.

**Files:**
- Create: `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs`
- Create: `src/Rastreamento.Application/Execucao/Quantidades.cs`
- Create: `src/Rastreamento.Application/Execucao/Falhas.cs`
- Create: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs`
- Create: `src/Rastreamento.Application/Execucao/LeitorDeEstado.cs`
- Create: `src/Rastreamento.Application/Execucao/ProjetorDoLivro.cs`
- Create: `src/Rastreamento.Application/Execucao/ApontamentoUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/ApontamentoUseCaseTests.cs`
- Create: `tests/Rastreamento.Infrastructure.Tests/Persistence/CorridaNoIniciarTests.cs`

**Interfaces:**
- Consumes: `IExecucaoRepository`, `FakeExecucaoRepo` (Task 4); calculadora e `Local` (Task 3);
  `IEstruturaRepository.ListarRoteiroAsync`, `ISetorRepository.ObterPorIdAsync`,
  `IReceitaPadraoRepository.ObterComponentesPorIdAsync`/`ObterSetoresPorIdAsync` (existentes);
  `FakeSetorRepo`, `FakeReceitaPadraoRepo`, `FakeEstruturaRepo` (existentes).
- Produces:
  - `CodigosDaExecucao` — uma constante `string` por código da spec §8.2, mais `OrigemInvalida` (D3), e
    `MensagemDeConflito`.
  - `Quantidades.CabeNaColuna(decimal) : bool`, `Quantidades.Formatar(decimal) : string` (pt-BR, até 4 casas).
  - `internal static class Falhas` (`NaoEncontrado<T>()`, `QuantidadeInvalida<T>(decimal)`,
    `PedidoFechado<T>()`, `Conflito<T>(string codigo, string mensagem)`, `Validacao<T>(string codigo, string mensagem)`,
    `EstaFechado(PedidoDoNo?)`) e a extensão `ExecutarAsync<T>(this IExecucaoRepository, Func<Task<Result<T>>>, CancellationToken)`
    que traduz `ConflitoDeConcorrenciaException` em 409 `ConflitoDeConcorrencia`; `internal static class NovoMovimento`
    (`De(...)`).
  - DTOs: `LocalDto(string Posicao, int? SetorId, string? SetorNome, int? Ordem)`,
    `MovimentacaoDto(int Id, int EstruturaItemId, string Tipo, decimal Quantidade, LocalDto Origem, LocalDto Destino, int? MontagemId, int? EstornoDeId, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada)`,
    `MontagemDto(int Id, int EstruturaItemId, int SetorId, string SetorNome, decimal Quantidade, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada, IReadOnlyList<MovimentacaoDto> Baixas)`,
    `InicioDto(int SetorId, decimal Quantidade)`, `TerminoDto(int SetorId, int Ordem, decimal Quantidade)`,
    `MontagemNovaDto(int SetorId, decimal Quantidade)`.
  - `internal sealed class LeitorDeEstado(IExecucaoRepository, IEstruturaRepository, IReceitaPadraoRepository)`
    com `Task<EstadoDeExecucao> CarregarAsync(IReadOnlyCollection<EstruturaItem> nos, CancellationToken)`;
    `internal sealed record EstadoDeExecucao(CalculadoraDeExecucao Calc, IReadOnlyDictionary<int, string> Descricoes, IReadOnlyDictionary<int, string?> Codigos)`
    com `Nome(int)`.
  - `internal sealed class ProjetorDoLivro(IExecucaoRepository, IReceitaPadraoRepository)` com
    `ProjetarMovimentacoesAsync(IReadOnlyList<Movimentacao>, ct)` e `ProjetarMontagensAsync(IReadOnlyList<Montagem>, ct)`.
  - `ApontamentoUseCase(IExecucaoRepository, IEstruturaRepository, ISetorRepository, IReceitaPadraoRepository)`:
    `Iniciar(int noId, InicioDto, int usuarioId, ct) : Task<Result<MovimentacaoDto>>`,
    `Terminar(int noId, TerminoDto, int usuarioId, ct) : Task<Result<MovimentacaoDto>>`,
    `Montar(int paiId, MontagemNovaDto, int usuarioId, ct) : Task<Result<MontagemDto>>`.
  - `CenarioDeExecucao` (teste): Setores `Corte`=1, `Dobra`=2, `Solda`=3, `Pintura`=4, `Inativo`=9;
    usuários `Operador`=10, `Movimentador`=11, `Pcp`=12; `No(id, pai, quantidade, razao, params setores)`;
    `Mover(item, tipo, de, para, quantidade, usuario)`; `Apontamento()`.

**Convenção de erro desta fase.** `Result.Erro` carrega o **código** (`CodigosDaExecucao.*`) e
`Result.Detalhe` a **frase** do operador — é o que o controller da Task 10 põe em `erro` e `mensagem`.
Diferente das validações da Estrutura (Fase 2), que põem a frase no `erro`; a spec da Fase 3 fixou o
par código/frase para todos os códigos dela (§8.2).

- [ ] **Step 1: O cenário de teste**

Criar `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Uma fabrica em memoria para os casos de uso da Fase 3: um Pedido `Aberto` com um Agrupamento,
/// cinco Setores (um inativo) e tres usuarios. Os nos se descrevem como "No {id}", o que deixa as
/// mensagens conferiveis por texto. Cada task acrescenta aqui a fabrica do caso de uso que cria.
/// </summary>
internal sealed class CenarioDeExecucao
{
  public const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4, Inativo = 9;
  public const int Operador = 10, Movimentador = 11, Pcp = 12;
  public const int PedidoId = 1, AgrupamentoId = 1;

  public FakeEstruturaRepo Estruturas { get; } = new();
  public FakeExecucaoRepo Execucao { get; }
  public FakeSetorRepo Setores { get; }
  public FakeReceitaPadraoRepo Catalogo { get; } = new();

  public CenarioDeExecucao()
  {
    Execucao = new FakeExecucaoRepo(Estruturas);
    var setores = new[]
    {
      new Setor { Id = Corte, Nome = "Corte", Ativo = true },
      new Setor { Id = Dobra, Nome = "Dobra", Ativo = true },
      new Setor { Id = Solda, Nome = "Solda", Ativo = true },
      new Setor { Id = Pintura, Nome = "Pintura", Ativo = true },
      new Setor { Id = Inativo, Nome = "Serra antiga", Ativo = false },
    };
    Setores = new FakeSetorRepo(setores);
    Catalogo.Setores.AddRange(setores);
    Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-01");
    Execucao.StatusDoPedido[PedidoId] = "Aberto";
    Execucao.Usuarios[Operador] = "Operador do Corte";
    Execucao.Usuarios[Movimentador] = "Movimentador";
    Execucao.Usuarios[Pcp] = "PCP";
  }

  /// <summary>Um no com Roteiro de um passo por Setor, `Ordem` 1, 2, 3... Sem Setor: sem Roteiro.</summary>
  public int No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores)
  {
    Estruturas.Itens.Add(new EstruturaItem
    {
      Id = id, AgrupamentoId = AgrupamentoId, Descricao = $"No {id}", EstruturaPaiId = pai,
      NivelHierarquico = pai is null ? "Peca" : "Item", Quantidade = quantidade, QuantidadePorPai = razao,
    });
    for (var i = 0; i < setores.Length; i++)
      Estruturas.Roteiros.Add(new EstruturaRoteiro
      {
        Id = 100 * id + i + 1, EstruturaItemId = id, SetorId = setores[i], Ordem = i + 1,
      });
    return id;
  }

  /// <summary>Arranjo direto no livro, sem caso de uso.</summary>
  public Movimentacao Mover(int item, string tipo, Local de, Local para, decimal quantidade, int usuario = Operador) =>
      Execucao.Semear(new Movimentacao
      {
        EstruturaItemId = item, Tipo = tipo, Quantidade = quantidade,
        OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
        DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
        DataHora = DateTime.UtcNow, UsuarioId = usuario,
      });

  public ApontamentoUseCase Apontamento() => new(Execucao, Estruturas, Setores, Catalogo);
}
```

- [ ] **Step 2: Escrever os testes que falham**

Criar `tests/Rastreamento.Application.Tests/Execucao/ApontamentoUseCaseTests.cs`:

```csharp
using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Iniciar, terminar e montar (spec da Fase 3, secoes 4.1, 4.2 e 4.4): um teste por codigo de erro.</summary>
public class ApontamentoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  // ------------------------------------------------------------------ iniciar

  [Fact]
  public async Task Iniciar_leva_de_a_iniciar_para_o_primeiro_passo_e_poe_o_Pedido_em_producao()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 4m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Inicio, r.Valor!.Tipo);
    Assert.Equal(new LocalDto(Posicoes.AIniciar, null, null, null), r.Valor.Origem);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor.Destino);
    Assert.Equal("Operador do Corte", r.Valor.UsuarioNome);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Iniciar_sem_Roteiro_da_SemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SemRoteiro, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Iniciar_fora_do_primeiro_passo_da_NaoEhOPrimeiroPasso()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Dobra, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.NaoEhOPrimeiroPasso, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Iniciar_acima_do_saldo_a_iniciar_da_SaldoInsuficiente_com_os_numeros()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 5m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 4 de No 1", r.Detalhe);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Iniciar_em_Pedido_fechado_da_PedidoFechado(string status)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = status;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00005")]
  [InlineData("1.12345")]
  public async Task Quantidade_com_mais_de_quatro_casas_e_recusada(string quantidade)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);

    var r = await c.Apontamento().Iniciar(
        1, new InicioDto(Corte, decimal.Parse(quantidade, CultureInfo.InvariantCulture)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);   // recusado antes de abrir transacao
  }

  [Fact]
  public async Task No_ou_Setor_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);

    Assert.Equal(TipoDeErro.NaoEncontrado,
        (await c.Apontamento().Iniciar(99, new InicioDto(Corte, 1m), Operador, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado,
        (await c.Apontamento().Iniciar(1, new InicioDto(77, 1m), Operador, Ct)).TipoDoErro);
  }

  [Fact]
  public async Task Conflito_na_transacao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.ConflitoNaProximaTransacao = true;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.ConflitoDeConcorrencia, TipoDeErro.Conflito);
    Assert.Equal(CodigosDaExecucao.MensagemDeConflito, r.Detalhe);
  }

  // ------------------------------------------------------------------ terminar

  [Fact]
  public async Task Terminar_leva_para_aguardando_coleta_no_mesmo_passo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 4m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor!.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoColeta, Corte, "Corte", 1), r.Valor.Destino);
  }

  [Fact]
  public async Task Terminar_no_Corte_do_passo_um_nao_e_o_ultimo_passo()
  {
    // Regra 21: Corte -> Dobra -> Corte. O que esta no Corte e do passo 1; terminar "o passo 3" nao
    // acha nada, porque o saldo e por passo, nao por Setor.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var errado = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 3, 1m), Operador, Ct);
    var certo = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 6m), Operador, Ct);

    AfirmarFalha(errado, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("(passo 3)", errado.Detalhe);
    Assert.Equal(1, certo.Valor!.Destino.Ordem);
  }

  [Fact]
  public async Task Terminar_num_Setor_inativado_continua_valendo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Inativo);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Inativo, 1), 3m);

    var r = await c.Apontamento().Terminar(1, new TerminoDto(Inativo, 1, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
  }

  // ------------------------------------------------------------------ montar

  private static CenarioDeExecucao PaiComDoisFilhos(decimal quantidadeDoPai = 10m)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, quantidadeDoPai, null, Solda);
    c.No(2, 1, 2m * quantidadeDoPai, 2m, Corte);
    c.No(3, 1, quantidadeDoPai, 1m, Corte);
    return c;
  }

  [Fact]
  public async Task Montar_grava_a_montagem_e_baixa_N_vezes_a_razao_de_cada_filho()
  {
    var c = PaiComDoisFilhos();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3m, r.Valor!.Quantidade);
    Assert.Equal("Solda", r.Valor.SetorNome);
    Assert.Equal(new[] { (2, 6m), (3, 3m) }, r.Valor.Baixas.Select(b => (b.EstruturaItemId, b.Quantidade)).ToArray());
    Assert.All(r.Valor.Baixas, b =>
    {
      Assert.Equal(TiposDeMovimentacao.Montagem, b.Tipo);
      Assert.Equal(r.Valor.Id, b.MontagemId);
      Assert.Equal(Posicoes.Montado, b.Destino.Posicao);
    });
    Assert.Equal(new[] { new[] { 1 }, new[] { 2, 3 } }, c.Execucao.Travas.Select(t => t.ToArray()).ToArray());
  }

  [Fact]
  public async Task Montar_no_sem_filhos_da_SemFilhos()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);

    AfirmarFalha(await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.SemFilhos, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Montar_acima_do_que_falta_da_MontagemAcimaDoQueFalta()
  {
    var c = PaiComDoisFilhos(quantidadeDoPai: 2m);
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 2m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.MontagemAcimaDoQueFalta, TipoDeErro.Conflito);
    Assert.Contains("Falta montar 1 de No 1", r.Detalhe);
  }

  [Fact]
  public async Task Montar_com_filho_insuficiente_nomeia_o_filho_e_nao_grava_nada()
  {
    var c = PaiComDoisFilhos();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 1m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Equal("No 3: 1 aqui, 3 necessários.", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Montar_recusa_quando_N_vezes_a_razao_passa_de_quatro_casas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 5m, 0.5m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 0.0001m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Contains("No 2", r.Detalhe);
  }

  [Fact]
  public async Task Montar_nao_exige_que_o_Setor_seja_do_Roteiro_do_pai()
  {
    // Spec secao 4.4: sem identidade de sub-lote, montar nao confere onde o pai esta.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Pintura);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 2m), Operador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Montar_em_Pedido_fechado_da_PedidoFechado()
  {
    var c = PaiComDoisFilhos();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ApontamentoUseCaseTests"`
Expected: FAIL na compilação — `ApontamentoUseCase`, `InicioDto` e os demais não existem.

- [ ] **Step 4: Códigos, quantidade e falhas**

Criar `src/Rastreamento.Application/Execucao/CodigosDaExecucao.cs`:

```csharp
namespace Rastreamento.Application.Execucao;

/// <summary>
/// Os codigos estaveis do campo `erro` da Fase 3 (spec secao 8.2; `OrigemInvalida` e o desvio D3 do
/// plano 2). O front comuta por eles; a frase para o operador vai em `Result.Detalhe`.
/// </summary>
public static class CodigosDaExecucao
{
  public const string QuantidadeInvalida = "QuantidadeInvalida";
  public const string DestinoIndevido = "DestinoIndevido";
  public const string EntregaVazia = "EntregaVazia";
  public const string RoteiroInvalido = "RoteiroInvalido";
  public const string OrigemInvalida = "OrigemInvalida";
  public const string Proibido = "Proibido";
  public const string SemRoteiro = "SemRoteiro";
  public const string NaoEhOPrimeiroPasso = "NaoEhOPrimeiroPasso";
  public const string SaldoInsuficiente = "SaldoInsuficiente";
  public const string SemFilhos = "SemFilhos";
  public const string MontagemAcimaDoQueFalta = "MontagemAcimaDoQueFalta";
  public const string FilhosInsuficientes = "FilhosInsuficientes";
  public const string DestinoForaDoRoteiroDoPai = "DestinoForaDoRoteiroDoPai";
  public const string PaiSemRoteiro = "PaiSemRoteiro";
  public const string PassoJaAlcancado = "PassoJaAlcancado";
  public const string QuantidadeAbaixoDoMovimentado = "QuantidadeAbaixoDoMovimentado";
  public const string EstornoImpossivel = "EstornoImpossivel";
  public const string JaEstornado = "JaEstornado";
  public const string PedidoFechado = "PedidoFechado";
  public const string ConflitoDeConcorrencia = "ConflitoDeConcorrencia";

  public const string MensagemDeConflito =
      "Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.";
}
```

Criar `src/Rastreamento.Application/Execucao/Quantidades.cs`:

```csharp
using System.Globalization;
using Rastreamento.Application.Estrutura;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Quantidade que o livro aceita: a faixa de `DECIMAL(18,4)` e NO MAXIMO quatro casas. A terceira
/// condicao nao existia na Fase 2 e importa aqui: o banco arredonda o que passa de quatro casas, e um
/// movimento validado com 0,00005 seria gravado com outro valor — a soma do livro deixaria de bater
/// com o que a validacao aceitou.
/// </summary>
public static class Quantidades
{
  private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

  public static bool CabeNaColuna(decimal quantidade) =>
      quantidade >= PlanejadorDeCopia.QuantidadeMinimaDaColuna
      && quantidade <= PlanejadorDeCopia.QuantidadeMaximaDaColuna
      && decimal.Round(quantidade, 4) == quantidade;

  /// <summary>Para a frase do operador: "2,5", "6", sem zeros a direita.</summary>
  public static string Formatar(decimal quantidade) => quantidade.ToString("0.####", PtBr);
}
```

Criar `src/Rastreamento.Application/Execucao/Falhas.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>As falhas que todo caso de uso da Fase 3 devolve igual — e o mesmo texto em todos.</summary>
internal static class Falhas
{
  public static Result<T> NaoEncontrado<T>() => Result<T>.Falha("NaoEncontrado", TipoDeErro.NaoEncontrado);

  public static Result<T> Validacao<T>(string codigo, string mensagem) =>
      Result<T>.Falha(codigo, TipoDeErro.Validacao, mensagem);

  public static Result<T> Conflito<T>(string codigo, string mensagem) =>
      Result<T>.Falha(codigo, TipoDeErro.Conflito, mensagem);

  public static Result<T> QuantidadeInvalida<T>(decimal quantidade) =>
      Validacao<T>(CodigosDaExecucao.QuantidadeInvalida,
          $"A quantidade {Quantidades.Formatar(quantidade)} não vale: tem de ser maior que zero e ter no máximo quatro casas decimais.");

  public static Result<T> PedidoFechado<T>() =>
      Conflito<T>(CodigosDaExecucao.PedidoFechado, "O Pedido deste item já foi concluído ou cancelado.");

  public static bool EstaFechado(PedidoDoNo? pedido) =>
      pedido is null || pedido.Status is "Concluido" or "Cancelado";

  /// <summary>
  /// A transacao da execucao, com o deadlock/lock timeout traduzido para 409. Nome diferente de
  /// `EmTransacaoAsync` de proposito: com o mesmo nome, o metodo da interface ganharia da extensao.
  /// </summary>
  public static async Task<Result<T>> ExecutarAsync<T>(
      this IExecucaoRepository execucao, Func<Task<Result<T>>> trabalho, CancellationToken ct)
  {
    try
    {
      return await execucao.EmTransacaoAsync(trabalho, ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Conflito<T>(CodigosDaExecucao.ConflitoDeConcorrencia, CodigosDaExecucao.MensagemDeConflito);
    }
  }
}

internal static class NovoMovimento
{
  public static Movimentacao De(
      int estruturaItemId, string tipo, decimal quantidade, Local de, Local para, int usuarioId,
      int? montagemId = null, int? estornoDeId = null) => new()
  {
    EstruturaItemId = estruturaItemId, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
    MontagemId = montagemId, EstornoDeId = estornoDeId, DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
  };
}
```

- [ ] **Step 5: DTOs do livro, carregador de estado e projetor**

Criar `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (a Task 9 acrescenta os de leitura):

```csharp
namespace Rastreamento.Application.Execucao;

/// <summary>Uma posicao com o nome do Setor resolvido. Ver o "Contrato JSON" do plano 2.</summary>
public sealed record LocalDto(string Posicao, int? SetorId, string? SetorNome, int? Ordem)
{
  internal static LocalDto De(Local local, IReadOnlyDictionary<int, string> nomesDosSetores) =>
      new(local.Posicao, local.SetorId,
          local.SetorId is int setorId ? nomesDosSetores.GetValueOrDefault(setorId) : null,
          local.Ordem);
}

public sealed record MovimentacaoDto(
    int Id, int EstruturaItemId, string Tipo, decimal Quantidade, LocalDto Origem, LocalDto Destino,
    int? MontagemId, int? EstornoDeId, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada);

public sealed record MontagemDto(
    int Id, int EstruturaItemId, int SetorId, string SetorNome, decimal Quantidade, DateTime DataHora,
    int UsuarioId, string UsuarioNome, bool Estornada, IReadOnlyList<MovimentacaoDto> Baixas);

public sealed record InicioDto(int SetorId, decimal Quantidade);

public sealed record TerminoDto(int SetorId, int Ordem, decimal Quantidade);

public sealed record MontagemNovaDto(int SetorId, decimal Quantidade);
```

Criar `src/Rastreamento.Application/Execucao/LeitorDeEstado.cs`:

```csharp
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>A calculadora montada sobre um conjunto de nos, e o nome de cada um para as frases.</summary>
internal sealed record EstadoDeExecucao(
    CalculadoraDeExecucao Calc, IReadOnlyDictionary<int, string> Descricoes, IReadOnlyDictionary<int, string?> Codigos)
{
  public string Nome(int id) => Descricoes.TryGetValue(id, out var descricao) ? descricao : $"no {id}";
}

/// <summary>
/// Le, para os nos pedidos, tudo o que a calculadora precisa — Roteiro, saldos, totais montados e passos
/// alcancados — em cinco consultas, nunca uma por no. A descricao segue a regra 19 (a do no, senao a do
/// Componente). Quem chama decide o conjunto: para o destino de um Item, o pai tem de estar nele.
/// </summary>
internal sealed class LeitorDeEstado
{
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;

  public LeitorDeEstado(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
  }

  public async Task<EstadoDeExecucao> CarregarAsync(IReadOnlyCollection<EstruturaItem> nos, CancellationToken ct)
  {
    var distintos = nos.DistinctBy(n => n.Id).ToList();
    var ids = distintos.Select(n => n.Id).ToList();

    var roteiros = (await _estruturas.ListarRoteiroAsync(ids, ct)).ToLookup(r => r.EstruturaItemId);
    var saldos = await _execucao.ListarSaldosAsync(ids, ct);
    var totais = await _execucao.ListarTotaisMontadosAsync(ids, ct);
    var alcancados = await _execucao.ListarPassosAlcancadosAsync(ids, ct);
    var componenteIds = distintos.Where(n => n.ComponenteId is not null).Select(n => n.ComponenteId!.Value).Distinct().ToList();
    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct)).ToDictionary(c => c.Id);

    var calc = new CalculadoraDeExecucao(
        distintos.Select(n => new NoDoCalculo(
            n.Id, n.EstruturaPaiId, n.Quantidade, n.QuantidadePorPai,
            roteiros[n.Id].OrderBy(r => r.Ordem).Select(r => new PassoDoCalculo(r.SetorId, r.Ordem)).ToList())),
        saldos, totais, alcancados);

    var descricoes = distintos.ToDictionary(
        n => n.Id,
        n => n.Descricao
             ?? (n.ComponenteId is int c && componentes.TryGetValue(c, out var comp) ? comp.Descricao : null)
             ?? $"no {n.Id}");
    var codigos = distintos.ToDictionary(
        n => n.Id,
        n => n.ComponenteId is int c && componentes.TryGetValue(c, out var comp) ? comp.Codigo : (string?)null);

    return new EstadoDeExecucao(calc, descricoes, codigos);
  }
}
```

Criar `src/Rastreamento.Application/Execucao/ProjetorDoLivro.cs`:

```csharp
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Movimentacao e Montagem para DTO, com nome de Setor e de autor resolvidos em lote e a marca de
/// estornada lida do livro — o mesmo formato em toda resposta que devolve um registro.
/// </summary>
internal sealed class ProjetorDoLivro
{
  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;

  public ProjetorDoLivro(IExecucaoRepository execucao, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _catalogo = catalogo;
  }

  public async Task<IReadOnlyList<MovimentacaoDto>> ProjetarMovimentacoesAsync(
      IReadOnlyList<Movimentacao> movimentos, CancellationToken ct)
  {
    if (movimentos.Count == 0) return [];

    var setorIds = movimentos.SelectMany(m => new[] { m.OrigemSetorId, m.DestinoSetorId }).OfType<int>().Distinct().ToList();
    var setores = await NomesDosSetoresAsync(setorIds, ct);
    var usuarios = await _execucao.ListarNomesDeUsuariosAsync(movimentos.Select(m => m.UsuarioId).Distinct().ToList(), ct);
    var estornadas = await _execucao.ListarEstornadasAsync(movimentos.Select(m => m.Id).ToList(), ct);

    return movimentos.Select(m => new MovimentacaoDto(
        m.Id, m.EstruturaItemId, m.Tipo, m.Quantidade,
        LocalDto.De(Local.DaOrigem(m), setores), LocalDto.De(Local.DoDestino(m), setores),
        m.MontagemId, m.EstornoDeId, m.DataHora, m.UsuarioId, usuarios.GetValueOrDefault(m.UsuarioId, string.Empty),
        estornadas.Contains(m.Id))).ToList();
  }

  public async Task<IReadOnlyList<MontagemDto>> ProjetarMontagensAsync(IReadOnlyList<Montagem> montagens, CancellationToken ct)
  {
    if (montagens.Count == 0) return [];

    var baixas = await _execucao.ListarBaixasAsync(montagens.Select(g => g.Id).ToList(), ct);
    var baixasPorMontagem = (await ProjetarMovimentacoesAsync(baixas, ct)).ToLookup(b => b.MontagemId);
    var setores = await NomesDosSetoresAsync(montagens.Select(g => g.SetorId).Distinct().ToList(), ct);
    var usuarios = await _execucao.ListarNomesDeUsuariosAsync(montagens.Select(g => g.UsuarioId).Distinct().ToList(), ct);

    return montagens.Select(g => new MontagemDto(
        g.Id, g.EstruturaItemId, g.SetorId, setores.GetValueOrDefault(g.SetorId, string.Empty), g.Quantidade,
        g.DataHora, g.UsuarioId, usuarios.GetValueOrDefault(g.UsuarioId, string.Empty), g.EstornadaEm is not null,
        baixasPorMontagem[g.Id].ToList())).ToList();
  }

  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      ids.Count == 0
          ? new Dictionary<int, string>()
          : (await _catalogo.ObterSetoresPorIdAsync(ids, ct)).ToDictionary(s => s.Id, s => s.Nome);
}
```

- [ ] **Step 6: O caso de uso**

Criar `src/Rastreamento.Application/Execucao/ApontamentoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O que o Operador registra no Setor: iniciar (regra 28), terminar (regra 22) e montar (regra 24) —
/// spec da Fase 3, secoes 4.1, 4.2 e 4.4. Cada escrita: valida a entrada, abre a transacao, trava os
/// nos, le o estado e valida com a calculadora, e so entao grava.
/// </summary>
public sealed class ApontamentoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly ISetorRepository _setores;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public ApontamentoUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, ISetorRepository setores,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _setores = setores;
    // Colaboradores internos, sem estado alem das dependencias que o caso de uso ja recebe — mesmo
    // criterio do `MontadorDeArvoreDeEstrutura` em `MontagemDeEstruturaUseCase`.
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public async Task<Result<MovimentacaoDto>> Iniciar(int noId, InicioDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([noId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      var pedido = await _execucao.ObterPedidoDoNoAsync(noId, ct);
      if (Falhas.EstaFechado(pedido)) return Falhas.PedidoFechado<MovimentacaoDto>();

      var estado = await _leitor.CarregarAsync(nos, ct);
      var nome = estado.Nome(noId);
      if (estado.Calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SemRoteiro,
            $"{nome} não tem Roteiro: o PCP precisa definir os passos antes da primeira entrada.");
      if (primeiro.SetorId != dto.SetorId)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.NaoEhOPrimeiroPasso,
            $"O primeiro passo de {nome} não é no {setor.Nome}.");

      var disponivel = estado.Calc.Saldo(noId, Local.AIniciar);
      if (dto.Quantidade > disponivel)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(disponivel)} de {nome} a iniciar.");

      var movimento = NovoMovimento.De(noId, TiposDeMovimentacao.Inicio, dto.Quantidade,
          Local.AIniciar, Local.NoSetor(dto.SetorId, primeiro.Ordem), usuarioId);
      _execucao.Adicionar(movimento);
      // Regra 28: o primeiro Inicio de qualquer no poe o Pedido em producao, e o status nao volta.
      await _execucao.MarcarPedidoEmProducaoAsync(pedido!.PedidoId, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([movimento], ct)).Single());
    }, ct);
  }

  public async Task<Result<MovimentacaoDto>> Terminar(int noId, TerminoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    // Setor inativo continua valendo: o que ja esta nele continua andando (spec secao 4.6).
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([noId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(noId, ct))) return Falhas.PedidoFechado<MovimentacaoDto>();

      var estado = await _leitor.CarregarAsync(nos, ct);
      var origem = Local.NoSetor(dto.SetorId, dto.Ordem);
      var disponivel = estado.Calc.Saldo(noId, origem);
      if (dto.Quantidade > disponivel)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(disponivel)} de {estado.Nome(noId)} no {setor.Nome} (passo {dto.Ordem}).");

      var movimento = NovoMovimento.De(noId, TiposDeMovimentacao.Termino, dto.Quantidade,
          origem, Local.AguardandoColeta(dto.SetorId, dto.Ordem), usuarioId);
      _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([movimento], ct)).Single());
    }, ct);
  }

  public async Task<Result<MontagemDto>> Montar(int paiId, MontagemNovaDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MontagemDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MontagemDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var pai = await _execucao.TravarNosAsync([paiId], ct);
      if (pai.Count == 0) return Falhas.NaoEncontrado<MontagemDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(paiId, ct))) return Falhas.PedidoFechado<MontagemDto>();

      var filhos = await _execucao.ListarFilhosAsync(paiId, ct);
      if (filhos.Count == 0)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.SemFilhos, "Este item não tem filhos para montar.");
      // Os filhos nasceram depois do pai, entao tem Id maior: travar o pai e depois eles mantem a
      // ordem crescente de Id que a spec secao 8.1 pede.
      var travados = await _execucao.TravarNosAsync(filhos.Select(f => f.Id), ct);

      var estado = await _leitor.CarregarAsync([.. pai, .. travados], ct);
      var falta = estado.Calc.FaltaMontar(paiId);
      if (dto.Quantidade > falta)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.MontagemAcimaDoQueFalta,
            $"Falta montar {Quantidades.Formatar(Math.Max(0m, falta))} de {estado.Nome(paiId)}.");

      var baixas = new List<(int FilhoId, decimal Quantidade)>();
      var insuficientes = new List<string>();
      foreach (var filho in travados)
      {
        var necessario = dto.Quantidade * (filho.QuantidadePorPai ?? 0m);
        if (!Quantidades.CabeNaColuna(necessario))
          return Falhas.Validacao<MontagemDto>(CodigosDaExecucao.QuantidadeInvalida,
              $"{Quantidades.Formatar(dto.Quantidade)} × {Quantidades.Formatar(filho.QuantidadePorPai ?? 0m)} de "
              + $"{estado.Nome(filho.Id)} dá {necessario}, que não cabe em quatro casas decimais.");
        var presente = estado.Calc.AguardandoMontagem(filho.Id, dto.SetorId);
        if (necessario > presente)
          insuficientes.Add($"{estado.Nome(filho.Id)}: {Quantidades.Formatar(presente)} aqui, "
              + $"{Quantidades.Formatar(necessario)} necessários");
        baixas.Add((filho.Id, necessario));
      }
      if (insuficientes.Count > 0)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.FilhosInsuficientes, string.Join("; ", insuficientes) + ".");

      var montagem = new Montagem
      {
        EstruturaItemId = paiId, SetorId = dto.SetorId, Quantidade = dto.Quantidade,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      };
      _execucao.Adicionar(montagem);
      await _execucao.SalvarAlteracoesAsync(ct);   // a baixa precisa do Id da Montagem

      foreach (var (filhoId, quantidade) in baixas)
        _execucao.Adicionar(NovoMovimento.De(filhoId, TiposDeMovimentacao.Montagem, quantidade,
            Local.AguardandoMontagem(dto.SetorId), Local.Montado, usuarioId, montagemId: montagem.Id));
      await _execucao.SalvarAlteracoesAsync(ct);

      return Result<MontagemDto>.Ok((await _projetor.ProjetarMontagensAsync([montagem], ct)).Single());
    }, ct);
  }
}
```

Em `Program.cs`, no bloco "Execucao (Fase 3)":

```csharp
builder.Services.AddScoped<ApontamentoUseCase>();
```

(com `using Rastreamento.Application.Execucao;` no topo). Em `RegistroDeDependenciasTests`, acrescente
`[InlineData(typeof(ApontamentoUseCase))]` e o `using`.

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ApontamentoUseCaseTests"`
Expected: PASS (22 testes, contando os casos das `[Theory]`).

- [ ] **Step 8: A corrida contra o banco (spec §9.2)**

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/CorridaNoIniciarTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Concorrencia do "iniciar" contra o SQL Server real (spec da Fase 3, secao 9.2), com o caso de uso de
/// producao e os repositorios reais — nenhuma logica de transacao reimplementada aqui. O primeiro teste
/// e deterministico (a trava de um contexto segura o outro ate o timeout); o segundo e a corrida de
/// verdade, que pode ou nao se sobrepor numa dada execucao — a propriedade que ele afirma vale nos dois
/// casos, e e por isso que ele nao fica intermitente.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class CorridaNoIniciarTests : TesteComBanco
{
  private static ApontamentoUseCase CasoDeUso(RastreamentoDbContext db) =>
      new(new ExecucaoRepository(db), new EstruturaRepository(db), new SetorRepository(db), new ReceitaPadraoRepository(db));

  [Fact]
  public async Task Iniciar_espera_a_trava_do_no_e_o_timeout_vira_ConflitoDeConcorrencia()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corr");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await arvore.RoteiroAsync(db, peca, corte);

      await using var dbA = NovoContexto();
      await using var dbB = NovoContexto();
      var repoA = new ExecucaoRepository(dbA);
      await dbB.Database.OpenConnectionAsync();
      await dbB.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 300");

      Result<MovimentacaoDto> enquantoTravado = null!;
      await repoA.EmTransacaoAsync(async () =>
      {
        await repoA.TravarNosAsync([peca], CancellationToken.None);
        enquantoTravado = await CasoDeUso(dbB).Iniciar(peca, new InicioDto(corte, 1m), arvore.AutorId, CancellationToken.None);
        return 0;
      }, CancellationToken.None);
      var depois = await CasoDeUso(dbB).Iniciar(peca, new InicioDto(corte, 1m), arvore.AutorId, CancellationToken.None);

      Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, enquantoTravado.Erro);
      Assert.True(depois.Sucesso);
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }

  [Fact]
  public async Task Dois_inicios_paralelos_com_saldo_para_um_so_nunca_passam_da_quantidade()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corr");
    try
    {
      var corte = await arvore.NovoSetorAsync(db);
      var peca = await arvore.NovaPecaAsync(db, 10m);
      await arvore.RoteiroAsync(db, peca, corte);

      async Task<Result<MovimentacaoDto>> IniciarSeteAsync()
      {
        await using var contexto = NovoContexto();
        return await CasoDeUso(contexto).Iniciar(peca, new InicioDto(corte, 7m), arvore.AutorId, CancellationToken.None);
      }

      var resultados = await Task.WhenAll(Task.Run(IniciarSeteAsync), Task.Run(IniciarSeteAsync));

      Assert.Single(resultados, r => r.Sucesso);
      Assert.Contains(resultados.Single(r => !r.Sucesso).Erro,
          new[] { CodigosDaExecucao.SaldoInsuficiente, CodigosDaExecucao.ConflitoDeConcorrencia });
      await using var leitura = NovoContexto();
      Assert.Equal(7m, await leitura.Movimentacoes.Where(m => m.EstruturaItemId == peca).SumAsync(m => m.Quantidade));
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~CorridaNoIniciarTests"`
Expected: PASS. Mutação de conferência, a registrar no relatório: em `ApontamentoUseCase.Iniciar`, apague
a linha do `TravarNosAsync` e troque o `nos` por `await _execucao.ListarNosAsync([noId], ct)` —
`Iniciar_espera_a_trava_do_no_e_o_timeout_vira_ConflitoDeConcorrencia` tem de falhar (a leitura com
range lock do SERIALIZABLE não conflita com o `UPDLOCK` de A). Desfaça.

- [ ] **Step 9: Suíte inteira e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS.

```bash
git add src/Rastreamento.Application/Execucao src/Rastreamento.Api/Program.cs \
  tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs tests/Rastreamento.Application.Tests/Execucao \
  tests/Rastreamento.Infrastructure.Tests/Persistence/CorridaNoIniciarTests.cs
git commit -m "feat(execucao): iniciar, terminar e montar, com trava por no e prova de corrida"
```

### Task 6: Entregar, em lista e tudo ou nada — `EntregaUseCase`

Spec §4.3: o destino de cada item sai da tabela da seção — calculado (próximo passo, local de expedição) ou
escolhido pelo Movimentador entre os Setores do Roteiro do pai (montagem e redirecionamento). **Não há
teto de sobra na entrega.**

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (DTOs da entrega)
- Create: `src/Rastreamento.Application/Execucao/EntregaUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs` (`Entrega()`)
- Create: `tests/Rastreamento.Application.Tests/Execucao/EntregaUseCaseTests.cs`

**Interfaces:**
- Consumes: tudo o que a Task 5 produziu (`Falhas`, `ExecutarAsync`, `NovoMovimento`, `LeitorDeEstado`,
  `ProjetorDoLivro`, `Quantidades`, `CodigosDaExecucao`); `CalculadoraDeExecucao.DestinoDaColeta` e
  `SetoresDoRoteiro` (Task 3).
- Produces: `OrigemDaEntregaDto(string? Posicao, int? SetorId, int? Ordem)`,
  `ItemDaEntregaDto(int EstruturaItemId, OrigemDaEntregaDto? Origem, int? DestinoSetorId, decimal Quantidade)`,
  `EntregaDto(IReadOnlyList<ItemDaEntregaDto>? Itens)`;
  `EntregaUseCase(IExecucaoRepository, IEstruturaRepository, IReceitaPadraoRepository)` com
  `Entregar(EntregaDto, int usuarioId, ct) : Task<Result<IReadOnlyList<MovimentacaoDto>>>` — a resposta na
  ordem da lista; `CenarioDeExecucao.Entrega()`.

- [ ] **Step 1: Escrever os testes que falham**

Em `CenarioDeExecucao`, depois de `Apontamento()`:

```csharp
  public EntregaUseCase Entrega() => new(Execucao, Estruturas, Catalogo);
```

Criar `tests/Rastreamento.Application.Tests/Execucao/EntregaUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Entregar (spec da Fase 3, secao 4.3). Cenario: Peca 1 de 10 com Roteiro Solda -> Pintura; Item 2 de 20
/// (razao 2) com Corte -> Dobra; Item 3 de 10 (razao 1) com Corte so.
/// </summary>
public class EntregaUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(2, 1, 20m, 2m, Corte, Dobra);
    c.No(3, 1, 10m, 1m, Corte);
    return c;
  }

  private static OrigemDaEntregaDto Coleta(int setor, int ordem) => new(Posicoes.AguardandoColeta, setor, ordem);

  private static OrigemDaEntregaDto Montagem(int setor) => new(Posicoes.AguardandoMontagem, setor, null);

  private static EntregaDto Lista(params ItemDaEntregaDto[] itens) => new(itens);

  [Fact]
  public async Task Com_passo_seguinte_vai_para_o_proximo_passo_sem_escolha()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
    var mov = Assert.Single(r.Valor!);
    Assert.Equal(TiposDeMovimentacao.Entrega, mov.Tipo);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Dobra, "Dobra", 2), mov.Destino);
    Assert.Equal(Movimentador, mov.UsuarioId);
  }

  [Fact]
  public async Task Destino_mandado_quando_ele_e_calculado_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), Solda, 8m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Item_no_ultimo_passo_vai_aguardar_montagem_no_Setor_escolhido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Item_no_ultimo_passo_sem_destino_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Setor_fora_do_Roteiro_do_pai_da_DestinoForaDoRoteiroDoPai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Dobra, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito);
    Assert.Contains("Dobra", r.Detalhe);
  }

  [Fact]
  public async Task Pai_sem_Roteiro_da_PaiSemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Peca_no_fim_do_Roteiro_vai_para_o_local_de_expedicao()
  {
    var c = Cenario();
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Pintura, 2), Local.AguardandoColeta(Pintura, 2), 4m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(1, Coleta(Pintura, 2), null, 4m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.NaExpedicao, null, null, null), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Redirecionar_o_que_aguarda_montagem_para_outro_Setor_do_pai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), Pintura, 6m)), Movimentador, Ct);

    var mov = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Pintura, "Pintura", null), mov.Destino);
  }

  [Fact]
  public async Task Redirecionar_para_o_mesmo_Setor_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), Solda, 6m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Lista_vazia_ou_ausente_da_EntregaVazia()
  {
    var c = Cenario();

    AfirmarFalha(await c.Entrega().Entregar(new EntregaDto([]), Movimentador, Ct),
        CodigosDaExecucao.EntregaVazia, TipoDeErro.Validacao);
    AfirmarFalha(await c.Entrega().Entregar(new EntregaDto(null), Movimentador, Ct),
        CodigosDaExecucao.EntregaVazia, TipoDeErro.Validacao);
  }

  [Theory]
  [InlineData(Posicoes.NoSetor, Corte, 1)]
  [InlineData(Posicoes.AguardandoColeta, Corte, null)]
  [InlineData(Posicoes.AguardandoMontagem, Solda, 1)]
  [InlineData(null, Corte, 1)]
  public async Task Origem_incoerente_da_OrigemInvalida(string? posicao, int setor, int? ordem)
  {
    var c = Cenario();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(2, new OrigemDaEntregaDto(posicao, setor, ordem), null, 1m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Mesmo_no_duas_vezes_na_lista_soma_contra_o_saldo()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 5m),
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 5m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 3", r.Detalhe);
    Assert.Single(c.Execucao.Movimentacoes);   // so o arranjo
  }

  [Fact]
  public async Task Lista_e_tudo_ou_nada()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m),
        new ItemDaEntregaDto(3, Coleta(Corte, 1), Dobra, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito);
    Assert.Equal(2, c.Execucao.Movimentacoes.Count);   // so os dois do arranjo
    Assert.Equal(0, c.Execucao.Saves);
  }

  [Fact]
  public async Task Nao_ha_teto_de_sobra_na_entrega()
  {
    // O pai ja tem 8 de 10 montadas: precisa de 2 do Item 3, e o Movimentador leva os 10 assim mesmo.
    var c = Cenario();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 8m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Trava_todos_os_nos_da_lista_de_uma_vez()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m),
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.Equal(new[] { 2, 3 }, Assert.Single(c.Execucao.Travas).Order().ToArray());
  }

  [Fact]
  public async Task No_inexistente_na_lista_da_404_e_Pedido_fechado_da_PedidoFechado()
  {
    var c = Cenario();
    AfirmarFalha(await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(99, Coleta(Corte, 1), null, 1m)), Movimentador, Ct),
        "NaoEncontrado", TipoDeErro.NaoEncontrado);

    c.Execucao.StatusDoPedido[PedidoId] = "Cancelado";
    AfirmarFalha(await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 1m)), Movimentador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EntregaUseCaseTests"`
Expected: FAIL na compilação — `EntregaUseCase` e os DTOs não existem.

- [ ] **Step 3: DTOs e caso de uso**

Acrescente a `ExecucaoDtos.cs`:

```csharp
/// <summary>
/// `Posicao`: `AguardandoColeta` (com `SetorId` e `Ordem`) ou `AguardandoMontagem` (com `SetorId`, sem
/// `Ordem`). Tudo anulavel de proposito: um corpo incompleto vira 400 `OrigemInvalida` com frase, e nao
/// um erro de binding sem codigo.
/// </summary>
public sealed record OrigemDaEntregaDto(string? Posicao, int? SetorId, int? Ordem);

public sealed record ItemDaEntregaDto(int EstruturaItemId, OrigemDaEntregaDto? Origem, int? DestinoSetorId, decimal Quantidade);

public sealed record EntregaDto(IReadOnlyList<ItemDaEntregaDto>? Itens);
```

Criar `src/Rastreamento.Application/Execucao/EntregaUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O Movimentador entrega uma LISTA, numa transacao so: ou tudo grava, ou nada (spec da Fase 3, secao
/// 4.3). A lista ja serve a 3B, cujo conjunto completo exige que os filhos entrem juntos. Um mesmo no
/// pode aparecer mais de uma vez: cada item desconta do saldo que os anteriores ja consumiram.
/// </summary>
public sealed class EntregaUseCase
{
  private sealed record Recusa(string Codigo, TipoDeErro Tipo, string Mensagem);

  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public EntregaUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _catalogo = catalogo;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> Entregar(EntregaDto dto, int usuarioId, CancellationToken ct)
  {
    var itens = dto.Itens ?? Array.Empty<ItemDaEntregaDto>();
    if (itens.Count == 0)
      return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EntregaVazia,
          "Escolha pelo menos um item para entregar.");

    var origens = new List<Local>();
    foreach (var item in itens)
    {
      if (!Quantidades.CabeNaColuna(item.Quantidade))
        return Falhas.QuantidadeInvalida<IReadOnlyList<MovimentacaoDto>>(item.Quantidade);
      if (OrigemValida(item.Origem) is not Local origem)
        return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.OrigemInvalida,
            "A origem de uma entrega é o que aguarda coleta (com Setor e passo) ou o que aguarda montagem "
            + "(com Setor, sem passo).");
      origens.Add(origem);
    }

    return await _execucao.ExecutarAsync(async () =>
    {
      var ids = itens.Select(i => i.EstruturaItemId).Distinct().ToList();
      var travados = await _execucao.TravarNosAsync(ids, ct);
      if (travados.Count != ids.Count) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
      foreach (var id in ids)
        if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(id, ct)))
          return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();

      // Os pais entram no estado (Roteiro, para o destino de montagem), mas nao sao travados: a entrega
      // so le o Roteiro deles, nao escreve nada neles.
      var paiIds = travados.Where(n => n.EstruturaPaiId is not null).Select(n => n.EstruturaPaiId!.Value)
          .Except(ids).Distinct().ToList();
      var pais = await _execucao.ListarNosAsync(paiIds, ct);
      var estado = await _leitor.CarregarAsync([.. travados, .. pais], ct);

      var setorIds = origens.Select(o => o.SetorId!.Value)
          .Concat(itens.Where(i => i.DestinoSetorId is not null).Select(i => i.DestinoSetorId!.Value))
          .Distinct().ToList();
      var nomes = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);
      string NomeDoSetor(int id) => nomes.TryGetValue(id, out var nome) ? nome : $"Setor {id}";

      var consumido = new Dictionary<(int Item, Local Local), decimal>();
      var movimentos = new List<Movimentacao>();
      for (var i = 0; i < itens.Count; i++)
      {
        var item = itens[i];
        var origem = origens[i];
        var chave = (item.EstruturaItemId, origem);
        var disponivel = estado.Calc.Saldo(item.EstruturaItemId, origem) - consumido.GetValueOrDefault(chave);
        if (item.Quantidade > disponivel)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.SaldoInsuficiente,
              $"Só há {Quantidades.Formatar(Math.Max(0m, disponivel))} de {estado.Nome(item.EstruturaItemId)} "
              + $"{Onde(origem, NomeDoSetor)}.");

        var (destino, recusa) = Destino(estado, item, origem, NomeDoSetor);
        if (recusa is not null)
          return Result<IReadOnlyList<MovimentacaoDto>>.Falha(recusa.Codigo, recusa.Tipo, recusa.Mensagem);

        consumido[chave] = consumido.GetValueOrDefault(chave) + item.Quantidade;
        movimentos.Add(NovoMovimento.De(
            item.EstruturaItemId, TiposDeMovimentacao.Entrega, item.Quantidade, origem, destino!.Value, usuarioId));
      }

      foreach (var movimento in movimentos) _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(movimentos, ct));
    }, ct);
  }

  private static Local? OrigemValida(OrigemDaEntregaDto? origem) => origem switch
  {
    { Posicao: Posicoes.AguardandoColeta, SetorId: int s, Ordem: int k } => Local.AguardandoColeta(s, k),
    { Posicao: Posicoes.AguardandoMontagem, SetorId: int s, Ordem: null } => Local.AguardandoMontagem(s),
    _ => null,
  };

  private static string Onde(Local origem, Func<int, string> nomeDoSetor) =>
      origem.Posicao == Posicoes.AguardandoColeta
          ? $"aguardando coleta no {nomeDoSetor(origem.SetorId!.Value)} (passo {origem.Ordem})"
          : $"aguardando montagem no {nomeDoSetor(origem.SetorId!.Value)}";

  /// <summary>A tabela da spec secao 4.3, linha a linha.</summary>
  private static (Local? Destino, Recusa? Recusa) Destino(
      EstadoDeExecucao estado, ItemDaEntregaDto item, Local origem, Func<int, string> nomeDoSetor)
  {
    var id = item.EstruturaItemId;
    var nome = estado.Nome(id);

    if (origem.Posicao == Posicoes.AguardandoColeta)
    {
      var calculado = estado.Calc.DestinoDaColeta(id, origem.Ordem!.Value);
      switch (calculado.Tipo)
      {
        case TipoDeDestino.ProximoPasso:
          if (item.DestinoSetorId is not null)
            return (null, Indevido($"{nome} vai para o próximo passo do Roteiro; esse destino não se escolhe."));
          return (Local.NoSetor(calculado.Passo!.Value.SetorId, calculado.Passo.Value.Ordem), null);
        case TipoDeDestino.Expedicao:
          if (item.DestinoSetorId is not null)
            return (null, Indevido($"{nome} terminou o Roteiro e vai para o local de expedição; esse destino não se escolhe."));
          return (Local.NaExpedicao, null);
        default:
          return ParaAMontagem(estado, item, calculado.PaiId!.Value, nomeDoSetor);
      }
    }

    // Redirecionamento: o que ja aguarda montagem vai aguardar em outro Setor do Roteiro do pai.
    if (item.DestinoSetorId is int mesmo && mesmo == origem.SetorId)
      return (null, Indevido($"{nome} já aguarda montagem no {nomeDoSetor(mesmo)}."));
    if (estado.Calc.No(id).PaiId is not int paiId)
      return (null, new Recusa(CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao,
          $"{nome} é uma Peça: não aguarda montagem de ninguém."));
    return ParaAMontagem(estado, item, paiId, nomeDoSetor);
  }

  private static (Local? Destino, Recusa? Recusa) ParaAMontagem(
      EstadoDeExecucao estado, ItemDaEntregaDto item, int paiId, Func<int, string> nomeDoSetor)
  {
    if (item.DestinoSetorId is not int destinoSetorId)
      return (null, Indevido(
          $"Escolha em que Setor {estado.Nome(item.EstruturaItemId)} vai aguardar a montagem de {estado.Nome(paiId)}."));

    var possiveis = estado.Calc.SetoresDoRoteiro(paiId);
    if (possiveis.Count == 0)
      return (null, new Recusa(CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito,
          $"{estado.Nome(paiId)} não tem Roteiro: o PCP precisa defini-lo antes de receber os filhos."));
    if (!possiveis.Contains(destinoSetorId))
      return (null, new Recusa(CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito,
          $"O {nomeDoSetor(destinoSetorId)} não está no Roteiro de {estado.Nome(paiId)}."));

    return (Local.AguardandoMontagem(destinoSetorId), null);
  }

  private static Recusa Indevido(string mensagem) =>
      new(CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao, mensagem);
}
```

Em `Program.cs`: `builder.Services.AddScoped<EntregaUseCase>();`. Em `RegistroDeDependenciasTests`:
`[InlineData(typeof(EntregaUseCase))]`.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EntregaUseCaseTests"`
Expected: PASS (19 testes, contando os casos da `[Theory]`).

Mutação de conferência, a registrar no relatório: apague a linha
`consumido[chave] = consumido.GetValueOrDefault(chave) + item.Quantidade;` —
`Mesmo_no_duas_vezes_na_lista_soma_contra_o_saldo` tem de falhar. Desfaça.

- [ ] **Step 5: Suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS.

```bash
git add src/Rastreamento.Application/Execucao src/Rastreamento.Api/Program.cs \
  tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs tests/Rastreamento.Application.Tests/Execucao
git commit -m "feat(execucao): entrega em lista, tudo ou nada, com destino calculado ou escolhido"
```

### Task 7: Estornar — `EstornoUseCase` — e a conservação como propriedade

Spec §4.5 (movimento e montagem, autor ou PCP/Administrador) e §9.1 (a regra 9 afirmada por uma
sequência aleatória de operações válidas, com semente fixa, em vez de exemplos escolhidos à mão).

**Files:**
- Modify: `src/Rastreamento.Application/Common/Result.cs` (`TipoDeErro.Proibido`)
- Modify: `tests/Rastreamento.Application.Tests/Common/ResultTests.cs`
- Modify: `src/Rastreamento.Application/Execucao/Falhas.cs` (`Proibido<T>()`)
- Create: `src/Rastreamento.Application/Execucao/EstornoUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs` (`Estorno()`)
- Create: `tests/Rastreamento.Application.Tests/Execucao/EstornoUseCaseTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/ConservacaoTests.cs`

**Interfaces:**
- Consumes: Tasks 3 a 6.
- Produces: `TipoDeErro.Proibido` (novo, **no fim** do enum); `Falhas.Proibido<T>()`;
  `EstornoUseCase(IExecucaoRepository, IEstruturaRepository, IReceitaPadraoRepository)` com
  `EstornarMovimentacao(int movimentacaoId, int usuarioId, bool podeEstornarDeOutros, ct) : Task<Result<MovimentacaoDto>>`
  e `EstornarMontagem(int montagemId, int usuarioId, bool podeEstornarDeOutros, ct) : Task<Result<IReadOnlyList<MovimentacaoDto>>>`.
  `podeEstornarDeOutros` é "o usuário é PCP ou Administrador" — o controller decide pelo perfil; a
  Application não conhece o ASP.NET. `CenarioDeExecucao.Estorno()`.

- [ ] **Step 1: Escrever os testes que falham**

Em `ResultTests.Falha_preserva_o_tipo_informado`, acrescente `[InlineData(TipoDeErro.Proibido)]`.

Em `CenarioDeExecucao`, depois de `Entrega()`:

```csharp
  public EstornoUseCase Estorno() => new(Execucao, Estruturas, Catalogo);

  /// <summary>
  /// A calculadora sobre o estado inteiro do cenario, lida direto dos fakes — para os testes afirmarem
  /// o saldo depois de uma operacao sem passar pelo caso de uso que acabaram de exercitar.
  /// </summary>
  public CalculadoraDeExecucao Calcular() =>
      new(Estruturas.Itens.Select(i => new NoDoCalculo(i.Id, i.EstruturaPaiId, i.Quantidade, i.QuantidadePorPai,
              Estruturas.Roteiros.Where(r => r.EstruturaItemId == i.Id).OrderBy(r => r.Ordem)
                  .Select(r => new PassoDoCalculo(r.SetorId, r.Ordem)).ToList())),
          Livro.SomarSaldos(Execucao.Movimentacoes),
          Execucao.Montagens.Where(g => g.EstornadaEm is null).GroupBy(g => g.EstruturaItemId)
              .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantidade)),
          Execucao.Movimentacoes
              .SelectMany(m => new[] { (m.EstruturaItemId, m.OrigemOrdem), (m.EstruturaItemId, m.DestinoOrdem) })
              .Where(p => p.Item2 is not null)
              .Select(p => (p.Item1, p.Item2!.Value))
              .Distinct()
              .ToList());
```

Criar `tests/Rastreamento.Application.Tests/Execucao/EstornoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Estornar (spec da Fase 3, secao 4.5): o inverso que aponta o original, uma vez so, so enquanto a quantidade nao andou.</summary>
public class EstornoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static async Task<(CenarioDeExecucao C, int Inicio)> IniciadoAsync(decimal quantidade = 5m)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var inicio = await c.Apontamento().Iniciar(1, new InicioDto(Corte, quantidade), Operador, Ct);
    return (c, inicio.Valor!.Id);
  }

  [Fact]
  public async Task Estornar_um_Inicio_devolve_a_iniciar_e_o_Pedido_continua_em_producao()
  {
    var (c, inicio) = await IniciadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, podeEstornarDeOutros: false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Estorno, r.Valor!.Tipo);
    Assert.Equal(inicio, r.Valor.EstornoDeId);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor.Origem);
    Assert.Equal(new LocalDto(Posicoes.AIniciar, null, null, null), r.Valor.Destino);
    Assert.Equal(10m, c.Calcular().Saldo(1, Local.AIniciar));
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);   // regra 28: nao volta
  }

  [Fact]
  public async Task Estorno_alheio_sem_ser_PCP_da_Proibido()
  {
    var (c, inicio) = await IniciadoAsync();
    var transacoesAntes = c.Execucao.Transacoes;

    var r = await c.Estorno().EstornarMovimentacao(inicio, Movimentador, podeEstornarDeOutros: false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
    Assert.Equal(transacoesAntes, c.Execucao.Transacoes);   // o 403 sai antes de abrir transacao
  }

  [Fact]
  public async Task PCP_estorna_registro_alheio()
  {
    var (c, inicio) = await IniciadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(inicio, Pcp, podeEstornarDeOutros: true, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(Pcp, r.Valor!.UsuarioId);
  }

  [Fact]
  public async Task Estornar_depois_de_a_quantidade_andar_da_EstornoImpossivel()
  {
    var (c, inicio) = await IniciadoAsync();
    await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 5m), Operador, Ct);

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains("já andou", r.Detalhe);
  }

  [Fact]
  public async Task Estornar_um_estorno_da_EstornoImpossivel()
  {
    var (c, inicio) = await IniciadoAsync();
    var estorno = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    var r = await c.Estorno().EstornarMovimentacao(estorno.Valor!.Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Estornar_duas_vezes_da_JaEstornado()
  {
    var (c, inicio) = await IniciadoAsync();
    await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Estorno_em_Pedido_fechado_da_PedidoFechado()
  {
    var (c, inicio) = await IniciadoAsync();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Registro_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Estorno().EstornarMovimentacao(999, Operador, true, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Estorno().EstornarMontagem(999, Operador, true, Ct)).TipoDoErro);
  }

  // ------------------------------------------------------------------ montagem

  private static async Task<(CenarioDeExecucao C, MontagemDto Montagem)> MontadoAsync()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 20m, 2m, Corte);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    var montagem = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);
    return (c, montagem.Valor!);
  }

  [Fact]
  public async Task Estornar_montagem_estorna_cada_baixa_e_marca_a_montagem()
  {
    var (c, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(montagem.Baixas.Select(b => b.Id).ToArray(), r.Valor!.Select(e => e.EstornoDeId!.Value).ToArray());
    Assert.All(r.Valor, e =>
    {
      Assert.Equal(TiposDeMovimentacao.Estorno, e.Tipo);
      Assert.Equal(montagem.Id, e.MontagemId);
      Assert.Equal(Posicoes.Montado, e.Origem.Posicao);
      Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), e.Destino);
    });
    var gravada = c.Execucao.Montagens.Single(g => g.Id == montagem.Id);
    Assert.NotNull(gravada.EstornadaEm);
    Assert.Equal(Operador, gravada.EstornadaPorUsuarioId);
    var estado = c.Calcular();
    Assert.Equal(0m, estado.TotalMontado(1));
    Assert.Equal(6m, estado.AguardandoMontagem(2, Solda));
    Assert.Equal(3m, estado.AguardandoMontagem(3, Solda));
  }

  [Fact]
  public async Task Estornar_baixa_de_montagem_avulsa_da_EstornoImpossivel_apontando_a_montagem()
  {
    var (c, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(montagem.Baixas[0].Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains($"montagem {montagem.Id}", r.Detalhe);
  }

  [Fact]
  public async Task Montagem_estornada_duas_vezes_da_JaEstornado()
  {
    var (c, montagem) = await MontadoAsync();
    await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    AfirmarFalha(await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct),
        CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Montagem_alheia_sem_ser_PCP_da_Proibido()
  {
    var (c, montagem) = await MontadoAsync();

    AfirmarFalha(await c.Estorno().EstornarMontagem(montagem.Id, Movimentador, false, Ct),
        CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
```

Criar `tests/Rastreamento.Application.Tests/Execucao/ConservacaoTests.cs`:

```csharp
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A regra 9 como PROPRIEDADE (spec da Fase 3, secao 9.1): uma sequencia aleatoria de iniciar, terminar,
/// entregar, montar e estornar, com semente FIXA, sobre uma arvore de tres niveis. Depois de CADA passo:
/// nenhuma posicao negativa, a soma das posicoes de cada no igual a quantidade dele, o montado de cada
/// filho igual as montagens validas do pai vezes a razao, e uma operacao recusada nao deixa rastro.
/// Os sorteios escolhem sobretudo operacoes plausiveis a partir do estado — e tambem quantidades uma
/// unidade acima do saldo, para o caminho de recusa correr junto.
/// </summary>
public class ConservacaoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;
  private const int Passos = 600;

  [Fact]
  public async Task Nenhuma_sequencia_de_operacoes_deixa_posicao_negativa_nem_muda_o_total()
  {
    var c = new CenarioDeExecucao();
    // A (Peca, 6) <- B (razao 2) <- D (razao 3); A <- C (razao 1)
    c.No(1, null, 6m, null, Solda, Pintura);
    c.No(2, 1, 12m, 2m, Corte, Solda);
    c.No(3, 1, 6m, 1m, Dobra);
    c.No(4, 2, 36m, 3m, Corte);
    int[] nos = [1, 2, 3, 4];
    var apontamento = c.Apontamento();
    var entrega = c.Entrega();
    var estorno = c.Estorno();
    var sorteio = new Random(20260925);
    var sucessos = new Dictionary<string, int>
    {
      ["iniciar"] = 0, ["terminar"] = 0, ["entregar"] = 0, ["montar"] = 0, ["estornar"] = 0, ["estornar montagem"] = 0,
    };

    for (var passo = 0; passo < Passos; passo++)
    {
      var calc = c.Calcular();
      var movimentosAntes = c.Execucao.Movimentacoes.Count;
      var montagensAntes = c.Execucao.Montagens.Count(g => g.EstornadaEm is null);
      string operacao;
      bool ok;

      switch (sorteio.Next(10))
      {
        case 0 or 1:
        {
          var id = nos[sorteio.Next(nos.Length)];
          var primeiro = calc.PrimeiroPasso(id)!.Value;
          operacao = "iniciar";
          ok = (await apontamento.Iniciar(id, new InicioDto(primeiro.SetorId, Quantidade(sorteio, calc.Saldo(id, Local.AIniciar))), Operador, Ct)).Sucesso;
          break;
        }
        case 2 or 3:
        {
          var baldes = Baldes(calc, nos, Posicoes.NoSetor);
          if (baldes.Count == 0) continue;
          var (id, local, saldo) = baldes[sorteio.Next(baldes.Count)];
          operacao = "terminar";
          ok = (await apontamento.Terminar(id, new TerminoDto(local.SetorId!.Value, local.Ordem!.Value, Quantidade(sorteio, saldo)), Operador, Ct)).Sucesso;
          break;
        }
        case 4 or 5 or 6:
        {
          var baldes = Baldes(calc, nos, Posicoes.AguardandoColeta).Concat(Baldes(calc, nos, Posicoes.AguardandoMontagem)).ToList();
          if (baldes.Count == 0) continue;
          var (id, local, saldo) = baldes[sorteio.Next(baldes.Count)];
          int? destino = null;
          if (calc.No(id).PaiId is int pai
              && (local.Posicao == Posicoes.AguardandoMontagem || calc.ProximoPasso(id, local.Ordem!.Value) is null))
          {
            var possiveis = calc.SetoresDoRoteiro(pai);
            destino = possiveis[sorteio.Next(possiveis.Count)];
          }
          operacao = "entregar";
          ok = (await entrega.Entregar(new EntregaDto(
              [new ItemDaEntregaDto(id, new OrigemDaEntregaDto(local.Posicao, local.SetorId, local.Ordem), destino, Quantidade(sorteio, saldo))]),
              Movimentador, Ct)).Sucesso;
          break;
        }
        case 7:
        {
          var pai = sorteio.Next(2) == 0 ? 1 : 2;
          var setores = calc.Filhos(pai).SelectMany(f => calc.SetoresOndeAguardaMontagem(f.Id)).Distinct().ToList();
          var setor = setores.Count > 0 ? setores[sorteio.Next(setores.Count)] : Solda;
          operacao = "montar";
          ok = (await apontamento.Montar(pai, new MontagemNovaDto(setor, 1m), Operador, Ct)).Sucesso;
          break;
        }
        case 8:
        {
          if (c.Execucao.Movimentacoes.Count == 0) continue;
          var movimento = c.Execucao.Movimentacoes[sorteio.Next(c.Execucao.Movimentacoes.Count)];
          operacao = "estornar";
          ok = (await estorno.EstornarMovimentacao(movimento.Id, Pcp, podeEstornarDeOutros: true, Ct)).Sucesso;
          break;
        }
        default:
        {
          if (c.Execucao.Montagens.Count == 0) continue;
          var montagem = c.Execucao.Montagens[sorteio.Next(c.Execucao.Montagens.Count)];
          operacao = "estornar montagem";
          ok = (await estorno.EstornarMontagem(montagem.Id, Pcp, podeEstornarDeOutros: true, Ct)).Sucesso;
          break;
        }
      }

      if (ok) sucessos[operacao]++;
      else
      {
        Assert.True(c.Execucao.Movimentacoes.Count == movimentosAntes, $"passo {passo}: {operacao} recusado gravou movimento");
        Assert.True(c.Execucao.Montagens.Count(g => g.EstornadaEm is null) == montagensAntes,
            $"passo {passo}: {operacao} recusado mexeu em montagem");
      }
      AfirmarRegra9(c, nos, passo, operacao);
    }

    Assert.All(sucessos, kv => Assert.True(kv.Value > 0, $"a semente nunca exercitou '{kv.Key}' com sucesso"));
  }

  private static void AfirmarRegra9(CenarioDeExecucao c, int[] nos, int passo, string operacao)
  {
    var calc = c.Calcular();
    foreach (var id in nos)
    {
      var saldos = calc.Saldos(id);
      Assert.All(saldos, s => Assert.True(s.Quantidade >= 0m,
          $"passo {passo} ({operacao}): no {id} ficou com {s.Quantidade} em {s.Local}"));
      Assert.Equal(calc.No(id).Quantidade, saldos.Sum(s => s.Quantidade));
      Assert.True(calc.TotalMontado(id) <= calc.No(id).Quantidade, $"passo {passo}: no {id} montado alem da quantidade");

      if (calc.No(id).PaiId is int pai)
      {
        var esperado = calc.TotalMontado(pai) * calc.No(id).QuantidadePorPai!.Value;
        Assert.Equal(esperado, calc.Saldo(id, Local.Montado));
      }
    }
  }

  private static decimal Quantidade(Random sorteio, decimal saldo)
  {
    // De 1 ate uma unidade ACIMA do saldo: a ultima opcao exercita a recusa.
    var teto = (int)Math.Floor(saldo) + 1;
    return teto <= 1 ? 1m : sorteio.Next(1, teto + 1);
  }

  private static List<(int Id, Local Local, decimal Saldo)> Baldes(CalculadoraDeExecucao calc, int[] nos, string posicao) =>
      nos.SelectMany(id => calc.Saldos(id)
              .Where(s => s.Local.Posicao == posicao && s.Quantidade > 0m)
              .Select(s => (Id: id, Local: s.Local, Saldo: s.Quantidade)))
          .ToList();

}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EstornoUseCaseTests|FullyQualifiedName~ConservacaoTests|FullyQualifiedName~ResultTests"`
Expected: FAIL na compilação — `TipoDeErro.Proibido` e `EstornoUseCase` não existem.

- [ ] **Step 3: `TipoDeErro.Proibido`**

Em `Result.cs`, no fim do enum `TipoDeErro`, depois de `NaoAutorizado`:

```csharp
  /// <summary>
  /// Autenticado, com o perfil certo, mas a regra do caso de uso recusa QUEM pede — 403. Nasceu na
  /// Fase 3 para o estorno de registro alheio (spec secao 4.5): o `[Authorize(Roles)]` deixa os perfis
  /// passarem, e so o caso de uso sabe quem e o autor.
  /// </summary>
  Proibido,
```

Em `Falhas.cs`:

```csharp
  public static Result<T> Proibido<T>() =>
      Result<T>.Falha(CodigosDaExecucao.Proibido, TipoDeErro.Proibido,
          "Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.");
```

- [ ] **Step 4: O caso de uso**

Criar `src/Rastreamento.Application/Execucao/EstornoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Correcao do livro, que e so de inclusao: o estorno grava o movimento inverso, apontando o original
/// (spec da Fase 3, secao 4.5). So enquanto a quantidade nao andou — o destino original ainda tem de
/// comportar o que volta. Uma vez so por registro (`UX_Movimentacao_EstornoDe` garante no banco).
/// Estorno nao se estorna; a baixa de filho de uma montagem so sai com a montagem inteira.
/// </summary>
public sealed class EstornoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public EstornoUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public async Task<Result<MovimentacaoDto>> EstornarMovimentacao(
      int movimentacaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var original = await _execucao.ObterMovimentacaoAsync(movimentacaoId, ct);
    if (original is null) return Falhas.NaoEncontrado<MovimentacaoDto>();
    if (original.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<MovimentacaoDto>();
    if (original.Tipo == TiposDeMovimentacao.Estorno)
      return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
          "Estorno não se estorna: registre de novo a operação original.");
    if (original.MontagemId is int montagemId)
      return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
          $"Esta baixa faz parte da montagem {montagemId}: estorne a montagem inteira.");

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([original.EstruturaItemId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(original.EstruturaItemId, ct)))
        return Falhas.PedidoFechado<MovimentacaoDto>();
      // Relido DEPOIS da trava: dois estornos simultaneos do mesmo registro travam o mesmo no, e o
      // segundo ve o primeiro aqui.
      if ((await _execucao.ListarEstornadasAsync([original.Id], ct)).Count > 0)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.JaEstornado, "Este registro já foi estornado.");

      var estado = await _leitor.CarregarAsync(nos, ct);
      var destino = Local.DoDestino(original);
      var ali = estado.Calc.Saldo(original.EstruturaItemId, destino);
      if (original.Quantidade > ali)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.EstornoImpossivel,
            $"Só há {Quantidades.Formatar(ali)} de {estado.Nome(original.EstruturaItemId)} onde este registro pôs "
            + $"{Quantidades.Formatar(original.Quantidade)}: a quantidade já andou.");

      var estorno = NovoMovimento.De(original.EstruturaItemId, TiposDeMovimentacao.Estorno, original.Quantidade,
          destino, Local.DaOrigem(original), usuarioId, estornoDeId: original.Id);
      _execucao.Adicionar(estorno);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([estorno], ct)).Single());
    }, ct);
  }

  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> EstornarMontagem(
      int montagemId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var montagem = await _execucao.ObterMontagemAsync(montagemId, ct);
    if (montagem is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();
    if (montagem.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var baixas = await _execucao.ListarBaixasAsync([montagemId], ct);
      var nos = await _execucao.TravarNosAsync(baixas.Select(b => b.EstruturaItemId).Append(montagem.EstruturaItemId), ct);
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(montagem.EstruturaItemId, ct)))
        return Falhas.PedidoFechado<IReadOnlyList<MovimentacaoDto>>();
      var atual = await _execucao.ObterMontagemAsync(montagemId, ct);   // relida depois da trava
      if (atual!.EstornadaEm is not null)
        return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.JaEstornado, "Esta montagem já foi estornada.");

      var estado = await _leitor.CarregarAsync(nos, ct);
      foreach (var baixa in baixas)
        if (estado.Calc.Saldo(baixa.EstruturaItemId, Local.Montado) < baixa.Quantidade)
          return Falhas.Conflito<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.EstornoImpossivel,
              $"{estado.Nome(baixa.EstruturaItemId)} não tem mais o que esta montagem baixou.");

      var estornos = baixas.Select(b => NovoMovimento.De(b.EstruturaItemId, TiposDeMovimentacao.Estorno, b.Quantidade,
          Local.Montado, Local.DaOrigem(b), usuarioId, montagemId: montagemId, estornoDeId: b.Id)).ToList();
      foreach (var estorno in estornos) _execucao.Adicionar(estorno);
      await _execucao.MarcarMontagemEstornadaAsync(montagemId, usuarioId, DateTime.UtcNow, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(estornos, ct));
    }, ct);
  }
}
```

Em `Program.cs`: `builder.Services.AddScoped<EstornoUseCase>();`. Em `RegistroDeDependenciasTests`:
`[InlineData(typeof(EstornoUseCase))]`.

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~EstornoUseCaseTests|FullyQualifiedName~ConservacaoTests|FullyQualifiedName~ResultTests"`
Expected: PASS (12 de estorno, 1 de conservação, os de `ResultTests` com um caso a mais).

Se `Nenhuma_sequencia_...` falhar só na última asserção (alguma operação nunca teve sucesso com a semente
`20260925`), suba `Passos` para 2000 e rode de novo; se ainda assim faltar, **não troque a semente até
passar** — escreva no relatório qual operação nunca teve sucesso e por quê, porque é sinal de que o
sorteio não alcança aquele caminho, e isso é achado, não ajuste.

Mutações de conferência, a registrar no relatório: em `EstornoUseCase.EstornarMovimentacao`, troque
`if (original.Quantidade > ali)` por `if (false)` — `Estornar_depois_de_a_quantidade_andar_da_EstornoImpossivel`
**e** `Nenhuma_sequencia_...` (posição negativa) têm de falhar. Em `ApontamentoUseCase.Montar`, troque
`dto.Quantidade * (filho.QuantidadePorPai ?? 0m)` por `dto.Quantidade` — `Nenhuma_sequencia_...` tem de
falhar na asserção do montado. Desfaça as duas.

- [ ] **Step 6: Suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS.

```bash
git add src/Rastreamento.Application src/Rastreamento.Api/Program.cs \
  tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs tests/Rastreamento.Application.Tests
git commit -m "feat(execucao): estorno de movimento e de montagem, e a regra 9 como propriedade"
```

### Task 8: Roteiro do nó e as edições da Fase 2 no esquema de trava

Spec §4.6 (editar o Roteiro, com os passos alcançados travados), §4.7 (reduzir a quantidade) e §8.1
(edição de quantidade, de Roteiro e o `DELETE` entram na transação com trava — desvio D5 para o
`DELETE`).

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (DTOs do Roteiro)
- Create: `src/Rastreamento.Application/Execucao/RoteiroDoNoUseCase.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` (construtor, `EditarNo`, `ExcluirNo`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs` (`RemoverSubarvoreAsync`
  reusa a transação aberta; dois comentários)
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs`, `EditarEExcluirNoTests.cs`,
  `ObterArvoreTests.cs`, `QuantidadePorPaiTests.cs` (construtor)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs` (`Roteiro()`, `Estrutura()`)
- Create: `tests/Rastreamento.Application.Tests/Execucao/RoteiroDoNoUseCaseTests.cs`
- Create: `tests/Rastreamento.Application.Tests/Execucao/EdicoesNaExecucaoTests.cs`

**Interfaces:**
- Consumes: Tasks 3 a 7.
- Produces: `RoteiroNovoDto(IReadOnlyList<int>? Passos)`,
  `PassoDoRoteiroDoNoDto(int SetorId, string Nome, int Ordem, bool Alcancado)`,
  `RoteiroDoNoDto(int EstruturaItemId, IReadOnlyList<PassoDoRoteiroDoNoDto> Passos)`;
  `RoteiroDoNoUseCase(IExecucaoRepository, IEstruturaRepository, IReceitaPadraoRepository)` com
  `Obter(int noId, ct) : Task<Result<RoteiroDoNoDto>>` e
  `Substituir(int noId, RoteiroNovoDto, ct) : Task<Result<RoteiroDoNoDto>>`;
  `MontagemDeEstruturaUseCase(IEstruturaRepository, IAgrupamentoRepository, IReceitaPadraoRepository, IPedidoRepository, IExecucaoRepository)`
  — o quinto parâmetro é novo; `EditarNo` pode devolver 409 `QuantidadeAbaixoDoMovimentado` e 409
  `ConflitoDeConcorrencia`, ambos com `Detalhe`.

- [ ] **Step 1: O construtor novo nos testes existentes**

`MontagemDeEstruturaUseCase` passa a receber `IExecucaoRepository` como quinto parâmetro. Nos testes, onde
o primeiro argumento é a variável `estruturas`, o fake do livro compartilha **aquela** árvore (é o que
`EditarNo` e `ExcluirNo` travam); nos demais, recebe uma árvore própria, que não é usada. Mecânico:

```bash
python3 - <<'EOF'
import re
arquivos = ['tests/Rastreamento.Application.Tests/Estrutura/CriarPecaTests.cs',
            'tests/Rastreamento.Application.Tests/Estrutura/EditarEExcluirNoTests.cs',
            'tests/Rastreamento.Application.Tests/Estrutura/ObterArvoreTests.cs',
            'tests/Rastreamento.Application.Tests/Estrutura/QuantidadePorPaiTests.cs']
def trocar(m):
    args = m.group(1)
    arvore = 'estruturas' if args.strip().startswith('estruturas') else 'new FakeEstruturaRepo()'
    return f'new MontagemDeEstruturaUseCase({args}, new FakeExecucaoRepo({arvore}));'
for p in arquivos:
    s = open(p, encoding='utf-8').read()
    novo, n = re.subn(r'new MontagemDeEstruturaUseCase\(([^;]*?)\);', trocar, s)
    if 'using Rastreamento.Application.Tests.Execucao;' not in novo:
        novo = novo.replace('using Xunit;', 'using Rastreamento.Application.Tests.Execucao;\nusing Xunit;', 1)
    open(p, 'w', encoding='utf-8').write(novo)
    print(p, n)
EOF
```

Expected: 2, 1, 5 e 1 trocas. Confira com `git diff --stat` que só esses quatro arquivos mudaram.

Em `CenarioDeExecucao`, depois de `Estorno()`:

```csharp
  public RoteiroDoNoUseCase Roteiro() => new(Execucao, Estruturas, Catalogo);

  public MontagemDeEstruturaUseCase Estrutura() =>
      new(Estruturas, new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo, new FakePedidoRepo(new Pedido
          {
            Id = PedidoId, Numero = "PED-01", Cliente = "Cliente", Tipo = "Normal",
            Status = Execucao.StatusDoPedido[PedidoId], DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = Pcp,
          }),
          Execucao);
```

(com `using Rastreamento.Application.Estrutura;` no topo do cenário).

- [ ] **Step 2: Escrever os testes que falham**

Criar `tests/Rastreamento.Application.Tests/Execucao/RoteiroDoNoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Editar o Roteiro do no (spec da Fase 3, secao 4.6): o que ja foi alcancado e historico.</summary>
public class RoteiroDoNoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>No 1, Roteiro Corte -> Dobra -> Pintura, com os passos 1 e 2 ja alcancados.</summary>
  private static CenarioDeExecucao Andado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Pintura);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 5m);
    c.Mover(1, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NoSetor(Dobra, 2), 5m);
    return c;
  }

  private static (int SetorId, int Ordem, bool Alcancado)[] Passos(RoteiroDoNoDto roteiro) =>
      roteiro.Passos.Select(p => (p.SetorId, p.Ordem, p.Alcancado)).ToArray();

  [Fact]
  public async Task Obter_marca_os_passos_alcancados()
  {
    var c = Andado();

    var r = await c.Roteiro().Obter(1, Ct);

    Assert.Equal(new[] { (Corte, 1, true), (Dobra, 2, true), (Pintura, 3, false) }, Passos(r.Valor!));
    Assert.Equal("Corte", r.Valor!.Passos[0].Nome);
  }

  [Fact]
  public async Task Sem_nada_alcancado_o_Roteiro_inteiro_muda_e_renumera_do_um()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Solda, Corte, Solda]), Ct);

    Assert.Equal(new[] { (Solda, 1, false), (Corte, 2, false), (Solda, 3, false) }, Passos(r.Valor!));
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Passos_alcancados_ficam_e_os_novos_vem_depois_deles()
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Corte, Dobra, Solda, Pintura]), Ct);

    Assert.Equal(new[] { (Corte, 1, true), (Dobra, 2, true), (Solda, 3, false), (Pintura, 4, false) }, Passos(r.Valor!));
  }

  [Theory]
  [InlineData(new[] { Corte, Solda, Pintura })]            // muda o passo 2, alcancado
  [InlineData(new[] { Solda, Corte, Dobra, Pintura })]     // insere antes dos alcancados
  [InlineData(new[] { Corte })]                             // remove o passo 2, alcancado
  public async Task Mexer_em_passo_alcancado_da_PassoJaAlcancado(int[] passos)
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto(passos), Ct);

    AfirmarFalha(r, CodigosDaExecucao.PassoJaAlcancado, TipoDeErro.Conflito);
    Assert.Equal(3, c.Estruturas.Roteiros.Count(p => p.EstruturaItemId == 1));   // nada mudou
  }

  [Theory]
  [InlineData(Inativo)]
  [InlineData(77)]
  public async Task Setor_inativo_ou_inexistente_nao_entra_no_Roteiro(int setor)
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Corte, Dobra, setor]), Ct);

    AfirmarFalha(r, CodigosDaExecucao.RoteiroInvalido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Setor_inativado_no_trecho_alcancado_continua_no_Roteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Inativo, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Inativo, 1), 5m);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Inativo, Dobra]), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { (Inativo, 1, true), (Dobra, 2, false) }, Passos(r.Valor!));
  }

  [Fact]
  public async Task No_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Roteiro().Obter(9, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Roteiro().Substituir(9, new RoteiroNovoDto([Corte]), Ct)).TipoDoErro);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
```

Criar `tests/Rastreamento.Application.Tests/Execucao/EdicoesNaExecucaoTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// As edicoes da Fase 2 que a Fase 3 passa a validar contra o livro (spec secao 4.7) e a pôr no esquema
/// de trava (secao 8.1; o DELETE trava a subarvore inteira — desvio D5 do plano 2). Os nos do cenario
/// nao tem Componente, entao a edicao manda a descricao (regra 19), senao seria recusada por ela.
/// </summary>
public class EdicoesNaExecucaoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  [Fact]
  public async Task Reduzir_abaixo_do_que_saiu_de_a_iniciar_da_QuantidadeAbaixoDoMovimentado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 5m), Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.QuantidadeAbaixoDoMovimentado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("6", r.Detalhe);
    Assert.Equal(10m, c.Estruturas.Itens.Single(i => i.Id == 1).Quantidade);
  }

  [Fact]
  public async Task Reduzir_ate_o_que_ja_andou_e_permitido_e_trava_o_no()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 6m), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(6m, c.Estruturas.Itens.Single(i => i.Id == 1).Quantidade);
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Pai_nao_desce_abaixo_do_total_montado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 4m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 3m), Ct);

    Assert.Equal(CodigosDaExecucao.QuantidadeAbaixoDoMovimentado, r.Erro);
    Assert.Contains("4", r.Detalhe);
  }

  [Fact]
  public async Task Conflito_na_edicao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.ConflitoNaProximaTransacao = true;

    var r = await c.Estrutura().EditarNo(1, new EdicaoDeNoDto("No 1", 8m), Ct);

    Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  }

  [Fact]
  public async Task Excluir_trava_a_subarvore_inteira_antes_de_ler_o_status()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.No(2, 1, 10m, 1m, Corte);
    c.No(3, 2, 10m, 1m, Corte);

    var r = await c.Estrutura().ExcluirNo(1, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { 1, 2, 3 }, Assert.Single(c.Execucao.Travas).Order().ToArray());
    Assert.Empty(c.Estruturas.Itens);
  }

  [Fact]
  public async Task Excluir_em_Pedido_em_producao_continua_PedidoNaoAberto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";

    var r = await c.Estrutura().ExcluirNo(1, Ct);

    Assert.Equal("PedidoNaoAberto", r.Erro);
    Assert.Single(c.Estruturas.Itens);
  }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~RoteiroDoNoUseCaseTests|FullyQualifiedName~EdicoesNaExecucaoTests"`
Expected: FAIL na compilação — `RoteiroDoNoUseCase`, os DTOs e o construtor de cinco parâmetros não existem.

- [ ] **Step 4: DTOs e `RoteiroDoNoUseCase`**

Acrescente a `ExecucaoDtos.cs`:

```csharp
/// <summary>Os Setores do Roteiro, em ordem; repetir um Setor e voltar a ele (regra 21).</summary>
public sealed record RoteiroNovoDto(IReadOnlyList<int>? Passos);

public sealed record PassoDoRoteiroDoNoDto(int SetorId, string Nome, int Ordem, bool Alcancado);

public sealed record RoteiroDoNoDto(int EstruturaItemId, IReadOnlyList<PassoDoRoteiroDoNoDto> Passos);
```

Criar `src/Rastreamento.Application/Execucao/RoteiroDoNoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O Roteiro de UM no, editavel pelo PCP (spec da Fase 3, secao 4.6; regra 7). Um passo e ALCANCADO
/// quando sua `Ordem` aparece no livro do no, como origem ou destino — inclusive de um movimento
/// estornado: o livro aponta para ele, entao ele e historico. Tudo ate o ultimo passo alcancado fica
/// como esta; o resto se troca livremente, e os passos novos ganham `Ordem` depois do ultimo travado.
/// Setor inativo nao entra no trecho novo; o que ja esta no trecho travado continua (o que esta nele
/// continua andando). Nao ha `PedidoFechado` aqui: a spec o reserva para movimentar.
/// </summary>
public sealed class RoteiroDoNoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;

  public RoteiroDoNoUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
  }

  public async Task<Result<RoteiroDoNoDto>> Obter(int noId, CancellationToken ct)
  {
    if ((await _execucao.ListarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<RoteiroDoNoDto>();
    return Result<RoteiroDoNoDto>.Ok(await ProjetarAsync(noId, ct));
  }

  public async Task<Result<RoteiroDoNoDto>> Substituir(int noId, RoteiroNovoDto dto, CancellationToken ct)
  {
    var passos = dto.Passos ?? Array.Empty<int>();

    return await _execucao.ExecutarAsync(async () =>
    {
      if ((await _execucao.TravarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<RoteiroDoNoDto>();

      var atual = (await _estruturas.ListarRoteiroAsync([noId], ct)).OrderBy(r => r.Ordem).ToList();
      var alcancados = (await _execucao.ListarPassosAlcancadosAsync([noId], ct)).Select(p => p.Ordem).ToHashSet();
      var travados = atual.FindLastIndex(r => alcancados.Contains(r.Ordem)) + 1;

      if (travados > 0)
      {
        var nomes = await NomesAsync(atual.Take(travados).Select(r => r.SetorId), ct);
        for (var i = 0; i < travados; i++)
          if (i >= passos.Count || passos[i] != atual[i].SetorId)
            return Falhas.Conflito<RoteiroDoNoDto>(CodigosDaExecucao.PassoJaAlcancado,
                $"O passo {atual[i].Ordem} ({nomes.GetValueOrDefault(atual[i].SetorId, "?")}) já foi alcançado e não muda; "
                + "os passos novos vêm depois dele.");
      }

      var novos = passos.Skip(travados).ToList();
      var setores = (await _catalogo.ObterSetoresPorIdAsync(novos.Distinct().ToList(), ct)).ToDictionary(s => s.Id);
      foreach (var setorId in novos)
        if (!setores.TryGetValue(setorId, out var setor) || !setor.Ativo)
          return Falhas.Validacao<RoteiroDoNoDto>(CodigosDaExecucao.RoteiroInvalido,
              setor is null
                  ? $"O Setor {setorId} não existe."
                  : $"O {setor.Nome} está inativo e não entra num Roteiro.");

      int? ultimaTravada = travados == 0 ? null : atual[travados - 1].Ordem;
      var primeiraNova = (ultimaTravada ?? 0) + 1;
      await _execucao.SubstituirPassosNaoAlcancadosAsync(
          noId, ultimaTravada, novos.Select((setorId, i) => (setorId, primeiraNova + i)).ToList(), ct);

      return Result<RoteiroDoNoDto>.Ok(await ProjetarAsync(noId, ct));
    }, ct);
  }

  private async Task<RoteiroDoNoDto> ProjetarAsync(int noId, CancellationToken ct)
  {
    var passos = (await _estruturas.ListarRoteiroAsync([noId], ct)).OrderBy(r => r.Ordem).ToList();
    var alcancados = (await _execucao.ListarPassosAlcancadosAsync([noId], ct)).Select(p => p.Ordem).ToHashSet();
    var nomes = await NomesAsync(passos.Select(p => p.SetorId), ct);
    return new RoteiroDoNoDto(noId, passos
        .Select(p => new PassoDoRoteiroDoNoDto(p.SetorId, nomes.GetValueOrDefault(p.SetorId, string.Empty), p.Ordem,
            alcancados.Contains(p.Ordem)))
        .ToList());
  }

  private async Task<Dictionary<int, string>> NomesAsync(IEnumerable<int> setorIds, CancellationToken ct)
  {
    var ids = setorIds.Distinct().ToList();
    if (ids.Count == 0) return new Dictionary<int, string>();
    return (await _catalogo.ObterSetoresPorIdAsync(ids, ct)).ToDictionary(s => s.Id, s => s.Nome);
  }
}
```

Em `Program.cs`: `builder.Services.AddScoped<RoteiroDoNoUseCase>();`. Em `RegistroDeDependenciasTests`:
`[InlineData(typeof(RoteiroDoNoUseCase))]`.

- [ ] **Step 5: `EditarNo` e `ExcluirNo` no esquema de trava**

Em `MontagemDeEstruturaUseCase`: campo `private readonly IExecucaoRepository _execucao;`, parâmetro novo
`IExecucaoRepository execucao` no **fim** do construtor e `_execucao = execucao;` no corpo; e
`using Rastreamento.Application.Execucao;` + `using Rastreamento.Domain.Entities;` no topo.

`EditarNo` inteiro passa a ser (a checagem de razão e de descrição é a mesma de antes, agora dentro da
transação, depois de o nó estar travado):

```csharp
  /// <summary>
  /// Edita `Descricao`, `Quantidade` e, no Item, `QuantidadePorPai` (regra 26). NAO cascateia a
  /// quantidade para os filhos — decisao de dominio, nao lacuna: a copia da receita e PRE-PREENCHIMENTO,
  /// e nao existe invariante "filho = pai x razao". `Descricao` vazia/so espaco grava `null` (volta a
  /// herdar a do Componente, regra 19), exceto no no ad-hoc, que nao tem de onde herdar.
  ///
  /// Desde a Fase 3 (spec secao 4.7), a quantidade nao desce abaixo do que ja saiu de "a iniciar" nem,
  /// num pai, abaixo do total montado — e roda no esquema de trava da execucao (secao 8.1): sem ele, um
  /// "reduzir" passaria no meio de um "iniciar" do mesmo no.
  /// </summary>
  public async Task<Result<EstruturaItemDto>> EditarNo(int id, EdicaoDeNoDto edicao, CancellationToken ct)
  {
    if (edicao.Quantidade < PlanejadorDeCopia.QuantidadeMinimaDaColuna)
      return Result<EstruturaItemDto>.Falha(ErroDeQuantidadeInvalida, TipoDeErro.Validacao);

    return await _execucao.ExecutarAsync(async () =>
    {
      if ((await _execucao.TravarNosAsync([id], ct)).Count == 0)
        return Result<EstruturaItemDto>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);
      // Rastreado, e lido DEPOIS da trava: e esta instancia que o SaveChanges grava.
      var no = await _estruturas.ObterPorIdAsync(id, ct);
      if (no is null) return Result<EstruturaItemDto>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);

      if (no.NivelHierarquico == "Item" && !RazaoValida(edicao.QuantidadePorPai))
        return Result<EstruturaItemDto>.Falha(ErroDeRazaoInvalida, TipoDeErro.Validacao);
      if (no.NivelHierarquico == "Peca" && edicao.QuantidadePorPai is not null)
        return Result<EstruturaItemDto>.Falha(ErroDeRazaoNaPeca, TipoDeErro.Validacao);

      var descricao = string.IsNullOrWhiteSpace(edicao.Descricao) ? null : edicao.Descricao.Trim();
      if (no.ComponenteId is null && descricao is null)
        return Result<EstruturaItemDto>.Falha(ErroDeDescricaoObrigatoria, TipoDeErro.Validacao);

      var saldos = await _execucao.ListarSaldosAsync([id], ct);
      var saido = -saldos.Where(s => s.Posicao == Posicoes.AIniciar).Sum(s => s.Quantidade);
      var montado = (await _execucao.ListarTotaisMontadosAsync([id], ct)).GetValueOrDefault(id);
      var piso = Math.Max(saido, montado);
      if (edicao.Quantidade < piso)
        return Result<EstruturaItemDto>.Falha(CodigosDaExecucao.QuantidadeAbaixoDoMovimentado, TipoDeErro.Conflito,
            $"Já saíram {Quantidades.Formatar(saido)} de \"a iniciar\" e {Quantidades.Formatar(montado)} foram montados: "
            + $"a quantidade não pode ficar abaixo de {Quantidades.Formatar(piso)}.");

      no.Descricao = descricao;
      no.Quantidade = edicao.Quantidade;
      no.QuantidadePorPai = edicao.QuantidadePorPai;
      await _estruturas.SalvarAlteracoesAsync(ct);

      var arvore = await _montador.MontarAsync(no.AgrupamentoId, ct);
      var noEditado = BuscarNo(arvore, id)
          ?? throw new InvalidOperationException(
              $"No {id} nao encontrado na arvore recem montada do Agrupamento {no.AgrupamentoId} "
                  + "(M1 da review da Task 4: editado e nao lido de volta — nunca deveria acontecer).");
      return Result<EstruturaItemDto>.Ok(noEditado);
    }, ct);
  }
```

`ExcluirNo` inteiro passa a ser:

```csharp
  /// <summary>
  /// Apaga um no e a subarvore inteira dele. So com o Pedido `Aberto` — e, desde a Fase 3, essa guarda
  /// tambem impede apagar no com movimento, porque o primeiro `Inicio` poe o Pedido em `EmProducao` e o
  /// status nao volta (regra 28). Trava a SUBARVORE inteira antes de ler o status (spec secao 8.1 e
  /// desvio D5 do plano 2): travar so o no da rota deixaria um "iniciar" num descendente correr contra o
  /// apagar e voltar 500 pela `FK_Movimentacao_EstruturaItem`.
  /// </summary>
  public async Task<Result> ExcluirNo(int id, CancellationToken ct)
  {
    // EXCLUIR e CORRECAO DE MONTAGEM, nao descarte — sao operacoes diferentes e tem palavras
    // diferentes. O descarte em Pedido rodando e da Fase 5 (decisao do usuario, 2026-09-25).
    var r = await _execucao.ExecutarAsync(async () =>
    {
      var ids = await _execucao.ListarIdsDaSubarvoreAsync(id, ct);
      var travados = await _execucao.TravarNosAsync(ids, ct);
      var no = travados.SingleOrDefault(n => n.Id == id);
      if (no is null) return Result<bool>.Falha(ErroDeNoNaoEncontrado, TipoDeErro.NaoEncontrado);

      var agrupamento = await _agrupamentos.ObterPorIdAsync(no.AgrupamentoId, ct);
      var pedido = agrupamento is null ? null : await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct);
      if (pedido is null || pedido.Status != StatusAberto)
        return Result<bool>.Falha("PedidoNaoAberto", TipoDeErro.Conflito);

      await _estruturas.RemoverSubarvoreAsync(id, ct);
      return Result<bool>.Ok(true);
    }, ct);

    return r.Sucesso ? Result.Ok() : Result.Falha(r.Erro!, r.TipoDoErro!.Value, r.Detalhe);
  }
```

- [ ] **Step 6: `RemoverSubarvoreAsync` dentro da transação de quem chama**

Trocar em `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`:

````markdown
  /// Transacao explicita, como `GravarArvoreAsync`, mas SEM `Serializable`: apagar nao disputa a
  /// mesma corrida de insercao concorrente que motivou o isolamento la (residual documentado
  /// naquele metodo, fora do escopo desta task). Reune a subarvore NIVEL POR NIVEL (largura),
````

por:

````markdown
  /// Desde a Fase 3 roda DENTRO da transacao SERIALIZABLE de `MontagemDeEstruturaUseCase.ExcluirNo`,
  /// que ja travou a subarvore inteira e leu o status do Pedido com a trava (spec da Fase 3, secao
  /// 8.1): a frase antiga deste comentario — "apagar nao disputa a mesma corrida" — deixou de ser
  /// verdade quando o primeiro `Inicio` passou a poder correr contra o apagar. Chamado fora de
  /// transacao, abre a propria, como antes. Reune a subarvore NIVEL POR NIVEL (largura),
````

Trocar em `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`:

````markdown
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var visitados = new HashSet<int> { id };
````

por:

````markdown
    await using var propria = _db.Database.CurrentTransaction is null
        ? await _db.Database.BeginTransactionAsync(ct)
        : null;

    var visitados = new HashSet<int> { id };
````

Trocar em `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`:

````markdown
      await _db.SaveChangesAsync(ct);
    }

    await tx.CommitAsync(ct);
  }
}
````

por:

````markdown
      await _db.SaveChangesAsync(ct);
    }

    if (propria is not null) await propria.CommitAsync(ct);
  }
}
````

E o residual de `GravarArvoreAsync`. Trocar em `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs`:

````markdown
  /// corrida, e a traducao sem teste que a mate seria guarda encenada. Se a Task 4/5 tocar
  /// concorrencia em `EstruturaItem`, considerar extrair o mesmo padrao.
````

por:

````markdown
  /// corrida, e a traducao sem teste que a mate seria guarda encenada. Se a Task 4/5 tocar
  /// concorrencia em `EstruturaItem`, considerar extrair o mesmo padrao. *Fase 3:* a edicao e a
  /// exclusao de no entraram no esquema de trava da execucao (`IExecucaoRepository.EmTransacaoAsync`,
  /// spec secao 8.1), que traduz 1205/1222; gravar arvore continua fora dele, porque so insere nos
  /// NOVOS, que nenhuma escrita da execucao disputa.
````

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS, todos (os novos: 10 de Roteiro contando as `[Theory]`, 6 de edição).

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~EstruturaRepositoryTests"`
Expected: PASS — `RemoverSubarvoreAsync` chamado fora de transação continua abrindo a própria.

Mutação de conferência, a registrar no relatório: em `RoteiroDoNoUseCase.Substituir`, troque
`FindLastIndex(...) + 1` por `0` — os três casos de `Mexer_em_passo_alcancado_da_PassoJaAlcancado` têm de
falhar. Desfaça.

- [ ] **Step 8: Suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS — inclusive `EstruturaEndpointsTests.DELETE_em_Pedido_nao_Aberto_devolve_409_PedidoNaoAberto`,
que agora passa pelo caminho com trava.

```bash
git add src/Rastreamento.Application src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs \
  src/Rastreamento.Api/Program.cs tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs \
  tests/Rastreamento.Application.Tests
git commit -m "feat(execucao): Roteiro editavel por no, e edicao e exclusao de no no esquema de trava"
```

### Task 9: As leituras — fila, tarefas, contagem, posições e o livro do nó

Spec §5.2 e §7, no formato da seção "Contrato JSON" deste plano. Uma consulta de nós e uma de saldo por
tela (§7.7); a mesma calculadora das escritas.

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (DTOs de leitura)
- Create: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs`
- Modify: `src/Rastreamento.Api/Program.cs`, `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/CenarioDeExecucao.cs` (`Consulta()`)
- Create: `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs`

**Interfaces:**
- Consumes: Tasks 3 a 8; `ISetorRepository.ListarAsync(bool incluirInativos, ct)`,
  `IAgrupamentoRepository.ObterPorIdAsync`, `IEstruturaRepository.ListarDoAgrupamentoAsync` (existentes).
- Produces: os records de leitura abaixo (`NoResumoDto`, `SetorResumoDto`, `DestinoDto`, `LinhaDaFilaDto`,
  `LinhaAguardandoColetaDto`, `FilhoNaMontagemDto`, `MontagemPendenteDto`, `LinhaDeSobraDto`,
  `FilaDoSetorDto`, `TarefaDto`, `TarefasDoSetorDto`, `ContagemDeTarefasDto`, `SaldoDto`, `PosicoesDoNoDto`,
  `LivroDoNoDto`); `ConsultaDeExecucaoUseCase(IExecucaoRepository, IEstruturaRepository, ISetorRepository, IAgrupamentoRepository, IReceitaPadraoRepository)`
  com `Fila(int setorId, ct) : Task<Result<FilaDoSetorDto>>`, `Tarefas(ct) : Task<IReadOnlyList<TarefasDoSetorDto>>`,
  `ContagemDeTarefas(ct) : Task<ContagemDeTarefasDto>`, `Posicoes(int agrupamentoId, ct) : Task<Result<IReadOnlyList<PosicoesDoNoDto>>>`,
  `LivroDoNo(int noId, ct) : Task<Result<LivroDoNoDto>>`; `CenarioDeExecucao.Consulta()`.

- [ ] **Step 1: Escrever os testes que falham**

Em `CenarioDeExecucao`, depois de `Estrutura()`:

```csharp
  public ConsultaDeExecucaoUseCase Consulta() =>
      new(Execucao, Estruturas, Setores,
          new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = PedidoId, Codigo = "AG-01", Tipo = "Avulso" }),
          Catalogo);
```

Criar `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>As leituras da Fase 3 (spec secoes 5.2, 6 e 7), no formato do "Contrato JSON" do plano 2.</summary>
public class ConsultaDeExecucaoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>O caso da spec do Kit: P de 10 (Solda); C de 10 (razao 1) e D de 45 (razao 4), no Corte.</summary>
  private static CenarioDeExecucao Kit()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    c.No(3, 1, 45m, 4m, Corte);
    return c;
  }

  [Fact]
  public async Task A_iniciar_aparece_so_no_Setor_do_primeiro_passo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var corte = (await c.Consulta().Fila(Corte, Ct)).Valor!;
    var dobra = (await c.Consulta().Fila(Dobra, Ct)).Valor!;

    var linha = Assert.Single(corte.AIniciar);
    Assert.Equal((1, 1, 10m), (linha.No.Id, linha.Ordem, linha.Quantidade));
    Assert.Equal("Corte", corte.SetorNome);
    Assert.Empty(dobra.AIniciar);
  }

  [Fact]
  public async Task Em_trabalho_por_passo_e_o_resto_continua_a_iniciar()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 4m);

    var fila = (await c.Consulta().Fila(Corte, Ct)).Valor!;

    Assert.Equal(6m, Assert.Single(fila.AIniciar).Quantidade);
    var emTrabalho = Assert.Single(fila.EmTrabalho);
    Assert.Equal((1, 4m), (emTrabalho.Ordem, emTrabalho.Quantidade));
  }

  [Fact]
  public async Task Aguardando_coleta_mostra_a_tarefa_com_destino_e_a_sobra_a_parte()
  {
    var c = Kit();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 45m);

    var fila = (await c.Consulta().Fila(Corte, Ct)).Valor!;

    var coleta = Assert.Single(fila.AguardandoColeta);
    Assert.Equal((3, 40m), (coleta.No.Id, coleta.Quantidade));
    Assert.Equal("Montagem", coleta.Destino.Tipo);
    Assert.Equal(1, coleta.Destino.PaiId);
    Assert.Equal(Solda, coleta.Destino.SugestaoSetorId);
    Assert.Equal(new[] { new SetorResumoDto(Solda, "Solda") }, coleta.Destino.SetoresPossiveis);
    var sobra = Assert.Single(fila.Sobra);
    Assert.Equal((3, "UltimoPasso", (int?)1, 5m), (sobra.No.Id, sobra.Origem, sobra.Ordem, sobra.Quantidade));
  }

  [Fact]
  public async Task Aguardando_montagem_agrupa_por_pai_com_da_para_montar_e_falta()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 5m, null, Solda);
    c.No(2, 1, 10m, 2m, Corte);
    c.No(3, 1, 5m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 1m);

    var fila = (await c.Consulta().Fila(Solda, Ct)).Valor!;

    var grupo = Assert.Single(fila.AguardandoMontagem);
    Assert.Equal((1, 5m, 1m), (grupo.Pai.Id, grupo.FaltaMontar, grupo.DaParaMontar));
    Assert.Equal(
        new[] { (2, 2m, 5m, (decimal?)4m, (decimal?)0m), (3, 1m, 1m, (decimal?)2m, (decimal?)1m) },
        grupo.Filhos.Select(f => (f.No.Id, f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToArray());
  }

  [Fact]
  public async Task Excesso_em_montagem_aparece_na_sobra_e_diz_quando_o_filho_esta_em_mais_de_um_Setor()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(3, 1, 45m, 4m, Corte);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 30m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 15m);

    var fila = (await c.Consulta().Fila(Solda, Ct)).Valor!;

    var sobra = Assert.Single(fila.Sobra);
    Assert.Equal(("Montagem", (int?)null, 5m, true), (sobra.Origem, sobra.Ordem, sobra.Quantidade, sobra.EmMaisDeUmSetor));
  }

  [Fact]
  public async Task Resumo_do_no_traz_o_caminho_Pedido_Agrupamento_pai()
  {
    var c = Kit();

    var fila = (await c.Consulta().Fila(Corte, Ct)).Valor!;

    var no = fila.AIniciar.Single(l => l.No.Id == 3).No;
    Assert.Equal(("No 3", "PED-01", "AG-01", (int?)1, "No 1"), (no.Descricao, no.PedidoNumero, no.AgrupamentoCodigo, no.PaiId, no.PaiDescricao));
  }

  [Fact]
  public async Task Tarefas_agrupam_por_Setor_de_origem_e_a_contagem_bate()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Pintura);
    c.No(2, null, 10m, null, Dobra, Pintura);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Dobra, 1), Local.AguardandoColeta(Dobra, 1), 3m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Dobra, 2), Local.AguardandoColeta(Dobra, 2), 2m);

    var tarefas = await c.Consulta().Tarefas(Ct);
    var contagem = await c.Consulta().ContagemDeTarefas(Ct);

    Assert.Equal(new[] { Corte, Dobra }, tarefas.Select(t => t.SetorId).ToArray());
    // Na Dobra: o no 1 no passo 2 e o no 2 no passo 1, por Id do no.
    Assert.Equal(new[] { (1, 2), (2, 1) }, tarefas[1].Itens.Select(i => (i.No.Id, i.Ordem)).ToArray());
    Assert.Equal(3, contagem.Total);
  }

  [Fact]
  public async Task Item_pronto_de_pai_sem_Roteiro_aparece_com_paiSemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var tarefa = Assert.Single(Assert.Single(await c.Consulta().Tarefas(Ct)).Itens);

    Assert.True(tarefa.Destino.PaiSemRoteiro);
    Assert.Empty(tarefa.Destino.SetoresPossiveis);
    Assert.Null(tarefa.Destino.SugestaoSetorId);
  }

  [Fact]
  public async Task Pedido_concluido_sai_da_fila_e_das_tarefas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 4m);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var fila = (await c.Consulta().Fila(Corte, Ct)).Valor!;

    Assert.Empty(fila.AIniciar);
    Assert.Empty(fila.AguardandoColeta);
    Assert.Empty(await c.Consulta().Tarefas(Ct));
    Assert.Equal(0, (await c.Consulta().ContagemDeTarefas(Ct)).Total);
  }

  [Fact]
  public async Task Posicoes_trazem_os_saldos_e_o_total_montado_so_de_quem_tem_filhos()
  {
    var c = Kit();
    c.Mover(3, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);

    var posicoes = (await c.Consulta().Posicoes(AgrupamentoId, Ct)).Valor!;

    Assert.Equal(new[] { 1, 2, 3 }, posicoes.Select(p => p.EstruturaItemId).ToArray());
    Assert.Equal(0m, posicoes[0].TotalMontado);
    Assert.Null(posicoes[2].TotalMontado);
    Assert.Equal(
        new[] { new SaldoDto(Posicoes.AIniciar, null, null, null, 40m), new SaldoDto(Posicoes.NoSetor, Corte, "Corte", 1, 5m) },
        posicoes[2].Saldos);
  }

  [Fact]
  public async Task Livro_do_no_traz_os_movimentos_com_a_marca_de_estornado_e_as_montagens_do_pai()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Corte);
    var inicio = (await c.Apontamento().Iniciar(1, new InicioDto(Solda, 2m), Operador, Ct)).Valor!;
    await c.Estorno().EstornarMovimentacao(inicio.Id, Operador, false, Ct);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);

    var livro = (await c.Consulta().LivroDoNo(1, Ct)).Valor!;

    Assert.Equal(new[] { (TiposDeMovimentacao.Inicio, true), (TiposDeMovimentacao.Estorno, false) },
        livro.Movimentacoes.Select(m => (m.Tipo, m.Estornada)).ToArray());
    var montagem = Assert.Single(livro.Montagens);
    Assert.Equal(2, Assert.Single(montagem.Baixas).EstruturaItemId);
  }

  [Fact]
  public async Task Setor_Agrupamento_ou_no_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().Fila(77, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().Posicoes(77, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Consulta().LivroDoNo(77, Ct)).TipoDoErro);
  }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ConsultaDeExecucaoUseCaseTests"`
Expected: FAIL na compilação — `ConsultaDeExecucaoUseCase` e os DTOs de leitura não existem.

- [ ] **Step 3: DTOs de leitura**

Acrescente a `ExecucaoDtos.cs` (o formato é o do "Contrato JSON" do plano; mudar um nome aqui é mudar o
contrato do plano 3):

```csharp
/// <summary>
/// Um no nas telas de fila e tarefas, com o caminho "Pedido > Agrupamento > pai" em campos separados —
/// quem monta o texto e o front. `Descricao` ja com o fallback da regra 19.
/// </summary>
public sealed record NoResumoDto(
    int Id, string Descricao, string? CodigoDoComponente, int PedidoId, string PedidoNumero,
    int AgrupamentoId, string AgrupamentoCodigo, int? PaiId, string? PaiDescricao);

public sealed record SetorResumoDto(int Id, string Nome);

/// <summary>`Tipo`: ProximoPasso | Expedicao | Montagem. Ver o "Contrato JSON" do plano 2.</summary>
public sealed record DestinoDto(
    string Tipo, int? SetorId, string? SetorNome, int? Ordem, int? PaiId, int? SugestaoSetorId,
    IReadOnlyList<SetorResumoDto> SetoresPossiveis, bool PaiSemRoteiro);

public sealed record LinhaDaFilaDto(NoResumoDto No, int Ordem, decimal Quantidade);

public sealed record LinhaAguardandoColetaDto(NoResumoDto No, int Ordem, decimal Quantidade, DestinoDto Destino);

public sealed record FilhoNaMontagemDto(
    NoResumoDto No, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima);

public sealed record MontagemPendenteDto(
    NoResumoDto Pai, decimal FaltaMontar, decimal DaParaMontar, IReadOnlyList<FilhoNaMontagemDto> Filhos);

/// <summary>`Origem`: UltimoPasso (com `Ordem`) | Montagem (sem `Ordem`; excesso no nivel do no).</summary>
public sealed record LinhaDeSobraDto(NoResumoDto No, string Origem, int? Ordem, decimal Quantidade, bool EmMaisDeUmSetor);

public sealed record FilaDoSetorDto(
    int SetorId, string SetorNome,
    IReadOnlyList<LinhaDaFilaDto> AIniciar,
    IReadOnlyList<LinhaDaFilaDto> EmTrabalho,
    IReadOnlyList<LinhaAguardandoColetaDto> AguardandoColeta,
    IReadOnlyList<MontagemPendenteDto> AguardandoMontagem,
    IReadOnlyList<LinhaDeSobraDto> Sobra);

public sealed record TarefaDto(NoResumoDto No, int Ordem, decimal Quantidade, DestinoDto Destino);

public sealed record TarefasDoSetorDto(int SetorId, string SetorNome, IReadOnlyList<TarefaDto> Itens);

public sealed record ContagemDeTarefasDto(int Total);

public sealed record SaldoDto(string Posicao, int? SetorId, string? SetorNome, int? Ordem, decimal Quantidade);

/// <summary>`TotalMontado`: numero nos nos com filhos (zero inclusive), nulo nos sem filhos.</summary>
public sealed record PosicoesDoNoDto(int EstruturaItemId, IReadOnlyList<SaldoDto> Saldos, decimal? TotalMontado);

/// <summary>`Montagens`: as montagens em que o no e o PAI montado.</summary>
public sealed record LivroDoNoDto(IReadOnlyList<MovimentacaoDto> Movimentacoes, IReadOnlyList<MontagemDto> Montagens);
```

- [ ] **Step 4: O caso de uso**

Criar `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// As leituras da Fase 3 (spec secoes 5.2, 6 e 7), todas pela mesma calculadora que as escritas usam.
/// Fila e tarefas leem os nos de TODO Pedido que nao esta `Concluido` nem `Cancelado` — uma consulta
/// de nos e uma de saldo, sem paginacao nem cache (secao 7.7: na escala de uma fabrica, cabe).
/// </summary>
public sealed class ConsultaDeExecucaoUseCase
{
  private const string UltimoPasso = "UltimoPasso";
  private const string EmMontagem = "Montagem";

  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly ISetorRepository _setores;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public ConsultaDeExecucaoUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, ISetorRepository setores,
      IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _setores = setores;
    _agrupamentos = agrupamentos;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public async Task<Result<FilaDoSetorDto>> Fila(int setorId, CancellationToken ct)
  {
    var setor = await _setores.ObterPorIdAsync(setorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<FilaDoSetorDto>();

    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;

    var aIniciar = new List<LinhaDaFilaDto>();
    var emTrabalho = new List<LinhaDaFilaDto>();
    var coleta = new List<LinhaAguardandoColetaDto>();
    var sobra = new List<LinhaDeSobraDto>();

    foreach (var no in calc.Nos)
    {
      if (calc.PrimeiroPasso(no.Id) is PassoDoCalculo primeiro && primeiro.SetorId == setorId
          && calc.Saldo(no.Id, Local.AIniciar) is var aIniciarAqui && aIniciarAqui > 0m)
        aIniciar.Add(new LinhaDaFilaDto(resumos[no.Id], primeiro.Ordem, aIniciarAqui));

      foreach (var (local, quantidade) in calc.Saldos(no.Id))
      {
        if (local.SetorId != setorId || quantidade <= 0m) continue;
        if (local.Posicao == Posicoes.NoSetor)
          emTrabalho.Add(new LinhaDaFilaDto(resumos[no.Id], local.Ordem!.Value, quantidade));
        else if (local.Posicao == Posicoes.AguardandoColeta)
        {
          // D8 do plano 2: a tarefa aqui, a sobra na secao dela — somadas, dao o saldo do passo.
          var ordem = local.Ordem!.Value;
          var tarefa = calc.Tarefa(no.Id, setorId, ordem);
          if (tarefa > 0m)
            coleta.Add(new LinhaAguardandoColetaDto(resumos[no.Id], ordem, tarefa,
                Destino(calc.DestinoDaColeta(no.Id, ordem), nomes)));
          var sobraDaColeta = calc.SobraDaColeta(no.Id, setorId, ordem);
          if (sobraDaColeta > 0m)
            sobra.Add(new LinhaDeSobraDto(resumos[no.Id], UltimoPasso, ordem, sobraDaColeta, false));
        }
        else if (local.Posicao == Posicoes.AguardandoMontagem && calc.ExcessoEmMontagem(no.Id) is var excesso && excesso > 0m)
          sobra.Add(new LinhaDeSobraDto(resumos[no.Id], EmMontagem, null, excesso,
              calc.SetoresOndeAguardaMontagem(no.Id).Count > 1));
      }
    }

    var montagem = calc.Nos
        .Where(n => n.PaiId is not null && calc.AguardandoMontagem(n.Id, setorId) > 0m)
        .Select(n => n.PaiId!.Value)
        .Distinct()
        .Order()
        .Select(paiId =>
        {
          var m = calc.CalcularMontabilidade(paiId, setorId);
          return new MontagemPendenteDto(resumos[paiId], m.FaltaMontar, m.DaParaMontar,
              m.Filhos.Select(f => new FilhoNaMontagemDto(
                  resumos[f.FilhoId], f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToList());
        })
        .ToList();

    return Result<FilaDoSetorDto>.Ok(new FilaDoSetorDto(
        setorId, setor.Nome, aIniciar, emTrabalho, coleta, montagem,
        sobra.OrderBy(s => s.No.Id).ThenBy(s => s.Ordem ?? int.MaxValue).ToList()));
  }

  public async Task<IReadOnlyList<TarefasDoSetorDto>> Tarefas(CancellationToken ct)
  {
    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);

    return estado.Calc.ColetasPendentes()
        .GroupBy(p => p.SetorId)
        .OrderBy(g => g.Key)
        .Select(g => new TarefasDoSetorDto(g.Key, nomes.GetValueOrDefault(g.Key, string.Empty),
            g.Select(p => new TarefaDto(resumos[p.EstruturaItemId], p.Ordem, p.Tarefa,
                Destino(estado.Calc.DestinoDaColeta(p.EstruturaItemId, p.Ordem), nomes))).ToList()))
        .ToList();
  }

  /// <summary>A mesma conta de `Tarefas` (spec secao 7.7), sem montar os DTOs.</summary>
  public async Task<ContagemDeTarefasDto> ContagemDeTarefas(CancellationToken ct)
  {
    var contexto = await _execucao.ListarNosEmProducaoAsync(ct);
    var estado = await _leitor.CarregarAsync(contexto.Select(x => x.No).ToList(), ct);
    return new ContagemDeTarefasDto(estado.Calc.ColetasPendentes().Count);
  }

  public async Task<Result<IReadOnlyList<PosicoesDoNoDto>>> Posicoes(int agrupamentoId, CancellationToken ct)
  {
    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Falhas.NaoEncontrado<IReadOnlyList<PosicoesDoNoDto>>();

    var nos = await _estruturas.ListarDoAgrupamentoAsync(agrupamentoId, ct);
    var estado = await _leitor.CarregarAsync(nos, ct);
    var nomes = await NomesDosSetoresAsync(ct);

    return Result<IReadOnlyList<PosicoesDoNoDto>>.Ok(nos.OrderBy(n => n.Id).Select(n => new PosicoesDoNoDto(
        n.Id,
        estado.Calc.Saldos(n.Id).Select(s => new SaldoDto(
            s.Local.Posicao, s.Local.SetorId,
            s.Local.SetorId is int setorId ? nomes.GetValueOrDefault(setorId) : null,
            s.Local.Ordem, s.Quantidade)).ToList(),
        estado.Calc.TemFilhos(n.Id) ? (decimal?)estado.Calc.TotalMontado(n.Id) : null)).ToList());
  }

  public async Task<Result<LivroDoNoDto>> LivroDoNo(int noId, CancellationToken ct)
  {
    if ((await _execucao.ListarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<LivroDoNoDto>();

    var movimentos = await _execucao.ListarMovimentacoesDoNoAsync(noId, ct);
    var montagens = await _execucao.ListarMontagensDoNoAsync(noId, ct);
    return Result<LivroDoNoDto>.Ok(new LivroDoNoDto(
        await _projetor.ProjetarMovimentacoesAsync(movimentos, ct),
        await _projetor.ProjetarMontagensAsync(montagens, ct)));
  }

  private async Task<(EstadoDeExecucao Estado, IReadOnlyDictionary<int, NoResumoDto> Resumos)> CarregarEmProducaoAsync(
      CancellationToken ct)
  {
    var contexto = await _execucao.ListarNosEmProducaoAsync(ct);
    var estado = await _leitor.CarregarAsync(contexto.Select(x => x.No).ToList(), ct);
    IReadOnlyDictionary<int, NoResumoDto> resumos = contexto.ToDictionary(x => x.No.Id, x => new NoResumoDto(
        x.No.Id, estado.Nome(x.No.Id), estado.Codigos.GetValueOrDefault(x.No.Id), x.PedidoId, x.PedidoNumero,
        x.AgrupamentoId, x.AgrupamentoCodigo, x.No.EstruturaPaiId,
        x.No.EstruturaPaiId is int pai ? estado.Nome(pai) : null));
    return (estado, resumos);
  }

  /// <summary>O catalogo de Setores inteiro, inativos inclusive: o que ja esta num Setor inativado continua aparecendo.</summary>
  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(CancellationToken ct) =>
      (await _setores.ListarAsync(incluirInativos: true, ct)).ToDictionary(s => s.Id, s => s.Nome);

  private static DestinoDto Destino(DestinoCalculado destino, IReadOnlyDictionary<int, string> nomes) =>
      destino.Tipo switch
      {
        TipoDeDestino.ProximoPasso => new DestinoDto(
            "ProximoPasso", destino.Passo!.Value.SetorId, nomes.GetValueOrDefault(destino.Passo.Value.SetorId),
            destino.Passo.Value.Ordem, null, null, Array.Empty<SetorResumoDto>(), false),
        TipoDeDestino.Expedicao => new DestinoDto(
            "Expedicao", null, null, null, null, null, Array.Empty<SetorResumoDto>(), false),
        _ => new DestinoDto(
            "Montagem", null, null, null, destino.PaiId, destino.SugestaoSetorId,
            destino.SetoresPossiveis.Select(s => new SetorResumoDto(s, nomes.GetValueOrDefault(s, string.Empty))).ToList(),
            destino.PaiSemRoteiro),
      };
}
```

Em `Program.cs`: `builder.Services.AddScoped<ConsultaDeExecucaoUseCase>();`. Em
`RegistroDeDependenciasTests`: `[InlineData(typeof(ConsultaDeExecucaoUseCase))]`.

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ConsultaDeExecucaoUseCaseTests"`
Expected: PASS (12 testes).

Mutação de conferência, a registrar no relatório: em `Fila`, troque `tarefa` por
`calc.Saldo(no.Id, local)` na linha do `coleta.Add` — `Aguardando_coleta_mostra_a_tarefa_com_destino_e_a_sobra_a_parte`
tem de falhar (a unidade apareceria duas vezes, D8). Desfaça.

- [ ] **Step 6: Suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx`
Expected: 0 warnings; tudo PASS.

```bash
git add src/Rastreamento.Application/Execucao src/Rastreamento.Api/Program.cs \
  tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs tests/Rastreamento.Application.Tests/Execucao
git commit -m "feat(execucao): fila do Setor, tarefas, contagem, posicoes e livro do no"
```

### Task 10: As rotas — controllers, perfis e o espelho no front

Um controller por conjunto de perfis (D1), porque a guarda `permissoesEspelhamOBackend.test.ts` compara
todo `[Authorize(Roles)]` de um arquivo com uma entrada só de `permissoes.ts`. Esta task prova o
**roteamento e os perfis**; o comportamento de cada rota (códigos, corpos) é a Task 11.

**Files:**
- Create: `src/Rastreamento.Api/Controllers/ExecucaoControllerBase.cs`, `ApontamentoController.cs`,
  `EntregaController.cs`, `EstornoController.cs`, `RoteiroDoNoController.cs`
- Modify: `src/Rastreamento.Api/Controllers/EstruturaController.cs` (`GET agrupamentos/{id}/posicoes`)
- Modify: `tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs` (`TabelaAprovada`)
- Modify: `tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs` (perfil)
- Create: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs`
- Modify: `web/src/auth/permissoes.ts`, `web/src/auth/permissoes.test.ts`,
  `web/src/auth/permissoesEspelhamOBackend.test.ts`
- Modify: `specs/05-api-endpoints.md` (seção "Execução / Rastreamento")

**Interfaces:**
- Consumes: os cinco casos de uso das Tasks 5 a 9.
- Produces: as rotas do "Contrato JSON"; `ExecucaoControllerBase` (`Traduzir<T>`, `Recusar`,
  `UsuarioDaSessao`, `EhPcpOuAdministrador`); `Recurso` do front com `apontamento`, `entrega`, `roteiro`,
  `estorno` — **o plano 3 consome estas quatro chaves**; `UsuarioDeTeste.CriarAsync(servicos, prefixo, perfil = "Administrador")`
  e `UsuarioDeTeste.Perfil`.

- [ ] **Step 1: Escrever os testes que falham — backend**

Em `PerfisDeEscritaDeclaradosTests.TabelaAprovada`, depois de
`["DELETE estrutura/{id:int}"] = ["PCP", "Administrador"],`:

```csharp

    // Execucao (Fase 3): um controller por conjunto de perfis — a guarda do front compara todo
    // `[Authorize(Roles)]` de um arquivo com UMA entrada de `permissoes.ts` (desvio D1 do plano 2).
    ["POST estrutura/{id:int}/inicios"] = ["Operador", "Administrador"],
    ["POST estrutura/{id:int}/terminos"] = ["Operador", "Administrador"],
    ["POST estrutura/{id:int}/montagens"] = ["Operador", "Administrador"],
    ["POST entregas"] = ["Movimentador", "Administrador"],
    // Estorno: o `[Authorize]` deixa passar quem pode ser autor, mais o PCP; autor x PCP quem decide e o
    // caso de uso (403 `Proibido`). O de montagem declara o Movimentador tambem (desvio D2 do plano 2).
    ["POST movimentacoes/{id:int}/estorno"] = ["Operador", "Movimentador", "PCP", "Administrador"],
    ["POST montagens/{id:int}/estorno"] = ["Operador", "Movimentador", "PCP", "Administrador"],
    ["PUT estrutura/{id:int}/roteiro"] = ["PCP", "Administrador"],
```

Em `UsuarioDeTeste`, o perfil vira parâmetro. Trocar em `tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs`:

````markdown
  public string NomeUsuario { get; }

  public int Id { get; private set; }

  private UsuarioDeTeste(IServiceProvider servicos, string nomeUsuario)
  {
    _servicos = servicos;
    NomeUsuario = nomeUsuario;
  }

  /// <param name="prefixo">Rotulo curto (ate 17 caracteres) para identificar o teste dono.</param>
  public static async Task<UsuarioDeTeste> CriarAsync(IServiceProvider servicos, string prefixo)
  {
    // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao anterior
    // que tenha morrido antes da limpeza. prefixo(<=17) + '-' + 32 hex cabe no NVARCHAR(50).
    var usuario = new UsuarioDeTeste(servicos, $"{prefixo}-{Guid.NewGuid():N}");

    using var escopo = servicos.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");
````

por:

````markdown
  public string NomeUsuario { get; }

  public string Perfil { get; }

  public int Id { get; private set; }

  private UsuarioDeTeste(IServiceProvider servicos, string nomeUsuario, string perfil)
  {
    _servicos = servicos;
    NomeUsuario = nomeUsuario;
    Perfil = perfil;
  }

  /// <param name="prefixo">Rotulo curto (ate 17 caracteres) para identificar o teste dono.</param>
  /// <param name="perfil">
  /// Nome do perfil, como esta em `db/seed.sql`. A Fase 3 precisa de usuario REAL por perfil: o autor de
  /// um movimento e FK para `dbo.Usuario`, e o estorno compara o autor (spec secao 9.3).
  /// </param>
  public static async Task<UsuarioDeTeste> CriarAsync(IServiceProvider servicos, string prefixo, string perfil = "Administrador")
  {
    // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao anterior
    // que tenha morrido antes da limpeza. prefixo(<=17) + '-' + 32 hex cabe no NVARCHAR(50).
    var usuario = new UsuarioDeTeste(servicos, $"{prefixo}-{Guid.NewGuid():N}", perfil);

    using var escopo = servicos.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var perfilDoBanco = await db.Perfis.SingleAsync(p => p.Nome == perfil);
````

e, no mesmo método, `PerfilId = perfil.Id,` passa a `PerfilId = perfilDoBanco.Id,`.

Criar `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs` — nesta task, só a matriz de perfis e a
autenticação; a Task 11 acrescenta o comportamento:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Rotas da Fase 3 (spec secao 9.3). Os 403 desta classe sao do `[Authorize(Roles)]`, que roda antes do
/// binding e da action: Ids inexistentes de proposito, sem tocar o banco. O 403 do caso de uso (estorno
/// alheio) e o comportamento de cada rota estao em `ExecucaoEndpointsTests.Comportamento` (Task 11).
/// </summary>
public partial class ExecucaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public ExecucaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private HttpClient ClienteComo(string perfil, int usuarioId = 1)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil, usuarioId));
    return cliente;
  }

  private static StringContent Json(string corpo) => new(corpo, Encoding.UTF8, "application/json");

  [Theory]
  [InlineData("Movimentador", "POST", "/api/estrutura/999999/inicios")]
  [InlineData("PCP", "POST", "/api/estrutura/999999/terminos")]
  [InlineData("Gestao", "POST", "/api/estrutura/999999/montagens")]
  [InlineData("Operador", "POST", "/api/entregas")]
  [InlineData("PCP", "POST", "/api/entregas")]
  [InlineData("Operador", "PUT", "/api/estrutura/999999/roteiro")]
  [InlineData("Movimentador", "PUT", "/api/estrutura/999999/roteiro")]
  [InlineData("Gestao", "POST", "/api/movimentacoes/999999/estorno")]
  [InlineData("Qualidade", "POST", "/api/montagens/999999/estorno")]
  [InlineData("Almoxarifado", "POST", "/api/movimentacoes/999999/estorno")]
  public async Task Perfil_sem_a_acao_recebe_403(string perfil, string verbo, string rota)
  {
    var resposta = await ClienteComo(perfil).SendAsync(new HttpRequestMessage(new HttpMethod(verbo), rota)
    {
      Content = Json("{}"),
    });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Theory]
  [InlineData("/api/tarefas")]
  [InlineData("/api/tarefas/contagem")]
  [InlineData("/api/setores/999999/fila")]
  [InlineData("/api/agrupamentos/999999/posicoes")]
  [InlineData("/api/estrutura/999999/movimentacoes")]
  [InlineData("/api/estrutura/999999/roteiro")]
  public async Task Sem_token_nenhuma_leitura_responde(string rota)
  {
    var resposta = await _factory.CreateClient().GetAsync(rota);

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Theory]
  [InlineData("/api/tarefas")]
  [InlineData("/api/tarefas/contagem")]
  public async Task Gestao_le_as_tarefas(string rota)
  {
    var resposta = await ClienteComo("Gestao").GetAsync(rota);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }
}
```

- [ ] **Step 2: Escrever os testes que falham — front**

Em `web/src/auth/permissoes.test.ts`, no teste `deixa Administrador escrever em tudo`, a lista passa a ser
`['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos', 'estrutura', 'apontamento', 'entrega', 'roteiro', 'estorno'] as const`,
e, antes do teste `nega perfil desconhecido em vez de liberar`, acrescente:

```ts
  it('Fase 3: cada ação de execução é de quem a faz no chão de fábrica', () => {
    // Espelha os quatro controllers da execução (desvio D1 do plano 2 da Fase 3).
    expect(podeEscrever('Operador', 'apontamento')).toBe(true)
    expect(podeEscrever('Movimentador', 'apontamento')).toBe(false)
    expect(podeEscrever('Movimentador', 'entrega')).toBe(true)
    expect(podeEscrever('Operador', 'entrega')).toBe(false)
    expect(podeEscrever('PCP', 'roteiro')).toBe(true)
    expect(podeEscrever('Operador', 'roteiro')).toBe(false)
    for (const p of ['Operador', 'Movimentador', 'PCP']) {
      expect(podeEscrever(p, 'estorno'), p).toBe(true)
    }
    for (const p of ['Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['apontamento', 'entrega', 'roteiro', 'estorno'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })
```

Em `permissoesEspelhamOBackend.test.ts`, no `CONTROLLERS_POR_RECURSO`, depois de `estrutura`:

```ts
  apontamento: ['ApontamentoController.cs'],
  entrega: ['EntregaController.cs'],
  roteiro: ['RoteiroDoNoController.cs'],
  estorno: ['EstornoController.cs'],
```

e, no `ISENTOS`, depois de `'CadastroControllerBase.cs'`:

```ts
  'ExecucaoControllerBase.cs':
    'base abstrata dos controllers da Fase 3, sem rota própria — só traduz Result em status e lê a ' +
    'sessão. Perfil é decisão de cada controller concreto; um `Roles` aqui valeria para os quatro.',
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ExecucaoEndpointsTests|FullyQualifiedName~PerfisDeEscritaDeclaradosTests"`
Expected: FAIL — 404 em vez de 403/401 nas rotas novas, e `PerfisDeEscritaDeclaradosTests` reclamando das
identidades da tabela que o roteamento não tem.

Run: `cd web && npm test -- --run src/auth`
Expected: FAIL — `'apontamento'` não é um `Recurso` (erro de tipo aparece no `npm run build`; no Vitest,
`podeEscrever` quebra ao ler `ESCRITA[recurso]` indefinido) e os quatro controllers não existem no disco.

- [ ] **Step 4: A base e os controllers**

Criar `src/Rastreamento.Api/Controllers/ExecucaoControllerBase.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Common;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que os controllers da Fase 3 fazem igual: traduzir `Result` em status e corpo
/// `{ erro, mensagem }` (spec da Fase 3, secao 8.2) e ler a sessao. Abstrata, sem rota nem
/// `[Authorize]`: perfil e decisao de cada controller concreto, e `permissoesEspelhamOBackend.test.ts`
/// isenta este arquivo conferindo que ele nunca declara `Roles`.
/// </summary>
public abstract class ExecucaoControllerBase : ControllerBase
{
  protected IActionResult Traduzir<T>(Result<T> r, bool criado = false)
  {
    if (r.Sucesso) return criado ? StatusCode(StatusCodes.Status201Created, r.Valor) : Ok(r.Valor);
    return Recusar(r.TipoDoErro, r.Erro, r.Detalhe);
  }

  /// <summary>
  /// 404 com `NotFound()`, sem `erro`, como `EstruturaController.Recusar`. O 403 daqui e o do CASO DE USO (`Proibido`) e
  /// leva corpo; o 403 do `[Authorize(Roles)]` nem chega a action.
  /// </summary>
  protected IActionResult Recusar(TipoDeErro? tipo, string? erro, string? detalhe)
  {
    if (tipo == TipoDeErro.NaoEncontrado) return NotFound();

    object corpo = detalhe is null ? new { erro } : new { erro, mensagem = detalhe };
    return tipo switch
    {
      TipoDeErro.Conflito => Conflict(corpo),
      TipoDeErro.Proibido => StatusCode(StatusCodes.Status403Forbidden, corpo),
      _ => BadRequest(corpo),
    };
  }

  /// <summary>Mesmo criterio de `CadastroControllerBase.UsuarioDaSessao`: sem `sub`, 401.</summary>
  protected int? UsuarioDaSessao() => int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

  /// <summary>Quem estorna registro alheio (spec secao 4.5). `RoleClaimType = "role"` no `Program.cs`.</summary>
  protected bool EhPcpOuAdministrador() => User.IsInRole("PCP") || User.IsInRole("Administrador");
}
```

Criar `src/Rastreamento.Api/Controllers/ApontamentoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O que o Operador registra no Setor — iniciar, terminar, montar — e a fila que ele le. Sem `[Route]`
/// de classe: as rotas de no sao `estrutura/{id}/...` (o prefixo da Fase 2) e a fila e `setores/{id}/fila`.
/// </summary>
[ApiController]
[Authorize]
public class ApontamentoController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: iniciar, terminar e montar sao do Operador.</summary>
  private const string PerfisDeEscrita = "Operador,Administrador";

  private readonly ApontamentoUseCase _apontamento;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public ApontamentoController(ApontamentoUseCase apontamento, ConsultaDeExecucaoUseCase consulta)
  {
    _apontamento = apontamento;
    _consulta = consulta;
  }

  [HttpPost("estrutura/{id:int}/inicios")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Iniciar(int id, [FromBody] InicioDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Iniciar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/terminos")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Terminar(int id, [FromBody] TerminoDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Terminar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpPost("estrutura/{id:int}/montagens")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Montar(int id, [FromBody] MontagemNovaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _apontamento.Montar(id, dto, usuarioId, ct), criado: true);
  }

  [HttpGet("setores/{id:int}/fila")]
  public async Task<IActionResult> Fila(int id, CancellationToken ct) => Traduzir(await _consulta.Fila(id, ct));
}
```

Criar `src/Rastreamento.Api/Controllers/EntregaController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>O Movimentador: as tarefas que ele le e a entrega em lista que ele registra.</summary>
[ApiController]
[Authorize]
public class EntregaController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: entregar (inclusive redirecionar) e do Movimentador.</summary>
  private const string PerfisDeEscrita = "Movimentador,Administrador";

  private readonly EntregaUseCase _entrega;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public EntregaController(EntregaUseCase entrega, ConsultaDeExecucaoUseCase consulta)
  {
    _entrega = entrega;
    _consulta = consulta;
  }

  [HttpPost("entregas")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Entregar([FromBody] EntregaDto dto, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _entrega.Entregar(dto, usuarioId, ct), criado: true);
  }

  [HttpGet("tarefas")]
  public async Task<IActionResult> Tarefas(CancellationToken ct) => Ok(await _consulta.Tarefas(ct));

  [HttpGet("tarefas/contagem")]
  public async Task<IActionResult> Contagem(CancellationToken ct) => Ok(await _consulta.ContagemDeTarefas(ct));
}
```

Criar `src/Rastreamento.Api/Controllers/EstornoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Estornar e ler o livro do no. O `[Authorize]` deixa passar quem pode ser autor de um registro, mais o
/// PCP; "o autor ou PCP/Administrador" e decisao do caso de uso, que devolve 403 `Proibido` com corpo
/// (spec secoes 4.5 e 5.1). Os dois estornos declaram os mesmos perfis porque vivem aqui (desvio D2 do
/// plano 2): no de montagem, o Movimentador nunca e autor, e cai no 403 do caso de uso.
/// </summary>
[ApiController]
[Authorize]
public class EstornoController : ExecucaoControllerBase
{
  private const string PerfisDeEscrita = "Operador,Movimentador,PCP,Administrador";

  private readonly EstornoUseCase _estorno;
  private readonly ConsultaDeExecucaoUseCase _consulta;

  public EstornoController(EstornoUseCase estorno, ConsultaDeExecucaoUseCase consulta)
  {
    _estorno = estorno;
    _consulta = consulta;
  }

  [HttpPost("movimentacoes/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarMovimentacao(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarMovimentacao(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  [HttpPost("montagens/{id:int}/estorno")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> EstornarMontagem(int id, CancellationToken ct)
  {
    if (UsuarioDaSessao() is not int usuarioId) return Unauthorized();
    return Traduzir(await _estorno.EstornarMontagem(id, usuarioId, EhPcpOuAdministrador(), ct), criado: true);
  }

  [HttpGet("estrutura/{id:int}/movimentacoes")]
  public async Task<IActionResult> Livro(int id, CancellationToken ct) => Traduzir(await _consulta.LivroDoNo(id, ct));
}
```

Criar `src/Rastreamento.Api/Controllers/RoteiroDoNoController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// O Roteiro de um no (spec secao 4.6). Controller proprio, e nao carona no `EstruturaController`, embora
/// os perfis hoje coincidam: e o `Recurso` `roteiro` do front, e a primeira vez que os perfis divergirem
/// a carona seria descoberta como bug.
/// </summary>
[ApiController]
[Authorize]
public class RoteiroDoNoController : ExecucaoControllerBase
{
  /// <summary>Spec secao 4.8: editar o Roteiro do no e do PCP.</summary>
  private const string PerfisDeEscrita = "PCP,Administrador";

  private readonly RoteiroDoNoUseCase _roteiro;

  public RoteiroDoNoController(RoteiroDoNoUseCase roteiro) => _roteiro = roteiro;

  [HttpGet("estrutura/{id:int}/roteiro")]
  public async Task<IActionResult> Obter(int id, CancellationToken ct) => Traduzir(await _roteiro.Obter(id, ct));

  [HttpPut("estrutura/{id:int}/roteiro")]
  [Authorize(Roles = PerfisDeEscrita)]
  public async Task<IActionResult> Substituir(int id, [FromBody] RoteiroNovoDto dto, CancellationToken ct) =>
      Traduzir(await _roteiro.Substituir(id, dto, ct));
}
```

Em `EstruturaController`: `using Rastreamento.Application.Execucao;`, o campo
`private readonly ConsultaDeExecucaoUseCase _consulta;`, o construtor passa a
`public EstruturaController(MontagemDeEstruturaUseCase montagem, ConsultaDeExecucaoUseCase consulta)` com
`_montagem = montagem; _consulta = consulta;`, e a ação nova, depois de `Obter`:

```csharp
  /// <summary>Saldo por posicao de cada no do Agrupamento (spec secao 5.2). Leitura de qualquer autenticado.</summary>
  [HttpGet("agrupamentos/{agrupamentoId:int}/posicoes")]
  public async Task<IActionResult> Posicoes(int agrupamentoId, CancellationToken ct) =>
      Traduzir(await _consulta.Posicoes(agrupamentoId, ct));
```

- [ ] **Step 5: O espelho no front**

Em `web/src/auth/permissoes.ts`, o tipo:

```ts
export type Recurso =
  | 'setores'
  | 'materiais'
  | 'componentes'
  | 'pedidos'
  | 'agrupamentos'
  | 'estrutura'
  | 'apontamento'
  | 'entrega'
  | 'roteiro'
  | 'estorno'
```

e, no `ESCRITA`, depois de `estrutura`:

```ts
  // Fase 3 — execução. Um `Recurso` por conjunto de perfis, porque a guarda de espelhamento compara cada
  // controller com UMA entrada daqui (desvio D1 do plano 2 da Fase 3; a spec previa três chaves).
  // Iniciar, terminar e montar, no chão de fábrica.
  apontamento: ['Operador', 'Administrador'],
  // Levar o que aguarda coleta, e redirecionar o que aguarda montagem.
  entrega: ['Movimentador', 'Administrador'],
  // Editar o Roteiro de um nó. Não é `estrutura`, embora os perfis hoje coincidam — mesmo motivo do
  // comentário de `estrutura` acima.
  roteiro: ['PCP', 'Administrador'],
  // Quem PODE ser autor de um registro, mais o PCP. O botão ainda compara o autor, e quem decide é o
  // 403 `Proibido` do backend.
  estorno: ['Operador', 'Movimentador', 'PCP', 'Administrador'],
```

- [ ] **Step 6: O contrato do `05`**

Trocar em `specs/05-api-endpoints.md`:

````markdown
  `POST /montagens/{id}/estorno` *(Operador, PCP)* — desfazem um registro com o movimento inverso,
  enquanto a quantidade não tiver andado. Só o autor, ou PCP ou Administrador (403 para os demais,
  decidido no caso de uso).
````

por:

````markdown
  `POST /montagens/{id}/estorno` *(Operador, PCP)* — desfazem um registro com o movimento inverso,
  enquanto a quantidade não tiver andado. Só o autor, ou PCP ou Administrador (403 para os demais,
  decidido no caso de uso). As duas rotas declaram os mesmos perfis no `[Authorize]` — Operador,
  Movimentador e PCP —, porque vivem no mesmo controller; na de montagem, o Movimentador, que nunca é
  autor de uma, recebe o 403 do caso de uso.
````

Trocar em `specs/05-api-endpoints.md`:

````markdown
- `GET /estrutura/{id}/roteiro` — o Roteiro do nó, com os passos já alcançados marcados.
````

por:

````markdown
- `GET /estrutura/{id}/roteiro` — o Roteiro do nó, com os passos já alcançados marcados.

O formato exato de cada corpo e de cada resposta está na seção "Contrato JSON" do plano 2 da Fase 3
(`docs/superpowers/plans/2026-09-25-fase-3-backend.md`), que é o que o front consome.
````

Trocar em `specs/05-api-endpoints.md`:

````markdown
| 400 | `RoteiroInvalido` | Setor inexistente ou inativo entrando no Roteiro |
````

por:

````markdown
| 400 | `RoteiroInvalido` | Setor inexistente ou inativo entrando no Roteiro |
| 400 | `OrigemInvalida` | origem da entrega fora de `AguardandoColeta`/`AguardandoMontagem`, ou com Setor e passo que não combinam com a posição |
````

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ExecucaoEndpointsTests|FullyQualifiedName~PerfisDeEscritaDeclaradosTests"`
Expected: 0 warnings; PASS (18 casos de `ExecucaoEndpointsTests`; as quatro asserções de `PerfisDeEscritaDeclaradosTests`).

Run: `cd web && npm test -- --run && npm run build`
Expected: PASS e build limpo. Confira que a guarda de espelhamento leu os quatro controllers novos: a
asserção `não passa calada: todo controller mapeado tem pelo menos um atributo de perfis` itera o mapa
por arquivo.

- [ ] **Step 8: Suíte inteira e commit**

Run: `dotnet test Rastreamento.slnx`
Expected: tudo PASS.

```bash
git add src/Rastreamento.Api/Controllers tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs \
  tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.cs \
  web/src/auth specs/05-api-endpoints.md
git commit -m "feat(api): rotas da execucao, um controller por conjunto de perfis, e o espelho no front"
```

### Task 11: O comportamento pela API e o critério de pronto da fase

Spec §9.3: cada rota com o status, o código e a frase certos, com usuários reais por perfil; e o critério
de pronto — um Pedido Avulso A ← B, C percorrido inteiro, com `GET /agrupamentos/{id}/posicoes`
respondendo, a cada passo, onde está cada peça e se ela aguarda coleta.

**Files:**
- Create: `tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs`
- Create: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`
- Create: `tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs`

**Interfaces:**
- Consumes: as rotas da Task 10 e o "Contrato JSON"; `UsuarioDeTeste.CriarAsync(..., perfil)`,
  `TokenDeTeste.Emitir(factory, perfil, usuarioId)`, `StlDeTesteDaApi.CuboBinario()` (existentes).
- Produces: `CenarioDaFase3NaApi` — Peça A (10, Solda → Pintura), filhos ad-hoc B (20, razão 2, Corte)
  e C (10, razão 1, Dobra), usuários `Operador`, `Movimentador`, `Pcp`, `Gestao`, e `Como(usuario)`.

Esta task não muda código de produção. Se um teste daqui falhar, o defeito está numa task anterior:
conserte lá, com teste de Application que o pegue, e registre no relatório.

- [ ] **Step 1: O cenário**

Criar `tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Um Pedido Avulso pronto para a Fase 3: Peca A (10; Roteiro Solda -> Pintura) com dois filhos ad-hoc,
/// B (20, razao 2; Corte) e C (10, razao 1; Dobra), e um usuario REAL por perfil — a autoria do livro e
/// FK para `dbo.Usuario`, e o estorno compara o autor. Criado pela API onde existe rota (Pedido,
/// Agrupamento, arvore, Roteiro); Setores e o Componente com solido vao direto no banco, como em
/// `EstruturaEndpointsTests`. A limpeza respeita as FKs: estornos, movimentos, montagens, arvore,
/// Agrupamento, Pedido, Componente, Setores e, por ultimo, os usuarios.
/// </summary>
internal sealed class CenarioDaFase3NaApi : IAsyncDisposable
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<UsuarioDeTeste> _usuarios = [];
  private int _arquivoId;

  public UsuarioDeTeste Operador { get; private set; } = null!;
  public UsuarioDeTeste Movimentador { get; private set; } = null!;
  public UsuarioDeTeste Pcp { get; private set; } = null!;
  public UsuarioDeTeste Gestao { get; private set; } = null!;
  public int Corte { get; private set; }
  public int Dobra { get; private set; }
  public int Solda { get; private set; }
  public int Pintura { get; private set; }
  public int PedidoId { get; private set; }
  public int AgrupamentoId { get; private set; }
  public int ComponenteId { get; private set; }
  public int A { get; private set; }
  public int B { get; private set; }
  public int C { get; private set; }

  private CenarioDaFase3NaApi(WebApplicationFactory<Program> factory) => _factory = factory;

  public static async Task<CenarioDaFase3NaApi> CriarAsync(WebApplicationFactory<Program> factory)
  {
    var c = new CenarioDaFase3NaApi(factory);
    c.Operador = await c.UsuarioAsync("f3-operador", "Operador");
    c.Movimentador = await c.UsuarioAsync("f3-movimentador", "Movimentador");
    c.Pcp = await c.UsuarioAsync("f3-pcp", "PCP");
    c.Gestao = await c.UsuarioAsync("f3-gestao", "Gestao");

    var rotulo = $"{Guid.NewGuid():N}"[..8];
    using (var escopo = factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var setores = new[] { "Corte", "Dobra", "Solda", "Pintura" }
          .Select(n => new Setor { Nome = $"f3-{rotulo}-{n}", Ativo = true }).ToArray();
      db.Setores.AddRange(setores);
      var arquivo = new ArquivoDeComponente
      {
        NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = 1,
      };
      db.ArquivosDeComponente.Add(arquivo);
      await db.SaveChangesAsync();
      var componente = new Componente
      {
        Codigo = $"f3-{rotulo}", Descricao = "Chassi da Fase 3", Tipo = "Fabricado", Ativo = true,
        ArquivoSolidoId = arquivo.Id,
      };
      db.Componentes.Add(componente);
      await db.SaveChangesAsync();
      (c.Corte, c.Dobra, c.Solda, c.Pintura) = (setores[0].Id, setores[1].Id, setores[2].Id, setores[3].Id);
      c._arquivoId = arquivo.Id;
      c.ComponenteId = componente.Id;
    }

    var pcp = c.Como(c.Pcp);
    c.PedidoId = await IdAsync(await pcp.PostAsJsonAsync("/api/pedidos", new { numero = $"f3-{rotulo}", cliente = "Cliente F3" }));
    c.AgrupamentoId = await IdAsync(await pcp.PostAsJsonAsync(
        $"/api/pedidos/{c.PedidoId}/agrupamentos", new { codigo = "AG-01", tipo = "Avulso" }));
    c.A = await IdAsync(await pcp.PostAsJsonAsync(
        $"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade = 10m, requerRelatorioDimensional = false }));
    c.B = await IdAsync(await pcp.PostAsJsonAsync(
        $"/api/estrutura/{c.A}/filhos", new { componenteId = (int?)null, descricao = "Suporte", quantidade = 20m, quantidadePorPai = 2m }));
    c.C = await IdAsync(await pcp.PostAsJsonAsync(
        $"/api/estrutura/{c.A}/filhos", new { componenteId = (int?)null, descricao = "Calço", quantidade = 10m, quantidadePorPai = 1m }));
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Solda, c.Pintura } }));
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/roteiro", new { passos = new[] { c.Corte } }));
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.C}/roteiro", new { passos = new[] { c.Dobra } }));
    return c;
  }

  public HttpClient Como(UsuarioDeTeste usuario)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, usuario.Perfil, usuario.Id));
    return cliente;
  }

  public static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

  private static async Task<int> IdAsync(HttpResponseMessage resposta)
  {
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  /// <summary>Falha de arranjo diz o corpo, e nao so "esperava 2xx".</summary>
  public static async Task Garantir(HttpResponseMessage resposta)
  {
    if (!resposta.IsSuccessStatusCode)
      throw new InvalidOperationException(
          $"{resposta.RequestMessage?.Method} {resposta.RequestMessage?.RequestUri}: {(int)resposta.StatusCode} "
          + await resposta.Content.ReadAsStringAsync());
  }

  private async Task<UsuarioDeTeste> UsuarioAsync(string prefixo, string perfil)
  {
    var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, prefixo, perfil);
    _usuarios.Add(usuario);
    return usuario;
  }

  public async ValueTask DisposeAsync()
  {
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var ag = AgrupamentoId;
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Movimentacao WHERE EstornoDeId IS NOT NULL AND EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Movimentacao WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Montagem WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {ag}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {PedidoId}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Componente WHERE Id = {ComponenteId}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.ArquivoDeComponente WHERE Id = {_arquivoId}");
      foreach (var setor in new[] { Corte, Dobra, Solda, Pintura })
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {setor}");
    }
    foreach (var usuario in _usuarios) await usuario.DisposeAsync();
  }
}
```

- [ ] **Step 2: Escrever os testes de comportamento**

Criar `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O comportamento de cada rota da Fase 3 pelo HTTP: o `TipoDeErro` virando o status, o corpo
/// `{ erro, mensagem }` inteiro, e o 403 do caso de uso (estorno alheio) distinto do 403 de perfil.
/// </summary>
public partial class ExecucaoEndpointsTests
{
  [Fact]
  public async Task Iniciar_devolve_201_com_o_movimento_e_poe_o_Pedido_em_producao()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 8m });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("Inicio", corpo.GetProperty("tipo").GetString());
    Assert.Equal("NoSetor", corpo.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(c.Corte, corpo.GetProperty("destino").GetProperty("setorId").GetInt32());
    Assert.Equal(1, corpo.GetProperty("destino").GetProperty("ordem").GetInt32());
    Assert.Equal(c.Operador.Id, corpo.GetProperty("usuarioId").GetInt32());
    Assert.False(corpo.GetProperty("estornada").GetBoolean());
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal("EmProducao", (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == c.PedidoId)).Status);
  }

  [Fact]
  public async Task Saldo_insuficiente_devolve_409_com_codigo_e_mensagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 21m });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("SaldoInsuficiente", corpo.GetProperty("erro").GetString());
    Assert.Contains("Só há 20 de Suporte", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Quantidade_invalida_devolve_400_com_codigo()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 0.00005m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("QuantidadeInvalida", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task No_inexistente_devolve_404()
  {
    // So o status: `NotFound()` num `[ApiController]` pode ganhar corpo ProblemDetails do ASP.NET, e o
    // contrato desta fase nao promete corpo no 404 — promete que nao ha `erro` para o front comutar.
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync("/api/estrutura/999999/inicios", new { setorId = c.Corte, quantidade = 1m });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Estorno_alheio_devolve_403_Proibido_com_corpo_e_o_PCP_estorna()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var inicio = await CorpoAsync(await c.Como(c.Operador).PostAsJsonAsync(
        $"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 5m }));
    var id = inicio.GetProperty("id").GetInt32();

    var alheio = await c.Como(c.Movimentador).PostAsync($"/api/movimentacoes/{id}/estorno", null);
    var doPcp = await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{id}/estorno", null);

    Assert.Equal(HttpStatusCode.Forbidden, alheio.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(alheio)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Created, doPcp.StatusCode);
    Assert.Equal(id, (await CorpoAsync(doPcp)).GetProperty("estornoDeId").GetInt32());
  }

  [Fact]
  public async Task Entrega_em_lista_devolve_201_e_as_tarefas_acompanham()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 20m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 20m }));

    var antes = await TarefasDoCenarioAsync(c);
    var entrega = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 12m },
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Pintura, quantidade = 8m },
      },
    });
    var depois = await TarefasDoCenarioAsync(c);

    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    Assert.Equal(2, (await CorpoAsync(entrega)).GetArrayLength());
    var tarefa = Assert.Single(antes);
    Assert.Equal("Montagem", tarefa.GetProperty("destino").GetProperty("tipo").GetString());
    Assert.Equal(c.Solda, tarefa.GetProperty("destino").GetProperty("sugestaoSetorId").GetInt32());
    Assert.Empty(depois);
  }

  [Fact]
  public async Task Montar_e_estornar_a_montagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 6m);
    await LevarParaAMontagemAsync(c, c.C, c.Dobra, 3m);

    var montagem = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/montagens", new { setorId = c.Solda, quantidade = 3m });
    var montagemId = (await CorpoAsync(montagem)).GetProperty("id").GetInt32();
    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila"));
    var estorno = await c.Como(c.Operador).PostAsync($"/api/montagens/{montagemId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, montagem.StatusCode);
    Assert.Equal(2, (await CorpoAsync(montagem)).GetProperty("baixas").GetArrayLength());
    Assert.Empty(fila.GetProperty("aguardandoMontagem").EnumerateArray());   // montou tudo o que havia
    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    Assert.Equal(2, (await CorpoAsync(estorno)).GetArrayLength());
  }

  [Fact]
  public async Task Roteiro_marca_o_alcancado_e_recusa_mexer_nele()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 1m }));

    var lido = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/estrutura/{c.A}/roteiro"));
    var recusa = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Corte, c.Pintura } });
    var aceito = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Solda, c.Dobra, c.Pintura } });

    Assert.Equal(new[] { true, false },
        lido.GetProperty("passos").EnumerateArray().Select(p => p.GetProperty("alcancado").GetBoolean()).ToArray());
    Assert.Equal(HttpStatusCode.Conflict, recusa.StatusCode);
    Assert.Equal("PassoJaAlcancado", (await CorpoAsync(recusa)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.OK, aceito.StatusCode);
    Assert.Equal(3, (await CorpoAsync(aceito)).GetProperty("passos").GetArrayLength());
  }

  [Fact]
  public async Task Reduzir_a_quantidade_abaixo_do_que_andou_devolve_409_com_mensagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 15m }));

    var resposta = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.B}",
        new { descricao = "Suporte", quantidade = 10m, quantidadePorPai = 2m });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("QuantidadeAbaixoDoMovimentado", corpo.GetProperty("erro").GetString());
    Assert.Contains("15", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Excluir_no_de_Pedido_em_producao_continua_PedidoNaoAberto()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m }));

    var resposta = await c.Como(c.Pcp).DeleteAsync($"/api/estrutura/{c.C}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    Assert.Equal("PedidoNaoAberto", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Gestao_le_fila_posicoes_livro_e_roteiro()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var gestao = c.Como(c.Gestao);

    foreach (var rota in new[]
    {
      $"/api/setores/{c.Corte}/fila", $"/api/agrupamentos/{c.AgrupamentoId}/posicoes",
      $"/api/estrutura/{c.B}/movimentacoes", $"/api/estrutura/{c.B}/roteiro",
    })
      Assert.Equal(HttpStatusCode.OK, (await gestao.GetAsync(rota)).StatusCode);
  }

  /// <summary>Iniciar, terminar e entregar para a montagem de A na Solda — o arranjo de montar.</summary>
  private static async Task LevarParaAMontagemAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade },
      },
    }));
  }

  /// <summary>As tarefas SO deste cenario: `GET /tarefas` e global, e o banco de dev e compartilhado.</summary>
  private static async Task<List<JsonElement>> TarefasDoCenarioAsync(CenarioDaFase3NaApi c)
  {
    var nos = new[] { c.A, c.B, c.C };
    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));
    return tarefas.EnumerateArray()
        .SelectMany(s => s.GetProperty("itens").EnumerateArray())
        .Where(i => nos.Contains(i.GetProperty("no").GetProperty("id").GetInt32()))
        .ToList();
  }
}
```

`ExecucaoEndpointsTests` não declara `IAsyncLifetime`: cada teste cria e descarta o próprio cenário, e a
parte da Task 10 não precisa de banco.

- [ ] **Step 3: O critério de pronto**

Criar `tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O critério de pronto da Fase 3 (`specs/06-roadmap-mvp.md` e spec secao 9.3), ponta a ponta: um Pedido
/// Avulso A &lt;- B, C percorrido inteiro — iniciar, terminar, entregar, montar, entregar a Peca no local de
/// expedicao —, com `GET /agrupamentos/{id}/posicoes` dizendo, a cada passo, onde esta cada peca e se
/// ela aguarda coleta, e `GET /tarefas` dizendo o que o Movimentador tem a levar.
/// </summary>
public class CriterioDeProntoDaFase3Tests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public CriterioDeProntoDaFase3Tests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Pedido_Avulso_A_com_B_e_C_percorrido_do_inicio_a_expedicao()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    var movimentador = c.Como(c.Movimentador);

    // Tudo nasce a iniciar (regra 28).
    await AfirmarPosicoesAsync(c, c.A, ("AIniciar", null, null, 10m));
    await AfirmarPosicoesAsync(c, c.B, ("AIniciar", null, null, 20m));

    // B e C: iniciar e terminar o unico passo — ficam aguardando coleta no Setor onde terminaram.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 20m }));
    await AfirmarPosicoesAsync(c, c.B, ("NoSetor", c.Corte, 1, 20m));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 20m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.C}/inicios", new { setorId = c.Dobra, quantidade = 10m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.C}/terminos", new { setorId = c.Dobra, ordem = 1, quantidade = 10m }));
    await AfirmarPosicoesAsync(c, c.B, ("AguardandoColeta", c.Corte, 1, 20m));
    await AfirmarPosicoesAsync(c, c.C, ("AguardandoColeta", c.Dobra, 1, 10m));

    // O Movimentador ve as duas como Item pronto, com a montagem de A como destino.
    var tarefas = await TarefasAsync(c);
    Assert.Equal(new[] { c.B, c.C }, tarefas.Select(t => t.No).Order().ToArray());
    Assert.All(tarefas, t => Assert.Equal("Montagem", t.Destino));

    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 20m },
        new { estruturaItemId = c.C, origem = new { posicao = "AguardandoColeta", setorId = c.Dobra, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 10m },
      },
    }));
    await AfirmarPosicoesAsync(c, c.B, ("AguardandoMontagem", c.Solda, null, 20m));
    Assert.Empty(await TarefasAsync(c));

    // A entra na Solda e e montada com os dois filhos.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 10m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/montagens", new { setorId = c.Solda, quantidade = 10m }));
    await AfirmarPosicoesAsync(c, c.B, ("Montado", null, null, 20m));
    await AfirmarPosicoesAsync(c, c.C, ("Montado", null, null, 10m));
    Assert.Equal(10m, (await PosicoesAsync(c)).Single(p => p.GetProperty("estruturaItemId").GetInt32() == c.A)
        .GetProperty("totalMontado").GetDecimal());

    // A termina a Solda, vai para a Pintura, termina, e vai ao local de expedicao.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/terminos", new { setorId = c.Solda, ordem = 1, quantidade = 10m }));
    Assert.Equal("ProximoPasso", Assert.Single(await TarefasAsync(c)).Destino);
    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = c.A, origem = new { posicao = "AguardandoColeta", setorId = c.Solda, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade = 10m } },
    }));
    await AfirmarPosicoesAsync(c, c.A, ("NoSetor", c.Pintura, 2, 10m));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/terminos", new { setorId = c.Pintura, ordem = 2, quantidade = 10m }));
    Assert.Equal("Expedicao", Assert.Single(await TarefasAsync(c)).Destino);
    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = c.A, origem = new { posicao = "AguardandoColeta", setorId = c.Pintura, ordem = (int?)2 }, destinoSetorId = (int?)null, quantidade = 10m } },
    }));

    await AfirmarPosicoesAsync(c, c.A, ("NaExpedicao", null, null, 10m));
    Assert.Empty(await TarefasAsync(c));
  }

  private static async Task<List<JsonElement>> PosicoesAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
          .EnumerateArray().ToList();

  /// <summary>O no esta, inteiro, exatamente nas posicoes dadas — nem uma unidade em outro lugar.</summary>
  private static async Task AfirmarPosicoesAsync(
      CenarioDaFase3NaApi c, int no, params (string Posicao, int? SetorId, int? Ordem, decimal Quantidade)[] esperado)
  {
    var saldos = (await PosicoesAsync(c)).Single(p => p.GetProperty("estruturaItemId").GetInt32() == no)
        .GetProperty("saldos").EnumerateArray()
        .Select(s => (
            s.GetProperty("posicao").GetString()!,
            s.GetProperty("setorId").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("setorId").GetInt32(),
            s.GetProperty("ordem").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("ordem").GetInt32(),
            s.GetProperty("quantidade").GetDecimal()))
        .ToArray();
    Assert.Equal(esperado, saldos);
  }

  private static async Task<List<(int No, string Destino)>> TarefasAsync(CenarioDaFase3NaApi c)
  {
    var nos = new[] { c.A, c.B, c.C };
    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));
    return tarefas.EnumerateArray()
        .SelectMany(s => s.GetProperty("itens").EnumerateArray())
        .Select(i => (No: i.GetProperty("no").GetProperty("id").GetInt32(), Destino: i.GetProperty("destino").GetProperty("tipo").GetString()!))
        .Where(t => nos.Contains(t.No))
        .ToList();
  }
}
```

- [ ] **Step 4: Rodar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ExecucaoEndpointsTests|FullyQualifiedName~CriterioDeProntoDaFase3Tests"`
Expected: PASS (29 de `ExecucaoEndpointsTests` — os 18 da Task 10 e 11 daqui —, e 1 de critério de pronto).

- [ ] **Step 5: Suíte inteira, três vezes**

Run: `dotnet build Rastreamento.slnx -warnaserror && dotnet test Rastreamento.slnx` — **três vezes seguidas**.
Expected: 0 warnings; tudo PASS nas três. Os testes desta task escrevem em tabelas compartilhadas e rodam em
paralelo com as outras classes: um vermelho intermitente aqui é o flaky da regra do `CLAUDE.md`
("asserção sobre contagem global de tabela compartilhada é flaky por construção"), e o conserto é escopar a
asserção, nunca repetir até passar. Registre as três contagens no relatório.

- [ ] **Step 6: Commit**

```bash
git add tests/Rastreamento.Api.Tests/CenarioDaFase3NaApi.cs \
  tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs \
  tests/Rastreamento.Api.Tests/CriterioDeProntoDaFase3Tests.cs
git commit -m "test(api): comportamento das rotas da execucao e o criterio de pronto da Fase 3"
```

---

## Depois das tasks

- **Revisão final de branch**, no modelo mais capaz, sobre a branch inteira (`main..HEAD`), com o pacote de
  `scripts/review-package` da skill — ver o `CLAUDE.md`, "Como este projeto executa plano".
- **Não entra aqui, e fica para o plano 3 ou para a verificação manual:** as telas (Fila, Tarefas, árvore
  com pílulas, histórico, editor de Roteiro) e a verificação no navegador da spec §9.5, com as contas
  `operador` e `movimentador` criadas à mão (fora do seed, pelo mesmo motivo do `operador` que o
  `CLAUDE.md` descreve).
- **Contagem de testes do `CLAUDE.md`** ("A suíte tem 464 testes", medida em 2026-08-26): não é atualizada
  por este plano. Se alguém a atualizar, remede com `dotnet test Rastreamento.slnx --list-tests` e diga a
  data, como o próprio parágrafo pede.
