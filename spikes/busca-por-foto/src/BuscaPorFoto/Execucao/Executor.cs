using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Dxf;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Stl;
using OpenCvSharp;

namespace BuscaPorFoto.Execucao;

/// <summary>
/// Roda um cenário sobre a pasta de dados e grava a saída ANONIMIZADA. Três regras guiam o desenho:
/// (1) quem pontua só recebe <see cref="Mascara"/>, e quem agrupa só a assinatura — nome, código e
/// ordem de enumeração não chegam ao ranking; as chaves e os ids saem da ordem ordinal e o grupo certo
/// só é procurado DEPOIS da pontuação. (2) A posição é pessimista e por GRUPO de geometria idêntica.
/// (3) Nada que o terminal ou a saída (fora os dois mapas) escreva leva código de peça ou nome de arquivo.
/// </summary>
public static class Executor
{
    /// <summary>Maior lado das máscaras que entram na comparação; acima disso só custa tempo.</summary>
    public const int LadoMaximoDaMascara = 512;

    /// <summary>Maior lado da foto sem marcador antes do recorte (sem escala, reduzir não perde nada).</summary>
    public const int LadoMaximoDaFotoSemMarcador = 2000;

    public const string ArquivoDeMapa = "mapa-de-codigos.csv";
    public const string ArquivoDeMapaDeNestings = "mapa-de-nestings.csv";
    public const string ArquivoDeResultadosCsv = "resultados.csv";
    public const string ArquivoDePontuacoesCsv = "pontuacoes.csv";
    public const string ArquivoDeClassificacao = "classificacao-do-recorte.csv";
    public const string ArquivoDeExecucao = "execucao.txt";
    public const string ArquivoDeFaltas = "faltando.txt";
    public const string ArquivoDeErro = "erro.txt";
    /// <summary>Os avisos inteiros do DXF (com nomes de bloco). Local: o terminal só os conta por tipo.</summary>
    public const string ArquivoDeAvisosDoDxf = "avisos-do-dxf.txt";
    public const string PastaDeSobreposicoes = "sobreposicoes";
    public const string PastaDeNestings = "nestings";

    // ---------------------------------------------------------------- corte

    public static IReadOnlyList<LinhaDeResultado> ExecutarCorte(
        DadosDoCorte dados, string saida, MetodoDeRecorte metodo, IReadOnlySet<string> camadasIgnoradas, TextWriter log,
        BordaDaChapa borda = BordaDaChapa.Auto)
    {
        var nestings = SepararNestings(dados.Nestings, camadasIgnoradas, borda, log);
        var alvos = dados.Alvos.OrderBy(a => a.Codigo, StringComparer.Ordinal).ToList();

        // Erro de dados: o alvo aponta um número que o nesting não tem. Para antes de gravar ou consultar qualquer coisa.
        var problemas = new List<string>();
        foreach (var alvo in alvos.Where(a => a.Numero is not null))
        {
            var nesting = nestings.FirstOrDefault(n => n.Nome == alvo.Nesting);
            if (nesting is null)
                problemas.Add($"alvo {alvo.Codigo}: o nesting {alvo.Nesting} não está entre os lidos");
            else if (nesting.Separado.Pecas.All(p => p.Numero != alvo.Numero))
                problemas.Add($"alvo {alvo.Codigo}: o número {alvo.Numero} não existe no nesting {alvo.Nesting}");
        }
        if (problemas.Count > 0)
        {
            DescartarMascaras(nestings);
            throw new DadosIncompletosException(problemas);
        }

        var referencias = new List<Referencia>();
        try
        {
            Directory.CreateDirectory(saida);
            EscreverMapaDeNestings(saida, nestings);
            EscreverAvisos(saida, nestings);
            File.WriteAllLines(Path.Combine(saida, ArquivoDeMapa),
                alvos.Select((a, i) => $"{IdDoAlvo(Cenario.Corte, i)};{a.Codigo}").Prepend("id;codigo"));
            File.WriteAllLines(Path.Combine(saida, ArquivoDeExecucao),
            [
                $"cenario={Cenario.Corte.NomeDaPasta()}",
                $"recorte={metodo.ToString().ToLowerInvariant()}",
                $"camadas-ignoradas={string.Join(',', camadasIgnoradas.Order(StringComparer.Ordinal))}",
                $"borda={borda.ToString().ToLowerInvariant()}",
            ]);

            // Referências: toda peça de todo nesting, chave K0001… pela ordem ordinal de (arquivo, número).
            var total = nestings.Sum(n => n.Separado.Pecas.Count);
            var chavePorPeca = new Dictionary<(string Nesting, int Numero), string>();
            foreach (var nesting in nestings)
                foreach (var peca in nesting.Separado.Pecas.OrderBy(p => p.Numero))
                {
                    var chave = ChaveDe(referencias.Count);
                    log.WriteLine($"Preparando referência {referencias.Count + 1}/{total}");
                    referencias.Add(new Referencia(chave, [peca.Silhueta.ReduzidaAte(LadoMaximoDaMascara)], peca.Assinatura));
                    chavePorPeca[(nesting.Nome, peca.Numero)] = chave;
                }
            DescartarMascaras(nestings);

            var universo = Universo.De(referencias);
            var paraConsultar = alvos.Select((a, i) => new AlvoParaConsultar(
                IdDoAlvo(Cenario.Corte, i), TipoDePeca.Chapa, a.CaminhoSemMarcador, a.CaminhoComMarcador,
                a.Numero is { } n ? chavePorPeca[(a.Nesting, n)] : null,
                universo)).ToList();
            return Consultar(Cenario.Corte, paraConsultar, saida, metodo, log);
        }
        finally
        {
            foreach (var r in referencias) foreach (var v in r.Vistas) v.Dispose();
        }
    }

