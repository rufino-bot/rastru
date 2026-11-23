using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Relatorio;

namespace BuscaPorFoto.Tests.Relatorio;

public class CriterioDeDecisaoTests
{
    [Theory]
    [InlineData(20, 20, Veredito.Aprova)]
    [InlineData(16, 20, Veredito.Aprova)]
    [InlineData(15, 20, Veredito.ZonaCinza)]
    [InlineData(12, 20, Veredito.ZonaCinza)]
    [InlineData(11, 20, Veredito.Reprova)]
    [InlineData(15, 18, Veredito.Aprova)]
    [InlineData(14, 18, Veredito.ZonaCinza)]
    [InlineData(11, 18, Veredito.ZonaCinza)]
    [InlineData(10, 18, Veredito.Reprova)]
    [InlineData(17, 21, Veredito.Aprova)]
    [InlineData(16, 21, Veredito.ZonaCinza)]
    [InlineData(13, 21, Veredito.ZonaCinza)]
    [InlineData(12, 21, Veredito.Reprova)]
    public void Aplica_os_limites_da_spec(int acertos, int total, Veredito esperado)
    {
        Assert.Equal(esperado, CriterioDeDecisao.Avaliar(acertos, total));
    }

    [Fact]
    public void Sem_alvo_nao_ha_decisao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CriterioDeDecisao.Avaliar(0, 0));
    }
}

public class GeradorDeRelatorioTests
{
    private const string NaoSeparada = "referência não separada do nesting";

    private static string Id(int indice) => $"A{indice + 1:D2}";

    /// <summary>
    /// Um bloco de alvos do mesmo tipo, a partir do índice dado. Os <paramref name="acertosSem"/> primeiros
    /// acertam sem marcador (IoU em 2º), os <paramref name="acertosCom"/> primeiros com marcador (3º); os
    /// demais erram (9º e 4º). Os <paramref name="comFalha"/> últimos falham nas duas condições. Hu sempre
    /// em 1º: se o relatório decidisse pelo melhor pontuador, aprovaria tudo.
    /// </summary>
    private static List<LinhaDeResultado> Bloco(
        Cenario cenario, TipoDePeca? tipo, int primeiro, int total, int acertosSem, int acertosCom,
        int comFalha = 0, string? falha = null)
    {
        var linhas = new List<LinhaDeResultado>();
        for (var i = 0; i < total; i++)
        {
            var id = Id(primeiro + i);
            var falhou = i >= total - comFalha;
            foreach (var pontuador in new[] { NomeDoPontuador.Hu, NomeDoPontuador.IoU })
                foreach (var condicao in new[] { Condicao.SemMarcador, Condicao.ComMarcador })
                {
                    if (falhou)
                    {
                        linhas.Add(new LinhaDeResultado(id, cenario, tipo, condicao, pontuador, null, falha,
                            null, null, falha == NaoSeparada ? 0 : 1, 40, 0));
                        continue;
                    }
                    var sem = condicao == Condicao.SemMarcador;
                    var acerto = i < (sem ? acertosSem : acertosCom);
                    var posicao = pontuador == NomeDoPontuador.Hu ? 1 : acerto ? (sem ? 2 : 3) : (sem ? 9 : 4);
                    linhas.Add(new LinhaDeResultado(id, cenario, tipo, condicao, pontuador, posicao, null,
                        0.5, 0.4, 1, 40, 10));
                }
        }
        return linhas;
    }

    private static List<ClassificacaoDoRecorte> Classificacao(int total, Func<int, Condicao, bool?> ok) =>
        Enumerable.Range(0, total)
            .SelectMany(i => new[] { Condicao.SemMarcador, Condicao.ComMarcador }
                .Select(c => new ClassificacaoDoRecorte(Id(i), c, ok(i, c))))
            .ToList();

    private static List<LinhaDePontuacao> Pontuacoes(int alvos, int grupos, int posicaoDaCerta)
    {
        var linhas = new List<LinhaDePontuacao>();
        for (var a = 0; a < alvos; a++)
            foreach (var condicao in new[] { Condicao.SemMarcador, Condicao.ComMarcador })
                for (var g = 0; g < grupos; g++)
                {
                    // O grupo certo fica em G0001 e tem a pontuação da posição pedida; os outros, uma escada.
                    var certo = g == 0;
                    var pontuacao = certo ? 1.0 - posicaoDaCerta * 0.01 : (g < posicaoDaCerta ? 1.0 - g * 0.01 : 0.5 - g * 0.001);
                    linhas.Add(new LinhaDePontuacao(Id(a), condicao, NomeDoPontuador.IoU, $"G{g + 1:D4}", pontuacao, certo));
                }
        return linhas;
    }

    private static List<LinhaDeResultado> Corte(int total, int acertosSem, int acertosCom, int comFalha = 0, string? falha = null) =>
        Bloco(Cenario.Corte, TipoDePeca.Chapa, 0, total, acertosSem, acertosCom, comFalha, falha);

