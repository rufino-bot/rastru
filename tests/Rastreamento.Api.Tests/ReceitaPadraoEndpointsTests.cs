using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Ponta a ponta da receita padrao, contra o SQL Server real (docker compose up -d).
/// Cada teste cria os proprios Componentes e Setores e apaga tudo que criou no DisposeAsync.
///
/// <para>
/// O QUE SO SE PROVA AQUI. Os casos de uso ja tem ~200 testes de regra; repetir validacao neste
/// nivel seria custo sem sinal. O que a fronteira HTTP acrescenta e:
/// <list type="bullet">
/// <item>o `TipoDeErro` virando o STATUS certo — inclusive os dois que quase se perderam na
/// escrita do plano: 409 de conflito de gravacao e 400 de pai inativo;</item>
/// <item>AUTORIZACAO POR PERFIL de verdade, com requisicao: `PerfisDeEscritaDeclaradosTests` le a
/// tabela de rotas e declara, no proprio summary, que nao faz requisicao nenhuma — quem prova o
/// 403 sao os testes de endpoint por recurso, e este arquivo e o par da receita padrao;</item>
/// <item>o prefixo `/api` (ver `CLAUDE.md`): sem ele a guarda de `Program.cs` devolve 404;</item>
/// <item>o CORPO do erro, que e como o front discrimina.</item>
/// </list>
/// </para>
/// </summary>
public class ReceitaPadraoEndpointsTests
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
  private readonly WebApplicationFactory<Program> _factory;
  private readonly List<int> _componentesCriados = [];
  private readonly List<int> _setoresCriados = [];
  private readonly List<int> _materiaisCriados = [];

  public ReceitaPadraoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();

    // As linhas de receita ANTES dos Componentes e Setores: as FKs apontam para eles.
    db.FilhosPadrao.RemoveRange(await db.FilhosPadrao
        .Where(f => _componentesCriados.Contains(f.ComponentePaiId)
                 || _componentesCriados.Contains(f.ComponenteFilhoId)).ToListAsync());
    db.MateriaisPadrao.RemoveRange(await db.MateriaisPadrao
        .Where(m => _componentesCriados.Contains(m.ComponenteId)
                 || _materiaisCriados.Contains(m.MaterialId)).ToListAsync());
    db.RoteirosPadrao.RemoveRange(await db.RoteirosPadrao
        .Where(r => _componentesCriados.Contains(r.ComponenteId)
                 || _setoresCriados.Contains(r.SetorId)).ToListAsync());
    await db.SaveChangesAsync();

    db.Componentes.RemoveRange(await db.Componentes
        .Where(c => _componentesCriados.Contains(c.Id)).ToListAsync());
    db.Setores.RemoveRange(await db.Setores
        .Where(s => _setoresCriados.Contains(s.Id)).ToListAsync());
    db.Materiais.RemoveRange(await db.Materiais
        .Where(m => _materiaisCriados.Contains(m.Id)).ToListAsync());
    await db.SaveChangesAsync();
  }

  /// <summary>Os tres sub-recursos, para as asserções que valem igual nos tres.</summary>
  private const string Filhos = "filhos-padrao";
  private const string Materiais = "materiais-padrao";
  private const string Roteiro = "roteiro-padrao";

  private HttpClient ClienteComo(string perfil)
  {
    var cliente = _factory.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, perfil));
    return cliente;
  }

  private async Task<int> NovoComponente()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var c = new Componente
    {
      Codigo = $"RP-{Guid.NewGuid():N}"[..12],
      Descricao = "Componente de teste da receita",
      Tipo = "Fabricado",
      Ativo = true,
    };
    db.Componentes.Add(c);
    await db.SaveChangesAsync();
    _componentesCriados.Add(c.Id);
    return c.Id;
  }

  /// <summary>
  /// Cria os PROPRIOS setores em vez de pescar os que estiverem no banco. Ler `db.Setores.Take(2)`
  /// amarraria o teste a massa ambiente — e o `db/seed.sql` (o unico seed obrigatorio) NAO tem
  /// setor nenhum: os que existem hoje sao resquicio de teste manual, e a massa de demo da Task 7
  /// e explicitamente proibida de sustentar teste automatizado.
  /// </summary>
  private async Task<(int, int)> DoisSetores()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    // Nome unico por UQ_Setor_Nome — sobra de execucao anterior nao perdoa.
    var a = new Setor { Nome = $"rp-{Guid.NewGuid():N}"[..12], Ativo = true };
    var b = new Setor { Nome = $"rp-{Guid.NewGuid():N}"[..12], Ativo = true };
    db.Setores.AddRange(a, b);
    await db.SaveChangesAsync();
    _setoresCriados.Add(a.Id);
    _setoresCriados.Add(b.Id);
    return (a.Id, b.Id);
  }

  /// <summary>Material proprio, pelo mesmo motivo de <see cref="DoisSetores"/>: nada de massa ambiente.</summary>
  private async Task<int> NovoMaterial()
  {
    using var escopo = _factory.Services.CreateScope();
    var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
    var m = new Material
    {
      Codigo = $"RP-{Guid.NewGuid():N}"[..12], // unico por UQ_Material_Codigo
      Descricao = "Material de teste da receita",
      UnidadeMedida = "UN",
      Ativo = true,
    };
    db.Materiais.Add(m);
    await db.SaveChangesAsync();
    _materiaisCriados.Add(m.Id);
    return m.Id;
  }

  /// <summary>Corpo minimo e VALIDO de cada sub-recurso: lista vazia — o comando de apagar.</summary>
  private static object CorpoVazio() => new { linhas = Array.Empty<object>() };

  // ---------------------------------------------------------------- autenticacao e perfil

  [Fact]
  public async Task Sem_token_a_leitura_e_401()
  {
    var id = await NovoComponente();

    var resposta = await _factory.CreateClient()
        .GetAsync($"/api/componentes/{id}/{Filhos}");

    Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
  }

  /// <summary>
  /// Escrita e so de Administrador e PCP. Esta e a fronteira REAL de seguranca — o
  /// `usePodeEscrever` do front esconde botao, e esconder botao nao e seguranca.
  ///
  /// <para>
  /// TRES rotas x DOIS perfis, e nao um caso so, por causa das duas degeneracoes que este projeto
  /// ja pagou (achado B11 da Fase 1A, onde um literal coincidia com o unico usuario do banco e o
  /// teste passava):
  /// <list type="bullet">
  /// <item>uma rota so deixaria `[Authorize(Roles)]` faltando nas outras duas passar por aqui —
  /// e o teste de endpoint e justamente o que a guarda de tabela de rotas NAO faz;</item>
  /// <item>um perfil so nao distingue "Operador e barrado" de "so o Operador e barrado": com
  /// `Roles = "Administrador,PCP,Qualidade"` o caso do Operador continua verde.</item>
  /// </list>
  /// </para>
  ///
  /// Id inexistente de proposito: o `[Authorize(Roles)]` roda ANTES do model binding e da acao,
  /// entao o 403 chega sem tocar no banco — nada e criado, nada precisa ser limpo.
  /// </summary>
  [Theory]
  [InlineData("Operador", Filhos)]
  [InlineData("Operador", Materiais)]
  [InlineData("Operador", Roteiro)]
  [InlineData("Qualidade", Filhos)]
  [InlineData("Qualidade", Materiais)]
  [InlineData("Qualidade", Roteiro)]
  public async Task Perfil_sem_escrita_recebe_403_no_post(string perfil, string subRecurso)
  {
    var resposta = await ClienteComo(perfil)
        .PostAsJsonAsync($"/api/componentes/999999/{subRecurso}", CorpoVazio());

    Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
  }

  /// <summary>
  /// O par positivo do teste acima, nos DOIS perfis da tabela aprovada: sem ele, encolher
  /// `PerfisDeEscrita` para so um dos dois deixaria a suite de endpoint verde (quem pegaria seria
  /// so a guarda de tabela, que nao faz requisicao). 200, e nao 201: o POST nao cria recurso novo
  /// enderecavel, substitui o conteudo de um sub-recurso que ja tem endereco.
  /// </summary>
  [Theory]
  [InlineData("Administrador", Filhos)]
  [InlineData("Administrador", Materiais)]
  [InlineData("Administrador", Roteiro)]
  [InlineData("PCP", Filhos)]
  [InlineData("PCP", Materiais)]
  [InlineData("PCP", Roteiro)]
  public async Task Perfil_de_escrita_grava_nos_tres_sub_recursos(string perfil, string subRecurso)
  {
    var id = await NovoComponente();

    var resposta = await ClienteComo(perfil)
        .PostAsJsonAsync($"/api/componentes/{id}/{subRecurso}", CorpoVazio());

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  /// <summary>
  /// Leitura e de QUALQUER autenticado — Operador inclusive, que e o perfil mais restrito. Os tres
  /// GET, porque o risco concreto e um `[Authorize(Roles)]` copiado "por simetria" para UM deles:
  /// com um caso so, os outros dois passariam a exigir perfil sem ninguem notar.
  /// </summary>
  [Theory]
  [InlineData(Filhos)]
  [InlineData(Materiais)]
  [InlineData(Roteiro)]
  public async Task Perfil_de_leitura_le_os_tres_sub_recursos(string subRecurso)
  {
    var id = await NovoComponente();

    var resposta = await ClienteComo("Operador").GetAsync($"/api/componentes/{id}/{subRecurso}");

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
  }

  // ---------------------------------------------------------------- ida e volta

  [Fact]
  public async Task PCP_grava_filhos_e_a_leitura_devolve_o_que_foi_gravado()
  {
    var pai = await NovoComponente();
    var filho = await NovoComponente();

    var post = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/{Filhos}",
        new { linhas = new[] { new { componenteFilhoId = filho, quantidadePadrao = 2m } } });
    Assert.Equal(HttpStatusCode.OK, post.StatusCode);

    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<FilhoLido>>($"/api/componentes/{pai}/{Filhos}");

    Assert.Equal(filho, Assert.Single(lidas!).ComponenteFilhoId);
  }

  /// <summary>
  /// O par de materiais do teste acima, e ele existe por uma medicao: sem ele, ligar o
  /// `GET .../materiais-padrao` ao `ListarRoteiro` do caso de uso — acao apontada para o metodo
  /// errado — passava a SUITE INTEIRA (199/199, medido). Os outros dois sub-recursos ja tinham
  /// ida-e-volta; materiais era o unico GET cujo unico teste olhava so o status.
  ///
  /// Ligacao errada e bug que SO este nivel pode pegar: os casos de uso nao sabem que existe
  /// controller, e a guarda de perfis le a tabela de rotas, nao o corpo da resposta. Do lado do
  /// POST o compilador ja protege (os tres `Receita*Dto` sao tipos distintos); do lado do GET,
  /// nao — `Traduzir&lt;T&gt;` e generico e aceita qualquer um dos tres.
  /// </summary>
  [Fact]
  public async Task PCP_grava_materiais_e_a_leitura_devolve_o_que_foi_gravado()
  {
    var id = await NovoComponente();
    var material = await NovoMaterial();

    var post = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{id}/{Materiais}",
        new { linhas = new[] { new { materialId = material, quantidadePadrao = 3.5m } } });
    Assert.Equal(HttpStatusCode.OK, post.StatusCode);

    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<MaterialLido>>($"/api/componentes/{id}/{Materiais}");

    var linha = Assert.Single(lidas!);
    Assert.Equal(material, linha.MaterialId);
    Assert.Equal(3.5m, linha.QuantidadePadrao);
  }

  /// <summary>
  /// A ordem do roteiro sai da POSICAO no array, atribuida pelo SERVIDOR — o cliente nao envia
  /// `Ordem` nenhuma (`LinhaDeRoteiroPadraoDto` so tem `SetorId`). Enviar fora de ordem alfabetica
  /// /numerica de id e o que separa "o servidor numerou" de "o banco devolveu na ordem que deu".
  /// </summary>
  [Fact]
  public async Task PCP_grava_roteiro_e_a_ordem_vem_do_servidor()
  {
    var id = await NovoComponente();
    var (setorA, setorB) = await DoisSetores();

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{id}/{Roteiro}",
        new { linhas = new[] { new { setorId = setorB }, new { setorId = setorA } } });

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<RoteiroLido>>($"/api/componentes/{id}/{Roteiro}");
    // setorB veio primeiro no array, entao setorB e a Ordem 1 — mesmo tendo o id MAIOR.
    Assert.Equal([(setorB, 1), (setorA, 2)], lidas!.Select(l => (l.SetorId, l.Ordem)));
  }

  [Fact]
  public async Task Lista_vazia_apaga_a_receita_e_responde_200()
  {
    var pai = await NovoComponente();
    var filho = await NovoComponente();
    await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/{Filhos}",
        new { linhas = new[] { new { componenteFilhoId = filho, quantidadePadrao = 1m } } });

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{pai}/{Filhos}", CorpoVazio());

    Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    var lidas = await ClienteComo("PCP")
        .GetFromJsonAsync<List<FilhoLido>>($"/api/componentes/{pai}/{Filhos}");
    Assert.Empty(lidas!);
  }

  // ---------------------------------------------------------------- traducao de status

  [Fact]
  public async Task Componente_inexistente_e_404()
  {
    var resposta = await ClienteComo("PCP").GetAsync($"/api/componentes/99999999/{Filhos}");

    Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
  }

  /// <summary>
  /// Corpo SEM o campo `linhas` e 400, NAO "apagar a receita". Lista vazia e comando explicito de
  /// apagar; campo ausente e requisicao malformada. Sem o `[Required]` no DTO, `POST {}` limparia
  /// a receita em silencio — mesma classe de bug que o `DefinirAtivoDto` ja pagou.
  ///
  /// Os TRES sub-recursos porque sao TRES DTOs distintos, cada um com o seu `[Required]`: um caso
  /// so provaria um atributo so. Id inexistente de proposito — a validacao de modelo do
  /// [ApiController] roda antes da acao, entao o 400 chega sem consultar o banco.
  /// </summary>
  [Theory]
  [InlineData(Filhos)]
  [InlineData(Materiais)]
  [InlineData(Roteiro)]
  public async Task Corpo_sem_o_campo_linhas_e_400(string subRecurso)
  {
    var resposta = await ClienteComo("PCP")
        .PostAsJsonAsync($"/api/componentes/999999/{subRecurso}", new { });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  /// <summary>
  /// Ciclo sai como 400, e nao 409: e validacao de ENTRADA — o corpo enviado esta errado e
  /// reenvia-lo igual falha de novo. O 409 destes endpoints significa o oposto ("outra gravacao
  /// derrubou a sua, tente de novo"), e e o que o teste logo abaixo prende. Os dois convivem no
  /// mesmo POST, entao trocar um pelo outro tem de quebrar algo — quebra estes dois.
  /// </summary>
  [Fact]
  public async Task Ciclo_responde_400_ponta_a_ponta()
  {
    var a = await NovoComponente();
    var b = await NovoComponente();

    await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{b}/{Filhos}",
        new { linhas = new[] { new { componenteFilhoId = a, quantidadePadrao = 1m } } });

    var resposta = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{a}/{Filhos}",
        new { linhas = new[] { new { componenteFilhoId = b, quantidadePadrao = 1m } } });

    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
  }

  /// <summary>
  /// O caminho de CONFLITO no nivel HTTP: `TipoDeErro.Conflito` tem de sair como **409**, e nao
  /// como 400. Sem este teste a traducao nasce morta — nenhum dos outros toca esse ramo, e foi
  /// exatamente por isso que o `Traduzir` de uma versao anterior do plano colapsava `Conflito` em
  /// `BadRequest` sem ninguem notar (achado C1 da review da Task 5).
  ///
  /// O conflito e FORCADO por substituicao do repositorio no DI, e nao por corrida real: provocar
  /// o deadlock do SERIALIZABLE por HTTP exigiria segurar um range lock de fora, e a requisicao
  /// ficaria pendurada no lock (o `SET LOCK_TIMEOUT 0` do teste de Infrastructure e da conexao do
  /// PERDEDOR, e aqui a conexao e da API). O que se prova aqui e a TRADUCAO; que o repositorio real
  /// levante `ConflitoDeConcorrenciaException` ja esta provado contra o SQL Server em
  /// `Perdedor_de_gravacao_simultanea_sobe_como_conflito_de_concorrencia`.
  ///
  /// O CORPO tambem e afirmado: e por ele que o front discrimina conflito (`web/src/api/cadastros.ts`
  /// testa `erro === 'ValorDuplicado'`), entao um 409 sem `erro` cairia no ramo de "formato de
  /// conflito inesperado" da Task 9.
  /// </summary>
  [Fact]
  public async Task Conflito_de_gravacao_responde_409()
  {
    var id = await NovoComponente();
    using var fabrica = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        s.AddScoped<IReceitaPadraoRepository, RepositorioQueSoDaConflito>()));
    var cliente = fabrica.CreateClient();
    cliente.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", TokenDeTeste.Emitir(_factory, "PCP"));

    var resposta = await cliente.PostAsJsonAsync(
        $"/api/componentes/{id}/{Filhos}", CorpoVazio());

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("erro").GetString()));
  }

  /// <summary>
  /// Componente pai INATIVO: a escrita e **400** e a leitura continua **200** (§2.3 da spec,
  /// decidido em 2026-08-20). Os dois lados no mesmo teste porque e um par — uma guarda copiada
  /// para o `GET` "por simetria" quebraria o `200`, e uma guarda faltando quebraria o `400`.
  ///
  /// **400, e nao 409**: e a irma da guarda de item inativo, que ja e validacao, e o 409 destes
  /// endpoints significa "outra gravacao derrubou a sua, tente de novo". Se o usuario revisitar a
  /// escolha (a spec registra o precedente contrario, `PedidoNaoAberto` -> `Conflito`), o que muda
  /// aqui e uma linha.
  ///
  /// O corpo do 400 nomeia o codigo do componente — e o que a tela mostra para o usuario entender
  /// por que a gravacao foi recusada.
  /// </summary>
  [Fact]
  public async Task Componente_pai_inativo_recusa_a_escrita_e_permite_a_leitura()
  {
    var id = await NovoComponente();
    string codigo;
    using (var escopo = _factory.Services.CreateScope())
    {
      var db = escopo.ServiceProvider.GetRequiredService<RastreamentoDbContext>();
      var componente = await db.Componentes.SingleAsync(c => c.Id == id);
      componente.Ativo = false;
      codigo = componente.Codigo;
      await db.SaveChangesAsync();
    }

    var escrita = await ClienteComo("PCP").PostAsJsonAsync(
        $"/api/componentes/{id}/{Filhos}", CorpoVazio());
    var leitura = await ClienteComo("Operador").GetAsync($"/api/componentes/{id}/{Filhos}");

    Assert.Equal(HttpStatusCode.BadRequest, escrita.StatusCode);
    Assert.Equal(HttpStatusCode.OK, leitura.StatusCode);
    var corpo = JsonDocument.Parse(await escrita.Content.ReadAsStringAsync()).RootElement;
    Assert.Contains(codigo, corpo.GetProperty("erro").GetString());
  }

  private sealed record FilhoLido(int Id, int ComponenteFilhoId, decimal QuantidadePadrao);
  private sealed record RoteiroLido(int Id, int SetorId, string Nome, int Ordem);
  private sealed record MaterialLido(int Id, int MaterialId, decimal QuantidadePadrao);

  /// <summary>
  /// Repositorio que atende as leituras com o minimo necessario para a requisicao CHEGAR na
  /// gravacao, e entao levanta <see cref="ConflitoDeConcorrenciaException"/> nas tres escritas.
  /// Registrado por `ConfigureTestServices`, que roda DEPOIS do `Program` e por isso substitui o
  /// registro real.
  /// </summary>
  private sealed class RepositorioQueSoDaConflito : IReceitaPadraoRepository
  {
    // ATIVO: um componente inativo pararia a requisicao no 400 de pai inativo, antes da gravacao.
    public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
        Task.FromResult<Componente?>(new Componente
        {
          Id = id,
          Codigo = "CONFLITO",
          Descricao = "Componente do teste de conflito",
          Tipo = "Fabricado",
          Ativo = true,
        });

    public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(
        int componenteId, CancellationToken ct) => Vazio<ComponenteFilhoPadrao>();

    public Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(
        int componenteId, CancellationToken ct) => Vazio<ComponenteMaterialPadrao>();

    public Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(
        int componenteId, CancellationToken ct) => Vazio<ComponenteRoteiroPadrao>();

    public Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct) => Vazio<Componente>();

    public Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct) => Vazio<Material>();

    public Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct) => Vazio<Setor>();

    public Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(
        CancellationToken ct) => Vazio<ComponenteFilhoPadrao>();

    public Task SubstituirFilhosAsync(
        int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct) =>
        throw Conflito();

    public Task SubstituirMateriaisAsync(
        int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct) =>
        throw Conflito();

    public Task SubstituirRoteiroAsync(
        int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct) =>
        throw Conflito();

    private static Task<IReadOnlyList<T>> Vazio<T>() =>
        Task.FromResult<IReadOnlyList<T>>([]);

    private static ConflitoDeConcorrenciaException Conflito() =>
        new(new InvalidOperationException("forcado pelo teste"));
  }
}
