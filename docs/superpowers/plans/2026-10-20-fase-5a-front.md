# Fase 5A — Aferição, expedição e fechamento — Plano 2 de 2 (front)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar à Qualidade a tela "Expedição" (aferir, contar, conserto, pedir liberação, expedir, lote e remessas
recentes), à Gestão a tela "Liberações", ao operador e ao Movimentador o conserto na Fila e nas Tarefas, e às telas de
Pedido os status "Aguardando expedição" e "Concluído" com a data.

**Architecture:** Um cliente novo (`web/src/api/expedicao.ts`) espelha a seção "Contrato JSON novo" do plano 1. Uma
pasta nova `web/src/expedicao/` reúne o que é da expedição (regra pura da tirada, filtro, lote, cartão, painéis,
histórico, permissões). A `ExpedicaoPage` segue o molde da `SeparacaoPage` (lista inteira, `FiltroDeDemanda` no
cliente, `BarraDoLote`); a `LiberacoesPage` segue a mesma, sem lote. A base visual é o esboço aprovado (D10 da
spec).

**Tech Stack:** React + TypeScript (Vite), React Router, Vitest + Testing Library + jsdom, Tailwind.

**Spec:** `docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md` (seções 6 e 7).
**Contrato:** seção "Contrato JSON novo" de `docs/superpowers/plans/2026-10-20-fase-5a-backend.md`, que é a fonte:
campo novo nasce lá primeiro. Este plano roda **depois** do plano 1, na mesma branch.

## Global Constraints

- Gate inteiro do `subagent-driven-development`, base registrada antes de cada task, `scripts/review-package BASE
  HEAD`; artefatos `front-task-N-…` no ledger `.superpowers/sdd/2026-10-20-fase-5a/`.
- **Dispensa de review prevista só para a Task 8**, a conferência no navegador, que é **do usuário** e cujo produto é
  relatório (classe "task cujo produto não é código" do `CLAUDE.md`). A justificativa vai escrita **antes**, no ledger
  e no relatório. **A review de branch inteira só é despachada depois que o usuário fizer a Task 8**, e o controlador
  para e avisa quando chegar nela.
- Padrão de Interface do `CLAUDE.md`, sem exceção: `<Pagina titulo>` dentro do `AppShell`; primitivas de
  `web/src/components/`; nenhum `<button>` cru; nenhum `data-testid` novo; cores só por token; os três estados com teste
  que morre se o estado sumir; gating na **ação**, nunca no link; um `PainelDeEscrita` aberto por vez por cartão, com
  `enviando` e `useDevolverFoco`.
- **Cor (spec 6.5):** "Conforme" `positivo`; "Não conforme" e o aviso de não conforme sem liberação `negativo`; todo o
  resto **neutro** ("Exige relatório", "Sem relatório", "Aferição pendente", "A medir", "Liberada", "Em conserto",
  "Aguardando liberação", os avisos de sem aferição e sem medição). "Atrasado" (`atraso`) e "Pausado" (`atencao`)
  como já são. A pílula "Aguardando expedição" do Pedido continua `neutro`.
