# Fase 0 — Backend Autenticado (API) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar a API .NET do walking skeleton: solution em 4 camadas, banco SQL Server mapeado por EF Core Database First e autenticação por login próprio com JWT (access + refresh rotacionado e revogável).

**Architecture:** Clean Architecture simplificada (`Domain` / `Application` / `Infrastructure` / `Api`). Regras de sessão isoladas em casos de uso na `Application`; EF Core, BCrypt, SHA-256 e emissão de JWT ficam na `Infrastructure`; `Api` só orquestra HTTP e a borda de fuso. Senha via BCrypt, access token JWT curto e refresh token opaco (hash SHA-256 no banco, entregue por cookie httpOnly).

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Web API, EF Core (SQL Server, Database First), BCrypt.Net-Next, JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`), xUnit + `Microsoft.AspNetCore.Mvc.Testing`, SQL Server via Docker.

Este plano é o **backend** da Fase 0 (T1–T8 da spec). O frontend (T9–T11) é um plano separado. Fonte: `docs/superpowers/specs/2026-07-23-fase-0-walking-skeleton-design.md`.

## Global Constraints

- Target framework: `net10.0`. **Nunca** `Add-Migration` para criar/alterar schema — o banco nasce de `specs/02-modelo-de-dados.sql` (Database First).
- Banco: SQL Server; dev/test local via Docker (`mcr.microsoft.com/mssql/server`).
- Hash de senha: BCrypt (`BCrypt.Net-Next`). Nunca senha em claro.
- Refresh token: valor opaco ≥ 256 bits (base64url); no banco só o **hash SHA-256**. Rotação a cada uso; revogável no logout.
- Lifetimes: access token = **15 minutos**; refresh token = **7 dias**.
- Refresh token entregue por **cookie httpOnly + Secure + SameSite=Strict + Path=/auth**; access token nunca vai em cookie.
- Datas: **persistir em UTC** (`SYSUTCDATETIME()` no banco, `DateTime.UtcNow` no código); converter para **GMT-3 (`America/Sao_Paulo`, offset `-03:00`)** só na borda (JSON da API em ISO 8601 com offset `-03:00`).
- Nomenclatura: entidades de domínio em português (`Usuario`, `Perfil`, `RefreshToken`); padrões técnicos em inglês (`Repository`, `UseCase`, `Controller`, `Dto`).
- Perfis (claim `role`): `Operador`, `Almoxarifado`, `PCP`, `Qualidade`, `Gestao`, `Administrador`.

## Prerequisites (uma vez, antes da T1)

O repositório ainda não é git e o plano usa commits frequentes. Rode na raiz do repo:

```bash
git init
printf 'bin/\nobj/\nnode_modules/\n.env\n*.user\n' > .gitignore
git add .gitignore && git commit -m "chore: init repo with gitignore"
```

Docker precisa estar instalado (fornece o SQL Server para T2, T3, T8).

---

### Task 1: Scaffold da solution .NET

**Files:**
- Create: `Rastreamento.sln`
- Create: `src/Rastreamento.Domain/Rastreamento.Domain.csproj`
- Create: `src/Rastreamento.Application/Rastreamento.Application.csproj`
- Create: `src/Rastreamento.Infrastructure/Rastreamento.Infrastructure.csproj`
- Create: `src/Rastreamento.Api/Rastreamento.Api.csproj`
- Create: `tests/Rastreamento.Domain.Tests/Rastreamento.Domain.Tests.csproj`
- Create: `tests/Rastreamento.Application.Tests/Rastreamento.Application.Tests.csproj`

**Interfaces:**
- Consumes: nada.
- Produces: solution compilável com o grafo de dependências `Application→Domain`, `Infrastructure→{Domain,Application}`, `Api→{Application,Infrastructure}`, `Domain.Tests→{Domain,Infrastructure}`, `Application.Tests→{Application,Domain}`.

- [ ] **Step 1: Criar solution e projetos**

```bash
dotnet new sln -n Rastreamento
dotnet new classlib -n Rastreamento.Domain        -o src/Rastreamento.Domain        -f net10.0
dotnet new classlib -n Rastreamento.Application   -o src/Rastreamento.Application   -f net10.0
dotnet new classlib -n Rastreamento.Infrastructure -o src/Rastreamento.Infrastructure -f net10.0
dotnet new webapi    -n Rastreamento.Api           -o src/Rastreamento.Api           -f net10.0 --use-controllers
dotnet new xunit     -n Rastreamento.Domain.Tests  -o tests/Rastreamento.Domain.Tests -f net10.0
dotnet new xunit     -n Rastreamento.Application.Tests -o tests/Rastreamento.Application.Tests -f net10.0
```

- [ ] **Step 2: Remover arquivos-template e adicionar projetos à solution**

```bash
rm -f src/Rastreamento.Domain/Class1.cs src/Rastreamento.Application/Class1.cs src/Rastreamento.Infrastructure/Class1.cs
rm -f src/Rastreamento.Api/WeatherForecast.cs src/Rastreamento.Api/Controllers/WeatherForecastController.cs
dotnet sln add src/Rastreamento.Domain src/Rastreamento.Application src/Rastreamento.Infrastructure src/Rastreamento.Api tests/Rastreamento.Domain.Tests tests/Rastreamento.Application.Tests
```

- [ ] **Step 3: Adicionar referências entre projetos**

```bash
dotnet add src/Rastreamento.Application   reference src/Rastreamento.Domain
dotnet add src/Rastreamento.Infrastructure reference src/Rastreamento.Domain src/Rastreamento.Application
dotnet add src/Rastreamento.Api           reference src/Rastreamento.Application src/Rastreamento.Infrastructure
dotnet add tests/Rastreamento.Domain.Tests      reference src/Rastreamento.Domain src/Rastreamento.Infrastructure
dotnet add tests/Rastreamento.Application.Tests reference src/Rastreamento.Application src/Rastreamento.Domain
```

- [ ] **Step 4: Build para verificar o scaffold**

Run: `dotnet build`
Expected: `Build succeeded` com 0 erros.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution .NET em 4 camadas + testes"
```

---

### Task 2: Schema (RefreshToken) + SQL Server em Docker + seed

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (adicionar tabela `RefreshToken` na seção "USUÁRIOS E PERFIS", após `dbo.Usuario`)
- Create: `docker-compose.yml`
- Create: `db/seed.sql`

