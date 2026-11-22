namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Números calculados do polígono (não dos pixels) que agrupam peças geometricamente iguais.
/// <see cref="Valores"/> = [área da região, perímetro (borda e furos), soma das áreas dos furos, λ1, λ2],
/// com λ1 ≥ λ2 os momentos principais de segunda ordem em torno do centróide. O agrupador é da Task 6.
/// </summary>
public sealed record AssinaturaGeometrica(int Furos, double[] Valores);

/// <summary>
/// Uma referência do catálogo: a chave é só um id opaco (<c>K0001</c>…); o agrupamento olha apenas a
/// <see cref="Assinatura"/>, nunca as vistas nem o conteúdo da chave.
/// </summary>
public sealed record Referencia(string Chave, IReadOnlyList<Mascara> Vistas, AssinaturaGeometrica Assinatura);

/// <summary>
/// Junta referências geometricamente idênticas (cópias, e o par esquerdo/direito, que tem os mesmos
/// invariantes), pela assinatura exata do polígono ou da malha — não pela sobreposição de máscaras.
/// </summary>
public static class AgrupadorDeReferencias
{
    /// <summary>Diferença máxima entre dois valores, relativa ao maior módulo dos dois.</summary>
    public const double ToleranciaRelativa = 0.005;

    public static bool Identicas(AssinaturaGeometrica a, AssinaturaGeometrica b)
    {
        if (a.Furos != b.Furos || a.Valores.Length != b.Valores.Length) return false;
        for (var i = 0; i < a.Valores.Length; i++)
        {
            double x = a.Valores[i], y = b.Valores[i];
            if (Math.Abs(x - y) > ToleranciaRelativa * Math.Max(Math.Abs(x), Math.Abs(y))) return false;
        }
        return true;
    }

    /// <summary>
    /// Grupos de chaves, fechados por transitividade (união de conjuntos sobre todos os pares).
    /// Ordem determinística: chaves em ordem ordinal dentro do grupo, grupos pela menor chave.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Agrupar(IReadOnlyList<Referencia> referencias)
    {
        var n = referencias.Count;
        var pai = Enumerable.Range(0, n).ToArray();
        int Raiz(int i) { while (pai[i] != i) { pai[i] = pai[pai[i]]; i = pai[i]; } return i; }

        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (Identicas(referencias[i].Assinatura, referencias[j].Assinatura))
                    pai[Raiz(i)] = Raiz(j);

        return Enumerable.Range(0, n)
            .GroupBy(Raiz)
            .Select(g => (IReadOnlyList<string>)g.Select(i => referencias[i].Chave).OrderBy(c => c, StringComparer.Ordinal).ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal)
            .ToList();
    }
}
