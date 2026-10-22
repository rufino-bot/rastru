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
   - Para cada Peça (nó de topo do `EstruturaItem`), marca se ela `RequerRelatorioDimensional`. O PCP
     **edita a marca depois**, no painel de edição da Peça (a caixa "Requer relatório dimensional", que só
     existe na Peça), enquanto ela não tem aferição válida; com uma, o servidor recusa (regra 10 em `01`).
4. Pedido fica com `Status = Aberto` até o primeiro apontamento de setor
   (`Status = EmProducao`). Os outros dois status vêm do fim do caminho da Peça (fluxo 4):
   `AguardandoExpedicao` quando tudo o que falta expedir já está no local de expedição, e `Concluido` com
   a última remessa. Um Pedido `Concluido` não aceita edição de estrutura (C15 da spec da Fase 5A; regra 13 em `01` para o fechamento); para mudá-lo,
   a Qualidade estorna a última remessa.
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
>
> **Emendado em 2026-10-22** pela spec da Fase 5A
> (`docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md`): o **conserto**
> da unidade não conforme entra na fila (passo 2) e nas Tarefas (passo 4), e o passo 12 conta o caminho
> inteiro. Quem manda para conserto é a Qualidade (fluxo 4).

1. **PCP** confere que todo nó tem Roteiro; nó sem Roteiro aparece como pendência na árvore, e o PCP
   o edita (regra 28).
2. **Operador** abre a fila do seu Setor e vê, nesta ordem, o que está em trabalho, o que está a
   iniciar ali, o que está aguardando montagem, o que está aguardando coleta e a sobra, e, **depois da
   sobra**, a seção **Conserto** (passo 12), que só existe quando há unidade em conserto naquele Setor. O
   card de nó de Agrupamento Kit traz a pílula **Kit**, em azul cheio, em qualquer seção; é a única tela
   que a mostra.
3. Ao pegar o material para trabalhar num nó cujo primeiro passo é ali, **inicia** uma quantidade
   (regra 28). Ao terminar, **termina** a quantidade feita: ela passa a aguardar coleta. O lote é
   divisível, e o que se valida é a conservação de quantidade (nunca movimentar mais do que existe
   naquele ponto). Quando o Setor tem uma **atividade** cadastrada, os botões levam o nome dela
   ("Iniciar solda", "Terminar solda"); sem atividade, ficam "Iniciar" e "Terminar".
4. **Movimentador** abre Tarefas, vê os Itens prontos com o destino de cada um e **entrega**: no
   próximo passo; na montagem do pai, no **primeiro passo do Roteiro do pai**, que o sistema calcula
   — o Movimentador não escolhe o Setor —; ou, se for Peça no fim do Roteiro, no local de expedição
   (regra 29). O conserto (passo 12) também é entrega: **Levar ao conserto em {Setor}** e **Levar ao
   local de expedição**, na seção **Conserto** de Tarefas.

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
   Tarefas, o grupo sem item some, e o vazio é "Nada para levar com esses filtros"; a única exceção é "Kits
   incompletos", que, havendo algum Kit incompleto antes do filtro, fica com "(0)" e o texto "Nenhum Kit
   incompleto combina com os filtros.", para o filtro não parecer ter feito o Kit sumir. A seleção mora na
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
     (Iniciar o pai, que consome os filhos como o botão individual). A linha **"em conserto"** da seção
     Conserto também tem caixa: ela termina no mesmo lote de **Terminar** de "Em trabalho" (item de
     conserto, sem passo), mas o "Marcar todos" de "Em trabalho" **não** a marca — é o de uma seção da
     tela. "Aguardando coleta" e "Sobra" não têm lote, nem a linha de conserto já pronta. Uma linha que não pode agir agora — Pedido pausado, ou "dá para iniciar 0" no pai —
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
    máximo de conjuntos, e as idas e voltas do conserto visíveis; os Kits incompletos não se marcam.
    Com todos marcados vira "Desmarcar todos", que desmarca só esses: o marcado oculto pelo filtro continua marcado. Marcar não reescreve
    a quantidade nem os conjuntos de quem já estava marcado. O resto do "Levar" não muda: ele já é
    uma entrega em lote, numa requisição, tudo ou nada.
