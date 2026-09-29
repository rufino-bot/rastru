# Fase 3D — Ajustes pós-verificação da Fase 3 — design

**Data:** 2026-09-28. **Origem:** os pontos que o usuário levantou na verificação manual da Fase 3 (§9.5
da spec da Fase 3, feita no celular) e o brainstorm de 2026-09-28. **Estado:** desenho aprovado pelo
usuário, ponto a ponto; falta o plano.

**Posição na fila** (decisão do usuário, 2026-09-28): *"primeiro os 4 pontos, depois filtros, 1F e 3B"*.
Esta spec cobre os pontos 1 a 3. O quarto era a **posição** dos filtros da fila por Material e por
Pedido, e a própria resposta a deu: eles vêm logo depois desta fase, com brainstorm próprio (a pergunta
"Material do catálogo ou do nó" continua aberta lá).

Spec de referência, que esta emenda sem reescrever: `docs/superpowers/specs/2026-09-24-fase-3-rastreamento-de-setor-design.md`
(daqui em diante, "spec da Fase 3").

## 1. Escopo

| Ponto | O que o usuário pediu | O que esta fase faz |
|---|---|---|
| 3 | O pai aparecer **só** no card de montagem, e a montagem amarrada à produção do pai | **Iniciar vira o verbo único**: iniciar um nó com filhos consome os filhos presentes. "Montar" deixa de ser ação. O Setor ganha uma **atividade** opcional que dá nome aos botões |
| 2 | Estornar na fila, onde o operador trabalha | **Estorno rápido** nas linhas de "Em trabalho" e "Aguardando coleta" (e na sobra do último passo), com lista curta dos registros estornáveis |
| 1 | Pausar um Pedido para outro mais urgente passar na frente | **Pausa** registrada em tabela própria; recusa só o **Iniciar** |

A ordem de implementação é a da tabela: o Ponto 2 usa a linha "Em trabalho" do pai que o Ponto 3 cria,
e a pausa recusa o Iniciar já unificado.

**Fora de escopo:** filtros da fila (próxima fase), 1F, 3B, e tudo das Fases 4 a 6. As consequências
desta fase para a 3B, a 5 e a 6 estão na seção 10.

## 2. Decisões do brainstorm, com o porquê

### 2.1 Iniciar é o verbo único; o pai começa consumindo os filhos (Ponto 3)

**Fato da fábrica, trazido pelo usuário:** o Setor onde um pai é montado é **sempre o primeiro passo**
do Roteiro dele. O pai não tem corpo próprio processado antes: tudo o que é físico está nos filhos, e
ele passa a existir quando os filhos são juntados.

**O incômodo que motivou:** na Fase 3, montagem (baixa dos filhos) e posição do pai (Início/Término)
são registros independentes. Na verificação deu para montar sem iniciar o pai, e dá para terminar o pai
sem ter montado — ele iria à expedição com os filhos ainda "em produção". E o pai aparecia em dois
lugares da fila: em "A iniciar aqui" e no card de "Aguardando montagem".

**A decisão, nas palavras do usuário:** o que o operador faz depois que os filhos são entregues *"corresponde
ao setor"* — solda, caldeiraria, montagem —, então *"não resolveria ter regra só pra Montar, teríamos que
ter um verbo mais genérico pra compor essa ação"*. Daí:

- **Iniciar N** de um nó **sem filhos** continua como hoje.
- **Iniciar N** de um nó **com filhos** exige os filhos presentes no Setor e **os consome** — é o registro
  que a montagem fazia, sob o verbo comum — e põe o pai em produção no primeiro passo, na mesma transação.
- **Montar deixa de ser ação.** O pai tem um lugar só na fila: o card com os filhos e o botão Iniciar.

**O que fecha por construção:** o pai só entra em produção consumindo os filhos, então tudo o que ele
termina, entrega ou leva à expedição já foi montado. A saída do pai limitada ao total montado — a
**trava** da regra 24, que a Fase 3 deixou para a 3B e restrita a Kit + `UtilizaKit` — passa a valer
para **todo nó que já tem filhos quando entra em produção**, sem validação nova (seção 10.1).

> **Ressalva do fechamento da fase (2026-09-29).** "Por construção" não alcança um caso: acrescentar
> filho a um nó **já iniciado** é livre (seção 4.7 da spec da Fase 3, "Acrescentar filho | livre"), e
> `AcrescentarFilho` não guarda status nem livro. Uma folha iniciada que ganha filho tem
> `SaidoDeAIniciar > TotalMontado` e pode sair acima do montado. Esse caso **não é tratado na 3D**;
> volta à 3B (seção 10.1), como decisão de desenho.

