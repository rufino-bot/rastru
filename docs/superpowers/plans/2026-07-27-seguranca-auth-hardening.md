# Hardening de Autenticação (reuso de refresh + lockout + rate limit + logging) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar quatro itens da dívida consciente de segurança do backend de auth — detecção de reuso de refresh token, lockout de conta, rate limit no login e logging de eventos de auth — sem quebrar nenhuma das invariantes de segurança já existentes.

**Architecture:** Cada defesa entra na camada onde o sinal nasce. O reuso de refresh token é detectado na camada Application (`RenovarTokenUseCase`) a partir de duas capacidades novas do repositório (ler token em qualquer estado; revogar em massa). O lockout de conta é estado persistido em `Usuario` (duas colunas), lido e escrito pelo `AutenticarUsuarioUseCase`. O rate limit é middleware nativo do ASP.NET Core, escopo só do `/auth/login`, e vive inteiro na camada Api. O logging usa `ILogger` — eventos de segurança nos casos de uso (sem `HttpContext`), desfecho grosso + IP no `AuthController`.

**Tech Stack:** .NET 10 / C#, ASP.NET Core Web API, EF Core 10 (Database First), SQL Server, xUnit. Nenhuma dependência externa nova — só o pacote `Microsoft.Extensions.Logging.Abstractions` no projeto Application.

**Spec de origem:** `docs/superpowers/specs/2026-07-27-seguranca-auth-hardening-design.md` (Seções A, B, C, D + cross-cutting).

**Branch:** `seguranca-auth-hardening`.

---

## Pré-requisitos (fora do escopo dos commits)

- **Rebasear a branch sobre a `origin/main` ANTES da Task 1.** A `seguranca-auth-hardening` saiu de
  uma `main` local desatualizada e está **10 commits atrás** da `origin/main` — falta nela o PR #2
  inteiro (alterações de domínio: lote divisível, Agrupamento, Expedicao, Perda). O PR #2 mexe em
  quatro dos arquivos que este plano também edita (`specs/02-modelo-de-dados.sql`, `CLAUDE.md`,
  `specs/03-arquitetura-tecnica.md`, `specs/05-api-endpoints.md`). Não há conflito semântico — o PR
  #2 não toca na tabela `Usuario` nem em nada de auth —, mas editar os mesmos arquivos a partir de
  uma base velha faria o merge desta branch reverter texto do PR #2.

```bash
git fetch origin
git checkout seguranca-auth-hardening
git rebase origin/main
```

  Depois do rebase, confirmar que não sobrou nada: `git log --oneline seguranca-auth-hardening..origin/main`
  deve sair vazio.

- **SQL Server no ar com schema e seed aplicados.** Boa parte da suíte roda contra o banco real:

```bash
docker compose up -d
```

  O banco `Rastreamento` em `localhost:1433` (`sa` / `Your_strong_Pass123`) já deve ter
  `specs/02-modelo-de-dados.sql` e `db/seed.sql` aplicados (ver `CLAUDE.md`). A **Task 3** adiciona
  duas colunas e traz o `ALTER TABLE` para aplicar no banco já existente.

- **Build sempre em 0 warnings:** `dotnet build Rastreamento.slnx -warnaserror`.

---

## Global Constraints

Valem para **todas** as tarefas. Copiados da spec e do `CLAUDE.md`:

- **401 genérico único.** Toda falha de login responde exatamente `"Usuário ou senha inválidos."`;
  toda falha de refresh responde exatamente `"Refresh token inválido ou expirado."`, sempre com
  `TipoDeErro.NaoAutorizado`. Nenhum caminho novo (reuso detectado, conta trancada, senha certa em
  conta trancada) pode ter corpo, status ou tipo de erro diferente dos demais.
- **Trabalho constante no login.** O BCrypt roda **sempre**, inclusive para usuário inexistente,
  inativo ou trancado (usando `IPasswordHasher.HashFicticio`). Nenhuma alteração pode introduzir um
  curto-circuito antes da verificação de senha.
- **Nunca logar:** senha, refresh token em texto plano, hash de refresh token, access token.
- **Datas sempre em UTC** dentro da aplicação (`DateTime.UtcNow`). A conversão para GMT-3 é só na
  serialização (`HorarioDeBrasiliaJsonConverter`).
- **Database First.** `specs/02-modelo-de-dados.sql` é a fonte da verdade do schema. Nada de
  `Add-Migration` / `EnsureCreated`. Mudança de schema: primeiro o `.sql`, depois o mapeamento EF.
- **Lifetimes Scoped.** Todo serviço de auth continua `Scoped` — é o que sustenta a atomicidade da
  rotação (ver `RegistroDeDependenciasTests`). Nada novo pode ser `Transient` ou `Singleton` nesse
  caminho, exceto os validadores de `IValidateOptions<T>` (que já são `Singleton` por padrão).
- **Nomenclatura:** domínio em português espelhando o DDL (`Usuario`, `RefreshToken`,
  `FalhasConsecutivas`, `BloqueadoAte`); padrões técnicos em inglês (`Repository`, `UseCase`,
  `Options`, `Validator`).
- **Defaults de configuração (exatos):** `Lockout:MaxFalhas` = **5**, `Lockout:DuracaoMinutos` =
  **15**, `RateLimit:PermitLimit` = **10**, `RateLimit:WindowSeconds` = **60**. Ambas as seções são
  validadas no startup com `IValidateOptions<T>` + `ValidateOnStart`, mesmo padrão do
  `JwtOptionsValidator`.
- **Pacote novo:** `Microsoft.Extensions.Logging.Abstractions` versão **10.0.10** (mesma linha dos
  demais pacotes Microsoft do repositório).

### Fora de escopo (deferido de propósito — não implementar)

Logout invalidando o access token (denylist); tabela de auditoria persistente; limpeza de linhas
`RefreshToken` expiradas; segredos de deploy (`SigningKey` de ambiente, `UseHttpsRedirection`);
mensagem dedicada de 429 no front; `ForwardedHeaders` (só vira necessário quando entrar proxy —
anotado na doc da Task 8).

---

## File Structure

```
specs/
  02-modelo-de-dados.sql                     MOD  Usuario: +FalhasConsecutivas, +BloqueadoAte
  03-arquitetura-tecnica.md                  MOD  seção de auth: as três defesas novas
  05-api-endpoints.md                        MOD  429 no /auth/login

src/Rastreamento.Domain/
  Abstractions/IRefreshTokenRepository.cs    MOD  +ObterPorHashAsync, +RevogarTodosAtivosDoUsuarioAsync
  Abstractions/IUsuarioRepository.cs         MOD  +SalvarAlteracoesAsync
  Entities/Usuario.cs                        MOD  +FalhasConsecutivas, +BloqueadoAte

src/Rastreamento.Application/
  Rastreamento.Application.csproj            MOD  +Microsoft.Extensions.Logging.Abstractions
  Auth/RenovarTokenUseCase.cs                MOD  detecção de reuso + log
  Auth/AutenticarUsuarioUseCase.cs           MOD  lockout + log
  Auth/LockoutOptions.cs                     NEW  MaxFalhas / DuracaoMinutos
  Auth/LockoutOptionsValidator.cs            NEW  validação no startup

src/Rastreamento.Infrastructure/
  Persistence/RefreshTokenRepository.cs      MOD  implementa os dois métodos novos
  Persistence/UsuarioRepository.cs           MOD  implementa SalvarAlteracoesAsync

src/Rastreamento.Api/
  Configuration/RateLimitOptions.cs          NEW  política de login (Api-only, não vaza p/ Application)
  Configuration/RateLimitOptionsValidator.cs NEW  validação no startup
  Controllers/AuthController.cs              MOD  [EnableRateLimiting] no login + logging de desfecho
  Program.cs                                 MOD  rate limiter, options de Lockout e RateLimit
  appsettings.json                           MOD  seções Lockout e RateLimit (defaults de produção)
  appsettings.Development.json               MOD  RateLimit folgado em dev/teste

tests/Rastreamento.Application.Tests/
  Auth/Fakes.cs                              MOD  fakes acompanham os contratos novos + FakeLogger
  Auth/RenovarTokenUseCaseTests.cs           MOD  cenários de reuso
  Auth/AutenticarUsuarioUseCaseTests.cs      MOD  cenários de lockout

tests/Rastreamento.Infrastructure.Tests/
  Persistence/RefreshTokenRepositoryTests.cs NEW  os dois métodos novos contra o banco real
  Persistence/DbContextMappingTests.cs       MOD  round-trip das colunas de lockout

tests/Rastreamento.Api.Tests/
  UsuarioDeTeste.cs                          NEW  usuário descartável (testes destrutivos)
  ReusoDeRefreshTokenTests.cs                NEW  reuso ponta a ponta
  LockoutTests.cs                            NEW  lockout ponta a ponta
  RateLimitTests.cs                          NEW  429 no /auth/login
  ConfiguracaoDeStartupTests.cs              MOD  config inválida de Lockout/RateLimit derruba o startup

CLAUDE.md                                    MOD  defesas de auth + contagem de testes que exigem banco
```

**Por que `RateLimitOptions` fica na Api e `LockoutOptions` na Application:** `LockoutOptions` é
consumida pelo `AutenticarUsuarioUseCase` (regra de negócio de autenticação, camada Application,
mesmo lugar de `JwtOptions`). `RateLimitOptions` é consumida só pelo `Program.cs` — é política de
middleware HTTP, não regra de negócio. Colocá-la na Application arrastaria uma preocupação de
transporte para dentro do domínio.

---

## Task 1: Repositório de refresh token — leitura em qualquer estado e revogação em massa

Entrega as duas capacidades de persistência que a detecção de reuso (Task 2) precisa. Sozinha não
muda nenhum comportamento observável da API.

**Files:**
- Modify: `src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs`
- Modify: `tests/Rastreamento.Application.Tests/Auth/Fakes.cs` (o fake tem que acompanhar o
  contrato, senão `Application.Tests` não compila)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/RefreshTokenRepositoryTests.cs` (novo)

**Interfaces:**
- Consumes: `RefreshToken` (`Id`, `UsuarioId`, `TokenHash`, `ExpiraEm`, `CriadoEm`, `RevogadoEm`,
  `SubstituidoPorTokenHash`, `Usuario`), `Usuario`, `Perfil`, `RastreamentoDbContext`
  (`RefreshTokens`, `Usuarios`, `Perfis`).
- Produces:
  - `Task<RefreshToken?> IRefreshTokenRepository.ObterPorHashAsync(string tokenHash, CancellationToken ct)`
    — token em qualquer estado, com `Usuario` e `Perfil` carregados, **rastreado**.
  - `Task<int> IRefreshTokenRepository.RevogarTodosAtivosDoUsuarioAsync(int usuarioId, DateTime revogadoEm, CancellationToken ct)`
    — revoga e persiste sozinho; devolve quantas linhas foram revogadas.
  - `FakeRefreshTokenRepo.RevogacoesEmMassa` (`List<int>`) — ids de usuário cujos tokens foram
    queimados, na ordem das chamadas.

- [ ] **Step 1: Escrever o teste que falha (repositório contra o banco real)**

Criar `tests/Rastreamento.Infrastructure.Tests/Persistence/RefreshTokenRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Requer o SQL Server no ar (docker compose up -d) com schema e seed aplicados.
/// Usa um usuario proprio, criado e removido pelo teste: `RevogarTodosAtivosDoUsuarioAsync` queima
/// TODOS os tokens ativos de um usuario, entao roda-lo contra o `admin` do seed derrubaria os
/// tokens de outros testes rodando em paralelo — e a contagem devolvida ficaria nao-deterministica.
/// </summary>
public class RefreshTokenRepositoryTests : IAsyncLifetime
{
    private const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    private int _usuarioId;

    private static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var db = NovoContexto();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        var usuario = new Usuario
        {
            // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao
            // anterior que tenha morrido antes da limpeza.
            NomeUsuario = $"repo-{Guid.NewGuid():N}",
            SenhaHash = "nao-usado-neste-teste",
            NomeCompleto = "Usuario de Teste do Repositorio",
            PerfilId = perfil.Id,
            Ativo = true,
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        _usuarioId = usuario.Id;
    }

