/* ---------------------------------------------------------------------
   Massa de vitrine da HML (D11 da spec do CI/CD, docs/superpowers/specs/2026-11-25-ci-cd-design.md).
   PROPOSITO: o banco da HML criado do zero com `vitrine = true` (o CriadorDeBanco) recebe o db/seed-demo.sql e
   depois este arquivo, para quem abre a HML ver cada tela com algo de verdade dentro. Sao oito Pedidos de
   fabricacao, cada um num estagio que as telas mostram, e o Retrabalho de um deles:
     PED-1041  Mineração Serra Alta         atrasado: EmProducao, entrega ha 6 dias, Pecas no Corte a Laser e na Dobra
     PED-1042  Agroindustrial Vale Verde    pausado: EmProducao, com a pausa aberta ha 2 dias
     PED-1043  Cimentos Planalto            meio da producao: a Peca dividida em 4 Setores, com lotes aguardando
                                            coleta, de 2 a 9 dias
     PED-1044  Portos do Sul Logística      Kit na Solda (UtilizaKit): um conjunto montado (o Inicio da Peca aponta a
                                            Montagem), um conjunto a espera de montagem na Solda e, no Corte a Laser,
                                            filhos prontos que ainda nao fecham um conjunto (o "Kit incompleto")
     PED-1045  Usina Santa Clara            aguardando expedicao (C11 da 5A: todo o nao expedido no local de
                                            expedicao): uma Peca aferida Conforme e outra NaoConforme, com a contagem,
                                            e uma remessa parcial ja feita
     PED-1046  Fertilizantes Cerrado        perda e reposicao: uma baixa por MortaEmProcesso de 2 unidades da Peca e o
                                            Retrabalho PED-1046-R1 que a repoe (MotivoRetrabalho Perda), ligado como a
                                            5B-2 liga (Perda.PedidoRetrabalhoId) e com a Peca copiada a iniciar
     PED-1047  Metalúrgica Horizonte        recem-aberto: Aberto, Peca com dois Itens e Roteiro, nada iniciado
     PED-1048  Papel e Celulose Ribeira     concluido: tudo expedido em duas remessas
   FORMA: cada linha e a que a API gravaria para a acao correspondente (os CHECKs do livro e as regras 9 e 22 a 40 de
   specs/01): Inicio, Termino e Entrega pelo Roteiro; a montagem do Kit como a Fase 3D a grava (dbo.Montagem, a baixa
   de cada filho e o Inicio do pai apontando-a); a entrega ao AguardandoMontagem da Solda so em conjunto completo
   (regra 25); a afericao em dbo.RelatorioDimensional, a contagem em dbo.ContagemDimensional com as linhas Medicao, a
   remessa em dbo.Remessa com as linhas Expedicao (tirando na ordem da C10: Liberada, Conforme, NaExpedicao); a baixa
   em dbo.Perda com a linha Perda; o Retrabalho com o numero que NumeroDoRetrabalho sugere, o cliente e a entrega do
   original, um Agrupamento com o codigo e o Tipo do original e a Peca copiada como a CopiaDaPeca a copia; o status
   e as datas de conclusao como o FechamentoDoPedido os grava. As datas sao relativas a carga (SYSUTCDATETIME()); a
   entrega e o dia de Brasilia (UTC-3).
   LIMITES: autor de tudo e o `admin`; sem estorno, liberacao, conserto, reclamacao, separacao de material, embalagem
   nem "Usar a sobra". Usa os Setores (Corte a Laser, Dobra, Solda, Pintura) e os Componentes do seed-demo.sql por
   nome e codigo, e aborta se faltarem. Marca a Solda com UtilizaKit = 1 se o seed-demo nao a marcou.
   Idempotente por numero de Pedido: o Pedido que ja existe nao e recriado, e o livro so e gerado para os Pedidos
   criados NESTA execucao; tudo numa transacao, com SET XACT_ABORT ON.
   CARGA: depois do db/seed.sql e do db/seed-demo.sql, com -b -f 65001 (a codepage preserva a acentuacao dos
   clientes; ver CLAUDE.md, «Comandos»):
     MSYS_NO_PATHCONV=1 docker compose cp db/massa-vitrine.sql sqlserver:/tmp/massa-vitrine.sql
     MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
       -S localhost -U sa -P 'Your_strong_Pass123' -C -I -b -f 65001 -d Rastreamento -i /tmp/massa-vitrine.sql
   Na HML quem a carrega e o `banco preparar`, so na criacao do banco. Coberta por MassaDaVitrineTests, num banco
   descartavel proprio: a suite nunca a carrega no banco compartilhado.
   --------------------------------------------------------------------- */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Admin INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = N'admin');
