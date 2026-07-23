using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

public class JwtAccessTokenGeneratorTests
{
    private static JwtAccessTokenGenerator NovoGerador()
    {
        var opts = Options.Create(new JwtOptions
        {
            Issuer = "rastreamento-api",
            Audience = "rastreamento-web",
            SigningKey = "chave-de-teste-super-secreta-com-32b+",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });
        return new JwtAccessTokenGenerator(opts);
    }

    [Fact]
    public void Gera_token_com_claims_do_usuario()
    {
        var usuario = new Usuario
        {
            Id = 42, NomeUsuario = "admin", NomeCompleto = "Administrador do Sistema",
            Perfil = new Perfil { Nome = "Administrador" }
        };

        var (token, expiraEm) = NovoGerador().Gerar(usuario);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("admin", jwt.Claims.Single(c => c.Type == "unique_name").Value);
        Assert.Equal("Administrador", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.True(expiraEm > System.DateTime.UtcNow);
    }
}
