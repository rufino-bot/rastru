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
/// O "nao repor" e o "a repor" nos DTOs (D3, C1, C3 e C15 da spec da Fase 5B-2, secoes 3.4, 4.1 e 4.6), no cenario da
/// Fase 3: Peca A (10; Solda -> Pintura), B (20, "Suporte") e C (10). A reclamacao usa uma Peca nova expedida no lugar de
/// A. Toda assercao escopada no Pedido do cenario.
/// </summary>
public class DispensaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public DispensaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static object Justificativa(string texto = "O cliente cancelou o item.") => new { justificativa = texto };

  private static Task<HttpResponseMessage> DispensarBaixaAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int perdaId, object? corpo = null) =>
      c.Como(quem).PostAsJsonAsync($"/api/baixas/{perdaId}/dispensa", corpo ?? Justificativa());

  private static Task<HttpResponseMessage> DispensarReclamacaoAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int id, object? corpo = null) =>
      c.Como(quem).PostAsJsonAsync($"/api/reclamacoes/{id}/dispensa", corpo ?? Justificativa());

  private static Task<HttpResponseMessage> AbrirDaBaixaAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int perdaId, string numero) =>
      c.Como(quem).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos",
          new { perdaId, numero, dataEntrega = SemAtraso, motivoRetrabalho = "Perda" });

  private static async Task<int> DarBaixaAsync(CenarioDaFase3NaApi c, int no, string motivo, decimal quantidade, bool incluirTopo)
  {
    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{no}/baixas", new
    {
      motivo, observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "AIniciar", setorId = (int?)null, ordem = (int?)null, quantidade } },
      incluirTopo,
    });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  private static async Task<string> NumeroDoOriginalAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString()!;

  private static async Task<decimal> ARepor_DoPedidoAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("aRepor").GetDecimal();

  /// <summary>A baixa na lista do Pedido (`GET /pedidos/{id}/baixas`).</summary>
  private static async Task<JsonElement> BaixaNaListaAsync(CenarioDaFase3NaApi c, int perdaId)
  {
    var lista = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}/baixas"));
    return lista.EnumerateArray().Single(b => b.GetProperty("id").GetInt32() == perdaId);
  }

  private static async Task<(int Peca, int Material)> PecaExpedidaAsync(CenarioDaFase3NaApi c, decimal quantidade)
  {
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.DeleteAsync($"/api/estrutura/{c.A}"));
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));
    var material = await c.CriarMaterialAsync("DS");
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/materiais",
        new { linhas = new[] { new { materialId = material, quantidade = 2m * quantidade } } }));

    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/inicios", new { setorId = c.Corte, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/terminos", new { setorId = c.Corte, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade } },
    }));
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade } } }));
    return (peca, material);
  }

  private static async Task<int> ReclamarAsync(CenarioDaFase3NaApi c, int peca, decimal quantidade)
  {
    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/reclamacoes",
        new { estruturaItemId = peca, quantidade, descricao = "Voltou com rebarba." });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  private static async Task<(HttpStatusCode Status, string? Erro)> RecusaAsync(HttpResponseMessage resposta) =>
      (resposta.StatusCode, (await CorpoAsync(resposta)).TryGetProperty("erro", out var e) ? e.GetString() : null);

  [Fact]
  public async Task Nao_repor_tira_a_baixa_do_a_repor()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    Assert.Equal(1m, await ARepor_DoPedidoAsync(c));
    var daLista = await BaixaNaListaAsync(c, perdaId);
    Assert.Equal(1m, daLista.GetProperty("aRepor").GetDecimal());
    Assert.Equal(JsonValueKind.Null, daLista.GetProperty("dispensa").ValueKind);
    Assert.Equal(JsonValueKind.Null, daLista.GetProperty("pedidoRetrabalho").ValueKind);

    var resposta = await DispensarBaixaAsync(c, c.Pcp, perdaId, Justificativa("  Sem previsao de reposicao.  "));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var dispensa = await CorpoAsync(resposta);
    Assert.Equal(("Sem previsao de reposicao.", c.Pcp.Id), (dispensa.GetProperty("justificativa").GetString(), dispensa.GetProperty("usuarioId").GetInt32()));
    Assert.False(string.IsNullOrEmpty(dispensa.GetProperty("usuarioNome").GetString()));
    Assert.Equal(0m, await ARepor_DoPedidoAsync(c));
    var depois = await BaixaNaListaAsync(c, perdaId);
    Assert.Equal(0m, depois.GetProperty("aRepor").GetDecimal());
    Assert.Equal(dispensa.GetProperty("id").GetInt32(), depois.GetProperty("dispensa").GetProperty("id").GetInt32());

    // A lista de Pedidos traz a mesma conta, numa consulta so.
    var numero = await NumeroDoOriginalAsync(c);
    var pagina = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos?busca={numero}"));
    var doPedido = pagina.GetProperty("itens").EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == c.PedidoId);
    Assert.Equal(0m, doPedido.GetProperty("aRepor").GetDecimal());
  }

  [Fact]
  public async Task Desfazer_o_nao_repor_devolve_o_a_repor()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "MortaEmProcesso", 2m, incluirTopo: false);
    var dispensaId = (await CorpoAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId))).GetProperty("id").GetInt32();
    Assert.Equal(0m, await ARepor_DoPedidoAsync(c));

    var estorno = await c.Como(c.Pcp).PostAsync($"/api/dispensas/{dispensaId}/estorno", null);

    Assert.Equal(HttpStatusCode.NoContent, estorno.StatusCode);
    Assert.Equal(2m, await ARepor_DoPedidoAsync(c));
    Assert.Equal(JsonValueKind.Null, (await BaixaNaListaAsync(c, perdaId)).GetProperty("dispensa").ValueKind);
    // O rastro fica: a linha nao e apagada, so marcada.
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var linha = await db.DispensasDeReposicao.AsNoTracking().SingleAsync(d => d.Id == dispensaId);
      Assert.Equal((c.Pcp.Id, true), (linha.EstornadaPorUsuarioId, linha.EstornadaEm is not null));
    }
    // Desfeita, pode-se dispensar de novo (o indice unico e so da vigente).
    Assert.Equal(HttpStatusCode.Created, (await DispensarBaixaAsync(c, c.Pcp, perdaId)).StatusCode);
  }

  [Fact]
  public async Task Desfazer_duas_vezes_e_409_JaEstornado()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    var dispensaId = (await CorpoAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId))).GetProperty("id").GetInt32();
    await Garantir(await c.Como(c.Pcp).PostAsync($"/api/dispensas/{dispensaId}/estorno", null));

    var segundo = await c.Como(c.Pcp).PostAsync($"/api/dispensas/{dispensaId}/estorno", null);

    Assert.Equal((HttpStatusCode.Conflict, "JaEstornado"), await RecusaAsync(segundo));
    Assert.Equal(HttpStatusCode.NotFound, (await c.Como(c.Pcp).PostAsync("/api/dispensas/2000000000/estorno", null)).StatusCode);
  }

  [Fact]
  public async Task Justificativa_vazia_e_400_JustificativaObrigatoria()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);

    foreach (var texto in new string?[] { null, "", "   " })
      Assert.Equal((HttpStatusCode.BadRequest, "JustificativaObrigatoria"),
          await RecusaAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId, new { justificativa = texto })));
    Assert.Equal((HttpStatusCode.BadRequest, "TextoLongoDemais"),
        await RecusaAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId, Justificativa(new string('x', 501)))));
    Assert.Equal(1m, await ARepor_DoPedidoAsync(c));
    // O limite exato passa.
    Assert.Equal(HttpStatusCode.Created, (await DispensarBaixaAsync(c, c.Pcp, perdaId, Justificativa(new string('x', 500)))).StatusCode);
  }

  [Fact]
  public async Task Nao_repor_baixa_ja_reposta_e_409_JaReposto()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    var numero = await NumeroDoOriginalAsync(c);
    await Garantir(await AbrirDaBaixaAsync(c, c.Qualidade, perdaId, $"{numero}-R1"));

    var resposta = await DispensarBaixaAsync(c, c.Pcp, perdaId);

    Assert.Equal((HttpStatusCode.Conflict, "JaReposto"), await RecusaAsync(resposta));
    // A baixa reposta mostra o Retrabalho dela e nao conta mais (C3).
    var baixa = await BaixaNaListaAsync(c, perdaId);
    Assert.Equal($"{numero}-R1", baixa.GetProperty("pedidoRetrabalho").GetProperty("numero").GetString());
    Assert.Equal(0m, baixa.GetProperty("aRepor").GetDecimal());
  }

  [Fact]
  public async Task Abrir_retrabalho_de_baixa_dispensada_e_409_ReposicaoDispensada()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    await Garantir(await DispensarBaixaAsync(c, c.Pcp, perdaId));

    var resposta = await AbrirDaBaixaAsync(c, c.Qualidade, perdaId, $"{await NumeroDoOriginalAsync(c)}-R1");

    Assert.Equal((HttpStatusCode.Conflict, "ReposicaoDispensada"), await RecusaAsync(resposta));
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.False(await db.Pedidos.AsNoTracking().AnyAsync(p => p.PedidoOrigemId == c.PedidoId));
  }

  [Fact]
  public async Task Dispensar_e_abrir_a_mesma_baixa_em_paralelo_deixa_so_um_dos_dois()
  {
    // A abertura e o "nao repor" travam o Pedido do alvo (decisao P5 do plano de backend da Fase 5B-2) e releem o alvo
    // depois da trava: um vence, o outro ve o vencedor e recusa (409 `JaReposto` ou `ReposicaoDispensada`); nunca os dois.
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 2m, incluirTopo: false);
    var numero = await NumeroDoOriginalAsync(c);

    var respostas = await Task.WhenAll(
        DispensarBaixaAsync(c, c.Pcp, perdaId),
        AbrirDaBaixaAsync(c, c.Qualidade, perdaId, $"{numero}-R1"));

    var vencedora = Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var perdedora = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro) = await RecusaAsync(perdedora);
    Assert.Equal(HttpStatusCode.Conflict, status);
    var abriu = vencedora.RequestMessage!.RequestUri!.AbsolutePath.EndsWith("/retrabalhos", StringComparison.Ordinal);
    Assert.Equal(abriu ? "JaReposto" : "ReposicaoDispensada", erro);

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var retrabalhos = await db.Pedidos.AsNoTracking().CountAsync(p => p.PedidoOrigemId == c.PedidoId);
    var vigentes = await db.DispensasDeReposicao.AsNoTracking().CountAsync(d => d.PerdaId == perdaId && d.EstornadaEm == null);
    Assert.Equal(abriu ? (1, 0) : (0, 1), (retrabalhos, vigentes));
  }

  [Fact]
  public async Task Qualidade_nao_repor_e_403()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);

    foreach (var quem in new[] { c.Qualidade, c.Operador, c.Movimentador, c.Gestao, c.Separador })
      Assert.Equal(HttpStatusCode.Forbidden, (await DispensarBaixaAsync(c, quem, perdaId)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await DispensarReclamacaoAsync(c, c.Qualidade, 1)).StatusCode);

    var dispensaId = (await CorpoAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId))).GetProperty("id").GetInt32();
    Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(c.Qualidade).PostAsync($"/api/dispensas/{dispensaId}/estorno", null)).StatusCode);
    Assert.Equal(0m, await ARepor_DoPedidoAsync(c));
  }

  [Fact]
  public async Task Baixa_sem_Peca_ou_de_Descarte_ou_estornada_e_409_SemPecaARepor()
  {
    await using var c = await CriarAsync(_factory);
    var doItem = await DarBaixaAsync(c, c.B, "PerdaArmazem", 2m, incluirTopo: false);
    var descarte = await DarBaixaAsync(c, c.A, "Descarte", 1m, incluirTopo: false);
    var estornada = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    await Garantir(await c.Como(c.Qualidade).PostAsync($"/api/baixas/{estornada}/estorno", null));

    foreach (var id in new[] { doItem, descarte, estornada })
      Assert.Equal((HttpStatusCode.Conflict, "SemPecaARepor"), await RecusaAsync(await DispensarBaixaAsync(c, c.Pcp, id)));
    Assert.Equal(HttpStatusCode.NotFound, (await DispensarBaixaAsync(c, c.Pcp, 2000000000)).StatusCode);
    Assert.Equal(0m, await ARepor_DoPedidoAsync(c));
  }

  [Fact]
  public async Task Segunda_dispensa_vigente_e_409_ReposicaoDispensada()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    await Garantir(await DispensarBaixaAsync(c, c.Pcp, perdaId));

    Assert.Equal((HttpStatusCode.Conflict, "ReposicaoDispensada"), await RecusaAsync(await DispensarBaixaAsync(c, c.Pcp, perdaId)));
  }

  [Fact]
  public async Task Nao_repor_a_reclamacao_tira_o_a_repor_e_trava_o_estorno_dela()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var id = await ReclamarAsync(c, peca, 1m);
    Assert.Equal(1m, await ARepor_DoPedidoAsync(c));

    var dispensa = await DispensarReclamacaoAsync(c, c.Pcp, id);

    Assert.Equal(HttpStatusCode.Created, dispensa.StatusCode);
    Assert.Equal(0m, await ARepor_DoPedidoAsync(c));
    var lista = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}/reclamacoes"));
    var daLista = lista.EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id);
    Assert.Equal((0m, (await CorpoAsync(dispensa)).GetProperty("id").GetInt32()),
        (daLista.GetProperty("aRepor").GetDecimal(), daLista.GetProperty("dispensa").GetProperty("id").GetInt32()));
    Assert.Equal((HttpStatusCode.Conflict, "ReposicaoDispensada"), await RecusaAsync(await DispensarReclamacaoAsync(c, c.Pcp, id)));
    // Com "nao repor" vigente, a reclamacao nao se estorna.
    Assert.Equal((HttpStatusCode.Conflict, "ReposicaoResolvida"),
        await RecusaAsync(await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null)));
  }

  [Fact]
  public async Task Estornar_reclamacao_com_retrabalho_e_409_ReposicaoResolvida()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var id = await ReclamarAsync(c, peca, 1m);
    var numero = await NumeroDoOriginalAsync(c);
    var aberta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos",
        new { reclamacaoId = id, destino = "Refazer", numero = $"{numero}-R1", dataEntrega = SemAtraso, motivoRetrabalho = "SolicitacaoCliente" });
    await Garantir(aberta);

    var estorno = await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null);

    Assert.Equal((HttpStatusCode.Conflict, "ReposicaoResolvida"), await RecusaAsync(estorno));
    // E a que tem Retrabalho nao aceita dispensa.
    Assert.Equal((HttpStatusCode.Conflict, "JaReposto"), await RecusaAsync(await DispensarReclamacaoAsync(c, c.Pcp, id)));
    var lista = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}/reclamacoes"));
    var daLista = lista.EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == id);
    Assert.Equal(($"{numero}-R1", 0m),
        (daLista.GetProperty("pedidoRetrabalho").GetProperty("numero").GetString(), daLista.GetProperty("aRepor").GetDecimal()));
  }

  [Fact]
  public async Task Reclamacao_estornada_ou_inexistente_nao_aceita_dispensa()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PecaExpedidaAsync(c, 2m);
    var id = await ReclamarAsync(c, peca, 1m);
    await Garantir(await c.Como(c.Qualidade).PostAsync($"/api/reclamacoes/{id}/estorno", null));

    Assert.Equal((HttpStatusCode.Conflict, "SemPecaARepor"), await RecusaAsync(await DispensarReclamacaoAsync(c, c.Pcp, id)));
    Assert.Equal(HttpStatusCode.NotFound, (await DispensarReclamacaoAsync(c, c.Pcp, 2000000000)).StatusCode);
  }

  [Fact]
  public async Task Pedido_de_Retrabalho_traz_a_origem_e_o_motivo_e_a_baixa_traz_o_Retrabalho()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await DarBaixaAsync(c, c.A, "PerdaArmazem", 1m, incluirTopo: false);
    var numero = await NumeroDoOriginalAsync(c);

    var aberta = await AbrirDaBaixaAsync(c, c.Qualidade, perdaId, $"{numero}-R1");

    var criado = await CorpoAsync(aberta);
    Assert.Equal((c.PedidoId, numero, "Perda"), (
        criado.GetProperty("pedidoOrigem").GetProperty("id").GetInt32(), criado.GetProperty("pedidoOrigem").GetProperty("numero").GetString(),
        criado.GetProperty("motivoRetrabalho").GetString()));
    var retrabalhoId = criado.GetProperty("id").GetInt32();
    var lido = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{retrabalhoId}"));
    Assert.Equal((c.PedidoId, "Perda", 0m), (
        lido.GetProperty("pedidoOrigem").GetProperty("id").GetInt32(), lido.GetProperty("motivoRetrabalho").GetString(),
        lido.GetProperty("aRepor").GetDecimal()));
    // O original nao tem origem nem motivo.
    var original = await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}"));
    Assert.Equal(JsonValueKind.Null, original.GetProperty("pedidoOrigem").ValueKind);
    Assert.Equal(JsonValueKind.Null, original.GetProperty("motivoRetrabalho").ValueKind);
    var baixa = await BaixaNaListaAsync(c, perdaId);
    Assert.Equal((retrabalhoId, numero + "-R1"), (
        baixa.GetProperty("pedidoRetrabalho").GetProperty("id").GetInt32(), baixa.GetProperty("pedidoRetrabalho").GetProperty("numero").GetString()));
  }
}
