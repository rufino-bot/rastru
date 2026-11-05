-- Migracao idempotente da Fase 5B-1 (perda e descarte: a baixa) para banco criado ANTES dela. A fonte de
-- verdade continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco
-- antigo ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar:
-- CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. Aborto: a dbo.Perda antiga so sai vazia (spec da Fase 5B-1, secao 3.3) -----------------------------
   A forma antiga e a que tem a coluna MotivoPerda; a nova nunca e tocada. Com linhas, o script aborta em
   vez de descartar dado em silencio. */
-- Contagem em EXEC: o lote inteiro e compilado antes de rodar, e uma tabela que ja mudou de forma
-- (segunda execucao) faria o SELECT falhar mesmo atras do COL_LENGTH.
DECLARE @Linhas INT = 0;

IF OBJECT_ID('dbo.Perda', 'U') IS NOT NULL AND COL_LENGTH('dbo.Perda', 'MotivoPerda') IS NOT NULL
BEGIN
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.Perda', N'@n INT OUTPUT', @n = @Linhas OUTPUT;
    IF @Linhas > 0
        THROW 50003, 'dbo.Perda tem linhas na forma antiga: migre-as a mao antes de rodar este script.', 1;
END

/* 2. A tabela antiga sai (vazia, pelo aborto acima) ------------------------------------------------------
   No MESMO lote do aborto, de proposito: o THROW encerra so o lote em que esta, e com o DROP num lote a
   parte o script, rodado sem sqlcmd -b ou pelo SSMS, apagaria a tabela com linhas. Como no da Fase 5A. */
IF OBJECT_ID('dbo.Perda', 'U') IS NOT NULL AND COL_LENGTH('dbo.Perda', 'MotivoPerda') IS NOT NULL
    DROP TABLE dbo.Perda;
GO

/* 3. A tabela nova (secao 3.2) ----------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Perda', 'U') IS NULL
    CREATE TABLE dbo.Perda (
        Id                 INT IDENTITY(1,1)  NOT NULL,
        Motivo             NVARCHAR(20)        NOT NULL,
        Observacao         NVARCHAR(500)       NULL,
        PedidoId           INT                 NOT NULL,
        AgrupamentoId      INT                 NULL,
        EstruturaItemId    INT                 NULL,
        Tudo               BIT                 NOT NULL CONSTRAINT DF_Perda_Tudo DEFAULT (0),
        DataHora           DATETIME2           NOT NULL CONSTRAINT DF_Perda_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId          INT                 NOT NULL,
        PedidoRetrabalhoId INT                 NULL,
        CONSTRAINT PK_Perda PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Perda_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
        CONSTRAINT FK_Perda_Agrupamento FOREIGN KEY (AgrupamentoId) REFERENCES dbo.Agrupamento (Id),
        CONSTRAINT FK_Perda_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Perda_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_Perda_PedidoRetrabalho FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
        CONSTRAINT CK_Perda_Motivo CHECK (Motivo IN ('PerdaArmazem', 'MortaEmProcesso', 'Descarte')),
        CONSTRAINT CK_Perda_Escopo CHECK (AgrupamentoId IS NULL OR EstruturaItemId IS NULL)
    );
GO

/* 4. dbo.Movimentacao: a coluna do agrupador (secao 3.1) -------------------------------------------------
   Em lote proprio: as constraints do bloco 5 a citam, e um lote so compila as colunas que ja existem. */
IF COL_LENGTH('dbo.Movimentacao', 'PerdaId') IS NULL
    ALTER TABLE dbo.Movimentacao ADD PerdaId INT NULL
        CONSTRAINT FK_Movimentacao_Perda FOREIGN KEY REFERENCES dbo.Perda (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_Perda' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_Perda ON dbo.Movimentacao (PerdaId) WHERE PerdaId IS NOT NULL;
GO

/* 5. dbo.Movimentacao: as constraints recriadas com a posicao e o tipo novos -------------------------------
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
                    'PedidoDeLiberacao', 'Liberacao', 'Recusa', 'Expedicao', 'Perda', 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_OrigemPosicao
    CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto',
                             'NaExpedicao', 'Conforme', 'NaoConforme', 'AguardandoLiberacao', 'Liberada', 'Montado', 'Expedido', 'Perdido'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_DestinoPosicao
    CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto',
                              'NaExpedicao', 'Conforme', 'NaoConforme', 'AguardandoLiberacao', 'Liberada', 'Montado', 'Expedido', 'Perdido'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_OrigemCoerente
    CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado', 'Conforme', 'NaoConforme',
                              'AguardandoLiberacao', 'Liberada', 'Expedido', 'Perdido')
            AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
        OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
        OR (OrigemPosicao IN ('AguardandoMontagem', 'AguardandoConserto', 'EmConserto', 'ConsertoPronto')
            AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_DestinoCoerente
    CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado', 'Conforme', 'NaoConforme',
                               'AguardandoLiberacao', 'Liberada', 'Expedido', 'Perdido')
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
        -- Fase 5B-1, secao 4.2 da spec: a baixa tira de uma posicao em producao ou do local de expedicao
        OR (Tipo = 'Perda' AND DestinoPosicao = 'Perdido'
                           AND OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem',
                                                 'AguardandoConserto', 'EmConserto', 'ConsertoPronto',
                                                 'NaExpedicao', 'Conforme', 'NaoConforme', 'Liberada'))
        OR (Tipo = 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_RegistroCoerente
    CHECK ((Tipo = 'Medicao' AND ContagemId IS NOT NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL)
        OR (Tipo IN ('PedidoDeLiberacao', 'Liberacao', 'Recusa')
            AND LiberacaoId IS NOT NULL AND ContagemId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL)
        OR (Tipo = 'Expedicao' AND RemessaId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL AND PerdaId IS NULL)
        OR (Tipo = 'Perda' AND PerdaId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL)
        OR (Tipo = 'Estorno'
            AND (CASE WHEN ContagemId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN LiberacaoId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN RemessaId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN PerdaId IS NULL THEN 0 ELSE 1 END) <= 1)
        OR (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Conserto')
            AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL));
GO