    private static string Gerar(Cenario cenario, List<LinhaDeResultado> linhas, MetodoDeRecorte metodo = MetodoDeRecorte.Otsu,
        List<ClassificacaoDoRecorte>? classificacao = null, IReadOnlyList<LinhaDePontuacao>? pontuacoes = null) =>
        GeradorDeRelatorio.Gerar(cenario, linhas, pontuacoes ?? [],
            classificacao ?? Classificacao(linhas.Select(l => l.Id).Distinct().Count(), (_, _) => true), metodo);

    [Fact]
    public void Decide_pela_melhor_condicao_so_com_IoU_e_com_todos_os_tipos()
    {
        // 10 dobradas, 6 usinadas e 4 soldadas; com marcador acertam 8 + 5 + 3 = 16, sem marcador 4 + 3 + 2 = 9.
        // Só as dobradas dariam 8 de 10.
        var linhas = Bloco(Cenario.FormaFinal, TipoDePeca.Dobrada, 0, 10, 4, 8)
            .Concat(Bloco(Cenario.FormaFinal, TipoDePeca.Usinada, 10, 6, 3, 5))
            .Concat(Bloco(Cenario.FormaFinal, TipoDePeca.Soldada, 16, 4, 2, 3))
            .ToList();

        var md = Gerar(Cenario.FormaFinal, linhas);

        Assert.Contains("Sem marcador: **9 de 20**", md);
        Assert.Contains("Com marcador: **16 de 20**", md);
        Assert.Contains("Melhor condição: **ComMarcador**", md);
        Assert.Contains("**Veredito: Aprova.**", md);
    }

    [Fact]
    public void Hu_perfeito_nao_aprova_se_o_IoU_reprova()
    {
        var md = Gerar(Cenario.Corte, Corte(20, 5, 8));

        Assert.Contains("**Veredito: Reprova.**", md);
    }

    [Fact]
    public void Empate_entre_condicoes_fica_com_sem_marcador()
    {
        var md = Gerar(Cenario.Corte, Corte(20, 14, 14));

        Assert.Contains("Melhor condição: **SemMarcador**", md);
        Assert.Contains("**Veredito: ZonaCinza.**", md);
    }

    [Fact]
    public void Referencia_nao_separada_sai_do_denominador_e_e_listada()
    {
        // 20 alvos, os dois últimos sem referência; 15 acertos entre os 18 que restam.
        var md = Gerar(Cenario.Corte, Corte(20, 0, 15, comFalha: 2, falha: NaoSeparada));

        Assert.Contains("Com marcador: **15 de 18**", md);
        Assert.Contains("Limites para 18: aprova com ≥ 15", md);
        Assert.Contains("**Veredito: Aprova.**", md);
        var lista = md[md.IndexOf("## Fora do denominador", StringComparison.Ordinal)..];
        Assert.Contains("- A19", lista);
        Assert.Contains("- A20", lista);
    }

    [Fact]
    public void Outra_falha_conta_como_erro()
    {
        var md = Gerar(Cenario.Corte, Corte(20, 0, 15, comFalha: 4, falha: "recorte vazio"));

        Assert.Contains("Com marcador: **15 de 20**", md);
        Assert.Contains("**Veredito: ZonaCinza.**", md);
        Assert.DoesNotContain("Fora do denominador", md);
    }

    [Fact]
    public void Intermediarias_nao_tem_veredito()
    {
        var linhas = Bloco(Cenario.Intermediarias, TipoDePeca.Dobrada, 0, 5, 3, 3);

        var md = Gerar(Cenario.Intermediarias, linhas);

        Assert.DoesNotContain("Veredito", md);
        Assert.Contains("Só informativo (spec §3): 5 fotos não medem taxa de acerto.", md);
    }

    [Fact]
    public void Mostra_a_pontuacao_da_certa_e_da_melhor_errada()
    {
        var linhas = new List<LinhaDeResultado>
        {
            new("A01", Cenario.Corte, TipoDePeca.Chapa, Condicao.SemMarcador, NomeDoPontuador.IoU, 1, null, 0.812, 0.790, 1, 40, 5),
        };

        var md = Gerar(Cenario.Corte, linhas);

        Assert.Contains("0,812", md);
        Assert.Contains("0,790", md);
    }

    [Fact]
    public void Lista_o_grupo_com_mais_de_uma_referencia()
    {
        var linhas = new List<LinhaDeResultado>
        {
            new("A01", Cenario.Corte, TipoDePeca.Chapa, Condicao.SemMarcador, NomeDoPontuador.IoU, 1, null, 0.9, 0.5, 4, 40, 5),
            new("A02", Cenario.Corte, TipoDePeca.Chapa, Condicao.SemMarcador, NomeDoPontuador.IoU, 1, null, 0.9, 0.5, 1, 40, 5),
        };

        var md = Gerar(Cenario.Corte, linhas);

        Assert.Contains("- A01: 4 referências", md);
        Assert.DoesNotContain("- A02", md);
    }

