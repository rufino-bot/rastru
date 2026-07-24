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
