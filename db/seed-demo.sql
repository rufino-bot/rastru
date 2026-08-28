-- =====================================================================
-- MASSA DE DEMONSTRACAO — conveniencia de desenvolvimento, NAO requisito.
--
-- NAO confundir com db/seed.sql, que e o MINIMO para o sistema funcionar
-- (perfis + usuarios) e continua sendo o unico obrigatorio.
--
-- NENHUM teste automatizado pode depender deste arquivo. Api.Tests e
-- Infrastructure.Tests criam a propria massa, e e isso que os torna
-- deterministicos numa maquina qualquer. A dependencia e de mao unica:
-- a verificacao MANUAL usa este demo, a suite ignora que ele existe.
--
-- Idempotente: rodar duas vezes nao duplica linha.
-- =====================================================================

-- Todo literal de texto aqui e prefixado com N'...' de proposito: as colunas sao NVARCHAR e a
-- carga passa por sqlcmd dentro do container. Sem o N, acentuacao portuguesa ('Rebarbacao',
-- 'Inspecao') entra corrompida e o banco fica verde assim mesmo.

SET NOCOUNT ON;

/* ---------------------------------------------------------------------
   SETORES (10) — chao de fabrica metalurgica, na ordem tipica do fluxo
   --------------------------------------------------------------------- */

MERGE dbo.Setor AS alvo
USING (VALUES
    (N'Corte a Laser'),
    (N'Dobra'),
    (N'Usinagem'),
    (N'Solda'),
    (N'Rebarbação'),
    (N'Tratamento Térmico'),
    (N'Jateamento'),
    (N'Pintura'),
    (N'Montagem'),
    (N'Inspeção Dimensional')
) AS origem (Nome)
ON alvo.Nome = origem.Nome
WHEN NOT MATCHED THEN INSERT (Nome, Ativo) VALUES (origem.Nome, 1);

/* ---------------------------------------------------------------------
   MATERIAIS (17) — prefixos por familia (MAT-CH, MAT-TB, MAT-BR,
   MAT-PF, MAT-CS, MAT-CN) para a busca por codigo ter o que filtrar, e
   palavras repetidas nas descricoes ('Chapa de aço', 'Tubo', 'Barra',
   'Perfil') para a busca por descricao tambem ter.
   --------------------------------------------------------------------- */

MERGE dbo.Material AS alvo
USING (VALUES
    (N'MAT-CH-001', N'Chapa de aço carbono SAE 1020 3,00 mm',              N'KG'),
    (N'MAT-CH-002', N'Chapa de aço carbono SAE 1020 4,75 mm',              N'KG'),
    (N'MAT-CH-003', N'Chapa de aço carbono SAE 1045 6,35 mm',              N'KG'),
    (N'MAT-CH-004', N'Chapa de aço inoxidável AISI 304 2,00 mm',           N'KG'),
    (N'MAT-CH-005', N'Chapa de alumínio 5052 3,00 mm',                     N'KG'),
    (N'MAT-TB-001', N'Tubo redondo de aço carbono 2" parede 2,25 mm',      N'M'),
    (N'MAT-TB-002', N'Tubo quadrado de aço carbono 40 x 40 mm parede 2,00 mm', N'M'),
    (N'MAT-BR-001', N'Barra redonda de aço SAE 1045 diâmetro 1"',          N'M'),
    (N'MAT-BR-002', N'Barra chata de aço carbono 2" x 1/4"',               N'M'),
    (N'MAT-PF-001', N'Perfil U de aço carbono 100 x 50 mm',                N'M'),
    (N'MAT-PF-002', N'Perfil cantoneira de aço carbono 2" x 1/4"',         N'M'),
    (N'MAT-CS-001', N'Parafuso sextavado M10 x 40 mm classe 8.8',          N'UN'),
    (N'MAT-CS-002', N'Porca sextavada M10 classe 8',                       N'UN'),
    (N'MAT-CS-003', N'Arruela lisa M10 zincada',                           N'UN'),
    (N'MAT-CN-001', N'Eletrodo revestido E6013 3,25 mm',                   N'KG'),
    (N'MAT-CN-002', N'Arame de solda MIG ER70S-6 1,20 mm',                 N'KG'),
    (N'MAT-CN-003', N'Tinta esmalte sintético cinza N6.5',                 N'KG')
) AS origem (Codigo, Descricao, UnidadeMedida)
ON alvo.Codigo = origem.Codigo
WHEN NOT MATCHED THEN
    INSERT (Codigo, Descricao, UnidadeMedida, Ativo)
    VALUES (origem.Codigo, origem.Descricao, origem.UnidadeMedida, 1);

