# Fase 1C — Receita padrão do Componente

Spec de design. Escrita em 2026-08-17, a partir do brainstorm da mesma data, logo depois de o
PR #7 (dívida I2/I3 da 1B) ser mesclado na `main`.

**Pré-requisito:** PR #7 mesclado — `origin/main` em `7757806`. Confirmado no disco antes de
escrever esta spec. A branch desta fase (`fase-1c-receita-padrao`) sai dessa `main`.

**O que esta fase destrava:** a Fase 2 cria `EstruturaItem` *copiando a receita padrão*. Enquanto
a 1C não existir, ela não tem o que copiar (`specs/06-roadmap-mvp.md:34-36`).

---

## 0. O que já estava decidido antes deste brainstorm

Nada abaixo foi re-decidido aqui. Está listado porque a spec precisa ser lida sem o ledger ao lado.

- **As 3 tabelas já existem no schema** (`specs/02-modelo-de-dados.sql:107+`), com
  `UQ_ComponenteFilhoPadrao (ComponentePaiId, ComponenteFilhoId)`,
  `CK_ComponenteFilhoPadrao_NaoAutoReferencia`,
  `UQ_ComponenteMaterialPadrao (ComponenteId, MaterialId)` e
  `UQ_ComponenteRoteiroPadrao (ComponenteId, Ordem)`.
  **Database First: o schema não nasce do EF.**
- **As 3 rotas já estão especificadas** (`specs/05-api-endpoints.md:75-77`), marcadas
  "Fase 1C, não implementado":
  `GET/POST /componentes/{id}/filhos-padrao`, `.../materiais-padrao`, `.../roteiro-padrao`.
- **O caso de uso aceita uma LISTA de linhas de uma vez**, não uma linha por chamada
  (`specs/06-roadmap-mvp.md:198-201`). Motivo registrado: *"é quase de graça agora e evita
  reescrever o caso de uso quando o import [de CAD] chegar; a tela continua digitando linha a
  linha."*
- **A 1C copia o molde de cadastro** (`CadastroControllerBase` + os `CadastroDe*UseCase`). Foi por
  isso que a dívida I2/I3 tinha prazo "antes da 1C" — o molde ia ser triplicado.

---

## 1. Decisões deste brainstorm

Seis decisões do usuário, em 2026-08-17. Cada uma com o custo que ela aceita.

### 1.1 Escopo: backend **e** tela

A fase entrega as 3 rotas em `src/` **e** a tela que as usa. É o critério de pronto que a Fase 1
usou em 1A e 1B ("CRUD pela tela"), e é o que faz a Fase 2 ter dado real para copiar — sem tela, a
receita só nasceria por `INSERT` manual no banco.

### 1.2 O `POST` **substitui a receita inteira**

`POST` significa *"a receita deste componente passa a ser EXATAMENTE estas N linhas"*. Não
acrescenta.

Por quê: é idempotente; **remoção sai de graça** (linha ausente = removida), o que importa porque
a spec de endpoints não prevê `DELETE` para estes sub-recursos; reordenar o roteiro deixa de brigar
com o `UQ (ComponenteId, Ordem)`; e o import de CAD encaixa direto, porque ele gera a proposta
inteira, não um diff.

**Custo aceito:** dois usuários editando o mesmo componente ao mesmo tempo — o último a salvar
apaga o trabalho do primeiro. Sem trava de concorrência nesta fase (ver §6).

### 1.3 Ciclo na receita é **barrado na gravação**

`CK_ComponenteFilhoPadrao_NaoAutoReferencia` impede `A → A`. Ele **não** impede `A → B → A`, e o
banco aceita gravar esse ciclo hoje.

A Fase 2 copia a receita **recursivamente**. Um ciclo de dois níveis vira recursão infinita nela.
Então o caso de uso de filhos-padrão caminha o grafo antes de gravar e recusa se houver ciclo **em
qualquer profundidade**.

