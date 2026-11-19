/* ---------------------------------------------------------------------
   Massa de carga da Fase 6A (C14) — OPCIONAL, a suite ignora que ela existe.
   50 Pedidos abertos CARGA-6A-001 a 050, 1 Agrupamento Avulso por Pedido, 10 Pecas por
   Agrupamento e 5 Itens por Peca (3.000 nos), Roteiro de 4 passos em cada no, e em media
   ~20 linhas de livro por no (~58 mil linhas), so com transicoes validas (Inicio, Termino
   e Entrega, em tres lotes parciais por no, cada lote parando num ponto diferente do
   caminho). DataHora espalhada pelos ultimos dias, para as idades diferirem.
   Serve para medir o GET /panorama com a fabrica carregada.

   Idempotente: o que ja existe (por codigo/numero) nao e recriado, e o livro so e
   gerado para os Pedidos criados NESTA execucao; tudo numa transacao. Cria os proprios
   Setores e Componentes (por codigo), sem depender do seed-demo.sql. Autor: 'admin'.
   Carregar com -b -f 65001 (ver CLAUDE.md, «Comandos»).
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

-- Nos novos (todos os do Agrupamento dos Pedidos novos)
CREATE TABLE #No (Id INT PRIMARY KEY, Rn INT NOT NULL);
INSERT INTO #No (Id, Rn)
SELECT e.Id, ROW_NUMBER() OVER (ORDER BY e.Id)
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

-- Livro: 3 lotes de 10 por no, cada um com profundidade 4..9 eventos (media ~6,5 => ~19,5 linhas/no).
-- Evento 1 = Inicio; par 2k = Termino do passo k; impar 2k+1 = Entrega do passo k ao k+1 (ou a NaExpedicao).
CREATE TABLE #Setor (Ordem INT PRIMARY KEY, Id INT NOT NULL);
INSERT INTO #Setor (Ordem, Id)
SELECT o.Ordem, s.Id
FROM (VALUES (1, N'CARGA-6A Corte'), (2, N'CARGA-6A Dobra'), (3, N'CARGA-6A Solda'), (4, N'CARGA-6A Pintura')) o(Ordem, Nome)
JOIN dbo.Setor s ON s.Nome = o.Nome;

WITH Lote AS (SELECT j FROM (VALUES (1),(2),(3)) t(j)),
     Ev   AS (SELECT e FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) t(e)),
     Gerado AS (
        SELECT n.Id AS NoId, n.Rn, l.j, v.e,
               ((n.Rn * 7 + l.j * 3) % 6) + 4 AS Profundidade
        FROM #No n CROSS JOIN Lote l CROSS JOIN Ev v
     ),
     Linha AS (
        SELECT g.NoId, g.j, g.e,
               DATEADD(MINUTE, g.e * 45 + g.j * 20, DATEADD(DAY, -(1 + g.Rn % 15), @agora)) AS DataHora,
               CASE WHEN g.e = 1 THEN 'Inicio' WHEN g.e % 2 = 0 THEN 'Termino' ELSE 'Entrega' END AS Tipo,
               CASE WHEN g.e = 1 THEN 1 ELSE g.e / 2 END AS Passo     -- passo de origem (Inicio: o 1 e destino)
        FROM Gerado g WHERE g.e <= g.Profundidade
     )
INSERT INTO dbo.Movimentacao (EstruturaItemId, Tipo, Quantidade, OrigemPosicao, OrigemSetorId, OrigemOrdem,
                              DestinoPosicao, DestinoSetorId, DestinoOrdem, DataHora, UsuarioId)
SELECT l.NoId, l.Tipo, 10,
       CASE l.Tipo WHEN 'Inicio' THEN 'AIniciar' WHEN 'Termino' THEN 'NoSetor' ELSE 'AguardandoColeta' END,
       CASE WHEN l.Tipo = 'Inicio' THEN NULL ELSE so.Id END,
       CASE WHEN l.Tipo = 'Inicio' THEN NULL ELSE l.Passo END,
       CASE l.Tipo WHEN 'Inicio' THEN 'NoSetor' WHEN 'Termino' THEN 'AguardandoColeta'
            ELSE CASE WHEN l.Passo < 4 THEN 'NoSetor' ELSE 'NaExpedicao' END END,
       CASE WHEN l.Tipo = 'Inicio' THEN s1.Id WHEN l.Tipo = 'Termino' THEN so.Id
            WHEN l.Passo < 4 THEN sd.Id ELSE NULL END,
       CASE WHEN l.Tipo = 'Inicio' THEN 1 WHEN l.Tipo = 'Termino' THEN l.Passo
            WHEN l.Passo < 4 THEN l.Passo + 1 ELSE NULL END,
       l.DataHora, @admin
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