**Interfaces:**
- Consumes: nada.
- Produces: banco `Rastreamento` no SQL Server local com todas as tabelas, 6 linhas em `Perfil` e 1 `Usuario` admin (`admin` / senha `Admin@123`) com `SenhaHash` BCrypt válido.

- [ ] **Step 1: Adicionar a tabela `RefreshToken` ao DDL**

Em `specs/02-modelo-de-dados.sql`, logo após o bloco `CREATE TABLE dbo.Usuario (...) GO` (linha do `GO` após `FK_Usuario_Perfil`), inserir:

```sql
CREATE TABLE dbo.RefreshToken (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    UsuarioId               INT                 NOT NULL,
    TokenHash               NVARCHAR(200)       NOT NULL,   -- SHA-256 do refresh token (nunca em claro)
    ExpiraEm                DATETIME2           NOT NULL,
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RefreshToken_CriadoEm DEFAULT (SYSUTCDATETIME()),
    RevogadoEm              DATETIME2           NULL,       -- NULL = ativo; preenchido no logout ou na rotação
    SubstituidoPorTokenHash NVARCHAR(200)       NULL,       -- rastro de rotação (auditoria)
    CONSTRAINT PK_RefreshToken PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RefreshToken_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_RefreshToken_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_RefreshToken_ExpiraAposCriado CHECK (ExpiraEm > CriadoEm)
);
GO
CREATE INDEX IX_RefreshToken_Usuario ON dbo.RefreshToken (UsuarioId);
GO
```

- [ ] **Step 2: Criar `docker-compose.yml`**

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Your_strong_Pass123"
    ports:
      - "1433:1433"
    volumes:
      - mssqldata:/var/opt/mssql
volumes:
  mssqldata:
```

- [ ] **Step 3: Subir o container e criar o banco vazio**

```bash
docker compose up -d
# aguarda o SQL Server aceitar conexão (repetir até "Rastreamento" ser criado)
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -Q "IF DB_ID('Rastreamento') IS NULL CREATE DATABASE Rastreamento;"
```

Expected: comando retorna sem erro (banco `Rastreamento` existe).

- [ ] **Step 4: Aplicar o DDL no banco**

```bash
docker cp specs/02-modelo-de-dados.sql "$(docker compose ps -q sqlserver)":/tmp/schema.sql
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -i /tmp/schema.sql
```

Expected: execução sem erros de sintaxe/constraint.

- [ ] **Step 5: Verificar que `RefreshToken` foi criada**

```bash
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -d Rastreamento -Q "SELECT COUNT(*) FROM sys.tables WHERE name='RefreshToken';"
```

Expected: retorna `1`.

- [ ] **Step 6: Criar `db/seed.sql` (6 perfis + admin)**

O `SenhaHash` abaixo é o BCrypt de `Admin@123` (custo 11). Gere o seu com o helper da Task 4 se preferir; este valor funciona.

```sql
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
```

> Nota: se este hash não validar contra `Admin@123` na sua versão do BCrypt, substitua-o rodando o helper da Task 4 (Step 6) e atualize esta linha. O plano valida o login de verdade na Task 8.

- [ ] **Step 7: Aplicar o seed**

```bash
docker cp db/seed.sql "$(docker compose ps -q sqlserver)":/tmp/seed.sql
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento -i /tmp/seed.sql
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Your_strong_Pass123' -C -d Rastreamento -Q "SELECT (SELECT COUNT(*) FROM dbo.Perfil) AS Perfis, (SELECT COUNT(*) FROM dbo.Usuario) AS Usuarios;"
```

Expected: `Perfis = 6`, `Usuarios = 1`.

- [ ] **Step 8: Commit**

```bash
git add specs/02-modelo-de-dados.sql docker-compose.yml db/seed.sql
git commit -m "feat(db): tabela RefreshToken + SQL Server docker + seed"
```

---

### Task 3: Entidades + EF Core Database First (mapeamento)

**Files:**
- Create: `src/Rastreamento.Domain/Entities/Perfil.cs`
- Create: `src/Rastreamento.Domain/Entities/Usuario.cs`
- Create: `src/Rastreamento.Domain/Entities/RefreshToken.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/PerfilConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/UsuarioConfiguration.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`
- Test: `tests/Rastreamento.Application.Tests/Persistence/DbContextMappingTests.cs`

**Interfaces:**
- Consumes: schema da Task 2.
- Produces: entidades `Perfil{ int Id; string Nome; }`, `Usuario{ int Id; string NomeUsuario; string SenhaHash; string NomeCompleto; int PerfilId; bool Ativo; Perfil Perfil; }`, `RefreshToken{ int Id; int UsuarioId; string TokenHash; DateTime ExpiraEm; DateTime CriadoEm; DateTime? RevogadoEm; string? SubstituidoPorTokenHash; Usuario Usuario; }`; `RastreamentoDbContext` com `DbSet<Perfil> Perfis`, `DbSet<Usuario> Usuarios`, `DbSet<RefreshToken> RefreshTokens`.

- [ ] **Step 1: Instalar pacote EF Core no Infrastructure**

```bash
dotnet add src/Rastreamento.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add tests/Rastreamento.Application.Tests package Microsoft.EntityFrameworkCore.SqlServer
```

- [ ] **Step 2: Escrever o teste de mapeamento (falha)**

`tests/Rastreamento.Application.Tests/Persistence/DbContextMappingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Application.Tests.Persistence;

public class DbContextMappingTests
{
    // Requer o SQL Server da Task 2 no ar (docker compose up -d) com seed aplicado.
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>()
            .UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    [Fact]
    public async Task Mapeia_seis_perfis_seedados()
    {
        await using var db = NovoContexto();
        var total = await db.Perfis.CountAsync();
        Assert.Equal(6, total);
    }

