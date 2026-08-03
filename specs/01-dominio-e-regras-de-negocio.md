# 01 - Domínio e Regras de Negócio

## Glossário

| Termo | Definição |
|---|---|
| **Pedido** | Unidade máxima de trabalho, cadastrada no sistema. Tipo `Fabricacao` ou `Retrabalho`. Um Retrabalho referencia obrigatoriamente o Pedido original. |
| **Pedido.Numero** | O **código identificador do Pedido**, e o campo pelo qual as pessoas se referem a ele. **Não é gerado por este sistema**: vem de um sistema externo, que o cria sequencialmente. Aqui ele é apenas registrado. Por isso é **único global** (`UQ_Pedido_Numero`) — a sequência é controlada na origem e não se repete. É texto (`NVARCHAR(30)`), não número: aceita prefixos e separadores, e o sistema não valida formato nem gera valor. Consequência prática: se a origem emitir um código já cadastrado, o cadastro é recusado com 409 — e isso é o comportamento desejado, não um defeito. |
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo é descritivo — não impõe roteiro. |
| **Componente** | Registro de **catálogo** (receita padrão/template), reutilizável entre Pedidos. Não é a instância física — é a definição. |
| **EstruturaItem** | A árvore **real** usada em um Agrupamento específico, podendo ter sido copiada do catálogo (`Componente`) e customizada. É recursiva: um `EstruturaItem` pode ter `EstruturaItem` filhos. O nó de topo (sem pai) é chamado de **Peça**; os nós com pai são chamados de **Item**. Representa um **lote agregado** (quantidade), não uma unidade física individual — e esse lote é divisível por quantidades livres (ver regra 9). |
| **Material** | Produto de estoque (chapas, parafusos, roelas, etc.) consumido para fabricar um `EstruturaItem`. |
| **Setor** | Departamento de produção (ex.: Corte e Dobra, Usinagem) pelo qual um `EstruturaItem` pode passar. |
| **Roteiro** | Sequência de Setores que um `EstruturaItem` percorre. Pode ser padrão (catálogo) ou específico daquele Pedido/Agrupamento. |
| **Relatório Dimensional** | Avaliação de conformidade dimensional de uma Peça, **opcional** (o cliente exige em Peças específicas — ex.: primeira manufatura ou primeiro trabalho após reprovação no cliente; marcado no cadastro via EstruturaItem.RequerRelatorioDimensional). Quando existe, é **um relatório por Peça, acumulativo**: cada remessa avaliada gera uma RelatorioDimensionalAvaliacao com quantidade aprovada/reprovada. Aprovação/reprovação é por quantidade. Reprovação não exige retrabalho imediato. |
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. |
| **Expedição (remessa)** | Saída de uma quantidade de uma Peça para o cliente. Pode ser **parcial**: o cliente aceita uma parte vital antes e o restante depois. Cada remessa é uma linha em Expedicao. |
| **Perda** | Baixa de quantidade perdida em produção (some no armazém ou morre após um processo que deu errado). Vai para um bucket terminal; a reposição é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda'). |

## Regras de negócio

1. Um Pedido é composto por N Agrupamentos.
2. Um Agrupamento é composto por N `EstruturaItem` de topo (Peças).
3. Uma Peça pode ser composta por N Itens; um Item pode, por sua vez, ser composto por
   outros Itens — recursão sem limite de profundidade, resolvida numa única tabela
   autorreferenciada (`EstruturaItem`), evitando o ciclo conceitual entre "Peça" e "Item".
4. Um `EstruturaItem` folha (sem filhos) é composto por N Materiais.
5. Materiais precisam ser **separados** (retirados do estoque) e entregues ao setor
   responsável antes da fabricação daquele item.
6. Um `EstruturaItem` tem uma origem (primeiro Setor do seu roteiro) e pode passar por
   zero ou mais Setores adicionais antes de estar pronto.
