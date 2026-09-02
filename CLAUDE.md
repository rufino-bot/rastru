# CLAUDE.md

Contexto para o Claude Code trabalhar neste repositório. Leia isto antes de qualquer
tarefa; para detalhes de domínio, arquitetura e roadmap, vá para a pasta `specs/`.

## Sobre o projeto

Sistema de rastreamento de peças dentro da fábrica: do cadastro do Pedido até a entrega
para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem por
Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

A fonte da verdade do domínio e das decisões já tomadas é a pasta `specs/`. Não
re-decida algo que já está resolvido lá sem perguntar antes.

## Stack

- **Backend**: .NET (C#), ASP.NET Core Web API
- **Frontend**: React + TypeScript (Vite), responsivo/mobile-first (uso em Android via navegador, sem PWA no MVP)
- **Banco**: SQL Server, on-premise
- **Auth**: login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador)
- **CI/CD**: nenhum ainda — deploy manual no MVP

## Mapa da pasta `specs/`

Leia o arquivo relevante para a tarefa em questão antes de codar — não é preciso carregar
todos de uma vez.

| Arquivo | Quando ler |
|---|---|
| `00-visao-geral.md` | Objetivo do sistema, escopo, perfis de usuário |
| `01-dominio-e-regras-de-negocio.md` | Antes de tocar em qualquer regra de negócio — glossário e regras numeradas |
| `02-modelo-de-dados.sql` | Fonte de verdade do schema do banco |
| `03-arquitetura-tecnica.md` | Estrutura de camadas, EF Core, autenticação, hospedagem |
| `04-fluxos-de-usuario.md` | Antes de implementar uma tela/fluxo — passo a passo por perfil |
| `05-api-endpoints.md` | Antes de criar/alterar um endpoint |
| `06-roadmap-mvp.md` | Para saber em que fase estamos e o que vem a seguir |

## Ordem de implementação

Seguir as fases de `06-roadmap-mvp.md` em sequência (Fase 0 → 6). Não implementar
funcionalidade de uma fase mais avançada antes da anterior estar concluída, mesmo que
pareça simples — a ordem existe para manter escopo fechado por etapa.

## Como este projeto executa plano — o gate de review não é opcional

Task de plano executa pelo fluxo de **`superpowers:subagent-driven-development`**, inteiro:

> **implementer → task review (conformidade com a spec + qualidade) → fix pass para
> Critical/Important → re-review** → só então a task é marcada completa. No fim da branch, a review
> de branch inteira, no modelo mais capaz.

**Invoque a skill.** Despachar implementer direto pela ferramenta de agente, sem o gate, é como a
qualidade se perde — e já se perdeu. O recorte exato, remedido em 2026-08-27 contra os artefatos do
ledger e o `git log`:

- **Tasks 1 a 6 da Fase 1C tiveram review.** A 1 tem "review APROVADA" registrada
  (`historico/05-fase-1c.md:681`), sem re-review; o `task-2-report.md` **é** o relatório de review da
  2; as 3 a 6 têm par `-review-brief` / `-review-report` em disco.
- **A Task 7 não teve review, e isso está certo:** delta de teste zero (457/457 em 3 execuções) e
  verificada ponto a ponto, com a decisão escrita no ledger na sessão da época
  (`historico/05-fase-1c.md:63-64`). Cai exatamente na segunda exceção da seção abaixo.
- **As Tasks 8 a 12 não tiveram nenhuma, e *não existe decisão registrada de parar*.** É esta a
  deriva: **15 commits, 2.711 linhas** já commitadas sem gate, achada pelo usuário em 2026-08-25.
  (O número que este parágrafo trazia antes — 3.009 linhas em 16 commits — media de a Task 7 em
  diante, e a 7 não é deriva.)

**A deriva foi fechada, e o fecho é o argumento a favor do gate:** as Tasks 10-12 receberam review
retroativa, que achou **8 Important** que a verificação do controlador não tinha achado; as Tasks 13
e 14 rodaram o fluxo inteiro. Na 13 a review achou duas afirmações falsas em spec que **nenhum teste
quebraria** (o delta de teste da task era zero); na 14, quatro rodadas seguidas acharam afirmação
inflando o rigor do processo na descrição de um PR que ia a repositório **público**.

### Pular a review exige justificativa ESCRITA, e de uma classe estreita

Não é proibido pular. É proibido pular **em silêncio**. A justificativa vai no ledger **e** no
relatório da task — se ela existe só na cabeça de quem decidiu, o próximo a retomar lê como deriva,
e é indistinguível dela. As classes que justificam:

- **Task cujo produto não é código.** Verificação manual, decisão de desenho, levantamento — o
  produto é o relatório, e revisar um relatório é lê-lo, não abrir um gate.
- **Delta zero verificado ponto a ponto** pelo controlador, com o "ponto a ponto" escrito.
  (Exemplo real, e o **único** desta fase: a Task 7 — ver acima.)