11. **Administrador marca o Setor que monta Kit.** No painel de criar ou editar Setor, a caixa
    **Utiliza Kit** diz que ali se montam os nós de Agrupamento Kit a partir dos filhos — hoje, a
    Solda. A lista de Setores mostra a pílula neutra "Utiliza Kit" nos marcados: é atributo do Setor,
    não a identidade de Kit da pílula azul da fila. A marca vale da troca em diante, e a tela de
    Tarefas recalcula na carga seguinte (D4 da spec da Fase 3B); o mesmo vale para trocar o Tipo de
    um Agrupamento. Trocar com Kit já em produção pode travar aquele Kit: ver a limitação conhecida
    da regra 25.
12. **O conserto anda pelo chão como o resto** (Fase 5A, regra 35 em `01`; D6, C3, C4 da spec). A Qualidade
    manda unidades não conformes para um Setor (fluxo 4, passo 4), e daí em diante:
    - O **Movimentador** vê em Tarefas a seção **Conserto**, no alto, acima de "Kits montáveis", com uma linha
      "Levar ao conserto em {Setor}" por unidade esperando, com caixa e quantidade como as outras, e
      **Entregar** no lote de sempre (os itens do conserto vão na mesma entrega dos itens e dos Kits marcados).
    - O **Operador** do Setor vê na Fila a seção **Conserto**, depois de "Sobra", com a Peça e o Pedido e
      "{q} em conserto", e o botão **Terminar conserto** (individual, ou no lote de Terminar: ver o passo 9). O
      conserto não tem passo do Roteiro, e o botão leva esse nome, não o da atividade do Setor. Terminado, a
      linha passa a "{q} pronto, aguardando levar à expedição", sem botão de terminar.
    - O **Movimentador** vê então em Tarefas "Levar ao local de expedição". Entregue, a unidade volta ao local
      de expedição como "a medir" e a Qualidade a mede de novo (fluxo 4, passo 3).
    - O **estorno** continua na própria fila, nas duas linhas do conserto, enquanto a quantidade não tiver
      andado. O **filtro** de Material e Pedido vale para a seção Conserto e para as tarefas dele, e a contagem
      do menu "Tarefas N" inclui o conserto. O conserto **não mexe no Roteiro** do nó.

## 3. Separação de Material

*Perfil: Separador (separa e estorna); PCP (edita os materiais do nó e estorna); Administrador (tudo isso e o
"Fica em" do Material). Leitura: qualquer perfil autenticado.*

*(Fase 4, spec `docs/superpowers/specs/2026-10-16-fase-4-separacao-de-materiais-design.md`, regras 4, 5 e 34 de
`01-dominio-e-regras-de-negocio.md`. O backend é o plano de backend da Fase 4; as telas são do plano de front,
e o desenho delas está na seção 6 da spec.)*

O material do almoxarifado é separado **por antecipação**, olhando o que vai ser fabricado (D1), e principalmente
em dois casos: o **Kit** da Fase 3B, montado na Solda, e a **montagem final**, que acontece sempre na
Expedição — uma Peça com Roteiro de um passo só, a Expedição, cujo início é a montagem (D2). Nos dois casos o
material pertence a um **nó com filhos** e é usado no Setor do **primeiro passo** do Roteiro dele. A chapa,
ao contrário, fica estocada no Corte, e o operador a pega ali mesmo. **A execução não muda.**

1. **O planejado do nó.** Os materiais planejados de um `EstruturaItem` (`EstruturaMaterial`) nascem
   copiados da receita padrão, já multiplicados pela quantidade do nó. O **PCP** os edita a qualquer
   momento, em qualquer nó — inclusive o que nasceu sem receita —, pelo editor de materiais do painel do
   nó (`PUT /estrutura/{id}/materiais`, que substitui a lista inteira). Tirar do plano um material que já
   foi separado é permitido: a separação continua no histórico, como "fora do planejado" (D14).
2. **Onde o material fica.** O cadastro de Material tem o campo **"Fica em"** (Administrador): o
   almoxarifado, que é o padrão, ou um Setor. Só o material do almoxarifado entra na lista do Separador
   (D6).
