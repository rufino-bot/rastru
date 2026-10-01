# Fase 1F — Cadastro sob demanda e ordenação das listas — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tirar o formulário de cadastro do topo de cinco telas e pô-lo num painel sob demanda, aberto por um botão no cabeçalho; dar às quatro telas de lista um seletor "Ordenar por" com "Mais recentes" como padrão; e tornar o cartão inteiro clicável nas três listas que só têm o texto como link.

**Architecture:** No backend, `GET /componentes` e `GET /pedidos` ganham `?ordem=`, que vira um enum no filtro de domínio e um `switch` de `OrderBy` no repositório; a ordem padrão de `GET /componentes` passa de `Codigo` a `Id` decrescente para todo consumidor. No front nascem duas primitivas — `PainelDeEscrita` (moldura, `<h2>` ligado por `aria-labelledby`, Cancelar, foco inicial), extraída do painel que a tela do Agrupamento escreveu à mão, e `SeletorDeOrdem` —, uma função pura `ordenarCadastro` para as listas que chegam inteiras, e o `useBuscaPaginada` ganha `voltarAoInicio()`. Cada tela passa a abrir em leitura; salvar com sucesso fecha o painel e devolve a consulta ao padrão, e o item novo aparece no topo. O cartão clicável conserta, na `ItemDeCadastro`, a armadilha do overlay que engole o botão do item.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (SQL Server), xUnit; React + TypeScript (Vite), React Router, Vitest + Testing Library + jsdom, Tailwind.

**Spec:** `docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md`, revista e aprovada em 2026-10-01 (decisões 1 a 12). Branch: `fase-1f-cadastro-sob-demanda`, sobre `75df075`.

## Global Constraints

- Execução por `superpowers:subagent-driven-development` com o **gate inteiro** (implementer → review → fix pass de Critical/Important → re-review). Única dispensa prevista: a **Task 11**, verificação no navegador — o produto é relatório, não código —, com a justificativa escrita **antes** no ledger e no relatório. A Task 10 (documentação) **tem** review: afirmação falsa em spec vira texto do TCC.
- **Base de cada task registrada ANTES de despachar o implementer** (SHA), e o pacote de review gerado com `scripts/review-package BASE HEAD`, nunca com `HEAD~1`.
- **Sem mudança de schema.** `specs/02-modelo-de-dados.sql` não muda e não há `db/alter-*.sql`: "Mais recentes" é `Id` decrescente (o `Id` é `IDENTITY`), e em Pedidos é a `DataAbertura` que já existe.
- `GET /setores` e `GET /materiais` **não mudam** (spec, "Setores e Materiais: no cliente, endpoints intocados"): ordenar essas duas telas é trabalho do cliente.
- Nomes de domínio em português, espelhando o DDL; nomes técnicos em inglês (`CLAUDE.md`, "Convenções de nomenclatura").
- **Citação por nome**, nunca `arquivo.ext:NN` nem distância relativa, em código e em prosa. Comentário de código **não cita** ledger, brief nem "Task N do plano": o repositório é público. As decisões D1–D10 abaixo podem ser citadas como "decisão Dn do plano da 1F", e as da spec como "decisão N da spec da 1F".
- **Comentário que descreve a classe de empilhamento não a escreve**: o scanner do Tailwind lê o fonte inteiro, comentário incluído, e planta a regra no CSS (registrado no comentário da armadilha em `ListaDeCadastro`).
- Backend: `dotnet build Rastreamento.slnx -warnaserror` com **0 warnings**; suíte com `dotnet test Rastreamento.slnx -m:1`. Teste de banco novo **escopa a asserção** nas linhas que ele mesmo inseriu (prefixo por teste, filtrado por `busca`): asserção sobre ordem ou contagem global de tabela compartilhada é flaky por construção. Teste novo de Infrastructure que escreve em `dbo.Componente` entra na `[Collection(ColecaoQueEscreveEmComponente.Nome)]` que a classe dele já usa.
- Front: `npm test -- --run` **e** `npm run build` (erro de tipo em `.test.tsx` quebra o build sem quebrar a suíte). Primitivas de `web/src/components/`, nada de campo, botão ou banner escrito à mão; cores só por token; `// @vitest-environment jsdom` + `afterEach(cleanup)`; mocks por `web/src/testes/api.ts` (`respostaJson`, `fetchPorRota`).
- **`data-testid` só onde não há papel nem texto estável** (`CLAUDE.md`, "Interface"). O `<form>` do painel ganha nome acessível pelo `aria-labelledby`, então teste **novo** acha o painel por `getByRole('form', { name: … })`. O `testId` da primitiva existe só para os testes existentes da tela do Agrupamento.
- **Gating de perfil na ação:** o botão do cabeçalho e o painel ficam sob o mesmo `usePodeEscrever(recurso)` que hoje envolve o formulário. O seletor de ordem é leitura: aparece para todo perfil.
- `git pull` antes de todo commit, nos dois repositórios (código e ledger). No ledger, `git add` por caminho explícito, em chamada separada do `task-brief`/`review-package`.

## Bancada

```bash
bash scripts/estado
docker compose up -d                       # local; na nuvem: bash scripts/backend-na-nuvem
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx -m:1
cd web && npm ci && npm test -- --run && npm run build
```

**Baseline:** o ledger registra, medido em 2026-10-01 na árvore de `75df075` (na nuvem): backend **945** (Api 302 · Application 490 · Infrastructure 153), build 0 warnings; front **1011 testes / 64 arquivos**, build ok (só o aviso antigo de chunk > 500 kB). **O pré-flight remede nesta máquina antes da Task 1** e escreve o número no ledger do plano. Este plano **não** dá totais absolutos por task: cada implementer mede o delta da própria task e o escreve no relatório.

## Contrato HTTP novo (fonte para o front)

```text
GET /componentes?...&ordem=recentes|codigo|descricao
GET /pedidos?...&ordem=recentes|numero|cliente

ordem ausente, vazia ou só espaços   → recentes
recentes   Componentes: Id decrescente
           Pedidos:     DataAbertura decrescente, Id decrescente (a ordem de hoje)
codigo     Codigo crescente          (único: UQ_Componente_Codigo)
descricao  Descricao crescente, Id decrescente
numero     Numero crescente          (único: UQ_Pedido_Numero)
cliente    Cliente crescente, Id decrescente

valor desconhecido (comparação ordinal: "Codigo" também é desconhecido)
  → 400 { "erro": "Ordem 'Codigo' desconhecida. Aceitas: recentes, codigo, descricao." }
```

Nenhum outro contrato muda. O envelope `{ itens, total, pagina, tamanho }` é o de hoje.

## Decisões deste plano (onde a spec deixou a escolha)

