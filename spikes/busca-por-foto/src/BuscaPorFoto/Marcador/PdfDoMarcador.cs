using System.Globalization;
using System.Text;

namespace BuscaPorFoto.Marcador;

/// <summary>
/// PDF de uma página A4 com o marcador em vetor (um retângulo por célula preta), numa medida
/// física exata, mais uma régua de conferência. Escrito à mão: é um arquivo de ~60 linhas e não
/// justifica dependência.
/// </summary>
public static class PdfDoMarcador
{
    public const double PontosPorMm = 72.0 / 25.4;
    public const double LarguraA4Pt = 595.28;
    public const double AlturaA4Pt = 841.89;

    public static byte[] Gerar()
    {
        var conteudo = Encoding.Latin1.GetBytes(Conteudo());

        var objetos = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LarguraA4Pt:F2} {AlturaA4Pt:F2}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            null, // stream: escrito à parte
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        using var saida = new MemoryStream();
        void Escrever(string texto) => saida.Write(Encoding.Latin1.GetBytes(texto));

        Escrever("%PDF-1.4\n");
        var deslocamentos = new long[objetos.Length];
        for (var i = 0; i < objetos.Length; i++)
        {
            deslocamentos[i] = saida.Position;
            Escrever($"{i + 1} 0 obj\n");
            if (objetos[i] is { } dicionario)
            {
                Escrever(dicionario + "\n");
            }
            else
            {
                Escrever($"<< /Length {conteudo.Length} >>\nstream\n");
                saida.Write(conteudo);
                Escrever("\nendstream\n");
            }
            Escrever("endobj\n");
        }

        var inicioDoXref = saida.Position;
        Escrever($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var deslocamento in deslocamentos)
            Escrever($"{deslocamento:D10} 00000 n \n");
        Escrever($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{inicioDoXref}\n%%EOF\n");

        return saida.ToArray();
    }

    private static string Conteudo()
    {
        var grade = MarcadorAruco.Grade();
        var celulaPt = MarcadorAruco.LadoMm / MarcadorAruco.CelulasPorLado * PontosPorMm;
        var ladoPt = MarcadorAruco.LadoMm * PontosPorMm;
        var x0 = (LarguraA4Pt - ladoPt) / 2;
        var yTopo = AlturaA4Pt - 160;

        var s = new StringBuilder();
        void Linha(FormattableString f) => s.Append(f.ToString(CultureInfo.InvariantCulture)).Append('\n');

        Linha($"0 g");
        for (var linha = 0; linha < MarcadorAruco.CelulasPorLado; linha++)
            for (var coluna = 0; coluna < MarcadorAruco.CelulasPorLado; coluna++)
                if (grade[linha, coluna])
                    Linha($"{x0 + coluna * celulaPt:F3} {yTopo - (linha + 1) * celulaPt:F3} {celulaPt:F3} {celulaPt:F3} re f");

        var yRegua = yTopo - ladoPt - 50;
        Linha($"0 G 1 w {x0:F3} {yRegua:F3} m {x0 + ladoPt:F3} {yRegua:F3} l S");
        Linha($"{x0:F3} {yRegua - 8:F3} m {x0:F3} {yRegua + 8:F3} l S");
        Linha($"{x0 + ladoPt:F3} {yRegua - 8:F3} m {x0 + ladoPt:F3} {yRegua + 8:F3} l S");

        var textos = new[]
        {
            "Marcador ArUco 4x4_50, id 0 - lado de 100 mm",
            "Imprima em tamanho real (100%), sem \"ajustar à página\".",
            "Confira com régua: o quadrado preto e a linha abaixo dele medem 100 mm cada.",
            "Se não medirem, a escala do spike fica errada: reimprima antes de fotografar.",
        };
        // Latin-1 coincide com a WinAnsiEncoding da fonte só até U+00FF; travessão e aspas
        // tipográficas viram '?' em silêncio no Encoding.Latin1.
        if (textos.SelectMany(t => t).FirstOrDefault(c => c > 'ÿ') is var fora and not '\0')
            throw new InvalidOperationException($"Caractere fora do Latin-1 no texto do PDF: U+{(int)fora:X4}");
        var yTexto = yRegua - 40;
        foreach (var texto in textos)
        {
            Linha($"BT /F1 11 Tf {x0 - 60:F3} {yTexto:F3} Td ({Escapar(texto)}) Tj ET");
            yTexto -= 18;
        }

        return s.ToString();
    }

    private static string Escapar(string texto) =>
        texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