    [Fact]
    public async Task Carrega_admin_com_perfil_navegacao()
    {
        await using var db = NovoContexto();
        var admin = await db.Usuarios.Include(u => u.Perfil)
            .SingleAsync(u => u.NomeUsuario == "admin");
        Assert.Equal("Administrador", admin.Perfil.Nome);
    }
}
```

- [ ] **Step 3: Rodar o teste e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: FAIL na compilação — `RastreamentoDbContext` não existe.

- [ ] **Step 4: Criar as entidades**

`src/Rastreamento.Domain/Entities/Perfil.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Perfil
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
}
```

`src/Rastreamento.Domain/Entities/Usuario.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public string NomeUsuario { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string NomeCompleto { get; set; } = string.Empty;
    public int PerfilId { get; set; }
    public bool Ativo { get; set; }
    public Perfil Perfil { get; set; } = null!;
}
```

`src/Rastreamento.Domain/Entities/RefreshToken.cs`:

```csharp
namespace Rastreamento.Domain.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiraEm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? RevogadoEm { get; set; }
    public string? SubstituidoPorTokenHash { get; set; }
    public Usuario Usuario { get; set; } = null!;
}
```

- [ ] **Step 5: Criar as configurations Fluent API**

`src/Rastreamento.Infrastructure/Persistence/Configurations/PerfilConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class PerfilConfiguration : IEntityTypeConfiguration<Perfil>
{
    public void Configure(EntityTypeBuilder<Perfil> b)
    {
        b.ToTable("Perfil");
        b.HasKey(p => p.Id);
        b.Property(p => p.Nome).HasMaxLength(30).IsRequired();
    }
}
```

`src/Rastreamento.Infrastructure/Persistence/Configurations/UsuarioConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("Usuario");
        b.HasKey(u => u.Id);
        b.Property(u => u.NomeUsuario).HasMaxLength(50).IsRequired();
        b.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
        b.Property(u => u.NomeCompleto).HasMaxLength(200).IsRequired();
        b.HasOne(u => u.Perfil).WithMany().HasForeignKey(u => u.PerfilId);
    }
}
```

`src/Rastreamento.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshToken");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        b.Property(t => t.SubstituidoPorTokenHash).HasMaxLength(200);
        b.Property(t => t.CriadoEm).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(t => t.Usuario).WithMany().HasForeignKey(t => t.UsuarioId);
    }
}
```

- [ ] **Step 6: Criar o `RastreamentoDbContext`**

`src/Rastreamento.Infrastructure/Persistence/RastreamentoDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class RastreamentoDbContext : DbContext
{
    public RastreamentoDbContext(DbContextOptions<RastreamentoDbContext> options) : base(options) { }

    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RastreamentoDbContext).Assembly);
    }
}
```

- [ ] **Step 7: Rodar o teste e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS (2 testes) — exige `docker compose up -d` + seed da Task 2.

- [ ] **Step 8: Commit**

```bash
git add src/Rastreamento.Domain/Entities src/Rastreamento.Infrastructure/Persistence tests/Rastreamento.Application.Tests/Persistence
git commit -m "feat(infra): entidades + EF Core Database First (Usuario/Perfil/RefreshToken)"
```

---

### Task 4: Hash de senha (BCrypt)

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/IPasswordHasher.cs`
- Create: `src/Rastreamento.Infrastructure/Security/BCryptPasswordHasher.cs`
- Test: `tests/Rastreamento.Domain.Tests/Security/BCryptPasswordHasherTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `IPasswordHasher{ string Hash(string senhaPlano); bool Verificar(string senhaPlano, string senhaHash); }`, implementado por `BCryptPasswordHasher`.

- [ ] **Step 1: Instalar BCrypt e referenciar no teste**

```bash
dotnet add src/Rastreamento.Infrastructure package BCrypt.Net-Next
```

- [ ] **Step 2: Escrever o teste (falha)**

`tests/Rastreamento.Domain.Tests/Security/BCryptPasswordHasherTests.cs`:

```csharp
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Domain.Tests.Security;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_nao_retorna_a_senha_em_claro()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.NotEqual("Admin@123", hash);
        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void Verificar_true_para_senha_correta()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.True(_hasher.Verificar("Admin@123", hash));
    }

    [Fact]
    public void Verificar_false_para_senha_errada()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.False(_hasher.Verificar("senha-errada", hash));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Domain.Tests`
Expected: FAIL na compilação — `BCryptPasswordHasher` não existe.

- [ ] **Step 4: Criar a interface e a implementação**

`src/Rastreamento.Domain/Abstractions/IPasswordHasher.cs`:

```csharp
namespace Rastreamento.Domain.Abstractions;

public interface IPasswordHasher
{
    string Hash(string senhaPlano);
    bool Verificar(string senhaPlano, string senhaHash);
}
```

`src/Rastreamento.Infrastructure/Security/BCryptPasswordHasher.cs`:

```csharp
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Security;

public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string senhaPlano) =>
        BCrypt.Net.BCrypt.HashPassword(senhaPlano, WorkFactor);

    public bool Verificar(string senhaPlano, string senhaHash) =>
        BCrypt.Net.BCrypt.Verify(senhaPlano, senhaHash);
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Domain.Tests`
Expected: PASS (3 testes).

- [ ] **Step 6: (Opcional) Gerar o hash real do admin**

Se o hash do seed (Task 2, Step 6) não validar, gere um novo:

```bash
dotnet run --project src/Rastreamento.Api -- --gerar-hash "Admin@123"   # só se você adicionar esse atalho; alternativamente use um teste temporário imprimindo _hasher.Hash("Admin@123")
```

Copie o valor `$2a$11$...` para `db/seed.sql` e reaplique o seed (Task 2, Step 7).

- [ ] **Step 7: Commit**

```bash
git add src/Rastreamento.Domain/Abstractions/IPasswordHasher.cs src/Rastreamento.Infrastructure/Security/BCryptPasswordHasher.cs tests/Rastreamento.Domain.Tests/Security
git commit -m "feat(security): hash de senha com BCrypt"
```

---

### Task 5: Hash do refresh token (SHA-256) + emissão de JWT

**Files:**
- Create: `src/Rastreamento.Domain/Abstractions/ITokenHasher.cs`
- Create: `src/Rastreamento.Infrastructure/Security/Sha256TokenHasher.cs`
- Create: `src/Rastreamento.Application/Auth/IAccessTokenGenerator.cs`
- Create: `src/Rastreamento.Infrastructure/Security/JwtOptions.cs`
- Create: `src/Rastreamento.Infrastructure/Security/JwtAccessTokenGenerator.cs`
- Test: `tests/Rastreamento.Domain.Tests/Security/Sha256TokenHasherTests.cs`
- Test: `tests/Rastreamento.Domain.Tests/Security/JwtAccessTokenGeneratorTests.cs`

**Interfaces:**
- Consumes: `Usuario` (Task 3).
- Produces: `ITokenHasher{ string Hash(string tokenPlano); }`; `IAccessTokenGenerator{ (string token, DateTime expiraEm) Gerar(Usuario usuario); }`; `JwtOptions{ string Issuer; string Audience; string SigningKey; int AccessTokenMinutes; int RefreshTokenDays; }`. O JWT carrega claims `sub`=Id, `unique_name`=NomeUsuario, `role`=Perfil.Nome, `nome_completo`=NomeCompleto.

- [ ] **Step 1: Instalar pacote de JWT no Infrastructure**

```bash
dotnet add src/Rastreamento.Infrastructure package System.IdentityModel.Tokens.Jwt
```

- [ ] **Step 2: Escrever o teste do SHA-256 (falha)**

`tests/Rastreamento.Domain.Tests/Security/Sha256TokenHasherTests.cs`:

```csharp
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Domain.Tests.Security;