**A verificação é sobre o grafo COMO ELE FICARIA depois da substituição**, não sobre o grafo atual
mais as linhas novas. A distinção é real e vem direto de §1.2: como o `POST` substitui a receita
inteira, ele pode **remover** uma aresta — e uma substituição que desfaz um ciclo preexistente tem
de ser **aceita**, não recusada. Validar contra o grafo atual deixaria o usuário preso: o único
jeito de consertar um ciclo seria por `SQL` na mão.

A validação é exclusiva de **filhos-padrão**. Materiais e roteiro não formam grafo — apontam para
`Material` e `Setor`, que não têm receita.

Por que agora e não na Fase 2: esta é a única hora em que dá para barrar na entrada. A Fase 2
herdaria um grafo já sujo e teria de tratar dado inválido que não criou — e o usuário só descobriria
o ciclo ao tentar montar a árvore.

### 1.4 O `Tipo` do Componente **não** restringe a receita

`Componente.Tipo` (`Bruto | Fabricado | Montagem`) continua sendo rótulo descritivo. Qualquer
componente pode ter as três receitas.

Por quê: menos regra para errar, e não trava um caso real da fábrica que a spec não conhece (um
`Bruto` que passa por um setor de corte, por exemplo).

**Custo aceito e nomeado:** nada impede cadastrar uma `Montagem` sem filho nenhum.

### 1.5 A receita se edita numa **página de detalhe do Componente**

Rota nova `/componentes/:id`, com as três receitas empilhadas. Copia o molde de
`PedidoDetalhePage` (`/pedidos/:id` listando agrupamentos), que já existe e já passou pela 1D.

Descartado: abas (primitiva nova, com foco por teclado e ARIA próprios) e modal a partir da lista
(F5 perde onde o usuário estava, que é justamente o que a rota resolve).

### 1.6 Escrita para **Administrador e PCP**

Os mesmos perfis do próprio `Componente` — quem cadastra a peça é quem conhece a receita dela.
Leitura liberada a qualquer autenticado, como nos outros catálogos.

**Consequência que importa para a dívida A3:** o `POST` é a **única** ação de escrita destes
recursos, então a guarda de perfis (que compara a **união** dos perfis de um endpoint) continua
exata. O gatilho de A3 — uma regra do tipo *"PCP cadastra, só Administrador remove"*, que a guarda
não consegue expressar — **não dispara nesta fase**.

---

## 2. Backend

### 2.1 Camadas

Segue `specs/03-arquitetura-tecnica.md`. Nada de estrutura nova.

| Onde | O que nasce |
|---|---|
| `Domain/Entities/` | `ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao` |
| `Domain/Abstractions/` | **um** `IReceitaPadraoRepository` |
| `Infrastructure/Persistence/` | `ReceitaPadraoRepository` + 3 arquivos em `Configurations/` |
| `Application/Cadastros/` | `ReceitaPadraoUseCase` + DTOs |
| `Api/Controllers/` | `ReceitaPadraoController` (novo) |

**Por que um repositório e não três.** As três tabelas têm a mesma forma de acesso — ler por
`ComponenteId`, substituir por `ComponenteId`. E a validação de ciclo (§1.3) precisa caminhar o
grafo de filhos por **vários** componentes, não só pelo da linha sendo gravada; num repositório
dedicado a uma tabela, esse caminhamento ficaria separado de onde a regra vive. Três repositórios
triplicariam o boilerplate sem ganhar fronteira nenhuma.

**Por que um controller novo e não engordar o `ComponentesController`.** Ele iria de 4 para 10
ações. O precedente de sub-recurso em controller próprio já existe: `AgrupamentosController`
atende `pedidos/{pedidoId:int}/agrupamentos` **e** `agrupamentos/{id:int}`, declarando o caminho
completo por ação em vez de um `[Route]` de classe. `ReceitaPadraoController` segue esse molde.

### 2.2 Contrato