- **D1 — `ordem` no domínio é enum com padrão.** `OrdemDeComponentes { Recentes, Codigo, Descricao }` e `OrdemDePedidos { Recentes, Numero, Cliente }` vivem junto dos filtros, em `IComponenteRepository.cs` e `IPedidoRepository.cs`, e entram nos records como **último** parâmetro posicional, com padrão `Recentes`: nenhum ponto que constrói o filtro hoje precisa mudar para compilar. O texto vira enum no caso de uso, por um dicionário ordinal — o repositório nunca vê string.
- **D2 — A ordem é a última validação.** O caso de uso valida faixa, depois (em Pedidos) status e material, e só então a ordem. Os testes de 400 que existem continuam recebendo a mesma frase.
- **D3 — O front só manda `ordem` quando não é a padrão.** `listarComponentes` e `listarPedidos` acrescentam `ordem` à URL só quando o filtro traz um valor diferente de `'recentes'`. As URLs de hoje não mudam (os testes que as afirmam continuam verdes **sem edição**), e o `SeletorComBusca`, que não passa ordem, recebe a padrão do servidor — que é o que a decisão 9 da spec pede.
- **D4 — `voltarAoInicio()` no `useBuscaPaginada`.** Zera busca (campo e consultada), volta à página 1, desmarca "incluir inativos" e **força** uma recarga mesmo quando a consulta já estava no padrão — sem isso, salvar na página 1 sem filtro nenhum não buscaria o item novo. O tamanho da página **não** muda: é preferência de exibição, não restrição da consulta. A ordem e as facetas são de fora do hook (`filtros`), e quem as zera é a tela, no mesmo handler. Implementação: um contador `geracao` no estado, nas dependências de `carregar`, que `voltarAoInicio` incrementa; as quatro trocas de estado caem num render só e saem numa requisição só.
- **D5 — O botão do cabeçalho some enquanto o painel dele está aberto.** O caminho de saída é `Cancelar` ou salvar. Reabrir com o painel aberto teria de escolher entre apagar o que foi digitado e ignorar o clique — sem o botão, a pergunta não existe. Na tela do Agrupamento, o "Nova Peça" some só quando o painel **de Peça** está aberto: com o painel de editar/acrescentar aberto, ele aparece, e clicá-lo troca de painel (decisão 4 da spec, exclusividade).
- **D6 — Editar setor usa o mesmo painel.** A `SetoresPage` é a única das quatro listas com edição no próprio item ("Editar"), e hoje ela preenche o mesmo formulário do topo. Depois desta fase, "Editar" abre o painel com título "Editar setor", o nome atual como subtítulo e o submit "Salvar alterações" (o rótulo de hoje). Clicar "Editar" em outro setor com o painel aberto troca o conteúdo (o painel remonta por `key`, e o foco volta ao primeiro campo). Salvar uma **edição** com sucesso fecha o painel e recarrega **mantendo** a ordem e o "Mostrar inativos": a decisão 7 da spec existe para o item **novo** aparecer, e o item editado já estava na tela — zerar a ordem de quem acabou de clicar num item da lista seria punir o uso.
- **D7 — Reativar com sucesso é desfecho de sucesso.** Em Setores, Materiais e Componentes, "Reativar o existente" bem-sucedido fecha o painel, limpa o formulário (o que o código já faz hoje) e devolve a consulta ao padrão, como o salvar. Consequência aceita: o item reativado tem `Id` antigo, então "Mais recentes" **não** o põe no topo. A spec trazia uma frase falsa sobre isso, corrigida no mesmo commit deste plano.
- **D8 — Erro de escrita dentro do painel; erro de leitura e de ação de lista fora.** Setores e Materiais hoje têm um `erro` só, para carga, salvar, reativar e inativar. Ele se divide em `erro` (carga e Inativar/Reativar do item, fora do painel, onde está hoje) e `erroDeEscrita` (salvar e "Reativar o existente", dentro do painel). Em Componentes e Pedidos o `erroDeEscrita` já existe e passa para dentro do painel; o Inativar/Reativar do item de Componentes, que hoje escreve em `erroDeEscrita`, passa a escrever num `erroDeAcao` fora do painel — com o painel fechado, um erro de Inativar não teria onde aparecer.
- **D9 — O foco inicial pula o cabeçalho do painel.** O primeiro controle focável é procurado **no conteúdo** (os filhos), não no `<form>` inteiro: o `Cancelar` vem antes, no cabeçalho, e é o primeiro focável do DOM.
- **D10 — Documentação inclui `specs/04-fluxos-de-usuario.md`.** A spec lista `05`, `06` e o `CLAUDE.md`; o fluxo 1 ("Cadastro de Pedido") descreve a lista de Pedidos com busca, filtros e URL, e passa a mentir por omissão sem a ordem e o botão "Novo pedido". A Task 10 o atualiza.

## Review Focus

Condições que a spec implica e que um usuário vai encontrar, sem que um teste "óbvio" da spec as exercite. Cada uma tem teste na task dona.

1. **Salvar na página 1, sem filtro, já em "Mais recentes"** — nada muda no estado da consulta, e mesmo assim a lista tem de buscar de novo. Teste no hook (Task 3, `voltarAoInicio recarrega mesmo sem nada a mudar`) e em Componentes (Task 6).
2. **Salvar com busca digitada e ainda no debounce** — o texto do campo mudou mas a consulta não; `voltarAoInicio` tem de zerar os dois, ou o debounce pendente dispara depois e refiltra. Teste na Task 3 (`voltarAoInicio cancela a busca que ainda estava no debounce`).
3. **Pedidos com a URL inteira preenchida** (`?busca=x&status=Aberto&material=3&ordem=cliente&pagina=2`) e salvar com sucesso → a URL volta a `/pedidos` limpa e a requisição final é a padrão. Teste na Task 7.
4. **Link velho com `?ordem=lixo` em Pedidos** — não pode virar 400 na tela. Teste na Task 7 (`ordem desconhecida na URL nao vai ao servidor`).
5. **Dois painéis na tela do Agrupamento** — abrir "Nova Peça" com o de editar aberto, e o contrário: nunca dois `<form>` de painel no documento. Teste na Task 8.
6. **Botão do item debaixo do overlay do link** — a suíte não alcança (jsdom não calcula layout); a Task 11 clica no **centro** do botão no navegador.

---

### Task 1: `?ordem=` em `GET /componentes` e `GET /pedidos` (backend)

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IComponenteRepository.cs`, `src/Rastreamento.Domain/Abstractions/IPedidoRepository.cs`
- Modify: `src/Rastreamento.Application/Cadastros/CadastroDeComponenteUseCase.cs`, `src/Rastreamento.Application/Cadastros/CadastroDePedidoUseCase.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/ComponenteRepository.cs`, `src/Rastreamento.Infrastructure/Persistence/PedidoRepository.cs`
- Modify: `src/Rastreamento.Api/Controllers/ComponentesController.cs`, `src/Rastreamento.Api/Controllers/PedidosController.cs`
- Modify: `tests/Rastreamento.Application.Tests/Cadastros/Fakes.cs`
- Test: `tests/Rastreamento.Application.Tests/Cadastros/CadastroDeComponenteUseCaseTests.cs`, `CadastroDePedidoUseCaseTests.cs`
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/ComponenteMappingTests.cs`, `PedidoRepositoryTests.cs`
- Test: `tests/Rastreamento.Api.Tests/ComponentesEndpointsTests.cs` e o arquivo de endpoints de Pedidos que hoje testa `GET /api/pedidos` (localizar com `grep -rln "/api/pedidos?" tests/Rastreamento.Api.Tests`)
- Doc: `specs/05-api-endpoints.md` (os itens `GET /componentes` e `GET /pedidos`)

**Interfaces:**
- Produces (Domain):

```csharp
public enum OrdemDeComponentes { Recentes, Codigo, Descricao }
public sealed record FiltroDeComponente(
    string? Busca, bool IncluirInativos, int Pagina, int Tamanho,
    OrdemDeComponentes Ordem = OrdemDeComponentes.Recentes);

public enum OrdemDePedidos { Recentes, Numero, Cliente }
public sealed record FiltroDePedidos(
    string? Busca, IReadOnlyList<string> Status, IReadOnlyList<int> Materiais, int Pagina, int Tamanho,
    OrdemDePedidos Ordem = OrdemDePedidos.Recentes);
```

- Produces (Application) — `ordem` entra **antes** de `pagina`, e todo chamador existente passa `null`:

```csharp
public Task<Result<PaginaDto<ComponenteDto>>> Listar(
    string? busca, bool incluirInativos, string? ordem, int pagina, int tamanho, CancellationToken ct);
public Task<Result<PaginaDto<PedidoDto>>> Listar(
    string? busca, string? status, string? material, string? ordem, int pagina, int tamanho, CancellationToken ct);
```

