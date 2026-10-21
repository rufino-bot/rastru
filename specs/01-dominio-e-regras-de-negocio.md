# 01 - Domínio e Regras de Negócio

## Glossário

| Termo | Definição |
|---|---|
| **Pedido** | Unidade máxima de trabalho, cadastrada no sistema. Tipo `Fabricacao` ou `Retrabalho`. Um Retrabalho referencia obrigatoriamente o Pedido original. |
| **Pedido.Numero** | O **código identificador do Pedido**, e o campo pelo qual as pessoas se referem a ele. **Não é gerado por este sistema**: vem de um sistema externo, que o cria sequencialmente. Aqui ele é apenas registrado. Por isso é **único global** (`UQ_Pedido_Numero`) — a sequência é controlada na origem e não se repete. É texto (`NVARCHAR(30)`), não número: aceita prefixos e separadores, e o sistema não valida formato nem gera valor. Consequência prática: se a origem emitir um código já cadastrado, o cadastro é recusado com 409 — e isso é o comportamento desejado, não um defeito. |
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo não impõe roteiro, mas **não é mais só descritivo**: um Agrupamento Kit fica sujeito ao conjunto completo (regra 25), e um Avulso, não; a trava de montagem (regra 24) vale, desde a Fase 3D, para todo nó que já tem filhos quando entra em produção. Até 2026-09-15 o Tipo era só descritivo; a reversão parcial veio de esclarecimento do processo da fábrica — ver `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`. |
| **Componente** | Registro de **catálogo** (receita padrão/template), reutilizável entre Pedidos. Não é a instância física — é a definição. |
| **Componente.Codigo** | O **identificador único da peça de catálogo** dentro deste sistema. É **alfanumérico** (`NVARCHAR(50)`) e **único global** (`UQ_Componente_Codigo`). Decisão do dono do projeto (2026-08-03): **o sistema não modela a numeração do cliente.** Nem toda peça chega com código definido pelo cliente, e o critério varia de cliente para cliente — essa regra **não é absorvida aqui**. O que vale é que toda peça de catálogo tenha um identificador único neste sistema, o que é o que permite reconhecê-la quando ela é pedida **várias vezes ao longo do ano**. Quem cadastra atribui o valor (reaproveitando o código do cliente quando existir); o sistema não gera nem valida formato. Consequência operacional a vigiar: o ganho depende de a peça repetida ser **encontrada e reutilizada**, não recadastrada sob um código novo — cadastro duplicado sob códigos diferentes não viola nenhuma constraint e passa despercebido. |
| **Componente.ArquivoSolidoId** | Referência (FK para `dbo.ArquivoDeComponente`) ao arquivo de **sólido 3D** (CAD) da peça de catálogo — **STL**, e só (decisão de 2026-09-12, do usuário: o three.js lê STL nativamente, enquanto STEP exigiria parser de terceiros no navegador; `.SLDPRT` continua fora, por ser proprietário). É obrigação de negócio para toda Peça de Pedido, mas coluna **nullable** por não valer para todo Componente; ver regra 18. `Componente.ArquivoFoto`, ao lado, é uma foto de referência **opcional**. |
| **EstruturaItem** | A árvore **real** usada em um Agrupamento específico, podendo ter sido copiada do catálogo (`Componente`) e customizada. É recursiva: um `EstruturaItem` pode ter `EstruturaItem` filhos. O nó de topo (sem pai) é chamado de **Peça**; os nós com pai são chamados de **Item**. Representa um **lote agregado** (quantidade), não uma unidade física individual — e esse lote é divisível por quantidades livres (ver regra 9). |
| **EstruturaItem.Descricao** | Nome próprio do nó dentro do Agrupamento. NULL = usa a descrição do `Componente` de origem. Serve ao item **ad-hoc** (`ComponenteId` NULL), que sem ela chega sem nome nenhum à tela do operador; ver regra 19. |
| **EstruturaItem.QuantidadePorPai** | Quantos daquele nó entram em **uma** unidade do pai, guardado ao lado da quantidade absoluta (`EstruturaItem.Quantidade`). Obrigatória em todo Item, nula na Peça. Serve à montagem de todo nó com filhos, às tarefas do Movimentador e à sobra (regras 23, 24 e 30) e, no Kit, ao conjunto completo (regra 25); o apontamento continua movimentando a quantidade absoluta. Ver regra 26. Decidida em 2026-09-15; a coluna entra no schema na Fase 3 (decisão de 2026-09-24, que trouxe a montagem da 3B para a 3). |
| **Material** | Produto de estoque (chapas, parafusos, roelas, etc.) consumido para fabricar um `EstruturaItem`. Qualquer nó, folha ou com filhos, pode ter Materiais (regra 4). |
| **Material.SetorDeEstoqueId ("Fica em")** | Onde o Material fica estocado: nulo é o **almoxarifado** (o padrão), e um Setor é o lugar onde ele fica guardado ali mesmo (a chapa no Corte, que o operador pega no próprio Setor). Só o material do almoxarifado entra na lista do Separador e tem pendência (regra 34). Decidido em 2026-10-16 (D6 da spec da Fase 4, `docs/superpowers/specs/2026-10-16-fase-4-separacao-de-materiais-design.md`); a coluna entrou no schema na Fase 4. |
| **Separação** | Registro de que o Separador tirou uma quantidade de um Material para um `EstruturaItem`. Vive num livro só de inclusão (`dbo.MaterialSeparacao`), onde a correção é um estorno; o **separado** de um material num nó é sempre calculado (regra 34). Separar não condiciona o início do nó (regra 5). |
| **Separador** | Perfil de quem separa o material do almoxarifado e o leva, ou busca, até o Setor do primeiro passo do nó, registrando o que levou. É o perfil que se chamava Almoxarifado, renomeado na Fase 4 (D5 da spec da Fase 4). |
| **Material.Codigo** | O **identificador único do material** dentro deste sistema. **Mesma regra do `Componente.Codigo`**, por decisão explícita: alfanumérico (`NVARCHAR(50)`), **único global** (`UQ_Material_Codigo`), atribuído por quem cadastra, sem geração nem validação de formato pelo sistema. A numeração de fornecedor **não** é modelada aqui. |
| **Setor** | Departamento de produção (ex.: Corte e Dobra, Usinagem) pelo qual um `EstruturaItem` pode passar. |
| **Setor.UtilizaKit** | Marca, no cadastro do Setor, de que ali se montam os nós de Agrupamento Kit a partir dos filhos — hoje, a Solda; a regra não depende do nome do Setor. Em Agrupamento Kit, é o que ativa o conjunto completo (regra 25); a trava de montagem deixou de depender dele na Fase 3D (regra 24). Decidida em 2026-09-15; a coluna entrou no schema na Fase 3B, com default 0: um Setor só ganha a marca quando o Administrador a liga no cadastro. |
| **Roteiro** | Sequência de Setores que um `EstruturaItem` percorre. Pode ser padrão (catálogo) ou específico daquele Pedido/Agrupamento. |
| **Aguardando coleta** | Estado da quantidade que ainda não foi levada ao próximo destino depois de o operador dá-la como terminada num Setor (regra 22) — ou, no Pedido de Retrabalho, depois de nascer marcada **pronta** (regra 27). Conta como **em produção** para a conservação de quantidade (regra 9) e é o que alimenta as tarefas do Movimentador (regra 23). No banco, é uma posição do livro de movimentações (`dbo.Movimentacao`), guardada no Setor e no passo em que a quantidade terminou; o destino é calculado (regra 29). |
| **Relatório Dimensional** | A conferência de conformidade dimensional de uma Peça, **opcional** (o cliente exige em Peças específicas — ex.: primeira manufatura ou primeiro trabalho após reprovação no cliente; marcado em `EstruturaItem.RequerRelatorioDimensional`). É feito **pela fábrica**, pela Qualidade, no local de expedição, e se registra como **aferição** (regra 10) e, depois de uma aferição não conforme, como **contagem** unidade a unidade (regra 11). As cotas vêm do cliente com o pedido e ficam **fora** do Rastru, que guarda só o resultado, quem e quando. Reprovação não gera retrabalho (regra 12). Reescrito em 2026-10-20: até então o termo descrevia a avaliação por remessa do **cliente**, que não é o que a fábrica faz. |
| **Aferição** | O registro de que a Qualidade mediu **uma** unidade de uma Peça que exige relatório, e que ela foi `Conforme` ou `NaoConforme`: esse resultado vale para o lote inteiro (regra 10). Registro só de inclusão (`dbo.RelatorioDimensional`), corrigido por estorno; a **válida** é a última não estornada. Não move quantidade. |
| **Conserto** | Destino da unidade não conforme que volta a um Setor para ser corrigida, dentro do **mesmo Pedido** (não é Retrabalho): a Qualidade a manda, o Movimentador a leva, o operador a corrige e o Movimentador a traz de volta ao local de expedição, onde é medida de novo (regra 35). Conta como em produção. |
| **Liberação** | A decisão da Gestão de que unidades não conformes **podem ser expedidas assim mesmo** — quando a medida fora não atrapalha o uso no cliente. A Qualidade **pede**, com justificativa, e a Gestão **responde**, com a quantidade liberada (de zero até a pedida) e o nome dela gravado (regra 36). |
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Separador, Movimentador, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. O Separador é o antigo Almoxarifado, renomeado na Fase 4 (D5 da spec da Fase 4). |
| **Movimentador** | Perfil de quem leva ao próximo destino a quantidade que aguarda coleta e registra a entrada nele (regra 22); em Agrupamento Kit, os filhos vão ao Setor com `UtilizaKit` em conjuntos completos (regra 25). O que ele tem a levar é a lista de tarefas da regra 23. Decidido em 2026-09-15; passa a existir no sistema na Fase 3. |
| **Remessa (expedição)** | Saída de quantidades de uma ou mais Peças para o cliente, registrada de uma vez por quem expede (a Qualidade): `dbo.Remessa` guarda **quando** e **quem**, e as quantidades são as linhas `Expedicao` do livro de movimentações que a apontam (`Movimentacao.RemessaId`), uma por posição de origem. Pode ser **parcial**: o cliente aceita uma parte vital antes e o restante depois. Uma remessa leva Peças de um ou mais Pedidos, e se estorna inteira (regra 16). Decidida em 2026-10-20 (C8 e C9 da spec da Fase 5A). |
| **Perda** | Baixa de quantidade de um `EstruturaItem` (Peça ou Item) que sai da produção: some no armazém, morre após um processo que deu errado, ou é **descarte** de sobra que nunca foi usada (regras 25 e 30). Vai para um bucket terminal; a reposição, quando há, é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda') — descarte não é reposto. O motivo `Descarte` entra no schema no início da Fase 5 (regra 17). |
| **Montado** | Destino terminal da quantidade de um Item que virou parte do pai: ao iniciar N do pai, baixa-se `N × QuantidadePorPai` de cada filho direto para "montado" (regra 24). É um dos quatro termos da conservação de quantidade (regra 9). O registro de montagem existe para todo nó com filhos, de Kit ou Avulso, e nasce do Iniciar do pai (regra 24). O destino entra no schema na Fase 3. Não confundir com o **total montado** do pai, que conta quantas unidades do pai já foram montadas e limita a saída dele do Setor, para todo nó com filhos (regra 24; sem exceção desde a Fase 3B, que recusa acrescentar filho a nó já iniciado). |
| **A iniciar** | Estado da quantidade de um nó que ainda não entrou em nenhum Setor. Todo nó nasce assim, com a quantidade inteira. Conta como **em produção** (regra 9). Sai daqui pela primeira entrada, registrada pelo operador do primeiro Setor do Roteiro quando ele pega o material para trabalhar (regra 28). |
| **Local de expedição** | O lugar da fábrica para onde o Movimentador leva a Peça que terminou o Roteiro, e de onde as cargas são expedidas (regra 29). Não é um Setor: não fabrica, não entra em Roteiro nem nos KPIs de tempo por Setor. Conta como **em produção** até a expedição. Desde a Fase 5A a quantidade ali se reparte em cinco posições do livro — `NaExpedicao` (o significado depende da aferição da Peça, regra 37), `Conforme`, `NaoConforme`, `AguardandoLiberacao` e `Liberada` —, e o que sai numa remessa vai a `Expedido` (regras 9 e 16). |
| **Sobra** | Quantidade de um Item que terminou o Roteiro, ou foi entregue para a montagem, além do que o pai ainda precisa — e o que o pai precisa é `(quantidade do pai − total montado) × QuantidadePorPai`. É identificada pelo estado e não vira tarefa de ninguém; se não for usada, sai como perda de motivo `Descarte` (regras 17 e 30). Não é perda até o `Descarte` ser registrado. |

