using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// A reclamacao do cliente (secoes 3.3 e 4.7 da spec da Fase 5B-2) no cenario da Fase 3: a Peca A do cenario sai, e
/// uma Peca nova, copiada do catalogo (sem descricao propria, "Chassi da Fase 3"), vai ao local de expedicao e e
/// expedida; ela e a unica Peca do Pedido, que assim conclui. Toda assercao escopada nessa Peca e nesse Pedido.
/// </summary>
public class ReclamacaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public ReclamacaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  /// <summary>A Peca nova, com tudo em `NaExpedicao`. Sai no `DisposeAsync` do cenario (pelo Agrupamento).</summary>
  private static async Task<int> UnicaPecaNaExpedicaoAsync(CenarioDaFase3NaApi c, decimal quantidade)
  {
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.DeleteAsync($"/api/estrutura/{c.A}"));
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));

    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/inicios", new { setorId = c.Corte, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/terminos", new { setorId = c.Corte, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade } },
    }));
    return peca;
  }

  private static async Task ExpedirAsync(CenarioDaFase3NaApi c, int peca, decimal quantidade) =>
      await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
          new { itens = new[] { new { estruturaItemId = peca, quantidade } } }));

  private static object Corpo(int peca, decimal quantidade, string? descricao = "A caixa chegou amassada.") =>
      new { estruturaItemId = peca, quantidade, descricao };

  private static Task<HttpResponseMessage> RegistrarAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, object corpo) =>
      c.Como(quem).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/reclamacoes", corpo);

  private async Task<int> LinhasDoLivroAsync(int peca)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Movimentacoes.CountAsync(m => m.EstruturaItemId == peca);
  }

  [Fact]
  public async Task Registrar_reclamacao_de_Peca_expedida_de_Pedido_concluido()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 6m);
    await ExpedirAsync(c, peca, 6m);
    var linhasAntes = await LinhasDoLivroAsync(peca);
    Assert.Equal("Concluido", (await CorpoAsync(await c.Como(c.Qualidade).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("status").GetString());

    var resposta = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 2m, "  A caixa chegou amassada.  "));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var r = await CorpoAsync(resposta);
    Assert.Equal(peca, r.GetProperty("pecaId").GetInt32());
    Assert.Equal("Chassi da Fase 3", r.GetProperty("descricao").GetString());
    Assert.StartsWith("f3-", r.GetProperty("codigo").GetString());
    Assert.Equal(2m, r.GetProperty("quantidade").GetDecimal());
    Assert.Equal("A caixa chegou amassada.", r.GetProperty("texto").GetString());
    Assert.EndsWith("-03:00", r.GetProperty("dataHora").GetString());
    Assert.Equal(c.Qualidade.Id, r.GetProperty("usuarioId").GetInt32());
    Assert.False(r.GetProperty("estornada").GetBoolean());
    Assert.True(r.GetProperty("estornavel").GetBoolean());
    Assert.Equal(JsonValueKind.Null, r.GetProperty("pedidoRetrabalho").ValueKind);
    Assert.Equal(JsonValueKind.Null, r.GetProperty("dispensa").ValueKind);
    Assert.Equal(2m, r.GetProperty("aRepor").GetDecimal());

    // Nao mexe no livro do original e nao reabre nada (C9).
    Assert.Equal(linhasAntes, await LinhasDoLivroAsync(peca));
    Assert.Equal("Concluido", (await CorpoAsync(await c.Como(c.Qualidade).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("status").GetString());

    // A lista traz a mais recente primeiro, para qualquer autenticado.
    await Garantir(await RegistrarAsync(c, c.Pcp, Corpo(peca, 1m, "Segunda.")));
    var lista = (await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}/reclamacoes"))).EnumerateArray().ToList();
    Assert.Equal(["Segunda.", "A caixa chegou amassada."], lista.Select(x => x.GetProperty("texto").GetString()).ToList());
    // Quem le e o Operador: nao e autor nem PCP, entao nao estorna.
    Assert.All(lista, x => Assert.False(x.GetProperty("estornavel").GetBoolean()));
    var doPcp = (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}/reclamacoes"))).EnumerateArray().ToList();
    Assert.All(doPcp, x => Assert.True(x.GetProperty("estornavel").GetBoolean()));
  }

  [Fact]
  public async Task Reclamacao_alem_do_expedido_e_409_SaldoInsuficiente()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 6m);
    await ExpedirAsync(c, peca, 4m);
    await ExpedirAsync(c, peca, 2m);

    // Duas remessas somam 6: reclamar tudo, em duas vezes, passa no limite; a seguinte recusa.
    Assert.Equal(HttpStatusCode.Created, (await RegistrarAsync(c, c.Qualidade, Corpo(peca, 4m))).StatusCode);
    Assert.Equal(HttpStatusCode.Created, (await RegistrarAsync(c, c.Qualidade, Corpo(peca, 2m))).StatusCode);
    var alem = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m));

    Assert.Equal(HttpStatusCode.Conflict, alem.StatusCode);
    var corpo = await CorpoAsync(alem);
    Assert.Equal("SaldoInsuficiente", corpo.GetProperty("erro").GetString());
    Assert.Contains("Chassi da Fase 3", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Reclamacao_de_Peca_nao_expedida_e_409_SaldoInsuficiente()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 3m);

    var resposta = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    Assert.Equal("SaldoInsuficiente", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Reclamacao_de_Item_e_400_NaoEhPeca()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await RegistrarAsync(c, c.Qualidade, Corpo(c.B, 1m));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("NaoEhPeca", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Descricao_vazia_e_400()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);

    foreach (var descricao in new string?[] { null, "", "   " })
    {
      var resposta = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m, descricao));
      Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
      Assert.Equal("DescricaoObrigatoria", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
    }
  }

  [Fact]
  public async Task Descricao_de_501_e_400_TextoLongoDemais()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);

    var longa = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m, new string('x', 501)));
    var limite = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m, new string('x', 500)));

    Assert.Equal(HttpStatusCode.BadRequest, longa.StatusCode);
    Assert.Equal("TextoLongoDemais", (await CorpoAsync(longa)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Created, limite.StatusCode);
  }

  [Fact]
  public async Task Quantidade_invalida_e_400()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);

    var resposta = await RegistrarAsync(c, c.Qualidade, Corpo(peca, 0m));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("QuantidadeInvalida", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Operador_e_403()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);

    foreach (var quem in new[] { c.Operador, c.Movimentador, c.Gestao, c.Separador })
      Assert.Equal(HttpStatusCode.Forbidden, (await RegistrarAsync(c, quem, Corpo(peca, 1m))).StatusCode);
    // Quem le a lista e qualquer autenticado.
    Assert.Equal(HttpStatusCode.OK, (await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}/reclamacoes")).StatusCode);
  }

  [Fact]
  public async Task Estornar_a_propria_reclamacao()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);
    var id = (await CorpoAsync(await RegistrarAsync(c, c.Qualidade, Corpo(peca, 2m)))).GetProperty("id").GetInt32();

    var estorno = await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null);

    Assert.Equal(HttpStatusCode.OK, estorno.StatusCode);
    var r = await CorpoAsync(estorno);
    Assert.True(r.GetProperty("estornada").GetBoolean());
    Assert.False(r.GetProperty("estornavel").GetBoolean());
    Assert.Equal(0m, r.GetProperty("aRepor").GetDecimal());
    // A quantidade estornada volta a poder ser reclamada.
    Assert.Equal(HttpStatusCode.Created, (await RegistrarAsync(c, c.Qualidade, Corpo(peca, 2m))).StatusCode);
  }

  [Fact]
  public async Task Estornar_a_de_outro_sem_ser_PCP_e_403_Proibido()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);
    var id = (await CorpoAsync(await RegistrarAsync(c, c.Pcp, Corpo(peca, 1m)))).GetProperty("id").GetInt32();

    var daQualidade = await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null);
    var doOperador = await c.Como(c.Operador).PostAsync($"/api/reclamacoes/{id}/estorno", null);

    Assert.Equal(HttpStatusCode.Forbidden, daQualidade.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(daQualidade)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, doOperador.StatusCode);

    // O PCP estorna a da Qualidade.
    var daQualidadeId = (await CorpoAsync(await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m)))).GetProperty("id").GetInt32();
    Assert.Equal(HttpStatusCode.OK, (await c.Como(c.Pcp).PostAsync($"/api/reclamacoes/{daQualidadeId}/estorno", null)).StatusCode);
  }

  [Fact]
  public async Task Estornar_duas_vezes_e_409_JaEstornado()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    await ExpedirAsync(c, peca, 2m);
    var id = (await CorpoAsync(await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m)))).GetProperty("id").GetInt32();
    Assert.Equal(HttpStatusCode.OK, (await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null)).StatusCode);

    var segundo = await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null);

    Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    Assert.Equal("JaEstornado", (await CorpoAsync(segundo)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.NotFound, (await c.Como(c.Qualidade).PostAsync("/api/reclamacoes/2000000000/estorno", null)).StatusCode);
  }

  [Fact]
  public async Task Estornar_remessa_abaixo_do_reclamado_e_409_AbaixoDoReclamado()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);
    var expedida = await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade = 2m } } });
    await Garantir(expedida);
    var remessaId = (await CorpoAsync(expedida)).GetProperty("remessa").GetProperty("id").GetInt32();
    var reclamacaoId = (await CorpoAsync(await RegistrarAsync(c, c.Qualidade, Corpo(peca, 1m)))).GetProperty("id").GetInt32();

    var recusada = await c.Como(c.Qualidade).PostAsync($"/api/remessas/{remessaId}/estorno", null);

    Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
    var corpoDaRecusa = await CorpoAsync(recusada);
    Assert.Equal("AbaixoDoReclamado", corpoDaRecusa.GetProperty("erro").GetString());
    // Sem Retrabalho, estornar a reclamacao e o caminho, e a frase o diz.
    Assert.EndsWith("Estorne antes a reclamação.", corpoDaRecusa.GetProperty("mensagem").GetString());
    // Nada foi gravado: a remessa segue valida e o expedido da Peca segue inteiro.
    Assert.Equal("Concluido", (await CorpoAsync(await c.Como(c.Qualidade).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("status").GetString());

    // Estornada a reclamacao, o chao deixa de existir e a remessa se estorna.
    await Garantir(await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{reclamacaoId}/estorno", null));
    Assert.Equal(HttpStatusCode.Created, (await c.Como(c.Qualidade).PostAsync($"/api/remessas/{remessaId}/estorno", null)).StatusCode);
  }
}