- Produces (HTTP): `[FromQuery] string? ordem = null` nos dois `Listar` dos controllers. O contrato está no cabeçalho.

- [ ] **Step 1: Testes de caso de uso que falham** (no fake, que passa a guardar `UltimoFiltro` também em Componentes, se ainda não guarda):
  - `Listar_sem_ordem_pede_Recentes` — `[Theory]` com `null`, `""` e `"  "` → `UltimoFiltro.Ordem == Recentes`. Um para Componentes, um para Pedidos.
  - `Listar_traduz_cada_ordem` — `[Theory]`: Componentes `("recentes", Recentes)`, `("codigo", Codigo)`, `("descricao", Descricao)`; Pedidos `("recentes", Recentes)`, `("numero", Numero)`, `("cliente", Cliente)`.
  - `Listar_com_ordem_desconhecida_e_Validacao_e_nao_consulta` — `[Theory]` com `"Codigo"`, `"nome"`, `"recente"` → `TipoDeErro.Validacao`, `Erro` igual a `$"Ordem '{valor}' desconhecida. Aceitas: recentes, codigo, descricao."` (Pedidos: `recentes, numero, cliente`), e `UltimoFiltro` nulo.
  - `Faixa_invalida_ganha_da_ordem_invalida` — `pagina = 0` com `ordem = "x"` → a frase de faixa de hoje (D2).
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/Rastreamento.Application.Tests --filter "FullyQualifiedName~CadastroDeComponenteUseCaseTests|FullyQualifiedName~CadastroDePedidoUseCaseTests"` → não compila.
- [ ] **Step 3: Implementar** os enums, os records, o parâmetro novo nos dois `Listar` (todos os chamadores existentes passam `null`) e a tradução por dicionário ordinal:

```csharp
private static readonly IReadOnlyDictionary<string, OrdemDeComponentes> OrdensAceitas =
    new Dictionary<string, OrdemDeComponentes>(StringComparer.Ordinal)
    {
      ["recentes"] = OrdemDeComponentes.Recentes,
      ["codigo"] = OrdemDeComponentes.Codigo,
      ["descricao"] = OrdemDeComponentes.Descricao,
    };
// depois das validações de hoje (D2):
var ordemDaConsulta = OrdemDeComponentes.Recentes;
if (!string.IsNullOrWhiteSpace(ordem) && !OrdensAceitas.TryGetValue(ordem, out ordemDaConsulta))
  return Result<PaginaDto<ComponenteDto>>.Falha(
      $"Ordem '{ordem}' desconhecida. Aceitas: {string.Join(", ", OrdensAceitas.Keys)}.",
      TipoDeErro.Validacao);
```

  A ordem das chaves do dicionário é a ordem da frase — o teste do Step 1 a fixa. O fake de Componentes passa a ordenar como o repositório real (o contrato no cabeçalho); o de Pedidos também.
- [ ] **Step 4: Rodar e ver passar.** O filtro do Step 2 → PASS. Rodar `tests/Rastreamento.Application.Tests` inteiro: os testes que dependiam da ordem por código do fake e ficaram vermelhos com a padrão nova são **listados por nome no relatório**; cada um passa `"codigo"` explicitamente **só** se o ponto dele não for a ordem padrão.
- [ ] **Step 5: Testes de banco que falham** (escopados por prefixo + `busca`):
  - `ComponenteMappingTests`: `Recentes_vem_do_maior_Id_para_o_menor` (três inseridos em sequência → `busca` pelo prefixo devolve na ordem inversa da inserção); `Descricao_desempata_por_Id_decrescente` (duas linhas com a mesma descrição e uma com descrição menor → a menor primeiro, depois as duas iguais com a de maior `Id` antes); `Codigo_ordena_crescente`.
  - `Pagina_em_ordem_de_codigo_independente_da_ordem_de_insercao` (existente) passa `Ordem: OrdemDeComponentes.Codigo` — o nome continua verdadeiro e a mutação que ele mata (apagar o `OrderBy` por código) continua morta. O mesmo para todo outro teste da classe cujo ponto é a ordem por código (há um comentário sobre `OrderBy(Codigo)` noutro teste dela: conferir).
  - `PedidoRepositoryTests`: `Numero_ordena_crescente` e `Cliente_desempata_por_Id_decrescente`; o teste de ordem que já existe (`DataAbertura` decrescente) continua valendo sem edição, porque é a `Recentes`.
- [ ] **Step 6: Implementar o `switch` nos dois repositórios.**

```csharp
var ordenada = filtro.Ordem switch
{
  OrdemDeComponentes.Codigo => consulta.OrderBy(c => c.Codigo),
  OrdemDeComponentes.Descricao => consulta.OrderBy(c => c.Descricao).ThenByDescending(c => c.Id),
  _ => consulta.OrderByDescending(c => c.Id),
};
// Pedidos:
//   Numero  => OrderBy(p => p.Numero)
//   Cliente => OrderBy(p => p.Cliente).ThenByDescending(p => p.Id)
//   _       => OrderByDescending(p => p.DataAbertura).ThenByDescending(p => p.Id)   (a de hoje)
```

  O comentário que hoje diz "OrderBy obrigatorio e por Codigo de proposito" passa a dizer por que toda opção termina em ordem total (unicidade de `Codigo`/`Numero`, desempate por `Id` nas outras) e que `Recentes` é a padrão desde a decisão 9 da spec da 1F. O XML doc de `IPedidoRepository.ListarAsync` ("Ordem: `DataAbertura` decrescente…") passa a descrever as três.
- [ ] **Step 7: Rodar e ver passar.** `dotnet test tests/Rastreamento.Infrastructure.Tests --filter "FullyQualifiedName~ComponenteMappingTests|FullyQualifiedName~PedidoRepositoryTests"` → PASS.
- [ ] **Step 8: Testes de API que falham.**
  - `ComponentesEndpointsTests`: `Listagem_sem_ordem_vem_dos_mais_recentes` (três criados por POST com o mesmo prefixo → `?busca={prefixo}` devolve os códigos na ordem inversa da criação); `Ordem_codigo_ordena_por_codigo`; `Ordem_desconhecida_responde_400_nomeando_o_valor` (`?ordem=Codigo` → 400, `erro` contém `'Codigo'`).
  - `Pagina_e_total_respeitam_a_busca` (existente) acrescenta `&ordem=codigo` às três URLs: o ponto dele é paginação e busca, e as asserções por posição pressupõem ordem por código.
  - Pedidos: `Ordem_numero_ordena_por_numero`, `Ordem_desconhecida_responde_400_nomeando_o_valor`, e `Listagem_sem_ordem_mantem_a_ordem_por_data_de_abertura` se nenhum teste existente já afirma isso pela API (conferir antes de escrever).
- [ ] **Step 9: Controllers e doc.** `[FromQuery] string? ordem = null` repassado ao caso de uso. Em `specs/05-api-endpoints.md`, nos dois endpoints: o parâmetro, as opções, a padrão, o desempate e o 400 (a linha "Ordem: `DataAbertura` decrescente…" de `GET /pedidos` vira a descrição das três). Em `GET /componentes`, dizer que a padrão é `recentes` desde a 1F e que vale para todo consumidor, inclusive o seletor de catálogo.
- [ ] **Step 10: Suíte e build.** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1` → verde. Delta de testes por projeto no relatório, e a lista dos testes existentes editados, com o motivo de cada um.
- [ ] **Step 11: Commit.**

```bash
git add src/ tests/ specs/05-api-endpoints.md
git commit -m "feat(api): ordem escolhida em GET /componentes e GET /pedidos, recentes por padrao"
```

---

### Task 2: Primitiva `PainelDeEscrita`

**Files:**
- Create: `web/src/components/PainelDeEscrita.tsx`
- Test (novo): `web/src/components/PainelDeEscrita.test.tsx`