**Descartadas:**
- **Montar registra também o Início do pai**, mantendo o botão "Montar": fechava os mesmos buracos, mas
  mantinha um verbo que não descreve o trabalho de todo Setor.
- **Só de tela** — o card de montagem ganhar o botão Iniciar do pai: resolvia onde o pai aparece e
  deixava os dois registros soltos, o incômodo original.

### 2.2 O destino do filho pronto é calculado: o primeiro passo do pai (revê a decisão 2.4 da Fase 3)

A decisão 2.4 da Fase 3 deixava o Movimentador escolher, entre os Setores do Roteiro do pai, onde
entregar o filho que terminou o próprio Roteiro. Com o pai começando sempre no primeiro passo, entregar
em qualquer outro Setor é sempre engano: o filho fica parado esperando um Iniciar que ali nunca é
aceito. **O destino passa a ser calculado** — o Setor do primeiro passo do pai —, sem escolha, como o
"próximo passo" já é.

O **redirecionamento** (`AguardandoMontagem(S)` → `AguardandoMontagem(S')`) continua, com destino também
calculado: serve ao filho entregue antes de o PCP editar o primeiro passo do pai (seção 4.4).

**Descartada:** manter a escolha com sugestão. O `<select>` da tela Tarefas deixaria o engano possível sem
que existisse um caso em que outra escolha fosse certa.

### 2.3 O Setor ganha uma atividade opcional, que dá nome aos botões

O usuário quis o nome da ação de cada Setor — *"o Montar acaba sendo bastante associado com o passo final
de muita peça lá na empresa"* —, sem atrapalhar o cadastro de Setor novo.

**Um verbo só por Setor não serve:** o operador aperta dois botões por peça, e "Soldar 5" se lê tanto
"vou soldar 5" (Iniciar) quanto "soldei 5" (Terminar) — ambiguidade que, no celular, vira erro de toque,
e o livro só se corrige por estorno. Por isso o Setor guarda um **substantivo** — a atividade —, e os
botões se compõem: "Iniciar montagem" / "Terminar montagem", "Iniciar solda" / "Terminar solda".

**Opcional:** Setor sem atividade mostra "Iniciar" / "Terminar". Cadastrar Setor novo — inclusive pelo
cadastro sob demanda da 1F — continua pedindo só o nome.

Os nomes internos **não mudam**: `dbo.Montagem`, as posições `AguardandoMontagem` e `Montado`, o tipo de
movimento `Montagem` e o rótulo "Aguardando montagem" descrevem o que acontece com os filhos (passam a
fazer parte do pai), seja qual for o Setor.

**Descartadas:** dois rótulos livres por Setor, um de início e um de fim (dois campos, e reabre a
ambiguidade se alguém cadastrar um verbo ambíguo); nenhum campo novo (o nome do Setor já está no título
da fila, mas o usuário quis o nome da ação).

### 2.4 Estorno rápido na fila (Ponto 2)

O lugar foi decidido pelo usuário na verificação: *"é na tela da Fila, pq o operador que vai fazer esse
estorno do que terminou"*. Depois, no brainstorm, estendido a "Em trabalho" e à montagem.

A dificuldade: uma linha da fila é um **saldo** (nó, passo, quantidade) que pode somar vários registros,
de autores diferentes, e o estorno desfaz **um movimento inteiro** (spec da Fase 3, §4.5 — sem estorno
parcial). **Decisão:** cada linha traz os registros por trás dela que ainda dá para estornar; tocar em
"Estornar" abre a **lista curta** deles (quem, quando, quanto), cada um com seu botão, e, **quando há um
só** — o caso comum —, vai **direto à confirmação**, com os detalhes.

Onde entra, e o que a lista contém:

| Linha da fila | Registros estornáveis |
|---|---|
| "Em trabalho", nó **sem filhos**, primeiro passo | os `Inicio` |
| "Em trabalho", nó **com filhos**, primeiro passo | as `Montagem` (o Início do pai que consumiu os filhos — seção 4.2) |
| "Em trabalho", passos seguintes | nenhum pela fila: quem pôs a quantidade ali foi uma `Entrega` do Movimentador, que se estorna pelo histórico do nó |
| "Aguardando coleta" | os `Termino` |
| "Sobra" do último passo | os `Termino` — o saldo `AguardandoColeta` que é só sobra aparece nesta seção e não em "Aguardando coleta", e um Término dele também pode ter sido engano |

