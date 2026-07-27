# Alterações de domínio (lote divisível + Agrupamento + Perda) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplicar três mudanças de domínio nas specs e no schema `.sql`, aproveitando que só a autenticação está construída (código = zero impacto).

**Architecture:** Puramente documentação + schema Database First. Edita-se `specs/02-modelo-de-dados.sql` (fonte da verdade do schema) à mão e valida-se aplicando o `.sql` num banco descartável de SQL Server; depois propagam-se as regras/nomenclatura para os demais docs. Nenhum código C# muda (nenhum arquivo em `src/` referencia as entidades afetadas).

**Tech Stack:** SQL Server (T-SQL), Markdown. Verificação via `docker compose` + `sqlcmd`.

## Global Constraints

- **Database First — nunca `Add-Migration`/`EnsureCreated`.** O schema nasce em `specs/02-modelo-de-dados.sql`; o EF mapeia a partir dele. (CLAUDE.md)
- **Nomes de domínio em português espelhando o DDL** (`Pedido`, `Agrupamento`, `EstruturaItem`, `Expedicao`, `Perda`, `RelatorioDimensional`, `RelatorioDimensionalAvaliacao`). Nomes técnicos em inglês. (CLAUDE.md)
- **Rastreamento por lote agregado, nunca serial** — quantidade livre **não** é serial. Isso NÃO muda e deve continuar afirmado.
- **Peça e Item numa única tabela recursiva `EstruturaItem`** — não criar tabelas separadas.
- **Reprovação e perda NÃO abrem retrabalho automático** — é sempre ação manual/opcional do usuário.
- **SQL Server local:** `localhost,1433`, `sa` / `Your_strong_Pass123` (só dev local).
- Fonte de verdade das decisões desta entrega: `docs/superpowers/specs/2026-07-27-alteracoes-dominio-lote-divisivel-e-agrupamento-design.md`.

---

## File Structure

| Arquivo | Responsabilidade | Task |
|---|---|---|
| `specs/02-modelo-de-dados.sql` | Schema (fonte da verdade). Núcleo de todas as três mudanças. | 1 |
| `specs/01-dominio-e-regras-de-negocio.md` | Glossário + regras numeradas. | 2 |
| `CLAUDE.md` | Invariantes, convenções, decisões descartadas, nota de comandos. | 3 |
| `specs/00-visao-geral.md`, `specs/03-arquitetura-tecnica.md`, `specs/06-roadmap-mvp.md` | Nomenclatura + ajustes leves. | 4 |
| `specs/04-fluxos-de-usuario.md`, `specs/05-api-endpoints.md` | Fluxos e endpoints (expedição parcial, dimensional por quantidade, perda). | 5 |

Ordem obrigatória: **Task 1 primeiro** (schema é a fonte da verdade; as demais citam nomes definidos nele). Tasks 2-5 podem ser revisadas independentemente, mas execute na ordem para manter a nomenclatura consistente.

Nota de verificação: como isto é doc/schema (não código executável em `src/`), o "teste" da Task 1 é **aplicar o `.sql` num banco descartável e ver zero erros**; nas Tasks 2-5 é **grep** contra termos proibidos + revisão de consistência. Não há suíte de testes C# afetada.

---

### Task 1: Schema — `specs/02-modelo-de-dados.sql`

**Files:**
- Modify: `specs/02-modelo-de-dados.sql`

**Interfaces:**
- Produces (nomes que as Tasks 2-5 citam):
  - Tabela `dbo.Agrupamento (Id, PedidoId, Codigo, Quantidade, Tipo, DataConclusao)`; `Tipo IN ('Kit','Avulso')`.
  - `dbo.EstruturaItem.AgrupamentoId` (antes `KitId`); nova coluna `RequerRelatorioDimensional BIT`.
  - Tabela `dbo.Expedicao (Id, EstruturaItemId, Quantidade, DataExpedicao, Responsavel)`.
  - Tabela `dbo.RelatorioDimensional (Id, EstruturaItemId, CriadoEm)` com `UNIQUE(EstruturaItemId)`.
  - Tabela `dbo.RelatorioDimensionalAvaliacao (Id, RelatorioDimensionalId, QuantidadeAvaliada, QuantidadeAprovada, QuantidadeReprovada, Medidas, InformadoPor, DataAvaliacao, PedidoRetrabalhoId)`.
  - Tabela `dbo.Perda (Id, EstruturaItemId, Quantidade, MotivoPerda, Observacao, SetorId, DataPerda, Responsavel, PedidoRetrabalhoId)`; `MotivoPerda IN ('PerdaArmazem','MortaEmProcesso')`.
  - `dbo.Pedido.MotivoRetrabalho` CHECK inclui `'Perda'`.

- [ ] **Step 1: Remover o bloco de comentário sobre `-I` / QUOTED_IDENTIFIER no topo**

O índice filtrado que exigia `-I` será removido (Step 6). Apague o parágrafo correspondente.

Old:
```
   Aplicar via sqlcmd exige a flag -I (SET QUOTED_IDENTIFIER ON), por causa
   do índice único filtrado UX_EstruturaSetorHistorico_UmaAbertaPorItem
   (índices filtrados exigem QUOTED_IDENTIFIER ON). Sem -I, o CREATE INDEX
   falha com o erro 1934.
   ===================================================================== */
```
New:
```
   Observação: o schema não usa mais índices filtrados; a flag -I do sqlcmd
   deixou de ser obrigatória (é inofensiva se mantida).
   ===================================================================== */
```

