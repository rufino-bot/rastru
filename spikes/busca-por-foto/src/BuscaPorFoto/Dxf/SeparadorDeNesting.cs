using System.Runtime.InteropServices;
using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Silhueta;
using OpenCvSharp;

namespace BuscaPorFoto.Dxf;

public sealed record PecaDoNesting(int Numero, Mascara Silhueta, int Furos, Rect2d CaixaMm, AssinaturaGeometrica Assinatura);

public sealed record NestingSeparado(IReadOnlyList<PecaDoNesting> Pecas, IReadOnlyList<string> Avisos);

/// <summary>
/// Do conjunto de trechos de um nesting às peças: emenda as pontas, acha os contornos fechados,
/// decide quem é borda de peça, furo ou borda da chapa pelo aninhamento, e rasteriza cada peça.
/// O que não se separa é AVISADO — nunca calado.
/// </summary>
public static class SeparadorDeNesting
{
    public const double ToleranciaDeEmendaMm = 0.01;
    public const double MmPorPixel = ProjetorDeSilhueta.MmPorPixel;

    private const int MargemPx = 2;
    private const double EmpateDaNumeracaoMm = 1.0;

    public static NestingSeparado Separar(DesenhoDxf desenho, IReadOnlySet<string> camadasIgnoradas)
    {
        var avisos = new List<string>(desenho.Avisos);
        var trechos = desenho.Trechos.Where(t => !camadasIgnoradas.Contains(t.Camada)).ToList();

        var contornos = ContornosFechados(trechos, avisos);
        var profundidade = Aninhar(contornos, avisos);

        var pecas = new List<(Contorno Borda, List<Contorno> Furos)>();
        var n = contornos.Count;
        for (var i = 0; i < n; i++)
        {
            if (profundidade[i] < 0 || profundidade[i] % 2 != 0) continue;
            var furos = new List<Contorno>();
            for (var j = 0; j < n; j++)
                if (profundidade[j] == profundidade[i] + 1 && Contem(contornos[i], contornos[j]))
                    furos.Add(contornos[j]);
            pecas.Add((contornos[i], furos));
        }

        var ordenadas = Numerar(pecas);
        var resultado = new List<PecaDoNesting>();
        var numero = 0;
        foreach (var (borda, furos) in ordenadas)
        {
            var mascara = Rasterizar(borda, furos);
            if (mascara is null)
            {
                avisos.Add("peça menor que um pixel ignorada");
                continue;
            }
            numero++;
            var caixa = new Rect2d(borda.MinX, borda.MinY, borda.MaxX - borda.MinX, borda.MaxY - borda.MinY);
            resultado.Add(new PecaDoNesting(numero, mascara, furos.Count, caixa, Assinar(borda, furos)));
        }
        return new NestingSeparado(resultado, avisos);
    }

    // ---- contornos ----

    private sealed class Contorno
    {
        public Contorno(List<(double X, double Y)> pontos)
        {
            Pontos = pontos;
            var n = pontos.Count;
            double soma = 0, perimetro = 0;
            MinX = MinY = double.MaxValue;
            MaxX = MaxY = double.MinValue;
            for (var i = 0; i < n; i++)
            {
                var (x0, y0) = pontos[i];
                var (x1, y1) = pontos[(i + 1) % n];
                soma += x0 * y1 - x1 * y0;
                perimetro += Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                MinX = Math.Min(MinX, x0); MaxX = Math.Max(MaxX, x0);
                MinY = Math.Min(MinY, y0); MaxY = Math.Max(MaxY, y0);
            }
            Area = Math.Abs(soma) / 2;
            Perimetro = perimetro;
        }

        public List<(double X, double Y)> Pontos { get; }
        public double Area { get; }
        public double Perimetro { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
    }

    private readonly record struct Aresta(int A, int B);