3. **A lista do Separador.** A tela **Separação** traz, em cartões pensados para o celular, todo nó com
   material do almoxarifado pendente (planejado menos separado) de Pedido ainda aberto — `Concluido` e
   `Cancelado` ficam de fora. O nó entra na lista **desde que existe**, não só quando o Pedido entra em
   produção; Pedido **pausado** fica na lista, marcado (separar é antecipar, e a pausa recusa só o
   início — regra 31), e o Pedido atrasado traz a pílula "Atrasado". Cada cartão mostra o nó, o Pedido
   com cliente e data de entrega, o **destino** — o Setor do primeiro passo do Roteiro do nó, ou "sem
   Roteiro", que é pendência do PCP (regra 28) — e uma linha por material pendente
   ("PAR-M8 Parafuso M8 · falta 2 UN (8 de 10)"). A ordem é a **data de entrega** do Pedido (D11). A
   lista chega inteira e a tela filtra no cliente por **Setor de destino**, **Pedido** e **Material**,
   com a seleção na URL.
4. **Separar.** O botão "Separar" do cartão abre um painel **já preenchido com a pendência** de cada
   material (D17): o caso comum é um toque só. Campo vazio ou zero não vai. Um campo acima da pendência
   mostra, embaixo, "acima do planejado em 2 UN", como texto neutro e não como erro: o sistema aceita
   (o parafuso que caiu, D9). O envio é uma requisição só, **tudo ou nada**
   (`POST /estrutura/{id}/separacoes`); no sucesso, o painel fecha, a lista recarrega e o cartão sem
   pendência some. Um material que fica num Setor também pode ser registrado, se estiver no planejado
   do nó (D16); ele só não aparece na lista. **Fora do planejado, a separação é recusada** (409
   `MaterialForaDoPlanejado`): é erro de plano, e o PCP o corrige no passo 1.
   **Em lote** (decisão do usuário na conferência da Fase 4, 2026-10-19): a caixa de cada cartão o marca
   inteiro, e o cartão marcado mostra um campo por material pendente, já preenchido com a pendência e
   editável. A barra fixa no rodapé envia todos os marcados numa requisição só, **tudo ou nada**
   (`POST /separacoes`): "Separar 2 itens". Se um nó é recusado, nada é gravado, a lista recarrega, a seleção
   fica e a frase do servidor, com o nome do nó, aparece na barra; o cartão que saiu da lista sai da seleção,
   com aviso. "Marcar todos" marca os cartões visíveis, e o marcado que o filtro esconde vai junto.
5. **Estornar.** A correção é um **estorno** da linha inteira, uma vez só (D10); não há saldo de
   estoque nem devolução. O Separador estorna só a que ele mesmo registrou; o PCP e o Administrador,
   qualquer uma. Estornar uma separação que saiu do plano é possível.
6. **O painel do nó** (na página do Agrupamento) mostra, para cada material, o planejado, o separado, a
   pendência e o "Fica em", com o histórico de separações (quem, quando, quanto) e o botão de estornar nas
   linhas que o usuário pode estornar.
7. **A Fila do Setor** mostra, na linha do nó **que inicia ali** e que tem material do almoxarifado no
   planejado, um resumo neutro: "Material: 2 de 3 completos", ou "Material completo". Conta
   **materiais completos, não quantidades** (D15). **O início não é bloqueado nem pede confirmação**
   (D12): a falta de material é informação, não bloqueio.

## 4. Expedição e Aferição

*Perfil: Qualidade (e Administrador) registra; a Gestão responde às liberações (subseção abaixo); qualquer
perfil autenticado lê. Fase 5A, spec
`docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md`, seção 6; regras 10, 11,
13, 16 e 35 a 37 de `01-dominio-e-regras-de-negocio.md`; contrato em `05-api-endpoints.md`, seção "Aferição,
expedição e fechamento". Telas: `/expedicao` e `/liberacoes`.*

No Rastru a **Qualidade é a pessoa da expedição**: quem mede, decide se passou e expede é a mesma pessoa (D1).
As cotas vêm do cliente com o pedido e ficam fora do sistema, que guarda só o resultado, quem e quando (D2). A
Peça chega ao local de expedição pela entrega do Movimentador (fluxo 2, passo 4); dali em diante o fluxo é este.

