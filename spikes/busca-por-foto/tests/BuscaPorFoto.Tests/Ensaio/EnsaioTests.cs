using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Ensaios;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Execucao;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Ensaios;

public class EnsaioTests
{
    private const double Mm = 0.5;

    // ---- formas desenhadas a 0,5 mm por pixel ----

    private static Mascara Desenhar(IEnumerable<(double X, double Y)> contornoEmMm)
    {
        var pontos = contornoEmMm.Select(p => new Point((int)Math.Round(p.X / Mm) + 2, (int)Math.Round(p.Y / Mm) + 2)).ToArray();
        var largura = pontos.Max(p => p.X) + 3;
        var altura = pontos.Max(p => p.Y) + 3;
        var pixels = new Mat(altura, largura, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(pixels, [pontos], Scalar.All(255));
        return new Mascara(pixels, Mm);
    }

    private static Mascara Retangulo(double w, double h) => Desenhar([(0, 0), (w, 0), (w, h), (0, h)]);

    private static Mascara L(double w, double h, double t) =>
        Desenhar([(0, 0), (w, 0), (w, t), (t, t), (t, h), (0, h)]);

    private static Mascara Circulo(double raioMm)
    {
        var r = (int)Math.Round(raioMm / Mm);
        var pixels = new Mat(2 * r + 5, 2 * r + 5, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(pixels, new Point(r + 2, r + 2), r, Scalar.All(255), -1);
        return new Mascara(pixels, Mm);
    }

    /// <summary>[área, perímetro, áreas dos furos, λ1, λ2] em mm, calculados dos pixels da própria forma.</summary>
    private static AssinaturaGeometrica Assinar(Mascara m)
    {
        var mom = Cv2.Moments(m.Pixels, binaryImage: true);
        var area = mom.M00 * Mm * Mm;
        var m20 = mom.Mu20 / mom.M00; var m02 = mom.Mu02 / mom.M00; var m11 = mom.Mu11 / mom.M00;
        var meio = (m20 + m02) / 2;
        var raiz = Math.Sqrt(((m20 - m02) / 2) * ((m20 - m02) / 2) + m11 * m11);
        Cv2.FindContours(m.Pixels, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        var perimetro = contornos.Sum(c => Cv2.ArcLength(c, true)) * Mm;
        return new AssinaturaGeometrica(0, [area, perimetro, 0, (meio + raiz) * Mm * Mm * area, (meio - raiz) * Mm * Mm * area]);
    }

    private static Referencia Ref(int indice, Mascara forma) => new($"K{indice + 1:D4}", [forma], Assinar(forma));

    private static List<Referencia> BibliotecaDeRetangulosELs()
    {
        (double W, double H)[] retangulos =
            [(60, 60), (100, 40), (150, 30), (200, 50), (80, 120), (180, 90), (120, 120), (240, 40), (70, 100), (160, 60), (90, 45), (130, 80), (220, 70)];
        (double W, double H, double T)[] ls =
            [(100, 100, 30), (150, 80, 25), (120, 60, 20), (200, 100, 40), (90, 140, 30), (160, 120, 20), (110, 110, 45), (180, 70, 35), (130, 150, 25), (140, 90, 50), (100, 60, 15), (170, 110, 30)];
        var formas = retangulos.Select(r => Retangulo(r.W, r.H)).Concat(ls.Select(l => L(l.W, l.H, l.T))).ToList();
        return formas.Select((f, i) => Ref(i, f)).ToList();
    }

    private static bool PixelsIguais(Mascara a, Mascara b)
    {
        if (a.Pixels.Size() != b.Pixels.Size()) return false;
        using var diferenca = new Mat();
        Cv2.Absdiff(a.Pixels, b.Pixels, diferenca);
        return Cv2.CountNonZero(diferenca) == 0;
    }

    // ---- o deformador ----

    [Fact]
    public void Deformar_e_deterministico_pela_semente_e_pelo_indice()
    {
        using var forma = Retangulo(100, 50);
        var p = ParametrosDeDeformacao.Padrao;

        using var a = Deformador.Deformar(forma, Condicao.SemMarcador, p, 3);
        using var b = Deformador.Deformar(forma, Condicao.SemMarcador, p, 3);
        using var outro = Deformador.Deformar(forma, Condicao.SemMarcador, p, 4);

        Assert.True(PixelsIguais(a, b));
        Assert.False(PixelsIguais(a, outro));
    }

    [Fact]
    public void Com_marcador_a_deformacao_mantem_o_tamanho_real()
    {
        using var forma = Retangulo(100, 50);

        using var foto = Deformador.Deformar(forma, Condicao.ComMarcador, ParametrosDeDeformacao.Padrao, 0);

        Assert.NotNull(foto.MmPorPixel);
        var areaMm2 = Cv2.CountNonZero(foto.Pixels) * foto.MmPorPixel!.Value * foto.MmPorPixel!.Value;
        Assert.InRange(areaMm2, 0.9 * 5000, 1.1 * 5000);
    }

    [Fact]
    public void Sem_marcador_a_deformacao_tira_a_escala()
    {
        using var forma = Retangulo(100, 50);

        using var foto = Deformador.Deformar(forma, Condicao.SemMarcador, ParametrosDeDeformacao.Padrao, 0);

        Assert.Null(foto.MmPorPixel);
    }

    // ---- o teto ----

    [Fact]
    public void Teto_de_formas_distintas_segue()
    {
        var biblioteca = BibliotecaDeRetangulosELs();
        try
        {
            var teto = Ensaio.MedirTeto(biblioteca, 20, ParametrosDeDeformacao.Padrao);

            Assert.Equal(20, teto.Alvos);
            Assert.Equal(25, teto.Grupos);
            Assert.True(teto.Segue);
        }
        finally { foreach (var r in biblioteca) foreach (var v in r.Vistas) v.Dispose(); }
    }

    [Fact]
    public void Segue_so_com_80_por_cento_na_melhor_condicao()
    {
        Assert.False(ResultadoDoTeto.Decidir(20, 15, 15));
        Assert.True(ResultadoDoTeto.Decidir(20, 16, 3));
        Assert.True(ResultadoDoTeto.Decidir(20, 3, 16));
        Assert.False(ResultadoDoTeto.Decidir(18, 14, 14));
        Assert.True(ResultadoDoTeto.Decidir(18, 15, 0));
    }

    [Fact]
    public void Sem_marcador_circulos_de_raios_diferentes_se_confundem()
    {
        var biblioteca = Enumerable.Range(0, 25).Select(i => Ref(i, Circulo(50 + 0.5 * i))).ToList();
        try
        {
            var teto = Ensaio.MedirTeto(biblioteca, 20, ParametrosDeDeformacao.Padrao);

            Assert.Equal(25, teto.Grupos);
            Assert.True(teto.AcertosSemMarcador <= 10, $"{teto.AcertosSemMarcador} de 20");
        }
        finally { foreach (var r in biblioteca) foreach (var v in r.Vistas) v.Dispose(); }
    }

    // ---- a leitura em massa ----

    [Fact]
    public void Ler_tudo_classifica_lido_avisado_e_recusado()
    {
        using var pasta = new PastaTemporaria();
        var sub = Directory.CreateDirectory(Path.Combine(pasta.Caminho, "sub")).FullName;
        File.WriteAllBytes(Path.Combine(pasta.Caminho, "bom.stl"), MalhaSintetica.StlBinario(MalhaSintetica.Caixa(10, 20, 30)));
        File.WriteAllText(Path.Combine(sub, "com-spline.DXF"),
            new DxfSintetico().Polilinha(DxfSintetico.Retangulo(0, 0, 100, 50), fechada: true).Spline().Gerar());
        File.WriteAllText(Path.Combine(sub, "lixo.stl"), "isto não é um STL");
        File.WriteAllText(Path.Combine(sub, "ignorado.txt"), "nem lido");

        var lidos = Ensaio.LerTudo(pasta.Caminho);

        Assert.Equal(3, lidos.Count);
        Assert.Equal("lido", lidos.Single(l => l.Arquivo.EndsWith("bom.stl")).Situacao);
        var dxf = lidos.Single(l => l.Arquivo.EndsWith("com-spline.DXF"));
        Assert.Equal("lido com avisos", dxf.Situacao);
        Assert.Contains(dxf.Avisos, a => a.Contains("SPLINE"));
        Assert.Equal("recusado: FormatException", lidos.Single(l => l.Arquivo.EndsWith("lixo.stl")).Situacao);
    }

    // ---- fotos de terceiros ----

    [Fact]
    public void Alvo_sem_foto_com_marcador_vira_falha()
    {
        using var pasta = new PastaTemporaria();
        ChapaSintetica[] alvos = [new("A-100", 100, 50, 20)];
        ConjuntoSintetico.MontarFormaFinal(pasta.Caminho, alvos,
            [("caixa.stl", MalhaSintetica.StlBinario(MalhaSintetica.Caixa(100, 80, 60)))]);
        var raiz = Path.Combine(pasta.Caminho, "forma-final");
        File.Delete(Path.Combine(raiz, "alvos", "A-100", "com-marcador.png"));
        File.Delete(Path.Combine(raiz, "tipos.txt"));

        var dados = Ensaio.LerFotosDeTerceiros(raiz);
        using var saida = new PastaTemporaria();
        var linhas = Executor.ExecutarComStl(dados, Cenario.FormaFinal, saida.Caminho, MetodoDeRecorte.Otsu, TextWriter.Null);

        var alvo = Assert.Single(dados.Alvos);
        Assert.Equal("", alvo.CaminhoComMarcador);
        Assert.Null(alvo.Tipo);
        Assert.NotEmpty(linhas);
        Assert.All(linhas.Where(l => l.Condicao == Condicao.ComMarcador),
            l => { Assert.Equal(Executor.FalhaSemFotoComMarcador, l.Falha); Assert.Null(l.Posicao); });
        Assert.All(linhas.Where(l => l.Condicao == Condicao.SemMarcador),
            l => { Assert.Null(l.Falha); Assert.NotNull(l.Posicao); });
    }
}