Resposta em **array nu**, não `PaginaDto<T>` — é o que `GET /pedidos/{id}/agrupamentos` já faz, e
receita não pagina (é curta por natureza).

```
GET  /componentes/{id}/filhos-padrao     → [{ id, componenteFilhoId, codigo, descricao, quantidadePadrao }]
POST /componentes/{id}/filhos-padrao     ← { linhas: [{ componenteFilhoId, quantidadePadrao }] }

GET  /componentes/{id}/materiais-padrao  → [{ id, materialId, codigo, descricao, unidadeMedida, quantidadePadrao }]
POST /componentes/{id}/materiais-padrao  ← { linhas: [{ materialId, quantidadePadrao }] }

GET  /componentes/{id}/roteiro-padrao    → [{ id, setorId, nome, ordem }]
POST /componentes/{id}/roteiro-padrao    ← { linhas: [{ setorId }] }
```

O `POST` responde `200` com a lista gravada (não `201`: não cria um recurso novo endereçável, ele
substitui o conteúdo de um sub-recurso que já tem endereço).

Três pontos do contrato que não são óbvios e por isso viram teste:

1. **O roteiro não aceita `ordem` do cliente.** O servidor numera `1..N` pela ordem do array
   recebido. Isso mata por construção o buraco e a duplicata de `Ordem` — que hoje virariam
   violação do `UQ (ComponenteId, Ordem)` e sairiam como erro de banco, não como `400` legível — e
   torna "reordenar" trivial na tela: manda o array na ordem nova.

2. **O mesmo Setor PODE repetir no roteiro.** O `UQ` é `(ComponenteId, Ordem)`, **não**
   `(ComponenteId, SetorId)`. Isso é deliberado no schema e significa **retorno ao setor** (a peça
   volta à usinagem depois da solda). A validação **não pode** barrar setor repetido — e há teste
   para o caso permitido, não só para os proibidos (§4.2).

3. **`linhas: []` apaga a receita.** É a consequência direta de §1.2, e é o **único** caminho de
   remoção que existe, já que não há `DELETE`. Vira caso de teste explícito em vez de efeito
   colateral não escrito.

### 2.3 Validações e erros

| Status | Quando |
|---|---|
| `401` | Sem token |
| `403` | Perfil sem escrita (qualquer um fora de Administrador/PCP) |
| `404` | Componente pai inexistente |
| `400` | Id inexistente na lista — **nomeando qual** |
| `400` | Quantidade ≤ 0 (filhos e materiais) |
| `400` | Duplicata dentro da lista enviada (mesmo filho duas vezes; mesmo material duas vezes) |
| `400` | Auto-referência (`componenteFilhoId` == componente pai da rota) |
| `400` | Item inativo entrando na receita (ver abaixo) |
| `400` | Ciclo em qualquer profundidade |

**Por que ciclo é `400` e não `409`.** O `409` do projeto já significa *"já existe cadastro com
esse código"*, e o front tem um `ehConflito` que depende disso. Dar dois significados ao mesmo
status estragaria essa leitura.

**Auto-referência e duplicata são validadas na aplicação mesmo tendo constraint no banco.** O
`CK` e o `UQ` continuam sendo a rede de segurança; o que a aplicação acrescenta é a **mensagem
legível** — sem ela o usuário recebe erro de banco no lugar de um `400` que diz qual linha está
errada.

**Item inativo:** componente-filho ou material **inativo** não pode **entrar** na receita (`400`).
Linha que já existe e cujo item foi inativado **depois** sobrevive — inativar um item de catálogo
não deve corromper receitas já cadastradas, e a 1B já estabeleceu que catálogo se inativa, não se
exclui.

### 2.4 A guarda de perfis não é opcional aqui

Os 3 `POST` novos **têm** de ganhar entrada em `TabelaAprovada`
(`tests/Rastreamento.Api.Tests/PerfisDeEscritaDeclaradosTests.cs`), chaveada por
`"POST componentes/{componenteId:int}/filhos-padrao"` e as duas irmãs — sem `/api`, que é
middleware e não entra no `RoutePattern`.

