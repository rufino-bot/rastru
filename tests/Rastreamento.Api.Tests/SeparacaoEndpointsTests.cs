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
/// As rotas da separacao de material (Fase 4) no cenario da Fase 3, com Materiais criados no banco por ele.
/// Toda asercao escopada no proprio cenario. Esta classe e estendida pelas rotas seguintes da separacao.
/// </summary>
public class SeparacaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public SeparacaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  [Fact]
  public async Task Pcp_substitui_e_le_os_materiais_do_no()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await c.CriarMaterialAsync("PAR");
    var chapa = await c.CriarMaterialAsync("CH", setorDeEstoqueId: c.Corte);
    var pcp = c.Como(c.Pcp);

    var put = await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/materiais",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 10m }, new { materialId = chapa, quantidade = 2.5m } } });

    Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    var corpo = await CorpoAsync(put);
    Assert.Equal(c.B, corpo.GetProperty("estruturaItemId").GetInt32());
    var itens = corpo.GetProperty("materiais").EnumerateArray().ToList();
    Assert.Equal(2, itens.Count);

    var doParafuso = itens.Single(i => i.GetProperty("materialId").GetInt32() == parafuso);
    Assert.Equal(JsonValueKind.Null, doParafuso.GetProperty("ficaEm").ValueKind);
    Assert.Equal(10m, doParafuso.GetProperty("planejado").GetDecimal());
    Assert.Equal(0m, doParafuso.GetProperty("separado").GetDecimal());
    Assert.Equal(10m, doParafuso.GetProperty("pendencia").GetDecimal());
    Assert.True(doParafuso.GetProperty("noPlanejado").GetBoolean());
    Assert.False(doParafuso.GetProperty("acimaDoPlanejado").GetBoolean());

    // Fica no Corte: nunca tem pendencia (D6), e o "Fica em" sai com o id e o nome do Setor.
    var daChapa = itens.Single(i => i.GetProperty("materialId").GetInt32() == chapa);
    Assert.Equal(c.Corte, daChapa.GetProperty("ficaEm").GetProperty("setorId").GetInt32());
    Assert.StartsWith("f3-", daChapa.GetProperty("ficaEm").GetProperty("nome").GetString());
    Assert.Equal(2.5m, daChapa.GetProperty("planejado").GetDecimal());
    Assert.Equal(0m, daChapa.GetProperty("pendencia").GetDecimal());

    var get = await pcp.GetAsync($"/api/estrutura/{c.B}/materiais");
    Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    var lido = (await CorpoAsync(get)).GetProperty("materiais").EnumerateArray()
        .Select(i => i.GetProperty("materialId").GetInt32()).ToHashSet();
    Assert.Equal(new HashSet<int> { parafuso, chapa }, lido);

    // Substituir de novo troca a lista inteira; `[]` limpa.
    var limpo = await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/materiais", new { linhas = Array.Empty<object>() });
    Assert.Equal(HttpStatusCode.OK, limpo.StatusCode);
    Assert.Empty((await CorpoAsync(limpo)).GetProperty("materiais").EnumerateArray());
  }

  [Theory]
  [InlineData("Operador")]
  [InlineData("Separador")]
  public async Task Operador_e_Separador_nao_substituem_materiais_do_no(string perfil)
  {
    await using var c = await CriarAsync(_factory);
    var cliente = perfil == "Operador" ? c.Como(c.Operador) : c.Como(c.Separador);

    var resposta = await cliente.PutAsJsonAsync($"/api/estrutura/{c.B}/materiais", new { linhas = Array.Empty<object>() });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Separador_le_os_materiais_do_no()
  {
    await using var c = await CriarAsync(_factory);

    var resposta = await c.Como(c.Separador).GetAsync($"/api/estrutura/{c.B}/materiais");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    Assert.Empty((await CorpoAsync(resposta)).GetProperty("materiais").EnumerateArray());
  }

  [Fact]
  public async Task Material_invalido_ou_no_inexistente_nao_grava_e_responde_o_status_certo()
  {
    await using var c = await CriarAsync(_factory);
    var inativo = await c.CriarMaterialAsync("INA", ativo: false);
    var pcp = c.Como(c.Pcp);

    var recusado = await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/materiais",
        new { linhas = new[] { new { materialId = inativo, quantidade = 1m } } });
    var semLinhas = await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/materiais", new { });
    var semNo = await pcp.PutAsJsonAsync("/api/estrutura/999999/materiais", new { linhas = Array.Empty<object>() });
    var leituraSemNo = await pcp.GetAsync("/api/estrutura/999999/materiais");

    Assert.Equal(HttpStatusCode.BadRequest, recusado.StatusCode);
    Assert.Equal("MaterialInvalido", (await CorpoAsync(recusado)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.BadRequest, semLinhas.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, semNo.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, leituraSemNo.StatusCode);
    Assert.Empty((await CorpoAsync(await pcp.GetAsync($"/api/estrutura/{c.B}/materiais"))).GetProperty("materiais").EnumerateArray());
  }

  // -------------------------------------------------------------------------
  // Separar, estornar e o historico do no
  // -------------------------------------------------------------------------

  /// <summary>Planeja `quantidade` de um Material novo do almoxarifado no no B, pelo PCP, e devolve o Id dele.</summary>
  private static async Task<int> PlanejarParafusoAsync(CenarioDaFase3NaApi c, decimal quantidade = 10m)
  {
    var parafuso = await c.CriarMaterialAsync("PAR");
    await Garantir(await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{c.B}/materiais",
        new { linhas = new[] { new { materialId = parafuso, quantidade } } }));
    return parafuso;
  }

  /// <summary>Uma separacao de `quantidade` do material no no B, por `quem`; devolve o Id da linha gravada.</summary>
  private static async Task<int> SepararAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int materialId, decimal quantidade)
  {
    var resposta = await c.Como(quem).PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes",
        new { linhas = new[] { new { materialId, quantidade } } });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("separacoes")[0].GetProperty("id").GetInt32();
  }

  [Fact]
  public async Task Separador_separa_e_le_o_historico()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separador = c.Como(c.Separador);

    var post = await separador.PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 12m } } });

    Assert.Equal(HttpStatusCode.Created, post.StatusCode);
    var corpo = await CorpoAsync(post);
    var linha = Assert.Single(corpo.GetProperty("separacoes").EnumerateArray());
    Assert.Equal("Separacao", linha.GetProperty("tipo").GetString());
    Assert.Equal(parafuso, linha.GetProperty("materialId").GetInt32());
    Assert.Equal(12m, linha.GetProperty("quantidade").GetDecimal());
    Assert.Equal(c.Separador.Id, linha.GetProperty("usuarioId").GetInt32());
    Assert.Equal("Usuario de Teste", linha.GetProperty("usuarioNome").GetString());
    Assert.Equal(JsonValueKind.Null, linha.GetProperty("estornoDeId").ValueKind);
    Assert.True(linha.GetProperty("estornavel").GetBoolean());
    // O horario sai no fuso de Brasilia (HorarioDeBrasiliaJsonConverter), como o resto da API.
    Assert.EndsWith("-03:00", linha.GetProperty("dataHora").GetString());
    var material = Assert.Single(corpo.GetProperty("materiais").EnumerateArray());
    Assert.Equal(12m, material.GetProperty("separado").GetDecimal());
    Assert.Equal(0m, material.GetProperty("pendencia").GetDecimal());
    Assert.True(material.GetProperty("acimaDoPlanejado").GetBoolean());

    var get = await separador.GetAsync($"/api/estrutura/{c.B}/separacoes");

    Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    var historico = (await CorpoAsync(get)).EnumerateArray().ToList();
    var lida = Assert.Single(historico);
    Assert.Equal(linha.GetProperty("id").GetInt32(), lida.GetProperty("id").GetInt32());
    Assert.True(lida.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", lida.GetProperty("dataHora").GetString());
    // Para outro Separador a mesma linha nao e estornavel.
    var deOutro = (await CorpoAsync(await c.Como(c.OutroSeparador).GetAsync($"/api/estrutura/{c.B}/separacoes"))).EnumerateArray();
    Assert.False(Assert.Single(deOutro).GetProperty("estornavel").GetBoolean());
  }

  [Fact]
  public async Task Separar_fora_do_planejado_e_409_e_no_inexistente_e_404()
  {
    await using var c = await CriarAsync(_factory);
    await PlanejarParafusoAsync(c);
    var fora = await c.CriarMaterialAsync("FORA");
    var separador = c.Como(c.Separador);

    var recusado = await separador.PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes",
        new { linhas = new[] { new { materialId = fora, quantidade = 1m } } });
    var semNo = await separador.PostAsJsonAsync("/api/estrutura/999999/separacoes",
        new { linhas = new[] { new { materialId = fora, quantidade = 1m } } });
    var semLinhas = await separador.PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes", new { });
    var vazia = await separador.PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes", new { linhas = Array.Empty<object>() });

    Assert.Equal(HttpStatusCode.Conflict, recusado.StatusCode);
    var corpoRecusado = await CorpoAsync(recusado);
    Assert.Equal("MaterialForaDoPlanejado", corpoRecusado.GetProperty("erro").GetString());
    // A frase nomeia o material (codigo) e o no (a descricao do B do cenario).
    Assert.Contains("-FORA", corpoRecusado.GetProperty("mensagem").GetString());
    Assert.Contains("do nó Suporte", corpoRecusado.GetProperty("mensagem").GetString());
    Assert.Equal(HttpStatusCode.NotFound, semNo.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, semLinhas.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, vazia.StatusCode);
    Assert.Equal("SeparacaoVazia", (await CorpoAsync(vazia)).GetProperty("erro").GetString());
    Assert.Empty((await CorpoAsync(await separador.GetAsync($"/api/estrutura/{c.B}/separacoes"))).EnumerateArray());
  }

  [Fact]
  public async Task Operador_nao_separa()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);

    var resposta = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{c.B}/separacoes",
        new { linhas = new[] { new { materialId = parafuso, quantidade = 1m } } });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Movimentador_nao_estorna_separacao()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separacao = await SepararAsync(c, c.Separador, parafuso, 2m);

    var resposta = await c.Como(c.Movimentador).PostAsync($"/api/separacoes/{separacao}/estorno", null);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Separador_nao_estorna_separacao_de_outro_Separador()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separacao = await SepararAsync(c, c.Separador, parafuso, 2m);

    var resposta = await c.Como(c.OutroSeparador).PostAsync($"/api/separacoes/{separacao}/estorno", null);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    Assert.Equal("Proibido", (await CorpoAsync(resposta)).GetProperty("erro").GetString());
    Assert.Single((await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/estrutura/{c.B}/separacoes"))).EnumerateArray());
  }

  [Fact]
  public async Task Pcp_estorna_separacao_de_outro()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separacao = await SepararAsync(c, c.Separador, parafuso, 2m);

    var resposta = await c.Como(c.Pcp).PostAsync($"/api/separacoes/{separacao}/estorno", null);

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var estorno = await CorpoAsync(resposta);
    Assert.Equal("Estorno", estorno.GetProperty("tipo").GetString());
    Assert.Equal(separacao, estorno.GetProperty("estornoDeId").GetInt32());
    Assert.Equal(c.Pcp.Id, estorno.GetProperty("usuarioId").GetInt32());
    Assert.Equal(2m, estorno.GetProperty("quantidade").GetDecimal());
    Assert.False(estorno.GetProperty("estornavel").GetBoolean());
    Assert.EndsWith("-03:00", estorno.GetProperty("dataHora").GetString());

    // O separado volta a zero e a pendencia ao planejado.
    var material = Assert.Single((await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/estrutura/{c.B}/materiais")))
        .GetProperty("materiais").EnumerateArray());
    Assert.Equal(0m, material.GetProperty("separado").GetDecimal());
    Assert.Equal(10m, material.GetProperty("pendencia").GetDecimal());
  }

  [Fact]
  public async Task Separador_estorna_a_propria_e_o_estorno_de_novo_e_409()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separacao = await SepararAsync(c, c.Separador, parafuso, 2m);
    var separador = c.Como(c.Separador);

    var estorno = await separador.PostAsync($"/api/separacoes/{separacao}/estorno", null);
    var deNovo = await separador.PostAsync($"/api/separacoes/{separacao}/estorno", null);
    var doEstorno = await separador.PostAsync(
        $"/api/separacoes/{(await CorpoAsync(estorno)).GetProperty("id").GetInt32()}/estorno", null);
    var inexistente = await separador.PostAsync("/api/separacoes/999999999/estorno", null);

    Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, deNovo.StatusCode);
    var corpoDeNovo = await CorpoAsync(deNovo);
    Assert.Equal("JaEstornada", corpoDeNovo.GetProperty("erro").GetString());
    Assert.Contains("-PAR", corpoDeNovo.GetProperty("mensagem").GetString());
    Assert.Contains("do nó Suporte", corpoDeNovo.GetProperty("mensagem").GetString());
    Assert.Equal(HttpStatusCode.Conflict, doEstorno.StatusCode);
    Assert.Equal("EstornoDeEstorno", (await CorpoAsync(doEstorno)).GetProperty("erro").GetString());
    Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
  }

  [Fact]
  public async Task Dois_estornos_simultaneos_da_mesma_separacao_dao_um_201_e_um_409()
  {
    // Review Focus 3: a corrida e fechada pelo indice unico UX_MaterialSeparacao_EstornoDe; nunca 500.
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    var separacao = await SepararAsync(c, c.Separador, parafuso, 2m);
    var separador = c.Como(c.Separador);
    var pcp = c.Como(c.Pcp);

    var respostas = await Task.WhenAll(
        separador.PostAsync($"/api/separacoes/{separacao}/estorno", null),
        pcp.PostAsync($"/api/separacoes/{separacao}/estorno", null));

    Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict],
        respostas.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray());
    var conflito = respostas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
    Assert.Equal("JaEstornada", (await CorpoAsync(conflito)).GetProperty("erro").GetString());

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal(1, await db.Separacoes.CountAsync(s => s.EstornoDeId == separacao));
  }

  // -------------------------------------------------------------------------
  // A lista do Separador
  // -------------------------------------------------------------------------

  [Fact]
  public async Task Pendencias_traz_o_no_do_cenario_com_destino_e_prazo()
  {
    await using var c = await CriarAsync(_factory);
    var parafuso = await PlanejarParafusoAsync(c);
    await SepararAsync(c, c.Separador, parafuso, 4m);

    var resposta = await c.Como(c.Separador).GetAsync("/api/separacoes/pendencias");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    // A lista e global: so o no B do cenario interessa (a asercao e escopada nele).
    var pendencia = Assert.Single((await CorpoAsync(resposta)).EnumerateArray().ToList(),
        p => p.GetProperty("no").GetProperty("id").GetInt32() == c.B);
    var no = pendencia.GetProperty("no");
    Assert.Equal("Suporte", no.GetProperty("descricao").GetString());
    Assert.Equal(JsonValueKind.Null, no.GetProperty("codigoDoComponente").ValueKind);
    Assert.Equal(c.AgrupamentoId, no.GetProperty("agrupamentoId").GetInt32());
    Assert.Equal("AG-01", no.GetProperty("agrupamentoCodigo").GetString());

    var pedido = pendencia.GetProperty("pedido");
    Assert.Equal(c.PedidoId, pedido.GetProperty("id").GetInt32());
    Assert.Equal("Cliente F3", pedido.GetProperty("cliente").GetString());
    // O DateOnly sai como "yyyy-MM-dd", como no PedidoDto.
    Assert.Equal("2026-10-22", pedido.GetProperty("dataEntrega").GetString());
    // O valor de `atrasado` depende do relogio real (o cenario fixa o prazo): a conta e provada com relogio fixo na
    // Application; aqui so o formato do campo.
    Assert.True(pedido.GetProperty("atrasado").ValueKind is JsonValueKind.True or JsonValueKind.False);
    Assert.Equal(JsonValueKind.Null, pedido.GetProperty("pausa").ValueKind);

    // O primeiro (e unico) passo do Roteiro de B e o Corte.
    var destino = pendencia.GetProperty("destino");
    Assert.Equal(c.Corte, destino.GetProperty("setorId").GetInt32());
    Assert.StartsWith("f3-", destino.GetProperty("nome").GetString());
    Assert.EndsWith("-Corte", destino.GetProperty("nome").GetString());

    var material = Assert.Single(pendencia.GetProperty("materiais").EnumerateArray());
    Assert.Equal(parafuso, material.GetProperty("materialId").GetInt32());
    Assert.Equal("UN", material.GetProperty("unidadeMedida").GetString());
    Assert.Equal((10m, 4m, 6m), (material.GetProperty("planejado").GetDecimal(),
        material.GetProperty("separado").GetDecimal(), material.GetProperty("pendencia").GetDecimal()));
  }

  [Fact]
  public async Task Pendencias_nao_traz_no_de_Pedido_concluido()
  {
    // Mata a mutacao "tirar o filtro de Pedido aberto": o filtro mora no ExecucaoRepository real, que o
    // fake da Application nao exercita.
    await using var c = await CriarAsync(_factory);
    await PlanejarParafusoAsync(c);
    var separador = c.Como(c.Separador);
    Assert.Contains((await CorpoAsync(await separador.GetAsync("/api/separacoes/pendencias"))).EnumerateArray(),
        p => p.GetProperty("no").GetProperty("id").GetInt32() == c.B);

    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Pedido SET Status = 'Concluido' WHERE Id = {c.PedidoId}");
    }

    var depois = await separador.GetAsync("/api/separacoes/pendencias");

    Assert.Equal(HttpStatusCode.OK, depois.StatusCode);
    Assert.DoesNotContain((await CorpoAsync(depois)).EnumerateArray(),
        p => p.GetProperty("no").GetProperty("id").GetInt32() == c.B);
  }

  [Fact]
  public async Task Pendencias_exige_autenticacao()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/separacoes/pendencias");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }
}
