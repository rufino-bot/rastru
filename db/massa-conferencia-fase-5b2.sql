-- =====================================================================
-- MASSA DA CONFERENCIA NO NAVEGADOR DA FASE 5B-2 (reposicao) — conveniencia de verificacao manual, NAO requisito.
--
-- O que cria (chaveado por nome de Setor e por numero de Pedido; todo numero comeca por CONF-5B2):
--   * os Setores 'Corte', 'Dobra' e 'Solda' (os que ja existem sao reaproveitados) e a marca UtilizaKit = 1 na
--     'Solda', reafirmada a cada carga (a tela de Setores pode te-la desmarcado; os Pedidos Avulsos nao mudam com ela);
--   * quatro Pedidos de fabricacao (cliente 'Conferência da Fase 5B-2', autor admin, entrega 2099-12-31: nunca
--     atrasam). Todos os nos sao copiados do catalogo, SEM descricao propria (EstruturaItem.Descricao = NULL: o nome
--     vem do Componente).
--
--   CONF-5B2 (EmProducao), dois Agrupamentos Avulsos, "2 a repor":
--
--     AVULSO-01  Peca de dois niveis; a perda de um Item travou a Peca e a baixa levou a Peca junto (incluirTopo):
--
--       MT-1010  Peca P, 4                     Roteiro: Solda(1)            3 a iniciar | 1 perdido (a baixa)
--        +- PF-3010  Item A, 8  (2 por P)      Roteiro: Corte(1), Dobra(2)  os 8 aguardando coleta na Dobra, no fim do
--        |                                        Roteiro; o pai so precisa de 6: 2 de SOBRA em AguardandoColeta
--        +- PF-3020  Item B, 4  (1 por P)      Roteiro: Corte(1)            os 4 aguardando montagem na Solda; o pai
--        |                                        so precisa de 3: 1 de SOBRA em AguardandoMontagem
--        +- CH-2110  Item C, 4  (1 por P)      Roteiro: Corte(1)            2 a iniciar | 1 no Corte | 1 perdido
--
--       A baixa (MortaEmProcesso, de parte de C, pela qualidade): C 1 do Corte (passo 1) e P 1 de "a iniciar", que e a
--       trava no topo levada junto. E a baixa "a repor" de 1 unidade de Peca: dela se abre o Retrabalho, e no
--       Retrabalho o "Usar a sobra" sugere A 2 (aguardando coleta na Dobra) e B 1 (aguardando montagem na Solda).
--
--     AVULSO-02  CH-2120 Peca E, 5, Roteiro: Corte(1): 2 a iniciar | 2 no Corte | 1 perdido. A baixa (PerdaArmazem, de
--                1 de "a iniciar", pela qualidade) e "a repor" de 1: e a que recebe o "Nao repor" e o "Desfazer".
--
--   CONF-5B2-KIT (EmProducao), o Agrupamento KIT-01, tipo Kit, com sobra ja dentro da Solda:
--
--       MT-1020  Peca K, 2                     Roteiro: Solda(1)            1 a iniciar | 1 perdido (a baixa)
--        +- EX-2210  Item X, 2  (1 por K)      Roteiro: Corte(1)            os 2 aguardando montagem na Solda; o pai so
--        |                                        precisa de 1: 1 de SOBRA na Solda (Setor UtilizaKit)
--        +- CH-2130  Item Y, 2  (1 por K)      Roteiro: Dobra(1)            1 aguardando montagem na Solda | 1 perdido
--
--       Os dois conjuntos entraram completos na Solda; um Y morreu la dentro. A baixa (MortaEmProcesso, de parte de Y,
--       de AguardandoMontagem na Solda, pela qualidade) leva K 1 de "a iniciar" junto. No Retrabalho, o pronto de X na
--       Solda e um conjunto em formacao, que o Y fabricado completa.
--
--   CONF-5B2-EXP (AguardandoExpedicao), AVULSO-01: CH-2150 Peca N, 4, exige relatorio, Roteiro: Corte(1). As 4 chegaram
--     ao local de expedicao; a qualidade aferiu NAO CONFORME, contou 2 conformes + 2 nao conformes e matou 1 nao
--     conforme (a baixa MortaEmProcesso de origem NaoConforme, a do "Matar"). Fica: 2 conformes | 1 nao conforme | 1
--     perdido. A baixa e "a repor" de 1, com o motivo sugerido "Reprovacao dimensional" (ReprovacaoDimensional).
--
--   CONF-5B2-ENT (Concluido), AVULSO-01, ja entregue ao cliente:
--
--       SP-2310  Peca R, 3                     Roteiro: Solda(1)            os 3 expedidos numa remessa
--        +- CH-2160  Item S, 6  (2 por R)      Roteiro: Corte(1)            os 6 montados em R
--
--     Para registrar a reclamacao e abrir dela o Retrabalho de consertar e o de refazer.
--
--   Livro (dbo.Movimentacao) escrito direto, para o estado ser real, com a conservacao da regra 9 fechando em TODO no
--   (a iniciar + nos Setores + aguardando coleta/montagem + local de expedicao + conforme/nao conforme + montado +
--   expedido + perdido = Quantidade). Inicio e Termino pelo `operador`, Entrega pelo `movimentador`, afericao,
--   contagem, remessa e baixas pela `qualidade`. Cada baixa e uma dbo.Perda (escopo: o no em que se clicou) com uma
--   linha `Perda` do livro por no e posicao de origem, apontando-a, como a escrita da aplicacao grava. Nenhum
--   Retrabalho, reclamacao ou "nao repor" e criado: o fluxo da 5B-2 e feito na tela.
--
-- Pre-requisitos: db/seed.sql (os usuarios admin, operador, movimentador e qualidade), db/seed-demo.sql (os
-- Componentes MT-1010, PF-3010, PF-3020, CH-2110, CH-2120, MT-1020, EX-2210, CH-2130, CH-2150, SP-2310 e CH-2160) e o
-- schema atual (specs/02-modelo-de-dados.sql, ou db/alter-fase-5b2.sql num banco anterior). Falta de qualquer um para o
-- script com erro.
--
-- Idempotente: Setor existente e reaproveitado; se um Pedido ja existe, nada e criado de novo para ele (e nada que a
-- conferencia tenha feito nele e tocado). Para refazer do zero, apague os Pedidos CONF-5B2*, os Retrabalhos abertos a
-- partir deles e tudo o que pende deles, e rode este arquivo de novo; quem so tem o repositorio do codigo regenera o
-- banco de dev, que e descartavel: DROP DATABASE, specs/02-modelo-de-dados.sql, db/seed.sql e db/seed-demo.sql, e
-- depois este arquivo.
--
-- Carga (a codepage 65001 e o que preserva a acentuacao; ver o seed-demo.sql):
--   MSYS_NO_PATHCONV=1 docker compose cp db/massa-conferencia-fase-5b2.sql sqlserver:/tmp/massa-conferencia-fase-5b2.sql
--   MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-conferencia-fase-5b2.sql
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
IF OBJECT_ID('dbo.Reclamacao') IS NULL OR COL_LENGTH('dbo.EstruturaItem', 'OrigemEstruturaItemId') IS NULL
    THROW 50000, N'Schema da Fase 5B-2 ausente (dbo.Reclamacao, EstruturaItem.OrigemEstruturaItemId): aplique db/alter-fase-5b2.sql antes.', 1;

