# 04 - Fluxos de Usuário

Cada fluxo abaixo deve virar um caso de uso na camada `Application` do backend
(ver `03-arquitetura-tecnica.md`) e uma tela (ou conjunto de telas) no frontend.

## 1. Cadastro de Pedido

*Perfil: PCP*

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

## 2. Apontamento em Setor

*Perfil: Operador*

1. Operador do setor abre a tela do seu Setor e vê os itens aguardando entrada.
2. Ao iniciar o trabalho em um `EstruturaItem`, registra entrada
   (`EstruturaSetorHistorico.DataEntrada`).
3. Ao concluir, registra saída (`DataSaida`) de uma quantidade; o sistema permite que a
   Peça tenha quantidades em setores diferentes ao mesmo tempo (lote divisível). O que se
   valida é a conservação de quantidade (nunca movimentar mais do que existe naquele ponto).
4. Sistema direciona o item para o próximo Setor do roteiro (`EstruturaRoteiro`), ou
   marca como pronto para virar parte da Peça-pai / seguir para Expedição, se for o
   último passo.

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

1. Quando uma quantidade se perde em produção (some no armazém = PerdaArmazem, ou morre
   após um processo = MortaEmProcesso), registra-se uma Perda (Peça, quantidade, motivo,
   opcionalmente o Setor onde estava, responsável, observação).
2. A quantidade perdida sai da produção (bucket terminal), contando para a conclusão da Peça.
3. Para repor, a Qualidade/PCP pode (opcional) abrir um Pedido de Retrabalho para aquela
   quantidade, com MotivoRetrabalho='Perda', vinculado via Perda.PedidoRetrabalhoId.

## 7. Consulta de KPIs (gestão)

*Perfil: Gestão*

1. Tempo médio de liberação por Setor (`EstruturaSetorHistorico`, `DataSaida - DataEntrada`,
   a partir da chegada — `DataInicioExecucao` fica disponível para refinar esse cálculo
   separando fila de execução, quando/se for preenchido).
2. Tempo total, tempo em fila e tempo de produção por Pedido (ver query de exemplo em
   `02-modelo-de-dados.sql`).
