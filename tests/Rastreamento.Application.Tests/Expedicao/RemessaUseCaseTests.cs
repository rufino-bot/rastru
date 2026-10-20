using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// A remessa (C8, C9, C10, C12, C13, D8 e D9 da spec da Fase 5A, secao 3.5): expedir uma Peca ou um lote numa
/// remessa so, de onde ela tira, os avisos, o fechamento que ela dispara e o estorno da remessa inteira.
/// Cenario: a Peca 1 vive no Pedido 1 e a Peca 3 no Pedido 2 (um Agrupamento cada, uma Peca por Pedido), as duas
/// COPIADAS DO CATALOGO (sem descricao propria: o nome nas frases e o do Componente, regra 19), em Pedido
/// `EmProducao`, levadas inteiras ao local de expedicao por arranjo direto no livro.
/// </summary>
public class RemessaUseCaseTests
{
  private const int Peca = 1, Item = 2, OutraPeca = 3;
  private const string NomeDaPeca = "Chapa dobrada 3 mm", NomeDaOutra = "Cantoneira 2 pol";
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>Uma Peca copiada do Componente `componenteId`, de `quantidade`, com tudo em `NaExpedicao`.</summary>
  private static void PecaNaExpedicao(
      CenarioDeExecucao c, int id, decimal quantidade, int agrupamento = AgrupamentoId, bool requerRelatorio = false)
  {
    c.NoDoAgrupamento(agrupamento, id, null, quantidade, null, Corte);
    var (componenteId, codigo, descricao) = id == Peca ? (70, "CHP-220", NomeDaPeca) : (71, "CNT-002", NomeDaOutra);
    var peca = c.Estruturas.Itens.Single(i => i.Id == id);
    peca.Descricao = null;
    peca.ComponenteId = componenteId;
    peca.RequerRelatorioDimensional = requerRelatorio;
    c.Catalogo.Componentes.Add(
        new Componente { Id = componenteId, Codigo = codigo, Descricao = descricao, Tipo = "Fabricado", Ativo = true });
    c.Mover(id, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, quantidade, Movimentador);
  }

  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Execucao.StatusDoPedido[PedidoId2] = "EmProducao";
    return c;
  }

  /// <summary>
  /// A Peca 1, de 5, que exige relatorio, com afericao nao conforme (situacao `NaoConforme`) e a quantidade
  /// repartida em Liberada 1, Conforme 2 e NaoConforme 2 (arranjo direto no livro).
  /// </summary>
  private static async Task<CenarioDeExecucao> RepartidaAsync()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 5m, requerRelatorio: true);
    Assert.True((await c.Afericao().Aferir(Peca, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct)).Sucesso);
    c.Mover(Peca, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.Conforme, 2m, Qualidade);
    c.Mover(Peca, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 3m, Qualidade);
    c.Mover(Peca, TiposDeMovimentacao.PedidoDeLiberacao, Local.NaoConforme, Local.AguardandoLiberacao, 1m, Qualidade);
    c.Mover(Peca, TiposDeMovimentacao.Liberacao, Local.AguardandoLiberacao, Local.Liberada, 1m, Gestao);
    return c;
  }

  private static Task<Result<ResultadoDaRemessaDto>> Expedir(CenarioDeExecucao c, params (int No, decimal Quantidade)[] itens) =>
      c.Remessa().Expedir(new NovaRemessaDto(itens.Select(i => new ItemDaRemessaDto(i.No, i.Quantidade)).ToList()), Qualidade, Ct);

  private static IReadOnlyList<Movimentacao> LinhasDeExpedicao(CenarioDeExecucao c) =>
      c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Expedicao).ToList();

  // ---------------------------------------------------------------- expedir

  [Fact]
  public async Task Expedir_tira_na_ordem_C10_e_grava_uma_linha_por_origem()
  {
    var c = await RepartidaAsync();

    var r = await Expedir(c, (Peca, 4m));

    Assert.True(r.Sucesso, r.Detalhe);
    var remessa = Assert.Single(c.Expedicao.Remessas);
    Assert.Equal(Qualidade, remessa.UsuarioId);
    var linhas = LinhasDeExpedicao(c);
    Assert.Equal(
        new[] { (Posicoes.Liberada, 1m), (Posicoes.Conforme, 2m), (Posicoes.NaoConforme, 1m) },
        linhas.Select(l => (l.OrigemPosicao, l.Quantidade)));
    Assert.All(linhas, l =>
    {
      Assert.Equal((Peca, Posicoes.Expedido, (int?)remessa.Id, Qualidade, remessa.DataHora),
          (l.EstruturaItemId, l.DestinoPosicao, l.RemessaId, l.UsuarioId, l.DataHora));
      Assert.Null(l.OrigemSetorId);
      Assert.Null(l.DestinoSetorId);
    });
    // AguardandoLiberacao nao sai (C10): nao havia nenhuma; o que sobra e 1 nao conforme.
    var calc = c.Calcular();
    Assert.Equal(4m, calc.Saldo(Peca, Local.Expedido));
    Assert.Equal(1m, calc.Saldo(Peca, Local.NaoConforme));

    var dto = r.Valor!.Remessa;
    Assert.Equal((remessa.Id, remessa.DataHora, Qualidade, "Qualidade", false, true),
        (dto.Id, dto.DataHora, dto.UsuarioId, dto.UsuarioNome, dto.Estornada, dto.Estornavel));
    Assert.Equal(
        new[] { (Posicoes.Liberada, 1m), (Posicoes.Conforme, 2m), (Posicoes.NaoConforme, 1m) },
        dto.Linhas.Select(l => (l.Origem, l.Quantidade)));
    Assert.All(dto.Linhas, l =>
        Assert.Equal((Peca, NomeDaPeca, (string?)"CHP-220", PedidoId, "PED-01", false),
            (l.PecaId, l.PecaDescricao, l.CodigoDoComponente, l.PedidoId, l.PedidoNumero, l.PedidoConcluido)));
  }

  [Fact]
  public async Task Expedir_devolve_os_avisos_da_situacao()
  {
    var repartida = await RepartidaAsync();
    var r = await Expedir(repartida, (Peca, 4m));
    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(new[] { new AvisoDaPecaDto(Peca, TiposDeAviso.NaoConformeSemLiberacao, 1m) }, r.Valor!.Avisos);

    // Peca que exige relatorio e nao tem afericao (Pendente), com 3 em NaExpedicao.
    var pendente = Cenario();
    PecaNaExpedicao(pendente, Peca, 3m, requerRelatorio: true);
    var semAfericao = await Expedir(pendente, (Peca, 3m));
    Assert.True(semAfericao.Sucesso, semAfericao.Detalhe);
    Assert.Equal(new[] { new AvisoDaPecaDto(Peca, TiposDeAviso.SemAfericao, 3m) }, semAfericao.Valor!.Avisos);
  }

  [Fact]
  public async Task Expedir_Peca_sem_relatorio_nao_avisa()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);

    var r = await Expedir(c, (Peca, 3m));

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Empty(r.Valor!.Avisos);
    Assert.Equal(Posicoes.NaExpedicao, Assert.Single(LinhasDeExpedicao(c)).OrigemPosicao);
  }

  [Fact]
  public async Task Expedir_acima_do_expedivel_e_recusado_e_cita_o_que_aguarda_liberacao()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 5m, requerRelatorio: true);
    c.Mover(Peca, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 2m, Qualidade);
    c.Mover(Peca, TiposDeMovimentacao.PedidoDeLiberacao, Local.NaoConforme, Local.AguardandoLiberacao, 2m, Qualidade);

    var r = await Expedir(c, (Peca, 4m));

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.Contains("3", r.Detalhe);                     // o expedivel
    Assert.Contains("2 aguardam", r.Detalhe);            // o que espera a Gestao
    Assert.Empty(c.Expedicao.Remessas);
    Assert.Empty(LinhasDeExpedicao(c));

    // O expedivel inteiro sai.
    Assert.True((await Expedir(c, (Peca, 3m))).Sucesso);
  }

  [Fact]
  public async Task Expedir_quantidade_invalida_e_recusado()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);

    var zero = await Expedir(c, (Peca, 0m));
    var negativa = await Expedir(c, (Peca, -1m));
    var cincoCasas = await Expedir(c, (Peca, 1.00001m));

    Assert.All(new[] { zero, negativa, cincoCasas }, r =>
    {
      Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    });
    Assert.Equal(0, c.Execucao.Transacoes);
    Assert.Empty(c.Expedicao.Remessas);
  }

  [Fact]
  public async Task Expedir_Item_e_recusado_e_no_inexistente_e_404()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    c.No(Item, Peca, 6m, 2m, Dobra);

    var item = await Expedir(c, (Item, 1m));
    var inexistente = await Expedir(c, (999, 1m));

    Assert.Equal(CodigosDaExpedicao.NaoEhPeca, item.Erro);
    Assert.Equal(TipoDeErro.Validacao, item.TipoDoErro);
    Assert.Contains("No 2", item.Detalhe);
    Assert.Equal(TipoDeErro.NaoEncontrado, inexistente.TipoDoErro);
    Assert.Null(inexistente.Detalhe);
    Assert.Empty(c.Expedicao.Remessas);
  }

  [Fact]
  public async Task Lote_de_duas_Pecas_de_dois_Pedidos_e_uma_remessa_so()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2);

    var r = await Expedir(c, (OutraPeca, 4m), (Peca, 2m));

    Assert.True(r.Sucesso, r.Detalhe);
    var remessa = Assert.Single(c.Expedicao.Remessas);
    var linhas = LinhasDeExpedicao(c);
    Assert.Equal(new[] { (OutraPeca, 4m), (Peca, 2m) }, linhas.Select(l => (l.EstruturaItemId, l.Quantidade)));
    Assert.All(linhas, l => Assert.Equal(remessa.Id, l.RemessaId));
    // As duas Pecas travadas antes, e os Pedidos delas depois (spec da Fase 3, secao 8.1).
    Assert.Equal(new[] { Peca, OutraPeca }, Assert.Single(c.Execucao.Travas));
    Assert.Equal(new[] { Peca, OutraPeca }, Assert.Single(c.Execucao.TravasDePedido));   // os Pedidos delas
    Assert.Equal(new[] { "nos:1,3", "pedidos:1,3" }, c.Execucao.Eventos.Where(e => e.StartsWith("nos:") || e.StartsWith("pedidos:")).TakeLast(2));
    // Os dois Pedidos recalculados: o 2 expediu tudo e concluiu; o 1 tem 1 no local de expedicao.
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId2]);
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId]);
    Assert.Equal(
        new[] { (OutraPeca, NomeDaOutra, PedidoId2, "PED-02", true), (Peca, NomeDaPeca, PedidoId, "PED-01", false) },
        r.Valor!.Remessa.Linhas.Select(l => (l.PecaId, l.PecaDescricao, l.PedidoId, l.PedidoNumero, l.PedidoConcluido)));
  }

  [Fact]
  public async Task Lote_com_a_mesma_Peca_duas_vezes_e_recusado()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);

    var r = await Expedir(c, (Peca, 1m), (Peca, 1m));

    Assert.Equal(CodigosDaExecucao.ItemRepetido, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain($"nó {Peca}", r.Detalhe);
    Assert.Equal(0, c.Execucao.Transacoes);
    Assert.Empty(c.Expedicao.Remessas);
  }

  [Fact]
  public async Task Lote_vazio_e_recusado()
  {
    var c = Cenario();

    var vazio = await Expedir(c);
    var nulo = await c.Remessa().Expedir(new NovaRemessaDto(null), Qualidade, Ct);
    var grande = await Expedir(c, Enumerable.Range(1, ApontamentoUseCase.TamanhoMaximoDoLote + 1).Select(i => (i, 1m)).ToArray());

    Assert.All(new[] { vazio, nulo }, r =>
    {
      Assert.Equal(CodigosDaExecucao.LoteVazio, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    });
    Assert.Equal(CodigosDaExecucao.LoteGrandeDemais, grande.Erro);
    Assert.Equal(0, c.Execucao.Transacoes);
  }

  [Fact]
  public async Task Lote_com_um_item_ruim_nao_grava_nada()
  {
    var c = Cenario();
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2);
    PecaNaExpedicao(c, Peca, 3m);

    var r = await Expedir(c, (OutraPeca, 4m), (Peca, 5m));

    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, r.Erro);
    Assert.Contains(NomeDaPeca, r.Detalhe);
    Assert.DoesNotContain(NomeDaOutra, r.Detalhe);
    Assert.Empty(c.Expedicao.Remessas);
    Assert.Empty(LinhasDeExpedicao(c));
    Assert.Equal("EmProducao", c.Execucao.StatusDoPedido[PedidoId2]);
    Assert.Equal(0, c.Execucao.Commits);
  }

  [Fact]
  public async Task Review_Focus_recusa_nomeia_Peca_copiada_do_catalogo()
  {
    // As duas Pecas nao tem descricao propria: a frase traz a do Componente, nunca "no {id}".
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2);
    c.Execucao.StatusDoPedido[PedidoId2] = "Concluido";

    var fechado = await Expedir(c, (Peca, 1m), (OutraPeca, 1m));
    var acima = await Expedir(c, (Peca, 9m));

    Assert.Equal(CodigosDaExecucao.PedidoFechado, fechado.Erro);
    Assert.Contains(NomeDaOutra, fechado.Detalhe);
    Assert.Equal(CodigosDaExecucao.SaldoInsuficiente, acima.Erro);
    Assert.Contains(NomeDaPeca, acima.Detalhe);
    Assert.All(new[] { fechado.Detalhe!, acima.Detalhe! }, frase =>
    {
      Assert.DoesNotContain($"no {Peca}", frase);
      Assert.DoesNotContain($"no {OutraPeca}", frase);
    });
  }

  [Fact]
  public async Task Review_Focus_Pedido_pausado_expede()
  {
    // A pausa recusa so o inicio (regra 31): a remessa continua.
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    c.Execucao.Pausas.Add(new PedidoPausa
    {
      Id = 1, PedidoId = PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = Pcp, Motivo = "Cliente pediu",
    });

    var r = await Expedir(c, (Peca, 3m));

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(3m, c.Calcular().Saldo(Peca, Local.Expedido));
  }

  [Fact]
  public async Task Expedir_em_Pedido_Concluido_e_recusado()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var concluido = await Expedir(c, (Peca, 1m));
    c.Execucao.StatusDoPedido[PedidoId] = "Cancelado";
    var cancelado = await Expedir(c, (Peca, 1m));

    Assert.All(new[] { concluido, cancelado }, r =>
    {
      Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
      Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    });
    Assert.Empty(c.Expedicao.Remessas);
  }

  [Fact]
  public async Task Ultima_remessa_conclui_Peca_Agrupamento_e_Pedido()
  {
    var c = Cenario();
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2);

    var primeira = await Expedir(c, (OutraPeca, 1m));
    Assert.True(primeira.Sucesso, primeira.Detalhe);
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId2]);
    Assert.Null(c.Execucao.DataConclusaoDoPedido.GetValueOrDefault(PedidoId2));
    Assert.False(Assert.Single(primeira.Valor!.Remessa.Linhas).PedidoConcluido);

    var ultima = await Expedir(c, (OutraPeca, 3m));

    Assert.True(ultima.Sucesso, ultima.Detalhe);
    var remessa = c.Expedicao.Remessas.Single(x => x.Id == ultima.Valor!.Remessa.Id);
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId2]);
    Assert.Equal(remessa.DataHora, c.Execucao.DataConclusaoDoPedido[PedidoId2]);
    Assert.Equal(remessa.DataHora, c.Execucao.DataConclusaoDoAgrupamento[AgrupamentoId2]);
    Assert.True(Assert.Single(ultima.Valor!.Remessa.Linhas).PedidoConcluido);   // o status de agora
  }

  // ---------------------------------------------------------------- estornar

  /// <summary>A Peca 3 (Pedido 2) expedida inteira numa remessa, que concluiu o Pedido.</summary>
  private static async Task<(CenarioDeExecucao C, int RemessaId)> ConcluidoAsync()
  {
    var c = Cenario();
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2, requerRelatorio: true);
    Assert.True((await c.Afericao().Aferir(OutraPeca, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct)).Sucesso);
    c.Mover(OutraPeca, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.Conforme, 3m, Qualidade);
    var r = await Expedir(c, (OutraPeca, 4m));
    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId2]);
    return (c, r.Valor!.Remessa.Id);
  }

  [Fact]
  public async Task Estornar_a_remessa_que_concluiu_reabre_o_Pedido()
  {
    var (c, remessaId) = await ConcluidoAsync();
    var originais = LinhasDeExpedicao(c);

    var r = await c.Remessa().Estornar(remessaId, Qualidade, podeEstornarDeOutros: false, Ct);

    // C13: aceito com o Pedido Concluido, e o recalculo o devolve a AguardandoExpedicao.
    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal("AguardandoExpedicao", c.Execucao.StatusDoPedido[PedidoId2]);
    Assert.Null(c.Execucao.DataConclusaoDoPedido[PedidoId2]);
    Assert.Null(c.Execucao.DataConclusaoDoAgrupamento[AgrupamentoId2]);

    var estornos = c.Execucao.Movimentacoes.Where(m => m.Tipo == TiposDeMovimentacao.Estorno).ToList();
    Assert.Equal(originais.Select(o => (int?)o.Id), estornos.Select(e => e.EstornoDeId));
    Assert.All(estornos, e => Assert.Equal((Posicoes.Expedido, (int?)remessaId, Qualidade), (e.OrigemPosicao, e.RemessaId, e.UsuarioId)));
    Assert.Equal(new[] { (Posicoes.Conforme, 3m), (Posicoes.NaExpedicao, 1m) }, estornos.Select(e => (e.DestinoPosicao, e.Quantidade)));
    var calc = c.Calcular();
    Assert.Equal(0m, calc.Saldo(OutraPeca, Local.Expedido));
    Assert.Equal(3m, calc.Saldo(OutraPeca, Local.Conforme));
    Assert.Equal(1m, calc.Saldo(OutraPeca, Local.NaExpedicao));

    Assert.Equal(estornos.Select(e => e.Id), r.Valor!.Select(d => d.Id));
    Assert.All(r.Valor!, d => Assert.Equal((TiposDeMovimentacao.Estorno, (int?)remessaId), (d.Tipo, d.RemessaId)));
  }

  [Fact]
  public async Task Estornar_remessa_duas_vezes_e_recusado()
  {
    var (c, remessaId) = await ConcluidoAsync();
    Assert.True((await c.Remessa().Estornar(remessaId, Qualidade, false, Ct)).Sucesso);

    var segunda = await c.Remessa().Estornar(remessaId, Qualidade, false, Ct);

    Assert.Equal(CodigosDaExecucao.JaEstornado, segunda.Erro);
    Assert.Equal(TipoDeErro.Conflito, segunda.TipoDoErro);
    Assert.Equal(2, c.Execucao.Movimentacoes.Count(m => m.Tipo == TiposDeMovimentacao.Estorno));
  }

  [Fact]
  public async Task Estornar_remessa_alheia_sem_ser_PCP_e_proibido()
  {
    var (c, remessaId) = await ConcluidoAsync();

    var alheia = await c.Remessa().Estornar(remessaId, Gestao, podeEstornarDeOutros: false, Ct);
    var inexistente = await c.Remessa().Estornar(999_999, Qualidade, false, Ct);

    Assert.Equal(CodigosDaExecucao.Proibido, alheia.Erro);
    Assert.Equal(TipoDeErro.Proibido, alheia.TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, inexistente.TipoDoErro);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);

    // O PCP estorna a de outro (C7), e o estorno leva o nome dele.
    var doPcp = await c.Remessa().Estornar(remessaId, Pcp, podeEstornarDeOutros: true, Ct);
    Assert.True(doPcp.Sucesso, doPcp.Detalhe);
    Assert.All(doPcp.Valor!, d => Assert.Equal((Pcp, "PCP"), (d.UsuarioId, d.UsuarioNome)));
  }

  [Fact]
  public async Task Estornar_remessa_de_Pedido_Cancelado_e_recusado()
  {
    var (c, remessaId) = await ConcluidoAsync();
    c.Execucao.StatusDoPedido[PedidoId2] = "Cancelado";

    var r = await c.Remessa().Estornar(remessaId, Qualidade, false, Ct);

    Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("PED-02", r.Detalhe);
    Assert.DoesNotContain(c.Execucao.Movimentacoes, m => m.Tipo == TiposDeMovimentacao.Estorno);
    Assert.Equal("Cancelado", c.Execucao.StatusDoPedido[PedidoId2]);
  }

  [Fact]
  public async Task Estornar_remessa_de_duas_Pecas_reabre_os_dois_Pedidos()
  {
    var c = Cenario();
    PecaNaExpedicao(c, Peca, 3m);
    PecaNaExpedicao(c, OutraPeca, 4m, AgrupamentoId2);
    var r = await Expedir(c, (Peca, 3m), (OutraPeca, 4m));
    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(("Concluido", "Concluido"), (c.Execucao.StatusDoPedido[PedidoId], c.Execucao.StatusDoPedido[PedidoId2]));
    c.Execucao.Travas.Clear();
    c.Execucao.TravasDePedido.Clear();

    var estorno = await c.Remessa().Estornar(r.Valor!.Remessa.Id, Qualidade, false, Ct);

    Assert.True(estorno.Sucesso, estorno.Detalhe);
    Assert.Equal(2, estorno.Valor!.Count);
    Assert.Equal(("AguardandoExpedicao", "AguardandoExpedicao"),
        (c.Execucao.StatusDoPedido[PedidoId], c.Execucao.StatusDoPedido[PedidoId2]));
    Assert.Equal(new[] { Peca, OutraPeca }, Assert.Single(c.Execucao.Travas));
    Assert.Equal(new[] { Peca, OutraPeca }, Assert.Single(c.Execucao.TravasDePedido));   // os Pedidos delas
    Assert.Equal(new[] { "nos:1,3", "pedidos:1,3" }, c.Execucao.Eventos.Where(e => e.StartsWith("nos:") || e.StartsWith("pedidos:")).TakeLast(2));
  }
}
