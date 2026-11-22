using OpenCvSharp;

namespace BuscaPorFoto.Foto;

public enum MetodoDeRecorte { Otsu, GrabCut }

public static class Recortador
{
    /// <summary>Lado máximo, em px, em que o GrabCut roda; a máscara volta ampliada ao tamanho original.</summary>
    public const int LadoMaximoDoGrabCut = 1000;

    /// <summary>Fração da imagem, em cada borda, que o GrabCut assume como fundo certo.</summary>
    public const double MargemDoGrabCut = 0.05;

    /// <summary>
    /// Máscara do tamanho da imagem, 255 no objeto que contém <paramref name="centro"/> (com os
    /// furos dele em 0), e vazia se nenhum o contém. É o objeto do centro, e não o maior, porque a
    /// peça é enquadrada no centro e pode estar sobre uma pilha (spec §4.2, §5.4): o maior pode ser
    /// a chapa de baixo. Quando <paramref name="cantosDoMarcador"/> é informado, o marcador é
    /// apagado antes do recorte, para não se juntar à peça que ele toca.
    /// </summary>
    public static Mat Recortar(Mat imagemBgr, Point2f centro, Point2f[]? cantosDoMarcador, MetodoDeRecorte metodo)
    {
        using var semMarcador = imagemBgr.Clone();
        if (cantosDoMarcador is not null)
            ApagarMarcador(semMarcador, cantosDoMarcador);

        using var bruta = metodo switch
        {
            MetodoDeRecorte.Otsu => PorOtsu(semMarcador),
            MetodoDeRecorte.GrabCut => PorGrabCut(semMarcador),
            _ => throw new ArgumentOutOfRangeException(nameof(metodo)),
        };
        return ObjetoDoCentroComFuros(bruta, centro);
    }

    /// <summary>Pinta o marcador, ampliado 20% em torno do centro, com a mediana da borda da imagem.</summary>
    private static void ApagarMarcador(Mat imagem, Point2f[] cantos)
    {
        var cx = cantos.Average(p => p.X);
        var cy = cantos.Average(p => p.Y);
        var ampliado = cantos
            .Select(p => new Point((int)Math.Round(cx + 1.2 * (p.X - cx)), (int)Math.Round(cy + 1.2 * (p.Y - cy))))
            .ToArray();
        Cv2.FillConvexPoly(imagem, ampliado, CorDaBorda(imagem));
    }

    private static Scalar CorDaBorda(Mat imagem)
    {
        // Largura e altura em variável: cada leitura de Mat.Width é uma chamada nativa, e o
        // analisador do OpenCvSharp (OCVS002) reprova o acesso dentro do laço.
        int largura = imagem.Width, altura = imagem.Height;
        var amostras = new List<Vec3b>();
        for (var x = 0; x < largura; x++)
        {
            amostras.Add(imagem.At<Vec3b>(0, x));
            amostras.Add(imagem.At<Vec3b>(altura - 1, x));
        }
        for (var y = 0; y < altura; y++)
        {
            amostras.Add(imagem.At<Vec3b>(y, 0));
            amostras.Add(imagem.At<Vec3b>(y, largura - 1));
        }
        byte Mediana(Func<Vec3b, byte> canal) => amostras.Select(canal).Order().ElementAt(amostras.Count / 2);
        return new Scalar(Mediana(v => v.Item0), Mediana(v => v.Item1), Mediana(v => v.Item2));
    }

    private static Mat PorOtsu(Mat imagemBgr)
    {
        using var cinza = Imagens.ParaCinza(imagemBgr);
        Cv2.GaussianBlur(cinza, cinza, new Size(5, 5), 0);
        var binaria = new Mat();
        Cv2.Threshold(cinza, binaria, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // O Otsu separa claro de escuro, não peça de fundo. O fundo é a classe que domina a borda.
        if (FracaoBrancaNaBorda(binaria) > 0.5)
            Cv2.BitwiseNot(binaria, binaria);

        using var elemento = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(binaria, binaria, MorphTypes.Open, elemento);
        Cv2.MorphologyEx(binaria, binaria, MorphTypes.Close, elemento);
        return binaria;
    }

    private static double FracaoBrancaNaBorda(Mat binaria)
    {
        int largura = binaria.Width, altura = binaria.Height;
        long brancos = 0, total = 0;
        for (var x = 0; x < largura; x++)
        {
            brancos += binaria.At<byte>(0, x) > 0 ? 1 : 0;
            brancos += binaria.At<byte>(altura - 1, x) > 0 ? 1 : 0;
            total += 2;
        }
        for (var y = 0; y < altura; y++)
        {
            brancos += binaria.At<byte>(y, 0) > 0 ? 1 : 0;
            brancos += binaria.At<byte>(y, largura - 1) > 0 ? 1 : 0;
            total += 2;
        }
        return (double)brancos / total;
    }

    private static Mat PorGrabCut(Mat imagemBgr)
    {
        var fator = Math.Min(1.0, (double)LadoMaximoDoGrabCut / Math.Max(imagemBgr.Width, imagemBgr.Height));
        using var reduzida = new Mat();
        Cv2.Resize(imagemBgr, reduzida, new Size(), fator, fator, InterpolationFlags.Area);

        var margemX = (int)(reduzida.Width * MargemDoGrabCut);
        var margemY = (int)(reduzida.Height * MargemDoGrabCut);
        var retangulo = new Rect(margemX, margemY, reduzida.Width - 2 * margemX, reduzida.Height - 2 * margemY);

        using var rotulos = new Mat(reduzida.Size(), MatType.CV_8UC1, Scalar.All(0));
        using var fundo = new Mat();
        using var frente = new Mat();
        Cv2.GrabCut(reduzida, rotulos, retangulo, fundo, frente, 5, GrabCutModes.InitWithRect);

        // GC_FGD = 1, GC_PR_FGD = 3: os dois têm o bit 0 ligado; fundo (0) e provável fundo (2) não.
        using var um = new Mat(rotulos.Size(), MatType.CV_8UC1, Scalar.All(1));
        using var bitZero = new Mat();
        Cv2.BitwiseAnd(rotulos, um, bitZero);
        using var pequena = new Mat();
        Cv2.Multiply(bitZero, Scalar.All(255), pequena);

        var cheia = new Mat();
        Cv2.Resize(pequena, cheia, imagemBgr.Size(), 0, 0, InterpolationFlags.Nearest);
        return cheia;
    }

    /// <summary>
    /// O menor contorno externo que contém o centro, ignorando os furos: o centro pode cair num furo
    /// da peça, e ela continua sendo a do centro. O menor, porque uma peça posta dentro do furo de
    /// outra também tem contorno externo, e o da peça de fora a envolve.
    /// </summary>
    private static Mat ObjetoDoCentroComFuros(Mat binaria, Point2f centro)
    {
        var saida = new Mat(binaria.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FindContours(binaria, out var contornos, out var hierarquia, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        var doCentro = Enumerable.Range(0, contornos.Length)
            .Where(i => hierarquia[i].Parent < 0 && Cv2.PointPolygonTest(contornos[i], centro, measureDist: false) >= 0)
            .ToList();
        if (doCentro.Count == 0) return saida;
        var escolhido = doCentro.MinBy(i => Cv2.ContourArea(contornos[i]));

        Cv2.DrawContours(saida, contornos, escolhido, Scalar.All(255), -1);
        for (var filho = hierarquia[escolhido].Child; filho >= 0; filho = hierarquia[filho].Next)
            Cv2.DrawContours(saida, contornos, filho, Scalar.All(0), -1);
        return saida;
    }
}
