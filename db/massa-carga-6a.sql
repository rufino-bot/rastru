/* ---------------------------------------------------------------------
   Massa de carga da Fase 6A (C14) — OPCIONAL, a suite ignora que ela existe.
   PROPOSITO: medir o GET /panorama com a fabrica carregada e servir de base para a conferencia visual
   da Peca com filhos (C5: "Parado ha" e o Item "mais parado") — por isso ela segue a regra de montagem
   (regras 22 a 27, Fase 3D).
   Forma: 50 Pedidos abertos CARGA-6A-001 a 050, 1 Agrupamento Avulso por Pedido, 10 Pecas por
   Agrupamento e 5 Itens por Peca (3.000 nos), Roteiro de 4 passos em cada no (Corte, Dobra, Solda,
   Pintura), ~66 mil linhas do livro. Cada no tem 3 lotes de 10 (de 40). Os Itens correm o Roteiro e o
   ultimo passo entrega ao AguardandoMontagem do Corte, o Setor onde a Peca monta (1o passo dela). A Peca
   so inicia um lote por uma Montagem: uma linha dbo.Montagem + a baixa (Tipo 'Montagem', AguardandoMontagem
   -> Montado) de cada um dos 5 Itens + o Inicio da Peca apontando a Montagem; depois ela corre o Roteiro.
   Tres cenarios, pelo numero do Pedido (mod 3):
     0: nenhuma Peca iniciada; Itens do lote 1 esperando em AguardandoMontagem (Item e a parte mais parada),
        os outros lotes ainda em producao;
     1: Pecas iniciadas nos lotes 1 e 2 (profundidades variadas); Itens do lote 3 ainda em producao;
     2: Pecas iniciadas nos 3 lotes.
   LIMITES: so Inicio/Termino/Entrega/Montagem validos (CHECKs do livro); nada de estorno, perda, medicao,
   liberacao, expedicao ou reposicao; todos os Itens tem QuantidadePorPai = 1; DataHora espalhada por
   2 a 15 dias atras (por Pedido) com os lotes a minutos de distancia.
   Idempotente: o que ja existe (por codigo/numero) nao e recriado, e o livro so e gerado para os Pedidos
   criados NESTA execucao; tudo numa transacao. Cria os proprios Setores e Componentes (por codigo), sem
   depender do seed-demo.sql. Autor: 'admin'. Carregar com -b -f 65001 (ver CLAUDE.md, «Comandos»).
   --------------------------------------------------------------------- */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @admin INT = (SELECT Id FROM dbo.Usuario WHERE NomeUsuario = 'admin');
IF @admin IS NULL THROW 50010, 'Usuario admin nao encontrado: aplique db/seed.sql antes.', 1;
DECLARE @agora DATETIME2 = SYSUTCDATETIME();

-- Setores do caminho (4 passos)
INSERT INTO dbo.Setor (Nome)
SELECT v.Nome FROM (VALUES (N'CARGA-6A Corte'), (N'CARGA-6A Dobra'), (N'CARGA-6A Solda'), (N'CARGA-6A Pintura')) v(Nome)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Setor s WHERE s.Nome = v.Nome);

-- Componentes das 10 Pecas
INSERT INTO dbo.Componente (Codigo, Descricao, Tipo)
SELECT 'CARGA-6A-C' + RIGHT('0' + CAST(n AS VARCHAR(2)), 2), N'Peca de carga 6A ' + CAST(n AS NVARCHAR(2)), 'Montagem'
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) t(n)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Componente c WHERE c.Codigo = 'CARGA-6A-C' + RIGHT('0' + CAST(n AS VARCHAR(2)), 2));

-- Pedidos novos
DECLARE @NovosPedidos TABLE (Id INT PRIMARY KEY, Seq INT);
WITH Seq AS (
    SELECT TOP (50) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects
)
INSERT INTO dbo.Pedido (Numero, Cliente, Tipo, Status, DataAbertura, DataEntrega, CriadoPorUsuarioId)
OUTPUT inserted.Id, CAST(RIGHT(inserted.Numero, 3) AS INT) INTO @NovosPedidos (Id, Seq)
SELECT 'CARGA-6A-' + RIGHT('00' + CAST(n AS VARCHAR(3)), 3), N'Cliente carga 6A', 'Fabricacao', 'EmProducao',
       DATEADD(DAY, -20, @agora),
       CAST(DATEADD(DAY, (n % 25) - 5, @agora) AS DATE),   -- parte no passado (atrasados), parte no futuro
       @admin
