# Alterações de domínio: lote divisível + Agrupamento

**Data:** 2026-07-27
**Tipo:** changeset de domínio sobre specs existentes (não é feature nova)
**Motivação:** aproveitar a janela em que só a autenticação está construída (código = zero
impacto) para corrigir duas regras de domínio antes que Pedido/Agrupamento/EstruturaItem
sejam implementados e cada mudança vire refação de várias camadas.

## Contexto

Três mudanças de domínio, duas delas revertendo regras hoje marcadas como "decididas e
fechadas" no CLAUDE.md:

- **#1 — Lote indivisível** → lote passa a ser **divisível por quantidades livres** (com
  expedição parcial e Relatório Dimensional opcional/por quantidade).
- **#2 — Kit obrigatório** → **Agrupamento** com tipo (`Kit` | `Avulso`).
- **#3 — Perda de peças** (novo) → baixa de quantidade em produção como bucket terminal
  "perdido", com Pedido de Retrabalho separado para reposição.

Confirmado por grep: nenhum arquivo em `src/` referencia `Kit`, `EstruturaItem`,
`EstruturaSetorHistorico` ou `RelatorioDimensional`. O backend é só auth (Fase 0). Logo o
impacto é **puramente specs + `02-modelo-de-dados.sql`**; o passo "atualizar mapeamento EF"
é no-op por ora — as entidades nascerão já corretas na fase delas.

Continua **Database First**: editar `02-modelo-de-dados.sql` à mão e aplicar por script SQL.
Nada de `Add-Migration` (decisão reconfirmada nesta conversa).

---

## Mudança #1 — Lote divisível, expedição parcial, dimensional opcional

### Modelo mental (decidido)

- **Quantidades livres, sem identidade.** Não existem "sub-lotes" rastreáveis. A Peça tem
  uma quantidade total; num instante essa quantidade está distribuída entre setores e o
  bucket "expedido". Não há etiqueta/RFID, então não se rastreia "aquele pedaço" — só
  *quantos* estão *onde*.
