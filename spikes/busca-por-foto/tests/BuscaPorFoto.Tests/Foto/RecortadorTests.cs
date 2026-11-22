using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Foto;

public class RecortadorTests
{
    private const double PxPorMm = 2;

    /// <summary>O centro da cena de 400 × 300 mm, em pixels: (200, 150) mm.</summary>
    private static readonly Point2f Centro = new((float)(200 * PxPorMm), (float)(150 * PxPorMm));

    private static Mat Vazia(Scalar fundo) => new((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, fundo);

    /// <summary>Cena com uma chapa com furo, que contém o centro da cena, e a máscara-verdade dela.</summary>
    private static (Mat Cena, Mat Verdade) Cena(Scalar peca, Scalar fundo, bool comRuido = false)
    {
        var cena = Vazia(fundo);
        var verdade = new Mat(cena.Size(), MatType.CV_8UC1, Scalar.All(0));
        CenaSintetica.DesenharChapa(cena, new Point2d(250, 170), 180, 90, 40, 25, PxPorMm, peca, fundo);
        CenaSintetica.DesenharChapa(verdade, new Point2d(250, 170), 180, 90, 40, 25, PxPorMm, Scalar.All(255), Scalar.All(0));
        if (comRuido)
        {
            // Ruído gerado já com sinal: num CV_8U a metade negativa seria cortada em zero.
            using var ruido = new Mat(cena.Size(), MatType.CV_16SC3);
            Cv2.Randn(ruido, Scalar.All(0), Scalar.All(12));
            using var cena16 = new Mat();
            cena.ConvertTo(cena16, MatType.CV_16SC3);
            Cv2.Add(cena16, ruido, cena16);
            cena16.ConvertTo(cena, MatType.CV_8UC3);
            // Respingos pequenos, que não podem virar "a peça".
            Cv2.Circle(cena, new Point(60, 500), 6, peca, -1);
            Cv2.Circle(cena, new Point(700, 60), 5, peca, -1);
        }
        return (cena, verdade);
    }

    [Fact]
    public void Otsu_recorta_peca_escura_em_fundo_claro_com_o_furo()
    {
        var (cena, verdade) = Cena(Scalar.All(50), Scalar.All(210), comRuido: true);
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
            Assert.Equal(0, mascara.At<byte>((int)(170 * PxPorMm), (int)(250 * PxPorMm)));
        }
    }

    [Fact]
    public void Otsu_recorta_peca_clara_em_fundo_escuro()
    {
        var (cena, verdade) = Cena(Scalar.All(220), Scalar.All(40));
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
        }
    }

    [Fact]
    public void Fica_com_a_peca_do_centro_e_nao_com_a_maior()
    {
        // A chapa grande, de 150 × 100 mm, fica longe do centro; a pequena, de 50 × 30, está nele.
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(90, 80), 150, 100, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 50, 30, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>((int)(150 * PxPorMm), (int)(200 * PxPorMm)));
        Assert.Equal(0, mascara.At<byte>((int)(80 * PxPorMm), (int)(90 * PxPorMm)));
    }

    [Fact]
    public void Peca_com_o_furo_no_centro_ainda_e_a_do_centro()
    {
        // O furo de 40 mm está centrado no centro da cena: o centro cai no furo, não na chapa.
        using var cena = Vazia(Scalar.All(210));
        using var verdade = new Mat(cena.Size(), MatType.CV_8UC1, Scalar.All(0));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 180, 90, 40, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(verdade, new Point2d(200, 150), 180, 90, 40, 0, PxPorMm, Scalar.All(255), Scalar.All(0));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
        Assert.Equal(0, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
    }

    [Fact]
    public void Peca_dentro_do_furo_de_outra_e_a_do_centro()
    {
        // Chapa de 300 × 200 mm com furo de 120 mm, e uma chapa de 40 × 30 dentro do furo, no centro.
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 300, 200, 120, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 40, 30, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
        Assert.Equal(0, mascara.At<byte>((int)(70 * PxPorMm), (int)(70 * PxPorMm)));
    }

    [Fact]
    public void Sem_objeto_no_centro_a_mascara_e_vazia()
    {
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(60, 60), 80, 50, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Null(BuscaPorFoto.Mascara.DeMascaraCheia(mascara, null));
    }

    /// <summary>
    /// O marcador de 100 mm encostado na chapa do centro: os dois viram um objeto só se o marcador
    /// não for apagado. O marcador ocupa x de 20 a 120 mm; a chapa, de 120 a 280.
    /// </summary>
    private static Mat CenaComMarcadorEncostado()
    {
        var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharMarcador(cena, new Point2d(20, 100), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 160, 60, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        return cena;
    }

    private static Point2f[] CantosDoMarcadorEncostado()
    {
        var lado = (float)(100 * PxPorMm);
        float x = (float)(20 * PxPorMm), y = (float)(100 * PxPorMm);
        return [new(x, y), new(x + lado, y), new(x + lado, y + lado), new(x, y + lado)];
    }

    /// <summary>Um ponto na célula de borda do marcador, que é sempre preta.</summary>
    private static (int Linha, int Coluna) PontoNaBordaDoMarcador => ((int)(105 * PxPorMm), (int)(25 * PxPorMm));

    [Fact]
    public void O_marcador_informado_nao_entra_na_mascara()
    {
        using var cena = CenaComMarcadorEncostado();

        using var mascara = Recortador.Recortar(cena, Centro, CantosDoMarcadorEncostado(), MetodoDeRecorte.Otsu);

        Assert.Equal(0, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
        Assert.Equal(255, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
    }

    [Fact]
    public void Sem_os_cantos_o_marcador_encostado_entra_na_mascara()
    {
        // Par negativo de O_marcador_informado_nao_entra_na_mascara, na mesma cena: prova que é o
        // apagamento que tira o marcador.
        using var cena = CenaComMarcadorEncostado();

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
    }

    [Fact]
    public void GrabCut_recorta_peca_com_ruido()
    {
        var (cena, verdade) = Cena(Scalar.All(50), Scalar.All(210), comRuido: true);
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.GrabCut);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.9);
        }
    }

    [Fact]
    public void Imagem_sem_objeto_da_mascara_vazia()
    {
        using var lisa = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(128));

        using var mascara = Recortador.Recortar(lisa, new Point2f(100, 100), null, MetodoDeRecorte.Otsu);

        Assert.Null(BuscaPorFoto.Mascara.DeMascaraCheia(mascara, null));
    }
}

public class SobreposicaoTests
{
    [Fact]
    public void Pinta_so_dentro_da_mascara()
    {
        using var imagem = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(128));
        using var mascara = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Rectangle(mascara, new Rect(30, 30, 40, 40), Scalar.All(255), -1);

        using var saida = Sobreposicao.Gerar(imagem, mascara);

        Assert.Equal(imagem.Size(), saida.Size());
        Assert.NotEqual(imagem.At<Vec3b>(50, 50), saida.At<Vec3b>(50, 50));
        Assert.Equal(imagem.At<Vec3b>(5, 5), saida.At<Vec3b>(5, 5));
    }
}
