-- Migracao idempotente da Fase 4 (separacao de materiais) para banco criado ANTES dela. A fonte de
-- verdade continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco
-- antigo ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar:
-- CLAUDE.md, "Comandos". O usuario 'separador' nao entra aqui: entra por db/seed.sql, reaplicavel.
SET NOCOUNT ON;
GO

/* 1. Material.SetorDeEstoqueId (spec da Fase 4, secao 3.1) ------------------------------------------- */
IF COL_LENGTH('dbo.Material', 'SetorDeEstoqueId') IS NULL
    ALTER TABLE dbo.Material ADD SetorDeEstoqueId INT NULL
        CONSTRAINT FK_Material_SetorDeEstoque FOREIGN KEY REFERENCES dbo.Setor (Id);
GO

/* 2. dbo.MaterialSeparacao reescrita como livro (secao 3.2) -------------------------------------------
   So a forma antiga (coluna Responsavel) e tocada. Com linhas, aborta em vez de descartar dado em
   silencio; vazia, e apagada e recriada na forma nova. A forma nova nao e mexida. */
IF COL_LENGTH('dbo.MaterialSeparacao', 'Responsavel') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MaterialSeparacao)
        THROW 50001, 'dbo.MaterialSeparacao tem linhas na forma antiga: migre-as a mao antes de rodar este script.', 1;

    DROP TABLE dbo.MaterialSeparacao;

    CREATE TABLE dbo.MaterialSeparacao (
        Id                  INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId     INT                 NOT NULL,
        MaterialId          INT                 NOT NULL,
        Tipo                NVARCHAR(20)        NOT NULL,
        Quantidade          DECIMAL(18,4)       NOT NULL,
        EstornoDeId         INT                 NULL,
        DataHora            DATETIME2           NOT NULL CONSTRAINT DF_MaterialSeparacao_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId           INT                 NOT NULL,
        CONSTRAINT PK_MaterialSeparacao PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_MaterialSeparacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_MaterialSeparacao_Material FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
        CONSTRAINT FK_MaterialSeparacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.MaterialSeparacao (Id),
        CONSTRAINT FK_MaterialSeparacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_MaterialSeparacao_QuantidadePositiva CHECK (Quantidade > 0),
        CONSTRAINT CK_MaterialSeparacao_Tipo CHECK (Tipo IN ('Separacao', 'Estorno')),
        CONSTRAINT CK_MaterialSeparacao_EstornoApontaOriginal
            CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
                OR (Tipo = 'Separacao' AND EstornoDeId IS NULL))
    );

    -- Em EXEC: o lote inteiro e compilado antes de rodar, e a tabela nova ainda nao existe nessa hora,
    -- so a antiga (sem EstornoDeId) — um CREATE INDEX direto falha com "Invalid column name".
    EXEC (N'CREATE UNIQUE INDEX UX_MaterialSeparacao_EstornoDe ON dbo.MaterialSeparacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;');
    EXEC (N'CREATE INDEX IX_MaterialSeparacao_EstruturaItem ON dbo.MaterialSeparacao (EstruturaItemId);');
END
GO

/* 3. Perfil Almoxarifado vira Separador (secao 3.3) ---------------------------------------------------
   Tres casos. Os dois perfis existem (o seed novo foi reaplicado antes deste script): os usuarios do
   antigo passam para o novo e o antigo e apagado, sem violar UQ_Perfil_Nome. So o antigo existe: e
   renomeado. Nenhum dos dois: o Separador e inserido. Com so o novo, nada a fazer. */
DECLARE @Antigo INT = (SELECT Id FROM dbo.Perfil WHERE Nome = 'Almoxarifado');
DECLARE @Novo   INT = (SELECT Id FROM dbo.Perfil WHERE Nome = 'Separador');

IF @Antigo IS NOT NULL AND @Novo IS NOT NULL
BEGIN
    UPDATE dbo.Usuario SET PerfilId = @Novo WHERE PerfilId = @Antigo;
    DELETE FROM dbo.Perfil WHERE Id = @Antigo;
END
ELSE IF @Antigo IS NOT NULL
    UPDATE dbo.Perfil SET Nome = 'Separador' WHERE Id = @Antigo;
ELSE IF @Novo IS NULL
    INSERT INTO dbo.Perfil (Nome) VALUES ('Separador');
GO
