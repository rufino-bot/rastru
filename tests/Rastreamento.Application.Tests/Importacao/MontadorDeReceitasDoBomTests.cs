using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Importacao;
using Xunit;

namespace Rastreamento.Application.Tests.Importacao;

public class MontadorDeReceitasDoBomTests
{
  private static LinhaCruaDoBom L(int linha, string nivel, string? codigo, string descricao, string quantidade) =>
      new(linha, nivel, codigo, descricao, quantidade);

  private static BomMontado Montar(params LinhaCruaDoBom[] linhas) =>
      MontadorDeReceitasDoBom.Montar("montagem.csv", linhas);

  private static int Chave(BomMontado bom, string codigo) =>
      bom.Componentes.Single(c => string.Equals(c.Codigo, codigo, StringComparison.OrdinalIgnoreCase)).Chave;

  private static string[] Mensagens(BomMontado bom) => bom.Erros.Select(e => e.ToString()).ToArray();

  [Fact]
  public void Bom_de_dois_niveis_vira_raiz_com_receitas_por_codigo()
  {
    var bom = Montar(
        L(2, "1", "A", "Peca A", "2"),
        L(3, "1.1", "B", "Peca B", "3"),
        L(4, "2", "C", "Peca C", "1"));

    Assert.Empty(bom.Erros);
    var raiz = bom.Componentes.Single(c => c.Chave == 0);
    Assert.Null(raiz.Codigo);
    Assert.Equal("montagem", raiz.Descricao);
    Assert.Equal(4, bom.Componentes.Count);

    var a = Chave(bom, "A");
    var b = Chave(bom, "B");
    var c = Chave(bom, "C");
    Assert.Equal(
        new[] { new FilhoDoBom(0, a, 1, 2m), new FilhoDoBom(0, c, 2, 1m), new FilhoDoBom(a, b, 1, 3m) },
        bom.Filhos.OrderBy(f => f.PaiChave).ThenBy(f => f.Ordem).ToArray());
  }

  [Fact]
  public void A_descricao_da_raiz_e_o_nome_do_arquivo_sem_extensao()
  {
    var bom = MontadorDeReceitasDoBom.Montar("pasta/Eixo principal.v2.xlsx", [L(2, "1", "A", "A", "1")]);

    Assert.Equal("Eixo principal.v2", bom.Componentes.Single(c => c.Chave == 0).Descricao);
  }