O estorno da montagem mora na linha **"Em trabalho" do pai**, não no card de "Aguardando montagem": o
card só existe enquanto algum filho aguarda no Setor, e some justamente quando o operador iniciou tudo
o que havia — o caso típico de "errei o N".

**Descartadas:** estornar sempre o registro mais recente do usuário (simples, mas o erro mais antigo
volta ao histórico, e o operador não vê o que está desfazendo); botão só quando a linha tem um registro
só (o botão aparece e some por um motivo que o operador não enxerga).

### 2.5 Pausar Pedido (Ponto 1)

A ideia do usuário: marcar que nenhuma peça daquele Pedido está sendo fabricada, para outro Pedido mais
urgente entrar no lugar.

- **A pausa recusa só o Iniciar.** Terminar, Entregar e Estornar registram algo que **já aconteceu** no
  chão: se o operador acabou de cortar 5 peças quando o Pedido foi pausado, recusar o "terminei" deixaria
  o livro mentindo até a retomada. Com Iniciar unificado (2.1), a recusa cobre também o início do pai.
- **Registro em tabela de pausas, só de inclusão** — uma linha por intervalo, com quem pausou e quando,
  e quem retomou e quando. O motivo: a Fase 6 calcula tempo total por Pedido, e uma pausa que não foi
  gravada não se reconstitui depois. Um status novo em `Pedido.Status` foi descartado de saída: apagaria
  a diferença entre `Aberto` e `EmProducao`, da qual o Iniciar depende.
- **Quem pausa e retoma:** PCP e **Gestão**, sempre com o Administrador. É a **primeira escrita da
  Gestão** no sistema — até aqui ela só lia.
- **Motivo:** texto livre, opcional ("Pedido 123 urgente"), mostrado junto da marca de pausado.
- **Na fila:** em "A iniciar aqui", o que é de Pedido pausado vai para um grupo **"Pausados"** no fim da
  seção, com a pílula e o motivo, sem Iniciar. Nas demais seções e na tela Tarefas, só a pílula — tudo
  ali continua funcionando.

  > **Desvio do plano da Fase 3D, fora da lista D1–D6 (2026-09-28): a seção 6.1 vale sobre esta
  > frase.** A frase acima diz que, fora de "A iniciar aqui", só a pílula aparece; a seção 6.1 diz que
  > a pílula aparece "em qualquer seção" da fila e que "o motivo, quando há, vai ao lado". O plano
  > seguiu a 6.1, e é o que o código faz: na fila, toda linha de Pedido pausado, de qualquer seção,
  > mostra a pílula **e** o motivo (o `CabecalhoDoNo` de `FilaDoSetorPage` é o de todas as seções);
  > fora da fila, a tela Tarefas e a lista de Pedidos mostram só a pílula. O motivo de valer a 6.1: é
  > a seção que descreve a tela, e o código a segue com um só componente para todas as seções da fila.

**Descartadas:** colunas no Pedido (perdem o histórico ao retomar); só um booleano (nem quem nem quando);
recusar também Terminar/Entregar/Montar (livro atrasado em relação ao chão durante a pausa); o pausado
sumir da fila (quem o procura não sabe se sumiu ou está pausado) ou ficar no lugar (misturado ao
liberado); pausa por Operador (a pausa deixa de ser decisão de planejamento).

## 3. Modelo de dados

### 3.1 `dbo.PedidoPausa` (nova)

```sql
-- Uma linha por intervalo em que o Pedido ficou pausado. SÓ INSERÇÃO, exceto o fecho do intervalo
-- (RetomadoEm/RetomadoPorUsuarioId, gravados uma vez). Pausado = existe linha com RetomadoEm NULL.
-- A pausa recusa só o Iniciar (spec da Fase 3D, seção 2.5).
CREATE TABLE dbo.PedidoPausa (
    Id                    INT IDENTITY(1,1)  NOT NULL,
    PedidoId              INT                 NOT NULL,
    PausadoEm             DATETIME2           NOT NULL CONSTRAINT DF_PedidoPausa_PausadoEm DEFAULT (SYSUTCDATETIME()),
    PausadoPorUsuarioId   INT                 NOT NULL,
    Motivo                NVARCHAR(200)       NULL,
    RetomadoEm            DATETIME2           NULL,
    RetomadoPorUsuarioId  INT                 NULL,
    CONSTRAINT PK_PedidoPausa PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_PedidoPausa_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT FK_PedidoPausa_PausadoPorUsuario FOREIGN KEY (PausadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT FK_PedidoPausa_RetomadoPorUsuario FOREIGN KEY (RetomadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_PedidoPausa_RetomadaCompleta
        CHECK ((RetomadoEm IS NULL AND RetomadoPorUsuarioId IS NULL)
            OR (RetomadoEm IS NOT NULL AND RetomadoPorUsuarioId IS NOT NULL)),
    CONSTRAINT CK_PedidoPausa_RetomadaAposPausa CHECK (RetomadoEm IS NULL OR RetomadoEm >= PausadoEm)
);
-- No máximo uma pausa aberta por Pedido.
CREATE UNIQUE INDEX UX_PedidoPausa_UmaAbertaPorPedido
    ON dbo.PedidoPausa (PedidoId) WHERE RetomadoEm IS NULL;
```

