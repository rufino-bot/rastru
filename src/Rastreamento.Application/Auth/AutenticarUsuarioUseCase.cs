using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class AutenticarUsuarioUseCase : IAutenticarUsuarioUseCase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmissorDeSessao _emissor;

    public AutenticarUsuarioUseCase(
        IUsuarioRepository usuarios,
        IPasswordHasher passwordHasher,
        IEmissorDeSessao emissor)
    {
        _usuarios = usuarios;
        _passwordHasher = passwordHasher;
        _emissor = emissor;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorNomeUsuarioAsync(req.NomeUsuario, ct);

        // Trabalho constante: o BCrypt roda SEMPRE, inclusive quando o usuario nao existe ou
        // esta inativo. Com o curto-circuito do `||` a verificacao era pulada nesses casos e a
        // resposta voltava ~100ms antes da de "senha errada" — corpo identico, tempo diferente.
        var hashDeReferencia = usuario is not null && usuario.Ativo
            ? usuario.SenhaHash
            : _passwordHasher.HashFicticio;
        var senhaConfere = _passwordHasher.Verificar(req.Senha, hashDeReferencia);

        // Falha unica e generica: usuario inexistente, inativo e senha errada sao
        // indistinguiveis para quem chama (evita enumeracao de usuarios).
        if (usuario is null || !usuario.Ativo || !senhaConfere)
            return Result<LoginResult>.Falha("Usuário ou senha inválidos.");

        var sessao = await _emissor.EmitirAsync(usuario, ct);
        return Result<LoginResult>.Ok(sessao);
    }
}
