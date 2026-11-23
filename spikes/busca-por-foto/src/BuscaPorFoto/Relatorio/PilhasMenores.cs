using BuscaPorFoto.Execucao;

namespace BuscaPorFoto.Relatorio;

/// <param name="Tamanho">Grupos na pilha: o certo e (Tamanho − 1) errados.</param>
/// <param name="AcertosMedios">Soma, sobre os alvos, da fração de sorteios em que o certo ficou no top-3.</param>
/// <param name="Alvos">Alvos que entraram na simulação (os que têm pontuações na condição).</param>
public sealed record ResultadoDaPilha(int Tamanho, double AcertosMedios, int Alvos);

/// <summary>
/// Simulação de pilhas menores, só informativa (spec §8): não entra na decisão. Usa apenas as
/// pontuações gravadas — não sabe de código de peça — e é determinística pela semente.
/// </summary>
public static class PilhasMenores
{
    public static readonly IReadOnlyList<int> Tamanhos = [10, 30, 60];
    public const int Sorteios = 200;
    public const int Semente = 20261122;

    /// <summary>
    /// Para cada tamanho, <paramref name="sorteios"/> sorteios de (tamanho − 1) grupos errados mais o
    /// certo, por alvo, só com o IoU da <paramref name="condicao"/>. O empate com a certa conta contra
    /// ela, como no ranking do executor. Uma consulta com menos grupos errados do que o pedido usa todos.
    /// </summary>
    public static IReadOnlyList<ResultadoDaPilha> Simular(
        IReadOnlyList<LinhaDePontuacao> pontuacoes, Condicao condicao, IReadOnlyList<int> tamanhos, int sorteios, int semente)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sorteios, 1);
        var consultas = pontuacoes
            .Where(p => p.Condicao == condicao && p.Pontuador == NomeDoPontuador.IoU)
            .GroupBy(p => p.Id)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var certas = g.Where(p => p.Certo).ToList();
                if (certas.Count != 1)
                    throw new InvalidDataException($"A consulta {g.Key} tem {certas.Count} grupos certos; esperado 1.");
                var erradas = g.Where(p => !p.Certo).OrderBy(p => p.Grupo, StringComparer.Ordinal).Select(p => p.Pontuacao).ToArray();
                return (Certa: certas[0].Pontuacao, Erradas: erradas);
            })
            .ToList();

        var resultado = new List<ResultadoDaPilha>();
        foreach (var tamanho in tamanhos)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(tamanho, 1);
            var sorteador = new Random(unchecked(semente * 31 + tamanho));
            var soma = 0.0;
            foreach (var (certa, erradas) in consultas)
            {
                var k = Math.Min(tamanho - 1, erradas.Length);
                var indices = Enumerable.Range(0, erradas.Length).ToArray();
                var acertos = 0;
                for (var s = 0; s < sorteios; s++)
                {
                    // Fisher–Yates parcial: os k primeiros índices são a amostra.
                    var adiante = 0;
                    for (var i = 0; i < k; i++)
                    {
                        var j = sorteador.Next(i, indices.Length);
                        (indices[i], indices[j]) = (indices[j], indices[i]);
                        if (erradas[indices[i]] >= certa) adiante++;
                    }
                    if (1 + adiante <= 3) acertos++;
                }
                soma += (double)acertos / sorteios;
            }
            resultado.Add(new ResultadoDaPilha(tamanho, soma, consultas.Count));
        }
        return resultado;
    }
}
