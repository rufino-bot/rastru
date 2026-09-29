using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// Iniciar (regra 28, inclusive o no com filhos que consome os filhos — regra 24) e terminar (regra
/// 22): um teste por codigo de erro.
/// </summary>
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

  [Fact]
  public async Task Iniciar_em_Pedido_pausado_da_PedidoPausado_e_nao_grava_nada()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto("urgente"), Pcp, Ct);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Corte, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoPausado, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Equal("O Pedido PED-01 está pausado.", r.Detalhe);
  }

  [Fact]
  public async Task Iniciar_um_pai_em_Pedido_pausado_tambem_e_recusado()
  {
    var c = ComFilhosNaSolda();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.PedidoPausado, TipoDeErro.Conflito);
    Assert.Equal("O Pedido PED-01 está pausado.", r.Detalhe);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Terminar_em_Pedido_pausado_continua_valendo()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 3m);
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    Assert.True((await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 3m), Operador, Ct)).Sucesso);
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

  // ------------------------------------------------------------------ iniciar um no com filhos

  private static CenarioDeExecucao PaiComDoisFilhos(decimal quantidadeDoPai = 10m)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, quantidadeDoPai, null, Solda);
    c.No(2, 1, 2m * quantidadeDoPai, 2m, Corte);
    c.No(3, 1, quantidadeDoPai, 1m, Corte);
    return c;
  }

  /// <summary>Filhos 2 (razao 2) e 3 (razao 1) entregues para a montagem de 1 na Solda.</summary>
  private static CenarioDeExecucao ComFilhosNaSolda(decimal presentes2 = 6m, decimal presentes3 = 3m, decimal quantidadeDoPai = 10m)
  {
    var c = PaiComDoisFilhos(quantidadeDoPai);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), presentes2);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), presentes3);
    return c;
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_consome_os_filhos_e_poe_o_pai_no_primeiro_passo()
  {
    var c = ComFilhosNaSolda();

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Inicio, r.Valor!.Tipo);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Solda, "Solda", 1), r.Valor.Destino);
    var montagem = Assert.Single(c.Execucao.Montagens);
    Assert.Equal((1, Solda, 3m), (montagem.EstruturaItemId, montagem.SetorId, montagem.Quantidade));
    Assert.Equal(montagem.Id, r.Valor.MontagemId);
    var baixas = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Montagem).ToList();
    Assert.Equal(new[] { (2, 6m), (3, 3m) }, baixas.Select(b => (b.EstruturaItemId, b.Quantidade)).ToArray());
    Assert.All(baixas, b => Assert.Equal(montagem.Id, b.MontagemId));
    var estado = c.Calcular();
    Assert.Equal(7m, estado.Saldo(1, Local.AIniciar));
    Assert.Equal(3m, estado.Saldo(1, Local.NoSetor(Solda, 1)));
    Assert.Equal(3m, estado.TotalMontado(1));
    Assert.Equal(estado.TotalMontado(1), estado.SaidoDeAIniciar(1));   // a invariante nova
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(new[] { new[] { 1 }, new[] { 2, 3 } }, c.Execucao.Travas.Select(t => t.ToArray()).ToArray());
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_sem_nenhum_presente_da_FilhosInsuficientes_e_nao_grava_nada()
  {
    var c = PaiComDoisFilhos();

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Empty(c.Execucao.Movimentacoes);
    Assert.Empty(c.Execucao.Montagens);
    Assert.Equal("Aberto", c.Execucao.StatusDoPedido[PedidoId]);
  }

  [Fact]
  public async Task Iniciar_no_com_filho_insuficiente_nomeia_o_filho_e_nao_grava_nada()
  {
    var c = ComFilhosNaSolda(presentes2: 6m, presentes3: 1m);
    var antes = c.Execucao.Movimentacoes.Count;

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 3m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.FilhosInsuficientes, TipoDeErro.Conflito);
    Assert.Equal("No 3: 1 aqui, 3 necessários.", r.Detalhe);
    Assert.Equal(antes, c.Execucao.Movimentacoes.Count);
    Assert.Empty(c.Execucao.Montagens);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_acima_do_a_iniciar_da_SaldoInsuficiente()
  {
    // O pai de 2 ja iniciou 1 (com os filhos); os filhos presentes dariam para mais 2, mas so resta 1.
    var c = ComFilhosNaSolda(presentes2: 6m, presentes3: 3m, quantidadeDoPai: 2m);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Solda, 1), 1m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 2m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.SaldoInsuficiente, TipoDeErro.Conflito);
    Assert.Contains("Só há 1 de No 1 a iniciar", r.Detalhe);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_fora_do_primeiro_passo_do_pai_da_NaoEhOPrimeiroPasso()
  {
    // Os filhos foram deixados na Pintura, que e o SEGUNDO passo do pai: la o pai nao comeca.
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda, Pintura);
    c.No(2, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Pintura), 5m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Pintura, 1m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.NaoEhOPrimeiroPasso, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_recusa_quando_N_vezes_a_razao_passa_de_quatro_casas()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 5m, 0.5m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 5m);

    var r = await c.Apontamento().Iniciar(1, new InicioDto(Solda, 0.0001m), Operador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.QuantidadeInvalida, TipoDeErro.Validacao);
    Assert.Contains("No 2", r.Detalhe);
    // pt-BR e SEM arredondar para quatro casas: e a quinta casa que faz o valor nao caber na coluna.
    Assert.Contains("0,00005", r.Detalhe);
  }

  [Fact]
  public async Task Iniciar_no_com_filhos_em_Pedido_fechado_da_PedidoFechado()
  {
    var c = ComFilhosNaSolda();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Apontamento().Iniciar(1, new InicioDto(Solda, 1m), Operador, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