    /// <summary>Grava <c>nestings/N01.png</c>… (a imagem numerada de cada nesting), o mapa e os avisos. Devolve quantos nestings.</summary>
    public static int GerarImagensNumeradas(
        DadosDoCorte dados, string saida, IReadOnlySet<string> camadasIgnoradas, TextWriter log,
        BordaDaChapa borda = BordaDaChapa.Auto)
    {
        var nestings = SepararNestings(dados.Nestings, camadasIgnoradas, borda, log);
        try
        {
            Directory.CreateDirectory(Path.Combine(saida, PastaDeNestings));
            EscreverMapaDeNestings(saida, nestings);
            EscreverAvisos(saida, nestings);
            foreach (var n in nestings.Where(n => n.Separado.Pecas.Count > 0))
            {
                using var imagem = ImagemNumerada.Gerar(n.Separado);
                ImagensEmDisco.Gravar(Path.Combine(saida, PastaDeNestings, $"{n.Id}.png"), imagem);
            }
            return nestings.Count;
        }
        finally { DescartarMascaras(nestings); }
    }

    private sealed record NestingLido(string Id, string Nome, NestingSeparado Separado);

    private static List<NestingLido> SepararNestings(
        IReadOnlyList<string> caminhos, IReadOnlySet<string> camadasIgnoradas, BordaDaChapa borda, TextWriter log)
    {
        var ordenados = caminhos.OrderBy(Path.GetFileName, StringComparer.Ordinal).ToList();
        var lidos = new List<NestingLido>();
        for (var i = 0; i < ordenados.Count; i++)
        {
            var id = $"N{i + 1:D2}";
            log.WriteLine($"Separando nesting {id} ({i + 1}/{ordenados.Count})");
            var desenho = LeitorDeDxf.Ler(File.ReadAllText(ordenados[i]));
            var separado = SeparadorDeNesting.Separar(desenho, camadasIgnoradas, borda);
            lidos.Add(new NestingLido(id, Path.GetFileName(ordenados[i]), separado));

            log.WriteLine($"{id}: {separado.Pecas.Count} peça(s), {separado.Avisos.Count} aviso(s)");
            foreach (var tipo in separado.Avisos.GroupBy(TipoDoAviso).OrderBy(g => g.Key, StringComparer.Ordinal))
                log.WriteLine($"  {tipo.Count()}x {tipo.Key}");
        }
        return lidos;
    }

    private static void DescartarMascaras(IEnumerable<NestingLido> nestings)
    {
        foreach (var n in nestings) foreach (var p in n.Separado.Pecas) p.Silhueta.Dispose();
    }

    private static void EscreverMapaDeNestings(string saida, IEnumerable<NestingLido> nestings) =>
        File.WriteAllLines(Path.Combine(saida, ArquivoDeMapaDeNestings),
            nestings.Select(n => $"{n.Id};{n.Nome}").Prepend("id;arquivo"));

