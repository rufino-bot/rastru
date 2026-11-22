namespace BuscaPorFoto.Comparacao;

public static class Ranking
{
    /// <summary>
    /// Posição (1 = primeiro) da peça certa, maior pontuação primeiro. EMPATE CONTA CONTRA: todo
    /// candidato com pontuação igual à da certa fica à frente dela. Assim a ordem de enumeração —
    /// que vem de nome de arquivo — nunca melhora o resultado.
    /// </summary>
    public static int PosicaoPessimista(IReadOnlyDictionary<string, double> pontuacaoPorCandidato, string idDaCerta)
    {
        if (!pontuacaoPorCandidato.TryGetValue(idDaCerta, out var daCerta))
            throw new ArgumentException($"O candidato certo '{idDaCerta}' não está entre os pontuados.", nameof(idDaCerta));
        if (pontuacaoPorCandidato.Values.Any(double.IsNaN))
            throw new ArgumentException("Pontuação NaN: não há ordem definida.", nameof(pontuacaoPorCandidato));

        return 1 + pontuacaoPorCandidato.Count(kv => kv.Key != idDaCerta && kv.Value >= daCerta);
    }
}
