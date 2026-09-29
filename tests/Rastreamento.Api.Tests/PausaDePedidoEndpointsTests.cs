using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Pausar e retomar um Pedido pelo HTTP (spec da Fase 3D, secoes 2.5 e 4.5): quem pausa, o que a
/// pausa recusa e o que deixa passar, e o codigo de cada conflito. O 403 por perfil esta em
/// `ExecucaoEndpointsTests.Perfil_sem_a_acao_recebe_403`.
/// </summary>
public class PausaDePedidoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public PausaDePedidoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Gestao_pausa_o_operador_nao_inicia_mas_termina_e_depois_de_retomar_inicia()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 5m }));

    var pausa = await c.Como(c.Gestao).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "outro pedido urgente" });
    var iniciarPausado = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m });
    var terminarPausado = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/terminos", new { setorId = c.Corte, ordem = 1, quantidade = 5m });
    var pedido = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}"));
    var numeroDoPedido = pedido.GetProperty("numero").GetString();
    var retomada = await c.Como(c.Gestao).PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null);
    var iniciarDepois = await operador.PostAsJsonAsync($"/api/estrutura/{c.B}/inicios", new { setorId = c.Corte, quantidade = 1m });

    Assert.Equal(HttpStatusCode.Created, pausa.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, iniciarPausado.StatusCode);
    var recusa = await CorpoAsync(iniciarPausado);
    Assert.Equal("PedidoPausado", recusa.GetProperty("erro").GetString());
    Assert.Equal($"O Pedido {numeroDoPedido} está pausado.", recusa.GetProperty("mensagem").GetString());
    Assert.Equal(HttpStatusCode.Created, terminarPausado.StatusCode);
    Assert.Equal("outro pedido urgente", pedido.GetProperty("pausa").GetProperty("motivo").GetString());
    Assert.Equal(HttpStatusCode.OK, retomada.StatusCode);
    var corpoDaRetomada = await CorpoAsync(retomada);
    Assert.Equal(c.Gestao.Id, corpoDaRetomada.GetProperty("retomadoPorUsuarioId").GetInt32());
    Assert.Equal("Usuario de Teste", corpoDaRetomada.GetProperty("retomadoPorNome").GetString());
    Assert.NotEqual(JsonValueKind.Null, corpoDaRetomada.GetProperty("retomadoEm").ValueKind);
    Assert.Equal(HttpStatusCode.Created, iniciarDepois.StatusCode);

    // O Iniciar depois da retomada usa `PausadoAsync`, outra consulta. As telas leem a pausa por
    // `PausasAbertas.ListarAsync`: sem o filtro de pausa aberta, o Pedido retomado seguiria pausado
    // no detalhe e na fila.
    var pedidoDepois = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}"));
    Assert.Equal(JsonValueKind.Null, pedidoDepois.GetProperty("pausa").ValueKind);
    var filaDepois = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Corte}/fila"));
    var linhasDeB = new[] { "aIniciar", "emTrabalho", "aguardandoColeta" }
        .SelectMany(secao => filaDepois.GetProperty(secao).EnumerateArray())
        .Where(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B)
        .ToList();
    Assert.NotEmpty(linhasDeB);
    Assert.All(linhasDeB, l => Assert.Equal(JsonValueKind.Null, l.GetProperty("no").GetProperty("pausa").ValueKind));
  }

  [Fact]
  public async Task Pausar_retomar_e_pausar_de_novo_deixa_a_fila_e_o_pedido_com_uma_so_pausa()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "primeira" }));
    await Garantir(await pcp.PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null));
    await Garantir(await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "segunda" }));

    // Duas linhas de `dbo.PedidoPausa` do mesmo Pedido (uma fechada, uma aberta): sem o filtro de
    // pausa aberta, o dicionario por Pedido receberia a chave duas vezes e a fila daria 500.
    var fila = await c.Como(c.Operador).GetAsync($"/api/setores/{c.Corte}/fila");
    var pedido = await c.Como(c.Operador).GetAsync($"/api/pedidos/{c.PedidoId}");

    Assert.Equal(HttpStatusCode.OK, fila.StatusCode);
    Assert.Equal(HttpStatusCode.OK, pedido.StatusCode);
    var linha = (await CorpoAsync(fila)).GetProperty("aIniciar").EnumerateArray()
        .Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    Assert.Equal("segunda", linha.GetProperty("no").GetProperty("pausa").GetProperty("motivo").GetString());
    Assert.Equal("segunda", (await CorpoAsync(pedido)).GetProperty("pausa").GetProperty("motivo").GetString());
  }

  [Fact]
  public async Task Pausar_duas_vezes_e_retomar_sem_pausa_dao_409_com_o_codigo()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);

    var retomarSemPausa = await pcp.PostAsync($"/api/pedidos/{c.PedidoId}/retomada", null);
    await Garantir(await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = (string?)null }));
    var pausarDeNovo = await pcp.PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = (string?)null });

    Assert.Equal("PedidoNaoPausado", (await CorpoAsync(retomarSemPausa)).GetProperty("erro").GetString());
    Assert.Equal("PedidoJaPausado", (await CorpoAsync(pausarDeNovo)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task A_fila_marca_o_no_de_Pedido_pausado()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    await Garantir(await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = "urgente" }));

    var fila = await CorpoAsync(await c.Como(c.Operador).GetAsync($"/api/setores/{c.Corte}/fila"));
    var linha = fila.GetProperty("aIniciar").EnumerateArray().Single(l => l.GetProperty("no").GetProperty("id").GetInt32() == c.B);

    Assert.Equal("urgente", linha.GetProperty("no").GetProperty("pausa").GetProperty("motivo").GetString());
  }

  [Fact]
  public async Task Motivo_com_mais_de_200_caracteres_da_400()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);

    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/pausas", new { motivo = new string('a', 201) });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Pedido_inexistente_da_404_na_pausa_e_na_retomada()
  {
    await using var c = await CenarioDaFase3NaApi.CriarAsync(_factory);
    var pcp = c.Como(c.Pcp);

    Assert.Equal(HttpStatusCode.NotFound, (await pcp.PostAsJsonAsync("/api/pedidos/999999/pausas", new { motivo = (string?)null })).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await pcp.PostAsync("/api/pedidos/999999/retomada", null)).StatusCode);
  }
}
