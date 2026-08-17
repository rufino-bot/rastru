using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Achado I3 da review de branch da 1B: as duas guardas que pegam essa classe de regressao vivem
/// em `web/` (`permissoesEspelhamOBackend.test.ts` e `permissoes.test.ts`), e uma fase so-de-backend
/// como a 1C pode rodar `dotnet test` sem nunca rodar `npm test`. Esta classe e o par de backend
/// da tabela `web/src/auth/permissoes.ts`.
///
/// <para>
/// A FONTE DE VERDADE desta guarda e a TABELA DE ROTEAMENTO REAL — o `EndpointDataSource` resolvido
/// do `IServiceProvider` de um `WebApplicationFactory&lt;Program&gt;` — e nao uma reconstrucao por
/// reflexao do que o ASP.NET provavelmente vai rotear. A versao anterior fazia a reconstrucao
/// (varria um assembly, com `DeclaredOnly`, chaveando por `Type.Name`, reconhecendo verbo so por
/// `HttpMethodAttribute`) e a re-review de `fase1c-re-review-a1-a2.md` mediu SETE formas de
/// endpoint de escrita que nasciam com a guarda verde: acao herdada de base abstrata (a pior — um
/// unico `[HttpDelete]` em `CadastroControllerBase` criava 5 DELETE sem gate, com a suite inteira
/// verde), acao sem atributo de verbo (que casa TODOS os verbos), `[AcceptVerbs]`, controller em
/// outro assembly referenciado, minimal API, nome de tipo colidido em outro namespace (que alem de
/// escapar MASCARAVA a regressao do controller real de mesmo nome) e atributo de verbo custom.
/// Todas as sete somem quando a pergunta deixa de ser "o que a reflexao acha" e passa a ser "o que
/// o roteamento roteia".
/// </para>
///
/// <para>
/// As duas propriedades asseguradas:
/// <list type="number">
/// <item>VALORES — os perfis exigidos por cada endpoint de escrita batem com a
/// <see cref="TabelaAprovada"/> (`Perfis_exigidos_batem_com_a_tabela_aprovada`).</item>
/// <item>DESCOBERTA — todo endpoint roteado que aceita verbo de escrita esta na tabela ou na lista
/// de isentos (`Todo_endpoint_de_escrita_esta_na_tabela_ou_na_lista_de_isentos`), e todo endpoint
/// que exige `Roles`, mesmo de leitura, esta na tabela
/// (`Todo_endpoint_que_exige_perfil_esta_na_tabela`).</item>
/// </list>
/// </para>
///
/// <para>
/// O QUE ESTA GUARDA NAO FAZ — declarado aqui de proposito, porque a versao anterior deste arquivo
/// afirmava no `summary` que "nenhuma acao de escrita escapa" com sete contra-exemplos medidos:
/// <list type="bullet">
/// <item>Ela le a DECLARACAO de `Roles`, nao a semantica de autorizacao. Varios
/// `[Authorize(Roles)]` na mesma cadeia sao combinados pelo ASP.NET com E logico (intersecao dos
/// perfis); esta guarda faz a UNIAO deles. A divergencia entre as duas leituras nao passa
/// despercebida — ela faz a asserção de valores falhar, porque a uniao deixa de bater com a tabela
/// — mas o numero que a guarda mostra na mensagem e a uniao, nao o acesso efetivo.</item>
/// <item>Ela nao sabe QUAIS perfis um `[Authorize(Policy = "...")]` admite. Um endpoint de escrita
/// cujo gate esteja numa policy ainda precisa de entrada na tabela (a asserção de descoberta o
/// nomeia), mas a asserção de valores so consegue conferir `Roles`.</item>
/// <item>Ela nao faz requisicao HTTP nenhuma: le a tabela de rotas, nao o comportamento. Um bug no
/// middleware de autorizacao, ou um `Roles = "Administrator"` que nao casa com o valor da claim
/// emitida pelo token, e invisivel aqui. Quem cobre isso sao os testes de 403 por recurso
/// (`SetoresEndpointsTests.Operador_nao_escreve_em_setor` e os pares dele).</item>
/// <item>Gate escrito em MIDDLEWARE, antes do roteamento, esta fora do alcance: nao aparece nos
/// metadados de endpoint nenhum.</item>
/// <item>Ela ve o que o `WebApplicationFactory` compoe (ambiente de teste). Um controller
/// registrado sob condicao de ambiente — `if (env.IsProduction())` — nao estaria nesta tabela.
/// Hoje nao existe registro condicional em `Program.cs`.</item>
/// </list>
/// Do lado positivo, e por construcao e nao por sorte: minimal API (`app.MapPost`) e controller de
/// assembly referenciado ENTRAM na tabela de roteamento, entao ENTRAM nesta guarda — nao ha
/// fronteira de "so controller do assembly da API" para declarar.
/// </para>
///
/// <para>
/// CUSTO, medido: a versao por reflexao rodava em ~22 ms e nao subia host nenhum. Esta sobe um
/// `WebApplicationFactory&lt;Program&gt;` uma vez para as tres asserções (por isso o
/// <see cref="Roteamento"/> e um `Lazy` estatico) e custa ~200 ms. Ela NAO precisa do SQL Server no
/// ar: ler a tabela de rotas nao abre conexao — medido com o container derrubado.
/// </para>
/// </summary>
public class PerfisDeEscritaDeclaradosTests
{
  /// <summary>
  /// Tabela aprovada: IDENTIDADE DE ENDPOINT -> conjunto de perfis autorizados. Mudou um
  /// `[Authorize(Roles)]` no backend? Muda aqui tambem — e o par desta tabela e
  /// `web/src/auth/permissoes.ts` (`CLAUDE.md`: "mudou lá, muda aqui"), so que do lado do backend.
  /// </summary>
  /// <remarks>
  /// A CHAVE e `"VERBO padrao-de-rota"`, e nao o nome do controller. Duas razoes:
  /// <list type="bullet">
  /// <item>Nome de tipo NAO identifica endpoint. Foi o achado C2: um segundo `SetoresController`
  /// noutro namespace colidia com o real no dicionario, e a colisao nao so deixava o novo passar
  /// como APAGAVA o sinal do antigo — a ampliacao de `Roles` no controller real ficava verde.
  /// Padrao de rota e o que o roteamento usa para distinguir endpoints, entao e o que distingue
  /// aqui.</item>
  /// <item>Perfil e decisao POR ACAO, nao por controller. `AgrupamentosController` ja mistura
  /// rotas sob `pedidos/` e sob `agrupamentos/`; agrupar por controller obrigava a somar os perfis
  /// de acoes diferentes numa uniao que nao existe em lugar nenhum do backend.</item>
  /// </list>
  /// O padrao de rota vem do `RoutePattern.RawText`, que NAO tem o `/api`: o prefixo e middleware
  /// (`UsePathBase`, ver `CLAUDE.md`), nao faz parte do padrao. Por isso as chaves sao
  /// `POST setores`, e nao `POST /api/setores`. A barra inicial e literal: rota de CONTROLLER nao
  /// tem (`setores`), rota de minimal API tem (`/perdas`, se `app.MapPost("/perdas", ...)` for a
  /// forma escrita) — a chave copia o texto como ele sai do `RawText`, e a mensagem de falha ja
  /// mostra a identidade exata a colar aqui.
  /// </remarks>
  private static readonly Dictionary<string, string[]> TabelaAprovada = new()
  {
    ["POST setores"] = ["Administrador"],
    ["PUT setores/{id:int}"] = ["Administrador"],
    ["PATCH setores/{id:int}/ativo"] = ["Administrador"],

    ["POST materiais"] = ["Administrador"],
    ["PUT materiais/{id:int}"] = ["Administrador"],
    ["PATCH materiais/{id:int}/ativo"] = ["Administrador"],

    ["POST componentes"] = ["Administrador", "PCP"],
    ["PUT componentes/{id:int}"] = ["Administrador", "PCP"],
    ["PATCH componentes/{id:int}/ativo"] = ["Administrador", "PCP"],

    ["POST pedidos"] = ["PCP", "Administrador"],
    ["PUT pedidos/{id:int}"] = ["PCP", "Administrador"],

    ["POST pedidos/{pedidoId:int}/agrupamentos"] = ["PCP", "Administrador"],
    ["PUT agrupamentos/{id:int}"] = ["PCP", "Administrador"],
    ["DELETE agrupamentos/{id:int}"] = ["PCP", "Administrador"],
  };