Fora dessas, a review acontece.

**Cuidado com a primeira classe: ela é a mais fácil de invocar errado.** Até 2026-08-27 este
parágrafo citava a Task 12 da Fase 1C como exemplo dela ("produto foi evidência de navegador e três
decisões"). A medição desmentiu: o relatório da Task 12 **não registra dispensa nenhuma**, e ela caiu
na review retroativa das 10-12 — ou seja, era deriva, não exceção, e a review posterior achou coisa
de verdade no escopo dela. A classe continua válida; o que não valia era o exemplo. **Se você for
invocar esta exceção, escreva a justificativa ANTES, no ledger e no relatório** — justificativa
reconstruída depois é indistinguível de racionalização.

### O que NÃO conta como review

- **A verificação do controlador.** Quem escreve o brief remede a mutação que mandou medir e confere
  contra as premissas que estabeleceu — mesmo ponto cego do implementer sobre o que ninguém pensou
  em olhar. É complemento, nunca substituto. A prova de que o ângulo externo acha o que o interno
  não acha está na Fase 1C: a verificação em navegador pegou um descumprimento de spec que **362
  testes verdes** não pegavam.
- **Review pequena e sem escopo definido.** O escopo é o diff da task, com a **base registrada antes
  de despachar o implementer** — nunca `HEAD~1`, que trunca task de múltiplos commits em silêncio.
  Gere o pacote com o `scripts/review-package BASE HEAD` da skill e passe o caminho ao revisor; o
  diff nunca entra no contexto do controlador.
- **Achado de review executado sem medir.** Recomendação de revisor se remede: três vezes na Fase 1C
  uma proposta concreta de review não sobreviveu à medição de quem foi executá-la.

## Banco de dados — regra importante

`specs/02-modelo-de-dados.sql` é a **fonte de verdade** do schema. O mapeamento do EF
Core é *Database First*: o schema nasce no `.sql`, não em migrations do EF. Se uma
tarefa exigir mudança de schema:

1. Alterar `02-modelo-de-dados.sql` primeiro.
2. Atualizar o mapeamento EF Core a partir do novo schema.
3. Refletir a mudança em `01-dominio-e-regras-de-negocio.md` se for regra de negócio.

Nunca deixe o EF Core criar/alterar tabelas por conta própria via `Add-Migration` a
partir do zero.

## Convenções de nomenclatura

- **Nomes de domínio em português**, espelhando exatamente o DDL: `Pedido`, `Agrupamento`,
  `EstruturaItem`, `Componente`, `Material`, `Setor`, `Usuario`, `Perfil`,
  `RelatorioDimensional`, `RelatorioDimensionalAvaliacao`, `Expedicao`, `Perda`, etc. Não
  traduza entidades de negócio para inglês — isso cria divergência entre código, banco e
  as specs.
- **Nomes técnicos/padrões de projeto em inglês**: `Repository`, `UseCase`, `DTO`,
  `Controller` (ex.: `PedidoRepository`, `AbrirRetrabalhoUseCase`).
- Siga a estrutura de camadas descrita em `03-arquitetura-tecnica.md`
  (`Domain` / `Application` / `Infrastructure` / `Api`).

## Interface (a partir da Fase 1D)

O padrão visual e de interação nasceu na Fase 1D e vale para **toda tela nova**. A spec de origem é
`docs/superpowers/specs/2026-08-06-fase-1d-ui-e-ux-design.md`.

- **Tela nova começa por `<Pagina titulo="…">`**, dentro da rota de layout do `AppShell` em
  `web/src/App.tsx`. Não escreva container próprio, e nunca `min-h-screen` numa tela — quem faz isso
  é o shell. As duas exceções são `LoginPage` e `TelaCarregando`, ambas por renderizarem **fora**
  do shell (a segunda substitui o `AppShell` inteiro enquanto a sessão ainda carrega, em
  `ProtectedRoute`) — não numa terceira tela nova.
- **Não escreva campo, botão, banner de erro, item de lista, pílula, paginação, estado vazio ou
  estado de carregando à mão.** As primitivas estão em `web/src/components/` (`EstadoCarregando`
  inclusive). Se faltar uma, crie-a lá com teste próprio — não a embuta na tela.
- **Escolher um item de catálogo paginado usa `SeletorComBusca`** (`web/src/components/`, com
  teste próprio), não um `<select>` com a lista inteira — que não escala quando o catálogo tem mais
  itens do que cabe numa página. O gatilho é esse: catálogo paginado. Hoje tem **um** consumidor
  (a receita padrão em `ComponenteDetalhePage`) — o bastante para nomear a primitiva certa, não
  para chamá-la de padrão já consolidado em várias telas.
- **Cores só pelos tokens** de `web/src/index.css` (`text-tinta`, `bg-acao`, `border-borda`…).
  `text-gray-*`, `text-red-600` e afins não existem mais em `web/src/`. Isto é **guarda executável**,
  não só varredura pontual: `web/src/tema/semCorForaDaPaleta.test.ts` varre `web/src/` inteiro atrás
  de classe da paleta padrão do Tailwind (inclusive variantes direcionais, `border-t-` etc.) e falha
  nomeando arquivo e linha — cai na suíte normal, então uma cor fora do token não passa despercebida
  numa tela nova (planos e specs em `docs/superpowers/` citam os nomes antigos ao narrar o histórico,
  o que é esperado e fora do escopo da guarda).
- **Tom novo tem de ser medido antes de entrar.** `web/src/tema/contraste.test.ts` lê o `@theme` do
  `index.css` e reprova token sem par de contraste declarado. Os tons claros de verde-água e âmbar
  reprovam AA como texto sobre branco apesar de funcionarem como fundo de botão — a regra existe
  por causa disso.
- **O documento declara `lang="pt-BR"`.** `web/index.html` nasceu com o `lang="en"` do template do
  Vite, o que viola a WCAG 3.1.1 (Language of Page) numa interface escrita inteira em português —
  leitor de tela pronuncia o conteúdo com fonética do inglês, e a tradução automática do navegador
  trata a página como inglesa. `web/src/tema/idiomaDaPagina.test.ts` lê o `index.html` real de disco
  e reprova quem trocar o atributo por qualquer coisa que não seja exatamente `pt-BR` (a grafia
  canônica do BCP 47 — `pt-br` e `PT-BR` também reprovam, de propósito). É **guarda, não
  varredura**, e a diferença foi medida em 2026-08-28: com `lang="en"` no arquivo, os 374 testes de
  então ficavam **verdes**, porque teste de tela monta componente no jsdom e nunca olha o documento
  que hospeda o React.
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

## Invariantes de negócio que não podem ser violadas

(resumo — ver `01-dominio-e-regras-de-negocio.md` para a lista completa)

- Um `EstruturaItem` (lote) é **divisível por quantidades livres**: pode ter quantidades em
  Setores diferentes ao mesmo tempo. Não há identidade de sub-lote (sem serial). O
  invariante a preservar é **conservação de quantidade**: soma em Setores + expedido
  (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça (validado na aplicação).
- `EstruturaItem` é recursivo: nó sem pai = **Peça**, nó com pai = **Item**. Não crie
  tabelas separadas para Peça e Item.
- Reprovação no Relatório Dimensional **não** gera Retrabalho automaticamente — é uma
  ação separada e opcional do usuário (perfil Qualidade), com `MotivoRetrabalho`
  obrigatório quando aplicada. O mesmo vale para **perda**: registrar a perda não abre
  Retrabalho sozinho.
- Um Pedido só é concluído quando o **último** Agrupamento dele é concluído (e um Agrupamento,
  quando todas as suas Peças concluem — toda a quantidade expedida ou perdida).
- Rastreamento é por **lote agregado**, nunca por unidade física individual (serial).
- **Falha de autenticação é sempre genérica.** Login responde `"Usuário ou senha inválidos."` e
  refresh responde `"Refresh token inválido ou expirado."` — em **todos** os caminhos de falha,
  inclusive conta trancada (mesmo com a senha certa) e reuso de refresh token detectado. Variar
  corpo, status ou tipo de erro por condição vira oráculo de enumeração.
- **O BCrypt roda sempre no login**, inclusive para usuário inexistente, inativo ou trancado
  (`IPasswordHasher.HashFicticio`). Nenhum `return` antecipado antes da verificação de senha.

## Prefixo `/api`

A API é servida **somente** sob `/api`: uma guarda em `Program.cs`, **logo acima** do
`app.UsePathBase("/api")`, devolve 404 quando `Request.Path` não começa por `/api` (comparação
ordinal — `/API/setores` também recebe 404, para não gravar o cookie de refresh sob um `Path` de
casing diferente). A guarda testa `Path`, não `PathBase`: sob sub-application/virtual directory do
IIS o host já entrega `PathBase` preenchido em toda requisição, e um predicado sobre `PathBase`
deixaria `/setores` passar sem prefixo nenhum — só ler o `Path`, antes do `UsePathBase` tirar o
prefixo dele, cobre esse caso. O front nunca escreve o prefixo à mão: quem o aplica é o `rota()` de
`web/src/api/client.ts`, e chamada nova passa o caminho **sem** prefixo (`/setores`) — escrever
`/api/...` no call site duplicaria.

Existe por uma colisão real: as rotas do SPA (`/setores`, `/materiais`, `/pedidos`, `/pedidos/:id`)
têm os mesmos caminhos dos endpoints, e sem prefixo dar F5 numa dessas telas faz o navegador pedir o
**documento** à API, que responde 401 (navegação não carrega `Authorization: Bearer`). Aconteceu no
e2e da Fase 1A.

**Fechado em 2026-08-04.** Até então `UsePathBase` respondia nos dois caminhos — ele não é branch,
tira o prefixo quando existe e deixa passar quando não existe — e enquanto os caminhos nus
respondessem a colisão de produção continuava de pé. A guarda fechou isso e as 125 URLs literais dos
testes de endpoint foram reescritas junto. O `Path` do cookie de refresh acompanha o `PathBase`
(`/api/auth`) em vez de ser literal, para o cookie ser gravado sob o mesmo prefixo em que o refresh
atende. Coberto por `AuthEndpointsTests.PrefixoDeApi.cs`.

**Restrição de ordem de pipeline:** a guarda é um 404 cego para tudo que não começa com `/api`. Se
o SPA vier a ser servido como estáticos pela própria API (`UseStaticFiles` / `MapFallbackToFile` —
ver hospedagem em `specs/03-arquitetura-tecnica.md`), o registro deles tem que vir **antes** da
guarda, senão ela devolve 404 para `index.html`, os assets e toda rota do SPA, com sintoma de "o
build do front não subiu". Hoje não se aplica: não há `UseStaticFiles` em `src/`.

## O que evitar (decisões já descartadas — não reabrir sem justificativa nova)

- Windows Authentication (decidido: login próprio + JWT)
- PWA/offline no MVP (decidido: não necessário agora)
- Rastreamento por serial individual (decidido: lote agregado)
- Criar Peça e Item como tabelas separadas (decidido: tabela recursiva única)
- Roteiro de setores fixo por tipo de peça (decidido: pode variar por pedido/agrupamento)

## Comandos

Antes de qualquer coisa, para saber onde o checkout está — branch, HEAD, upstream, árvore suja e
Docker, medidos, não lembrados:

```bash
bash scripts/estado
```

`--testes` acrescenta as suítes (caro: front ~15s, backend ~2min). Ele **observa e não age**: não
sobe o banco, porque ação com efeito colateral não pertence a um script cujo propósito é medir. Um
hook `SessionStart` (em `.claude/settings.json`) já roda a forma barata na abertura da sessão e
injeta o resultado como contexto.

`.claude/settings.json` também versiona `worktree.bgIsolation: "none"`. A review de 2026-08-25
sugeriu separá-lo como "preferência de máquina" — **não é**, e por isso ele fica: é o que permite
subagente em segundo plano escrever no checkout principal, e o fluxo deste projeto é exatamente
esse (implementer, fix pass e review despachados sobre a árvore de trabalho). Com o padrão
`"worktree"`, todo agente em background pararia antes de editar. Quem clonar o repositório e usar o
mesmo fluxo precisa dos dois; por isso o arquivo é versionado inteiro. O que **não** entra nele é
credencial ou caminho de máquina — para isso existe `.claude/settings.local.json`.

### O ledger tem repositório próprio, e o script vigia isso

`.superpowers/` — ledger, briefs, relatórios, histórico de fase — é um **repositório git separado e
privado** (`rufino-bot/rastru-ledger`), desde 2026-08-25. Não é submodule: são dois repos
independentes na mesma árvore, e este aqui ignora `.superpowers/` na raiz (`.gitignore:6`). Nada do
código muda por causa disso.

Existe porque aquele conteúdo é o **único** registro das decisões, medições e dívidas do projeto, e
até então vivia no disco de uma máquina só. Ficam de fora dele os pacotes `.diff` de review (o
nome de cada um é o par de SHAs — `git diff A..B` reconstrói) e o estado de runtime do brainstorm.

`scripts/estado` confere duas coisas na abertura, porque as duas falhas desta cópia são
**silenciosas** e só apareceriam no dia de trocar de máquina:

1. **trabalho sem commit ou sem push** — registro que existe só localmente não é backup;
2. **`sdd/.gitignore` recriado com `*`** — ele é redundante aqui (a raiz já ignora a pasta) e
   destrutivo lá, onde ignora o ledger inteiro. Já aconteceu **três vezes**. A primeira foi pega
   com `git check-ignore -v` antes do primeiro `git add`, senão o repositório teria nascido vazio
   de conteúdo; a de **2026-09-02** deu o mecanismo, medido e não inferido:
   `scripts/sdd-workspace:21` da skill `subagent-driven-development` faz
   `printf '*\n' > "$dir/.gitignore"` **incondicionalmente**, e é chamado tanto por `task-brief`
   quanto por `review-package`. Ou seja: o arquivo volta **a cada task** que gere brief ou pacote
   de review, não de vez em quando. A premissa do script (o comentário dele trata o diretório como
   scratch descartável do repo do projeto) era verdadeira até 2026-08-25 e deixou de ser.

   **Conferir só na abertura não basta**, e isso também foi medido: a ocorrência de 09-02 nasceu
   às 07:39, depois da abertura das 07:25, no primeiro `review-package` da sessão. Por isso existe
   **`scripts/desarma-gitignore-do-sdd`**, ligado a dois hooks em `.claude/settings.json`
   (`PostToolUse` de `Bash`, e `SubagentStop` para o caso de o subagente ter rodado o script). Ele
   apaga o arquivo quando o conteúdo é **exatamente** `*`, e **não toca** em nenhum outro conteúdo
   — exclusão deliberada que alguém escreva ali sobrevive. Os três casos (positivo, conteúdo
   deliberado, arquivo ausente) foram exercitados quando o script nasceu, e o hook foi visto
   disparar ao vivo. O `scripts/estado` continua conferindo na abertura, como segunda rede.

   **O modo de falha é silencioso, e é por isso que a guarda é automática em vez de um lembrete:**
   `git add -A` pula untracked ignorado sem reclamar. Um `git add` explícito falha alto — foi o que
   salvou em 09-02 —, mas isso é sorte da forma do comando, não guarda.

Backend (solution `Rastreamento.slnx`, na raiz):

```bash
docker compose up -d          # PRÉ-REQUISITO dos testes de integração (ver abaixo)
dotnet build Rastreamento.slnx -warnaserror    # o build tem que ficar em 0 warnings
dotnet test  Rastreamento.slnx                 # suíte inteira
```

Um projeto de teste por vez, enquanto se itera:

```bash
dotnet test tests/Rastreamento.Domain.Tests           # (vazio por enquanto)
dotnet test tests/Rastreamento.Application.Tests      # casos de uso, com fakes — não precisa de banco
dotnet test tests/Rastreamento.Infrastructure.Tests   # hashers + mapeamento EF (parte precisa de banco)
dotnet test tests/Rastreamento.Api.Tests              # ponta a ponta (a maior parte precisa de banco)
```

Frontend: ainda não criado (Fase 1 em diante).

### Pré-requisito externo dos testes

Parte da suíte roda contra o **SQL Server real**, não contra banco em memória — é o que
prova o mapeamento EF, os lifetimes do DI, a atomicidade da rotação de refresh token, a
queima da família de tokens no reuso e o lockout de conta ponta a ponta.

**O banco de dev é descartável** (autorização do dono do projeto, 2026-08-17): pode ser
derrubado, regenerado e populado à vontade. A restrição antiga que vivia neste parágrafo — "não
derrubar o SQL Server" — **não vale mais**, e não é motivo válido para deixar uma contagem sem
reconferir.

A suíte tem **464 testes** (medido em 2026-08-26 com `dotnet test Rastreamento.slnx
--list-tests`, que só descobre os testes por reflexão — não os executa, e não precisa do banco no
ar: 205 em `Api.Tests`, 201 em `Application.Tests`, 58 em `Infrastructure.Tests`). Quantos
exatamente **precisam** do SQL Server (em vez de fake) **não foi remedido nesta passada** — isso
exigiria rodar a suíte de verdade (não só descobrir os testes) com o banco fora do ar e ver o que
falha por erro de conexão em vez de mensagem útil, e a task que escreveu este parágrafo é
documentação pura, sem delta de teste, então não executou a suíte para medir isso. O que se sabe
por leitura de código, sem precisar rodar nada: `Application.Tests` usa fakes e não precisa de
banco (nenhum teste seu); `Infrastructure.Tests` e `Api.Tests` precisam **em parte**, sem a fração
medida.

**Paralelismo do xUnit contra um banco só.** O xUnit roda classes de teste em paralelo dentro do
mesmo assembly, e o SQL Server de dev é compartilhado — duas classes que escrevem na **mesma
tabela** interferem uma na outra. Isso já produziu dois flakies nesta fase; o segundo
(`ComponenteMappingTests.Busca_em_branco_nao_filtra_nada`, que compara duas contagens sem escopo)
falhava em **4 de 10** execuções da suíte de Infrastructure, nos dois sentidos: insert concorrente
inflando o total e limpeza concorrente derrubando-o. A regra que saiu disso: **teste novo que
escreve numa tabela já escrita por outra classe entra na mesma `[Collection]` daquela tabela** —
hoje `ColecaoQueEscreveEmComponente` (`tests/Rastreamento.Infrastructure.Tests/Persistence/`),
aplicada a `ComponenteMappingTests`, `ReceitaPadraoMapeamentoTests` e
`ReceitaPadraoRepositoryTests`. Serializar só as classes que disputam a tabela, e não o assembly
inteiro: o resto da suíte não paga por isso. Escopar a asserção (prefixo por teste) é a alternativa
e é melhor quando dá — só não dá quando o que se afirma é uma contagem global.

**`[Collection]` não atravessa processo — medido em 2026-08-22.** A mitigação acima não bastou:
`Busca_em_branco_nao_filtra_nada` continuou intermitente (4 vermelhas em 20 execuções da solução)
porque quem escrevia na janela entre as duas consultas era **outro assembly** — `Api.Tests`, em
outro processo — e `[Collection]` só serializa classes dentro do mesmo assembly. Reproduzido em
ambiente controlado (escrita concorrente em `dbo.Componente` durante o teste): **11 vermelhas em 30**
antes (~37%, em duas rodadas de 10 e 20), **0 em 40** depois, na mesma bancada. O conserto foi **escopar a asserção**: o teste afirma sobre as linhas do
próprio prefixo (`Itens` de uma página só, `Tamanho = int.MaxValue`, para a linha do prefixo nunca
cair fora da página) em vez de comparar dois `Total` globais. A regra geral que fica: **asserção
sobre contagem global de tabela compartilhada é flaky por construção** — nenhuma `[Collection]`
resolve, porque o outro escritor pode estar em outro processo. Escope, ou afirme só o que é monótono
(`Total >= n` das linhas que o próprio teste inseriu).

```bash
docker compose up -d
# aplicar, uma vez, no banco `Rastreamento` de localhost:1433 (sa / Your_strong_Pass123):
#   specs/02-modelo-de-dados.sql   (schema — fonte de verdade)
#   db/seed.sql                    (perfis + usuário admin / Admin@123)
#   db/seed-demo.sql               (OPCIONAL — massa de demonstração, Fase 1C)
```

**`db/seed-demo.sql` é opcional e não obrigatório** — a suíte **ignora que ele existe**, e é isso que
a torna determinística numa máquina qualquer (`grep -rn "seed-demo"` em `src/` e `tests/` devolve
zero). Mas quem regenerar o banco **sem** ele perde a massa de verificação manual (54 Componentes,
17 Materiais, 10 Setores, 3 receitas completas) e vai achar que a tela está quebrada quando ela só
está vazia. A dependência é de **mão única**: a verificação manual usa o demo, a suíte não.

É idempotente (`MERGE` no catálogo, `INSERT…WHERE NOT EXISTS` nas receitas, sempre chaveado por
código/nome — nunca por Id), então rodá-lo de novo num banco que já o tem não duplica nada.
**Carregue-o com `-f 65001`**, e por `docker cp` + `-i` (o compose não monta o repo dentro do
container): a acentuação dos nomes de setor é `NVARCHAR` e, se a codepage se perder na carga, os
dados entram corrompidos **e o banco fica verde assim mesmo**. O teste de que sobreviveu não é
olhar: é medir o code point (`Rebarbação` tem `LEN = 10`; lido como Latin-1 daria 12).

**Banco regenerado em 2026-08-04.** Ele foi recriado do zero (`DROP DATABASE` +
`specs/02-modelo-de-dados.sql` + `db/seed.sql`) porque estava em desacordo com a fonte de
verdade: `dbo.Componente` não tinha `ArquivoSolido`/`ArquivoFoto`, que nasceram no schema depois
de o banco ter sido criado. Consequência: **os quatro blocos de `ALTER` idempotente abaixo viraram
no-op nesta máquina.** Eles continuam corretos, e necessários, para quem tiver banco anterior a
essa data — não os remova.

Num banco que já existia antes do hardening de auth, as colunas de lockout entram por `ALTER`
(o `.sql` é script de criação). É idempotente — e o `MSYS_NO_PATHCONV=1` é o que impede o Git Bash
de traduzir o caminho do `sqlcmd` dentro do container:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Usuario','FalhasConsecutivas') IS NULL ALTER TABLE dbo.Usuario ADD FalhasConsecutivas INT NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0), BloqueadoAte DATETIME2 NULL;"
```

Na Fase 1A entram as colunas de autoria, também por `ALTER` idempotente em banco pré-existente
(cobre as duas tabelas — `Pedido` e `Agrupamento` — porque é isso que o script de Task 1
efetivamente aplica):

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Pedido','CriadoPorUsuarioId') IS NULL ALTER TABLE dbo.Pedido ADD CriadoPorUsuarioId INT NOT NULL CONSTRAINT FK_Pedido_CriadoPorUsuario FOREIGN KEY REFERENCES dbo.Usuario(Id); IF COL_LENGTH('dbo.Agrupamento','CriadoPorUsuarioId') IS NULL ALTER TABLE dbo.Agrupamento ADD CriadoPorUsuarioId INT NOT NULL CONSTRAINT FK_Agrupamento_CriadoPorUsuario FOREIGN KEY REFERENCES dbo.Usuario(Id), CriadoEm DATETIME2 NOT NULL CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT (SYSUTCDATETIME());"
```

Ainda na Fase 1A, o fix pass da Task 12 acrescenta um segundo usuário ao seed (achado B11: com
um único usuário no banco, a prova de autoria no nível HTTP era degenerada — um literal `1` no
lugar de `usuarioId.Value` coincidia com o Id do único usuário e passava). Também idempotente,
mesmo padrão do `IF NOT EXISTS` do `admin` em `db/seed.sql`:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'pcp') INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo) SELECT 'pcp', '\$2a\$11\$gbL2eZQIk1S1zAYieDUJO.Um1Sbom9oC56Xpd3RcdYdIYRDygpSuG', 'Planejamento e Controle', (SELECT Id FROM dbo.Perfil WHERE Nome = 'PCP'), 1;"
```

**Terceiro usuário, `operador`/`Admin@123`, perfil `Operador`, sem permissão de escrita, existe só
no banco desta máquina — deliberadamente FORA de `db/seed.sql`.** Criado à mão em 2026-08-25 para o
V5 da Task 12 da Fase 1C (gating por perfil) ser verificável: `admin` e `pcp`, os dois usuários do
seed acima, **escrevem** — sem um usuário de perfil sem-escrita no banco não dá para provar que a
AÇÃO some para quem não pode escrever (ver seção Interface, "Gating de perfil vai na AÇÃO"). Ele
reusa o hash BCrypt do próprio `admin` (`db/seed.sql:12`, válido para `Admin@123`). **Decisão da
Task 13 (documentação): não entrou no seed.** Razão: `db/seed.sql:17-21` registra que o `pcp`
recebeu hash e senha distintos do `admin` de propósito no seed, "para não virar mina para quem
tentar logar depois" — versionar `operador` reusando o hash do `admin` sob outro nome de usuário
quebraria esse padrão. Consequência: **a próxima verificação manual de gating por um perfil sem
escrita** (Operador, Almoxarifado, Qualidade ou Gestão) **precisa recriar este usuário à mão de
novo**, no mesmo padrão idempotente do bloco `pcp` acima — reusando o hash do `admin`, como da
primeira vez — este parágrafo existe para que essa recriação não parta do zero, redescobrindo por
que nenhum usuário sem-escrita está disponível.

Ainda na Fase 1A, decisão de domínio: `Agrupamento.Quantidade` saiu do schema — um Agrupamento é
composto por N Peças, e a contagem de Peças já responde "quantas são"; indicar uma quantidade no
Agrupamento era redundante. A quantidade com significado no domínio é `EstruturaItem.Quantidade`
("lote agregado, divisível por quantidades livres", Fase 2, intocada). `DROP COLUMN` idempotente:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Agrupamento','Quantidade') IS NOT NULL ALTER TABLE dbo.Agrupamento DROP COLUMN Quantidade;"
```

