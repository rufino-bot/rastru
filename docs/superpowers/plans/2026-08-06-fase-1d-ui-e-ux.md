# Fase 1D — Identidade visual e UX — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao sistema um padrão visual e de interação único — tokens, primitivas e shell de navegação — e aplicá-lo às 7 telas existentes, de modo que toda tela futura nasça dentro do padrão em vez de fora dele.

**Architecture:** Fase de **front puro**. Nada em `src/` (backend) muda. Três camadas novas em `web/src/`: (1) tokens de tema em `index.css` via `@theme` do Tailwind v4, com prova automatizada de contraste; (2) primitivas de UI à mão em `web/src/components/`, sem biblioteca de terceiros; (3) um shell de aplicação (`AppShell`) como rota de layout do React Router. As 7 telas passam a consumir as três. Antes de qualquer reescrita de markup entra a **rede**: teste de fumaça por tela e as dívidas de comportamento (`useBuscaPaginada`, `ErroDeApi`).

**Tech Stack:** React 19 + TypeScript (Vite 8), React Router 7, Tailwind CSS v4 (já instalado), Vitest 4 + `@testing-library/react` 16 (já instalados), oxlint. **Zero dependência nova** — nem de UI, nem de teste.

## Global Constraints

Valem para **todas** as tasks. Em conflito entre este plano e o adendo `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md`, **o adendo ganha**.

- **Spec de origem:** `docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md`. **Leia inteira antes da sua task** — ela tem o *porquê* de decisões que aqui aparecem só como valor.
- **Adendo obrigatório:** `docs/superpowers/plans/2026-07-28-fase-1a-convencoes-obrigatorias.md`. Desta fase interessam **F1–F6** (front); B1–B14 são de backend e não se aplicam, porque **nenhuma task toca em `src/`**.
- **Baseline MEDIDA em 2026-08-06, no HEAD desta branch** (`90dde7c`, rebase sobre `main` = `0513224`, que já contém a 1B):
  - `cd web && npm test` → **96 testes / 6 arquivos**, todos verdes.
  - `cd web && npm run build` → limpo (`dist/assets/index-*.js` ≈ 256,8 kB; `index-*.css` ≈ 9,9 kB).
  - `cd web && npm run lint` → **1 warning, e ele é alheio**: `src/auth/AuthContext.tsx:48 react(only-export-components)`. Não conserte — não é desta fase.
  - Backend: **não re-medido de propósito**. Esta fase não toca em `src/` nem em `tests/`; `dotnet test` não faz parte do ciclo de nenhuma task aqui. Se você mudou algo em `src/`, você saiu do escopo.
  - **Se a sua contagem inicial divergir de 96, pare e reporte.** Contagem de brief desatualizada já mordeu três tasks neste projeto.
- **Os totais absolutos de teste que aparecem daqui para baixo são ESTIMATIVAS, calculadas sem executar nada — e a Task 1 já provou que erram.** O plano previa 111 ao fim dela; a implementação mediu **110**, porque a aritmética somou 4 testes de `LoginPage` onde o código do próprio plano define 3. O que **vincula** é: (a) a baseline que você mede no início da sua task tem de bater com o número que a task anterior **reportou** — não com o número escrito aqui; e (b) o **delta** que a sua task acrescenta. Se o total divergir da estimativa e o delta estiver certo, **reporte o número real e siga** — não invente teste nem apague teste para fechar a conta. Números conhecidos e medidos: **início da fase 96 · fim da Task 1 115**.

> **Como a Task 1 chegou a 115 — leia antes de "corrigir" qualquer número aqui.** A estimativa dizia 111 e estava errada; a implementação mediu **110**; três rodadas de review levaram a fase a 115. A ordem dos fatos: estimativa 111 → implementação **110** → 1º fix pass +1 (`PedidoDetalhePage`, desfecho `NaoEncontrado`, achado M1) = **111** → 2º fix pass +4 (`PedidoNaoAberto`; `salvar()` nos ramos de sucesso e conflito; login bem-sucedido — achados A2 e A3, este último uma **ampliação de escopo autorizada pelo usuário**) = **115** → 3º fix pass +0 (só asserções acrescentadas a testes existentes, achados B1-B4). Medição final: `npx vitest run` = 115 em 10 arquivos, saída limpa, commit `6af85ca`. **Não deduza contagem por aritmética — meça.** Este plano já produziu dois números errados por soma feita de cabeça, e um deles quase travou a Task 2 por alarme falso.
- **`dotnet test` NÃO roda o front.** A suíte desta fase é `cd web && npm test` (= `vitest run`, não é watch).
- **Rode `npm run build` além de `npm test`.** Erro de tipo em `.test.tsx` quebra o build **sem** quebrar o `npm test` — o Vitest não faz typecheck e o `tsconfig.app.json` inclui `src` inteiro. Uma task não está pronta com build vermelho.
- **Ordem obrigatória da fase (spec §10): rede antes do markup.** As Tasks 1–3 são rede e comportamento; a primeira linha de markup novo só aparece na Task 4. Não antecipe.
- **Critério de pronto nunca é "os testes passam"** — é *"antes a mutação não matava nada, depois mata, e mata só o esperado"*. Cada task lista as mutações que precisa matar; **meça-as você mesmo** e reporte o número de mortes por mutação.
- **Ao verificar uma mutação, afirme que o texto ANTIGO SUMIU** do arquivo (`grep -c` caindo), nunca só que o novo apareceu. Deu **três falsos zeros** na fase passada.
- **Reverter mutação com edição inversa, nunca `git checkout`** — a árvore tem sujeira alheia permanente.
- **`git status` tem sujeira alheia e permanente:** `.claude/settings.local.json` modificado e `.claude/settings.json` untracked. **Não commite nenhuma das duas.**
- **Não edite fonte com `Set-Content` do PowerShell 5.1** — ele corrompe UTF-8 (acentuação) e a suíte fica verde mesmo assim. Use as ferramentas de edição de arquivo.
- **O repositório é PÚBLICO** (`github.com/rufino-bot/rastru`). Varredura de segredo antes de qualquer push é passo obrigatório. **A varredura formal da fase é a Task 13**, e ela é sobre o **histórico** (`git log -p origin/main..HEAD`), não sobre a árvore: a branch é local, nunca foi empurrada, e o push publica os commits todos de uma vez — segredo apagado num commit posterior continua no anterior. Um `git diff` limpo **não** substitui isso.
- **Política de commit — mudou em 2026-08-14, por decisão do usuário.** *"Os commits na remote podem ser feitos por vocês, só o PR que eu aprovo manualmente, só garanta que nada foi perdido no commit."* Ou seja: **o commit é nosso; abrir ou mesclar PR é dele.** A contrapartida não é opcional — **todo Step de commit deste plano carrega a mesma sub-etapa de verificação pós-commit** (`git status --short` sem sobra; `git show --stat HEAD` batendo com o que a task produziu; nenhum `.claude/settings*.json` no commit; varredura de segredo antes do push). O texto canônico dela está no **Step 6 da Task 9A**, e está repetido nos Steps de commit das Tasks 9B, 10, 11 e 12. **As Tasks 1–8 já estão commitadas e não foram reescritas** — a política nova vale para o que ainda vai ser implementado.
- **⚠️ NÃO EXISTE MAIS TOTAL ABSOLUTO CONFIÁVEL NESTE PLANO. Use `baseline que você MEDIU no Step 1 + o delta da sua task`.** Os deltas são estáveis e estão abaixo; os totais são derivados e apodrecem a cada fix pass.

  | Task | Delta | Total SE a baseline for a esperada |
  |---|---|---|
  | 4 | **+23** na entrega, **+28** com o fix pass da review | **172 — MEDIDO** (144 + 22 do plano + 1 da guarda do M8 = 167 na entrega; + 5 do fix pass de `3768888`) |
  | 5 | **+23** (recontado) | **200 — MEDIDO** (195 na entrega; + 5 do fix pass da review, `8d75c53`) |
  | 6 | **+23** (recontado) | 223 (= 200 medidos + 23) |
  | 7 | **+29** na entrega | **259 — MEDIDO** (230 medidos ao iniciar a task + 29) |
  | 7 (fix pass) | **+9** | **268 — MEDIDO** (259 + 9 dos achados da review — I3, I2, I4, m5, m6, m7 × 2, ver `.superpowers/sdd/fase1d-task-7-fix-report.md`) |
  | passe de primitivas (pré-Task 8) | **+4** | **272 — MEDIDO** (268 + 4 — caminho C do usuário: fechar defeitos de `Pagina`/`Botao`/`Campo`/`EstadoVazio` antes da Task 8 consumi-los; ver `.superpowers/sdd/fase1d-passe-primitivas-report.md`) |
  | 8 | **+11** na entrega | **283 — MEDIDO** (272 + 11) |
  | 8 (fix pass) | **+7** | **290 — MEDIDO** (283 + 7 dos achados da review — C1 ×3, I2, I3 ×2, M10; ver `.superpowers/sdd/fase1d-task-8-fix-report.md`). **290 é a baseline da Task 9A**, e foi RE-MEDIDA em 2026-08-13 (`Tests 290 passed (290)` / 26 arquivos). |
  | **9A** (re-layout) | **+7, −0** (3 `it(` do Step 3 + a sonda do `EstadoVazio` da decisão U1 + o teste de `perfil = 'PCP'` que fecha a M9 + **os dois que fecham X3 e X6**, decisão do usuário de 2026-08-14) | **297 — `295 MEDIDO + 2 DERIVADO`** (o pré-flight de 2026-08-14 executou a task com os 5 primeiros e leu `295 passed`; os de X3/X6 entraram depois e nunca rodaram). É **só soma**: MEDIDO, os 34 testes da tela fecham **34/34 verdes só com adaptação** — **nenhuma remoção é necessária**, aqui ou na 9B. |
  | **9B** (adoção do hook) | **−8, +3** (saem os 7 da decisão U3 mais o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso`, de `ComponentesPage.test.tsx`, pela decisão U2 — **citação por nome, não por linha: ver a caixa de convenção na abertura da 9B**; entram o teste da guarda do `catch` em `useBuscaPaginada.test.tsx`, o do debounce na tela e o da sobrevivência do erro de escrita à recarga) | **292 — DERIVADO** (297 − 8 + 3). **A única task desta fase que subtrai.** **A colisão `290 == 290` que esta linha registrava ACABOU** com o `+2` da 9A: a fase anda por **290 → 297 → 292**, três números distintos, e um total inesperado volta a ser sinal em vez de ruído. **A prova por arquivo continua exigida assim mesmo — `ComponentesPage.test.tsx` = 35 e `useBuscaPaginada.test.tsx` = 19** —, porque o total é derivado e apodrece, e o par por arquivo é medição direta. |
  | **13** (varredura de segredo no histórico) | **0** | **inalterado** — a task não escreve código nem teste: lê o histórico do git e devolve um veredito. **Quem somar em cima soma 0.** Está aqui explicitamente para a próxima contagem não herdar um `+?`. |

  **A Task 9 foi PARTIDA em 9A e 9B em 2026-08-13, por decisão do usuário** sobre a recomendação medida do pré-flight (`.superpowers/sdd/fase1d-task-9-preflight.md`; a reescrita está em `.superpowers/sdd/fase1d-task-9-split-report.md`). A linha única que estava aqui dizia "entre 284 e 294, X ainda NÃO decidido" e vinha com a instrução de **não propagar nenhum extremo**: essa instrução perdeu o motivo — o X foi decidido (U3, X = 7, mais o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` da U2 = 8 saídas, todas na 9B) e cada uma das duas tasks tem agora um delta próprio e vinculante. **As Tasks 10, 11 e 12 não são afetadas:** os Steps 1 delas medem baseline **por arquivo** (`PedidoDetalhePage` 4, `HomePage` 3, `LoginPage` 5), não o total da suíte — conferido em 2026-08-13.

  *(O delta da Task 4 era `+20` e foi corrigido para `+22` no pré-flight de 2026-08-08 — **quarto erro de contagem deste plano** —, contando os `it(` do Step 3 dela um a um. Ao fechar, a task entregou **+23** (a guarda do M8 acrescentou um teste, fechando em 167), e o fix pass da review dela **subiu o total para 172** — 3 testes do furo da regex, 1 da prova cromática e 1 da dispensa com motivo vazio. **O delta da Task 5 foi recontado no pré-flight de 2026-08-09 e está CERTO** (`Botao` 10 + `Campo` 5 + `BannerDeErro` 3 + `Pagina` 5 = 23) — o que estava errado eram os absolutos, que se contradiziam entre o Step 11 e a definition of done. **O delta da Task 6 foi recontado no pré-flight de 2026-08-09 e o `+21` estava ERRADO:** os `it(` do próprio plano somavam **20**, e a correção do pré-flight acrescentou 3 (o teste de peso visual da paginação e os 2 pares novos da guarda), fechando em **+23**. A Task 6 tinha o pior erro de contagem desta fase — **quatro** baselines mutuamente contraditórias no mesmo texto (179 no Step 1, 7 no Step 3, 200 no Step 7, 187 implícito na DoD), e o `200` do Step 7 era **exatamente a baseline real do dia**, portanto indistinguível de sucesso para quem rodasse a suíte antes de escrever qualquer linha. **A Task 7 fechou em `259` (230 + 29), medido**; o fix pass dos achados da review dela (2026-08-10) acrescentou **+9**, fechando em **268** — número que passa a ser a baseline da Task 8. Depois do passe curto de primitivas (**+4**, fechando em **272**), a **Task 8 fechou em `283` (272 + 11), medido**; o fix pass dos achados da review dela (2026-08-12) acrescentou **+7** — não os `+9` que o brief do fix pass previa (a própria previsão já avisava "não medição"; M7 e m3 couberam dentro de casos existentes em vez de virarem `it(` novos) —, fechando em **290**, que passa a ser a baseline da Task 9A. **A Task 9 virou 9A + 9B em 2026-08-13** e as duas contagens foram derivadas do zero, teste a teste, no relatório da partida — não herdadas do `X` da task unificada. Se você for implementar qualquer uma delas, **conte os `it(` do seu próprio Step de teste antes de confiar em qualquer delta deste plano**, e lembre que `it.each` conta **casos**, não blocos.)*

  **Esta nota já foi corrigida TRÊS vezes** — nasceu em 2026-08-07 dizendo "+3" (quando a Task 3 tinha alvo 139), virou "+4" quando a Task 3 mediu 140, e agora a Task 3 fechou em **144** depois de três fix passes, o que a deixaria "+8". **Essa recorrência é a demonstração do problema, não uma exceção a ele:** enquanto o vinculante for um total somado, cada task que soma em cima herda o erro da anterior, e três contagens erradas deste plano nasceram assim — uma delas quase travou uma task por alarme falso. Por isso a tabela acima dá **delta** como vinculante e marca o total como derivado.

  **Regra operacional:** (a) meça a baseline no Step 1; (b) se ela não bater com o total que a task anterior **reportou ao fechar**, pare e reporte — divergência aí é sinal real; (c) se bater, o que você tem de entregar é o **delta**; (d) se o delta estiver certo e o total divergir do derivado, **reporte o número real e siga**. **Nunca invente nem apague teste para fechar conta** — a Task 3 acrescentou um 14º teste onde o plano previa 13 porque um mutante não morria sem ele, e isso é o certo a fazer.
- **Toda review de task grava ARTEFATO em disco, não só devolve relatório ao controlador.** Decisão do usuário em 2026-08-07. O revisor escreve o relatório completo em `.superpowers/sdd/fase1d-task-N-review.md` (fix pass: `-fix-review.md`; re-review: `-re-review.md`) **e** devolve o mesmo conteúdo como mensagem final. **Motivo, e ele tem caso concreto:** as reviews das Tasks 1 e 2 desta fase não deixaram arquivo — os achados sobreviveram só resumidos no ledger, que é gitignored. Foi exatamente assim que a Fase 1B **perdeu o achado I7** (a review listava 6 Important, o resumo no ledger listava 5, e ainda renumerou os outros, fazendo "I2" significar coisas diferentes nos dois arquivos). Regra derivada, já registrada na 1B e agora executável: **resumir achado de review no ledger perde achado — aponte para o arquivo, não re-narre a lista.**
- **Texto de interface em português, com acentuação correta.** Nomes de domínio em português (`Componente`, `Agrupamento`); nomes técnicos em inglês só onde já é a convenção do repo (`Repository`, `UseCase`, `DTO`). Nomes de primitiva e de hook: **português** (`Pagina`, `Botao`, `useBuscaPaginada`) — é a convenção que o front já segue (`TelaCarregando`, `estadoDaSessao`, `apiFetch`).

### Decisões desta fase que **não** se re-decidem

Estão fechadas na spec ou foram decididas pelo usuário durante o planejamento. Se você discorda, **reporte — não altere**.

- **Paleta (spec §3):** chrome `#134E4A`, marca `#5EEAD4`, ação `#3E6E68`, fundo de pílula `#E8F0EF`, positivo `#166534` (era `#16A34A`; corrigido no pré-flight de 2026-08-08 porque reprovava AA **nos dois papéis** — ver a caixa da Task 4), negativo `#DC2626`. A ação ser monocromática com o chrome foi escolha explícita do usuário, contra a recomendação de dois matizes. **Verde e vermelho são reservados a estado; cor de identidade nunca significa estado.**
- **Pilha de fonte do sistema** (`ui-sans-serif` / `ui-monospace`). Zero asset para baixar. Código de peça e material em monoespaçada — decisão funcional (alinha na coluna, facilita conferir na bancada), não decorativa.
- **Sem biblioteca de componentes de terceiros.** Primitivas à mão sobre Tailwind v4.
- **Sem tema escuro.** Fora de escopo declarado — dobraria os estados a provar em cada primitiva.
- **Gating de perfil vai na AÇÃO, não no link** — decisão do usuário em 2026-08-06, e ela **diverge da letra da spec §6**. Motivo medido: os controllers liberam **leitura** para qualquer usuário autenticado; só escrita tem `Roles`. Esconder o link de Materiais do Almoxarifado tiraria dele uma leitura que ele pode (e vai precisar) fazer na Fase 4. Então: o link aparece para todos; o que some para quem não pode escrever é o **formulário de cadastro** e os **botões Inativar/Reativar/Excluir**. O `try/catch` do F2 **continua existindo** — o 403 do backend segue sendo a fronteira real de segurança, e o gating de UI não substitui nada.
- **`useBuscaPaginada` nasce sem consumidor (Task 3) e é adotado na Task 9B.** A spec §9 manda o hook entrar "antes do re-layout das telas"; construí-lo já acoplado à `ComponentesPage` obrigaria a reescrever `ComponentesPage.test.tsx` (987 linhas, 34 testes) **duas** vezes — uma pelo hook, outra pelo re-layout. Existir antes e ser adotado no re-layout satisfaz a ordem da spec com um terço do churn. **A partida da Task 9 em 9A (re-layout) + 9B (adoção do hook), em 2026-08-13, NÃO reabre essa decisão nem a contradiz:** MEDIDO no pré-flight, o re-layout sozinho fecha 34/34 sem tocar em paginação, e a 9B mexe em ~10 testes — reescrever uma vez e ajustar 10, não reescrever 987 linhas duas vezes.
- **"Ficou bonito" não é critério de aceite.** Foi barrado de propósito. Os critérios verificáveis estão na spec §11 e viram passos de teste/grep neste plano.

### Valores fixos desta fase (copie exatamente)

- **Largura de página:** `max-w-3xl` (768px). O `max-w-md` (448px) de hoje é o que a spec §7 diz não caber na tela de Componentes.
- **Debounce da busca:** **300 ms**, parametrizável (`atrasoDoDebounce`), default no hook.
- **Tamanhos de página:** `[20, 50, 100]`. O teto 100 é do backend — acima vira 400.
- **Perfis de escrita, espelhando os `[Authorize(Roles)]` do backend** (conferidos no disco em 2026-08-06):
  - `setores` → `Administrador`
  - `materiais` → `Administrador`
  - `componentes` → `Administrador`, `PCP`
  - `pedidos` → `PCP`, `Administrador`
  - `agrupamentos` → `PCP`, `Administrador`
- **Perfis existentes** (`db/seed.sql`): `Operador`, `Almoxarifado`, `PCP`, `Qualidade`, `Gestao`, `Administrador`. Repare: **`Gestao` sem acento** — é o valor do banco.
- **Status de `Pedido`** (`CK` do DDL): `Aberto`, `EmProducao`, `AguardandoExpedicao`, `Concluido`, `Cancelado`.
- **Breakpoint da gaveta:** `md` do Tailwind (768px), como a spec §6 pede ("abaixo de ~768px").

---

## File Structure

Mapa do que a fase cria e modifica. Cada arquivo tem uma responsabilidade só.

**Criados — rede e comportamento (Tasks 1–3)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/testes/api.ts` | Kit de teste de tela: `respostaJson`, `fetchPorRota`. Sem ele, cada arquivo de teste reinventa o mock de `fetch` roteado. |
| `web/src/pages/HomePage.test.tsx` | Fumaça da Home |
| `web/src/pages/PedidosPage.test.tsx` | Fumaça de Pedidos |
| `web/src/pages/PedidoDetalhePage.test.tsx` | Fumaça do detalhe do Pedido |
| `web/src/pages/LoginPage.test.tsx` | Fumaça do Login (harness de auth, diferente das demais) |
| `web/src/api/erros.ts` | `ErroDeApi` (carrega o `status`) + `mensagemDeErro` (401/403/404/5xx/rede → texto amigável) |
| `web/src/api/erros.test.ts` | Prova de cada ramo de `mensagemDeErro` |
| `web/src/hooks/useBuscaPaginada.ts` | Busca com debounce, cancelamento por sequência, clamp de página e reset de filtro |
| `web/src/hooks/useBuscaPaginada.test.tsx` | Prova do hook, incluindo o W3 |

**Criados — tema e primitivas (Tasks 4–6)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/tema/contraste.ts` | `luminanciaRelativa` / `razaoDeContraste` — funções puras WCAG |
| `web/src/tema/contraste.test.ts` | Lê o `@theme` do `index.css` e **mede** cada par declarado. Token novo sem par declarado **reprova**. |
| `web/src/components/Pagina.tsx` | Container de página: largura, respiro, cabeçalho com título e ação |
| `web/src/components/Botao.tsx` | `primario` / `secundario` / `perigo`, com estado `carregando` que desabilita |
| `web/src/components/Campo.tsx` | Label + controle, com `id` ligado por `useId` |
| `web/src/components/BannerDeErro.tsx` | Mensagem de erro com `role="alert"` |
| `web/src/components/ListaDeCadastro.tsx` | `ListaDeCadastro` (o `<ul>`) e `ItemDeCadastro` (o `<li>`, com inativo distinguível e slot de ação) |
| `web/src/components/Pilula.tsx` | Rótulo de categoria/estado |
| `web/src/components/EstadoVazio.tsx` | Estado vazio explícito, com título e descrição próprios |
| `web/src/components/ControlesDePaginacao.tsx` | Anterior / posição / Próxima |
| `web/src/components/*.test.tsx` | Um arquivo de teste por primitiva |

**Criados — shell e permissões (Task 7)**

| Arquivo | Responsabilidade |
|---|---|
| `web/src/auth/permissoes.ts` | Tabela declarativa `recurso → perfis de escrita`, espelhando o backend |
| `web/src/auth/permissoes.test.ts` | Prova da tabela |
| `web/src/auth/usePermissao.ts` | `usePodeEscrever(recurso)` — lê o perfil da sessão |
| `web/src/components/AppShell.tsx` | Barra superior + gaveta abaixo de 768px + identidade + logout + `<Outlet/>` |
| `web/src/components/AppShell.test.tsx` | Prova do shell |

**Modificados**

| Arquivo | O que muda | Task |
|---|---|---|
| `web/src/index.css` | Ganha o bloco `@theme` com os tokens | 4 |
| `web/src/api/cadastros.ts` | Os `throw new Error(...)` viram `throw new ErroDeApi(status, ...)` | 2 |
| `web/src/App.tsx` | As 6 rotas protegidas passam a ser filhas de uma rota de layout com `AppShell` | 7 |
| `web/src/pages/SetoresPage.tsx` · `MateriaisPage.tsx` · `PedidosPage.tsx` | Retrofit: primitivas, estados, gating de ação | 8 |
| `web/src/pages/ComponentesPage.tsx` | Re-layout com as primitivas (mantendo o estado local de busca/paginação) | 9A |
| `web/src/pages/ComponentesPage.tsx` | Troca do motor: `useBuscaPaginada` + `ControlesDePaginacao` | 9B |
| `web/src/hooks/useBuscaPaginada.test.tsx` | Ganha o teste da guarda de sequência do `catch` (decisão U2) | 9B |
| `web/src/pages/PedidoDetalhePage.tsx` | Re-layout + modal com as primitivas | 10 |
| `web/src/pages/HomePage.tsx` | Muda de papel: cartões de contagem reais | 11 |
| `web/src/pages/LoginPage.tsx` | Identidade aplicada | 12 |
| `web/src/components/TelaCarregando.tsx` | Passa a usar os tokens | 4 |
| `specs/06-roadmap-mvp.md` | Registra a Fase 1D e a ausência de aresta de dependência | 12 |

**Não muda nada em `src/`, `tests/`, `specs/02-modelo-de-dados.sql`, `db/`.**

---

### Task 1: Rede de fumaça — kit de teste e as 4 telas sem prova

**Files:**
- Create: `web/src/testes/api.ts`
- Create: `web/src/pages/HomePage.test.tsx`
- Create: `web/src/pages/PedidosPage.test.tsx`
- Create: `web/src/pages/PedidoDetalhePage.test.tsx`
- Create: `web/src/pages/LoginPage.test.tsx`

**Interfaces:**
- Consumes: `apiFetch`, `inicializar`, `_resetParaTeste` de `web/src/api/client.ts`; as telas como estão hoje.
- Produces: `respostaJson(corpo: unknown, status?: number): Response` e `fetchPorRota(mapa: Record<string, () => Response | Promise<Response>>): Mock` em `web/src/testes/api.ts`. **Todas as tasks seguintes usam as duas.**

**Por que esta task existe primeiro:** hoje só 3 das 7 telas têm teste. As Tasks 8–12 reescrevem markup em massa. Regressão de markup não quebra build nem tipo — aparece para o usuário. Esta é a rede que torna as reescritas seguras.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Test Files  6 passed (6)` · `Tests  96 passed (96)`. Se divergir, **pare e reporte**.

- [ ] **Step 2: Escrever o kit de teste**

`web/src/testes/api.ts`:

```ts
import { vi } from 'vitest'

/**
 * Resposta JSON pronta para `vi.stubGlobal('fetch', ...)`.
 *
 * O corpo NUNCA é `''`, mesmo em resposta de erro: um corpo vazio faz `.json()` lançar sozinho, e
 * um `rejects.toThrow()` sem argumento passa pelo parse falho em vez de passar pela guarda que se
 * queria provar (adendo F6 — custou três mutações vivas na Fase 1A).
 */
export function respostaJson(corpo: unknown, status = 200): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Mock de `fetch` roteado por caminho. A chave é o caminho COM o prefixo `/api` — é isso que o
 * `rota()` de `client.ts` monta, e escrever a chave sem o prefixo é o erro que faz o teste falhar
 * com "fetch não esperado" em vez de com a asserção.
 *
 * A query string é descartada na comparação: o teste declara `/api/componentes`, não
 * `/api/componentes?busca=&pagina=1&tamanho=20`. Quando a URL completa importa (prova de filtro),
 * asserte sobre `fetchMock.mock.calls[n][0]`, que guarda a URL inteira.
 *
 * Rota não declarada REJEITA com mensagem nomeando a URL, em vez de devolver `undefined` — sem
 * isso o teste morre dez linhas adiante em "cannot read property 'ok' of undefined", e o que se
 * lê no relatório não tem relação com a causa.
 */
export function fetchPorRota(mapa: Record<string, () => Response | Promise<Response>>) {
  return vi.fn((url: string | URL) => {
    const caminho = String(url).split('?')[0]
    const entrada = mapa[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}
```

- [ ] **Step 3: Rodar a suíte para confirmar que o kit não quebrou nada**

```bash
cd web && npm test
```

Expected: ainda `96 passed`. O kit ainda não tem consumidor.

- [ ] **Step 4: Escrever o teste de fumaça da `PedidosPage`**

`web/src/pages/PedidosPage.test.tsx`:

```tsx
// @vitest-environment jsdom
//
// Ambiente por ARQUIVO, e não em `vite.config.ts`: os testes de `api/` rodam em ambiente `node` e
// usam `new Response(...)`; trocar o ambiente global arriscaria mexer nos globals deles sem ganho.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PedidosPage } from './PedidosPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que só existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha o segundo teste renderiza por cima do primeiro e os `getBy*` falham com
// "found multiple elements".
afterEach(cleanup)

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

describe('PedidosPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os pedidos que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect(screen.getByText(/Fábrica Alfa/)).toBeTruthy()
  })

  it('mostra a data de abertura no fuso que a API mandou, sem reconverter', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    // 06/08/2026 09:30 — se alguém trocar `formatarDataHora` por `new Date(...).toLocaleString()`,
    // o horário anda com o fuso da máquina e este teste morre. É exatamente o defeito que a função
    // existe para impedir.
    expect(await screen.findByText(/06\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra erro quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar os pedidos.')).toBeTruthy()
  })

  it('limpa o formulário e recarrega a lista depois de abrir um pedido', async () => {
    let listagens = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [] : [PEDIDO])
      },
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)
    await screen.findByPlaceholderText('Código do pedido')

    fireEvent.change(screen.getByPlaceholderText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByPlaceholderText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    // O POST cai na MESMA rota da listagem: `fetchPorRota` casa por caminho, e o mock devolve a
    // lista nova. O que se prova aqui é o ramo de SUCESSO — campo limpo e lista recarregada —,
    // que é a metade que nenhum teste do projeto cobria antes desta task.
    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect((screen.getByPlaceholderText('Código do pedido') as HTMLInputElement).value).toBe('')
  })
})
```

- [ ] **Step 5: Rodar e confirmar que os 4 testes novos passam**

```bash
cd web && npm test -- PedidosPage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 6: Provar que a rede morde — mutação na `PedidosPage`**

Aplique, uma de cada vez, revertendo por edição inversa:

| # | Mutação em `web/src/pages/PedidosPage.tsx` | Mortes esperadas |
|---|---|---|
| M1 | remover `setForm(FORMULARIO_VAZIO)` do ramo de sucesso (linha 42) | ≥ 1 |
| M2 | remover `await carregar()` do ramo de sucesso (linha 43) | ≥ 1 |
| M3 | trocar `{p.numero} — {p.cliente}` por `{p.cliente} — {p.numero}` (linha 79) | ≥ 1 |
| M4 | trocar `formatarDataHora(p.dataAbertura)` por `p.dataAbertura` (linha 81) | ≥ 1 |
| M5 | remover a linha `setErro('Não foi possível carregar os pedidos.')` (linha 21) | ≥ 1 |

Confirme cada mutação com `grep -c` do texto **antigo** caindo a 0, e reverta antes da próxima.
**Se alguma matar 0, o teste correspondente é decorativo — conserte o teste, não a contagem.**

- [ ] **Step 7: Commit**

```bash
git add web/src/testes/api.ts web/src/pages/PedidosPage.test.tsx
git commit -m "test(web): kit de teste de tela e rede de fumaca da PedidosPage"
```

- [ ] **Step 8: Escrever o teste de fumaça do `PedidoDetalhePage`**

`web/src/pages/PedidoDetalhePage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { PedidoDetalhePage } from './PedidoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

const AGRUPAMENTO = {
  id: 21,
  pedidoId: 7,
  codigo: 'AGR-01',
  tipo: 'Kit',
  criadoEm: '2026-08-06T10:00:00-03:00',
  criadoPorUsuarioId: 1,
}

// A tela lê `:id` da rota, então ela precisa nascer DENTRO de uma rota casada — renderizar o
// componente solto deixaria `useParams()` vazio e `pedidoId` viraria NaN.
function renderizarDetalhe() {
  return render(
    <MemoryRouter initialEntries={['/pedidos/7']}>
      <Routes>
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PedidoDetalhePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra o cabeçalho do pedido e os agrupamentos dele', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('PED-001')).toBeTruthy()
    expect(screen.getByText('Fábrica Alfa')).toBeTruthy()
    expect(await screen.findByText('AGR-01')).toBeTruthy()
  })

  it('pede confirmação antes de excluir e só exclui depois do "Excluir" do diálogo', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))

    // O diálogo apareceu e NADA foi excluído ainda: a pausa deliberada é a propriedade sob teste.
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)

    // Dois botões escritos "Excluir" na tela agora (o do item e o do diálogo): pegar pelo texto
    // dentro do diálogo é o que impede o teste de clicar no errado e passar por acidente.
    const dialogo = screen.getByRole('dialog')
    const confirmar = Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!
    fireEvent.click(confirmar)

    await screen.findByText('AGR-01')
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(true)
  })

  it('cancelar fecha o diálogo sem excluir', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)
  })

  it('explica o motivo quando a exclusão é recusada por agrupamento não vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoNaoVazio' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('Este agrupamento já tem estrutura e não pode mais ser excluído.'),
    ).toBeTruthy()
  })
})
```

- [ ] **Step 9: Rodar e confirmar**

```bash
cd web && npm test -- PedidoDetalhePage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 10: Provar que a rede morde — mutação no `PedidoDetalhePage`**

| # | Mutação em `web/src/pages/PedidoDetalhePage.tsx` | Mortes esperadas |
|---|---|---|
| M6 | trocar o `onClick` do botão "Excluir" do item por `() => excluir(a.id)` (pula a confirmação, linha 142) | ≥ 1 |
| M7 | trocar `setPendenteExclusao(null)` do "Cancelar" por `confirmarExclusao` (linha 178) | ≥ 1 |
| M8 | trocar `MOTIVO_DA_RECUSA.AgrupamentoNaoVazio` pelo texto de `PedidoNaoAberto` (linha 15) | ≥ 1 |
| M9 | remover `<h1 …>{pedido.numero}</h1>` (linha 104) | ≥ 1 |

- [ ] **Step 11: Commit**

```bash
git add web/src/pages/PedidoDetalhePage.test.tsx
git commit -m "test(web): rede de fumaca do PedidoDetalhePage"
```

- [ ] **Step 12: Escrever o teste de fumaça da `HomePage`**

`web/src/pages/HomePage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// A `HomePage` usa `useAuth()` só para o `logout`. Como o `AuthProvider` de verdade dispara um
// init-refresh no mount (rede), aqui ele é substituído por um provider mínimo — o que se testa é a
// tela, não a sessão. O `useAuth` real tem sua prova em `estadoDaSessao.test.ts` e no LoginPage.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: { status: 'anonimo' }, login: async () => {}, logout: async () => {} }),
}))

const USUARIO = { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Administrador do Sistema', perfil: 'Administrador' }

describe('HomePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra quem está logado depois de carregar /me', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Administrador do Sistema')).toBeTruthy()
    expect(screen.getByText('Administrador')).toBeTruthy()
  })

  it('mostra "Carregando…" antes da resposta e o esconde depois', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson(USUARIO),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getByText('Carregando…')).toBeTruthy()
    await screen.findByText('Administrador do Sistema')
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('mostra erro quando /me falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/me': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('Não foi possível carregar seus dados.')).toBeTruthy()
  })
})
```

**Aviso ao implementador da Task 11:** este arquivo será reescrito quando a Home mudar de papel. Isso é esperado, não regressão — a rede existe para pegar mudança **não intencional** de markup; a Task 11 muda a tela de propósito e atualiza o teste junto.

- [ ] **Step 13: Rodar e confirmar**

```bash
cd web && npm test -- HomePage
```

Expected: `Tests  3 passed (3)`.

- [ ] **Step 14: Escrever o teste de fumaça da `LoginPage`**

`web/src/pages/LoginPage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LoginPage } from './LoginPage'
import { AuthProvider } from '../auth/AuthContext'
import { _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Harness diferente das outras telas, de propósito: a `LoginPage` só existe dentro do
// `AuthProvider` de VERDADE — é ele que chama `client.inicializar` e o init-refresh. Por isso
// aqui NÃO se chama `inicializar()` à mão (o provider chama) e o mock precisa responder
// `/api/auth/refresh`, que dispara no mount antes de qualquer interação.
describe('LoginPage', () => {
  beforeEach(() => { _resetParaTeste() })
  afterEach(() => { vi.unstubAllGlobals() })

  function renderizarLogin() {
    return render(
      <MemoryRouter><AuthProvider><LoginPage /></AuthProvider></MemoryRouter>,
    )
  }

  it('mostra os dois campos e o botão de entrar', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
    }))

    renderizarLogin()

    expect(await screen.findByLabelText('Usuário')).toBeTruthy()
    expect(screen.getByLabelText('Senha')).toBeTruthy()
    expect(screen.getByText('Entrar')).toBeTruthy()
  })

  it('mostra a mensagem genérica quando o login é recusado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
      '/api/auth/login': () => respostaJson({ erro: 'nao importa' }, 401),
    }))

    renderizarLogin()
    fireEvent.change(await screen.findByLabelText('Usuário'), { target: { value: 'admin' } })
    fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'errada' } })
    fireEvent.click(screen.getByText('Entrar'))

    // Texto ÚNICO e genérico: honra o não-oráculo do backend, que responde o mesmo 401 para
    // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
    // desfaria no front a defesa que o backend paga BCrypt para manter.
    expect(await screen.findByText('Usuário ou senha inválidos.')).toBeTruthy()
  })

  it('avisa que a sessão expirou quando a volta ao login veio de sessão perdida', async () => {
    // O init-refresh responde 200 (sessão restaurada), e a primeira chamada autenticada devolve
    // 401 duas vezes seguidas -> `onSessionLost` -> estado anônimo com motivo 'sessao-expirada'.
    // É o único caminho que acende este aviso, e nenhum teste o cobria.
    let refreshes = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/auth/refresh': () => {
        refreshes += 1
        return refreshes === 1
          ? respostaJson({
              accessToken: 't',
              accessTokenExpiraEm: '2026-08-06T10:00:00-03:00',
              usuario: { id: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' },
            })
          : respostaJson({ erro: 'expirou' }, 401)
      },
      '/api/me': () => respostaJson({ erro: 'nao autorizado' }, 401),
    }))

    const { apiFetch } = await import('../api/client')
    renderizarLogin()
    // Espera o provider terminar o init-refresh antes de forçar a perda de sessão.
    await screen.findByText('Entrar')
    await apiFetch('/me')

    expect(await screen.findByText('Sessão expirada. Entre novamente.')).toBeTruthy()
  })
})
```

**Atenção:** os `<label>` da `LoginPage` hoje envolvem o `<input>` sem `htmlFor`/`id`. `getByLabelText` funciona com label envolvente, então o teste passa **como está** — não mexa na tela nesta task. A ligação explícita por `id` entra na Task 5, junto do `Campo`.

- [ ] **Step 15: Rodar a suíte inteira**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: **115 testes / 10 arquivos**, todos verdes · build limpo · lint com **só** o warning alheio de `AuthContext.tsx:48`.
*(96 + 19: PedidosPage 4, PedidoDetalhePage **8**, HomePage 3, LoginPage **4**. Medido em 2026-08-06, estado final pós-3 rodadas de review — ver a nota da linha 25 para como se chegou nele.)*

- [ ] **Step 16: Provar que a rede morde — mutação na `HomePage` e na `LoginPage`**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M10 | `HomePage.tsx:26` — remover `setCarregando(false)` do `finally` | ≥ 1 |
| M11 | `HomePage.tsx:43` — trocar `{usuario.perfil}` por `{usuario.nomeUsuario}` | ≥ 1 |
| M12 | `LoginPage.tsx:26` — trocar a mensagem por `'Senha inválida.'` | ≥ 1 |
| M13 | `LoginPage.tsx:16` — trocar `estado.motivo === 'sessao-expirada'` por `false` | ≥ 1 |

- [ ] **Step 17: Commit**

```bash
git add web/src/pages/HomePage.test.tsx web/src/pages/LoginPage.test.tsx
git commit -m "test(web): rede de fumaca da HomePage e da LoginPage"
```

**Definition of done da Task 1:** as **7** telas têm arquivo de teste; suíte em **115** (medido, estado final); build e lint limpos; as mutações medidas, cada uma com ≥ 1 morte, e o número reportado. **CUMPRIDA** — commits `165b36a`..`6af85ca`, três rodadas de review, última limpa.

---

### Task 2: `ErroDeApi` e mensagens amigáveis de API

**Files:**
- Create: `web/src/api/erros.ts`
- Create: `web/src/api/erros.test.ts`
- Modify: `web/src/api/cadastros.ts` (os 12 `throw new Error(...)`)
- Modify: `web/src/api/cadastros.test.ts` (acrescentar um `describe` no fim)

**Interfaces:**
- Consumes: nada de tasks anteriores.
- Produces: `class ErroDeApi extends Error { readonly status: number }` e `mensagemDeErro(e: unknown, fallback: string): string`, ambos de `web/src/api/erros.ts`. **As Tasks 8–11 chamam `mensagemDeErro` em todo `catch` de tela.**

**A dívida que esta task fecha** (spec §9, rastreada desde a Fase 1A): hoje toda falha vira o mesmo texto — `'Não foi possível carregar os setores.'` — não importa se foi 403 de perfil, 404 de registro sumido, sessão expirada ou wifi caído. O usuário lê "não foi possível" e não sabe se tenta de novo, se chama o administrador, ou se o registro não existe mais.

**O que NÃO muda:** a assinatura pública de `cadastros.ts` (tudo continua lançando `Error`, já que `ErroDeApi extends Error`), os textos das mensagens existentes, e o `try/catch` do F2 nas telas. Os **16** `rejects.toThrow()` de `cadastros.test.ts` seguem válidos — foram conferidos no disco: **nenhum passa argumento de mensagem**.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  115 passed (115)` — número **medido** ao fim da Task 1 (commit `6af85ca`, já com as três rodadas de review), não estimado. Se divergir, pare e reporte.

- [ ] **Step 2: Escrever o teste de `mensagemDeErro`, que ainda não existe**

`web/src/api/erros.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { ErroDeApi, mensagemDeErro } from './erros'

const PADRAO = 'Não foi possível carregar os setores.'

describe('mensagemDeErro', () => {
  it('explica sessão expirada no 401', () => {
    expect(mensagemDeErro(new ErroDeApi(401, 'x'), PADRAO)).toBe('Sua sessão expirou. Entre novamente.')
  })

  it('explica falta de permissão no 403', () => {
    // O caso mais frequente na fábrica: Operador clicando em ação de Administrador. Hoje ele lê
    // "Não foi possível alterar o setor", tenta de novo, e recebe o mesmo 403.
    expect(mensagemDeErro(new ErroDeApi(403, 'x'), PADRAO))
      .toBe('Seu perfil não tem permissão para esta ação.')
  })

  it('explica registro inexistente no 404', () => {
    expect(mensagemDeErro(new ErroDeApi(404, 'x'), PADRAO)).toBe('Este registro não existe mais.')
  })

  it('explica falha do servidor no 500', () => {
    expect(mensagemDeErro(new ErroDeApi(500, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('explica falha do servidor no 503 também, não só no 500', () => {
    // A guarda é `>= 500`, não `=== 500`: hardcodar o 500 deixaria 502/503/504 — os status que um
    // proxy ou uma API fora do ar realmente devolvem — caindo no texto genérico.
    expect(mensagemDeErro(new ErroDeApi(503, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('cai no texto da tela para status que não têm explicação própria', () => {
    // 400 é falha de validação: quem sabe o que dizer sobre ela é a tela, não esta função.
    expect(mensagemDeErro(new ErroDeApi(400, 'x'), PADRAO)).toBe(PADRAO)
  })

  it('explica queda de rede', () => {
    // `fetch` rejeita com TypeError quando a requisição nem sai — o sintoma do wifi de fábrica.
    expect(mensagemDeErro(new TypeError('Failed to fetch'), PADRAO))
      .toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
  })

  it('cai no texto da tela para erro que não é de API nem de rede', () => {
    expect(mensagemDeErro(new Error('qualquer outra coisa'), PADRAO)).toBe(PADRAO)
    expect(mensagemDeErro('nem erro é', PADRAO)).toBe(PADRAO)
  })
})

describe('ErroDeApi', () => {
  it('carrega o status e continua sendo um Error', () => {
    // O `instanceof Error` é o que mantém os 16 `rejects.toThrow()` de cadastros.test.ts válidos.
    const e = new ErroDeApi(409, 'Falha na requisição (409).')
    expect(e).toBeInstanceOf(Error)
    expect(e.status).toBe(409)
    expect(e.message).toBe('Falha na requisição (409).')
    expect(e.name).toBe('ErroDeApi')
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- erros
```

Expected: FAIL — `Failed to resolve import "./erros"`.

- [ ] **Step 4: Escrever `erros.ts`**

`web/src/api/erros.ts`:

```ts
/**
 * Erro de API que CARREGA o status. Antes desta classe o status vivia só dentro da string da
 * mensagem (`Falha ao listar setores (403).`), então a tela não tinha como distinguir "seu perfil
 * não pode" de "o servidor caiu" sem fazer parse de texto.
 *
 * Continua sendo `Error`: as funções de `cadastros.ts` não mudam de contrato, e todo
 * `rejects.toThrow()` que já existia segue valendo.
 */
export class ErroDeApi extends Error {
  constructor(readonly status: number, mensagem: string) {
    super(mensagem)
    this.name = 'ErroDeApi'
  }
}

/**
 * Traduz o que caiu no `catch` para uma frase que diga ao usuário o que fazer a seguir.
 *
 * `fallback` é o texto específico da tela ("Não foi possível carregar os setores.") e continua
 * sendo o destino de tudo que esta função não sabe explicar melhor — status de validação (400),
 * erro de programação, valor que nem erro é. A função nunca INVENTA explicação: ou reconhece o
 * caso, ou devolve o que a tela já diria.
 *
 * O 401 aqui é informativo, não corretivo: quem devolve o usuário ao login é o `onSessionLost` do
 * `client.ts`, depois de o refresh falhar. Esta mensagem cobre a janela em que a tela ainda está
 * montada.
 */
export function mensagemDeErro(e: unknown, fallback: string): string {
  if (e instanceof ErroDeApi) {
    if (e.status === 401) return 'Sua sessão expirou. Entre novamente.'
    if (e.status === 403) return 'Seu perfil não tem permissão para esta ação.'
    if (e.status === 404) return 'Este registro não existe mais.'
    if (e.status >= 500) return 'O servidor não respondeu como esperado. Tente de novo em instantes.'
    return fallback
  }
  // `fetch` rejeita com TypeError quando a requisição nem sai (DNS, rede, CORS). É o único erro
  // de rede que chega aqui — o resto do caminho já virou Response.
  if (e instanceof TypeError) return 'Sem conexão com o servidor. Verifique a rede e tente de novo.'
  return fallback
}
```

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- erros
```

Expected: `Tests  9 passed (9)`.

- [ ] **Step 6: Trocar os 12 `throw` de `cadastros.ts`**

Acrescente o import no topo de `web/src/api/cadastros.ts`:

```ts
import { apiFetch } from './client'
import { ErroDeApi } from './erros'
```

Troque **cada** `throw new Error(...)` mantendo a mensagem **caractere por caractere**. Lista completa, na ordem do arquivo (linhas de hoje):

| Linha | Mensagem (inalterada) | Status a passar |
|---|---|---|
| 32 | `Falha na requisição (409): formato de conflito inesperado.` | `409` |
| 34 | `Falha na requisição (${resp.status}).` | `resp.status` |
| 40 | `Falha ao listar setores (${resp.status}).` | `resp.status` |
| 58 | `Falha ao alterar o setor (${resp.status}).` | `resp.status` |
| 81 | `Falha ao listar materiais (${resp.status}).` | `resp.status` |
| 100 | `Falha ao alterar o material (${resp.status}).` | `resp.status` |
| 132 | `Falha ao listar pedidos (${resp.status}).` | `resp.status` |
| 138 | `Falha ao carregar o pedido (${resp.status}).` | `resp.status` |
| 174 | `Falha ao listar agrupamentos (${resp.status}).` | `resp.status` |
| 204 | `Falha ao excluir o agrupamento (${resp.status}).` | `resp.status` |
| 254 | `Falha ao listar componentes (${resp.status}).` | `resp.status` |
| 275 | `Falha ao alterar o componente (${resp.status}).` | `resp.status` |

Forma de cada troca:

```ts
// antes
if (!resp.ok) throw new Error(`Falha ao listar setores (${resp.status}).`)
// depois
if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar setores (${resp.status}).`)
```

Confirme que não sobrou nenhum:

```bash
cd web && grep -c "throw new Error" src/api/cadastros.ts
```

Expected: `0`.

- [ ] **Step 7: Rodar a suíte inteira — nenhum teste antigo pode quebrar**

```bash
cd web && npm test
```

Expected: `Tests  124 passed (124)` (115 + 9). **Se algum teste de `cadastros.test.ts` quebrar, você mudou uma mensagem.** Confira com `git diff` e restaure o texto exato.

- [ ] **Step 8: Acrescentar a prova de que o status viaja**

**DENTRO** de `describe('cadastros')`, como último bloco dele — **não** depois do `})` que o fecha. Os hooks `beforeEach` (`_resetParaTeste()` + `inicializar({...})`) e `afterEach` (`vi.unstubAllGlobals()`) estão registrados **dentro** desse `describe`, e um bloco irmão não os herda: sem `inicializar()`, `apiFetch` lança `'client nao inicializado'` (`client.ts:38-40`) e os três testes falham. *(Esta instrução dizia "no fim do arquivo, depois do último `describe`" e estava errada — os três testes passavam só porque o `describe` vizinho tinha deixado `deps` preenchido. Achado da review da Task 2, provado rodando o bloco isolado.)*

```ts
describe('status nas falhas de API', () => {
  // Sem estes testes, trocar `resp.status` por um literal (`403`, `500`) em qualquer um dos 12
  // `throw` passaria verde: os `rejects.toThrow()` que já existem não olham o status, só o fato de
  // ter lançado. E é o status que a tela usa para escolher a mensagem.
  it('listarSetores propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'x' }), { status: 403 }),
    ))

    await expect(listarSetores(false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })
  })

  it('lerOuFalhar propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ erro: 'x' }), { status: 500 }),
    ))

    await expect(criarSetor('Solda')).rejects.toMatchObject({ name: 'ErroDeApi', status: 500 })
  })

  it('definirAtivoComponente propaga o status da resposta', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))

    await expect(definirAtivoComponente(4, false)).rejects.toMatchObject({ name: 'ErroDeApi', status: 403 })
  })
})
```

**Confira os imports do topo do arquivo:** `listarSetores`, `criarSetor` e `definirAtivoComponente` já são importados lá. Não duplique.

- [ ] **Step 9: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  127 passed (127)` · build limpo · lint só com o warning alheio de `AuthContext.tsx:48`.

*(127 = 115 medidos ao fim da Task 1 + 9 de `erros.test.ts` + 3 do Step 8. **O que vincula é o delta `+12`, não o absoluto** — se a baseline que você mediu no Step 1 for outra, reporte o número real e siga, conforme a constraint global. Este é um total derivado por soma, e soma feita de cabeça já errou duas vezes neste plano.)*

> **FECHAMENTO MEDIDO: a Task 2 fechou em 126, não 127.** A implementação bateu os 127 previstos; o **fix pass** (`e451730`) consolidou dois testes de `definirAtivoComponente` num só — o novo era clone verbatim do existente, com asserção mais forte, então o antigo virara subconjunto dele (achado C3 da review). **Não é teste perdido:** a asserção forte migrou para o teste que sobreviveu (`cadastros.test.ts:677`), e a re-review confirmou a consolidação como a escolha certa. Medição final: `npm test` = **126 em 11 arquivos**, verde, HEAD `e451730`.

- [ ] **Step 10: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `erros.ts` — `e.status >= 500` → `e.status === 500` | ≥ 1 (o teste do 503) |
| M2 | `erros.ts` — remover o ramo `if (e.status === 403)` | ≥ 1 |
| M3 | `erros.ts` — `e instanceof TypeError` → `false` | ≥ 1 |
| M4 | `erros.ts` — `if (e instanceof ErroDeApi)` → `false` | ≥ 1 (mata vários) |
| M5 | `cadastros.ts:40` — `resp.status` → `500` no `ErroDeApi` de `listarSetores` | ≥ 1 |
| M6 | `cadastros.ts:34` — `resp.status` → `400` no `lerOuFalhar` | ≥ 1 |

- [ ] **Step 11: Commit**

```bash
git add web/src/api/erros.ts web/src/api/erros.test.ts web/src/api/cadastros.ts web/src/api/cadastros.test.ts
git commit -m "feat(web): ErroDeApi com status e mensagens amigaveis de falha"
```

**Definition of done da Task 2:** suíte em **127** (= baseline medida + 12); `grep -c "throw new Error" src/api/cadastros.ts` = 0; as 6 mutações medidas com ≥ 1 morte; **nenhuma tela modificada** — as telas passam a chamar `mensagemDeErro` nas Tasks 8–11.

**CUMPRIDA em 2026-08-07, e o número final é 126** (ver a caixa do Step 9). `grep -c "throw new Error"` = 0 e `grep -c "new ErroDeApi("` = 12, conferidos no disco pela re-review. Nenhuma tela tocada. Review + fix pass + re-review: **Aprovado, sem Critical nem Important**; relatório em `.superpowers/sdd/fase1d-task-2-re-review.md`.

---

### Task 3: `useBuscaPaginada` — debounce, cancelamento, clamp e o W3

**Files:**
- Create: `web/src/hooks/useBuscaPaginada.ts`
- Create: `web/src/hooks/useBuscaPaginada.test.tsx`

**Interfaces:**
- Consumes: nada.
- Produces (a **Task 9B** é quem consome — a 9A faz o re-layout ainda com o estado local da tela):

```ts
export interface PaginaDeBusca<T> { itens: T[]; total: number }
export interface FiltroDeBusca { busca: string; incluirInativos: boolean; pagina: number; tamanho: number }
export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
}
export interface BuscaPaginada<T> {
  itens: T[]; total: number; totalDePaginas: number
  textoDaBusca: string; busca: string
  incluirInativos: boolean; pagina: number; tamanho: number
  carregando: boolean; erro: unknown
  mudarBusca(valor: string): void
  mudarInativos(valor: boolean): void
  mudarTamanho(valor: number): void
  irParaPagina(valor: number): void
  recarregar(): Promise<void>
}
export function useBuscaPaginada<T>(opcoes: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T>
```

**As quatro dívidas que ele fecha, e por que juntas:** debounce, cancelamento, clamp de página e reset de filtro são a **mesma** propriedade vista de quatro ângulos — *o que está na tela corresponde ao filtro que o usuário vê*. Resolver três e deixar uma produz a cobertura desigual que a spec §9 nomeia.

**O hook nasce sem consumidor.** Decisão registrada nas Global Constraints: acoplá-lo à `ComponentesPage` aqui obrigaria a reescrever `ComponentesPage.test.tsx` (987 linhas, 34 testes) duas vezes — uma pelo hook, outra pelo re-layout. O consumidor chega na **Task 9B**; o re-layout é a **9A**, e ele não toca neste hook.

**Esta suíte ganha um teste na Task 9B (decisão U2 do usuário, 2026-08-13):** a guarda de sequência do `catch` (`useBuscaPaginada.ts:106`) é a **única das três** que os testes deste arquivo **não** matam — MEDIDO no pré-flight: apagá-la deixa os 18 testes daqui verdes, e o único matador do projeto é `ComponentesPage.test.tsx:554`. Não é dívida desta task (o teste dela nasce junto com o consumidor, onde a corrida tem cenário), mas está registrado aqui para não se perder.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: **126 testes / 11 arquivos** — número **medido** ao fechar a Task 2 (a estimativa deste plano dizia 127; o fix pass consolidou dois testes, ver a caixa do Step 9 da Task 2). Se divergir de 126, pare e reporte.

- [ ] **Step 2: Escrever o teste do hook, que ainda não existe**

`web/src/hooks/useBuscaPaginada.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from './useBuscaPaginada'

afterEach(cleanup)

interface Item { id: number; nome: string }

// Componente-hospedeiro: um hook não renderiza nada, então a prova passa por uma tela mínima que
// expõe cada saída num nó com texto asserível. Deliberadamente burro — se ele tiver lógica, o
// teste passa a provar o hospedeiro, não o hook.
function Hospedeiro({ buscar, atraso = 300 }: {
  buscar: (f: FiltroDeBusca) => Promise<PaginaDeBusca<Item>>
  atraso?: number
}) {
  const b = useBuscaPaginada<Item>({ buscar, atrasoDoDebounce: atraso })
  return (
    <div>
      <input aria-label="busca" value={b.textoDaBusca} onChange={(e) => b.mudarBusca(e.target.value)} />
      <input aria-label="inativos" type="checkbox" checked={b.incluirInativos}
             onChange={(e) => b.mudarInativos(e.target.checked)} />
      <select aria-label="tamanho" value={b.tamanho} onChange={(e) => b.mudarTamanho(Number(e.target.value))}>
        <option value={20}>20</option>
        <option value={50}>50</option>
      </select>
      <button onClick={() => b.irParaPagina(b.pagina + 1)}>Próxima</button>
      <p>pagina:{b.pagina}</p>
      <p>total:{b.total}</p>
      <p>paginas:{b.totalDePaginas}</p>
      {b.carregando && <p>carregando</p>}
      {b.erro !== null && <p>erro</p>}
      <ul>{b.itens.map((i) => <li key={i.id}>{i.nome}</li>)}</ul>
    </div>
  )
}

function pagina(itens: Item[], total = itens.length): PaginaDeBusca<Item> {
  return { itens, total }
}

// Timers falsos com `advanceTimersByTimeAsync`, que avança o relógio E drena as microtasks — sem
// ele o `await` da resposta ficaria pendurado e o teste leria um estado intermediário.
// `findBy*` NÃO é usado neste arquivo, de propósito: ele depende do `waitFor`, que tem seu próprio
// relacionamento com timers falsos. Avançar explicitamente e ler com `getBy*` é determinístico.
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}

describe('useBuscaPaginada', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => { vi.useRealTimers() })

  it('carrega na montagem e mostra os itens', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(buscar).toHaveBeenCalledTimes(1)
    expect(buscar).toHaveBeenCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('faz UMA requisição para três teclas digitadas em sequência', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(buscar).toHaveBeenCalledTimes(1) // só a carga inicial

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'S' } })
    fireEvent.change(campo, { target: { value: 'SU' } })
    fireEvent.change(campo, { target: { value: 'SUP' } })

    // Antes de o debounce vencer, nada saiu. Hoje, sem o hook, isto são TRÊS requisições — foi
    // medido na review da Task 6 da 1B.
    await avancar(299)
    expect(buscar).toHaveBeenCalledTimes(1)

    await avancar(1)
    expect(buscar).toHaveBeenCalledTimes(2)
    expect(buscar).toHaveBeenLastCalledWith({ busca: 'SUP', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('mostra o texto digitado no campo imediatamente, sem esperar o debounce', async () => {
    // O debounce atrasa a REQUISIÇÃO, nunca o campo. Um campo que só atualiza depois de 300 ms é
    // percebido como teclado travado.
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'SUP' } })

    expect((screen.getByLabelText('busca') as HTMLInputElement).value).toBe('SUP')
  })

  it('ignora a resposta de uma busca que já foi superada por outra', async () => {
    // A corrida real: "SU" demora 200 ms e "SUP" responde na hora. Sem guarda de sequência, a
    // resposta de "SU" chega DEPOIS e sobrescreve a lista — campo mostrando SUP, lista mostrando o
    // resultado de SU. Foi provado empiricamente na review da Task 6.
    const buscar = vi.fn((f: FiltroDeBusca) => {
      if (f.busca === 'SU') {
        return new Promise<PaginaDeBusca<Item>>((resolve) => {
          setTimeout(() => resolve(pagina([{ id: 1, nome: 'RESULTADO DE SU' }])), 200)
        })
      }
      return Promise.resolve(pagina([{ id: 2, nome: 'RESULTADO DE SUP' }]))
    })

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'SU' } })
    await avancar(300)   // dispara "SU", que só responde em +200 ms
    fireEvent.change(campo, { target: { value: 'SUP' } })
    await avancar(300)   // dispara "SUP", que responde na hora
    await avancar(500)   // deixa a resposta atrasada de "SU" chegar

    expect(screen.getByText('RESULTADO DE SUP')).toBeTruthy()
    expect(screen.queryByText('RESULTADO DE SU')).toBeNull()
  })

  it('volta para a página 1 quando a busca muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:2')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'X' } })
    await avancar(300)

    expect(screen.getByText('pagina:1')).toBeTruthy()
  })

  it('volta para a página 1 quando o filtro de inativos muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: true, pagina: 1, tamanho: 20 })
  })

  it('volta para a página 1 quando o tamanho de página muda', async () => {
    // Os TRÊS resets têm teste próprio, e cada um exige uma montagem diferente. Confundir as
    // montagens foi o erro que deixou o terceiro reset sem prova na Fase 1B.
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.change(screen.getByLabelText('tamanho'), { target: { value: '50' } })
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 50 })
  })

  it('recua a página quando o total encolhe e a página atual deixa de existir', async () => {
    // Clamp: 100 itens = 5 páginas, o usuário vai para a 3, alguém inativa 90 itens, agora são 10
    // itens = 1 página. Sem clamp a tela fica numa página vazia, com cara de bug.
    let total = 100
    const buscar = vi.fn((f: FiltroDeBusca) =>
      Promise.resolve(pagina(f.pagina === 1 ? [{ id: 1, nome: 'Sobrou' }] : [], total)),
    )

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:3')).toBeTruthy()

    total = 10
    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(screen.getByText('Sobrou')).toBeTruthy()
  })

  it('mostra "carregando" em TODA recarga, não só na primeira', async () => {
    // Este é o W3, deferido da Fase 1B com justificativa: `setCarregando(true)` no início de cada
    // carga não tinha prova nenhuma. Sem ele, depois da primeira carga o usuário nunca mais vê
    // sinal de atividade — a lista antiga fica parada na tela até a resposta nova trocar tudo de
    // repente. No wifi da fábrica são segundos olhando para dado errado sem saber.
    let liberar: (p: PaginaDeBusca<Item>) => void = () => {}
    const buscar = vi.fn()
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Antigo' }]))
      .mockImplementationOnce(() => new Promise<PaginaDeBusca<Item>>((r) => { liberar = r }))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.queryByText('carregando')).toBeNull()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)
    expect(screen.getByText('carregando')).toBeTruthy()
    expect(screen.getByText('Antigo')).toBeTruthy()  // a lista antiga segue visível, mas AVISADA

    await act(async () => { liberar(pagina([{ id: 2, nome: 'Novo' }])) })
    expect(screen.queryByText('carregando')).toBeNull()
    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('expõe o erro e o limpa quando uma recarga posterior tem sucesso', async () => {
    const buscar = vi.fn()
      .mockRejectedValueOnce(new Error('rede caiu'))
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.getByText('erro')).toBeTruthy()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.queryByText('erro')).toBeNull()
    expect(screen.getByText('Corte')).toBeTruthy()
  })

  it('sai do estado de carregando mesmo quando a busca falha', async () => {
    const buscar = vi.fn().mockRejectedValue(new Error('rede caiu'))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.queryByText('carregando')).toBeNull()
  })

  it('calcula totalDePaginas pelo total do servidor, não pelo tamanho da página recebida', async () => {
    // `total` é o total SOB O FILTRO, não `itens.length`. Confundir os dois faz a paginação sumir
    // exatamente quando ela é necessária.
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }], 41))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:3')).toBeTruthy()  // ceil(41 / 20)
  })

  it('mantém pelo menos uma página quando não há nada', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 0))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- useBuscaPaginada
```

Expected: FAIL — `Failed to resolve import "./useBuscaPaginada"`.

- [ ] **Step 4: Escrever o hook**

`web/src/hooks/useBuscaPaginada.ts`:

```ts
import { useCallback, useEffect, useRef, useState } from 'react'

export interface PaginaDeBusca<T> {
  itens: T[]
  total: number
}

export interface FiltroDeBusca {
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
}

export interface OpcoesDeBuscaPaginada<T> {
  buscar: (filtro: FiltroDeBusca) => Promise<PaginaDeBusca<T>>
  tamanhoInicial?: number
  atrasoDoDebounce?: number
}

export interface BuscaPaginada<T> {
  itens: T[]
  total: number
  totalDePaginas: number
  /** O que está no campo AGORA. Atualiza a cada tecla. */
  textoDaBusca: string
  /** O que foi realmente consultado. Atrasa `atrasoDoDebounce` em relação ao campo. */
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
  carregando: boolean
  /** O erro cru. Quem traduz para texto de tela é `mensagemDeErro` — o hook não conhece domínio. */
  erro: unknown
  mudarBusca(valor: string): void
  mudarInativos(valor: boolean): void
  mudarTamanho(valor: number): void
  irParaPagina(valor: number): void
  recarregar(): Promise<void>
}

/**
 * Busca paginada com as quatro propriedades que a Fase 1B resolveu à mão na `ComponentesPage` — e,
 * segundo a review da Task 6, resolveu errado ou sem prova:
 *
 * 1. **Debounce** — digitar "SUP" faz UMA requisição, não três.
 * 2. **Cancelamento por sequência** — vence a última requisição ENVIADA, não a última a RESPONDER.
 *    Sem isto o campo mostra "SUP" e a lista mostra o resultado de "SU".
 * 3. **Clamp de página** — se o total encolhe e a página atual deixa de existir, recua em vez de
 *    mostrar lista vazia com cara de bug.
 * 4. **Reset de filtro** — mudar busca, tamanho ou inativos volta para a página 1.
 *
 * Não usa `AbortController`: abortar exigiria que cada função de listagem aceitasse um
 * `AbortSignal`, ou seja, mudar a assinatura pública de `cadastros.ts` e o arquivo de teste de 679
 * linhas dele. A guarda de sequência entrega a mesma propriedade observável — o efeito da resposta
 * obsoleta é descartado — ao custo de a requisição obsoleta ainda trafegar. Já adjudicado duas
 * vezes neste projeto; não reabrir sem medição nova.
 */
export function useBuscaPaginada<T>({
  buscar,
  tamanhoInicial = 20,
  atrasoDoDebounce = 300,
}: OpcoesDeBuscaPaginada<T>): BuscaPaginada<T> {
  const [textoDaBusca, setTextoDaBusca] = useState('')
  const [busca, setBusca] = useState('')
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [pagina, setPagina] = useState(1)
  const [tamanho, setTamanho] = useState(tamanhoInicial)
  const [itens, setItens] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  // `true` na montagem: a primeira carga já está a caminho quando o primeiro render acontece.
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<unknown>(null)

  const sequenciaRef = useRef(0)

  // A função de busca vive num ref para que um chamador que passe uma lambda nova a cada render
  // não vire laço infinito de requisições: com ela nas dependências de `carregar`, uma lambda
  // inline dispararia carga -> render -> lambda nova -> carga. O ref quebra o ciclo, e o
  // exhaustive-deps não cobra refs.
  const buscarRef = useRef(buscar)
  useEffect(() => { buscarRef.current = buscar })

  // Debounce: o campo (`textoDaBusca`) anda na hora; a consulta (`busca`) espera o silêncio.
  // O reset de página vive AQUI, e não em `mudarBusca`, para acontecer junto com a consulta nova —
  // resetar a cada tecla dispararia uma carga por tecla, que é o que o debounce impede.
  useEffect(() => {
    if (textoDaBusca === busca) return
    const timer = setTimeout(() => {
      setPagina(1)
      setBusca(textoDaBusca)
    }, atrasoDoDebounce)
    return () => clearTimeout(timer)
  }, [textoDaBusca, busca, atrasoDoDebounce])

  const carregar = useCallback(async () => {
    const minhaSequencia = ++sequenciaRef.current
    setCarregando(true)
    try {
      const resposta = await buscarRef.current({ busca, incluirInativos, pagina, tamanho })
      if (minhaSequencia !== sequenciaRef.current) return
      setItens(resposta.itens)
      setTotal(resposta.total)
      setErro(null)
    } catch (e) {
      if (minhaSequencia !== sequenciaRef.current) return
      setErro(e)
    } finally {
      // A guarda cobre os QUATRO efeitos pós-`await`, não só os dois óbvios: sem ela a resposta
      // obsoleta apagaria o "carregando" da requisição que ainda está em voo.
      if (minhaSequencia === sequenciaRef.current) setCarregando(false)
    }
  }, [busca, incluirInativos, pagina, tamanho])

  useEffect(() => { carregar() }, [carregar])

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  // Clamp: só depois de a carga assentar, para não recuar a página no meio de uma requisição cujo
  // `total` ainda é o antigo.
  useEffect(() => {
    if (!carregando && pagina > totalDePaginas) setPagina(totalDePaginas)
  }, [carregando, pagina, totalDePaginas])

  const mudarBusca = useCallback((valor: string) => { setTextoDaBusca(valor) }, [])

  const mudarInativos = useCallback((valor: boolean) => {
    setPagina(1)
    setIncluirInativos(valor)
  }, [])

  const mudarTamanho = useCallback((valor: number) => {
    setPagina(1)
    setTamanho(valor)
  }, [])

  const irParaPagina = useCallback((valor: number) => { setPagina(valor) }, [])

  return {
    itens, total, totalDePaginas,
    textoDaBusca, busca,
    incluirInativos, pagina, tamanho,
    carregando, erro,
    mudarBusca, mudarInativos, mudarTamanho, irParaPagina,
    recarregar: carregar,
  }
}
```

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- useBuscaPaginada
```

Expected: `Tests  13 passed (13)`. Se o teste do clamp falhar por uma renderização a mais, **não relaxe a asserção** — confira a ordem dos efeitos.

- [ ] **Step 6: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  139 passed (139)` (**126 medido** ao fim da Task 2 + 13 desta task) · build limpo · lint só com o warning alheio.

*(**Este número foi corrigido em 2026-08-07, e o erro é o terceiro do mesmo tipo neste plano.** Dizia `136 (123 + 13)`: o `123` era a contagem obsoleta da Task 2, já substituída por 127 na estimativa e depois medida em **126**. O delta `+13` é o que vincula — se você medir 139 com o delta certo, está correto; se medir outro total **com o delta certo**, reporte o número real e siga, conforme a constraint global. Não invente nem apague teste para fechar conta.)*

- [ ] **Step 7: Medir as mutações**

| # | Mutação em `useBuscaPaginada.ts` | Mortes | O que passa a acontecer em produção se ela sobreviver |
|---|---|---|---|
| M1 | `atrasoDoDebounce` → `0` no `setTimeout` | ≥ 1 | 3 teclas = 3 requisições |
| M2 | remover a guarda de sequência do bloco de sucesso | ≥ 1 | resposta obsoleta sobrescreve a lista |
| M3 | remover `setCarregando(true)` do início de `carregar` | ≥ 1 | **é o W3** — sem sinal de atividade nas recargas |
| M4 | remover o `useEffect` do clamp | ≥ 1 | página órfã depois que o total encolhe |
| M5 | remover `setPagina(1)` do debounce | ≥ 1 | buscar estando na página 7 mostra vazio |
| M6 | remover `setPagina(1)` de `mudarInativos` | ≥ 1 | idem, pelo checkbox |
| M7 | remover `setPagina(1)` de `mudarTamanho` | ≥ 1 | idem, pelo seletor |
| M8 | `Math.max(1, …)` → `Math.ceil(total / tamanho)` | ≥ 1 | "Página 1 de 0" na lista vazia |
| M9 | `setTotal(resposta.total)` → `setTotal(resposta.itens.length)` | ≥ 1 | paginação some justamente quando é necessária |
| M10 | remover `setErro(null)` do sucesso | ≥ 1 | erro antigo gruda na tela |
| M11 | remover a guarda de sequência do `finally` | ≥ 1 | "carregando" some com a resposta errada |

**M5, M6 e M7 exigem montagens DIFERENTES** — cada reset tem o seu teste, e as montagens são opostas. Medir as três com a mesma montagem faz você reportar morte onde não há; foi assim que o terceiro reset ficou sem prova na Fase 1B.

- [ ] **Step 8: Commit**

```bash
git add web/src/hooks/useBuscaPaginada.ts web/src/hooks/useBuscaPaginada.test.tsx
git commit -m "feat(web): useBuscaPaginada com debounce, cancelamento, clamp e W3"
```

**Definition of done da Task 3:** suíte em **139** (= baseline 126 medida + **delta 13**, e o delta é o que vincula); as 11 mutações medidas com ≥ 1 morte cada; **nenhuma tela modificada**; build e lint limpos.

**CUMPRIDA em 2026-08-07, e o número medido é 140 — delta +14, não +13.** O 14º teste não é enfeite nem conta forçada: **três dos onze mutantes sobreviveram** à suíte que este plano especificou, e todos os três eram de comportamento assíncrono. **M11 (guarda do `finally`) sobrevivia porque nenhum teste do plano tinha duas requisições em voo ao mesmo tempo** — o teste que faltava é o 14º. Os outros dois eram testes que mediam a coisa errada: **M2** (guarda de sequência) tinha um teste que *não criava corrida*, porque `avancar(300)` engolia a resposta lenta dentro da própria janela e ela chegava antes, não depois; **M4** (clamp) disparava a recarga pelo checkbox de inativos, e `mudarInativos` já chama `setPagina(1)` sozinho, então media o reset da M6. Nenhuma asserção foi relaxada para fechar nenhum dos três. Detalhe em `.superpowers/sdd/fase1d-task-3-report.md`.

**FECHADA DE VERDADE em 2026-08-08, em 144 — depois de TRÊS fix passes e DUAS re-reviews.** Os 140 acima eram o estado antes de a review ser adjudicada. Trajetória: 140 → fix1 `4cf13f6` (+3, cobertura das opções públicas) → **143** → fix2 `46ba224` (+1, o frescor do `buscarRef`) → **144** → fix3 `5bc6579` (+0, só comentários e uma asserção). **Nenhuma linha de produção foi alterada em nenhum dos três** — `useBuscaPaginada.ts` é hoje byte-idêntico ao de `90ceae4`.

**O que os três fix passes ensinaram, e vale para as tasks 4 a 12:**

1. **Um mecanismo pode ter mais de uma propriedade, e fechar uma não fecha a outra.** O `buscarRef` tem **estabilidade** (não laçar) e **frescor** (não usar closure obsoleto). O fix1 fechou a primeira e ninguém notou que a segunda seguia com cobertura zero — apagar `useBuscaPaginada.ts:82` deixava a suíte inteira verde. Quem achou foi o revisor, **fora do mandato dele**, auditando a outra metade. Ao cobrir um mecanismo, pergunte quantas propriedades ele tem.
2. **Morte por timeout é morte de baixa especificidade, e pode ser aceitável.** A mutação do laço infinito só pode matar por não-terminação — nenhuma asserção roda. Foi julgado aceitável porque quem morre é o próprio teste do mecanismo, diferente do B1 da Task 1, onde o timeout era colateral. Mas o teto passou a ser explícito (`{ timeout: 1000 }`), medido com folga de ~1000×, para a defesa não depender de config global que ninguém escolheu.
3. **O vício de comentário que encena precisão chegou a OITO ocorrências nesta fase — e as ocorrências 7 e 8 nasceram dentro da correção da 6ª.** Um fix pass de comentário é um lugar de risco alto, não baixo. O que funcionou foi o brief **entregar os fatos medidos** em vez de mandar o fixer derivá-los, e dizer explicitamente **onde parar de explicar**.
4. **Correção proposta por revisor read-only também precisa de medição.** A review propôs um texto para o comentário do laço (*"trava no `await avancar(0)`"*) que ela mesma rotulou como inferência. Medido com sondas: **falso** — `render()` retorna, `avancar(0)` retorna, e a asserção é avaliada e falha; o que aflora é o timeout. Aplicar a sugestão teria produzido a nona ocorrência. **Inferência rotulada é honesta e útil; mas se vira texto commitado, mede-se antes.**

---

### Task 4: Tokens de tema e a prova automatizada de contraste

**Files:**
- Create: `web/src/tema/contraste.ts`
- Create: `web/src/tema/contraste.test.ts`
- Modify: `web/src/index.css` (hoje tem **uma** linha)
- Modify: `web/src/components/TelaCarregando.tsx` (passa a usar os tokens)
- Create: `web/tsconfig.test.json` — **acrescentado em 2026-08-08**, ver a caixa do Step 3
- Modify: `web/tsconfig.app.json` (`exclude` dos arquivos de teste) — idem
- Modify: `web/tsconfig.json` (referência ao projeto novo) — idem

**Interfaces:**
- Consumes: nada.
- Produces: as classes utilitárias do Tailwind derivadas dos tokens — `bg-chrome`, `text-marca`, `bg-acao`, `text-tinta`, `border-borda-campo`, `font-mono`, etc. **Todas as tasks de 5 em diante usam só estas classes; nenhuma volta a escrever `text-gray-*` ou `bg-gray-*`.**

**A propriedade que esta task instala, e é a mais valiosa da fase:** a spec §3 diz *"todo tom novo que a implementação precisar tem de ser medido antes de entrar"*, e registra a armadilha — tons claros de verde-água e âmbar reprovam em AA como texto sobre branco **apesar de** funcionarem como fundo de botão com texto branco. Escolher paleta olhando só o botão é o erro clássico. Aqui isso deixa de depender de disciplina: o teste **lê o `@theme` do `index.css`** e mede. Token novo sem par declarado **reprova a suíte**.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: **144 testes / 12 arquivos** — número **medido em 2026-08-08** ao fechar a Task 3 de verdade, depois de três fix passes e duas re-reviews. Se divergir de 144, pare e reporte.

*(A estimativa original era 139; a implementação mediu 140, e os fix passes levaram a 143 e depois 144. **O 140 que este Step dizia antes está obsoleto** — era o estado anterior à adjudicação da review. Ver a caixa da definition of done da Task 3 para a trajetória. Nenhuma linha de produção mudou em nenhum dos três fix passes.)*

- [ ] **Step 2: Escrever as funções de contraste, que ainda não existem**

`web/src/tema/contraste.ts`:

```ts
/**
 * Luminância relativa de uma cor `#RRGGBB`, pela fórmula da WCAG 2.x.
 *
 * Implementada à mão em nove linhas em vez de trazer uma dependência: a fase inteira existe para
 * *reduzir* superfície (a Task 5 da 1B recusou até `jest-dom`), e o que se ganharia era isto aqui.
 */
export function luminanciaRelativa(hex: string): number {
  const canais = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const [r, g, b] = canais.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** Razão de contraste entre duas cores `#RRGGBB`. Vai de 1 (idênticas) a 21 (preto/branco). */
export function razaoDeContraste(a: string, b: string): number {
  const la = luminanciaRelativa(a)
  const lb = luminanciaRelativa(b)
  const [claro, escuro] = la > lb ? [la, lb] : [lb, la]
  return (claro + 0.05) / (escuro + 0.05)
}
```

- [ ] **Step 3: Escrever o teste que MEDE o `@theme`**

`web/src/tema/contraste.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { razaoDeContraste, luminanciaRelativa } from './contraste'

// Lê o CSS de verdade, em vez de duplicar a paleta num objeto TS. Duplicar criaria a divergência
// clássica: alguém muda o token no CSS, o teste segue medindo o valor velho e reporta verde sobre
// uma cor que já não está na tela.
const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

function tokens(): Record<string, string> {
  const bloco = css.match(/@theme\s*\{([\s\S]*?)\n\}/)
  if (!bloco) throw new Error('bloco @theme não encontrado em index.css')
  const mapa: Record<string, string> = {}
  for (const [, nome, valor] of bloco[1].matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    mapa[nome] = valor
  }
  return mapa
}

const T = tokens()

// AA da WCAG: 4.5:1 para texto normal, 3:1 para componente de interface (borda de campo, ícone).
const TEXTO = 4.5
const INTERFACE = 3

const PARES: Array<{ frente: string; fundo: string; minimo: number; onde: string }> = [
  { frente: 'tinta', fundo: 'superficie', minimo: TEXTO, onde: 'texto padrão sobre cartão' },
  { frente: 'tinta', fundo: 'fundo', minimo: TEXTO, onde: 'texto padrão sobre o fundo da página' },
  { frente: 'tinta-fraca', fundo: 'superficie', minimo: TEXTO, onde: 'texto secundário e legendas' },
  { frente: 'superficie', fundo: 'chrome', minimo: TEXTO, onde: 'texto da barra de navegação' },
  { frente: 'marca', fundo: 'chrome', minimo: TEXTO, onde: 'logo sobre o chrome' },
  { frente: 'superficie', fundo: 'acao', minimo: TEXTO, onde: 'rótulo do botão primário' },
  { frente: 'superficie', fundo: 'acao-forte', minimo: TEXTO, onde: 'rótulo do primário sob hover' },
  { frente: 'acao', fundo: 'superficie', minimo: TEXTO, onde: 'link de ação e botão secundário' },
  { frente: 'acao', fundo: 'acao-fundo', minimo: TEXTO, onde: 'texto da pílula' },
  { frente: 'negativo', fundo: 'superficie', minimo: TEXTO, onde: 'mensagem de erro' },
  { frente: 'superficie', fundo: 'negativo', minimo: TEXTO, onde: 'rótulo do botão de perigo' },
  { frente: 'positivo-texto', fundo: 'superficie', minimo: TEXTO, onde: 'rótulo de estado aprovado/ativo' },
  { frente: 'superficie', fundo: 'positivo', minimo: TEXTO, onde: 'selo de estado positivo com texto branco' },
  { frente: 'borda-campo', fundo: 'superficie', minimo: INTERFACE, onde: 'borda de input e de botão secundário' },
  { frente: 'acao', fundo: 'fundo', minimo: INTERFACE, onde: 'anel de foco sobre o fundo da página' },
]

/**
 * Tokens que existem só como fundo ou separador e por isso não têm par de contraste próprio.
 * Cada exceção precisa de motivo escrito — a lista é curta de propósito: é ela que impede o
 * "declaro como decorativo e passo" de virar a saída fácil.
 */
const SEM_EXIGENCIA: Record<string, string> = {
  borda: 'separador decorativo entre linhas de lista; nenhuma informação depende de enxergá-lo',
}

describe('paleta declarada em index.css', () => {
  it('declara todos os tokens que o plano da fase fixou', () => {
    for (const nome of [
      'chrome', 'marca', 'acao', 'acao-forte', 'acao-fundo',
      'positivo', 'positivo-texto', 'negativo',
      'tinta', 'tinta-fraca', 'borda', 'borda-campo', 'fundo', 'superficie',
    ]) {
      expect(T[nome], `token --color-${nome} ausente em index.css`).toBeTruthy()
    }
  })

  it.each(PARES)('$frente sobre $fundo passa AA ($onde)', ({ frente, fundo, minimo }) => {
    const razao = razaoDeContraste(T[frente], T[fundo])
    expect(razao, `${frente} sobre ${fundo} deu ${razao.toFixed(2)}:1`).toBeGreaterThanOrEqual(minimo)
  })

  it('não deixa entrar tom novo sem medição', () => {
    // A guarda que faz a regra da spec §3 valer sozinha: quem acrescentar um `--color-*` tem de
    // declarar onde ele é usado, ou registrá-lo em SEM_EXIGENCIA com motivo.
    const medidos = new Set(PARES.flatMap((p) => [p.frente, p.fundo]))
    const naoMedidos = Object.keys(T).filter((n) => !medidos.has(n) && !(n in SEM_EXIGENCIA))

    expect(naoMedidos, `tokens sem par de contraste declarado: ${naoMedidos.join(', ')}`).toEqual([])
  })

  it('mantém verde e vermelho reservados a estado — nenhum deles é o chrome nem a ação', () => {
    // A regra que sustenta a paleta (spec §3): cor de identidade nunca significa estado. Ela é o
    // que faz a tela de Qualidade da Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho"
    // aparecem na mesma linha.
    expect(T.chrome).not.toBe(T.positivo)
    expect(T.chrome).not.toBe(T.negativo)
    expect(T.acao).not.toBe(T.positivo)
    expect(T.acao).not.toBe(T.negativo)
  })
})

describe('razaoDeContraste', () => {
  it('dá 21 entre preto e branco', () => {
    expect(razaoDeContraste('#000000', '#ffffff')).toBeCloseTo(21, 1)
  })

  it('dá 1 para a mesma cor', () => {
    expect(razaoDeContraste('#134E4A', '#134E4A')).toBeCloseTo(1, 5)
  })

  it('é simétrica', () => {
    expect(razaoDeContraste('#134E4A', '#ffffff')).toBeCloseTo(razaoDeContraste('#ffffff', '#134E4A'), 10)
  })

  it('linearizea o canal escuro pela rampa, não pela potência', () => {
    // Abaixo de 0.03928 a WCAG usa `c / 12.92`, não a exponencial. Trocar por `** 2.4` em toda a
    // faixa erra justamente nos tons muito escuros — que é onde vive o chrome.
    expect(luminanciaRelativa('#050505')).toBeCloseTo(0.00152, 4)
  })
})
```

> **O `node:fs` acima quebra o Step 7 deste plano — corrigido em 2026-08-08, com medição.**
>
> `tsconfig.app.json` tinha `"types": ["vite/client"]` e `"include": ["src"]`, então ele
> type-checava os arquivos de teste sem os tipos do Node: `npm run build` parava em
> `src/tema/contraste.test.ts(2,30): error TS2591: Cannot find name 'node:fs'`. **A suíte ficava
> verde assim mesmo** — o Vitest não usa o `tsc`. Segunda vez nesta fase que verde do Vitest não
> prova que compila.
>
> As três saídas óbvias foram medidas antes de escolher, e **duas delas eram armadilha**:
>
> | Saída | Build | Barra Node em código de app? | Teste roda? |
> |---|---|---|---|
> | `"types": [..., "node"]` no app | passa | **não** (inferido — mesmo mecanismo da linha abaixo) | sim |
> | `/// <reference types="node" />` no teste | passa | **não** — sonda com `process.platform` e `import 'node:fs'` num arquivo de app **compilou** | sim |
> | `import css from '../index.css?raw'` | passa | sim | **não** — o plugin do Tailwind intercepta e o `?raw` devolve **string vazia** |
> | **`tsconfig.test.json` separado** ← adotada | passa | **sim** — a mesma sonda volta a dar `TS2591` | sim, **166** |
>
> O `/// <reference>` parece contido e não é: ele carrega o pacote de tipos no **programa inteiro**,
> então é o mesmo vazamento do `types`, só que escondido num arquivo de teste em vez de declarado no
> tsconfig. O `?raw` é pior ainda: passa no build, e falha por **zero silencioso** — a mesma classe
> do achado M5 desta fase. Aqui só não passou em falso porque o `tokens()` tem `if (!bloco) throw`.
>
> A saída adotada exclui `src/**/*.test.ts(x)` e `src/testes` do `tsconfig.app.json` e cria um
> `tsconfig.test.json` com `"types": ["vite/client", "node"]`, referenciado pelo `tsconfig.json`.
> **Os testes continuam type-checados** — medido plantando `const x: number = "texto"` no
> `contraste.test.ts`, e o build pegou (`TS2322`). Isso vale para o projeto inteiro daqui em
> diante, não só para esta task: teste pode usar Node, código de app não.

- [ ] **Step 4: Rodar para ver falhar**

```bash
cd web && npm test -- contraste
```

Expected: FAIL — `bloco @theme não encontrado em index.css`.

- [ ] **Step 5: Escrever o `@theme`**

`web/src/index.css`, substituindo a única linha de hoje:

```css
@import "tailwindcss";

/*
  Tokens da Fase 1D. A paleta e a regra que a sustenta estão em
  `docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md` §3.

  REGRA: cor de identidade nunca significa estado; cor de estado nunca decora. Verde e vermelho
  são RESERVADOS — aprovado/ativo e reprovado/perda/erro. É isso que faz a tela de Qualidade da
  Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho" dividem a mesma linha.

  Todo tom novo aqui precisa de par declarado em `src/tema/contraste.test.ts`, senão a suíte
  reprova. Não é zelo: os tons claros de verde-água (#0D9488) e âmbar (#D97706) REPROVAM em AA
  como texto sobre branco apesar de funcionarem como fundo de botão — escolher paleta olhando só
  o botão é o erro clássico, e o teste existe para que ele não passe.
*/
@theme {
  /* Pilha do sistema: zero asset para baixar no wifi da fábrica, e trocar por fonte própria
     depois é mudar este token, não reescrever tela. Monoespaçada para código de peça e material —
     decisão funcional (alinha na coluna, facilita conferir contra o desenho na bancada). */
  --font-sans: ui-sans-serif, system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  --font-mono: ui-monospace, "Cascadia Mono", "Segoe UI Mono", "Roboto Mono", Menlo, monospace;

  /* Identidade */
  --color-chrome: #134E4A;       /* barra de navegação e cabeçalho */
  --color-marca: #5EEAD4;        /* logo, SEMPRE sobre o chrome escuro */

  /* Ação — mesma família do chrome, por escolha explícita do usuário contra a recomendação de
     dois matizes. O custo é real e a compensação é de FORMA, não de cor: o primário ganha peso
     por tamanho e densidade tipográfica, e o contorno neutro fica reservado ao secundário.
     Nenhuma tela deve ter dois botões com o mesmo peso visual. */
  --color-acao: #3E6E68;
  --color-acao-forte: #2F544F;   /* hover/active do primário */
  --color-acao-fundo: #E8F0EF;   /* fundo tingido da pílula; a tinta é a MESMA --color-acao */

  /* Estado — reservados */
  --color-positivo: #166534;         /* SÓ como fundo, com texto branco (7,13:1) */
  --color-positivo-texto: #15803D;   /* o tom que passa AA como TEXTO sobre claro */
  --color-negativo: #DC2626;         /* passa AA nos dois papéis */

  /* Neutros, com viés verde-acinzentado para casarem com o chrome */
  --color-tinta: #1C2422;
  --color-tinta-fraca: #55605D;
  --color-borda: #D3DEDB;        /* separador decorativo */
  --color-borda-campo: #6B7A77;  /* borda de input/botão secundário — precisa de 3:1 */
  --color-fundo: #F7FAF9;
  --color-superficie: #FFFFFF;
}
```

**Repare no par `positivo` / `positivo-texto`, porque é a armadilha da spec em ato:** o verde vivo `#16A34A` — que era o valor original deste plano — dá **3,30:1** contra branco e **reprova AA nos DOIS papéis**, como texto sobre branco *e* como fundo de selo com texto branco.

> **CORREÇÃO DE 2026-08-08, feita no pré-flight da task, e a lição é maior que o número.** Este parágrafo dizia que `#16A34A` reprovava como texto "embora branco *sobre* ele passe com folga". **Isso é falso: razão de contraste é simétrica** — e o próprio teste desta task tem um caso (`é simétrica`) que o afirma. O par `superficie / positivo` teria **reprovado na primeira execução**, em 3,30:1, e o implementer bateria de frente com um plano que se contradiz. Medido antes de despachar: dos 15 pares, 14 passavam e **só este falhava**. `--color-positivo` passou a **`#166534`** (branco sobre ele = **7,13:1**), escolha do usuário entre as alternativas medidas, porque preserva a intenção original de dois tons com papéis distintos — `#166534` como fundo de selo, `#15803D` (5,02:1) como verde de texto sobre claro.

Por isso são dois tokens com papéis diferentes, e por isso o teste mede os dois sentidos.

**Valores esperados — os 15 pares, MEDIDOS por mim em 2026-08-08 com a fórmula deste Step, não estimados.** Se você medir algo diferente, foi você que mudou um tom (ou a fórmula está errada, o que é achado):

| par | razão | mínimo |
|---|---|---|
| tinta / superficie | 15,85 | 4,5 |
| tinta / fundo | 15,09 | 4,5 |
| tinta-fraca / superficie | 6,53 | 4,5 |
| superficie / chrome | 9,48 | 4,5 |
| marca / chrome | 6,41 | 4,5 |
| superficie / acao | 5,78 | 4,5 |
| superficie / acao-forte | 8,39 | 4,5 |
| acao / superficie | 5,78 | 4,5 |
| acao / acao-fundo | **4,99** | 4,5 |
| negativo / superficie | 4,83 | 4,5 |
| superficie / negativo | 4,83 | 4,5 |
| positivo-texto / superficie | 5,02 | 4,5 |
| superficie / positivo | 7,13 | 4,5 |
| borda-campo / superficie | 4,49 | 3 |
| acao / fundo | 5,50 | 3 |

**A menor margem da paleta é `acao / acao-fundo`, em 4,99:1** — 0,49 acima do mínimo. Qualquer clareamento do `--color-acao` ou escurecimento do `--color-acao-fundo` a derruba primeiro.

**Se algum par reprovar, escureça o tom até passar e reporte o valor final** — não relaxe o mínimo, e não remova o par da lista.

- [ ] **Step 6: Rodar para ver passar**

```bash
cd web && npm test -- contraste
```

Expected: `Tests  22 passed (22)` — **15** pares (`it.each`) + **3** no bloco da paleta (declara os tokens, não deixa entrar tom novo, mantém verde/vermelho reservados) + **4** no bloco `razaoDeContraste`. Se algum par reprovar, ajuste o **tom**, nunca o limite.

> **Este número era `20 (15 pares + 5 dos outros blocos)` e estava errado — corrigido no pré-flight de 2026-08-08, contando os `it(` do Step 3 um a um.** Os "outros blocos" somam **7**, não 5. **É o quarto erro de contagem deste plano**, e a mesma classe dos outros três: número somado de cabeça em vez de contado. Consequência evitada: o implementer veria 22, leria "esperado 20" e pararia por alarme falso — foi exatamente isso que quase travou a Task 2.

- [ ] **Step 7: Confirmar que o Tailwind gera as classes**

```bash
cd web && npm run build
```

Expected: build limpo. O CSS emitido cresce em relação aos ~9,9 kB da baseline — é esperado.

Confirme que as utilitárias existem de fato, e não só os custom properties:

```bash
cd web && grep -c "bg-chrome\|text-marca" dist/assets/*.css
```

Expected: `0` **por enquanto** — o Tailwind v4 só emite a classe quando algum arquivo a usa, e nenhuma tela usa ainda. A conferência de verdade acontece na Task 5, quando a primeira primitiva referenciar as classes. **Não conclua daqui que os tokens não funcionaram.**

- [ ] **Step 8: Migrar a `TelaCarregando` para os tokens**

É a primeira consumidora e a mais simples — serve de prova de fogo do `@theme`.

`web/src/components/TelaCarregando.tsx`:

```tsx
// Estado 'carregando' da sessao. Texto explicito de proposito: numa conexao lenta (wifi de
// chao de fabrica) uma tela em branco faz o usuario recarregar, e o reload no meio do
// init-refresh pode derrubar a sessao. Mostrar que algo acontece tira o incentivo de recarregar.
export function TelaCarregando() {
  return (
    <div className="min-h-screen bg-fundo flex flex-col items-center justify-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-borda border-t-acao" />
      <p className="text-tinta-fraca">Restaurando sessão…</p>
    </div>
  )
}
```

- [ ] **Step 9: Confirmar que a classe agora é emitida**

```bash
cd web && npm run build && grep -c "border-t-acao" dist/assets/*.css
```

Expected: ≥ `1`. **Este é o passo que prova que o `@theme` está ligado ao Tailwind** — se der `0`, o bloco não está sendo lido (confira que ele vem **depois** do `@import "tailwindcss"`).

- [ ] **Step 10: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: **a baseline que você mediu no Step 1 desta task + 22** (≈ 166 sobre os 144 da Task 3). **O que vincula é o delta `+22`**, não o absoluto: reporte o total real medido e siga. Se a guarda do M8 entrar, o delta vira `+23`.

*(Este Step dizia `+20` até 2026-08-08 — era o resíduo do quarto erro de contagem do plano, corrigido no pré-flight só no DoD e não aqui. **Medido em 2026-08-08: 166 testes / 13 arquivos**, com os Steps 1 a 7 aplicados.)*

- [ ] **Step 11: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `index.css` — trocar `--color-acao-fundo` por `#F3F7F6` (clarear a pílula, aumentando o contraste) | **0** — é a direção segura; serve de **controle**, para confirmar que o teste não é um carimbo |
| M2 | `index.css` — trocar `--color-tinta-fraca` por `#8A9693` (clarear até reprovar) | ≥ 1 |
| M3 | `index.css` — trocar `--color-positivo-texto` pelo literal **`#16A34A`** | ≥ 1 — **é a armadilha da spec, encenada.** Morre no par `positivo-texto / superficie` (3,30:1 < 4,5), medido. **⚠️ Use o literal, NÃO "o valor do `positivo`":** desde a correção de 2026-08-08 o `positivo` é `#166534`, que dá **7,13:1** e **sobreviveria** à mutação. O texto antigo desta linha dizia "(o valor do `positivo`)" e teria produzido um mutante equivalente. |
| M4 | `index.css` — acrescentar `--color-ambar: #D97706;` sem declarar par | ≥ 1 (o teste "não deixa entrar tom novo sem medição") |
| M5 | `contraste.ts` — trocar `c <= 0.03928 ? c / 12.92 : …` por só a exponencial | ≥ 1 |
| M6 | `contraste.ts` — trocar os coeficientes `0.2126 / 0.7152 / 0.0722` por `1/3` cada | ≥ 1 |
| M7 | `index.css` — trocar `--color-marca` por `#16A34A` | ≥ 1, **mas confirme ONDE:** morre no par **`marca / chrome`** (2,87:1 < 4,5), medido em 2026-08-08. **NÃO** morre pelo teste de identidade × estado, como esta linha afirmava antes — aquele teste só compara `chrome` e `acao` contra `positivo` e `negativo`, e **não toca em `marca`**. Se a sua medição matar noutro lugar, reporte. |

**M1 é controle deliberado, e o único da fase cuja resposta certa é zero.** Reporte-a assim — uma mutação que melhora o contraste *deve* passar. Se ela matar alguma coisa, o teste está preso ao valor em vez de à propriedade.

- [ ] **Step 12: Commit**

```bash
git add web/src/index.css web/src/tema/contraste.ts web/src/tema/contraste.test.ts web/src/components/TelaCarregando.tsx \
        web/tsconfig.json web/tsconfig.app.json web/tsconfig.test.json
git commit -m "feat(web): tokens de tema com prova automatizada de contraste AA"
```

**Definition of done da Task 4:** suíte em **baseline medida + 22** (≈ **166**, se a baseline for os 144 esperados — **o delta é o que vincula**, não o total; o delta era `+20` e foi corrigido no pré-flight de 2026-08-08, contando os `it(` do Step 3); `grep -c "border-t-acao" dist/assets/*.css` ≥ 1; as 7 mutações medidas (M1 com 0 mortes, as outras com ≥ 1); os valores de contraste medidos **reportados um a um** no relatório.

---

### Task 5: Primitivas de página e formulário — `Pagina`, `Botao`, `Campo`, `BannerDeErro`

**Files:**
- Create: `web/src/components/Pagina.tsx` + `Pagina.test.tsx`
- Create: `web/src/components/Botao.tsx` + `Botao.test.tsx`
- Create: `web/src/components/Campo.tsx` + `Campo.test.tsx`
- Create: `web/src/components/BannerDeErro.tsx` + `BannerDeErro.test.tsx`

**Interfaces:**
- Consumes: os tokens da Task 4.
- Produces:

```ts
export function Pagina(props: { titulo: string; acao?: ReactNode; children: ReactNode }): JSX.Element
export function Botao(props: ButtonHTMLAttributes<HTMLButtonElement> & {
  variante?: 'primario' | 'secundario' | 'perigo'
  carregando?: boolean
  rotuloCarregando?: string
}): JSX.Element
export function Campo(props: { rotulo: string; children: (id: string, idDaDica?: string) => ReactNode; dica?: string }): JSX.Element
export function BannerDeErro(props: { mensagem: string | null }): JSX.Element | null
```

**Critério de pronto de uma primitiva (spec §5):** ela é usada por **pelo menos duas telas** e **não sobrou nenhuma cópia da forma antiga** no repositório. As quatro daqui têm 6, 7, 6 e 7 consumidores respectivamente — a conferência final é a Task 12.

**A regra de peso visual (spec §3), que vale para todas as tasks seguintes:** como a ação é monocromática com o chrome, o botão primário fica discreto, e **botão discreto custa clique**. A compensação é de forma: o primário ganha peso por tamanho e densidade tipográfica, e o contorno neutro fica reservado ao secundário. **Nenhuma tela deve ter dois botões com o mesmo peso visual.**

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: **`Tests  172 passed (172)`** — a Task 4 fechou aí, e o número foi **medido em 2026-08-09**, depois do fix pass da review dela (`3768888`). Este Step dizia `156`, que era estimativa morta. Se você medir outro número, **pare e reporte**: divergência na baseline é sinal real, ao contrário de divergência no total derivado.

- [ ] **Step 2: Escrever o teste do `Botao`, que ainda não existe**

`web/src/components/Botao.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { Botao } from './Botao'

afterEach(cleanup)

describe('Botao', () => {
  it('renderiza o rótulo e dispara o clique', () => {
    const aoClicar = vi.fn()
    render(<Botao onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByText('Adicionar'))

    expect(aoClicar).toHaveBeenCalledTimes(1)
  })

  it('desabilita e troca o rótulo enquanto a mutação está em voo', () => {
    // A dívida "botão desabilitado durante mutação" (spec §9). Sem isto, o PCP clica "Adicionar"
    // duas vezes no wifi lento e o segundo POST volta 409 — a tela acusando de duplicado o
    // cadastro que ela mesma acabou de fazer.
    render(<Botao carregando rotuloCarregando="Salvando…">Adicionar</Botao>)

    const botao = screen.getByRole('button') as HTMLButtonElement
    expect(botao.disabled).toBe(true)
    expect(botao.textContent).toBe('Salvando…')
  })

  it('não dispara o clique quando está carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao carregando onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('mantém o rótulo normal quando carregando não recebe rótulo próprio', () => {
    render(<Botao carregando>Adicionar</Botao>)

    expect(screen.getByRole('button').textContent).toBe('Adicionar')
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(true)
  })

  it('respeita o disabled vindo de fora, sem estar carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao disabled onClick={aoClicar}>Anterior</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('dá pesos visuais diferentes a primário, secundário e perigo', () => {
    // A propriedade sob teste é "os três são DISTINGUÍVEIS", não a lista exata de classes: um
    // teste que fixasse as classes quebraria em todo ajuste de espaçamento e viraria ruído.
    const { container } = render(
      <div>
        <Botao variante="primario">P</Botao>
        <Botao variante="secundario">S</Botao>
        <Botao variante="perigo">X</Botao>
      </div>,
    )
    // Classe a classe (`split`), NUNCA `toContain` sobre a string inteira: a classe do secundário
    // termina em `hover:bg-acao-fundo`, que **contém `bg-acao` como substring**. MEDIDO no
    // pré-flight de 2026-08-09: um `toContain('bg-acao')` sobre a string casa com o secundário e
    // não distingue coisa nenhuma. Sobre o array, `toContain` exige o token exato.
    const classes = Array.from(container.querySelectorAll('button')).map((b) => b.className.split(/\s+/))

    expect(new Set(classes.map((c) => c.join(' '))).size).toBe(3)
    expect(classes[0]).toContain('bg-acao')       // primário: preenchido
    expect(classes[1]).toContain('border')        // secundário: contorno neutro
    expect(classes[2]).toContain('bg-negativo')   // perigo: vermelho reservado a estado
  })

  it('usa primário quando a variante não é dita', () => {
    render(<Botao>Adicionar</Botao>)

    // Mesmo motivo do teste acima, e aqui é o que dá sentido ao M14: sobre a string inteira, trocar
    // o default para `secundario` PASSARIA, porque `hover:bg-acao-fundo` contém `bg-acao`.
    expect(screen.getByRole('button').className.split(/\s+/)).toContain('bg-acao')
  })

  it('tem indicação de foco visível', () => {
    // Critério de aceite da spec §11: foco visível em todo controle interativo. `focus-visible`,
    // e não `focus`: o anel não deve aparecer no clique de mouse, só na navegação por teclado.
    render(<Botao>Adicionar</Botao>)

    // O token EXATO, não o prefixo: `toContain('focus-visible:outline')` sobre a string casava
    // também com `focus-visible:outline-offset-2` e `focus-visible:outline-acao`, que sobrariam
    // depois da M5. MEDIDO no pré-flight de 2026-08-09 — a M5 sobrevivia à asserção antiga.
    expect(screen.getByRole('button').className.split(/\s+/)).toContain('focus-visible:outline-2')
  })

  it('é do tipo button por padrão, para não submeter formulário sem querer', () => {
    // `<button>` sem `type` dentro de `<form>` é `submit` por especificação. Todo botão de ação
    // secundária dentro de um formulário submeteria o formulário — defeito silencioso e clássico.
    render(<Botao>Cancelar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('button')
  })

  it('aceita type=submit quando o chamador pede', () => {
    render(<Botao type="submit">Adicionar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('submit')
  })
})
```

- [ ] **Step 3: Rodar para ver falhar**

```bash
cd web && npm test -- Botao
```

Expected: FAIL — `Failed to resolve import "./Botao"`.

- [ ] **Step 4: Escrever o `Botao`**

`web/src/components/Botao.tsx`:

```tsx
import type { ButtonHTMLAttributes, ReactNode } from 'react'

export type VarianteDeBotao = 'primario' | 'secundario' | 'perigo'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: VarianteDeBotao
  /** Mutação em voo: desabilita e, se houver, troca o rótulo. */
  carregando?: boolean
  rotuloCarregando?: string
  children: ReactNode
}

// `focus-visible` (e não `focus`): o anel aparece na navegação por teclado e não no clique de
// mouse. Critério de aceite da spec §11.
const BASE =
  'inline-flex items-center justify-center rounded-lg transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao ' +
  'disabled:opacity-50 disabled:cursor-not-allowed'

// O peso visual é o que compensa a ação ser monocromática com o chrome (spec §3): o primário é
// maior e mais denso; o secundário é contorno neutro. Dois botões com o mesmo peso na mesma tela
// devolvem ao usuário a decisão que o desenho deveria ter tomado por ele.
const POR_VARIANTE: Record<VarianteDeBotao, string> = {
  primario: 'bg-acao text-superficie px-5 py-2.5 font-semibold hover:bg-acao-forte',
  secundario: 'border border-borda-campo text-tinta px-4 py-2 hover:bg-acao-fundo',
  perigo: 'bg-negativo text-superficie px-5 py-2.5 font-semibold hover:brightness-90',
}

export function Botao({
  variante = 'primario',
  carregando = false,
  rotuloCarregando,
  className = '',
  disabled,
  children,
  ...resto
}: Props) {
  return (
    <button
      // `<button>` sem `type` dentro de `<form>` é `submit` por especificação — um "Cancelar"
      // herdaria isso e submeteria o formulário. O default seguro é `button`; quem submete pede.
      type="button"
      {...resto}
      disabled={disabled || carregando}
      className={`${BASE} ${POR_VARIANTE[variante]} ${className}`}
    >
      {carregando && rotuloCarregando ? rotuloCarregando : children}
    </button>
  )
}
```

**Atenção à ordem de `{...resto}`:** ele vem **depois** de `type="button"`, e é isso que deixa o chamador pedir `submit`. Mover o spread para **antes** do `type` faz o literal ganhar e o `type="submit"` do chamador ser ignorado — defeito silencioso, e é o que a M3 mede.

> **Correção do pré-flight de 2026-08-09.** Esta caixa dizia também que o spread tem de vir **antes** de `disabled`/`className` "para que `carregando` e as classes de variante não sejam sobrescritos por acidente". **Esse mecanismo não existe**, e a M3 antiga (mover o spread para depois de `disabled`) era **mutante equivalente**. MEDIDO: `disabled`, `className`, `variante` e `carregando` são **destructurados** na assinatura, então `resto` sai com `['type', 'onClick']` e **nunca** pode conter nenhum deles. A única ordem com consequência observável é a do `type`.

- [ ] **Step 5: Rodar para ver passar**

```bash
cd web && npm test -- Botao
```

Expected: `Tests  10 passed (10)`.

- [ ] **Step 6: Escrever o teste do `Campo`**

`web/src/components/Campo.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Campo } from './Campo'

afterEach(cleanup)

describe('Campo', () => {
  it('liga o rótulo ao controle por id, e não por aninhamento', () => {
    // Ligação explícita: leitor de tela anuncia o rótulo, e clicar no texto foca o campo. Hoje as
    // telas usam `placeholder` como rótulo — que some assim que o usuário digita a primeira letra,
    // deixando o campo sem identificação para quem voltar depois de uma interrupção na bancada.
    render(<Campo rotulo="Código">{(id) => <input id={id} />}</Campo>)

    const controle = screen.getByLabelText('Código') as HTMLInputElement
    expect(controle.tagName).toBe('INPUT')
    expect(controle.id).toBeTruthy()
  })

  it('dá ids diferentes a dois campos com o mesmo rótulo', () => {
    // `useId` por instância. Ids repetidos fariam `getByLabelText` casar sempre com o primeiro, e
    // o clique no segundo rótulo focaria o campo errado — na tela e no leitor de tela.
    const { container } = render(
      <div>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
      </div>,
    )
    const ids = Array.from(container.querySelectorAll('input')).map((i) => i.id)

    expect(ids[0]).not.toBe(ids[1])
  })

  it('mostra a dica quando ela existe e a associa ao controle', () => {
    render(
      <Campo rotulo="Unidade" dica="UN, KG, M…">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    const controle = screen.getByLabelText('Unidade')
    const dica = screen.getByText('UN, KG, M…')
    expect(controle.getAttribute('aria-describedby')).toBe(dica.id)
  })

  it('não deixa aria-describedby pendurado quando não há dica', () => {
    // Apontar para um id inexistente faz o leitor de tela anunciar vazio — pior que não apontar.
    render(
      <Campo rotulo="Código">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    expect(screen.getByLabelText('Código').getAttribute('aria-describedby')).toBeNull()
  })

  it('serve a select, não só a input', () => {
    render(
      <Campo rotulo="Tipo">
        {(id) => <select id={id}><option>Bruto</option></select>}
      </Campo>,
    )

    expect((screen.getByLabelText('Tipo') as HTMLSelectElement).tagName).toBe('SELECT')
  })
})
```

- [ ] **Step 7: Escrever o `Campo`**

`web/src/components/Campo.tsx`:

```tsx
import { useId, type ReactNode } from 'react'

interface Props {
  rotulo: string
  /**
   * Recebe o `id` gerado — e, quando há dica, o id dela — e devolve o controle:
   * `{(id, idDaDica) => <input id={id} aria-describedby={idDaDica} … />}`.
   * O segundo parâmetro pode ser ignorado em campo sem dica.
   */
  children: (id: string, idDaDica?: string) => ReactNode
  dica?: string
}

/**
 * Rótulo + controle, ligados por `id` explícito.
 *
 * O padrão é *render prop* em vez de `<Campo type="text" …>` porque as telas usam `input` e
 * `select`, e o `select` de `ComponentesPage` tem lista fechada com `<option>` próprios. Uma
 * primitiva que tentasse abstrair os dois viraria um repasse de props sem valor. O que a
 * primitiva realmente entrega é a **ligação acessível**, e é isso que ela guarda.
 *
 * Classes do controle ficam com o chamador — via `CLASSES_DE_CONTROLE`, exportada abaixo — para
 * que ele possa acrescentar `font-mono` num campo de código sem lutar contra a primitiva.
 */
export function Campo({ rotulo, children, dica }: Props) {
  const id = useId()
  const idDaDica = `${id}-dica`

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium text-tinta">
        {rotulo}
      </label>
      {/* O `aria-describedby` vai no CONTROLE, não num wrapper: leitor de tela associa a descrição
          ao campo focado, e num `<div>` externo ele nunca é anunciado. Por isso a render prop
          recebe os dois ids. Quando não há dica o segundo é `undefined`, e React omite o atributo
          em vez de apontar para um id inexistente — apontar para o vazio é pior que não apontar. */}
      {children(id, dica ? idDaDica : undefined)}
      {dica && <p id={idDaDica} className="text-sm text-tinta-fraca">{dica}</p>}
    </div>
  )
}

/** Aparência única de todo input/select do sistema. Use com `className={CLASSES_DE_CONTROLE}`. */
export const CLASSES_DE_CONTROLE =
  'w-full rounded-lg border border-borda-campo bg-superficie px-3 py-2.5 text-tinta ' +
  'placeholder:text-tinta-fraca ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'
```

**O teste da dica exige que o chamador use o segundo parâmetro.** Os dois casos do Step 6 que envolvem dica precisam ser escritos assim:

```tsx
<Campo rotulo="Unidade" dica="UN, KG, M…">
  {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
</Campo>
```

Campo sem dica ignora o segundo parâmetro — `{(id) => <input id={id} />}` continua válido, e é a forma usada em quase todas as telas.

- [ ] **Step 8: Rodar `Campo` até ficar verde**

```bash
cd web && npm test -- Campo
```

Expected: `Tests  5 passed (5)`.

- [ ] **Step 9: Escrever `BannerDeErro` com seu teste**

`web/src/components/BannerDeErro.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { BannerDeErro } from './BannerDeErro'

afterEach(cleanup)

describe('BannerDeErro', () => {
  it('mostra a mensagem', () => {
    render(<BannerDeErro mensagem="Seu perfil não tem permissão para esta ação." />)

    expect(screen.getByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('não renderiza nada quando não há mensagem', () => {
    // O chamador passa o estado direto (`<BannerDeErro mensagem={erro} />`) em vez de repetir
    // `{erro && …}` em sete telas. Só funciona se o nulo for tratado aqui.
    const { container } = render(<BannerDeErro mensagem={null} />)

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como alerta', () => {
    // `role="alert"` faz o leitor de tela ler a mensagem assim que ela aparece, sem exigir que o
    // usuário navegue até ela. Numa tela de bancada, o erro costuma estar longe do foco.
    render(<BannerDeErro mensagem="Sem conexão com o servidor." />)

    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor.')
  })
})
```

`web/src/components/BannerDeErro.tsx`:

```tsx
interface Props {
  /** Aceita `null` para o chamador passar o estado direto, sem `{erro && …}` em sete telas. */
  mensagem: string | null
}

export function BannerDeErro({ mensagem }: Props) {
  if (!mensagem) return null

  return (
    <p
      role="alert"
      className="rounded-lg border border-negativo/30 bg-negativo/5 px-4 py-3 text-negativo"
    >
      {mensagem}
    </p>
  )
}
```

- [ ] **Step 10: Escrever `Pagina` com seu teste**

`web/src/components/Pagina.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pagina } from './Pagina'

afterEach(cleanup)

describe('Pagina', () => {
  it('mostra o título como o h1 da tela', () => {
    render(<Pagina titulo="Setores"><p>conteúdo</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Setores')
    expect(screen.getByText('conteúdo')).toBeTruthy()
  })

  it('mostra a ação de cabeçalho quando ela existe', () => {
    render(<Pagina titulo="Pedidos" acao={<button>Novo</button>}><p>c</p></Pagina>)

    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('funciona sem ação', () => {
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 })).toBeTruthy()
  })

  it('é o landmark main da página', () => {
    // Um `main` por tela dá ao leitor de tela o atalho "ir para o conteúdo" e separa o conteúdo do
    // shell de navegação, que envolve todas as telas a partir da Task 7.
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('main')).toBeTruthy()
  })

  it('não impõe altura mínima de tela — quem faz isso é o shell', () => {
    // `min-h-screen` DENTRO da página, com o shell também aplicando, produz barra de rolagem
    // permanente de alguns pixels. As 6 telas de hoje têm `min-h-screen`; ele sai daqui.
    expect(screen.queryByRole('main')).toBeNull()
    const { container } = render(<Pagina titulo="Setores"><p>c</p></Pagina>)
    expect(container.querySelector('main')!.className).not.toContain('min-h-screen')
  })
})
```

`web/src/components/Pagina.tsx`:

```tsx
import type { ReactNode } from 'react'

interface Props {
  titulo: string
  /** Ação principal da tela, alinhada ao título (ex.: "Novo pedido"). */
  acao?: ReactNode
  children: ReactNode
}

/**
 * Substitui as SEIS cópias de `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4`.
 *
 * `max-w-3xl` (768px) e não `max-w-md` (448px): a spec §7 registra que busca + filtro + seletor de
 * tamanho + paginação não cabem em 448px. `min-h-screen` sai daqui e vai para o `AppShell` — as
 * duas coisas juntas produzem rolagem permanente de alguns pixels.
 *
 * O respiro é generoso de propósito (direção "sóbria e espaçada", spec §3). O custo foi medido e
 * aceito: na mesma altura de tela, ~3 itens onde a densa mostraria ~6.
 */
export function Pagina({ titulo, acao, children }: Props) {
  return (
    <main className="mx-auto w-full max-w-3xl px-4 py-8 sm:px-6 flex flex-col gap-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight text-tinta">{titulo}</h1>
        {acao}
      </header>
      {children}
    </main>
  )
}
```

- [ ] **Step 11: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: **`Tests  195 passed (195)`** (172 medidos + 10 + 5 + 3 + 5) · build limpo · lint só com o warning alheio.

> **Correção do pré-flight de 2026-08-09 — sexto erro de contagem deste plano, e o primeiro em que o plano se contradizia sozinho.** Este Step dizia `179` (= 156 + 23) e a definition of done dizia `≈ 187` (= 164 + 23): **os dois absolutos estavam mortos E discordavam entre si**. O **delta `+23` está certo** — conferido `it(` a `it(` no pré-flight: `Botao` 10, `Campo` 5, `BannerDeErro` 3, `Pagina` 5. É o delta que vincula; se o seu total divergir com o delta certo, reporte o número real e siga.

- [ ] **Step 12: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `Botao.tsx` — remover `carregando` de `disabled={disabled \|\| carregando}` | ≥ 1 |
| M2 | `Botao.tsx` — trocar `type="button"` por `type="submit"` | ≥ 1 |
| M3 | `Botao.tsx` — mover `{...resto}` para **antes** de `type="button"` | ≥ 1 (o teste `aceita type=submit`) |
| M4 | `Botao.tsx` — dar a mesma string de classe a `primario` e `secundario` | ≥ 1 |
| M5 | `Botao.tsx` — trocar `focus-visible:outline-2` por nada | ≥ 1 |
| M6 | `Campo.tsx` — trocar `useId()` por uma constante `'campo'` | ≥ 1 |
| M7 | `Campo.tsx` — remover `htmlFor={id}` do `<label>` | ≥ 1 |
| M8 | `Campo.tsx` — trocar `dica ? idDaDica : undefined` por `idDaDica` | ≥ 1 |
| M9 | `BannerDeErro.tsx` — trocar `if (!mensagem) return null` por `if (false)` | ≥ 1 |
| M10 | `BannerDeErro.tsx` — remover `role="alert"` | ≥ 1 |
| M11 | `Pagina.tsx` — trocar `<h1>` por `<h2>` | ≥ 1 |
| M12 | `Pagina.tsx` — trocar `<main>` por `<div>` | ≥ 1 |
| M13 | `Pagina.tsx` — acrescentar `min-h-screen` às classes | ≥ 1 |
| M14 | `Botao.tsx` — trocar o default `variante = 'primario'` por `'secundario'` | ≥ 1 |

**M3, M5 e M14 vêm do pré-flight de 2026-08-09, e as três nasceram de medição:** a M3 antiga era equivalente (ver a caixa do Step 4); a M5 **sobrevivia** à asserção antiga, que casava com as sobras `focus-visible:outline-offset-2` e `focus-visible:outline-acao`; e a M14 não existia — o teste `usa primário quando a variante não é dita` passava com o default trocado, porque `hover:bg-acao-fundo` contém `bg-acao` como substring. As três asserções foram reescritas para comparar **token exato** em vez de substring. Se alguma delas sobreviver mesmo assim, **pare e reporte**: é sinal de que a reescrita não fechou o que dizia fechar.

- [ ] **Step 13: Commit**

```bash
git add web/src/components/Pagina.tsx web/src/components/Pagina.test.tsx web/src/components/Botao.tsx web/src/components/Botao.test.tsx web/src/components/Campo.tsx web/src/components/Campo.test.tsx web/src/components/BannerDeErro.tsx web/src/components/BannerDeErro.test.tsx
git commit -m "feat(web): primitivas Pagina, Botao, Campo e BannerDeErro"
```

**Definition of done da Task 5:** suíte em **baseline medida + 23** (**195**, se a baseline for os 172 medidos — **o delta é o que vincula**, e o `≈ 187` que estava aqui era o sexto erro de contagem do plano); as **14** mutações medidas com ≥ 1 morte cada; **nenhuma tela consumindo as primitivas ainda** (isso é das Tasks 8–12); build e lint limpos.

---

### Task 6: Primitivas de lista — `ListaDeCadastro`, `Pilula`, `EstadoVazio`, `ControlesDePaginacao`

**Files:**
- Create: `web/src/components/ListaDeCadastro.tsx` + `ListaDeCadastro.test.tsx`
- Create: `web/src/components/Pilula.tsx` + `Pilula.test.tsx`
- Create: `web/src/components/EstadoVazio.tsx` + `EstadoVazio.test.tsx`
- Create: `web/src/components/ControlesDePaginacao.tsx` + `ControlesDePaginacao.test.tsx`
- Modify: `web/src/index.css` — um token novo (`--color-positivo-fundo`), ver Step 5
- Modify: `web/src/tema/contraste.test.ts` — dois pares novos e o token novo na lista obrigatória

**Interfaces:**
- Consumes: `Botao` (Task 5), tokens (Task 4).
- Produces:

```ts
export function ListaDeCadastro(props: { children: ReactNode; rotulo?: string }): JSX.Element
export function ItemDeCadastro(props: { ativo?: boolean; acao?: ReactNode; children: ReactNode }): JSX.Element
export function Pilula(props: { children: ReactNode; tom?: 'neutro' | 'positivo' | 'negativo' }): JSX.Element
export function EstadoVazio(props: { titulo: string; descricao?: string; acao?: ReactNode }): JSX.Element
export function ControlesDePaginacao(props: {
  pagina: number; totalDePaginas: number; total: number; aoMudarPagina: (p: number) => void
}): JSX.Element
```

**Por que `ListaDeCadastro` não recebe os itens por prop:** as quatro listas do sistema mostram coisas diferentes (nome; código + descrição + unidade; código + descrição + tipo; código + tipo) e uma primitiva que tentasse abstrair isso viraria seis render props. O que ela guarda é o que **não** varia: a semântica de lista, o espaçamento, e o item com inativo distinguível mais slot de ação.

**O estado vazio é decidido pela TELA, não pela lista** — a spec §9 exige distinguir "nenhum resultado para a busca", "catálogo vazio" e "erro de rede", que hoje renderizam a mesma lista vazia e muda. Uma lista que renderizasse o vazio sozinha não teria como saber qual dos três é.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: `Tests  200 passed (200)`.

**Baseline MEDIDA em 2026-08-09**, no pré-flight desta task, com HEAD em `8d75c53` (183 `it(`
literais + 17 entradas do `it.each(PARES)`). O plano dizia `179`, e ainda dizia `7` no Step 3, `200`
no Step 7 e `≈208` na DoD — **quatro baselines mutuamente contraditórias**. A do Step 7 era a pior:
`200` é exatamente a baseline de hoje, então quem rodasse `npm test` antes de escrever qualquer
linha veria `200 passed` e concluiria que o step passou. **O que vincula é o delta.**

- [ ] **Step 2: Escrever o teste de `ListaDeCadastro` / `ItemDeCadastro`**

`web/src/components/ListaDeCadastro.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { ListaDeCadastro, ItemDeCadastro } from './ListaDeCadastro'

afterEach(cleanup)

describe('ListaDeCadastro', () => {
  it('é uma lista com um item por filho', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro>Corte</ItemDeCadastro>
        <ItemDeCadastro>Solda</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByRole('list')).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
  })

  it('aceita um rótulo acessível para distinguir duas listas na mesma tela', () => {
    render(<ListaDeCadastro rotulo="Agrupamentos"><ItemDeCadastro>A</ItemDeCadastro></ListaDeCadastro>)

    expect(screen.getByRole('list', { name: 'Agrupamentos' })).toBeTruthy()
  })
})

describe('ItemDeCadastro', () => {
  it('mostra o conteúdo e a ação', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro acao={<button>Inativar</button>}>Corte</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(screen.getByText('Inativar')).toBeTruthy()
  })

  it('distingue item inativo de item ativo', () => {
    // Distinção que NÃO é só cor: `line-through` mais tinta fraca. Cor sozinha exclui quem não a
    // percebe, e a lista de inativos é justamente onde o usuário decide se reativa ou não.
    const { container } = render(
      <ListaDeCadastro>
        <ItemDeCadastro ativo>Ativo</ItemDeCadastro>
        <ItemDeCadastro ativo={false}>Inativo</ItemDeCadastro>
      </ListaDeCadastro>,
    )
    const [itemAtivo, itemInativo] = Array.from(container.querySelectorAll('li'))

    expect(itemInativo.textContent).toContain('Inativo')
    expect(itemInativo.innerHTML).toContain('line-through')
    expect(itemAtivo.innerHTML).not.toContain('line-through')
  })

  it('trata item sem a prop ativo como ativo', () => {
    // `Agrupamento` não tem coluna `Ativo` — a lista do PedidoDetalhe usa o item sem a prop, e não
    // pode sair riscada por causa disso.
    const { container } = render(
      <ListaDeCadastro><ItemDeCadastro>AGR-01</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(container.querySelector('li')!.innerHTML).not.toContain('line-through')
  })

  it('anuncia o estado inativo em texto, não só no traço', () => {
    // `line-through` é visual e não chega ao leitor de tela. Sem isto, quem usa leitor ouve
    // "Corte" nos dois casos e não tem como saber qual está inativo.
    render(
      <ListaDeCadastro><ItemDeCadastro ativo={false}>Corte</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(screen.getByText('(inativo)')).toBeTruthy()
  })
})
```

`web/src/components/ListaDeCadastro.tsx`:

```tsx
import type { ReactNode } from 'react'

/**
 * O `<ul>` das quatro listas de cadastro. NÃO recebe os itens por prop: as quatro mostram campos
 * diferentes, e abstrair isso viraria seis render props sem ganho. O que a primitiva guarda é o
 * que não varia — semântica de lista e espaçamento.
 *
 * O estado vazio fica com a TELA (`EstadoVazio`): a spec §9 exige distinguir "nenhum resultado
 * para a busca" de "catálogo vazio" e de "erro de rede", e daqui não dá para saber qual é.
 */
export function ListaDeCadastro({ children, rotulo }: { children: ReactNode; rotulo?: string }) {
  return (
    <ul aria-label={rotulo} className="flex flex-col gap-2">
      {children}
    </ul>
  )
}

export function ItemDeCadastro({
  ativo = true,
  acao,
  children,
}: {
  /** Ausente = ativo. `Agrupamento` não tem coluna `Ativo` e usa o item sem a prop. */
  ativo?: boolean
  acao?: ReactNode
  children: ReactNode
}) {
  return (
    <li className="flex items-center justify-between gap-3 rounded-lg border border-borda bg-superficie px-4 py-3">
      <span className={ativo ? 'text-tinta' : 'text-tinta-fraca'}>
        <span className={ativo ? undefined : 'line-through'}>{children}</span>
        {/* O traço é visual e não chega ao leitor de tela; sem este texto, ativo e inativo soam
            idênticos para quem não vê a lista.

            IRMÃO do nome riscado, e não filho: `text-decoration` de um ancestral é pintada ATRAVÉS
            dos descendentes em fluxo e um descendente NÃO consegue desligá-la (CSS Text Decoration
            L3). A primeira versão deste plano punha o rótulo dentro do `line-through` com
            `no-underline` para tentar salvá-lo — classe que não faz nada e que encena uma decisão.
            Como irmão, o rótulo nunca é riscado por construção, sem depender de truque de cascata. */}
        {!ativo && <span className="ml-2">(inativo)</span>}
      </span>
      {acao}
    </li>
  )
}
```

- [ ] **Step 3: Rodar até ficar verde**

```bash
cd web && npm test -- ListaDeCadastro
```

Expected: `Tests  6 passed (6)` — **contados no próprio bloco acima**: 2 em `ListaDeCadastro` e 4 em
`ItemDeCadastro`. O plano dizia `7`, contradizendo o arquivo que ele mesmo especifica.

- [ ] **Step 4: Escrever `EstadoVazio` com seu teste**

`web/src/components/EstadoVazio.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { EstadoVazio } from './EstadoVazio'

afterEach(cleanup)

describe('EstadoVazio', () => {
  it('mostra título e descrição', () => {
    render(<EstadoVazio titulo="Nenhum componente encontrado" descricao="Tente outro termo de busca." />)

    expect(screen.getByText('Nenhum componente encontrado')).toBeTruthy()
    expect(screen.getByText('Tente outro termo de busca.')).toBeTruthy()
  })

  it('funciona só com título', () => {
    render(<EstadoVazio titulo="Catálogo vazio" />)

    expect(screen.getByText('Catálogo vazio')).toBeTruthy()
  })

  it('mostra a ação sugerida quando ela existe', () => {
    render(<EstadoVazio titulo="Catálogo vazio" acao={<button>Cadastrar o primeiro</button>} />)

    expect(screen.getByText('Cadastrar o primeiro')).toBeTruthy()
  })

  it('é anunciado como região de status, não como erro', () => {
    // `role="status"` e não `role="alert"`: lista vazia é informação, não falha. Anunciar como
    // alerta interromperia a leitura para dizer que não há nada — e o usuário que acabou de
    // filtrar já sabe disso.
    render(<EstadoVazio titulo="Nenhum resultado" />)

    expect(screen.getByRole('status').textContent).toContain('Nenhum resultado')
  })
})
```

`web/src/components/EstadoVazio.tsx`:

```tsx
import type { ReactNode } from 'react'

/**
 * Estado vazio explícito. Hoje "nenhum resultado para a busca", "catálogo vazio" e "erro de rede"
 * renderizam a mesma lista vazia e muda — o usuário não tem como distinguir "não achei" de
 * "não perguntei" (spec §9).
 *
 * O texto vem da tela, porque só ela sabe qual dos três é.
 */
export function EstadoVazio({
  titulo,
  descricao,
  acao,
}: {
  titulo: string
  descricao?: string
  acao?: ReactNode
}) {
  return (
    <div
      role="status"
      className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-borda bg-superficie px-6 py-12 text-center"
    >
      <p className="font-medium text-tinta">{titulo}</p>
      {descricao && <p className="text-sm text-tinta-fraca">{descricao}</p>}
      {acao && <div className="mt-2">{acao}</div>}
    </div>
  )
}
```

- [ ] **Step 5: Declarar o tom da pílula positiva, e só então escrever `Pilula`**

**Esta ordem não é estética.** A versão original deste plano pintava as pílulas de estado com
`bg-positivo/10` e `bg-negativo/10` — **modificadores de opacidade**, que o Tailwind emite como
`color-mix(in oklab, …)`. Tom derivado **não é declaração `--color-*`**, então escapa inteiro das
duas guardas da Task 4. É exatamente o Critical que a review da Task 5 achou no `BannerDeErro` e
que o fix `8d75c53` fechou; o plano ia replantá-lo, em dobro.

**Medido no pré-flight de 2026-08-09**, com a fórmula do projeto — as quatro combinações reprovam
os 4,5 de texto normal (e o limiar não é opinião: a guarda **já** classifica texto de pílula como
`TEXTO`, em `contraste.test.ts:64`):

| tinta composta | texto | razão |
|---|---|---|
| `positivo/10` sobre `superficie` → `#E8F0EB` | `#15803D` | **4,322** |
| `positivo/10` sobre `fundo` → `#E1EBE5` | `#15803D` | **4,112** |
| `negativo/10` sobre `superficie` → `#FCE9E9` | `#DC2626` | **4,133** |
| `negativo/10` sobre `fundo` → `#F4E5E4` | `#DC2626` | **3,950** |

A saída é a mesma do fix do banner: **converter cor derivada em cor declarada**, para o tom viver
sob a guarda. A pílula negativa **reusa os tokens do banner** (zero token novo); só a positiva
precisa de um fundo declarado.

**1. Um token novo em `web/src/index.css`**, no bloco "Estado — reservados":

```css
  --color-positivo-fundo: #F1F8F3;   /* tinta da pílula positiva; valor FIXO, não composto */
```

**2. Dois pares novos em `PARES`, de `web/src/tema/contraste.test.ts`** (e o token novo na lista de
tokens obrigatórios logo abaixo):

| frente | fundo | mínimo | onde |
|---|---|---|---|
| `positivo-texto` | `positivo-fundo` | `TEXTO` | texto da pílula de estado positivo |
| `negativo-texto` | `negativo-fundo` | `TEXTO` | texto da pílula de estado negativo |

**Valores medidos por mim no pré-flight — RE-MEDIR antes de escrever qualquer número:**
`positivo-texto`/`positivo-fundo` = **4,648**; `negativo-texto`/`negativo-fundo` = **5,984** (já
verde hoje, é o par do banner). Candidatos que descartei: `#EDF6F0` dá 4,548 (margem de 0,048,
frágil) e `#E8F0EB` dá 4,322 — **este último é exatamente o que o `/10` produzia**, o que é a
medição que condena a versão antiga.

**Os itens 1 e 2 são um passo só, não dois.** MEDIDO no pré-flight: declarar
`--color-positivo-fundo` sem acrescentar o par derruba a suíte em
`não deixa entrar tom novo sem medição`, com `tokens sem par de contraste declarado:
positivo-fundo`. Não é obstáculo — é a guarda da Task 4 funcionando, e ver essa falha é a
confirmação barata de que o token novo entrou no lugar certo. Só não se assuste com ela.

`web/src/components/Pilula.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pilula } from './Pilula'

afterEach(cleanup)

describe('Pilula', () => {
  it('mostra o rótulo', () => {
    render(<Pilula>Montagem</Pilula>)

    expect(screen.getByText('Montagem')).toBeTruthy()
  })

  it('usa a tinta de ação sobre fundo tingido no tom neutro', () => {
    // A mesma `--color-acao` do botão, agora sobre fundo de baixa saturação. Um segundo tom só
    // para a pílula seria uma cor a mais para manter coerente, sem ganho (spec §3).
    //
    // TOKEN A TOKEN (`split`), nunca `toContain` sobre a className inteira: com `toContain`, mutar
    // `text-acao` para `text-acao-forte` SOBREVIVE, porque a segunda string contém a primeira.
    // Essa armadilha custou três achados na Task 5.
    render(<Pilula>Kit</Pilula>)

    const classes = screen.getByText('Kit').className.split(/\s+/)
    expect(classes).toContain('bg-acao-fundo')
    expect(classes).toContain('text-acao')
  })

  it('reserva verde e vermelho para estado, em tons declarados', () => {
    const { container } = render(
      <div>
        <Pilula tom="positivo">Aprovado</Pilula>
        <Pilula tom="negativo">Reprovado</Pilula>
      </div>,
    )
    const [positiva, negativa] = Array.from(container.querySelectorAll('span'))
      .map((s) => s.className.split(/\s+/))

    // `positivo-texto`, não `positivo`: são dois tokens com papéis distintos na paleta.
    // `text-negativo-texto` e não `text-negativo`, pelo mesmo motivo — e com `split` a distinção
    // é real: sob `toContain`, `'text-negativo-texto'` satisfaria uma asserção de `'text-negativo'`.
    expect(positiva).toContain('text-positivo-texto')
    expect(negativa).toContain('text-negativo-texto')

    // Os fundos são tokens DECLARADOS, medidos pela guarda da Task 4. Nenhum modificador de
    // opacidade: era por `/NN` que a cor escapava da guarda (Critical da review da Task 5).
    expect(positiva).toContain('bg-positivo-fundo')
    expect(negativa).toContain('bg-negativo-fundo')
    expect([...positiva, ...negativa].some((c) => c.includes('/'))).toBe(false)
  })
})
```

`web/src/components/Pilula.tsx`:

```tsx
import type { ReactNode } from 'react'

export type TomDePilula = 'neutro' | 'positivo' | 'negativo'

// Os três tons são PARES DECLARADOS, medidos pela guarda da Task 4 — nenhum modificador de
// opacidade. `bg-positivo/10` e `bg-negativo/10` (a versão anterior) viravam
// `color-mix(in oklab, …)`, que não é declaração `--color-*` e escapava da guarda inteira; medido,
// os quatro casos reprovavam AA (4,32 / 4,11 / 4,13 / 3,95 contra os 4,5 exigidos).
//
// `positivo-texto` e não `positivo`, e `negativo-texto` e não `negativo`: os pares medidos são
// esses. NÃO repita aqui a justificativa de que "o verde cheio reprova como texto sobre claro" —
// ela é falsa: `#166534` sobre branco dá 7,130, MEDIDO. Razão de contraste é simétrica.
const POR_TOM: Record<TomDePilula, string> = {
  neutro: 'bg-acao-fundo text-acao',
  positivo: 'bg-positivo-fundo text-positivo-texto',
  negativo: 'bg-negativo-fundo text-negativo-texto',
}

/**
 * Rótulo curto de categoria ou estado (tipo do componente, tipo do agrupamento, status do pedido).
 *
 * O tom `neutro` usa a MESMA tinta do botão primário sobre fundo tingido — não é engano: é a mesma
 * cor em dois contextos. Verde e vermelho ficam reservados a estado de verdade.
 */
export function Pilula({ children, tom = 'neutro' }: { children: ReactNode; tom?: TomDePilula }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${POR_TOM[tom]}`}>
      {children}
    </span>
  )
}
```

- [ ] **Step 6: Escrever `ControlesDePaginacao` com seu teste**

`web/src/components/ControlesDePaginacao.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ControlesDePaginacao } from './ControlesDePaginacao'

afterEach(cleanup)

describe('ControlesDePaginacao', () => {
  it('mostra a posição e o total', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByText(/Página 2 de 5/)).toBeTruthy()
    expect(screen.getByText(/97/)).toBeTruthy()
  })

  it('avança uma página no "Próxima"', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect(aoMudarPagina).toHaveBeenCalledWith(3)
  })

  it('recua uma página no "Anterior"', () => {
    // Na Fase 1B este botão nunca foi clicado por teste nenhum: mutá-lo para `pagina + 1`
    // sobrevivia, e a paginação só andaria para frente sem ninguém ver (achado I3 da Task 6).
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect(aoMudarPagina).toHaveBeenCalledWith(1)
  })

  it('desabilita "Anterior" na primeira página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={1} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Anterior'))

    expect((screen.getByText('Anterior') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('desabilita "Próxima" na última página', () => {
    const aoMudarPagina = vi.fn()
    render(<ControlesDePaginacao pagina={5} totalDePaginas={5} total={97} aoMudarPagina={aoMudarPagina} />)

    fireEvent.click(screen.getByText('Próxima'))

    expect((screen.getByText('Próxima') as HTMLButtonElement).disabled).toBe(true)
    expect(aoMudarPagina).not.toHaveBeenCalled()
  })

  it('não aparece quando há uma página só', () => {
    // Controles de paginação numa lista de 3 itens são ruído, e "Página 1 de 1" não informa nada.
    const { container } = render(
      <ControlesDePaginacao pagina={1} totalDePaginas={1} total={3} aoMudarPagina={() => {}} />,
    )

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como navegação', () => {
    render(<ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />)

    expect(screen.getByRole('navigation', { name: 'Paginação' })).toBeTruthy()
  })

  it('não põe peso primário em nenhum dos dois botões', () => {
    // A spec §3 barra dois botões com o mesmo peso visual na mesma tela, e é por isso que o `Botao`
    // tem variantes. Sem esta prova, mutar qualquer um dos dois para `primario` deixava a suíte
    // verde — a decisão que dá sentido às variantes não era tocada por nenhuma mutação do plano.
    // Token a token: `bg-acao-fundo` (hover do secundário) CONTÉM `bg-acao`, então uma comparação
    // por substring não discriminaria as duas variantes.
    const { container } = render(
      <ControlesDePaginacao pagina={2} totalDePaginas={5} total={97} aoMudarPagina={() => {}} />,
    )

    for (const botao of Array.from(container.querySelectorAll('button'))) {
      const classes = botao.className.split(/\s+/)
      expect(classes).toContain('border-borda-campo')
      expect(classes).not.toContain('bg-acao')
    }
  })
})
```

`web/src/components/ControlesDePaginacao.tsx`:

```tsx
import { Botao } from './Botao'

interface Props {
  pagina: number
  totalDePaginas: number
  /** Total sob o filtro, vindo do servidor — não `itens.length`. */
  total: number
  aoMudarPagina: (pagina: number) => void
}

/**
 * Anterior / posição / Próxima.
 *
 * `flex-wrap` não é detalhe: no celular Android da fábrica os três controles não cabem lado a
 * lado, e sem a quebra a página rola na horizontal — o que a spec §11 barra explicitamente.
 */
export function ControlesDePaginacao({ pagina, totalDePaginas, total, aoMudarPagina }: Props) {
  // Uma página só: os controles não informam nada e viram ruído.
  if (totalDePaginas <= 1) return null

  return (
    <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3">
      <Botao variante="secundario" disabled={pagina <= 1} onClick={() => aoMudarPagina(pagina - 1)}>
        Anterior
      </Botao>
      <span className="text-sm text-tinta-fraca">
        Página {pagina} de {totalDePaginas} — {total} no total
      </span>
      <Botao
        variante="secundario"
        disabled={pagina >= totalDePaginas}
        onClick={() => aoMudarPagina(pagina + 1)}
      >
        Próxima
      </Botao>
    </nav>
  )
}
```

- [ ] **Step 7: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: `Tests  223 passed (223)` · build limpo · lint só com o warning alheio.

Delta **+23** sobre a baseline medida de 200, recontado no pré-flight: `ListaDeCadastro` **6** +
`EstadoVazio` **4** + `Pilula` **3** + `ControlesDePaginacao` **8** + **2** entradas novas do
`it.each(PARES)` (os dois pares de pílula do Step 5). **O que vincula é o delta**, não o absoluto.

- [ ] **Step 8: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `ListaDeCadastro.tsx` — trocar `<ul>`/`<li>` por `<div>` | ≥ 1 |
| M2 | `ListaDeCadastro.tsx` — trocar `ativo = true` por `ativo = false` no default | ≥ 1 |
| M3 | `ListaDeCadastro.tsx` — inverter a condição da classe (`ativo ? riscado : normal`) | ≥ 1 |
| M4 | `ListaDeCadastro.tsx` — remover o `(inativo)` | ≥ 1 |
| M5 | `EstadoVazio.tsx` — trocar `role="status"` por `role="alert"` | ≥ 1 |
| M6 | `Pilula.tsx` — trocar `text-positivo-texto` por `text-positivo` | ≥ 1 |
| M6b | `Pilula.tsx` — trocar `text-negativo-texto` por `text-negativo` | ≥ 1 — **sob `toContain` ela SOBREVIVERIA**; é a prova de que a comparação token a token vale |
| M6c | `Pilula.tsx` — trocar `bg-positivo-fundo` por `bg-positivo/10` | ≥ 1 no teste de tons declarados |
| M6d | `index.css` — trocar `--color-positivo-fundo` para `#E8F0EB` (o valor que o `/10` produzia) | **morrer no PAR DE CONTRASTE** (4,322 < 4,5) — é a que prova que o tom entrou sob a guarda |
| M7 | `ControlesDePaginacao.tsx` — trocar `aoMudarPagina(pagina - 1)` por `pagina + 1` no "Anterior" | ≥ 1 |
| M8 | `ControlesDePaginacao.tsx` — trocar `disabled={pagina <= 1}` por `false` | ≥ 1 |
| M9 | `ControlesDePaginacao.tsx` — trocar `disabled={pagina >= totalDePaginas}` por `false` | ≥ 1 |
| M10 | `ControlesDePaginacao.tsx` — remover o `if (totalDePaginas <= 1) return null` | ≥ 1 |
| M12 | `ControlesDePaginacao.tsx` — trocar `variante="secundario"` por `primario` no "Anterior" | ≥ 1 no teste de peso visual |
| M11 | `ControlesDePaginacao.tsx` — remover `flex-wrap` | **0 esperado** — jsdom não faz layout; **é o buraco desta task, e vai declarado no relatório** |
| M13 | `ListaDeCadastro.tsx` — pôr o `(inativo)` de volta DENTRO do `<span>` riscado | **0 esperado** — jsdom não resolve cascata de `text-decoration`; ver abaixo |

**M11 e M13 são a fronteira honesta da rede.** Nenhum teste em jsdom prova que a página não rola na
horizontal, porque jsdom não calcula layout; e nenhum prova que o "(inativo)" não sai riscado,
porque jsdom não resolve cascata de `text-decoration`. A verificação de viewport de celular é
**manual** e acontece na Task 12, com o dev server e o DevTools varrendo de 320px para cima; o "(inativo)"
entra na mesma varredura visual. **Declare as duas como sobreviventes conhecidos** — não invente
asserção de classe para fingir que morreram; classe presente não é layout correto.

**Por que a M13 existe e por que a estrutura mudou:** a versão anterior deste plano punha o rótulo
"(inativo)" DENTRO do `<span>` com `line-through` e tentava salvá-lo com `no-underline`. Isso não
funciona: por CSS Text Decoration L3 a decoração de um ancestral é pintada através dos descendentes
em fluxo e **um descendente não consegue desligá-la**. Era uma classe que não fazia nada e que
encenava uma decisão. A correção **não** é outra classe — é estrutural: o rótulo virou **irmão** do
nome riscado, e aí nunca é riscado por construção. A M13 fica registrada como sobrevivente
justamente porque a suíte não é capaz de defender isso; o que a defende é a estrutura.

- [ ] **Step 9: Commit**

```bash
git add web/src/components/ListaDeCadastro.tsx web/src/components/ListaDeCadastro.test.tsx web/src/components/Pilula.tsx web/src/components/Pilula.test.tsx web/src/components/EstadoVazio.tsx web/src/components/EstadoVazio.test.tsx web/src/components/ControlesDePaginacao.tsx web/src/components/ControlesDePaginacao.test.tsx web/src/index.css web/src/tema/contraste.test.ts
git commit -m "feat(web): primitivas de lista, pilula, estado vazio e paginacao"
```

**Definition of done da Task 6:** suíte em **baseline medida + 23** (**223**, se a baseline ainda for
os 200 medidos em 2026-08-09 — **o delta é o que vincula**); as **16** mutações medidas (M11 e M13
declaradas como sobreviventes conhecidos, com o motivo); build e lint limpos; **nenhuma tela
modificada ainda**.

*(Esta linha dizia **15**. Erro meu no pré-flight: as 11 originais mais as 5 que a correção
acrescentou — M6b, M6c, M6d, M12, M13 — dão **16**, e a tabela acima sempre teve 16 linhas. O
implementer da task **sinalizou a divergência em vez de reconciliá-la em silêncio**, que é
exatamente o comportamento que os briefs pedem: contradição entre dois números meus é motivo de
parar e reportar, nunca de ajustar um deles para fechar. Contado por mim depois: 16.)*

**Este plano foi corrigido em 2026-08-09, no pré-flight, em 6 pontos** — relatório em
`.superpowers/sdd/fase1d-task-6-preflight.md`. Os dois que mais custariam: as pílulas replantavam o
Critical do `BannerDeErro` em dobro (cor derivada por `/NN` fora da guarda, as quatro combinações
reprovando AA), e as quatro baselines contraditórias incluíam uma — o `200` do Step 7 — que era
**exatamente a baseline real de hoje**, e portanto indistinguível de sucesso.

---

### Task 7: Shell de navegação e a tabela de permissões

> **REESCRITA em 2026-08-10 pelo pré-flight** (`.superpowers/sdd/fase1d-task-7-preflight.md`), que
> achou **7 defeitos, 5 medidos**. O que mudou em relação à versão de 2026-08-06: as três baselines
> mortas; o `AppShell.tsx` inteiro, que **derrubava a suíte** por violar a guarda de modificador de
> opacidade instalada na Task 6 (`7f56f39`, posterior a este plano); cinco tokens novos; o teste do
> `usePodeEscrever`, que não existia; a guarda de espelhamento front/backend; e as quatro decisões
> visuais que o usuário tomou sobre protótipo medido. **Delta +14 → +29.**

**Files:**
- Modify: `web/src/index.css` (5 tokens novos do chrome)
- Modify: `web/src/tema/contraste.test.ts` (5 pares novos + os nomes na lista de tokens exigidos)
- Create: `web/src/auth/permissoes.ts` + `permissoes.test.ts`
- Create: `web/src/auth/usePermissao.ts` + `usePermissao.test.tsx`
- Create: `web/src/auth/permissoesEspelhamOBackend.test.ts`
- Create: `web/src/components/AppShell.tsx` + `AppShell.test.tsx`
- Modify: `web/src/App.tsx` (as 6 rotas protegidas viram filhas de uma rota de layout)

**Interfaces:**
- Consumes: `useAuth` (`web/src/auth/AuthContext.tsx`), tokens (Task 4).
- Produces:

```ts
export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos'
export function podeEscrever(perfil: string, recurso: Recurso): boolean
export function usePodeEscrever(recurso: Recurso): boolean
export function AppShell(): JSX.Element   // renderiza <Outlet/>
```

**Hoje não existe shell nenhum.** Conferido no disco: 6 das 7 telas repetem `min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4`, a navegação vive dentro da `HomePage` (`HomePage.tsx:47-54`, uma fileira de `<Link>` com borda), e **nenhuma das seis telas internas tem caminho de volta que não seja o botão do navegador**.

**Gating: na AÇÃO, não no link.** Decisão do usuário em 2026-08-06, e ela **diverge da letra da spec §6** — o motivo está nas Global Constraints e foi medido: os controllers liberam leitura para qualquer usuário autenticado. Aqui a Task entrega a **tabela**, o **hook** e a **guarda que impede a tabela de divergir do backend em silêncio**; quem consome o hook são as Tasks 8–10. **O `try/catch` do F2 continua em toda tela** — o 403 do backend é a fronteira real, e esconder o botão não substitui autorização.

**Dívida registrada ao decidir isto (2026-08-10, fora do escopo desta fase):** os `Roles` são literais de código, então eleger um perfil para uma ação nova — ou criar um perfil — exige **alterar código e refazer o deploy**. Os perfis já vivem no banco; o mapeamento perfil → ação não. O usuário quer revisitar a forma de populá-los. Consequência para esta task: a guarda do Step 8 depende da forma atual, e por isso ela **falha quando não encontra atributo nenhum** em vez de passar com lista vazia.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: **`Tests  230 passed (230)`** — medido em 2026-08-10 sobre `7f56f39`, depois do fix pass da Task 6. Este Step dizia `200`, que era baseline morta de duas tasks atrás. Se você medir outro número, **pare e reporte**: divergência na baseline é sinal real, ao contrário de divergência num total derivado.

- [ ] **Step 2: Declarar os cinco tokens do chrome**

O `AppShell` precisa de cinco tons sobre o chrome escuro. A versão anterior deste plano os produzia com modificador de opacidade (`bg-superficie/15`, `text-superficie/80`, `border-superficie/40`…) — **isso derruba a suíte**: a guarda `semModificadorDeOpacidadeEmCor.test.ts`, instalada no fix pass da Task 6, proíbe `/NN` em classe de cor em todo `web/src/`. Medido no pré-flight: **10 violações, 5 classes distintas**.

Os três primeiros valores abaixo são *exatamente* o que o `/NN` produzia, agora declarados; os dois últimos são valores novos, e a razão de cada um está na tabela do Step 3.

Em `web/src/index.css`, dentro do `@theme`, logo abaixo de `--color-marca`:

```css
  /* Tons do chrome. Existem DECLARADOS, e não como `bg-superficie/15` e afins, porque
     modificador de opacidade compõe com `color-mix(in oklab, …)` — que não é declaração
     `--color-*` e escapa das duas guardas de `contraste.test.ts`. Foi o caminho que sangrou
     três vezes nesta fase (oklch do M8, `}` em comentário do I1, `/NN` do banner e da pílula). */
  --color-chrome-tinta-fraca:   #D0DCDB;  /* link inativo da barra */
  --color-chrome-tinta-apagada: #B7C2C1;  /* identidade do usuário — informa, não convida a clicar */
  --color-chrome-ativo:         #366965;  /* fundo do item da tela atual; também o separador da gaveta */
  --color-chrome-hover:         #2B605C;  /* fundo do item sob o ponteiro */
  --color-chrome-borda:         #7B9C9A;  /* contorno do "Sair" e do botão de menu */
```

Rode agora, **antes** de mexer no `contraste.test.ts`:

```bash
cd web && npm test -- contraste
```

Expected: **FALHA**, em `não deixa entrar tom novo sem medição`, nomeando os cinco:
`tokens sem par de contraste declarado: chrome-tinta-fraca, chrome-tinta-apagada, chrome-ativo, chrome-hover, chrome-borda`.

**Esta falha é o Step funcionando.** Não a contorne; ela é a prova de que a guarda da Task 4 pega tom novo sem medição. (Medida de graça no pré-flight da Task 6, com um token só.)

- [ ] **Step 3: Declarar os cinco pares e ver a falha morrer**

Em `web/src/tema/contraste.test.ts`, acrescente ao fim de `PARES`:

```ts
  { frente: 'chrome-tinta-fraca', fundo: 'chrome', minimo: TEXTO, onde: 'link inativo da barra de navegação' },
  { frente: 'chrome-tinta-apagada', fundo: 'chrome', minimo: TEXTO, onde: 'identidade do usuário no shell' },
  { frente: 'superficie', fundo: 'chrome-ativo', minimo: TEXTO, onde: 'rótulo do item da tela atual' },
  { frente: 'superficie', fundo: 'chrome-hover', minimo: TEXTO, onde: 'rótulo do item sob o ponteiro' },
  { frente: 'chrome-borda', fundo: 'chrome', minimo: INTERFACE, onde: 'contorno do Sair e do botão de menu' },
```

E acrescente os cinco nomes à lista do `it('declara todos os tokens que o plano da fase fixou')` — senão o token pode sumir do `index.css` sem ninguém notar.

**Os valores, medidos no pré-flight com o hex exato** (não com a mistura em float — a diferença já mordeu uma vez nesta conversa: `#769896` dá 3,024, e não os 3,035 do float):

| Par | Razão | Mínimo |
|---|---|---|
| `chrome-tinta-fraca` sobre `chrome` | **6,742** | 4,5 |
| `chrome-tinta-apagada` sobre `chrome` | **5,189** | 4,5 |
| `superficie` sobre `chrome-ativo` | **6,240** | 4,5 |
| `superficie` sobre `chrome-hover` | **7,166** | 4,5 |
| `chrome-borda` sobre `chrome` | **3,187** | 3,0 |

**Por que a borda é `#7B9C9A` (44% de branco) e não `#769896` (42%):** o valor de 42% passa por 0,024, margem que qualquer ajuste futuro no chrome derruba. 44% dá folga de 0,187. **Decisão do usuário em 2026-08-10, sobre protótipo medido.** O valor de 40% do plano original dava **2,895** e reprovava.

```bash
cd web && npm test -- contraste
```

Expected: verde, com **5 pares a mais** no `it.each` — de 19 para 24 entradas.

- [ ] **Step 4: Escrever o teste das permissões**

`web/src/auth/permissoes.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { podeEscrever } from './permissoes'

describe('podeEscrever', () => {
  it('deixa Administrador escrever em tudo', () => {
    for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
      expect(podeEscrever('Administrador', r), r).toBe(true)
    }
  })

  it('deixa PCP escrever em componentes, pedidos e agrupamentos', () => {
    expect(podeEscrever('PCP', 'componentes')).toBe(true)
    expect(podeEscrever('PCP', 'pedidos')).toBe(true)
    expect(podeEscrever('PCP', 'agrupamentos')).toBe(true)
  })

  it('NÃO deixa PCP escrever em setores nem materiais', () => {
    // Espelha `[Authorize(Roles = "Administrador")]` de SetoresController e MateriaisController.
    // Se esta linha inverter, o PCP ganha um formulário que o backend vai recusar com 403.
    expect(podeEscrever('PCP', 'setores')).toBe(false)
    expect(podeEscrever('PCP', 'materiais')).toBe(false)
  })

  it('não deixa Operador, Almoxarifado, Qualidade nem Gestao escreverem em nada', () => {
    // `Gestao` sem acento: é o valor que está em `db/seed.sql`, e o perfil chega do backend como
    // claim. Escrever "Gestão" aqui faria a comparação falhar em silêncio.
    // CONFERIDO no pré-flight de 2026-08-10: `db/seed.sql:3` traz os 6 perfis sem acento.
    for (const p of ['Operador', 'Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })

  it('nega perfil desconhecido em vez de liberar', () => {
    // Perfil novo no banco sem entrada aqui tem que cair no lado seguro.
    expect(podeEscrever('PerfilQueNaoExiste', 'pedidos')).toBe(false)
    expect(podeEscrever('', 'pedidos')).toBe(false)
  })
})
```

`web/src/auth/permissoes.ts`:

```ts
export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos'

/**
 * Espelho dos `[Authorize(Roles = …)]` do backend, conferidos no disco em 2026-08-10.
 *
 * **Isto é conveniência de interface, não segurança.** A autorização real é do backend e continua
 * sendo: esconder um botão não impede requisição nenhuma. O que esta tabela evita é o usuário
 * preencher um formulário inteiro para receber 403 no fim.
 *
 * A divergência com o backend é silenciosa nos dois sentidos — liberar demais dá 403 no fim do
 * formulário (chato, visível); liberar de menos some com a ação para quem tinha direito a ela
 * (invisível, e o suspeito natural vira o backend, que está certo). Por isso ela não é vigiada
 * por leitura: `permissoesEspelhamOBackend.test.ts` lê os controllers e compara.
 */
const ESCRITA: Record<Recurso, readonly string[]> = {
  setores: ['Administrador'],
  materiais: ['Administrador'],
  componentes: ['Administrador', 'PCP'],
  pedidos: ['PCP', 'Administrador'],
  agrupamentos: ['PCP', 'Administrador'],
}

export function podeEscrever(perfil: string, recurso: Recurso): boolean {
  return ESCRITA[recurso].includes(perfil)
}

/** Exportado só para a guarda de espelhamento. Não use em tela: use `podeEscrever`. */
export const TABELA_DE_ESCRITA = ESCRITA
```

- [ ] **Step 5: Rodar e confirmar**

```bash
cd web && npm test -- permissoes
```

Expected: `Tests  5 passed (5)`.

- [ ] **Step 6: O hook, com teste — ele NÃO tinha nenhum**

O pré-flight achou que `usePermissao.ts` nasceria como o único arquivo novo da task sem prova, e sem nenhuma mutação tocando nele. Não é acadêmico: é este hook que as Tasks 8–10 consomem para esconder formulário e botões de (in)ativar. **Duas mutações sobreviveriam:** `return true` (a tabela inteira vira decoração) e tirar a guarda de `status === 'autenticado'` (em sessão anônima `estado.usuario` é `undefined` e o acesso a `.perfil` **estoura em runtime**).

`web/src/auth/usePermissao.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from './estadoDaSessao'

afterEach(cleanup)

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('./AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePodeEscrever } = await import('./usePermissao')

describe('usePodeEscrever', () => {
  it('libera quando o perfil da sessão pode escrever no recurso', () => {
    estadoAtual = {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    }

    expect(renderHook(() => usePodeEscrever('pedidos')).result.current).toBe(true)
  })

  it('nega quando o perfil da sessão não pode escrever naquele recurso', () => {
    // O MESMO usuário do caso acima, em OUTRO recurso — é o par que impede `return true` de passar.
    estadoAtual = {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    }

    expect(renderHook(() => usePodeEscrever('setores')).result.current).toBe(false)
  })

  it('nega em sessão não autenticada, sem estourar', () => {
    // `estado.usuario` não existe neste ramo da união. Sem a guarda de status, isto não devolve
    // `false`: lança TypeError ao ler `.perfil` de `undefined`.
    estadoAtual = { status: 'anonimo' }

    expect(renderHook(() => usePodeEscrever('pedidos')).result.current).toBe(false)
  })
})
```

`web/src/auth/usePermissao.ts`:

```ts
import { useAuth } from './AuthContext'
import { podeEscrever, type Recurso } from './permissoes'

/**
 * `podeEscrever` ligado à sessão. Sessão não autenticada devolve `false` — as telas de cadastro só
 * existem dentro do `ProtectedRoute`, então na prática este caso é a montagem de teste. A guarda
 * fica assim mesmo: sem ela, o caminho anônimo lança TypeError em vez de negar.
 */
export function usePodeEscrever(recurso: Recurso): boolean {
  const { estado } = useAuth()
  return estado.status === 'autenticado' && podeEscrever(estado.usuario.perfil, recurso)
}
```

```bash
cd web && npm test -- usePermissao
```

Expected: `Tests  3 passed (3)`.

- [ ] **Step 7: A guarda de espelhamento — o que impede a divergência silenciosa**

`web/src/auth/permissoesEspelhamOBackend.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { TABELA_DE_ESCRITA, type Recurso } from './permissoes'

/**
 * `permissoes.ts` é uma cópia MANUAL dos `[Authorize(Roles = …)]` do backend, e o teste dela é
 * auto-referente: afirma a tabela contra si mesma. Sem esta guarda, mudar um atributo em `src/`
 * não quebra nada aqui, e o sentido perigoso da divergência (liberar de MENOS) não produz erro
 * nenhum em lugar nenhum — a ação some para quem tinha direito a ela.
 *
 * Lê o `.cs` como texto de propósito: nada de compilar C# a partir do vitest. O acoplamento é o
 * caminho do diretório, e ele quebra BARULHENTO se a pasta se mover, o que é o modo de falha certo.
 */
const RAIZ = new URL('../../../src/Rastreamento.Api/Controllers/', import.meta.url)

const CONTROLLER_POR_RECURSO: Record<Recurso, string> = {
  setores: 'SetoresController.cs',
  materiais: 'MateriaisController.cs',
  componentes: 'ComponentesController.cs',
  pedidos: 'PedidosController.cs',
  agrupamentos: 'AgrupamentosController.cs',
}

/** `[Authorize(Roles = "A,B")]` e também `[Authorize(Roles = NomeDeConst)]`. */
const ATRIBUTO = /\[Authorize\(Roles\s*=\s*(?:"([^"]*)"|([A-Za-z_]\w*))\)\]/g
/** `private const string PerfisDeEscrita = "Administrador,PCP";` — ComponentesController usa isto. */
const CONSTANTE = /const\s+string\s+([A-Za-z_]\w*)\s*=\s*"([^"]*)"\s*;/g

/** Conjuntos de perfis declarados no arquivo, um por atributo `Roles`, na ordem em que aparecem. */
function perfisDeclarados(nomeDoArquivo: string): string[][] {
  const fonte = readFileSync(fileURLToPath(new URL(nomeDoArquivo, RAIZ)), 'utf8')

  const constantes = new Map<string, string>()
  for (const [, nome, valor] of fonte.matchAll(CONSTANTE)) constantes.set(nome, valor)

  const encontrados: string[][] = []
  for (const [, literal, identificador] of fonte.matchAll(ATRIBUTO)) {
    const bruto = literal ?? constantes.get(identificador)
    // Const declarada em OUTRO arquivo cairia aqui. Hoje não acontece; se acontecer, é para
    // falhar e ser resolvido, nunca para ser ignorado em silêncio.
    expect(bruto, `${nomeDoArquivo}: não resolvi os perfis de \`${identificador}\``).toBeTruthy()
    encontrados.push(bruto!.split(',').map((p) => p.trim()).sort())
  }
  return encontrados
}

describe('a tabela de escrita do front espelha os [Authorize(Roles)] do backend', () => {
  it.each(Object.keys(CONTROLLER_POR_RECURSO) as Recurso[])(
    '%s: os perfis da tabela são os mesmos do controller',
    (recurso) => {
      const doBackend = perfisDeclarados(CONTROLLER_POR_RECURSO[recurso])
      const daTabela = [...TABELA_DE_ESCRITA[recurso]].sort()

      // Ordem não importa (o backend escreve "Administrador,PCP" em um e "PCP,Administrador" em
      // outro), então a comparação é por conjunto ordenado.
      for (const conjunto of doBackend) {
        expect(conjunto, `${recurso}: backend ${conjunto} vs tabela ${daTabela}`).toEqual(daTabela)
      }
    },
  )

  it('não passa calada: todo controller tem pelo menos um atributo de perfis', () => {
    // ESTE é o teste que impede a guarda de virar decoração. Se os `Roles` deixarem de ser
    // literal no atributo — policy, claims, permissão em banco (dívida registrada em 2026-08-10) —
    // a varredura passa a não achar nada, e sem esta asserção o `for` acima ficaria VERDE
    // percorrendo lista vazia, exatamente quando parou de vigiar.
    for (const [recurso, arquivo] of Object.entries(CONTROLLER_POR_RECURSO)) {
      expect(perfisDeclarados(arquivo).length, `${recurso}: nenhum [Authorize(Roles)] em ${arquivo}`)
        .toBeGreaterThan(0)
    }
  })
})
```

```bash
cd web && npm test -- permissoesEspelham
```

Expected: `Tests  6 passed (6)` — 5 casos do `it.each` (um por recurso) + 1. **O delta desta guarda é +6, não +2:** são 2 blocos `it(` no arquivo, mas o vitest conta **casos**, e `it.each` de 5 recursos produz 5. Contar bloco em vez de caso é exatamente como o `≈187` da Task 5 e as quatro baselines da Task 6 nasceram.

**ESTE DESENHO FOI PROVADO NO PRÉ-FLIGHT, contra os controllers reais, antes de virar plano.** Quatro provas rodadas em 2026-08-10 — se a sua implementação divergir de alguma, é a implementação que está errada, não a prova:

1. **Casa com o estado de hoje.** Os 5 controllers, 14 atributos no total (setores 3, materiais 3, componentes 3, pedidos 2, agrupamentos 3), todos batendo com a tabela. A guarda passa hoje.
2. **A const é resolvida.** `ComponentesController` usa `PerfisDeEscrita`, e a varredura leu `["Administrador","PCP"]` — não vazio, não `undefined`.
3. **A M16 mata a guarda e NÃO mata o teste auto-referente.** Com `SetoresController` mutado para `"Administrador,PCP"`, o backend passa a declarar `["Administrador","PCP"]` contra a tabela `["Administrador"]`. É a prova de que a guarda lê o que diz ler.
4. **O "não passa calada" pega a mudança de forma.** Trocando `Roles` por `Policy`, a varredura acha **0** atributos — o `for` de comparação ficaria verde percorrendo lista vazia, e é o segundo `it(` que mata. É o cenário da dívida registrada: se os `Roles` deixarem de ser literais, a guarda **para de vigiar**, e tem de gritar em vez de passar.

- [ ] **Step 8: Escrever o teste do `AppShell`**

`web/src/components/AppShell.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AppShell } from './AppShell'

afterEach(cleanup)

const logout = vi.fn()

// O `AuthProvider` de verdade dispara init-refresh no mount; aqui só interessa o que o shell faz
// com a sessão já resolvida.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    },
    login: async () => {},
    logout,
  }),
}))

function renderizarShell(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<p>conteúdo da home</p>} />
          <Route path="/setores" element={<p>conteúdo de setores</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AppShell', () => {
  it('renderiza a tela filha', () => {
    // Sem o `<Outlet/>` o shell aparece e o conteúdo some — e o build passa.
    renderizarShell()

    expect(screen.getByText('conteúdo da home')).toBeTruthy()
  })

  it('dá caminho de volta a partir de qualquer tela interna', () => {
    // Hoje as seis telas internas só voltam pelo botão do navegador. Este é o defeito de navegação
    // que o shell existe para fechar.
    renderizarShell('/setores')

    expect(screen.getByRole('navigation', { name: 'Principal' })).toBeTruthy()
    expect(screen.getAllByRole('link').some((l) => l.getAttribute('href') === '/')).toBe(true)
  })

  it('leva a todas as áreas do sistema', () => {
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    for (const d of ['/', '/pedidos', '/componentes', '/materiais', '/setores']) {
      expect(destinos, `link para ${d}`).toContain(d)
    }
  })

  it('mostra os links mesmo para perfil que não pode escrever neles', () => {
    // Decisão do usuário em 2026-08-06: gating vai na AÇÃO, não no link. O PCP não escreve em
    // Setores nem Materiais, mas LÊ os dois — e a Fase 4 depende disso para o Almoxarifado.
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toContain('/setores')
    expect(destinos).toContain('/materiais')
  })

  it('marca o item da tela atual', () => {
    renderizarShell('/setores')

    const atual = screen.getAllByRole('link').find((l) => l.getAttribute('aria-current') === 'page')
    expect(atual?.getAttribute('href')).toBe('/setores')
  })

  it('distingue o item atual por fundo, tinta E peso', () => {
    // O `aria-current` sozinho não é a distinção VISUAL, e era exatamente ela que nenhuma mutação
    // tocava (D5 do pré-flight): trocar as classes de ativo e inativo entre si deixava a suíte
    // verde. Asserção token a token, não `toContain` de string — a lição da Task 5, onde
    // `toContain('bg-acao')` casava com `hover:bg-acao-fundo`.
    renderizarShell('/setores')

    const links = screen.getAllByRole('link')
    const atual = links.find((l) => l.getAttribute('aria-current') === 'page')!
    const outro = links.find((l) => l.getAttribute('href') === '/pedidos')!

    const classesDoAtual = atual.className.split(/\s+/)
    const classesDoOutro = outro.className.split(/\s+/)

    expect(classesDoAtual).toContain('bg-chrome-ativo')
    expect(classesDoAtual).toContain('text-superficie')
    expect(classesDoAtual).toContain('font-semibold')

    expect(classesDoOutro).not.toContain('bg-chrome-ativo')
    expect(classesDoOutro).not.toContain('font-semibold')
    expect(classesDoOutro).toContain('text-chrome-tinta-fraca')
  })

  it('mostra quem está logado e o perfil', () => {
    renderizarShell()

    expect(screen.getByText('Planejamento e Controle')).toBeTruthy()
    expect(screen.getByText('PCP')).toBeTruthy()
  })

  it('sai da sessão pelo botão Sair', () => {
    renderizarShell()

    fireEvent.click(screen.getByText('Sair'))

    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('abre e fecha a gaveta pelo botão de menu', () => {
    renderizarShell()

    const botao = screen.getByRole('button', { name: 'Abrir menu' })
    expect(botao.getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(botao)
    expect(screen.getByRole('button', { name: 'Fechar menu' }).getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Fechar menu' }))
    expect(screen.getByRole('button', { name: 'Abrir menu' }).getAttribute('aria-expanded')).toBe('false')
  })

  it('fecha a gaveta ao navegar', () => {
    // Sem isto, no celular a gaveta continua cobrindo a tela que o usuário acabou de abrir — e ele
    // acha que o clique não funcionou.
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const linkDaGaveta = screen.getAllByRole('link').filter((l) => l.getAttribute('href') === '/setores')
    fireEvent.click(linkDaGaveta[linkDaGaveta.length - 1])

    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })
})
```

- [ ] **Step 9: Rodar para ver falhar**

```bash
cd web && npm test -- AppShell
```

Expected: FAIL — `Failed to resolve import "./AppShell"`.

- [ ] **Step 10: Escrever o `AppShell`**

`web/src/components/AppShell.tsx`:

```tsx
import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface ItemDeNavegacao {
  para: string
  rotulo: string
}

/**
 * Todos os itens aparecem para todos os perfis, de propósito: a leitura de todos estes recursos é
 * liberada para qualquer usuário autenticado no backend (conferido em 2026-08-10). O gating de
 * perfil vive na AÇÃO — formulário e botões de (in)ativar —, não aqui.
 *
 * Com 10+ telas esta barra fica apertada e vai exigir agrupamento (ex.: "Cadastros" com submenu).
 * Custo conhecido e aceito na spec §6: o agrupamento entra quando doer, não agora.
 */
const ITENS: ItemDeNavegacao[] = [
  { para: '/', rotulo: 'Início' },
  { para: '/pedidos', rotulo: 'Pedidos' },
  { para: '/componentes', rotulo: 'Componentes' },
  { para: '/materiais', rotulo: 'Materiais' },
  { para: '/setores', rotulo: 'Setores' },
]

// Sem `font-medium` aqui: o peso é decidido por estado, mais abaixo. Duas classes de peso no
// mesmo elemento não se resolvem pela ordem em que você as escreve — quem ganha é a que vier
// depois no CSS gerado, e isso não é controlável a partir daqui.
const CONTROLE_BASE =
  'rounded-lg px-3 py-2 text-sm transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca'

// Sobre o chrome escuro o anel de foco é `marca`, não `acao`: MEDIDO em 2026-08-10, `acao` sobre
// `chrome` dá 1,640 contra os 3,0 exigidos, e o foco sumiria justamente na navegação por teclado.
// `marca` dá 6,405. É por isto que o `Botao` da Task 5 não serve aqui sem uma variante nova.
const BOTAO_DO_CHROME =
  `${CONTROLE_BASE} font-medium border border-chrome-borda text-superficie hover:bg-chrome-hover`

function classesDoLink({ isActive }: { isActive: boolean }): string {
  // Três dimensões de distinção: fundo, tinta e PESO. O peso entrou por decisão do usuário em
  // 2026-08-10, depois de ler o protótipo: o fundo do ativo dá só 1,518 contra o chrome, então
  // fundo+tinta não bastavam "de bate e pronto". Clarear o fundo era a alternativa, e é troca
  // ruim — no teto do que ainda passa AA (25% de branco) o destaque sobe para 1,974 e o rótulo
  // CAI de 6,240 para 4,801. Peso não entra nessa troca: não altera contraste nenhum.
  return `${CONTROLE_BASE} ${
    isActive
      ? 'bg-chrome-ativo font-semibold text-superficie'
      : 'font-medium text-chrome-tinta-fraca hover:bg-chrome-hover hover:text-superficie'
  }`
}

export function AppShell() {
  const { estado, logout } = useAuth()
  const [gavetaAberta, setGavetaAberta] = useState(false)

  const usuario = estado.status === 'autenticado' ? estado.usuario : null

  // A gaveta fecha no `onClick` de cada link dela (mais abaixo), e não num efeito sobre a rota:
  // o efeito rodaria também quando a navegação vem de outro lugar (um cartão da Home, o `Navigate`
  // do login), e fechar algo que já está fechado é render à toa.
  return (
    <div className="min-h-screen bg-fundo font-sans text-tinta">
      <header className="bg-chrome">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          <span className="text-lg font-semibold tracking-tight text-marca">Rastru</span>

          {/* Barra: some abaixo de 768px, onde a gaveta assume. */}
          <nav aria-label="Principal" className="hidden md:flex md:items-center md:gap-1">
            {ITENS.map((i) => (
              <NavLink key={i.para} to={i.para} end={i.para === '/'} className={classesDoLink}>
                {i.rotulo}
              </NavLink>
            ))}
          </nav>

          <div className="hidden md:flex md:items-center md:gap-3">
            {usuario && (
              <span className="text-right text-sm leading-tight text-chrome-tinta-apagada">
                <span className="block font-medium text-superficie">{usuario.nomeCompleto}</span>
                <span className="block text-xs">{usuario.perfil}</span>
              </span>
            )}
            <button type="button" onClick={logout} className={BOTAO_DO_CHROME}>
              Sair
            </button>
          </div>

          <button
            type="button"
            onClick={() => setGavetaAberta((a) => !a)}
            aria-expanded={gavetaAberta}
            aria-label={gavetaAberta ? 'Fechar menu' : 'Abrir menu'}
            className={`${BOTAO_DO_CHROME} md:hidden`}
          >
            {gavetaAberta ? '✕' : '☰'}
          </button>
        </div>

        {/* Gaveta: mesma lista, empilhada, só abaixo de 768px. O celular Android da fábrica é uso
            declarado, não hipótese. */}
        {gavetaAberta && (
          <nav aria-label="Menu" className="flex flex-col gap-1 border-t border-chrome-ativo px-4 pb-4 md:hidden">
            {ITENS.map((i) => (
              <NavLink
                key={i.para}
                to={i.para}
                end={i.para === '/'}
                onClick={() => setGavetaAberta(false)}
                className={classesDoLink}
              >
                {i.rotulo}
              </NavLink>
            ))}

            {/* Pé da gaveta. O "Sair" ocupa a largura toda (sem `self-start`) por achado do
                usuário sobre o protótipo: com largura automática, o botão herdava a mesma padding
                dos links, então o TEXTO dele alinhava com os outros textos enquanto a CAIXA
                avançava para fora — duas linhas verticais competindo. Em largura total a borda
                coincide com o fundo do item ativo, e sobra uma linha só. */}
            <div className="mt-2 flex flex-col gap-2 border-t border-chrome-ativo pt-3">
              {usuario && (
                <span className="px-3 text-xs leading-snug text-chrome-tinta-apagada">
                  <span className="block text-sm font-medium">{usuario.nomeCompleto}</span>
                  {usuario.perfil}
                </span>
              )}
              <button type="button" onClick={logout} className={`${BOTAO_DO_CHROME} text-left`}>
                Sair
              </button>
            </div>
          </nav>
        )}
      </header>

      <Outlet />
    </div>
  )
}
```

**Um ponto de atenção no teste, e ele não é contornável:** com a gaveta **aberta**, `Sair` e o nome do usuário existem **duas vezes** no DOM (barra + gaveta), e `getByText('Sair')` falha com *"found multiple elements"*. Na montagem dos testes a gaveta começa fechada, então o teste de logout passa como está; o teste da gaveta usa `getAllByRole` por isso. **Se você mudar a estrutura, ajuste os testes para `getAllBy*` — não remova um dos dois botões.** No celular a barra está escondida por CSS, e é o botão da gaveta que o usuário alcança; remover um deles deixaria um dos dois tamanhos de tela sem saída de sessão.

- [ ] **Step 11: Rodar até ficar verde**

```bash
cd web && npm test -- AppShell
```

Expected: `Tests  10 passed (10)`.

- [ ] **Step 12: Ligar o shell no roteador**

`web/src/App.tsx`:

```tsx
import { Routes, Route, Navigate } from 'react-router-dom'
import { LoginPage } from './pages/LoginPage'
import { HomePage } from './pages/HomePage'
import { SetoresPage } from './pages/SetoresPage'
import { MateriaisPage } from './pages/MateriaisPage'
import { ComponentesPage } from './pages/ComponentesPage'
import { PedidosPage } from './pages/PedidosPage'
import { PedidoDetalhePage } from './pages/PedidoDetalhePage'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { AppShell } from './components/AppShell'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      {/*
        Rota de layout: o shell embrulha TODAS as telas internas de uma vez, em vez de cada tela
        repetir cabeçalho e caminho de volta. O `ProtectedRoute` sobe para cá junto — antes ele
        estava repetido em seis linhas, e uma tela nova podia nascer sem ele por esquecimento.
      */}
      <Route element={<ProtectedRoute><AppShell /></ProtectedRoute>}>
        <Route path="/" element={<HomePage />} />
        <Route path="/setores" element={<SetoresPage />} />
        <Route path="/materiais" element={<MateriaisPage />} />
        <Route path="/componentes" element={<ComponentesPage />} />
        <Route path="/pedidos" element={<PedidosPage />} />
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
```

- [ ] **Step 13: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: **`Tests  259 passed (259)`** (230 + 29) · build limpo · lint só com o warning alheio de `AuthContext.tsx:48`.

O delta de **+29**, recontado caso a caso (não bloco a bloco):

| Arquivo | Casos |
|---|---|
| `contraste.test.ts` — 5 pares novos no `it.each(PARES)` | +5 |
| `permissoes.test.ts` | +5 |
| `usePermissao.test.tsx` | +3 |
| `permissoesEspelhamOBackend.test.ts` — `it.each` de 5 recursos + 1 | +6 |
| `AppShell.test.tsx` | +10 |
| **total** | **+29** |

**Confira o número medindo, não somando:** se der diferente, reporte o real e **não** ajuste a conta para bater. (O `+25` que este plano trazia numa versão anterior de hoje era meu erro: contei os dois `it.each` como um caso cada. Corrigido antes do despacho — mas é a prova de que a conta tem de ser conferida contra a medição, nunca o contrário.)

**Se algum teste de tela quebrar aqui, leia com cuidado:** os testes de tela renderizam a tela **direto**, sem o `App`, então o shell não entra em nenhum deles. Uma quebra aqui significa que você mexeu numa tela — o que esta task não faz.

**Duplicação esperada, NÃO conserte:** a `HomePage` mostra nome, perfil e "Sair" (`HomePage.tsx:39-53`), e o shell também. Da Task 7 até a **Task 11** a Home terá isso em duplicata, e a fileira de `<Link>` de `HomePage.tsx:47-51` fica redundante com a barra. É escopo da Task 11. Mesma coisa com o `min-h-screen` duplicado (shell + as 6 telas antigas), que produz a rolagem vertical de alguns pixels descrita em `Pagina.tsx:14-15` — some tela a tela no retrofit das Tasks 8–11.

- [ ] **Step 14: Ver no navegador — este Step NÃO é executável por subagente**

Ele exige navegador, DevTools e a API de pé. **Quem executa é o usuário**, depois do commit. O implementer **declara o Step como pendente** e não o marca como feito. (Decisão de 2026-08-10; o MCP de Chrome não está configurado nesta máquina — conferido.)

```bash
cd web && npm run dev
```

Abra `http://localhost:5173`, entre com `admin` / `Admin@123` (a API precisa estar de pé: `dotnet run --project src/Rastreamento.Api`, com o Docker no ar) e confirme, **manualmente**:

- a barra aparece em todas as telas internas e o item da tela atual está marcado;
- abaixo de 768px (DevTools → Toggle device toolbar) a barra vira o botão de menu e a gaveta abre;
- **nenhuma tela rola na horizontal em nenhuma largura ≥ 320px** — critério da spec §11 que nenhum teste em jsdom alcança. **É um range, não um ponto:** arraste de 320 a ~1280 e registre 320, 767 e 768, ou use o snippet localizador da spec §11, que acha o elemento culpado sem depender de qual largura está aberta;
- `Tab` percorre os links com anel de foco visível sobre o chrome escuro;
- **o deslocamento dos vizinhos ao trocar de tela**: o item ativo é `font-semibold` e o texto em 600 é mais largo que em 500. Previsão registrada em 2026-08-10, **não observada**: como cada clique navega e re-renderiza a barra inteira, não deve aparecer como pulo. Se incomodar, a mitigação é reservar a largura do estado 600, e ela entra depois.

**Reporte o que viu, item por item.** As telas ainda estão com o visual antigo dentro do shell novo — é esperado; o retrofit começa na Task 8.

- [ ] **Step 15: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `permissoes.ts` — acrescentar `'PCP'` a `setores` | ≥ 1 |
| M2 | `permissoes.ts` — trocar `.includes(perfil)` por `.length > 0` | ≥ 1 |
| M3 | `permissoes.ts` — acrescentar `'Gestao'` a `pedidos` | ≥ 1 |
| M4 | `AppShell.tsx` — remover o `<Outlet />` | ≥ 1 |
| M5 | `AppShell.tsx` — remover o item `/setores` de `ITENS` | ≥ 1 |
| M6 | `AppShell.tsx` — trocar `NavLink` por `Link` (perde o `aria-current`) | ≥ 1 |
| M7 | `AppShell.tsx` — remover `end={i.para === '/'}` | ≥ 1 (o "Início" ficaria marcado em toda tela) |
| M8 | `AppShell.tsx` — remover `onClick={() => setGavetaAberta(false)}` dos links da gaveta | ≥ 1 |
| M9 | `AppShell.tsx` — trocar `onClick={logout}` por `onClick={() => {}}` | ≥ 1 |
| M10 | `AppShell.tsx` — trocar `aria-label` do botão por texto fixo `'Menu'` | ≥ 1 |
| M11 | `App.tsx` — tirar o `ProtectedRoute` da rota de layout | **0 esperado** — sobrevivente declarado |
| M12 | `AppShell.tsx` — trocar as classes de ativo e inativo entre si em `classesDoLink` | ≥ 1, **em `AppShell.test.tsx`, no teste de fundo/tinta/peso** |
| M13 | `AppShell.tsx` — remover `font-semibold` do ramo ativo | ≥ 1, **no mesmo teste** |
| M14 | `usePermissao.ts` — trocar o corpo por `return true` | ≥ 1 |
| M15 | `usePermissao.ts` — remover a guarda `estado.status === 'autenticado' &&` | ≥ 1 (tem de morrer no caso anônimo, por TypeError ou por `false`) |
| M16 | **`src/Rastreamento.Api/Controllers/SetoresController.cs`** — trocar `Roles = "Administrador"` por `Roles = "Administrador,PCP"` nos três atributos | ≥ 1, e **SÓ** em `permissoesEspelhamOBackend.test.ts` |
| M17 | `index.css` — `--color-chrome-borda` de volta a `#719592` | ≥ 1, **no par de contraste** (dá 2,895 contra o mínimo 3,0) |
| M18 | `AppShell.tsx` — remover `hidden md:flex` da barra e `md:hidden` da gaveta | **0 esperado** — sobrevivente declarado |

**M16 é a mutação que decide se a guarda do Step 7 vale alguma coisa.** Ela muta o **backend**, não a tabela — e por isso `permissoes.test.ts` tem de continuar VERDE enquanto a guarda morre. Se ela matar os dois, você mutou a tabela por engano; se não matar nada, a guarda não está lendo o que diz ler. **Reverta o `.cs` com cuidado** — é o único momento desta fase em que se toca em `src/`, e a árvore tem de voltar limpa.

**M17 tem de morrer no PAR DE CONTRASTE**, não em teste de componente. Se morrer em outro lugar — ou não morrer — o token não está sob a guarda e o Step 2 não fez o que diz.

**Duas fronteiras honestas, para DECLARAR e não encobrir:**
- **M11** — não há teste de roteamento no projeto (varri `web/src/`: o único lugar que monta o `App` é `main.tsx:12`). A proteção das rotas é mantida por leitura de código. Não invente um teste de `App` só para fechá-la.
- **M18** — jsdom não aplica media query, e os dois `<nav>` já estão no DOM o tempo todo. Se as classes responsivas sumirem, barra e gaveta aparecem juntas em toda largura e os 10 testes seguem verdes. **É mais grave que o `flex-wrap` da Task 6** e depende inteiramente do Step 14. Declare com essa palavra.

- [ ] **Step 16: Commit**

```bash
git add web/src/index.css web/src/tema/contraste.test.ts \
        web/src/auth/permissoes.ts web/src/auth/permissoes.test.ts \
        web/src/auth/usePermissao.ts web/src/auth/usePermissao.test.tsx \
        web/src/auth/permissoesEspelhamOBackend.test.ts \
        web/src/components/AppShell.tsx web/src/components/AppShell.test.tsx \
        web/src/App.tsx
git commit -m "feat(web): shell de navegacao com gaveta, tabela de permissoes e guarda de espelhamento"
```

**Varra o diff por segredo antes do commit** — o repositório é PÚBLICO.

**Definition of done da Task 7:** suíte em **baseline medida + 29** (**259**, se a baseline ainda for os 230 medidos em 2026-08-10 — **o delta é o que vincula**); as 18 mutações medidas, com M11 e M18 declaradas e **M16 e M17 mortas no lugar certo, nomeado no relatório**; o Step 14 **declarado pendente** (não é executável por subagente); build e lint limpos; `git status` sem sobra em `src/` depois da M16.

---

### Task 8: Retrofit de `SetoresPage`, `MateriaisPage` e `PedidosPage`

**Files:**
- Modify: `web/src/pages/SetoresPage.tsx` + `SetoresPage.test.tsx`
- Modify: `web/src/pages/MateriaisPage.tsx` + `MateriaisPage.test.tsx`
- Modify: `web/src/pages/PedidosPage.tsx` + `PedidosPage.test.tsx`

**Interfaces:**
- Consumes: `Pagina`, `Botao`, `Campo`+`CLASSES_DE_CONTROLE`, `BannerDeErro` (Task 5); `ListaDeCadastro`+`ItemDeCadastro`, `Pilula`, `EstadoVazio` (Task 6); `usePodeEscrever` (Task 7); `mensagemDeErro` (Task 2).
- Produces: nada de novo. **É consumo puro** — se você precisou criar uma primitiva aqui, ela faltou nas Tasks 5–6 e isso é achado a reportar.

**As três não recebem redesenho** (spec §7): são formulário + lista, e o container novo mais as primitivas as resolvem por inteiro. **Se durante a execução alguma destoar de fato ao lado das novas, absorva na própria fase** — são as telas mais simples do sistema — e não abra fase nova.

> **A duplicação do ciclo de CRUD entre as telas é DELIBERADA e já foi adjudicada — não é achado.**
> Decisão do usuário no pre-flight de 2026-08-06, com a análise medida antes de perguntar.
>
> A review de branch da 1B registrou que "a duplicação deixou de ser barata", e o defeito I1
> apareceu nas três telas ao mesmo tempo — o argumento a favor de extrair um `useCadastroSimples`
> é real. O que decide contra é o **alcance**: o hook serviria **só `SetoresPage` e
> `MateriaisPage`**. `PedidosPage` não tem `ativo`/`reativar` (não há coluna `Ativo` em `Pedido`),
> e `ComponentesPage` carrega pelo `useBuscaPaginada` — o `carregar` dela é outro. Extrair uma
> abstração para **dois** consumidores é o mesmo problema que a spec §5 nomeia no critério das
> primitivas, invertido.
>
> **Se a review levantar isto, a resposta é este bloco.** Reabrir exige medição nova — por exemplo,
> um terceiro consumidor real aparecendo.

**Quatro mudanças de comportamento entram junto do markup**, e são as que a spec §9 pede:
1. `mensagemDeErro` no lugar do texto fixo em todo `catch`;
2. `EstadoVazio` distinguindo lista vazia de erro;
3. botão desabilitado durante a mutação (`enviando`);
4. gating na ação — `usePodeEscrever` esconde formulário e botões de (in)ativar.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test
```

Expected: **`Tests  272 passed (272)`** — o que o passe curto de primitivas (caminho C do usuário, pré-Task 8) fechou: 268 do fix pass da Task 7 + 4 dos nove pontos do brief `.superpowers/sdd/fase1d-passe-primitivas-brief.md` (`Pagina` perde o `main` para o `AppShell`, repasse e afordâncias de `Botao`, o anel de `CLASSES_DE_CONTROLE` preso ao de `Botao`), medido e reportado em `.superpowers/sdd/fase1d-passe-primitivas-report.md`. Este Step dizia `268` — corrigido aqui na mesma passada, como manda a regra em vigor.

**Confira contra o que o passe de primitivas REALMENTE fechou, não contra este número.** O 272 é o medido no passe; se divergir, **pare e reporte** — e atualize este Step junto, que é a regra em vigor desde 2026-08-10: task fechada com delta ≠ 0 corrige a baseline das seguintes na mesma passada.

- [ ] **Step 2: Reescrever a `SetoresPage`**

`web/src/pages/SetoresPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import {
  listarSetores, criarSetor, definirAtivoSetor, ehConflito, type SetorDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'

export function SetoresPage() {
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [nome, setNome] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('setores')

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setSetores(await listarSetores(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os setores.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarSetor(nome)
      if (ehConflito(resultado)) {
        if (resultado.existeInativo) {
          setErro(`Já existe um setor "${nome}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um setor com este nome.')
        }
        return
      }
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o setor.'))
    } finally {
      setEnviando(false)
    }
  }

  // O `try/catch` continua sendo a fronteira REAL de perfil (F2): esconder o botão é conveniência
  // de interface, e o 403 do backend segue valendo para quem chamar a API por fora da tela.
  async function alternarAtivo(setor: SetorDto) {
    try {
      await definirAtivoSetor(setor.id, !setor.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o setor.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoSetor(id, true)
      setErro(null)
      setIdReativavel(null)
      setNome('')
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o setor.'))
    }
  }

  return (
    <Pagina titulo="Setores">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Campo rotulo="Nome do setor">
              {(id) => (
                <input
                  id={id}
                  value={nome}
                  onChange={(e) => setNome(e.target.value)}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…">Adicionar</Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && setores.length === 0 ? (
        // `erro === null` é o que distingue "não há setores" de "a listagem falhou": no `catch`
        // de `carregar`, `setSetores` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também seria verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner de erro, afirmando "nenhum setor cadastrado" a partir de uma falha de conexão.
        <EstadoVazio
          titulo="Nenhum setor cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {setores.map((s) => (
            <ItemDeCadastro
              key={s.id}
              ativo={s.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(s)}>
                  {s.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              {s.nome}
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

**Corrigido no fix pass da review da Task 8 (`.superpowers/sdd/fase1d-task-8-fix-report.md`, C1):**
o bloco acima transcrevia `setores.length === 0` sozinho, e no `catch` de `carregar` a lista fica
`[]` sem nunca ter sido preenchida — a condição também era verdadeira numa falha de rede, então o
`EstadoVazio` ("Nenhum setor cadastrado") aparecia JUNTO do banner de erro, afirmando um fato
sobre o banco a partir de uma falha de conexão. Corrigido para `erro === null && setores.length
=== 0`, com o comentário reescrito para não afirmar mais o que era falso (era o achado I1: "aqui
só sobra o caso 'não há setores'" não estava certo enquanto a condição não checava `erro`). Mesma
correção nos blocos dos Steps 6 e 8.

- [ ] **Step 3: Atualizar o teste da `SetoresPage`**

Os dois testes que já existem passam a precisar de uma sessão **com** permissão de escrita (senão o formulário e o checkbox somem). Reescreva `web/src/pages/SetoresPage.test.tsx`:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SetoresPage } from './SetoresPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra, então ele é variável de teste agora.
let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const CORTE = { id: 1, nome: 'Corte', ativo: true }

describe('SetoresPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os setores que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CORTE])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os setores.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('Corte')
    expect(screen.queryByText('Não foi possível carregar os setores.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, um Operador que tentasse inativar
    // leria "Não foi possível alterar o setor" e tentaria de novo, indefinidamente.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([CORTE]),
      '/api/setores/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há setores', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum setor cadastrado')).toBeTruthy()
  })

  it('desabilita o botão enquanto o cadastro está em voo', async () => {
    // O POST fica pendurado de propósito, para a asserção acontecer com a mutação EM VOO. Sem o
    // `enviando`, o PCP clica duas vezes no wifi lento e o segundo POST volta 409 — a tela
    // acusando de duplicado o cadastro que ela mesma acabou de fazer.
    let liberar: (r: Response) => void = () => {}
    let jaListou = false
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        // A primeira chamada é a listagem (GET); a segunda é o POST.
        if (!jaListou) { jaListou = true; return respostaJson([]) }
        return new Promise<Response>((r) => { liberar = r })
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    // Solta a resposta no fim para o teste não terminar com promise pendurada, o que vaza estado
    // para o caso seguinte.
    liberar(respostaJson({ id: 2, nome: 'Solda', ativo: true }, 201))
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê setores mas não escreve — `[Authorize(Roles = "Administrador")]` no backend. O link
    // continua no shell de propósito; o que some é a ação.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.queryByLabelText('Nome do setor')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })
})
```

**Sobre o `jaListou` do último teste:** ele distingue GET de POST **contando chamadas**, o que é frágil se a tela passar a fazer uma listagem a mais. Uma alternativa mais robusta é olhar `init.method` — `fetchPorRota` recebe a URL, então isso exige alterar o kit para repassar o segundo argumento. **Não altere o kit por causa deste teste**; se a contagem quebrar numa task futura, aí sim é o momento.

**Corrigido no fix pass da review da Task 8** (C1, I3, M4, m2, m3 — ver
`.superpowers/sdd/fase1d-task-8-fix-report.md`): a lista de testes acima ficou incompleta e um
teste tinha desenho que não observava o que afirmava provar.
- **C1**: acrescente `não mostra o estado vazio quando a listagem falha` (fetch rejeita, espera o
  banner, e assere `queryByText('Nenhum setor cadastrado')` nulo).
- **I3**: acrescente `limpa o campo depois de cadastrar com sucesso` — sem isto, ninguém provava
  que `salvar` limpa `nome` no sucesso (era mutante vivo). O mock precisa de **três** respostas
  contadas por chamada (GET inicial → array; POST → o setor criado; GET de recarga → array de
  novo), não duas — `setores.map` quebra se a 3ª chamada devolver o objeto único do POST.
- **M4**: o teste `desabilita o botão enquanto o cadastro está em voo` precisa virar `desabilita o
  botão enquanto o cadastro está em voo, e reabilita depois`. O mock de "pendurado para sempre"
  (`jaListou`) pendurava também a 3ª chamada (o GET de recarga que `carregar` dispara depois do
  `liberar`), e o teste terminava sem nunca esperar o botão voltar — por isso remover o
  `finally { setEnviando(false) }` sobrevivia. Troque para um mock que CONTA as chamadas: 1ª (GET)
  devolve a lista; 2ª (POST) devolve a promise que `liberar` resolve; 3ª (GET de recarga) resolve
  normal. Depois de `liberar(...)`, espere o botão voltar (`findByText('Adicionar')`) e assere que
  **não** está mais desabilitado.
- **m2**: o comentário de `explica o 403 em vez do texto genérico` ("um Operador que tentasse
  inativar leria…") ficou falso depois do gating — o Operador não vê mais o botão. Reescreva
  citando o cenário real do 403 (perfil mudado no servidor, tabela do front defasada, chamada por
  fora da tela).
- **m3**: acrescente, dentro de `mostra estado vazio quando não há setores`, a asserção do texto
  da `descricao` (`'Use o formulário acima para criar o primeiro.'`) — prende o ramo positivo do
  ternário `descricao={podeEscrever ? '…' : undefined}`, que não tinha cobertura em nenhum lado.

- [ ] **Step 4: Rodar até ficar verde**

```bash
cd web && npm test -- SetoresPage
```

Expected: `Tests  6 passed (6)` na entrega original; **`Tests  8 passed (8)` depois do fix pass da
review** (C1 e I3 acrescentados, ver a caixa acima).

- [ ] **Step 5: Checkpoint — sem commit aqui**

**Corrigido no pré-flight (`.superpowers/sdd/fase1d-task-8-preflight.md`): este Step mandava
commitar a `SetoresPage` isolada.** Medido: das 13 tasks deste plano, as outras 12 fazem **um único**
commit no fim — Task 8 era a única com dois (este Step e o Step 12). Não commite ainda: confirme que
`npm test -- SetoresPage` está verde (Step 4) e siga para a `MateriaisPage`. O commit único da task
inteira acontece no Step 12, que já foi atualizado para incluir os arquivos da `SetoresPage`.

- [ ] **Step 6: Reescrever a `MateriaisPage`**

`web/src/pages/MateriaisPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import {
  listarMateriais, criarMaterial, definirAtivoMaterial, ehConflito,
  type MaterialDto, type NovoMaterial,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoMaterial = { codigo: '', descricao: '', unidadeMedida: '' }

export function MateriaisPage() {
  const [materiais, setMateriais] = useState<MaterialDto[]>([])
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoMaterial>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('materiais')

  async function carregar(comInativos: boolean) {
    setCarregando(true)
    try {
      setMateriais(await listarMateriais(comInativos))
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os materiais.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(incluirInativos) }, [incluirInativos])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarMaterial(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Material_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErro(`Já existe um material com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um material com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o material.'))
    } finally {
      setEnviando(false)
    }
  }

  async function alternarAtivo(material: MaterialDto) {
    try {
      await definirAtivoMaterial(material.id, !material.ativo)
      setErro(null)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o material.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoMaterial(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(incluirInativos)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o material.'))
    }
  }

  return (
    <Pagina titulo="Materiais">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  // Monoespaçada em código de material: alinha na coluna e facilita conferir
                  // contra o desenho na bancada (spec §4). É funcional, não decorativo.
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Unidade" dica="UN, KG, M…">
              {(id, idDaDica) => (
                /* Texto livre de propósito: NVARCHAR(10) sem CHECK no DDL, sem lista fechada. */
                <input
                  id={id}
                  aria-describedby={idDaDica}
                  value={form.unidadeMedida}
                  onChange={(e) => setForm({ ...form, unidadeMedida: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      <label className="flex items-center gap-2 text-sm text-tinta-fraca">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => setIncluirInativos(e.target.checked)}
          className="size-4 accent-acao"
        />
        Mostrar inativos
      </label>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && materiais.length === 0 ? (
        // `erro === null` é o que distingue "não há materiais" de "a listagem falhou" — ver a
        // correção do C1 na `SetoresPage` (Step 2). Mesmo mecanismo.
        <EstadoVazio
          titulo="Nenhum material cadastrado"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {materiais.map((m) => (
            <ItemDeCadastro
              key={m.id}
              ativo={m.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(m)}>
                  {m.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{m.codigo}</span>
              {' — '}
              {m.descricao}
              {' '}
              <Pilula>{m.unidadeMedida}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

- [ ] **Step 7: Atualizar o teste da `MateriaisPage`**

O arquivo atual tem 2 testes que renderizam a tela e batem em `/api/materiais`. Aplique **as mesmas** mudanças da `SetoresPage`:

1. acrescente o `vi.mock('../auth/AuthContext', …)` com a variável `perfil` e `beforeEach(() => { perfil = 'Administrador' })`;
2. troque os mocks de `fetch` inline por `fetchPorRota` + `respostaJson`;
3. **os campos deixam de ter `placeholder`** — troque todo `getByPlaceholderText('Código')` por `getByLabelText('Código')`, e idem para "Descrição" e "Unidade";
4. acrescente os quatro testes novos, com os mesmos textos da `SetoresPage` adaptados:
   - explica o 403 em vez do texto genérico (rota `/api/materiais/1/ativo`);
   - mostra estado vazio (`'Nenhum material cadastrado'`);
   - desabilita o botão enquanto o cadastro está em voo (`'Salvando…'`);
   - esconde formulário e ação para quem não pode escrever (`perfil = 'PCP'`).

**Corrigido no fix pass da review da Task 8** (C1, I3, M4, M7, m2 — ver
`.superpowers/sdd/fase1d-task-8-fix-report.md`), mesmo padrão da `SetoresPage` (Step 3):
5. acrescente `não mostra o estado vazio quando a listagem falha` (C1);
6. acrescente `limpa o formulário depois de cadastrar com sucesso` (I3) — mock com três respostas
   contadas (GET → array; POST → o material criado; GET de recarga → array de novo);
7. renomeie `desabilita o botão enquanto o cadastro está em voo` para `desabilita o botão enquanto
   o cadastro está em voo, e reabilita depois` (M4), com o mesmo mock contado por chamada da
   `SetoresPage` — a versão "pendurado para sempre" nunca prova a reabilitação;
8. em `mostra os materiais que a API devolveu`, acrescente `expect(await
   screen.findByText('KG')).toBeTruthy()` (M7) — sem isto, nenhuma asserção da suíte olhava o
   texto da `Pilula` de unidade, e o `KG` da fixture só aparecia por acidente;
9. reescreva o comentário de `explica o 403 em vez do texto genérico` (m2), mesmo motivo da
   `SetoresPage`.

Expected ao fim: `Tests  6 passed (6)` na entrega original; **`Tests  8 passed (8)` depois do fix
pass da review** (C1 e I3 acrescentados — M4 e M7 mudam o desenho de testes já existentes, sem
aumentar a contagem), em `npm test -- MateriaisPage`.

- [ ] **Step 8: Reescrever a `PedidosPage`**

`web/src/pages/PedidosPage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarPedidos, criarPedido, ehConflito, formatarDataHora,
  type PedidoDto, type NovoPedido,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoPedido = { numero: '', cliente: '' }

/**
 * Status do `CK_Pedido_Status`. `Concluido` é o único que ganha tom positivo; `Cancelado`, o
 * negativo. Os intermediários ficam neutros — verde e vermelho são reservados a estado que exige
 * decisão, e "em produção" não exige nenhuma.
 */
function tomDoStatus(status: string): 'neutro' | 'positivo' | 'negativo' {
  if (status === 'Concluido') return 'positivo'
  if (status === 'Cancelado') return 'negativo'
  return 'neutro'
}

export function PedidosPage() {
  const [pedidos, setPedidos] = useState<PedidoDto[]>([])
  const [form, setForm] = useState<NovoPedido>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('pedidos')

  async function carregar() {
    setCarregando(true)
    try {
      setPedidos(await listarPedidos())
      setErro(null)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os pedidos.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarPedido(form)
      if (ehConflito(resultado)) {
        // Pedido não tem reativação (não há coluna Ativo): o caminho é abrir o que já existe.
        setErro('Já existe um pedido com este número.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar()
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o pedido.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Pagina titulo="Pedidos">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do pedido">
              {(id) => (
                <input
                  id={id}
                  value={form.numero}
                  onChange={(e) => setForm({ ...form, numero: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Cliente">
              {(id) => (
                <input
                  id={id}
                  value={form.cliente}
                  onChange={(e) => setForm({ ...form, cliente: e.target.value })}
                  required
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Abrindo…" className="self-start">
            Abrir pedido
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && pedidos.length === 0 ? (
        // `erro === null` é o que distingue "não há pedidos" de "a listagem falhou" — ver a
        // correção do C1 na `SetoresPage` (Step 2). Mesmo mecanismo.
        <EstadoVazio
          titulo="Nenhum pedido aberto"
          descricao={podeEscrever ? 'Use o formulário acima para abrir o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro>
          {pedidos.map((p) => (
            <ItemDeCadastro key={p.id}>
              {/*
                O item inteiro é o alvo do clique, e não só o número: numa tela de bancada com
                tablet, alvo pequeno erra. `after:absolute after:inset-0` estende a área clicável do
                link ao cartão sem aninhar elementos interativos.
              */}
              <Link
                to={`/pedidos/${p.id}`}
                className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
              >
                <span className="font-medium">
                  <span className="font-mono">{p.numero}</span> — {p.cliente}
                </span>
                <span className="flex items-center gap-2 text-sm text-tinta-fraca">
                  <Pilula tom={tomDoStatus(p.status)}>{p.status}</Pilula>
                  aberto em {formatarDataHora(p.dataAbertura)}
                </span>
              </Link>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </Pagina>
  )
}
```

**O `after:inset-0` exige `relative` no `<li>`** — acrescente `relative` às classes de `ItemDeCadastro` em `web/src/components/ListaDeCadastro.tsx` e **rode `npm test -- ListaDeCadastro` para confirmar que nada quebrou**. Sem o `relative`, o pseudo-elemento se ancora no viewport e cobre a tela inteira: todo clique em qualquer lugar navegaria para o último pedido da lista. **É um defeito de tela cheia que nenhum teste em jsdom pega** — confirme no navegador, na Task 12.

- [ ] **Step 9: Atualizar o teste da `PedidosPage`**

O arquivo criado na Task 1 tem 4 testes. Ajuste:

1. acrescente o `vi.mock('../auth/AuthContext', …)` com `perfil` (default `'PCP'`, que **pode** escrever pedidos);
2. troque `getByPlaceholderText('Código do pedido')` por `getByLabelText('Código do pedido')`, e `'Cliente'` idem;
3. **`mostra os pedidos que a API devolveu` precisa de novo texto-alvo.** O novo markup separa
   `p.numero` num `<span className="font-mono">` aninhado dentro do `<span className="font-medium">`
   — `getNodeText` do Testing Library (`web/node_modules/@testing-library/dom/dist/get-node-text.js`)
   só concatena os `TEXT_NODE` **diretos** de um elemento, então nenhum nó tem mais o texto completo
   `'PED-001 — Fábrica Alfa'`; o próprio comentário deste teste (linhas 43–49 do arquivo atual) já
   previa a quebra e nomeava a saída: assere a **ordem**, não a string inteira — por exemplo,
   `within(await screen.findByText('PED-001')).getByText(...)` não serve (nós irmãos, não
   ancestrais); prefira `expect(container.querySelector('li')!.textContent).toMatch(/PED-001.*Fábrica Alfa/)`
   ou dois `getByText` separados (`'PED-001'` e `/Fábrica Alfa/`) mais uma asserção de ordem no DOM;
4. **`mostra erro quando a listagem falha` precisa do texto novo.** A resposta mockada é um 500
   (`respostaJson({ erro: 'Falhou' }, 500)`), e `mensagemDeErro` (`web/src/api/erros.ts:36`) mapeia
   status ≥ 500 para `'O servidor não respondeu como esperado. Tente de novo em instantes.'` — **não**
   mais o fallback fixo `'Não foi possível carregar os pedidos.'` que o teste hoje afirma. Troque o
   texto esperado;
5. acrescente três testes:
   - **estado vazio**: `'Nenhum pedido aberto'`;
   - **gating**: `perfil = 'Operador'` → `queryByLabelText('Código do pedido')` é `null` e a lista continua visível;
   - **status como pílula**: um pedido `Concluido` e um `Cancelado` na mesma lista. **Não baste
     asserir que os dois textos aparecem** — `Pilula` renderiza `children` independente de `tom`,
     então a M8 do Step 11 (`tomDoStatus` sempre `'positivo'`) não muda texto nenhum e sobreviveria a
     um teste só de texto. Assere também o tom: por exemplo,
     `screen.getByText('Concluido').className` (ou o `closest` do `<span>` da pílula) contém um token
     `positivo-*`, e o de `Cancelado` contém `negativo-*`.

**Corrigido no fix pass da review da Task 8** (C1, I2, M10 — ver
`.superpowers/sdd/fase1d-task-8-fix-report.md`):
6. acrescente `não mostra o estado vazio quando a listagem falha` (C1);
7. **o teste de gating do item 5 (`Operador`) prova só a metade negativa** — `Operador` não
   escreve nem `pedidos` nem `setores`, então a M9 (`usePodeEscrever('pedidos')` → `('setores')`)
   morre por carona num teste de CRUD, não no teste cujo nome é sobre permissão (achado I2).
   Acrescente a metade positiva: `mostra o formulário para quem pode escrever pedidos mas não
   setores`, com `perfil = 'PCP'` (escreve `pedidos`, não escreve `setores` —
   `web/src/auth/permissoes.ts`), asserindo `findByLabelText('Código do pedido')`. **Não troque
   para `('componentes')`**: é mutante equivalente hoje, `componentes` e `pedidos` têm o mesmo
   conjunto de perfis;
8. acrescente `limpa a mensagem de erro da carga inicial quando o cadastro seguinte tem sucesso`
   (M10, equivalente ao teste que `SetoresPage`/`MateriaisPage` já tinham desde a Task 1 — I1 da
   review de branch da 1B). **Nota honesta, medida no fix pass**: a mutação M10 (remover
   `setErro(null)` de dentro de `carregar`) SOBREVIVE a este teste — é mutante equivalente NESTA
   tela, não falta de cobertura. `salvar` já chama `setErro(null)` no início de si mesma
   (`PedidosPage.tsx:54`), ANTES de chamar `carregar()`; como o único outro disparo de
   `carregar()` é o efeito de montagem (onde `erro` já nasce `null`), não existe caminho em que o
   `setErro(null)` de dentro de `carregar` seja o que limpa um erro visível — ao contrário de
   `Setores`/`Materiais`, cujo checkbox "Mostrar inativos" dispara uma `carregar` independente de
   `salvar`. Mantenha o teste mesmo assim: ele prova um comportamento real (o banner da carga
   inicial não sobrevive a um cadastro bem-sucedido), só não é o que mata M10.

Expected: `Tests  7 passed (7)` na entrega original; **`Tests  10 passed (10)` depois do fix pass
da review** (C1, I2 e M10 acrescentados).

- [ ] **Step 10: Rodar tudo**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected na entrega original: **`Tests  283 passed (283)`** (272 + 4 na Setores + 4 na Materiais + 3 na Pedidos) · build limpo · lint só com o warning alheio.

**O delta +11 é o que vincula**; o total depende do que fechou antes desta task. Este Step dizia `279` (= 268 + 11), herdado da baseline pré-passe-de-primitivas; a baseline correta é **272**, medida no passe curto de primitivas (`.superpowers/sdd/fase1d-passe-primitivas-report.md`).

**Corrigido no fix pass da review da Task 8, MEDIDO** (`.superpowers/sdd/fase1d-task-8-fix-report.md`):
a review achou 1 Critical (C1), 3 Important (I1, I2, I3) e 6 Minor (m1–m6). O fix pass fechou C1,
I1, I2, I3, m1, m2, m3, M4, M7 e M10 (m4, m5, m6 ficaram fora por decisão do usuário — pedem
desenho, não conserto, e vão para a Task 9/review de branch — **e o pré-flight de 2026-08-13 MEDIU
que "Task 9" é dono errado do m6**: o único `after:inset-0` do projeto está em `PedidosPage.tsx:131`
num `ItemDeCadastro` **sem** `acao`, e nenhuma task desta fase, nem a 9A, nem a 9B, nem a 10, combina
overlay e ação; reatribuir é decisão do usuário, ver `.superpowers/sdd/fase1d-task-9-preflight.md`
§7.3). **Delta real do fix pass: `+7`**, não
o `+9` que a previsão do brief tinha ("PREVISÃO, não medição" — o próprio brief avisava). A
composição medida: C1 (3, um teste por tela) + I2 (1) + I3 (2, um por tela gêmea) + M10 (1) = 7. M7
e m3 caíram para **dentro** de casos já existentes (uma asserção a mais, não um `it(` novo) —
exatamente o cenário que o brief previa como possível ("o M7 e o m3 podem caber em casos
existentes, e aí o delta cai"). M4, m1 e m2 são redesenho de mock e correção de comentário, sem
teste novo (delta 0), como o brief também previa. **Suíte final: `Tests  290 passed (290)`** (283 +
7) · build limpo · lint só com o warning alheio.

**Se o número divergir, reporte o número real e a composição** — não ajuste o plano; a contagem exata depende de quantos testes antigos você fundiu ou dividiu.

- [ ] **Step 11: Medir as mutações**

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | `SetoresPage.tsx` — trocar `mensagemDeErro(e, …)` por só o fallback no `catch` de `alternarAtivo` | ≥ 1 |
| M2 | `SetoresPage.tsx` — trocar `podeEscrever &&` por `true &&` no formulário | ≥ 1 |
| M3 | `SetoresPage.tsx` — remover `setEnviando(true)` | ≥ 1 |
| M4 | `SetoresPage.tsx` — remover o `finally { setEnviando(false) }` | ≥ 1 (o botão nunca voltaria) — **na entrega original SOBREVIVIA** (0 mortes): o mock "pendurado para sempre" também pendurava a 3ª chamada e o teste terminava antes de observar a reabilitação. **MEDIDO no fix pass da review, morre depois do mock redesenhado** (Step 3/7): `SetoresPage.test.tsx` → `desabilita o botão enquanto o cadastro está em voo, e reabilita depois`; `MateriaisPage.test.tsx`, mesmo nome. |
| M5 | `SetoresPage.tsx` — trocar `erro === null && setores.length === 0` por `false` | ≥ 1 (a condição mudou no fix pass do C1; a mutação continua matando pelo mesmo teste de estado vazio) |
| M6 | `MateriaisPage.tsx` — trocar `podeEscrever` de `'materiais'` por `'componentes'` | ≥ 1 (o PCP ganharia formulário que o backend recusa) |
| M7 | `MateriaisPage.tsx` — trocar `{m.unidadeMedida}` da pílula por `{m.tipo}` (campo inexistente) | ≥ 1 — **na entrega original SOBREVIVIA** (0 mortes): nenhuma asserção da suíte olhava o texto da unidade. **MEDIDO no fix pass da review**: morre em `mostra os materiais que a API devolveu` (asserção de `'KG'` acrescentada). |
| M8 | `PedidosPage.tsx` — trocar `tomDoStatus` para devolver `'positivo'` sempre | ≥ 1 (só morre se o teste de "status como pílula" do Step 9 asserir o tom/classe da `Pilula`, não só o texto — corrigido no pré-flight) |
| M9 | `PedidosPage.tsx` — trocar `usePodeEscrever('pedidos')` por `('setores')` | ≥ 1 — **na entrega original morria pelo teste ERRADO**: só o de CRUD (`limpa o formulário e recarrega a lista depois de abrir um pedido`), por carona; o teste cujo nome é sobre permissão (`Operador`) passava sob a mutação (achado I2, não escreve nem `pedidos` nem `setores`). **MEDIDO no fix pass**: com `mostra o formulário para quem pode escrever pedidos mas não setores` (`PCP`) acrescentado, morre no teste certo. |
| M10 | `PedidosPage.tsx` — remover `setErro(null)` do sucesso de `carregar` | ≥ 1 na entrega original (**0 mortes** — não havia teste algum cobrindo). **MEDIDO no fix pass, depois de acrescentar o teste equivalente ao I1 da 1B**: continua **SOBREVIVENDO** (0 mortes) — mutante equivalente NESTA tela. `salvar` já chama `setErro(null)` no início de si mesma antes de chamar `carregar()`, e o único outro disparo de `carregar()` é o efeito de montagem (onde `erro` nasce `null`); não existe caminho em que o `setErro(null)` de dentro de `carregar` seja observável. Diferente de `Setores`/`Materiais`, que têm o checkbox "Mostrar inativos" disparando uma `carregar` independente de `salvar`. Argumentado abrindo a implementação, não pela semântica do nome — ver `.superpowers/sdd/fase1d-task-8-fix-report.md`. |

- [ ] **Step 12: Commit (único da task — ver Step 5)**

```bash
git add web/src/pages/SetoresPage.tsx web/src/pages/SetoresPage.test.tsx web/src/pages/MateriaisPage.tsx web/src/pages/MateriaisPage.test.tsx web/src/pages/PedidosPage.tsx web/src/pages/PedidosPage.test.tsx web/src/components/ListaDeCadastro.tsx
git commit -m "feat(web): retrofit de SetoresPage, MateriaisPage e PedidosPage com as primitivas"
```

**Definition of done da Task 8:** as três telas sem nenhuma classe antiga (`max-w-md`, `border rounded px-3 py-2`, `text-gray-*`, `text-red-600`); as 10 mutações medidas com ≥ 1 morte; suíte, build e lint verdes.

**Nota honesta, corrigida no fix pass da review (regra em vigor desde 2026-08-10 — task que fecha
com achado corrige a DoD que a causou, na mesma passada):** na entrega original, **3 das 10
mutações tinham 0 mortes** (M4, M7, M10) e a M9 morria pelo teste errado (I2) — a DoD "as 10
mutações medidas com ≥ 1 morte" não estava cumprida como escrita, só como intenção. Depois do fix
pass: **M4, M7 e a versão certa de M9 passam a morrer no lugar certo**; **M10 continua com 0
mortes, mas agora por mutante equivalente medido e argumentado** (não por falta de teste) — ver a
nota na tabela do Step 11 e o relatório do fix pass. A DoD real desta task, portanto, é "9 das 10
mutações com ≥ 1 morte, e a 10ª (M10) documentada como equivalente" — não "as 10 com ≥ 1 morte"
como o texto original afirmava.

---

### Task 9A: Re-layout da `ComponentesPage`

**Files:**
- Modify: `web/src/pages/ComponentesPage.tsx` (227 linhas hoje)
- Modify: `web/src/pages/ComponentesPage.test.tsx` (**987 linhas, 34 testes** — a maior suíte de tela da fase)

**Interfaces:**
- Consumes: `mensagemDeErro` (Task 2); `Pagina`, `Botao`, `Campo`/`CLASSES_DE_CONTROLE`, `BannerDeErro` (Task 5); `ListaDeCadastro`/`ItemDeCadastro`, `Pilula`, `EstadoVazio` (Task 6); `usePodeEscrever` (Task 7).
- Produces: nada.
- **NÃO consome `useBuscaPaginada` (Task 3) nem `ControlesDePaginacao` (Task 6).** Os dois entram na **Task 9B**. A tela continua com o estado local de hoje (`busca`, `pagina`, `tamanho`, `incluirInativos`, `sequenciaRef`, `carregar`, os três `mudar*`, `totalDePaginas`) e com o bloco de paginação escrito à mão — só que vestido com as primitivas.

> **Por que 9A e 9B são duas tasks, e não uma (decisão do usuário em 2026-08-13, sobre a
> recomendação medida do pré-flight — `.superpowers/sdd/fase1d-task-9-preflight.md`).**
>
> 1. **São duas mudanças independentes, com testes independentes.** MEDIDO: das cinco classes de
>    adaptação da suíte da tela, **quatro vêm do re-layout** ((a) harness, (b) seletores,
>    (d) mensagens, (e) markup do item) e **só a paginação (b′) vem da adoção do hook**. As 6
>    quebras de mensagem e a do texto riscado não têm nada com o `useBuscaPaginada`.
> 2. **A conta deixa de ser subtração e soma ao mesmo tempo.** A 9A é **só soma** (nenhum dos 34
>    sai); a **9B é a única task desta fase que subtrai**. Foi a mistura das duas direções que
>    impediu a Task 9 unificada de ter um número único — o defeito D3 do pré-flight.
> 3. **As lacunas de mutação caem em lados diferentes.** M9 (gating de perfil) e M6 (estado vazio)
>    são do re-layout → 9A. Debounce e seletor de tamanho são da adoção do hook → 9B. Juntas, uma
>    task fecharia com quatro buracos de proveniência misturada.
> 4. **Custo honesto, MEDIDO:** a `ComponentesPage.tsx` é tocada duas vezes e a suíte é adaptada
>    duas vezes — a segunda só nos de paginação e nos que mudam de dono, ~10 testes. Não é a
>    "reescrita dupla" que as Global Constraints usaram para justificar o hook nascer sem
>    consumidor (aquilo era reescrever 987 linhas duas vezes).

**A tela que a spec §7 marca como re-layout obrigatório:** busca + filtro + seletor de tamanho + paginação **não cabem em 448px**. Esta task resolve a largura, as primitivas, as mensagens e o gating; a 9B troca o motor de busca por baixo.

**Aviso sobre a suíte de tela — CORRIGIDO pelo pré-flight de 2026-08-12, o texto original estava factualmente errado:** os 34 testes de hoje foram escritos contra o comportamento **sem debounce**, mas **nenhum deles quebra por causa do debounce** — e, de todo modo, **o debounce não entra nesta task**, entra na 9B. MEDIDO (ver `.superpowers/sdd/fase1d-task-9-preflight.md`): **zero** testes deste arquivo digitam três letras, e **zero** assertam uma contagem de requisições por tecla — os três `toHaveBeenCalledTimes` existentes (`:518/:523`, `:569/:574`, `:611/:617` — números **corrigidos** na continuação de 2026-08-13: os `:527/:532`, `:578/:583`, `:620/:626` que este parágrafo trazia antes eram as linhas da árvore de trabalho descartável da primeira passada, com o harness já inserido, e não batiam com o arquivo em disco) contam 1 e 2 depois de **uma** mudança de campo.

**As classes de adaptação desta task são QUATRO — (a) harness, (b) seletores, (d) mensagens, (e) markup do item (MEDIDO na continuação de 2026-08-13).** Aplicando **exatamente** o que o Step 3(a)+(b) manda — harness e a tabela de seletores, nada mais — a suíte fecha em **7 vermelhos | 27 verdes**, e os sete são exatamente os da tabela abaixo.

> **CORRIGIDO pelo pré-flight da 9A (2026-08-14) — o número anterior era `9 vermelhos | 25 verdes`,
> e era da task 9 UNIFICADA.** Naquele Step 2 a tela adotava `ControlesDePaginacao`, cujo
> `if (totalDePaginas <= 1) return null` derrubava junto os dois testes de paginação. **A 9A não
> adota a primitiva, então esses dois ficam VERDES aqui** — é exatamente o que o parágrafo "Os dois
> de paginação (`:132` e `:772`) NÃO quebram nesta task" já afirmava, e o `9 | 25` o contradizia.
> **MEDIDO em 2026-08-14** contra o bloco do Step 2 desta task, com `(a)` e `(b)` aplicados e nada
> mais: `Tests  7 failed | 27 passed (34)`, e os sete nomes batem um a um com a tabela.

Os sete:

| # | Teste | Por quê | Classe |
|---|---|---|---|
| 1 | `mostra mensagem de erro quando reativar falha` (`:360`) | PATCH 403 → `mensagemDeErro` devolve **"Seu perfil não tem permissão para esta ação."** | **(d) mensagens** |
| 2 | `mostra mensagem de erro quando alternar o ativo falha` (`:725`) | idem, PATCH 403 | **(d) mensagens** |
| 3 | `limpa uma mensagem de erro anterior quando alternar o ativo tem sucesso depois de uma falha` (`:831`) | idem, PATCH 403 | **(d) mensagens** |
| 4 | `cadastro com sucesso limpa uma mensagem de erro anterior de outra acao` (`:862`) | idem, PATCH 403 | **(d) mensagens** |
| 5 | `mostra mensagem de erro quando salvar falha com um erro que nao e conflito` (`:396`) | POST 500 → **"O servidor não respondeu como esperado. Tente de novo em instantes."** | **(d) mensagens** |
| 6 | `mostra mensagem de erro quando a carga da lista falha` (`:742`) | GET 500 → mesma mensagem de 500 | **(d) mensagens** |
| 7 | `exibe um componente inativo com o botao Reativar e o texto riscado` (`:753`) | `getByText('INA-001').closest('span')` passa a devolver o `<span className="font-mono font-semibold">` do código; o `line-through` é do **avô** (`ItemDeCadastro`). Falha MEDIDA: *expected 'font-mono font-semibold' to contain 'line-through'* | **(e) markup do item** |

**Atenção:** os DOIS testes que mantêm a mensagem antiga são os que rejeitam com `new Error('rede caiu')` (`:554` e `:930`) — `Error` puro não é `ErroDeApi` nem `TypeError`, então `mensagemDeErro` cai no *fallback*, que é a frase de hoje. Não os troque junto com os outros.

**Os dois de paginação (`:132` e `:772`) NÃO quebram nesta task — MEDIDO em 2026-08-14 (era DERIVADO; o pré-flight da 9A rodou e confirmou):** eles quebrariam por causa do `if (totalDePaginas <= 1) return null` de `ControlesDePaginacao`, e a 9A não adota a primitiva; o bloco de paginação à mão continua renderizando "Anterior", "Próxima" e "Página 1 de 1 — 0 no total" com uma página só, como hoje. Nenhum dos dois aparece entre os 7 vermelhos de `(a)+(b)`. **Confirme rodando mesmo assim** — se algum deles ficar vermelho aqui, você adotou `ControlesDePaginacao` sem querer, e isso é escopo da 9B.

**NENHUM dos 34 sai nesta task, e nenhum precisa sair.** MEDIDO (2026-08-14, pré-flight da 9A): com as **quatro** classes de adaptação desta task aplicadas — `(a)`, `(b)`, `(d)`, `(e)` — a suíte da tela fecha **34/34 verde**, `Tests  34 passed (34)`. **Toda remoção nesta fase é de-duplicação voluntária, não necessidade** — e as remoções decididas pelo usuário (U3) são todas de propriedade que vive no `useBuscaPaginada`, portanto **todas da 9B**. Não apague teste aqui para fechar conta nenhuma.

- [ ] **Step 1: Confirmar a baseline e inventariar a suíte que vai mudar**

```bash
cd web && npm test -- ComponentesPage && npm test
```

Expected: `Tests  34 passed (34)` no arquivo — **34, não 33** (o plano dizia 33 em seis lugares; corrigido no pré-flight de 2026-08-12, MEDIDO por `npm test -- ComponentesPage` e por `grep -c "^\s*it(" src/pages/ComponentesPage.test.tsx`). Suíte inteira: **`Tests  290 passed (290)` / 26 arquivos — MEDIDA em 2026-08-13**, nesta branch, com a árvore de `web/` limpa.

**A conta desta task, com a composição aberta:**

```
290 (baseline MEDIDA)  −  0 removidos  +  7 novos  =  297
                                        ↑ 5 MEDIDOS (fecham em 295) + 2 DERIVADOS (X3 e X6)
```

**Leia a marca de evidência, ela não é enfeite:** o pré-flight da 9A executou a task inteira numa
árvore descartável com **os 5 testes** e leu `Tests  295 passed (295)` / `Test Files  26 passed
(26)` — o `295` é **MEDIDO**. O `297` é `295 MEDIDO + 2 DERIVADO`: os dois testes que fecham X3 e
X6 entraram por **decisão do usuário em 2026-08-14** e nunca rodaram. Meça você mesmo — é a guarda
contra estar num checkout diferente, e agora também contra os dois derivados.

Os 7 novos, um a um — **conte-os no seu próprio Step 3, não confie neste número**:

| # | Teste novo | Onde | Por quê |
|---|---|---|---|
| 1 | `manda o formulário inteiro no POST, com o tipo escolhido` | Step 3 | C1 da review da Task 6 encenado; mata M1/M2, **mas não é o único matador delas** — o usuário decidiu **mantê-lo assim mesmo** (ver o comentário do próprio teste) |
| 2 | `distingue "nada corresponde à busca" de "catálogo vazio"` | Step 3 | único matador da M6 (MEDIDO: contra os 34 adaptados sozinhos, M6 mata **0**) |
| 3 | `esconde formulário e ação de inativar para quem não pode escrever` | Step 3 | gating de perfil (F2 / spec §6) |
| 4 | `não afirma "nenhum componente cadastrado" quando a carga da lista falhou` | Step 3 | **decisão U1** — a sonda que prova que o banner de erro e o estado vazio não aparecem juntos |
| 5 | `mostra o formulário para o perfil PCP…` | Step 3 | **fecha a M9**, que o pré-flight mediu com **0 mortes** |
| 6 | `mostra o tipo de cada componente na lista` | Step 3 | **DECISÃO DO USUÁRIO, 2026-08-14: fecha o X3.** Matador da **M16** — apagar `<Pilula>{c.tipo}</Pilula>` deixava os 39 verdes e o tipo sumia da lista inteira |
| 7 | `dá à tela o título "Componentes" no h1` | Step 3 | **DECISÃO DO USUÁRIO, 2026-08-14: fecha o X6.** Matador da **M17** — `titulo="XXX"` deixava os 39 verdes e o `<h1>` podia dizer qualquer coisa |

> **Por que 7 e não 5 (decisão do usuário, 2026-08-14).** O item 5 do pré-flight da 9A achou **seis**
> decisões do Step 2 que nenhuma das 10 mutações tocava (X1–X6, todas MEDIDAS sobrevivendo a
> `39 passed (39)`). O usuário decidiu fechar **duas** — X3 e X6, as únicas que são **conteúdo
> visível da tela** — e deixar X1, X2, X4 e X5 registradas como achado, para decidir depois. Cada
> uma que fecha custa **um teste e uma mutação**: fechar com teste e não registrar a mutação
> fecharia a instância sem deixar guarda executável, e a próxima pessoa apagaria o teste sem que
> nada acusasse.

**Nenhum `it.each` nesta task** (conferido: os dois `it.each` do repositório estão em `permissoesEspelhamOBackend.test.ts:49` e `tema/contraste.test.ts:103`) — se você introduzir um, lembre que **o vitest conta CASOS, não blocos**: um `it.each` de 5 linhas é +5, não +1. Contar bloco em vez de caso é como o `≈187` da Task 5 e as quatro baselines da Task 6 nasceram.

Liste os nomes antes de mexer — é este inventário que o relatório vai comparar no fim:

```bash
cd web && grep -n "^\s*it(" src/pages/ComponentesPage.test.tsx
```

- [ ] **Step 2: Re-layout da `ComponentesPage`**

**Este Step NÃO é medido** — ao contrário do bloco da 9B, que o pré-flight instalou e compilou. É a
tela de hoje com as primitivas por cima; escreva-o conferindo cada assinatura no disco.

**O que NÃO muda (mantenha byte a byte, linhas 17–123 de `ComponentesPage.tsx`):** todo o estado
(`componentes`, `total`, `busca`, `pagina`, `tamanho`, `incluirInativos`, `idReativavel`,
`carregando`), o `sequenciaRef` **com o comentário dele inteiro**, o `totalDePaginas`, o `carregar`
de quatro parâmetros, o `useEffect`, os três `mudar*` com `setPagina(1)`, e o corpo de `salvar`,
`alternarAtivo` e `reativar`. **A Task 9B é quem apaga isso** — apagar aqui é sair de escopo e
deixar a tela sem busca.

**O que muda, e só isto:**

1. **Imports.** Entram `mensagemDeErro` (`../api/erros`), `usePodeEscrever` (`../auth/usePermissao`)
   e as sete primitivas. **Sai** `import { Link } from 'react-router-dom'` — o `← Início` do topo
   some, porque o caminho de volta é o shell da Task 7. É o que a Task 8 fez nas três telas dela.
2. **`const podeEscrever = usePodeEscrever('componentes')`.**
3. **`const [enviando, setEnviando] = useState(false)`**, com `setEnviando(true)` no começo de
   `salvar` e `finally { setEnviando(false) }` — é o que alimenta o `carregando` do `Botao`. Mesmo
   padrão de `SetoresPage.tsx:21` e `:105`.
4. **Os quatro `catch` passam a usar `mensagemDeErro`** (é isto que quebra os 6 testes de mensagem,
   e a quebra é correta). O `catch` de `carregar` precisa passar a capturar: `catch (e) {` … `if
   (minhaSequencia !== sequenciaRef.current) return; setErro(mensagemDeErro(e, 'Não foi possível
   carregar os componentes.')) }`. As frases de *fallback* são as de hoje, uma a uma.
5. **O `return` inteiro**, abaixo.

```tsx
  const buscando = busca.trim() !== ''

  return (
    <Pagina titulo="Componentes">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            {/* Lista fechada (CK_Componente_Tipo): select, não input livre. */}
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
                  className={CLASSES_DE_CONTROLE}
                >
                  {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      {/*
        A barra de filtros é o que não cabia em 448px (spec §7). Em `max-w-3xl` os três controles
        cabem lado a lado a partir de `sm`, e empilham no celular sem rolagem horizontal.
      */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1">
          <Campo rotulo="Buscar por código ou descrição">
            {(id) => (
              <input
                id={id}
                value={busca}
                onChange={(e) => mudarBusca(e.target.value)}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-tinta-fraca sm:pb-2.5">
          <input
            type="checkbox"
            checked={incluirInativos}
            onChange={(e) => mudarInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <Campo rotulo="Por página">
          {(id) => (
            <select
              id={id}
              value={tamanho}
              onChange={(e) => mudarTamanho(Number(e.target.value))}
              className={CLASSES_DE_CONTROLE}
            >
              {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          )}
        </Campo>
      </div>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && componentes.length === 0 ? (
        // DECISÃO U1 (usuário, 2026-08-13). `erro === null &&` NÃO é enfeite: no `catch` de
        // `carregar`, `setComponentes` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também é verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner, e afirmando "não há componentes" a partir de uma falha de conexão. É a MESMA
        // forma do Critical que o fix pass da Task 8 pagou; `SetoresPage.tsx:129` diz o mesmo.
        // Os três vazios que a spec §9 manda distinguir: busca sem resultado, catálogo vazio e —
        // acima, no banner — erro de rede. Antes os três renderizavam a mesma lista muda.
        <EstadoVazio
          titulo={buscando ? 'Nenhum componente encontrado' : 'Nenhum componente cadastrado'}
          descricao={
            buscando
              ? `Nada corresponde a "${busca}".`
              : podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined
          }
        />
      ) : (
        <ListaDeCadastro>
          {componentes.map((c) => (
            <ItemDeCadastro
              key={c.id}
              ativo={c.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(c)}>
                  {c.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{c.codigo}</span>
              {' — '}
              {c.descricao}
              {' '}
              <Pilula>{c.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      {/* PROVISÓRIO — a Task 9B troca este bloco inteiro por `<ControlesDePaginacao>`. Ele está
          aqui, com as primitivas em vez das classes antigas, só porque a 9A não pode adotar a
          primitiva de paginação sem adotar o hook junto (o `if (totalDePaginas <= 1) return null`
          dela quebra dois testes desta suíte, e essa quebra é da 9B). NÃO acrescente o
          `return null` de uma página só aqui: isso importaria a colisão da 9B para dentro da 9A. */}
      <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3">
        <Botao variante="secundario" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>
          Anterior
        </Botao>
        <span className="text-sm text-tinta-fraca">
          Página {pagina} de {totalDePaginas} — {total} no total
        </span>
        <Botao variante="secundario" disabled={pagina >= totalDePaginas} onClick={() => setPagina(pagina + 1)}>
          Próxima
        </Botao>
      </nav>
    </Pagina>
  )
```

- [ ] **Step 3: Adaptar a suíte da tela e acrescentar os cinco testes**

Trabalhe **em cima** do arquivo existente, não do zero. Quatro classes de mudança — (a) harness, (b) seletores, (d) mensagens, (e) markup do item. **A classe (c), "mudou de dono", NÃO existe nesta task**: ela pressupõe o `useBuscaPaginada`, e é a 9B inteira.

**(a) Harness.** Acrescente no topo, como nas outras telas:

```tsx
import { respostaJson, fetchPorRota } from '../testes/api'

let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))
```

> **⚠️ DEFEITO MEDIDO no snippet acima (continuação do pré-flight, 2026-08-13): falta o reset de
> `perfil` no `beforeEach`.** `web/src/pages/SetoresPage.test.tsx:24-27` — o harness que a Task 8
> entregou e que este snippet copia — tem `beforeEach(() => { perfil = 'Administrador'; … })`, e
> este não. Consequência MEDIDA: o `perfil = 'Operador'` do terceiro teste novo **vaza para todo
> teste que rodar depois dele**, e o seguinte falha com *Unable to find a label with the text of:
> Código* — o formulário não existe para Operador. Acrescente `perfil = 'Administrador'` na
> primeira linha do `beforeEach` que já existe no arquivo. **Com dois testes de perfil nesta task
> (Operador e PCP), esse reset deixou de ser higiene e virou pré-requisito.**

**(b) Seletores.** Os campos deixam de ter `placeholder`:

| Antes | Depois |
|---|---|
| `getByPlaceholderText('Código')` | `getByLabelText('Código')` |
| `getByPlaceholderText('Descrição')` | `getByLabelText('Descrição')` |
| `getByLabelText('Tipo')` | continua (já era `aria-label`, agora é `<label>`) |
| `getByPlaceholderText('Buscar por código ou descrição')` | `getByLabelText('Buscar por código ou descrição')` |
| `getByLabelText('Por página')` | continua (era `<label>` embrulhando o `select`, agora é `Campo`) |

**(d) Mensagens.** Os 6 da tabela dos sete, acima: troque a frase esperada pela que `mensagemDeErro` devolve para o status daquele mock. **Não** troque `:554` nem `:930`.

**(e) Markup do item.** `:753` — o `line-through` mudou de nó: é do `<span>` de `ItemDeCadastro`, não do `<span>` do código. Asserte no ancestral certo.

**Acrescente os sete testes.** Os três primeiros são os que a Task 9 unificada já previa; o quarto e o quinto vêm das decisões U1 e do fechamento da M9 (`≥ 1` morte é DoD, e a M9 hoje mata **zero**); o sexto e o sétimo fecham o X3 e o X6 por decisão do usuário em 2026-08-14.

> **Duas exigências de forma, e as duas sustentam o `+11` que a 9B depende (ver a abertura da 9B):**
>
> 1. **Os sete entram no FIM do `describe`**, depois do último `it(` de hoje (`:956`), imediatamente
>    antes do `})` que fecha o bloco (linha 987). Não os intercale entre os testes existentes:
>    tudo o que a 9B cita por número de linha está **acima** deles, e é isso que faz o deslocamento
>    do arquivo ser **exatamente +11** (o do harness), e não +11 mais o tamanho dos testes novos.
> 2. **O sexto teste precisa de `within`**, que este arquivo ainda não importa. Acrescente-o à linha
>    de import que já existe — `import { render, screen, cleanup, fireEvent, waitFor } from
>    '@testing-library/react'` vira `import { render, screen, within, cleanup, fireEvent, waitFor }
>    from '@testing-library/react'`. **Edição de linha existente, não linha nova** — o
>    `AppShell.test.tsx:3` já usa esse import, então é convenção do repo, não invenção.

```tsx
it('manda o formulário inteiro no POST, com o tipo escolhido', async () => {
  // C1 da review da Task 6: `criarComponente(form)` -> `criarComponente({...form, tipo: 'Bruto'})`
  // matava ZERO **na época daquela review**. O usuário escolhia "Montagem" e o sistema gravava
  // "Bruto", em silêncio — e `tipo` é justamente o campo que governa a Receita Padrão da 1C.
  //
  // ⚠️ CORRIGIDO no pré-flight da 9A (2026-08-14): hoje o C1 JÁ TEM DONO. O teste
  // `cadastra com sucesso: envia o corpo digitado (inclusive o tipo escolhido)…` (`:435`) foi
  // escrito exatamente para isso — o comentário dele, em `:433-434`, diz que troca o `<select>` de
  // Tipo para um valor diferente do default "senao a mutacao … sobreviveria por coincidencia".
  // MEDIDO: a M1 mata `:435` mesmo SEM este teste, e a M2 mata `:435` e `:786`. Ou seja, este
  // teste NÃO acrescenta nenhuma morte de mutação — é o achado nº 5 do relatório de pré-flight.
  //
  // DECIDIDO pelo usuário em 2026-08-14: ele **FICA**, e a composição não muda por causa dele.
  // Razão: prova o **corpo exato** do POST (`toBe(JSON.stringify(...))`), o que `:435` faz de forma
  // mais frouxa; e tirar um teste barato e correto para economizar uma linha de conta custaria
  // propagar uma baseline nova para a 9B — a dívida que mais caro saiu nesta fase. Não o remova
  // "para limpar duplicação": a redundância aqui é deliberada e está registrada.
  const fetchMock = fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
  fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'CMP-1' } })
  fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Suporte' } })
  fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'Montagem' } })
  fireEvent.click(screen.getByText('Adicionar'))

  const post = fetchMock.mock.calls.find((c) => (c[1] as RequestInit)?.method === 'POST')!
  expect((post[1] as RequestInit).body)
    .toBe(JSON.stringify({ codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Montagem' }))
})

it('distingue "nada corresponde à busca" de "catálogo vazio"', async () => {
  // Sem timers falsos: nesta task a busca ainda dispara a requisição na tecla, sem debounce.
  // A 9B reescreve este teste — ver o Step 3 dela.
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
  expect(await screen.findByText('Nenhum componente cadastrado')).toBeTruthy()

  fireEvent.change(screen.getByLabelText('Buscar por código ou descrição'), { target: { value: 'XPTO' } })

  expect(await screen.findByText('Nenhum componente encontrado')).toBeTruthy()
  expect(screen.getByText('Nada corresponde a "XPTO".')).toBeTruthy()
})

it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
  perfil = 'Operador'
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({
      itens: [{ id: 1, codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Bruto', ativo: true }],
      total: 1, pagina: 1, tamanho: 20,
    }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  expect(await screen.findByText('CMP-1')).toBeTruthy()
  expect(screen.queryByLabelText('Código')).toBeNull()
  expect(screen.queryByText('Inativar')).toBeNull()
})

it('não afirma "nenhum componente cadastrado" quando a carga da lista falhou', async () => {
  // DECISÃO U1. A sonda que o pré-flight de 2026-08-13 rodou contra o bloco original do Step 2 (o
  // que dizia só `itens.length === 0`): com o GET em 500, `queryByText('Nenhum componente
  // cadastrado')` NÃO era nulo — a tela mostrava o banner de erro E o estado vazio ao mesmo tempo.
  // É a mesma forma do Critical que o fix pass da Task 8 pagou. Sem este teste o conserto do
  // Step 2 entra sem guarda, e a Task 8 já provou que essa forma volta.
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ erro: 'Falha' }, 500),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  expect(await screen.findByRole('alert')).toBeTruthy()
  expect(screen.queryByText('Nenhum componente cadastrado')).toBeNull()
  expect(screen.queryByText('Nenhum componente encontrado')).toBeNull()
})

it('mostra o formulário para o perfil PCP, que escreve em componentes mas não em setores', async () => {
  // MATADOR DA M9, e o único possível. MEDIDO no pré-flight: `permissoes.ts:19-23` dá
  // `componentes: ['Administrador','PCP']` e `setores: ['Administrador']`; a suíte só usava
  // `Administrador` (escreve nos dois) e `Operador` (não escreve em nenhum), e nenhum dos dois
  // separa os recursos — por isso trocar `('componentes')` por `('setores')` matava ZERO.
  perfil = 'PCP'
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  expect(await screen.findByLabelText('Código')).toBeTruthy()
})

it('mostra o tipo de cada componente na lista', async () => {
  // MATADOR DA M16, e fecha o X3 do pré-flight da 9A (decisão do usuário, 2026-08-14). MEDIDO
  // naquele passe: apagar `<Pilula>{c.tipo}</Pilula>` do item deixava os 39 testes VERDES — o tipo
  // do componente sumia da lista inteira, em silêncio, e `tipo` é justamente o campo que governa a
  // Receita Padrão da 1C. A `Pilula` tem teste próprio (`Pilula.test.tsx`), mas ele prova a
  // PRIMITIVA; ninguém provava que ESTA tela a usa para mostrar o tipo.
  //
  // `within(item)` e não `screen.getByText('Montagem')` direto: com o formulário na tela, o
  // `<select>` de Tipo também tem uma `<option>Montagem</option>`, e a busca global acharia DUAS
  // ocorrências e estouraria. O escopo no `<li>` é o que torna a asserção sobre a lista, e não
  // sobre o formulário — molde de `PedidosPage.test.tsx:62` (`closest('li')!`).
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({
      itens: [{ id: 1, codigo: 'CMP-1', descricao: 'Suporte', tipo: 'Montagem', ativo: true }],
      total: 1, pagina: 1, tamanho: 20,
    }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  const item = (await screen.findByText('CMP-1')).closest('li')!
  expect(within(item).getByText('Montagem')).toBeTruthy()
})

it('dá à tela o título "Componentes" no h1', async () => {
  // MATADOR DA M17, e fecha o X6 do pré-flight da 9A (decisão do usuário, 2026-08-14). MEDIDO
  // naquele passe: trocar `titulo="Componentes"` por `titulo="XXX"` deixava os 39 testes VERDES —
  // o `<h1>` da tela podia dizer qualquer coisa e nenhum deles lia o título.
  //
  // `Pagina.test.tsx:9` prova que a PRIMITIVA põe o título no `<h1>`; este prova que ESTA tela
  // passa o título certo para ela. São duas propriedades diferentes, e só a segunda é da 9A.
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  }))

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)

  expect((await screen.findByRole('heading', { level: 1 })).textContent).toBe('Componentes')
})
```

- [ ] **Step 4: Rodar até ficar verde**

```bash
cd web && npm test -- ComponentesPage && npm test && npm run build && npm run lint
```

Expected: `Tests  41 passed (41)` no arquivo (34 adaptados + 7 novos) e **`Tests  297 passed (297)`** na suíte / **26 arquivos**.

**A proveniência dos dois números, e ela é mista:** o pré-flight da 9A executou este Step inteiro em 2026-08-14, contra o bloco do Step 2 e os **cinco** testes como estão escritos, e **MEDIU** `Tests  39 passed (39)` no arquivo, `Test Files  26 passed (26)` e `Tests  295 passed (295)` na suíte, `npm run build` verde (`✓ built in 544ms`) e `npm run lint` com **só** o warning alheio conhecido (`AuthContext.tsx:48`). Os testes **6 e 7** (X3 e X6, decisão do usuário de 2026-08-14) entraram **depois** dessa medição e nunca rodaram: `41` e `297` são `39 + 2` e `295 + 2`, **DERIVADOS**. Se o total divergir, **reporte o número real e siga**; se algum dos 34 tiver saído, você saiu do escopo desta task.

**Verificação de tipo é `npm run build`** (`tsc -b && vite build`), não `tsc --noEmit`.

- [ ] **Step 5: Medir as mutações**

| # | Mutação em `ComponentesPage.tsx` | Mortes esperadas | Matador |
|---|---|---|---|
| M1 | `criarComponente(form)` → `criarComponente({ ...form, tipo: 'Bruto' })` | ≥ 1 (**MEDIDO na 9A: 2**) | `cadastra com sucesso…` (`:435`) **e** o teste novo `manda o formulário inteiro no POST…`. ⚠️ **O `:435` sozinho já mata** — ver o achado sobre o teste novo nº 1 |
| M2 | `criarComponente(form)` → trocar `codigo` por `descricao` no objeto | ≥ 1 (**MEDIDO na 9A: 3**) | `cadastra com sucesso…` (`:435`), `cadastro com sucesso apos um conflito anterior esconde o botao Reativar o existente` (`:786`) e o teste novo nº 1 |
| M3 | remover `setForm(FORMULARIO_VAZIO)` do ramo de sucesso | ≥ 1 (**MEDIDO na 9A: 2**) | `cadastra com sucesso…` (`:435`) e `cadastro com sucesso apos um conflito anterior…` (`:786`) |
| M4 | remover `await carregar(busca, incluirInativos, pagina, tamanho)` do ramo de sucesso de `salvar` | ≥ 1 (**MEDIDO na 9A: 1**) | `cadastra com sucesso…` (`:435`) — **matador único** |
| M6 | trocar `buscando` por `false` | ≥ 1 | **só** o teste novo `distingue "nada corresponde à busca"…` — MEDIDO: contra os 34 adaptados sozinhos são **0**. O teste novo é obrigatório, não opcional. |
| M7 | trocar `resultado.existeInativo` por `!resultado.existeInativo` | ≥ 1 (**MEDIDO na 9A: 10**) | os 10 testes da família de conflito/reativar (`:153`, `:241`, `:282`, `:316`, `:360`, `:657`, `:689`, `:786`, `:895`, `:956`) |
| M8 | trocar `!componente.ativo` por `true` em `alternarAtivo` | ≥ 1 — **é o I7 da 1B**: o botão escrito "Inativar" num item já inativo (**MEDIDO na 9A: 1**) | `Inativar manda ativo=false…` (`:186`) — **matador único** |
| M9 | trocar `usePodeEscrever('componentes')` por `('setores')` | ≥ 1 (**MEDIDO na 9A: 1**) | **só** o teste novo `mostra o formulário para o perfil PCP…` |
| M11 | remover `setEnviando(false)` do `finally` | ≥ 1 (**MEDIDO na 9A: 1**) | `cadastro com sucesso apos um conflito anterior esconde o botao Reativar o existente` (`:786`) — **matador único**, e ele mata **por acidente**: é o único teste que clica "Adicionar" duas vezes, e com `enviando` travado em `true` o `rotuloCarregando` deixa o botão chamado "Salvando…", então o segundo `getByRole('button', { name: 'Adicionar' })` não acha nada |
| M12 | **NOVA** — trocar `erro === null && componentes.length === 0` por `componentes.length === 0` | ≥ 1 | teste novo `não afirma "nenhum componente cadastrado"…` (guarda executável da decisão U1) |
| M16 | **NOVA** — apagar o `<Pilula>{c.tipo}</Pilula>` do item da lista | ≥ 1 (**MEDIDO em 0 antes**: sobrevivia aos 39) | **só** o teste novo `mostra o tipo de cada componente na lista` — fecha o **X3** |
| M17 | **NOVA** — trocar `titulo="Componentes"` por `titulo="XXX"` na `Pagina` | ≥ 1 (**MEDIDO em 0 antes**: sobrevivia aos 39) | **só** o teste novo `dá à tela o título "Componentes" no h1` — fecha o **X6** |

> **Por que M16 e M17, e não M13/M14/M15:** os números não se reaproveitam entre as duas tasks da
> partida. A **9B** já usa M13 (debounce desligado), M14 (`onChange` inerte no seletor de tamanho) e
> M15 (guarda do `catch` no hook), além de M5 e M10 — e o Step 5 dela manda **re-verificar** as
> mutações herdadas da 9A pelo número. Repetir um número faria "M13" significar duas coisas
> diferentes no mesmo plano, que é exatamente como a Fase 1B perdeu o achado I7.

> **De onde vêm as "mortes esperadas" MEDIDAS acima:** os números da coluna foram **remedidos em
> 2026-08-14 pelo pré-flight da 9A**, contra a configuração daquele passe — o bloco do Step 2 da
> 9A + os 34 adaptados + os **5** testes novos de então = **39**, tudo verde antes de mutar. Cada
> mutação foi aplicada isoladamente e revertida.
>
> **⚠️ A sua configuração tem 41, não 39** (os testes 6 e 7 fecham X3 e X6, decisão do usuário de
> 2026-08-14, e entraram depois daquela medição). **Aqui a monotonia VALE, ao contrário do caso de
> (A) logo abaixo:** o código mutado é o mesmo — o bloco do Step 2 não mudou uma linha —, e só o
> conjunto de testes cresceu. Logo os números MEDIDOS acima são **piso**, não igualdade: uma
> contagem que **sobe** é esperada e não é sinal de nada; uma que **cai** é sinal real, e aí você
> mexeu no Step 2 sem querer. Contagem de mortes é propriedade do par (código, suíte): crescer a
> suíte sobre o mesmo código só pode somar.
>
> **Convenção de linha desta tabela:** os `:N` dos matadores são linhas do arquivo de **HOJE**
> (HEAD `0ae67df`) — é onde você os localiza antes de aplicar o Step 3. Depois do harness do Step
> 3(a) **todos somam +11** (MEDIDO em 2026-08-14, com o reset de `perfil` incluído): `:435` vira
> `:446`, `:786` vira `:797`, `:186` vira `:197`. **O nome do teste, escrito ao lado de cada
> citação, é a âncora que não desloca** — use-o para conferir.
>
> **⚠️ CORRIGIDO — a afirmação anterior era falsa e mandava procurar um bug que não existe.** O
> texto dizia que a configuração desta task "é um superconjunto de (A) [os 34 adaptados + 3 novos =
> 37], logo nenhuma contagem pode cair; se cair, você removeu teste". **Duas contagens caem, e não
> falta teste nenhum:** M3 de 3 → **2** e M4 de 2 → **1**. O motivo é que (A) foi medida contra o
> Step 2 da task 9 **unificada**, em que o `carregar` da tela já era o `recarregar()` do
> `useBuscaPaginada`; o conjunto de TESTES da 9A é superconjunto de (A), mas o **código mutado não é
> o mesmo**, e contagem de mortes é propriedade do par (código, suíte), não da suíte sozinha.
> **Superconjunto de testes não implica monotonia de mortes quando o alvo muda.** O que vincula
> nesta task é a DoD: `≥ 1` morte para cada uma das **12** — as 10 primeiras MEDIDO (as 10 passam),
> e M16/M17 com o matador escrito no Step 3 e nunca executado.
>
> **Mutantes equivalentes, registrados e NÃO listados:** trocar `usePodeEscrever('componentes')`
> por `('pedidos')` ou `('agrupamentos')` — as listas de perfis são **idênticas** às de
> `componentes`, e nenhum perfil do sistema as separa (MEDIDO: `('pedidos')` dá 0 mortes). Não
> tente matá-los; não há teste que possa. Só `('setores')` é distinguível, e é ele que a M9 usa.
>
> **M5 e M10 não estão aqui de propósito:** `erroDeEscrita ?? erroDeLeitura` e o
> `<ControlesDePaginacao>` só existem depois da 9B, e é lá que eles são medidos.
>
> **SEIS decisões deste Step 2 que NENHUMA das 10 mutações originais tocava — MEDIDO em 2026-08-14,
> cada uma aplicada sozinha contra os 39 verdes de então, todas SOBREVIVERAM com `Tests  39 passed
> (39)`.** O usuário **fechou duas** (X3 e X6) em 2026-08-14; as outras quatro continuam
> **registradas como achado e NÃO viram exigência desta task** — fechar cada uma custa um teste e
> uma mutação, e mexe na contagem, então é decisão dele, não sua:
>
> | # | Decisão | Mutação que sobrevive | O que passa despercebido | Estado |
> |---|---|---|---|---|
> | X1 | `descricao` do `EstadoVazio` no caso "catálogo vazio" | apagar o `podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined` | o convite some para quem pode escrever; o vazio vira mudo de novo, que é metade do que a spec §9 pediu | **aberto — achado** |
> | X2 | o `.trim()` de `const buscando = busca.trim() !== ''` | `busca.trim() !== ''` → `busca !== ''` | busca só com espaços passa a dizer `Nada corresponde a " ".` em vez de "catálogo vazio" | **aberto — achado** |
> | X3 | `<Pilula>{c.tipo}</Pilula>` no item da lista | apagar a `Pilula` | o **tipo do componente some da lista inteira** e nada quebra | ✅ **FECHADO** (usuário, 2026-08-14) → **M16**, matada pelo teste `mostra o tipo de cada componente na lista` |
> | X4 | `rotuloCarregando="Salvando…"` no botão de submit | apagar a prop | o feedback de envio em voo some (o `disabled` continua) | **aberto — achado** |
> | X5 | `aria-label="Paginação"` na `<nav>` | apagar o atributo | `<nav>` sem nome acessível; some do rotor do leitor de tela | **aberto — achado** |
> | X6 | `titulo="Componentes"` na `Pagina` | `titulo="XXX"` | **o `<h1>` da tela pode dizer qualquer coisa** | ✅ **FECHADO** (usuário, 2026-08-14) → **M17**, matada pelo teste `dá à tela o título "Componentes" no h1` |
>
> **As duas fechadas eram as duas mais caras: conteúdo visível da tela, não enfeite.** As quatro
> abertas são dívida conhecida e declarada — se você as fechar por conta própria, mexeu na contagem
> desta task e na baseline da 9B sem autorização. As da 9B — debounce e seletor de tamanho — foram
> achadas pela mesma pergunta no pré-flight anterior.
>
> **X4 continua valendo como nota de método:** a M11 mata *por causa* do `rotuloCarregando` (via
> nome acessível), e mesmo assim apagar o `rotuloCarregando` sozinho não mata nada. Morte por efeito
> colateral não é cobertura da decisão.

- [ ] **Step 6: Commit, e a verificação de que nada se perdeu**

```bash
git add web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx
git commit -m "feat(web): re-layout da ComponentesPage com as primitivas"
```

> **POLÍTICA DE COMMIT — mudou em 2026-08-14, por decisão do usuário.** *"Os commits na remote podem
> ser feitos por vocês, só o PR que eu aprovo manualmente, só garanta que nada foi perdido no
> commit."* Ou seja: **o commit é nosso** (o achado nº 6 do pré-flight da 9A, que mandava não
> commitar, **caiu** — o Step estava certo; o que faltava era a verificação). **Abrir ou mesclar PR
> continua sendo aprovação manual do usuário — não é nosso.**

**Verificação pós-commit — obrigatória, e é o que a política nova exige em troca. Execute, não presuma:**

1. **`git status --short` depois do commit** — nenhum `M` nem `??` que devesse ter entrado ficou de
   fora. Arquivo modificado que sobra no `status` é trabalho perdido: o commit diz uma coisa e a
   árvore diz outra.
2. **`git show --stat HEAD`** — os arquivos e a contagem batem com o que esta task produziu (aqui:
   `ComponentesPage.tsx` e `ComponentesPage.test.tsx`, e mais nada).
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — é a sujeira alheia
   permanente deste repo: **não commite, não reverta, não edite**. Se algum dos dois aparecer no
   `git show --stat`, o commit está errado.
4. **O repositório é PÚBLICO** (`github.com/rufino-bot/rastru`): **varredura de segredo antes do
   push é passo obrigatório**, não zelo. Nada de token, senha, connection string ou hash de senha
   real no diff.

**Definition of done da Task 9A:**

- a tela **sem nenhuma classe antiga**: `grep -nE "max-w-md|border rounded px-|text-gray-|text-red-600" src/pages/ComponentesPage.tsx` = vazio;
- `grep -c "from '\.\./hooks/useBuscaPaginada'\|from '\.\./components/ControlesDePaginacao'" src/pages/ComponentesPage.tsx` = **0** — o hook e a primitiva de paginação são da 9B;

  > **CORRIGIDO pelo pré-flight da 9A (2026-08-14): esta linha era `grep -c
  > "useBuscaPaginada\|ControlesDePaginacao"` = 0, e o próprio plano a fazia falhar.** O comentário
  > `PROVISÓRIO` que o Step 2 manda escrever cita `<ControlesDePaginacao>` literalmente, então quem
  > seguisse a task à risca escrevia o comentário e depois reprovava a DoD. **MEDIDO** na árvore com
  > o bloco do Step 2 instalado: o grep antigo devolvia **1**, e o único casamento era esse
  > comentário. A forma nova grepa o **caminho do import**, não o token, e por isso ignora
  > comentário e prosa sem enfraquecer a guarda: **adotar o hook ou a primitiva exige importá-los**,
  > e é assim que a 9B os importa (`import { useBuscaPaginada } from '../hooks/useBuscaPaginada'`,
  > `import { ControlesDePaginacao } from '../components/ControlesDePaginacao'` — ver o Step 3 dela).
  > **Prova de que o conserto ainda pega adoção de verdade, MEDIDA:** com o bloco do Step 2
  > instalado o grep novo devolve **0**; acrescentando as duas linhas de import da 9B ao mesmo
  > arquivo, devolve **2**. A guarda continua fechando a fronteira da partida.
- `grep -c "sequenciaRef" src/pages/ComponentesPage.tsx` ≥ 1 — a guarda de sequência **continua na tela** nesta task; ela só muda de casa na 9B;
- as **12 mutações** do Step 5 medidas com **≥ 1 morte cada**, com o matador **nomeado** no relatório (nenhuma fica sem matador: a M9 e a M6, que o pré-flight mediu em 0, ganham teste próprio; a **M16** e a **M17**, que fecham X3 e X6 por decisão do usuário, também; os dois mutantes equivalentes ficam declarados fora da tabela);
- a composição **`290 − 0 + 7 = 297`** reportada com os números reais medidos, junto com o **par por arquivo (`ComponentesPage.test.tsx` = 41, `useBuscaPaginada.test.tsx` = 18 intocado)**, e **zero** testes removidos — se você removeu algum, saiu do escopo;
- a **verificação pós-commit do Step 6** executada e reportada: `git status --short` limpo do que era desta task, `git show --stat HEAD` com os dois arquivos e nada mais, nenhum `.claude/settings*.json` no commit, varredura de segredo feita;
- suíte, `npm run build` e `npm run lint` verdes (o lint tem 1 warning alheio conhecido, `AuthContext.tsx:48`).

---

### Task 9B: Adoção do `useBuscaPaginada` na `ComponentesPage`

**Files:**
- Modify: `web/src/pages/ComponentesPage.tsx` (como a 9A a deixou)
- Modify: `web/src/pages/ComponentesPage.test.tsx` (**41 testes**, como a 9A a deixou)
- Modify: `web/src/hooks/useBuscaPaginada.test.tsx` (**18 testes hoje — MEDIDO**; ganha 1)

**Interfaces:**
- Consumes: `useBuscaPaginada` (Task 3), `ControlesDePaginacao` (Task 6) — as duas peças que a 9A deliberadamente não tocou; e tudo o que a 9A já consome.
- Produces: nada.

> **⚠️ Nesta task a âncora vinculante é o NOME DO TESTE, não o número da linha** (migração de
> 2026-08-14, decisão do usuário). Toda citação a `ComponentesPage.test.tsx` e a
> `useBuscaPaginada.test.tsx` identifica o teste pelo **nome**; onde ainda aparecer um número, ele
> vem na forma "(hoje `:47`)" e é **auxílio de navegação perecível** — linha do arquivo de HOJE
> (HEAD `73f721f`), **já errada quando esta task começa**, porque a 9A empurra tudo +11. Se o número
> não bater com o nome, **o nome ganha e o número está velho**; nunca o contrário.
>
> Origem: achado nº 9 do pré-flight da 9A, que ele mediu e não corrigiu por estar fora do escopo
> dele. Os números foram corrigidos em 2026-08-14 — **a instância** — e substituídos por nomes no
> mesmo dia — **o mecanismo**. Número de linha em plano vence toda vez que alguém insere uma linha
> acima, e esta fase já pagou por isso mais de uma vez.
>
> **Como localizar, e a prova de que dá:**
> `grep -n "<nome exato do teste>" web/src/pages/ComponentesPage.test.tsx`. Os **21** nomes que esta
> task cita foram conferidos contra os dois arquivos reais em 2026-08-14: cada um casa com
> **exatamente um** `it(`, nenhum é substring de outro, e cada arquivo tem um `describe` só (não há
> nome repetido em bloco irmão). **Um par que só o nome do arquivo separa:** `volta para a pagina 1
> quando a busca muda` (tela, **sem** acento) e `volta para a página 1 quando a busca muda` (hook,
> **com** acento) são testes **diferentes**; idem o par do filtro de inativos. **Cite sempre com o
> arquivo.**
>
> **E não é só a 9A que move linha:** o **Step 2 desta task** insere um teste em
> `useBuscaPaginada.test.tsx`, então os `hoje :N` daquele arquivo também ficam velhos **dentro da
> própria task**, a partir do ponto de inserção. Mais uma razão para o nome mandar.
>
> **O que continua citado por número, de propósito:** `ControlesDePaginacao.test.tsx` (`:36`, `:46`,
> `:56`) e os arquivos de código (`useBuscaPaginada.ts:106` e `:117`, `ControlesDePaginacao.tsx:19`
> e `:23`, `cadastros.ts:233-256`). **Nenhuma das tasks que restam (9A, 9B, 10, 11, 12) lista esses
> arquivos em `Files`** — conferido em 2026-08-14 —, então as linhas deles não se mexem nesta fase;
> e código não tem nome de teste para servir de âncora.
>
> **O deslocamento é +11, MEDIDO duas vezes e por dois caminhos:** o pré-flight da 9A leu
> `:36` → `:47` e `:772` → `:783` na árvore descartável em que executou a task; a aplicação das
> decisões do usuário remontou o arquivo do zero (import de `../testes/api`, bloco `let perfil` +
> `vi.mock`, e a linha `perfil = 'Administrador'` do `beforeEach`) e conferiu os **34** `it(` um a
> um — **os 34 deslocam +11, sem exceção**, e o arquivo vai de 987 para 998 linhas.
>
> **Os 7 testes que a 9A acrescenta NÃO mudam esse número**, porque entram no fim do `describe`,
> abaixo de tudo o que esta task cita (o teste citado que fica mais embaixo hoje é o `mostra Página
> 1 de 1 quando o total e zero`, em `:772`); e o `within` que o teste do X3
> exige é acrescentado à linha de import que já existe, não numa linha nova. É por isso que o Step 3
> da 9A exige as duas coisas de forma explícita.
>
> **Tabela de NAVEGAÇÃO, não de âncora** (ela nasceu para converter os números e sobrevive só como
> mapa: depois da migração acima, quem vincula é a coluna da esquerda). Serve para achar o teste
> rápido num editor e para reconhecer números velhos em relatórios antigos desta fase:
>
> | Teste (**a âncora**) | hoje | depois da 9A |
> |---|---|---|
> | `volta para a pagina 1 quando a busca muda` | `:47` | **`:58`** |
> | `atualiza a URL de busca mesmo ja estando na pagina 1` | `:75` | **`:86`** |
> | `volta para a pagina 1 quando o tamanho da pagina muda` | `:92` | **`:103`** |
> | `atualiza a URL de tamanho mesmo ja estando na pagina 1` | `:117` | **`:128`** |
> | `desabilita Anterior na primeira pagina e Proxima na ultima` | `:132` | **`:143`** |
> | `mostra o total e a contagem de paginas` | `:144` | **`:155`** |
> | `volta para a pagina 1 quando o filtro de inativos muda` | `:480` | **`:491`** |
> | `mantem o resultado da requisicao mais recente quando respostas chegam fora de ordem` | `:505` | **`:516`** |
> | `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` | `:554` | **`:565`** |
> | `mantem o indicador de carregando enquanto a requisicao mais recente ainda nao respondeu` | `:598` | **`:609`** |
> | `Anterior volta para a pagina anterior quando habilitado` | `:636` | **`:647`** |
> | `mostra Página 1 de 1 quando o total e zero` | `:772` | **`:783`** |
> | `limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso` | `:930` | **`:941`** |
>
> **Pior caso concreto de trabalhar pelo número, e é o motivo da migração:** depois da 9A, `:47`
> aponta para `lista os componentes que a API devolveu` — teste **diferente** do que esta task quer
> remover, e que **não pode sair**. O número não erra alto: erra apontando para um teste válido, o
> que faz a remoção errada parecer certa. **Por isso o nome vincula e o número é conveniência.**

**Esta é a única task da fase que REMOVE teste, e a única que subtrai na conta.** Leia o parágrafo de composição do Step 1 antes de apagar a primeira linha.

**Esta task apaga cerca de 60 linhas de lógica da tela** — a guarda de sequência (`sequenciaRef`), os três `mudar*` com `setPagina(1)`, o `carregar` com quatro parâmetros e o cálculo de `totalDePaginas` **vivem no hook agora**. Apagar sem substituir é o risco: confira que cada propriedade continua provada, ou pela suíte do hook, ou pela da tela. **Duas das três guardas de sequência estão provadas no hook; a terceira não está — Step 2 do teste, abaixo.**

> **Colisão registrada, não corrigida, pelo passe curto de primitivas pré-Task 8 (P9 do brief
> `.superpowers/sdd/fase1d-passe-primitivas-brief.md`) — é desta task.**
>
> `web/src/components/ControlesDePaginacao.tsx:19` faz `if (totalDePaginas <= 1) return null` —
> com uma página só, o componente não renderiza nada.
>
> **São DOIS testes atingidos pela COLISÃO DA PAGINAÇÃO, não um** (varredura completa dos 34,
> MEDIDA no pré-flight de 2026-08-12 e RE-MEDIDA na continuação de 2026-08-13, executando a suíte
> de hoje contra a tela nova. Cuidado com o escopo desta frase: estes são os dois que restam
> vermelhos **depois** de adaptar harness, seletores, mensagens e o seletor do riscado — e essas
> quatro adaptações são todas da **Task 9A**, já feitas quando esta task começa):
>
> - `desabilita Anterior na primeira pagina e Proxima na ultima`, em
>   `web/src/pages/ComponentesPage.test.tsx` (hoje `:132-141`; `:143-152` depois da 9A —
>   navegação, não âncora). Com `total: 1`/`tamanho: 20` → 1 página → `null`; ela busca "Anterior" via
>   `getByRole` e espera `disabled === true`. Falha MEDIDA: *Unable to find an accessible element
>   with the role "button" and name "Anterior"*.
> - `mostra Página 1 de 1 quando o total e zero`, em `web/src/pages/ComponentesPage.test.tsx`
>   (hoje `:772-781`; `:783-792` depois da 9A — navegação, não âncora).
>   Com `total: 0` → `useBuscaPaginada.ts:117` faz `Math.max(1, Math.ceil(0/20)) = 1` → `null`; o
>   texto `Página 1 de 1 — 0 no total` nunca renderiza. Falha MEDIDA: *Unable to find an element
>   with the text: Página 1 de 1 — 0 no total*.
>
> **E são só esses dois.** Os outros dois testes que tocam paginação sobrevivem porque têm mais de
> uma página: `mostra o total e a contagem de paginas` (hoje `:144`, total 41 → 3 páginas) e
> `Anterior volta para a pagina anterior quando habilitado` (hoje `:636`, total 100 → 5 páginas) — MEDIDOS verdes,
> e os dois são matadores da M10. **Não os toque.**
>
> Não é regressão desta task: é uma decisão de desenho tomada na Task 6 que os testes da tela nunca
> acompanharam. A propriedade "não aparece quando há uma página só" **já tem dono próprio**
> (`ControlesDePaginacao.test.tsx:56`), e o `disabled` dos dois botões também (`:36` e `:46`,
> MEDIDOS). **Decisão U3 do usuário, 2026-08-13:** `mostra Página 1 de 1 quando o total e zero`
> **sai** (MUDA DE DONO — a prova já está em `mantém pelo menos uma página quando não há nada`, de
> `useBuscaPaginada.test.tsx`, + `ControlesDePaginacao.test.tsx:56`), e `desabilita Anterior na
> primeira pagina e Proxima na ultima` **fica, adaptado** ao comportamento real da primitiva
> (nenhum botão quando há só uma página).

**O debounce entra aqui, e o custo de relógio é real.** MEDIDO no pré-flight: os seis testes que digitam na busca passam **sem timers falsos**, custando de 331 a 453 ms de relógio real cada (405 / 343 / 437 / 453 / 453 / 331 ms) em vez de ~20 ms, porque o `waitFor` tem timeout padrão de 1000 ms > 300 ms do debounce. **Cinco desses seis saem nesta task** — `volta para a pagina 1 quando a busca muda`, `atualiza a URL de busca mesmo ja estando na pagina 1`, `mantem o resultado da requisicao mais recente quando respostas chegam fora de ordem`, `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` e `mantem o indicador de carregando enquanto a requisicao mais recente ainda nao respondeu` (hoje `:47`, `:75`, `:505`, `:554`, `:598`); sobra `limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso` (hoje `:930`), que continua passando com relógio real. **Consequência a não perder, e é o D14 do pré-flight: nada prova que o debounce está LIGADO na tela** — MEDIDO, passar `atrasoDoDebounce: 0` deixava a suíte inteira verde, **0 mortes**. O Step 3 fecha isso com um teste.

**Sobre `listarComponentes` (D11, imprecisão de texto do plano original):** a assinatura real é `(f: FiltroDeComponentes) => Promise<PaginaDe<ComponenteDto>>` (`cadastros.ts:233-256`), e a do hook é `(FiltroDeBusca) => Promise<PaginaDeBusca<T>>` — **dois pares de tipos nomeados diferentes, estruturalmente compatíveis**. Compila (MEDIDO, `npm run build` verde com o bloco do Step 2 instalado). Não é erro; está registrado porque "exatamente" é palavra que convida a não conferir.

- [ ] **Step 1: Confirmar a baseline e fechar a conta ANTES de apagar**

```bash
cd web && npm test -- ComponentesPage && npm test -- useBuscaPaginada && npm test
```

Expected: **41** no arquivo da tela, **18** no do hook, e **297** na suíte.

> **⚠️ A baseline desta task é DERIVADA, não MEDIDA.** `297 = 290 (medido em 2026-08-13) + 7 (delta
> da 9A)`, e dentro desses 7 só **5** foram executados alguma vez (pelo pré-flight da 9A, que fechou
> em `295`); os outros 2 são os testes de X3 e X6, decididos pelo usuário em 2026-08-14 e nunca
> rodados. A 9A ainda não rodou de verdade quando este texto foi escrito. Vale a regra operacional
> do topo do plano: (a) meça no Step 1; (b) **se não bater com o total que a 9A reportou ao fechar,
> pare e reporte** — divergência na baseline é sinal real; (c) se bater, o que vincula é o
> **delta**; (d) se o delta estiver certo e o total divergir, reporte o número real e siga. **Nunca
> invente nem apague teste para fechar conta.**

**A conta desta task, com a composição aberta:**

```
297 (DERIVADO da 9A)  −  8 removidos  +  3 novos  =  292 (DERIVADO)
```

> **A colisão `290 == 290` ACABOU — e o registro dela fica, porque a razão importa.** Até
> 2026-08-14 esta task fechava em **290**, *exatamente* a baseline com que a 9A começava: quem
> rodasse `npm test` num checkout sem a 9A aplicada via `290 passed` e não tinha como distinguir
> isso de sucesso — a mesma armadilha do `200` do Step 7 da Task 6, que era a baseline real do dia.
> **O fechamento de X3 e X6 (+2 na 9A) desfez a coincidência:** a fase agora anda por **290 → 297 →
> 292**, três números distintos, e um total inesperado volta a ser sinal.
>
> **Isso não dispensa a prova por arquivo, e ela continua exigida:** o total é derivado e apodrece a
> cada fix pass (foi o que aconteceu com esta task três vezes), enquanto o par por arquivo é
> medição direta do que ESTA task fez. **`ComponentesPage.test.tsx` fecha em 35 e
> `useBuscaPaginada.test.tsx` em 19.** Reporte os dois, sempre — não porque o total é ambíguo, mas
> porque o total sozinho nunca provou de onde veio.

**Os 8 que saem — DECISÃO U3 do usuário (X = 7) mais o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` da decisão U2.** MEDIDO e vinculante: **nenhum deles precisa sair**; com adaptação, 34/34 ficam verdes. Toda saída aqui é **de-duplicação voluntária de prova que já tem dono medido em outro arquivo** — não apague nenhum outro para fechar conta:

**A âncora de cada linha é o NOME, nas duas colunas** — o que sai é o `it(` com aquele nome exato em `ComponentesPage.test.tsx`, e o novo dono é o `it(` com aquele nome exato em `useBuscaPaginada.test.tsx`. Os `hoje :N` são navegação: os da esquerda somam +11 depois da 9A, e os da direita andam assim que o Step 2 insere o teste novo no arquivo do hook.

| # | Teste que sai de `ComponentesPage.test.tsx` | Novo dono em `useBuscaPaginada.test.tsx`, MEDIDO | Mutação que prova o novo dono |
|---|---|---|---|
| 1 | `volta para a pagina 1 quando a busca muda` (hoje `:47`) | `volta para a página 1 quando a busca muda` (hoje `:144`) — **note o acento: é o teste do hook, não este** | tirar `setPagina(1)` do efeito de debounce → 1 morte |
| 2 | `atualiza a URL de busca mesmo ja estando na pagina 1` (hoje `:75`) | idem | idem |
| 3 | `volta para a pagina 1 quando o tamanho da pagina muda` (hoje `:92`) | `volta para a página 1 quando o tamanho de página muda` (hoje `:174`) | tirar `setPagina(1)` de `mudarTamanho` → 1 morte |
| 4 | `volta para a pagina 1 quando o filtro de inativos muda` (hoje `:480`) | `volta para a página 1 quando o filtro de inativos muda` (hoje `:159`) | tirar `setPagina(1)` de `mudarInativos` → 1 morte |
| 5 | `mantem o resultado da requisicao mais recente quando respostas chegam fora de ordem` (hoje `:505`) | `ignora a resposta de uma busca que já foi superada por outra` (hoje `:102`) | tirar a guarda antes de `setItens` → **2** mortes |
| 6 | `mantem o indicador de carregando enquanto a requisicao mais recente ainda nao respondeu` (hoje `:598`) | `mantém "carregando" quando a resposta obsoleta chega com outra ainda em voo` (hoje `:248`) | tirar `setCarregando(true)` de `carregar` (W3) → 2 mortes |
| 7 | `mostra Página 1 de 1 quando o total e zero` (hoje `:772`) | `mantém pelo menos uma página quando não há nada` (hoje `:318`) + `ControlesDePaginacao.test.tsx:56` | `Math.max(1, …)` → `Math.ceil(…)` → 5 mortes |
| 8 | `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` (hoje `:554`) | **NÃO TEM DONO AINDA** — ver o Step 2 | tirar a guarda antes de `setErro(e)` → **0 mortes no hook** |

**O nº 8 é condicional e a ordem não é negociável (DECISÃO U2 do usuário, 2026-08-13): ele só sai DEPOIS de o hook ganhar o teste equivalente e de você ter visto esse teste ficar VERMELHO sob a mutação.** Se você apagar `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` antes disso, a suíte fica verde e o projeto perde a única prova que tem da terceira guarda de sequência — o D5 do pré-flight, o pior defeito possível segundo o próprio brief da task.

**Os que FICAM e por quê** (a lista é tão vinculante quanto a de saída; a âncora é o nome, e o `hoje :N → :N depois da 9A` é só navegação):

- `atualiza a URL de tamanho mesmo ja estando na pagina 1` (hoje `:117` → `:128`) — **único ancoradouro do seletor de tamanho na tela**. É ele, e só ele, que mata a M14 (D8 do pré-flight). Se sair, o `<select>` "Por página" pode ficar inerte sem que nada quebre.
- `mostra o total e a contagem de paginas` (hoje `:144` → `:155`) — matador da M10. MEDIDO: dos 5 matadores da M10, 4 estavam na lista de remoção do plano original; este é um dos dois que restam.
- `Anterior volta para a pagina anterior quando habilitado` (hoje `:636` → `:647`) — o outro matador da M10.
- `desabilita Anterior na primeira pagina e Proxima na ultima` (hoje `:132` → `:143`) — **fica adaptado**, não sai.
- **Os 7 testes que a 9A acrescentou também ficam, todos** — inclusive os dois que fecham X3 e X6 (`mostra o tipo de cada componente na lista` e `dá à tela o título "Componentes" no h1`), que continuam valendo porque o Step 3 desta task mantém `<Pilula>{c.tipo}</Pilula>` e `<Pagina titulo="Componentes">`. Só um deles é **reescrito** aqui: o `distingue "nada corresponde à busca"…`, que passa a precisar de timers falsos (classe (f), Step 4).

- [ ] **Step 2: Dar dono ao que ainda não tem — o teste da guarda do `catch` no hook**

**Faça este Step ANTES de tocar na tela.** Ele é o pré-requisito da decisão U2.

> **⚠️ CORREÇÃO MEDIDA (continuação do pré-flight, 2026-08-13).** Cada propriedade da lista "mudou
> de dono" foi verificada por mutação **contra `useBuscaPaginada.test.tsx` de verdade** (18 testes):
>
> | Propriedade | Mutação no hook | Mortes na suíte do hook | Dono real |
> |---|---|---|---|
> | reset de página na busca | tirar `setPagina(1)` do efeito de debounce | **1** | hook ✅ |
> | reset de página em inativos | tirar `setPagina(1)` de `mudarInativos` | **1** | hook ✅ |
> | reset de página no tamanho | tirar `setPagina(1)` de `mudarTamanho` | **1** | hook ✅ |
> | guarda de sequência — ramo de **sucesso** | tirar a guarda antes de `setItens` | **2** | hook ✅ |
> | guarda de sequência — **`finally`** | trocar por `setCarregando(false)` nu | **1** | hook ✅ |
> | guarda de sequência — **`catch`** | tirar a guarda antes de `setErro(e)` | **0 — SOBREVIVE** | ❌ **só a tela** |
> | clamp | apagar o `useEffect` do clamp | **1** | hook ✅ |
> | `totalDePaginas` | `Math.max(1, …)` → `Math.ceil(…)` | **5** | hook ✅ |
> | W3 | tirar `setCarregando(true)` de `carregar` | **2** | hook ✅ |
>
> **"Guarda de sequência da corrida" são TRÊS guardas, e o hook só prova DUAS.** A do `catch`
> (`useBuscaPaginada.ts:106`) não tem dono no hook: apagá-la deixa os 18 testes do hook **verdes**,
> e o único matador do projeto inteiro é `nao mostra erro de uma requisicao desatualizada que falha
> depois de uma mais recente ter sucesso`, em `ComponentesPage.test.tsx` (hoje `:554`).

Acrescente em `web/src/hooks/useBuscaPaginada.test.tsx`, no molde do vizinho `ignora a resposta de uma busca que já foi superada por outra` (hoje `:102`) — mesmo `Hospedeiro`, mesmo `avancar`, mesma técnica de resposta atrasada:

```tsx
it('não mostra erro de uma busca superada que FALHA depois de a mais recente ter sucesso', async () => {
  // Fecha o D5 do pré-flight de 2026-08-13. Das TRÊS guardas de sequência do hook, a do `catch`
  // (`useBuscaPaginada.ts:106`) era a única sem dono aqui: apagá-la deixava estes 18 testes verdes,
  // e o único matador do projeto era o `nao mostra erro de uma requisicao desatualizada que falha
  // depois de uma mais recente ter sucesso`, da `ComponentesPage.test.tsx` — prova de mecanismo do
  // hook morando na suíte de uma tela. Este teste é o pré-requisito para aquele sair (decisão U2).
  //
  // Simétrico ao `ignora a resposta de uma busca que já foi superada por outra`, deste mesmo
  // arquivo, mas pelo ramo de ERRO: lá a resposta obsoleta era um sucesso que não pode
  // sobrescrever a lista; aqui é uma FALHA que não pode acender o erro de uma consulta que o
  // usuário já abandonou.
  const ordemDeResposta: string[] = []
  const buscar = vi.fn((f: FiltroDeBusca) => {
    if (f.busca === 'SU') {
      return new Promise<PaginaDeBusca<Item>>((_resolve, reject) => {
        setTimeout(() => { ordemDeResposta.push('SU'); reject(new Error('rede caiu')) }, 1000)
      })
    }
    ordemDeResposta.push(f.busca)
    return Promise.resolve(pagina([{ id: 2, nome: 'RESULTADO DE SUP' }]))
  })

  render(<Hospedeiro buscar={buscar} />)
  await avancar(0)

  const campo = screen.getByLabelText('busca')
  fireEvent.change(campo, { target: { value: 'SU' } })
  await avancar(300)   // dispara "SU", que só REJEITA em +1000 ms
  fireEvent.change(campo, { target: { value: 'SUP' } })
  await avancar(300)   // dispara "SUP", que responde na hora
  await avancar(2000)  // deixa a rejeição atrasada de "SU" enfim chegar

  // `ordemDeResposta` prova que a falha obsoleta REALMENTE chegou, e por último — sem isso,
  // "não tem erro na tela" passaria igual num teste em que ela nunca chegou.
  expect(ordemDeResposta).toEqual(['', 'SUP', 'SU'])
  expect(screen.getByText('RESULTADO DE SUP')).toBeTruthy()
  expect(screen.queryByText('erro')).toBeNull()
})
```

**Prove que ele vale antes de seguir:** apague a guarda de sequência do `catch` em `useBuscaPaginada.ts:106` e confirme que **este teste fica vermelho** (a mutação M15 do Step 5). Reverta por **edição inversa**, nunca `git checkout` — a árvore tem sujeira alheia permanente. Só depois de ver o vermelho é que o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso`, da `ComponentesPage.test.tsx`, pode sair.

- [ ] **Step 3: Trocar o motor da `ComponentesPage`**

`web/src/pages/ComponentesPage.tsx` — este é o bloco que o pré-flight **instalou e compilou** (`npm run build` → verde, `✓ built in 288ms`, MEDIDO em 2026-08-13), com a **única** alteração da decisão U1 na condição do `EstadoVazio`. Ele é o alvo final da Task 9 inteira (9A + 9B):

```tsx
import { useState, type FormEvent } from 'react'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { useBuscaPaginada } from '../hooks/useBuscaPaginada'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { ControlesDePaginacao } from '../components/ControlesDePaginacao'

const FORMULARIO_VAZIO: NovoComponente = { codigo: '', descricao: '', tipo: 'Fabricado' }

/** As três opções de `CK_Componente_Tipo`. Lista fechada, ao contrário de `unidadeMedida`. */
const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/** Dentro do teto de 100 do backend, de propósito: um valor acima viraria 400. */
const TAMANHOS = [20, 50, 100]

export function ComponentesPage() {
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('componentes')

  // `listarComponentes` é passada direto por ser estável (função de módulo) e por a assinatura
  // dela ser estruturalmente compatível com a que o hook pede — `FiltroDeComponentes`/`PaginaDe<T>`
  // de um lado, `FiltroDeBusca`/`PaginaDeBusca<T>` do outro: nomes diferentes, mesma forma
  // (compila, MEDIDO). Nada de lambda inline aqui: o hook a guarda num ref justamente para tolerar
  // isso, mas passar a estável é mais claro.
  const lista = useBuscaPaginada<ComponenteDto>({ buscar: listarComponentes })

  // Dois erros, e não um: o de LEITURA vem do hook e é apagado pela recarga seguinte; o de
  // ESCRITA (conflito de código, 403) tem de sobreviver à recarga que o próprio salvar dispara.
  // Um estado só faria a mensagem de duplicidade piscar e sumir — o defeito que a review da Task 11
  // da Fase 1A chamou de "erro que pisca". É por causa DESTA divisão que a 9A podia viver com um
  // `erro` único e a 9B não pode.
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os componentes.')

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarComponente(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Componente_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErroDeEscrita(`Já existe um componente com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um componente com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o componente.'))
    } finally {
      setEnviando(false)
    }
  }

  // O 403 do backend é a fronteira real de perfil (F2): esconder o botão é conveniência, e o
  // try/catch é o que faz a tela dizer alguma coisa quando ele chega assim mesmo.
  async function alternarAtivo(componente: ComponenteDto) {
    try {
      await definirAtivoComponente(componente.id, !componente.ativo)
      setErroDeEscrita(null)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível alterar o componente.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErroDeEscrita(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o componente.'))
    }
  }

  const buscando = lista.textoDaBusca.trim() !== ''

  return (
    <Pagina titulo="Componentes">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            {/* Lista fechada (CK_Componente_Tipo): select, não input livre. */}
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
                  className={CLASSES_DE_CONTROLE}
                >
                  {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erroDeEscrita ?? erroDeLeitura} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      {/*
        A barra de filtros é o que não cabia em 448px (spec §7). Em `max-w-3xl` os três controles
        cabem lado a lado a partir de `sm`, e empilham no celular sem rolagem horizontal.
      */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1">
          <Campo rotulo="Buscar por código ou descrição">
            {(id) => (
              <input
                id={id}
                value={lista.textoDaBusca}
                onChange={(e) => lista.mudarBusca(e.target.value)}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-tinta-fraca sm:pb-2.5">
          <input
            type="checkbox"
            checked={lista.incluirInativos}
            onChange={(e) => lista.mudarInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <Campo rotulo="Por página">
          {(id) => (
            <select
              id={id}
              value={lista.tamanho}
              onChange={(e) => lista.mudarTamanho(Number(e.target.value))}
              className={CLASSES_DE_CONTROLE}
            >
              {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          )}
        </Campo>
      </div>

      {lista.carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erroDeLeitura === null && lista.itens.length === 0 ? (
        // DECISÃO U1 (usuário, 2026-08-13), e ela CORRIGE o que a versão anterior deste bloco
        // dizia. Sem o `erroDeLeitura === null &&`, a tela mostra o banner de erro E "Nenhum
        // componente cadastrado" ao mesmo tempo sob GET 500 — MEDIDO por sonda no pré-flight —,
        // afirmando "não há componentes" a partir de uma falha de rede. É a mesma forma do
        // Critical que o fix pass da Task 8 pagou (`SetoresPage.tsx:129`). Usa-se o derivado
        // `erroDeLeitura`, e não `lista.erro`, porque ele é `null` exatamente quando `lista.erro`
        // é, e lê melhor ao lado do `BannerDeErro` logo acima.
        //
        // Os três vazios que a spec §9 manda distinguir: busca sem resultado, catálogo vazio e —
        // acima, no banner — erro de rede. Antes os três renderizavam a mesma lista muda.
        <EstadoVazio
          titulo={buscando ? 'Nenhum componente encontrado' : 'Nenhum componente cadastrado'}
          descricao={
            buscando
              ? `Nada corresponde a "${lista.textoDaBusca}".`
              : podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined
          }
        />
      ) : (
        <ListaDeCadastro>
          {lista.itens.map((c) => (
            <ItemDeCadastro
              key={c.id}
              ativo={c.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(c)}>
                  {c.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{c.codigo}</span>
              {' — '}
              {c.descricao}
              {' '}
              <Pilula>{c.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      <ControlesDePaginacao
        pagina={lista.pagina}
        totalDePaginas={lista.totalDePaginas}
        total={lista.total}
        aoMudarPagina={lista.irParaPagina}
      />
    </Pagina>
  )
}
```

- [ ] **Step 4: Adaptar a suíte da tela, remover os 8 e acrescentar os 3**

Três classes de mudança nesta task — (b′) paginação, (c) mudança de dono, (f) debounce nos testes que digitam na busca. As quatro classes de adaptação de markup ((a) harness, (b) seletores, (d) mensagens, (e) markup do item) **já foram feitas na 9A**.

**(b′) Paginação.** Adapte `desabilita Anterior na primeira pagina e Proxima na ultima` ao comportamento real de `ControlesDePaginacao` (nenhum botão — nenhuma paginação — quando há só uma página). Não toque em `mostra o total e a contagem de paginas` nem em `Anterior volta para a pagina anterior quando habilitado`.

**(c) Mudança de dono.** Remova os 8 da tabela do Step 1, **nessa ordem, e o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso` só depois do Step 2**. **O que NÃO sai:** tudo que prova a integração da tela — corpo do POST, ramo 201, conflito com e sem inativo, alternar ativo, reativar, a montagem da URL, os dois de perfil, a sonda do estado vazio, `atualiza a URL de tamanho mesmo ja estando na pagina 1`, `mostra o total e a contagem de paginas`, `Anterior volta para a pagina anterior quando habilitado` e `desabilita Anterior na primeira pagina e Proxima na ultima` adaptado.

**(f) Debounce.** Agora a busca é debounced, então **todo teste que digita na busca** precisa de timers falsos e do helper de avanço:

```tsx
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}
```

**Use timers falsos só nos testes que tocam a busca.** Ligá-los no arquivo inteiro obrigaria a reescrever os ~30 que não têm nada com debounce, e o `findBy*` deles passaria a depender de avanço manual. `vi.useFakeTimers()` dentro do `it`, e **`vi.useRealTimers()` no `afterEach` que já existe** (`afterEach(() => { vi.unstubAllGlobals() })`) — senão os timers falsos vazam para o teste seguinte, do mesmo jeito que o `perfil` vazava (defeito MEDIDO no pré-flight).

**Quem digita na busca depois das remoções:** `limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso` (hoje `:930` → `:941`) e o teste `distingue "nada corresponde à busca"…` que a 9A acrescentou. **Os outros seis testes novos da 9A não tocam na busca** — não precisam de timers falsos. MEDIDO: o `limpa a mensagem de erro da carga inicial…` passa **sem** timers falsos, custando ~330–450 ms de relógio real; o da 9A foi escrito sem timers e **precisa ser reescrito aqui** com `vi.useFakeTimers()` + `avancar(300)`, no molde que a Task 9 unificada já trazia:

```tsx
  fireEvent.change(screen.getByLabelText('Buscar por código ou descrição'), { target: { value: 'XPTO' } })
  await avancar(300)
```

**Acrescente os três testes.**

```tsx
it('agrupa as teclas da busca numa requisição só — o debounce está LIGADO na tela', async () => {
  // Fecha o D14 do pré-flight: MEDIDO, `useBuscaPaginada({ buscar, atrasoDoDebounce: 0 })` — o
  // debounce efetivamente desligado — deixava a suíte inteira verde, 0 mortes. O debounce é metade
  // do título desta task e não tinha prova nenhuma no nível da tela. Molde: o
  // `faz UMA requisição para três teclas digitadas em sequência`, de `useBuscaPaginada.test.tsx`,
  // aplicado à tela em vez de ao Hospedeiro.
  vi.useFakeTimers()
  const fetchMock = fetchPorRota({
    '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
  })
  vi.stubGlobal('fetch', fetchMock)

  render(<MemoryRouter><ComponentesPage /></MemoryRouter>)
  await avancar(0)
  expect(fetchMock).toHaveBeenCalledTimes(1)   // só a carga da montagem

  const campo = screen.getByLabelText('Buscar por código ou descrição')
  fireEvent.change(campo, { target: { value: 'S' } })
  fireEvent.change(campo, { target: { value: 'SU' } })
  fireEvent.change(campo, { target: { value: 'SUP' } })

  await avancar(299)
  expect(fetchMock).toHaveBeenCalledTimes(1)   // o debounce ainda não venceu

  await avancar(1)
  expect(fetchMock).toHaveBeenCalledTimes(2)   // UMA requisição para as TRÊS teclas
})

it('mantém a mensagem de código duplicado depois da recarga da lista', async () => {
  // O defeito mais provável desta task — o `erroDeEscrita ?? erroDeLeitura` existe para evitá-lo.
  //
  // O condicional "se não houver teste cobrindo" do plano original está DECIDIDO (pré-flight de
  // 2026-08-13): não há. MEDIDO: nenhum dos 34 dispara recarga depois de um erro de escrita ficar
  // na tela — o ramo de conflito de `salvar` faz `return` ANTES do `await lista.recarregar()`.
  //
  // E o esboço que o plano original trazia NÃO provava o que o título diz: ele nunca disparava
  // recarga nenhuma, provava só a PRECEDÊNCIA de `erroDeEscrita` sobre `erroDeLeitura` (já morta 9
  // vezes pela M5), e o mock devolvia o corpo de conflito também no GET da carga inicial, que não
  // é cenário real. Para provar o que o Step promete, o teste tem de, DEPOIS de a mensagem de
  // conflito aparecer, disparar uma recarga de verdade — marcar "Mostrar inativos", ou mudar a
  // busca e avançar os 300 ms — e só então afirmar que a mensagem continua lá.
  //
  // Use `fetchPorRota` com respostas DIFERENTES por método: 409 no POST, 200 no GET.
  // …preenche e submete…
  expect(await screen.findByText('Já existe um componente com este código.')).toBeTruthy()
  // …dispara a recarga (checkbox "Mostrar inativos")…
  expect(screen.getByText('Já existe um componente com este código.')).toBeTruthy()
})
```

E o terceiro é o do **Step 2**, em `useBuscaPaginada.test.tsx` — ele conta na suíte inteira, mesmo não estando neste arquivo. **É essa a razão de a conta desta task não fechar olhando só a tela.**

- [ ] **Step 5: Rodar tudo e medir as mutações**

```bash
cd web && npm test -- ComponentesPage && npm test -- useBuscaPaginada && npm test && npm run build && npm run lint
```

Expected: **35** no arquivo da tela, **19** no do hook, **292** na suíte. O `292` não colide mais com nenhuma baseline desta fase (290 → 297 → 292 — ver o Step 1), mas **reporte o par por arquivo assim mesmo**: é ele que prova o que ESTA task fez.

| # | Mutação | Onde | Mortes esperadas | Matador |
|---|---|---|---|---|
| M4′ | remover `await lista.recarregar()` do ramo de sucesso de `salvar` | tela | ≥ 1 (MEDIDO: 2) | **nomeie ao medir** — o call site mudou de `carregar(…)` para `lista.recarregar()`, por isso remede |
| M5 | trocar `erroDeEscrita ?? erroDeLeitura` por só `erroDeLeitura` | tela | ≥ 1 (MEDIDO: 9) | mata pelas mensagens de escrita, **não** pela "sobrevivência à recarga" que o teste novo promete — não confunda as duas |
| M6′ | trocar `buscando` por `false` (agora sobre `lista.textoDaBusca`) | tela | ≥ 1 | `distingue "nada corresponde à busca"…` |
| M10 | remover o `<ControlesDePaginacao>` | tela | ≥ 1 (MEDIDO: 5 antes das remoções) | `mostra o total e a contagem de paginas` **e** `Anterior volta para a pagina anterior quando habilitado` (hoje `:144` e `:636`) — os dois ficam de propósito |
| M13 | **NOVA** — `useBuscaPaginada({ buscar: listarComponentes, atrasoDoDebounce: 0 })` | tela | ≥ 1 (fecha o **D14**; MEDIDO em 0 antes) | `agrupa as teclas da busca numa requisição só…` |
| M14 | **NOVA** — `onChange={() => {}}` no `<select>` "Por página" | tela | ≥ 1 (fecha o **D8**; MEDIDO em 0 quando este teste saía) | `atualiza a URL de tamanho mesmo ja estando na pagina 1` (hoje `:117`) — **é por isto que ele fica** |
| M15 | **NOVA** — tirar a guarda de sequência antes de `setErro(e)` (`useBuscaPaginada.ts:106`) | **hook** | ≥ 1 (fecha o **D5**; MEDIDO em 0 no hook antes) | o teste do Step 2 |

> **Regressão obrigatória, e não é zelo:** esta suíte de tela perdeu 8 testes. Rode de novo, contra
> a suíte de 35, as mutações que a **9A** fechou e cujo código esta task **não** alterou — **M1,
> M2, M3, M7, M8, M9, M12, M16 e M17** — e confirme que cada uma continua com **≥ 1 morte**. MEDIDO
> no pré-flight, nenhum dos 8 removidos é matador delas; **confirme, não presuma** — foi exatamente
> assim que a M10 caiu de 5 mortes para 1 sem ninguém perceber (D9).
>
> **M16 e M17 entraram nesta lista em 2026-08-14, com o fechamento de X3 e X6 na 9A.** Elas mutam
> código que o Step 3 desta task **reescreve mas preserva** — `<Pilula>{c.tipo}</Pilula>` e
> `<Pagina titulo="Componentes">` continuam lá —, e os dois matadores delas não estão entre os 8
> removidos. É por isso que são re-verificação e não mutação nova: se alguma das duas voltar a zero
> aqui, a reescrita da tela perdeu conteúdo visível em silêncio, que é exatamente o que o X3 e o X6
> descreviam.
>
> **Mutantes equivalentes, registrados e NÃO listados:** `usePodeEscrever('pedidos')` e
> `('agrupamentos')` — listas de perfis idênticas às de `componentes`, nenhum perfil as separa
> (MEDIDO: 0 mortes, e nenhum teste possível). Só `('setores')` é distinguível, e é a M9 da 9A.

- [ ] **Step 6: Commit**

```bash
git add web/src/pages/ComponentesPage.tsx web/src/pages/ComponentesPage.test.tsx web/src/hooks/useBuscaPaginada.test.tsx
git commit -m "feat(web): ComponentesPage sobre useBuscaPaginada e ControlesDePaginacao"
```

**Verificação pós-commit — obrigatória (mesma sub-etapa do Step 6 da 9A; política do usuário de 2026-08-14: o commit é nosso, o PR é dele):**

1. **`git status --short` depois do commit** — nada de `M`/`??` que devesse ter entrado. **Aqui o risco é maior que na 9A**: são três arquivos, e o do hook (`useBuscaPaginada.test.tsx`) é o mais fácil de esquecer no `git add` — sem ele o commit fica com a tela nova e sem o teste do Step 2, que é o pré-requisito da decisão U2.
2. **`git show --stat HEAD`** — os **três** arquivos, e nada mais.
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — sujeira alheia permanente: não commite, não reverta, não edite.
4. **O repositório é PÚBLICO**: varredura de segredo antes do push, passo obrigatório.

**Abrir ou mesclar PR não é nosso** — aprovação manual do usuário.

**Definition of done da Task 9B:**

- `grep -c "sequenciaRef" src/pages/ComponentesPage.tsx` = **0** — a guarda vive no hook agora;
- `grep -c "useBuscaPaginada" src/pages/ComponentesPage.tsx` ≥ 1 e `grep -c "ControlesDePaginacao" src/pages/ComponentesPage.tsx` ≥ 1;
- as **7 mutações** do Step 5 medidas com **≥ 1 morte cada**, com o matador nomeado, **mais** a re-verificação das **9** herdadas da 9A (M1, M2, M3, M7, M8, M9, M12, **M16 e M17**) — **nenhuma mutação de nenhuma das duas tasks fica sem matador**, e os dois mutantes equivalentes ficam declarados fora das tabelas;
- **a lista dos 8 testes removidos, um a um, com o nome e o motivo, no relatório** — e a confirmação explícita de que **nenhuma remoção era necessária** (34/34 ficavam verdes só com adaptação): toda saída aqui é de-duplicação decidida pelo usuário em U3/U2;
- **a prova de ordem da decisão U2 declarada no relatório**: o teste do hook existia e ficou **vermelho** sob a M15 **antes** de o `nao mostra erro de uma requisicao desatualizada que falha depois de uma mais recente ter sucesso`, da `ComponentesPage.test.tsx`, sair;
- **a composição `297 − 8 + 3 = 292` reportada com os números reais**, junto com o **par por arquivo (35 / 19)** — o total já não colide com baseline nenhuma (290 → 297 → 292), mas o par por arquivo continua sendo a prova direta do que esta task fez;
- a **verificação pós-commit do Step 6** executada e reportada: `git status --short`, `git show --stat HEAD` com os **três** arquivos, nenhum `.claude/settings*.json` no commit, varredura de segredo feita;
- suíte, `npm run build` e `npm run lint` verdes.

**Nota honesta, escrita ANTES da entrega:** a DoD da Task 9 unificada era inatingível em dois pontos independentes — "as 11 mutações com ≥ 1 morte" com a M9 medida em 0, e "suíte verde" sem contagem alguma a bater. As duas tasks acima fecham os dois, e fecham também as três lacunas que nenhuma mutação tocava (M9, debounce, seletor de tamanho) e a guarda de sequência sem dono. **O que continua em aberto e NÃO é destas tasks:** o clamp inferior de `irParaPagina` e a validação de `tamanho` em `mudarTamanho` (inalcançáveis pela UI — `ControlesDePaginacao.tsx:23` desabilita "Anterior" com `pagina <= 1`, e `TAMANHOS` é um `<select>` fechado; **vão para a review de branch**); o `recarregar` com filtros capturados (a `ComponentesPage` é a única tela da fase que o expõe, não é regressão — a tela de hoje tem o mesmo defeito —, e adiar é defensável: **decisão do usuário**); e a dívida **m6** (overlay `after:inset-0` × `acao`), cujo dono nomeado era "a Task 9" e **MEDIDO não é**: o único `after:inset-0` do projeto está em `PedidosPage.tsx:131`, num `ItemDeCadastro` **sem** `acao`, e nem a 9A, nem a 9B, nem a Task 10 combinam os dois — **nenhuma task desta fase combina**. Reatribuí-la (à primitiva `ListaDeCadastro` ou à review de branch) é **decisão do usuário**, registrada em `.superpowers/sdd/fase1d-task-9-preflight.md` §7.3.

---

### Task 10: Re-layout do `PedidoDetalhePage`

**Files:**
- Modify: `web/src/pages/PedidoDetalhePage.tsx`
- Modify: `web/src/pages/PedidoDetalhePage.test.tsx`

**Interfaces:**
- Consumes: todas as primitivas, `usePodeEscrever('agrupamentos')`, `mensagemDeErro`.
- Produces: nada.

**A segunda tela que a spec §7 marca como re-layout:** detalhe do pedido, formulário, modal e lista de agrupamentos disputam a mesma coluna estreita.

**Três decisões da tela atual que NÃO se desfazem** (estão comentadas no código de hoje e cada uma custou um defeito):

1. **Não existe early return de página inteira.** `carregar()` roda após cada cadastro e exclusão; um `if (carregando) return <p>…</p>` demoliria a tela toda a cada ação — é isso que se sente como "lentidão", não a rede (o hop do proxy do Vite foi medido em ~5–15 ms). Pior: o early return ficava **antes** do bloco de erro, e a mensagem de recusa da exclusão era escrita e imediatamente escondida — o "erro que pisca" da review da Task 11.
2. **No modal, "Excluir" vem antes de "Cancelar" no DOM, e "Cancelar" tem mais peso visual.** Esta é a única exclusão física do sistema. Dois botões idênticos trocam a pausa deliberada por um sorteio.
3. **`autoFocus` no "Cancelar".** Mesma intenção, aplicada ao teclado: sem foco explícito, quem navega por teclado sem ler tabularia direto para o botão destrutivo.

**O que muda no modal:** ele ganha as primitivas (`Botao variante="perigo"` / `variante="secundario"`), o fundo e a borda dos tokens. **Não** ganha Esc, clique-fora nem focus-trap — segue fora de escopo, como já estava.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test -- PedidoDetalhePage
```

Expected: `Tests  4 passed (4)`.

- [ ] **Step 2: Reescrever a tela**

`web/src/pages/PedidoDetalhePage.tsx`:

```tsx
import { useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  obterPedido, listarAgrupamentos, criarAgrupamento, excluirAgrupamento, ehConflito,
  formatarDataHora, type PedidoDto, type AgrupamentoDto, type NovoAgrupamento,
  type ResultadoExclusao,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

const FORMULARIO_VAZIO: NovoAgrupamento = { codigo: '', tipo: 'Kit' }

// Tipado contra a união, e não `Record<string, string>`: com o tipo frouxo, renomear ou perder uma
// chave compila, passa os testes e passa o lint — e em runtime `MOTIVO_DA_RECUSA[desfecho]` vira
// undefined e a tela fica MUDA no caso que mais acontece. O tipo forte faz o tsc cobrar o mapa.
const MOTIVO_DA_RECUSA: Record<Exclude<ResultadoExclusao, 'ok'>, string> = {
  AgrupamentoNaoVazio: 'Este agrupamento já tem estrutura e não pode mais ser excluído.',
  PedidoNaoAberto: 'O pedido não está mais aberto: não dá para excluir agrupamentos dele.',
  NaoEncontrado: 'Este agrupamento já não existe mais.',
}

export function PedidoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const pedidoId = Number(id)

  const [pedido, setPedido] = useState<PedidoDto | null>(null)
  const [agrupamentos, setAgrupamentos] = useState<AgrupamentoDto[]>([])
  const [form, setForm] = useState<NovoAgrupamento>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)
  const [pendenteExclusao, setPendenteExclusao] = useState<AgrupamentoDto | null>(null)

  const podeEscrever = usePodeEscrever('agrupamentos')

  // Recebe o id como argumento (em vez de fechar sobre `pedidoId` de fora) porque a dependência do
  // useEffect precisa aparecer usada dentro do corpo do callback, senão o exhaustive-deps acusa
  // 'carregar' como dependência faltando.
  async function carregar(id: number) {
    setCarregando(true)
    try {
      // Duas chamadas de propósito: o Pedido e o sub-recurso de Agrupamentos são rotas separadas.
      const [p, a] = await Promise.all([obterPedido(id), listarAgrupamentos(id)])
      setPedido(p)
      setAgrupamentos(a)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar o pedido.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar(pedidoId) }, [pedidoId])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      const resultado = await criarAgrupamento(pedidoId, form)
      if (ehConflito(resultado)) {
        setErro('Já existe um agrupamento com este código neste pedido.')
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o agrupamento.'))
    } finally {
      setEnviando(false)
    }
  }

  // `excluirAgrupamento` só lança para status fora de 204/404/409 — os dois 409 e o 404 chegam como
  // retorno normal, tratados pelo MOTIVO_DA_RECUSA.
  async function excluir(agrupamentoId: number) {
    setErro(null)
    try {
      const desfecho = await excluirAgrupamento(agrupamentoId)
      if (desfecho !== 'ok') setErro(MOTIVO_DA_RECUSA[desfecho])
      await carregar(pedidoId)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível excluir o agrupamento.'))
    }
  }

  function confirmarExclusao() {
    if (!pendenteExclusao) return
    const agrupamentoId = pendenteExclusao.id
    setPendenteExclusao(null)
    excluir(agrupamentoId)
  }

  // SEM early return de página inteira, de propósito — ele existia e causava DOIS defeitos:
  // demolia a tela a cada ação (o que se sente como lentidão) e escondia a mensagem de recusa da
  // exclusão atrás do "Carregando…". O estado de carregamento fica ESCOPADO à lista.
  return (
    <Pagina titulo={pedido ? pedido.numero : 'Pedido'}>
      {pedido && (
        <div className="flex flex-col gap-2 rounded-lg border border-borda bg-superficie p-4">
          <p className="text-lg text-tinta">{pedido.cliente}</p>
          <p className="flex flex-wrap items-center gap-2 text-sm text-tinta-fraca">
            <Pilula>{pedido.tipo}</Pilula>
            <Pilula>{pedido.status}</Pilula>
            aberto em {formatarDataHora(pedido.dataAbertura)}
          </p>
        </div>
      )}

      <h2 className="text-lg font-medium text-tinta">Agrupamentos</h2>

      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código do agrupamento">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as NovoAgrupamento['tipo'] })}
                  className={CLASSES_DE_CONTROLE}
                >
                  <option value="Kit">Kit</option>
                  <option value="Avulso">Avulso</option>
                </select>
              )}
            </Campo>
          </div>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : agrupamentos.length === 0 ? (
        <EstadoVazio
          titulo="Nenhum agrupamento neste pedido"
          descricao={podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined}
        />
      ) : (
        <ListaDeCadastro rotulo="Agrupamentos">
          {agrupamentos.map((a) => (
            <ItemDeCadastro
              key={a.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => setPendenteExclusao(a)}>Excluir</Botao>
              )}
            >
              <span className="font-mono font-semibold">{a.codigo}</span>{' '}
              <Pilula>{a.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      {pendenteExclusao && (
        <div className="fixed inset-0 z-10 flex items-center justify-center bg-tinta/50 p-4">
          <div
            role="dialog"
            aria-modal="true"
            className="flex w-full max-w-sm flex-col gap-4 rounded-lg bg-superficie p-5 shadow-lg"
          >
            <p className="text-tinta">
              Excluir o agrupamento <strong className="font-mono">{pendenteExclusao.codigo}</strong>?
              Esta ação não pode ser desfeita.
            </p>
            {/*
              Ordem e peso visual são deliberados, não estética. Esta é a única exclusão física do
              sistema, e até a Fase 1A os dois botões tinham a MESMA classe — um modal de confirmação
              com dois botões idênticos troca a pausa deliberada por um sorteio. "Excluir" em
              vermelho (convenção de ação destrutiva) e "Cancelar" à DIREITA, onde cai o polegar num
              tablet. NÃO trocar a ordem nem igualar os pesos.
            */}
            <div className="flex flex-wrap justify-end gap-2">
              <Botao variante="perigo" onClick={confirmarExclusao}>Excluir</Botao>
              {/*
                autoFocus: mesma intenção, aplicada ao teclado. No DOM "Excluir" vem antes de
                "Cancelar" (ordem visual decidida, não mexer) — sem foco explícito, quem navega por
                teclado sem ler tabularia direto para o botão destrutivo.
                NÃO adicionar Esc / clique-fora / focus-trap: fora de escopo, como já estava.
              */}
              <Botao variante="secundario" onClick={() => setPendenteExclusao(null)} autoFocus>
                Cancelar
              </Botao>
            </div>
          </div>
        </div>
      )}
    </Pagina>
  )
}
```

**Repare no que sumiu:** o `<Link to="/pedidos">&larr; Pedidos</Link>` do topo. O caminho de volta agora é o shell. **Se ao ver no navegador (Task 12) faltar o retorno específico "para a lista de pedidos"**, acrescente-o como `acao` da `Pagina` e diga que fez — não é regressão, é uma decisão que só o uso resolve.

- [ ] **Step 3: Ajustar a suíte**

Os 4 testes da Task 1 continuam válidos em intenção. Ajustes:

1. acrescentar o `vi.mock('../auth/AuthContext', …)` com `perfil` default `'PCP'`;
2. `renderizarDetalhe` continua igual;
3. o teste do cabeçalho: `screen.findByText('PED-001')` agora casa com o `<h1>` da `Pagina` — segue valendo;
4. acrescentar dois testes:

```tsx
it('esconde o formulário e o botão de excluir para quem não pode escrever', async () => {
  perfil = 'Operador'
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/pedidos/7': () => respostaJson(PEDIDO),
    '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
  }))

  renderizarDetalhe()

  expect(await screen.findByText('AGR-01')).toBeTruthy()
  expect(screen.queryByLabelText('Código do agrupamento')).toBeNull()
  expect(screen.queryByText('Excluir')).toBeNull()
})

it('mostra estado vazio quando o pedido não tem agrupamentos', async () => {
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/pedidos/7': () => respostaJson(PEDIDO),
    '/api/pedidos/7/agrupamentos': () => respostaJson([]),
  }))

  renderizarDetalhe()

  expect(await screen.findByText('Nenhum agrupamento neste pedido')).toBeTruthy()
})
```

**Atenção ao teste "pede confirmação antes de excluir":** com o gating, o botão do item existe só para quem escreve — mantenha `perfil = 'PCP'` no `beforeEach`. E o truque de pegar o "Excluir" **dentro** do diálogo continua necessário, porque os dois existem ao mesmo tempo.

- [ ] **Step 4: Rodar e medir**

```bash
cd web && npm test -- PedidoDetalhePage && npm test && npm run build && npm run lint
```

Expected: `Tests  6 passed (6)` no arquivo; suíte inteira verde.

| # | Mutação | Mortes esperadas |
|---|---|---|
| M1 | trocar o `onClick` do "Excluir" do item por `() => excluir(a.id)` | ≥ 1 |
| M2 | trocar `variante="perigo"` do modal por `"secundario"` | **0 esperado** — nenhum teste olha o peso visual; **declare** |
| M3 | remover `autoFocus` do "Cancelar" | **0 esperado** — jsdom não prova ordem de tabulação; **declare** |
| M4 | trocar `MOTIVO_DA_RECUSA.AgrupamentoNaoVazio` pelo texto de `PedidoNaoAberto` | ≥ 1 |
| M5 | mover o `<BannerDeErro>` para depois do bloco `carregando ? …` | ≥ 1 se houver teste do "erro que pisca"; se der 0, **escreva o teste** |
| M6 | trocar `usePodeEscrever('agrupamentos')` por `('setores')` | ≥ 1 |
| M7 | remover `setEnviando(false)` do `finally` | ≥ 1 |

**M2 e M3 são fronteira declarada, não lacuna a fechar com asserção de classe.** O peso visual e a ordem de foco são exatamente o que a spec §11 manda verificar **no navegador** — Task 12.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/PedidoDetalhePage.tsx web/src/pages/PedidoDetalhePage.test.tsx
git commit -m "feat(web): re-layout do PedidoDetalhePage com as primitivas"
```

**Verificação pós-commit — obrigatória (a mesma do Step 6 da 9A; política do usuário de 2026-08-14: o commit é nosso, o PR é dele). Execute, não presuma:**

1. **`git status --short` depois do commit** — nada de `M`/`??` que devesse ter entrado.
2. **`git show --stat HEAD`** — os arquivos e a contagem batem com o que esta task produziu (aqui: os dois de `PedidoDetalhePage`, e nada mais).
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — sujeira alheia permanente: não commite, não reverta, não edite.
4. **O repositório é PÚBLICO**: varredura de segredo antes do push, passo obrigatório.

**Abrir ou mesclar PR não é nosso** — aprovação manual do usuário.

**Definition of done da Task 10:** as 7 mutações medidas (M2 e M3 declaradas); as três decisões preservadas (sem early return; ordem e peso do modal; `autoFocus` no Cancelar) — **confirme uma a uma no relatório**; a **verificação pós-commit** executada e reportada; suíte, build e lint verdes.

---

### Task 11: Home — cartões de contagem com números reais

**Files:**
- Modify: `web/src/pages/HomePage.tsx`
- Modify: `web/src/pages/HomePage.test.tsx` (reescrita — a tela muda de papel)

**Interfaces:**
- Consumes: `listarComponentes`, `listarSetores`, `listarMateriais`, `listarPedidos`, `mensagemDeErro`, `Pagina`, `Botao`, `BannerDeErro`.
- Produces: nada.

**A Home perde o emprego de menu** (spec §8): a navegação virou o shell na Task 7, e a fileira de `<Link>` com borda de `HomePage.tsx:47-54` fica sem função. No lugar entram **cartões de contagem com números verdadeiros** e atalhos.

**Esta task fecha a única violação medida do critério de viewport da spec §11.** Conferido no navegador em 2026-08-10, durante o Step 14 da Task 7: a Home rola na horizontal — `613px` de conteúdo em `412px` de viewport (`documentElement.clientWidth 412` contra `scrollWidth 613`). A causa é exatamente esta fileira: um `flex gap-3` **sem `flex-wrap`** com seis controles somando **589px** num container de **400px** (`max-w-md` menos `p-6`), o que a faz transbordar em **qualquer** largura de janela — numa tela larga o excesso só vaza para as margens e ninguém nota.

Os cartões resolvem por construção, mas **confirme, não presuma**: rode o snippet localizador da spec §11 na Home depois do re-layout e reporte que ele voltou vazio. Foi a presunção de que "cabe" que deixou isso passar até aqui.

Enquanto esta task não chega, o sintoma visível é o `bg-chrome` do shell não cobrir a área rolada — decidido em 2026-08-10 **não** mascarar isso, porque o branco à direita é o sinal de que a tela não cabe.

**Nada de número fake, e nada de rota crua** — a spec §8 fecha isso com dois motivos concretos: um número inventado é uma afirmação falsa numa tela que vai à banca; e o teste de fumaça **não distingue número fake de número real**, então a suíte ficaria verde provando uma mentira. Mock que precisa ser removido depois é o tipo de coisa que sobrevive até a defesa.

**De onde vêm os números, sem tocar no backend:**

| Cartão | Fonte | Custo |
|---|---|---|
| Componentes | `listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 })` → `.total` | 1 requisição, **nenhum item trafegado** — o `total` do `PaginaDe<T>` foi feito para isto |
| Pedidos abertos | `listarPedidos()` → `.filter(p => p.status === 'Aberto').length` | 1 requisição |
| Materiais | `listarMateriais(false)` → `.length` | 1 requisição |
| Setores | `listarSetores(false)` → `.length` | 1 requisição |

**Cartões que dependem de domínio inexistente NÃO aparecem.** "Peças em produção" depende do `EstruturaItem` (Fase 2); "reprovados do dia", do Relatório Dimensional (Fase 5). A grade nasce com o que tem número verdadeiro e **cresce por fase** — o layout fica pronto, e a Fase 6 preenche as lacunas com KPI de verdade em vez de redesenhar a Home.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test -- HomePage
```

Expected: `Tests  3 passed (3)`.

- [ ] **Step 2: Reescrever a `HomePage`**

`web/src/pages/HomePage.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, listarPedidos,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'

interface Contagens {
  pedidosAbertos: number
  componentes: number
  materiais: number
  setores: number
}

function CartaoDeContagem({ titulo, valor, para }: { titulo: string; valor: number | null; para: string }) {
  return (
    <Link
      to={para}
      className="flex flex-col gap-1 rounded-lg border border-borda bg-superficie px-5 py-6 transition-colors hover:border-acao focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      {/* Traço em vez de zero enquanto carrega: "0 pedidos" é uma afirmação, e ela seria falsa. */}
      <span className="text-3xl font-semibold text-tinta">{valor === null ? '—' : valor}</span>
      <span className="text-sm text-tinta-fraca">{titulo}</span>
    </Link>
  )
}

export function HomePage() {
  const [contagens, setContagens] = useState<Contagens | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    setErro(null)
    try {
      // `tamanho: 1` no de componentes: só o `total` interessa, e assim nenhum item trafega. As
      // outras três listagens ainda não são paginadas no backend (dívida rastreada da 1B: o
      // `PaginaDto<T>` não foi migrado para Setor/Material) — quando forem, este cartão vira o
      // molde das outras.
      const [paginaDeComponentes, pedidos, materiais, setores] = await Promise.all([
        listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 }),
        listarPedidos(),
        listarMateriais(false),
        listarSetores(false),
      ])
      setContagens({
        pedidosAbertos: pedidos.filter((p) => p.status === 'Aberto').length,
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os números do sistema.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  return (
    <Pagina titulo="Início">
      <BannerDeErro mensagem={erro} />

      {carregando && <p className="text-tinta-fraca">Carregando…</p>}

      <div className="grid gap-4 sm:grid-cols-2">
        <CartaoDeContagem titulo="pedidos abertos" valor={contagens?.pedidosAbertos ?? null} para="/pedidos" />
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
      </div>
    </Pagina>
  )
}
```

**Repare no que a Home NÃO faz mais:** ela não chama `/me` nem mostra usuário/perfil — isso mudou de casa para o shell na Task 7, e mantê-lo aqui duplicaria a informação em duas alturas da mesma tela. Ela também não tem botão "Sair" nem "Recarregar": o primeiro é do shell; o segundo era uma muleta de quando não havia estado de erro decente.

- [ ] **Step 3: Reescrever o teste da Home**

`web/src/pages/HomePage.test.tsx`, substituindo os 3 testes da Task 1:

```tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

const PEDIDOS = [
  { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 2, numero: 'PED-002', cliente: 'Beta', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-05T09:00:00-03:00', criadoPorUsuarioId: 1 },
]

function apiCompleta() {
  return fetchPorRota({
    // `total: 41` com UM item: é o `total` sob o filtro que vale, não `itens.length`. Se a tela
    // ler o array, ela mostra "1 componente" num catálogo de 41 — e é justamente por isso que a
    // chamada usa `tamanho: 1`.
    '/api/componentes': () => respostaJson({ itens: [{ id: 1, codigo: 'C', descricao: 'D', tipo: 'Bruto', ativo: true }], total: 41, pagina: 1, tamanho: 1 }),
    '/api/pedidos': () => respostaJson(PEDIDOS),
    '/api/materiais': () => respostaJson([{ id: 1, codigo: 'M1', descricao: 'Aço', unidadeMedida: 'KG', ativo: true }]),
    '/api/setores': () => respostaJson([
      { id: 1, nome: 'Corte', ativo: true },
      { id: 2, nome: 'Solda', ativo: true },
    ]),
  })
}

describe('HomePage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra o total de componentes vindo do campo total, não do tamanho da página', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('41')).toBeTruthy()
  })

  it('conta só os pedidos abertos', async () => {
    // Dois pedidos, um Aberto e um Concluido: contar `.length` daria 2 e a tela mentiria.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(cartao.textContent).toContain('1')
  })

  it('mostra as contagens de materiais e setores', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByText('materiais ativos').closest('a')!.textContent).toContain('1')
    expect(screen.getByText('setores ativos').closest('a')!.textContent).toContain('2')
  })

  it('pede só um item ao contar componentes', async () => {
    // A propriedade que torna o cartão barato: `tamanho: 1`. Se alguém trocar por 20, a Home passa
    // a trafegar 20 componentes para mostrar um número.
    const fetchMock = apiCompleta()
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const url = String(fetchMock.mock.calls.find((c) => String(c[0]).startsWith('/api/componentes'))![0])
    expect(url).toContain('tamanho=1')
  })

  it('leva a cada área pelo cartão', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toEqual(expect.arrayContaining(['/pedidos', '/componentes', '/materiais', '/setores']))
  })

  it('mostra traço, e não zero, enquanto os números não chegaram', () => {
    // "0 pedidos abertos" numa fábrica que tem pedidos é uma afirmação falsa. O traço diz "ainda
    // não sei", que é a verdade naquele instante.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getAllByText('—').length).toBe(4)
  })

  it('explica a falha quando alguma das listagens não responde', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'x' }, 500),
      '/api/pedidos': () => respostaJson(PEDIDOS),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(
      await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.'),
    ).toBeTruthy()
  })
})
```

- [ ] **Step 4: Rodar e medir**

```bash
cd web && npm test -- HomePage && npm test && npm run build && npm run lint
```

Expected: `Tests  7 passed (7)` no arquivo; suíte inteira verde.

| # | Mutação em `HomePage.tsx` | Mortes esperadas |
|---|---|---|
| M1 | `paginaDeComponentes.total` → `paginaDeComponentes.itens.length` | ≥ 1 |
| M2 | `tamanho: 1` → `tamanho: 20` | ≥ 1 |
| M3 | `pedidos.filter(…).length` → `pedidos.length` | ≥ 1 |
| M4 | `valor === null ? '—' : valor` → `valor ?? 0` | ≥ 1 |
| M5 | trocar `'Aberto'` por `'EmProducao'` no filtro | ≥ 1 |
| M6 | trocar `materiais.length` por `setores.length` | ≥ 1 |
| M7 | remover o `catch` inteiro (deixar a promise rejeitar) | ≥ 1 |

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/HomePage.tsx web/src/pages/HomePage.test.tsx
git commit -m "feat(web): Home com cartoes de contagem reais, sem numero fake"
```

**Verificação pós-commit — obrigatória (a mesma do Step 6 da 9A; política do usuário de 2026-08-14: o commit é nosso, o PR é dele). Execute, não presuma:**

1. **`git status --short` depois do commit** — nada de `M`/`??` que devesse ter entrado.
2. **`git show --stat HEAD`** — os arquivos e a contagem batem com o que esta task produziu (aqui: os dois de `HomePage`, e nada mais).
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — sujeira alheia permanente: não commite, não reverta, não edite.
4. **O repositório é PÚBLICO**: varredura de segredo antes do push, passo obrigatório.

**Abrir ou mesclar PR não é nosso** — aprovação manual do usuário.

**Definition of done da Task 11:** as 7 mutações medidas com ≥ 1 morte; `grep -c "'/me'" src/pages/HomePage.tsx` = 0 (a identidade vive no shell); **nenhum número constante na tela**; a **verificação pós-commit** executada e reportada; suíte, build e lint verdes.

---

### Task 12: `LoginPage`, varredura de conformidade e atualização das specs

**Files:**
- Modify: `web/src/pages/LoginPage.tsx` + `LoginPage.test.tsx`
- Modify: `specs/06-roadmap-mvp.md`
- Modify: `CLAUDE.md` (uma seção nova)

**Interfaces:**
- Consumes: tudo.
- Produces: nada de código. Esta task **fecha** a fase.

**Três trabalhos distintos, e a ordem importa:** primeiro o Login (última tela sem identidade), depois a varredura que prova o critério de aceite da spec §11, e só então as specs — que descrevem o que ficou, não o que se pretendia.

- [ ] **Step 1: Confirmar a baseline**

```bash
cd web && npm test && npm run build && npm run lint
```

Expected: tudo verde. Anote os números — são a baseline do relatório final.

- [ ] **Step 2: Aplicar a identidade à `LoginPage`**

O layout já é adequado (cartão centralizado, `max-w-sm`) — a spec §7 diz que ela só recebe identidade.

`web/src/pages/LoginPage.tsx`:

```tsx
import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'

export function LoginPage() {
  const { estado, login } = useAuth()
  const [nomeUsuario, setNomeUsuario] = useState('')
  const [senha, setSenha] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  if (estado.status === 'autenticado') return <Navigate to="/" replace />

  // Aviso discreto quando o usuário chegou aqui por sessão perdida (onSessionLost), não por acesso
  // normal. Comunica que foi rotina de autenticação, não erro dele.
  const sessaoExpirada = estado.status === 'anonimo' && estado.motivo === 'sessao-expirada'

  async function aoEnviar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setEnviando(true)
    try {
      await login(nomeUsuario, senha)
    } catch {
      // Mensagem ÚNICA e genérica: honra o não-oráculo do backend, que responde o mesmo 401 para
      // usuário inexistente, conta trancada e senha errada. Variar a mensagem por caso aqui
      // desfaria no front a defesa que o backend paga BCrypt para manter.
      setErro('Usuário ou senha inválidos.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    // A única tela fora do shell, e por isso a única que ainda carrega `min-h-screen`.
    <div className="min-h-screen bg-chrome font-sans flex items-center justify-center p-4">
      <form
        onSubmit={aoEnviar}
        className="flex w-full max-w-sm flex-col gap-5 rounded-xl bg-superficie p-6 shadow-lg"
      >
        {/* A marca aparece sobre o chrome escuro do fundo, não dentro do cartão claro: o
            verde-água só tem contraste AA sobre o petróleo. */}
        <h1 className="text-center text-2xl font-semibold tracking-tight text-chrome">Rastru</h1>

        {sessaoExpirada && (
          // Âmbar seria um quinto matiz para manter coerente, e os tons claros dele reprovam AA
          // como texto (spec §3). Aviso de rotina fica no cinza-esverdeado dos neutros.
          <p className="rounded-lg border border-borda bg-fundo px-3 py-2 text-center text-sm text-tinta-fraca">
            Sessão expirada. Entre novamente.
          </p>
        )}

        <Campo rotulo="Usuário">
          {(id) => (
            <input
              id={id}
              value={nomeUsuario}
              onChange={(e) => setNomeUsuario(e.target.value)}
              required
              autoComplete="username"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <Campo rotulo="Senha">
          {(id) => (
            <input
              id={id}
              type="password"
              value={senha}
              onChange={(e) => setSenha(e.target.value)}
              required
              autoComplete="current-password"
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>

        <BannerDeErro mensagem={erro} />

        <Botao type="submit" carregando={enviando} rotuloCarregando="Entrando…">Entrar</Botao>
      </form>
    </div>
  )
}
```

**Os 4 testes da Task 1 continuam válidos sem mudança** — eles já usam `getByLabelText('Usuário')`, que passa de label envolvente para `htmlFor` sem quebrar. **Confirme rodando; se algum quebrar, não relaxe a asserção.**

Acrescente um:

```tsx
it('desabilita o botão enquanto o login está em voo', async () => {
  let liberar: (r: Response) => void = () => {}
  vi.stubGlobal('fetch', fetchPorRota({
    '/api/auth/refresh': () => respostaJson({ erro: 'sem sessão' }, 401),
    '/api/auth/login': () => new Promise<Response>((r) => { liberar = r }),
  }))

  renderizarLogin()
  fireEvent.change(await screen.findByLabelText('Usuário'), { target: { value: 'admin' } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'Admin@123' } })
  fireEvent.click(screen.getByText('Entrar'))

  expect((await screen.findByText('Entrando…') as HTMLButtonElement).disabled).toBe(true)
  liberar(respostaJson({ erro: 'x' }, 401))
})
```

- [ ] **Step 3: Rodar e commitar o Login**

```bash
cd web && npm test -- LoginPage
```

Expected: `Tests  5 passed (5)`.

```bash
git add web/src/pages/LoginPage.tsx web/src/pages/LoginPage.test.tsx
git commit -m "feat(web): identidade visual na LoginPage"
```

**Verificação pós-commit — obrigatória (a mesma do Step 6 da 9A; política do usuário de 2026-08-14: o commit é nosso, o PR é dele). Execute, não presuma:**

1. **`git status --short` depois do commit** — nada de `M`/`??` que devesse ter entrado.
2. **`git show --stat HEAD`** — os arquivos e a contagem batem com o que este Step produziu (aqui: os dois de `LoginPage`, e nada mais). **Este é o primeiro de DOIS commits desta task** — o Step 8 faz o segundo, com as specs e o `CLAUDE.md`; não junte os dois nem deixe o segundo lote entrar aqui por engano.
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — sujeira alheia permanente: não commite, não reverta, não edite.
4. **O repositório é PÚBLICO**: varredura de segredo antes do push, passo obrigatório — a varredura formal, sobre o **histórico** da branch, é a **Task 13**, e é ela que libera o push de tudo o que a fase produziu.

**Abrir ou mesclar PR não é nosso** — aprovação manual do usuário.

- [ ] **Step 4: Varredura de conformidade — o critério de aceite da spec §11**

*"Nenhuma cópia da forma antiga sobrou no repositório"*, verificável por busca. Rode **cada uma** e reporte o número:

```bash
cd web
grep -rn "max-w-md mx-auto" src/          # esperado: 0 — o container antigo
grep -rn "min-h-screen" src/              # esperado: 2 — só AppShell.tsx e LoginPage.tsx
grep -rn "border rounded px-3 py-2" src/  # esperado: 0 — o input/botão copiado
grep -rn "text-gray-" src/                # esperado: 0 — a paleta antiga
grep -rn "bg-gray-\|border-gray-" src/    # esperado: 0
grep -rn "text-red-600" src/              # esperado: 0 — virou text-negativo
grep -rn "bg-amber-\|text-amber-" src/    # esperado: 0
grep -rn "placeholder=" src/pages/        # esperado: 0 — placeholder deixou de fazer papel de rótulo
```

**Qualquer resultado diferente do esperado é trabalho desta task, não observação.** Se sobrou classe antiga, migre; se sobrou `min-h-screen` numa terceira tela, tire.

E a conferência de que as primitivas têm consumidor real (critério da spec §5 — *"primitiva com um consumidor só é abstração prematura"*):

```bash
cd web
for p in Pagina Botao Campo BannerDeErro ListaDeCadastro Pilula EstadoVazio ControlesDePaginacao; do
  echo -n "$p: "; grep -rl "from '../components/$p'" src/pages/ | wc -l
done
```

Expected: `Pagina` 6, `Botao` ≥ 6, `Campo` ≥ 6, `BannerDeErro` ≥ 6, `ListaDeCadastro` 4, `Pilula` ≥ 3, `EstadoVazio` ≥ 5, `ControlesDePaginacao` 1.

**`ControlesDePaginacao` com um consumidor só é a exceção conhecida, e ela é legítima:** só a `ComponentesPage` tem paginação hoje porque só ela é paginada no backend. Está declarada aqui para não virar achado de review. As demais precisam de ≥ 2; **se alguma tiver menos, reporte** — ou faltou um consumidor, ou a primitiva não devia existir.

- [ ] **Step 5: Verificação manual no navegador — o que jsdom não alcança**

Suba os dois lados:

```bash
dotnet run --project src/Rastreamento.Api
cd web && npm run dev
```

Com `admin` / `Admin@123` (Administrador) e depois `pcp` / a senha do seed (PCP), confirme **item por item** e **relate cada um**:

| # | O que verificar | Por que nenhum teste pega |
|---|---|---|
| V1 | **Nenhuma** das 7 telas rola na horizontal em **nenhuma largura ≥ 320px** — arraste de 320 a ~1280, registrando 320, 767 e 768; e rode o snippet localizador da spec §11 em cada tela | jsdom não calcula layout |
| V2 | A barra vira gaveta abaixo de 768px, e a gaveta fecha ao navegar | idem |
| V3 | `Tab` percorre toda tela com **anel de foco visível**, inclusive sobre o chrome escuro | jsdom não renderiza foco |
| V4 | No modal de exclusão, o foco começa em **"Cancelar"** e "Excluir" está visivelmente mais pesado | M2/M3 da Task 10, declaradas |
| V5 | Nenhum par texto/fundo parece apagado — cruze com os números medidos na Task 4 | o teste mede tokens, não composições |
| V6 | Clicar em qualquer ponto de um cartão de pedido navega para **aquele** pedido | o `after:inset-0` sem `relative` cobriria a tela inteira |
| V7 | Com o perfil **PCP**: Setores e Materiais aparecem no menu e listam, **sem** formulário nem botão de inativar | o gating está testado, a composição real não |
| V8 | Desligue a API e recarregue: a mensagem é "Sem conexão com o servidor…", não o texto genérico | o `TypeError` real do `fetch` não é o que o mock produz |

**V8 é a única prova de ponta a ponta de que `mensagemDeErro` acerta o caso de rede** — o mock dos testes rejeita com `Error`, não com o `TypeError` que o navegador lança. Faça e reporte.

- [ ] **Step 6: Registrar a fase em `specs/06-roadmap-mvp.md`**

Acrescente, **depois** do bloco de citação da Fase 1 (que hoje termina em "Dívidas rastreadas de 1A: camada global de erro de API no front e gating de navegação por perfil."), e **antes** de `## Fase 2`:

```markdown
## Fase 1D — Identidade visual e UX

- Tokens de tema, primitivas de interface à mão sobre Tailwind e shell de navegação.
- Retrofit das 7 telas existentes para o padrão novo.
- Critério de pronto: mesma primitiva nas 7 telas, estados carregando/vazio/erro em toda tela que
  busca dados, navegação por teclado com foco visível, contraste AA medido por teste, e nenhuma
  tela rolando na horizontal em viewport de celular.

> **Esta fase NÃO tem aresta de dependência.** Ela não bloqueia nem é bloqueada pela 1C, e pode
> rodar antes ou depois dela. A letra é rótulo cronológico, não ordem obrigatória — sem esta frase,
> a sequência 1B → 1C → 1D se lê como dependência, e ela não é.
>
> **1D concluída em 2026-08-XX.** Fecha três dívidas de UX que vinham da 1A e da 1B: camada global
> de erro de API (`ErroDeApi` + `mensagemDeErro`), gating de perfil, e botão desabilitado durante
> mutação. Fecha também o `useBuscaPaginada` (debounce, cancelamento, clamp, reset) e o W3
> (`setCarregando` sem prova).
>
> **O gating de perfil ficou na AÇÃO, não no link** — o link continua visível para todos porque a
> leitura de todos estes recursos é liberada a qualquer usuário autenticado no backend; o que some
> para quem não pode escrever é o formulário e os botões de (in)ativar. Esconder o link de Materiais
> do Almoxarifado tiraria dele uma leitura de que a **Fase 4** depende.
>
> **Corolário registrado:** se daqui a três fases o sistema precisar de outra passada de UI, isso não
> é uma fase planejada que faltou — é sinal de que o padrão não pegou. Não existe "Fase 1D parte 2".
```

**Substitua `2026-08-XX` pela data real da conclusão.**

- [ ] **Step 7: Registrar as convenções novas em `CLAUDE.md`**

Acrescente uma seção **depois** de "Convenções de nomenclatura":

```markdown
## Interface (a partir da Fase 1D)

O padrão visual e de interação nasceu na Fase 1D e vale para **toda tela nova**. A spec de origem é
`docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md`.

- **Tela nova começa por `<Pagina titulo="…">`**, dentro da rota de layout do `AppShell` em
  `web/src/App.tsx`. Não escreva container próprio, e nunca `min-h-screen` numa tela — quem faz isso
  é o shell (a `LoginPage` é a única exceção, por ficar fora dele).
- **Não escreva campo, botão, banner de erro, item de lista, pílula, paginação ou estado vazio à
  mão.** As primitivas estão em `web/src/components/`. Se faltar uma, crie-a lá com teste próprio —
  não a embuta na tela.
- **Cores só pelos tokens** de `web/src/index.css` (`text-tinta`, `bg-acao`, `border-borda`…).
  `text-gray-*`, `text-red-600` e afins não existem mais no repositório, e a varredura da Fase 1D
  confirmou zero ocorrências.
- **Tom novo tem de ser medido antes de entrar.** `web/src/tema/contraste.test.ts` lê o `@theme` do
  `index.css` e reprova token sem par de contraste declarado. Os tons claros de verde-água e âmbar
  reprovam AA como texto sobre branco apesar de funcionarem como fundo de botão — a regra existe
  por causa disso.
- **Cor de identidade nunca significa estado; cor de estado nunca decora.** Verde (`positivo`) e
  vermelho (`negativo`) são reservados a aprovado/ativo e reprovado/perda/erro. É o que faz a tela
  de Qualidade da Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho" dividem a mesma linha.
- **Tela que busca dados tem os três estados**: carregando, vazio (com texto que distingue "não
  achei" de "não há nada") e erro (via `mensagemDeErro`), **cada um com teste que morre se o estado
  sumir**.
- **Busca paginada usa `useBuscaPaginada`** (`web/src/hooks/`), nunca `useState` + `useEffect` à
  mão: ele já resolve debounce, cancelamento por sequência, clamp de página e reset de filtro.
- **Gating de perfil vai na AÇÃO, não no link.** `usePodeEscrever(recurso)` esconde formulário e
  botões de escrita; o link continua visível porque leitura é de todos. **A tabela
  `web/src/auth/permissoes.ts` espelha os `[Authorize(Roles)]` do backend — mudou lá, muda aqui.**
  E o `try/catch` do F2 continua obrigatório: o 403 é a fronteira real, esconder botão não é
  segurança.
- **Teste de tela usa `web/src/testes/api.ts`** (`respostaJson`, `fetchPorRota`) e declara
  `// @vitest-environment jsdom` no topo, com `afterEach(cleanup)` explícito — este projeto não usa
  `globals: true`.
- **`npm run build` faz parte do ciclo**, não só `npm test`: erro de tipo em `.test.tsx` quebra o
  build sem quebrar a suíte (o Vitest não faz typecheck).
```

- [ ] **Step 8: Conferência rápida do diff, e o commit final**

O repositório é **público**. Uma conferência barata do que a fase deixou na árvore, antes do commit:

```bash
git diff main --stat
git diff main | grep -inE "password|senha|secret|token|api[_-]?key|connectionstring" | grep -v "CancellationToken\|accessToken\|refreshToken\|tokenRef\|getToken\|setToken"
```

Expected: nada além de identificadores de código. **Se aparecer valor literal, pare e reporte.**

> **⚠️ ISTO NÃO É A VARREDURA DE SEGREDO, e não libera o push.** `git diff main` compara **estados
> de árvore**: mostra só o líquido da fase. Um segredo acrescentado num commit lá atrás e removido
> depois **não aparece aqui**, e continua no histórico, que é o que o push publica. **A varredura
> que vale é a Task 13**, sobre `git log -p origin/main..HEAD` — commit a commit. Este Step é
> triagem barata; a Task 13 é a prova.

```bash
git add specs/06-roadmap-mvp.md CLAUDE.md
git commit -m "docs: registra a Fase 1D no roadmap e as convencoes de interface"
```

**Verificação pós-commit — obrigatória (a mesma do Step 6 da 9A; política do usuário de 2026-08-14: o commit é nosso, o PR é dele). Este é o commit FINAL da fase, então ela vale dobrado:**

1. **`git status --short` depois do commit** — nada de `M`/`??` sobrando. **Este é o último commit da branch:** o que ficar de fora aqui não entra na fase, e um arquivo de task anterior esquecido no `status` significa que aquela task commitou incompleta.
2. **`git show --stat HEAD`** — `specs/06-roadmap-mvp.md` e `CLAUDE.md`, e nada mais.
3. **Nunca entram `.claude/settings.local.json` nem `.claude/settings.json`** — sujeira alheia permanente: não commite, não reverta, não edite.
4. **A conferência de diff é o começo deste Step**, executada e não presumida — **mas ela não é a varredura de segredo.** A varredura que libera o push é a **Task 13**, sobre `git log -p origin/main..HEAD`; ela vem **depois** deste commit, porque só então o histórico da fase está completo.

**Abrir ou mesclar o PR da fase NÃO é nosso** — é aprovação manual do usuário. Pare depois do commit e reporte.

- [ ] **Step 9: Relatório de fechamento da fase**

Reporte, em números medidos e não estimados:

- suíte final (`npm test`), build e lint, com a composição por arquivo;
- **crescimento da suíte**: 96 no início → N no fim, e quantos testes vieram de cada task;
- o resultado de **cada** comando de varredura do Step 4;
- o resultado de **cada** verificação manual V1–V8 do Step 5;
- a lista consolidada dos **sobreviventes declarados** da fase: `flex-wrap` (Task 6, M11),
  `ProtectedRoute` na rota de layout (Task 7, M11), peso do botão de perigo e `autoFocus`
  (Task 10, M2/M3) — e qualquer outro que tenha aparecido;
- os **valores de contraste medidos** de cada par (Task 4).

**Definition of done da Task 12:** as 8 varreduras do Step 4 com o resultado esperado; as 8 verificações manuais feitas e relatadas; `specs/06-roadmap-mvp.md` e `CLAUDE.md` atualizados; a conferência rápida de diff do Step 8 feita (**a varredura formal é a Task 13, e é ela que libera o push**); suíte, build e lint verdes.

---

### Task 13: Varredura de segredo no HISTÓRICO da branch, antes de montar o PR

**Files:**
- **Cria: nada. Modifica: nada.** *Não se aplica, porque* esta task não escreve código, teste nem
  documento: ela **lê o histórico do git** e devolve um veredito. Os arquivos intermediários que ela
  produz (o patch e a lista de casamentos) vão para **fora da árvore** — o `$TMP` do Step 3, nunca `web/`,
  `src/`, `docs/` ou a raiz.
- **Consequência verificável:** se esta task terminar com `git status --short` diferente do estado em
  que começou, **ela errou**. Isso está na DoD.

**Interfaces:**
- **Consumes:** o **histórico completo** da branch `fase-1d-ui-e-ux` — todos os commits de
  `origin/main..HEAD`. Não a árvore de trabalho, não o `git diff`, não o último commit.
- **Provides:** o **go / no-go do push e do PR** da fase. *Nenhuma interface de código — não se
  aplica, porque a task não produz símbolo, tipo ou componente que outra task importe.*

**Delta de teste: ZERO — e ela NÃO entra na contagem da suíte.** Esta task não acrescenta nem remove
`it(`. O total com que a Task 12 fechar continua valendo depois dela; **quem somar em cima soma 0**.
Se `npm test` mudar de número aqui, aconteceu alguma coisa fora do escopo desta task — pare e
reporte. (Esta linha existe porque total de teste em plano é dívida composta: uma task sem delta
declarado vira `+?` na cabeça da próxima pessoa que somar.)

**Por que ela existe, e por que só agora.** O repositório é **PÚBLICO**
(`github.com/rufino-bot/rastru`) e esta branch é **local**: nunca foi empurrada, não tem upstream.
Em 2026-08-14 eram **49 commits** à frente de `origin/main` (MEDIDO,
`git rev-list --count origin/main..HEAD`) — **serão mais quando esta task rodar; meça, não copie o
49**. O push publica **todos eles de uma vez**, e é o primeiro e único momento em que qualquer coisa
escrita em qualquer um desses commits deixa de ser privada.

> **🚨 O ERRO QUE ESTA TASK EXISTE PARA NÃO SE COMETER: varrer o ESTADO FINAL.**
>
> `git diff`, `git diff origin/main`, `git status`, `grep -r web/src/`, abrir o último commit —
> **todos comparam ESTADOS de árvore e mostram só o líquido.** Um segredo acrescentado no commit 3 e
> apagado no commit 40 **não aparece em nenhum deles**, e continua dentro do objeto git do commit 3,
> que o push publica junto com o resto.
>
> **A varredura é sobre `git log -p origin/main..HEAD` — commit a commit, o histórico inteiro.**
> Se você conseguiu cumprir esta task lendo só o último commit, ou só a árvore de trabalho, **você
> não a cumpriu** — e o relatório que disser "varredura limpa" a partir disso é pior que nenhum,
> porque compra uma segurança que não foi verificada.

> **LINHA DE BASE ACEITA — declare-a, não a trate como achado.** Estas três coisas já estão
> commitadas há muito tempo, são conhecidas e **por desenho**. Uma varredura que acusa a linha de
> base é uma varredura que as pessoas aprendem a ignorar:
>
> | # | O quê | Onde | Por que não é achado |
> |---|---|---|---|
> | 1 | `Your_strong_Pass123` | `docker-compose.yml:6` (`MSSQL_SA_PASSWORD`), `src/Rastreamento.Api/appsettings.json:10` (connection string), `CLAUDE.md` — MEDIDO em 2026-08-14 | Senha do `sa` do **SQL Server em container de desenvolvimento local**. Credencial de dev, documentada de propósito; não existe em produção. **É ela que faz o padrão `Password=` casar** — o casamento é esperado, e é por isso que a exclusão é por valor. |
> | 2 | `$2a$11$XdGh9XVWVeYjBsgH0t4xPO…` (usuário `admin`, senha `Admin@123`) | `db/seed.sql`, `CLAUDE.md` | **Hash BCrypt do seed**, por desenho — é o que faz o ambiente de dev ter um login. |
> | 3 | `$2a$11$gbL2eZQIk1S1zAYieDUJO…` (usuário `pcp`, senha `Pcp@123`) | `db/seed.sql`, `CLAUDE.md` | Idem — o segundo usuário do seed, que entrou no fix pass da Task 12 da Fase 1A (achado B11). |
>
> **Tudo o que aparecer fora desta lista é ACHADO**, mesmo que pareça inofensivo, mesmo que seja de
> teste, mesmo que já esteja em `main`. Em particular: um `Jwt:SigningKey` com valor literal **é
> achado** — a dívida de movê-lo para segredo de ambiente está registrada no `CLAUDE.md` como *em
> aberto*, e "está em aberto" não é o mesmo que "está aceito nesta varredura".

- [ ] **Step 1: Fixar o intervalo e PROVAR qual é**

```bash
git fetch origin
git rev-list --count origin/main..HEAD          # N commits — anote o número
git log --oneline origin/main..HEAD | tail -1   # o PRIMEIRO commit da branch
git log --oneline -1                            # o ÚLTIMO (HEAD)
```

Reporte os três. **O intervalo coberto entra no relatório como número e como par de SHAs, não como
"a branch"** — é isso que torna a DoD verificável sem julgamento: quem ler o relatório consegue
recontar o intervalo e conferir que ele bate com o que foi empurrado.

`origin/main..HEAD` é "tudo o que HEAD alcança e `origin/main` não" — equivale ao
`merge-base(origin/main, HEAD)..HEAD` e é o conjunto exato que o push vai publicar. **Se o `fetch`
mover `origin/main`, o intervalo encolhe corretamente**; por isso o `fetch` vem antes.

- [ ] **Step 2: Preferir ferramenta dedicada — e NÃO presumir que existe**

```bash
command -v gitleaks trufflehog
```

- **`gitleaks` disponível:**
  `gitleaks detect --log-opts="origin/main..HEAD" --redact --report-path="${TMPDIR:-/tmp}/gitleaks-1d.json"`
  — **repare no `--log-opts`: sem ele o `gitleaks detect` varre o diretório de trabalho**, que é
  exatamente o erro que esta task proíbe. Com ele, varre commit a commit.
- **`trufflehog` disponível:**
  `trufflehog git file://. --since-commit origin/main --results=verified,unknown`
- **Nenhum dos dois:** foi o caso em **2026-08-14 — MEDIDO** (`command -v` não achou nenhum dos
  dois nesta máquina). **Siga para o Step 3, que funciona sozinho e é suficiente.** Não instale
  ferramenta para cumprir esta task: instalar cria dependência nova numa fase que declarou não ter
  CI, e o `grep` sobre o patch cobre o que precisa ser coberto aqui.

Rodar uma ferramenta dedicada **não dispensa** o Step 4 (arquivos acrescentados): as duas leem
conteúdo, e conteúdo binário elas também não abrem.

- [ ] **Step 3: A varredura por `grep` sobre o patch do HISTÓRICO (fallback autossuficiente)**

```bash
TMP="${TMPDIR:-/tmp}"          # no Git Bash do Windows o TMPDIR costuma vir vazio — fixe-o
git log -p --no-color origin/main..HEAD > "$TMP/historico-1d.patch"
grep -c '' "$TMP/historico-1d.patch"     # tamanho em linhas — prova que não veio vazio
```

**`git log -p` percorre TODOS os commits do intervalo**, um a um, com o patch de cada um — é essa a
diferença que a caixa 🚨 acima descreve. **Grave fora da árvore** (o `$TMP` acima), senão a própria task
suja o `git status` que ela tem de reportar limpo.

Agora os padrões. Rode assim, em duas etapas, e **reporte as duas contagens**:

```bash
# (a) casamentos BRUTOS, sem filtro nenhum
grep -inE "senha|password|secret|api[_-]?key|token *[:=]|Bearer +[A-Za-z0-9._-]{16,}|BEGIN [A-Z ]*PRIVATE KEY|SigningKey|Password=|Pwd=|Data Source=|Server=.*User" \
  "$TMP/historico-1d.patch" | tee "$TMP/brutos-1d.txt" | wc -l

# (b) o mesmo, tirando a LINHA DE BASE (por valor) e os identificadores de código (por nome)
grep -v -e 'Your_strong_Pass123' -e 'XdGh9XVWVeYjBsgH0t4xPO' -e 'gbL2eZQIk1S1zAYieDUJO' \
  "$TMP/brutos-1d.txt" \
| grep -viE "CancellationToken|accessToken|refreshToken|tokenRef|getToken|setToken|IPasswordHasher|SenhaHash|HashFicticio|type=\"password\"|autoComplete=|name=\"senha\"|rotulo=\"Senha\"" \
  > "$TMP/achados-1d.txt"
wc -l < "$TMP/achados-1d.txt"
cat "$TMP/achados-1d.txt"
```

**Três coisas sobre esse filtro, e nenhuma é detalhe:**

1. **A linha de base é excluída por VALOR, não por padrão.** `XdGh9XVWVeYjBsgH0t4xPO` e
   `gbL2eZQIk1S1zAYieDUJO` são trechos dos **dois hashes específicos** do seed. Excluir `\$2a\$11\$`
   inteiro cegaria a varredura para **qualquer** hash BCrypt novo, inclusive um de produção colado
   por engano — que é justamente o que se quer pegar.
2. **A segunda lista exclui IDENTIFICADOR de código, nunca VALOR.** Cada padrão que você
   acrescentar ali é uma cegueira que você está criando de propósito; se precisar acrescentar,
   **escreva no relatório qual e por quê**.
3. **Limite conhecido, e é por isso que as duas contagens são exigidas:** o filtro é **por linha** —
   uma linha que tenha `accessToken` **e** um valor literal ao lado sai junto. Se (b) derrubou uma
   ordem de grandeza em relação a (a), **olhe uma amostra de `brutos-1d.txt` com o olho** antes de
   declarar limpo. "0 achados" com um filtro que comeu 3.000 linhas não é resultado, é anestesia.

- [ ] **Step 4: Os arquivos ACRESCENTADOS em qualquer commit — o que o `-p` não mostra**

```bash
git log --diff-filter=A --name-only --pretty=format: origin/main..HEAD | sort -u | grep -v '^$'
```

`git log -p` escreve `Binary files … differ` no lugar do conteúdo: um `.pfx`, um `.p12`, um `.key`,
um dump `.bak`/`.dump`/`.sqlite` ou um `.zip` **passa pelo Step 3 sem uma linha de saída**. Este
Step lista todo arquivo que **nasceu** em algum commit do intervalo — **inclusive os que foram
apagados depois**, que é exatamente o caso perigoso.

Confira a lista inteira e reporte-a. Procure: `.env` (em qualquer variante — `.env.local`,
`.env.production`), `.pem`, `.key`, `.pfx`, `.p12`, `.crt`, `.bak`, `.dump`, `.sql` que não seja
`specs/02-modelo-de-dados.sql` nem `db/seed.sql`, `.sqlite`, `.zip`, `.7z`, imagem/PDF inesperado, e
qualquer coisa que não seja fonte, teste, doc ou config que esta fase declarou tocar.

**Referência do que é esperado nesta fase:** os 49 commits medidos em 2026-08-14 tocavam **só**
`web/**` e `docs/superpowers/**` (MEDIDO, `git log --name-only`). Um arquivo fora desses dois
prefixos é, no mínimo, pergunta — e um binário lá dentro é achado até prova em contrário.

- [ ] **Step 5: O que fazer com um achado — e o que NÃO fazer**

> **🚨 UM ACHADO REAL NÃO SE CORRIGE COM UM COMMIT NOVO.** Apagar o segredo num commit posterior
> deixa o original **intacto no histórico** — e o push publica o histórico. O commit de remoção só
> faz o `git diff` ficar limpo, que é a ilusão que esta task inteira existe para desmontar.
>
> O conserto tem **duas partes, e nenhuma é opcional**:
>
> 1. **Reescrever o histórico** — `git rebase -i` (se for um commit só, e a branch não foi
>    publicada, que é o nosso caso) ou `git filter-repo` (se o valor aparece em vários commits).
>    Reescrever muda os SHAs de todos os commits a partir dali; como a branch é **local e sem
>    upstream**, isso é barato **agora** e caro depois do push.
> 2. **Rotacionar a credencial exposta** — trocá-la no sistema de origem. Uma vez escrita num
>    objeto git, ela tem de ser considerada **comprometida**, mesmo que o objeto nunca tenha saído
>    desta máquina: a suposição de que "ninguém viu" não é verificável, e o custo de errar é
>    assimétrico.
>
> **Esta task PARA no achado.** Ela não conserta, não commita, não empurra. Reporte o achado, o
> commit onde ele nasceu (`git log -S'<valor>' --oneline origin/main..HEAD`) e as duas partes do
> conserto — **a decisão de reescrever histórico é do usuário**.

- [ ] **Step 6: Relatório e veredito**

Reporte, com os comandos **colados** (não parafraseados) e os números medidos:

- o intervalo: `origin/main..HEAD`, **N** commits, SHA do primeiro e do último;
- qual caminho foi usado (ferramenta dedicada ou o `grep` do Step 3), e o resultado do
  `command -v` que decidiu isso;
- as **duas** contagens do Step 3 — brutos e pós-filtro — e o conteúdo de `achados-1d.txt`;
- a lista completa do Step 4 (arquivos acrescentados no intervalo), com o veredito de cada item que
  não seja fonte/teste/doc;
- a **declaração nominal das três exceções da linha de base**, dizendo explicitamente que **tudo
  fora delas foi tratado como achado**;
- se houve achado: o commit de origem e as duas partes do conserto — **e a task para aqui**;
- `git status --short`, provando que a task não deixou nada na árvore.

**Só depois deste veredito o push da branch é liberado.** E **abrir ou mesclar o PR NÃO é nosso** —
é aprovação manual do usuário (política de 2026-08-14: *o commit é nosso, o PR é dele*). Pare depois
do relatório.

**Definition of done da Task 13** — verificável sem julgamento, item por item:

- **o comando exato** da varredura está no relatório, colado, e ele contém `origin/main..HEAD` — se
  o comando reportado for um `git diff` ou um `grep -r` sobre a árvore, **a DoD não está cumprida**,
  independentemente do resultado;
- **o intervalo coberto** está reportado como número de commits **e** como par de SHAs
  (primeiro/último), conferível por quem ler;
- as **duas contagens** do Step 3 (brutos e pós-filtro) estão reportadas, e não só a segunda;
- a **lista de arquivos acrescentados** do Step 4 está reportada inteira;
- **as três exceções da linha de base estão declaradas nominalmente**, com a frase explícita de que
  o que está fora delas foi tratado como achado;
- **resultado:** ou "nenhum achado" com as evidências acima, ou o achado com commit de origem e as
  duas partes do conserto (reescrita de histórico **+** rotação da credencial) — nunca "resolvido
  por commit de remoção";
- **`git status --short` igual ao do início** — esta task não modifica arquivo nenhum do
  repositório, e os intermediários dela ficam no `$TMP` do Step 3;
- **delta de teste 0**, declarado no relatório, para a próxima contagem não herdar `+?`.

---

## Depois da fase

1. **Review de branch inteira** (Opus) — o escopo é a **integração entre tasks**, não repetição das
   reviews individuais: as costuras entre primitivas e telas, o shell atravessando as 7, e a
   coerência entre o que o teste de contraste mede e o que a tela compõe.
2. `superpowers:finishing-a-development-branch` → **PR** — **depois da Task 13**, que é o que libera
   o push. O projeto retomou o rastro por PR nas
   #1/#2/#3/#4/#5; a 1A entrou por push direto e isso foi registrado como perda.
3. **Fica de fora e tem dono próprio:** as dívidas **I2 e I3** da review de branch da 1B (o `Trim`
   de `LocalizarDuplicado` sem prova; o teste negativo de autorização que fixa um perfil por
   controller). São **backend**, e o prazo delas é **fix pass antes da 1C**.

## O que esta fase deliberadamente NÃO faz

Nomeado, não esquecido (spec §12):

- **Tema escuro.** Não é uma quinta paleta — dobra os estados a provar em cada primitiva.
- **Qualquer mudança em `src/`.** Front puro. Se você mexeu no backend, saiu do escopo.
- **KPIs de produção.** Fase 6, e dependem de domínio das Fases 2 e 5.
- **Biblioteca de componentes de terceiros.**
- **Esc / clique-fora / focus-trap no modal** do `PedidoDetalhePage` — já estava fora na 1A e continua.
- **Migrar `Setor`/`Material` para `PaginaDto<T>`** — dívida de backend rastreada, com motivo escrito.
