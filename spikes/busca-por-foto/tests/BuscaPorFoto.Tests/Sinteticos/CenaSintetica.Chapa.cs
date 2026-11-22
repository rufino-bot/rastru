using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

public static partial class CenaSintetica
{
    /// <summary>
    /// Desenha uma chapa retangular com furo quadrado central, girada <paramref name="anguloGraus"/>
    /// em torno de <paramref name="centroMm"/>. O furo recebe <paramref name="corDoFundo"/>.
    /// </summary>
    public static void DesenharChapa(Mat imagem, Point2d centroMm, double larguraMm, double alturaMm,
        double ladoDoFuroMm, double anguloGraus, double pixelsPorMm, Scalar corDaPeca, Scalar corDoFundo)
    {
        Point[] Retangulo(double l, double a)
        {
            var rad = anguloGraus * Math.PI / 180;
            var (c, s) = (Math.Cos(rad), Math.Sin(rad));
            return new[] { (-l / 2, -a / 2), (l / 2, -a / 2), (l / 2, a / 2), (-l / 2, a / 2) }
                .Select(p => new Point(
                    (int)Math.Round((centroMm.X + p.Item1 * c - p.Item2 * s) * pixelsPorMm),
                    (int)Math.Round((centroMm.Y + p.Item1 * s + p.Item2 * c) * pixelsPorMm)))
                .ToArray();
        }

        Cv2.FillConvexPoly(imagem, Retangulo(larguraMm, alturaMm), corDaPeca);
        if (ladoDoFuroMm > 0)
            Cv2.FillConvexPoly(imagem, Retangulo(ladoDoFuroMm, ladoDoFuroMm), corDoFundo);
    }

    /// <summary>
    /// Simula a foto tirada de um ângulo: leva os cantos da imagem para dentro, em proporções
    /// diferentes em cada canto, e preenche a borda com <paramref name="corDoFundo"/>.
    /// </summary>
    public static Mat EmPerspectiva(Mat cena, Scalar corDoFundo)
    {
        var (origem, destino) = CantosDaPerspectiva(cena.Size());
        using var h0 = Cv2.GetPerspectiveTransform(origem, destino);
        var saida = new Mat();
        Cv2.WarpPerspective(cena, saida, h0, cena.Size(), InterpolationFlags.Linear, BorderTypes.Constant, corDoFundo);
        return saida;
    }

    /// <summary>Os cantos da cena e para onde <see cref="EmPerspectiva"/> os leva.</summary>
    public static (Point2f[] Origem, Point2f[] Destino) CantosDaPerspectiva(Size tamanho)
    {
        float w = tamanho.Width, h = tamanho.Height;
        var origem = new[] { new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h) };
        var destino = new[]
        {
            new Point2f(0.06f * w, 0.03f * h), new Point2f(0.97f * w, 0.08f * h),
            new Point2f(0.92f * w, 0.98f * h), new Point2f(0.02f * w, 0.93f * h),
        };
        return (origem, destino);
    }
}