/* ---------------------------------------------------------------------
   COMPONENTES (54) — catalogo de um transportador de correia.
   Tipo respeita o CHECK do schema: Bruto | Fabricado | Montagem.
   Prefixos: MT- montagem, PA- painel, CH- chapa, EX- eixo, SP- suporte,
   PF- perfil, TB- tubo, FL- flange, BS- base, BR- bruto.
   Sao 54 > 20 (tamanho de pagina padrao), entao a listagem da 3 paginas.
   --------------------------------------------------------------------- */

MERGE dbo.Componente AS alvo
USING (VALUES
    -- Montagens
    (N'MT-1000', N'Transportador de correia 6 m montado completo',        N'Montagem',  1),
    (N'MT-1010', N'Chassi soldado do transportador 6 m',                  N'Montagem',  1),
    (N'MT-1020', N'Conjunto tambor motriz montado',                       N'Montagem',  1),
    (N'MT-1030', N'Conjunto tambor movido montado',                       N'Montagem',  1),
    (N'MT-1040', N'Conjunto esticador do transportador',                  N'Montagem',  1),
    (N'MT-1050', N'Conjunto de guarda-corpo do transportador',            N'Montagem',  1),
    (N'MT-1060', N'Conjunto de calha de descarga',                        N'Montagem',  1),
    (N'MT-1070', N'Conjunto de mesa de impacto',                          N'Montagem',  1),
    -- Paineis
    (N'PA-2010', N'Painel lateral de chapa dobrada 6 m',                  N'Fabricado', 1),
    (N'PA-2020', N'Painel lateral de chapa dobrada 4 m',                  N'Fabricado', 1),
    (N'PA-2030', N'Painel de fechamento frontal',                         N'Fabricado', 1),
    (N'PA-2040', N'Painel de fechamento traseiro',                        N'Fabricado', 1),
    (N'PA-2050', N'Painel de inspeção com dobradiça',                     N'Fabricado', 1),
    (N'PA-2060', N'Painel de proteção do acionamento',                    N'Fabricado', 1),
    (N'PA-2070', N'Painel perfurado de ventilação',                       N'Fabricado', 1),
    -- Chapas
    (N'CH-2110', N'Chapa de fechamento inferior do chassi',               N'Fabricado', 1),
    (N'CH-2120', N'Chapa de reforço da longarina',                        N'Fabricado', 1),
    (N'CH-2130', N'Virola de chapa calandrada do tambor',                 N'Fabricado', 1),
    (N'CH-2140', N'Disco de tampa do tambor em chapa',                    N'Fabricado', 1),
    (N'CH-2150', N'Chapa de base do mancal',                              N'Fabricado', 1),
    (N'CH-2160', N'Chapa de reforço do suporte de motor',                 N'Fabricado', 1),
    (N'CH-2170', N'Chapa guia de alinhamento da correia',                 N'Fabricado', 1),
    (N'CH-2180', N'Chapa de desgaste da calha de descarga',               N'Fabricado', 1),
    (N'CH-2190', N'Chapa de reforço do esticador',                        N'Fabricado', 1),
    (N'CH-2200', N'Chapa de identificação do equipamento',                N'Fabricado', 1),
    -- Eixos
    (N'EX-2210', N'Eixo do tambor motriz usinado',                        N'Fabricado', 1),
    (N'EX-2220', N'Eixo do tambor movido usinado',                        N'Fabricado', 1),
    (N'EX-2230', N'Eixo do rolete de retorno usinado',                    N'Fabricado', 1),
    (N'EX-2240', N'Eixo do rolete de carga usinado',                      N'Fabricado', 1),
    (N'EX-2250', N'Eixo do esticador roscado',                            N'Fabricado', 1),
    (N'EX-2260', N'Eixo de articulação do painel de inspeção',            N'Fabricado', 1),
    (N'EX-2270', N'Eixo bruto de aço SAE 1045 sem usinagem',              N'Bruto',     1),
    -- Suportes
    (N'SP-2050', N'Suporte de mancal soldado',                            N'Fabricado', 1),
    (N'SP-2310', N'Suporte de motorredutor',                              N'Fabricado', 1),
    (N'SP-2320', N'Suporte de rolete de carga',                           N'Fabricado', 1),
    (N'SP-2330', N'Suporte de rolete de retorno',                         N'Fabricado', 1),
    (N'SP-2340', N'Suporte de sensor de desalinhamento',                  N'Fabricado', 1),
    (N'SP-2350', N'Suporte de guarda-corpo',                              N'Fabricado', 1),
    -- Perfis
    (N'PF-3010', N'Longarina de perfil U 100 mm cortada',                 N'Fabricado', 1),
    (N'PF-3020', N'Travessa de perfil U 100 mm cortada',                  N'Fabricado', 1),
    (N'PF-3030', N'Cantoneira de reforço cortada',                        N'Fabricado', 1),
    (N'PF-3040', N'Coluna de perfil U do guarda-corpo',                   N'Fabricado', 1),
    -- Tubos
    (N'TB-3110', N'Tubo do rolete de carga cortado',                      N'Fabricado', 1),
    (N'TB-3120', N'Tubo do rolete de retorno cortado',                    N'Fabricado', 1),
    (N'TB-3130', N'Tubo do corrimão do guarda-corpo',                     N'Fabricado', 1),
    (N'TB-3140', N'Tubo espaçador do esticador',                          N'Fabricado', 1),
    -- Flanges e bases
    (N'FL-3210', N'Flange de fixação do mancal',                          N'Fabricado', 1),
    (N'FL-3220', N'Flange cega de inspeção',                              N'Fabricado', 1),
    (N'BS-3310', N'Base de apoio do transportador',                       N'Fabricado', 1),
    (N'BS-3320', N'Base de apoio do motorredutor',                        N'Fabricado', 1),
    -- Brutos
    (N'BR-4010', N'Barra bruta redonda de aço SAE 1045',                  N'Bruto',     1),
    (N'BR-4020', N'Chapa bruta de aço carbono 4,75 mm',                   N'Bruto',     1),
    -- Inativos DE PROPOSITO: a listagem so os mostra com IncluirInativos, e um pai inativo recusa
    -- escrita de receita (regra da Fase 1C). Servem para conferir os dois comportamentos na tela.
    (N'CH-2900', N'Chapa de reforço da longarina (descontinuada)',        N'Fabricado', 0),
    (N'PA-2900', N'Painel lateral de chapa rebitada (descontinuado)',     N'Fabricado', 0)
) AS origem (Codigo, Descricao, Tipo, Ativo)
ON alvo.Codigo = origem.Codigo
WHEN NOT MATCHED THEN
    INSERT (Codigo, Descricao, Tipo, Ativo)
    VALUES (origem.Codigo, origem.Descricao, origem.Tipo, origem.Ativo);

