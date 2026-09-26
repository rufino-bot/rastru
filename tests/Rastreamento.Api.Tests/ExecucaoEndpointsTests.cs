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
/// Rotas da Fase 3 (spec secao 9.3). Os 403 desta classe sao do `[Authorize(Roles)]`, que roda antes do
/// binding e da action: Ids inexistentes de proposito, sem tocar o banco. O comportamento de cada rota
/// esta em `ExecucaoEndpointsTests.Comportamento` (Task 11) — com UMA exceção, medida abaixo: o 403 do
/// CASO DE USO (`Proibido`, estorno alheio) precisa de pelo menos uma prova NO NIVEL HTTP nesta task
/// (ruling do controlador sobre a review da Task 7 — ver `Movimentador_que_nao_e_autor_da_montagem_recebe_403_Proibido`),
/// porque `ExecucaoControllerBase.Recusar` mapear `TipoDeErro.Proibido` certo é comportamento do
/// CONTROLLER, não só do caso de uso, e a Task 11 não é obrigada a cobrir 403 especificamente.
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
  /// Ruling do controlador sobre a review da Task 7: `ExecucaoControllerBase.Recusar` mapeia
  /// `TipoDeErro.Proibido` para 403 com corpo `{ erro: "Proibido" }` — e nenhum teste desta task
  /// provava isso NO NIVEL HTTP sem este caso (a Task 11, dona do comportamento das rotas, nao e
  /// obrigada a cobrir especificamente o 403 do caso de uso). Prova o MAPEAMENTO do controller, nao
  /// o fluxo de negocio inteiro (isso e da Task 11): a `Montagem` nasce por insercao direta no banco
  /// — `EstornoUseCase.EstornarMontagem` so precisa achar uma `Montagem` real e comparar autoria
  /// ANTES de qualquer escrita, entao nao ha necessidade de passar por Inicio/Termino/Entrega/Montar
  /// para chegar la. Autor: usuarioId 1 (o `admin` real do seed — FK de autor exige linha real de
  /// Usuario). Quem tenta estornar: Movimentador com OUTRO usuarioId (2, nunca grava nada: o
  /// `Proibido` sai ANTES de qualquer escrita, entao nao precisa ser usuario real). O
  /// `[Authorize(Roles)]` deixa o Movimentador passar (esta na lista, desvio D2), e o caso de uso
  /// recusa por autoria — o Movimentador nunca e autor de uma Montagem (precedente do `05`).
  /// </summary>
  [Fact]
  public async Task Movimentador_que_nao_e_autor_da_montagem_recebe_403_Proibido()
  {
    var administrador = ClienteComo("Administrador");
    var pcp = ClienteComo("PCP");
    var movimentador = ClienteComo("Movimentador", usuarioId: 2);

    var nomeDoSetor = $"exec-proibido-{Guid.NewGuid():N}";
    var respostaSetor = await administrador.PostAsJsonAsync("/api/setores", new { nome = nomeDoSetor });
    Assert.Equal(HttpStatusCode.Created, respostaSetor.StatusCode);
    var setorId = await IdDoCorpo(respostaSetor);

    var numeroDoPedido = $"ped-proibido-{Guid.NewGuid():N}"[..25];
    var respostaPedido = await pcp.PostAsJsonAsync("/api/pedidos", new { numero = numeroDoPedido, cliente = "Cliente de teste" });
    var pedidoId = await IdDoCorpo(respostaPedido);
    var respostaAgrupamento = await pcp.PostAsJsonAsync($"/api/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", tipo = "Kit" });
    var agrupamentoId = await IdDoCorpo(respostaAgrupamento);

    using var escopoDoComponente = _factory.Services.CreateScope();
    var dbDoComponente = escopoDoComponente.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var arquivo = new ArquivoDeComponente { NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = 1 };
    dbDoComponente.ArquivosDeComponente.Add(arquivo);
    await dbDoComponente.SaveChangesAsync();
    var componente = new Componente
    {
      Codigo = $"EX-{Guid.NewGuid():N}"[..12], Descricao = "Componente do teste de Proibido",
      Tipo = "Fabricado", Ativo = true, ArquivoSolidoId = arquivo.Id,
    };
    dbDoComponente.Componentes.Add(componente);
    await dbDoComponente.SaveChangesAsync();
    var componenteId = componente.Id;

    var respostaPeca = await pcp.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura",
        new { componenteId, quantidade = 1m, requerRelatorioDimensional = false });
    Assert.Equal(HttpStatusCode.Created, respostaPeca.StatusCode);
    var pecaId = await IdDoCorpo(respostaPeca);

    var montagem = new Montagem
    {
      EstruturaItemId = pecaId, SetorId = setorId, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = 1,
    };
    dbDoComponente.Montagens.Add(montagem);
    await dbDoComponente.SaveChangesAsync();
    var montagemId = montagem.Id;

    try
    {
      var respostaEstorno = await movimentador.PostAsync($"/api/montagens/{montagemId}/estorno", null);

      Assert.Equal(HttpStatusCode.Forbidden, respostaEstorno.StatusCode);
      var corpo = JsonDocument.Parse(await respostaEstorno.Content.ReadAsStringAsync()).RootElement;
      Assert.Equal("Proibido", corpo.GetProperty("erro").GetString());
    }
    finally
    {
      using var escopo = _factory.Services.CreateScope();
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

      // Ordem por FK: Montagem antes de EstruturaItem/Setor; Agrupamento/Pedido por ultimo; e
      // Componente/ArquivoDeComponente por fora da arvore do Pedido (mesma ordem de
      // EstruturaEndpointsTests.DisposeAsync).
      db.Montagens.RemoveRange(await db.Montagens.Where(m => m.Id == montagemId).ToListAsync());
      await db.SaveChangesAsync();
      db.Estruturas.RemoveRange(await db.Estruturas.Where(e => e.Id == pecaId).ToListAsync());
      await db.SaveChangesAsync();
      db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => a.Id == agrupamentoId).ToListAsync());
      await db.SaveChangesAsync();
      db.Pedidos.RemoveRange(await db.Pedidos.Where(p => p.Id == pedidoId).ToListAsync());
      await db.SaveChangesAsync();
      db.Componentes.RemoveRange(await db.Componentes.Where(c => c.Id == componenteId).ToListAsync());
      await db.SaveChangesAsync();
      db.ArquivosDeComponente.RemoveRange(await db.ArquivosDeComponente.Where(a => a.Id == arquivo.Id).ToListAsync());
      await db.SaveChangesAsync();
      db.Setores.RemoveRange(await db.Setores.Where(s => s.Id == setorId).ToListAsync());
      await db.SaveChangesAsync();
    }
  }

  private static async Task<int> IdDoCorpo(HttpResponseMessage resposta) =>
      JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
}