- **Textos exatos:**
  - Menu e título **"Expedição"**, subtítulo **"O que está no local de expedição, por prazo de entrega."**; menu e
    título **"Liberações"**, subtítulo **"Unidades não conformes que a Qualidade pediu para expedir."**.
  - Cartão: **"{n} aqui"** e **"de {q} · {e} expedidas"**; pílulas **"Exige relatório"** / **"Sem relatório"** e
    **"Aferição pendente"** / **"Conforme"** / **"Não conforme"**; contagem **"Conformes"**, **"Não conformes"**,
    **"· liberadas pela Gestão"**, **"· em conserto ({Setor})"** (uma linha por Setor), **"· aguardando liberação"**,
    **"· aguardando decisão"**, **"A medir"**.
  - Botões: **"Aferir"**, **"Contar"**, **"Mandar para conserto"**, **"Pedir liberação"**, **"Expedir"**.
  - Painéis: **"Aferir"** (opções **"Conforme"** e **"Não conforme"**, submit **"Registrar aferição"**, dica
    **"Uma unidade medida decide o lote."**); **"Contar as unidades"** (campos **"Conformes"** e **"Não conformes"**,
    dica **"{a} a medir."**, submit **"Registrar contagem"**); **"Mandar para conserto"** (campos **"Setor"** e
    **"Quantidade"**, submit **"Mandar para conserto"**); **"Pedir liberação"** (campos **"Quantidade"** e
    **"Justificativa"**, submit **"Pedir liberação"**); **"Expedir remessa"** (campo **"Quantidade"**, submit
    **"Expedir {q}"**).
  - Aviso do expedir: **"{q} sem aferição"**, **"{q} sem medição"**, **"{q} não conformes sem liberação"**, seguidos de
    **"Dá para expedir assim mesmo; fica registrado."**.
  - Lote: barra **"Expedir"** / **"Expedindo…"**.
  - Seção **"Remessas recentes"**; botão **"Estornar remessa"**; painel **"Estornar remessa"** com uma linha por Peça e
    **"Reabre o Pedido {número}."** para cada Pedido concluído que ela reabre; submit **"Estornar"**.
  - Vazios: **"Nada no local de expedição."** e, com filtro, **"Nenhuma Peça com esses filtros."**; **"Nenhuma
    remessa nos últimos 30 dias."**; **"Nenhuma liberação pendente."**. Erros de carga: **"Não foi possível carregar a
    expedição."**, **"Não foi possível carregar as liberações."**.
  - Liberações: botão **"Responder"**; painel **"Liberar para expedir"**, campos **"Quantidade liberada"** e
    **"Observação (opcional)"**; submits **"Liberar {q}"** e **"Recusar tudo"** (liberar zero).
  - Fila: seção **"Conserto"**; botão **"Terminar conserto"**; linha pronta **"{q} pronto, aguardando levar à
    expedição"**. Tarefas: seção **"Conserto"**, linhas **"Levar ao conserto em {Setor}"** e **"Levar ao local de
    expedição"**.
  - Pedido: **"Concluído em {data}"**. Edição do nó: a caixa **"Requer relatório dimensional"**, só na Peça.
  - Quantidade formatada por `formatarQuantidade`; campo de quantidade lido por `web/src/execucao/quantidade.ts`.
- Teste de tela: `// @vitest-environment jsdom`, `afterEach(cleanup)`, mocks por `web/src/testes/api.ts`, elemento
  achado por papel e nome acessível.
- `npm test -- --run` **e** `npm run build` a cada task. Na máquina local, `npx vitest run --maxWorkers=2`.
- Citação por nome; comentário não cita ledger, brief nem "Task N". Texto de tela com acento.
- `git pull` antes de todo commit.

## Review Focus

1. **Duplo toque no "Expedir {q}"** no celular — duas remessas. Esperado: o botão trava com `enviando` e um `ref`
   guarda a função, como `FormularioDeQuantidade` (Task 3).
2. **Contagem com vírgula** (`0,5`) — o campo manda `0.5`, nem `5` nem `NaN` (Task 3).
3. **409 `SaldoInsuficiente`** porque outra pessoa expediu com o painel aberto — a frase do servidor aparece dentro do
   painel e a lista recarrega (Task 4).
4. **A remessa que conclui o Pedido tira o cartão da lista com o painel aberto** — o foco cai no título da tela, não no
   `<body>` (Task 4).
5. **Gestão abre a `/expedicao`** — lê tudo e não vê nenhum botão de escrita; a Qualidade abre a `/liberacoes` e não vê
   "Responder" (Tasks 4 e 5).

## Decisões deste plano

- **Q1 — "Liberações" é tela própria** (`/liberacoes`), com link no menu para todos (leitura é de todos) e a ação só
  para a Gestão. A spec deixou tela própria ou seção; tela própria é o que o esboço mostrou.
- **Q2 — A prévia do aviso é calculada no front** por `tirarDaRemessa` e `avisosDaRemessa`
  (`web/src/expedicao/tiradaDaRemessa.ts`), cópia da ordem C10 do backend; o `201` traz os avisos de verdade, e a tela
  não os mostra de novo (a prévia já foi vista). Divergência entre as duas é defeito, e o teste de cada lado fixa a
  mesma tabela.
- **Q3 — Os códigos novos entram em `CodigoDeErroDaExecucao`** com tradução no `TRADUCAO_DOS_CODIGOS`, como os da
  separação.
- **Q4 — O conserto entra no lote das Tarefas** como mais um item com origem, porque `POST /entregas` já leva a origem.
  Na Fila, "Terminar conserto" é individual (a rota não tem lote, P6 do plano 1).
