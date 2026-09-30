# Filtros da demanda e ação em lote — design

**Data:** 2026-09-29. **Origem:** dois pedidos do usuário nas verificações manuais no celular — os
filtros da fila por Material e por Pedido (verificação da Fase 3, fila do Corte a Laser) e a ação em
lote com "marcar todos" (verificação da Fase 3D) — mais a dívida da lista de Pedidos que a Fase 1E
nomeou. **Estado:** desenho aprovado pelo usuário, seção a seção, no brainstorm de 2026-09-29; o plano
ainda não existe.

**Posição na fila** (decisão do usuário, 2026-09-28, registrada em `specs/06-roadmap-mvp.md`): depois da
Fase 3D, **filtros → 1F → 3B**. Esta spec é o "filtros" dessa ordem, com o escopo ampliado no
brainstorm (seção 1).

**Prazo:** o usuário pediu os filtros "antes do primeiro deploy" (2026-09-27): *"não precisa pra agora
durante os testes, mas é importante ter antes do deploy pq afeta qualidade de vida do operador"*. No
brainstorm, surgiu um segundo motivo, mais próximo: uma demonstração do sistema completo para a empresa
onde ele vai ser implantado, prevista para daqui a um mês ou menos. É isso que puxou a tela de Pedidos
para dentro do escopo (seção 2.1) e o que decide a divisão em dois planos (seção 9).

## 1. Escopo

| Bloco | O que o usuário pediu | O que esta spec faz |
|---|---|---|
| A | Filtrar a fila do Setor por Material (o Corte trabalha por espessura de chapa) e por Pedido (para achar a prioridade) | Um componente de filtro reutilizável, `FiltroDeDemanda`, aplicado à **fila do Setor** e às **Tarefas** do Movimentador, com as facetas Material e Pedido |
| B | *"a mesma ideia do checkbox que existe pro 'Levar' do movimentador, colocar pras ações dos operadores, ao invés de ter que iniciar peça a peça"*, e *"um checkbox pra marcar todos os itens da página"* | Seleção múltipla para **Iniciar**, **Terminar** e **Iniciar o pai** na fila, com "Marcar todos" por seção; "Marcar todos" nas Tarefas; duas rotas de lote no backend |
| C | A lista de Pedidos que a Fase 1E deixou como dívida | `GET /pedidos` **paginado**, com busca e filtro (Status e Material, pelo mesmo componente); um **resumo** para a Home; o **rótulo em português** do status do Pedido |

**Fora de escopo, por decisão escrita:**

- **Editar os Materiais de um nó** (`estrutura/{id}/materiais`, rota planejada e ainda sem fase — ver
  `specs/05-api-endpoints.md`). Consequência: o Item ad-hoc, que nasce sem material, não aparece no
  filtro de Material enquanto essa rota não existir (seção 2.3).
- **Estornar um lote de uma vez.** O lote gera N movimentações independentes, e o estorno continua por
  linha (seção 5.6).
- **Filtro em outras telas** além das três desta spec. O componente é desenhado para ser reusado
  (seção 3), mas cada tela nova é decisão própria.
- **Contagem por opção no filtro de Pedidos** (seção 7.2).
- **Índice novo** para a busca por Material em Pedidos. No volume de um piloto não se justifica; fica
  anotado caso uma medição diga o contrário.
- **Mudança de schema:** nenhuma. `dbo.EstruturaMaterial` já existe e já é gravado (seção 2.3). O
  `specs/02-modelo-de-dados.sql` não muda, e não há `db/alter-*.sql` nesta fase.

## 2. Decisões do brainstorm, com o porquê

### 2.1 A, B e C numa spec só

O usuário decidiu primeiro por A + B, e em seguida trouxe o C: *"acho que o item C vai ter bastante
impacto pra mostrar um protótipo completo do Rastru pra empresa que vamos implantar, e isso parece ser em
breve (um mês ou menos)"*. A e B moram na mesma tela e são o mesmo gesto — filtrar e agir sobre o que
sobrou —, e o "marcar todos" do B perde metade do sentido sem o filtro do A. O C mora em outra tela e
tem outro mecanismo (seção 2.6), mas usa o mesmo componente de filtro.