    [Fact]
    public void Pilhas_menores_sao_so_informativas_e_nao_mudam_o_veredito()
    {
        // 12 de 20 no conjunto inteiro (zona cinza). Nas pontuações a certa é a 4ª entre 40 grupos: numa pilha
        // de 10 quase sempre cabe no top-3, e uma pilha que decidisse aprovaria.
        var linhas = Corte(20, 12, 12);
        var pontuacoes = Pontuacoes(alvos: 20, grupos: 40, posicaoDaCerta: 4);

        var sem = Gerar(Cenario.Corte, linhas);
        var com = Gerar(Cenario.Corte, linhas, pontuacoes: pontuacoes);

        Assert.Contains("**Veredito: ZonaCinza.**", sem);
        Assert.Contains("**Veredito: ZonaCinza.**", com);
        Assert.Contains("Sem marcador: **12 de 20**", com);
        Assert.DoesNotContain("Pilhas menores", sem);
        Assert.Contains("Pilhas menores (informativo: não entra na decisão)", com);
    }

    [Fact]
    public void Pilha_simulada_e_deterministica_e_inclui_a_certa()
    {
        var pontuacoes = Pontuacoes(alvos: 5, grupos: 40, posicaoDaCerta: 4);

        var a = PilhasMenores.Simular(pontuacoes, Condicao.ComMarcador, PilhasMenores.Tamanhos, PilhasMenores.Sorteios, PilhasMenores.Semente);
        var b = PilhasMenores.Simular(pontuacoes, Condicao.ComMarcador, PilhasMenores.Tamanhos, PilhasMenores.Sorteios, PilhasMenores.Semente);
        Assert.Equal(a, b);

        // Com a certa sempre em 1º, ela está no top-3 de qualquer pilha que a inclua.
        var primeira = PilhasMenores.Simular(Pontuacoes(alvos: 5, grupos: 40, posicaoDaCerta: 0), Condicao.ComMarcador,
            PilhasMenores.Tamanhos, PilhasMenores.Sorteios, PilhasMenores.Semente);
        Assert.Equal(PilhasMenores.Tamanhos.Count, primeira.Count);
        Assert.All(primeira, r =>
        {
            Assert.Equal(5, r.Alvos);
            Assert.Equal(r.Alvos, r.AcertosMedios);
        });
    }

    [Fact]
    public void Classificacao_incompleta_nao_avalia_as_regras_de_recorte()
    {
        var md = Gerar(Cenario.Corte, Corte(20, 12, 13),
            classificacao: Classificacao(20, (i, c) => i == 0 && c == Condicao.SemMarcador ? null : true));

        Assert.Contains("Classificação incompleta: 1 consulta(s)", md);
        Assert.DoesNotContain("**Gatilho do plano B", md);
        Assert.DoesNotContain("Plano B **não** se aplica", md);
        Assert.DoesNotContain("Regra do recorte", md);
    }

    [Fact]
    public void Mais_de_um_terco_de_recorte_falho_numa_execucao_Otsu_manda_reexecutar_com_GrabCut()
    {
        // 14 de 40 falhos = 35% > 1/3.
        var md = Gerar(Cenario.Corte, Corte(20, 16, 16),
            classificacao: Classificacao(20, (i, c) => !(i < 14 && c == Condicao.SemMarcador)));

        Assert.Contains("reexecute com `--recorte grabcut`", md);
    }

    [Fact]
    public void Ate_um_terco_falho_a_execucao_Otsu_decide()
    {
        // 13 de 40 = 32,5%.
        var md = Gerar(Cenario.Corte, Corte(20, 16, 16),
            classificacao: Classificacao(20, (i, c) => !(i < 13 && c == Condicao.SemMarcador)));

        Assert.Contains("esta execução Otsu é a que decide", md);
    }

    [Fact]
    public void Plano_B_so_na_zona_cinza_e_so_com_a_maioria_de_erros_de_recorte()
    {
        // Melhor condição: sem marcador, 12 acertos (zona cinza), 8 erros (A13..A20).
        var linhas = Corte(20, acertosSem: 12, acertosCom: 0);

        var cinco = Gerar(Cenario.Corte, linhas, MetodoDeRecorte.GrabCut,
            Classificacao(20, (i, c) => !(c == Condicao.SemMarcador && i >= 15)));
        var quatro = Gerar(Cenario.Corte, linhas, MetodoDeRecorte.GrabCut,
            Classificacao(20, (i, c) => !(c == Condicao.SemMarcador && i >= 16)));

        Assert.Contains("**Veredito: ZonaCinza.**", cinco);
        Assert.Contains("**Gatilho do plano B", cinco);
        Assert.Contains("Plano B **não** se aplica", quatro);
    }

    [Fact]
    public void Reprova_nao_fala_de_plano_B()
    {
        // 8 acertos (reprova) e todos os 12 erros de recorte: nem o gatilho nem a negativa aparecem.
        var md = Gerar(Cenario.Corte, Corte(20, acertosSem: 8, acertosCom: 0),
            MetodoDeRecorte.GrabCut, Classificacao(20, (i, c) => !(c == Condicao.SemMarcador && i >= 8)));

        Assert.Contains("**Veredito: Reprova.**", md);
        Assert.DoesNotContain("Gatilho do plano B", md);
        Assert.DoesNotContain("Plano B **não** se aplica", md);
    }
}
