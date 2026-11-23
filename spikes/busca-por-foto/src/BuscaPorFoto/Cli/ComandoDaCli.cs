using BuscaPorFoto.Dxf;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Marcador;
using BuscaPorFoto.Relatorio;
using OpenCvSharp;

namespace BuscaPorFoto.Cli;

/// <summary>
/// A linha de comando. O terminal nunca recebe código de peça nem nome de arquivo do usuário: o que
/// falta vai para <c>faltando.txt</c>, o que dá errado (tipo, mensagem e pilha) para <c>erro.txt</c>, e
/// o terminal só diz quantos e onde.
/// </summary>
public static class ComandoDaCli
{
    public const string Uso = """
        Uso:
          BuscaPorFoto.Cli marcador <arquivo.pdf>
          BuscaPorFoto.Cli nesting <dados> <saida> [--ignorar-camada <nome>]... [--borda auto|sim|nao]
          BuscaPorFoto.Cli executar <dados> <saida> corte [--recorte otsu|grabcut] [--ignorar-camada <nome>]... [--borda auto|sim|nao]
          BuscaPorFoto.Cli executar <dados> <saida> forma-final|intermediarias [--recorte otsu|grabcut]
          BuscaPorFoto.Cli relatorio <saida>
        """;

    private sealed record OpcoesDoComando(MetodoDeRecorte Recorte, HashSet<string> Camadas, BordaDaChapa Borda);

    public static int Rodar(string[] args, TextWriter saida, TextWriter erro)
    {
        string? pastaDeSaida = null;
        try
        {
            // O log nativo do OpenCV escreve caminhos de arquivo (com código de peça) no stderr.
            Cv2.SetLogLevel(LogLevel.SILENT);
            switch (args)
            {
                case ["marcador", var pdf]:
                    File.WriteAllBytes(pdf, PdfDoMarcador.Gerar());
                    saida.WriteLine($"Marcador gravado em {Path.GetFullPath(pdf)}");
                    return 0;

                case ["nesting", var dados, var pasta, .. var resto]:
                {
                    if (LerOpcoes(resto, aceitaRecorte: false, aceitaCamadaEBorda: true) is not { } o) break;
                    pastaDeSaida = pasta;
                    var n = Executor.GerarImagensNumeradas(ConjuntoDeDados.LerNestings(dados), pasta, o.Camadas, saida, o.Borda);
                    saida.WriteLine($"{n} nesting(s); imagens em {Path.GetFullPath(Path.Combine(pasta, Executor.PastaDeNestings))}");
                    saida.WriteLine($"Abra as imagens e escreva o alvos.txt (codigo, arquivo do nesting, numero), com a lista em {Path.GetFullPath(Path.Combine(pasta, Executor.ArquivoDeMapaDeNestings))}");
                    return 0;
                }

                case ["executar", var dados, var pasta, var nome, .. var resto]:
                {
                    if (CenarioExtensoes.DaPasta(nome) is not { } cenario) break;
                    var corte = cenario == Cenario.Corte;
                    if (LerOpcoes(resto, aceitaRecorte: true, aceitaCamadaEBorda: corte) is not { } o) break;
                    pastaDeSaida = pasta;

                    var linhas = cenario switch
                    {
                        Cenario.Corte => Executor.ExecutarCorte(ConjuntoDeDados.LerCorte(dados), pasta, o.Recorte, o.Camadas, saida, o.Borda),
                        Cenario.FormaFinal => Executor.ExecutarComStl(ConjuntoDeDados.LerFormaFinal(dados), cenario, pasta, o.Recorte, saida),
                        _ => Executor.ExecutarComStl(ConjuntoDeDados.LerIntermediarias(dados), cenario, pasta, o.Recorte, saida),
                    };
                    saida.WriteLine($"{linhas.Count} linhas em {Path.GetFullPath(Path.Combine(pasta, Executor.ArquivoDeResultadosCsv))}");
                    saida.WriteLine($"Classifique o recorte em {Path.GetFullPath(Path.Combine(pasta, Executor.ArquivoDeClassificacao))} olhando {Path.GetFullPath(Path.Combine(pasta, Executor.PastaDeSobreposicoes))}.");
                    return 0;
                }

                case ["relatorio", var pasta]:
                {
                    pastaDeSaida = pasta;
                    var cenario = LerDaExecucao(pasta, "cenario", v => CenarioExtensoes.DaPasta(v));
                    var metodo = LerDaExecucao(pasta, "recorte", v => Enum.TryParse<MetodoDeRecorte>(v, ignoreCase: true, out var m) ? m : (MetodoDeRecorte?)null);
                    var linhas = ArquivoDeResultados.Ler(Path.Combine(pasta, Executor.ArquivoDeResultadosCsv));
                    var pontuacoes = ArquivoDePontuacoes.Ler(Path.Combine(pasta, Executor.ArquivoDePontuacoesCsv));
                    var classificacao = ArquivoDeClassificacaoDoRecorte.Ler(Path.Combine(pasta, Executor.ArquivoDeClassificacao));
                    var caminho = Path.GetFullPath(Path.Combine(pasta, "relatorio.md"));
                    File.WriteAllText(caminho, GeradorDeRelatorio.Gerar(cenario, linhas, pontuacoes, classificacao, metodo));
                    // Só o caminho e a palavra do veredito: nada dos dados vai para o terminal.
                    saida.WriteLine($"Relatório em {caminho}");
                    saida.WriteLine($"{cenario.NomeDaPasta()}: {(GeradorDeRelatorio.VereditoDoCenario(cenario, linhas) is { } v ? v.ToString() : "sem veredito (só informativo ou sem alvo)")}");
                    return 0;
                }
            }
        }
        catch (DadosIncompletosException ex)
        {
            var caminho = Gravar(pastaDeSaida, Executor.ArquivoDeFaltas, string.Join(Environment.NewLine, ex.Faltando));
            erro.WriteLine($"{ex.Faltando.Count} ausente(s); a lista está em {caminho}");
            return 1;
        }
        catch (Exception ex)
        {
            var caminho = Gravar(pastaDeSaida, Executor.ArquivoDeErro, $"{ex.GetType().FullName}{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.StackTrace}");
            erro.WriteLine($"erro: {ex.GetType().Name}; detalhes em {caminho}");
            return 1;
        }

        erro.WriteLine(Uso);
        return 2;
    }

