# Filtros da demanda — Plano 1 (Filtros) — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Filtrar a demanda por Material e por Pedido na fila do Setor e nas Tarefas, com um componente de filtro reutilizável, e transformar a lista de Pedidos em lista paginada com busca e filtro — com a Home lendo um resumo do servidor e o status do Pedido em português.

**Architecture:** Só leitura; nada toca o livro. No backend, o `NoResumoDto` ganha `PedidoCliente` e `Materiais` (carregados em lote junto dos nós em produção), e `GET /pedidos` vira paginado no molde de `GET /componentes`, com duas rotas novas (`/pedidos/resumo`, `/pedidos/materiais`). No front, a primitiva `FiltroDeDemanda` é controlada; a seleção mora na URL por um hook (`useSelecaoNaUrl`); fila e Tarefas filtram no cliente com funções puras; Pedidos filtra no servidor por `useBuscaPaginada`, que ganha filtros extras e estado inicial vindo da URL.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), React Router, Vitest + Testing Library + jsdom.

**Spec:** `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md` (aprovada em 2026-09-29) — este plano cobre **só o Plano 1** da seção 9 dela. O Plano 2 (lote: `POST /inicios`, `POST /terminos`, seleção na fila, "Marcar todos") é outro plano, escrito depois deste.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 7**, verificação manual, com a justificativa escrita **antes** no ledger e no relatório (o produto é relatório, não código).
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com `scripts/review-package BASE HEAD` — nunca `HEAD~1`.
- **Sem mudança de schema** (spec, seção 1): `specs/02-modelo-de-dados.sql` não muda e não há `db/alter-*.sql`. `dbo.EstruturaMaterial` já existe e já é gravado na criação do nó.
- Material do filtro é o **do nó** (`dbo.EstruturaMaterial`), nunca o do catálogo (`ComponenteMaterialPadrao`) — spec, seção 2.3.
- Regra de casamento, em todo lugar: **OU dentro da faceta, E entre facetas, faceta vazia não restringe** (spec, seção 2.5). No cliente, só `casaComFiltro` a implementa; nenhuma página a reescreve.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês (`CLAUDE.md`, "Convenções de nomenclatura").
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa, em código e em prosa (`CLAUDE.md`, "Convenção de citação"). Comentário de código **não cita** ledger, brief nem "Task N do plano": o repositório é público.
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`. Teste de banco novo escopa a asserção nas linhas que ele mesmo criou (prefixo, ou cliente único) — contagem global de tabela compartilhada é flaky por construção (`CLAUDE.md`); o único "global" aceito é o monótono (`>= n` das linhas do próprio teste).
- Front: `npm test -- --run` **e** `npm run build`. Telas: `<Pagina>`, primitivas de `web/src/components/` (nada de `<button>` cru fora da exceção de chrome — a pílula removível do filtro é `Botao variante="secundario"`), cores só por token, três estados com teste, `// @vitest-environment jsdom` + `afterEach(cleanup)`, mocks por `web/src/testes/api.ts`.
- **Cor de estado nunca decora:** pílula de filtro ativo e contagem de opção usam o tom neutro — nunca `positivo`, `negativo` ou `atencao` (spec, seção 3.6).
- Perfis: as três rotas novas são **leitura de qualquer autenticado** (`[Authorize]` sem `Roles`), como `GET /pedidos` hoje. Nenhuma linha nova em `web/src/auth/permissoes.ts`.
- `git pull` antes de todo commit, nos dois repositórios (código e ledger). No ledger, `git add` por caminho explícito, em chamada separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
bash scripts/backend-na-nuvem          # na nuvem: SDK, dockerd, SQL Server, banco com schema e seed (idempotente)
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm test -- --run && npm run build
```

**Baseline, medida em 2026-09-29 no commit `fba9356` (base deste plano), na nuvem:** backend **856** (Application 443 · Infrastructure 132 · Api 281), build 0 warnings; front **869 testes / 59 arquivos**, build ok. Na primeira execução da suíte do backend, **1** teste de `Api.Tests` falhou; o nome **não** foi capturado (o filtro da saída o descartou). Não se repetiu: `Api.Tests` sozinho deu 281/281, e duas execuções completas seguintes com `-m:1` deram 856/856. Quem vir uma falha no `Api.Tests` durante este plano registra o **nome** antes de atribuí-la. Este plano **não** dá totais absolutos por task — cada implementer mede o delta da própria task e o escreve no relatório.

## Contrato JSON novo (fonte para o front)

Só o que muda. camelCase, como o ASP.NET serializa.

```jsonc
// NoResumo (fila e Tarefas) ganha dois campos — aditivo
{ "...": "...", "pedidoNumero": "1042", "pedidoCliente": "Metalúrgica Alfa",
  "materiais": [ { "id": 3, "codigo": "CH-300", "descricao": "Chapa SAE 1020 3,00 mm" } ] }   // [] quando não há; ordem por código

// GET /pedidos?busca=&status=Aberto,EmProducao&material=3,5&pagina=1&tamanho=20
{ "itens": [ PedidoDto ], "total": 57, "pagina": 1, "tamanho": 20 }
// status e material: listas separadas por vírgula; pedaço vazio ignorado, repetido colapsado.
// 400 { "erro": "..." } para: pagina < 1, tamanho < 1, tamanho > 100, status fora dos cinco, material que não é inteiro positivo.

// GET /pedidos/resumo
{ "porStatus": [ { "status": "Aberto", "quantidade": 12 }, { "status": "EmProducao", "quantidade": 3 },
                 { "status": "AguardandoExpedicao", "quantidade": 0 }, { "status": "Concluido", "quantidade": 40 },
                 { "status": "Cancelado", "quantidade": 2 } ],        // SEMPRE os cinco, na ordem do CK_Pedido_Status, zeros inclusive
  "maisAntigosAbertos": [ PedidoDto ] }                                 // até 5, fora de Concluido/Cancelado, por DataAbertura crescente