FROM Seq
WHERE NOT EXISTS (SELECT 1 FROM dbo.Pedido p WHERE p.Numero = 'CARGA-6A-' + RIGHT('00' + CAST(n AS VARCHAR(3)), 3));

-- 1 Agrupamento Avulso por Pedido novo
INSERT INTO dbo.Agrupamento (PedidoId, Codigo, Tipo, CriadoPorUsuarioId, CriadoEm)
SELECT Id, 'AG-1', 'Avulso', @admin, DATEADD(DAY, -20, @agora) FROM @NovosPedidos;

-- 10 Pecas por Agrupamento novo
INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
SELECT a.Id, c.Id, N'P' + RIGHT('0' + CAST(k.n AS VARCHAR(2)), 2), NULL, 'Peca', 40, NULL, CASE WHEN k.n % 2 = 0 THEN 1 ELSE 0 END
FROM dbo.Agrupamento a
JOIN @NovosPedidos np ON np.Id = a.PedidoId
CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) k(n)
JOIN dbo.Componente c ON c.Codigo = 'CARGA-6A-C' + RIGHT('0' + CAST(k.n AS VARCHAR(2)), 2)
WHERE a.Codigo = 'AG-1';

-- 5 Itens por Peca nova
INSERT INTO dbo.EstruturaItem (AgrupamentoId, ComponenteId, Descricao, EstruturaPaiId, NivelHierarquico, Quantidade, QuantidadePorPai, RequerRelatorioDimensional)
SELECT p.AgrupamentoId, NULL, N'Item ' + CAST(i.n AS NVARCHAR(2)), p.Id, 'Item', 40, 1, 0
FROM dbo.EstruturaItem p
JOIN dbo.Agrupamento a ON a.Id = p.AgrupamentoId
JOIN @NovosPedidos np ON np.Id = a.PedidoId
CROSS JOIN (VALUES (1),(2),(3),(4),(5)) i(n)
WHERE a.Codigo = 'AG-1' AND p.NivelHierarquico = 'Peca';

-- Nos novos (todos os do Agrupamento dos Pedidos novos): Seq = numero do Pedido, Idx = posicao do Item na Peca
CREATE TABLE #No (Id INT PRIMARY KEY, Rn INT NOT NULL, PaiId INT NULL, Idx INT NOT NULL, Seq INT NOT NULL);
INSERT INTO #No (Id, Rn, PaiId, Idx, Seq)
SELECT e.Id, ROW_NUMBER() OVER (ORDER BY e.Id), e.EstruturaPaiId,
       ISNULL(CAST(RIGHT(e.Descricao, 1) AS INT), 0), np.Seq
FROM dbo.EstruturaItem e
JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId
JOIN @NovosPedidos np ON np.Id = a.PedidoId
WHERE a.Codigo = 'AG-1';

-- Roteiro de 4 passos em cada no novo
INSERT INTO dbo.EstruturaRoteiro (EstruturaItemId, SetorId, Ordem)
SELECT n.Id, s.Id, o.Ordem
FROM #No n
CROSS JOIN (VALUES (1, N'CARGA-6A Corte'), (2, N'CARGA-6A Dobra'), (3, N'CARGA-6A Solda'), (4, N'CARGA-6A Pintura')) o(Ordem, Nome)
JOIN dbo.Setor s ON s.Nome = o.Nome;

CREATE TABLE #Setor (Ordem INT PRIMARY KEY, Id INT NOT NULL);
INSERT INTO #Setor (Ordem, Id)
SELECT o.Ordem, s.Id
FROM (VALUES (1, N'CARGA-6A Corte'), (2, N'CARGA-6A Dobra'), (3, N'CARGA-6A Solda'), (4, N'CARGA-6A Pintura')) o(Ordem, Nome)
JOIN dbo.Setor s ON s.Nome = o.Nome;

