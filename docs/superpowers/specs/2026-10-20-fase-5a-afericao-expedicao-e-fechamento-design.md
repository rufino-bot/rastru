# Fase 5A — Aferição, expedição e fechamento — desenho

Data: 2026-10-20. Branch: `claude/brainstorm-fase-5-4otppt`, a partir da `main` em `141ae3c`. Desenho
aprovado pelo usuário em chat, numa sessão de nuvem de 2026-10-20, com um esboço da tela da Expedição
(página privada, fora do repositório) aprovado como base.

**Ponto de partida.** A seção "Fase 5 — Dimensional e fechamento" de `specs/06-roadmap-mvp.md` junta três
assuntos que mal se tocam. O usuário aprovou dividi-la em três specs, cada uma com plano e PR próprios, nesta
ordem:

- **5A — Aferição, expedição e fechamento** (esta spec): o fim do caminho de uma Peça, do local de
  expedição até o caminhão, e o fechamento automático de Agrupamento e Pedido.
- **5B — Perda e reposição**: perda de Peça e de Item, `Descarte` da sobra, descartar o que saiu do projeto
  com o Pedido rodando, perda que impede montar (regra 27), "matar a peça" reprovada e o Pedido de
  Retrabalho com o nó pronto.
- **5C — As pontas do Kit que a 3B deixou**: a perda nos tetos (D5 da 3B), a espera desalinhada e a
  estrutura alterada no meio da produção (D1 da 3B).

O fechamento nasce aqui contando só o expedido; a 5B acrescenta o perdido na mesma conta.

