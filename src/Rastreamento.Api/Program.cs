using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Api.Serialization;
using Rastreamento.Application.Auth;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Infrastructure.Persistence;
using Rastreamento.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// A conversao de fuso e do serializador, e nao de cada endpoint: registrada uma vez aqui, nenhuma
// resposta futura pode esquecer de converter e devolver UTC rotulado como local.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new HorarioDeBrasiliaJsonConverter()));

// ValidateOnStart: configuracao de JWT invalida derruba a aplicacao no startup, em vez de deixar
// a API subir limpa assinando tokens com a chave de exemplo que esta commitada (ver
// JwtOptionsValidator).
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateOnStart();

builder.Services.AddDbContext<RastreamentoDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Rastreamento")));

// Tudo Scoped, e nao Transient, de proposito: a rotacao do refresh token muta o token antigo e
// conta com o change tracking do EF para revoga-lo no mesmo SaveChanges que insere o novo. Isso
// exige que caso de uso, emissor e repositorio compartilhem o mesmo DbContext da requisicao.
// Quem sustenta a atomicidade e o DbContext (Scoped por padrao no AddDbContext acima); manter os
// repositorios Scoped e o que garante que continue assim se o registro do contexto mudar.
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenHasher, Sha256TokenHasher>();
builder.Services.AddScoped<IAccessTokenGenerator, JwtAccessTokenGenerator>();
builder.Services.AddScoped<IEmissorDeSessao, EmissorDeSessao>();
builder.Services.AddScoped<IAutenticarUsuarioUseCase, AutenticarUsuarioUseCase>();
builder.Services.AddScoped<IRenovarTokenUseCase, RenovarTokenUseCase>();
builder.Services.AddScoped<IRevogarTokenUseCase, RevogarTokenUseCase>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// A validacao do bearer le o MESMO JwtOptions do resto da aplicacao (um unico bind da secao
// "Jwt"): um segundo `Get<JwtOptions>()` aqui leria a configuracao por fora do IOptions e
// escaparia do JwtOptionsValidator.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        // Sem o mapeamento legado de claims: `sub`, `role` e afins chegam com o nome que o
        // JwtAccessTokenGenerator emitiu, e nao traduzidos para as URIs do WS-Federation.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RoleClaimType = "role",
            NameClaimType = "unique_name",
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Torna a classe gerada pelos top-level statements visivel para WebApplicationFactory<Program>.
public partial class Program { }
