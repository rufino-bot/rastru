# Fase 5B-1 — Perda e descarte — desenho

Data: 2026-11-03. Branch: `claude/happy-wright-14yv9r`, a partir da `main` em `bb8626e`. Desenho conversado com o
usuário em chat, numa sessão de nuvem de 2026-11-03, com um esboço das telas (página privada, fora do repositório:
https://claude.ai/artifact/8L2ur2ErzMMHufFznVMiFn). As decisões do usuário são D1–D14 (seção 2); as chamadas
do desenho, que o usuário confere ao ler, são C1–C27. O builder check (um revisor lendo a spec como quem vai planejar)
levantou oito perguntas: duas viraram D12 e D13, respondidas pelo usuário em 2026-11-04, e seis viraram C22 a C27. O documento escrito foi **aprovado pelo usuário em 2026-11-04**. D14 entrou na execução, no mesmo dia, depois da review da conta da baixa.

**Ponto de partida.** A seção «Fase 5B — Perda e reposição» de `specs/06-roadmap-mvp.md` junta dois assuntos.
O usuário aprovou dividi-la em duas specs, cada uma com plano e PR próprios, **nesta ordem** (D1):

- **5B-1 — Perda e descarte** (esta spec): dar fim à quantidade que não vai ser expedida, que é a perda, o
  descarte da sobra, o descarte do que saiu do projeto com o Pedido rodando e o "matar" da não conforme. O
  fechamento passa a contar o perdido. Fecha o item **obrigatório antes do primeiro uso real**.
- **5B-2 — Reposição**: o Pedido de Retrabalho (que ainda não existe em código nenhum), o nó **pronto** (tipo
  `Pronto`, regra 27), a reposição da perda que impede montar e o vínculo do Retrabalho com a reclamação do
  cliente (`SolicitacaoCliente`) e com a peça morta (`ReprovacaoDimensional`).

A 5C (as pontas do Kit) continua depois da 5B, e a 3C (push) depois da Fase 5.

**O que o código tinha antes desta spec** (lido em `bb8626e`):

- `dbo.Perda` existe **só no schema**: sem entidade, sem mapeamento EF, sem rota, sem tela. Tem uma linha por
  nó (`EstruturaItemId`, `Quantidade`, `SetorId` opcional), `Responsavel` como texto digitado e
  `PedidoRetrabalhoId`. `CK_Perda_Motivo` aceita só `PerdaArmazem` e `MortaEmProcesso`. A perda não passa pelo
  livro: não existe posição `Perdido`.
- O fechamento (`CalculoDoFechamento`, Fase 5A) conclui a Peça só com **tudo expedido**.
- A sobra (regra 30) é calculada (`CalculadoraDeExecucao.SobraDaColeta` e `ExcessoEmMontagem`) e aparece na
  seção "Sobra" da Fila do Setor, sem ação nenhuma: não há como tirá-la de produção.
- O que o pai ainda precisa dos filhos é `FaltaMontar(pai) × QuantidadePorPai − aguardando montagem`, com
  `FaltaMontar(pai) = Quantidade(pai) − TotalMontado(pai)` (`CalculadoraDeExecucao.Precisa`). Nada nessa conta
  desconta uma perda do pai.
- `POST /pedidos` só cria Pedido de fabricação; o status `Cancelado` existe em `CK_Pedido_Status`, mas nada leva
  até ele.

## 1. O que se quer, e o que conta como pronto

**Como funciona na fábrica** (palavras do usuário, resumidas):

- Quando o cliente tira uma Peça do projeto com o Pedido rodando, **para tudo**: o que ainda não começou **e o
  que já está fabricado ou dentro de um Setor** (D2).
- Quando o cliente **diminui** a quantidade, é o mesmo descarte, só que de uma parte (D3).
- Quem descarta **escolhe a posição** de onde tira, e o sistema não adivinha (D4).
- A peça morta na expedição é refeita **num Pedido de Retrabalho à parte**, não no mesmo Pedido (D6).
- O cliente cancelar o Pedido inteiro é descartar Peça por Peça até ele concluir, **numa ação só**: fazer Peça por
  Peça à mão é exaustivo (D7). O mesmo vale para o Agrupamento (D8).

**Pronto é:**

- a Qualidade ou o PCP dão baixa em qualquer nó, Peça ou Item, por perda ou descarte, de tudo o que está em
  produção ou de uma parte, escolhendo as posições;
- o "Descartar tudo" do Pedido e do Agrupamento leva tudo o que está em produção, numa escrita só;
- a Qualidade "mata" a não conforme no cartão da Peça da expedição;
- a sobra da Fila do Setor se descarta da própria linha;
- cada baixa se estorna inteira;
- o fechamento conta o perdido junto do expedido: uma Peça ou um Item que saiu do projeto deixa de ser produzido
  **sem travar o fechamento do Pedido** (critério de pronto do `06`), e o filho acrescentado por engano deixa de
  travar a montagem do pai.

## 2. Decisões

### 2.1 Do usuário

| # | Decisão |
|---|---|
| D1 | A 5B se divide em 5B-1 (perda, descarte, "matar", fechamento) e 5B-2 (Retrabalho, nó pronto, reposição, reclamação do cliente), cada uma com spec, plano e PR. |
| D2 | Descartar o que saiu do projeto para **tudo**, inclusive o que já está fabricado ou num Setor. |
| D3 | Diminuir a quantidade é o mesmo descarte, de uma parte. |
| D4 | Na baixa de uma parte, quem dá baixa escolhe a posição (e o Setor) de onde tira. O sistema não escolhe sozinho. |
| D5 | Só a Qualidade e o PCP dão baixa (mais o Administrador). O Operador vê a linha de sobra e não a descarta. |
| D6 | O "matar" da não conforme é baixa; o refazer é um Pedido de Retrabalho à parte, na 5B-2. |
| D7 | O Pedido cancelado pelo cliente é descartado até concluir, por uma ação só ("Descartar tudo"). Termina `Concluido`, não `Cancelado`. |
| D8 | O "Descartar tudo" existe também no Agrupamento. |
| D9 | Um botão só, "Dar baixa", com o motivo dentro; não dois botões ("Registrar perda" e "Descartar"). |
| D10 | O registro da baixa segue o molde da remessa da 5A: uma `Perda` por baixa, as quantidades em linhas do livro que a apontam, estorno inteiro (abordagem A da seção 9). |
| D11 | Descarte total leva tudo do nó **e da árvore abaixo dele**, em todas as posições, sem escolha; o expedido e o já montado dentro de um pai ficam onde estão. |
| D12 | Com o Pedido rodando (fora de `Aberto`), editar a quantidade de um nó **só aumenta**; diminuir é pela baixa. Com o Pedido `Aberto`, a edição continua livre, para corrigir erro de cadastro (seção 4.10). |
| D13 | A perda de um Item que impede montar é **uma baixa só**: o painel oferece dar baixa, junto, das unidades da Peça do topo que a perda trava, e as duas se estornam juntas (seção 4.6). |
| D14 | A baixa de **tudo** de um Item por perda (`PerdaArmazem` ou `MortaEmProcesso`) que trava a montagem da Peça do topo também avisa e oferece, como a de parte (D13): a prévia devolve `travaNoTopo` e o painel mostra a caixa, marcada por padrão. Com `Descarte` não há trava, porque o filho sai do projeto (C22). Decisão do usuário de 2026-11-04, na execução. |

### 2.2 Do desenho (o usuário confere)

| # | Chamada |
|---|---|
| C1 | Posição nova **`Perdido`**, terminal, sem Setor e sem passo; tipo de movimento novo **`Perda`**. |
| C2 | `dbo.Perda` reescrita como **registro da baixa** (seção 3.2); `Responsavel` digitado sai, o autor é o usuário logado (como a remessa, D9 da 5A). |
| C3 | O motivo é da baixa inteira, não de cada linha: `PerdaArmazem`, `MortaEmProcesso` ou `Descarte`. Rótulos na tela: "Sumiu no armazém", "Morreu num processo", "Descarte". |
| C4 | De onde se dá baixa: toda posição **em produção** menos `AguardandoLiberacao` (seção 4.2). |
| C5 | **O que falta montar desconta o perdido do pai.** `FaltaMontar(pai)` passa a ser `Quantidade − TotalMontado − perdido que saiu de "a iniciar"`, que é o saldo do pai em `AIniciar` (seção 4.4). |
| C6 | Na baixa de uma parte de um pai, os filhos **a iniciar** além do que o pai agora precisa saem junto, automaticamente, na mesma baixa, descendo a árvore. Os filhos que já existem fisicamente além disso ficam como estão, e viram sobra pelo cálculo de sempre (seção 4.3). |
| C7 | Uma baixa, de um nó ou de muitos (o "Descartar tudo"), grava **uma** `Perda` e se estorna **inteira**, pela rota dela; a rota genérica `POST /movimentacoes/{id}/estorno` recusa linha de baixa (`EstornoImpossivel`), como já recusa a da remessa. |
| C8 | Quem estorna: o autor, o PCP ou o Administrador (o mesmo de C7 da 5A). |
| C9 | O estorno da baixa não tem prazo: `Perdido` é terminal, a quantidade nunca anda depois. Recusado só se já estornada (409 `JaEstornado`). |
| C10 | A baixa e o estorno recalculam o fechamento na mesma transação, como a remessa (C12 e C13 da 5A). |
| C11 | **Pedido `Aberto` também conclui** quando todas as Peças dele concluem por perda. Até aqui `Aberto` só saía pelo primeiro início; o "Descartar tudo" de um Pedido que nunca começou precisa concluí-lo. |
| C12 | **Baixa em Pedido `Concluido`**: aceita só o que ainda está em produção, que num Pedido concluído é a **sobra viva** de Item (regra 30); em `Cancelado`, recusada (409 `PedidoFechado`). O estorno da baixa é aceito em `Concluido` e reabre o que a baixa concluiu. |
| C13 | A pausa do Pedido não recusa a baixa (regra 31: a pausa recusa só o início). |
| C14 | A prévia do que a baixa faz vem do **servidor**, por uma rota que calcula sem gravar, e não de uma cópia da conta no front. Diferente da `tiradaDaRemessa`, a conta da baixa percorre a árvore. |
| C15 | O "Descartar tudo" com unidade em `AguardandoLiberacao` em alguma Peça é recusado (409 `LiberacaoEmAberto`), nomeando as Peças: o pedido de liberação se estorna antes. |
| C16 | Na tela, o "matar" é a mesma rota da baixa, com origem `NaoConforme` e motivo `MortaEmProcesso` fixos. |
| C17 | O descarte da sobra é a mesma rota, com origem na posição da sobra e motivo `Descarte` fixo; a tela limita a quantidade à sobra da linha, o servidor não (descartar além da sobra é descartar o que o pai precisa, que é o descarte comum). |
| C18 | Um registro de baixa tem no máximo `TamanhoMaximoDoLote` linhas de origem informadas; o descarte total não tem teto de linhas (seção 5.1). |
| C19 | A observação é opcional, aparada, até 500 caracteres (400 `TextoLongoDemais`), como a da resposta à liberação. |
| C20 | O status `Cancelado` continua sem ação que leve a ele. Registrado aqui para não ser lido como esquecimento. |
| C21 | A 5B-1 entrega da D5 da 3B (a perda nos tetos) **só** a perda do próprio pai em `FaltaMontar` (C5). O resto da D5 (o conjunto que perde parte dentro da Solda) e a espera desalinhada continuam na 5C. |
| C22 | "Saiu do projeto" é marcado, não deduzido: o filho deixa de contar para a montagem do pai só quando tem uma baixa **`tudo`** de motivo `Descarte` não estornada **no próprio nó** (coluna `Perda.Tudo`; seção 4.5). Uma baixa de parte, mesmo que zere o filho, não o tira da conta. |
| C23 | O estorno que desfaz a conclusão de um Pedido que **nunca teve início** (líquido de estorno, regra 14) o devolve a `Aberto`. Num Pedido `Aberto`, o Agrupamento cujas Peças concluíram todas por baixa ganha `DataConclusao` (seção 4.8). |
| C24 | Num Pedido `Concluido`, todo saldo de **Item** ainda em produção é sobra viva, em qualquer posição: a baixa dele é aceita (de parte, `tudo` ou o "Descartar tudo"), só com motivo `Descarte`. Baixa de Peça num Pedido concluído não existe, porque a Peça não tem nada em produção (seção 4.8). |
| C25 | A seção "Sobra" da Fila do Setor passa a incluir a sobra de Pedido `Concluido`; as outras seções da Fila e as Tarefas continuam excluindo o Pedido concluído (seção 6). |
| C26 | A lista do Separador e o resumo de material da Fila deixam de mostrar o nó **perdido ou descartado por inteiro**, isto é, com perdido igual à quantidade dele (decisão do usuário, 2026-11-04). O nó todo montado ou expedido, e o perdido só em parte, continuam como na Fase 4. Na baixa de parte, o planejado do material **não** se reescala: é a mesma limitação da dívida m5 da Fase 4 (editar a quantidade não reescala), registrada como risco. |
| C27 | A baixa do Pedido e a do Agrupamento aparecem numa seção "Baixas" da `PedidoDetalhePage` (todas as baixas do Pedido, por `GET /pedidos/{id}/baixas`), com o "Estornar". O `HistoricoDeBaixas` do nó continua mostrando toda baixa que tem linha nele ou num descendente, inclusive a do Pedido. |

## 3. Modelo de dados

### 3.1 Posição e tipo novos no livro

| Posição | Setor | Passo | O que é | Conta como |
|---|---|---|---|---|
| `Perdido` | — | — | **terminal**; saiu por uma baixa (perda ou descarte) | perdido |

Tipo de movimento novo: **`Perda`**, de uma posição em produção para `Perdido`, apontando a baixa
(`Movimentacao.PerdaId`). O estorno de uma linha `Perda` é um `Estorno` de `Perdido` de volta à origem, também
apontando a baixa.

A regra 9 fecha com o quarto termo: em produção + montado + expedido + **perdido** (a posição `Perdido`) = a
quantidade do nó. Qualquer nó entra em `Perdido`, Peça ou Item.

Constraints de `dbo.Movimentacao` que mudam: `CK_Movimentacao_Tipo` ganha `Perda`;
`CK_Movimentacao_OrigemPosicao` e `CK_Movimentacao_DestinoPosicao` ganham `Perdido`; `CK_Movimentacao_OrigemCoerente`
e `CK_Movimentacao_DestinoCoerente` põem `Perdido` entre as posições sem Setor e sem passo;
`CK_Movimentacao_RegistroCoerente` ganha `(Tipo = 'Perda' AND PerdaId IS NOT NULL AND ContagemId IS NULL AND
LiberacaoId IS NULL AND RemessaId IS NULL)`, `PerdaId` entra na soma do `Estorno` (no máximo um registro) e nas
linhas que exigem os outros nulos. Coluna nova `PerdaId INT NULL` com FK `FK_Movimentacao_Perda` e índice filtrado
`IX_Movimentacao_Perda` (`WHERE PerdaId IS NOT NULL`), como `RemessaId`.

### 3.2 `dbo.Perda` reescrita como registro da baixa

| Coluna | Tipo | O que é |
|---|---|---|
| `Id` | `INT IDENTITY` | PK |
| `Motivo` | `NVARCHAR(20) NOT NULL` | `PerdaArmazem`, `MortaEmProcesso` ou `Descarte` (`CK_Perda_Motivo`) |
| `Observacao` | `NVARCHAR(500) NULL` | livre, opcional (C19) |
| `PedidoId` | `INT NOT NULL`, FK | o Pedido de que é a baixa (toda baixa é de um Pedido só) |
| `AgrupamentoId` | `INT NULL`, FK | preenchido no "Descartar tudo" do Agrupamento |
| `EstruturaItemId` | `INT NULL`, FK | preenchido na baixa de um nó (o nó em que se clicou; as linhas podem ser dele, de descendentes e, por D13, da Peça do topo) |
| `Tudo` | `BIT NOT NULL` | a baixa levou tudo (D11): o "tudo" de um nó, ou o "Descartar tudo". É o que marca "saiu do projeto" (C22) |
| `DataHora` | `DATETIME2 NOT NULL` | a da escrita |
| `UsuarioId` | `INT NOT NULL`, FK | o autor, da claim `sub` |
| `PedidoRetrabalhoId` | `INT NULL`, FK | **não usada na 5B-1**; fica para a 5B-2 |

`CK_Perda_Escopo`: no máximo um de `AgrupamentoId` e `EstruturaItemId` preenchido; os dois nulos é o "Descartar
tudo" do Pedido. As quantidades **não** ficam aqui: são as linhas `Perda` do livro que apontam a baixa, uma por nó e
por posição de origem (como a remessa, C8 da 5A). `MotivoPerda`, `Quantidade`, `SetorId`, `DataPerda` e
`Responsavel` saem.

### 3.3 Migração — `db/alter-fase-5b1.sql`

Mesmo formato dos anteriores (idempotente, `-b -f 65001`): **aborta** (`THROW 50003`) se `dbo.Perda` tiver alguma
linha na forma antiga, em vez de descartar dado em silêncio; recria `dbo.Perda` na forma nova se ainda estiver na
antiga e vazia; acrescenta `Movimentacao.PerdaId` com FK e índice; e recria as constraints do livro da seção 3.1
(`DROP` se existir e `ADD`, sempre, como o `alter-fase-5a.sql`). Não precisa regenerar o banco. O `CLAUDE.md` ganha
o comando dele, no molde do da 5A.

### 3.4 O que não muda

`EstruturaItem` (a quantidade cadastrada do nó continua a mesma depois da baixa: o perdido é um termo da soma, não
uma redução), o Roteiro, `dbo.Pedido` (o `MotivoRetrabalho` e o `PedidoOrigemId` são da 5B-2), a separação de
material, a aferição, a liberação e a remessa.

## 4. Regras de domínio

### 4.1 A baixa

Uma **baixa** tira quantidade de produção e a leva a `Perdido`, com um motivo:

- **`PerdaArmazem`** e **`MortaEmProcesso`** (regra 17): a quantidade sumiu ou morreu num processo. A reposição,
  quando houver, é um Pedido de Retrabalho, na 5B-2; registrar a perda não o abre.
- **`Descarte`**: a quantidade não vai ser usada. É a sobra (regra 30) e o que saiu do projeto do cliente (D2, D3).
  Não se repõe.

Três formas, todas na mesma rota (seção 5.1):

- **Parte de um nó** (D3, D4): quem dá baixa informa, para cada posição de onde tira, a quantidade (a posição com
  Setor e passo, quando tem). Acima do saldo da posição, 409 `SaldoInsuficiente`, nomeando a posição. Se o nó tem
  filhos, vale C6 (seção 4.3).
- **Tudo de um nó** (D11): leva todo o saldo em produção do nó **e de todos os descendentes**, em todas as posições.
  Nó sem nada em produção na árvore: 409 `NadaEmProducao`.
- **Tudo de um Agrupamento ou de um Pedido** (D7, D8): o "tudo de um nó" de cada Peça que ainda tem algo em
  produção, numa baixa só, motivo `Descarte` fixo. Nenhuma Peça com algo em produção: 409 `NadaEmProducao`.

O expedido e o montado dentro de um pai **nunca** saem por baixa: são terminais (D11).

### 4.2 De onde se dá baixa (C4)

`AIniciar`, `NoSetor`, `AguardandoColeta`, `AguardandoMontagem`, `AguardandoConserto`, `EmConserto`,
`ConsertoPronto`, `NaExpedicao`, `Conforme`, `NaoConforme` e `Liberada`. **Não** de `AguardandoLiberacao`: a unidade
está presa a um pedido de liberação em aberto, e tirá-la dali deixaria a Gestão respondendo sobre o que não existe.
Na baixa de uma parte, a origem `AguardandoLiberacao` é 400 `OrigemInvalida`; no descarte total, vale C15. Origem
`Montado`, `Expedido` ou `Perdido` também é 400 `OrigemInvalida`.

### 4.3 Os filhos de um pai que perde unidades (C5, C6)

Quando um nó com filhos perde unidades que estavam **a iniciar** (que, para nó com filhos, são as ainda não
montadas), o que ele precisa dos filhos cai. Na mesma baixa, para cada filho direto:

- o que o filho ainda deve ao pai é `Deve(filho) = FaltaMontar(pai, depois da baixa) × QuantidadePorPai(filho)`;
- o excesso a iniciar é `min(AIniciar(filho), max(0, EmProducao(filho) − Deve(filho)))`, em que `EmProducao` é o
  saldo do filho em todas as posições em produção: o que o filho tem além do que deve, tirado primeiro do que nem
  começou;
- esse excesso sai junto, de `AIniciar` para `Perdido`, com o motivo da baixa;
- a conta se repete descendo a árvore, porque o filho que perdeu "a iniciar" também é pai.

O filho que já existe fisicamente (num Setor, aguardando coleta, aguardando montagem) além do que o pai precisa
**não sai sozinho**: continua onde está, e vira sobra pelo cálculo de sempre da regra 30 quando estiver na
posição dela (o fim do Roteiro ou aguardando montagem). Quem está no Setor o descarta da linha de sobra.

No descarte **total** de um nó (D11), essa conta não é necessária: tudo da árvore sai.

Unidades do pai perdidas **depois** de iniciadas (num Setor, aguardando coleta, no local de expedição) não mudam o
que ele precisa dos filhos: o filho delas já foi montado.

### 4.4 O que falta montar desconta o perdido (C5, C21)

`FaltaMontar(pai) = Quantidade(pai) − TotalMontado(pai) − perdido de "a iniciar" do pai`. Para nó com filhos, isso é
o saldo de `AIniciar` dele, porque o único jeito de sair de "a iniciar" é o início, que monta. Tudo o que depende de
`FaltaMontar` segue: `Precisa` (as tarefas do Movimentador, regra 23), a sobra (regra 30), a montabilidade do Setor
(regra 24) e o teto de entrada do Kit (regra 25). É o que destrava o filho acrescentado por engano: descartado o
filho inteiro, o pai deixa de exigi-lo (a razão continua, mas o filho não tem mais nada a dar e a montabilidade
precisa olhar só os filhos que ainda têm o que fornecer — ver a seção 4.5).

### 4.5 O filho descartado por inteiro sai da conta da montagem

Um filho **descartado** por inteiro não pode mais limitar a montagem do pai: sem nada em produção, ele zeraria o "dá
para montar" para sempre. A montabilidade (`CalcularMontabilidade`), o conjunto completo do Kit (`ConjuntoCompleto`),
o teto de entrada, o cartão de "Dá para montar N" **e o consumo dos filhos no início do pai**
(`ApontamentoUseCase.IniciarNoAsync`, que hoje baixa todo filho travado) **ignoram** o filho que tem uma baixa `Tudo`
de motivo `Descarte`, não estornada, com escopo no próprio nó ou num ancestral dele que não seja o pai (C22). "Saiu do projeto" é isso: a peça não é mais parte do pai daqui em diante. As
unidades do pai já montadas continuam com ele dentro.

O filho perdido por inteiro por `PerdaArmazem` ou `MortaEmProcesso` **não** é ignorado: o pai continua sem poder
montar o que não tem, e o caminho é a regra 27 (seção 4.6), que dá baixa da unidade da Peça do topo.

### 4.6 Perda que impede montar (regra 27), na 5B-1

A 5B-1 traz a **baixa**; a reposição é da 5B-2. A perda de um Item que impede montar é **uma baixa só** (D13):

- **Quando trava.** Depois da perda, sobe-se pela cadeia do Item até a Peça do topo: em cada pai, as unidades que
  ainda faltam montar e que os filhos em produção já não cobrem são `FaltaMontar(pai) − ⌊EmProducao(filho) ÷
  QuantidadePorPai(filho)⌋`, quando positivo, e isso se propaga para cima pela razão de cada nível até virar unidades
  da Peça do topo. A prévia (C14) devolve esse número (`travaNoTopo`) quando ele é maior que zero.
- **O que o painel oferece.** Com `travaNoTopo > 0`, o `PainelDeBaixa` do Item mostra *"Isso impede montar N {Peça do
  topo}. Dar baixa também delas"*, marcado por padrão. Marcado, a mesma baixa leva também N unidades da Peça do topo,
  de "a iniciar" (ainda não montadas, porque são as que travaram), com o mesmo motivo. Por C6, os filhos a iniciar
  daquelas unidades saem junto, descendo a árvore; os que existem fisicamente ficam, e viram sobra — e é deles que a
  5B-2 tira o que marca **pronto** no Retrabalho.