    private static List<Contorno> ContornosFechados(List<Trecho> trechos, List<string> avisos)
    {
        // Nós: pontas a até a tolerância são o mesmo. Busca na célula da ponta e nas 8 vizinhas: o
        // arredondamento simples separaria duas pontas próximas que caem em células vizinhas.
        var nos = new List<(double X, double Y)>();
        var grade = new Dictionary<(long, long), List<int>>();
        var tol = ToleranciaDeEmendaMm;

        int No(double x, double y)
        {
            var cx = (long)Math.Floor(x / tol);
            var cy = (long)Math.Floor(y / tol);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!grade.TryGetValue((cx + dx, cy + dy), out var lista)) continue;
                foreach (var idx in lista)
                {
                    var (nx, ny) = nos[idx];
                    if ((nx - x) * (nx - x) + (ny - y) * (ny - y) <= tol * tol) return idx;
                }
            }
            nos.Add((x, y));
            if (!grade.TryGetValue((cx, cy), out var propria)) grade[(cx, cy)] = propria = [];
            propria.Add(nos.Count - 1);
            return nos.Count - 1;
        }

        var arestas = new List<Aresta>();
        foreach (var t in trechos)
        {
            var a = No(t.A.X, t.A.Y);
            var b = No(t.B.X, t.B.Y);
            if (a != b) arestas.Add(new Aresta(a, b)); // comprimento zero sai
        }

        var nNos = nos.Count;
        var grau = new int[nNos];
        var pai = Enumerable.Range(0, nNos).ToArray();
        int Raiz(int x)
        {
            while (pai[x] != x) { pai[x] = pai[pai[x]]; x = pai[x]; }
            return x;
        }
        foreach (var (a, b) in arestas)
        {
            grau[a]++; grau[b]++;
            pai[Raiz(a)] = Raiz(b);
        }

        var porComponente = new SortedDictionary<int, List<int>>(); // raiz → arestas; ordem pela primeira aresta não importa, só ser estável
        var nAresta = arestas.Count;
        for (var e = 0; e < nAresta; e++)
        {
            var raiz = Raiz(arestas[e].A);
            if (!porComponente.TryGetValue(raiz, out var lista)) porComponente[raiz] = lista = [];
            lista.Add(e);
        }

        var adjacencia = new List<(int Aresta, int Outro)>[nNos];
        for (var i = 0; i < nNos; i++) adjacencia[i] = [];
        for (var e = 0; e < nAresta; e++)
        {
            adjacencia[arestas[e].A].Add((e, arestas[e].B));
            adjacencia[arestas[e].B].Add((e, arestas[e].A));
        }

        int abertos = 0, nosComuns = 0, degenerados = 0;
        var contornos = new List<Contorno>();
        foreach (var (_, es) in porComponente)
        {
            var nosDoComponente = es.SelectMany(e => new[] { arestas[e].A, arestas[e].B }).Distinct().ToList();
            var comuns = nosDoComponente.Count(x => grau[x] >= 3);
            if (comuns > 0) { nosComuns += comuns; continue; }
            if (nosDoComponente.Any(x => grau[x] == 1)) { abertos += es.Count; continue; }

            // Todo nó tem grau 2: percorre o ciclo.
            var pontos = new List<(double X, double Y)>();
            var atual = nosDoComponente[0];
            var arestaAnterior = -1;
            do
            {
                pontos.Add(nos[atual]);
                var (aresta, outro) = adjacencia[atual].First(x => x.Aresta != arestaAnterior);
                arestaAnterior = aresta;
                atual = outro;
            } while (atual != nosDoComponente[0]);

            var contorno = pontos.Count >= 3 ? new Contorno(pontos) : null;
            if (contorno is null || contorno.Area < 1e-9) degenerados++;
            else contornos.Add(contorno);
        }

