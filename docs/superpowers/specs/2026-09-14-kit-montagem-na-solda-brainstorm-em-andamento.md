# Kit — trava de montagem na Solda e aviso de Kit pronto (brainstorm EM ANDAMENTO)

> **Isto não é spec aprovada.** É o estado de um brainstorm (`superpowers:brainstorming`)
> interrompido em 2026-09-14 para o usuário responder de outro dispositivo. Quando o desenho
> for aprovado, este arquivo vira `2026-09-14-kit-montagem-na-solda-design.md` (ou é substituído
> por ele).

## Como retomar (para a sessão que abrir este arquivo)

1. Carregue a skill `superpowers:brainstorming`.
2. Leia este arquivo inteiro. O contexto da seção "O que a documentação diz hoje" **já foi
   levantado** — não refaça a busca, só confira se `main` mudou algo em `specs/01-dominio-e-regras-de-negocio.md`
   (glossário "Agrupamento", regras 9 e 16) e na §2.1/§2.2 da spec da Fase 2.
3. A conversa parou na **Pergunta 10**, abaixo, aguardando a resposta do usuário. Continue dali,
   uma pergunta por vez.
4. Esta ideia **não é da Fase 2B** e não deve entrar na branch `fase-2b-solido-3d`.

## A ideia, nas palavras do usuário

Duas partes, com pesos diferentes:

1. **Trava de montagem (mexe no fluxo — é validação de caso de uso).** Uma Peça montada por
   Itens precisa de **todos os seus Itens no Setor de Solda** para ser fabricada/soldada. Se nem
   todos chegaram, o operador **não pode marcar a Peça como pronta** nem movê-la ao próximo Setor
   do Roteiro dela — fisicamente ela não poderia ser fabricada.
2. **Aviso de Kit pronto (não mexe no fluxo, pode tocar arquitetura).** Avisar os
   movimentadores/separadores de que um Kit **pode ser montado** — todos os Itens estão prontos e
   podem ser ou já foram levados à Solda. O usuário pensou numa **mensageria simples** dirigida a
   esse perfil.
   - Extensão **adiada de propósito** pelo usuário: o mesmo aviso quando um Item fica pronto e
     pode ir ao próximo Setor do Roteiro. Só entra se a mensageria do Kit der certo.

## O que a documentação diz hoje (levantado em 2026-09-14)

- **Glossário, "Agrupamento"**: Tipo `'Kit'` = "peças que vão para a solda, juntas"; `'Avulso'` =
  "peças que não passam por solda". **"O Tipo é descritivo — não impõe roteiro."**
