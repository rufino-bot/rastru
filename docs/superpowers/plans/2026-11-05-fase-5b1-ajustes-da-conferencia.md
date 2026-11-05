# Fase 5B-1 — Ajustes da conferência (F1–F3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** fechar os achados A1, A2, A3 e A5 da conferência da Fase 5B-1 no navegador (2026-11-05), mais a decisão
(a) do roteiro, antes da review de branch inteira.

**Architecture:** três tasks independentes. F1 mexe no contrato do livro (`MovimentacaoDto` ganha `perdaId` e
`motivoDaBaixa`) e no `HistoricoDoNo`; F2 só na `PreviaDaBaixa`; F3 só na `Pagina`. As telas não mudam em F2 nem em F3,
fora o `PainelDeBaixa`, que passa o `noId` à prévia.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (Database First), React + TypeScript + Vite, Vitest + Testing Library.

**Origem das decisões:** os achados e as decisões do usuário estão no ledger privado do projeto (não no repositório),
e o que vale para estas tasks está copiado aqui. A spec da fase é
`docs/superpowers/specs/2026-11-03-fase-5b1-perda-e-descarte-design.md`.

## Global Constraints

- Gate inteiro de `superpowers:subagent-driven-development` em cada task: implementer → review da task → fix de
  Critical/Important → re-review.
- A review de branch inteira **não** é despachada depois destas tasks: antes dela vem a conferência curta do usuário no
  navegador, só de F1–F3.
- Rótulos dos motivos, exatos, os do painel (`ROTULO_DO_MOTIVO` em `web/src/api/baixas.ts`): "Sumiu no armazém",
  "Morreu num processo", "Descarte".
- Seção "Interface" do `CLAUDE.md` inteira: primitivas de `web/src/components/`, `data-testid` só sem papel nem texto,
  cores só por token, perda é texto **neutro** (nenhum `positivo`, `negativo`, `atencao` ou `atraso` nestas tasks).
- Teste de tela com `// @vitest-environment jsdom` no topo, `afterEach(cleanup)` explícito e `web/src/testes/api.ts`
  quando houver `fetch`.
- `npm test -- --run --maxWorkers=2` **e** `npm run build`, em `web/`, no fim de toda task (o Vitest não faz typecheck).
- Task que toca o backend: `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e
  `dotnet test Rastreamento.slnx -m:1`. A API local (porta 5169) trava as DLLs: pare-a antes do build e **suba-a de
  novo** no fim (`dotnet run --project src/Rastreamento.Api`, em segundo plano), porque o usuário vai conferir no
  navegador. O banco tem a massa da conferência com baixas do usuário: **nenhum script de zerar nem de massa roda**.
- Citação por nome, nunca `arquivo:NN` nem distância relativa; comentário não cita ledger, achado ("A2") nem "Task N".
- Nomes de domínio em português, espelhando o DDL.

---

### Task 1: F1 — a baixa no Histórico do nó (A2 + A3)

**O que o usuário viu:** no "Histórico" do nó, a linha de uma baixa aparecia como "undefined de 14", "a iniciar →
perdido", com "Estornar" ao lado. O tipo `Perda` do livro não existe no front (`TipoDeMovimentacao` parou na 5A, e
`rotuloDoTipo` sai do `switch` sem `return`), e a rota genérica de estorno recusa a linha de baixa
(`EstornoImpossivel`), então o botão não deveria estar lá.

**Decisão do usuário (2026-11-05):** "Baixa com o motivo". A linha diz **"Baixa de {q} · {motivo}"**, com o rótulo do
motivo do painel; no lugar do "Estornar", a frase **"Parte de uma baixa: estorna-se a baixa inteira, na seção
Baixas."** (no molde da frase da montagem, que já existe no `HistoricoDoNo`).

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ExecucaoDtos.cs` (`MovimentacaoDto`)
- Modify: `src/Rastreamento.Application/Execucao/ProjetorDoLivro.cs` (`ProjetarMovimentacoesAsync`)
- Modify: `src/Rastreamento.Domain/Abstractions/IExecucaoRepository.cs` (método novo)
- Modify: `src/Rastreamento.Infrastructure/Persistence/ExecucaoRepository.cs`
- Modify: `tests/Rastreamento.Application.Tests/Execucao/FakeExecucaoRepo.cs`
- Test: `tests/Rastreamento.Api.Tests/BaixaEndpointsTests.cs` (livro do nó depois de uma baixa e do estorno dela)
- Test: um teste de mapeamento em `tests/Rastreamento.Infrastructure.Tests/` para o método novo do repositório (siga o
  arquivo de testes do `ExecucaoRepository` que já existe ali; se a tabela escrita já tiver `[Collection]`, use a mesma)
