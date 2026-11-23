using System.Globalization;

namespace BuscaPorFoto.Execucao;

/// <param name="Grupo">Id opaco do grupo (<c>G0001</c>…); só vale dentro do universo da consulta.</param>
/// <param name="Certo">Verdadeiro no grupo que contém a referência do alvo.</param>
public sealed record LinhaDePontuacao(string Id, Condicao Condicao, NomeDoPontuador Pontuador, string Grupo, double Pontuacao, bool Certo);

/// <summary>
/// Uma linha por grupo de cada consulta de IoU que não falhou: é o que o relatório usa para o top-3 em
/// pilhas menores e para a melhor errada.
/// </summary>
public static class ArquivoDePontuacoes
{
    public const string Cabecalho = "id;condicao;pontuador;grupo;pontuacao;certo";

    public static void Escrever(string caminho, IEnumerable<LinhaDePontuacao> linhas)
    {
        var texto = linhas.Select(l => string.Join(';',
            l.Id, l.Condicao, l.Pontuador, l.Grupo,
            l.Pontuacao.ToString("R", CultureInfo.InvariantCulture),
            l.Certo ? "1" : "0"));
        TextoEmDisco.GravarLinhas(caminho, texto.Prepend(Cabecalho));
    }

    public static IReadOnlyList<LinhaDePontuacao> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var c = l.Split(';');
            if (c.Length != 6) throw new InvalidDataException($"Linha com {c.Length} colunas em {caminho}.");
            return new LinhaDePontuacao(
                c[0], Enum.Parse<Condicao>(c[1]), Enum.Parse<NomeDoPontuador>(c[2]), c[3],
                double.Parse(c[4], CultureInfo.InvariantCulture), c[5] == "1");
        }).ToList();
    }
}
