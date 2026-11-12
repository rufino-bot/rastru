using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;
using static Rastreamento.Api.Tests.CenarioDaFase3NaApi;

namespace Rastreamento.Api.Tests;

/// <summary>
/// "Usar a sobra" e o estorno dele (secao 4.4 da spec da Fase 5B-2, C4, C9, C11, C12, C18, C19; decisoes P2 e P6 do
/// plano de backend da Fase 5B-2), no cenario da Fase 3: Peca A (10, copiada do catalogo; Solda -> Pintura), B (20,
/// razao 2, "Suporte"; Corte) e C (10, razao 1, "Calço"; Dobra). A sobra nasce de uma baixa de A "a iniciar": o que B
/// e C ja fizeram alem do que A ainda pede vira sobra viva, aguardando coleta (B) e aguardando montagem (C), e o
/// Retrabalho aberto dessa baixa recebe a sobra como pronto. Toda assercao escopada no cenario e no Retrabalho dele.
/// </summary>
public class ReaproveitamentoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private const string SemAtraso = "2099-12-31";

  private readonly WebApplicationFactory<Program> _factory;

  public ReaproveitamentoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  private static object DaColeta(int no, int setor, int ordem, decimal quantidade) =>
      new { estruturaItemId = no, origem = new { posicao = "AguardandoColeta", setorId = setor, ordem = (int?)ordem }, destinoSetorId = (int?)null, quantidade };

  private static async Task IniciarETerminarAsync(CenarioDaFase3NaApi c, int no, int setor, decimal quantidade)
  {
    var operador = c.Como(c.Operador);
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/inicios", new { setorId = setor, quantidade }));
    await Garantir(await operador.PostAsJsonAsync($"/api/estrutura/{no}/terminos", new { setorId = setor, ordem = 1, quantidade }));
  }

  private static async Task EntregarAsync(CenarioDaFase3NaApi c, params object[] itens) =>
      await Garantir(await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new { itens }));

  /// <summary>Uma baixa de "a iniciar" de A por perda; devolve o Id dela.</summary>
  private static async Task<int> PerderAAsync(CenarioDaFase3NaApi c, decimal quantidade)
  {
    var resposta = await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.A}/baixas", new
    {
      motivo = "PerdaArmazem", observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "AIniciar", setorId = (int?)null, ordem = (int?)null, quantidade } },
      incluirTopo = false,
    });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  /// <summary>Abre o Retrabalho da baixa; devolve o Id dele.</summary>
  private static async Task<int> AbrirRetrabalhoAsync(CenarioDaFase3NaApi c, int perdaId)
  {
    var numero = (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{c.PedidoId}"))).GetProperty("numero").GetString();
    var resposta = await c.Como(c.Pcp).PostAsJsonAsync($"/api/pedidos/{c.PedidoId}/retrabalhos",
        new { perdaId, numero = $"{numero}-R1", dataEntrega = SemAtraso, motivoRetrabalho = "Perda" });
    await Garantir(resposta);
    return (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
  }

  /// <summary>
  /// B faz as 20 no Corte (aguardando coleta) e C as 10 na Dobra, entregues a montagem na Solda; A perde `perdidas` "a
  /// iniciar". Sobram 2 x `perdidas` de B aguardando coleta e `perdidas` de C aguardando montagem. Devolve o Id da baixa.
  /// </summary>
  private static async Task<int> SobraNasDuasPosicoesAsync(CenarioDaFase3NaApi c, decimal perdidas = 1m)
  {
    await IniciarETerminarAsync(c, c.B, c.Corte, 20m);
    await IniciarETerminarAsync(c, c.C, c.Dobra, 10m);
    await EntregarAsync(c, DaColeta(c.C, c.Dobra, 1, 10m));
    return await PerderAAsync(c, perdidas);
  }

  /// <summary>
  /// E, filho de A copiado do catalogo, sem descricao propria (o nome e o do Componente, "Chassi da Fase 3"; regra 19),
  /// 10 com razao 1 no Corte, faz as 10; A perde 1 "a iniciar": sobra 1 de E aguardando coleta. Devolve E e a baixa.
  /// </summary>
  private static async Task<(int E, int PerdaId)> SobraDoFilhoDoCatalogoAsync(CenarioDaFase3NaApi c)
  {
    var criado = await c.Como(c.Pcp).PostAsJsonAsync($"/api/estrutura/{c.A}/filhos",
        new { componenteId = (int?)c.ComponenteId, descricao = (string?)null, quantidade = 10m, quantidadePorPai = 1m });
    await Garantir(criado);
    var e = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    await Garantir(await c.Como(c.Pcp).PutAsJsonAsync($"/api/estrutura/{e}/roteiro", new { passos = new[] { c.Corte } }));
    await IniciarETerminarAsync(c, e, c.Corte, 10m);
    return (e, await PerderAAsync(c, 1m));
  }

  /// <summary>O no do Retrabalho que veio do no <paramref name="origem"/> do original (C14).</summary>
  private async Task<int> CopiaDeAsync(int retrabalhoId, int origem)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await (from e in db.Estruturas.AsNoTracking()
                  join a in db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
                  where a.PedidoId == retrabalhoId && e.OrigemEstruturaItemId == origem
                  select e.Id).SingleAsync();
  }

  /// <summary>O saldo liquido do no numa posicao, somado do livro (destinos menos origens; "a iniciar" sem a quantidade).</summary>
  private async Task<decimal> LiquidoAsync(int no, string posicao)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var linhas = await db.Movimentacoes.AsNoTracking().Where(m => m.EstruturaItemId == no).ToListAsync();
    return linhas.Where(m => m.DestinoPosicao == posicao).Sum(m => m.Quantidade)
           - linhas.Where(m => m.OrigemPosicao == posicao).Sum(m => m.Quantidade);
  }

  private async Task<List<Movimentacao>> LivroAsync(params int[] nos)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Movimentacoes.AsNoTracking().Where(m => nos.Contains(m.EstruturaItemId)).OrderBy(m => m.Id).ToListAsync();
  }

  private async Task<List<Perda>> PerdasAsync(params int[] pedidos)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return await db.Perdas.AsNoTracking().Where(p => pedidos.Contains(p.PedidoId)).OrderBy(p => p.Id).ToListAsync();
  }

  private async Task<string> NomeDoSetorAsync(int setorId)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return (await db.Setores.AsNoTracking().SingleAsync(s => s.Id == setorId)).Nome;
  }

  private async Task<string> StatusAsync(int pedidoId)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return (await db.Pedidos.AsNoTracking().SingleAsync(p => p.Id == pedidoId)).Status;
  }

  private static object Item(int no, string posicao, int? setorId, int? ordem, decimal quantidade) =>
      new { estruturaItemId = no, posicao, setorId, ordem, quantidade };

  private static Task<HttpResponseMessage> UsarAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int retrabalhoId, params object[] itens) =>
      c.Como(quem).PostAsJsonAsync($"/api/pedidos/{retrabalhoId}/sobra-reaproveitada", new { itens });

  private static Task<HttpResponseMessage> PreverAsync(CenarioDaFase3NaApi c, UsuarioDeTeste quem, int retrabalhoId) =>
      c.Como(quem).GetAsync($"/api/pedidos/{retrabalhoId}/sobra-reaproveitavel");

  private static async Task<(HttpStatusCode Status, string? Erro, string? Mensagem)> RecusaAsync(HttpResponseMessage resposta)
  {
    var texto = await resposta.Content.ReadAsStringAsync();
    if (string.IsNullOrEmpty(texto)) return (resposta.StatusCode, null, null);
    var corpo = JsonDocument.Parse(texto).RootElement;
    return (resposta.StatusCode,
        corpo.TryGetProperty("erro", out var e) ? e.GetString() : null,
        corpo.TryGetProperty("mensagem", out var m) ? m.GetString() : null);
  }

  [Fact]
  public async Task Excluir_no_do_Retrabalho_que_recebeu_pronto_e_409_NoComMovimentacao_e_o_livro_fica()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await SobraNasDuasPosicoesAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (b, cc) = (await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));
    await Garantir(await UsarAsync(c, c.Pcp, retrabalhoId,
        Item(b, "AguardandoColeta", c.Corte, 1, 2m), Item(cc, "AguardandoMontagem", c.Solda, null, 1m)));
    var antes = (await LivroAsync(b, cc)).Count;
    Assert.Equal("Aberto", await StatusAsync(retrabalhoId));

    var resposta = await c.Como(c.Pcp).DeleteAsync($"/api/estrutura/{b}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var (status, erro, mensagem) = await RecusaAsync(resposta);
    Assert.Equal((HttpStatusCode.Conflict, "NoComMovimentacao"), (status, erro));
    Assert.False(string.IsNullOrWhiteSpace(mensagem));
    // Nada apagado: os nos e o livro continuam.
    Assert.Equal(antes, (await LivroAsync(b, cc)).Count);
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    Assert.Equal(2, await db.Estruturas.AsNoTracking().CountAsync(e => e.Id == b || e.Id == cc));
  }

  [Fact]
  public async Task Previa_lista_a_sobra_por_posicao_e_sugere_o_que_o_no_precisa()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await SobraNasDuasPosicoesAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (b, cc) = (await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));

    var resposta = await PreverAsync(c, c.Pcp, retrabalhoId);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var itens = (await CorpoAsync(resposta)).GetProperty("itens").EnumerateArray()
        .Select(i => string.Join("|",
            i.GetProperty("estruturaItemId").GetInt32(), i.GetProperty("descricao").GetString(),
            i.GetProperty("posicao").GetString(), i.GetProperty("setorId").ToString(), i.GetProperty("setorNome").ToString(),
            i.GetProperty("ordem").ToString(), i.GetProperty("sobra").GetDecimal().ToString("0.####"),
            i.GetProperty("precisa").GetDecimal().ToString("0.####"), i.GetProperty("sugerido").GetDecimal().ToString("0.####")))
        .ToList();
    var (corte, solda) = (await NomeDoSetorAsync(c.Corte), await NomeDoSetorAsync(c.Solda));
    Assert.Equal(
        new[]
        {
          $"{b}|Suporte|AguardandoColeta|{c.Corte}|{corte}|1|2|2|2",
          $"{cc}|Calço|AguardandoMontagem|{c.Solda}|{solda}||1|1|1",
        },
        itens);
  }

  [Fact]
  public async Task Usar_a_sobra_grava_a_baixa_no_original_e_o_pronto_no_retrabalho()
  {
    await using var c = await CriarAsync(_factory);
    var perdaId = await SobraNasDuasPosicoesAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (b, cc) = (await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));

    var resposta = await UsarAsync(c, c.Pcp, retrabalhoId,
        Item(b, "AguardandoColeta", c.Corte, 1, 2m), Item(cc, "AguardandoMontagem", c.Solda, null, 1m));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = await CorpoAsync(resposta);
    var baixaId = corpo.GetProperty("id").GetInt32();
    Assert.Equal(("Reaproveitado", retrabalhoId, false, true),
        (corpo.GetProperty("motivo").GetString(), corpo.GetProperty("pedidoRetrabalho").GetProperty("id").GetInt32(),
         corpo.GetProperty("estornada").GetBoolean(), corpo.GetProperty("estornavel").GetBoolean()));
    // As linhas da baixa sao as do original, nunca os `Pronto` do Retrabalho que a apontam.
    Assert.Equal(
        new[] { $"{c.B}|AguardandoColeta|2", $"{c.C}|AguardandoMontagem|1" },
        corpo.GetProperty("linhas").EnumerateArray()
            .Select(l => $"{l.GetProperty("estruturaItemId").GetInt32()}|{l.GetProperty("origemPosicao").GetString()}|{l.GetProperty("quantidade").GetDecimal():0.####}"));

    var baixa = (await PerdasAsync(c.PedidoId)).Single(p => p.Id == baixaId);
    Assert.Equal(("Reaproveitado", c.PedidoId, (int?)c.A, (int?)retrabalhoId, false),
        (baixa.Motivo, baixa.PedidoId, baixa.EstruturaItemId, baixa.PedidoRetrabalhoId, baixa.Tudo));

    // No original, a sobra saiu da posicao dela para `Perdido`.
    Assert.Equal(18m, await LiquidoAsync(c.B, "AguardandoColeta"));
    Assert.Equal(9m, await LiquidoAsync(c.C, "AguardandoMontagem"));

    // No Retrabalho, um `Pronto` por linha, na mesma posicao fisica, apontando a baixa do original.
    var prontos = (await LivroAsync(b, cc)).Where(m => m.Tipo == "Pronto").ToList();
    Assert.Equal(
        new[] { $"{b}|AIniciar|AguardandoColeta|{c.Corte}|1|2|{baixaId}", $"{cc}|AIniciar|AguardandoMontagem|{c.Solda}||1|{baixaId}" },
        prontos.Select(m => $"{m.EstruturaItemId}|{m.OrigemPosicao}|{m.DestinoPosicao}|{m.DestinoSetorId}|{m.DestinoOrdem}|{m.Quantidade:0.####}|{m.PerdaId}"));

    // C11: o pronto nao e inicio.
    Assert.Equal("Aberto", await StatusAsync(retrabalhoId));
    // Nada a dispensar: B e C sao folhas. So a baixa do original.
    Assert.Empty(await PerdasAsync(retrabalhoId));

    // As Tarefas mostram o pronto de B aguardando coleta no Corte.
    var tarefas = await CorpoAsync(await c.Como(c.Movimentador).GetAsync("/api/tarefas"));
    var deB = tarefas.GetProperty("grupos").EnumerateArray()
        .SelectMany(g => g.GetProperty("itens").EnumerateArray())
        .Single(i => i.GetProperty("no").GetProperty("id").GetInt32() == b);
    Assert.Equal((1, 2m), (deB.GetProperty("ordem").GetInt32(), deB.GetProperty("quantidade").GetDecimal()));

    // A baixa aparece nas baixas do original, com as linhas do original so.
    var doOriginal = (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/pedidos/{c.PedidoId}/baixas"))).EnumerateArray()
        .Single(p => p.GetProperty("id").GetInt32() == baixaId);
    Assert.Equal(2, doOriginal.GetProperty("linhas").GetArrayLength());
  }

  [Fact]
  public async Task Pronto_de_pai_dispensa_os_filhos_numa_baixa_ligada()
  {
    await using var c = await CriarAsync(_factory);
    var d = await FilhoDeCAsync(c);
    var perdaId = await SobraDeCComFilhoAsync(c, d);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (cc, dd) = (await CopiaDeAsync(retrabalhoId, c.C), await CopiaDeAsync(retrabalhoId, d));

    var resposta = await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoColeta", c.Dobra, 1, 1m));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var baixaId = (await CorpoAsync(resposta)).GetProperty("id").GetInt32();
    // C' pronto ja esta montado (C17): a D' que ia a montagem dele sai, por baixa de `Descarte` no Retrabalho (C18),
    // ligada a do original (decisao P2 do plano de backend da Fase 5B-2).
    var ligada = Assert.Single(await PerdasAsync(retrabalhoId));
    Assert.Equal(("Descarte", false, (int?)baixaId, (int?)null),
        (ligada.Motivo, ligada.Tudo, ligada.PerdaDeOrigemId, ligada.PedidoRetrabalhoId));
    var linhaDeD = Assert.Single(await LivroAsync(dd), m => m.Tipo == "Perda");
    Assert.Equal(("AIniciar", "Perdido", 1m, (int?)ligada.Id),
        (linhaDeD.OrigemPosicao, linhaDeD.DestinoPosicao, linhaDeD.Quantidade, linhaDeD.PerdaId));
    Assert.Equal(1m, await LiquidoAsync(cc, "AguardandoColeta"));
    Assert.Equal("Aberto", await StatusAsync(retrabalhoId));
  }

  /// <summary>Um filho D de C (10, razao 1, "Arruela"; Corte), acrescentado antes de qualquer inicio.</summary>
  private static async Task<int> FilhoDeCAsync(CenarioDaFase3NaApi c)
  {
    var pcp = c.Como(c.Pcp);
    var criado = await pcp.PostAsJsonAsync($"/api/estrutura/{c.C}/filhos",
        new { componenteId = (int?)null, descricao = "Arruela", quantidade = 10m, quantidadePorPai = 1m });
    await Garantir(criado);
    var d = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{d}/roteiro", new { passos = new[] { c.Corte } }));
    return d;
  }

  /// <summary>D vai a montagem de C na Dobra, C monta as 10 e as termina; A perde 1: sobra 1 de C aguardando coleta.</summary>
  private static async Task<int> SobraDeCComFilhoAsync(CenarioDaFase3NaApi c, int d)
  {
    await IniciarETerminarAsync(c, d, c.Corte, 10m);
    await EntregarAsync(c, DaColeta(d, c.Corte, 1, 10m));
    await IniciarETerminarAsync(c, c.C, c.Dobra, 10m);
    return await PerderAAsync(c, 1m);
  }

  [Fact]
  public async Task Sobra_ja_na_Solda_de_um_Kit_vira_pronto_e_o_irmao_completa()
  {
    await using var c = await CriarAsync(_factory, kit: true);
    await IniciarETerminarAsync(c, c.B, c.Corte, 20m);
    await IniciarETerminarAsync(c, c.C, c.Dobra, 10m);
    await EntregarAsync(c, DaColeta(c.B, c.Corte, 1, 20m), DaColeta(c.C, c.Dobra, 1, 10m));
    var perdaId = await PerderAAsync(c, 1m);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (a, b, cc) = (await CopiaDeAsync(retrabalhoId, c.A), await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));

    // So a C' vira pronto, ja na Solda: um conjunto em formacao (D8).
    await Garantir(await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoMontagem", c.Solda, null, 1m)));

    // A B' fabricada entra sozinha para completar o conjunto, e o inicio de A' monta.
    await IniciarETerminarAsync(c, b, c.Corte, 2m);
    var entrega = await c.Como(c.Movimentador).PostAsJsonAsync("/api/entregas", new { itens = new[] { DaColeta(b, c.Corte, 1, 2m) } });
    Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
    var inicio = await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{a}/inicios", new { setorId = c.Solda, quantidade = 1m });
    Assert.Equal(HttpStatusCode.Created, inicio.StatusCode);
    Assert.Equal(0m, await LiquidoAsync(cc, "AguardandoMontagem"));
    Assert.Equal(0m, await LiquidoAsync(b, "AguardandoMontagem"));
    Assert.Equal(1m, await LiquidoAsync(cc, "Montado"));
  }

  [Fact]
  public async Task Usar_a_sobra_com_Retrabalho_em_producao_e_409_RetrabalhoForaDeAberto()
  {
    await using var c = await CriarAsync(_factory);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraNasDuasPosicoesAsync(c));
    var b = await CopiaDeAsync(retrabalhoId, c.B);
    await Garantir(await c.Como(c.Operador).PostAsJsonAsync($"/api/estrutura/{b}/inicios", new { setorId = c.Corte, quantidade = 1m }));
    Assert.Equal("EmProducao", await StatusAsync(retrabalhoId));

    var (status, erro, _) = await RecusaAsync(await UsarAsync(c, c.Pcp, retrabalhoId, Item(b, "AguardandoColeta", c.Corte, 1, 1m)));
    var (statusDaPrevia, erroDaPrevia, _) = await RecusaAsync(await PreverAsync(c, c.Pcp, retrabalhoId));

    Assert.Equal((HttpStatusCode.Conflict, "RetrabalhoForaDeAberto"), (status, erro));
    Assert.Equal((HttpStatusCode.Conflict, "RetrabalhoForaDeAberto"), (statusDaPrevia, erroDaPrevia));
    Assert.Single(await PerdasAsync(c.PedidoId));
    Assert.Equal(20m, await LiquidoAsync(c.B, "AguardandoColeta"));
  }

  [Fact]
  public async Task Usar_a_sobra_num_Pedido_que_nao_e_Retrabalho_e_404()
  {
    await using var c = await CriarAsync(_factory);
    await SobraNasDuasPosicoesAsync(c);

    var (status, _, _) = await RecusaAsync(await UsarAsync(c, c.Pcp, c.PedidoId, Item(c.B, "AguardandoColeta", c.Corte, 1, 1m)));

    Assert.Equal(HttpStatusCode.NotFound, status);
    Assert.Equal(HttpStatusCode.NotFound, (await PreverAsync(c, c.Pcp, c.PedidoId)).StatusCode);
  }

  [Fact]
  public async Task PCP_sim_Qualidade_403()
  {
    await using var c = await CriarAsync(_factory);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraNasDuasPosicoesAsync(c));
    var b = await CopiaDeAsync(retrabalhoId, c.B);

    Assert.Equal(HttpStatusCode.Forbidden, (await PreverAsync(c, c.Qualidade, retrabalhoId)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await UsarAsync(c, c.Qualidade, retrabalhoId, Item(b, "AguardandoColeta", c.Corte, 1, 2m))).StatusCode);
    Assert.Single(await PerdasAsync(c.PedidoId));

    Assert.Equal(HttpStatusCode.OK, (await PreverAsync(c, c.Pcp, retrabalhoId)).StatusCode);
    Assert.Equal(HttpStatusCode.Created,
        (await UsarAsync(c, c.Pcp, retrabalhoId, Item(b, "AguardandoColeta", c.Corte, 1, 2m))).StatusCode);
  }

  [Fact]
  public async Task Original_concluido_aceita_o_reaproveitamento()
  {
    await using var c = await CriarAsync(_factory);
    // A perde as 10: o original conclui, com toda a B e a C como sobra viva (C24 da spec da Fase 5B-1).
    var perdaId = await SobraNasDuasPosicoesAsync(c, perdidas: 10m);
    Assert.Equal("Concluido", await StatusAsync(c.PedidoId));
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (b, cc) = (await CopiaDeAsync(retrabalhoId, c.B), await CopiaDeAsync(retrabalhoId, c.C));

    var resposta = await UsarAsync(c, c.Pcp, retrabalhoId,
        Item(b, "AguardandoColeta", c.Corte, 1, 20m), Item(cc, "AguardandoMontagem", c.Solda, null, 10m));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    // C9: o original nao reabre e nao muda de status.
    Assert.Equal("Concluido", await StatusAsync(c.PedidoId));
    Assert.Equal(0m, await LiquidoAsync(c.B, "AguardandoColeta"));
    Assert.Equal(20m, await LiquidoAsync(b, "AguardandoColeta"));
    Assert.Equal("Aberto", await StatusAsync(retrabalhoId));
  }

  private static Task<HttpResponseMessage> DescartarAsVinteDeBAsync(CenarioDaFase3NaApi c) =>
      c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{c.B}/baixas", new
      {
        motivo = "Descarte", observacao = (string?)null, tudo = false,
        origens = new[] { new { posicao = "AguardandoColeta", setorId = (int?)c.Corte, ordem = (int?)1, quantidade = 20m } },
        incluirTopo = false,
      });

  // As duas ordens da corrida, uma de cada vez: o teste concorrente abaixo, na bancada, cai sempre na mesma.

  [Fact]
  public async Task Descarte_da_sobra_antes_deixa_o_usar_a_sobra_em_409_AlemDaSobra()
  {
    await using var c = await CriarAsync(_factory);
    var (e, perdaId) = await SobraDoFilhoDoCatalogoAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var ee = await CopiaDeAsync(retrabalhoId, e);
    // O descarte leva as 10 de E aguardando coleta no original, a sobra inclusive.
    await Garantir(await c.Como(c.Qualidade).PostAsJsonAsync($"/api/estrutura/{e}/baixas", new
    {
      motivo = "Descarte", observacao = (string?)null, tudo = false,
      origens = new[] { new { posicao = "AguardandoColeta", setorId = (int?)c.Corte, ordem = (int?)1, quantidade = 10m } },
      incluirTopo = false,
    }));

    var (status, erro, mensagem) = await RecusaAsync(await UsarAsync(c, c.Pcp, retrabalhoId, Item(ee, "AguardandoColeta", c.Corte, 1, 1m)));

    Assert.Equal((HttpStatusCode.Conflict, "AlemDaSobra"), (status, erro));
    Assert.Equal("Só sobram 0 de Chassi da Fase 3 nessa posição no Pedido de origem.", mensagem);
    Assert.DoesNotContain(await LivroAsync(ee), m => m.Tipo == "Pronto");
  }

  [Fact]
  public async Task Usar_a_sobra_antes_deixa_o_descarte_alem_do_saldo_em_409_SaldoInsuficiente()
  {
    await using var c = await CriarAsync(_factory);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraNasDuasPosicoesAsync(c));
    var b = await CopiaDeAsync(retrabalhoId, c.B);
    await Garantir(await UsarAsync(c, c.Pcp, retrabalhoId, Item(b, "AguardandoColeta", c.Corte, 1, 2m)));

    var (status, erro, _) = await RecusaAsync(await DescartarAsVinteDeBAsync(c));

    Assert.Equal((HttpStatusCode.Conflict, "SaldoInsuficiente"), (status, erro));
    Assert.Equal(18m, await LiquidoAsync(c.B, "AguardandoColeta"));
  }

  [Fact]
  public async Task Usar_a_sobra_enquanto_se_descarta_a_mesma_sobra_um_passa_e_o_outro_e_409()
  {
    await using var c = await CriarAsync(_factory);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraNasDuasPosicoesAsync(c));
    var b = await CopiaDeAsync(retrabalhoId, c.B);

    // O descarte leva as 20 de B aguardando coleta: se ele vem antes, nao sobra nada a usar (409 `AlemDaSobra`); se o
    // "Usar a sobra" vem antes, so ficam 18 e o descarte de 20 nao cabe (409 `SaldoInsuficiente`).
    var respostas = await Task.WhenAll(
        UsarAsync(c, c.Pcp, retrabalhoId, Item(b, "AguardandoColeta", c.Corte, 1, 2m)),
        DescartarAsVinteDeBAsync(c));

    var (usar, descartar) = (respostas[0], respostas[1]);
    Assert.True(await LiquidoAsync(c.B, "AguardandoColeta") >= 0m);
    Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
    var recusada = respostas.Single(r => r.StatusCode != HttpStatusCode.Created);
    var (status, erro, _) = await RecusaAsync(recusada);
    Assert.Equal(HttpStatusCode.Conflict, status);
    if (usar.StatusCode == HttpStatusCode.Created)
    {
      Assert.Equal("SaldoInsuficiente", erro);
      Assert.Equal(18m, await LiquidoAsync(c.B, "AguardandoColeta"));
      Assert.Equal(2m, await LiquidoAsync(b, "AguardandoColeta"));
    }
    else
    {
      Assert.Equal("AlemDaSobra", erro);
      Assert.Equal(HttpStatusCode.Created, descartar.StatusCode);
      Assert.Equal(0m, await LiquidoAsync(c.B, "AguardandoColeta"));
      Assert.DoesNotContain(await LivroAsync(b), m => m.Tipo == "Pronto");
    }
  }

  [Fact]
  public async Task Roteiro_editado_antes_recusa_com_400_PosicaoDoProntoInexistente_e_no_sem_origem_nao_aparece()
  {
    await using var c = await CriarAsync(_factory);
    var (e, perdaId) = await SobraDoFilhoDoCatalogoAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var (a, ee) = (await CopiaDeAsync(retrabalhoId, c.A), await CopiaDeAsync(retrabalhoId, e));

    // O PCP troca o Roteiro de E' para a Dobra e acrescenta a mao um filho sem origem a A'.
    var pcp = c.Como(c.Pcp);
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{ee}/roteiro", new { passos = new[] { c.Dobra } }));
    var novo = await pcp.PostAsJsonAsync($"/api/estrutura/{a}/filhos",
        new { componenteId = (int?)null, descricao = "Feito a mão", quantidade = 1m, quantidadePorPai = 1m });
    await Garantir(novo);
    var aMao = (await CorpoAsync(novo)).GetProperty("id").GetInt32();

    var previa = (await CorpoAsync(await PreverAsync(c, c.Pcp, retrabalhoId))).GetProperty("itens").EnumerateArray().ToList();
    Assert.DoesNotContain(previa, i => i.GetProperty("estruturaItemId").GetInt32() == aMao);
    var deE = Assert.Single(previa, i => i.GetProperty("estruturaItemId").GetInt32() == ee);
    Assert.Equal(("Chassi da Fase 3", 1m, 0m),
        (deE.GetProperty("descricao").GetString(), deE.GetProperty("sobra").GetDecimal(), deE.GetProperty("sugerido").GetDecimal()));

    var (status, erro, mensagem) = await RecusaAsync(await UsarAsync(c, c.Pcp, retrabalhoId, Item(ee, "AguardandoColeta", c.Corte, 1, 1m)));

    Assert.Equal((HttpStatusCode.BadRequest, "PosicaoDoProntoInexistente"), (status, erro));
    Assert.Contains("Chassi da Fase 3", mensagem);
    Assert.Single(await PerdasAsync(c.PedidoId));
  }

  [Fact]
  public async Task Estornar_o_reaproveitamento_desfaz_as_tres_coisas()
  {
    await using var c = await CriarAsync(_factory);
    var d = await FilhoDeCAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraDeCComFilhoAsync(c, d));
    var (cc, dd) = (await CopiaDeAsync(retrabalhoId, c.C), await CopiaDeAsync(retrabalhoId, d));
    var usada = await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoColeta", c.Dobra, 1, 1m));
    await Garantir(usada);
    var baixaId = (await CorpoAsync(usada)).GetProperty("id").GetInt32();

    var resposta = await c.Como(c.Pcp).PostAsync($"/api/baixas/{baixaId}/estorno", null);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    // A baixa do original, o `Pronto` de C' e a baixa ligada de D': tres estornos.
    Assert.Equal(3, (await CorpoAsync(resposta)).GetArrayLength());
    Assert.Equal(0m, await LiquidoAsync(c.C, "Perdido"));
    Assert.Equal(10m, await LiquidoAsync(c.C, "AguardandoColeta"));
    Assert.Equal(0m, await LiquidoAsync(cc, "AguardandoColeta"));
    Assert.Equal(0m, await LiquidoAsync(cc, "AIniciar"));
    Assert.Equal(0m, await LiquidoAsync(dd, "Perdido"));
    Assert.Equal(0m, await LiquidoAsync(dd, "AIniciar"));
    var ligada = Assert.Single(await PerdasAsync(retrabalhoId));
    Assert.Contains(await LivroAsync(dd), m => m.Tipo == "Estorno" && m.PerdaId == ligada.Id);
    var doOriginal = (await CorpoAsync(await c.Como(c.Gestao).GetAsync($"/api/pedidos/{c.PedidoId}/baixas"))).EnumerateArray()
        .Single(p => p.GetProperty("id").GetInt32() == baixaId);
    Assert.True(doOriginal.GetProperty("estornada").GetBoolean());
    Assert.Equal("Aberto", await StatusAsync(retrabalhoId));

    // Estornado, a sobra volta a estar a disposicao e a baixa nao se estorna de novo.
    var (status, erro, _) = await RecusaAsync(await c.Como(c.Pcp).PostAsync($"/api/baixas/{baixaId}/estorno", null));
    Assert.Equal((HttpStatusCode.Conflict, "JaEstornado"), (status, erro));
    Assert.Equal(HttpStatusCode.Created, (await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoColeta", c.Dobra, 1, 1m))).StatusCode);
  }

  [Fact]
  public async Task Estornar_com_pronto_que_andou_e_409_ProntoJaAndou()
  {
    await using var c = await CriarAsync(_factory);
    var (e, perdaId) = await SobraDoFilhoDoCatalogoAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var ee = await CopiaDeAsync(retrabalhoId, e);
    var usada = await UsarAsync(c, c.Pcp, retrabalhoId, Item(ee, "AguardandoColeta", c.Corte, 1, 1m));
    await Garantir(usada);
    var baixaId = (await CorpoAsync(usada)).GetProperty("id").GetInt32();
    // O Movimentador leva a E' pronta a montagem de A' na Solda.
    await EntregarAsync(c, DaColeta(ee, c.Corte, 1, 1m));

    var (status, erro, mensagem) = await RecusaAsync(await c.Como(c.Pcp).PostAsync($"/api/baixas/{baixaId}/estorno", null));

    Assert.Equal((HttpStatusCode.Conflict, "ProntoJaAndou"), (status, erro));
    Assert.Equal("Só há 0 de Chassi da Fase 3 onde o pronto pôs 1: a quantidade já andou no Retrabalho.", mensagem);
    Assert.DoesNotContain(await LivroAsync(e, ee), m => m.Tipo == "Estorno");
  }

  [Fact]
  public async Task Estornar_a_baixa_ligada_direto_e_409_EstornoImpossivel()
  {
    await using var c = await CriarAsync(_factory);
    var d = await FilhoDeCAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, await SobraDeCComFilhoAsync(c, d));
    var cc = await CopiaDeAsync(retrabalhoId, c.C);
    await Garantir(await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoColeta", c.Dobra, 1, 1m)));
    var ligada = Assert.Single(await PerdasAsync(retrabalhoId));

    var (status, erro, _) = await RecusaAsync(await c.Como(c.Pcp).PostAsync($"/api/baixas/{ligada.Id}/estorno", null));

    Assert.Equal((HttpStatusCode.Conflict, "EstornoImpossivel"), (status, erro));
    // Nas baixas do Retrabalho, a ligada nao se oferece para estorno.
    var naLista = (await CorpoAsync(await c.Como(c.Pcp).GetAsync($"/api/pedidos/{retrabalhoId}/baixas"))).EnumerateArray()
        .Single(p => p.GetProperty("id").GetInt32() == ligada.Id);
    Assert.False(naLista.GetProperty("estornavel").GetBoolean());
  }

  [Fact]
  public async Task Rota_generica_recusa_estornar_Pronto_com_EstornoImpossivel()
  {
    await using var c = await CriarAsync(_factory);
    var (e, perdaId) = await SobraDoFilhoDoCatalogoAsync(c);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, perdaId);
    var ee = await CopiaDeAsync(retrabalhoId, e);
    await Garantir(await UsarAsync(c, c.Pcp, retrabalhoId, Item(ee, "AguardandoColeta", c.Corte, 1, 1m)));
    var pronto = Assert.Single(await LivroAsync(ee), m => m.Tipo == "Pronto");

    var (status, erro, _) = await RecusaAsync(await c.Como(c.Pcp).PostAsync($"/api/movimentacoes/{pronto.Id}/estorno", null));

    Assert.Equal((HttpStatusCode.Conflict, "EstornoImpossivel"), (status, erro));
    Assert.DoesNotContain(await LivroAsync(ee), m => m.Tipo == "Estorno");
  }

  /// <summary>Um filho D de C (20, razao 2, "Arruela"; Corte), acrescentado antes de qualquer inicio.</summary>
  private static async Task<int> FilhoDeRazaoDoisAsync(CenarioDaFase3NaApi c)
  {
    var pcp = c.Como(c.Pcp);
    var criado = await pcp.PostAsJsonAsync($"/api/estrutura/{c.C}/filhos",
        new { componenteId = (int?)null, descricao = "Arruela", quantidade = 20m, quantidadePorPai = 2m });
    await Garantir(criado);
    var d = (await CorpoAsync(criado)).GetProperty("id").GetInt32();
    await Garantir(await pcp.PutAsJsonAsync($"/api/estrutura/{d}/roteiro", new { passos = new[] { c.Corte } }));
    return d;
  }

  /// <summary>
  /// Nos nos dados, o que esta em producao (a iniciar, aguardando coleta e aguardando montagem) mais o perdido fecha a
  /// quantidade do no (regra 9), e a producao nao fica negativa. Isso prova que nao sobrou saldo nas outras posicoes
  /// (no Setor, local de expedicao, conserto); nao prova que o livro conserva a soma, que e propriedade de cada linha.
  /// </summary>
  private async Task ConferirConservacaoAsync(int retrabalhoId, params int[] nos)
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    foreach (var no in nos)
    {
      var quantidade = (await db.Estruturas.AsNoTracking().SingleAsync(e => e.Id == no)).Quantidade;
      var emProducao = quantidade + await LiquidoAsync(no, "AIniciar") + await LiquidoAsync(no, "AguardandoColeta")
                       + await LiquidoAsync(no, "AguardandoMontagem");
      Assert.True(emProducao >= 0m, $"No {no} ficou com producao negativa.");
      Assert.Equal(quantidade, emProducao + await LiquidoAsync(no, "Perdido"));
    }
  }

  [Fact]
  public async Task Segundo_uso_da_sobra_leva_a_Perdido_o_pronto_do_filho_que_o_pai_pronto_nao_pede()
  {
    await using var c = await CriarAsync(_factory);
    var d = await FilhoDeRazaoDoisAsync(c);
    // D faz as 20 e vai a montagem de C na Dobra; C inicia e termina 9 (consome 18 de D). A perde 1 e depois mais 1: o
    // "a iniciar" de C sai com a primeira (D sobra 2, aguardando montagem), a segunda faz sobrar 1 de C, aguardando coleta.
    await IniciarETerminarAsync(c, d, c.Corte, 20m);
    await EntregarAsync(c, DaColeta(d, c.Corte, 1, 20m));
    await IniciarETerminarAsync(c, c.C, c.Dobra, 9m);
    var primeiraBaixa = await PerderAAsync(c, 1m);
    await PerderAAsync(c, 1m);
    var retrabalhoId = await AbrirRetrabalhoAsync(c, primeiraBaixa);
    var (cc, dd) = (await CopiaDeAsync(retrabalhoId, c.C), await CopiaDeAsync(retrabalhoId, d));
    // O livro de C diz 9 aguardando coleta; A precisa de 8, e a diferenca de 1 e a sobra.
    Assert.Equal(2m, await LiquidoAsync(d, "AguardandoMontagem"));
    Assert.Equal(9m, await LiquidoAsync(c.C, "AguardandoColeta"));

    // Primeiro uso: as 2 D prontas, aguardando montagem na Dobra. C' ainda nao esta pronta, nada a dispensar.
    var primeira = await UsarAsync(c, c.Pcp, retrabalhoId, Item(dd, "AguardandoMontagem", c.Dobra, null, 2m));
    await Garantir(primeira);
    var primeiroId = (await CorpoAsync(primeira)).GetProperty("id").GetInt32();
    Assert.Empty(await PerdasAsync(retrabalhoId));
    Assert.Equal(2m, await LiquidoAsync(dd, "AguardandoMontagem"));
    // As 2 D' sao toda a quantidade do no: o liquido de "a iniciar" e -2, e nada resta a iniciar.
    Assert.Equal(-2m, await LiquidoAsync(dd, "AIniciar"));

    // Segundo uso: C' pronta (1 e toda a quantidade dela) nao pede mais D'. As 2 D' prontas do primeiro uso nao tem de
    // onde sair de "a iniciar" e saem da posicao do pronto, numa baixa de `Descarte` ligada a este uso.
    var segunda = await UsarAsync(c, c.Pcp, retrabalhoId, Item(cc, "AguardandoColeta", c.Dobra, 1, 1m));
    await Garantir(segunda);
    var segundoId = (await CorpoAsync(segunda)).GetProperty("id").GetInt32();
    var ligada = Assert.Single(await PerdasAsync(retrabalhoId));
    Assert.Equal(("Descarte", false, (int?)segundoId), (ligada.Motivo, ligada.Tudo, ligada.PerdaDeOrigemId));
    var linhaDeD = Assert.Single(await LivroAsync(dd), m => m.Tipo == "Perda");
    Assert.Equal(("AguardandoMontagem", c.Dobra, "Perdido", 2m, (int?)ligada.Id),
        (linhaDeD.OrigemPosicao, linhaDeD.OrigemSetorId, linhaDeD.DestinoPosicao, linhaDeD.Quantidade, linhaDeD.PerdaId));
    Assert.Equal(0m, await LiquidoAsync(dd, "AguardandoMontagem"));
    Assert.Equal(2m, await LiquidoAsync(dd, "Perdido"));
    Assert.Equal(1m, await LiquidoAsync(cc, "AguardandoColeta"));
    await ConferirConservacaoAsync(retrabalhoId, cc, dd);

    // Estornar o primeiro uso com o segundo valendo: o pronto de D' ja nao esta todo nas posicoes em que os Pronto
    // do primeiro uso o puseram.
    var (status, erro, _) = await RecusaAsync(await c.Como(c.Pcp).PostAsync($"/api/baixas/{primeiroId}/estorno", null));
    Assert.Equal((HttpStatusCode.Conflict, "ProntoJaAndou"), (status, erro));
    Assert.Equal(2m, await LiquidoAsync(dd, "Perdido"));

    // Estornar o segundo devolve as D' a posicao do pronto; depois o primeiro se estorna.
    await Garantir(await c.Como(c.Pcp).PostAsync($"/api/baixas/{segundoId}/estorno", null));
    Assert.Equal(2m, await LiquidoAsync(dd, "AguardandoMontagem"));
    Assert.Equal(0m, await LiquidoAsync(dd, "Perdido"));
    Assert.Equal(0m, await LiquidoAsync(cc, "AguardandoColeta"));
    await ConferirConservacaoAsync(retrabalhoId, cc, dd);
    await Garantir(await c.Como(c.Pcp).PostAsync($"/api/baixas/{primeiroId}/estorno", null));
    Assert.Equal(0m, await LiquidoAsync(dd, "AguardandoMontagem"));
    Assert.Equal(0m, await LiquidoAsync(dd, "AIniciar"));
    await ConferirConservacaoAsync(retrabalhoId, cc, dd);
  }
}