    private static void EscreverAvisos(string saida, IEnumerable<NestingLido> nestings) =>
        File.WriteAllLines(Path.Combine(saida, ArquivoDeAvisosDoDxf),
            nestings.SelectMany(n => n.Separado.Avisos.Select(a => $"{n.Id}: {a}")));

    // Os avisos do DXF trazem, às vezes, nome de bloco (e o tipo da entidade). O TIPO do aviso é o texto
    // sem a parte variável: os três modelos que citam nome viram um texto fixo, e dos demais só os números
    // saem (viram "#"). Os demais textos são fixos no código, sem nome de camada nem de arquivo.
    private static readonly (Regex Modelo, string Tipo)[] ModelosDeAviso =
    [
        (new Regex(@"^bloco .+ não encontrado: INSERT ignorado$", RegexOptions.Singleline), "bloco não encontrado: INSERT ignorado"),
        (new Regex(@"^blocos aninhados além de \d+ níveis: INSERT de .+ ignorado$", RegexOptions.Singleline), "blocos aninhados além do limite: INSERT ignorado"),
        (new Regex(@"^\d+ entidade\(s\) .+ não lida\(s\)$", RegexOptions.Singleline), "entidade(s) de um tipo não lida(s)"),
    ];

    private static readonly Regex Digitos = new(@"\d+");

    /// <summary>O aviso do DXF sem nome de bloco, de entidade nem número: o que o terminal pode mostrar.</summary>
    public static string TipoDoAviso(string aviso)
    {
        foreach (var (modelo, tipo) in ModelosDeAviso)
            if (modelo.IsMatch(aviso)) return tipo;
        return Digitos.Replace(aviso, "#");
    }

    // ---------------------------------------------------- forma final e intermediárias

    public static IReadOnlyList<LinhaDeResultado> ExecutarComStl(
        DadosComStl dados, Cenario cenario, string saida, MetodoDeRecorte metodo, TextWriter log)
    {
        if (cenario == Cenario.Corte)
            throw new ArgumentException("O corte não tem STL; use ExecutarCorte.", nameof(cenario));

        var alvos = dados.Alvos.OrderBy(a => a.Codigo, StringComparer.Ordinal).ToList();
        var referencias = new List<Referencia>();
        try
        {
            Directory.CreateDirectory(saida);
            File.WriteAllLines(Path.Combine(saida, ArquivoDeMapa),
                alvos.Select((a, i) => $"{IdDoAlvo(cenario, i)};{a.Codigo}").Prepend("id;codigo"));
            File.WriteAllLines(Path.Combine(saida, ArquivoDeExecucao),
            [
                $"cenario={cenario.NomeDaPasta()}",
                $"recorte={metodo.ToString().ToLowerInvariant()}",
            ]);

            // Os alvos primeiro, depois os outros candidatos; a chave é só o índice.
            var caminhos = alvos.Select(a => a.CaminhoDoStl).Concat(dados.OutrosCandidatos).ToList();
            for (var i = 0; i < caminhos.Count; i++)
            {
                log.WriteLine($"Projetando candidato {i + 1}/{caminhos.Count}");
                var malha = LeitorDeStl.Ler(File.ReadAllBytes(caminhos[i]));
                var vistas = ProjetorDeSilhueta.Projetar(malha);
                var reduzidas = vistas.Select(v => v.ReduzidaAte(LadoMaximoDaMascara)).ToList();
                foreach (var v in vistas) v.Dispose();
                referencias.Add(new Referencia(ChaveDe(i), reduzidas, ProjetorDeSilhueta.Assinatura(malha)));
            }

            // Forma final: todo mundo no mesmo universo. Intermediárias: o universo do B (os outros
            // candidatos) mais o PRÓPRIO STL da foto — e não os das outras intermediárias.
            var doB = referencias.Skip(alvos.Count).ToList();
            var universoUnico = cenario == Cenario.FormaFinal ? Universo.De(referencias) : null;
            var paraConsultar = alvos.Select((a, i) => new AlvoParaConsultar(
                IdDoAlvo(cenario, i), a.Tipo, a.CaminhoSemMarcador, a.CaminhoComMarcador, ChaveDe(i),
                universoUnico ?? Universo.De(doB.Append(referencias[i]).ToList()))).ToList();
            return Consultar(cenario, paraConsultar, saida, metodo, log);
        }
        finally
        {
            foreach (var r in referencias) foreach (var v in r.Vistas) v.Dispose();
        }
    }

