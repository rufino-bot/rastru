# Data de entrega do Pedido — desenho

Data: 2026-10-08. Branch: `data-de-entrega`, a partir da `main` em `b35cae8`. Desenho aprovado pelo
usuário em chat na mesma data: oito perguntas de produto, a abordagem técnica e as quatro seções do
desenho (schema, backend, front, testes), cada uma aprovada em separado.

**Ponto de partida.** A §5 da spec da Fase 1E
(`docs/superpowers/specs/2026-08-16-fase-1e-refinamento-visual-design.md`) registrou "prazo de entrega" e
"pedidos em atraso" como candidato de fase futura: exige a coluna em `dbo.Pedido`, o campo no cadastro e
a definição de "atraso" no `01`. Esta spec é esse candidato.

**Quando executa.** **Antes da Fase 3B**, por decisão do usuário (D1). É exceção à ordem das fases, e o
`CLAUDE.md` só admite exceção declarada por escrito em `specs/06-roadmap-mvp.md`. O registro faz parte
dos ajustes da seção 9 e vem **antes** do plano.

> **Emenda de 2026-10-09 (decisões do plano que mudam o corpo desta spec).** O plano
> (`docs/superpowers/plans/2026-10-08-data-de-entrega-do-pedido.md`, seção "Decisões deste plano", P1 a P7)
> decidiu, em quatro delas, algo diferente do que o corpo desta spec afirma, e o texto das seções fica como
> foi desenhado; onde ele diverge, vale esta nota. As outras três (P3, P5 e P6) detalham o que a spec deixou em aberto
> e não a contradizem.
>
> - **§6.5, "No sucesso, o painel fecha e o Pedido é recarregado" (P2).** A tela aplica a resposta do
>   `PUT /pedidos/{id}`, que já é o `PedidoDto` inteiro (com `pausa` e `atrasado`), e não faz segunda
>   requisição nem recarrega os Agrupamentos. Vale: o painel fecha e o Pedido passa a ser o devolvido pelo
>   `PUT`.
> - **§6.5, "Entrega em 22/10/2026" no cabeçalho (P7).** O prazo não é uma frase isolada; ele vai na mesma
>   linha das pílulas, no formato da `LinhaDePedido` ("entrega em … · aberto em …").
> - **§7.1, os `new Pedido` que "passam a informar a data" (P1).** Informam a data os montados para o
>   **banco** (`Infrastructure.Tests` e `Api.Tests`), onde `0001-01-01` viraria o Pedido mais atrasado da
>   tabela compartilhada. Em `Application.Tests`, que roda contra fake, a data só entra no teste que é sobre
>   ela.
> - **§7.1, a ordem `entrega` com "`Tamanho = int.MaxValue` direto no repositório" (P4).** Os testes de
>   banco da ordem usam o `Filtro` do helper existente, com a página de 100, escopados pela busca do cliente
>   único de cada teste.
> - **§7.1, o teste de banco de `ListarMaisUrgentesAsync` com `quantos = int.MaxValue` (P4).** Ele chama com
>   `quantos = 5`, com seis candidatos próprios, e por isso afirma também o corte em cinco, e não só a
>   ordem relativa. O padrão é o do teste que já existia para `ListarMaisAntigosAsync`, robusto a linhas de
>   terceiros.

## 1. O que se quer, e o que conta como pronto

**Problema.** O Pedido não tem prazo. A Home mostra os cinco Pedidos abertos **há mais tempo**, o que
responde "o que é velho", mas não responde "o que está atrasado ou para vencer", que é a pergunta da
operação.

**Pronto é:**

- todo Pedido novo nasce com data de entrega, informada no cadastro;
- quem tem escrita em Pedidos (hoje PCP e Administrador) edita número, cliente e data de entrega na
  página do Pedido;
- a lista de Pedidos mostra a data de entrega e a pílula **Atrasado** e abre ordenada por prazo;
- a seção da Home vira **"Prazos de entrega"**: os cinco Pedidos não encerrados de prazo mais urgente,
  os atrasados primeiro.

## 2. Decisões tomadas na conversa