- **No corpo.** `POST /estrutura/{id}/baixas` aceita `incluirTopo: bool` (só na baixa de um Item: de parte, ou de tudo com motivo `PerdaArmazem` ou `MortaEmProcesso`, D14). A
  quantidade da Peça do topo é a da prévia, recalculada dentro da transação, não enviada pelo cliente.
- **Se a Peça do topo não tem N a iniciar** (já começou mais do que dá para montar), a baixa leva o que houver a
  iniciar, e a prévia diz isso; o resto é caso de baixa da Peça onde ela estiver, à mão.

A Peça original conclui normalmente (regra 13), e a baixa inteira se estorna junto (C7).

### 4.7 "Matar" a não conforme (D6, C16)

Baixa da Peça, origem `NaoConforme`, motivo `MortaEmProcesso`. Sai do expedível e conta para o fechamento. O refazer
é um Pedido de Retrabalho à parte (5B-2).

### 4.8 Fechamento (regra 13; C10, C11)

- A Peça conclui com **expedido + perdido = quantidade**.
- O status `AguardandoExpedicao` passa a ser: todo o não expedido **e não perdido** de cada Peça está no local de
  expedição.
- Pedido `Aberto` cujas Peças concluíram todas (C11) vai a `Concluido`; o Agrupamento dele cujas Peças concluíram
  todas ganha `DataConclusao` mesmo com o Pedido ainda `Aberto` (C23). Os demais casos de C16 da 5A não mudam
  (Pedido sem Peça não conclui; Agrupamento sem Peça não segura).
