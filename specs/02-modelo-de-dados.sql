/* =====================================================================
   MODELO DE RASTREAMENTO DE PEÇAS - SQL Server (T-SQL)
   Camadas: Catálogo (receita padrão) > Pedido/Agrupamento > Estrutura real
            (árvore recursiva) > Execução/rastreamento > Dimensional

   Observação: o schema não usa mais índices filtrados; a flag -I do sqlcmd
   deixou de ser obrigatória (é inofensiva se mantida).
   ===================================================================== */

/* ---------------------------------------------------------------------
   TABELAS DE APOIO
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Setor (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Nome            NVARCHAR(100)       NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Setor_Ativo DEFAULT (1),
    CONSTRAINT PK_Setor PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Setor_Nome UNIQUE (Nome)
);

CREATE TABLE dbo.Material (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Descricao       NVARCHAR(200)       NOT NULL,
    UnidadeMedida   NVARCHAR(10)        NOT NULL, -- ex: UN, M, KG, M2
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Material_Ativo DEFAULT (1),
    CONSTRAINT PK_Material PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Material_Codigo UNIQUE (Codigo)
);

/* ---------------------------------------------------------------------
   USUÁRIOS E PERFIS (login próprio + JWT)
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Perfil (
    Id      INT IDENTITY(1,1) NOT NULL,
    Nome    NVARCHAR(30)      NOT NULL, -- Operador | Qualidade | PCP | Gestao | Administrador
    CONSTRAINT PK_Perfil PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Perfil_Nome UNIQUE (Nome)
);

-- MVP: um perfil por usuário. Se no futuro precisar de múltiplos perfis por
-- pessoa, trocar por tabela associativa UsuarioPerfil (N:N).
CREATE TABLE dbo.Usuario (
    Id              INT IDENTITY(1,1)  NOT NULL,
    NomeUsuario     NVARCHAR(50)        NOT NULL,
    SenhaHash       NVARCHAR(200)       NOT NULL,
    NomeCompleto    NVARCHAR(200)       NOT NULL,
    PerfilId        INT                 NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Usuario_Ativo DEFAULT (1),
    -- Lockout de conta (anti brute-force): falhas de senha em sequência e até quando a conta
    -- está trancada. BloqueadoAte NULL = destrancada; a trava expira sozinha, sem ação de admin.
    -- O contador zera no login bem-sucedido e também no momento em que a trava é aplicada.
    FalhasConsecutivas INT              NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0),
    BloqueadoAte    DATETIME2           NULL,
    CONSTRAINT PK_Usuario PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Usuario_NomeUsuario UNIQUE (NomeUsuario),
    CONSTRAINT FK_Usuario_Perfil FOREIGN KEY (PerfilId) REFERENCES dbo.Perfil (Id)
);
GO

CREATE TABLE dbo.RefreshToken (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    UsuarioId               INT                 NOT NULL,
    TokenHash               NVARCHAR(200)       NOT NULL,   -- SHA-256 do refresh token (nunca em claro)
    ExpiraEm                DATETIME2           NOT NULL,
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RefreshToken_CriadoEm DEFAULT (SYSUTCDATETIME()),
    RevogadoEm              DATETIME2           NULL,       -- NULL = ativo; preenchido no logout ou na rotação
    SubstituidoPorTokenHash NVARCHAR(200)       NULL,       -- rastro de rotação (auditoria)
    RowVersion              ROWVERSION          NOT NULL,   -- token de concorrência otimista
    CONSTRAINT PK_RefreshToken PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RefreshToken_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_RefreshToken_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_RefreshToken_ExpiraAposCriado CHECK (ExpiraEm > CriadoEm)
);
GO
CREATE INDEX IX_RefreshToken_Usuario ON dbo.RefreshToken (UsuarioId);
GO

/* ---------------------------------------------------------------------
   CATÁLOGO (receita padrão / template reutilizável entre pedidos)
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Componente (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Descricao       NVARCHAR(200)       NOT NULL,
    Tipo            NVARCHAR(20)        NOT NULL, -- Bruto | Fabricado | Montagem
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Componente_Ativo DEFAULT (1),
    CONSTRAINT PK_Componente PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Componente_Codigo UNIQUE (Codigo),
    CONSTRAINT CK_Componente_Tipo CHECK (Tipo IN ('Bruto', 'Fabricado', 'Montagem'))
);

-- Receita padrão: de quais componentes-filho um componente-pai é composto
CREATE TABLE dbo.ComponenteFilhoPadrao (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    ComponentePaiId         INT                 NOT NULL,
    ComponenteFilhoId       INT                 NOT NULL,
    QuantidadePadrao        DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_ComponenteFilhoPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteFilhoPadrao_Pai
        FOREIGN KEY (ComponentePaiId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteFilhoPadrao_Filho
        FOREIGN KEY (ComponenteFilhoId) REFERENCES dbo.Componente (Id),
    CONSTRAINT CK_ComponenteFilhoPadrao_NaoAutoReferencia
        CHECK (ComponentePaiId <> ComponenteFilhoId),
    CONSTRAINT UQ_ComponenteFilhoPadrao UNIQUE (ComponentePaiId, ComponenteFilhoId)
);

CREATE TABLE dbo.ComponenteMaterialPadrao (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    ComponenteId        INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    QuantidadePadrao    DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_ComponenteMaterialPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteMaterialPadrao_Componente
        FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteMaterialPadrao_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
    CONSTRAINT UQ_ComponenteMaterialPadrao UNIQUE (ComponenteId, MaterialId)
);

CREATE TABLE dbo.ComponenteRoteiroPadrao (
    Id              INT IDENTITY(1,1)  NOT NULL,
    ComponenteId    INT                 NOT NULL,
    SetorId         INT                 NOT NULL,
    Ordem           INT                 NOT NULL,
    CONSTRAINT PK_ComponenteRoteiroPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteRoteiroPadrao_Componente
        FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteRoteiroPadrao_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT UQ_ComponenteRoteiroPadrao UNIQUE (ComponenteId, Ordem)
);

/* ---------------------------------------------------------------------
   PEDIDO / AGRUPAMENTO
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Pedido (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    Numero              NVARCHAR(30)        NOT NULL,
    Cliente             NVARCHAR(200)       NOT NULL,
    Tipo                NVARCHAR(20)        NOT NULL, -- Fabricacao | Retrabalho
    PedidoOrigemId      INT                 NULL,      -- preenchido só quando Tipo = Retrabalho
    MotivoRetrabalho    NVARCHAR(30)        NULL,      -- preenchido só quando Tipo = Retrabalho
    Status              NVARCHAR(20)        NOT NULL CONSTRAINT DF_Pedido_Status DEFAULT ('Aberto'),
    DataAbertura        DATETIME2           NOT NULL CONSTRAINT DF_Pedido_DataAbertura DEFAULT (SYSUTCDATETIME()),
    DataConclusao       DATETIME2           NULL,
    CriadoPorUsuarioId  INT                 NOT NULL,  -- autoria: responde "quem abriu este pedido"
    CONSTRAINT PK_Pedido PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Pedido_Numero UNIQUE (Numero),
    CONSTRAINT FK_Pedido_PedidoOrigem
        FOREIGN KEY (PedidoOrigemId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT FK_Pedido_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Pedido_Tipo CHECK (Tipo IN ('Fabricacao', 'Retrabalho')),
    CONSTRAINT CK_Pedido_Status
        CHECK (Status IN ('Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado')),
    CONSTRAINT CK_Pedido_OrigemObrigatoriaSeRetrabalho
        CHECK (Tipo <> 'Retrabalho' OR PedidoOrigemId IS NOT NULL),
    CONSTRAINT CK_Pedido_MotivoRetrabalho
        CHECK (MotivoRetrabalho IS NULL
            OR MotivoRetrabalho IN ('ReprovacaoDimensional', 'ErroInterno', 'SolicitacaoCliente', 'Perda')),
    CONSTRAINT CK_Pedido_MotivoSoSeRetrabalho
        CHECK (Tipo = 'Retrabalho' OR MotivoRetrabalho IS NULL),
    CONSTRAINT CK_Pedido_ConclusaoAposAbertura
        CHECK (DataConclusao IS NULL OR DataConclusao >= DataAbertura)
);

CREATE TABLE dbo.Agrupamento (
    Id              INT IDENTITY(1,1)  NOT NULL,
    PedidoId        INT                 NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Tipo            NVARCHAR(20)        NOT NULL, -- Kit (vai para solda) | Avulso (não passa por solda); descritivo
    DataConclusao   DATETIME2           NULL, -- preenchida quando todas as Peças do agrupamento fecham
    CriadoPorUsuarioId INT                 NOT NULL,
    CriadoEm        DATETIME2           NOT NULL
        CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Agrupamento PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Agrupamento_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT FK_Agrupamento_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_Agrupamento_PedidoCodigo UNIQUE (PedidoId, Codigo),
    CONSTRAINT CK_Agrupamento_Tipo CHECK (Tipo IN ('Kit', 'Avulso'))
);

/* ---------------------------------------------------------------------
   ESTRUTURA REAL (árvore recursiva efetivamente usada no Pedido/Agrupamento;
   pode ter sido copiada do catálogo e depois customizada)
   --------------------------------------------------------------------- */

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