    public async Task DisposeAsync()
    {
        await using var db = NovoContexto();
        // RefreshToken tem FK para Usuario: as linhas filhas saem primeiro.
        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => t.UsuarioId == _usuarioId).ToListAsync());
        db.Usuarios.RemoveRange(await db.Usuarios.Where(u => u.Id == _usuarioId).ToListAsync());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ObterPorHashAsync_enxerga_token_revogado_que_ObterAtivoPorHashAsync_nao_ve()
    {
        var hash = await CriarTokenAsync(revogadoEm: DateTime.UtcNow.AddMinutes(-5));

        await using var db = NovoContexto();
        var repo = new RefreshTokenRepository(db);

        // A diferenca entre os dois metodos E o ponto: sem ObterPorHashAsync nao ha como
        // perceber que um token ja rotacionado foi reapresentado.
        Assert.Null(await repo.ObterAtivoPorHashAsync(hash, default));

        var encontrado = await repo.ObterPorHashAsync(hash, default);
        Assert.NotNull(encontrado);
        Assert.NotNull(encontrado!.RevogadoEm);
        Assert.Equal(_usuarioId, encontrado.UsuarioId);
        // Usuario + Perfil vem junto: o caso de uso precisa de Usuario.Ativo e o caminho feliz
        // da rotacao precisa do nome do Perfil para a claim `role`.
        Assert.NotNull(encontrado.Usuario);
        Assert.Equal("Administrador", encontrado.Usuario.Perfil.Nome);
    }

    [Fact]
    public async Task RevogarTodosAtivosDoUsuarioAsync_revoga_so_os_ativos_e_devolve_a_contagem()
    {
        var ativoA = await CriarTokenAsync(revogadoEm: null);
        var ativoB = await CriarTokenAsync(revogadoEm: null);
        var revogadoAntes = DateTime.UtcNow.AddHours(-1);
        var jaRevogado = await CriarTokenAsync(revogadoEm: revogadoAntes);

        var agora = DateTime.UtcNow;
        int revogados;
        await using (var db = NovoContexto())
            revogados = await new RefreshTokenRepository(db)
                .RevogarTodosAtivosDoUsuarioAsync(_usuarioId, agora, default);

        Assert.Equal(2, revogados);

        await using var leitura = NovoContexto();
        foreach (var hash in new[] { ativoA, ativoB })
        {
            var linha = await leitura.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash);
            Assert.Equal(agora, linha.RevogadoEm!.Value, TimeSpan.FromSeconds(1));
        }

        // O que ja estava revogado mantem a data original: a queima nao reescreve historico.
        var antigo = await leitura.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == jaRevogado);
        Assert.Equal(revogadoAntes, antigo.RevogadoEm!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RevogarTodosAtivosDoUsuarioAsync_nao_toca_em_token_de_outro_usuario()
    {
        var meu = await CriarTokenAsync(revogadoEm: null);

        // O `admin` do seed serve de vizinho: o filtro por UsuarioId tem que isolar a queima.
        string hashDoVizinho;
        int idDoVizinho;
        await using (var db = NovoContexto())
        {
            var admin = await db.Usuarios.SingleAsync(u => u.NomeUsuario == "admin");
            idDoVizinho = admin.Id;
            hashDoVizinho = $"vizinho-{Guid.NewGuid():N}";
            db.RefreshTokens.Add(new RefreshToken
            {
                UsuarioId = idDoVizinho,
                TokenHash = hashDoVizinho,
                CriadoEm = DateTime.UtcNow,
                ExpiraEm = DateTime.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await using (var db = NovoContexto())
                await new RefreshTokenRepository(db)
                    .RevogarTodosAtivosDoUsuarioAsync(_usuarioId, DateTime.UtcNow, default);

            await using var leitura = NovoContexto();
            Assert.NotNull((await leitura.RefreshTokens.AsNoTracking()
                .SingleAsync(t => t.TokenHash == meu)).RevogadoEm);
            Assert.Null((await leitura.RefreshTokens.AsNoTracking()
                .SingleAsync(t => t.TokenHash == hashDoVizinho)).RevogadoEm);
        }
        finally
        {
            await using var limpeza = NovoContexto();
            limpeza.RefreshTokens.RemoveRange(
                await limpeza.RefreshTokens.Where(t => t.TokenHash == hashDoVizinho).ToListAsync());
            await limpeza.SaveChangesAsync();
        }
    }

    /// <summary>Insere um refresh token do usuario do teste e devolve o hash usado.</summary>
    private async Task<string> CriarTokenAsync(DateTime? revogadoEm)
    {
        await using var db = NovoContexto();
        var hash = $"teste-{Guid.NewGuid():N}";
        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = _usuarioId,
            TokenHash = hash,
            // CriadoEm antes de ExpiraEm: CK_RefreshToken_ExpiraAposCriado exige.
            CriadoEm = DateTime.UtcNow.AddMinutes(-10),
            ExpiraEm = DateTime.UtcNow.AddDays(7),
            RevogadoEm = revogadoEm,
        });
        await db.SaveChangesAsync();
        return hash;
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```bash
docker compose up -d
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Expected: FALHA DE COMPILAÇÃO — `'IRefreshTokenRepository' does not contain a definition for
'ObterPorHashAsync'` e `'RevogarTodosAtivosDoUsuarioAsync'`.

- [ ] **Step 3: Ampliar o contrato do repositório**

Substituir o conteúdo de `src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs` por:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IRefreshTokenRepository
{
    Task AdicionarAsync(RefreshToken token, CancellationToken ct);

    /// <summary>
    /// Retorna o token nao revogado (<c>RevogadoEm IS NULL</c>) correspondente ao hash informado,
    /// com <c>Usuario</c> e <c>Perfil</c> carregados. Nao filtra por expiracao: quem verifica
    /// <c>ExpiraEm</c> e o caso de uso.
    /// </summary>
    Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Retorna o token correspondente ao hash em QUALQUER estado — inclusive ja revogado —, com
    /// <c>Usuario</c> e <c>Perfil</c> carregados e rastreado. E o que torna a reapresentacao de um
    /// token ja rotacionado visivel: <see cref="ObterAtivoPorHashAsync"/> nunca enxerga esse caso,
    /// entao com ele o sinal de reuso e indistinguivel de "token nunca existiu".
    /// </summary>
    Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// Marca <c>RevogadoEm = revogadoEm</c> em todos os refresh tokens ainda ativos do usuario e
    /// persiste, num unico comando. Devolve quantas linhas foram revogadas. Usado na deteccao de
    /// reuso: um refresh vazado derruba a familia inteira de sessoes daquele usuario.
    /// </summary>
    Task<int> RevogarTodosAtivosDoUsuarioAsync(int usuarioId, DateTime revogadoEm, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implementar no repositório EF**

Em `src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs`, inserir os dois métodos
logo antes de `SalvarAlteracoesAsync`:

```csharp
    /// <summary>
    /// Sem filtro de estado, de proposito (ver o contrato da interface). Rastreado como o
    /// <see cref="ObterAtivoPorHashAsync"/>: o caminho feliz da rotacao muta o registro e conta com
    /// o change tracking para revoga-lo no mesmo <c>SaveChanges</c> que insere o novo.
    /// </summary>
    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens
            .Include(t => t.Usuario).ThenInclude(u => u.Perfil)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <summary>
    /// <c>ExecuteUpdateAsync</c> emite um unico UPDATE no servidor e ja commita — nao carrega as
    /// linhas para a memoria nem depende de um <c>SaveChanges</c> posterior. Em troca, ele nao
    /// passa pelo change tracker: entidades ja rastreadas nesta requisicao continuam com o
    /// <c>RevogadoEm</c> antigo em memoria. O unico chamador (o caminho de reuso do
    /// <c>RenovarTokenUseCase</c>) descarta a entidade que tinha lido e retorna 401 logo em
    /// seguida, entao nao ha estado desatualizado sendo reaproveitado.
    /// </summary>
    public Task<int> RevogarTodosAtivosDoUsuarioAsync(
        int usuarioId, DateTime revogadoEm, CancellationToken ct) =>
        _db.RefreshTokens
            .Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, revogadoEm), ct);
```

- [ ] **Step 5: Fazer o fake acompanhar o contrato**

Em `tests/Rastreamento.Application.Tests/Auth/Fakes.cs`, substituir a classe
`FakeRefreshTokenRepo` inteira por:

```csharp
public class FakeRefreshTokenRepo : IRefreshTokenRepository
{
    public List<RefreshToken> Adicionados { get; } = new();
    public RefreshToken? Ativo { get; set; }

    /// <summary>Quantos commits o repositorio recebeu — permite provar "um unico save".</summary>
    public int Saves { get; private set; }

    /// <summary>Ids de usuario cuja familia de tokens foi queimada, na ordem das chamadas.</summary>
    public List<int> RevogacoesEmMassa { get; } = new();

    /// <summary>Tudo que o fake "conhece": o token de partida mais os emitidos durante o teste.</summary>
    private IEnumerable<RefreshToken> Todos =>
        Ativo is null ? Adicionados : Adicionados.Append(Ativo);

    public Task AdicionarAsync(RefreshToken token, CancellationToken ct)
    {
        Adicionados.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Ativo is not null && Ativo.TokenHash == tokenHash && Ativo.RevogadoEm is null ? Ativo : null);

