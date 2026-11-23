using System.Text.RegularExpressions;
using BuscaPorFoto.Dxf;

namespace BuscaPorFoto.Execucao;

/// <summary>
/// O <c>nesting.txt</c> que o comando <c>nesting</c> grava: as opções da separação e quantas peças saíram de
/// cada nesting. O número que o usuário escreve no <c>alvos.txt</c> vem da imagem daquele comando, mas o
/// <c>executar … corte</c> separa de novo: com uma opção diferente (a borda, uma camada ignorada) a numeração
/// muda, o número aponta outra peça, e o resultado sai errado sem aviso. Por isso o executar confere.
/// </summary>
internal static class ConferenciaDoNesting
{
    public const string Arquivo = "nesting.txt";

    private static readonly Regex ChaveDeNesting = new(@"^N\d{2,}$");

    public static IReadOnlyList<string> Linhas(BordaDaChapa borda, IReadOnlySet<string> camadasIgnoradas, IEnumerable<(string Id, int Pecas)> nestings) =>
    [
        $"borda={borda.ToString().ToLowerInvariant()}",
        $"camadas-ignoradas={string.Join(',', camadasIgnoradas.Order(StringComparer.Ordinal))}",
        .. nestings.Select(n => $"{n.Id}={n.Pecas}"),
    ];

    /// <summary>
    /// O que difere entre o que o <c>nesting</c> gravou em <paramref name="pastaDoNesting"/> e o que esta execução
    /// separou. Vazio quando bate. O texto vai para o <c>faltando.txt</c> (local); só ids e opções, nunca nome de arquivo.
    /// </summary>
    public static IReadOnlyList<string> Divergencias(
        string pastaDoNesting, BordaDaChapa borda, IReadOnlySet<string> camadasIgnoradas, IEnumerable<(string Id, int Pecas)> nestings)
    {
        var caminho = Path.Combine(pastaDoNesting, Arquivo);
        if (!File.Exists(caminho))
            return [$"{caminho}: não existe; rode o comando nesting nesta pasta antes de conferir"];

        var gravado = Ler(File.ReadAllLines(caminho));
        var agora = Ler(Linhas(borda, camadasIgnoradas, nestings));
        var problemas = new List<string>();
        foreach (var chave in gravado.Keys.Union(agora.Keys).Order(StringComparer.Ordinal))
        {
            var antes = gravado.GetValueOrDefault(chave);
            var depois = agora.GetValueOrDefault(chave);
            if (antes == depois) continue;
            problemas.Add(ChaveDeNesting.IsMatch(chave)
                ? $"{caminho}: {chave} tinha {antes ?? "nenhuma"} peça(s) no comando nesting e tem {depois ?? "nenhuma"} nesta execução"
                : $"{caminho}: {chave} era '{antes ?? ""}' no comando nesting e é '{depois ?? ""}' nesta execução; repita as mesmas opções");
        }
        return problemas;
    }

    private static Dictionary<string, string> Ler(IEnumerable<string> linhas)
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var linha in linhas)
        {
            var igual = linha.IndexOf('=');
            if (igual > 0) mapa[linha[..igual].Trim()] = linha[(igual + 1)..].Trim();
        }
        return mapa;
    }
}