IF @Admin IS NULL THROW 50020, N'Usuario admin nao encontrado: aplique db/seed.sql antes.', 1;

DECLARE @Corte   INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Corte a Laser');
DECLARE @Dobra   INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Dobra');
DECLARE @Solda   INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Solda');
DECLARE @Pintura INT = (SELECT Id FROM dbo.Setor WHERE Nome = N'Pintura');
IF @Corte IS NULL OR @Dobra IS NULL OR @Solda IS NULL OR @Pintura IS NULL
    THROW 50021, N'Setores Corte a Laser, Dobra, Solda e Pintura nao encontrados: aplique db/seed-demo.sql antes.', 1;

IF (SELECT COUNT(*) FROM dbo.Componente
    WHERE Ativo = 1 AND Codigo IN (N'CH-2110', N'CH-2120', N'CH-2130', N'CH-2140', N'CH-2150', N'CH-2160', N'CH-2170',
                                   N'CH-2190', N'SP-2050', N'MT-1020', N'MT-1050', N'PF-3040', N'TB-3130', N'PA-2030')) <> 14
    THROW 50022, N'Componentes do seed-demo.sql nao encontrados (ou inativos): aplique db/seed-demo.sql antes.', 1;

-- Regra 25: a Solda e o Setor onde o Kit monta.
UPDATE dbo.Setor SET UtilizaKit = 1 WHERE Id = @Solda AND UtilizaKit = 0;

DECLARE @Agora DATETIME2 = SYSUTCDATETIME();
DECLARE @Hoje  DATE = CAST(DATEADD(HOUR, -3, @Agora) AS DATE);   -- o dia de Brasilia

DECLARE @Pedido INT, @Agrupamento INT, @Peca1 INT, @Peca2 INT, @ItemA INT, @ItemB INT;
DECLARE @Montagem INT, @Contagem INT, @Remessa INT, @Remessa2 INT, @Perda INT, @Retrabalho INT, @AgrupamentoR INT, @PecaR INT;
DECLARE @Entrega DATE, @Conclusao DATETIME2;

