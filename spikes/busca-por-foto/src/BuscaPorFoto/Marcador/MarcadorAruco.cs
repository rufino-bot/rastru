using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Marcador;

/// <summary>
/// O único marcador do spike: dicionário 4x4_50, id 0, impresso com lado de 100 mm.
/// Quem gera o PDF e quem detecta na foto leem as mesmas constantes daqui.
/// </summary>
public static class MarcadorAruco
{
    public const PredefinedDictionaryType Dicionario = PredefinedDictionaryType.Dict4X4_50;
    public const int Id = 0;

    /// <summary>Lado externo do quadrado preto impresso, borda inclusa.</summary>
    public const double LadoMm = 100.0;

    /// <summary>4 células de dados + 1 célula de borda de cada lado.</summary>
    public const int CelulasPorLado = 6;

    /// <summary>
    /// Grade de células do marcador: <c>[linha, coluna]</c> verdadeiro quando a célula é preta.
    /// A linha 0 é o topo.
    /// </summary>
    public static bool[,] Grade()
    {
        using var dicionario = CvAruco.GetPredefinedDictionary(Dicionario);
        using var imagem = new Mat();
        // Um pixel por célula: a imagem gerada É a grade.
        dicionario.GenerateImageMarker(Id, CelulasPorLado, imagem, 1);

        var grade = new bool[CelulasPorLado, CelulasPorLado];
        for (var linha = 0; linha < CelulasPorLado; linha++)
            for (var coluna = 0; coluna < CelulasPorLado; coluna++)
                grade[linha, coluna] = imagem.At<byte>(linha, coluna) < 128;
        return grade;
    }
}
