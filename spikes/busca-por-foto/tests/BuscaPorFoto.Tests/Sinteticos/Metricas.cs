using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

public static class Metricas
{
    /// <summary>Interseção sobre união de duas máscaras de mesmo tamanho (pixel &gt; 0 é objeto).</summary>
    public static double IoU(Mat a, Mat b)
    {
        using var e = new Mat();
        using var ou = new Mat();
        Cv2.BitwiseAnd(a, b, e);
        Cv2.BitwiseOr(a, b, ou);
        return (double)Cv2.CountNonZero(e) / Cv2.CountNonZero(ou);
    }
}