  /// <summary>
  /// Endpoints de escrita isentos de `[Authorize(Roles)]` POR DESENHO — nao por esquecimento. Cada
  /// entrada carrega o motivo por escrito, e a isencao vale para a IDENTIDADE (verbo + rota), nunca
  /// para o controller inteiro.
  /// </summary>
  /// <remarks>
  /// A granularidade e o achado I5: enquanto a isencao era pelo nome `AuthController`, um
  /// `[HttpPost("usuarios")]` novo naquele controller criava criacao de usuario ANONIMA com a
  /// guarda verde — a isencao valia para sempre, para qualquer acao futura, enquanto o comentario
  /// que a justificava falava so das tres acoes de sessao. Isentando as tres rotas nomeadas, a
  /// quarta rota aparece.
  /// </remarks>
  private static readonly Dictionary<string, string> IsentosDeAutorizacaoPorDesenho = new()
  {
    ["POST auth/login"] =
        "anonimo por contrato: e o proprio mecanismo de autenticacao, ninguem esta autenticado " +
        "ainda ao chama-lo.",
    ["POST auth/refresh"] =
        "anonimo por contrato: autentica pelo cookie de refresh, nao por access token — exigir " +
        "perfil aqui impediria renovar uma sessao expirada.",
    ["POST auth/logout"] =
        "anonimo por contrato: revoga o refresh token apresentado no cookie; exigir access token " +
        "valido impediria deslogar depois de ele expirar.",
  };

