# Fase 1F — Cadastro sob demanda e ordenação das listas

Spec de design, 2026-09-06. **Revista em 2026-10-01**, antes do plano — ver a seção seguinte.

## Revisão de 2026-10-01 — o que mudou e por quê

A spec nasceu em 2026-09-06 sobre `9ec40e6` (fim da Fase 1E) e esperou: a ordem de execução pôs a
Fase 3, a 3D e os filtros da demanda antes dela. Quando a fase abriu, a `main` estava em `75df075`,
**303 commits** depois (`git rev-list --count 9ec40e6..75df075`). A releitura, feita com o usuário,
mediu cada tela contra a `main` nova.

**O que continua de pé, medido:** nas cinco telas do escopo o `<form>` de cadastro ainda é o primeiro
filho de `<Pagina>`, na moldura de filtro; a prop `acao` da `Pagina` continua sem uso nas listas; o
painel de escrita da Task 8b continua escrito à mão na tela do Agrupamento. As seis decisões de
2026-09-06 não foram re-decididas.

**O que piorou:** a `PedidosPage` ganhou, nos filtros da demanda, um filtro de verdade
(`FiltroDeDemanda`) — e o formulário de cadastro está **acima** dele. É a inversão que esta spec
apontava em Componentes, agora também em Pedidos.

**O que os filtros quebraram aqui:** a decisão 4 prometia que, depois de salvar, *"a lista reaparece
com o item recém-criado nela"*. Com busca, filtros e paginação no servidor, isso deixou de ser
verdade: `PedidosPage` e `ComponentesPage` recarregam a **mesma** consulta, e um item novo que a
consulta corrente exclui — ou que cai noutra página — não aparece. O painel fecharia sobre uma lista
sem o item, e o fechamento é o único sinal de sucesso. O conserto virou escopo novo: a consulta volta
ao padrão ao salvar, e o padrão passa a ser "mais recentes" (decisões 7 a 11).

**Escopo acrescentado:**

- **Ordenação escolhida pelo usuário** nas quatro telas de lista (decisões 8 a 11) — pedido de
  2026-10-01, que nasceu da pergunta acima.
- **Cartão de lista inteiro clicável** em `FilaPage`, `PedidoDetalhePage` e `ComponentesPage` —
  decisão de 2026-09-28, na verificação manual da Fase 3, registrada no ledger e posta na 1F; esta é a
  primeira vez que ela entra numa spec.