- O estorno que reabre um Pedido que **nunca teve início** (nenhuma linha `Inicio` líquida de estorno nos nós dele,
  regra 14) o devolve a `Aberto`, e não a `EmProducao` (C23). O `CalculoDoFechamento` passa a receber essa informação
  em vez de sair cedo para `Aberto`.
- Num Pedido `Concluido`, a baixa de Item é aceita só com motivo `Descarte`, em qualquer posição em produção (C24);
  perda de Item num Pedido concluído é 409 `PedidoFechado`.
- A baixa e o estorno dela recalculam o fechamento, do zero, na mesma transação (C10).

### 4.9 Estorno (C7, C8, C9)

Pela rota da baixa: um `Estorno` por linha, de `Perdido` de volta à origem, todos apontando a baixa, numa
transação. Ordem: baixa existe (404), autor ou PCP/Administrador (403), Pedido `Cancelado` (409 `PedidoFechado`), já
estornada (409 `JaEstornado`, relido depois da trava). O estorno devolve **também** os filhos que saíram junto por
C6. Não há prazo: `Perdido` é terminal.

> Risco aceito: estornar uma baixa de pai depois de a sobra dos filhos ter sido descartada (por outra baixa) deixa
> o pai precisando de filhos que já foram descartados; quem quer desfazer tudo estorna as duas.

