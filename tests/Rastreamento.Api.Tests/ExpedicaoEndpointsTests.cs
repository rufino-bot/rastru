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
    Assert.Equal("AguardandoConserto", ida.GetProperty("origem").GetProperty("posicao").GetString());
    Assert.Equal(c.Dobra, ida.GetProperty("origem").GetProperty("setorId").GetInt32());
    Assert.Equal(JsonValueKind.Null, ida.GetProperty("origem").GetProperty("ordem").ValueKind);
    Assert.Equal("EmConserto", ida.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(c.Dobra, ida.GetProperty("destino").GetProperty("setorId").GetInt32());
    Assert.Equal(1m, ida.GetProperty("quantidade").GetDecimal());
    Assert.Empty(await FilaDeConsertoAsync());

    // A origem da tarefa volta como esta na entrega (cada tarefa serve de corpo para a sua).
    var origemDaIda = ida.GetProperty("origem");
    var entrega = await movimentador.PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new
        {
          estruturaItemId = peca,
          origem = new { posicao = origemDaIda.GetProperty("posicao").GetString(), setorId = origemDaIda.GetProperty("setorId").GetInt32(), ordem = (int?)null },
          destinoSetorId = (int?)null, quantidade = 1m,
        },
      },
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

    // No inexistente sem `ordem` continua 404 (nunca 500).
    var inexistente = await c.Como(c.Operador).PostAsJsonAsync("/api/terminos", new
    {
      setorId = c.Dobra, itens = new[] { new { estruturaItemId = 2_000_000_000, ordem = (int?)null, quantidade = 1m, conserto = false } },
    });
    Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);

    var lote = await c.Como(c.Operador).PostAsJsonAsync("/api/terminos", corpo);
    Assert.Equal(HttpStatusCode.Created, lote.StatusCode);
    Assert.Equal("ConsertoPronto", (await CorpoAsync(lote))[0].GetProperty("destino").GetProperty("posicao").GetString());
  }

  [Fact]
  public async Task Gestao_responde_o_pedido_da_Qualidade()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    var gestao = c.Como(c.Gestao);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));

    var semJustificativa = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao", new { quantidade = 2m, justificativa = "  " });
    Assert.Equal(HttpStatusCode.BadRequest, semJustificativa.StatusCode);
    Assert.Equal("JustificativaObrigatoria", (await CorpoAsync(semJustificativa)).GetProperty("erro").GetString());

    var pedido = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao",
        new { quantidade = 2m, justificativa = "Aba da dobra 1,5 mm fora; o cliente não usa." });
    Assert.Equal(HttpStatusCode.Created, pedido.StatusCode);
    var corpoDoPedido = await CorpoAsync(pedido);
    var pedidoId = corpoDoPedido.GetProperty("id").GetInt32();
    Assert.Equal("Pedido", corpoDoPedido.GetProperty("tipo").GetString());
    Assert.Equal(JsonValueKind.Null, corpoDoPedido.GetProperty("pedidoDeLiberacaoId").ValueKind);
    Assert.Equal(peca, corpoDoPedido.GetProperty("estruturaItemId").GetInt32());
    Assert.Equal(2m, corpoDoPedido.GetProperty("quantidade").GetDecimal());
    Assert.Equal("Aba da dobra 1,5 mm fora; o cliente não usa.", corpoDoPedido.GetProperty("texto").GetString());
    Assert.Equal(c.Qualidade.Id, corpoDoPedido.GetProperty("usuarioId").GetInt32());
    Assert.False(corpoDoPedido.GetProperty("estornada").GetBoolean());
    Assert.True(corpoDoPedido.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", corpoDoPedido.GetProperty("dataHora").GetString());

    // No maximo um em aberto por Peca (C18).
    var outro = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao", new { quantidade = 1m, justificativa = "outro" });
    Assert.Equal(HttpStatusCode.Conflict, outro.StatusCode);
    Assert.Equal("LiberacaoEmAberto", (await CorpoAsync(outro)).GetProperty("erro").GetString());

    var resposta = await gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 1m, observacao = (string?)null });
    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpoDaResposta = await CorpoAsync(resposta);
    var respostaId = corpoDaResposta.GetProperty("id").GetInt32();
    Assert.Equal("Resposta", corpoDaResposta.GetProperty("tipo").GetString());
    Assert.Equal(pedidoId, corpoDaResposta.GetProperty("pedidoDeLiberacaoId").GetInt32());
    Assert.Equal(1m, corpoDaResposta.GetProperty("quantidade").GetDecimal());
    Assert.Equal(JsonValueKind.Null, corpoDaResposta.GetProperty("texto").ValueKind);
    Assert.Equal(c.Gestao.Id, corpoDaResposta.GetProperty("usuarioId").GetInt32());

    // Respondido, nao se responde de novo; e o id da resposta nao e pedido.
    var segunda = await gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 1m });
    Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    Assert.Equal("LiberacaoNaoPendente", (await CorpoAsync(segunda)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.NotFound,
        (await gestao.PostAsJsonAsync($"/api/liberacoes/{respostaId}/resposta", new { quantidadeLiberada = 0m })).StatusCode);

    // 4 conformes, 1 liberada e 1 de volta a nao conforme (C5).
    var posicoes = (await CorpoAsync(await qualidade.GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
        .EnumerateArray().Single(p => p.GetProperty("estruturaItemId").GetInt32() == peca)
        .GetProperty("saldos").EnumerateArray()
        .Select(s => (s.GetProperty("posicao").GetString()!, s.GetProperty("quantidade").GetDecimal()))
        .OrderBy(x => x.Item1, StringComparer.Ordinal).ToArray();
    Assert.Equal(new[] { ("Conforme", 4m), ("Liberada", 1m), ("NaoConforme", 1m) }, posicoes);

    // O livro do no: o pedido aponta o pedido, as duas da resposta apontam a resposta.
    var livro = (await CorpoAsync(await qualidade.GetAsync($"/api/estrutura/{peca}/movimentacoes")))
        .GetProperty("movimentacoes").EnumerateArray()
        .Where(m => m.GetProperty("liberacaoId").ValueKind != JsonValueKind.Null)
        .Select(m => (m.GetProperty("tipo").GetString()!, m.GetProperty("liberacaoId").GetInt32()))
        .OrderBy(x => x.Item1, StringComparer.Ordinal).ToArray();
    Assert.Equal(new[] { ("Liberacao", respostaId), ("PedidoDeLiberacao", pedidoId), ("Recusa", respostaId) }, livro);
  }

  [Fact]
  public async Task Qualidade_nao_responde()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));
    var pedido = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao",
        new { quantidade = 2m, justificativa = "Fora de cota, o cliente aceita." });
    await Garantir(pedido);
    var pedidoId = (await CorpoAsync(pedido)).GetProperty("id").GetInt32();

    foreach (var usuario in new[] { c.Qualidade, c.Pcp, c.Operador, c.Movimentador })
      Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(usuario).PostAsJsonAsync(
          $"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 2m })).StatusCode);

    // E o pedido e so da Qualidade: a Gestao responde, nao pede.
    foreach (var usuario in new[] { c.Gestao, c.Pcp, c.Operador })
      Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(usuario).PostAsJsonAsync(
          $"/api/estrutura/{peca}/pedidos-de-liberacao", new { quantidade = 1m, justificativa = "x" })).StatusCode);
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

  [Fact]
  public async Task Qualidade_estorna_a_propria_contagem()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    var afericao = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" });
    await Garantir(afericao);
    var afericaoId = (await CorpoAsync(afericao)).GetProperty("id").GetInt32();
    var contagem = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m });
    await Garantir(contagem);
    var contagemId = (await CorpoAsync(contagem)).GetProperty("id").GetInt32();

    // A aferição nao conforme depende da contagem: enquanto ela vale, nao se estorna.
    var recusada = await qualidade.PostAsync($"/api/afericoes/{afericaoId}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
    Assert.Equal("EstornoImpossivel", (await CorpoAsync(recusada)).GetProperty("erro").GetString());

    // Alheia: a Gestao passa pelo [Authorize], mas nao e a autora (403 do caso de uso, com corpo).
    var alheia = await c.Como(c.Gestao).PostAsync($"/api/contagens/{contagemId}/estorno", null);
    Assert.Equal(HttpStatusCode.Forbidden, alheia.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(alheia)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(c.Operador).PostAsync($"/api/contagens/{contagemId}/estorno", null)).StatusCode);

    // A linha de uma contagem nao se estorna sozinha (C17); a contagem, inteira, sim.
    var medicao = (await CorpoAsync(await qualidade.GetAsync($"/api/estrutura/{peca}/movimentacoes")))
        .GetProperty("movimentacoes").EnumerateArray()
        .First(m => m.GetProperty("tipo").GetString() == "Medicao");
    var generico = await qualidade.PostAsync($"/api/movimentacoes/{medicao.GetProperty("id").GetInt32()}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, generico.StatusCode);

    var estorno = await qualidade.PostAsync($"/api/contagens/{contagemId}/estorno", null);
    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    var linhas = (await CorpoAsync(estorno)).EnumerateArray().ToList();
    Assert.Equal(2, linhas.Count);
    Assert.All(linhas, l =>
    {
      Assert.Equal("Estorno", l.GetProperty("tipo").GetString());
      Assert.Equal(contagemId, l.GetProperty("contagemId").GetInt32());
      Assert.Equal("NaExpedicao", l.GetProperty("destino").GetProperty("posicao").GetString());
    });
    var segundo = await qualidade.PostAsync($"/api/contagens/{contagemId}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    Assert.Equal("JaEstornado", (await CorpoAsync(segundo)).GetProperty("erro").GetString());

    // Sem contagem valida, a aferição nao conforme se estorna; e a PCP estorna a de outro.
    var daAfericao = await c.Como(c.Pcp).PostAsync($"/api/afericoes/{afericaoId}/estorno", null);
    Assert.Equal(HttpStatusCode.Created, daAfericao.StatusCode);
    var corpoDaAfericao = await CorpoAsync(daAfericao);
    Assert.Equal(afericaoId, corpoDaAfericao.GetProperty("id").GetInt32());
    Assert.True(corpoDaAfericao.GetProperty("estornada").GetBoolean());
    Assert.False(corpoDaAfericao.GetProperty("estornavel").GetBoolean());
  }

  [Fact]
  public async Task Gestao_estorna_a_propria_resposta()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, pedidoId) = await PedidoDeLiberacaoAsync(c);
    var gestao = c.Como(c.Gestao);
    var resposta = await gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 1m });
    await Garantir(resposta);
    var respostaId = (await CorpoAsync(resposta)).GetProperty("id").GetInt32();

    // O pedido com resposta valida nao se estorna; e a resposta e da Gestao, nao da Qualidade.
    var pedidoComResposta = await c.Como(c.Qualidade).PostAsync($"/api/liberacoes/{pedidoId}/estorno", null);
    Assert.Equal(HttpStatusCode.Conflict, pedidoComResposta.StatusCode);
    Assert.Equal("EstornoImpossivel", (await CorpoAsync(pedidoComResposta)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Forbidden, (await c.Como(c.Qualidade).PostAsync($"/api/liberacoes/{respostaId}/estorno", null)).StatusCode);

    var estorno = await gestao.PostAsync($"/api/liberacoes/{respostaId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    var linhas = (await CorpoAsync(estorno)).EnumerateArray().ToList();
    Assert.Equal(2, linhas.Count);   // a liberada e a recusa
    Assert.All(linhas, l => Assert.Equal(respostaId, l.GetProperty("liberacaoId").GetInt32()));
    // O pedido voltou a pendente: a Gestao responde de novo.
    var outra = await gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 0m });
    Assert.Equal(HttpStatusCode.Created, outra.StatusCode);
    Assert.Equal(peca, (await CorpoAsync(outra)).GetProperty("estruturaItemId").GetInt32());
  }

  [Fact]
  public async Task Qualidade_estorna_o_proprio_conserto_pela_rota_generica()
  {
    await using var c = await CriarAsync(_factory);
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));
    var conserto = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/consertos", new { setorId = c.Dobra, quantidade = 1m });
    await Garantir(conserto);
    var consertoId = (await CorpoAsync(conserto)).GetProperty("id").GetInt32();

    var estorno = await qualidade.PostAsync($"/api/movimentacoes/{consertoId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    Assert.Equal("NaoConforme", (await CorpoAsync(estorno)).GetProperty("destino").GetProperty("posicao").GetString());
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Resposta_e_estorno_do_pedido_ao_mesmo_tempo_nao_deixam_resposta_pendurada(bool respostaSaiPrimeiro)
  {
    // Review Focus 2: a Gestao responde ao mesmo tempo em que a Qualidade estorna o pedido. A trava da Peca
    // serializa as duas; quem chega depois ve o que a outra fez. A ordem de disparo varia entre os dois casos.
    await using var c = await CriarAsync(_factory);
    var (peca, pedidoId) = await PedidoDeLiberacaoAsync(c);
    var gestao = c.Como(c.Gestao);
    var qualidade = c.Como(c.Qualidade);
    Task<HttpResponseMessage> Responder() =>
        gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 1m });
    Task<HttpResponseMessage> Estornar() => qualidade.PostAsync($"/api/liberacoes/{pedidoId}/estorno", null);

    var disparos = respostaSaiPrimeiro
        ? new[] { Responder(), Estornar() }
        : new[] { Estornar(), Responder() };
    var respostas = await Task.WhenAll(disparos);

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
    Assert.Contains((await CorpoAsync(recusada)).GetProperty("erro").GetString(),
        new[] { "LiberacaoNaoPendente", "EstornoImpossivel" });

    // No banco: se o pedido foi estornado, nenhuma resposta a ele tem linha valida.
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var linhas = await db.Movimentacoes.AsNoTracking().Where(m => m.LiberacaoId != null && m.EstruturaItemId == peca).ToListAsync();
    var estornadas = linhas.Where(l => l.EstornoDeId != null).Select(l => l.EstornoDeId!.Value).ToHashSet();
    var idsDasRespostas = await db.Liberacoes.AsNoTracking()
        .Where(l => l.PedidoDeLiberacaoId == pedidoId).Select(l => l.Id).ToListAsync();
    var pedidoEstornado = linhas.Any(l => l.LiberacaoId == pedidoId && l.Tipo != "Estorno" && estornadas.Contains(l.Id));
    var respostaValida = linhas.Any(l => l.LiberacaoId is int id && idsDasRespostas.Contains(id)
        && l.Tipo != "Estorno" && !estornadas.Contains(l.Id));
    Assert.False(pedidoEstornado && respostaValida, "resposta com linha valida num pedido estornado");
  }

  // ---------------------------------------------------------------- leituras (secao 5.1 e C20)

  [Fact]
  public async Task Lista_da_expedicao_traz_a_Peca_no_contrato_e_e_de_todo_perfil()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, pedidoId) = await PedidoDeLiberacaoAsync(c);

    // Sem `Roles`: o Operador tambem le.
    var resposta = await c.Como(c.Operador).GetAsync("/api/expedicao");
    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var pedido = (await CorpoAsync(resposta)).GetProperty("pedidos").EnumerateArray()
        .Single(p => p.GetProperty("id").GetInt32() == c.PedidoId);
    Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", pedido.GetProperty("dataEntrega").GetString());
    Assert.False(pedido.GetProperty("atrasado").GetBoolean());
    Assert.Equal(JsonValueKind.Null, pedido.GetProperty("pausa").ValueKind);

    var item = pedido.GetProperty("pecas").EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == peca);
    Assert.Equal("Chassi da Fase 3", item.GetProperty("descricao").GetString());
    Assert.Equal(c.AgrupamentoId, item.GetProperty("agrupamentoId").GetInt32());
    Assert.Equal(6m, item.GetProperty("quantidade").GetDecimal());
    Assert.True(item.GetProperty("requerRelatorioDimensional").GetBoolean());
    Assert.Equal("NaoConforme", item.GetProperty("situacao").GetString());
    Assert.Equal("NaoConforme", item.GetProperty("afericao").GetProperty("resultado").GetString());
    var posicoes = item.GetProperty("posicoes");
    Assert.Equal(new[] { 0m, 4m, 0m, 2m, 0m, 0m, 0m },
        new[] { "naExpedicao", "conforme", "naoConforme", "aguardandoLiberacao", "liberada", "emConserto", "expedido" }
            .Select(n => posicoes.GetProperty(n).GetDecimal()));
    Assert.False(posicoes.TryGetProperty("expedivel", out _));
    Assert.Equal(4m, item.GetProperty("expedivel").GetDecimal());
    Assert.Equal(0, item.GetProperty("consertos").GetArrayLength());
    var pendente = item.GetProperty("liberacaoPendente");
    Assert.Equal((pedidoId, 2m),
        (pendente.GetProperty("id").GetInt32(), pendente.GetProperty("quantidade").GetDecimal()));
    Assert.False(string.IsNullOrWhiteSpace(pendente.GetProperty("usuarioNome").GetString()));
  }

  [Fact]
  public async Task Historico_da_Peca_vem_mais_recente_primeiro_e_marca_estornavel_pelo_leitor()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, pedidoId) = await PedidoDeLiberacaoAsync(c);

    var comoQualidade = await c.Como(c.Qualidade).GetAsync($"/api/estrutura/{peca}/expedicao");
    Assert.Equal(HttpStatusCode.OK, comoQualidade.StatusCode);
    var historico = (await CorpoAsync(comoQualidade)).EnumerateArray().ToList();
    Assert.Equal(new[] { "PedidoDeLiberacao", "Contagem", "Afericao" }, historico.Select(h => h.GetProperty("tipo").GetString()));
    Assert.Equal(pedidoId, historico[0].GetProperty("id").GetInt32());
    Assert.Equal(2m, historico[0].GetProperty("quantidade").GetDecimal());
    Assert.Equal(4m, historico[1].GetProperty("conformes").GetDecimal());
    Assert.Equal(2m, historico[1].GetProperty("naoConformes").GetDecimal());
    Assert.Equal("NaoConforme", historico[2].GetProperty("resultado").GetString());
    Assert.All(historico, h => Assert.True(h.GetProperty("estornavel").GetBoolean()));
    Assert.All(historico, h => Assert.EndsWith("-03:00", h.GetProperty("dataHora").GetString()));

    var comoOperador = await c.Como(c.Operador).GetAsync($"/api/estrutura/{peca}/expedicao");
    Assert.All((await CorpoAsync(comoOperador)).EnumerateArray(), h => Assert.False(h.GetProperty("estornavel").GetBoolean()));

    Assert.Equal(HttpStatusCode.NotFound, (await c.Como(c.Qualidade).GetAsync("/api/estrutura/0/expedicao")).StatusCode);
  }

  [Fact]
  public async Task Pendentes_trazem_o_pedido_ate_a_Gestao_responder()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, pedidoId) = await PedidoDeLiberacaoAsync(c);
    var gestao = c.Como(c.Gestao);

    async Task<JsonElement[]> NossosAsync()
    {
      var resposta = await gestao.GetAsync("/api/liberacoes/pendentes");
      Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
      return (await CorpoAsync(resposta)).EnumerateArray().Where(p => p.GetProperty("id").GetInt32() == pedidoId).ToArray();
    }

    var pendente = Assert.Single(await NossosAsync());
    Assert.Equal(2m, pendente.GetProperty("quantidade").GetDecimal());
    Assert.Contains("Aba da dobra", pendente.GetProperty("justificativa").GetString());
    Assert.False(string.IsNullOrWhiteSpace(pendente.GetProperty("usuarioNome").GetString()));
    Assert.Equal((peca, "Chassi da Fase 3"),
        (pendente.GetProperty("peca").GetProperty("id").GetInt32(), pendente.GetProperty("peca").GetProperty("descricao").GetString()));
    Assert.Equal(c.PedidoId, pendente.GetProperty("pedido").GetProperty("id").GetInt32());
    Assert.False(pendente.GetProperty("pedido").GetProperty("atrasado").GetBoolean());

    await Garantir(await gestao.PostAsJsonAsync($"/api/liberacoes/{pedidoId}/resposta", new { quantidadeLiberada = 1m }));

    Assert.Empty(await NossosAsync());
  }

  [Fact]
  public async Task Remessas_recentes_trazem_a_remessa_com_as_linhas_e_o_estornavel_do_leitor()
  {
    await using var c = await CriarAsync(_factory);
    var (peca, _) = await PedidoDeLiberacaoAsync(c);
    var expedida = await c.Como(c.Qualidade).PostAsJsonAsync("/api/remessas",
        new { itens = new[] { new { estruturaItemId = peca, quantidade = 3m } } });
    await Garantir(expedida);
    var remessaId = (await CorpoAsync(expedida)).GetProperty("remessa").GetProperty("id").GetInt32();

    async Task<JsonElement> NossaAsync(UsuarioDeTeste quem)
    {
      var resposta = await c.Como(quem).GetAsync("/api/remessas/recentes");
      Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
      return (await CorpoAsync(resposta)).EnumerateArray().Single(r => r.GetProperty("id").GetInt32() == remessaId);
    }

    var remessa = await NossaAsync(c.Qualidade);
    Assert.Equal(c.Qualidade.Id, remessa.GetProperty("usuarioId").GetInt32());
    Assert.False(string.IsNullOrWhiteSpace(remessa.GetProperty("usuarioNome").GetString()));
    Assert.False(remessa.GetProperty("estornada").GetBoolean());
    Assert.True(remessa.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", remessa.GetProperty("dataHora").GetString());
    var linha = Assert.Single(remessa.GetProperty("linhas").EnumerateArray());
    Assert.Equal((peca, "Chassi da Fase 3", c.PedidoId, "Conforme", 3m),
        (linha.GetProperty("pecaId").GetInt32(), linha.GetProperty("pecaDescricao").GetString(),
            linha.GetProperty("pedidoId").GetInt32(), linha.GetProperty("origem").GetString(), linha.GetProperty("quantidade").GetDecimal()));
    Assert.False(linha.GetProperty("pedidoConcluido").GetBoolean());

    Assert.False((await NossaAsync(c.Operador)).GetProperty("estornavel").GetBoolean());
  }

  /// <summary>Aferição nao conforme, contagem de 4 e 2 e um pedido de liberacao de 2: devolve a Peca e o pedido.</summary>
  private static async Task<(int Peca, int PedidoId)> PedidoDeLiberacaoAsync(CenarioDaFase3NaApi c)
  {
    var peca = await PecaNaExpedicaoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/afericoes", new { resultado = "NaoConforme" }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/contagens", new { conformes = 4m, naoConformes = 2m }));
    var pedido = await qualidade.PostAsJsonAsync($"/api/estrutura/{peca}/pedidos-de-liberacao",
        new { quantidade = 2m, justificativa = "Aba da dobra 1,5 mm fora; o cliente não usa." });
    await Garantir(pedido);
    return (peca, (await CorpoAsync(pedido)).GetProperty("id").GetInt32());
  }
}
