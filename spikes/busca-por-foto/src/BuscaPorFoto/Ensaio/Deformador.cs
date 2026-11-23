using BuscaPorFoto.Execucao;
using OpenCvSharp;

namespace BuscaPorFoto.Ensaios;

/// <summary>
/// Os parâmetros da "foto" sintética do ensaio com dados públicos, fixados ANTES de qualquer execução (spec §9).
/// </summary>
/// <param name="InclinacaoComMarcadorGraus">Inclinação máxima da câmera com marcador: o resíduo depois da retificação.</param>
/// <param name="InclinacaoSemMarcadorGraus">Inclinação máxima da câmera sem marcador, que não corrige nada.</param>
/// <param name="FatorDeSerrilhado">Fração do tamanho a que a máscara é reduzida (INTER_AREA) antes de voltar (INTER_NEAREST).</param>
/// <param name="SigmaDoDesfoqueMm">Sigma do desfoque gaussiano, em milímetros, antes do limiar em 127.</param>
public sealed record ParametrosDeDeformacao(
    double InclinacaoComMarcadorGraus, double InclinacaoSemMarcadorGraus, double FatorDeSerrilhado,
    double SigmaDoDesfoqueMm, int Semente)
{
    public static ParametrosDeDeformacao Padrao { get; } = new(3, 15, 0.25, 0.75, 20261122);
}

/// <summary>
/// Faz de uma silhueta de referência a "foto" dela: rotação e espelho sorteados, inclinação da câmera num eixo
/// sorteado (perspectiva), borda serrilhada e desfoque. Determinístico: o gerador é
/// <c>new Random(Semente + indice)</c>. Com marcador a escala é a real; sem marcador, desconhecida.
/// </summary>
public static class Deformador
{
    /// <summary>Distância da câmera ao plano da peça, em múltiplos do lado da tela: perspectiva moderada.</summary>
    private const double DistanciaDaCameraEmLados = 3;

    public static Mascara Deformar(Mascara referencia, Condicao condicao, ParametrosDeDeformacao p, int indice)
    {
        if (condicao == Condicao.ComMarcador && referencia.MmPorPixel is null)
            throw new ArgumentException("A referência de uma foto com marcador precisa de escala.", nameof(referencia));
        var mmPorPixel = referencia.MmPorPixel ?? throw new ArgumentException("A referência precisa de escala.", nameof(referencia));

        var rng = new Random(p.Semente + indice);
        var angulo = rng.NextDouble() * 360;
        var espelhar = rng.Next(2) == 1;
        var eixoX = rng.Next(2) == 1;
        var maxInclinacao = condicao == Condicao.ComMarcador ? p.InclinacaoComMarcadorGraus : p.InclinacaoSemMarcadorGraus;
        var inclinacao = (rng.NextDouble() * 2 - 1) * maxInclinacao;

        var lado = (int)Math.Ceiling(Math.Sqrt(referencia.Pixels.Width * (double)referencia.Pixels.Width
                                               + referencia.Pixels.Height * (double)referencia.Pixels.Height)) + 8;
        using var tela = new Mat(lado, lado, MatType.CV_8UC1, Scalar.All(0));
        using (var destino = new Mat(tela, new Rect((lado - referencia.Pixels.Width) / 2, (lado - referencia.Pixels.Height) / 2,
                   referencia.Pixels.Width, referencia.Pixels.Height)))
            referencia.Pixels.CopyTo(destino);
        if (espelhar) Cv2.Flip(tela, tela, FlipMode.Y);

        using var girada = Girar(tela, angulo);
        using var inclinada = Inclinar(girada, eixoX, inclinacao);
        using var serrilhada = Serrilhar(inclinada, p.FatorDeSerrilhado);
        using var desfocada = Desfocar(serrilhada, p.SigmaDoDesfoqueMm / mmPorPixel);

        return Mascara.DeMascaraCheia(desfocada, condicao == Condicao.ComMarcador ? mmPorPixel : null)
               ?? throw new InvalidOperationException("A deformação apagou a máscara.");
    }

    private static Mat Girar(Mat tela, double graus)
    {
        using var afim = Cv2.GetRotationMatrix2D(new Point2f(tela.Width / 2f, tela.Height / 2f), graus, 1);
        var saida = new Mat();
        Cv2.WarpAffine(tela, saida, afim, tela.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
        Cv2.Threshold(saida, saida, 127, 255, ThresholdTypes.Binary);
        return saida;
    }

    /// <summary>Gira o plano da peça em torno de um eixo pelo centro e o projeta: o lado mais perto fica maior.</summary>
    private static Mat Inclinar(Mat tela, bool eixoX, double graus)
    {
        var meio = tela.Width / 2.0;
        var f = DistanciaDaCameraEmLados * tela.Width;
        var rad = graus * Math.PI / 180;
        Point2f Projetar(double x, double y)
        {
            double u = eixoX ? x : x * Math.Cos(rad), v = eixoX ? y * Math.Cos(rad) : y;
            var z = eixoX ? y * Math.Sin(rad) : x * Math.Sin(rad);
            var k = f / (f + z);
            return new Point2f((float)(meio + u * k), (float)(meio + v * k));
        }

        var origem = new[] { new Point2f(0, 0), new Point2f(tela.Width, 0), new Point2f(tela.Width, tela.Height), new Point2f(0, tela.Height) };
        var destino = origem.Select(c => Projetar(c.X - meio, c.Y - meio)).ToArray();
        using var h = Cv2.GetPerspectiveTransform(origem, destino);
        var saida = new Mat();
        Cv2.WarpPerspective(tela, saida, h, tela.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
        Cv2.Threshold(saida, saida, 127, 255, ThresholdTypes.Binary);
        return saida;
    }

    private static Mat Serrilhar(Mat tela, double fator)
    {
        using var pequena = new Mat();
        Cv2.Resize(tela, pequena, new Size(), fator, fator, InterpolationFlags.Area);
        Cv2.Threshold(pequena, pequena, 127, 255, ThresholdTypes.Binary);
        var saida = new Mat();
        Cv2.Resize(pequena, saida, tela.Size(), 0, 0, InterpolationFlags.Nearest);
        return saida;
    }

    private static Mat Desfocar(Mat tela, double sigmaEmPixels)
    {
        var saida = new Mat();
        Cv2.GaussianBlur(tela, saida, new Size(), sigmaEmPixels);
        Cv2.Threshold(saida, saida, 127, 255, ThresholdTypes.Binary);
        return saida;
    }
}