- [ ] **Step 2: Adicionar `'Perda'` ao CHECK de `Pedido.MotivoRetrabalho`**

Old:
```sql
    CONSTRAINT CK_Pedido_MotivoRetrabalho
        CHECK (MotivoRetrabalho IS NULL
            OR MotivoRetrabalho IN ('ReprovacaoDimensional', 'ErroInterno', 'SolicitacaoCliente')),
```
New:
```sql
    CONSTRAINT CK_Pedido_MotivoRetrabalho
        CHECK (MotivoRetrabalho IS NULL
            OR MotivoRetrabalho IN ('ReprovacaoDimensional', 'ErroInterno', 'SolicitacaoCliente', 'Perda')),
```

- [ ] **Step 3: Renomear `Kit` → `Agrupamento` e adicionar `Tipo`**

Ajuste o comentário de seção `PEDIDO / KIT` → `PEDIDO / AGRUPAMENTO`. Depois substitua a tabela:

Old:
```sql
CREATE TABLE dbo.Kit (
    Id              INT IDENTITY(1,1)  NOT NULL,
    PedidoId        INT                 NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Quantidade      DECIMAL(18,4)       NOT NULL,
    DataConclusao   DATETIME2           NULL, -- preenchida na expedição/aprovação do kit
    CONSTRAINT PK_Kit PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Kit_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT UQ_Kit_PedidoCodigo UNIQUE (PedidoId, Codigo)
);
```
New:
```sql
CREATE TABLE dbo.Agrupamento (
    Id              INT IDENTITY(1,1)  NOT NULL,
    PedidoId        INT                 NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Quantidade      DECIMAL(18,4)       NOT NULL,
    Tipo            NVARCHAR(20)        NOT NULL, -- Kit (vai para solda) | Avulso (não passa por solda); descritivo
    DataConclusao   DATETIME2           NULL, -- preenchida quando todas as Peças do agrupamento fecham
    CONSTRAINT PK_Agrupamento PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Agrupamento_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT UQ_Agrupamento_PedidoCodigo UNIQUE (PedidoId, Codigo),
    CONSTRAINT CK_Agrupamento_Tipo CHECK (Tipo IN ('Kit', 'Avulso'))
);
```

- [ ] **Step 4: `EstruturaItem` — `KitId`→`AgrupamentoId`, nova flag, ajuste de comentários**

Ajuste o comentário de seção `árvore recursiva efetivamente usada no Pedido/Kit` → `...no Pedido/Agrupamento`. Depois substitua a tabela:

Old:
```sql
CREATE TABLE dbo.EstruturaItem (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    KitId               INT                 NOT NULL,
    ComponenteId        INT                 NULL,       -- nullable: item 100% ad-hoc, sem base no catálogo
    EstruturaPaiId      INT                 NULL,       -- self-FK: recursão Peça -> Item -> ... -> Item
    NivelHierarquico    NVARCHAR(10)        NOT NULL,   -- Peca | Item (denormalizado p/ consulta rápida)
    Quantidade          DECIMAL(18,4)       NOT NULL,   -- lote agregado, indivisível
    CONSTRAINT PK_EstruturaItem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaItem_Kit FOREIGN KEY (KitId) REFERENCES dbo.Kit (Id),
    CONSTRAINT FK_EstruturaItem_Componente FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_EstruturaItem_Pai FOREIGN KEY (EstruturaPaiId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT CK_EstruturaItem_NaoAutoReferencia CHECK (EstruturaPaiId <> Id),
    CONSTRAINT CK_EstruturaItem_NivelHierarquico CHECK (NivelHierarquico IN ('Peca', 'Item')),
    -- Peça = topo da árvore dentro do Kit (sem pai); Item = tem pai
    CONSTRAINT CK_EstruturaItem_PecaSemPai
        CHECK ((NivelHierarquico = 'Peca' AND EstruturaPaiId IS NULL)
            OR (NivelHierarquico = 'Item' AND EstruturaPaiId IS NOT NULL))
);
```
New:
```sql
CREATE TABLE dbo.EstruturaItem (
    Id                          INT IDENTITY(1,1)  NOT NULL,
    AgrupamentoId               INT                 NOT NULL,
    ComponenteId                INT                 NULL,       -- nullable: item 100% ad-hoc, sem base no catálogo
    EstruturaPaiId              INT                 NULL,       -- self-FK: recursão Peça -> Item -> ... -> Item
    NivelHierarquico            NVARCHAR(10)        NOT NULL,   -- Peca | Item (denormalizado p/ consulta rápida)
    Quantidade                  DECIMAL(18,4)       NOT NULL,   -- lote agregado (divisível por quantidades livres)
    RequerRelatorioDimensional  BIT                 NOT NULL
        CONSTRAINT DF_EstruturaItem_RequerRelatorio DEFAULT (0), -- vale p/ Peça (topo); cliente exige no cadastro
    CONSTRAINT PK_EstruturaItem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaItem_Agrupamento FOREIGN KEY (AgrupamentoId) REFERENCES dbo.Agrupamento (Id),
    CONSTRAINT FK_EstruturaItem_Componente FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_EstruturaItem_Pai FOREIGN KEY (EstruturaPaiId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT CK_EstruturaItem_NaoAutoReferencia CHECK (EstruturaPaiId <> Id),
    CONSTRAINT CK_EstruturaItem_NivelHierarquico CHECK (NivelHierarquico IN ('Peca', 'Item')),
    -- Peça = topo da árvore dentro do Agrupamento (sem pai); Item = tem pai
    CONSTRAINT CK_EstruturaItem_PecaSemPai
        CHECK ((NivelHierarquico = 'Peca' AND EstruturaPaiId IS NULL)
            OR (NivelHierarquico = 'Item' AND EstruturaPaiId IS NOT NULL))
);
```

