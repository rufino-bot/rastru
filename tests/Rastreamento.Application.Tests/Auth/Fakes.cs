using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Auth;

public class FakePasswordHasher : IPasswordHasher
{
    /// <summary>Quantas vezes <see cref="Verificar"/> foi chamado — permite provar que o
    /// caminho de miss do login gasta o mesmo trabalho que o caminho de sucesso.</summary>
    public int Verificacoes { get; private set; }

    /// <summary>Contra qual hash foi a ultima verificacao.</summary>
    public string? UltimoHashVerificado { get; private set; }

    public string HashFicticio => "hash:<ficticio>";

    public string Hash(string senhaPlano) => "hash:" + senhaPlano;

    public bool Verificar(string senhaPlano, string senhaHash)
    {
        Verificacoes++;
        UltimoHashVerificado = senhaHash;
        return senhaHash == "hash:" + senhaPlano;
    }
}

public class FakeTokenHasher : ITokenHasher
{
    public string Hash(string tokenPlano) => "sha:" + tokenPlano;
}

public class FakeAccessTokenGenerator : IAccessTokenGenerator
{
    public (string token, DateTime expiraEm) Gerar(Usuario usuario) =>
        ("access-" + usuario.NomeUsuario, DateTime.UtcNow.AddMinutes(15));
}

public class FakeUsuarioRepo : IUsuarioRepository
{
    private readonly Usuario? _usuario;

    public FakeUsuarioRepo(Usuario? usuario) => _usuario = usuario;

    public Task<Usuario?> ObterPorNomeUsuarioAsync(string nomeUsuario, CancellationToken ct) =>
        Task.FromResult(_usuario is not null && _usuario.NomeUsuario == nomeUsuario ? _usuario : null);

    public Task<Usuario?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_usuario is not null && _usuario.Id == id ? _usuario : null);
}

public class FakeRefreshTokenRepo : IRefreshTokenRepository
{
    public List<RefreshToken> Adicionados { get; } = new();
    public RefreshToken? Ativo { get; set; }

    /// <summary>Quantos commits o repositorio recebeu — permite provar "um unico save".</summary>
    public int Saves { get; private set; }

    public Task AdicionarAsync(RefreshToken token, CancellationToken ct)
    {
        Adicionados.Add(token);
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> ObterAtivoPorHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(Ativo is not null && Ativo.TokenHash == tokenHash && Ativo.RevogadoEm is null ? Ativo : null);

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

public static class FakeJwtOptions
{
    public static IOptions<JwtOptions> Instance =>
        Options.Create(new JwtOptions { AccessTokenMinutes = 15, RefreshTokenDays = 7 });
}