    /// <summary>Sem filtro de estado: e o que permite ao caso de uso ver um token ja revogado.</summary>
    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Ativo is not null && Ativo.TokenHash == tokenHash ? Ativo : null);

    /// <summary>
    /// Revoga de verdade os tokens conhecidos do usuario (nao so registra a chamada): e assim que
    /// o teste consegue provar que a sessao emitida ao ladrao tambem cai.
    /// </summary>
    public Task<int> RevogarTodosAtivosDoUsuarioAsync(
        int usuarioId, DateTime revogadoEm, CancellationToken ct)
    {
        RevogacoesEmMassa.Add(usuarioId);
        var revogados = 0;
        foreach (var token in Todos.Where(t => t.UsuarioId == usuarioId && t.RevogadoEm is null))
        {
            token.RevogadoEm = revogadoEm;
            revogados++;
        }

        // O metodo real persiste sozinho — o fake conta o save para nao mascarar isso.
        Saves++;
        return Task.FromResult(revogados);
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Rodar os testes e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Infrastructure.Tests
dotnet test tests/Rastreamento.Application.Tests
```

Expected: build em 0 warnings; `Infrastructure.Tests` PASS (os 3 testes novos incluídos);
`Application.Tests` PASS sem mudança de comportamento (só o fake foi ampliado).

- [ ] **Step 7: Commit**

```bash
git add src/Rastreamento.Domain/Abstractions/IRefreshTokenRepository.cs \
        src/Rastreamento.Infrastructure/Persistence/RefreshTokenRepository.cs \
        tests/Rastreamento.Application.Tests/Auth/Fakes.cs \
        tests/Rastreamento.Infrastructure.Tests/Persistence/RefreshTokenRepositoryTests.cs
git commit -m "feat(auth): repositorio le refresh token em qualquer estado e revoga em massa"
```

---

## Task 2: Detecção de reuso de refresh token

Fecha a Seção A da spec. Um refresh token já rotacionado, reapresentado, queima toda a família de
sessões do usuário — e responde o mesmo 401 genérico de sempre.

**Files:**
- Modify: `src/Rastreamento.Application/Rastreamento.Application.csproj` (pacote de logging)
- Modify: `src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs`
- Modify: `tests/Rastreamento.Application.Tests/Auth/Fakes.cs` (adiciona `FakeLogger<T>`)
- Test: `tests/Rastreamento.Application.Tests/Auth/RenovarTokenUseCaseTests.cs`
- Create: `tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs`
- Test: `tests/Rastreamento.Api.Tests/ReusoDeRefreshTokenTests.cs` (novo)

**Interfaces:**
- Consumes: `IRefreshTokenRepository.ObterPorHashAsync` e `.RevogarTodosAtivosDoUsuarioAsync`
  (Task 1); `ITokenHasher.Hash`; `IEmissorDeSessao.RotacionarAsync`; `Result<LoginResult>`.
- Produces:
  - `RenovarTokenUseCase(IRefreshTokenRepository, ITokenHasher, IEmissorDeSessao, ILogger<RenovarTokenUseCase>)`
    — assinatura nova do construtor (parâmetro de logger no fim).
  - `FakeLogger<T>` com `Entradas` (`List<FakeLogger<T>.Entrada>`), onde
    `Entrada` é `record Entrada(LogLevel Nivel, string Mensagem)`.
  - `UsuarioDeTeste.CriarAsync(IServiceProvider servicos, string prefixo)` →
    `Task<UsuarioDeTeste>`, com `NomeUsuario` (string), `Id` (int),
    `UsuarioDeTeste.Senha` (const `"Senha@123"`) e `DisposeAsync()`.

- [ ] **Step 1: Escrever os testes de unidade que falham**

Em `tests/Rastreamento.Application.Tests/Auth/RenovarTokenUseCaseTests.cs`:

(a) trocar o helper `Montar` para injetar o logger — substituir o bloco atual por:

```csharp
    private static readonly FakeTokenHasher Hasher = new();

    private readonly FakeLogger<RenovarTokenUseCase> _logger = new();

    private (RenovarTokenUseCase uc, FakeRefreshTokenRepo repo) Montar(RefreshToken? ativo)
    {
        var repo = new FakeRefreshTokenRepo { Ativo = ativo };
        var emissor = new EmissorDeSessao(repo, Hasher, new FakeAccessTokenGenerator(), FakeJwtOptions.Instance);
        var uc = new RenovarTokenUseCase(repo, Hasher, emissor, _logger);
        return (uc, repo);
    }
```

(b) substituir `Refresh_revogado_falha_e_nao_emite` por:

```csharp
    [Fact]
    public async Task Refresh_de_token_revogado_e_reuso_queima_a_familia_do_usuario()
    {
        // Cenario de roubo: o ladrao usou o token A primeiro e recebeu o B. Quando o legitimo
        // reapresenta o A, o refresh vazou — e derrubar so o A deixaria a sessao B do ladrao viva.
        var reapresentado = TokenAtivo();
        reapresentado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);

        var (uc, repo) = Montar(reapresentado);
        var doLadrao = new RefreshToken
        {
            Id = 6,
            UsuarioId = 1,
            TokenHash = Hasher.Hash("plano-do-ladrao"),
            CriadoEm = DateTime.UtcNow.AddMinutes(-5),
            ExpiraEm = DateTime.UtcNow.AddDays(7),
        };
        repo.Adicionados.Add(doLadrao);

        var r = await uc.ExecutarAsync("plano-antigo", default);

        Assert.False(r.Sucesso);
        Assert.Equal(new[] { 1 }, repo.RevogacoesEmMassa);
        Assert.NotNull(doLadrao.RevogadoEm);   // a sessao do ladrao cai junto
        // Nao emite sessao nova: o unico "Adicionados" e o token do ladrao que o teste plantou.
        Assert.Single(repo.Adicionados);
        Assert.Null(r.Valor);
    }

    [Fact]
    public async Task Reuso_loga_warning_sem_expor_o_token()
    {
        var reapresentado = TokenAtivo();
        reapresentado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);
        var (uc, _) = Montar(reapresentado);

        await uc.ExecutarAsync("plano-antigo", default);

        var entrada = Assert.Single(_logger.Entradas);
        Assert.Equal(LogLevel.Warning, entrada.Nivel);
        Assert.Contains("euso", entrada.Mensagem);  // "Reuso"/"reuso", sem depender da caixa
        // Nunca logar segredo: nem o token em texto plano, nem o hash dele.
        Assert.DoesNotContain("plano-antigo", entrada.Mensagem);
        Assert.DoesNotContain(Hasher.Hash("plano-antigo"), entrada.Mensagem);
    }

    [Fact]
    public async Task Refresh_expirado_ou_de_usuario_desativado_nao_queima_a_familia()
    {
        // Expiracao e desativacao nao sao sinal de roubo: queimar a familia ali transformaria
        // um refresh atrasado numa deslogada geral.
        var expirado = TokenAtivo();
        expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
        var (ucExpirado, repoExpirado) = Montar(expirado);

        var desativado = TokenAtivo();
        desativado.Usuario.Ativo = false;
        var (ucDesativado, repoDesativado) = Montar(desativado);

        Assert.False((await ucExpirado.ExecutarAsync("plano-antigo", default)).Sucesso);
        Assert.False((await ucDesativado.ExecutarAsync("plano-antigo", default)).Sucesso);

        Assert.Empty(repoExpirado.RevogacoesEmMassa);
        Assert.Empty(repoDesativado.RevogacoesEmMassa);
        Assert.Equal(0, repoExpirado.Saves);
        Assert.Equal(0, repoDesativado.Saves);
    }

    [Fact]
    public async Task Token_desconhecido_nao_queima_nada()
    {
        var (uc, repo) = Montar(null);

        var r = await uc.ExecutarAsync("qualquer", default);

        Assert.False(r.Sucesso);
        Assert.Empty(repo.RevogacoesEmMassa);
        Assert.Equal(0, repo.Saves);
    }
```

(c) substituir `Falhas_nao_revelam_qual_condicao_falhou` por (o caminho de reuso entra na
comparação — é o que garante que a defesa nova não virou oráculo):

```csharp
    [Fact]
    public async Task Falhas_nao_revelam_qual_condicao_falhou()
    {
        var expirado = TokenAtivo();
        expirado.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);

        var desativado = TokenAtivo();
        desativado.Usuario.Ativo = false;

        var reusado = TokenAtivo();
        reusado.RevogadoEm = DateTime.UtcNow.AddMinutes(-5);

        var falhas = new List<Result<LoginResult>>();
        foreach (var cenario in new RefreshToken?[] { null, expirado, desativado, reusado })
        {
            var (uc, _) = Montar(cenario);
            falhas.Add(await uc.ExecutarAsync("plano-antigo", default));
        }

        // Token vazio tambem nao pode se distinguir dos demais.
        var (ucVazio, _) = Montar(TokenAtivo());
        falhas.Add(await ucVazio.ExecutarAsync("", default));

        Assert.Single(falhas.Select(f => f.Erro).Distinct());
        // O tipo do erro tambem e unico: e ele que decide o status HTTP, entao variar aqui
        // vazaria a condicao que falhou pelo codigo de resposta.
        Assert.Single(falhas.Select(f => f.TipoDoErro).Distinct());
        Assert.All(falhas, f => Assert.Equal(TipoDeErro.NaoAutorizado, f.TipoDoErro));
    }
```

(d) acrescentar `using Microsoft.Extensions.Logging;` no topo do arquivo.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Expected: FALHA DE COMPILAÇÃO — `The type or namespace name 'FakeLogger<>' could not be found` e
`RenovarTokenUseCase` não aceita 4 argumentos.

- [ ] **Step 3: Adicionar o pacote de logging à camada Application**

Em `src/Rastreamento.Application/Rastreamento.Application.csproj`, dentro do `<ItemGroup>` que já
tem `Microsoft.Extensions.Options`:

```xml
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.10" />
```

- [ ] **Step 4: Adicionar o `FakeLogger<T>`**

No fim de `tests/Rastreamento.Application.Tests/Auth/Fakes.cs`, acrescentar (e
`using Microsoft.Extensions.Logging;` no topo do arquivo):

```csharp
/// <summary>
/// Captura o que foi logado para que os testes possam provar duas coisas: que o evento de
/// seguranca sai no nivel certo e que a mensagem nao carrega segredo (token, hash, senha).
/// </summary>
public class FakeLogger<T> : ILogger<T>
{
    public record Entrada(LogLevel Nivel, string Mensagem);

    public List<Entrada> Entradas { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entradas.Add(new Entrada(logLevel, formatter(state, exception)));
}
```

- [ ] **Step 5: Implementar a detecção de reuso**

Substituir o conteúdo de `src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs` por:

```csharp
using Microsoft.Extensions.Logging;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RenovarTokenUseCase : IRenovarTokenUseCase
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenHasher _tokenHasher;
    private readonly IEmissorDeSessao _emissor;
    private readonly ILogger<RenovarTokenUseCase> _logger;

    public RenovarTokenUseCase(
        IRefreshTokenRepository refreshTokens,
        ITokenHasher tokenHasher,
        IEmissorDeSessao emissor,
        ILogger<RenovarTokenUseCase> logger)
    {
        _refreshTokens = refreshTokens;
        _tokenHasher = tokenHasher;
        _emissor = emissor;
        _logger = logger;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return Falha();

        var hash = _tokenHasher.Hash(refreshTokenPlano);

        // Em QUALQUER estado, de proposito: ObterAtivoPorHashAsync filtra RevogadoEm IS NULL e
        // deixaria a reapresentacao de um token ja rotacionado indistinguivel de "nunca existiu" —
        // que e exatamente o sinal de reuso que se quer enxergar.
        var atual = await _refreshTokens.ObterPorHashAsync(hash, ct);

        if (atual is null) return Falha();

        if (atual.RevogadoEm is not null)
        {
            // REUSO. Este token ja foi rotacionado e alguem o reapresentou: ou o legitimo depois de
            // o ladrao ter rotacionado, ou o contrario. Nos dois casos o refresh vazou, e revogar
            // so este deixaria viva a sessao emitida ao ladrao. Recomendacao do OWASP: reuse
            // detected -> invalidate the token family. Aqui se queima tudo do usuario (mais simples
            // e mais robusto que rastrear a cadeia, e num roubo confirmado se quer derrubar tudo).
            var revogados = await _refreshTokens.RevogarTodosAtivosDoUsuarioAsync(
                atual.UsuarioId, DateTime.UtcNow, ct);

            _logger.LogWarning(
                "Reuso de refresh token detectado para o usuario {UsuarioId}: {Revogados} sessao(oes) ativa(s) revogada(s).",
                atual.UsuarioId, revogados);

            // Mesmo 401 generico dos demais caminhos: a queima e efeito colateral so no banco, e
            // quem chama nao consegue distinguir reuso de "token invalido".
            return Falha();
        }

        // Expirado e usuario desativado NAO sao sinal de roubo — 401 sem queimar a familia.
        // `!atual.Usuario.Ativo`: sem isso, desativar um usuario nao o expulsa — ele continuaria
        // rotacionando o refresh ate a expiracao natural (ate 7 dias).
        if (atual.ExpiraEm <= DateTime.UtcNow || !atual.Usuario.Ativo) return Falha();

        // Revogação do antigo + emissão do novo num único save (ver EmissorDeSessao).
        var novaSessao = await _emissor.RotacionarAsync(atual, ct);
        return Result<LoginResult>.Ok(novaSessao);
    }

    /// <summary>
    /// Falha unica: todos os caminhos (vazio, desconhecido, reuso, expirado, usuario desativado)
    /// devolvem exatamente isto. Variar mensagem ou tipo aqui vazaria a condicao que falhou.
    /// </summary>
    private static Result<LoginResult> Falha() =>
        Result<LoginResult>.Falha("Refresh token inválido ou expirado.", TipoDeErro.NaoAutorizado);
}
```

- [ ] **Step 6: Rodar os testes de unidade e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Application.Tests
```

Expected: build em 0 warnings; todos os testes PASS.

- [ ] **Step 7: Criar o usuário descartável para os testes de ponta a ponta**