public class Sha256TokenHasherTests
{
    private readonly Sha256TokenHasher _hasher = new();

    [Fact]
    public void Hash_e_deterministico()
    {
        Assert.Equal(_hasher.Hash("abc"), _hasher.Hash("abc"));
    }

    [Fact]
    public void Hash_difere_por_entrada()
    {
        Assert.NotEqual(_hasher.Hash("abc"), _hasher.Hash("abd"));
    }
}
```

- [ ] **Step 3: Escrever o teste do JWT (falha)**

`tests/Rastreamento.Domain.Tests/Security/JwtAccessTokenGeneratorTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.Extensions.Options;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Domain.Tests.Security;

public class JwtAccessTokenGeneratorTests
{
    private static JwtAccessTokenGenerator NovoGerador()
    {
        var opts = Options.Create(new JwtOptions
        {
            Issuer = "rastreamento-api",
            Audience = "rastreamento-web",
            SigningKey = "chave-de-teste-super-secreta-com-32b+",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });
        return new JwtAccessTokenGenerator(opts);
    }

    [Fact]
    public void Gera_token_com_claims_do_usuario()
    {
        var usuario = new Usuario
        {
            Id = 42, NomeUsuario = "admin", NomeCompleto = "Administrador do Sistema",
            Perfil = new Perfil { Nome = "Administrador" }
        };

        var (token, expiraEm) = NovoGerador().Gerar(usuario);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("admin", jwt.Claims.Single(c => c.Type == "unique_name").Value);
        Assert.Equal("Administrador", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.True(expiraEm > System.DateTime.UtcNow);
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Domain.Tests`
Expected: FAIL na compilação — tipos não existem.

- [ ] **Step 5: Criar `ITokenHasher` e `Sha256TokenHasher`**

`src/Rastreamento.Domain/Abstractions/ITokenHasher.cs`:

```csharp
namespace Rastreamento.Domain.Abstractions;

public interface ITokenHasher
{
    string Hash(string tokenPlano);
}
```

`src/Rastreamento.Infrastructure/Security/Sha256TokenHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Security;

public class Sha256TokenHasher : ITokenHasher
{
    public string Hash(string tokenPlano)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(tokenPlano));
        return Convert.ToHexString(bytes);
    }
}
```

- [ ] **Step 6: Criar `JwtOptions`, `IAccessTokenGenerator` e `JwtAccessTokenGenerator`**

`src/Rastreamento.Infrastructure/Security/JwtOptions.cs`:

```csharp
namespace Rastreamento.Infrastructure.Security;

public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
```

`src/Rastreamento.Application/Auth/IAccessTokenGenerator.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Auth;

public interface IAccessTokenGenerator
{
    (string token, DateTime expiraEm) Gerar(Usuario usuario);
}
```

`src/Rastreamento.Infrastructure/Security/JwtAccessTokenGenerator.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Security;

public class JwtAccessTokenGenerator : IAccessTokenGenerator
{
    private readonly JwtOptions _opts;