### 4.10 Editar a quantidade do nó (D12)

Com o Pedido fora de `Aberto` (`EmProducao` ou `AguardandoExpedicao`), `PUT /estrutura/{id}` com quantidade **menor**
que a atual é recusado com 409 `DiminuirPelaBaixa`, cuja mensagem manda para o "Dar baixa"; aumentar continua livre e
recalcula o fechamento, como hoje (C15 da 5A). Com o Pedido `Aberto`, a edição continua livre nos dois sentidos, com a
guarda de sempre (`QuantidadeAbaixoDoMovimentado`). Isso troca a frase da regra 13 que hoje permite "baixar a
quantidade de uma Peça até o que já foi expedido". No painel de edição do nó, com o Pedido rodando, o campo de
quantidade diz que diminuir é pelo "Dar baixa".

## 5. Backend

### 5.1 Rotas

Todas na transação da execução (`ExecutarAsync`), travando os nós e depois os Pedidos (seção 8.1 da spec da Fase 3).

- `POST /estrutura/{id}/baixas` *(Qualidade, PCP, Administrador)* — `{ motivo, observacao?, tudo: bool, origens?:
  [{ posicao, setorId?, ordem?, quantidade }], incluirTopo?: bool }` (`incluirTopo`, D13 e D14, só na baixa de um Item,
  de parte ou de tudo por perda; noutro caso, 400 `OrigemInvalida`). `tudo: true` exige `origens` ausente ou vazia; `tudo: false` exige
  `origens` com pelo menos uma, sem posição repetida (400 `ItemRepetido`) e no máximo `TamanhoMaximoDoLote` (C18).
  `201` com a baixa projetada (seção 5.2).
