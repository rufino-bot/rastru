using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Estrutura (Task 5 da Fase 2), contra o SQL Server real
/// (docker compose up -d). Mesmo molde de <see cref="AgrupamentosEndpointsTests"/> e
/// <see cref="ReceitaPadraoEndpointsTests"/>: cada teste cria os próprios Pedido/Agrupamento/
/// Componente e apaga tudo que criou no <see cref="DisposeAsync"/>.
///
/// <para>
/// O QUE SÓ SE PROVA AQUI (o caso de uso já tem cobertura própria em
/// <c>Rastreamento.Application.Tests/Estrutura/</c>): o <c>TipoDeErro</c> virando o STATUS certo,
/// autorização por perfil de verdade com requisição HTTP, o prefixo <c>/api</c>, e o CORPO do
/// erro — em especial que <c>Detalhe</c> (a frase que nomeia o caminho do ciclo) sobrevive até a
/// resposta, e não só o código.
/// </para>
/// </summary>
public class EstruturaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _numerosCriados = [];
  private readonly List<int> _componentesCriados = [];

  public EstruturaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
    var pedidoIds = pedidos.Select(p => p.Id).ToList();

    // EstruturaMaterial/EstruturaRoteiro antes de EstruturaItem, e EstruturaItem antes de
    // Agrupamento/Componente/Pedido -- mesma ordem de FK de AgrupamentosEndpointsTests.
    var agrupamentoIds = await db.Agrupamentos
        .Where(a => pedidoIds.Contains(a.PedidoId)).Select(a => a.Id).ToListAsync();

    foreach (var agId in agrupamentoIds)
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId}");
    }

    // Receita de catalogo (FilhosPadrao) usada pelo teste de ciclo.
    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => _componentesCriados.Contains(f.ComponentePaiId)
                 || _componentesCriados.Contains(f.ComponenteFilhoId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => pedidoIds.Contains(a.PedidoId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(await db.Componentes.Where(c => _componentesCriados.Contains(c.Id)).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(pedidos);
    await db.SaveChangesAsync();
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private async Task<int> NovoComponente(string prefixo = "ES")
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"{prefixo}-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste da estrutura",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  private async Task<int> NovoPedido(HttpClient cliente)
  {
    var numero = $"ped-{Guid.NewGuid():N}"[..25];
    _numerosCriados.Add(numero);
    var resposta = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();
  }

  private async Task<int> NovoAgrupamento(HttpClient cliente, int pedidoId, string codigo = "AG-01")
  {
    var resposta = await cliente.PostAsJsonAsync(
        $"/api/pedidos/{pedidoId}/agrupamentos", new { codigo, tipo = "Kit" });
    return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();
  }

  /// <summary>Pedido + Agrupamento + Componente prontos, e o Id do Agrupamento -- base da maioria dos casos.</summary>
  private async Task<(int AgrupamentoId, int ComponenteId, int PedidoId)> NovoAgrupamentoComComponente(HttpClient cliente)
  {
    var pedidoId = await NovoPedido(cliente);
    var agrupamentoId = await NovoAgrupamento(cliente, pedidoId);
    var componenteId = await NovoComponente();
    return (agrupamentoId, componenteId, pedidoId);
  }

  private static object NovaPeca(int componenteId, decimal quantidade = 1m, bool requerRelatorio = false) =>
      new { componenteId, quantidade, requerRelatorioDimensional = requerRelatorio };

  // ---------------------------------------------------------------- criacao e leitura

  [Fact]
  public async Task POST_cria_a_Peca_e_devolve_201_com_a_arvore()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId, 3m, requerRelatorio: true));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(componenteId, corpo.GetProperty("componenteId").GetInt32());
    Assert.Equal("Peca", corpo.GetProperty("nivelHierarquico").GetString());
    Assert.True(corpo.GetProperty("requerRelatorioDimensional").GetBoolean());
    Assert.Empty(corpo.GetProperty("filhos").EnumerateArray());
  }

  [Fact]
  public async Task GET_devolve_a_arvore_aninhada_do_Agrupamento()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = "Sub-item ad-hoc", quantidade = 2m });

    var resposta = await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    var raiz = Assert.Single(corpo.EnumerateArray());
    Assert.Equal(raizId, raiz.GetProperty("id").GetInt32());
    var filho = Assert.Single(raiz.GetProperty("filhos").EnumerateArray());
    Assert.Equal("Sub-item ad-hoc", filho.GetProperty("descricao").GetString());
  }

  /// <summary>
  /// Afirma os DOIS campos do corpo: `erro` (o código estável) e `mensagem` (a frase que nomeia o
  /// caminho do ciclo). Um teste que afirmasse só o código deixaria a frase cair sem ninguém notar
  /// — é exatamente como este defeito nasceu no plano (ver o brief e o CLAUDE.md da Task 5).
  /// </summary>
  [Fact]
  public async Task POST_com_ciclo_na_receita_devolve_409_com_codigo_E_mensagem()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, _, _) = await NovoAgrupamentoComComponente(cliente);
    var a = await NovoComponente();
    var b = await NovoComponente();
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao { ComponentePaiId = a, ComponenteFilhoId = b, QuantidadePadrao = 1m });
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao { ComponentePaiId = b, ComponenteFilhoId = a, QuantidadePadrao = 1m });
      await db.SaveChangesAsync();
    }

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(a));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("CicloNaReceita", corpo.GetProperty("erro").GetString());
    Assert.Contains($"{a} -> {b} -> {a}", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task DELETE_em_Pedido_nao_Aberto_devolve_409_PedidoNaoAberto()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, pedidoId) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
      pedido.Status = "EmProducao";
      await db.SaveChangesAsync();
    }

    var resposta = await cliente.DeleteAsync($"/api/estrutura/{raizId}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("PedidoNaoAberto", corpo.GetProperty("erro").GetString());
  }

  [Fact]
  public async Task POST_de_filho_ad_hoc_sem_descricao_devolve_400()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = (string?)null, quantidade = 1m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- perfil

  /// <summary>
  /// Autentica como Operador. Id inexistente de propósito -- `[Authorize(Roles)]` roda antes do
  /// model binding e da action, então o 403 chega sem tocar no banco.
  /// </summary>
  [Fact]
  public async Task Perfil_sem_escrita_recebe_403_no_POST()
  {
    var resposta = await ClienteComo("Operador")
        .PostAsJsonAsync("/api/agrupamentos/999999/estrutura", NovaPeca(999999));

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  /// <summary>
  /// O par positivo do teste acima -- sem ele, um `[Authorize(Roles)]` posto na CLASSE inteira (em
  /// vez de só na ação de escrita) passaria no teste do 403 e quebraria a leitura para todo mundo
  /// sem ninguém notar. Precisa de Agrupamento real: 200 exige o caso de uso rodar até o fim.
  /// </summary>
  [Fact]
  public async Task Perfil_sem_escrita_recebe_200_no_GET()
  {
    var (agrupamentoId, _, _) = await NovoAgrupamentoComComponente(ClienteComo("PCP"));

    var resposta = await ClienteComo("Operador").GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }
}
