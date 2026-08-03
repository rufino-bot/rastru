using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Rastreamento.Api.Configuration;
using Rastreamento.Api.Serialization;
using Rastreamento.Application.Auth;
using Rastreamento.Application.Cadastros;
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

builder.Services.AddSingleton<IValidateOptions<LockoutOptions>, LockoutOptionsValidator>();
builder.Services.AddOptions<LockoutOptions>()
    .Bind(builder.Configuration.GetSection("Lockout"))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>();
builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection("RateLimit"))
    .ValidateOnStart();

// Teto grosso POR IP contra forca bruta de login. Complementa o lockout (trava fina, por conta):
// um barra o flood de uma origem, o outro protege a conta especifica; nenhum substitui o outro.
// Escopo deliberado: so o /auth/login. O /auth/refresh fica de fora — e legitimo e frequente, e o
// refresh token e opaco de 256 bits (forca bruta inviavel), entao throttlar so puniria o operador.
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiter.AddPolicy(RateLimitOptions.NomeDaPoliticaDeLogin, http =>
    {
        var politica = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // Particao por IP. Atras de proxy reverso isto vira o IP do proxy e o limite passa a ser
        // global — quando houver proxy, configurar ForwardedHeaders (anotado no CLAUDE.md).
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = politica.PermitLimit,
                Window = TimeSpan.FromSeconds(politica.WindowSeconds),
                // Sem fila: excedeu, recusa na hora. Enfileirar seguraria conexao a toa e daria ao
                // atacante um jeito de consumir recurso do servidor sem levar 429.
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    rateLimiter.OnRejected = (contexto, _) =>
    {
        // Sem Retry-After o cliente so pode adivinhar quando voltar — e adivinhar em loop.
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar))
            contexto.HttpContext.Response.Headers.RetryAfter =
                ((int)esperar.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);

        contexto.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Rastreamento.Api.RateLimit")
            .LogWarning(
                "Requisicao barrada por rate limit em {Caminho}, origem {Ip}.",
                contexto.HttpContext.Request.Path,
                contexto.HttpContext.Connection.RemoteIpAddress);

        return ValueTask.CompletedTask;
    };
});

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

// Cadastros (Fase 1A). Sem interface de use case de proposito: nada os substitui por fake — os
// testes de Application fakeiam o repositorio. Ver a decisao registrada no plano da Fase 1A.
builder.Services.AddScoped<ISetorRepository, SetorRepository>();
builder.Services.AddScoped<CadastroDeSetorUseCase>();
builder.Services.AddScoped<IMaterialRepository, MaterialRepository>();
builder.Services.AddScoped<CadastroDeMaterialUseCase>();
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<CadastroDePedidoUseCase>();
builder.Services.AddScoped<IAgrupamentoRepository, AgrupamentoRepository>();
builder.Services.AddScoped<CadastroDeAgrupamentoUseCase>();

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

// Prefixo /api. Existe por causa de uma COLISAO DE CAMINHO: as rotas do SPA (/setores,
// /materiais, /pedidos, /pedidos/:id) tem os mesmos caminhos dos endpoints da API. Em dev isso
// se manifestou como "401 ao dar F5" e foi contornado no vite.config.ts; em producao, com SPA e
// API na mesma origem, nao haveria Vite para interceptar. Mesma origem, alias, nao e escolha
// livre: o cookie de refresh e SameSite=Strict, que bloqueia cross-site.
//
// UsePathBase nao e branch: ele TIRA o prefixo quando existe e deixa passar quando nao existe.
// Entao a API responde nos DOIS caminhos (/api/setores e /setores) — de proposito, para o front
// migrar sem que os testes de endpoint, que ainda batem nos caminhos nus, precisem mudar junto.
// MEDIDO: /api/setores e /setores devolvem ambos 401 (rota casou, faltou token), nao 404.
//
// ISTO E ESTADO DE TRANSICAO, NAO DESTINO. Enquanto os caminhos nus responderem, a colisao de
// producao continua de pe — quem a fecha e remover o servico duplo, e o custo disso sao ~129
// URLs literais nos testes de endpoint. Passo seguinte, em commit proprio.
app.UsePathBase("/api");

// Antes da autenticacao: barrar o flood nao deve custar nem a validacao do token.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Torna a classe gerada pelos top-level statements visivel para WebApplicationFactory<Program>.
public partial class Program { }