- [ ] **Step 5: Ajustar o comentário de `EstruturaSetorHistorico`**

`QuantidadeMovimentada` já existe — não criar. Só reforce no comentário que agora podem coexistir várias passagens abertas. Sob a linha `QuantidadeMovimentada DECIMAL(18,4)     NOT NULL,` não há mudança de coluna; ajuste o comentário logo abaixo de `DataSaida`:

Old:
```sql
    DataSaida           DATETIME2           NULL,       -- NULL = ainda está nesse setor
```
New:
```sql
    DataSaida           DATETIME2           NULL,       -- NULL = ainda está nesse setor (várias passagens abertas por item são permitidas: lote divisível)
```

- [ ] **Step 6: Dropar o índice filtrado único (indivisibilidade)**

Remova por completo o comentário + o `CREATE UNIQUE INDEX`:

Old:
```sql
-- Lote é indivisível: garante no máx. 1 passagem "aberta" (sem saída) por item
CREATE UNIQUE INDEX UX_EstruturaSetorHistorico_UmaAbertaPorItem
    ON dbo.EstruturaSetorHistorico (EstruturaItemId)
    WHERE DataSaida IS NULL;
GO
```
New: *(bloco removido — nada no lugar)*

- [ ] **Step 7: Adicionar a tabela `Expedicao` (bucket terminal de remessas)**

Insira logo após a tabela `MaterialSeparacao` (dentro da seção EXECUÇÃO / RASTREAMENTO), antes da seção DIMENSIONAL:
```sql
-- Remessa de expedição. Expedição parcial = várias linhas cuja soma <= Quantidade da Peça
-- (a validação "soma <= total" é feita na camada de aplicação, não como constraint).
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
GO
```

- [ ] **Step 8: Remodelar `RelatorioDimensional` (header) + criar `RelatorioDimensionalAvaliacao` (detalhe)**

Substitua toda a tabela `RelatorioDimensional`:

Old:
```sql
CREATE TABLE dbo.RelatorioDimensional (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId         INT                 NOT NULL, -- peça avaliada (NivelHierarquico = 'Peca')
    DentroTolerancia        BIT                 NOT NULL,
    Medidas                 NVARCHAR(MAX)       NULL,      -- detalhes/medições informadas
    InformadoPor            NVARCHAR(200)       NOT NULL,  -- contato do cliente que informou
    DataAvaliacao           DATETIME2           NOT NULL CONSTRAINT DF_RelatorioDimensional_Data DEFAULT (SYSUTCDATETIME()),
    PedidoRetrabalhoId      INT                 NULL,      -- retrabalho aberto por causa desta reprovação
    CONSTRAINT PK_RelatorioDimensional PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RelatorioDimensional_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_RelatorioDimensional_PedidoRetrabalho
        FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id)
    -- Confirmado com o negócio: reprovação pode ficar registrada sem retrabalho
    -- imediato. PedidoRetrabalhoId fica NULL até que (e se) um retrabalho seja
    -- aberto depois, via update nesta linha.
);
GO
```
New:
```sql
-- Header: no máx. um Relatório Dimensional por Peça (opcional; só quando o cliente exige,
-- flag EstruturaItem.RequerRelatorioDimensional). Acumulativo via RelatorioDimensionalAvaliacao.
CREATE TABLE dbo.RelatorioDimensional (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId         INT                 NOT NULL, -- peça avaliada (NivelHierarquico = 'Peca')
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RelatorioDimensional_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_RelatorioDimensional PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RelatorioDimensional_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT UQ_RelatorioDimensional_EstruturaItem UNIQUE (EstruturaItemId)
);
GO

-- Detalhe: uma avaliação por remessa avaliada pelo cliente (chega conforme a expedição parcial).
-- Aprovação/reprovação é por quantidade; retrabalho (opcional) fica vinculado à avaliação.
CREATE TABLE dbo.RelatorioDimensionalAvaliacao (
    Id                     INT IDENTITY(1,1) NOT NULL,
    RelatorioDimensionalId INT               NOT NULL,
    QuantidadeAvaliada     DECIMAL(18,4)      NOT NULL,
    QuantidadeAprovada     DECIMAL(18,4)      NOT NULL,
    QuantidadeReprovada    DECIMAL(18,4)      NOT NULL,
    Medidas                NVARCHAR(MAX)      NULL,
    InformadoPor           NVARCHAR(200)      NOT NULL, -- contato do cliente que informou
    DataAvaliacao          DATETIME2          NOT NULL CONSTRAINT DF_RDA_Data DEFAULT (SYSUTCDATETIME()),
    PedidoRetrabalhoId     INT               NULL,      -- retrabalho aberto por causa desta avaliação (opcional)
    CONSTRAINT PK_RelatorioDimensionalAvaliacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RDA_Relatorio
        FOREIGN KEY (RelatorioDimensionalId) REFERENCES dbo.RelatorioDimensional (Id),
    CONSTRAINT FK_RDA_PedidoRetrabalho
        FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT CK_RDA_Quantidades
        CHECK (QuantidadeAprovada + QuantidadeReprovada = QuantidadeAvaliada
           AND QuantidadeAprovada >= 0 AND QuantidadeReprovada >= 0)
);
GO
```

