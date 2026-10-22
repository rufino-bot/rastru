-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 5A — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (chaveado por nome de Setor e por numero de Pedido, CONF-5A e CONF-5A-ATR):
--   * os Setores 'Corte', 'Solda' e 'Expedição' (os que ja existem sao reaproveitados e NAO sao alterados): sao
--     os destinos do conserto (a Qualidade escolhe qualquer Setor ativo, inclusive a Expedicao; C19);
--   * o Pedido CONF-5A, com entrega depois de amanha (cliente 'Conferência da Fase 5A', Fabricacao, autor admin),
--     com DOIS Agrupamentos e quatro Pecas, todas ja no local de expedicao (posicao NaExpedicao):
--
--       AVULSO-01 (Avulso)
--         Placa de identificacao   CH-2200, 10, SEM relatorio          -> "Sem relatorio"
--         Chapa de fechamento      CH-2110,  6, exige relatorio        -> "Afericao pendente" (o fluxo conforme)
--         CH-2150 (do catalogo)    CH-2150,  8, exige relatorio        -> descricao PROPRIA nula: o rotulo vem do
--                                                                         Componente (o fluxo nao conforme)
--       AVULSO-02 (Avulso)        <- outro Agrupamento do mesmo Pedido
--         Reforco do suporte       CH-2160,  4, SEM relatorio
--
--   * o Pedido CONF-5A-ATR, com entrega ontem (a lista da Expedicao o marca Atrasado), com o Agrupamento
--     AVULSO-01 e uma Peca:
--
--         Guia de alinhamento      CH-2170,  5, exige relatorio
--
--   Todas as Pecas tem Roteiro de um passo, no Corte, e o livro (dbo.Movimentacao) ja registra o caminho inteiro
--   ate o local de expedicao: Inicio e Termino pelo `operador`, Entrega pelo `movimentador`. Por isso os dois
--   Pedidos nascem 'AguardandoExpedicao' (C11: todo o nao expedido de cada Peca esta no local de expedicao) e a
--   conservacao da regra 9 fecha em cada no (quantidade total = no local de expedicao). Nenhuma afericao, contagem,
--   liberacao ou remessa e criada: o fluxo da spec e feito na tela, com o roteiro da conferencia.
--
--   A Peca "CH-2150 (do catalogo)" e a copiada do catalogo SEM descricao propria (EstruturaItem.Descricao = NULL):
--   o nome dela em lote e em recusa vem do Componente (a licao da Fase 4). Uma Peca sempre referencia um
--   Componente (CK_EstruturaItem_PecaTemComponente), e as cinco usam Componentes ativos do seed-demo.
--
-- Pre-requisitos: db/seed.sql (os usuarios admin, operador e movimentador), db/seed-demo.sql (os Componentes
-- CH-2110, CH-2150, CH-2160, CH-2170 e CH-2200) e db/alter-fase-5a.sql (o livro com as posicoes do fim do caminho).
-- Falta de qualquer um para o script com erro.
--
-- Idempotente: Setor existente e reaproveitado; se um Pedido ja existe, nada e criado de novo para ele (e nada
-- que a conferencia tenha feito nele e tocado). Para refazer do zero, rode o zerar-conf-5a.sql do ledger da fase
-- (ele apaga os dois Pedidos e tudo o que pende deles) e depois este arquivo, ou regenere o banco de dev, que e
-- descartavel.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-5a.sql sqlserver:/tmp/massa-conferencia-fase-5a.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-5a.sql
--
-- NENHUM teste automatizado pode depender deste arquivo.
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Admin        INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'admin');
DECLARE @Operador     INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'operador');
DECLARE @Movimentador INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'movimentador');

IF @Admin IS NULL OR @Operador IS NULL OR @Movimentador IS NULL
    THROW 50000, N'Usuarios admin, operador e movimentador nao encontrados: carregue db/seed.sql antes.', 1;
IF OBJECT_ID('dbo.Remessa') IS NULL
    THROW 50000, N'Tabela dbo.Remessa ausente: aplique db/alter-fase-5a.sql antes.', 1;

DECLARE @CompPlaca  INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2200' AND Ativo = 1);
DECLARE @CompChapa  INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2110' AND Ativo = 1);
DECLARE @CompBase   INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2150' AND Ativo = 1);
DECLARE @CompReforco INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2160' AND Ativo = 1);
DECLARE @CompGuia   INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2170' AND Ativo = 1);

