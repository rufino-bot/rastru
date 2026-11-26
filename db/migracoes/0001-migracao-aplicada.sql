-- Registro das migrações aplicadas (C9). Sem GO: roda na transação do migrador, que anota esta mesma
-- migração depois dela. Num banco novo a tabela já nasceu do specs/02-modelo-de-dados.sql.
IF OBJECT_ID('dbo.MigracaoAplicada') IS NULL
    CREATE TABLE dbo.MigracaoAplicada (
        Nome        NVARCHAR(200) NOT NULL,
        AplicadaEm  DATETIME2     NOT NULL CONSTRAINT DF_MigracaoAplicada_AplicadaEm DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MigracaoAplicada PRIMARY KEY (Nome)
    );
