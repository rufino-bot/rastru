# 04 - Fluxos de Usuário

Cada fluxo abaixo deve virar um caso de uso na camada `Application` do backend
(ver `03-arquitetura-tecnica.md`) e uma tela (ou conjunto de telas) no frontend.

## 1. Cadastro de Pedido

*Perfil: PCP (pausar e retomar o Pedido: PCP ou Gestão)*

1. PCP cadastra Pedido (`Tipo = Fabricacao`), com Cliente, Número e **Data de entrega** (o prazo,
   obrigatório; qualquer data vale, inclusive no passado — regra 33 em `01`).
2. PCP cadastra N Agrupamentos para o Pedido (cada um com Tipo `'Kit'` ou `'Avulso'`).
3. Para cada Agrupamento, PCP monta a estrutura (`EstruturaItem`):
   - Pode puxar de um `Componente` padrão do catálogo (copia `ComponenteFilhoPadrao`,
     `ComponenteMaterialPadrao` e `ComponenteRoteiroPadrao` para dentro da estrutura
     real do Agrupamento, como ponto de partida editável).
   - Pode criar itens 100% customizados (sem `ComponenteId`), específicos deste Pedido.
   - Pode **importar a estrutura inteira de uma Peça** a partir do BOM exportado do CAD, conferindo-a
     antes de confirmar (subseção "Importar a estrutura de um BOM").
   - Para cada Peça (nó de topo do `EstruturaItem`), marca se ela `RequerRelatorioDimensional`.
4. Pedido fica com `Status = Aberto` até o primeiro apontamento de setor
   (`Status = EmProducao`).
5. Quando outro Pedido precisa passar na frente, **PCP ou Gestão pausam** o Pedido, com um motivo
   opcional, e o **retomam** depois (regra 31). A pausa não muda o status; ela só recusa o início
   de nós dele. A **lista** de Pedidos mostra da pausa só a pílula "Pausado", ao lado do status; quem
   pausou, desde quando e por quê aparecem no **detalhe** do Pedido, onde também ficam os botões
   Pausar e Retomar.
6. Para achar um Pedido, o PCP usa a **lista de Pedidos**, que é **paginada** (20 por página) e tem
   busca e filtro (spec `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md`, seção 7). A
   **busca** acha o Pedido pelo número, pelo cliente ou pelo **código de uma peça** de qualquer nó
   dele, inclusive de um Item filho ("em que Pedido está a CH-2150?"). O **filtro** combina **Status**,
   com o nome em português ("Em produção", "Aguardando expedição"), e **Material** — o material do nó
   do Pedido, não o da receita do catálogo; as opções de Material são as que aparecem em algum Pedido,
   e o filtro de Pedidos não mostra contagem por opção. Dentro de uma faceta vale OU; entre as facetas
   e a busca, E. Busca, filtros e página ficam na URL, então o F5 e o "voltar" do detalhe de um Pedido
   devolvem a lista como estava. Sem resultado, a tela diz "Nenhum pedido com essa busca ou esses
   filtros" e oferece "Limpar filtros"; sem nenhum Pedido cadastrado, diz "Nenhum pedido aberto". A
   Home não pagina Pedidos: lê o **resumo** do servidor (contagem por status e até cinco Pedidos não
   encerrados de prazo mais urgente, na seção **"Prazos de entrega"**), que conta todos os Pedidos e não
   só a primeira página.

   Cada Pedido da lista e da Home mostra o prazo ("entrega em dd/mm/aaaa") ao lado da data de abertura,
   e a pílula **Atrasado** (em tom próprio, roxo) quando o prazo é anterior a hoje e o Pedido ainda não
   encerrou: **vencer hoje não é atraso**, e `Concluido` e `Cancelado` nunca ficam atrasados. Quem decide
   é o servidor, com o "hoje" de Brasília (regra 33 em `01`); a tela não recalcula.

   A lista também é onde o Pedido nasce, e desde a Fase 1F
   (`docs/superpowers/specs/2026-09-06-fase-1f-cadastro-sob-demanda-design.md`) ela **abre em
   leitura**: não há campos no topo. Quem pode escrever (PCP e Administrador) usa o botão **Novo
   pedido**, no cabeçalho, que abre o cadastro (Código do pedido, Cliente e **Data de entrega**, os três
   obrigatórios) num painel acima da busca;
   "Cancelar" fecha o painel e descarta o digitado, e um erro ao salvar — número repetido, falha de rede —
   aparece dentro dele, que continua aberto. Quem não pode escrever não vê o botão. A lista se
   **ordena** por **Prazo de entrega** (a padrão: os não encerrados por prazo, do mais atrasado ao mais
   distante, e depois os encerrados, do prazo mais recente ao mais antigo), **Mais recentes** (data de
   abertura, a mais nova primeiro), **Número** ou **Cliente**, num seletor "Ordenar por" que todo perfil
   vê. A ordem vai na URL como a busca, os filtros e a página, e a padrão não é escrita lá: `/pedidos` sem
   nada é "Prazo de entrega". Abrir o Pedido com sucesso fecha o painel e leva a lista a **"Mais
   recentes"** (`?ordem=recentes`), com busca e filtros zerados e na página 1, para o Pedido
   recém-aberto aparecer no topo — a ordem por prazo o mandaria para o fim quando o prazo é distante.

   Na página do Pedido, quem pode escrever (PCP e Administrador) usa o botão **Editar pedido**, no
   cabeçalho, que abre um painel com número, cliente e data de entrega, e que vale **em qualquer
   status**, inclusive o prazo de um Pedido já concluído (o Pedido é documento e se corrige por edição).
   O número repetido aparece como erro dentro do painel. O cabeçalho mostra o prazo e, quando é o caso, a
   pílula Atrasado.