| # | Decisão | Alternativas descartadas |
|---|---|---|
| D1 | Executa **antes da 3B**, como item próprio, em branch própria. **Não** é emendado na 3B, como foi o título do Agrupamento: não depende de Kit nem de montagem | emendar na 3B; depois da 3B |
| D2 | A data de entrega é **obrigatória de ponta a ponta**: `NOT NULL` no banco, obrigatória no cadastro e na edição | obrigatória no formulário com coluna nula; opcional |
| D3 | **Qualquer data é aceita**, inclusive no passado; só a ausência é recusada. Motivo do usuário: um Retrabalho pode ser aberto pelo cliente bem depois de as peças terem sido expedidas, e um Pedido pode chegar ao sistema já atrasado | recusar data anterior a hoje no cadastro; recusar data anterior à `DataAbertura` (`CHECK`) |
| D4 | **Retrabalho (regra para a Fase 5):** o formulário do Pedido de Retrabalho traz, por padrão, a `DataEntrega` do Pedido de origem, e o campo é editável no cadastro | — |
| D5 | O atraso tem **tom próprio**, o quarto estado reservado: roxo `#6B21A8` sobre `#F3E8FF` (7,39:1). Motivo: a mesma linha pode estar reprovada **e** atrasada (na Fila do Setor, num item seguinte; na Qualidade, na Fase 5), e as duas em vermelho deixariam de se distinguir | `negativo` (vermelho, ampliando o significado dele); `atencao` (âmbar, o mesmo do Pausado); sem pílula, só texto; o roxo mais fechado `#581C87` |
| D6 | Neste item, a pílula vai **só à lista de Pedidos, à Home e à página do Pedido**. A Fila do Setor e as Tarefas recebem a pílula num **item seguinte** (seção 10), com o tom já decidido | levar já à Fila e às Tarefas; levar e ainda ordenar a Fila por prazo (reabriria a decisão 8 da 1F) |
| D7 | A edição cobre **número, cliente e data de entrega no mesmo painel**, pelo `PUT /pedidos/{id}` que já existe | só a data (pediria endpoint novo ou um `PUT` que ignora parte do corpo); os três com a data travada no Pedido encerrado |
| D8 | A edição vale **em qualquer status**. Segue a política da Fase 1: o Pedido é um documento, não se inativa nem se exclui, e se corrige por edição | só enquanto não encerrado (congelaria o prazo para um KPI de pontualidade que nenhuma fase pede hoje); regra por campo |
| D9 | **Auditoria de cadastro fica fora**, registrada como lacuna para uma spec própria (seção 8) | "último editor" no Pedido; histórico de alterações do Pedido |
| D10 | A lista ganha a ordem **"Prazo de entrega"**, e ela é a **padrão** da tela e do servidor. Emenda a decisão 9 da 1F só para Pedidos | a opção sem virar padrão; nenhuma ordem nova |
| D11 | **Salvar um Pedido novo leva a lista a "Mais recentes"** (`?ordem=recentes`), com busca e filtros zerados, na página 1, e o Pedido novo aparece no topo. Emenda a decisão 7 da 1F só para Pedidos: salvar devolve à consulta **em que o item novo aparece**, não ao padrão | salvar navega ao detalhe do Pedido novo; salvar volta ao padrão por prazo (o Pedido novo pode cair fora da página) |
| D12 | **O backend decide o atraso** e o entrega pronto no `PedidoDto` (`Atrasado`). O "hoje" é a data de Brasília, vinda de um `TimeProvider` | o front calcula pelo relógio do aparelho (a regra repetida no navegador, e o "hoje" com duas fontes, porque a ordem continua no servidor); coluna calculada no SQL (depende de *agora*, não pode ser `PERSISTED`) |
| D13 | A seção da Home se chama **"Prazos de entrega"**, e a opção da ordem, **"Prazo de entrega"**. Motivo do usuário: "mais próximas" não seria acurado, porque um atrasado pode estar mais longe de hoje que um que vence amanhã, e vem antes dele | "Entregas mais próximas"; "Entrega (mais próxima)" |

## 3. Modelo de dados

### 3.1 A coluna

Em `specs/02-modelo-de-dados.sql`, primeiro (Database First), logo depois de `DataAbertura`:

```sql
DataEntrega         DATE                NOT NULL,  -- prazo combinado com o cliente; dia, sem hora nem fuso
```

- **`DATE`, não `DATETIME2`.** O prazo é um dia, não um instante. A borda de fuso da aplicação, o
  `HorarioDeBrasiliaJsonConverter`, trata `DateTime`: por dentro tudo é UTC, e a saída vai com offset
  -03:00. Um prazo guardado como `DateTime` à meia-noite UTC sairia como o dia anterior às 21h, e o
  prazo andaria um dia para trás sem nenhum teste reclamar.
