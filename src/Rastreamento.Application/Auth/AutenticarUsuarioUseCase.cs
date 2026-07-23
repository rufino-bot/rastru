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

        // Falha unica e generica: usuario inexistente, inativo e senha errada sao
        // indistinguiveis para quem chama (evita enumeracao de usuarios).
        if (usuario is null || !usuario.Ativo || !_passwordHasher.Verificar(req.Senha, usuario.SenhaHash))
            return Result<LoginResult>.Falha("Usuário ou senha inválidos.");

        var sessao = await _emissor.EmitirAsync(usuario, ct);
        return Result<LoginResult>.Ok(sessao);
    }
}
