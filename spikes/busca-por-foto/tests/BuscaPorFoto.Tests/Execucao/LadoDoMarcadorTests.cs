using BuscaPorFoto.Cli;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;

namespace BuscaPorFoto.Tests.Execucao;

/// <summary>I5 da review de branch: o lado MEDIDO do marcador entra na execução (spec §4.2), e fica registrado.</summary>
public class LadoDoMarcadorTests
{
    private static readonly ChapaSintetica[] Alvos = [new("A-100", 100, 50, 20), new("B-200", 200, 100, 40)];
    private static readonly (string, byte[])[] Distratores = [("caixa.stl", MalhaSintetica.StlBinario(MalhaSintetica.Caixa(100, 80, 60)))];

    private static IReadOnlyList<LinhaDeResultado> RodarFormaFinal(string dados, string saida, double? lado = null) =>
        lado is { } l
            ? Executor.ExecutarComStl(ConjuntoDeDados.LerFormaFinal(dados), Cenario.FormaFinal, saida, MetodoDeRecorte.Otsu, TextWriter.Null, l)
            : Executor.ExecutarComStl(ConjuntoDeDados.LerFormaFinal(dados), Cenario.FormaFinal, saida, MetodoDeRecorte.Otsu, TextWriter.Null);

    private static double PontuacaoDaCertaComMarcador(IReadOnlyList<LinhaDeResultado> linhas) =>
        linhas.Single(l => l.Id == "B01" && l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU).PontuacaoDaCerta!.Value;

    [Fact]
    public void O_lado_chega_ao_retificador_e_muda_a_escala_da_foto()
    {
        // As fotos sintéticas têm o marcador com 100 mm. Dizer que ele mede 70 estica a foto em 30%: a peça
        // deixa de caber na referência, e a pontuação com marcador cai. Se o executor ignorasse o lado, as duas
        // execuções dariam o mesmo número.
        using var dados = new PastaTemporaria();
        using var saidaCerta = new PastaTemporaria();
        using var saidaErrada = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores);

        var certa = PontuacaoDaCertaComMarcador(RodarFormaFinal(dados.Caminho, saidaCerta.Caminho, 100));
        var padrao = PontuacaoDaCertaComMarcador(RodarFormaFinal(dados.Caminho, saidaErrada.Caminho));
        using var saida70 = new PastaTemporaria();
        var com70 = PontuacaoDaCertaComMarcador(RodarFormaFinal(dados.Caminho, saida70.Caminho, 70));

        Assert.Equal(certa, padrao);
        Assert.True(certa > 0.85, $"com o lado certo a certa vale {certa}");
        Assert.True(com70 < certa - 0.1, $"com 70 a certa vale {com70}, com 100 vale {certa}");
    }

    [Fact]
    public void O_lado_fica_no_arquivo_de_execucao_nos_tres_cenarios_e_o_padrao_e_100()
    {
        using var dados = new PastaTemporaria();
        using var padrao = new PastaTemporaria();
        using var medido = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores);

        RodarFormaFinal(dados.Caminho, padrao.Caminho);
        RodarFormaFinal(dados.Caminho, medido.Caminho, 98.5);

        Assert.Contains("lado-do-marcador=100", File.ReadAllLines(Path.Combine(padrao.Caminho, Executor.ArquivoDeExecucao)));
        Assert.Contains("lado-do-marcador=98.5", File.ReadAllLines(Path.Combine(medido.Caminho, Executor.ArquivoDeExecucao)));
    }

    [Fact]
    public void Corte_grava_o_lado_no_arquivo_de_execucao()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarCorte(dados.Caminho, "n.dxf", [PecaDoDxf.De(Alvos[0]), new(120, 120, 30)], [(Alvos[0], 1)]);

        Executor.ExecutarCorte(ConjuntoDeDados.LerCorte(dados.Caminho), saida.Caminho, MetodoDeRecorte.Otsu,
            new HashSet<string>(), TextWriter.Null, ladoDoMarcadorMm: 97.25);

        Assert.Contains("lado-do-marcador=97.25", File.ReadAllLines(Path.Combine(saida.Caminho, Executor.ArquivoDeExecucao)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Lado_que_nao_e_positivo_e_recusado_antes_de_gravar(double lado)
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores);

        Assert.Throws<ArgumentOutOfRangeException>(() => RodarFormaFinal(dados.Caminho, saida.Caminho, lado));
        Assert.Empty(Directory.GetFileSystemEntries(saida.Caminho));
    }

    private static (int Codigo, string Saida, string Erro) Cli(params string[] args)
    {
        var saida = new StringWriter();
        var erro = new StringWriter();
        var codigo = ComandoDaCli.Rodar(args, saida, erro);
        return (codigo, saida.ToString(), erro.ToString());
    }

    [Theory]
    [InlineData("98,5", "98.5")]
    [InlineData("98.5", "98.5")]
    [InlineData("100", "100")]
    public void A_opcao_da_linha_de_comando_aceita_virgula_ou_ponto_e_chega_ao_execucao_txt(string digitado, string gravado)
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, Alvos.Take(1), Distratores);

        var (codigo, _, _) = Cli("executar", dados.Caminho, saida.Caminho, "forma-final", "--lado-do-marcador", digitado);

        Assert.Equal(0, codigo);
        Assert.Contains($"lado-do-marcador={gravado}", File.ReadAllLines(Path.Combine(saida.Caminho, Executor.ArquivoDeExecucao)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("")]
    public void A_opcao_com_valor_invalido_devolve_o_uso(string valor)
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();

        Assert.Equal(2, Cli("executar", dados.Caminho, saida.Caminho, "forma-final", "--lado-do-marcador", valor).Codigo);
        Assert.Equal(2, Cli("executar", dados.Caminho, saida.Caminho, "corte", "--lado-do-marcador", valor).Codigo);
    }

    [Fact]
    public void O_comando_nesting_nao_aceita_o_lado_do_marcador()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();

        Assert.Equal(2, Cli("nesting", dados.Caminho, saida.Caminho, "--lado-do-marcador", "100").Codigo);
    }
}
