using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// Os estornos da afericao, da contagem e da liberacao (pedido ou resposta): C7, C17 e as secoes 3.4 e 3.6 da spec
/// da Fase 5A. Cenario: Pedido `EmProducao`, Peca de 6 que exige relatorio, COPIADA DO CATALOGO (sem descricao
/// propria: o nome nas frases e o do Componente, regra 19), levada inteira ao local de expedicao por arranjo direto
/// no livro; o que cada teste precisa alem disso (afericao, contagem, pedido) ele faz pelos casos de uso.
/// </summary>
public class EstornoDaExpedicaoUseCaseTests
{
  private const int Peca = 1;
  private const string NomeDaPeca = "Chapa dobrada 3 mm";
  private const string Justificativa = "Aba da dobra 1,5 mm fora; o cliente não usa.";
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario(string status = "EmProducao")
  {
    var c = new CenarioDeExecucao();
    c.No(Peca, null, 6m, null, Corte);
    var peca = c.Estruturas.Itens.Single(i => i.Id == Peca);
    peca.Descricao = null;
    peca.ComponenteId = 70;
    peca.RequerRelatorioDimensional = true;
    c.Catalogo.Componentes.Add(
        new Componente { Id = 70, Codigo = "CHP-220", Descricao = NomeDaPeca, Tipo = "Fabricado", Ativo = true });
    c.Execucao.StatusDoPedido[PedidoId] = status;
    c.Mover(Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, 6m, Movimentador);
    return c;
  }

