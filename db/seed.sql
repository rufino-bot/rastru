SET NOCOUNT ON;
MERGE dbo.Perfil AS alvo
USING (VALUES ('Operador'),('Separador'),('Movimentador'),('PCP'),('Qualidade'),('Gestao'),('Administrador')) AS origem(Nome)
ON alvo.Nome = origem.Nome
WHEN NOT MATCHED THEN INSERT (Nome) VALUES (origem.Nome);

-- Hash BCrypt real (custo 11), gerado com BCryptPasswordHasher (Task 4), válido para a
-- senha 'Admin@123'. Ver tests/Rastreamento.Infrastructure.Tests/Security/SeedAdminSenhaTests.cs.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'admin')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'admin',
       '$2a$11$XdGh9XVWVeYjBsgH0t4xPOh8Sh3T/qHH.7ZC6eEO6CwO9jsTICQaC',
       'Administrador do Sistema',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Administrador'),
       1;

-- Segundo usuario do seed (achado B11, review da Task 10): com um unico usuario no banco, a prova
-- de autoria no nivel HTTP era degenerada — trocar `usuarioId.Value` por um literal `1` nos
-- controllers coincidia com o Id do unico usuario e passava. Hash BCrypt real (custo 11), gerado
-- com o mesmo BCryptPasswordHasher de producao, valido para a senha 'Pcp@123' — distinto do hash
-- do admin de proposito, para nao virar mina para quem tentar logar depois.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'pcp')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'pcp',
       '$2a$11$gbL2eZQIk1S1zAYieDUJO.Um1Sbom9oC56Xpd3RcdYdIYRDygpSuG',
       'Planejamento e Controle',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'PCP'),
       1;

-- Usuarios de perfil sem escrita de cadastro, para ver a tela como o chao de fabrica a ve: o
-- 'operador' inicia e termina no Setor, o 'movimentador' leva o que ficou pronto (as Tarefas). Sem
-- eles, a verificacao manual do gating por perfil precisava recria-los a mao a cada banco novo.
-- Mesmo padrao do 'pcp': hash BCrypt real (custo 11), gerado com o BCryptPasswordHasher de
-- producao, com senha e hash proprios — 'Operador@123' e 'Movimentador@123'.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'operador')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'operador',
       '$2a$11$xRh4vVSSJLbrNKEmCyrKfusUk4OY0W0QTG9kflfaZCGv7563GPLMK',
       N'Operador de Produção',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Operador'),
       1;

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'movimentador')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'movimentador',
       '$2a$11$3EX6uhKCdVGYnT9RlXowuuUbGwW2hHAx8gYKeMRMY3k1lZhf62JC6',
       'Movimentador de Materiais',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Movimentador'),
       1;

-- O 'separador' (Fase 4) separa material no almoxarifado: e o perfil que a lista de pendencias serve.
-- Mesmo padrao do 'operador' e do 'movimentador': hash BCrypt real (custo 11), gerado com o
-- BCryptPasswordHasher de producao, com senha e hash proprios — 'Separador@123'.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'separador')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'separador',
       '$2a$11$wjxXcXaIyKIsTnb9Bx6w9eVAQYIIQfpmqsWI0lGbXNky7QIiBjkrq',
       N'Separador de Materiais',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Separador'),
       1;

-- O 'qualidade' (Fase 5A) afere, conta, manda para conserto, pede liberacao e expede: no Rastru o perfil
-- Qualidade e a pessoa da expedicao (D1 da spec da Fase 5A). Mesmo padrao do 'separador': hash BCrypt
-- real (custo 11), gerado com o BCryptPasswordHasher de producao, com senha e hash proprios —
-- 'Qualidade@123'.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'qualidade')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'qualidade',
       '$2a$11$M49ee1JGq.83ZsOnjeTJUOTditPqAK2cWd2AZ2XlazlAV7bijDooK',
       N'Qualidade e Expedição',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Qualidade'),
       1;

-- O 'gestao' (Fase 5A) responde aos pedidos de liberacao da Qualidade (D7). Mesmo padrao, senha e hash
-- proprios — 'Gestao@123'.
IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'gestao')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'gestao',
       '$2a$11$uzJx2sA0I00h0HJvUfYiWOHhfIvOqmdY/sqXwMGlYxEnPkgWU9.Jq',
       N'Gestão',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Gestao'),
       1;