Se alguém esquecer, **a guarda falha nomeando as rotas**. Isso não é burocracia: é exatamente o
cenário para o qual ela foi reescrita sobre o `EndpointDataSource` na sessão de 2026-08-17, e a
**1C é o primeiro recurso novo desde então** — é a primeira vez que a guarda vai fazer o trabalho
para o qual foi construída.

O par no front é uma entrada nova `'receitaPadrao'` em `web/src/auth/permissoes.ts` **e** em
`CONTROLLER_POR_RECURSO` (`permissoesEspelhamOBackend.test.ts`), apontando para
`ReceitaPadraoController.cs`. Perfis idênticos aos de `componentes`, mas **entrada própria** — o
mapa do front é por controller, e recurso apontando para o controller errado é justamente o que
essa guarda existe para pegar.

---

## 3. Frontend

### 3.1 Rota e navegação

`/componentes/:id` → `ComponenteDetalhePage`, dentro da rota de layout do `AppShell` em
`web/src/App.tsx`. `ComponentesPage` ganha link por item — mesmo molde de
`PedidosPage → /pedidos/:id`.

A tela começa por `<Pagina titulo=…>`. Sem container próprio, sem `min-h-screen` (quem faz isso é
o shell).

### 3.2 Três seções, três salvamentos

*Filhos-padrão*, *Materiais-padrão*, *Roteiro-padrão*, empilhadas. Independentes entre si porque
cada uma é um endpoint próprio.

Cada seção mantém um **rascunho local** e tem seu **próprio botão Salvar**, que envia a lista
inteira daquela seção. Não salva a cada linha digitada. O botão fica desabilitado enquanto não
houver alteração pendente, e desabilitado durante a mutação (regra da 1D).

**Custo aceito e declarado:** sair da página com rascunho não salvo **perde o rascunho**. Fechar
isso exigiria bloqueio de navegação (`useBlocker`), que é trabalho próprio e uma primitiva a mais.
Fica fora desta fase (§6).

### 3.3 A primitiva nova: `SeletorComBusca`

Em `web/src/components/`, com teste próprio — **não embutida na tela**, como manda o `CLAUDE.md`.

Motivação: o filho da receita é um `Componente`, e `Componente` é justamente a listagem que a 1B
paginou **por causa do volume**. Carregar tudo num `<select>` desfaria na prática a decisão de
paginar.

Decisão do usuário, com o raciocínio dele registrado: *"quem vai cadastrar as receitas, componentes
e afins está no começo do processo de fabricação (Gestão e PCP) […] já vai estar em um computador, é
inviável fazer esses cadastros pelo celular"* — o ganho de usabilidade compensa o custo de tasks
dedicadas de frontend. E a busca é **por código ou por nome**, não só por código.

O que a primitiva entrega:

- busca em `GET /componentes?busca=`, que **já casa em código ou descrição**
  (`specs/05-api-endpoints.md`) — o endpoint não muda
- **usa `useBuscaPaginada` por dentro** — debounce, cancelamento por sequência e clamp já estão
  resolvidos lá, e a regra do projeto proíbe refazer isso com `useState` + `useEffect` à mão
- ARIA de combobox: `role="combobox"`, `aria-expanded`, `aria-activedescendant`, listbox com
  `option`
- teclado: `↓`/`↑` navega, `Enter` seleciona, `Esc` fecha
- os três estados dentro do painel: carregando, "nenhum componente encontrado", erro

**Material e Setor continuam em `<select>` nativo.** Não é inconsistência por descuido: o roadmap
registra que `Setor` e `Material` **não têm o volume** que motivou a paginação do `Componente`, e
para lista curta o `<select>` nativo é melhor de usar e sai de graça em acessibilidade.
**Fronteira declarada: se `Material` for paginado um dia, ele migra para o `SeletorComBusca`, e o
gatilho é a paginação de `Material`.**

