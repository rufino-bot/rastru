using System.Net;
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
/// Ponta a ponta dos endpoints de Estrutura (Task 5 da Fase 2), contra o SQL Server real
/// (docker compose up -d). Mesmo molde de <see cref="AgrupamentosEndpointsTests"/> e
/// <see cref="ReceitaPadraoEndpointsTests"/>: cada teste cria os próprios Pedido/Agrupamento/
/// Componente e apaga tudo que criou no <see cref="DisposeAsync"/>.
///
/// <para>
/// O QUE SÓ SE PROVA AQUI (o caso de uso já tem cobertura própria em
/// <c>Rastreamento.Application.Tests/Estrutura/</c>): o <c>TipoDeErro</c> virando o STATUS certo,
/// autorização por perfil de verdade com requisição HTTP, o prefixo <c>/api</c>, e o CORPO do
/// erro — em especial que <c>Detalhe</c> (a frase que nomeia o caminho do ciclo) sobrevive até a
/// resposta, e não só o código.
/// </para>
/// </summary>
public class EstruturaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _numerosCriados = [];
  private readonly List<int> _componentesCriados = [];

  public EstruturaEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
    var pedidoIds = pedidos.Select(p => p.Id).ToList();

    // EstruturaMaterial/EstruturaRoteiro antes de EstruturaItem, e EstruturaItem antes de
    // Agrupamento/Componente/Pedido -- mesma ordem de FK de AgrupamentosEndpointsTests.
    var agrupamentoIds = await db.Agrupamentos
        .Where(a => pedidoIds.Contains(a.PedidoId)).Select(a => a.Id).ToListAsync();

    foreach (var agId in agrupamentoIds)
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaMaterial WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaRoteiro WHERE EstruturaItemId IN (SELECT Id FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId})");
      await db.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId = {agId}");
    }

    // Receita de catalogo (FilhosPadrao) usada pelo teste de ciclo.
    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => _componentesCriados.Contains(f.ComponentePaiId)
                 || _componentesCriados.Contains(f.ComponenteFilhoId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => pedidoIds.Contains(a.PedidoId)).ToListAsync());
    await db.SaveChangesAsync();

    // Componente antes de ArquivoDeComponente: quem tem a FK e Componente
    // (FK_Componente_ArquivoSolido), mesma ordem de SolidoEndpointsTests.DisposeAsync. Os
    // ArquivoSolidoId sao capturados ANTES do RemoveRange -- as instancias em memoria continuam
    // legiveis depois, mas capturar antes deixa a intencao explicita.
    var componentes = await db.Componentes.Where(c => _componentesCriados.Contains(c.Id)).ToListAsync();
    var arquivoIds = componentes
        .Where(c => c.ArquivoSolidoId is not null)
        .Select(c => c.ArquivoSolidoId!.Value)
        .ToList();

    db.Componentes.RemoveRange(componentes);
    await db.SaveChangesAsync();

    db.ArquivosDeComponente.RemoveRange(
        await db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(pedidos);
    await db.SaveChangesAsync();
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  /// <summary>
  /// COM solido (regra 18, Task 5): ponto unico, entao toda Peca criada por um teste desta classe
  /// que nao arranja o proprio cenario tem de onde nascer sem a guarda nova recusar. Grava o
  /// `ArquivoDeComponente` primeiro (`CriadoPorUsuarioId = 1`, o `admin` do seed -- mesmo usuario
  /// que `TokenDeTeste.Emitir` usa por padrao para o `sub` da autoria de Pedido) e liga
  /// `ArquivoSolidoId` ao Componente. Quem quer o caso negativo (sem solido) usa
  /// <see cref="NovoComponenteSemSolido"/>.
  /// </summary>
  private async Task<int> NovoComponente(string prefixo = "ES")
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var arquivo = new ArquivoDeComponente
    {
      NomeOriginal = "cubo.stl", Conteudo = StlDeTesteDaApi.CuboBinario(), CriadoPorUsuarioId = 1,
    };
    db.ArquivosDeComponente.Add(arquivo);
    await db.SaveChangesAsync();

    var c = new Componente
    {
      Codigo = $"{prefixo}-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste da estrutura",
      Tipo = "Fabricado",
      Ativo = true,
      ArquivoSolidoId = arquivo.Id,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  /// <summary>
  /// SEM solido -- usado so por `Post_de_Peca_com_Componente_sem_solido_da_400_regra_18`, para
  /// provar a regra 18 no nivel HTTP e nao so no caso de uso.
  /// </summary>
  private async Task<int> NovoComponenteSemSolido(string prefixo = "ES")
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"{prefixo}-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste da estrutura, sem solido",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  private async Task<int> NovoPedido(HttpClient cliente)
  {
    var numero = $"ped-{Guid.NewGuid():N}"[..25];
    _numerosCriados.Add(numero);
    var resposta = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();
  }

  private async Task<int> NovoAgrupamento(HttpClient cliente, int pedidoId, string codigo = "AG-01")
  {
    var resposta = await cliente.PostAsJsonAsync(
        $"/api/pedidos/{pedidoId}/agrupamentos", new { codigo, tipo = "Kit" });
    return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();
  }

  /// <summary>Pedido + Agrupamento + Componente prontos, e o Id do Agrupamento -- base da maioria dos casos.</summary>
  private async Task<(int AgrupamentoId, int ComponenteId, int PedidoId)> NovoAgrupamentoComComponente(HttpClient cliente)
  {
    var pedidoId = await NovoPedido(cliente);
    var agrupamentoId = await NovoAgrupamento(cliente, pedidoId);
    var componenteId = await NovoComponente();
    return (agrupamentoId, componenteId, pedidoId);
  }

  private static object NovaPeca(int componenteId, decimal quantidade = 1m, bool requerRelatorio = false) =>
      new { componenteId, quantidade, requerRelatorioDimensional = requerRelatorio };

  // ---------------------------------------------------------------- criacao e leitura

  [Fact]
  public async Task POST_cria_a_Peca_e_devolve_201_com_a_arvore()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId, 3m, requerRelatorio: true));

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(componenteId, corpo.GetProperty("componenteId").GetInt32());
    Assert.Equal("Peca", corpo.GetProperty("nivelHierarquico").GetString());
    Assert.True(corpo.GetProperty("requerRelatorioDimensional").GetBoolean());
    Assert.Empty(corpo.GetProperty("filhos").EnumerateArray());
  }

  [Fact]
  public async Task Post_de_Peca_com_Componente_sem_solido_da_400_regra_18()
  {
    // Componente criado SEM passar pelo helper `NovoComponente` (que liga ArquivoSolidoId), para
    // provar a regra no nivel HTTP e nao so no caso de uso.
    var cliente = ClienteComo("PCP");
    var pedidoId = await NovoPedido(cliente);
    var agrupamentoId = await NovoAgrupamento(cliente, pedidoId);
    var componenteId = await NovoComponenteSemSolido();

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Contains("solido", corpo.GetProperty("erro").GetString(), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task GET_devolve_a_arvore_aninhada_do_Agrupamento()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = "Sub-item ad-hoc", quantidade = 2m, quantidadePorPai = 2m });

    var resposta = await cliente.GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    var raiz = Assert.Single(corpo.EnumerateArray());
    Assert.Equal(raizId, raiz.GetProperty("id").GetInt32());
    var filho = Assert.Single(raiz.GetProperty("filhos").EnumerateArray());
    Assert.Equal("Sub-item ad-hoc", filho.GetProperty("descricao").GetString());
  }

  [Fact]
  public async Task Filho_leva_quantidadePorPai_e_a_arvore_mostra_semRoteiro()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId, 10m));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var filho = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = "Calço", quantidade = 25m, quantidadePorPai = 2.5m });
    Assert.Equal(HttpStatusCode.Created, filho.StatusCode);

    var arvore = JsonDocument.Parse(await (await cliente.GetAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura")).Content.ReadAsStringAsync()).RootElement;
    var raiz = Assert.Single(arvore.EnumerateArray());
    Assert.Equal(JsonValueKind.Null, raiz.GetProperty("quantidadePorPai").ValueKind);
    Assert.True(raiz.GetProperty("semRoteiro").GetBoolean());   // Componente de teste sem roteiro padrao
    var item = Assert.Single(raiz.GetProperty("filhos").EnumerateArray());
    Assert.Equal(2.5m, item.GetProperty("quantidadePorPai").GetDecimal());
    Assert.True(item.GetProperty("semRoteiro").GetBoolean());
  }

  [Fact]
  public async Task Filho_sem_quantidadePorPai_devolve_400_com_a_regra_26()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos", new { componenteId = (int?)null, descricao = "Calço", quantidade = 1m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Contains("regra 26", corpo.GetProperty("erro").GetString());
  }

  /// <summary>
  /// Afirma os DOIS campos do corpo: `erro` (o código estável) e `mensagem` (a frase que nomeia o
  /// caminho do ciclo). Um teste que afirmasse só o código deixaria a frase cair sem ninguém notar
  /// — é exatamente como este defeito nasceu no plano (ver o brief e o CLAUDE.md da Task 5).
  /// </summary>
  [Fact]
  public async Task POST_com_ciclo_na_receita_devolve_409_com_codigo_E_mensagem()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, _, _) = await NovoAgrupamentoComComponente(cliente);
    var a = await NovoComponente();
    var b = await NovoComponente();
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao { ComponentePaiId = a, ComponenteFilhoId = b, QuantidadePadrao = 1m });
      db.FilhosPadrao.Add(new ComponenteFilhoPadrao { ComponentePaiId = b, ComponenteFilhoId = a, QuantidadePadrao = 1m });
      await db.SaveChangesAsync();
    }

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(a));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("CicloNaReceita", corpo.GetProperty("erro").GetString());
    Assert.Contains($"{a} -> {b} -> {a}", corpo.GetProperty("mensagem").GetString());
  }

  [Fact]
  public async Task DELETE_em_Pedido_nao_Aberto_devolve_409_PedidoNaoAberto()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, pedidoId) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
      pedido.Status = "EmProducao";
      await db.SaveChangesAsync();
    }

    var resposta = await cliente.DeleteAsync($"/api/estrutura/{raizId}");

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("PedidoNaoAberto", corpo.GetProperty("erro").GetString());
  }

  /// <summary>
  /// Par positivo de DELETE_em_Pedido_nao_Aberto_devolve_409_PedidoNaoAberto: com o Pedido `Aberto`
  /// (o caso comum), o DELETE tem de ATRAVESSAR o esquema de trava inteiro da Task 8
  /// (`_execucao.ExecutarAsync` -> `TravarNosAsync` da subarvore -> `RemoverSubarvoreAsync` DENTRO da
  /// transacao Serializable ja aberta) e chegar a `SaveChangesAsync`/commit reais contra o SQL
  /// Server. Achado da review da Task 8 (Important): antes deste teste, nenhum teste HTTP passava
  /// por esse caminho de sucesso -- so o de 409, que retorna ANTES de `RemoverSubarvoreAsync` correr.
  /// A prova de que ele bita esta na mutacao registrada no relatorio da Task 8 (fix pass): revertendo
  /// o branch `propria` de `EstruturaRepository.RemoverSubarvoreAsync` para um
  /// `BeginTransactionAsync` incondicional, este teste morre com 500 ("This SqlTransaction has
  /// completed; it is no longer usable" / conexao ja em transacao) -- porque a chamada abre uma
  /// SEGUNDA transacao sobre a mesma conexao, dentro da Serializable que `ExecutarAsync` ja tinha
  /// aberto.
  /// </summary>
  [Fact]
  public async Task DELETE_com_Pedido_Aberto_remove_a_subarvore_e_devolve_204()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();
    var filho = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = "Sub-item da subarvore", quantidade = 1m, quantidadePorPai = 1m });
    var filhoId = JsonDocument.Parse(await filho.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.DeleteAsync($"/api/estrutura/{raizId}");

    Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    // Escopado aos DOIS Ids deste teste (raiz e filho) -- nunca uma contagem global da tabela.
    Assert.False(await db.Estruturas.AnyAsync(i => i.Id == raizId || i.Id == filhoId));
  }

  /// <summary>
  /// Achado da review da Task 8 (Important, mesmo grupo de `DELETE_com_Pedido_Aberto_remove_a_subarvore_e_devolve_204`):
  /// nenhum teste HTTP fazia um PUT bem-sucedido -- so o caso de uso, em Rastreamento.Application.Tests,
  /// exercitava o caminho feliz de `EditarNo` dentro do esquema de trava.
  /// </summary>
  [Fact]
  public async Task PUT_edita_descricao_e_quantidade_e_devolve_200()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId, 5m));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PutAsJsonAsync(
        $"/api/estrutura/{raizId}", new { descricao = "Peca editada pelo PUT", quantidade = 9m });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(raizId, corpo.GetProperty("id").GetInt32());
    Assert.Equal("Peca editada pelo PUT", corpo.GetProperty("descricao").GetString());
    Assert.Equal(9m, corpo.GetProperty("quantidade").GetDecimal());
  }

  [Fact]
  public async Task POST_de_filho_ad_hoc_sem_descricao_devolve_400()
  {
    var cliente = ClienteComo("PCP");
    var (agrupamentoId, componenteId, _) = await NovoAgrupamentoComComponente(cliente);
    var criada = await cliente.PostAsJsonAsync(
        $"/api/agrupamentos/{agrupamentoId}/estrutura", NovaPeca(componenteId));
    var raizId = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/estrutura/{raizId}/filhos",
        new { componenteId = (int?)null, descricao = (string?)null, quantidade = 1m, quantidadePorPai = 1m });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- perfil

  /// <summary>
  /// Autentica como Operador. Id inexistente de propósito -- `[Authorize(Roles)]` roda antes do
  /// model binding e da action, então o 403 chega sem tocar no banco.
  /// </summary>
  [Fact]
  public async Task Perfil_sem_escrita_recebe_403_no_POST()
  {
    var resposta = await ClienteComo("Operador")
        .PostAsJsonAsync("/api/agrupamentos/999999/estrutura", NovaPeca(999999));

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  /// <summary>
  /// O par positivo de Perfil_sem_escrita_recebe_403_no_POST -- sem ele, um `[Authorize(Roles)]`
  /// posto na CLASSE inteira (em vez de só na ação de escrita) passaria no teste do 403 e
  /// quebraria a leitura para todo mundo sem ninguém notar. Precisa de Agrupamento real: 200 exige
  /// o caso de uso rodar até o fim.
  /// </summary>
  [Fact]
  public async Task Perfil_sem_escrita_recebe_200_no_GET()
  {
    var (agrupamentoId, _, _) = await NovoAgrupamentoComComponente(ClienteComo("PCP"));

    var resposta = await ClienteComo("Operador").GetAsync($"/api/agrupamentos/{agrupamentoId}/estrutura");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }
}