CREATE TABLE dbo.EstruturaMaterial (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    Quantidade          DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_EstruturaMaterial PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaMaterial_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaMaterial_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
    CONSTRAINT UQ_EstruturaMaterial UNIQUE (EstruturaItemId, MaterialId)
);

CREATE TABLE dbo.EstruturaRoteiro (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    SetorId             INT                 NOT NULL,
    Ordem               INT                 NOT NULL,
    CONSTRAINT PK_EstruturaRoteiro PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaRoteiro_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaRoteiro_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT UQ_EstruturaRoteiro UNIQUE (EstruturaItemId, Ordem)
);

/* ---------------------------------------------------------------------
   EXECUÇÃO / RASTREAMENTO
   --------------------------------------------------------------------- */

CREATE TABLE dbo.EstruturaSetorHistorico (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    SetorId             INT                 NOT NULL,
    QuantidadeMovimentada DECIMAL(18,4)     NOT NULL,
    DataEntrada         DATETIME2           NOT NULL,   -- chegada no setor (pode ficar em fila)
    DataInicioExecucao  DATETIME2           NULL,       -- início real do trabalho (opcional, p/ KPI de fila x capacidade)
    DataSaida           DATETIME2           NULL,       -- NULL = ainda está nesse setor (várias passagens abertas por item são permitidas: lote divisível)
    CONSTRAINT PK_EstruturaSetorHistorico PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaSetorHistorico_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaSetorHistorico_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT CK_EstruturaSetorHistorico_InicioAposEntrada
        CHECK (DataInicioExecucao IS NULL OR DataInicioExecucao >= DataEntrada),
    CONSTRAINT CK_EstruturaSetorHistorico_SaidaAposEntrada
        CHECK (DataSaida IS NULL OR DataSaida >= DataEntrada)
);
GO