- **Spec `2026-07-27-alteracoes-dominio-lote-divisivel-e-agrupamento-design.md`, "Mudança #2"**:
  decidiu explicitamente que o Tipo **não impõe regra dura** (não valida "Kit obriga Solda no
  roteiro", não bloqueia expedição); Solda é um Setor comum. **Esta ideia reverte essa decisão em
  parte** — o desenho final precisa registrar a reversão nessa linha do glossário e dizer por quê.
- Schema: só `CK_Agrupamento_Tipo`. Nenhum caso de uso, endpoint ou fluxo usa o Tipo para nada.
- **Não existe Setor marcado como "Solda"**: `dbo.Setor` é só nome + ativo.
- **Não existe perfil Movimentador/Separador**: os perfis são Operador, Almoxarifado, PCP,
  Qualidade, Gestão, Administrador (regra 15).
- **Fase certa**: a trava vive no apontamento de Setor (`EstruturaSetorHistorico`), que é a
  **Fase 3**. A "separação" da **Fase 4** (`MaterialSeparacao`) é de material bruto de estoque para
  um `EstruturaItem` — não é o "separador" desta ideia, que recolhe/leva Itens prontos. O usuário
  descreveu a ideia como "no momento da separação de material"; a leitura acima foi apresentada a
  ele e ainda **não foi confirmada nem contestada** explicitamente.
- **Quantidade é absoluta e sem invariante de razão** (spec da Fase 2, §2.1 e §2.2): Peça de 10 com
  "4 suportes por unidade" guarda o filho como 40; a razão 4 não é guardada, e um filho de 45 para
  uma Peça de 10 é legítimo (sobra de refugo).
- **Lote divisível** (regra 9) e **expedição parcial** (regra 16).
- Horizonte do sistema: vida esperada curta (1-2 meses) — "não escala" não decide nada aqui.

## Decisões já tomadas

- **D1 — Unidade da trava: os Itens da própria Peça** (resposta à Pergunta 1). Cada Peça de um
  Agrupamento Kit espera os **próprios** filhos na Solda; Peças do mesmo Kit são independentes
  entre si. O Tipo do Agrupamento decide **se** a trava vale.
  - Descartadas: (B) o Agrupamento Kit inteiro trava junto; (C) toda Peça com filhos trava,
    independente do Tipo — descartada porque um Avulso pode ter filhos montados de outro jeito
    (ex.: parafusados), e a trava seria falsa ali.

- **D2 — Montagem parcial: monta o que dá** (resposta à Pergunta 2, 2026-09-15, opção B abaixo).
  Com 24 suportes de 40 na Solda, soldam-se 6 das 10 Peças. O usuário amarrou isso à expedição
  parcial (regra 16): montar parte é o que permite expedir a parte vital antes.
  - **Consequência aceita pelo usuário: errata na spec da Fase 2**, motivada por **esclarecimento do
    processo atual da fábrica** (não por erro de raciocínio da época). A §2.1 descartou guardar a
    razão por unidade; a trava precisa dela. A **redação** da errata depende da Pergunta 3 (de onde
    vem a razão), e por isso ainda não foi escrita na spec da Fase 2 — entra junto com o design
    aprovado, como seção de errata datada naquela spec, sem reescrever a §2.1 original.
  - Fato levantado ao retomar: a receita de catálogo **já guarda** a razão —
    `ComponenteFilhoPadrao.QuantidadePadrao` é por unidade do pai; é a cópia da Fase 2 que multiplica
    e descarta a razão ao gravar o `EstruturaItem`.

- **D3 — A razão fica guardada numa coluna nova do `EstruturaItem`, ao lado do absoluto**
  (resposta à Pergunta 3, 2026-09-15, opção A). Nome fechado: `QuantidadePorPai` (ver o item seguinte).
  A cópia da receita a preenche com `ComponenteFilhoPadrao.QuantidadePadrao`; o Item ad-hoc a
  recebe de quem cadastra. O absoluto continua sendo o que a Fase 3 movimenta; a razão só serve à
  trava, que libera `mínimo, entre os filhos, de (quantidade na Solda ÷ razão)`.
  - **Texto da errata da Fase 2 agora definido**: a §2.1 passa a guardar **as duas** quantidades
    (absoluta e por unidade do pai), em vez de descartar a razão; o motivo é o esclarecimento de
    processo (montagem parcial, D2). A §2.2 continua valendo — os dois números podem divergir
    (sobra de refugo), e não se cria invariante entre eles.
  - Descartadas: (B) derivar filho ÷ pai — a sobra de refugo vira exigência, e editar o absoluto
    muda a trava em silêncio; (C) guardar só a razão e derivar o absoluto — reabre o custo que a
    §2.1 registrou e acaba com o absoluto customizável da §2.2.
  - Em aberto para o desenho: se a razão é obrigatória em todo Item ou só em filho de Peça de Kit, e
    o que a edição de nó (Fase 2) faz com ela.

## Pergunta 2 — RESPONDIDA (B), mantida para registro

**A Solda de verdade monta parte da quantidade de uma Peça?**

Exemplo: Peça de 10, filho "suporte" de 40. Chegaram 24 suportes na Solda.

- **A) Não, só com o lote inteiro.** Nenhuma das 10 é soldada até os 40 estarem lá. No sistema: a
  Peça só avança quando **toda** a quantidade de cada filho está na Solda. Simples, sem razão.
  Custo: um suporte atrasado ou perdido trava as 10, e isso briga com a expedição parcial (regra 16).
- **B) Sim, monta o que dá.** Com 24 suportes, soldam-se 6. Exige a **razão por unidade** —
  **reabre a §2.1 da Fase 2**, que descartou guardá-la (provável coluna nova em `EstruturaItem`).
- **C) Sim, mas o operador informa quantas montou.** O sistema só confere que **cada filho tem
  alguma quantidade** na Solda. Sem razão, mas a trava fica fraca: com 1 suporte presente, aceitaria
  soldar as 10.