- [ ] **Step 9: Adicionar a tabela `Perda` (bucket terminal)**

Insira logo após `RelatorioDimensionalAvaliacao` (ainda na seção DIMENSIONAL/QUALIDADE, ou abra uma subseção "PERDAS"):
```sql
-- Baixa de quantidade perdida em produção (some no armazém ou morre após processo).
-- Bucket terminal: em setores + expedido + perdido = total da Peça.
-- Reposição = Pedido de Retrabalho separado (MotivoRetrabalho='Perda'), manual/opcional.
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
    CONSTRAINT FK_Perda_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Perda_PedidoRetrabalho FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT CK_Perda_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Perda_Motivo CHECK (MotivoPerda IN ('PerdaArmazem', 'MortaEmProcesso'))
);
GO
```

- [ ] **Step 10: Atualizar os índices de apoio**

Na seção "ÍNDICES DE APOIO", substitua o bloco:

Old:
```sql
CREATE INDEX IX_EstruturaItem_Kit ON dbo.EstruturaItem (KitId);
CREATE INDEX IX_EstruturaItem_Pai ON dbo.EstruturaItem (EstruturaPaiId);
CREATE INDEX IX_EstruturaSetorHistorico_Item ON dbo.EstruturaSetorHistorico (EstruturaItemId, DataEntrada);
CREATE INDEX IX_EstruturaSetorHistorico_Setor ON dbo.EstruturaSetorHistorico (SetorId, DataEntrada);
CREATE INDEX IX_Pedido_PedidoOrigem ON dbo.Pedido (PedidoOrigemId);
CREATE INDEX IX_RelatorioDimensional_EstruturaItem ON dbo.RelatorioDimensional (EstruturaItemId);
GO
```
New:
```sql
CREATE INDEX IX_EstruturaItem_Agrupamento ON dbo.EstruturaItem (AgrupamentoId);
CREATE INDEX IX_EstruturaItem_Pai ON dbo.EstruturaItem (EstruturaPaiId);
CREATE INDEX IX_EstruturaSetorHistorico_Item ON dbo.EstruturaSetorHistorico (EstruturaItemId, DataEntrada);
CREATE INDEX IX_EstruturaSetorHistorico_Setor ON dbo.EstruturaSetorHistorico (SetorId, DataEntrada);
CREATE INDEX IX_Pedido_PedidoOrigem ON dbo.Pedido (PedidoOrigemId);
CREATE INDEX IX_Expedicao_EstruturaItem ON dbo.Expedicao (EstruturaItemId);
CREATE INDEX IX_RDA_Relatorio ON dbo.RelatorioDimensionalAvaliacao (RelatorioDimensionalId);
CREATE INDEX IX_Perda_EstruturaItem ON dbo.Perda (EstruturaItemId);
GO
```
*(O antigo `IX_RelatorioDimensional_EstruturaItem` sai: o `UNIQUE (EstruturaItemId)` do header já cria o índice.)*

- [ ] **Step 11: Atualizar a query de exemplo no rodapé (Kit→Agrupamento)**

Old:
```sql
-- FROM dbo.Pedido p
-- CROSS APPLY (
--     SELECT MIN(esh.DataEntrada) AS InicioReal
--     FROM dbo.EstruturaSetorHistorico esh
--     JOIN dbo.EstruturaItem ei ON ei.Id = esh.EstruturaItemId
--     JOIN dbo.Kit k ON k.Id = ei.KitId
--     WHERE k.PedidoId = p.Id
-- ) inicio_producao
```
New:
```sql
-- FROM dbo.Pedido p
-- CROSS APPLY (
--     SELECT MIN(esh.DataEntrada) AS InicioReal
--     FROM dbo.EstruturaSetorHistorico esh
--     JOIN dbo.EstruturaItem ei ON ei.Id = esh.EstruturaItemId
--     JOIN dbo.Agrupamento a ON a.Id = ei.AgrupamentoId
--     WHERE a.PedidoId = p.Id
-- ) inicio_producao
```
Também troque o comentário de cabeçalho do arquivo `Pedido/Kit > Estrutura real` → `Pedido/Agrupamento > Estrutura real`.

- [ ] **Step 12: Verificar aplicando o `.sql` num banco descartável**

Suba o SQL Server e aplique o script inteiro num banco novo (não toca no `Rastreamento` de dev nem no seed):
```bash
docker compose up -d
# aguarde o banco ficar pronto (alguns segundos)
sqlcmd -S localhost,1433 -U sa -P Your_strong_Pass123 -C -Q "IF DB_ID('Rastreamento_verify') IS NOT NULL DROP DATABASE Rastreamento_verify; CREATE DATABASE Rastreamento_verify;"
sqlcmd -S localhost,1433 -U sa -P Your_strong_Pass123 -C -d Rastreamento_verify -I -i specs/02-modelo-de-dados.sql
```
Expected: o segundo comando termina **sem nenhuma linha `Msg NNNN, Level ...`** (zero erros). Se `sqlcmd` não estiver no host, rode dentro do container:
`docker compose exec -T <servico-sqlserver> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P Your_strong_Pass123 -C -d Rastreamento_verify -I -i /dev/stdin < specs/02-modelo-de-dados.sql`

- [ ] **Step 13: Limpar o banco de verificação**

```bash
sqlcmd -S localhost,1433 -U sa -P Your_strong_Pass123 -C -Q "DROP DATABASE Rastreamento_verify;"
```
Expected: sem erro.

