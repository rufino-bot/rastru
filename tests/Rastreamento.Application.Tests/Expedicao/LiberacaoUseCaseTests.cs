using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// O pedido de liberacao da Qualidade e a resposta da Gestao (D7, C5, C6, C18 e secao 3.6 da spec da Fase 5A).
/// Cenario: Pedido `EmProducao`, Peca de 6 que exige relatorio, COPIADA DO CATALOGO (sem descricao propria: o
/// nome nas frases e o do Componente, regra 19), afericao nao conforme e contagem de 4 conformes e 2 nao; e um
/// Item filho dela.
/// </summary>
public class LiberacaoUseCaseTests
{
  private const int Peca = 1, Item = 2;
  private const string NomeDaPeca = "Chapa dobrada 3 mm";
  private const string Justificativa = "Aba da dobra 1,5 mm fora; o cliente não usa.";
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static async Task<CenarioDeExecucao> CenarioAsync(string status = "EmProducao")
  {
    var c = new CenarioDeExecucao();
    c.No(Peca, null, 6m, null, Corte);
    c.No(Item, Peca, 12m, 2m, Dobra);
    var peca = c.Estruturas.Itens.Single(i => i.Id == Peca);
    peca.Descricao = null;
    peca.ComponenteId = 70;
    peca.RequerRelatorioDimensional = true;
    c.Catalogo.Componentes.Add(
        new Componente { Id = 70, Codigo = "CHP-220", Descricao = NomeDaPeca, Tipo = "Fabricado", Ativo = true });
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Mover(Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, 6m, Movimentador);
    Assert.True((await c.Afericao().Aferir(Peca, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct)).Sucesso);
    Assert.True((await c.Afericao().Contar(Peca, new NovaContagemDto(4m, 2m), Qualidade, Ct)).Sucesso);
    c.Execucao.StatusDoPedido[PedidoId] = status;
    return c;
  }

  private static Task<Result<LiberacaoDto>> Pedir(
      CenarioDeExecucao c, decimal quantidade, string? justificativa = Justificativa, int no = Peca) =>
      c.Liberacao().Pedir(no, new NovoPedidoDeLiberacaoDto(quantidade, justificativa), Qualidade, Ct);

  private static Task<Result<LiberacaoDto>> Responder(
      CenarioDeExecucao c, int pedidoId, decimal liberada, string? observacao = null) =>
      c.Liberacao().Responder(pedidoId, new RespostaDeLiberacaoDto(liberada, observacao), Gestao, Ct);

  /// <summary>Com o pedido de 2 ja feito: o ponto de partida da resposta.</summary>
  private static async Task<(CenarioDeExecucao C, int PedidoId)> PedidoAsync(decimal quantidade = 2m)
  {
    var c = await CenarioAsync();
    var r = await Pedir(c, quantidade);
    Assert.True(r.Sucesso, r.Detalhe);
    return (c, r.Valor!.Id);
  }

  /// <summary>Estorna direto no livro (sem caso de uso) as linhas que apontam o registro de liberacao.</summary>
  private static void EstornarLinhas(CenarioDeExecucao c, int liberacaoId)
  {
    var linhas = c.Execucao.Movimentacoes
        .Where(m => m.LiberacaoId == liberacaoId && m.Tipo != TiposDeMovimentacao.Estorno).ToList();
    Assert.NotEmpty(linhas);
    foreach (var linha in linhas)
    {
      var estorno = c.Mover(Peca, TiposDeMovimentacao.Estorno,
          new Local(linha.DestinoPosicao, linha.DestinoSetorId, linha.DestinoOrdem),
          new Local(linha.OrigemPosicao, linha.OrigemSetorId, linha.OrigemOrdem), linha.Quantidade, linha.UsuarioId);
      estorno.EstornoDeId = linha.Id;
      estorno.LiberacaoId = liberacaoId;
    }
  }

  // ---------------------------------------------------------------- pedir

