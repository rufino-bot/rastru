using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Iniciar e terminar em lote (spec dos filtros e do lote, secao 6): o lote e a acao individual item a
/// item, numa transacao so, tudo ou nada. O teste central compara o livro de dois cenarios identicos,
/// um feito em lote e o outro um a um.
/// </summary>
public class LoteDeApontamentoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>
  /// Na Solda: a Peca 10 a iniciar (Roteiro Solda) e o pai 1 (Roteiro Solda) com os filhos 2 (razao 2) e
  /// 3 (razao 1) aguardando montagem ali — 6 de 2 e 3 de 3, o bastante para 3 do pai.
  /// </summary>
  private static CenarioDeExecucao NaSolda()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 20m, 2m, Corte);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    c.No(10, null, 10m, null, Solda);
    return c;
  }

  private static LoteDeInicioDto Inicios(int setor, params (int No, decimal Quantidade)[] itens) =>
      new(setor, itens.Select(i => new ItemDeInicioDto(i.No, i.Quantidade)).ToList());

  private static LoteDeTerminoDto Terminos(int setor, params (int No, int Ordem, decimal Quantidade)[] itens) =>
      new(setor, itens.Select(i => new ItemDeTerminoDto(i.No, i.Ordem, i.Quantidade)).ToList());

  /// <summary>O livro sem Id nem hora: o que tem de ser igual entre o lote e os mesmos itens um a um.</summary>
  private static object[] LivroDe(CenarioDeExecucao c) =>
      c.Execucao.Movimentacoes
          .Select(m => (object)(m.EstruturaItemId, m.Tipo, m.Quantidade, Local.DaOrigem(m), Local.DoDestino(m),
              m.MontagemId is not null, m.UsuarioId))
          .ToArray();

  private static object[] MontagensDe(CenarioDeExecucao c) =>
      c.Execucao.Montagens.Select(g => (object)(g.EstruturaItemId, g.SetorId, g.Quantidade)).ToArray();

  // ------------------------------------------------------------------ equivalencia

  [Fact]
  public async Task Lote_de_inicios_produz_o_mesmo_livro_que_os_mesmos_inicios_um_a_um()
  {
    var emLote = NaSolda();
    var umAUm = NaSolda();

    var lote = await emLote.Apontamento().IniciarEmLote(Inicios(Solda, (10, 4m), (1, 2m)), Operador, Ct);
    Assert.True((await umAUm.Apontamento().Iniciar(10, new InicioDto(Solda, 4m), Operador, Ct)).Sucesso);
    Assert.True((await umAUm.Apontamento().Iniciar(1, new InicioDto(Solda, 2m), Operador, Ct)).Sucesso);

    Assert.True(lote.Sucesso);
    Assert.Equal(LivroDe(umAUm), LivroDe(emLote));
    Assert.Equal(MontagensDe(umAUm), MontagensDe(emLote));
    Assert.Equal(umAUm.Execucao.StatusDoPedido, emLote.Execucao.StatusDoPedido);
    // O que foi comparado nao e vazio: as duas entregas do arranjo, o Inicio da 10, as duas baixas e o
    // Inicio do pai.
    Assert.Equal(6, emLote.Execucao.Movimentacoes.Count);
    Assert.Single(emLote.Execucao.Montagens);
    Assert.Equal("EmProducao", emLote.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Lote_de_terminos_produz_o_mesmo_livro_que_os_mesmos_terminos_um_a_um()
  {
    static CenarioDeExecucao NoCorte()
    {
      var c = new CenarioDeExecucao();
      c.No(1, null, 10m, null, Corte);
      c.No(4, null, 10m, null, Corte, Dobra);
      c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);
      c.Mover(4, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
      return c;
    }
    var emLote = NoCorte();
    var umAUm = NoCorte();

    var lote = await emLote.Apontamento().TerminarEmLote(Terminos(Corte, (4, 1, 6m), (1, 1, 3m)), Operador, Ct);
    Assert.True((await umAUm.Apontamento().Terminar(4, new TerminoDto(Corte, 1, 6m), Operador, Ct)).Sucesso);
    Assert.True((await umAUm.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 3m), Operador, Ct)).Sucesso);

    Assert.True(lote.Sucesso);
    Assert.Equal(LivroDe(umAUm), LivroDe(emLote));
    Assert.Equal(umAUm.Execucao.StatusDoPedido, emLote.Execucao.StatusDoPedido);
    Assert.Equal(4, emLote.Execucao.Movimentacoes.Count);
  }

  [Fact]
  public async Task Terminar_o_mesmo_no_em_dois_passos_no_mesmo_lote_vale()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 2m);
    c.Mover(1, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NoSetor(Corte, 2), 2m);

    var r = await c.Apontamento().TerminarEmLote(Terminos(Corte, (1, 1, 3m), (1, 2, 2m)), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(
        new[] { (TiposDeMovimentacao.Termino, 1, 3m), (TiposDeMovimentacao.Termino, 2, 2m) },
        r.Valor!.Select(m => (m.Tipo, m.Origem.Ordem!.Value, m.Quantidade)).ToArray());
    var estado = c.Calcular();
    Assert.Equal(0m, estado.Saldo(1, Local.NoSetor(Corte, 1)));
    Assert.Equal(0m, estado.Saldo(1, Local.NoSetor(Corte, 2)));
  }

  [Fact]
  public async Task Resposta_vem_na_ordem_dos_itens()
  {
    var c = new CenarioDeExecucao();
    c.No(5, null, 10m, null, Corte);
    c.No(10, null, 10m, null, Corte);

    var r = await c.Apontamento().IniciarEmLote(Inicios(Corte, (10, 1m), (5, 2m)), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { (10, 1m), (5, 2m) }, r.Valor!.Select(m => (m.EstruturaItemId, m.Quantidade)).ToArray());
    Assert.All(r.Valor!, m => Assert.Equal("Operador do Corte", m.UsuarioNome));
  }

  // ------------------------------------------------------------------ recusas

  [Fact]
  public async Task Recusa_no_meio_do_lote_nao_grava_nada()
  {
    var c = NaSolda();
    var livroAntes = LivroDe(c);
    var montagensAntes = MontagensDe(c);

    var r = await c.Apontamento().IniciarEmLote(Inicios(Solda, (1, 2m), (10, 99m)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    // O pai gravou de verdade antes da recusa: a Montagem (para ter o Id) e depois as baixas e o Inicio.
    Assert.Equal(2, c.Execucao.Saves);
    Assert.Equal(livroAntes, LivroDe(c));
    Assert.Equal(montagensAntes, MontagensDe(c));
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(1, c.Execucao.Desfeitas);
    Assert.Equal(0, c.Execucao.Commits);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Lote_vazio_da_LoteVazio(bool nulo)
  {
    var c = NaSolda();

    var inicios = await c.Apontamento().IniciarEmLote(new LoteDeInicioDto(Solda, nulo ? null : []), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(new LoteDeTerminoDto(Solda, nulo ? null : []), Operador, Ct);

    AfirmarFalha(inicios, CodigosDaExecucao.LoteVazio, TipoDeErro.Validacao);
    AfirmarFalha(terminos, CodigosDaExecucao.LoteVazio, TipoDeErro.Validacao);
    Assert.Equal("Marque pelo menos um item.", inicios.Detalhe);
    Assert.Equal("Marque pelo menos um item.", terminos.Detalhe);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Lote_acima_do_teto_da_LoteGrandeDemais_sem_abrir_transacao()
  {
    var c = NaSolda();
    var ids = Enumerable.Range(1000, ApontamentoUseCase.TamanhoMaximoDoLote + 1).ToArray();

    var inicios = await c.Apontamento().IniciarEmLote(
        Inicios(Solda, ids.Select(id => (id, 1m)).ToArray()), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(
        Terminos(Solda, ids.Select(id => (id, 1, 1m)).ToArray()), Operador, Ct);

    AfirmarFalha(inicios, CodigosDaExecucao.LoteGrandeDemais, TipoDeErro.Validacao);
    AfirmarFalha(terminos, CodigosDaExecucao.LoteGrandeDemais, TipoDeErro.Validacao);
    Assert.Equal("Um lote tem no máximo 100 itens.", inicios.Detalhe);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Lote_no_teto_nao_e_recusado_pelo_teto()
  {
    var c = NaSolda();
    var ids = Enumerable.Range(1000, ApontamentoUseCase.TamanhoMaximoDoLote).ToArray();

    var inicios = await c.Apontamento().IniciarEmLote(
        Inicios(Solda, ids.Select(id => (id, 1m)).ToArray()), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(
        Terminos(Solda, ids.Select(id => (id, 1, 1m)).ToArray()), Operador, Ct);

    Assert.Equal(TipoDeErro.NaoEncontrado, inicios.TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, terminos.TipoDoErro);
  }

  [Fact]
  public async Task Item_repetido_da_ItemRepetido_com_o_no()
  {
    var c = new CenarioDeExecucao();
    c.No(7, null, 10m, null, Corte, Corte);

    var inicios = await c.Apontamento().IniciarEmLote(Inicios(Corte, (7, 1m), (7, 2m)), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(Terminos(Corte, (7, 2, 1m), (7, 2, 1m)), Operador, Ct);

    AfirmarFalha(inicios, CodigosDaExecucao.ItemRepetido, TipoDeErro.Validacao);
    Assert.Equal("O nó 7 aparece mais de uma vez no lote.", inicios.Detalhe);
    AfirmarFalha(terminos, CodigosDaExecucao.ItemRepetido, TipoDeErro.Validacao);
    Assert.Equal("O nó 7 no passo 2 aparece mais de uma vez no lote.", terminos.Detalhe);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Quantidade_invalida_em_um_item_da_QuantidadeInvalida_sem_abrir_transacao()
  {
    var c = NaSolda();
    var individual = await c.Apontamento().Iniciar(10, new InicioDto(Solda, 0m), Operador, Ct);

    var inicios = await c.Apontamento().IniciarEmLote(Inicios(Solda, (10, 1m), (1, 0m)), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(Terminos(Solda, (10, 1, 1m), (1, 1, 0m)), Operador, Ct);

    AfirmarFalha(inicios, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    AfirmarFalha(terminos, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Equal(individual.Detalhe, inicios.Detalhe);
    Assert.Equal(individual.Detalhe, terminos.Detalhe);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Recusas_de_entrada_seguem_a_ordem_fixa()
  {
    // Lista vazia -> teto -> quantidade -> repetido -> Setor inexistente: cada par abaixo tem as duas
    // recusas, e a primeira da ordem e a que volta.
    var c = NaSolda();
    var acimaDoTetoComZero = Enumerable.Range(1000, ApontamentoUseCase.TamanhoMaximoDoLote + 1).Select(id => (id, 0m)).ToArray();

    var tetoAntesDaQuantidade = await c.Apontamento().IniciarEmLote(Inicios(Solda, acimaDoTetoComZero), Operador, Ct);
    var quantidadeAntesDoRepetido = await c.Apontamento().IniciarEmLote(Inicios(Solda, (10, 1m), (10, 0m)), Operador, Ct);
    var repetidoAntesDoSetor = await c.Apontamento().IniciarEmLote(Inicios(777, (10, 1m), (10, 1m)), Operador, Ct);

    Assert.Equal(CodigosDaExecucao.LoteGrandeDemais, tetoAntesDaQuantidade.Erro);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, quantidadeAntesDoRepetido.Erro);
    Assert.Equal(CodigosDaExecucao.ItemRepetido, repetidoAntesDoSetor.Erro);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Setor_inexistente_da_404()
  {
    var c = NaSolda();

    var inicios = await c.Apontamento().IniciarEmLote(Inicios(777, (10, 1m)), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(Terminos(777, (10, 1, 1m)), Operador, Ct);

    Assert.Equal(TipoDeErro.NaoEncontrado, inicios.TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, terminos.TipoDoErro);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task No_inexistente_no_lote_da_404_sem_gravar()
  {
    var c = NaSolda();
    var livroAntes = LivroDe(c);

    var r = await c.Apontamento().IniciarEmLote(Inicios(Solda, (10, 1m), (99, 1m)), Operador, Ct);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Null(r.Detalhe);   // sem corpo, como a rota individual
    Assert.Equal(livroAntes, LivroDe(c));
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(1, c.Execucao.Desfeitas);
  }

  [Fact]
  public async Task Pedido_pausado_no_lote_nomeia_o_item()
  {
    // A 20 (outro Pedido) grava antes; o 1 e recusado pela pausa do Pedido dele, e a 20 volta junto.
    var c = new CenarioDeExecucao();
    c.NoDoAgrupamento(AgrupamentoId2, 20, null, 10m, null, Corte);
    c.No(1, null, 10m, null, Corte);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    var r = await c.Apontamento().IniciarEmLote(Inicios(Corte, (20, 1m), (1, 1m)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoPausado, TipoDeErro.Conflito);
    Assert.Equal("No 1: O Pedido PED-01 está pausado.", r.Detalhe);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId2]);
  }

  [Fact]
  public async Task Pedido_fechado_no_lote_nomeia_o_item()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 2m);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var inicios = await c.Apontamento().IniciarEmLote(Inicios(Corte, (1, 1m)), Operador, Ct);
    var terminos = await c.Apontamento().TerminarEmLote(Terminos(Corte, (1, 1, 1m)), Operador, Ct);

    AfirmarFalha(inicios, CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
    Assert.Equal("No 1: O Pedido deste item já foi concluído ou cancelado.", inicios.Detalhe);
    AfirmarFalha(terminos, CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
    Assert.Equal("No 1: O Pedido deste item já foi concluído ou cancelado.", terminos.Detalhe);
  }

  [Fact]
  public async Task Filhos_insuficientes_no_lote_nomeia_o_pai()
  {
    static CenarioDeExecucao Cenario() => NaSolda();   // 6 de 2 e 3 de 3: 4 do pai pedem 8 e 4
    var individual = await Cenario().Apontamento().Iniciar(1, new InicioDto(Solda, 4m), Operador, Ct);

    var r = await Cenario().Apontamento().IniciarEmLote(Inicios(Solda, (1, 4m)), Operador, Ct);

    AfirmarFalha(individual, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.StartsWith("No 1: ", r.Detalhe);
    Assert.Equal("No 1: " + individual.Detalhe, r.Detalhe);
  }

  [Fact]
  public async Task Produto_por_filho_que_nao_cabe_no_lote_nomeia_o_pai()
  {
    static CenarioDeExecucao Cenario()
    {
      var c = new CenarioDeExecucao();
      c.No(1, null, 10m, null, Solda);
      c.No(2, 1, 5m, 0.5m, Corte);
      c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);
      return c;
    }
    var individual = await Cenario().Apontamento().Iniciar(1, new InicioDto(Solda, 0.0001m), Operador, Ct);

    var r = await Cenario().Apontamento().IniciarEmLote(Inicios(Solda, (1, 0.0001m)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Equal("No 1: " + individual.Detalhe, r.Detalhe);
  }

  [Theory]
  [InlineData(CodigosDaExecucao.SemRoteiro)]
  [InlineData(CodigosDaExecucao.NaoEhOPrimeiroPasso)]
  [InlineData(CodigosDaExecucao.SaldoInsuficiente)]
  public async Task Recusa_que_ja_nomeia_o_item_tem_a_frase_da_rota_individual(string codigo)
  {
    CenarioDeExecucao Cenario()
    {
      var c = new CenarioDeExecucao();
      if (codigo == CodigosDaExecucao.SemRoteiro) c.No(1, null, 10m, null);
      else c.No(1, null, 10m, null, Corte, Dobra);
      return c;
    }
    var (setor, quantidade) = codigo switch
    {
      CodigosDaExecucao.NaoEhOPrimeiroPasso => (Dobra, 1m),
      CodigosDaExecucao.SaldoInsuficiente => (Corte, 11m),
      _ => (Corte, 1m),
    };
    var individual = await Cenario().Apontamento().Iniciar(1, new InicioDto(setor, quantidade), Operador, Ct);

    var r = await Cenario().Apontamento().IniciarEmLote(Inicios(setor, (1, quantidade)), Operador, Ct);

    AfirmarFalha(individual, codigo, TipoDeErro.Conflito);
    AfirmarFalha(r, codigo, TipoDeErro.Conflito);
    Assert.Equal(individual.Detalhe, r.Detalhe);
  }

  [Fact]
  public async Task Saldo_insuficiente_no_termino_em_lote_tem_a_frase_da_rota_individual()
  {
    static CenarioDeExecucao Cenario()
    {
      var c = new CenarioDeExecucao();
      c.No(1, null, 10m, null, Corte);
      c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 2m);
      return c;
    }
    var individual = await Cenario().Apontamento().Terminar(1, new TerminoDto(Corte, 1, 3m), Operador, Ct);

    var r = await Cenario().Apontamento().TerminarEmLote(Terminos(Corte, (1, 1, 3m)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Equal(individual.Detalhe, r.Detalhe);
  }

  [Fact]
  public async Task Conflito_na_transacao_do_lote_vira_ConflitoDeConcorrencia()
  {
    var c = NaSolda();
    c.Execucao.ConflitoNaProximaTransacao = true;

    var r = await c.Apontamento().IniciarEmLote(Inicios(Solda, (10, 1m)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.ConflitoDeConcorrencia, TipoDeErro.Conflito);
    Assert.Equal(CodigosDaExecucao.MensagemDeConflito, r.Detalhe);
  }

  // ------------------------------------------------------------------ travas

  [Fact]
  public async Task Lote_trava_todos_os_nos_e_os_Pedidos_antes_do_primeiro_item()
  {
    var c = NaSolda();
    c.NoDoAgrupamento(AgrupamentoId2, 20, null, 10m, null, Solda);

    var r = await c.Apontamento().IniciarEmLote(Inicios(Solda, (20, 1m), (1, 1m), (10, 1m)), Operador, Ct);

    Assert.True(r.Sucesso);
    var eventos = c.Execucao.Eventos;
    Assert.StartsWith("nos:", eventos[0]);
    Assert.Equal(new[] { 1, 2, 3, 10, 20 }, IdsDo(eventos[0]).Order().ToArray());
    Assert.StartsWith("pedidos:", eventos[1]);
    Assert.Equal(new[] { 1, 10, 20 }, IdsDo(eventos[1]).Order().ToArray());
    Assert.Single(eventos, e => e.StartsWith("pedidos:"));
    Assert.True(eventos.IndexOf("save") > 1);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId2]);
  }

  [Fact]
  public async Task Terminar_em_lote_nao_trava_Pedido()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.No(4, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 2m);
    c.Mover(4, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 2m);

    var r = await c.Apontamento().TerminarEmLote(Terminos(Corte, (4, 1, 1m), (1, 1, 1m)), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.DoesNotContain(c.Execucao.Eventos, e => e.StartsWith("pedidos:"));
    Assert.Empty(c.Execucao.TravasDePedido);
    Assert.Equal(new[] { 1, 4 }, IdsDo(c.Execucao.Eventos[0]).Order().ToArray());
    Assert.True(c.Execucao.Eventos.IndexOf("save") > 0);
  }

  private static int[] IdsDo(string evento) =>
      evento[(evento.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
