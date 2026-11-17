-- Migracao idempotente da miniatura do solido para banco criado ANTES dela. A fonte de verdade continua
-- sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: cada bloco confere antes de agir. Nao ha dado a transformar: as duas
-- colunas nascem nulas. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. Componente.ArquivoMiniaturaId (spec da miniatura, C4) ------------------------------------------- */
IF COL_LENGTH('dbo.Componente', 'ArquivoMiniaturaId') IS NULL
    ALTER TABLE dbo.Componente ADD ArquivoMiniaturaId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Componente_ArquivoMiniatura')
    ALTER TABLE dbo.Componente ADD CONSTRAINT FK_Componente_ArquivoMiniatura
        FOREIGN KEY (ArquivoMiniaturaId) REFERENCES dbo.ArquivoDeComponente (Id);
GO

/* 2. ImportacaoDeEstruturaComponente.ArquivoMiniaturaPendenteId (spec da miniatura, C5) -------------- */
IF COL_LENGTH('dbo.ImportacaoDeEstruturaComponente', 'ArquivoMiniaturaPendenteId') IS NULL
    ALTER TABLE dbo.ImportacaoDeEstruturaComponente ADD ArquivoMiniaturaPendenteId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ImportacaoDeEstruturaComponente_ArquivoMiniaturaPendente')
    ALTER TABLE dbo.ImportacaoDeEstruturaComponente ADD CONSTRAINT FK_ImportacaoDeEstruturaComponente_ArquivoMiniaturaPendente
        FOREIGN KEY (ArquivoMiniaturaPendenteId) REFERENCES dbo.ArquivoDeComponente (Id);
GO