- **Sem `DEFAULT`**: quem cria o Pedido sempre informa a data (D2).
- **Sem `CHECK`** contra a `DataAbertura` (D3).
- **Sem índice**: a Home pega cinco dos Pedidos não encerrados, e a tabela é pequena.

### 3.2 Migração de banco anterior — `db/alter-data-entrega.sql`

Mesmo formato dos anteriores (idempotente, `-b -f 65001`), em três passos:

1. adiciona a coluna **nula**, se não existir;
2. preenche os Pedidos que já existem com a **data de abertura em Brasília**,
   `CAST(DATEADD(HOUR, -3, DataAbertura) AS DATE)`;
3. torna a coluna `NOT NULL`, se ainda não for.

O preenchimento do passo 2 é um valor **inventado**: esses Pedidos não têm prazo de verdade. A data de
abertura é a mais honesta das invenções: todo Pedido antigo ainda aberto aparece como **atrasado**, o
que deixa visível que o prazo precisa ser revisto. Um "abertura + 30 dias" esconderia isso. Hoje o único
banco atingido é o de dev, que é descartável; não há VPS no ar.

### 3.3 O que não muda

`db/seed.sql` e `db/seed-demo.sql`: nenhum dos dois cria Pedido. Para a conferência no navegador, os
Pedidos atrasados se criam pela própria tela, porque datas no passado são aceitas (D3).

## 4. Regra de atraso

Vai para `specs/01-dominio-e-regras-de-negocio.md` como regra numerada:

> Um Pedido está **atrasado** quando a `DataEntrega` é anterior a **hoje** e o status não é `Concluido`
> nem `Cancelado`. "Hoje" é a data corrente no horário de Brasília (GMT-3 fixo, o mesmo da borda de
> fuso da aplicação). Um Pedido que vence hoje ainda **não** está atrasado.

Mais duas regras no `01`: a data de entrega é obrigatória e aceita qualquer dia (D2, D3), e o Pedido de
Retrabalho herda a data do Pedido de origem, editável no cadastro (D4, para a Fase 5).

## 5. Backend

### 5.1 Domínio e mapeamento

- `Pedido.DataEntrega` é `DateOnly`, e o `PedidoConfiguration` a mapeia para `date`, o que o EF 10 faz
  nativamente.
- `DateOnly` vai ao JSON como `"2026-10-22"` pelo serializador padrão, **sem** passar pelo
  `HorarioDeBrasiliaJsonConverter`, que só trata `DateTime`.

### 5.2 O "hoje" e a regra, num lugar só

- Um helper em `Application` calcula o "hoje de Brasília" a partir de um `TimeProvider`:
  `DateOnly.FromDateTime(UtcNow − 3h)`. É o mesmo -03:00 fixo do conversor, e escrito como tal, com o
  mesmo motivo (offset fixo em vez de busca de fuso, que lança exceção num host sem ICU).
- Uma função pura aplica a regra da seção 4. Ninguém mais a reescreve: a Fila e as Tarefas, no item
  seguinte, leem o mesmo booleano.
- `TimeProvider.System` é registrado no `Program.cs`. Nos testes entra um fake escrito à mão (subclasse
  de `TimeProvider` com `GetUtcNow` fixo), sem pacote novo.

### 5.3 DTOs

- `NovoPedidoDto` ganha `DateOnly? DataEntrega`. É anulável **só no DTO**, para a ausência virar o 400
  de campo obrigatório ("Numero, cliente e data de entrega sao obrigatorios.") em vez de um
  `0001-01-01` silencioso. Data malformada já cai no 400 do desserializador.
- `PedidoDto` ganha `DataEntrega` (`DateOnly`) e `Atrasado` (`bool`).
- `ResumoDePedidosDto.MaisAntigosAbertos` vira **`MaisUrgentes`**. A renomeação quebra o contrato de
  propósito, porque o nome antigo passaria a mentir. O front muda na mesma branch.

### 5.4 Casos de uso (`CadastroDePedidoUseCase`)

- **`Cadastrar` e `Editar`** gravam a data. O `Editar` continua sem guarda de status (D8), e o comentário
  dele que promete a guarda "só edita Pedido Aberto" para a Fase 3 sai, porque ela foi decidida contra.
