using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BuscaPorFoto.Marcador;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Tests.Marcador;

public class MarcadorArucoTests
{
    [Fact]
    public void A_grade_desenhada_e_detectada_como_o_marcador_do_spike()
    {
        using var imagem = new Mat(800, 800, MatType.CV_8UC1, Scalar.All(255));
        CenaSintetica.DesenharMarcador(imagem, new Point2d(20, 20), pixelsPorMm: 4);

        using var dicionario = CvAruco.GetPredefinedDictionary(MarcadorAruco.Dicionario);
        new ArucoDetector(dicionario).DetectMarkers(imagem, out _, out var ids, out _);

        Assert.Equal(new[] { MarcadorAruco.Id }, ids);
    }

    [Fact]
    public void A_borda_da_grade_e_toda_preta()
    {
        var grade = MarcadorAruco.Grade();
        var n = MarcadorAruco.CelulasPorLado;
        for (var i = 0; i < n; i++)
        {
            Assert.True(grade[0, i]);
            Assert.True(grade[n - 1, i]);
            Assert.True(grade[i, 0]);
            Assert.True(grade[i, n - 1]);
        }
    }
}

public class PdfDoMarcadorTests
{
    private static readonly string Texto = Encoding.Latin1.GetString(PdfDoMarcador.Gerar());

    [Fact]
    public void Comeca_pelo_cabecalho_de_pdf_e_termina_em_eof()
    {
        Assert.StartsWith("%PDF-1.4\n", Texto);
        Assert.EndsWith("%%EOF\n", Texto);
    }

    [Fact]
    public void Cada_deslocamento_do_xref_aponta_para_o_seu_objeto()
    {
        // "\nxref\n", e não "xref\n": a última ocorrência desta é a do "startxref".
        var xref = Texto[Texto.LastIndexOf("\nxref\n", StringComparison.Ordinal)..];
        var deslocamentos = Regex.Matches(xref, @"^(\d{10}) 00000 n $", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(5, deslocamentos.Count);
        for (var i = 0; i < deslocamentos.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj\n", Texto[deslocamentos[i]..]);
    }

    [Fact]
    public void Cada_celula_preta_e_desenhada_na_sua_posicao_da_grade()
    {
        // Lê os retângulos do conteúdo do PDF e os converte em (linha, coluna) pela própria caixa
        // delimitadora, sem usar PdfDoMarcador: a linha 0 é o topo da página e a coluna 0 é a
        // esquerda (o y do PDF cresce para cima). Um espelho vertical ou horizontal da grade quebra aqui.
        var retangulos = LerRetangulos();
        var esquerda = retangulos.Min(r => r.X);
        var topo = retangulos.Max(r => r.Y + r.Altura);
        var lado = retangulos.Max(r => r.X + r.Largura) - esquerda;
        var celula = lado / MarcadorAruco.CelulasPorLado;

        var desenhadas = retangulos
            .Select(r => (
                Linha: (int)Math.Round((topo - (r.Y + r.Altura)) / celula),
                Coluna: (int)Math.Round((r.X - esquerda) / celula)))
            .OrderBy(p => p.Linha).ThenBy(p => p.Coluna)
            .ToList();

        var grade = MarcadorAruco.Grade();
        var esperadas = new List<(int Linha, int Coluna)>();
        for (var linha = 0; linha < MarcadorAruco.CelulasPorLado; linha++)
            for (var coluna = 0; coluna < MarcadorAruco.CelulasPorLado; coluna++)
                if (grade[linha, coluna])
                    esperadas.Add((linha, coluna));

        Assert.Equal(esperadas, desenhadas);
    }

    [Fact]
    public void O_marcador_mede_100_mm_de_lado_em_pontos_tipograficos()
    {
        // 100 mm = 100 * 72 / 25.4 = 283.465 pt. Valor literal, de propósito: não usa
        // PdfDoMarcador.PontosPorMm, para que uma constante errada não passe por consistência interna.
        var retangulos = LerRetangulos();
        var largura = retangulos.Max(r => r.X + r.Largura) - retangulos.Min(r => r.X);
        var altura = retangulos.Max(r => r.Y + r.Altura) - retangulos.Min(r => r.Y);

        Assert.InRange(largura, 283.455, 283.475);
        Assert.InRange(altura, 283.455, 283.475);
    }

    private static List<(double X, double Y, double Largura, double Altura)> LerRetangulos() =>
        Regex.Matches(Texto, @"^([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f$", RegexOptions.Multiline)
            .Select(m => (
                X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Largura: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                Altura: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToList();
}