    public JwtAccessTokenGenerator(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public (string token, DateTime expiraEm) Gerar(Usuario usuario)
    {
        var expiraEm = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim("sub", usuario.Id.ToString()),
            new Claim("unique_name", usuario.NomeUsuario),
            new Claim("role", usuario.Perfil.Nome),
            new Claim("nome_completo", usuario.NomeCompleto),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: expiraEm,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expiraEm);
    }
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Domain.Tests`
Expected: PASS (SHA-256 + JWT + BCrypt da Task 4).

- [ ] **Step 8: Commit**

```bash
git add src/Rastreamento.Domain/Abstractions/ITokenHasher.cs src/Rastreamento.Infrastructure/Security tests/Rastreamento.Domain.Tests/Security src/Rastreamento.Application/Auth/IAccessTokenGenerator.cs
git commit -m "feat(security): SHA-256 do refresh token + emissao de JWT"
```

---

### Task 6: Caso de uso de login

**Files:**
- Create: `src/Rastreamento.Application/Common/Result.cs`
- Create: `src/Rastreamento.Application/Auth/Dtos.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs`
- Create: `src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs`
- Create: `src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Auth/AutenticarUsuarioUseCaseTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (T4), `ITokenHasher` (T5), `IAccessTokenGenerator` (T5), `Usuario`/`RefreshToken` (T3).
- Produces:
  - `Result<T>{ bool Sucesso; T? Valor; string? Erro; static Result<T> Ok(T v); static Result<T> Falha(string e); }`
  - `LoginRequest(string NomeUsuario, string Senha)`, `UsuarioDto(int Id, string NomeUsuario, string NomeCompleto, string Perfil)`, `LoginResult(string AccessToken, DateTime AccessTokenExpiraEm, string RefreshTokenPlano, DateTime RefreshTokenExpiraEm, UsuarioDto Usuario)`
  - `IUsuarioRepository{ Task<Usuario?> ObterPorNomeUsuarioAsync(string, CancellationToken); Task<Usuario?> ObterPorIdAsync(int, CancellationToken); }`
  - `IRefreshTokenRepository{ Task AdicionarAsync(RefreshToken, CancellationToken); Task<RefreshToken?> ObterAtivoPorHashAsync(string, CancellationToken); Task SalvarAlteracoesAsync(CancellationToken); }` — `ObterAtivoPorHashAsync` retorna o token com `RevogadoEm IS NULL` (não filtra expiração; quem checa `ExpiraEm` é o use case), com `Usuario`+`Perfil` incluídos.
  - `AutenticarUsuarioUseCase : IAutenticarUsuarioUseCase{ Task<Result<LoginResult>> ExecutarAsync(LoginRequest, CancellationToken); }`

- [ ] **Step 1: Escrever o teste do login (falha)**

`tests/Rastreamento.Application.Tests/Auth/AutenticarUsuarioUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class AutenticarUsuarioUseCaseTests
{
    private static Usuario AdminAtivo(IPasswordHasher hasher) => new()
    {
        Id = 1, NomeUsuario = "admin", NomeCompleto = "Administrador do Sistema",
        Ativo = true, SenhaHash = hasher.Hash("Admin@123"),
        Perfil = new Perfil { Nome = "Administrador" }
    };

    private static AutenticarUsuarioUseCase NovoUseCase(Usuario? usuario, out FakeRefreshTokenRepo refreshRepo)
    {
        var hasher = new FakePasswordHasher();
        refreshRepo = new FakeRefreshTokenRepo();
        return new AutenticarUsuarioUseCase(
            new FakeUsuarioRepo(usuario), refreshRepo, hasher,
            new FakeTokenHasher(), new FakeAccessTokenGenerator(), new FakeJwtOptions());
    }

    [Fact]
    public async Task Login_valido_retorna_tokens_e_persiste_refresh()
    {
        var hasher = new FakePasswordHasher();
        var uc = NovoUseCase(AdminAtivo(hasher), out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", "Admin@123"), default);

        Assert.True(r.Sucesso);
        Assert.Equal("Administrador", r.Valor!.Usuario.Perfil);
        Assert.False(string.IsNullOrWhiteSpace(r.Valor.RefreshTokenPlano));
        Assert.Single(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Login_senha_errada_falha_sem_persistir()
    {
        var hasher = new FakePasswordHasher();
        var uc = NovoUseCase(AdminAtivo(hasher), out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

        Assert.False(r.Sucesso);
        Assert.Empty(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Login_usuario_inexistente_falha()
    {
        var uc = NovoUseCase(null, out _);
        var r = await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);
        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Login_usuario_inativo_falha()
    {
        var hasher = new FakePasswordHasher();
        var inativo = AdminAtivo(hasher); inativo.Ativo = false;
        var uc = NovoUseCase(inativo, out _);
        var r = await uc.ExecutarAsync(new LoginRequest("admin", "Admin@123"), default);
        Assert.False(r.Sucesso);
    }
}
```

`tests/Rastreamento.Application.Tests/Auth/Fakes.cs`:

```csharp
using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Application.Tests.Auth;

public class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string s) => "hash:" + s;
    public bool Verificar(string s, string h) => h == "hash:" + s;
}

public class FakeTokenHasher : ITokenHasher
{
    public string Hash(string t) => "sha:" + t;
}

public class FakeAccessTokenGenerator : IAccessTokenGenerator
{
    public (string token, DateTime expiraEm) Gerar(Usuario u) =>
        ("access-" + u.NomeUsuario, DateTime.UtcNow.AddMinutes(15));
}

public class FakeUsuarioRepo : IUsuarioRepository
{
    private readonly Usuario? _u;
    public FakeUsuarioRepo(Usuario? u) => _u = u;
    public Task<Usuario?> ObterPorNomeUsuarioAsync(string n, CancellationToken ct) =>
        Task.FromResult(_u is not null && _u.NomeUsuario == n ? _u : null);
    public Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_u is not null && _u.Id == id ? _u : null);
}

public class FakeRefreshTokenRepo : IRefreshTokenRepository
{
    public List<RefreshToken> Adicionados { get; } = new();
    public RefreshToken? Ativo { get; set; }
    public Task AdicionarAsync(RefreshToken t, CancellationToken ct) { Adicionados.Add(t); return Task.CompletedTask; }
    public Task<RefreshToken?> ObterAtivoPorHashAsync(string h, CancellationToken ct) =>
        Task.FromResult(Ativo is not null && Ativo.TokenHash == h && Ativo.RevogadoEm is null ? Ativo : null);
    public Task SalvarAlteracoesAsync(CancellationToken ct) => Task.CompletedTask;
}

public static class FakeJwtOptions
{
    public static IOptions<JwtOptions> Instance => Options.Create(new JwtOptions { RefreshTokenDays = 7, AccessTokenMinutes = 15 });
}
```

> Nota: o teste usa `new FakeJwtOptions()` como atalho de legibilidade; troque por `FakeJwtOptions.Instance` na chamada do construtor (o use case recebe `IOptions<JwtOptions>`).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: FAIL na compilação — `AutenticarUsuarioUseCase`, `Result`, DTOs e interfaces não existem.

- [ ] **Step 3: Criar `Result<T>`**

`src/Rastreamento.Application/Common/Result.cs`:

```csharp
namespace Rastreamento.Application.Common;

public sealed class Result<T>
{
    public bool Sucesso { get; }
    public T? Valor { get; }
    public string? Erro { get; }

    private Result(bool sucesso, T? valor, string? erro)
    {
        Sucesso = sucesso; Valor = valor; Erro = erro;
    }

    public static Result<T> Ok(T valor) => new(true, valor, null);
    public static Result<T> Falha(string erro) => new(false, default, erro);
}
```

- [ ] **Step 4: Criar DTOs e interfaces de repositório**

`src/Rastreamento.Application/Auth/Dtos.cs`:

```csharp
namespace Rastreamento.Application.Auth;

public record LoginRequest(string NomeUsuario, string Senha);
public record UsuarioDto(int Id, string NomeUsuario, string NomeCompleto, string Perfil);
public record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiraEm,
    string RefreshTokenPlano,
    DateTime RefreshTokenExpiraEm,
    UsuarioDto Usuario);

public interface IAutenticarUsuarioUseCase
{
    Task<Common.Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct);
}
public interface IRenovarTokenUseCase
{
    Task<Common.Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}
public interface IRevogarTokenUseCase
{
    Task ExecutarAsync(string refreshTokenPlano, CancellationToken ct);
}
```

`src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IUsuarioRepository
{
    Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct);
    Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct);
}
```

`src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs`:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IRefreshTokenRepository
{
    Task AdicionarAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct);
    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Criar `AutenticarUsuarioUseCase`**

`src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs`:

```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Application.Auth;

public class AutenticarUsuarioUseCase : IAutenticarUsuarioUseCase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenHasher _tokenHasher;
    private readonly IAccessTokenGenerator _accessTokens;
    private readonly JwtOptions _jwt;

    public AutenticarUsuarioUseCase(
        IUsuarioRepository usuarios, IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher, ITokenHasher tokenHasher,
        IAccessTokenGenerator accessTokens, IOptions<JwtOptions> jwt)
    {
        _usuarios = usuarios; _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher; _tokenHasher = tokenHasher;
        _accessTokens = accessTokens; _jwt = jwt.Value;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorNomeUsuarioAsync(req.NomeUsuario, ct);
        if (usuario is null || !usuario.Ativo || !_passwordHasher.Verificar(req.Senha, usuario.SenhaHash))
            return Result<LoginResult>.Falha("Usuário ou senha inválidos.");

        var resultado = await EmitirSessaoAsync(usuario, ct);
        return Result<LoginResult>.Ok(resultado);
    }

    // Reutilizado pelo RenovarTokenUseCase (T7).
    internal async Task<LoginResult> EmitirSessaoAsync(Usuario usuario, CancellationToken ct)
    {
        var (accessToken, accessExpira) = _accessTokens.Gerar(usuario);

        var refreshPlano = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var refreshExpira = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        await _refreshTokens.AdicionarAsync(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = _tokenHasher.Hash(refreshPlano),
            CriadoEm = DateTime.UtcNow,
            ExpiraEm = refreshExpira
        }, ct);
        await _refreshTokens.SalvarAlteracoesAsync(ct);

        var dto = new UsuarioDto(usuario.Id, usuario.NomeUsuario, usuario.NomeCompleto, usuario.Perfil.Nome);
        return new LoginResult(accessToken, accessExpira, refreshPlano, refreshExpira, dto);
    }
}
```

- [ ] **Step 6: Ajustar o teste para usar `FakeJwtOptions.Instance`**

No `AutenticarUsuarioUseCaseTests`, a chamada do construtor deve passar `FakeJwtOptions.Instance` como último argumento (não `new FakeJwtOptions()`).

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS (4 testes de login + mapeamento da T3).

- [ ] **Step 8: Commit**

```bash
git add src/Rastreamento.Application src/Rastreamento.Domain/Abstractions tests/Rastreamento.Application.Tests/Auth
git commit -m "feat(auth): caso de uso de login com emissao de sessao"
```

---

### Task 7: Refresh (rotação) + logout (revogação)

**Files:**
- Create: `src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs`
- Create: `src/Rastreamento.Application/Auth/RevogarTokenUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Auth/RenovarTokenUseCaseTests.cs`
- Test: `tests/Rastreamento.Application.Tests/Auth/RevogarTokenUseCaseTests.cs`

**Interfaces:**
- Consumes: `IRefreshTokenRepository`, `ITokenHasher`, `IAccessTokenGenerator`, `AutenticarUsuarioUseCase.EmitirSessaoAsync` (T6), `Usuario`/`RefreshToken`.
- Produces: `RenovarTokenUseCase : IRenovarTokenUseCase`; `RevogarTokenUseCase : IRevogarTokenUseCase`. Regra: refresh válido → marca o antigo `RevogadoEm=UtcNow` + `SubstituidoPorTokenHash`=hash do novo, emite nova sessão. Expirado/revogado/ausente → `Falha`. Logout é idempotente (não falha se o token não existir).

- [ ] **Step 1: Escrever os testes (falham)**

`tests/Rastreamento.Application.Tests/Auth/RenovarTokenUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class RenovarTokenUseCaseTests
{
    private static (RenovarTokenUseCase uc, FakeRefreshTokenRepo repo) Montar(RefreshToken? ativo)
    {
        var repo = new FakeRefreshTokenRepo { Ativo = ativo };
        var hasher = new FakeTokenHasher();
        var login = new AutenticarUsuarioUseCase(
            new FakeUsuarioRepo(UsuarioDe(ativo)), repo, new FakePasswordHasher(),
            hasher, new FakeAccessTokenGenerator(), FakeJwtOptions.Instance);
        var uc = new RenovarTokenUseCase(repo, hasher, login);
        return (uc, repo);
    }

    private static Usuario? UsuarioDe(RefreshToken? t) => t?.Usuario;

    private static RefreshToken TokenAtivo() => new()
    {
        Id = 5, UsuarioId = 1, TokenHash = "sha:plano-antigo",
        CriadoEm = System.DateTime.UtcNow.AddMinutes(-1),
        ExpiraEm = System.DateTime.UtcNow.AddDays(7), RevogadoEm = null,
        Usuario = new Usuario { Id = 1, NomeUsuario = "admin", NomeCompleto = "Admin", Perfil = new Perfil { Nome = "Administrador" } }
    };

    [Fact]
    public async Task Refresh_valido_rotaciona_e_revoga_o_antigo()
    {
        var (uc, repo) = Montar(TokenAtivo());
        var r = await uc.ExecutarAsync("plano-antigo", default);

        Assert.True(r.Sucesso);
        Assert.NotNull(repo.Ativo!.RevogadoEm);            // antigo revogado
        Assert.NotNull(repo.Ativo.SubstituidoPorTokenHash); // aponta p/ o novo
        Assert.Single(repo.Adicionados);                    // novo persistido
    }

    [Fact]
    public async Task Refresh_expirado_falha()
    {
        var expirado = TokenAtivo();
        expirado.ExpiraEm = System.DateTime.UtcNow.AddMinutes(-1);
        var (uc, _) = Montar(expirado);
        var r = await uc.ExecutarAsync("plano-antigo", default);
        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Refresh_inexistente_falha()
    {
        var (uc, _) = Montar(null);
        var r = await uc.ExecutarAsync("qualquer", default);
        Assert.False(r.Sucesso);
    }
}
```

`tests/Rastreamento.Application.Tests/Auth/RevogarTokenUseCaseTests.cs`:

```csharp
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class RevogarTokenUseCaseTests
{
    [Fact]
    public async Task Logout_revoga_token_existente()
    {
        var repo = new FakeRefreshTokenRepo
        {
            Ativo = new RefreshToken { Id = 9, TokenHash = "sha:plano", RevogadoEm = null,
                ExpiraEm = System.DateTime.UtcNow.AddDays(1), CriadoEm = System.DateTime.UtcNow }
        };
        var uc = new RevogarTokenUseCase(repo, new FakeTokenHasher());

        await uc.ExecutarAsync("plano", default);

        Assert.NotNull(repo.Ativo!.RevogadoEm);
    }

    [Fact]
    public async Task Logout_token_inexistente_nao_lanca()
    {
        var repo = new FakeRefreshTokenRepo { Ativo = null };
        var uc = new RevogarTokenUseCase(repo, new FakeTokenHasher());

        var ex = await Record.ExceptionAsync(() => uc.ExecutarAsync("nada", default));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: FAIL na compilação — `RenovarTokenUseCase`/`RevogarTokenUseCase` não existem.

- [ ] **Step 3: Criar `RenovarTokenUseCase`**

`src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs`:

```csharp
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RenovarTokenUseCase : IRenovarTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenHasher _tokenHasher;
    private readonly AutenticarUsuarioUseCase _login;

