# Fase 5C — As pontas do Kit — desenho

Data: 2026-11-12. Branch: `claude/adoring-einstein-upfpm6`, a partir da `main` em `6926c9e`. Desenho conversado com o
usuário em chat, numa sessão de nuvem de 2026-11-12, parte por texto e parte por três áudios dele, transcritos na sessão.
As decisões do usuário são D1–D5 (seção 2.1); as chamadas do desenho, que o usuário confere ao ler, são C1–C16 (seção
2.2). O builder check (um revisor lendo a spec como quem vai planejar, conferindo as premissas contra o código) achou
uma premissa errada, a cascata da baixa, que virou C15, e uma ambiguidade, que virou C16. Nenhuma das duas pedia decisão
nova do usuário: C15 é consequência de D4. O documento escrito foi **aprovado pelo usuário em 2026-11-12**.

**Ponto de partida.** A Fase 5 foi partida em 5A, 5B e 5C em 2026-10-20. A 5B-2 (reposição) foi mesclada em 2026-11-12
(rufino-bot/rastru#37). Esta é a **5C — as pontas do Kit que a 3B deixou** (`specs/06-roadmap-mvp.md`, seção «Fase 5C»):
a perda nos tetos do Kit e a espera desalinhada (o resto da D5 da spec da 3B; C21 da 5B-1), a estrutura alterada no meio
da produção (D1 da 3B; D6 da 5B-2) e o pai Kit cujos filhos saíram todos do projeto (I2 da review de branch da 5B-1).
Depois dela vem a 3C (push), cuja posição em relação à Fase 6 não está decidida.

**O que o código tem antes desta spec** (lido em `6926c9e`):

- `CalculadoraDeExecucao.ConjuntosAEspera` conta os conjuntos à espera de um pai que recebe em conjunto
  (`RecebeEmConjunto`: Agrupamento Kit, com filhos, primeiro passo num Setor `UtilizaKit`) pelo **filho mais adiantado**:
  o maior, entre os `FilhosQueContam`, de ⌈espera em Setores `UtilizaKit` ÷ `QuantidadePorPai`⌉ (D5 da 3B).
  `TetoDeEntrada = max(0, FaltaMontar − ConjuntosAEspera)`.
- A 5B-2 acrescentou o **conjunto em formação** (`Formacao`, `ConjuntosEmFormacao`, `ComplementoDoConjunto`), que só liga
  quando algum filho chegou à montagem por um `Pronto` (`_comProntoEmMontagem`): os irmãos entram para completá-lo.
- A sobra em montagem é `ExcessoEmMontagem(filho) = max(0, AguardandoMontagemTotal(filho) − FaltaMontar(pai) × razão)`,
  no nível do nó. É ela que a Fila mostra como linha de sobra "em montagem" (`ConsultaDeExecucaoUseCase`, com o
  "Descartar" da 5B-1) e que o "Usar a sobra" da 5B-2 oferece (`CalculoDoReaproveitamento`).
- `Precisa(filho) = max(0, FaltaMontar(pai) × razão − AguardandoMontagemTotal(filho))` limita a tarefa da coleta no
  último passo (`Tarefa`) e define a sobra da coleta (`SobraDaColeta`).
- `ConjuntoCompleto.Conferir` valida a entrega para a montagem de um Kit com o `TetoDeEntrada` (e o complemento do
  conjunto em formação).
- `IniciaComoFolha(id)` exige `!DeKit` quando o nó tem filhos e nenhum conta: o pai Avulso cujos filhos saíram todos do
  projeto inicia pela Fila como folha (C22 da 5B-1); o pai Kit não aparece em lugar nenhum.
- `RoteiroDoNoUseCase` trava o Roteiro de um nó só até o último passo alcançado por alguma unidade; o resto se troca, e
  `ProximoPasso` é calculado na hora. Acrescentar filho a nó que já saiu de "a iniciar" é 409 `PaiJaIniciado` (D1 da 3B).

**O defeito, medido por leitura.** Pai C de 10, Kit, primeiro passo na Solda; filhos A (razão 2) e B (razão 1). Três
conjuntos entraram (6 A, 3 B à espera). Uma A estraga dentro da Solda, e a baixa leva junto 1 unidade de C
(`incluirTopo`, regra 27). Fica `FaltaMontar = 9`, espera 5 A e 3 B. Hoje `ConjuntosAEspera = max(⌈5÷2⌉, ⌈3÷1⌉) = 3` e o
teto é 6: o resto do conjunto quebrado (1 A, 1 B) ocupa uma vaga. Ele também **não é sobra** (5 − 9×2 < 0), então a Fila
não oferece o "Descartar" e o "Usar a sobra" do Retrabalho não o enxerga. No fim, com 17 A e 9 B à espera e 8 montados,
o teto é `9 − 9 = 0`: o nono conjunto nunca entra e o Kit trava. Os quatro caminhos da espera desalinhada (regra 25,
«Limitação conhecida») dão no mesmo defeito.

## 1. O que se quer, e o que conta como pronto

O Kit nunca trava. O que está à espera na Solda e não fecha um conjunto deixa de ocupar vaga e aparece como sobra, para
sair por descarte ou pelo "Usar a sobra" do Retrabalho. A estrutura alterada no meio da produção ganha tratamento
definido, e o pai Kit cujos filhos saíram todos aparece na Fila.

**Pronto quando:**

- No exemplo do defeito, com parte de A e de B ainda a iniciar, a baixa não descarta nada a iniciar (C15), o Kit
  continua aparecendo nas Tarefas até o nono conjunto entrar e as 9 unidades se montam; a Fila da Solda mostra 1 A e
  1 B como sobra, com "Descartar"; e o "Usar a sobra" de um Retrabalho aberto para aquela baixa oferece os dois.
- O exemplo da regra 25 (pai de 2; 7 de A com razão 4; 1 de B com razão 1) aceita um conjunto a mais e monta as 2
  unidades; as 3 A que sobram aparecem como sobra.
- O conjunto em formação do Retrabalho (D8 da 5B-2) se comporta exatamente como hoje.
- A decisão sobre a estrutura alterada está escrita em `specs/01` (regra 24), e o pai Kit cujos filhos saíram todos
  inicia pela Fila como folha.

## 2. Decisões

### 2.1 Do usuário

| # | Decisão |
|---|---|
| D1 | **A árvore não muda depois que a produção começa.** O usuário, em 2026-11-12: "até o presente momento, não houve caso da árvore da peça ser alterada durante a produção". Revisão de projeto mexe na **forma** das peças (furo, dobra, corte), não em quais peças compõem o conjunto. A guarda `PaiJaIniciado` deixa de ser a resposta provisória da 3B e passa a definitiva; a "montagem tardia" (descartada na D1 da 3B) continua descartada. |
| D2 | **Peça que dá para corrigir volta ao Setor que faz a correção** (corte, usinagem, laser, serra, dobra, solda), e a que não dá morre em processo e é refeita. Na fábrica, o caso costuma ser descoberto com a peça **ainda solta, antes da montagem**, e isso o Rastru já atende: o PCP acrescenta o passo no fim do Roteiro do nó (o trecho não alcançado se edita), e o que aguarda coleta no antigo último passo passa a ter esse passo como destino. A peça que não dá é a baixa `MortaEmProcesso` e a reposição. **Nada novo na 5C.** |
| D3 | **Revisão que chega depois do pedido** pode não entrar na produção em andamento (vai para o próximo pedido, quando o cliente não exige a atualização do que já estava em produção), ou é cobrada do cliente por outro orçamento. O orçamento novo é um Pedido novo; o Rastru não precisa de nada para isso. Quando o problema só aparece no fim, com a peça montada ou na expedição, os caminhos são o conserto da expedição (regra 35) ou esse Pedido novo. |
| D4 | **O conjunto que perde parte dentro da Solda vai para o Retrabalho, como o sistema já faz**: a baixa leva a unidade da Peça do topo (regra 27, `incluirTopo`), o resto do conjunto vira sobra no original, e o Retrabalho a marca pronta e fabrica só a peça que falta (D8 da 5B-2). O conjunto **não** se completa no Pedido original, e a frase da regra 25 ("não se completa com refugo novo") fica. *"Como o sistema já faz" vale para o caminho (baixa, sobra, Retrabalho), não para a conta: hoje o resto do conjunto não aparece como sobra, e a cascata da baixa ainda descarta as peças a iniciar que o substituiriam (C15).* |
| D5 | **Caminho A** (seção 8): no pai que recebe em conjunto, os conjuntos à espera são os **completos**; o que está à espera e não fecha conjunto é **sobra**. Escolhido sobre o caminho B (barrar na origem cada caminho de desalinhamento e corrigir só a perda na conta). |

### 2.2 Do desenho (o usuário confere)

| # | Chamada |
|---|---|
| C1 | **Uma spec só para a 5C**, com um plano (backend, mais o que o front precisar) e um PR. A estrutura alterada não tem código (D1, D2), então não há razão para partir a fase como a 5B. |
| C2 | **Conjuntos à espera = completos + em formação que valem.** `completos = min` entre os `FilhosQueContam` de ⌊espera em Setores `UtilizaKit` (menos o `saindo` do redirecionamento) ÷ razão⌋. Os "em formação que valem" são os de hoje (`Formacao`, D8 da 5B-2), e são zero fora do pai com `Pronto` em montagem. A forma de contar os em formação continua a mesma, ou seja, o ⌈espera ÷ razão⌉ do filho mais adiantado continua existindo, mas só dentro de `Formacao`. |
| C3 | **O teto de entrada não muda de fórmula**: `max(0, FaltaMontar − ConjuntosAEspera)`, com a definição nova de C2. No pai com `Pronto` em montagem o valor é o mesmo de hoje (seção 3.3). |
| C4 | **A sobra em montagem do pai que recebe em conjunto** é, por filho, `max(0, espera em Setores UtilizaKit − min(ConjuntosAEspera, max(0, FaltaMontar)) × razão)`, mais a espera em Setor sem `UtilizaKit` pela regra de hoje. Isso cobre o que não fecha conjunto e o que passa do que o pai precisa, numa conta só. O pai que não recebe em conjunto fica com o `ExcessoEmMontagem` de hoje. |
| C5 | **O que o pai ainda precisa receber de um filho** (`Precisa`) desconta a espera **menos a sobra**: `max(0, FaltaMontar × razão − (espera − sobra em montagem))`. Sem isso, o resto do conjunto quebrado continuaria reduzindo a tarefa da coleta, e uma peça boa aguardando coleta seria marcada como sobra. No pai que não recebe em conjunto o resultado é o de hoje. |
| C6 | **"Dá para montar" e o "falta para a próxima" da Fila** (`CalcularMontabilidade`) não podem convidar a completar um conjunto quebrado. No pai que recebe em conjunto, o "falta" de cada filho é calculado sobre o presente **menos a sobra**. O plano confere, na tela, o que a Fila mostra hoje para Kit: o `MontagemPendenteDto` já leva `RecebeEmConjunto`. |
| C7 | **Os caminhos de desalinhamento ficam todos abertos** e passam a dar sobra, em vez de travar: a troca Avulso↔Kit e a marca `UtilizaKit` com Kit em produção (a D4 da 3B continua: livre), e o estorno de uma linha só de uma entrega de Kit. As exceções são os dois caminhos que mudam a árvore (C8). |
| C8 | **Barrar na origem os dois caminhos que mudam a estrutura de um Kit com conjunto à espera**, coerente com D1: acrescentar filho a um pai que recebe em conjunto e tem algo aguardando montagem em Setor `UtilizaKit`, e editar a `QuantidadePorPai` de um filho desse pai. Os dois dão 409 com o código novo `PaiComConjuntoAEspera`. Sem a guarda, o filho novo com espera zero faria **toda** a espera do pai virar sobra, e a razão nova faria parte dela virar sobra de uma vez. |
| C9 | **O pai Kit cujos filhos saíram todos do projeto inicia como folha**, como o Avulso (C22 da 5B-1): `IniciaComoFolha` perde a condição `!DeKit`. Ele aparece "a iniciar" na Fila do primeiro passo e inicia por ela; `KitsDaColeta` continua pulando-o, porque não há filho a receber. |
| C10 | **Sem mudança de schema** e sem `db/alter-fase-5c.sql`: tudo é cálculo, e a recusa nova é de aplicação. |
| C11 | **O Pedido de fabricação sem perda e sem desalinhamento não muda de comportamento**: com a espera sempre em conjuntos inteiros, `completos` é igual ao ⌈⌉ do filho mais adiantado, e a sobra só aparece além do que o pai precisa, como hoje. Os testes da 3B, da 5B-1 e da 5B-2 têm de passar sem mudar de expectativa, salvo os que afirmam o defeito (o plano os lista). |
| C12 | **Kit com pai de quantidade fracionária** (Minor 9 da review de branch da 3B: nunca recebe a última fração) **fica fora**: não é caso da fábrica, e continua como dívida registrada. |
| C13 | **A tabela "Contrato de erro da Execução" de `specs/05`** ganha os códigos que já faltavam (`ConjuntoIncompleto`, `AlemDoQueOPaiPrecisa`, `PaiJaIniciado`) e o novo (`PaiComConjuntoAEspera`). |
| C14 | **Conferência do usuário no navegador**, curta, com uma massa própria (`CONF-5C`) que monte os exemplos da seção 1. Pela regra do usuário de 2026-10-07, a review de branch só é despachada depois dela. |
| C15 | **A conta da baixa usa a mesma sobra** (achado do builder check). `CalculoDaBaixa` (a `Cascata` da C6 da 5B-1, o `ViramSobra` e o `CalcularTrava`) mede o que cobre o pai pelo `EmProducao` do filho, que conta o resto do conjunto quebrado como útil. Com D4, esse resto vai para o Retrabalho; então, no pai que recebe em conjunto, o que cobre o pai é o em produção **menos a sobra em montagem depois da baixa** (seção 3.5). Sem isso, a baixa com `incluirTopo` descarta também as peças a iniciar que substituiriam o resto, e a última unidade do original nunca se completa. A conta vale também para a cascata do pronto (`levaOProntoJunto`, 5B-2), que usa a mesma `Cascata`. |
| C16 | **A espera fora de Setor `UtilizaKit`** (filho entregue antes de o PCP mudar o primeiro passo do pai, ou antes de o Setor ganhar a marca) entra na sobra **depois** dos conjuntos contados: `max(0, espera fora de UtilizaKit − max(0, FaltaMontar − contados) × razão)`. Aplicar a regra de hoje só a ela ignoraria os conjuntos que já estão na Solda e subcontaria a sobra. A divisão da sobra entre Setores no "Usar a sobra" (`CalculoDoReaproveitamento.Disponivel`) segue: primeiro o que não fecha conjunto na Solda, depois o resto; o plano escreve a ordem. |

## 3. Regras de domínio

### 3.1 Conjuntos à espera e teto (C2, C3)

Para um pai P que recebe em conjunto, com filhos que contam c₁…cₙ:

- `espera(c)` = o que c aguarda montagem em Setores `UtilizaKit`, menos o `saindo` de um redirecionamento.
- `completos(P) = minᵢ ⌊espera(cᵢ) ÷ razão(cᵢ)⌋`.
- `ConjuntosAEspera(P) = completos(P) + emFormacaoQueValem(P)`, com o segundo termo da 5B-2, zero sem `Pronto` em
  montagem.
- `TetoDeEntrada(P) = max(0, FaltaMontar(P) − ConjuntosAEspera(P))`.

Os números do exemplo do defeito: depois da baixa, `completos = min(⌊5÷2⌋, ⌊3÷1⌋) = 2`, teto `9 − 2 = 7`. Entram mais 7
conjuntos (19 A, 10 B à espera), `completos = 9`, montam-se as 9 unidades, e ficam 1 A e 1 B, que são sobra desde a
baixa.

O exemplo da regra 25: pai de 2, 7 A (razão 4), 1 B (razão 1). `completos = min(1, 1) = 1`, teto `2 − 1 = 1`. Entra um
conjunto (11 A, 2 B), `completos = 2`, montam-se 2, e as 3 A que sobram são sobra.

### 3.2 A sobra em montagem e o que o pai precisa (C4, C5, C6)

Para o filho c de um pai P que recebe em conjunto:

- `contados(P) = min(ConjuntosAEspera(P), max(0, FaltaMontar(P)))`.
- `sobraEmMontagem(c) = max(0, espera(c) − contados(P) × razão(c)) + max(0, esperaForaDoKit(c) − max(0, FaltaMontar(P)
  − contados(P)) × razão(c))`, em que `esperaForaDoKit` é o que c aguarda montagem em Setor sem `UtilizaKit` (C16).
- `Precisa(c) = max(0, FaltaMontar(P) × razão(c) − (esperaTotal(c) − sobraEmMontagem(c)))`.

No exemplo do defeito, logo depois da baixa: `contados = 2`, sobra 1 A e 1 B, e `Precisa(A) = 9×2 − (5 − 1) = 14`, que
são os 7 conjuntos que ainda entram. Hoje daria 13, e uma A boa aguardando coleta ficaria marcada como sobra.

A sobra em montagem é a que a Fila mostra na linha "em montagem" (com o "Descartar" da 5B-1, limitado à sobra da linha
na tela) e a que o "Usar a sobra" do Retrabalho oferece (`CalculoDoReaproveitamento` lê o mesmo número). Nada muda em
quem pode descartar ou reaproveitar.

### 3.3 O conjunto em formação do Retrabalho (D8 da 5B-2) continua igual (C2, C11)

No pai com `Pronto` em montagem, `ConjuntosAEspera` era `⌈⌉ do mais adiantado`, e o teto, `FaltaMontar − esse número`.
Pela definição nova, é `completos + min(⌈⌉ − completos, max(0, FaltaMontar − completos))`, e o teto que sai é o mesmo
sempre que não for zero por limitação de `FaltaMontar`, caso em que os dois dão zero. O complemento
(`ComplementoDoConjunto`) e a entrega (`ConjuntoCompleto`) não mudam. A sobra em montagem desse pai desconta os conjuntos
em formação que valem (eles estão em `contados`), então a peça pronta que espera o irmão **não** aparece como sobra. O
exemplo da 4.5 da spec da 5B-2 (pai de 1, A pronto na Solda, B razão 1) tem de continuar dando teto 0, complemento 1 de
B e sobra 0.

### 3.4 Os caminhos de desalinhamento (C7, C8)

| Caminho | Antes | Depois |
|---|---|---|
| Perda dentro da Solda com `incluirTopo` | trava no fim; o resto não é sobra | o resto é sobra desde a baixa; o Kit não trava |
| Perda dentro da Solda sem `incluirTopo` | o conjunto quebrado ocupa vaga; a última unidade não completa | o resto é sobra; um conjunto completo pode entrar no lugar; a perda da unidade continua sendo o caminho da regra 27 |
| Troca Avulso→Kit, ou marca `UtilizaKit`, com espera desalinhada | teto pode zerar | o que não fecha conjunto é sobra; o teto conta os completos |
| Estorno de uma linha só de uma entrega de Kit | teto pode zerar | o irmão que ficou na Solda é sobra até ser estornado também (ou descartado); refazer a entrega é com o conjunto inteiro |
| Editar a `QuantidadePorPai` de filho de pai com conjunto à espera | desalinha | 409 `PaiComConjuntoAEspera` |
| Acrescentar filho a pai de Kit não iniciado com conjunto à espera | o filho novo nasce sem espera; teto zera | 409 `PaiComConjuntoAEspera` |

A guarda nova (C8) olha o pai que recebe em conjunto **no momento da edição**, com algo aguardando montagem em Setor
`UtilizaKit`, líquido de estorno. Ela não substitui o `PaiJaIniciado`, que continua valendo para todo pai.

### 3.5 A conta da baixa (C15)

A baixa planeja sobre o "depois" (o `Rascunho` de `CalculoDaBaixa`). Para o filho c de um pai P que recebe em conjunto, o
que cobre P passa a ser `EmProducaoDepois(c) − sobraEmMontagemDepois(c)`, com a sobra de 3.2 calculada sobre a espera e
o `FaltaMontar` **depois** das linhas já planejadas. Isso vale nos três lugares:

- na **`Cascata`** (C6 da 5B-1): o excesso de c que sai de "a iniciar" é `max(0, (EmProducaoDepois − sobraEmMontagemDepois)
  − DeveDepois)`;
- no **`ViramSobra`**: o que a baixa faz virar sobra inclui o resto do conjunto quebrado (a prévia passa a listá-lo, e é
  o que o painel mostra como "vira sobra");
- no **`CalcularTrava`**: a produção do filho que cobre o pai desconta a sobra do Kit.

O exemplo do defeito, refeito. C de 10 Kit; A total 20 (razão 2) e B total 10; 3 conjuntos à espera; uma A perdida dentro
da Solda com `incluirTopo`. Tirada a A, a espera é 5 A e 3 B, `completos = 2`, e a sobra
já é 1 A e 1 B. A trava é `10 − ⌊(19 − 1) ÷ 2⌋ = 1`, e o painel oferece a caixa do topo, marcada (D13 da 5B-1). Com a
unidade de C planejada, `FaltaMontar` depois é 9 e a sobra continua 1 A e 1 B. A cascata faz então `(19 − 1) − 18 = 0` para A e `(10 − 1) − 9 = 0` para B, e **não** descarta
nada a iniciar. O original fica com 18 A e 9 B úteis para as 9 unidades, e o resto (1 A, 1 B) aparece no `ViramSobra` e
vai para o Retrabalho. Hoje a mesma baixa descarta 1 A e 1 B a iniciar, e a nona unidade nunca se completa.

No pai que não recebe em conjunto, a sobra em montagem do 3.2 é o `ExcessoEmMontagem` de hoje, que a baixa já trata
pela comparação com `DeveDepois`; a conta da baixa do Avulso não muda.

### 3.6 O pai Kit cujos filhos saíram todos (C9)

Com `FilhosQueContam(P)` vazia, P inicia como folha, seja Kit ou Avulso: aparece em "a iniciar" na Fila do primeiro passo
dele e a "Dá para montar" é o que falta montar. O `ApontamentoUseCase` já grava a `Montagem` sem baixa de filho (medido
na review de branch da 5B-1, I2). Como não há filho que conte, `RecebeEmConjunto` não tem o que validar na entrega e
`KitsDaColeta` não o lista.

## 4. Backend

- **`CalculadoraDeExecucao`**: `ConjuntosAEspera` (C2), a sobra em montagem do pai que recebe em conjunto (C4, no lugar do
  `ExcessoEmMontagem` para esses pais, ou dentro dele), `Precisa` (C5), o "falta para a próxima" de `CalcularMontabilidade`
  (C6) e `IniciaComoFolha` (C9). `Formacao` continua, e passa a ser o único lugar do ⌈⌉ do filho mais adiantado. Os nomes
  finais e a forma do código são do plano.
- **A guarda nova** (C8): no caso de uso que acrescenta filho e no que edita a `QuantidadePorPai` (o plano acha os dois
  pelo código), com o código `PaiComConjuntoAEspera` em `CodigosDaExecucao` ou no lugar onde mora o `PaiJaIniciado`. A
  mensagem diz o que fazer: dar baixa ou descartar o que está à espera, ou esperar a montagem.
- **`CalculoDaBaixa`** (C15): a `Cascata`, o `ViramSobra` e o `CalcularTrava` descontam a sobra em montagem do Kit,
  calculada sobre o "depois" do `Rascunho`. A calculadora precisa expor a sobra de um jeito que aceite o "depois" (o
  `saindo` do redirecionamento é o precedente); a forma é do plano.
- **Nenhuma rota nova**, nenhum DTO novo. A Fila, as Tarefas e o "Usar a sobra" mudam de números, não de forma.

## 5. Front

Espera-se **nenhuma mudança de componente**: a linha de sobra "em montagem" da Fila, o "Descartar" dela, os cartões de
Kit das Tarefas, o "Usar a sobra" e o "a iniciar" do pai que inicia como folha já existem. O plano confere duas coisas e
decide:

- se a Fila mostra para Kit o "falta X de Y para a próxima" (C6), e se a frase muda com o número novo;
- se a recusa `PaiComConjuntoAEspera` aparece no painel do nó da `AgrupamentoDetalhePage` pela frase do servidor, como as
  outras recusas da edição (deve aparecer, pelo tratamento de erro de escrita que o painel já tem).

## 6. Testes

- **Application** (calculadora, com fakes): os exemplos da seção 3, cada um afirmando `ConjuntosAEspera`, teto, sobra e
  `Precisa` antes e depois; o conjunto em formação da 5B-2 com os números de hoje (3.3); o Kit sem perda nem
  desalinhamento, para provar C11; o pai Kit cujos filhos saíram todos (C9); a guarda nova nos dois casos de uso; a baixa do 3.5 (`CalculoDaBaixa`), com
  A e B ainda em "a iniciar", afirmando que a cascata não descarta nada deles, que o `ViramSobra` lista 1 A e 1 B e que a
  trava é 1; a baixa de um Avulso com a mesma árvore, sem mudança de resultado.
- **Api** (ponta a ponta, banco real): o exemplo do defeito do começo ao fim, com parte de A e de B ainda a iniciar
  (baixa com `incluirTopo` dentro da Solda; a prévia com a sobra e sem descarte a iniciar;
  Tarefas oferecendo o Kit até o nono conjunto; Fila com a sobra; "Usar a sobra" do Retrabalho oferecendo 1 A e 1 B; as 9
  unidades montadas); a espera desalinhada pela troca Avulso→Kit; o estorno de uma linha só de uma entrega de Kit; os dois
  409 novos; o pai Kit iniciando pela Fila.
- Cada teste novo morre se a regra sumir: o plano diz, para cada um, a mutação que o mata.

## 7. Riscos conhecidos, aceitos

- **Sobra transitória no estorno.** Quem estorna só a linha de A de uma entrega de Kit vê o B que ficou na Solda como
  sobra até estornar o B também. Nesse intervalo, alguém com permissão de baixa pode descartá-lo por engano. O "Descartar"
  pede confirmação e se estorna inteiro (regra 38), e a alternativa (uma rota de estorno da entrega inteira) é código
  novo para um caso de correção.
- **Peça boa rotulada sobra depois da troca Avulso→Kit.** O que esperava solto, do tempo de Avulso, e não fecha conjunto
  passa a ser sobra, embora o pai ainda precise dele: a regra 25 só deixa entrar conjunto inteiro, então ele não seria
  usado de qualquer jeito. O rótulo adianta o que hoje só se descobre quando o Kit trava.
- **Peça solta ocupa a Solda até sair.** O motivo físico da regra 25 continua: a sobra fica lá até alguém descartá-la ou
  levá-la ao Retrabalho. Agora ela aparece na Fila, em vez de ficar escondida na conta.

## 8. Abordagens consideradas

- **A — contar só os conjuntos completos (escolhida, D5).** Uma regra resolve a perda dentro da Solda e os caminhos de
  desalinhamento, e o resto vira sobra visível, que é o que o Retrabalho reaproveita (D4). Custa uma mudança no cálculo
  central e a reconferência de tudo que o lê.
- **B — barrar na origem e corrigir só a perda.** Recusar o estorno de linha avulsa de entrega de Kit, a edição de razão e
  o filho novo com conjunto à espera, e consertar só a perda na conta. Mais código (o estorno da entrega inteira não
  existe), e a troca Avulso→Kit não se barra sem desfazer a D4 da 3B. Descartada. Da B ficou só a guarda dos dois caminhos
  que mudam a árvore (C8), que é a D1 aplicada.
- **Generalizar o conjunto em formação da 5B-2 ao Pedido de fabricação** (deixar o irmão entrar sozinho para completar o
  conjunto quebrado). Contraria D4: o conjunto quebrado vai para o Retrabalho, não se completa no original. Descartada.

## 9. Ajustes de documentação (na mesma branch)

- `specs/01`: regra **24** (a guarda `PaiJaIniciado` é definitiva, com o motivo de D1; o tratamento da estrutura alterada
  é o de D2 e D3); regra **25** (conjuntos à espera são os completos, com o termo do conjunto em formação; a «Limitação
  conhecida» sai e vira a tabela de 3.4 em prosa; a guarda `PaiComConjuntoAEspera`); regra **30** (a sobra do Kit inclui
  o que não fecha conjunto); regra **38** (a ressalva do pai Kit cujos filhos saíram todos sai; a C6, o "vira sobra" e a trava descontam a sobra
  do Kit, C15); regra **39** (a frase
  que manda a estrutura alterada para a 5C).
- `specs/05`: a tabela "Contrato de erro da Execução" (C13), e o 409 novo nas rotas de acrescentar filho e de editar o nó.
- `specs/06`: a seção «Fase 5C» com o estado, e a nota de que a estrutura alterada foi decidida sem código.
- `specs/04`, se o fluxo do PCP ou da Qualidade citar a limitação ou a estrutura alterada como pendente (o plano procura).
- `CLAUDE.md`: o bullet dos invariantes que diz que "o tratamento de estrutura alterada no meio da produção fica para a
  Fase 5", e o estado da 5C na «Ordem de implementação», só depois do merge.
- As specs de fases anteriores **não** se reescrevem; o `01` é a fonte.

## 10. Fora do escopo

- Mandar de volta a um Setor a peça já entregue à montagem ou já montada (D2; quando aparecer, é o conserto da expedição
  ou um Pedido novo).
- Rota de estorno da entrega de Kit inteira (seção 7).
- Kit com pai de quantidade fracionária (C12).
- Qualquer mudança no "a repor", no "Não repor" ou na abertura do Retrabalho.
