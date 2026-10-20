-- Migracao idempotente da Fase 5A (afericao, expedicao e fechamento) para banco criado ANTES dela. A fonte
-- de verdade continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco
-- antigo ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar:
-- CLAUDE.md, "Comandos". Os usuarios 'qualidade' e 'gestao' nao entram aqui: entram por db/seed.sql,
-- reaplicavel.
SET NOCOUNT ON;
GO

/* 1. Aborto: as tabelas antigas so saem vazias (spec da Fase 5A, secao 3.9) -----------------------------
   Com linhas, o script aborta em vez de descartar dado em silencio. A forma antiga de
   dbo.RelatorioDimensional e a sem a coluna Tipo; a nova nunca e tocada. */
-- Contagem em EXEC: o lote inteiro e compilado antes de rodar, e uma tabela que ja saiu (segunda
-- execucao) faria o SELECT falhar com "Invalid object name" mesmo atras do OBJECT_ID.
DECLARE @Linhas INT = 0;

IF OBJECT_ID('dbo.Expedicao', 'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.Expedicao', N'@n INT OUTPUT', @n = @Linhas OUTPUT;
    IF @Linhas > 0
        THROW 50002, 'dbo.Expedicao tem linhas na forma antiga: migre-as a mao antes de rodar este script.', 1;
END

IF OBJECT_ID('dbo.RelatorioDimensionalAvaliacao', 'U') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RelatorioDimensionalAvaliacao', N'@n INT OUTPUT', @n = @Linhas OUTPUT;
    IF @Linhas > 0
        THROW 50002, 'dbo.RelatorioDimensionalAvaliacao tem linhas na forma antiga: migre-as a mao antes de rodar este script.', 1;
END

IF OBJECT_ID('dbo.RelatorioDimensional', 'U') IS NOT NULL AND COL_LENGTH('dbo.RelatorioDimensional', 'Tipo') IS NULL
BEGIN
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.RelatorioDimensional', N'@n INT OUTPUT', @n = @Linhas OUTPUT;
    IF @Linhas > 0
        THROW 50002, 'dbo.RelatorioDimensional tem linhas na forma antiga: migre-as a mao antes de rodar este script.', 1;
END
GO

/* 2. As tabelas antigas saem (a avaliacao primeiro: ela aponta o cabecalho) ------------------------------ */
IF OBJECT_ID('dbo.RelatorioDimensionalAvaliacao', 'U') IS NOT NULL
    DROP TABLE dbo.RelatorioDimensionalAvaliacao;
IF OBJECT_ID('dbo.RelatorioDimensional', 'U') IS NOT NULL AND COL_LENGTH('dbo.RelatorioDimensional', 'Tipo') IS NULL
    DROP TABLE dbo.RelatorioDimensional;
IF OBJECT_ID('dbo.Expedicao', 'U') IS NOT NULL
    DROP TABLE dbo.Expedicao;
GO

/* 3. As quatro tabelas novas e os indices (secoes 3.4 a 3.6) -------------------------------------------- */
IF OBJECT_ID('dbo.Remessa', 'U') IS NULL
    CREATE TABLE dbo.Remessa (
        Id        INT IDENTITY(1,1)  NOT NULL,
        DataHora  DATETIME2           NOT NULL CONSTRAINT DF_Remessa_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId INT                 NOT NULL,
        CONSTRAINT PK_Remessa PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Remessa_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id)
    );
GO