- **`Listar`** ganha a ordem `entrega` (`OrdemDePedidos.Entrega`), e ela passa a ser a **padrão** quando
  `?ordem=` vem ausente ou em branco (D10). `recentes`, `numero` e `cliente` continuam aceitas, e a
  frase do 400 de ordem desconhecida passa a listar `entrega`. A ordem `entrega` é:
  1. os não encerrados primeiro, por `DataEntrega` crescente (o mais atrasado no topo), com
     `DataAbertura` crescente e `Id` crescente desempatando;
  2. depois os `Concluido` e `Cancelado`, por `DataEntrega` **decrescente** (o encerrado mais recente
     primeiro), com `Id` decrescente desempatando.

  O motivo da divisão: a lista traz todos os status, e ordenar só pela data poria os Concluídos antigos
  no topo, porque são os de prazo mais velho. Termina em ordem total, que a paginação exige (a regra da
  1F).
- **`Resumo`**: `ListarMaisAntigosAsync` vira **`ListarMaisUrgentesAsync`**: não encerrados, por
  `DataEntrega` crescente, com `DataAbertura` e `Id` desempatando, cinco itens. É o mesmo critério do
  primeiro bloco da ordem `entrega`.

## 6. Front

### 6.1 Tema

- `web/src/index.css` ganha `--color-atraso-texto: #6B21A8` e `--color-atraso-fundo: #F3E8FF`, com o
  par declarado em `contraste.test.ts` (7,39:1). Sem o par, a guarda reprova o token.
- `Pilula` ganha o tom `atraso`.

### 6.2 API (`web/src/api/cadastros.ts`)

- `PedidoDto` ganha `dataEntrega` (`"aaaa-mm-dd"`) e `atrasado`. `NovoPedido` ganha `dataEntrega`.
- Nasce o **`editarPedido`**, e sai o comentário que explicava a ausência dele.
- `OrdemDePedidos` ganha `'entrega'`, e o resumo passa a ler `maisUrgentes`.
- Nasce o **`formatarData`** (`"2026-10-22"` → `22/10/2026`), que **corta a string** em vez de criar
  um `Date`. Um `new Date("2026-10-22")` é lido como meia-noite UTC e, num navegador em Brasília, mostra
  21/10: a mesma armadilha da seção 3.1, do lado do front.

### 6.3 `LinhaDePedido` (a lista e a Home ao mesmo tempo)

A ordem na linha é status, Pausado, **Atrasado**, e depois "entrega em 22/10/2026 · aberto em
01/10/2026 08:14".

### 6.4 `PedidosPage`

- O painel "Novo pedido" ganha o campo **"Data de entrega"**, um `input type="date"` obrigatório dentro
  de um `Campo`. É o primeiro campo de data do projeto, e o `Campo` já atende.
- O `SeletorDeOrdem` ganha **"Prazo de entrega"** como primeira opção e padrão. A URL limpa `/pedidos`
  passa a ser a ordem por prazo, e "Mais recentes" passa a ser escrita como `?ordem=recentes`. Continua
  valendo que valor desconhecido lido da URL cai no padrão e não vai ao servidor.
- Salvar com sucesso chama o `voltarAoInicio()` com a ordem em `recentes`, escrita na URL (D11).

### 6.5 `PedidoDetalhePage`

- O cabeçalho mostra "Entrega em 22/10/2026" e a pílula Atrasado, junto do cliente.
- O `acao` da `Pagina` ganha o botão **"Editar pedido"**, sob `usePodeEscrever('pedidos')`. Ele abre um
  `PainelDeEscrita` com número, cliente e data já preenchidos, com `enviando` passado e `useDevolverFoco`
  ligado ao botão.
- O 409 de número duplicado aparece como erro dentro do painel. Não existe "Reativar o existente",
  porque Pedido não tem `Ativo`: o caminho é corrigir o número.
- No sucesso, o painel fecha e o Pedido é recarregado.

### 6.6 `HomePage`

O título da seção "Pedidos abertos há mais tempo" vira **"Prazos de entrega"** (D13), também como
`rotulo` acessível da lista, e a lista lê `maisUrgentes`. Os textos do estado vazio não mudam, porque o
conjunto (os não encerrados) não mudou.

## 7. Testes