- **Q5 — Permissões num hook próprio**, `usePermissoesDaExpedicao` (`registrar`, `responder`,
  `podeEstornar(autorId)`), no molde de `usePermissoesDaSeparacao`.

## Mapa de arquivos

| Arquivo | Task | Responsabilidade |
|---|---|---|
| `api/expedicao.ts`, `api/execucao.ts`, `api/cadastros.ts`, `api/estrutura.ts`, `api/erros.ts`, `execucao/formatacao.ts` | 1 | clientes, tipos, rótulos |
| `auth/permissoes.ts`, `auth/permissoesEspelhamOBackend.test.ts`, `expedicao/usePermissoesDaExpedicao.ts` | 1 | perfis |
| `expedicao/{tiradaDaRemessa,filtroDaExpedicao,loteDaExpedicao}.ts` | 2 | regras puras do front |
| `expedicao/{CartaoDaPeca,PainelDeAfericao,PainelDeContagem,PainelDeConserto,PainelDePedidoDeLiberacao,PainelDeRemessa,HistoricoDaExpedicao}.tsx` | 3 | cartão e painéis |
| `pages/ExpedicaoPage.tsx`, `expedicao/RemessasRecentes.tsx`, `App.tsx`, `components/AppShell.tsx` | 4 | a tela da Qualidade |
| `pages/LiberacoesPage.tsx` | 5 | a tela da Gestão |
| `pages/FilaDoSetorPage.tsx`, `pages/TarefasPage.tsx`, `execucao/loteDaFila.ts` | 6 | conserto no chão |
| `pages/PedidoDetalhePage.tsx`, `pages/AgrupamentoDetalhePage.tsx` | 6 | status, data e a marca do relatório |
| `specs/04-fluxos-de-usuario.md`, `CLAUDE.md`, `db/massa-conferencia-fase-5a.sql` | 7 | fluxos, contagens e massa |
| ledger `conferencia/` | 8 | conferência do usuário |

---

### Task 1: Clientes, tipos, rótulos, códigos e permissões

**Modelo:** sonnet.

**Files:**
- Create: `web/src/api/expedicao.ts`, `web/src/api/expedicao.test.ts`
- Modify: `web/src/api/execucao.ts` (`Posicao`, `TipoDeMovimentacao`, `MovimentacaoDto`, `LinhaDeConserto`,
  `TarefaDeConserto`, `FilaDoSetorDto.conserto`, `TarefasDto.consertos`, `OrigemDaEntrega`, `terminarConserto`)
- Modify: `web/src/api/cadastros.ts` (`dataConclusao` em Pedido e Agrupamento), `web/src/api/estrutura.ts`
  (`requerRelatorioDimensional` na edição do nó)
- Modify: `web/src/api/erros.ts`, `web/src/api/erros.test.ts`
- Modify: `web/src/execucao/formatacao.ts`, `web/src/execucao/formatacao.test.ts`
- Modify: `web/src/auth/permissoes.ts`, `web/src/auth/permissoes.test.ts`, `web/src/auth/permissoesEspelhamOBackend.test.ts`
- Create: `web/src/expedicao/usePermissoesDaExpedicao.ts`, `web/src/expedicao/usePermissoesDaExpedicao.test.tsx`

**Interfaces:**
- Produces (`api/expedicao.ts`), tipos campo a campo do contrato: `SituacaoDaAfericao`, `AfericaoDto`,
  `ContagemDto`, `LiberacaoDto`, `LiberacaoPendenteDto`, `PosicoesDaPecaDto`, `ConsertoEmCursoDto`,
  `PecaNaExpedicaoDto`, `PedidoNaExpedicaoDto`, `ExpedicaoDto`, `LinhaDaRemessaDto`, `RemessaDto`,
  `AvisoDaPecaDto`, `ResultadoDaRemessaDto`, `RegistroDaExpedicaoDto`; funções:

