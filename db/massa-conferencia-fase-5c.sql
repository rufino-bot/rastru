-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 5C (as pontas do Kit) — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (chaveado por nome de Setor e por numero de Pedido; todo numero comeca por CONF-5C):
--   * os Setores 'Corte', 'Dobra' e 'Solda' (os que ja existem sao reaproveitados) e a marca UtilizaKit = 1 na
--     'Solda', reafirmada a cada carga;
--   * tres Pedidos de fabricacao (cliente 'Conferência da Fase 5C', autor admin, entrega 2099-12-31: nunca atrasam),
--     cada um com um Agrupamento KIT-01 do tipo Kit, EmProducao. Todos os nos sao copiados do catalogo, SEM descricao
--     propria. Nada e perdido nem estornado nos dois primeiros: o que a conferencia faz, faz na tela.
--
--   CONF-5C-PERDA, o exemplo do defeito (secao 1 da spec): a baixa de uma A dentro da Solda, com a unidade do topo junto.
--
--       MT-1010  Peca C, 10                    Roteiro: Solda(1)            10 a iniciar
--        +- PF-3010  Item A, 20  (2 por C)     Roteiro: Corte(1)            6 aguardando montagem na Solda | 14 a iniciar
--        +- PF-3020  Item B, 10  (1 por C)     Roteiro: Corte(1)            3 aguardando montagem na Solda | 7 a iniciar
--
--       Tres conjuntos entraram completos na Solda. Na conferencia: "Dar baixa" de 1 A de "Aguardando montagem" na
--       Solda, MortaEmProcesso, com a caixa do topo marcada. Esperado: a previa lista 1 A e 1 B como "vira sobra" e nao
--       descarta nada a iniciar; a Fila da Solda mostra 1 A e 1 B como sobra "em montagem", com "Descartar"; as Tarefas
--       continuam oferecendo o Kit ate o nono conjunto; o "Usar a sobra" de um Retrabalho dessa baixa oferece os dois.
--
--   CONF-5C-DESALINHADO, a espera desalinhada da regra 25:
--
--       MT-1020  Peca K, 2                     Roteiro: Solda(1)            2 a iniciar
--        +- EX-2210  Item X, 8  (4 por K)      Roteiro: Corte(1)            7 aguardando montagem na Solda | 1 a iniciar
--        +- CH-2130  Item Y, 2  (1 por K)      Roteiro: Dobra(1)            1 aguardando montagem na Solda | 1 a iniciar
--
--       Gravados como entregas avulsas, o que o app nao deixaria fazer. Esperado: 1 conjunto completo a espera, o teto
--       de entrada e 1 (entra mais um conjunto: 11 X e 2 Y) e as 3 X que nao fecham conjunto sao sobra "em montagem".
--
--   CONF-5C-SAIU, o pai Kit cujos filhos saíram todos do projeto:
--
--       MT-1010  Peca K, 3                     Roteiro: Solda(1)            3 a iniciar
--        +- PF-3010  Item A, 6  (2 por K)      Roteiro: Corte(1)            6 perdidos (Descarte, Tudo = 1)
--        +- PF-3020  Item B, 3  (1 por K)      Roteiro: Corte(1)            3 perdidos (Descarte, Tudo = 1)
--
--       Esperado: a Peca aparece "a iniciar" na Fila da Solda e inicia por ela, como folha. Cada descarte e uma
--       dbo.Perda (escopo: o no) com a linha `Perda` do livro apontando-a, como a escrita da aplicacao grava.
--
--   Livro (dbo.Movimentacao) escrito direto, com a conservacao da regra 9 fechando em TODO no. Inicio e Termino pelo
--   `operador`, Entrega pelo `movimentador`, baixas pela `qualidade`.
--
-- Pre-requisitos: db/seed.sql (os usuarios admin, operador, movimentador e qualidade), db/seed-demo.sql (os
-- Componentes MT-1010, PF-3010, PF-3020, MT-1020, EX-2210 e CH-2130) e o schema atual (specs/02-modelo-de-dados.sql).
-- Falta de qualquer um para o script com erro.
--
-- Idempotente: Setor existente e reaproveitado; se um Pedido ja existe, nada e criado de novo para ele. Para refazer do
-- zero, apague os Pedidos CONF-5C* e tudo o que pende deles, e rode este arquivo de novo; ou regenere o banco de dev,
-- que e descartavel.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-5c.sql sqlserver:/tmp/massa-conferencia-fase-5c.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-5c.sql
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
IF OBJECT_ID('dbo.Perda') IS NULL OR COL_LENGTH('dbo.Perda', 'Tudo') IS NULL
    THROW 50000, N'Schema atual ausente (dbo.Perda): aplique specs/02-modelo-de-dados.sql ou os db/alter-fase-*.sql antes.', 1;

DECLARE @CompP INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1010' AND Ativo = 1);
DECLARE @CompA INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3010' AND Ativo = 1);
DECLARE @CompB INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3020' AND Ativo = 1);
DECLARE @CompK INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020' AND Ativo = 1);
DECLARE @CompX INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'EX-2210' AND Ativo = 1);
DECLARE @CompY INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2130' AND Ativo = 1);

