using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Achado I3 da review de branch da 1B, fechado em duas passadas (a segunda e o fix pass A1/A2 de
/// `fase1c-fix-divida-1b-review.md`). As duas guardas que pegam essa classe de regressao vivem em
/// `web/` (`permissoesEspelhamOBackend.test.ts` e `permissoes.test.ts`), e uma fase so-de-backend
/// como a 1C pode rodar `dotnet test` sem nunca rodar `npm test`. Esta classe fecha, por REFLEXAO
/// sobre `typeof(Program).Assembly` (sem tocar banco):
/// <list type="bullet">
/// <item>AMPLIAR um `[Authorize(Roles)]` ja listado — `Perfis_declarados_batem_com_a_tabela_aprovada`.</item>
/// <item>Controller NOVO com `[Authorize(Roles)]` fora da tabela, declarado no METODO ou na
/// CLASSE — `Nenhum_controller_com_Authorize_Roles_fica_fora_da_tabela`, que depois do fix A1
/// tambem enxerga o atributo do tipo.</item>
/// <item>Controller NOVO de escrita SEM `Roles` nenhum (nem `[Authorize]` simples, nem ausencia
/// total) — invisivel para as duas asserções acima, que so descobrem quem tem `Roles`;
/// `Controller_com_verbo_de_escrita_esta_na_tabela_ou_na_lista_de_isentos` descobre por VERBO
/// HTTP (POST/PUT/PATCH/DELETE) em vez de por atributo, entao nenhuma acao de escrita escapa.</item>
/// </list>
/// </summary>
public class PerfisDeEscritaDeclaradosTests
{
  /// <summary>
  /// Tabela aprovada: controller de escrita -> conjunto de perfis autorizados. Mudou um
  /// `[Authorize(Roles)]` no backend? Muda aqui tambem — e o par desta tabela e
  /// `web/src/auth/permissoes.ts` (`CLAUDE.md`: "mudou lá, muda aqui"), so que do lado do
  /// backend.
  /// </summary>
  private static readonly Dictionary<string, string[]> TabelaAprovada = new()
  {
    ["SetoresController"] = ["Administrador"],
    ["MateriaisController"] = ["Administrador"],
    ["ComponentesController"] = ["Administrador", "PCP"],
    ["PedidosController"] = ["PCP", "Administrador"],
    ["AgrupamentosController"] = ["PCP", "Administrador"],
  };

  /// <summary>
  /// Todo controller do assembly da API (via `typeof(Program).Assembly` — alcancavel porque a
  /// suite inteira ja usa `WebApplicationFactory&lt;Program&gt;`) que declara pelo menos um
  /// `[Authorize(Roles = ...)]` em algum metodo de acao, com o conjunto de perfis UNIAO de
  /// todas as acoes daquele controller.
  /// </summary>
  /// <remarks>
  /// `inherit: false` na leitura do atributo (<see cref="GetCustomAttributes(MemberInfo, bool)"/>
  /// com `false`), DE PROPOSITO: hoje nenhuma classe base de controller
  /// (`CadastroControllerBase`) carrega `[Authorize(Roles)]`, entao o resultado e identico com
  /// `inherit: true`. A escolha e `false` porque a decisao de QUEM pode escrever pertence ao
  /// controller CONCRETO, no ponto de rota real — se um dia `CadastroControllerBase` ganhar um
  /// `[Authorize(Roles)]` (ex.: um perfil minimo valido para toda escrita de cadastro),
  /// `inherit: false` continua listando so os perfis que o controller concreto decidiu, sem
  /// herdar em silencio um perfil que o controller nao pediu explicitamente. `inherit: true`
  /// esconderia essa fonte, e a guarda pararia de nomear de onde o perfil realmente vem.
  /// Ortogonal ao fix do A1 (uniao do atributo do TIPO com o dos METODOS, abaixo): `inherit`
  /// fala de CLASSE BASE — o buraco do A1 era a propria classe do controller concreto, que
  /// `inherit: false` sempre leu; so faltava incluir o atributo do TIPO na uniao.
  /// </remarks>
  private static Dictionary<string, string[]> DescobrirPerfisDeEscrita()
  {
    var controllers = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    var resultado = new Dictionary<string, string[]>();
    foreach (var controller in controllers)
    {
      // Une o atributo do TIPO ao dos METODOS: o ASP.NET combina os dois filtros em runtime,
      // entao [Authorize(Roles)] declarado uma vez na classe autoriza identico a repeti-lo em
      // cada acao. Antes desta uniao um controller que centralizasse os perfis no tipo (em vez
      // de repetir em cada metodo — a forma mais limpa de escrever, nao um erro) nunca aparecia
      // em `resultado`: achado A1 da review do fix da 1B.
      var doTipo = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
          .Cast<AuthorizeAttribute>();

      var dosMetodos = controller
          .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
          .SelectMany(m => m.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false))
          .Cast<AuthorizeAttribute>();

      var perfis = doTipo.Concat(dosMetodos)
          .Where(a => !string.IsNullOrEmpty(a.Roles))
          .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries))
          .Distinct()
          .ToArray();

      if (perfis.Length > 0)
        resultado[controller.Name] = perfis;
    }

    return resultado;
  }

  /// <summary>
  /// Controllers de escrita isentos de `[Authorize(Roles)]` POR DESENHO — nao por esquecimento.
  /// Hoje so `AuthController`: `login`/`refresh`/`logout` sao POST anonimos por contrato (e o
  /// proprio mecanismo de autenticacao — ninguem esta autenticado ainda ao chama-los). Adicionar
  /// um nome aqui exige o mesmo raciocinio por escrito nesta lista; e a lista EXPLICITA, nao um
  /// controller que simplesmente nao apareceu em lugar nenhum, que faz uma omissao nova ficar
  /// barulhenta em vez de silenciosa.
  /// </summary>
  private static readonly string[] IsentosDeAutorizacaoPorDesenho = ["AuthController"];

  /// <summary>
  /// Todo controller do assembly da API que declara pelo menos uma acao com verbo de escrita
  /// (POST/PUT/PATCH/DELETE) — usado pela asserção de DESCOBERTA do achado A2: gatilho por VERBO,
  /// nao por presenca de `[Authorize(Roles)]`. `DescobrirPerfisDeEscrita` so enxerga quem
  /// DECLAROU `Roles`; um controller com `[Authorize]` nu (sem `Roles`) ou sem atributo nenhum
  /// nunca entra la, e por isso nunca aparece em `descobertos` nas duas asserções acima — os dois
  /// lados olhariam para o mesmo conjunto vazio e concordariam. Verbo HTTP nenhuma acao de
  /// escrita escapa de declarar, entao este metodo enxerga o controller de qualquer forma.
  /// </summary>
  private static HashSet<string> DescobrirControllersComVerboDeEscrita()
  {
    var verbosDeEscrita = new HashSet<string> { "POST", "PUT", "PATCH", "DELETE" };

    var controllers = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    var resultado = new HashSet<string>();
    foreach (var controller in controllers)
    {
      var temVerboDeEscrita = controller
          .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
          .SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), inherit: false))
          .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
          .Any(a => a.HttpMethods.Any(verbosDeEscrita.Contains));

      if (temVerboDeEscrita)
        resultado.Add(controller.Name);
    }

    return resultado;
  }

  [Fact]
  public void Perfis_declarados_batem_com_a_tabela_aprovada()
  {
    var descobertos = DescobrirPerfisDeEscrita();

    foreach (var (controller, perfisEsperados) in TabelaAprovada)
    {
      Assert.True(
          descobertos.TryGetValue(controller, out var perfisReais),
          $"{controller}: esperava [Authorize(Roles)] declarado, mas o controller nao tem nenhum.");

      // Por CONJUNTO, nao por string: o backend escreve "Administrador,PCP" num controller e
      // "PCP,Administrador" noutro, e a ordem nao e regra de negocio nenhuma.
      var esperado = new HashSet<string>(perfisEsperados);
      var real = new HashSet<string>(perfisReais!);
      Assert.True(
          esperado.SetEquals(real),
          $"{controller}: tabela aprovada tem [{string.Join(", ", esperado.OrderBy(p => p))}], " +
          $"backend declara [{string.Join(", ", real.OrderBy(p => p))}].");
    }
  }

  [Fact]
  public void Nenhum_controller_com_Authorize_Roles_fica_fora_da_tabela()
  {
    // A asserção de DESCOBERTA — a que protege a 1C. Iterar a TABELA (como o teste acima faz)
    // nunca acharia um controller NOVO fora dela; e preciso iterar o que a REFLEXAO achou e
    // conferir que a tabela conhece cada um. Sem isto, ComponenteFilhoPadraoController (ou
    // qualquer um dos outros dois recursos da 1C) podia nascer com `[Authorize(Roles = "...")]`
    // errado e a suite .NET inteira continuaria verde.
    var descobertos = DescobrirPerfisDeEscrita();

    var semEntradaNaTabela = descobertos.Keys.Except(TabelaAprovada.Keys).ToList();

    Assert.True(
        semEntradaNaTabela.Count == 0,
        "Controller com [Authorize(Roles)] fora da tabela aprovada: " +
        $"{string.Join(", ", semEntradaNaTabela)}. Adicione uma entrada em TabelaAprovada " +
        "(e o par correspondente em web/src/auth/permissoes.ts, se ainda nao existir).");
  }

  [Fact]
  public void Controller_com_verbo_de_escrita_esta_na_tabela_ou_na_lista_de_isentos()
  {
    // Achado A2: o teste acima so descobre controller que DECLAROU `Roles`. Um controller de
    // escrita com `[Authorize]` nu, ou sem atributo de autorizacao nenhum, nao aparece la — e
    // aqui, iterando pelo VERBO HTTP, e onde essa omissao vira barulho. Isto e o pior dos dois
    // buracos: nao e "perfil errado", e "escrita liberada a qualquer autenticado" (ou a qualquer
    // um, sem `[Authorize]` nenhum).
    var comVerboDeEscrita = DescobrirControllersComVerboDeEscrita();
    var isentos = new HashSet<string>(IsentosDeAutorizacaoPorDesenho);

    var semCobertura = comVerboDeEscrita
        .Where(c => !TabelaAprovada.ContainsKey(c) && !isentos.Contains(c))
        .ToList();

    Assert.True(
        semCobertura.Count == 0,
        "Controller com verbo de escrita (POST/PUT/PATCH/DELETE) fora da tabela aprovada e fora " +
        $"da lista de isentos: {string.Join(", ", semCobertura)}. Se a escrita deve ser " +
        "restrita a perfis, adicione [Authorize(Roles = \"...\")] e uma entrada em " +
        "TabelaAprovada (e o par em web/src/auth/permissoes.ts). Se a escrita e anonima por " +
        "desenho (como AuthController), adicione o nome a IsentosDeAutorizacaoPorDesenho com o " +
        "motivo.");
  }
}