```ts
export function obterExpedicao(): Promise<ExpedicaoDto>
export function obterHistoricoDaExpedicao(pecaId: number): Promise<RegistroDaExpedicaoDto[]>
export function listarRemessasRecentes(): Promise<RemessaDto[]>
export function listarLiberacoesPendentes(): Promise<LiberacaoPendenteDto[]>
export function aferir(pecaId: number, resultado: 'Conforme' | 'NaoConforme'): Promise<AfericaoDto>
export function contar(pecaId: number, conformes: number, naoConformes: number): Promise<ContagemDto>
export function mandarParaConserto(pecaId: number, setorId: number, quantidade: number): Promise<MovimentacaoDto>
export function pedirLiberacao(pecaId: number, quantidade: number, justificativa: string): Promise<LiberacaoDto>
export function responderLiberacao(id: number, quantidadeLiberada: number, observacao: string | null): Promise<LiberacaoDto>
export function expedir(itens: { estruturaItemId: number; quantidade: number }[]): Promise<ResultadoDaRemessaDto>
export function estornarAfericao(id: number): Promise<AfericaoDto>
export function estornarContagem(id: number): Promise<MovimentacaoDto[]>
export function estornarLiberacao(id: number): Promise<MovimentacaoDto[]>
export function estornarRemessa(id: number): Promise<MovimentacaoDto[]>
export function estornarRegistroDaExpedicao(r: RegistroDaExpedicaoDto): Promise<unknown>   // escolhe a rota pelo tipo; Conserto → estornarMovimentacao
```

  Todas as escritas chamam `avisarQueOLivroMudou` (mexem no livro), menos `aferir` e `estornarAfericao`.
  `permissoes.ts`: `expedicao: ['Qualidade', 'Administrador']`, `liberacao: ['Gestao', 'Administrador']`,
  `estornoDaExpedicao: ['Qualidade', 'Gestao', 'PCP', 'Administrador']`, e `estorno` ganha `'Qualidade'`; o mapa da
  guarda de espelhamento ganha `ExpedicaoController.cs`, `LiberacaoController.cs` e `EstornoDaExpedicaoController.cs`.
  `usePermissoesDaExpedicao(): { registrar: boolean; responder: boolean; podeEstornar(autorId: number): boolean }`.

- [ ] **Step 1: Testes que falham.**

```ts
// api/expedicao.test.ts
it('expedir manda POST /remessas com os itens e devolve remessa e avisos')
it('estornarRegistroDaExpedicao escolhe a rota pelo tipo')   // Afericao, Contagem, Conserto, PedidoDeLiberacao, RespostaDeLiberacao
it('falha de escrita vira ErroDeApi com o codigo')            // 409 SaldoInsuficiente
// formatacao.test.ts
it.each([
  ['AguardandoConserto', '1 aguardando levar ao conserto em Dobra'],
  ['EmConserto', '1 em conserto em Dobra'],
  ['ConsertoPronto', '1 pronto do conserto em Dobra'],
  ['Conforme', '1 conforme no local de expedição'],
  ['NaoConforme', '1 não conforme no local de expedição'],
  ['AguardandoLiberacao', '1 aguardando liberação'],
  ['Liberada', '1 liberada pela Gestão'],
  ['Expedido', '1 expedido'],
])('rotuloDoSaldo de %s', …)
it('rotuloDoTipo traduz os seis tipos novos')   // Medição, Conserto, Pedido de liberação, Liberação, Recusa, Expedição
// erros.test.ts: um caso por código de CodigosDaExpedicao, com a frase de reserva
// permissoes.test.ts / usePermissoesDaExpedicao.test.tsx: os perfis exatos acima
```