### 3.4 Estados e gating

Sem exceção às regras da 1D:

- cada seção com **carregando**, **vazio** (texto que distingue "não achei" de "não há nada") e
  **erro** via `mensagemDeErro` — **cada um com teste que morre se o estado sumir**
- `usePodeEscrever('receitaPadrao')` esconde formulário e botão Salvar; a **lista continua
  visível** para todos, porque leitura é de todos
- o `try/catch` do `403` continua obrigatório: esconder botão não é segurança, o `403` é a
  fronteira real
- cores só pelos tokens; teste de tela com `web/src/testes/api.ts`,
  `// @vitest-environment jsdom` no topo e `afterEach(cleanup)` explícito
- `npm run build` faz parte do ciclo, não só `npm test`

---

## 4. Prova

### 4.1 Por camada

| Projeto | O que prova |
|---|---|
| `Application.Tests` | Todas as validações com fakes, **sem banco**: ciclo em profundidade ≥ 2, **substituição que DESFAZ um ciclo preexistente é aceita** (§1.3), duplicata na lista enviada, quantidade ≤ 0, id inexistente, item inativo, `linhas: []` apaga, roteiro numerado `1..N`, setor repetido aceito |
| `Infrastructure.Tests` | Mapeamento EF das 3 tabelas contra **SQL Server real** (é o que prova o Database First) e que a substituição é **atômica** — apagar + inserir numa transação só |
| `Api.Tests` | Ponta a ponta: `401`, `403` por perfil, `404` pai inexistente, `400` por cada validação, `200` no caminho feliz |
| `web/` | `SeletorComBusca.test.tsx` (teclado, ARIA, três estados) e `ComponenteDetalhePage.test.tsx` (3 seções × carregando/vazio/erro, salvar, gating, `403`) |

As guardas de tema (`semCorForaDaPaleta`, `contraste`) varrem `web/src/` inteiro sozinhas — a tela
nova cai nelas sem ninguém precisar lembrar. É o ponto de elas serem executáveis.

### 4.2 O teste que prova o **permitido**

`setor repetido no roteiro é aceito`. Sem ele, alguém "corrige" a receita acrescentando uma
validação de setor único, a suíte fica verde, e o **retorno ao setor** — que o schema permite de
propósito — some em silêncio.

### 4.3 As mutações que a review vai ter de derrubar

Nomeadas já aqui, e o ponto é que **a review seja instruída a burlar a guarda, não a conferi-la**:

1. apagar a validação de ciclo → a suíte fica verde?
2. `ordem = i` em vez de `i + 1` → alguém morre?
3. `linhas: []` deixar de apagar → alguém morre?
4. tirar o `[Authorize(Roles)]` de um dos `POST` novos → a guarda pega?
5. **acrescentar** validação de setor único no roteiro → **algum teste tem de morrer**
6. validar o ciclo contra o grafo **atual** em vez do **resultante** (§1.3) → alguém morre? Esta é
   a mutação mais traiçoeira das seis: ela deixa verdes todos os testes de ciclo *proibido* e só
   quebra o caso em que o usuário está **consertando** um ciclo

Isso vem de uma lição que já custou duas rodadas neste projeto, registrada na sessão de
2026-08-17: **mutação de prova escolhida por quem desenhou a guarda tende a confirmar o desenho
dela.** Nas duas vezes, quem achou os buracos foi um revisor sem compromisso com o desenho e
mandado **burlar**.

### 4.4 O que se **mede** no início do plano, em vez de herdar

- **Baseline de testes.** O ledger registra backend **338** (129 Application + 38 Infrastructure +
  171 Api) e front **317 / 28 arquivos**. Isso é **registro, não medição desta sessão** — remedir
  antes de escrever as contagens do plano, porque total absoluto herdado propaga erro de task em
  task.
