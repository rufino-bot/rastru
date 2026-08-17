using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Achado I3 da review de branch da 1B. A suite .NET era CEGA a ampliacao de `[Authorize(Roles)]`
/// e a controller NOVO sem `[Authorize(Roles)]` nenhum na tabela: as duas guardas que pegam essa
/// classe de regressao vivem em `web/` (`permissoesEspelhamOBackend.test.ts` e
/// `permissoes.test.ts`), e uma fase so-de-backend como a 1C pode rodar `dotnet test` sem nunca
/// rodar `npm test`. Esta classe enumera por REFLEXAO todo `[Authorize(Roles = ...)]` do
/// assembly da API e compara com uma tabela declarada aqui, para o backend ter sinal proprio.
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
  /// </remarks>
  private static Dictionary<string, string[]> DescobrirPerfisDeEscrita()
  {
    var controllers = typeof(Program).Assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    var resultado = new Dictionary<string, string[]>();
    foreach (var controller in controllers)
    {
      var perfis = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
          .SelectMany(m => m.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false))
          .Cast<AuthorizeAttribute>()
          .Where(a => !string.IsNullOrEmpty(a.Roles))
          .SelectMany(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries))
          .Distinct()
          .ToArray();

      if (perfis.Length > 0)
        resultado[controller.Name] = perfis;
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
}
