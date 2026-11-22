using OpenCvSharp;

namespace BuscaPorFoto.Foto;

/// <summary>
/// A imagem que o humano olha para classificar o recorte como ok ou falho: a foto com a máscara
/// pintada de verde por cima e o contorno em vermelho.
/// </summary>
public static class Sobreposicao
{
    public static Mat Gerar(Mat imagemBgr, Mat mascaraCheia)
    {
        var saida = imagemBgr.Clone();
        using var verde = new Mat(imagemBgr.Size(), MatType.CV_8UC3, new Scalar(0, 255, 0));
        using var misturada = new Mat();
        Cv2.AddWeighted(imagemBgr, 0.5, verde, 0.5, 0, misturada);
        misturada.CopyTo(saida, mascaraCheia);

        Cv2.FindContours(mascaraCheia, out var contornos, out _, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
        Cv2.DrawContours(saida, contornos, -1, new Scalar(0, 0, 255), Math.Max(2, imagemBgr.Width / 400));
        return saida;
    }
}
