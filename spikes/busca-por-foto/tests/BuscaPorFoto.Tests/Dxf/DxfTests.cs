using BuscaPorFoto.Dxf;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Dxf;

public class DxfTests
{
    private static NestingSeparado Separar(DxfSintetico dxf, params string[] camadasIgnoradas) =>
        SeparadorDeNesting.Separar(LeitorDeDxf.Ler(dxf.Gerar()), camadasIgnoradas.ToHashSet());

    private static NestingSeparado Separar(DxfSintetico dxf, BordaDaChapa borda) =>
        SeparadorDeNesting.Separar(LeitorDeDxf.Ler(dxf.Gerar()), new HashSet<string>(), borda);

    private static double AreaMm2(PecaDoNesting peca)
    {
        var mm = peca.Silhueta.MmPorPixel ?? throw new InvalidOperationException("Silhueta sem escala.");
        return Cv2.CountNonZero(peca.Silhueta.Pixels) * mm * mm;
    }

    private static void AssertArea(double esperada, PecaDoNesting peca) =>
        Assert.InRange(AreaMm2(peca), esperada * 0.98, esperada * 1.02);

    private static DxfSintetico Retangulo100x50() =>
        new DxfSintetico().Polilinha(DxfSintetico.Retangulo(0, 0, 100, 50), fechada: true);

    [Fact]
    public void Retangulo_de_polilinha_fechada_vira_uma_peca_em_mm_reais()
    {
        var nesting = Separar(Retangulo100x50());

        var peca = Assert.Single(nesting.Pecas);
        AssertArea(5000, peca);
        Assert.Equal(0, peca.Furos);
        Assert.Empty(nesting.Avisos);
    }

    [Fact]
    public void Circulo_dentro_da_peca_e_furo()
    {
        var nesting = Separar(Retangulo100x50().Circulo(50, 25, 10));

        var peca = Assert.Single(nesting.Pecas);
        Assert.Equal(1, peca.Furos);
        AssertArea(5000 - Math.PI * 100, peca);
        var pixels = peca.Silhueta.Pixels;
        Assert.Equal(0, pixels.At<byte>(pixels.Rows / 2, pixels.Cols / 2));
    }

    [Fact]
    public void Bulge_vira_arco()
    {
        var dxf = new DxfSintetico().Polilinha([(0, 0, 0), (100, 0, 1), (100, 50, 0), (0, 50, 0)], fechada: true);

        var peca = Assert.Single(Separar(dxf).Pecas);

        AssertArea(5000 + Math.PI * 25 * 25 / 2, peca);
    }

    [Fact]
    public void Linhas_e_arco_soltos_que_fecham_viram_uma_peca()
    {
        var dxf = new DxfSintetico()
            .Linha(0, 0, 80, 0).Linha(80, 0, 80, 40).Linha(0, 40, 0, 0)
            .Arco(40, 40, 40, 0, 180);

        var peca = Assert.Single(Separar(dxf).Pecas);

        AssertArea(3200 + Math.PI * 40 * 40 / 2, peca);
    }

    [Fact]
    public void PolilinhaAntiga_le_como_a_nova()
    {
        var dxf = new DxfSintetico().PolilinhaAntiga(DxfSintetico.Retangulo(0, 0, 100, 50), fechada: true);

        var peca = Assert.Single(Separar(dxf).Pecas);

        AssertArea(5000, peca);
    }

