using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Iniciar, terminar e montar (spec da Fase 3, secoes 4.1, 4.2 e 4.4): um teste por codigo de erro.</summary>
public class ApontamentoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  // ------------------------------------------------------------------ iniciar

  [Fact]
  public async Task Iniciar_leva_de_a_iniciar_para_o_primeiro_passo_e_poe_o_Pedido_em_producao()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 4m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Inicio, r.Valor!.Tipo);
    Assert.Equal(new LocalDto(Posicoes.AIniciar, null, null, null), r.Valor.Origem);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor.Destino);
    Assert.Equal("Operador do Corte", r.Valor.UsuarioNome);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Iniciar_sem_Roteiro_da_SemRoteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SemRoteiro, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Iniciar_fora_do_primeiro_passo_da_NaoEhOPrimeiroPasso()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Dobra, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.NaoEhOPrimeiroPasso, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Iniciar_acima_do_saldo_a_iniciar_da_SaldoInsuficiente_com_os_numeros()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 5m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 4 de No 1", r.Detalhe);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Iniciar_em_Pedido_fechado_da_PedidoFechado(string status)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.StatusDoPedido[PedidoId] = status;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00005")]
  [InlineData("1.12345")]
  public async Task Quantidade_com_mais_de_quatro_casas_e_recusada(string quantidade)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);

    var r = await c.Apontamento().Iniciar(
        1, new InicioDto(Corte, decimal.Parse(quantidade, CultureInfo.InvariantCulture)), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);   // recusado antes de abrir transacao
  }

  [Fact]
  public async Task No_ou_Setor_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);

    Assert.Equal(TipoDeErro.NaoEncontrado,
        (await c.Apontamento().Iniciar(99, new InicioDto(Corte, 1m), Operador, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado,
        (await c.Apontamento().Iniciar(1, new InicioDto(77, 1m), Operador, Ct)).TipoDoErro);
  }

  [Fact]
  public async Task Conflito_na_transacao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    c.Execucao.ConflitoNaProximaTransacao = true;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.ConflitoDeConcorrencia, TipoDeErro.Conflito);
    Assert.Equal(CodigosDaExecucao.MensagemDeConflito, r.Detalhe);
  }

  // ------------------------------------------------------------------ terminar

  [Fact]
  public async Task Terminar_leva_para_aguardando_coleta_no_mesmo_passo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var r = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 4m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor!.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoColeta, Corte, "Corte", 1), r.Valor.Destino);
  }

  [Fact]
  public async Task Terminar_no_Corte_do_passo_um_nao_e_o_ultimo_passo()
  {
    // Regra 21: Corte -> Dobra -> Corte. O que esta no Corte e do passo 1; terminar "o passo 3" nao
    // acha nada, porque o saldo e por passo, nao por Setor.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);

    var errado = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 3, 1m), Operador, Ct);
    var certo = await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 6m), Operador, Ct);

    AfirmarFalha(errado, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("(passo 3)", errado.Detalhe);
    Assert.Equal(1, certo.Valor!.Destino.Ordem);
  }

  [Fact]
  public async Task Terminar_num_Setor_inativado_continua_valendo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Inativo);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Inativo, 1), 3m);

    var r = await c.Apontamento().Terminar(1, new TerminoDto(Inativo, 1, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
  }

  // ------------------------------------------------------------------ montar

  private static CenarioDeExecucao PaiComDoisFilhos(decimal quantidadeDoPai = 10m)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, quantidadeDoPai, null, Solda);
    c.No(2, 1, 2m * quantidadeDoPai, 2m, Corte);
    c.No(3, 1, quantidadeDoPai, 1m, Corte);
    return c;
  }

  [Fact]
  public async Task Montar_grava_a_montagem_e_baixa_N_vezes_a_razao_de_cada_filho()
  {
    var c = PaiComDoisFilhos();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3m, r.Valor!.Quantidade);
    Assert.Equal("Solda", r.Valor.SetorNome);
    Assert.Equal(new[] { (2, 6m), (3, 3m) }, r.Valor.Baixas.Select(b => (b.EstruturaItemId, b.Quantidade)).ToArray());
    Assert.All(r.Valor.Baixas, b =>
    {
      Assert.Equal(TiposDeMovimentacao.Montagem, b.Tipo);
      Assert.Equal(r.Valor.Id, b.MontagemId);
      Assert.Equal(Posicoes.Montado, b.Destino.Posicao);
    });
    Assert.Equal(new[] { new[] { 1 }, new[] { 2, 3 } }, c.Execucao.Travas.Select(t => t.ToArray()).ToArray());
  }

  [Fact]
  public async Task Montar_no_sem_filhos_da_SemFilhos()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);

    AfirmarFalha(await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.SemFilhos, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Montar_acima_do_que_falta_da_MontagemAcimaDoQueFalta()
  {
    var c = PaiComDoisFilhos(quantidadeDoPai: 2m);
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 1m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 2m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.MontagemAcimaDoQueFalta, TipoDeErro.Conflito);
    Assert.Contains("Falta montar 1 de No 1", r.Detalhe);
  }

  [Fact]
  public async Task Montar_com_filho_insuficiente_nomeia_o_filho_e_nao_grava_nada()
  {
    var c = PaiComDoisFilhos();
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 1m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Equal("No 3: 1 aqui, 3 necessários.", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Montar_recusa_quando_N_vezes_a_razao_passa_de_quatro_casas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 5m, 0.5m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 0.0001m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Contains("No 2", r.Detalhe);
  }

  [Fact]
  public async Task Montar_nao_exige_que_o_Setor_seja_do_Roteiro_do_pai()
  {
    // Spec secao 4.4: sem identidade de sub-lote, montar nao confere onde o pai esta.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Pintura);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 2m);

    var r = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 2m), Operador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Montar_em_Pedido_fechado_da_PedidoFechado()
  {
    var c = PaiComDoisFilhos();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