Criar `tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Usuario exclusivo de um teste, criado e removido por ele. Os testes de reuso de refresh token e
/// de lockout tem efeito destrutivo sobre a conta (queimar todas as sessoes; trancar por 15 min):
/// rodar contra o `admin` do seed faria um teste derrubar os outros — inclusive de outra classe,
/// que o xUnit roda em paralelo por padrao.
/// </summary>
public sealed class UsuarioDeTeste : IAsyncDisposable
{
    public const string Senha = "Senha@123";

    private readonly IServiceProvider _servicos;

    public string NomeUsuario { get; }

    public int Id { get; private set; }

    private UsuarioDeTeste(IServiceProvider servicos, string nomeUsuario)
    {
        _servicos = servicos;
        NomeUsuario = nomeUsuario;
    }

    /// <param name="prefixo">Rotulo curto (ate 17 caracteres) para identificar o teste dono.</param>
    public static async Task<UsuarioDeTeste> CriarAsync(IServiceProvider servicos, string prefixo)
    {
        // Nome unico por execucao: UQ_Usuario_NomeUsuario nao perdoa sobra de uma execucao anterior
        // que tenha morrido antes da limpeza. prefixo(<=17) + '-' + 32 hex cabe no NVARCHAR(50).
        var usuario = new UsuarioDeTeste(servicos, $"{prefixo}-{Guid.NewGuid():N}");

        using var escopo = servicos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        var linha = new Usuario
        {
            NomeUsuario = usuario.NomeUsuario,
            // BCrypt real: o login em producao roda o hasher de verdade, entao o hash tem que ser.
            SenhaHash = new BCryptPasswordHasher().Hash(Senha),
            NomeCompleto = "Usuario de Teste",
            PerfilId = perfil.Id,
            Ativo = true,
        };

        db.Usuarios.Add(linha);
        await db.SaveChangesAsync();
        usuario.Id = linha.Id;
        return usuario;
    }

    public async ValueTask DisposeAsync()
    {
        using var escopo = _servicos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

        // RefreshToken tem FK para Usuario: as linhas filhas saem primeiro.
        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => t.UsuarioId == Id).ToListAsync());
        db.Usuarios.RemoveRange(await db.Usuarios.Where(u => u.Id == Id).ToListAsync());
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 8: Escrever o teste de ponta a ponta do reuso**

Criar `tests/Rastreamento.Api.Tests/ReusoDeRefreshTokenTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Reuso de refresh token ponta a ponta, contra o SQL Server real (docker compose up -d + seed).
/// </summary>
public class ReusoDeRefreshTokenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string NomeDoCookie = "refreshToken";

    private readonly WebApplicationFactory<Program> _factory;

    public ReusoDeRefreshTokenTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Reapresentar_refresh_ja_rotacionado_derruba_todas_as_sessoes_do_usuario()
    {
        await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "reuso");

        // O legitimo faz login (token A) e renova uma vez (token B).
        var cliente = NovoCliente();
        var login = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokenA = ValorDoRefresh(login);

        var renovacao = await ComRefresh(tokenA).PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, renovacao.StatusCode);
        var tokenB = ValorDoRefresh(renovacao);

        // Alguem reapresenta o A (ja rotacionado): sinal de que o refresh vazou.
        var replay = await ComRefresh(tokenA).PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // O ponto da defesa: o B — que estava valido ate agora — cai junto.
        var aposQueima = await ComRefresh(tokenB).PostAsync("/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, aposQueima.StatusCode);

        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UsuarioId == usuario.Id).ToListAsync();
        Assert.NotEmpty(tokens);
        Assert.All(tokens, t => Assert.NotNull(t.RevogadoEm));
    }

    [Fact]
    public async Task Reuso_responde_igual_a_token_desconhecido()
    {
        // Sem oraculo: a queima e efeito colateral so no banco. Quem chama nao pode perceber que
        // acertou um token que existiu — isso confirmaria ao ladrao que o token era real.
        await using var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, "reuso");

        var cliente = NovoCliente();
        var login = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
        var tokenA = ValorDoRefresh(login);
        await ComRefresh(tokenA).PostAsync("/auth/refresh", null);

        var reuso = await ComRefresh(tokenA).PostAsync("/auth/refresh", null);
        var desconhecido = await ComRefresh("token-que-nunca-existiu").PostAsync("/auth/refresh", null);

        Assert.Equal(desconhecido.StatusCode, reuso.StatusCode);
        Assert.Equal(await desconhecido.Content.ReadAsStringAsync(),
                     await reuso.Content.ReadAsStringAsync());
    }

    // BaseAddress https: o CookieContainer do HttpClient so reenvia cookies Secure em https.
    private HttpClient NovoCliente() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>Cliente sem cookie container, com um refresh token especifico no header.</summary>
    private HttpClient ComRefresh(string refreshPlano)
    {
        var cliente = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });
        cliente.DefaultRequestHeaders.Add("Cookie", $"{NomeDoCookie}={refreshPlano}");
        return cliente;
    }

    private static string ValorDoRefresh(HttpResponseMessage resposta)
    {
        var cookie = resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{NomeDoCookie}="));
        return cookie[(NomeDoCookie.Length + 1)..cookie.IndexOf(';')];
    }
}
```

- [ ] **Step 9: Rodar os testes de ponta a ponta e confirmar que passam**

```bash
dotnet test tests/Rastreamento.Api.Tests
```

Expected: PASS, incluindo os 2 testes novos de `ReusoDeRefreshTokenTests`.

- [ ] **Step 10: Commit**

```bash
git add src/Rastreamento.Application/Rastreamento.Application.csproj \
        src/Rastreamento.Application/Auth/RenovarTokenUseCase.cs \
        tests/Rastreamento.Application.Tests/Auth/Fakes.cs \
        tests/Rastreamento.Application.Tests/Auth/RenovarTokenUseCaseTests.cs \
        tests/Rastreamento.Api.Tests/UsuarioDeTeste.cs \
        tests/Rastreamento.Api.Tests/ReusoDeRefreshTokenTests.cs
