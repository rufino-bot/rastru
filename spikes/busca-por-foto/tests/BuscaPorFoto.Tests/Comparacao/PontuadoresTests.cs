using BuscaPorFoto.Comparacao;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Comparacao;

public class PontuadoresTests
{
    /// <summary>Máscara de um polígono dado em mm, girado, numa escala de <paramref name="mmPorPixel"/>.</summary>
    private static Mascara Poligono(IEnumerable<(double X, double Y)> verticesMm, double mmPorPixel = 0.5,
        double anguloGraus = 0, bool espelhar = false, (double X, double Y, double Lado)? furo = null)
    {
        var rad = anguloGraus * Math.PI / 180;
        Point Converter((double X, double Y) p)
        {
            var x = espelhar ? -p.X : p.X;
            return new Point(
                (int)Math.Round((1000 + x * Math.Cos(rad) - p.Y * Math.Sin(rad)) / mmPorPixel),
                (int)Math.Round((1000 + x * Math.Sin(rad) + p.Y * Math.Cos(rad)) / mmPorPixel));
        }

        using var cheia = new Mat((int)(2000 / mmPorPixel), (int)(2000 / mmPorPixel), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(cheia, new[] { verticesMm.Select(Converter).ToArray() }, Scalar.All(255));
        if (furo is { } f)
        {
            var h = f.Lado / 2;
            Cv2.FillPoly(cheia, new[] { new[] { (f.X - h, f.Y - h), (f.X + h, f.Y - h), (f.X + h, f.Y + h), (f.X - h, f.Y + h) }.Select(Converter).ToArray() }, Scalar.All(0));
        }
        return Mascara.DeMascaraCheia(cheia, mmPorPixel)!;
    }

    private static (double, double)[] Retangulo(double l, double a) => [(0, 0), (l, 0), (l, a), (0, a)];

    /// <summary>Um "L" assimétrico: o espelho dele não coincide com nenhuma rotação dele.</summary>
    private static (double, double)[] Ele() => [(0, 0), (120, 0), (120, 25), (30, 25), (30, 70), (0, 70)];

    [Fact]
    public void IoU_reconhece_a_mesma_forma_girada()
    {
        using var a = Poligono(Ele());
        using var b = Poligono(Ele(), anguloGraus: 37);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void IoU_reconhece_a_mesma_tira_fina_girada_meio_passo()
    {
        // 2,5° é o pior caso da busca grossa de 5 em 5°: numa tira de 300 × 8 mm, esse desvio
        // desloca as pontas mais do que a largura dela. Só o refino em volta do melhor ângulo a acha.
        using var a = Poligono(Retangulo(300, 8));
        using var b = Poligono(Retangulo(300, 8), anguloGraus: 2.5);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.8);
    }

    [Fact]
    public void IoU_reconhece_a_mesma_forma_espelhada()
    {
        using var a = Poligono(Ele());
        using var b = Poligono(Ele(), anguloGraus: 90, espelhar: true);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void Sem_escala_retangulos_proporcionais_empatam_e_com_escala_nao()
    {
        using var pequeno = Poligono(Retangulo(100, 50));
        using var grande = Poligono(Retangulo(200, 100));

        Assert.True(PontuadorIoU.Pontuar(pequeno, grande, usarEscala: false) >= 0.9);
        Assert.True(PontuadorIoU.Pontuar(pequeno, grande, usarEscala: true) <= 0.3);
    }

    [Fact]
    public void Com_escala_vale_mesmo_quando_as_duas_mascaras_tem_mm_por_pixel_diferentes()
    {
        using var foto = Poligono(Retangulo(100, 50), mmPorPixel: 0.5);
        using var candidato = Poligono(Retangulo(100, 50), mmPorPixel: 2);

        Assert.True(PontuadorIoU.Pontuar(foto, candidato, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void IoU_enxerga_o_furo_e_Hu_nao()
    {
        using var comFuro = Poligono(Retangulo(100, 50), furo: (50, 25, 35));
        using var semFuro = Poligono(Retangulo(100, 50));

        Assert.True(PontuadorIoU.Pontuar(comFuro, semFuro, usarEscala: true) <= 0.8);
        Assert.True(PontuadorHu.Pontuar(comFuro, semFuro) >= -1e-6);
    }

    [Fact]
    public void Os_dois_pontuadores_preferem_a_forma_certa_a_uma_diferente()
    {
        using var foto = Poligono(Ele(), anguloGraus: 20);
        using var certa = Poligono(Ele());
        using var errada = Poligono(Retangulo(120, 70));

        Assert.True(PontuadorIoU.Pontuar(foto, certa, true) > PontuadorIoU.Pontuar(foto, errada, true) + 0.1);
        Assert.True(PontuadorHu.Pontuar(foto, certa) > PontuadorHu.Pontuar(foto, errada));
    }

    [Fact]
    public void Com_escala_exige_mm_por_pixel_nas_duas()
    {
        using var semEscala = Mascara.DeMascaraCheia(Poligono(Retangulo(100, 50)).Pixels.Clone(), null)!;
        using var comEscala = Poligono(Retangulo(100, 50));

        Assert.Throws<ArgumentException>(() => PontuadorIoU.Pontuar(semEscala, comEscala, usarEscala: true));
    }
}

public class RankingTests
{
    [Fact]
    public void Posicao_pela_pontuacao_decrescente()
    {
        var p = new Dictionary<string, double> { ["a"] = 0.9, ["b"] = 0.8, ["c"] = 0.7 };

        Assert.Equal(1, Ranking.PosicaoPessimista(p, "a"));
        Assert.Equal(2, Ranking.PosicaoPessimista(p, "b"));
        Assert.Equal(3, Ranking.PosicaoPessimista(p, "c"));
    }

    [Fact]
    public void Empate_conta_contra_a_peca_certa_seja_qual_for_a_ordem()
    {
        var p = new Dictionary<string, double> { ["a"] = 0.8, ["b"] = 0.8, ["c"] = 0.8, ["d"] = 0.1 };

        Assert.Equal(3, Ranking.PosicaoPessimista(p, "a"));
        Assert.Equal(3, Ranking.PosicaoPessimista(p, "c"));
    }

    [Fact]
    public void Candidato_certo_ausente_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Ranking.PosicaoPessimista(new Dictionary<string, double> { ["a"] = 1 }, "z"));
    }

    [Fact]
    public void NaN_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Ranking.PosicaoPessimista(new Dictionary<string, double> { ["a"] = double.NaN, ["b"] = 1 }, "b"));
    }
}
