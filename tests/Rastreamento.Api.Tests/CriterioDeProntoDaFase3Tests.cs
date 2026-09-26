using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O critério de pronto da Fase 3 (`specs/06-roadmap-mvp.md` e spec secao 9.3), ponta a ponta: um Pedido
/// Avulso A &lt;- B, C percorrido inteiro — iniciar, terminar, entregar, montar, entregar a Peca no local de
/// expedicao —, com `GET /agrupamentos/{id}/posicoes` dizendo, a cada passo, onde esta cada peca e se
/// ela aguarda coleta, e `GET /tarefas` dizendo o que o Movimentador tem a levar.
/// </summary>
public class CriterioDeProntoDaFase3Tests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public CriterioDeProntoDaFase3Tests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Pedido_Avulso_A_com_B_e_C_percorrido_do_inicio_a_expedicao()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    var movimentador = c.Como(c.Movimentador);

    // Tudo nasce a iniciar (regra 28).
    await AfirmarPosicoesAsync(c, c.A, ("AIniciar", null, null, 10m));
    await AfirmarPosicoesAsync(c, c.B, ("AIniciar", null, null, 20m));

    // B e C: iniciar e terminar o unico passo — ficam aguardando coleta no Setor onde terminaram.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 20m }));
    await AfirmarPosicoesAsync(c, c.B, ("NoSetor", c.Corte, 1, 20m));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 20m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.C}/inicios", new { setorId = c.Dobra, quantidade = 10m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.C}/terminos", new { setorId = c.Dobra, ordem = 1, quantidade = 10m }));
    await AfirmarPosicoesAsync(c, c.B, ("AguardandoColeta", c.Corte, 1, 20m));
    await AfirmarPosicoesAsync(c, c.C, ("AguardandoColeta", c.Dobra, 1, 10m));

    // O Movimentador ve as duas como Item pronto, com a montagem de A como destino.
    var tarefas = await TarefasAsync(c);
    Assert.Equal(new[] { c.B, c.C }, tarefas.Select(t => t.No).Order().ToArray());
    Assert.All(tarefas, t => Assert.Equal("Montagem", t.Destino));

    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 20m },
        new { estruturaItemId = c.C, origem = new { posicao = "AguardandoColeta", setorId = c.Dobra, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 10m },
      },
    }));
    await AfirmarPosicoesAsync(c, c.B, ("AguardandoMontagem", c.Solda, null, 20m));
    Assert.Empty(await TarefasAsync(c));

    // A entra na Solda e e montada com os dois filhos.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 10m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/montagens", new { setorId = c.Solda, quantidade = 10m }));
    await AfirmarPosicoesAsync(c, c.B, ("Montado", null, null, 20m));
    await AfirmarPosicoesAsync(c, c.C, ("Montado", null, null, 10m));
    Assert.Equal(10m, (await PosicoesAsync(c)).Single(p => p.GetProperty("estruturaItemId").GetInt32() == c.A)
        .GetProperty("totalMontado").GetDecimal());

    // A termina a Solda, vai para a Pintura, termina, e vai ao local de expedicao.
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/terminos", new { setorId = c.Solda, ordem = 1, quantidade = 10m }));
    Assert.Equal("ProximoPasso", Assert.Single(await TarefasAsync(c)).Destino);
    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = c.A, origem = new { posicao = "AguardandoColeta", setorId = c.Solda, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade = 10m } },
    }));
    await AfirmarPosicoesAsync(c, c.A, ("NoSetor", c.Pintura, 2, 10m));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.A}/terminos", new { setorId = c.Pintura, ordem = 2, quantidade = 10m }));
    Assert.Equal("Expedicao", Assert.Single(await TarefasAsync(c)).Destino);
    await Garantir(await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = c.A, origem = new { posicao = "AguardandoColeta", setorId = c.Pintura, ordem = (int?)2 }, destinoSetorId = (int?)null, quantidade = 10m } },
    }));

    await AfirmarPosicoesAsync(c, c.A, ("NaExpedicao", null, null, 10m));
    Assert.Empty(await TarefasAsync(c));
  }

  private static async Task<List<JsonElement>> PosicoesAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
          .EnumerateArray().ToList();

  /// <summary>O no esta, inteiro, exatamente nas posicoes dadas — nem uma unidade em outro lugar.</summary>
  private static async Task AfirmarPosicoesAsync(
      CenarioDaFase3NaApi c, int no, params (string Posicao, int? SetorId, int? Ordem, decimal Quantidade)[] esperado)
  {
    var saldos = (await PosicoesAsync(c)).Single(p => p.GetProperty("estruturaItemId").GetInt32() == no)
        .GetProperty("saldos").EnumerateArray()
        .Select(s => (
            s.GetProperty("posicao").GetString()!,
            s.GetProperty("setorId").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("setorId").GetInt32(),
            s.GetProperty("ordem").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("ordem").GetInt32(),
            s.GetProperty("quantidade").GetDecimal()))
        .ToArray();
    Assert.Equal(esperado, saldos);
  }

  private static async Task<List<(int No, string Destino)>> TarefasAsync(CenarioDaFase3NaApi c)
  {
    var nos = new[] { c.A, c.B, c.C };
    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));
    return tarefas.EnumerateArray()
        .SelectMany(s => s.GetProperty("itens").EnumerateArray())
        .Select(i => (No: i.GetProperty("no").GetProperty("id").GetInt32(), Destino: i.GetProperty("destino").GetProperty("tipo").GetString()!))
        .Where(t => nos.Contains(t.No))
        .ToList();
  }
}
