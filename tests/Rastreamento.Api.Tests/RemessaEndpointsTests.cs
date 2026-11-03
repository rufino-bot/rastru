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
/// As rotas da remessa (C8, C9, C12 e C13 da spec da Fase 5A, secoes 3.5 e 5.2) no cenario da Fase 3. A Peca A do
/// cenario e excluida (o Pedido ainda esta `Aberto`), e uma Peca nova, copiada do catalogo, que exige relatorio, e
/// levada ao local de expedicao pelas rotas que existem: ela e a unica Peca do Pedido, que assim pode concluir.
/// Toda asserção escopada nessa Peca e nesse Pedido.
/// </summary>
public class RemessaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public RemessaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  /// <summary>A Peca nova, no Corte, com tudo em `NaExpedicao`. Sai no `DisposeAsync` do cenario (pelo Agrupamento).</summary>
  private static async Task<int> UnicaPecaNaExpedicaoAsync(CenarioDaFase3NaApi c, decimal quantidade)
  {
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.DeleteAsync($"/api/estrutura/{c.A}"));
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = true });
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

  private static object Remessa(int peca, decimal quantidade) =>
      new { itens = new[] { new { estruturaItemId = peca, quantidade } } };

  private static async Task<JsonElement> PedidoAsync(CenarioDaFase3NaApi c) =>
      await CorpoAsync(await c.Como(c.Qualidade).GetAsync($"/api/pedidos/{c.PedidoId}"));

  [Fact]
  public async Task Qualidade_expede_e_estorna_a_remessa()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 6m);
    var qualidade = c.Como(c.Qualidade);
    Assert.Equal("AguardandoExpedicao", (await PedidoAsync(c)).GetProperty("status").GetString());

    var expedida = await qualidade.PostAsJsonAsync("/api/remessas", Remessa(peca, 6m));

    Assert.Equal(HttpStatusCode.Created, expedida.StatusCode);
    var corpo = await CorpoAsync(expedida);
    var remessa = corpo.GetProperty("remessa");
    var remessaId = remessa.GetProperty("id").GetInt32();
    Assert.Equal(c.Qualidade.Id, remessa.GetProperty("usuarioId").GetInt32());
    Assert.False(remessa.GetProperty("estornada").GetBoolean());
    Assert.True(remessa.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", remessa.GetProperty("dataHora").GetString());
    var linha = Assert.Single(remessa.GetProperty("linhas").EnumerateArray());
    Assert.Equal(peca, linha.GetProperty("pecaId").GetInt32());
    Assert.Equal("Chassi da Fase 3", linha.GetProperty("pecaDescricao").GetString());   // copiada do catalogo
    Assert.StartsWith("f3-", linha.GetProperty("codigoDoComponente").GetString());
    Assert.Equal(c.PedidoId, linha.GetProperty("pedidoId").GetInt32());
    Assert.True(linha.GetProperty("pedidoConcluido").GetBoolean());
    Assert.Equal("NaExpedicao", linha.GetProperty("origem").GetString());
    Assert.Equal(6m, linha.GetProperty("quantidade").GetDecimal());
    // Exige relatorio e nao foi aferida: avisa e deixa expedir (D8).
    var aviso = Assert.Single(corpo.GetProperty("avisos").EnumerateArray());
    Assert.Equal((peca, "SemAfericao", 6m),
        (aviso.GetProperty("pecaId").GetInt32(), aviso.GetProperty("tipo").GetString(), aviso.GetProperty("quantidade").GetDecimal()));

    // A ultima remessa conclui o Pedido, com a data (C12).
    var concluido = await PedidoAsync(c);
    Assert.Equal("Concluido", concluido.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.String, concluido.GetProperty("dataConclusao").ValueKind);
    Assert.EndsWith("-03:00", concluido.GetProperty("dataConclusao").GetString());

    // A linha da remessa nao se estorna pela rota generica (C17), nem pelo PCP.
    var linhaNoLivro = (await CorpoAsync(await qualidade.GetAsync($"/api/estrutura/{peca}/movimentacoes")))
        .GetProperty("movimentacoes").EnumerateArray()
        .Single(m => m.GetProperty("tipo").GetString() == "Expedicao");
    Assert.Equal(remessaId, linhaNoLivro.GetProperty("remessaId").GetInt32());
    var generico = await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{linhaNoLivro.GetProperty("id").GetInt32()}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, generico.StatusCode);
    Assert.Equal("EstornoImpossivel", (await CorpoAsync(generico)).GetProperty("erro").GetString());

    // O estorno da remessa e aceito no Pedido Concluido e o reabre (C13).
    var estorno = await qualidade.PostAsync($"/api/remessas/{remessaId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    var estornos = (await CorpoAsync(estorno)).EnumerateArray().ToList();
    var linhaDoEstorno = Assert.Single(estornos);
    Assert.Equal("Estorno", linhaDoEstorno.GetProperty("tipo").GetString());
    Assert.Equal(remessaId, linhaDoEstorno.GetProperty("remessaId").GetInt32());
    Assert.Equal(linhaNoLivro.GetProperty("id").GetInt32(), linhaDoEstorno.GetProperty("estornoDeId").GetInt32());
    Assert.Equal("Expedido", linhaDoEstorno.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal("NaExpedicao", linhaDoEstorno.GetProperty("destino").GetProperty("posicao").GetString());
    var reaberto = await PedidoAsync(c);
    Assert.Equal("AguardandoExpedicao", reaberto.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.Null, reaberto.GetProperty("dataConclusao").ValueKind);

    // Uma vez so.
    var segundo = await qualidade.PostAsync($"/api/remessas/{remessaId}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    Assert.Equal("JaEstornado", (await CorpoAsync(segundo)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Movimentador_nao_expede()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 2m);

    var movimentador = await c.Como(c.Movimentador).PostAsJsonAsync("/api/remessas", Remessa(peca, 1m));
    var operador = await c.Como(c.Operador).PostAsJsonAsync("/api/remessas", Remessa(peca, 1m));

    Assert.Equal(HttpStatusCode.Forbidden, movimentador.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, operador.StatusCode);

    // O estorno: a Gestao passa pelo `[Authorize]`, mas a remessa nao e dela (403 do caso de uso, com corpo); o PCP
    // estorna a de outro; o Movimentador nem chega.
    var expedida = await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas", Remessa(peca, 1m));
    await Garantir(expedida);
    var remessaId = (await CorpoAsync(expedida)).GetProperty("remessa").GetProperty("id").GetInt32();
    Assert.Equal(HttpStatusCode.Forbidden,
        (await c.Como(c.Movimentador).PostAsync($"/api/remessas/{remessaId}/estorno", null)).StatusCode);
    var daGestao = await c.Como(c.Gestao).PostAsync($"/api/remessas/{remessaId}/estorno", null);
    Assert.Equal(HttpStatusCode.Forbidden, daGestao.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(daGestao)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Created, (await c.Como(c.Pcp).PostAsync($"/api/remessas/{remessaId}/estorno", null)).StatusCode);
  }

  [Fact]
  public async Task Duas_remessas_ao_mesmo_tempo_nunca_passam_do_expedivel()
  {
    // Review Focus 1: 10 no local de expedicao, duas remessas de 6 ao mesmo tempo.
    await using var c = await CriarAsync(_factory);
    var peca = await UnicaPecaNaExpedicaoAsync(c, 10m);

    var respostas = await Task.WhenAll(
        c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas", Remessa(peca, 6m)),
        c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas", Remessa(peca, 6m)));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
    Assert.Contains((await CorpoAsync(recusada)).GetProperty("erro").GetString(), new[] { "SaldoInsuficiente", "ConflitoDeConcorrencia" });

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var expedido = await db.Movimentacoes.AsNoTracking()
        .Where(m => m.EstruturaItemId == peca && m.DestinoPosicao == "Expedido")
        .SumAsync(m => m.Quantidade);
    Assert.Equal(6m, expedido);
  }
}