Nenhuma recomendação foi dada ainda, de propósito: depende de como a fábrica opera, não de
técnica. Se for **A**, o desenho é pequeno; se for **B**, a ideia deixa de ser pequena.

- **Nome da coluna da D3 fechado: `QuantidadePorPai`** (2026-09-15). Descartados:
  `QuantidadePorUnidadeDoPai` (comprido), `QuantidadeUnitaria` (não diz "em relação a quê"),
  `Proporcao` (sai do padrão `Quantidade*`), `QuantidadePorPeca` (mente quando o pai é Item) e
  `QuantidadePadrao` — repetir nome entre tabelas não é o problema; o problema é que "Padrão" neste
  projeto significa catálogo, que ao lado de `Quantidade` sugere "sugerida × efetiva" da mesma
  grandeza (convida a "sincronizar" 45 com 4), e que contradiz a regra 19 (o nó é cópia, não
  referência viva ao catálogo).
- **D4 — A trava é marcada no `Setor`, coluna `UtilizaKit`** (resposta à Pergunta 4, 2026-09-15,
  opção A; nome dado pelo usuário). A trava vale quando **as três** condições valem:
  `Agrupamento.Tipo = 'Kit'` **e** `Setor.UtilizaKit = 1` **e** a Peça **tem ao menos um filho**.
  - Vale **todas as vezes** que a Peça passa por um Setor marcado — não só na primeira (a
    recomendação apresentada dizia "primeira passagem"; o usuário decidiu "todas"). Isso cria a
    dependência tratada na Pergunta 5: numa segunda passagem os Itens já viraram Peça e não estão
    em Setor nenhum.
  - Hoje só a Solda seria marcada, mas a marca não é nome fixo.
  - Descartadas: (B) marca no passo do Roteiro — PCP marcaria em todo Pedido e a receita padrão
    precisaria da mesma marca; (C) nome fixo "Solda"; (D) implícito no primeiro Setor do Roteiro.

- **D5 — A trava é local a cada nó com filhos, e a ordem de baixo para cima sai sozinha**
  (esclarecimento do usuário ao ler a Pergunta 5, 2026-09-15). Exemplo dele: A depende de B e C, B de
  C e D, C de D e E, todos passam pela Solda. Monta-se primeiro C (só tem folhas), depois B (precisa
  de C já montado), depois A. Consequências:
  - **A trava não é só da Peça**: vale para **todo `EstruturaItem` com filhos** (Peça ou Item de
    submontagem) do Agrupamento Kit que passa por Setor `UtilizaKit`, sempre olhando **só os filhos
    diretos**. Isso responde a antiga pergunta "Sub-Itens" da fila e **generaliza a D1 e a D4**
    ("a Peça tem filho" → "o nó tem filho").
  - Não é preciso calcular "o pai mais baixo": a ordem vem de o filho-submontagem só existir na
    Solda depois de montado.
  - **Precisão de modelo a confirmar no desenho**: `EstruturaItem` tem **um** pai só
    (`EstruturaPaiId`). "C dentro de A e dentro de B" são **dois nós distintos** (mesmo `Componente`,
    duas linhas), não o mesmo lote compartilhado.
  - **O que a D5 não resolve**: (1) o mesmo nó voltar à Solda (regra 21 permite Setor repetido no
    Roteiro) — o caso que motivava a pergunta de "segunda passagem"; (2) o destino da quantidade de
    D e E quando C é montado — a conservação ainda precisa de "montado" (Pergunta 5).

- **D6 — Um nó montado volta a Setor `UtilizaKit`, e a partir daí a trava conta o que já foi
  montado** (resposta à Pergunta 5b, 2026-09-15). Ex.: C em Solda → Usinagem → Solda: na segunda
  passagem, o limite de C que pode sair não é "filhos na Solda ÷ `QuantidadePorPai`", é o total de
  C já montado. Logo, **"quanto deste nó já foi montado" precisa ser um número que o sistema sabe** —
  o que pesa a favor de a montagem ser registro próprio (Pergunta 5, opção A).
