# Fase 5B-2 — Reposição — desenho

Data: 2026-11-05. Branch: `claude/eloquent-mendel-7s7ocs`, a partir da `main` em `d86e7dc`. Desenho conversado com o
usuário em chat, numa sessão de nuvem de 2026-11-05 e 2026-11-06. As decisões do usuário são D1–D9 (seção 2.1); as
chamadas do desenho, que o usuário confere ao ler, são C1–C20 (seção 2.2). O builder check (um revisor lendo a spec como
quem vai planejar) levantou seis perguntas: duas viraram D8 e D9, respondidas pelo usuário em 2026-11-06, e quatro
viraram C17 a C20. O documento escrito foi **aprovado pelo usuário em 2026-11-06**.

**Ponto de partida.** A 5B foi partida em duas em 2026-11-04 (D1 da spec da 5B-1). A 5B-1 (perda e descarte) foi
mesclada em 2026-11-05 (PR #36). Esta é a **5B-2 — Reposição**: o Pedido de Retrabalho, o nó **pronto** (tipo de
movimento `Pronto`, regra 27), a reposição da perda que impede montar e o vínculo do Retrabalho com a reclamação do
cliente e com a peça morta. Depois dela vem a 5C (as pontas do Kit), e a 3C (push) depois da Fase 5.

**O que o código tinha antes desta spec** (lido em `d86e7dc`):

- `dbo.Pedido` já tem `Tipo` (`Fabricacao` | `Retrabalho`), `PedidoOrigemId` e `MotivoRetrabalho`, com
  `CK_Pedido_Tipo`, `CK_Pedido_OrigemObrigatoriaSeRetrabalho`, `CK_Pedido_MotivoRetrabalho` (`ReprovacaoDimensional`,
  `ErroInterno`, `SolicitacaoCliente`, `Perda`) e `CK_Pedido_MotivoSoSeRetrabalho`, e o índice
  `IX_Pedido_PedidoOrigem`. A entidade `Pedido` os mapeia. **Nenhum caso de uso os usa**: o
  `CadastroDePedidoUseCase` grava sempre `Tipo = Fabricacao`.
- `dbo.Perda` é o registro da baixa (uma linha por baixa, regra 38), com `PedidoRetrabalhoId INT NULL` (FK para
  `dbo.Pedido`) **sem uso**. `CK_Perda_Motivo` aceita `PerdaArmazem`, `MortaEmProcesso` e `Descarte`.
- O livro (`dbo.Movimentacao`) **não** tem o tipo `Pronto`. As posições `AguardandoColeta(Setor, passo)`,
  `AguardandoMontagem(Setor)` e `AguardandoConserto(Setor)` existem (`Local.cs`).
- A cópia de estrutura que existe é a **do catálogo** (`PlanejadorDeCopia`, puro, com a guarda de ciclo, profundidade
  e tamanho). Não há cópia de uma árvore **real** de Pedido para outro.
- A pílula "N perdidos" (`rotuloDoSaldo`, `web/src/execucao/formatacao.ts`; `ResumoDePosicoes`) é **neutra**. O tom
  `atencao` (âmbar) é, pelo `CLAUDE.md`, reservado a "atenção (hoje, Pedido pausado)".
- Não há registro nenhum de reclamação do cliente.

## 1. O que se quer, e o que conta como pronto

**Como funciona na fábrica** (palavras do usuário, resumidas):

- O Retrabalho é, de regra, **a mesma Peça** de novo (D1).
- Quando sobra parte boa de uma unidade perdida, ela **sempre** vai para a reposição (D2).
- Às vezes a fábrica decide **não** repor (o cliente aceita menos, a peça morta é sucata). Quem decide é o **PCP**,
  com **justificativa obrigatória** (D3).
- Quando o cliente reclama de uma peça já entregue, **a peça volta**. Na maior parte dos casos ela é **consertada**;
  com erro grosseiro (erro de projeto que impede montar), é plausível **refazê-la inteira** (D4).

**Pronto é** (critério do `06`: fluxo ponta a ponta — cadastro → produção → expedição/perda → aferição → (se aplicável,
e só quando o usuário decidir) retrabalho):

- da baixa por perda de uma Peça (ou de um Item, com a Peça do topo junto, `incluirTopo`) e da Peça "morta" na
  expedição, a Qualidade ou o PCP abrem um Pedido de Retrabalho, que nasce como cópia da Peça perdida, para as
  unidades perdidas, ligado ao original;
- o PCP usa a sobra boa daquela árvore como **pronto** no Retrabalho, e ela sai do original na mesma escrita;
- a Qualidade, o PCP ou o Administrador registram uma reclamação do cliente sobre uma Peça já expedida, e dela se
  abre um Retrabalho para **consertar** (a peça devolvida entra aguardando conserto) ou para **refazer**;
- o que falta repor aparece em **âmbar** até ganhar Retrabalho ou um "não repor" do PCP, com justificativa;
- o Retrabalho anda, é aferido, expedido e fecha como qualquer Pedido, e o original **nunca reabre**.

## 2. Decisões