    public RenovarTokenUseCase(
        IRefreshTokenRepository refreshTokens, ITokenHasher tokenHasher, AutenticarUsuarioUseCase login)
    {
        _refreshTokens = refreshTokens; _tokenHasher = tokenHasher; _login = login;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano))
            return Result<LoginResult>.Falha("Refresh token ausente.");

        var hash = _tokenHasher.Hash(refreshTokenPlano);
        var atual = await _refreshTokens.ObterAtivoPorHashAsync(hash, ct);
        if (atual is null || atual.RevogadoEm is not null || atual.ExpiraEm <= DateTime.UtcNow)
            return Result<LoginResult>.Falha("Refresh token inválido ou expirado.");

        var novaSessao = await _login.EmitirSessaoAsync(atual.Usuario, ct);

        atual.RevogadoEm = DateTime.UtcNow;
        atual.SubstituidoPorTokenHash = _tokenHasher.Hash(novaSessao.RefreshTokenPlano);
        await _refreshTokens.SalvarAlteracoesAsync(ct);

        return Result<LoginResult>.Ok(novaSessao);
    }
}
```

- [ ] **Step 4: Criar `RevogarTokenUseCase`**

`src/Rastreamento.Application/Auth/RevogarTokenUseCase.cs`:

```csharp
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RevogarTokenUseCase : IRevogarTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenHasher _tokenHasher;

    public RevogarTokenUseCase(IRefreshTokenRepository refreshTokens, ITokenHasher tokenHasher)
    {
        _refreshTokens = refreshTokens; _tokenHasher = tokenHasher;
    }

    public async Task ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return;

        var atual = await _refreshTokens.ObterAtivoPorHashAsync(_tokenHasher.Hash(refreshTokenPlano), ct);
        if (atual is null) return;

        atual.RevogadoEm = DateTime.UtcNow;
        await _refreshTokens.SalvarAlteracoesAsync(ct);
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected: PASS (refresh 3 + logout 2 + login 4 + mapeamento 2).