CREATE TABLE dbo.MaterialSeparacao (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    Quantidade          DECIMAL(18,4)       NOT NULL,
    DataSeparacao       DATETIME2           NOT NULL CONSTRAINT DF_MaterialSeparacao_Data DEFAULT (SYSUTCDATETIME()),
    Responsavel         NVARCHAR(100)       NOT NULL,
    CONSTRAINT PK_MaterialSeparacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MaterialSeparacao_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_MaterialSeparacao_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id)
);

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

/* ---------------------------------------------------------------------
   DIMENSIONAL / QUALIDADE
   --------------------------------------------------------------------- */

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
           AND QuantidadeAvaliada > 0
           AND QuantidadeAprovada >= 0 AND QuantidadeReprovada >= 0)
);
GO

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

/* ---------------------------------------------------------------------
   ÍNDICES DE APOIO (consultas de rastreamento e KPI mais comuns)
   --------------------------------------------------------------------- */

CREATE INDEX IX_EstruturaItem_Agrupamento ON dbo.EstruturaItem (AgrupamentoId);
CREATE INDEX IX_EstruturaItem_Pai ON dbo.EstruturaItem (EstruturaPaiId);
CREATE INDEX IX_EstruturaSetorHistorico_Item ON dbo.EstruturaSetorHistorico (EstruturaItemId, DataEntrada);
CREATE INDEX IX_EstruturaSetorHistorico_Setor ON dbo.EstruturaSetorHistorico (SetorId, DataEntrada);
CREATE INDEX IX_Pedido_PedidoOrigem ON dbo.Pedido (PedidoOrigemId);
CREATE INDEX IX_Expedicao_EstruturaItem ON dbo.Expedicao (EstruturaItemId);
CREATE INDEX IX_RDA_Relatorio ON dbo.RelatorioDimensionalAvaliacao (RelatorioDimensionalId);
CREATE INDEX IX_Perda_EstruturaItem ON dbo.Perda (EstruturaItemId);
GO

/* =====================================================================
   EXEMPLOS DE CONSULTA (KPIs)
   ===================================================================== */

-- Tempo de liberação por setor (tempo médio que cada setor leva)
-- SELECT SetorId, AVG(DATEDIFF(MINUTE, DataEntrada, DataSaida)) AS MediaMinutos
-- FROM dbo.EstruturaSetorHistorico
-- WHERE DataSaida IS NOT NULL
-- GROUP BY SetorId;

-- Tempo total, tempo em fila e tempo de produção por pedido
-- SELECT
--     p.Id,
--     p.DataAbertura,
--     inicio_producao.InicioReal,
--     p.DataConclusao,
--     DATEDIFF(DAY, p.DataAbertura, inicio_producao.InicioReal)   AS DiasEmFila,
--     DATEDIFF(DAY, inicio_producao.InicioReal, p.DataConclusao) AS DiasProducao,
--     DATEDIFF(DAY, p.DataAbertura, p.DataConclusao)              AS DiasTotal
-- FROM dbo.Pedido p
-- CROSS APPLY (
--     SELECT MIN(esh.DataEntrada) AS InicioReal
--     FROM dbo.EstruturaSetorHistorico esh
--     JOIN dbo.EstruturaItem ei ON ei.Id = esh.EstruturaItemId
--     JOIN dbo.Agrupamento a ON a.Id = ei.AgrupamentoId
--     WHERE a.PedidoId = p.Id
-- ) inicio_producao
-- WHERE p.DataConclusao IS NOT NULL;
