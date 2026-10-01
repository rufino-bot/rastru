# Deadlock na suíte de Api e no gravar árvore — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a suíte de `Api.Tests` deixa de falhar de forma intermitente por deadlock entre classes paralelas, e
`POST /agrupamentos/{id}/estrutura` e `POST /estrutura/{id}/filhos` deixam de devolver 500 com 1205 cru.

**Architecture:** item 1 desliga a paralelização do xUnit só no assembly de `Api.Tests`, com um teste-guarda
que lê o atributo. Item 2 move a gravação da árvore (e a leitura de volta) de `CriarPeca` e
`AcrescentarFilho` para dentro de `IExecucaoRepository.ExecutarAsync`, a mesma transação com retry de
1205 e tradução para 409 que a execução usa; `GravarArvoreAsync` deixa de abrir transação e passa a
exigir uma.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Database First), SQL Server, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-01-deadlock-na-suite-de-api-design.md`

## Global Constraints

- `dotnet build Rastreamento.slnx -warnaserror` termina com 0 avisos.
- A suíte roda com `dotnet test Rastreamento.slnx -m:1`, com o banco de `bash scripts/backend-na-nuvem`
  (ou `docker compose up -d` + schema + seed, fora da nuvem).
- Nomes de domínio em português; nomes técnicos em inglês (`Repository`, `UseCase`).
- Comentário de código cita alvo **pelo nome** (teste, método, constante), nunca por `arquivo.ext:NN` nem
  por distância relativa ("acima", "o teste anterior").
- Comentários de código seguem o estilo dos arquivos vizinhos: português **sem acento** em `src/` e
  `tests/`.
- Nenhuma mudança de schema.
- Uma política de retry só: nada de laço de retry novo fora de `ExecucaoRepository.ComRetryDeDeadlockAsync`.
- O 409 da gravação usa `CodigosDaExecucao.ConflitoDeConcorrencia` e `CodigosDaExecucao.MensagemDeConflito`
  (via `Falhas.ExecutarAsync`), sem frase nova.

## Review Focus

1. **Retry reexecuta a gravação do zero.** Um deadlock na 1ª tentativa e sucesso na 2ª tem de deixar a árvore
   gravada **uma** vez, sem duplicata. Coberto por construção: a vítima do deadlock teve a transação
   desfeita pelo SQL Server, `ComRetryDeDeadlockAsync` limpa o change tracker e `GravarNo` constrói
   entidades novas a cada chamada (o `NoParaGravar` é imutável). O revisor da Task 2 confere que nenhuma
   entidade é criada fora do `trabalho` e reaproveitada entre tentativas.
2. **Erro que não é conflito continua cru.** Violação de FK (547) no meio da árvore, dentro de
   `EmTransacaoAsync`, tem de subir como `DbUpdateException`, e não como 409. Teste:
   `GravarArvoreAsync_e_atomico_erro_no_meio_da_arvore_nao_deixa_nada_gravado` (Task 2, Passo 6), que passa a
   compor com `EmTransacaoAsync` e mantém o `Assert.ThrowsAsync<DbUpdateException>`.
3. **Chamador que esquece a transação.** `GravarArvoreAsync` fora de transação tem de recusar alto, e não
   gravar meia árvore. Teste: `GravarArvoreAsync_fora_de_transacao_recusa_e_nao_grava_nada` (Task 2,
   Passo 5).
4. **Tirar o atributo de paralelização.** A suíte ficaria verde na maioria das execuções. Teste:
   `Paralelizacao_entre_classes_fica_desligada_neste_assembly` (Task 1).
5. **Concorrência dentro de um teste.** `CorridaNaQueimaDeFamiliaTests` e as corridas da execução lançam
   `Task`s concorrentes dentro de um mesmo teste e têm de continuar concorrentes. Coberto pela suíte
   inteira verde na Task 1: o atributo serializa coleções, não `Task`s.

---

### Task 1: `Api.Tests` sem paralelização entre classes

**Files:**
- Create: `tests/Rastreamento.Api.Tests/ParalelizacaoDaSuiteDeApi.cs`
- Modify: `CLAUDE.md` (seção "Processos de teste também competem pelo banco")

**Interfaces:**
- Consumes: nada.
- Produces: nada que outra task use.

- [ ] **Passo 1: escrever o teste-guarda, sem o atributo**

Em `ParalelizacaoDaSuiteDeApi.cs`, classe `ParalelizacaoDaSuiteDeApiTests`, namespace
`Rastreamento.Api.Tests`:

```csharp
[Fact]
public void Paralelizacao_entre_classes_fica_desligada_neste_assembly()
{
  var atributo = typeof(ParalelizacaoDaSuiteDeApiTests).Assembly
      .GetCustomAttribute<CollectionBehaviorAttribute>();
  Assert.NotNull(atributo);
  Assert.True(atributo!.DisableTestParallelization);
}
```

- [ ] **Passo 2: ver o teste falhar**

Run: `dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ParalelizacaoDaSuiteDeApiTests"`
Expected: FAIL em `Assert.NotNull`.

