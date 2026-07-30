using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Material, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa o que criou — UQ_Material_Codigo nao perdoa sobra de execucao anterior.
/// </summary>
public class MateriaisEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _codigosCriados = [];

    public MateriaisEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        db.Materiais.RemoveRange(
            await db.Materiais.Where(m => _codigosCriados.Contains(m.Codigo)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private string CodigoUnico()
    {
        var codigo = $"mat-{Guid.NewGuid():N}";
        _codigosCriados.Add(codigo);
        return codigo;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
        return cliente;
    }

    private static object CorpoValido(string codigo) =>
        new { codigo, descricao = "Chapa de aco 3mm", unidadeMedida = "KG" };

    [Fact]
    public async Task Administrador_cadastra_material()
    {
        var resposta = await ClienteComo("Administrador")
            .PostAsJsonAsync("/materiais", CorpoValido(CodigoUnico()));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/materiais")]
    [InlineData("PUT", "/materiais/999999")]
    [InlineData("PATCH", "/materiais/999999/ativo")]
    public async Task Almoxarifado_nao_escreve_em_material(string metodo, string rota)
    {
        // Almoxarifado e o perfil que MAIS mexe com material no dia a dia e mesmo assim nao escreve
        // no catalogo: os tres verbos sao [Authorize(Roles = "Administrador")]. Cobrir os tres, e
        // nao so o POST, e o que impede apagar o atributo do PUT ou do PATCH em silencio.
        // O [Authorize(Roles)] roda antes do model binding e da action, entao um id inexistente
        // (999999) ainda responde 403 aqui, nunca 404 — e por isso o Theory nao precisa cadastrar
        // Material nenhum. Codigo literal fixo (nao CodigoUnico()): como nada e criado num 403,
        // registrar o codigo na lista de limpeza do DisposeAsync seria inofensivo mas deselegante.
        object corpo = metodo == "PATCH"
            ? new { ativo = false }
            : new { codigo = "almoxarifado-nao-pode", descricao = "Chapa", unidadeMedida = "KG" };
        var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota)
        {
            Content = JsonContent.Create(corpo)
        };

        var resposta = await ClienteComo("Almoxarifado").SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Almoxarifado_le_a_lista_de_materiais()
    {
        var resposta = await ClienteComo("Almoxarifado").GetAsync("/materiais");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/materiais");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Codigo_duplicado_inativo_responde_409_indicando_reativacao()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/materiais/{id}/ativo", new { ativo = false });

        var resposta = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
        Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
        Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
    }

    [Fact]
    public async Task Material_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        await cliente.PatchAsJsonAsync($"/materiais/{id}/ativo", new { ativo = false });

        var padrao = await cliente.GetStringAsync("/materiais");
        var comInativos = await cliente.GetStringAsync("/materiais?incluirInativos=true");

        Assert.DoesNotContain(codigo, padrao);
        Assert.Contains(codigo, comInativos);
    }

    [Fact]
    public async Task Editar_altera_descricao_e_unidade()
    {
        var cliente = ClienteComo("Administrador");
        var codigo = CodigoUnico();
        var criado = await cliente.PostAsJsonAsync("/materiais", CorpoValido(codigo));
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var resposta = await cliente.PutAsJsonAsync(
            $"/materiais/{id}", new { codigo, descricao = "Chapa de aco 5mm", unidadeMedida = "UN" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Chapa de aco 5mm", corpo.GetProperty("descricao").GetString());
        Assert.Equal("UN", corpo.GetProperty("unidadeMedida").GetString());
    }

    [Fact]
    public async Task Unidade_de_medida_em_branco_responde_400()
    {
        var resposta = await ClienteComo("Administrador").PostAsJsonAsync(
            "/materiais", new { codigo = CodigoUnico(), descricao = "Chapa", unidadeMedida = " " });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