**Quando executa.** A Fase 4 foi mesclada em 2026-10-20 (rufino-bot/rastru#34), e a Fase 5 é a seguinte.
Não é exceção à ordem das fases: é a própria Fase 5, partida em três.

**O que o código tinha antes desta spec** (lido em `141ae3c`):

- `dbo.Expedicao`, `dbo.RelatorioDimensional`, `dbo.RelatorioDimensionalAvaliacao` e `dbo.Perda` existem
  **só no schema**: sem entidade, sem mapeamento EF, sem rota, sem tela. Nenhum código as escreve.
- O livro (`dbo.Movimentacao`) já leva a Peça que termina o último passo até `NaExpedicao`, o local de
  expedição (regra 29), pela entrega do Movimentador. Dali ela não sai: não existe posição `Expedido`.
- `EstruturaItem.RequerRelatorioDimensional` é gravado ao criar a Peça (o "Requer relatório dimensional"
  do painel "Nova Peça" da `AgrupamentoDetalhePage`, e o da conferência do import) e aparece como pílula
  neutra na `ArvoreDeEstrutura`. Não se edita depois.
- O status `AguardandoExpedicao` existe em `CK_Pedido_Status` e no filtro de Pedidos, mas nada o liga.
  `Concluido` também não é ligado por ninguém; `Agrupamento.DataConclusao` e `Pedido.DataConclusao` nunca
  são preenchidas.
- A montagem final da Peça acontece no Setor **Expedição**, como o primeiro (e único) passo do Roteiro dela
  (D2 da spec da Fase 4). O **local de expedição** (`NaExpedicao`) é para onde ela vai **depois** desse passo.

## 1. O que se quer, e o que conta como pronto

**Como funciona na fábrica** (palavras do usuário, resumidas):

- Quem mede o Relatório Dimensional, decide se passou e expede é **a mesma pessoa, a da expedição**. As
  cotas vêm do cliente com o pedido e ficam **fora** do Rastru.
- **Uma aferição só por Peça.** Mede-se **uma** unidade, e ela aprova ou reprova o lote inteiro. Aprovada,
  as remessas seguintes daquela Peça não são medidas de novo.
- **Reprovada, mede-se todas as unidades**, inclusive as que chegam depois, uma a uma.
- A não conforme tem três destinos: **consertar** (volta ao Setor que corrige), **"matar a peça"** (vira
  perda e é refeita do começo; 5B) ou **expedir assim mesmo**, quando a medida fora não atrapalha o uso no
  cliente — a sobra de uma dobra de chapa, por exemplo. Essa liberação é dada por um **superior da
  administração**.
- O Rastru **avisa e deixa expedir**: não trava a expedição da Peça reprovada.
- O cliente ainda pode reclamar **depois** de receber (acabamento, pintura, até medida). A aferição
  aprovada não impede isso. A reclamação é a 5B (Pedido de Retrabalho, motivo `SolicitacaoCliente`), não
  esta fase.

**Pronto é:**

- a Qualidade (a pessoa da expedição) tem uma tela com o que está no local de expedição, por Pedido e por
  prazo, e nela afere, conta as não conformes, manda para conserto, pede liberação e expede;
- a Gestão vê as liberações pendentes e libera ou recusa, com o nome dela gravado;
- o conserto anda pelo chão como as outras entregas: o Movimentador leva, o operador corrige, o
  Movimentador traz de volta, e a Qualidade mede de novo;
- cada remessa registra quantas unidades de cada Peça saíram, quando e quem expediu;
- Peça, Agrupamento e Pedido concluem sozinhos quando tudo foi expedido, e o Pedido passa por
  "Aguardando expedição" antes disso.

## 2. Decisões

**Decididas pelo usuário na conversa:**

| # | Decisão | Alternativas descartadas |
|---|---|---|
| D1 | Quem afere, conta e expede é a **Qualidade**: no Rastru, o perfil Qualidade é a pessoa da expedição. Não há perfil novo | perfil próprio de Expedição |
| D2 | O Rastru **não guarda as cotas** nem as medidas: só o resultado, quem e quando | campo de medidas (o `Medidas NVARCHAR(MAX)` do schema antigo) |
| D3 | **Uma aferição por Peça**: uma unidade medida decide o lote inteiro. **Conforme** libera a Peça toda, inclusive as remessas futuras | uma avaliação por remessa (o modelo antigo da regra 10); aprovação parcial na aferição |
| D4 | **Não conforme** na aferição obriga a **medir todas as unidades**, as presentes e as que chegarem depois, uma a uma, registrando quantas são conformes e quantas não | medir de novo só uma amostra; a reprovação valer para o lote sem contagem |
| D5 | A não conforme tem três destinos: **conserto**, **"matar"** (5B) e **expedir com liberação da Gestão** | — |
| D6 | **Conserto**: a Qualidade escolhe o Setor, o **Movimentador leva**, como as outras entregas, e o operador corrige. É dentro do **mesmo Pedido**, não um Pedido de Retrabalho | consertar na própria expedição sem registro; abrir Retrabalho para conserto |
| D7 | A **liberação** é **pedida pela Qualidade** no Rastru e **respondida pela Gestão**, e fica gravada: quem liberou, quantas unidades e quando | liberação fora do sistema; Gestão liberando por conta própria, sem pedido |
| D8 | **Avisar e deixar expedir**: o Rastru não trava a expedição de Peça sem aferição ou reprovada; avisa antes de confirmar e o histórico mostra o que saiu assim | travar até haver aferição conforme ou liberação |
| D9 | A **remessa** guarda quantas unidades saíram, quando e **quem expediu** (o usuário logado) | nome digitado (`Responsavel NVARCHAR` do schema antigo); número de nota fiscal (é do ERP, fora de escopo) |
| D10 | O esboço da tela (lista do local de expedição por Pedido e prazo; cartão da Peça com a contagem; painéis de aferir e expedir; lista de liberações da Gestão) é a base da interface | — |

**Chamadas do desenho** (minhas, apresentadas ao usuário em chat antes de ele aprovar a escrita; marcadas
aqui para ele as conferir na leitura):

| # | Decisão | Por quê |
|---|---|---|
| C1 | O conserto, a contagem, a liberação e a expedição entram **no livro de movimentações** (`dbo.Movimentacao`) como **posições** novas, e não num livro paralelo | conservação de quantidade (regra 9) e estorno vêm de graça; a linha da expedição guarda **de onde** a unidade saiu (conforme, liberada, não conforme), e é isso que faz o histórico mostrar "2 não conformes expedidas sem liberação" sem estado extra. O descarte do D13 da Fase 4 (material no livro) não se aplica: aqui é a quantidade **do nó** que anda |
| C2 | A **aferição** (a amostra) **não** é movimento: nenhuma quantidade anda. É um registro próprio, só de inclusão, com estorno | ela decide o significado de `NaExpedicao` para aquela Peça (seção 3.2), não move unidades |
| C3 | O conserto terminado volta **direto ao local de expedição**, sem refazer o resto do Roteiro, e é **medido de novo** | o usuário: "volta pro setor que precisa corrigir"; nada indica refazer os passos seguintes |
| C4 | Na Fila do Setor, a unidade em conserto aparece **marcada como conserto**, com o "Terminar" de sempre; nas Tarefas, as duas entregas do conserto (ida e volta) aparecem como as outras | o operador e o Movimentador não ganham tela nova |
| C5 | A Gestão pode liberar **menos** do que foi pedido, ou **nada** (recusa). O que não foi liberado volta a "aguardando decisão" | resposta parcial é o caso natural de "libero 2 das 3" |
| C6 | O **pedido de liberação exige justificativa** da Qualidade; a resposta da Gestão aceita **observação opcional** | a liberação é decisão com nome; se o cliente reclamar, o porquê é o que se procura |
| C7 | Correção é sempre por **estorno**, nunca edição: aferição, contagem, conserto, pedido e resposta de liberação, remessa. Quem estorna: o **autor**, o **PCP** ou o **Administrador**, como na separação (regra 34) e no livro (Fase 3) | consistência com os dois livros que já existem |
| C8 | A **remessa** é um registro próprio (`dbo.Remessa`), que agrupa as linhas de expedição de uma ou mais Peças saídas juntas. Estorna-se **a remessa inteira** | uma remessa de 9 unidades pode sair de duas posições (7 conformes e 2 liberadas) e são duas linhas no livro; sem o agrupador, "quantas saíram nesta remessa" e o estorno dela ficam linha a linha |
| C9 | A expedição tem **lote**: várias Peças numa remessa só, pela `BarraDoLote`, no molde da Separação | um caminhão leva várias Peças; a barra já serve a Fila, as Tarefas e a Separação |
| C10 | A remessa **escolhe sozinha de onde tira**, nesta ordem: liberada, conforme, local de expedição, não conforme. A Qualidade só diz a quantidade. A unidade **aguardando liberação não sai**: espera a Gestão responder (ou o pedido ser estornado) | a Qualidade pensa em "mandar 9", não em "7 desta posição e 2 daquela"; tirar da não conforme por último é o que faz o aviso valer |
| C11 | O status **`AguardandoExpedicao`** passa a valer quando toda a quantidade ainda não expedida das Peças do Pedido está no local de expedição (em qualquer das posições dele) | o status já existe e o filtro de Pedidos já o oferece |
| C12 | **Fechamento automático**, na mesma transação da remessa: a Peça conclui com tudo expedido (regra 13), o Agrupamento com a última Peça, o Pedido com o último Agrupamento | a regra 13 já estava escrita; faltava quem a executasse |
| C13 | **Estornar uma remessa reabre** o que ela concluiu: Peça, Agrupamento e Pedido voltam a abertos, e o status do Pedido é recalculado. O estorno de remessa é a **única** escrita aceita num Pedido `Concluido` | sem isso, uma remessa digitada errada no último caminhão ficaria sem correção |
| C14 | O PCP passa a **editar** `RequerRelatorioDimensional` de uma Peça, enquanto ela não tem aferição válida | hoje a marca só nasce na criação; o cliente pode exigir o relatório depois |

## 3. Modelo de dados

### 3.1 Posições novas no livro

A quantidade de uma Peça no fim do caminho passa a se repartir assim (a tabela de posições da seção 3.1 da
spec da Fase 3 ganha estas linhas):

| Posição | Setor | Passo | O que é | Conta como |
|---|---|---|---|---|
| `NaExpedicao` (existe) | — | — | no local de expedição. O significado depende da aferição da Peça (seção 3.2) | em produção |
| `Conforme` | — | — | no local de expedição, **medida uma a uma** depois de uma aferição não conforme, e conforme | em produção |
| `NaoConforme` | — | — | no local de expedição, medida e não conforme, **aguardando decisão** | em produção |
| `AguardandoLiberacao` | — | — | não conforme com pedido de liberação em aberto na Gestão | em produção |
| `Liberada` | — | — | não conforme **liberada** pela Gestão para expedir | em produção |
| `AguardandoConserto` | sim (o de destino) | — | a Qualidade mandou para conserto; espera o Movimentador no local de expedição | em produção |
| `EmConserto` | sim | — | no Setor que corrige | em produção |
| `ConsertoPronto` | sim | — | o operador terminou o conserto; espera o Movimentador no Setor | em produção |
| `Expedido` | — | — | **terminal**; saiu numa remessa | expedido |

A regra 9 continua fechando: o "no local de expedição" dela passa a ser a soma de `NaExpedicao`,
`Conforme`, `NaoConforme`, `AguardandoLiberacao` e `Liberada`; o conserto conta como em produção; e
`Expedido` é o "expedido" que a regra sempre teve. **Só Peça** entra nessas posições: Item nunca chega ao
local de expedição (regra 29).

**Atenção ao tamanho:** `OrigemPosicao`, `DestinoPosicao` e `Tipo` são `NVARCHAR(20)`. Os nomes acima cabem
(o maior, `AguardandoLiberacao`, tem 19). Quem renomear confere.

### 3.2 O significado de `NaExpedicao`, pela aferição

A aferição válida da Peça (a última não estornada; seção 3.4) decide o que é a unidade em `NaExpedicao`:

| A Peça | Unidade em `NaExpedicao` é | Expedir dali avisa? |
|---|---|---|
| não exige relatório | pronta para expedir | não |
| exige, **sem** aferição | aguardando aferição | **sim**: "sem aferição" |
| aferição **conforme** | conforme (D3) | não |
| aferição **não conforme** | **a medir** (D4) | **sim**: "sem medição" |

Por isso `Conforme` como posição só existe depois de uma aferição não conforme: com aferição conforme, o
lote inteiro já é conforme e ninguém o move.

### 3.3 Tipos de movimento

Novos tipos (todos com `UsuarioId` do autor, como os de hoje), e as transições que a
`CK_Movimentacao_Transicao` passa a aceitar:

| Tipo | De | Para | Quem |
|---|---|---|---|
| `Medicao` | `NaExpedicao` | `Conforme` ou `NaoConforme` | Qualidade; só com aferição válida **não conforme** |
| `Conserto` | `NaoConforme` | `AguardandoConserto` (Setor escolhido) | Qualidade |
| `Entrega` (existe) | `AguardandoConserto` | `EmConserto` (mesmo Setor) | Movimentador |
| `Termino` (existe) | `EmConserto` | `ConsertoPronto` (mesmo Setor) | Operador |
| `Entrega` (existe) | `ConsertoPronto` | `NaExpedicao` | Movimentador |
| `PedidoDeLiberacao` | `NaoConforme` | `AguardandoLiberacao` | Qualidade |
| `Liberacao` | `AguardandoLiberacao` | `Liberada` | Gestão |
| `Recusa` | `AguardandoLiberacao` | `NaoConforme` | Gestão |
| `Expedicao` | `Liberada`, `Conforme`, `NaExpedicao` ou `NaoConforme` | `Expedido` | Qualidade |
| `Estorno` (existe) | o inverso de qualquer um acima | | autor, PCP ou Administrador (C7) |

- Uma contagem ("medi 6: 4 conformes e 2 não") são **duas** linhas `Medicao`, gravadas juntas.
- O conserto usa `Entrega` e `Termino` de sempre, para o Movimentador e o operador não verem ação nova. As
  constraints de coerência (`CK_Movimentacao_OrigemCoerente`, `CK_Movimentacao_DestinoCoerente`) ganham as
  posições novas: as do local de expedição e `Expedido` sem Setor e sem passo; as três do conserto com Setor
  e **sem** passo, como `AguardandoMontagem`.
- O conserto **não** mexe no `EstruturaRoteiro`: o Roteiro continua sendo o que foi planejado, e os passos
  dele continuam sendo histórico.
- A linha de `Expedicao` aponta a remessa (`RemessaId`, seção 3.5), como a baixa de filho aponta a
  `Montagem`. A resposta da Gestão aponta o pedido de liberação (seção 3.6).
- **Pausa** (regra 31) não recusa nada disto: ela só recusa o início.

O formato exato das constraints e das colunas novas é de quem implementa, desde que o livro continue só de
inclusão e o `CHECK` recuse as transições que não estão na tabela acima.

### 3.4 Aferição — `dbo.RelatorioDimensional` reescrita como registro só de inclusão

A forma antiga (cabeçalho único por Peça e `RelatorioDimensionalAvaliacao` com "contato do cliente que
informou") descreve a avaliação do **cliente**, que não é o que a fábrica faz (D1–D4). As duas tabelas não
têm código nem dado. **`RelatorioDimensionalAvaliacao` sai do schema**; `RelatorioDimensional` é
reescrita como o registro das aferições:

- uma linha por aferição: a Peça, o resultado (`Conforme` | `NaoConforme`), quando, quem;
- estorno como linha própria que aponta a original, no máximo um por linha (o padrão de
  `UX_MaterialSeparacao_EstornoDe`), estorno de estorno recusado;
- a **aferição válida** da Peça é a última não estornada. Só se registra uma aferição nova quando não há
  válida (estorne a anterior primeiro);
- só Peça com `RequerRelatorioDimensional` recebe aferição;
- estornar uma aferição **não conforme** é recusado enquanto houver `Medicao` válida da Peça (as contagens
  dependem dela); estornar uma **conforme** é livre (nada depende dela no livro: o que saiu de `NaExpedicao`
  saiu de lá).

O nome `RelatorioDimensional` fica porque é o termo do glossário. Colunas, nomes de índice e de constraint
são de quem implementa.

### 3.5 Remessa — `dbo.Expedicao` vira `dbo.Remessa`

`dbo.Expedicao` (uma linha por Peça, com `Responsavel` digitado) sai do schema. Entra **`dbo.Remessa`**, o
agrupador (C8): quando e quem. As quantidades são as linhas `Expedicao` do livro que apontam a remessa
(`Movimentacao.RemessaId`). Uma remessa leva uma ou mais Peças, de um ou mais Pedidos.

O estorno é **da remessa inteira**: um `Estorno` para cada linha dela, numa transação. É recusado
(`EstornoImpossivel`) se a remessa já foi estornada. Nada mais o impede: `Expedido` é terminal, então a
quantidade expedida nunca "andou" depois.

### 3.6 Liberação — registro próprio, só de inclusão

O pedido e a resposta da liberação guardam o que o livro não guarda:

- **pedido**: a Peça, a quantidade pedida, a **justificativa** (obrigatória, C6), quando e quem (Qualidade).
  É gravado junto da linha `PedidoDeLiberacao`, que aponta para ele;
- **resposta**: a quantidade liberada (de zero até a pedida), a observação (opcional), quando e quem
  (Gestão). É gravada junto das linhas `Liberacao` (a liberada) e `Recusa` (o resto), que apontam o pedido.
  Um pedido tem **no máximo uma resposta válida**;
- estornar a resposta estorna as linhas dela e devolve o pedido a "em aberto"; estornar o pedido (sem
  resposta válida) devolve a quantidade a `NaoConforme`. As duas só enquanto a quantidade não andou
  (`EstornoImpossivel` do livro).

A forma das tabelas (uma tabela de pedidos com outra de respostas, ou um registro só com tipo) é de quem
implementa, desde que nada seja sobrescrito.

### 3.7 Pedido e Agrupamento

Nenhuma coluna nova. Passam a ser escritos, sempre na transação do movimento que os muda:

- `Pedido.Status`: `EmProducao` ↔ `AguardandoExpedicao` (C11) ↔ `Concluido` (C12, C13). `Aberto` continua
  só antes do primeiro início, e o status não volta a `Aberto` (como hoje);
- `Agrupamento.DataConclusao` e `Pedido.DataConclusao`: a data da remessa que concluiu; limpas pelo estorno
  dela (C13).

Um Agrupamento sem Peças não conclui (não há o que expedir). Pedido `Cancelado` fica fora de tudo isto.

### 3.8 `EstruturaItem.RequerRelatorioDimensional`

Não muda no schema; ganha edição pelo PCP (C14), recusada com aferição válida.

### 3.9 Migração de banco anterior — `db/alter-fase-5a.sql`

Idempotente, `-b -f 65001`, no formato dos anteriores, com o comando no `CLAUDE.md`. Leva o banco até o
`02-modelo-de-dados.sql`:

- `dbo.Expedicao`, `dbo.RelatorioDimensionalAvaliacao` e a forma antiga de `dbo.RelatorioDimensional`
  saem **só se vazias**; com alguma linha, o script **aborta** (`THROW`), como o da Fase 4 com
  `MaterialSeparacao`;
- entram `dbo.Remessa`, a forma nova de `dbo.RelatorioDimensional`, o registro da liberação,
  `Movimentacao.RemessaId` e a referência à liberação;
- as constraints de `dbo.Movimentacao` são recriadas com as posições e os tipos novos.

Não há dado a transformar: o banco **não** precisa ser regenerado.

### 3.10 O que não muda

`dbo.Perda` (é da 5B); o Roteiro; a regra de destino do fim do Roteiro (regra 29); a separação de
material; o Kit.

## 4. Regras de domínio (`specs/01`)

- **Regra 10 reescrita**: a aferição é da fábrica, feita pela Qualidade no local de expedição, uma por
  Peça, uma unidade decidindo o lote (D3); não conforme obriga a medir todas (D4).
- **Regra 11 reescrita**: aprovação por quantidade passa a ser o resultado da **medição uma a uma** depois
  da aferição não conforme, e não da avaliação por remessa.
- **Regra 12**: a reprovação da aferição não abre Retrabalho (o destino é conserto, liberação ou, na 5B,
  "matar"). O vínculo do Retrabalho com a reprovação **do cliente** é redesenhado na 5B; até lá a regra
  ganha uma nota de que a avaliação por remessa deixou de existir.
- **Regra 13**: ganha quem a executa (C12) e o estorno que reabre (C13).
- **Regra 16 reescrita**: a remessa (C8, C9) e de onde ela tira (C10); a soma das remessas de uma Peça
  continua nunca passando da quantidade dela, agora por construção (o livro só expede o que está no local
  de expedição).
- **Regra 35 (nova) — Conserto**: D6, C3, C4.
- **Regra 36 (nova) — Liberação da não conforme**: D7, C5, C6.
- **Regra 37 (nova) — Avisar e deixar expedir**: D8, com a tabela da seção 3.2.
- **Glossário**: "Relatório Dimensional" passa a descrever a aferição interna; entram "Aferição",
  "Conserto", "Liberação" e "Remessa".
- `specs/00`: o perfil Qualidade é a pessoa da expedição (afere e expede); a Gestão libera não conforme. A
  frase de "Fora de escopo" que diz que o relatório "não é amostragem sobre 100%" é reescrita (agora é
  amostra de uma unidade, e 100% depois da reprovação). O objetivo "aprovação/reprovação por quantidade"
  acompanha.

## 5. Backend

### 5.1 Rotas

As formas são de quem implementa, no padrão das que existem (`05-api-endpoints.md`). O que as rotas têm de
cobrir:

- **Lista da expedição**: as Peças com quantidade no local de expedição **ou** em conserto, de Pedidos
  abertos, com: Pedido (código, data de entrega, atrasado, pausado), Peça (código, descrição, quantidade
  total, expedida), a quantidade em cada posição da seção 3.1, se exige relatório, a aferição válida (com
  autor e data) e a liberação em aberto. Ordenada pela data de entrega do Pedido. Chega inteira; o front
  filtra no cliente (como a Separação).
- **Histórico da Peça na expedição**: aferições, medições, consertos, pedidos e respostas de liberação e
  remessas, com autor, data e `estornavel` calculado para quem lê.
- **Escritas**: registrar aferição; registrar contagem (conformes e não conformes, tudo ou nada, até a
  quantidade em `NaExpedicao`); mandar para conserto (quantidade e Setor); pedir liberação (quantidade,
  justificativa); responder liberação (quantidade liberada, observação); expedir (uma Peça ou um lote,
  cada uma com quantidade, numa remessa só, tudo ou nada); e os estornos (C7).
- **Liberações pendentes**: as abertas, para a Gestão.
- A **Fila do Setor** e as **Tarefas** passam a trazer as posições do conserto (C4), com o destino
  calculado: `AguardandoConserto` vai ao Setor escolhido, `ConsertoPronto` vai ao local de expedição.
- O **livro do nó** (`GET /estrutura/{id}/movimentacoes`) passa a mostrar os tipos novos.
- **Editar `RequerRelatorioDimensional`** de uma Peça (C14).

As escritas da expedição correm na transação SERIALIZABLE da execução (`ExecucaoRepository.EmTransacaoAsync`),
com o retry de deadlock que ela já tem: elas leem saldo do livro e escrevem nele.

### 5.2 Perfis

| Ação | Perfis |
|---|---|
| aferir, contar, mandar para conserto, pedir liberação, expedir | Qualidade, Administrador |
| responder liberação | Gestão, Administrador |
| levar ao conserto e trazer de volta | Movimentador, Administrador (a `Entrega` de hoje) |
| terminar o conserto | Operador, Administrador (o apontamento de hoje) |
| estornar | autor, PCP, Administrador (C7) |
| editar `RequerRelatorioDimensional` | PCP, Administrador |
| ler tudo isto | todos |

`web/src/auth/permissoes.ts` ganha as entradas novas (uma por conjunto de perfis, como pede a guarda
`permissoesEspelhamOBackend.test.ts`).

### 5.3 Contrato de erro

Os códigos que a execução já usa (`Proibido`, `EstornoImpossivel`, `PedidoFechado`, quantidade além do
saldo) cobrem a maior parte. Casos que esta fase precisa recusar, com código a escolher na implementação e
entrada na tabela do `05`:

- aferir Peça que não exige relatório, ou que já tem aferição válida;
- contar sem aferição válida não conforme, ou além do que está em `NaExpedicao`;
- conserto, pedido de liberação ou expedição além do saldo da posição;
- responder liberação já respondida, ou com quantidade acima da pedida;
- expedir quantidade acima do expedível (C10: tudo menos `AguardandoLiberacao`);
- escrita num Pedido `Concluido` ou `Cancelado`, **exceto** estorno de remessa (C13);
- editar `RequerRelatorioDimensional` com aferição válida.

**Recusa de lote nomeia a Peça**, como o lote da separação faz com o nó. A lição da Fase 4 vale aqui: o
nome tem de sair certo também na Peça copiada do catálogo, que não tem descrição própria.

## 6. Front

### 6.1 Tela nova "Expedição" (`/expedicao`)

Base: o esboço aprovado (D10).

- `<Pagina titulo="Expedição">`, uma linha de subtítulo; filtros por `FiltroDeDemanda` com a seleção na URL
  (Pedido e situação da aferição, no mínimo), filtragem no cliente por `casaComFiltro`.
- Peças **agrupadas por Pedido**, na ordem da data de entrega; o cabeçalho do grupo mostra código, data de
  entrega e as pílulas "Atrasado" e "Pausado" de sempre.
- **Cartão da Peça**: código e descrição, "N aqui de Q · E expedidas", pílula "Exige relatório" ou "Sem
  relatório", a situação da aferição, e, depois de uma não conforme, a **contagem**: conformes, não
  conformes e, dentro destas, liberadas, em conserto (com o Setor), aguardando liberação e aguardando
  decisão; mais "a medir" quando houver.
- **Painéis** (`PainelDeEscrita`, um aberto por vez, com `useDevolverFoco`): **Aferir** (Conforme | Não
  conforme), **Contar** (conformes e não conformes, que somam até o "a medir"), **Mandar para conserto**
  (quantidade e Setor), **Pedir liberação** (quantidade e justificativa), **Expedir** (quantidade; abre com
  o expedível preenchido, como o "Separar" da Fase 4). O **Expedir** mostra, **antes** de confirmar, o aviso
  do que vai sair sem aferição, sem medição ou não conforme sem liberação (D8), e não trava.
- **Histórico** da Peça no cartão, com estorno onde `estornavel`.
- **Lote** (C9): seleção de Peças e `BarraDoLote` "Expedir", com a quantidade de cada uma; o aviso do D8
  vale por Peça.
- Os três estados (carregando, vazio — distinguindo "nada no local de expedição" de "nada casa com o
  filtro" — e erro).

### 6.2 Liberações (Gestão)

A lista das liberações em aberto (Peça, Pedido, quantidade pedida, justificativa, quem pediu e quando) e o
painel de resposta (quantidade liberada, observação; "Recusar" é liberar zero). Se é tela própria
(`/liberacoes`) ou seção da `/expedicao` visível a todos com a ação só para a Gestão, é de quem implementa,
seguindo "gating vai na ação, não no link".

### 6.3 Fila do Setor e Tarefas

- **Fila**: a unidade `EmConserto` aparece num cartão marcado "Conserto", com a Peça e o Pedido, e o
  "Terminar" de sempre (C4).
- **Tarefas**: "Levar ao conserto em {Setor}" para `AguardandoConserto`, e "Levar ao local de expedição"
  para `ConsertoPronto`, com o "Entregar" e o lote de sempre.

### 6.4 Pedido e Agrupamento

Os status "Aguardando expedição" e "Concluído" passam a aparecer de verdade nas listas, no detalhe e na
Home, com os rótulos que já existem. A pílula "Aguardando expedição" continua **neutra** (a decisão de
destacá-la segue pendente do usuário, no ledger). O painel "Nova Peça" segue com o "Requer relatório
dimensional"; a edição dele (C14) entra onde a Peça é editada hoje.

### 6.5 Cor

- **Conforme** usa `positivo`; **Não conforme** e o aviso de expedir não conforme sem liberação usam
  `negativo`: são aprovado e reprovado, que é o que essas cores significam.
- "Exige relatório", "Sem relatório", "Aferição pendente", "A medir", "Liberada", "Em conserto" e "Aguardando
  liberação" são **neutros**: não são aprovação nem reprovação.
- "Atrasado" (`atraso`) e "Pausado" (`atencao`) como já são.

### 6.6 Menu

Entra "Expedição" (e "Liberações", se for tela própria). O menu já não tem "Início" desde a Fase 4.

## 7. Testes

- **Backend**: cada transição da tabela 3.3, aceita e recusada pelo `CHECK` e pelo caso de uso; a
  conservação da regra 9 depois de cada uma; a tabela 3.2 (o aviso por situação da aferição); a ordem de
  tirada da remessa (C10), inclusive "aguardando liberação não sai"; o fechamento (C12) em Peça,
  Agrupamento e Pedido, e o estorno que reabre (C13); a escrita recusada em Pedido concluído, menos o
  estorno de remessa; o status `AguardandoExpedicao` indo e voltando com o conserto; os perfis da 5.2; a
  recusa de lote nomeando a Peça copiada do catálogo; e o `db/alter-fase-5a.sql` abortando com dado.
- **Front**: os três estados da tela; o aviso do Expedir por situação; cada painel; o lote; o gating por
  perfil; a cor (pílula conforme/não conforme com tom, o resto neutro).
- **Massa**: os cenários e a massa da conferência incluem **Peça copiada do catálogo, sem descrição
  própria** (a lição da Fase 4), Peça sem relatório, e Pedido com mais de um Agrupamento.
- **Conferência no navegador, pelo usuário**, com o fluxo inteiro: aferir conforme e expedir em duas
  remessas; aferir não conforme, contar, mandar para conserto, terminar, trazer de volta, contar de novo,
  pedir liberação, a Gestão responder em parte, expedir com aviso; ver o Pedido passar a "Aguardando
  expedição" e a "Concluído"; estornar a última remessa e ver o Pedido reabrir. A review de branch espera
  essa conferência (regra do usuário de 2026-10-07, no `CLAUDE.md`).

## 8. Riscos conhecidos, aceitos

- **O livro ganha oito posições e seis tipos.** As constraints de `dbo.Movimentacao` ficam maiores, e todo
  código que enumera posições (`Posicoes`, a calculadora da execução, a consulta da Fila) precisa
  conhecê-las. É o preço do C1; a alternativa (livro paralelo) duplicaria conservação e estorno.
- **A remessa escolhe a origem sozinha** (C10). Se a Qualidade quiser expedir justamente as não conformes e
  segurar as conformes, não consegue. Nada na conversa pediu isso.
- **A contagem é em lote, não por unidade**: "4 conformes e 2 não" não diz quais. É o rastreamento por lote
  agregado do projeto, sem serial.
- **Unidade expedida antes da aferição** fica sem resultado para sempre. É o "avisar e deixar expedir" do
  D8, e o aviso fica no histórico.

## 9. Ajustes de documentação (na mesma branch)

- `specs/02-modelo-de-dados.sql`: seção 3.
- `specs/01`, `specs/00`: seção 4.
- `specs/04-fluxos-de-usuario.md`: o fluxo da Qualidade na expedição, o da Gestão na liberação, e o
  conserto nos fluxos do Operador e do Movimentador.
- `specs/05-api-endpoints.md`: as rotas e o contrato de erro.
- `specs/06-roadmap-mvp.md`: a Fase 5 dividida em 5A, 5B e 5C, com o conteúdo de cada uma (o "Descartar
  Peça ou Item em Pedido rodando", marcado **obrigatório antes do primeiro uso real**, vai para a 5B com a
  marca).
- `CLAUDE.md`: a ordem de implementação (5A, 5B, 5C), o comando do `db/alter-fase-5a.sql`, e as contagens
  das primitivas que mudarem (`PainelDeEscrita`, `useDevolverFoco`, `FiltroDeDemanda`, `BarraDoLote`).

## 10. Fora do escopo

- "Matar a peça", perda, `Descarte`, Pedido de Retrabalho e o vínculo dele com a reclamação do cliente:
  5B.
- As pontas do Kit: 5C.
- Cotas e medidas (D2); número de nota fiscal e transportadora (ERP).
- Notificação à Gestão quando há liberação pendente: é a Fase 3C (push), que executa depois da Fase 5.
- Escolher de qual posição a remessa tira (C10).

## 11. Planos

Dois, como na Fase 4: **backend** (schema, `alter`, livro, casos de uso, rotas, `01`/`02`/`05`) e **front**
(tela, painéis, Fila, Tarefas, status, `04`/`06`/`CLAUDE.md`), o de front depois do de backend.