DECLARE @CompP INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1010' AND Ativo = 1);
DECLARE @CompA INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3010' AND Ativo = 1);
DECLARE @CompB INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3020' AND Ativo = 1);
DECLARE @CompC INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2110' AND Ativo = 1);
DECLARE @CompE INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2120' AND Ativo = 1);
DECLARE @CompK INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020' AND Ativo = 1);
DECLARE @CompX INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'EX-2210' AND Ativo = 1);
DECLARE @CompY INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2130' AND Ativo = 1);
DECLARE @CompN INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2150' AND Ativo = 1);
DECLARE @CompR INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'SP-2310' AND Ativo = 1);
DECLARE @CompS INT = (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2160' AND Ativo = 1);

IF @CompP IS NULL OR @CompA IS NULL OR @CompB IS NULL OR @CompC IS NULL OR @CompE IS NULL OR @CompK IS NULL
   OR @CompX IS NULL OR @CompY IS NULL OR @CompN IS NULL OR @CompR IS NULL OR @CompS IS NULL
    THROW 50000, N'Componentes MT-1010, PF-3010, PF-3020, CH-2110, CH-2120, MT-1020, EX-2210, CH-2130, CH-2150, SP-2310 e CH-2160 ativos nao encontrados: carregue db/seed-demo.sql antes.', 1;

-- Setores: so cria o que falta. A Solda e Setor de Kit (regra 25): a marca e reafirmada a cada carga.
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Corte') INSERT INTO dbo.Setor (Nome) VALUES (N'Corte');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Dobra') INSERT INTO dbo.Setor (Nome) VALUES (N'Dobra');
IF NOT EXISTS (SELECT 1 FROM dbo.Setor WHERE Nome = N'Solda') INSERT INTO dbo.Setor (Nome) VALUES (N'Solda');