### 2.1 Do usuário

| # | Decisão |
|---|---|
| D1 | O Retrabalho nasce como **cópia da Peça original** — a árvore, os Roteiros e os materiais —, para as N unidades a repor, e é **editável** como qualquer Pedido `Aberto` (abordagem A da seção 9.1). |
| D2 | A sobra boa de uma unidade perdida **sempre** vai para a reposição. "Pronto" é **quantidade** de um nó, não marca: o PCP usa a sobra viva da árvore daquela Peça, com a tela sugerindo o máximo que o Retrabalho precisa; a escrita é **uma só** — no original, uma baixa da sobra apontando o Retrabalho; no Retrabalho, os `Pronto` —, e o estorno desfaz as duas juntas. Quem não quer reaproveitar não marca nada. |
| D3 | Quem decide **não repor** é o **PCP**, com **justificativa obrigatória**. |
| D4 | A reclamação do cliente tem dois destinos: **consertar** (o caso comum: a peça volta e passa por um Setor) ou **refazer inteira** (erro grosseiro). |
| D5 | Os dois destinos vão **pelo Retrabalho** (abordagem 1 da seção 9.2). No original fica só o **registro da reclamação**; o livro do original não muda, o que foi entregue continua entregue, e o original **nunca reabre**. |
| D6 | "O tratamento de estrutura alterada no meio da produção" (regra 24; D1 da 3B), que não tinha dono, vai para a **5C**. |
| D7 | Uma spec, **um PR**, dois planos (backend e front), no molde da 5B-1 (seção 9.3). |
| D8 | A sobra de Kit que **já está na Solda** (aguardando montagem num Setor `UtilizaKit`) também vira pronto, **nesta fase**: no Retrabalho, ela é um conjunto **em formação**, que os irmãos entram para completar (seção 4.5). Não fica para a 5C. |
| D9 | **Um Retrabalho por baixa ou por reclamação**, sem juntar vários alvos num só: cada um pode ter um motivo diferente, e a fábrica quer acompanhá-los separadamente. |
| A4 | (Decisão de 2026-11-05, na conferência da 5B-1: "Âmbar só na 5B-2".) O estado "perda **a repor**" ganha o âmbar `atencao`. A pílula "N perdidos" continua neutra. |

### 2.2 Do desenho (o usuário confere)

