using System.Globalization;
using System.Numerics;

namespace BuscaPorFoto.Dxf;

/// <summary>Um segmento reto do desenho, em mm, na camada de origem.</summary>
public readonly record struct Trecho(Vector2 A, Vector2 B, string Camada);

public sealed record DesenhoDxf(IReadOnlyList<Trecho> Trechos, IReadOnlyList<string> Avisos);

/// <summary>
/// Lê DXF ASCII e devolve só segmentos retos em mm: arcos, círculos e bulges viram trechos já na
/// leitura (antes de qualquer transformação, para o espelho de um bloco não inverter o sentido de
/// um arco), e cada INSERT é expandido. O que não se lê é AVISADO, nunca calado — exceto TEXT e MTEXT,
/// que são anotação.
/// </summary>
public static class LeitorDeDxf
{
    public const double ErroMaximoDaCordaMm = 0.05;
    public const int ProfundidadeMaximaDeBloco = 8;

    private const int LimiteDeTrechosPorArco = 100_000;

    public static DesenhoDxf Ler(string texto)
    {
        var pares = LerPares(texto);
        var secoes = SepararSecoes(pares);
        if (!secoes.TryGetValue("ENTITIES", out var paresEntidades))
            throw new FormatException("DXF sem seção ENTITIES.");

        var avisos = new List<string>();
        var fator = FatorDeUnidade(secoes.GetValueOrDefault("HEADER"), avisos);
        var erroNoDesenho = ErroMaximoDaCordaMm / fator;

        var blocos = new Dictionary<string, Bloco>(StringComparer.Ordinal);
        if (secoes.TryGetValue("BLOCKS", out var paresBlocos))
            LerBlocos(paresBlocos, erroNoDesenho, blocos);

        var itens = Converter(Brutas(paresEntidades), erroNoDesenho);
        var trechos = new List<Trecho>();
        var naoLidos = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var avisosDeBloco = new List<string>();
        Expandir(itens, Afim.Identidade, 0, blocos, fator, trechos, naoLidos, avisosDeBloco);

        avisos.AddRange(avisosDeBloco);
        foreach (var (tipo, n) in naoLidos)
            avisos.Add($"{n} entidade(s) {tipo} não lida(s)");
        return new DesenhoDxf(trechos, avisos);
    }

    // ---- pares (código de grupo, valor) ----

    private readonly record struct Par(int Codigo, string Valor);

    private static List<Par> LerPares(string texto)
    {
        var limpo = texto.TrimStart('﻿');
        if (limpo.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal))
            throw new FormatException("DXF binário não é suportado: salve como DXF ASCII.");

        var linhas = limpo.Split('\n');
        var pares = new List<Par>(linhas.Length / 2);
        for (var i = 0; i + 1 < linhas.Length; i += 2)
        {
            if (!int.TryParse(linhas[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var codigo))
                throw new FormatException($"Código de grupo inválido na linha {i + 1} do DXF.");
            pares.Add(new Par(codigo, linhas[i + 1].Trim()));
        }
        return pares;
    }

    private static Dictionary<string, List<Par>> SepararSecoes(List<Par> pares)
    {
        var secoes = new Dictionary<string, List<Par>>(StringComparer.Ordinal);
        var n = pares.Count;
        for (var i = 0; i < n; i++)
        {
            if (pares[i] is not { Codigo: 0, Valor: "SECTION" }) continue;
            if (i + 1 >= n || pares[i + 1].Codigo != 2) throw new FormatException("SECTION sem nome.");
            var nome = pares[i + 1].Valor;
            var corpo = new List<Par>();
            i += 2;
            while (i < n && pares[i] is not { Codigo: 0, Valor: "ENDSEC" })
                corpo.Add(pares[i++]);
            secoes[nome] = corpo;
        }
        return secoes;
    }