DECLARE @Corte INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte');
DECLARE @Dobra INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @Solda INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');

UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = @Solda AND UtilizaKit = 0;

-- Linhas de producao de cada Pedido: (no, tipo, quantidade, origem, destino, autor). Os Setores e passos vem em
-- colunas; as posicoes sem Setor levam NULL. As linhas que apontam um registro (baixa, contagem, remessa, montagem)
-- sao inseridas a parte, depois do registro. As constraints CK_Movimentacao_* conferem cada linha.
DECLARE @M TABLE (
    Seq INT IDENTITY(1,1), No INT, Tipo NVARCHAR(20), Qtd DECIMAL(18,4),
    OP NVARCHAR(20), OS INT, OO INT, DP NVARCHAR(20), DS INT, DOrd INT, Usr INT);

-- ---------------------------------------------------------------------
-- CONF-5B2: a perda de Item que trava a Peca (sobra nas duas posicoes) e a baixa para o "Nao repor".
-- ---------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B2')
BEGIN
    PRINT N'Pedido CONF-5B2 ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B2', N'Conferência da Fase 5B-2', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @Pedido INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @Agr1 INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@Pedido, N'AVULSO-02', N'Avulso', @Admin);
    DECLARE @Agr2 INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompP, NULL, NULL, N'Peca', 4, NULL, 0);
    DECLARE @P INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompA, NULL, @P, N'Item', 8, 2, 0);
    DECLARE @A INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompB, NULL, @P, N'Item', 4, 1, 0);
    DECLARE @B INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr1, @CompC, NULL, @P, N'Item', 4, 1, 0);
    DECLARE @C INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@Agr2, @CompE, NULL, NULL, N'Peca', 5, NULL, 0);
    DECLARE @E INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@P, @Solda, 1),
        (@A, @Corte, 1), (@A, @Dobra, 2),
        (@B, @Corte, 1),
        (@C, @Corte, 1),
        (@E, @Corte, 1);

    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        -- A (8): do Corte a Dobra; os 8 aguardando coleta no ultimo passo.
        (@A, N'Inicio',  8, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@A, N'Termino', 8, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@A, N'Entrega', 8, N'AguardandoColeta', @Corte, 1,    N'NoSetor',            @Dobra, 2,    @Movimentador),
        (@A, N'Termino', 8, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta',   @Dobra, 2,    @Operador),
        -- B (4): entregues a montagem de P, na Solda.
        (@B, N'Inicio',  4, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@B, N'Termino', 4, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@B, N'Entrega', 4, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador),
        -- C (4): 2 iniciados no Corte.
        (@C, N'Inicio',  2, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        -- E (5): 2 iniciados no Corte.
        (@E, N'Inicio',  2, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M ORDER BY Seq;

    -- A baixa de parte de C, com a Peca do topo junto: C 1 do Corte e P 1 de "a iniciar".
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'MortaEmProcesso', N'Chapa trincou no corte.', @Pedido, @C, 0, @Qualidade);
    DECLARE @PerdaC INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@C, N'Perda', 1, N'NoSetor',  @Corte, 1,    N'Perdido', @PerdaC, @Qualidade),
        (@P, N'Perda', 1, N'AIniciar', NULL,   NULL, N'Perdido', @PerdaC, @Qualidade);

    -- A baixa de E (Peca) de 1 de "a iniciar".
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'PerdaArmazem', N'Chapa sumiu do estoque.', @Pedido, @E, 0, @Qualidade);
    DECLARE @PerdaE INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@E, N'Perda', 1, N'AIniciar', N'Perdido', @PerdaE, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B2 criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5B2-KIT: o Kit com a sobra ja dentro da Solda (D8).
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B2-KIT')
BEGIN
    PRINT N'Pedido CONF-5B2-KIT ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B2-KIT', N'Conferência da Fase 5B-2', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedidoK INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedidoK, N'KIT-01', N'Kit', @Admin);
    DECLARE @AgrK INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrK, @CompK, NULL, NULL, N'Peca', 2, NULL, 0);
    DECLARE @K INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrK, @CompX, NULL, @K, N'Item', 2, 1, 0);
    DECLARE @X INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrK, @CompY, NULL, @K, N'Item', 2, 1, 0);
    DECLARE @Y INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@K, @Solda, 1), (@X, @Corte, 1), (@Y, @Dobra, 1);

    -- Os dois conjuntos entraram completos na Solda (regra 25).
    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@X, N'Inicio',  2, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@X, N'Termino', 2, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@Y, N'Inicio',  2, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Dobra, 1,    @Operador),
        (@Y, N'Termino', 2, N'NoSetor',          @Dobra, 1,    N'AguardandoColeta',   @Dobra, 1,    @Operador),
        (@X, N'Entrega', 2, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador),
        (@Y, N'Entrega', 2, N'AguardandoColeta', @Dobra, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M ORDER BY Seq;

    -- Um Y morreu dentro da Solda; a baixa leva K 1 de "a iniciar" junto.
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'MortaEmProcesso', N'Virola amassada na Solda.', @PedidoK, @Y, 0, @Qualidade);
    DECLARE @PerdaY INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@Y, N'Perda', 1, N'AguardandoMontagem', @Solda, N'Perdido', @PerdaY, @Qualidade),
        (@K, N'Perda', 1, N'AIniciar',           NULL,   N'Perdido', @PerdaY, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B2-KIT criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5B2-EXP: a peca morta na expedicao (o "Matar" de uma nao conforme).
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B2-EXP')
BEGIN
    PRINT N'Pedido CONF-5B2-EXP ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B2-EXP', N'Conferência da Fase 5B-2', N'Fabricacao', N'AguardandoExpedicao', '2099-12-31', @Admin);
    DECLARE @PedidoX INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedidoX, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrX INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrX, @CompN, NULL, NULL, N'Peca', 4, NULL, 1);
    DECLARE @N INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@N, @Corte, 1);

    INSERT INTO @M (No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr) VALUES
        (@N, N'Inicio',  4, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    @Operador),
        (@N, N'Termino', 4, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    @Operador),
        (@N, N'Entrega', 4, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId)
    SELECT No, Tipo, Qtd, OP, OS, OO, DP, DS, DOrd, Usr FROM @M ORDER BY Seq;

    -- A afericao nao conforme e a contagem 2 conformes + 2 nao conformes.
    INSERT INTO dbo.RelatorioDimensional (EstruturaItemId, Tipo, Resultado, UsuarioId)
    VALUES (@N, N'Afericao', N'NaoConforme', @Qualidade);
    INSERT INTO dbo.ContagemDimensional (EstruturaItemId, Conformes, NaoConformes, UsuarioId)
    VALUES (@N, 2, 2, @Qualidade);
    DECLARE @Contagem INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, ContagemId, UsuarioId) VALUES
        (@N, N'Medicao', 2, N'NaExpedicao', N'Conforme',    @Contagem, @Qualidade),
        (@N, N'Medicao', 2, N'NaExpedicao', N'NaoConforme', @Contagem, @Qualidade);

    -- O "Matar" de 1 nao conforme.
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, EstruturaItemId, Tudo, UsuarioId)
    VALUES (N'MortaEmProcesso', N'Furação fora de posição, sem conserto.', @PedidoX, @N, 0, @Qualidade);
    DECLARE @PerdaN INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, PerdaId, UsuarioId) VALUES
        (@N, N'Perda', 1, N'NaoConforme', N'Perdido', @PerdaN, @Qualidade);

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B2-EXP criado.';
END

