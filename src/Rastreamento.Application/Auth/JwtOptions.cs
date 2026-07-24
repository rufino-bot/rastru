namespace Rastreamento.Application.Auth;

public class JwtOptions
{
    /// <summary>
    /// Valor de <see cref="SigningKey"/> que esta commitado no <c>appsettings.json</c>. Um deploy
    /// que esqueca de sobrescrever a configuracao assinaria tokens com uma chave publica no git —
    /// por isso <see cref="JwtOptionsValidator"/> recusa exatamente este valor no startup.
    /// </summary>
    public const string SigningKeyPlaceholder = "troque-esta-chave-por-uma-forte-de-32bytes-ou-mais";

    /// <summary>Minimo do HMAC-SHA256: a chave precisa ter pelo menos o tamanho do digest.</summary>
    public const int TamanhoMinimoDaSigningKeyEmBytes = 32;

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