**Interfaces:**
- Produces:

```tsx
interface Props {
  /** O nome da ação ("Novo setor"). Vira o `<h2>` e o nome acessível do `<form>`. */
  titulo: string
  /** Opcional: identifica o alvo (o nó sendo editado, o setor sendo editado). */
  subtitulo?: ReactNode
  aoEnviar: (e: FormEvent<HTMLFormElement>) => void
  /** O `Cancelar`. Quem descarta o digitado é a tela, aqui dentro. */
  aoFechar: () => void
  /** Só para os testes existentes da tela do Agrupamento (decisão da spec: parâmetro, não fixo). */
  testId?: string
  /** Campos, banner de erro de escrita e o submit — tudo que varia por tela. */
  children: ReactNode
}
export function PainelDeEscrita(props: Props): JSX.Element
```

- Render: `<form onSubmit={aoEnviar} aria-labelledby={idDoTitulo} data-testid={testId} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">`; um cabeçalho `flex flex-wrap items-center justify-between gap-3` com `<div><h2 id={idDoTitulo} className="text-lg font-medium text-tinta">` + `<p className="text-xs text-tinta-fraca">{subtitulo}</p>` (só quando há subtítulo) e `<Botao variante="secundario" onClick={aoFechar}>Cancelar</Botao>`; e um `<div ref={conteudo} className="flex flex-col gap-4">{children}</div>`. É a forma do painel que a tela do Agrupamento escreveu à mão, mais o `id`/`aria-labelledby` que ele não tinha. `idDoTitulo` por `useId()`.
- Foco (D9): `useEffect(() => { conteudo.current?.querySelector<HTMLElement>('input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])')?.focus() }, [])` — só na montagem. Quem precisa do foco de novo (trocar o alvo com o painel aberto) remonta o painel por `key`.

- [ ] **Step 1: Testes que falham:**
  - `o titulo e o h2 e da nome acessivel ao form` — `getByRole('form', { name: 'Novo setor' })` existe e `getByRole('heading', { level: 2, name: 'Novo setor' })` também.
  - `mostra o subtitulo quando existe e nao deixa paragrafo vazio quando nao existe`.
  - `Cancelar chama aoFechar e nao envia o form` — `aoEnviar` não chamado.
  - `submeter chama aoEnviar`.
  - `ao abrir, o foco vai ao primeiro campo do conteudo, e nao ao Cancelar` — filhos: um `<input>` com rótulo → `document.activeElement` é ele.
  - `pula controle desabilitado ao escolher o foco` — primeiro filho `<input disabled>`, segundo um `<select>` → o foco vai ao `<select>`.
  - `repassa o testId ao form` e `sem testId nao poe o atributo`.
- [ ] **Step 2: Rodar e ver falhar.** `cd web && npx vitest run src/components/PainelDeEscrita.test.tsx` → falha (módulo inexistente).
- [ ] **Step 3: Implementar** como descrito em Interfaces, com um comentário de cabeçalho que diga de onde a primitiva veio (o painel da tela do Agrupamento), por que não fecha no `Escape` nem prende o foco (não é modal; spec, "O painel"), e por que o foco procura no conteúdo (D9).
- [ ] **Step 4: Rodar e ver passar.** O arquivo → PASS; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 5: Commit.**

```bash
git add web/src/components/PainelDeEscrita.tsx web/src/components/PainelDeEscrita.test.tsx
git commit -m "feat(web): primitiva PainelDeEscrita, extraida do painel do Agrupamento"
```

---

### Task 3: Ordenação no front — `SeletorDeOrdem`, `ordenarCadastro`, `ordem` na API e `voltarAoInicio`

**Files:**
- Create: `web/src/components/SeletorDeOrdem.tsx`, `web/src/components/SeletorDeOrdem.test.tsx`
- Create: `web/src/cadastros/ordenarCadastro.ts`, `web/src/cadastros/ordenarCadastro.test.ts`
- Modify: `web/src/api/cadastros.ts` (`FiltroDeComponentes`, `listarComponentes`, `FiltroDePedidos`, `listarPedidos`, tipos de ordem)
- Modify: o teste de `api/cadastros` que afirma as URLs de `listarComponentes`/`listarPedidos` (localizar com `grep -rln "listarComponentes\|listarPedidos" web/src --include=*.test.ts*`)
- Modify: `web/src/hooks/useBuscaPaginada.ts`, `web/src/hooks/useBuscaPaginada.test.tsx`

**Interfaces:**
- Produces (`api/cadastros.ts`):

```ts
export type OrdemDeComponentes = 'recentes' | 'codigo' | 'descricao'
export type OrdemDePedidos = 'recentes' | 'numero' | 'cliente'
// FiltroDeComponentes e FiltroDePedidos ganham:   ordem?: OrdemDe…
// listar…: if (f.ordem !== undefined && f.ordem !== 'recentes') params.set('ordem', f.ordem)   — D3
//   (em listarComponentes, depois de `tamanho`; em listarPedidos, depois de `tamanho`)
```

- Produces (`SeletorDeOrdem.tsx`):

```tsx
export interface OpcaoDeOrdem<T extends string> { valor: T; rotulo: string }
interface Props<T extends string> {
  opcoes: readonly OpcaoDeOrdem<T>[]
  valor: T
  aoMudar: (valor: T) => void
}
export function SeletorDeOrdem<T extends string>(props: Props<T>): JSX.Element
// <Campo rotulo="Ordenar por">{(id) => <select id={id} value={valor} onChange=… className={CLASSES_DE_CONTROLE}>…</select>}</Campo>
```

  Rótulos que as telas passam (decisão 10 da spec — direção na opção): `'Mais recentes'`, `'Código (A→Z)'`, `'Descrição (A→Z)'`, `'Número (A→Z)'`, `'Cliente (A→Z)'`, `'Nome (A→Z)'`.

- Produces (`cadastros/ordenarCadastro.ts`):

```ts
/** `texto` nulo = "Mais recentes": `id` decrescente. Senão, `localeCompare` em pt-BR, e `id`
    decrescente no empate. Devolve um array NOVO: nunca ordena o estado no lugar. */
export function ordenarCadastro<T extends { id: number }>(itens: readonly T[], texto: ((item: T) => string) | null): T[]
```

- Produces (`useBuscaPaginada`): `BuscaPaginada<T>.voltarAoInicio(): void` (D4).

- [ ] **Step 1: Testes que falham — `SeletorDeOrdem`:** `tem o rotulo Ordenar por` (`getByLabelText('Ordenar por')` é um `<select>`); `lista as opcoes na ordem dada`; `marca o valor corrente`; `escolher chama aoMudar com o valor e nao com o rotulo`.
- [ ] **Step 2: Testes que falham — `ordenarCadastro`:** `nulo ordena por id decrescente`; `texto ordena crescente em pt-BR` (`'Ébano'` entre `'Delta'` e `'Faia'`; sem `localeCompare` cairia depois de `'Z'`); `empate no texto desempata por id decrescente`; `nao muta o array recebido`.
- [ ] **Step 3: Testes que falham — API:** `listarComponentes nao manda ordem quando e a padrao` (sem `ordem` e com `'recentes'`: a URL é a de hoje); `listarComponentes manda ordem quando nao e a padrao` (`'codigo'` → `&ordem=codigo`); os mesmos dois para `listarPedidos`.
- [ ] **Step 4: Testes que falham — hook** (`useBuscaPaginada.test.tsx`, no estilo do arquivo — `renderHook`, timers falsos onde ele já usa):
  - `voltarAoInicio zera busca, pagina e inativos e recarrega` — depois de busca `'x'` consultada, página 3 e inativos ligados → a última chamada de `buscar` tem `busca: ''`, `pagina: 1`, `incluirInativos: false`; `textoDaBusca === ''`.
  - `voltarAoInicio recarrega mesmo sem nada a mudar` — montado e assentado no padrão → `voltarAoInicio()` → `buscar` chamado mais uma vez. *(Review Focus 1.)*
  - `voltarAoInicio cancela a busca que ainda estava no debounce` — `mudarBusca('abc')` e, antes do atraso, `voltarAoInicio()`; avançar o relógio além do atraso → nenhuma chamada com `busca: 'abc'`. *(Review Focus 2.)*
  - `voltarAoInicio nao muda o tamanho`.
  - `voltarAoInicio junto de filtros novos faz uma requisicao so` — no mesmo `act`, trocar os `filtros` (rerender) e chamar `voltarAoInicio()` → exatamente uma chamada nova, já com os filtros novos.