- [ ] **Passo 3: acrescentar o atributo no mesmo arquivo**

`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, acima do namespace, com um comentário
que diga: por que (classes paralelas do mesmo assembly deadlockam sob SERIALIZABLE, inclusive em nós sem
relação, pelo range lock de fim de índice da emenda de 2026-09-26 da seção 8.1 da spec da Fase 3; o
`-m:1` só serializa projetos); a medição de 2026-10-01 (os números da seção 1 da spec deste plano e a falha
na limpeza `CenarioDaFase3NaApi.DisposeAsync` medida na sessão de nuvem); por que não `[Collection]`
seletiva (seção 2.1 da spec); e que concorrência **dentro** de um teste não muda.

- [ ] **Passo 4: ver o teste passar e a suíte de Api inteira verde**

Run: `dotnet test tests/Rastreamento.Api.Tests`
Expected: PASS, todos. Anote o tempo de `Duration` para o relatório.

- [ ] **Passo 5: medir a mutação**

Comente o atributo, rode o filtro do Passo 2 (FAIL), descomente, rode de novo (PASS). Registre as duas
saídas no relatório.

- [ ] **Passo 6: parágrafo no `CLAUDE.md`**

Na seção "Processos de teste também competem pelo banco", depois do parágrafo do `-m:1`, um parágrafo novo:
o `-m:1` não basta **dentro** de um assembly; medido em 2026-10-01 (usuário: 4/7 vermelhas na branch do
Plano 2, 1/5 na `main`; sessão de nuvem: a contagem que o controlador passar no brief); `Api.Tests` roda com
`DisableTestParallelization`, e o teste-guarda é `Paralelizacao_entre_classes_fica_desligada_neste_assembly`;
`Infrastructure.Tests` continua paralela, por não ter aparecido na medição. Os números "depois" entram na
Task 3, não aqui.

- [ ] **Passo 7: build e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror` — Expected: 0 avisos.

```bash
git add tests/Rastreamento.Api.Tests/ParalelizacaoDaSuiteDeApi.cs CLAUDE.md
git commit -m "test(api): desliga a paralelizacao entre classes de Api.Tests, com guarda"
```

---

### Task 2: gravar árvore na transação da execução

**Files:**
- Modify: `tests/Rastreamento.Application.Tests/Estrutura/Fakes.cs` (`FakeEstruturaRepo`)
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs` (construtor)
- Create: `tests/Rastreamento.Application.Tests/Estrutura/GravacaoDeArvoreNaTransacaoTests.cs`
- Modify: `tests/Rastreamento.Infrastructure.Tests/Persistence/EstruturaRepositoryTests.cs`
- Modify: `src/Rastreamento.Application/Estrutura/MontagemDeEstruturaUseCase.cs` (`CriarPeca`, `AcrescentarFilho`)
- Modify: `src/Rastreamento.Infrastructure/Persistence/EstruturaRepository.cs` (`GravarArvoreAsync`)
- Modify: `src/Rastreamento.Domain/Abstractions/IEstruturaRepository.cs` (XML doc de `GravarArvoreAsync`)
- Modify: `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md` (seção 8.1, emenda)

**Interfaces:**
- Consumes: `IExecucaoRepository.EmTransacaoAsync` e a extensão `Falhas.ExecutarAsync` (já existem);
  `FakeExecucaoRepo.ConflitoNaProximaTransacao` (já existe).
- Produces: `FakeEstruturaRepo.EstaEmTransacao` (`Func<bool>?`, setter público), preenchido pelo construtor
  de `FakeExecucaoRepo`. O contrato de `IEstruturaRepository.GravarArvoreAsync` passa a ser "exige transação
  aberta pelo chamador".

- [ ] **Passo 1: o fake de estrutura passa a exigir transação, como o real vai exigir**

Em `FakeEstruturaRepo`: propriedade `public Func<bool>? EstaEmTransacao { get; set; }`. No início de
`GravarArvoreAsync`, se ela não é nula e devolve `false`, lança
`InvalidOperationException("GravarArvoreAsync fora de EmTransacaoAsync.")`, no molde da guarda de
`TravarNosAsync` do `FakeExecucaoRepo`. No construtor de `FakeExecucaoRepo`, `estruturas.EstaEmTransacao =
() => _emTransacao;`. Fake de estrutura usado sem `FakeExecucaoRepo` continua sem a guarda.

- [ ] **Passo 2: os testes novos de Application**

`GravacaoDeArvoreNaTransacaoTests`, montando o caso de uso direto (mesmos fakes do `Montar` de
`CriarPecaTests`: `FakeEstruturaRepo`, `FakeAgrupamentoRepo` com `new Agrupamento { Id = 1, PedidoId = 1,
Codigo = "AG-01", Tipo = "Kit" }`, `FakeReceitaPadraoRepo` com um Componente 1 com `ArquivoSolidoId`
preenchido, `FakePedidoRepo()` vazio) e guardando a instância de `FakeExecucaoRepo`:

```csharp
[Fact]
public async Task CriarPeca_com_conflito_na_transacao_devolve_409_ConflitoDeConcorrencia_sem_gravar()
{
  // arranjo acima; execucao.ConflitoNaProximaTransacao = true;
  var r = await useCase.CriarPeca(1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);
  Assert.False(r.Sucesso);
  Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
  Assert.Equal(0, estruturas.GravacoesDeArvore);
}

