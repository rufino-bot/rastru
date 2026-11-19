-- Migracao idempotente do Panorama para banco criado ANTES dele. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Nao muda tabela existente e nao ha dado
-- a transformar: dbo.Embalagem nasce vazia. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. dbo.Embalagem: o registro do "Embalado" da Peca (regra 40), so de inclusao ----------------------- */
IF OBJECT_ID('dbo.Embalagem') IS NULL
    CREATE TABLE dbo.Embalagem (
        Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Embalagem PRIMARY KEY,
        EstruturaItemId INT           NOT NULL CONSTRAINT FK_Embalagem_EstruturaItem REFERENCES dbo.EstruturaItem (Id),
        Tipo            NVARCHAR(10)  NOT NULL CONSTRAINT CK_Embalagem_Tipo CHECK (Tipo IN (N'Marcar', N'Desmarcar')),
        DataHora        DATETIME2     NOT NULL CONSTRAINT DF_Embalagem_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId       INT           NOT NULL CONSTRAINT FK_Embalagem_Usuario REFERENCES dbo.Usuario (Id)
    );
GO

/* 2. O indice da ultima linha de cada Peca ------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Embalagem_EstruturaItem' AND object_id = OBJECT_ID('dbo.Embalagem'))
    CREATE INDEX IX_Embalagem_EstruturaItem ON dbo.Embalagem (EstruturaItemId, Id DESC);
GO
