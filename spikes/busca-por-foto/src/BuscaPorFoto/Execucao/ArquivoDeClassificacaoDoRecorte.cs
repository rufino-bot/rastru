namespace BuscaPorFoto.Execucao;

/// <param name="RecorteOk">Verdadeiro "ok", falso "falho", nulo ainda não classificado.</param>
public sealed record ClassificacaoDoRecorte(string Id, Condicao Condicao, bool? RecorteOk);

/// <summary>
/// Preenchido à mão, olhando as sobreposições: <c>recorte</c> é <c>ok</c>, <c>falho</c> ou vazio. Não
/// há coluna de par espelhado: o grupo de geometria idêntica já o diz.
/// </summary>
public static class ArquivoDeClassificacaoDoRecorte
{
    public const string Cabecalho = "id;condicao;recorte";

    public static IReadOnlyList<ClassificacaoDoRecorte> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Trim().Length > 0).Select(l =>
        {
            var c = l.Split(';');
            if (c.Length != 3) throw new InvalidDataException($"Linha com {c.Length} colunas em {caminho}: '{l}'.");
            bool? ok = c[2].Trim().ToLowerInvariant() switch
            {
                "ok" => true,
                "falho" => false,
                "" => null,
                var outro => throw new InvalidDataException($"Recorte '{outro}' em {caminho}: use ok, falho ou vazio."),
            };
            return new ClassificacaoDoRecorte(c[0], Enum.Parse<Condicao>(c[1]), ok);
        }).ToList();
    }
}
