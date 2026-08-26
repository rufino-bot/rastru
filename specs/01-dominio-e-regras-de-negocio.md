# 01 - Domínio e Regras de Negócio

## Glossário

| Termo | Definição |
|---|---|
| **Pedido** | Unidade máxima de trabalho, cadastrada no sistema. Tipo `Fabricacao` ou `Retrabalho`. Um Retrabalho referencia obrigatoriamente o Pedido original. |
| **Pedido.Numero** | O **código identificador do Pedido**, e o campo pelo qual as pessoas se referem a ele. **Não é gerado por este sistema**: vem de um sistema externo, que o cria sequencialmente. Aqui ele é apenas registrado. Por isso é **único global** (`UQ_Pedido_Numero`) — a sequência é controlada na origem e não se repete. É texto (`NVARCHAR(30)`), não número: aceita prefixos e separadores, e o sistema não valida formato nem gera valor. Consequência prática: se a origem emitir um código já cadastrado, o cadastro é recusado com 409 — e isso é o comportamento desejado, não um defeito. |
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo é descritivo — não impõe roteiro. |
| **Componente** | Registro de **catálogo** (receita padrão/template), reutilizável entre Pedidos. Não é a instância física — é a definição. |
| **Componente.Codigo** | O **identificador único da peça de catálogo** dentro deste sistema. É **alfanumérico** (`NVARCHAR(50)`) e **único global** (`UQ_Componente_Codigo`). Decisão do dono do projeto (2026-08-03): **o sistema não modela a numeração do cliente.** Nem toda peça chega com código definido pelo cliente, e o critério varia de cliente para cliente — essa regra **não é absorvida aqui**. O que vale é que toda peça de catálogo tenha um identificador único neste sistema, o que é o que permite reconhecê-la quando ela é pedida **várias vezes ao longo do ano**. Quem cadastra atribui o valor (reaproveitando o código do cliente quando existir); o sistema não gera nem valida formato. Consequência operacional a vigiar: o ganho depende de a peça repetida ser **encontrada e reutilizada**, não recadastrada sob um código novo — cadastro duplicado sob códigos diferentes não viola nenhuma constraint e passa despercebido. |
| **Componente.ArquivoSolido** | Referência ao arquivo de **sólido 3D** (CAD) da peça de catálogo — STEP ou STL. É obrigação de negócio para toda Peça de Pedido, mas coluna **nullable** por não valer para todo Componente; ver regra 18. `Componente.ArquivoFoto`, ao lado, é uma foto de referência **opcional**. |
| **EstruturaItem** | A árvore **real** usada em um Agrupamento específico, podendo ter sido copiada do catálogo (`Componente`) e customizada. É recursiva: um `EstruturaItem` pode ter `EstruturaItem` filhos. O nó de topo (sem pai) é chamado de **Peça**; os nós com pai são chamados de **Item**. Representa um **lote agregado** (quantidade), não uma unidade física individual — e esse lote é divisível por quantidades livres (ver regra 9). |
| **EstruturaItem.Descricao** | Nome próprio do nó dentro do Agrupamento. NULL = usa a descrição do `Componente` de origem. Serve ao item **ad-hoc** (`ComponenteId` NULL), que sem ela chega sem nome nenhum à tela do operador; ver regra 19. |
| **Material** | Produto de estoque (chapas, parafusos, roelas, etc.) consumido para fabricar um `EstruturaItem`. |
| **Material.Codigo** | O **identificador único do material** dentro deste sistema. **Mesma regra do `Componente.Codigo`**, por decisão explícita: alfanumérico (`NVARCHAR(50)`), **único global** (`UQ_Material_Codigo`), atribuído por quem cadastra, sem geração nem validação de formato pelo sistema. A numeração de fornecedor **não** é modelada aqui. |
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

