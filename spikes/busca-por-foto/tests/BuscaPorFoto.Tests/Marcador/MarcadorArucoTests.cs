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
    public void Desenha_um_retangulo_por_celula_preta()
    {
        var pretas = MarcadorAruco.Grade().Cast<bool>().Count(b => b);

        Assert.Equal(pretas, Regex.Matches(Texto, @" re f$", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void O_marcador_mede_exatamente_100_mm_de_lado()
    {
        var retangulos = Regex.Matches(Texto, @"^([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f$", RegexOptions.Multiline)
            .Select(m => Enumerable.Range(1, 4)
                .Select(g => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture)).ToArray())
            .ToList();

        var largura = retangulos.Max(r => r[0] + r[2]) - retangulos.Min(r => r[0]);
        var altura = retangulos.Max(r => r[1] + r[3]) - retangulos.Min(r => r[1]);
        var esperadoPt = MarcadorAruco.LadoMm * PdfDoMarcador.PontosPorMm;

        Assert.Equal(esperadoPt, largura, precision: 2);
        Assert.Equal(esperadoPt, altura, precision: 2);
    }
}