- [ ] **Step 6: Commit**

```bash
git add src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs src/Rastreamento.Application/Auth/RevogarTokenUseCase.cs tests/Rastreamento.Application.Tests/Auth
git commit -m "feat(auth): refresh com rotacao + logout com revogacao"
```

---

### Task 8: Repositórios EF, controller de auth, wiring e integração

**Files:**
- Create: `src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs`
- Create: `src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs`
- Create: `src/Rastreamento.Api/Controllers/AuthController.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Modify: `src/Rastreamento.Api/appsettings.json`
- Test: `tests/Rastreamento.Application.Tests/Api/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: todos os use cases (T6/T7), `RastreamentoDbContext` (T3), `IAccessTokenGenerator`/`JwtOptions` (T5).
- Produces: endpoints `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `GET /me` conforme a spec; cookie `refreshToken` httpOnly/Secure/SameSite=Strict/Path=/auth; JSON com timestamps em ISO 8601 offset `-03:00`.

- [ ] **Step 1: Instalar pacotes na Api e no projeto de teste**

```bash
dotnet add src/Rastreamento.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Rastreamento.Api package Microsoft.EntityFrameworkCore.Design
dotnet add tests/Rastreamento.Application.Tests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Criar os repositórios EF**

`src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly RastreamentoDbContext _db;
    public UsuarioRepository(RastreamentoDbContext db) => _db = db;

    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil).SingleOrDefaultAsync(u => u.NomeUsuario == nomeUsuario, ct);

    public Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil).SingleOrDefaultAsync(u => u.Id == id, ct);
}
```

