-- Migracao idempotente da Fase 3D para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md, "Comandos"
-- (o mesmo comando do db/alter-fase-3.sql, com este arquivo).
SET NOCOUNT ON;
GO

/* 1. Setor.Atividade (spec da Fase 3D, secao 2.3) ------------------------------------------------ */
IF COL_LENGTH('dbo.Setor', 'Atividade') IS NULL
    ALTER TABLE dbo.Setor ADD Atividade NVARCHAR(40) NULL;
GO

/* 2. O Inicio de um pai aponta a Montagem (spec da Fase 3D, secao 3.3) -------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_Movimentacao_MontagemSoNaBaixa' AND definition NOT LIKE '%''Inicio''%')
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_MontagemSoNaBaixa')
    ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo IN ('Inicio', 'Estorno'))
            OR (Tipo NOT IN ('Montagem', 'Inicio', 'Estorno') AND MontagemId IS NULL));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Movimentacao_UmInicioPorMontagem')
    CREATE UNIQUE INDEX UX_Movimentacao_UmInicioPorMontagem
        ON dbo.Movimentacao (MontagemId) WHERE Tipo = 'Inicio' AND MontagemId IS NOT NULL;
GO

/* 3. dbo.PedidoPausa (spec da Fase 3D, secao 3.1) ------------------------------------------------ */
IF OBJECT_ID('dbo.PedidoPausa') IS NULL
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
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PedidoPausa_UmaAbertaPorPedido')
    CREATE UNIQUE INDEX UX_PedidoPausa_UmaAbertaPorPedido
        ON dbo.PedidoPausa (PedidoId) WHERE RetomadoEm IS NULL;
GO