- [ ] **Step 5: Rodar e ver falhar.** `npx vitest run src/components/SeletorDeOrdem.test.tsx src/cadastros/ src/hooks/useBuscaPaginada.test.tsx` e o teste da API → falham.
- [ ] **Step 6: Implementar.** No hook (D4): `const [geracao, setGeracao] = useState(0)` nas dependências de `carregar`, e

```ts
const voltarAoInicio = useCallback(() => {
  setTextoDaBusca('')
  setBusca('')
  setPagina(1)
  setIncluirInativos(false)
  setGeracao((g) => g + 1)
}, [])
```

  `geracao` não entra no `FiltroDeBusca` (é só gatilho). O comentário do hook ganha um parágrafo sobre por que a recarga é forçada (Review Focus 1). Como `textoDaBusca` e `busca` vão juntos a `''`, o efeito do debounce vê os dois iguais e não agenda nada (Review Focus 2).
- [ ] **Step 7: Rodar e ver passar.** Os arquivos do Step 5 → PASS. `npm test -- --run` inteiro verde **sem editar** teste existente fora dos citados (D3: as URLs de hoje não mudam); `npm run build` verde.
- [ ] **Step 8: Commit.**

```bash
git add web/src/components/SeletorDeOrdem.tsx web/src/components/SeletorDeOrdem.test.tsx web/src/cadastros/ web/src/api/ web/src/hooks/
git commit -m "feat(web): SeletorDeOrdem, ordenarCadastro, ordem na API e voltarAoInicio no hook"
```

---

### Task 4: `SetoresPage` — painel, edição no painel e ordem no cliente

**Files:**
- Modify: `web/src/pages/SetoresPage.tsx`
- Test: `web/src/pages/SetoresPage.test.tsx`

**Interfaces:**
- Consumes: `PainelDeEscrita` (Task 2); `SeletorDeOrdem`, `OpcaoDeOrdem`, `ordenarCadastro` (Task 3).
- Estado da tela: `painel: { tipo: 'novo' } | { tipo: 'editar'; setor: SetorDto } | null` (substitui o `editando`), `ordem: 'recentes' | 'nome'` (padrão `'recentes'`), `erro` (carga e ações do item) e `erroDeEscrita` (D8).

Comportamento (spec, "Ciclo de vida", mais D5–D8):

| Evento | Efeito |
|---|---|
| "Novo setor" (cabeçalho, `acao` da `Pagina`, `Botao` primário) | `painel = { tipo: 'novo' }`, campos vazios; o botão some (D5) |
| "Editar" de um item | `painel = { tipo: 'editar', setor }`, campos preenchidos; o painel remonta por `key={`editar-${setor.id}`}` (D6) |
| `Cancelar` | `painel = null`, campos vazios, `erroDeEscrita` e `idReativavel` nulos |
| Salvar novo com sucesso | `painel = null`, campos vazios, `ordem = 'recentes'`, `incluirInativos = false`, recarrega (decisão 7) |
| Salvar edição com sucesso | `painel = null`, campos vazios, recarrega **mantendo** ordem e inativos (D6) |
| 409 | painel aberto; `erroDeEscrita` e, se inativo, "Reativar o existente" **dentro** do painel |
| "Reativar o existente" com sucesso | como salvar novo com sucesso (D7) |
| Falha de rede ao salvar ou reativar | painel aberto, `erroDeEscrita` dentro dele |
| Inativar/Reativar do item | `erro`, fora do painel (como hoje) |

Título do painel: "Novo setor" / "Editar setor"; subtítulo só na edição: o nome atual do setor. Submit: "Adicionar" / "Salvar alterações" (rótulos de hoje). O `Cancelar` que o formulário tinha no modo de edição sai: quem cancela agora é o do painel. Ordem: `SeletorDeOrdem` com `[{ valor: 'recentes', rotulo: 'Mais recentes' }, { valor: 'nome', rotulo: 'Nome (A→Z)' }]`, logo acima da lista, junto do "Mostrar inativos"; a lista renderiza `ordenarCadastro(setores, ordem === 'nome' ? (s) => s.nome : null)`. `EstadoVazio`: "Use o botão Novo setor para criar o primeiro." (spec, detalhe 2).

- [ ] **Step 1: Edição mecânica dos testes existentes.** Todo teste que hoje acha um campo do formulário passa a clicar antes em "Novo setor" (ou em "Editar" do item, nos de edição) — por um auxiliar no topo do arquivo, `async function abrirNovoSetor()`. O teste que afirma a frase literal do `EstadoVazio` muda para a frase nova. **Nenhum** teste existente é apagado; se algum perder o sentido, o relatório diz qual e por quê.
- [ ] **Step 2: Testes novos que falham:**
  - `abre em leitura: sem formulario antes do clique` — `queryByRole('form', { name: 'Novo setor' })` nulo e `queryByLabelText('Nome do setor')` nulo. *(O matador da regressão: devolver o form ao topo deixa este vermelho.)*
  - `Novo setor abre o painel e some enquanto ele esta aberto`.
  - `Cancelar fecha o painel e descarta o digitado` — reabrir mostra o campo vazio.
  - `salvar novo com sucesso fecha o painel, volta a Mais recentes e o setor novo e o primeiro` — com a ordem em "Nome (A→Z)" e "Mostrar inativos" marcado antes de salvar; depois: painel fechado, seletor em "Mais recentes", checkbox desmarcado, e o primeiro `listitem` é o setor que a recarga devolveu com o maior `id`.
  - `Editar abre o painel com o nome preenchido e o titulo Editar setor`.
  - `salvar edicao com sucesso fecha o painel e mantem a ordem escolhida` (D6).
  - `Editar outro setor com o painel aberto troca o conteudo`.
  - `conflito mantem o painel aberto com o erro e o Reativar dentro dele` — `within(getByRole('form', { name: 'Novo setor' }))` acha os dois.
  - `reativar com sucesso fecha o painel e volta a Mais recentes` (D7).
  - `erro de Inativar aparece fora do painel` — com o painel fechado, o banner aparece (D8).
  - `ordenar por nome reordena a lista no cliente sem nova requisicao` — `fetch` chamado o mesmo número de vezes antes e depois.
  - `quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo setor`.
