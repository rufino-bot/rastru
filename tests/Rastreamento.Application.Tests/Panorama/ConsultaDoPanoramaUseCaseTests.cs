using Rastreamento.Application.Execucao;
using Rastreamento.Application.Panorama;
using Rastreamento.Application.Tests.Arquivos;
using Rastreamento.Application.Tests.Common;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>
/// A leitura do Panorama (C2, C3, C8, C9 e a secao 5.1 da spec do Panorama). Cenario da execucao: dois Pedidos
/// `EmProducao`, cada no copiado de um Componente proprio (`CMP-{id}`, "Componente {id}"). O "hoje" de Brasilia
/// e 2026-10-20.
/// </summary>
public class ConsultaDoPanoramaUseCaseTests
{
  private const int Peca1 = 1, Peca2 = 2, Peca3 = 3, Peca4 = 4, Peca5 = 5, Peca6 = 6, PecaDoTerceiroPedido = 12;
  private const int Item7 = 7, Item8 = 8, Item9 = 9, Item10 = 10;
  private static readonly CancellationToken Ct = CancellationToken.None;
  private static readonly DateTime T0 = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

  private sealed record Montagem(CenarioDeExecucao Cenario, FakeEmbalagemRepo Embalagem, FakeArquivoDeComponenteRepo Arquivos);

