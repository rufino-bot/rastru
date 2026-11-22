using BuscaPorFoto.Marcador;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Foto;

/// <param name="Imagem">Foto em vista de cima, na escala <see cref="Retificador.PixelsPorMm"/>.</param>
/// <param name="CantosDoMarcador">Os 4 cantos do marcador nas coordenadas de <paramref name="Imagem"/>.</param>
/// <param name="CentroDaFoto">O centro da foto original, levado às coordenadas de <paramref name="Imagem"/>: é onde a peça foi enquadrada (spec §4.2).</param>
public sealed record FotoRetificada(Mat Imagem, Point2f[] CantosDoMarcador, Point2f CentroDaFoto) : IDisposable
{
    public double MmPorPixel => 1.0 / Retificador.PixelsPorMm;
    public void Dispose() => Imagem.Dispose();
}

public static class Retificador
{
    public const double PixelsPorMm = 2.0;

    /// <summary>
    /// Maior distância, em mm, que a saída alcança a partir do marcador. Sem esse teto, um canto
    /// da foto perto da linha do horizonte do plano projeta para o infinito.
    /// </summary>
    public const double AlcanceMaximoMm = 1500;

    /// <summary>Corrige a perspectiva pelo marcador; nulo quando o marcador não é detectado.</summary>
    public static FotoRetificada? Retificar(Mat fotoBgr)
    {
        using var cinza = Imagens.ParaCinza(fotoBgr);

        using var dicionario = CvAruco.GetPredefinedDictionary(MarcadorAruco.Dicionario);
        new ArucoDetector(dicionario).DetectMarkers(cinza, out var cantos, out var ids, out _);
        var indice = Array.IndexOf(ids, MarcadorAruco.Id);
        if (indice < 0) return null;

        var ladoPx = (float)(MarcadorAruco.LadoMm * PixelsPorMm);
        var marcadorNaOrigem = new[] { new Point2f(0, 0), new Point2f(ladoPx, 0), new Point2f(ladoPx, ladoPx), new Point2f(0, ladoPx) };
        using var paraOrigem = Cv2.GetPerspectiveTransform(cantos[indice], marcadorNaOrigem);

        var cantosDaFoto = new[]
        {
            new Point2f(0, 0), new Point2f(fotoBgr.Width, 0),
            new Point2f(fotoBgr.Width, fotoBgr.Height), new Point2f(0, fotoBgr.Height),
        };
        var projetados = Cv2.PerspectiveTransform(cantosDaFoto, paraOrigem);

        var alcance = (float)(AlcanceMaximoMm * PixelsPorMm);
        float Limitar(float v) => float.IsFinite(v) ? Math.Clamp(v, -alcance, alcance + ladoPx) : 0;
        var minX = projetados.Min(p => Limitar(p.X));
        var maxX = projetados.Max(p => Limitar(p.X));
        var minY = projetados.Min(p => Limitar(p.Y));
        var maxY = projetados.Max(p => Limitar(p.Y));

        // Translada para a caixa começar em (0,0).
        using var translacao = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        translacao.Set(0, 2, (double)-minX);
        translacao.Set(1, 2, (double)-minY);
        using var homografia = (translacao * paraOrigem).ToMat();

        var tamanho = new Size((int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
        var retificada = new Mat();
        Cv2.WarpPerspective(fotoBgr, retificada, homografia, tamanho, InterpolationFlags.Linear,
            BorderTypes.Replicate);

        var cantosNaSaida = marcadorNaOrigem.Select(p => new Point2f(p.X - minX, p.Y - minY)).ToArray();
        var centro = Cv2.PerspectiveTransform(new[] { new Point2f(fotoBgr.Width / 2f, fotoBgr.Height / 2f) }, homografia)[0];
        return new FotoRetificada(retificada, cantosNaSaida, centro);
    }
}