### 2.2 O filtro é um componente, e vale nas duas telas de execução

Os dois filtros, Material e Pedido, entram na **fila do Setor** e nas **Tarefas**. Nas palavras do
usuário, sobre o componente: *"tente componentizar esse filtro pq acredito que ele pode ser reutilizado
em outras páginas, é um comportamento normal quase na empresa inteira procurar a demanda por esses 2
filtros, ainda que no papel ou em alguma planilha que eles verificam"*. Daí o nome `FiltroDeDemanda` e
o contrato agnóstico de origem da seção 3.

### 2.3 Material é o do nó (`EstruturaMaterial`), não o do catálogo

O schema liga Material a um nó em dois níveis: `ComponenteMaterialPadrao` (a receita do catálogo) e
`EstruturaMaterial` (o nó daquele Pedido). Medido no código antes da decisão:

- `EstruturaMaterial` **é gravado na criação do nó**, copiado da receita, tanto na Peça (`CriarPeca`)
  quanto no filho vindo de Componente (`AcrescentarFilho`). Os dois passam pelo `PlanejadorDeCopia`. Ele
  **não** depende da Fase 4 para existir.
- O Item ad-hoc nasce "sem receita, sem filhos, sem materiais, sem roteiro" (comentário do
  `AcrescentarFilho`), e portanto **sem material nos dois níveis**.

Hoje, então, as duas opções cobrem os mesmos nós. A diferença é o tempo: o nível do nó é uma foto
tirada na criação, e editar a receita depois não muda o resultado do filtro num Pedido já em produção;
o nível do catálogo mudaria esse resultado sem ninguém mexer no Pedido. O usuário escolheu **o nível do
nó**. Quando a edição de Materiais por nó existir, o Item ad-hoc passa a aparecer no filtro sem mudança
nenhuma aqui.

### 2.4 As opções vêm do que está na lista

Na fila e nas Tarefas, o filtro de Material lista só os Materiais que existem na tela **naquele
momento**, com a contagem ("Chapa SAE 1020 3,00 mm · 7"), e o de Pedido lista só os Pedidos que têm
algo ali. O operador lê a demanda de relance antes de filtrar e nunca escolhe uma opção que dá lista
vazia. Em Pedidos, que é paginado, a mesma ideia vira "os Materiais que aparecem em algum Pedido",
vindos do servidor (seção 7.2).

### 2.5 Vários valores por faceta

Dentro de cada faceta a combinação é **OU**, e entre facetas é **E**. O motivo que o usuário deu:
*"uma chapa pode comportar mais de um pedido ao ser reaproveitada pra cortar as peças deles, tentando
sempre otimizar espaço. Então é comum ter mais de um pedido por chapa"*. O caso típico é "Material 3,00
mm E Pedidos {1042, 1043}".

### 2.6 Onde se filtra: onde os dados moram

A fila (`GET /setores/{id}/fila`) e as Tarefas **não são paginadas**: chegam inteiras, com atualização
periódica. Por isso filtram **no cliente**, e as opções com contagem saem da própria resposta, sem
custo. `GET /pedidos` passa a ser paginado (seção 7) e por isso filtra **no servidor**. Das três
abordagens levadas ao usuário — esta; tudo no servidor; tudo no cliente —, ele escolheu esta. Os dois
caminhos de filtragem usam o mesmo componente, e a regra de casamento no cliente é uma função só
(seção 3.2).

Uma consequência que o brainstorm tirou disso: com a fila sem paginação, a dúvida do pedido de lote
entre "marcar todos da página visível" e "marcar tudo o que o filtro achou" deixa de existir. São a mesma
coisa.

### 2.7 O filtro age sobre todas as seções da fila

Filtrou por "Pedido 1042", tudo o que aparece na tela é do 1042: Em trabalho, A iniciar aqui,
Aguardando montagem, Aguardando coleta e Sobra. A regra do cartão de montagem está na seção 4.3.

