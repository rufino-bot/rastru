using BuscaPorFoto.Marcador;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <summary>Desenha cenas sintéticas em milímetros sobre uma imagem, numa escala conhecida.</summary>
public static partial class CenaSintetica
{
    /// <summary>
    /// Desenha o marcador com o canto superior esquerdo em <paramref name="origemMm"/>; <paramref name="ladoMm"/> é o
    /// lado que a régua mediria (a impressão que saiu de 98 mm, por exemplo).
    /// </summary>
    public static void DesenharMarcador(Mat imagem, Point2d origemMm, double pixelsPorMm, double ladoMm = MarcadorAruco.LadoMm)
    {
        var grade = MarcadorAruco.Grade();
        var celulaPx = ladoMm / MarcadorAruco.CelulasPorLado * pixelsPorMm;
        var x0 = origemMm.X * pixelsPorMm;
        var y0 = origemMm.Y * pixelsPorMm;
        for (var linha = 0; linha < MarcadorAruco.CelulasPorLado; linha++)
            for (var coluna = 0; coluna < MarcadorAruco.CelulasPorLado; coluna++)
            {
                var cor = grade[linha, coluna] ? Scalar.All(0) : Scalar.All(255);
                var r = new Rect(
                    (int)Math.Round(x0 + coluna * celulaPx), (int)Math.Round(y0 + linha * celulaPx),
                    (int)Math.Ceiling(celulaPx), (int)Math.Ceiling(celulaPx));
                Cv2.Rectangle(imagem, r, cor, -1);
            }
    }
}
