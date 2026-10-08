-- Migracao idempotente da data de entrega do Pedido para banco criado ANTES dela. A fonte de verdade
-- continua sendo specs/02-modelo-de-dados.sql (Database First); este arquivo so leva um banco antigo
-- ate la. Rodar de novo nao muda nada: cada bloco confere antes de agir. Como aplicar: CLAUDE.md,
-- "Comandos" (o mesmo comando do db/alter-fase-3d.sql, com este arquivo).
SET NOCOUNT ON;
GO

/* 1. A coluna nasce nula, para os Pedidos que ja existem (spec da data de entrega, secao 3.2) ------ */
IF COL_LENGTH('dbo.Pedido', 'DataEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD DataEntrega DATE NULL;
GO

/* 2. Preenche os existentes com a data de abertura em Brasilia (GMT-3 fixo). E um valor inventado:
      esses Pedidos nao tem prazo de verdade, e com ele todo Pedido antigo ainda aberto aparece como
      atrasado — o que mostra que o prazo precisa ser revisto. ------------------------------------- */
UPDATE dbo.Pedido
   SET DataEntrega = CAST(DATEADD(HOUR, -3, DataAbertura) AS DATE)
 WHERE DataEntrega IS NULL;
GO

/* 3. NOT NULL, como no 02 --------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Pedido') AND name = 'DataEntrega' AND is_nullable = 1)
    ALTER TABLE dbo.Pedido ALTER COLUMN DataEntrega DATE NOT NULL;
GO
