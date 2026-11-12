using Rastreamento.Application.Baixa;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Retrabalho;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Retrabalho;

/// <summary>
/// A conta pura do "Usar a sobra" (secao 4.4 da spec da Fase 5B-2; C17, C18 e C19): a sobra viva do original por
/// posicao (decisao P8 do plano de backend), a sugestao de cima para baixo, a conferencia do pedido do PCP e a cascata
/// que tira os filhos a iniciar que o pai pronto ja nao pede. As duas arvores sao montadas a mao na calculadora, com
/// o pronto passado explicitamente (o cenario em memoria da execucao nao o passa).
///
/// A arvore e a da secao 4.4: A, a Peca, com B, que tem C, que tem D, quatro D por C. No Retrabalho os nos sao
/// 101 a 104; no original, 1 a 4.
/// </summary>
public class CalculoDoReaproveitamentoTests
{
  private const int Corte = 1, Dobra = 2, Solda = 3, Pintura = 4;
  private const int A = 101, B = 102, C = 103, D = 104;
  private const int OA = 1, OB = 2, OC = 3, OD = 4;

  private static readonly IReadOnlyDictionary<int, int> OrigemDe =
      new Dictionary<int, int> { [A] = OA, [B] = OB, [C] = OC, [D] = OD };

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList());

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calcular(
      IEnumerable<NoDoCalculo> nos, IEnumerable<SaldoLiquido> saldos, IReadOnlyDictionary<int, decimal>? montados = null,
      IReadOnlyDictionary<int, decimal>? perdidosDeAIniciar = null, IReadOnlyDictionary<int, decimal>? prontos = null) =>
      new(nos, saldos, montados ?? new Dictionary<int, decimal>(), Array.Empty<(int, int)>(),
          perdidosDeAIniciar: perdidosDeAIniciar, prontos: prontos);

  // Regra 19: D e copiado do catalogo, sem descricao propria, e se chama pelo Componente.
  private static readonly Componente Chapa = new()
  {
    Id = 70, Codigo = "CHP-220", Descricao = "Chapa dobrada 3 mm", Tipo = "Fabricado", Ativo = true,
  };

  private static readonly EstruturaItem[] NosDoRetrabalho =
  [
    new() { Id = A, Descricao = "Carenagem", NivelHierarquico = "Peca", ComponenteId = 71 },
    new() { Id = B, EstruturaPaiId = A, Descricao = "Quadro", NivelHierarquico = "Item" },
    new() { Id = C, EstruturaPaiId = B, Descricao = "Suporte", NivelHierarquico = "Item" },
    new() { Id = D, EstruturaPaiId = C, ComponenteId = Chapa.Id, Descricao = null, NivelHierarquico = "Item" },
  ];

  private static string Nome(int id)
  {
    var no = NosDoRetrabalho.Single(n => n.Id == id);
    return no.Descricao ?? (no.ComponenteId == Chapa.Id ? Chapa.Descricao : $"no {id}");
  }

  /// <summary>O Retrabalho recem-aberto: tudo a iniciar, nada andou. `rotDeD` deixa o PCP trocar o Roteiro de D.</summary>
  private static IReadOnlyList<NoDoCalculo> ArvoreDoRetrabalho(decimal escala = 1m, int rotDeD = Corte) =>
  [
    No(A, null, escala, null, Pintura), No(B, A, escala, 1m, Solda), No(C, B, escala, 1m, Dobra),
    No(D, C, 4m * escala, 4m, rotDeD),
  ];

  private static CalculadoraDeExecucao RetrabalhoNovo(int rotDeD = Corte) => Calcular(ArvoreDoRetrabalho(rotDeD: rotDeD), []);

  /// <summary>
  /// O original em que B se perdeu de "a iniciar": C e as quatro D dele nao sao mais pedidos. Duas D ja estavam
  /// fabricadas, aguardando coleta no Corte: sao a sobra da secao 4.4.
  /// </summary>
  private static CalculadoraDeExecucao OriginalComDuasDDeSobra() => Calcular(
      [No(OA, null, 1m, null, Pintura), No(OB, OA, 1m, 1m, Solda), No(OC, OB, 1m, 1m, Dobra), No(OD, OC, 4m, 4m, Corte)],
      [
        Em(OB, Local.AIniciar, -1m), Em(OB, Local.Perdido, 1m),
        Em(OC, Local.AIniciar, -1m), Em(OC, Local.Perdido, 1m),
        Em(OD, Local.AIniciar, -4m), Em(OD, Local.AguardandoColeta(Corte, 1), 2m), Em(OD, Local.Perdido, 2m),
      ],
      perdidosDeAIniciar: new Dictionary<int, decimal> { [OB] = 1m, [OC] = 1m, [OD] = 2m });

  /// <summary>
  /// O original em que B se perdeu e sobraram um C pronto (aguardando coleta na Dobra) e duas D (aguardando coleta no
  /// Corte), alem das quatro D montadas no C.
  /// </summary>
  private static CalculadoraDeExecucao OriginalComCEDDeSobra() => Calcular(
      [No(OA, null, 1m, null, Pintura), No(OB, OA, 1m, 1m, Solda), No(OC, OB, 2m, 1m, Dobra), No(OD, OC, 8m, 4m, Corte)],
      [
        Em(OB, Local.AIniciar, -1m), Em(OB, Local.Perdido, 1m),
        Em(OC, Local.AIniciar, -2m), Em(OC, Local.AguardandoColeta(Dobra, 1), 1m), Em(OC, Local.Perdido, 1m),
        Em(OD, Local.AIniciar, -8m), Em(OD, Local.Montado, 4m), Em(OD, Local.AguardandoColeta(Corte, 1), 2m),
        Em(OD, Local.Perdido, 2m),
      ],
      montados: new Dictionary<int, decimal> { [OC] = 1m },
      perdidosDeAIniciar: new Dictionary<int, decimal> { [OB] = 1m, [OC] = 1m, [OD] = 2m });

  private static ItemReaproveitado Usar(int no, Local local, decimal quantidade) => new(no, local, quantidade);

  private static Result<IReadOnlyList<ItemReaproveitado>> Conferir(
      CalculadoraDeExecucao retrabalho, CalculadoraDeExecucao original, params ItemReaproveitado[] pedido) =>
      CalculoDoReaproveitamento.Conferir(retrabalho, CalculoDoReaproveitamento.Disponivel(original, OrigemDe), pedido, Nome);

  [Fact]
  public void Disponivel_lista_a_sobra_da_coleta_do_no_de_origem()
  {
    var disponivel = CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), OrigemDe);

    Assert.Equal([new SobraDisponivel(D, OD, Local.AguardandoColeta(Corte, 1), 2m)], disponivel);
  }

  [Fact]
  public void Sugere_o_minimo_entre_a_sobra_e_o_que_o_no_precisa()
  {
    var disponivel = CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), OrigemDe);

    var sugestao = CalculoDoReaproveitamento.Sugerir(RetrabalhoNovo(), disponivel);

    var (sobra, precisa, sugerido) = Assert.Single(sugestao);
    Assert.Equal(D, sobra.NoDoRetrabalhoId);
    Assert.Equal(4m, precisa);
    Assert.Equal(2m, sugerido);
  }

  [Fact]
  public void Sugestao_desce_do_topo_e_o_pronto_do_pai_reduz_o_do_filho()
  {
    var disponivel = CalculoDoReaproveitamento.Disponivel(OriginalComCEDDeSobra(), OrigemDe);

    var sugestao = CalculoDoReaproveitamento.Sugerir(RetrabalhoNovo(), disponivel);

    Assert.Equal(2, sugestao.Count);
    var doC = sugestao.Single(s => s.Item1.NoDoRetrabalhoId == C);
    Assert.Equal((1m, 1m), (doC.Precisa, doC.Sugerido));
    var doD = sugestao.Single(s => s.Item1.NoDoRetrabalhoId == D);
    Assert.Equal((0m, 0m), (doD.Precisa, doD.Sugerido));
  }

  [Fact]
  public void Sobra_em_montagem_reparte_por_Setor()
  {
    // C perdeu uma das duas unidades de "a iniciar": ainda pede 4 D. Ha 6 D aguardando montagem, 1 na Dobra e 5 na
    // Solda, entao sobram 2, repartidas pelos Setores em ordem de Id ate o saldo de cada um.
    var original = Calcular(
        [No(OA, null, 2m, null, Pintura), No(OB, OA, 2m, 1m, Solda), No(OC, OB, 2m, 1m, Dobra), No(OD, OC, 8m, 4m, Corte)],
        [
          Em(OC, Local.AIniciar, -1m), Em(OC, Local.Perdido, 1m),
          Em(OD, Local.AIniciar, -6m), Em(OD, Local.AguardandoMontagem(Dobra), 1m), Em(OD, Local.AguardandoMontagem(Solda), 5m),
        ],
        perdidosDeAIniciar: new Dictionary<int, decimal> { [OC] = 1m });

    var disponivel = CalculoDoReaproveitamento.Disponivel(original, OrigemDe);

    Assert.Equal(
        [
          new SobraDisponivel(D, OD, Local.AguardandoMontagem(Dobra), 1m),
          new SobraDisponivel(D, OD, Local.AguardandoMontagem(Solda), 1m),
        ],
        disponivel);

    // A segunda posicao do mesmo no ja ve a primeira sugestao: precisa 4, depois 3. Ela nao e sugerida porque C, o pai,
    // comeca na Dobra: aguardando montagem na Solda, o pronto nao teria onde ficar no Retrabalho.
    var sugestao = CalculoDoReaproveitamento.Sugerir(RetrabalhoNovo(), disponivel);
    Assert.Equal([(4m, 1m), (3m, 0m)], sugestao.Select(s => (s.Precisa, s.Sugerido)).ToList());
  }

  [Fact]
  public void No_sem_origem_nao_aparece_no_disponivel()
  {
    // D foi tirado e acrescentado a mao pelo PCP: nao tem origem. A sobra de D no original continua la.
    var semD = new Dictionary<int, int> { [A] = OA, [B] = OB, [C] = OC };

    Assert.NotEmpty(CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), OrigemDe));
    Assert.DoesNotContain(
        CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), semD), s => s.NoDoRetrabalhoId == D);
  }

  [Fact]
  public void Pedir_dentro_da_sobra_e_do_que_o_no_precisa_passa()
  {
    var r = Conferir(RetrabalhoNovo(), OriginalComDuasDDeSobra(), Usar(D, Local.AguardandoColeta(Corte, 1), 2m));

    Assert.True(r.Sucesso, $"{r.Erro}: {r.Detalhe}");
    Assert.Equal([Usar(D, Local.AguardandoColeta(Corte, 1), 2m)], r.Valor!);
  }

  [Fact]
  public void Pedir_alem_da_sobra_e_409_AlemDaSobra()
  {
    var r = Conferir(RetrabalhoNovo(), OriginalComDuasDDeSobra(), Usar(D, Local.AguardandoColeta(Corte, 1), 3m));

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDoRetrabalho.AlemDaSobra, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("Chapa dobrada 3 mm", r.Detalhe);
    Assert.Contains("2", r.Detalhe);
  }

  [Fact]
  public void Pedir_alem_do_a_iniciar_do_no_e_409_SaldoInsuficiente()
  {
    // No Retrabalho, D perdeu tres de "a iniciar": so uma pode ficar pronta, embora C ainda peca quatro.
    var retrabalho = Calcular(
        ArvoreDoRetrabalho(),
        [Em(D, Local.AIniciar, -3m), Em(D, Local.Perdido, 3m)],
        perdidosDeAIniciar: new Dictionary<int, decimal> { [D] = 3m });

    var r = Conferir(retrabalho, OriginalComDuasDDeSobra(), Usar(D, Local.AguardandoColeta(Corte, 1), 2m));

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("Chapa dobrada 3 mm", r.Detalhe);
  }

  [Fact]
  public void Pedir_alem_do_que_o_pai_ainda_pede_e_409_AlemDoQueOPaiPrecisa()
  {
    var original = OriginalComCEDDeSobra();

    // C pronto no mesmo pedido ja chega montado: D nao e mais pedida.
    var r = Conferir(RetrabalhoNovo(), original,
        Usar(C, Local.AguardandoColeta(Dobra, 1), 1m), Usar(D, Local.AguardandoColeta(Corte, 1), 2m));

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.AlemDoQueOPaiPrecisa, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("Chapa dobrada 3 mm", r.Detalhe);
    Assert.Contains("Suporte", r.Detalhe);

    // Sem o pronto de C, as duas D passam.
    Assert.True(Conferir(RetrabalhoNovo(), original, Usar(D, Local.AguardandoColeta(Corte, 1), 2m)).Sucesso);
  }

  [Fact]
  public void Pronto_de_um_pai_ja_gravado_tambem_conta_no_que_o_filho_pode()
  {
    // C ficou pronto num uso anterior da sobra: no segundo, D nao e mais pedida.
    var retrabalho = Calcular(
        ArvoreDoRetrabalho(),
        [Em(C, Local.AIniciar, -1m), Em(C, Local.AguardandoColeta(Dobra, 1), 1m)],
        prontos: new Dictionary<int, decimal> { [C] = 1m });

    var r = Conferir(retrabalho, OriginalComDuasDDeSobra(), Usar(D, Local.AguardandoColeta(Corte, 1), 1m));

    Assert.Equal(CodigosDaExecucao.AlemDoQueOPaiPrecisa, r.Erro);
  }

  [Fact]
  public void Pronto_anterior_do_proprio_no_conta_no_que_o_pai_ainda_pede()
  {
    // O PCP pos seis D no Retrabalho, embora C peca quatro; duas ja ficaram prontas num uso anterior. Restam quatro a
    // iniciar, mas C so pede mais duas.
    var retrabalho = Calcular(
        [No(A, null, 1m, null, Pintura), No(B, A, 1m, 1m, Solda), No(C, B, 1m, 1m, Dobra), No(D, C, 6m, 4m, Corte)],
        [Em(D, Local.AIniciar, -2m), Em(D, Local.AguardandoColeta(Corte, 1), 2m)],
        prontos: new Dictionary<int, decimal> { [D] = 2m });
    var disponivel = new[] { new SobraDisponivel(D, OD, Local.AguardandoColeta(Corte, 1), 3m) };

    var (_, precisa, sugerido) = Assert.Single(CalculoDoReaproveitamento.Sugerir(retrabalho, disponivel));
    Assert.Equal((2m, 2m), (precisa, sugerido));

    var r = CalculoDoReaproveitamento.Conferir(retrabalho, disponivel, [Usar(D, Local.AguardandoColeta(Corte, 1), 3m)], Nome);
    Assert.Equal(CodigosDaExecucao.AlemDoQueOPaiPrecisa, r.Erro);
    Assert.Contains("Só 2 de Chapa dobrada 3 mm", r.Detalhe);
  }

  [Fact]
  public void Pronto_do_avo_tambem_tira_o_que_o_neto_pode()
  {
    // A se perdeu no original: sobrou um B pronto (com o C e as quatro D montados) e duas D aguardando coleta.
    var original = Calcular(
        [No(OA, null, 1m, null, Pintura), No(OB, OA, 1m, 1m, Solda), No(OC, OB, 1m, 1m, Dobra), No(OD, OC, 6m, 4m, Corte)],
        [
          Em(OA, Local.AIniciar, -1m), Em(OA, Local.Perdido, 1m),
          Em(OB, Local.AIniciar, -1m), Em(OB, Local.AguardandoColeta(Solda, 1), 1m),
          Em(OC, Local.AIniciar, -1m), Em(OC, Local.Montado, 1m),
          Em(OD, Local.AIniciar, -6m), Em(OD, Local.Montado, 4m), Em(OD, Local.AguardandoColeta(Corte, 1), 2m),
        ],
        montados: new Dictionary<int, decimal> { [OB] = 1m, [OC] = 1m },
        perdidosDeAIniciar: new Dictionary<int, decimal> { [OA] = 1m });

    var disponivel = CalculoDoReaproveitamento.Disponivel(original, OrigemDe);
    var sugestao = CalculoDoReaproveitamento.Sugerir(RetrabalhoNovo(), disponivel);
    Assert.Equal((1m, 1m), sugestao.Where(s => s.Item1.NoDoRetrabalhoId == B).Select(s => (s.Precisa, s.Sugerido)).Single());
    Assert.Equal((0m, 0m), sugestao.Where(s => s.Item1.NoDoRetrabalhoId == D).Select(s => (s.Precisa, s.Sugerido)).Single());

    var r = CalculoDoReaproveitamento.Conferir(RetrabalhoNovo(), disponivel,
        [Usar(B, Local.AguardandoColeta(Solda, 1), 1m), Usar(D, Local.AguardandoColeta(Corte, 1), 2m)], Nome);
    Assert.Equal(CodigosDaExecucao.AlemDoQueOPaiPrecisa, r.Erro);
  }

  [Fact]
  public void Posicao_que_nao_existe_no_Roteiro_do_Retrabalho_e_400_PosicaoDoProntoInexistente()
  {
    // O PCP trocou o Roteiro de D no Retrabalho: o Corte saiu, entrou a Pintura.
    var retrabalho = RetrabalhoNovo(rotDeD: Pintura);
    var disponivel = CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), OrigemDe);

    var r = CalculoDoReaproveitamento.Conferir(
        retrabalho, disponivel, [Usar(D, Local.AguardandoColeta(Corte, 1), 2m)], Nome);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDoRetrabalho.PosicaoDoProntoInexistente, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("Chapa dobrada 3 mm", r.Detalhe);

    // A previa nao sugere o que a confirmacao recusaria.
    var (_, precisa, sugerido) = Assert.Single(CalculoDoReaproveitamento.Sugerir(retrabalho, disponivel));
    Assert.Equal((4m, 0m), (precisa, sugerido));

    // Aguardando montagem: o pai de D (C) comeca na Dobra, nao na Solda.
    var naMontagem = CalculoDoReaproveitamento.Conferir(
        RetrabalhoNovo(), [new SobraDisponivel(D, OD, Local.AguardandoMontagem(Solda), 2m)],
        [Usar(D, Local.AguardandoMontagem(Solda), 1m)], Nome);
    Assert.Equal(CodigosDoRetrabalho.PosicaoDoProntoInexistente, naMontagem.Erro);
    Assert.Contains("Chapa dobrada 3 mm", naMontagem.Detalhe);
  }

  [Fact]
  public void Pedido_mal_formado_e_400()
  {
    var retrabalho = RetrabalhoNovo();
    var disponivel = CalculoDoReaproveitamento.Disponivel(OriginalComDuasDDeSobra(), OrigemDe);

    Assert.Equal(CodigosDaExecucao.LoteVazio,
        CalculoDoReaproveitamento.Conferir(retrabalho, disponivel, [], Nome).Erro);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida,
        CalculoDoReaproveitamento.Conferir(retrabalho, disponivel, [Usar(D, Local.AguardandoColeta(Corte, 1), 0m)], Nome).Erro);
    Assert.Equal(CodigosDaExecucao.ItemRepetido,
        CalculoDoReaproveitamento.Conferir(retrabalho, disponivel,
            [Usar(D, Local.AguardandoColeta(Corte, 1), 1m), Usar(D, Local.AguardandoColeta(Corte, 1), 1m)], Nome).Erro);
    Assert.Equal(CodigosDoRetrabalho.PosicaoDoProntoInexistente,
        CalculoDoReaproveitamento.Conferir(retrabalho, disponivel, [Usar(D, Local.NoSetor(Corte, 1), 1m)], Nome).Erro);
    Assert.Equal("NaoEncontrado",
        CalculoDoReaproveitamento.Conferir(retrabalho, disponivel, [Usar(999, Local.AguardandoColeta(Corte, 1), 1m)], Nome).Erro);
  }

  [Fact]
  public void Dispensa_do_pronto_tira_os_filhos_a_iniciar_que_o_pai_pronto_nao_pede()
  {
    var calcComPronto = Calcular(
        ArvoreDoRetrabalho(),
        [Em(C, Local.AIniciar, -1m), Em(C, Local.AguardandoColeta(Dobra, 1), 1m)],
        prontos: new Dictionary<int, decimal> { [C] = 1m });

    var linhas = CalculoDaBaixa.PlanejarDispensaDoPronto(calcComPronto, [C]);

    Assert.Equal([new LinhaPlanejada(D, Local.AIniciar, 4m)], linhas);
  }

  [Fact]
  public void Dispensa_desce_dois_niveis()
  {
    var calcComPronto = Calcular(
        ArvoreDoRetrabalho(),
        [Em(B, Local.AIniciar, -1m), Em(B, Local.AguardandoColeta(Solda, 1), 1m)],
        prontos: new Dictionary<int, decimal> { [B] = 1m });

    var linhas = CalculoDaBaixa.PlanejarDispensaDoPronto(calcComPronto, [B]);

    Assert.Equal([new LinhaPlanejada(C, Local.AIniciar, 1m), new LinhaPlanejada(D, Local.AIniciar, 4m)], linhas);
  }

  [Fact]
  public void Dispensa_do_pronto_parcial_deixa_o_que_o_pai_ainda_pede()
  {
    // Duas unidades de C, uma pronta: das oito D a iniciar, so as quatro da unidade pronta saem.
    var calcComPronto = Calcular(
        ArvoreDoRetrabalho(escala: 2m),
        [Em(C, Local.AIniciar, -1m), Em(C, Local.AguardandoColeta(Dobra, 1), 1m)],
        prontos: new Dictionary<int, decimal> { [C] = 1m });

    Assert.Equal([new LinhaPlanejada(D, Local.AIniciar, 4m)], CalculoDaBaixa.PlanejarDispensaDoPronto(calcComPronto, [C]));
    Assert.Empty(CalculoDaBaixa.PlanejarDispensaDoPronto(calcComPronto, [D]));
  }
}