- `POST /estrutura/{id}/baixas/previa` — o mesmo corpo; responde `200` com o que a baixa gravaria (as linhas por nó
  e posição, e os filhos que virariam sobra), **sem gravar**, com as mesmas recusas (C14). Mesmos perfis.
- `POST /agrupamentos/{id}/descarte` e `POST /pedidos/{id}/descarte` *(Qualidade, PCP, Administrador)* — `{
  observacao? }`, o "Descartar tudo" (D7, D8). `201` com a baixa projetada.
- `POST /agrupamentos/{id}/descarte/previa` e `POST /pedidos/{id}/descarte/previa` — prévia, `200`.
- `POST /baixas/{id}/estorno` *(Qualidade, PCP, Administrador; e só o autor, se não for PCP nem Administrador)* —
  `200` com os movimentos de estorno.
- `GET /estrutura/{id}/baixas` *(qualquer perfil autenticado)* — as baixas com linha no nó ou num descendente, da
  mais recente para a mais antiga, com `estornada` e `estornavel` (C8), as linhas e o nome do autor.
- `GET /pedidos/{id}/baixas` *(qualquer perfil autenticado)* — todas as baixas do Pedido, na mesma forma (C27).
- `POST /movimentacoes/{id}/estorno` recusa linha `Perda` com 409 `EstornoImpossivel` (C7).