        if (abertos > 0) avisos.Add($"{abertos} trecho(s) aberto(s) ignorado(s)");
        if (nosComuns > 0)
            avisos.Add($"{nosComuns} nó(s) com três ou mais trechos: possível corte em linha comum; as peças desse trecho não foram separadas");
        if (degenerados > 0) avisos.Add($"{degenerados} contorno(s) degenerado(s) ignorado(s)");
        return contornos;
    }

    // ---- aninhamento ----

    private static bool Contem(Contorno externo, Contorno interno)
    {
        if (externo.Area <= interno.Area) return false;
        if (interno.MinX < externo.MinX || interno.MaxX > externo.MaxX ||
            interno.MinY < externo.MinY || interno.MaxY > externo.MaxY) return false;
        var dentro = 0;
        foreach (var (x, y) in interno.Pontos)
            if (PontoDentro(externo, x, y)) dentro++;
        return dentro * 2 > interno.Pontos.Count;
    }

    private static bool PontoDentro(Contorno c, double x, double y)
    {
        var p = c.Pontos;
        var n = p.Count;
        var dentro = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((p[i].Y > y) != (p[j].Y > y) &&
                x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>
    /// Profundidade de cada contorno (quantos outros o contêm), já sem a borda da chapa; −1 no que saiu.
    /// A borda da chapa é o contorno de profundidade 0 que contém todos os outros E tem ao menos duas
    /// filhas diretas: uma só filha é o furo de uma peça (um retângulo com um círculo, ou uma peça
    /// com um furo e uma peça menor dentro dele), e a geometria sozinha não distingue isso de uma
    /// chapa com uma peça só.
    /// </summary>
    private static int[] Aninhar(List<Contorno> contornos, List<string> avisos)
    {
        var n = contornos.Count;
        var contem = new bool[n, n];
        var profundidade = new int[n];
        for (var j = 0; j < n; j++)
        for (var i = 0; i < n; i++)
        {
            if (i == j || !Contem(contornos[j], contornos[i])) continue;
            contem[j, i] = true;
            profundidade[i]++;
        }

        for (var j = 0; j < n; j++)
        {
            if (profundidade[j] != 0 || n < 2) continue;
            var contemTodos = true;
            var filhasDiretas = 0;
            for (var i = 0; i < n; i++)
            {
                if (i == j) continue;
                if (!contem[j, i]) { contemTodos = false; break; }
                if (profundidade[i] == 1) filhasDiretas++;
            }
            if (!contemTodos || filhasDiretas < 2) continue;

            for (var i = 0; i < n; i++) profundidade[i]--; // j vira −1; os demais descem 1
            avisos.Add("borda da chapa removida");
            break;
        }
        return profundidade;
    }

    // ---- numeração ----

    private static List<(Contorno Borda, List<Contorno> Furos)> Numerar(List<(Contorno Borda, List<Contorno> Furos)> pecas)
    {
        // De cima para baixo pela borda de cima da caixa (o y do DXF cresce para cima); dentro de 1 mm
        // do topo da primeira peça da linha, da esquerda para a direita.
        var porTopo = pecas
            .OrderByDescending(p => p.Borda.MaxY)
            .ThenBy(p => p.Borda.MinX)
            .ThenBy(p => p.Borda.Area)
            .ToList();
        var saida = new List<(Contorno, List<Contorno>)>();
        var n = porTopo.Count;
        var i = 0;
        while (i < n)
        {
            var topo = porTopo[i].Borda.MaxY;
            var linha = new List<(Contorno Borda, List<Contorno> Furos)>();
            while (i < n && topo - porTopo[i].Borda.MaxY <= EmpateDaNumeracaoMm) linha.Add(porTopo[i++]);
            saida.AddRange(linha.OrderBy(p => p.Borda.MinX).ThenBy(p => p.Borda.Area));
        }
        return saida;
    }

    // ---- silhueta ----

    /// <summary>
    /// Preenche pelo centro do pixel (par-ímpar sobre a borda e os furos juntos): sem o viés de
    /// meio pixel por lado que o FillPoly do OpenCV põe, que em peça pequena passa de 2% da área.
    /// A imagem sai com o y invertido, como no CAD.
    /// </summary>
    private static Mascara? Rasterizar(Contorno borda, List<Contorno> furos)
    {
        var largura = (int)Math.Ceiling((borda.MaxX - borda.MinX) / MmPorPixel) + 2 * MargemPx;
        var altura = (int)Math.Ceiling((borda.MaxY - borda.MinY) / MmPorPixel) + 2 * MargemPx;

        var arestas = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (var c in furos.Prepend(borda))
        {
            var p = c.Pontos;
            var n = p.Count;
            for (var i = 0; i < n; i++)
            {
                var (xa, ya) = p[i];
                var (xb, yb) = p[(i + 1) % n];
                arestas.Add((
                    (xa - borda.MinX) / MmPorPixel + MargemPx, (borda.MaxY - ya) / MmPorPixel + MargemPx,
                    (xb - borda.MinX) / MmPorPixel + MargemPx, (borda.MaxY - yb) / MmPorPixel + MargemPx));
            }
        }

        var buffer = new byte[largura * altura];
        var cruzamentos = new List<double>();
        for (var linha = 0; linha < altura; linha++)
        {
            var yc = linha + 0.5;
            cruzamentos.Clear();
            foreach (var (x0, y0, x1, y1) in arestas)
            {
                if ((y0 <= yc) == (y1 <= yc)) continue;
                cruzamentos.Add(x0 + (yc - y0) * (x1 - x0) / (y1 - y0));
            }
            cruzamentos.Sort();
            var nc = cruzamentos.Count;
            for (var k = 0; k + 1 < nc; k += 2)
            {
                var de = Math.Max(0, (int)Math.Ceiling(cruzamentos[k] - 0.5));
                var ate = Math.Min(largura, (int)Math.Ceiling(cruzamentos[k + 1] - 0.5));
                for (var col = de; col < ate; col++) buffer[linha * largura + col] = 255;
            }
        }

        using var cheia = new Mat(altura, largura, MatType.CV_8UC1);
        Marshal.Copy(buffer, 0, cheia.Data, buffer.Length);
        return Mascara.DeMascaraCheia(cheia, MmPorPixel);
    }

    // ---- assinatura ----

    /// <summary>
    /// [área da região (borda menos furos), perímetro (borda e furos), soma das áreas dos furos, λ1, λ2],
    /// do polígono. λ1 ≥ λ2 são os momentos principais de segunda ordem em torno do centróide.
    /// </summary>
    private static AssinaturaGeometrica Assinar(Contorno borda, List<Contorno> furos)
    {
        var (ox, oy) = borda.Pontos[0]; // origem local: coordenadas grandes perdem precisão nos produtos
        var m = Momentos(borda, ox, oy);
        var somaFuros = 0.0;
        var perimetro = borda.Perimetro;
        foreach (var f in furos)
        {
            var mf = Momentos(f, ox, oy);
            m = (m.A - mf.A, m.Sx - mf.Sx, m.Sy - mf.Sy, m.Ixx - mf.Ixx, m.Iyy - mf.Iyy, m.Ixy - mf.Ixy);
            somaFuros += f.Area;
            perimetro += f.Perimetro;
        }

        var cx = m.Sx / m.A;
        var cy = m.Sy / m.A;
        var sxx = m.Iyy - m.A * cx * cx;
        var syy = m.Ixx - m.A * cy * cy;
        var sxy = m.Ixy - m.A * cx * cy;
        var meio = (sxx + syy) / 2;
        var raio = Math.Sqrt((sxx - syy) * (sxx - syy) / 4 + sxy * sxy);
        return new AssinaturaGeometrica(furos.Count, [m.A, perimetro, somaFuros, meio + raio, meio - raio]);
    }

    /// <summary>Área, ∫x, ∫y, ∫y², ∫x² e ∫xy do polígono, orientado como positivo.</summary>
    private static (double A, double Sx, double Sy, double Ixx, double Iyy, double Ixy) Momentos(Contorno c, double ox, double oy)
    {
        double a = 0, sx = 0, sy = 0, ixx = 0, iyy = 0, ixy = 0;
        var p = c.Pontos;
        var n = p.Count;
        for (var i = 0; i < n; i++)
        {
            var x0 = p[i].X - ox;
            var y0 = p[i].Y - oy;
            var x1 = p[(i + 1) % n].X - ox;
            var y1 = p[(i + 1) % n].Y - oy;
            var cr = x0 * y1 - x1 * y0;
            a += cr;
            sx += (x0 + x1) * cr;
            sy += (y0 + y1) * cr;
            ixx += (y0 * y0 + y0 * y1 + y1 * y1) * cr;
            iyy += (x0 * x0 + x0 * x1 + x1 * x1) * cr;
            ixy += (x0 * y1 + 2 * x0 * y0 + 2 * x1 * y1 + x1 * y0) * cr;
        }
        var sinal = a < 0 ? -1.0 : 1.0;
        return (sinal * a / 2, sinal * sx / 6, sinal * sy / 6, sinal * ixx / 12, sinal * iyy / 12, sinal * ixy / 24);
    }
}