### 2.8 O lote cobre Iniciar, Terminar e Iniciar o pai, travado na seção do primeiro item

O usuário escolheu incluir o "Iniciar o pai" (Aguardando montagem, quando a montagem começa neste Setor)
junto de Iniciar e Terminar, e acrescentou a trava: *"os checkboxes habilitados só podem corresponder à
mesma seção que ele marcou o primeiro item, assim não mistura ações diferentes em liberações
diferentes"*. Com isso, "Iniciar" e "Iniciar o pai" nunca se misturam, apesar do mesmo verbo, porque
moram em seções diferentes. E todo lote é uma ação só: um botão, um rótulo, uma requisição.

### 2.9 Quantidade: o saldo inteiro por padrão, editável por linha

É o molde do "Levar" das Tarefas. Marcar a linha abre o campo já preenchido com o saldo, e dá para
diminuir numa linha sem desmarcar as outras. O caso comum custa um toque, e o raro continua possível sem
sair do lote.

### 2.10 Recusa: tudo ou nada

O lote vai numa requisição e numa transação. Se um item é recusado, nada é gravado. É a semântica que o
`POST /entregas` já tem. O livro nunca fica com meio lote, e o operador nunca precisa descobrir o que
entrou e o que não entrou.

### 2.11 Pedidos: paginação, busca, filtro e o resumo da Home

O usuário escolheu a opção mais completa das quatro levadas: paginação com o resumo da Home, busca
textual, e o componente de filtro com Status e Material. A busca por "código de peça" é a leitura
oferecida no brainstorm do item herdado da 1E, e foi aceita: achar o Pedido pelo código do Componente
de qualquer nó dele ("em que Pedido está a CH-2150?").

### 2.12 O rótulo em português do status entra

O filtro de Status mostraria o enum cru ("EmProducao", "AguardandoExpedicao") justamente no controle
novo. O usuário: *"entra rótulo em português, já era um cosmético previsto"*.

## 3. O componente `FiltroDeDemanda`

### 3.1 O que é

Uma primitiva nova em `web/src/components/`, com teste próprio, conforme a seção "Interface" do
`CLAUDE.md`. É **controlada**: não busca nada e não guarda a seleção. A página entrega as opções e
recebe a seleção de volta.

### 3.2 Contrato

- `facetas`: lista de `{ chave, titulo, opcoes: [{ valor, rotulo, contagem? }] }`. Exemplos de chave:
  `material`, `pedido`, `status`. `contagem` é opcional: a fila e as Tarefas mandam, e Pedidos não
  manda.
- `selecao`: `Record<chave, valor[]>`, e `aoMudar(novaSelecao)`.
- **Regra de casamento:** OU dentro da faceta, E entre facetas; faceta sem nada marcado não restringe.
  O módulo do componente exporta a função pura `casaComFiltro`, que as páginas que filtram no cliente
  usam. Nenhuma página reimplementa a regra.

### 3.3 No celular

- **Fechado**, ocupa uma linha: o botão **"Filtrar"**, com o número de filtros ativos, as seleções
  ativas como pílulas removíveis ("Chapa 3,00 mm ×", "Pedido 1042 ×") e **"Limpar"**.
- **Aberto**, mostra um bloco por faceta com as opções como caixas marcáveis e a contagem ao lado.
- Numa faceta com muitas opções, aparece um campo de busca dentro dela. O limite (da ordem de 8 opções)
  é calibrado no plano.

### 3.4 Casos de borda

- **Uma opção marcada que deixou de existir na lista** (a atualização periódica tirou o último item
  daquele Pedido) **continua marcada e visível, com contagem 0**, e não sai da seleção em silêncio. O
  operador entende por que a tela está vazia e pode removê-la.
- **O estado vazio com filtro ativo tem texto próprio** ("Nada nesta fila com esses filtros", com o
  botão "Limpar filtros"), distinto do vazio real ("não há nada"). É a exigência da seção "Interface"
  do `CLAUDE.md` de distinguir "não achei" de "não há nada".