### 5.2 Projeção da baixa

`{ id, motivo, observacao, escopo: { tipo: 'No' | 'Agrupamento' | 'Pedido', id }, dataHora, usuarioId,
usuarioNome, estornada, estornavel, linhas: [{ estruturaItemId, descricao, codigo, origemPosicao, origemSetorId,
origemSetorNome, origemOrdem, quantidade }] }`. A prévia devolve `{ linhas, viramSobra: [{ estruturaItemId,
descricao, quantidade }], travaNoTopo: { pecaId, descricao, quantidade, aIniciar } | null }` (D13).

### 5.3 Perfis

Tabela nova `baixa: ['Qualidade', 'PCP', 'Administrador']` em `web/src/auth/permissoes.ts`, espelhando o
`[Authorize(Roles)]` do controller novo (D5). O estorno da baixa entra na mesma lista, com a regra do autor na
aplicação.

### 5.4 Contrato de erro (códigos novos ou reusados)

| Código | Status | Quando |
|---|---|---|
| `MotivoInvalido` | 400 | motivo fora dos três |
| `OrigemInvalida` | 400 | posição que não admite baixa (seção 4.2), ou Setor/passo incoerente com a posição |
| `ItemRepetido` | 400 | a mesma origem duas vezes |
| `TextoLongoDemais` | 400 | observação acima de 500 |
| `QuantidadeInvalida` | 400 | quantidade não positiva ou fora da coluna |
| `SaldoInsuficiente` | 409 | acima do saldo da origem; a mensagem nomeia a posição |
| `NadaEmProducao` | 409 | o "tudo" não acharia nada a tirar |
| `LiberacaoEmAberto` | 409 | descarte total com unidade aguardando liberação (C15); nomeia as Peças |
| `PedidoFechado` | 409 | Pedido `Cancelado`; ou, num Pedido `Concluido`, baixa de Peça ou motivo que não é `Descarte` (C12, C24) |
| `JaEstornado` | 409 | estorno de baixa já estornada |
| `EstornoImpossivel` | 409 | linha `Perda` pela rota genérica |
| `DiminuirPelaBaixa` | 409 | `PUT /estrutura/{id}` diminuindo a quantidade com o Pedido rodando (D12) |