7. O roteiro de Setores pode ser copiado de um padrão do catálogo (`Componente`), mas
   pode ser customizado por Pedido/Agrupamento — não é fixo.
8. Componentes (receita/catálogo) podem ser **padrão** (reutilizados entre Pedidos) ou
   **customizados** (criados especificamente para um Pedido, sem entrar no catálogo geral).
9. O lote de um `EstruturaItem` é **divisível por quantidades livres**: uma parte pode estar
   num Setor e outra parte em outro Setor ao mesmo tempo (ex.: 6 na Usinagem, 4 na Corte).
   Não há identidade de sub-lote (sem etiqueta/serial) — controla-se apenas *quanto* está
   *onde*. Invariante: **conservação de quantidade** — soma das unidades em todos os Setores +
   expedido (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça. (A divisão física
   entre pinturas terceirizadas no fim do processo segue controlada fora do sistema.)
10. O Relatório Dimensional é **opcional**: só quando o cliente exige, em Peças específicas
    (ex.: primeira manufatura, primeiro trabalho após reprovação no cliente). Isso é sabido
    no cadastro do Pedido e marcado **por Peça** em `EstruturaItem.RequerRelatorioDimensional`.
    Quando existe, é **um relatório por Peça, acumulativo**: cada remessa avaliada pelo
    cliente gera uma `RelatorioDimensionalAvaliacao` (quantidade avaliada/aprovada/reprovada).
11. Aprovação e reprovação são **por quantidade** dentro da Peça: numa mesma avaliação parte
    das unidades pode aprovar e parte reprovar; e um Agrupamento pode ter Peças com
    resultados diferentes ao mesmo tempo.
12. Quando uma quantidade é reprovada, **pode** (não é obrigatório, nem imediato) ser aberto
    um novo Pedido do tipo `Retrabalho` **para aquela quantidade**, vinculado ao Pedido
    original (`PedidoOrigemId`) e à avaliação que o originou
    (`RelatorioDimensionalAvaliacao.PedidoRetrabalhoId`). O Pedido de Retrabalho registra um
    `MotivoRetrabalho` categorizado: `ReprovacaoDimensional`, `ErroInterno`,
    `SolicitacaoCliente` ou `Perda`.
13. Uma Peça está **concluída** quando nada dela está mais em produção — toda a quantidade
    virou expedido (`Expedicao`) **ou** perdido (`Perda`). Um Agrupamento conclui quando
    todas as suas Peças concluem (`Agrupamento.DataConclusao`), e um Pedido conclui quando o
    **último** Agrupamento conclui (`Pedido.DataConclusao`). Não depende mais de "avaliado",
    já que o Relatório Dimensional é opcional.
14. O início real de produção de um Pedido é derivado (não armazenado) como o menor
    `DataEntrada` entre todos os `EstruturaSetorHistorico` dos itens daquele Pedido —
    usado para separar tempo em fila de tempo de produção. `DataEntrada` representa a
    **chegada** no setor (pode ficar esperando antes de começar); o início real da
    execução é opcionalmente registrado em `DataInicioExecucao`, no mesmo registro, sem
    afetar o cálculo de tempo em fila.
15. Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa.
16. A **expedição pode ser parcial** (remessas): o cliente aceita uma parte vital antes e o
    restante segue depois. Cada remessa é uma linha em `Expedicao` com a quantidade. A soma
    das remessas de uma Peça nunca excede a quantidade total (validado na aplicação).
17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem` ou
    `MortaEmProcesso`), levando a quantidade ao bucket terminal "perdido". Para repor, abre-se
    um Pedido de Retrabalho separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça
    original. Como na reprovação, registrar a perda **não** abre retrabalho automaticamente.

## Pontos ainda em aberto

Nenhum ponto crítico de domínio em aberto no momento. Itens de infraestrutura (CI/CD,
detalhes de deploy) estão em `03-arquitetura-tecnica.md`.