### 3.5 Onde a seleção mora

Na **URL** da página (`?material=3,5&pedido=42`). Sobrevive ao F5 e ao "voltar", e um link filtrado
pode ser compartilhado ("a fila do Corte, só a chapa de 3 mm"). Cada página lê e escreve os próprios
parâmetros. O componente não sabe de URL.

### 3.6 Cores

Só tokens de `web/src/index.css`. A pílula de filtro ativo **não** usa cor de estado (`positivo`,
`negativo`, `atencao`): pela regra "cor de estado nunca decora", filtro ativo não é estado de negócio.

## 4. Filtros na fila do Setor e nas Tarefas

### 4.1 Backend: o `NoResumoDto` ganha Materiais e Cliente

O `NoResumoDto`, que a fila e as Tarefas usam para cada nó, ganha dois campos:

- `Materiais: [{ Id, Codigo, Descricao }]`, que são os `EstruturaMaterial` do nó, carregados **numa
  consulta em lote** pelos ids dos nós da resposta, sem uma ida ao banco por nó;
- `PedidoCliente`, porque o rótulo da opção de Pedido é "Número · Cliente" (seção 4.2) e o DTO hoje só
  traz `PedidoNumero`.

A mudança é aditiva: nenhum consumidor atual quebra, e não nasce rota nova.

### 4.2 As facetas

Saem da resposta já carregada, no cliente:

- **Material**: o rótulo é a descrição ("Chapa de aço carbono SAE 1020 3,00 mm"), com o código em fonte
  monoespaçada;
- **Pedido**: o rótulo é "Número · Cliente", ordenado por número;
- **contagem de cada opção** = quantas linhas ou cartões visíveis casariam com ela. Um cartão de
  montagem conta **uma vez** por opção, mesmo que o pai e dois filhos tenham a mesma chapa.

### 4.3 Como cada seção casa

- **Em trabalho, A iniciar aqui, Aguardando coleta e Sobra**: a linha casa pelo próprio nó.
- **Aguardando montagem**: o cartão casa se **o pai ou algum filho presente** casar. Quando casa,
  aparece **inteiro**, com todos os filhos: esconder um filho mostraria uma montagem pela metade, com o
  "dá para iniciar" fora de contexto. Sem essa regra, filtrar por Material esconderia toda montagem,
  porque o pai em geral não tem chapa própria.
- Uma seção que fica sem linha nenhuma depois do filtro mostra o vazio próprio da seção. A **ordem** das
  seções não muda (Em trabalho → A iniciar aqui → Aguardando montagem → Aguardando coleta → Sobra, spec
  da Fase 3D).

Um nó sem material nenhum some quando o filtro de Material está ativo. É o caso do Item ad-hoc (seção
2.3) e, em geral, do pai de montagem visto sozinho. É o comportamento esperado.

### 4.4 Nas Tarefas

Os mesmos dois filtros, aplicados dentro de cada grupo (o Setor de origem). Um grupo sem nenhum item
depois do filtro some. Se todos somem, aparece o vazio de "nada com esses filtros".

### 4.5 Filtro e seleção em lote convivem

- Um item marcado que **sai da vista por causa do filtro continua marcado**. A barra do lote avisa
  ("3 marcados, 1 oculto pelo filtro"), e o lote vai com ele. Desmarcar ao filtrar faria o operador
  perder trabalho sem aviso só por mexer no filtro.
- Um item marcado que **sai da lista** (outra pessoa agiu) sai da seleção com aviso, que é a regra que
  as Tarefas já têm.

## 5. Lote na fila do Setor e nas Tarefas: a tela

### 5.1 Onde aparecem os checkboxes

| Seção | Ação do lote |
|---|---|
| A iniciar aqui | Iniciar |
| Em trabalho | Terminar |
| Aguardando montagem, só no cartão em que a montagem começa neste Setor | Iniciar o pai |

