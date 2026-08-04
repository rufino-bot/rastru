using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Auth;

public class EmissorDeSessao : IEmissorDeSessao
{
  private readonly IRefreshTokenRepository _refreshTokens;
  private readonly ITokenHasher _tokenHasher;
  private readonly IAccessTokenGenerator _accessTokens;
  private readonly JwtOptions _jwt;

  public EmissorDeSessao(
      IRefreshTokenRepository refreshTokens,
      ITokenHasher tokenHasher,
      IAccessTokenGenerator accessTokens,
      IOptions<JwtOptions> jwt)
  {
    _refreshTokens = refreshTokens;
    _tokenHasher = tokenHasher;
    _accessTokens = accessTokens;
    _jwt = jwt.Value;

    // Configuracao invalida falha aqui (resolucao do DI, no startup) e nao la na frente,
    // com uma excecao crua de SQL ao violar CK_RefreshToken_ExpiraAposCriado.
    if (_jwt.RefreshTokenDays <= 0)
      throw new ArgumentOutOfRangeException(
          nameof(jwt), _jwt.RefreshTokenDays,
          $"{nameof(JwtOptions.RefreshTokenDays)} deve ser maior que zero.");

    if (_jwt.AccessTokenMinutes <= 0)
      throw new ArgumentOutOfRangeException(
          nameof(jwt), _jwt.AccessTokenMinutes,
          $"{nameof(JwtOptions.AccessTokenMinutes)} deve ser maior que zero.");
  }

  public async Task<LoginResult> EmitirAsync(Usuario usuario, CancellationToken ct)
  {
    var (novo, sessao) = PrepararSessao(usuario);

    await _refreshTokens.AdicionarAsync(novo, ct);
    await _refreshTokens.SalvarAlteracoesAsync(ct);

    return sessao;
  }

  public async Task<LoginResult> RotacionarAsync(RefreshToken atual, CancellationToken ct)
  {
    var (novo, sessao) = PrepararSessao(atual.Usuario);

    // Reaproveita o hash do token recem-emitido em vez de re-hashear o texto plano:
    // um hash so, sem risco de as duas contas divergirem.
    atual.SubstituidoPorTokenHash = novo.TokenHash;
    // Revogado no mesmo instante em que o novo foi criado (sem segunda leitura de relogio).
    atual.RevogadoEm = novo.CriadoEm;

    await _refreshTokens.AdicionarAsync(novo, ct);
    // Um unico save: revogacao do antigo e emissao do novo commitam juntas.
    await _refreshTokens.SalvarAlteracoesAsync(ct);

    return sessao;
  }

  /// <summary>
  /// Monta o refresh token novo (ja com <c>TokenHash</c> calculado) e o <see cref="LoginResult"/>
  /// correspondente, sem persistir nada — quem salva e o metodo publico que chamou.
  /// </summary>
  private (RefreshToken novo, LoginResult sessao) PrepararSessao(Usuario usuario)
  {
    var (accessToken, accessExpira) = _accessTokens.Gerar(usuario);

    // Token opaco em base64url; devolvido em texto plano UMA unica vez e
    // persistido apenas como hash.
    var refreshPlano = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    var agora = DateTime.UtcNow;
    var refreshExpira = agora.AddDays(_jwt.RefreshTokenDays);

    var novo = new RefreshToken
    {
      UsuarioId = usuario.Id,
      TokenHash = _tokenHasher.Hash(refreshPlano),
      CriadoEm = agora,
      ExpiraEm = refreshExpira
    };

    var dto = new UsuarioDto(usuario.Id, usuario.NomeUsuario, usuario.NomeCompleto, usuario.Perfil.Nome);
    return (novo, new LoginResult(accessToken, accessExpira, refreshPlano, refreshExpira, dto));
  }
}