[Fact]
public async Task AcrescentarFilho_com_conflito_na_transacao_devolve_409_ConflitoDeConcorrencia_sem_gravar()
{
  // arranjo acima; cria a Peca com CriarPeca (sucesso), e so ENTAO execucao.ConflitoNaProximaTransacao = true;
  var r = await useCase.AcrescentarFilho(raizId, new NovoFilhoDto(ComponenteId: null, Descricao: "Filho ad-hoc", Quantidade: 1m, QuantidadePorPai: 1m), CancellationToken.None);
  Assert.False(r.Sucesso);
  Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
  Assert.Equal(1, estruturas.GravacoesDeArvore);   // so a da Peca
}
```

- [ ] **Passo 3: ver Application falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: FAIL nos dois testes novos (o resultado é sucesso), e também em todo teste de `CriarPecaTests`,
`EditarEExcluirNoTests` e `QuantidadePorPaiTests` que grava árvore, com
`InvalidOperationException: GravarArvoreAsync fora de EmTransacaoAsync.` Registre a contagem de vermelhos no
relatório.

- [ ] **Passo 4: mover a gravação para `ExecutarAsync`**

Em `MontagemDeEstruturaUseCase.CriarPeca` e `AcrescentarFilho`, só as duas últimas etapas (gravar e montar
o DTO de volta) entram em `return await _execucao.ExecutarAsync(async () => { ... }, ct);`, no molde de
`EditarNo`. Validação, existência de Agrupamento/Componente/pai, `PlanejarCopiaDoCatalogo` e a leitura
descartada do Pedido ficam **antes**, onde estão. Comentário curto em cada um: por que a gravação está na
transação da execução (o 500 medido em 2026-10-01, a emenda de 2026-09-26 da seção 8.1 da spec da Fase 3)
e por que o catálogo fica fora (a seção 3.2 da spec deste plano). Em `AcrescentarFilho`, registrar o
residual da seção 3.6 da spec (pai lido fora da transação; `DELETE` concorrente do pai dá 547 cru, 500).

Run: `dotnet test tests/Rastreamento.Application.Tests` — Expected: PASS, todos.

- [ ] **Passo 5: o teste novo de Infrastructure**

Em `EstruturaRepositoryTests`:

```csharp
[Fact]
public async Task GravarArvoreAsync_fora_de_transacao_recusa_e_nao_grava_nada()
{
  // arranjo: o mesmo de GravarArvoreAsync_grava_pai_e_filho_com_o_nivel_hierarquico_correto, com try/finally e LimparAsync
  var erro = await Assert.ThrowsAsync<InvalidOperationException>(
      () => repo.GravarArvoreAsync(agrupamentoId, null, no, CancellationToken.None));
  Assert.Contains("EmTransacaoAsync", erro.Message);
  // num contexto novo: nenhuma linha de dbo.EstruturaItem com aquele AgrupamentoId
}
```

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~EstruturaRepositoryTests"`
Expected: FAIL no teste novo (hoje o método abre a própria transação e grava).

- [ ] **Passo 6: os outros testes de `EstruturaRepositoryTests` passam a abrir a transação**

As quatro chamadas a `repo.GravarArvoreAsync(...)` dos testes existentes passam a correr dentro de
`new ExecucaoRepository(db).EmTransacaoAsync(() => repo.GravarArvoreAsync(...), CancellationToken.None)`,
no mesmo `db`. O de atomicidade mantém `Assert.ThrowsAsync<DbUpdateException>` e o `Assert.Contains` da FK
de material, e o comentário dele passa a dizer que quem dá a atomicidade é a transação de
`EmTransacaoAsync`, e que um 547 atravessa o laço de retry cru (Review Focus, item 2).