| # | Chamada |
|---|---|
| C1 | **"A repor" conta no nível da Peça**, que é o que o cliente deixa de receber: unidades de Peça que saíram por uma baixa de `PerdaArmazem` ou `MortaEmProcesso` não estornada, **ou** que voltaram numa reclamação não estornada, e que ainda não têm nem Retrabalho nem "não repor". A perda de um Item que não trava a montagem não acende nada; a que trava acende pela Peça do topo que a mesma baixa leva (`incluirTopo`). `Descarte` nunca acende. |
| C2 | **O vínculo é por baixa e por reclamação**, não por nó: `Perda.PedidoRetrabalhoId` (já existe) e `Reclamacao.PedidoRetrabalhoId` (nova). Isso responde a terceira pergunta da seção 9 da spec do Kit ("se as linhas preenchem `dbo.Perda.PedidoRetrabalhoId`"): `dbo.Perda` deixou de ter uma linha por nó na 5B-1, e o vínculo que a regra 27 recusava era o **por nó**. A regra 27 continua valendo: as linhas do livro não ganham vínculo. |
| C3 | **A decisão de repor vale para a baixa (ou a reclamação) inteira**: ou ela ganha **um** Retrabalho, ou ganha um "não repor". Não há reposição parcial: quem quer repor menos abre o Retrabalho e diminui a quantidade nele (D1, a cópia é editável enquanto `Aberto`). |
| C4 | **Motivo novo de baixa, `Reaproveitado`** ("foi para o Retrabalho"), para a sobra que sai do original como pronto (D2). Não é `Descarte` (que "não se repõe" e é o que saiu do projeto) e nunca acende o âmbar. Só nasce pela ação "Usar a sobra" (seção 4.4), nunca pelo "Dar baixa". A baixa `Reaproveitado` aponta o Retrabalho em `Perda.PedidoRetrabalhoId`. |
| C5 | **O motivo do Retrabalho vem sugerido pela origem** e quem abre pode trocar: baixa por perda → `Perda`; baixa da não conforme ("matar", origem `NaoConforme`) → `ReprovacaoDimensional`; reclamação → `SolicitacaoCliente`; abertura avulsa, do Pedido → sem sugestão (o `ErroInterno` mora aqui). |
| C6 | **Quem abre e quem cadastra** (regra 27, mantida): a Qualidade, o PCP ou o Administrador **abrem** — o Retrabalho nasce `Aberto`, com a cópia. O PCP ou o Administrador **cadastram**: editam a estrutura e usam a sobra (seção 4.4). Registrar a reclamação: Qualidade, PCP ou Administrador. "Não repor" e desfazê-lo: PCP ou Administrador (D3). |
| C7 | **Número e prazo.** O cliente vem copiado do original. O número vem sugerido como `<número do original>-R1`, `-R2`… (o primeiro livre), editável, e continua único (a guarda de sempre). A **data de entrega** vem, por padrão, a **do original**, editável no cadastro: é o que a regra 33 já diz (D4 da spec da data de entrega). *Corrige o que foi dito em chat ("sem padrão"), que contrariava a regra 33.* |
| C8 | **Retrabalho de Retrabalho** vale e não tem regra própria: a perda dentro de um Retrabalho abre outro, com origem no Pedido onde a perda aconteceu (`PedidoOrigemId` = o Retrabalho). |
| C9 | **O original nunca reabre e não muda**: nem o status, nem as datas de conclusão, nem a estrutura. O que ele ganha são as seções "Retrabalhos" e "Reclamações" no detalhe. A única escrita no livro do original é a baixa `Reaproveitado` da sobra (C4), que é baixa de sobra viva de Item e por isso é aceita também num original `Concluido` (como o `Descarte` de C24 da 5B-1). |
| C10 | **O Retrabalho anda e fecha como o Pedido de fabricação** (regras 13, 16, 28 a 31, 35 a 38): Fila, Tarefas, Separação, aferição (a marca `RequerRelatorioDimensional` é copiada), remessa, baixa, pausa, fechamento. O que ele tem de próprio é só o nascimento (seções 4.3 a 4.7). |
| C11 | **`Pronto` não tira o Retrabalho de `Aberto`** e não é o início real (regra 14): é a peça que já existia, não fabricação nova. O Pedido passa a `EmProducao` no primeiro `Inicio`, como sempre (regra 28). É isso que deixa o PCP continuar editando a estrutura e usando a sobra depois do pronto. |
| C12 | **"Usar a sobra" só com o Retrabalho `Aberto`** (antes do primeiro início). A parte da unidade perdida que ainda estava num Setor (`NoSetor`) quando a baixa aconteceu só vira sobra quando termina; o PCP pode esperar, porque o `Pronto` não move o status (C11). Registrado como limitação (seção 8). |
| C13 | **`Devolucao`, tipo de movimento novo**, de `AIniciar` para `AguardandoConserto(Setor)`, no Retrabalho de consertar (seção 4.7). Diferente do `Pronto`, ela **conta como início**: o Retrabalho de consertar passa a `EmProducao` na abertura e o início real é a data da devolução. Não há o que cadastrar depois. |
| C14 | **O nó copiado guarda de que nó veio**: coluna nova `EstruturaItem.OrigemEstruturaItemId INT NULL` (FK para `dbo.EstruturaItem`), preenchida só na cópia do Retrabalho. É o que liga a sobra do original ao nó do Retrabalho no "Usar a sobra", e sobrevive à edição do PCP (o nó acrescentado à mão não tem origem e não recebe pronto). |
| C15 | **O "não repor" é registro, e desfazê-lo deixa rastro**: tabela nova `dbo.DispensaDeReposicao` (seção 3.4), só de inclusão, com o estorno no próprio registro (quem e quando), no molde da liberação da 5A. Justificativa obrigatória, aparada, até 500 caracteres. |
| C16 | **Onde o âmbar aparece**: a pílula **"N a repor"** (`atencao`) no cabeçalho da `PedidoDetalhePage` do original e na `LinhaDePedido` da lista (ao lado de "Pausado" e "Atrasado"), e a pílula "A repor" na linha de cada baixa e de cada reclamação que ainda está a repor. A contagem vem do servidor (`aRepor` no DTO do Pedido); a tela não recalcula. O tom é o `atencao` de sempre; se a combinação de tinta e fundo for nova, o par entra em `contraste.test.ts`. |
| C17 | **O pronto de um nó com filhos conta como montado** (a letra da regra 27: "total montado = quantidade"). O `FaltaMontar` do código é `Quantidade − TotalMontado − PerdidoDeAIniciar` (`CalculadoraDeExecucao.FaltaMontar`), e o `Pronto` não muda nenhum dos três termos: por isso o `Pronto` de um nó com filhos **soma ao total montado** dele, e o `FaltaMontar`, as Tarefas, a sobra, o "Dá para montar", o teto do Kit e o C6 da baixa seguem dele. Ressalva do builder check: a spec dizia que o pronto "já entrava" no `FaltaMontar`, e não entra. |
| C18 | **Os filhos que o pronto dispensa saem por baixa, não por exclusão**: o "Usar a sobra" grava, no Retrabalho e na mesma transação, a cascata C6 da 5B-1 — os filhos além do que o pai pronto ainda precisa vão a `Perdido`, com motivo `Descarte`, numa baixa que se estorna junto da `Reaproveitado`. **Emenda de 2026-11-10 (decisão do usuário na conferência):** o excesso do filho sai primeiro do "a iniciar" e, o que faltar, das posições em que está o pronto dele de um uso anterior da sobra, onde quer que esteja agora, inclusive onde uma entrega o levou (num Retrabalho `Aberto` nada iniciou, então todo saldo fora de "a iniciar" e de `Perdido` veio de um `Pronto` de um uso anterior); a cascata da baixa da 5B-1 não muda. Não se exclui nó (o `ExcluirNo` recusa nó com separação, `NoComSeparacao`, que pode existir num Pedido `Aberto`), e o estorno traz tudo de volta. O planejado do material **não** se reescala: é a dívida C26 da 5B-1, a mesma. |
| C19 | **O servidor limita o que sai do original à sobra viva** (regra 30) do nó, por posição, e não só a tela — diferente de C17 da 5B-1, porque tirar além da sobra criaria no original uma falta que nunca fica âmbar (não é baixa de perda). 409 `AlemDaSobra` (nome a confirmar no plano). |
| C20 | **Estornos que tirariam o chão de um alvo já resolvido são recusados**: o estorno de uma baixa `PerdaArmazem`/`MortaEmProcesso` que já tem Retrabalho, ou dispensa vigente (desfaz-se a dispensa antes), é 409; e o estorno de uma remessa que deixaria o expedido de uma Peça abaixo do que já foi reclamado (não estornado) é 409. Como não há estorno da abertura (seção 11), a baixa reposta não se estorna mais (seção 8). |

