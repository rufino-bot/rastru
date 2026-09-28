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
