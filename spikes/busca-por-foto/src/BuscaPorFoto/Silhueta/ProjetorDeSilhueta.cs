using System.Numerics;
using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Stl;
using OpenCvSharp;

namespace BuscaPorFoto.Silhueta;

public static class ProjetorDeSilhueta
{
    public const double MmPorPixel = 0.5;

    /// <summary>
    /// As três vistas principais, uma por eixo, sempre: a primeira olha ao longo do eixo de menor
    /// dispersão (numa chapa, a face maior). Toda peça do cenário B tem três, porque a dobrada pode
    /// estar apoiada na base ou nas abas (spec §5.2). A vista do lado oposto de um eixo não entra:
    /// é o espelho da primeira, e os pontuadores já testam espelho.
    /// </summary>
    public static IReadOnlyList<Mascara> Projetar(IReadOnlyList<Triangulo> malha)
    {
        if (malha.Count == 0)
            throw new ArgumentException("Malha vazia.", nameof(malha));

        var (centro, eixos, _) = EixosPrincipais(malha);
        return new[]
        {
            Vista(malha, centro, eixos[0], eixos[1]),
            Vista(malha, centro, eixos[0], eixos[2]),
            Vista(malha, centro, eixos[1], eixos[2]),
        };
    }

    /// <summary>
    /// Centro e eixos principais da SUPERFÍCIE, ordenados por dispersão decrescente. Usa o segundo
    /// momento exato de cada triângulo, ∫ p pᵀ dA = (A/12)·(Σ vᵢvᵢᵀ + (Σ vᵢ)(Σ vᵢ)ᵀ), e não os
    /// vértices ponderados: estes dependem da triangulação — numa caixa com a diagonal de cada face
    /// escolhida de um lado só, os eixos saíam girados 16°.
    /// </summary>
    private static (Vector3 Centro, Vector3[] Eixos, double[] Autovalores) EixosPrincipais(IReadOnlyList<Triangulo> malha)
    {
        double areaTotal = 0, cx = 0, cy = 0, cz = 0;
        foreach (var t in malha)
        {
            var area = Area(t);
            areaTotal += area;
            cx += area * (t.A.X + t.B.X + t.C.X) / 3;
            cy += area * (t.A.Y + t.B.Y + t.C.Y) / 3;
            cz += area * (t.A.Z + t.B.Z + t.C.Z) / 3;
        }
        if (areaTotal <= 0)
            throw new ArgumentException("Malha sem área.", nameof(malha));
        cx /= areaTotal; cy /= areaTotal; cz /= areaTotal;

        var cov = new double[3, 3];
        foreach (var t in malha)
        {
            var area = Area(t);
            var d = new[] { t.A, t.B, t.C }
                .Select(v => new[] { v.X - cx, v.Y - cy, v.Z - cz })
                .ToArray();
            var soma = new[] { d[0][0] + d[1][0] + d[2][0], d[0][1] + d[1][1] + d[2][1], d[0][2] + d[1][2] + d[2][2] };
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    cov[i, j] += area / 12 * (d[0][i] * d[0][j] + d[1][i] * d[1][j] + d[2][i] * d[2][j] + soma[i] * soma[j]);
        }

        var (valores, vetores) = Jacobi(cov);
        var ordem = Enumerable.Range(0, 3).OrderByDescending(i => valores[i]).ToArray();
        var eixos = ordem
            .Select(i => Vector3.Normalize(new Vector3((float)vetores[0, i], (float)vetores[1, i], (float)vetores[2, i])))
            .ToArray();
        return (new Vector3((float)cx, (float)cy, (float)cz), eixos, ordem.Select(i => valores[i]).ToArray());
    }

    /// <summary>
    /// Assinatura da malha, invariante a rotação e translação: [área de superfície, λ1, λ2, λ3,
    /// extensão nos três eixos principais], com λ1 ≥ λ2 ≥ λ3 os autovalores do mesmo tensor de
    /// segundo momento da superfície que dá os eixos das vistas, e as extensões medidas ao longo
    /// desses eixos (max − min da projeção dos vértices). Sem furos contados: <c>Furos = 0</c>.
    /// </summary>
    public static AssinaturaGeometrica Assinatura(IReadOnlyList<Triangulo> malha)
    {
        if (malha.Count == 0)
            throw new ArgumentException("Malha vazia.", nameof(malha));

        var (centro, eixos, autovalores) = EixosPrincipais(malha);
        var vertices = malha.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        var extensoes = eixos.Select(e =>
        {
            var proj = vertices.Select(v => (double)Vector3.Dot(v - centro, e)).ToList();
            return proj.Max() - proj.Min();
        });
        var area = malha.Sum(t => (double)Area(t));
        return new AssinaturaGeometrica(0, [area, .. autovalores, .. extensoes]);
    }

    private static double Area(Triangulo t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;

    private static Mascara Vista(IReadOnlyList<Triangulo> malha, Vector3 centro, Vector3 eixoU, Vector3 eixoV)
    {
        var projetados = malha
            .Select(t => new[] { t.A, t.B, t.C }
                .Select(v => (U: (double)Vector3.Dot(v - centro, eixoU), V: (double)Vector3.Dot(v - centro, eixoV)))
                .ToArray())
            .ToList();

        var minU = projetados.SelectMany(p => p).Min(p => p.U);
        var maxU = projetados.SelectMany(p => p).Max(p => p.U);
        var minV = projetados.SelectMany(p => p).Min(p => p.V);
        var maxV = projetados.SelectMany(p => p).Max(p => p.V);

        const int margem = 2;
        var largura = (int)Math.Ceiling((maxU - minU) / MmPorPixel) + 2 * margem;
        var altura = (int)Math.Ceiling((maxV - minV) / MmPorPixel) + 2 * margem;
        var cheia = new Mat(altura, largura, MatType.CV_8UC1, Scalar.All(0));

        // Um FillConvexPoly por triângulo, e não um FillPoly com todos: o FillPoly preenche por
        // paridade, e as faces de cima e de baixo da chapa se cancelariam.
        foreach (var tri in projetados)
        {
            var pontos = tri
                .Select(p => new Point(
                    (int)Math.Round((p.U - minU) / MmPorPixel) + margem,
                    (int)Math.Round((p.V - minV) / MmPorPixel) + margem))
                .ToArray();
            Cv2.FillConvexPoly(cheia, pontos, Scalar.All(255));
        }

        using (cheia)
            return Mascara.DeMascaraCheia(cheia, MmPorPixel)
                ?? throw new InvalidOperationException("A projeção não gerou pixel nenhum.");
    }

    /// <summary>Autovalores e autovetores (em colunas) de uma matriz 3×3 simétrica, por rotações de Jacobi.</summary>
    private static (double[] Valores, double[,] Vetores) Jacobi(double[,] matriz)
    {
        var a = (double[,])matriz.Clone();
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        for (var varredura = 0; varredura < 50; varredura++)
        {
            var foraDaDiagonal = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (foraDaDiagonal < 1e-12 * (Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1e-300))
                break;

            for (var p = 0; p < 2; p++)
                for (var q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;

                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }

        return (new[] { a[0, 0], a[1, 1], a[2, 2] }, v);
    }
}
