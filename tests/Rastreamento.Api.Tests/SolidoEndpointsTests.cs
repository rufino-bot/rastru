using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta dos dois endpoints do solido 3D do Componente (Task 4 da Fase 2B), contra o SQL
/// Server real (docker compose up -d). Mesmo molde de <see cref="EstruturaEndpointsTests"/>: cada
/// teste cria o proprio Componente e apaga tudo que criou no <see cref="DisposeAsync"/>.
///
/// <para>
/// O QUE SO SE PROVA AQUI (o caso de uso ja tem cobertura propria em
/// <c>SolidoDoComponenteUseCaseTests</c>): o caminho HTTP inteiro -- multipart binding do
/// `IFormFile`, `TipoDeErro` virando o STATUS certo, autorizacao por perfil de verdade, o
/// `Content-Disposition` da resposta binaria e a leitura de volta do `TemSolido` via
/// `GET /componentes/{id}`.
/// </para>
/// </summary>
public class SolidoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<int> _componentesCriados = [];

  public SolidoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  /// <summary>
  /// Componente primeiro, arquivo depois -- mesma ordem de <c>ArquivoDeComponenteRepositoryTests
  /// .LimparAsync</c>, e pelo MESMO motivo: sem navegacao entre as duas entidades, o EF nao
  /// enxerga `FK_Componente_ArquivoSolido` no modelo e nao ordena os deletes sozinho. Apagar quem
  /// TEM a FK (Componente) antes de quem e apontado (ArquivoDeComponente) evita violar a
  /// constraint.
  /// </summary>
  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    var componentes = await db.Componentes
        .Where(c => _componentesCriados.Contains(c.Id)).ToListAsync();
    var arquivoIds = componentes
        .Where(c => c.ArquivoSolidoId is not null)
        .Select(c => c.ArquivoSolidoId!.Value)
        .ToList();

    db.Componentes.RemoveRange(componentes);
    await db.SaveChangesAsync();

    db.ArquivosDeComponente.RemoveRange(
        await db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private async Task<int> NovoComponente(string prefixo = "SLD")
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"{prefixo}-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste do solido",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  private static async Task<HttpResponseMessage> EnviarSolido(
      HttpClient cliente, int componenteId, byte[] conteudo, string nomeArquivo = "cubo.stl")
  {
    // `await` DENTRO do `using`, nao so retornar a Task: o `MultipartFormDataContent` precisa
    // continuar vivo enquanto o corpo e lido durante o envio -- devolver a Task direto descartaria
    // o `corpo` (fim do bloco `using`, sincrono) antes de o `PostAsync` terminar de le-lo, e a
    // requisicao falharia com `ObjectDisposedException` (medido ao escrever este teste).
    using var corpo = new MultipartFormDataContent();
    var arquivo = new ByteArrayContent(conteudo);
    arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    corpo.Add(arquivo, "arquivo", nomeArquivo);
    return await cliente.PostAsync($"/api/componentes/{componenteId}/solido", corpo);
  }

  // ---------------------------------------------------------------- POST

  [Fact]
  public async Task Post_de_STL_valido_grava_e_o_componente_passa_a_ter_solido()
  {
    var cliente = ClienteComo("Administrador");
    var componenteId = await NovoComponente();

    var resposta = await EnviarSolido(cliente, componenteId, StlDeTesteDaApi.CuboBinario());

    Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
    var detalhe = JsonDocument.Parse(
        await cliente.GetStringAsync($"/api/componentes/{componenteId}")).RootElement;
    Assert.True(detalhe.GetProperty("temSolido").GetBoolean());
  }

  [Fact]
  public async Task Post_de_arquivo_que_nao_e_STL_da_400()
  {
    var cliente = ClienteComo("Administrador");
    var componenteId = await NovoComponente();

    var resposta = await EnviarSolido(
        cliente, componenteId, StlDeTesteDaApi.Invalido(), "lixo.stl");

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  [Fact]
  public async Task Post_de_componente_inexistente_da_404()
  {
    var resposta = await EnviarSolido(
        ClienteComo("Administrador"), 999999, StlDeTesteDaApi.CuboBinario());

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Perfil que NAO e Administrador nem PCP -- molde de
  /// <c>ComponentesEndpointsTests.Operador_nao_escreve_em_componente</c>. Id inexistente de
  /// proposito: `[Authorize(Roles)]` roda antes do model binding, entao o 403 chega sem tocar no
  /// banco nem exigir um Componente real.
  /// </summary>
  [Fact]
  public async Task Post_sem_perfil_de_escrita_da_403()
  {
    var resposta = await EnviarSolido(
        ClienteComo("Operador"), 999999, StlDeTesteDaApi.CuboBinario());

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  [Fact]
  public async Task Post_sem_autenticacao_nenhuma_da_401()
  {
    var resposta = await EnviarSolido(
        _factory.CreateClient(), 999999, StlDeTesteDaApi.CuboBinario());

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- GET

  [Fact]
  public async Task Get_devolve_o_binario_com_o_nome_original_no_Content_Disposition()
  {
    var cliente = ClienteComo("Administrador");
    var componenteId = await NovoComponente();
    var conteudo = StlDeTesteDaApi.CuboBinario();
    await EnviarSolido(cliente, componenteId, conteudo, "cubo.stl");

    var resposta = await cliente.GetAsync($"/api/componentes/{componenteId}/solido");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    // Byte a byte, nao so o tamanho: um corpo trocado por outro do mesmo comprimento passaria
    // numa afirmacao so de Length.
    Assert.Equal(conteudo, await resposta.Content.ReadAsByteArrayAsync());
    Assert.Contains("cubo.stl", resposta.Content.Headers.ContentDisposition?.ToString() ?? string.Empty);
  }

  [Fact]
  public async Task Get_de_componente_sem_solido_da_404()
  {
    var componenteId = await NovoComponente();

    var resposta = await ClienteComo("Administrador")
        .GetAsync($"/api/componentes/{componenteId}/solido");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Leitura e de todos: o gate e o `[Authorize]` de CLASSE, como em `Obter` -- este endpoint NAO
  /// tem `[Authorize(Roles)]`. Precisa de sólido real: 200 exige o caso de uso `Obter` rodar ate o
  /// fim, e o par negativo (perfil sem escrita recebendo 403) e o que provaria um `Roles` colado
  /// aqui por engano -- mas GET nunca escreve, entao esse par nao existe neste endpoint por
  /// desenho, so o positivo importa.
  /// </summary>
  [Fact]
  public async Task Get_e_permitido_a_qualquer_autenticado()
  {
    var componenteId = await NovoComponente();
    await EnviarSolido(ClienteComo("Administrador"), componenteId, StlDeTesteDaApi.CuboBinario());

    var resposta = await ClienteComo("Operador").GetAsync($"/api/componentes/{componenteId}/solido");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }
}
