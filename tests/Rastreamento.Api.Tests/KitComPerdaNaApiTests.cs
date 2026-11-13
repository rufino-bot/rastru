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
/// A perda dentro da Solda de um Kit, ponta a ponta (C15 e secao 3.5 da spec da Fase 5C): A (10; Solda -> Pintura),
/// B (20, razao 2, "Suporte"; Corte) e C (10, razao 1, "Calço"; Dobra), num Agrupamento Kit com a Solda marcada
/// `UtilizaKit`. O resto do conjunto quebrado e sobra desde a baixa e vai para o Retrabalho, e a baixa nao descarta o
/// "a iniciar" que o substitui. Toda assercao escopada no proprio cenario.
/// </summary>
public class KitComPerdaNaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public KitComPerdaNaApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static async Task ProntoAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
  }

  private static object DaColeta(int no, int setor, decimal quantidade) =>
      new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade };

  /// <summary>Termina `conjuntos` conjuntos (2 de B e 1 de C cada) e os entrega a Solda numa entrega so.</summary>
  private static async Task EntregarConjuntosAsync(CenarioDaFase3NaApi c, decimal conjuntos)
  {
    await ProntoAsync(c, c.B, c.Corte, conjuntos * 2m);
    await ProntoAsync(c, c.C, c.Dobra, conjuntos);
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas",
        new { itens = new[] { DaColeta(c.B, c.Corte, conjuntos * 2m), DaColeta(c.C, c.Dobra, conjuntos) } }));
  }

  private static List<(int No, string Origem, decimal Quantidade)> Linhas(JsonElement corpo) =>
      corpo.GetProperty("linhas").EnumerateArray()
          .Select(l => (l.GetProperty("estruturaItemId").GetInt32(), l.GetProperty("origemPosicao").GetString()!,
              l.GetProperty("quantidade").GetDecimal()))
          .ToList();

  /// <summary>As sobras "em montagem" dos nos deste cenario na Fila da Solda: (no, quantidade).</summary>
  private static async Task<(int No, decimal Quantidade)[]> SobrasEmMontagemAsync(CenarioDaFase3NaApi c) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila")))
          .GetProperty("sobra").EnumerateArray()
          .Where(l => l.GetProperty("origem").GetString() == "Montagem")
          .Select(l => (No: l.GetProperty("no").GetProperty("id").GetInt32(), Quantidade: l.GetProperty("quantidade").GetDecimal()))
          .Where(l => l.No == c.A || l.No == c.B || l.No == c.C)
          .OrderBy(l => l.No)
          .ToArray();

  /// <summary>O saldo do no numa posicao, somado por `GET /agrupamentos/{id}/posicoes`.</summary>
  private static async Task<decimal> SaldoAsync(CenarioDaFase3NaApi c, int no, string posicao) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
          .EnumerateArray()
          .Single(p => p.GetProperty("estruturaItemId").GetInt32() == no)
          .GetProperty("saldos").EnumerateArray()
          .Where(s => s.GetProperty("posicao").GetString() == posicao)
          .Sum(s => s.GetProperty("quantidade").GetDecimal());

  /// <summary>O no do Retrabalho que veio do no <paramref name="origem"/> do original.</summary>
  private async Task<int> CopiaDeAsync(int retrabalhoId, int origem)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await (from e in db.Estruturas.AsNoTracking()
                  join a in db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                  where a.PedidoId == retrabalhoId && e.OrigemEstruturaItemId == origem
                  select e.Id).SingleAsync();
  }

  [Fact]
  public async Task Perda_dentro_da_Solda_vai_para_o_Retrabalho_e_o_Kit_monta_as_nove()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    var qualidade = c.Como(c.Qualidade);
    var pcp = c.Como(c.Pcp);

    // 1. Tres conjuntos na Solda (6 de B e 3 de C); 14 de B e 7 de C ficam a iniciar.
    await EntregarConjuntosAsync(c, 3m);

    // 2. A previa da perda de 1 B dentro da Solda, levando a Peca do topo.
    var corpo = new
    {
      motivo = "PerdaArmazem", observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "AguardandoMontagem", setorId = (int?)c.Solda, ordem = (int?)null, quantidade = 1m } },
      incluirTopo = true,
    };
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa", corpo));

    Assert.Equal(1m, previa.GetProperty("travaNoTopo").GetProperty("quantidade").GetDecimal());
    // Nenhuma linha de B nem de C a iniciar: sao elas que fecham os conjuntos das 9 que ficam.
    Assert.Equal([(c.B, "AguardandoMontagem", 1m), (c.A, "AIniciar", 1m)], Linhas(previa));
    Assert.Equal(new[] { (c.B, 1m), (c.C, 1m) },
        previa.GetProperty("viramSobra").EnumerateArray()
            .Select(s => (s.GetProperty("estruturaItemId").GetInt32(), s.GetProperty("quantidade").GetDecimal()))
            .OrderBy(s => s.Item1));

    // 3. A baixa gravada, e o resto do conjunto quebrado como sobra "em montagem" na Fila da Solda.
    var gravada = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo);
    await Garantir(gravada);
    var doGravado = await CorpoAsync(gravada);
    Assert.Equal(Linhas(previa), Linhas(doGravado));
    Assert.Equal(new[] { (c.B, 1m), (c.C, 1m) }, await SobrasEmMontagemAsync(c));

    // 4. O Retrabalho dessa baixa recebe o resto: o "Usar a sobra" oferece 1 B e 1 C na Solda.
    var numero = (await CorpoAsync(await pcp.GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString();
    var aberto = await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos",
        new { perdaId = doGravado.GetProperty("id").GetInt32(), numero = $"{numero}-R1", dataEntrega = SemAtraso, motivoRetrabalho = "Perda" });
    await Garantir(aberto);
    var retrabalhoId = (await CorpoAsync(aberto)).GetProperty("id").GetInt32();
    var (b, cc) = (await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));
    var oferecido = (await CorpoAsync(await pcp.GetAsync($"/api/pedidos/{retrabalhoId}/sobra-reaproveitavel")))
        .GetProperty("itens").EnumerateArray()
        .Select(i => (i.GetProperty("estruturaItemId").GetInt32(), i.GetProperty("posicao").GetString(),
            i.GetProperty("setorId").GetInt32(), i.GetProperty("sobra").GetDecimal()))
        .ToList();
    Assert.Equal([(b, "AguardandoMontagem", c.Solda, 1m), (cc, "AguardandoMontagem", c.Solda, 1m)], oferecido);

    // 5. O original fabrica e entrega os outros 7 conjuntos e monta as 9 que ficaram.
    await EntregarConjuntosAsync(c, 7m);
    var inicio = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 9m });

    Assert.Equal(HttpStatusCode.Created, inicio.StatusCode);
    Assert.Equal(0m, await SaldoAsync(c, c.A, "AIniciar"));   // nada mais a montar: 9 iniciadas e 1 perdida
    Assert.Equal(9m, await SaldoAsync(c, c.A, "NoSetor"));
    Assert.Equal(new[] { (c.B, 1m), (c.C, 1m) }, await SobrasEmMontagemAsync(c));
  }
}
