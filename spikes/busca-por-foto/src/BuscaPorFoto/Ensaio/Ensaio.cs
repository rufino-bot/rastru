using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Dxf;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Stl;

namespace BuscaPorFoto.Ensaios;

/// <param name="Alvos">Quantas "fotos" foram sorteadas e consultadas.</param>
/// <param name="AcertosComMarcador">Quantas tiveram o grupo certo no top-3, com a escala real.</param>
/// <param name="AcertosSemMarcador">O mesmo, com as áreas normalizadas.</param>
/// <param name="Grupos">Quantos candidatos (grupos da biblioteca inteira) o ranking teve.</param>
public sealed record ResultadoDoTeto(int Alvos, int AcertosComMarcador, int AcertosSemMarcador, int Grupos, bool Segue)
{
    /// <summary>Segue se a MELHOR das duas condições tem pelo menos ⌈0,8·n⌉ acertos no top-3 (spec §9, medição 2).</summary>
    public static bool Decidir(int alvos, int acertosCom, int acertosSem) =>
        Math.Max(acertosCom, acertosSem) >= Relatorio.CriterioDeDecisao.MinimoParaAprovar(alvos);
}

/// <param name="Arquivo">Caminho relativo à pasta lida.</param>
/// <param name="Situacao"><c>lido</c>, <c>lido com avisos</c> ou <c>recusado: {TipoDaExcecao}</c>.</param>
public sealed record LeituraDeArquivo(string Arquivo, string Situacao, IReadOnlyList<string> Avisos);

/// <param name="Arquivos">Quantos arquivos foram abertos para formar a biblioteca.</param>
/// <param name="Recusados">Quantos deles os leitores recusaram (ficam fora da biblioteca).</param>
public sealed record BibliotecaDoEnsaio(IReadOnlyList<Referencia> Referencias, int Arquivos, int Recusados);

/// <summary>O ensaio com dados públicos (spec §9): leitura em massa, teto do método e fotos de terceiros.</summary>
public static class Ensaio
{
    /// <summary>Tamanho máximo da biblioteca do teto: a spec pede ~150 a 200 peças.</summary>
    public const int LimiteDaBiblioteca = 200;

    public const string SituacaoLido = "lido";
    public const string SituacaoLidoComAvisos = "lido com avisos";
    public const string PrefixoDaRecusa = "recusado: ";

    // ------------------------------------------------------------ medição 1

    /// <summary>Todo <c>.dxf</c> e <c>.stl</c> abaixo da pasta, em ordem ordinal do caminho relativo.</summary>
    public static IReadOnlyList<LeituraDeArquivo> LerTudo(string pasta)
    {
        var resultado = new List<LeituraDeArquivo>();
        foreach (var caminho in ArquivosDeGeometria(pasta))
        {
            var relativo = Path.GetRelativePath(pasta, caminho);
            try
            {
                var avisos = LerUm(caminho, out _, out _);
                resultado.Add(new LeituraDeArquivo(relativo, avisos.Count == 0 ? SituacaoLido : SituacaoLidoComAvisos, avisos));
            }
            catch (Exception ex)
            {
                resultado.Add(new LeituraDeArquivo(relativo, PrefixoDaRecusa + ex.GetType().Name, []));
            }
        }
        return resultado;
    }