    // ---------------------------------------------------------------- comum

    private static string ChaveDe(int indice) => $"K{indice + 1:D4}";

    private static string IdDoAlvo(Cenario cenario, int indice) => $"{cenario.Prefixo()}{indice + 1:D2}";

    /// <param name="ChaveDaCerta">A referência do alvo; nulo se ele não saiu separado do nesting.</param>
    private sealed record AlvoParaConsultar(
        string Id, TipoDePeca? Tipo, string CaminhoSemMarcador, string CaminhoComMarcador, string? ChaveDaCerta, Universo Universo);

    /// <summary>As referências de uma consulta e os grupos de geometria idêntica entre elas (ids <c>G0001</c>…).</summary>
    internal sealed class Universo
    {
        public IReadOnlyList<Referencia> Referencias { get; }
        public IReadOnlyList<(string Id, IReadOnlyList<string> Chaves)> Grupos { get; }

        private Universo(IReadOnlyList<Referencia> referencias, IReadOnlyList<(string, IReadOnlyList<string>)> grupos)
        {
            Referencias = referencias;
            Grupos = grupos;
        }

        public static Universo De(IReadOnlyList<Referencia> referencias) =>
            new(referencias, AgrupadorDeReferencias.Agrupar(referencias)
                .Select((chaves, i) => ($"G{i + 1:D4}", chaves)).ToList());

        public (string Id, IReadOnlyList<string> Chaves) GrupoDe(string chave) =>
            Grupos.First(g => g.Chaves.Contains(chave));
    }

    private static IReadOnlyList<LinhaDeResultado> Consultar(
        Cenario cenario, IReadOnlyList<AlvoParaConsultar> alvos, string saida, MetodoDeRecorte metodo, TextWriter log)
    {
        Directory.CreateDirectory(Path.Combine(saida, PastaDeSobreposicoes));
        var linhas = new List<LinhaDeResultado>();
        var pontuacoes = new List<LinhaDePontuacao>();

        foreach (var alvo in alvos)
            foreach (var condicao in new[] { Condicao.SemMarcador, Condicao.ComMarcador })
            {
                log.WriteLine($"Consultando {alvo.Id} {condicao}");
                var caminho = condicao == Condicao.SemMarcador ? alvo.CaminhoSemMarcador : alvo.CaminhoComMarcador;
                var (mascara, falhaDaFoto) = PrepararFoto(caminho, condicao, metodo,
                    Path.Combine(saida, PastaDeSobreposicoes, $"{alvo.Id}-{condicao}.png"));
                try
                {
                    foreach (var pontuador in new[] { NomeDoPontuador.Hu, NomeDoPontuador.IoU })
                    {
                        var falha = alvo.ChaveDaCerta is null ? "referência não separada do nesting" : falhaDaFoto;
                        if (falha is not null || mascara is null)
                        {
                            var noGrupo = alvo.ChaveDaCerta is { } k ? alvo.Universo.GrupoDe(k).Chaves.Count : 0;
                            linhas.Add(new LinhaDeResultado(alvo.Id, cenario, alvo.Tipo, condicao, pontuador,
                                null, falha, null, null, noGrupo, alvo.Universo.Grupos.Count, 0));
                            continue;
                        }

                        var relogio = Stopwatch.StartNew();
                        var pontuacaoPorGrupo = PontuarGrupos(alvo.Universo, mascara, pontuador, usarEscala: condicao == Condicao.ComMarcador);
                        relogio.Stop();

                        // Só agora se procura o grupo certo.
                        var certo = alvo.Universo.GrupoDe(alvo.ChaveDaCerta!);
                        var posicao = Ranking.PosicaoPessimista(pontuacaoPorGrupo, certo.Id);
                        var daCerta = pontuacaoPorGrupo[certo.Id];
                        var erradas = pontuacaoPorGrupo.Where(kv => kv.Key != certo.Id).Select(kv => kv.Value).ToList();
                        linhas.Add(new LinhaDeResultado(alvo.Id, cenario, alvo.Tipo, condicao, pontuador,
                            posicao, null, daCerta, erradas.Count > 0 ? erradas.Max() : null,
                            certo.Chaves.Count, alvo.Universo.Grupos.Count, relogio.ElapsedMilliseconds));

                        if (pontuador == NomeDoPontuador.IoU)
                            foreach (var (id, _) in alvo.Universo.Grupos)
                                pontuacoes.Add(new LinhaDePontuacao(alvo.Id, condicao, pontuador, id, pontuacaoPorGrupo[id], id == certo.Id));
                    }
                }
                finally { mascara?.Dispose(); }
            }

        ArquivoDeResultados.Escrever(Path.Combine(saida, ArquivoDeResultadosCsv), linhas);
        ArquivoDePontuacoes.Escrever(Path.Combine(saida, ArquivoDePontuacoesCsv), pontuacoes);
        EscreverModeloDeClassificacao(Path.Combine(saida, ArquivoDeClassificacao), alvos.Select(a => a.Id).ToList());
        return linhas;
    }

