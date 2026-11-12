-- Migracao idempotente da Fase 5B-2 (reposicao: Pronto, Devolucao, Reclamacao, "nao repor") para banco criado ANTES
-- dela. A fonte de verdade continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um
-- banco antigo ate la. Rodar de novo nao muda nada: tabelas e colunas so nascem se faltam, e as constraints sao
-- recriadas (DROP se existir e ADD, sempre). Nao descarta dado: nenhuma tabela antiga muda de forma. Como aplicar:
-- CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. dbo.Perda: a coluna da baixa ligada (decisao P2 do plano de backend), em lote proprio -------------------
   As constraints do bloco 2 citam PerdaId e PedidoRetrabalhoId, e um lote so compila as colunas que ja existem. */
IF COL_LENGTH('dbo.Perda', 'PerdaDeOrigemId') IS NULL
    ALTER TABLE dbo.Perda ADD PerdaDeOrigemId INT NULL
        CONSTRAINT FK_Perda_PerdaDeOrigem FOREIGN KEY REFERENCES dbo.Perda (Id);
GO

/* 2. dbo.Perda: o motivo novo e as duas guardas do retrabalho (secao 3.2) -------------------------------------
   O ADD confere as linhas que ja existem: as da 5B-1 nao tem PedidoRetrabalhoId, e nenhuma e Reaproveitado. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Perda_Motivo' AND parent_object_id = OBJECT_ID('dbo.Perda'))
    ALTER TABLE dbo.Perda DROP CONSTRAINT CK_Perda_Motivo;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Perda_ReaproveitadoTemRetrabalho' AND parent_object_id = OBJECT_ID('dbo.Perda'))
    ALTER TABLE dbo.Perda DROP CONSTRAINT CK_Perda_ReaproveitadoTemRetrabalho;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Perda_DescarteSemRetrabalho' AND parent_object_id = OBJECT_ID('dbo.Perda'))
    ALTER TABLE dbo.Perda DROP CONSTRAINT CK_Perda_DescarteSemRetrabalho;
GO

ALTER TABLE dbo.Perda ADD CONSTRAINT CK_Perda_Motivo
    CHECK (Motivo IN ('PerdaArmazem', 'MortaEmProcesso', 'Descarte', 'Reaproveitado'));
ALTER TABLE dbo.Perda ADD CONSTRAINT CK_Perda_ReaproveitadoTemRetrabalho
    CHECK (Motivo <> 'Reaproveitado' OR PedidoRetrabalhoId IS NOT NULL);
ALTER TABLE dbo.Perda ADD CONSTRAINT CK_Perda_DescarteSemRetrabalho
    CHECK (Motivo <> 'Descarte' OR PedidoRetrabalhoId IS NULL);
GO

/* 3. dbo.Reclamacao (secao 3.3) -------------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.Reclamacao', 'U') IS NULL
    CREATE TABLE dbo.Reclamacao (
        Id                     INT IDENTITY(1,1)  NOT NULL,
        PedidoId               INT                 NOT NULL,
        EstruturaItemId        INT                 NOT NULL,
        Quantidade             DECIMAL(18,4)       NOT NULL,
        Descricao              NVARCHAR(500)       NOT NULL,
        PedidoRetrabalhoId     INT                 NULL,
        DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Reclamacao_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId              INT                 NOT NULL,
        EstornadaEm            DATETIME2           NULL,
        EstornadaPorUsuarioId  INT                 NULL,
        CONSTRAINT PK_Reclamacao PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Reclamacao_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
        CONSTRAINT FK_Reclamacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Reclamacao_PedidoRetrabalho FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
        CONSTRAINT FK_Reclamacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_Reclamacao_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_Reclamacao_Quantidade CHECK (Quantidade > 0),
        CONSTRAINT CK_Reclamacao_EstornoCompleto
            CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
                OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL))
    );
GO

/* 4. dbo.DispensaDeReposicao (secao 3.4), com os dois indices unicos filtrados ---------------------------------- */
IF OBJECT_ID('dbo.DispensaDeReposicao', 'U') IS NULL
    CREATE TABLE dbo.DispensaDeReposicao (
        Id                     INT IDENTITY(1,1)  NOT NULL,
        PerdaId                INT                 NULL,
        ReclamacaoId           INT                 NULL,
        Justificativa          NVARCHAR(500)       NOT NULL,
        DataHora               DATETIME2           NOT NULL CONSTRAINT DF_DispensaDeReposicao_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId              INT                 NOT NULL,
        EstornadaEm            DATETIME2           NULL,
        EstornadaPorUsuarioId  INT                 NULL,
        CONSTRAINT PK_DispensaDeReposicao PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_DispensaDeReposicao_Perda FOREIGN KEY (PerdaId) REFERENCES dbo.Perda (Id),
        CONSTRAINT FK_DispensaDeReposicao_Reclamacao FOREIGN KEY (ReclamacaoId) REFERENCES dbo.Reclamacao (Id),
        CONSTRAINT FK_DispensaDeReposicao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_DispensaDeReposicao_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_DispensaDeReposicao_Alvo
            CHECK ((PerdaId IS NOT NULL AND ReclamacaoId IS NULL) OR (PerdaId IS NULL AND ReclamacaoId IS NOT NULL)),
        CONSTRAINT CK_DispensaDeReposicao_EstornoCompleto
            CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
                OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DispensaDeReposicao_PerdaVigente' AND object_id = OBJECT_ID('dbo.DispensaDeReposicao'))
    CREATE UNIQUE INDEX UX_DispensaDeReposicao_PerdaVigente
        ON dbo.DispensaDeReposicao (PerdaId) WHERE PerdaId IS NOT NULL AND EstornadaEm IS NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DispensaDeReposicao_ReclamacaoVigente' AND object_id = OBJECT_ID('dbo.DispensaDeReposicao'))
    CREATE UNIQUE INDEX UX_DispensaDeReposicao_ReclamacaoVigente
        ON dbo.DispensaDeReposicao (ReclamacaoId) WHERE ReclamacaoId IS NOT NULL AND EstornadaEm IS NULL;
GO

/* 5. dbo.Movimentacao: a coluna da reclamacao (secao 3.1) -------------------------------------------------------
   Em lote proprio: as constraints do bloco 7 a citam. */
IF COL_LENGTH('dbo.Movimentacao', 'ReclamacaoId') IS NULL
    ALTER TABLE dbo.Movimentacao ADD ReclamacaoId INT NULL
        CONSTRAINT FK_Movimentacao_Reclamacao FOREIGN KEY REFERENCES dbo.Reclamacao (Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_Reclamacao' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_Reclamacao ON dbo.Movimentacao (ReclamacaoId) WHERE ReclamacaoId IS NOT NULL;
GO

/* 6. dbo.EstruturaItem: o no de origem da copia do Retrabalho (secao 3.5, C14) --------------------------------- */
IF COL_LENGTH('dbo.EstruturaItem', 'OrigemEstruturaItemId') IS NULL
    ALTER TABLE dbo.EstruturaItem ADD OrigemEstruturaItemId INT NULL
        CONSTRAINT FK_EstruturaItem_Origem FOREIGN KEY REFERENCES dbo.EstruturaItem (Id);
GO

/* 7. dbo.Movimentacao: os tipos Pronto e Devolucao (secao 3.1) -------------------------------------------------
   So tres constraints mudam: o tipo, a transicao e o registro. As de posicao e de Setor/passo ja servem (as duas
   transicoes novas saem de AIniciar para AguardandoColeta, AguardandoMontagem e AguardandoConserto). DROP se
   existir e ADD, sempre: a recriacao e o que torna o script rodavel de novo. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_Tipo' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_Tipo;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_Transicao' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_Transicao;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Movimentacao_RegistroCoerente' AND parent_object_id = OBJECT_ID('dbo.Movimentacao'))
    ALTER TABLE dbo.Movimentacao DROP CONSTRAINT CK_Movimentacao_RegistroCoerente;
GO

ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_Tipo
    CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Medicao', 'Conserto',
                    'PedidoDeLiberacao', 'Liberacao', 'Recusa', 'Expedicao', 'Perda', 'Pronto', 'Devolucao', 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_Transicao
    CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
        OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                              AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                              AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
        OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                              AND DestinoPosicao = 'AguardandoMontagem')
        OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
        -- Fase 5A, tabela da seção 3.3 da spec: medicao, conserto, liberacao e expedicao
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
        -- Fase 5B-2, secao 3.1 da spec: a reposicao entra no Retrabalho vindo de AIniciar
        OR (Tipo = 'Pronto' AND OrigemPosicao = 'AIniciar'
                            AND DestinoPosicao IN ('AguardandoColeta', 'AguardandoMontagem'))
        OR (Tipo = 'Devolucao' AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'AguardandoConserto')
        OR (Tipo = 'Estorno'));
ALTER TABLE dbo.Movimentacao ADD CONSTRAINT CK_Movimentacao_RegistroCoerente
    CHECK ((Tipo = 'Medicao' AND ContagemId IS NOT NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL AND ReclamacaoId IS NULL)
        OR (Tipo IN ('PedidoDeLiberacao', 'Liberacao', 'Recusa')
            AND LiberacaoId IS NOT NULL AND ContagemId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL AND ReclamacaoId IS NULL)
        OR (Tipo = 'Expedicao' AND RemessaId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL AND PerdaId IS NULL AND ReclamacaoId IS NULL)
        OR (Tipo IN ('Perda', 'Pronto') AND PerdaId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND ReclamacaoId IS NULL)
        OR (Tipo = 'Devolucao' AND ReclamacaoId IS NOT NULL AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL)
        OR (Tipo = 'Estorno'
            AND (CASE WHEN ContagemId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN LiberacaoId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN RemessaId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN PerdaId IS NULL THEN 0 ELSE 1 END
               + CASE WHEN ReclamacaoId IS NULL THEN 0 ELSE 1 END) <= 1)
        OR (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Conserto')
            AND ContagemId IS NULL AND LiberacaoId IS NULL AND RemessaId IS NULL AND PerdaId IS NULL AND ReclamacaoId IS NULL));
GO