1. **A tela.** O item **Expedição** do menu abre os Pedidos não encerrados com Peça no local de expedição ou em
   conserto, na ordem do prazo de entrega. Cada Pedido tem um cabeçalho (número, cliente, "entrega dd/mm/aaaa"
   e as pílulas **Atrasado** e **Pausado** de sempre) e, abaixo, um cartão por Peça: código e descrição (a do
   Componente, quando a Peça foi copiada do catálogo e não tem descrição própria), o Agrupamento, a pílula
   neutra **Exige relatório** ou **Sem relatório**, a situação da aferição (**Aferição pendente**, neutra;
   **Conforme**, verde; **Não conforme**, vermelha), **"N aqui"** e **"de Q · E expedidas"**. "N aqui" é o que
   está no local de expedição, em qualquer posição dele; o que está em conserto não entra na conta. A lista
   chega inteira, e os filtros **Pedido** e **Aferição** agem no cliente, com a seleção na URL. Os três
   estados: carregando; vazio ("Nada no local de expedição.", ou "Nenhuma Peça com esses filtros." com
   "Limpar filtros"); erro ("Não foi possível carregar a expedição."). A tela não se atualiza sozinha:
   recarrega depois de cada escrita.
2. **Aferir.** A Peça que **exige** relatório e ainda não tem aferição válida mostra **Aferir**. O painel pede o
   resultado, **Conforme** ou **Não conforme**, sem nenhum marcado (aferir é decisão, e um toque distraído não a
   registra), com a dica "Uma unidade medida decide o lote.". **Conforme** vale para a Peça toda, inclusive as
   remessas futuras: ninguém mede mais. **Não conforme** obriga a medir todas, as de agora e as que chegarem
   (passo 3). A Peça que não exige relatório não tem **Aferir**.
3. **Contar.** Depois de uma aferição não conforme, as unidades que estão no local de expedição e ainda não foram
   medidas são **"A medir"**, e a Peça mostra **Contar**. O painel **Contar as unidades** diz "{a} a medir." e
   pede **Conformes** e **Não conformes**: campo vazio vale zero ("medi 6, todas conformes" não pede digitar 0 na
   outra), a vírgula decimal vale, a soma não pode ser zero nem passar do que há a medir, e as duas parcelas
   vão juntas, tudo ou nada. A contagem diz quantas, e não quais: é lote agregado, sem serial. Com ela, o
   cartão passa a mostrar a contagem — **Conformes**, **Não conformes** e, dentro destas, **"· liberadas pela
   Gestão"**, **"· em conserto ({Setor})"** (uma linha por Setor), **"· aguardando liberação"** e **"· aguardando
   decisão"**, mais **A medir** —, e as linhas de dentro só aparecem quando têm unidade. Texto neutro: a cor do
   estado já está na pílula.