- **D7 — Montar é registro próprio, separado de mover** (resposta à Pergunta 5, 2026-09-15,
  opção A; motivo do usuário: "montar e mover precisam ser ações diferentes"). Na Solda, o operador
  registra "montei N de C". O sistema:
  1. valida `N ≤ mínimo, entre os filhos diretos, de (quantidade na Solda ÷ QuantidadePorPai)`;
  2. baixa `N × QuantidadePorPai` de cada filho direto para um **destino terminal novo, "montado"**
     — que entra na conservação de quantidade (regra 9) ao lado de Setor, expedido e perdido;
  3. soma N ao **total montado** de C, que tem data, responsável e quantidade.
  A saída de C de Setor `UtilizaKit` fica limitada ao total montado, em qualquer passagem (D6). O
  registro de montagem é o candidato natural a disparar o aviso de Kit pronto (parte 2 da ideia).
  - Descartadas: (B) montagem implícita na saída — numa segunda passagem sair não é montar, e a saída
    teria de adivinhar qual dos dois está fazendo; (C) Item não consumido — não fecha a conservação.

- **D8 — Unidade que não dá para montar: a perda sobe, e a reposição é Pedido de Retrabalho**
  (resposta à Pergunta 6, 2026-09-15, opção A). O PCP cria o Retrabalho para a peça faltante. A
  regra 17 fica intacta.
  - **Requisito novo trazido pelo usuário: no Pedido de Retrabalho, poder marcar nós como já
    prontos.** No exemplo, C é feita de D e E, mas só D se perdeu; o E existe e não precisa ser
    fabricado de novo. O Retrabalho precisa dizer "E já está pronto" para a trava de montagem (D7)
    contar E como presente sem ele percorrer Roteiro.
  - Em aberto: até onde a perda sobe (Pergunta 7) e como "pronto" funciona (fila).

- **D9 — A perda sobe até a Peça do topo, e o "pronto" do Retrabalho é só da árvore daquela
  unidade** (resposta à Pergunta 7, 2026-09-15, opção A; "se não vai furar as outras regras").
  Perde-se 1 A no Pedido original, que conclui pela regra 13; o Retrabalho tem A (1 unidade) como
  Peça, fabrica o que faltou (D) e marca como pronto o que já existe **dentro da árvore dessa única
  A** (E, B...). **Todo o resto continua no Pedido original**: as outras 9 A, e qualquer Peça que não
  seja desta árvore, não se movem nem são marcadas.

- **D10 — As partes prontas da unidade perdida saem do original como perda, junto com a Peça**
  (resposta à Pergunta 8, 2026-09-15, opção A). Registrar a perda de 1 A leva ao bucket "perdido" o
  que pendia daquela unidade (no exemplo, 1 E e 1 B); no Retrabalho, esses nós nascem marcados como
  prontos, com quantidade própria. Sem vínculo por nó entre o que saiu e o que entrou.
  - **Justificativa do usuário, registrada como tal:** o rastro não se perde, porque o Pedido de
    Retrabalho **já nasce vinculado ao original** (`PedidoOrigemId`, `MotivoRetrabalho = 'Perda'`),
    e o original é quem mostra o saldo perdido. Confrontando os dois, encontra-se o destino do
    saldo — se foi realmente perdido/excluído, ou se voltou como Retrabalho e foi expedido depois.
    Isso vale mesmo que alguém precise da informação; um vínculo por nó seria redundante com o
    vínculo por Pedido.
  - Descartadas: (B) transferência rastreada — destino novo na conservação e vínculo nos dois lados,
    redundante com `PedidoOrigemId`; (C) nada no original — a conservação do Item nunca fecha e o
    operador vê na Solda um E que não existe mais.

- **D11 — "Pronto" no Retrabalho = sem Roteiro a percorrer; a movimentação física continua
  normal** (resposta à Pergunta 9, 2026-09-15, opção A). Folha pronta = já fabricada; nó com filhos
  pronto = já montado (total montado = quantidade, trava satisfeita, filhos dispensáveis no
  Retrabalho). O nó ainda **entra** no Setor `UtilizaKit` do pai por movimentação comum — o sistema
  registra só o que aconteceu no chão de fábrica. Coerente com a D7 (montar ≠ mover).
  - Descartadas: (B) nascer dentro do Setor de montagem — movimentação que ninguém fez, e escolha de
    Setor ambígua com dois `UtilizaKit` no Roteiro; (C) não passar por Setor — o E some da fila da
    Solda e o movimentador não tem o que levar.

**Parte 1 da ideia (trava de montagem) fechada no nível de domínio.** Daqui em diante, parte 2
(aviso de Kit pronto).

## Pergunta 10 — ABERTA, aguardando o usuário