Uma linha que não pode agir tem o checkbox **desabilitado, com o motivo na própria linha**, como a tela
já mostra hoje. É o caso do Pedido pausado e do "dá para iniciar 0". Os checkboxes só aparecem para quem
pode apontar (os mesmos perfis de Iniciar e Terminar, seção 6.1).

### 5.2 Trava de seção

Ao marcar o primeiro item, os checkboxes das outras seções ficam desabilitados, com uma dica curta
("Conclua ou limpe a seleção de *A iniciar aqui*"). A trava solta quando a seleção esvazia.

### 5.3 "Marcar todos", por seção

Fica no cabeçalho de cada seção que tem ação de lote. Marca **todas as linhas visíveis depois do
filtro** que podem agir, e pula as desabilitadas. Com a seção inteira marcada, vira "Desmarcar todos".
Respeita a trava: fica desabilitado nas outras seções.

### 5.4 Quantidade e barra do lote

- Cada linha marcada abre o campo já preenchido com o saldo, ou com o "dá para iniciar" no pai,
  editável e validado pelo mesmo `lerQuantidade` do "Levar".
- Enquanto há seleção, uma **barra fixa no rodapé** mostra o botão com o verbo da seção ("**Iniciar 5
  itens**", "**Terminar 5 itens**"), o aviso de marcados ocultos pelo filtro (seção 4.5) e "Limpar
  seleção". O botão fica desabilitado se alguma quantidade for inválida, no mesmo desenho do
  `algumInvalido` das Tarefas.

### 5.5 Recusa

Tudo ou nada (seção 2.10). Nada é gravado, a fila recarrega, e o banner nomeia **o item e o motivo**
("Só há 3 de CH-2150 — Chapa de base do mancal a iniciar aqui"). A seleção sobrevive, sem os itens que
saíram da lista.

### 5.6 O que continua como está

- **O botão individual de cada linha.** Para uma peça só, "Iniciar" direto na linha continua sendo o
  caminho mais curto, e o fluxo individual já testado não muda. O custo é a linha ter checkbox e botão;
  se isso pesar no celular, a verificação manual (seção 9) é o lugar de rever.
- **Estorno por item.** Cada linha mostra os próprios estornáveis, como hoje. Não existe "estornar
  lote".

### 5.7 Tarefas

Ganham **"Marcar todos"** no topo, que marca todos os itens visíveis depois do filtro, em todos os
grupos, e pula os bloqueados (como o de pai sem Roteiro). O resto do "Levar" não muda: ele já é lote,
numa requisição, tudo ou nada.

## 6. Lote no backend

### 6.1 Rotas

No molde do `POST /entregas`:

- `POST /inicios`, com `{ setorId, itens: [{ estruturaItemId, quantidade }] }`. Serve tanto a "A
  iniciar aqui" quanto a "Iniciar o pai": o início de um nó com filhos já consome os filhos (spec da
  Fase 3D), e o lote não muda essa regra.
- `POST /terminos`, com `{ setorId, itens: [{ estruturaItemId, ordem, quantidade }] }`.
- Perfis: os mesmos das rotas individuais (`Operador,Administrador`). A tabela
  `web/src/auth/permissoes.ts` continua espelhando o backend sem recurso novo.
- Resposta: `201`, com a lista de `MovimentacaoDto` na ordem dos itens.

### 6.2 Semântica

O lote é **exatamente** a ação individual aplicada item a item, em ordem, **numa transação SERIALIZABLE
só**, pelo `EmTransacaoAsync` e com o mesmo retry de deadlock. Cada item desconta do que os anteriores
já consumiram, como faz a entrega.

A regra de iniciar e terminar continua **uma só, com dois pontos de entrada**: o núcleo por item sai do
`ApontamentoUseCase` e é usado pela forma individual e pela de lote. As rotas individuais
(`estrutura/{id}/inicios` e `estrutura/{id}/terminos`) não mudam de contrato. Duas cópias da regra de
conservação poderiam divergir, e é isso que o núcleo único impede.

### 6.3 Recusas

- Lista vazia → `400`.
- **Item repetido** no lote → `400`. A chave é o nó no Iniciar e o par nó + ordem no Terminar. Somar
  duas linhas do mesmo nó em silêncio esconderia um erro do cliente.