O fecho do intervalo é uma atualização única de uma linha aberta — o mesmo desenho de
`Montagem.EstornadaEm`. A regra "o livro é só de inclusão" do `CLAUDE.md` é sobre `dbo.Movimentacao` e
não muda.

### 3.2 `dbo.Setor.Atividade` (coluna nova)

`Atividade NVARCHAR(40) NULL` — substantivo em minúscula, como aparece no meio do botão ("montagem",
"solda", "corte"). Texto só com espaços é gravado como `NULL`, para o botão não virar "Iniciar ".

### 3.3 `dbo.Movimentacao`: o Início do pai aponta a Montagem

O movimento do pai (`Inicio`, `AIniciar` → `NoSetor(S, primeira Ordem)`) grava o `MontagemId` da montagem que o
criou — é o que permite estornar os dois juntos (seção 4.2) e recusar o estorno avulso dele. Hoje a
constraint `CK_Movimentacao_MontagemSoNaBaixa` proíbe `MontagemId` em `Inicio`. Passa a:

```sql
CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
    CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
        OR (Tipo IN ('Inicio', 'Estorno'))            -- Inicio: MontagemId só no início de um pai
        OR (Tipo NOT IN ('Montagem', 'Inicio', 'Estorno') AND MontagemId IS NULL)),
```

e entra o índice que garante **um** Início por Montagem:

```sql
CREATE UNIQUE INDEX UX_Movimentacao_UmInicioPorMontagem
    ON dbo.Movimentacao (MontagemId) WHERE Tipo = 'Inicio' AND MontagemId IS NOT NULL;
```

O comentário de cabeçalho de `dbo.Montagem` ("Registro de 'montei N'") passa a dizer que a montagem é o
início do pai: a linha registra o consumo dos filhos, e o `Inicio` do pai a aponta.

### 3.4 Migração

`db/alter-fase-3d.sql`, idempotente, no padrão do `db/alter-fase-3.sql` (carga com `-b -f 65001`). O
**banco de dev é descartável** e deve ser **regenerado** depois da migração: as montagens gravadas na
verificação da Fase 3 foram feitas sem o Início do pai e não respeitam a regra nova. Não há deploy, então
não há dado de produção a converter.

## 4. Operações e regras

As regras gerais da spec da Fase 3 (§4: transação, validação pela calculadora, `PedidoFechado`) valem
igual. O que muda:

### 4.1 Iniciar (Operador, no Setor S): nó X, quantidade N — substitui §4.1 e §4.4 da Fase 3

- **Exige, para todo nó:** X tem Roteiro (`SemRoteiro`); o primeiro passo do Roteiro de X é em S
  (`NaoEhOPrimeiroPasso`); N ≤ saldo `AIniciar` de X (`SaldoInsuficiente`); o Pedido não está `Concluido`
  nem `Cancelado` (`PedidoFechado`); **o Pedido não está pausado** (`PedidoPausado`).
- **Exige, se X tem filhos:** para cada filho direto c, `N × QuantidadePorPai(c)` ≤ saldo
  `AguardandoMontagem(S)` de c (`FilhosInsuficientes`, nomeando o filho que limita).
- **Grava, nó sem filhos:** um `Inicio`, `AIniciar` → `NoSetor(S, primeira Ordem)` — como hoje.
- **Grava, nó com filhos:** uma `Montagem` (X, S, N); para cada filho c, um `Montagem`
  `AguardandoMontagem(S)` → `Montado` de `N × QuantidadePorPai(c)`; e um `Inicio` de X, `AIniciar` →
  `NoSetor(S, primeira Ordem)`, de N, com o `MontagemId`.
- **Efeito:** se o Pedido estava `Aberto`, passa a `EmProducao`, e o status não volta com o estorno —
  como hoje.