**Quem termina o trabalho num Setor é quem leva o Item ao próximo?** O aviso "Kit pronto para coleta"
pressupõe um estado que a Fase 3 ainda não desenhou: **Item terminado no Setor, esperando alguém
levar**. Hoje o schema só tem `DataEntrada`/`DataSaida` em `EstruturaSetorHistorico`.

- **A) São pessoas diferentes.** O operador marca "terminei"; o movimentador leva. A Fase 3 precisa
  do estado "aguardando coleta" (terminar ≠ mover, mesmo espírito da D7). O aviso sai quando os
  filhos diretos — **aguardando coleta + já na Solda** — passam a permitir montar mais unidades do
  pai. Esse mesmo estado é o que a extensão adiada (aviso de Item pronto para o próximo Setor)
  precisa.
- **B) É a mesma pessoa.** O operador termina e já leva. Não há "coleta" para avisar; o aviso útil
  seria para o **operador da Solda** ("dá para montar N de C"), disparado quando uma entrada na
  Solda aumenta o montável.
- **C) Depende do Setor.** Precisa de marca por Setor, e o aviso vale só onde há movimentador.

Sem recomendação: é pergunta de como a fábrica opera.

## Pergunta 9 — RESPONDIDA (A), mantida para registro

**Como um nó marcado "pronto" se comporta no Retrabalho?**

- **A) Pronto = já fabricado (folha) ou já montado (nó com filhos), sem Roteiro a percorrer; a
  movimentação física continua normal.** O nó não passa pelos Setores anteriores, mas ainda precisa
  **entrar** no Setor `UtilizaKit` do pai por uma movimentação comum (alguém leva o E até a Solda).
  Para nó com filhos, "pronto" significa total montado = quantidade (a trava dele fica satisfeita e
  os filhos nem precisam existir no Retrabalho). Coerente com a D7 (montar ≠ mover), e o aviso de
  Kit pronto funciona no Retrabalho igual ao original.
- **B) Pronto nasce direto dentro do Setor de montagem do pai.** O sistema cria a entrada no Setor
  sozinho ao marcar. Um passo a menos, mas registra uma movimentação que ninguém fez e escolhe um
  Setor por conta própria (qual, se o Roteiro do pai tem dois `UtilizaKit`?).
- **C) Pronto não passa por Setor nenhum: a trava soma "prontos" aos presentes.** Sem movimentação
  falsa, mas o E nunca aparece na fila da Solda, e o movimentador não tem o que levar no sistema.

Recomendação apresentada: **A**.

## Pergunta 8 — RESPONDIDA (A), mantida para registro

**O que acontece, no Pedido original, com as partes prontas dessa A que foram para o Retrabalho?**
Exemplo: com a 10ª A perdida, o original ainda tem 1 E sobrando na Solda e 1 B já montada. No chão de
fábrica elas vão para o Retrabalho; no sistema, a conservação do original ainda as conta.

- **A) Baixa como perda junto com a A.** Registrar a perda de 1 A leva junto, para o bucket
  "perdido", o que estava pendurado naquela unidade (1 E, 1 B). No Retrabalho, E e B nascem
  marcados como prontos, com quantidade própria. Sem mecanismo novo. Custo: o histórico chama de
  "perdido" algo que foi reaproveitado, e não há vínculo entre o E que saiu e o E que entrou.
- **B) Transferência rastreada.** Destino novo na conservação, "transferido para Retrabalho",
  apontando o Pedido de destino; no Retrabalho, o nó pronto aponta de onde veio. Fiel e
  auditável. Custo: mais uma tabela/coluna e mais uma regra de conservação nos dois lados.
- **C) Nada no original.** A sobra fica lá: não trava a conclusão (a regra 13 só olha a Peça), mas
  a conservação do Item nunca fecha e o operador vê um E "na Solda" que não existe mais.

Recomendação apresentada: **A**, pelo horizonte curto do sistema e porque a perda já é o registro que
leva a A para o Retrabalho; a **B** só vale se alguém precisar consultar "de onde veio este E".

## Pergunta 7 — RESPONDIDA (A), mantida para registro

**A perda sobe só até C, ou até a Peça do topo (A)?** Cadeia do exemplo: A ← C ← D.

