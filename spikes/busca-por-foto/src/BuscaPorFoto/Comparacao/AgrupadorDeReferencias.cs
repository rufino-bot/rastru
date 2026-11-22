namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Números calculados do polígono (não dos pixels) que agrupam peças geometricamente iguais.
/// <see cref="Valores"/> = [área da região, perímetro (borda e furos), soma das áreas dos furos, λ1, λ2],
/// com λ1 ≥ λ2 os momentos principais de segunda ordem em torno do centróide. O agrupador é da Task 6.
/// </summary>
public sealed record AssinaturaGeometrica(int Furos, double[] Valores);