    /// <summary>
    /// Grava em <paramref name="pasta"/> e devolve o caminho completo. Se o comando não tem saída ou ela não
    /// abre, grava numa pasta nova do diretório temporário — nunca no diretório atual, que pode estar dentro
    /// do repositório (público), e o arquivo leva código de peça.
    /// </summary>
    private static string Gravar(string? pasta, string nome, string texto)
    {
        if (pasta is not null)
            try
            {
                Directory.CreateDirectory(pasta);
                var caminho = Path.GetFullPath(Path.Combine(pasta, nome));
                File.WriteAllText(caminho, texto);
                return caminho;
            }
            catch (Exception) { /* cai no diretório atual */ }
        var temporario = Path.Combine(Directory.CreateTempSubdirectory("busca-por-foto-").FullName, nome);
        File.WriteAllText(temporario, texto);
        return temporario;
    }

    /// <summary>Lê uma chave de <c>execucao.txt</c>; o valor inválido é erro inesperado (a mensagem vai para o erro.txt).</summary>
    private static T LerDaExecucao<T>(string pasta, string chave, Func<string, T?> converter) where T : struct
    {
        var prefixo = chave + "=";
        var valor = File.ReadAllLines(Path.Combine(pasta, Executor.ArquivoDeExecucao))
            .FirstOrDefault(l => l.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..].Trim();
        return valor is not null && converter(valor) is { } resultado
            ? resultado
            : throw new InvalidDataException($"{Executor.ArquivoDeExecucao} sem uma chave '{chave}' válida.");
    }

    private static OpcoesDoComando? LerOpcoes(string[] args, bool aceitaRecorte, bool aceitaCamadaEBorda)
    {
        var recorte = MetodoDeRecorte.Otsu;
        var camadas = new HashSet<string>(StringComparer.Ordinal);
        var borda = BordaDaChapa.Auto;
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) return null;
            var valor = args[i + 1];
            switch (args[i])
            {
                case "--recorte" when aceitaRecorte:
                    if (valor == "otsu") recorte = MetodoDeRecorte.Otsu;
                    else if (valor == "grabcut") recorte = MetodoDeRecorte.GrabCut;
                    else return null;
                    break;
                case "--ignorar-camada" when aceitaCamadaEBorda:
                    camadas.Add(valor);
                    break;
                case "--borda" when aceitaCamadaEBorda:
                    if (valor == "auto") borda = BordaDaChapa.Auto;
                    else if (valor == "sim") borda = BordaDaChapa.Sim;
                    else if (valor == "nao") borda = BordaDaChapa.Nao;
                    else return null;
                    break;
                default:
                    return null;
            }
        }
        return new OpcoesDoComando(recorte, camadas, borda);
    }
}