- Modify: `specs/05-api-endpoints.md` (o item `GET /estrutura/{id}/movimentacoes`)
- Modify: `web/src/api/execucao.ts` (`TipoDeMovimentacao`, `MovimentacaoDto`)
- Modify: `web/src/testes/execucao.ts` (fábrica `movimentacao`)
- Modify: `web/src/execucao/formatacao.ts` (`rotuloDoTipo`) e `web/src/execucao/formatacao.test.ts`
- Modify: `web/src/execucao/HistoricoDoNo.tsx` e o teste dele (`web/src/execucao/HistoricoDoNo.test.tsx`)

**Interfaces:**
- Produces (backend): `MovimentacaoDto` ganha, **no fim** e com default nulo, `int? PerdaId = null, string?
  MotivoDaBaixa = null` — o mesmo idioma dos `ContagemId`/`LiberacaoId`/`RemessaId` da 5A. JSON: `perdaId`,
  `motivoDaBaixa`. Os dois vêm preenchidos em **toda** linha que aponta uma baixa — a `Perda` e o `Estorno` dela (o
  estorno de uma baixa também grava `PerdaId`) — e nulos nas demais.
- Produces (backend): `IExecucaoRepository.ListarMotivosDasPerdasAsync(IReadOnlyCollection<int> perdaIds,
  CancellationToken ct) → Task<IReadOnlyDictionary<int, string>>` — o `Motivo` de cada `dbo.Perda` pedida, por Id. O
  projetor só o chama quando algum movimento tem `PerdaId` (lista vazia não vai ao banco).
- Produces (front): `TipoDeMovimentacao` ganha `'Perda'`; `MovimentacaoDto` ganha `perdaId: number | null` e
  `motivoDaBaixa: MotivoDaBaixa | null` (com `import type { MotivoDaBaixa } from './baixas'` — import só de tipo, que o
  compilador apaga; `baixas.ts` já importa valores de `execucao.ts`). `rotuloDoTipo('Perda')` devolve `'Baixa'`.

**Antes de mudar o contrato, meça os consumidores** (medido pelo controlador em 2026-11-05, confira de novo): no
backend, um só construtor de `MovimentacaoDto` (`ProjetorDoLivro`), usado por todo caso de uso que devolve linhas do
livro — inclusive o estorno da expedição e o da baixa —, então os campos novos aparecem em toda resposta que devolve
`MovimentacaoDto`, com nulo onde não se aplicam. No front, `rotuloDoTipo` tem um único chamador (`HistoricoDoNo`); o
`MovimentacaoDto` é tipo de retorno em `api/expedicao.ts` e `api/baixas.ts`, que não leem campo nenhum dele. O campo
novo é obrigatório no tipo do front, como os da 5A: todo literal de `MovimentacaoDto` em teste quebra o `npm run build`
até ganhar os dois campos — use a fábrica `movimentacao` de `web/src/testes/execucao.ts`, que passa a escrevê-los nulos.

- [ ] **Step 1: teste de Api que falha.** Em `BaixaEndpointsTests`, um teste que dá uma baixa num nó (motivo
  `PerdaArmazem`), lê `GET /api/estrutura/{id}/movimentacoes` e afirma que a linha `Perda` traz `perdaId` igual ao `id`
  da baixa e `motivoDaBaixa` `"PerdaArmazem"`; e que uma linha que não é de baixa (o `Inicio`, por exemplo) traz os dois
  nulos. Um segundo teste estorna a baixa (`POST /api/baixas/{id}/estorno`) e afirma o mesmo par na linha `Estorno`.
  Rode e veja falhar.