O teto "N ≤ o que falta montar" (`MontagemAcimaDoQueFalta`) fica coberto por `SaldoInsuficiente`: com o
pai entrando em produção só pela montagem, o saldo `AIniciar` do pai **é** a quantidade menos o total
montado. `MontagemAcimaDoQueFalta` e `SemFilhos` saem do catálogo (seção 8).

### 4.2 Estornar — emenda a §4.5 da Fase 3

- **A Montagem** se estorna inteira, como hoje, e agora inclui o `Inicio` do pai que a aponta: grava um
  `Estorno` para cada baixa de filho **e** para o Início do pai, e marca `EstornadaEm`. Só passa se o
  pai ainda tiver N em `NoSetor(S, primeira Ordem)` — isto é, se ninguém o terminou depois (`EstornoImpossivel`).
- **O `Inicio` de um pai** não se estorna avulso (`EstornoImpossivel`, com mensagem apontando a
  montagem) — o mesmo tratamento que a baixa de filho já tem.
- **Quem estorna** não muda: o autor, ou PCP ou Administrador.

### 4.3 Entregar — emenda a §4.3 da Fase 3

| Origem | Situação | Destino | `destinoSetorId` |
|---|---|---|---|
| `AguardandoColeta(S, k)` | existe passo depois de k | `NoSetor` do próximo passo | proibido |
| `AguardandoColeta(S, k)` | k é o último, nó é **Item** | `AguardandoMontagem(P1)`, P1 = Setor do **primeiro passo do pai** | **proibido** (era obrigatório) |
| `AguardandoColeta(S, k)` | k é o último, nó é **Peça** | `NaExpedicao` | proibido |
| `AguardandoMontagem(S)` | redirecionamento, S ≠ P1 | `AguardandoMontagem(P1)` | **proibido** (era obrigatório) |

- Pai sem Roteiro continua recusado (`PaiSemRoteiro`). Redirecionar de onde o filho já está no primeiro
  passo do pai é recusado (`RedirecionamentoSemEfeito`, código novo).
- `DestinoForaDoRoteiroDoPai` sai do catálogo: não há mais escolha a validar.
- `DestinoIndevido` passa a cobrir `destinoSetorId` mandado em qualquer caso — ele deixa de existir no
  corpo da entrega.

  > **Decidido no plano (desvio D2, 2026-09-28).** O campo **fica no contrato só para ser recusado**:
  > `ItemDaEntregaDto.DestinoSetorId` continua existindo, anulável, e um valor não nulo dá 400
  > `DestinoIndevido`. O motivo é que retirá-lo do DTO faria o `System.Text.Json` **ignorar em
  > silêncio** um cliente antigo que ainda o mandasse — a entrega seguiria com o destino calculado e
  > ninguém saberia que o campo foi descartado —, e mantê-lo para recusar falha alto. O
  > comportamento observável para quem não manda o campo é o mesmo.

### 4.4 Editar o Roteiro do nó — emenda a §4.6 da Fase 3

Sem regra nova. O primeiro passo do pai vira alcançado no primeiro Iniciar dele (o `Inicio` grava a
primeira `Ordem` como `DestinoOrdem`, e a §4.6 da Fase 3 trava todo passo que aparece no livro), e daí não se edita mais. Antes disso, editável: filhos já entregues no Setor antigo
aparecem na fila dele com **"Levar ao Setor P1"** (redirecionamento) e sem Iniciar.

### 4.5 Pausar e retomar (PCP, Gestão)

- **Pausar Pedido P**, com motivo opcional (até 200 caracteres): exige P fora de `Concluido`/`Cancelado`
  (`PedidoFechado`) e sem pausa aberta (`PedidoJaPausado`). Grava uma linha em `dbo.PedidoPausa`.
- **Retomar P:** exige pausa aberta (`PedidoNaoPausado`). Preenche `RetomadoEm`/`RetomadoPorUsuarioId`
  daquela linha.
- **Concorrência:** pausar e retomar travam a linha do Pedido com `UPDLOCK`, a mesma que o Iniciar já
  trava (emenda de 2026-09-26 à §8.1 da Fase 3), e o Iniciar lê a pausa **dentro** da sua transação. Um
  Iniciar e um Pausar simultâneos se serializam: ou o Iniciar grava antes e a pausa vem depois, ou a pausa
  grava antes e o Iniciar é recusado.
- **Pedido que fecha com pausa aberta:** nada a fazer — `PedidoFechado` já recusa tudo, e o intervalo
  fica aberto no registro. Como a Fase 6 trata esse intervalo é dela.

### 4.6 Perfis — emenda a §4.8 da Fase 3 (sempre com `Administrador`)

