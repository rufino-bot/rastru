using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

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
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/pedidos")]
    [InlineData("PUT", "/pedidos/999999")]
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
            "/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });
        var leitura = await cliente.GetAsync("/pedidos");

        Assert.Equal(HttpStatusCode.Forbidden, escrita.StatusCode);
        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task Numero_duplicado_responde_409_nao_reativavel()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });

        var resposta = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente Y" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("numero", corpo.GetProperty("campo").GetString());
        Assert.False(corpo.GetProperty("existeInativo").GetBoolean());
    }

    [Fact]
    public async Task Obter_pedido_devolve_o_que_foi_cadastrado()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        var criado = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        var id = JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var resposta = await cliente.GetAsync($"/pedidos/{id}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(numero, corpo.GetProperty("numero").GetString());
        Assert.Equal("Cliente X", corpo.GetProperty("cliente").GetString());
    }

    [Fact]
    public async Task Obter_pedido_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP").GetAsync("/pedidos/999999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Editar_altera_o_cliente_sem_trocar_o_autor()
    {
        var cliente = ClienteComo("PCP");
        var numero = NumeroUnico();
        var criado = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        var corpoCriado = JsonDocument.Parse(await criado.Content.ReadAsStringAsync()).RootElement;
        var id = corpoCriado.GetProperty("id").GetInt32();
        var autor = corpoCriado.GetProperty("criadoPorUsuarioId").GetInt32();

        var resposta = await cliente.PutAsJsonAsync(
            $"/pedidos/{id}", new { numero, cliente = "Cliente Z" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Cliente Z", corpo.GetProperty("cliente").GetString());
        Assert.Equal(autor, corpo.GetProperty("criadoPorUsuarioId").GetInt32());
    }

    [Fact]
    public async Task Editar_pedido_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP")
            .PutAsJsonAsync("/pedidos/999999", new { numero = NumeroUnico(), cliente = "Cliente X" });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Cliente_em_branco_responde_400()
    {
        var resposta = await ClienteComo("PCP")
            .PostAsJsonAsync("/pedidos", new { numero = NumeroUnico(), cliente = "  " });

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
            ["numero"] = NumeroUnico(), ["cliente"] = "Cliente X",
        };
        valores[campo] = new string('x', tamanho);

        var resposta = await ClienteComo("PCP").PostAsJsonAsync("/pedidos", valores);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/pedidos");

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
            "/pedidos", new { numero = NumeroUnico(), cliente = "Cliente X" });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
