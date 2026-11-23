using System.Globalization;

namespace BuscaPorFoto.Execucao;

public enum Condicao { SemMarcador, ComMarcador }

public enum NomeDoPontuador { Hu, IoU }

/// <param name="Id">Id anonimizado (<c>A01</c>, <c>B01</c>, <c>I01</c>…) — nunca o código real.</param>
/// <param name="Posicao">Pessimista, por grupo. Nulo quando <paramref name="Falha"/> impediu a consulta.</param>
/// <param name="PontuacaoDaCerta">Pontuação do grupo da peça certa (a melhor das referências e vistas dele).</param>
/// <param name="MelhorErrada">A maior pontuação entre os demais grupos; nulo se não há outro.</param>
/// <param name="ReferenciasNoGrupoDaCerta">0 quando o alvo não tem referência (não saiu separado do nesting).</param>
/// <param name="Grupos">Quantos candidatos (grupos) o ranking teve.</param>
public sealed record LinhaDeResultado(
    string Id, Cenario Cenario, TipoDePeca? Tipo, Condicao Condicao, NomeDoPontuador Pontuador,
    int? Posicao, string? Falha, double? PontuacaoDaCerta, double? MelhorErrada,
    int ReferenciasNoGrupoDaCerta, int Grupos, long Milissegundos);

public static class ArquivoDeResultados
{
    public const string Cabecalho = "id;cenario;tipo;condicao;pontuador;posicao;falha;pontuacao_da_certa;melhor_errada;referencias_no_grupo;grupos;ms";

    public static void Escrever(string caminho, IEnumerable<LinhaDeResultado> linhas)
    {
        var texto = linhas.Select(l => string.Join(';',
            l.Id,
            l.Cenario,
            l.Tipo?.ToString() ?? "",
            l.Condicao,
            l.Pontuador,
            Numero(l.Posicao),
            (l.Falha ?? "").Replace(';', ',').Replace('\n', ' ').Replace('\r', ' '),
            Numero(l.PontuacaoDaCerta),
            Numero(l.MelhorErrada),
            Inteiro(l.ReferenciasNoGrupoDaCerta),
            Inteiro(l.Grupos),
            Inteiro(l.Milissegundos)));
        File.WriteAllLines(caminho, texto.Prepend(Cabecalho));
    }

    public static IReadOnlyList<LinhaDeResultado> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var c = l.Split(';');
            if (c.Length != 12) throw new InvalidDataException($"Linha com {c.Length} colunas em {caminho}.");
            return new LinhaDeResultado(
                c[0],
                Enum.Parse<Cenario>(c[1]),
                c[2].Length == 0 ? null : Enum.Parse<TipoDePeca>(c[2]),
                Enum.Parse<Condicao>(c[3]),
                Enum.Parse<NomeDoPontuador>(c[4]),
                c[5].Length == 0 ? null : int.Parse(c[5], CultureInfo.InvariantCulture),
                c[6].Length == 0 ? null : c[6],
                c[7].Length == 0 ? null : double.Parse(c[7], CultureInfo.InvariantCulture),
                c[8].Length == 0 ? null : double.Parse(c[8], CultureInfo.InvariantCulture),
                int.Parse(c[9], CultureInfo.InvariantCulture),
                int.Parse(c[10], CultureInfo.InvariantCulture),
                long.Parse(c[11], CultureInfo.InvariantCulture));
        }).ToList();
    }

    private static string Numero(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Inteiro(long v) => v.ToString(CultureInfo.InvariantCulture);
    // "R": ida e volta exata, para o arquivo devolver o mesmo número que o executor calculou.
    private static string Numero(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";
}
