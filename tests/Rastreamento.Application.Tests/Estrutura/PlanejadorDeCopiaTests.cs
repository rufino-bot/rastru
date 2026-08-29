using Rastreamento.Application.Estrutura;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

public class PlanejadorDeCopiaTests
{
  private static ReceitaDoCatalogo Receita(
      (int Pai, int Filho, decimal Qtd)[] filhos,
      (int Comp, int Material, decimal Qtd)[]? materiais = null,
      (int Comp, int Setor, int Ordem)[]? roteiro = null) =>
      new(filhos.ToLookup(f => f.Pai, f => (f.Filho, f.Qtd)),
          (materiais ?? []).ToLookup(m => m.Comp, m => (m.Material, m.Qtd)),
          (roteiro ?? []).ToLookup(r => r.Comp, r => (r.Setor, r.Ordem)));

  // Gera uma receita em arvore de largura constante: raiz = 1, cada no de cada geracao ganha
  // `fanOut` filhos, por `niveis` (contando a raiz como nivel 1). Usada pelos testes do teto de NOS
  // — precisa ser larga e rasa, o oposto da corrente usada nos testes do teto de PROFUNDIDADE, para
  // separar os dois erros.
  private static (int Pai, int Filho, decimal Qtd)[] ArvoreLarga(int fanOut, int niveis)
  {
    var arestas = new List<(int, int, decimal)>();
    var proximoId = 2; // raiz e 1
    var nivelAtual = new List<int> { 1 };
    for (var nivel = 1; nivel < niveis; nivel++)
    {
      var proximoNivel = new List<int>();
      foreach (var pai in nivelAtual)
        for (var i = 0; i < fanOut; i++)
        {
          var filho = proximoId++;
          arestas.Add((pai, filho, 1m));
          proximoNivel.Add(filho);
        }
      nivelAtual = proximoNivel;
    }
    return arestas.ToArray();
  }