4. **O destino da não conforme.** As unidades **aguardando decisão** têm três saídas (a quarta, "matar a peça",
   é da Fase 5B):
   - **Mandar para conserto.** O painel pede o **Setor** — qualquer Setor ativo, inclusive a Expedição, e nenhum
     vem escolhido — e a **Quantidade**, que abre com todas as que aguardam decisão. A unidade vai ao Setor:
     o Movimentador a leva, o operador a corrige e o Movimentador a traz de volta (fluxo 2, passo 12), e ela
     volta ao local de expedição como **a medir**, para ser contada de novo. É o mesmo Pedido, e enquanto a
     unidade está no conserto o Pedido deixa de estar "Aguardando expedição" (passo 8).
   - **Pedir liberação.** O painel pede a **Quantidade** (abre com todas as que aguardam decisão) e a
     **Justificativa**, obrigatória: se o cliente reclamar, o porquê é o que se procura. As unidades passam a
     **aguardando liberação** e **não saem em remessa** enquanto esperam a Gestão (subseção abaixo). Só há um
     pedido em aberto por Peça: enquanto ele existe, o cartão o mostra ("Liberação pedida: {q}, por {nome} em
     {data} — {justificativa}") e **Pedir liberação** some.
   - **Expedir assim mesmo**, sem liberação: o Rastru avisa e deixa (passo 5).
5. **Expedir.** **Expedir** aparece quando há **expedível**: o que está no local de expedição, menos o que aguarda
   a Gestão. O painel **Expedir remessa** pede só a **Quantidade**, que abre com o expedível: a Qualidade pensa
   em "mandar 9", e o servidor escolhe de onde tira, nesta ordem — liberada, conforme, a que está no local de
   expedição, e por último a não conforme (regra 16, C10). Antes de confirmar, o painel mostra o **aviso** do que
   vai sair sem aferição ("{q} sem aferição"), sem medição ("{q} sem medição") ou não conforme sem liberação
   ("1 não conforme sem liberação", "2 não conformes sem liberação": no singular, quando é uma), seguido de
   **"Dá para expedir assim mesmo; fica registrado."**. O aviso **não trava** (regra 37): só o de não conforme
   sem liberação é vermelho, e os outros dois são texto neutro. O botão diz "Expedir {q}". O duplo toque no
   celular não grava duas remessas. Se outra pessoa expediu antes e a quantidade ficou acima do expedível, o
   servidor recusa, a frase aparece dentro do painel e a lista recarrega.
6. **Expedir em lote** (C9). Com perfil que registra, a Peça com expedível ganha a caixa **Marcar**; **Marcar
   todos** marca as visíveis depois do filtro. A Peça marcada mostra o campo **Quantidade** (já com o expedível)
   e o aviso daquela Peça, e a barra fixa do rodapé traz **Expedir** e **Limpar seleção**, como na Separação. O
   lote vai numa remessa só, **tudo ou nada**: se uma Peça é recusada, nada é gravado, a lista recarrega, a
   seleção fica e a frase do servidor, com o nome da Peça, aparece na barra. A Peça marcada que sai da lista
   (outra pessoa a expediu) sai da seleção, com aviso. A marcada que o filtro esconde vai no lote.
7. **Remessas recentes.** No fim da tela, a seção **Remessas recentes** traz as dos últimos 30 dias, a mais
   nova primeiro: data e hora, quem expediu, e uma linha por Peça com a quantidade (vazio: "Nenhuma remessa nos
   últimos 30 dias."). É **daqui** que se estorna uma remessa, e não do histórico da Peça: a remessa desfaz mais
   de uma Peça. **Estornar remessa** abre o painel que lista todas as Peças que ela desfaz e, para cada Pedido
   que ela concluiu, "Reabre o Pedido {número}."; **Estornar** a desfaz **inteira**. Quem estorna: o autor, o
   PCP ou o Administrador (a remessa já estornada fica marcada e não se estorna de novo).
8. **O fechamento e o status do Pedido** (regra 13). A Peça conclui quando toda a quantidade foi expedida e sai
   da lista; o Agrupamento conclui com a última Peça, e o Pedido com o último Agrupamento que tem Peça. O
   cabeçalho do Pedido mostra **"Concluído em dd/mm/aaaa"**, só o dia. Antes disso, o status é **Aguardando
   expedição** quando todo o não expedido de cada Peça está no local de expedição (a pílula é neutra), e volta a
   **Em produção** se uma unidade sai dele — para o conserto, por exemplo — ou se se acrescenta Peça ao Pedido.
   Estornar a remessa que concluiu o Pedido o reabre, e é a única escrita da expedição que um Pedido
   `Concluido` aceita.
9. **Histórico e estorno.** Cada cartão tem o **Histórico**, recolhido, que só busca ao abrir e passa a dizer
   "Histórico (N)": aferições, contagens, consertos, pedidos e respostas de liberação e remessas, do mais
   recente ao mais antigo, com autor e data; o estornado fica marcado. **Estornar** aparece onde o servidor diz
   que o registro é estornável e a sessão pode estornar aquele autor — o autor, o PCP ou o Administrador (C7) —,
   com confirmação ("O estorno fica no histórico."), e cada registro se estorna **inteiro**: a contagem não
   tem meio estorno. As regras do que impede estão em `01`: a aferição não conforme enquanto houver contagem; o
   pedido de liberação enquanto tiver resposta válida; a resposta quando a Peça já tem outro pedido em aberto;
   qualquer um deles depois de a quantidade ter andado. Se o servidor recusa, a frase aparece no histórico e o
   cartão recarrega. A remessa aparece no histórico com "Estorna-se em Remessas recentes.", sem botão.
10. **Quem vê o quê.** Qualidade e Administrador registram (aferir, contar, conserto, pedir liberação, expedir).
    Os demais perfis leem a tela inteira e **não veem botão de registro**. O estorno segue a autoria: a Gestão,
    que não registra nada aqui, ainda assim vê **Estornar** no registro **dela** — a resposta de liberação, no
    histórico da Peça — e no de mais ninguém; o PCP e o Administrador veem o botão em todos. O esconder do botão
    é conveniência: o 403 do servidor é a fronteira.

### Liberação da não conforme (Gestão)

*Perfil: Gestão (e Administrador) responde; qualquer perfil lê. Regra 36 de `01`; D7, C5, C6 e C18 da spec.*

A Qualidade pede (passo 4 acima) e a Gestão decide: expedir não conforme sem liberação é possível, mas é a
exceção, e a decisão fica gravada com o nome de quem a deu.

1. O item **Liberações** do menu abre a lista dos pedidos em aberto, do mais antigo ao mais novo: a Peça, o
   Pedido (número e cliente, "entrega dd/mm/aaaa" e **Atrasado**), "Quantidade pedida: N", a justificativa e quem
   pediu e quando. Vazio: "Nenhuma liberação pendente."; erro: "Não foi possível carregar as liberações.". A
   Qualidade e os demais perfis abrem a tela e leem a fila, sem o botão **Responder**.
2. **Responder** abre o painel **Liberar para expedir**, com a **Quantidade liberada** (abre com a pedida) e a
   **Observação (opcional)**. **Liberar {q}** libera até a quantidade pedida — pode ser menos ("libero 2 das 3") —,
   e **Recusar tudo** é liberar zero. O que foi liberado passa a "liberadas pela Gestão" e sai em remessa sem
   aviso; o resto volta a "aguardando decisão" e a Qualidade volta a decidir (passo 4 acima).
3. Se outra pessoa respondeu antes, ou a Qualidade estornou o pedido, o servidor recusa: a frase sobe para a tela
   e a lista recarrega, e o pedido respondido sai dela.
4. A Gestão corrige a **própria resposta** por estorno, no histórico da Peça em `/expedicao` (passo 9 acima),
   enquanto a quantidade não tiver andado: o pedido volta a "em aberto". Se a Peça já tem outro pedido em
   aberto, o servidor recusa.
5. Avisar a Gestão quando há pedido pendente é a notificação push da Fase 3C; até lá, ela abre a tela.

## 5. Retrabalho (Reprovação ou Perda)

*Perfil: Qualidade*

> **Nota (2026-09-15).** Quando o Retrabalho repõe uma perda que impediu montar, o Pedido novo não
> segue o fluxo 1-4 "normalmente" em tudo: o que já existe da unidade perdida é marcado **pronto**,
> não percorre Roteiro e nasce aguardando coleta (regra 27 de `01-dominio-e-regras-de-negocio.md`).
> Revisto na spec da Fase 5.

> **Nota (2026-09-24).** Nesse caso, o Retrabalho é **aberto** por quem registra a perda — Qualidade
> ou PCP, como a seção "6. Perda de peças" já admite — e **cadastrado** pelo PCP (regra 27). O
> "*Perfil: Qualidade*" no alto desta seção descreve a abertura a partir de uma reprovação.

> **Nota (2026-10-22).** A Fase 5A trocou a "reprovação no Relatório Dimensional" pela **aferição não
> conforme** (fluxo 4): ela **não** abre Retrabalho, e a unidade não conforme tem como destinos o conserto, a
> liberação da Gestão e a expedição com aviso. O passo 3 abaixo falava da `RelatorioDimensionalAvaliacao`,
> que **deixou de existir** (regra 12 de `01-dominio-e-regras-de-negocio.md`); como o Retrabalho se liga à
> reclamação do cliente é redesenhado na Fase 5B, e este fluxo é revisto lá.

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
