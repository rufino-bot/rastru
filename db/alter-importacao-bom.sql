-- Migracao idempotente do import da estrutura a partir do BOM, para banco criado ANTES dele. A fonte de
-- verdade continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco
-- antigo ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md,
-- "Comandos" (o mesmo comando do db/alter-fase-3d.sql, com este arquivo). Os comentarios sobre o indice
-- filtrado e a ausencia de cascata estao no 02.
SET NOCOUNT ON;
GO

/* 1. dbo.ImportacaoDeEstrutura (a FK RaizId vem depois das duas tabelas) ------------------------- */
IF OBJECT_ID('dbo.ImportacaoDeEstrutura') IS NULL
    CREATE TABLE dbo.ImportacaoDeEstrutura (
        Id                          INT IDENTITY(1,1)  NOT NULL,
        AgrupamentoId               INT                 NOT NULL,
        NomeDoArquivo               NVARCHAR(260)       NOT NULL,
        RaizId                      INT                 NULL,
        QuantidadeDaPeca            DECIMAL(18,4)       NULL,
        RequerRelatorioDimensional  BIT                 NOT NULL CONSTRAINT DF_ImportacaoDeEstrutura_RequerRelatorio DEFAULT (0),
        CriadoPorUsuarioId          INT                 NOT NULL,
        CriadoEm                    DATETIME2           NOT NULL CONSTRAINT DF_ImportacaoDeEstrutura_CriadoEm DEFAULT (SYSUTCDATETIME()),
        AtualizadoEm                DATETIME2           NOT NULL CONSTRAINT DF_ImportacaoDeEstrutura_AtualizadoEm DEFAULT (SYSUTCDATETIME()),
        Versao                      ROWVERSION          NOT NULL,
        CONSTRAINT PK_ImportacaoDeEstrutura PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ImportacaoDeEstrutura_Agrupamento FOREIGN KEY (AgrupamentoId) REFERENCES dbo.Agrupamento (Id),
        CONSTRAINT FK_ImportacaoDeEstrutura_CriadoPorUsuario FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id)
    );
GO

/* 2. dbo.ImportacaoDeEstruturaComponente e o indice filtrado do codigo ---------------------------- */
IF OBJECT_ID('dbo.ImportacaoDeEstruturaComponente') IS NULL
    CREATE TABLE dbo.ImportacaoDeEstruturaComponente (
        Id                              INT IDENTITY(1,1)  NOT NULL,
        ImportacaoId                    INT                 NOT NULL,
        CodigoLido                      NVARCHAR(50)        NULL,
        DescricaoLida                   NVARCHAR(200)       NOT NULL,
        ComponenteId                    INT                 NULL,
        CodigoNovo                      NVARCHAR(50)        NULL,
        DescricaoNova                   NVARCHAR(200)       NULL,
        TipoNovo                        NVARCHAR(20)        NULL,
        EscolhaDeReceita                NVARCHAR(10)        NULL,
        ImpressaoDaReceitaDoCatalogo    BINARY(32)          NULL,
        ArquivoSolidoPendenteId         INT                 NULL,
        CONSTRAINT PK_ImportacaoDeEstruturaComponente PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ImportacaoDeEstruturaComponente_Importacao FOREIGN KEY (ImportacaoId) REFERENCES dbo.ImportacaoDeEstrutura (Id),
        CONSTRAINT FK_ImportacaoDeEstruturaComponente_Componente FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
        CONSTRAINT FK_ImportacaoDeEstruturaComponente_ArquivoSolidoPendente FOREIGN KEY (ArquivoSolidoPendenteId) REFERENCES dbo.ArquivoDeComponente (Id),
        CONSTRAINT CK_ImportacaoDeEstruturaComponente_CasadoOuNovo
            CHECK (ComponenteId IS NULL OR (CodigoNovo IS NULL AND DescricaoNova IS NULL AND TipoNovo IS NULL)),
        CONSTRAINT CK_ImportacaoDeEstruturaComponente_Escolha CHECK (EscolhaDeReceita IN ('Catalogo', 'Importada')),
        CONSTRAINT CK_ImportacaoDeEstruturaComponente_TipoNovo CHECK (TipoNovo IN ('Bruto', 'Fabricado', 'Montagem'))
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ImportacaoDeEstruturaComponente_Codigo')
    CREATE UNIQUE INDEX UX_ImportacaoDeEstruturaComponente_Codigo
        ON dbo.ImportacaoDeEstruturaComponente (ImportacaoId, CodigoLido) WHERE CodigoLido IS NOT NULL;
GO

/* 2b. O indice de ImportacaoId (o porque esta no 02) ---------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImportacaoDeEstruturaComponente_ImportacaoId')
    CREATE INDEX IX_ImportacaoDeEstruturaComponente_ImportacaoId
        ON dbo.ImportacaoDeEstruturaComponente (ImportacaoId);
GO

/* 3. A FK circular RaizId, agora que as duas tabelas existem ------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ImportacaoDeEstrutura_Raiz')
    ALTER TABLE dbo.ImportacaoDeEstrutura ADD CONSTRAINT FK_ImportacaoDeEstrutura_Raiz
        FOREIGN KEY (RaizId) REFERENCES dbo.ImportacaoDeEstruturaComponente (Id);
GO

/* 4. dbo.ImportacaoDeEstruturaFilho --------------------------------------------------------------- */
IF OBJECT_ID('dbo.ImportacaoDeEstruturaFilho') IS NULL
    CREATE TABLE dbo.ImportacaoDeEstruturaFilho (
        Id              INT IDENTITY(1,1)  NOT NULL,
        PaiId           INT                 NOT NULL,
        FilhoId         INT                 NOT NULL,
        Ordem           INT                 NOT NULL,
        QuantidadeLida  DECIMAL(18,4)       NOT NULL,
        Quantidade      DECIMAL(18,4)       NOT NULL,
        CONSTRAINT PK_ImportacaoDeEstruturaFilho PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ImportacaoDeEstruturaFilho_Pai FOREIGN KEY (PaiId) REFERENCES dbo.ImportacaoDeEstruturaComponente (Id),
        CONSTRAINT FK_ImportacaoDeEstruturaFilho_Filho FOREIGN KEY (FilhoId) REFERENCES dbo.ImportacaoDeEstruturaComponente (Id),
        CONSTRAINT UQ_ImportacaoDeEstruturaFilho UNIQUE (PaiId, FilhoId),
        CONSTRAINT CK_ImportacaoDeEstruturaFilho_Quantidade CHECK (Quantidade > 0 AND QuantidadeLida > 0),
        CONSTRAINT CK_ImportacaoDeEstruturaFilho_NaoAutoReferencia CHECK (PaiId <> FilhoId)
    );
GO