- [ ] **Step 14: Commit**

```bash
git add specs/02-modelo-de-dados.sql
git commit -m "feat(schema): lote divisível, Agrupamento, Expedicao, Perda e dimensional por quantidade"
```

---

### Task 2: Regras de negócio — `specs/01-dominio-e-regras-de-negocio.md`

**Files:**
- Modify: `specs/01-dominio-e-regras-de-negocio.md`

**Interfaces:**
- Consumes: nomes de tabela/coluna da Task 1.
- Produces: glossário e regras 1-15 atualizados que as Tasks 3-5 espelham em linguagem.

- [ ] **Step 1: Atualizar o glossário**

- Linha `**Kit**`: renomear para `**Agrupamento**` e trocar a definição por:
  `Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo é descritivo — não impõe roteiro.`
- Linha `**EstruturaItem**`: remover o trecho final `— e esse lote não se divide` e trocar por `— e esse lote é divisível por quantidades livres (ver regra 9).`
- Linha `**Relatório Dimensional**`: trocar a definição por:
  `Avaliação de conformidade dimensional de uma Peça, **opcional** (o cliente exige em Peças específicas — ex.: primeira manufatura ou primeiro trabalho após reprovação no cliente; marcado no cadastro via EstruturaItem.RequerRelatorioDimensional). Quando existe, é **um relatório por Peça, acumulativo**: cada remessa avaliada gera uma RelatorioDimensionalAvaliacao com quantidade aprovada/reprovada. Aprovação/reprovação é por quantidade. Reprovação não exige retrabalho imediato.`
- Adicionar ao glossário duas entradas novas:
  `| **Expedição (remessa)** | Saída de uma quantidade de uma Peça para o cliente. Pode ser **parcial**: o cliente aceita uma parte vital antes e o restante depois. Cada remessa é uma linha em Expedicao. |`
  `| **Perda** | Baixa de quantidade perdida em produção (some no armazém ou morre após um processo que deu errado). Vai para um bucket terminal; a reposição é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda'). |`

- [ ] **Step 2: Reescrever a regra 9 (indivisibilidade → divisibilidade)**

Old:
```
9. O lote de um `EstruturaItem` é **indivisível** dentro do sistema: não pode estar em dois
   Setores ao mesmo tempo. (A exceção — divisão entre empresas de pintura terceirizadas no
   fim do processo — é controlada fora do sistema, por etiqueta no ERP delas.)
```
New:
```
9. O lote de um `EstruturaItem` é **divisível por quantidades livres**: uma parte pode estar
   num Setor e outra parte em outro Setor ao mesmo tempo (ex.: 6 na Usinagem, 4 na Corte).
   Não há identidade de sub-lote (sem etiqueta/serial) — controla-se apenas *quanto* está
   *onde*. Invariante: **conservação de quantidade** — soma das unidades em todos os Setores +
   expedido (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça. (A divisão física
   entre pinturas terceirizadas no fim do processo segue controlada fora do sistema.)
```

- [ ] **Step 3: Reescrever as regras 10, 11, 12**

Old (10-12):
```
10. Ao chegar na Expedição, é gerado um Relatório Dimensional **por Peça** (não por Kit),
    informado pelo cliente que originou o Pedido. Hoje é feito para 100% das peças.
11. Uma Peça pode ser aprovada ou reprovada individualmente — um Kit pode ter peças
    aprovadas e reprovadas ao mesmo tempo.
12. Quando uma Peça é reprovada, **pode** (não é obrigatório, nem imediato) ser aberto um
    novo Pedido do tipo `Retrabalho`, vinculado ao Pedido original (`PedidoOrigemId`) e ao
    Relatório Dimensional que o originou (`RelatorioDimensional.PedidoRetrabalhoId`). O
    Pedido de Retrabalho também registra um `MotivoRetrabalho` categorizado:
    `ReprovacaoDimensional`, `ErroInterno` ou `SolicitacaoCliente`.
```
New:
```
10. O Relatório Dimensional é **opcional**: só quando o cliente exige, em Peças específicas
    (ex.: primeira manufatura, primeiro trabalho após reprovação no cliente). Isso é sabido
    no cadastro do Pedido e marcado **por Peça** em `EstruturaItem.RequerRelatorioDimensional`.
    Quando existe, é **um relatório por Peça, acumulativo**: cada remessa avaliada pelo
    cliente gera uma `RelatorioDimensionalAvaliacao` (quantidade avaliada/aprovada/reprovada).
11. Aprovação e reprovação são **por quantidade** dentro da Peça: numa mesma avaliação parte
    das unidades pode aprovar e parte reprovar; e um Agrupamento pode ter Peças com
    resultados diferentes ao mesmo tempo.
12. Quando uma quantidade é reprovada, **pode** (não é obrigatório, nem imediato) ser aberto
    um novo Pedido do tipo `Retrabalho` **para aquela quantidade**, vinculado ao Pedido
    original (`PedidoOrigemId`) e à avaliação que o originou
    (`RelatorioDimensionalAvaliacao.PedidoRetrabalhoId`). O Pedido de Retrabalho registra um
    `MotivoRetrabalho` categorizado: `ReprovacaoDimensional`, `ErroInterno`,
    `SolicitacaoCliente` ou `Perda`.
```

- [ ] **Step 4: Reescrever a regra 13 (conclusão) e adicionar as regras 16 e 17**