// GET /pedidos/materiais
[ { "id": 3, "codigo": "CH-300", "descricao": "Chapa SAE 1020 3,00 mm" } ]   // os que aparecem em algum nó; ativos ou não; ordem por descrição, depois código
```

## Decisões deste plano (onde a spec deixou a escolha)

- **D1 — Ordem total em `GET /pedidos`.** A ordem de hoje (`DataAbertura` decrescente) não é total, e `Skip/Take` sem ordem total repete e pula linhas entre páginas (o motivo registrado em `ComponenteRepository.ListarAsync`). O plano acrescenta o desempate por `Id` decrescente. A ordem vista pelo usuário não muda.
- **D2 — Contagem das opções na fila e nas Tarefas** (spec, seção 4.2: "quantas linhas ou cartões visíveis casariam com ela"). O plano lê "visíveis" como **sob a seleção das OUTRAS facetas**: a contagem de "Pedido 1042" com "Material 3 mm" marcado é quantas linhas do 1042 têm 3 mm. É o que faz a contagem cumprir a seção 2.4 ("nunca escolhe uma opção que dá lista vazia" — a opção que daria vazio mostra 0). A **lista** de opções continua sendo o que existe na tela sem filtro nenhum, para ela não pular enquanto o operador marca.
- **D3 — Onde as duas metades da troca de guarda da 1E nascem** (spec, seção 7.6). A metade do backend (o resumo conta além do tamanho de página) nasce na Task 2, junto do resumo; a do front (a Home lê o resumo, não uma lista) nasce na Task 4, **no mesmo commit** que remove o teste antigo *"devolve o conjunto inteiro de pedidos, nao uma pagina — a HomePage depende disso"*. O teste antigo não pode sair antes: até a Task 4 a Home ainda lê a lista.
- **D4 — A URL é lida na montagem e escrita a cada mudança.** A seleção de fila e Tarefas mora só na URL (`useSelecaoNaUrl`). Em Pedidos, busca e página moram no estado do `useBuscaPaginada` e são **copiadas** para a URL; a URL é lida uma vez, na montagem (sobrevive ao F5 e ao "voltar" de um detalhe, que remontam a página). Escrever na URL usa `replace` — marcar uma caixa não cria entrada nova no histórico.
- **D5 — Valor marcado que a tela nunca viu** (F5 com `?pedido=42` depois de o Pedido 42 sair da fila). A seção 3.4 da spec manda manter a opção marcada e visível com 0; o rótulo dela não existe mais em lugar nenhum. O componente lembra o rótulo de toda opção que já viu; a nunca vista aparece como **"Não está mais na lista"**, marcada, com 0, removível.
- **D6 — O filtro não é "outra pessoa agiu".** A fila e as Tarefas continuam calculando "o item saiu da lista" (o aviso de `SAIU_DA_FILA` / `SAIU_DA_LISTA`) sobre a resposta **sem** filtro. Esconder uma linha pelo filtro não fecha formulário aberto com aviso, nem tira item da seleção das Tarefas.
- **D7 — Nas Tarefas, o convívio de filtro e seleção vale já neste plano** (spec, seção 4.5), porque o "Levar" das Tarefas já é lote: o item marcado e oculto continua na entrega, e o botão avisa quantos estão ocultos.
- **D8 — Pedidos sanitiza a URL antes de chamar a API.** Um `?material=abc` ou `?status=Qualquer` colado à mão não vira 400 na tela: a página só manda ao servidor os valores válidos (inteiro positivo; um dos cinco status). O valor inválido continua na seleção e aparece como opção ausente (D5), removível. O 400 do backend continua sendo a fronteira para quem chama a API direto.

## Review Focus

Condições que a spec implica e que um usuário vai encontrar, sem que um teste "óbvio" da spec as exercite. Cada uma tem teste na task dona.

1. **Busca de Pedidos com curinga de `LIKE`** (`%`, `_`, `[`): o usuário que digita "CH_2150" ou "50%" espera o texto literal, não "tudo". Teste na Task 2 (`Busca_trata_curinga_de_LIKE_como_texto`).
2. **URL colada à mão com lixo** (`?material=abc,,5`, `?pagina=0`, `?pagina=99`): a tela não quebra, ignora pedaço vazio, recua para a última página existente, e o valor inválido fica visível e removível. Testes na Task 3 (`useSelecaoNaUrl`) e na Task 4 (`PedidosPage`).
3. **F5 com um valor marcado que a lista nunca teve** (D5): a opção aparece marcada, com 0, e dá para removê-la. Teste na Task 3.
4. **"Pausados" dentro de "A iniciar aqui" sob filtro**: o título "Pausados" some quando o filtro esconde todos os pausados, e as linhas pausadas que casam continuam no subgrupo. Teste na Task 5.
5. **Busca dentro da faceta com caixa e acento diferentes** ("chapa" acha "Chapa"; "solda" acha "Sôlda"): o operador digita no celular sem acento. Teste na Task 3.

---

### Task 1: O `NoResumoDto` ganha o cliente do Pedido e os Materiais do nó (backend)

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (`ContextoDoNo`, record novo `MaterialDoNo`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs` (`ListarNosEmProducaoAsync`)
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (record novo `MaterialResumoDto`)
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`NoResumoDto`)
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (`CarregarEmProducaoAsync`)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`, `CenarioDeExecucao.cs`
- Test: `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ExecucaoRepositoryTests.cs`, `ArvoreDeTesteNoBanco.cs`
- Test: `tests/Rastreamento.Api.Tests/ExecucaoEndpointsTests.Comportamento.cs`

**Interfaces:**
- Produces (Domain): `record MaterialDoNo(int Id, string Codigo, string Descricao)`; `ContextoDoNo(EstruturaItem No, int PedidoId, string PedidoNumero, string PedidoCliente, int AgrupamentoId, string AgrupamentoCodigo, PausaAberta? Pausa, IReadOnlyList<MaterialDoNo> Materiais)`.
- Produces (Application): `record MaterialResumoDto(int Id, string Codigo, string Descricao)` em `Rastreamento.Application.Cadastros` (a Task 2 o reusa em `GET /pedidos/materiais`); `NoResumoDto(int Id, string Descricao, string? CodigoDoComponente, int PedidoId, string PedidoNumero, string PedidoCliente, int AgrupamentoId, string AgrupamentoCodigo, int? PaiId, string? PaiDescricao, PausaResumoDto? Pausa, IReadOnlyList<MaterialResumoDto> Materiais)`.
- Produces (teste): `ArvoreDeTesteNoBanco.NovoMaterialAsync(db, string descricao) : Task<int>` e `ArvoreDeTesteNoBanco.MaterialNoNoAsync(db, int noId, int materialId) : Task` — a limpeza apaga os Materiais criados, **depois** de `dbo.EstruturaMaterial`. A Task 2 os reusa.

- [ ] **Step 1: Testes que falham (Application).** Em `ConsultaDeExecucaoUseCaseTests`, com o `CenarioDeExecucao` ganhando `Execucao.ClienteDoPedido[PedidoId] = "Cliente do cenário"` e um catálogo de materiais no fake (`Execucao.CatalogoDeMateriais[id] = (codigo, descricao)`, lido junto de `Estruturas.Materiais`, a lista de `EstruturaMaterial` que o `FakeEstruturaRepo` já tem):
  - `Fila_traz_o_cliente_do_Pedido_e_os_materiais_do_no_ordenados_por_codigo` — nó com dois `EstruturaMaterial` (códigos "CH-600" e "CH-300"); a linha da fila traz `PedidoCliente == "Cliente do cenário"` e `Materiais` igual a `[MaterialResumoDto(id300, "CH-300", …), MaterialResumoDto(id600, "CH-600", …)]`, nessa ordem.
  - `No_sem_material_traz_lista_vazia_e_nao_nula` — `Assert.Empty(linha.No.Materiais)`.
  - `Tarefas_trazem_os_materiais_do_no` — o `TarefaDto.No.Materiais` do item pronto traz o material.
  - `Cartao_de_montagem_traz_os_materiais_do_pai_e_de_cada_filho` — `MontagemPendenteDto.Pai.Materiais` e `Filhos[i].No.Materiais` vêm cada um do próprio nó.

- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~ConsultaDeExecucaoUseCaseTests"` → não compila (`PedidoCliente`/`Materiais` inexistentes).

- [ ] **Step 3: Implementar.** `ContextoDoNo` e `NoResumoDto` como nas Interfaces. `ListarNosEmProducaoAsync` seleciona `p.Cliente` e carrega os materiais de **todos** os nós da resposta numa consulta só (junção `EstruturaMateriais` × `Materiais` com `ids.Contains(em.EstruturaItemId)`, no mesmo formato em que `PausasAbertas.ListarAsync` já carrega as pausas), agrupados por nó e ordenados por `Codigo`; nó sem material recebe `[]`. `CarregarEmProducaoAsync` projeta `MaterialDoNo` em `MaterialResumoDto`. O fake devolve o mesmo formato.

