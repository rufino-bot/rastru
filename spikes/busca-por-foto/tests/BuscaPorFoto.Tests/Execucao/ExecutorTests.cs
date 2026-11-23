using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Execucao;

public sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; } = Directory.CreateTempSubdirectory("busca-por-foto-").FullName;
    public void Dispose() => Directory.Delete(Caminho, recursive: true);
}

public class ExecutorTests
{
    // A-100 e B-200 são proporcionais (a mesma forma no dobro do tamanho): só a escala os separa.
    private static readonly ChapaSintetica[] Alvos =
    [
        new("A-100", 100, 50, 20),
        new("B-200", 200, 100, 40),
        new("C-150", 150, 60, 15),
    ];

    private static readonly (string, byte[])[] Distratores =
    [
        ("caixa.stl", MalhaSintetica.StlBinario(MalhaSintetica.Caixa(100, 80, 60))),
        ("quadrada.stl", MalhaSintetica.StlBinario(MalhaSintetica.ChapaComFuro(120, 120, 3, 30))),
    ];

    private const string ArquivoDoNesting = "chapa-SECRETA-07.dxf";

    private static readonly PecaDoDxf[] DistratoresNoNesting = [new(120, 120, 30), new(100, 80, 0)];

    private static readonly IReadOnlySet<string> SemCamadas = new HashSet<string>();

    private static List<PecaDoDxf> PecasDoNesting(IEnumerable<ChapaSintetica> alvos) =>
        alvos.Select(PecaDoDxf.De).Concat(DistratoresNoNesting).ToList();

    private static void MontarCorteComTodos(string pasta, ChapaSintetica[]? alvos = null, string? blocoAusente = null)
    {
        alvos ??= Alvos;
        // Os alvos são as primeiras peças (números 1 a n), na ordem do arreglo.
        ConjuntoSintetico.MontarCorte(pasta, ArquivoDoNesting, PecasDoNesting(alvos),
            alvos.Select((a, i) => (a, (int?)(i + 1))).ToList(), blocoAusente);
    }

    private static IReadOnlyList<LinhaDeResultado> RodarCorte(string dados, string saida, TextWriter? log = null) =>
        Executor.ExecutarCorte(ConjuntoDeDados.LerCorte(dados), saida, MetodoDeRecorte.Otsu, SemCamadas, log ?? TextWriter.Null);

    private static IReadOnlyList<LinhaDeResultado> RodarFormaFinal(string dados, string saida, TextWriter? log = null) =>
        Executor.ExecutarComStl(ConjuntoDeDados.LerFormaFinal(dados), Cenario.FormaFinal, saida, MetodoDeRecorte.Otsu, log ?? TextWriter.Null);