    private static List<string> ArquivosDeGeometria(string pasta) =>
        Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories)
            .Where(f => Extensao(f) is ".dxf" or ".stl")
            .OrderBy(f => Path.GetRelativePath(pasta, f), StringComparer.Ordinal)
            .ToList();

    private static string Extensao(string caminho) => Path.GetExtension(caminho).ToLowerInvariant();

    /// <summary>Lê um arquivo e devolve os avisos; o desenho ou a malha saem pelos <c>out</c> (um deles, conforme o formato).</summary>
    private static IReadOnlyList<string> LerUm(string caminho, out DesenhoDxf? desenho, out IReadOnlyList<Triangulo>? malha)
    {
        desenho = null;
        malha = null;
        if (Extensao(caminho) == ".dxf")
        {
            desenho = LeitorDeDxf.Ler(File.ReadAllText(caminho));
            return desenho.Avisos;
        }
        malha = LeitorDeStl.Ler(File.ReadAllBytes(caminho));
        return malha.Count == 0 ? ["malha sem triângulos"] : [];
    }

    // ------------------------------------------------------------ medição 2

    /// <summary>
    /// Sorteia <paramref name="alvos"/> referências da biblioteca, "fotografa" uma vista sorteada de cada uma
    /// (<see cref="Deformador"/>) e pontua contra os grupos da biblioteca inteira, nas duas condições. Acerto é o
    /// grupo certo no top-3, pela posição pessimista.
    /// </summary>
    public static ResultadoDoTeto MedirTeto(IReadOnlyList<Referencia> biblioteca, int alvos, ParametrosDeDeformacao p)
    {
        if (alvos < 1 || alvos > biblioteca.Count)
            throw new ArgumentOutOfRangeException(nameof(alvos), "O teto precisa de 1 a N alvos, com N o tamanho da biblioteca.");
        if (biblioteca.Any(r => r.Vistas.Count == 0))
            throw new ArgumentException("Toda referência precisa de ao menos uma vista.", nameof(biblioteca));

        var ordenada = biblioteca.OrderBy(r => r.Chave, StringComparer.Ordinal).ToList();
        var universo = Executor.Universo.De(ordenada);

        var rng = new Random(p.Semente);
        var sorteados = Enumerable.Range(0, ordenada.Count).OrderBy(_ => rng.Next()).Take(alvos).ToList();
        var vistas = sorteados.Select(i => rng.Next(ordenada[i].Vistas.Count)).ToList();

        var acertoCom = new bool[alvos];
        var acertoSem = new bool[alvos];
        Parallel.For(0, alvos, i =>
        {
            var referencia = ordenada[sorteados[i]];
            var vista = referencia.Vistas[vistas[i]];
            var certo = universo.GrupoDe(referencia.Chave).Id;
            foreach (var condicao in new[] { Condicao.ComMarcador, Condicao.SemMarcador })
            {
                using var foto = Deformador.Deformar(vista, condicao, p, i);
                var pontuacoes = Executor.PontuarGrupos(universo, foto, NomeDoPontuador.IoU, usarEscala: condicao == Condicao.ComMarcador);
                var acertou = Ranking.PosicaoPessimista(pontuacoes, certo) <= 3;
                (condicao == Condicao.ComMarcador ? acertoCom : acertoSem)[i] = acertou;
            }
        });

        int com = acertoCom.Count(a => a), sem = acertoSem.Count(a => a);
        return new ResultadoDoTeto(alvos, com, sem, universo.Grupos.Count, ResultadoDoTeto.Decidir(alvos, com, sem));
    }

    /// <summary>
    /// A biblioteca de uma pasta: cada STL vira uma referência de três vistas e cada peça separada de um DXF (sem
    /// borda de chapa: a planificação é uma peça só), uma de uma vista. Arquivos em ordem sorteada pela semente,
    /// lidos até juntar <paramref name="limite"/> referências, e o excedente do último é cortado.
    /// </summary>
    public static BibliotecaDoEnsaio CarregarBiblioteca(string pasta, int limite, int semente)
    {
        var rng = new Random(semente);
        var arquivos = ArquivosDeGeometria(pasta).OrderBy(_ => rng.Next()).ToList();
        var referencias = new List<Referencia>();
        int abertos = 0, recusados = 0;
        try
        {
            foreach (var caminho in arquivos)
            {
                if (referencias.Count >= limite) break;
                abertos++;
                try { AcrescentarReferencias(caminho, referencias); }
                catch (Exception) { recusados++; }
            }
            if (referencias.Count > limite)
            {
                foreach (var sobra in referencias.Skip(limite)) foreach (var v in sobra.Vistas) v.Dispose();
                referencias.RemoveRange(limite, referencias.Count - limite);
            }
        }
        catch
        {
            foreach (var r in referencias) foreach (var v in r.Vistas) v.Dispose();
            throw;
        }
        // Chaves opacas, na ordem em que entraram.
        var chaveadas = referencias.Select((r, i) => r with { Chave = $"K{i + 1:D4}" }).ToList();
        return new BibliotecaDoEnsaio(chaveadas, abertos, recusados);
    }

    private static void AcrescentarReferencias(string caminho, List<Referencia> referencias)
    {
        LerUm(caminho, out var desenho, out var malha);
        if (malha is not null)
        {
            if (malha.Count == 0) throw new InvalidDataException("Malha vazia.");
            var vistas = ProjetorDeSilhueta.Projetar(malha);
            var reduzidas = vistas.Select(v => v.ReduzidaAte(Executor.LadoMaximoDaMascara)).ToList();
            foreach (var v in vistas) v.Dispose();
            referencias.Add(new Referencia("", reduzidas, ProjetorDeSilhueta.Assinatura(malha)));
            return;
        }

        var separado = SeparadorDeNesting.Separar(desenho!, new HashSet<string>(), BordaDaChapa.Nao);
        foreach (var peca in separado.Pecas)
        {
            referencias.Add(new Referencia("", [peca.Silhueta.ReduzidaAte(Executor.LadoMaximoDaMascara)], peca.Assinatura));
            peca.Silhueta.Dispose();
        }
    }

    // ------------------------------------------------------------ medição 3

    /// <summary>
    /// <c>&lt;pasta&gt;/alvos/&lt;nome&gt;/{solido.stl, sem-marcador.*}</c> e <c>&lt;pasta&gt;/distratores/*.stl</c>. Sem
    /// <c>tipos.txt</c> e sem foto com marcador: <c>Tipo</c> nulo e <c>CaminhoComMarcador</c> vazio.
    /// </summary>
    public static DadosComStl LerFotosDeTerceiros(string pasta)
    {
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException($"A pasta do ensaio não existe: {pasta}");
        var faltando = new List<string>();

        var pastaDosAlvos = Path.Combine(pasta, "alvos");
        var alvos = new List<AlvoComStl>();
        if (!Directory.Exists(pastaDosAlvos)) faltando.Add(pastaDosAlvos);
        else
        {
            foreach (var dir in Directory.GetDirectories(pastaDosAlvos).Order(StringComparer.Ordinal))
            {
                var stl = Path.Combine(dir, ConjuntoDeDados.NomeDoStl);
                if (!File.Exists(stl)) faltando.Add(stl);
                var foto = Directory.GetFiles(dir).FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), "sem-marcador", StringComparison.OrdinalIgnoreCase)
                    && ConjuntoDeDados.ExtensoesDeFoto.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                if (foto is null) faltando.Add(Path.Combine(dir, "sem-marcador{" + string.Join(",", ConjuntoDeDados.ExtensoesDeFoto) + "}"));
                alvos.Add(new AlvoComStl(Path.GetFileName(dir), stl, null, foto ?? "", ""));
            }
            if (alvos.Count == 0) faltando.Add(Path.Combine(pastaDosAlvos, "*"));
        }

        var pastaDosDistratores = Path.Combine(pasta, "distratores");
        var distratores = new List<string>();
        if (!Directory.Exists(pastaDosDistratores)) faltando.Add(pastaDosDistratores);
        else
            distratores.AddRange(Directory.GetFiles(pastaDosDistratores)
                .Where(f => Extensao(f) == ".stl")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal));

        if (faltando.Count > 0) throw new DadosIncompletosException(faltando);
        return new DadosComStl(alvos, distratores);
    }
}