- [ ] **Step 4: Rodar e ver passar.** Mesmo comando → PASS; e a suíte de Application inteira verde.

- [ ] **Step 5: Testes de banco que falham.** Em `ExecucaoRepositoryTests`:
  - `Nos_em_producao_trazem_o_cliente_e_os_materiais_do_proprio_no` — árvore do `ArvoreDeTesteNoBanco` com dois materiais num nó e nenhum noutro; asserção escopada nos Ids da árvore: cliente `"Cliente de teste"`, materiais do primeiro nó por código, `[]` no segundo.
  - `Materiais_dos_nos_em_producao_saem_numa_consulta_so` — contexto construído com um `DbCommandInterceptor` de teste que conta comandos executados; chama `ListarNosEmProducaoAsync` com a árvore de 3 nós com material, anota a contagem, acrescenta mais 3 nós com material e chama de novo: **a contagem de comandos é a mesma** (não cresce com o número de nós). A contagem não depende do conteúdo do banco inteiro, então não sofre do flaky de contagem global.

- [ ] **Step 6: Rodar, implementar o que faltar, e ver passar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~ExecucaoRepositoryTests"` → PASS.

- [ ] **Step 7: Contrato na API.** Em `ExecucaoEndpointsTests.Comportamento.cs`, `Fila_serializa_pedidoCliente_e_materiais_em_camelCase`: `GET /api/setores/{id}/fila` no `CenarioDaFase3NaApi` — o `no` de uma linha tem a propriedade `pedidoCliente` (string) e `materiais` (array, vazio nos nós ad-hoc do cenário). Rodar → PASS.

- [ ] **Step 8: Suíte e build.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` → verde; anotar o delta no relatório.

- [ ] **Step 9: Commit.**

```bash
git add src/ tests/
git commit -m "feat(execucao): NoResumoDto traz o cliente do Pedido e os Materiais do no"
```

---

### Task 2: `GET /pedidos` paginado com busca e filtro, `GET /pedidos/resumo` e `GET /pedidos/materiais` (backend)

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs` (record novo `FiltroDePedidos`; `ListarAsync` troca de assinatura; métodos novos)
- Modify: `src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs`
- Modify: `src/Rastreamento.Application/Cadastros/Dtos.cs` (`ResumoDePedidosDto`, `ContagemDeStatusDto`)
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`
- Modify: `src/Rastreamento.Api/Controllers/PedidosController.cs`
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs` (`FakePedidoRepo`)
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDePedidoUseCaseTests.cs`
- Test (novo): `tests/Rastreamento.Infrastructure.Tests/Persistence/PedidoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/PedidosEndpointsTests.cs`

**Interfaces:**
- Consumes: `MaterialResumoDto` e os helpers de material do `ArvoreDeTesteNoBanco` (Task 1).
- Produces (Domain): `record FiltroDePedidos(string? Busca, IReadOnlyList<string> Status, IReadOnlyList<int> Materiais, int Pagina, int Tamanho)`; em `IPedidoRepository`: `Task<(IReadOnlyList<Pedido> Itens, int Total)> ListarAsync(FiltroDePedidos filtro, CancellationToken ct)` (substitui o `ListarAsync(ct)` sem filtro — o único chamador é o caso de uso), `Task<IReadOnlyDictionary<string, int>> ContarPorStatusAsync(CancellationToken ct)`, `Task<IReadOnlyList<Pedido>> ListarMaisAntigosAsync(IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct)`, `Task<IReadOnlyList<Material>> ListarMateriaisEmUsoAsync(CancellationToken ct)`.
- Produces (Application): `CadastroDePedidoUseCase.TamanhoDePaginaPadrao = 20`, `TamanhoDePaginaMaximo = 100`; `Task<Result<PaginaDto<PedidoDto>>> Listar(string? busca, string? status, string? material, int pagina, int tamanho, CancellationToken ct)`; `Task<ResumoDePedidosDto> Resumo(CancellationToken ct)`; `Task<IReadOnlyList<MaterialResumoDto>> MateriaisEmUso(CancellationToken ct)`; `record ContagemDeStatusDto(string Status, int Quantidade)`; `record ResumoDePedidosDto(IReadOnlyList<ContagemDeStatusDto> PorStatus, IReadOnlyList<PedidoDto> MaisAntigosAbertos)`.
- Produces (HTTP): o contrato JSON do cabeçalho — `GET /pedidos`, `GET /pedidos/resumo`, `GET /pedidos/materiais`, todos `[Authorize]` sem `Roles`.

Valores que a spec e o DDL fixam, para o caso de uso: status válidos, **na ordem do `CK_Pedido_Status`**, `Aberto`, `EmProducao`, `AguardandoExpedicao`, `Concluido`, `Cancelado`; encerrados = `Concluido`, `Cancelado`; `QUANTOS_MAIS_ANTIGOS = 5`.

- [ ] **Step 1: Testes que falham (Application).** O `FakePedidoRepo` guarda o último `FiltroDePedidos` recebido (`UltimoFiltro`) e aplica o filtro em memória só no que o caso de uso precisa provar (faixa e total). Em `CadastroDePedidoUseCaseTests`:
  - `Listar_devolve_a_pagina_com_o_total_do_filtro` — 3 Pedidos, `tamanho: 2` → `Itens.Count == 2`, `Total == 3`, `Pagina == 1`, `Tamanho == 2`.
  - `Listar_recusa_faixa_invalida` — `[Theory]` com `(0, 20)`, `(1, 0)`, `(1, 101)` → `TipoDeErro.Validacao`, `repo.UltimoFiltro` nulo.
  - `Listar_recusa_status_desconhecido_nomeando_o_valor` — `status: "Aberto,Qualquer"` → `Validacao`, `Erro` contém `"Qualquer"`.
  - `Listar_recusa_material_que_nao_e_inteiro_positivo` — `[Theory]` `"abc"`, `"0"`, `"-3"`, `"1.5"` → `Validacao`.
  - `Listar_ignora_pedaco_vazio_e_colapsa_repetido` — `status: " Aberto,,Aberto ,EmProducao"`, `material: "5,,5,3"` → `UltimoFiltro.Status == ["Aberto", "EmProducao"]`, `UltimoFiltro.Materiais == [5, 3]`; `busca: "  CH  "` chega como `"CH"`; busca só de espaços chega como `null`.
  - `Listar_traz_a_pausa_aberta_do_Pedido_pausado_e_nulo_nos_demais` — o teste existente, adaptado à assinatura nova (lê `resultado.Valor!.Itens`).
  - `Resumo_traz_os_cinco_status_na_ordem_do_DDL_com_zero_no_que_falta` — o fake devolve `{ Aberto: 2, Concluido: 1 }` → `PorStatus` com os cinco, nessa ordem, `EmProducao`/`AguardandoExpedicao`/`Cancelado` com 0.
  - `Resumo_pede_os_mais_antigos_fora_dos_encerrados_e_no_maximo_cinco` — o fake registra os argumentos: `foraDosStatus` = `{Concluido, Cancelado}`, `quantos == 5`; os `PedidoDto` voltam com a pausa aberta projetada.
  - `MateriaisEmUso_projeta_id_codigo_e_descricao`.

- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CadastroDePedidoUseCaseTests"` → não compila.