Old (13):
```
13. Um Pedido é considerado **concluído** quando o **último** Kit daquele Pedido é
    concluído (expedido/avaliado). `Pedido.DataConclusao` só é preenchida nesse momento.
```
New (13) + duas regras novas ao final da lista:
```
13. Uma Peça está **concluída** quando nada dela está mais em produção — toda a quantidade
    virou expedido (`Expedicao`) **ou** perdido (`Perda`). Um Agrupamento conclui quando
    todas as suas Peças concluem (`Agrupamento.DataConclusao`), e um Pedido conclui quando o
    **último** Agrupamento conclui (`Pedido.DataConclusao`). Não depende mais de "avaliado",
    já que o Relatório Dimensional é opcional.

16. A **expedição pode ser parcial** (remessas): o cliente aceita uma parte vital antes e o
    restante segue depois. Cada remessa é uma linha em `Expedicao` com a quantidade. A soma
    das remessas de uma Peça nunca excede a quantidade total (validado na aplicação).

17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem` ou
    `MortaEmProcesso`), levando a quantidade ao bucket terminal "perdido". Para repor, abre-se
    um Pedido de Retrabalho separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça
    original. Como na reprovação, registrar a perda **não** abre retrabalho automaticamente.
```

- [ ] **Step 5: Atualizar a seção "Pontos ainda em aberto" (se necessário) e verificar**

Confirme que o texto não afirma mais indivisibilidade em lugar nenhum. Rode:
```bash
grep -niE "indivis|último Kit|100% das peças|lote agregado, indivisível" specs/01-dominio-e-regras-de-negocio.md
```
Expected: **nenhuma linha** (todas as menções de indivisibilidade/último-Kit/100% foram reescritas). Um `grep -ni "Kit" specs/01-dominio-e-regras-de-negocio.md` só deve retornar ocorrências onde "Kit" é o **tipo** de Agrupamento (ex.: `'Kit'`), nunca a entidade.

- [ ] **Step 6: Commit**

```bash
git add specs/01-dominio-e-regras-de-negocio.md
git commit -m "docs(dominio): regras de lote divisível, dimensional por quantidade, expedição parcial e perda"
```

---

### Task 3: `CLAUDE.md` — invariantes, convenções, decisões descartadas

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: nomes da Task 1 e regras da Task 2.

- [ ] **Step 1: Reescrever o invariante do lote indivisível**

Old:
```
- Um `EstruturaItem` (lote) nunca pode estar em dois Setores ao mesmo tempo — o lote é
  indivisível. Há um índice filtrado único no banco garantindo isso; não contorne essa
  regra na camada de aplicação.