## 6. Front

Módulo novo `web/src/baixas/`, no molde de `web/src/expedicao/`: o `PainelDeBaixa`, o `HistoricoDeBaixas`, o
`PainelDeDescarteTotal` e o `usePermissoesDaBaixa` (o único lugar que lê `permissoes.ts` para a baixa). Os painéis
usam `PainelDeEscrita` e `useEscritaDoPainel`; a tela não chama `usePodeEscrever` direto.

- **Painel do nó** (`PainelDoNo`, na `AgrupamentoDetalhePage`): botão "Dar baixa" (D9), que abre o `PainelDeBaixa`:
  motivo (três opções), "Tudo o que está em produção" ou "Uma parte" (um `Campo` de quantidade por posição com
  saldo, rotulado por `rotuloDoSaldo`; a de `AguardandoLiberacao` aparece sem campo, com a frase de por que não), a
  prévia do servidor ("O que acontece") e a observação. O botão de enviar diz a quantidade ("Dar baixa em 4"). Seção
  nova "Baixas" (`HistoricoDeBaixas`), com "Estornar" onde `estornavel`.
- **Cartão da Peça da expedição** (`CartaoDaPeca`): ação "Matar", ao lado de "Mandar para conserto" e "Pedir
  liberação", quando `naoConforme > 0`; painel com a quantidade (até o não conforme) e a observação (C16).
- **Pedido** (`PedidoDetalhePage`) e **Agrupamento** (`AgrupamentoDetalhePage`): "Descartar tudo" no `acao` da
  `Pagina`, ao lado do botão que já existe, abrindo o `PainelDeDescarteTotal` com a prévia do servidor, a observação
  e o botão `perigo`. Só aparece com algo em produção.
- **Fila do Setor**, seção "Sobra": "Descartar" na linha, só para Qualidade, PCP e Administrador (D5), com a
  quantidade até a sobra da linha (C17). A seção passa a incluir a sobra de Pedido `Concluido` (C25).
- **Pedido** (`PedidoDetalhePage`): seção "Baixas", com todas as baixas do Pedido e o "Estornar" (C27).
- **Painel do Item** (D13, D14): com `travaNoTopo` na prévia, em "Uma parte" ou em "Tudo" com motivo de perda, a caixa "Isso impede montar N {Peça}. Dar baixa também delas",
  marcada por padrão (uma `CaixaDeLote` não serve: é uma caixa com texto, um `Campo` de caixa).
