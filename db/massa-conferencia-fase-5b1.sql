-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 5B-1 (perda e descarte) — conveniencia de verificacao manual,
-- NAO requisito.
--
-- O que cria (chaveado por nome de Setor e por numero de Pedido, CONF-5B1 e CONF-5B1-DESC):
--   * os Setores 'Corte', 'Dobra', 'Solda' e 'Expedição' (os que ja existem sao reaproveitados e NAO sao alterados);
--   * o Pedido CONF-5B1 (cliente 'Conferência da Fase 5B-1', Fabricacao, autor admin, entrega 2099-12-31: nunca
--     atrasa), status EmProducao, com DOIS Agrupamentos:
--
--     AVULSO-01  Peca de TRES NIVEIS, copiada do catalogo (todos os nos SEM descricao propria: EstruturaItem.Descricao
--                = NULL, o nome vem do Componente — a licao da Fase 4), com QuantidadePorPai > 1:
--
--       MT-1010  Peca P, 10                          Roteiro: Solda(1)         10 a iniciar
--        +- SP-2050  Item A, 20  (2 por P)           Roteiro: Dobra(1), Solda(2)   20 a iniciar
--        |   +- CH-2120  Item B, 60  (3 por A)       Roteiro: Corte(1), Dobra(2)   LOTE DIVIDIDO:
--        |                                              14 a iniciar | 12 no Corte (passo 1) | 10 aguardando coleta
--        |                                              no Corte | 12 na Dobra (passo 2) | 6 aguardando coleta na
--        |                                              Dobra | 6 aguardando montagem na Dobra
--        +- CH-2110  Item C, 12  (1 por P)           Roteiro: Corte(1)         os 12 aguardando montagem na Solda;
--                                                       o pai precisa de 10, entao 2 sao SOBRA (regra 30) e a Fila
--                                                       da Solda mostra a linha "Sobra" com "Descartar"
--
--       "Dar baixa" em B (parte): varios campos de posicao. Baixa de B por PerdaArmazem/MortaEmProcesso: a prévia traz
--       travaNoTopo (D13/D14, a caixa "Isso impede montar…"). Baixa de P em "a iniciar": a cascata C6 tira de A
--       e B o que o pai deixa de precisar, e o C (ja fisico) fica onde esta e entra em viramSobra.
--
--     AVULSO-02  CH-2150 (do catalogo), Peca, 8, exige relatorio, Roteiro: Corte(1). As 8 estao no local de
--                expedicao; a Qualidade ja aferiu NAO CONFORME e contou 5 conformes + 3 nao conformes (uma
--                ContagemDimensional e as duas linhas Medicao do livro). O cartao da Expedicao mostra
--                naoConforme = 3 e o botao "Matar".
--
--   * o Pedido CONF-5B1-DESC (entrega 2099-12-31, EmProducao), para o "Descartar tudo" do Agrupamento e do Pedido:
--
--     AVULSO-01   CH-2160 Peca 10: 4 a iniciar | 3 no Corte | 3 aguardando coleta no Corte
--                 SP-2310 Peca  6: 3 na Dobra (passo 2) | 3 aguardando coleta na Dobra
--     AVULSO-02   CH-2170 Peca  5: 5 a iniciar
--                 CH-2180 Peca  4: 2 a iniciar | 2 no Corte
--                 CH-2190 Peca  3: 3 no local de expedicao
--
--   Ledger (dbo.Movimentacao) escrito direto, para o estado ser real, com a conservacao da regra 9 fechando em TODO
--   no (a iniciar + nos Setores + aguardando coleta/montagem + local de expedicao + conforme/nao conforme + montado
--   + expedido + perdido = Quantidade). Inicio e Termino pelo `operador`, Entrega pelo `movimentador`, afericao e
--   contagem pelo `qualidade`. Nenhuma Perda, remessa ou liberacao e criada: o fluxo da 5B-1 e feito na tela.
--
-- Pre-requisitos: db/seed.sql (os usuarios admin, operador, movimentador e qualidade), db/seed-demo.sql (os
-- Componentes MT-1010, SP-2050, CH-2120, CH-2110, CH-2150, CH-2160, SP-2310, CH-2170, CH-2180 e CH-2190) e o schema
-- atual (specs/02-modelo-de-dados.sql, ou db/alter-fase-5b1.sql num banco anterior). Falta de qualquer um para o
-- script com erro.
--
-- Idempotente: Setor existente e reaproveitado; se um Pedido ja existe, nada e criado de novo para ele (e nada
-- que a conferencia tenha feito nele e tocado). Para refazer do zero, rode o zerar-conf-5b1.sql (so no ledger, NAO
-- versionado: apaga os dois Pedidos e tudo o que pende deles) e depois este arquivo. Quem so tem o repositorio do
-- codigo regenera o banco de dev, que e descartavel: DROP DATABASE, specs/02-modelo-de-dados.sql, db/seed.sql e
-- db/seed-demo.sql, e depois este arquivo.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-5b1.sql sqlserver:/tmp/massa-conferencia-fase-5b1.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-5b1.sql
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
IF COL_LENGTH('dbo.Movimentacao', 'PerdaId') IS NULL
    THROW 50000, N'Coluna Movimentacao.PerdaId ausente: aplique db/alter-fase-5b1.sql antes.', 1;