- **A) Sobe até a Peça do topo.** Registra-se perda de 1 A no Pedido original, que conclui normalmente
  (regra 13 só olha a Peça). O Retrabalho tem como Peça **A** (1 unidade), com a estrutura que
  faltar: C com D a fabricar, e com **E, B e o que mais existir marcados como prontos**. É o A que
  vai ao cliente, então a expedição acontece no Retrabalho.
- **B) Para em C.** Perda de 1 C no original; o Retrabalho fabrica só C. Mas então a 10ª A do
  original continua esperando uma C que, pela regra 17, nunca volta para aquele Pedido — o original
  não conclui. E, pela regra 18, a Peça do Retrabalho precisaria de Componente e sólido próprios
  para C.

Recomendação apresentada: **A** — é a única em que o Pedido original fecha, e o "marcar como pronto"
passa a ser exatamente o que o Retrabalho precisa para não refabricar o que já existe.

## Pergunta 6 — RESPONDIDA (A), mantida para registro

**O que acontece quando um filho se perde e o pai não consegue ser montado inteiro?** Exemplo: C de
10, D de 40 (`QuantidadePorPai` = 4). Perdem-se 2 D (regra 17) → 38 ÷ 4 = 9 C montáveis; a 10ª
nunca monta. Sobram ainda 2 D na Solda.

Por que importa: a regra 13 conclui só a **Peça** (topo), mas a 10ª C não montada impede montar a
10ª A — e a 10ª A fica em produção para sempre, travando Agrupamento e Pedido. É o mesmo desenho do
ponto em aberto "Descontinuar uma Peça trava o fechamento do Pedido".

- **A) A perda sobe para o pai.** A quantidade do pai que não dá para montar é registrada como perda
  do **pai** (`MortaEmProcesso`), e a reposição é um Pedido de Retrabalho, como a regra 17 já manda.
  Sem mecanismo novo; o Retrabalho monta a estrutura que precisar refazer. Custo: no sistema a
  reposição é da unidade inteira, mesmo que na fábrica só o suporte seja refeito.
- **B) Repõe o filho dentro do mesmo Pedido.** Aumenta-se a quantidade absoluta de D (+2), que é
  customizável pela §2.2, e os 2 novos suportes seguem o Roteiro até a Solda. Fiel ao chão de
  fábrica, mas **contradiz a regra 17** (reposição é sempre Pedido de Retrabalho separado) para
  Item.
- **C) Não decidir agora.** A trava só informa a falta, e o destino da unidade não montada fica com o
  ponto em aberto de "Descontinuar", na Fase 5.

Sem recomendação por enquanto: depende de como a fábrica repõe hoje.

## Pergunta 5b — RESPONDIDA (sim, volta), mantida para registro

**Um mesmo nó volta a um Setor `UtilizaKit` depois de montado?** Ex.: C passa por Solda → Usinagem →
Solda (retorno permitido pela regra 21). Se nunca acontece, "toda passagem" (D4) não gera caso
especial. Se acontece, a segunda passagem precisa contar "já montado", não "filhos na Solda".

## Pergunta 5 — RESPONDIDA (A), mantida para registro

**Como a montagem é registrada, e o que acontece com a quantidade do Item montado?**

Hoje a conservação (regra 9) fecha só com Setor + expedido + perdido — um Item soldado dentro da Peça
não é nenhum dos três. E a D4 ("todas as vezes") precisa saber quanto **já foi montado**, senão a
segunda passagem pela Solda trava para sempre.

- **A) Montagem é um registro explícito.** Na Solda, o operador registra "montei N Peças". O sistema
  valida `N ≤ mínimo(filho na Solda ÷ QuantidadePorPai)` e baixa `N × QuantidadePorPai` de cada
  filho para um destino terminal novo, "montado". A trava da saída passa a ser: **a Peça não sai de
  Setor `UtilizaKit` com mais do que o total já montado** — o que torna a segunda passagem
  automaticamente liberada. Custo: um apontamento a mais para o operador.
- **B) Montagem implícita na saída da Peça.** Ao apontar saída de N Peças do Setor marcado, o
  sistema consome os Itens nesse momento. Um clique a menos, mas mistura "montei" com "movi": Peça
  montada esperando na Solda não aparece como montada, e a contagem de "já montado" precisa existir
  do mesmo jeito para a segunda passagem.
- **C) Item não é consumido.** Não fecha a conservação do Item e a segunda passagem trava.

