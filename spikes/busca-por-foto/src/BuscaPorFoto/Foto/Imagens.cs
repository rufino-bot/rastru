using OpenCvSharp;

namespace BuscaPorFoto.Foto;

internal static class Imagens
{
    /// <summary>Cópia em tons de cinza; aceita imagem de 1 ou 3 canais.</summary>
    public static Mat ParaCinza(Mat imagem)
    {
        if (imagem.Channels() == 1) return imagem.Clone();
        var cinza = new Mat();
        Cv2.CvtColor(imagem, cinza, ColorConversionCodes.BGR2GRAY);
        return cinza;
    }
}

/// <summary>
/// Imagem de e para o disco SEM entregar o caminho ao OpenCV: quando ele não abre um arquivo, o log
/// nativo escreve o caminho inteiro (com o código da peça) no stderr. Aqui o .NET lê e grava os bytes.
/// </summary>
internal static class ImagensEmDisco
{
    /// <summary>Imagem colorida; vazia se o arquivo não existe, não abre ou não é uma imagem.</summary>
    public static Mat Ler(string caminho)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(caminho); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new Mat(); }
        if (bytes.Length == 0) return new Mat();
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    /// <summary>Grava como PNG; falha alto (sem o caminho na mensagem) se a codificação ou a escrita não deu certo.</summary>
    public static void Gravar(string caminho, Mat imagem)
    {
        if (imagem.Empty() || !Cv2.ImEncode(".png", imagem, out var bytes) || bytes.Length == 0)
            throw new IOException("Não foi possível codificar uma imagem da saída.");
        File.WriteAllBytes(caminho, bytes);
    }
}
