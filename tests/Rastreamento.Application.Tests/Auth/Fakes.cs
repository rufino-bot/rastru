using Microsoft.Extensions.Logging;
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
}

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

public static class FakeJwtOptions
{
    public static IOptions<JwtOptions> Instance =>
        Options.Create(new JwtOptions { AccessTokenMinutes = 15, RefreshTokenDays = 7 });
}

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
