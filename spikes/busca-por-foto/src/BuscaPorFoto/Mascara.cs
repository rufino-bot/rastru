using OpenCvSharp;

namespace BuscaPorFoto;

/// <summary>
/// Uma forma binária recortada justa no objeto: <see cref="Pixels"/> é CV_8UC1 com 255 no objeto
/// e 0 fora (furos inclusos). <see cref="MmPorPixel"/> é nulo quando a escala é desconhecida —
/// foto sem marcador.
///
/// É a ÚNICA coisa que os pontuadores recebem. Nome de arquivo, código da peça e ordem de
/// enumeração não passam por aqui, e é isso que impede vazamento para o ranking.
/// </summary>
public sealed class Mascara : IDisposable
{
    public Mat Pixels { get; }
    public double? MmPorPixel { get; }

    public Mascara(Mat pixels, double? mmPorPixel)
    {
        if (pixels.Type() != MatType.CV_8UC1)
            throw new ArgumentException("A máscara tem de ser CV_8UC1.", nameof(pixels));
        if (Cv2.CountNonZero(pixels) == 0)
            throw new ArgumentException("A máscara está vazia.", nameof(pixels));
        Pixels = pixels;
        MmPorPixel = mmPorPixel;
    }

    /// <summary>Recorta uma máscara do tamanho da imagem até a caixa do objeto; nulo se vazia.</summary>
    public static Mascara? DeMascaraCheia(Mat cheia, double? mmPorPixel)
    {
        if (Cv2.CountNonZero(cheia) == 0) return null;
        var caixa = Cv2.BoundingRect(cheia);
        return new Mascara(new Mat(cheia, caixa).Clone(), mmPorPixel);
    }

    /// <summary>Reduz (nunca amplia) até o maior lado caber em <paramref name="ladoMaximo"/>, mantendo a escala coerente.</summary>
    public Mascara ReduzidaAte(int ladoMaximo)
    {
        var maior = Math.Max(Pixels.Width, Pixels.Height);
        if (maior <= ladoMaximo) return new Mascara(Pixels.Clone(), MmPorPixel);

        var fator = (double)ladoMaximo / maior;
        var reduzida = new Mat();
        Cv2.Resize(Pixels, reduzida, new Size(), fator, fator, InterpolationFlags.Area);
        Cv2.Threshold(reduzida, reduzida, 127, 255, ThresholdTypes.Binary);
        return new Mascara(reduzida, MmPorPixel / fator);
    }

    public void Dispose() => Pixels.Dispose();
}
