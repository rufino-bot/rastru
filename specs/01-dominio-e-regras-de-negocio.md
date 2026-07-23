# 01 - Domínio e Regras de Negócio

## Glossário

| Termo | Definição |
|---|---|
| **Pedido** | Unidade máxima de trabalho, cadastrada no sistema. Tipo `Fabricacao` ou `Retrabalho`. Um Retrabalho referencia obrigatoriamente o Pedido original. |
| **Kit** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Kits. |
| **Componente** | Registro de **catálogo** (receita padrão/template), reutilizável entre Pedidos. Não é a instância física — é a definição. |
| **EstruturaItem** | A árvore **real** usada em um Kit específico, podendo ter sido copiada do catálogo (`Componente`) e customizada. É recursiva: um `EstruturaItem` pode ter `EstruturaItem` filhos. O nó de topo (sem pai) é chamado de **Peça**; os nós com pai são chamados de **Item**. Representa um **lote agregado** (quantidade), não uma unidade física individual — e esse lote não se divide. |
| **Material** | Produto de estoque (chapas, parafusos, roelas, etc.) consumido para fabricar um `EstruturaItem`. |
| **Setor** | Departamento de produção (ex.: Corte e Dobra, Usinagem) pelo qual um `EstruturaItem` pode passar. |
| **Roteiro** | Sequência de Setores que um `EstruturaItem` percorre. Pode ser padrão (catálogo) ou específico daquele Pedido/Kit. |
| **Relatório Dimensional** | Avaliação de conformidade dimensional de uma Peça, feita na Expedição, informada pelo cliente. Pode aprovar ou reprovar — a reprovação é por peça (parcial dentro do Kit), não pelo Kit inteiro. Reprovação **não** exige abertura imediata de retrabalho: fica registrada e o retrabalho pode ser aberto depois. |
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. |

## Regras de negócio

1. Um Pedido é composto por N Kits.
2. Um Kit é composto por N `EstruturaItem` de topo (Peças).
3. Uma Peça pode ser composta por N Itens; um Item pode, por sua vez, ser composto por
   outros Itens — recursão sem limite de profundidade, resolvida numa única tabela
   autorreferenciada (`EstruturaItem`), evitando o ciclo conceitual entre "Peça" e "Item".
4. Um `EstruturaItem` folha (sem filhos) é composto por N Materiais.
5. Materiais precisam ser **separados** (retirados do estoque) e entregues ao setor
   responsável antes da fabricação daquele item.
6. Um `EstruturaItem` tem uma origem (primeiro Setor do seu roteiro) e pode passar por
   zero ou mais Setores adicionais antes de estar pronto.
7. O roteiro de Setores pode ser copiado de um padrão do catálogo (`Componente`), mas
   pode ser customizado por Pedido/Kit — não é fixo.
8. Componentes (receita/catálogo) podem ser **padrão** (reutilizados entre Pedidos) ou
   **customizados** (criados especificamente para um Pedido, sem entrar no catálogo geral).
9. O lote de um `EstruturaItem` é **indivisível** dentro do sistema: não pode estar em dois
   Setores ao mesmo tempo. (A exceção — divisão entre empresas de pintura terceirizadas no
   fim do processo — é controlada fora do sistema, por etiqueta no ERP delas.)
10. Ao chegar na Expedição, é gerado um Relatório Dimensional **por Peça** (não por Kit),
    informado pelo cliente que originou o Pedido. Hoje é feito para 100% das peças.
11. Uma Peça pode ser aprovada ou reprovada individualmente — um Kit pode ter peças
    aprovadas e reprovadas ao mesmo tempo.
12. Quando uma Peça é reprovada, **pode** (não é obrigatório, nem imediato) ser aberto um
    novo Pedido do tipo `Retrabalho`, vinculado ao Pedido original (`PedidoOrigemId`) e ao
    Relatório Dimensional que o originou (`RelatorioDimensional.PedidoRetrabalhoId`). O
    Pedido de Retrabalho também registra um `MotivoRetrabalho` categorizado:
    `ReprovacaoDimensional`, `ErroInterno` ou `SolicitacaoCliente`.
13. Um Pedido é considerado **concluído** quando o **último** Kit daquele Pedido é
    concluído (expedido/avaliado). `Pedido.DataConclusao` só é preenchida nesse momento.
14. O início real de produção de um Pedido é derivado (não armazenado) como o menor
    `DataEntrada` entre todos os `EstruturaSetorHistorico` dos itens daquele Pedido —
    usado para separar tempo em fila de tempo de produção. `DataEntrada` representa a
    **chegada** no setor (pode ficar esperando antes de começar); o início real da
    execução é opcionalmente registrado em `DataInicioExecucao`, no mesmo registro, sem
    afetar o cálculo de tempo em fila.
15. Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa.

## Pontos ainda em aberto

Nenhum ponto crítico de domínio em aberto no momento. Itens de infraestrutura (CI/CD,
detalhes de deploy) estão em `03-arquitetura-tecnica.md`.
