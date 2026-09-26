-- Migracao idempotente da Fase 3 para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. EstruturaItem.QuantidadePorPai (regra 26) ------------------------------------------------ */
IF COL_LENGTH('dbo.EstruturaItem', 'QuantidadePorPai') IS NULL
    ALTER TABLE dbo.EstruturaItem ADD QuantidadePorPai DECIMAL(18,4) NULL;
GO

-- Preenche os Itens que ja existiam com Quantidade / Quantidade do pai (spec da Fase 3, secao 3.4).
-- Aproximacao aceita: o banco de dev e descartavel (autorizacao do dono do projeto, 2026-08-17) e
-- nao ha banco de producao. O piso de 0,0001 impede o CHECK abaixo de recusar uma razao que o
-- arredondamento levaria a zero.
UPDATE f
   SET f.QuantidadePorPai = CASE WHEN f.Quantidade / p.Quantidade < 0.0001 THEN 0.0001
                                 ELSE CAST(f.Quantidade / p.Quantidade AS DECIMAL(18,4)) END
  FROM dbo.EstruturaItem f
  JOIN dbo.EstruturaItem p ON p.Id = f.EstruturaPaiId
 WHERE f.NivelHierarquico = 'Item' AND f.QuantidadePorPai IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EstruturaItem_QuantidadePorPai')
    ALTER TABLE dbo.EstruturaItem ADD CONSTRAINT CK_EstruturaItem_QuantidadePorPai
        CHECK ((NivelHierarquico = 'Peca' AND QuantidadePorPai IS NULL)
            OR (NivelHierarquico = 'Item' AND QuantidadePorPai IS NOT NULL AND QuantidadePorPai > 0));
GO

/* 2. Livro de movimentacoes (spec da Fase 3, secoes 3.2 e 3.3) --------------------------------- */
-- dbo.EstruturaSetorHistorico nunca teve codigo que a usasse; sai com os dois indices dela.
IF OBJECT_ID('dbo.EstruturaSetorHistorico') IS NOT NULL
    DROP TABLE dbo.EstruturaSetorHistorico;
GO

-- Registro de "montei N" de um nó com filhos (regra 24). O total montado do nó é a soma de
-- Quantidade das montagens não estornadas. A baixa de CADA filho fica em dbo.Movimentacao
-- (Tipo = 'Montagem', MontagemId = esta linha), com N × QuantidadePorPai gravado: editar a
-- razão depois não reescreve o passado.
IF OBJECT_ID('dbo.Montagem') IS NULL
BEGIN
    CREATE TABLE dbo.Montagem (
        Id                     INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId        INT                 NOT NULL, -- o pai montado (nó com filhos)
        SetorId                INT                 NOT NULL, -- onde foi montado
        Quantidade             DECIMAL(18,4)       NOT NULL, -- N unidades do pai
        DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Montagem_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId              INT                 NOT NULL,
        EstornadaEm            DATETIME2           NULL,     -- NULL = vale; preenchida = estornada (spec da Fase 3, seção 4.5)
        EstornadaPorUsuarioId  INT                 NULL,
        CONSTRAINT PK_Montagem PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Montagem_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Montagem_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Montagem_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT FK_Montagem_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_Montagem_QuantidadePositiva CHECK (Quantidade > 0),
        CONSTRAINT CK_Montagem_EstornoCompleto
            CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
                OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL)),
        CONSTRAINT CK_Montagem_EstornoAposMontagem CHECK (EstornadaEm IS NULL OR EstornadaEm >= DataHora)
    );
END;
GO