**O que saiu:** a seção "Fora de escopo — Filtros e busca", que descrevia como futura uma fase já
feita (os filtros da demanda, PRs #25 e #26).

## O problema, medido

Nas quatro telas de lista — `SetoresPage`, `MateriaisPage`, `ComponentesPage`, `PedidosPage` — e
na `AgrupamentoDetalhePage`, o formulário de cadastro é o **primeiro filho** de `<Pagina>`, logo
abaixo do `<h1>`, sem título próprio, dentro da moldura
`rounded-lg border border-borda bg-superficie p-4`.

Essa é, letra por letra, a mesma moldura que a barra de filtros de `ComponentesPage` usa. O
resultado é que um bloco de campos aparece no topo da tela sem nada que o anuncie como cadastro
até o rótulo do botão, lá embaixo — e a leitura natural de "campos no topo, abaixo do título" é
*filtro*, não *cadastro*.

Em `ComponentesPage` a inversão é literal: o cadastro está **acima** da barra de busca de verdade.
Desde os filtros da demanda, o mesmo vale para `PedidosPage`, acima do `FiltroDeDemanda`.

**Não é problema para quem só lê.** Os cinco formulários já são renderizados sob
`{podeEscrever && …}` (`usePodeEscrever`, em `web/src/auth/usePermissao.ts`), então a tela de um
perfil sem escrita já está limpa hoje. O defeito atinge quem cadastra — `Administrador` e `PCP` — e
foi assim que apareceu, usando a aplicação com o usuário `admin`.

### O gancho que já existe

`Pagina` declara desde a Fase 1D uma prop `acao`, documentada como *"Ação principal da tela,
alinhada ao título (ex.: 'Novo pedido')"*, com o teste *"mostra a ação de cabeçalho quando ela
existe"* em `Pagina.test.tsx`. **Nenhuma tela de lista a usa.** O único consumidor é
`AgrupamentoDetalhePage`, que a reaproveita para outra coisa (o `Id NN` discreto).

E a Task 8b da Fase 2 escreveu à mão, na mesma tela, um "painel de escrita" que abre sob demanda,
com `<h2>`, subtítulo e botão Cancelar — exatamente a forma que esta fase precisa. As duas peças
do conserto já estão no projeto; falta ligá-las.

## Decisões

### De 2026-09-06 (brainstorming original)

Todas do usuário.

1. **Prioridade é a consulta.** Materiais e Setores serão carregados por script de importação, não
   pela tela. Componentes e Pedidos variam por perfil — há perfis que só consultam e perfis que
   consultam e cadastram. O regime permanente da tela é leitura.
2. **Botão no cabeçalho, painel inline.** Não modal. O `Confirmacao` de hoje não prende foco nem
   fecha no `Escape` — suficiente para um sim/não, insuficiente para um formulário —, e um modal
   de formulário briga com o teclado virtual no celular.
3. **Escopo: cinco telas.** As quatro de lista mais `AgrupamentoDetalhePage`. Esta entra porque
   hoje convive com **dois** padrões: o "Criar Peça" fixo no topo e o painel sob demanda logo
   abaixo. Deixá-la de fora consagraria a incoerência na tela mais recente do sistema.
4. **Salvar com sucesso fecha o painel.** O fechamento é o feedback: a lista reaparece com o item
   recém-criado nela. Descartadas: manter aberto com campos limpos (ambíguo entre "salvou" e
   "perdi o que digitei" sem confirmação explícita) e fechar com faixa de sucesso (exigiria uma
   primitiva de mensagem de sucesso, que o projeto não tem — só `BannerDeErro`).
   **Emenda de 2026-10-01:** "a lista reaparece com o item" só é verdade porque a consulta volta ao
   padrão ao salvar (decisão 7) e o padrão é "mais recentes" (decisão 9).
5. **O painel da Task 8b migra para a primitiva nova.** A tela do Agrupamento fica com um
   mecanismo só. Risco de mexer em código recém-revisado, mitigado pelos testes da própria 8b, que
   continuam valendo e pegam regressão.
6. **Nome e posição: Fase 1F, executada depois da Fase 2.** A numeração `1x` marca a família —
   refinamento de interface, herdeira da 1D (que criou a prop `acao`) e da 1E. A posição no
   roadmap marca a execução. Descartada a renumeração de `2B` → `2C`: "Fase 2B" já é citada em
   `CLAUDE.md`, na regra 18 de `01-dominio-e-regras-de-negocio.md` e na spec da Fase 2, e o ledger
   passaria a falar de uma 2B que virou outra coisa.
   **Emenda de 2026-10-01:** a posição de execução mudou depois (decisões de 2026-09-19 e
   2026-09-28) — hoje a 1F vem depois dos filtros da demanda e antes da Fase 3B (`CLAUDE.md`,
   "Ordem de implementação"). O nome não muda.

### De 2026-10-01 (releitura)

Todas do usuário.

7. **Salvar com sucesso devolve a consulta ao padrão** — "o comportamento padrão de refresh da
   página": busca vazia, filtros limpos, página 1, ordem "Mais recentes". Descartadas: manter a
   consulta (o item novo pode não aparecer) e navegar para o detalhe do item criado (só valeria para
   as telas que têm detalhe, e daria comportamentos diferentes entre as quatro).
8. **Ordenação escolhida pelo usuário nas quatro telas de lista** — Componentes, Pedidos, Setores,
   Materiais. Descartadas: estender à Fila do Setor e às Tarefas (lá a ordem serve ao trabalho do
   chão de fábrica, e "mais recente" significaria outra coisa) e restringir às duas paginadas.
9. **O padrão é "Mais recentes"** nas quatro, para o item recém-criado vir sempre no topo. Em
   Componentes ele vale **para todo consumidor de `GET /componentes`**, inclusive o `SeletorComBusca`
   — escolha explícita do usuário, preferida a um parâmetro que só a tela usasse.
10. **Um seletor só, "Ordenar por", com a direção embutida na opção** ("Código (A→Z)"). Descartados:
    campo + botão de direção (dois controles, e "Mais recentes" invertido vira "mais antigos") e as
    duas direções listadas por campo (a lista dobra).
11. **A ordem vai para a URL só em Pedidos**, que já guarda busca, página e filtros lá. Nas outras
    três ela fica no estado da tela, como a busca de Componentes já fica.
12. **Cartão inteiro clicável** em `FilaPage`, `PedidoDetalhePage` e `ComponentesPage` (decisão de
    2026-09-28, posta nesta fase).

## O desenho — painel sob demanda

### Estado normal da tela

Título, botão de ação à direita dele, e a partir daí a barra de consulta (onde houver) e a lista.
**Nenhum bloco de campos no topo**, que é o que produzia a leitura de filtro.

O botão obedece ao mesmo `usePodeEscrever(recurso)` que hoje envolve o formulário: para quem não
pode escrever, ele não existe, e a tela permanece como está hoje.

### O painel

Clicar no botão abre, como **primeiro elemento abaixo do cabeçalho** — acima da barra de consulta,
em Componentes e em Pedidos —, um painel contendo:

- um `<h2>` com o nome da ação, ligado ao `<form>` por `aria-labelledby` — o mesmo padrão que
  `ComponenteDetalhePage` já usa nas suas seções;
- um subtítulo opcional (a tela do Agrupamento identifica ali o nó sendo editado);
- os campos, sem alteração dos que existem hoje;
- o `BannerDeErro` de **escrita**, dentro do painel, ao lado do botão que o produz — regra herdada
  do m9 da Task 8, que existe para não empilhar erro de escrita com erro de carga mostrando a
  mesma frase genérica de rede sem dizer qual ação falhou;
- os botões de submit e Cancelar.

Ao abrir, o foco vai para o primeiro controle focável do painel. Sem isso, quem navega por teclado
ou usa leitor de tela clica no botão e o foco permanece no cabeçalho, com o painel recém-aberto
inalcançável a não ser tateando.

**Emenda de 2026-10-02:** "Ao abrir, o foco vai para o primeiro controle focável do painel" decide
só o foco ao **abrir**. O foco ao **fechar** foi decidido depois, pelo usuário, em 2026-10-01, depois
da revisão da branch: quando o painel fecha por `Cancelar` ou por um desfecho de sucesso, o foco
volta ao botão que o abriu — o do cabeçalho nas cinco telas; na edição de setor, o "Editar" daquele
setor, com o "Novo setor" do cabeçalho de reserva quando o "Editar" não volta com a recarga. O
controle focado sai do DOM junto com o painel, e sem isso o foco cairia no `<body>`: quem navega por
teclado ou leitor de tela voltaria ao topo da página sem aviso. O foco só é devolvido se tiver caído
no `<body>`: fechar o painel de Peça porque o do nó abriu, na tela do Agrupamento, não tira o foco do
campo do painel novo. O painel do nó, anterior a esta fase, não devolve foco. Quem devolve é o hook
`useDevolverFoco` (`web/src/hooks/`), chamado pela tela, e não a primitiva, porque o botão de origem
é da tela. O painel continua não sendo modal: devolver o foco ao fechar não prende o foco enquanto
ele está aberto.

**O painel não fecha no `Escape` e não prende o foco.** Ele não é modal: o resto da tela continua
utilizável e o `Cancelar` é o caminho de saída. Registrado para não ser re-decidido como omissão.

### Ciclo de vida

| Evento | Efeito |
|---|---|
| Clique no botão do cabeçalho | Abre o painel, foco no primeiro campo |
| `Cancelar` | Fecha, descarta o que foi digitado, limpa erro de escrita e o estado de reativação |
| Salvar com sucesso | Fecha, limpa o formulário, **devolve a consulta ao padrão** (decisão 7) e recarrega |
| Salvar com conflito (409) | **Continua aberto**, com o erro e o botão "Reativar o existente" dentro dele |
| Salvar com falha de rede | Continua aberto, com o erro dentro dele |

Na tela do Agrupamento não há consulta a devolver: salvar recarrega a árvore, como hoje.

**Emenda de 2026-10-02:** a linha do `Cancelar` vale com o painel parado. Enquanto a escrita está em
voo — o salvar e, nas telas que o têm, o "Reativar o existente" —, o `Cancelar` fica
**desabilitado**, por decisão do usuário de 2026-10-01, depois da revisão da branch. O motivo:
`Cancelar` não cancela a requisição, e a resposta chegaria depois sobre o que estivesse aberto — um
sucesso fecharia um painel reaberto e apagaria o digitado. Só a requisição de escrita conta; a
recarga que vem depois dela, não, com uma exceção anterior a esta fase: o painel do nó, na tela do
Agrupamento, segura o envio durante a recarga que segue um 409 de edição. Para isso a primitiva
ganhou a prop `enviando`, que cada tela passa. As linhas do `Cancelar` e de salvar com sucesso
ganharam também um efeito no foco, que volta ao botão que abriu o painel (emenda de mesma data em
«O painel»).

### A primitiva

Nasce `web/src/components/PainelDeEscrita.tsx`, com `PainelDeEscrita.test.tsx`, extraída do painel
que `AgrupamentoDetalhePage` já escreveu à mão — a mesma jogada que gerou `Confirmacao` na Task 8b,
e pela mesma razão: mais de um consumidor é o que torna a primitiva certa a nomear agora, em vez de
mais uma cópia colada na tela seguinte.

Ela guarda o que não varia: a moldura, o `<h2>` e sua ligação por `aria-labelledby`, o subtítulo
opcional, o botão `Cancelar` e o foco inicial. **Não** guarda os campos, que diferem em todas as
telas — mesmo raciocínio de `ListaDeCadastro`, que também não recebe os itens por prop.

O `data-testid` é **parâmetro, não fixo**. A tela do Agrupamento passa a ter dois painéis
possíveis, e os testes da Task 8b buscam `painel-de-escrita` por `getByTestId` — que lança quando
há dois no documento. O painel de editar/acrescentar conserva aquele identificador (preservando os
testes existentes) e o de criar Peça recebe outro.

**Emenda de 2026-10-01:** o painel de criar Peça **não** recebeu identificador. Ele chegou a ter um
na implementação, e a review da tela do Agrupamento o tirou: nada o usava, e o `CLAUDE.md` só aceita
`data-testid` onde o alvo não tem papel nem nome — e o `<form>` do painel tem os dois, pelo
`aria-labelledby` do `<h2>`. Os testes o acham por `getByRole('form', { name: 'Nova Peça' })`. O
objetivo deste parágrafo, nunca dois `painel-de-escrita` no documento, é cumprido pela ausência do
atributo no painel de Peça; a «Exclusividade de painéis», em «Os detalhes que vão doer», garante
além disso um painel só por vez.

O estado aberto/fechado fica em `useState` na tela. Um hook para um booleano seria abstração vazia.

### Por tela

| Tela | Botão no cabeçalho | Título do painel | Submit |
|---|---|---|---|
| `SetoresPage` | Novo setor | Novo setor | Adicionar |
| `MateriaisPage` | Novo material | Novo material | Adicionar |
| `ComponentesPage` | Novo componente | Novo componente | Adicionar |
| `PedidosPage` | Novo pedido | Novo pedido | Abrir pedido |
| `AgrupamentoDetalhePage` | Nova Peça | Nova Peça | Criar Peça |

Os rótulos de submit são os de hoje e não mudam: "Abrir pedido" e "Criar Peça" são vocabulário de
domínio, não estilo.

### Os detalhes que vão doer

1. **"Reativar o existente"** (Setores, Materiais, Componentes) hoje vive **fora** do formulário,
   como irmão dele. Passa para dentro do painel, junto do banner de erro: ele só aparece depois de
   um salvar que falhou por conflito, e nesse caminho o painel continua aberto.
   **Corrigido em 2026-10-01, na escrita do plano:** esta frase dizia que testes de
   `ComponentesPage.test.tsx` dependem de o formulário *conservar* o digitado depois de uma
   reativação bem-sucedida. É o contrário — o teste *"reativar com sucesso limpa o formulario"*
   afirma que ele é limpo, e o código faz isso nas três telas. Reativar com sucesso é um desfecho de
   sucesso como o salvar: fecha o painel e devolve a consulta ao padrão (decisão D7 do plano da 1F).
2. **`EstadoVazio` mente depois desta fase.** As cinco telas do escopo dizem *"Use o formulário
   acima para criar o primeiro"* (ou variação), e o formulário deixa de estar acima. O texto passa a
   referenciar o botão. `SetoresPage.test.tsx` afirma essa frase literal e muda junto. Em Pedidos só
   a variante "não há pedidos" cita o formulário; a de "nenhum pedido com estes filtros" não cita e
   fica como está. `PedidoDetalhePage` também usa a frase e **não** muda: lá o formulário continua
   acima, dentro da sua seção.
3. **O `acao` da `AgrupamentoDetalhePage` já está ocupado** pelo `Id NN`. Os dois passam a dividir
   o slot, botão e Id lado a lado — `acao` aceita qualquer `ReactNode`.
4. **Exclusividade de painéis** na tela do Agrupamento: o "Nova Peça" entra na máquina de estado
   que já existe para editar/acrescentar/confirmar exclusão. Abrir um fecha os outros, nas duas
   direções — é o invariante que garante um único `PainelDeEscrita` no documento por vez, do qual
   os `getByTestId` da 8b dependem.

## O desenho — ordenação

### Opções por tela

| Tela | Opções ("Ordenar por") | Onde ordena |
|---|---|---|
| Componentes | **Mais recentes** · Código (A→Z) · Descrição (A→Z) | servidor |
| Pedidos | **Mais recentes** · Número (A→Z) · Cliente (A→Z) | servidor |
| Setores | **Mais recentes** · Nome (A→Z) | cliente |
| Materiais | **Mais recentes** · Código (A→Z) · Descrição (A→Z) | cliente |

Em negrito, o padrão.

**"Mais recentes"** é `Id` decrescente em Componentes, Setores e Materiais — nenhuma das três tem
data de criação, e o `Id` é `IDENTITY`, então a ordem de inserção. Em **Pedidos** é a ordem de hoje,
`DataAbertura` decrescente com `Id` decrescente desempatando: a data de abertura **é** a data de
criação (o caso de uso grava `DateTime.UtcNow`), e é a data que a lista mostra — ordenar por `Id` ali
poderia contrariar a data visível numa massa de demonstração com datas atribuídas.

### Contrato no servidor (as duas paginadas)

- `GET /componentes` ganha `?ordem=recentes|codigo|descricao`; `GET /pedidos` ganha
  `?ordem=recentes|numero|cliente`.
- **Ausente vale `recentes`.** Em Componentes isto muda a ordem padrão do endpoint (hoje `Codigo`)
  para **todo** consumidor (decisão 9): a tela, o `SeletorComBusca` (criar Peça, acrescentar filho,
  receita padrão) e a Home — esta só lê o total, e não muda.
- **Valor desconhecido responde 400** `{ "erro": "..." }`, com a frase nomeando o valor recusado —
  o mesmo contrato do `status` fora dos cinco em `GET /pedidos`. Os valores são os literais acima,
  em minúsculas.
- **Toda opção termina em ordem total**, que a paginação por `Skip`/`Take` exige: Código e Número já
  são únicos (`UQ_Componente_Codigo`, `UQ_Pedido_Numero`); Descrição e Cliente repetem, e desempatam
  por `Id` decrescente.

### Setores e Materiais: no cliente, endpoints intocados

As duas telas recebem a lista inteira, e `GET /setores` e `GET /materiais` alimentam outros
consumidores — o editor de roteiro do nó, a receita padrão do Componente, a `FilaPage`, a faceta de
Material da `PedidosPage`. Mudar a ordem padrão desses endpoints reordenaria esses seletores sem
ninguém ter pedido. A tela ordena o que recebeu; os endpoints ficam como estão.

### A primitiva `SeletorDeOrdem`

Nasce em `web/src/components/`, com teste próprio: um `Campo` de rótulo "Ordenar por" com um
`<select>`, no mesmo desenho do "Por página" de Componentes. Recebe as opções da tela (valor e
rótulo) e o valor corrente, devolve a escolha por `aoMudar`; não busca nada e não sabe de URL — o
mesmo contrato controlado do `FiltroDeDemanda`. É primitiva, e não `<select>` na tela, porque são
quatro consumidores e o `CLAUDE.md` proíbe campo escrito à mão.

### Estado e posição

- **Componentes e Pedidos:** a ordem entra no `useBuscaPaginada` pelo parâmetro `filtros`, que
  compara por valor e volta à página 1 quando muda — o comportamento certo ao trocar a ordem.
- **Pedidos:** a ordem vai para a URL junto de busca, página e filtros. O padrão **não** é escrito
  na URL (`/pedidos` limpa é "Mais recentes"). Devolver a consulta ao padrão depois de salvar é o
  que o botão "Limpar filtros" do estado vazio já faz (busca e seleção), mais a ordem. Valor desconhecido lido da URL cai no padrão e não é
  enviado ao servidor — mandá-lo produziria um 400 por causa de um link velho.
- **Setores e Materiais:** `useState` na tela, e a ordenação é um `sort` sobre a lista recebida
  (comparação de texto com `localeCompare` em `pt-BR`; "Mais recentes" por `id` decrescente).
- **Posição:** na barra de consulta, onde ela existe — em Componentes ao lado de "Por página", em
  Pedidos junto do `FiltroDeDemanda`. Em Setores e Materiais, que não têm barra, o seletor fica
  sozinho logo acima da lista.
- **Perfil sem escrita vê o seletor**: ordenar é leitura.

## O desenho — cartão inteiro clicável

| Tela | O link cobre hoje | Botão no mesmo item? |
|---|---|---|
| `FilaPage` (escolher o Setor) | só o nome | não |
| `PedidoDetalhePage` (Agrupamentos) | só o código | sim, "Excluir" |
| `ComponentesPage` | só "código — descrição" | sim, "Inativar"/"Reativar" |

A `PedidosPage` já tem o cartão inteiro clicável — a `LinhaDePedido` estende a área do link ao
`<li>` com um pseudo-elemento absoluto sobre o item inteiro — e não tem botão no item. É o modelo.

**O obstáculo é a armadilha documentada em `ListaDeCadastro`**, medida em Chrome na review da Task 8
da Fase 1D: quando o item tem `acao`, o overlay do link cobre o `<li>` e **engole** o clique no
botão. As duas telas com botão restringiram o link ao texto exatamente por isso, e o comentário da
armadilha já registra a saída — a `acao` num wrapper posicionado acima do overlay.

1. **O conserto vai na primitiva, uma vez só.** `ItemDeCadastro` passa a renderizar a `acao` dentro
   de um wrapper posicionado e empilhado acima de qualquer overlay do conteúdo. Setores e Materiais
   (botão, sem link) não mudam de aparência. O comentário da armadilha deixa de ser aviso e passa a
   dizer o que a primitiva faz — e continua **descrevendo** a classe de empilhamento em vez de
   escrevê-la, porque o scanner do Tailwind lê comentário e planta a regra no CSS.

   **Emenda de 2026-10-02:** o wrapper deixou de ter classe de empilhamento. Ele é só
   **posicionado**, com o índice de empilhamento automático, e o comentário passou a dizer isso. Com
   índice próprio, o wrapper empatava com a lista aberta do `SeletorComBusca`, que tem o mesmo
   índice, e o empate se resolve pela ordem do DOM: na `ComponenteDetalhePage`, o seletor de
   "Componente filho" abre para baixo, sobre a lista de Materiais da receita, e os botões "Remover…"
   dela, que vêm depois no DOM, pintavam por cima das opções e tomavam o clique. Sem índice, o
   wrapper continua acima do overlay do link — entre elementos posicionados sem índice, quem vem
   depois na ordem do DOM pinta por cima, e o wrapper vem depois do link — e fica abaixo de toda
   camada que tem índice, como aquela lista. Medido no Chromium com `document.elementFromPoint` no
   centro do botão, nas três formas: com índice, o botão ganha da lista; sem índice, a lista ganha
   do botão e o botão ganha do overlay; sem posição, o overlay ganha do botão. O cuidado de
   **descrever** as classes de empilhamento em vez de escrevê-las continua no comentário, pelo mesmo
   motivo. E a prova continua sendo do navegador, como diz o item «A prova não pode ser a suíte».

2. **As três telas adotam o overlay da `LinhaDePedido`** no link que já existe. O nome acessível do
   link continua sendo o texto dele. Os comentários de `ComponentesPage` e `PedidoDetalhePage` que
   justificam **não** usar o overlay saem, porque a razão deles deixa de existir.
3. **A prova não pode ser a suíte.** O jsdom não calcula layout — é o que o comentário da armadilha
   diz, e é por isso que ela ficou sem conserto até aqui. O critério de pronto inclui a verificação
   no navegador; na suíte entra o que ela consegue provar (o link existe, com o mesmo destino, e a
   `acao` continua alcançável por papel e nome).

Pedidos não muda (já funciona assim). Setores e Materiais não ganham link (não têm página de
detalhe).

## Guardas

**Backend** (`GET /componentes` e `GET /pedidos`): sem `?ordem` vale `recentes`; cada opção devolve
a ordem certa; o desempate por `Id` com Descrição ou Cliente repetidos; valor desconhecido dá 400.
As asserções são **escopadas pelas linhas que o próprio teste inseriu** (prefixo por teste), nunca
sobre a tabela inteira — asserção sobre contagem ou ordem global de tabela compartilhada é flaky por
construção (`CLAUDE.md`, seção do pré-requisito dos testes).

**`PainelDeEscrita`:** título ligado ao form por `aria-labelledby`, `Cancelar` chama `aoFechar`, e o
foco chega no primeiro controle. O teste de foco mora aqui e não se repete nas telas.

**Emenda de 2026-10-02:** "O teste de foco mora aqui" vale para o foco ao **abrir**, e esse teste
continua só na primitiva. As duas regras que vieram depois da revisão da branch (emendas de mesma
data em «O painel» e em «Ciclo de vida») têm teste também nas telas, porque dependem do que cada tela
passa:
as cinco têm teste de que o `Cancelar` devolve o foco ao botão de origem e de que ele fica
desabilitado com a escrita em voo; Componentes, Pedidos e Agrupamento afirmam também o foco
devolvido depois de salvar com sucesso, e Setores, depois de salvar a edição. O hook
`useDevolverFoco` tem teste próprio, e a primitiva, o do `Cancelar` desabilitado pela prop
`enviando`. Antes disso, nesta mesma fase, a tela do Agrupamento já testava o foco no primeiro campo
depois de trocar de nó com o painel aberto — remontagem por `key`, que é da tela, e não da
primitiva.

**`SeletorDeOrdem`:** rótulo acessível, opções da tela, valor corrente marcado, `aoMudar` com a
escolha.

**Por tela, o painel — quatro asserções:**

1. o formulário **não** existe antes do clique — este é o matador da regressão: devolver o form ao
   topo deixa o teste vermelho;
2. existe depois do clique;
3. some no `Cancelar`;
4. some ao salvar com sucesso, a consulta volta ao padrão e o item novo é o primeiro da lista.

**Por tela, a ordem:** trocar a opção reordena (cliente) ou envia `?ordem=` (servidor). Em Pedidos,
a ordem lida da URL é respeitada, um valor desconhecido na URL não é enviado, e salvar devolve a URL
a `/pedidos` limpa.

**Perfil sem escrita:** sem botão e sem painel; com o seletor de ordem.

Os testes existentes das cinco telas passam a operar depois de abrir o painel — é uma edição
mecânica e ampla, e o volume dela é o maior custo da fase.

## Documentação que muda junto

- `specs/05-api-endpoints.md`: `?ordem` nos dois endpoints, com o padrão e o 400.
- `specs/06-roadmap-mvp.md`: a seção da Fase 1F, que esta spec deixava para a abertura da fase.
- `CLAUDE.md`, seção "Interface": as primitivas `PainelDeEscrita` e `SeletorDeOrdem`, cada uma com o
  seu gatilho, no formato das entradas de `SeletorComBusca` e `FiltroDeDemanda`; e a contagem de
  `data-testid`, remedida com o comando registrado lá (o painel de criar Peça ganha um identificador).
  **Emenda de 2026-10-01:** não ganhou — ver a emenda da seção "A primitiva". A contagem foi
  remedida mesmo assim, porque o `painel-de-escrita` passou a chegar ao DOM pela primitiva.

## Critério de pronto

- As cinco telas abrem em estado de leitura, sem bloco de campos abaixo do título.
- `npm test` verde e `npm run build` limpo — o build faz parte do ciclo, erro de tipo em `.test.tsx`
  quebra o build sem quebrar a suíte.
- `dotnet build Rastreamento.slnx -warnaserror` com 0 avisos e `dotnet test Rastreamento.slnx -m:1`
  verde.
- Verificação no navegador, a 375px e em desktop:
  - as cinco telas em leitura, e o painel abrindo, cancelando e salvando;
  - a tela do Agrupamento com os dois painéis, um de cada vez;
  - a ordenação nas quatro telas, e o item recém-criado no topo depois de salvar;
  - o cartão clicável nas três telas — e, nas duas com botão, clicar no **centro** do botão executa
    a ação, clicar no resto do cartão navega.
- Um perfil sem escrita continua vendo a tela sem botão e sem painel.

## Fora de escopo

- **`PedidoDetalhePage` e `ComponenteDetalhePage` no painel sob demanda.** Nelas o formulário já
  vive dentro de seção com `<h2>` — o defeito é bem menor, e a de Componente hospeda a receita
  padrão, a tela mais complexa do projeto. (O cartão clicável de `PedidoDetalhePage` está **dentro**
  do escopo; o que fica fora é o formulário dela.)
- **Ordenação na Fila do Setor e nas Tarefas** (decisão 8).
- **Busca e página de Componentes na URL** (decisão 11).
- **Primitiva de mensagem de sucesso.** Consequência da decisão 4; se um dia a confirmação
  explícita for necessária, nasce ali.