  /// <summary>Verbos que escrevem — os que exigem gate de perfil.</summary>
  private static readonly HashSet<string> VerbosDeEscrita =
      new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

  /// <summary>Um endpoint da tabela de roteamento real, reduzido ao que esta guarda pergunta.</summary>
  /// <param name="Identidade">`"VERBO padrao-de-rota"` — a chave da <see cref="TabelaAprovada"/>.</param>
  /// <param name="AceitaVerboDeEscrita">
  /// True se o endpoint casa POST/PUT/PATCH/DELETE — INCLUSIVE quando nao declara verbo nenhum,
  /// caso em que o ASP.NET o casa com TODOS os verbos (achado I1: mais permissivo que
  /// `[HttpPost]`, e invisivel para quem procura o atributo).
  /// </param>
  /// <param name="Perfis">Uniao dos `Roles` de todos os `IAuthorizeData` do endpoint.</param>
  /// <param name="Anonimo">Presenca de `[AllowAnonymous]`, que ANULA o `[Authorize(Roles)]`.</param>
  private sealed record EndpointRoteado(
      string Identidade, bool AceitaVerboDeEscrita, string[] Perfis, bool Anonimo);

  /// <summary>
  /// A tabela de roteamento real, lida uma unica vez para as tres asserções — subir o host tres
  /// vezes triplicaria o custo sem mudar o resultado. `Lazy` estatico ja e thread-safe por padrao,
  /// o que importa porque o xUnit roda os `[Fact]` de classes diferentes em paralelo.
  /// </summary>
  private static readonly Lazy<IReadOnlyList<EndpointRoteado>> Roteamento =
      new(LerTabelaDeRoteamento);

  private static IReadOnlyList<EndpointRoteado> LerTabelaDeRoteamento()
  {
    using var factory = new WebApplicationFactory<Program>();

    // `factory.Services` sobe o host (e so o host): resolver o EndpointDataSource nao abre conexao
    // com o SQL Server, entao esta guarda roda com o container fora do ar.
    var fonte = factory.Services.GetRequiredService<EndpointDataSource>();

    return fonte.Endpoints.Select(Descrever).ToList();
  }