-- Lotes (no, j) com profundidade (quantos eventos o lote cumpre) e o instante-base do no.
-- Evento 1 = Inicio; par 2k = Termino do passo k; impar 2k+1 = Entrega do passo k ao k+1; o 9 leva ao
-- AguardandoMontagem do Corte (Item) ou a NaExpedicao (Peca).
-- A Peca inicia o lote j (IniciaLote) conforme o cenario; o Item desse lote tem de estar em AguardandoMontagem (9).
CREATE TABLE #Lote (NoId INT NOT NULL, Rn INT NOT NULL, j INT NOT NULL, Peca BIT NOT NULL, PaiId INT NULL, Idx INT NOT NULL,
                    Prof INT NOT NULL, Base DATETIME2 NOT NULL, TMont DATETIME2 NOT NULL, PRIMARY KEY (NoId, j));
INSERT INTO #Lote (NoId, Rn, j, Peca, PaiId, Idx, Prof, Base, TMont)
SELECT n.Id, n.Rn, l.j, CASE WHEN n.PaiId IS NULL THEN 1 ELSE 0 END, n.PaiId, n.Idx,
       CASE
         WHEN n.PaiId IS NULL THEN ((n.Rn * 7 + l.j * 3) % 6) + 4                       -- Peca: 4..9 (so conta se iniciou o lote)
         WHEN n.Seq % 3 = 2 THEN 9                                                       -- Pecas iniciam os 3 lotes
         WHEN n.Seq % 3 = 1 AND l.j <= 2 THEN 9
         WHEN n.Seq % 3 = 0 AND l.j = 1 THEN 9                                           -- esperando montagem, Peca nao iniciada
         WHEN n.Seq % 3 = 0 THEN ((n.Rn * 7 + l.j * 3) % 5) + 2                          -- ainda em producao: 2..6
         ELSE ((n.Rn * 7 + l.j * 3) % 6) + 3                                             -- ainda em producao: 3..8
       END,
       DATEADD(MINUTE, ISNULL(n.Idx, 0) * 7 + l.j * 20, DATEADD(DAY, -(2 + n.Seq % 14), @agora)),
       DATEADD(MINUTE, 9 * 45 + l.j * 20 + 60, DATEADD(DAY, -(2 + n.Seq % 14), @agora))
FROM #No n CROSS JOIN (VALUES (1),(2),(3)) l(j);

-- A Peca so inicia o lote j se o cenario manda
DELETE p FROM #Lote p JOIN #No n ON n.Id = p.NoId
WHERE p.Peca = 1 AND NOT (n.Seq % 3 = 2 OR (n.Seq % 3 = 1 AND p.j <= 2));