- [ ] **Step 2: o método do repositório.** No `IExecucaoRepository`, com resumo em português no estilo dos vizinhos:

```csharp
/// <summary>O motivo de cada baixa (`dbo.Perda`) pedida, por Id. Baixa inexistente nao volta.</summary>
Task<IReadOnlyDictionary<int, string>> ListarMotivosDasPerdasAsync(IReadOnlyCollection<int> perdaIds, CancellationToken ct);
```

  No `ExecucaoRepository`, no molde de `ListarNomesDeUsuariosAsync`:

```csharp
public async Task<IReadOnlyDictionary<int, string>> ListarMotivosDasPerdasAsync(
    IReadOnlyCollection<int> perdaIds, CancellationToken ct)
{
  var lista = perdaIds.ToList();
  return await _db.Perdas.AsNoTracking()
      .Where(p => lista.Contains(p.Id))
      .ToDictionaryAsync(p => p.Id, p => p.Motivo, ct);
}
```

  No `FakeExecucaoRepo`, um dicionário público `MotivosDasPerdas` (`Dictionary<int, string>`) e o método lendo dele, no
  molde de `Usuarios`/`ListarNomesDeUsuariosAsync`. Teste de Infrastructure: grava uma `Perda` e pede o motivo dela e o
  de um Id que não existe (só o primeiro volta).
- [ ] **Step 3: o DTO e o projetor.**

```csharp
public sealed record MovimentacaoDto(
    int Id, int EstruturaItemId, string Tipo, decimal Quantidade, LocalDto Origem, LocalDto Destino,
    int? MontagemId, int? EstornoDeId, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada,
    int? ContagemId = null, int? LiberacaoId = null, int? RemessaId = null,
    int? PerdaId = null, string? MotivoDaBaixa = null);
```

  Em `ProjetarMovimentacoesAsync`, junto das outras leituras em lote:

```csharp
var perdaIds = movimentos.Select(m => m.PerdaId).OfType<int>().Distinct().ToList();
var motivos = perdaIds.Count == 0
    ? new Dictionary<int, string>()
    : await _execucao.ListarMotivosDasPerdasAsync(perdaIds, ct);
```

  e, no `new MovimentacaoDto(...)`, os dois argumentos finais `m.PerdaId,
  m.PerdaId is int p ? motivos.GetValueOrDefault(p) : null`. Atualize o resumo do record se ele descrever os campos.
- [ ] **Step 4:** `dotnet build Rastreamento.slnx -warnaserror` e os testes do Step 1 passando; depois
  `dotnet test Rastreamento.slnx -m:1` inteiro.
- [ ] **Step 5: `specs/05-api-endpoints.md`.** No item `GET /estrutura/{id}/movimentacoes`, depois da frase da 5A sobre
  `contagemId`, `liberacaoId` e `remessaId`, uma frase da 5B-1: o tipo `Perda` e a posição `Perdido` aparecem no livro, e
  todo `MovimentacaoDto` ganha, no fim, `perdaId` e `motivoDaBaixa` (`PerdaArmazem`, `MortaEmProcesso` ou `Descarte`),
  preenchidos na linha `Perda` e no `Estorno` dela e nulos nas demais. Confira se a seção da baixa (a das rotas
  `/estrutura/{id}/baixas`) precisa citar isso também; se citar, uma linha só, apontando a de cima.
