using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <param name="Codigo">Nome da pasta do alvo (o "código real" do teste).</param>
public sealed record ChapaSintetica(string Codigo, float Largura, float Altura, float LadoDoFuro);

/// <summary>Uma peça do nesting: retângulo com furo quadrado central (0 = sem furo); horário inverte o sentido dos vértices.</summary>
public sealed record PecaDoDxf(float Largura, float Altura, float LadoDoFuro, bool SentidoHorario = false)
{
    public static PecaDoDxf De(ChapaSintetica c) => new(c.Largura, c.Altura, c.LadoDoFuro);
}

/// <summary>Monta em disco a pasta de dados nos três formatos (spec §4.3), com fotos sintéticas e um nesting em DXF.</summary>
public static class ConjuntoSintetico
{
    public static readonly Scalar Fundo = Scalar.All(200);
    public const double PxPorMm = 4;
    public static readonly string[] TiposEmRodizio = ["dobrada", "usinada", "soldada"];

    /// <summary>
    /// O nesting tem as peças numa fileira, com os topos alinhados, da esquerda para a direita: o número de
    /// cada uma é a posição dela na lista, começando em 1. <paramref name="blocoAusente"/> acrescenta um
    /// INSERT de bloco que não existe (o leitor avisa o nome dele).
    /// </summary>
    public static void MontarCorte(string pasta, string arquivoDoNesting, IReadOnlyList<PecaDoDxf> pecas,
        IReadOnlyList<(ChapaSintetica Alvo, int? Numero)> alvos, string? blocoAusente = null,
        bool marcadorNaFotoComMarcador = true)
    {
        var corte = Directory.CreateDirectory(Path.Combine(pasta, "corte")).FullName;
        Directory.CreateDirectory(Path.Combine(corte, "nestings"));
        File.WriteAllText(Path.Combine(corte, "nestings", arquivoDoNesting), DxfDoNesting(pecas, blocoAusente));
        File.WriteAllLines(Path.Combine(corte, "alvos.txt"),
            alvos.Select(a => $"{a.Alvo.Codigo} {arquivoDoNesting} {(a.Numero?.ToString() ?? "-")}").Prepend("# codigo nesting numero"));
        foreach (var (alvo, i) in alvos.Select((a, i) => (a.Alvo, i)))
            EscreverFotos(Path.Combine(corte, "alvos", alvo.Codigo), alvo, i, marcadorNaFotoComMarcador);
    }

    public static void MontarFormaFinal(string pasta, IEnumerable<ChapaSintetica> alvos, IEnumerable<(string Nome, byte[] Stl)> distratores,
        bool marcadorNaFotoComMarcador = true)
    {
        var raiz = Directory.CreateDirectory(Path.Combine(pasta, "forma-final")).FullName;
        var lista = alvos.ToList();
        foreach (var (chapa, i) in lista.Select((c, i) => (c, i)))
        {
            var dir = Path.Combine(raiz, "alvos", chapa.Codigo);
            EscreverFotos(dir, chapa, i, marcadorNaFotoComMarcador);
            File.WriteAllBytes(Path.Combine(dir, "solido.stl"), StlDaChapa(chapa));
        }
        File.WriteAllLines(Path.Combine(raiz, "tipos.txt"),
            lista.Select((c, i) => $"{c.Codigo} {TiposEmRodizio[i % TiposEmRodizio.Length]}"));

        var dirDistratores = Directory.CreateDirectory(Path.Combine(raiz, "distratores")).FullName;
        foreach (var (nome, stl) in distratores)
            File.WriteAllBytes(Path.Combine(dirDistratores, nome), stl);
    }

    public static void MontarIntermediarias(string pasta, IEnumerable<(ChapaSintetica Chapa, byte[] Stl)> alvos)
    {
        var raiz = Directory.CreateDirectory(Path.Combine(pasta, "intermediarias")).FullName;
        foreach (var (item, i) in alvos.Select((a, i) => (a, i)))
        {
            var dir = Path.Combine(raiz, "alvos", item.Chapa.Codigo);
            EscreverFotos(dir, item.Chapa, i, marcadorNoCom: true);
            File.WriteAllBytes(Path.Combine(dir, "solido.stl"), item.Stl);
        }
    }

    public static byte[] StlDaChapa(ChapaSintetica chapa) =>
        MalhaSintetica.StlBinario(MalhaSintetica.ChapaComFuro(chapa.Largura, chapa.Altura, 3, chapa.LadoDoFuro));

    public static string DxfDoNesting(IReadOnlyList<PecaDoDxf> pecas, string? blocoAusente = null)
    {
        const double topo = 500, folga = 30;
        var dxf = new DxfSintetico();
        double x = 0;
        foreach (var peca in pecas)
        {
            var y = topo - peca.Altura;
            dxf.Polilinha(Sentido(DxfSintetico.Retangulo(x, y, peca.Largura, peca.Altura), peca.SentidoHorario), fechada: true);
            if (peca.LadoDoFuro > 0)
                dxf.Polilinha(Sentido(DxfSintetico.Retangulo(
                    x + (peca.Largura - peca.LadoDoFuro) / 2, y + (peca.Altura - peca.LadoDoFuro) / 2, peca.LadoDoFuro, peca.LadoDoFuro),
                    peca.SentidoHorario), fechada: true);
            x += peca.Largura + folga;
        }
        if (blocoAusente is not null) dxf.Insert(blocoAusente, 0, 0);
        return dxf.Gerar();
    }

    private static (double X, double Y, double Bulge)[] Sentido((double X, double Y, double Bulge)[] vertices, bool horario) =>
        horario ? vertices.Reverse().ToArray() : vertices;

    private static void EscreverFotos(string dir, ChapaSintetica chapa, int indice, bool marcadorNoCom)
    {
        Directory.CreateDirectory(dir);
        using (var sem = Fotografar(chapa, comMarcador: false, anguloGraus: 15 + 40 * indice))
            Cv2.ImWrite(Path.Combine(dir, "sem-marcador.png"), sem);
        using (var com = Fotografar(chapa, comMarcador: marcadorNoCom, anguloGraus: 70 + 25 * indice))
            Cv2.ImWrite(Path.Combine(dir, "com-marcador.png"), com);
    }

    /// <summary>A chapa no CENTRO da cena, (200, 150) mm: é onde o recorte pelo centro a procura.</summary>
    private static Mat Fotografar(ChapaSintetica chapa, bool comMarcador, double anguloGraus)
    {
        using var cena = new Mat((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, Fundo);
        if (comMarcador)
            CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), chapa.Largura, chapa.Altura, chapa.LadoDoFuro,
            anguloGraus, PxPorMm, Scalar.All(50), Fundo);
        return CenaSintetica.EmPerspectiva(cena, Fundo);
    }
}
