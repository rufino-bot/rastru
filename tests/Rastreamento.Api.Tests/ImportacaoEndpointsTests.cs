using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta das rotas de criar, ler, listar e descartar o rascunho do import do BOM, contra o SQL
/// Server real (docker compose up -d). Cada teste cria o proprio Pedido, Agrupamento e Componentes (com
/// um prefixo unico) e a limpeza apaga os rascunhos pelo repositorio real (a ordem filhos, RaizId,
/// registros, cabecalho e arquivos e a dele), depois o resto na ordem das FKs. O CSV e sintetico e
/// montado no teste.
///
/// <para>
/// O QUE SO SE PROVA AQUI (o caso de uso tem cobertura propria em <c>ImportacaoDeEstruturaUseCaseTests</c>):
/// o multipart binding do <c>IFormFile</c>, o gravar e reler do rascunho pelo EF de verdade (o estado que
/// o <c>GET</c> devolve e o do <c>POST</c>), o <c>TipoDeErro</c> virando o STATUS certo, o corpo do
/// erro (<c>BomInvalido</c> com uma linha por erro), a autoria vinda da claim <c>sub</c> e o perfil.
/// </para>
/// </summary>
public class ImportacaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private const string Cabecalho = "N DO ITEM;N DA PECA;DESCRICAO;QTD";

  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _numerosCriados = [];
  private readonly List<int> _componentesCriados = [];

  public ImportacaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
    var pedidoIds = pedidos.Select(p => p.Id).ToList();
    var agrupamentoIds = await db.Agrupamentos
        .Where(a => pedidoIds.Contains(a.PedidoId)).Select(a => a.Id).ToListAsync();

    // Rascunho primeiro: tem FK para Agrupamento e, os registros casados, para Componente.
    var rascunhoIds = await db.ImportacoesDeEstrutura
        .Where(i => agrupamentoIds.Contains(i.AgrupamentoId)).Select(i => i.Id).ToListAsync();
    var repositorio = escopo.ServiceProvider.GetRequiredService<IImportacaoDeEstruturaRepository>();
    foreach (var id in rascunhoIds)
      await repositorio.ExcluirAsync(id, CancellationToken.None);

    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => pedidoIds.Contains(a.PedidoId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(await db.Componentes.Where(c => _componentesCriados.Contains(c.Id)).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(pedidos);
    await db.SaveChangesAsync();
  }

  /// <summary>
  /// O token leva o Id do <c>pcp</c> do seed, e nao o do <c>admin</c> (1): com o literal <c>1</c> no
  /// lugar da claim <c>sub</c> o autor coincidiria com o <c>admin</c> e a prova de autoria ficaria
  /// degenerada (mesmo cuidado de <c>AgrupamentosEndpointsTests</c>).
  /// </summary>
  private (int Id, string NomeCompleto) UsuarioReal()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var pcp = db.Usuarios.Single(u => u.NomeUsuario == "pcp");
    return (pcp.Id, pcp.NomeCompleto);
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", TokenDeTeste.Emitir(_factory, perfil, UsuarioReal().Id));
    return cliente;
  }

  private async Task<int> NovoAgrupamento(HttpClient cliente)
  {
    var numero = $"ped-{Guid.NewGuid():N}"[..25];
    _numerosCriados.Add(numero);
    var pedido = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    var pedidoId = JsonDocument.Parse(await pedido.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
    var agrupamento = await cliente.PostAsJsonAsync(
        $"/api/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", tipo = "Kit" });
    return JsonDocument.Parse(await agrupamento.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
  }

  /// <summary>Um Componente de catalogo, de codigo unico e em MAIUSCULAS (o CSV o cita em minusculas).</summary>
  private async Task<(int Id, string Codigo)> NovoComponente(string descricao = "Componente do catalogo")
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"IMP-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
      Descricao = descricao,
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return (c.Id, c.Codigo);
  }

  private static string Csv(params string[] linhas) => string.Join("\n", linhas.Prepend(Cabecalho));

  private static async Task<HttpResponseMessage> Enviar(
      HttpClient cliente, int agrupamentoId, string csv, string nomeDoArquivo = "conjunto.csv")
  {
    // `await` DENTRO do `using`: o MultipartFormDataContent precisa estar vivo enquanto o corpo e lido.
    using var corpo = new MultipartFormDataContent();
    var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
    corpo.Add(arquivo, "arquivo", nomeDoArquivo);
    return await cliente.PostAsync($"/api/agrupamentos/{agrupamentoId}/importacoes", corpo);
  }

  /// <summary>Igualdade estrutural de JSON, com os numeros comparados por valor (<c>2</c> e <c>2.0000</c> sao o mesmo).</summary>
  private static bool MesmoJson(JsonElement a, JsonElement b)
  {
    if (a.ValueKind != b.ValueKind) return false;
    switch (a.ValueKind)
    {
      case JsonValueKind.Object:
        var propriedades = a.EnumerateObject().ToList();
        return propriedades.Count == b.EnumerateObject().Count()
            && propriedades.All(p => b.TryGetProperty(p.Name, out var outro) && MesmoJson(p.Value, outro));
      case JsonValueKind.Array:
        var itens = a.EnumerateArray().ToList();
        var outros = b.EnumerateArray().ToList();
        return itens.Count == outros.Count && itens.Zip(outros).All(par => MesmoJson(par.First, par.Second));
      case JsonValueKind.Number:
        return a.GetDecimal() == b.GetDecimal();
      case JsonValueKind.String:
        return a.GetString() == b.GetString();
      default:
        return true;
    }
  }

  private static async Task<JsonElement> Json(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

  // ---------------------------------------------------------------- POST + GET

  [Fact]
  public async Task Post_cria_rascunho_e_get_devolve_o_mesmo_estado()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);
    var (componenteId, codigo) = await NovoComponente();
    var codigoNovo = $"NOVO-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
    var csv = Csv($"1;{codigo.ToLowerInvariant()};Peca do CAD;2", $"2;{codigoNovo};Peca nova;1,5");

    var criado = await Enviar(cliente, agrupamentoId, csv, "Suporte do motor.csv");

    Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    var textoDoPost = await criado.Content.ReadAsStringAsync();
    var corpo = JsonDocument.Parse(textoDoPost).RootElement;
    var id = corpo.GetProperty("id").GetInt32();
    Assert.Equal(agrupamentoId, corpo.GetProperty("agrupamentoId").GetInt32());
    Assert.Equal("Suporte do motor.csv", corpo.GetProperty("nomeDoArquivo").GetString());
    Assert.Equal(UsuarioReal().NomeCompleto, corpo.GetProperty("criadoPor").GetString());
    Assert.Equal(JsonValueKind.Null, corpo.GetProperty("quantidadeDaPeca").ValueKind);
    Assert.False(corpo.GetProperty("requerRelatorioDimensional").GetBoolean());
    // P14: a versao e o ROWVERSION (8 bytes) em base64.
    Assert.Equal(8, Convert.FromBase64String(corpo.GetProperty("versao").GetString()!).Length);

    // O casado pelo codigo em minusculas; o outro, novo, ja preenchido a partir do arquivo.
    var componentes = corpo.GetProperty("componentes").EnumerateArray().ToList();
    Assert.Equal(3, componentes.Count);
    var casado = componentes.Single(c => c.GetProperty("componenteId").ValueKind == JsonValueKind.Number);
    Assert.Equal(componenteId, casado.GetProperty("componenteId").GetInt32());
    Assert.Equal(codigo, casado.GetProperty("codigoDoCatalogo").GetString());
    var novo = componentes.Single(c => c.GetProperty("codigoLido").GetString() == codigoNovo);
    Assert.Equal((codigoNovo, "Peca nova", "Fabricado"),
        (novo.GetProperty("codigoNovo").GetString(), novo.GetProperty("descricaoNova").GetString(), novo.GetProperty("tipoNovo").GetString()));
    var raiz = componentes.Single(c => c.GetProperty("codigoLido").ValueKind == JsonValueKind.Null);
    Assert.Equal("Suporte do motor", raiz.GetProperty("descricaoLida").GetString());
    Assert.Equal("Montagem", raiz.GetProperty("tipoNovo").GetString());

    var filhos = corpo.GetProperty("raiz").GetProperty("filhos").EnumerateArray().ToList();
    Assert.Equal([codigo, codigoNovo], filhos.Select(f => f.GetProperty("codigo").GetString()));
    Assert.Equal([2m, 1.5m], filhos.Select(f => f.GetProperty("quantidadePorPai").GetDecimal()));
    Assert.Contains(corpo.GetProperty("bloqueios").EnumerateArray(),
        b => b.GetProperty("tipo").GetString() == "QuantidadeDaPecaAusente");

    // O GET reconstroi tudo do banco e tem de dar o mesmo estado que o POST devolveu. Compara o JSON por
    // valor: o POST ainda tem o `2` que o caso de uso montou, e o GET le `2.0000` da coluna DECIMAL(18,4).
    var lido = await cliente.GetAsync($"/api/importacoes/{id}");
    Assert.Equal(HttpStatusCode.OK, lido.StatusCode);
    var textoDoGet = await lido.Content.ReadAsStringAsync();
    Assert.True(MesmoJson(corpo, JsonDocument.Parse(textoDoGet).RootElement), $"POST: {textoDoPost}\nGET:  {textoDoGet}");
  }

  [Fact]
  public async Task Post_com_csv_invalido_responde_400_BomInvalido_com_mensagem_por_linha()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);

    var resposta = await Enviar(cliente, agrupamentoId, Csv("1;A-1;Peca A;abc", "2;B-1;Peca B;x"));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("BomInvalido", corpo.GetProperty("erro").GetString());
    var linhas = corpo.GetProperty("mensagem").GetString()!.Split('\n');
    Assert.Equal(2, linhas.Length);
    Assert.StartsWith("Linha 2: ", linhas[0]);
    Assert.StartsWith("Linha 3: ", linhas[1]);
    // Arquivo recusado nao deixa rascunho.
    var lista = await Json(await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/importacoes"));
    Assert.Empty(lista.EnumerateArray());
  }

  [Fact]
  public async Task Post_com_codigo_de_mais_de_50_caracteres_responde_400_BomInvalido_e_nao_500()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);

    var resposta = await Enviar(cliente, agrupamentoId, Csv($"1;{new string('X', 51)};Peca A;1"));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("BomInvalido", corpo.GetProperty("erro").GetString());
    Assert.StartsWith("Linha 2: ", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task Post_de_extensao_nao_suportada_responde_400_BomInvalido()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);

    var resposta = await Enviar(cliente, agrupamentoId, Csv("1;A-1;Peca A;1"), "conjunto.txt");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal("BomInvalido", (await Json(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public async Task Post_em_agrupamento_inexistente_responde_404()
  {
    var resposta = await Enviar(ClienteComo("PCP"), 999999, Csv("1;A-1;Peca A;1"));

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Post_com_token_sem_a_claim_sub_responde_401()
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.TokenSemAClaim(_factory, "sub"));

    var resposta = await Enviar(cliente, 999999, Csv("1;A-1;Peca A;1"));

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Get_de_rascunho_inexistente_responde_404()
  {
    var resposta = await ClienteComo("PCP").GetAsync("/api/importacoes/999999");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- listar

  [Fact]
  public async Task Lista_do_agrupamento_traz_o_rascunho_criado()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);
    var outroAgrupamentoId = await NovoAgrupamento(cliente);
    var id = (await Json(await Enviar(cliente, agrupamentoId, Csv("1;A-1;Peca A;1"), "um.csv"))).GetProperty("id").GetInt32();

    var lista = (await Json(await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/importacoes")))
        .EnumerateArray().ToList();

    var item = Assert.Single(lista);
    Assert.Equal(id, item.GetProperty("id").GetInt32());
    Assert.Equal("um.csv", item.GetProperty("nomeDoArquivo").GetString());
    Assert.Equal(UsuarioReal().NomeCompleto, item.GetProperty("criadoPor").GetString());
    Assert.True(item.TryGetProperty("criadoEm", out _));
    Assert.True(item.TryGetProperty("atualizadoEm", out _));
    // O rascunho de um Agrupamento nao aparece na lista de outro.
    Assert.Empty((await Json(await cliente.GetAsync($"/api/agrupamentos/{outroAgrupamentoId}/importacoes"))).EnumerateArray());
  }

  [Fact]
  public async Task Lista_de_agrupamento_inexistente_responde_404()
  {
    var resposta = await ClienteComo("PCP").GetAsync("/api/agrupamentos/999999/importacoes");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- descartar

  [Fact]
  public async Task Delete_responde_204_e_o_get_seguinte_404()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente);
    var (_, codigo) = await NovoComponente();
    var id = (await Json(await Enviar(cliente, agrupamentoId, Csv($"1;{codigo};Peca A;1", "1.1;B-1;Peca B;3"))))
        .GetProperty("id").GetInt32();

    var primeira = await cliente.DeleteAsync($"/api/importacoes/{id}");
    var leitura = await cliente.GetAsync($"/api/importacoes/{id}");
    var segunda = await cliente.DeleteAsync($"/api/importacoes/{id}");

    Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, leitura.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, segunda.StatusCode);
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.False(await db.ImportacoesDeEstruturaComponentes.AnyAsync(c => c.ImportacaoId == id));
  }

  // ---------------------------------------------------------------- perfil

  /// <summary>
  /// Par negativo dos testes acima, que rodam como PCP: o perfil sem escrita recebe 403 em TODA rota de
  /// escrita desta fase, e continua lendo (leitura e de qualquer autenticado). O
  /// <c>[Authorize(Roles)]</c> roda antes do model binding, entao o 403 chega com Id inexistente.
  /// </summary>
  [Fact]
  public async Task Operador_recebe_403_em_toda_escrita()
  {
    var cliente = ClienteComo("Operador");

    var criar = await Enviar(cliente, 999999, Csv("1;A-1;Peca A;1"));
    var descartar = await cliente.DeleteAsync("/api/importacoes/999999");

    Assert.Equal(HttpStatusCode.Forbidden, criar.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, descartar.StatusCode);
  }

  [Fact]
  public async Task Operador_le_a_lista_e_o_rascunho()
  {
    var pcp = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(pcp);
    var id = (await Json(await Enviar(pcp, agrupamentoId, Csv("1;A-1;Peca A;1")))).GetProperty("id").GetInt32();
    var operador = ClienteComo("Operador");

    Assert.Equal(HttpStatusCode.OK, (await operador.GetAsync($"/api/agrupamentos/{agrupamentoId}/importacoes")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await operador.GetAsync($"/api/importacoes/{id}")).StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_nem_escreve()
  {
    var anonimo = _factory.CreateClient();

    Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/importacoes/1")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/agrupamentos/1/importacoes")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.DeleteAsync("/api/importacoes/1")).StatusCode);
  }
}
