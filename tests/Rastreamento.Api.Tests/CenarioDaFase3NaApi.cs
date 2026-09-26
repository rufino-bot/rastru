using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Um Pedido Avulso pronto para a Fase 3: Peca A (10; Roteiro Solda -> Pintura) com dois filhos ad-hoc,
/// B (20, razao 2; Corte) e C (10, razao 1; Dobra), e um usuario REAL por perfil — a autoria do livro e
/// FK para `dbo.Usuario`, e o estorno compara o autor. Criado pela API onde existe rota (Pedido,
/// Agrupamento, arvore, Roteiro); Setores e o Componente com solido vao direto no banco, como em
/// `EstruturaEndpointsTests`. A limpeza respeita as FKs: estornos, movimentos, montagens, arvore,
/// Agrupamento, Pedido, Componente, Setores e, por ultimo, os usuarios.
/// </summary>
internal sealed class CenarioDaFase3NaApi : IAsyncDisposable
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<UsuarioDeTeste> _usuarios = [];
  private int _arquivoId;

  public UsuarioDeTeste Operador { get; private set; } = null!;
  public UsuarioDeTeste Movimentador { get; private set; } = null!;
  public UsuarioDeTeste Pcp { get; private set; } = null!;
  public UsuarioDeTeste Gestao { get; private set; } = null!;
  public int Corte { get; private set; }
  public int Dobra { get; private set; }
  public int Solda { get; private set; }
  public int Pintura { get; private set; }
  public int PedidoId { get; private set; }
  public int AgrupamentoId { get; private set; }
  public int ComponenteId { get; private set; }
  public int A { get; private set; }
  public int B { get; private set; }
  public int C { get; private set; }

  private CenarioDaFase3NaApi(WebApplicationFactory<Program> factory) => _factory = factory;

  /// <summary>
  /// Ruling R11 do controlador: o arranjo inteiro corre num try/catch que descarta o cenario (o que
  /// ja foi criado at aquele ponto) antes de relancar. Sem isso, uma falha no meio — por exemplo um
  /// `Garantir` que estoura porque o Roteiro recusou um Setor — deixaria usuarios, Setores, Componente
  /// e Pedido como lixo no banco compartilhado, sem nenhum `[Fact]` para limpar depois (o `await using`
  /// do chamador nunca chega a existir, porque `CriarAsync` nao retornou). `DisposeAsync` tolera um
  /// cenario parcialmente construido: os campos `int` nao setados ficam 0 e os `DELETE ... WHERE Id = 0`
  /// so nao acham linha — nenhuma referencia nula no caminho, e a lista de usuarios so contem quem de
  /// fato foi criado.
  /// </summary>
  public static async Task<CenarioDaFase3NaApi> CriarAsync(WebApplicationFactory<Program> factory)
  {
    var c = new CenarioDaFase3NaApi(factory);
    try
    {
      c.Operador = await c.UsuarioAsync("f3-operador", "Operador");
      c.Movimentador = await c.UsuarioAsync("f3-movimentador", "Movimentador");
      c.Pcp = await c.UsuarioAsync("f3-pcp", "PCP");
      c.Gestao = await c.UsuarioAsync("f3-gestao", "Gestao");

      var rotulo = $"{Guid.NewGuid():N}"[..8];
      using (var escopo = factory.Services.CreateScope())
      {
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        var setores = new[] { "Corte", "Dobra", "Solda", "Pintura" }
            .Select(n => new Setor { Nome = $"f3-{rotulo}-{n}", Ativo = true }).ToArray();
        db.Setores.AddRange(setores);
        // C7: o autor do arquivo e um usuario do PROPRIO cenario (c.Pcp), nao o admin do seed
        // (CriadoPorUsuarioId = 1) — spec 9.3, "sem depender do seed".
        var arquivo = new ArquivoDeComponente
        {
          NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = c.Pcp.Id,
        };
        db.ArquivosDeComponente.Add(arquivo);
        await db.SaveChangesAsync();
        var componente = new Componente
        {
          Codigo = $"f3-{rotulo}", Descricao = "Chassi da Fase 3", Tipo = "Fabricado", Ativo = true,
          ArquivoSolidoId = arquivo.Id,
        };
        db.Componentes.Add(componente);
        await db.SaveChangesAsync();
        (c.Corte, c.Dobra, c.Solda, c.Pintura) = (setores[0].Id, setores[1].Id, setores[2].Id, setores[3].Id);
        c._arquivoId = arquivo.Id;
        c.ComponenteId = componente.Id;
      }

      var pcp = c.Como(c.Pcp);
      c.PedidoId = await IdAsync(await pcp.PostAsJsonAsync("/api/pedidos", new { numero = $"f3-{rotulo}", cliente = "Cliente F3" }));
      c.AgrupamentoId = await IdAsync(await pcp.PostAsJsonAsync(
          $"/api/pedidos/{c.PedidoId}/agrupamentos", new { codigo = "AG-01", tipo = "Avulso" }));
      c.A = await IdAsync(await pcp.PostAsJsonAsync(
          $"/api/agrupamentos/{c.AgrupamentoId}/estrutura",
          new { componenteId = c.ComponenteId, quantidade = 10m, requerRelatorioDimensional = false }));
      c.B = await IdAsync(await pcp.PostAsJsonAsync(
          $"/api/estrutura/{c.A}/filhos", new { componenteId = (int?)null, descricao = "Suporte", quantidade = 20m, quantidadePorPai = 2m }));
      c.C = await IdAsync(await pcp.PostAsJsonAsync(
          $"/api/estrutura/{c.A}/filhos", new { componenteId = (int?)null, descricao = "Calço", quantidade = 10m, quantidadePorPai = 1m }));
      await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.A}/roteiro", new { passos = new[] { c.Solda, c.Pintura } }));
      await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.B}/roteiro", new { passos = new[] { c.Corte } }));
      await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{c.C}/roteiro", new { passos = new[] { c.Dobra } }));
      return c;
    }
    catch
    {
      await c.DisposeAsync();
      throw;
    }
  }

  public HttpClient Como(UsuarioDeTeste usuario)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, usuario.Perfil, usuario.Id));
    return cliente;
  }

  public static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

  private static async Task<int> IdAsync(HttpResponseMessage resposta)
  {
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  /// <summary>Falha de arranjo diz o corpo, e nao so "esperava 2xx".</summary>
  public static async Task Garantir(HttpResponseMessage resposta)
  {
    if (!resposta.IsSuccessStatusCode)
      throw new InvalidOperationException(
          $"{resposta.RequestMessage?.Method} {resposta.RequestMessage?.RequestUri}: {(int)resposta.StatusCode} "
          + await resposta.Content.ReadAsStringAsync());
  }

  private async Task<UsuarioDeTeste> UsuarioAsync(string prefixo, string perfil)
  {
    var usuario = await UsuarioDeTeste.CriarAsync(_factory.Services, prefixo, perfil);
    _usuarios.Add(usuario);
    return usuario;
  }

  public async ValueTask DisposeAsync()
  {
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var ag = AgrupamentoId;
      // Ruling "Ledger cleanup by tree" (Task 10): `UsuarioDeTeste.DisposeAsync` so apaga o proprio
      // usuario e os proprios RefreshToken — nunca o livro. Por isso o livro dos nos deste cenario e
      // apagado AQUI, antes da arvore, e os usuarios sao descartados por ULTIMO (foreach abaixo):
      // estornos primeiro (EstornoDeId nao nulo), depois os demais movimentos, depois Montagem.
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Movimentacao WHERE EstornoDeId IS NOT NULL AND EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Movimentacao WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.Montagem WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag})");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {ag}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Agrupamento WHERE Id = {ag}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Pedido WHERE Id = {PedidoId}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Componente WHERE Id = {ComponenteId}");
      await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.ArquivoDeComponente WHERE Id = {_arquivoId}");
      foreach (var setor in new[] { Corte, Dobra, Solda, Pintura })
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {setor}");
    }
    foreach (var usuario in _usuarios) await usuario.DisposeAsync();
  }
}
