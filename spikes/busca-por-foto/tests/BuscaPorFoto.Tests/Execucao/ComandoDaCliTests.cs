using BuscaPorFoto.Cli;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Tests.Sinteticos;

namespace BuscaPorFoto.Tests.Execucao;

public class ComandoDaCliTests
{
    private static (int Codigo, string Saida, string Erro) Rodar(params string[] args)
    {
        var saida = new StringWriter();
        var erro = new StringWriter();
        var codigo = ComandoDaCli.Rodar(args, saida, erro);
        return (codigo, saida.ToString(), erro.ToString());
    }

    [Fact]
    public void Pasta_incompleta_imprime_so_a_contagem_e_o_caminho_da_lista()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "forma-final", "alvos", "X-SECRETO"));
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "forma-final", "distratores"));
        File.WriteAllText(Path.Combine(dados.Caminho, "forma-final", "tipos.txt"), "");

        var (codigo, textoDaSaida, textoDoErro) = Rodar("executar", dados.Caminho, saida.Caminho, "forma-final");

        Assert.Equal(1, codigo);
        Assert.DoesNotContain("X-SECRETO", textoDaSaida);
        Assert.DoesNotContain("X-SECRETO", textoDoErro);
        var lista = Path.Combine(saida.Caminho, Executor.ArquivoDeFaltas);
        Assert.Contains("ausente(s); a lista está em", textoDoErro);
        Assert.Contains(lista, textoDoErro);
        Assert.Contains("X-SECRETO", File.ReadAllText(lista));
    }

    [Fact]
    public void Erro_inesperado_imprime_so_o_tipo_e_o_arquivo_de_erro()
    {
        using var raiz = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        var inexistente = Path.Combine(raiz.Caminho, "X-SECRETO");

        var (codigo, textoDaSaida, textoDoErro) = Rodar("executar", inexistente, saida.Caminho, "corte");

        Assert.Equal(1, codigo);
        Assert.DoesNotContain("X-SECRETO", textoDaSaida);
        Assert.DoesNotContain("X-SECRETO", textoDoErro);
        var arquivo = Path.Combine(saida.Caminho, Executor.ArquivoDeErro);
        Assert.Contains($"erro: DirectoryNotFoundException; detalhes em {arquivo}", textoDoErro);
        Assert.Contains("X-SECRETO", File.ReadAllText(arquivo));
    }

    [Fact]
    public void Uso_errado_devolve_2()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();

        Assert.Equal(2, Rodar("executar", dados.Caminho, saida.Caminho).Codigo);
        Assert.Equal(2, Rodar("executar", dados.Caminho, saida.Caminho, "cenario-que-nao-existe").Codigo);
        Assert.Equal(2, Rodar("executar", dados.Caminho, saida.Caminho, "corte", "--borda", "talvez").Codigo);
        Assert.Equal(2, Rodar("executar", dados.Caminho, saida.Caminho, "forma-final", "--borda", "nao").Codigo);
        Assert.Equal(2, Rodar("nesting", dados.Caminho, saida.Caminho, "--recorte", "otsu").Codigo);
        Assert.Equal(2, Rodar().Codigo);
        Assert.Contains("Uso:", Rodar("executar", dados.Caminho, saida.Caminho).Erro);
    }

    [Fact]
    public void Marcador_grava_o_pdf()
    {
        using var pasta = new PastaTemporaria();
        var pdf = Path.Combine(pasta.Caminho, "marcador.pdf");

        var (codigo, _, _) = Rodar("marcador", pdf);

        Assert.Equal(0, codigo);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf).Take(8).ToArray()));
    }

    [Fact]
    public void Borda_e_camada_ignorada_chegam_ao_arquivo_de_execucao()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        var alvo = new ChapaSintetica("A-100", 100, 50, 20);
        ConjuntoSintetico.MontarCorte(dados.Caminho, "n.dxf", [PecaDoDxf.De(alvo), new(120, 120, 30)], [(alvo, 1)]);

        var (codigo, _, _) = Rodar("executar", dados.Caminho, saida.Caminho, "corte", "--borda", "nao", "--ignorar-camada", "COTAS");

        Assert.Equal(0, codigo);
        var execucao = File.ReadAllLines(Path.Combine(saida.Caminho, Executor.ArquivoDeExecucao));
        Assert.Contains("borda=nao", execucao);
        Assert.Contains("camadas-ignoradas=COTAS", execucao);
        Assert.Contains("cenario=corte", execucao);
        Assert.Contains("recorte=otsu", execucao);
    }
}
