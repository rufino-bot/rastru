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
/// `GET /pedidos/{id}/andamento` (secao 5.3 da spec da Fase 6A) no cenario da Fase 3: a Peca A (10; Solda -> Pintura)
/// tem os Itens B (20, Corte) e C (10, Dobra). Cada teste tem o Pedido dele, entao toda asserção e sobre aquele
/// Pedido. O relogio e o real: as asserções dizem relacoes (nulo, maior ou igual), nao valores de data.
/// </summary>
public class AndamentoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public AndamentoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task<JsonElement> AndamentoAsync(CenarioDaFase3NaApi c, UsuarioDeTeste? quem = null)
  {
    var resposta = await c.Como(quem ?? c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}/andamento");
    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    return await CorpoAsync(resposta);
  }

  private static JsonElement Peca(JsonElement andamento, int id) =>
      andamento.GetProperty("pecas").EnumerateArray().Single(p => p.GetProperty("estruturaItemId").GetInt32() == id);

  private static JsonElement Item(JsonElement no, int id) =>
      no.GetProperty("itens").EnumerateArray().Single(p => p.GetProperty("estruturaItemId").GetInt32() == id);

  private static string[] Chaves(JsonElement no) =>
      no.GetProperty("trilha").EnumerateArray().Select(b => b.GetProperty("chave").GetString()!).ToArray();

  private static JsonElement Bloco(JsonElement no, string chave) =>
      no.GetProperty("trilha").EnumerateArray().Single(b => b.GetProperty("chave").GetString() == chave);

  private static async Task<int> PecaNovaAsync(CenarioDaFase3NaApi c, decimal quantidade, params int[] passos)
  {
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade, requerRelatorioDimensional = false });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    if (passos.Length > 0) await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos }));
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

  /// <summary>Recua em `dias` a hora de toda linha de `Inicio` do no: o passo "passado" passa a ter permanencia conhecida.</summary>
  private async Task RecuarOInicioAsync(int no, int dias)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    await db.Movimentacoes.Where(m => m.EstruturaItemId == no && m.Tipo == "Inicio")
        .ExecuteUpdateAsync(s => s.SetProperty(m => m.DataHora, m => m.DataHora.AddDays(-dias)));
  }

  [Fact]
  public async Task Pedido_inexistente_da_404()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Pcp).GetAsync("/api/pedidos/2147483000/andamento");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Pedido_concluido_traz_numeros_e_nenhuma_peca()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.C, c.Dobra, 2m);
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Pedidos.Where(p => p.Id == c.PedidoId)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Concluido").SetProperty(p => p.DataConclusao, DateTime.UtcNow));
    }

    var andamento = await AndamentoAsync(c);

    Assert.NotEqual(JsonValueKind.Null, andamento.GetProperty("abertura").ValueKind);
    Assert.NotEqual(JsonValueKind.Null, andamento.GetProperty("agora").ValueKind);
    Assert.NotEqual(JsonValueKind.Null, andamento.GetProperty("inicioReal").ValueKind);
    Assert.Equal(JsonValueKind.Null, andamento.GetProperty("maisParado").ValueKind);
    Assert.Empty(andamento.GetProperty("pecas").EnumerateArray());
  }

  [Fact]
  public async Task Pedido_nao_iniciado_tem_inicio_real_nulo()
  {
    await using var c = await CriarAsync(_factory);

    var andamento = await AndamentoAsync(c);

    Assert.Equal(JsonValueKind.Null, andamento.GetProperty("inicioReal").ValueKind);
    Assert.Equal(0L, andamento.GetProperty("pausadoDepoisDoInicioSegundos").GetInt64());
    // A tem filhos: o "a iniciar" dela nao conta (C5), so os passos que ainda nao chegaram. Os filhos contam.
    var a = Peca(andamento, c.A);
    Assert.Equal(["Passo:1", "Passo:2"], Chaves(a));
    Assert.All(a.GetProperty("trilha").EnumerateArray(), b => Assert.Equal("Futuro", b.GetProperty("situacao").GetString()));
    var b = Item(a, c.B);
    Assert.Equal(["AIniciar", "Passo:1"], Chaves(b));
    Assert.Equal(20m, Bloco(b, "AIniciar").GetProperty("quantidade").GetDecimal());
    Assert.NotEqual(JsonValueKind.Null, Bloco(b, "AIniciar").GetProperty("desde").ValueKind);
  }

  [Fact]
  public async Task Trilha_com_passo_passado_atual_e_futuro()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNovaAsync(c, 4m, c.Corte, c.Dobra, c.Pintura);
    await IniciarAsync(c, peca, c.Corte, 4m);
    await TerminarAsync(c, peca, c.Corte, 1, 4m);
    await EntregarAsync(c, peca, c.Corte, 1, 4m);

    var no = Peca(await AndamentoAsync(c), peca);

    Assert.Equal(["Passo:1", "Passo:2", "Passo:3"], Chaves(no));
    Assert.Equal("Passado", Bloco(no, "Passo:1").GetProperty("situacao").GetString());
    var atual = Bloco(no, "Passo:2");
    Assert.Equal(("Atual", 4m, c.Dobra), (
        atual.GetProperty("situacao").GetString(), atual.GetProperty("quantidade").GetDecimal(), atual.GetProperty("setorId").GetInt32()));
    Assert.Equal("Passo", atual.GetProperty("tipo").GetString());
    Assert.NotEqual(JsonValueKind.Null, atual.GetProperty("desde").ValueKind);
    Assert.Equal("Futuro", Bloco(no, "Passo:3").GetProperty("situacao").GetString());
  }

  [Fact]
  public async Task Permanencia_media_do_passo_passado()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNovaAsync(c, 4m, c.Corte, c.Dobra);
    await IniciarAsync(c, peca, c.Corte, 4m);
    await TerminarAsync(c, peca, c.Corte, 1, 4m);
    await EntregarAsync(c, peca, c.Corte, 1, 2m);
    await RecuarOInicioAsync(peca, 2);

    var no = Peca(await AndamentoAsync(c), peca);

    // O Corte e passado E atual ao mesmo tempo (2 ainda esperam coleta): um bloco so, com a permanencia de 2 dias.
    // `quantidade` e o total do passo (no Setor + coleta), como o segmento do Panorama: aqui nada ficou no Setor.
    var corte = Bloco(no, "Passo:1");
    Assert.Equal(("Atual", 2m, 2m), (
        corte.GetProperty("situacao").GetString(), corte.GetProperty("quantidade").GetDecimal(),
        corte.GetProperty("aguardandoColeta").GetDecimal()));
    var segundos = corte.GetProperty("permanenciaMediaSegundos").GetInt64();
    Assert.InRange(segundos, 2 * 86400, 2 * 86400 + 300);
    Assert.Equal(JsonValueKind.Null, Bloco(no, "Passo:2").GetProperty("permanenciaMediaSegundos").ValueKind);
  }

  [Fact]
  public async Task Remessa_parcial_traz_o_expedido_do_no()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNovaAsync(c, 4m, c.Corte);
    await IniciarAsync(c, peca, c.Corte, 4m);
    await TerminarAsync(c, peca, c.Corte, 1, 4m);
    await EntregarAsync(c, peca, c.Corte, 1, 4m);
    var sem = Peca(await AndamentoAsync(c), peca);
    Assert.Equal(0m, sem.GetProperty("expedido").GetDecimal());

    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade = 1m } } }));

    var no = Peca(await AndamentoAsync(c), peca);
    Assert.Equal(1m, no.GetProperty("expedido").GetDecimal());
    // As 3 que sobraram continuam no local de expedicao: o bloco `Expedicao` segue com elas.
    Assert.Equal(3m, Bloco(no, "Expedicao").GetProperty("quantidade").GetDecimal());
  }

  [Fact]
  public async Task Item_aguardando_montagem_tem_bloco_de_montagem()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    await TerminarAsync(c, c.B, c.Corte, 1, 20m);
    await EntregarAsync(c, c.B, c.Corte, 1, 20m);

    var b = Item(Peca(await AndamentoAsync(c), c.A), c.B);

    var montagem = Bloco(b, $"Montagem:{c.Solda}");
    Assert.Equal(("Montagem", "Atual", 20m, c.Solda), (
        montagem.GetProperty("tipo").GetString(), montagem.GetProperty("situacao").GetString(),
        montagem.GetProperty("quantidade").GetDecimal(), montagem.GetProperty("setorId").GetInt32()));
    Assert.NotEqual(JsonValueKind.Null, montagem.GetProperty("desde").ValueKind);
    Assert.Equal("Passado", Bloco(b, "Passo:1").GetProperty("situacao").GetString());
    // A trilha na ordem: o passo do Item antes da montagem.
    Assert.Equal(["Passo:1", $"Montagem:{c.Solda}"], Chaves(b));
  }

  [Fact]
  public async Task Item_como_mais_parado()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.C, c.Dobra, 2m);

    var andamento = await AndamentoAsync(c);

    // B e C esperam a iniciar desde a criacao do Agrupamento (a mesma data); o menor Id desempata, e B e Item.
    var maisParado = andamento.GetProperty("maisParado");
    Assert.Equal((c.B, "AIniciar"), (maisParado.GetProperty("estruturaItemId").GetInt32(), maisParado.GetProperty("bloco").GetString()));
    Assert.NotEqual(JsonValueKind.Null, andamento.GetProperty("inicioReal").ValueKind);
    var a = Peca(andamento, c.A);
    Assert.Equal(2m, Bloco(Item(a, c.C), "Passo:1").GetProperty("quantidade").GetDecimal());
    Assert.Equal(8m, Bloco(Item(a, c.C), "AIniciar").GetProperty("quantidade").GetDecimal());
  }

  [Fact]
  public async Task Pausa_depois_do_inicio_em_segundos()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.C, c.Dobra, 2m);
    await Garantir(await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "teste" }));
    await Garantir(await c.Como(c.Gestao).PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null));
    DateTime inicio;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      inicio = await db.Movimentacoes.Where(m => m.EstruturaItemId == c.C && m.Tipo == "Inicio").MinAsync(m => m.DataHora);
      // Uma hora antes do inicio e ate duas horas depois dele: so as duas horas depois contam (regra 14).
      await db.PedidoPausas.Where(p => p.PedidoId == c.PedidoId)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.PausadoEm, inicio.AddHours(-1)).SetProperty(p => p.RetomadoEm, inicio.AddHours(2)));
    }

    var andamento = await AndamentoAsync(c);

    Assert.Equal(2 * 3600L, andamento.GetProperty("pausadoDepoisDoInicioSegundos").GetInt64());
  }

  [Fact]
  public async Task No_sem_roteiro_mostra_so_os_blocos_que_existem()
  {
    await using var c = await CriarAsync(_factory);
    var semRoteiro = await PecaNovaAsync(c, 3m);

    var andamento = await AndamentoAsync(c);

    var no = Peca(andamento, semRoteiro);
    Assert.Equal(["AIniciar"], Chaves(no));
    Assert.Equal(3m, Bloco(no, "AIniciar").GetProperty("quantidade").GetDecimal());
    Assert.Empty(no.GetProperty("itens").EnumerateArray());
    // O resto do Pedido continua inteiro ao lado do no sem Roteiro.
    Assert.Equal(["Passo:1", "Passo:2"], Chaves(Peca(andamento, c.A)));
  }

  [Fact]
  public async Task Qualquer_perfil_le()
  {
    await using var c = await CriarAsync(_factory);
    var cliente = _factory.CreateClient();
    var login = await cliente.PostAsJsonAsync("/api/auth/login", new { nomeUsuario = "operador", senha = "Operador@123" });
    Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    var token = (await CorpoAsync(login)).GetProperty("accessToken").GetString();
    cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    var resposta = await cliente.GetAsync($"/api/pedidos/{c.PedidoId}/andamento");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"/api/pedidos/{c.PedidoId}/andamento")).StatusCode);
  }
}