## 3. Modelo de dados

### 3.1 Livro: dois tipos novos

| Tipo | De | Para | Aponta | Onde |
|---|---|---|---|---|
| `Pronto` | `AIniciar` | a **mesma posição física** da sobra no original: `AguardandoColeta(Setor, passo)` ou `AguardandoMontagem(Setor)` | `PerdaId` (a baixa `Reaproveitado` no original) | no Retrabalho |
| `Devolucao` | `AIniciar` | `AguardandoConserto(Setor)` | `ReclamacaoId` (coluna nova) | no Retrabalho de consertar |

O estorno de cada um é o `Estorno` de sempre, de volta a `AIniciar`, apontando o mesmo registro.

`CK_Movimentacao_Tipo` ganha os dois; `CK_Movimentacao_RegistroCoerente` ganha `(Tipo = 'Pronto' AND PerdaId IS NOT
NULL AND os outros nulos)` e `(Tipo = 'Devolucao' AND ReclamacaoId IS NOT NULL AND os outros nulos)`; `ReclamacaoId`
entra na soma do `Estorno` (no máximo um registro) e nas linhas que exigem os outros nulos. Coluna nova
`Movimentacao.ReclamacaoId INT NULL`, FK `FK_Movimentacao_Reclamacao`, índice filtrado como o de `PerdaId`.

**Por que a mesma posição física, e não sempre "aguardando coleta".** A regra 27 diz que o nó pronto "nasce aguardando
coleta". A sobra da regra 30 está em dois lugares: no fim do Roteiro, aguardando coleta (aí o pronto nasce igual), ou
**já entregue** à montagem do pai, `AguardandoMontagem` no Setor onde o pai começa. A spec do Kit (seção 3.6) descartou
o pronto "nascer dentro do Setor de montagem" por ser movimento que ninguém fez e por ambiguidade de Setor; aqui as
duas razões caem: a peça **está** lá, e o Setor é o dela. Como o Retrabalho copia os Roteiros (D1), o Setor e o passo
existem nele. Se o PCP editou o Roteiro antes de usar a sobra e a posição já não existe, o "Usar a sobra" recusa aquele
nó (400, código a nomear no plano). **Esta chamada revê a letra da regra 27 e o usuário confere.**

### 3.2 `dbo.Perda`

- `CK_Perda_Motivo` ganha `Reaproveitado` (C4).
- `PedidoRetrabalhoId` passa a ser usada: na baixa de `PerdaArmazem`/`MortaEmProcesso`, "reposta por"; na de
  `Reaproveitado`, "foi para". Nunca na de `Descarte`. Guarda: `CK_Perda_ReaproveitadoTemRetrabalho`
  (`Motivo <> 'Reaproveitado' OR PedidoRetrabalhoId IS NOT NULL`) e `CK_Perda_DescarteSemRetrabalho`.

### 3.3 `dbo.Reclamacao` (nova)

| Coluna | Tipo | O que é |
|---|---|---|
| `Id` | `INT IDENTITY` | PK |
| `PedidoId` | `INT NOT NULL`, FK | o Pedido que expediu a Peça |
| `EstruturaItemId` | `INT NOT NULL`, FK | a **Peça** (nó de topo) reclamada |
| `Quantidade` | `DECIMAL(18,4) NOT NULL` | quantas voltaram (`> 0`) |
| `Descricao` | `NVARCHAR(500) NOT NULL` | o que o cliente disse; obrigatória, aparada |
| `PedidoRetrabalhoId` | `INT NULL`, FK | o Retrabalho que a atende |
| `DataHora`, `UsuarioId` | | a escrita e o autor da sessão |
| `EstornadaEm`, `EstornadaPorUsuarioId` | `NULL` | o estorno do registro |

Guarda de aplicação: a soma das reclamações não estornadas de uma Peça não passa do **expedido** dela (409
`SaldoInsuficiente`). Só Peça (400 `NaoEhPeca`). A reclamação é aceita em qualquer status do original, inclusive
`Concluido`, que é o caso comum (C9). O estorno do registro é aceito só enquanto ela não tem Retrabalho nem "não repor"
vigente (409, código a nomear no plano); quem estorna: o autor, o PCP ou o Administrador.