  [Fact]
  public async Task Pedir_move_de_NaoConforme_para_AguardandoLiberacao_com_a_justificativa()
  {
    var c = await CenarioAsync();

    var r = await Pedir(c, 2m, "  " + Justificativa + "  ");

    Assert.True(r.Sucesso, r.Detalhe);
    var pedido = Assert.Single(c.Expedicao.Liberacoes);
    Assert.Equal((TiposDeLiberacao.Pedido, (int?)null, Peca, 2m, Justificativa, Qualidade),
        (pedido.Tipo, pedido.PedidoDeLiberacaoId, pedido.EstruturaItemId, pedido.Quantidade, pedido.Texto, pedido.UsuarioId));
    var dto = r.Valor!;
    Assert.Equal((pedido.Id, TiposDeLiberacao.Pedido, (int?)null, Peca, 2m, Justificativa),
        (dto.Id, dto.Tipo, dto.PedidoDeLiberacaoId, dto.EstruturaItemId, dto.Quantidade, dto.Texto));
    Assert.Equal((pedido.DataHora, Qualidade, "Qualidade"), (dto.DataHora, dto.UsuarioId, dto.UsuarioNome));
    Assert.False(dto.Estornada);
    Assert.True(dto.Estornavel);   // quem acabou de pedir e o autor (C7)

    var linha = Assert.Single(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.PedidoDeLiberacao);
    Assert.Equal((Posicoes.NaoConforme, Posicoes.AguardandoLiberacao, 2m, (int?)pedido.Id, Qualidade),
        (linha.OrigemPosicao, linha.DestinoPosicao, linha.Quantidade, linha.LiberacaoId, linha.UsuarioId));
    Assert.Null(linha.ContagemId);
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaoConforme));
    Assert.Equal(2m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    Assert.Equal(4m, calc.Saldo(Peca, Local.Conforme));
  }

  [Fact]
  public async Task Pedir_sem_justificativa_e_recusado()
  {
    var c = await CenarioAsync();

    var branca = await Pedir(c, 2m, "   ");
    var nula = await Pedir(c, 2m, null);

    Assert.All(new[] { branca, nula }, r =>
    {
      Assert.Equal(CodigosDaExpedicao.JustificativaObrigatoria, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    });
    Assert.Empty(c.Expedicao.Liberacoes);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.PedidoDeLiberacao);
  }

  [Fact]
  public async Task Pedir_com_texto_de_501_caracteres_e_recusado()
  {
    var c = await CenarioAsync();

    var longa = await Pedir(c, 2m, new string('x', CodigosDaExpedicao.TamanhoMaximoDoTexto + 1));

    Assert.Equal(CodigosDaExpedicao.TextoLongoDemais, longa.Erro);
    Assert.Equal(TipoDeErro.Validacao, longa.TipoDoErro);
    Assert.Empty(c.Expedicao.Liberacoes);

    // O teto e inclusivo: 500 cabem.
    var noTeto = await Pedir(c, 2m, new string('x', CodigosDaExpedicao.TamanhoMaximoDoTexto));
    Assert.True(noTeto.Sucesso, noTeto.Detalhe);
  }

  [Fact]
  public async Task Pedir_com_outro_em_aberto_na_mesma_Peca_e_recusado()
  {
    var (c, _) = await PedidoAsync(quantidade: 1m);

    var segundo = await Pedir(c, 1m);

    Assert.Equal(CodigosDaExpedicao.LiberacaoEmAberto, segundo.Erro);
    Assert.Equal(TipoDeErro.Conflito, segundo.TipoDoErro);
    Assert.Contains(NomeDaPeca, segundo.Detalhe);
    Assert.DoesNotContain($"no {Peca}", segundo.Detalhe);
    Assert.Single(c.Expedicao.Liberacoes);
    Assert.Equal(1m, c.Calcular().Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Pedir_depois_de_respondido_ou_estornado_o_anterior_passa()
  {
    // Respondido: o pedido deixa de estar em aberto e a Peca aceita outro (C18 e "no maximo um EM ABERTO").
    var (respondido, primeiro) = await PedidoAsync(quantidade: 1m);
    Assert.True((await Responder(respondido, primeiro, 0m)).Sucesso);
    var depoisDaResposta = await Pedir(respondido, 2m);
    Assert.True(depoisDaResposta.Sucesso, depoisDaResposta.Detalhe);

    // Estornado (arranjo direto no livro): idem.
    var (estornado, outro) = await PedidoAsync(quantidade: 1m);
    EstornarLinhas(estornado, outro);
    var depoisDoEstorno = await Pedir(estornado, 2m);
    Assert.True(depoisDoEstorno.Sucesso, depoisDoEstorno.Detalhe);
  }

  [Fact]
  public async Task Pedir_com_a_resposta_estornada_e_recusado()
  {
    // Estornar a resposta devolve o pedido a "em aberto" (secao 3.6): a Peca volta a ter um pedido em aberto.
    var (c, pedidoId) = await PedidoAsync(quantidade: 1m);
    var resposta = await Responder(c, pedidoId, 1m);
    EstornarLinhas(c, resposta.Valor!.Id);

    var outro = await Pedir(c, 1m);

    Assert.Equal(CodigosDaExpedicao.LiberacaoEmAberto, outro.Erro);
  }

  [Fact]
  public async Task Pedir_acima_do_NaoConforme_e_recusado()
  {
    var c = await CenarioAsync();

    var acima = await Pedir(c, 3m);
    var invalida = await Pedir(c, 0m);

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, acima.Erro);
    Assert.Equal(TipoDeErro.Conflito, acima.TipoDoErro);
    Assert.Contains(NomeDaPeca, acima.Detalhe);
    Assert.Contains("2", acima.Detalhe);
    Assert.DoesNotContain($"no {Peca}", acima.Detalhe);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, invalida.Erro);
    Assert.Empty(c.Expedicao.Liberacoes);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.PedidoDeLiberacao);
  }

  [Fact]
  public async Task Pedir_para_Item_e_recusado()
  {
    var c = await CenarioAsync();

    var r = await Pedir(c, 1m, no: Item);
    var inexistente = await Pedir(c, 1m, no: 999);

    Assert.Equal(CodigosDaExpedicao.NaoEhPeca, r.Erro);
    Assert.Equal(TipoDeErro.NaoEncontrado, inexistente.TipoDoErro);
    Assert.Empty(c.Expedicao.Liberacoes);
  }

  [Fact]
  public async Task Pedir_em_Pedido_Concluido_e_recusado()
  {
    var c = await CenarioAsync("Concluido");

    var r = await Pedir(c, 1m);

    Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Empty(c.Expedicao.Liberacoes);
  }

  [Fact]
  public async Task Pedir_trava_o_no_e_depois_o_Pedido_antes_de_gravar()
  {
    var c = await CenarioAsync();
    c.Execucao.Eventos.Clear();
    c.Execucao.PedidoLidoAposEventos.Clear();

    Assert.True((await Pedir(c, 2m)).Sucesso);

    Assert.Equal($"nos:{Peca}", c.Execucao.Eventos[0]);
    Assert.Equal($"pedidos:{Peca}", c.Execucao.Eventos[1]);
    Assert.All(c.Execucao.Eventos.Skip(2), e => Assert.Equal("save", e));
    Assert.NotEmpty(c.Execucao.PedidoLidoAposEventos);
    Assert.All(c.Execucao.PedidoLidoAposEventos, n => Assert.True(n >= 2, $"Pedido lido depois de {n} eventos"));
  }

  // ---------------------------------------------------------------- responder

  [Fact]
  public async Task Responder_tudo_libera_tudo()
  {
    var (c, pedidoId) = await PedidoAsync();

    var r = await Responder(c, pedidoId, 2m, "  Cliente avisado por telefone.  ");

    Assert.True(r.Sucesso, r.Detalhe);
    var resposta = Assert.Single(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
    Assert.Equal(((int?)pedidoId, Peca, 2m, "Cliente avisado por telefone.", Gestao),
        (resposta.PedidoDeLiberacaoId, resposta.EstruturaItemId, resposta.Quantidade, resposta.Texto, resposta.UsuarioId));
    var dto = r.Valor!;
    Assert.Equal((resposta.Id, TiposDeLiberacao.Resposta, (int?)pedidoId, Peca, 2m, "Cliente avisado por telefone."),
        (dto.Id, dto.Tipo, dto.PedidoDeLiberacaoId, dto.EstruturaItemId, dto.Quantidade, dto.Texto));
    Assert.Equal((resposta.DataHora, Gestao, "Gestão"), (dto.DataHora, dto.UsuarioId, dto.UsuarioNome));
    Assert.False(dto.Estornada);
    Assert.True(dto.Estornavel);

    var linha = Assert.Single(c.Execucao.Movimentacoes, m => m.LiberacaoId == resposta.Id);
    Assert.Equal((TiposDeMovimentacao.Liberacao, Posicoes.AguardandoLiberacao, Posicoes.Liberada, 2m, Gestao),
        (linha.Tipo, linha.OrigemPosicao, linha.DestinoPosicao, linha.Quantidade, linha.UsuarioId));
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Recusa);
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    Assert.Equal(2m, calc.Saldo(Peca, Local.Liberada));
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Responder_em_parte_libera_e_devolve_o_resto()
  {
    var (c, pedidoId) = await PedidoAsync();

    var r = await Responder(c, pedidoId, 1m);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Null(r.Valor!.Texto);   // observacao opcional (C6)
    var linhas = c.Execucao.Movimentacoes
        .Where(m => m.Tipo is TiposDeMovimentacao.Liberacao or TiposDeMovimentacao.Recusa)
        .OrderBy(m => m.Tipo).ToList();
    Assert.Equal(
        new[]
        {
          (TiposDeMovimentacao.Liberacao, Posicoes.AguardandoLiberacao, Posicoes.Liberada, 1m, (int?)r.Valor.Id),
          (TiposDeMovimentacao.Recusa, Posicoes.AguardandoLiberacao, Posicoes.NaoConforme, 1m, (int?)r.Valor.Id),
        },
        linhas.Select(m => (m.Tipo, m.OrigemPosicao, m.DestinoPosicao, m.Quantidade, m.LiberacaoId)).ToArray());
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    Assert.Equal(1m, calc.Saldo(Peca, Local.Liberada));
    Assert.Equal(1m, calc.Saldo(Peca, Local.NaoConforme));   // o que nao foi liberado volta a aguardar decisao (C5)
  }

  [Fact]
  public async Task Responder_zero_e_recusa_total()
  {
    var (c, pedidoId) = await PedidoAsync();

    var r = await Responder(c, pedidoId, 0m, "   ");

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(0m, r.Valor!.Quantidade);
    Assert.Null(r.Valor.Texto);   // observacao em branco nao e gravada
    var linha = Assert.Single(c.Execucao.Movimentacoes, m => m.LiberacaoId == r.Valor.Id);
    Assert.Equal((TiposDeMovimentacao.Recusa, Posicoes.AguardandoLiberacao, Posicoes.NaoConforme, 2m),
        (linha.Tipo, linha.OrigemPosicao, linha.DestinoPosicao, linha.Quantidade));
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Liberacao);
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(Peca, Local.AguardandoLiberacao));
    Assert.Equal(2m, calc.Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Responder_acima_do_pedido_e_recusado()
  {
    var (c, pedidoId) = await PedidoAsync();

    var acima = await Responder(c, pedidoId, 3m);
    var negativa = await Responder(c, pedidoId, -1m);
    var cincoCasas = await Responder(c, pedidoId, 0.00001m);

    Assert.Equal(CodigosDaExpedicao.QuantidadeAcimaDaPedida, acima.Erro);
    Assert.Equal(TipoDeErro.Validacao, acima.TipoDoErro);
    Assert.Contains("2", acima.Detalhe);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, negativa.Erro);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, cincoCasas.Erro);
    Assert.DoesNotContain(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
    Assert.Equal(2m, c.Calcular().Saldo(Peca, Local.AguardandoLiberacao));
  }

  [Fact]
  public async Task Responder_com_observacao_de_501_caracteres_e_recusado()
  {
    var (c, pedidoId) = await PedidoAsync();

    var r = await Responder(c, pedidoId, 2m, new string('x', CodigosDaExpedicao.TamanhoMaximoDoTexto + 1));

    Assert.Equal(CodigosDaExpedicao.TextoLongoDemais, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.DoesNotContain(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
  }

  [Fact]
  public async Task Responder_duas_vezes_e_recusado()
  {
    var (c, pedidoId) = await PedidoAsync();
    Assert.True((await Responder(c, pedidoId, 1m)).Sucesso);

    var segunda = await Responder(c, pedidoId, 1m);

    Assert.Equal(CodigosDaExpedicao.LiberacaoNaoPendente, segunda.Erro);
    Assert.Equal(TipoDeErro.Conflito, segunda.TipoDoErro);
    Assert.Contains(NomeDaPeca, segunda.Detalhe);
    Assert.Single(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
    Assert.Equal(1m, c.Calcular().Saldo(Peca, Local.Liberada));
  }

  [Fact]
  public async Task Responder_de_novo_depois_de_estornada_a_resposta_passa()
  {
    // Estornar a resposta devolve o pedido a "em aberto" (secao 3.6): a Gestao responde de novo.
    var (c, pedidoId) = await PedidoAsync();
    var primeira = await Responder(c, pedidoId, 1m);
    EstornarLinhas(c, primeira.Valor!.Id);

    var segunda = await Responder(c, pedidoId, 2m);

    Assert.True(segunda.Sucesso, segunda.Detalhe);
    var calc = c.Calcular();
    Assert.Equal(2m, calc.Saldo(Peca, Local.Liberada));
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Responder_pedido_estornado_e_recusado()
  {
    var (c, pedidoId) = await PedidoAsync();
    EstornarLinhas(c, pedidoId);

    var r = await Responder(c, pedidoId, 2m);

    Assert.Equal(CodigosDaExpedicao.LiberacaoNaoPendente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.DoesNotContain(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
    Assert.Equal(2m, c.Calcular().Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Responder_uma_resposta_e_404()
  {
    var (c, pedidoId) = await PedidoAsync();
    var resposta = await Responder(c, pedidoId, 1m);

    var r = await Responder(c, resposta.Valor!.Id, 1m);
    var inexistente = await Responder(c, 999_999, 1m);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, inexistente.TipoDoErro);
    Assert.Single(c.Expedicao.Liberacoes, l => l.Tipo == TiposDeLiberacao.Resposta);
  }

  [Fact]
  public async Task Responder_trava_o_no_e_depois_o_Pedido_antes_de_gravar()
  {
    var (c, pedidoId) = await PedidoAsync();
    c.Execucao.Eventos.Clear();
    c.Execucao.PedidoLidoAposEventos.Clear();

    Assert.True((await Responder(c, pedidoId, 1m)).Sucesso);

    Assert.Equal($"nos:{Peca}", c.Execucao.Eventos[0]);
    Assert.Equal($"pedidos:{Peca}", c.Execucao.Eventos[1]);
    Assert.All(c.Execucao.Eventos.Skip(2), e => Assert.Equal("save", e));
    Assert.All(c.Execucao.PedidoLidoAposEventos, n => Assert.True(n >= 2, $"Pedido lido depois de {n} eventos"));
  }
}
