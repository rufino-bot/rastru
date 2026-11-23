using BuscaPorFoto.Cli;
using BuscaPorFoto.Dxf;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;

namespace BuscaPorFoto.Tests.Execucao;

/// <summary>
/// I2 da review de branch: o número do alvos.txt vem do comando "nesting", e o "executar corte" separa de novo.
/// Opção ou contagem diferente muda a numeração sem que nada avise; o executar tem de recusar.
/// </summary>
public class ConferenciaDoNestingTests
{
    private const string Nesting = "chapa-SECRETA-07.dxf";
    private static readonly ChapaSintetica[] Alvos = [new("A-100", 100, 50, 20), new("B-200", 200, 100, 40)];
    private static readonly IReadOnlySet<string> SemCamadas = new HashSet<string>();

    private static List<PecaDoDxf> Pecas(int distratores = 1) =>
        [.. Alvos.Select(PecaDoDxf.De), .. Enumerable.Range(0, distratores).Select(i => new PecaDoDxf(120 + 10 * i, 120, 30))];

    private static void Montar(string dados) =>
        ConjuntoSintetico.MontarCorte(dados, Nesting, Pecas(), Alvos.Select((a, i) => (a, (int?)(i + 1))).ToList());

    private static void RodarNesting(string dados, string pastaDoNesting, IReadOnlySet<string>? camadas = null, BordaDaChapa borda = BordaDaChapa.Auto) =>
        Executor.GerarImagensNumeradas(ConjuntoDeDados.LerNestings(dados), pastaDoNesting, camadas ?? SemCamadas, TextWriter.Null, borda);

    private static IReadOnlyList<LinhaDeResultado> RodarCorte(
        string dados, string saida, string? conferir, IReadOnlySet<string>? camadas = null, BordaDaChapa borda = BordaDaChapa.Auto) =>
        Executor.ExecutarCorte(ConjuntoDeDados.LerCorte(dados), saida, MetodoDeRecorte.Otsu, camadas ?? SemCamadas, TextWriter.Null,
            borda, conferirNesting: conferir);

    [Fact]
    public void O_comando_nesting_grava_as_opcoes_e_a_contagem_de_pecas_por_nesting()
    {
        using var dados = new PastaTemporaria();
        using var pasta = new PastaTemporaria();
        Montar(dados.Caminho);

        RodarNesting(dados.Caminho, pasta.Caminho, new HashSet<string> { "COTAS", "ANOTACAO" }, BordaDaChapa.Nao);

        Assert.Equal(["borda=nao", "camadas-ignoradas=ANOTACAO,COTAS", "N01=3"],
            File.ReadAllLines(Path.Combine(pasta.Caminho, ConferenciaDoNesting.Arquivo)));
    }