### 3.4 `dbo.DispensaDeReposicao` (nova)

`Id`, `PerdaId INT NULL` FK, `ReclamacaoId INT NULL` FK (exatamente um preenchido, `CK_DispensaDeReposicao_Alvo`),
`Justificativa NVARCHAR(500) NOT NULL`, `DataHora`, `UsuarioId`, `EstornadaEm`, `EstornadaPorUsuarioId`. Índice único
filtrado por alvo onde `EstornadaEm IS NULL`: no máximo uma dispensa vigente por baixa ou reclamação. "Não repor" é
recusado se o alvo já tem Retrabalho, e "Abrir retrabalho" é recusado se o alvo tem dispensa vigente (409 nos dois).
Só baixa de `PerdaArmazem`/`MortaEmProcesso` não estornada aceita dispensa.

### 3.5 `dbo.EstruturaItem`

Coluna nova `OrigemEstruturaItemId INT NULL`, FK para `dbo.EstruturaItem` (C14).

### 3.6 Migração — `db/alter-fase-5b2.sql`

Mesmo formato dos anteriores (idempotente, `-b -f 65001`): cria `dbo.Reclamacao` e `dbo.DispensaDeReposicao`;
acrescenta `Movimentacao.ReclamacaoId` e `EstruturaItem.OrigemEstruturaItemId`; recria `CK_Perda_Motivo`, os dois
`CK_Perda_*` novos e as constraints do livro da seção 3.1 (`DROP` se existir e `ADD`, sempre). `dbo.Pedido` não
muda. O `CLAUDE.md` ganha o comando, no molde do da 5B-1.

## 4. Regras de domínio

### 4.1 A repor (C1, C16)

Unidades a repor de um Pedido = Σ, sobre as baixas de `PerdaArmazem`/`MortaEmProcesso` não estornadas sem Retrabalho
e sem dispensa vigente, das unidades de **Peça** (nó de topo) que a baixa levou a `Perdido`; mais Σ das `Quantidade` das
reclamações não estornadas sem Retrabalho e sem dispensa vigente. Calculado na leitura, no servidor. Um Retrabalho
"cobre" a baixa ou a reclamação inteira (C3), seja qual for a quantidade dele depois da edição.

### 4.2 Abrir o Retrabalho

Três portas, uma rota (`POST /pedidos/{id}/retrabalhos`, `{id}` = o original):

- **da baixa** (`perdaId`): a Peça é a Peça do topo da baixa, a quantidade sugerida são as unidades de Peça que a baixa
  levou; só baixa de `PerdaArmazem`/`MortaEmProcesso` não estornada, sem Retrabalho e sem dispensa. Baixa sem unidade
  de Peça (perda de Item sem `incluirTopo`) não tem "Abrir retrabalho": não há Peça a repor (C1).
- **da reclamação** (`reclamacaoId` e `destino`: `Consertar` com `setorId`, ou `Refazer`): a Peça e a quantidade são as
  da reclamação.
- **avulsa** (`estruturaItemId` de uma Peça do original e `quantidade`): sem vínculo; o motivo é escolhido (C5).

O corpo traz também `numero`, `dataEntrega` e `motivoRetrabalho` (sugeridos pela tela, C5 e C7). Numa transação: cria o
Pedido `Tipo = Retrabalho`, `PedidoOrigemId`, `MotivoRetrabalho`, `Status = Aberto`; cria **um** Agrupamento com o nome
e o Tipo (Kit/Avulso) do Agrupamento da Peça original; copia a árvore (4.3), ou só a Peça (4.7, consertar); grava o
vínculo (`Perda.PedidoRetrabalhoId` ou `Reclamacao.PedidoRetrabalhoId`); no consertar, grava a `Devolucao`. Trava a
baixa ou a reclamação (e o Pedido original) para que duas aberturas simultâneas deem um 201 e um 409.

### 4.3 A cópia da árvore (D1)

- A Peça do Retrabalho tem a quantidade escolhida (N). Cada descendente tem `N × produto das QuantidadePorPai` do
  caminho até ele — a quantidade que N unidades pedem, e não a do original. Os materiais (o planejado) se reescalam
  pela mesma razão; os Roteiros copiam-se como estão; `ComponenteId`, `Descricao`, `QuantidadePorPai` e
  `RequerRelatorioDimensional` copiam-se.
- O nó que saiu do projeto (baixa `tudo` de `Descarte` não estornada no próprio nó, C22 da 5B-1) **não** entra na
  cópia, com a árvore abaixo dele: saiu do projeto do cliente.
- Função pura no molde do `PlanejadorDeCopia`, com os mesmos tetos de profundidade e de nós e a mesma guarda de faixa
  da coluna (`QuantidadeForaDaColunaException` → 400).
- Todo nó copiado grava `OrigemEstruturaItemId` (C14) e nasce **a iniciar** (regra 28).

