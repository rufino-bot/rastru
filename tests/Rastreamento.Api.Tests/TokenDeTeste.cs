using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Application.Auth;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Emite um access token valido para um perfil arbitrario, assinando com as MESMAS JwtOptions que
/// a API valida. Evita criar uma linha em Usuario por perfil so para testar `[Authorize(Roles)]`.
/// </summary>
public static class TokenDeTeste
{
    public static string Emitir(WebApplicationFactory<Program> factory, string perfil, int usuarioId = 1)
    {
        using var escopo = factory.Services.CreateScope();
        var jwt = escopo.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims:
            [
                new Claim("sub", usuarioId.ToString()),
                new Claim("unique_name", $"teste-{perfil}"),
                new Claim("nome_completo", $"Usuario de Teste {perfil}"),
                new Claim("role", perfil),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credenciais);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
