# Kit, montagem e movimentação — design

- **Data:** 2026-09-15 (brainstorm iniciado em 2026-09-14)
- **Status:** aprovado pelo usuário, seção a seção
- **Natureza:** spec de **domínio e roadmap**. Não define endpoints, telas nem tasks — cada fase que
  ela alimenta (3, 3B, 5 e 3C) terá brainstorm, spec e plano próprios, partindo daqui.

## 1. Contexto

Hoje o Tipo do Agrupamento (`'Kit'` | `'Avulso'`) é **só descritivo**. A spec
`2026-07-27-alteracoes-dominio-lote-divisivel-e-agrupamento-design.md`, na seção "Mudança #2 — Kit →
Agrupamento com Tipo", decidiu que ele não impõe regra dura: não valida "Kit obriga Solda no roteiro"
nem bloqueia nada, e Solda é um Setor comum. Nenhum caso de uso, endpoint ou fluxo usa o Tipo.

O usuário trouxe duas necessidades do chão de fábrica que **revertem essa decisão em parte**:

1. **Trava de montagem.** Uma peça montada a partir de outras (soldada) só pode ser fabricada com as
   suas partes presentes na Solda. Sem elas, o operador não pode dá-la como pronta nem movê-la ao
   próximo Setor, porque fisicamente ela não existe ainda.
2. **Aviso de coleta.** Os movimentadores precisam saber o que está pronto para levar — em especial,
   quando um Kit está completo e pode ir inteiro para a Solda.

Durante o brainstorm, a segunda necessidade se mostrou **geral**: todo Item e toda Peça, de Kit ou
Avulso, precisa ser levado ao próximo Setor por alguém. Só a trava é exclusiva do Kit.