git commit -m "feat(auth): reuso de refresh token queima a familia de sessoes do usuario"
```

---

## Task 3: Estado de lockout no schema, na entidade e no repositório

Só persistência. Depois desta tarefa as colunas existem e fazem round-trip, mas nada ainda as lê —
o comportamento entra na Task 5.

**Files:**
- Modify: `specs/02-modelo-de-dados.sql` (tabela `Usuario`)
- Modify: `src/Rastreamento.Domain/Entities/Usuario.cs`
- Modify: `src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs`
- Modify: `src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs`
- Modify: `tests/Rastreamento.Application.Tests/Auth/Fakes.cs` (`FakeUsuarioRepo`)
- Test: `tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs`

**Interfaces:**
- Consumes: `RastreamentoDbContext.Usuarios`, `Perfil`.
- Produces:
  - `Usuario.FalhasConsecutivas` (`int`), `Usuario.BloqueadoAte` (`DateTime?`).
  - `Task IUsuarioRepository.SalvarAlteracoesAsync(CancellationToken ct)`.
  - `FakeUsuarioRepo.Saves` (`int`).

- [ ] **Step 1: Escrever o teste de mapeamento que falha**

Acrescentar em `tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs`,
depois de `Carrega_admin_com_perfil_navegacao`:

```csharp
    [Fact]
    public async Task Mapeia_as_colunas_de_lockout_do_usuario_com_round_trip()
    {
        await using var db = NovoContexto();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        var bloqueadoAte = DateTime.UtcNow.AddMinutes(15);
        var usuario = new Usuario
        {
            NomeUsuario = $"lockout-{Guid.NewGuid():N}",
            SenhaHash = "nao-usado-neste-teste",
            NomeCompleto = "Usuario de Teste de Lockout",
            PerfilId = perfil.Id,
            Ativo = true,
            FalhasConsecutivas = 3,
            BloqueadoAte = bloqueadoAte,
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        var id = usuario.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Usuarios.SingleAsync(u => u.Id == id);

            Assert.Equal(3, carregado.FalhasConsecutivas);
            Assert.Equal(bloqueadoAte, carregado.BloqueadoAte!.Value, TimeSpan.FromSeconds(1));
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Usuarios.RemoveRange(await dbLimpeza.Usuarios.Where(u => u.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Usuario_novo_nasce_destrancado()
    {
        // O default da coluna (0 falhas, BloqueadoAte NULL) e o que garante que o seed e qualquer
        // usuario inserido sem mencionar essas colunas nao nasca trancado.
        // Usuario proprio, e nao o `admin` do seed: o contador do admin e estado mutavel que
        // outros testes (login com senha errada) tocam, entao afirmar sobre ele seria flaky.
        await using var db = NovoContexto();
        var perfil = await db.Perfis.SingleAsync(p => p.Nome == "Administrador");

        var usuario = new Usuario
        {
            NomeUsuario = $"novo-{Guid.NewGuid():N}",
            SenhaHash = "nao-usado-neste-teste",
            NomeCompleto = "Usuario Recem-Criado",
            PerfilId = perfil.Id,
            Ativo = true,
            // FalhasConsecutivas e BloqueadoAte NAO sao informados de proposito.
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        var id = usuario.Id;

        try
        {
            await using var dbLeitura = NovoContexto();
            var carregado = await dbLeitura.Usuarios.AsNoTracking().SingleAsync(u => u.Id == id);

            Assert.Equal(0, carregado.FalhasConsecutivas);
            Assert.Null(carregado.BloqueadoAte);
        }
        finally
        {
            await using var dbLimpeza = NovoContexto();
            dbLimpeza.Usuarios.RemoveRange(await dbLimpeza.Usuarios.Where(u => u.Id == id).ToListAsync());
            await dbLimpeza.SaveChangesAsync();
        }
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests
```

Expected: FALHA DE COMPILAÇÃO — `'Usuario' does not contain a definition for 'FalhasConsecutivas'`.

- [ ] **Step 3: Alterar a fonte da verdade do schema**

Em `specs/02-modelo-de-dados.sql`, na `CREATE TABLE dbo.Usuario`, inserir as duas colunas logo
depois da linha do `Ativo`:

```sql
CREATE TABLE dbo.Usuario (
    Id              INT IDENTITY(1,1)  NOT NULL,
    NomeUsuario     NVARCHAR(50)        NOT NULL,
    SenhaHash       NVARCHAR(200)       NOT NULL,
    NomeCompleto    NVARCHAR(200)       NOT NULL,
    PerfilId        INT                 NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Usuario_Ativo DEFAULT (1),
    -- Lockout de conta (anti brute-force): falhas de senha em sequência e até quando a conta
    -- está trancada. BloqueadoAte NULL = destrancada; a trava expira sozinha, sem ação de admin.
    -- O contador zera no login bem-sucedido e também no momento em que a trava é aplicada.
    FalhasConsecutivas INT              NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0),
    BloqueadoAte    DATETIME2           NULL,
    CONSTRAINT PK_Usuario PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Usuario_NomeUsuario UNIQUE (NomeUsuario),
    CONSTRAINT FK_Usuario_Perfil FOREIGN KEY (PerfilId) REFERENCES dbo.Perfil (Id)
);
GO
```

- [ ] **Step 4: Aplicar a mudança no banco local**

O `.sql` é um script de criação: no banco que já existe, a mudança entra por `ALTER TABLE`.
É idempotente (`IF COL_LENGTH(...) IS NULL`), então rodar duas vezes não quebra.

```bash
docker compose up -d
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Usuario','FalhasConsecutivas') IS NULL ALTER TABLE dbo.Usuario ADD FalhasConsecutivas INT NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0), BloqueadoAte DATETIME2 NULL;"
```

Verificar:

```bash
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -d Rastreamento \
  -Q "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuario') ORDER BY column_id;"
```

Expected: a lista termina com `FalhasConsecutivas` e `BloqueadoAte`.

- [ ] **Step 5: Refletir o schema na entidade**

Substituir o conteúdo de `src/Rastreamento.Domain/Entities/Usuario.cs` por:

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

    /// <summary>Falhas de senha em sequencia desde o ultimo sucesso ou desde a ultima trava.</summary>
    public int FalhasConsecutivas { get; set; }

    /// <summary>
    /// Ate quando a conta esta trancada, em UTC. <c>null</c> ou no passado = destrancada — a trava
    /// expira sozinha, sem intervencao de admin (ver <c>LockoutOptions.DuracaoMinutos</c>).
    /// </summary>
    public DateTime? BloqueadoAte { get; set; }

    public Perfil Perfil { get; set; } = null!;
}
```

Não é preciso mexer em `UsuarioConfiguration`: `int` → `INT NOT NULL` e `DateTime?` →
`datetime2 NULL` são exatamente o que a convenção do EF gera, e os nomes das colunas batem com os
das propriedades. Quem prova isso é o teste de round-trip do Step 1.

- [ ] **Step 6: Dar ao repositório de usuário a capacidade de persistir**

Substituir o conteúdo de `src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs` por:

```csharp
using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IUsuarioRepository
{
    /// <summary>
    /// Retorna o usuario RASTREADO (sem <c>AsNoTracking</c>), com <c>Perfil</c> carregado. O
    /// rastreamento e requisito do lockout: o caso de uso muta <c>FalhasConsecutivas</c> /
    /// <c>BloqueadoAte</c> na entidade e conta com o change tracking para o
    /// <see cref="SalvarAlteracoesAsync"/> enxergar a mudanca.
    /// </summary>
    Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
```

Substituir o conteúdo de `src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly RastreamentoDbContext _db;

    public UsuarioRepository(RastreamentoDbContext db) => _db = db;

    // Perfil vem junto porque o nome do perfil vira a claim `role` do access token.
    // Sem AsNoTracking de proposito: ver o contrato da interface (lockout depende do tracking).
    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        _db.Usuarios.Include(u => u.Perfil)
            .SingleOrDefaultAsync(u => u.NomeUsuario == nomeUsuario, ct);

    public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 7: Fazer o fake acompanhar o contrato**

Em `tests/Rastreamento.Application.Tests/Auth/Fakes.cs`, substituir a classe `FakeUsuarioRepo`
inteira por:

```csharp
public class FakeUsuarioRepo : IUsuarioRepository
{
    private readonly Usuario? _usuario;

    public FakeUsuarioRepo(Usuario? usuario) => _usuario = usuario;

    /// <summary>Quantos commits o repositorio recebeu — permite provar que o contador de falhas
    /// realmente foi persistido (e que o caminho de miss nao escreve nada).</summary>
    public int Saves { get; private set; }

    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        Task.FromResult(_usuario is not null && _usuario.NomeUsuario == nomeUsuario ? _usuario : null);

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 8: Rodar os testes e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Infrastructure.Tests
dotnet test tests/Rastreamento.Application.Tests
```

Expected: build em 0 warnings; ambos PASS, incluindo os 2 testes novos de mapeamento.

- [ ] **Step 9: Commit**

```bash
git add specs/02-modelo-de-dados.sql \
        src/Rastreamento.Domain/Entities/Usuario.cs \
        src/Rastreamento.Domain/Abstractions/IUsuarioRepository.cs \
        src/Rastreamento.Infrastructure/Persistence/UsuarioRepository.cs \
        tests/Rastreamento.Application.Tests/Auth/Fakes.cs \
        tests/Rastreamento.Infrastructure.Tests/Persistence/DbContextMappingTests.cs
git commit -m "feat(db): colunas de lockout em Usuario + repositorio persiste alteracoes"
```

---

## Task 4: Configuração de lockout validada no startup

`LockoutOptions` existe, é bindada do appsettings e derruba a aplicação se vier inválida. Ainda
ninguém a consome — isso é a Task 5.

**Files:**
- Create: `src/Rastreamento.Application/Auth/LockoutOptions.cs`
- Create: `src/Rastreamento.Application/Auth/LockoutOptionsValidator.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Modify: `src/Rastreamento.Api/appsettings.json`
- Test: `tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs`

**Interfaces:**
- Consumes: `IValidateOptions<T>` / `ValidateOptionsResult` (padrão do `JwtOptionsValidator`).
- Produces: `LockoutOptions` com `MaxFalhas` (`int`, default 5) e `DuracaoMinutos` (`int`,
  default 15); `LockoutOptionsValidator : IValidateOptions<LockoutOptions>`; seção
  `"Lockout"` no appsettings.

- [ ] **Step 1: Escrever os testes de startup que falham**

Acrescentar em `tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs`, depois de
`Aplicacao_nao_sobe_com_tempo_de_vida_nao_positivo`:

```csharp
    [Theory]
    [InlineData("Lockout:MaxFalhas")]
    [InlineData("Lockout:DuracaoMinutos")]
    public void Aplicacao_nao_sobe_com_lockout_nao_positivo(string chave)
    {
        // MaxFalhas=0 trancaria toda conta no primeiro erro de digitacao; DuracaoMinutos=0 seria
        // uma trava que ja nasce expirada — nos dois casos a defesa vira outra coisa em silencio.
        var excecao = Record.Exception(() => SubirApi(new() { [chave] = "0" }));

        var validacao = Assert.IsType<OptionsValidationException>(excecao);
        Assert.Contains("Lockout", validacao.Message, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ConfiguracaoDeStartupTests"
```

Expected: FAIL — `Assert.IsType() Failure: Value is null` (a aplicação sobe normalmente com
`Lockout:MaxFalhas=0`, porque nada valida essa seção ainda).

- [ ] **Step 3: Criar as options**

Criar `src/Rastreamento.Application/Auth/LockoutOptions.cs`:

```csharp
namespace Rastreamento.Application.Auth;

/// <summary>
/// Politica de lockout de conta (secao <c>Lockout</c> do appsettings). E a trava FINA, por conta,
/// que complementa o rate limit (teto grosso, por IP): uma nao substitui a outra.
/// </summary>
public class LockoutOptions
{
    /// <summary>Falhas de senha em sequencia ate a conta ser trancada.</summary>
    public int MaxFalhas { get; set; } = 5;

    /// <summary>
    /// Duracao da trava, em minutos. E temporaria de proposito: expira sozinha, sem intervencao de
    /// admin. Assim, um atacante que tranque um operador so atrasa o acesso dele por esse tempo —
    /// o lockout-DoS fica limitado e aceito.
    /// </summary>
    public int DuracaoMinutos { get; set; } = 15;
}
```

Criar `src/Rastreamento.Application/Auth/LockoutOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Rastreamento.Application.Auth;

/// <summary>
/// Valida a secao <c>Lockout</c> no startup (via <c>ValidateOnStart</c>), mesmo padrao do
/// <see cref="JwtOptionsValidator"/>. Valor nao positivo aqui nao explode: ele desfigura a defesa
/// em silencio (<c>MaxFalhas=0</c> tranca no primeiro erro de digitacao; <c>DuracaoMinutos=0</c> e
/// uma trava que ja nasce expirada). Configuracao errada tem que derrubar a aplicacao.
/// </summary>
public class LockoutOptionsValidator : IValidateOptions<LockoutOptions>
{
    public ValidateOptionsResult Validate(string? name, LockoutOptions options)
    {
        var falhas = new List<string>();

        if (options.MaxFalhas <= 0)
            falhas.Add($"Lockout:{nameof(LockoutOptions.MaxFalhas)} deve ser maior que zero.");

        if (options.DuracaoMinutos <= 0)
            falhas.Add($"Lockout:{nameof(LockoutOptions.DuracaoMinutos)} deve ser maior que zero.");

        return falhas.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(falhas);
    }
}
```

- [ ] **Step 4: Registrar no startup**

Em `src/Rastreamento.Api/Program.cs`, logo depois do bloco `AddOptions<JwtOptions>()`, inserir:

```csharp
builder.Services.AddSingleton<IValidateOptions<LockoutOptions>, LockoutOptionsValidator>();
builder.Services.AddOptions<LockoutOptions>()
    .Bind(builder.Configuration.GetSection("Lockout"))
    .ValidateOnStart();
```

- [ ] **Step 5: Adicionar os defaults ao appsettings**

Em `src/Rastreamento.Api/appsettings.json`, acrescentar a seção `Lockout` depois de `Jwt`
(o arquivo inteiro fica assim — a seção `RateLimit` entra na Task 6):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
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
  },
  "Lockout": {
    "MaxFalhas": 5,
    "DuracaoMinutos": 15
  }
}
```

- [ ] **Step 6: Rodar os testes e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~ConfiguracaoDeStartupTests"
```

Expected: build em 0 warnings; PASS, incluindo `Aplicacao_nao_sobe_com_lockout_nao_positivo`
(2 casos) e o contraprova `Aplicacao_sobe_com_a_configuracao_do_repositorio`.

- [ ] **Step 7: Commit**

```bash
git add src/Rastreamento.Application/Auth/LockoutOptions.cs \
        src/Rastreamento.Application/Auth/LockoutOptionsValidator.cs \
        src/Rastreamento.Api/Program.cs \
        src/Rastreamento.Api/appsettings.json \
        tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs
git commit -m "feat(auth): LockoutOptions validada no startup"
```

---

## Task 5: Lockout de conta no login

Fecha a Seção B da spec. Cinco senhas erradas em sequência trancam a conta por 15 minutos — sem
abrir mão do trabalho constante nem do 401 genérico.

**Files:**
- Modify: `src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs`
- Test: `tests/Rastreamento.Application.Tests/Auth/AutenticarUsuarioUseCaseTests.cs`
- Test: `tests/Rastreamento.Api.Tests/LockoutTests.cs` (novo)

**Interfaces:**
- Consumes: `LockoutOptions` (Task 4), `IUsuarioRepository.SalvarAlteracoesAsync` (Task 3),
  `Usuario.FalhasConsecutivas` / `.BloqueadoAte` (Task 3), `FakeLogger<T>` (Task 2),
  `UsuarioDeTeste` (Task 2).
- Produces:
  `AutenticarUsuarioUseCase(IUsuarioRepository, IPasswordHasher, IEmissorDeSessao, IOptions<LockoutOptions>, ILogger<AutenticarUsuarioUseCase>)`
  — assinatura nova do construtor.

> **Por que o teste ponta a ponta não usa o `admin`:** trancar o `admin` do seed por 15 minutos
> deixaria toda a `AuthEndpointsTests` vermelha. O `AuthEndpointsTests` continua fazendo 2 logins
> com senha errada por execução contra o `admin` — bem abaixo do `MaxFalhas` de 5, e cada login
> bem-sucedido zera o contador, então ele não se tranca sozinho. Se em algum momento essa margem
> encolher, a saída é migrar aqueles dois testes para `UsuarioDeTeste`, não afrouxar o lockout.

- [ ] **Step 1: Escrever os testes de unidade que falham**

Em `tests/Rastreamento.Application.Tests/Auth/AutenticarUsuarioUseCaseTests.cs`:

(a) acrescentar no topo do arquivo:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

(b) substituir os campos e o helper `NovoUseCase` pelo bloco abaixo (todas as chamadas existentes
continuam válidas — os parâmetros novos têm default):

```csharp
    // Instancia por teste (o xUnit cria uma instancia da classe por [Fact]): os contadores do
    // fake nao vazam de um teste para o outro.
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeLogger<AutenticarUsuarioUseCase> _logger = new();

    /// <summary>O repositorio da ultima chamada a <see cref="NovoUseCase"/>.</summary>
    private FakeUsuarioRepo _usuarios = null!;

    private Usuario AdminAtivo() => new()
    {
        Id = 1,
        NomeUsuario = "admin",
        NomeCompleto = "Administrador do Sistema",
        Ativo = true,
        SenhaHash = _hasher.Hash(SenhaCorreta),
        Perfil = new Perfil { Nome = "Administrador" }
    };

    private AutenticarUsuarioUseCase NovoUseCase(
        Usuario? usuario,
        out FakeRefreshTokenRepo refreshRepo,
        int maxFalhas = 5,
        int duracaoMinutos = 15)
    {
        refreshRepo = new FakeRefreshTokenRepo();
        _usuarios = new FakeUsuarioRepo(usuario);
        var emissor = new EmissorDeSessao(
            refreshRepo,
            new FakeTokenHasher(),
            new FakeAccessTokenGenerator(),
            FakeJwtOptions.Instance);
        return new AutenticarUsuarioUseCase(
            _usuarios,
            _hasher,
            emissor,
            Options.Create(new LockoutOptions { MaxFalhas = maxFalhas, DuracaoMinutos = duracaoMinutos }),
            _logger);
    }
```

(c) acrescentar no fim da classe:

```csharp
    // ----- Lockout de conta ----------------------------------------------------------------

    [Fact]
    public async Task Senha_errada_incrementa_o_contador_e_persiste()
    {
        var admin = AdminAtivo();
        var uc = NovoUseCase(admin, out _);

        await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

        Assert.Equal(1, admin.FalhasConsecutivas);
        Assert.Null(admin.BloqueadoAte);
        Assert.Equal(1, _usuarios.Saves);   // incrementar sem salvar nao tranca ninguem
    }

    [Fact]
    public async Task Atingir_o_limite_tranca_a_conta_e_zera_o_contador()
    {
        var admin = AdminAtivo();
        var uc = NovoUseCase(admin, out _, maxFalhas: 3, duracaoMinutos: 15);

        for (var i = 0; i < 3; i++)
            await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

        Assert.NotNull(admin.BloqueadoAte);
        Assert.True(admin.BloqueadoAte > DateTime.UtcNow.AddMinutes(14));
        Assert.True(admin.BloqueadoAte < DateTime.UtcNow.AddMinutes(16));
        // Zera de proposito: apos a trava expirar a conta recomeca limpa, e nao a uma falha
        // de ser trancada de novo para sempre.
        Assert.Equal(0, admin.FalhasConsecutivas);
    }

    [Fact]
    public async Task Conta_trancada_falha_mesmo_com_a_senha_certa()
    {
        var admin = AdminAtivo();
        admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
        var uc = NovoUseCase(admin, out var refreshRepo);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.False(r.Sucesso);
        Assert.Equal(ErroGenerico, r.Erro);
        Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
        Assert.Empty(refreshRepo.Adicionados);
    }

    [Fact]
    public async Task Tentativa_em_conta_trancada_nao_estende_a_trava()
    {
        var admin = AdminAtivo();
        var travaOriginal = DateTime.UtcNow.AddMinutes(10);
        admin.BloqueadoAte = travaOriginal;
        var uc = NovoUseCase(admin, out _);

        await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

        // Nem incrementa nem re-tranca: senao um atacante persistente manteria a conta do
        // operador trancada indefinidamente.
        Assert.Equal(travaOriginal, admin.BloqueadoAte);
        Assert.Equal(0, admin.FalhasConsecutivas);
        Assert.Equal(0, _usuarios.Saves);
    }

    [Fact]
    public async Task Trava_expirada_deixa_a_conta_entrar_de_novo()
    {
        var admin = AdminAtivo();
        admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(-1);   // ja passou
        admin.FalhasConsecutivas = 0;
        var uc = NovoUseCase(admin, out _);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.True(r.Sucesso);
        // Login bem-sucedido limpa o rastro da trava.
        Assert.Null(admin.BloqueadoAte);
        Assert.Equal(0, admin.FalhasConsecutivas);
    }

    [Fact]
    public async Task Login_bem_sucedido_zera_falhas_acumuladas()
    {
        var admin = AdminAtivo();
        admin.FalhasConsecutivas = 3;
        var uc = NovoUseCase(admin, out _);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.True(r.Sucesso);
        Assert.Equal(0, admin.FalhasConsecutivas);
        Assert.Equal(1, _usuarios.Saves);
    }

    [Fact]
    public async Task Login_limpo_nao_escreve_no_banco_a_toa()
    {
        var uc = NovoUseCase(AdminAtivo(), out _);

        var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.True(r.Sucesso);
        // Nada mudou no usuario: um UPDATE por login de rotina seria escrita pura de desperdicio.
        Assert.Equal(0, _usuarios.Saves);
    }

    [Fact]
    public async Task Usuario_inexistente_nao_escreve_nada()
    {
        var uc = NovoUseCase(null, out _);

        await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

        Assert.Equal(0, _usuarios.Saves);
    }

    [Fact]
    public async Task Trancar_a_conta_loga_warning_sem_a_senha()
    {
        var admin = AdminAtivo();
        var uc = NovoUseCase(admin, out _, maxFalhas: 2);

        await uc.ExecutarAsync(new LoginRequest("admin", "senha-secreta-do-teste"), default);
        Assert.Empty(_logger.Entradas);   // so a trava e evento de seguranca, nao cada erro

        await uc.ExecutarAsync(new LoginRequest("admin", "senha-secreta-do-teste"), default);

        var entrada = Assert.Single(_logger.Entradas);
        Assert.Equal(LogLevel.Warning, entrada.Nivel);
        Assert.Contains("admin", entrada.Mensagem);
        Assert.DoesNotContain("senha-secreta-do-teste", entrada.Mensagem);
    }

    [Fact]
    public async Task Conta_trancada_ainda_verifica_a_senha()
    {
        // Trabalho constante tambem no caminho novo: se o BCrypt fosse pulado para conta trancada,
        // a resposta voltaria ~100ms antes e revelaria o estado da conta pelo tempo.
        var admin = AdminAtivo();
        admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
        var uc = NovoUseCase(admin, out _);

        await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

        Assert.Equal(1, _hasher.Verificacoes);
        Assert.Equal(admin.SenhaHash, _hasher.UltimoHashVerificado);
    }

    [Fact]
    public async Task Falhas_de_login_nao_revelam_qual_condicao_falhou()
    {
        var trancado = AdminAtivo();
        trancado.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);

        var inativo = AdminAtivo();
        inativo.Ativo = false;

        var falhas = new List<Result<LoginResult>>();

        // Senha errada, conta trancada COM a senha certa, conta inativa e usuario inexistente
        // tem que ser indistinguiveis pelo corpo e pelo tipo do erro.
        var ucSenhaErrada = NovoUseCase(AdminAtivo(), out _);
        falhas.Add(await ucSenhaErrada.ExecutarAsync(new LoginRequest("admin", "errada"), default));

        var ucTrancado = NovoUseCase(trancado, out _);
        falhas.Add(await ucTrancado.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default));

        var ucInativo = NovoUseCase(inativo, out _);
        falhas.Add(await ucInativo.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default));

        var ucInexistente = NovoUseCase(null, out _);
        falhas.Add(await ucInexistente.ExecutarAsync(new LoginRequest("ninguem", "x"), default));

        Assert.All(falhas, f => Assert.False(f.Sucesso));
        Assert.Single(falhas.Select(f => f.Erro).Distinct());
        Assert.Single(falhas.Select(f => f.TipoDoErro).Distinct());
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
dotnet test tests/Rastreamento.Application.Tests
```

Expected: FALHA DE COMPILAÇÃO — `AutenticarUsuarioUseCase` não aceita 5 argumentos.

- [ ] **Step 3: Implementar o lockout**

Substituir o conteúdo de `src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs` por:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class AutenticarUsuarioUseCase : IAutenticarUsuarioUseCase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmissorDeSessao _emissor;
    private readonly LockoutOptions _lockout;
    private readonly ILogger<AutenticarUsuarioUseCase> _logger;

    public AutenticarUsuarioUseCase(
        IUsuarioRepository usuarios,
        IPasswordHasher passwordHasher,
        IEmissorDeSessao emissor,
        IOptions<LockoutOptions> lockout,
        ILogger<AutenticarUsuarioUseCase> logger)
    {
        _usuarios = usuarios;
        _passwordHasher = passwordHasher;
        _emissor = emissor;
        _lockout = lockout.Value;
        _logger = logger;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorNomeUsuarioAsync(req.NomeUsuario, ct);
        var agora = DateTime.UtcNow;

        // Trabalho constante: o BCrypt roda SEMPRE, inclusive quando o usuario nao existe, esta
        // inativo ou esta trancado. Com o curto-circuito do `||` a verificacao era pulada nesses
        // casos e a resposta voltava ~100ms antes da de "senha errada" — corpo identico, tempo
        // diferente. O lockout nao pode reabrir esse oraculo: nenhum return antes desta linha.
        var hashDeReferencia = usuario is not null && usuario.Ativo
            ? usuario.SenhaHash
            : _passwordHasher.HashFicticio;
        var senhaConfere = _passwordHasher.Verificar(req.Senha, hashDeReferencia);

        // Usuario inexistente ou inativo: 401 sem tocar em contador nenhum (nao ha o que contar,
        // e escrever aqui daria ao atacante um sinal de existencia da conta).
        if (usuario is null || !usuario.Ativo) return Falha();

        // Conta trancada: falha MESMO com a senha certa, e sem incrementar — se cada tentativa
        // estendesse a trava, um atacante persistente manteria o operador de fora indefinidamente.
        if (usuario.BloqueadoAte > agora) return Falha();

        if (!senhaConfere)
        {
            usuario.FalhasConsecutivas++;

            if (usuario.FalhasConsecutivas >= _lockout.MaxFalhas)
            {
                usuario.BloqueadoAte = agora.AddMinutes(_lockout.DuracaoMinutos);
                // Zera junto: depois que a trava expirar a conta recomeca limpa, em vez de ficar
                // a uma falha de ser trancada de novo para sempre.
                usuario.FalhasConsecutivas = 0;

                _logger.LogWarning(
                    "Conta {NomeUsuario} trancada por excesso de falhas de login ate {BloqueadoAte:o} (UTC).",
                    usuario.NomeUsuario, usuario.BloqueadoAte);
            }

            await _usuarios.SalvarAlteracoesAsync(ct);
            return Falha();
        }

        // Sucesso: limpa o rastro. A escrita e condicional de proposito — um UPDATE em todo login
        // de rotina seria puro desperdicio, e aqui nao ha oraculo a proteger (o usuario ja provou
        // quem e).
        if (usuario.FalhasConsecutivas != 0 || usuario.BloqueadoAte is not null)
        {
            usuario.FalhasConsecutivas = 0;
            usuario.BloqueadoAte = null;
            await _usuarios.SalvarAlteracoesAsync(ct);
        }

        var sessao = await _emissor.EmitirAsync(usuario, ct);
        return Result<LoginResult>.Ok(sessao);
    }

    /// <summary>
    /// Falha unica e generica: usuario inexistente, inativo, conta trancada (mesmo com a senha
    /// certa) e senha errada sao indistinguiveis para quem chama.
    /// </summary>
    private static Result<LoginResult> Falha() =>
        Result<LoginResult>.Falha("Usuário ou senha inválidos.", TipoDeErro.NaoAutorizado);
}
```

- [ ] **Step 4: Rodar os testes de unidade e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Application.Tests
```

Expected: build em 0 warnings; todos PASS, incluindo os 11 testes novos de lockout.

- [ ] **Step 5: Escrever o teste de ponta a ponta do lockout**

Criar `tests/Rastreamento.Api.Tests/LockoutTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Lockout ponta a ponta, contra o SQL Server real (docker compose up -d + seed). Usa uma fabrica
/// propria com <c>MaxFalhas=3</c> — menos requisicoes que o default — e um usuario descartavel:
/// trancar o `admin` do seed por 15 minutos quebraria todo o resto da suite.
/// </summary>
public class LockoutTests
{
    private static WebApplicationFactory<Program> NovaFabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lockout:MaxFalhas"] = "3",
                ["Lockout:DuracaoMinutos"] = "15",
            })));

    [Fact]
    public async Task Tres_senhas_erradas_trancam_a_conta_mesmo_para_a_senha_certa()
    {
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
        {
            var errada = await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });
            Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        }

        var comSenhaCerta = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });

        Assert.Equal(HttpStatusCode.Unauthorized, comSenhaCerta.StatusCode);
        Assert.DoesNotContain("Set-Cookie", comSenhaCerta.Headers.Select(h => h.Key));

        var linha = await ComBancoAsync(fabrica, db =>
            db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id));
        Assert.NotNull(linha.BloqueadoAte);
        Assert.True(linha.BloqueadoAte > DateTime.UtcNow);
        Assert.Equal(0, linha.FalhasConsecutivas);
    }

    [Fact]
    public async Task Conta_trancada_responde_igual_a_senha_errada()
    {
        // Sem oraculo: o atacante nao pode distinguir "tranquei a conta" de "errei a senha" —
        // saber que trancou ja confirma que a conta existe.
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
            await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });

        var trancada = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });
        var inexistente = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = $"ninguem-{Guid.NewGuid():N}", senha = "errada" });

        Assert.Equal(inexistente.StatusCode, trancada.StatusCode);
        Assert.Equal(await inexistente.Content.ReadAsStringAsync(),
                     await trancada.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Trava_expirada_libera_o_login_e_limpa_o_estado()
    {
        using var fabrica = NovaFabrica();
        await using var usuario = await UsuarioDeTeste.CriarAsync(fabrica.Services, "lockout");
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < 3; i++)
            await cliente.PostAsJsonAsync("/auth/login",
                new { nomeUsuario = usuario.NomeUsuario, senha = "errada" });

        // Empurra a trava para o passado em vez de esperar 15 minutos: o que se quer exercitar e
        // a comparacao com o relogio, nao a passagem real do tempo.
        await ComBancoAsync(fabrica, async db =>
        {
            var linha = await db.Usuarios.SingleAsync(u => u.Id == usuario.Id);
            linha.BloqueadoAte = DateTime.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });

        var resposta = await cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = usuario.NomeUsuario, senha = UsuarioDeTeste.Senha });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        var depois = await ComBancoAsync(fabrica, db =>
            db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario.Id));
        Assert.Null(depois.BloqueadoAte);
        Assert.Equal(0, depois.FalhasConsecutivas);
    }

    // BaseAddress https: o CookieContainer do HttpClient so reenvia cookies Secure em https.
    private static HttpClient NovoCliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task<T> ComBancoAsync<T>(
        WebApplicationFactory<Program> fabrica, Func<RastreamentoDbContext, Task<T>> consulta)
    {
        using var escopo = fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>());
    }
}
```

- [ ] **Step 6: Rodar os testes de ponta a ponta e confirmar que passam**

```bash
dotnet test tests/Rastreamento.Api.Tests
```

Expected: PASS, incluindo os 3 testes de `LockoutTests`.

- [ ] **Step 7: Commit**

```bash
git add src/Rastreamento.Application/Auth/AutenticarUsuarioUseCase.cs \
        tests/Rastreamento.Application.Tests/Auth/AutenticarUsuarioUseCaseTests.cs \
        tests/Rastreamento.Api.Tests/LockoutTests.cs
git commit -m "feat(auth): lockout temporario de conta apos falhas consecutivas de login"
```

---

## Task 6: Rate limiting no `/auth/login`

Fecha a Seção C da spec. Teto grosso por IP, janela fixa, só no login.

**Files:**
- Create: `src/Rastreamento.Api/Configuration/RateLimitOptions.cs`
- Create: `src/Rastreamento.Api/Configuration/RateLimitOptionsValidator.cs`
- Modify: `src/Rastreamento.Api/Program.cs`
- Modify: `src/Rastreamento.Api/Controllers/AuthController.cs` (só o atributo)
- Modify: `src/Rastreamento.Api/appsettings.json`
- Modify: `src/Rastreamento.Api/appsettings.Development.json`
- Test: `tests/Rastreamento.Api.Tests/RateLimitTests.cs` (novo)
- Test: `tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs`

**Interfaces:**
- Consumes: `IValidateOptions<T>`, middleware nativo `Microsoft.AspNetCore.RateLimiting` /
  `System.Threading.RateLimiting` (sem pacote novo — vem do framework).
- Produces: `RateLimitOptions` com `PermitLimit` (`int`, default 10), `WindowSeconds` (`int`,
  default 60) e a const `NomeDaPoliticaDeLogin = "login"`;
  `RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>`; seção `"RateLimit"` no
  appsettings.

> **Por que `appsettings.Development.json` recebe um limite folgado:** no `TestServer` do
> `WebApplicationFactory` o `RemoteIpAddress` é `null`, então **toda** requisição de teste cai na
> mesma partição. Com o default de produção (10/60s), a dezena de logins de `AuthEndpointsTests`
> estouraria o limite e a suíte ficaria vermelha por um motivo que não é o dela. O limite real
> continua provado — pelo `RateLimitTests`, que sobe uma fábrica própria com um limite pequeno.

- [ ] **Step 1: Escrever o teste do 429 que falha**

Criar `tests/Rastreamento.Api.Tests/RateLimitTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Rate limit do <c>/auth/login</c>. Fabrica propria com limite pequeno: no TestServer o
/// <c>RemoteIpAddress</c> e null, entao todas as requisicoes compartilham a mesma particao — se
/// este teste usasse a fabrica compartilhada, apertaria o limite para os demais testes.
/// As tentativas usam um usuario inexistente de proposito: assim o teste nao mexe no contador de
/// lockout de nenhuma conta real.
/// </summary>
public class RateLimitTests
{
    private const int Limite = 3;

    private static WebApplicationFactory<Program> NovaFabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:PermitLimit"] = Limite.ToString(),
                ["RateLimit:WindowSeconds"] = "60",
            })));

    private static HttpClient NovoCliente(WebApplicationFactory<Program> fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private static Task<HttpResponseMessage> TentarLoginAsync(HttpClient cliente) =>
        cliente.PostAsJsonAsync("/auth/login",
            new { nomeUsuario = $"ninguem-{Guid.NewGuid():N}", senha = "errada" });

    [Fact]
    public async Task Login_alem_do_limite_responde_429_com_Retry_After()
    {
        using var fabrica = NovaFabrica();
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < Limite; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await TentarLoginAsync(cliente)).StatusCode);

        var barrada = await TentarLoginAsync(cliente);

        Assert.Equal(HttpStatusCode.TooManyRequests, barrada.StatusCode);
        // Sem Retry-After o cliente so pode adivinhar quando voltar — e adivinhar em loop.
        Assert.True(barrada.Headers.RetryAfter is not null,
            "resposta 429 deveria trazer o header Retry-After");
    }

    [Fact]
    public async Task Refresh_nao_e_limitado()
    {
        // Escopo deliberado: /auth/refresh e legitimo e frequente (a cada ~15 min por usuario,
        // mais retries), e o refresh token e opaco de 256 bits — forca bruta nele e inviavel.
        // Throttlar refresh puniria o operador em wifi ruim sem fechar nenhum ataque real.
        using var fabrica = NovaFabrica();
        var cliente = NovoCliente(fabrica);

        for (var i = 0; i < Limite + 3; i++)
        {
            var resposta = await cliente.PostAsync("/auth/refresh", null);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }
    }
}
```

E acrescentar em `tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs`:

```csharp
    [Theory]
    [InlineData("RateLimit:PermitLimit")]
    [InlineData("RateLimit:WindowSeconds")]
    public void Aplicacao_nao_sobe_com_rate_limit_nao_positivo(string chave)
    {
        // PermitLimit=0 barraria todo login; WindowSeconds=0 e uma janela sem duracao. Nos dois
        // casos o /auth/login para de funcionar — melhor nao subir do que subir quebrado.
        var excecao = Record.Exception(() => SubirApi(new() { [chave] = "0" }));

        var validacao = Assert.IsType<OptionsValidationException>(excecao);
        Assert.Contains("RateLimit", validacao.Message, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
dotnet test tests/Rastreamento.Api.Tests --filter "FullyQualifiedName~RateLimitTests|FullyQualifiedName~ConfiguracaoDeStartupTests"
```

Expected: FAIL — `Login_alem_do_limite_responde_429_com_Retry_After` recebe `Unauthorized` em vez
de `TooManyRequests`, e `Aplicacao_nao_sobe_com_rate_limit_nao_positivo` recebe `null` em vez de
`OptionsValidationException`.

- [ ] **Step 3: Criar as options do rate limit**

Criar `src/Rastreamento.Api/Configuration/RateLimitOptions.cs`:

```csharp
namespace Rastreamento.Api.Configuration;

/// <summary>
/// Politica de rate limit do <c>/auth/login</c> (secao <c>RateLimit</c> do appsettings). Fica na
/// camada Api, e nao na Application, porque e politica de transporte HTTP — nao regra de negocio
/// de autenticacao (essa e a <c>LockoutOptions</c>).
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// Nome da politica aplicada ao <c>/auth/login</c> via <c>[EnableRateLimiting]</c>. Const para
    /// que o registro no <c>Program.cs</c> e o atributo no controller nao possam divergir.
    /// </summary>
    public const string NomeDaPoliticaDeLogin = "login";

    /// <summary>Tentativas permitidas por IP dentro da janela.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>Tamanho da janela fixa, em segundos.</summary>
    public int WindowSeconds { get; set; } = 60;
}
```

Criar `src/Rastreamento.Api/Configuration/RateLimitOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Rastreamento.Api.Configuration;

/// <summary>
/// Valida a secao <c>RateLimit</c> no startup (via <c>ValidateOnStart</c>), mesmo padrao do
/// <c>JwtOptionsValidator</c>. Valor nao positivo aqui nao explode sozinho: <c>PermitLimit=0</c>
/// barra todo login e <c>WindowSeconds=0</c> e uma janela sem duracao — nos dois casos a API sobe
/// limpa e o login simplesmente para de funcionar.
/// </summary>
public class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitOptions options)
    {
        var falhas = new List<string>();

        if (options.PermitLimit <= 0)
            falhas.Add($"RateLimit:{nameof(RateLimitOptions.PermitLimit)} deve ser maior que zero.");

        if (options.WindowSeconds <= 0)
            falhas.Add($"RateLimit:{nameof(RateLimitOptions.WindowSeconds)} deve ser maior que zero.");

        return falhas.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(falhas);
    }
}
```

- [ ] **Step 4: Registrar o rate limiter no `Program.cs`**

Acrescentar aos `using` do topo de `src/Rastreamento.Api/Program.cs`:

```csharp
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Rastreamento.Api.Configuration;
```

Inserir, logo depois do bloco `AddOptions<LockoutOptions>()` da Task 4:

```csharp
builder.Services.AddSingleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>();
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection("RateLimit"))
    .ValidateOnStart();

// Teto grosso POR IP contra forca bruta de login. Complementa o lockout (trava fina, por conta):
// um barra o flood de uma origem, o outro protege a conta especifica; nenhum substitui o outro.
// Escopo deliberado: so o /auth/login. O /auth/refresh fica de fora — e legitimo e frequente, e o
// refresh token e opaco de 256 bits (forca bruta inviavel), entao throttlar so puniria o operador.
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiter.AddPolicy(RateLimitOptions.NomeDaPoliticaDeLogin, http =>
    {
        var politica = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // Particao por IP. Atras de proxy reverso isto vira o IP do proxy e o limite passa a ser
        // global — quando houver proxy, configurar ForwardedHeaders (anotado no CLAUDE.md).
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = politica.PermitLimit,
                Window = TimeSpan.FromSeconds(politica.WindowSeconds),
                // Sem fila: excedeu, recusa na hora. Enfileirar seguraria conexao a toa e daria ao
                // atacante um jeito de consumir recurso do servidor sem levar 429.
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    rateLimiter.OnRejected = (contexto, _) =>
    {
        // Sem Retry-After o cliente so pode adivinhar quando voltar — e adivinhar em loop.
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar))
            contexto.HttpContext.Response.Headers.RetryAfter =
                ((int)esperar.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);

        contexto.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Rastreamento.Api.RateLimit")
            .LogWarning(
                "Requisicao barrada por rate limit em {Caminho}, origem {Ip}.",
                contexto.HttpContext.Request.Path,
                contexto.HttpContext.Connection.RemoteIpAddress);

        return ValueTask.CompletedTask;
    };
});
```

E, no pipeline, inserir `app.UseRateLimiter();` imediatamente antes de `app.UseAuthentication();`:

```csharp
var app = builder.Build();

// Antes da autenticacao: barrar o flood nao deve custar nem a validacao do token.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

- [ ] **Step 5: Aplicar a política ao endpoint de login**

Em `src/Rastreamento.Api/Controllers/AuthController.cs`, acrescentar aos `using`:

```csharp
using Microsoft.AspNetCore.RateLimiting;
using Rastreamento.Api.Configuration;
```

E o atributo na action `Login` (só o `[HttpPost]` ganha vizinho — o resto do método não muda):

```csharp
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitOptions.NomeDaPoliticaDeLogin)]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
```

- [ ] **Step 6: Configurar os defaults**

Em `src/Rastreamento.Api/appsettings.json`, acrescentar depois da seção `Lockout`:

```json
  "RateLimit": {
    "PermitLimit": 10,
    "WindowSeconds": 60
  }
```

Substituir o conteúdo de `src/Rastreamento.Api/appsettings.Development.json` por:

```json
{
  "Jwt": {
    "SigningKey": "chave-de-desenvolvimento-e-de-teste-nao-usar-em-producao-32bytes+"
  },
  "RateLimit": {
    "PermitLimit": 1000
  }
}
```

- [ ] **Step 7: Rodar os testes e confirmar que passam**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test tests/Rastreamento.Api.Tests
```

Expected: build em 0 warnings; PASS — os 2 testes de `RateLimitTests`, os 2 casos novos de
`ConfiguracaoDeStartupTests` e **toda** a `AuthEndpointsTests` (o limite folgado de dev é o que
mantém a suíte compartilhada fora do teto).

- [ ] **Step 8: Commit**

```bash
git add src/Rastreamento.Api/Configuration/RateLimitOptions.cs \
        src/Rastreamento.Api/Configuration/RateLimitOptionsValidator.cs \
        src/Rastreamento.Api/Program.cs \
        src/Rastreamento.Api/Controllers/AuthController.cs \
        src/Rastreamento.Api/appsettings.json \
        src/Rastreamento.Api/appsettings.Development.json \
        tests/Rastreamento.Api.Tests/RateLimitTests.cs \
        tests/Rastreamento.Api.Tests/ConfiguracaoDeStartupTests.cs
git commit -m "feat(auth): rate limit por IP no /auth/login com 429 e Retry-After"
```

---

## Task 7: Logging de desfecho de login e refresh no controller

Fecha o que resta da Seção D: o `AuthController` loga o desfecho grosso + IP (tem `HttpContext`);
os eventos de segurança específicos já saem dos casos de uso (Tasks 2 e 5) e o 429 sai do
`OnRejected` (Task 6).

**Files:**
- Modify: `src/Rastreamento.Api/Controllers/AuthController.cs`

**Interfaces:**
- Consumes: `ILogger<AuthController>` (injetado pelo DI padrão — não precisa de registro),
  `Result<LoginResult>`, `LoginResult.Usuario.NomeUsuario`.
- Produces: nenhuma API nova.

- [ ] **Step 1: Injetar o logger e logar os desfechos**

Em `src/Rastreamento.Api/Controllers/AuthController.cs`:

(a) substituir os campos e o construtor por:

```csharp
    private readonly IAutenticarUsuarioUseCase _autenticar;
    private readonly IRenovarTokenUseCase _renovar;
    private readonly IRevogarTokenUseCase _revogar;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAutenticarUsuarioUseCase autenticar,
        IRenovarTokenUseCase renovar,
        IRevogarTokenUseCase revogar,
        ILogger<AuthController> logger)
    {
        _autenticar = autenticar;
        _renovar = renovar;
        _revogar = revogar;
        _logger = logger;
    }
```

(b) substituir os corpos de `Login` e `Refresh` por:

```csharp
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitOptions.NomeDaPoliticaDeLogin)]
    public async Task<IActionResult> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        var resultado = await _autenticar.ExecutarAsync(
            new LoginRequest(body.NomeUsuario, body.Senha), ct);

        if (!resultado.Sucesso)
        {
            // O usuario TENTADO, nunca a senha. E o desfecho grosso: por que falhou (inexistente,
            // inativo, trancado, senha errada) fica de fora de proposito — o log espelha o 401
            // generico, e o evento de seguranca que importa (a trava) sai do caso de uso.
            _logger.LogWarning("Falha de login para {NomeUsuario}, origem {Ip}.",
                body.NomeUsuario, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { erro = resultado.Erro });
        }

        _logger.LogInformation("Login de {NomeUsuario}, origem {Ip}.",
            resultado.Valor!.Usuario.NomeUsuario, HttpContext.Connection.RemoteIpAddress);

        return Ok(EntregarSessao(resultado.Valor));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshPlano = Request.Cookies[NomeDoCookieDeRefresh] ?? string.Empty;
        var resultado = await _renovar.ExecutarAsync(refreshPlano, ct);

        if (!resultado.Sucesso)
        {
            // Sem o token (nem plano nem hash) na mensagem: e segredo, e o log nao e cofre.
            // Quando a causa for reuso, o RenovarTokenUseCase ja registrou o Warning especifico.
            _logger.LogWarning("Falha ao renovar sessao, origem {Ip}.",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { erro = resultado.Erro });
        }

        _logger.LogInformation("Sessao renovada para {NomeUsuario}, origem {Ip}.",
            resultado.Valor!.Usuario.NomeUsuario, HttpContext.Connection.RemoteIpAddress);

        return Ok(EntregarSessao(resultado.Valor));
    }
```

- [ ] **Step 2: Rodar a suíte inteira e confirmar que nada regrediu**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
```

Expected: build em 0 warnings; toda a suíte PASS.

- [ ] **Step 3: Conferir manualmente que o log sai e não vaza segredo**

```bash
dotnet run --project src/Rastreamento.Api
```

Em outro terminal:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5169/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"nomeUsuario":"admin","senha":"Admin@123"}'
curl -k -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:5169/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"nomeUsuario":"admin","senha":"senha-errada-de-proposito"}'
```

Expected: `200` e depois `401`; no console do `dotnet run`, uma linha `info` "Login de admin,
origem ..." e uma `warn` "Falha de login para admin, origem ...". **Conferir que nenhuma linha
contém `Admin@123`, `senha-errada-de-proposito`, o valor do cookie `refreshToken` ou o access
token.** Encerrar o `dotnet run` com Ctrl+C.

- [ ] **Step 4: Commit**

```bash
git add src/Rastreamento.Api/Controllers/AuthController.cs
git commit -m "feat(auth): logging de desfecho de login e refresh com IP de origem"
```

---

## Task 8: Documentação e verificação final

Deixa `CLAUDE.md` e a pasta `specs/` refletindo o que passou a existir, com a contagem de testes
que dependem do banco **medida**, não estimada.

**Files:**
- Modify: `CLAUDE.md`
- Modify: `specs/03-arquitetura-tecnica.md`
- Modify: `specs/05-api-endpoints.md`

**Interfaces:**
- Consumes: tudo das Tasks 1–7.
- Produces: nenhuma API.

- [ ] **Step 1: Medir a suíte completa**

```bash
docker compose up -d
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
```

Anotar o total de testes. Depois, medir quantos exigem banco, por projeto:

```bash
dotnet test tests/Rastreamento.Infrastructure.Tests --list-tests
dotnet test tests/Rastreamento.Api.Tests --list-tests
```

Os que exigem banco são: em `Infrastructure.Tests`, todos os de `Persistence/`; em `Api.Tests`,
todos exceto `HorarioDeBrasiliaJsonConverterTests` e `ConfiguracaoDeStartupTests`. Anotar os três
números (total geral, quantos exigem banco, e a quebra por projeto) — eles vão para o `CLAUDE.md`
no Step 2.

- [ ] **Step 2: Atualizar o `CLAUDE.md`**

(a) Na seção "Invariantes de negócio que não podem ser violadas", acrescentar ao final:

```markdown
- **Falha de autenticação é sempre genérica.** Login responde `"Usuário ou senha inválidos."` e
  refresh responde `"Refresh token inválido ou expirado."` — em **todos** os caminhos de falha,
  inclusive conta trancada (mesmo com a senha certa) e reuso de refresh token detectado. Variar
  corpo, status ou tipo de erro por condição vira oráculo de enumeração.
- **O BCrypt roda sempre no login**, inclusive para usuário inexistente, inativo ou trancado
  (`IPasswordHasher.HashFicticio`). Nenhum `return` antecipado antes da verificação de senha.
```

(b) Substituir a subseção "### Pré-requisito externo dos testes" pelo texto abaixo, preenchendo os
números medidos no Step 1 (marcados como `<N>`):

```markdown
### Pré-requisito externo dos testes

Parte da suíte roda contra o **SQL Server real**, não contra banco em memória — é o que
prova o mapeamento EF, os lifetimes do DI, a atomicidade da rotação de refresh token, a
queima da família de tokens no reuso e o lockout de conta ponta a ponta.
Hoje são **<N> dos <TOTAL> testes** (<A> em `Infrastructure.Tests`, <B> em `Api.Tests`). Sem o
banco no ar eles falham com erro de conexão, não com mensagem útil.

```bash
docker compose up -d
# aplicar, uma vez, no banco `Rastreamento` de localhost:1433 (sa / Your_strong_Pass123):
#   specs/02-modelo-de-dados.sql   (schema — fonte de verdade)
#   db/seed.sql                    (perfis + usuário admin / Admin@123)
```

Num banco que já existia antes do hardening de auth, as colunas de lockout entram por `ALTER`
(o `.sql` é script de criação). É idempotente:

```bash
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Usuario','FalhasConsecutivas') IS NULL ALTER TABLE dbo.Usuario ADD FalhasConsecutivas INT NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0), BloqueadoAte DATETIME2 NULL;"
```

O schema **não** é criado pelo EF (nada de `Add-Migration`/`EnsureCreated`): é Database
First, o `.sql` é a fonte de verdade.
```

(c) Substituir a seção "### Trade-off conhecido de autenticação" por:

```markdown
### Defesas de autenticação em vigor

- **Trabalho constante + 401 genérico** no login e no refresh (sem oráculo de timing nem de corpo).
- **Reuso de refresh token detectado:** reapresentar um token já rotacionado revoga **todos** os
  refresh tokens ativos daquele usuário e responde o mesmo 401 genérico. Limite inerente: só
  detecta quando o token **antigo** reaparece — se o atacante roubar o token atual e o legítimo
  nunca replayar o anterior, a defesa é a expiração natural do refresh.
- **Lockout de conta:** `Lockout:MaxFalhas` (5) falhas consecutivas trancam por
  `Lockout:DuracaoMinutos` (15). A trava expira sozinha — não existe lockout permanente que exija
  admin, e por isso o lockout-DoS fica limitado a atrasar o operador por esse tempo. Concorrência:
  duas tentativas simultâneas podem competir pelo contador (off-by-one no pior caso), aceito no MVP.
- **Rate limit por IP no `/auth/login`:** `RateLimit:PermitLimit` (10) por
  `RateLimit:WindowSeconds` (60), janela fixa, 429 com `Retry-After`. O `/auth/refresh` fica de
  fora de propósito. Em `appsettings.Development.json` o limite é folgado — no `TestServer` o IP é
  nulo e toda a suíte cairia numa partição só.
- **Logging de auth via `ILogger`** (não há tabela de auditoria persistente): login ok/falha e
  refresh ok/falha com IP no `AuthController`; trava de conta e reuso de refresh nos casos de uso;
  429 no `OnRejected`. **Nunca** se loga senha, refresh token (plano ou hash) nem access token.

### Trade-offs conhecidos de autenticação

**Logout não invalida o access token.** O logout revoga o refresh token no banco, mas o
access token é JWT stateless e o `/me` responde só a partir das claims, sem ida ao banco.
Ou seja: depois do logout — ou de desativar um usuário, ou de a família de tokens ser queimada por
reuso — a sessão ainda funciona até o access token expirar (`Jwt:AccessTokenMinutes`, hoje 15 min).
É o comportamento padrão de JWT stateless e está aceito no MVP; se um dia precisar ser imediato, a
saída é uma denylist de tokens ou validação por requisição, não um remendo no logout.

**Rate limit atrás de proxy reverso.** A partição usa `RemoteIpAddress`. Se entrar um proxy na
frente da API, configurar `ForwardedHeaders` — senão todos os clientes compartilham o IP do proxy e
o limite vira global por acidente. Flag de deploy, ainda não necessária (deploy manual, sem proxy).

**Ainda em aberto (deferido de propósito):** tabela de auditoria persistente; limpeza de linhas
`RefreshToken` expiradas; `SigningKey` como segredo de ambiente e `UseHttpsRedirection`; mensagem
dedicada de 429 no front (hoje cai no erro genérico de auth — só dispara sob abuso).
```

- [ ] **Step 3: Atualizar as specs**

Em `specs/03-arquitetura-tecnica.md`, na seção "Autenticação e Autorização", acrescentar ao final
da lista:

```markdown
- **Hardening da Fase 0 (implementado):** detecção de reuso de refresh token (reapresentar um token
  já rotacionado revoga toda a família de tokens ativos do usuário); lockout temporário de conta
  (`Usuario.FalhasConsecutivas` / `Usuario.BloqueadoAte`, configurável em `Lockout`); rate limit
  por IP no `/auth/login` (middleware nativo do ASP.NET Core, configurável em `RateLimit`); logging
  dos eventos de auth via `ILogger`. Todas as falhas continuam com resposta única e genérica —
  nenhuma dessas defesas pode ser observada pelo corpo ou pelo status da resposta.
```

Em `specs/05-api-endpoints.md`, substituir a linha do login por:

```markdown
- `POST /auth/login` — usuário/senha → retorna JWT. Falha sempre em **401** genérico (usuário
  inexistente, inativo, senha errada e conta trancada são indistinguíveis). Excesso de tentativas
  do mesmo IP → **429** com `Retry-After`.
```

- [ ] **Step 4: Verificação final**

```bash
dotnet build Rastreamento.slnx -warnaserror
dotnet test Rastreamento.slnx
git status --short
```

Expected: build em 0 warnings; suíte inteira PASS; `git status` sem arquivo não rastreado que
devesse ter entrado num commit.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md specs/03-arquitetura-tecnica.md specs/05-api-endpoints.md
git commit -m "docs: defesas de auth (reuso, lockout, rate limit, logging) no CLAUDE.md e specs"
```