- [ ] **Step 3: Rodar e ver falhar.** `npx vitest run src/pages/SetoresPage.test.tsx`.
- [ ] **Step 4: Implementar** conforme a tabela.
- [ ] **Step 5: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/pages/SetoresPage.tsx web/src/pages/SetoresPage.test.tsx
git commit -m "feat(web): Setores abre em leitura, cadastro e edicao no painel, ordem por nome ou recentes"
```

---

### Task 5: `MateriaisPage` — painel e ordem no cliente

**Files:**
- Modify: `web/src/pages/MateriaisPage.tsx`
- Test: `web/src/pages/MateriaisPage.test.tsx`

**Interfaces:**
- Consumes: as mesmas da Task 4.
- Estado: `painelAberto: boolean` (Materiais não tem edição na tela), `ordem: 'recentes' | 'codigo' | 'descricao'`, `erro` e `erroDeEscrita` (D8).

Mesma tabela da Task 4 sem as linhas de edição. Painel: título "Novo material", submit "Adicionar". Opções de ordem: Mais recentes · Código (A→Z) · Descrição (A→Z); lista por `ordenarCadastro(materiais, ordem === 'codigo' ? (m) => m.codigo : ordem === 'descricao' ? (m) => m.descricao : null)`. `EstadoVazio`: "Use o botão Novo material para criar o primeiro."

- [ ] **Step 1: Edição mecânica dos testes existentes** (auxiliar `abrirNovoMaterial()`), nas mesmas regras da Task 4.
- [ ] **Step 2: Testes novos que falham:** `abre em leitura: sem formulario antes do clique`; `Novo material abre o painel e some enquanto ele esta aberto`; `Cancelar fecha o painel e descarta o digitado`; `salvar com sucesso fecha o painel, volta a Mais recentes e o material novo e o primeiro`; `conflito mantem o painel aberto com o erro e o Reativar dentro dele`; `reativar com sucesso fecha o painel e volta a Mais recentes`; `erro de Inativar aparece fora do painel`; `ordenar por codigo e por descricao reordena no cliente sem nova requisicao`; `quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo material`.
- [ ] **Step 3: Rodar e ver falhar.** `npx vitest run src/pages/MateriaisPage.test.tsx`.
- [ ] **Step 4: Implementar.**
- [ ] **Step 5: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/pages/MateriaisPage.tsx web/src/pages/MateriaisPage.test.tsx
git commit -m "feat(web): Materiais abre em leitura, cadastro no painel, ordem escolhida"
```

---

### Task 6: `ComponentesPage` — painel e ordem no servidor

**Files:**
- Modify: `web/src/pages/ComponentesPage.tsx`
- Test: `web/src/pages/ComponentesPage.test.tsx`

**Interfaces:**
- Consumes: `PainelDeEscrita`; `SeletorDeOrdem`; `OrdemDeComponentes` e o `ordem` de `FiltroDeComponentes`; `voltarAoInicio`.
- O hook passa a receber `filtros: { ordem: [ordem] }` (memoizado), e `buscar` vira uma função de módulo `buscarComponentes(f: FiltroDeBusca)` que repassa `ordem: f.filtros?.ordem?.[0] as OrdemDeComponentes | undefined` a `listarComponentes` — mesmo molde do `buscarPedidos` da `PedidosPage`. O comentário que hoje justifica passar `listarComponentes` direto ao hook sai.
- Estado: `painelAberto`, `ordem: OrdemDeComponentes` (padrão `'recentes'`, estado da tela — decisão 11), `erroDeEscrita` (dentro do painel) e `erroDeAcao` (Inativar/Reativar do item, fora — D8).

Painel: "Novo componente" / "Adicionar". Ordem: Mais recentes · Código (A→Z) · Descrição (A→Z), na barra de consulta ao lado de "Por página". Sucesso (salvar ou reativar — D7): fecha, limpa, `setOrdem('recentes')` e `lista.voltarAoInicio()` **no mesmo handler**, sem `lista.recarregar()` (o `voltarAoInicio` já recarrega; os dois juntos dariam duas requisições). `EstadoVazio` do catálogo vazio: "Use o botão Novo componente para criar o primeiro."; o de busca sem resultado não muda. **O link do item não muda nesta task** (o cartão clicável é a Task 8).

- [ ] **Step 1: Edição mecânica dos testes existentes** (auxiliar `abrirNovoComponente()`). Os que afirmam "limpa o formulário e recarrega a lista" passam a afirmar que o painel fechou e que houve **uma** requisição nova depois do POST. Conferir um por um os testes de `Reativar` (D7 muda o desfecho: o painel fecha), e citar no relatório cada um que mudou de asserção.
- [ ] **Step 2: Testes novos que falham:**
  - `abre em leitura: sem formulario antes do clique`.
  - `Novo componente abre o painel acima da barra de busca` — na ordem do documento, o `<form>` do painel vem antes do campo "Buscar por código ou descrição" (`compareDocumentPosition`).
  - `Cancelar fecha o painel e descarta o digitado`.
  - `salvar com sucesso fecha o painel e devolve a consulta ao padrao` — antes de salvar: busca `'x'` consultada, página 2, ordem "Código (A→Z)", inativos marcados; depois: a última URL de `GET /componentes` não tem `busca` com valor, tem `pagina=1`, `incluirInativos=false` e **não** tem `ordem`; o seletor mostra "Mais recentes"; o campo de busca está vazio.
  - `salvar com sucesso na consulta padrao ainda recarrega` *(Review Focus 1)*.
  - `ordem escolhida vai ao servidor e volta a pagina 1` — "Código (A→Z)" estando na página 2 → requisição com `ordem=codigo` e `pagina=1` (afirmar os dois parâmetros, não a string inteira).
  - `conflito mantem o painel aberto com o erro e o Reativar dentro dele`.
  - `erro de Inativar aparece com o painel fechado`.
  - `quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo componente`.
- [ ] **Step 3: Rodar e ver falhar.** `npx vitest run src/pages/ComponentesPage.test.tsx`.
- [ ] **Step 4: Implementar.**
- [ ] **Step 5: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx
git commit -m "feat(web): Componentes abre em leitura, cadastro no painel, ordem no servidor"
```

---

### Task 7: `PedidosPage` — painel e ordem na URL

**Files:**
- Modify: `web/src/pages/PedidosPage.tsx`
- Test: `web/src/pages/PedidosPage.test.tsx`

**Interfaces:**
- Consumes: `PainelDeEscrita`; `SeletorDeOrdem`; `OrdemDePedidos` e o `ordem` de `FiltroDePedidos`; `voltarAoInicio`.
- A ordem mora **só na URL**, como a seleção das facetas (decisão 11): `const ordem: OrdemDePedidos = ehOrdemValida(params.get('ordem')) ? … : 'recentes'`. Mudar escreve `ordem` com `replace: true`, e **apaga** o parâmetro quando volta a `'recentes'` (spec: o padrão não vai à URL). Valor desconhecido na URL vale `'recentes'` e não é enviado ao servidor (spec; Review Focus 4). A ordem entra nos `filtros` do hook (`{ status, material, ordem: [ordem] }`), e `buscarPedidos` repassa `ordem: f.filtros?.ordem?.[0]`.
- Estado: `painelAberto`; `erroDeEscrita` passa para dentro do painel. O `BannerDeErro` de fora fica com `erroDeLeitura` e o dos materiais.

Painel: "Novo pedido" / "Abrir pedido", **acima** da busca e do `FiltroDeDemanda` (o primeiro elemento abaixo do cabeçalho). Ordem: Mais recentes · Número (A→Z) · Cliente (A→Z), na barra de consulta, depois do `FiltroDeDemanda`. Sucesso: fecha, limpa, `setParams(new URLSearchParams(), { replace: true })` e `lista.voltarAoInicio()` no mesmo handler, sem `lista.recarregar()`. `EstadoVazio` "Nenhum pedido aberto": "Use o botão Novo pedido para abrir o primeiro."; o de filtros sem resultado não muda (spec, detalhe 2).

- [ ] **Step 1: Edição mecânica dos testes existentes** (auxiliar `abrirNovoPedido()`). O comentário de `salvar` ("Recarrega a MESMA consulta: busca, filtros e página seguem como estavam") é o contrário da regra nova e sai; os testes que afirmam esse comportamento mudam de asserção, e o relatório os cita por nome.
- [ ] **Step 2: Testes novos que falham:**
  - `abre em leitura: sem formulario antes do clique`.
  - `Novo pedido abre o painel acima da busca e do filtro`.
  - `Cancelar fecha o painel e descarta o digitado`.
  - `salvar com sucesso limpa a URL inteira e a consulta volta ao padrao` — rota inicial `/pedidos?busca=x&status=Aberto&material=3&ordem=cliente&pagina=2`; depois de salvar: a localização é `/pedidos` sem query, e a última requisição de `GET /pedidos` não tem `busca` com valor, `status`, `material` nem `ordem`, e tem `pagina=1`. *(Review Focus 3.)*
  - `ordem lida da URL e respeitada` — `/pedidos?ordem=numero` → a primeira requisição tem `ordem=numero` e o seletor mostra "Número (A→Z)".
  - `ordem desconhecida na URL nao vai ao servidor` — `/pedidos?ordem=lixo` → requisição sem `ordem`, seletor em "Mais recentes", nenhuma mensagem de erro. *(Review Focus 4.)*
  - `escolher Mais recentes tira ordem da URL` e `escolher Cliente poe ordem=cliente na URL sem criar entrada de historico`.
  - `conflito mantem o painel aberto com a mensagem dentro dele`.
  - `quem nao pode escrever ve a lista, o filtro e o seletor de ordem, e nao ve o botao Novo pedido`.
- [ ] **Step 3: Rodar e ver falhar.** `npx vitest run src/pages/PedidosPage.test.tsx`.
- [ ] **Step 4: Implementar.**
- [ ] **Step 5: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/pages/PedidosPage.tsx web/src/pages/PedidosPage.test.tsx
git commit -m "feat(web): Pedidos abre em leitura, cadastro no painel, ordem na URL"
```

