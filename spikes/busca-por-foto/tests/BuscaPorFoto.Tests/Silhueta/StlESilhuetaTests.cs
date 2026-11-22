using System.Numerics;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Stl;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Silhueta;

public class LeitorDeStlTests
{
    [Fact]
    public void Le_binario_mesmo_com_cabecalho_comecando_por_solid()
    {
        var malha = MalhaSintetica.Caixa(10, 20, 30);

        var lida = LeitorDeStl.Ler(MalhaSintetica.StlBinario(malha, cabecalho: "solid peca"));

        Assert.Equal(malha, lida);
    }

    [Fact]
    public void Le_ascii()
    {
        var malha = MalhaSintetica.Caixa(10, 20, 30);

        var lida = LeitorDeStl.Ler(MalhaSintetica.StlAscii(malha));

        Assert.Equal(malha, lida);
    }

    [Fact]
    public void Recusa_arquivo_que_nao_e_stl()
    {
        Assert.Throws<FormatException>(() => LeitorDeStl.Ler("isto não é um STL"u8.ToArray()));
    }
}

public class ProjetorDeSilhuetaTests
{
    private static (double MaiorMm, double MenorMm, double AreaMm2) Medir(Mascara m)
    {
        var mm = m.MmPorPixel!.Value;
        return (Math.Max(m.Pixels.Width, m.Pixels.Height) * mm,
                Math.Min(m.Pixels.Width, m.Pixels.Height) * mm,
                Cv2.CountNonZero(m.Pixels) * mm * mm);
    }

    [Fact]
    public void Chapa_gera_tres_vistas_e_a_primeira_tem_mm_reais_e_o_furo_vazado()
    {
        var vistas = ProjetorDeSilhueta.Projetar(MalhaSintetica.ChapaComFuro(100, 50, 3, 20));

        Assert.Equal(3, vistas.Count);
        var primeira = vistas[0];
        var (maior, menor, area) = Medir(primeira);
        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
        Assert.Equal(0, primeira.Pixels.At<byte>(primeira.Pixels.Height / 2, primeira.Pixels.Width / 2));
    }

    [Fact]
    public void Chapa_em_orientacao_qualquer_da_a_mesma_primeira_vista()
    {
        var girada = MalhaSintetica.Transformar(
            MalhaSintetica.ChapaComFuro(100, 50, 3, 20),
            Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f) * Matrix4x4.CreateTranslation(500, -200, 80));

        var (maior, menor, area) = Medir(ProjetorDeSilhueta.Projetar(girada)[0]);

        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
    }

    [Fact]
    public void Caixa_gera_as_tres_vistas_principais()
    {
        var vistas = ProjetorDeSilhueta.Projetar(MalhaSintetica.Caixa(100, 80, 60));

        Assert.Equal(3, vistas.Count);
        var areas = vistas.Select(v => Medir(v).AreaMm2).Order().ToArray();
        Assert.InRange(areas[0], 4800 * 0.97, 4800 * 1.04);
        Assert.InRange(areas[1], 6000 * 0.97, 6000 * 1.04);
        Assert.InRange(areas[2], 8000 * 0.97, 8000 * 1.04);
    }

    [Fact]
    public void Malha_vazia_e_recusada()
    {
        Assert.Throws<ArgumentException>(() => ProjetorDeSilhueta.Projetar(new List<Triangulo>()));
    }
}
