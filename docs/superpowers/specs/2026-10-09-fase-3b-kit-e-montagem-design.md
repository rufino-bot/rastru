# Fase 3B — Kit e montagem (design)

Data: 2026-10-09. Brainstorm com o usuário na mesma data. Origem: a seção "Fase 3B — Kit e montagem" de
`specs/06-roadmap-mvp.md` e a seção 9 ("Deixado para a spec de cada fase") da spec
`docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`.

## 1. Contexto

A 3B recebe pronto, das Fases 3 e 3D:

- `EstruturaItem.QuantidadePorPai`, a `Montagem` e o destino terminal "montado".
- O **início** de um nó com filhos consome os filhos que aguardam montagem no Setor (regra 24). É o único
  jeito de o pai entrar em produção.
- O filho que termina o próprio Roteiro aguarda coleta e é entregue, **sem escolha**, na posição
  `AguardandoMontagem` do Setor do **primeiro passo do pai** (regra 29). O redirecionamento
  (`AguardandoMontagem(S)` → `AguardandoMontagem(P1)`) existe para quando o PCP muda esse primeiro passo.
- A entrega (`POST /entregas`) recebe uma **lista** e grava tudo numa transação. O `EntregaUseCase` foi
  escrito assim pensando no conjunto completo desta fase.
- A calculadora (`CalculadoraDeExecucao`) já tem o que o pai ainda precisa receber de cada filho
  (`Precisa`), a sobra (regra 30) e a lista de "Item pronto" (`ColetasPendentes`).

O que falta à 3B, pelo roadmap: `Setor.UtilizaKit`; o **conjunto completo** na entrada de Setor com
`UtilizaKit` (regra 25); a tarefa **Kit pronto para montagem** (regra 23); o nó que ganha filho depois de
iniciado (a exceção da regra 24); as perguntas da seção 9 da spec do Kit; e o **C2**, o título da página do
Agrupamento com o Pedido.

## 2. Decisões, com o porquê

### D1 — Filho acrescentado a nó já iniciado: recusado

Acrescentar filho a um nó que **já saiu de "a iniciar"** passa a ser recusado. Era livre desde a seção 4.7
da spec da Fase 3. Uma folha C de 10 com 6 iniciadas que ganhasse o filho E deixava as 6 saírem "acima do
montado", e a conta do que o pai precisa receber pedia E para as 10, quando só as 4 restantes podem
consumi-lo: tarefa e sobra erradas.

Com a guarda, a saída limitada ao total montado vale **sem exceção**, por construção (regra 24). A
condição é o que saiu de "a iniciar" **líquido de estorno**: se todo início foi estornado, a guarda volta a
aceitar.

**Revisitar na Fase 5.** O usuário tem uma ideia de como tratar estrutura alterada no meio da produção, e
ela não cabe nesta fase. A guarda é a resposta da 3B, não a definitiva. Estrutura errada descoberta no meio
da produção é o assunto de "alterar projeto ou descontinuar", que já é da Fase 5 ("Pontos ainda em aberto"
do `01`).

