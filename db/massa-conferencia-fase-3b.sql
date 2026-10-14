-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 3B — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (tudo chaveado pelo numero do Pedido, CONF-3B):
--   * a marca UtilizaKit = 1 no Setor 'Solda';
--   * o Pedido CONF-3B (cliente 'Conferência da 3B', Fabricacao, Aberto, entrega daqui a 15 dias, autor admin);
--   * o Agrupamento KIT-01, tipo Kit;
--   * a estrutura de tres niveis, sem nenhuma movimentacao (o fluxo e feito na tela):
--
--       A  Peca, 5                 Roteiro: Solda -> Pintura   (Componente MT-1020 do catalogo)
--       |- B  Item, 5, razao 1     Roteiro: Solda
--       |  |- D  Item, 20, razao 4 Roteiro: Corte a Laser
--       |  `- E  Item, 10, razao 2 Roteiro: Dobra
--       `- C  Item, 10, razao 2    Roteiro: Corte a Laser -> Dobra
--
--   Os Itens sao ad-hoc (descricao propria, sem Componente); a Peca usa o Componente MT-1020 (ativo), porque Peca
--   sempre referencia um Componente (CK_EstruturaItem_PecaTemComponente). O solido 3D NAO e exigido: o seed-demo.sql
--   nao cria nenhum, e ele so existe se alguem o enviar pela tela do Componente.
--
-- Pre-requisitos: db/seed.sql (usuario admin), db/seed-demo.sql (Setores Corte a Laser, Dobra, Solda e Pintura
-- e o Componente MT-1020) e db/alter-fase-3b.sql (coluna Setor.UtilizaKit). Falta de qualquer um para o
-- script com erro.
--
-- Idempotente: se o Pedido CONF-3B ja existe, nada e criado de novo (so reafirma a marca da Solda).
-- Para refazer o roteiro do zero, apague o Pedido CONF-3B e o que pende dele (movimentacoes, montagens,
-- estrutura, Agrupamento) ou regenere o banco de dev, que e descartavel.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-3b.sql sqlserver:/tmp/massa-conferencia-fase-3b.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-3b.sql
--
-- NENHUM teste automatizado pode depender deste arquivo.
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UsuarioId   INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'admin');
DECLARE @Solda       INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');
DECLARE @Pintura     INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Pintura');
DECLARE @Corte       INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte a Laser');
DECLARE @Dobra       INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @ComponenteId INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020' AND Ativo = 1);

IF @UsuarioId IS NULL
    THROW 50000, N'Usuario admin nao encontrado: carregue db/seed.sql antes.', 1;
IF @Solda IS NULL OR @Pintura IS NULL OR @Corte IS NULL OR @Dobra IS NULL
    THROW 50000, N'Setores Solda, Pintura, Corte a Laser e Dobra nao encontrados: carregue db/seed-demo.sql antes.', 1;
IF @ComponenteId IS NULL
    THROW 50000, N'Componente MT-1020 ativo nao encontrado: carregue db/seed-demo.sql antes.', 1;

-- A marca do Setor e reafirmada a cada carga (a tela de Setores pode te-la desmarcado).
UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = @Solda AND UtilizaKit = 0;

IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-3B')
BEGIN
    PRINT N'Pedido CONF-3B ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-3B', N'Conferência da 3B', N'Fabricacao', N'Aberto',
            CAST(DATEADD(DAY, 15, SYSUTCDATETIME()) AS DATE), @UsuarioId);
    DECLARE @PedidoId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@PedidoId, N'KIT-01', N'Kit', @UsuarioId);
    DECLARE @AgrupamentoId INT = SCOPE_IDENTITY();

    -- A: Peca (topo), 5 unidades.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoId, @ComponenteId, N'A', NULL, N'Peca', 5, NULL);
    DECLARE @A INT = SCOPE_IDENTITY();

    -- B: Item filho de A, 5 unidades, 1 por A.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoId, NULL, N'B', @A, N'Item', 5, 1);
    DECLARE @B INT = SCOPE_IDENTITY();

    -- C: Item filho de A, 10 unidades, 2 por A.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoId, NULL, N'C', @A, N'Item', 10, 2);
    DECLARE @C INT = SCOPE_IDENTITY();

    -- D: Item filho de B, 20 unidades, 4 por B.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoId, NULL, N'D', @B, N'Item', 20, 4);
    DECLARE @D INT = SCOPE_IDENTITY();

    -- E: Item filho de B, 10 unidades, 2 por B.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoId, NULL, N'E', @B, N'Item', 10, 2);
    DECLARE @E INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@A, @Solda,   1),
        (@A, @Pintura, 2),
        (@B, @Solda,   1),
        (@C, @Corte,   1),
        (@C, @Dobra,   2),
        (@D, @Corte,   1),
        (@E, @Dobra,   1);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-3B criado.';
END
