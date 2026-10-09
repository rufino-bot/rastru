using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Application.Arquivos;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Importacao;
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
    var pedido = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X", dataEntrega = "2026-10-22" });
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

  // ---------------------------------------------------------------- editar o rascunho

  /// <summary>Cria o rascunho de um CSV e devolve o corpo (com o <c>id</c> e a <c>versao</c> para a proxima escrita).</summary>
  private async Task<JsonElement> NovoRascunho(HttpClient cliente, params string[] linhas)
  {
    var agrupamentoId = await NovoAgrupamento(cliente);
    var criado = await Enviar(cliente, agrupamentoId, Csv(linhas));
    Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    return await Json(criado);
  }

  private static string CodigoUnico(string prefixo) => $"{prefixo}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

  private static int RegistroDe(JsonElement rascunho, string codigoLido) =>
      rascunho.GetProperty("componentes").EnumerateArray()
          .Single(c => c.GetProperty("codigoLido").GetString() == codigoLido).GetProperty("registroId").GetInt32();

  private static string VersaoDe(JsonElement rascunho) => rascunho.GetProperty("versao").GetString()!;

  private static int IdDe(JsonElement rascunho) => rascunho.GetProperty("id").GetInt32();

  private static Task<HttpResponseMessage> PutComponente(
      HttpClient cliente, JsonElement rascunho, int registroId, int? componenteId, string? escolha = null) =>
      cliente.PutAsJsonAsync(
          $"/api/importacoes/{IdDe(rascunho)}/componentes/{registroId}",
          new
          {
            versao = VersaoDe(rascunho), componenteId, codigoNovo = (string?)null, descricaoNova = (string?)null,
            tipoNovo = (string?)null, escolhaDeReceita = escolha,
          });

  private static async Task<HttpResponseMessage> EnviarSolidoPendente(
      HttpClient cliente, JsonElement rascunho, int registroId, byte[] conteudo, string nome = "peca.stl")
  {
    using var corpo = new MultipartFormDataContent();
    corpo.Add(new StringContent(VersaoDe(rascunho)), "versao");
    var arquivo = new ByteArrayContent(conteudo);
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    corpo.Add(arquivo, "arquivo", nome);
    return await cliente.PostAsync($"/api/importacoes/{IdDe(rascunho)}/componentes/{registroId}/solido", corpo);
  }

  private static async Task<HttpResponseMessage> Reenviar(HttpClient cliente, JsonElement rascunho, string csv)
  {
    using var corpo = new MultipartFormDataContent();
    corpo.Add(new StringContent(VersaoDe(rascunho)), "versao");
    var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
    corpo.Add(arquivo, "arquivo", "reenviado.csv");
    return await cliente.PostAsync($"/api/importacoes/{IdDe(rascunho)}/arquivo", corpo);
  }

  private async Task<int> IdDoPendente(int registroId)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return (await db.ImportacoesDeEstruturaComponentes.AsNoTracking().SingleAsync(c => c.Id == registroId))
        .ArquivoSolidoPendenteId!.Value;
  }

  [Fact]
  public async Task Put_componente_e_get_refletem_o_casamento_manual()
  {
    var cliente = ClienteComo("PCP");
    var (componenteId, codigo) = await NovoComponente();
    var codigoNovo = CodigoUnico("NOVO");
    var rascunho = await NovoRascunho(cliente, $"1;{codigoNovo};Peca nova;1");
    var registroId = RegistroDe(rascunho, codigoNovo);

    var resposta = await PutComponente(cliente, rascunho, registroId, componenteId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = await Json(resposta);
    var situacao = corpo.GetProperty("componentes").EnumerateArray()
        .Single(c => c.GetProperty("registroId").GetInt32() == registroId);
    Assert.Equal(componenteId, situacao.GetProperty("componenteId").GetInt32());
    Assert.Equal(codigo, situacao.GetProperty("codigoDoCatalogo").GetString());
    Assert.Equal(JsonValueKind.Null, situacao.GetProperty("codigoNovo").ValueKind);
    Assert.NotEqual(VersaoDe(rascunho), VersaoDe(corpo));

    // O GET le do banco e tem de mostrar o mesmo: o casamento esta gravado, e os campos do novo, limpos.
    var lido = await Json(await cliente.GetAsync($"/api/importacoes/{IdDe(rascunho)}"));
    Assert.True(MesmoJson(corpo, lido));
    Assert.Equal(codigo, lido.GetProperty("raiz").GetProperty("filhos")[0].GetProperty("codigo").GetString());
  }

  [Fact]
  public async Task Put_componente_com_escolha_de_receita_grava_a_impressao_e_o_get_devolve_a_escolha()
  {
    var cliente = ClienteComo("PCP");
    var (paiId, paiCodigo) = await NovoComponente();
    var (filhoDoCatalogoId, _) = await NovoComponente();
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao
      {
        ComponentePaiId = paiId, ComponenteFilhoId = filhoDoCatalogoId, QuantidadePadrao = 2m,
      });
      await db.SaveChangesAsync();
    }

    try
    {
      var rascunho = await NovoRascunho(cliente, $"1;{paiCodigo};Pai;1", $"1.1;{CodigoUnico("LIDO")};Filho do BOM;3");

      var resposta = await PutComponente(cliente, rascunho, RegistroDe(rascunho, paiCodigo), paiId, "Catalogo");

      Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
      var situacao = (await Json(resposta)).GetProperty("componentes").EnumerateArray()
          .Single(c => c.GetProperty("componenteId").ValueKind == JsonValueKind.Number);
      Assert.True(situacao.GetProperty("divergente").GetBoolean());
      Assert.Equal("Catalogo", situacao.GetProperty("escolhaDeReceita").GetString());
      using var escopo = _factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var gravado = await db.ImportacoesDeEstruturaComponentes.AsNoTracking().SingleAsync(c => c.ComponenteId == paiId);
      Assert.Equal(32, gravado.ImpressaoDaReceitaDoCatalogo!.Length);
    }
    finally
    {
      // O rascunho e apagado pelo DisposeAsync; a receita de catalogo tem de sair antes dos Componentes dele.
      using var escopo = _factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      db.FilhosPadrao.RemoveRange(await db.FilhosPadrao.Where(f => f.ComponentePaiId == paiId).ToListAsync());
      await db.SaveChangesAsync();
    }
  }

  [Fact]
  public async Task Put_peca_e_put_filho_gravam_e_o_get_reflete()
  {
    var cliente = ClienteComo("PCP");
    var rascunho = await NovoRascunho(cliente, $"1;{CodigoUnico("PAI")};Pai;1", $"1.1;{CodigoUnico("FIL")};Filho;2");
    var id = IdDe(rascunho);
    var filhoId = rascunho.GetProperty("raiz").GetProperty("filhos")[0].GetProperty("filhos")[0].GetProperty("filhoId").GetInt32();

    var peca = await cliente.PutAsJsonAsync(
        $"/api/importacoes/{id}", new { versao = VersaoDe(rascunho), quantidadeDaPeca = 4.5m, requerRelatorioDimensional = true });
    Assert.Equal(HttpStatusCode.OK, peca.StatusCode);
    var filho = await cliente.PutAsJsonAsync(
        $"/api/importacoes/{id}/filhos/{filhoId}", new { versao = VersaoDe(await Json(peca)), quantidade = 3.25m });
    Assert.Equal(HttpStatusCode.OK, filho.StatusCode);

    var lido = await Json(await cliente.GetAsync($"/api/importacoes/{id}"));
    Assert.Equal(4.5m, lido.GetProperty("quantidadeDaPeca").GetDecimal());
    Assert.True(lido.GetProperty("requerRelatorioDimensional").GetBoolean());
    Assert.Equal(3.25m, lido.GetProperty("raiz").GetProperty("filhos")[0].GetProperty("filhos")[0].GetProperty("quantidadePorPai").GetDecimal());
    var fora = await cliente.PutAsJsonAsync($"/api/importacoes/{id}/filhos/{filhoId}", new { versao = VersaoDe(lido), quantidade = 0m });
    Assert.Equal(HttpStatusCode.BadRequest, fora.StatusCode);
  }

  [Fact]
  public async Task Put_com_versao_velha_responde_409()
  {
    var cliente = ClienteComo("PCP");
    var rascunho = await NovoRascunho(cliente, $"1;{CodigoUnico("VEL")};Peca;1");
    var id = IdDe(rascunho);
    var primeira = await cliente.PutAsJsonAsync(
        $"/api/importacoes/{id}", new { versao = VersaoDe(rascunho), quantidadeDaPeca = 1m, requerRelatorioDimensional = false });
    Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);

    // A segunda escrita ainda manda a versao que o POST devolveu.
    var velha = await cliente.PutAsJsonAsync(
        $"/api/importacoes/{id}", new { versao = VersaoDe(rascunho), quantidadeDaPeca = 2m, requerRelatorioDimensional = false });

    Assert.Equal(HttpStatusCode.Conflict, velha.StatusCode);
    Assert.Equal("ImportacaoDesatualizada", (await Json(velha)).GetProperty("erro").GetString());
    var lido = await Json(await cliente.GetAsync($"/api/importacoes/{id}"));
    Assert.Equal(1m, lido.GetProperty("quantidadeDaPeca").GetDecimal());
  }

  [Fact]
  public async Task Post_solido_pendente_e_get_devolve_o_mesmo_binario()
  {
    var cliente = ClienteComo("PCP");
    var codigo = CodigoUnico("SOL");
    var rascunho = await NovoRascunho(cliente, $"1;{codigo};Peca;1");
    var registroId = RegistroDe(rascunho, codigo);
    var cubo = StlDeTesteDaApi.CuboBinario();
    var url = $"/api/importacoes/{IdDe(rascunho)}/componentes/{registroId}/solido";

    var semPendente = await cliente.GetAsync(url);
    var enviado = await EnviarSolidoPendente(cliente, rascunho, registroId, cubo, "parafuso.stl");

    Assert.Equal(HttpStatusCode.NotFound, semPendente.StatusCode);
    Assert.Equal(HttpStatusCode.OK, enviado.StatusCode);
    var situacao = (await Json(enviado)).GetProperty("componentes").EnumerateArray()
        .Single(c => c.GetProperty("registroId").GetInt32() == registroId);
    Assert.True(situacao.GetProperty("temSolidoPendente").GetBoolean());
    Assert.Equal("parafuso.stl", situacao.GetProperty("nomeDoSolido").GetString());
    Assert.Equal(cubo.Length, situacao.GetProperty("tamanhoDoSolidoEmBytes").GetInt32());
    var baixado = await cliente.GetAsync(url);
    Assert.Equal(HttpStatusCode.OK, baixado.StatusCode);
    Assert.Equal("application/octet-stream", baixado.Content.Headers.ContentType!.MediaType);
    Assert.Equal("parafuso.stl", baixado.Content.Headers.ContentDisposition!.FileName?.Trim('"'));
    Assert.Equal(cubo, await baixado.Content.ReadAsByteArrayAsync());
  }

  [Fact]
  public async Task Post_solido_pendente_que_substitui_apaga_o_arquivo_anterior_do_banco()
  {
    var cliente = ClienteComo("PCP");
    var codigo = CodigoUnico("SUB");
    var rascunho = await NovoRascunho(cliente, $"1;{codigo};Peca;1");
    var registroId = RegistroDe(rascunho, codigo);
    var primeiro = await Json(await EnviarSolidoPendente(cliente, rascunho, registroId, StlDeTesteDaApi.CuboBinario(), "um.stl"));
    var idDoPrimeiro = await IdDoPendente(registroId);

    var segundo = await EnviarSolidoPendente(cliente, primeiro, registroId, StlDeTesteDaApi.CuboBinario(), "dois.stl");

    Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
    var idDoSegundo = await IdDoPendente(registroId);
    Assert.NotEqual(idDoPrimeiro, idDoSegundo);
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.False(await db.ArquivosDeComponente.AnyAsync(a => a.Id == idDoPrimeiro));
    Assert.True(await db.ArquivosDeComponente.AnyAsync(a => a.Id == idDoSegundo));
  }

  [Fact]
  public async Task Post_solido_pendente_invalido_responde_400_com_a_mensagem_do_validador()
  {
    var cliente = ClienteComo("PCP");
    var codigo = CodigoUnico("INV");
    var rascunho = await NovoRascunho(cliente, $"1;{codigo};Peca;1");

    var resposta = await EnviarSolidoPendente(cliente, rascunho, RegistroDe(rascunho, codigo), StlDeTesteDaApi.Invalido());

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Equal(
        ValidadorDeArquivoStl.Validar("peca.stl", StlDeTesteDaApi.Invalido()),
        (await Json(resposta)).GetProperty("erro").GetString());
  }

  /// <summary>
  /// Conteudo lixo (zeros) um byte acima do teto: quem recusa aqui e o VALIDADOR, e nao o
  /// <c>RequestSizeLimit</c> -- o <c>TestServer</c> nao exercita o limite do Kestrel (medido no teste
  /// de limite de <c>SolidoEndpointsTests</c>), e e por isso que
  /// <c>RequestSizeLimit_dos_dois_envios_e_o_do_arquivo_mais_a_margem_do_multipart</c> confere so a DECLARACAO.
  /// </summary>
  [Fact]
  public async Task Post_solido_pendente_acima_do_limite_e_recusado()
  {
    var cliente = ClienteComo("PCP");
    var codigo = CodigoUnico("GDE");
    var rascunho = await NovoRascunho(cliente, $"1;{codigo};Peca;1");

    var resposta = await EnviarSolidoPendente(
        cliente, rascunho, RegistroDe(rascunho, codigo), new byte[ValidadorDeArquivoStl.TamanhoMaximoEmBytes + 1]);

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    Assert.Contains("16 MiB", (await Json(resposta)).GetProperty("erro").GetString());
  }

  [Fact]
  public void RequestSizeLimit_dos_dois_envios_e_o_do_arquivo_mais_a_margem_do_multipart()
  {
    using var factory = new WebApplicationFactory<Program>();
    var fonte = factory.Services.GetRequiredService<EndpointDataSource>();

    long LimiteDe(string rota) =>
        fonte.Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == rota
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") ?? false))
            .Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize!.Value;

    // 4096 espelha `ImportacaoController.MargemDoCorpoMultipartEmBytes` (privada); acompanha se ela mudar.
    Assert.Equal(
        ValidadorDeArquivoStl.TamanhoMaximoEmBytes + 4096, LimiteDe("importacoes/{id:int}/componentes/{registroId:int}/solido"));
    Assert.Equal(LeitorDeBom.TamanhoMaximoEmBytes + 4096, LimiteDe("importacoes/{id:int}/arquivo"));
  }

  [Fact]
  public async Task Reimport_com_csv_invalido_responde_400_e_nao_altera_o_rascunho()
  {
    var cliente = ClienteComo("PCP");
    var codigo = CodigoUnico("REI");
    var rascunho = await NovoRascunho(cliente, $"1;{codigo};Peca;1");
    var id = IdDe(rascunho);
    var antes = await (await cliente.GetAsync($"/api/importacoes/{id}")).Content.ReadAsStringAsync();

    var resposta = await Reenviar(cliente, rascunho, Csv($"1;{codigo};Peca;abc", "2;OUTRO;Outra;x"));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("BomInvalido", corpo.GetProperty("erro").GetString());
    Assert.Equal(2, corpo.GetProperty("mensagem").GetString()!.Split('\n').Length);
    Assert.Equal(antes, await (await cliente.GetAsync($"/api/importacoes/{id}")).Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Reimport_valido_troca_as_receitas_e_preserva_por_codigo_no_banco_de_verdade()
  {
    var cliente = ClienteComo("PCP");
    var (componenteId, _) = await NovoComponente();
    var a = CodigoUnico("A");
    var b = CodigoUnico("B");
    var c = CodigoUnico("C");
    var d = CodigoUnico("D");
    var rascunho = await NovoRascunho(cliente, $"1;{a};Peca A;1", $"1.1;{b};Filho B;2", $"2;{c};Peca C;3");
    var idDeA = RegistroDe(rascunho, a);
    var idDeB = RegistroDe(rascunho, b);
    var idDeC = RegistroDe(rascunho, c);
    // A fica casado a mao com um Componente do catalogo e ganha solido pendente; B, que vai sair, tambem tem pendente.
    var casado = await Json(await PutComponente(cliente, rascunho, idDeA, componenteId));
    var comSolidoDeA = await Json(await EnviarSolidoPendente(cliente, casado, idDeA, StlDeTesteDaApi.CuboBinario(), "a.stl"));
    var comSolidoDeB = await Json(await EnviarSolidoPendente(cliente, comSolidoDeA, idDeB, StlDeTesteDaApi.CuboBinario(), "b.stl"));
    var pendenteDeA = await IdDoPendente(idDeA);
    var pendenteDeB = await IdDoPendente(idDeB);

    // B e C saem do arquivo, e D entra sob A.
    var resposta = await Reenviar(cliente, comSolidoDeB, Csv($"1;{a};Peca A;1", $"1.1;{d};Filho D;5"));

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("reenviado.csv", corpo.GetProperty("nomeDoArquivo").GetString());
    var codigos = corpo.GetProperty("componentes").EnumerateArray().Select(x => x.GetProperty("codigoLido").GetString()).ToList();
    Assert.Equal(3, codigos.Count);
    Assert.Contains(a, codigos);
    Assert.Contains(d, codigos);
    Assert.DoesNotContain(b, codigos);
    var emA = corpo.GetProperty("componentes").EnumerateArray().Single(x => x.GetProperty("codigoLido").GetString() == a);
    Assert.Equal(
        (idDeA, componenteId, true),
        (emA.GetProperty("registroId").GetInt32(), emA.GetProperty("componenteId").GetInt32(), emA.GetProperty("temSolidoPendente").GetBoolean()));
    var filhoDeA = corpo.GetProperty("raiz").GetProperty("filhos")[0].GetProperty("filhos")[0];
    Assert.Equal((d, 5m), (filhoDeA.GetProperty("codigo").GetString(), filhoDeA.GetProperty("quantidadePorPai").GetDecimal()));
    Assert.True(MesmoJson(corpo, await Json(await cliente.GetAsync($"/api/importacoes/{IdDe(rascunho)}"))));

    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.False(await db.ImportacoesDeEstruturaComponentes.AnyAsync(x => x.Id == idDeB || x.Id == idDeC));
    Assert.False(await db.ArquivosDeComponente.AnyAsync(x => x.Id == pendenteDeB));
    Assert.True(await db.ArquivosDeComponente.AnyAsync(x => x.Id == pendenteDeA));
    Assert.Equal(1, await db.ImportacoesDeEstruturaFilhos.CountAsync(x => x.PaiId == idDeA));
  }

  /// <summary>Par negativo dos testes de edicao, que rodam como PCP: toda rota nova de escrita recusa o Operador.</summary>
  [Fact]
  public async Task Operador_recebe_403_em_toda_escrita_de_edicao()
  {
    var cliente = ClienteComo("Operador");
    var rascunho = JsonDocument.Parse("""{"id":999999,"versao":"AAAAAAAAB9E="}""").RootElement;
    var corpoJson = new { versao = "AAAAAAAAB9E=", quantidadeDaPeca = 1m, requerRelatorioDimensional = false };

    Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync("/api/importacoes/999999", corpoJson)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync("/api/importacoes/999999/componentes/1", corpoJson)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync("/api/importacoes/999999/filhos/1", corpoJson)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await EnviarSolidoPendente(cliente, rascunho, 1, StlDeTesteDaApi.CuboBinario())).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Reenviar(cliente, rascunho, Csv("1;A-1;Peca A;1"))).StatusCode);
  }

  // ---------------------------------------------------------------- perfil

  /// <summary>
  /// Par negativo dos testes desta classe que escrevem com <c>ClienteComo("PCP")</c>: o perfil sem escrita
  /// recebe 403 em TODA rota de escrita desta fase, e continua lendo (leitura e de qualquer autenticado). O
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