18. **Toda Peça que um Pedido precisa tem um sólido 3D** — é obrigação do negócio, anterior a
    este sistema: a peça não entra em produção sem o arquivo de CAD. O sistema guarda a
    referência em `Componente.ArquivoSolido` (STEP ou STL — `.SLDPRT` é proprietário e não é
    lido). **A coluna é nullable e isso é deliberado**: a obrigatoriedade vale para *Peça de
    Pedido*, não para toda linha de catálogo — um `Componente` do tipo `Bruto` não tem sólido —
    e o banco não distingue os dois casos nessa tabela. Logo, é regra de aplicação, cobrada na
    Fase 2 (onde a Peça nasce), não constraint de schema. `Componente.ArquivoFoto` é **opcional**
    e serve só para o operador reconhecer a peça; não substitui o sólido.

    **Decidido em 2026-08-04, a aplicar na Fase 2 (não implementado ainda):** uma **Peça**
    (`EstruturaItem` sem pai) **sempre** referencia um `Componente`; só um **Item** (nó com pai)
    pode ser ad-hoc (`ComponenteId` NULL). Fecha com
    `CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)`.

    O furo que isso tapa: o sólido mora em `Componente`, a obrigação vale em `EstruturaItem`, e a
    ponte entre os dois é nullable — quando é NULL não existe linha de `Componente`, então não é
    campo vazio, é **campo inexistente**. Hoje o schema aceita uma Peça ad-hoc (nenhuma constraint
    impede), e para ela a regra 18 é literalmente inexprimível.

    Precisão que importa: a constraint garante que existe **onde** pendurar o sólido. Que ele
    esteja *preenchido* continua regra de aplicação — um `CHECK` não alcança outra tabela, e
    `ArquivoSolido` segue nullable por causa do `Bruto`.

    Motivação registrada, porque é o que sustenta o custo de "peça de uma vez só vira linha de
    catálogo":
    - uma peça ad-hoc já precisa de `Codigo` (senão o operador não a acha), descrição e sólido —
      isso **já é** uma linha de catálogo, só sem o nome;
    - no cadastro não dá para saber se vai repetir, e `Componente.Ativo` já tira da lista o que
      não repetiu;
    - **a peça ad-hoc que a fábrica decide promover a catálogo não exige migração nenhuma**: sob
      esta regra a linha já existe desde o começo, então promover é decisão de uso, não mudança de
      dado. Sem ela, promover seria criar o `Componente` e ainda decidir se os `EstruturaItem`
      antigos passam a apontar para ele;
    - o ad-hoc não morre, só recua para onde é de fato usado — **sub-Itens abaixo da Peça**;
    - custo real, e ele já tem dono: o catálogo acumula linhas que nunca se repetem, e o risco
      recai sobre a mesma disciplina registrada em `Componente.Codigo` — o ganho depende de a peça
      repetida ser **encontrada e reutilizada**, não recadastrada sob código novo.

    Alternativas descartadas: repetir `ArquivoSolido` em `EstruturaItem` (dois lugares para olhar,
    e abre override de geometria — se a geometria mudou não é mais a mesma peça; nota que em
    `Descricao` o override é útil, em geometria é perigoso, por isso a mesma forma dá respostas
    diferentes nos dois campos); e aceitar Peça sem sólido (a regra 18 viraria "quase toda peça",
    e a busca por foto nasceria cega justamente na peça de uma vez só — a que o operador **menos**
    reconhece, já que a de catálogo volta várias vezes por ano).

19. **`EstruturaItem.Descricao` nomeia o nó**; quando NULL, o nome exibido é o do `Componente`
    de origem. Existe porque um item ad-hoc (`ComponenteId` NULL) não tinha **nenhum** texto
    próprio: numa consulta de "o que está no meu setor" ele chegava anônimo à tela do operador —
    exatamente a pessoa que não sabe o que a peça é. Encontrado ao provar a consulta de setor
    contra dados semeados (2026-08-03).

20. **A receita padrão de filhos (`ComponenteFilhoPadrao`) não pode conter ciclo, em nenhuma
    profundidade.** É a regra que existe porque a receita é um **grafo**: cada linha aponta de um
    `Componente` pai para um `Componente` filho, que por sua vez tem receita própria — e é essa
    cadeia que a Fase 2 percorre para copiar a receita recursivamente ao montar um `EstruturaItem`
    a partir de um Componente padrão. Um ciclo nesse grafo faria a cópia recursiva girar para
    sempre. A verificação é sobre o **grafo resultante** da gravação (o estado depois de aplicar a
    substituição inteira que o `POST` representa), não sobre o estado anterior a ela — é essa
    nuance que torna possível consertar um ciclo já gravado: uma substituição que remove a aresta
    que fechava o ciclo é aceita, mesmo partindo de um grafo sujo.
21. **Setor repetido no roteiro padrão (`ComponenteRoteiroPadrao`) é permitido, e significa
    retorno ao mesmo setor** — não é duplicata a corrigir. Esta é a regra que existe justamente
    para que ninguém "conserte" esse comportamento no futuro achando que é bug. O roteiro é uma
    **sequência**, não um grafo: cada passo aponta para um `Setor`, que não referencia `Componente`
    nem tem roteiro próprio, então a travessia sempre termina, por mais vezes que o mesmo Setor
    apareça nela. É por isso que a chave única do schema é `UQ_ComponenteRoteiroPadrao
    (ComponenteId, Ordem)` — **não** `(ComponenteId, SetorId)`: a unicidade é da posição na
    sequência, não do Setor visitado.

    **Distinção com a regra 20**: as duas regras usam "repetir" em sentidos opostos porque as
    estruturas são de naturezas diferentes. O roteiro repete um **nó terminal** (`Setor`, que não
    tem para onde apontar de volta) — por isso repetir é sempre seguro e é permitido. A receita de
    filhos repete um **nó não-terminal** (`Componente`, que tem receita própria) — por isso repetir
    pode fechar um ciclo, e é isso, não a repetição em si, que a regra 20 proíbe.

## Pontos ainda em aberto

- **Busca de peça por foto** (comparar a foto do operador contra as silhuetas do sólido).
  Não é decisão de domínio ainda: depende de um spike medir a taxa de acerto. Ver
  `06-roadmap-mvp.md`.

Itens de infraestrutura (CI/CD, detalhes de deploy) estão em `03-arquitetura-tecnica.md`.
