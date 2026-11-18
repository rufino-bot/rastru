-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DO PANORAMA — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (chaveado por nome de Setor e por numero de Pedido; todo numero comeca por CONF-PAN; cliente
-- 'Conferência do Panorama', autor admin; entrega 2099-12-31, salvo o Pedido atrasado, que nunca atrasa os outros):
--   * os Setores 'Corte', 'Dobra' e 'Solda' (os que ja existem sao reaproveitados e NAO sao alterados).
--   * todos os nos sao copiados do catalogo, SEM descricao propria (o rotulo vem do Componente). Agrupamentos Avulso.
--   * NENHUMA linha de dbo.Embalagem: quem marca o "Embalado" e o usuario, na tela.
--
--   CONF-PAN-DIV (EmProducao): a Peca dividida em dois Setores, mais algo a iniciar.
--       MT-1010  Peca, 10     Roteiro: Corte(1), Solda(2)
--       ANTES: "2 a iniciar · 3 Corte, 1 aguardando coleta · 4 Solda" (3 em trabalho e 1 aguardando coleta no passo 1;
--       4 no passo 2). Embalado: desabilitado (ha producao).
--
--   CONF-PAN-PAUSA (EmProducao, com pausa aberta, motivo "Alteração de projeto"):
--       CH-2110  Peca, 6      Roteiro: Corte(1)
--       ANTES: "2 a iniciar · 3 Corte, 1 aguardando coleta", pilula "Pausado" (ambar), com o motivo
--       "Alteração de projeto" logo abaixo, em texto pequeno, fora da pilula.
--       A pausa e do Pedido inteiro. O Pedido nao e atrasado.
--
--   CONF-PAN-EXP (AguardandoExpedicao): duas Pecas prontas para o "Embalado", ambas no Roteiro Corte(1).
--       CH-2200  Peca, 10, exige relatorio, afericao Conforme (uma linha RelatorioDimensional): 10 na Expedicao.
--       CH-2160  Peca,  8, sem relatorio: 5 ja expedidas (uma Remessa) e 3 no local de expedicao.
--       ANTES: "10 Expedição" (aferição "Conforme", positiva) e "3 Expedição · 5 expedidas". Embalado: a caixa
--       aparece HABILITADA para Qualidade, PCP e Administrador (nada em producao, ao menos uma no local); Gestao
--       ve o estado sem a caixa. Marque, recarregue (a marca fica), e desmarque.
--       Para ver o "some sozinho" (D5): marque a CH-2200 e depois, na Expedicao, faca a contagem com uma nao
--       conforme e o "Mandar para conserto": a unidade vai a producao e a marca sai sozinha (a regra e calculada na
--       leitura); quando ela voltar ao local, a marca NAO volta, e marcar de novo grava outra. O contrario tambem vale
--       conferir: marque a CH-2160 e estorne a remessa dela; as 5 voltam ao local de expedicao, que nao e producao, e
--       a marca continua valendo.
--
--   CONF-PAN-ITENS (EmProducao): a Peca montada, com Itens (e um neto) em tres Setores.
--       MT-1020  Peca, 4      Roteiro: Solda(1)          4 a iniciar
--        +- EX-2210  Item, 8 (2 por pai)  Corte(1)       3 em trabalho no Corte
--        |   +- PF-3020  Item (neto), 16 (2 por pai)  Solda(1)   4 em trabalho na Solda
--        +- CH-2130  Item, 4 (1 por pai)  Dobra(1)       2 aguardando coleta na Dobra
--       ANTES: "4 a iniciar" e "Itens em: Corte, Dobra, Solda" (ordem alfabetica, sem repetir). O filtro de Setor
--       "Solda" acha esta Peca pelo neto, embora a propria Peca nao tenha unidade la. Os Itens avancaram sem o pai
--       ter iniciado: o livro admite (e so o app que consome os filhos no Inicio do pai), e e assim que o Panorama
--       ve os Itens de uma Peca ainda a iniciar.
--
--   CONF-PAN-ATRASADO (EmProducao, entrega 10 dias atras):
--       CH-2170  Peca, 5, exige relatorio, Roteiro: Corte(1)    2 a iniciar · 3 Corte; "Atrasado" (roxo), sem afericao.
--
--   O QUE FICOU DE FORA (simplificacao declarada): nenhuma Peca em conserto ("1 em conserto na Solda"): chegar la exige
--   ContagemDimensional + Medicao + Conserto + Entrega, e o app grava isso pela tela da Expedicao; para ver o segmento
--   faca na Expedicao, sobre a CH-2200 de CONF-PAN-EXP, a contagem com uma nao conforme e o "Mandar para conserto".
--   Cada linha de dbo.Movimentacao abaixo tem a forma que o app grava (Inicio, Termino, Entrega, Expedicao), e a
--   conservacao da regra 9 fecha em TODO no. Inicio e Termino pelo `operador`, Entrega pelo `movimentador`,
--   afericao e expedicao pela `qualidade`.
--
-- Pre-requisitos: db/seed.sql (admin, operador, movimentador e qualidade), db/seed-demo.sql (os Componentes MT-1010,
-- CH-2110, CH-2200, CH-2160, MT-1020, EX-2210, PF-3020, CH-2130 e CH-2170), o schema atual e db/alter-panorama.sql.
-- Falta de qualquer um para o script com erro.
--
-- Idempotente: Setor existente e reaproveitado; se um Pedido ja existe, nada e criado de novo para ele. Para refazer do
-- zero, apague os Pedidos CONF-PAN* e tudo o que pende deles, ou regenere o banco de dev (descartavel).
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-panorama.sql sqlserver:/tmp/massa-conferencia-panorama.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-panorama.sql
--
-- NENHUM teste automatizado pode depender deste arquivo.
-- =====================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Admin        INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'admin');
DECLARE @Operador     INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'operador');
DECLARE @Movimentador INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'movimentador');
DECLARE @Qualidade    INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'qualidade');