### 4.4 Usar a sobra (D2, C4, C11, C12)

Ação do PCP no Retrabalho `Aberto` (`POST /pedidos/{id}/sobra-reaproveitada`, `{id}` = o Retrabalho):

- **A prévia** (rota de leitura, sem gravar, no molde de C14 da 5B-1) lista, para cada nó do Retrabalho com
  `OrigemEstruturaItemId`, a **sobra viva** do nó de origem no Pedido de origem (regra 30: aguardando coleta no fim do
  Roteiro e excesso aguardando montagem), por posição, e **sugere** usar, descendo do topo, `min(sobra, o que o nó
  ainda precisa)`. O que um nó precisa depois do pronto do pai é `(a iniciar do pai depois do pronto) ×
  QuantidadePorPai`; por isso a sugestão desce de cima para baixo.
- **Confirmar**, com as quantidades por nó e posição que o PCP deixou: numa transação, (1) uma baixa no original de
  motivo `Reaproveitado`, apontando o Retrabalho, com uma linha `Perda` por nó e posição, e (2) no Retrabalho, um
  `Pronto` por linha, para a mesma posição física (seção 3.1), apontando aquela baixa. Por nó, o pronto não passa do a
  iniciar dele (409 `SaldoInsuficiente`); por posição, a baixa não passa do saldo do original (409, o de sempre).
- **O nó com filhos pronto já está montado** (regra 27, C17): o `Pronto` dele soma ao total montado, e o
  `FaltaMontar` desconta o pronto por isso.
- **Os descendentes que o pronto dispensa saem por baixa** (C18): na mesma transação, a cascata C6 da 5B-1 leva a
  `Perdido`, com motivo `Descarte`, o que cada filho tem em produção além de `FaltaMontar(pai) × QuantidadePorPai`,
  descendo a árvore. **Emenda de 2026-11-10 (decisão do usuário na conferência):** o excesso sai primeiro do "a
  iniciar" — preserva a peça física já pronta sempre que ela ainda serve ao pai — e, o que faltar, das posições em
  que está o pronto de um uso anterior da sobra, onde quer que esteja agora, inclusive onde uma entrega o levou, na
  ordem dos saldos da calculadora (a de `Posicoes`, depois passo e Setor);
  cada posição tirada é uma linha `Perda` própria da baixa de `Descarte`. Estornar o primeiro uso enquanto o segundo
  vale é 409 `ProntoJaAndou` (o pronto do primeiro já não está **todo** nas posições em que os `Pronto` dele o puseram); estornar o segundo devolve esses
  filhos à posição de onde a baixa ligada os tirou. Nada se exclui da estrutura.
- **O que sai do original não passa da sobra viva** do nó de origem, por posição (C19).
- **Estorno**: pela rota da baixa `Reaproveitado` (a de sempre, C7 da 5B-1), **inteiro**, desfazendo também os
  `Pronto` que a apontam e a baixa de `Descarte` da cascata (C18), numa transação; recusado se algum pronto já andou
  (409, código a nomear no plano).
- Pode-se usar a sobra **mais de uma vez** enquanto o Retrabalho estiver `Aberto` (C12): cada uso é uma baixa
  `Reaproveitado`.

### 4.5 O pronto de Kit que já está na Solda (D8)

O caso: a sobra de um filho de pai **Kit** está aguardando montagem num Setor `UtilizaKit` (um conjunto que ficou
incompleto lá dentro, porque um irmão morreu, ou porque o `incluirTopo` levou a Peça). No Retrabalho, o `Pronto` a põe
na mesma posição (seção 3.1). Pela conta de hoje, ela entraria em `ConjuntosAEspera` (contado pelo filho mais
adiantado) e zeraria o `TetoDeEntrada`: o irmão fabricado nunca entraria, porque a entrada exige conjunto completo
(regra 25), e o Retrabalho travaria. Exemplo: pai de 1 unidade, filhos A e B de razão 1, A pronto na Solda; B não entra.

A regra nova: **no Retrabalho, o que um `Pronto` pôs em `AguardandoMontagem` forma conjuntos em formação**, e a
entrega de um irmão para a montagem aceita, além dos conjuntos completos de sempre (até o teto), **a quantidade que
completa os conjuntos em formação**: para cada filho, até `(unidades em formação) × QuantidadePorPai(filho) − o que ele
já tem aguardando montagem naquele Setor`. Fora disso, a regra 25 vale como está. A fórmula exata, onde ela entra
(`ConjuntosAEspera`/`TetoDeEntrada`, o `ConjuntoCompleto` da entrega, o cartão do Kit nas Tarefas) e a prova de que o
Pedido de fabricação não muda de comportamento são do plano. **O que é da 5C e continua lá**: o conjunto que perde parte
dentro da Solda **no próprio Pedido** (o resto da D5 da 3B, C21 da 5B-1) e a espera desalinhada; esta regra só cobre o
conjunto que um `Pronto` começou.

### 4.6 Não repor (D3, C15)