- Acima do **tamanho máximo** do lote → `400`. O teto é definido no plano, folgado para o "marcar
  todos" de uma fila real.
- **A primeira recusa de negócio aborta tudo**, com o mesmo código e status que a rota individual daria
  (`409` para saldo, pausa ou corrida; `404` para nó inexistente) e **com a mensagem nomeando o item**.
  Vale inclusive para o Pedido pausado: num lote, uma mensagem sem o nome do item não diz ao operador
  qual linha desmarcar.

### 6.4 O que não muda

O livro. Cada item gera as mesmas linhas que a ação individual geraria (Início; ou Montagem mais as
baixas dos filhos, no pai; ou Término), e o estorno continua por linha. A conservação de quantidade
(regra 9) fica coberta por construção, por passar pelo mesmo núcleo, e também por teste (seção 8).

## 7. Pedidos

### 7.1 `GET /pedidos` paginado

Parâmetros `?busca=&status=A,B&material=3,5&pagina=&tamanho=`, com a resposta no envelope `PaginaDto`
que os Componentes já usam. O filtro é montado no `IQueryable`, no molde do `FiltroDeComponente`, e a
ordem atual da lista não muda.

- **`busca`**: acha o Pedido pelo **número**, pelo **cliente** ou pelo **código do Componente de
  qualquer nó** dele (Pedido → Agrupamento → `EstruturaItem` → `Componente`), inclusive de nó filho, e
  não só da Peça.
- **`status`**: vários, com OU entre eles.
- **`material`**: vários, com OU entre eles. O Pedido casa se **algum nó** dele tem aquele
  `EstruturaMaterial`, o mesmo nível de Material da fila (seção 2.3).
- Entre os parâmetros vale **E**, a mesma regra do componente.

### 7.2 Opções do filtro de Pedidos

Vêm do servidor, **sem contagem**. Contagem por opção sobre uma lista paginada pede uma consulta a mais
por faceta, e a contagem por status já está na Home.

- **Status**: a lista fixa dos status, com o rótulo em português (seção 7.5).
- **Material**: uma rota nova, `GET /pedidos/materiais`, que devolve os Materiais que aparecem em algum
  nó de algum Pedido. É a versão servidor de "só o que existe na lista" (seção 2.4).

### 7.3 A tela

A `PedidosPage` troca o `listarPedidos()` cru por `useBuscaPaginada`, com o campo de busca, o
`FiltroDeDemanda` com Status e Material e a paginação (`ControlesDePaginacao`). O formulário de criar
Pedido continua na mesma tela. Busca, filtros e página ficam na URL (seção 3.5).

### 7.4 O resumo da Home

A Home conta os Pedidos por status e escolhe "os abertos há mais tempo" varrendo a lista inteira de
`listarPedidos()`. Com `/pedidos` paginado, ela passaria a contar só a primeira página, **em silêncio**.
Por isso entra **`GET /pedidos/resumo`**, que devolve:

- a **contagem por status**, com `GROUP BY` no servidor;
- **os 5 abertos há mais tempo**, pelo critério de hoje: fora de `ENCERRADOS` (Concluído, Cancelado),
  por data de abertura.

A `HomePage` deixa de chamar `listarPedidos()`, e as contas que ela faz hoje no cliente passam a vir
prontas. Critério e número mudam de lugar, não de valor.

### 7.5 Rótulo em português

Um `rotuloDoStatus` em `web/src/pedidos/statusDoPedido.ts` ("Em produção", "Aguardando expedição" etc.)
passa a valer na Home, na `PedidosPage`, na `LinhaDePedido` e no filtro de Status.

### 7.6 A guarda da Fase 1E é trocada, não apagada

O teste de `web/src/api/cadastros.test.ts` chamado *"devolve o conjunto inteiro de pedidos, nao uma
pagina — a HomePage depende disso"* fica vermelho se `listarPedidos()` paginar. Ele existe exatamente
contra o risco da seção 7.4. Com a Home fora dessa lista, a proteção passa para dois testes novos:

