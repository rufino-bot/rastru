using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace BuscaPorFoto.Stl;

public readonly record struct Triangulo(Vector3 A, Vector3 B, Vector3 C);

public static class LeitorDeStl
{
    private const int TamanhoDoCabecalho = 80;
    private const int TamanhoDoTriangulo = 50;

    /// <summary>
    /// Lê STL binário ou ASCII. Decide pelo tamanho, não pelo prefixo "solid": exportadores de CAD
    /// escrevem "solid" no cabeçalho do binário também.
    /// </summary>
    public static IReadOnlyList<Triangulo> Ler(byte[] conteudo)
    {
        if (conteudo.Length >= TamanhoDoCabecalho + 4)
        {
            var quantidade = BinaryPrimitives.ReadUInt32LittleEndian(conteudo.AsSpan(TamanhoDoCabecalho));
            if (TamanhoDoCabecalho + 4L + TamanhoDoTriangulo * (long)quantidade == conteudo.Length)
                return LerBinario(conteudo, (int)quantidade);
        }
        return LerAscii(Encoding.ASCII.GetString(conteudo));
    }

    private static List<Triangulo> LerBinario(byte[] conteudo, int quantidade)
    {
        var triangulos = new List<Triangulo>(quantidade);
        for (var i = 0; i < quantidade; i++)
        {
            // 12 bytes de normal, que o spike não usa, depois os três vértices.
            var inicio = TamanhoDoCabecalho + 4 + i * TamanhoDoTriangulo + 12;
            triangulos.Add(new Triangulo(
                LerVetor(conteudo, inicio), LerVetor(conteudo, inicio + 12), LerVetor(conteudo, inicio + 24)));
        }
        return triangulos;
    }

    private static Vector3 LerVetor(byte[] b, int inicio) => new(
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio)),
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio + 8)));

    private static List<Triangulo> LerAscii(string texto)
    {
        var vertices = new List<Vector3>();
        foreach (var linhaCrua in texto.Split('\n'))
        {
            var partes = linhaCrua.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 4 && partes[0] == "vertex")
                vertices.Add(new Vector3(
                    float.Parse(partes[1], CultureInfo.InvariantCulture),
                    float.Parse(partes[2], CultureInfo.InvariantCulture),
                    float.Parse(partes[3], CultureInfo.InvariantCulture)));
        }

        if (vertices.Count == 0 || vertices.Count % 3 != 0)
            throw new FormatException(
                $"STL inválido: não é binário de tamanho consistente e o ASCII tem {vertices.Count} vértices.");

        var triangulos = new List<Triangulo>(vertices.Count / 3);
        for (var i = 0; i < vertices.Count; i += 3)
            triangulos.Add(new Triangulo(vertices[i], vertices[i + 1], vertices[i + 2]));
        return triangulos;
    }
}
