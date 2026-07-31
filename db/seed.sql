SET NOCOUNT ON;
MERGE dbo.Perfil AS alvo
USING (VALUES ('Operador'),('Almoxarifado'),('PCP'),('Qualidade'),('Gestao'),('Administrador')) AS origem(Nome)
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
