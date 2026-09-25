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
  public async Task Item_no_ultimo_passo_vai_aguardar_montagem_no_Setor_escolhido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), Assert.Single(r.Valor!).Destino);
  }

  [Fact]
  public async Task Item_no_ultimo_passo_sem_destino_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), null, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoIndevido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Setor_fora_do_Roteiro_do_pai_da_DestinoForaDoRoteiroDoPai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Dobra, 10m)), Movimentador, Ct);

    AfirmarFalha(r, CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito);
    Assert.Contains("Dobra", r.Detalhe);
  }

  /// <summary>
  /// Ruling R12 (Task 6): a checagem de "pai sem Roteiro" vem ANTES da checagem de destino nulo em
  /// `ParaAMontagem`. Com D7 a tela mostra um Item pronto cujo pai nao tem Roteiro sem nenhum Setor
  /// para escolher, entao o pedido natural chega sem `destinoSetorId` e tem de cair aqui tambem, nao em
  /// `DestinoIndevido`.
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
  public async Task Redirecionar_o_que_aguarda_montagem_para_outro_Setor_do_pai()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), Pintura, 6m)), Movimentador, Ct);

    var mov = Assert.Single(r.Valor!);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Solda, "Solda", null), mov.Origem);
    Assert.Equal(new LocalDto(Posicoes.AguardandoMontagem, Pintura, "Pintura", null), mov.Destino);
  }

  [Fact]
  public async Task Redirecionar_para_o_mesmo_Setor_da_DestinoIndevido()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m);

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Montagem(Solda), Solda, 6m)), Movimentador, Ct);

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

    AfirmarFalha(r, CodigosDaExecucao.DestinoForaDoRoteiroDoPai, TipoDeErro.Conflito);
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

    var r = await c.Entrega().Entregar(Lista(new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m)), Movimentador, Ct);

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Trava_todos_os_nos_da_lista_de_uma_vez()
  {
    var c = Cenario();
    c.Mover(3, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 10m);
    c.Mover(2, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m);

    await c.Entrega().Entregar(Lista(
        new ItemDaEntregaDto(3, Coleta(Corte, 1), Solda, 10m),
        new ItemDaEntregaDto(2, Coleta(Corte, 1), null, 8m)), Movimentador, Ct);

    Assert.Equal(new[] { 2, 3 }, Assert.Single(c.Execucao.Travas).Order().ToArray());
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

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