- [ ] **Step 2: Rodar e ver falhar** — `cd web && npm test -- --run src/api src/execucao/formatacao src/auth src/expedicao`.
- [ ] **Step 3: Implementar.** `rotuloDoLocal` ganha as mesmas oito posições ("conserto em Dobra", "local de
  expedição (conforme)", …), com o `switch` exaustivo que o `tsc` confere.
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`. Esperado: verde e build ok.
- [ ] **Step 5: Commit** — `feat(fase-5a): cliente, tipos e permissoes da expedicao no front`.

### Task 2: Regras puras do front — tirada, filtro e lote

**Modelo:** opus (a tirada é a cópia da C10, e as duas têm de bater).

**Files:**
- Create: `web/src/expedicao/{tiradaDaRemessa,filtroDaExpedicao,loteDaExpedicao}.ts` e os `.test.ts` de cada um

**Interfaces:**
- Consumes: `PosicoesDaPecaDto`, `SituacaoDaAfericao`, `PecaNaExpedicaoDto` (Task 1); `casaComFiltro`
  (`components/FiltroDeDemanda`).
- Produces:

```ts
export interface Tirada { posicao: 'Liberada' | 'Conforme' | 'NaExpedicao' | 'NaoConforme'; quantidade: number }
export function tirarDaRemessa(p: PosicoesDaPecaDto, quantidade: number): Tirada[] | null
export function avisosDaRemessa(s: SituacaoDaAfericao, tiradas: Tirada[]): { tipo: 'SemAfericao' | 'SemMedicao' | 'NaoConformeSemLiberacao'; quantidade: number }[]
export const CHAVES_DA_EXPEDICAO: readonly ['pedido', 'afericao']
export function facetasDaExpedicao(pedidos: PedidoNaExpedicaoDto[]): Faceta[]   // Pedido (número) e Aferição (SemRelatorio|Pendente|Conforme|NaoConforme, com os rótulos da tela)
export function filtrarExpedicao(pedidos: PedidoNaExpedicaoDto[], selecao: SelecaoDeFiltro): PedidoNaExpedicaoDto[]   // Pedido sem Peça que casa sai
// loteDaExpedicao: no molde de loteDaSeparacao (alternar, digitar, marcarTodos, reconciliar, camposInvalidos, itensDoLote)
```

- [ ] **Step 1: Testes que falham** — `tiradaDaRemessa.test.ts` repete **a mesma tabela** de
  `Tirar_segue_Liberada_Conforme_NaExpedicao_NaoConforme`, `Tirar_nunca_tira_AguardandoLiberacao`,
  `Tirar_acima_do_expedivel_devolve_null` e `Aviso_por_situacao_e_origem` do plano 1 (mesmos números); o filtro testa
  OU dentro da faceta e E entre facetas pelo `casaComFiltro`; o lote testa marcar, digitar com vírgula, reconciliar
  depois de recarga (Peça que saiu da lista sai da seleção, com `SAIU_DO_LOTE`) e o campo acima do expedível como
  inválido.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(fase-5a): tirada, filtro e lote da expedicao`.

### Task 3: O cartão da Peça, os painéis e o histórico

**Modelo:** opus.

**Files:**
- Create: `web/src/expedicao/{CartaoDaPeca,PainelDeAfericao,PainelDeContagem,PainelDeConserto,PainelDePedidoDeLiberacao,PainelDeRemessa,HistoricoDaExpedicao}.tsx` e os `.test.tsx` de cada um

**Interfaces:**
- Consumes: Tasks 1 e 2; `PainelDeEscrita`, `Campo`, `Botao`, `Pilula`, `BannerDeErro`, `ListaRecolhivel`.
- Produces: `CartaoDaPeca` (props: `peca`, `setores` para o conserto, `aoRegistrar(): void` para recarregar, e o
  controle de marcação do lote vindo da tela); cada painel recebe `peca`, `aoFechar`, `aoConcluir` e faz a própria
  escrita, com o erro de escrita dentro dele; `HistoricoDaExpedicao` (props `pecaId`) carrega o histórico ao abrir
  (dentro de uma `ListaRecolhivel` "Histórico (N)") e estorna por `estornarRegistroDaExpedicao`, sem botão na remessa.

- [ ] **Step 1: Testes que falham.**

```tsx
// CartaoDaPeca.test.tsx
it('mostra "6 aqui" e "de 10 · 0 expedidas" e as pílulas da situação')
it('depois de não conforme mostra a contagem com uma linha por Setor de conserto')
it('pílula Conforme usa positivo e Não conforme usa negativo; as outras neutras')
it('Peça sem relatório não oferece Aferir nem Contar')
it('Contar só aparece com aferição não conforme e algo a medir')
it('Mandar para conserto e Pedir liberação só aparecem com não conforme aguardando decisão')
it('sem permissão de registrar não há nenhum botão de escrita')
// PainelDeRemessa.test.tsx
it('abre com o expedível preenchido')
it('mostra a prévia dos avisos antes de confirmar')           // 2 não conformes sem liberação, em negativo
it('Review Focus: duplo toque grava uma remessa só')
it('409 SaldoInsuficiente aparece dentro do painel')
// PainelDeContagem.test.tsx
it('Review Focus: aceita vírgula e manda 0.5')
it('mostra a dica "{a} a medir."')
// PainelDeAfericao / PainelDeConserto / PainelDePedidoDeLiberacao: o corpo exato da requisição e o erro dentro do painel
// HistoricoDaExpedicao.test.tsx: os rótulos de cada tipo; Estornar só onde estornavel; nenhum Estornar na Remessa
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** O seletor de Setor do conserto é `Campo` com `<select>` dos Setores **ativos** (a lista
  de Setores não é catálogo paginado, então não é `SeletorComBusca`).
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(fase-5a): cartao da Peca, paineis e historico da expedicao`.

### Task 4: A tela "Expedição"

**Modelo:** sonnet. Molde: `SeparacaoPage`.

**Files:**
- Create: `web/src/pages/ExpedicaoPage.tsx`, `web/src/pages/ExpedicaoPage.test.tsx`
- Create: `web/src/expedicao/RemessasRecentes.tsx`, `web/src/expedicao/RemessasRecentes.test.tsx`
- Modify: `web/src/App.tsx` (rota `/expedicao`), `web/src/components/AppShell.tsx` (e o teste dele: "Expedição" e
  "Liberações" entram depois de "Separação")

**Interfaces:**
- Consumes: Tasks 1–3; `useSelecaoNaUrl`, `useDevolverFoco`, `FiltroDeDemanda`, `BarraDoLote`.
- Produces: `ExpedicaoPage`; `RemessasRecentes` (props `aoEstornar(): void`).

- [ ] **Step 1: Testes que falham.**

```tsx
it('mostra carregando, depois os Pedidos na ordem que a API manda')
it('vazio sem filtro diz "Nada no local de expedição."')
it('vazio com filtro diz "Nenhuma Peça com esses filtros."')
it('erro de carga mostra "Não foi possível carregar a expedição."')
it('o filtro por Aferição vai para a URL')
it('o lote expede duas Peças numa remessa só e recarrega')
it('Review Focus: 409 do painel mostra a frase dentro dele e recarrega a lista')
it('Review Focus: a remessa que tira o cartão da lista devolve o foco ao título')
it('Review Focus: Gestão vê a tela sem botão de escrita')
// RemessasRecentes.test.tsx
it('lista as remessas com as Peças e quantidades')
it('Estornar remessa abre o painel com as Peças e "Reabre o Pedido {número}."')
it('sem permissão não mostra Estornar remessa')
it('vazio diz "Nenhuma remessa nos últimos 30 dias."')
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** "Reabre o Pedido {número}." aparece uma vez por Pedido distinto das linhas com
  `pedidoConcluido` verdadeiro. Depois do estorno, recarrega a lista e as remessas.
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(fase-5a): tela da Expedicao`.

### Task 5: A tela "Liberações"

**Modelo:** sonnet.

**Files:**
- Create: `web/src/pages/LiberacoesPage.tsx`, `web/src/pages/LiberacoesPage.test.tsx`
- Modify: `web/src/App.tsx` (rota `/liberacoes`)

- [ ] **Step 1: Testes que falham.**

```tsx
it('lista os pedidos com Peça, Pedido, quantidade, justificativa, quem pediu e quando')
it('os três estados: carregando, "Nenhuma liberação pendente." e o erro de carga')
it('Liberar 2 manda quantidadeLiberada 2 e tira o pedido da lista')
it('Recusar tudo manda quantidadeLiberada 0')
it('quantidade acima da pedida é recusada pelo servidor e a frase fica no painel')
it('Review Focus: Qualidade vê a lista sem Responder')
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Cartão por pedido, com "Atrasado" no Pedido quando vier; "Responder" sob
  `usePermissoesDaExpedicao().responder`.
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(fase-5a): tela de Liberacoes da Gestao`.

### Task 6: Conserto na Fila e nas Tarefas; status e marca do relatório

**Modelo:** sonnet.

**Files:**
- Modify: `web/src/pages/FilaDoSetorPage.tsx` (e teste), `web/src/pages/TarefasPage.tsx` (e teste), `web/src/execucao/loteDaFila.ts` (e teste)
- Modify: `web/src/pages/PedidoDetalhePage.tsx` (e teste), `web/src/pages/AgrupamentoDetalhePage.tsx` (e teste)

- [ ] **Step 1: Testes que falham.**

```tsx
// FilaDoSetorPage
it('seção Conserto mostra o que está em conserto com Terminar conserto')
it('Terminar conserto manda POST /estrutura/{id}/terminos-de-conserto com o Setor e a quantidade')
it('o pronto do conserto aparece como "{q} pronto, aguardando levar à expedição", sem ação')
it('sem permissão de apontamento não há Terminar conserto')
// TarefasPage
it('seção Conserto lista a ida e a volta com os rótulos exatos')
it('entregar um conserto manda a origem AguardandoConserto com o Setor e sem ordem')
it('o conserto entra no lote de entrega junto dos Itens prontos')
// PedidoDetalhePage
it('Pedido concluído mostra "Concluído em {data}"')
// AgrupamentoDetalhePage
it('editar uma Peça oferece Requer relatório dimensional e manda o valor')
it('editar um Item não oferece a caixa')
it('409 ComAfericao aparece no painel do nó')
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** A seção "Conserto" da Fila vem depois de "Aguardando coleta"; a das Tarefas, antes dos
  Kits. `loteDaFila` ganha o item de conserto com a origem dele.
- [ ] **Step 4: Rodar** `npm test -- --run` e `npm run build`.
- [ ] **Step 5: Commit** — `feat(fase-5a): conserto na Fila e nas Tarefas, status do Pedido e marca do relatorio`.

### Task 7: Fluxos, contagens do `CLAUDE.md` e massa da conferência

**Modelo:** sonnet. Produto é texto e massa; tem review.

**Files:**
- Modify: `specs/04-fluxos-de-usuario.md`, `CLAUDE.md`
- Create: `db/massa-conferencia-fase-5a.sql` (o `zerar-conf-5a.sql` vai para o ledger, como na Fase 4)

- [ ] **Step 1: `04`** — o fluxo da Qualidade na expedição (aferir, contar, conserto, pedir liberação, expedir, lote,
  estornar remessa), o da Gestão nas liberações, e o conserto nos fluxos do Operador e do Movimentador.
- [ ] **Step 2: `CLAUDE.md`, seção Interface** — remedir com os comandos registrados lá e escrever a data nova, a
  métrica e quem mudou: `<PainelDeEscrita`, `useDevolverFoco(`, `<FiltroDeDemanda`, `<BarraDoLote`, `<ListaRecolhivel`;
  um bullet novo **"A expedição"** no molde do da separação (pasta `web/src/expedicao/`, o hook de permissões, a regra
  de cor da spec 6.5, a prévia do aviso como cópia da C10). A contagem da suíte do backend e do front, remedida, com a
  data e o commit.
- [ ] **Step 3: Massa** — `db/massa-conferencia-fase-5a.sql`, idempotente, chaveada por código (`CONF-5A`): um Pedido
  com dois Agrupamentos e quatro Peças já no local de expedição (uma sem relatório, uma que exige, **uma copiada do
  catálogo sem descrição própria**, uma de outro Agrupamento), outro Pedido atrasado, e os Setores de conserto. O
  roteiro do usuário (o fluxo da seção 7 da spec, passo a passo, com os usuários `qualidade`, `gestao`, `movimentador`,
  `operador` e `pcp`) vai em `.superpowers/sdd/2026-10-20-fase-5a/conferencia/roteiro.md`.
- [ ] **Step 4: Rodar** a massa duas vezes no banco local (a segunda sem mudança) e as duas suítes.
- [ ] **Step 5: Commit** — `docs(fase-5a): fluxos, contagens e massa da conferencia`.

### Task 8: Conferência no navegador, pelo usuário

**Modelo:** nenhum: a task é do usuário. **Dispensa de review escrita antes**, no ledger e no relatório (classe "task
cujo produto não é código").

- [ ] **Step 1:** O controlador para e avisa o usuário que chegou a conferência, com o roteiro e o comando da massa.
  Captura por agente pode preparar a conferência; **não** a substitui nem destrava a review de branch.
- [ ] **Step 2:** O usuário confere e decide; os achados viram `conferencia/achados.md`, com a palavra dele em cada um.
- [ ] **Step 3:** Só então: review de branch inteira (modelo mais capaz) e o fix wave, nesta ordem.

## Self-review (feito ao escrever este plano)

- **Cobertura da spec, seção 6:** 6.1 → Tasks 2, 3, 4; 6.2 → 5; 6.3 → 6; 6.4 → 6; 6.5 → Global Constraints e os testes
  de cor da Task 3; 6.6 → 4. Seção 7 (front e conferência) → as Tasks 1–6 e 8; a massa com Peça copiada do catálogo →
  7. Seção 9 → 7 (`04`, `CLAUDE.md`).
- **Tipos conferidos com o plano 1:** os nomes do "Contrato JSON novo", `TerminoDeConsertoDto` (`setorId`, `quantidade`),
  as origens novas da entrega, `conserto` na Fila e `consertos` nas Tarefas.
- **Ajustes da conferência:** não há task para eles de antemão; se vierem, entram como Task 9 no molde da Task 8 do
  plano de front da Fase 4.