-- Livro de movimentações: cada linha move Quantidade de um nó de uma posição para outra.
-- SÓ INSERÇÃO: não se edita nem se apaga; correção é um Estorno (movimento inverso que aponta o
-- original). Saldo de uma posição = Σ Quantidade onde ela é destino − Σ onde ela é origem;
-- AIniciar = EstruturaItem.Quantidade − Σ onde ela é origem + Σ onde ela é destino (estorno).
-- Conservação (regra 9) por construção: todo movimento tira de uma posição e põe em outra.
IF OBJECT_ID('dbo.Movimentacao') IS NULL
BEGIN
    CREATE TABLE dbo.Movimentacao (
        Id               INT IDENTITY(1,1)  NOT NULL,
        EstruturaItemId  INT                 NOT NULL,
        Tipo             NVARCHAR(20)        NOT NULL, -- Inicio | Termino | Entrega | Montagem | Estorno
        Quantidade       DECIMAL(18,4)       NOT NULL,
        OrigemPosicao    NVARCHAR(20)        NOT NULL,
        OrigemSetorId    INT                 NULL,
        OrigemOrdem      INT                 NULL,     -- passo do Roteiro do próprio nó
        DestinoPosicao   NVARCHAR(20)        NOT NULL,
        DestinoSetorId   INT                 NULL,
        DestinoOrdem     INT                 NULL,
        MontagemId       INT                 NULL,     -- baixa de filho (e o estorno dela)
        EstornoDeId      INT                 NULL,     -- só no Estorno: o movimento que ele desfaz
        DataHora         DATETIME2           NOT NULL CONSTRAINT DF_Movimentacao_DataHora DEFAULT (SYSUTCDATETIME()),
        UsuarioId        INT                 NOT NULL, -- autor
        CONSTRAINT PK_Movimentacao PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Movimentacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
        CONSTRAINT FK_Movimentacao_OrigemSetor FOREIGN KEY (OrigemSetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Movimentacao_DestinoSetor FOREIGN KEY (DestinoSetorId) REFERENCES dbo.Setor (Id),
        CONSTRAINT FK_Movimentacao_Montagem FOREIGN KEY (MontagemId) REFERENCES dbo.Montagem (Id),
        CONSTRAINT FK_Movimentacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.Movimentacao (Id),
        CONSTRAINT FK_Movimentacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
        CONSTRAINT CK_Movimentacao_QuantidadePositiva CHECK (Quantidade > 0),
        CONSTRAINT CK_Movimentacao_Tipo
            CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno')),
        CONSTRAINT CK_Movimentacao_OrigemPosicao
            CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
        CONSTRAINT CK_Movimentacao_DestinoPosicao
            CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
        -- Setor e passo combinam com a posição (tabela da seção 3.1 da spec da Fase 3)
        CONSTRAINT CK_Movimentacao_OrigemCoerente
            CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
                OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
                OR (OrigemPosicao = 'AguardandoMontagem' AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL)),
        CONSTRAINT CK_Movimentacao_DestinoCoerente
            CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
                OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
                OR (DestinoPosicao = 'AguardandoMontagem' AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL)),
        -- Cada tipo só faz as transições dele; o Estorno é o inverso de um dos outros
        CONSTRAINT CK_Movimentacao_Transicao
            CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
                OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                                      AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
                OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                                      AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
                OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                                      AND DestinoPosicao = 'AguardandoMontagem')
                OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
                OR (Tipo = 'Estorno')),
        CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
            CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
                OR (Tipo = 'Estorno')
                OR (Tipo NOT IN ('Montagem', 'Estorno') AND MontagemId IS NULL)),
        CONSTRAINT CK_Movimentacao_EstornoApontaOriginal
            CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
                OR (Tipo <> 'Estorno' AND EstornoDeId IS NULL))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_EstruturaItem' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_EstruturaItem ON dbo.Movimentacao (EstruturaItemId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_DestinoSetor' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_DestinoSetor ON dbo.Movimentacao (DestinoSetorId) WHERE DestinoSetorId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Movimentacao_OrigemSetor' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE INDEX IX_Movimentacao_OrigemSetor ON dbo.Movimentacao (OrigemSetorId) WHERE OrigemSetorId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Movimentacao_EstornoDe' AND object_id = OBJECT_ID('dbo.Movimentacao'))
    CREATE UNIQUE INDEX UX_Movimentacao_EstornoDe ON dbo.Movimentacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Montagem_EstruturaItem' AND object_id = OBJECT_ID('dbo.Montagem'))
    CREATE INDEX IX_Montagem_EstruturaItem ON dbo.Montagem (EstruturaItemId);
GO

/* 3. Perfil Movimentador (mesmo MERGE de db/seed.sql, para quem nao roda o seed de novo) -------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Perfil WHERE Nome = 'Movimentador')
    INSERT INTO dbo.Perfil (Nome) VALUES ('Movimentador');
GO