- **Schema.** As 3 tabelas estão no `.sql` e o banco de dev foi regenerado dele em 2026-08-04,
  então **não deve** precisar de `ALTER`. Uma conferência de uma linha confirma isso em vez de
  presumir — e se precisar, o `ALTER` idempotente segue o padrão dos demais em `CLAUDE.md`.

### 4.5 Massa de demonstração — `db/seed-demo.sql` (novo)

**Autorização do usuário em 2026-08-17:** o banco do Docker pode ser **regenerado à vontade** — o
conteúdo atual é resquício de teste dele — e pode ser populado com valores mais coerentes. Isso
levanta a restrição que a sessão de 2026-08-17 tinha ("não derrubar o SQL Server"), e por causa
dela uma contagem ficou registrada em `CLAUDE.md` como não reconferida.

**Por que isto vira item da 1C e não um extra:** o `SeletorComBusca` (§3.3) existe **por causa do
volume** do catálogo. O `db/seed.sql` de hoje tem 28 linhas e **nenhum item de catálogo** — só
perfis e dois usuários. Contra um catálogo vazio, "busca por código ou nome" não é verificável: não
há o que buscar, não há como ver a lista rolar, não há como provar que o debounce importa. A fase
se compromete a verificar essa primitiva; a massa é o que torna a verificação possível.

**A fronteira, e ela é o ponto:** a massa vai num arquivo **separado**, `db/seed-demo.sql`, e
**não** dentro de `db/seed.sql`.

- `db/seed.sql` é o **mínimo para o sistema funcionar** (perfis + usuários). Continua como está.
- `db/seed-demo.sql` é **conveniência de desenvolvimento**: catálogo de `Componente`, `Material` e
  `Setor` com nomes plausíveis de fábrica, em quantidade suficiente para a busca paginada ter
  sentido, mais algumas receitas padrão de exemplo.

**Nenhum teste automatizado pode depender de `seed-demo.sql`.** Os testes de `Api.Tests` e
`Infrastructure.Tests` criam a própria massa, e é isso que os torna determinísticos. Massa de demo
compartilhada com a suíte é como suíte verde vira suíte que só passa nesta máquina. A dependência é
de mão única: a **verificação manual** usa o demo; a **suíte** ignora que ele existe.

---

## 5. Documentos que esta fase atualiza

- `specs/05-api-endpoints.md` — as 3 linhas marcadas "Fase 1C, não implementado" passam a
  descrever o contrato real (corpo, perfis, erros).
- `specs/06-roadmap-mvp.md` — o bloco "Falta 1C" vira "1C concluída".
- `specs/01-dominio-e-regras-de-negocio.md` — regra nova: **a receita padrão não pode conter
  ciclo**; e o registro de que **setor repetido no roteiro é permitido e significa retorno ao
  setor**.
- `CLAUDE.md` — se o `SeletorComBusca` estabelecer padrão para escolha de item de catálogo grande,
  entra na seção de interface. E o bloco de pré-requisito dos testes ganha o `db/seed-demo.sql`
  (§4.5), com a distinção entre ele e o `db/seed.sql` escrita — senão o próximo a ler vai supor que
  a suíte depende do demo.

---

## 6. Fora de escopo — cada um com o gatilho escrito

| Fora | Gatilho para reabrir |
|---|---|
| Bloqueio de navegação com rascunho pendente (`useBlocker`) | Usuário relatar perda de trabalho ao navegar |
| Trava de concorrência entre dois editores da mesma receita | Dois usuários editarem receitas de verdade em paralelo |
| Import de CAD | É fase própria, já especificada em `06-roadmap-mvp.md`; o contrato de lista desta spec foi desenhado para ele |
| Regra de receita por `Tipo` de componente | Um caso real da fábrica em que a regra evitaria erro |
| `SeletorComBusca` para Material/Setor | A paginação de `Material` (§3.3) |
| `DELETE` por linha | Só se o "substitui a receita inteira" se mostrar ruim de usar |
