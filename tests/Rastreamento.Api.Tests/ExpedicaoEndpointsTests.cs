using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// As rotas da expedicao (spec da Fase 5A, secoes 5.1 e 5.2) no cenario da Fase 3: uma Peca a mais no
/// Agrupamento do cenario, copiada do catalogo, de 6, que exige relatorio, levada ao local de expedicao pelas
/// rotas que existem (iniciar, terminar, entregar). Toda asserção escopada nessa Peca.
/// </summary>
public class ExpedicaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public ExpedicaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  /// <summary>A Peca nova, no Corte, de 6, com as 6 em `NaExpedicao`. Sai no `DisposeAsync` do cenario (pelo Agrupamento).</summary>
  private static async Task<int> PecaNaExpedicaoAsync(CenarioDaFase3NaApi c)
  {
    var pcp = c.Como(c.Pcp);
    var criada = await pcp.PostAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
        new { componenteId = c.ComponenteId, quantidade = 6m, requerRelatorioDimensional = true });
    await Garantir(criada);
    var peca = (await CorpoAsync(criada)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{peca}/roteiro", new { passos = new[] { c.Corte } }));

    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/inicios", new { setorId = c.Corte, quantidade = 6m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{peca}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 6m }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade = 6m } },
    }));
    return peca;
  }

  [Fact]
  public async Task Qualidade_afere_conta_e_manda_para_conserto()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);

    var afericao = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" });
    Assert.Equal(HttpStatusCode.Created, afericao.StatusCode);
    var corpoDaAfericao = await CorpoAsync(afericao);
    Assert.Equal("NaoConforme", corpoDaAfericao.GetProperty("resultado").GetString());
    Assert.Equal(c.Qualidade.Id, corpoDaAfericao.GetProperty("usuarioId").GetInt32());
    Assert.False(corpoDaAfericao.GetProperty("estornada").GetBoolean());
    Assert.True(corpoDaAfericao.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", corpoDaAfericao.GetProperty("dataHora").GetString());

    // Uma afericao por Peca (D3): a segunda e 409 com o codigo, e a frase nomeia a Peca pelo Componente.
    var segunda = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "Conforme" });
    Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    var corpoDaSegunda = await CorpoAsync(segunda);
    Assert.Equal("AfericaoJaRegistrada", corpoDaSegunda.GetProperty("erro").GetString());
    Assert.Contains("Chassi da Fase 3", corpoDaSegunda.GetProperty("mensagem").GetString());

    var contagem = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m });
    Assert.Equal(HttpStatusCode.Created, contagem.StatusCode);
    var corpoDaContagem = await CorpoAsync(contagem);
    var contagemId = corpoDaContagem.GetProperty("id").GetInt32();
    Assert.Equal(4m, corpoDaContagem.GetProperty("conformes").GetDecimal());
    Assert.Equal(2m, corpoDaContagem.GetProperty("naoConformes").GetDecimal());
    Assert.Equal(c.Qualidade.Id, corpoDaContagem.GetProperty("usuarioId").GetInt32());

    var conserto = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/consertos", new { setorId = c.Dobra, quantidade = 1m });
    Assert.Equal(HttpStatusCode.Created, conserto.StatusCode);
    var corpoDoConserto = await CorpoAsync(conserto);
    Assert.Equal("Conserto", corpoDoConserto.GetProperty("tipo").GetString());
    Assert.Equal("NaoConforme", corpoDoConserto.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal("AguardandoConserto", corpoDoConserto.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(c.Dobra, corpoDoConserto.GetProperty("destino").GetProperty("setorId").GetInt32());
    Assert.Equal(JsonValueKind.Null, corpoDoConserto.GetProperty("contagemId").ValueKind);
    Assert.Equal(JsonValueKind.Null, corpoDoConserto.GetProperty("liberacaoId").ValueKind);
    Assert.Equal(JsonValueKind.Null, corpoDoConserto.GetProperty("remessaId").ValueKind);

    // As posicoes da Peca: 4 conformes, 1 nao conforme aguardando decisao, 1 aguardando o Movimentador.
    var posicoes = (await CorpoAsync(await qualidade.GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
        .EnumerateArray().Single(p => p.GetProperty("estruturaItemId").GetInt32() == peca)
        .GetProperty("saldos").EnumerateArray()
        .Select(s => (
            s.GetProperty("posicao").GetString()!,
            s.GetProperty("setorId").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("setorId").GetInt32(),
            s.GetProperty("quantidade").GetDecimal()))
        .ToArray();
    Assert.Equal(new[] { ("AguardandoConserto", (int?)c.Dobra, 1m), ("Conforme", null, 4m), ("NaoConforme", null, 1m) }, posicoes);

    // O livro do no mostra as duas linhas da contagem apontando-a.
    var livro = (await CorpoAsync(await qualidade.GetAsync($"/api/estrutura/{peca}/movimentacoes")))
        .GetProperty("movimentacoes").EnumerateArray()
        .Where(m => m.GetProperty("tipo").GetString() == "Medicao")
        .ToList();
    Assert.Equal(2, livro.Count);
    Assert.All(livro, m => Assert.Equal(contagemId, m.GetProperty("contagemId").GetInt32()));
  }

  [Fact]
  public async Task Conserto_ida_e_volta_pela_API()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    var movimentador = c.Como(c.Movimentador);
    var operador = c.Como(c.Operador);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/consertos", new { setorId = c.Dobra, quantidade = 1m }));

    async Task<JsonElement[]> TarefasDeConsertoAsync() =>
        (await CorpoAsync(await movimentador.GetAsync("/api/tarefas"))).GetProperty("consertos").EnumerateArray()
            .Where(t => t.GetProperty("no").GetProperty("id").GetInt32() == peca).ToArray();

    async Task<JsonElement[]> FilaDeConsertoAsync() =>
        (await CorpoAsync(await operador.GetAsync($"/api/setores/{c.Dobra}/fila"))).GetProperty("conserto").EnumerateArray()
            .Where(l => l.GetProperty("no").GetProperty("id").GetInt32() == peca).ToArray();

    async Task<decimal> NaExpedicaoAsync() =>
        (await CorpoAsync(await qualidade.GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
            .EnumerateArray().Single(p => p.GetProperty("estruturaItemId").GetInt32() == peca)
            .GetProperty("saldos").EnumerateArray()
            .Where(s => s.GetProperty("posicao").GetString() == "NaExpedicao")
            .Select(s => s.GetProperty("quantidade").GetDecimal()).SingleOrDefault();

    // A ida: o Movimentador a ve nas Tarefas, com o destino calculado, e a entrega ao Setor.
    var ida = Assert.Single(await TarefasDeConsertoAsync());
    Assert.Equal("NaExpedicao", ida.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal("EmConserto", ida.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(c.Dobra, ida.GetProperty("destino").GetProperty("setorId").GetInt32());
    Assert.Equal(1m, ida.GetProperty("quantidade").GetDecimal());
    Assert.Empty(await FilaDeConsertoAsync());

    var entrega = await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoConserto", setorId = c.Dobra, ordem = (int?)null }, destinoSetorId = (int?)null, quantidade = 1m } },
    });
    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    Assert.Equal("EmConserto", (await CorpoAsync(entrega))[0].GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Empty(await TarefasDeConsertoAsync());

    // O Operador a ve na Fila do Setor e termina o conserto.
    var naFila = Assert.Single(await FilaDeConsertoAsync());
    Assert.Equal("EmConserto", naFila.GetProperty("posicao").GetString());
    Assert.Equal(1m, naFila.GetProperty("quantidade").GetDecimal());
    var termino = await operador.PostAsJsonAsync($"/api/estrutura/{peca}/terminos-de-conserto", new { setorId = c.Dobra, quantidade = 1m });
    Assert.Equal(HttpStatusCode.Created, termino.StatusCode);
    var corpoDoTermino = await CorpoAsync(termino);
    Assert.Equal("Termino", corpoDoTermino.GetProperty("tipo").GetString());
    Assert.Equal("EmConserto", corpoDoTermino.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal("ConsertoPronto", corpoDoTermino.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal("ConsertoPronto", Assert.Single(await FilaDeConsertoAsync()).GetProperty("posicao").GetString());

    // A volta: o Movimentador a leva ao local de expedicao, e a unidade volta a ter la.
    var volta = Assert.Single(await TarefasDeConsertoAsync());
    Assert.Equal("ConsertoPronto", volta.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal(c.Dobra, volta.GetProperty("origem").GetProperty("setorId").GetInt32());
    Assert.Equal("NaExpedicao", volta.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(0m, await NaExpedicaoAsync());
    var trazida = await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "ConsertoPronto", setorId = c.Dobra, ordem = (int?)null }, destinoSetorId = (int?)null, quantidade = 1m } },
    });
    Assert.Equal(HttpStatusCode.Created, trazida.StatusCode);
    Assert.Equal("NaExpedicao", (await CorpoAsync(trazida))[0].GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(1m, await NaExpedicaoAsync());
    Assert.Empty(await TarefasDeConsertoAsync());
    Assert.Empty(await FilaDeConsertoAsync());
  }

  [Fact]
  public async Task Conserto_no_lote_de_termino_da_API_e_so_do_Operador()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/consertos", new { setorId = c.Dobra, quantidade = 2m }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[] { new { estruturaItemId = peca, origem = new { posicao = "AguardandoConserto", setorId = c.Dobra, ordem = (int?)null }, destinoSetorId = (int?)null, quantidade = 2m } },
    }));
    var corpo = new { setorId = c.Dobra, itens = new[] { new { estruturaItemId = peca, ordem = (int?)null, quantidade = 2m, conserto = true } } };

    var proibidos = new[] { c.Movimentador, c.Qualidade, c.Pcp };
    foreach (var usuario in proibidos)
    {
      Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(usuario).PostAsJsonAsync("/api/terminos", corpo)).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(usuario).PostAsJsonAsync(
          $"/api/estrutura/{peca}/terminos-de-conserto", new { setorId = c.Dobra, quantidade = 2m })).StatusCode);
    }

    // Com `ordem` o item de conserto e 400 OrigemInvalida; sem `ordem` e sem `conserto`, tambem.
    var comOrdem = await c.Como(c.Operador).PostAsJsonAsync("/api/terminos", new
    {
      setorId = c.Dobra, itens = new[] { new { estruturaItemId = peca, ordem = (int?)1, quantidade = 2m, conserto = true } },
    });
    Assert.Equal(HttpStatusCode.BadRequest, comOrdem.StatusCode);
    Assert.Equal("OrigemInvalida", (await CorpoAsync(comOrdem)).GetProperty("erro").GetString());
    var semOrdem = await c.Como(c.Operador).PostAsJsonAsync("/api/terminos", new
    {
      setorId = c.Dobra, itens = new[] { new { estruturaItemId = peca, ordem = (int?)null, quantidade = 2m, conserto = false } },
    });
    Assert.Equal(HttpStatusCode.BadRequest, semOrdem.StatusCode);
    Assert.Equal("OrigemInvalida", (await CorpoAsync(semOrdem)).GetProperty("erro").GetString());

    var lote = await c.Como(c.Operador).PostAsJsonAsync("/api/terminos", corpo);
    Assert.Equal(HttpStatusCode.Created, lote.StatusCode);
    Assert.Equal("ConsertoPronto", (await CorpoAsync(lote))[0].GetProperty("destino").GetProperty("posicao").GetString());
  }

  [Fact]
  public async Task Operador_nao_afere()
  {
    await using var c = await CriarAsync(_factory);

    var afericao = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/afericoes", new { resultado = "Conforme" });
    var contagem = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/contagens", new { conformes = 1m, naoConformes = 0m });
    var conserto = await c.Como(c.Movimentador).PostAsJsonAsync($"/api/estrutura/{c.A}/consertos", new { setorId = c.Dobra, quantidade = 1m });

    Assert.Equal(HttpStatusCode.Forbidden, afericao.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, contagem.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, conserto.StatusCode);
  }
}
