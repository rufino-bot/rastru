using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Foto;

public class RetificadorTests
{
    private static readonly Scalar Fundo = Scalar.All(200);
    private const double PxPorMmDaCena = 4;

    private static Mat Cena(bool comMarcador, double ladoDoMarcadorMm = 100)
    {
        var cena = new Mat((int)(300 * PxPorMmDaCena), (int)(400 * PxPorMmDaCena), MatType.CV_8UC3, Fundo);
        if (comMarcador)
            CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMmDaCena, ladoDoMarcadorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(260, 180), 160, 80, 0, anguloGraus: 0,
            PxPorMmDaCena, Scalar.All(40), Fundo);
        using (cena)
            return CenaSintetica.EmPerspectiva(cena, Fundo);
    }

    /// <summary>Maior lado, em mm, da chapa (o maior contorno escuro fora da caixa do marcador) na foto retificada.</summary>
    private static double MaiorLadoDaChapaMm(FotoRetificada retificada)
    {
        using var cinza = new Mat();
        Cv2.CvtColor(retificada.Imagem, cinza, ColorConversionCodes.BGR2GRAY);
        using var escura = new Mat();
        Cv2.Threshold(cinza, escura, 100, 255, ThresholdTypes.BinaryInv);
        Cv2.FindContours(escura, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var caixaDoMarcador = Cv2.BoundingRect(retificada.CantosDoMarcador.Select(p => new Point(p.X, p.Y)));
        var chapa = contornos
            .Where(c => (Cv2.BoundingRect(c) & caixaDoMarcador).Width == 0)
            .MaxBy(c => Cv2.ContourArea(c))!;
        var retangulo = Cv2.MinAreaRect(chapa);
        return Math.Max(retangulo.Size.Width, retangulo.Size.Height) * retificada.MmPorPixel;
    }

    [Fact]
    public void Devolve_a_peca_em_milimetros_reais_apesar_da_perspectiva()
    {
        using var foto = Cena(comMarcador: true);

        using var retificada = Retificador.Retificar(foto);

        Assert.NotNull(retificada);
        using var cinza = new Mat();
        Cv2.CvtColor(retificada.Imagem, cinza, ColorConversionCodes.BGR2GRAY);
        using var escura = new Mat();
        Cv2.Threshold(cinza, escura, 100, 255, ThresholdTypes.BinaryInv);
        Cv2.FindContours(escura, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // A chapa é o maior contorno escuro que não toca a caixa do marcador.
        var caixaDoMarcador = Cv2.BoundingRect(retificada.CantosDoMarcador.Select(p => new Point(p.X, p.Y)));
        var chapa = contornos
            .Where(c => (Cv2.BoundingRect(c) & caixaDoMarcador).Width == 0)
            .MaxBy(c => Cv2.ContourArea(c))!;
        var retangulo = Cv2.MinAreaRect(chapa);
        var maiorMm = Math.Max(retangulo.Size.Width, retangulo.Size.Height) * retificada.MmPorPixel;
        var menorMm = Math.Min(retangulo.Size.Width, retangulo.Size.Height) * retificada.MmPorPixel;

        Assert.InRange(maiorMm, 160 * 0.97, 160 * 1.03);
        Assert.InRange(menorMm, 80 * 0.97, 80 * 1.03);
    }

    [Fact]
    public void O_lado_medido_do_marcador_e_a_escala_da_foto()
    {
        // Um marcador que saiu da impressora com 98 mm. Com o lado medido, a chapa de 160 mm sai com 160 mm;
        // com o nominal (100), sai ~2% maior — o erro que a medida na régua existe para evitar (spec §4.2).
        using var foto = Cena(comMarcador: true, ladoDoMarcadorMm: 98);

        using var comAMedida = Retificador.Retificar(foto, 98)!;
        using var comONominal = Retificador.Retificar(foto)!;

        var medido = MaiorLadoDaChapaMm(comAMedida);
        var nominal = MaiorLadoDaChapaMm(comONominal);
        Assert.InRange(medido, 160 * 0.99, 160 * 1.01);
        Assert.InRange(nominal, 160 * 100.0 / 98 * 0.99, 160 * 100.0 / 98 * 1.01);
        Assert.True(nominal > 160 * 1.015, $"com 100 a chapa sai com {nominal:F1} mm: devia sobrar ~2%");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Lado_do_marcador_que_nao_e_positivo_e_recusado(double lado)
    {
        using var foto = Cena(comMarcador: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => Retificador.Retificar(foto, lado));
    }

    [Fact]
    public void Os_cantos_devolvidos_sao_os_do_marcador_na_saida()
    {
        using var foto = Cena(comMarcador: true);

        using var retificada = Retificador.Retificar(foto)!;

        var c = retificada.CantosDoMarcador;
        var ladoPx = 100 * Retificador.PixelsPorMm;
        Assert.Equal(ladoPx, c[1].X - c[0].X, precision: 1);
        Assert.Equal(ladoPx, c[3].Y - c[0].Y, precision: 1);
        // O centro do marcador numa saída retificada é uma célula da grade: preto ou branco puro,
        // nunca o cinza do fundo.
        var centro = retificada.Imagem.At<Vec3b>((int)(c[0].Y + ladoPx / 2), (int)(c[0].X + ladoPx / 2));
        Assert.NotEqual(200, centro.Item0);
    }

    [Fact]
    public void O_centro_da_foto_vai_para_o_ponto_da_cena_que_ele_mostra()
    {
        using var foto = Cena(comMarcador: true);
        // Que ponto da cena, em mm, aparece no centro da foto: o inverso da perspectiva simulada.
        var (origem, destino) = CenaSintetica.CantosDaPerspectiva(foto.Size());
        using var daFotoParaACena = Cv2.GetPerspectiveTransform(destino, origem);
        var naCenaPx = Cv2.PerspectiveTransform(new[] { new Point2f(foto.Width / 2f, foto.Height / 2f) }, daFotoParaACena)[0];
        var naCenaMm = new Point2d(naCenaPx.X / PxPorMmDaCena, naCenaPx.Y / PxPorMmDaCena);

        using var retificada = Retificador.Retificar(foto)!;

        // Na saída, o canto do marcador (posto em 20, 20 mm na cena) está em CantosDoMarcador[0].
        var c0 = retificada.CantosDoMarcador[0];
        var esperadoX = c0.X + (naCenaMm.X - 20) * Retificador.PixelsPorMm;
        var esperadoY = c0.Y + (naCenaMm.Y - 20) * Retificador.PixelsPorMm;
        Assert.InRange(retificada.CentroDaFoto.X, esperadoX - 2, esperadoX + 2);
        Assert.InRange(retificada.CentroDaFoto.Y, esperadoY - 2, esperadoY + 2);
    }

    [Fact]
    public void Sem_marcador_devolve_nulo()
    {
        using var foto = Cena(comMarcador: false);

        Assert.Null(Retificador.Retificar(foto));
    }
}