    [Fact]
    public void Duas_pecas_e_os_furos_de_cada_uma()
    {
        var dxf = new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 100, 50), fechada: true)
            .Circulo(25, 25, 8).Circulo(75, 25, 8)
            .Polilinha(DxfSintetico.Retangulo(200, 0, 60, 60), fechada: true)
            .Circulo(230, 30, 10);

        var nesting = Separar(dxf);

        Assert.Equal(2, nesting.Pecas.Count);
        Assert.Equal(2, nesting.Pecas.OrderByDescending(AreaMm2).First().Furos);
        Assert.Equal(1, nesting.Pecas.OrderBy(AreaMm2).First().Furos);
    }

    [Fact]
    public void Peca_dentro_do_furo_de_outra_e_uma_peca_a_mais()
    {
        var dxf = new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 200, 200), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(40, 40, 120, 120), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(80, 85, 40, 30), fechada: true);

        var nesting = Separar(dxf);

        Assert.Equal(2, nesting.Pecas.Count);
        Assert.Equal(1, nesting.Pecas.OrderByDescending(AreaMm2).First().Furos);
        Assert.Equal(0, nesting.Pecas.OrderBy(AreaMm2).First().Furos);
    }

    [Fact]
    public void Borda_da_chapa_sai_com_aviso()
    {
        var dxf = new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 1000, 500), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(100, 100, 200, 100), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(500, 100, 150, 150), fechada: true);

        var nesting = Separar(dxf);

        Assert.Equal(2, nesting.Pecas.Count);
        Assert.Contains(nesting.Avisos, a => a.Contains("borda da chapa"));
    }

    [Fact]
    public void Linha_aberta_dentro_da_peca_e_avisada_e_nao_muda_a_peca()
    {
        var nesting = Separar(Retangulo100x50().Linha(10, 25, 90, 25, "DOBRA"));

        var peca = Assert.Single(nesting.Pecas);
        Assert.Equal(0, peca.Furos);
        AssertArea(5000, peca);
        Assert.Contains(nesting.Avisos, a => a.Contains("aberto"));
    }

    [Fact]
    public void Linha_comum_e_avisada_e_nao_junta_as_pecas()
    {
        var dxf = new DxfSintetico()
            .RetanguloEmLinhas(0, 0, 100, 50)
            .Linha(100, 0, 200, 0).Linha(200, 0, 200, 50).Linha(200, 50, 100, 50);

        var nesting = Separar(dxf);

        Assert.Empty(nesting.Pecas);
        Assert.Contains(nesting.Avisos, a => a.Contains("linha comum"));
    }

    [Fact]
    public void Entidade_desconhecida_e_avisada()
    {
        var nesting = Separar(Retangulo100x50().Spline());

        Assert.Single(nesting.Pecas);
        Assert.Contains(nesting.Avisos, a => a.Contains("SPLINE"));
    }

    [Fact]
    public void Texto_e_ignorado_sem_aviso()
    {
        var nesting = Separar(Retangulo100x50().Texto());

        Assert.Single(nesting.Pecas);
        Assert.Empty(nesting.Avisos);
    }

    [Fact]
    public void Insert_de_bloco_e_expandido_com_rotacao_e_espelho()
    {
        var dxf = new DxfSintetico()
            .Bloco("P", b => b.Polilinha(
                [(0, 0, 0), (120, 0, 0), (120, 25, 0), (30, 25, 0), (30, 70, 0), (0, 70, 0)], fechada: true))
            .Insert("P", 500, 0, rotacaoGraus: 90)
            .Insert("P", 0, 500, escalaX: -1);

        var nesting = Separar(dxf);

        Assert.Equal(2, nesting.Pecas.Count);
        foreach (var p in nesting.Pecas) AssertArea(4350, p);
        var girada = nesting.Pecas.Single(p => p.CaixaMm.Width < 100);
        var espelhada = nesting.Pecas.Single(p => p.CaixaMm.Width > 100);
        Assert.Equal(-120, espelhada.CaixaMm.X, 0.5); // sem o espelho seria 0
        Assert.Equal(70, girada.CaixaMm.Width, 0.01);
        Assert.Equal(120, girada.CaixaMm.Height, 0.01);
        var (a, b) = (nesting.Pecas[0].Assinatura, nesting.Pecas[1].Assinatura);
        Assert.Equal(a.Furos, b.Furos);
        Assert.Equal(a.Valores.Length, b.Valores.Length);
        for (var i = 0; i < a.Valores.Length; i++)
            Assert.Equal(a.Valores[i], b.Valores[i], Math.Abs(a.Valores[i]) * 1e-9 + 1e-9);
    }

    [Fact]
    public void Polegada_vira_milimetro()
    {
        var dxf = new DxfSintetico().ComUnidade(1).Polilinha(DxfSintetico.Retangulo(0, 0, 4, 2), fechada: true);

        var peca = Assert.Single(Separar(dxf).Pecas);

        AssertArea(101.6 * 50.8, peca);
    }

    [Fact]
    public void Sem_unidade_assume_milimetro_e_avisa()
    {
        var nesting = Separar(Retangulo100x50().SemUnidade());

        AssertArea(5000, Assert.Single(nesting.Pecas));
        Assert.Contains(nesting.Avisos, a => a.Contains("$INSUNITS"));
    }

    [Fact]
    public void Camada_ignorada_nao_vira_furo()
    {
        var dxf = Retangulo100x50().Circulo(50, 25, 10, "MARCACAO");

        var ignorada = Assert.Single(Separar(dxf, "MARCACAO").Pecas);
        var mantida = Assert.Single(Separar(dxf).Pecas);

        Assert.Equal(0, ignorada.Furos);
        Assert.Equal(1, mantida.Furos);
    }

    [Fact]
    public void Pontas_a_menos_da_tolerancia_em_celulas_vizinhas_se_emendam()
    {
        var dxf = new DxfSintetico()
            .Linha(0, 0, 100.0049, 0)
            .Linha(100.0051, 0, 100, 50)
            .Linha(100, 50, 0, 50)
            .Linha(0, 50, 0, 0);

        var peca = Assert.Single(Separar(dxf).Pecas);

        AssertArea(5000, peca);
    }

    [Fact]
    public void Dxf_binario_e_recusado()
    {
        Assert.Throws<FormatException>(() => LeitorDeDxf.Ler("AutoCAD Binary DXF\r\n\u001a\0\0\0"));
    }

    [Fact]
    public void Numeracao_vai_de_cima_para_baixo_e_da_esquerda_para_a_direita()
    {
        // Escritos fora de ordem de propósito: a numeração não pode depender da ordem do arquivo.
        var dxf = new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 40, 30), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(200, 100, 80, 50), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(0, 100, 60, 50), fechada: true);

        var pecas = Separar(dxf).Pecas;

        Assert.Equal(new[] { 1, 2, 3 }, pecas.Select(p => p.Numero));
        AssertArea(3000, pecas.Single(p => p.Numero == 1));
        AssertArea(4000, pecas.Single(p => p.Numero == 2));
        AssertArea(1200, pecas.Single(p => p.Numero == 3));
    }

    [Fact]
    public void Imagem_numerada_mostra_cada_peca()
    {
        var dxf = new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 100, 50), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(200, 0, 60, 60), fechada: true);
        var nesting = Separar(dxf);

        using var imagem = ImagemNumerada.Gerar(nesting);

        Assert.Equal(MatType.CV_8UC3, imagem.Type());
        Assert.True(Math.Max(imagem.Width, imagem.Height) <= ImagemNumerada.LadoMaximo);
        Assert.Equal(new Vec3b(255, 255, 255), imagem.At<Vec3b>(0, 0));

        var minX = nesting.Pecas.Min(p => p.CaixaMm.X);
        var maxY = nesting.Pecas.Max(p => p.CaixaMm.Y + p.CaixaMm.Height);
        var maior = Math.Max(
            nesting.Pecas.Max(p => p.CaixaMm.X + p.CaixaMm.Width) - minX,
            maxY - nesting.Pecas.Min(p => p.CaixaMm.Y));
        var escala = ImagemNumerada.Escala(maior);
        foreach (var p in nesting.Pecas)
        {
            var px = ImagemNumerada.Margem + (int)Math.Round((p.CaixaMm.X + p.CaixaMm.Width / 2 - minX) * escala);
            var py = ImagemNumerada.Margem + (int)Math.Round((maxY - (p.CaixaMm.Y + p.CaixaMm.Height / 2)) * escala);
            Assert.NotEqual(new Vec3b(255, 255, 255), imagem.At<Vec3b>(py, px));
        }
    }

    private static void AssertCaixa(double x, double y, double largura, double altura, PecaDoNesting p)
    {
        Assert.Equal(x, p.CaixaMm.X, 0.5);
        Assert.Equal(y, p.CaixaMm.Y, 0.5);
        Assert.Equal(largura, p.CaixaMm.Width, 0.5);
        Assert.Equal(altura, p.CaixaMm.Height, 0.5);
    }

    [Fact]
    public void Insert_respeita_o_ponto_base_do_bloco()
    {
        // Bloco (10,5)-(60,35) com base (10,5), inserido em (100,200): vai a (100,200)-(150,230).
        // Ignorar a base o deixaria em x = 110, y = 205.
        var dxf = new DxfSintetico()
            .Bloco("B", b => b.Polilinha(DxfSintetico.Retangulo(10, 5, 50, 30), fechada: true), baseX: 10, baseY: 5)
            .Insert("B", 100, 200);

        AssertCaixa(100, 200, 50, 30, Assert.Single(Separar(dxf).Pecas));
    }

    [Fact]
    public void Insert_aninhado_compoe_as_transformacoes()
    {
        // R = 30x20 na origem. W põe R em (100,0): x 100..130, y 0..20. INSERT de W em (0,0) girado 90°:
        // (x, y) -> (-y, x), logo x -20..0, y 100..130: caixa (-20, 100, 20, 30).
        var dxf = new DxfSintetico()
            .Bloco("R", b => b.Polilinha(DxfSintetico.Retangulo(0, 0, 30, 20), fechada: true))
            .Bloco("W", b => b.Insert("R", 100, 0))
            .Insert("W", 0, 0, rotacaoGraus: 90);

        AssertCaixa(-20, 100, 20, 30, Assert.Single(Separar(dxf).Pecas));
    }

    [Fact]
    public void Insert_com_escala_nao_uniforme_e_rotacao_escala_antes_de_girar()
    {
        // R 30x20, escala (2,1) -> x 0..60, y 0..20; depois 90°: (x, y) -> (-y, x): caixa (-20, 0, 20, 60).
        // Girar antes de escalar daria (-40, 0, 40, 30); girar -90° daria (0, -60, 20, 60).
        var dxf = new DxfSintetico()
            .Bloco("R", b => b.Polilinha(DxfSintetico.Retangulo(0, 0, 30, 20), fechada: true))
            .Insert("R", 0, 0, escalaX: 2, escalaY: 1, rotacaoGraus: 90);

        AssertCaixa(-20, 0, 20, 60, Assert.Single(Separar(dxf).Pecas));
    }

    [Fact]
    public void Circulo_com_extrusao_menos_z_tem_o_x_espelhado()
    {
        // No OCS o centro é (-50, 25); no WCS é (50, 25), dentro do retângulo: é furo.
        var peca = Assert.Single(Separar(Retangulo100x50().Circulo(-50, 25, 10, extrusaoZ: -1)).Pecas);

        Assert.Equal(1, peca.Furos);
    }

    [Fact]
    public void Extrusao_que_nao_e_mais_ou_menos_z_e_avisada()
    {
        var nesting = Separar(Retangulo100x50().Circulo(50, 25, 10, extrusaoZ: 0.5));

        Assert.Contains(nesting.Avisos, a => a.Contains("extrusão não suportada"));
        Assert.Equal(0, Assert.Single(nesting.Pecas).Furos);
    }

    [Fact]
    public void Peca_unica_com_dois_furos_sob_Nao_continua_peca()
    {
        var dxf = Retangulo100x50().Circulo(25, 25, 8).Circulo(75, 25, 8);

        var peca = Assert.Single(Separar(dxf, BordaDaChapa.Nao).Pecas);

        Assert.Equal(2, peca.Furos);
    }

    [Fact]
    public void Peca_unica_com_dois_furos_sob_Auto_perde_a_borda_e_avisa_da_alternativa()
    {
        var dxf = Retangulo100x50().Circulo(25, 25, 8).Circulo(75, 25, 8);

        var nesting = Separar(dxf, BordaDaChapa.Auto);

        Assert.Equal(2, nesting.Pecas.Count);
        Assert.Contains(nesting.Avisos, a => a.Contains("borda da chapa") && a.Contains("'nao'"));
    }

    private static DxfSintetico ChapaComUmaPeca() =>
        new DxfSintetico()
            .Polilinha(DxfSintetico.Retangulo(0, 0, 1000, 500), fechada: true)
            .Polilinha(DxfSintetico.Retangulo(100, 100, 100, 50), fechada: true)
            .Circulo(150, 125, 10);

    [Fact]
    public void Chapa_com_uma_peca_so_sob_Sim_tira_a_borda()
    {
        var nesting = Separar(ChapaComUmaPeca(), BordaDaChapa.Sim);

        var peca = Assert.Single(nesting.Pecas);
        Assert.Equal(1, peca.Furos);
        AssertArea(5000 - Math.PI * 100, peca);
        Assert.Contains(nesting.Avisos, a => a.Contains("borda da chapa"));
    }

    [Fact]
    public void Chapa_com_uma_peca_so_sob_Auto_e_lida_como_peca_com_furo_e_avisa_da_alternativa()
    {
        var nesting = Separar(ChapaComUmaPeca(), BordaDaChapa.Auto);

        var peca = Assert.Single(nesting.Pecas, p => p.CaixaMm.Width > 500);
        Assert.Equal(1, peca.Furos);
        Assert.Contains(nesting.Avisos, a => a.Contains("borda da chapa") && a.Contains("'sim'"));
    }

    [Fact]
    public void Assinatura_do_retangulo_com_furo_central_tem_os_valores_analiticos()
    {
        var peca = Assert.Single(Separar(Retangulo100x50().Circulo(50, 25, 10)).Pecas);

        var v = peca.Assinatura.Valores;
        Assert.Equal(1, peca.Assinatura.Furos);
        Assert.Equal(5, v.Length);
        // Analítico: área 5000 − 100π; perímetro 300 + 20π; furos 100π; momentos do retângulo menos
        // πr⁴/4 do furo (4.166.667 − 7.854 e 1.041.667 − 7.854). O polígono do círculo dá ~0,5% menos.
        Assert.InRange(v[0], 4687.9 * 0.995, 4687.9 * 1.005);
        Assert.InRange(v[1], 362.7 * 0.995, 362.7 * 1.005);
        Assert.InRange(v[2], 312.1 * 0.995, 312.1 * 1.005);
        Assert.InRange(v[3], 4158813 * 0.995, 4158813 * 1.005);
        Assert.InRange(v[4], 1033813 * 0.995, 1033813 * 1.005);
    }
}
