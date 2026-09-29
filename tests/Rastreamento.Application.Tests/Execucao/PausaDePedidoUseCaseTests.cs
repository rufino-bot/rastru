using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Pausar e retomar (spec da Fase 3D, secao 4.5): um teste por codigo de erro.</summary>
public class PausaDePedidoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  [Fact]
  public async Task Pausar_grava_a_pausa_aberta_com_o_motivo_aparado()
  {
    var c = new CenarioDeExecucao();

    var r = await c.Pausa().Pausar(PedidoId, new NovaPausaDto("  PED-9 urgente "), Pcp, Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(("PED-9 urgente", "PCP", (DateTime?)null), (r.Valor!.Motivo, r.Valor.PausadoPorNome, r.Valor.RetomadoEm));
    var gravada = Assert.Single(c.Execucao.Pausas);
    Assert.Equal((PedidoId, Pcp), (gravada.PedidoId, gravada.PausadoPorUsuarioId));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("   ")]
  public async Task Motivo_ausente_ou_em_branco_grava_nulo(string? motivo)
  {
    var c = new CenarioDeExecucao();

    Assert.Null((await c.Pausa().Pausar(PedidoId, new NovaPausaDto(motivo), Pcp, Ct)).Valor!.Motivo);
  }

  [Fact]
  public async Task Motivo_com_mais_de_200_caracteres_da_MotivoLongoDemais_sem_abrir_transacao()
  {
    var c = new CenarioDeExecucao();

    var r = await c.Pausa().Pausar(PedidoId, new NovaPausaDto(new string('a', 201)), Pcp, Ct);

    AfirmarFalha(r, CodigosDaExecucao.MotivoLongoDemais, TipoDeErro.Validacao);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Pausar_o_que_ja_esta_pausado_da_PedidoJaPausado()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct), CodigosDaExecucao.PedidoJaPausado, TipoDeErro.Conflito);
    Assert.Single(c.Execucao.Pausas);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Pausar_Pedido_fechado_da_PedidoFechado(string status)
  {
    var c = new CenarioDeExecucao();
    c.Execucao.StatusDoPedido[PedidoId] = status;

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct), CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Pedido_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Pausa().Pausar(77, new NovaPausaDto(null), Pcp, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Pausa().Retomar(77, Pcp, Ct)).TipoDoErro);
  }

  [Fact]
  public async Task Retomar_fecha_o_intervalo_com_quem_e_quando()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);

    var r = await c.Pausa().Retomar(PedidoId, Operador, Ct);

    Assert.True(r.Sucesso);
    Assert.NotNull(r.Valor!.RetomadoEm);
    Assert.Equal((Operador, "Operador do Corte"), (r.Valor.RetomadoPorUsuarioId, r.Valor.RetomadoPorNome));
    Assert.NotNull(Assert.Single(c.Execucao.Pausas).RetomadoEm);
  }

  [Fact]
  public async Task Retomar_sem_pausa_aberta_da_PedidoNaoPausado()
  {
    var c = new CenarioDeExecucao();

    AfirmarFalha(await c.Pausa().Retomar(PedidoId, Pcp, Ct), CodigosDaExecucao.PedidoNaoPausado, TipoDeErro.Conflito);
  }

  [Fact]
  public async Task Retomar_Pedido_que_fechou_com_a_pausa_aberta_so_fecha_o_intervalo()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var r = await c.Pausa().Retomar(PedidoId, Pcp, Ct);

    Assert.True(r.Sucesso);
    Assert.NotNull(Assert.Single(c.Execucao.Pausas).RetomadoEm);
  }

  [Fact]
  public async Task Pausar_de_novo_depois_de_retomar_abre_outro_intervalo()
  {
    var c = new CenarioDeExecucao();
    await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct);
    await c.Pausa().Retomar(PedidoId, Pcp, Ct);

    Assert.True((await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct)).Sucesso);
    Assert.Equal(2, c.Execucao.Pausas.Count);
  }

  [Fact]
  public async Task Conflito_na_transacao_vira_ConflitoDeConcorrencia()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.ConflitoNaProximaTransacao = true;

    AfirmarFalha(await c.Pausa().Pausar(PedidoId, new NovaPausaDto(null), Pcp, Ct),
        CodigosDaExecucao.ConflitoDeConcorrencia, TipoDeErro.Conflito);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
