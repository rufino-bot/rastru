using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// `GET /panorama` (secao 5.1 da spec do Panorama) no cenario da Fase 3. A Peca A (10, Solda -> Pintura) tem os Itens
/// B (Corte) e C (Dobra) e, dentro de B, o neto "Neto" (Pintura): so o neto e o C sao iniciados, em dois Setores e em
/// dois niveis. Uma Peca D (6, Corte -> Dobra) e dividida em dois passos, uma Peca E (2) e expedida inteira e o
/// Pedido tem um Retrabalho avulso, aberto antes de o Pedido ser pausado. A lista e global (todo Pedido nao encerrado
/// do banco compartilhado), entao toda asserção e escopada nos dois Pedidos do cenario.
/// </summary>
public class PanoramaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public PanoramaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task<int> PecaNovaAsync(CenarioDaFase3NaApi c, decimal quantidade, params int[] passos)
  {
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos }));
    return peca;
  }

  private static async Task IniciarAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync(
          $"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));

  private static async Task TerminarAsync(CenarioDaFase3NaApi c, int no, int setor, int ordem, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync(
          $"/api/estrutura/{no}/terminos", new { setorId = setor, ordem, quantidade }));

  private static async Task EntregarAsync(CenarioDaFase3NaApi c, int no, int setor, int ordem, decimal quantidade) =>
      await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
      {
        itens = new[]
        {
          new
          {
            estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)ordem },
            destinoSetorId = (int?)null, quantidade,
          },
        },
      }));

  [Fact]
  public async Task Get_panorama_devolve_a_forma_com_peca_dividida_pausada_retrabalho_e_itens_em()
  {
    await using var c = await CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);

    // Dois niveis e dois Setores: o neto de B na Pintura e o C na Dobra. A e B continuam a iniciar.
    var neto = (await CorpoAsync(await pcp.PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)null, descricao = "Neto", quantidade = 40m, quantidadePorPai = 2m }))).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{neto}/roteiro", new { passos = new[] { c.Pintura } }));
    await IniciarAsync(c, neto, c.Pintura, 4m);
    await IniciarAsync(c, c.C, c.Dobra, 2m);

    // D dividida: 6 iniciadas no Corte, 3 terminadas e 2 entregues ao passo da Dobra.
    var d = await PecaNovaAsync(c, 6m, c.Corte, c.Dobra);
    await IniciarAsync(c, d, c.Corte, 6m);
    await TerminarAsync(c, d, c.Corte, 1, 3m);
    await EntregarAsync(c, d, c.Corte, 1, 2m);

    // E expedida inteira: nao aparece.
    var e = await PecaNovaAsync(c, 2m, c.Corte);
    await IniciarAsync(c, e, c.Corte, 2m);
    await TerminarAsync(c, e, c.Corte, 1, 2m);
    await EntregarAsync(c, e, c.Corte, 1, 2m);
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = e, quantidade = 2m } } }));

    // O Retrabalho avulso de A, e so depois a pausa do Pedido original.
    var numero = (await CorpoAsync(await pcp.GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString();
    var retrabalho = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos", new
    {
      estruturaItemId = c.A, quantidade = 3m, numero = $"{numero}-R1", dataEntrega = SemAtraso, motivoRetrabalho = "ErroInterno",
    });
    await Garantir(retrabalho);
    var retrabalhoId = (await CorpoAsync(retrabalho)).GetProperty("id").GetInt32();
    await Garantir(await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "Alteração de projeto" }));

    var resposta = await c.Como(c.Qualidade).GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var pecas = (await CorpoAsync(resposta)).GetProperty("pecas").EnumerateArray()
        .Where(p => p.GetProperty("pedido").GetProperty("id").GetInt32() is var id && (id == c.PedidoId || id == retrabalhoId))
        .ToList();
    // Peca A e D do original (B, C, o neto e a E expedida nao entram) e a copia de A no Retrabalho, em C9: o prazo
    // (os dois de 2099-12-31), o numero do Pedido (o original vem antes do "-R1"), o codigo e o Id.
    Assert.Equal(new[] { c.A, d }, pecas.Where(p => p.GetProperty("pedido").GetProperty("id").GetInt32() == c.PedidoId)
        .Select(p => p.GetProperty("estruturaItemId").GetInt32()));
    var copia = Assert.Single(pecas, p => p.GetProperty("pedido").GetProperty("id").GetInt32() == retrabalhoId);

    var a = pecas.Single(p => p.GetProperty("estruturaItemId").GetInt32() == c.A);
    Assert.Equal(c.AgrupamentoId, a.GetProperty("agrupamentoId").GetInt32());
    Assert.Equal(10m, a.GetProperty("quantidade").GetDecimal());
    var pedidoDeA = a.GetProperty("pedido");
    Assert.Equal((numero, SemAtraso, false, false), (
        pedidoDeA.GetProperty("numero").GetString(), pedidoDeA.GetProperty("dataEntrega").GetString(),
        pedidoDeA.GetProperty("atrasado").GetBoolean(), pedidoDeA.GetProperty("retrabalho").GetBoolean()));
    Assert.Equal("Alteração de projeto", pedidoDeA.GetProperty("pausa").GetProperty("motivo").GetString());
    var componente = a.GetProperty("componente");
    Assert.Equal((c.ComponenteId, "Chassi da Fase 3", JsonValueKind.Null), (
        componente.GetProperty("id").GetInt32(), componente.GetProperty("descricao").GetString(),
        componente.GetProperty("miniaturaVersao").ValueKind));
    Assert.Equal(new[] { c.Dobra, c.Pintura }, a.GetProperty("itensEm").EnumerateArray().Select(s => s.GetProperty("setorId").GetInt32()));
    Assert.All(a.GetProperty("itensEm").EnumerateArray(), s => Assert.Contains("f3-", s.GetProperty("nome").GetString()));
    var segmentosDeA = a.GetProperty("distribuicao").EnumerateArray().ToList();
    Assert.Equal(("AIniciar", 10m), (segmentosDeA.Single().GetProperty("tipo").GetString(), segmentosDeA.Single().GetProperty("quantidade").GetDecimal()));
    Assert.Equal(JsonValueKind.Null, segmentosDeA.Single().GetProperty("setorId").ValueKind);
    Assert.Equal(("SemRelatorio", false), (
        a.GetProperty("afericao").GetProperty("situacao").GetString(), a.GetProperty("afericao").GetProperty("exigeRelatorio").GetBoolean()));
    var embalagem = a.GetProperty("embalagem");
    Assert.Equal((false, false, 10m, JsonValueKind.Null), (
        embalagem.GetProperty("vale").GetBoolean(), embalagem.GetProperty("podeMarcar").GetBoolean(),
        embalagem.GetProperty("emProducao").GetDecimal(), embalagem.GetProperty("ultima").ValueKind));

    // A copia no Retrabalho: marcada, sem pausa (a pausa e do Pedido original), ninguem a comecou.
    Assert.True(copia.GetProperty("pedido").GetProperty("retrabalho").GetBoolean());
    Assert.Equal(JsonValueKind.Null, copia.GetProperty("pedido").GetProperty("pausa").ValueKind);
    Assert.Equal(3m, copia.GetProperty("quantidade").GetDecimal());

    // D: um segmento por passo, na ordem do Roteiro, com o aguardando coleta dentro do passo.
    var segmentosDeD = pecas.Single(p => p.GetProperty("estruturaItemId").GetInt32() == d)
        .GetProperty("distribuicao").EnumerateArray()
        .Select(s => (
            s.GetProperty("tipo").GetString()!, s.GetProperty("setorId").GetInt32(), s.GetProperty("ordem").GetInt32(),
            s.GetProperty("quantidade").GetDecimal(), s.GetProperty("aguardandoColeta").GetDecimal()))
        .ToArray();
    Assert.Equal(new[] { ("Passo", c.Corte, 1, 4m, 1m), ("Passo", c.Dobra, 2, 2m, 0m) }, segmentosDeD);
  }

  [Theory]
  [InlineData("Operador")]
  [InlineData("Movimentador")]
  [InlineData("Separador")]
  [InlineData("Qualidade")]
  [InlineData("Gestao")]
  [InlineData("Pcp")]
  public async Task Get_panorama_e_de_qualquer_perfil(string perfil)
  {
    await using var c = await CriarAsync(_factory);
    var quem = perfil switch
    {
      "Operador" => c.Operador, "Movimentador" => c.Movimentador, "Separador" => c.Separador,
      "Qualidade" => c.Qualidade, "Gestao" => c.Gestao, _ => c.Pcp,
    };

    var resposta = await c.Como(quem).GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Fact]
  public async Task Get_panorama_sem_login_e_401()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/panorama");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }
}
