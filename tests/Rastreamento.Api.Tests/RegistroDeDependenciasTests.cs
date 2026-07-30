using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Application.Auth;
using Rastreamento.Application.Cadastros;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guarda de regressao para os lifetimes do DI. A rotacao do refresh token muta o token antigo
/// e conta com o change tracking do EF para que um unico <c>SaveChanges</c> cubra a revogacao do
/// antigo e a insercao do novo. Isso so vale se caso de uso, emissor e repositorio compartilharem
/// a mesma instancia de <see cref="RastreamentoDbContext"/> — ou seja, se tudo for
/// <c>Scoped</c>. Com <c>Transient</c> a revogacao se perderia em silencio.
/// <para>
/// Os cadastros da Fase 1A entram pelo mesmo motivo: <c>Editar</c> e <c>DefinirAtivo</c> mutam a
/// entidade que o repositorio carregou e dependem de <c>SalvarAlteracoesAsync</c> enxergar a
/// mudanca no MESMO <see cref="RastreamentoDbContext"/>. Cada entidade nova acrescenta aqui o par
/// repositorio + caso de uso.
/// </para>
/// </summary>
public class RegistroDeDependenciasTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RegistroDeDependenciasTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData(typeof(RastreamentoDbContext))]
    [InlineData(typeof(IUsuarioRepository))]
    [InlineData(typeof(IRefreshTokenRepository))]
    [InlineData(typeof(IEmissorDeSessao))]
    [InlineData(typeof(IAutenticarUsuarioUseCase))]
    [InlineData(typeof(IRenovarTokenUseCase))]
    [InlineData(typeof(IRevogarTokenUseCase))]
    [InlineData(typeof(ISetorRepository))]
    [InlineData(typeof(CadastroDeSetorUseCase))]
    [InlineData(typeof(IMaterialRepository))]
    [InlineData(typeof(CadastroDeMaterialUseCase))]
    public void Servico_e_registrado_como_Scoped(Type servico)
    {
        using var escopo = _factory.Services.CreateScope();
        using var outroEscopo = _factory.Services.CreateScope();

        var instancia = escopo.ServiceProvider.GetRequiredService(servico);
        var mesmaInstancia = escopo.ServiceProvider.GetRequiredService(servico);
        var deOutroEscopo = outroEscopo.ServiceProvider.GetRequiredService(servico);

        Assert.Same(instancia, mesmaInstancia); // Transient falharia aqui
        Assert.NotSame(instancia, deOutroEscopo); // Singleton falharia aqui
    }
}