## Regras de negócio

1. Um Pedido é composto por N Agrupamentos.
2. Um Agrupamento é composto por N `EstruturaItem` de topo (Peças).
3. Uma Peça pode ser composta por N Itens; um Item pode, por sua vez, ser composto por
   outros Itens — recursão sem limite de profundidade, resolvida numa única tabela
   autorreferenciada (`EstruturaItem`), evitando o ciclo conceitual entre "Peça" e "Item".
4. Qualquer `EstruturaItem`, folha ou com filhos, pode ter Materiais (o planejado,
   `EstruturaMaterial`). O material de um nó é usado no Setor do **primeiro passo** do Roteiro
   dele: numa folha, onde ela é fabricada; num nó com filhos, onde ele inicia consumindo os filhos
   (regra 24). *Reescrita em 2026-10-16 (spec da Fase 4, D2):* até então a regra dizia que só a
   folha tinha Materiais, e a receita padrão já aceitava material num Componente com filhos — o
   texto estava atrás do código. A montagem final, por exemplo, acontece na Expedição, no início de
   uma Peça cujo Roteiro tem um passo só, e o material dela é separado para esse passo.
5. O material que fica no almoxarifado é **separado** pelo Separador e levado, ou buscado, até o
   Setor do primeiro passo do nó. O que fica num Setor, o operador pega ali (`Material.SetorDeEstoqueId`).
   **Separar nunca condiciona o início**: a regra 28 não muda, e a falta de material é informação,
   não bloqueio — o resumo aparece na Fila do Setor e no painel do nó, e o início não pede
   confirmação (D12 da spec da Fase 4). *Reescrita em 2026-10-16:* a redação anterior falava em
   "retirados do estoque" e "entregues ao setor responsável antes da fabricação", e sugeria que a
   separação era um pré-requisito.
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

   **Fechada na Fase 5A** (spec da Fase 5A, seção 3.1): o "no local de expedição" é a soma das cinco
   posições `NaExpedicao`, `Conforme`, `NaoConforme`, `AguardandoLiberacao` e `Liberada`; a unidade em
   **conserto** (`AguardandoConserto`, `EmConserto`, `ConsertoPronto`) conta como em produção; e o "expedido"
   é a posição terminal `Expedido` do livro, que as linhas `Expedicao` de uma remessa alimentam. O "perdido"
   (`Perda`) é da Fase 5B.
