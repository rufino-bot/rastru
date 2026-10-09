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
/// A confirmacao do rascunho do import (<c>POST /importacoes/{id}/confirmacao</c>) ponta a ponta, contra o
/// SQL Server real. Cada teste cria o proprio Pedido, Agrupamentos, Componentes, Materiais e Setores, com
/// codigos unicos, e a limpeza apaga tudo, inclusive os Componentes que a confirmacao criou (achados pelo
/// codigo, que o teste escolheu).
///
/// <para>
/// O QUE SO SE PROVA AQUI (o caso de uso tem cobertura propria em <c>ImportacaoDeEstruturaUseCaseTests</c>):
/// que a transacao da execucao aceita as escritas do catalogo, da receita e do rascunho (cada repositorio
/// usa a transacao aberta em vez de abrir a sua), que o arquivo pendente ligado sobrevive a exclusao do
/// rascunho, e que a Peca confirmada e a MESMA que a "Nova Peca" faria a partir do catalogo gravado.
/// </para>
/// </summary>
public class ConfirmacaoDeImportacaoTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private const string Cabecalho = "N DO ITEM;N DA PECA;DESCRICAO;QTD";

  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _numerosCriados = [];
  private readonly List<int> _componentesCriados = [];
  private readonly List<string> _codigosNovos = [];
  private readonly List<int> _materiaisCriados = [];
  private readonly List<int> _setoresCriados = [];

  public ConfirmacaoDeImportacaoTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
    var pedidoIds = pedidos.Select(p => p.Id).ToList();
    var agrupamentoIds = await db.Agrupamentos
        .Where(a => pedidoIds.Contains(a.PedidoId)).Select(a => a.Id).ToListAsync();

    // Rascunho que sobrou (o teste que nao confirmou) pelo repositorio real, que conhece a ordem das FKs.
    var rascunhoIds = await db.ImportacoesDeEstrutura
        .Where(i => agrupamentoIds.Contains(i.AgrupamentoId)).Select(i => i.Id).ToListAsync();
    var repositorio = escopo.ServiceProvider.GetRequiredService<IImportacaoDeEstruturaRepository>();
    foreach (var id in rascunhoIds)
      await repositorio.ExcluirAsync(id, CancellationToken.None);

    foreach (var agId in agrupamentoIds)
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId}");
    }
    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => pedidoIds.Contains(a.PedidoId)).ToListAsync());
    await db.SaveChangesAsync();

    // Os Componentes que a confirmacao criou entram pela lista de codigos.
    var ids = _componentesCriados
        .Concat(await db.Componentes.Where(c => _codigosNovos.Contains(c.Codigo)).Select(c => c.Id).ToListAsync())
        .ToList();
    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => ids.Contains(f.ComponentePaiId) || ids.Contains(f.ComponenteFilhoId)).ToListAsync());
    db.MateriaisPadrao.RemoveRange(await db.MateriaisPadrao.Where(m => ids.Contains(m.ComponenteId)).ToListAsync());
    db.RoteirosPadrao.RemoveRange(await db.RoteirosPadrao.Where(r => ids.Contains(r.ComponenteId)).ToListAsync());
    await db.SaveChangesAsync();

    // Componente antes de ArquivoDeComponente: quem tem a FK e o Componente.
    var componentes = await db.Componentes.Where(c => ids.Contains(c.Id)).ToListAsync();
    var arquivoIds = componentes.Where(c => c.ArquivoSolidoId is not null).Select(c => c.ArquivoSolidoId!.Value).ToList();
    db.Componentes.RemoveRange(componentes);
    await db.SaveChangesAsync();
    db.ArquivosDeComponente.RemoveRange(await db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ToListAsync());
    db.Materiais.RemoveRange(await db.Materiais.Where(m => _materiaisCriados.Contains(m.Id)).ToListAsync());
    db.Setores.RemoveRange(await db.Setores.Where(s => _setoresCriados.Contains(s.Id)).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(pedidos);
    await db.SaveChangesAsync();
  }

  // ---------------------------------------------------------------- arranjo

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private async Task<int> NovoPedido(HttpClient cliente)
  {
    var numero = $"ped-{Guid.NewGuid():N}"[..25];
    _numerosCriados.Add(numero);
    var pedido = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X", dataEntrega = "2026-10-22" });
    return (await Json(pedido)).GetProperty("id").GetInt32();
  }

  private static async Task<int> NovoAgrupamento(HttpClient cliente, int pedidoId, string codigo)
  {
    var agrupamento = await cliente.PostAsJsonAsync($"/api/pedidos/{pedidoId}/agrupamentos", new { codigo, tipo = "Kit" });
    return (await Json(agrupamento)).GetProperty("id").GetInt32();
  }

  private string CodigoNovo(string prefixo)
  {
    var codigo = $"{prefixo}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
    _codigosNovos.Add(codigo);
    return codigo;
  }

  /// <summary>Um Componente de catalogo, com solido (todo nao-Bruto da arvore final precisa de um).</summary>
  private async Task<(int Id, string Codigo)> NovoComponente(string tipo = "Fabricado", bool comSolido = true)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    int? arquivoId = null;
    if (comSolido)
    {
      var arquivo = new ArquivoDeComponente
      {
        NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = 1,
      };
      db.ArquivosDeComponente.Add(arquivo);
      await db.SaveChangesAsync();
      arquivoId = arquivo.Id;
    }
    var c = new Componente
    {
      Codigo = $"CAT-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
      Descricao = "Componente do catalogo",
      Tipo = tipo,
      Ativo = true,
      ArquivoSolidoId = arquivoId,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return (c.Id, c.Codigo);
  }

  private async Task<int> NovoMaterial()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var m = new Material
    {
      Codigo = $"MAT-{Guid.NewGuid():N}"[..16], Descricao = "Material de teste", UnidadeMedida = "UN", Ativo = true,
    };
    db.Materiais.Add(m);
    await db.SaveChangesAsync();
    _materiaisCriados.Add(m.Id);
    return m.Id;
  }

  private async Task<int> NovoSetor()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var s = new Setor { Nome = $"Setor {Guid.NewGuid():N}"[..30], Ativo = true };
    db.Setores.Add(s);
    await db.SaveChangesAsync();
    _setoresCriados.Add(s.Id);
    return s.Id;
  }

  /// <summary>Grava a receita de catalogo direto no banco: o que se prova aqui e a confirmacao, nao a tela da receita.</summary>
  private async Task ReceitaNoCatalogo(
      int componenteId, (int FilhoId, decimal Qtd)[]? filhos = null,
      (int MaterialId, decimal Qtd)[]? materiais = null, (int SetorId, int Ordem)[]? roteiro = null)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    foreach (var (filhoId, qtd) in filhos ?? [])
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao { ComponentePaiId = componenteId, ComponenteFilhoId = filhoId, QuantidadePadrao = qtd });
    foreach (var (materialId, qtd) in materiais ?? [])
      db.MateriaisPadrao.Add(new ComponenteMaterialPadrao { ComponenteId = componenteId, MaterialId = materialId, QuantidadePadrao = qtd });
    foreach (var (setorId, ordem) in roteiro ?? [])
      db.RoteirosPadrao.Add(new ComponenteRoteiroPadrao { ComponenteId = componenteId, SetorId = setorId, Ordem = ordem });
    await db.SaveChangesAsync();
  }

  private static string Csv(params string[] linhas) => string.Join("\n", linhas.Prepend(Cabecalho));

  private static async Task<JsonElement> Json(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

  private static async Task<JsonElement> NovoRascunho(HttpClient cliente, int agrupamentoId, string csv)
  {
    using var corpo = new MultipartFormDataContent();
    var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
    corpo.Add(arquivo, "arquivo", "conjunto.csv");
    var criado = await cliente.PostAsync($"/api/agrupamentos/{agrupamentoId}/importacoes", corpo);
    Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    return await Json(criado);
  }

  private static int IdDe(JsonElement rascunho) => rascunho.GetProperty("id").GetInt32();

  private static string VersaoDe(JsonElement rascunho) => rascunho.GetProperty("versao").GetString()!;

  /// <summary>O registro pelo codigo lido; a raiz (sem codigo) com <paramref name="codigoLido"/> nulo.</summary>
  private static int RegistroDe(JsonElement rascunho, string? codigoLido) =>
      rascunho.GetProperty("componentes").EnumerateArray()
          .Single(c => c.GetProperty("codigoLido").GetString() == codigoLido).GetProperty("registroId").GetInt32();

  /// <summary>Uma escrita que devolve o rascunho: tem de dar 200, e o corpo traz a versao da proxima.</summary>
  private static async Task<JsonElement> Ok(Task<HttpResponseMessage> escrita)
  {
    var resposta = await escrita;
    var texto = await resposta.Content.ReadAsStringAsync();
    Assert.True(resposta.StatusCode == HttpStatusCode.OK, $"{(int)resposta.StatusCode}: {texto}");
    return JsonDocument.Parse(texto).RootElement;
  }

  private static Task<HttpResponseMessage> PutComponente(
      HttpClient cliente, JsonElement rascunho, int registroId, int? componenteId,
      string? codigoNovo = null, string? escolha = null) =>
      cliente.PutAsJsonAsync(
          $"/api/importacoes/{IdDe(rascunho)}/componentes/{registroId}",
          new
          {
            versao = VersaoDe(rascunho), componenteId, codigoNovo, descricaoNova = (string?)null,
            tipoNovo = (string?)null, escolhaDeReceita = escolha,
          });

  private static Task<HttpResponseMessage> PutPeca(HttpClient cliente, JsonElement rascunho, decimal quantidade) =>
      cliente.PutAsJsonAsync(
          $"/api/importacoes/{IdDe(rascunho)}",
          new { versao = VersaoDe(rascunho), quantidadeDaPeca = quantidade, requerRelatorioDimensional = true });

  private static async Task<HttpResponseMessage> EnviarSolido(HttpClient cliente, JsonElement rascunho, int registroId)
  {
    using var corpo = new MultipartFormDataContent();
    corpo.Add(new StringContent(VersaoDe(rascunho)), "versao");
    var arquivo = new ByteArrayContent(StlDeTesteDaApi.CuboBinario());
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    corpo.Add(arquivo, "arquivo", "peca.stl");
    return await cliente.PostAsync($"/api/importacoes/{IdDe(rascunho)}/componentes/{registroId}/solido", corpo);
  }

  private static Task<HttpResponseMessage> Confirmar(HttpClient cliente, JsonElement rascunho, string? versao = null) =>
      cliente.PostAsJsonAsync(
          $"/api/importacoes/{IdDe(rascunho)}/confirmacao", new { versao = versao ?? VersaoDe(rascunho) });

  /// <summary>
  /// Um rascunho pronto para confirmar, com o catalogo inteiro em jogo:
  /// <code>
  /// raiz (nova, solido pendente)
  ///   novoA (novo, Montagem, solido pendente) x2
  ///     X (casado; materiais e roteiro no catalogo) x3
  ///   Y (casado; receita de catalogo Y -> Z x4, e folha no BOM: diverge, escolha "Catalogo") x1
  ///     Z (so do catalogo, Bruto, com roteiro)
  /// </code>
  /// Quantidade da Peca 2, com relatorio dimensional.
  /// </summary>
  private async Task<(JsonElement Rascunho, int AgrupamentoId, int PedidoId, string CodigoDaRaiz, int YId, int ZId)>
      RascunhoPronto(HttpClient cliente)
  {
    var pedidoId = await NovoPedido(cliente);
    var agrupamentoId = await NovoAgrupamento(cliente, pedidoId, "AG-01");
    var material = await NovoMaterial();
    var setor1 = await NovoSetor();
    var setor2 = await NovoSetor();
    var (xId, x) = await NovoComponente();
    await ReceitaNoCatalogo(xId, materiais: [(material, 1.5m)], roteiro: [(setor1, 1), (setor2, 2)]);
    var (zId, _) = await NovoComponente("Bruto", comSolido: false);
    await ReceitaNoCatalogo(zId, roteiro: [(setor2, 1)]);
    var (yId, y) = await NovoComponente();
    await ReceitaNoCatalogo(yId, filhos: [(zId, 4m)]);
    var novoA = CodigoNovo("NA");
    var codigoDaRaiz = CodigoNovo("RAIZ");

    var r = await NovoRascunho(cliente, agrupamentoId, Csv($"1;{novoA};Sub A;2", $"1.1;{x};Peca X;3", $"2;{y};Peca Y;1"));
    r = await Ok(PutComponente(cliente, r, RegistroDe(r, null), null, codigoNovo: codigoDaRaiz));
    r = await Ok(PutComponente(cliente, r, RegistroDe(r, y), yId, escolha: "Catalogo"));
    r = await Ok(EnviarSolido(cliente, r, RegistroDe(r, null)));
    r = await Ok(EnviarSolido(cliente, r, RegistroDe(r, novoA)));
    r = await Ok(PutPeca(cliente, r, 2m));
    Assert.Empty(r.GetProperty("bloqueios").EnumerateArray());
    return (r, agrupamentoId, pedidoId, codigoDaRaiz, yId, zId);
  }

  /// <summary>
  /// A arvore como texto, sem Ids de no: codigo, descricao, quantidade, razao, nivel, relatorio, materiais
  /// e roteiro de cada no, com os filhos ordenados pelo proprio texto (a ordem entre irmaos depende do Id
  /// das linhas de receita, que nao e o que se compara).
  /// </summary>
  private static string Arvore(JsonElement no)
  {
    static string Numero(JsonElement e) => e.ValueKind == JsonValueKind.Null ? "-" : e.GetDecimal().ToString("0.####");
    var materiais = no.GetProperty("materiais").EnumerateArray()
        .Select(m => $"{m.GetProperty("materialId").GetInt32()}:{Numero(m.GetProperty("quantidade"))}").Order();
    var roteiro = no.GetProperty("roteiro").EnumerateArray()
        .Select(p => $"{p.GetProperty("setorId").GetInt32()}@{p.GetProperty("ordem").GetInt32()}");
    var filhos = no.GetProperty("filhos").EnumerateArray().Select(Arvore).Order(StringComparer.Ordinal);
    return $"{no.GetProperty("codigoDoComponente").GetString()}|{no.GetProperty("descricao").GetString()}"
        + $"|{Numero(no.GetProperty("quantidade"))}|{Numero(no.GetProperty("quantidadePorPai"))}"
        + $"|{no.GetProperty("nivelHierarquico").GetString()}|{no.GetProperty("requerRelatorioDimensional").GetBoolean()}"
        + $"|M[{string.Join(",", materiais)}]|R[{string.Join(",", roteiro)}]|F[{string.Join(";", filhos)}]";
  }

  // ---------------------------------------------------------------- testes

  [Fact]
  public async Task Peca_confirmada_e_identica_a_criada_pelo_Nova_Peca_a_partir_do_mesmo_catalogo()
  {
    var cliente = ClienteComo("PCP");
    var (rascunho, _, pedidoId, codigoDaRaiz, _, _) = await RascunhoPronto(cliente);

    var confirmada = await Confirmar(cliente, rascunho);

    var texto = await confirmada.Content.ReadAsStringAsync();
    Assert.True(confirmada.StatusCode == HttpStatusCode.Created, $"{(int)confirmada.StatusCode}: {texto}");
    var peca = JsonDocument.Parse(texto).RootElement;
    Assert.Equal(codigoDaRaiz, peca.GetProperty("codigoDoComponente").GetString());

    // A "Nova Peca" do mesmo Componente raiz, num Agrupamento irmao, copia a receita que a confirmacao gravou.
    var irmao = await NovoAgrupamento(cliente, pedidoId, "AG-02");
    var nova = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{irmao}/estrutura",
        new { componenteId = peca.GetProperty("componenteId").GetInt32(), quantidade = 2m, requerRelatorioDimensional = true });
    Assert.Equal(HttpStatusCode.Created, nova.StatusCode);

    var esperada = Arvore(await Json(nova));
    Assert.Equal(esperada, Arvore(peca));
    // E a arvore nao e trivial: os materiais, o roteiro e o filho que so o catalogo trazia estao nela.
    Assert.Matches(@"M\[\d+:18\]", esperada);   // X: 1,5 por unidade x 12 unidades (2 x 2 x 3)
    Assert.Contains("|8|4|Item|", esperada);   // Z: 4 por Y, 2 Y
  }

  [Fact]
  public async Task Catalogo_alterado_entre_escolha_e_confirmacao_responde_409_ReceitaDoCatalogoMudou()
  {
    var cliente = ClienteComo("PCP");
    var (rascunho, agrupamentoId, _, codigoDaRaiz, yId, zId) = await RascunhoPronto(cliente);
    // Outra pessoa muda a receita de catalogo de Y depois da escolha; ela continua divergindo da lida.
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE dbo.ComponenteFilhoPadrao SET QuantidadePadrao = 5 WHERE ComponentePaiId = {yId} AND ComponenteFilhoId = {zId}");
    }

    var resposta = await Confirmar(cliente, rascunho);

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("ReceitaDoCatalogoMudou", corpo.GetProperty("erro").GetString());
    var codigoDeY = (await Json(await cliente.GetAsync($"/api/componentes/{yId}"))).GetProperty("codigo").GetString()!;
    Assert.Contains(codigoDeY, corpo.GetProperty("mensagem").GetString());
    // A escolha foi zerada, e o rascunho volta a pedir uma.
    var relido = await Json(await cliente.GetAsync($"/api/importacoes/{IdDe(rascunho)}"));
    var situacaoDeY = relido.GetProperty("componentes").EnumerateArray()
        .Single(c => c.GetProperty("componenteId").ValueKind == JsonValueKind.Number && c.GetProperty("componenteId").GetInt32() == yId);
    Assert.Equal(JsonValueKind.Null, situacaoDeY.GetProperty("escolhaDeReceita").ValueKind);
    Assert.Contains(relido.GetProperty("bloqueios").EnumerateArray(),
        b => b.GetProperty("tipo").GetString() == "DivergenciaSemEscolha");
    // E nada da transacao ficou: nem a Peca nem o Componente novo da raiz.
    Assert.Empty((await Json(await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura"))).EnumerateArray());
    using var leitura = _factory.Services.CreateScope();
    Assert.False(await leitura.ServiceProvider.GetRequiredService<RastreamentoDbContext>()
        .Componentes.AnyAsync(c => c.Codigo == codigoDaRaiz));
  }

  [Fact]
  public async Task Confirmar_com_bloqueio_responde_400_ImportacaoComBloqueios()
  {
    var cliente = ClienteComo("PCP");
    var agrupamentoId = await NovoAgrupamento(cliente, await NovoPedido(cliente), "AG-01");
    // Recem-criado: a raiz esta sem codigo e sem solido, e falta a quantidade da Peca.
    var rascunho = await NovoRascunho(cliente, agrupamentoId, Csv($"1;{CodigoNovo("B")};Peca B;1"));

    var resposta = await Confirmar(cliente, rascunho);

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = await Json(resposta);
    Assert.Equal("ImportacaoComBloqueios", corpo.GetProperty("erro").GetString());
    // Decisao P15 do plano do import: a lista vive so no GET.
    Assert.False(corpo.TryGetProperty("mensagem", out _));
    Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/api/importacoes/{IdDe(rascunho)}")).StatusCode);
  }

  [Fact]
  public async Task Confirmar_com_versao_velha_responde_409_ImportacaoDesatualizada()
  {
    var cliente = ClienteComo("PCP");
    var (rascunho, agrupamentoId, _, _, _, _) = await RascunhoPronto(cliente);

    var resposta = await Confirmar(cliente, rascunho, Convert.ToBase64String(new byte[8]));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    Assert.Equal("ImportacaoDesatualizada", (await Json(resposta)).GetProperty("erro").GetString());
    Assert.Empty((await Json(await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura"))).EnumerateArray());
  }

  [Fact]
  public async Task Depois_de_confirmar_o_get_do_rascunho_e_404_e_o_agrupamento_tem_a_peca()
  {
    var cliente = ClienteComo("PCP");
    var (rascunho, agrupamentoId, _, _, _, _) = await RascunhoPronto(cliente);

    var confirmada = await Confirmar(cliente, rascunho);

    Assert.Equal(HttpStatusCode.Created, confirmada.StatusCode);
    var peca = await Json(confirmada);
    Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/importacoes/{IdDe(rascunho)}")).StatusCode);
    var arvore = (await Json(await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura"))).EnumerateArray().ToList();
    var unica = Assert.Single(arvore);
    Assert.Equal(peca.GetProperty("id").GetInt32(), unica.GetProperty("id").GetInt32());
    Assert.True(unica.GetProperty("requerRelatorioDimensional").GetBoolean());
    // O solido pendente da raiz virou o solido do Componente novo e sobreviveu a exclusao do rascunho.
    var solido = await cliente.GetAsync($"/api/componentes/{peca.GetProperty("componenteId").GetInt32()}/solido");
    Assert.Equal(HttpStatusCode.OK, solido.StatusCode);
    Assert.Equal(StlDeTesteDaApi.CuboBinario(), await solido.Content.ReadAsByteArrayAsync());
  }

  [Fact]
  public async Task Confirmar_sem_perfil_de_escrita_responde_403()
  {
    var resposta = await ClienteComo("Operador").PostAsJsonAsync("/api/importacoes/999999/confirmacao", new { versao = "AAAAAAAAAAA=" });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }
}
