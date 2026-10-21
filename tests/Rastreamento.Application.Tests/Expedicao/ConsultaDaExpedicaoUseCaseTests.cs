using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// As leituras da expedicao (secao 5.1 e C20 da spec da Fase 5A): a lista da Qualidade, o historico da Peca, os
/// pedidos de liberacao pendentes e as remessas recentes. Cenario: dois Pedidos `EmProducao`, uma Peca por vez
/// COPIADA DO CATALOGO (sem descricao propria: o nome e o do Componente, regra 19; a Peca 1 e a "Chapa dobrada
/// 3 mm"), levada ao local de expedicao por arranjo direto no livro. O "hoje" de Brasilia e 2026-10-20.
/// </summary>
public class ConsultaDaExpedicaoUseCaseTests
{
  private const int Peca1 = 1, Peca3 = 3, Peca4 = 4, Peca5 = 5, Peca6 = 6, Filho = 9;
  private const string NomeDaPeca1 = "Chapa dobrada 3 mm";
  private static readonly CancellationToken Ct = CancellationToken.None;
  private static readonly DateTime T0 = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
  private static readonly QuemLe ComoQualidade = new(Qualidade, false);

  private static CenarioDeExecucao Cenario()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Execucao.StatusDoPedido[PedidoId2] = "EmProducao";
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 30);
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 22);
    return c;
  }

  /// <summary>
  /// Uma Peca de `quantidade`, copiada do catalogo (a Peca 1 e a "Chapa dobrada 3 mm", `CHP-220`; as demais sao
  /// "Componente N"), a menos que `descricaoPropria` a nomeie.
  /// </summary>
  private static void Peca(
      CenarioDeExecucao c, int id, decimal quantidade, int agrupamento = AgrupamentoId, bool requerRelatorio = false,
      string? descricaoPropria = null)
  {
    c.NoDoAgrupamento(agrupamento, id, null, quantidade, null, Corte);
    var (componenteId, codigo, descricao) =
        id == Peca1 ? (70, "CHP-220", NomeDaPeca1) : (100 + id, $"CMP-{id:000}", $"Componente {id}");
    var peca = c.Estruturas.Itens.Single(i => i.Id == id);
    peca.Descricao = descricaoPropria;
    peca.ComponenteId = componenteId;
    peca.RequerRelatorioDimensional = requerRelatorio;
    c.Catalogo.Componentes.Add(
        new Componente { Id = componenteId, Codigo = codigo, Descricao = descricao, Tipo = "Fabricado", Ativo = true });
  }

  /// <summary>Leva `quantidade` (a Peca inteira) ao local de expedicao, em `NaExpedicao`.</summary>
  private static void NaExpedicao(CenarioDeExecucao c, int id, decimal quantidade)
  {
    c.Mover(id, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, quantidade, Movimentador);
  }

  /// <summary>Uma linha do livro com o vinculo ao registro e o instante escolhidos.</summary>
  private static Movimentacao Linha(
      CenarioDeExecucao c, int peca, string tipo, Local de, Local para, decimal quantidade, int usuario, DateTime dataHora,
      int? contagem = null, int? liberacao = null, int? remessa = null, int? estornoDe = null)
  {
    var linha = c.Mover(peca, tipo, de, para, quantidade, usuario);
    linha.DataHora = dataHora;
    linha.ContagemId = contagem;
    linha.LiberacaoId = liberacao;
    linha.RemessaId = remessa;
    linha.EstornoDeId = estornoDe;
    return linha;
  }

  private static async Task<ExpedicaoDto> ListaAsync(CenarioDeExecucao c, QuemLe? quem = null)
  {
    var r = await c.ConsultaDaExpedicao().Lista(quem ?? ComoQualidade, Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return r.Valor!;
  }

  // ---------------------------------------------------------------- lista

  [Fact]
  public async Task Lista_traz_so_Pecas_com_algo_no_local_de_expedicao_ou_em_conserto()
  {
    var c = Cenario();
    Peca(c, Peca1, 5m);                      // na expedicao
    NaExpedicao(c, Peca1, 5m);
    Peca(c, Peca4, 4m);                      // so com quantidade em conserto
    NaExpedicao(c, Peca4, 4m);
    c.Mover(Peca4, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 4m, Qualidade);
    c.Mover(Peca4, TiposDeMovimentacao.Conserto, Local.NaoConforme, Local.AguardandoConserto(Dobra), 4m, Qualidade);
    Peca(c, Peca5, 3m);                      // ja expedida inteira
    NaExpedicao(c, Peca5, 3m);
    c.Mover(Peca5, TiposDeMovimentacao.Expedicao, Local.NaExpedicao, Local.Expedido, 3m, Qualidade);
    Peca(c, Peca6, 2m);                      // ainda a iniciar
    c.NoDoAgrupamento(AgrupamentoId, Filho, Peca1, 5m, 1m, Corte);   // um Item nunca entra

    var lista = await ListaAsync(c);

    var pedido = Assert.Single(lista.Pedidos);
    Assert.Equal(new[] { Peca1, Peca4 }, pedido.Pecas.Select(p => p.Id));
  }

  [Fact]
  public async Task Lista_agrupa_por_Pedido_na_ordem_da_data_de_entrega()
  {
    var c = Cenario();   // Pedido 1 entrega em 30/10; Pedido 2, em 22/10
    c.Execucao.ClienteDoPedido[PedidoId] = "Cliente A";
    c.Execucao.ClienteDoPedido[PedidoId2] = "Cliente B";
    Peca(c, Peca4, 2m);
    Peca(c, Peca1, 2m);
    Peca(c, Peca3, 2m, AgrupamentoId2);
    foreach (var id in new[] { Peca4, Peca1, Peca3 }) NaExpedicao(c, id, 2m);
    c.Execucao.Pausas.Add(new PedidoPausa
    {
      Id = 1, PedidoId = PedidoId, PausadoEm = T0, PausadoPorUsuarioId = Pcp, Motivo = "Falta de material",
    });

    var lista = await ListaAsync(c);

    Assert.Equal(new[] { PedidoId2, PedidoId }, lista.Pedidos.Select(p => p.Id));
    var primeiro = lista.Pedidos[0];
    Assert.Equal(("PED-02", "Cliente B", new DateOnly(2026, 10, 22), false), (primeiro.Numero, primeiro.Cliente, primeiro.DataEntrega, primeiro.Atrasado));
    Assert.Null(primeiro.Pausa);
    Assert.Equal(new[] { Peca3 }, primeiro.Pecas.Select(p => p.Id));
    var segundo = lista.Pedidos[1];
    // Pecas do mesmo Pedido por Id, nao pela ordem em que entraram no cenario.
    Assert.Equal(new[] { Peca1, Peca4 }, segundo.Pecas.Select(p => p.Id));
    Assert.Equal("Falta de material", segundo.Pausa!.Motivo);
    Assert.Equal((AgrupamentoId, "AG-01"), (segundo.Pecas[0].AgrupamentoId, segundo.Pecas[0].AgrupamentoCodigo));
  }

  [Fact]
  public async Task Lista_marca_o_Pedido_atrasado_pelo_dia_de_Brasilia()
  {
    var c = Cenario();
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 19);   // ontem em Brasilia
    Peca(c, Peca1, 2m);
    NaExpedicao(c, Peca1, 2m);

    var lista = await ListaAsync(c);

    Assert.True(Assert.Single(lista.Pedidos).Atrasado);
  }

  [Fact]
  public async Task Lista_traz_situacao_posicoes_consertos_e_expedivel()
  {
    var c = Cenario();
    Peca(c, Peca1, 10m, requerRelatorio: true);
    NaExpedicao(c, Peca1, 10m);
    Assert.True((await c.Afericao().Aferir(Peca1, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct)).Sucesso);
    c.Mover(Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.Conforme, 4m, Qualidade);
    c.Mover(Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 3m, Qualidade);
    c.Mover(Peca1, TiposDeMovimentacao.Conserto, Local.NaoConforme, Local.AguardandoConserto(Dobra), 1m, Qualidade);
    c.Mover(Peca1, TiposDeMovimentacao.PedidoDeLiberacao, Local.NaoConforme, Local.AguardandoLiberacao, 1m, Qualidade);

    var lista = await ListaAsync(c);

    var peca = Assert.Single(Assert.Single(lista.Pedidos).Pecas);
    Assert.Equal((NomeDaPeca1, "CHP-220", 10m, true), (peca.Descricao, peca.CodigoDoComponente, peca.Quantidade, peca.RequerRelatorioDimensional));
    Assert.Equal("NaoConforme", peca.Situacao);
    Assert.Equal(new PosicoesDaPecaDto(3m, 4m, 1m, 1m, 0m, 1m, 0m), peca.Posicoes);
    // O que a remessa pode tirar: tudo do local de expedicao menos o que espera a Gestao.
    Assert.Equal(8m, peca.Expedivel);
    var conserto = Assert.Single(peca.Consertos);
    Assert.Equal((Dobra, "Dobra", Posicoes.AguardandoConserto, 1m), (conserto.SetorId, conserto.SetorNome, conserto.Posicao, conserto.Quantidade));
    var afericao = peca.Afericao!;
    Assert.Equal((ResultadosDeAfericao.NaoConforme, Qualidade, "Qualidade", false, true),
        (afericao.Resultado, afericao.UsuarioId, afericao.UsuarioNome, afericao.Estornada, afericao.Estornavel));
    Assert.Null(peca.LiberacaoPendente);

    // O autor ou PCP/Administrador estorna a afericao; o Operador nao.
    var comoOperador = Assert.Single(Assert.Single((await ListaAsync(c, ComoOperador)).Pedidos).Pecas);
    Assert.False(comoOperador.Afericao!.Estornavel);
  }

  [Fact]
  public async Task Lista_diz_SemRelatorio_e_Pendente()
  {
    var c = Cenario();
    Peca(c, Peca1, 2m);                          // nao exige relatorio
    Peca(c, Peca4, 2m, requerRelatorio: true);   // exige e nao tem afericao
    NaExpedicao(c, Peca1, 2m);
    NaExpedicao(c, Peca4, 2m);

    var pecas = Assert.Single((await ListaAsync(c)).Pedidos).Pecas;

    Assert.Equal(new[] { "SemRelatorio", "Pendente" }, pecas.Select(p => p.Situacao));
    Assert.All(pecas, p => Assert.Null(p.Afericao));
  }

  [Fact]
  public async Task Lista_traz_a_liberacao_pendente()
  {
    var c = Cenario();
    Peca(c, Peca1, 5m, requerRelatorio: true);
    NaExpedicao(c, Peca1, 5m);
    Assert.True((await c.Afericao().Aferir(Peca1, new NovaAfericaoDto(ResultadosDeAfericao.NaoConforme), Qualidade, Ct)).Sucesso);
    c.Mover(Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 3m, Qualidade);
    var pedido = await c.Liberacao().Pedir(Peca1, new NovoPedidoDeLiberacaoDto(2m, "Aba da dobra 1,5 mm fora"), Qualidade, Ct);
    Assert.True(pedido.Sucesso, pedido.Detalhe);

    var pendente = Assert.Single(Assert.Single((await ListaAsync(c)).Pedidos).Pecas).LiberacaoPendente!;

    Assert.Equal((pedido.Valor!.Id, 2m, "Aba da dobra 1,5 mm fora", "Qualidade"),
        (pendente.Id, pendente.Quantidade, pendente.Justificativa, pendente.UsuarioNome));
    Assert.Equal(pedido.Valor.DataHora, pendente.DataHora);

    // Respondido, deixa de estar pendente.
    Assert.True((await c.Liberacao().Responder(pedido.Valor.Id, new RespostaDeLiberacaoDto(2m, null), Gestao, Ct)).Sucesso);
    Assert.Null(Assert.Single(Assert.Single((await ListaAsync(c)).Pedidos).Pecas).LiberacaoPendente);
  }

  [Fact]
  public async Task Lista_nao_traz_Pedido_concluido()
  {
    var c = Cenario();
    Peca(c, Peca1, 2m);
    Peca(c, Peca3, 2m, AgrupamentoId2);
    NaExpedicao(c, Peca1, 2m);
    NaExpedicao(c, Peca3, 2m);
    c.Execucao.StatusDoPedido[PedidoId2] = "Concluido";

    var lista = await ListaAsync(c);

    Assert.Equal(PedidoId, Assert.Single(lista.Pedidos).Id);
  }

  [Fact]
  public async Task Review_Focus_lista_nomeia_Peca_copiada_do_catalogo()
  {
    var c = Cenario();
    Peca(c, Peca1, 2m);                                                   // sem descricao propria: a do Componente
    Peca(c, Peca4, 2m, descricaoPropria: "Chapa com nome próprio");       // com descricao propria: a do no
    NaExpedicao(c, Peca1, 2m);
    NaExpedicao(c, Peca4, 2m);

    var pecas = Assert.Single((await ListaAsync(c)).Pedidos).Pecas;

    Assert.Equal((NomeDaPeca1, "CHP-220"), (pecas[0].Descricao, pecas[0].CodigoDoComponente));
    Assert.Equal(("Chapa com nome próprio", "CMP-004"), (pecas[1].Descricao, pecas[1].CodigoDoComponente));
  }

  // ---------------------------------------------------------------- historico

  /// <summary>
  /// O historico completo da Peca 1, instante por instante: afericao 1 (estornada pelo estorno 2), afericao 3, contagem
  /// 1 (3 conformes, 2 nao), conserto, pedido de liberacao 1 (Qualidade), resposta 2 (Gestao) e remessa 1
  /// (duas linhas, 3 unidades).
  /// </summary>
  private static (CenarioDeExecucao C, Movimentacao Conserto) HistoricoSemeado()
  {
    var c = Cenario();
    Peca(c, Peca1, 10m, requerRelatorio: true);
    NaExpedicao(c, Peca1, 10m);
    c.Expedicao.Afericoes.Add(new RelatorioDimensional
    {
      Id = 1, EstruturaItemId = Peca1, Tipo = TiposDeAfericao.Afericao, Resultado = ResultadosDeAfericao.NaoConforme,
      DataHora = T0, UsuarioId = Qualidade,
    });
    c.Expedicao.Afericoes.Add(new RelatorioDimensional
    {
      Id = 2, EstruturaItemId = Peca1, Tipo = TiposDeAfericao.Estorno, EstornoDeId = 1,
      DataHora = T0.AddMinutes(1), UsuarioId = Qualidade,
    });
    c.Expedicao.Afericoes.Add(new RelatorioDimensional
    {
      Id = 3, EstruturaItemId = Peca1, Tipo = TiposDeAfericao.Afericao, Resultado = ResultadosDeAfericao.NaoConforme,
      DataHora = T0.AddMinutes(2), UsuarioId = Qualidade,
    });

    c.Expedicao.Contagens.Add(new ContagemDimensional
    {
      Id = 1, EstruturaItemId = Peca1, Conformes = 3m, NaoConformes = 2m, DataHora = T0.AddMinutes(3), UsuarioId = Qualidade,
    });
    Linha(c, Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.Conforme, 3m, Qualidade, T0.AddMinutes(3), contagem: 1);
    Linha(c, Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 2m, Qualidade, T0.AddMinutes(3), contagem: 1);

    var conserto = Linha(c, Peca1, TiposDeMovimentacao.Conserto, Local.NaoConforme, Local.AguardandoConserto(Dobra), 1m,
        Qualidade, T0.AddMinutes(4));

    c.Expedicao.Liberacoes.Add(new LiberacaoDimensional
    {
      Id = 1, EstruturaItemId = Peca1, Tipo = TiposDeLiberacao.Pedido, Quantidade = 1m, Texto = "Aba fora",
      DataHora = T0.AddMinutes(5), UsuarioId = Qualidade,
    });
    Linha(c, Peca1, TiposDeMovimentacao.PedidoDeLiberacao, Local.NaoConforme, Local.AguardandoLiberacao, 1m, Qualidade,
        T0.AddMinutes(5), liberacao: 1);
    c.Expedicao.Liberacoes.Add(new LiberacaoDimensional
    {
      Id = 2, EstruturaItemId = Peca1, Tipo = TiposDeLiberacao.Resposta, PedidoDeLiberacaoId = 1, Quantidade = 1m,
      Texto = "Pode ir", DataHora = T0.AddMinutes(6), UsuarioId = Gestao,
    });
    Linha(c, Peca1, TiposDeMovimentacao.Liberacao, Local.AguardandoLiberacao, Local.Liberada, 1m, Gestao,
        T0.AddMinutes(6), liberacao: 2);

    c.Expedicao.Remessas.Add(new Remessa { Id = 1, DataHora = T0.AddMinutes(7), UsuarioId = Qualidade });
    Linha(c, Peca1, TiposDeMovimentacao.Expedicao, Local.Liberada, Local.Expedido, 1m, Qualidade, T0.AddMinutes(7), remessa: 1);
    Linha(c, Peca1, TiposDeMovimentacao.Expedicao, Local.Conforme, Local.Expedido, 2m, Qualidade, T0.AddMinutes(7), remessa: 1);
    return (c, conserto);
  }

  private static async Task<IReadOnlyList<RegistroDaExpedicaoDto>> HistoricoAsync(CenarioDeExecucao c, QuemLe quem)
  {
    var r = await c.ConsultaDaExpedicao().Historico(Peca1, quem, Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return r.Valor!;
  }

  [Fact]
  public async Task Historico_traz_todos_os_registros_mais_recente_primeiro()
  {
    var (c, conserto) = HistoricoSemeado();

    var historico = await HistoricoAsync(c, ComoQualidade);

    // O estorno da afericao nao e um registro a parte: so marca a afericao 1 como estornada.
    Assert.Equal(
        new[] { "Remessa", "RespostaDeLiberacao", "PedidoDeLiberacao", "Conserto", "Contagem", "Afericao", "Afericao" },
        historico.Select(h => h.Tipo));
    Assert.Equal(new[] { 1, 2, 1, conserto.Id, 1, 3, 1 }, historico.Select(h => h.Id));
    Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 0 }, historico.Select(h => (int)(h.DataHora - T0).TotalMinutes));

    var remessa = historico[0];
    Assert.Equal((3m, "Qualidade"), (remessa.Quantidade, remessa.UsuarioNome));
    var resposta = historico[1];
    Assert.Equal((1m, "Pode ir", Gestao, "Gestão"), (resposta.Quantidade, resposta.Texto, resposta.UsuarioId, resposta.UsuarioNome));
    var pedido = historico[2];
    Assert.Equal((1m, "Aba fora"), (pedido.Quantidade, pedido.Texto));
    var consertoDto = historico[3];
    Assert.Equal((1m, "Dobra"), (consertoDto.Quantidade, consertoDto.SetorNome));
    var contagem = historico[4];
    Assert.Equal((3m, 2m), (contagem.Conformes, contagem.NaoConformes));
    Assert.Equal((ResultadosDeAfericao.NaoConforme, Qualidade), (historico[5].Resultado, historico[5].UsuarioId));
    Assert.All(historico.Where(h => h.Tipo != "Afericao"), h => Assert.Null(h.Resultado));
  }

  [Fact]
  public async Task Historico_marca_estornavel_so_para_o_autor_ou_PCP()
  {
    var (c, _) = HistoricoSemeado();

    static IReadOnlyList<bool> Estornaveis(IReadOnlyList<RegistroDaExpedicaoDto> h) => h.Select(h => h.Estornavel).ToList();

    // Ordem: Remessa, Resposta (da Gestao), Pedido, Conserto, Contagem, Afericao 3, Afericao 1 (estornada).
    // A Remessa nunca e estornavel aqui (C20: so em "Remessas recentes"), nem para quem a fez.
    Assert.Equal(new[] { false, false, true, true, true, true, false }, Estornaveis(await HistoricoAsync(c, ComoQualidade)));
    Assert.Equal(new[] { false, false, false, false, false, false, false }, Estornaveis(await HistoricoAsync(c, ComoOperador)));
    Assert.Equal(new[] { false, true, true, true, true, true, false }, Estornaveis(await HistoricoAsync(c, ComoPcp)));
    Assert.Equal(new[] { false, true, false, false, false, false, false },
        Estornaveis(await HistoricoAsync(c, new QuemLe(Gestao, false))));

    var historico = await HistoricoAsync(c, ComoPcp);
    Assert.Equal(new[] { false, false, false, false, false, false, true }, historico.Select(h => h.Estornada));
  }

  [Fact]
  public async Task Historico_marca_estornado_o_registro_de_linhas_estornadas()
  {
    var (c, conserto) = HistoricoSemeado();
    var linhaDaContagem = c.Execucao.Movimentacoes.Where(m => m.ContagemId == 1).ToList();
    foreach (var original in linhaDaContagem)
      Linha(c, Peca1, TiposDeMovimentacao.Estorno, Local.DoDestino(original), Local.DaOrigem(original), original.Quantidade,
          Qualidade, T0.AddMinutes(8), contagem: 1, estornoDe: original.Id);
    Linha(c, Peca1, TiposDeMovimentacao.Estorno, Local.AguardandoConserto(Dobra), Local.NaoConforme, 1m, Qualidade,
        T0.AddMinutes(8), estornoDe: conserto.Id);

    var historico = await HistoricoAsync(c, ComoPcp);

    Assert.Equal(("Contagem", true, false), (historico[4].Tipo, historico[4].Estornada, historico[4].Estornavel));
    Assert.Equal(("Conserto", true, false), (historico[3].Tipo, historico[3].Estornada, historico[3].Estornavel));
    Assert.False(historico[2].Estornada);
  }

  [Fact]
  public async Task Historico_de_no_inexistente_e_404()
  {
    var r = await Cenario().ConsultaDaExpedicao().Historico(999, ComoQualidade, Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- pendentes

  [Fact]
  public async Task Pendentes_traz_so_o_pedido_sem_resposta_valida()
  {
    var c = Cenario();   // Pedido 1 entrega 30/10; Pedido 2, 22/10
    c.Execucao.ClienteDoPedido[PedidoId] = "Cliente A";
    Peca(c, Peca1, 5m, requerRelatorio: true);
    Peca(c, Peca3, 5m, AgrupamentoId2);
    Peca(c, Peca4, 5m);
    Peca(c, Peca5, 5m);
    foreach (var id in new[] { Peca1, Peca3, Peca4, Peca5 })
    {
      NaExpedicao(c, id, 5m);
      c.Mover(id, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 5m, Qualidade);
    }
    var emAberto1 = (await c.Liberacao().Pedir(Peca1, new NovoPedidoDeLiberacaoDto(2m, "Primeira"), Qualidade, Ct)).Valor!;
    var respondido = (await c.Liberacao().Pedir(Peca4, new NovoPedidoDeLiberacaoDto(1m, "Respondida"), Qualidade, Ct)).Valor!;
    Assert.True((await c.Liberacao().Responder(respondido.Id, new RespostaDeLiberacaoDto(1m, null), Gestao, Ct)).Sucesso);
    var estornado = (await c.Liberacao().Pedir(Peca5, new NovoPedidoDeLiberacaoDto(1m, "Estornada"), Qualidade, Ct)).Valor!;
    Assert.True((await c.EstornoDaExpedicao().EstornarLiberacao(estornado.Id, Qualidade, false, Ct)).Sucesso);
    var emAberto2 = (await c.Liberacao().Pedir(Peca3, new NovoPedidoDeLiberacaoDto(3m, "Segunda"), Qualidade, Ct)).Valor!;

    var r = await c.ConsultaDaExpedicao().LiberacoesPendentes(Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    // Mais antigo primeiro.
    Assert.Equal(new[] { emAberto1.Id, emAberto2.Id }, r.Valor!.Select(p => p.Id));
    var primeira = r.Valor![0];
    Assert.Equal((2m, "Primeira", emAberto1.DataHora, "Qualidade"),
        (primeira.Quantidade, primeira.Justificativa, primeira.DataHora, primeira.UsuarioNome));
    Assert.Equal((Peca1, NomeDaPeca1, "CHP-220"), (primeira.Peca.Id, primeira.Peca.Descricao, primeira.Peca.CodigoDoComponente));
    Assert.Equal((PedidoId, "PED-01", "Cliente A", new DateOnly(2026, 10, 30), false),
        (primeira.Pedido.Id, primeira.Pedido.Numero, primeira.Pedido.Cliente, primeira.Pedido.DataEntrega, primeira.Pedido.Atrasado));
  }

  [Fact]
  public async Task Pendentes_nao_traz_pedido_de_Pedido_concluido()
  {
    var c = Cenario();
    Peca(c, Peca1, 5m);
    NaExpedicao(c, Peca1, 5m);
    c.Mover(Peca1, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 5m, Qualidade);
    Assert.True((await c.Liberacao().Pedir(Peca1, new NovoPedidoDeLiberacaoDto(1m, "x"), Qualidade, Ct)).Sucesso);
    c.Execucao.StatusDoPedido[PedidoId] = "Concluido";

    var r = await c.ConsultaDaExpedicao().LiberacoesPendentes(Ct);

    Assert.Empty(r.Valor!);
  }

  // ---------------------------------------------------------------- remessas recentes

  private static Remessa NovaRemessa(CenarioDeExecucao c, int id, DateTime dataHora, int usuario, int peca, decimal quantidade)
  {
    var remessa = new Remessa { Id = id, DataHora = dataHora, UsuarioId = usuario };
    c.Expedicao.Remessas.Add(remessa);
    Linha(c, peca, TiposDeMovimentacao.Expedicao, Local.NaExpedicao, Local.Expedido, quantidade, usuario, dataHora, remessa: id);
    return remessa;
  }

  [Fact]
  public async Task Remessas_recentes_traz_as_dos_ultimos_30_dias_com_as_linhas()
  {
    Assert.Equal(30, ConsultaDaExpedicaoUseCase.DiasDeRemessasRecentes);
    var c = Cenario();
    Peca(c, Peca1, 9m);
    Peca(c, Peca3, 9m, AgrupamentoId2);
    var agora = DateTime.UtcNow;
    NovaRemessa(c, 1, agora.AddDays(-31), Qualidade, Peca1, 1m);       // fora da janela
    NovaRemessa(c, 2, agora.AddDays(-29), Qualidade, Peca1, 2m);
    NovaRemessa(c, 3, agora.AddHours(-1), Qualidade, Peca3, 4m);

    var r = await c.ConsultaDaExpedicao().RemessasRecentes(ComoQualidade, Ct);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(new[] { 3, 2 }, r.Valor!.Select(x => x.Id));   // mais recente primeiro
    var linha = Assert.Single(r.Valor![0].Linhas);
    Assert.Equal((Peca3, "Componente 3", "CMP-003", PedidoId2, "PED-02", false, Posicoes.NaExpedicao, 4m),
        (linha.PecaId, linha.PecaDescricao, linha.CodigoDoComponente, linha.PedidoId, linha.PedidoNumero,
            linha.PedidoConcluido, linha.Origem, linha.Quantidade));
    Assert.Equal((Peca1, NomeDaPeca1, 2m), (r.Valor[1].Linhas[0].PecaId, r.Valor[1].Linhas[0].PecaDescricao, r.Valor[1].Linhas[0].Quantidade));
  }

  [Fact]
  public async Task Remessas_recentes_marca_estornavel_e_estornada()
  {
    var c = Cenario();
    Peca(c, Peca1, 9m);
    var agora = DateTime.UtcNow;
    NovaRemessa(c, 1, agora.AddHours(-3), Qualidade, Peca1, 1m);
    NovaRemessa(c, 2, agora.AddHours(-2), Qualidade, Peca1, 2m);
    NovaRemessa(c, 3, agora.AddHours(-1), Pcp, Peca1, 3m);
    var daSegunda = c.Execucao.Movimentacoes.Single(m => m.RemessaId == 2);
    Linha(c, Peca1, TiposDeMovimentacao.Estorno, Local.Expedido, Local.NaExpedicao, 2m, Qualidade, agora, remessa: 2,
        estornoDe: daSegunda.Id);

    var comoQualidade = (await c.ConsultaDaExpedicao().RemessasRecentes(ComoQualidade, Ct)).Valor!;
    var comoPcp = (await c.ConsultaDaExpedicao().RemessasRecentes(ComoPcp, Ct)).Valor!;

    // Mais recente primeiro: 3, 2, 1. A estornada nao se estorna de novo; a alheia so o PCP estorna.
    Assert.Equal(new[] { 3, 2, 1 }, comoQualidade.Select(x => x.Id));
    Assert.Equal(new[] { false, false, true }, comoQualidade.Select(x => x.Estornavel));
    Assert.Equal(new[] { false, true, false }, comoQualidade.Select(x => x.Estornada));
    Assert.Equal(new[] { true, false, true }, comoPcp.Select(x => x.Estornavel));
    // O estorno nao vira linha da remessa.
    Assert.All(comoQualidade, x => Assert.Single(x.Linhas));
  }

  [Fact]
  public async Task Remessas_recentes_marca_o_Pedido_que_a_remessa_concluiu()
  {
    var c = Cenario();
    Peca(c, Peca1, 3m);
    NaExpedicao(c, Peca1, 3m);
    var expedida = await c.Remessa().Expedir(new NovaRemessaDto([new ItemDaRemessaDto(Peca1, 3m)]), Qualidade, Ct);
    Assert.True(expedida.Sucesso, expedida.Detalhe);
    Assert.Equal("Concluido", c.Execucao.StatusDoPedido[PedidoId]);

    var recentes = (await c.ConsultaDaExpedicao().RemessasRecentes(ComoQualidade, Ct)).Valor!;

    var linha = Assert.Single(Assert.Single(recentes).Linhas);
    Assert.True(linha.PedidoConcluido);
    // A tela usa isto para avisar que o estorno reabre o Pedido (C20): e estornavel mesmo com o Pedido concluido.
    Assert.True(recentes[0].Estornavel);
  }
}