/* ---------------------------------------------------------------------
   PED-1041 — atrasado: a entrega foi ha 6 dias e as Pecas ainda estao no Corte a Laser e na Dobra.
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1041')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1041', N'Mineração Serra Alta', N'Fabricacao', N'EmProducao', DATEADD(DAY, -20, @Agora),
            DATEADD(DAY, -6, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -20, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2110'), NULL, NULL, N'Peca', 12);
    SET @Peca1 = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2120'), NULL, NULL, N'Peca', 8);
    SET @Peca2 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2), (@Peca1, @Pintura, 3),
        (@Peca2, @Corte, 1), (@Peca2, @Dobra, 2);

    -- Peca 1: 12 cortadas, 7 levadas a Dobra, 5 aguardando coleta no Corte a Laser. Peca 2: 8 no Corte a Laser.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  12, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1, DATEADD(DAY, -15, @Agora), @Admin),
        (@Peca1, N'Termino', 12, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1, DATEADD(DAY, -14, @Agora), @Admin),
        (@Peca1, N'Entrega',  7, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra, 2, DATEADD(DAY, -12, @Agora), @Admin),
        (@Peca2, N'Inicio',   8, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1, DATEADD(DAY, -10, @Agora), @Admin);
END

/* ---------------------------------------------------------------------
   PED-1042 — pausado: em producao, com a pausa aberta ha 2 dias (regra 31).
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1042')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1042', N'Agroindustrial Vale Verde', N'Fabricacao', N'EmProducao', DATEADD(DAY, -12, @Agora),
            DATEADD(DAY, 10, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -12, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2150'), NULL, NULL, N'Peca', 10);
    SET @Peca1 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2), (@Peca1, @Pintura, 3);

    -- 10 na Dobra, 4 delas ja terminadas e aguardando coleta.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  10, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1, DATEADD(DAY, -6, @Agora), @Admin),
        (@Peca1, N'Termino', 10, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1, DATEADD(DAY, -5, @Agora), @Admin),
        (@Peca1, N'Entrega', 10, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra, 2, DATEADD(HOUR, 2, DATEADD(DAY, -5, @Agora)), @Admin),
        (@Peca1, N'Termino',  4, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta', @Dobra, 2, DATEADD(DAY, -3, @Agora), @Admin);

    INSERT INTO dbo.PedidoPausa (PedidoId, PausadoEm, PausadoPorUsuarioId, Motivo)
    VALUES (@Pedido, DATEADD(DAY, -2, @Agora), @Admin, N'Outro Pedido mais urgente passou na frente.');
END

/* ---------------------------------------------------------------------
   PED-1043 — meio da producao: a Peca de 20 dividida em 4 Setores, com lotes aguardando coleta de idades
   diferentes (6 no Corte a Laser ha 8 dias, 5 na Dobra ha 5 dias; 5 na Solda ha 4 dias, 4 na Pintura ha 2 dias).
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1043')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1043', N'Cimentos Planalto', N'Fabricacao', N'EmProducao', DATEADD(DAY, -11, @Agora),
            DATEADD(DAY, 12, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -11, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'SP-2050'), NULL, NULL, N'Peca', 20);
    SET @Peca1 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2), (@Peca1, @Solda, 3), (@Peca1, @Pintura, 4);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  20, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte,   1, DATEADD(DAY, -9, @Agora), @Admin),
        (@Peca1, N'Termino', 20, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte,   1, DATEADD(DAY, -8, @Agora), @Admin),
        (@Peca1, N'Entrega', 14, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra,   2, DATEADD(DAY, -7, @Agora), @Admin),
        (@Peca1, N'Termino', 14, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta', @Dobra,   2, DATEADD(DAY, -5, @Agora), @Admin),
        (@Peca1, N'Entrega',  9, N'AguardandoColeta', @Dobra, 2,    N'NoSetor',          @Solda,   3, DATEADD(DAY, -4, @Agora), @Admin),
        (@Peca1, N'Termino',  4, N'NoSetor',          @Solda, 3,    N'AguardandoColeta', @Solda,   3, DATEADD(DAY, -2, @Agora), @Admin),
        (@Peca1, N'Entrega',  4, N'AguardandoColeta', @Solda, 3,    N'NoSetor',          @Pintura, 4, DATEADD(HOUR, 3, DATEADD(DAY, -2, @Agora)), @Admin);
END

/* ---------------------------------------------------------------------
   PED-1044 — Kit na Solda. Peca MT-1020 de 4, Roteiro Solda -> Pintura; filhos CH-2130 (4, 1 por Peca) e CH-2140
   (8, 2 por Peca), ambos de um passo no Corte a Laser. Dois conjuntos completos entraram na Solda: o primeiro foi
   montado (o Inicio de 1 da Peca consumiu 1 + 2), o segundo esta a espera de montagem. No Corte a Laser aguardam
   coleta 2 CH-2130 e 1 CH-2140, que nao fecham um conjunto (faltam 2 CH-2140 por conjunto); 3 CH-2140 ainda no Setor.
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1044')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1044', N'Portos do Sul Logística', N'Fabricacao', N'EmProducao', DATEADD(DAY, -10, @Agora),
            DATEADD(DAY, 8, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'KIT-01', N'Kit', @Admin, DATEADD(DAY, -10, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1020'), NULL, NULL, N'Peca', 4, NULL);
    SET @Peca1 = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2130'), NULL, @Peca1, N'Item', 4, 1);
    SET @ItemA = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2140'), NULL, @Peca1, N'Item', 8, 2);
    SET @ItemB = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Solda, 1), (@Peca1, @Pintura, 2),
        (@ItemA, @Corte, 1),
        (@ItemB, @Corte, 1);

    -- Os filhos no Corte a Laser e a primeira entrega de um conjunto completo (1 + 2) a montagem, na Solda.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@ItemA, N'Inicio',  4, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    DATEADD(DAY, -7, @Agora), @Admin),
        (@ItemB, N'Inicio',  8, N'AIniciar',         NULL,   NULL, N'NoSetor',            @Corte, 1,    DATEADD(MINUTE, 10, DATEADD(DAY, -7, @Agora)), @Admin),
        (@ItemA, N'Termino', 4, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    DATEADD(DAY, -6, @Agora), @Admin),
        (@ItemB, N'Termino', 5, N'NoSetor',          @Corte, 1,    N'AguardandoColeta',   @Corte, 1,    DATEADD(MINUTE, 10, DATEADD(DAY, -6, @Agora)), @Admin),
        (@ItemA, N'Entrega', 1, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, DATEADD(DAY, -5, @Agora), @Admin),
        (@ItemB, N'Entrega', 2, N'AguardandoColeta', @Corte, 1,    N'AguardandoMontagem', @Solda, NULL, DATEADD(DAY, -5, @Agora), @Admin);

    -- O Iniciar de 1 da Peca na Solda e a montagem (regra 24): a Montagem, a baixa de cada filho e o Inicio apontando-a.
    INSERT INTO dbo.Montagem (EstruturaItemId, SetorId, Quantidade, DataHora, UsuarioId)
    VALUES (@Peca1, @Solda, 1, DATEADD(DAY, -4, @Agora), @Admin);
    SET @Montagem = SCOPE_IDENTITY();

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, MontagemId, DataHora, UsuarioId) VALUES
        (@ItemA, N'Montagem', 1, N'AguardandoMontagem', @Solda, NULL, N'Montado', NULL,   NULL, @Montagem, DATEADD(DAY, -4, @Agora), @Admin),
        (@ItemB, N'Montagem', 2, N'AguardandoMontagem', @Solda, NULL, N'Montado', NULL,   NULL, @Montagem, DATEADD(DAY, -4, @Agora), @Admin),
        (@Peca1, N'Inicio',   1, N'AIniciar',           NULL,   NULL, N'NoSetor', @Solda, 1,    @Montagem, DATEADD(DAY, -4, @Agora), @Admin);

    -- O segundo conjunto completo, a espera de montagem (teto da regra 25: 4 - 1 montado - 0 a espera = 3).
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@ItemA, N'Entrega', 1, N'AguardandoColeta', @Corte, 1, N'AguardandoMontagem', @Solda, NULL, DATEADD(DAY, -2, @Agora), @Admin),
        (@ItemB, N'Entrega', 2, N'AguardandoColeta', @Corte, 1, N'AguardandoMontagem', @Solda, NULL, DATEADD(DAY, -2, @Agora), @Admin);
END

/* ---------------------------------------------------------------------
   PED-1045 — aguardando expedicao. Duas Pecas que exigem relatorio, ambas inteiras no local de expedicao:
   CH-2160 (10), aferida Conforme; CH-2170 (6), aferida NaoConforme e medida (4 medidas: 3 conformes, 1 nao
   conforme; 2 ainda a medir). Uma remessa parcial levou 4 da CH-2160 (da NaExpedicao, que a afericao conforme torna
   expedivel) e 2 da CH-2170 (das conformes, na ordem da C10).
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1045')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1045', N'Usina Santa Clara', N'Fabricacao', N'AguardandoExpedicao', DATEADD(DAY, -14, @Agora),
            DATEADD(DAY, 3, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -14, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, RequerRelatorioDimensional)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2160'), NULL, NULL, N'Peca', 10, 1);
    SET @Peca1 = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, RequerRelatorioDimensional)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2170'), NULL, NULL, N'Peca', 6, 1);
    SET @Peca2 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2),
        (@Peca2, @Corte, 1);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  10, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    DATEADD(DAY, -10, @Agora), @Admin),
        (@Peca1, N'Termino', 10, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    DATEADD(DAY, -9, @Agora), @Admin),
        (@Peca1, N'Entrega', 10, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra, 2,    DATEADD(HOUR, 2, DATEADD(DAY, -9, @Agora)), @Admin),
        (@Peca1, N'Termino', 10, N'NoSetor',          @Dobra, 2,    N'AguardandoColeta', @Dobra, 2,    DATEADD(DAY, -8, @Agora), @Admin),
        (@Peca1, N'Entrega', 10, N'AguardandoColeta', @Dobra, 2,    N'NaExpedicao',      NULL,   NULL, DATEADD(DAY, -7, @Agora), @Admin),
        (@Peca2, N'Inicio',   6, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1,    DATEADD(DAY, -9, @Agora), @Admin),
        (@Peca2, N'Termino',  6, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1,    DATEADD(DAY, -8, @Agora), @Admin),
        (@Peca2, N'Entrega',  6, N'AguardandoColeta', @Corte, 1,    N'NaExpedicao',      NULL,   NULL, DATEADD(HOUR, 1, DATEADD(DAY, -7, @Agora)), @Admin);

    -- As afericoes (regra 10): uma por Peca, sem mover quantidade.
    INSERT INTO dbo.RelatorioDimensional (EstruturaItemId, Tipo, Resultado, DataHora, UsuarioId) VALUES
        (@Peca1, N'Afericao', N'Conforme',    DATEADD(DAY, -6, @Agora), @Admin),
        (@Peca2, N'Afericao', N'NaoConforme', DATEADD(HOUR, 1, DATEADD(DAY, -6, @Agora)), @Admin);

    -- A contagem da nao conforme (regra 11): uma linha Medicao por resultado diferente de zero.
    INSERT INTO dbo.ContagemDimensional (EstruturaItemId, Conformes, NaoConformes, DataHora, UsuarioId)
    VALUES (@Peca2, 3, 1, DATEADD(DAY, -5, @Agora), @Admin);
    SET @Contagem = SCOPE_IDENTITY();

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, ContagemId, DataHora, UsuarioId) VALUES
        (@Peca2, N'Medicao', 3, N'NaExpedicao', N'Conforme',    @Contagem, DATEADD(DAY, -5, @Agora), @Admin),
        (@Peca2, N'Medicao', 1, N'NaExpedicao', N'NaoConforme', @Contagem, DATEADD(DAY, -5, @Agora), @Admin);

    -- A remessa parcial (regra 16): uma linha Expedicao por Peca e posicao de origem.
    INSERT INTO dbo.Remessa (DataHora, UsuarioId) VALUES (DATEADD(DAY, -3, @Agora), @Admin);
    SET @Remessa = SCOPE_IDENTITY();

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, RemessaId, DataHora, UsuarioId) VALUES
        (@Peca1, N'Expedicao', 4, N'NaExpedicao', N'Expedido', @Remessa, DATEADD(DAY, -3, @Agora), @Admin),
        (@Peca2, N'Expedicao', 2, N'Conforme',    N'Expedido', @Remessa, DATEADD(DAY, -3, @Agora), @Admin);
END

/* ---------------------------------------------------------------------
   PED-1046 — perda e reposicao. Peca CH-2190 de 8: na Dobra, 2 morreram em processo (a baixa); o Retrabalho
   PED-1046-R1 as repoe, aberto pela porta da baixa (regra 39) um dia depois, com a Peca copiada (2 unidades, o
   Roteiro, OrigemEstruturaItemId) a iniciar. O vinculo e Perda.PedidoRetrabalhoId (C2 da 5B-2).
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1046')
BEGIN
    SET @Entrega = DATEADD(DAY, 15, @Hoje);

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1046', N'Fertilizantes Cerrado', N'Fabricacao', N'EmProducao', DATEADD(DAY, -12, @Agora), @Entrega, @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -12, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'CH-2190'), NULL, NULL, N'Peca', 8);
    SET @Peca1 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2), (@Peca1, @Pintura, 3);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  8, N'AIniciar',         NULL,   NULL, N'NoSetor',          @Corte, 1, DATEADD(DAY, -8, @Agora), @Admin),
        (@Peca1, N'Termino', 8, N'NoSetor',          @Corte, 1,    N'AguardandoColeta', @Corte, 1, DATEADD(DAY, -7, @Agora), @Admin),
        (@Peca1, N'Entrega', 8, N'AguardandoColeta', @Corte, 1,    N'NoSetor',          @Dobra, 2, DATEADD(HOUR, 2, DATEADD(DAY, -7, @Agora)), @Admin);

    -- O Retrabalho, como o RetrabalhoUseCase o grava: o numero sugerido, o cliente e a entrega do original, Aberto.
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, PedidoOrigemId, MotivoRetrabalho, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1046-R1', N'Fertilizantes Cerrado', N'Retrabalho', @Pedido, N'Perda', N'Aberto', DATEADD(DAY, -2, @Agora),
            @Entrega, @Admin);
    SET @Retrabalho = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Retrabalho, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -2, @Agora));
    SET @AgrupamentoR = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade,
                                   QuantidadePorPai, OrigemEstruturaItemId, RequerRelatorioDimensional)
    SELECT @AgrupamentoR, ComponenteId, Descricao, NULL, N'Peca', 2, NULL, Id, RequerRelatorioDimensional
    FROM dbo.EstruturaItem WHERE Id = @Peca1;
    SET @PecaR = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem)
    SELECT @PecaR, SetorId, Ordem FROM dbo.EstruturaRoteiro WHERE EstruturaItemId = @Peca1;

    -- A baixa (regra 38): 2 mortas em processo na Dobra, ja reposta pelo Retrabalho.
    INSERT INTO dbo.Perda (Motivo, Observacao, PedidoId, AgrupamentoId, EstruturaItemId, Tudo, DataHora, UsuarioId, PedidoRetrabalhoId)
    VALUES (N'MortaEmProcesso', N'Duas chapas trincaram na dobra.', @Pedido, NULL, @Peca1, 0, DATEADD(DAY, -3, @Agora), @Admin,
            @Retrabalho);
    SET @Perda = SCOPE_IDENTITY();

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, PerdaId, DataHora, UsuarioId)
    VALUES (@Peca1, N'Perda', 2, N'NoSetor', @Dobra, 2, N'Perdido', @Perda, DATEADD(DAY, -3, @Agora), @Admin);

    -- Depois da baixa, 3 das 6 que ficaram foram terminadas na Dobra.
    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId)
    VALUES (@Peca1, N'Termino', 3, N'NoSetor', @Dobra, 2, N'AguardandoColeta', @Dobra, 2, DATEADD(DAY, -1, @Agora), @Admin);
END

/* ---------------------------------------------------------------------
   PED-1047 — recem-aberto: Peca MT-1050 de 3 com dois Itens e o Roteiro de cada um, nada iniciado.
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1047')
BEGIN
    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
    VALUES (N'PED-1047', N'Metalúrgica Horizonte', N'Fabricacao', N'Aberto', DATEADD(DAY, -1, @Agora),
            DATEADD(DAY, 25, @Hoje), @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Admin, DATEADD(DAY, -1, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'MT-1050'), NULL, NULL, N'Peca', 3, NULL);
    SET @Peca1 = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'PF-3040'), NULL, @Peca1, N'Item', 6, 2);
    SET @ItemA = SCOPE_IDENTITY();
    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'TB-3130'), NULL, @Peca1, N'Item', 3, 1);
    SET @ItemB = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Solda, 1), (@Peca1, @Pintura, 2),
        (@ItemA, @Corte, 1), (@ItemA, @Dobra, 2),
        (@ItemB, @Corte, 1);
END

/* ---------------------------------------------------------------------
   PED-1048 — concluido: Peca PA-2030 de 5 pelo Roteiro inteiro e expedida em duas remessas (3 e 2); o
   fechamento grava o status e a data de conclusao do Agrupamento e do Pedido com a hora da ultima remessa (regra 13).
   --------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Pedido WHERE Numero = N'PED-1048')
BEGIN
    SET @Conclusao = DATEADD(DAY, -6, @Agora);

    INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, DataConclusao, CriadoPorUsuarioId)
    VALUES (N'PED-1048', N'Papel e Celulose Ribeira', N'Fabricacao', N'Concluido', DATEADD(DAY, -25, @Agora),
            DATEADD(DAY, -2, @Hoje), @Conclusao, @Admin);
    SET @Pedido = SCOPE_IDENTITY();

    INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, DataConclusao, CriadoPorUsuarioId, CriadoEm)
    VALUES (@Pedido, N'AVULSO-01', N'Avulso', @Conclusao, @Admin, DATEADD(DAY, -25, @Agora));
    SET @Agrupamento = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade)
    VALUES (@Agrupamento, (SELECT Id FROM dbo.Componente WHERE Codigo = N'PA-2030'), NULL, NULL, N'Peca', 5);
    SET @Peca1 = SCOPE_IDENTITY();

    INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem) VALUES
        (@Peca1, @Corte, 1), (@Peca1, @Dobra, 2), (@Peca1, @Pintura, 3);

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                                  DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId) VALUES
        (@Peca1, N'Inicio',  5, N'AIniciar',         NULL,     NULL, N'NoSetor',          @Corte,   1,    DATEADD(DAY, -20, @Agora), @Admin),
        (@Peca1, N'Termino', 5, N'NoSetor',          @Corte,   1,    N'AguardandoColeta', @Corte,   1,    DATEADD(DAY, -19, @Agora), @Admin),
        (@Peca1, N'Entrega', 5, N'AguardandoColeta', @Corte,   1,    N'NoSetor',          @Dobra,   2,    DATEADD(HOUR, 2, DATEADD(DAY, -19, @Agora)), @Admin),
        (@Peca1, N'Termino', 5, N'NoSetor',          @Dobra,   2,    N'AguardandoColeta', @Dobra,   2,    DATEADD(DAY, -17, @Agora), @Admin),
        (@Peca1, N'Entrega', 5, N'AguardandoColeta', @Dobra,   2,    N'NoSetor',          @Pintura, 3,    DATEADD(DAY, -16, @Agora), @Admin),
        (@Peca1, N'Termino', 5, N'NoSetor',          @Pintura, 3,    N'AguardandoColeta', @Pintura, 3,    DATEADD(DAY, -14, @Agora), @Admin),
        (@Peca1, N'Entrega', 5, N'AguardandoColeta', @Pintura, 3,    N'NaExpedicao',      NULL,     NULL, DATEADD(DAY, -13, @Agora), @Admin);

    INSERT INTO dbo.Remessa (DataHora, UsuarioId) VALUES (DATEADD(DAY, -10, @Agora), @Admin);
    SET @Remessa = SCOPE_IDENTITY();
    INSERT INTO dbo.Remessa (DataHora, UsuarioId) VALUES (@Conclusao, @Admin);
    SET @Remessa2 = SCOPE_IDENTITY();

    INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, DestinoPosicao, RemessaId, DataHora, UsuarioId) VALUES
        (@Peca1, N'Expedicao', 3, N'NaExpedicao', N'Expedido', @Remessa,  DATEADD(DAY, -10, @Agora), @Admin),
        (@Peca1, N'Expedicao', 2, N'NaExpedicao', N'Expedido', @Remessa2, @Conclusao, @Admin);
END

COMMIT TRANSACTION;