- no backend, o resumo conta **todos** os Pedidos, inclusive quando há mais Pedidos do que o tamanho de
  página;
- no front, a Home lê o resumo e não uma lista.

O plano registra a remoção do teste antigo como **troca deliberada de guarda**, no mesmo commit que
introduz as novas.

## 8. Testes que o desenho exige

Cada um tem de morrer se o que ele protege sumir.

- **Componente:** `casaComFiltro` (OU dentro da faceta, E entre facetas, faceta vazia não restringe);
  opção marcada que some da lista continua marcada, com 0; vazio com filtro distinto do vazio real;
  remover pílula; "Limpar".
- **Fila e Tarefas:** as facetas saem dos dados com a contagem certa; o cartão de montagem casa pelo pai
  ou por um filho presente e aparece inteiro; o filtro lê e escreve a URL; item marcado e oculto pelo
  filtro continua no lote.
- **Lote na tela:** a trava de seção; "Marcar todos" marca só o visível e habilitado; o verbo da barra;
  o botão desabilitado com quantidade inválida; a recusa recarrega e mantém a seleção.
- **Lote no backend:**
  - **o lote produz o mesmo livro que os mesmos itens feitos um a um** — o teste central, que prova a
    seção 6.4;
  - tudo ou nada com recusa no meio do lote (nada gravado);
  - item repetido, lista vazia e acima do teto dão `400`;
  - a mensagem de Pedido pausado nomeia o item;
  - perfis sem escrita recebem `403`.
- **`NoResumoDto`:** os materiais do nó chegam, e a consulta é em lote.
- **Pedidos:** cada parâmetro sozinho e combinado; a busca pelo código do Componente de um nó **filho**;
  o resumo conta além do tamanho de página (seção 7.6).
- `npm run build` e `dotnet build Rastreamento.slnx -warnaserror` fazem parte do ciclo, como sempre.

## 9. Divisão em planos e verificação

Uma spec, uma branch de fase, **dois planos**:

1. **Plano 1 — Filtros:** o `FiltroDeDemanda`, o `NoResumoDto` com Materiais e Cliente, os filtros da
   fila e das Tarefas, Pedidos paginados com busca e filtro, o resumo da Home e o rótulo em português.
   É **só leitura**, sem risco para o livro, e sozinho já é a demonstração para a empresa.
2. **Plano 2 — Lote:** `POST /inicios`, `POST /terminos`, o lote na fila e o "Marcar todos" nas
   Tarefas. **Escreve no livro** sob SERIALIZABLE, com outro perfil de risco e de review.

O motivo da divisão é o prazo da demonstração: o Plano 1 entrega o protótipo visível mesmo que o 2
atrase, e a review do lote não fica diluída num diff grande de telas. O Plano 2 depende do 1, porque a
seleção convive com o filtro (seção 4.5).

Cada plano termina com uma **verificação manual no celular**, pelo usuário. O produto dessa task é um
relatório, e a dispensa de review dela é escrita antes, como nas fases anteriores.

## 10. Documentos que mudam junto

Entram no plano como task de documentação, **com review** (a documentação vira texto do TCC):

- `specs/05-api-endpoints.md`: `GET /pedidos` paginado com os parâmetros, `GET /pedidos/resumo`,
  `GET /pedidos/materiais`, `POST /inicios`, `POST /terminos` e os campos novos do `NoResumoDto`;
- `specs/04-fluxos-de-usuario.md`: filtrar a fila e as Tarefas e agir em lote (Operador e
  Movimentador); buscar e filtrar Pedidos (PCP);
- `specs/06-roadmap-mvp.md`: a seção desta fase, com o escopo ampliado (A + B + C) e a posição mantida
  (filtros → 1F → 3B);
- `CLAUDE.md`, seção "Interface": o `FiltroDeDemanda` como a primitiva de filtrar demanda por facetas,
  ao lado de `SeletorComBusca` e `useBuscaPaginada`, e quando usar cada uma.
