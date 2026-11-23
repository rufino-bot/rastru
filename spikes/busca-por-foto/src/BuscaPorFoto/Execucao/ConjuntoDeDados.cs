namespace BuscaPorFoto.Execucao;

public enum Cenario { Corte, FormaFinal, Intermediarias }

public enum TipoDePeca { Chapa, Dobrada, Usinada, Soldada }

public static class CenarioExtensoes
{
    /// <summary>Nome da pasta do cenário na pasta de dados e valor de <c>cenario=</c> no <c>execucao.txt</c>.</summary>
    public static string NomeDaPasta(this Cenario cenario) => cenario switch
    {
        Cenario.Corte => "corte",
        Cenario.FormaFinal => "forma-final",
        Cenario.Intermediarias => "intermediarias",
        _ => throw new ArgumentOutOfRangeException(nameof(cenario)),
    };

    /// <summary>Prefixo dos ids anonimizados: A, B, I.</summary>
    public static char Prefixo(this Cenario cenario) => cenario switch
    {
        Cenario.Corte => 'A',
        Cenario.FormaFinal => 'B',
        Cenario.Intermediarias => 'I',
        _ => throw new ArgumentOutOfRangeException(nameof(cenario)),
    };

    public static Cenario? DaPasta(string nome) => nome switch
    {
        "corte" => Cenario.Corte,
        "forma-final" => Cenario.FormaFinal,
        "intermediarias" => Cenario.Intermediarias,
        _ => null,
    };
}

/// <summary>
/// Pasta de dados com arquivo ausente ou inválido. A <see cref="Exception.Message"/> traz SÓ a
/// contagem: os caminhos (que têm código de peça) vão em <see cref="Faltando"/>, para um arquivo local.
/// </summary>
public sealed class DadosIncompletosException : IOException
{
    public IReadOnlyList<string> Faltando { get; }

    public DadosIncompletosException(IReadOnlyList<string> faltando)
        : base($"{faltando.Count} arquivo(s) ou pasta(s) ausente(s) ou inválido(s)") =>
        Faltando = faltando;
}

/// <param name="Nesting">O nome do arquivo do nesting, como escrito no <c>alvos.txt</c>.</param>
/// <param name="Numero">Nulo: o alvo não saiu separado do nesting (<c>-</c> no <c>alvos.txt</c>).</param>
public sealed record AlvoDoCorte(string Codigo, string Nesting, int? Numero, string CaminhoSemMarcador, string CaminhoComMarcador);

public sealed record AlvoComStl(string Codigo, string CaminhoDoStl, TipoDePeca? Tipo, string CaminhoSemMarcador, string CaminhoComMarcador);

/// <param name="Nestings">Caminhos dos arquivos DXF, em ordem ordinal de nome de arquivo.</param>
public sealed record DadosDoCorte(IReadOnlyList<string> Nestings, IReadOnlyList<AlvoDoCorte> Alvos);

/// <param name="OutrosCandidatos">STL de candidatos que não são alvo deste cenário. Na forma final, os distratores; nas intermediárias, os alvos e os distratores da forma final.</param>
public sealed record DadosComStl(IReadOnlyList<AlvoComStl> Alvos, IReadOnlyList<string> OutrosCandidatos);

/// <summary>
/// Lê a pasta de dados (spec §4.3) e confere que tudo está lá antes de gastar tempo de cálculo.
/// Falta de arquivo lança <see cref="DadosIncompletosException"/> listando todos os caminhos de uma vez.
/// </summary>
public static class ConjuntoDeDados
{
    public static readonly string[] ExtensoesDeFoto = [".jpg", ".jpeg", ".png"];

    public const string NomeDoStl = "solido.stl";

    /// <summary>
    /// Só os nestings, para o comando <c>nesting</c>, que roda ANTES de existir o <c>alvos.txt</c>
    /// (é a imagem numerada que permite escrevê-lo).
    /// </summary>
    public static DadosDoCorte LerNestings(string pasta)
    {
        ExigirPasta(pasta);
        var faltando = new List<string>();
        var nestings = ListarNestings(pasta, faltando);
        if (faltando.Count > 0) throw new DadosIncompletosException(faltando);
        return new DadosDoCorte(nestings, []);
    }