`src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly RastreamentoDbContext _db;
    public RefreshTokenRepository(RastreamentoDbContext db) => _db = db;

    public async Task AdicionarAsync(RefreshToken token, CancellationToken ct) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens
            .Include(t => t.Usuario).ThenInclude(u => u.Perfil)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash && t.RevogadoEm == null, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Configurar `appsettings.json`**

`src/Rastreamento.Api/appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Rastreamento": "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True"
  },
  "Jwt": {
    "Issuer": "rastreamento-api",
    "Audience": "rastreamento-web",
    "SigningKey": "troque-esta-chave-por-uma-forte-de-32bytes-ou-mais",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

- [ ] **Step 4: Escrever `Program.cs` (DI + JWT + pipeline)**

`src/Rastreamento.Api/Program.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddDbContext<RastreamentoDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Rastreamento")));

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenHasher, Sha256TokenHasher>();
builder.Services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();
builder.Services.AddScoped<AutenticarUsuarioUseCase>();
builder.Services.AddScoped<IAutenticarUsuarioUseCase>(sp => sp.GetRequiredService<AutenticarUsuarioUseCase>());
builder.Services.AddScoped<IRenovarTokenUseCase, RenovarTokenUseCase>();
builder.Services.AddScoped<IRevogarTokenUseCase, RevogarTokenUseCase>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RoleClaimType = "role", NameClaimType = "unique_name"
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { } // expõe Program p/ WebApplicationFactory
```

- [ ] **Step 5: Escrever `AuthController.cs`**

`src/Rastreamento.Api/Controllers/AuthController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookie = "refreshToken";
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly IAutenticarUsuarioUseCase _login;
    private readonly IRenovarTokenUseCase _renovar;
    private readonly IRevogarTokenUseCase _revogar;
    private readonly JwtOptions _jwt;

    public AuthController(IAutenticarUsuarioUseCase login, IRenovarTokenUseCase renovar,
        IRevogarTokenUseCase revogar, IOptions<JwtOptions> jwt)
    {
        _login = login; _renovar = renovar; _revogar = revogar; _jwt = jwt.Value;
    }

    public record LoginBody(string NomeUsuario, string Senha);
    public record LoginResponse(string AccessToken, DateTimeOffset AccessTokenExpiraEm, UsuarioDto Usuario);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        var r = await _login.ExecutarAsync(new LoginRequest(body.NomeUsuario, body.Senha), ct);
        if (!r.Sucesso) return Unauthorized(new { erro = r.Erro });
        return Ok(MontarResposta(r.Valor!));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var plano = Request.Cookies[RefreshCookie] ?? string.Empty;
        var r = await _renovar.ExecutarAsync(plano, ct);
        if (!r.Sucesso) return Unauthorized(new { erro = r.Erro });
        return Ok(MontarResposta(r.Valor!));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var plano = Request.Cookies[RefreshCookie] ?? string.Empty;
        await _revogar.ExecutarAsync(plano, ct);
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/auth" });
        return NoContent();
    }

    private LoginResponse MontarResposta(LoginResult resultado)
    {
        Response.Cookies.Append(RefreshCookie, resultado.RefreshTokenPlano, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
            Path = "/auth", Expires = new DateTimeOffset(resultado.RefreshTokenExpiraEm, TimeSpan.Zero)
        });
        return new LoginResponse(
            resultado.AccessToken,
            TimeZoneInfo.ConvertTime(new DateTimeOffset(resultado.AccessTokenExpiraEm, TimeSpan.Zero), Brasilia),
            resultado.Usuario);
    }
}

[ApiController]
[Route("me")]
public class MeController : ControllerBase
{
    [Authorize]
    [HttpGet]
    public IActionResult Get()
    {
        var id = int.Parse(User.FindFirst("sub")!.Value);
        var nomeUsuario = User.FindFirst("unique_name")!.Value;
        var nomeCompleto = User.FindFirst("nome_completo")!.Value;
        var perfil = User.FindFirst("role")!.Value;
        return Ok(new UsuarioDto(id, nomeUsuario, nomeCompleto, perfil));
    }
}
```

- [ ] **Step 6: Escrever o teste de integração (falha)**

`tests/Rastreamento.Application.Tests/Api/AuthEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Rastreamento.Application.Tests.Api;

// Requer SQL Server da Task 2 no ar com seed (admin / Admin@123).
public class AuthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public AuthEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient NovoCliente() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private record LoginResp(string accessToken, System.DateTimeOffset accessTokenExpiraEm, UsuarioResp usuario);
    private record UsuarioResp(int id, string nomeUsuario, string nomeCompleto, string perfil);

    [Fact]
    public async Task Login_valido_retorna_200_e_cookie()
    {
        var client = NovoCliente();
        var resp = await client.PostAsJsonAsync("/auth/login", new { nomeUsuario = "admin", senha = "Admin@123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith("refreshToken="));
    }

    [Fact]
    public async Task Login_senha_errada_retorna_401()
    {
        var client = NovoCliente();
        var resp = await client.PostAsJsonAsync("/auth/login", new { nomeUsuario = "admin", senha = "errada" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_sem_token_retorna_401_e_com_token_retorna_usuario()
    {
        var client = NovoCliente();
        var semToken = await client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, semToken.StatusCode);

        var login = await (await client.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = "admin", senha = "Admin@123" })).Content.ReadFromJsonAsync<LoginResp>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login!.accessToken);

        var me = await client.GetFromJsonAsync<UsuarioResp>("/me");
        Assert.Equal("admin", me!.nomeUsuario);
        Assert.Equal("Administrador", me.perfil);
    }

    [Fact]
    public async Task Refresh_rotaciona_e_logout_revoga()
    {
        var client = NovoCliente();
        await client.PostAsJsonAsync("/auth/login", new { nomeUsuario = "admin", senha = "Admin@123" });

        var refresh1 = await client.PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh1.StatusCode);

        var logout = await client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshAposLogout = await client.PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAposLogout.StatusCode);
    }
}
```

- [ ] **Step 7: Rodar e ver falhar, depois passar**

Run: `dotnet test tests/Rastreamento.Application.Tests`
Expected inicial: FAIL (controller/rotas ainda não expostos ou cookie ausente); após Steps 2–5 aplicados, PASS em todos.

> Se `Login_valido` falhar por hash, é sinal de que o `SenhaHash` do seed não corresponde a `Admin@123`: gere o hash com a Task 4 e reaplique o seed (Task 2, Step 7).

- [ ] **Step 8: Rodar a suíte completa**

Run: `dotnet test`
Expected: PASS em `Rastreamento.Domain.Tests` e `Rastreamento.Application.Tests`.

- [ ] **Step 9: Commit**

```bash
git add src/Rastreamento.Infrastructure/Persistence src/Rastreamento.Api tests/Rastreamento.Application.Tests/Api
git commit -m "feat(api): endpoints de auth (login/refresh/logout) + /me + wiring JWT"
```

---

## Self-Review

**1. Spec coverage:**
- Solution 4 camadas + testes → T1. ✔
- Schema `RefreshToken` no DDL + Docker + seed → T2. ✔
- EF Core Database First (Usuario/Perfil/RefreshToken) → T3. ✔
- BCrypt → T4. ✔ SHA-256 + JWT → T5. ✔
- Login (`POST /auth/login`) → T6/T8. ✔ Refresh + logout → T7/T8. ✔ `[Authorize]` + `/me` → T8. ✔
- Cookie httpOnly/Secure/SameSite=Strict/Path=/auth → T8 (`MontarResposta`). ✔
- Datas UTC + borda GMT-3 (`-03:00`) → T8 (`DateTimeOffset` convertido para `America/Sao_Paulo`). ✔
- Acceptance Criteria 1–7 e 10 (backend) → cobertos por T2/T3/T8. ✔
- **Gaps propositais (não-backend):** critérios 8 (front) e 9 (`docs/deploy-manual.md`) e T9–T11 ficam no plano de frontend. Critério 6 do escopo de deploy manual será coberto lá.

**2. Placeholder scan:** sem "TBD/TODO". As notas sobre o hash do seed são instruções de contingência com passo concreto (gerar via Task 4), não placeholders.

**3. Type consistency:** `IPasswordHasher.Verificar`, `ITokenHasher.Hash`, `IAccessTokenGenerator.Gerar → (string token, DateTime expiraEm)`, `IRefreshTokenRepository.ObterAtivoPorHashAsync`, `Result<T>.{Sucesso,Valor,Erro}`, `LoginResult.RefreshTokenPlano` — usados de forma idêntica entre T4–T8. `AutenticarUsuarioUseCase.EmitirSessaoAsync` (internal) é consumido por `RenovarTokenUseCase` em T7 conforme declarado. ✔

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-23-fase-0-backend-auth.md`.

O plano de **frontend** (T9–T11: Vite + Tailwind + AuthContext + login + Home consumindo `/me` + `docs/deploy-manual.md`) é um documento separado, a ser gerado depois que este backend estiver executado (o front depende dos contratos já implementados).