  private static Montagem Montar()
  {
    var c = new CenarioDeExecucao();
    c.Execucao.StatusDoPedido[PedidoId] = "EmProducao";
    c.Execucao.StatusDoPedido[PedidoId2] = "EmProducao";
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 30);
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 22);
    return new Montagem(c, new FakeEmbalagemRepo(), new FakeArquivoDeComponenteRepo());
  }

  /// <summary>Um no (Peca, ou Item com `pai`) de `quantidade`, de um Componente proprio, com Roteiro no Corte.</summary>
  private static void No(
      CenarioDeExecucao c, int id, decimal quantidade, int agrupamento = AgrupamentoId, int? pai = null,
      string? codigo = null, bool requerRelatorio = false)
  {
    c.NoDoAgrupamento(agrupamento, id, pai, quantidade, pai is null ? null : 1m, Corte);
    var no = c.Estruturas.Itens.Single(i => i.Id == id);
    no.Descricao = null;
    no.ComponenteId = 100 + id;
    no.RequerRelatorioDimensional = requerRelatorio;
    c.Catalogo.Componentes.Add(new Componente
    {
      Id = 100 + id, Codigo = codigo ?? $"CMP-{id:000}", Descricao = $"Componente {id}", Tipo = "Fabricado", Ativo = true,
    });
  }

  private static async Task<PanoramaDto> ListaAsync(Montagem m)
  {
    var uc = new ConsultaDoPanoramaUseCase(
        m.Cenario.Execucao, m.Cenario.Expedicao, m.Cenario.Estruturas, m.Cenario.Catalogo, m.Embalagem, m.Arquivos,
        new RelogioFixo(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));
    var r = await uc.Lista(Ct);
    Assert.True(r.Sucesso, r.Detalhe);
    return r.Valor!;
  }

  /// <summary>Leva `quantidade` ao local de expedicao, em `NaExpedicao`.</summary>
  private static void NaExpedicao(CenarioDeExecucao c, int id, decimal quantidade)
  {
    c.Mover(id, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), quantidade);
    c.Mover(id, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, quantidade, Movimentador);
  }

  [Fact]
  public async Task Lista_peca_de_pedido_aberto_que_ninguem_comecou()
  {
    var m = Montar();
    m.Cenario.Execucao.StatusDoPedido[PedidoId] = "Aberto";
    No(m.Cenario, Peca1, 5m);

    var peca = Assert.Single((await ListaAsync(m)).Pecas);

    Assert.Equal((Peca1, AgrupamentoId, 5m), (peca.EstruturaItemId, peca.AgrupamentoId, peca.Quantidade));
    Assert.Equal((PedidoId, "PED-01", new DateOnly(2026, 10, 30), false, false), (
        peca.Pedido.Id, peca.Pedido.Numero, peca.Pedido.DataEntrega, peca.Pedido.Atrasado, peca.Pedido.Retrabalho));
    Assert.Equal((101, "CMP-001", "Componente 1", (string?)null), (
        peca.Componente.Id, peca.Componente.Codigo, peca.Componente.Descricao, peca.Componente.MiniaturaVersao));
    var segmento = Assert.Single(peca.Distribuicao);
    Assert.Equal((TiposDeSegmento.AIniciar, 5m), (segmento.Tipo, segmento.Quantidade));
    Assert.Empty(peca.ItensEm);
    Assert.False(peca.Embalagem.Vale);
    Assert.False(peca.Embalagem.PodeMarcar);
    Assert.Equal(5m, peca.Embalagem.EmProducao);
    Assert.Null(peca.Embalagem.Ultima);
    Assert.Equal((false, "SemRelatorio"), (peca.Afericao.ExigeRelatorio, peca.Afericao.Situacao));
  }

  [Fact]
  public async Task Peca_toda_expedida_ou_baixada_nao_aparece()
  {
    var m = Montar();
    var c = m.Cenario;
    No(c, Peca1, 3m);                    // toda expedida
    NaExpedicao(c, Peca1, 3m);
    c.Mover(Peca1, TiposDeMovimentacao.Expedicao, Local.NaExpedicao, Local.Expedido, 3m, Qualidade);
    No(c, Peca2, 3m);                    // toda baixada
    c.Mover(Peca2, TiposDeMovimentacao.Perda, Local.AIniciar, Local.Perdido, 3m, Qualidade);
    No(c, Peca3, 4m);                    // 2 expedidas e 2 baixadas: nada mais a andar
    NaExpedicao(c, Peca3, 2m);
    c.Mover(Peca3, TiposDeMovimentacao.Expedicao, Local.NaExpedicao, Local.Expedido, 2m, Qualidade);
    c.Mover(Peca3, TiposDeMovimentacao.Perda, Local.AIniciar, Local.Perdido, 2m, Qualidade);
    No(c, Peca4, 4m);                    // 3 de 4 expedidas: ainda aparece
    NaExpedicao(c, Peca4, 3m);
    c.Mover(Peca4, TiposDeMovimentacao.Expedicao, Local.NaExpedicao, Local.Expedido, 3m, Qualidade);
    No(c, Peca5, 2m, AgrupamentoId2);    // Pedido concluido
    c.Execucao.StatusDoPedido[PedidoId2] = "Concluido";

    var lista = await ListaAsync(m);

    Assert.Equal(new[] { Peca4 }, lista.Pecas.Select(p => p.EstruturaItemId));
  }

  [Fact]
  public async Task Item_nao_tem_linha_propria()
  {
    var m = Montar();
    No(m.Cenario, Peca1, 2m);
    No(m.Cenario, Item7, 4m, pai: Peca1);

    var lista = await ListaAsync(m);

    Assert.Equal(new[] { Peca1 }, lista.Pecas.Select(p => p.EstruturaItemId));
  }

  [Fact]
  public async Task Itens_em_lista_setores_dos_descendentes_sem_repetir_em_ordem_alfabetica()
  {
    var m = Montar();
    var c = m.Cenario;
    No(c, Peca1, 2m);
    c.Mover(Peca1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Dobra, 1), 1m);   // a Peca em si: nao conta
    No(c, Item7, 2m, pai: Peca1);        // filho, em Solda
    c.Mover(Item7, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Solda, 1), 2m);
    No(c, Item8, 2m, pai: Item7);        // neto (segundo nivel), aguardando coleta no Corte
    c.Mover(Item8, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 2m);
    c.Mover(Item8, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 2m);
    No(c, Item9, 2m, pai: Peca1);        // outro filho, tambem em Solda (repete) e parte em conserto na Pintura
    c.Mover(Item9, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Solda, 1), 1m);
    c.Mover(Item9, TiposDeMovimentacao.Conserto, Local.AIniciar, Local.EmConserto(Pintura), 1m);

    var peca = (await ListaAsync(m)).Pecas.Single(p => p.EstruturaItemId == Peca1);

    // Alfabetica ("Pintura" antes de "Solda"), nao pela ordem dos Ids (Solda = 3, Pintura = 4).
    Assert.Equal(new[] { "Corte", "Pintura", "Solda" }, peca.ItensEm.Select(s => s.Nome));
    Assert.Equal(new[] { Corte, Pintura, Solda }, peca.ItensEm.Select(s => s.SetorId));
  }

  [Fact]
  public async Task Item_a_iniciar_nao_entra_em_itens_em()
  {
    var m = Montar();
    var c = m.Cenario;
    No(c, Peca1, 2m);
    No(c, Item7, 2m, pai: Peca1);                         // a iniciar (nenhum movimento)
    No(c, Item8, 2m, pai: Peca1);                         // aguardando montagem: conta
    c.Mover(Item8, TiposDeMovimentacao.Entrega, Local.AIniciar, Local.AguardandoMontagem(Dobra), 2m);
    No(c, Item9, 2m, pai: Peca1);                         // ja montado: nao esta em Setor nenhum
    c.Mover(Item9, TiposDeMovimentacao.Entrega, Local.AIniciar, Local.Montado, 2m);
    No(c, Peca2, 1m);                                     // so tem descendente a iniciar
    No(c, Item10, 1m, pai: Peca2);

    var lista = await ListaAsync(m);

    Assert.Equal(new[] { "Dobra" }, lista.Pecas.Single(p => p.EstruturaItemId == Peca1).ItensEm.Select(s => s.Nome));
    Assert.Empty(lista.Pecas.Single(p => p.EstruturaItemId == Peca2).ItensEm);
  }

  [Fact]
  public async Task Ordem_e_prazo_numero_codigo_e_id()
  {
    var m = Montar();
    var c = m.Cenario;
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 25);
    c.Execucao.DataDeEntregaDoPedido[PedidoId2] = new DateOnly(2026, 10, 25);   // o mesmo prazo: decide o numero
    c.Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-a");
    c.Execucao.Agrupamentos[AgrupamentoId2] = ("AG-02", PedidoId2, "PED-B");     // ordinal: "B" antes de "a"
    No(c, Peca1, 1m, AgrupamentoId, codigo: "CMP-b");
    No(c, Peca2, 1m, AgrupamentoId, codigo: "CMP-C");                            // ordinal: "C" antes de "b"
    No(c, Peca3, 1m, AgrupamentoId, codigo: "CMP-C");                            // mesmo codigo: decide o Id
    No(c, Peca4, 1m, AgrupamentoId2, codigo: "CMP-z");
    No(c, Peca5, 1m, AgrupamentoId2, codigo: "CMP-z");
    No(c, Peca6, 1m, AgrupamentoId2, codigo: "CMP-z");
    c.Execucao.Agrupamentos[3] = ("AG-03", 3, "PED-Z");                          // prazo mais proximo, numero ultimo
    c.Execucao.StatusDoPedido[3] = "EmProducao";
    c.Execucao.DataDeEntregaDoPedido[3] = new DateOnly(2026, 10, 21);
    No(c, PecaDoTerceiroPedido, 1m, 3, codigo: "CMP-x");

    var lista = await ListaAsync(m);

    Assert.Equal(
        new[] { PecaDoTerceiroPedido, Peca4, Peca5, Peca6, Peca2, Peca3, Peca1 },
        lista.Pecas.Select(p => p.EstruturaItemId));
  }

  [Fact]
  public async Task Atraso_e_pausa_vem_do_servidor()
  {
    var m = Montar();
    var c = m.Cenario;
    c.Execucao.DataDeEntregaDoPedido[PedidoId] = new DateOnly(2026, 10, 19);    // ontem em Brasilia
    c.Execucao.PedidoOrigemDoPedido[PedidoId2] = PedidoId;                       // Retrabalho
    c.Execucao.Pausas.Add(new PedidoPausa
    {
      Id = 1, PedidoId = PedidoId, PausadoEm = T0, PausadoPorUsuarioId = Pcp, Motivo = "Alteração de projeto",
    });
    No(c, Peca1, 2m);
    No(c, Peca2, 2m, AgrupamentoId2);

    var lista = await ListaAsync(m);

    var atrasada = lista.Pecas.Single(p => p.EstruturaItemId == Peca1);
    Assert.Equal((true, false), (atrasada.Pedido.Atrasado, atrasada.Pedido.Retrabalho));
    Assert.Equal("Alteração de projeto", atrasada.Pedido.Pausa!.Motivo);
    var retrabalho = lista.Pecas.Single(p => p.EstruturaItemId == Peca2);
    Assert.Equal((false, true), (retrabalho.Pedido.Atrasado, retrabalho.Pedido.Retrabalho));
    Assert.Null(retrabalho.Pedido.Pausa);
  }

  [Fact]
  public async Task Embalagem_vale_quando_marcada_e_sem_volta_a_producao()
  {
    var m = Montar();
    var c = m.Cenario;
    No(c, Peca1, 2m);                   // pronta, marcada, sem volta
    NaExpedicao(c, Peca1, 2m);
    m.Embalagem.Ultimas[Peca1] = new UltimaEmbalagem(Peca1, TiposDeEmbalagem.Marcar, T0, Qualidade, false);
    No(c, Peca2, 2m);                   // pronta, marcada, mas algo voltou a producao depois
    NaExpedicao(c, Peca2, 2m);
    m.Embalagem.Ultimas[Peca2] = new UltimaEmbalagem(Peca2, TiposDeEmbalagem.Marcar, T0, Qualidade, true);
    No(c, Peca3, 2m);                   // marcada, mas 1 unidade ainda em producao
    NaExpedicao(c, Peca3, 1m);
    c.Mover(Peca3, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 1m);
    m.Embalagem.Ultimas[Peca3] = new UltimaEmbalagem(Peca3, TiposDeEmbalagem.Marcar, T0, Qualidade, false);
    No(c, Peca4, 2m);                   // pronta, sem marca: pode marcar
    NaExpedicao(c, Peca4, 2m);

    var lista = await ListaAsync(m);

    EmbalagemDaPecaDto E(int id) => lista.Pecas.Single(p => p.EstruturaItemId == id).Embalagem;
    Assert.Equal((true, true, 0m), (E(Peca1).Vale, E(Peca1).PodeMarcar, E(Peca1).EmProducao));
    Assert.Equal((TiposDeEmbalagem.Marcar, T0, "Qualidade"), (E(Peca1).Ultima!.Tipo, E(Peca1).Ultima!.DataHora, E(Peca1).Ultima!.Usuario));
    Assert.Equal((false, true), (E(Peca2).Vale, E(Peca2).PodeMarcar));
    Assert.Equal((false, false, 1m), (E(Peca3).Vale, E(Peca3).PodeMarcar, E(Peca3).EmProducao));
    Assert.Equal((false, true), (E(Peca4).Vale, E(Peca4).PodeMarcar));
    Assert.Null(E(Peca4).Ultima);
  }

  [Fact]
  public async Task Afericao_miniatura_e_embalagem_saem_em_lote()
  {
    var m = Montar();
    var c = m.Cenario;
    No(c, Peca1, 2m, requerRelatorio: true);
    No(c, Peca2, 2m, requerRelatorio: true);
    No(c, Peca3, 2m);
    c.Expedicao.Afericoes.Add(new RelatorioDimensional
    {
      Id = 1, EstruturaItemId = Peca1, Tipo = TiposDeAfericao.Afericao, Resultado = ResultadosDeAfericao.Conforme,
      DataHora = T0, UsuarioId = Qualidade,
    });
    var miniatura = new ArquivoDeComponente { Id = 900, NomeOriginal = "m.png", Conteudo = [1, 2, 3], Sha256 = new byte[32] };
    miniatura.Sha256[0] = 0xAB;
    m.Arquivos.Gravados.Add(miniatura);
    c.Catalogo.Componentes.Single(x => x.Id == 101).ArquivoMiniaturaId = 900;
    c.Catalogo.Componentes.Single(x => x.Id == 102).ArquivoMiniaturaId = 900;

    var lista = await ListaAsync(m);

    Assert.Equal((true, "Conforme"), (lista.Pecas[0].Afericao.ExigeRelatorio, lista.Pecas[0].Afericao.Situacao));
    Assert.Equal("Pendente", lista.Pecas[1].Afericao.Situacao);
    Assert.Equal("SemRelatorio", lista.Pecas[2].Afericao.Situacao);
    Assert.Equal(Convert.ToHexStringLower(miniatura.Sha256), lista.Pecas[0].Componente.MiniaturaVersao);
    Assert.Equal(lista.Pecas[0].Componente.MiniaturaVersao, lista.Pecas[1].Componente.MiniaturaVersao);
    Assert.Null(lista.Pecas[2].Componente.MiniaturaVersao);
    Assert.Equal(1, m.Arquivos.ConsultasDeSha256);
    Assert.Equal(1, m.Embalagem.ConsultasDeUltimas);
  }
}