- **Editar nó** (D12): com o Pedido rodando, a dica do campo de quantidade diz que diminuir é pelo "Dar baixa".
- **Separação** (C26): a `SeparacaoPage` e o resumo de material da Fila deixam de listar o nó perdido ou descartado por inteiro.
- **Onde está** (`ResumoDePosicoes`, `rotuloDoSaldo`): `Perdido` aparece como "N perdidos". A Fila e as Tarefas não
  listam `Perdido`.
- **Cor**: a baixa é ação de **perigo** só no "Descartar tudo" (o `Botao` `perigo`, como o "Excluir"); o resto é
  primário comum. "N perdidos" é **pílula neutra**: perder não é reprovado nem aprovado.

## 7. Testes

- Domínio: a conta de C6 descendo três níveis; `FaltaMontar` com perdido de "a iniciar" e com perdido depois de
  iniciado (não muda); o filho descartado por inteiro sai da montabilidade e do conjunto completo; o fechamento com
  expedido + perdido; `Aberto` que conclui (C11).
- Api: cada código da seção 5.4; o `PUT` que diminui com o Pedido rodando (D12); a baixa com `incluirTopo` em dois
  níveis, e o estorno dela; o estorno que devolve a `Aberto` (C23); o estorno inteiro (inclusive os filhos de C6); a rota genérica recusando a linha;
  o "Descartar tudo" com várias Peças em várias posições; a prévia batendo com a gravação; baixa de sobra em Pedido
  concluído (C12).
- Front: os três estados de cada painel; o gating por perfil (o Operador não vê "Dar baixa" nem o "Descartar" da
  sobra); a prévia exibida antes de confirmar.
- Massa da conferência no navegador: um Pedido com Peça de três níveis, lote dividido entre Setores, uma não
  conforme e sobra na Fila; nós copiados do catálogo **sem** descrição própria (a lição da Fase 4).
- Data de entrega de teste que não deve atrasar: `"2099-12-31"` (a lição da 5A).

## 8. Riscos conhecidos, aceitos

- O estorno da baixa de um pai depois do descarte da sobra dos filhos (seção 4.9).
- O filho descartado por inteiro deixa de limitar a montagem (seção 4.5); se o descarte foi engano, o estorno o traz
  de volta à conta.
- Na baixa de parte, o planejado do material do nó não se reescala (C26), como na edição de quantidade (dívida m5 da
  Fase 4): a pendência do Separador pode ficar maior que o necessário.

## 9. Abordagens consideradas

- **A — `Perda` como registro da baixa, no molde da remessa** (escolhida, D10): uma linha de `Perda` por baixa e as
  quantidades em linhas do livro que a apontam. Repete um padrão revisado; o estorno inteiro e o "Descartar tudo"
  numa baixa só saem sem invenção. Custa reescrever a tabela, que está vazia.
- **B — `Perda` como está, uma linha por nó e posição, ligada ao livro**: mexe menos no schema, mas nada agrupa a
  baixa de muitos nós, e o estorno inteiro fica sem chão.
- **C — sem `Perda`, motivo direto no livro**: uma tabela a menos, mas põe no livro colunas só da perda e tira o
  lugar do vínculo com o Retrabalho da 5B-2.

## 10. Ajustes de documentação (na mesma branch)

- `specs/01`: regras 9, 13, 17, 27 e 30 com a baixa; o ponto em aberto «Descontinuar uma Peça trava o fechamento
  do Pedido» sai da lista, fechado pela baixa de `Descarte` (o bucket é `Perdido`); regra nova da baixa (seções 4.1
  a 4.9) e do estorno dela.
- `specs/02`: `dbo.Perda` e `dbo.Movimentacao` (seção 3).
- `specs/04`: fluxos 5 e 6 revistos (o 5 só aponta para a 5B-2).
- `specs/05`: as rotas da seção 5.1; a rota antiga `POST /pecas/{id}/perdas` sai; o `PUT /estrutura/{id}` ganha
  `DiminuirPelaBaixa` (D12) na tabela "Contrato de erro da Estrutura".
- `specs/01`, regra 13: a frase "baixar a quantidade de uma Peça até o que já foi expedido a conclui" sai (D12), e a
  regra ganha C11, C23 e C24.
- `specs/06`: a 5B partida em 5B-1 e 5B-2; da 5C sai a perda do próprio pai nos tetos (C21).
- `CLAUDE.md`: a ordem (5B-1, 5B-2), o comando do `db/alter-fase-5b1.sql`, o invariante da regra 9 com o perdido,
  o módulo `web/src/baixas/` na seção Interface, e as contagens que mudarem (remedidas, com a métrica dita).

## 11. Fora do escopo

Pedido de Retrabalho, nó pronto, tipo `Pronto`, `Perda.PedidoRetrabalhoId`, reclamação do cliente (5B-2); a perda
nos tetos do Kit além de C21 e a espera desalinhada (5C); o status `Cancelado` (C20); KPIs de perda (Fase 6).