Recomendação apresentada: **A**.

## Pergunta 4 — RESPONDIDA (A, `UtilizaKit`), mantida para registro

**Como o sistema sabe em qual Setor a trava vale?** Hoje `dbo.Setor` é só nome + ativo, e a regra 21
permite o mesmo Setor repetido no Roteiro.

- **A) Marca no `Setor`** (ex.: `EhMontagem BIT`). Todo Setor marcado aplica a trava. Uma coluna,
  vale para todos os Pedidos. Com Setor de montagem repetido no Roteiro da Peça, a trava vale na
  **primeira** passagem (na segunda os Itens já viraram Peça).
- **B) Marca no passo do Roteiro da Peça** (`EstruturaRoteiro`). Explícito por Peça; cobre "esta
  Peça é montada neste passo" mesmo num Setor que não é Solda. Custo: o PCP marca em todo Pedido, e
  a receita padrão (`ComponenteRoteiroPadrao`) precisa da mesma marca para a cópia trazer.
- **C) Nome fixo "Solda".** Sem schema; quebra ao renomear o Setor, e a spec da Fase 1 (cadastros
  básicos) já registra que inativar "Solda" impede recriar o nome.
- **D) Implícito: o primeiro Setor do Roteiro da Peça.** Sem schema; errado quando a própria Peça
  tem processo antes da montagem (ex.: a base é cortada e dobrada, depois recebe os suportes).

Recomendação apresentada: **A**.

## Pergunta 3 — RESPONDIDA (A), mantida para registro

**De onde vem a razão por unidade que a trava usa?** Exemplo: Peça de 10, suporte de 45
(4 por unidade + 5 de sobra de refugo).

- **A) Coluna nova no `EstruturaItem`, guardada junto com o absoluto** (ex.:
  `QuantidadePorUnidadeDoPai`). A cópia da receita preenche com `QuantidadePadrao`; o item ad-hoc
  informa à mão. O absoluto continua sendo o que o apontamento da Fase 3 movimenta, e a razão só
  serve à trava. A trava libera `mínimo(presente na Solda ÷ razão)` entre os filhos → com 24
  suportes, 6 Peças. Errata **pequena**: a §2.1 passa a guardar as duas, não troca uma pela outra.
  Custo: dois números que podem divergir (45 vs 10 × 4), e isso é legítimo pela §2.2.
- **B) Derivar: filho ÷ pai, sem schema novo.** 45 ÷ 10 = 4,5 → a trava exige 4,5 por Peça e, com
  24 suportes, libera 5 em vez de 6. A sobra de refugo vira exigência; editar o absoluto muda a
  trava sem ninguém perceber.
- **C) Inverter a §2.1: guardar só a razão e derivar o absoluto.** A errata **grande** — reabre o
  custo que a Fase 2 registrou (toda consulta de setor sobe a árvore) e acaba com o absoluto
  customizável da §2.2.

Recomendação apresentada: **A**.

## Perguntas ainda não feitas (fila, uma por vez, nesta ordem provável)

(Os antigos itens "Item perdido" e "Sub-Itens" saíram da fila: o primeiro virou a Pergunta 6, o
segundo foi respondido pela D5.)

(O item "Pronto no Retrabalho" saiu da fila: a parte do original foi respondida pela D10, e a
mecânica do nó virou a Pergunta 9.)

5. **Quem recebe o aviso**: perfil novo (Movimentador) — que exige mexer em `permissoes.ts` e nos
   `[Authorize(Roles)]`, ver a dívida de permissão hardcoded —, ou um perfil existente
   (Almoxarifado? Operador?).
6. **Mecanismo do aviso** (arquitetura): sem PWA no MVP, então sem push do sistema operacional.
   Candidatos a comparar: tela/contador "Kits prontos para coleta" com polling; SignalR;
   tabela de notificação lida/não lida. Considerar o horizonte curto do sistema.
7. **Momento do aviso**: quando o **último** Item fica pronto no Setor anterior (pode ser levado) ou
   quando o último Item **chega** na Solda (pode ser montado)? O usuário disse "podem ser/foram
   alocados" — as duas leituras cabem.
8. **Em que fase entra**: a trava depende da Fase 3; decidir se vira parte da Fase 3 ou uma fase
   própria logo depois dela. `06-roadmap-mvp.md` precisa refletir.
