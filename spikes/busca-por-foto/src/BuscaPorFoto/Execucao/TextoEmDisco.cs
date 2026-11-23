using System.Text;

namespace BuscaPorFoto.Execucao;

/// <summary>
/// Texto da saída do spike gravado sempre com <c>\n</c> e UTF-8 sem BOM, em qualquer sistema. O agente que
/// roda a Task 10 varre os arquivos com <c>grep -F -f</c> no Git Bash do Windows, e um <c>\r</c> no fim da
/// linha de um mapa de códigos faria o código não casar com o relatório, que sai só com <c>\n</c>.
/// </summary>
internal static class TextoEmDisco
{
    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Uma linha por item, cada uma terminada em <c>\n</c> (inclusive a última).</summary>
    public static void GravarLinhas(string caminho, IEnumerable<string> linhas)
    {
        var texto = new StringBuilder();
        foreach (var linha in linhas) texto.Append(linha).Append('\n');
        Gravar(caminho, texto.ToString());
    }

    public static void Gravar(string caminho, string texto) => File.WriteAllText(caminho, texto, Utf8SemBom);
}