| Ação | Perfis |
|---|---|
| Iniciar (inclusive nó com filhos) | Operador |
| ~~Montar~~ | — (deixa de ser ação) |
| Pausar e retomar Pedido | **PCP, Gestão** |
| Editar atividade do Setor | quem já edita Setor (sem mudança) |

## 5. API

### 5.1 Escrita

| Rota | Mudança |
|---|---|
| `POST /estrutura/{id}/inicios` | aceita nó com filhos; a resposta traz o movimento de Início, com `montagemId` preenchido quando houve consumo (desvio D1 do plano da Fase 3D) |
| `POST /estrutura/{id}/montagens` | **sai** |
| `POST /montagens/{id}/estorno` | fica; passa a estornar também o Início do pai |
| `POST /entregas` | `destinoSetorId` deixa de ser aceito (seção 4.3) |
| `POST /pedidos/{id}/pausas` | **nova** — `{ motivo? }` → 201, a pausa. PCP, Gestão |
| `POST /pedidos/{id}/retomada` | **nova** — sem corpo → 200, a pausa fechada. PCP, Gestão |
| `POST /setores`, `PUT /setores/{id}` | ganham `atividade?` |

> **Desvio D1 do plano (2026-09-28).** A redação original desta linha dizia que a resposta do Iniciar
> traz "o movimento e, se houve consumo, a montagem com as baixas". O implementado devolve **só o
> movimento de Início** (`MovimentacaoDto`), com `montagemId` preenchido quando houve consumo — a
> montagem e as baixas estão no livro do nó (`GET /estrutura/{id}/movimentacoes`), onde já eram lidas.
> O motivo: o front não usa o corpo da resposta, e um envelope novo mudaria o contrato das folhas —
> o mesmo `POST` serve ao nó sem filhos — sem ganho.

### 5.2 Leitura

- `GET /setores/{id}/fila`:
  - toda linha de "Em trabalho", "Aguardando coleta" e "Sobra" (último passo) ganha `estornaveis`:
    `[{ tipo: 'Inicio' | 'Termino' | 'Montagem', id, quantidade, usuarioId, usuarioNome, dataHora }]`
    (correção do fechamento da fase, 2026-09-29),
    **filtrado no servidor por quem pergunta** — o autor vê os seus; PCP e Administrador veem todos.
    Um registro entra quando não foi estornado e a quantidade dele ainda cabe no saldo da posição.
  - `NoResumoDto` ganha `pausa: { desde, porUsuarioNome, motivo } | null` (correção do fechamento da fase, 2026-09-29);
  - o corpo ganha `setorAtividade`, ao lado de `setorId`/`setorNome`;
  - "A iniciar aqui" deixa de listar nós com filhos (seção 7).

  > **Dois desvios do plano da Fase 3D, fora da lista D1–D6.** Ambos foram decididos no "Contrato JSON
  > novo" do plano (2026-09-28), onde o contrato traz os formatos abaixo, e implementados depois
  > (`EstornavelDto` na Task 5; `PausaResumoDto` na Task 7). Vale o implementado:
  >
  > - **`estornaveis[].tipo` é `Inicio | Termino | Montagem`**, não `'movimentacao' | 'montagem'`. Os
  >   dois primeiros são o tipo do próprio movimento e se estornam por
  >   `POST /movimentacoes/{id}/estorno`; `Montagem` é o início de um pai, que consumiu os filhos, e se
  >   estorna por `POST /montagens/{id}/estorno`. O motivo: `Inicio` e `Termino` são o `Tipo` que o
  >   próprio livro já grava (o servidor copia `Tipo` do movimento), e só a `Montagem` muda a rota de
  >   estorno; o par `'movimentacao' | 'montagem'` não dizia se o registro é um início ou um término.
  > - **`NoResumoDto.pausa` é `{ desde, porUsuarioNome, motivo } | null`**, não `{ motivo }`. O motivo:
  >   é o mesmo `PausaResumoDto` que `GET /pedidos` e `GET /pedidos/{id}` já devolviam nesta seção,
  >   então a fila e as tarefas reaproveitam o registro em vez de manter uma segunda forma menor.
- `GET /tarefas`: o item de montagem perde `sugestaoSetorId`/`setoresPossiveis` e traz o destino
  calculado, como os demais; ganha a `pausa` no nó.
- `GET /pedidos`, `GET /pedidos/{id}`: ganham `pausa: { desde, porUsuarioNome, motivo } | null`.
- `GET /setores`: cada Setor ganha `atividade`. (Não existe `GET /setores/{id}` hoje; a fila traz a
  atividade no próprio corpo.)