- [ ] **Passo 7: `GravarArvoreAsync` exige transação**

Em `EstruturaRepository.GravarArvoreAsync`: sem `BeginTransactionAsync`; se
`_db.Database.CurrentTransaction is null`, lança `InvalidOperationException` com mensagem que contenha
`EmTransacaoAsync` (molde de `ExecucaoRepository.TravarNosAsync`); senão, `GravarNo` e devolve o Id.
`IsolationLevel` e `System.Data` saem se ficarem sem uso. O XML doc perde o parágrafo "Residual conhecido"
e a premissa "só insere nós NOVOS, que nenhuma escrita da execução disputa", e ganha: exige a transação de
`EmTransacaoAsync`; por que (nó novo cai no gap travado por leitura SERIALIZABLE de outra transação, medido
em 2026-10-01 como 500 em `POST /estrutura/{id}/filhos`); a atomicidade vem da transação do chamador. O XML
doc de `IEstruturaRepository.GravarArvoreAsync` ganha uma frase: exige transação aberta
(`IExecucaoRepository.EmTransacaoAsync`).

Run: `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~EstruturaRepositoryTests"`
Expected: PASS, todos.

- [ ] **Passo 8: medir as mutações**

Cada uma aplicada sozinha, rodada e desfeita; saída no relatório:
1. `CriarPeca` grava fora do `ExecutarAsync` → `CriarPeca_com_conflito_na_transacao_...` FAIL.
2. `AcrescentarFilho` grava fora do `ExecutarAsync` → `AcrescentarFilho_com_conflito_na_transacao_...` FAIL.
3. Guarda de `CurrentTransaction` removida de `EstruturaRepository.GravarArvoreAsync` →
   `GravarArvoreAsync_fora_de_transacao_recusa_e_nao_grava_nada` FAIL.

- [ ] **Passo 9: emenda na spec da Fase 3**

Na seção 8.1 de `2026-09-24-fase-3-rastreamento-de-setor-design.md`, depois da emenda de 2026-09-26, uma
**Emenda de 2026-10-01**: criar Peça e acrescentar filho gravam dentro da transação da execução (retry de
1205, 409 no esgotamento e em 1222), sem trava de nó (acrescentar filho é livre, seção 4.7); motivo: o 500
medido e a premissa refutada do comentário de `GravarArvoreAsync`; referência à spec deste plano.

- [ ] **Passo 10: build, suíte e commit**

Run: `dotnet build Rastreamento.slnx -warnaserror` (0 avisos) e `dotnet test Rastreamento.slnx -m:1` (verde).

```bash
git add src/ tests/ docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md
git commit -m "fix(estrutura): gravar arvore na transacao da execucao, com retry de deadlock e 409"
```

---

### Task 3: medição "depois" e registro

Task do controlador. **Produto não é código**: são as execuções medidas e o registro delas. A dispensa de
review é a primeira classe do `CLAUDE.md` ("Task cujo produto não é código") e fica escrita **antes**, no
ledger e no relatório desta task. O único texto versionado que ela muda (os números "depois" no parágrafo
do `CLAUDE.md` da Task 1) entra na review de branch inteira.

**Files:**
- Modify: `CLAUDE.md` (o parágrafo da Task 1, com os números "depois")
- Ledger: `.superpowers/sdd/2026-10-01-deadlock-suite/` (`medicao-antes/`, `medicao-depois/`, `progress.md`)

- [ ] **Passo 1:** no HEAD da branch, `dotnet build Rastreamento.slnx -warnaserror`, depois 10 execuções de
  `dotnet test Rastreamento.slnx -m:1 --no-build --logger "trx;LogFilePrefix=execN" --results-directory <dir>/execN`
  (diretório próprio por execução), mais as mesmas 10 com `-- xUnit.MaxParallelThreads=16` se a medição
  "antes" tiver as duas variantes. Nada de build durante a medição: o `--no-build` mede os binários do HEAD.
- [ ] **Passo 2:** resumo por execução (commit, rc, tempo de parede, `Duration` de cada assembly e nomes das
  falhas, lidos do `.trx`), para "antes" e "depois", no ledger.
- [ ] **Passo 3:** critério da seção 2.4 da spec: 0 vermelhas no "depois", com pelo menos uma vermelha no
  "antes" da mesma variante. Se houver vermelha no "depois", **parar** e investigar (systematic-debugging)
  antes de qualquer outra coisa.
- [ ] **Passo 4:** os números "depois" (vermelhas e o custo de tempo da suíte de Api) no parágrafo do
  `CLAUDE.md` da Task 1; commit `docs: medicao depois do conserto do deadlock na suite de Api`.
