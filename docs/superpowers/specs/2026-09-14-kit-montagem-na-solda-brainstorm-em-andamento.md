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
3. A conversa parou na **Pergunta 4**, abaixo, aguardando a resposta do usuário. Continue dali,
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
  (resposta à Pergunta 3, 2026-09-15, opção A). Nome de trabalho: `QuantidadePorUnidadeDoPai`.
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

## Pergunta 4 — ABERTA, aguardando o usuário

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

2. **O que acontece com a quantidade do Item depois de soldado?** Ele "some" dentro da Peça. A
   conservação de quantidade (regra 9) fala da Peça; hoje não há bucket "consumido na montagem"
   para Item. Pode já estar respondido quando a Fase 3 for desenhada — confirmar antes de inventar.
3. **Item perdido** (regra 17): com a montagem parcial (D2), a perda de suporte reduz quantas Peças
   dá para montar — as Peças que ficam sem Item nunca montam, e isso as prende em produção? Liga com o ponto em aberto
   "Descontinuar uma Peça trava o fechamento do Pedido", do mesmo arquivo de domínio.
4. **Sub-Itens**: a trava vale só para os filhos diretos da Peça, ou um Item com filhos (submontagem)
   também espera os dele numa Solda anterior?
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