### Importar a estrutura de um BOM

*Perfil: PCP (e Administrador) — quem tem escrita em `estrutura`. Spec:
`docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`; contrato em
`05-api-endpoints.md`, seção "Importação da estrutura"; regra 32 em `01`.*

A árvore de uma Peça já existe pronta no CAD, e o BOM exportado dela (CSV ou XLSX) a traz com nível,
part number, descrição e quantidade. Importar não grava nada: gera um **rascunho** que o PCP confere. Um
arquivo é **uma Peça**, e a montagem de topo é a raiz dela.

1. Na página do **Agrupamento**, o PCP usa **Importar BOM** (ao lado de "Nova Peça") e escolhe o arquivo, de
   até 5 MiB. A tela recusa antes de enviar o que passa do limite. Se o arquivo tem problema — coluna
   ausente, quantidade inválida, nível que pula um degrau, o mesmo código com filhos diferentes, ciclo —,
   o painel lista os erros, uma linha por problema, com o número da linha do arquivo, e nada é
   criado — eles se acumulam por etapa (leitura, montagem da árvore, tamanhos), então corrigir o arquivo pode
   revelar os da etapa seguinte; o PCP corrige o arquivo e o escolhe de novo. Sem erro, abre a **tela de conferência**
   (`/importacoes/:id`).
2. A **conferência** é uma tela de **PC**: o BOM só existe no computador de quem cadastra, e no celular ela
   não quebra, mas não é otimizada. De cima para baixo:
   - o **painel do Componente selecionado**: o sólido (ver e enviar o STL), o casamento com o catálogo e,
     se a receita diverge, o comparativo e a escolha. A partir da largura `lg` ele fica numa coluna à
     direita, fixo enquanto a tela rola, e a faixa da Peça e a árvore ficam à esquerda, sempre à vista;
     abaixo disso é um bloco comum no alto da tela;
   - a **faixa da Peça**: quantidade e "Requer relatório dimensional", o resumo das pendências em pílulas
     (cada uma leva ao primeiro nó com aquela pendência), a lista do que falta para confirmar, **Confirmar**
     e **Descartar**;
   - a **árvore expandida**: código, descrição, quantidade por pai (editável na linha) e as pílulas de
     situação de cada nó. Clicar num nó o seleciona no painel; as ocorrências do mesmo código ficam
     marcadas juntas, para ficar visível que a decisão vale para todas.