## 6. Telas

### 6.1 Fila do Setor — emenda a §6.1 da Fase 3

| Seção | Conteúdo | Ação |
|---|---|---|
| A iniciar aqui | nós **sem filhos** com primeiro passo aqui e saldo `AIniciar`; no fim, o grupo **"Pausados"** | **Iniciar {atividade}**; nenhuma nos Pausados |
| Em trabalho | como hoje | **Terminar {atividade}**; **Estornar** (seção 2.4) |
| Aguardando coleta | como hoje | **Estornar** |
| Aguardando montagem | agrupado por pai: filhos presentes, "dá para iniciar N; falta X de Y" | **Iniciar {atividade}** do pai quando este Setor é o primeiro passo dele; senão, **Levar ao Setor P1** (Movimentador) |
| Sobra | como hoje | **Estornar**, na sobra do último passo |

- `{atividade}` é a do Setor da fila; sem atividade, os botões são "Iniciar" e "Terminar". A mensagem de
  confirmação e o campo de quantidade usam o mesmo texto.
- A pílula **"Pausado"** aparece em toda linha de Pedido pausado, em qualquer seção; o motivo, quando há,
  vai ao lado. Tom **neutro** — pausado não é erro nem perda, e o vermelho `negativo` fica reservado
  (seção "Interface" do `CLAUDE.md`).
- O Estornar reaproveita o padrão do `HistoricoDoNo`: trava de duplo toque, recarga depois de 409,
  confirmação com botão `primario` (estorno é correção, não destruição). Depois de estornar, a fila
  recarrega e o contador de Tarefas reconta.

### 6.2 Tarefas — emenda a §6.2 da Fase 3

O `<select>` do Setor de montagem sai: o destino vem calculado. A pílula "Pausado" aparece nos itens de
Pedido pausado; a entrega continua permitida.

### 6.3 Pedido

- **Detalhe do Pedido:** para PCP, Gestão e Administrador, botão **"Pausar"** — abre um campo de motivo
  opcional — e, com pausa aberta, **"Retomar"**. Para todos, com pausa aberta, um aviso: "Pausado desde
  {data} por {nome}" e o motivo.
- **Lista de Pedidos:** a pílula "Pausado" ao lado do status.

### 6.4 Setor