Descartadas:
- **Aceitar, e o filho novo valer só para o que falta iniciar** (a base das contas passaria de "falta
  montar" a "falta iniciar"): o sistema passaria a afirmar que as unidades já iniciadas estão completas sem
  a parte, o que pode não ser verdade no chão.
- **Montagem tardia** (ação nova que consome o filho para dentro das unidades já iniciadas): reintroduz
  "montar" como ação, que a 3D acabou de eliminar.

### D2 — Filho que termina o próprio Roteiro no Setor onde o pai é montado: entrega como os outros

Quando o último passo do filho é no mesmo Setor do primeiro passo do pai (a Solda, por exemplo), o caminho
continua o de hoje: o filho termina, aguarda coleta e é **entregue** de Solda para Solda. No Kit, essa
entrega entra **no mesmo conjunto** dos irmãos vindos de fora, e o cartão do Kit o mostra com a marca "já
está na Solda".

O motivo, nas palavras do usuário: os movimentadores vão pegando os itens nos outros Setores e entregam tudo
junto; na tela de Tarefas, marcam todos os itens do Kit como entregues, inclusive o que já estava lá.

Descartadas:
- **Terminar o último passo no Setor do primeiro passo do pai ir direto a `AguardandoMontagem`:** abre uma
  porta pela qual um filho chega à montagem sem o conjunto completo, obriga a contar os conjuntos à espera
  de outro jeito e muda também o fluxo do Avulso.
- **Quem libera é o operador da Solda:** a entrega é ação do Movimentador, e ganharia um segundo ator.

### D3 — Onde vale o conjunto completo: Kit e destino `AguardandoMontagem` num Setor com `UtilizaKit`

O conjunto completo vale quando as duas condições estão juntas: o Agrupamento é **Kit**, e o destino da
entrega é `AguardandoMontagem(S)` com `S.UtilizaKit`. Cobre a entrega vinda da coleta e o
redirecionamento.

Isso responde duas perguntas da seção 9 da spec do Kit:
- **"A entrada num Setor `UtilizaKit` que não é para a montagem do pai".** Uma folha cujo próprio Roteiro
  tem passo na Solda entra em `NoSetor`, não em `AguardandoMontagem`: a posição, que a Fase 3 criou, já
  separa os dois casos. A folha não é validada como conjunto e não conta nos conjuntos à espera do pai.
- **"Qual das duas ações da regra 22 o limite pelo total montado trava".** Com a D1, a saída limitada ao
  total montado vale por construção para todo nó, e não há validação nova em terminar nem em mover.

Um filho de Kit cujo pai começa num Setor **sem** `UtilizaKit` segue como "Item pronto" comum. Uma Peça de
Kit não tem pai e segue para a expedição como hoje.

### D4 — Trocar o Tipo do Agrupamento ou a marca `UtilizaKit` com produção em andamento: livre

As duas trocas continuam livres, e a regra vale **da troca em diante**. O conjunto completo é verificado
só no momento da entrega. O que já está em `AguardandoMontagem` fica onde está, e o início do pai continua
consumindo o que houver. A tela de Tarefas recalcula na carga seguinte: os filhos passam dos grupos "Em
{Setor}" para os cartões de Kit, ou o inverso. Não há estado gravado que fique incoerente, porque a tarefa
e a regra são calculadas.

Descartada: **recusar a troca com movimento**. Protegia contra o Kit que vira Avulso no meio, mas travava a
correção de cadastro, e o Setor é compartilhado por todos os Pedidos.

*Nota (2026-10-09, decisão do usuário na implementação, anexada sem reescrever a D4):* trocar o Tipo do
Agrupamento de Avulso para Kit, ou marcar `UtilizaKit` num Setor com Kit já em produção, pode deixar a
espera **desalinhada** entre os filhos. Exemplo: um pai de quantidade 2, com 7 aguardando montagem de um filho
de razão 4 e 1 de um filho de razão 1. Pela contagem da D5, os conjuntos à espera são ⌈7 ÷ 4⌉ = 2, e o teto
fica em 0: o Kit some das Tarefas, a entrega para esse pai é recusada, e o teto continua 0 mesmo depois de
iniciar o pai. A troca não é o único caminho. A entrada, que só aceita conjuntos completos, e o início, que tira
de cada filho `N × QuantidadePorPai`, mantêm sozinhos a espera na proporção das razões; ela só se desalinha
quando se forma ou muda sem passar por eles, ou quando a razão muda depois. Além da troca, por exemplo: o
estorno de uma linha só de uma entrega de Kit (o estorno é feito linha a linha e não confere o conjunto) e a
edição da `QuantidadePorPai` de um filho que tem conjuntos à espera. É o mesmo mecanismo da perda dentro da
Solda (uma espera que deixou de ser feita de conjuntos inteiros), e vai com ela para a Fase 5 (D5). O código
não muda; a limitação está escrita na regra 25 do `01` e no item da Fase 5 do `06`.

### D5 — Os conjuntos à espera são contados pelo filho mais adiantado; a perda nos tetos fica para a Fase 5

A regra 25 define os conjuntos à espera como "os que entraram e ainda não foram montados, não o mínimo por
filho". A 3B os conta como o **maior**, entre os filhos diretos, de
`⌈AguardandoMontagem(c) ÷ QuantidadePorPai(c)⌉`, somado em todos os Setores com `UtilizaKit`. Sem perda,
com toda entrada em conjunto completo, isso é exatamente o número de conjuntos que entraram. Um conjunto que
perde parte dentro da Solda continua contado como à espera, e por isso não se completa com refugo novo.

**Como a perda do próprio nó entra nos dois tetos** (seção 9 da spec do Kit) passa para a **Fase 5**. Na 3B
não existe como registrar perda (`Perda`, o destino perdido e o `Descarte` nascem na Fase 5), e uma regra
escrita agora não teria código que a exercitasse.

### D6 — Validação na entrega existente, não num endpoint próprio

O conjunto completo é validado no `EntregaUseCase`, com o contrato de lista de hoje. A tela monta a lista a
partir do número de conjuntos escolhido.

O motivo: um caminho só de entrega, com as travas que já existem (Pedido fechado, saldo, pai sem Roteiro);
um Kit e itens soltos cabem numa transação só, que é o que o Movimentador faz; e a regra mora num lugar, com
a mesma função da calculadora alimentando a tela e a recusa.

Descartadas:
- **Endpoint "entregar N conjuntos do Kit X"**, com o servidor derivando as quantidades: dois caminhos de
  entrega com as mesmas travas, e a entrega de lista continuaria aceitando filho de Kit solto a menos que
  também fosse travada — a regra ficaria em dois lugares.
- **Só na tela:** a regra 25 é validação do sistema, não organização.

### D7 — "Kits montáveis" e "Kits incompletos" na tela de Tarefas

- **Um cartão por Kit (pai)**, com uma caixa **Levar** e um campo **Conjuntos**. O Movimentador escolhe
  quantos conjuntos, não quanto de cada filho: a tela não consegue compor um conjunto incompleto.
  Descartada: uma caixa por filho mais "marcar o Kit todo", com quantidade editável por filho — permitiria
  compor o conjunto que a API recusa.
- **O filho de Kit aparece só no cartão do Kit**, não nos grupos "Em {Setor}". A regra 23 já o chama de
  "só informativo" como Item pronto; listá-lo nos dois lugares duplicaria o mesmo item.
- **O Kit que ainda não fecha um conjunto aparece em "Kits incompletos"**, informativa, no fim da tela,
  para nada pronto sumir da tela do Movimentador. Descartado: mostrar só os montáveis.
- **"Kits incompletos" vem recolhida, com a contagem no título.** Ressalva do usuário: de 60% a 70% da
  produção passa pela Solda, e a seção pode ficar grande. Se ficar demais, **essa parte** se revisita
  sozinha; o resto do desenho fica.

### D8 — Pílula "Kit" em azul cheio, só na Fila do Setor

A marca aparece nos cartões de nó de Agrupamento Kit **na Fila do Setor**, e em nenhuma outra tela: é onde
ela orienta trabalho. O tipo do Agrupamento continua em pílula neutra onde já aparece hoje
(`PedidoDetalhePage`).

A cor é **azul cheio com texto branco** (`#1D4ED8` com `#FFFFFF`, 6,70:1). Foram comparados quatro azuis num
cartão da Fila, ao lado de "Pausado" e "Atrasado"; o usuário escolheu o cheio por destacar mais que os fundos
pastel. A forma também separa: as pílulas de estado são tingidas, e esta é cheia.

É a primeira pílula **de categoria com cor própria**: até aqui toda categoria era neutra e a cor era
reservada a estado. O azul é **identidade de Kit, nunca estado**, na regra "cor de identidade nunca significa
estado; cor de estado nunca decora" do `CLAUDE.md`. O token entra com par de contraste declarado, medido pela
guarda `contraste.test.ts`.

### D9 — C2: o título da página do Agrupamento com o Pedido

Decidido em 2026-10-08, na conferência do import, e escrito na seção da 3B do `06`. O título passa a
"**Pedido X** — {código do Agrupamento} — {Avulso|Kit}", com "Pedido X" como link para a página do Pedido. O
número vem em `GET /agrupamentos/{id}` (campo novo), não numa segunda requisição do front. Motivo do usuário:
os Agrupamentos devem ganhar códigos de lote interno da empresa, e é preciso ver qual Pedido está sendo
alterado.

## 3. Schema

- `dbo.Setor` ganha `UtilizaKit BIT NOT NULL`, com default `0`. Primeiro em `specs/02-modelo-de-dados.sql`,
  depois no mapeamento EF.
- **`db/alter-fase-3b.sql`**, idempotente, no formato dos anteriores (`-b -f 65001`). Leva um banco anterior
  até o `02`: só a coluna, com o default. Não há dado a transformar e o banco **não** precisa ser
  regenerado. O bloco de comando entra no `CLAUDE.md`, ao lado dos outros alters.
- Nada mais muda no schema: o conjunto completo e as tarefas são calculados do livro.

## 4. Backend

### 4.1 Cadastro de Setor

`UtilizaKit` entra no corpo de criação e de edição e na resposta de `GET /setores`. A permissão de escrita
continua a de hoje (`Administrador`).

### 4.2 Guarda `PaiJaIniciado`

`POST /estrutura/{id}/filhos` recusa com **409 `PaiJaIniciado`** quando o nó `id` tem saída líquida de "a
iniciar" maior que zero. A mensagem nomeia o nó e diz que ele já entrou em produção. A checagem usa o mesmo
`SaidoDeAIniciar` da calculadora que a edição de nó já usa para recusar `QuantidadeAbaixoDoMovimentado`, no
mesmo caso de uso (`MontagemDeEstruturaUseCase`).

### 4.3 Conjunto completo no `EntregaUseCase`

Depois de calcular o destino de cada item (como hoje), os itens cujo destino é `AguardandoMontagem(S)` com
`S.UtilizaKit` e cujo nó pertence a Agrupamento Kit são agrupados pelo pai P. Para cada P:

1. **Todos** os filhos diretos de P aparecem na lista.
2. A soma das quantidades de cada filho c, na lista, é `N × QuantidadePorPai(c)` para um **mesmo N inteiro
   ≥ 1**, comum a todos os filhos.

   Falhando 1 ou 2: **`ConjuntoIncompleto`**. A mensagem nomeia o pai e o filho que falta ou que não fecha.
3. `N ≤ teto(P)`, com `teto(P) = Quantidade(P) − TotalMontado(P) − ConjuntosÀEspera(P)` (D5). No
   redirecionamento, a quantidade que está sendo movida sai da conta dos conjuntos à espera, porque ela já
   estava contada.

   Falhando 3: **409 `AlemDoQueOPaiPrecisa`**, com o teto na mensagem.

Os itens com outros destinos, de outros pais ou de Agrupamento Avulso seguem exatamente as regras de hoje, na
mesma transação. O tipo HTTP de `ConjuntoIncompleto` (400 ou 409) fica para o plano, com o critério dos
códigos existentes em `CodigosDaExecucao`.

### 4.4 Calculadora: `KitsDaColeta`

Função nova, pura como as outras, usada pela consulta das tarefas e pela validação da entrega. Para cada pai
P de Agrupamento Kit cujo **primeiro passo** fica num Setor com `UtilizaKit`:

- **coletável(c)**: o que o filho c tem aguardando coleta no **último** passo do Roteiro dele;
- **N máximo** = `min(min_c ⌊coletável(c) ÷ QuantidadePorPai(c)⌋, teto(P))`;
- **montável** quando N máximo ≥ 1. **Incompleto** quando N máximo = 0, algum filho tem coletável > 0 e
  teto(P) > 0; para cada filho, o que falta para o primeiro conjunto
  (`max(0, QuantidadePorPai(c) − coletável(c))`);
- por filho: a origem (Setor e passo), a quantidade para N e se o Setor de origem é o do primeiro passo de
  P ("já está no Setor de destino").

Os filhos desses pais saem de `ColetasPendentes` (o "Item pronto"). A **sobra** (regra 30) não muda.

### 4.5 Respostas

- `GET /tarefas` ganha os Kits, separados em montáveis e incompletos, cada um com o pai (o mesmo resumo de
  nó dos itens), o Setor de destino, o N máximo e os filhos. Hoje a resposta é uma lista de grupos por
  Setor; acrescentar os Kits muda a forma dela (um objeto com os grupos e os Kits). O único consumidor é a
  `TarefasPage`, que muda junto nesta fase.
- `GET /tarefas/contagem` soma cada Kit **montável** como uma tarefa; os incompletos não contam.
- O resumo de nó da Fila (`NoResumoDto`) ganha o tipo do Agrupamento, para a pílula "Kit".
- `GET /agrupamentos/{id}` ganha o número do Pedido.
- `specs/05-api-endpoints.md` registra os campos novos e os três códigos novos.

## 5. Telas

### 5.1 Tarefas

De cima para baixo:

1. **"Kits montáveis"** primeiro, porque é a tarefa principal de quem trabalha com Kit.
2. Os grupos **"Em {Setor}"** de hoje, sem os filhos de Kit.
3. **"Kits incompletos (N)"** por último, num `<details>` recolhido.

**O cartão do Kit montável** mostra o nome do pai, o caminho (Pedido › Agrupamento › …), "Destino: {Setor}
(início de {pai})", "Dá para levar N conjunto(s)", a pílula "Pausado" quando for o caso e a caixa **Levar**.
Marcada a caixa, abre o campo **Conjuntos**, inteiro de 1 a N, que já vem com N. Abaixo dele, cada filho com
a origem ("de {Setor}, passo k" ou "já está em {Setor}") e a quantidade, que acompanha o N digitado.

**O cartão do Kit incompleto** mostra os filhos com "pronto: X" e "falta para 1 conjunto: Y" (ou "nenhum
pronto"), sem caixa de marcar.

**"Marcar todos"** marca também os Kits montáveis, cada um com o N máximo. O botão passa a contar Kits e
itens separados ("Entregar 2 Kits e 3 itens"), e a lista enviada é uma só.

**Os filtros** (`FiltroDeDemanda`, facetas de Material e Pedido) valem para os cartões de Kit, avaliados
pelo pai e pelos filhos prontos, como o cartão de montagem da Fila já é: o Material mora nos filhos, e avaliar só o
pai esconderia o Kit de quem filtra pela chapa do filho (decisão P5 do plano da Fase 3B). O comportamento com os
marcados ocultos pelo filtro é o de hoje.

**A recusa da API** usa o banner de erro da entrega que já existe, e a tela recarrega no 409, como hoje.

### 5.2 Fila do Setor

- Pílula **"Kit"** (tom novo `kit` da primitiva `Pilula`) em todo cartão de nó de Agrupamento Kit, em
  qualquer seção da fila.
- **Redirecionamento de Kit:** quando o destino do redirecionamento é um Setor com `UtilizaKit` e o
  Agrupamento é Kit, o "Levar ao Setor P1" deixa de ser por filho e passa a ser um botão **por Kit**, que
  leva os conjuntos completos que estão ali.

### 5.3 Setores

O painel de criar e editar ganha a caixa **"Utiliza Kit"**. A linha da lista ganha a pílula **neutra**
"Utiliza Kit": é atributo do Setor, não identidade de Kit (D8).

### 5.4 Agrupamento

- **C2:** o título "Pedido X — {código} — {Avulso|Kit}", com "Pedido X" como link. Hoje o `titulo` da
  `Pagina` é `string`; a primitiva passa a aceitar o link, sem container próprio na tela.
- O acrescentar filho recusado (`PaiJaIniciado`) mostra a mensagem no banner do próprio painel, como as
  outras recusas de escrita. O botão continua visível: a árvore não sabe se o nó já foi iniciado, e o 409 é
  a fronteira.

## 6. Documentos

Atualizados na própria branch:

- **`01`:** regra 23 (Kits montáveis e incompletos); regra 24 sem a "Exceção conhecida" e com a guarda da D1;
  regra 25 com os conjuntos à espera da D5 e com o redirecionamento; nota de que a perda nos tetos é da Fase
  5; e a nota de "Pontos ainda em aberto" sobre a trava, que cita a exceção.
- **`02`:** a coluna `Setor.UtilizaKit`.
- **`04`:** os fluxos do Movimentador (Tarefas) e do operador (Fila).
- **`05`:** os campos e os códigos novos.
- **`06`:** a 3B concluída; na Fase 5, dois itens novos: a perda nos tetos (D5) e o filho acrescentado a nó
  iniciado (D1, com a ideia do usuário).
- **`CLAUDE.md`:** o azul de Kit como cor de identidade de categoria; o invariante da regra 24 sem a
  exceção; o bloco do `alter-fase-3b.sql`.
- **Spec do Kit** (`2026-09-15-kit-montagem-e-movimentacao-design.md`): errata que fecha a seção 9 da 3B,
  apontando para esta spec.

## 7. Testes

- **Application:**
  - `KitsDaColeta`: montável; incompleto, com o que falta; teto pelo filho mais adiantado; "já está no
    Setor de destino"; pai sem `UtilizaKit` fora; Avulso fora.
  - Entrega: filho faltando; N diferente entre os filhos; N não inteiro; além do teto; lista mista com Kit e
    item solto; redirecionamento contado uma vez; e os casos que **não** mudam — Avulso, pai em Setor sem
    `UtilizaKit`, folha indo para `NoSetor` na Solda.
  - Guarda `PaiJaIniciado`, e a volta a aceitar depois de estornar todo início.
- **Infrastructure:** o mapeamento de `UtilizaKit`.
- **Api:** ponta a ponta dos três códigos novos e dos campos novos.
- **Front:** cartão montável e campo Conjuntos; incompletos recolhidos com a contagem; "Marcar todos" e a
  lista enviada; pílula "Kit" na Fila e o par de contraste do token; caixa "Utiliza Kit" em Setores; título do
  C2 com o link; o 409 de `PaiJaIniciado` no painel.

## 8. Critério de pronto

- Um Kit de três níveis é montado de baixo para cima, com montagem parcial.
- O sistema recusa o conjunto incompleto, a entrada além do que o pai precisa e o filho acrescentado a nó
  iniciado.
- "Kits montáveis" mostra o Kit, e ele some quando é levado; o incompleto aparece recolhido em "Kits
  incompletos".

**Conferência no navegador:** é task **do usuário**, na regra do `CLAUDE.md`, e a review de branch só é
despachada depois dela. A massa é preparada por script de API (a Solda marcada com `UtilizaKit` e um Pedido
Kit de três níveis com filhos espalhados por Setores), fora do `db/seed-demo.sql`, para o demo não mudar de
comportamento.

## 9. Fora de escopo

- A perda nos tetos (D5) e o filho acrescentado a nó iniciado (D1): Fase 5.
- A pílula "Atrasado" na Fila e nas Tarefas: item seguinte, decidido na spec da data de entrega.
- O push: Fase 3C.
- A revisão do volume de "Kits incompletos", se ficar demais (D7).
