# Fase 3 — Rastreamento de setor — design

- **Data:** 2026-09-24
- **Status:** design aprovado pelo usuário, seção a seção (seções 1 a 8 do brainstorm); **spec escrita
  aguardando a revisão dele**
- **Natureza:** spec de **fase** — domínio, schema, operações, API, telas e testes da Fase 3 de
  `specs/06-roadmap-mvp.md`, mais o texto das mudanças que ela impõe às specs de domínio. Parte da
  spec do Kit (`2026-09-15-kit-montagem-e-movimentacao-design.md`), que deixou para esta fase as
  perguntas da §9 ("Fase 3"), e do ponto em aberto do `01` que diz "a decidir no brainstorming da
  Fase 3".
- **Registro do brainstorm:** `.superpowers/sdd/fase3-brainstorm-estado.md` (ledger privado), com a
  resposta verbatim do usuário a cada decisão.

## 1. Contexto e escopo

O roadmap define a Fase 3 como: apontamento de entrada e saída de `EstruturaItem` em `Setor`,
**terminar e mover como ações separadas** (regra 22), o perfil **Movimentador**, a validação da
conservação de quantidade (regra 9), a fila do setor e a tela **Tarefas** com os **Itens prontos**
(regra 23). O critério de pronto: dá para acompanhar, item por item, em que Setor cada peça está e se
ela aguarda coleta; o Movimentador vê o que tem a levar e registra a entrega.

O brainstorm **ampliou** a fase em três frentes, por decisão do usuário (seção 2):

1. **Montagem de todo nó com filhos** sobe da 3B para cá (decisão 1): o registro "montei N", o destino
   "montado" e `EstruturaItem.QuantidadePorPai`. A 3B encolhe para `Setor.UtilizaKit`, a trava de
   montagem, o conjunto completo e a tarefa "Kit pronto".
2. **Roteiro editável por nó** (decisão 8), que o `05` listava como "planejado, sem fase".
3. **O que falta para montar**, na fila do Setor (decisão 3).

E fecha duas dívidas: a sobra de refugo, que a spec do Kit deixou sem ator nem momento (decisão 5,
contraexemplos (a) e (b) da review de branch do Kit), e o ponto em aberto do `01` sobre como sai de
"em produção" o filho de um nó sem trava (decisão 1).

**Fora desta fase** (e para onde vai):

| O quê | Fase |
|---|---|
| `Setor.UtilizaKit`, trava de montagem, conjunto completo, tarefa "Kit pronto" | 3B |
| Onde cada material fica e requisição de material pelo operador (decisão 3) | 4 |
| Posições `Expedido` e `Perdido`, tipo `Pronto` (nó pronto do Retrabalho), registro do `Descarte`, status `AguardandoExpedicao` e fechamento | 5 |
| KPI de tempo por Setor e por Pedido lendo o livro | 6 |
| Notificação push | 3C |
| CRUD de Usuário (contas de Movimentador nascem por SQL) | dívida sem fase |

**Kit antes da 3B.** Até a 3B, um Agrupamento Kit se comporta como Avulso: a montagem existe para ele
(decisão 1), mas sem trava nem conjunto completo. Isso é consequência da ordem das fases, não regra.

## 2. Decisões do brainstorm, com o porquê

Cada decisão traz o que foi escolhido, o porquê e o que foi descartado. As palavras do usuário estão
no registro do brainstorm.

### 2.1 Como sai de "em produção" o filho de um nó sem trava — montar é registro de todo nó com filhos

Um Item de Agrupamento Avulso (parafusado no pai, por exemplo) não tinha registro que o tirasse de
"em produção": a Peça-pai concluía com o filho vivo. **Montar passa a ser registro de todo nó com
filhos**, de Kit ou Avulso; só a **validação da trava** (regra 24) fica restrita a Kit + `UtilizaKit`.
Como a baixa de cada filho é `N × QuantidadePorPai`, a razão sobe junto para esta fase.

Descartadas: **entrega ao pai vira "montado"** (a quantidade que o Movimentador informa, sem razão —
dois caminhos até "montado", e total montado do pai inexistente fora do Kit) e **baixa implícita
quando o pai sai do último Setor** (o sistema teria de adivinhar quanto de cada filho foi usado; a
spec do Kit, §3.5, já recusou baixa implícita para o `Descarte` pelo mesmo motivo).

### 2.2 Quando a quantidade começa a existir na produção — "a iniciar", e a primeira entrada é do operador

Antes da primeira entrada num Setor, a quantidade de um nó não cabia em nenhum termo da regra 9. Fato
da fábrica, trazido pelo usuário: parte do material já está no Setor (chapas no Corte), parte é levada
pelo almoxarifado, e **a fabricação começa quando o operador pega o material para trabalhar**. Então o
nó nasce **"a iniciar"** — parte do "em produção" — e a **primeira entrada é registrada pelo operador**
do primeiro Setor do Roteiro, sem o Movimentador: não há o que levar. A entrega de material não é o
gatilho. O Pedido vira `EmProducao` na primeira entrada de qualquer nó dele.

Descartadas: **liberação explícita pelo PCP**, com a primeira entrada pelo Movimentador (tarefa de
levar algo que fisicamente não existe, e uma ação a mais), **o nó só entra na conta na primeira
entrada** (reabre o buraco que a spec do Kit mandou fechar) e **um perfil "Separador"** que leva a
matéria-prima (ele é o Almoxarifado da `00`; o custo real seria acoplar a primeira entrada à separação
de material da Fase 4 e exigir um gatilho de liberação).

### 2.3 O que falta para começar — filhos aqui, materiais na Fase 4

O usuário quis que o operador saiba o que falta. As duas leituras foram separadas: **filhos que faltam
para montar** entram nesta fase, calculados pelo estado (seção 7); **materiais que faltam** vão para a
Fase 4, que ganha "onde cada material fica" (chapa é do Corte, não de todo Setor) e a **requisição do
operador** atendida pelo Almoxarifado — uma tabela, porque requisição é evento, não estado duplicado.

### 2.4 Para onde vai o filho que terminou o próprio Roteiro — o Movimentador escolhe

O sistema não sabe em que Setor o pai é montado (a `UtilizaKit` responde isso só para o Kit, e só na
3B). **O Movimentador escolhe o Setor de destino entre os do Roteiro do pai**, com sugestão (seção 7);
a montagem é registrada pelo operador daquele Setor. Sem cadastro novo. Destino errado é visível (os
filhos aparecem na fila de um Setor que não monta aquilo) e se corrige com outra entrega. Na 3B, a
`UtilizaKit` só estreita a sugestão para o Kit.

Descartadas: **marca de "passo de montagem" no Roteiro do pai** (coluna em `EstruturaRoteiro` e em
`ComponenteRoteiroPadrao`, e um conceito paralelo à `UtilizaKit` já decidida) e **montar baixa direto
de "aguardando coleta"** (contradiz 2.3: os filhos estão presentes no Setor).

### 2.5 A sobra de refugo — identificada aqui, descartada na Fase 5

Com a montagem de todo nó, a sobra aparece já nesta fase (C de 10, D de 45 com razão 4: montadas as
10 C, 5 D ficam "em produção" para sempre). **A Fase 3 identifica a sobra pelo estado** e a mostra à
parte, na fila do Setor onde está, fora de "Item pronto" — fecha o contraexemplo (b), a entrada morta
na lista. O **registro** do `Descarte` fica na Fase 5, com a perda, pelo **mesmo ator da perda**
(Qualidade ou PCP, como o fluxo "6. Perda de peças" do `04`), a qualquer momento depois de virar sobra.
A **regra 13 não muda**: o Pedido conclui com sobra viva, que continua listada até ser descartada — o
contraexemplo (a) fica visível, não bloqueia.

Descartadas: **descarte já nesta fase, pelo operador** (puxa um pedaço da Fase 5 e cria um segundo ator
para o mesmo registro) e **conclusão do Pedido exige sobra zerada** (muda a regra 13, que a spec do Kit
declarou que não muda, e um esquecimento deixaria o Pedido aberto sem nada em produção de verdade).