IF @CompP IS NULL OR @CompA IS NULL OR @CompB IS NULL OR @CompK IS NULL OR @CompX IS NULL OR @CompY IS NULL
    THROW 50000, N'Componentes MT-1010, PF-3010, PF-3020, MT-1020, EX-2210 e CH-2130 ativos nao encontrados: carregue db/seed-demo.sql antes.', 1;

-- Setores: so cria o que falta. A Solda e Setor de Kit (regra 25): a marca e reafirmada a cada carga.
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte') INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Dobra') INSERT INTO dbo.Setor (Nome) VALUES (N'Dobra');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda') INSERT INTO dbo.Setor (Nome) VALUES (N'Solda');

DECLARE @Corte INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');
DECLARE @Dobra INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @Solda INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');

UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = @Solda AND UtilizaKit = 0;

-- Linhas de producao de cada Pedido: (no, tipo, quantidade, origem, destino, autor).
DECLARE @M TABLE (
    Seq INT IDENTITY(1,1), No INT, Tipo NVARCHAR(20), Qtd DECIMAL(18,4),
    OP NVARCHAR(20), OS INT, OO INT, DP NVARCHAR(20), DS INT, DOrd INT, Usr INT);

-- ---------------------------------------------------------------------
-- CONF-5C-PERDA: tres conjuntos na Solda; a baixa de uma A e feita na conferencia.
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5C-PERDA')
BEGIN
    PRINT N'Pedido CONF-5C-PERDA ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5C-PERDA', N'Conferência da Fase 5C', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @Pedido1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido1, N'KIT-01', N'Kit', @Admin);
    DECLARE @Agr1 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompP, NULL, NULL, N'Peca', 10, NULL, 0);
    DECLARE @C1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompA, NULL, @C1, N'Item', 20, 2, 0);
    DECLARE @A1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompB, NULL, @C1, N'Item', 10, 1, 0);
    DECLARE @B1 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@C1, @Solda, 1), (@A1, @Corte, 1), (@B1, @Corte, 1);

    -- Tres conjuntos completos entraram na Solda (regra 25): 6 A e 3 B.
    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@A1, N'Inicio',  6, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@A1, N'Termino', 6, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@A1, N'Entrega', 6, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador),
        (@B1, N'Inicio',  3, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@B1, N'Termino', 3, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@B1, N'Entrega', 3, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador);
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M ORDER BY Seq;

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5C-PERDA criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5C-DESALINHADO: 7 X (razao 4) e 1 Y (razao 1) na Solda, entregues avulsos.
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5C-DESALINHADO')
BEGIN
    PRINT N'Pedido CONF-5C-DESALINHADO ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5C-DESALINHADO', N'Conferência da Fase 5C', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @Pedido2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido2, N'KIT-01', N'Kit', @Admin);
    DECLARE @Agr2 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr2, @CompK, NULL, NULL, N'Peca', 2, NULL, 0);
    DECLARE @K2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr2, @CompX, NULL, @K2, N'Item', 8, 4, 0);
    DECLARE @X2 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr2, @CompY, NULL, @K2, N'Item', 2, 1, 0);
    DECLARE @Y2 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@K2, @Solda, 1), (@X2, @Corte, 1), (@Y2, @Dobra, 1);

    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@X2, N'Inicio',  7, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@X2, N'Termino', 7, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@X2, N'Entrega', 7, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador),
        (@Y2, N'Inicio',  1, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Dobra, 1,    @Operador),
        (@Y2, N'Termino', 1, N'NoSetor',          @Dobra, 1,    N'AguardandoColeta',   @Dobra, 1,    @Operador),
        (@Y2, N'Entrega', 1, N'AguardandoColeta', @Dobra, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador);
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M ORDER BY Seq;

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5C-DESALINHADO criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5C-SAIU: o Kit cujos dois filhos foram descartados por inteiro.
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5C-SAIU')
BEGIN
    PRINT N'Pedido CONF-5C-SAIU ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5C-SAIU', N'Conferência da Fase 5C', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @Pedido3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido3, N'KIT-01', N'Kit', @Admin);
    DECLARE @Agr3 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr3, @CompP, NULL, NULL, N'Peca', 3, NULL, 0);
    DECLARE @K3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr3, @CompA, NULL, @K3, N'Item', 6, 2, 0);
    DECLARE @A3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr3, @CompB, NULL, @K3, N'Item', 3, 1, 0);
    DECLARE @B3 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@K3, @Solda, 1), (@A3, @Corte, 1), (@B3, @Corte, 1);

    -- O descarte de tudo de A (6) e de B (3), de "a iniciar": saiu do projeto (Tudo = 1).
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'Descarte', N'Item saiu do projeto.', @Pedido3, @A3, 1, @Qualidade);
    DECLARE @PerdaA3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@A3, N'Perda', 6, N'AIniciar', N'Perdido', @PerdaA3, @Qualidade);
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'Descarte', N'Item saiu do projeto.', @Pedido3, @B3, 1, @Qualidade);
    DECLARE @PerdaB3 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@B3, N'Perda', 3, N'AIniciar', N'Perdido', @PerdaB3, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5C-SAIU criado.';
END