10. O Relatório Dimensional é **opcional**: só quando o cliente exige, em Peças específicas
    (ex.: primeira manufatura, primeiro trabalho após reprovação no cliente). Isso é sabido
    no cadastro do Pedido e marcado **por Peça** em `EstruturaItem.RequerRelatorioDimensional`.
    Quando exige, a **aferição** é da fábrica: feita pela **Qualidade**, que no Rastru é a pessoa da
    expedição, no local de expedição, **uma por Peça** — mede-se **uma** unidade, e o resultado
    (`Conforme` ou `NaoConforme`) decide o lote inteiro (D3). **Conforme** libera a Peça toda, inclusive
    as remessas futuras, sem nova medição; **não conforme** obriga a medir todas as unidades, as presentes
    e as que chegarem depois, uma a uma (regra 11). O Rastru **não guarda as cotas nem as medidas** (D2):
    só o resultado, quem e quando.
    - **Registro só de inclusão** (`dbo.RelatorioDimensional`): uma linha por aferição, e a correção é um
      **estorno**, no máximo um por linha, de quem a registrou, do PCP ou do Administrador (C7). A
      aferição **válida** da Peça é a última não estornada, e só se registra uma nova quando não há
      válida (409 `AfericaoJaRegistrada`: estorne a anterior primeiro). A aferição não move quantidade (C2).
    - **Só a Peça com a marca recebe aferição** (409 `SemRelatorioDimensional`). O PCP **edita a marca** de
      uma Peça (`PUT /estrutura/{id}`) enquanto ela não tem aferição válida (C14; com aferição válida, 409
      `ComAfericao`: estorne a aferição antes). A marca só existe na Peça (400 `RelatorioSoNaPeca`).
    - **Estornar** uma aferição **não conforme** é recusado enquanto a Peça tem contagem válida (409
      `EstornoImpossivel`: as contagens dependem dela); estornar uma **conforme** é livre, porque nada
      no livro depende dela.

    **Reescrita em 2026-10-20** (spec da Fase 5A, `docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md`, D1 a D4, C2 e C14).
    A redação anterior descrevia "um relatório por Peça, acumulativo", com uma
    `RelatorioDimensionalAvaliacao` por remessa **avaliada pelo cliente**: era a avaliação do cliente, não
    o que a fábrica faz, e as duas tabelas saíram do schema sem nunca terem sido escritas por código.
11. A **medição uma a uma**, depois de uma aferição não conforme (D4), é **por quantidade**: a Qualidade
    registra uma **contagem** — "medi 6: 4 conformes e 2 não conformes" — sobre as unidades que estão
    em `NaExpedicao` (a medir), e cada resultado vai para a posição `Conforme` ou `NaoConforme` do local
    de expedição. A contagem é **tudo ou nada** e vai até a quantidade que está em `NaExpedicao` (409
    `SaldoInsuficiente` acima disso); vazia é 400 `ContagemVazia`; sem aferição válida **não conforme**, 409
    `SemAfericaoNaoConforme`. No livro é **uma** linha `Medicao` por resultado diferente de zero (até duas) ligadas por uma
    `dbo.ContagemDimensional`, e a contagem se estorna **inteira**, nunca meia (C17), enquanto o que ela
    mediu não tiver andado. A contagem não diz **quais** unidades — é lote agregado, sem serial. Um
    Agrupamento pode ter Peças com resultados diferentes ao mesmo tempo. *Reescrita em 2026-10-20 (D4, C17):*
    antes, aprovação e reprovação por quantidade eram o resultado da avaliação por remessa.
12. A reprovação da aferição **não** abre Retrabalho. A unidade não conforme tem três destinos: **consertar**
    (regra 35), **expedir assim mesmo, com a liberação da Gestão** (regra 36) ou, na Fase 5B, **"matar a
    peça"** (vira perda e é refeita do começo). O Pedido do tipo `Retrabalho` continua sendo uma ação
    **separada e opcional**, aberta **para aquela quantidade** e vinculada ao Pedido original
    (`PedidoOrigemId`), com um `MotivoRetrabalho` categorizado: `ReprovacaoDimensional`, `ErroInterno`,
    `SolicitacaoCliente` ou `Perda`. **Nota (2026-10-20, Fase 5A):** o vínculo que a regra dava ao
    Retrabalho — a avaliação que o originou (`RelatorioDimensionalAvaliacao.PedidoRetrabalhoId`) — **deixou
    de existir**, porque a avaliação por remessa deixou de existir (regra 10). Como o Retrabalho se liga à
    reclamação do **cliente** (`SolicitacaoCliente`, depois de ele receber) é redesenhado na Fase 5B.
13. Uma Peça está **concluída** quando nada dela está mais em produção — toda a quantidade
    virou expedido (a posição `Expedido` do livro) **ou** perdido (`Perda`, da Fase 5B). Um Agrupamento
    conclui quando todas as suas Peças concluem (`Agrupamento.DataConclusao`), e um Pedido conclui quando o
    **último** Agrupamento conclui (`Pedido.DataConclusao`). Não depende mais de "avaliado",
    já que o Relatório Dimensional é opcional.

    **Quem a executa (Fase 5A, C11 a C13, C15 e C16).** O fechamento é **recalculado do livro, do zero**, na
    mesma transação de todo movimento que toca o fim do caminho — a remessa, o estorno dela, a entrega que
    chega ao local de expedição, o conserto, a contagem... — e de criar, editar (a quantidade da Peça) ou
    importar uma Peça num Pedido rodando; acrescentar filho não o recalcula, porque não muda a quantidade de
    nenhuma Peça nem o que ela expediu. Como só o expedido conta (o perdido é da 5B), a Peça conclui com **toda** a quantidade em
    `Expedido`. A data de conclusão do Agrupamento e do Pedido é a da escrita que os concluiu.
    - **Status do Pedido** (C11): `AguardandoExpedicao` quando toda a quantidade ainda **não expedida** das
      Peças do Pedido está no local de expedição (qualquer das cinco posições); `EmProducao` quando alguma
      ainda está noutro lugar, **inclusive em conserto** — mandar para conserto tira a unidade do local de
      expedição e devolve o Pedido a `EmProducao`. `Aberto` só vale antes do primeiro início, e o status
      não volta a ele; `Cancelado` fica fora de tudo isto.
    - **Agrupamento sem Peças não conclui e não segura o Pedido** (C16): o fechamento olha só os
      Agrupamentos que têm Peças, e um Pedido sem Peça nenhuma não conclui.
    - **Estornar a remessa reabre** o que ela concluiu (C13): Peça, Agrupamento e Pedido voltam a abertos,
      as datas de conclusão são limpas e o status é recalculado. Entre as escritas da **expedição**, é a
      **única** que um Pedido `Concluido` aceita; no `Cancelado`, a remessa não se estorna. Toda outra escrita da
      expedição num Pedido `Concluido` ou `Cancelado` é recusada (409 `PedidoFechado`). A regra vale para a
      expedição e para a estrutura (C15, abaixo), não para o Pedido inteiro: editar o Pedido, a separação de
      material, os materiais e o Roteiro do nó não têm guarda de Pedido concluído.
    - **Pedido `Concluido` recusa edição de estrutura** (C15): criar Peça, acrescentar filho, editar nó e
      confirmar o import voltam 409 `PedidoFechado`. Em Pedido rodando (`EmProducao` ou
      `AguardandoExpedicao`) a edição continua livre, como era (decisão de 2026-08-29), e **recalcula** status
      e fechamento: acrescentar Peça a um Pedido aguardando expedição o devolve a `EmProducao`; baixar a
      quantidade de uma Peça até o que já foi expedido a conclui (a quantidade não desce abaixo do que já saiu
      de "a iniciar", como sempre). Quem precisa mudar um Pedido concluído estorna a última remessa.
