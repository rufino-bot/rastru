# Fase 1F — Cadastro sob demanda

Spec de design, 2026-09-06.

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

**Não é problema para quem só lê.** Os cinco formulários já são renderizados sob
`{podeEscrever && …}` ([usePermissao.ts](../../../web/src/auth/usePermissao.ts)), então a tela de
um perfil sem escrita já está limpa hoje. O defeito atinge quem cadastra — `Administrador` e
`PCP` — e foi assim que apareceu, usando a aplicação com o usuário `admin`.

### O gancho que já existe

`Pagina` declara desde a Fase 1D uma prop `acao`, documentada como *"Ação principal da tela,
alinhada ao título (ex.: 'Novo pedido')"*, com teste em
[`Pagina.test.tsx:17`](../../../web/src/components/Pagina.test.tsx). **Nenhuma tela de lista a
usa.** O único consumidor é `AgrupamentoDetalhePage`, que a reaproveita para outra coisa (o
`Id NN` discreto), com comentário reconhecendo o desvio.

E a Task 8b da Fase 2 escreveu à mão, na mesma tela, um "painel de escrita" que abre sob demanda,
com `<h2>`, subtítulo e botão Cancelar — exatamente a forma que esta fase precisa. As duas peças
do conserto já estão no projeto; falta ligá-las.

## Decisões

Todas do usuário, em 2026-09-06, durante o brainstorming.

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
5. **O painel da Task 8b migra para a primitiva nova.** A tela do Agrupamento fica com um
   mecanismo só. Risco de mexer em código recém-revisado, mitigado pelos testes da própria 8b, que
   continuam valendo e pegam regressão.
6. **Nome e posição: Fase 1F, executada depois da Fase 2.** A numeração `1x` marca a família —
   refinamento de interface, herdeira da 1D (que criou a prop `acao`) e da 1E. A posição no
   roadmap marca a execução. Descartada a renumeração de `2B` → `2C`: "Fase 2B" já é citada em
   `CLAUDE.md`, na regra 18 de `01-dominio-e-regras-de-negocio.md` e na spec da Fase 2, e o ledger
   passaria a falar de uma 2B que virou outra coisa.

## O desenho

### Estado normal da tela

Título, botão de ação à direita dele, e a partir daí a lista — com a barra de filtros logo abaixo
do cabeçalho, onde houver. **Nenhum bloco de campos no topo**, que é o que produzia a leitura de
filtro.

O botão obedece ao mesmo `usePodeEscrever(recurso)` que hoje envolve o formulário: para quem não
pode escrever, ele não existe, e a tela permanece como está hoje.

### O painel

Clicar no botão abre, como primeiro elemento abaixo do cabeçalho, um painel contendo:

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

**O painel não fecha no `Escape` e não prende o foco.** Ele não é modal: o resto da tela continua
utilizável e o `Cancelar` é o caminho de saída. Registrado para não ser re-decidido como omissão.

### Ciclo de vida

| Evento | Efeito |
|---|---|
| Clique no botão do cabeçalho | Abre o painel, foco no primeiro campo |
| `Cancelar` | Fecha, descarta o que foi digitado, limpa erro de escrita e o estado de reativação |
| Salvar com sucesso | Fecha, limpa o formulário, recarrega a lista |
| Salvar com conflito (409) | **Continua aberto**, com o erro e o botão "Reativar o existente" dentro dele |
| Salvar com falha de rede | Continua aberto, com o erro dentro dele |

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
   um salvar que falhou por conflito, e nesse caminho o painel continua aberto. Há testes que
   dependem do formulário conservar o que foi digitado depois de uma reativação bem-sucedida
   (`ComponentesPage.test.tsx`) — continuam válidos, porque o painel permanece aberto nesse fluxo.
2. **`EstadoVazio` mente depois desta fase.** As cinco telas do escopo dizem *"Use o formulário
   acima para criar o primeiro"* (ou variação), e o formulário deixa de estar acima. O texto passa a
   referenciar o botão. `SetoresPage.test.tsx` afirma essa frase literal e muda junto.
   `PedidoDetalhePage` também usa a frase e **não** muda: lá o formulário continua acima, dentro
   da sua seção.
3. **O `acao` da `AgrupamentoDetalhePage` já está ocupado** pelo `Id NN`. Os dois passam a dividir
   o slot, botão e Id lado a lado — `acao` aceita qualquer `ReactNode`.
4. **Exclusividade de painéis** na tela do Agrupamento: o "Nova Peça" entra na máquina de estado
   que já existe para editar/acrescentar/confirmar exclusão. Abrir um fecha os outros, nas duas
   direções — é o invariante que garante um único `PainelDeEscrita` no documento por vez, do qual
   os `getByTestId` da 8b dependem.

## Guardas

Por tela, quatro asserções:

1. o formulário **não** existe antes do clique — este é o matador da regressão: devolver o form ao
   topo deixa o teste vermelho;
2. existe depois do clique;
3. some no `Cancelar`;
4. some ao salvar com sucesso, e a lista mostra o item novo.

Na primitiva: título ligado ao form por `aria-labelledby`, `Cancelar` chama `aoFechar`, e o foco
chega no primeiro controle. O teste de foco mora aqui e não se repete nas telas.

Os testes existentes das cinco telas passam a operar depois de abrir o painel — é uma edição
mecânica e ampla, e o volume dela é o maior custo da fase.

## Critério de pronto

- As cinco telas abrem em estado de leitura, sem bloco de campos abaixo do título.
- `npm test` verde e `npm run build` limpo — o build faz parte do ciclo, erro de tipo em `.test.tsx`
  quebra o build sem quebrar a suíte.
- Verificação no navegador a 375px, incluindo a tela do Agrupamento com os dois painéis: nesta
  suíte, quem pegou o descumprimento de spec que 362 testes verdes não pegaram foi a verificação
  manual, e o `ItemDeCadastro` documenta uma armadilha de layout que o jsdom **não** consegue medir.
- Um perfil sem escrita continua vendo a tela sem botão e sem painel.

## Fora de escopo

- **Filtros e busca.** Decisão do usuário: fase própria, **depois da Fase 3**. O rastreamento por
  Setor nasce lá, e "onde a peça está agora" tende a ser o filtro mais útil do sistema — abrir a
  barra de filtros antes disso significa reabrir a mesma tela duas vezes. Essa fase futura herda o
  filtro pedido no brainstorming (pedidos que contenham determinada peça, por código de
  Componente), que precisa da árvore de estrutura preenchida pela Fase 2, e paga junto a dívida
  registrada na Fase 1E: `listarPedidos()` não é paginado, e a Home deriva resumo e lista do array
  inteiro.
- **`PedidoDetalhePage` e `ComponenteDetalhePage`.** Nelas o formulário já vive dentro de seção com
  `<h2>` — o defeito é bem menor, e a de Componente hospeda a receita padrão, a tela mais complexa
  do projeto.
- **Primitiva de mensagem de sucesso.** Consequência da decisão 4; se um dia a confirmação
  explícita for necessária, nasce ali.
- **Entrada da Fase 1F em `specs/06-roadmap-mvp.md`.** Fica para a abertura da fase: o arquivo está
  modificado na árvore de trabalho por outra sessão (Task 9 da Fase 2, documentação), e editá-lo
  agora colidiria com trabalho em andamento.
