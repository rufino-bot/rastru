-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 4 — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (chaveado por nome de Setor, codigo de Material e numero de Pedido, CONF-4 e CONF-4-ATR):
--   * os Setores 'Corte', 'Solda' e 'Expedição' (os que ja existem sao reaproveitados; a Solda leva a marca
--     UtilizaKit = 1);
--   * os Materiais:
--       CONF4-PAR-M8  parafuso, UN, no almoxarifado   (entra na lista do Separador)
--       CONF4-ELET    eletrodo, KG, no almoxarifado   (para a quantidade fracionada: 0,5 KG)
--       CONF4-CH3     chapa 3 mm, UN, fica no Corte   (NAO entra na lista do Separador)
--   * o Pedido CONF-4, com entrega amanha (cliente 'Conferência da Fase 4', Fabricacao, Aberto, autor admin),
--     com o Agrupamento KIT-01 (tipo Kit):
--
--       Kit  Peca, 2             Roteiro: Solda       planejado: CONF4-PAR-M8 8, CONF4-ELET 0,5
--       |- Chapa A  Item, 2, razao 1   Roteiro: Corte  planejado: CONF4-CH3 2
--       `- Chapa B  Item, 4, razao 2   Roteiro: Corte  planejado: CONF4-CH3 4
--
--   * o Pedido CONF-4-ATR, com entrega ontem (a lista do Separador o traz primeiro e o marca Atrasado), com o
--     Agrupamento AVULSO-01 (tipo Avulso):
--
--       Suporte  Peca, 10        Roteiro: Expedição   planejado: CONF4-PAR-M8 4
--
--   Nenhuma separacao nem movimentacao e criada: o fluxo e feito na tela. Na lista do Separador (GET
--   /separacoes/pendencias) aparecem o Kit e o Suporte; as chapas nao, porque o CONF4-CH3 fica no Corte.
--   A Peca usa o Componente MT-1020 (ativo), porque Peca sempre referencia um Componente
--   (CK_EstruturaItem_PecaTemComponente). Os Itens sao ad-hoc (descricao propria, sem Componente).
--
-- Pre-requisitos: db/seed.sql (usuario admin), db/seed-demo.sql (o Componente MT-1020 e o Setor Solda),
-- db/alter-fase-3b.sql (Setor.UtilizaKit) e db/alter-fase-4.sql (Material.SetorDeEstoqueId e o livro
-- MaterialSeparacao). Falta de qualquer um para o script com erro.
--
-- Idempotente: Setor e Material existentes sao reaproveitados e NAO sao alterados (a tela de Materiais pode ter
-- mudado o "Fica em"; so a marca UtilizaKit da Solda e reafirmada); se um Pedido ja existe, nada e criado de novo
-- para ele. Para refazer a estrutura do zero, apague os dois Pedidos e o que pende deles (separacoes,
-- movimentacoes, estrutura, Agrupamentos) ou regenere o banco de dev, que e descartavel.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-4.sql sqlserver:/tmp/massa-conferencia-fase-4.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-4.sql
--
-- NENHUM teste automatizado pode depender deste arquivo.
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @UsuarioId    INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'admin');
DECLARE @ComponenteId INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020' AND Ativo = 1);

IF @UsuarioId IS NULL
    THROW 50000, N'Usuario admin nao encontrado: carregue db/seed.sql antes.', 1;
IF @ComponenteId IS NULL
    THROW 50000, N'Componente MT-1020 ativo nao encontrado: carregue db/seed-demo.sql antes.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda')
    THROW 50000, N'Setor Solda nao encontrado: carregue db/seed-demo.sql antes.', 1;
IF COL_LENGTH('dbo.Material', 'SetorDeEstoqueId') IS NULL
    THROW 50000, N'Coluna Material.SetorDeEstoqueId ausente: aplique db/alter-fase-4.sql antes.', 1;

-- Setores: so cria o que falta. A marca da Solda e reafirmada a cada carga.
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte')      INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Expedição')  INSERT INTO dbo.Setor (Nome) VALUES (N'Expedição');
UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Nome = N'Solda' AND UtilizaKit = 0;

DECLARE @Corte     INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');
DECLARE @Solda     INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');
DECLARE @Expedicao INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Expedição');

-- Materiais: so cria o que falta. NULL em SetorDeEstoqueId = almoxarifado; a chapa fica no Corte.
IF NOT EXISTS (SELECT 1 FROM dbo.Material WHERE Codigo = N'CONF4-PAR-M8')
    INSERT INTO dbo.Material (Codigo, Descricao, UnidadeMedida, SetorDeEstoqueId)
    VALUES (N'CONF4-PAR-M8', N'Parafuso sextavado M8', N'UN', NULL);
IF NOT EXISTS (SELECT 1 FROM dbo.Material WHERE Codigo = N'CONF4-ELET')
    INSERT INTO dbo.Material (Codigo, Descricao, UnidadeMedida, SetorDeEstoqueId)
    VALUES (N'CONF4-ELET', N'Eletrodo de solda', N'KG', NULL);
IF NOT EXISTS (SELECT 1 FROM dbo.Material WHERE Codigo = N'CONF4-CH3')
    INSERT INTO dbo.Material (Codigo, Descricao, UnidadeMedida, SetorDeEstoqueId)
    VALUES (N'CONF4-CH3', N'Chapa de aço 3 mm', N'UN', @Corte);

DECLARE @Parafuso INT = (SELECT Id FROM dbo.Material WHERE Codigo = N'CONF4-PAR-M8');
DECLARE @Eletrodo INT = (SELECT Id FROM dbo.Material WHERE Codigo = N'CONF4-ELET');
DECLARE @Chapa    INT = (SELECT Id FROM dbo.Material WHERE Codigo = N'CONF4-CH3');

-- Entrega em dia de Brasilia (UTC-3, sem horario de verao): o servidor de dev roda em UTC.
DECLARE @Hoje DATE = CAST(DATEADD(HOUR, -3, SYSUTCDATETIME()) AS DATE);

-- ---------------------------------------------------------------------
-- CONF-4: o Kit e as duas chapas.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-4')
BEGIN
    PRINT N'Pedido CONF-4 ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-4', N'Conferência da Fase 4', N'Fabricacao', N'Aberto', DATEADD(DAY, 1, @Hoje), @UsuarioId);
    DECLARE @PedidoKit INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@PedidoKit, N'KIT-01', N'Kit', @UsuarioId);
    DECLARE @AgrupamentoKit INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoKit, @ComponenteId, N'Kit', NULL, N'Peca', 2, NULL);
    DECLARE @Kit INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoKit, NULL, N'Chapa A', @Kit, N'Item', 2, 1);
    DECLARE @ChapaA INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoKit, NULL, N'Chapa B', @Kit, N'Item', 4, 2);
    DECLARE @ChapaB INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Kit,    @Solda, 1),
        (@ChapaA, @Corte, 1),
        (@ChapaB, @Corte, 1);

    INSERT INTO dbo.EstruturaMaterial (EstruturaItemId, MaterialId, Quantidade) VALUES
        (@Kit,    @Parafuso, 8),
        (@Kit,    @Eletrodo, 0.5),
        (@ChapaA, @Chapa,    2),
        (@ChapaB, @Chapa,    4);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-4 criado.';
END

-- ---------------------------------------------------------------------
-- CONF-4-ATR: o Pedido atrasado, com a Peca que so passa pela Expedicao.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-4-ATR')
BEGIN
    PRINT N'Pedido CONF-4-ATR ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-4-ATR', N'Conferência da Fase 4', N'Fabricacao', N'Aberto', DATEADD(DAY, -1, @Hoje), @UsuarioId);
    DECLARE @PedidoAtrasado INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@PedidoAtrasado, N'AVULSO-01', N'Avulso', @UsuarioId);
    DECLARE @AgrupamentoAvulso INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai)
    VALUES (@AgrupamentoAvulso, @ComponenteId, N'Suporte', NULL, N'Peca', 10, NULL);
    DECLARE @Suporte INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@Suporte, @Expedicao, 1);
    INSERT INTO dbo.EstruturaMaterial (EstruturaItemId, MaterialId, Quantidade) VALUES (@Suporte, @Parafuso, 4);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-4-ATR criado.';
END