### 7.1 Backend

**`Application.Tests`** (fakes, sem banco):

- `Cadastrar` sem data dá 400; com data no passado, aceita.
- `Editar` grava a data, inclusive num Pedido `Concluido`.
- A regra de atraso, com o relógio fake:

  | Prazo | Status | Atrasado? |
  |---|---|---|
  | ontem | aberto | sim |
  | hoje | aberto | não |
  | ontem | `Concluido` | não |
  | ontem | `Cancelado` | não |

- A virada do dia: às 02h59 UTC ainda é o dia anterior em Brasília; às 03h00 UTC já é o dia seguinte.
- `Listar` sem `?ordem=` usa `entrega`; `recentes` continua aceita; o 400 de ordem desconhecida lista
  `entrega`.
- `Resumo` devolve até cinco e exclui os encerrados.

**`Infrastructure.Tests`** (banco real):

- A `DateOnly` vai e volta da coluna `date` sem deslocar o dia.
- A ordem `entrega` traduzida em SQL: abertos por prazo crescente, encerrados por prazo decrescente,
  desempates como na seção 5.4. Escopada pela busca do prefixo do teste, numa página só
  (`Tamanho = int.MaxValue` direto no repositório), afirmando a ordem relativa das linhas do próprio
  teste.
- **"Os cinco mais urgentes" é consulta global** sobre uma tabela que outras classes e outro processo
  escrevem ao mesmo tempo, o caso que o `CLAUDE.md` descreve como flaky por construção. O teste de banco
  de `ListarMaisUrgentesAsync` chama com `quantos = int.MaxValue` e afirma só a ordem relativa das
  linhas que ele mesmo criou (prefixo de número por teste) e a ausência das encerradas dele — o mesmo
  conserto do `Busca_em_branco_nao_filtra_nada`. O corte em cinco é afirmado em `Application.Tests`,
  com fake. **Não** se usa "ano reservado" para garantir o topo: um teste que esquecesse a data
  gravaria `0001-01-01` (ver o último item desta lista) e passaria na frente.

**`Api.Tests`**:

- `POST` sem `dataEntrega` dá 400. Com ela dá 201, e o JSON traz `"dataEntrega":"2026-10-22"` **exato**,
  sem hora nem offset: é a guarda contra a data passar pelo conversor de `DateTime`.
- `PUT` edita a data. `GET /pedidos` sem ordem vem por prazo. O resumo traz `maisUrgentes`.
- Os `POST /api/pedidos` dos testes existentes passam a mandar a data, por um helper onde o corpo se
  repete. (Medido em 2026-10-08: 21 linhas de `tests/` têm o literal `"/api/pedidos"`, mas a linha não é
  a unidade — o literal aparece também em GET e num `InlineData`, e um `POST` ocupa mais de uma linha.
  O plano conta os `POST` por leitura.)
- Os `new Pedido` montados direto (25 ocorrências em 12 arquivos, em 2026-10-08: 16 em
  `Application.Tests`, com fake; 8 em `Infrastructure.Tests` e 1 em `Api.Tests`, com banco) e o `INSERT`
  cru do `PedidoMappingTests` passam a informar a data. **O `NOT NULL` não pega quem esquecer:** o
  `DateOnly` não informado vale `0001-01-01`, que é um `DATE` válido, e o Pedido vira silenciosamente o
  mais atrasado do banco. Quem pega é o `INSERT` cru (sem a coluna, o SQL Server recusa) e a review.

### 7.2 Front (Vitest + jsdom)

- `Pilula` com o tom `atraso`, e o par em `contraste.test.ts`.
- `formatarData`: o teste fixa o fuso em `America/Sao_Paulo`, para que trocar o corte da string por
  `new Date(...)` deixe o teste vermelho em qualquer máquina, em vez de passar por sorte do fuso.
- `LinhaDePedido`: a pílula aparece só com `atrasado`, e um teste morre se ela usar o tom `negativo` ou
  `atencao`.
- `PedidosPage`: o campo de data é obrigatório e vai no corpo do `POST`; a padrão é "Prazo de entrega"
  sem `?ordem` na URL; depois de salvar, a URL fica com `?ordem=recentes`.
- `PedidoDetalhePage`: o "Editar pedido" some para perfil sem escrita; o painel abre preenchido; o `PUT`
  leva os três campos; o 409 aparece dentro do painel; o `Cancelar` trava durante o envio; o foco volta
  ao botão; o cabeçalho mostra o prazo e a pílula.