/* ---------------------------------------------------------------------
   RECEITAS PADRAO — 3 completas (filhos + materiais + roteiro).
   O grafo abaixo e um DAG, conferido a mao, SEM NENHUM CICLO:

       MT-1000 -> MT-1010 -> {PF-3010, PF-3020, CH-2110, CH-2120, SP-2050}
               -> MT-1020 -> {EX-2210, CH-2130, CH-2140, FL-3210}
               -> MT-1030 -> {EX-2220, CH-2130, CH-2140}
               -> {PA-2010, SP-2050, BS-3310}

   Nenhum filho volta a ser ancestral de si mesmo. SP-2050, CH-2130 e
   CH-2140 aparecem sob mais de um pai — isso e reuso, nao ciclo.
   --------------------------------------------------------------------- */

INSERT INTO dbo.ComponenteFilhoPadrao (ComponentePaiId, ComponenteFilhoId, QuantidadePadrao)
SELECT pai.Id, filho.Id, r.QuantidadePadrao
FROM (VALUES
    -- Receita 1: transportador completo
    (N'MT-1000', N'MT-1010', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1000', N'MT-1020', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1000', N'MT-1030', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1000', N'PA-2010', CAST(2  AS DECIMAL(18,4))),
    (N'MT-1000', N'SP-2050', CAST(4  AS DECIMAL(18,4))),
    (N'MT-1000', N'BS-3310', CAST(2  AS DECIMAL(18,4))),
    -- Receita 2: chassi soldado (a que tem o retorno ao setor no roteiro)
    (N'MT-1010', N'PF-3010', CAST(2  AS DECIMAL(18,4))),
    (N'MT-1010', N'PF-3020', CAST(8  AS DECIMAL(18,4))),
    (N'MT-1010', N'CH-2110', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1010', N'CH-2120', CAST(4  AS DECIMAL(18,4))),
    (N'MT-1010', N'SP-2050', CAST(4  AS DECIMAL(18,4))),
    -- Receita 3: conjunto tambor motriz
    (N'MT-1020', N'EX-2210', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1020', N'CH-2130', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1020', N'CH-2140', CAST(2  AS DECIMAL(18,4))),
    (N'MT-1020', N'FL-3210', CAST(2  AS DECIMAL(18,4))),
    -- Receita parcial de apoio, para o tambor movido nao ficar vazio na tela
    (N'MT-1030', N'EX-2220', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1030', N'CH-2130', CAST(1  AS DECIMAL(18,4))),
    (N'MT-1030', N'CH-2140', CAST(2  AS DECIMAL(18,4)))
) AS r (PaiCodigo, FilhoCodigo, QuantidadePadrao)
JOIN dbo.Componente pai   ON pai.Codigo   = r.PaiCodigo
JOIN dbo.Componente filho ON filho.Codigo = r.FilhoCodigo
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ComponenteFilhoPadrao existente
    WHERE existente.ComponentePaiId   = pai.Id
      AND existente.ComponenteFilhoId = filho.Id
);

INSERT INTO dbo.ComponenteMaterialPadrao (ComponenteId, MaterialId, QuantidadePadrao)
SELECT c.Id, m.Id, r.QuantidadePadrao
FROM (VALUES
    -- Receita 1
    (N'MT-1000', N'MAT-CS-001', CAST(48      AS DECIMAL(18,4))),
    (N'MT-1000', N'MAT-CS-002', CAST(48      AS DECIMAL(18,4))),
    (N'MT-1000', N'MAT-CS-003', CAST(96      AS DECIMAL(18,4))),
    (N'MT-1000', N'MAT-CN-003', CAST(3.5     AS DECIMAL(18,4))),
    -- Receita 2
    (N'MT-1010', N'MAT-PF-001', CAST(24.0000 AS DECIMAL(18,4))),
    (N'MT-1010', N'MAT-CH-002', CAST(86.4000 AS DECIMAL(18,4))),
    (N'MT-1010', N'MAT-CN-001', CAST(2.2500  AS DECIMAL(18,4))),
    (N'MT-1010', N'MAT-CN-002', CAST(4.8000  AS DECIMAL(18,4))),
    (N'MT-1010', N'MAT-CN-003', CAST(1.8000  AS DECIMAL(18,4))),
    -- Receita 3
    (N'MT-1020', N'MAT-BR-001', CAST(1.4000  AS DECIMAL(18,4))),
    (N'MT-1020', N'MAT-CH-003', CAST(32.6000 AS DECIMAL(18,4))),
    (N'MT-1020', N'MAT-CN-002', CAST(0.9000  AS DECIMAL(18,4))),
    -- Apoio
    (N'EX-2210', N'MAT-BR-001', CAST(1.4000  AS DECIMAL(18,4))),
    (N'CH-2130', N'MAT-CH-003', CAST(18.4000 AS DECIMAL(18,4))),
    (N'PA-2010', N'MAT-CH-001', CAST(41.2000 AS DECIMAL(18,4)))
) AS r (ComponenteCodigo, MaterialCodigo, QuantidadePadrao)
JOIN dbo.Componente c ON c.Codigo = r.ComponenteCodigo
JOIN dbo.Material   m ON m.Codigo = r.MaterialCodigo
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ComponenteMaterialPadrao existente
    WHERE existente.ComponenteId = c.Id
      AND existente.MaterialId   = m.Id
);

-- Roteiro padrao. A UNIQUE da tabela e (ComponenteId, Ordem) — NAO (ComponenteId, SetorId) —, e e
-- exatamente isso que permite o RETORNO AO SETOR: no roteiro do MT-1010, 'Solda' aparece na ordem 3
-- e de novo na ordem 5, porque a peca volta a soldar depois da usinagem dos alojamentos. Nao e bug
-- da tela nem do schema; e o caso real que a massa existe para deixar visivel.
INSERT INTO dbo.ComponenteRoteiroPadrao (ComponenteId, SetorId, Ordem)
SELECT c.Id, s.Id, r.Ordem
FROM (VALUES
    -- Receita 1: montagem final
    (N'MT-1000', N'Montagem',             1),
    (N'MT-1000', N'Inspeção Dimensional', 2),
    (N'MT-1000', N'Pintura',              3),
    -- Receita 2: chassi soldado — SOLDA NA ORDEM 3 E DE NOVO NA ORDEM 5 (retorno ao setor)
    (N'MT-1010', N'Corte a Laser',        1),
    (N'MT-1010', N'Dobra',                2),
    (N'MT-1010', N'Solda',                3),
    (N'MT-1010', N'Usinagem',             4),
    (N'MT-1010', N'Solda',                5),
    (N'MT-1010', N'Rebarbação',           6),
    (N'MT-1010', N'Jateamento',           7),
    (N'MT-1010', N'Pintura',              8),
    (N'MT-1010', N'Inspeção Dimensional', 9),
    -- Receita 3: conjunto tambor motriz
    (N'MT-1020', N'Corte a Laser',        1),
    (N'MT-1020', N'Usinagem',             2),
    (N'MT-1020', N'Solda',                3),
    (N'MT-1020', N'Rebarbação',           4),
    (N'MT-1020', N'Inspeção Dimensional', 5),
    -- Apoio
    (N'EX-2210', N'Usinagem',             1),
    (N'EX-2210', N'Tratamento Térmico',   2),
    (N'EX-2210', N'Inspeção Dimensional', 3),
    (N'PA-2010', N'Corte a Laser',        1),
    (N'PA-2010', N'Dobra',                2),
    (N'PA-2010', N'Rebarbação',           3),
    (N'PA-2010', N'Pintura',              4)
) AS r (ComponenteCodigo, SetorNome, Ordem)
JOIN dbo.Componente c ON c.Codigo = r.ComponenteCodigo
JOIN dbo.Setor      s ON s.Nome   = r.SetorNome
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.ComponenteRoteiroPadrao existente
    WHERE existente.ComponenteId = c.Id
      AND existente.Ordem        = r.Ordem
);