  private static EndpointRoteado Descrever(Endpoint endpoint)
  {
    var rota = endpoint is RouteEndpoint roteado
        ? roteado.RoutePattern.RawText ?? "(padrao de rota vazio)"
        : "(endpoint sem padrao de rota)";

    // `HttpMethods` pode ser nulo ou vazio: e a acao que nao declara verbo. O ASP.NET a casa com
    // TODOS os verbos — a identidade abaixo grita isso em vez de omitir, porque nao existe entrada
    // legitima na tabela para um endpoint assim.
    var verbos = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
    var semVerbo = verbos is null || verbos.Count == 0;

    var identidade = semVerbo
        ? $"(SEM VERBO DECLARADO: ACEITA TODOS) {rota}"
        : $"{string.Join("|", verbos!.OrderBy(v => v, StringComparer.Ordinal))} {rota}";

    // `GetOrderedMetadata<IAuthorizeData>` ja entrega o `[Authorize]` da CLASSE e o do METODO
    // combinados pelo proprio framework. E isto que torna desnecessaria, por construcao, toda a
    // discussao de `inherit` e de uniao tipo+metodo que a versao por reflexao precisava fazer a
    // mao (e errava — achado A1).
    var perfis = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
        .Where(a => !string.IsNullOrWhiteSpace(a.Roles))
        .SelectMany(a => a.Roles!.Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct()
        .ToArray();

    var anonimo = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    var aceitaEscrita = semVerbo || verbos!.Any(VerbosDeEscrita.Contains);

    return new EndpointRoteado(identidade, aceitaEscrita, perfis, anonimo);
  }

  [Fact]
  public void Perfis_exigidos_batem_com_a_tabela_aprovada()
  {
    var roteamento = Roteamento.Value;

    foreach (var (identidade, perfisEsperados) in TabelaAprovada)
    {
      var encontrados = roteamento.Where(e => e.Identidade == identidade).ToList();

      // Iterar a tabela tambem pega REMOCAO e RENOMEACAO de rota: a entrada sobrevive a um
      // endpoint que sumiu, e a guarda passaria a proteger nada.
      Assert.True(
          encontrados.Count == 1,
          $"{identidade}: a tabela aprovada espera exatamente 1 endpoint roteado com essa " +
          $"identidade, o roteamento tem {encontrados.Count}. Se a rota mudou, atualize a chave " +
          "aqui (e o par em web/src/auth/permissoes.ts); se o endpoint sumiu, remova a entrada.");

      var endpoint = encontrados[0];

      // Por CONJUNTO, nao por string: o backend escreve "Administrador,PCP" num controller e
      // "PCP,Administrador" noutro, e a ordem nao e regra de negocio nenhuma.
      var esperado = new HashSet<string>(perfisEsperados);
      var real = new HashSet<string>(endpoint.Perfis);
      Assert.True(
          esperado.SetEquals(real),
          $"{identidade}: tabela aprovada tem [{string.Join(", ", esperado.OrderBy(p => p))}], " +
          $"o roteamento exige [{string.Join(", ", real.OrderBy(p => p))}].");

      // `[AllowAnonymous]` vence qualquer `[Authorize]` da cadeia: o endpoint continua declarando
      // os perfis certos e mesmo assim atende sem token nenhum (achado M1 — medido: `POST setores`
      // com `[AllowAnonymous]` respondeu 201 sem credencial). Esta e a SEGUNDA linha de defesa: o
      // analisador ASP0026 do proprio ASP.NET ja reprova a combinacao em tempo de compilacao, e
      // com `-warnaserror` (o build deste projeto) isso vira erro — medido nas duas formas,
      // atributo no metodo e atributo na classe. Esta asserção e o que sobra se alguem suprimir o
      // ASP0026 ou rodar um build sem `-warnaserror`.
      Assert.False(
          endpoint.Anonimo,
          $"{identidade}: o endpoint carrega [AllowAnonymous], que ANULA o [Authorize(Roles)] " +
          "declarado — a escrita fica aberta a qualquer um. Se a escrita e mesmo anonima por " +
          "desenho, mova a entrada para IsentosDeAutorizacaoPorDesenho com o motivo por escrito.");
    }
  }

  [Fact]
  public void Todo_endpoint_de_escrita_esta_na_tabela_ou_na_lista_de_isentos()
  {
    // A asserção de DESCOBERTA — a que protege a 1C. Iterar a tabela (como a asserção acima faz)
    // nunca acharia um endpoint NOVO fora dela. Aqui o lado iterado e o ROTEAMENTO REAL, entao
    // qualquer forma que produza rota de escrita aparece: acao herdada de base abstrata (C1),
    // acao sem verbo (I1), `[AcceptVerbs]` (I2), controller de outro assembly (I3), minimal API
    // (I4), rota nova no AuthController (I5) e atributo de verbo custom (M2).
    var semCobertura = Roteamento.Value
        .Where(e => e.AceitaVerboDeEscrita)
        .Select(e => e.Identidade)
        .Where(i => !TabelaAprovada.ContainsKey(i) && !IsentosDeAutorizacaoPorDesenho.ContainsKey(i))
        .Distinct()
        .OrderBy(i => i, StringComparer.Ordinal)
        .ToList();

    Assert.True(
        semCobertura.Count == 0,
        "Endpoint roteado que aceita verbo de escrita, fora da tabela aprovada e fora da lista de " +
        $"isentos: {string.Join(", ", semCobertura)}. Se a escrita deve ser restrita a perfis, " +
        "declare [Authorize(Roles = \"...\")] e acrescente a identidade a TabelaAprovada (e o par " +
        "em web/src/auth/permissoes.ts). Se e anonima por desenho (como as tres rotas de sessao " +
        "do auth), acrescente a identidade a IsentosDeAutorizacaoPorDesenho COM O MOTIVO. " +
        "Identidade '(SEM VERBO DECLARADO: ACEITA TODOS)' significa acao sem atributo de verbo: " +
        "ela casa POST tambem, e quase sempre o conserto e declarar o verbo, nao isentar.");
  }

  [Fact]
  public void Todo_endpoint_que_exige_perfil_esta_na_tabela()
  {
    // Complementar a de cima, num eixo que ela nao cobre: um endpoint de LEITURA que passe a
    // exigir `Roles` e uma permissao nova que `web/src/auth/permissoes.ts` nao conhece, e nenhum
    // verbo de escrita a denunciaria.
    var foraDaTabela = Roteamento.Value
        .Where(e => e.Perfis.Length > 0)
        .Select(e => e.Identidade)
        .Where(i => !TabelaAprovada.ContainsKey(i))
        .Distinct()
        .OrderBy(i => i, StringComparer.Ordinal)
        .ToList();

    Assert.True(
        foraDaTabela.Count == 0,
        "Endpoint com [Authorize(Roles)] fora da tabela aprovada: " +
        $"{string.Join(", ", foraDaTabela)}. Acrescente uma entrada em TabelaAprovada (e o par " +
        "correspondente em web/src/auth/permissoes.ts, se ainda nao existir).");
  }
}