`POST /baixas/{id}/dispensa` e `POST /reclamacoes/{id}/dispensa`, com `justificativa`; o desfazer é
`POST /dispensas/{id}/estorno`. PCP ou Administrador. O efeito é só tirar o alvo do "a repor" (4.1).

### 4.7 A reclamação (D4, D5, C13)

- **Registrar** (`POST /pedidos/{id}/reclamacoes`): Peça, quantidade (até o expedido menos o já reclamado),
  descrição. Não mexe no livro do original (C9) e não reabre nada.
- **Consertar**: o Retrabalho tem **só a Peça** (sem filhos — ela volta montada, e os filhos nem precisam existir, como
  no nó pronto da regra 27), com o Roteiro copiado e sem materiais; a `Devolucao` põe a quantidade inteira em
  `AguardandoConserto` no Setor escolhido. Daí segue o conserto da regra 35 sem nada novo: o Movimentador leva, o
  operador termina, volta ao local de expedição como `NaExpedicao`, é medida se a Peça exige, e sai por remessa do
  Retrabalho.
- **Refazer**: a cópia da árvore (4.3), toda a iniciar. A peça devolvida é sucata: fica só no registro da reclamação, e
  não há sobra a usar (a Peça montada não se desmonta).
- **Estorno da `Devolucao`**: pela rota genérica, enquanto a quantidade não andou. Não há estorno da abertura (seção
  8): estornada a `Devolucao`, o Retrabalho fica com a Peça a iniciar.

### 4.8 O que muda no fechamento

Nada na conta: o Retrabalho fecha pela regra 13 como qualquer Pedido. O `Pronto` conta como em produção (é posição em
produção). O original continua como estava (C9).

## 5. Backend

### 5.1 Rotas (nomes finais no plano)

| Rota | O que faz | Quem |
|---|---|---|
| `POST /pedidos/{id}/retrabalhos` | abre (4.2) | Qualidade, PCP, Admin |
| `GET /pedidos/{id}/retrabalhos` | os Retrabalhos que têm o Pedido como origem | todos que leem Pedido |
| `GET /pedidos/{id}/sobra-reaproveitavel` | a prévia do 4.4 | PCP, Admin |
| `POST /pedidos/{id}/sobra-reaproveitada` | usa a sobra (4.4) | PCP, Admin |
| `POST /pedidos/{id}/reclamacoes`, `GET` | registra e lista | Qualidade, PCP, Admin / leitura |
| `POST /reclamacoes/{id}/estorno` | estorna o registro | autor, PCP, Admin |
| `POST /baixas/{id}/dispensa`, `POST /reclamacoes/{id}/dispensa`, `POST /dispensas/{id}/estorno` | não repor e desfazer | PCP, Admin |

O DTO do Pedido ganha `tipo`, `pedidoOrigem` (Id e número), `motivoRetrabalho` e `aRepor`; o da baixa e o da
reclamação ganham `pedidoRetrabalho` (Id e número), `dispensa` (vigente, com justificativa, autor e hora) e `aRepor`
(booleano).

### 5.2 Contrato de erro

Os códigos de sempre onde servem (`SaldoInsuficiente`, `NaoEhPeca`, `PedidoFechado`, `JaEstornado`,
`TextoLongoDemais`, `Validacao`); os novos — além da sobra viva (C19), estorno de baixa reposta ou dispensada e de
remessa abaixo do reclamado (C20), alvo já reposto, alvo dispensado, alvo sem Peça a repor, Retrabalho fora
de `Aberto` no "Usar a sobra", posição de pronto que não existe no Roteiro copiado, pronto que já andou — nomeados no
plano e listados em `specs/05`.

## 6. Front

- **`PedidoDetalhePage` do original:** pílula "N a repor" no cabeçalho; seções **"Reclamações"** (com "Registrar
  reclamação" e, por linha, "Abrir retrabalho", "Não repor", "Estornar") e **"Retrabalhos"** (número, motivo, status,
  link); na seção "Baixas" que já existe, por linha a repor, "Abrir retrabalho" e "Não repor", e a pílula "A repor"; a
  baixa reposta mostra "Reposta por <número>", a dispensada "Não repor: <justificativa>" com o "Desfazer".
- **"Abrir retrabalho" do Pedido**, no cabeçalho, para a porta avulsa.
- **O painel de abertura**: número (sugerido), data de entrega (a do original), motivo (sugerido), quantidade (da
  porta, editável na avulsa), e, da reclamação, "Consertar" (com o Setor) ou "Refazer". Confirmar leva ao detalhe do
  Retrabalho.
- **`PedidoDetalhePage` do Retrabalho:** "Retrabalho de <número do original> — <motivo>" no cabeçalho; com o Pedido
  `Aberto`, o botão **"Usar a sobra"** (PCP), que abre a prévia com as quantidades sugeridas e editáveis.
- **Um Retrabalho por alvo** (D9): a baixa ou a reclamação com Retrabalho mostra "Reposta por <número>" e não oferece
  outro; não há "juntar a um Retrabalho aberto".
