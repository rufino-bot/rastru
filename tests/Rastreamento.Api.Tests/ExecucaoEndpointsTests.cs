using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Rotas da Fase 3 (spec secao 9.3). Os 403 de `Perfil_sem_a_acao_recebe_403` e o 401 de
/// `Sem_token_nenhuma_leitura_responde` sao do `[Authorize(Roles)]`, que roda antes do binding e da
/// action: Ids inexistentes de proposito, sem tocar o banco. O comportamento de cada rota esta em
/// `ExecucaoEndpointsTests.Comportamento` (Task 11) — com UMA exceção, o `[Fact]`
/// `Movimentador_que_nao_e_autor_da_montagem_recebe_403_Proibido`: o 403 do CASO DE USO (`Proibido`,
/// estorno alheio) precisa de pelo menos uma prova NO NIVEL HTTP nesta task, porque
/// `ExecucaoControllerBase.Recusar` mapear `TipoDeErro.Proibido` certo é comportamento do
/// CONTROLLER, não só do caso de uso, e a Task 11 não é obrigada a cobrir
/// 403 especificamente. Esse `[Fact]`, diferente dos `[Theory]` desta classe, cria estado real no
/// banco (Setor, Pedido, Agrupamento, Componente, Peça, Montagem, dois usuários).
/// </summary>
public partial class ExecucaoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public ExecucaoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private HttpClient ClienteComo(string perfil, int usuarioId = 1)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil, usuarioId));
    return cliente;
  }

  private static StringContent Json(string corpo) => new(corpo, Encoding.UTF8, "application/json");

  [Theory]
  [InlineData("Movimentador", "POST", "/api/estrutura/999999/inicios")]
  [InlineData("PCP", "POST", "/api/estrutura/999999/terminos")]
  [InlineData("Gestao", "POST", "/api/estrutura/999999/montagens")]
  [InlineData("Operador", "POST", "/api/entregas")]
  [InlineData("PCP", "POST", "/api/entregas")]
  [InlineData("Operador", "PUT", "/api/estrutura/999999/roteiro")]
  [InlineData("Movimentador", "PUT", "/api/estrutura/999999/roteiro")]
  [InlineData("Gestao", "POST", "/api/movimentacoes/999999/estorno")]
  [InlineData("Qualidade", "POST", "/api/montagens/999999/estorno")]
  [InlineData("Almoxarifado", "POST", "/api/movimentacoes/999999/estorno")]
  public async Task Perfil_sem_a_acao_recebe_403(string perfil, string verbo, string rota)
  {
    var resposta = await ClienteComo(perfil).SendAsync(new HttpRequestMessage(new HttpMethod(verbo), rota)
    {
      Content = Json("{}"),
    });

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Theory]
  [InlineData("/api/tarefas")]
  [InlineData("/api/tarefas/contagem")]
  [InlineData("/api/setores/999999/fila")]
  [InlineData("/api/agrupamentos/999999/posicoes")]
  [InlineData("/api/estrutura/999999/movimentacoes")]
  [InlineData("/api/estrutura/999999/roteiro")]
  public async Task Sem_token_nenhuma_leitura_responde(string rota)
  {
    var resposta = await _factory.CreateClient().GetAsync(rota);

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Theory]
  [InlineData("/api/tarefas")]
  [InlineData("/api/tarefas/contagem")]
  public async Task Gestao_le_as_tarefas(string rota)
  {
    var resposta = await ClienteComo("Gestao").GetAsync(rota);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  /// <summary>
  /// `ExecucaoControllerBase.Recusar` mapeia `TipoDeErro.Proibido` para 403 com corpo
  /// `{ erro: "Proibido" }` — e nenhum teste desta task
  /// provava isso NO NIVEL HTTP sem este caso (a Task 11, dona do comportamento das rotas, nao e
  /// obrigada a cobrir especificamente o 403 do caso de uso). Prova o MAPEAMENTO do controller, nao
  /// o fluxo de negocio inteiro (isso e da Task 11): a `Montagem` nasce por insercao direta no banco
  /// — `EstornoUseCase.EstornarMontagem` so precisa achar uma `Montagem` real e comparar autoria
  /// ANTES de qualquer escrita, entao nao ha necessidade de passar por Inicio/Termino/Entrega/Montar
  /// para chegar la. Autor e requerente sao `UsuarioDeTeste` de perfil real (spec secao 9.3: cada
  /// teste cria o proprio usuario por perfil, sem depender do seed) — o autor precisa ser um usuario
  /// REAL por causa da FK de `Montagem.UsuarioId`, e usar o mesmo tipo para o requerente evita a
  /// dependencia do `admin` do seed dos dois lados. O `[Authorize(Roles)]` deixa o Movimentador
  /// passar (esta na lista, desvio D2), e o caso de uso recusa por autoria — o Movimentador nunca e
  /// autor de uma Montagem (precedente do `05`).
  /// </summary>
  [Fact]
  public async Task Movimentador_que_nao_e_autor_da_montagem_recebe_403_Proibido()
  {
    var administrador = ClienteComo("Administrador");
    var pcp = ClienteComo("PCP");

    await using var autor = await UsuarioDeTeste.CriarAsync(_factory.Services, "exec-proib-op", "Operador");
    await using var requerente = await UsuarioDeTeste.CriarAsync(_factory.Services, "exec-proib-mv", "Movimentador");
    var movimentador = ClienteComo("Movimentador", requerente.Id);

    // Nulos ate serem criados: um erro de arranjo no meio nao deixa Id nenhum sem entrada no
    // `finally`, e a limpeza (por FK) so tenta apagar o que de fato foi gravado.
    int? setorId = null, pedidoId = null, agrupamentoId = null, componenteId = null, arquivoId = null,
        pecaId = null, montagemId = null;
    try
    {
      var nomeDoSetor = $"exec-proibido-{Guid.NewGuid():N}";
      var respostaSetor = await administrador.PostAsJsonAsync("/api/setores", new { nome = nomeDoSetor });
      Assert.Equal(HttpStatusCode.Created, respostaSetor.StatusCode);
      setorId = await IdDoCorpo(respostaSetor);

      var numeroDoPedido = $"ped-proibido-{Guid.NewGuid():N}"[..25];
      var respostaPedido = await pcp.PostAsJsonAsync("/api/pedidos", new { numero = numeroDoPedido, cliente = "Cliente de teste" });
      Assert.Equal(HttpStatusCode.Created, respostaPedido.StatusCode);
      pedidoId = await IdDoCorpo(respostaPedido);

      var respostaAgrupamento = await pcp.PostAsJsonAsync($"/api/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", tipo = "Kit" });
      Assert.Equal(HttpStatusCode.Created, respostaAgrupamento.StatusCode);
      agrupamentoId = await IdDoCorpo(respostaAgrupamento);

      using var escopoDoComponente = _factory.Services.CreateScope();
      var dbDoComponente = escopoDoComponente.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var arquivo = new ArquivoDeComponente
      {
        NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = autor.Id,
      };
      dbDoComponente.ArquivosDeComponente.Add(arquivo);
      await dbDoComponente.SaveChangesAsync();
      arquivoId = arquivo.Id;

      var componente = new Componente
      {
        Codigo = $"EX-{Guid.NewGuid():N}"[..12], Descricao = "Componente do teste de Proibido",
        Tipo = "Fabricado", Ativo = true, ArquivoSolidoId = arquivo.Id,
      };
      dbDoComponente.Componentes.Add(componente);
      await dbDoComponente.SaveChangesAsync();
      componenteId = componente.Id;

      var respostaPeca = await pcp.PostAsJsonAsync(
          $"/api/agrupamentos/{agrupamentoId}/estrutura",
          new { componenteId, quantidade = 1m, requerRelatorioDimensional = false });
      Assert.Equal(HttpStatusCode.Created, respostaPeca.StatusCode);
      pecaId = await IdDoCorpo(respostaPeca);

      var montagem = new Montagem
      {
        EstruturaItemId = pecaId.Value, SetorId = setorId.Value, Quantidade = 1m,
        DataHora = DateTime.UtcNow, UsuarioId = autor.Id,
      };
      dbDoComponente.Montagens.Add(montagem);
      await dbDoComponente.SaveChangesAsync();
      montagemId = montagem.Id;

      var respostaEstorno = await movimentador.PostAsync($"/api/montagens/{montagemId}/estorno", null);

      Assert.Equal(HttpStatusCode.Forbidden, respostaEstorno.StatusCode);
      var corpo = JsonDocument.Parse(await respostaEstorno.Content.ReadAsStringAsync()).RootElement;
      Assert.Equal("Proibido", corpo.GetProperty("erro").GetString());
    }
    finally
    {
      using var escopo = _factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

      // Por FK, e so o que de fato foi criado: Montagem antes de EstruturaItem/Setor;
      // Agrupamento/Pedido por ultimo; Componente/ArquivoDeComponente por fora da arvore do
      // Pedido (mesma ordem de EstruturaEndpointsTests.DisposeAsync). Isto roda ANTES do
      // `await using` de `autor`/`requerente` descartar os dois usuarios — a `Montagem` tem FK
      // para `Usuario` (autor.Id), e apagar o usuario antes dela estouraria
      // `FK_Montagem_Usuario`.
      if (montagemId is int mId)
      {
        db.Montagens.RemoveRange(await db.Montagens.Where(m => m.Id == mId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (pecaId is int pId)
      {
        db.Estruturas.RemoveRange(await db.Estruturas.Where(e => e.Id == pId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (agrupamentoId is int agId)
      {
        db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.Id == agId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (pedidoId is int pdId)
      {
        db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pdId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (componenteId is int cId)
      {
        db.Componentes.RemoveRange(await db.Componentes.Where(c => c.Id == cId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (arquivoId is int aId)
      {
        db.ArquivosDeComponente.RemoveRange(await db.ArquivosDeComponente.Where(a => a.Id == aId).ToListAsync());
        await db.SaveChangesAsync();
      }
      if (setorId is int sId)
      {
        db.Setores.RemoveRange(await db.Setores.Where(s => s.Id == sId).ToListAsync());
        await db.SaveChangesAsync();
      }
    }
  }

  private static async Task<int> IdDoCorpo(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
}
