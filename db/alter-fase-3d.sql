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
