using BuscaPorFoto.Cli;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Tests.Sinteticos;
using System.Diagnostics;

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

    private static readonly ChapaSintetica[] AlvosDoCorte = [new("A-100", 100, 50, 20), new("B-200", 200, 100, 40)];
    private const string NestingSecreto = "chapa-SECRETA-07.dxf";

    private static void MontarCorteParaCli(string dados) =>
        ConjuntoSintetico.MontarCorte(dados, NestingSecreto, [.. AlvosDoCorte.Select(PecaDoDxf.De), new(120, 120, 30)],
            AlvosDoCorte.Select((a, i) => (a, (int?)(i + 1))).ToList());

    [Fact]
    public void Nesting_com_sucesso_imprime_so_ids_e_contagens()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        MontarCorteParaCli(dados.Caminho);

        var (codigo, textoDaSaida, textoDoErro) = Rodar("nesting", dados.Caminho, saida.Caminho);

        Assert.Equal(0, codigo);
        Assert.Contains("N01: 3 peça(s)", textoDaSaida); // positivo conhecido
        foreach (var proibido in new[] { "A-100", "B-200", NestingSecreto, "SECRETA" })
        {
            Assert.DoesNotContain(proibido, textoDaSaida);
            Assert.DoesNotContain(proibido, textoDoErro);
        }
    }

    [Fact]
    public void Executar_com_sucesso_imprime_so_ids_e_contagens()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        MontarCorteParaCli(dados.Caminho);

        var (codigo, textoDaSaida, textoDoErro) = Rodar("executar", dados.Caminho, saida.Caminho, "corte");

        Assert.Equal(0, codigo);
        Assert.Contains("Consultando A01 SemMarcador", textoDaSaida); // positivo conhecido
        foreach (var proibido in new[] { "A-100", "B-200", NestingSecreto, "SECRETA" })
        {
            Assert.DoesNotContain(proibido, textoDaSaida);
            Assert.DoesNotContain(proibido, textoDoErro);
        }
    }

    [Fact]
    public async Task Foto_ilegivel_no_executavel_de_verdade_nao_vaza_o_codigo_pelo_log_do_OpenCV()
    {
        // O OpenCV escreve o caminho do arquivo no stderr nativo quando não abre a foto: só um processo
        // filho, com o executável real, vê isso.
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.MontarFormaFinal(dados.Caminho, [new ChapaSintetica("X-SECRETO", 100, 50, 20)], []);
        var foto = Path.Combine(dados.Caminho, "forma-final", "alvos", "X-SECRETO", "sem-marcador.png");
        File.Delete(foto);
        File.CreateSymbolicLink(foto, Path.Combine(dados.Caminho, "nao-existe.png")); // link quebrado

        var dir = AppContext.BaseDirectory.TrimEnd('/');
        var cli = Path.GetFullPath(Path.Combine(dir, "../../../../..", "src/BuscaPorFoto.Cli/bin",
            Path.GetFileName(Path.GetDirectoryName(dir))!, Path.GetFileName(dir), "BuscaPorFoto.Cli.dll"));
        Assert.True(File.Exists(cli), cli);
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { cli, "executar", dados.Caminho, saida.Caminho, "forma-final" }) info.ArgumentList.Add(a);
        using var processo = Process.Start(info)!;
        var lerSaida = processo.StandardOutput.ReadToEndAsync();
        var lerErro = processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();

        var textoDaSaida = await lerSaida;
        var textoDoErro = await lerErro;
        Assert.Contains("Consultando B01", textoDaSaida); // positivo conhecido: rodou de verdade
        Assert.DoesNotContain("X-SECRETO", textoDaSaida);
        Assert.DoesNotContain("X-SECRETO", textoDoErro);
    }

    [Fact]
    public void Saida_inutilizavel_grava_a_lista_no_temporario_e_nao_no_diretorio_atual()
    {
        using var dados = new PastaTemporaria();
        using var pasta = new PastaTemporaria();
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "forma-final", "alvos", "X-SECRETO"));
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "forma-final", "distratores"));
        File.WriteAllText(Path.Combine(dados.Caminho, "forma-final", "tipos.txt"), "");
        var arquivo = Path.Combine(pasta.Caminho, "e-um-arquivo");
        File.WriteAllText(arquivo, "");

        var (codigo, _, textoDoErro) = Rodar("executar", dados.Caminho, Path.Combine(arquivo, "saida"), "forma-final");

        Assert.Equal(1, codigo);
        var lista = textoDoErro.Split("a lista está em ")[1].Trim();
        try
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), lista);
            Assert.False(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), Executor.ArquivoDeFaltas)));
            Assert.Contains("X-SECRETO", File.ReadAllText(lista));
        }
        finally { Directory.Delete(Path.GetDirectoryName(lista)!, recursive: true); }
    }
}