```
New:
```
- Um `EstruturaItem` (lote) é **divisível por quantidades livres**: pode ter quantidades em
  Setores diferentes ao mesmo tempo. Não há identidade de sub-lote (sem serial). O
  invariante a preservar é **conservação de quantidade**: soma em Setores + expedido
  (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça (validado na aplicação).
```

- [ ] **Step 2: Ajustar o invariante de conclusão do Pedido**

Old:
```
- Um Pedido só é concluído quando o **último** Kit dele é concluído.
```
New:
```
- Um Pedido só é concluído quando o **último** Agrupamento dele é concluído (e um Agrupamento,
  quando todas as suas Peças concluem — toda a quantidade expedida ou perdida).
```

- [ ] **Step 3: Ajustar a nota sobre reprovação (incluir perda) — se presente**

Old:
```
- Reprovação no Relatório Dimensional **não** gera Retrabalho automaticamente — é uma
  ação separada e opcional do usuário (perfil Qualidade), com `MotivoRetrabalho`
  obrigatório quando aplicada.
```
New:
```
- Reprovação no Relatório Dimensional **não** gera Retrabalho automaticamente — é uma
  ação separada e opcional do usuário (perfil Qualidade), com `MotivoRetrabalho`
  obrigatório quando aplicada. O mesmo vale para **perda**: registrar a perda não abre
  Retrabalho sozinho.
```

- [ ] **Step 4: Atualizar as convenções de nomenclatura**

Na lista de "Nomes de domínio em português", troque `Kit` por `Agrupamento` e acrescente `Expedicao`, `Perda`, `RelatorioDimensionalAvaliacao`. Exemplo de linha resultante:
`Pedido`, `Agrupamento`, `EstruturaItem`, `Componente`, `Material`, `Setor`, `Usuario`, `Perfil`, `RelatorioDimensional`, `RelatorioDimensionalAvaliacao`, `Expedicao`, `Perda`, etc.

- [ ] **Step 5: Atualizar "O que evitar" (decisões descartadas)**

- **Manter** a linha "Rastreamento por serial individual (decidido: lote agregado)".
- **Remover** qualquer item que trate indivisibilidade de lote como decisão fechada, se existir. (A entrada "Criar Peça e Item como tabelas separadas" permanece.)
- Se houver menção a "lote indivisível" na seção de invariantes de comandos/`-I`, revise: o schema não usa mais índice filtrado, então a flag `-I` do sqlcmd deixou de ser obrigatória. Ajuste a instrução de aplicação do `.sql` se ela citar `-I` como obrigatório.

- [ ] **Step 6: Verificar**

```bash
grep -niE "indivis|último Kit|índice filtrado" CLAUDE.md
```
Expected: nenhuma linha afirmando indivisibilidade/último-Kit como regra vigente. `grep -ni "\bKit\b" CLAUDE.md` só deve sobrar onde "Kit" for o tipo de Agrupamento.

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): invariantes de lote divisível, Agrupamento e perda"
```

---

### Task 4: Nomenclatura leve — `00-visao-geral.md`, `03-arquitetura-tecnica.md`, `06-roadmap-mvp.md`

**Files:**
- Modify: `specs/00-visao-geral.md`
- Modify: `specs/03-arquitetura-tecnica.md`
- Modify: `specs/06-roadmap-mvp.md`

- [ ] **Step 1: `00-visao-geral.md`**

- Linha 8 `Cadastro de Pedidos, Kits e da estrutura...` → `Cadastro de Pedidos, Agrupamentos e da estrutura...`.
- Linha 11 `Relatório Dimensional por peça na Expedição, com aprovação/reprovação` → acrescentar que é **opcional** e **por quantidade**: `Relatório Dimensional opcional por Peça (quando o cliente exige), com aprovação/reprovação por quantidade`.
- Linha 21 `Amostragem no relatório dimensional — hoje é feito em 100% das peças (pode mudar no futuro)`: remover, pois deixou de ser 100%. Se quiser manter uma linha de escopo, trocar por: `O Relatório Dimensional é opcional (exigido pelo cliente em Peças específicas), não amostragem sobre 100%.`
- Linhas 30 `**PCP** ... cadastra Pedidos, Kits, estrutura` → `...cadastra Pedidos, Agrupamentos, estrutura`.
- Manter a linha 20 (rastreamento por lote agregado, não serial) — continua verdadeira.

- [ ] **Step 2: `03-arquitetura-tecnica.md`**

- Linha 6 `negócio (recursão, indivisibilidade de lote, transição de status)` → `negócio (recursão, conservação de quantidade do lote, transição de status)`.
- Linha 24 `índices já definidos (ex.: índice filtrado que garante lote indivisível).` → `índices já definidos (o índice filtrado de indivisibilidade foi removido — o lote é divisível).`
- Linhas 28-29 `fechar Kit → verificar se é o último → fechar Pedido) ... ConcluirKitUseCase` → `fechar Agrupamento → verificar se é o último → fechar Pedido) ... ConcluirAgrupamentoUseCase`.
- Linha 32-33 (lista de `MotivoRetrabalho` / `RelatorioDimensionalId`): acrescentar `Perda` aos motivos e trocar `RelatorioDimensionalId` por `RelatorioDimensionalAvaliacaoId` (o vínculo do retrabalho passou para a avaliação).

- [ ] **Step 3: `06-roadmap-mvp.md`**

- Linha 22 `Pedido, Kit — criação e listagem` → `Pedido, Agrupamento — criação e listagem`.
- Linha 23 `cadastrar um Pedido com Kits vazios` → `...com Agrupamentos vazios`.
- Linha 29 `estrutura de um Kit (Peça → Itens → sub-Itens)` → `...de um Agrupamento (Peça → Itens → sub-Itens)`.
- Linha 35 `Validação de lote indivisível (reaproveitar o índice filtrado...)`: substituir por `Validação de conservação de quantidade do lote (soma em setores + expedido + perdido = total; validada na aplicação, não mais por índice filtrado).`
- Linhas 48-49 `Registro de RelatorioDimensional por Peça ... Regra de fechamento de Kit (todas as Peças aprovadas) e de Pedido (último Kit...)`: trocar por `Registro opcional de RelatorioDimensional por Peça (perfil Qualidade), avaliado por quantidade. Regra de fechamento de Agrupamento (todas as Peças concluídas — expedidas ou perdidas) e de Pedido (último Agrupamento).` Considere acrescentar à Fase 5 (ou à fase de expedição) o **registro de Expedicao (remessas parciais)** e o **registro de Perda**, se ainda não estiverem previstos.

- [ ] **Step 4: Verificar**

```bash
grep -niE "\bKit\b|indivis|último Kit|100% das peças" specs/00-visao-geral.md specs/03-arquitetura-tecnica.md specs/06-roadmap-mvp.md
```
Expected: nenhuma menção residual a `Kit` como entidade, indivisibilidade ou 100% das peças.

- [ ] **Step 5: Commit**

```bash
git add specs/00-visao-geral.md specs/03-arquitetura-tecnica.md specs/06-roadmap-mvp.md
git commit -m "docs(specs): nomenclatura Agrupamento e ajustes de escopo (visão geral, arquitetura, roadmap)"
```

---

### Task 5: Fluxos e endpoints — `04-fluxos-de-usuario.md`, `05-api-endpoints.md`

**Files:**
- Modify: `specs/04-fluxos-de-usuario.md`
- Modify: `specs/05-api-endpoints.md`

- [ ] **Step 1: `04-fluxos-de-usuario.md` — cadastro e apontamento de setor**

- Linhas 11-15 (`PCP cadastra N Kits` / `Para cada Kit, PCP monta a estrutura`): trocar `Kit`→`Agrupamento` e mencionar que cada Agrupamento tem um Tipo (`Kit`/`Avulso`). Ex.: `2. PCP cadastra N Agrupamentos para o Pedido (cada um com Tipo 'Kit' ou 'Avulso'). 3. Para cada Agrupamento, PCP monta a estrutura (EstruturaItem)...`. Marcar na Peça, no cadastro, se ela `RequerRelatorioDimensional`.
- Linhas 27-28 (registro de saída valida "não existe outra passagem aberta — lote indivisível"): reescrever para o modelo divisível:
  `3. Ao concluir, registra saída (DataSaida) de uma quantidade; o sistema permite que a Peça tenha quantidades em setores diferentes ao mesmo tempo (lote divisível). O que se valida é a conservação de quantidade (nunca movimentar mais do que existe naquele ponto).`

- [ ] **Step 2: `04-fluxos-de-usuario.md` — Relatório Dimensional (Expedição) e Retrabalho**

Reescreva a seção "4. Relatório Dimensional (Expedição)" (linhas ~43-51) para refletir expedição parcial + dimensional opcional por quantidade:
```
## 4. Expedição e Relatório Dimensional

1. A expedição pode ser **parcial**: o cliente aceita uma quantidade vital agora
   (uma remessa = uma linha em Expedicao) e o restante depois.
2. Se a Peça foi marcada com RequerRelatorioDimensional, a Qualidade registra, para cada
   remessa avaliada pelo cliente, uma RelatorioDimensionalAvaliacao com a quantidade
   avaliada/aprovada/reprovada (acumulando no relatório único da Peça). Sem essa marca,
   não há relatório.
3. Uma Peça conclui quando toda a sua quantidade virou expedido ou perdido. O Agrupamento
   conclui quando todas as suas Peças concluem; se for o último Agrupamento em aberto do
   Pedido → Pedido.DataConclusao é preenchida.
```
Na seção "5. Retrabalho" (linhas ~58-66): trocar `RelatorioDimensional` (vínculo) por `RelatorioDimensionalAvaliacao`; deixar claro que o retrabalho é aberto **para a quantidade** reprovada; e acrescentar que **perda** também origina retrabalho (`MotivoRetrabalho='Perda'`), manual. Trocar `cadastro de Kit/estrutura` por `cadastro de Agrupamento/estrutura`.

- [ ] **Step 3: `04-fluxos-de-usuario.md` — novo fluxo de Perda**

Acrescente uma seção nova:
```
## 6. Perda de peças

1. Quando uma quantidade se perde em produção (some no armazém = PerdaArmazem, ou morre
   após um processo = MortaEmProcesso), registra-se uma Perda (Peça, quantidade, motivo,
   opcionalmente o Setor onde estava, responsável, observação).
2. A quantidade perdida sai da produção (bucket terminal), contando para a conclusão da Peça.
3. Para repor, a Qualidade/PCP pode (opcional) abrir um Pedido de Retrabalho para aquela
   quantidade, com MotivoRetrabalho='Perda', vinculado via Perda.PedidoRetrabalhoId.
```

- [ ] **Step 4: `05-api-endpoints.md` — Agrupamento**

- Seção "## Pedido / Kit" (linha 21) → "## Pedido / Agrupamento".
- Linha 26 (body do retrabalho): incluir `'Perda'` no enum e trocar `relatorioDimensionalId?` por `relatorioDimensionalAvaliacaoId?`:
  `Body: { motivoRetrabalho: 'ReprovacaoDimensional' | 'ErroInterno' | 'SolicitacaoCliente' | 'Perda', relatorioDimensionalAvaliacaoId?: number, perdaId?: number }`.
- Linhas 27-33 (`/kits...`): trocar `kits`→`agrupamentos` nas rotas (`GET/POST /pedidos/{id}/agrupamentos`, `GET /agrupamentos/{id}`, `GET /agrupamentos/{id}/estrutura`, `POST /agrupamentos/{id}/estrutura`).

- [ ] **Step 5: `05-api-endpoints.md` — Expedição, Dimensional e Perda**

- Reescrever a seção "## Dimensional" para o modelo header+detalhe por quantidade:
  `GET /pecas/{estruturaItemId}/relatorio-dimensional` (header + avaliações),
  `POST /pecas/{estruturaItemId}/relatorio-dimensional/avaliacoes` — body `{ quantidadeAvaliada, quantidadeAprovada, quantidadeReprovada, medidas?, informadoPor }`.
- Acrescentar seção "## Expedição": `POST /pecas/{estruturaItemId}/expedicoes` — body `{ quantidade, responsavel }` (remessa parcial); `GET /pecas/{estruturaItemId}/expedicoes`.
- Acrescentar seção "## Perdas": `POST /pecas/{estruturaItemId}/perdas` — body `{ quantidade, motivoPerda: 'PerdaArmazem' | 'MortaEmProcesso', setorId?, observacao?, responsavel }`; `GET /pecas/{estruturaItemId}/perdas`.
  *(Rotas em nível de Peça, `estruturaItemId`; nomes definitivos podem ser afinados na fase de implementação da API.)*

- [ ] **Step 6: Verificar**

```bash
grep -niE "\bkits?\b|indivis|DentroTolerancia|último Kit" specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md
```
Expected: nenhuma menção residual a `kit(s)` como entidade/rota, indivisibilidade, `DentroTolerancia` ou "último Kit". Ocorrências de `'Kit'` como valor de `Tipo` são aceitáveis.

- [ ] **Step 7: Commit**

```bash
git add specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md
git commit -m "docs(specs): fluxos e endpoints de expedição parcial, dimensional por quantidade e perda"
```

---

## Notas de execução

- **Sem impacto em `src/`**: nenhuma etapa toca código C#; a suíte de testes existente (auth) não é afetada. Não rode `dotnet` — não há o que compilar aqui.
- **Database First**: a única "aplicação" de schema neste plano é contra o banco descartável `Rastreamento_verify` (Task 1). O banco de dev (`Rastreamento`, com seed) **não** é alterado por este plano; ele será reconstruído a partir do `.sql` atualizado quando a fase que usa essas tabelas começar.
- **Ordem**: Task 1 → 2 → 3 → 4 → 5.