IF @Admin IS NULL OR @Operador IS NULL OR @Movimentador IS NULL OR @Qualidade IS NULL
    THROW 50000, N'Usuarios admin, operador, movimentador e qualidade nao encontrados: carregue db/seed.sql antes.', 1;
IF OBJECT_ID('dbo.Embalagem') IS NULL
    THROW 50000, N'Tabela dbo.Embalagem ausente: aplique db/alter-panorama.sql antes.', 1;

DECLARE @CMt1010 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1010' AND Ativo = 1);
DECLARE @CCh2110 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2110' AND Ativo = 1);
DECLARE @CCh2200 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2200' AND Ativo = 1);
DECLARE @CCh2160 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2160' AND Ativo = 1);
DECLARE @CMt1020 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020' AND Ativo = 1);
DECLARE @CEx2210 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'EX-2210' AND Ativo = 1);
DECLARE @CPf3020 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3020' AND Ativo = 1);
DECLARE @CCh2130 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2130' AND Ativo = 1);
DECLARE @CCh2170 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2170' AND Ativo = 1);

IF @CMt1010 IS NULL OR @CCh2110 IS NULL OR @CCh2200 IS NULL OR @CCh2160 IS NULL OR @CMt1020 IS NULL
   OR @CEx2210 IS NULL OR @CPf3020 IS NULL OR @CCh2130 IS NULL OR @CCh2170 IS NULL
    THROW 50000, N'Componentes MT-1010, CH-2110, CH-2200, CH-2160, MT-1020, EX-2210, PF-3020, CH-2130 e CH-2170 ativos nao encontrados: carregue db/seed-demo.sql antes.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte') INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Dobra') INSERT INTO dbo.Setor (Nome) VALUES (N'Dobra');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda') INSERT INTO dbo.Setor (Nome) VALUES (N'Solda');

DECLARE @Corte INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');
DECLARE @Dobra INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @Solda INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');

-- Entrega em dia de Brasilia (UTC-3, sem horario de verao): o servidor de dev roda em UTC.
DECLARE @Hoje DATE = CAST(DATEADD(HOUR, -3, SYSUTCDATETIME()) AS DATE);

-- ---------------------------------------------------------------------
-- CONF-PAN-DIV: 10 = 2 a iniciar | 3 Corte | 1 aguardando coleta (Corte) | 4 Solda.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-PAN-DIV')
BEGIN
    PRINT N'Pedido CONF-PAN-DIV ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-PAN-DIV', N'Conferência do Panorama', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedDiv INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedDiv, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrDiv INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrDiv, @CMt1010, NULL, NULL, N'Peca', 10, NULL, 0);
    DECLARE @NoDiv INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@NoDiv, @Corte, 1), (@NoDiv, @Solda, 2);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@NoDiv, N'Inicio',  8, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1, @Operador),
        (@NoDiv, N'Termino', 5, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1, @Operador),
        (@NoDiv, N'Entrega', 4, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Solda, 2, @Movimentador);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-PAN-DIV criado.';
END

-- ---------------------------------------------------------------------
-- CONF-PAN-PAUSA: 6 = 2 a iniciar | 3 Corte | 1 aguardando coleta (Corte); Pedido pausado.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-PAN-PAUSA')
BEGIN
    PRINT N'Pedido CONF-PAN-PAUSA ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-PAN-PAUSA', N'Conferência do Panorama', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedPausa INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedPausa, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrPausa INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrPausa, @CCh2110, NULL, NULL, N'Peca', 6, NULL, 0);
    DECLARE @NoPausa INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@NoPausa, @Corte, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@NoPausa, N'Inicio',  4, N'AIniciar', NULL,   NULL, N'NoSetor',          @Corte, 1, @Operador),
        (@NoPausa, N'Termino', 1, N'NoSetor',  @Corte, 1,    N'AguardandoColeta', @Corte, 1, @Operador);

    INSERT INTO dbo.PedidoPausa (PedidoId, PausadoPorUsuarioId, Motivo)
    VALUES (@PedPausa, @Admin, N'Alteração de projeto');

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-PAN-PAUSA criado.';
END