    [Fact]
    public void Mesmas_opcoes_e_mesma_contagem_rodam_normalmente()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);
        RodarNesting(dados.Caminho, nesting.Caminho);

        var linhas = RodarCorte(dados.Caminho, saida.Caminho, conferir: nesting.Caminho);

        Assert.NotEmpty(linhas);
    }

    [Fact]
    public void Borda_diferente_entre_o_nesting_e_o_executar_recusa_antes_de_gravar_qualquer_coisa()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);
        RodarNesting(dados.Caminho, nesting.Caminho, borda: BordaDaChapa.Auto);

        var erro = Assert.Throws<DadosIncompletosException>(() =>
            RodarCorte(dados.Caminho, saida.Caminho, conferir: nesting.Caminho, borda: BordaDaChapa.Nao));

        Assert.Equal("1 arquivo(s) ou pasta(s) ausente(s) ou inválido(s)", erro.Message);
        var detalhe = Assert.Single(erro.Faltando);
        Assert.Contains("borda era 'auto'", detalhe);
        Assert.Contains("é 'nao'", detalhe);
        Assert.Empty(Directory.GetFileSystemEntries(saida.Caminho));
    }

    [Fact]
    public void Camada_ignorada_diferente_recusa()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);
        RodarNesting(dados.Caminho, nesting.Caminho);

        var erro = Assert.Throws<DadosIncompletosException>(() =>
            RodarCorte(dados.Caminho, saida.Caminho, conferir: nesting.Caminho, camadas: new HashSet<string> { "COTAS" }));

        Assert.Contains("camadas-ignoradas era ''", Assert.Single(erro.Faltando));
    }

    [Fact]
    public void Contagem_de_pecas_diferente_recusa_e_diz_qual_nesting()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);
        RodarNesting(dados.Caminho, nesting.Caminho);
        // O DXF mudou depois da imagem numerada: uma peça a mais, e o número do alvos.txt já não vale.
        File.WriteAllText(Path.Combine(dados.Caminho, "corte", "nestings", Nesting), ConjuntoSintetico.DxfDoNesting(Pecas(distratores: 2)));

        var erro = Assert.Throws<DadosIncompletosException>(() =>
            RodarCorte(dados.Caminho, saida.Caminho, conferir: nesting.Caminho));

        var detalhe = Assert.Single(erro.Faltando);
        Assert.Contains("N01 tinha 3 peça(s) no comando nesting e tem 4 nesta execução", detalhe);
        Assert.DoesNotContain("SECRETA", detalhe);
    }

    [Fact]
    public void Pasta_sem_o_arquivo_do_nesting_recusa()
    {
        using var dados = new PastaTemporaria();
        using var vazia = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);

        var erro = Assert.Throws<DadosIncompletosException>(() => RodarCorte(dados.Caminho, saida.Caminho, conferir: vazia.Caminho));

        Assert.Contains("rode o comando nesting", Assert.Single(erro.Faltando));
    }

    [Fact]
    public void Sem_a_opcao_de_conferir_nada_e_conferido()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);

        Assert.NotEmpty(RodarCorte(dados.Caminho, saida.Caminho, conferir: null, borda: BordaDaChapa.Nao));
    }

    // ---- pela linha de comando: o terminal só tem a contagem e os ids ----

    private static (int Codigo, string Saida, string Erro) Cli(params string[] args)
    {
        var saida = new StringWriter();
        var erro = new StringWriter();
        var codigo = ComandoDaCli.Rodar(args, saida, erro);
        return (codigo, saida.ToString(), erro.ToString());
    }

    [Fact]
    public void Executar_com_borda_diferente_do_nesting_devolve_1_com_so_a_contagem_e_a_lista_no_faltando_txt()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);
        Assert.Equal(0, Cli("nesting", dados.Caminho, nesting.Caminho, "--borda", "sim").Codigo);

        var (codigo, textoDaSaida, textoDoErro) = Cli("executar", dados.Caminho, saida.Caminho, "corte", "--conferir-nesting", nesting.Caminho);

        Assert.Equal(1, codigo);
        var lista = Path.Combine(saida.Caminho, Executor.ArquivoDeFaltas);
        Assert.Contains($"1 ausente(s); a lista está em {lista}", textoDoErro);
        Assert.Contains("borda era 'sim'", File.ReadAllText(lista));
        foreach (var proibido in new[] { "A-100", "B-200", Nesting, "SECRETA" })
        {
            Assert.DoesNotContain(proibido, textoDaSaida);
            Assert.DoesNotContain(proibido, textoDoErro);
        }
    }

    [Fact]
    public void Executar_com_as_mesmas_opcoes_imprime_as_mesmas_contagens_do_nesting()
    {
        using var dados = new PastaTemporaria();
        using var nesting = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Montar(dados.Caminho);

        var doNesting = Cli("nesting", dados.Caminho, nesting.Caminho);
        var doExecutar = Cli("executar", dados.Caminho, saida.Caminho, "corte", "--conferir-nesting", nesting.Caminho);

        Assert.Equal(0, doNesting.Codigo);
        Assert.Equal(0, doExecutar.Codigo);
        Assert.Contains("N01: 3 peça(s)", doNesting.Saida);
        Assert.Contains("N01: 3 peça(s)", doExecutar.Saida);
        Assert.Contains("--conferir-nesting", doNesting.Saida); // o comando nesting lembra de conferir
    }

    [Fact]
    public void A_opcao_de_conferir_so_existe_no_executar_corte()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();

        Assert.Equal(2, Cli("executar", dados.Caminho, saida.Caminho, "forma-final", "--conferir-nesting", saida.Caminho).Codigo);
        Assert.Equal(2, Cli("nesting", dados.Caminho, saida.Caminho, "--conferir-nesting", saida.Caminho).Codigo);
    }
}