  [Fact]
  public void Raiz_sem_receita_gera_um_no_so()
  {
    var plano = PlanejadorDeCopia.Planejar(Receita([]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

    Assert.Null(plano.Erro);
    Assert.Equal(1, plano.Raiz!.ComponenteId);
    Assert.Equal(10m, plano.Raiz.Quantidade);
    Assert.Empty(plano.Raiz.Filhos);
  }

  [Fact]
  public void Quantidade_do_filho_e_multiplicada_pela_do_pai()
  {
    var plano = PlanejadorDeCopia.Planejar(
        Receita([(1, 2, 4m)]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

    Assert.Equal(40m, plano.Raiz!.Filhos.Single().Quantidade);
  }

  [Fact]
  public void A_multiplicacao_acumula_nivel_a_nivel()
  {
    var plano = PlanejadorDeCopia.Planejar(
        Receita([(1, 2, 4m), (2, 3, 3m)]), componenteRaizId: 1, quantidadeDaRaiz: 10m);

    Assert.Equal(120m, plano.Raiz!.Filhos.Single().Filhos.Single().Quantidade);
  }

  [Fact]
  public void Diamante_e_aceito_o_mesmo_componente_sob_dois_pais()
  {
    // 1 -> 2, 1 -> 3, e tanto 2 quanto 3 usam o componente 4.
    var plano = PlanejadorDeCopia.Planejar(
        Receita([(1, 2, 1m), (1, 3, 1m), (2, 4, 5m), (3, 4, 7m)]), 1, 1m);

    Assert.Null(plano.Erro);
    var ramos = plano.Raiz!.Filhos.OrderBy(f => f.ComponenteId).ToList();
    Assert.Equal(5m, ramos[0].Filhos.Single().Quantidade);
    Assert.Equal(7m, ramos[1].Filhos.Single().Quantidade);
  }

  [Fact]
  public void Ciclo_direto_e_recusado()
  {
    var plano = PlanejadorDeCopia.Planejar(Receita([(1, 2, 1m), (2, 1, 1m)]), 1, 1m);

    Assert.Null(plano.Raiz);
    Assert.Equal(PlanejadorDeCopia.CodigoDeCiclo, plano.CodigoDoErro);
  }

  [Fact]
  public void Ciclo_indireto_e_recusado_e_a_mensagem_nomeia_o_caminho()
  {
    var plano = PlanejadorDeCopia.Planejar(Receita([(1, 2, 1m), (2, 3, 1m), (3, 2, 1m)]), 1, 1m);

    Assert.Equal(PlanejadorDeCopia.CodigoDeCiclo, plano.CodigoDoErro);
    // Nao basta os digitos aparecerem soltos ("2" casa com o "2" de dentro de "12"): a mensagem tem
    // de nomear a SEQUENCIA contigua do trecho ciclico, fechado no no reencontrado — 2 -> 3 -> 2 -,
    // senao Skip(IndexOf + 1) (perde o fechamento) ou o caminho inteiro sem Skip nenhum (1 -> 2 -> 3
    // -> 2, ramo inocente incluso) tambem passariam.
    Assert.Contains("2 -> 3 -> 2", plano.Erro);
  }

  [Fact]
  public void Profundidade_acima_do_teto_e_recusada()
  {
    // Comprimento derivado da constante, nao de um literal: cadeia com PROFUNDIDADE MAXIMA + 1 nos
    // (Range(1, PM) da PM arestas, entao PM + 1 nos) — imediatamente ACIMA do teto. Junto com o
    // teste seguinte (imediatamente abaixo), o par prende o `>` de PlanejadorDeCopia.cs:83: trocar
    // por `>=` (ou deslocar a constante em 1, para qualquer lado) mata um dos dois.
    var corrente = Enumerable.Range(1, PlanejadorDeCopia.ProfundidadeMaxima)
        .Select(i => (i, i + 1, 1m)).ToArray();
    var plano = PlanejadorDeCopia.Planejar(Receita(corrente), 1, 1m);

    Assert.Equal(PlanejadorDeCopia.CodigoDeProfundidade, plano.CodigoDoErro);
  }

  [Fact]
  public void Corrente_dentro_do_teto_e_aceita()
  {
    // Par do teste acima: cadeia com exatamente PROFUNDIDADE MAXIMA nos (Range(1, PM - 1) da
    // PM - 1 arestas, entao PM nos) — imediatamente ABAIXO do limite que recusa.
    var corrente = Enumerable.Range(1, PlanejadorDeCopia.ProfundidadeMaxima - 1)
        .Select(i => (i, i + 1, 1m)).ToArray();
    var plano = PlanejadorDeCopia.Planejar(Receita(corrente), 1, 1m);

    Assert.Null(plano.Erro);
  }

  [Fact]
  public void Numero_de_nos_acima_do_teto_e_recusado()
  {
    // Larga e rasa de proposito: fan-out 8, 4 niveis (raiz + 3 geracoes) = 1 + 8 + 64 + 512 = 585
    // nos, profundidade so 4 — bem longe do teto de profundidade (20). So assim o teste prova que
    // quem recusa e o teto de NOS (CodigoDeTamanho), nao o de profundidade (CodigoDeProfundidade).
    var arvore = ArvoreLarga(fanOut: 8, niveis: 4);
    var plano = PlanejadorDeCopia.Planejar(Receita(arvore), 1, 1m);

    Assert.Equal(PlanejadorDeCopia.CodigoDeTamanho, plano.CodigoDoErro);
  }

  [Fact]
  public void Numero_de_nos_dentro_do_teto_e_aceito()
  {
    // Par do teste acima: sem ele, um NosMaximos de 1 passaria no teste anterior sem guarda
    // nenhuma. Fan-out 5, 3 niveis (raiz + 2 geracoes) = 1 + 5 + 25 = 31 nos, dentro do teto de 500.
    var arvore = ArvoreLarga(fanOut: 5, niveis: 3);
    var plano = PlanejadorDeCopia.Planejar(Receita(arvore), 1, 1m);

    Assert.Null(plano.Erro);
  }

  [Fact]
  public void Materiais_e_roteiro_sao_copiados_em_cada_no_com_a_ordem_preservada()
  {
    // Tres armadilhas de proposito: o material tambem multiplica (3 x 1,5 = 4,5, nao 1,5); o
    // roteiro entra fora de ordem, senao o OrderBy da implementacao nao e load-bearing; e a Ordem
    // NAO e uma sequencia contigua 1..N, senao uma implementacao que REINDEXA por posicao
    // (Select((r, i) => (r.SetorId, i + 1))) produziria por acaso o mesmo resultado que preservar a
    // Ordem gravada, e a mutacao que troca uma pela outra não mataria este teste.
    var plano = PlanejadorDeCopia.Planejar(
        Receita([(1, 2, 3m)],
                materiais: [(2, 90, 1.5m)],
                roteiro: [(2, 7, 30), (2, 8, 20), (2, 7, 10)]),   // FORA de ordem, de proposito
        1, 1m);

    var filho = plano.Raiz!.Filhos.Single();
    Assert.Equal(3m, filho.Quantidade);
    Assert.Null(filho.Descricao);   // regra 19: NULL herda do Componente — o planejador nao copia texto
    Assert.Equal((90, 4.5m), filho.Materiais.Single());          // 3 x 1,5 — o material multiplica
    Assert.Equal([(7, 10), (8, 20), (7, 30)], filho.Roteiro);    // reordenado pela Ordem gravada
  }
}