- **`LinhaDePedido`:** pílula "N a repor" (`atencao`) e a marca "Retrabalho" (tom neutro) no Pedido de Retrabalho.
- O `CLAUDE.md` amplia a regra de cor: `atencao` passa a ser "Pedido pausado **e** reposição pendente".

## 7. Testes

Como na 5B-1: TDD; a conta pura da cópia (4.3) e a da sugestão do pronto (4.4) testadas sem banco; as rotas pela
`Api.Tests`; as constraints pela `Infrastructure.Tests`; os testes que provam regra medidos por mutação na review de
branch. Massa de teste com nó **sem** descrição própria (lição da Fase 4) e com sobra nas duas posições
(`AguardandoColeta` e `AguardandoMontagem`). Data de entrega de teste que não deve atrasar é `2099-12-31`. Conferência
do usuário no navegador, com massa própria (`CONF-5B2`), **antes** da review de branch.

## 8. Riscos conhecidos, aceitos

- A parte da unidade perdida que ainda estava em processo vira sobra só quando termina; se o Retrabalho já começou,
  ela não vira pronto e fica como sobra do original, a descartar (C12).
- A baixa de perda já reposta não se estorna mais (C20): se a peça "sumida" aparecer depois, ela fica no original como
  está, e quem quiser a dá baixa de `Descarte`.
- Não há "desfazer a abertura" do Retrabalho: aberto por engano, o PCP faz o "Descartar tudo" dele (5B-1), e ele
  conclui; o vínculo com a baixa ou a reclamação **fica**, e o âmbar dela não volta. Corrigir isso exige estorno de
  abertura, que fica fora (seção 11).
- O Retrabalho "cobre" o alvo inteiro mesmo se o PCP diminuir a quantidade dele (C3): o que não for reposto deixa de
  aparecer como a repor.

## 9. Abordagens consideradas

### 9.1 Como nasce a estrutura do Retrabalho

- **A — cópia da Peça original** (escolhida, D1): menos digitação, é a mesma peça; custa uma cópia de árvore real, que
  não existe.
- **B — Pedido vazio, cadastrado pelo caminho de sempre** (catálogo, import do BOM): quase nada novo de estrutura, mas o
  PCP redigita a árvore e marca o pronto nó a nó.
- **C — as duas**: é a A mais a edição que já existe; não precisa ser opção à parte.

### 9.2 Onde se conserta a peça devolvida pelo cliente

- **1 — pelo Retrabalho** (escolhida, D5): o original nunca reabre, e tudo o que volta passa por Retrabalho; custa o
  tipo `Devolucao`.
- **2 — no próprio original**, de `Expedido` de volta a aguardando conserto: sem movimento novo, mas reabre um Pedido
  entregue, mexe na data de conclusão e nos KPIs de entrega da Fase 6, e contraria a regra 12 e a spec da 5A.

### 9.3 Como entregar

- **1 — uma spec, um PR, dois planos** (escolhida, D7): uma conferência e uma review de branch; as partes partilham o
  vínculo, o âmbar e o "não repor".
- **2 — uma spec, dois PRs** (reposição da baixa; depois a reclamação): PRs menores, mas duas conferências e duas
  reviews, e o critério de pronto só fecha no segundo.
- Duas specs: descartada, repetiria as regras comuns.

## 10. Ajustes de documentação (na mesma branch)

- `specs/01`: regra 12 (o vínculo com a reclamação, que a nota de 2026-10-20 deixou para cá); regra 14 (a `Devolucao`
  conta como início; o `Pronto` não); regra 17 (o vínculo por baixa, C2, e o motivo `Reaproveitado`); regra 27 (pronto é
  quantidade, a posição física, conta como montado, C2, C17 e 4.4); regra 25 (o conjunto em formação do pronto, 4.5);
regra 28 (o `Pronto` não é entrada do operador, C11); regra 38 (o motivo
  `Reaproveitado`, que só nasce do "Usar a sobra"); **regra nova 39** — reposição: a repor, não repor, a reclamação, a
  abertura, a cópia; a regra 24 aponta a 5C para a estrutura alterada no meio da produção (D6).
- `specs/02`: `dbo.Reclamacao`, `dbo.DispensaDeReposicao`, as colunas e constraints novas.
- `specs/04`: fluxo 5 reescrito (abrir, usar a sobra, não repor, reclamação), com as notas antigas resumidas.
- `specs/05`: as rotas da seção 5.1 e o contrato de erro.
- `specs/06`: a 5B-2 com spec; a 5C ganha a estrutura alterada no meio da produção (D6).
- `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`: errata apontando C2 (a terceira pergunta
  da seção 9, respondida) e a posição física do pronto (seção 3.1).
- `CLAUDE.md`: o comando do `db/alter-fase-5b2.sql`, a regra de cor (C16), e as contagens que mudarem (remedidas).

## 11. Fora do escopo

Estorno da abertura do Retrabalho; reposição parcial de uma baixa (C3); juntar várias baixas ou reclamações num
Retrabalho só (D9); estrutura alterada no meio da produção (5C,
D6); KPIs de retrabalho e de reclamação (Fase 6); o status `Cancelado` (C20 da 5B-1).
