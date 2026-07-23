SET NOCOUNT ON;
MERGE dbo.Perfil AS alvo
USING (VALUES ('Operador'),('Almoxarifado'),('PCP'),('Qualidade'),('Gestao'),('Administrador')) AS origem(Nome)
ON alvo.Nome = origem.Nome
WHEN NOT MATCHED THEN INSERT (Nome) VALUES (origem.Nome);

IF NOT EXISTS (SELECT 1 FROM dbo.Usuario WHERE NomeUsuario = 'admin')
INSERT INTO dbo.Usuario (NomeUsuario, SenhaHash, NomeCompleto, PerfilId, Ativo)
SELECT 'admin',
       '$2a$11$Q7Yd0m1s9k8N0oS0nF0mUe0m6mQ2m3bqk8y3Y0m0nJ8x5uV5mB3rS',
       'Administrador do Sistema',
       (SELECT Id FROM dbo.Perfil WHERE Nome = 'Administrador'),
       1;
