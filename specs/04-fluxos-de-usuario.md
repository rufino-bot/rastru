# 04 - Fluxos de Usuário

Cada fluxo abaixo deve virar um caso de uso na camada `Application` do backend
(ver `03-arquitetura-tecnica.md`) e uma tela (ou conjunto de telas) no frontend.

## 1. Cadastro de Pedido

*Perfil: PCP*

1. PCP cadastra Pedido (`Tipo = Fabricacao`), com Cliente e Número.
2. PCP cadastra N Kits para o Pedido.
3. Para cada Kit, PCP monta a estrutura (`EstruturaItem`):
   - Pode puxar de um `Componente` padrão do catálogo (copia `ComponenteFilhoPadrao`,
     `ComponenteMaterialPadrao` e `ComponenteRoteiroPadrao` para dentro da estrutura
     real do Kit, como ponto de partida editável).
   - Pode criar itens 100% customizados (sem `ComponenteId`), específicos deste Pedido.
4. Pedido fica com `Status = Aberto` até o primeiro apontamento de setor
   (`Status = EmProducao`).

## 2. Apontamento em Setor

*Perfil: Operador*

1. Operador do setor abre a tela do seu Setor e vê os itens aguardando entrada.
2. Ao iniciar o trabalho em um `EstruturaItem`, registra entrada
   (`EstruturaSetorHistorico.DataEntrada`).
3. Ao concluir, registra saída (`DataSaida`) — sistema valida que não existe outra
   passagem aberta para o mesmo item (lote indivisível).
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

## 4. Relatório Dimensional (Expedição)

*Perfil: Qualidade*

1. Quando todas as Peças de um Kit chegam à Expedição, Qualidade registra, para
   **cada Peça** (`EstruturaItem` com `NivelHierarquico = Peca`), um
   `RelatorioDimensional` com `DentroTolerancia = true/false`.
2. Se todas as Peças do Kit estiverem aprovadas → `Kit.DataConclusao` é preenchida.
3. Se o Kit concluído for o último Kit em aberto do Pedido → `Pedido.DataConclusao`
   é preenchida e `Status = Concluido`.

## 5. Retrabalho por Reprovação

*Perfil: Qualidade*

1. Se uma Peça é reprovada no Relatório Dimensional, o registro fica salvo normalmente
   — **não** abre retrabalho automaticamente.
2. Quando (e se) Qualidade decidir abrir o retrabalho, cria um novo Pedido
   (`Tipo = Retrabalho`, `PedidoOrigemId` = Pedido original, `MotivoRetrabalho` =
   `ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`).
3. Se a abertura partiu de uma reprovação específica, o `RelatorioDimensional`
   correspondente é vinculado (`PedidoRetrabalhoId`).
4. O novo Pedido de Retrabalho segue o mesmo fluxo 1-4 normalmente (cadastro de Kit/
   estrutura, apontamento de setor, separação de material, novo dimensional).

## 6. Consulta de KPIs (gestão)

*Perfil: Gestão*

1. Tempo médio de liberação por Setor (`EstruturaSetorHistorico`, `DataSaida - DataEntrada`,
   a partir da chegada — `DataInicioExecucao` fica disponível para refinar esse cálculo
   separando fila de execução, quando/se for preenchido).
2. Tempo total, tempo em fila e tempo de produção por Pedido (ver query de exemplo em
   `02-modelo-de-dados.sql`).
