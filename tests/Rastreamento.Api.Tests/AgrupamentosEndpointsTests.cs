using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos endpoints de Agrupamento, contra o SQL Server real (docker compose up -d).
/// A limpeza apaga Agrupamento ANTES de Pedido — FK_Agrupamento_Pedido nao aceita a ordem inversa.
/// </summary>
public class AgrupamentosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<string> _numerosCriados = [];

    public AgrupamentosEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

        var pedidos = await db.Pedidos.Where(p => _numerosCriados.Contains(p.Numero)).ToListAsync();
        var ids = pedidos.Select(p => p.Id).ToList();

        // EstruturaItem (Fase 2, sem entidade) sai por SQL: um teste insere uma linha de proposito.
        foreach (var id in ids)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.EstruturaItem WHERE AgrupamentoId IN (SELECT Id FROM dbo.Agrupamento WHERE PedidoId = {id})");

        db.Agrupamentos.RemoveRange(await db.Agrupamentos.Where(a => ids.Contains(a.PedidoId)).ToListAsync());
        await db.SaveChangesAsync();

        db.Pedidos.RemoveRange(pedidos);
        await db.SaveChangesAsync();
    }

    private int IdDeUsuarioReal()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
        return db.Usuarios.Single(u => u.NomeUsuario == "admin").Id;
    }

    private HttpClient ClienteComo(string perfil)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenDeTeste.Emitir(_factory, perfil, IdDeUsuarioReal()));
        return cliente;
    }

    /// <summary>Abre um Pedido pela propria API e devolve o Id — a base de todos os casos daqui.</summary>
    private async Task<int> NovoPedido(HttpClient cliente)
    {
        var numero = $"ped-{Guid.NewGuid():N}"[..25];
        _numerosCriados.Add(numero);
        var resposta = await cliente.PostAsJsonAsync("/pedidos", new { numero, cliente = "Cliente X" });
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();
    }

    private static object Kit(string codigo = "AG-01") =>
        new { codigo, quantidade = 10, tipo = "Kit" };

    /// <summary>Cria um Agrupamento pela API e devolve o Id — usado pelos casos de PUT e DELETE.</summary>
    private static async Task<int> NovoAgrupamento(HttpClient cliente, int pedidoId, string codigo = "AG-01")
    {
        var criado = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit(codigo));
        return JsonDocument.Parse(await criado.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Pcp_cria_agrupamento_no_pedido_com_autoria_e_timestamp()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(pedidoId, corpo.GetProperty("pedidoId").GetInt32());
        Assert.Equal(IdDeUsuarioReal(), corpo.GetProperty("criadoPorUsuarioId").GetInt32());
        Assert.False(string.IsNullOrEmpty(corpo.GetProperty("criadoEm").GetString()));
    }

    [Theory]
    [InlineData("POST", "/pedidos/999999/agrupamentos")]
    [InlineData("PUT", "/agrupamentos/999999")]
    [InlineData("DELETE", "/agrupamentos/999999")]
    public async Task Operador_nao_escreve_em_agrupamento(string metodo, string rota)
    {
        // O [Authorize(Roles = "PCP,Administrador")] roda antes do model binding e da action, entao
        // uma rota com id inexistente (999999) ainda responde 403 aqui, nunca 404 — e por isso o
        // Theory nao precisa criar Pedido nem Agrupamento nenhum para provar autorizacao.
        // Os TRES verbos de escrita entram: com so o POST coberto, apagar o atributo do PUT ou do
        // DELETE deixaria a suite verde (adendo B5), e no caso do DELETE a mutacao APAGARIA dado.
        var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);
        if (metodo != "DELETE") requisicao.Content = JsonContent.Create(Kit("operador-nao-pode"));

        var resposta = await ClienteComo("Operador").SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Operador_le_a_lista_de_agrupamentos()
    {
        var pedidoId = await NovoPedido(ClienteComo("PCP"));

        var leitura = await ClienteComo("Operador").GetAsync($"/pedidos/{pedidoId}/agrupamentos");

        Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    }

    [Fact]
    public async Task Sem_token_nao_le_a_lista()
    {
        var resposta = await _factory.CreateClient().GetAsync("/pedidos/999999/agrupamentos");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Codigo_repetido_no_mesmo_pedido_responde_409()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    }

    [Fact]
    public async Task Mesmo_codigo_em_outro_pedido_e_aceito()
    {
        var cliente = ClienteComo("PCP");
        var primeiro = await NovoPedido(cliente);
        var segundo = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{primeiro}/agrupamentos", Kit());

        var resposta = await cliente.PostAsJsonAsync($"/pedidos/{segundo}/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
    }

    [Fact]
    public async Task Tipo_invalido_responde_400()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync(
            $"/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", quantidade = 10, tipo = "Conjunto" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quantidade_invalida_responde_400_e_nao_cria_linha(decimal quantidade)
    {
        // `Quantidade` e DECIMAL(18,4) sem CHECK no DDL (diferente de `Tipo`, que tem
        // CK_Agrupamento_Tipo como rede) — a guarda `if (quantidade <= 0)` do use case e a UNICA
        // defesa. Removendo-a, o pior caso nao e 500: e dado invalido persistido em silencio, daí
        // o teste tambem confirmar que nada foi criado (adendo B14), nao so o status.
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync(
            $"/pedidos/{pedidoId}/agrupamentos", new { codigo = "AG-01", quantidade, tipo = "Kit" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var lista = await cliente.GetStringAsync($"/pedidos/{pedidoId}/agrupamentos");
        Assert.DoesNotContain("AG-01", lista);
    }

    [Fact]
    public async Task Codigo_maior_que_a_coluna_responde_400_e_nao_500()
    {
        // 51 chars, um alem do NVARCHAR(50) de dbo.Agrupamento.Codigo. Prova que o [MaxLength(50)]
        // de NovoAgrupamentoDto pega ANTES de o insert estourar SqlException: removendo o atributo,
        // este teste morre com 500. Provar que o ALVO do atributo esta certo (sem [property:], que
        // faria todo POST virar 500) e propriedade diferente, coberta pelos POSTs felizes daqui.
        //
        // O [MaxLength(20)] de `Tipo` NAO ganha caso aqui, e a omissao e deliberada: qualquer valor
        // com 21 chars tambem esta fora de TiposValidos, entao o use case ja responde 400 pelo
        // outro motivo. Medido por mutacao: removendo os DOIS atributos, so o caso de `codigo`
        // morre. Um InlineData de `tipo` passaria sempre — seria prova falsa do atributo, que e
        // exatamente o que o adendo B8 existe para evitar. A cobertura real de `Tipo` longo demais
        // e Tipo_invalido_responde_400; o atributo fica como defesa em profundidade, sem teste que
        // o falsifique neste nivel.
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);

        var resposta = await cliente.PostAsJsonAsync(
            $"/pedidos/{pedidoId}/agrupamentos",
            new { codigo = new string('x', 51), quantidade = 10, tipo = "Kit" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Criar_agrupamento_em_pedido_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP")
            .PostAsJsonAsync("/pedidos/999999/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Editar_agrupamento_troca_quantidade_e_tipo_sem_mexer_no_pedido_nem_na_autoria()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var id = await NovoAgrupamento(cliente, pedidoId);
        var original = JsonDocument.Parse(await cliente.GetStringAsync($"/agrupamentos/{id}")).RootElement;
        var criadoEm = original.GetProperty("criadoEm").GetString();

        var resposta = await cliente.PutAsJsonAsync(
            $"/agrupamentos/{id}", new { codigo = "AG-01", quantidade = 25, tipo = "Avulso" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(25m, corpo.GetProperty("quantidade").GetDecimal());
        Assert.Equal("Avulso", corpo.GetProperty("tipo").GetString());
        Assert.Equal(pedidoId, corpo.GetProperty("pedidoId").GetInt32());
        Assert.Equal(IdDeUsuarioReal(), corpo.GetProperty("criadoPorUsuarioId").GetInt32());
        Assert.Equal(criadoEm, corpo.GetProperty("criadoEm").GetString());

        // Releitura: sem o SaveChanges o corpo acima ainda viria certo (e a projecao em memoria),
        // mas o GET seguinte devolveria o valor antigo.
        var relido = JsonDocument.Parse(await cliente.GetStringAsync($"/agrupamentos/{id}")).RootElement;
        Assert.Equal(25m, relido.GetProperty("quantidade").GetDecimal());
        Assert.Equal("Avulso", relido.GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Editar_agrupamento_inexistente_responde_404()
    {
        // Sem este caso nada exercita o `resultado.Sucesso ? Ok(...) : TraduzirFalha(...)` do PUT:
        // trocar o corpo da action por `return Ok(resultado.Valor);` passaria batido (adendo B7).
        var resposta = await ClienteComo("PCP").PutAsJsonAsync(
            "/agrupamentos/999999", new { codigo = "AG-01", quantidade = 10, tipo = "Kit" });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Editar_para_codigo_repetido_no_mesmo_pedido_responde_409()
    {
        // Unico teste que exercita `DuplicadoNoPedidoDe`: no PUT o PedidoId nao vem da rota (o
        // Agrupamento e identificado so por `id`), entao o delegate precisa buscar o Agrupamento
        // atual para descobrir em qual Pedido procurar o duplicado. Substituir o corpo dele por
        // `ct => Task.FromResult<ValorDuplicadoDto?>(null)` deixava a suite inteira verde antes
        // deste caso (adendo B12) — nenhum outro teste chega no ramo de erro do PUT com um
        // conflito de verdade. O corpo do 409 aqui e o formato `ValorDuplicado`, diferente do 409
        // pelado do DELETE (AgrupamentoNaoVazio/PedidoNaoAberto).
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        await NovoAgrupamento(cliente, pedidoId, "AG-01");
        var segundo = await NovoAgrupamento(cliente, pedidoId, "AG-02");

        var resposta = await cliente.PutAsJsonAsync(
            $"/agrupamentos/{segundo}", new { codigo = "AG-01", quantidade = 10, tipo = "Kit" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ValorDuplicado", corpo.GetProperty("erro").GetString());
        Assert.Equal("codigo", corpo.GetProperty("campo").GetString());
    }

    [Fact]
    public async Task Obter_agrupamento_inexistente_responde_404()
    {
        var resposta = await ClienteComo("PCP").GetAsync("/agrupamentos/999999");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Excluir_agrupamento_vazio_responde_204_e_repetir_responde_404()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var id = await NovoAgrupamento(cliente, pedidoId);

        var primeira = await cliente.DeleteAsync($"/agrupamentos/{id}");
        var segunda = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.NoContent, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, segunda.StatusCode);
    }

    [Fact]
    public async Task Excluir_agrupamento_com_estrutura_responde_409_AgrupamentoNaoVazio()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var id = await NovoAgrupamento(cliente, pedidoId);

        // EstruturaItem e da Fase 2 e nao tem entidade: a linha entra por SQL, que e exatamente o
        // que TemEstruturaAsync consulta. ComponenteId nulo = item ad-hoc, permitido pelo DDL.
        using (var escopo = _factory.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.EstruturaItem (AgrupamentoId, NivelHierarquico, Quantidade) VALUES ({id}, 'Peca', 1)");
        }

        var resposta = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("AgrupamentoNaoVazio", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Excluir_agrupamento_de_pedido_nao_aberto_responde_409_PedidoNaoAberto()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        var id = await NovoAgrupamento(cliente, pedidoId);

        // Nenhum endpoint da Fase 1 transiciona status (isso e Fase 3): o teste muda a linha
        // direto, que e o unico jeito de exercitar a guarda hoje.
        using (var escopo = _factory.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == pedidoId);
            pedido.Status = "EmProducao";
            await db.SaveChangesAsync();
        }

        var resposta = await cliente.DeleteAsync($"/agrupamentos/{id}");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("PedidoNaoAberto", corpo.GetProperty("erro").GetString());
    }

    [Fact]
    public async Task Listar_agrupamentos_do_pedido_devolve_os_criados()
    {
        var cliente = ClienteComo("PCP");
        var pedidoId = await NovoPedido(cliente);
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit("AG-01"));
        await cliente.PostAsJsonAsync($"/pedidos/{pedidoId}/agrupamentos", Kit("AG-02"));

        var lista = await cliente.GetStringAsync($"/pedidos/{pedidoId}/agrupamentos");

        Assert.Contains("AG-01", lista);
        Assert.Contains("AG-02", lista);
    }

    [Fact]
    public async Task Cadastrar_com_token_sem_a_claim_sub_responde_401()
    {
        // Molde de PedidosEndpointsTests.Cadastrar_com_token_sem_a_claim_sub_responde_401. A
        // guarda `if (usuarioId is null) return Unauthorized();` (AgrupamentosController.cs:37)
        // roda ANTES de qualquer acesso ao Pedido da rota — troca-la por `usuarioId ?? 0` nao
        // gera 401, gera 500 (violacao de FK_Agrupamento_CriadoPorUsuario com id 0), que e a
        // classe de defeito do adendo B13. Por rodar antes do use case, nem precisa de um Pedido
        // real: 999999 nunca e tocado. Role Administrador (o default de TokenSemAClaim) para o
        // filtro [Authorize(Roles = "PCP,Administrador")] deixar a requisicao chegar na action —
        // senao o teste provaria 403, nao 401.
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDeTeste.TokenSemAClaim(_factory, "sub"));

        var resposta = await cliente.PostAsJsonAsync("/pedidos/999999/agrupamentos", Kit());

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