- [ ] **Step 3: Implementar o caso de uso e o fake.** Molde de `CadastroDeComponenteUseCase.Listar` (faixa inválida é 400; página além do fim é sucesso com itens vazios). Mensagens de erro em português com acento-livre como as vizinhas do arquivo; a de status diz o valor recusado e lista os cinco aceitos.

- [ ] **Step 4: Rodar e ver passar.** Mesmo comando → PASS.

- [ ] **Step 5: Testes de banco que falham** (`PedidoRepositoryTests`, `: TesteComBanco`). Cada teste cria os próprios Pedidos com um **cliente único** (`$"cli-{Guid.NewGuid():N}"`) e o usa como `Busca` — ou, onde a busca é o objeto do teste, afirma só sobre os Ids que criou. Limpeza em `finally`, na ordem das FKs (`ArvoreDeTesteNoBanco.LimparAsync` para os que têm árvore).
  - `Busca_acha_pelo_numero_pelo_cliente_e_pelo_codigo_do_Componente_de_um_no_filho` — três Pedidos: um casa pelo número, um pelo cliente, e um só porque um nó **filho** (não a Peça) tem Componente cujo código contém o texto; um quarto, sem nada disso, fica de fora.
  - `Busca_trata_curinga_de_LIKE_como_texto` — Pedido com número contendo `"50%"` e outro com `"50x"`: buscar `"50%"` acha só o primeiro; buscar `"_"` não acha Pedido sem sublinhado. *(Review Focus 1.)*
  - `Status_combina_com_OU` — Pedidos `Aberto`, `EmProducao`, `Cancelado` do mesmo cliente único; `Status = [Aberto, Cancelado]` → os dois.
  - `Material_casa_se_algum_no_do_Pedido_tem_o_material` — o material está só num nó filho; `Materiais = [m]` acha o Pedido; `Materiais = [outro]` não.
  - `Parametros_combinam_com_E` — busca + status + material juntos restringem à interseção.
  - `Total_e_do_filtro_e_a_ordem_e_abertura_decrescente_com_desempate_por_Id` — três Pedidos, dois com a **mesma** `DataAbertura`; `Tamanho = 2`: página 1 e página 2 juntas trazem os três, sem repetição, na ordem `DataAbertura` desc, `Id` desc (D1); `Total == 3` nas duas.
  - `Mais_antigos_deixa_encerrados_de_fora_e_para_no_limite` — seis Pedidos abertos com `DataAbertura` em 1990 (mais antigos que qualquer outro do banco de dev) e um `Concluido` ainda mais antigo; `ListarMaisAntigosAsync({Concluido, Cancelado}, 5)` devolve cinco dos seis, do mais antigo ao mais novo, sem o concluído.
  - `Materiais_em_uso_trazem_o_que_aparece_em_algum_no_e_nao_o_que_nao_aparece` — dois Materiais novos, um ligado a um nó e outro não: o primeiro está na lista, o segundo não (asserção só sobre os dois Ids).

- [ ] **Step 6: Implementar o repositório e ver passar.** Filtro montado no `IQueryable`, no molde de `ComponenteRepository.ListarAsync` (contagem antes do `Skip/Take`, com o mesmo filtro; `Trim` já veio do caso de uso). Busca por nó: existe `EstruturaItem` de algum `Agrupamento` do Pedido cujo `ComponenteId` aponta para `Componente` com `Codigo` contendo o texto. Material: existe `EstruturaMaterial` com `MaterialId` na lista, de nó de algum `Agrupamento` do Pedido. `ContarPorStatusAsync` com `GroupBy` no servidor. `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~PedidoRepositoryTests"` → PASS.

- [ ] **Step 7: Controller e testes de API.** `PedidosController.Listar` recebe `[FromQuery] string? busca = null, string? status = null, string? material = null, int pagina = 1, int tamanho = CadastroDePedidoUseCase.TamanhoDePaginaPadrao` e devolve `Ok(valor)` ou `BadRequest(new { erro })`; `[HttpGet("resumo")]` e `[HttpGet("materiais")]` antes de `{id:int}` não colidem (a restrição `int` já separa). Em `PedidosEndpointsTests`:
  - `Lista_paginada_responde_o_envelope_com_o_total_do_filtro` — três Pedidos de cliente único; `?busca={cliente}&tamanho=2` → `itens` com 2, `total` 3, `pagina` 1, `tamanho` 2.
  - `Faixa_ou_filtro_invalido_responde_400_com_erro` — `[Theory]` `?pagina=0`, `?tamanho=101`, `?status=Qualquer`, `?material=abc` → 400 com propriedade `erro`.
  - `Resumo_conta_todos_os_Pedidos_alem_do_tamanho_de_pagina` — **a metade backend da troca de guarda da 1E (D3).** Cria 25 Pedidos `Cancelado` (direto no banco, números por `NumeroUnico()`); `GET /api/pedidos/resumo` → a quantidade de `Cancelado` é `>= 25` (monótono: só as linhas deste teste são garantidas). Com um resumo que contasse uma página de 20, fica vermelho.
  - `Resumo_e_materiais_sao_leitura_de_qualquer_perfil` — `ClienteComo("Qualidade")` → 200 nas duas.
  - `Sem_token_nao_le_resumo_nem_materiais` — 401 nas duas.
  - `Qualidade_nao_cadastra_pedido_mas_le_a_lista` e `Sem_token_nao_le_a_lista` continuam verdes sem mudança.