    public static DadosDoCorte LerCorte(string pasta)
    {
        ExigirPasta(pasta);
        var faltando = new List<string>();
        var pastaDoCenario = Path.Combine(pasta, Cenario.Corte.NomeDaPasta());
        var pastaDosNestings = Path.Combine(pastaDoCenario, "nestings");
        var nestings = ListarNestings(pasta, faltando);

        var alvosTxt = Path.Combine(pastaDoCenario, "alvos.txt");
        var alvos = new List<AlvoDoCorte>();
        if (!File.Exists(alvosTxt))
            faltando.Add(alvosTxt);
        else
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            var linhas = File.ReadAllLines(alvosTxt);
            for (var i = 0; i < linhas.Length; i++)
            {
                var linha = linhas[i].Trim();
                if (linha.Length == 0 || linha[0] == '#') continue;

                // <codigo> <arquivo-do-nesting> <número|->: o nome do arquivo pode ter espaço,
                // então só o primeiro e o último campo são fixos.
                var campos = linha.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var invalida = $"{alvosTxt}: linha {i + 1} inválida";
                if (campos.Length < 3) { faltando.Add(invalida); continue; }
                var codigo = campos[0];
                var nesting = string.Join(' ', campos[1..^1]);
                int? numero = null;
                if (campos[^1] != "-")
                {
                    if (!int.TryParse(campos[^1], System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 1)
                    { faltando.Add(invalida); continue; }
                    numero = n;
                }
                if (!vistos.Add(codigo) || Path.GetFileName(nesting) != nesting)
                { faltando.Add(invalida); continue; }

                if (!File.Exists(Path.Combine(pastaDosNestings, nesting)))
                    faltando.Add(Path.Combine(pastaDosNestings, nesting));
                var dir = Path.Combine(pastaDoCenario, "alvos", codigo);
                alvos.Add(new AlvoDoCorte(codigo, nesting, numero, Foto(dir, "sem-marcador", faltando), Foto(dir, "com-marcador", faltando)));
            }
            if (alvos.Count == 0 && faltando.Count == 0) faltando.Add($"{alvosTxt}: nenhum alvo");
        }

        if (faltando.Count > 0) throw new DadosIncompletosException(faltando);
        return new DadosDoCorte(nestings, alvos.OrderBy(a => a.Codigo, StringComparer.Ordinal).ToList());
    }

    public static DadosComStl LerFormaFinal(string pasta)
    {
        ExigirPasta(pasta);
        var faltando = new List<string>();
        var pastaDoCenario = Path.Combine(pasta, Cenario.FormaFinal.NomeDaPasta());

        var tiposTxt = Path.Combine(pastaDoCenario, "tipos.txt");
        var tipos = LerTipos(tiposTxt, faltando);
        var alvos = LerAlvosComStl(Path.Combine(pastaDoCenario, "alvos"), faltando,
            codigo =>
            {
                if (tipos is null) return null; // o arquivo ausente já foi listado
                if (tipos.TryGetValue(codigo, out var tipo)) return tipo;
                return Ausente(tiposTxt, faltando);
            });
        var distratores = ListarStl(Path.Combine(pastaDoCenario, "distratores"), faltando);

        if (faltando.Count > 0) throw new DadosIncompletosException(faltando);
        return new DadosComStl(alvos, distratores);
    }

    public static DadosComStl LerIntermediarias(string pasta)
    {
        ExigirPasta(pasta);
        var faltando = new List<string>();
        var alvos = LerAlvosComStl(Path.Combine(pasta, Cenario.Intermediarias.NomeDaPasta(), "alvos"), faltando, _ => null);

        // O universo do B: o STL de cada alvo da forma final e os distratores. As fotos e os tipos
        // da forma final não importam aqui.
        var pastaDaFormaFinal = Path.Combine(pasta, Cenario.FormaFinal.NomeDaPasta());
        var daFormaFinal = new List<string>();
        var pastaDosAlvosB = Path.Combine(pastaDaFormaFinal, "alvos");
        if (!Directory.Exists(pastaDosAlvosB))
            faltando.Add(pastaDosAlvosB);
        else
            foreach (var dir in Directory.GetDirectories(pastaDosAlvosB).Order(StringComparer.Ordinal))
            {
                var stl = Path.Combine(dir, NomeDoStl);
                if (!File.Exists(stl)) faltando.Add(stl);
                else daFormaFinal.Add(stl);
            }
        daFormaFinal.AddRange(ListarStl(Path.Combine(pastaDaFormaFinal, "distratores"), faltando));

        if (faltando.Count > 0) throw new DadosIncompletosException(faltando);
        return new DadosComStl(alvos, daFormaFinal);
    }