---

### Task 8: `AgrupamentoDetalhePage` — "Nova Peça" no painel e o painel da 8b migrado

**Files:**
- Modify: `web/src/pages/AgrupamentoDetalhePage.tsx`
- Test: `web/src/pages/AgrupamentoDetalhePage.test.tsx`

**Interfaces:**
- Consumes: `PainelDeEscrita` (Task 2).
- **O tipo local `PainelDeEscrita` da tela colide com o nome da primitiva** e é renomeado para `ModoDoPainel` (o estado `painel` continua com esse nome).
- Estado novo: `painelDePecaAberto: boolean`. A exclusividade (decisão 4 da spec) vira regra das quatro aberturas: `abrirNovaPeca` fecha o painel de nó (`fecharPainel()`), `noParaExcluir` e `noEmDetalhe`; `abrirAcrescentarFilho`, `abrirEditar`, `pedirExclusao` e `abrirDetalhe` fecham o painel de Peça (`fecharPainelDePeca()`, que também limpa `componente`, `quantidade`, `requerRelatorioDimensional` e `erroEscrita`).
- Os dois painéis passam a usar a primitiva:
  - Peça: `<PainelDeEscrita titulo="Nova Peça" testId="painel-de-peca" aoEnviar={salvar} aoFechar={fecharPainelDePeca}>`, com os campos de hoje, o `BannerDeErro` de `erroEscrita` e o submit "Criar Peça".
  - Nó: `<PainelDeEscrita key={…} titulo={painel.tipo === 'acrescentarFilho' ? 'Acrescentar sub-Item' : 'Editar nó'} subtitulo={rotuloDoNoDoPainel} testId="painel-de-escrita" aoEnviar={salvarPainel} aoFechar={fecharPainel}>`. A `key` é `${painel.tipo}-${painel.tipo === 'editar' ? painel.no.id : painel.paiId}`, para trocar de nó remontar e devolver o foco ao primeiro campo. O `data-testid` `painel-de-escrita` é preservado (os testes da 8b o buscam).
- Cabeçalho (spec, detalhe 3): `acao` vira um `<div className="flex items-center gap-3">` com o `Botao` "Nova Peça" (só com `podeEscrever` e com o painel de Peça fechado — D5) e o `<span>` do `Id NN` de hoje. O comentário que justifica o `acao` com o Id passa a dizer que ele divide o slot com a ação principal.
- Salvar a Peça com sucesso: fecha o painel de Peça e recarrega a árvore (não há consulta a devolver — spec, "Ciclo de vida").
- `EstadoVazio`: "Use o botão Nova Peça para criar a primeira."

- [ ] **Step 1: Edição mecânica dos testes existentes** (auxiliar `abrirNovaPeca()`): todo teste de criar Peça abre o painel antes. Os testes do painel de nó continuam por `getByTestId('painel-de-escrita')`, **sem edição**, salvo os que dependem de o formulário de Peça estar no documento ao mesmo tempo — o relatório os cita.
- [ ] **Step 2: Testes novos que falham:**
  - `abre em leitura: sem formulario de Peca antes do clique` — `queryByRole('form', { name: 'Nova Peça' })` nulo.
  - `Nova Peca abre o painel e some enquanto ele esta aberto`.
  - `Cancelar da Peca fecha e descarta o digitado`.
  - `criar Peca com sucesso fecha o painel e recarrega a arvore`.
  - `abrir Editar com o painel de Peca aberto fecha o de Peca` e `abrir Nova Peca com o painel de no aberto fecha o de no` — nos dois, um único `<form>` de painel no documento (`getAllByRole('form')` com tamanho 1). *(Review Focus 5.)*
  - `abrir Nova Peca fecha o detalhe do no e a confirmacao de exclusao`.
  - `o Id do agrupamento continua no cabecalho ao lado do botao`.
  - `o painel de no tem o titulo como nome acessivel` — `getByRole('form', { name: 'Editar nó' })`.
  - `quem nao pode escrever nao ve Nova Peca e continua vendo o Id`.
- [ ] **Step 3: Rodar e ver falhar.** `npx vitest run src/pages/AgrupamentoDetalhePage.test.tsx`.
- [ ] **Step 4: Implementar.**
- [ ] **Step 5: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes.
- [ ] **Step 6: Commit.**

```bash
git add web/src/pages/AgrupamentoDetalhePage.tsx web/src/pages/AgrupamentoDetalhePage.test.tsx
git commit -m "feat(web): Agrupamento abre em leitura, Nova Peca no painel, um painel por vez"
```

---

### Task 9: Cartão inteiro clicável — conserto na `ItemDeCadastro` e três telas

**Files:**
- Modify: `web/src/components/ListaDeCadastro.tsx` (`ItemDeCadastro`)
- Test: `web/src/components/ListaDeCadastro.test.tsx`
- Modify: `web/src/pages/FilaPage.tsx`, `web/src/pages/PedidoDetalhePage.tsx`, `web/src/pages/ComponentesPage.tsx`
- Test: `web/src/pages/FilaPage.test.tsx`, `web/src/pages/PedidoDetalhePage.test.tsx`, `web/src/pages/ComponentesPage.test.tsx`

**Interfaces:**
- `ItemDeCadastro` passa a renderizar a `acao`, quando existe, dentro de um wrapper `<div className="relative z-10">` — posicionado e empilhado acima de um overlay absoluto do conteúdo. O `<li>` já é `relative`. A assinatura não muda.
- O comentário da armadilha deixa de ser aviso e passa a dizer o que a primitiva faz e por quê, citando a medição original (o clique no centro do botão devolvia o link), **sem escrever** a classe de empilhamento por extenso no comentário (Global Constraints).
- Nas três telas, o `<Link>` do item ganha as classes do overlay da `LinhaDePedido` (`after:absolute after:inset-0`), mantendo as de foco que já tem. O texto do link não muda (o nome acessível continua o mesmo). Saem os comentários de `ComponentesPage` e `PedidoDetalhePage` que justificam **não** usar o overlay.

