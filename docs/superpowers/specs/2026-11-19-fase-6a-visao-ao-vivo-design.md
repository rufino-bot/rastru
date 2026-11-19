# Fase 6A — Visão ao vivo do atraso — desenho

Data: 2026-11-19. Branch: `claude/gracious-cerf-y5sakh`, a partir da `main` em `accd69e` (pós-merge do Panorama,
rufino-bot/rastru#40, mais o conserto do `LivroMapeamentoTests`). O desenho foi conversado com o usuário em chat numa
sessão de nuvem em 2026-11-19. Os mockups da conversa são uma página privada do usuário
(`https://claude.ai/artifact/FcagZG3PtDuGxfiCn5dg6b`): A (tela própria, descartada), B (dentro do Panorama) e C (na tela
do Pedido); o usuário escolheu **B e C juntas**. As decisões do usuário são D1 a D8 (seção 2.1). As chamadas do desenho,
que o usuário confere ao ler, são C1 a C18 (seção 2.2). O builder check (um revisor lendo a spec como quem vai planejar,
conferindo as premissas contra o código) não achou premissa falsa que derrube o desenho; achou lacunas em C3, C8, C10 e
C11 e leituras que o Panorama ainda não faz, e elas foram fechadas em C3, C8, C10, C17, C18 e na seção 5. A escala da massa de C14 veio do usuário. O documento
escrito foi **aprovado pelo usuário em 2026-11-19**.

**Ponto de partida.** O Panorama está mesclado. Do roadmap restavam a 3C (push) e a Fase 6 (KPIs), sem ordem decidida;
o usuário escolheu a **Fase 6 antes da 3C** (D1) e a partiu em **6A** (esta, ao vivo) e **6B** (histórico) (D2).

## 1. O que se quer, e o que conta como pronto

**O problema.** Antes do Rastru, a única medida que a Gestão tinha era a planilha que o Panorama substituiu. Quando via um
Pedido atrasado, ou para atrasar, ela **ia até o Setor descobrir o que estava sendo feito naquele momento**, tentando
entender as complicações para produzir o Pedido. O Panorama mostra **onde** cada Peça está agora, mas não **desde quando**,
nem o que a está segurando.

**O que se quer.** Responder, sem ir até o Setor, a "**onde travou, desde quando, e o que o Rastru sabe que complica**",
em dois lugares que a Gestão já usa (D5):

- **no Panorama**, para a fábrica inteira: a idade de cada parte da Peça ("Solda 4 · há 6 d"), uma coluna **"Parado há"**
  que se ordena, e uma faceta **"Complicação"**;
- **na tela do Pedido**, para um Pedido: a seção **"Onde está travando"**, com a trilha do Roteiro de cada Peça (quanto
  tempo passou em cada Setor e onde está parada agora) e três números do Pedido (aberto há, esperou para começar, em
  produção há).

**Pronto é:**

- abrir o Panorama, ordenar por "Parado há" e ver no topo a Peça cuja parte está parada há mais tempo, com o lugar e a
  idade de cada parte;
- filtrar o Panorama por complicação (material pendente, aguardando liberação…);
- abrir um Pedido atrasado e ver, por Peça, em que passo do Roteiro as unidades estão, há quanto tempo, quanto tempo
  ficaram nos passos anteriores, e o que o Rastru sabe que as segura;
- nada disso pede digitação nova a ninguém;
- o `GET /panorama` cabe no limite de desempenho de C14 com a massa de carga de C14.

**Sucesso:** a Gestão vai ao Setor para conversar sobre o porquê, e não para descobrir onde a Peça está e desde quando.

### 1.1 Não objetivos

- **Histórico e KPI agregado** (tempo médio por Setor, pontualidade, conformidade, perda, retrabalho): são da **6B**, com
  brainstorming próprio. A 6A só monta o cálculo que a 6B vai reusar (D6) e mostra o tempo por passo **de um Pedido**.
- **O porquê que o operador explicaria** (máquina parada, dúvida no desenho, falta de gente): o Rastru não registra, e a
  6A não cria campo de motivo. A conversa no Setor continua para isso.
- **Separar fila de trabalho dentro do Setor** (o "comecei" do operador): fora. O tempo "no Setor" é da chegada ao
  "terminei" e inclui a espera dentro do Setor (C10).
- **Tempo útil**: não há turno nem calendário de trabalho cadastrado; todo tempo é de calendário (C1).
- **Tela própria de atrasos** (o mockup A): descartada pelo usuário (D5).

### 1.2 Anti-objetivos (falharia mesmo funcionando)

- Mostrar uma idade que mente: a do saldo velho escondido atrás de uma chegada nova, ou a de unidades que já saíram. É
  por isso que a idade vem do pareamento por ordem de chegada, e não da primeira ou da última entrada (seção 8).
- Ranquear pessoas: o livro tem o autor de cada movimento, e a 6A **não** mostra idade nem tempo por usuário.
- Pedir apontamento novo só para alimentar a tela.
- Tornar o Panorama lento a ponto de a recarga de 30 s pesar na fábrica (C14).

## 2. Decisões

### 2.1 Do usuário

| | Decisão |
|---|---|
| D1 | A Fase 6 vem **antes da 3C** (2026-11-19). |
| D2 | A Fase 6 se parte em **6A** (ao vivo) e **6B** (histórico), nesta ordem, cada uma com spec, plano e PR próprios, como a Fase 5. |
| D3 | A 6A existe para a pergunta da Gestão diante de um atraso: **onde travou, desde quando e o que complica**. A 6B existe para a Qualidade (e a Gestão) estudarem os atrasos depois, "tentando entender as dores de cada Setor". |
| D4 | **Perfis:** a 6A é leitura de **todo perfil autenticado**, como o Panorama. A 6B será de **Gestão, Qualidade, PCP e Administrador** (emenda o "Perfil Gestão tem acesso a essas telas; demais perfis não" de `specs/06`). |
| D5 | **Forma:** mockups **B e C juntos**: a idade entra no Panorama (no "Onde está", na coluna "Parado há" e na faceta "Complicação") e na tela do Pedido (a seção "Onde está travando"). **Sem** tela própria. |
| D6 | **Cálculo:** pareamento por ordem de chegada (a primeira parte que chegou é a primeira que sai), **no C#**, num cálculo puro sobre as linhas do livro, reusável pela 6B. |
| D7 | **Desempenho:** medir o `GET /panorama` com massa carregada **antes do merge**. Passou do limite: primeiro um **cache em memória** (`IMemoryCache`) válido enquanto o maior `Id` da `Movimentacao` não muda (até a emenda F2, seção 11); só depois, se ainda faltar, o pareamento no SQL. **Redis**, só quando houver mais de uma instância da API. |
| D8 | Para a 6B: a **pontualidade conta pela última remessa**, que é a data em que o Pedido conclui (`Pedido.DataConclusao`). Registrado aqui para a 6B não reabrir. |

### 2.2 Do desenho (o usuário confere)

| | Chamada |
|---|---|
| C1 | **Tempo de calendário.** A idade de uma parte é `agora − data de chegada`, sem descontar noite, fim de semana nem pausa. A pausa do Pedido continua aparecendo como já aparece (pílula "Pausado", `atencao`). |
| C2 | **O que se pareia.** Para cada nó e cada **lugar** (o `Local` da Execução: posição, Setor e passo), as linhas que **põem** quantidade no lugar são chegadas, as que **tiram** são saídas, e as saídas consomem as chegadas mais antigas primeiro. O que sobra são as **partes vivas** do saldo, cada uma com quantidade e data de chegada. A soma das partes vivas de um lugar é igual ao saldo que a `CalculadoraDeExecucao` já dá para ele; o cálculo afirma isso em teste. |
| C3 | **Estornos.** Uma linha estornada e o estorno dela saem **os dois** do pareamento, e o pareamento é refeito sobre o livro líquido, **como se o movimento estornado nunca tivesse existido**. Consequência aceita: uma saída que aconteceu entre o movimento e o estorno dele passa a consumir outra parte (a mais antiga viva sem o estornado), e a idade de uma parte pode mudar depois de um estorno. Como o lote não tem identidade (regra 9, sem serial), não há "a unidade que de fato saiu": a ordem de chegada sobre o livro líquido é a única leitura coerente. Teste com esse caso. |
| C4 | **"A iniciar" não tem linha de chegada** (a quantidade nasce no nó). A data de chegada dele é o `Agrupamento.CriadoEm` do nó, que é a data mais antiga em que a Peça pode ter existido (o `EstruturaItem` não tem data própria). O aumento de quantidade depois disso herda a mesma data: aproximação aceita (seção 7). |
| C5 | **O que conta como parado.** Toda posição que não é terminal: ficam de fora `Montado`, `Expedido` e `Perdido`. **Exceção:** o "a iniciar" de um nó **com filhos** não conta, porque ele espera a montagem, e quem está parado são os filhos (que contam). O local de expedição, o conserto e o aguardando montagem contam. |
| C6 | **No Panorama.** Cada segmento do "Onde está" (os de `DistribuicaoDaPeca`) ganha a idade da **parte mais antiga** dele; o segmento de passo, que já separa o aguardando coleta, ganha duas ("Solda 4 · há 6 d", "coleta 3 · há 2 d"). A coluna **"Parado há"** é a maior idade entre as partes que contam (C5) da Peça **e dos descendentes dela**; ela se ordena pelo `CabecalhoOrdenavel` (e entra no `SeletorDeOrdem` do celular). Os descendentes continuam sem número próprio no "Itens em" (D13 do Panorama): a idade deles aparece só no "Parado há" e na tela do Pedido. Peça sem parte que conta (tudo expedido ou perdido não entra no Panorama; tudo montado também não): "Parado há" vazio. |
| C7 | **Formato da idade.** Menos de 1 h: "há menos de 1 h"; menos de 24 h: "há N h"; daí em diante, "há N d", em dias inteiros arredondados para baixo. Mesma função no Panorama e no Pedido, com teste. A ordenação usa a idade exata, não o texto. |
| C8 | **As complicações** (o que o Rastru sabe), calculadas no servidor por Peça, olhando a Peça e os descendentes: **material pendente** (algum nó com "a iniciar" > 0 e `ResumoDoMaterial.Pendencia` > 0, a mesma regra 34 do `CalculoDaSeparacao`), **aguardando liberação** (saldo em `AguardandoLiberacao`), **aguardando decisão** (saldo em `NaoConforme`), **em conserto** (saldo nas três posições do conserto), **montagem incompleta** (filho em `AguardandoMontagem` enquanto o pai não pode iniciar por falta de outro filho, o mesmo "faltam N" que a Fila já calcula) e **a repor** (unidades a repor da 5B-2, pelas mesmas contas de `ConsultaDeReposicao.UnidadesARepor`). **Montagem incompleta**, precisamente: um filho do nó tem saldo em `AguardandoMontagem` no Setor do passo do pai, e o pai não inicia ali porque algum filho tem `FilhoNaMontagem.FaltaParaProxima` > 0 (o "faltam N" que a Fila mostra), para Kit e Avulso, pela mesma `CalculadoraDeExecucao`; o texto é "Montagem: faltam N de {filho}". **A repor**, precisamente: marca a Peça quando uma baixa ou reclamação com unidades a repor aponta para ela ou para um descendente; a baixa de escopo Agrupamento ou Pedido (o "Descartar tudo") marca as Peças do escopo. Não marca todas as Peças do Pedido só porque o Pedido tem algo a repor. **Material pendente**, precisamente: vale para qualquer nó com "a iniciar" > 0, inclusive o pai com filhos (a exclusão de C5 é da idade, não do material, porque o pai também precisa do material dele para iniciar); o texto nomeia o primeiro material pendente e "e mais N" quando houver outros. A **pausa** continua onde está (na célula do prazo) e também é opção da faceta. Cada uma vem com um código estável e um texto curto ("Material não separado: Chapa 3 mm"). |
| C9 | **Cor.** Idade, "Parado há", complicações e a marca do mais parado são **neutros** (`tinta`, `tinta-fraca`, `Pilula` neutra, peso de fonte e borda). Ficar parado não é aprovado nem reprovado, e material faltando já é neutro pela regra da separação. Só o que já tem tom continua com ele: "Atrasado" (`atraso`), "Pausado" (`atencao`), "Conforme" e "Não conforme" da aferição. |
| C10 | **Na tela do Pedido**, a seção **"Onde está travando"** (`<h2>`) fica entre o cabeçalho e "Agrupamentos", e só aparece para Pedido não encerrado (nem `Concluido` nem `Cancelado`). Por Peça ainda em aberto, uma **trilha**: o bloco "A iniciar" (quando conta, C5), um bloco por passo do Roteiro da Peça (passado: "passou · N d no Setor"; atual: quantidade e idade, com o aguardando coleta à parte; futuro: "ainda não chegou", borda tracejada) o bloco **"Aguardando montagem · {Setor}"** quando o nó (tipicamente um Item) tem saldo esperando o pai, com quantidade, idade e a complicação "montagem incompleta" quando houver, e os blocos do conserto e do local de expedição quando houver saldo, com as complicações dela embaixo. **"passou · N d"** é a média, ponderada pela quantidade, do tempo de cada parte que já saiu daquele passo, da chegada ao "terminei"; a espera de coleta não entra (é da 6B separar). Os **Itens** da Peça ficam numa `ListaRecolhivel` "Itens (N)" sob a trilha, cada um com a trilha dele, **aberta** quando o mais parado do Pedido é um Item. O bloco **mais parado do Pedido** ganha a marca "mais parado" (borda mais grossa, sem cor). No celular, a trilha empilha na vertical. |
| C11 | **Os três números do Pedido**, no topo da seção: **aberto há** (`agora − DataAbertura`); **esperou para começar** (início real da regra 14 `− DataAbertura`, ou "ainda não começou"); **em produção há** (`agora − início real`), com "dos quais N pausado" quando houve pausa depois do início (a soma de `PausadoEm → RetomadoEm`, ou até agora para a pausa aberta). |
| C12 | **Contrato.** O servidor manda **datas**, e o cliente calcula a idade contra o `agora` que vem na mesma resposta (o relógio do servidor), para o celular com relógio errado não mentir. O `GET /panorama` ganha `agora`, o `desde` (e o `aguardandoColetaDesde`) de cada segmento, o `paradoDesde` e as `complicacoes` de cada Peça. Rota nova `GET /pedidos/{id}/andamento` para a seção do Pedido. Formas na seção 5. |
| C13 | **Perfis** (D4): as duas leituras são de todo perfil autenticado. Nenhuma escrita nova, então nenhuma entrada nova em `permissoes.ts`. |
| C14 | **Medição** (D7): uma massa de carga **gerada por script** (`db/massa-carga-6a.sql`, idempotente, prefixo `CARGA-6A-`, fora do `seed-demo.sql`) com o **dobro da escala real** que o usuário informou (2026-11-19: uns 25 Pedidos abertos ao mesmo tempo, com umas 10 Peças cada): **50 Pedidos abertos, 10 Peças cada (500 Peças), 5 Itens por Peça (3.000 nós) e uma média de 20 linhas de livro por nó (60.000 linhas)**. Os Itens por Peça e as linhas por nó são estimativa do desenho, não número da fábrica. **Limite:** o `GET /panorama` responde em **até 1 s no servidor**, mediana de 10 chamadas seguidas, na máquina de dev. Passou: o cache de D7, e medir de novo. |
| C15 | **Sem mudança de schema.** O `IX_Movimentacao_EstruturaItem` serve a leitura das linhas por nó. Sem `alter` novo. |
| C16 | **O cálculo puro** se chama `CalculoDaIdade` (em `src/Rastreamento.Application/Execucao/`, ao lado da `CalculadoraDeExecucao`), recebe as linhas do livro de um conjunto de nós, as datas de criação dos Agrupamentos e devolve as partes vivas por (nó, lugar), mais, por (nó, passo), as permanências das partes que já saíram (para o "passou · N d" da 6A e o tempo por Setor da 6B). Não lê banco. |
| C17 | **Início real** (regra 14) **ainda não existe em código** (só a verificação de "nunca teve início" do fechamento): nasce como função pura ao lado do `CalculoDaIdade`, seguindo a regra 14 inteira — a menor data das primeiras entradas dos nós do Pedido, líquida de estorno; a `Devolucao` conta como início e o `Pronto` não. Com teste por caso, inclusive o Retrabalho de consertar (início na data da devolução). A 6B reusa. |
| C18 | **A chegada a um passo** é a linha que põe quantidade em `NoSetor` dele: o `Inicio` no primeiro passo (o operador ao pegar o material) e a `Entrega` nos seguintes (o Movimentador). A idade "no Setor" inclui a espera antes do trabalho começar, e é isso que o rótulo "no Setor" diz (seção 1.1). |

## 3. Regras

### 3.1 A idade de uma parte

- Uma **chegada** é uma linha do livro cujo destino é o lugar; uma **saída**, uma linha cuja origem é o lugar. As
  linhas são consideradas na ordem de `DataHora` e, no empate, de `Id`.
- Uma saída de quantidade `q` consome `q` das chegadas mais antigas ainda vivas daquele (nó, lugar), dividindo uma
  chegada quando preciso. O que não foi consumido são as partes vivas, com a data da chegada de que vieram.
- "A iniciar" começa com uma chegada implícita de `Quantidade` do nó na data `Agrupamento.CriadoEm` (C4).
- Estornos: C3. A posição `Perdido` e as demais terminais não entram (C5).
- **Invariante** (testado): para todo (nó, lugar), a soma das partes vivas é igual ao saldo da `CalculadoraDeExecucao`.
  Uma saída maior que as chegadas vivas não deveria existir (o livro não deixa saldo negativo); se aparecer, o cálculo
  não quebra a tela: consome o que houver e o teste do invariante é quem denuncia.

### 3.2 O tempo de um passo que já passou

- Para cada parte que saiu de `NoSetor` de um passo pelo "terminei" (`Termino`), a permanência é `DataHora do Termino −
  data de chegada da parte`. "passou · N d" (C10) é a média dessas permanências ponderada pela quantidade.
- A parte que saiu do Setor por outro caminho (baixa, montagem do pai no mesmo Setor) não entra na média: não terminou ali.

### 3.3 As complicações

C8 é a lista. Cada uma reusa a conta que já existe (separação, posições do livro, Fila, reposição); **nenhuma conta nova
de regra de negócio** nasce aqui, só a leitura em lote para todas as Peças.

## 4. Banco

Nada muda (C15). A massa de carga (C14) é script de dev, não schema.

## 5. Backend

### 5.1 `CalculoDaIdade` (C16)

Função pura com testes em `Application.Tests`: chegada e saída simples; saída que divide uma chegada; duas chegadas e uma
saída maior que a primeira; estorno de chegada; estorno de saída (a parte volta com a data original); "a iniciar"
implícito; o invariante contra a `CalculadoraDeExecucao` num cenário com montagem, conserto e remessa; permanência
ponderada de um passo.

### 5.2 `GET /panorama` *(qualquer perfil autenticado)*

O use case do Panorama passa a ler também as linhas do livro dos nós que ele já carrega, numa consulta só para todos, e o
`CalculoDaIdade` sobre elas. **Leituras novas** (o builder check conferiu que não existem): as linhas do livro em lote
(hoje só há `ListarMovimentacoesDoNoAsync`, por nó); o `Agrupamento.CriadoEm` de cada nó (o `ContextoDoNo` não o traz);
o planejado, as separações e os materiais dos nós (a dependência de separação que o `ConsultaDeExecucaoUseCase` usa em
`ResumosDeMaterialAsync`, nova no use case do Panorama); as baixas e reclamações a repor por Peça. Todas em lote, uma
consulta cada, e todas dentro da medição de C14. Os tipos `Segmento` e `SegmentoDto` ganham as datas. Acréscimos ao DTO (o builder
ajusta nomes ao padrão vizinho):

```jsonc
{
  "agora": "2026-11-19T12:42:00Z",
  "pecas": [
    {
      // … o que já existe …
      "distribuicao": [
        { "tipo": "Passo", "setorId": 4, "ordem": 2, "quantidade": 7, "aguardandoColeta": 3,
          "desde": "2026-11-13T09:10:00Z", "aguardandoColetaDesde": "2026-11-17T15:02:00Z" }
      ],
      "paradoDesde": "2026-11-05T10:00:00Z",   // null quando nenhuma parte conta (C5, C6)
      "complicacoes": [ { "codigo": "MaterialPendente", "texto": "Material não separado: Chapa 3 mm" } ]
    }
  ]
}
```

`Expedido` e `Perdido` não têm `desde`. A ordem de C9 do Panorama não muda.

### 5.3 `GET /pedidos/{id}/andamento` *(qualquer perfil autenticado)*

404 para Pedido inexistente. Pedido encerrado devolve os números e `pecas: []` (a tela não mostra a seção, C10).

```jsonc
{
  "agora": "…",
  "abertura": "…", "inicioReal": "…",          // null: ainda não começou
  "pausadoDepoisDoInicio": "P5D",                // duração; ou segundos, a critério do builder
  "maisParado": { "estruturaItemId": 812, "bloco": "Passo:2" },
  "pecas": [
    {
      "estruturaItemId": 812, "codigo": "BASE-200", "descricao": "…", "quantidade": 10,
      "trilha": [
        { "tipo": "AIniciar", "quantidade": 3, "desde": "…" },
        { "tipo": "Passo", "ordem": 1, "setor": "Corte", "situacao": "Passado", "permanenciaMedia": "P1D" },
        { "tipo": "Passo", "ordem": 2, "setor": "Solda", "situacao": "Atual", "quantidade": 4, "desde": "…",
          "aguardandoColeta": 0 },
        { "tipo": "Passo", "ordem": 4, "setor": "Pintura", "situacao": "Futuro" }
      ],
      "complicacoes": [ … ],
      "itens": [ /* a mesma forma, recursiva, só com os Itens que têm algo que conta ou já passaram por passo */ ]
    }
  ]
}
```

Um passo pode ser passado **e** atual ao mesmo tempo (parte já saiu, parte ainda está): o builder escolhe se vira um
bloco com as duas coisas ou dois campos no mesmo bloco; a tela mostra as duas.

### 5.4 Desempenho (C14, D7)

O builder escreve a massa de carga, mede o `GET /panorama` antes e depois da 6A, e registra os números no ledger do plano.
Passou do limite, o cache de D7 (até a emenda F2, seção 11): chave pelo maior `Movimentacao.Id` (uma consulta `MAX(Id)` por requisição), guardando a
resposta do Panorama **sem o `agora`**, que é refeito a cada requisição (senão a idade congelaria entre escritas); qualquer escrita que não passa pelo livro e muda o Panorama (a embalagem, a pausa, a
separação, a edição de estrutura) também tem de invalidar, e o builder lista essas escritas no plano antes de escolher
entre chave composta e invalidação explícita.

**Emenda do plano (2026-11-19).** O plano fecha a escolha que este parágrafo deixava: o cache guarda **só o resultado do
`CalculoDaIdade`**, com chave no maior `Movimentacao.Id` e na lista de `(Id, Quantidade)` dos nós (até a emenda F2, seção 11), e não a resposta
inteira. As idades só dependem do livro, das quantidades e do `Agrupamento.CriadoEm`; então nenhuma escrita fora do livro
precisa invalidar o cache, e o resto do Panorama segue lido a cada requisição, como antes da 6A.

### 5.5 Testes de API

Panorama: `desde`, `paradoDesde` e uma complicação de cada tipo num cenário real; estorno que devolve a parte com a data
original. Andamento: 404; Pedido encerrado; Pedido não iniciado ("ainda não começou"); pausa depois do início; Item como
mais parado.

## 6. Front

- **Idade:** uma função pura `idadeDesde(desde, agora)` com o formato de C7, em `web/src/panorama/` (a tela do Pedido a
  importa de lá), com teste.
- **Panorama:** o "Onde está" mostra a idade de cada segmento; coluna "Parado há" com `CabecalhoOrdenavel` (e a opção no
  `SeletorDeOrdem` do celular); coluna "Complicação" com os textos; faceta "Complicação" no `FiltroDeDemanda` (as opções
  de C8, a pausa e "Nenhuma"), com `casaComFiltro` e a URL como as outras facetas. O cartão do celular ganha os mesmos
  dados. Os três estados não mudam.
- **Pedido:** a seção "Onde está travando" na `PedidoDetalhePage`, num componente de `web/src/andamento/` (com teste),
  que busca `GET /pedidos/{id}/andamento` com os três estados próprios e recarrega junto da página quando ela recarrega.
  A trilha usa primitivas existentes (`ListaRecolhivel` para os Itens); se faltar uma (o bloco da trilha), ela nasce em
  `web/src/components/` com teste e vira bullet em «Interface» do `CLAUDE.md`.
- Cores: C9. `npm run build` no ciclo.

## 7. Riscos conhecidos, aceitos

- **"A iniciar" usa a data do Agrupamento** (C4): a Peça cuja quantidade foi aumentada depois aparece mais velha do que é
  na parte acrescentada, e a do Agrupamento criado muito antes do Pedido começar aparece "parada" desde a criação. É a
  verdade do que o Rastru sabe: a Peça existia e não tinha começado.
- **Tempo de calendário** (C1): um Setor que não trabalha no fim de semana parece mais lento na segunda-feira.
- **"No Setor" inclui a fila dentro do Setor** (C10, seção 1.1): "passou · 3 d na Solda" não diz quanto disso foi solda.
- **O custo do Panorama cresce** com as linhas do livro dos Pedidos abertos; D7 e C14 são a rede.
- **As complicações são só as que o Rastru registra** (seção 1.1).

## 8. Abordagens consideradas

**Para o lugar** (escolha do usuário, D5): A, tela própria "Atrasos", com um cartão por Pedido (descartada: repetiria o
Panorama agrupado por Pedido); B, dentro do Panorama; C, na tela do Pedido. Escolhidas **B e C juntas**: o Panorama
mostra o que está parado na fábrica toda, e o Pedido mostra onde e desde quando.

**Para o cálculo** (escolha do usuário, D6):

1. **Pareamento por ordem de chegada no C#, sobre as linhas do livro** — **escolhido.** Segue o padrão dos `Calculo…`
   do projeto, testa sem banco, a 6B reusa. Custo: o Panorama passa a ler as linhas do livro dos Pedidos abertos.
2. **Pareamento no SQL**, com somas acumuladas. Mais leve no tráfego, mas a regra fica em SQL, testável só com banco, os
   estornos complicam a consulta, e a 6B repetiria a lógica. Fica como segundo recurso de D7.
3. **Primeira ou última entrada na posição**, sem parear. Barato, mas mente com lote dividido (seção 1.2). Descartado.

**Para o cache** (D7): Redis contra cache em memória. O Rastru é uma instância numa VPS; o livro só de inclusão dá uma
chave exata (o maior `Id`), sem defasagem de TTL; e o custo cresce com o número de cálculos, não de pessoas olhando.
Cache em memória, e só se a medição pedir.

## 9. Ajustes de documentação (na mesma branch)

- `specs/06-roadmap-mvp.md`: a Fase 6 partida em 6A e 6B (D2), antes da 3C (D1), e o acesso de D4.
- `specs/04-fluxos-de-usuario.md`: o fluxo 7 (KPIs) passa a apontar para 6A e 6B, e o fluxo 8 (Panorama) ganha a idade
  e a faceta; um fluxo novo para "Onde está travando" na tela do Pedido.
- `specs/05-api-endpoints.md`: os acréscimos do `GET /panorama` e a rota `GET /pedidos/{id}/andamento`; a seção «KPIs»
  passa a dizer que as rotas de lá são da 6B.
- `specs/01-dominio-e-regras-de-negocio.md`: a regra da idade (seção 3.1) e do "passou" (3.2), como regra nova, e o
  termo "Parado há" no glossário.
- `CLAUDE.md`: a «Ordem de implementação» com a 6A, e o bullet do Panorama em «Interface» com a idade e a faceta.

## 10. Fora do escopo

Tudo da 6B (seção 1.1); o "comecei" do operador; campo de motivo de atraso; tempo útil por turno; notificação de Peça
parada (seria da 3C); idade por usuário (seção 1.2).

## 11. Emendas da conferência (2026-11-19)

- **F2 — o cache de D7 passa a ser incremental.** Medido na conferência: com a chave no maior `Id` global do livro,
  qualquer apontamento invalidava o cache inteiro, e a chamada sem cache levava 1,01 s contra 0,68 s da `main`. O
  `CacheDasIdades` (singleton) guarda as linhas por nó e o maior `Id` lido; a cada chamada lê só as linhas novas,
  relendo as últimas 1.000 posições de `Id` para pegar o commit fora de ordem, e refaz o pareamento só dos nós
  tocados. A cada 10 min ele lê tudo de novo. Sai o `IMemoryCache`.
- **F1 — a trilha vira uma linha do Roteiro.** O usuário achou o cartão de blocos poluído e escolheu, entre três
  desenhos, a linha do Roteiro: um ponto por passo e a Expedição por último, ocupando sempre a largura do cartão, com o
  rótulo encolhendo pela largura de cada ponto (nome, número e nome, só número; o ponto atual mantém o nome), e embaixo
  só o que tem peça agora, por extenso, com os passos já passados numa linha. No celular, vertical, com passados e
  futuros consecutivos agrupados. Montagem e conserto continuam blocos da trilha (C10), mas aparecem só na lista, não
  na linha. A borda tracejada do passo futuro de C10 vira o ponto vazio.
  As frases concordam em número ("1 pronta", "Já passou", "já passou", "ainda não chegou") e, no celular, a lista
  termina em "Expedição · ainda não chegou" quando a trilha não tem bloco de Expedição.
