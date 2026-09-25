# 01 - Domínio e Regras de Negócio

## Glossário

| Termo | Definição |
|---|---|
| **Pedido** | Unidade máxima de trabalho, cadastrada no sistema. Tipo `Fabricacao` ou `Retrabalho`. Um Retrabalho referencia obrigatoriamente o Pedido original. |
| **Pedido.Numero** | O **código identificador do Pedido**, e o campo pelo qual as pessoas se referem a ele. **Não é gerado por este sistema**: vem de um sistema externo, que o cria sequencialmente. Aqui ele é apenas registrado. Por isso é **único global** (`UQ_Pedido_Numero`) — a sequência é controlada na origem e não se repete. É texto (`NVARCHAR(30)`), não número: aceita prefixos e separadores, e o sistema não valida formato nem gera valor. Consequência prática: se a origem emitir um código já cadastrado, o cadastro é recusado com 409 — e isso é o comportamento desejado, não um defeito. |
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo não impõe roteiro, mas **não é mais só descritivo**: um Agrupamento Kit fica sujeito à trava de montagem e ao conjunto completo (regras 24 e 25); um Avulso, não. Até 2026-09-15 o Tipo era só descritivo; a reversão parcial veio de esclarecimento do processo da fábrica — ver `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`. |
| **Componente** | Registro de **catálogo** (receita padrão/template), reutilizável entre Pedidos. Não é a instância física — é a definição. |
| **Componente.Codigo** | O **identificador único da peça de catálogo** dentro deste sistema. É **alfanumérico** (`NVARCHAR(50)`) e **único global** (`UQ_Componente_Codigo`). Decisão do dono do projeto (2026-08-03): **o sistema não modela a numeração do cliente.** Nem toda peça chega com código definido pelo cliente, e o critério varia de cliente para cliente — essa regra **não é absorvida aqui**. O que vale é que toda peça de catálogo tenha um identificador único neste sistema, o que é o que permite reconhecê-la quando ela é pedida **várias vezes ao longo do ano**. Quem cadastra atribui o valor (reaproveitando o código do cliente quando existir); o sistema não gera nem valida formato. Consequência operacional a vigiar: o ganho depende de a peça repetida ser **encontrada e reutilizada**, não recadastrada sob um código novo — cadastro duplicado sob códigos diferentes não viola nenhuma constraint e passa despercebido. |
| **Componente.ArquivoSolidoId** | Referência (FK para `dbo.ArquivoDeComponente`) ao arquivo de **sólido 3D** (CAD) da peça de catálogo — **STL**, e só (decisão de 2026-09-12, do usuário: o three.js lê STL nativamente, enquanto STEP exigiria parser de terceiros no navegador; `.SLDPRT` continua fora, por ser proprietário). É obrigação de negócio para toda Peça de Pedido, mas coluna **nullable** por não valer para todo Componente; ver regra 18. `Componente.ArquivoFoto`, ao lado, é uma foto de referência **opcional**. |
| **EstruturaItem** | A árvore **real** usada em um Agrupamento específico, podendo ter sido copiada do catálogo (`Componente`) e customizada. É recursiva: um `EstruturaItem` pode ter `EstruturaItem` filhos. O nó de topo (sem pai) é chamado de **Peça**; os nós com pai são chamados de **Item**. Representa um **lote agregado** (quantidade), não uma unidade física individual — e esse lote é divisível por quantidades livres (ver regra 9). |
| **EstruturaItem.Descricao** | Nome próprio do nó dentro do Agrupamento. NULL = usa a descrição do `Componente` de origem. Serve ao item **ad-hoc** (`ComponenteId` NULL), que sem ela chega sem nome nenhum à tela do operador; ver regra 19. |
| **EstruturaItem.QuantidadePorPai** | Quantos daquele nó entram em **uma** unidade do pai, guardado ao lado da quantidade absoluta (`EstruturaItem.Quantidade`). Obrigatória em todo Item, nula na Peça. Serve à montagem de todo nó com filhos, às tarefas do Movimentador e à sobra (regras 23, 24 e 30) e, no Kit, à trava e ao conjunto completo (regras 24 e 25); o apontamento continua movimentando a quantidade absoluta. Ver regra 26. Decidida em 2026-09-15; a coluna entra no schema na Fase 3 (decisão de 2026-09-24, que trouxe a montagem da 3B para a 3). |
| **Material** | Produto de estoque (chapas, parafusos, roelas, etc.) consumido para fabricar um `EstruturaItem`. |
| **Material.Codigo** | O **identificador único do material** dentro deste sistema. **Mesma regra do `Componente.Codigo`**, por decisão explícita: alfanumérico (`NVARCHAR(50)`), **único global** (`UQ_Material_Codigo`), atribuído por quem cadastra, sem geração nem validação de formato pelo sistema. A numeração de fornecedor **não** é modelada aqui. |
| **Setor** | Departamento de produção (ex.: Corte e Dobra, Usinagem) pelo qual um `EstruturaItem` pode passar. |
| **Setor.UtilizaKit** | Marca, no cadastro do Setor, de que ali se montam os nós de Agrupamento Kit a partir dos filhos — hoje, a Solda; a regra não depende do nome do Setor. Em Agrupamento Kit, é o que ativa a trava de montagem e o conjunto completo (regras 24 e 25). Decidida em 2026-09-15; a coluna entra no schema no início da Fase 3B. |
| **Roteiro** | Sequência de Setores que um `EstruturaItem` percorre. Pode ser padrão (catálogo) ou específico daquele Pedido/Agrupamento. |
| **Aguardando coleta** | Estado da quantidade que ainda não foi levada ao próximo destino depois de o operador dá-la como terminada num Setor (regra 22) — ou, no Pedido de Retrabalho, depois de nascer marcada **pronta** (regra 27). Conta como **em produção** para a conservação de quantidade (regra 9) e é o que alimenta as tarefas do Movimentador (regra 23). No banco, é uma posição do livro de movimentações (`dbo.Movimentacao`), guardada no Setor e no passo em que a quantidade terminou; o destino é calculado (regra 29). |
| **Relatório Dimensional** | Avaliação de conformidade dimensional de uma Peça, **opcional** (o cliente exige em Peças específicas — ex.: primeira manufatura ou primeiro trabalho após reprovação no cliente; marcado no cadastro via EstruturaItem.RequerRelatorioDimensional). Quando existe, é **um relatório por Peça, acumulativo**: cada remessa avaliada gera uma RelatorioDimensionalAvaliacao com quantidade aprovada/reprovada. Aprovação/reprovação é por quantidade. Reprovação não exige retrabalho imediato. |
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. |
| **Movimentador** | Perfil de quem leva ao próximo destino a quantidade que aguarda coleta e registra a entrada nele (regra 22); em Agrupamento Kit, os filhos vão ao Setor com `UtilizaKit` em conjuntos completos (regra 25). O que ele tem a levar é a lista de tarefas da regra 23. Decidido em 2026-09-15; passa a existir no sistema na Fase 3. |
| **Expedição (remessa)** | Saída de uma quantidade de uma Peça para o cliente. Pode ser **parcial**: o cliente aceita uma parte vital antes e o restante depois. Cada remessa é uma linha em Expedicao. |
| **Perda** | Baixa de quantidade de um `EstruturaItem` (Peça ou Item) que sai da produção: some no armazém, morre após um processo que deu errado, ou é **descarte** de sobra que nunca foi usada (regras 25 e 30). Vai para um bucket terminal; a reposição, quando há, é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda') — descarte não é reposto. O motivo `Descarte` entra no schema no início da Fase 5 (regra 17). |
| **Montado** | Destino terminal da quantidade de um Item que virou parte do pai: ao registrar "montei N", baixa-se `N × QuantidadePorPai` de cada filho direto para "montado" (regra 24). É um dos quatro termos da conservação de quantidade (regra 9). O registro de montagem existe para todo nó com filhos, de Kit ou Avulso; só a validação da trava é restrita ao Kit (regra 24). O destino entra no schema na Fase 3. Não confundir com o **total montado** do pai, que conta quantas unidades do pai já foram montadas e limita a saída dele do Setor com `UtilizaKit` (regra 24). |
| **A iniciar** | Estado da quantidade de um nó que ainda não entrou em nenhum Setor. Todo nó nasce assim, com a quantidade inteira. Conta como **em produção** (regra 9). Sai daqui pela primeira entrada, registrada pelo operador do primeiro Setor do Roteiro quando ele pega o material para trabalhar (regra 28). |
| **Local de expedição** | O lugar da fábrica para onde o Movimentador leva a Peça que terminou o Roteiro, e de onde as cargas são expedidas (regra 29). Não é um Setor: não fabrica, não entra em Roteiro nem nos KPIs de tempo por Setor. Conta como **em produção** até a expedição (Fase 5). |
| **Sobra** | Quantidade de um Item que terminou o Roteiro, ou foi entregue para a montagem, além do que o pai ainda precisa — e o que o pai precisa é `(quantidade do pai − total montado) × QuantidadePorPai`. É identificada pelo estado e não vira tarefa de ninguém; se não for usada, sai como perda de motivo `Descarte` (regras 17 e 30). Não é perda até o `Descarte` ser registrado. |

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
   *onde*. Invariante: **conservação de quantidade**, para **todo** `EstruturaItem` (Peça ou
   Item) — em produção (a iniciar, nos Setores, aguardando coleta, aguardando montagem ou no
   local de expedição — regras 22, 28 e 29) + montado dentro do pai (regra 24) + expedido
   (`Expedicao`) + perdido (`Perda`) = quantidade total do nó.
   "Montado" só existe para Item, e expedição só para Peça. (A divisão física entre pinturas
   terceirizadas no fim do processo segue controlada fora do sistema.)

   **Alterada pela decisão de 2026-09-15:** até então o invariante falava só da Peça e não tinha
   o termo "montado" — um Item soldado dentro do pai não era nenhum dos três termos antigos. O
   destino "montado" só entra no schema (`02-modelo-de-dados.sql`) no início da Fase 3B.

   **Alterada de novo em 2026-09-24** (spec da Fase 3): "em produção" ganhou as posições a
   iniciar, aguardando montagem e no local de expedição (regras 28 e 29), e o destino "montado"
   entrou no schema na **Fase 3**, não na 3B, porque montar passou a valer para todo nó com filhos
   (regra 24).
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
14. O início real de produção de um Pedido é derivado (não armazenado) como a menor data entre
    as primeiras entradas (regra 28) dos nós daquele Pedido, no livro de movimentações — usado
    para separar tempo em fila de tempo de produção. A primeira entrada é o início do trabalho,
    porque o operador a registra ao pegar o material; as entradas seguintes, registradas pelo
    Movimentador ao entregar, são **chegada** no Setor (a peça pode esperar antes de começar).
    Separar fila de execução dentro do Setor é decisão da Fase 6. **Alterada em 2026-09-24**
    (spec da Fase 3): antes, o início vinha de `EstruturaSetorHistorico.DataEntrada`, tabela que
    o livro de movimentações substituiu.
