using System.Globalization;
using System.Text;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <summary>Construtor fluente que escreve DXF ASCII, o bastante para os testes do leitor.</summary>
public sealed class DxfSintetico
{
    private int? _insunits = 4;
    private readonly StringBuilder _entidades = new();
    private readonly StringBuilder _blocos = new();

    public DxfSintetico ComUnidade(int insunits) { _insunits = insunits; return this; }

    public DxfSintetico SemUnidade() { _insunits = null; return this; }

    public DxfSintetico Linha(double x1, double y1, double x2, double y2, string camada = "0")
    {
        Par(_entidades, 0, "LINE");
        Par(_entidades, 8, camada);
        Par(_entidades, 10, x1); Par(_entidades, 20, y1);
        Par(_entidades, 11, x2); Par(_entidades, 21, y2);
        return this;
    }

    public DxfSintetico Arco(double cx, double cy, double raio, double deGraus, double ateGraus, string camada = "0")
    {
        Par(_entidades, 0, "ARC");
        Par(_entidades, 8, camada);
        Par(_entidades, 10, cx); Par(_entidades, 20, cy);
        Par(_entidades, 40, raio);
        Par(_entidades, 50, deGraus); Par(_entidades, 51, ateGraus);
        return this;
    }

    public DxfSintetico Circulo(double cx, double cy, double raio, string camada = "0")
    {
        Par(_entidades, 0, "CIRCLE");
        Par(_entidades, 8, camada);
        Par(_entidades, 10, cx); Par(_entidades, 20, cy);
        Par(_entidades, 40, raio);
        return this;
    }

    public DxfSintetico Polilinha((double X, double Y, double Bulge)[] vertices, bool fechada, string camada = "0")
    {
        Par(_entidades, 0, "LWPOLYLINE");
        Par(_entidades, 8, camada);
        Par(_entidades, 90, vertices.Length);
        Par(_entidades, 70, fechada ? 1 : 0);
        foreach (var (x, y, bulge) in vertices)
        {
            Par(_entidades, 10, x);
            Par(_entidades, 20, y);
            if (bulge != 0) Par(_entidades, 42, bulge);
        }
        return this;
    }

    public DxfSintetico PolilinhaAntiga((double X, double Y, double Bulge)[] vertices, bool fechada, string camada = "0")
    {
        Par(_entidades, 0, "POLYLINE");
        Par(_entidades, 8, camada);
        Par(_entidades, 66, 1);
        Par(_entidades, 70, fechada ? 1 : 0);
        foreach (var (x, y, bulge) in vertices)
        {
            Par(_entidades, 0, "VERTEX");
            Par(_entidades, 8, camada);
            Par(_entidades, 10, x);
            Par(_entidades, 20, y);
            if (bulge != 0) Par(_entidades, 42, bulge);
        }
        Par(_entidades, 0, "SEQEND");
        Par(_entidades, 8, camada);
        return this;
    }

    public DxfSintetico Spline()
    {
        Par(_entidades, 0, "SPLINE");
        Par(_entidades, 8, "0");
        Par(_entidades, 70, 8);
        Par(_entidades, 71, 3);
        Par(_entidades, 10, 0.0); Par(_entidades, 20, 0.0);
        Par(_entidades, 10, 10.0); Par(_entidades, 20, 10.0);
        return this;
    }

    public DxfSintetico Texto()
    {
        Par(_entidades, 0, "TEXT");
        Par(_entidades, 8, "0");
        Par(_entidades, 10, 5.0); Par(_entidades, 20, 5.0);
        Par(_entidades, 40, 2.5);
        Par(_entidades, 1, "PECA 1");
        return this;
    }

    public DxfSintetico Bloco(string nome, Action<DxfSintetico> conteudo, double baseX = 0, double baseY = 0)
    {
        var interno = new DxfSintetico();
        conteudo(interno);
        Par(_blocos, 0, "BLOCK");
        Par(_blocos, 8, "0");
        Par(_blocos, 2, nome);
        Par(_blocos, 70, 0);
        Par(_blocos, 10, baseX); Par(_blocos, 20, baseY); Par(_blocos, 30, 0.0);
        _blocos.Append(interno._entidades);
        Par(_blocos, 0, "ENDBLK");
        Par(_blocos, 8, "0");
        return this;
    }

    public DxfSintetico Insert(string nome, double x, double y, double escalaX = 1, double escalaY = 1, double rotacaoGraus = 0)
    {
        Par(_entidades, 0, "INSERT");
        Par(_entidades, 8, "0");
        Par(_entidades, 2, nome);
        Par(_entidades, 10, x); Par(_entidades, 20, y);
        Par(_entidades, 41, escalaX); Par(_entidades, 42, escalaY);
        Par(_entidades, 50, rotacaoGraus);
        return this;
    }

    /// <summary>Retângulo de (x, y) a (x + largura, y + altura), na ordem anti-horária.</summary>
    public static (double X, double Y, double Bulge)[] Retangulo(double x, double y, double largura, double altura) =>
    [
        (x, y, 0), (x + largura, y, 0), (x + largura, y + altura, 0), (x, y + altura, 0),
    ];

    /// <summary>Retângulo desenhado em quatro <see cref="Linha"/>.</summary>
    public DxfSintetico RetanguloEmLinhas(double x, double y, double largura, double altura)
    {
        Linha(x, y, x + largura, y);
        Linha(x + largura, y, x + largura, y + altura);
        Linha(x + largura, y + altura, x, y + altura);
        Linha(x, y + altura, x, y);
        return this;
    }

    public string Gerar()
    {
        var sb = new StringBuilder();
        Par(sb, 0, "SECTION"); Par(sb, 2, "HEADER");
        Par(sb, 9, "$ACADVER"); Par(sb, 1, "AC1015");
        if (_insunits is { } u) { Par(sb, 9, "$INSUNITS"); Par(sb, 70, u); }
        Par(sb, 0, "ENDSEC");
        Par(sb, 0, "SECTION"); Par(sb, 2, "BLOCKS");
        sb.Append(_blocos);
        Par(sb, 0, "ENDSEC");
        Par(sb, 0, "SECTION"); Par(sb, 2, "ENTITIES");
        sb.Append(_entidades);
        Par(sb, 0, "ENDSEC");
        Par(sb, 0, "EOF");
        return sb.ToString();
    }

    private static void Par(StringBuilder sb, int codigo, string valor) =>
        sb.Append(codigo.ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append("\r\n").Append(valor).Append("\r\n");

    private static void Par(StringBuilder sb, int codigo, double valor) =>
        Par(sb, codigo, valor.ToString("R", CultureInfo.InvariantCulture));

    private static void Par(StringBuilder sb, int codigo, int valor) =>
        Par(sb, codigo, valor.ToString(CultureInfo.InvariantCulture));
}