    [Fact]
    public void Corte_com_marcador_e_IoU_cada_alvo_fica_em_primeiro()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        MontarCorteComTodos(dados.Caminho);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho);

        var comMarcador = linhas.Where(l => l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.Equal(3, comMarcador.Count);
        Assert.All(comMarcador, l => Assert.Equal(1, l.Posicao));
        Assert.All(linhas, l => Assert.Equal(Cenario.Corte, l.Cenario));
        Assert.All(linhas, l => Assert.Equal(5, l.Grupos));
        Assert.Equal(["A01", "A02", "A03"], linhas.Select(l => l.Id).Distinct().Order().ToArray());
    }

    [Fact]
    public void FormaFinal_com_marcador_e_IoU_cada_alvo_fica_em_primeiro()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos, Distratores);

        var linhas = RodarFormaFinal(dados.Caminho, saida.Caminho);

        var comMarcador = linhas.Where(l => l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.Equal(3, comMarcador.Count);
        Assert.All(comMarcador, l => Assert.Equal(1, l.Posicao));
        // O tipo vem do tipos.txt (em rodízio na ordem de código: A-100, B-200, C-150).
        Assert.Equal([TipoDePeca.Dobrada, TipoDePeca.Usinada, TipoDePeca.Soldada], comMarcador.Select(l => l.Tipo!.Value).ToArray());
        Assert.All(linhas, l => Assert.Equal(Cenario.FormaFinal, l.Cenario));
    }

    [Fact]
    public void Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2()
    {
        // Guarda de vazamento: sem escala o par proporcional empata, e com o empate pessimista um dos dois
        // tem de ficar em 2º. Um bônus à peça certa a poria em 1º.
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos, Distratores);

        var linhas = RodarFormaFinal(dados.Caminho, saida.Caminho);

        var semMarcador = linhas.Where(l => l.Condicao == Condicao.SemMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.Equal(3, semMarcador.Count);
        Assert.All(semMarcador, l => Assert.InRange(l.Posicao!.Value, 1, 2));
        Assert.Contains(semMarcador.Where(l => l.Id is "B01" or "B02"), l => l.Posicao == 2);

        // O empate exato, que não depende da sorte da rasterização: uma gêmea do A-100 com o dobro da
        // espessura é outro grupo (outra área e volume) com a MESMA vista da face, então a pontuação dela é
        // idêntica à do alvo, e o empate conta contra: o A-100 tem de ficar em 2º. Um bônus de 1e-9 à certa o poria em 1º.
        using var dadosComGemea = new PastaTemporaria();
        using var saidaComGemea = new PastaTemporaria();
        var gemea = ("gemea.stl", MalhaSintetica.StlBinario(MalhaSintetica.ChapaComFuro(100, 50, 6, 20)));
        ConjuntoSintetico.MontarFormaFinal(dadosComGemea.Caminho, Alvos.Take(1), [.. Distratores, gemea]);

        var comGemea = RodarFormaFinal(dadosComGemea.Caminho, saidaComGemea.Caminho);

        var doAlvo = Assert.Single(comGemea, l => l.Condicao == Condicao.SemMarcador && l.Pontuador == NomeDoPontuador.IoU);
        Assert.Equal(4, doAlvo.Grupos);
        Assert.Equal(2, doAlvo.Posicao);
    }

    [Fact]
    public void Trocar_os_codigos_nao_muda_a_posicao_de_nenhuma_geometria()
    {
        // Guarda de vazamento por nome e ordem: inverter a ordem ordinal dos códigos mudaria alguma posição
        // se nome de pasta ou ordem de enumeração influenciasse o ranking.
        var renomeados = Alvos.Select(a => a with { Codigo = a.Codigo switch { "A-100" => "Z-100", "C-150" => "A-150", var c => c } }).ToArray();

        foreach (var cenario in new[] { Cenario.Corte, Cenario.FormaFinal })
        {
            using var dadosA = new PastaTemporaria();
            using var dadosB = new PastaTemporaria();
            using var saidaA = new PastaTemporaria();
            using var saidaB = new PastaTemporaria();
            if (cenario == Cenario.Corte)
            {
                MontarCorteComTodos(dadosA.Caminho, Alvos);
                MontarCorteComTodos(dadosB.Caminho, renomeados);
            }
            else
            {
                ConjuntoSintetico.MontarFormaFinal(dadosA.Caminho, Alvos, Distratores);
                ConjuntoSintetico.MontarFormaFinal(dadosB.Caminho, renomeados, Distratores);
            }

            var linhasA = cenario == Cenario.Corte ? RodarCorte(dadosA.Caminho, saidaA.Caminho) : RodarFormaFinal(dadosA.Caminho, saidaA.Caminho);
            var linhasB = cenario == Cenario.Corte ? RodarCorte(dadosB.Caminho, saidaB.Caminho) : RodarFormaFinal(dadosB.Caminho, saidaB.Caminho);

            Assert.NotEqual(
                File.ReadAllText(Path.Combine(saidaA.Caminho, Executor.ArquivoDeMapa)),
                File.ReadAllText(Path.Combine(saidaB.Caminho, Executor.ArquivoDeMapa)));
            Assert.Equal(
                PosicoesPorGeometria(linhasA, saidaA.Caminho, Alvos),
                PosicoesPorGeometria(linhasB, saidaB.Caminho, renomeados));
        }
    }

    private static SortedDictionary<string, int?> PosicoesPorGeometria(
        IReadOnlyList<LinhaDeResultado> linhas, string saida, ChapaSintetica[] alvos)
    {
        var codigoPorId = File.ReadAllLines(Path.Combine(saida, Executor.ArquivoDeMapa)).Skip(1)
            .Select(l => l.Split(';')).ToDictionary(c => c[0], c => c[1]);
        var geometriaPorCodigo = alvos.ToDictionary(a => a.Codigo, a => $"{a.Largura}x{a.Altura}x{a.LadoDoFuro}");
        return new SortedDictionary<string, int?>(linhas.ToDictionary(
            l => $"{geometriaPorCodigo[codigoPorId[l.Id]]}|{l.Condicao}|{l.Pontuador}",
            l => l.Posicao), StringComparer.Ordinal);
    }

    [Fact]
    public void Copias_do_mesmo_contorno_no_nesting_contam_como_um_candidato()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        var alvo = Alvos[0];
        // O alvo (número 1) e mais três cópias: uma girada 90 graus, uma com os vértices no sentido
        // inverso (o espelho, para uma peça simétrica) e uma igual. Mais dois distratores.
        PecaDoDxf[] pecas =
        [
            PecaDoDxf.De(alvo), PecaDoDxf.De(alvo) with { Largura = alvo.Altura, Altura = alvo.Largura },
            PecaDoDxf.De(alvo) with { SentidoHorario = true }, PecaDoDxf.De(alvo), .. DistratoresNoNesting,
        ];
        ConjuntoSintetico.MontarCorte(dados.Caminho, ArquivoDoNesting, pecas, [(alvo, 1)]);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho);

        var linha = Assert.Single(linhas, l => l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU);
        Assert.Equal(1, linha.Posicao);
        Assert.Equal(4, linha.ReferenciasNoGrupoDaCerta);
        Assert.Equal(3, linha.Grupos);
    }

    [Fact]
    public void Alvo_marcado_como_nao_separado_vira_falha_de_referencia()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarCorte(dados.Caminho, ArquivoDoNesting, PecasDoNesting(Alvos.Take(1)), [(Alvos[0], null)]);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho);

        Assert.Equal(4, linhas.Count);
        Assert.All(linhas, l =>
        {
            Assert.Null(l.Posicao);
            Assert.Equal(Executor.FalhaReferenciaNaoSeparada, l.Falha);
            Assert.Equal(0, l.ReferenciasNoGrupoDaCerta);
        });
    }

    [Fact]
    public void Numero_inexistente_no_nesting_e_recusado_antes_de_consultar()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarCorte(dados.Caminho, ArquivoDoNesting, PecasDoNesting(Alvos.Take(3)),
            [(Alvos[0], 9)]); // o nesting tem 5 peças

        var erro = Assert.Throws<DadosIncompletosException>(() => RodarCorte(dados.Caminho, saida.Caminho));

        Assert.Contains(erro.Faltando, f => f.Contains("A-100"));
        var pastaDeSobreposicoes = Path.Combine(saida.Caminho, Executor.PastaDeSobreposicoes);
        Assert.False(Directory.Exists(pastaDeSobreposicoes) && Directory.GetFiles(pastaDeSobreposicoes).Length > 0);
        Assert.False(File.Exists(Path.Combine(saida.Caminho, Executor.ArquivoDeResultadosCsv)));
    }

    [Fact]
    public void Intermediaria_compara_contra_a_forma_final_mais_o_proprio_stl()
    {
        using var dados = new PastaTemporaria();
        using var saidaB = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos, Distratores);
        var gruposDoB = RodarFormaFinal(dados.Caminho, saidaB.Caminho).First().Grupos;
        Assert.Equal(5, gruposDoB);

        // Um STL novo: um grupo a mais.
        var nova = new ChapaSintetica("I-NOVA", 90, 70, 10);
        ConjuntoSintetico.MontarIntermediarias(dados.Caminho, [(nova, ConjuntoSintetico.StlDaChapa(nova))]);
        var comNova = Executor.ExecutarComStl(ConjuntoDeDados.LerIntermediarias(dados.Caminho), Cenario.Intermediarias,
            Path.Combine(dados.Caminho, "saida-nova"), MetodoDeRecorte.Otsu, TextWriter.Null);
        Assert.All(comNova, l => Assert.Equal(gruposDoB + 1, l.Grupos));
        Assert.All(comNova, l => Assert.Equal("I01", l.Id));
        Assert.All(comNova, l => Assert.Null(l.Tipo));
        Assert.All(comNova, l => Assert.Equal(Cenario.Intermediarias, l.Cenario));

        // O mesmo STL de um do B: cai no grupo dele, e o número de grupos não muda.
        Directory.Delete(Path.Combine(dados.Caminho, "intermediarias"), recursive: true);
        var igual = new ChapaSintetica("I-IGUAL", 200, 100, 40);
        ConjuntoSintetico.MontarIntermediarias(dados.Caminho, [(igual, ConjuntoSintetico.StlDaChapa(igual))]);
        var comIgual = Executor.ExecutarComStl(ConjuntoDeDados.LerIntermediarias(dados.Caminho), Cenario.Intermediarias,
            Path.Combine(dados.Caminho, "saida-igual"), MetodoDeRecorte.Otsu, TextWriter.Null);
        Assert.All(comIgual, l => Assert.Equal(gruposDoB, l.Grupos));
        Assert.All(comIgual, l => Assert.Equal(2, l.ReferenciasNoGrupoDaCerta));
    }

    [Fact]
    public void Foto_com_marcador_sem_marcador_vira_falha_e_nao_posicao()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores, marcadorNaFotoComMarcador: false);

        var linhas = RodarFormaFinal(dados.Caminho, saida.Caminho);

        var com = linhas.Where(l => l.Condicao == Condicao.ComMarcador).ToList();
        Assert.Equal(2, com.Count);
        Assert.All(com, l => { Assert.Null(l.Posicao); Assert.Equal(Executor.FalhaMarcadorNaoDetectado, l.Falha); });
        Assert.All(linhas.Where(l => l.Condicao == Condicao.SemMarcador), l => Assert.NotNull(l.Posicao));
    }

    [Fact]
    public void Modelo_de_classificacao_so_tem_as_consultas_que_geraram_sobreposicao()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores, marcadorNaFotoComMarcador: false);

        RodarFormaFinal(dados.Caminho, saida.Caminho);

        var classificacao = ArquivoDeClassificacaoDoRecorte.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDeClassificacao));
        var unica = Assert.Single(classificacao);
        Assert.Equal(Condicao.SemMarcador, unica.Condicao);
        Assert.True(Executor.TemSobreposicao(null));
        Assert.True(Executor.TemSobreposicao(Executor.FalhaRecorteVazio));
        Assert.False(Executor.TemSobreposicao(Executor.FalhaMarcadorNaoDetectado));
        Assert.False(Executor.TemSobreposicao(Executor.FalhaFotoIlegivel));
        Assert.False(Executor.TemSobreposicao(Executor.FalhaReferenciaNaoSeparada));
    }

    [Fact]
    public void Nenhum_arquivo_da_saida_exceto_os_mapas_contem_codigo_ou_nome_de_nesting()
    {
        using var dadosCorte = new PastaTemporaria();
        using var dadosB = new PastaTemporaria();
        using var saidaCorte = new PastaTemporaria();
        using var saidaB = new PastaTemporaria();
        MontarCorteComTodos(dadosCorte.Caminho, blocoAusente: "BLOCO-RESERVADO");
        ConjuntoSintetico.MontarFormaFinal(dadosB.Caminho, Alvos, Distratores);

        RodarCorte(dadosCorte.Caminho, saidaCorte.Caminho);
        Executor.GerarImagensNumeradas(ConjuntoDeDados.LerNestings(dadosCorte.Caminho), saidaCorte.Caminho, SemCamadas, TextWriter.Null);
        RodarFormaFinal(dadosB.Caminho, saidaB.Caminho);

        // Positivo conhecido: os mapas têm os códigos e o nome do nesting; a varredura os acharia em qualquer outro lugar.
        Assert.Contains("B-200", File.ReadAllText(Path.Combine(saidaCorte.Caminho, Executor.ArquivoDeMapa)));
        Assert.Contains(ArquivoDoNesting, File.ReadAllText(Path.Combine(saidaCorte.Caminho, Executor.ArquivoDeMapaDeNestings)));
        Assert.Contains("B-200", File.ReadAllText(Path.Combine(saidaB.Caminho, Executor.ArquivoDeMapa)));

        var proibidos = Alvos.Select(a => a.Codigo).Append(ArquivoDoNesting).Append(Path.GetFileNameWithoutExtension(ArquivoDoNesting))
            .Append("caixa.stl").Append("quadrada.stl").ToList();
        var mapas = new[] { Executor.ArquivoDeMapa, Executor.ArquivoDeMapaDeNestings };
        var verificados = 0;
        foreach (var saida in new[] { saidaCorte.Caminho, saidaB.Caminho })
            foreach (var arquivo in Directory.GetFiles(saida, "*", SearchOption.AllDirectories)
                         .Where(f => !mapas.Contains(Path.GetFileName(f))))
            {
                verificados++;
                foreach (var proibido in proibidos)
                {
                    Assert.DoesNotContain(proibido, Path.GetRelativePath(saida, arquivo));
                    // O avisos-do-dxf.txt é local de propósito (traz o nome do bloco) e não leva peça nem nesting.
                    if (!arquivo.EndsWith(".png", StringComparison.Ordinal))
                        Assert.DoesNotContain(proibido, File.ReadAllText(arquivo));
                }
            }
        Assert.True(verificados > 15);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(saidaCorte.Caminho, Executor.PastaDeSobreposicoes), "*.png").Length);
        Assert.Contains("BLOCO-RESERVADO", File.ReadAllText(Path.Combine(saidaCorte.Caminho, Executor.ArquivoDeAvisosDoDxf)));
    }

    [Fact]
    public void O_log_nao_contem_codigo_nem_nome_de_nesting()
    {
        using var dadosCorte = new PastaTemporaria();
        using var dadosB = new PastaTemporaria();
        using var saidaCorte = new PastaTemporaria();
        using var saidaB = new PastaTemporaria();
        MontarCorteComTodos(dadosCorte.Caminho, blocoAusente: "BLOCO-RESERVADO");
        ConjuntoSintetico.MontarFormaFinal(dadosB.Caminho, Alvos, Distratores);
        var log = new StringWriter();

        RodarCorte(dadosCorte.Caminho, saidaCorte.Caminho, log);
        Executor.GerarImagensNumeradas(ConjuntoDeDados.LerNestings(dadosCorte.Caminho), saidaCorte.Caminho, SemCamadas, log);
        RodarFormaFinal(dadosB.Caminho, saidaB.Caminho, log);

        var texto = log.ToString();
        Assert.Contains("A01", texto); // positivo conhecido: o log leva ids
        Assert.Contains("Projetando candidato 1/5", texto);
        Assert.Contains("1x bloco não encontrado: INSERT ignorado", texto); // o aviso, contado por tipo
        // A regra do tipo: nome de bloco, de entidade e número saem; o resto do texto fica.
        Assert.Equal("blocos aninhados além do limite: INSERT ignorado", Executor.TipoDoAviso("blocos aninhados além de 8 níveis: INSERT de FLANGE-12 ignorado"));
        Assert.Equal("entidade(s) de um tipo não lida(s)", Executor.TipoDoAviso("3 entidade(s) SPLINE não lida(s)"));
        Assert.Equal("# trecho(s) aberto(s) ignorado(s)", Executor.TipoDoAviso("5 trecho(s) aberto(s) ignorado(s)"));
        foreach (var proibido in Alvos.Select(a => a.Codigo).Append(ArquivoDoNesting).Append("BLOCO-RESERVADO")
                     .Append("caixa.stl").Append("quadrada.stl"))
            Assert.DoesNotContain(proibido, texto);
    }

    [Fact]
    public void Resultados_pontuacoes_e_modelo_de_classificacao_voltam_do_disco()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        MontarCorteComTodos(dados.Caminho);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho);

        Assert.Equal(linhas, ArquivoDeResultados.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDeResultadosCsv)));

        // pontuacoes.csv: uma linha por grupo de cada consulta de IoU, e a conta da posição fecha com a do resultado.
        var pontuacoes = ArquivoDePontuacoes.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDePontuacoesCsv));
        var iou = linhas.Where(l => l.Pontuador == NomeDoPontuador.IoU && l.Falha is null).ToList();
        Assert.Equal(6, iou.Count);
        foreach (var linha in iou)
        {
            var doGrupo = pontuacoes.Where(p => p.Id == linha.Id && p.Condicao == linha.Condicao).ToList();
            Assert.All(doGrupo, p => Assert.Equal(NomeDoPontuador.IoU, p.Pontuador));
            Assert.Equal(linha.Grupos, doGrupo.Count);
            Assert.Matches(@"^G\d{4}$", doGrupo[0].Grupo);
            var certa = Assert.Single(doGrupo, p => p.Certo);
            var erradas = doGrupo.Where(p => !p.Certo).ToList();
            Assert.Equal(linha.PontuacaoDaCerta, certa.Pontuacao);
            Assert.Equal(linha.MelhorErrada, erradas.Max(p => p.Pontuacao));
            Assert.Equal(1 + erradas.Count(p => p.Pontuacao >= certa.Pontuacao), linha.Posicao);
        }
        Assert.DoesNotContain(pontuacoes, p => p.Pontuador == NomeDoPontuador.Hu);

        var classificacao = ArquivoDeClassificacaoDoRecorte.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDeClassificacao));
        Assert.Equal(6, classificacao.Count);
        Assert.All(classificacao, c => Assert.Null(c.RecorteOk));
    }

    [Fact]
    public void Nao_sobrescreve_classificacao_ja_preenchida()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores);
        var caminho = Path.Combine(saida.Caminho, Executor.ArquivoDeClassificacao);
        File.WriteAllLines(caminho, [ArquivoDeClassificacaoDoRecorte.Cabecalho, "B01;SemMarcador;ok", "B01;ComMarcador;falho"]);

        RodarFormaFinal(dados.Caminho, saida.Caminho);

        Assert.Equal([ArquivoDeClassificacaoDoRecorte.Cabecalho, "B01;SemMarcador;ok", "B01;ComMarcador;falho"], File.ReadAllLines(caminho));
    }

    [Fact]
    public void Pasta_incompleta_lista_o_que_falta_so_no_arquivo_local()
    {
        using var dados = new PastaTemporaria();
        var alvo = Path.Combine(dados.Caminho, "forma-final", "alvos", "X-SECRETO");
        Directory.CreateDirectory(alvo);
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "forma-final", "distratores"));
        File.WriteAllText(Path.Combine(dados.Caminho, "forma-final", "tipos.txt"), "");
        // Só a foto sem marcador existe: faltam o sólido, a foto com marcador e a linha do tipo.
        File.Copy(CopiaDeFoto(), Path.Combine(alvo, "sem-marcador.png"));

        var erro = Assert.Throws<DadosIncompletosException>(() => ConjuntoDeDados.LerFormaFinal(dados.Caminho));

        Assert.Equal(3, erro.Faltando.Count);
        Assert.Contains(erro.Faltando, f => f.EndsWith("solido.stl", StringComparison.Ordinal));
        Assert.Contains(erro.Faltando, f => f.Contains("com-marcador"));
        Assert.Contains(erro.Faltando, f => f.Contains("tipos.txt"));
        Assert.Equal("3 arquivo(s) ou pasta(s) ausente(s) ou inválido(s)", erro.Message);
        Assert.DoesNotContain("X-SECRETO", erro.Message);
    }

    [Fact]
    public void Foto_que_so_casa_com_uma_vista_que_nao_e_a_primeira_ainda_acha_o_alvo()
    {
        // Três alvos com o MESMO sólido (uma caixa 100x80x60, um grupo só) e fotos de faces diferentes:
        // cada foto casa com uma vista, e só uma delas é a primeira. A pontuação do grupo é a da melhor vista.
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        var caixa = MalhaSintetica.StlBinario(MalhaSintetica.Caixa(100, 80, 60));
        ChapaSintetica[] alvos = [new("C1", 100, 80, 0), new("C2", 100, 60, 0), new("C3", 80, 60, 0)];
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, alvos,
            [("quadrada.stl", Distratores[1].Item2), ("barra.stl", MalhaSintetica.StlBinario(MalhaSintetica.Caixa(200, 20, 20)))],
            stls: alvos.ToDictionary(a => a.Codigo, _ => caixa));

        var linhas = RodarFormaFinal(dados.Caminho, saida.Caminho);

        var comMarcador = linhas.Where(l => l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.Equal(3, comMarcador.Count);
        Assert.All(comMarcador, l => Assert.Equal(1, l.Posicao));
        Assert.All(comMarcador, l => Assert.Equal(3, l.ReferenciasNoGrupoDaCerta));
    }

    private static Mascara Retangulo(int largura, int altura)
    {
        var mat = new Mat(altura, largura, MatType.CV_8UC1, Scalar.All(255));
        return new Mascara(mat, 0.5);
    }

    [Fact]
    public void A_pontuacao_do_grupo_e_a_da_melhor_referencia_e_da_melhor_vista()
    {
        var assinatura = new AssinaturaGeometrica(0, [1, 2, 3, 4, 5]);
        using var foto = Retangulo(100, 50);
        using var ruim = Retangulo(20, 200);
        using var igual = Retangulo(100, 50);
        using var outraIgual = Retangulo(100, 50);
        using var outraRuim = Retangulo(20, 200);
        // K0001 (a primeira do grupo) só tem vistas ruins; K0002 tem uma ruim e depois a boa; K0003 é de outro grupo.
        var universo = Executor.Universo.De([
            new Referencia("K0001", [ruim], assinatura),
            new Referencia("K0002", [outraRuim, igual], assinatura),
            new Referencia("K0003", [outraIgual], new AssinaturaGeometrica(0, [9, 9, 9, 9, 9])),
        ]);
        Assert.Equal(2, universo.Grupos.Count);

        var pontuacoes = Executor.PontuarGrupos(universo, foto, NomeDoPontuador.IoU, usarEscala: false);

        Assert.True(pontuacoes["G0001"] > 0.99, $"o grupo vale {pontuacoes["G0001"]}: tem de ser o da melhor vista da melhor referência");
        Assert.True(pontuacoes["G0002"] > 0.99);
    }

    [Fact]
    public void Corte_com_dois_nestings_forma_um_universo_so_e_o_mapa_segue_a_ordem_ordinal()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        var alvo = Alvos[0];
        // O alvo está no SEGUNDO nesting; no primeiro há uma quase-gêmea (furo 22 em vez de 20).
        ConjuntoSintetico.MontarCorte(dados.Caminho, "b-segundo.dxf", [PecaDoDxf.De(alvo), new(100, 80, 0)], [(alvo, 1)]);
        ConjuntoSintetico.MontarNestingExtra(dados.Caminho, "a-primeiro.dxf", [new(100, 50, 22), new(120, 120, 30)]);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho);

        Assert.All(linhas, l => Assert.Equal(4, l.Grupos));
        Assert.Equal(["id;arquivo", "N01;a-primeiro.dxf", "N02;b-segundo.dxf"],
            File.ReadAllLines(Path.Combine(saida.Caminho, Executor.ArquivoDeMapaDeNestings)));
    }

    [Fact]
    public void Imagem_ilegivel_ou_que_nao_grava_falha_sem_dizer_o_caminho()
    {
        using var pasta = new PastaTemporaria();
        var ausente = Path.Combine(pasta.Caminho, "X-SECRETO.png");
        using (var lida = ImagensEmDisco.Ler(ausente)) Assert.True(lida.Empty());

        using var vazia = new Mat();
        var ex = Assert.ThrowsAny<Exception>(() => ImagensEmDisco.Gravar(Path.Combine(pasta.Caminho, "x.png"), vazia));
        Assert.False(File.Exists(Path.Combine(pasta.Caminho, "x.png")));
        Assert.DoesNotContain("X-SECRETO", ex.Message);
    }

    private static string CopiaDeFoto()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"busca-por-foto-foto-{Guid.NewGuid():N}.png");
        using var pasta = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(pasta.Caminho, Alvos.Take(1), []);
        File.Copy(Path.Combine(pasta.Caminho, "forma-final", "alvos", "A-100", "sem-marcador.png"), caminho);
        return caminho;
    }
}
