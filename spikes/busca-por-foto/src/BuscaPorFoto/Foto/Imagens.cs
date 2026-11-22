using OpenCvSharp;

namespace BuscaPorFoto.Foto;

internal static class Imagens
{
    /// <summary>Cópia em tons de cinza; aceita imagem de 1 ou 3 canais.</summary>
    public static Mat ParaCinza(Mat imagem)
    {
        if (imagem.Channels() == 1) return imagem.Clone();
        var cinza = new Mat();
        Cv2.CvtColor(imagem, cinza, ColorConversionCodes.BGR2GRAY);
        return cinza;
    }
}
