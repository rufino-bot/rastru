using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Pedido, contra o SQL Server real (docker compose up -d).
/// </summary>
public class PedidosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<string> _numerosCriados = [];

  public PedidosEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
    var ids = pedidos.Select(p => p.Id).ToList();

    // Defensivo: esta classe nunca cria Agrupamento hoje, entao a linha abaixo remove sempre
    // zero registros. Mas apagar Pedido primeiro travaria em FK_Agrupamento_Pedido no dia em
    // que um teste passar a criar Agrupamento aqui — a ordem certa e a mesma que
    // AgrupamentosEndpointsTests.DisposeAsync ja usa.
    db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => ids.Contains(a.PedidoId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Pedidos.RemoveRange(pedidos);
    await db.SaveChangesAsync();
  }

  private string NumeroUnico()
  {
    var numero = $"ped-{Guid.NewGuid():N}"[..25];
    _numerosCriados.Add(numero);
    return numero;
  }

  /// <summary>
  /// Id de um Usuario que EXISTE (o `pcp` do db/seed.sql, nao o `admin`). O token precisa apontar
  /// para uma linha real porque FK_Pedido_CriadoPorUsuario nao aceita autor inventado — nos
  /// testes de catalogo isso nao importava, aqui importa. O perfil vem do parametro; o Id, do
  /// banco. Deliberadamente NAO e o `admin`: o Id dele e 1, e um `usuarioId.Value` trocado por um
  /// literal `1` no controller coincidiria e a prova de autoria (adendo B11) ficaria degenerada.
  /// </summary>
  private int IdDeUsuarioReal()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    return db.Usuarios.Single(u => u.NomeUsuario == "pcp").Id;
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Bearer", TokenDeTeste.Emitir(_factory, perfil, IdDeUsuarioReal()));
    return cliente;
  }

  [Fact]
  public async Task Pcp_cadastra_pedido_aberto_de_fabricacao_com_autor()
  {
    var resposta = await ClienteComo("PCP")
        .PostAsJsonAsync("/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("Aberto", corpo.GetProperty("status").GetString());
    Assert.Equal("Fabricacao", corpo.GetProperty("tipo").GetString());
    Assert.Equal(IdDeUsuarioReal(), corpo.GetProperty("criadoPorUsuarioId").GetInt32());
  }

  [Fact]
  public async Task Administrador_tambem_cadastra_pedido()
  {
    var resposta = await ClienteComo("Administrador")
        .PostAsJsonAsync("/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

    Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
  }

  [Theory]
  [InlineData("POST", "/api/pedidos")]
  [InlineData("PUT", "/api/pedidos/999999")]
  public async Task Qualidade_nao_escreve_em_pedido(string metodo, string rota)
  {
    // So dois verbos: Pedido nao tem PATCH (sem coluna Ativo) nem DELETE (documento, ver a
    // spec, "Politica de exclusao"). O [Authorize(Roles = "PCP,Administrador")] roda ANTES
    // do model binding e da action, entao um id inexistente (999999) ainda responde 403 aqui,
    // nunca 404 — e por isso o Theory nao precisa cadastrar Pedido nenhum. Numero literal fixo
    // (nao NumeroUnico()): como nada e criado num 403, registrar o numero na lista de limpeza
    // do DisposeAsync seria inofensivo mas deselegante. Qualidade e o perfil de negacao aqui
    // (nao PCP nem Administrador, os dois autorizados).
    var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota)
    {
      Content = JsonContent.Create(new { numero = "qualidade-nao-pode", cliente = "Cliente X" })
    };

    var resposta = await ClienteComo("Qualidade").SendAsync(requisicao);

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Qualidade_nao_cadastra_pedido_mas_le_a_lista()
  {
    var cliente = ClienteComo("Qualidade");

    var escrita = await cliente.PostAsJsonAsync(
        "/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });
    var leitura = await cliente.GetAsync("/api/pedidos");

    Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
    Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
  }

  /// <summary>Pedido gravado direto no banco (o cadastro pela API so cria `Aberto`), com numero limpo pelo `DisposeAsync`.</summary>
  private async Task<int> GravarPedidoAsync(
      string cliente, string status, string? numero = null, DateTime? dataAbertura = null, DateOnly? dataEntrega = null)
  {
    if (numero is not null) _numerosCriados.Add(numero);
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var pedido = new Pedido
    {
      Numero = numero ?? NumeroUnico(), Cliente = cliente, Tipo = "Fabricacao", Status = status,
      DataAbertura = dataAbertura ?? DateTime.UtcNow, DataEntrega = dataEntrega ?? new DateOnly(2026, 10, 22),
      CriadoPorUsuarioId = IdDeUsuarioReal(),
    };
    db.Pedidos.Add(pedido);
    await db.SaveChangesAsync();
    return pedido.Id;
  }

  [Fact]
  public async Task Lista_paginada_responde_o_envelope_com_o_total_do_filtro()
  {
    var cliente = $"cli-{Guid.NewGuid():N}";
    for (var i = 0; i < 3; i++) await GravarPedidoAsync(cliente, "Aberto");

    var resposta = await ClienteComo("PCP").GetAsync($"/api/pedidos?busca={cliente}&tamanho=2");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(2, corpo.GetProperty("itens").GetArrayLength());
    Assert.Equal(3, corpo.GetProperty("total").GetInt32());
    Assert.Equal(1, corpo.GetProperty("pagina").GetInt32());
    Assert.Equal(2, corpo.GetProperty("tamanho").GetInt32());
  }

  /// <summary>Ids, na ordem devolvida, de `GET /pedidos?busca={cliente}{consulta}`.</summary>
  private async Task<int[]> IdsListadosAsync(string cliente, string consulta)
  {
    var corpo = JsonDocument.Parse(
        await ClienteComo("PCP").GetStringAsync($"/api/pedidos?busca={cliente}{consulta}")).RootElement;
    return corpo.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray();
  }

  [Fact]
  public async Task Listagem_sem_ordem_mantem_a_ordem_por_data_de_abertura()
  {
    // Numeros em ordem CONTRARIA a das datas: so a ordem por DataAbertura decrescente acerta.
    var cliente = $"cli-{Guid.NewGuid():N}";
    var tok = Guid.NewGuid().ToString("N")[..16];
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var antigo = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-a", t1);
    var recente = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-c", t1.AddHours(2));
    var meio = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-b", t1.AddHours(1));

    Assert.Equal([recente, meio, antigo], await IdsListadosAsync(cliente, ""));
    Assert.Equal([recente, meio, antigo], await IdsListadosAsync(cliente, "&ordem=recentes"));
  }

  [Fact]
  public async Task Ordem_numero_ordena_por_numero()
  {
    var cliente = $"cli-{Guid.NewGuid():N}";
    var tok = Guid.NewGuid().ToString("N")[..16];
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var c = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-c", t1.AddHours(3));
    var a = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-a", t1.AddHours(1));
    var b = await GravarPedidoAsync(cliente, "Aberto", $"{tok}-b", t1.AddHours(2));

    Assert.Equal([a, b, c], await IdsListadosAsync(cliente, "&ordem=numero"));
  }

  [Fact]
  public async Task Ordem_cliente_ordena_por_cliente()
  {
    var tok = Guid.NewGuid().ToString("N");
    var t1 = new DateTime(2001, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    var beta = await GravarPedidoAsync($"{tok} Beta", "Aberto", dataAbertura: t1.AddHours(2));
    var alfa = await GravarPedidoAsync($"{tok} Alfa", "Aberto", dataAbertura: t1);

    Assert.Equal([alfa, beta], await IdsListadosAsync(tok, "&ordem=cliente"));
  }

  [Fact]
  public async Task Ordem_desconhecida_responde_400_nomeando_o_valor()
  {
    var resposta = await ClienteComo("PCP").GetAsync("/api/pedidos?ordem=Numero");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(
        "Ordem 'Numero' desconhecida. Aceitas: recentes, numero, cliente.",
        corpo.GetProperty("erro").GetString());
  }

  [Theory]
  [InlineData("?pagina=0")]
  [InlineData("?tamanho=101")]
  [InlineData("?status=Qualquer")]
  [InlineData("?material=abc")]
  public async Task Faixa_ou_filtro_invalido_responde_400_com_erro(string consulta)
  {
    var resposta = await ClienteComo("PCP").GetAsync($"/api/pedidos{consulta}");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("erro").GetString()));
  }

  [Fact]
  public async Task Resumo_conta_todos_os_Pedidos_alem_do_tamanho_de_pagina()
  {
    // Mais Pedidos do que cabem numa pagina (20): um resumo calculado sobre uma pagina daria <= 20.
    // Monotono (>=) porque so as linhas deste teste sao garantidas no banco compartilhado.
    var cliente = $"cli-{Guid.NewGuid():N}";
    for (var i = 0; i < 25; i++) await GravarPedidoAsync(cliente, "Cancelado");

    var resposta = await ClienteComo("PCP").GetAsync("/api/pedidos/resumo");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    var porStatus = corpo.GetProperty("porStatus").EnumerateArray()
        .ToDictionary(c => c.GetProperty("status").GetString()!, c => c.GetProperty("quantidade").GetInt32());
    Assert.True(porStatus["Cancelado"] >= 25);
    Assert.Equal(
        ["Aberto", "EmProducao", "AguardandoExpedicao", "Concluido", "Cancelado"],
        corpo.GetProperty("porStatus").EnumerateArray().Select(c => c.GetProperty("status").GetString()));
    Assert.True(corpo.GetProperty("maisAntigosAbertos").GetArrayLength() <= 5);
  }

  [Theory]
  [InlineData("/api/pedidos/resumo")]
  [InlineData("/api/pedidos/materiais")]
  public async Task Resumo_e_materiais_sao_leitura_de_qualquer_perfil(string rota)
  {
    var resposta = await ClienteComo("Qualidade").GetAsync(rota);

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  [Theory]
  [InlineData("/api/pedidos/resumo")]
  [InlineData("/api/pedidos/materiais")]
  public async Task Sem_token_nao_le_resumo_nem_materiais(string rota)
  {
    var resposta = await _factory.CreateClient().GetAsync(rota);

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Numero_duplicado_responde_409_nao_reativavel()
  {
    var cliente = ClienteComo("PCP");
    var numero = NumeroUnico();
    await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });

    var resposta = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente Y" });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
    Assert.Equal("numero", corpo.GetProperty("campo").GetString());
    Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
  }

  [Fact]
  public async Task Numero_duplicado_com_espaco_a_esquerda_responde_409_completo()
  {
    // Achado I2 da review da 1B: LocalizarDuplicado normaliza o valor antes de consultar
    // (CadastroDePedidoUseCase.cs:108), mas nada testava esse Normalizar. Cria o Pedido e tenta
    // criar outro com " numero" (espaco a esquerda, como sai de copy-paste de planilha/PDF). O
    // 409 tem de vir COMPLETO: sem o Normalizar no localizador, `campo`/`idExistente` somem e o
    // front cai no erro generico. Pedido nao tem `Ativo` — `existeInativo` e sempre false, sem
    // botao de reativacao para sumir.
    var cliente = ClienteComo("PCP");
    var numero = NumeroUnico();
    var criado = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.PostAsJsonAsync(
        "/api/pedidos", new { numero = $" {numero}", cliente = "Cliente Y" });

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("numero", corpo.GetProperty("campo").GetString());
    Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
    Assert.Equal(id, corpo.GetProperty("idExistente").GetInt32());
  }

  [Fact]
  public async Task Obter_pedido_devolve_o_que_foi_cadastrado()
  {
    var cliente = ClienteComo("PCP");
    var numero = NumeroUnico();
    var criado = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
        .RootElement.GetProperty("id").GetInt32();

    var resposta = await cliente.GetAsync($"/api/pedidos/{id}");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal(numero, corpo.GetProperty("numero").GetString());
    Assert.Equal("Cliente X", corpo.GetProperty("cliente").GetString());
  }

  [Fact]
  public async Task Obter_pedido_inexistente_responde_404()
  {
    var resposta = await ClienteComo("PCP").GetAsync("/api/pedidos/999999");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Editar_altera_o_cliente_sem_trocar_o_autor()
  {
    var cliente = ClienteComo("PCP");
    var numero = NumeroUnico();
    var criado = await cliente.PostAsJsonAsync("/api/pedidos", new { numero, cliente = "Cliente X" });
    var corpoCriado = JsonDocument.Parse(await criado.Content.ReadAsStringAsync()).RootElement;
    var id = corpoCriado.GetProperty("id").GetInt32();
    var autor = corpoCriado.GetProperty("criadoPorUsuarioId").GetInt32();

    var resposta = await cliente.PutAsJsonAsync(
        $"/api/pedidos/{id}", new { numero, cliente = "Cliente Z" });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.Equal("Cliente Z", corpo.GetProperty("cliente").GetString());
    Assert.Equal(autor, corpo.GetProperty("criadoPorUsuarioId").GetInt32());
  }

  [Fact]
  public async Task Editar_pedido_inexistente_responde_404()
  {
    var resposta = await ClienteComo("PCP")
        .PutAsJsonAsync("/api/pedidos/999999", new { numero = NumeroUnico(), cliente = "Cliente X" });

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  [Fact]
  public async Task Cliente_em_branco_responde_400()
  {
    var resposta = await ClienteComo("PCP")
        .PostAsJsonAsync("/api/pedidos", new { numero = NumeroUnico(), cliente = "  " });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Theory]
  [InlineData("numero", 31)]
  [InlineData("cliente", 201)]
  public async Task Campo_maior_que_a_coluna_responde_400_e_nao_500(string campo, int tamanho)
  {
    // Um caractere alem de NVARCHAR(30)/(200) de dbo.Pedido, respectivamente. Prova que o
    // [MaxLength] de cada parametro de NovoPedidoDto pega ANTES de o insert estourar
    // SqlException — mesmo papel de Campo_maior_que_a_coluna_responde_400_e_nao_500 em
    // MateriaisEndpointsTests.
    var valores = new Dictionary<string, object>
    {
      ["numero"] = NumeroUnico(),
      ["cliente"] = "Cliente X",
    };
    valores[campo] = new string('x', tamanho);

    var resposta = await ClienteComo("PCP").PostAsJsonAsync("/api/pedidos", valores);

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Sem_token_nao_le_a_lista()
  {
    var resposta = await _factory.CreateClient().GetAsync("/api/pedidos");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  [Fact]
  public async Task Cadastrar_com_token_sem_a_claim_sub_responde_401()
  {
    // O caminho descoberto: token valido e assinado por nos, mas sem a claim `sub`. E o unico
    // lugar da fase onde um valor lido do HttpContext vai para o banco com FK
    // (FK_Pedido_CriadoPorUsuario) — apagar a guarda do controller gera
    // InvalidOperationException (usuarioId.Value sem valor) -> 500; "consertar" com `?? 0`
    // estoura a FK -> 500 tambem. Role Administrador (o default de TokenSemAClaim) para o
    // filtro de autorizacao do [Authorize(Roles = "PCP,Administrador")] deixar a requisicao
    // chegar na action — senao o teste provaria 403, nao 401.
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.TokenSemAClaim(_factory, "sub"));

    var resposta = await cliente.PostAsJsonAsync(
        "/api/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }
}
