using OpenCvSharp;

namespace BuscaPorFoto.Dxf;

/// <summary>
/// A chapa inteira numa imagem só, para o usuário apontar o alvo pelo número: fundo branco, peças em
/// cinza claro com contorno preto, o número no centróide e o y invertido (a imagem fica como no CAD).
/// </summary>
public static class ImagemNumerada
{
    public const int LadoMaximo = 2000;
    public const int Margem = 20;

    /// <summary>Pixels por mm para o maior lado do conjunto, em mm, caber em <see cref="LadoMaximo"/> (no máximo 1/MmPorPixel).</summary>
    public static double Escala(double maiorLadoMm)
    {
        var util = LadoMaximo - 2 * Margem - 2;
        return Math.Min(util / Math.Max(maiorLadoMm, 1e-6), 1 / SeparadorDeNesting.MmPorPixel);
    }

    public static Mat Gerar(NestingSeparado nesting)
    {
        if (nesting.Pecas.Count == 0)
            throw new ArgumentException("Nenhuma peça para numerar.", nameof(nesting));

        var minX = nesting.Pecas.Min(p => p.CaixaMm.X);
        var minY = nesting.Pecas.Min(p => p.CaixaMm.Y);
        var maxX = nesting.Pecas.Max(p => p.CaixaMm.X + p.CaixaMm.Width);
        var maxY = nesting.Pecas.Max(p => p.CaixaMm.Y + p.CaixaMm.Height);
        var escala = Escala(Math.Max(maxX - minX, maxY - minY));
        var largura = (int)Math.Ceiling((maxX - minX) * escala) + 2 * Margem + 1;
        var altura = (int)Math.Ceiling((maxY - minY) * escala) + 2 * Margem + 1;

        var imagem = new Mat(altura, largura, MatType.CV_8UC3, new Scalar(255, 255, 255));
        foreach (var peca in nesting.Pecas)
        {
            var x0 = Margem + (int)Math.Round((peca.CaixaMm.X - minX) * escala);
            var y0 = Margem + (int)Math.Round((maxY - (peca.CaixaMm.Y + peca.CaixaMm.Height)) * escala);
            var w = Math.Min(Math.Max(1, (int)Math.Round(peca.CaixaMm.Width * escala)), largura - x0);
            var h = Math.Min(Math.Max(1, (int)Math.Round(peca.CaixaMm.Height * escala)), altura - y0);

            using var mascara = new Mat();
            Cv2.Resize(peca.Silhueta.Pixels, mascara, new Size(w, h), 0, 0, InterpolationFlags.Nearest);
            using var regiao = new Mat(imagem, new Rect(x0, y0, w, h));
            regiao.SetTo(new Scalar(210, 210, 210), mascara);

            using var copia = mascara.Clone();
            Cv2.FindContours(copia, out var contornos, out _, RetrievalModes.List, ContourApproximationModes.ApproxNone);
            Cv2.DrawContours(regiao, contornos, -1, new Scalar(0, 0, 0), 1);

            var m = Cv2.Moments(mascara, true);
            if (m.M00 <= 0) continue;
            var texto = peca.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tamanho = Cv2.GetTextSize(texto, HersheyFonts.HersheySimplex, 0.8, 2, out _);
            var centro = new Point(
                x0 + (int)Math.Round(m.M10 / m.M00) - tamanho.Width / 2,
                y0 + (int)Math.Round(m.M01 / m.M00) + tamanho.Height / 2);
            Cv2.PutText(imagem, texto, centro, HersheyFonts.HersheySimplex, 0.8, new Scalar(0, 0, 0), 2);
        }
        return imagem;
    }
}
