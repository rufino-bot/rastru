using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// O comportamento de cada rota da Fase 3 pelo HTTP: o `TipoDeErro` virando o status, o corpo
/// `{ erro, mensagem }` inteiro, e o 403 do caso de uso (estorno alheio) distinto do 403 de perfil.
/// </summary>
public partial class ExecucaoEndpointsTests
{
  [Fact]
  public async Task Iniciar_devolve_201_com_o_movimento_e_poe_o_Pedido_em_producao()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 8m });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("Inicio", corpo.GetProperty("tipo").GetString());
    Assert.Equal("NoSetor", corpo.GetProperty("destino").GetProperty("posicao").GetString());
    Assert.Equal(c.Corte, corpo.GetProperty("destino").GetProperty("setorId").GetInt32());
    Assert.Equal(1, corpo.GetProperty("destino").GetProperty("ordem").GetInt32());
    Assert.Equal(c.Operador.Id, corpo.GetProperty("usuarioId").GetInt32());
    Assert.False(corpo.GetProperty("estornada").GetBoolean());
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal("EmProducao", (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == c.PedidoId)).Status);
  }

  [Fact]
  public async Task Saldo_insuficiente_devolve_409_com_codigo_e_mensagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 21m });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("SaldoInsuficiente", corpo.GetProperty("erro").GetString());
    Assert.Contains("Só há 20 de Suporte", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Quantidade_invalida_devolve_400_com_codigo()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 0.00005m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("QuantidadeInvalida", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task No_inexistente_devolve_404()
  {
    // So o status: `NotFound()` num `[ApiController]` pode ganhar corpo ProblemDetails do ASP.NET, e o
    // contrato desta fase nao promete corpo no 404 — promete que nao ha `erro` para o front comutar.
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync("/api/estrutura/999999/inicios", new { setorId = c.Corte, quantidade = 1m });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Estorno_alheio_devolve_403_Proibido_com_corpo_e_o_PCP_estorna()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var inicio = await CorpoAsync(await c.Como(c.Operador).PostAsJsonAsync(
        $"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 5m }));
    var id = inicio.GetProperty("id").GetInt32();

    var alheio = await c.Como(c.Movimentador).PostAsync($"/api/movimentacoes/{id}/estorno", null);
    var doPcp = await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{id}/estorno", null);

    Assert.Equal(HttpStatusCode.Forbidden, alheio.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(alheio)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.Created, doPcp.StatusCode);
    Assert.Equal(id, (await CorpoAsync(doPcp)).GetProperty("estornoDeId").GetInt32());
  }

  [Fact]
  public async Task Entrega_em_lista_devolve_201_e_as_tarefas_acompanham()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 20m }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 20m }));

    var antes = await TarefasDoCenarioAsync(c);
    var entrega = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade = 12m },
        new { estruturaItemId = c.B, origem = new { posicao = "AguardandoColeta", setorId = c.Corte, ordem = (int?)1 }, destinoSetorId = (int?)c.Pintura, quantidade = 8m },
      },
    });
    var depois = await TarefasDoCenarioAsync(c);

    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    Assert.Equal(2, (await CorpoAsync(entrega)).GetArrayLength());
    var tarefa = Assert.Single(antes);
    Assert.Equal("Montagem", tarefa.GetProperty("destino").GetProperty("tipo").GetString());
    Assert.Equal(c.Solda, tarefa.GetProperty("destino").GetProperty("sugestaoSetorId").GetInt32());
    Assert.Empty(depois);
  }

  [Fact]
  public async Task Montar_e_estornar_a_montagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await LevarParaAMontagemAsync(c, c.B, c.Corte, 6m);
    await LevarParaAMontagemAsync(c, c.C, c.Dobra, 3m);

    var montagem = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/montagens", new { setorId = c.Solda, quantidade = 3m });
    var montagemId = (await CorpoAsync(montagem)).GetProperty("id").GetInt32();
    var fila = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/setores/{c.Solda}/fila"));
    var estorno = await c.Como(c.Operador).PostAsync($"/api/montagens/{montagemId}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, montagem.StatusCode);
    Assert.Equal(2, (await CorpoAsync(montagem)).GetProperty("baixas").GetArrayLength());
    Assert.Empty(fila.GetProperty("aguardandoMontagem").EnumerateArray());   // montou tudo o que havia
    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    Assert.Equal(2, (await CorpoAsync(estorno)).GetArrayLength());
  }

  [Fact]
  public async Task Roteiro_marca_o_alcancado_e_recusa_mexer_nele()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.A}/inicios", new { setorId = c.Solda, quantidade = 1m }));

    var lido = await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/estrutura/{c.A}/roteiro"));
    var recusa = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Corte, c.Pintura } });
    var aceito = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Solda, c.Dobra, c.Pintura } });

    Assert.Equal(new[] { true, false },
        lido.GetProperty("passos").EnumerateArray().Select(p => p.GetProperty("alcancado").GetBoolean()).ToArray());
    Assert.Equal(HttpStatusCode.Conflict, recusa.StatusCode);
    Assert.Equal("PassoJaAlcancado", (await CorpoAsync(recusa)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.OK, aceito.StatusCode);
    Assert.Equal(3, (await CorpoAsync(aceito)).GetProperty("passos").GetArrayLength());
  }

  [Fact]
  public async Task Reduzir_a_quantidade_abaixo_do_que_andou_devolve_409_com_mensagem()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 15m }));

    var resposta = await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.B}",
        new { descricao = "Suporte", quantidade = 10m, quantidadePorPai = 2m });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    Assert.Equal("QuantidadeAbaixoDoMovimentado", corpo.GetProperty("erro").GetString());
    Assert.Contains("15", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Excluir_no_de_Pedido_em_producao_continua_PedidoNaoAberto()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m }));

    var resposta = await c.Como(c.Pcp).DeleteAsync($"/api/estrutura/{c.C}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    Assert.Equal("PedidoNaoAberto", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Gestao_le_fila_posicoes_livro_e_roteiro()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var gestao = c.Como(c.Gestao);

    foreach (var rota in new[]
    {
      $"/api/setores/{c.Corte}/fila", $"/api/agrupamentos/{c.AgrupamentoId}/posicoes",
      $"/api/estrutura/{c.B}/movimentacoes", $"/api/estrutura/{c.B}/roteiro",
    })
      Assert.Equal(HttpStatusCode.OK, (await gestao.GetAsync(rota)).StatusCode);
  }

  /// <summary>Iniciar, terminar e entregar para a montagem de A na Solda — o arranjo de montar.</summary>
  private static async Task LevarParaAMontagemAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
    await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new
    {
      itens = new[]
      {
        new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)1 }, destinoSetorId = (int?)c.Solda, quantidade },
      },
    }));
  }

  /// <summary>As tarefas SO deste cenario: `GET /tarefas` e global, e o banco de dev e compartilhado.</summary>
  private static async Task<List<JsonElement>> TarefasDoCenarioAsync(CenarioDaFase3NaApi c)
  {
    var nos = new[] { c.A, c.B, c.C };
    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));
    return tarefas.EnumerateArray()
        .SelectMany(s => s.GetProperty("itens").EnumerateArray())
        .Where(i => nos.Contains(i.GetProperty("no").GetProperty("id").GetInt32()))
        .ToList();
  }
}
