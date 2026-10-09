-- Migracao idempotente da Fase 3B para banco criado ANTES dela. A fonte de verdade continua sendo
-- specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo ate la.
-- Rodar de novo nao muda nada: o bloco confere antes de agir. Nao ha dado a transformar: todo Setor
-- existente nasce sem UtilizaKit. Como aplicar: CLAUDE.md, "Comandos".
SET NOCOUNT ON;
GO

/* 1. Setor.UtilizaKit (spec da Fase 3B, secao 3) ----------------------------------------------------- */
IF COL_LENGTH('dbo.Setor', 'UtilizaKit') IS NULL
    ALTER TABLE dbo.Setor ADD UtilizaKit BIT NOT NULL CONSTRAINT DF_Setor_UtilizaKit DEFAULT (0);
GO