- **Invariante nova:** conservação de quantidade — soma das unidades em todos os setores +
  expedido = quantidade total da Peça. (Substitui o invariante "uma passagem aberta por
  item". A Mudança #3 estende isso somando o bucket "perdido".)
- **Expedição parcial:** o cliente aceita uma parte vital agora; o resto vai depois. Cada
  remessa é registrada com sua quantidade e data.
- **Relatório Dimensional opcional:** o cliente exige em Peças específicas (primeira
  manufatura, primeiro trabalho após reprovação no cliente). Sabe-se no cadastro do Pedido.
  Flag **por Peça**. Quando existe, é **um relatório por Peça, acumulativo** (avaliações vão
  chegando conforme as remessas).
- **Reprovação e retrabalho por quantidade:** o cliente informa "das N recebidas, X ok, Y
  reprovadas"; o retrabalho (quando aberto) é para a **quantidade** reprovada, não para "a
  Peça inteira".

### Schema (`02-modelo-de-dados.sql`)

1. **Dropar** o índice filtrado único
   `UX_EstruturaSetorHistorico_UmaAbertaPorItem` — é ele que impõe indivisibilidade.
   Remover também o bloco de comentário no topo do arquivo sobre a flag `-I` /
   `SET QUOTED_IDENTIFIER ON`, que só existia por causa desse índice filtrado. Conferir se
   nenhum outro índice filtrado permanece; se não sobrar nenhum, a exigência do `-I` cai.
2. `EstruturaItem.Quantidade` — coluna permanece; ajustar o comentário
   `-- lote agregado, indivisível` para `-- lote agregado` (remover "indivisível").
   `EstruturaSetorHistorico.QuantidadeMovimentada` **já existe** — o schema já antecipava
   movimentação por quantidade; nada a criar ali.
3. Nova coluna em `EstruturaItem`:
   `RequerRelatorioDimensional BIT NOT NULL CONSTRAINT DF_EstruturaItem_RequerRelatorio DEFAULT (0)`.
   Semanticamente vale só para Peça (nível topo); não há CHECK amarrando a `NivelHierarquico`
   no MVP (Itens simplesmente ficam com 0). Documentar isso em `01`.
4. Nova tabela **`Expedicao`** (remessa) — o bucket terminal que hoje não existe:
   ```sql
   CREATE TABLE dbo.Expedicao (
       Id              INT IDENTITY(1,1)  NOT NULL,
       EstruturaItemId INT                 NOT NULL, -- Peça expedida (NivelHierarquico = 'Peca')
       Quantidade      DECIMAL(18,4)       NOT NULL,
       DataExpedicao   DATETIME2           NOT NULL CONSTRAINT DF_Expedicao_Data DEFAULT (SYSUTCDATETIME()),
       Responsavel     NVARCHAR(100)       NOT NULL,
       CONSTRAINT PK_Expedicao PRIMARY KEY CLUSTERED (Id),
       CONSTRAINT FK_Expedicao_EstruturaItem
           FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
       CONSTRAINT CK_Expedicao_QuantidadePositiva CHECK (Quantidade > 0)
   );
   -- + IX_Expedicao_EstruturaItem ON (EstruturaItemId)
   ```
   Expedição parcial = várias linhas cuja soma ≤ quantidade total da Peça. A regra "soma das
   remessas ≤ total" é validada na camada de aplicação (não como constraint de banco).
5. **`RelatorioDimensional` remodelado — header + detalhe (5a):**
   - `RelatorioDimensional` vira **header, 1 por Peça**:
     - Manter `Id`, `EstruturaItemId`. Adicionar `UNIQUE (EstruturaItemId)` (um por Peça).
     - **Remover** do header os campos que agora são por-avaliação: `DentroTolerancia`,
       `Medidas`, `InformadoPor`, `DataAvaliacao`, `PedidoRetrabalhoId`. (O header passa a
       ser praticamente só a âncora por Peça; pode ganhar `CriadoEm`.)
   - Nova tabela **`RelatorioDimensionalAvaliacao`** — N por remessa avaliada:
     ```sql
     CREATE TABLE dbo.RelatorioDimensionalAvaliacao (
         Id                     INT IDENTITY(1,1) NOT NULL,
         RelatorioDimensionalId INT               NOT NULL,
         QuantidadeAvaliada     DECIMAL(18,4)      NOT NULL,
         QuantidadeAprovada     DECIMAL(18,4)      NOT NULL,
         QuantidadeReprovada    DECIMAL(18,4)      NOT NULL,
         Medidas                NVARCHAR(MAX)      NULL,
         InformadoPor           NVARCHAR(200)      NOT NULL,
         DataAvaliacao          DATETIME2          NOT NULL CONSTRAINT DF_RDA_Data DEFAULT (SYSUTCDATETIME()),
         PedidoRetrabalhoId     INT               NULL, -- retrabalho aberto por causa desta avaliação
         CONSTRAINT PK_RelatorioDimensionalAvaliacao PRIMARY KEY CLUSTERED (Id),
         CONSTRAINT FK_RDA_Relatorio
             FOREIGN KEY (RelatorioDimensionalId) REFERENCES dbo.RelatorioDimensional (Id),
         CONSTRAINT FK_RDA_PedidoRetrabalho
             FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
         CONSTRAINT CK_RDA_Quantidades
             CHECK (QuantidadeAprovada + QuantidadeReprovada = QuantidadeAvaliada
                AND QuantidadeAprovada >= 0 AND QuantidadeReprovada >= 0)
     );
     -- + IX_RDA_Relatorio ON (RelatorioDimensionalId)
     ```
   - Atualizar `IX_RelatorioDimensional_EstruturaItem` conforme os campos que sobrarem.
   - O rastro de retrabalho por quantidade passa a viver na avaliação
     (`RelatorioDimensionalAvaliacao.PedidoRetrabalhoId`), permitindo múltiplos retrabalhos
     ao longo das remessas — o que a FK única no header antigo não permitia.

### Regras de negócio (`01-dominio-e-regras-de-negocio.md`)

- **Regra 9 (indivisibilidade) — REVERTER:** o lote é **divisível**; pode estar com
  quantidades em setores diferentes ao mesmo tempo. Invariante = conservação de quantidade.
  Manter a nota de que a divisão física entre pinturas terceirizadas continua fora do sistema.
- **Regra 10 — dimensional opcional:** sai "100% das peças"; entra "opcional, exigido pelo
  cliente em Peças específicas, marcado no cadastro via flag por Peça
  (`EstruturaItem.RequerRelatorioDimensional`)". Um relatório por Peça, acumulativo.
- **Regra 11 — por quantidade:** aprovação/reprovação é por **quantidade** dentro da Peça
  (parcial dentro da própria Peça e dentro do Agrupamento), não "aprova/reprova a Peça".
- **Regra 12 — retrabalho por quantidade:** o Pedido de Retrabalho é aberto para a
  quantidade reprovada; vínculo agora é `RelatorioDimensionalAvaliacao.PedidoRetrabalhoId`.
  `MotivoRetrabalho` segue obrigatório e categorizado.
- **Regra 13 — conclusão por expedição total:** um Pedido conclui quando **toda a quantidade
  de todas as Peças de todos os Agrupamentos foi expedida** (soma das remessas = total de
  cada Peça). Deixa de depender de "avaliado", já que o dimensional é opcional.
- **Nova regra — expedição parcial:** a expedição pode ser parcial (remessas); o cliente
  aceita parte vital antes e o restante depois. Cada remessa é uma linha em `Expedicao`.
- **Glossário:** `EstruturaItem` perde "e esse lote não se divide"; `Relatório Dimensional`
  passa a "opcional, por Peça, acumulativo, avaliado por quantidade".

---

## Mudança #2 — Kit → Agrupamento com Tipo

### Decidido

- Toda Peça pertence a um **Agrupamento** (a entidade geral que hoje se chama `Kit`).
- O Agrupamento tem um **Tipo**: `Kit` (peças que vão para a solda, juntas) ou `Avulso`
  (peças que não passam por solda). A palavra "Kit" sobrevive como *tipo*.
- O Tipo é **só descritivo** no MVP: agrupa em telas/relatórios e serve de rótulo, mas **não
  impõe regra dura** (não valida "Kit obriga Solda no roteiro" nem bloqueia expedição). Se um
  Kit precisa passar por Solda, isso já vem no roteiro daquelas Peças (Solda é um Setor comum).

### Schema (`02-modelo-de-dados.sql`)

- Renomear a tabela `dbo.Kit` → `dbo.Agrupamento` e junto: `PK_Kit → PK_Agrupamento`,
  `FK_Kit_Pedido → FK_Agrupamento_Pedido`, `UQ_Kit_PedidoCodigo → UQ_Agrupamento_PedidoCodigo`.
- Adicionar coluna
  `Tipo NVARCHAR(20) NOT NULL CONSTRAINT CK_Agrupamento_Tipo CHECK (Tipo IN ('Kit','Avulso'))`.
- `EstruturaItem.KitId → AgrupamentoId`; `FK_EstruturaItem_Kit → FK_EstruturaItem_Agrupamento`;
  `IX_EstruturaItem_Kit → IX_EstruturaItem_Agrupamento`.
- Atualizar a query de exemplo no rodapé (`JOIN dbo.Kit k ... k.PedidoId` → `Agrupamento`).
- Ajustar comentários de seção ("PEDIDO / KIT" → "PEDIDO / AGRUPAMENTO", "Pedido/Kit" →
  "Pedido/Agrupamento").

### Regras de negócio (`01`)

- **Regra 1/2:** Pedido → N Agrupamentos → N Peças; todo Agrupamento tem Tipo (`Kit` = vai
  para solda / `Avulso` = não passa por solda). Tipo descritivo, não impõe roteiro.
- **Regra 13:** "último Kit" → "último Agrupamento" (redação alinhada à nova conclusão por
  expedição total).
- **Glossário:** `Kit` → `Agrupamento` (com Tipo).

---

## Mudança #3 — Perda de peças (bucket terminal) + retrabalho de reposição

### Decidido

- Peças às vezes se **perdem** durante a produção: some no armazém (`PerdaArmazem`) ou "morre"
  após um processo que deu errado (`MortaEmProcesso`). Diferente de reprovação (que é
  pós-expedição, informada pelo cliente), a perda acontece **em produção**.
- A quantidade perdida vai para um **bucket terminal "perdido"** — igual a "expedido". Sai da
  produção e não volta para a Peça original.
- Para repor, abre-se um **Pedido de Retrabalho separado** (`MotivoRetrabalho = 'Perda'`).
  **Nunca reabre a Peça original.** É ação **manual/opcional** do usuário (não dispara
  automático ao registrar a perda) — mesmo padrão da reprovação.

### Schema (`02-modelo-de-dados.sql`)

1. Nova tabela **`Perda`**:
   ```sql
   CREATE TABLE dbo.Perda (
       Id                 INT IDENTITY(1,1)  NOT NULL,
       EstruturaItemId    INT                 NOT NULL, -- Peça que sofreu a perda
       Quantidade         DECIMAL(18,4)       NOT NULL,
       MotivoPerda        NVARCHAR(20)        NOT NULL, -- PerdaArmazem | MortaEmProcesso
       Observacao         NVARCHAR(MAX)       NULL,     -- detalhe livre opcional
       SetorId            INT                 NULL,     -- onde estava quando se perdeu (opcional)
       DataPerda          DATETIME2           NOT NULL CONSTRAINT DF_Perda_Data DEFAULT (SYSUTCDATETIME()),
       Responsavel        NVARCHAR(100)       NOT NULL,
       PedidoRetrabalhoId INT                 NULL,     -- retrabalho aberto p/ repor (opcional)
       CONSTRAINT PK_Perda PRIMARY KEY CLUSTERED (Id),
       CONSTRAINT FK_Perda_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
       CONSTRAINT FK_Perda_Setor          FOREIGN KEY (SetorId)         REFERENCES dbo.Setor (Id),
       CONSTRAINT FK_Perda_PedidoRetrabalho FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
       CONSTRAINT CK_Perda_QuantidadePositiva CHECK (Quantidade > 0),
       CONSTRAINT CK_Perda_Motivo CHECK (MotivoPerda IN ('PerdaArmazem','MortaEmProcesso'))
   );
   -- + IX_Perda_EstruturaItem ON (EstruturaItemId)
   ```
   `MotivoPerda` nasce com dois valores e cresce depois via edit no CHECK (mesmo padrão
   string+CHECK do resto do schema); `Observacao` livre para detalhe.
2. `Pedido.MotivoRetrabalho` — o CHECK ganha `'Perda'`:
   `IN ('ReprovacaoDimensional','ErroInterno','SolicitacaoCliente','Perda')`.

### Regras de negócio (`01`)

- **Conservação de quantidade — atualizada:** `em setores + expedido + perdido = total da
  Peça`. "Perdido" é terminal, igual "expedido". (Estende o invariante da Mudança #1.)
- **Nova regra — perda:** registra baixa de quantidade em produção (armazém / morta em
  processo); para repor, abre-se um Pedido Retrabalho separado (`MotivoRetrabalho='Perda'`),
  manual/opcional — nunca reabre a Peça.
- **Regra 13 — conclusão:** Peça fecha quando nada mais está em produção (tudo virou expedido
  **ou** perdido); Pedido conclui quando todas as Peças fecham.

---

## Raio de impacto

### Código
**Zero.** Nenhum arquivo em `src/` referencia as entidades afetadas. "Atualizar mapeamento
EF" é no-op por ora.

### Specs a editar
| Arquivo | O que muda |
|---|---|
| `02-modelo-de-dados.sql` | Núcleo do schema: drop do índice, `Expedicao`, remodelagem do dimensional, flag por Peça, rename `Kit→Agrupamento` + `Tipo`, tabela `Perda` + `'Perda'` no CHECK de `MotivoRetrabalho`. |
| `01-dominio-e-regras-de-negocio.md` | Regras 9-13, glossário `Kit`/`EstruturaItem`/`Relatório Dimensional`, nova regra de expedição parcial e nova regra de perda; conservação de quantidade com bucket "perdido". |
| `00-visao-geral.md` | Nomenclatura Kit→Agrupamento; menção a lote/expedição se houver. |
| `03-arquitetura-tecnica.md` | Menções a Kit/lote indivisível, se houver. |
| `04-fluxos-de-usuario.md` | Fluxo de Expedição (parcial/remessas), de Qualidade (dimensional opcional, por quantidade) e de perda/baixa; nomenclatura. |
| `05-api-endpoints.md` | Endpoints de Kit→Agrupamento; endpoints de expedição (remessa), dimensional por quantidade e registro de perda. |
| `06-roadmap-mvp.md` | Nomenclatura; conferir se alguma fase descreve indivisibilidade/expedição total. |
| `CLAUDE.md` | Invariantes (remover "lote indivisível / índice filtrado"; ajustar conclusão "último Kit→Agrupamento"); convenções (`Kit`→`Agrupamento`); decisões descartadas (remover "lote indivisível"; **manter** "sem serial"). |

### O que NÃO muda (reafirmado)
- Rastreamento por **lote agregado, nunca serial** — quantidade livre não é serial.
- Peça e Item continuam numa única tabela recursiva (`EstruturaItem`).
- Database First; sem `Add-Migration`.
- Reprovação **não** gera retrabalho automático — segue ação opcional da Qualidade. O mesmo
  vale para perda: registrar a perda **não** abre retrabalho sozinho.

## Pontos em aberto
Nenhum. As três mudanças estão fechadas e prontas para virar plano de implementação
(editar `.sql` + specs, à mão, Database First).
