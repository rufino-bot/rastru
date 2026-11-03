using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Entregar (spec da Fase 3, secao 4.3). Cenario: Peca 1 de 10 com Roteiro Solda -> Pintura; Item 2 de 20
/// (razao 2) com Corte -> Dobra; Item 3 de 10 (razao 1) com Corte so.
/// </summary>
public class EntregaUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(2, 1, 20m, 2m, Corte, Dobra);
    c.No(3, 1, 10m, 1m, Corte);
    return c;
  }

  private static OrigemDaEntregaDto Coleta(int setor, int ordem) => new(Posicoes.AguardandoColeta, setor, ordem);

  private static OrigemDaEntregaDto Montagem(int setor) => new(Posicoes.AguardandoMontagem, setor, null);

  private static EntregaDto Lista(params ItemDaEntregaDto[] itens) => new(itens);

  [Fact]
  public async Task Com_passo_seguinte_vai_para_o_proximo_passo_sem_escolha()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
    var mov = Assert.Single(r.Valor!);
    Assert.Equal(TiposDeMovimentacao.Entrega, mov.Tipo);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Dobra, "Dobra", 2), mov.Destino);
    Assert.Equal(Movimentador, mov.UsuarioId);
  }

  [Fact]
  public async Task Destino_mandado_quando_ele_e_calculado_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), Solda, 8m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Item_no_ultimo_passo_vai_aguardar_no_primeiro_passo_do_pai_sem_escolha()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Item_no_ultimo_passo_com_destino_mandado_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    // Ate o Setor certo e recusado: o destino da montagem nao se escolhe.
    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  /// <summary>
  /// Em `ParaAMontagem` a checagem de "pai sem Roteiro" vem ANTES da de destino mandado: e a recusa
  /// que diz o que fazer (o PCP define o Roteiro), entao o pedido com `destinoSetorId` preenchido cai
  /// em `PaiSemRoteiro`, nao em `DestinoIndevido` — e o que a primeira chamada abaixo afirma. A segunda,
  /// sem destino (o pedido natural, desde que o destino nao se escolhe), chega a `PaiSemRoteiro` porque
  /// nao ha primeiro passo do pai; ali a ordem das checagens nao importa, e ela fica como o caso comum.
  /// </summary>
  [Fact]
  public async Task Pai_sem_Roteiro_da_PaiSemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);
    AfirmarFalha(r, CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito);

    var r2 = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);
    AfirmarFalha(r2, CodigosDaExecucao.PaiSemRoteiro, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Peca_no_fim_do_Roteiro_vai_para_o_local_de_expedicao()
  {
    var c = Cenario();
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Pintura, 2), Local.AguardandoColeta(Pintura, 2), 4m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(1, Coleta(Pintura, 2), null, 4m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.NaExpedicao, null, null, null), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Redirecionar_leva_ao_primeiro_passo_do_pai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Pintura), null, 6m)), Movimentador, Ct);

    var mov = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Pintura, "Pintura", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), mov.Destino);
  }

  [Fact]
  public async Task Redirecionar_o_que_ja_esta_no_primeiro_passo_do_pai_da_RedirecionamentoSemEfeito()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), null, 6m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.RedirecionamentoSemEfeito, TipoDeErro.Conflito);
    Assert.Contains("Solda", r.Detalhe);
  }

  [Fact]
  public async Task Redirecionar_com_destino_mandado_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Pintura), Solda, 6m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Lista_vazia_ou_ausente_da_EntregaVazia()
  {
    var c = Cenario();

    AfirmarFalha(await c.Entrega().Entregar(new EntregaDto([]), Movimentador, Ct),
        CodigosDaExecucao.EntregaVazia, TipoDeErro.Validacao);
    AfirmarFalha(await c.Entrega().Entregar(new EntregaDto(null), Movimentador, Ct),
        CodigosDaExecucao.EntregaVazia, TipoDeErro.Validacao);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00001")]
  [InlineData("1.00005")]
  public async Task Quantidade_fora_da_faixa_da_QuantidadeInvalida(string quantidade)
  {
    var c = Cenario();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, decimal.Parse(quantidade, CultureInfo.InvariantCulture))),
        Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);   // recusado antes de abrir transacao
  }

  [Theory]
  [InlineData(Posicoes.NoSetor, Corte, 1)]
  [InlineData(Posicoes.AguardandoColeta, Corte, null)]
  [InlineData(Posicoes.AguardandoMontagem, Solda, 1)]
  [InlineData(null, Corte, 1)]
  public async Task Origem_incoerente_da_OrigemInvalida(string? posicao, int setor, int? ordem)
  {
    var c = Cenario();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(2, new OrigemDaEntregaDto(posicao, setor, ordem), null, 1m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Mesmo_no_duas_vezes_na_lista_soma_contra_o_saldo()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    var r = await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 5m),
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 5m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 3", r.Detalhe);
    Assert.Single(c.Execucao.Movimentacoes);   // so o arranjo
  }

  [Fact]
  public async Task Lista_e_tudo_ou_nada()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m),
        new ItemDaEntregaDto(3, Coleta(Corte, 1), Dobra, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
    Assert.Equal(2, c.Execucao.Movimentacoes.Count);   // so os dois do arranjo
    Assert.Equal(0, c.Execucao.Saves);
  }

  [Fact]
  public async Task Nao_ha_teto_de_sobra_na_entrega()
  {
    // O pai ja tem 8 de 10 montadas: precisa de 2 do Item 3, e o Movimentador leva os 10 assim mesmo.
    var c = Cenario();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 8m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Trava_todos_os_nos_da_lista_de_uma_vez()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m),
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.Equal(new[] { 2, 3 }, Assert.Single(c.Execucao.Travas).Order().ToArray());
  }

  [Fact]
  public async Task Entregar_em_Pedido_pausado_continua_valendo()
  {
    var c = Cenario();
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task No_inexistente_na_lista_da_404_e_Pedido_fechado_da_PedidoFechado()
  {
    var c = Cenario();
    AfirmarFalha(await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(99, Coleta(Corte, 1), null, 1m)), Movimentador, Ct),
        "NaoEncontrado", TipoDeErro.NaoEncontrado);

    c.Execucao.StatusDoPedido[PedidoId] = "Cancelado";
    AfirmarFalha(await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 1m)), Movimentador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  // ------------------------------------------------------------------ conserto (Fase 5A, secao 5.3)

  private static OrigemDaEntregaDto DoConserto(string posicao, int? ordem = null) => new(posicao, Dobra, ordem);

  /// <summary>Peca 1 de 6, com 2 aguardando o Movimentador para o conserto na Dobra e 4 em `NaExpedicao`.</summary>
  private static CenarioDeExecucao NoConserto()
  {
    var c = new CenarioDeExecucao();
    c.PecaNoConserto(1, 6m, Dobra, 2m);
    return c;
  }

  /// <summary>As 2 unidades ja entregues ao Setor e terminadas: `ConsertoPronto`, esperando a volta.</summary>
  private static CenarioDeExecucao ConsertoPronto()
  {
    var c = NoConserto();
    c.Mover(1, TiposDeMovimentacao.Entrega, Local.AguardandoConserto(Dobra), Local.EmConserto(Dobra), 2m, Movimentador);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.EmConserto(Dobra), Local.ConsertoPronto(Dobra), 2m);
    return c;
  }

  [Fact]
  public async Task Entrega_leva_do_AguardandoConserto_ao_EmConserto_no_mesmo_Setor()
  {
    var c = NoConserto();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.AguardandoConserto), null, 2m)), Movimentador, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    var mov = Assert.Single(r.Valor!);
    Assert.Equal(TiposDeMovimentacao.Entrega, mov.Tipo);
    Assert.Equal(new LocalDto(Posicoes.AguardandoConserto, Dobra, "Dobra", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.EmConserto, Dobra, "Dobra", null), mov.Destino);
    Assert.Equal(Movimentador, mov.UsuarioId);
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(1, Local.AguardandoConserto(Dobra)));
    Assert.Equal(2m, calc.Saldo(1, Local.EmConserto(Dobra)));
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    // So a Peca pode mudar o fechamento: o Pedido dela e travado, depois do no e antes de ler o Pedido.
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.TravasDePedido));
  }

  [Fact]
  public async Task Entrega_traz_do_ConsertoPronto_ao_NaExpedicao()
  {
    var c = ConsertoPronto();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.ConsertoPronto), null, 2m)), Movimentador, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    var mov = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.ConsertoPronto, Dobra, "Dobra", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.NaExpedicao, null, null, null), mov.Destino);
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(1, Local.ConsertoPronto(Dobra)));
    Assert.Equal(6m, calc.Saldo(1, Local.NaExpedicao));   // a unidade voltou: as 4 de antes e as 2 do conserto
    // A volta toca o fim do caminho: o fechamento e recalculado, e com as 6 la o Pedido aguarda a expedicao (C11).
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Theory]
  [InlineData(Posicoes.AguardandoConserto)]
  [InlineData(Posicoes.ConsertoPronto)]
  public async Task Entrega_de_conserto_com_destino_mandado_e_recusada(string posicao)
  {
    var c = ConsertoPronto();
    c.Mover(1, TiposDeMovimentacao.Conserto, Local.NaoConforme, Local.AguardandoConserto(Dobra), 0.5m, Qualidade);

    // Ate o Setor certo e recusado: o destino do conserto nao se escolhe.
    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(1, DoConserto(posicao), Dobra, 0.5m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
    Assert.Contains("Chapa dobrada 3 mm", r.Detalhe);
  }

  [Theory]
  [InlineData(Posicoes.AguardandoConserto)]
  [InlineData(Posicoes.ConsertoPronto)]
  public async Task Entrega_de_conserto_com_ordem_e_recusada(string posicao)
  {
    var c = ConsertoPronto();

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(1, DoConserto(posicao, 1), null, 1m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.OrigemInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);   // recusado antes de abrir transacao
  }

  [Fact]
  public async Task Entrega_de_conserto_acima_do_saldo_nomeia_a_Peca_e_o_Setor()
  {
    var c = NoConserto();

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.AguardandoConserto), null, 3m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Equal("Só há 2 de Chapa dobrada 3 mm aguardando conserto no Setor Dobra.", r.Detalhe);
    Assert.Equal(0, c.Execucao.Commits);
  }

  [Fact]
  public async Task Entrega_de_conserto_em_Pedido_concluido_da_PedidoFechado()
  {
    var c = NoConserto();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var r = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.AguardandoConserto), null, 1m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  /// <summary>
  /// Review Focus 3 do plano de backend da Fase 5A: so MANDAR para Setor inativo e recusado (e na rota do
  /// conserto). Depois de mandado, a entrega, o termino e a volta continuam, com o Setor inativado no meio.
  /// </summary>
  [Fact]
  public async Task Review_Focus_conserto_em_Setor_inativado_continua()
  {
    var c = NoConserto();
    c.Catalogo.Setores.Single(x => x.Id == Dobra).Ativo = false;

    var ida = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.AguardandoConserto), null, 2m)), Movimentador, Ct);
    var termino = await c.Apontamento().TerminarConserto(1, new TerminoDeConsertoDto(Dobra, 2m), Operador, Ct);
    var volta = await c.Entrega().Entregar(
        Lista(new ItemDaEntregaDto(1, DoConserto(Posicoes.ConsertoPronto), null, 2m)), Movimentador, Ct);

    Assert.True(ida.Sucesso, ida.Detalhe);
    Assert.True(termino.Sucesso, termino.Detalhe);
    Assert.True(volta.Sucesso, volta.Detalhe);
    Assert.Equal(6m, c.Calcular().Saldo(1, Local.NaExpedicao));
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