Na Fase 2 entra a constraint que fecha a regra 18 no schema. **Diferente dos quatro blocos acima,
este NÃO é no-op nesta máquina:** o banco foi regenerado em 2026-08-04 a partir do `.sql`, e naquela
data a constraint ainda era comentário.

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EstruturaItem_PecaTemComponente') ALTER TABLE dbo.EstruturaItem ADD CONSTRAINT CK_EstruturaItem_PecaTemComponente CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL);"
```

O schema **não** é criado pelo EF (nada de `Add-Migration`/`EnsureCreated`): é Database
First, o `.sql` é a fonte de verdade.

### Defesas de autenticação em vigor

- **Trabalho constante + 401 genérico** no login e no refresh: nos três caminhos de falha do login
  (usuário inexistente, inativo/trancado, senha errada) o BCrypt roda sempre, contra um hash de
  mesmo custo, e o corpo da resposta é idêntico. Residual aceito, e **não corrigível em
  princípio**: a escrita no banco não é uniforme — só senha errada numa conta existente, ativa e
  destrancada faz `UPDATE` (`Senha_errada_incrementa_o_contador_e_persiste` afirma `Saves == 1`;
  `Usuario_inexistente_nao_escreve_nada` e `Tentativa_em_conta_trancada_nao_estende_a_trava`
  afirmam `Saves == 0`). A banda é baixa (~1–3 ms de `UPDATE` contra ~100–150 ms de BCrypt fator
  11) e não tem correção possível: não há como escrever uma linha de contador para um usuário que
  não tem linha.
- **Reuso de refresh token detectado:** reapresentar um token já rotacionado revoga **todos** os
  refresh tokens ativos daquele usuário e responde o mesmo 401 genérico. Limite inerente: só
  detecta quando o token **antigo** reaparece — se o atacante roubar o token atual e o legítimo
  nunca replayar o anterior, a defesa é a expiração natural do refresh.
- **Lockout de conta:** `Lockout:MaxFalhas` (5) falhas consecutivas trancam por
  `Lockout:DuracaoMinutos` (15). Cada trava expira sozinha, mas **retrancar não tem limite**: quem
  sabe o nome de usuário (inclusive `admin`) manda 5 senhas erradas a cada 15 min — 20
  requisições/hora contra um orçamento de rate limit de 600/hora (10 por minuto, ~3% dele) — e
  segura a conta trancada indefinidamente, de um único IP. O rate limit não cobre esse padrão
  porque a janela dele é curta (segundos), não de ciclos de 15 minutos; hoje não existe caminho de
  desbloqueio administrativo. Isso é inerente a lockout por contador (a própria OWASP registra o
  trade-off) — o desenho está certo, o que estava documentado errado aqui era o limite do dano.
  Concorrência: com N tentativas simultâneas o contador pode sub-contar em até N−1 (proporcional à
  concorrência, não um simples off-by-one), e existe a race reversa — um login concorrente
  bem-sucedido que leu a linha antes de uma falha travá-la grava `BloqueadoAte = null` depois,
  apagando uma trava recém-criada; benigno, porque quem venceu a race provou a senha certa. O que
  **não** acontece: uma trava que nunca libera. O timestamp da trava é capturado antes do BCrypt
  rodar, então uma requisição atrasada só consegue estender a trava pela duração dela mesma, nunca
  travar por mais tempo que isso.
- **Rate limit por IP no `/auth/login`:** `RateLimit:PermitLimit` (10) por
  `RateLimit:WindowSeconds` (60), janela fixa, 429 com `Retry-After`. O `/auth/refresh` fica de
  fora de propósito — ver `specs/05-api-endpoints.md`, que registra a isenção e a consequência dela
  (achado ainda em aberto, pendente de decisão). Em `appsettings.Development.json` o limite é
  folgado — no `TestServer` o IP é nulo e toda a suíte cairia numa partição só.
- **Logging de auth via `ILogger`** (não há tabela de auditoria persistente): login ok/falha e
  refresh ok/falha com IP no `AuthController`; trava de conta e reuso de refresh nos casos de uso;
  429 no `OnRejected`. **Nunca** se loga senha, refresh token (plano ou hash) nem access token.

### Trade-offs conhecidos de autenticação

**Logout não invalida o access token.** O logout revoga o refresh token no banco, mas o
access token é JWT stateless e o `/me` responde só a partir das claims, sem ida ao banco.
Ou seja: depois do logout — ou de desativar um usuário, ou de a família de tokens ser queimada por
reuso — a sessão ainda funciona até o access token expirar (`Jwt:AccessTokenMinutes`, hoje 15 min).
É o comportamento padrão de JWT stateless e está aceito no MVP; se um dia precisar ser imediato, a
saída é uma denylist de tokens ou validação por requisição, não um remendo no logout.

**Queima de família por gatilho benigno (custo aceito, não é bug).** A detecção de reuso
(reapresentar um refresh já rotacionado queima toda a família de sessões do usuário) reage do
mesmo jeito a um roubo de verdade e a três cenários legítimos — não há como distinguir os dois
casos pelo request isolado, e não se quer distinguir (ver `ConflitoDeConcorrenciaException` e o
`RowVersion` em `RefreshToken`, que fecham a corrida entre a queima e uma rotação em voo):

- **Retry com resposta perdida** — o mais provável no wifi da fábrica: o cliente rotaciona,
  o servidor processa e revoga o token antigo, mas a resposta (com o token novo) se perde na
  rede; o cliente reenvia o mesmo request com o token antigo, que agora já está rotacionado.
- **Duplo refresh concorrente** — duas chamadas a `/auth/refresh` com o mesmo cookie saem
  antes que a primeira rotacione; a segunda apresenta um token que a primeira já rotacionou.
  É exatamente por isto que o cliente HTTP do frontend **deve** fazer single-flight do refresh
  (ver `03-arquitetura-tecnica.md`).
- **Replay pós-logout com 204 perdido** — o cliente desloga, a revogação é aplicada no banco,
  mas a resposta 204 se perde; o cliente (ou uma aba antiga) reapresenta o mesmo refresh token.

Nos três casos o usuário é deslogado de todos os dispositivos e precisa logar de novo. É o custo
aceito da detecção de reuso no MVP — não existe janela de graça (ex.: permitir o token antigo por
alguns segundos após a rotação) porque isso reabriria a mesma corrida que o `RowVersion` fecha.

**Rate limit atrás de proxy reverso.** A partição usa `RemoteIpAddress`. Se entrar um proxy na
frente da API, configurar `ForwardedHeaders` — senão todos os clientes compartilham o IP do proxy e
o limite vira global por acidente. Flag de deploy, ainda não necessária (deploy manual, sem proxy).

**Ainda em aberto (deferido de propósito):** tabela de auditoria persistente; limpeza de linhas
`RefreshToken` expiradas; `SigningKey` como segredo de ambiente e `UseHttpsRedirection`; mensagem
dedicada de 429 no front (hoje cai no erro genérico de auth — só dispara sob abuso).
