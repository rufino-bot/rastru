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
