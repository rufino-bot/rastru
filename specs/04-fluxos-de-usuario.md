# 04 - Fluxos de Usuário

Cada fluxo abaixo deve virar um caso de uso na camada `Application` do backend
(ver `03-arquitetura-tecnica.md`) e uma tela (ou conjunto de telas) no frontend.

## 1. Cadastro de Pedido

*Perfil: PCP (pausar e retomar o Pedido: PCP ou Gestão)*

1. PCP cadastra Pedido (`Tipo = Fabricacao`), com Cliente e Número.
2. PCP cadastra N Agrupamentos para o Pedido (cada um com Tipo `'Kit'` ou `'Avulso'`).
3. Para cada Agrupamento, PCP monta a estrutura (`EstruturaItem`):
   - Pode puxar de um `Componente` padrão do catálogo (copia `ComponenteFilhoPadrao`,
     `ComponenteMaterialPadrao` e `ComponenteRoteiroPadrao` para dentro da estrutura
     real do Agrupamento, como ponto de partida editável).
   - Pode criar itens 100% customizados (sem `ComponenteId`), específicos deste Pedido.
   - Para cada Peça (nó de topo do `EstruturaItem`), marca se ela `RequerRelatorioDimensional`.
4. Pedido fica com `Status = Aberto` até o primeiro apontamento de setor
   (`Status = EmProducao`).
5. Quando outro Pedido precisa passar na frente, **PCP ou Gestão pausam** o Pedido, com um motivo
   opcional, e o **retomam** depois (regra 31). A pausa não muda o status; ela só recusa o início
   de nós dele. A **lista** de Pedidos mostra só a pílula "Pausado", ao lado do status; quem
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
   Home não lista Pedidos: lê o **resumo** do servidor (contagem por status e os cinco abertos há mais
   tempo), que conta todos os Pedidos e não só a primeira página.

## 2. Apontamento em Setor

*Perfis: Operador, Movimentador e PCP*

> **Reescrito em 2026-09-24** pela spec da Fase 3
> (`docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`), que decidiu o que a
> nota de 2026-09-15 deixava em aberto. **Emendado em 2026-09-28** pela spec da Fase 3D
> (`docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`): montar deixou de
> ser ação — o pai é iniciado, e iniciá-lo consome os filhos (regra 24) —, o que torna a trava de
> montagem estrutural para todo nó que já tem filhos quando entra em produção; o **conjunto
> completo** na entrada de Setor com `UtilizaKit` (regra 25) e o caso do nó que ganha filho depois
> de iniciado continuam na Fase 3B.

1. **PCP** confere que todo nó tem Roteiro; nó sem Roteiro aparece como pendência na árvore, e o PCP
   o edita (regra 28).
2. **Operador** abre a fila do seu Setor e vê, nesta ordem, o que está em trabalho, o que está a
   iniciar ali, o que está aguardando montagem, o que está aguardando coleta e a sobra.
3. Ao pegar o material para trabalhar num nó cujo primeiro passo é ali, **inicia** uma quantidade
   (regra 28). Ao terminar, **termina** a quantidade feita: ela passa a aguardar coleta. O lote é
   divisível, e o que se valida é a conservação de quantidade (nunca movimentar mais do que existe
   naquele ponto). Quando o Setor tem uma **atividade** cadastrada, os botões levam o nome dela
   ("Iniciar solda", "Terminar solda"); sem atividade, ficam "Iniciar" e "Terminar".
4. **Movimentador** abre Tarefas, vê os Itens prontos com o destino de cada um e **entrega**: no
   próximo passo; na montagem do pai, no **primeiro passo do Roteiro do pai**, que o sistema calcula
   — o Movimentador não escolhe o Setor —; ou, se for Peça no fim do Roteiro, no local de expedição
   (regra 29).
5. **Operador** do primeiro passo do pai vê "Dá para iniciar N; falta iniciar X" e **inicia** o pai:
   iniciar é o que consome os filhos presentes no Setor e põe o pai em produção (regra 24). Não há
   mais "montar". Em outro Setor que ainda tenha filhos aguardando (entregues antes de o PCP editar
   o primeiro passo do pai), o card não oferece Iniciar: diz para onde levar os filhos.
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
   "Limpar". As opções são as que estão na tela naquele momento, com a contagem: a lista de opções
   não pula enquanto o operador marca, e a contagem de cada opção respeita o que está marcado nas
   *outras* facetas, de modo que a opção que deixaria a lista vazia mostra 0. Dentro de uma faceta
   vale OU (uma chapa reaproveitada serve a mais de um Pedido); entre as facetas, E. Uma opção
   marcada que sai da lista (a atualização periódica tirou o último item daquele Pedido) continua
   marcada, com 0, até o operador removê-la. O filtro age em todas as seções da fila; no card de
   **Aguardando montagem**, o card casa se o pai **ou** algum filho presente casar, e aparece
   inteiro. Um nó sem material — o Item ad-hoc, por exemplo — some quando há filtro de Material. A
   seção que perde todas as linhas mostra "Nada nesta seção com esses filtros"; a fila inteira
   esvaziada pelo filtro diz "Nada nesta fila com esses filtros" e oferece "Limpar filtros"; em
   Tarefas, o grupo sem item some, e o vazio é "Nada para levar com esses filtros". A seleção mora na
   URL (`?material=…&pedido=…`), sobrevive ao F5 e permite compartilhar o link ("a fila do Corte, só
   a chapa de 3 mm"). O filtro só muda o que se desenha: esconder uma linha não é "outra pessoa
   agiu", então não dispara o aviso de que o item saiu da fila; em Tarefas, o item já marcado que o
   filtro esconde **continua marcado e vai na entrega**, e a tela avisa "N marcados ocultos pelo
   filtro".

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