-- ---------------------------------------------------------------------
-- CONF-5B2-ENT: o Pedido concluido, com a Peca expedida numa remessa, para a reclamacao.
-- ---------------------------------------------------------------------
DELETE FROM @M;
IF EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'CONF-5B2-ENT')
BEGIN
    PRINT N'Pedido CONF-5B2-ENT ja existe: nada a criar.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'CONF-5B2-ENT', N'Conferência da Fase 5B-2', N'Fabricacao', N'EmProducao', '2099-12-31', @Admin);
    DECLARE @PedidoE INT = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId) VALUES (@PedidoE, N'AVULSO-01', N'Avulso', @Admin);
    DECLARE @AgrE INT = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrE, @CompR, NULL, NULL, N'Peca', 3, NULL, 0);
    DECLARE @R INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
    VALUES (@AgrE, @CompS, NULL, @R, N'Item', 6, 2, 0);
    DECLARE @S INT = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES (@R, @Solda, 1), (@S, @Corte, 1);

    -- S (6): do Corte a montagem de R, na Solda.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@S, N'Inicio',  6, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    @Operador),
        (@S, N'Termino', 6, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    @Operador),
        (@S, N'Entrega', 6, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, @Movimentador);

    -- O inicio de R na Solda consome os 6 S (a montagem): a baixa dos filhos e o Inicio do pai apontam a Montagem.
    INSERT INTO dbo.Montagem (EstruturaItemId, SetorId, Quantidade, UsuarioId) VALUES (@R, @Solda, 3, @Operador);
    DECLARE @Montagem INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, MontagemId, UsuarioId) VALUES
        (@S, N'Montagem', 6, N'AguardandoMontagem', @Solda, NULL, N'Montado', NULL,   NULL, @Montagem, @Operador),
        (@R, N'Inicio',   3, N'AIniciar',           NULL,   NULL, N'NoSetor', @Solda, 1,    @Montagem, @Operador);

    -- R (3): da Solda ao local de expedicao.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, UsuarioId) VALUES
        (@R, N'Termino', 3, N'NoSetor',          @Solda, 1,    N'AguardandoColeta', @Solda, 1,    @Operador),
        (@R, N'Entrega', 3, N'AguardandoColeta', @Solda, 1,    N'NaExpedicao',      NULL,   NULL, @Movimentador);

    -- A remessa dos 3 (a Qualidade expede), que conclui o Agrupamento e o Pedido (regra 13).
    INSERT INTO dbo.Remessa (UsuarioId) VALUES (@Qualidade);
    DECLARE @Remessa INT = SCOPE_IDENTITY();
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, RemessaId, UsuarioId) VALUES
        (@R, N'Expedicao', 3, N'NaExpedicao', N'Expedido', @Remessa, @Qualidade);

    UPDATE dbo.Agrupamento SET DataConclusao = SYSUTCDATETIME() WHERE Id = @AgrE;
    UPDATE dbo.Pedido SET Status = N'Concluido', DataConclusao = SYSUTCDATETIME() WHERE Id = @PedidoE;

    COMMIT TRANSACTION;
    PRINT N'Pedido CONF-5B2-ENT criado.';
END