    private static double FatorDeUnidade(List<Par>? header, List<string> avisos)
    {
        int? unidade = null;
        if (header is not null)
        {
            var n = header.Count;
            for (var i = 0; i + 1 < n; i++)
            {
                if (header[i] is { Codigo: 9, Valor: "$INSUNITS" } && header[i + 1].Codigo == 70)
                    unidade = (int)Num(header[i + 1].Valor);
            }
        }

        switch (unidade)
        {
            case 1: return 25.4;
            case 4: return 1;
            case 5: return 10;
            case 6: return 1000;
            case null or 0:
                avisos.Add("sem $INSUNITS: assumido milímetro");
                return 1;
            default:
                avisos.Add($"unidade {unidade} não suportada: assumido milímetro");
                return 1;
        }
    }

    private static double Num(string valor)
    {
        if (!double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw new FormatException($"Número inválido no DXF: '{valor}'.");
        return d;
    }

    // ---- entidades brutas ----

    private sealed class Bruta(string tipo)
    {
        public string Tipo { get; } = tipo;
        public List<Par> Grupos { get; } = [];

        public double Num(int codigo, double padrao = 0)
        {
            foreach (var g in Grupos)
                if (g.Codigo == codigo) return LeitorDeDxf.Num(g.Valor);
            return padrao;
        }

        public string Txt(int codigo, string padrao)
        {
            foreach (var g in Grupos)
                if (g.Codigo == codigo) return g.Valor;
            return padrao;
        }
    }

    private static List<Bruta> Brutas(List<Par> pares)
    {
        var brutas = new List<Bruta>();
        Bruta? atual = null;
        foreach (var par in pares)
        {
            if (par.Codigo == 0)
            {
                atual = new Bruta(par.Valor);
                brutas.Add(atual);
            }
            else atual?.Grupos.Add(par);
        }
        return brutas;
    }

    // ---- itens: traços já em segmentos, inserções e o que não se lê ----

    private readonly record struct Seg(double X1, double Y1, double X2, double Y2);

    private abstract record Item;
    private sealed record Tracos(string Camada, List<Seg> Segmentos) : Item;
    private sealed record Insercao(string Nome, double X, double Y, double EscalaX, double EscalaY, double RotacaoGraus) : Item;
    private sealed record NaoLido(string Tipo) : Item;

    private sealed record Bloco(double BaseX, double BaseY, List<Item> Itens);

    private static void LerBlocos(List<Par> pares, double erro, Dictionary<string, Bloco> blocos)
    {
        var brutas = Brutas(pares);
        var n = brutas.Count;
        for (var i = 0; i < n; i++)
        {
            if (brutas[i].Tipo != "BLOCK") continue;
            var cabecalho = brutas[i];
            var interno = new List<Bruta>();
            i++;
            while (i < n && brutas[i].Tipo != "ENDBLK") interno.Add(brutas[i++]);
            blocos[cabecalho.Txt(2, "")] = new Bloco(cabecalho.Num(10), cabecalho.Num(20), Converter(interno, erro));
        }
    }

    private static List<Item> Converter(List<Bruta> brutas, double erro)
    {
        var itens = new List<Item>();
        var n = brutas.Count;
        for (var i = 0; i < n; i++)
        {
            var b = brutas[i];
            var camada = b.Txt(8, "0");
            switch (b.Tipo)
            {
                case "TEXT" or "MTEXT":
                    break;
                case "LINE":
                    itens.Add(new Tracos(camada, [new Seg(b.Num(10), b.Num(20), b.Num(11), b.Num(21))]));
                    break;
                case "CIRCLE":
                {
                    var r = b.Num(40);
                    if (r <= 0) { itens.Add(new NaoLido("CIRCLE")); break; }
                    var segs = new List<Seg>();
                    AdicionarArco(segs, b.Num(10), b.Num(20), r, 0, 2 * Math.PI, erro, fechado: true);
                    itens.Add(new Tracos(camada, segs));
                    break;
                }
                case "ARC":
                {
                    var r = b.Num(40);
                    if (r <= 0) { itens.Add(new NaoLido("ARC")); break; }
                    var a0 = b.Num(50) * Math.PI / 180;
                    var delta = (b.Num(51) - b.Num(50)) * Math.PI / 180;
                    while (delta <= 0) delta += 2 * Math.PI;
                    var segs = new List<Seg>();
                    AdicionarArco(segs, b.Num(10), b.Num(20), r, a0, delta, erro, fechado: false);
                    itens.Add(new Tracos(camada, segs));
                    break;
                }
                case "LWPOLYLINE":
                    itens.Add(new Tracos(camada, PolilinhaLeve(b, erro)));
                    break;
                case "POLYLINE":
                {
                    var vertices = new List<Bruta>();
                    while (i + 1 < n && brutas[i + 1].Tipo == "VERTEX") vertices.Add(brutas[++i]);
                    if (i + 1 < n && brutas[i + 1].Tipo == "SEQEND") i++;
                    var bandeiras = (int)b.Num(70);
                    if ((bandeiras & (16 | 64)) != 0) { itens.Add(new NaoLido("POLYLINE")); break; }
                    var pontos = vertices.Select(v => (v.Num(10), v.Num(20), v.Num(42))).ToList();
                    itens.Add(new Tracos(camada, SegmentosDeVertices(pontos, (bandeiras & 1) != 0, erro)));
                    break;
                }
                case "INSERT":
                    itens.Add(new Insercao(b.Txt(2, ""), b.Num(10), b.Num(20), b.Num(41, 1), b.Num(42, 1), b.Num(50)));
                    break;
                default:
                    itens.Add(new NaoLido(b.Tipo));
                    break;
            }
        }
        return itens;
    }

    private static List<Seg> PolilinhaLeve(Bruta b, double erro)
    {
        var vertices = new List<(double X, double Y, double Bulge)>();
        foreach (var g in b.Grupos)
        {
            switch (g.Codigo)
            {
                case 10: vertices.Add((Num(g.Valor), 0, 0)); break;
                case 20 when vertices.Count > 0:
                    vertices[^1] = vertices[^1] with { Y = Num(g.Valor) };
                    break;
                case 42 when vertices.Count > 0:
                    vertices[^1] = vertices[^1] with { Bulge = Num(g.Valor) };
                    break;
            }
        }
        return SegmentosDeVertices(vertices, ((int)b.Num(70) & 1) != 0, erro);
    }

    private static List<Seg> SegmentosDeVertices(List<(double X, double Y, double Bulge)> v, bool fechada, double erro)
    {
        var segs = new List<Seg>();
        var n = v.Count;
        var ate = fechada ? n : n - 1;
        for (var i = 0; i < ate; i++)
        {
            var p = v[i];
            var q = v[(i + 1) % n];
            if (p.Bulge == 0) { segs.Add(new Seg(p.X, p.Y, q.X, q.Y)); continue; }

            // Bulge b = tan(θ/4): θ é o ângulo do arco, positivo no sentido anti-horário, de p para q.
            var dx = q.X - p.X;
            var dy = q.Y - p.Y;
            var corda = Math.Sqrt(dx * dx + dy * dy);
            if (corda == 0) continue;
            var theta = 4 * Math.Atan(p.Bulge);
            var raio = Math.Abs(corda / (2 * Math.Sin(theta / 2)));
            var h = corda / 2 / Math.Tan(theta / 2); // distância do centro à corda, à esquerda de p→q
            var cx = (p.X + q.X) / 2 - dy / corda * h;
            var cy = (p.Y + q.Y) / 2 + dx / corda * h;
            AdicionarArco(segs, cx, cy, raio, Math.Atan2(p.Y - cy, p.X - cx), theta, erro, fechado: false);
        }
        return segs;
    }

    /// <summary>
    /// n = ⌈|Δθ| / (2·acos(1 − e/r))⌉, e no mínimo 8 num círculo inteiro. Δθ pode ser negativo
    /// (bulge horário).
    /// </summary>
    private static void AdicionarArco(List<Seg> segs, double cx, double cy, double r, double a0, double delta, double erro, bool fechado)
    {
        var passo = Math.Max(2 * Math.Acos(Math.Clamp(1 - erro / r, -1, 1)), 1e-4);
        var n = (int)Math.Ceiling(Math.Abs(delta) / passo);
        n = Math.Max(n, (int)Math.Ceiling(8 * Math.Abs(delta) / (2 * Math.PI)));
        n = Math.Clamp(n, 1, LimiteDeTrechosPorArco);

        var (px, py) = (cx + r * Math.Cos(a0), cy + r * Math.Sin(a0));
        var (x0, y0) = (px, py);
        for (var k = 1; k <= n; k++)
        {
            var a = a0 + delta * k / n;
            var (qx, qy) = fechado && k == n ? (x0, y0) : (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            segs.Add(new Seg(px, py, qx, qy));
            (px, py) = (qx, qy);
        }
    }

    // ---- expansão de blocos ----

    /// <summary>x' = a·x + c·y + tx, y' = b·x + d·y + ty.</summary>
    private readonly record struct Afim(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static Afim Identidade => new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Aplicar(double x, double y) => (A * x + C * y + Tx, B * x + D * y + Ty);

        /// <summary>externa(interna(p)).</summary>
        public static Afim Compor(Afim externa, Afim interna)
        {
            var (tx, ty) = externa.Aplicar(interna.Tx, interna.Ty);
            return new Afim(
                externa.A * interna.A + externa.C * interna.B,
                externa.B * interna.A + externa.D * interna.B,
                externa.A * interna.C + externa.C * interna.D,
                externa.B * interna.C + externa.D * interna.D,
                tx, ty);
        }

        /// <summary>p' = R(θ)·S·(p − base) + inserção.</summary>
        public static Afim DeInsercao(Insercao ins, Bloco bloco)
        {
            var t = ins.RotacaoGraus * Math.PI / 180;
            var (c, s) = (Math.Cos(t), Math.Sin(t));
            var m = new Afim(c * ins.EscalaX, s * ins.EscalaX, -s * ins.EscalaY, c * ins.EscalaY, 0, 0);
            var (bx, by) = m.Aplicar(bloco.BaseX, bloco.BaseY);
            return m with { Tx = ins.X - bx, Ty = ins.Y - by };
        }
    }

    private static void Expandir(
        List<Item> itens, Afim t, int profundidade, Dictionary<string, Bloco> blocos, double fator,
        List<Trecho> saida, SortedDictionary<string, int> naoLidos, List<string> avisos)
    {
        foreach (var item in itens)
        {
            switch (item)
            {
                case Tracos tracos:
                    foreach (var s in tracos.Segmentos)
                    {
                        var (ax, ay) = t.Aplicar(s.X1, s.Y1);
                        var (bx, by) = t.Aplicar(s.X2, s.Y2);
                        saida.Add(new Trecho(
                            new Vector2((float)(ax * fator), (float)(ay * fator)),
                            new Vector2((float)(bx * fator), (float)(by * fator)),
                            tracos.Camada));
                    }
                    break;
                case Insercao ins:
                    if (!blocos.TryGetValue(ins.Nome, out var bloco))
                        AvisarUmaVez(avisos, $"bloco {ins.Nome} não encontrado: INSERT ignorado");
                    else if (profundidade >= ProfundidadeMaximaDeBloco)
                        AvisarUmaVez(avisos, $"blocos aninhados além de {ProfundidadeMaximaDeBloco} níveis: INSERT de {ins.Nome} ignorado");
                    else
                        Expandir(bloco.Itens, Afim.Compor(t, Afim.DeInsercao(ins, bloco)), profundidade + 1, blocos, fator, saida, naoLidos, avisos);
                    break;
                case NaoLido nl:
                    naoLidos[nl.Tipo] = naoLidos.GetValueOrDefault(nl.Tipo) + 1;
                    break;
            }
        }
    }

    private static void AvisarUmaVez(List<string> avisos, string aviso)
    {
        if (!avisos.Contains(aviso)) avisos.Add(aviso);
    }
}
