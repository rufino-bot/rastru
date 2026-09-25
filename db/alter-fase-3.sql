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
