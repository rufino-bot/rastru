using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

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
        IUsuarioRepository usuarios,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        ITokenHasher tokenHasher,
        IAccessTokenGenerator accessTokens,
        IOptions<JwtOptions> jwt)
    {
        _usuarios = usuarios;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _tokenHasher = tokenHasher;
        _accessTokens = accessTokens;
        _jwt = jwt.Value;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorNomeUsuarioAsync(req.NomeUsuario, ct);

        // Falha unica e generica: usuario inexistente, inativo e senha errada sao
        // indistinguiveis para quem chama (evita enumeracao de usuarios).
        if (usuario is null || !usuario.Ativo || !_passwordHasher.Verificar(req.Senha, usuario.SenhaHash))
            return Result<LoginResult>.Falha("Usuário ou senha inválidos.");

        var resultado = await EmitirSessaoAsync(usuario, ct);
        return Result<LoginResult>.Ok(resultado);
    }

    // Reutilizado pelo RenovarTokenUseCase (T7).
    internal async Task<LoginResult> EmitirSessaoAsync(Usuario usuario, CancellationToken ct)
    {
        var (accessToken, accessExpira) = _accessTokens.Gerar(usuario);

        // Token opaco em base64url; devolvido em texto plano UMA unica vez e
        // persistido apenas como hash.
        var refreshPlano = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var agora = DateTime.UtcNow;
        var refreshExpira = agora.AddDays(_jwt.RefreshTokenDays);

        await _refreshTokens.AdicionarAsync(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = _tokenHasher.Hash(refreshPlano),
            CriadoEm = agora,
            ExpiraEm = refreshExpira
        }, ct);
        await _refreshTokens.SalvarAlteracoesAsync(ct);

        var dto = new UsuarioDto(usuario.Id, usuario.NomeUsuario, usuario.NomeCompleto, usuario.Perfil.Nome);
        return new LoginResult(accessToken, accessExpira, refreshPlano, refreshExpira, dto);
    }
}