IF @CompPlaca IS NULL OR @CompChapa IS NULL OR @CompBase IS NULL OR @CompReforco IS NULL OR @CompGuia IS NULL
    THROW 50000, N'Componentes CH-2110, CH-2150, CH-2160, CH-2170 e CH-2200 ativos nao encontrados: carregue db/seed-demo.sql antes.', 1;

-- Setores: so cria o que falta, e nao altera nenhum.
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte')     INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda')     INSERT INTO dbo.Setor (Nome) VALUES (N'Solda');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Expedição') INSERT INTO dbo.Setor (Nome) VALUES (N'Expedição');

DECLARE @Corte INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');

-- Entrega em dia de Brasilia (UTC-3, sem horario de verao): o servidor de dev roda em UTC.
DECLARE @Hoje DATE = CAST(DATEADD(HOUR, -3, SYSUTCDATETIME()) AS DATE);

-- Cada Peca chega ao local de expedicao pelo caminho do livro: Inicio (a iniciar -> no Setor), Termino (no Setor ->
-- aguardando coleta) e Entrega (aguardando coleta -> local de expedicao), sempre com a quantidade inteira, no passo 1
-- do Roteiro, no Corte. As tres linhas satisfazem CK_Movimentacao_Transicao e as de coerencia de Setor e passo.

-- ---------------------------------------------------------------------
-- CONF-5A: dois Agrupamentos e quatro Pecas.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5A')
BEGIN
    PRINT N'Pedido CONF-5A ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5A', N'Conferência da Fase 5A', N'Fabricacao', N'AguardandoExpedicao', DATEADD(DAY, 2, @Hoje), @Admin);
    DECLARE @Pedido INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @Agrupamento1 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@Pedido, N'AVULSO-02', N'Avulso', @Admin);
    DECLARE @Agrupamento2 INT = SCOPE_IDENTITY();

    -- Peca sem relatorio.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agrupamento1, @CompPlaca, N'Placa de identificação', NULL, N'Peca', 10, NULL, 0);
    DECLARE @Placa INT = SCOPE_IDENTITY();

    -- Peca que exige relatorio (o fluxo conforme).
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agrupamento1, @CompChapa, N'Chapa de fechamento', NULL, N'Peca', 6, NULL, 1);
    DECLARE @Chapa INT = SCOPE_IDENTITY();

    -- Peca copiada do catalogo, SEM descricao propria (Descricao NULL), que exige relatorio (o fluxo nao conforme).
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agrupamento1, @CompBase, NULL, NULL, N'Peca', 8, NULL, 1);
    DECLARE @Base INT = SCOPE_IDENTITY();

    -- Peca do outro Agrupamento, sem relatorio.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agrupamento2, @CompReforco, N'Reforço do suporte', NULL, N'Peca', 4, NULL, 0);
    DECLARE @Reforco INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Placa, @Corte, 1), (@Chapa, @Corte, 1), (@Base, @Corte, 1), (@Reforco, @Corte, 1);

    -- O caminho de cada Peca ate o local de expedicao, no livro.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT n.Id, N'Inicio', n.Quantidade, N'AIniciar', NULL, NULL, N'NoSetor', @Corte, 1, @Operador
    FROM dbo.EstruturaItem n WHERE n.Id IN (@Placa, @Chapa, @Base, @Reforco);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT n.Id, N'Termino', n.Quantidade, N'NoSetor', @Corte, 1, N'AguardandoColeta', @Corte, 1, @Operador
    FROM dbo.EstruturaItem n WHERE n.Id IN (@Placa, @Chapa, @Base, @Reforco);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT n.Id, N'Entrega', n.Quantidade, N'AguardandoColeta', @Corte, 1, N'NaExpedicao', NULL, NULL, @Movimentador
    FROM dbo.EstruturaItem n WHERE n.Id IN (@Placa, @Chapa, @Base, @Reforco);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5A criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5A-ATR: o Pedido atrasado, com uma Peca que exige relatorio.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5A-ATR')
BEGIN
    PRINT N'Pedido CONF-5A-ATR ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5A-ATR', N'Conferência da Fase 5A', N'Fabricacao', N'AguardandoExpedicao', DATEADD(DAY, -1, @Hoje), @Admin);
    DECLARE @PedidoAtrasado INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId)
    VALUES (@PedidoAtrasado, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrupamentoAtrasado INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico,
                                   Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrupamentoAtrasado, @CompGuia, N'Guia de alinhamento', NULL, N'Peca', 5, NULL, 1);
    DECLARE @Guia INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@Guia, @Corte, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@Guia, N'Inicio',  5, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@Guia, N'Termino', 5, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@Guia, N'Entrega', 5, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5A-ATR criado.';
END