**Exemplo usado ao longo do documento.** Uma Peça A é montada com B e C; B é montada com C e D; C é
montada com D e E — todas passam pela Solda. (Cada `EstruturaItem` tem **um** pai só, então "C dentro
de A" e "C dentro de B" são **dois nós**, do mesmo Componente.) Para os números: C de quantidade 10,
com D de 40 (4 por C).

## 2. Escopo deste documento

**Muda junto com a aprovação desta spec:**

| Arquivo | Mudança |
|---|---|
| `specs/01-dominio-e-regras-de-negocio.md` | Glossário do Agrupamento, regras 9, 15 e 17 alteradas, regras 22 a 27 novas (seção 4) |
| `specs/06-roadmap-mvp.md` | Fase 3 ampliada, Fase 3B e Fase 3C novas, Fase 5 ampliada, dívida de CRUD (seção 5) |
| `specs/03-arquitetura-tecnica.md` | Separar "PWA/offline" (continua descartado) de "PWA mínimo para push" (seção 7) |
| `specs/00-visao-geral.md` | Perfil Movimentador (seção 7) |
| Spec da Fase 2 (`2026-08-29-fase-2-estrutura-recursiva-design.md`) | Errata datada na §2.1, sem reescrever o texto original (seção 6) |

**Fica para o início de cada fase:** as mudanças em `specs/02-modelo-de-dados.sql` —
`Setor.UtilizaKit`, `EstruturaItem.QuantidadePorPai`, o destino "montado", o motivo `Descarte` em
`CK_Perda_Motivo` e a tabela de inscrição de push. Segue o precedente da Fase 2, cuja constraint
entrou no schema ao iniciar a fase, e evita um schema três fases à frente do código. As **regras**
entram no `01` já.

**Não muda:** a regra 13 (conclusão pela Peça), a regra 17 no que tem de essencial (reposição é
sempre Pedido de Retrabalho separado) e o rastreamento por lote agregado, sem serial.

## 3. Decisões, com o porquê

### 3.1 Terminar e mover são ações separadas

Na fábrica, **quem termina o trabalho num Setor não é quem leva a peça ao próximo**. O operador
registra que terminou; a quantidade passa a **aguardar coleta**; um **Movimentador** a leva e
registra a entrada no próximo destino. Isso vale para todo `EstruturaItem`, de Kit ou Avulso.

Consequência: a Fase 3, que hoje prevê só "apontamento de entrada/saída", ganha esse estado
intermediário. Como ele é representado no banco é decisão da spec da Fase 3; para a conservação de
quantidade, "aguardando coleta" conta como **em produção**.

**Perfil novo `Movimentador`.** Descartados o Almoxarifado (misturaria separação de material bruto
com movimentação de Item) e o Operador (o aviso ficaria sem destinatário). O custo é conhecido e foi
aceito: perfil novo exige mudar `dbo.Perfil`, a tabela `web/src/auth/permissoes.ts` e os
`[Authorize(Roles)]` literais — código e deploy.

### 3.2 Tarefas do Movimentador

O aviso é uma **lista de tarefas calculada a partir do estado**, com atualização periódica e
contador no menu — não uma tabela de notificações. O aviso **é** o estado "aguardando coleta":
quando alguém leva, ele some sozinho, e nunca sobra tarefa já feita. O usuário quis assim porque o
movimentador deve **consultar todas as suas tarefas**, não só receber uma notificação.

Duas seções, com papéis diferentes:

- **Item pronto** — o que terminou num Setor e aguarda coleta. É **tarefa** em geral; é **só
  informativo** quando se trata de filho de Kit a caminho de um Setor `UtilizaKit`, porque esse
  filho não vai sozinho (seção 3.5).
- **Kit pronto para montagem** — é a **tarefa** do movimentador com Kits: levar tudo até a Solda.
  Aparece quando os filhos diretos aguardando coleta formam ao menos um **conjunto completo** do pai
  e informa quantos conjuntos dá para levar.

Descartadas: tabela de notificação persistida (duplica o estado e mostraria "Kit pronto" de um Kit
que outro movimentador já levou) e tempo real por WebSocket (infraestrutura na VPS para ganhar
segundos). A notificação no celular vem por cima desta lista, na Fase 3C (seção 3.7).

### 3.3 Trava de montagem

**Quando vale.** As três condições juntas: o Agrupamento é **Kit**, o Setor tem **`UtilizaKit`**, e o
nó **tem filhos**. Avulso nunca trava — um Avulso pode ter filhos montados de outro jeito (parafusados,
por exemplo), e a trava seria falsa ali.

**Onde se marca.** No `Setor`, coluna **`UtilizaKit`** (nome dado pelo usuário). Hoje só a Solda seria
marcada, mas a regra não depende de nome. Descartados: marca no passo do Roteiro (o PCP marcaria em
todo Pedido, e a receita padrão precisaria da mesma marca), nome fixo "Solda" e "primeiro Setor do
Roteiro" (erra quando a própria Peça tem processo antes da montagem).

**Local a cada nó.** A trava vale para **todo nó com filhos**, Peça ou Item de submontagem, e olha
**só os filhos diretos**. Não é preciso calcular "o pai mais baixo": no exemplo, C só pode ser montado
quando D e E chegam; B precisa de C, que só existe na Solda depois de montado; A vem por último. A
ordem de baixo para cima é consequência.

**Montar é registro próprio**, separado de mover ("montar e mover precisam ser ações diferentes"). Na
Solda, o operador registra "montei N de C". O sistema:

1. aceita se `N ≤ mínimo, entre os filhos diretos, de ⌊quantidade no Setor ÷ QuantidadePorPai⌋`;
2. baixa `N × QuantidadePorPai` de cada filho para o destino terminal **"montado"**, **gravando a
   baixa por filho** — assim, editar a razão depois não reescreve o passado;
3. soma N ao **total montado** de C.

A **saída** de C de um Setor `UtilizaKit` fica limitada ao total montado. Isso cobre a volta do mesmo
nó à Solda (permitida pela regra 21 — por exemplo, Solda → Usinagem → Solda): na segunda passagem os
filhos já viraram C, e a trava conta o já montado, não os filhos presentes.

Descartada a montagem implícita na saída: numa segunda passagem, sair não é montar, e o apontamento
teria de adivinhar qual dos dois está fazendo.

### 3.4 Montagem parcial e `QuantidadePorPai`

**A Solda monta o que dá.** Com 24 dos 40 D presentes, montam-se 6 das 10 C. O usuário amarrou isso à
expedição parcial (regra 16): montar parte é o que permite expedir a parte vital antes. Descartados:
"só com o lote inteiro" (um D atrasado trava as 10) e "o operador informa quanto montou, o sistema só
confere que cada filho tem alguma quantidade" (com 1 D presente, aceitaria montar as 10).

**A razão por unidade passa a ser guardada**, numa coluna nova **`EstruturaItem.QuantidadePorPai`**, ao
lado da quantidade absoluta:

- a cópia da receita a preenche com `ComponenteFilhoPadrao.QuantidadePadrao` — a razão **já existe**
  no catálogo; é a cópia da Fase 2 que multiplica e a descarta;
- num Item ad-hoc, quem cadastra informa;
- é **obrigatória em todo Item e nula na Peça**, para que trocar o Tipo de um Agrupamento não deixe
  nó sem razão;
- a absoluta continua sendo o que a Fase 3 movimenta; a razão serve à trava e ao aviso;
- **não existe invariante entre as duas** (a §2.2 da Fase 2 continua valendo): C de 10 com D de 45 —
  4 por C mais 5 de sobra de refugo — é legítimo.

Isso reverte o que a §2.1 da Fase 2 descartou, e por isso gera a errata da seção 6.

Descartados: derivar a razão como filho ÷ pai (45 ÷ 10 = 4,5 transforma a sobra de refugo em
exigência e libera 5 C em vez de 6) e inverter a §2.1, guardando só a razão (reabre o custo que a
Fase 2 registrou, de toda consulta de setor subir a árvore, e acaba com o absoluto customizável).

**Nome.** `QuantidadePorPai`. Descartados: `QuantidadePorUnidadeDoPai` (comprido),
`QuantidadeUnitaria` (não diz "em relação a quê"), `Proporcao` (sai do padrão `Quantidade*` do
schema), `QuantidadePorPeca` (mente quando o pai é Item) e `QuantidadePadrao`. Neste último, repetir
nome entre tabelas não é o problema; o problema é que "Padrão" neste projeto significa catálogo, que
ao lado de `Quantidade` sugere "sugerida × efetiva" da mesma grandeza (convidando alguém a
"sincronizar" 45 com 4), e que contradiz a regra 19 — o nó é cópia da receita, não referência viva a
ela.

### 3.5 Conjunto completo e descarte

**Um Kit pode ir para a Solda em parte do pai, mas nunca incompleto.** Levar os conjuntos de 7 das 10
C é normal; levar só os D, ou D de menos, não. O motivo é físico: peça solta na Solda ocupa espaço que
serviria a outra coisa, e, se houver perda antes de o resto chegar, aquele espaço fica sem destino.

É **validação do sistema**, não só organização: a entrada de filhos de Kit num Setor `UtilizaKit` só
é aceita em **conjuntos completos** — `N × QuantidadePorPai` de todos os filhos diretos, juntos, na
mesma movimentação. Sem exceção para completar conjunto que perdeu parte dentro da Solda: isso é
perda (seção 3.6).

**A sobra que não fecha conjunto** (os 5 D de refugo) nunca entra na Solda e, se não for usada, sai
como **descarte** — um motivo novo em `Perda`, `Descarte`, ao lado de `PerdaArmazem` e
`MortaEmProcesso`. Entra no bucket "perdido"; relatório de perda separa `Descarte` de perda de
verdade, e Retrabalho de reposição não se aplica a ele. Descartados: tabela própria de descarte
(mais uma tabela para o que é só outro motivo de saída terminal) e baixa implícita ao concluir a
Peça (sem data, responsável nem Setor).

### 3.6 Perda que impede montar

Com montagem parcial, perder um filho reduz o que dá para montar. Perdendo 2 dos 40 D, dá para montar
9 C; a 10ª C nunca monta, então a 10ª B e a 10ª A também não — e, pela regra 13, a 10ª A ficaria em
produção para sempre, travando Agrupamento e Pedido.

**A perda sobe até a Peça do topo.** Registra-se a perda de 1 A no Pedido original, que conclui
normalmente. As partes daquela unidade que existem e ainda não foram montadas nela (no exemplo, o E
e os D que seriam da 10ª C, e as demais partes já prontas da 10ª B e da 10ª A) **saem junto como
perda**. A reposição é um **Pedido de Retrabalho**, como a regra 17 já
manda, e o PCP o monta para a peça faltante.

Parar a perda em C foi descartado: a 10ª A do original continuaria esperando uma C que, pela regra
17, nunca volta para aquele Pedido; e a Peça do Retrabalho seria C, que pela regra 18 precisaria de
Componente e sólido próprios. Repor o filho dentro do mesmo Pedido também foi descartado, por
contradizer a regra 17.

**O Retrabalho marca como pronto o que já existe**, e só dentro da árvore daquela unidade — as outras
9 A e qualquer Peça fora dessa árvore continuam no Pedido original. Um nó **pronto**:

- não percorre Roteiro (folha pronta = já fabricada; nó com filhos pronto = já montado, com total
  montado igual à quantidade, e seus filhos nem precisam existir no Retrabalho);
- nasce **aguardando coleta** e é levado à Solda por uma movimentação normal — o sistema registra só o
  que aconteceu no chão de fábrica.

Descartados: o nó pronto nascer dentro do Setor de montagem (movimentação que ninguém fez, e escolha
ambígua de Setor com dois `UtilizaKit` no Roteiro) e não passar por Setor nenhum (some da fila da
Solda, e o movimentador não tem o que levar).

**Por que as partes saem como perda, sem vínculo por nó com o Retrabalho.** Justificativa do usuário:
o Pedido de Retrabalho **já nasce vinculado ao original** (`PedidoOrigemId`, com
`MotivoRetrabalho = 'Perda'`), e é o original que mostra o saldo perdido. Confrontando os dois,
encontra-se o destino do saldo — se foi realmente perdido ou descartado, ou se voltou como Retrabalho
e foi expedido depois. Um vínculo por nó seria redundante com o vínculo por Pedido. Descartado:
transferência rastreada, com destino novo na conservação e vínculo nos dois lados.

### 3.7 Notificação push

Uma **fase pequena própria (3C)**, por cima da lista de tarefas: a notificação diz "olhe agora", e
tocar nela abre a lista, que continua sendo a fonte da verdade. Nada da lista é jogado fora.

**Não reabre a decisão "PWA/offline".** Aquela decisão (em `specs/03-arquitetura-tecnica.md`) descartou
desenhar a camada de estado para funcionar sem rede. O push exige só o **mínimo de PWA**: manifesto,
ícones e um service worker **sem cache de API**.

Custo levantado e aceito: tabela de inscrição (usuário, endpoint, chaves do navegador) com endpoints
de inscrever e cancelar; chaves VAPID como segredo de ambiente na VPS; biblioteca de envio e limpeza
de inscrição morta; disparo por evento no servidor (recalcular conjunto completo a cada "terminei");
pedido de permissão na tela; verificação manual num Android real por HTTPS, porque service worker não
roda no jsdom. A entrega não é garantida (o Android atrasa push para economizar bateria) — é reforço,
nunca a única fonte da tarefa. Estimativa: 5 a 7 tasks.

O celular do movimentador é **pessoal**, o caso favorável: a inscrição fica presa a uma pessoa, sem
troca a cada turno.

## 4. Texto para `specs/01-dominio-e-regras-de-negocio.md`

### 4.1 Alterações

- **Glossário, "Agrupamento":** o Tipo deixa de ser só descritivo. Kit fica sujeito às regras 24 e 25;
  Avulso segue sem trava. A redação nova registra a data desta reversão e o motivo (seção 1).
- **Regra 9:** a conservação passa a valer para **todo `EstruturaItem`**, não só para a Peça:
  **em produção + montado + expedido + perdido = total**. "Aguardando coleta" conta como em produção.
- **Regra 15:** a lista de perfis ganha o **Movimentador**.
- **Regra 17:** a perda pode ser registrada em qualquer `EstruturaItem`, e ganha o motivo `Descarte`.

### 4.2 Regras novas

- **22. Terminar e mover são ações separadas.** O operador registra que terminou no Setor e a
  quantidade passa a aguardar coleta; o Movimentador registra a entrada no próximo destino. Vale para
  todo `EstruturaItem`. Um filho que concluiu o próprio Roteiro aguarda coleta para a montagem do pai.
- **23. Tarefas do Movimentador**, calculadas do estado: **Item pronto** (tarefa; só informativo para
  filho de Kit a caminho de Setor `UtilizaKit`) e **Kit pronto** (tarefa, quando os filhos diretos
  aguardando coleta formam ao menos um conjunto completo; conjuntos = mínimo, entre os filhos, de
  ⌊aguardando coleta ÷ `QuantidadePorPai`⌋).
- **24. Trava de montagem**, quando o Agrupamento é Kit, o Setor tem `UtilizaKit` e o nó tem filhos.
  Montar é registro próprio ("montei N"), aceito se `N ≤ mínimo, entre os filhos diretos, de
  ⌊quantidade no Setor ÷ QuantidadePorPai⌋`; grava a baixa de cada filho para "montado". A saída do nó
  de Setor `UtilizaKit` é limitada ao total montado, em qualquer passagem.
- **25. Conjunto completo.** Filhos de Kit só entram em Setor `UtilizaKit` em conjuntos completos —
  todos os filhos diretos, na proporção, na mesma movimentação. Sobra que não fecha conjunto sai como
  `Descarte`.
- **26. `QuantidadePorPai`.** Razão por unidade do pai, guardada ao lado da quantidade absoluta.
  Obrigatória em todo Item, nula na Peça. Preenchida pela cópia da receita ou informada no ad-hoc. Sem
  invariante entre as duas quantidades.
- **27. Perda que impede montar.** Sobe até a Peça do topo; as partes não montadas daquela unidade
  saem junto como perda; a reposição é Pedido de Retrabalho, em que o que já existe é marcado pronto
  (sem Roteiro, nasce aguardando coleta, levado normalmente; com filhos, conta como montado).

## 5. Texto para `specs/06-roadmap-mvp.md`

- **Fase 3 — ampliada.** Acrescenta: terminar ≠ mover, estado "aguardando coleta", perfil
  `Movimentador`, tela Tarefas com Itens prontos. Critério de pronto novo: dá para acompanhar em qual
  Setor cada peça está **e** se aguarda coleta, e o Movimentador vê o que tem a levar e registra a
  entrega.
- **Fase 3B — Kit e montagem (nova), logo depois da Fase 3.** Regras 24 a 26: `Setor.UtilizaKit`,
  `EstruturaItem.QuantidadePorPai`, registro de montagem e destino "montado", conjunto completo na
  entrada, aviso Kit pronto na tela Tarefas. Critério de pronto: um Kit de três níveis (o exemplo da
  seção 1) é montado de baixo para cima com montagem parcial; o sistema recusa conjunto incompleto e
  saída acima do montado; o aviso de Kit pronto aparece e some quando o Kit é levado.
- **Fase 5 — ampliada.** Regra 27 (perda que sobe até a Peça, nó pronto no Retrabalho), perda de Item e
  motivo `Descarte`.
- **Fase 3C — Notificação push (nova).** Nota no topo: **executada depois da Fase 5**, como a 1F já
  ficou na fila fora da ordem das letras — o fluxo ponta a ponta vem primeiro, e o push é reforço.
  Critério de pronto: um Movimentador com o celular bloqueado recebe o aviso de Kit pronto, e tocar
  nele abre a tela Tarefas.
- **Nova seção "Fora das fases — dívida: CRUD de Usuário e permissão por Perfil".** Ver seção 8.

## 6. Errata da spec da Fase 2

Bloco no formato que aquela spec já usa (`> **NOTA (data, contexto).**`, anotado e não reescrito —
precedente da nota R2 de 2026-09-11), inserido logo abaixo da §2.1, com rótulo **ERRATA**. Conteúdo:

- a quantidade absoluta continua sendo o que o apontamento movimenta, e continua guardada;
- a razão por unidade do pai, que a §2.1 descartou, **passa a ser guardada também**, em
  `EstruturaItem.QuantidadePorPai`;
- o motivo é **esclarecimento do processo atual da fábrica** — a Solda monta parcialmente, amarrada à
  expedição parcial —, não erro de raciocínio da época: a Fase 2 não tinha essa informação;
- a §2.2 continua valendo: não existe invariante entre as duas quantidades;
- remete a esta spec.

## 7. `specs/03-arquitetura-tecnica.md` e `specs/00-visao-geral.md`

- **`03`:** o item "PWA/offline: não é necessário no MVP" continua valendo para funcionamento sem
  rede. Ganha a ressalva de que o **PWA mínimo para push** (manifesto, service worker sem cache de
  API) está previsto na Fase 3C e não reabre aquela decisão. A lista de pendências de pré-deploy ganha
  as **chaves VAPID**, no mesmo procedimento da `SigningKey`, valendo só a partir da Fase 3C.
- **`00`:** a lista de perfis ganha o **Movimentador** — leva Itens prontos ao próximo Setor e Kits
  completos à Solda.

## 8. Dívida registrada: CRUD de Usuário e permissão por Perfil

Medido em 2026-09-15: `GET/POST /usuarios` consta em `specs/05-api-endpoints.md`, mas uma busca por
`usuarios` e por `UsuarioController`/`UsuariosController` em `src/` não acha nada (a mesma busca por
`setores` acha o `SetoresController`, então ela funciona), e nenhuma fase do roadmap o implementa. Hoje um usuário só nasce
por SQL. O usuário julga a estrutura cada vez mais necessária e decidiu deixá-la para depois, como
dívida cara e importante. São **duas dívidas de custo diferente**, registradas separadas:

- **CRUD de Usuário** (criar conta, ativar, atribuir perfil existente) — **barata**, e fica mais
  urgente com o Movimentador: até existir, cada conta de movimentador nasce por SQL na VPS.
- **Permissão por Perfil fora do código** (o mapeamento perfil → ação sair dos `[Authorize(Roles)]`
  literais) — **cara**; é a que faz perfil novo exigir código e deploy.

Sem fase e sem data.

## 9. Deixado para a spec de cada fase

- **Fase 3:** como "aguardando coleta" é representado no banco, com o lote divisível entre Setores.
- **Fase 3B:** trocar o Tipo do Agrupamento ou a marca `UtilizaKit` com produção em andamento; filho que
  conclui a própria montagem na mesma Solda em que o pai será montado (a "movimentação" seria de Solda
  para Solda); o que a edição de nó da Fase 2 faz com `QuantidadePorPai`.
- **Fase 5:** de qual Setor sai a parte que acompanha a perda, com o lote dividido entre Setores.
- **Fase 3C:** tudo de implementação do push.