### 2.6 A Peça que terminou o Roteiro — vai ao local de expedição pelo Movimentador

Fato da fábrica: existe um local específico de expedição, e tudo é movimentado até lá para expedir as
cargas. A Peça pronta **aguarda coleta com destino Expedição** e é **tarefa do Movimentador**, que a
leva ao **local de expedição** (não é Setor) e registra a entrega. "No local de expedição" conta como
em produção na regra 9; a Fase 5 baixa a expedição de lá.

Descartadas: **fica "pronta" no último Setor, sem tarefa** (não é o que a fábrica faz) e **Expedição
como Setor comum** (obrigaria o passo no fim de todo Roteiro de Peça e poluiria os KPIs por Setor da
Fase 6).

### 2.7 Como o banco guarda "quanto está onde" — um livro de movimentações

Tabela nova, só de inclusão: cada linha move N unidades de um nó de uma **posição** para outra. O
saldo de cada posição é a soma do que entrou menos o que saiu. A regra 9 sai **por construção** — cada
movimento tira de uma posição e põe em outra —, e fila e tarefas são consultas sobre o saldo, no mesmo
padrão "calculado a partir do estado" da regra 23. Nada de identidade de sub-lote.

Custo aceito: o KPI de tempo por Setor (Fase 6) deixa de ser o `AVG(DataSaida − DataEntrada)` de hoje
e passa a parear entradas e saídas por ordem de chegada (FIFO) — a complexidade vai para uma consulta
de leitura, numa fase só. A `EstruturaSetorHistorico`, que nenhum código usa, sai.

Descartadas: **porções em `EstruturaSetorHistorico`** (cada terminar, levar ou montar parcial divide
linhas, na escrita, onde o erro corrompe dado; e as porções viram a identidade de sub-lote que o
domínio diz não existir) e **tabela de saldo mais histórico** (duas fontes da verdade — o mesmo motivo
pelo qual a tabela de aviso foi recusada para as tarefas).

### 2.8 Nó sem Roteiro — Roteiro editável por nó