DECLARE @CompP INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1010' AND Ativo = 1);
DECLARE @CompA INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'SP-2050' AND Ativo = 1);
DECLARE @CompB INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2120' AND Ativo = 1);
DECLARE @CompC INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2110' AND Ativo = 1);
DECLARE @CompN INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2150' AND Ativo = 1);
DECLARE @CompD1 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2160' AND Ativo = 1);
DECLARE @CompD2 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'SP-2310' AND Ativo = 1);
DECLARE @CompD3 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2170' AND Ativo = 1);
DECLARE @CompD4 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2180' AND Ativo = 1);
DECLARE @CompD5 INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2190' AND Ativo = 1);

IF @CompP IS NULL OR @CompA IS NULL OR @CompB IS NULL OR @CompC IS NULL OR @CompN IS NULL
   OR @CompD1 IS NULL OR @CompD2 IS NULL OR @CompD3 IS NULL OR @CompD4 IS NULL OR @CompD5 IS NULL
    THROW 50000, N'Componentes MT-1010, SP-2050, CH-2120, CH-2110, CH-2150, CH-2160, SP-2310, CH-2170, CH-2180 e CH-2190 ativos nao encontrados: carregue db/seed-demo.sql antes.', 1;

-- Setores: so cria o que falta, e nao altera nenhum.
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte')     INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Dobra')     INSERT INTO dbo.Setor (Nome) VALUES (N'Dobra');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda')     INSERT INTO dbo.Setor (Nome) VALUES (N'Solda');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Expedição') INSERT INTO dbo.Setor (Nome) VALUES (N'Expedição');

DECLARE @Corte INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');
DECLARE @Dobra INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @Solda INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');

-- Linhas do livro de cada Pedido: (no, tipo, quantidade, origem, destino, autor). Os Setores e passos vem em
-- colunas; as posicoes sem Setor levam NULL. Inseridas de uma vez no fim de cada Pedido (as constraints
-- CK_Movimentacao_* conferem cada linha: transicao, Setor e passo coerentes com a posicao).
DECLARE @M TABLE (
    No INT, Tipo NVARCHAR(20), Qtd DECIMAL(18,4),
    OP NVARCHAR(20), OS INT, OO INT, DP NVARCHAR(20), DS INT, DOrd INT, Usr INT,
    ContagemId INT NULL);

