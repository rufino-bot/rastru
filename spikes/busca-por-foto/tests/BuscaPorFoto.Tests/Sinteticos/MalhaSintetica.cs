using System.Globalization;
using System.Numerics;
using System.Text;
using BuscaPorFoto.Stl;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <summary>Malhas geradas em código, em milímetros, e a serialização delas em STL.</summary>
public static class MalhaSintetica
{
    public static List<Triangulo> Caixa(float x, float y, float z)
    {
        var p = new Vector3[]
        {
            new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0),
            new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z),
        };
        int[][] faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return faces.SelectMany(f => Quad(p[f[0]], p[f[1]], p[f[2]], p[f[3]])).ToList();
    }

    /// <summary>Chapa <paramref name="largura"/> × <paramref name="altura"/> × <paramref name="espessura"/> com um furo quadrado passante no centro.</summary>
    public static List<Triangulo> ChapaComFuro(float largura, float altura, float espessura, float ladoDoFuro)
    {
        var tris = new List<Triangulo>();
        float fx0 = (largura - ladoDoFuro) / 2, fx1 = fx0 + ladoDoFuro;
        float fy0 = (altura - ladoDoFuro) / 2, fy1 = fy0 + ladoDoFuro;

        foreach (var z in new[] { 0f, espessura })
        {
            Vector3 P(float x, float y) => new(x, y, z);
            // Quatro trapézios em volta do furo.
            tris.AddRange(Quad(P(0, 0), P(largura, 0), P(fx1, fy0), P(fx0, fy0)));
            tris.AddRange(Quad(P(largura, 0), P(largura, altura), P(fx1, fy1), P(fx1, fy0)));
            tris.AddRange(Quad(P(largura, altura), P(0, altura), P(fx0, fy1), P(fx1, fy1)));
            tris.AddRange(Quad(P(0, altura), P(0, 0), P(fx0, fy0), P(fx0, fy1)));
        }

        void Parede(float x0, float y0, float x1, float y1) =>
            tris.AddRange(Quad(new(x0, y0, 0), new(x1, y1, 0), new(x1, y1, espessura), new(x0, y0, espessura)));
        Parede(0, 0, largura, 0); Parede(largura, 0, largura, altura); Parede(largura, altura, 0, altura); Parede(0, altura, 0, 0);
        Parede(fx0, fy0, fx1, fy0); Parede(fx1, fy0, fx1, fy1); Parede(fx1, fy1, fx0, fy1); Parede(fx0, fy1, fx0, fy0);
        return tris;
    }

    public static List<Triangulo> Transformar(IEnumerable<Triangulo> malha, Matrix4x4 m) =>
        malha.Select(t => new Triangulo(Vector3.Transform(t.A, m), Vector3.Transform(t.B, m), Vector3.Transform(t.C, m))).ToList();

    public static byte[] StlBinario(IReadOnlyList<Triangulo> malha, string cabecalho = "solid exportado por um CAD")
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var cab = new byte[80];
        Encoding.ASCII.GetBytes(cabecalho).CopyTo(cab, 0);
        w.Write(cab);
        w.Write((uint)malha.Count);
        foreach (var t in malha)
        {
            for (var i = 0; i < 3; i++) w.Write(0f);
            foreach (var v in new[] { t.A, t.B, t.C }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            w.Write((ushort)0);
        }
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] StlAscii(IReadOnlyList<Triangulo> malha)
    {
        var s = new StringBuilder("solid sintetico\n");
        foreach (var t in malha)
        {
            s.Append("  facet normal 0 0 0\n    outer loop\n");
            foreach (var v in new[] { t.A, t.B, t.C })
                s.Append(CultureInfo.InvariantCulture, $"      vertex {v.X} {v.Y} {v.Z}\n");
            s.Append("    endloop\n  endfacet\n");
        }
        s.Append("endsolid sintetico\n");
        return Encoding.ASCII.GetBytes(s.ToString());
    }

    private static IEnumerable<Triangulo> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        yield return new Triangulo(a, b, c);
        yield return new Triangulo(a, c, d);
    }
}