15. Cada Usuário tem um Perfil (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa. O **Movimentador** entrou pela
    decisão de 2026-09-15 (regra 22) e passa a existir no sistema na Fase 3.
16. A **expedição pode ser parcial** (remessas): o cliente aceita uma parte vital antes e o
    restante segue depois. Cada remessa é uma linha em `Expedicao` com a quantidade. A soma
    das remessas de uma Peça nunca excede a quantidade total (validado na aplicação).
17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem`, `MortaEmProcesso` ou
    `Descarte`), levando a quantidade ao bucket terminal "perdido". Pode ser registrada em
    **qualquer** `EstruturaItem`, Peça ou Item. Para repor, abre-se um Pedido de Retrabalho
    separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça original. Como na reprovação,
    registrar a perda **não** abre retrabalho automaticamente. `Descarte` é a sobra que nunca foi
    usada (regras 25 e 30) e não se repõe. Quando a perda de uma parte impede montar o pai, vale a
    regra 27.

    **Alterada pela decisão de 2026-09-15:** entraram o motivo `Descarte` e a perda de Item. No
    schema, o `Descarte` só entra no início da Fase 5 — até lá, a `CK_Perda_Motivo` de
    `02-modelo-de-dados.sql` aceita só `PerdaArmazem` e `MortaEmProcesso`. A perda de Item não
    depende de mudança de schema: `dbo.Perda` já referencia qualquer `EstruturaItem`.

18. **Toda Peça que um Pedido precisa tem um sólido 3D** — é obrigação do negócio, anterior a
    este sistema: a peça não entra em produção sem o arquivo de CAD. O sistema guarda a
    referência em `Componente.ArquivoSolidoId` (**STL**, e só — decisão de 2026-09-12, do usuário:
    o three.js lê STL nativamente, enquanto STEP exigiria parser de terceiros no navegador;
    `.SLDPRT` continua fora, por ser proprietário). **A coluna é nullable e isso é deliberado**:
    a obrigatoriedade vale para *Peça de Pedido*, não para toda linha de catálogo — um
    `Componente` do tipo `Bruto` não tem sólido —
    e o banco não distingue os dois casos nessa tabela. Logo, é regra de aplicação, cobrada na
    Fase 2B — onde nasce o upload que permite preenchê-la; a Fase 2 fechou só o gancho, a
    constraint `CK_EstruturaItem_PecaTemComponente` —, não constraint de schema.
    `Componente.ArquivoFoto` é **opcional** e serve só para o operador reconhecer a peça; não
    substitui o sólido.

    **Decidido em 2026-08-04, aplicado na Fase 2:** uma **Peça**
    (`EstruturaItem` sem pai) **sempre** referencia um `Componente`; só um **Item** (nó com pai)
    pode ser ad-hoc (`ComponenteId` NULL). Fecha com
    `CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL)`.

    O furo que isso tapa: o sólido mora em `Componente`, a obrigação vale em `EstruturaItem`, e a
    ponte entre os dois é nullable — quando é NULL não existe linha de `Componente`, então não é
    campo vazio, é **campo inexistente**. Antes desta constraint, o schema aceitava uma Peça ad-hoc
    (nenhuma constraint impedia), e para ela a regra 18 era literalmente inexprimível.

    Precisão que importa: a constraint garante que existe **onde** pendurar o sólido. Que ele
    esteja *preenchido* continua regra de aplicação — um `CHECK` não alcança outra tabela, e
    `ArquivoSolidoId` segue nullable por causa do `Bruto`.

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

    Alternativas descartadas: repetir `ArquivoSolidoId` em `EstruturaItem` (dois lugares para olhar,
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

    **Consequência aplicada na criação e na edição (Fase 2):** um nó ad-hoc não pode ficar sem
    `Descricao` — nem ao nascer, nem por uma edição que a esvazie. Sem `ComponenteId`, o nó não tem
    de onde herdar — aceitar a edição devolveria o nó exatamente ao anonimato que esta regra existe
    para impedir. A mesma condição vale nos dois caminhos: `AcrescentarFilho` a exige ao criar o nó
    ad-hoc, e `EditarNo` a reaplica na edição, para não deixar a `Descricao` esvaziar depois.

    **Editar um nó de catálogo congela a descrição herdada como própria — comportamento aceito,
    decisão do usuário de 2026-09-02.** A leitura direta desta regra ("descrição nula herda do
    `Componente`") continua verdadeira, mas ela sozinha faz esperar que um nó de catálogo acompanhe
    o catálogo para sempre, e na prática **não acompanha depois da primeira edição**. O mecanismo:
    a API entrega a descrição **já resolvida** — `EstruturaItemDto.Descricao` traz o texto do
    `Componente` quando `EstruturaItem.Descricao` é NULL, sem bandeira que separe "própria" de
    "herdada" —, o formulário de edição pré-preenche o campo com esse texto resolvido (para o
    usuário editar a partir do que vê, e não de um campo em branco), e `EditarNo` grava o que
    recebe. Logo, salvar uma edição de **só a quantidade** grava a descrição herdada como
    `EstruturaItem.Descricao` própria, e a partir daí uma mudança na descrição do `Componente` não
    alcança mais aquele nó.

    Foi aceito assim porque o nó nasce como **cópia** da receita, não como referência viva a ela — a
    quantidade e a estrutura já divergem do catálogo pelo mesmo motivo —, e porque a alternativa
    exigiria distinguir as duas descrições no contrato da API para que a interface pudesse oferecer
    a escolha. Quem for **desfazer** esta decisão começa por aí: trazer a referência do catálogo no
    DTO. Enquanto ela valer, não conte com um nó de catálogo seguindo mudanças de descrição do
    `Componente` — e um relatório que precise do nome de catálogo lê o `Componente`, não o nó.

20. **A receita padrão de filhos (`ComponenteFilhoPadrao`) não pode conter ciclo, em nenhuma
    profundidade.** É a regra que existe porque a receita é um **grafo**: cada linha aponta de um
    `Componente` pai para um `Componente` filho, que por sua vez tem receita própria — e é essa
    cadeia que a Fase 2 percorre para copiar a receita recursivamente ao montar um `EstruturaItem`
    a partir de um Componente padrão. Um ciclo nesse grafo faria a cópia recursiva girar para
    sempre. A verificação é sobre o **grafo resultante** da gravação (o estado depois de aplicar a
    substituição inteira que o `POST` representa), não sobre o estado anterior a ela — é essa
    nuance que torna possível consertar um ciclo já gravado: uma substituição que remove a aresta
    que fechava o ciclo é aceita, mesmo partindo de um grafo sujo. A leitura desse grafo acontece
    fora da transação que grava a substituição (`ReceitaPadraoUseCase.cs:439-448`): dois `POST`
    simultâneos em componentes diferentes podem gravar um ciclo que nenhum dos dois via sozinho.
    Por isso a regra é defesa em profundidade na escrita, não garantia de que o grafo gravado seja
    sempre acíclico — uma travessia recursiva que dependa disso, como a cópia da Fase 2, precisa da
    própria guarda contra ciclo.
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

*As regras 22 a 27 foram decididas em 2026-09-15 (spec
`docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`) — os tetos das regras 23
a 25, em 2026-09-19 — e são implementadas nas Fases 3, 3B e 5 de `06-roadmap-mvp.md` (a montagem
de todo nó e `QuantidadePorPai`, na Fase 3, por decisão de 2026-09-24); o schema correspondente
entra no início de cada fase.*

22. **Terminar e mover são ações separadas** — na fábrica, feitas por pessoas diferentes. O
    operador registra que terminou o trabalho num Setor, e aquela quantidade passa a **aguardar
    coleta**; o **Movimentador** a leva e registra a entrada no próximo destino. Vale para todo
    `EstruturaItem`, de Agrupamento Kit ou Avulso. Um filho que concluiu o próprio Roteiro aguarda
    coleta para a montagem do pai. Para a conservação de quantidade (regra 9), aguardar coleta
    conta como em produção; no banco, ele é uma posição do livro de movimentações (spec da Fase 3).
23. **As tarefas do Movimentador são calculadas a partir do estado**, não gravadas como aviso:
    quando alguém leva, a tarefa some sozinha. São duas:
    - **Item pronto** — quantidade aguardando coleta, menos a sobra (regra 30). É tarefa, exceto
      para filho de Agrupamento Kit a caminho de Setor com `UtilizaKit`, em que é só informativo,
      porque esse filho não vai sozinho (regra 25).
    - **Kit pronto para montagem** — tarefa que aparece quando os filhos diretos de um nó,
      aguardando coleta, formam ao menos um conjunto completo **que o nó ainda precisa receber**
      (regra 25). O número de conjuntos é o mínimo, entre os filhos diretos, de
      ⌊quantidade aguardando coleta ÷ `QuantidadePorPai`⌋, sem passar desse mesmo teto, o da
      entrada na regra 25. Sem o teto, a divisão contaria a sobra de refugo que a regra 26 admite:
      se um filho de 45 com razão 4 sob um pai de 10 fosse o único filho direto, daria 11
      conjuntos.

    Notificação no celular é reforço desta lista, não substituto (Fase 3C).
24. **Montagem e trava de montagem.** Montar é registro de **todo** nó com filhos, de Agrupamento
    Kit ou Avulso — até 2026-09-24, montar só existia sob a trava (spec da Fase 3). Olha só os
    **filhos diretos** do nó. O registro, os dois tetos e a baixa dos três primeiros itens valem
    para todo nó; a **trava**, dos dois últimos, só quando três condições valem juntas: o
    Agrupamento é **Kit**, o Setor tem **`UtilizaKit`** (marca no cadastro do Setor — hoje, a
    Solda), e o nó **tem filhos** (Peça ou Item de submontagem).
    - **Montar é registro próprio**, separado de mover. O operador registra "montei N" no Setor
      onde os filhos estão (regra 29); o sistema
      aceita se N não passar do mínimo, entre os filhos diretos, de
      ⌊quantidade do filho no Setor ÷ `QuantidadePorPai`⌋, **nem do que ainda falta montar do
      nó** — a quantidade dele menos o total já montado. Este teto é outro que o da regra 25, e
      vale mesmo com ele, porque nem todo filho chega ao Setor por uma entrada: um filho pode ser
      montado no mesmo Setor em que o pai será montado, caso que a spec da Fase 3B decide. A
      montagem pode ser **parcial** (montar 6 de 10), o que casa com a expedição parcial (regra 16).
    - Ao montar, baixa-se `N × QuantidadePorPai` de cada filho direto para o destino terminal
      **"montado"**, **gravando a baixa de cada filho**, e não só N — editar a razão depois não
      reescreve o passado. N soma ao **total montado** do nó.
    - A **saída** do nó de um Setor com `UtilizaKit` é limitada ao total montado, em **qualquer**
      passagem: se o nó volta à Solda (regra 21), os filhos já viraram o nó, e é o total montado
      que conta.
    - A ordem de baixo para cima é consequência, não cálculo: um nó intermediário só existe na
      Solda depois de montado, então o pai dele só monta depois.
25. **Conjunto completo.** Um Kit pode ir à Solda em parte do pai (os conjuntos de 7 de 10), mas
    **nunca incompleto nem além do necessário**: a entrada de filhos de Agrupamento Kit num Setor
    com `UtilizaKit` só é aceita em conjuntos completos — `N × QuantidadePorPai` de **todos** os
    filhos diretos, juntos, na mesma movimentação —, e com N **sem passar do que o nó ainda
    precisa receber**: a quantidade dele, menos o total já montado e menos os conjuntos que já
    estão à espera de montagem em **qualquer** Setor com `UtilizaKit`. Esses conjuntos à espera são
    os que **entraram e ainda não foram montados**, não o mínimo por filho: uma unidade que perdeu
    parte dentro da Solda não se completa com refugo novo. (Como a perda do próprio nó entra nesta
    conta é decisão da spec da Fase 3B.) O motivo é físico: peça solta ou a mais na Solda ocupa espaço, e, se
    houver perda antes de o resto chegar, aquele espaço fica sem destino. Não há exceção para
    completar conjunto que perdeu parte dentro da Solda — isso é perda (regra 27). A **sobra** —
    tudo o que passa do que o nó precisa, feche conjunto ou não (ex.: refugo além do necessário)
    — nunca entra na Solda e, se não for usada, sai como perda de motivo `Descarte` (regra 17).
26. **`EstruturaItem.QuantidadePorPai`** guarda quantos daquele nó entram em **uma** unidade do
    pai, **ao lado** da quantidade absoluta (`EstruturaItem.Quantidade`). É **obrigatória em todo
    Item e nula na Peça**. A cópia da receita a preenche com
    `ComponenteFilhoPadrao.QuantidadePadrao`; num Item ad-hoc, quem cadastra informa. A quantidade
    absoluta continua sendo o que o apontamento movimenta; a razão serve à montagem de todo nó
    (regra 24), às tarefas e à sobra (regra 30) e, no Kit, à trava e ao conjunto completo.
    **Não existe invariante entre as duas**: um Item de 45 com razão 4 sob um pai de 10 é legítimo
    (sobra de refugo). A Fase 2 havia decidido não guardar a razão; ver a errata na §2.1 da spec da
    Fase 2 (`docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`).
27. **Perda que impede montar.** Quando a perda de uma parte impede montar uma unidade do pai, a
    perda **sobe até a Peça do topo**: registra-se a perda daquela unidade da Peça no Pedido
    original, que conclui normalmente (regra 13), e as partes daquela unidade que existem e ainda
    não foram montadas saem junto como perda. A reposição é um Pedido de Retrabalho (regra 17),
    **aberto** por quem registra a perda (Qualidade ou PCP) e **cadastrado** pelo PCP para a Peça
    faltante, em que o que já existe é marcado **pronto** — só dentro
    da árvore daquela unidade; o resto continua no Pedido original.
    - Nó **pronto** não percorre Roteiro: folha pronta já foi fabricada; nó com filhos pronto já
      foi montado (total montado = quantidade, e os filhos nem precisam existir no Retrabalho).
    - Nó pronto nasce **aguardando coleta** e é levado normalmente (regra 22).
    - As partes saem do original como perda, sem vínculo por nó com o Retrabalho: o Retrabalho já
      nasce ligado ao original (`PedidoOrigemId`, `MotivoRetrabalho = 'Perda'`), e confrontar os
      dois mostra o destino do saldo — perdido ou descartado de fato, ou reposto e expedido depois.

*As regras 28 a 30 foram decididas em 2026-09-24, no brainstorm da Fase 3 (spec
`docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`), e são implementadas na
Fase 3 — menos o registro do `Descarte` da regra 30, que é da Fase 5.*

28. **Todo nó nasce a iniciar, e a primeira entrada é do operador.** A quantidade inteira de um nó
    nasce **a iniciar**, que conta como em produção (regra 9). A fabricação começa quando o operador
    do primeiro Setor do Roteiro pega o material para trabalhar — parte do material já está no Setor,
    parte é levada pelo almoxarifado —, e é esse operador quem registra a primeira entrada, sem o
    Movimentador: não há o que levar. A primeira entrada **exige Roteiro**; nó sem Roteiro é
    pendência do PCP, que edita o Roteiro de qualquer nó (regra 7). Depois que o nó começa a andar,
    os passos já alcançados são histórico e não se editam. O Pedido passa a `EmProducao` na primeira
    entrada de qualquer nó dele.
29. **O fim do Roteiro.** O que termina um passo que não é o último vai para o próximo passo, sem
    escolha. O que termina o **último** passo aguarda coleta com um destino que depende do nó:
    - **Item** — a montagem do pai, num Setor do Roteiro do pai que o **Movimentador escolhe** ao
      entregar, com sugestão do sistema; a montagem é registrada pelo operador daquele Setor (regra
      24). Entregue no Setor errado, redireciona-se com outra entrega.
    - **Peça** — o **local de expedição**, levada pelo Movimentador como tarefa; a expedição (Fase 5)
      sai de lá.
30. **A sobra é identificada, não é tarefa, e só sai por descarte.** O que um Item tem, no fim do
    Roteiro ou entregue para a montagem, além do que o pai ainda precisa, é **sobra**: sai da lista
    de tarefas do Movimentador e aparece à parte, no Setor onde está. Só deixa de estar em produção
    por uma perda de motivo `Descarte` (regra 17), registrada pelo mesmo ator da perda — Qualidade ou
    PCP — a qualquer momento depois de virar sobra. A regra 13 não muda: o Pedido conclui com sobra
    viva, que continua listada até ser descartada.

## Pontos ainda em aberto

- **Busca de peça por foto** (comparar a foto do operador contra as silhuetas do sólido).
  Não é decisão de domínio ainda: depende de um spike medir a taxa de acerto. Ver
  `06-roadmap-mvp.md`.
- **Descontinuar uma Peça trava o fechamento do Pedido.** O comportamento padrão quando um cliente
  altera o projeto de um Pedido em execução é o descartado **parar de ser produzido**, não ser
  apagado. Mas pela regra 13 uma Peça só conclui quando **toda** a quantidade virou expedido ou
  perdido — e a quantidade que nunca entrou em produção não é nem uma coisa nem outra. A Peça nunca
  conclui, o Agrupamento nunca conclui, e o Pedido nunca fecha. Descontinuar precisa de um **bucket
  terminal** próprio ou de uma exceção explícita na regra 13. Levantado na Fase 2 e deliberadamente
  não decidido lá: tem efeito na Fase 5 (fechamento), não só na 3.
  *Nota (2026-09-24):* desde a regra 28, a quantidade que nunca entrou num Setor está **a
  iniciar**, que conta como em produção. O buraco continua o mesmo: ela nunca vira expedido nem
  perdido.
  *Nota (2026-09-24, revisão da Fase 3):* a Fase 2 havia empurrado o descarte em Pedido rodando
  para a Fase 3, e a Fase 3 não o resolveu; com a montagem de todo nó, um filho acrescentado por
  engano a um Pedido rodando, que não se apaga, trava a montagem do pai até ser fabricado. A fase
  continua a decidir.
  *Nota (2026-09-25):* o usuário decidiu a **fase**: o descarte em Pedido rodando é da **Fase 5**,
  junto da perda e do `Descarte` da sobra (regra 30) — a quantidade descartada precisa de um destino
  no livro, e `Perdido` só nasce lá. Tem de existir antes do primeiro uso real. **Como** o descarte
  fecha a regra 13 (bucket próprio ou `Perda` com motivo `Descarte`) continua a decidir, na spec da
  Fase 5.

> **Decidido em 2026-09-24** (spec da Fase 3), e por isso fora desta lista: como sai de "em
> produção" o filho de um nó que não passa pela trava de montagem — montar passou a ser registro
> de todo nó com filhos, e só a validação da trava ficou restrita ao Kit (regra 24).

Itens de infraestrutura (CI/CD, detalhes de deploy) estão em `03-arquitetura-tecnica.md`.
