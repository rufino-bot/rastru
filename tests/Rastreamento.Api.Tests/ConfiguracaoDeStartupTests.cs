using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guarda de regressao da validacao de configuracao no startup. Sem ela, um deploy que esqueca de
/// sobrescrever o <c>appsettings.json</c> sobe limpo e assina tokens com a chave que esta publica
/// no repositorio — sem erro, sem log, sem sintoma ate alguem forjar um token. O comportamento
/// correto e a aplicacao nao subir.
/// </summary>
public class ConfiguracaoDeStartupTests
{
    [Theory]
    [InlineData("")]
    [InlineData("curta-demais")]
    [InlineData(JwtOptions.SigningKeyPlaceholder)]
    // A chave de exemplo com whitespace no fim passaria pela comparacao exata E pelo minimo
    // de bytes, e a aplicacao subiria com uma chave publica no repositorio.
    [InlineData(JwtOptions.SigningKeyPlaceholder + "\n")]
    [InlineData("  " + JwtOptions.SigningKeyPlaceholder + "  ")]
    public void Aplicacao_nao_sobe_com_SigningKey_invalida(string signingKey)
    {
        var excecao = Record.Exception(() => SubirApi(new() { ["Jwt:SigningKey"] = signingKey }));

        var validacao = Assert.IsType<OptionsValidationException>(excecao);
        Assert.Contains(nameof(JwtOptions.SigningKey), validacao.Message);
    }

    [Theory]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    public void Aplicacao_nao_sobe_sem_issuer_ou_audience(string chave)
    {
        var excecao = Record.Exception(() => SubirApi(new() { [chave] = "" }));

        Assert.IsType<OptionsValidationException>(excecao);
    }

    [Theory]
    [InlineData("Jwt:AccessTokenMinutes")]
    [InlineData("Jwt:RefreshTokenDays")]
    public void Aplicacao_nao_sobe_com_tempo_de_vida_nao_positivo(string chave)
    {
        var excecao = Record.Exception(() => SubirApi(new() { [chave] = "0" }));

        Assert.IsType<OptionsValidationException>(excecao);
    }

    [Fact]
    public void Aplicacao_sobe_com_a_configuracao_do_repositorio()
    {
        // Contraprova dos testes acima: a configuracao de desenvolvimento e valida, entao a falha
        // nos outros casos vem da chave trocada, e nao de a API nao subir de qualquer jeito.
        var excecao = Record.Exception(() => SubirApi(new()));

        Assert.Null(excecao);
    }

    /// <summary>
    /// Sobe a API de verdade com <paramref name="configuracao"/> sobrescrevendo o
    /// <c>appsettings</c>. <c>AddInMemoryCollection</c> entra por ultimo na cadeia, entao vence os
    /// arquivos; <c>CreateClient</c> e o que efetivamente inicia o host (e dispara o
    /// <c>ValidateOnStart</c>).
    /// </summary>
    private static void SubirApi(Dictionary<string, string?> configuracao)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureAppConfiguration(
                c => c.AddInMemoryCollection(configuracao)));
        using var cliente = factory.CreateClient();
    }
}
