using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O Kit ponta a ponta (spec da Fase 3B): A (10; Solda -> Pintura), B (20, razao 2; Corte), C (10, razao 1; Dobra),
/// num Agrupamento Kit com a Solda marcada. Toda asercao escopada no proprio cenario.
/// </summary>
public class KitNaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public KitNaApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task ProntoAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
  }

  private static object DaColeta(int no, int setor, decimal quantidade) =>
      new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade };

  private static async Task<JsonElement> TarefasAsync(CenarioDaFase3NaApi c) =>
      await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));

  private static JsonElement? KitDe(JsonElement tarefas, string secao, int paiId) =>
      tarefas.GetProperty(secao).EnumerateArray()
          .Where(k => k.GetProperty("pai").GetProperty("id").GetInt32() == paiId)
          .Select(k => (JsonElement?)k).SingleOrDefault();

  [Fact]
  public async Task Entrega_de_Kit_sem_todos_os_filhos_da_400_ConjuntoIncompleto()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 2m);

    var resposta = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new { itens = new[] { DaColeta(c.B, c.Corte, 4m) } });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("ConjuntoIncompleto", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Kit_montavel_aparece_e_some_quando_levado()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 2m);

    var antes = await TarefasAsync(c);
    var entrega = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas",
        new { itens = new[] { DaColeta(c.B, c.Corte, 4m), DaColeta(c.C, c.Dobra, 2m) } });
    var depois = await TarefasAsync(c);

    var kit = KitDe(antes, "kitsMontaveis", c.A);
    Assert.NotNull(kit);
    Assert.Equal(2m, kit!.Value.GetProperty("conjuntos").GetDecimal());
    Assert.Equal(c.Solda, kit.Value.GetProperty("destino").GetProperty("id").GetInt32());
    var filhosNosGrupos = antes.GetProperty("grupos").EnumerateArray()
        .SelectMany(g => g.GetProperty("itens").EnumerateArray())
        .Select(i => i.GetProperty("no").GetProperty("id").GetInt32());
    Assert.DoesNotContain(filhosNosGrupos, id => id == c.B || id == c.C);
    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    Assert.Null(KitDe(depois, "kitsMontaveis", c.A));
  }

  [Fact]
  public async Task Kit_sem_conjunto_fechado_aparece_nos_incompletos()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);

    var tarefas = await TarefasAsync(c);

    Assert.Null(KitDe(tarefas, "kitsMontaveis", c.A));
    Assert.NotNull(KitDe(tarefas, "kitsIncompletos", c.A));
  }

  [Fact]
  public async Task Fila_traz_o_tipo_do_Agrupamento_no_resumo()
  {
    await using var c = await CriarAsync(_factory, kit: true);

    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Corte}/fila"));

    var linha = fila.GetProperty("aIniciar").EnumerateArray().Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    Assert.Equal("Kit", linha.GetProperty("no").GetProperty("agrupamentoTipo").GetString());
  }

  [Fact]
  public async Task Grupo_de_montagem_da_Fila_diz_conjuntoCompleto()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 2m);
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas",
        new { itens = new[] { DaColeta(c.B, c.Corte, 4m), DaColeta(c.C, c.Dobra, 2m) } }));

    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila"));

    var grupo = fila.GetProperty("aguardandoMontagem").EnumerateArray()
        .Single(g => g.GetProperty("pai").GetProperty("id").GetInt32() == c.A);
    Assert.True(grupo.GetProperty("conjuntoCompleto").GetBoolean());
  }
}
