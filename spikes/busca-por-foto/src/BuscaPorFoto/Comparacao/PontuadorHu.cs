using OpenCvSharp;

namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Momentos de Hu do contorno EXTERNO — a linha de base que o roadmap cita. Invariante a rotação,
/// escala e espelho (o I1 compara logaritmos dos módulos), e cego aos furos por construção.
/// Devolve a distância com sinal trocado, para que maior seja melhor, como no IoU.
/// </summary>
public static class PontuadorHu
{
    public static double Pontuar(Mascara foto, Mascara candidato)
    {
        using var a = SoOContornoExterno(foto.Pixels);
        using var b = SoOContornoExterno(candidato.Pixels);
        return -Cv2.MatchShapes(a, b, ShapeMatchModes.I1);
    }

    private static Mat SoOContornoExterno(Mat mascara)
    {
        Cv2.FindContours(mascara, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        var preenchida = new Mat(mascara.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(preenchida, contornos, -1, Scalar.All(255), -1);
        return preenchida;
    }
}
