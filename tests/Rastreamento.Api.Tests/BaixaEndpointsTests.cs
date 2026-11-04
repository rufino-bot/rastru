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
/// A baixa de um no (secoes 4.1 a 4.6 e 5.1 a 5.4 da spec da Fase 5B-1) no cenario da Fase 3: Peca A (10, copiada do
/// catalogo, sem descricao propria, "Chassi da Fase 3"; Solda -> Pintura), B (20, razao 2, "Suporte"; Corte) e C (10,
/// razao 1, "Calço"; Dobra). Toda frase que nomeia um no usa A ou um no copiado do catalogo. Toda assercao escopada
/// nos nos e no Pedido do cenario.
/// </summary>
public class BaixaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public BaixaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static object Origem(string posicao, int? setorId, int? ordem, decimal quantidade) =>
      new { posicao, setorId, ordem, quantidade };

  private static object Parte(string motivo, params object[] origens) =>
      new { motivo, observacao = (string?)null, tudo = false, origens, incluirTopo = false };

  private static async Task IniciarAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade) =>
      await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));

  /// <summary>Iniciar, terminar e entregar para a montagem de A na Solda.</summary>
  private static async Task LevarParaAMontagemAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    await IniciarAsync(c, no, setor, quantidade);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)null, quantidade },
      },
    }));
  }

  /// <summary>Os saldos do no, por `GET /agrupamentos/{id}/posicoes`: (posicao, Setor, passo) -> quantidade.</summary>
  private static async Task<List<(string Posicao, int? SetorId, int? Ordem, decimal Quantidade)>> SaldosAsync(
      CenarioDaFase3NaApi c, int no) =>
      (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/agrupamentos/{c.AgrupamentoId}/posicoes")))
          .EnumerateArray()
          .Single(p => p.GetProperty("estruturaItemId").GetInt32() == no)
          .GetProperty("saldos").EnumerateArray()
          .Select(s => (
              s.GetProperty("posicao").GetString()!,
              s.GetProperty("setorId").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("setorId").GetInt32(),
              s.GetProperty("ordem").ValueKind == JsonValueKind.Null ? (int?)null : s.GetProperty("ordem").GetInt32(),
              s.GetProperty("quantidade").GetDecimal()))
          .ToList();

  private static async Task<decimal> SaldoAsync(CenarioDaFase3NaApi c, int no, string posicao) =>
      (await SaldosAsync(c, no)).Where(s => s.Posicao == posicao).Sum(s => s.Quantidade);

  /// <summary>As linhas da baixa como (no, posicao de origem, quantidade), na ordem da resposta.</summary>
  private static List<(int No, string Origem, decimal Quantidade)> Linhas(JsonElement corpo) =>
      corpo.GetProperty("linhas").EnumerateArray()
          .Select(l => (l.GetProperty("estruturaItemId").GetInt32(), l.GetProperty("origemPosicao").GetString()!,
              l.GetProperty("quantidade").GetDecimal()))
          .ToList();

  /// <summary>
  /// Todos os campos de cada linha. A quantidade pelo valor: a gravada volta do banco com quatro casas, e o texto do
  /// JSON (`3` contra `3.0000`) mudaria sem mudar o numero.
  /// </summary>
  private static List<string> LinhasInteiras(JsonElement corpo) =>
      corpo.GetProperty("linhas").EnumerateArray()
          .Select(l => string.Join("|",
              l.GetProperty("estruturaItemId").GetInt32(), l.GetProperty("descricao").GetString(),
              l.GetProperty("codigo").ToString(), l.GetProperty("origemPosicao").GetString(),
              l.GetProperty("origemSetorId").ToString(), l.GetProperty("origemSetorNome").ToString(),
              l.GetProperty("origemOrdem").ToString(), l.GetProperty("quantidade").GetDecimal().ToString("0.####")))
          .ToList();

  private async Task<int> PerdasDoPedidoAsync(CenarioDaFase3NaApi c)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Perdas.AsNoTracking().CountAsync(p => p.PedidoId == c.PedidoId);
  }

  private static async Task<(HttpStatusCode Status, string? Erro, string? Mensagem)> RecusaAsync(HttpResponseMessage resposta)
  {
    var texto = await resposta.Content.ReadAsStringAsync();
    if (string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null, null);
    var corpo = JsonDocument.Parse(texto).RootElement;
    return (resposta.StatusCode, corpo.GetProperty("erro").GetString(),
        corpo.TryGetProperty("mensagem", out var m) ? m.GetString() : null);
  }

  /// <summary>Um filho de A copiado do catalogo, sem descricao propria (o nome e "Chassi da Fase 3").</summary>
  private static async Task<int> FilhoDoCatalogoAsync(CenarioDaFase3NaApi c)
  {
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 10m, quantidadePorPai = 1m });
    await Garantir(criado);
    return (await CorpoAsync(criado)).GetProperty("id").GetInt32();
  }

  [Fact]
  public async Task Baixa_de_parte_grava_uma_Perda_e_as_linhas_e_devolve_201()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 5m);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", new
    {
      motivo = "MortaEmProcesso", observacao = "  Quebrou na serra  ", tudo = false,
      origens = new[] { Origem("NoSetor", c.Corte, 1, 2m) }, incluirTopo = false,
    });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.True(baixa.GetProperty("id").GetInt32() > 0);
    Assert.Equal("MortaEmProcesso", baixa.GetProperty("motivo").GetString());
    Assert.Equal("Quebrou na serra", baixa.GetProperty("observacao").GetString());
    Assert.Equal("No", baixa.GetProperty("escopo").GetProperty("tipo").GetString());
    Assert.Equal(c.B, baixa.GetProperty("escopo").GetProperty("id").GetInt32());
    Assert.False(baixa.GetProperty("tudo").GetBoolean());
    Assert.EndsWith("-03:00", baixa.GetProperty("dataHora").GetString());
    Assert.Equal(c.Qualidade.Id, baixa.GetProperty("usuarioId").GetInt32());
    Assert.Equal("Usuario de Teste", baixa.GetProperty("usuarioNome").GetString());
    Assert.False(baixa.GetProperty("estornada").GetBoolean());
    Assert.True(baixa.GetProperty("estornavel").GetBoolean());
    var linha = Assert.Single(baixa.GetProperty("linhas").EnumerateArray());
    Assert.Equal(c.B, linha.GetProperty("estruturaItemId").GetInt32());
    Assert.Equal("Suporte", linha.GetProperty("descricao").GetString());
    Assert.Equal(JsonValueKind.Null, linha.GetProperty("codigo").ValueKind);
    Assert.Equal("NoSetor", linha.GetProperty("origemPosicao").GetString());
    Assert.Equal(c.Corte, linha.GetProperty("origemSetorId").GetInt32());
    Assert.StartsWith("f3-", linha.GetProperty("origemSetorNome").GetString());
    Assert.EndsWith("-Corte", linha.GetProperty("origemSetorNome").GetString());
    Assert.Equal(1, linha.GetProperty("origemOrdem").GetInt32());
    Assert.Equal(2m, linha.GetProperty("quantidade").GetDecimal());

    Assert.Equal(2m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(3m, await SaldoAsync(c, c.B, "NoSetor"));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var perda = await db.Perdas.AsNoTracking().SingleAsync(p => p.PedidoId == c.PedidoId);
    Assert.Equal(c.B, perda.EstruturaItemId);
    Assert.Null(perda.AgrupamentoId);
    Assert.False(perda.Tudo);
    var linhaNoLivro = await db.Movimentacoes.AsNoTracking().SingleAsync(m => m.PerdaId == perda.Id);
    Assert.Equal(("Perda", "NoSetor", "Perdido"), (linhaNoLivro.Tipo, linhaNoLivro.OrigemPosicao, linhaNoLivro.DestinoPosicao));
  }

  [Fact]
  public async Task Baixa_de_parte_do_pai_tira_os_filhos_a_iniciar_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 3m)));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // A deixa de precisar de 3: B (razao 2) perde 6 a iniciar, C (razao 1) perde 3 (C6).
    Assert.Equal([(c.A, "AIniciar", 3m), (c.B, "AIniciar", 6m), (c.C, "AIniciar", 3m)], Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(6m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(14m, await SaldoAsync(c, c.B, "AIniciar"));
    Assert.Equal(7m, await SaldoAsync(c, c.C, "AIniciar"));
  }

  [Fact]
  public async Task Baixa_tudo_de_uma_Peca_leva_a_arvore_e_conclui_a_Peca()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 4m);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        new { motivo = "Descarte", tudo = true });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixa = await CorpoAsync(resposta);
    Assert.True(baixa.GetProperty("tudo").GetBoolean());
    Assert.Equal(
        [(c.A, "AIniciar", 10m), (c.B, "AIniciar", 16m), (c.B, "NoSetor", 4m), (c.C, "AIniciar", 10m)],
        Linhas(baixa));
    Assert.Equal([("Perdido", (int?)null, (int?)null, 10m)], await SaldosAsync(c, c.A));
    Assert.Equal([("Perdido", (int?)null, (int?)null, 20m)], await SaldosAsync(c, c.B));

    // A Peca A e a unica do Pedido: perdida inteira, ela conclui, e o Pedido com ela (regra 13, C10).
    var pedido = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/pedidos/{c.PedidoId}"));
    Assert.Equal("Concluido", pedido.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.String, pedido.GetProperty("dataConclusao").ValueKind);
  }

  [Fact]
  public async Task Previa_devolve_o_mesmo_que_a_baixa_grava_e_nao_grava()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    var corpo = Parte("Descarte", Origem("AIniciar", null, null, 3m));
    var qualidade = c.Como(c.Qualidade);

    var previa = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", corpo);

    Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
    var doPrevisto = await CorpoAsync(previa);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
    Assert.Equal(0m, await SaldoAsync(c, c.A, "Perdido"));
    // B ja saiu todo de "a iniciar": nao perde nada, e as 6 que A deixa de pedir viram sobra (C6). C perde 3.
    Assert.Equal([(c.A, "AIniciar", 3m), (c.C, "AIniciar", 3m)], Linhas(doPrevisto));
    var sobra = Assert.Single(doPrevisto.GetProperty("viramSobra").EnumerateArray());
    Assert.Equal((c.B, "Suporte", 6m),
        (sobra.GetProperty("estruturaItemId").GetInt32(), sobra.GetProperty("descricao").GetString(), sobra.GetProperty("quantidade").GetDecimal()));
    Assert.Equal(JsonValueKind.Null, doPrevisto.GetProperty("travaNoTopo").ValueKind);
    var primeira = doPrevisto.GetProperty("linhas")[0];
    Assert.Equal("Chassi da Fase 3", primeira.GetProperty("descricao").GetString());
    Assert.StartsWith("f3-", primeira.GetProperty("codigo").GetString());

    var gravada = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, gravada.StatusCode);
    Assert.Equal(LinhasInteiras(doPrevisto), LinhasInteiras(await CorpoAsync(gravada)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task IncluirTopo_grava_a_Peca_do_topo_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 20m);
    var corpo = new
    {
      motivo = "PerdaArmazem", tudo = false, origens = new[] { Origem("NoSetor", c.Corte, 1, 4m) }, incluirTopo = true,
    };
    var qualidade = c.Como(c.Qualidade);

    // Sem as 4 de B, A so monta 8 das 10: a previa avisa que trava 2 (secao 4.6).
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa", corpo with { incluirTopo = false }));
    var trava = previa.GetProperty("travaNoTopo");
    Assert.Equal((c.A, "Chassi da Fase 3", 2m, 10m),
        (trava.GetProperty("pecaId").GetInt32(), trava.GetProperty("descricao").GetString(),
         trava.GetProperty("quantidade").GetDecimal(), trava.GetProperty("aIniciar").GetDecimal()));
    Assert.Equal([(c.B, "NoSetor", 4m)], Linhas(previa));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // As 2 de A saem de "a iniciar", e por C6 as 2 de C que elas pediam saem junto (decisao P10 do plano de backend da Fase 5B-1).
    Assert.Equal([(c.B, "NoSetor", 4m), (c.A, "AIniciar", 2m), (c.C, "AIniciar", 2m)], Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(2m, await SaldoAsync(c, c.A, "Perdido"));
  }

  [Fact]
  public async Task IncluirTopo_de_um_neto_sobe_a_cadeia_ate_a_Peca_e_desce_pelo_outro_ramo()
  {
    // Tres niveis: um neto N copiado do catalogo (20, razao 1) sob B. A cadeia de N e [B, A]; C so se alcanca pela
    // arvore da Peca do topo, nao pela de B (secao 4.6 da spec da Fase 5B-1).
    await using var c = await CriarAsync(_factory);
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 20m, quantidadePorPai = 1m });
    await Garantir(criado);
    var neto = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    var corpo = new
    {
      motivo = "PerdaArmazem", tudo = false, origens = new[] { Origem("AIniciar", null, null, 4m) }, incluirTopo = true,
    };
    var qualidade = c.Como(c.Qualidade);

    // Sem 4 de N, B so monta 16 das 20, e A (razao 2 de B) so monta 8 das 10: trava 2 de A, pela razao de cada nivel.
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{neto}/baixas/previa", corpo));
    var trava = previa.GetProperty("travaNoTopo");
    Assert.Equal((c.A, "Chassi da Fase 3", 2m, 10m),
        (trava.GetProperty("pecaId").GetInt32(), trava.GetProperty("descricao").GetString(),
         trava.GetProperty("quantidade").GetDecimal(), trava.GetProperty("aIniciar").GetDecimal()));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{neto}/baixas", corpo);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // As 2 de A saem de "a iniciar" e a cascata C6 desce da Peca pelos dois ramos: B deixa de precisar de 4, C de 2. N
    // ja perdeu as 4 que B deixa de pedir, entao nao perde mais nada.
    Assert.Equal(
        [(neto, "AIniciar", 4m), (c.A, "AIniciar", 2m), (c.B, "AIniciar", 4m), (c.C, "AIniciar", 2m)],
        Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(2m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Tudo_de_Item_por_perda_com_IncluirTopo_grava_a_Peca_do_topo_na_mesma_Perda()
  {
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 6m);
    var qualidade = c.Como(c.Qualidade);

    // D14 da spec da Fase 5B-1: sem B nenhum, A nao monta nada, e a previa diz que trava as 10.
    var previa = await CorpoAsync(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas/previa",
        new { motivo = "MortaEmProcesso", tudo = true }));
    Assert.Equal(10m, previa.GetProperty("travaNoTopo").GetProperty("quantidade").GetDecimal());

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        new { motivo = "MortaEmProcesso", tudo = true, incluirTopo = true });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    Assert.Equal(
        [(c.B, "AIniciar", 14m), (c.B, "NoSetor", 6m), (c.A, "AIniciar", 10m), (c.C, "AIniciar", 10m)],
        Linhas(await CorpoAsync(resposta)));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
    Assert.Equal(10m, await SaldoAsync(c, c.A, "Perdido"));
  }

  [Fact]
  public async Task Tudo_de_Item_por_Descarte_com_IncluirTopo_e_OrigemInvalida()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas",
        new { motivo = "Descarte", tudo = true, incluirTopo = true });

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "OrigemInvalida"), (status, erro));
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  public static TheoryData<string, string> CorposRecusadosNaEntrada => new()
  {
    { """{ "motivo": "Roubo", "tudo": true }""", "MotivoInvalido" },
    { """{ "tudo": true }""", "MotivoInvalido" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "Montado", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AguardandoLiberacao", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "setorId": 1, "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "tudo": true, "origens": [ { "posicao": "AIniciar", "quantidade": 1 } ] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [] }""", "OrigemInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 1 }, { "posicao": "AIniciar", "quantidade": 2 } ] }""", "ItemRepetido" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 0 } ] }""", "QuantidadeInvalida" },
    { """{ "motivo": "Descarte", "origens": [ { "posicao": "AIniciar", "quantidade": 0.00001 } ] }""", "QuantidadeInvalida" },
    { """{ "motivo": "Descarte", "incluirTopo": true, "origens": [ { "posicao": "AIniciar", "quantidade": 1 } ] }""", "OrigemInvalida" },
  };

  [Theory]
  [MemberData(nameof(CorposRecusadosNaEntrada))]
  public async Task Entrada_invalida_e_400_com_o_codigo_e_nada_grava(string json, string codigo)
  {
    await using var c = await CriarAsync(_factory);
    var qualidade = c.Como(c.Qualidade);

    foreach (var rota in new[] { $"/api/estrutura/{c.A}/baixas", $"/api/estrutura/{c.A}/baixas/previa" })
    {
      var resposta = await qualidade.PostAsync(rota, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
      var (status, erro, mensagem) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.BadRequest, codigo), (status, erro));
      Assert.False(string.IsNullOrWhiteSpace(mensagem));
    }
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Observacao_acima_de_500_e_TextoLongoDemais()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        new { motivo = "Descarte", observacao = new string('x', 501), tudo = true });

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "TextoLongoDemais"), (status, erro));
    // 500 cabe.
    var cabe = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa",
        new { motivo = "Descarte", observacao = new string('x', 500), tudo = true });
    Assert.Equal(HttpStatusCode.OK, cabe.StatusCode);
  }

  [Fact]
  public async Task Origens_acima_do_teto_do_lote_e_LoteGrandeDemais()
  {
    await using var c = await CriarAsync(_factory);
    var origens = Enumerable.Range(1, 101).Select(i => Origem("NoSetor", c.Corte, i, 1m)).ToArray();

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", Parte("Descarte", origens));

    var (status, erro, _) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.BadRequest, "LoteGrandeDemais"), (status, erro));
  }

  [Fact]
  public async Task Acima_do_saldo_da_origem_e_SaldoInsuficiente_nomeando_a_posicao()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas",
        Parte("PerdaArmazem", Origem("AIniciar", null, null, 11m)));

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));
    Assert.Equal("Só há 10 de Chassi da Fase 3 a iniciar.", mensagem);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Tudo_sem_nada_em_producao_e_NadaEmProducao()
  {
    await using var c = await CriarAsync(_factory);
    var filho = await FilhoDoCatalogoAsync(c);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{filho}/baixas", new { motivo = "Descarte", tudo = true }));

    var resposta = await qualidade.PostAsJsonAsync($"/api/estrutura/{filho}/baixas", new { motivo = "Descarte", tudo = true });

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "NadaEmProducao"), (status, erro));
    Assert.Equal("Chassi da Fase 3 não tem nada em produção para dar baixa.", mensagem);
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Pedido_Cancelado_e_PedidoFechado()
  {
    await using var c = await CriarAsync(_factory);
    using (var escopo = _factory.Services.CreateScope())
    {
      // Nenhuma rota leva a `Cancelado` (C20 da spec da Fase 5B-1): o status vai direto no banco.
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pedido SET Status = 'Cancelado' WHERE Id = {c.PedidoId}");
    }

    foreach (var rota in new[] { $"/api/estrutura/{c.B}/baixas", $"/api/estrutura/{c.B}/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true });
      var (status, erro, _) = await RecusaAsync(resposta);
      Assert.Equal((HttpStatusCode.Conflict, "PedidoFechado"), (status, erro));
    }
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task No_inexistente_e_404()
  {
    await using var c = await CriarAsync(_factory);

    foreach (var rota in new[] { "/api/estrutura/999999999/baixas", "/api/estrutura/999999999/baixas/previa" })
    {
      var resposta = await c.Como(c.Qualidade).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true });
      Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
      // 404 sem codigo no corpo, como o da execucao (`ExecucaoControllerBase.Recusar`).
      Assert.DoesNotContain("\"erro\"", await resposta.Content.ReadAsStringAsync());
    }
    // A rota existe: o 404 acima e do no, nao da rota.
    Assert.Equal(HttpStatusCode.OK,
        (await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", new { motivo = "Descarte", tudo = true })).StatusCode);
  }

  [Fact]
  public async Task Operador_e_Movimentador_nao_dao_baixa()
  {
    await using var c = await CriarAsync(_factory);

    foreach (var quem in new[] { c.Operador, c.Movimentador })
      foreach (var rota in new[] { $"/api/estrutura/{c.A}/baixas", $"/api/estrutura/{c.A}/baixas/previa" })
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.Como(quem).PostAsJsonAsync(rota, new { motivo = "Descarte", tudo = true })).StatusCode);

    // O PCP da baixa (D5).
    Assert.Equal(HttpStatusCode.OK,
        (await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas/previa", new { motivo = "Descarte", tudo = true })).StatusCode);
    Assert.Equal(0, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Estorno_generico_recusa_linha_de_baixa()
  {
    await using var c = await CriarAsync(_factory);
    var gravada = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.C}/baixas",
        Parte("Descarte", Origem("AIniciar", null, null, 1m)));
    await Garantir(gravada);
    var baixaId = (await CorpoAsync(gravada)).GetProperty("id").GetInt32();
    int linhaId;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      linhaId = await db.Movimentacoes.AsNoTracking().Where(m => m.PerdaId == baixaId).Select(m => m.Id).SingleAsync();
    }

    var resposta = await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{linhaId}/estorno", null);

    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "EstornoImpossivel"), (status, erro));
    Assert.Equal($"Esta linha faz parte da baixa {baixaId}: estorne a baixa inteira.", mensagem);
    Assert.Equal(1m, await SaldoAsync(c, c.C, "Perdido"));
  }

  [Fact]
  public async Task Duas_baixas_simultaneas_da_mesma_posicao_uma_passa_outra_e_SaldoInsuficiente()
  {
    // Review Focus 1: 3 de B no Corte, duas baixas de 2 ao mesmo tempo.
    await using var c = await CriarAsync(_factory);
    await IniciarAsync(c, c.B, c.Corte, 3m);
    var corpo = Parte("MortaEmProcesso", Origem("NoSetor", c.Corte, 1, 2m));

    var respostas = await Task.WhenAll(
        c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo),
        c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", corpo));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));
    Assert.Equal(2m, await SaldoAsync(c, c.B, "Perdido"));
    Assert.Equal(1m, await SaldoAsync(c, c.B, "NoSetor"));
    Assert.Equal(1, await PerdasDoPedidoAsync(c));
  }

  [Fact]
  public async Task Baixa_e_inicio_do_pai_simultaneos_nao_consomem_o_mesmo_a_iniciar()
  {
    // Review Focus 2: A tem 10 a iniciar e os filhos para montar as 10 na Solda. A baixa tira 6 de "a iniciar" e o
    // inicio consome 6 dali: as duas juntas passariam das 10.
    await using var c = await CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 20m);
    await LevarParaAMontagemAsync(c, c.C, c.Dobra, 10m);

    var respostas = await Task.WhenAll(
        c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", Parte("Descarte", Origem("AIniciar", null, null, 6m))),
        c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 6m }));

    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = Assert.Single(respostas, r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));

    Assert.Equal(4m, await SaldoAsync(c, c.A, "AIniciar"));
    var perdido = await SaldoAsync(c, c.A, "Perdido");
    var iniciado = await SaldoAsync(c, c.A, "NoSetor");
    Assert.Equal(6m, perdido + iniciado);
    Assert.True(perdido == 0m || iniciado == 0m);
    // O pai so consome os filhos das unidades que iniciou: nunca os das que a baixa tirou.
    Assert.Equal(2m * iniciado, await SaldoAsync(c, c.B, "Montado"));
    Assert.Equal(20m - 2m * iniciado, await SaldoAsync(c, c.B, "AguardandoMontagem"));
    Assert.Equal(iniciado, await SaldoAsync(c, c.C, "Montado"));
    Assert.Equal(10m - iniciado, await SaldoAsync(c, c.C, "AguardandoMontagem"));
  }
}
