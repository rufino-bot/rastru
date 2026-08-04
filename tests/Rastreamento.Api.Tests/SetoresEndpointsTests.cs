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
/// Ponta a ponta dos endpoints de Setor, contra o SQL Server real (docker compose up -d).
/// Cada teste limpa os Setores que criou — UQ_Setor_Nome nao perdoa sobra de execucao anterior.
/// </summary>
public class SetoresEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _nomesCriados = [];

  public SetoresEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    db.Setores.RemoveRange(await db.Setores.Where(s => _nomesCriados.Contains(s.Nome)).ToListAsync());
    await db.SaveChangesAsync();
  }

  private string NomeUnico()
  {
    var nome = $"setor-{Guid.NewGuid():N}";
    _nomesCriados.Add(nome);
    return nome;
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  [Fact]
  public async Task Administrador_cadastra_setor()
  {
    var resposta = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/setores", new { nome = NomeUnico() });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
  }

  [Theory]
  [InlineData("POST", "/api/setores")]
  [InlineData("PUT", "/api/setores/999999")]
  [InlineData("PATCH", "/api/setores/999999/ativo")]
  public async Task Operador_nao_escreve_em_setor(string metodo, string rota)
  {
    // O [Authorize(Roles = "Administrador")] roda antes do model binding e da action, entao
    // um id inexistente (999999) ainda responde 403 aqui, nunca 404 — e por isso o Theory nao
    // precisa cadastrar Setor nenhum para provar autorizacao: PUT e PATCH batem em rota que
    // nunca existiu e o 403 chega do mesmo jeito.
    // Nome literal fixo (nao NomeUnico()): como nada e criado num 403, registrar o nome na
    // lista de limpeza do DisposeAsync seria inofensivo mas deselegante.
    object corpo = metodo == "PATCH" ? new { ativo = false } : new { nome = "operador-nao-pode" };
    var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota)
    {
      Content = JsonContent.Create(corpo)
    };

    var resposta = await ClienteComo("Operador").SendAsync(requisicao);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Operador_le_a_lista_de_setores()
  {
    var resposta = await ClienteComo("Operador").GetAsync("/api/setores");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_a_lista()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/setores");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Nome_duplicado_ativo_responde_409_sem_reativacao()
  {
    var cliente = ClienteComo("Administrador");
    var nome = NomeUnico();
    await cliente.PostAsJsonAsync("/api/setores", new { nome });

    var resposta = await cliente.PostAsJsonAsync("/api/setores", new { nome });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
    Assert.Equal("nome", corpo.GetProperty("campo").GetString());
    Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
  }

  [Fact]
  public async Task Nome_duplicado_inativo_responde_409_indicando_reativacao()
  {
    var cliente = ClienteComo("Administrador");
    var nome = NomeUnico();
    var criado = await cliente.PostAsJsonAsync("/api/setores", new { nome });
    var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    await cliente.PatchAsJsonAsync($"/api/setores/{id}/ativo", new { ativo = false });

    var resposta = await cliente.PostAsJsonAsync("/api/setores", new { nome });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.True(corpo.GetProperty("existeInativo").GetBoolean());
    Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Setor_inativado_some_da_lista_padrao_e_volta_com_incluirInativos()
  {
    var cliente = ClienteComo("Administrador");
    var nome = NomeUnico();
    var criado = await cliente.PostAsJsonAsync("/api/setores", new { nome });
    var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    await cliente.PatchAsJsonAsync($"/api/setores/{id}/ativo", new { ativo = false });

    var padrao = await cliente.GetStringAsync("/api/setores");
    var comInativos = await cliente.GetStringAsync("/api/setores?incluirInativos=true");

    Assert.DoesNotContain(nome, padrao);
    Assert.Contains(nome, comInativos);
  }

  [Fact]
  public async Task Editar_setor_inexistente_responde_404()
  {
    var resposta = await ClienteComo("Administrador")
        .PutAsJsonAsync("/api/setores/999999", new { nome = NomeUnico() });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Nome_em_branco_responde_400()
  {
    var resposta = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/setores", new { nome = "   " });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Nome_maior_que_a_coluna_responde_400_e_nao_500()
  {
    // 101 caracteres contra o NVARCHAR(100) de UQ_Setor_Nome. Prova que o [MaxLength] de
    // NovoSetorDto pega ANTES de o insert estourar SqlException — e o unico teste que exercita
    // o atributo, e vale para o molde inteiro (Material, Pedido, Agrupamento o copiam).
    // Tambem e o teste que pegou o alvo errado do atributo: com `[property: MaxLength]` o MVC
    // recusa a validacao inteira e o POST responde 500 (ver o remarks de NovoSetorDto).
    var resposta = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/setores", new { nome = new string('x', 101) });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }
}
