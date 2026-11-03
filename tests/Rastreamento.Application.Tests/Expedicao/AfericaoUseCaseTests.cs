using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// Aferir, contar e mandar para conserto (secoes 3.2 a 3.4 e 5.3 da spec da Fase 5A). Cenario: Pedido
/// `EmProducao`, Peca de 6 que exige relatorio, COPIADA DO CATALOGO (sem descricao propria: o nome nas
/// frases e o do Componente, regra 19), com as 6 no local de expedicao; e um Item filho dela.
/// </summary>
public class AfericaoUseCaseTests
{
  private const int Peca = 1, Item = 2;
  private const string NomeDaPeca = "Chapa dobrada 3 mm";
  private static readonly CancellationToken Ct = CancellationToken.None;

  private static CenarioDeExecucao Cenario(string status = "EmProducao")
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
    c.Execucao.StatusDoPedido[PedidoId] = status;
    c.Mover(Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 6m);
    c.Mover(Peca, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, 6m, Movimentador);
    return c;
  }

  private static Task<Result<AfericaoDto>> Aferir(CenarioDeExecucao c, string? resultado, int no = Peca) =>
      c.Afericao().Aferir(no, new NovaAfericaoDto(resultado), Qualidade, Ct);

  private static Task<Result<ContagemDto>> Contar(CenarioDeExecucao c, decimal conformes, decimal naoConformes) =>
      c.Afericao().Contar(Peca, new NovaContagemDto(conformes, naoConformes), Qualidade, Ct);

  private static Task<Result<MovimentacaoDto>> Consertar(CenarioDeExecucao c, int setorId, decimal quantidade) =>
      c.Afericao().MandarParaConserto(Peca, new NovoConsertoDto(setorId, quantidade), Qualidade, Ct);

  /// <summary>Afericao nao conforme e contagem de 4 conformes e 2 nao: o ponto de partida do conserto.</summary>
  private static async Task<CenarioDeExecucao> ContadoAsync(string status = "EmProducao")
  {
    var c = Cenario(status);
    Assert.True((await Aferir(c, ResultadosDeAfericao.NaoConforme)).Sucesso);
    Assert.True((await Contar(c, 4m, 2m)).Sucesso);
    return c;
  }

  // ---------------------------------------------------------------- aferir

  [Fact]
  public async Task Aferir_conforme_grava_e_devolve_a_afericao()
  {
    var c = Cenario();
    var movimentos = c.Execucao.Movimentacoes.Count;

    var r = await Aferir(c, ResultadosDeAfericao.Conforme);

    Assert.True(r.Sucesso, r.Detalhe);
    var gravada = Assert.Single(c.Expedicao.Afericoes);
    Assert.Equal(gravada.Id, r.Valor!.Id);
    Assert.Equal(Peca, gravada.EstruturaItemId);
    Assert.Equal(TiposDeAfericao.Afericao, gravada.Tipo);
    Assert.Equal(ResultadosDeAfericao.Conforme, gravada.Resultado);
    Assert.Equal(Qualidade, gravada.UsuarioId);
    Assert.Equal(ResultadosDeAfericao.Conforme, r.Valor.Resultado);
    Assert.Equal(gravada.DataHora, r.Valor.DataHora);
    Assert.Equal(Qualidade, r.Valor.UsuarioId);
    Assert.Equal("Qualidade", r.Valor.UsuarioNome);
    Assert.False(r.Valor.Estornada);
    Assert.True(r.Valor.Estornavel);   // quem acabou de registrar e o autor (C7)
    // A afericao nao e movimento (C2): nenhuma quantidade anda.
    Assert.Equal(movimentos, c.Execucao.Movimentacoes.Count);
    Assert.Equal(1, c.Execucao.Commits);
  }

  [Fact]
  public async Task Aferir_Item_e_recusado()
  {
    var c = Cenario();

    var r = await Aferir(c, ResultadosDeAfericao.Conforme, no: Item);

    Assert.Equal(CodigosDaExpedicao.NaoEhPeca, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Empty(c.Expedicao.Afericoes);
  }

  [Fact]
  public async Task Aferir_no_inexistente_e_404()
  {
    var c = Cenario();

    var r = await Aferir(c, ResultadosDeAfericao.Conforme, no: 999);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  [Fact]
  public async Task Aferir_Peca_sem_relatorio_e_recusado()
  {
    var c = Cenario();
    c.Estruturas.Itens.Single(i => i.Id == Peca).RequerRelatorioDimensional = false;

    var r = await Aferir(c, ResultadosDeAfericao.Conforme);

    Assert.Equal(CodigosDaExpedicao.SemRelatorioDimensional, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain($"no {Peca}", r.Detalhe);
    Assert.Empty(c.Expedicao.Afericoes);
  }

  [Fact]
  public async Task Aferir_com_resultado_desconhecido_e_recusado()
  {
    var c = Cenario();

    var desconhecido = await Aferir(c, "Talvez");
    var nulo = await Aferir(c, null);
    var caixaErrada = await Aferir(c, "conforme");

    Assert.All(new[] { desconhecido, nulo, caixaErrada }, r =>
    {
      Assert.Equal(CodigosDaExpedicao.ResultadoInvalido, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    });
    Assert.Empty(c.Expedicao.Afericoes);
  }

  [Fact]
  public async Task Segunda_afericao_sem_estornar_a_primeira_e_recusada()
  {
    var c = Cenario();
    var primeira = await Aferir(c, ResultadosDeAfericao.Conforme);

    var segunda = await Aferir(c, ResultadosDeAfericao.NaoConforme);

    Assert.Equal(CodigosDaExpedicao.AfericaoJaRegistrada, segunda.Erro);
    Assert.Equal(TipoDeErro.Conflito, segunda.TipoDoErro);
    Assert.Contains(NomeDaPeca, segunda.Detalhe);
    Assert.Single(c.Expedicao.Afericoes);

    // Estornada a primeira (arranjo direto no registro), a Peca volta a aceitar afericao: o que recusa e a
    // afericao VALIDA, nao a existencia de uma linha.
    c.Expedicao.Afericoes.Add(new RelatorioDimensional
    {
      Id = 9000, EstruturaItemId = Peca, Tipo = TiposDeAfericao.Estorno, EstornoDeId = primeira.Valor!.Id,
      DataHora = DateTime.UtcNow, UsuarioId = Qualidade,
    });
    var depoisDoEstorno = await Aferir(c, ResultadosDeAfericao.NaoConforme);
    Assert.True(depoisDoEstorno.Sucesso, depoisDoEstorno.Detalhe);
  }

  [Fact]
  public async Task Aferir_em_Pedido_Concluido_e_recusado()
  {
    var c = Cenario("Concluido");

    var aferir = await Aferir(c, ResultadosDeAfericao.NaoConforme);
    var contar = await Contar(c, 4m, 2m);
    var consertar = await Consertar(c, Dobra, 1m);

    Assert.Equal(CodigosDaExecucao.PedidoFechado, aferir.Erro);
    Assert.Equal(TipoDeErro.Conflito, aferir.TipoDoErro);
    Assert.Equal(CodigosDaExecucao.PedidoFechado, contar.Erro);
    Assert.Equal(CodigosDaExecucao.PedidoFechado, consertar.Erro);
    Assert.Empty(c.Expedicao.Afericoes);
  }

  // ---------------------------------------------------------------- contar

  [Fact]
  public async Task Contar_grava_duas_linhas_Medicao_com_a_mesma_contagem()
  {
    var c = Cenario();
    await Aferir(c, ResultadosDeAfericao.NaoConforme);

    var r = await Contar(c, 4m, 2m);

    Assert.True(r.Sucesso, r.Detalhe);
    var contagem = Assert.Single(c.Expedicao.Contagens);
    Assert.Equal(contagem.Id, r.Valor!.Id);
    Assert.Equal((Peca, 4m, 2m, Qualidade), (contagem.EstruturaItemId, contagem.Conformes, contagem.NaoConformes, contagem.UsuarioId));
    Assert.Equal((4m, 2m, Qualidade, "Qualidade"), (r.Valor.Conformes, r.Valor.NaoConformes, r.Valor.UsuarioId, r.Valor.UsuarioNome));
    Assert.False(r.Valor.Estornada);
    Assert.True(r.Valor.Estornavel);

    var medicoes = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Medicao).OrderBy(m => m.Id).ToList();
    Assert.Equal(2, medicoes.Count);
    Assert.All(medicoes, m =>
    {
      Assert.Equal(contagem.Id, m.ContagemId);
      Assert.Equal(Posicoes.NaExpedicao, m.OrigemPosicao);
      Assert.Equal(Qualidade, m.UsuarioId);
      Assert.Null(m.EstornoDeId);
    });
    Assert.Equal(
        new[] { (Posicoes.Conforme, 4m), (Posicoes.NaoConforme, 2m) },
        medicoes.Select(m => (m.DestinoPosicao, m.Quantidade)).OrderBy(x => x.DestinoPosicao).ToArray());

    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(Peca, Local.NaExpedicao));
    Assert.Equal(4m, calc.Saldo(Peca, Local.Conforme));
    Assert.Equal(2m, calc.Saldo(Peca, Local.NaoConforme));
  }

  [Fact]
  public async Task Contar_so_conformes_grava_uma_linha()
  {
    var c = Cenario();
    await Aferir(c, ResultadosDeAfericao.NaoConforme);

    var r = await Contar(c, 6m, 0m);

    Assert.True(r.Sucesso, r.Detalhe);
    var medicao = Assert.Single(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Medicao);
    Assert.Equal((Posicoes.Conforme, 6m, r.Valor!.Id), (medicao.DestinoPosicao, medicao.Quantidade, medicao.ContagemId!.Value));
    Assert.Equal(0m, r.Valor.NaoConformes);
  }

  [Fact]
  public async Task Contar_sem_afericao_nao_conforme_e_recusado()
  {
    var semAfericao = Cenario();
    var conforme = Cenario();
    await Aferir(conforme, ResultadosDeAfericao.Conforme);

    var r1 = await Contar(semAfericao, 4m, 2m);
    var r2 = await Contar(conforme, 4m, 2m);

    Assert.All(new[] { r1, r2 }, r =>
    {
      Assert.Equal(CodigosDaExpedicao.SemAfericaoNaoConforme, r.Erro);
      Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
      Assert.Contains(NomeDaPeca, r.Detalhe);
    });
    Assert.Empty(semAfericao.Expedicao.Contagens);
    Assert.Empty(conforme.Expedicao.Contagens);
    Assert.DoesNotContain(conforme.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Medicao);
  }

  [Fact]
  public async Task Contar_mais_que_o_que_esta_na_expedicao_e_recusado()
  {
    var c = Cenario();
    await Aferir(c, ResultadosDeAfericao.NaoConforme);

    var r = await Contar(c, 5m, 2m);

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.Contains("6", r.Detalhe);
    Assert.DoesNotContain($"no {Peca}", r.Detalhe);
    Assert.Empty(c.Expedicao.Contagens);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Medicao);
  }

  [Fact]
  public async Task Contagem_zero_e_zero_e_recusada()
  {
    var c = Cenario();
    await Aferir(c, ResultadosDeAfericao.NaoConforme);

    var vazia = await Contar(c, 0m, 0m);
    var negativa = await Contar(c, -1m, 2m);
    var cincoCasas = await Contar(c, 1m, 0.00001m);

    Assert.Equal(CodigosDaExpedicao.ContagemVazia, vazia.Erro);
    Assert.Equal(TipoDeErro.Validacao, vazia.TipoDoErro);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, negativa.Erro);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, cincoCasas.Erro);
    Assert.Empty(c.Expedicao.Contagens);
  }

  [Fact]
  public async Task Contar_trava_o_no_e_depois_o_Pedido_antes_de_gravar()
  {
    var c = Cenario();
    await Aferir(c, ResultadosDeAfericao.NaoConforme);
    c.Execucao.Eventos.Clear();
    c.Execucao.PedidoLidoAposEventos.Clear();

    await Contar(c, 4m, 2m);

    Assert.Equal($"nos:{Peca}", c.Execucao.Eventos[0]);
    Assert.Equal($"pedidos:{Peca}", c.Execucao.Eventos[1]);
    Assert.All(c.Execucao.Eventos.Skip(2), e => Assert.Equal("save", e));
    // E o Pedido so e lido depois de travado: lido antes, a trava converteria S em U e deadlockaria.
    Assert.All(c.Execucao.PedidoLidoAposEventos, n => Assert.True(n >= 2, $"Pedido lido depois de {n} eventos"));
    Assert.NotEmpty(c.Execucao.PedidoLidoAposEventos);
  }

  // ---------------------------------------------------------------- conserto

  [Fact]
  public async Task Conserto_move_de_NaoConforme_para_AguardandoConserto_no_Setor()
  {
    var c = await ContadoAsync();

    var r = await Consertar(c, Dobra, 1m);

    Assert.True(r.Sucesso, r.Detalhe);
    var dto = r.Valor!;
    Assert.Equal(TiposDeMovimentacao.Conserto, dto.Tipo);
    Assert.Equal((Posicoes.NaoConforme, (int?)null, (int?)null), (dto.Origem.Posicao, dto.Origem.SetorId, dto.Origem.Ordem));
    Assert.Equal((Posicoes.AguardandoConserto, (int?)Dobra, "Dobra", (int?)null),
        (dto.Destino.Posicao, dto.Destino.SetorId, dto.Destino.SetorNome, dto.Destino.Ordem));
    Assert.Equal((1m, Qualidade, "Qualidade"), (dto.Quantidade, dto.UsuarioId, dto.UsuarioNome));
    Assert.Null(dto.ContagemId);

    var calc = c.Calcular();
    Assert.Equal(1m, calc.Saldo(Peca, Local.NaoConforme));
    Assert.Equal(1m, calc.Saldo(Peca, Local.AguardandoConserto(Dobra)));
    Assert.Equal(4m, calc.Saldo(Peca, Local.Conforme));
  }

  [Fact]
  public async Task Conserto_em_Setor_inativo_e_recusado()
  {
    var c = await ContadoAsync();

    var inativo = await Consertar(c, Inativo, 1m);
    var inexistente = await Consertar(c, 999, 1m);

    Assert.All(new[] { inativo, inexistente }, r =>
    {
      Assert.Equal(CodigosDaExpedicao.SetorInvalido, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    });
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Conserto);
  }

  [Fact]
  public async Task Conserto_acima_do_NaoConforme_e_recusado()
  {
    var c = await ContadoAsync();

    var r = await Consertar(c, Dobra, 3m);
    var invalida = await Consertar(c, Dobra, 0m);

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.Contains("2", r.Detalhe);
    Assert.DoesNotContain($"no {Peca}", r.Detalhe);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, invalida.Erro);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Conserto);
  }

  [Fact]
  public async Task Conserto_de_Item_e_recusado()
  {
    var c = await ContadoAsync();

    var r = await c.Afericao().MandarParaConserto(Item, new NovoConsertoDto(Dobra, 1m), Qualidade, Ct);

    Assert.Equal(CodigosDaExpedicao.NaoEhPeca, r.Erro);
  }

  [Fact]
  public async Task Conserto_tira_o_Pedido_de_AguardandoExpedicao()
  {
    var c = await ContadoAsync("AguardandoExpedicao");
    // A contagem so troca de posicao dentro do local de expedicao: o Pedido continua aguardando.
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId]);
    c.Execucao.Eventos.Clear();
    c.Execucao.PedidoLidoAposEventos.Clear();

    var r = await Consertar(c, Dobra, 1m);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId]);
    // A trava do Pedido vem logo depois da do no, antes de gravar (spec da Fase 3, secao 8.1).
    Assert.Equal(new[] { $"nos:{Peca}", $"pedidos:{Peca}", "save" }, c.Execucao.Eventos);
    Assert.All(c.Execucao.PedidoLidoAposEventos, n => Assert.True(n >= 2, $"Pedido lido depois de {n} eventos"));
  }
}
