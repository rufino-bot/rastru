using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Estornar (spec da Fase 3, secao 4.5): o inverso que aponta o original, uma vez so, so enquanto a quantidade nao andou.</summary>
public class EstornoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static async Task<(CenarioDeExecucao C, int Inicio)> IniciadoAsync(decimal quantidade = 5m)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);
    var inicio = await c.Apontamento().Iniciar(1, new InicioDto(Corte, quantidade), Operador, Ct);
    return (c, inicio.Valor!.Id);
  }

  [Fact]
  public async Task Estornar_um_Inicio_devolve_a_iniciar_e_o_Pedido_continua_em_producao()
  {
    var (c, inicio) = await IniciadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, podeEstornarDeOutros: false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(TiposDeMovimentacao.Estorno, r.Valor!.Tipo);
    Assert.Equal(inicio, r.Valor.EstornoDeId);
    Assert.Equal(new LocalDto(Posicoes.NoSetor, Corte, "Corte", 1), r.Valor.Origem);
    Assert.Equal(new LocalDto(Posicoes.AIniciar, null, null, null), r.Valor.Destino);
    Assert.Equal(10m, c.Calcular().Saldo(1, Local.AIniciar));
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);   // regra 28: nao volta
  }

  [Fact]
  public async Task Estorno_alheio_sem_ser_PCP_da_Proibido()
  {
    var (c, inicio) = await IniciadoAsync();
    var transacoesAntes = c.Execucao.Transacoes;

    var r = await c.Estorno().EstornarMovimentacao(inicio, Movimentador, podeEstornarDeOutros: false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
    Assert.Equal(transacoesAntes, c.Execucao.Transacoes);   // o 403 sai antes de abrir transacao
  }

  [Fact]
  public async Task PCP_estorna_registro_alheio()
  {
    var (c, inicio) = await IniciadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(inicio, Pcp, podeEstornarDeOutros: true, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(Pcp, r.Valor!.UsuarioId);
  }

  [Fact]
  public async Task Estornar_depois_de_a_quantidade_andar_da_EstornoImpossivel()
  {
    var (c, inicio) = await IniciadoAsync();
    await c.Apontamento().Terminar(1, new TerminoDto(Corte, 1, 5m), Operador, Ct);

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains("já andou", r.Detalhe);
  }

  [Fact]
  public async Task Estornar_um_estorno_da_EstornoImpossivel()
  {
    var (c, inicio) = await IniciadoAsync();
    var estorno = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    var r = await c.Estorno().EstornarMovimentacao(estorno.Valor!.Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Estornar_duas_vezes_da_JaEstornado()
  {
    var (c, inicio) = await IniciadoAsync();
    await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    var r = await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Estorno_em_Pedido_fechado_da_PedidoFechado()
  {
    var (c, inicio) = await IniciadoAsync();
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    AfirmarFalha(await c.Estorno().EstornarMovimentacao(inicio, Operador, false, Ct),
        CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Registro_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Estorno().EstornarMovimentacao(999, Operador, true, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Estorno().EstornarMontagem(999, Operador, true, Ct)).TipoDoErro);
  }

  // ------------------------------------------------------------------ montagem

  private static async Task<(CenarioDeExecucao C, MontagemDto Montagem)> MontadoAsync()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 20m, 2m, Corte);
    c.No(3, 1, 10m, 1m, Corte);
    c.Mover(2, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 3m);
    var montagem = await c.Apontamento().Montar(1, new MontagemNovaDto(Solda, 3m), Operador, Ct);
    return (c, montagem.Valor!);
  }

  [Fact]
  public async Task Estornar_montagem_estorna_cada_baixa_e_marca_a_montagem()
  {
    var (c, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(montagem.Baixas.Select(b => b.Id).ToArray(), r.Valor!.Select(e => e.EstornoDeId!.Value).ToArray());
    Assert.All(r.Valor!, e =>
    {
      Assert.Equal(TiposDeMovimentacao.Estorno, e.Tipo);
      Assert.Equal(montagem.Id, e.MontagemId);
      Assert.Equal(Posicoes.Montado, e.Origem.Posicao);
      Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), e.Destino);
    });
    var gravada = c.Execucao.Montagens.Single(g => g.Id == montagem.Id);
    Assert.NotNull(gravada.EstornadaEm);
    Assert.Equal(Operador, gravada.EstornadaPorUsuarioId);
    var estado = c.Calcular();
    Assert.Equal(0m, estado.TotalMontado(1));
    Assert.Equal(6m, estado.AguardandoMontagem(2, Solda));
    Assert.Equal(3m, estado.AguardandoMontagem(3, Solda));
  }

  [Fact]
  public async Task Estornar_baixa_de_montagem_avulsa_da_EstornoImpossivel_apontando_a_montagem()
  {
    var (c, montagem) = await MontadoAsync();

    var r = await c.Estorno().EstornarMovimentacao(montagem.Baixas[0].Id, Operador, false, Ct);

    AfirmarFalha(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains($"montagem {montagem.Id}", r.Detalhe);
  }

  [Fact]
  public async Task Montagem_estornada_duas_vezes_da_JaEstornado()
  {
    var (c, montagem) = await MontadoAsync();
    await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct);

    AfirmarFalha(await c.Estorno().EstornarMontagem(montagem.Id, Operador, false, Ct),
        CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Montagem_alheia_sem_ser_PCP_da_Proibido()
  {
    var (c, montagem) = await MontadoAsync();

    AfirmarFalha(await c.Estorno().EstornarMontagem(montagem.Id, Movimentador, false, Ct),
        CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