- [ ] **Step 6: teste de front que falha.** Em `formatacao.test.ts`, `rotuloDoTipo('Perda')` é `'Baixa'`. No teste do
  `HistoricoDoNo`, com o livro trazendo uma linha `movimentacao({ tipo: 'Perda', quantidade: 14, perdaId: 1,
  motivoDaBaixa: 'PerdaArmazem', destino: { posicao: 'Perdido', … } })` de um autor que pode estornar:
  - o texto "Baixa de 14 · Sumiu no armazém" aparece;
  - **não** há botão "Estornar" nessa linha (consulte por nome acessível, `getByRole('button', { name: /Estornar: Baixa/ })`
    ausente) — e um `Inicio` do mesmo livro **continua** com o dele, para o teste morrer se o botão sumir de tudo;
  - a frase "Parte de uma baixa: estorna-se a baixa inteira, na seção Baixas." aparece;
  - com a mesma linha `estornada: true`, a frase **não** aparece (molde da frase da montagem, que só aparece com a
    montagem viva);
  - um `Estorno` com `estornoDeId` apontando a baixa diz "Desfaz: Baixa de 14 · Sumiu no armazém, em …".
  Um caso por motivo é desnecessário; um segundo motivo (por exemplo `Descarte`) num `it.each` curto basta para provar
  que o rótulo vem do mapa, e não de um literal.
- [ ] **Step 7: o front.** `'Perda'` em `TipoDeMovimentacao` (com o comentário do tipo dizendo que é da Fase 5B-1, a
  baixa), os dois campos em `MovimentacaoDto` com comentário no estilo dos da 5A, e a fábrica `movimentacao` com
  `perdaId: null, motivoDaBaixa: null`. Em `rotuloDoTipo`, `case 'Perda': return 'Baixa'`. No `HistoricoDoNo`:

```tsx
function tituloDaMovimentacao(m: MovimentacaoDto): string {
  const base = `${rotuloDoTipo(m.tipo)} de ${formatarQuantidade(m.quantidade)}`
  return m.tipo === 'Perda' && m.motivoDaBaixa ? `${base} · ${ROTULO_DO_MOTIVO[m.motivoDaBaixa]}` : base
}
```

  `estornavel` passa a exigir também `m.perdaId === null`; a frase entra ao lado da da montagem:

