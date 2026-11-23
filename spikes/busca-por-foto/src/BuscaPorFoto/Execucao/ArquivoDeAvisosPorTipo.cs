using System.Globalization;

namespace BuscaPorFoto.Execucao;

/// <param name="Nesting">Id anonimizado do nesting (<c>N01</c>…) — nunca o nome do arquivo.</param>
/// <param name="Tipo">O aviso sem nome de bloco, de entidade nem número (<see cref="Executor.TipoDoAviso"/>).</param>
public sealed record LinhaDeAvisoPorTipo(string Nesting, string Tipo, int N);

/// <summary>
/// Os avisos da leitura do DXF contados por tipo e por nesting. É o que o relatório do Corte mostra: a spec
/// §10.3 manda que entidade desconhecida, contorno aberto e peça não separada <i>apareçam no relatório</i>, e o
/// <c>avisos-do-dxf.txt</c> inteiro (com nome de bloco) fica na máquina do usuário.
/// </summary>
public static class ArquivoDeAvisosPorTipo
{
    public const string Cabecalho = "nesting;tipo;n";

    public static void Escrever(string caminho, IEnumerable<LinhaDeAvisoPorTipo> linhas) =>
        TextoEmDisco.GravarLinhas(caminho, linhas
            .Select(l => $"{l.Nesting};{l.Tipo.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ')};{l.N.ToString(CultureInfo.InvariantCulture)}")
            .Prepend(Cabecalho));

    public static IReadOnlyList<LinhaDeAvisoPorTipo> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var c = l.Split(';');
            if (c.Length != 3) throw new InvalidDataException($"Linha com {c.Length} colunas em {caminho}.");
            return new LinhaDeAvisoPorTipo(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture));
        }).ToList();
    }
}