-- Uma Montagem por (Peca, lote) iniciado, no instante TMont (que e posterior ao 9o evento de todos os 5 Itens)
INSERT INTO dbo.Montagem (EstruturaItemId, SetorId, Quantidade, DataHora, UsuarioId)
SELECT p.NoId, (SELECT Id FROM #Setor WHERE Ordem = 1), 10, p.TMont, @admin
FROM #Lote p WHERE p.Peca = 1;

WITH Ev AS (SELECT e FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) t(e)),
     Corrida AS (
        -- Itens: o livro do proprio lote; Pecas: o Inicio (com a Montagem) e o resto do caminho
        SELECT l.NoId, l.j, v.e, l.Peca,
               CASE WHEN l.Peca = 1 THEN DATEADD(MINUTE, (v.e - 1) * 45 + 1, l.TMont)
                    ELSE DATEADD(MINUTE, v.e * 45, l.Base) END AS DataHora,
               CASE WHEN v.e = 1 THEN 'Inicio' WHEN v.e % 2 = 0 THEN 'Termino' ELSE 'Entrega' END AS Tipo,
               CASE WHEN v.e = 1 THEN 1 ELSE v.e / 2 END AS Passo
        FROM #Lote l CROSS JOIN Ev v WHERE v.e <= l.Prof
     ),
     Linha AS (
        SELECT c.NoId, c.j, c.e, c.DataHora, c.Tipo, c.Passo, c.Peca, CAST(NULL AS INT) AS MontagemId FROM Corrida c WHERE NOT (c.Peca = 1 AND c.e = 1)
        UNION ALL
        -- Inicio da Peca: aponta a Montagem do lote
        SELECT c.NoId, c.j, 1, c.DataHora, 'Inicio', 1, 1, m.Id
        FROM Corrida c JOIN dbo.Montagem m ON m.EstruturaItemId = c.NoId AND m.DataHora = (SELECT TMont FROM #Lote WHERE NoId = c.NoId AND j = c.j)
        WHERE c.Peca = 1 AND c.e = 1
        UNION ALL
        -- Baixa de cada Item: AguardandoMontagem do Corte -> Montado, apontando a Montagem da Peca pai (e = 0: antes do Inicio)
        SELECT i.NoId, i.j, 0, m.DataHora, 'Montagem', 0, 0, m.Id
        FROM #Lote i
        JOIN dbo.Montagem m ON m.EstruturaItemId = i.PaiId AND m.DataHora = (SELECT TMont FROM #Lote WHERE NoId = i.PaiId AND j = i.j)
        WHERE i.Peca = 0
     )
INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                              DestinoPosicao, DestinoSetorId, DestinoOrdem, MontagemId, DataHora, UsuarioId)
SELECT l.NoId, l.Tipo, 10,
       CASE l.Tipo WHEN 'Inicio' THEN 'AIniciar' WHEN 'Termino' THEN 'NoSetor'
            WHEN 'Montagem' THEN 'AguardandoMontagem' ELSE 'AguardandoColeta' END,
       CASE WHEN l.Tipo = 'Inicio' THEN NULL WHEN l.Tipo = 'Montagem' THEN s1.Id ELSE so.Id END,
       CASE WHEN l.Tipo IN ('Inicio', 'Montagem') THEN NULL ELSE l.Passo END,
       CASE l.Tipo WHEN 'Inicio' THEN 'NoSetor' WHEN 'Termino' THEN 'AguardandoColeta' WHEN 'Montagem' THEN 'Montado'
            ELSE CASE WHEN l.Passo < 4 THEN 'NoSetor' WHEN l.Peca = 1 THEN 'NaExpedicao' ELSE 'AguardandoMontagem' END END,
       CASE WHEN l.Tipo = 'Inicio' THEN s1.Id WHEN l.Tipo = 'Termino' THEN so.Id WHEN l.Tipo = 'Montagem' THEN NULL
            WHEN l.Passo < 4 THEN sd.Id WHEN l.Peca = 1 THEN NULL ELSE s1.Id END,
       CASE WHEN l.Tipo = 'Inicio' THEN 1 WHEN l.Tipo = 'Termino' THEN l.Passo WHEN l.Tipo = 'Montagem' THEN NULL
            WHEN l.Passo < 4 THEN l.Passo + 1 ELSE NULL END,
       l.MontagemId, l.DataHora, @admin
FROM Linha l
JOIN #Setor s1 ON s1.Ordem = 1
LEFT JOIN #Setor so ON so.Ordem = l.Passo
LEFT JOIN #Setor sd ON sd.Ordem = l.Passo + 1
ORDER BY l.NoId, l.DataHora, l.j, l.e;

DROP TABLE #Setor;
DROP TABLE #No;
COMMIT TRANSACTION;

SELECT
  (SELECT COUNT(*) FROM dbo.Pedido WHERE Numero LIKE 'CARGA-6A-%') AS Pedidos,
  (SELECT COUNT(*) FROM dbo.EstruturaItem e JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId
     JOIN dbo.Pedido p ON p.Id = a.PedidoId WHERE p.Numero LIKE 'CARGA-6A-%') AS Nos,
  (SELECT COUNT(*) FROM dbo.Movimentacao m JOIN dbo.EstruturaItem e ON e.Id = m.EstruturaItemId
     JOIN dbo.Agrupamento a ON a.Id = e.AgrupamentoId
     JOIN dbo.Pedido p ON p.Id = a.PedidoId WHERE p.Numero LIKE 'CARGA-6A-%') AS LinhasDoLivro;
