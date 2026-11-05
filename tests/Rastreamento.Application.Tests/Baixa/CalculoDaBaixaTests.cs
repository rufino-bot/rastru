using Rastreamento.Application.Baixa;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Baixa;

/// <summary>
/// A conta pura da baixa (secoes 4.1 a 4.3 e 4.6 da spec da Fase 5B-1): o que sai do no, os filhos a iniciar que saem
/// junto (C6), o que vira sobra e quantas unidades da Peca do topo a perda de um Item trava (D13). Cada arvore e montada
/// a mao na calculadora, sem repositorio. Os saldos sao o liquido do livro: o "a iniciar" de um no e a quantidade dele
/// mais o liquido em `AIniciar` (negativo, o que ja saiu).
/// </summary>
public class CalculoDaBaixaTests
{
  private const int Corte = 1, Solda = 3, Pintura = 4;

  private static readonly Dictionary<int, string> NomesDosSetores = new()
  {
    [Corte] = "Corte", [Solda] = "Solda", [Pintura] = "Pintura",
  };

  private static string NomeDoSetor(int id) => NomesDosSetores[id];

  private static string NomePeloId(int id) => $"No {id}";

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList());

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos, IEnumerable<SaldoLiquido> saldos, IReadOnlyDictionary<int, decimal>? montados = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null) =>
      new(nos, saldos, montados ?? new Dictionary<int, decimal>(), Array.Empty<(int, int)>(),
          perdidosDeAIniciar: perdidosDeAIniciar);

  private static OrigemDaBaixa De(Local local, decimal quantidade) => new(local, quantidade);

  private static PedidoDeBaixa Parte(int noId, IReadOnlyList<int> cadeia, params OrigemDaBaixa[] origens) =>
      new(noId, MotivosDePerda.PerdaArmazem, false, origens, false, cadeia);

  private static PedidoDeBaixa ParteComTopo(int noId, IReadOnlyList<int> cadeia, params OrigemDaBaixa[] origens) =>
      new(noId, MotivosDePerda.PerdaArmazem, false, origens, true, cadeia);

  private static PedidoDeBaixa Tudo(int noId, IReadOnlyList<int>? cadeia = null) =>
      new(noId, MotivosDePerda.Descarte, true, Array.Empty<OrigemDaBaixa>(), false, cadeia ?? Array.Empty<int>());

  private static PlanoDaBaixa Planejar(CalculadoraDeExecucao calc, PedidoDeBaixa pedido)
  {
    var r = CalculoDaBaixa.Planejar(calc, pedido, NomePeloId, NomeDoSetor);
    Assert.True(r.Sucesso, $"{r.Erro}: {r.Detalhe}");
    return r.Valor!;
  }

  private static Result<PlanoDaBaixa> Recusar(CalculadoraDeExecucao calc, PedidoDeBaixa pedido, Func<int, string>? nome = null)
  {
    var r = CalculoDaBaixa.Planejar(calc, pedido, nome ?? NomePeloId, NomeDoSetor);
    Assert.False(r.Sucesso);
    return r;
  }

  // Regra 19: um no copiado do catalogo, sem descricao propria, se chama pelo Componente.
  private static readonly Componente Chapa = new()
  {
    Id = 70, Codigo = "CHP-220", Descricao = "Chapa dobrada 3 mm", Tipo = "Fabricado", Ativo = true,
  };

  private static EstruturaItem CopiadoDoCatalogo(int id, int? pai) => new()
  {
    Id = id, EstruturaPaiId = pai, ComponenteId = Chapa.Id, Descricao = null,
    NivelHierarquico = pai is null ? "Peca" : "Item",
  };

  private static Func<int, string> NomesPelaRegra19(params EstruturaItem[] nos) => id =>
  {
    var no = nos.Single(n => n.Id == id);
    return no.Descricao ?? (no.ComponenteId == Chapa.Id ? Chapa.Descricao : $"no {id}");
  };

  // A Peca 10 com 3 a iniciar, 4 no Setor e 3 aguardando coleta (7 montadas), e o filho 20, razao 2, com 14 montados,
  // 4 a iniciar e 2 no Setor.
  private static CalculadoraDeExecucao PecaComFilhoMontado() => Calcular(
      [No(10, null, 10m, null, Solda), No(20, 10, 20m, 2m, Corte)],
      [
        Em(10, Local.AIniciar, -7m), Em(10, Local.NoSetor(Solda, 1), 4m), Em(10, Local.AguardandoColeta(Solda, 1), 3m),
        Em(20, Local.AIniciar, -16m), Em(20, Local.Montado, 14m), Em(20, Local.NoSetor(Corte, 1), 2m),
      ],
      new Dictionary<int, decimal> { [10] = 7m });

  [Fact]
  public void Parte_de_uma_folha_gera_so_as_linhas_pedidas()
  {
    var calc = Calcular(
        [No(1, null, 6m, null, Corte)],
        [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Corte, 1), 1m), Em(1, Local.AguardandoColeta(Corte, 1), 3m)]);

    var plano = Planejar(calc, Parte(1, [], De(Local.AIniciar, 1m), De(Local.AguardandoColeta(Corte, 1), 2m)));

    Assert.Equal(
        [new LinhaPlanejada(1, Local.AIniciar, 1m), new LinhaPlanejada(1, Local.AguardandoColeta(Corte, 1), 2m)],
        plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Null(plano.Trava);
  }

  [Fact]
  public void Tudo_leva_o_no_e_os_descendentes_em_todas_as_posicoes()
  {
    var calc = Calcular(
        [No(10, null, 10m, null, Solda), No(20, 10, 20m, 2m, Corte)],
        [
          Em(10, Local.AIniciar, -7m), Em(10, Local.NoSetor(Solda, 1), 4m), Em(10, Local.AguardandoColeta(Solda, 1), 3m),
          Em(20, Local.AIniciar, -8m), Em(20, Local.NoSetor(Corte, 1), 8m),
        ],
        new Dictionary<int, decimal> { [10] = 7m });

    var plano = Planejar(calc, Tudo(10));

    Assert.Equal(
        [
          new LinhaPlanejada(10, Local.AIniciar, 3m),
          new LinhaPlanejada(10, Local.NoSetor(Solda, 1), 4m),
          new LinhaPlanejada(10, Local.AguardandoColeta(Solda, 1), 3m),
          new LinhaPlanejada(20, Local.AIniciar, 12m),
          new LinhaPlanejada(20, Local.NoSetor(Corte, 1), 8m),
        ],
        plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Null(plano.Trava);
  }

  [Fact]
  public void Tudo_nao_leva_o_montado_nem_o_expedido_nem_o_ja_perdido()
  {
    var calc = Calcular(
        [No(10, null, 6m, null, Solda), No(20, 10, 6m, 1m, Corte)],
        [
          Em(10, Local.AIniciar, -6m), Em(10, Local.Expedido, 2m), Em(10, Local.Perdido, 1m), Em(10, Local.Conforme, 3m),
          Em(20, Local.AIniciar, -6m), Em(20, Local.Montado, 6m),
        ],
        new Dictionary<int, decimal> { [10] = 6m });

    var plano = Planejar(calc, Tudo(10));

    Assert.Equal([new LinhaPlanejada(10, Local.Conforme, 3m)], plano.Linhas);
  }

  [Fact]
  public void Parte_do_pai_tira_os_filhos_a_iniciar_alem_do_que_ele_deve()
  {
    var plano = Planejar(PecaComFilhoMontado(),
        Parte(10, [], De(Local.AIniciar, 3m), De(Local.NoSetor(Solda, 1), 1m)));

    Assert.Equal(
        [
          new LinhaPlanejada(10, Local.AIniciar, 3m),
          new LinhaPlanejada(10, Local.NoSetor(Solda, 1), 1m),
          new LinhaPlanejada(20, Local.AIniciar, 4m),
        ],
        plano.Linhas);
    Assert.Equal([(20, 2m)], plano.ViramSobra);
    Assert.Null(plano.Trava);
  }

  [Fact]
  public void Parte_do_pai_so_de_unidades_iniciadas_nao_mexe_nos_filhos()
  {
    var plano = Planejar(PecaComFilhoMontado(), Parte(10, [], De(Local.NoSetor(Solda, 1), 1m)));

    Assert.Equal([new LinhaPlanejada(10, Local.NoSetor(Solda, 1), 1m)], plano.Linhas);
    Assert.Empty(plano.ViramSobra);
  }

  [Fact]
  public void Cascata_desce_tres_niveis()
  {
    // Lote dividido nos tres niveis. A Peca 1 (4) iniciou 1 (consumindo 2 do Item 2); o Item 2 (8, razao 2) iniciou 4
    // (consumindo 12 do Item 3), 2 deles ja montados na Peca e 2 no Setor; o Item 3 (24, razao 3) tem 8 a iniciar e 4
    // no Setor. Perder 2 a iniciar da Peca deixa 1 unidade por fazer, que precisa de 2 do Item 2 — os 2 que ja estao no
    // Setor. Os 4 a iniciar do Item 2 saem, e com eles o Item 3 deixa de ser preciso: os 8 a iniciar saem e os 4 no
    // Setor viram sobra.
    var calc = Calcular(
        [No(1, null, 4m, null, Solda), No(2, 1, 8m, 2m, Corte), No(3, 2, 24m, 3m, Corte)],
        [
          Em(1, Local.AIniciar, -1m), Em(1, Local.NoSetor(Solda, 1), 1m),
          Em(2, Local.AIniciar, -4m), Em(2, Local.Montado, 2m), Em(2, Local.NoSetor(Corte, 1), 2m),
          Em(3, Local.AIniciar, -16m), Em(3, Local.Montado, 12m), Em(3, Local.NoSetor(Corte, 1), 4m),
        ],
        new Dictionary<int, decimal> { [1] = 1m, [2] = 4m });

    var plano = Planejar(calc, Parte(1, [], De(Local.AIniciar, 2m)));

    Assert.Equal(
        [
          new LinhaPlanejada(1, Local.AIniciar, 2m),
          new LinhaPlanejada(2, Local.AIniciar, 4m),
          new LinhaPlanejada(3, Local.AIniciar, 8m),
        ],
        plano.Linhas);
    Assert.Equal([(3, 4m)], plano.ViramSobra);
  }

  [Fact]
  public void Sobra_que_ja_existia_nao_entra_em_ViramSobra()
  {
    // O filho ja tinha 2 fabricados alem do que o pai precisava (8 no Setor para 3 x 2 = 6). A baixa de 1 a iniciar do
    // pai faz o filho dever 4: a sobra passa a 4, e o que a baixa faz virar sobra sao os 2 a mais, nao os 4.
    var calc = Calcular(
        [No(10, null, 3m, null, Solda), No(20, 10, 8m, 2m, Corte)],
        [Em(20, Local.AIniciar, -8m), Em(20, Local.NoSetor(Corte, 1), 8m)]);

    var plano = Planejar(calc, Parte(10, [], De(Local.AIniciar, 1m)));

    Assert.Equal([new LinhaPlanejada(10, Local.AIniciar, 1m)], plano.Linhas);
    Assert.Equal([(20, 2m)], plano.ViramSobra);
  }

  [Fact]
  public void A_iniciar_alem_do_que_o_pai_precisava_sai_e_o_fabricado_que_passa_a_sobrar_vira_sobra()
  {
    // O filho tinha 2 a iniciar alem do que o pai precisava (2 a iniciar e 6 no Setor para 3 x 2 = 6). A baixa de 1 a
    // iniciar do pai tira os 2 a iniciar, e 2 dos 6 fabricados, que eram precisos, viram sobra.
    var calc = Calcular(
        [No(10, null, 3m, null, Solda), No(20, 10, 8m, 2m, Corte)],
        [Em(20, Local.AIniciar, -6m), Em(20, Local.NoSetor(Corte, 1), 6m)]);

    var plano = Planejar(calc, Parte(10, [], De(Local.AIniciar, 1m)));

    Assert.Equal(
        [new LinhaPlanejada(10, Local.AIniciar, 1m), new LinhaPlanejada(20, Local.AIniciar, 2m)],
        plano.Linhas);
    Assert.Equal([(20, 2m)], plano.ViramSobra);
  }

  [Fact]
  public void Origem_AguardandoLiberacao_e_OrigemInvalida()
  {
    var calc = Calcular([No(1, null, 4m, null, Corte)], [Em(1, Local.AIniciar, -4m), Em(1, Local.AguardandoLiberacao, 4m)]);

    var r = Recusar(calc, Parte(1, [], De(Local.AguardandoLiberacao, 1m)));

    Assert.Equal(CodigosDaBaixa.OrigemInvalida, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
  }

  [Fact]
  public void Tudo_com_AguardandoLiberacao_e_LiberacaoEmAberto()
  {
    var peca = CopiadoDoCatalogo(1, null);
    var calc = Calcular(
        [No(1, null, 4m, null, Corte)],
        [Em(1, Local.AIniciar, -4m), Em(1, Local.NaoConforme, 3m), Em(1, Local.AguardandoLiberacao, 1m)]);

    var r = Recusar(calc, Tudo(1), NomesPelaRegra19(peca));

    Assert.Equal(CodigosDaExpedicao.LiberacaoEmAberto, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(
        "Chapa dobrada 3 mm tem 1 aguardando liberação: estorne o pedido de liberação antes de descartar tudo.",
        r.Detalhe);
  }

  [Theory]
  [InlineData(Posicoes.Montado)]
  [InlineData(Posicoes.Expedido)]
  [InlineData(Posicoes.Perdido)]
  public void Origem_Montado_e_OrigemInvalida(string posicao)
  {
    var calc = Calcular(
        [No(10, null, 2m, null, Solda), No(20, 10, 2m, 1m, Corte)],
        [Em(20, Local.AIniciar, -2m), Em(20, new Local(posicao, null, null), 2m)]);

    var r = Recusar(calc, Parte(20, [10], De(new Local(posicao, null, null), 1m)));

    Assert.Equal(CodigosDaBaixa.OrigemInvalida, r.Erro);
  }

  [Fact]
  public void Origem_com_Setor_ou_passo_incoerente_e_OrigemInvalida()
  {
    var calc = Calcular([No(1, null, 4m, null, Corte)], [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Corte, 1), 4m)]);

    Assert.Equal(CodigosDaBaixa.OrigemInvalida,
        Recusar(calc, Parte(1, [], De(new Local(Posicoes.NoSetor, Corte, null), 1m))).Erro);
    Assert.Equal(CodigosDaBaixa.OrigemInvalida,
        Recusar(calc, Parte(1, [], De(new Local(Posicoes.AIniciar, Corte, null), 1m))).Erro);
    Assert.Equal(CodigosDaBaixa.OrigemInvalida,
        Recusar(calc, Parte(1, [], De(new Local(Posicoes.AguardandoMontagem, Corte, 1), 1m))).Erro);
  }

  [Fact]
  public void Origem_repetida_e_ItemRepetido()
  {
    var calc = Calcular([No(1, null, 4m, null, Corte)], [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Corte, 1), 4m)]);

    var r = Recusar(calc, Parte(1, [], De(Local.NoSetor(Corte, 1), 1m), De(Local.NoSetor(Corte, 1), 1m)));

    Assert.Equal(CodigosDaExecucao.ItemRepetido, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
  }

  [Fact]
  public void Acima_do_saldo_e_SaldoInsuficiente_nomeando_a_posicao()
  {
    var peca = CopiadoDoCatalogo(1, null);
    var calc = Calcular(
        [No(1, null, 4m, null, Corte, Solda)],
        [Em(1, Local.AIniciar, -4m), Em(1, Local.NoSetor(Solda, 2), 2m), Em(1, Local.AguardandoColeta(Corte, 1), 2m)]);

    var r = Recusar(calc, Parte(1, [], De(Local.AguardandoColeta(Corte, 1), 1m), De(Local.NoSetor(Solda, 2), 3m)),
        NomesPelaRegra19(peca));

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal("Só há 2 de Chapa dobrada 3 mm no Setor Solda (passo 2).", r.Detalhe);
  }

  [Fact]
  public void Tudo_sem_nada_em_producao_e_NadaEmProducao()
  {
    var peca = CopiadoDoCatalogo(10, null);
    var calc = Calcular(
        [No(10, null, 2m, null, Solda), No(20, 10, 2m, 1m, Corte)],
        [
          Em(10, Local.AIniciar, -2m), Em(10, Local.Expedido, 2m),
          Em(20, Local.AIniciar, -2m), Em(20, Local.Montado, 2m),
        ],
        new Dictionary<int, decimal> { [10] = 2m });

    var r = Recusar(calc, Tudo(10), NomesPelaRegra19(peca));

    Assert.Equal(CodigosDaBaixa.NadaEmProducao, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal("Chapa dobrada 3 mm não tem nada em produção para dar baixa.", r.Detalhe);
  }

  [Fact]
  public void Tudo_com_origens_e_OrigemInvalida()
  {
    var calc = Calcular([No(1, null, 4m, null, Corte)], []);
    var pedido = new PedidoDeBaixa(1, MotivosDePerda.Descarte, true, [De(Local.AIniciar, 1m)], false, []);

    Assert.Equal(CodigosDaBaixa.OrigemInvalida, Recusar(calc, pedido).Erro);
  }

  // A Peca 1 (4, nada iniciado), o Item 2 (8, razao 2, nada iniciado) e o Item 3 (24, razao 3): 20 no Setor e 4 a
  // iniciar. Perder 4 do Item 3 no Setor deixa 20, que montam so 6 do Item 2: faltam 2 do Item 2, que travam 1 Peca.
  private static CalculadoraDeExecucao CadeiaDeTresNiveis() => Calcular(
      [No(1, null, 4m, null, Solda), No(2, 1, 8m, 2m, Pintura), No(3, 2, 24m, 3m, Corte)],
      [Em(3, Local.AIniciar, -20m), Em(3, Local.NoSetor(Corte, 1), 20m)]);

  [Fact]
  public void Perda_de_item_que_trava_um_pai_calcula_a_trava_no_topo()
  {
    var plano = Planejar(CadeiaDeTresNiveis(), Parte(3, [2, 1], De(Local.NoSetor(Corte, 1), 4m)));

    Assert.Equal([new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 4m)], plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Equal(new TravaNoTopo(1, 1m, 4m), plano.Trava);
  }

  [Fact]
  public void Perda_de_item_que_os_filhos_ainda_cobrem_nao_trava()
  {
    // O Item 3 tem 26 para os 24 que o Item 2 precisa: perder 2 a iniciar ainda deixa 24.
    var calc = Calcular(
        [No(1, null, 4m, null, Solda), No(2, 1, 8m, 2m, Pintura), No(3, 2, 26m, 3m, Corte)],
        [Em(3, Local.AIniciar, -20m), Em(3, Local.NoSetor(Corte, 1), 20m)]);

    var plano = Planejar(calc, Parte(3, [2, 1], De(Local.AIniciar, 2m)));

    Assert.Null(plano.Trava);
  }

  [Fact]
  public void Trava_no_topo_desconta_o_que_o_nivel_de_cima_tem_alem_do_que_o_pai_precisa()
  {
    // O Item 2 tem 10 para os 8 que a Peca precisa (2 ja iniciados, no Setor). Perder 4 do Item 3 trava 2 do Item 2,
    // mas os 8 que sobram do Item 2 ainda montam as 4 Pecas: nada trava no topo.
    var calc = Calcular(
        [No(1, null, 4m, null, Solda), No(2, 1, 10m, 2m, Pintura), No(3, 2, 30m, 3m, Corte)],
        [
          Em(2, Local.AIniciar, -2m), Em(2, Local.NoSetor(Pintura, 1), 2m),
          Em(3, Local.AIniciar, -30m), Em(3, Local.Montado, 6m), Em(3, Local.NoSetor(Corte, 1), 24m),
        ],
        new Dictionary<int, decimal> { [2] = 2m });

    var plano = Planejar(calc, Parte(3, [2, 1], De(Local.NoSetor(Corte, 1), 4m)));

    Assert.Null(plano.Trava);
  }

  [Fact]
  public void IncluirTopo_leva_a_Peca_de_a_iniciar_e_desce_a_cascata()
  {
    var plano = Planejar(CadeiaDeTresNiveis(), ParteComTopo(3, [2, 1], De(Local.NoSetor(Corte, 1), 4m)));

    // A Peca perde 1 a iniciar; as 3 que ficam precisam de 6 do Item 2, que perde 2 a iniciar; os 6 que ficam precisam
    // de 18 do Item 3, que tem 20 em producao: os 2 a iniciar alem disso saem.
    Assert.Equal(
        [
          new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 4m),
          new LinhaPlanejada(1, Local.AIniciar, 1m),
          new LinhaPlanejada(2, Local.AIniciar, 2m),
          new LinhaPlanejada(3, Local.AIniciar, 2m),
        ],
        plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Equal(new TravaNoTopo(1, 1m, 4m), plano.Trava);
  }

  [Fact]
  public void IncluirTopo_com_menos_a_iniciar_leva_o_que_ha()
  {
    // Montado a mao: a Peca 1 tem 2 por montar, mas so 1 a iniciar. A perda de todo o Item 3 em producao trava as 2.
    var calc = Calcular(
        [No(1, null, 4m, null, Solda), No(2, 1, 8m, 2m, Pintura), No(3, 2, 24m, 3m, Corte)],
        [
          Em(1, Local.AIniciar, -3m), Em(1, Local.NoSetor(Solda, 1), 3m),
          Em(2, Local.AIniciar, -4m), Em(2, Local.Montado, 4m),
          Em(3, Local.AIniciar, -24m), Em(3, Local.Montado, 12m), Em(3, Local.NoSetor(Corte, 1), 12m),
        ],
        new Dictionary<int, decimal> { [1] = 2m, [2] = 4m });

    var plano = Planejar(calc, ParteComTopo(3, [2, 1], De(Local.NoSetor(Corte, 1), 12m)));

    Assert.Equal(new TravaNoTopo(1, 2m, 1m), plano.Trava);
    Assert.Equal(
        [
          new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 12m),
          new LinhaPlanejada(1, Local.AIniciar, 1m),
          new LinhaPlanejada(2, Local.AIniciar, 2m),
        ],
        plano.Linhas);
  }

  [Fact]
  public void IncluirTopo_em_Peca_e_OrigemInvalida()
  {
    var r = Recusar(PecaComFilhoMontado(), ParteComTopo(10, [], De(Local.AIniciar, 1m)));

    Assert.Equal(CodigosDaBaixa.OrigemInvalida, r.Erro);
  }

  // A Peca 1 (4, nada iniciado), o Item 2 (10, razao 2; 2 ja iniciados, no Setor, e 8 a iniciar) e o Item 3 (30,
  // razao 3; 6 montados no Item 2 e 24 no Setor).
  private static CalculadoraDeExecucao CadeiaComItem2Iniciado() => Calcular(
      [No(1, null, 4m, null, Solda), No(2, 1, 10m, 2m, Pintura), No(3, 2, 30m, 3m, Corte)],
      [
        Em(2, Local.AIniciar, -2m), Em(2, Local.NoSetor(Pintura, 1), 2m),
        Em(3, Local.AIniciar, -30m), Em(3, Local.Montado, 6m), Em(3, Local.NoSetor(Corte, 1), 24m),
      ],
      new Dictionary<int, decimal> { [2] = 2m });

  private static PedidoDeBaixa TudoDoItem(string motivo, bool incluirTopo) =>
      new(3, motivo, true, Array.Empty<OrigemDaBaixa>(), incluirTopo, [2, 1]);

  [Fact]
  public void Tudo_por_perda_de_item_que_trava_calcula_a_trava_no_topo()
  {
    // D14: sem o Item 3, os 8 do Item 2 por montar ficam travados; os 2 ja iniciados montam 1 Peca, e 3 travam.
    var plano = Planejar(CadeiaComItem2Iniciado(), TudoDoItem(MotivosDePerda.MortaEmProcesso, incluirTopo: false));

    Assert.Equal([new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 24m)], plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Equal(new TravaNoTopo(1, 3m, 4m), plano.Trava);
  }

  [Fact]
  public void Tudo_por_perda_com_IncluirTopo_leva_a_Peca_de_a_iniciar()
  {
    // A Peca perde 3 a iniciar; a que fica precisa de 2 do Item 2, os 2 ja iniciados: os 8 a iniciar dele saem.
    var plano = Planejar(CadeiaComItem2Iniciado(), TudoDoItem(MotivosDePerda.PerdaArmazem, incluirTopo: true));

    Assert.Equal(
        [
          new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 24m),
          new LinhaPlanejada(1, Local.AIniciar, 3m),
          new LinhaPlanejada(2, Local.AIniciar, 8m),
        ],
        plano.Linhas);
    Assert.Empty(plano.ViramSobra);
    Assert.Equal(new TravaNoTopo(1, 3m, 4m), plano.Trava);
  }

  [Fact]
  public void Tudo_por_descarte_nao_tem_trava_e_IncluirTopo_e_OrigemInvalida()
  {
    // C22: o Item descartado por inteiro sai do projeto e deixa de travar o pai.
    var plano = Planejar(CadeiaComItem2Iniciado(), TudoDoItem(MotivosDePerda.Descarte, incluirTopo: false));
    Assert.Equal([new LinhaPlanejada(3, Local.NoSetor(Corte, 1), 24m)], plano.Linhas);
    Assert.Null(plano.Trava);

    var r = Recusar(CadeiaComItem2Iniciado(), TudoDoItem(MotivosDePerda.Descarte, incluirTopo: true));
    Assert.Equal(CodigosDaBaixa.OrigemInvalida, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
  }

  [Fact]
  public void Tudo_por_perda_de_Peca_com_IncluirTopo_e_OrigemInvalida()
  {
    var pedido = new PedidoDeBaixa(10, MotivosDePerda.PerdaArmazem, true, Array.Empty<OrigemDaBaixa>(), true, []);

    Assert.Equal(CodigosDaBaixa.OrigemInvalida, Recusar(PecaComFilhoMontado(), pedido).Erro);
  }

  [Fact]
  public void Razao_com_casas_arredonda_para_baixo_em_quatro_casas_o_que_sai_e_o_que_vira_sobra()
  {
    // Razao 0,3333 e meia Peca perdida: as 2,5 que ficam pedem 0,83325 de cada filho, cinco casas que o livro nao
    // guarda. O filho 20 (1 a iniciar) perde 0,16675 -> 0,1667; o filho 30 (1 no Setor) devia 0,9999 e passa a dever
    // 0,83325: a sobra vai de 0,0001 a 0,16675, e o que a baixa faz virar sobra e 0,16665 -> 0,1666.
    var calc = Calcular(
        [No(10, null, 3m, null, Solda), No(20, 10, 1m, 0.3333m, Corte), No(30, 10, 1m, 0.3333m, Corte)],
        [Em(30, Local.AIniciar, -1m), Em(30, Local.NoSetor(Corte, 1), 1m)]);

    var plano = Planejar(calc, Parte(10, [], De(Local.AIniciar, 0.5m)));

    Assert.Equal(
        [new LinhaPlanejada(10, Local.AIniciar, 0.5m), new LinhaPlanejada(20, Local.AIniciar, 0.1667m)],
        plano.Linhas);
    Assert.Equal([(30, 0.1666m)], plano.ViramSobra);
  }

  [Fact]
  public void Perda_ja_gravada_do_pai_nao_conta_de_novo_na_segunda_baixa()
  {
    // A Peca 10 (10) ja perdeu 3 a iniciar numa baixa gravada, que levou 6 a iniciar do filho 20 (razao 2): falta
    // montar 7, e o filho tem 14. Perder mais 2 a iniciar deixa 5 por montar, que pedem 10: saem 4 a iniciar do filho.
    var calc = Calcular(
        [No(10, null, 10m, null, Solda), No(20, 10, 20m, 2m, Corte)],
        [
          Em(10, Local.AIniciar, -3m), Em(10, Local.Perdido, 3m),
          Em(20, Local.AIniciar, -6m), Em(20, Local.Perdido, 6m),
        ],
        perdidosDeAIniciar: new Dictionary<int, decimal> { [10] = 3m, [20] = 6m });
    Assert.Equal(7m, calc.FaltaMontar(10));

    var plano = Planejar(calc, Parte(10, [], De(Local.AIniciar, 2m)));

    Assert.Equal(
        [new LinhaPlanejada(10, Local.AIniciar, 2m), new LinhaPlanejada(20, Local.AIniciar, 4m)],
        plano.Linhas);
  }
}