```tsx
{m.tipo === 'Perda' && !m.estornada && (
  <span className="text-xs text-tinta-fraca">Parte de uma baixa: estorna-se a baixa inteira, na seção Baixas.</span>
)}
```

  e a lista do JSDoc "O que NÃO tem Estornar" ganha a linha da baixa ("a linha de uma baixa: estorna-se a baixa
  inteira, na seção Baixas"). Confira se `NOME_DO_ESTORNAVEL` ou outro `Record`/`switch` sobre `TipoDeMovimentacao` em
  `formatacao.ts` reclama do tipo novo no `npm run build`, e trate o que reclamar.
- [ ] **Step 8:** `npm test -- --run --maxWorkers=2` e `npm run build` em `web/`; a API de volta no ar.
- [ ] **Step 9: commit** (`feat(fase-5b1): a baixa no historico do no, com o motivo e sem estornar a linha`), só os
  arquivos desta task.

**Fora desta task (registrado, não fazer):** a linha `Medicao`, `Expedicao`, `PedidoDeLiberacao` etc. da 5A também
recebe "Estornar" no `HistoricoDoNo` (a condição `estornavel` não olha `contagemId`/`remessaId`/`liberacaoId`), e a rota
genérica a recusa do mesmo jeito. É dívida da 5A, para a review de branch triar; não entra aqui.

---

### Task 2: F2 — a prévia da baixa em duas listas com título visível (A1 + decisão (a))

**O que o usuário viu:** em "O que acontece", a lista do que sai não tem título visível — "Saem de produção" é só o
`aria-label` da `<ul>` —, enquanto a da sobra tem ("Ficam onde estão e viram sobra:"). E, com a trava no topo marcada,
a lista mistura as linhas do nó com as dos que saem junto, inclusive nós **acima** do nó da baixa (a Peça do topo).

**Decisões do usuário (2026-11-05):** A1, "Título visível igual" — o título no mesmo estilo do da sobra
(`text-sm text-tinta-fraca`), logo abaixo de "O que acontece". (a), "Separar" — duas listas: as linhas do **próprio nó**
em cima e as dos que **saem junto** (a cascata dos filhos e, com a trava, a Peça do topo) embaixo, cada uma com título
visível. O formato das linhas de sobra (sem código nem posição) **não** foi decidido e não muda.

**Textos (decididos neste plano):** "Saem de produção:" para as linhas do nó e "Saem junto:" para as outras. Com as
duas listas vazias, a frase de hoje, "Nada sai de produção com esses valores.", continua.

**Files:**
- Modify: `web/src/baixas/PreviaDaBaixa.tsx`
- Modify: `web/src/baixas/PainelDeBaixa.tsx` (passa o `noId`)
- Create: `web/src/baixas/PreviaDaBaixa.test.tsx` (a prévia não tem teste próprio hoje)
- Modify, se algum teste do `PainelDeBaixa` precisar: `web/src/baixas/PainelDeBaixa.test.tsx`

**Interfaces:**
- Consumes: `PreviaDaBaixaDto` e `LinhaDaBaixaDto` de `web/src/api/baixas.ts`, sem mudança. A linha é do próprio nó
  quando `estruturaItemId === noId`.
- Produces: `PreviaDaBaixa` passa a receber `{ previa: PreviaDaBaixaDto; noId: number }`. O único chamador é o
  `PainelDeBaixa` (`grep -rn "<PreviaDaBaixa" web/src`), que tem o nó em `no.id`.

- [ ] **Step 1: o teste que falha**, em `PreviaDaBaixa.test.tsx`, montando o componente direto com uma prévia de
  três linhas (uma do nó 7, uma de um filho 8, uma da Peça do topo 5) e uma sobra:
  - `getByRole('list', { name: 'Saem de produção:' })` tem só a linha do nó 7, e `getByRole('list', { name: 'Saem
    junto:' })` tem as do 8 e do 5 — e os dois títulos são **texto visível** (`getByText('Saem de produção:')` é um
    elemento que não é a lista);
  - prévia só com linhas do nó: não há lista "Saem junto:" nem o título dela;
  - prévia só com linhas de outros nós (o componente não sabe se o servidor a devolve; o caso fixa que cada lista
    aparece só com linha): não há lista "Saem de produção:" nem o título dela;
  - prévia vazia: "Nada sai de produção com esses valores." e nenhuma das duas listas;
  - a sobra continua com "Ficam onde estão e viram sobra:" e a lista "Viram sobra".
  Rode e veja falhar.
- [ ] **Step 2: a implementação.** Um `useId()` por título; o `<p id={…} className="text-sm text-tinta-fraca">` e a
  `<ul aria-labelledby={…}>`, para o nome acessível e o texto visível serem um só (sem `aria-label` duplicando). Extraia
  o `<li>` de hoje (`{q} de {código — descrição}, {origem}`) numa função ou componente local, para as duas listas
  usarem a mesma linha, sem copiar o JSX. A frase de prévia vazia vale quando `previa.linhas.length === 0`. Atualize o
  JSDoc do componente: as duas listas, de onde vem a separação (o `estruturaItemId` contra o `noId`) e que a da sobra não
  muda. No `PainelDeBaixa`, `<PreviaDaBaixa previa={previa} noId={no.id} />`.
- [ ] **Step 3:** `npm test -- --run --maxWorkers=2` e `npm run build` em `web/`.
- [ ] **Step 4: commit** (`feat(fase-5b1): a previa da baixa separa o no dos que saem junto, com titulo visivel`).

---

### Task 3: F3 — o título da aba diz a página (A5)

**O que o usuário pediu (2026-11-05):** o título da aba do navegador traz o nome da página. Hoje toda aba se chama
"Rastru" (o `<title>` fixo de `web/index.html`), e nenhuma tela escreve `document.title`. Ganho de acessibilidade
também: WCAG 2.4.2 (Page Titled), no espírito da guarda do `lang="pt-BR"`.

**Decisão do usuário:** entra nesta branch, como task própria, e as telas **não** mudam — quem escreve o título é a
`Pagina`, o ponto de entrada de toda tela.

**Forma (decidida neste plano, medida):** **"{título} · Rastru"**; sem título legível, só "Rastru". O `titulo` da
`Pagina` é `ReactNode`, e **seis** telas passam um elemento, não texto (medido com `grep -rn -A3 "<Pagina" web/src
--include=*.tsx | grep -v "\.test\."`: `AgrupamentoDetalhePage` e `PedidoDetalhePage` passam um `span` focável com o
nome do registro dentro, e `ExpedicaoPage`, `LiberacoesPage`, `SeparacaoPage` e `FilaDoSetorPage` um `span` focável com
o nome da tela). Por isso o título da aba sai do **texto renderizado do `<h1>`** (`textContent`, aparado e com espaços
internos colapsados), lido por `ref` num efeito que roda a cada render — e não de `typeof titulo === 'string'`, que
deixaria essas seis telas com "Rastru". Assim o título acompanha o que muda depois da carga (o número do Pedido chega
depois do "Pedido" de carregando). Escreva `document.title` só quando o valor mudar.

**Ao desmontar**, a `Pagina` devolve "Rastru", para a tela fora do shell não herdar o título da última página. É o que
decide as duas exceções: **`LoginPage` e `TelaCarregando` ficam "Rastru"** (não usam `Pagina`, e o `index.html`
continua dizendo "Rastru"). Na troca de uma tela por outra, o React roda a limpeza da antiga antes do efeito da nova,
então não sobra "Rastru" entre as duas — prove isso no teste.

**Files:**
- Modify: `web/src/components/Pagina.tsx`
- Modify: `web/src/components/Pagina.test.tsx`
- Modify: `CLAUDE.md`, seção "Interface", o bullet "Tela nova começa por `<Pagina titulo="…">`": uma frase dizendo que a
  `Pagina` escreve o título da aba a partir do texto do `<h1>` ("{título} · Rastru"), que a tela não escreve
  `document.title`, e que `LoginPage` e `TelaCarregando` ficam "Rastru".

**Interfaces:**
- Produces: nenhuma prop nova. A `Pagina` continua `{ titulo, acao?, largura?, children }`. Exporte o sufixo como
  constante (`SUFIXO_DO_TITULO_DA_ABA = 'Rastru'`, ou nome equivalente) se o teste ou a limpeza precisarem dele, para o
  literal não se repetir.

- [ ] **Step 1: o teste que falha**, em `Pagina.test.tsx` (o arquivo já tem `jsdom` e `cleanup`; zere
  `document.title` num `beforeEach`):
  - `<Pagina titulo="Pedidos">` → `document.title` é `"Pedidos · Rastru"`;
  - `titulo` com elemento (`<span tabIndex={-1}>PED-001</span>`, e um com link dentro, no molde do teste "aceita um
    título com link dentro do h1") → o texto do `<h1>`, sem marcação: `"PED-001 · Rastru"`;
  - `rerender` trocando o título (`"Pedido"` → `"PED-001"`) → o título da aba acompanha;
  - desmontar → `"Rastru"`;
  - trocar uma `Pagina` por outra no mesmo `rerender` (chaves diferentes, para o React desmontar uma e montar a
    outra) → o título é o da nova;
  - título só de espaço → `"Rastru"`.
  Rode e veja falhar.
- [ ] **Step 2: a implementação**, na `Pagina`: `useRef<HTMLHeadingElement>` no `<h1>` e um `useEffect` sem lista de
  dependências que lê o `textContent`, monta o título e o escreve se mudou; outro `useEffect(() => () => { document.title
  = … }, [])` para a limpeza (ou a mesma lógica num efeito só, se ficar mais claro — mas a limpeza não pode rodar a cada
  render, senão a aba pisca "Rastru"). JSDoc da `Pagina` com o porquê de ler o DOM (o `titulo` é `ReactNode`, e seis
  telas passam elemento) e a decisão das duas telas fora do shell.
- [ ] **Step 3:** o bullet do `CLAUDE.md` (ver Files).
- [ ] **Step 4:** `npm test -- --run --maxWorkers=2` e `npm run build` em `web/`. Uma suíte inteira verde prova que
  nenhum teste de tela dependia do título da aba.
- [ ] **Step 5: commit** (`feat(interface): o titulo da aba diz a pagina`).