Medido no código: o Roteiro de um nó só nasce **copiado da receita** (`EstruturaRepository`); um Item
ad-hoc nasce sem nenhum, e não há endpoint para dar ou editar o Roteiro de um nó. Sem primeiro passo,
o nó ficaria "a iniciar" para sempre e travaria a montagem do pai; a regra 7 ("o roteiro pode ser
customizado por Pedido/Agrupamento") também não tinha como ser cumprida. **A Fase 3 inclui editar o
Roteiro por nó** (PCP). A primeira entrada **exige** Roteiro; nó sem Roteiro aparece ao PCP como
pendência. Depois de o nó andar, só se editam os passos **ainda não alcançados** — passo alcançado é
histórico, e o livro aponta para ele.

Descartadas: **Roteiro vazio significa "vem pronto de fora"**, sozinha ou junto da edição. Fato do
usuário: tudo o que é Item é fabricado dentro da fábrica.

## 3. Modelo de dados

Tudo aqui entra em `specs/02-modelo-de-dados.sql` **antes** do mapeamento EF (Database First, regra do
`CLAUDE.md`), com os blocos `ALTER` idempotentes no `CLAUDE.md` para quem já tem banco.

### 3.1 Posições

A quantidade de cada nó está, a qualquer momento, repartida entre estas posições:

| Posição | Setor | Passo (`Ordem`) | O que é | Conta como |
|---|---|---|---|---|
| `AIniciar` | — | — | nunca entrou num Setor. Não aparece como destino, exceto no estorno de um `Inicio`; o saldo é a quantidade do nó menos o que saiu daqui | em produção |
| `NoSetor` | sim | sim | em trabalho no passo `Ordem` do **próprio** Roteiro | em produção |
| `AguardandoColeta` | sim | sim | terminou o passo `Ordem` e espera no Setor onde terminou; o destino é **calculado** (seção 7) | em produção |
| `AguardandoMontagem` | sim | — | filho entregue no Setor onde o pai será montado | em produção |
| `NaExpedicao` | — | — | Peça no local de expedição | em produção |
| `Montado` | — | — | terminal; só Item | montado |

A Fase 5 acrescenta `Expedido` e `Perdido`. O "passo" é a `Ordem` do `EstruturaRoteiro`, não o Setor:
o mesmo Setor pode aparecer duas vezes no Roteiro (regra 21).

### 3.2 `dbo.Montagem` (nova)

```sql
-- Registro de "montei N" de um nó com filhos (regra 24). O total montado do nó é a soma de
-- Quantidade das montagens não estornadas. A baixa de CADA filho fica em dbo.Movimentacao
-- (Tipo = 'Montagem', MontagemId = esta linha), com N × QuantidadePorPai gravado: editar a
-- razão depois não reescreve o passado.
CREATE TABLE dbo.Montagem (
    Id                     INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId        INT                 NOT NULL, -- o pai montado (nó com filhos)
    SetorId                INT                 NOT NULL, -- onde foi montado
    Quantidade             DECIMAL(18,4)       NOT NULL, -- N unidades do pai
    DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Montagem_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId              INT                 NOT NULL,
    EstornadaEm            DATETIME2           NULL,     -- NULL = vale; preenchida = estornada (seção 4.5)
    EstornadaPorUsuarioId  INT                 NULL,
    CONSTRAINT PK_Montagem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Montagem_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Montagem_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Montagem_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT FK_Montagem_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Montagem_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Montagem_EstornoCompleto
        CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
            OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL)),
    CONSTRAINT CK_Montagem_EstornoAposMontagem CHECK (EstornadaEm IS NULL OR EstornadaEm >= DataHora)
);
```

### 3.3 `dbo.Movimentacao` (nova) — o livro

```sql
-- Livro de movimentações: cada linha move Quantidade de um nó de uma posição para outra.
-- SÓ INSERÇÃO: não se edita nem se apaga; correção é um Estorno (movimento inverso que aponta o
-- original). Saldo de uma posição = Σ Quantidade onde ela é destino − Σ onde ela é origem;
-- AIniciar = EstruturaItem.Quantidade − Σ onde ela é origem + Σ onde ela é destino (estorno).
-- Conservação (regra 9) por construção: todo movimento tira de uma posição e põe em outra.
CREATE TABLE dbo.Movimentacao (
    Id               INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId  INT                 NOT NULL,
    Tipo             NVARCHAR(20)        NOT NULL, -- Inicio | Termino | Entrega | Montagem | Estorno
    Quantidade       DECIMAL(18,4)       NOT NULL,
    OrigemPosicao    NVARCHAR(20)        NOT NULL,
    OrigemSetorId    INT                 NULL,
    OrigemOrdem      INT                 NULL,     -- passo do Roteiro do próprio nó
    DestinoPosicao   NVARCHAR(20)        NOT NULL,
    DestinoSetorId   INT                 NULL,
    DestinoOrdem     INT                 NULL,
    MontagemId       INT                 NULL,     -- baixa de filho (e o estorno dela)
    EstornoDeId      INT                 NULL,     -- só no Estorno: o movimento que ele desfaz
    DataHora         DATETIME2           NOT NULL CONSTRAINT DF_Movimentacao_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId        INT                 NOT NULL, -- autor
    CONSTRAINT PK_Movimentacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Movimentacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Movimentacao_OrigemSetor FOREIGN KEY (OrigemSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_DestinoSetor FOREIGN KEY (DestinoSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_Montagem FOREIGN KEY (MontagemId) REFERENCES dbo.Montagem (Id),
    CONSTRAINT FK_Movimentacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.Movimentacao (Id),
    CONSTRAINT FK_Movimentacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Movimentacao_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Movimentacao_Tipo
        CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno')),
    CONSTRAINT CK_Movimentacao_OrigemPosicao
        CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    CONSTRAINT CK_Movimentacao_DestinoPosicao
        CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    -- Setor e passo combinam com a posição (tabela da seção 3.1 da spec da Fase 3)
    CONSTRAINT CK_Movimentacao_OrigemCoerente
        CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
            OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
            OR (OrigemPosicao = 'AguardandoMontagem' AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL)),
    CONSTRAINT CK_Movimentacao_DestinoCoerente
        CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
            OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
            OR (DestinoPosicao = 'AguardandoMontagem' AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL)),
    -- Cada tipo só faz as transições dele; o Estorno é o inverso de um dos outros
    CONSTRAINT CK_Movimentacao_Transicao
        CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
            OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                                  AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                                  AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                                  AND DestinoPosicao = 'AguardandoMontagem')
            OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
            OR (Tipo = 'Estorno')),
    CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo = 'Estorno')
            OR (Tipo NOT IN ('Montagem', 'Estorno') AND MontagemId IS NULL)),
    CONSTRAINT CK_Movimentacao_EstornoApontaOriginal
        CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
            OR (Tipo <> 'Estorno' AND EstornoDeId IS NULL))
);

CREATE INDEX IX_Movimentacao_EstruturaItem ON dbo.Movimentacao (EstruturaItemId);
CREATE INDEX IX_Movimentacao_DestinoSetor ON dbo.Movimentacao (DestinoSetorId) WHERE DestinoSetorId IS NOT NULL;
CREATE INDEX IX_Movimentacao_OrigemSetor ON dbo.Movimentacao (OrigemSetorId) WHERE OrigemSetorId IS NOT NULL;
-- Um movimento se estorna uma vez só: JaEstornado garantido pelo banco, não só pela aplicação.
CREATE UNIQUE INDEX UX_Movimentacao_EstornoDe ON dbo.Movimentacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
```

**Não se estorna um estorno.** A aplicação recusa (`EstornoImpossivel`); o registro errado de um
estorno se corrige registrando de novo a operação original.

### 3.4 `EstruturaItem.QuantidadePorPai` (coluna nova)

```sql
    QuantidadePorPai            DECIMAL(18,4)       NULL,       -- regra 26: quantos entram em UMA unidade do pai
    ...
    CONSTRAINT CK_EstruturaItem_QuantidadePorPai
        CHECK ((NivelHierarquico = 'Peca' AND QuantidadePorPai IS NULL)
            OR (NivelHierarquico = 'Item' AND QuantidadePorPai IS NOT NULL AND QuantidadePorPai > 0))
```

- **Cópia da receita:** preenche com `ComponenteFilhoPadrao.QuantidadePadrao` (a mesma razão que hoje já
  multiplica a quantidade absoluta do filho).
- **Filho acrescentado à mão** (`POST /estrutura/{id}/filhos`), com Componente ou ad-hoc: quem cadastra
  informa. A tela vem preenchida quando a receita do Componente do pai lista aquele filho.
- **Banco que já existe:** o bloco `ALTER` acrescenta a coluna nula, preenche os Itens com
  `Quantidade ÷ Quantidade do pai` e só então cria o `CHECK`. É aproximação aceitável porque o banco de
  dev é descartável (autorização do dono do projeto, 2026-08-17); não há banco de produção.

### 3.5 Demais mudanças de schema

- **`dbo.Perfil`**: o comentário de `Nome` passa a listar os sete perfis (`Operador | Almoxarifado |
  Movimentador | PCP | Qualidade | Gestao | Administrador` — hoje não lista nem o Almoxarifado), e
  `db/seed.sql` ganha `('Movimentador')` no `MERGE`.
- **`dbo.EstruturaSetorHistorico` sai**, com os índices `IX_EstruturaSetorHistorico_Item` e
  `IX_EstruturaSetorHistorico_Setor`. O comentário do `CadastroDeSetorUseCase` ("Setor não se exclui —
  linhas de `EstruturaSetorHistorico` apontam para ele") passa a citar `Movimentacao` e `Montagem`.
- **Consultas de KPI de exemplo** no fim do `02`: as duas que leem `EstruturaSetorHistorico` viram nota
  "reescrever na Fase 6 sobre `dbo.Movimentacao`, pareando entradas e saídas por ordem de chegada".
- **Sem mudança:** `EstruturaRoteiro` (editar por nó não exige coluna), `Pedido` (`Status =
  'EmProducao'` já existe), `Agrupamento`, `Perda`.

## 4. Operações e regras

Toda escrita roda numa transação e valida o saldo da origem antes de gravar (concorrência na seção 8).
Os limites são os mesmos que a leitura mostra: a validação usa a calculadora da seção 7.

### 4.1 Iniciar (Operador, no Setor S): nó X, quantidade N

- **Exige:** X tem Roteiro (`SemRoteiro`); o **primeiro** passo do Roteiro de X é em S
  (`NaoEhOPrimeiroPasso`); N ≤ saldo `AIniciar` de X (`SaldoInsuficiente`); o Pedido não está
  `Concluido` nem `Cancelado` (`PedidoFechado`).
- **Grava:** `Inicio`, `AIniciar` → `NoSetor(S, primeira Ordem)`.
- **Efeito:** se o Pedido estava `Aberto`, passa a `EmProducao`. O status **não volta** se o `Inicio`
  for estornado: a produção começou.

### 4.2 Terminar (Operador, no Setor S): nó X, passo k, N

- **Exige:** N ≤ saldo `NoSetor(S, k)` de X.
- **Grava:** `Termino`, `NoSetor(S, k)` → `AguardandoColeta(S, k)`.

### 4.3 Entregar (Movimentador): lista de itens, tudo ou nada

Cada item diz o nó, a origem (`AguardandoColeta(S, k)` ou `AguardandoMontagem(S)`) e N; a lista inteira
grava numa só transação, ou nada grava. A lista já serve à 3B, cujo conjunto completo exige que todos os
filhos entrem juntos. O destino de cada item:

| Origem | Situação | Destino | `destinoSetorId` |
|---|---|---|---|
| `AguardandoColeta(S, k)` | existe passo depois de k no Roteiro do nó | `NoSetor` do **próximo** passo — sem escolha; o Movimentador não pula passo | proibido |
| `AguardandoColeta(S, k)` | k é o último passo, nó é **Item** | `AguardandoMontagem(S')`, S' um Setor do Roteiro do **pai** (decisão 2.4) | obrigatório |
| `AguardandoColeta(S, k)` | k é o último passo, nó é **Peça** | `NaExpedicao` | proibido |
| `AguardandoMontagem(S)` | redirecionamento (destino errado, decisão 2.4) | `AguardandoMontagem(S')`, S' ≠ S, Setor do Roteiro do pai | obrigatório |

- **Exige:** N ≤ saldo da origem; pai com Roteiro quando o destino é montagem (`PaiSemRoteiro`); S' no
  Roteiro do pai (`DestinoForaDoRoteiroDoPai`); `destinoSetorId` só quando a tabela o pede
  (`DestinoIndevido`).
- **Grava:** um `Entrega` por item.
- **Não há teto de sobra na entrega.** O Movimentador pode levar a sobra para a montagem; ela aparece
  como excesso lá (seção 7). Recusar seria regra da 3B (conjunto completo, só Kit).

### 4.4 Montar (Operador, no Setor S): pai P, N

- **Exige:** P tem filhos (`SemFilhos`); N ≤ quantidade de P − total montado de P
  (`MontagemAcimaDoQueFalta`); para **cada** filho direto c, `N × QuantidadePorPai(c)` ≤ saldo
  `AguardandoMontagem(S)` de c (`FilhosInsuficientes`, com a mensagem nomeando o filho que limita).
- **Grava:** uma `Montagem` (P, S, N) e, para cada filho direto c, um `Montagem`
  `AguardandoMontagem(S)` → `Montado` de `N × QuantidadePorPai(c)`.
- **Não exige** que P esteja em S: sem identidade de sub-lote, não dá para saber quais unidades do pai
  no Setor já foram montadas, e a regra 24 também não pede isso. A 3B endurece para o Kit.
- **Montar não move o pai.** O total montado limita a própria montagem nesta fase; a saída do pai do
  Setor limitada ao total montado é da trava (3B).

### 4.5 Estornar (o autor, ou PCP/Administrador)

- **Movimento:** grava o inverso (origem e destino trocados, mesma quantidade), `Tipo = 'Estorno'`,
  `EstornoDeId` = o original. Só se o saldo do destino original ainda comportar a quantidade — isto é, se
  ninguém a moveu depois (`EstornoImpossivel`). Um movimento se estorna uma vez só (`JaEstornado`,
  garantido pelo índice único). Estorno não se estorna.
- **Montagem:** estorna-se a `Montagem` inteira, não uma baixa de filho avulsa: grava um `Estorno` para
  cada baixa (`MontagemId` preenchido) e marca `EstornadaEm`/`EstornadaPorUsuarioId`. Estornar uma baixa
  de filho diretamente é recusado (`EstornoImpossivel`, mensagem apontando a montagem).
- **Quem:** o autor do original, ou PCP ou Administrador (`Proibido`, 403, para os demais).
- **Por que existe:** registro por celular no chão de fábrica vai ter erro de toque, e o livro é só de
  inclusão.

### 4.6 Editar o Roteiro do nó (PCP)

- **Troca a lista de passos** (Setores em ordem). Os passos **já alcançados** — toda `Ordem` que aparece
  em `OrigemOrdem` ou `DestinoOrdem` do livro daquele nó — não podem ser alterados nem removidos, e
  nenhum passo novo pode ser inserido antes deles (`PassoJaAlcancado`). Os demais se editam livremente.
- **Setor inativo** não entra num Roteiro (`RoteiroInvalido`). O que já está num Setor que foi
  inativado continua andando: inativar um Setor não prende peça dentro dele.

### 4.7 O que muda nas edições da Fase 2

| Edição | Regra nova |
|---|---|
| Excluir nó | recusado se ele ou alguém da subárvore tem movimento ou montagem (`NoComMovimentacao`) |
| Reduzir `Quantidade` | recusado abaixo do que já saiu de `AIniciar` e, se for pai, abaixo do total montado (`QuantidadeAbaixoDoMovimentado`) |
| Editar `QuantidadePorPai` | livre: a baixa já gravada não muda; afeta montagens futuras, sobra e "falta" |
| Acrescentar filho | livre; `QuantidadePorPai` obrigatória no corpo |

### 4.8 Perfis (sempre com `Administrador` junto)

| Ação | Perfis |
|---|---|
| Iniciar, terminar, montar | Operador |
| Entregar (inclusive redirecionar) | Movimentador |
| Editar Roteiro do nó | PCP |
| Estornar | autor; PCP |
| Todas as leituras | todo autenticado |

## 5. API

Rotas no prefixo que a Fase 2 implementou (`estrutura/{id}`), não no rascunho `estrutura-itens/{id}` do
`05`, e sempre sob `/api` (guarda de `Program.cs`). Erros no formato atual `{ erro, mensagem }`;
códigos na seção 8.

### 5.1 Escrita

| Rota | Corpo | Perfis (+ Administrador) | Resposta |
|---|---|---|---|
| `POST /estrutura/{id}/inicios` | `{ setorId, quantidade }` | Operador | 201, o movimento |
| `POST /estrutura/{id}/terminos` | `{ setorId, ordem, quantidade }` | Operador | 201, o movimento |
| `POST /estrutura/{id}/montagens` | `{ setorId, quantidade }` | Operador | 201, a montagem com as baixas |
| `POST /entregas` | `{ itens: [{ estruturaItemId, origem: { posicao, setorId, ordem }, destinoSetorId?, quantidade }] }` | Movimentador | 201, os movimentos |
| `POST /movimentacoes/{id}/estorno` | — | Operador, Movimentador, PCP | 201, o estorno |
| `POST /montagens/{id}/estorno` | — | Operador, PCP | 201, os estornos |
| `PUT /estrutura/{id}/roteiro` | `{ passos: [setorId, …] }`, em ordem | PCP | 200, o Roteiro |

No estorno, o `[Authorize]` deixa os perfis passarem e o **caso de uso** recusa com 403 quem não é o
autor nem PCP. Isso exige um `TipoDeErro` novo, `Proibido`, e o mapeamento dele nos controllers —
hoje o `Result` só conhece 400, 404 e 409.

### 5.2 Leitura (todo autenticado)

| Rota | O que devolve |
|---|---|
| `GET /setores/{id}/fila` | As seções da fila (seção 6.1), cada linha com nó, caminho na árvore (Pedido › Agrupamento › pai), passo e quantidades |
| `GET /tarefas` | Os "Item pronto" (seção 7), com destino calculado e, quando é montagem, a sugestão e os Setores possíveis, agrupados pelo Setor de origem |
| `GET /tarefas/contagem` | Só o número de tarefas, para o contador do menu |
| `GET /agrupamentos/{id}/posicoes` | Saldo por posição de todos os nós do Agrupamento, numa chamada, e o total montado dos nós com filhos |
| `GET /estrutura/{id}/movimentacoes` | O livro do nó, com autor, tipo e se já foi estornado |
| `GET /estrutura/{id}/roteiro` | O Roteiro do nó, com a marca de quais passos já foram alcançados |

### 5.3 Mudanças em rotas que já existem

- `GET /agrupamentos/{id}/estrutura`: cada nó ganha `quantidadePorPai` e `semRoteiro`.
- `POST /estrutura/{id}/filhos` e `PUT /estrutura/{id}`: ganham `quantidadePorPai` (obrigatória no
  Item); o `PUT` e o `DELETE` passam a recusar pelas regras da seção 4.7.

Sem paginação na fila e nas tarefas: elas mostram só o que está em produção agora, e o volume de uma
fábrica cabe numa página. Se um dia não couber, o padrão `useBuscaPaginada` já existe.

## 6. Telas

Padrão da Fase 1D: `<Pagina titulo>` dentro do `AppShell`, primitivas de `web/src/components/`, os três
estados (carregando, vazio, erro) com teste, cores só pelos tokens. Mobile-first: o uso principal é o
celular no chão de fábrica.

### 6.1 Fila do Setor — `/fila` e `/fila/:setorId` (item novo no menu: **Fila**)

`/fila` lista os Setores; o aparelho lembra o último escolhido (`localStorage` com `try/catch` —
conveniência por aparelho, não vínculo do usuário ao Setor) e abre direto nele. `/fila/:setorId` tem as
seções:

| Seção | Conteúdo | Ação |
|---|---|---|
| A iniciar aqui | nós com Roteiro cujo primeiro passo é este Setor, com saldo `AIniciar`, de Pedidos não concluídos nem cancelados | **Iniciar** (Operador) |
| Em trabalho | saldo `NoSetor` aqui, por passo | **Terminar** (Operador) |
| Aguardando coleta | saldo `AguardandoColeta` aqui, com o destino | nenhuma — informa |
| Aguardando montagem | agrupado por pai: filhos presentes, "dá para montar N; falta X de Y para a próxima" | **Montar** (Operador); **Levar para outro Setor** (Movimentador) |
| Sobra | o que passa do que o pai precisa (seção 7) | nenhuma — o descarte é da Fase 5 |

Toda ação abre um campo de quantidade já preenchido com todo o disponível, que o operador pode diminuir
(lote divisível). As ações aparecem conforme o perfil (`usePodeEscrever`); os demais leem. Os estados
vazios distinguem "nada neste Setor agora" de erro.

### 6.2 Tarefas — `/tarefas` (item novo no menu, com **contador**)

Os "Item pronto", agrupados pelo Setor de origem, cada um com o destino calculado. Quando o destino é
montagem, um `<select>` com os Setores do Roteiro do pai, já na sugestão — `<select>` simples, não
`SeletorComBusca`, porque são poucos Setores e não um catálogo paginado. O Movimentador marca vários
itens, ajusta quantidades e toca **Entregar**: uma requisição, tudo ou nada.

O contador do menu consulta `/tarefas/contagem` a cada 30 s. A lista e a fila se atualizam a cada 30 s
enquanto a tela está aberta, e na hora depois de cada ação.

### 6.3 Árvore do Agrupamento (tela existente)

- **Onde está cada peça** (o critério de pronto): cada nó mostra o saldo por posição em pílulas — "4 a
  iniciar · 6 no Corte · 2 aguardando coleta" — e, nos nós com filhos, o total montado.
- **Pendência:** pílula **sem Roteiro** para o PCP.
- **Detalhe do nó:** **histórico** (o livro), com **Estornar** para quem pode; **editor de Roteiro**
  (PCP), com os passos alcançados travados.
- **Formulários de filho:** o campo **Quantidade por pai**, obrigatório no Item.

### 6.4 Primitivas, cores e permissões

- **Primitiva nova**, com teste próprio: `ResumoDePosicoes` (as pílulas de saldo por posição), usada na
  árvore e na fila. Antes de criar outra — uma linha de fila com ação, por exemplo —, o plano confere se
  `ListaDeCadastro` ou outra existente serve.
- **Cores:** "aguardando coleta", "sobra" e "sem Roteiro" em tom **neutro ou de atenção**, nunca
  `negativo` — o vermelho é de reprovado, perda e erro, e sobra não é perda até alguém descartar.
- **`web/src/auth/permissoes.ts`** ganha três chaves, espelhando os `[Authorize]`: `apontamento`
  (Operador, Administrador), `entrega` (Movimentador, Administrador) e `roteiro` (PCP, Administrador).
  O estorno compara o autor na tela, mas quem decide é o 403 do backend.

## 7. Cálculo de fila e tarefas

### 7.1 Saldo

Uma consulta sobre o livro soma `+Quantidade` no destino e `−Quantidade` na origem, agrupando por nó e
posição (posição, Setor, `Ordem`). `AIniciar` = quantidade do nó menos o que saiu de lá, mais o que
voltou por estorno. O estorno é só um movimento inverso: a soma o absorve, sem filtro especial. Total
montado = Σ `Montagem.Quantidade` onde `EstornadaEm IS NULL`.

### 7.2 Uma calculadora só, para a leitura e para a escrita

A regra vive em funções puras na camada `Application`: recebem os nós (pai, razão, quantidade,
Roteiro), os saldos e os totais montados, e devolvem fila, tarefas, "dá para montar" e sobra. Os casos
de uso de escrita validam **com as mesmas funções** — o que fecha, por construção, a tela oferecer
"montar 3" e a API recusar 3. Testáveis com fakes, sem banco.

### 7.3 Destino de `AguardandoColeta(S, k)`

1. Existe passo depois de k no Roteiro do nó → `NoSetor` do próximo passo. Calculado na leitura: se o PCP
   editar os passos não alcançados, o destino acompanha.
2. k é o último, nó é Peça → local de expedição.
3. k é o último, nó é Item → montagem do pai. Sugestão de Setor, nesta ordem:
   1. o Setor onde o pai tem saldo `NoSetor` (se mais de um, o de maior saldo);
   2. senão, o Setor do primeiro passo do pai ainda não alcançado;
   3. pai **sem Roteiro** → sem sugestão; a entrega é recusada (`PaiSemRoteiro`), e a pendência aparece
      ao PCP (decisão 2.8).

### 7.4 Tarefas "Item pronto"

Para um filho c de pai P, seja **o que P ainda precisa receber de c**:

`precisa(c) = max(0, (Quantidade(P) − totalMontado(P)) × QuantidadePorPai(c) − Σ AguardandoMontagem(c))`

- Quantidade `AguardandoColeta` que ainda vai a outro passo, ou Peça indo à expedição: tarefa inteira.
- Item no último passo: tarefa = `min(AguardandoColeta no último passo, precisa(c))`.

É o teto das regras 23 e 25 aplicado a todo nó. A 3B só acrescenta o conjunto completo e a tarefa
"Kit pronto".

### 7.5 Sobra (decisão 2.5)

- **No último passo:** `AguardandoColeta` no último passo − a tarefa. O último passo é um Setor só, então
  essa sobra tem lugar certo: aparece na seção Sobra da fila daquele Setor.
- **Em `AguardandoMontagem`:** o excesso `Σ AguardandoMontagem(c) − (Quantidade(P) − totalMontado(P)) ×
  QuantidadePorPai(c)`, quando positivo, aparece **no nível do nó**, em cada Setor onde c espera, como
  "tem X a mais do que o pai precisa". Sem identidade de sub-lote, não dá para dizer em qual Setor está a
  unidade a mais quando c espera em dois; o texto diz isso em vez de escolher arbitrariamente.
- O que ainda está antes do fim do Roteiro **não é sobra**: uma perda até lá pode torná-lo necessário.

### 7.6 "Dá para montar N; falta X de Y" (decisão 2.3)

No Setor S, para o pai P: `N = min(Quantidade(P) − totalMontado(P), min_c ⌊AguardandoMontagem(S, c) ÷
QuantidadePorPai(c)⌋)`. O "falta" é, por filho, o que ele precisa para completar a unidade N+1:
`max(0, (N + 1) × QuantidadePorPai(c) − AguardandoMontagem(S, c))`, mostrado só enquanto N+1 ≤ o que
falta montar.

### 7.7 Desempenho

Cada tela faz uma consulta de saldo e uma dos nós envolvidos, sem cache. Fila e tarefas mostram dezenas a
centenas de linhas na escala de uma fábrica. `/tarefas/contagem` usa a mesma calculadora e devolve só o
número.

## 8. Erros e concorrência

### 8.1 Concorrência

- Toda escrita desta fase roda em transação `Serializable` e **começa travando** as linhas de
  `EstruturaItem` envolvidas (`UPDLOCK`), **em ordem crescente de Id**: o nó; o pai e os filhos, na
  montagem; todos os nós da lista, na entrega. A ordem fixa torna deadlock raro, em vez de rotineiro.
- Deadlock ou lock timeout (1205/1222) viram `ConflitoDeConcorrenciaException` → 409, no padrão que o
  `ReceitaPadraoRepository` já tem.
- **As edições da Fase 2** que a seção 4.7 passa a validar contra o livro (excluir nó, reduzir quantidade)
  e a edição de Roteiro entram no mesmo esquema. Sem isso, um "reduzir quantidade" passaria no meio de um
  "iniciar". Fecha o residual que o comentário de `GravarArvoreAsync`, no `EstruturaRepository`, registra
  ("considerar extrair o mesmo padrão").
- **Toque duplo no celular:** o botão fica desabilitado enquanto envia; se o segundo envio passar, a
  validação de saldo limita o estrago, e o estorno corrige.

### 8.2 Catálogo de erros

`erro` é o código pelo qual o front decide; `mensagem`, a frase para o operador, que nomeia nó, Setor e
números quando ajuda.

| Status | Código | Quando |
|---|---|---|
| 404 | — | nó, Setor, movimento ou montagem inexistente |
| 400 | `QuantidadeInvalida` | quantidade ≤ 0 ou fora da coluna |
| 400 | `DestinoIndevido` | `destinoSetorId` mandado quando o destino é calculado, ou faltando quando é montagem |
| 400 | `EntregaVazia` | lista de entrega vazia |
| 400 | `RoteiroInvalido` | Setor inexistente ou inativo entrando no Roteiro |
| 403 | `Proibido` | estorno de registro alheio sem ser PCP nem Administrador |
| 409 | `SemRoteiro` | iniciar nó sem Roteiro |
| 409 | `NaoEhOPrimeiroPasso` | iniciar num Setor que não é o do primeiro passo |
| 409 | `SaldoInsuficiente` | "Só há 6 de *Suporte* no Corte (passo 2)." |
| 409 | `SemFilhos` | montar nó sem filhos |
| 409 | `MontagemAcimaDoQueFalta` | "Falta montar 4 de *Chassi*." |
| 409 | `FilhosInsuficientes` | nomeia o filho que limita: "*D*: 12 aqui, 16 necessários." |
| 409 | `DestinoForaDoRoteiroDoPai` | Setor de montagem que não está no Roteiro do pai |
| 409 | `PaiSemRoteiro` | entrega para montagem de pai sem Roteiro |
| 409 | `PassoJaAlcancado` | editar, remover ou inserir antes de um passo que já é histórico |
| 409 | `NoComMovimentacao` | excluir nó (ou subárvore) com movimento |
| 409 | `QuantidadeAbaixoDoMovimentado` | reduzir quantidade abaixo do que já andou ou foi montado |
| 409 | `EstornoImpossivel` | a quantidade já foi movida depois; estorno de estorno; baixa de montagem avulsa |
| 409 | `JaEstornado` | o movimento ou a montagem já foi estornado |
| 409 | `PedidoFechado` | movimentar nó de Pedido `Concluido` ou `Cancelado` |
| 409 | `ConflitoDeConcorrencia` | "Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo." |

### 8.3 No front

Cada código tem tradução em `mensagemDeErro`; quando o backend manda `mensagem`, ela é mostrada — o
precedente do `Detalhe`, porque o front não tem como reconstruir "*D*: 12 aqui, 16 necessários". Depois
de um 409 a tela recarrega os dados: o 409 quase sempre quer dizer que o que está na tela ficou velho.

## 9. Testes

### 9.1 Application (fakes, sem banco)

- **A calculadora**, por tabela: saldo, destino, tarefa, sobra, "dá para montar"/"falta". Os números da
  spec do Kit (C de 10, D de 45 com razão 4) entram como casos, inclusive os contraexemplos (a) e (b).
- **Cada caso de uso de escrita**, aceitando e recusando: um teste por código de erro da seção 8.2,
  afirmando o **código**, não só o status.
- **Conservação como propriedade:** sequência aleatória de operações válidas (iniciar, terminar,
  entregar, montar, estornar), com **semente fixa**, sobre uma árvore de três níveis; depois de **cada**
  passo, a soma das posições de cada nó é igual à quantidade dele. É o teste que afirma a regra 9, em vez
  de exemplos escolhidos à mão.
- **Estorno**, nos dois lados: estornar devolve o saldo exato de antes; estornar depois de a quantidade
  ter andado é recusado.

### 9.2 Infrastructure (SQL Server)

- **Mapeamento** de `Movimentacao`, `Montagem` e `QuantidadePorPai`, e **cada `CHECK`** da seção 3 com um
  caso que o banco recusa; o índice único de `EstornoDeId` recusando o segundo estorno.
- **A consulta de saldo bate com a calculadora** sobre os mesmos dados — prova de que o SQL e o C# somam
  igual.
- **Corrida:** dois "iniciar" paralelos no mesmo nó, com saldo para um só; um passa, o outro dá
  `ConflitoDeConcorrencia` ou `SaldoInsuficiente`, e a soma nunca passa da quantidade. Molde do teste de
  corrida do refresh token.
- Asserções **escopadas pelos nós do próprio teste**, nunca por contagem global de tabela (lição do
  flaky de 2026-08-22 registrada no `CLAUDE.md`).

### 9.3 Api (ponta a ponta)

- **Todas as rotas**, com a **matriz de perfis**: Movimentador não inicia, Operador não entrega, estorno
  alheio dá 403, PCP edita Roteiro, Gestão só lê. Cada teste cria o próprio usuário por perfil
  (`UsuarioDeTeste`), sem depender do seed.
- **Critério de pronto:** um Pedido Avulso A ← B, C percorrido inteiro — iniciar, terminar, entregar,
  montar, entregar a Peça na expedição —, com `GET /agrupamentos/{id}/posicoes` respondendo, a cada
  passo, onde está cada peça e se ela aguarda coleta.

### 9.4 Front (Vitest + jsdom)

- **Fila** e **Tarefas**: os três estados, cada um com teste que falha se o estado sumir; as ações; a
  entrega em lote; o recarregamento depois de 409.
- `ResumoDePosicoes`; o que a árvore ganhou (pílulas, histórico com estorno, editor de Roteiro, campo de
  razão); as chaves novas de `permissoes.ts`, cobertas pela guarda de espelhamento que já existe.
- `npm run build` no ciclo.

### 9.5 Verificação manual

No navegador, em largura de celular, com três contas: `operador` (recriado à mão, como o `CLAUDE.md`
prevê), `movimentador` (criado do mesmo jeito, fora do seed, pelo mesmo motivo) e `pcp`. Percorre o
critério de pronto e o estorno.

### 9.6 Restrição de bancada

O container da sessão de nuvem em que esta spec foi escrita **não tem `dotnet`**, e o engine do Docker
não responde: ali só a suíte do front roda. As tasks de backend precisam de uma bancada com .NET e SQL
Server — a máquina do usuário ou um ambiente de nuvem configurado para isso. Decide-se no plano.

## 10. Mudanças nos documentos

A **primeira task do plano** aplica esta seção, com gate inteiro (documentação não dispensa review:
estas specs viram texto do TCC). O plano ancora cada troca no texto exato do arquivo, no molde "Trocar X
por Y" da frente do Kit, e confere as âncoras contra a árvore antes de despachar. As tasks de código vêm
depois.

### 10.1 `specs/02-modelo-de-dados.sql` e `db/seed.sql`

A seção 3 inteira: `dbo.Montagem` e `dbo.Movimentacao` (com índices), `EstruturaItem.QuantidadePorPai`
com `CK_EstruturaItem_QuantidadePorPai`, a saída de `dbo.EstruturaSetorHistorico` e dos dois índices
dela, o comentário de `dbo.Perfil.Nome`, a nota nas consultas de KPI de exemplo. Em `db/seed.sql`,
`('Movimentador')` no `MERGE` de perfis.

### 10.2 `specs/01-dominio-e-regras-de-negocio.md`

**Glossário — alterações:**

- **EstruturaItem.QuantidadePorPai:** "Serve à trava de montagem, ao conjunto completo e às tarefas do
  Movimentador (regras 23 a 25)" passa a "Serve à montagem de todo nó com filhos, às tarefas do
  Movimentador e à sobra (regras 23, 24 e 30) e, no Kit, à trava e ao conjunto completo (regras 24 e
  25)"; "a coluna entra no schema no início da Fase 3B" passa a "a coluna entra no schema na Fase 3
  (decisão de 2026-09-24, que trouxe a montagem da 3B para a 3)".
- **Aguardando coleta:** "Como é representado no banco é decisão da Fase 3." passa a "No banco, é uma
  posição do livro de movimentações (`dbo.Movimentacao`), guardada no Setor e no passo em que a
  quantidade terminou; o destino é calculado (regra 29)."
- **Montado:** "Pelas regras atuais, o registro de montagem só existe sob a trava da regra 24 — como sai
  o filho de um nó sem trava está em "Pontos ainda em aberto"." passa a "O registro de montagem existe
  para todo nó com filhos, de Kit ou Avulso; só a validação da trava é restrita ao Kit (regra 24)."; "O
  destino entra no schema no início da Fase 3B." passa a "O destino entra no schema na Fase 3."

**Glossário — entradas novas:**

| Termo | Definição |
|---|---|
| **A iniciar** | Estado da quantidade de um nó que ainda não entrou em nenhum Setor. Todo nó nasce assim, com a quantidade inteira. Conta como **em produção** (regra 9). Sai daqui pela primeira entrada, registrada pelo operador do primeiro Setor do Roteiro quando ele pega o material para trabalhar (regra 28). |
| **Local de expedição** | O lugar da fábrica para onde o Movimentador leva a Peça que terminou o Roteiro, e de onde as cargas são expedidas (regra 29). Não é um Setor: não fabrica, não entra em Roteiro nem nos KPIs de tempo por Setor. Conta como **em produção** até a expedição (Fase 5). |
| **Sobra** | Quantidade de um Item que terminou o Roteiro, ou foi entregue para a montagem, além do que o pai ainda precisa: `(quantidade do pai − total montado) × QuantidadePorPai`. É identificada pelo estado e não vira tarefa de ninguém; se não for usada, sai como perda de motivo `Descarte` (regras 17 e 30). |

**Regra 9 — alteração:** "em produção (nos Setores ou aguardando coleta — regra 22)" passa a "em
produção (a iniciar, nos Setores, aguardando coleta, aguardando montagem ou no local de expedição —
regras 22, 28 e 29)". E, no parágrafo "Alterada pela decisão de 2026-09-15", "O destino "montado" só
entra no schema (`02-modelo-de-dados.sql`) no início da Fase 3B." passa a "O destino "montado" entra no
schema na Fase 3. **Alterada de novo em 2026-09-24:** "em produção" ganhou as posições a iniciar,
aguardando montagem e no local de expedição (spec da Fase 3)."

**Regra 24 — alteração:** o primeiro parágrafo ganha, antes das três condições: "**Montar é registro de
todo nó com filhos**, de Agrupamento Kit ou Avulso: o operador registra "montei N" no Setor onde os
filhos estão, e baixa-se `N × QuantidadePorPai` de cada filho direto para "montado" (decisão de
2026-09-24; até então, montar só existia sob a trava). O que se segue é a **trava**, que restringe essa
montagem no Kit." E "um filho pode ser montado no mesmo Setor em que o pai será montado, caso que a spec
da Fase 3B decide" fica como está.

**Regra 22 — alteração:** "como esse estado é representado no banco é decisão da Fase 3" passa a "no
banco, ele é uma posição do livro de movimentações (spec da Fase 3)".

**Regra 26 — alteração:** "a razão serve à trava, ao conjunto completo e às tarefas" passa a "a razão
serve à montagem de todo nó, às tarefas e à sobra e, no Kit, à trava e ao conjunto completo".

**Nota antes da regra 22:** "e são implementadas nas Fases 3, 3B e 5" ganha "— a montagem de todo nó e
`QuantidadePorPai`, na Fase 3, por decisão de 2026-09-24".

**Regras novas:**

> *As regras 28 a 30 foram decididas em 2026-09-24, no brainstorm da Fase 3
> (`docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`).*
>
> 28. **Todo nó nasce a iniciar, e a primeira entrada é do operador.** A quantidade inteira de um nó
>     nasce **a iniciar**, que conta como em produção (regra 9). A fabricação começa quando o operador
>     do primeiro Setor do Roteiro pega o material para trabalhar — parte do material já está no Setor,
>     parte é levada pelo almoxarifado —, e é esse operador quem registra a primeira entrada, sem o
>     Movimentador: não há o que levar. A primeira entrada **exige Roteiro**; nó sem Roteiro é pendência
>     do PCP, que edita o Roteiro de qualquer nó (regra 7). Depois que o nó começa a andar, os passos já
>     alcançados são histórico e não se editam. O Pedido passa a `EmProducao` na primeira entrada de
>     qualquer nó dele.
> 29. **O fim do Roteiro.** O que termina um passo que não é o último vai para o próximo passo, sem
>     escolha. O que termina o **último** passo aguarda coleta com um destino que depende do nó:
>     - **Item** — a montagem do pai, num Setor do Roteiro do pai que o **Movimentador escolhe** ao
>       entregar, com sugestão do sistema; a montagem é registrada pelo operador daquele Setor (regra 24).
>       Entregue no Setor errado, redireciona-se com outra entrega.
>     - **Peça** — o **local de expedição**, levada pelo Movimentador como tarefa; a expedição (Fase 5)
>       sai de lá.
> 30. **A sobra é identificada, não tarefa, e só sai por descarte.** O que um Item tem, no fim do
>     Roteiro ou entregue para a montagem, além do que o pai ainda precisa, é **sobra**: sai da lista de
>     tarefas do Movimentador e aparece à parte, no Setor onde está. Só deixa de estar em produção por
>     uma perda de motivo `Descarte` (regra 17), registrada pelo mesmo ator da perda — Qualidade ou PCP —
>     a qualquer momento depois de virar sobra. A regra 13 não muda: o Pedido conclui com sobra viva,
>     que continua listada até ser descartada.

**Pontos ainda em aberto:** o item "**Como sai de "em produção" o filho de um nó que não passa pela
trava de montagem.**" sai, trocado por uma nota datada: "*Decidido em 2026-09-24 (spec da Fase 3):
montar passou a ser registro de todo nó com filhos, e só a validação da trava ficou restrita ao Kit —
ver regra 24.*"

### 10.3 `specs/06-roadmap-mvp.md`

**Fase 3 — texto novo** (substitui a lista de bullets e o critério de pronto; a nota "Ampliada em
2026-09-15" fica, e ganha a nota de 2026-09-24):

> - Livro de movimentações (`dbo.Movimentacao`): a quantidade de cada nó repartida entre a iniciar, no
>   Setor (por passo do Roteiro), aguardando coleta, aguardando montagem, no local de expedição e
>   montado; conservação de quantidade (regra 9) por construção, validada na aplicação.
> - **Terminar e mover como ações separadas** (regra 22): o operador inicia (primeira entrada, regra
>   28) e termina; o **Movimentador** entrega no próximo destino. Perfil novo `Movimentador` — linha em
>   `dbo.Perfil`, na tabela `web/src/auth/permissoes.ts` e nos `[Authorize(Roles)]`.
> - **Montagem de todo nó com filhos** (regra 24): registro "montei N", destino "montado" e
>   `EstruturaItem.QuantidadePorPai` (regra 26), sem a trava, que é da 3B.
> - **Roteiro editável por nó** (regras 7 e 28), pelo PCP; passo alcançado não se edita.
> - Fim do Roteiro (regra 29): Item vai à montagem do pai, num Setor que o Movimentador escolhe; Peça
>   vai ao local de expedição.
> - **Estorno** de registro errado, pelo autor ou pelo PCP.
> - Tela de **fila do setor** para o operador: a iniciar, em trabalho, aguardando coleta, aguardando
>   montagem com "dá para montar N; falta X de Y", e **sobra** (regra 30).
> - Tela **Tarefas** do Movimentador com os **Itens prontos** (regra 23), calculada a partir do estado e
>   atualizada periodicamente — sem tabela de aviso.
> - Critério de pronto: dá para acompanhar, item por item, em qual posição cada peça está — inclusive se
>   aguarda coleta —; o Movimentador vê o que tem a levar e registra a entrega; um Pedido percorre
>   iniciar, terminar, entregar, montar e chegar ao local de expedição.
>
> > **Ampliada de novo em 2026-09-24** pela spec `2026-09-24-fase-3-rastreamento-de-setor-design.md`:
> > a montagem de todo nó e `QuantidadePorPai` vieram da 3B, e o Roteiro editável por nó, que o `05`
> > listava sem fase, entrou aqui.

**Fase 3B — texto novo** (os dois primeiros bullets):

> - `Setor.UtilizaKit` (regra 24); o schema entra no início desta fase. A montagem, o destino "montado"
>   e `QuantidadePorPai` já existem desde a Fase 3.
> - **Trava de montagem** por nó (regra 24): no Kit, em Setor com `UtilizaKit`, a montagem só aceita o
>   que os filhos diretos presentes permitem, e a saída do nó é limitada ao total montado.

(Os bullets de conjunto completo, tarefa "Kit pronto", "A decidir na spec desta fase" e o critério de
pronto ficam como estão.)

**Fase 4 — bullets novos:**

> - **Onde cada material fica** — no almoxarifado, ou estocado num Setor (as chapas no Corte) —, e a
>   **requisição de material** que o operador abre para o que falta e o Almoxarifado atende (decisão de
>   2026-09-24, no brainstorm da Fase 3).

**Fase 5 — alterações:** "Registro de `Expedicao` (remessas parciais), só de Peça" ganha "— a expedição
baixa do **local de expedição** (regra 29)"; e um bullet novo:

> - O nó **pronto** do Retrabalho (regra 27) nasce aguardando coleta pelo tipo de movimento `Pronto`;
>   `Expedido` e `Perdido` entram como posições do livro; o `Descarte` da sobra é registrado pelo ator
>   da perda (regra 30).

**Fase 6 — alteração:** "calculado a partir de `DataEntrada` (chegada) até `DataSaida`.
`DataInicioExecucao` já existe no schema e pode ser adotado depois, sem migração, caso o negócio queira
decompor fila x execução." passa a "calculado sobre o livro de movimentações (`dbo.Movimentacao`),
pareando entradas e saídas de cada Setor por ordem de chegada. Separar fila de execução dentro do Setor
exigiria uma posição a mais no livro — decidir nesta fase, se o negócio pedir."

### 10.4 `specs/04-fluxos-de-usuario.md`

**"2. Apontamento em Setor" — texto novo** (substitui o cabeçalho de perfil, a nota de 2026-09-15 e os
quatro passos):

> *Perfis: Operador, Movimentador e PCP*
>
> > **Reescrito em 2026-09-24** pela spec da Fase 3, que decidiu o que a nota de 2026-09-15 deixava em
> > aberto. Em Agrupamento Kit, a trava de montagem e o conjunto completo (regras 24 e 25) entram na
> > Fase 3B.
>
> 1. **PCP** confere que todo nó tem Roteiro; nó sem Roteiro aparece como pendência na árvore, e o PCP
>    o edita (regra 28).
> 2. **Operador** abre a fila do seu Setor e vê o que está a iniciar ali, em trabalho, aguardando coleta,
>    aguardando montagem e a sobra.
> 3. Ao pegar o material para trabalhar num nó cujo primeiro passo é ali, **inicia** uma quantidade
>    (regra 28). Ao terminar, **termina** a quantidade feita: ela passa a aguardar coleta.
> 4. **Movimentador** abre Tarefas, vê os Itens prontos com o destino de cada um e **entrega**: no próximo
>    passo; na montagem do pai, num Setor do Roteiro dele que escolhe; ou, se for Peça no fim do Roteiro,
>    no local de expedição (regra 29).
> 5. **Operador** do Setor de montagem vê "dá para montar N; falta X de Y" e **monta** o que dá (regra
>    24).
> 6. Registro errado se corrige por **estorno**, pelo autor ou pelo PCP, enquanto a quantidade não tiver
>    andado.

**"7. Consulta de KPIs" — item 1:** "(`EstruturaSetorHistorico`, `DataSaida - DataEntrada`, a partir da
chegada — `DataInicioExecucao` fica disponível para refinar esse cálculo separando fila de execução,
quando/se for preenchido)" passa a "(sobre o livro de movimentações, pareando entradas e saídas de cada
Setor por ordem de chegada — ver Fase 6 em `06-roadmap-mvp.md`)".

### 10.5 `specs/05-api-endpoints.md`

- **"Execução / Rastreamento"** é reescrita com a seção 5 desta spec: o cabeçalho "Planejado — Fase 3/4,
  ainda sem controller" e a nota de 2026-09-15 dão lugar às rotas das seções 5.1 e 5.2, com os perfis;
  `POST /estrutura-itens/{id}/separacoes-material` fica, marcada como Fase 4.
- **Seção "Estrutura", bloco "Planejado"**: `GET/POST /estrutura-itens/{id}/roteiro` sai do bloco e
  vira `GET` e `PUT /estrutura/{id}/roteiro`, da Fase 3; o parágrafo "Sem fase atribuída, e isto foi
  medido, não esquecido" passa a falar só de `materiais`.
- As mudanças da seção 5.3 nas rotas existentes da árvore.

### 10.6 Spec do Kit (`2026-09-15-kit-montagem-e-movimentacao-design.md`)

**Errata datada, anexada ao fim, sem reescrever o texto original** (precedente da errata da Fase 2):

> ## Errata — 2026-09-24, spec da Fase 3
>
> - **§2, "Fica para o início de cada fase":** `EstruturaItem.QuantidadePorPai` e o destino "montado"
>   entraram no schema na **Fase 3**, não na 3B: a spec da Fase 3 fez de montar um registro de todo nó
>   com filhos (regra 24), e a baixa `N × QuantidadePorPai` precisa da razão.
> - **§9, "Fase 3":** as três perguntas foram respondidas pela spec da Fase 3 — "aguardando coleta" é uma
>   posição do livro de movimentações; a quantidade conta como em produção desde o cadastro, na posição
>   **a iniciar** (regra 28), e o nó pronto do Retrabalho nasce aguardando coleta pelo tipo `Pronto`, na
>   Fase 5; o filho de nó sem trava sai de "em produção" pela montagem, que vale para todo nó.
> - **§9, "Fase 3B":** "o que a edição de nó da Fase 2 faz com `QuantidadePorPai`" foi respondido na
>   Fase 3 — a edição é livre, e a baixa já gravada não muda. "Filho que conclui a própria montagem na
>   mesma Solda em que o pai será montado" continua da 3B, agora com um caminho existente para comparar:
>   fora da trava, o filho termina o último passo, aguarda coleta e é entregue em `AguardandoMontagem`
>   no mesmo Setor.
> - A dívida **m12** da review de branch (o `Descarte` sem ator nem momento) foi fechada pela regra 30.

### 10.7 `CLAUDE.md`

- **Invariantes de negócio:** no bullet do Kit, "(Regras 22 a 27, decididas em 2026-09-15 …
  implementadas a partir da Fase 3.)" ganha, antes do parêntese: "Montar é registro de **todo** nó com
  filhos (Kit ou Avulso); só a trava é do Kit." E um bullet novo: "**O livro de movimentações é só de
  inclusão.** Correção é estorno (movimento inverso que aponta o original), nunca `UPDATE` nem `DELETE`
  em `dbo.Movimentacao`."
- **Blocos `ALTER` idempotentes da Fase 3** na seção de banco, no padrão dos existentes: criar
  `dbo.Montagem` e `dbo.Movimentacao` com os índices; acrescentar `QuantidadePorPai`, preencher e criar o
  `CHECK`; incluir o perfil `Movimentador`; remover `dbo.EstruturaSetorHistorico`.

### 10.8 `README.md`

O parágrafo "A seguir vêm a **Fase 3** (…) e a **Fase 3B** (…)" passa a: "A seguir vêm a **Fase 3**
(rastreamento de setor: livro de movimentações, terminar e mover como ações separadas — com o perfil
**Movimentador** e a tela de tarefas dele —, montagem de todo nó com filhos, Roteiro editável por nó,
fila do setor para o operador e chegada ao local de expedição) e a **Fase 3B** (Kit: trava de montagem
por nó, com conjunto completo na entrada do Setor marcado como de montagem — hoje, a Solda)."

### 10.9 Não mudam

`specs/00-visao-geral.md` e `specs/03-arquitetura-tecnica.md` (conferido: nenhuma menção a
`EstruturaSetorHistorico` nem a apontamento), a regra 13 e o rastreamento por lote agregado, sem serial.

## 11. Deixado para outras fases

- **3B:** trocar o Tipo do Agrupamento ou a `UtilizaKit` com produção em andamento; filho que conclui a
  própria montagem na mesma Solda do pai; qual ação da regra 22 o limite pelo total montado trava; a
  entrada num Setor `UtilizaKit` que não é para a montagem do pai; como a perda do próprio nó entra nos
  dois tetos (tudo da §9 da spec do Kit, menos o que a errata da seção 10.6 respondeu).
- **4:** "onde cada material fica" e a requisição de material (decisão 2.3).
- **5:** posições `Expedido` e `Perdido`, tipo `Pronto`, registro do `Descarte` (regra 30), status
  `AguardandoExpedicao`, fechamento, e as perguntas da §9 da spec do Kit para a Fase 5.
- **6:** KPI sobre o livro, por FIFO; separar fila de execução, se o negócio pedir.
- **Sem fase:** CRUD de Usuário — até existir, cada conta de Movimentador nasce por SQL na VPS.
