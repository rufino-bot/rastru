using OpenCvSharp;

namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Sobreposição de máscaras numa grade comum, com busca de rotação e espelho. Maior é melhor,
/// de 0 a 1.
/// </summary>
public static class PontuadorIoU
{
    public const int LadoDaGrade = 256;
    public const int PassoDeRotacaoGraus = 5;

    /// <summary>Passo do refino em volta do melhor ângulo grosso, de cada lado, até um passo grosso.</summary>
    public const double PassoDoRefinoGraus = 0.5;

    /// <summary>
    /// Sem escala, as duas máscaras são normalizadas para esta fração da área da grade.
    /// </summary>
    public const double FracaoDeAreaSemEscala = 0.2;

    /// <param name="usarEscala">
    /// Verdadeiro: cada máscara entra no tamanho real (exige <see cref="Mascara.MmPorPixel"/> nas
    /// duas), e diferença de tamanho derruba a pontuação. Falso: as duas são levadas à mesma área.
    /// </param>
    public static double Pontuar(Mascara foto, Mascara candidato, bool usarEscala)
    {
        var (escalaFoto, escalaCandidato) = Escalas(foto, candidato, usarEscala);

        using var gradeCandidato = NaGrade(candidato.Pixels, escalaCandidato, 0, espelhar: false);
        using var fotoReduzida = Redimensionar(foto.Pixels, escalaFoto);
        var centroide = Centroide(fotoReduzida);

        var melhor = 0.0;
        foreach (var espelhar in new[] { false, true })
        {
            var (melhorAqui, anguloAqui) = (0.0, 0.0);
            for (var angulo = 0; angulo < 360; angulo += PassoDeRotacaoGraus)
            {
                using var gradeFoto = Girar(fotoReduzida, centroide, angulo, espelhar);
                var iou = IoU(gradeFoto, gradeCandidato);
                if (iou > melhorAqui) (melhorAqui, anguloAqui) = (iou, angulo);
            }
            // Refino em volta do melhor ângulo grosso: numa peça longa e estreita, meio passo de
            // rotação desloca as pontas mais do que a largura dela.
            for (var delta = -PassoDeRotacaoGraus + PassoDoRefinoGraus; delta < PassoDeRotacaoGraus; delta += PassoDoRefinoGraus)
            {
                using var gradeFoto = Girar(fotoReduzida, centroide, anguloAqui + delta, espelhar);
                melhorAqui = Math.Max(melhorAqui, IoU(gradeFoto, gradeCandidato));
            }
            melhor = Math.Max(melhor, melhorAqui);
        }
        return melhor;
    }

    /// <summary>
    /// Fator pixel-da-máscara → pixel-da-grade de cada uma. A restrição é que as duas caibam na
    /// grade em QUALQUER rotação: o raio (maior distância do centróide a um canto da caixa) vezes a
    /// escala não passa de metade do lado.
    /// </summary>
    private static (double Foto, double Candidato) Escalas(Mascara foto, Mascara candidato, bool usarEscala)
    {
        var raioMaximo = LadoDaGrade / 2.0 * 0.98;
        double sFoto, sCandidato;
        if (usarEscala)
        {
            if (foto.MmPorPixel is not { } mmFoto || candidato.MmPorPixel is not { } mmCandidato)
                throw new ArgumentException("Pontuar com escala exige MmPorPixel nas duas máscaras.");
            var raioMm = Math.Max(Raio(foto.Pixels) * mmFoto, Raio(candidato.Pixels) * mmCandidato);
            var mmPorCelula = raioMm / raioMaximo;
            sFoto = mmFoto / mmPorCelula;
            sCandidato = mmCandidato / mmPorCelula;
        }
        else
        {
            var areaAlvo = FracaoDeAreaSemEscala * LadoDaGrade * LadoDaGrade;
            sFoto = Math.Sqrt(areaAlvo / Cv2.CountNonZero(foto.Pixels));
            sCandidato = Math.Sqrt(areaAlvo / Cv2.CountNonZero(candidato.Pixels));
            var k = Math.Min(1.0, raioMaximo / Math.Max(Raio(foto.Pixels) * sFoto, Raio(candidato.Pixels) * sCandidato));
            sFoto *= k;
            sCandidato *= k;
        }
        return (sFoto, sCandidato);
    }

    private static double Raio(Mat mascara)
    {
        var c = Centroide(mascara);
        int w = mascara.Width, h = mascara.Height;
        return new[] { (0.0, 0.0), (w, 0.0), (w, (double)h), (0.0, (double)h) }
            .Max(p => Math.Sqrt((p.Item1 - c.X) * (p.Item1 - c.X) + (p.Item2 - c.Y) * (p.Item2 - c.Y)));
    }

    private static Point2d Centroide(Mat mascara)
    {
        var m = Cv2.Moments(mascara, binaryImage: true);
        return new Point2d(m.M10 / m.M00, m.M01 / m.M00);
    }

    /// <summary>Reduz com INTER_AREA antes de girar: amostrar uma máscara grande direto na grade de 128 px perde as partes finas.</summary>
    private static Mat Redimensionar(Mat mascara, double escala)
    {
        var tamanho = new Size(Math.Max(1, (int)Math.Round(mascara.Width * escala)), Math.Max(1, (int)Math.Round(mascara.Height * escala)));
        var saida = new Mat();
        Cv2.Resize(mascara, saida, tamanho, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(saida, saida, 127, 255, ThresholdTypes.Binary);
        return saida;
    }

    private static Mat NaGrade(Mat mascara, double escala, int angulo, bool espelhar)
    {
        using var reduzida = Redimensionar(mascara, escala);
        return Girar(reduzida, Centroide(reduzida), angulo, espelhar);
    }

    /// <summary>Leva o centróide ao centro da grade, espelha (em x) se pedido, e gira.</summary>
    private static Mat Girar(Mat mascara, Point2d centroide, double angulo, bool espelhar)
    {
        var rad = angulo * Math.PI / 180;
        double c = Math.Cos(rad), s = Math.Sin(rad), mx = espelhar ? -1 : 1;
        double meio = LadoDaGrade / 2.0;
        using var afim = new Mat(2, 3, MatType.CV_64FC1);
        afim.Set(0, 0, c * mx); afim.Set(0, 1, -s); afim.Set(0, 2, meio - (c * mx * centroide.X - s * centroide.Y));
        afim.Set(1, 0, s * mx); afim.Set(1, 1, c); afim.Set(1, 2, meio - (s * mx * centroide.X + c * centroide.Y));

        var grade = new Mat();
        Cv2.WarpAffine(mascara, grade, afim, new Size(LadoDaGrade, LadoDaGrade), InterpolationFlags.Nearest,
            BorderTypes.Constant, Scalar.All(0));
        return grade;
    }

    private static double IoU(Mat a, Mat b)
    {
        using var e = new Mat();
        using var ou = new Mat();
        Cv2.BitwiseAnd(a, b, e);
        Cv2.BitwiseOr(a, b, ou);
        var uniao = Cv2.CountNonZero(ou);
        return uniao == 0 ? 0 : (double)Cv2.CountNonZero(e) / uniao;
    }
}