- [ ] **Step 8: Suíte e build.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` → verde.

- [ ] **Step 9: Commit.**

```bash
git add src/ tests/
git commit -m "feat(pedidos): GET /pedidos paginado com busca e filtro, resumo e materiais em uso"
```

A partir daqui, até a Task 4, o front real (não o de teste) lê `GET /pedidos` como lista e quebra contra esta API. Ninguém publica a branch no meio; a verificação ponta a ponta é a Task 7.

---

### Task 3: A primitiva `FiltroDeDemanda` e a seleção na URL (front)

**Files:**
- Create: `web/src/components/FiltroDeDemanda.tsx`, `web/src/components/FiltroDeDemanda.test.tsx`
- Create: `web/src/hooks/useSelecaoNaUrl.ts`, `web/src/hooks/useSelecaoNaUrl.test.tsx`

**Interfaces:**
- Produces (`FiltroDeDemanda.tsx`):

```ts
export type Selecao = Record<string, string[]>
export interface OpcaoDeFaceta { valor: string; rotulo: string; rotuloCurto?: string; detalhe?: string; contagem?: number }
export interface Faceta { chave: string; titulo: string; opcoes: OpcaoDeFaceta[] }
export const LIMITE_PARA_BUSCA_NA_FACETA = 8
export const ROTULO_DE_OPCAO_AUSENTE = 'Não está mais na lista'
export function casaComFiltro(valoresDoItem: Record<string, readonly string[]>, selecao: Selecao): boolean
export function contarFiltrosAtivos(selecao: Selecao): number
export function FiltroDeDemanda(props: { facetas: Faceta[]; selecao: Selecao; aoMudar: (s: Selecao) => void }): JSX.Element
```

  `detalhe` é o código do Material, desenhado em fonte monoespaçada; `rotuloCurto` é o texto da pílula ("Pedido 1042"), e sem ele a pílula usa `rotulo`.
- Produces (`useSelecaoNaUrl.ts`): `function useSelecaoNaUrl(chaves: readonly string[]): { selecao: Selecao; mudarSelecao: (s: Selecao) => void; limpar: () => void }` — lê `?chave=a,b` de cada chave; pedaço vazio sai, espaço é aparado, repetido colapsa; escreve com `setSearchParams(…, { replace: true })`, apagando o parâmetro da chave vazia e **preservando** os parâmetros que não são dele.

- [ ] **Step 1: Testes que falham — `casaComFiltro`** (no `FiltroDeDemanda.test.tsx`, bloco próprio):
  - `faceta sem nada marcado nao restringe` — `casaComFiltro({ material: ['3'] }, {})` e `casaComFiltro({ material: ['3'] }, { material: [] })` são `true`.
  - `OU dentro da faceta` — item `{ pedido: ['42'] }` casa com `{ pedido: ['41', '42'] }`.
  - `E entre facetas` — item `{ material: ['3'], pedido: ['42'] }` casa com `{ material: ['3'], pedido: ['42'] }` e não casa com `{ material: ['3'], pedido: ['41'] }`.
  - `item com varios valores na faceta casa se algum for marcado` — `{ material: ['3', '5'] }` casa com `{ material: ['5'] }`.
  - `item sem valor numa faceta ativa nao casa` — `{ material: [] }` com `{ material: ['3'] }` é `false`.

- [ ] **Step 2: Testes que falham — componente** (`render` com `facetas` Material e Pedido; `aoMudar = vi.fn()`):
  - `fechado mostra Filtrar com o numero de filtros ativos, as pilulas e Limpar` — seleção `{ material: ['3'], pedido: ['42'] }` → botão com nome acessível "Filtrar (2)", `aria-expanded="false"`, pílulas "Chapa 3,00 mm" e "Pedido 1042", botão "Limpar".
  - `sem filtro ativo nao mostra pilulas nem Limpar`.
  - `abrir mostra um grupo por faceta com as opcoes marcaveis e a contagem` — clicar "Filtrar" → `aria-expanded="true"`; `getByRole('group', { name: 'Material' })` com a caixa "Chapa SAE 1020 3,00 mm" e o texto "7"; o código aparece em elemento `font-mono`.
  - `marcar uma opcao chama aoMudar com a selecao nova` — marcar "Pedido 1042" com seleção `{ material: ['3'] }` → `aoMudar({ material: ['3'], pedido: ['42'] })`.
  - `remover pilula tira so aquele valor` — botão com nome "Remover filtro Pedido 1042" → `aoMudar({ material: ['3'] })` (a chave vazia some).
  - `Limpar zera a selecao` → `aoMudar({})`.
  - `opcao marcada que some da lista continua marcada e visivel com zero` — renderiza com a opção "Pedido 1042" presente, depois `rerender` com ela fora de `opcoes`: a pílula continua, o painel mostra a caixa marcada com o rótulo lembrado e contagem "0".
  - `valor marcado que a tela nunca viu aparece como ausente, com zero, removivel` — seleção `{ pedido: ['99'] }` sem opção 99 → caixa marcada com rótulo `ROTULO_DE_OPCAO_AUSENTE` e "0"; desmarcá-la chama `aoMudar({})`. *(Review Focus 3.)*
  - `faceta com mais opcoes que o limite ganha campo de busca` — 9 opções → `getByRole('searchbox', { name: 'Buscar em Pedido' })`; 8 opções → sem campo.
  - `busca na faceta ignora caixa e acento` — opções "Chapa fina" e "Sôlda MIG" (e mais 7): digitar "solda" deixa só "Sôlda MIG"; digitar "CHAPA" deixa só "Chapa fina". *(Review Focus 5.)*
  - `pilula de filtro nao usa cor de estado` — nenhuma classe `positivo`, `negativo` ou `atencao` dentro do componente.

- [ ] **Step 3: Testes que falham — `useSelecaoNaUrl`** (componente de sonda dentro de `MemoryRouter` com `initialEntries`, e uma sonda de `useLocation` para ler a URL resultante):
  - `le cada chave separada por virgula` — `/fila/1?material=3,5&pedido=42` → `{ material: ['3', '5'], pedido: ['42'] }`.
  - `ignora pedaco vazio, apara espaco e colapsa repetido` — `?material=abc,,5, 5` → `{ material: ['abc', '5'] }`. *(Review Focus 2.)*
  - `escreve so as chaves com valor e preserva os outros parametros` — em `?pagina=2&material=3`, `mudarSelecao({ pedido: ['42'] })` → busca da URL tem `pagina=2` e `pedido=42`, sem `material`.
  - `escrever nao cria entrada nova no historico` — a sonda de `useNavigationType()` lê `REPLACE` depois de `mudarSelecao`.
  - `limpar tira so as chaves do hook`.

- [ ] **Step 4: Rodar e ver falhar.** `cd web && npx vitest run src/components/FiltroDeDemanda.test.tsx src/hooks/useSelecaoNaUrl.test.tsx` → FAIL (módulos inexistentes).

- [ ] **Step 5: Implementar.** Fechado, uma linha que quebra (`flex-wrap`): `Botao variante="secundario"` "Filtrar" (com `(n)` quando há filtro ativo, `aria-expanded`, `aria-controls` do painel), as pílulas (cada uma `Botao variante="secundario"` com o rótulo curto e "×", nome acessível "Remover filtro …") e "Limpar". Aberto, um `<fieldset>` com `<legend>` por faceta; caixas marcáveis como as das Tarefas (`size-5 accent-acao`); contagem em `text-tinta-fraca`. A memória de rótulos é um `useRef<Map>` por `chave:valor`, atualizada a cada render com as opções presentes. A normalização da busca da faceta é `normalize('NFD')` sem os diacríticos, em minúscula.

- [ ] **Step 6: Rodar e ver passar.** Mesmo comando → PASS.

- [ ] **Step 7: Suíte e build.** `npm test -- --run` e `npm run build` → verdes (inclusive as guardas de tema, que varrem o arquivo novo).

- [ ] **Step 8: Commit.**

```bash
git add web/src/components/FiltroDeDemanda.tsx web/src/components/FiltroDeDemanda.test.tsx web/src/hooks/useSelecaoNaUrl.ts web/src/hooks/useSelecaoNaUrl.test.tsx
git commit -m "feat(web): FiltroDeDemanda e a selecao do filtro na URL"
```

---

### Task 4: Pedidos paginados com busca e filtro, a Home pelo resumo e o status em português (front)

**Files:**
- Modify: `web/src/api/cadastros.ts` (`listarPedidos` troca de assinatura; `obterResumoDePedidos`, `listarMateriaisDosPedidos`, tipos novos)
- Modify: `web/src/pedidos/statusDoPedido.ts` (`rotuloDoStatus`)
- Modify: `web/src/pedidos/LinhaDePedido.tsx`
- Modify: `web/src/hooks/useBuscaPaginada.ts`
- Modify: `web/src/pages/HomePage.tsx`, `web/src/pages/PedidosPage.tsx`
- Test: `web/src/api/cadastros.test.ts`, `web/src/pedidos/statusDoPedido.test.ts`, `web/src/pedidos/LinhaDePedido.test.tsx`, `web/src/hooks/useBuscaPaginada.test.tsx`, `web/src/pages/HomePage.test.tsx`, `web/src/pages/PedidosPage.test.tsx`

**Interfaces:**
- Consumes: `FiltroDeDemanda`, `Faceta`, `Selecao`, `useSelecaoNaUrl` (Task 3); o contrato JSON de Pedidos (Task 2).
- Produces:

```ts
// cadastros.ts
export interface FiltroDePedidos { busca: string; status: string[]; material: string[]; pagina: number; tamanho: number }
export function listarPedidos(f: FiltroDePedidos): Promise<PaginaDe<PedidoDto>>
export interface MaterialResumoDto { id: number; codigo: string; descricao: string }
export interface ContagemDeStatusDto { status: string; quantidade: number }
export interface ResumoDePedidosDto { porStatus: ContagemDeStatusDto[]; maisAntigosAbertos: PedidoDto[] }
export function obterResumoDePedidos(): Promise<ResumoDePedidosDto>
export function listarMateriaisDosPedidos(): Promise<MaterialResumoDto[]>
// statusDoPedido.ts
export function rotuloDoStatus(status: string): string
// useBuscaPaginada.ts — opções novas, todas opcionais; sem elas o comportamento é o de hoje
interface FiltroDeBusca { busca: string; incluirInativos: boolean; pagina: number; tamanho: number; filtros: Record<string, string[]> }
interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
  inicial?: { busca?: string; pagina?: number }
  filtros?: Record<string, string[]>
  aoMudarConsulta?: (consulta: { busca: string; pagina: number }) => void
}
```

  `rotuloDoStatus`: `Aberto` → "Aberto", `EmProducao` → "Em produção", `AguardandoExpedicao` → "Aguardando expedição", `Concluido` → "Concluído", `Cancelado` → "Cancelado"; qualquer outro volta como veio.
  `listarPedidos` monta a URL com `URLSearchParams` na ordem `busca`, `status` (só se houver), `material` (só se houver), `pagina`, `tamanho`; listas juntadas por vírgula.
  `filtros` do hook é comparado **por valor** (serializado), não por referência: um objeto novo com o mesmo conteúdo a cada render não recarrega; conteúdo diferente volta à página 1 e recarrega.

- [ ] **Step 1: Testes que falham — camada de API e rótulo.**
  - `cadastros.test.ts`: `listarPedidos manda busca, status, material, pagina e tamanho na URL` (lê `new URL(fetchMock.mock.calls[0][0], 'http://x').searchParams`: `status` = `"Aberto,EmProducao"`, `material` = `"3"`); `listarPedidos sem status nem material nao manda os parametros`; `listarPedidos devolve o envelope de pagina`; `obterResumoDePedidos le /pedidos/resumo`; `listarMateriaisDosPedidos le /pedidos/materiais`; os dois lançam `ErroDeApi` em resposta não-ok. **Remova** neste mesmo passo o teste *"devolve o conjunto inteiro de pedidos, nao uma pagina — a HomePage depende disso"* e o comentário de guarda acima dele — é a **troca deliberada de guarda** (spec, seção 7.6; D3): a proteção passa ao teste do resumo no backend (Task 2) e ao teste de Home do Step 3.
  - `statusDoPedido.test.ts`: `rotuloDoStatus traduz os cinco status`; `rotuloDoStatus devolve status desconhecido como veio`.
  - `LinhaDePedido.test.tsx`: a pílula mostra "Em produção", não "EmProducao".

- [ ] **Step 2: Testes que falham — `useBuscaPaginada`.** `usa busca e pagina iniciais na primeira consulta` (`inicial: { busca: 'CH', pagina: 3 }` → primeira chamada de `buscar` com `busca: 'CH'`, `pagina: 3`); `filtros entram na consulta`; `mudar filtros volta a pagina 1 e recarrega`; `filtros com o mesmo conteudo em objeto novo nao recarregam` (dois `rerender` com `{ status: ['Aberto'] }` literal novo → `buscar` chamado uma vez só); `avisa a consulta a quem guarda na URL` (`aoMudarConsulta` recebe `{ busca: 'SUP', pagina: 1 }` depois do debounce, e `{ busca: 'SUP', pagina: 2 }` depois de `irParaPagina(2)`). Os testes existentes do hook continuam verdes sem mudança.

- [ ] **Step 3: Testes que falham — Home.** Troque o mapa de rotas do `apiCompleta()` de `'/api/pedidos'` para `'/api/pedidos/resumo'`, com um resumo equivalente aos cinco Pedidos de hoje (`porStatus` Aberto 2, EmProducao 1, AguardandoExpedicao 0, Concluido 1, Cancelado 1; `maisAntigosAbertos` na ordem já esperada). Adapte os testes que olhavam a lista crua: o texto da pílula passa a `rotuloDoStatus` ("Em produção 1", "Aguardando expedição 0"). Novos:
  - `a Home le o resumo e nao uma lista de pedidos` — **a metade front da troca de guarda (D3).** O mapa **não** tem `'/api/pedidos'`; se a Home chamar a lista, o `fetchPorRota` rejeita e o teste vê o banner de erro. Com `porStatus` Aberto 30 e EmProducao 12, o cartão mostra "42" — mais do que qualquer página de 20.
  - `conta como abertos todos os status fora de Concluido e Cancelado` — o número grande é a soma dos três não encerrados.
  - `distingue cadastro vazio de todos encerrados` — continua valendo com o resumo: soma zero → "Nenhum pedido foi cadastrado ainda."; só encerrados → "Todos os pedidos cadastrados estão concluídos ou cancelados."
  - Os três testes da Home que provavam a regra dos "abertos há mais tempo" no cliente (*"lista os pedidos abertos ha mais tempo, do mais antigo para o mais novo"*, *"deixa Concluido e Cancelado fora da lista de ha mais tempo"*, *"para em cinco mesmo havendo mais pedidos elegiveis"*) perdem o objeto: a regra passou ao servidor e é provada por `Mais_antigos_deixa_encerrados_de_fora_e_para_no_limite` (Task 2). Viram um só, `mostra os mais antigos na ordem em que o resumo os manda`; a remoção entra no relatório como mudança de lugar da guarda, não como perda.

- [ ] **Step 4: Testes que falham — Pedidos.** Rotas `'/api/pedidos'` (envelope `PaginaDe`) e `'/api/pedidos/materiais'`; render em `MemoryRouter` com `initialEntries`. Os testes existentes passam a receber o envelope; o de estado vazio distingue:
  - `mostra estado vazio de cadastro quando nao ha pedidos e nenhum filtro` (texto de hoje);
  - `mostra vazio de filtro quando a busca ou o filtro nao acham nada` — "Nenhum pedido com essa busca ou esses filtros", com `Botao` "Limpar filtros" que zera busca e seleção.
  - `busca, status e material da URL entram na primeira consulta` — `/pedidos?busca=CH&status=Aberto&material=3&pagina=2` → a primeira URL chamada tem os quatro.
  - `marcar um status no filtro consulta de novo na pagina 1 e escreve na URL`.
  - `as opcoes de Status sao os cinco em portugues e as de Material vem de /pedidos/materiais`.
  - `valor invalido da URL nao vai ao servidor e fica visivel como ausente` — `?material=abc&status=Qualquer` → a URL chamada não tem `material` nem `status`; a opção ausente aparece marcada (D8). *(Review Focus 2.)*
  - `pagina da URL alem do fim recua para a ultima` — `?pagina=99` com `total` 3 → a tela termina na página 1 (clamp do hook). *(Review Focus 2.)*
  - `pagina com os controles de paginacao` — `total` 45, `tamanho` 20 → "Página 1 de 3 — 45 no total".
  - `abrir um pedido recarrega mantendo busca e filtros` — depois do POST, a nova consulta tem os mesmos parâmetros.

- [ ] **Step 5: Rodar e ver falhar.** `npx vitest run src/api/cadastros.test.ts src/pedidos src/hooks/useBuscaPaginada.test.tsx src/pages/HomePage.test.tsx src/pages/PedidosPage.test.tsx` → FAIL.

- [ ] **Step 6: Implementar.** `HomePage` troca `listarPedidos()` por `obterResumoDePedidos()` e deriva `abertos`, `porStatus` e `cadastroVazio` do resumo; `maisAntigos` é `maisAntigosAbertos`. `PedidosPage` usa `useBuscaPaginada` com `inicial` lido da URL, `filtros` vindos de `useSelecaoNaUrl(['status', 'material'])` já sanitizados (D8), `aoMudarConsulta` escrevendo `busca`/`pagina` na URL (`replace`, apagando o default), o campo de busca (`Campo` + `CLASSES_DE_CONTROLE`, `type="search"`, rótulo "Buscar por número, cliente ou código de peça"), o `FiltroDeDemanda` com as facetas Status (os cinco, rótulo `rotuloDoStatus`) e Material (de `listarMateriaisDosPedidos`, carregada uma vez; falha nela vira banner, e a lista continua), e `ControlesDePaginacao`. O formulário de abrir Pedido não muda; depois de abrir, `recarregar()`. `LinhaDePedido` e o resumo da Home usam `rotuloDoStatus`.

- [ ] **Step 7: Rodar e ver passar.** Mesmo comando → PASS.

- [ ] **Step 8: Suíte e build.** `npm test -- --run` e `npm run build` → verdes.

- [ ] **Step 9: Commit** (um só, com a troca de guarda dentro — D3).

```bash
git add web/src/
git commit -m "feat(web): Pedidos paginados com busca e filtro, Home pelo resumo, status em portugues"
```

---

### Task 5: Filtros na fila do Setor e nas Tarefas (front)

**Files:**
- Modify: `web/src/api/execucao.ts` (`NoResumoDto`)
- Modify: `web/src/testes/execucao.ts` (`no()`), `web/src/execucao/formatacao.test.ts` (o literal de `NoResumoDto`)
- Create: `web/src/execucao/filtroDaDemanda.ts`, `web/src/execucao/filtroDaDemanda.test.ts`
- Modify: `web/src/pages/FilaDoSetorPage.tsx`, `web/src/pages/TarefasPage.tsx`
- Test: `web/src/pages/FilaDoSetorPage.test.tsx`, `web/src/pages/TarefasPage.test.tsx`

**Interfaces:**
- Consumes: `FiltroDeDemanda`, `casaComFiltro`, `Faceta`, `Selecao`, `useSelecaoNaUrl` (Task 3); `MaterialResumoDto` (Task 4, em `cadastros.ts`); o `NoResumo` da Task 1.
- Produces:

```ts
// execucao.ts — NoResumoDto ganha
pedidoCliente: string
materiais: MaterialResumoDto[]
// filtroDaDemanda.ts
export const CHAVES_DA_DEMANDA = ['material', 'pedido'] as const
export function facetasDaFila(fila: FilaDoSetorDto, selecao: Selecao): Faceta[]
export function filtrarFila(fila: FilaDoSetorDto, selecao: Selecao): FilaDoSetorDto
export function facetasDasTarefas(grupos: TarefasDoSetorDto[], selecao: Selecao): Faceta[]
export function filtrarTarefas(grupos: TarefasDoSetorDto[], selecao: Selecao): TarefasDoSetorDto[]
```

Regras que as funções fixam (spec, seções 4.2 e 4.3; D2):
- A **unidade** de contagem e de casamento é a linha (Em trabalho, A iniciar — pausados inclusive —, Aguardando coleta, Sobra, e cada item das Tarefas) ou o **cartão** de montagem. Os nós de uma linha: o próprio. Os de um cartão: o pai e os filhos com `presente > 0`.
- Uma unidade casa se **algum** nó dela casa (`casaComFiltro` sobre `{ material: ids dos materiais, pedido: [pedidoId] }`, como string). Cartão que casa aparece **inteiro**, com todos os filhos, inclusive os ausentes.
- Opções de uma faceta: os valores presentes nas unidades **sem filtro**. Material: valor = id, rótulo = `descricao`, `detalhe` = `codigo`, ordem por descrição (`localeCompare` com `numeric: true`). Pedido: valor = `pedidoId`, rótulo = `"{pedidoNumero} · {pedidoCliente}"`, `rotuloCurto` = `"Pedido {pedidoNumero}"`, ordem por número (`numeric: true`).
- Contagem de uma opção = unidades que casam com a seleção em que **esta faceta** é trocada só por esta opção e as outras ficam como estão (D2). Um cartão conta uma vez por opção.

- [ ] **Step 1: Contrato do nó.** `NoResumoDto` ganha os dois campos; `no()` ganha `pedidoCliente: 'Metalúrgica Alfa'` e `materiais: []`; o literal de `formatacao.test.ts` também. `npm run build` → verde (é o que prova que nenhum outro literal ficou sem os campos).

- [ ] **Step 2: Testes que falham — funções puras** (`filtroDaDemanda.test.ts`, com a massa de `web/src/testes/execucao.ts` e dois materiais, `CHAPA_3` e `CHAPA_6`):
  - `facetas da fila listam os materiais e os pedidos presentes com a contagem`.
  - `material com a mesma descricao e codigos diferentes vira duas opcoes` (valores por id).
  - `cartao de montagem casa pelo filho presente e conta uma vez por opcao` — pai sem material, dois filhos presentes com `CHAPA_3` → a opção `CHAPA_3` conta 1; `filtrarFila` mantém o cartão com **todos** os filhos.
  - `filho ausente do cartao nao faz o cartao casar` — o único filho com `CHAPA_6` tem `presente: 0` → com `CHAPA_6` marcado, o cartão sai.
  - `contagem de uma faceta respeita a selecao da outra` (D2) — com `material: [CHAPA_3]`, a contagem de cada Pedido é só das linhas com `CHAPA_3`.
  - `no sem material some com o filtro de material ativo`.
  - `filtrar a fila age em todas as secoes e preserva a ordem de cada uma`.
  - `tarefas: grupo sem item depois do filtro sai; itens de cada grupo mantem a ordem`.

- [ ] **Step 3: Testes que falham — fila** (`FilaDoSetorPage.test.tsx`; render em `MemoryRouter` com `initialEntries` `/fila/1…` e a rota `/fila/:setorId`):
  - `mostra o filtro com as facetas da fila`.
  - `a selecao da URL filtra a fila na primeira carga` — `/fila/1?pedido=1` com linhas de dois Pedidos → só as do Pedido 1.
  - `marcar uma opcao escreve na URL`.
  - `secao que fica sem linha pelo filtro mostra o proprio vazio` — "Em trabalho" com linha só de outro Pedido → título "Em trabalho" e o texto "Nada nesta seção com esses filtros."
  - `fila que fica vazia pelo filtro mostra o vazio do filtro com Limpar filtros` — "Nada nesta fila com esses filtros", e "Limpar filtros" devolve a fila inteira; distinto do "Nada neste Setor agora" da fila realmente vazia (que continua com o teste de hoje).
  - `Pausados some quando o filtro esconde todos os pausados` e `linha pausada que casa continua no subgrupo Pausados`. *(Review Focus 4.)*
  - `filtrar nao dispara o aviso de outra pessoa ter movido o item` (D6) — com o formulário de Iniciar aberto numa linha, marcar um Pedido que a esconde: nenhum banner com `SAIU_DA_FILA`.
  - `opcao marcada que a atualizacao tirou continua marcada com zero` — a segunda carga do `useCargaPeriodica` (timers falsos, como os testes de atualização periódica de hoje) sem a linha do Pedido marcado → pílula e caixa continuam, contagem 0, e a tela mostra o vazio do filtro.

- [ ] **Step 4: Testes que falham — Tarefas** (`TarefasPage.test.tsx`):
  - `filtra as tarefas por material e pedido e some o grupo vazio`.
  - `todos os grupos vazios pelo filtro mostram o vazio do filtro` — "Nada para levar com esses filtros", com "Limpar filtros".
  - `item marcado e oculto pelo filtro continua na entrega` (D7) — marcar dois itens, filtrar escondendo um: o botão diz "Entregar 2 itens" e, abaixo, "1 marcado oculto pelo filtro"; o corpo do POST leva os dois.
  - `filtrar nao tira item da selecao nem mostra o aviso de saiu da lista` (D6).

- [ ] **Step 5: Rodar e ver falhar.** `npx vitest run src/execucao/filtroDaDemanda.test.ts src/pages/FilaDoSetorPage.test.tsx src/pages/TarefasPage.test.tsx` → FAIL.

- [ ] **Step 6: Implementar.** As duas páginas: `useSelecaoNaUrl(CHAVES_DA_DEMANDA)`; `FiltroDeDemanda` logo abaixo dos banners, só quando há dado (`fila`/`grupos` não nulos e não vazios de verdade); desenham a partir de `filtrarFila`/`filtrarTarefas`, e calculam `chavesDaFila`, a limpeza de `escolhas` e `marcados` a partir da resposta **sem** filtro (D6, D7). Na fila, `SecoesDaFila` recebe a fila filtrada e a original: seção que tinha linha e perdeu todas pelo filtro aparece com o título e a linha "Nada nesta seção com esses filtros."; se todas perderam, só o `EstadoVazio` do filtro. A ordem das seções não muda (Em trabalho → A iniciar aqui → Aguardando montagem → Aguardando coleta → Sobra).

- [ ] **Step 7: Rodar e ver passar.** Mesmo comando → PASS.

- [ ] **Step 8: Suíte e build.** `npm test -- --run` e `npm run build` → verdes.

- [ ] **Step 9: Commit.**

```bash
git add web/src/
git commit -m "feat(web): filtro de Material e Pedido na fila do Setor e nas Tarefas"
```

---

### Task 6: Documentos que mudam junto (com review)

**Files:**
- Modify: `specs/05-api-endpoints.md` (seção "Pedido / Agrupamento": `GET /pedidos` paginado com os parâmetros e os 400, `GET /pedidos/resumo`, `GET /pedidos/materiais`; seção "Execução / Rastreamento": os campos novos do `NoResumo`)
- Modify: `specs/04-fluxos-de-usuario.md` (seção "2. Apontamento em Setor": filtrar a fila e as Tarefas; seção "1. Cadastro de Pedido": buscar e filtrar Pedidos)
- Modify: `specs/06-roadmap-mvp.md` (seção própria desta fase, depois da "Fase 3D": escopo A + B + C, dois planos, posição mantida **filtros → 1F → 3B**; o que o Plano 1 entregou e que o Plano 2 falta)
- Modify: `CLAUDE.md` (seção "Interface": o `FiltroDeDemanda` como a primitiva de filtrar demanda por facetas, ao lado de `SeletorComBusca` e `useBuscaPaginada`, e quando usar cada um; o `useSelecaoNaUrl`)

Só a parte do Plano 1 da seção 10 da spec: `POST /inicios`, `POST /terminos` e o fluxo de agir em lote ficam para o Plano 2. O revisor **mede cada afirmação** contra o código do HEAD (a documentação vira texto do TCC): nomes, parâmetros, códigos de status, ordem das listas, e toda contagem que o texto der — com o comando que a produziu e a data, na forma que o `CLAUDE.md` já usa ("medido em … com …").

- [ ] **Step 1: Escrever** as quatro mudanças acima, citando por nome, sem número de linha.
- [ ] **Step 2: Conferir** cada nome citado com `grep -rn` no HEAD; conferir o contrato JSON contra os testes de API das Tasks 1 e 2.
- [ ] **Step 3: Rodar** `npm test -- --run` (as guardas de tema e de idioma leem arquivos do repositório) e `dotnet build Rastreamento.slnx -warnaserror`.
- [ ] **Step 4: Commit.**

```bash
git add specs/ CLAUDE.md
git commit -m "docs: filtros da demanda e Pedidos paginados nos specs e no CLAUDE.md"
```

---

### Task 7: Verificação manual no celular (dispensa de review escrita antes)

O produto é um **relatório**, não código — a primeira classe de dispensa do `CLAUDE.md` ("Task cujo produto não é código"). A justificativa vai no ledger **e** no relatório **antes** de a verificação começar.

Feita pelo usuário, no celular, na sessão local (o celular não alcança o container da nuvem). O controlador prepara a bancada: banco regenerado com a massa de verificação da Fase 3D, e **pelo menos três Pedidos em produção, com Material em pelo menos dois nós e dois Materiais diferentes** (a massa `catalogo-abc.sql` do workspace do plano 3 da Fase 3 não garante isso: medir e, se faltar, acrescentar `EstruturaMaterial` pelo catálogo antes de criar as Peças). Mais de vinte Pedidos, para a paginação aparecer.

Roteiro:
1. Fila do Corte: abrir o filtro, ver as contagens, filtrar por um Material; pílulas, "Limpar"; o cartão de montagem que casa por um filho aparece inteiro.
2. Filtrar por dois Pedidos (OU) e um Material (E); F5 mantém; "voltar" mantém.
3. Tarefas: filtrar; marcar dois, esconder um pelo filtro; o aviso de oculto; entregar leva os dois.
4. Pedidos: buscar por número, por cliente e pelo código de peça de um nó filho; filtrar por Status e Material; paginar; F5 mantém.
5. Home: contagens por status em português batem com a lista de Pedidos; "abertos há mais tempo" certo.
6. Celular em pé: o filtro fechado ocupa uma linha; aberto, cabe sem rolagem horizontal.

Registro: no relatório da task e no ledger, com as palavras do usuário para cada achado. Achado que vira código entra como task nova no plano, com o gate.