    /// <summary>
    /// Pontuação de cada GRUPO: a melhor das referências e vistas dele. Recebe só a máscara da foto e o
    /// universo — não sabe qual é o alvo.
    /// </summary>
    internal static Dictionary<string, double> PontuarGrupos(Universo universo, Mascara foto, NomeDoPontuador pontuador, bool usarEscala)
    {
        var porChave = universo.Referencias.ToDictionary(
            r => r.Chave,
            r => r.Vistas.Max(vista => pontuador == NomeDoPontuador.Hu
                ? PontuadorHu.Pontuar(foto, vista)
                : PontuadorIoU.Pontuar(foto, vista, usarEscala)));
        return universo.Grupos.ToDictionary(g => g.Id, g => g.Chaves.Max(c => porChave[c]));
    }

    private static (Mascara? Mascara, string? Falha) PrepararFoto(
        string caminho, Condicao condicao, MetodoDeRecorte metodo, string caminhoDaSobreposicao)
    {
        using var foto = ImagensEmDisco.Ler(caminho);
        if (foto.Empty()) return (null, "foto ilegível");

        FotoRetificada? retificada = null;
        Mat? reduzida = null;
        try
        {
            Mat imagem;
            Point2f centro;
            Point2f[]? cantos = null;
            double? mmPorPixel = null;
            if (condicao == Condicao.ComMarcador)
            {
                retificada = Retificador.Retificar(foto);
                if (retificada is null) return (null, "marcador não detectado");
                imagem = retificada.Imagem;
                centro = retificada.CentroDaFoto;
                cantos = retificada.CantosDoMarcador;
                mmPorPixel = retificada.MmPorPixel;
            }
            else
            {
                var fator = Math.Min(1.0, (double)LadoMaximoDaFotoSemMarcador / Math.Max(foto.Width, foto.Height));
                reduzida = new Mat();
                Cv2.Resize(foto, reduzida, new Size(), fator, fator, InterpolationFlags.Area);
                imagem = reduzida;
                centro = new Point2f(reduzida.Width / 2f, reduzida.Height / 2f);
            }

            using var cheia = Recortador.Recortar(imagem, centro, cantos, metodo);
            using (var sobreposicao = Sobreposicao.Gerar(imagem, cheia))
                ImagensEmDisco.Gravar(caminhoDaSobreposicao, sobreposicao);

            using var mascara = Mascara.DeMascaraCheia(cheia, mmPorPixel);
            return mascara is null ? (null, "recorte vazio") : (mascara.ReduzidaAte(LadoMaximoDaMascara), null);
        }
        finally
        {
            retificada?.Dispose();
            reduzida?.Dispose();
        }
    }

    /// <summary>
    /// Modelo que o humano preenche olhando as sobreposições. Não sobrescreve um que já exista —
    /// ele pode estar preenchido.
    /// </summary>
    private static void EscreverModeloDeClassificacao(string caminho, IReadOnlyList<string> ids)
    {
        if (File.Exists(caminho)) return;
        var linhas = ids.SelectMany(id => new[] { Condicao.SemMarcador, Condicao.ComMarcador }.Select(c => $"{id};{c};"));
        File.WriteAllLines(caminho, linhas.Prepend(ArquivoDeClassificacaoDoRecorte.Cabecalho));
    }
}