-- ---------------------------------------------------------------------
-- CONF-PAN-EXP: duas Pecas prontas para embalar.
--   CH-2200: 10 no local de expedicao, afericao Conforme.
--   CH-2160: 8 = 5 expedidas (uma Remessa) + 3 no local de expedicao.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-PAN-EXP')
BEGIN
    PRINT N'Pedido CONF-PAN-EXP ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-PAN-EXP', N'Conferência do Panorama', N'Fabricacao', N'AguardandoExpedicao', '2099-12-31', @Admin);
    DECLARE @PedExp INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedExp, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrExp INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrExp, @CCh2200, NULL, NULL, N'Peca', 10, NULL, 1);
    DECLARE @NoExpA INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrExp, @CCh2160, NULL, NULL, N'Peca', 8, NULL, 0);
    DECLARE @NoExpB INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@NoExpA, @Corte, 1), (@NoExpB, @Corte, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@NoExpA, N'Inicio',  10, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@NoExpA, N'Termino', 10, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@NoExpA, N'Entrega', 10, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador),
        (@NoExpB, N'Inicio',  8,  N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@NoExpB, N'Termino', 8,  N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@NoExpB, N'Entrega', 8,  N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);

    -- A afericao Conforme da CH-2200 (a Peca que exige relatorio).
    INSERT INTO dbo.RelatorioDimensional (EstruturaItemId, Tipo, Resultado, UsuarioId)
    VALUES (@NoExpA, N'Afericao', N'Conforme', @Qualidade);

    -- A remessa que levou 5 da CH-2160 (a posicao de origem e o local de expedicao, sem afericao).
    INSERT INTO dbo.Remessa (UsuarioId) VALUES (@Qualidade);
    DECLARE @RemExp INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, RemessaId, UsuarioId)
    VALUES (@NoExpB, N'Expedicao', 5, N'NaExpedicao', N'Expedido', @RemExp, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-PAN-EXP criado.';
END

-- ---------------------------------------------------------------------
-- CONF-PAN-ITENS: a Peca (4 a iniciar) com Itens em Corte, Dobra e Solda.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-PAN-ITENS')
BEGIN
    PRINT N'Pedido CONF-PAN-ITENS ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-PAN-ITENS', N'Conferência do Panorama', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedIt INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedIt, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrIt INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrIt, @CMt1020, NULL, NULL, N'Peca', 4, NULL, 0);
    DECLARE @NoItP INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrIt, @CEx2210, NULL, @NoItP, N'Item', 8, 2, 0);
    DECLARE @NoItF1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrIt, @CCh2130, NULL, @NoItP, N'Item', 4, 1, 0);
    DECLARE @NoItF2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrIt, @CPf3020, NULL, @NoItF1, N'Item', 16, 2, 0);
    DECLARE @NoItN INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@NoItP, @Solda, 1), (@NoItF1, @Corte, 1), (@NoItF2, @Dobra, 1), (@NoItN, @Solda, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@NoItF1, N'Inicio',  3, N'AIniciar', NULL,   NULL, N'NoSetor',          @Corte, 1, @Operador),
        (@NoItF2, N'Inicio',  2, N'AIniciar', NULL,   NULL, N'NoSetor',          @Dobra, 1, @Operador),
        (@NoItF2, N'Termino', 2, N'NoSetor',  @Dobra, 1,    N'AguardandoColeta', @Dobra, 1, @Operador),
        (@NoItN,  N'Inicio',  4, N'AIniciar', NULL,   NULL, N'NoSetor',          @Solda, 1, @Operador);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-PAN-ITENS criado.';
END

-- ---------------------------------------------------------------------
-- CONF-PAN-ATRASADO: entrega 10 dias atras; 5 = 2 a iniciar | 3 Corte.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-PAN-ATRASADO')
BEGIN
    PRINT N'Pedido CONF-PAN-ATRASADO ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-PAN-ATRASADO', N'Conferência do Panorama', N'Fabricacao', N'EmProducao', DATEADD(DAY, -10, @Hoje), @Admin);
    DECLARE @PedAtr INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedAtr, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrAtr INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrAtr, @CCh2170, NULL, NULL, N'Peca', 5, NULL, 1);
    DECLARE @NoAtr INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@NoAtr, @Corte, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@NoAtr, N'Inicio', 3, N'AIniciar', NULL, NULL, N'NoSetor', @Corte, 1, @Operador);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-PAN-ATRASADO criado.';
END