O cadastro de Setor ganha o campo opcional **Atividade**, com texto de ajuda mostrando o efeito ("Os
botões da fila ficam 'Iniciar montagem' e 'Terminar montagem'").

### 6.5 Permissões

`web/src/auth/permissoes.ts` ganha a chave `pausa` (PCP, Gestão, Administrador), espelhando os
`[Authorize]` das duas rotas novas. O 403 continua sendo a fronteira.

## 7. Cálculo — emenda a §7 da Fase 3

- **"A iniciar aqui"** exclui nós com filhos: o início deles acontece no card de montagem.
- **"Dá para iniciar N"** (era "dá para montar", §7.6): a mesma fórmula, `min(Quantidade(P) −
  totalMontado(P), min_c ⌊AguardandoMontagem(S, c) ÷ QuantidadePorPai(c)⌋)`, e a mesma calculadora serve
  à leitura e à validação do Iniciar do pai.
- **Destino da coleta** (§7.3, item 3): o Setor do primeiro passo do pai. A sugestão deixa de existir.
- **`estornaveis`:** para a posição da linha, os registros do tipo da tabela da seção 2.4, não
  estornados, com `Quantidade ≤ saldo da posição`, filtrados por autor quando quem pergunta não é PCP nem
  Administrador. Ordem: do mais recente ao mais antigo.
- **Pausados:** um nó está no grupo "Pausados" quando o Pedido dele tem pausa aberta.

## 8. Erros — emenda a §8.2 da Fase 3

| Status | Código | Situação |
|---|---|---|
| 409 | `PedidoPausado` | **novo** — "O Pedido {número} está pausado." no Iniciar |
| 409 | `PedidoJaPausado` | **novo** — pausar Pedido já pausado |
| 409 | `PedidoNaoPausado` | **novo** — retomar Pedido sem pausa aberta |
| 409 | `RedirecionamentoSemEfeito` | **novo** — redirecionar o que já está no primeiro passo do pai |
| 409 | `FilhosInsuficientes` | passa a vir do Iniciar |
| 409 | `EstornoImpossivel` | ganha o caso "Início de pai avulso" |
| — | `SemFilhos`, `MontagemAcimaDoQueFalta`, `DestinoForaDoRoteiroDoPai` | **saem** |

Cada código novo ganha tradução em `mensagemDeErro`.

## 9. Testes

Mesma estrutura da §9 da Fase 3. O que esta fase acrescenta, em especial:

- **Application:** cada código novo, aceitando e recusando, afirmando o código; o Iniciar do pai grava
  os três registros (montagem, baixas, Início com `MontagemId`) ou nenhum; estorno da montagem devolve
  o pai a `AIniciar` e os filhos a `AguardandoMontagem`; estorno recusado com o pai já terminado;
  estorno avulso do Início do pai recusado; `estornaveis` por autor e por perfil, com o corte por saldo;
  "A iniciar aqui" sem nós com filhos; a pausa recusando o Iniciar e deixando Terminar, Entregar e
  Estornar passarem; destino calculado.
- **Propriedade de conservação** (a da §9.1 da Fase 3), refeita com o Iniciar unificado e acrescida da
  invariante nova: **para todo nó com filhos, o que saiu de `AIniciar` é igual ao total montado**.
- **Infrastructure:** as constraints e os dois índices únicos novos (`UX_PedidoPausa_UmaAbertaPorPedido`,
  `UX_Movimentacao_UmInicioPorMontagem`), cada um com o caso que o viola.
- **Api:** as rotas novas, a saída de `POST /estrutura/{id}/montagens` e os `[Authorize]` — Gestão
  pausando e **não** iniciando.
- **Front:** rótulos com e sem atividade; lista curta e atalho de um registro só; grupo "Pausados" sem
  Iniciar; pílula nas demais seções; botões de pausa só para quem pode; os três estados nas telas
  tocadas.
- **Verificação manual:** no celular, como na §9.5 da Fase 3 — produto é relatório, com dispensa de
  review escrita antes, na regra do `CLAUDE.md`.

## 10. Consequências para outros documentos e fases

### 10.1 3B encolhe

A trava da regra 24 (saída do nó limitada ao total montado) passa a valer, por construção, para todo
nó que já tem filhos quando entra em produção. A 3B fica com: `Setor.UtilizaKit`, o **conjunto
completo** na entrada (regra 25), a tarefa **Kit pronto** (regra 23) e o caso do nó que ganha filho
depois de iniciado (ressalva da seção 2.1). O `06` é atualizado junto.

### 10.2 Fase 5

O nó **pronto** do Retrabalho (regra 27) nasce aguardando coleta com o total montado igual à quantidade.
Continua compatível: um nó pronto não percorre Roteiro, então não passa pelo Iniciar. A spec da Fase 5
precisa confirmar como o pronto grava o "saiu de `AIniciar` = total montado" da invariante da seção 9.

### 10.3 Fase 6

Os intervalos de `dbo.PedidoPausa` existem para o tempo por Pedido poder descontar a pausa. Descontar ou
não é decisão dela.

### 10.4 Documentos

- `specs/02-modelo-de-dados.sql` e `db/`: seção 3; o arquivo novo `db/alter-fase-3d.sql`.
- `specs/01-dominio-e-regras-de-negocio.md`: regra 24 (montar deixa de ser ação; iniciar um nó com
  filhos consome os filhos; a trava vale, por construção, para todo nó que já tem filhos ao entrar em
  produção); regra 29 (o destino do Item é o
  primeiro passo do pai, sem escolha); regra 28 (o início de um nó com filhos é o consumo dos filhos);
  **regra 31, nova** — a pausa de Pedido.
- `specs/04-fluxos-de-usuario.md`: o fluxo do operador perde "montar"; o PCP e a Gestão ganham pausar.
- `specs/05-api-endpoints.md`: seção 5.
- `specs/06-roadmap-mvp.md`: a Fase 3D, e a 3B encolhida.
- **Spec da Fase 3:** uma nota no topo apontando esta spec como emenda das §2.4, §4.1, §4.3, §4.4, §4.5,
  §4.8, §6.1, §6.2, §7.3, §7.6 e §8.2 — sem reescrever aquelas seções, que são o registro da decisão da
  época.
- `CLAUDE.md`, seção "Invariantes de negócio": o bullet do Kit diz que "montar é registro de todo nó com
  filhos"; passa a dizer que o início de um nó com filhos consome os filhos.

## 11. Não medido

- **O custo da atividade do Setor** foi estimado no brainstorm como "uma task pequena no backend e outra
  no front", sem medir o código do cadastro de Setor. O plano mede.
- **Quantos testes da Fase 3 dependem de `POST /estrutura/{id}/montagens`** e do `<select>` de destino
  não foi contado. O plano conta antes de fixar a baseline.