IF OBJECT_ID('dbo.ContagemDimensional', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContagemDimensional (
        Id               INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId  INT                 NOT NULL,
        Conformes        DECIMAL(18,4)       NOT NULL,
        NaoConformes     DECIMAL(18,4)       NOT NULL,
        DataHora         DATETIME2           NOT NULL CONSTRAINT DF_ContagemDimensional_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId        INT                 NOT NULL,
        CONSTRAINT PK_ContagemDimensional PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ContagemDimensional_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_ContagemDimensional_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_ContagemDimensional_ConformesNaoNegativo CHECK (Conformes >= 0),
        CONSTRAINT CK_ContagemDimensional_NaoConformesNaoNegativo CHECK (NaoConformes >= 0),
        CONSTRAINT CK_ContagemDimensional_AlgumaUnidade CHECK (Conformes + NaoConformes > 0)
    );
    CREATE INDEX IX_ContagemDimensional_EstruturaItem ON dbo.ContagemDimensional (EstruturaItemId);
END
GO

IF OBJECT_ID('dbo.LiberacaoDimensional', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LiberacaoDimensional (
        Id                   INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId      INT                 NOT NULL,
        Tipo                 NVARCHAR(20)        NOT NULL,
        PedidoDeLiberacaoId  INT                 NULL,
        Quantidade           DECIMAL(18,4)       NOT NULL,
        Texto                NVARCHAR(500)       NULL,
        DataHora             DATETIME2           NOT NULL CONSTRAINT DF_LiberacaoDimensional_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId            INT                 NOT NULL,
        CONSTRAINT PK_LiberacaoDimensional PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_LiberacaoDimensional_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_LiberacaoDimensional_Pedido FOREIGN KEY (PedidoDeLiberacaoId) REFERENCES dbo.LiberacaoDimensional (Id),
        CONSTRAINT FK_LiberacaoDimensional_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_LiberacaoDimensional_Tipo CHECK (Tipo IN ('Pedido', 'Resposta')),
        CONSTRAINT CK_LiberacaoDimensional_PedidoCoerente
            CHECK (Tipo <> 'Pedido' OR (PedidoDeLiberacaoId IS NULL AND Quantidade > 0 AND Texto IS NOT NULL)),
        CONSTRAINT CK_LiberacaoDimensional_RespostaCoerente
            CHECK (Tipo <> 'Resposta' OR (PedidoDeLiberacaoId IS NOT NULL AND Quantidade >= 0))
    );
    CREATE INDEX IX_LiberacaoDimensional_EstruturaItem ON dbo.LiberacaoDimensional (EstruturaItemId);
END
GO

IF OBJECT_ID('dbo.RelatorioDimensional', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelatorioDimensional (
        Id               INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId  INT                 NOT NULL,
        Tipo             NVARCHAR(20)        NOT NULL,
        Resultado        NVARCHAR(20)        NULL,
        EstornoDeId      INT                 NULL,
        DataHora         DATETIME2           NOT NULL CONSTRAINT DF_RelatorioDimensional_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId        INT                 NOT NULL,
        CONSTRAINT PK_RelatorioDimensional PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_RelatorioDimensional_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_RelatorioDimensional_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.RelatorioDimensional (Id),
        CONSTRAINT FK_RelatorioDimensional_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_RelatorioDimensional_Tipo CHECK (Tipo IN ('Afericao', 'Estorno')),
        CONSTRAINT CK_RelatorioDimensional_Resultado CHECK (Resultado IS NULL OR Resultado IN ('Conforme', 'NaoConforme')),
        CONSTRAINT CK_RelatorioDimensional_ResultadoNaAfericao CHECK (Tipo = 'Estorno' OR Resultado IS NOT NULL),
        CONSTRAINT CK_RelatorioDimensional_EstornoApontaOriginal
            CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
                OR (Tipo = 'Afericao' AND EstornoDeId IS NULL))
    );
    CREATE UNIQUE INDEX UX_RelatorioDimensional_EstornoDe ON dbo.RelatorioDimensional (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
    CREATE INDEX IX_RelatorioDimensional_EstruturaItem ON dbo.RelatorioDimensional (EstruturaItemId);
END
GO

/* 4. dbo.Movimentacao: as tres colunas de agrupador (secao 3.3) ------------------------------------------
   Em lote proprio: as constraints do bloco 5 as citam, e um lote so compila as colunas que ja existem. */
IF COL_LENGTH('dbo.Movimentacao', 'ContagemId') IS NULL
    ALTER TABLE dbo.Movimentacao ADD ContagemId INT NULL
        CONSTRAINT FK_Movimentacao_Contagem FOREIGN KEY REFERENCES dbo.ContagemDimensional (Id);
IF COL_LENGTH('dbo.Movimentacao', 'LiberacaoId') IS NULL
    ALTER TABLE dbo.Movimentacao ADD LiberacaoId INT NULL
        CONSTRAINT FK_Movimentacao_Liberacao FOREIGN KEY REFERENCES dbo.LiberacaoDimensional (Id);
IF COL_LENGTH('dbo.Movimentacao', 'RemessaId') IS NULL
    ALTER TABLE dbo.Movimentacao ADD RemessaId INT NULL
        CONSTRAINT FK_Movimentacao_Remessa FOREIGN KEY REFERENCES dbo.Remessa (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_Contagem' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_Contagem ON dbo.Movimentacao (ContagemId) WHERE ContagemId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_Liberacao' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_Liberacao ON dbo.Movimentacao (LiberacaoId) WHERE LiberacaoId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_Remessa' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_Remessa ON dbo.Movimentacao (RemessaId) WHERE RemessaId IS NOT NULL;
GO

/* 5. dbo.Movimentacao: as sete constraints recriadas com as posicoes e os tipos novos ---------------------
   DROP se existir e ADD, sempre: a recriacao e o que torna o script rodavel de novo. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_Tipo' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_Tipo;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_OrigemPosicao' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_OrigemPosicao;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_DestinoPosicao' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_DestinoPosicao;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_OrigemCoerente' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_OrigemCoerente;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_DestinoCoerente' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_DestinoCoerente;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_Transicao' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_Transicao;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_RegistroCoerente' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_RegistroCoerente;
GO

ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_Tipo
    CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Medicao', 'Conserto',
                    'PedidoDeLiberacao', 'Liberacao', 'Recusa', 'Expedicao', 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_OrigemPosicao
    CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto',
                             'NaExpedicao', 'Conforme', 'NaoConforme', 'AguardandoLiberacao', 'Liberada', 'Montado', 'Expedido'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_DestinoPosicao
    CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto',
                              'NaExpedicao', 'Conforme', 'NaoConforme', 'AguardandoLiberacao', 'Liberada', 'Montado', 'Expedido'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_OrigemCoerente
    CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado', 'Conforme', 'NaoConforme',
                              'AguardandoLiberacao', 'Liberada', 'Expedido')
            AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
        OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
        OR (OrigemPosicao IN ('AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto')
            AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_DestinoCoerente
    CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado', 'Conforme', 'NaoConforme',
                               'AguardandoLiberacao', 'Liberada', 'Expedido')
            AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
        OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
        OR (DestinoPosicao IN ('AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto')
            AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_Transicao
    CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
        OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                              AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                              AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                              AND DestinoPosicao = 'AguardandoMontagem')
        OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
        -- Fase 5A, tabela da secao 3.3 da spec: medicao, conserto, liberacao e expedicao
        OR (Tipo = 'Medicao'  AND OrigemPosicao = 'NaExpedicao' AND DestinoPosicao IN ('Conforme', 'NaoConforme'))
        OR (Tipo = 'Conserto' AND OrigemPosicao = 'NaoConforme' AND DestinoPosicao = 'AguardandoConserto')
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoConserto' AND DestinoPosicao = 'EmConserto'
                              AND OrigemSetorId = DestinoSetorId)
        OR (Tipo = 'Termino'  AND OrigemPosicao = 'EmConserto' AND DestinoPosicao = 'ConsertoPronto'
                              AND OrigemSetorId = DestinoSetorId)
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'ConsertoPronto' AND DestinoPosicao = 'NaExpedicao')
        OR (Tipo = 'PedidoDeLiberacao' AND OrigemPosicao = 'NaoConforme' AND DestinoPosicao = 'AguardandoLiberacao')
        OR (Tipo = 'Liberacao' AND OrigemPosicao = 'AguardandoLiberacao' AND DestinoPosicao = 'Liberada')
        OR (Tipo = 'Recusa'   AND OrigemPosicao = 'AguardandoLiberacao' AND DestinoPosicao = 'NaoConforme')
        OR (Tipo = 'Expedicao' AND OrigemPosicao IN ('Liberada', 'Conforme', 'NaExpedicao', 'NaoConforme')
                               AND DestinoPosicao = 'Expedido')
        OR (Tipo = 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_RegistroCoerente
    CHECK ((Tipo = 'Medicao' AND ContagemId IS NOT NULL AND LiberacaoId IS NULL AND RemessaId IS NULL)
        OR (Tipo IN ('PedidoDeLiberacao', 'Liberacao', 'Recusa')
            AND LiberacaoId IS NOT NULL AND ContagemId IS NULL AND RemessaId IS NULL)
        OR (Tipo = 'Expedicao' AND RemessaId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL)
        OR (Tipo = 'Estorno'
            AND (CASE WHEN ContagemId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN LiberacaoId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN RemessaId IS NULL THEN 0 ELSE 1 END) <= 1)
        OR (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Conserto')
            AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL));
GO
