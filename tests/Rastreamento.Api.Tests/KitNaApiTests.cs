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
  public async Task Entrega_de_Kit_alem_do_que_o_pai_precisa_da_409_AlemDoQueOPaiPrecisa()
  {
    // Os filhos tem mais que Quantidade x razao do pai (sobra de refugo, legitima pela regra 26): B 22 e C 11 fecham
    // 11 conjuntos, e o pai (10) so precisa de 10. Cada filho esta dentro do que o Roteiro dele permite iniciar.
    await using var c = await CriarAsync(_factory, kit: true);
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}",
        new { descricao = "Suporte", quantidade = 22m, quantidadePorPai = 2m }));
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.C}",
        new { descricao = "Calço", quantidade = 11m, quantidadePorPai = 1m }));
    await ProntoAsync(c, c.B, c.Corte, 22m);
    await ProntoAsync(c, c.C, c.Dobra, 11m);

    var resposta = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas",
        new { itens = new[] { DaColeta(c.B, c.Corte, 22m), DaColeta(c.C, c.Dobra, 11m) } });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("AlemDoQueOPaiPrecisa", corpo.GetProperty("erro").GetString());
    Assert.Contains("só precisa receber 10 conjunto", corpo.GetProperty("mensagem").GetString());
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

  private static async Task<JsonElement> FilaAsync(CenarioDaFase3NaApi c, int setor) =>
      await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{setor}/fila"));

  /// <summary>As sobras "em montagem" dos nos deste cenario na Fila do Setor: (no, quantidade).</summary>
  private static (int No, decimal Quantidade)[] SobrasEmMontagem(CenarioDaFase3NaApi c, JsonElement fila) =>
      fila.GetProperty("sobra").EnumerateArray()
          .Where(l => l.GetProperty("origem").GetString() == "Montagem")
          .Select(l => (No: l.GetProperty("no").GetProperty("id").GetInt32(), Quantidade: l.GetProperty("quantidade").GetDecimal()))
          .Where(l => l.No == c.A || l.No == c.B || l.No == c.C)
          .ToArray();

  private static async Task<JsonElement> EntregarAsync(CenarioDaFase3NaApi c, params object[] itens)
  {
    var resposta = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new { itens });
    await Garantir(resposta);
    return await CorpoAsync(resposta);
  }

  [Fact]
  public async Task Espera_desalinhada_pela_troca_de_Avulso_para_Kit_nao_trava_o_Kit()
  {
    // C7 da spec da Fase 5C. B passa a 22 para os 18 que ficam a iniciar fecharem os 9 conjuntos do teto: e o que
    // separa o teto 9 (um conjunto completo a espera) do 8 (dois, contando pelo filho mais adiantado).
    await using var c = await CriarAsync(_factory, kit: true);
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}",
        new { descricao = "Suporte", quantidade = 22m, quantidadePorPai = 2m }));
    await Garantir(await pcp.PutAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}", new { codigo = "AG-01", tipo = "Avulso" }));
    await ProntoAsync(c, c.B, c.Corte, 4m);
    await ProntoAsync(c, c.C, c.Dobra, 1m);
    await EntregarAsync(c, DaColeta(c.B, c.Corte, 4m));   // o Avulso aceita cada filho sozinho
    await EntregarAsync(c, DaColeta(c.C, c.Dobra, 1m));

    await Garantir(await pcp.PutAsJsonAsync($"/api/agrupamentos/{c.AgrupamentoId}", new { codigo = "AG-01", tipo = "Kit" }));
    await ProntoAsync(c, c.B, c.Corte, 18m);
    await ProntoAsync(c, c.C, c.Dobra, 9m);

    var kit = KitDe(await TarefasAsync(c), "kitsMontaveis", c.A);
    Assert.NotNull(kit);
    Assert.Equal(9m, kit!.Value.GetProperty("conjuntos").GetDecimal());   // teto 10 - 1
    Assert.Equal(new[] { (c.B, 2m) }, SobrasEmMontagem(c, await FilaAsync(c, c.Solda)));
  }

  [Fact]
  public async Task Estorno_de_uma_linha_de_entrega_de_kit_deixa_o_irmao_como_sobra()
  {
    // C7 da spec da Fase 5C, e o risco aceito "Sobra transitoria no estorno" da secao 7.
    await using var c = await CriarAsync(_factory, kit: true);
    await ProntoAsync(c, c.B, c.Corte, 2m);
    await ProntoAsync(c, c.C, c.Dobra, 1m);
    var linhas = await EntregarAsync(c, DaColeta(c.B, c.Corte, 2m), DaColeta(c.C, c.Dobra, 1m));
    int LinhaDe(int no) => linhas.EnumerateArray()
        .Single(l => l.GetProperty("estruturaItemId").GetInt32() == no).GetProperty("id").GetInt32();
    var movimentador = c.Como(c.Movimentador);

    var inteiro = SobrasEmMontagem(c, await FilaAsync(c, c.Solda));
    await Garantir(await movimentador.PostAsync($"/api/movimentacoes/{LinhaDe(c.B)}/estorno", null));
    var semB = SobrasEmMontagem(c, await FilaAsync(c, c.Solda));
    await Garantir(await movimentador.PostAsync($"/api/movimentacoes/{LinhaDe(c.C)}/estorno", null));
    var semOsDois = SobrasEmMontagem(c, await FilaAsync(c, c.Solda));

    Assert.Empty(inteiro);
    Assert.Equal(new[] { (c.C, 1m) }, semB);
    Assert.Empty(semOsDois);
  }

  [Fact]
  public async Task Pai_kit_cujos_filhos_sairam_todos_aparece_a_iniciar_e_inicia_pela_Fila()
  {
    // C9 da spec da Fase 5C.
    await using var c = await CriarAsync(_factory, kit: true);
    var qualidade = c.Como(c.Qualidade);
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", new { motivo = "Descarte", tudo = true }));
    await Garantir(await qualidade.PostAsJsonAsync($"/api/estrutura/{c.C}/baixas", new { motivo = "Descarte", tudo = true }));

    var fila = await FilaAsync(c, c.Solda);
    var inicio = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 1m });

    var linha = fila.GetProperty("aIniciar").EnumerateArray().Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.A);
    Assert.Equal(10m, linha.GetProperty("quantidade").GetDecimal());
    Assert.Equal(HttpStatusCode.Created, inicio.StatusCode);
  }
}