- [ ] **Step 1: Testes que falham:**
  - `ListaDeCadastro.test.tsx`: `a acao fica num wrapper posicionado acima do conteudo` — o pai do botão de ação tem as classes `relative` e `z-10` (é o que a suíte consegue afirmar; o efeito real é verificado na Task 11); `sem acao nao cria wrapper`.
  - Nas três telas: `o link do item cobre o cartao inteiro` — o `<a>` tem `after:absolute` e `after:inset-0`; e, onde há botão (`PedidoDetalhePage`, `ComponentesPage`), `o botao do item continua alcancavel pelo papel e nome` e o clique nele dispara a ação, não a navegação (em jsdom isso só prova o handler, e o teste diz isso no nome ou num comentário).
- [ ] **Step 2: Rodar e ver falhar.** `npx vitest run src/components/ListaDeCadastro.test.tsx src/pages/FilaPage.test.tsx src/pages/PedidoDetalhePage.test.tsx src/pages/ComponentesPage.test.tsx`.
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar e ver passar**; `npm test -- --run` e `npm run build` verdes. Conferir que a guarda `semCorForaDaPaleta` continua verde (as classes novas não são de cor).
- [ ] **Step 5: Commit.**

```bash
git add web/src/components/ListaDeCadastro.tsx web/src/components/ListaDeCadastro.test.tsx web/src/pages/FilaPage.tsx web/src/pages/FilaPage.test.tsx web/src/pages/PedidoDetalhePage.tsx web/src/pages/PedidoDetalhePage.test.tsx web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx
git commit -m "feat(web): cartao inteiro clicavel na Fila, no Pedido e em Componentes, sem engolir o botao"
```

---

### Task 10: Documentação

**Files:**
- Modify: `CLAUDE.md` (seção "Interface")
- Modify: `specs/06-roadmap-mvp.md` (seção nova da Fase 1F)
- Modify: `specs/04-fluxos-de-usuario.md` (fluxo 1, a lista de Pedidos — D10)

Conteúdo, medido e não lembrado:

- **`CLAUDE.md`, "Interface":**
  - Um bullet para **`PainelDeEscrita`**, no formato do de `SeletorComBusca`: o gatilho (formulário de escrita aberto sob demanda numa tela de leitura), o que a primitiva guarda e o que não guarda, que não é modal, e quantas telas a consomem — **medido** com `grep -rn "<PainelDeEscrita" web/src --include=*.tsx | grep -v "\.test\."`, com a data e o comando.
  - Um bullet para **`SeletorDeOrdem`**, com o mesmo cuidado, e o lugar dele entre as primitivas de consulta: escolher item de catálogo (`SeletorComBusca`), buscar em lista paginada (`useBuscaPaginada`), restringir por facetas (`FiltroDeDemanda`), **ordenar** (`SeletorDeOrdem`). Dizer que a padrão é "Mais recentes" nas quatro telas de cadastro, e que salvar com sucesso devolve a consulta ao padrão (`voltarAoInicio`).
  - **A regra "Tela nova começa por `<Pagina>`"** ganha: cadastro não fica no topo da tela — vai num `PainelDeEscrita` aberto pela `acao` da `Pagina`.
  - **A exceção do `data-testid`**: remedir com o comando registrado lá (`grep -rn "data-testid" web/src/ --include=*.tsx | grep -v "\.test\."`), dizer a data nova e acrescentar o `painel-de-peca` e o atributo repassado pela primitiva.
- **`specs/06-roadmap-mvp.md`:** seção "Fase 1F — Cadastro sob demanda e ordenação das listas", na posição de execução (depois de "Filtros da demanda e ação em lote", antes da "Fase 3B"), no formato das vizinhas: o que nasceu (as duas primitivas, o `?ordem`, o cartão clicável), a spec e o plano, o critério de pronto, e o que ficou fora (spec, "Fora de escopo").
- **`specs/04-fluxos-de-usuario.md`, fluxo 1, item da lista de Pedidos:** "Novo pedido" abre o cadastro; a ordem ("Mais recentes" por padrão, Número, Cliente) vai na URL como busca e filtros; salvar devolve a lista ao padrão. Conferir se outro fluxo descreve o formulário de cadastro no topo de Setores, Materiais ou Componentes (`grep -n -i "formulário" specs/04-fluxos-de-usuario.md`) e corrigir o que achar.

- [ ] **Step 1: Medir** os três `grep` acima e anotar as saídas no relatório.
- [ ] **Step 2: Escrever** as três alterações.
- [ ] **Step 3: Reler a seção inteira** de cada trecho alterado, não só a frase (contradição entre bullets vizinhos é o defeito recorrente de prosa neste projeto).
- [ ] **Step 4: Commit.**

```bash
git add CLAUDE.md specs/06-roadmap-mvp.md specs/04-fluxos-de-usuario.md
git commit -m "docs: Fase 1F no roadmap, nos fluxos e nas primitivas do CLAUDE.md"
```

---

### Task 11: Verificação no navegador (sem código)

**Dispensa de review — justificativa a escrever no ledger e no relatório ANTES de começar:** o produto desta task é relatório de verificação, não código (primeira classe de dispensa do `CLAUDE.md`). Achado vira task de conserto, que tem gate.

Bancada: API e front de dev no ar (`request_keep_awake` antes — o PC suspende e derruba os servidores), banco com `db/seed-demo.sql`, usuários `admin` (escreve) e um perfil sem escrita (recriar o `operador` à mão se não existir, pelo bloco do `CLAUDE.md`). A 375 px e em desktop:

- [ ] **V1 — As cinco telas abrem em leitura**: nenhum bloco de campos abaixo do título; o botão no cabeçalho, alinhado ao título.
- [ ] **V2 — Painel em cada tela**: abre com o foco no primeiro campo; Cancelar fecha; salvar fecha e o item novo aparece **no topo** (Setores, Materiais, Componentes, Pedidos), mesmo tendo saído de uma busca, de um filtro e de outra ordem; em Pedidos, a URL volta limpa.
- [ ] **V3 — Conflito**: código repetido mantém o painel aberto com a mensagem e "Reativar o existente" dentro dele.
- [ ] **V4 — Editar setor** no painel (D6).
- [ ] **V5 — Agrupamento com os dois painéis**: "Nova Peça" e Editar de um nó, um de cada vez; o Id ao lado do botão no cabeçalho, sem quebrar a 375 px.
- [ ] **V6 — Ordem**: as quatro telas, cada opção; em Pedidos, F5 preserva a ordem, e "voltar" do detalhe de um Pedido também.
- [ ] **V7 — Cartão clicável** na Fila, no detalhe do Pedido (Agrupamentos) e em Componentes: clicar no meio do cartão navega; nas duas com botão, clicar no **centro** do botão executa a ação e **não** navega. *(Review Focus 6.)*
- [ ] **V8 — Perfil sem escrita**: sem botão, sem painel, com o seletor de ordem.
- [ ] **Relatório**: o que foi visto, por item, com captura onde o visual decide. A pergunta "cabe? alinha?" vai ao usuário, que está olhando o painel.

---

## Self-review do plano (feito na escrita)

- **Cobertura da spec:** decisões 1–6 → Tasks 2 e 4–8; 7 → Tasks 3 (D4) e 4–7; 8–11 → Tasks 1, 3–7; 12 → Task 9; "Os detalhes que vão doer" 1 → Tasks 4–6 (Reativar no painel); 2 → Tasks 4–8 (`EstadoVazio`); 3 e 4 → Task 8; "Guardas" → os testes de cada task; "Documentação" → Tasks 1 (05) e 10; "Critério de pronto" → Steps de suíte/build e Task 11.
- **Achado na escrita, corrigido na spec no mesmo commit:** a frase de "Os detalhes que vão doer", item 1, sobre os testes de reativação (D7).
- **Lacunas da spec que este plano decide** e que o usuário confere: D5 (botão some com o painel aberto), D6 (edição de setor no painel, mantendo a consulta), D7 (reativar fecha e devolve ao padrão; o reativado não sobe ao topo), D8 (onde fica o erro de Inativar com o painel fechado), D10 (`04-fluxos`).