    // ---- partes ----

    /// <summary>Pasta de dados inexistente é erro do chamador, não "dado incompleto": quem a escreveu errado vê a exceção comum.</summary>
    private static void ExigirPasta(string pasta)
    {
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException($"A pasta de dados não existe: {pasta}");
    }

    private static List<string> ListarNestings(string pasta, List<string> faltando)
    {
        var dir = Path.Combine(pasta, Cenario.Corte.NomeDaPasta(), "nestings");
        if (!Directory.Exists(dir)) { faltando.Add(dir); return []; }
        var arquivos = Directory.GetFiles(dir)
            .Where(f => string.Equals(Path.GetExtension(f), ".dxf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        if (arquivos.Count == 0) faltando.Add(Path.Combine(dir, "*.dxf"));
        return arquivos;
    }

    private static List<string> ListarStl(string dir, List<string> faltando)
    {
        if (!Directory.Exists(dir)) { faltando.Add(dir); return []; }
        return Directory.GetFiles(dir)
            .Where(f => string.Equals(Path.GetExtension(f), ".stl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private static List<AlvoComStl> LerAlvosComStl(string pastaDosAlvos, List<string> faltando, Func<string, TipoDePeca?> tipoDe)
    {
        var alvos = new List<AlvoComStl>();
        if (!Directory.Exists(pastaDosAlvos)) { faltando.Add(pastaDosAlvos); return alvos; }
        foreach (var dir in Directory.GetDirectories(pastaDosAlvos).Order(StringComparer.Ordinal))
        {
            var stl = Path.Combine(dir, NomeDoStl);
            if (!File.Exists(stl)) faltando.Add(stl);
            var sem = Foto(dir, "sem-marcador", faltando);
            var com = Foto(dir, "com-marcador", faltando);
            alvos.Add(new AlvoComStl(Path.GetFileName(dir), stl, tipoDe(Path.GetFileName(dir)), sem, com));
        }
        if (alvos.Count == 0) faltando.Add(Path.Combine(pastaDosAlvos, "*"));
        return alvos;
    }

    /// <summary>Uma linha por alvo: <c>&lt;codigo&gt; dobrada|usinada|soldada</c>. Nulo se o arquivo não existe (já listado).</summary>
    private static Dictionary<string, TipoDePeca>? LerTipos(string tiposTxt, List<string> faltando)
    {
        if (!File.Exists(tiposTxt)) { faltando.Add(tiposTxt); return null; }
        var tipos = new Dictionary<string, TipoDePeca>(StringComparer.Ordinal);
        var linhas = File.ReadAllLines(tiposTxt);
        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i].Trim();
            if (linha.Length == 0 || linha[0] == '#') continue;
            var campos = linha.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            TipoDePeca? tipo = campos.Length == 2 ? campos[1].ToLowerInvariant() switch
            {
                "dobrada" => TipoDePeca.Dobrada,
                "usinada" => TipoDePeca.Usinada,
                "soldada" => TipoDePeca.Soldada,
                _ => null,
            } : null;
            if (tipo is null || !tipos.TryAdd(campos[0], tipo.Value))
                faltando.Add($"{tiposTxt}: linha {i + 1} inválida");
        }
        return tipos;
    }

    /// <summary>Alvo sem linha no <c>tipos.txt</c>: registra e devolve nulo.</summary>
    private static TipoDePeca? Ausente(string tiposTxt, List<string> faltando)
    {
        faltando.Add($"{tiposTxt}: falta a linha de um alvo");
        return null;
    }

    private static string Foto(string dir, string nome, List<string> faltando)
    {
        var achada = Directory.Exists(dir)
            ? Directory.GetFiles(dir).FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), nome, StringComparison.OrdinalIgnoreCase)
                && ExtensoesDeFoto.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            : null;
        if (achada is null) faltando.Add(Path.Combine(dir, nome + "{" + string.Join(",", ExtensoesDeFoto) + "}"));
        return achada ?? "";
    }
}