3. O PCP resolve as pendências:
   - **Casamento.** O código do BOM é casado com o `Codigo` do catálogo, exato, sem diferenciar caixa. Sem
     casamento, o Componente é **novo**, com código, descrição e tipo editáveis (`Montagem` se tem filhos,
     `Fabricado` se é folha; o PCP pode marcar uma folha como `Bruto`, o que tira a exigência de sólido).
     Linha sem part number vira novo com código em branco, que é preciso preencher ou casar à mão. O PCP
     pode trocar o casamento por outro Componente. Descrição diferente num casado: vale a do catálogo, e a
     do BOM aparece ao lado. Um casado **inativo** aparece marcado e é **reativado na confirmação**.
   - **Sólido.** Todo Componente que não é `Bruto` precisa de sólido (regra 32); o PCP envia o STL de cada
     um na própria conferência. O enviado só substitui o do catálogo na confirmação. Um item que entra
     **só pela receita do catálogo** mostra o painel com o envio desabilitado: o sólido dele se envia no cadastro do
     Componente.
   - **Receita divergente.** Onde o catálogo tem receita diferente da do BOM, o PCP vê o **comparativo de um
     nível** (igual, quantidade diferente, só no BOM, só no catálogo) e escolhe, sem padrão marcado,
     "manter a receita do catálogo" ou "usar a receita importada" — com o efeito de manter o catálogo ("retira
     N itens do BOM e traz M do catálogo") à vista antes de decidir. A escolha vale para todas as ocorrências
     do Componente. Trocar o casamento zera a escolha: ela é feita depois de ver o comparativo do casamento novo.
     Depois da escolha, a linha da árvore passa a dizer qual receita foi escolhida ("Receita do catálogo" ou
     "Receita importada", sem o âmbar), e o resumo da faixa da Peça deixa de contar aquele código entre as
     divergências a decidir.
   - **Quantidades.** Corrige na linha (a tecla Enter ou sair do campo salva) e informa a quantidade da Peça.
4. **Confirmar** fica desabilitado enquanto houver bloqueio, e a lista diz o que falta. Confirmar cria os
   Componentes novos, reativa os inativos, grava sólidos e receitas no catálogo e cria a Peça, **idêntica**
   à que o "Nova Peça" criaria a partir do catálogo resultante; volta à página do Agrupamento. Se a receita
   de catálogo de algum Componente com escolha mudou desde que o PCP escolheu, a confirmação é recusada: a
   tela se atualiza com aviso e a escolha daquele código volta a ser pedida.
5. A conferência **sobrevive** a F5 e a fechar o navegador: na página do Agrupamento, a seção **Importações
   em conferência** lista os rascunhos (arquivo, autor, data) com **Continuar** e **Descartar**, e **outro
   PCP** pode continuar o de um colega. Se duas pessoas editam o mesmo rascunho, a escrita mais velha recebe
   aviso e a tela recarrega. Descartar apaga o rascunho e os sólidos enviados, e não deixa rastro no
   catálogo. **Não há expiração**: rascunho abandonado fica até alguém descartar.
6. Para mudar a **forma** da árvore (acrescentar, remover ou mover um nó), o PCP corrige no CAD e reimporta;
   a tela de conferência corrige só o que é do sistema (casamento, dados do Componente novo, quantidade,
   `Bruto`). O botão **Reimportar** fica na faixa da Peça, ao lado de Confirmar e Descartar, e abre um painel
   com o campo de arquivo (o mesmo do Importar BOM, com o mesmo limite de 5 MiB). A reimportação preserva o
   sólido enviado, o casamento e os dados do novo por código; as quantidades voltam ao que o arquivo diz. Se
   o arquivo tem problemas, as linhas aparecem no painel e o rascunho fica como estava.
7. Enquanto houver rascunho de importação, o Agrupamento **não pode ser excluído** — a tela de Pedido
   explica e manda descartar antes.

## 2. Apontamento em Setor

*Perfis: Operador, Movimentador e PCP (marcar o Setor que monta Kit: Administrador)*

> **Reescrito em 2026-09-24** pela spec da Fase 3
> (`docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`), que decidiu o que a
> nota de 2026-09-15 deixava em aberto. **Emendado em 2026-09-28** pela spec da Fase 3D
> (`docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`): montar deixou de
> ser ação — o pai é iniciado, e iniciá-lo consome os filhos (regra 24) —, o que torna a trava de
> montagem estrutural para todo nó que já tem filhos quando entra em produção; o **conjunto
> completo** na entrada de Setor com `UtilizaKit` (regra 25) e o caso do nó que ganha filho depois
> de iniciado continuam na Fase 3B.
>
> **Emendado em 2026-10-09** pela spec da Fase 3B
> (`docs/superpowers/specs/2026-10-09-fase-3b-kit-e-montagem-design.md`): os cartões de Kit em
> Tarefas (passos 4 e 10), a pílula "Kit" e o "Levar o Kit" na fila (passos 2 e 5), o filtro nos
> cartões de Kit (passo 8) e a marca "Utiliza Kit" do Setor (passo 11). O caso do nó que ganha
> filho depois de iniciado deixou de existir: acrescentar filho a nó já iniciado passou a ser
> recusado (regra 24).

1. **PCP** confere que todo nó tem Roteiro; nó sem Roteiro aparece como pendência na árvore, e o PCP
   o edita (regra 28).
2. **Operador** abre a fila do seu Setor e vê, nesta ordem, o que está em trabalho, o que está a
   iniciar ali, o que está aguardando montagem, o que está aguardando coleta e a sobra. O card de
   nó de Agrupamento Kit traz a pílula **Kit**, em azul cheio, em qualquer seção; é a única tela
   que a mostra.
3. Ao pegar o material para trabalhar num nó cujo primeiro passo é ali, **inicia** uma quantidade
   (regra 28). Ao terminar, **termina** a quantidade feita: ela passa a aguardar coleta. O lote é
   divisível, e o que se valida é a conservação de quantidade (nunca movimentar mais do que existe
   naquele ponto). Quando o Setor tem uma **atividade** cadastrada, os botões levam o nome dela
   ("Iniciar solda", "Terminar solda"); sem atividade, ficam "Iniciar" e "Terminar".
4. **Movimentador** abre Tarefas, vê os Itens prontos com o destino de cada um e **entrega**: no
   próximo passo; na montagem do pai, no **primeiro passo do Roteiro do pai**, que o sistema calcula
   — o Movimentador não escolhe o Setor —; ou, se for Peça no fim do Roteiro, no local de expedição
   (regra 29).

   O filho de Agrupamento Kit no último passo, cujo pai começa num Setor com **Utiliza Kit**, não
   vai sozinho e não aparece entre os Itens prontos: os filhos vão juntos, em conjuntos completos
   (regras 23 e 25). No topo da tela, **Kits montáveis** traz um cartão por pai, com o caminho,
   "Destino: {Setor} (início de {pai})", "Dá para levar N conjunto(s)" e cada filho com a origem
   ("de {Setor}, passo k", ou "já está em {Setor}" quando o filho terminou no próprio Setor do pai)
   e a quantidade. A caixa **Levar** abre o campo **Conjuntos**, um inteiro de 1 a N que já vem com
   N, e a quantidade de cada filho acompanha o número digitado: o Movimentador escolhe quantos
   conjuntos leva, não quanto de cada filho. No fim da tela, **Kits incompletos**, recolhida e com
   a contagem no título, mostra o Kit que o pai ainda precisa receber mas que não fecha um conjunto, com o que cada filho
   tem pronto e o que falta para 1 conjunto, sem caixa de marcar. Kits e itens marcados vão numa
   entrega só, e o botão os conta separados ("Entregar 2 Kits e 3 itens"). O sistema recusa o
   conjunto incompleto e o Kit além do que o pai ainda precisa receber, e a recusa aparece no aviso
   de erro da entrega.
5. **Operador** do primeiro passo do pai vê "Dá para iniciar N; falta iniciar X" e **inicia** o pai:
   iniciar é o que consome os filhos presentes no Setor e põe o pai em produção (regra 24). Não há
   mais "montar". Em outro Setor que ainda tenha filhos aguardando (entregues antes de o PCP editar
   o primeiro passo do pai), o card não oferece Iniciar: diz para onde levar os filhos. Se o pai é
   de Agrupamento Kit e começa num Setor com Utiliza Kit, quem entrega não leva filho a filho: o
   card oferece **Levar o Kit para {Setor}**, que pede o número de conjuntos e leva todos os filhos
   juntos — só os conjuntos completos que estão ali (regra 25).
6. Registro errado se corrige por **estorno**, pelo autor ou pelo PCP, enquanto a quantidade não
   tiver andado. O operador estorna **na própria fila**: as linhas de "Em trabalho", "Aguardando
   coleta" e a sobra do último passo oferecem "Estornar", que abre a lista curta dos registros que
   ele ainda pode estornar (ou vai direto à confirmação quando há um só). Estornar o início de um
   pai desfaz junto o consumo dos filhos.
7. Se o Pedido de um nó está **pausado** (fluxo 1, passo 5), o operador o vê no fim de "A iniciar
   aqui", marcado como pausado e sem o botão de iniciar — e o card de montagem do pai pausado
   também fica sem Iniciar; terminar, entregar e estornar continuam valendo para o que já começou.
8. **Operador e Movimentador filtram a demanda** na fila do Setor e em Tarefas (spec
   `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md`, seções 3 e 4): o botão **Filtrar**
   (com o número de filtros ativos) abre as facetas **Material** — o do nó, que o Corte usa para
   separar por espessura de chapa — e **Pedido**, e cada seleção aparece como pílula removível, com
   "Limpar". As opções vêm da resposta sem filtro — o que existe na fila (ou em Tarefas) —, com a
   contagem: a lista de opções não pula enquanto o operador marca, e a contagem de cada opção
   respeita o que está marcado nas *outras* facetas, de modo que a opção que deixaria a lista vazia mostra 0. Dentro de uma faceta
   vale OU (uma chapa reaproveitada serve a mais de um Pedido); entre as facetas, E. Uma opção
   marcada que sai da lista (a atualização periódica tirou o último item daquele Pedido) continua
   marcada, com 0, até o operador removê-la. O filtro age em todas as seções da fila; no card de
   **Aguardando montagem**, o card casa se o pai **ou** algum filho presente casar, e aparece
   inteiro; em Tarefas, o cartão de Kit, montável ou incompleto, casa do mesmo jeito, pelo pai ou
   por algum filho pronto. Um nó sem material — o Item ad-hoc, por exemplo — some quando há filtro de Material. A
   seção que perde todas as linhas mostra "Nada nesta seção com esses filtros"; a fila inteira
   esvaziada pelo filtro diz "Nada nesta fila com esses filtros" e oferece "Limpar filtros"; em
   Tarefas, o grupo sem item some, e o vazio é "Nada para levar com esses filtros". A seleção mora na
   URL (`?material=…&pedido=…`), sobrevive ao F5 e permite compartilhar o link ("a fila do Corte, só
   a chapa de 3 mm"). O filtro só muda o que se desenha: esconder uma linha não é "outra pessoa
   agiu", então não dispara o aviso de que o item saiu da fila; em Tarefas, o item já marcado que o
   filtro esconde **continua marcado e vai na entrega**, e a tela avisa "N marcados ocultos pelo
   filtro".
9. **Operador age em lote na fila do Setor** (spec `docs/superpowers/specs/2026-09-29-filtros-e-lote-design.md`,
   seções 5 e 6): em vez de iniciar ou terminar uma linha de cada vez, marca várias e registra tudo
   de uma vez. Só quem aponta (Operador e Administrador) vê os controles do lote.
   - **Onde há caixa de marcar.** Em "A iniciar aqui" (a ação do lote é Iniciar), em "Em trabalho"
     (Terminar) e, em "Aguardando montagem", só no cartão do pai cuja montagem começa neste Setor
     (Iniciar o pai, que consome os filhos como o botão individual). "Aguardando coleta" e "Sobra" não
     têm lote. Uma linha que não pode agir agora — Pedido pausado, ou "dá para iniciar 0" no pai —
     tem a caixa desabilitada, e o motivo é o que a linha já mostra.
   - **Uma seção por vez.** Com o primeiro item marcado, as caixas das outras seções ficam
     desabilitadas, e a seção travada que tem alguma linha visível que poderia ser marcada mostra no
     cabeçalho a dica "Conclua ou limpe a seleção de *A iniciar aqui*" (o nome da seção em uso); sem
     essa linha, o cabeçalho não traz dica nem "Marcar todos". A trava solta quando a seleção esvazia.
   - **"Marcar todos", por seção.** Fica no cabeçalho da seção e marca as linhas **visíveis depois do
     filtro** que podem agir; pula as desabilitadas. Com todas elas marcadas, vira "Desmarcar todos", e
     desmarca só elas. Sem linha que possa agir, o botão não aparece; nas outras seções, durante um
     lote, fica desabilitado. Marcar não reescreve a quantidade de quem já estava marcado.
   - **Quantidade.** Cada linha marcada mostra o campo "Quantidade" já preenchido com o saldo da linha
     (no pai, com o "dá para iniciar"), editável e validado como o "Levar". Se a atualização periódica
     baixar o máximo abaixo do que foi digitado, o campo acusa e o botão do lote desabilita; o texto
     nunca é reescrito.
   - **A barra do lote.** Enquanto há seleção, uma barra no rodapé mostra o botão com o verbo puro
     ("Iniciar 5 itens", "Terminar 1 item" — sem a atividade do Setor que o botão da linha usa), o
     aviso "N marcados ocultos pelo filtro" quando o filtro esconde algum marcado e "Limpar seleção".
     O botão fica desabilitado se alguma quantidade estiver inválida ou se alguma linha marcada ficou
     bloqueada (o Pedido foi pausado depois de marcar): ela continua marcada, com o motivo, até o
     operador desmarcá-la.
   - **Lote e botão individual não coexistem.** Com pelo menos uma linha marcada, os botões individuais
     de Iniciar, Terminar e Iniciar o pai somem da fila inteira; marcar uma linha fecha o formulário
     individual que estivesse aberto. O "Levar" do filho e o "Estornar" continuam, porque não são ação
     de lote. Sem marcação, a fila é a de antes.
   - **Tudo ou nada.** O lote vai numa requisição (`POST /inicios` ou `POST /terminos`, em
     `specs/05-api-endpoints.md`). Se um item é recusado, **nada é gravado**: a fila recarrega, a
     barra mostra o motivo nomeando o item ("Só há 3 de Chapa de base do mancal a iniciar.") e a seleção continua, salvo o que saiu da fila nesse meio-tempo — uma linha que desapareceu
     da resposta sai da seleção, com o aviso "Um item que você tinha marcado não está mais nesta fila:
     outra pessoa o moveu. Confira a seleção." O que o filtro apenas esconde continua marcado e vai no
     lote. Com sucesso, a seleção esvazia e a fila recarrega.
   - **Estorno** continua por linha; não existe "estornar lote".
10. **Movimentador marca todos em Tarefas.** Além de marcar item a item, o botão **Marcar todos** no
    topo da lista marca todos os itens visíveis depois do filtro, em todos os grupos, e pula o que não
    se pode marcar (o pai sem Roteiro). Marca também os Kits montáveis visíveis, cada um com o número
    máximo de conjuntos; os Kits incompletos não se marcam. Com todos marcados vira "Desmarcar
    todos", que desmarca só esses: o marcado oculto pelo filtro continua marcado. Marcar não reescreve
    a quantidade nem os conjuntos de quem já estava marcado. O resto do "Levar" não muda: ele já é
    uma entrega em lote, numa requisição, tudo ou nada.
11. **Administrador marca o Setor que monta Kit.** No painel de criar ou editar Setor, a caixa
    **Utiliza Kit** diz que ali se montam os nós de Agrupamento Kit a partir dos filhos — hoje, a
    Solda. A lista de Setores mostra a pílula neutra "Utiliza Kit" nos marcados: é atributo do Setor,
    não a identidade de Kit da pílula azul da fila. A marca vale da troca em diante, e a tela de
    Tarefas recalcula na carga seguinte (D4 da spec da Fase 3B); o mesmo vale para trocar o Tipo de
    um Agrupamento. Trocar com Kit já em produção pode travar aquele Kit: ver a limitação conhecida
    da regra 25.

## 3. Separação de Material

*Perfil: Almoxarifado*

1. Antes (ou durante) a fabricação de um `EstruturaItem` folha, o Almoxarifado separa
   os Materiais necessários (`EstruturaMaterial`).
2. Cada separação é registrada em `MaterialSeparacao` (quantidade, responsável, data).
3. Regra a validar: sistema pode alertar (não necessariamente bloquear) se a
   quantidade separada for menor que a quantidade planejada em `EstruturaMaterial`.

## 4. Expedição e Relatório Dimensional

*Perfil: Qualidade*

1. A expedição pode ser **parcial**: o cliente aceita uma quantidade vital agora
   (uma remessa = uma linha em Expedicao) e o restante depois.
2. Se a Peça foi marcada com RequerRelatorioDimensional, a Qualidade registra, para cada
   remessa avaliada pelo cliente, uma RelatorioDimensionalAvaliacao com a quantidade
   avaliada/aprovada/reprovada (acumulando no relatório único da Peça). Sem essa marca,
   não há relatório.
3. Uma Peça conclui quando toda a sua quantidade virou expedido ou perdido. O Agrupamento
   conclui quando todas as suas Peças concluem; se for o último Agrupamento em aberto do
   Pedido → Pedido.DataConclusao é preenchida.

## 5. Retrabalho (Reprovação ou Perda)

*Perfil: Qualidade*

> **Nota (2026-09-15).** Quando o Retrabalho repõe uma perda que impediu montar, o Pedido novo não
> segue o fluxo 1-4 "normalmente" em tudo: o que já existe da unidade perdida é marcado **pronto**,
> não percorre Roteiro e nasce aguardando coleta (regra 27 de `01-dominio-e-regras-de-negocio.md`).
> Revisto na spec da Fase 5.

> **Nota (2026-09-24).** Nesse caso, o Retrabalho é **aberto** por quem registra a perda — Qualidade
> ou PCP, como a seção "6. Perda de peças" já admite — e **cadastrado** pelo PCP (regra 27). O
> "*Perfil: Qualidade*" no alto desta seção descreve a abertura a partir de uma reprovação.

1. Se uma Peça (ou parte de sua quantidade) é reprovada no Relatório Dimensional, o
   registro fica salvo normalmente — **não** abre retrabalho automaticamente.
2. Quando (e se) Qualidade decidir abrir o retrabalho, cria um novo Pedido
   (`Tipo = Retrabalho`, `PedidoOrigemId` = Pedido original, `MotivoRetrabalho` =
   `ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`/`Perda`), para a
   quantidade reprovada (ou perdida).
3. Se a abertura partiu de uma reprovação específica, a `RelatorioDimensionalAvaliacao`
   correspondente é vinculada (`PedidoRetrabalhoId`). Se partiu de uma perda, é a
   `Perda` correspondente que é vinculada (`Perda.PedidoRetrabalhoId`).
4. O novo Pedido de Retrabalho segue o mesmo fluxo 1-4 normalmente (cadastro de
   Agrupamento/estrutura, apontamento de setor, separação de material, novo dimensional).

## 6. Perda de peças

*Perfil: Qualidade / PCP*

> **Nota (2026-09-15).** A perda passou a valer para **qualquer** `EstruturaItem`, não só Peça, e
> ganhou o motivo `Descarte` (regra 17 de `01-dominio-e-regras-de-negocio.md`); quando a perda de
> uma parte impede montar o pai, a perda sobe até a Peça do topo (regra 27). O passo a passo desta
> seção é revisto na spec da Fase 5.

1. Quando uma quantidade se perde em produção (some no armazém = PerdaArmazem, ou morre
   após um processo = MortaEmProcesso), registra-se uma Perda (Peça, quantidade, motivo,
   opcionalmente o Setor onde estava, responsável, observação).
2. A quantidade perdida sai da produção (bucket terminal), contando para a conclusão da Peça.
3. Para repor, a Qualidade/PCP pode (opcional) abrir um Pedido de Retrabalho para aquela
   quantidade, com MotivoRetrabalho='Perda', vinculado via Perda.PedidoRetrabalhoId.

## 7. Consulta de KPIs (gestão)

*Perfil: Gestão*

1. Tempo médio de liberação por Setor (sobre o livro de movimentações, pareando entradas e saídas
   de cada Setor por ordem de chegada — ver Fase 6 em `06-roadmap-mvp.md`).
2. Tempo total, tempo em fila e tempo de produção por Pedido (ver query de exemplo em
   `02-modelo-de-dados.sql`).