-- ---------------------------------------------------------------------
-- CONF-5B1: a Peca de tres niveis, o lote dividido, a sobra e a Peca com nao conforme.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B1')
BEGIN
    PRINT N'Pedido CONF-5B1 ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B1', N'Conferência da Fase 5B-1', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @Pedido INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @Agr1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido, N'AVULSO-02', N'Avulso', @Admin);
    DECLARE @Agr2 INT = SCOPE_IDENTITY();

    -- A arvore de tres niveis, sem descricao propria em nenhum no.
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompP, NULL, NULL, N'Peca', 10, NULL, 0);
    DECLARE @P INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompA, NULL, @P, N'Item', 20, 2, 0);
    DECLARE @A INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompB, NULL, @A, N'Item', 60, 3, 0);
    DECLARE @B INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompC, NULL, @P, N'Item', 12, 1, 0);   -- 12 para um pai que precisa de 10: 2 de sobra
    DECLARE @C INT = SCOPE_IDENTITY();

    -- A Peca do nao conforme (exige relatorio).
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr2, @CompN, NULL, NULL, N'Peca', 8, NULL, 1);
    DECLARE @N INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@P, @Solda, 1),
        (@A, @Dobra, 1), (@A, @Solda, 2),
        (@B, @Corte, 1), (@B, @Dobra, 2),
        (@C, @Corte, 1),
        (@N, @Corte, 1);

    -- B (60): Inicio 46 no Corte; Termino 34; Entrega 24 ao passo 2 (Dobra); Termino 12 na Dobra; Entrega 6 a montagem.
    --   saldos: 14 a iniciar | 12 NoSetor Corte1 | 10 coleta Corte1 | 12 NoSetor Dobra2 | 6 coleta Dobra2 | 6 montagem Dobra
    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@B, N'Inicio',  46, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@B, N'Termino', 34, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@B, N'Entrega', 24, N'AguardandoColeta', @Corte, 1,    N'NoSetor',            @Dobra, 2,    @Movimentador),
        (@B, N'Termino', 12, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta',   @Dobra, 2,    @Operador),
        (@B, N'Entrega',  6, N'AguardandoColeta', @Dobra, 2,    N'AguardandoMontagem', @Dobra, NULL, @Movimentador);
    -- C (12): todos entregues a montagem da Solda; o pai precisa de 10 (2 de sobra).
    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@C, N'Inicio',  12, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@C, N'Termino', 12, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@C, N'Entrega', 12, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador);
    -- N (8): do Corte ao local de expedicao.
    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@N, N'Inicio',  8, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@N, N'Termino', 8, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@N, N'Entrega', 8, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M;

    -- A afericao nao conforme e a contagem 5 conformes + 3 nao conformes da Peca N (Qualidade).
    INSERT INTO dbo.RelatorioDimensional (EstruturaItemId, Tipo, Resultado, UsuarioId)
    VALUES (@N, N'Afericao', N'NaoConforme', @Qualidade);
    INSERT INTO dbo.ContagemDimensional (EstruturaItemId, Conformes, NaoConformes, UsuarioId)
    VALUES (@N, 5, 3, @Qualidade);
    DECLARE @Contagem INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, ContagemId, UsuarioId) VALUES
        (@N, N'Medicao', 5, N'NaExpedicao', N'Conforme',    @Contagem, @Qualidade),
        (@N, N'Medicao', 3, N'NaExpedicao', N'NaoConforme', @Contagem, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B1 criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5B1-DESC: dois Agrupamentos, cinco Pecas em varias posicoes, para o "Descartar tudo".
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B1-DESC')
BEGIN
    PRINT N'Pedido CONF-5B1-DESC ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B1-DESC', N'Conferência da Fase 5B-1', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedidoD INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedidoD, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrD1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedidoD, N'AVULSO-02', N'Avulso', @Admin);
    DECLARE @AgrD2 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrD1, @CompD1, NULL, NULL, N'Peca', 10, NULL, 0);
    DECLARE @D1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrD1, @CompD2, NULL, NULL, N'Peca', 6, NULL, 0);
    DECLARE @D2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrD2, @CompD3, NULL, NULL, N'Peca', 5, NULL, 0);
    DECLARE @D3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrD2, @CompD4, NULL, NULL, N'Peca', 4, NULL, 0);
    DECLARE @D4 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrD2, @CompD5, NULL, NULL, N'Peca', 3, NULL, 0);
    DECLARE @D5 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@D1, @Corte, 1), (@D2, @Corte, 1), (@D2, @Dobra, 2), (@D3, @Corte, 1), (@D4, @Corte, 1), (@D5, @Corte, 1);

    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        -- D1 (10): 4 a iniciar | 3 no Corte | 3 aguardando coleta no Corte
        (@D1, N'Inicio',  6, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@D1, N'Termino', 3, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        -- D2 (6): 3 no passo 2 (Dobra) | 3 aguardando coleta na Dobra
        (@D2, N'Inicio',  6, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@D2, N'Termino', 6, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@D2, N'Entrega', 6, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra, 2,    @Movimentador),
        (@D2, N'Termino', 3, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta', @Dobra, 2,    @Operador),
        -- D4 (4): 2 a iniciar | 2 no Corte
        (@D4, N'Inicio',  2, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        -- D5 (3): no local de expedicao
        (@D5, N'Inicio',  3, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@D5, N'Termino', 3, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@D5, N'Entrega', 3, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);
    -- D3 (5) fica inteira a iniciar: sem linha no livro.

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M;

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B1-DESC criado.';
END