14. O início real de produção de um Pedido é derivado (não armazenado) como a menor data entre
    as primeiras entradas (regra 28) dos nós daquele Pedido, no livro de movimentações — usado
    para separar tempo em fila de tempo de produção. A primeira entrada é o início do trabalho,
    porque o operador a registra ao pegar o material; as entradas seguintes, registradas pelo
    Movimentador ao entregar, são **chegada** no Setor (a peça pode esperar antes de começar).
    Separar fila de execução dentro do Setor é decisão da Fase 6. **Alterada em 2026-09-24**
    (spec da Fase 3): antes, o início vinha de `EstruturaSetorHistorico.DataEntrada`, tabela que
    o livro de movimentações substituiu.
15. Cada Usuário tem um Perfil (Operador, Separador, Movimentador, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa. O **Movimentador** entrou pela
    decisão de 2026-09-15 (regra 22) e passa a existir no sistema na Fase 3. O **Separador** é o
    Almoxarifado renomeado na Fase 4 (D5 da spec da Fase 4): o nome antigo existia no seed e não
    autorizava nenhuma rota.
16. A **expedição pode ser parcial** (remessas): o cliente aceita uma parte vital antes e o
    restante segue depois. A **remessa** (`dbo.Remessa`, C8) é o agrupador: guarda **quando** e **quem**
    expediu (o usuário logado, D9 — não há nome digitado nem número de nota fiscal, que é do ERP), e as
    quantidades são as linhas `Expedicao` do livro que a apontam, de uma posição de origem para `Expedido`.
    Uma remessa leva **uma ou mais Peças**, de um ou mais Pedidos, **tudo ou nada** (C9: o lote, no molde da
    Separação), e se estorna **inteira** (um estorno por linha, numa transação).
    - **De onde tira (C10).** A remessa escolhe sozinha, nesta ordem: `Liberada`, `Conforme`,
      `NaExpedicao` e, por último, `NaoConforme`. A Qualidade só diz a quantidade. A unidade
      `AguardandoLiberacao` **não sai**: espera a resposta da Gestão (ou o pedido ser estornado). Tirar da não
      conforme por último é o que faz o aviso da regra 37 valer. Acima do expedível (tudo do local de
      expedição menos o que aguarda liberação), 409 `SaldoInsuficiente`, com a frase nomeando o que espera
      a Gestão quando há.
    - **A soma das remessas de uma Peça nunca excede a quantidade dela, por construção:** o livro só expede o
      que está no local de expedição. *Reescrita em 2026-10-20 (spec da Fase 5A):* antes, "validado na
      aplicação", com uma linha em `Expedicao` por Peça.
    - **Só a Peça** chega ao local de expedição (regra 29): o Item numa remessa é 400 `NaoEhPeca`. A **pausa**
      do Pedido (regra 31) não recusa a remessa nem o resto da expedição: ela recusa só o início.
    - **Estorno da remessa.** Recusado se ela já foi estornada (409 `JaEstornado`) e, num Pedido `Cancelado`,
      409 `PedidoFechado`. `Expedido` é terminal, então a quantidade expedida nunca "andou" depois, e nada mais
      o impede. Quem estorna: o autor, o PCP ou o Administrador (C7).
17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem`, `MortaEmProcesso` ou
    `Descarte`), levando a quantidade ao bucket terminal "perdido". Pode ser registrada em
    **qualquer** `EstruturaItem`, Peça ou Item. Para repor, abre-se um Pedido de Retrabalho
    separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça original. Como na reprovação,
    registrar a perda **não** abre retrabalho automaticamente. `Descarte` é a sobra que nunca foi
    usada (regras 25 e 30) e não se repõe. Quando a perda de uma parte impede montar o pai, vale a
    regra 27.

    **Alterada pela decisão de 2026-09-15:** entraram o motivo `Descarte` e a perda de Item. No
    schema, o `Descarte` só entra no início da Fase 5 (a **5B**, desde que a Fase 5 foi dividida em 5A, 5B e
    5C em 2026-10-20) — até lá, a `CK_Perda_Motivo` de
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

    **No import da estrutura a partir do BOM do CAD (regra 32), a exigência vale para a árvore
    inteira, exceto `Bruto`** — e a raiz, que é a Peça, continua exigindo sólido qualquer que seja o
    tipo dela.

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

*A Fase 3B (spec `docs/superpowers/specs/2026-10-09-fase-3b-kit-e-montagem-design.md`) emendou as regras
23, 24 e 25.*

22. **Terminar e mover são ações separadas** — na fábrica, feitas por pessoas diferentes. O
    operador registra que terminou o trabalho num Setor, e aquela quantidade passa a **aguardar
    coleta**; o **Movimentador** a leva e registra a entrada no próximo destino. Vale para todo
    `EstruturaItem`, de Agrupamento Kit ou Avulso. Um filho que concluiu o próprio Roteiro aguarda
    coleta para a montagem do pai. Para a conservação de quantidade (regra 9), aguardar coleta
    conta como em produção; no banco, ele é uma posição do livro de movimentações (spec da Fase 3).
23. **As tarefas do Movimentador são calculadas a partir do estado**, não gravadas como aviso:
    quando alguém leva, a tarefa some sozinha. São duas:
    - **Item pronto** — quantidade aguardando coleta, menos a sobra (regra 30). É tarefa, exceto
      para o filho de Agrupamento Kit no último passo do Roteiro cujo pai começa num Setor com
      `UtilizaKit`: esse filho não vai sozinho (regra 25), e por isso **não** aparece como Item
      pronto — aparece no cartão do Kit do pai, enquanto o pai ainda precisa receber conjuntos.
    - **Kit pronto para montagem** — tarefa que aparece quando os filhos diretos de um nó,
      aguardando coleta no último passo do Roteiro deles, formam ao menos um conjunto completo
      **que o nó ainda precisa receber** (regra 25). O número de conjuntos é o mínimo, entre os
      filhos diretos, de ⌊quantidade aguardando coleta ÷ `QuantidadePorPai`⌋, sem passar desse
      mesmo teto, o da entrada na regra 25. Sem o teto, a divisão contaria a sobra de refugo que a
      regra 26 admite: se um filho de 45 com razão 4 sob um pai de 10 fosse o único filho direto,
      daria 11 conjuntos.

    Na tela de Tarefas, desde a Fase 3B, o Kit pronto é um cartão por pai na seção **"Kits
    montáveis"**, em que o Movimentador escolhe **quantos conjuntos** leva, e não quanto de cada
    filho: a tela não consegue compor um conjunto incompleto. O Kit que o nó ainda precisa receber,
    com algum filho pronto, mas que ainda não fecha um conjunto aparece em **"Kits incompletos"**,
    uma seção recolhida e só informativa, que não conta como tarefa. Com o teto da regra 25 em
    zero, o Kit não aparece em nenhuma das duas, e os filhos prontos dele não aparecem em lugar
    nenhum das Tarefas.

    Notificação no celular é reforço desta lista, não substituto (Fase 3C).
24. **Montagem e trava de montagem.** A montagem é registro de **todo** nó com filhos, de Agrupamento
    Kit ou Avulso — até 2026-09-24, só existia sob a trava (spec da Fase 3) —, mas **não é mais uma
    ação própria**: desde a Fase 3D ela nasce do **Iniciar** do nó (spec
    `docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`, seção 2.1). Olha
    só os **filhos diretos** do nó. O registro, os dois tetos e a baixa dos filhos valem para todo nó;
    o que depende de Kit é só o **conjunto completo** na entrada (regra 25), e a **saída limitada ao
    total montado** vale, por construção, para todo nó com filhos, não por uma condição de
    Agrupamento ou de Setor — desde a Fase 3B sem exceção, porque acrescentar filho a nó já
    iniciado é recusado (item sobre a saída do nó).
    - **Montar não é ação.** O operador registra **iniciar N** do nó com filhos, no **primeiro
      passo** do Roteiro dele, no Setor onde os filhos estão — e é esse início que grava
      a montagem: consome `N × QuantidadePorPai` de cada filho direto presente e põe N do pai em
      produção naquele passo, na mesma transação. Iniciar um nó com filhos **exige** os filhos: o
      sistema aceita se N não passar do mínimo, entre os filhos diretos, de
      ⌊quantidade do filho no Setor ÷ `QuantidadePorPai`⌋, **nem do que ainda falta iniciar do nó**
      — a quantidade dele menos o que já saiu de "a iniciar". O limite do que ainda falta iniciar
      não repete o teto de entrada da regra 25, e vale também onde aquele teto vale: o da regra 25
      só existe para Kit num Setor com `UtilizaKit` e confere só a entrega, e há o que escapa dele
      — por exemplo, o que já aguardava montagem antes de o Agrupamento virar Kit ou de o Setor
      ganhar a marca (D4 da spec da Fase 3B), ou a quantidade do nó reduzida depois de os
      conjuntos entrarem. O filho cujo último passo é no próprio
      Setor onde o pai começa não é exceção: termina, aguarda coleta e é entregue ali mesmo, como
      os outros (D2 da mesma spec). A montagem pode ser **parcial** (iniciar 6 de 10), o que casa
      com a expedição parcial (regra 16).
    - Ao iniciar, baixa-se `N × QuantidadePorPai` de cada filho direto para o destino terminal
      **"montado"**, **gravando a baixa de cada filho**, e não só N — editar a razão depois não
      reescreve o passado. N soma ao **total montado** do nó. O movimento de início do pai aponta a
      montagem, e por isso a correção é **uma só**: estornar a montagem desfaz as baixas e o
      início do pai juntos (enquanto o pai ainda estiver onde o início o pôs); o início do pai não
      se estorna sozinho.
    - A **saída** do nó é limitada ao total montado, em **qualquer** passagem: se o nó volta ao
      Setor (regra 21), os filhos já viraram o nó, e é o total montado que conta. Desde a Fase 3D
      isso **vale, sem validação própria, para todo nó que já tem filhos quando entra em
      produção**, porque o pai só entra em produção consumindo os filhos: tudo o que ele termina,
      entrega ou leva à expedição já foi montado (o que saiu de "a iniciar" é igual ao total
      montado). Desde a Fase 3B, vale **sem exceção**: acrescentar filho a um nó que já saiu
      de "a iniciar", líquido de estorno, é recusado (`PaiJaIniciado`, D1 da spec da Fase 3B). Até
      ali era livre (spec da Fase 3, seção 4.7), e o nó saía de "a iniciar" sem consumir o filho
      novo: a igualdade entre o que saiu de "a iniciar" e o total montado não valia para ele, e a
      saída dele podia passar do total montado. Se todo início do nó for estornado, ele volta a
      aceitar filho. A guarda é a resposta da 3B; o tratamento de estrutura alterada no meio da
      produção fica para a Fase 5.
    - A ordem de baixo para cima é consequência, não cálculo: um nó intermediário só existe no
      Setor depois de montado, então o pai dele só monta depois.
25. **Conjunto completo.** Um Kit pode ir à Solda em parte do pai (os conjuntos de 7 de 10), mas
    **nunca incompleto nem além do necessário**: a entrada de filhos de Agrupamento Kit num Setor
    com `UtilizaKit`, para a montagem do pai, só é aceita em conjuntos completos —
    `N × QuantidadePorPai` de **todos** os filhos diretos, juntos, na mesma movimentação —, e com
    N **sem passar do que o nó ainda
    precisa receber**: a quantidade dele, menos o total já montado e menos os conjuntos que já
    estão à espera de montagem em **qualquer** Setor com `UtilizaKit`. Esses conjuntos à espera são
    os que **entraram e ainda não foram montados**, não o mínimo por filho: uma unidade que perdeu
    parte dentro da Solda não se completa com refugo novo. Desde a Fase 3B eles são contados pelo
    **filho mais adiantado**: o maior, entre os filhos diretos, de
    ⌈quantidade aguardando montagem ÷ `QuantidadePorPai`⌉, somando só os Setores com `UtilizaKit`
    (D5 da spec da Fase 3B). Sem perda, e sem nenhum caminho por fora da entrada em conjunto
    completo e do início (o bullet «Limitação conhecida» desta regra dá exemplos deles), isso é o
    número de conjuntos que entraram e ainda não foram montados por inteiro; o arredondamento para cima
    mantém contado o conjunto que perdeu parte. (Como a perda do próprio nó entra nesta conta é decisão da Fase 5.) O motivo é
    físico: peça solta ou a mais na Solda ocupa espaço, e, se houver perda antes de o resto chegar,
    aquele espaço fica sem destino. Não há exceção para completar conjunto que perdeu parte dentro
    da Solda — isso é perda (regra 27). A **sobra** — tudo o que passa do que o nó precisa, feche
    conjunto ou não (ex.: refugo além do necessário) — nunca entra na Solda e, se não for usada, sai como perda de motivo `Descarte` (regra 17).
    - **Onde vale (D3 da spec da Fase 3B).** O nó é de Agrupamento Kit, tem filhos e o **primeiro
      passo** do Roteiro dele é num Setor com `UtilizaKit`; o conjunto completo vale para a entrega
      que leva os filhos a **aguardar montagem** nesse Setor — a que vem da coleta e também o
      redirecionamento, em que a quantidade que sai da espera de um Setor com `UtilizaKit` já estava
      contada e não ocupa o teto duas vezes. **Não** vale para a folha que passa pela Solda no
      próprio Roteiro, nem para o nó com filhos que volta a ela para seguir o próprio Roteiro: eles
      entram no Setor para ser trabalhados, não para a montagem do pai, e não contam como à espera. A Peça de Kit não tem pai e segue para o local de expedição como as
      outras.
    - **Limitação conhecida (D4 e D5 da spec da Fase 3B).** A espera pode ficar **desalinhada**
      entre os filhos — um pai de quantidade 2, com 7 aguardando montagem de um filho de razão 4 e
      1 de um filho de razão 1. Contada pelo filho mais adiantado (⌈7 ÷ 4⌉ = 2), a espera leva o
      teto a zero: o Kit some das Tarefas, a entrega para esse pai é recusada, e o teto continua
      zero mesmo depois de iniciar o pai. A entrada em conjunto completo e o início, que tira de
      cada filho `N × QuantidadePorPai`, mantêm sozinhos a espera na proporção das razões; caminhos
      por fora deles podem desalinhá-la. Alguns exemplos, sem pretender esgotar a lista: trocar o
      Tipo do Agrupamento de Avulso para Kit, ou marcar `UtilizaKit` num Setor, quando já há Kit em
      produção; estornar uma linha só de uma entrega de Kit, porque o estorno é feito linha a linha
      e não confere o conjunto; editar a `QuantidadePorPai` de um filho cujo pai tem conjuntos à
      espera; e acrescentar filho a um pai de Kit que ainda não iniciou, mas já tem conjuntos à
      espera — a guarda `PaiJaIniciado` (D1 da spec da Fase 3B) recusa o pai que já saiu de "a
      iniciar", e esse não saiu; o filho novo nasce sem nada à espera. É o mesmo mecanismo da perda
      dentro da Solda (uma espera que deixou de ser feita de conjuntos inteiros), e vai com ela
      para a Fase 5.
26. **`EstruturaItem.QuantidadePorPai`** guarda quantos daquele nó entram em **uma** unidade do
    pai, **ao lado** da quantidade absoluta (`EstruturaItem.Quantidade`). É **obrigatória em todo
    Item e nula na Peça**. A cópia da receita a preenche com
    `ComponenteFilhoPadrao.QuantidadePadrao`; num Item ad-hoc, quem cadastra informa. A quantidade
    absoluta continua sendo o que o apontamento movimenta; a razão serve à montagem de todo nó
    (regra 24), às tarefas e à sobra (regra 30) e, no Kit, ao conjunto completo.
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
    parte é levada pelo Separador (regra 5) —, e é esse operador quem registra a primeira entrada, sem o
    Movimentador: não há o que levar. Para um nó **com filhos**, iniciar é o **consumo dos filhos**
    (regra 24): o pai só entra em produção consumindo `N × QuantidadePorPai` de cada filho direto
    presente no Setor, e esse é o único jeito de ele começar (spec da Fase 3D, seção 2.1). A
    primeira entrada **exige Roteiro**; nó sem Roteiro é pendência do PCP, que edita o Roteiro de
    qualquer nó (regra 7). Depois que o nó começa a andar,
    os passos já alcançados são histórico e não se editam. O Pedido passa a `EmProducao` na primeira
    entrada de qualquer nó dele.
29. **O fim do Roteiro.** O que termina um passo que não é o último vai para o próximo passo, sem
    escolha. O que termina o **último** passo aguarda coleta com um destino que depende do nó:
    - **Item** — a montagem do pai, **no primeiro passo do Roteiro dele**, levada pelo Movimentador
      **sem escolha**: é lá que o pai começa, consumindo os filhos (regra 24). Entregue antes de o
      PCP mudar o primeiro passo, redireciona-se com outra entrega, que leva ao primeiro passo de
      agora (spec da Fase 3D, seção 2.2 — revê a decisão 2.4 da Fase 3).
    - **Peça** — o **local de expedição**, levada pelo Movimentador como tarefa; a expedição (Fase 5A,
      regra 16) sai de lá.
30. **A sobra é identificada, não é tarefa, e só sai por descarte.** O que um Item tem, no fim do
    Roteiro ou entregue para a montagem, além do que o pai ainda precisa, é **sobra**: sai da lista
    de tarefas do Movimentador e aparece à parte, no Setor onde está. Só deixa de estar em produção
    por uma perda de motivo `Descarte` (regra 17), registrada pelo mesmo ator da perda — Qualidade ou
    PCP — a qualquer momento depois de virar sobra. A regra 13 não muda: o Pedido conclui com sobra
    viva, que continua listada até ser descartada.

*A regra 31 foi decidida em 2026-09-28, na spec da Fase 3D
(`docs/superpowers/specs/2026-09-28-fase-3d-ajustes-pos-verificacao-design.md`, seção 2.5).*

31. **Um Pedido pode ser pausado.** PCP ou Gestão pausam um Pedido quando outro mais urgente precisa
    passar na frente, com um motivo opcional. A pausa recusa **só o início** de qualquer nó do
    Pedido — inclusive o de um pai, que consome os filhos (regra 24) —; terminar, entregar e
    estornar continuam, porque registram o que já aconteceu no chão. Cada pausa é um intervalo
    guardado (quem pausou e retomou, e quando), para o tempo por Pedido poder descontá-la (Fase 6).
    A pausa não muda o status do Pedido.

*A regra 32 foi decidida em 2026-10-02, na spec do import da estrutura
(`docs/superpowers/specs/2026-10-02-import-de-estrutura-do-bom-design.md`, decisões D3, D5 e D6, e a seção 13
dela para o que a implementação fixou).*

32. **Importar a estrutura de um BOM é propor, conferir e confirmar — e a confirmação aprova a receita.** O
    BOM exportado do CAD vira um **rascunho** (nada é gravado no catálogo nem na estrutura até a
    confirmação), que um humano confere. Três regras de domínio saem disso:
    - **Sólido em toda a árvore (D3).** Para confirmar, **todo Componente não-`Bruto` da árvore final**
      tem de ter sólido — o do catálogo ou um enviado na conferência, que só substitui o do catálogo na
      confirmação. Estende a regra 18, que cobra a Peça, aos Itens **no fluxo de import**; fora dele, a
      regra 18 não muda. A **raiz** (a Peça) exige sólido mesmo sendo `Bruto`. Entra na conta o que chega
      pela receita de catálogo mantida (a escolha por Componente, D5), não só o que veio no arquivo.
    - **Escolha de receita por Componente (D5).** Um código **diverge** quando está casado com um
      Componente do catálogo e a receita de catálogo dele (filhos diretos e quantidades) é diferente da lida
      do BOM — inclusive quando o BOM não traz filhos e o catálogo traz. Quem confere escolhe, **por Componente e sem
      padrão pré-marcado**, uma de duas receitas **inteiras**: a do catálogo ou a importada — nunca uma
      mistura linha a linha, que não existiria nem no catálogo nem no CAD. A escolha vale para **todas as
      ocorrências** do Componente na árvore e decide **só os filhos diretos** dele: com "catálogo", os
      filhos presentes só no BOM saem (com o que está abaixo deles) e os presentes só no catálogo entram,
      expandidos pela receita **efetiva** deles: a do catálogo ou, quando o filho é também um registro do
      rascunho, a efetiva desse registro (a lida, salvo a escolha "catálogo" dele). Sem escolha, a divergência
      **bloqueia** a confirmação. Componente novo, casado **sem receita no catálogo** (mesmo que o BOM traga
      filhos para ele) ou casado com receita igual **não diverge** e recebe a do BOM sem escolha. A escolha
      numa receita que deixou de divergir é inerte.
    - **Quem confirma aprova a receita (D6).** "Usar a importada" **substitui** a receita padrão do
      Componente no catálogo (`ComponenteFilhoPadrao`); os Pedidos existentes não mudam, porque a Peça é
      **cópia** da receita no momento da criação (regras 7 e 19). Se a receita de catálogo de um Componente
      **com escolha** mudou depois da escolha, a confirmação é recusada e a escolha daquele código volta a
      ser pedida — ninguém aprova uma receita que não viu.

    Valem ainda, como parte da regra: só o que está na árvore final é criado, reativado ou gravado; a
    confirmação cria a Peça **idêntica** à que o "Nova Peça" criaria a partir do catálogo resultante; e a
    combinação das receitas importadas com as de catálogo mantidas **não pode fechar ciclo** (regra 20) —
    o que nenhuma das duas tinha sozinha.

*A regra 33 foi decidida em 2026-10-08, na spec da data de entrega do Pedido
(`docs/superpowers/specs/2026-10-08-data-de-entrega-do-pedido-design.md`, decisões D2, D3, D4, D8 e D12).*

33. **Todo Pedido tem data de entrega, e o atraso é derivado dela.**
    - **Obrigatória, livre e editável (D2, D3, D8).** A `DataEntrega` é informada no cadastro e pode
      ser editada em qualquer status. Aceita qualquer dia, inclusive anterior à abertura: um Pedido pode chegar ao
      sistema já atrasado, e um Retrabalho pode ser aberto pelo cliente bem depois de as peças terem
      sido expedidas. É um dia, sem hora nem fuso.
    - **Atrasado (D12).** Um Pedido está **atrasado** quando a `DataEntrega` é anterior a **hoje** e o
      status não é `Concluido` nem `Cancelado`. "Hoje" é a data corrente no horário de Brasília (GMT-3
      fixo, o mesmo da borda de fuso da aplicação). Um Pedido que vence hoje ainda **não** está atrasado.
      O atraso não é gravado: é calculado a cada leitura, no servidor.
    - **Retrabalho (D4, para a Fase 5).** O formulário do Pedido de Retrabalho traz, por padrão, a
      `DataEntrega` do Pedido de origem, e o campo é editável no cadastro.

*A regra 34 foi decidida em 2026-10-16, na spec da Fase 4
(`docs/superpowers/specs/2026-10-16-fase-4-separacao-de-materiais-design.md`, decisões D6, D9, D10, D13 e D14,
e a decisão P2 do plano de backend da Fase 4).*

34. **A separação é um livro só de inclusão.**
    - **Separado (D13).** O **separado** de um material num nó é a soma das separações menos a soma dos
      estornos dele. É sempre **calculado**; não há coluna de quantidade separada em `EstruturaMaterial`.
    - **Pendência (D6).** A **pendência** é o planejado menos o separado, quando positiva, e só existe para o
      material que **fica no almoxarifado** (`Material.SetorDeEstoqueId` nulo) e **está no planejado** do nó.
      O material que fica num Setor nunca tem pendência: o operador o pega ali. Mesmo assim o Separador
      **pode** registrá-lo, se ele estiver no planejado (D16).
    - **Só o planejado (D9).** Só se separa material que está no planejado do nó. Fora dele, a separação é
      recusada (409 `MaterialForaDoPlanejado`): é erro de plano, e o PCP o corrige nos materiais do nó. **Acima
      do planejado** o sistema aceita e avisa (o parafuso que caiu): `acimaDoPlanejado` vale só para o material
      que está no planejado.
    - **Estorno (D10).** A correção é um **estorno**, da linha inteira, **no máximo um por linha**, pelo autor da
      separação, pelo PCP ou pelo Administrador. Estorno não se estorna. Não há saldo de estoque nem devolução: o
      sistema registra o que saiu para cada nó, não quanto há no almoxarifado.
    - **Editar o planejado é livre (D14).** O PCP edita os materiais de qualquer nó a qualquer momento, inclusive
      depois de separar. O que já foi separado é fato e continua no histórico; o material separado que saiu do
      plano volta na leitura com `noPlanejado: false`, `planejado: 0` e **sem pendência** — aparece como "fora
      do planejado", e a separação dele continua estornável. Ele volta **enquanto o separado for maior que
      zero**: estornada toda a separação, não resta nada dele no nó, e ele sai da lista de materiais do nó (as
      linhas continuam no histórico; decisão do usuário na conferência da Fase 4, 2026-10-19).
    - **Livro só de inclusão (D10, D13).** Nunca `UPDATE` nem `DELETE` numa linha de `dbo.MaterialSeparacao`,
      pelo mesmo motivo do livro de movimentações. Por isso **nó com qualquer linha de separação, ainda que
      estornada, não se exclui** (409 `NoComSeparacao`, decisão P2 do plano de backend da Fase 4): com a
      separação antecipada (D11), ela pode existir num Pedido `Aberto`, e apagar o nó exigiria apagar linhas do
      livro.
    - **Não condiciona o início (regra 5, D12).** A pausa do Pedido (regra 31) também não: ela recusa só o
      início, e separar continua.
    - **Riscos aceitos (seção 8 da spec da Fase 4).** Uma separação concorrente com o `PUT` dos materiais
      do nó pode passar por um material que acabou de sair do plano: ela fica no
      histórico como "fora do planejado" e é estornável, e os dois não são serializados por isso. A lista do
      Separador chega inteira, com o mesmo risco do limite de 2100 parâmetros do SQL Server já registrado para
      o `Contains` sobre os nós de Pedidos abertos (medir junto). Um access token emitido com o perfil
      `Almoxarifado` vale, depois da migração, no máximo `Jwt:AccessTokenMinutes` (15 min), e nenhuma rota
      autoriza esse nome.
    - **Fora do escopo (seção 10 da spec).** A requisição de material (D7), o saldo de estoque e a devolução
      (D10), bloquear ou avisar no início (D12), a notificação ao Separador (Fase 3C) e a separação em Pedido
      concluído (a conclusão só nasce na Fase 5, que herda a guarda: a lista do Separador já exclui Pedido
      `Concluido` ou `Cancelado`).

*As regras 35 a 37 foram decididas em 2026-10-20, na spec da Fase 5A
(`docs/superpowers/specs/2026-10-20-fase-5a-afericao-expedicao-e-fechamento-design.md`,
decisões D1 a D10 do usuário e C1 a C20 do desenho, mais as decisões P1, P6 e P10 do plano de backend da fase
e as R4 a R6 da implementação, registradas nas emendas de 2026-10-21 da spec).*

35. **Conserto: a não conforme volta a um Setor, dentro do mesmo Pedido.**
    - **Quem manda e para onde (D6, C19).** A **Qualidade** manda para conserto unidades `NaoConforme`
      (quantidade e Setor) para **qualquer Setor ativo**, inclusive o Setor Expedição: quem sabe qual
      corrige é a Qualidade, e restringir ao Roteiro recusaria a correção feita num Setor que não estava
      nele. Setor inexistente ou inativo é 400 `SetorInvalido`; acima do `NaoConforme`, 409
      `SaldoInsuficiente`. É o **mesmo Pedido**: não é Pedido de Retrabalho.
    - **O caminho (C1, C3).** `AguardandoConserto` (no Setor escolhido, esperando o Movimentador no local de
      expedição) → o Movimentador leva, e a unidade vai a `EmConserto` no mesmo Setor → o operador termina, e
      vai a `ConsertoPronto` → o Movimentador a traz **direto ao local de expedição**, sem refazer o resto do
      Roteiro. Ela volta como `NaExpedicao` e é **medida de novo** (regra 11). A ida e a volta são o `Entrega`
      de sempre, e o terminar é o `Termino` de sempre, com a Peça sem passo do Roteiro: nenhuma ação nova
      para o Movimentador e o operador. O conserto **não mexe no `EstruturaRoteiro`**, e conta como em
      produção (regra 9).
    - **Onde aparece (C4).** Na Fila do Setor, a unidade em conserto (`EmConserto` e `ConsertoPronto`) vem numa
      seção própria, marcada como conserto, com o "Terminar" de sempre — individual
      (`POST /estrutura/{id}/terminos-de-conserto`) ou no lote de término (item de conserto de
      `POST /terminos`, decisão P6 do plano de backend). Nas Tarefas, a ida (`AguardandoConserto`, com o Setor,
      para `EmConserto`) e a volta (`ConsertoPronto`, para o local de expedição) são tarefas como as outras, e
      entram na contagem do menu.
    - **Setor inativado com a peça lá dentro (decisão P10 do plano de backend).** Só **mandar** para Setor
      inativo é recusado: o término e a entrega de volta continuam valendo, como no terminar de sempre.
    - **Estorno (C7).** O conserto não tem agrupador: estorna-se como qualquer movimento, por
      `POST /movimentacoes/{id}/estorno`, enquanto a quantidade não tiver andado; a Qualidade passou a ter
      esse estorno.
36. **Liberação da não conforme: a Qualidade pede, a Gestão responde, e fica gravado.**
    - **Pedir (D7, C6, C18).** A Qualidade pede a liberação de uma quantidade `NaoConforme`, com
      **justificativa obrigatória** (aparada; 400 `JustificativaObrigatoria`; no máximo 500 caracteres, 400
      `TextoLongoDemais`). As unidades vão a `AguardandoLiberacao` e **não saem numa remessa** enquanto
      esperam (regra 16). **No máximo um pedido em aberto por Peça** (409 `LiberacaoEmAberto`): para pedir
      mais, espera-se a resposta, ou estorna-se o pedido e pede-se de novo com a quantidade nova.
    - **Responder (D7, C5).** A **Gestão** (ou o Administrador) responde com a **quantidade liberada**, de
      zero até a pedida (acima dela, 400 `QuantidadeAcimaDaPedida`), e uma observação opcional (até 500). A
      liberada vai a `Liberada`; **o resto volta a `NaoConforme`** (a linha `Recusa`), e zero é a recusa
      total. A resposta fica com o **nome de quem a deu**. Responder um pedido que já foi respondido ou
      estornado é 409 `LiberacaoNaoPendente`. A pendência é relida depois da trava da Peça, a mesma que
      serializa o pedido, a resposta e o estorno do mesmo pedido: se a Qualidade estorna o pedido no instante
      em que a Gestão responde, quem chega depois recebe 409 (`LiberacaoNaoPendente` na resposta,
      `EstornoImpossivel` no estorno), e não fica resposta válida num pedido estornado.
    - **O que o livro guarda.** O pedido e a resposta são registros de `dbo.LiberacaoDimensional` (uma
      tabela, `Tipo` `Pedido` ou `Resposta`), só de inclusão. A linha `PedidoDeLiberacao` aponta o **pedido**;
      as linhas `Liberacao` e `Recusa` apontam a **resposta**. Um pedido está **pendente** quando a linha dele
      não foi estornada e não há resposta com linha não estornada.
    - **Estorno (C7, C17, C18).** Cada registro se estorna **inteiro**. O **pedido** só se estorna sem resposta
      válida (estorne a resposta primeiro) e devolve a quantidade a `NaoConforme`. A **resposta** devolve o
      pedido a "em aberto", e por isso é recusada quando a Peça já tem **outro** pedido em aberto
      (409 `EstornoImpossivel`, para não haver dois). As duas só enquanto a quantidade não andou. Quem
      estorna: o autor, o PCP ou o Administrador.
37. **Avisar e deixar expedir (D8): o Rastru não trava a expedição pela aferição.** Nenhuma situação da
    aferição recusa a remessa; ela **avisa**, e o aviso volta no `201` de `POST /remessas`, por Peça, com a
    quantidade que sai assim. O que a unidade em `NaExpedicao` é, e se avisa ao expedi-la dali, depende da
    aferição válida da Peça:

    | A Peça | A unidade em `NaExpedicao` é | Expedir dali avisa? |
    |---|---|---|
    | não exige relatório | pronta para expedir | não |
    | exige, **sem** aferição | aguardando aferição | **sim**: `SemAfericao` |
    | aferição **conforme** | conforme (D3) | não |
    | aferição **não conforme** | **a medir** (D4) | **sim**: `SemMedicao` |

    A unidade que sai de `NaoConforme` (medida, não conforme e sem liberação) avisa **sempre**:
    `NaoConformeSemLiberacao`. A que sai de `Liberada` ou de `Conforme` nunca avisa. A marca desligada vence: sem `RequerRelatorioDimensional`, a aferição
    não conta. Por isso `Conforme` como posição só existe depois de uma aferição não conforme: com aferição
    conforme, o lote inteiro já é conforme e ninguém o move. As linhas de cada remessa (`GET /remessas/recentes`)
    trazem a posição de origem da quantidade, então "2 não conformes expedidas sem liberação" se lê sem
    estado extra. **Risco aceito:** a unidade expedida antes da aferição fica sem resultado para sempre.

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

> **Nota (2026-10-21, Fase 5A):** o fechamento que a regra 13 pedia nasceu na 5A **contando só o expedido**, e
> o ponto «Descontinuar uma Peça trava o fechamento do Pedido» segue em aberto: a Peça descontinuada continua sem concluir até a **Fase 5B**, que traz o
> `Perdido` e o descarte da Peça ou do Item em Pedido rodando (**obrigatório antes do primeiro uso real**, como
> o roadmap marca).

> **Decidido em 2026-09-24** (spec da Fase 3), e por isso fora desta lista: como sai de "em
> produção" o filho de um nó que não passa pela trava de montagem — montar passou a ser registro
> de todo nó com filhos, e só a validação da trava ficou restrita ao Kit (regra 24).
> **Atualizado em 2026-09-28** (spec da Fase 3D, seção 2.1): a trava deixou de ser restrita ao Kit — o
> pai só entra em produção iniciando-o, o que consome os filhos, então a saída dele já é limitada ao
> total montado, em qualquer Agrupamento (regra 24) — para o nó que já tem filhos ao entrar em
> produção; o nó que ganha filho depois de iniciado é a exceção, da 3B.
> **Atualizado em 2026-10-09** (spec da Fase 3B, D1): desde a Fase 3B, acrescentar filho a nó já
> iniciado é recusado, e a exceção deixou de existir.

Itens de infraestrutura (CI/CD, detalhes de deploy) estão em `03-arquitetura-tecnica.md`.
