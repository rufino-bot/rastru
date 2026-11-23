using BuscaPorFoto.Execucao;

namespace BuscaPorFoto.Tests.Execucao;

public class TextoEmDiscoTests
{
    [Fact]
    public void Grava_uma_linha_por_item_com_barra_n_e_sem_BOM()
    {
        using var pasta = new PastaTemporaria();
        var caminho = Path.Combine(pasta.Caminho, "mapa.csv");

        TextoEmDisco.GravarLinhas(caminho, ["id;codigo", "A01;X-100"]);

        Assert.Equal("id;codigo\nA01;X-100\n"u8.ToArray(), File.ReadAllBytes(caminho));
    }

    [Fact]
    public void Gravar_nao_acrescenta_BOM()
    {
        using var pasta = new PastaTemporaria();
        var caminho = Path.Combine(pasta.Caminho, "r.md");

        TextoEmDisco.Gravar(caminho, "ação\n");

        Assert.Equal(new byte[] { 0x61, 0xC3, 0xA7, 0xC3, 0xA3, 0x6F, 0x0A }, File.ReadAllBytes(caminho));
    }

    [Fact]
    public void O_codigo_do_spike_nao_grava_texto_por_outro_caminho()
    {
        // Guarda do fim de linha: no Linux o final de linha do sistema é "\n" e nenhum teste de saída veria
        // um "\r" que só o Windows poria. A varredura de anonimização da Task 10 depende de "\n" em todo
        // arquivo; então só o TextoEmDisco grava, e este teste morre se outra escrita aparecer.
        var raiz = RaizDoCodigo();
        var proibidos = new[] { "File.WriteAllLines(", "File.AppendAllLines(", "Environment.NewLine", "AppendLine(", "StreamWriter" };
        var achados = Directory.GetFiles(raiz, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "TextoEmDisco.cs")
            .SelectMany(f => proibidos.Where(File.ReadAllText(f).Contains).Select(p => $"{Path.GetFileName(f)}: {p}"))
            .ToList();

        Assert.Empty(achados);
        Assert.True(Directory.GetFiles(raiz, "Executor.cs", SearchOption.AllDirectories).Length == 1); // positivo: achou o código
    }

    private static string RaizDoCodigo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidata = Path.Combine(dir.FullName, "src", "BuscaPorFoto");
            if (File.Exists(Path.Combine(candidata, "BuscaPorFoto.csproj"))) return candidata;
        }
        throw new InvalidOperationException("Não achei src/BuscaPorFoto acima do diretório de teste.");
    }
}