  private static async Task<int> AferirAsync(CenarioDeExecucao c, string resultado)
  {
    var r = await c.Afericao().Aferir(Peca, new NovaAfericaoDto(resultado), Qualidade, Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return r.Valor!.Id;
  }

  /// <summary>Afericao nao conforme e contagem de 4 conformes e 2 nao; devolve o Id da contagem.</summary>
  private static async Task<(CenarioDeExecucao C, int AfericaoId, int ContagemId)> ComContagemAsync()
  {
    var c = Cenario();
    var afericaoId = await AferirAsync(c, ResultadosDeAfericao.NaoConforme);
    var contagem = await c.Afericao().Contar(Peca, new NovaContagemDto(4m, 2m), Qualidade, Ct);
    Assert.True(contagem.Sucesso, contagem.Detalhe);
    return (c, afericaoId, contagem.Valor!.Id);
  }

  /// <summary>Com a contagem feita e um pedido de `quantidade` (das 2 nao conformes) da Qualidade; devolve o Id do pedido.</summary>
  private static async Task<(CenarioDeExecucao C, int PedidoDeLiberacaoId)> ComPedidoAsync(decimal quantidade = 2m)
  {
    var (c, _, _) = await ComContagemAsync();
    var r = await c.Liberacao().Pedir(Peca, new NovoPedidoDeLiberacaoDto(quantidade, Justificativa), Qualidade, Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return (c, r.Valor!.Id);
  }

  private static async Task<int> ResponderAsync(CenarioDeExecucao c, int pedidoId, decimal liberada)
  {
    var r = await c.Liberacao().Responder(pedidoId, new RespostaDeLiberacaoDto(liberada, null), Gestao, Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return r.Valor!.Id;
  }

  private static void AssertRecusa<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- afericao

  [Fact]
  public async Task Estornar_afericao_conforme_libera_uma_nova()
  {
    var c = Cenario();
    var afericaoId = await AferirAsync(c, ResultadosDeAfericao.Conforme);

    var r = await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    var dto = r.Valor!;
    Assert.Equal((afericaoId, ResultadosDeAfericao.Conforme, true, false), (dto.Id, dto.Resultado, dto.Estornada, dto.Estornavel));
    var estorno = Assert.Single(c.Expedicao.Afericoes, a => a.Tipo == TiposDeAfericao.Estorno);
    Assert.Equal((afericaoId, Peca, Qualidade, (string?)null), (estorno.EstornoDeId, estorno.EstruturaItemId, estorno.UsuarioId, estorno.Resultado));
    // Sem aferição válida de novo: registra-se outra.
    var nova = await c.Afericao().Aferir(Peca, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct);
    Assert.True(nova.Sucesso, nova.Detalhe);
  }

  [Fact]
  public async Task Estornar_afericao_nao_conforme_com_contagem_valida_e_recusado()
  {
    var (c, afericaoId, _) = await ComContagemAsync();

    var r = await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain(c.Expedicao.Afericoes, a => a.Tipo == TiposDeAfericao.Estorno);
  }

  [Fact]
  public async Task Estornar_afericao_nao_conforme_depois_de_estornar_a_contagem_passa()
  {
    var (c, afericaoId, contagemId) = await ComContagemAsync();
    Assert.True((await c.EstornoDaExpedicao().EstornarContagem(contagemId, Qualidade, false, Ct)).Sucesso);

    var r = await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Single(c.Expedicao.Afericoes, a => a.Tipo == TiposDeAfericao.Estorno);
  }

  [Fact]
  public async Task Estorno_de_estorno_de_afericao_e_recusado()
  {
    var c = Cenario();
    var afericaoId = await AferirAsync(c, ResultadosDeAfericao.Conforme);
    Assert.True((await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct)).Sucesso);
    var estornoId = c.Expedicao.Afericoes.Single(a => a.Tipo == TiposDeAfericao.Estorno).Id;

    var r = await c.EstornoDaExpedicao().EstornarAfericao(estornoId, Qualidade, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Single(c.Expedicao.Afericoes, a => a.Tipo == TiposDeAfericao.Estorno);
  }

  [Fact]
  public async Task Estornar_afericao_duas_vezes_e_recusado()
  {
    var c = Cenario();
    var afericaoId = await AferirAsync(c, ResultadosDeAfericao.Conforme);
    Assert.True((await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct)).Sucesso);

    var segunda = await c.EstornoDaExpedicao().EstornarAfericao(afericaoId, Qualidade, false, Ct);

    AssertRecusa(segunda, CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
    Assert.Single(c.Expedicao.Afericoes, a => a.Tipo == TiposDeAfericao.Estorno);
  }

  // ---------------------------------------------------------------- contagem

  [Fact]
  public async Task Estornar_contagem_devolve_as_duas_linhas_a_NaExpedicao()
  {
    var (c, _, contagemId) = await ComContagemAsync();

    var r = await c.EstornoDaExpedicao().EstornarContagem(contagemId, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    var originais = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Medicao).ToList();
    var estornos = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Estorno).ToList();
    Assert.Equal(2, estornos.Count);
    Assert.Equal(2, r.Valor!.Count);
    Assert.All(estornos, e =>
    {
      Assert.Equal(contagemId, e.ContagemId);   // o mesmo agrupador da original
      Assert.Equal(Posicoes.NaExpedicao, e.DestinoPosicao);
      Assert.Equal(Qualidade, e.UsuarioId);
    });
    Assert.Equal(originais.Select(o => o.Id).Order(), estornos.Select(e => e.EstornoDeId!.Value).Order());
    var calc = c.Calcular();
    Assert.Equal(6m, calc.Saldo(Peca, Local.NaExpedicao));
    Assert.Equal(0m, calc.Saldo(Peca, Local.Conforme));
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Estornar_contagem_cuja_nao_conforme_ja_foi_para_conserto_e_recusado()
  {
    var (c, _, contagemId) = await ComContagemAsync();
    Assert.True((await c.Afericao().MandarParaConserto(Peca, new NovoConsertoDto(Dobra, 1m), Qualidade, Ct)).Sucesso);

    var r = await c.EstornoDaExpedicao().EstornarContagem(contagemId, Qualidade, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);   // nada gravado pela metade
  }

  [Fact]
  public async Task Estornar_contagem_duas_vezes_e_recusado()
  {
    var (c, _, contagemId) = await ComContagemAsync();
    Assert.True((await c.EstornoDaExpedicao().EstornarContagem(contagemId, Qualidade, false, Ct)).Sucesso);

    var segunda = await c.EstornoDaExpedicao().EstornarContagem(contagemId, Qualidade, false, Ct);

    AssertRecusa(segunda, CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
    Assert.Equal(2, c.Execucao.Movimentacoes.Count(m => m.Tipo == TiposDeMovimentacao.Estorno));
  }

  // ---------------------------------------------------------------- liberacao

  [Fact]
  public async Task Estornar_pedido_de_liberacao_devolve_a_NaoConforme()
  {
    var (c, pedidoId) = await ComPedidoAsync();

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(pedidoId, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    var estorno = Assert.Single(r.Valor!);
    Assert.Equal(TiposDeMovimentacao.Estorno, estorno.Tipo);
    var linha = Assert.Single(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
    Assert.Equal((pedidoId, Posicoes.NaoConforme), (linha.LiberacaoId, linha.DestinoPosicao));
    var calc = c.Calcular();
    Assert.Equal(2m, calc.Saldo(Peca, Local.NaoConforme));
    Assert.Equal(0m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    // Já não há pedido em aberto: a Qualidade pede de novo.
    var novo = await c.Liberacao().Pedir(Peca, new NovoPedidoDeLiberacaoDto(1m, Justificativa), Qualidade, Ct);
    Assert.True(novo.Sucesso, novo.Detalhe);
  }

  [Fact]
  public async Task Estornar_pedido_com_resposta_valida_e_recusado()
  {
    var (c, pedidoId) = await ComPedidoAsync();
    await ResponderAsync(c, pedidoId, 1m);

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(pedidoId, Qualidade, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains("estorne a resposta", r.Detalhe);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
  }

  [Fact]
  public async Task Estornar_pedido_cuja_resposta_foi_estornada_passa()
  {
    var (c, pedidoId) = await ComPedidoAsync();
    var respostaId = await ResponderAsync(c, pedidoId, 1m);
    Assert.True((await c.EstornoDaExpedicao().EstornarLiberacao(respostaId, Gestao, false, Ct)).Sucesso);

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(pedidoId, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(2m, c.Calcular().Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Estornar_resposta_devolve_o_pedido_a_pendente()
  {
    var (c, pedidoId) = await ComPedidoAsync();
    var respostaId = await ResponderAsync(c, pedidoId, 1m);   // 1 liberada, 1 recusada

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(respostaId, Gestao, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(2, r.Valor!.Count);
    var estornos = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Estorno).ToList();
    Assert.All(estornos, e => Assert.Equal(respostaId, e.LiberacaoId));   // o agrupador da resposta, nao o do pedido
    var calc = c.Calcular();
    Assert.Equal(2m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    Assert.Equal(0m, calc.Saldo(Peca, Local.Liberada));
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaoConforme));
    // O pedido voltou a pendente: a Gestao responde de novo.
    var outra = await c.Liberacao().Responder(pedidoId, new RespostaDeLiberacaoDto(2m, null), Gestao, Ct);
    Assert.True(outra.Sucesso, outra.Detalhe);
  }

  [Fact]
  public async Task Estornar_resposta_cuja_liberada_ja_saiu_e_recusado()
  {
    var (c, pedidoId) = await ComPedidoAsync();
    var respostaId = await ResponderAsync(c, pedidoId, 1m);
    // A remessa tira primeiro a Liberada (C10).
    var remessa = await c.Remessa().Expedir(
        new NovaRemessaDto([new ItemDaRemessaDto(Peca, 1m)]), Qualidade, Ct);
    Assert.True(remessa.Sucesso, remessa.Detalhe);

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(respostaId, Gestao, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
  }

  [Fact]
  public async Task Estornar_resposta_com_outro_pedido_em_aberto_e_recusado()
  {
    // Estornar a resposta reabre o pedido dela; com outro pedido ja em aberto seriam dois (C18).
    var (c, pedidoId) = await ComPedidoAsync(1m);
    var respostaId = await ResponderAsync(c, pedidoId, 0m);   // recusa total: 1 volta a NaoConforme
    var outro = await c.Liberacao().Pedir(Peca, new NovoPedidoDeLiberacaoDto(1m, Justificativa), Qualidade, Ct);
    Assert.True(outro.Sucesso, outro.Detalhe);

    var r = await c.EstornoDaExpedicao().EstornarLiberacao(respostaId, Gestao, false, Ct);

    AssertRecusa(r, CodigosDaExecucao.EstornoImpossivel, TipoDeErro.Conflito);
    Assert.Contains(NomeDaPeca, r.Detalhe);
  }

  [Fact]
  public async Task Estornar_liberacao_duas_vezes_e_recusado()
  {
    var (c, pedidoId) = await ComPedidoAsync();
    Assert.True((await c.EstornoDaExpedicao().EstornarLiberacao(pedidoId, Qualidade, false, Ct)).Sucesso);

    var segunda = await c.EstornoDaExpedicao().EstornarLiberacao(pedidoId, Qualidade, false, Ct);

    AssertRecusa(segunda, CodigosDaExecucao.JaEstornado, TipoDeErro.Conflito);
    Assert.Single(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
  }

  // ---------------------------------------------------------------- autoria, existencia e Pedido fechado

  [Fact]
  public async Task Estorno_alheio_sem_ser_PCP_e_proibido()
  {
    var (c, _, contagemId) = await ComContagemAsync();
    var afericaoId = c.Expedicao.Afericoes.Single().Id;
    var pedido = await c.Liberacao().Pedir(Peca, new NovoPedidoDeLiberacaoDto(1m, Justificativa), Qualidade, Ct);
    var pedidoId = pedido.Valor!.Id;
    var uc = c.EstornoDaExpedicao();

    // A Gestao passa pelo [Authorize] mas nao e a autora de nenhum dos tres.
    AssertRecusa(await uc.EstornarAfericao(afericaoId, Gestao, false, Ct), CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
    AssertRecusa(await uc.EstornarContagem(contagemId, Gestao, false, Ct), CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
    AssertRecusa(await uc.EstornarLiberacao(pedidoId, Gestao, false, Ct), CodigosDaExecucao.Proibido, TipoDeErro.Proibido);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);

    // O PCP (ou o Administrador) estorna o de outro; a ordem respeita o que depende de que.
    Assert.True((await uc.EstornarLiberacao(pedidoId, Pcp, true, Ct)).Sucesso);
    Assert.True((await uc.EstornarContagem(contagemId, Pcp, true, Ct)).Sucesso);
    Assert.True((await uc.EstornarAfericao(afericaoId, Pcp, true, Ct)).Sucesso);
  }

  [Fact]
  public async Task Estorno_de_registro_inexistente_e_404()
  {
    var c = Cenario();
    var uc = c.EstornoDaExpedicao();

    AssertRecusa(await uc.EstornarAfericao(999_999, Qualidade, false, Ct), "NaoEncontrado", TipoDeErro.NaoEncontrado);
    AssertRecusa(await uc.EstornarContagem(999_999, Qualidade, false, Ct), "NaoEncontrado", TipoDeErro.NaoEncontrado);
    AssertRecusa(await uc.EstornarLiberacao(999_999, Qualidade, false, Ct), "NaoEncontrado", TipoDeErro.NaoEncontrado);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Estorno_em_Pedido_fechado_e_recusado(string status)
  {
    var (c, afericaoId, contagemId) = await ComContagemAsync();
    var pedido = await c.Liberacao().Pedir(Peca, new NovoPedidoDeLiberacaoDto(1m, Justificativa), Qualidade, Ct);
    c.Execucao.StatusDoPedido[PedidoId] = status;
    var uc = c.EstornoDaExpedicao();

    AssertRecusa(await uc.EstornarAfericao(afericaoId, Qualidade, false, Ct), CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
    AssertRecusa(await uc.EstornarContagem(contagemId, Qualidade, false, Ct), CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
    AssertRecusa(await uc.EstornarLiberacao(pedido.Valor!.Id, Qualidade, false, Ct), CodigosDaExecucao.PedidoFechado, TipoDeErro.Conflito);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
  }

  // ---------------------------------------------------------------- rota generica

  [Fact]
  public async Task Conserto_da_Qualidade_se_estorna_pela_rota_generica()
  {
    var (c, _, _) = await ComContagemAsync();
    var conserto = await c.Afericao().MandarParaConserto(Peca, new NovoConsertoDto(Dobra, 1m), Qualidade, Ct);
    Assert.True(conserto.Sucesso, conserto.Detalhe);

    var r = await c.Estorno().EstornarMovimentacao(conserto.Valor!.Id, Qualidade, false, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(2m, c.Calcular().Saldo(Peca, Local.NaoConforme));
    Assert.Equal(0m, c.Calcular().Saldo(Peca, Local.AguardandoConserto(Dobra)));
  }
}