- `HomePage`: o título "Prazos de entrega", a leitura de `maisUrgentes` e os três estados.
- `npm run build` no ciclo, porque o Vitest não faz checagem de tipo.

### 7.3 Mutações que a review de cada task deve tentar

Tirar a exclusão dos encerrados da regra de atraso; trocar `<` por `<=`; tirar o −3h; voltar a padrão
para `recentes`; ordenar os encerrados por prazo crescente; formatar a data com `new Date(...)`.

### 7.4 Conferência no navegador, pelo usuário

Antes da review de branch (regra do usuário de 2026-10-07). Roteiro mínimo: criar um Pedido com prazo
vencido e ver a pílula; conferir a ordem da Home e da lista, com e sem filtro de status; editar número e
data; tentar um número duplicado; abrir o seletor de data **no Android**, por ser o primeiro
`input type="date"` do projeto.

## 8. Lacuna registrada: auditoria de cadastro

Medido em 2026-10-08: **não existe auditoria de edição de cadastro.** Editar sobrescreve a linha e não
deixa rastro, nem em tabela nem em log. Vale para Pedido e, igualmente, para Setor, Material,
Componente, Agrupamento e nó da estrutura. O que existe é pontual:

- autoria de criação (`CriadoPorUsuarioId`) em `Pedido`, `Agrupamento` e `ArquivoDeComponente`;
- o livro de movimentações (`dbo.Movimentacao`), só de inclusão: quem moveu o quê, quando;
- a pausa de Pedido (`dbo.PedidoPausa`): quem pausou e quem retomou, quando;
- autenticação, só por `ILogger`, sem persistência (a "tabela de auditoria persistente" já consta do
  `CLAUDE.md` como deferida).

Este item torna a lacuna mais visível, porque o prazo passa a ser editável em qualquer status (D8).
Fica para uma **spec própria** (D9): auditoria de cadastro é transversal a todas as telas de edição, e
resolvê-la só no Pedido criaria um padrão sem decidir o geral.

## 9. Ajustes de documentação

Antes do plano:

- `specs/06-roadmap-mvp.md`: a exceção de ordem (este item antes da 3B, D1), com uma seção própria para
  este item.
- `CLAUDE.md`, seção "Ordem de implementação": as exceções passam de três para quatro, e a próxima da
  ordem passa a ser este item.

Na implementação:

- `specs/02-modelo-de-dados.sql` (seção 3.1), `specs/01-dominio-e-regras-de-negocio.md` (seção 4) e
  `specs/05-api-endpoints.md` (o corpo do `POST` e do `PUT`, os campos novos do `PedidoDto`, a ordem
  `entrega` como padrão, o `maisUrgentes` do resumo).
- `CLAUDE.md`:
  - o bloco de comando do `db/alter-data-entrega.sql`, como os da 3D e do import;
  - a regra "cor de estado nunca decora", com o roxo como quarto estado reservado, só para atraso;
  - o bullet do `SeletorDeOrdem`, que diz "a padrão é Mais recentes nas quatro telas" e "salvar com
    sucesso devolve a consulta ao padrão" (as emendas D10 e D11);
  - as contagens que este item muda, remedidas com o comando de cada uma (`<PainelDeEscrita`,
    `useDevolverFoco(`), dizendo a data nova.
- A spec da 1F ganha uma nota de emenda nas decisões 7 e 9, apontando para esta spec, sem reescrever o
  texto original.

## 10. Fora do escopo

- **A pílula Atrasado na Fila do Setor e nas Tarefas** — item seguinte (D6). O caminho medido: as duas
  telas compartilham o `NoResumoDto`, que já traz `PedidoId` e `PedidoNumero`; leva o `Atrasado` do
  Pedido como mais um campo, e a pergunta em aberto é se a pílula aparece em toda linha de Pedido
  atrasado ou só na que tem quantidade no Setor.
- Ordenar a Fila do Setor ou as Tarefas por prazo (a decisão 8 da 1F fica de pé).
- Auditoria de cadastro (seção 8).
- Filtro "só atrasados" na lista de Pedidos: a ordem por prazo já os põe no topo.
- KPI de pontualidade (`DataConclusao` contra `DataEntrega`) — Fase 6, se vier.