  [Fact]
  public void Codigo_repetido_em_dois_pais_vira_um_componente_so()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.1", "B", "B", "2"),
        L(4, "1.1.1", "D", "D", "4"),
        L(5, "2", "C", "C", "1"),
        L(6, "2.1", "B", "B", "5"),
        L(7, "2.1.1", "D", "D", "4"));

    Assert.Empty(bom.Erros);
    Assert.Single(bom.Componentes, c => c.Codigo == "B");
    var b = Chave(bom, "B");
    // A quantidade de B na receita de cada pai e a da linha daquele pai; a receita DE B e uma so.
    Assert.Equal(2m, bom.Filhos.Single(f => f.PaiChave == Chave(bom, "A") && f.FilhoChave == b).Quantidade);
    Assert.Equal(5m, bom.Filhos.Single(f => f.PaiChave == Chave(bom, "C") && f.FilhoChave == b).Quantidade);
    Assert.Single(bom.Filhos, f => f.PaiChave == b);
  }

  [Fact]
  public void Codigo_com_caixa_ou_espaco_diferente_e_o_mesmo_componente()
  {
    var bom = Montar(
        L(2, "1", " ab-1 ", "primeira", "1"),
        L(3, "2", "AB-1", "segunda", "1"));

    Assert.Equal(2, bom.Componentes.Count);
    Assert.Equal("ab-1", bom.Componentes.Single(c => c.Chave != 0).Codigo);
    Assert.Equal(2m, Assert.Single(bom.Filhos).Quantidade);
  }

  [Fact]
  public void Mesmo_filho_repetido_sob_o_mesmo_pai_soma()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "2"),
        L(3, "2", "A", "A", "3"));

    Assert.Empty(bom.Erros);
    var filho = Assert.Single(bom.Filhos);
    Assert.Equal(new FilhoDoBom(0, Chave(bom, "A"), 1, 5m), filho);
  }

  [Fact]
  public void Mesmo_filho_repetido_sob_a_mesma_ocorrencia_de_um_pai_soma()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.1", "B", "B", "2"),
        L(4, "1.2", "B", "B", "3"));

    Assert.Empty(bom.Erros);
    Assert.Equal(5m, bom.Filhos.Single(f => f.PaiChave == Chave(bom, "A")).Quantidade);
  }

  [Fact]
  public void A_ordem_e_a_da_primeira_aparicao_sob_o_pai()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "2", "B", "B", "1"),
        L(4, "3", "A", "A", "1"),
        L(5, "4", "C", "C", "1"));

    var ordens = bom.Filhos.Where(f => f.PaiChave == 0).OrderBy(f => f.Ordem)
        .Select(f => (f.FilhoChave, f.Ordem)).ToArray();
    Assert.Equal(
        new[] { (Chave(bom, "A"), 1), (Chave(bom, "B"), 2), (Chave(bom, "C"), 3) }, ordens);
  }

  [Fact]
  public void Mesmo_codigo_com_filhos_diferentes_e_erro_do_arquivo()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.1", "B", "B", "1"),
        L(4, "1.1.1", "X", "X", "1"),
        L(5, "2", "C", "C", "1"),
        L(6, "2.1", "B", "B", "1"),
        L(7, "2.1.1", "Y", "Y", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(6, erro.Linha);
    Assert.Contains("'B'", erro.Mensagem);
    Assert.Contains("3", erro.Mensagem);
    Assert.Contains("6", erro.Mensagem);
    Assert.Empty(bom.Componentes);
    Assert.Empty(bom.Filhos);
  }

  [Fact]
  public void Mesmo_codigo_com_a_mesma_receita_em_quantidade_diferente_e_erro()
  {
    var bom = Montar(
        L(2, "1", "B", "B", "1"),
        L(3, "1.1", "X", "X", "2"),
        L(4, "2", "B", "B", "1"),
        L(5, "2.1", "X", "X", "3"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(4, erro.Linha);
    Assert.Contains("'B'", erro.Mensagem);
  }

  [Fact]
  public void Ciclo_entre_codigos_e_erro_do_arquivo()
  {
    // A contem B e B contem A. Um arquivo finito so chega aqui com a ocorrencia mais funda sem filhos,
    // o que tambem e um conflito de receita; o ciclo precisa ser dito mesmo assim.
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.1", "B", "B", "1"),
        L(4, "1.1.1", "A", "A", "1"));

    var ciclo = Assert.Single(bom.Erros, e => e.Mensagem.Contains("ciclo"));
    Assert.Contains("A -> B -> A", ciclo.Mensagem);
    Assert.Equal(2, ciclo.Linha);
  }

  [Fact]
  public void Codigo_que_contem_a_si_mesmo_e_ciclo()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.1", "A", "A", "1"));

    Assert.Contains(bom.Erros, e => e.Mensagem.Contains("ciclo") && e.Mensagem.Contains("A -> A"));
  }

  [Fact]
  public void Nivel_que_pula_degrau_e_erro()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "1.2.3", "B", "B", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(3, erro.Linha);
    Assert.Contains("nivel", erro.Mensagem);
    Assert.Equal("Linha 3: " + erro.Mensagem, erro.ToString());
  }

  [Fact]
  public void Nivel_de_filho_sem_o_pai_antes_e_erro()
  {
    var bom = Montar(L(2, "1.1", "A", "A", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(2, erro.Linha);
    Assert.Contains("nivel", erro.Mensagem);
  }

  [Theory]
  [InlineData("")]
  [InlineData("a")]
  [InlineData("1.")]
  [InlineData("1..2")]
  [InlineData("0")]
  [InlineData("1.-2")]
  public void Nivel_que_nao_e_inteiro_positivo_por_segmento_e_erro(string nivel)
  {
    var bom = Montar(L(2, nivel, "A", "A", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(2, erro.Linha);
    Assert.Contains("nivel", erro.Mensagem);
  }

  [Fact]
  public void O_pai_e_a_ultima_linha_com_o_nivel_do_prefixo()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "1"),
        L(3, "2", "B", "B", "1"),
        L(4, "2.1", "X", "X", "7"),
        L(5, "1.1", "Y", "Y", "1"));

    // 1.1 aparece depois de 2.1, mas seu pai e a linha "1" (A), nao a ultima linha lida.
    Assert.Empty(bom.Erros);
    Assert.Equal(1m, bom.Filhos.Single(f => f.PaiChave == Chave(bom, "A")).Quantidade);
    Assert.Equal(7m, bom.Filhos.Single(f => f.PaiChave == Chave(bom, "B")).Quantidade);
  }

  [Theory]
  [InlineData("1,5")]
  [InlineData("1.5")]
  [InlineData(" 1,5 ")]
  [InlineData("1.50000")]
  public void Quantidade_com_virgula_e_com_ponto_le_igual(string texto)
  {
    var bom = Montar(L(2, "1", "A", "A", texto));

    Assert.Empty(bom.Erros);
    Assert.Equal(1.5m, Assert.Single(bom.Filhos).Quantidade);
  }

  [Theory]
  [InlineData("")]
  [InlineData("abc")]
  [InlineData("0")]
  [InlineData("0,00001")]
  [InlineData("-1")]
  [InlineData("1,23456")]
  [InlineData("1.234,5")]
  [InlineData("1,2,3")]
  [InlineData("1e3")]
  [InlineData("99999999999999,99999")]
  [InlineData("100000000000000")]
  [InlineData("999999999999999999999999999999999")]
  public void Quantidade_invalida_zero_negativa_ou_com_mais_de_4_casas_e_erro(string texto)
  {
    var bom = Montar(L(7, "1", "A", "A", texto));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(7, erro.Linha);
    Assert.Contains("quantidade invalida", erro.Mensagem);
  }

  [Fact]
  public void Os_limites_da_faixa_da_coluna_sao_aceitos()
  {
    var bom = Montar(
        L(2, "1", "A", "A", PlanejadorDeCopia.QuantidadeMinimaDaColuna.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        L(3, "2", "B", "B", PlanejadorDeCopia.QuantidadeMaximaDaColuna.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    Assert.Empty(bom.Erros);
  }

  [Fact]
  public void Soma_acima_da_faixa_da_coluna_e_erro()
  {
    var maximo = PlanejadorDeCopia.QuantidadeMaximaDaColuna.ToString(System.Globalization.CultureInfo.InvariantCulture);
    var bom = Montar(
        L(2, "1", "A", "A", maximo),
        L(3, "2", "A", "A", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(3, erro.Linha);
    Assert.Contains("quantidade", erro.Mensagem);
  }

  [Fact]
  public void Linha_sem_codigo_vira_componente_proprio_com_codigo_nulo()
  {
    var bom = Montar(
        L(2, "1", null, "Chapa", "1"),
        L(3, "2", "  ", "Chapa", "1"));

    Assert.Empty(bom.Erros);
    var semCodigo = bom.Componentes.Where(c => c.Chave != 0).ToArray();
    Assert.Equal(2, semCodigo.Length);
    Assert.All(semCodigo, c => Assert.Null(c.Codigo));
    Assert.NotEqual(semCodigo[0].Chave, semCodigo[1].Chave);
    Assert.Equal(2, bom.Filhos.Count);
  }

  [Fact]
  public void Arvore_acima_de_NosMaximos_e_erro()
  {
    // A raiz conta como no, como no PlanejadorDeCopia: raiz + NosMaximos filhos = NosMaximos + 1.
    var linhas = Enumerable.Range(1, PlanejadorDeCopia.NosMaximos)
        .Select(i => L(i + 1, i.ToString(), $"C{i}", $"C{i}", "1")).ToArray();

    var bom = MontadorDeReceitasDoBom.Montar("grande.csv", linhas);

    var erro = Assert.Single(bom.Erros);
    Assert.Null(erro.Linha);
    Assert.Contains(PlanejadorDeCopia.NosMaximos.ToString(), erro.Mensagem);
  }

  [Fact]
  public void Arvore_com_exatamente_NosMaximos_nos_e_aceita()
  {
    var linhas = Enumerable.Range(1, PlanejadorDeCopia.NosMaximos - 1)
        .Select(i => L(i + 1, i.ToString(), $"C{i}", $"C{i}", "1")).ToArray();

    Assert.Empty(MontadorDeReceitasDoBom.Montar("limite.csv", linhas).Erros);
  }

  [Fact]
  public void Nos_repetidos_por_pais_diferentes_contam_cada_ocorrencia_expandida()
  {
    // Um subconjunto S de 30 filhos pendurado em 17 pais: a arvore expandida passa de 500 nos,
    // embora o arquivo tenha ~50 codigos. O arquivo repete S inteiro sob cada pai, como o CAD faz.
    var linhas = new List<LinhaCruaDoBom>();
    var n = 2;
    for (var p = 1; p <= 17; p++)
    {
      linhas.Add(L(n++, p.ToString(), $"P{p}", $"P{p}", "1"));
      linhas.Add(L(n++, $"{p}.1", "S", "S", "1"));
      for (var f = 1; f <= 29; f++)
        linhas.Add(L(n++, $"{p}.1.{f}", $"F{f}", $"F{f}", "1"));
    }

    var bom = MontadorDeReceitasDoBom.Montar("repetido.csv", linhas);

    // 1 raiz + 17 x (1 pai + 1 S + 29 filhos) = 528 nos expandidos.
    var erro = Assert.Single(bom.Erros);
    Assert.Contains(PlanejadorDeCopia.NosMaximos.ToString(), erro.Mensagem);
  }

  [Fact]
  public void Profundidade_acima_de_ProfundidadeMaxima_e_erro()
  {
    // A raiz e o nivel 1: uma corrente de ProfundidadeMaxima linhas tem ProfundidadeMaxima + 1 niveis.
    var linhas = Corrente(PlanejadorDeCopia.ProfundidadeMaxima);

    var bom = MontadorDeReceitasDoBom.Montar("funda.csv", linhas);

    var erro = Assert.Single(bom.Erros);
    Assert.Null(erro.Linha);
    Assert.Contains(PlanejadorDeCopia.ProfundidadeMaxima.ToString(), erro.Mensagem);
  }

  [Fact]
  public void Profundidade_igual_a_ProfundidadeMaxima_e_aceita()
  {
    var linhas = Corrente(PlanejadorDeCopia.ProfundidadeMaxima - 1);

    Assert.Empty(MontadorDeReceitasDoBom.Montar("limite.csv", linhas).Erros);
  }

  private static LinhaCruaDoBom[] Corrente(int niveis)
  {
    var nivel = "";
    var linhas = new List<LinhaCruaDoBom>();
    for (var i = 1; i <= niveis; i++)
    {
      nivel = i == 1 ? "1" : nivel + ".1";
      linhas.Add(L(i + 1, nivel, $"N{i}", $"N{i}", "1"));
    }
    return linhas.ToArray();
  }

  [Fact]
  public void Lista_vazia_e_erro_de_arquivo_vazio()
  {
    var bom = MontadorDeReceitasDoBom.Montar("vazio.csv", []);

    var erro = Assert.Single(bom.Erros);
    Assert.Null(erro.Linha);
    Assert.Contains("vazio", erro.Mensagem);
    Assert.Equal(erro.Mensagem, erro.ToString());
    Assert.Empty(bom.Componentes);
    Assert.Empty(bom.Filhos);
  }

  [Fact]
  public void Todos_os_erros_vem_juntos()
  {
    var bom = Montar(
        L(2, "1", "A", "A", "abc"),
        L(3, "1.1", "B", "B", "1"),
        L(4, "3.1", "C", "C", "1"),
        L(5, "2", "D", "D", "0"));

    Assert.Equal(
        new[] { 2, 4, 5 },
        bom.Erros.Select(e => e.Linha!.Value).OrderBy(l => l).ToArray());
    Assert.Equal(3, bom.Erros.Count);
  }

  [Fact]
  public void Linha_com_quantidade_invalida_ainda_e_pai_das_seguintes()
  {
    // A quantidade ruim da linha 2 nao pode virar um "nivel que pula degrau" na linha 3.
    var bom = Montar(
        L(2, "1", "A", "A", "x"),
        L(3, "1.1", "B", "B", "1"));

    var erro = Assert.Single(bom.Erros);
    Assert.Equal(2, erro.Linha);
  }

  [Fact]
  public void ToString_do_erro_com_linha_prefixa_o_numero()
  {
    Assert.Equal("Linha 7: quantidade invalida.", new ErroDoBom(7, "quantidade invalida.").ToString());
    Assert.Equal("arquivo vazio.", new ErroDoBom(null, "arquivo vazio.").ToString());
  }
}
