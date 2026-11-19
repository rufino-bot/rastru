using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Panorama;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>
/// Marcar e desmarcar o "Embalado" (secoes 3.2 e 3.3 da spec do Panorama). Cenario: Pedido `EmProducao`, Peca de 6
/// copiada do catalogo, com as 6 no local de expedicao, e um Item filho dela.
/// </summary>
public class EmbalagemUseCaseTests
{
  private const int Peca = 1, Item = 2;
  private static readonly CancellationToken Ct = CancellationToken.None;
  private static readonly DateTime T0 = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

  private sealed record Montagem(CenarioDeExecucao Cenario, FakeEmbalagemRepo Embalagem)
  {
    public Task<Result<bool>> Registrar(bool? embalado, int no = Peca) =>
        new EmbalagemUseCase(Cenario.Execucao, Cenario.Estruturas, Cenario.Catalogo, Embalagem)
            .Registrar(no, new NovaEmbalagemDto(embalado), Qualidade, Ct);
  }

  /// <summary>A Peca de 6 com `naExpedicao` unidades no local de expedicao; o resto fica a iniciar.</summary>
  private static Montagem Montar(string status = "EmProducao", decimal naExpedicao = 6m)
  {
    var c = new CenarioDeExecucao();
    c.No(Peca, null, 6m, null, Corte);
    c.No(Item, Peca, 12m, 2m, Dobra);
    var peca = c.Estruturas.Itens.Single(i => i.Id == Peca);
    peca.Descricao = null;
    peca.ComponenteId = 70;
    c.Catalogo.Componentes.Add(
        new Componente { Id = 70, Codigo = "CHP-220", Descricao = "Chapa dobrada 3 mm", Tipo = "Fabricado", Ativo = true });
    c.Execucao.StatusDoPedido[PedidoId] = status;
    if (naExpedicao > 0m)
    {
      c.Mover(Peca, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), naExpedicao);
      c.Mover(Peca, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), naExpedicao);
      c.Mover(Peca, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NaExpedicao, naExpedicao, Movimentador);
    }
    return new Montagem(c, new FakeEmbalagemRepo());
  }

  private static void UltimaLinha(Montagem m, string tipo = TiposDeEmbalagem.Marcar, bool voltou = false) =>
      m.Embalagem.Ultimas[Peca] = new UltimaEmbalagem(Peca, tipo, T0, Qualidade, voltou);

  [Fact]
  public async Task Corpo_sem_embalado_e_recusado_antes_de_ler_o_no()
  {
    var m = Montar();

    var r = await m.Registrar(null, no: 999);

    Assert.Equal(CodigosDoPanorama.CorpoInvalido, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task No_inexistente_e_404()
  {
    var m = Montar();

    var r = await m.Registrar(true, no: 999);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Item_e_recusado_com_NaoEhPeca()
  {
    var m = Montar();

    var r = await m.Registrar(true, no: Item);

    Assert.Equal(CodigosDaExpedicao.NaoEhPeca, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("é um Item: só a Peça é embalada.", r.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Theory]
  [InlineData("Concluido")]
  [InlineData("Cancelado")]
  public async Task Pedido_fechado_recusa_marcar_e_desmarcar(string status)
  {
    var m = Montar(status);
    UltimaLinha(m);

    foreach (var embalado in new[] { true, false })
    {
      var r = await m.Registrar(embalado);

      Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
      Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    }
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Item_de_pedido_fechado_e_PedidoFechado_e_nao_NaoEhPeca()
  {
    var m = Montar("Concluido");

    var r = await m.Registrar(true, no: Item);

    Assert.Equal(CodigosDaExecucao.PedidoFechado, r.Erro);
  }

  [Fact]
  public async Task Marcar_com_producao_em_andamento_diz_quantos_faltam()
  {
    var m = Montar(naExpedicao: 4m);   // 4 no local e 2 a iniciar

    var r = await m.Registrar(true);

    Assert.Equal(CodigosDoPanorama.ProducaoNaoTerminou, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal("Faltam 2 em produção.", r.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Peca_que_nunca_saiu_de_a_iniciar_nao_pode_ser_marcada()
  {
    var m = Montar(naExpedicao: 0m);

    var r = await m.Registrar(true);

    Assert.Equal(CodigosDoPanorama.ProducaoNaoTerminou, r.Erro);
    Assert.Equal("Faltam 6 em produção.", r.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Marcar_com_o_embalado_valendo_e_JaEmbalado()
  {
    var m = Montar();
    UltimaLinha(m);

    var r = await m.Registrar(true);

    Assert.Equal(CodigosDoPanorama.JaEmbalado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal("Esta Peça já está embalada.", r.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Desmarcar_sem_marca_e_NaoEmbalado()
  {
    var m = Montar();

    var r = await m.Registrar(false);

    Assert.Equal(CodigosDoPanorama.NaoEmbalado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal("Esta Peça não está embalada.", r.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Desmarcar_a_marca_que_sumiu_e_NaoEmbalado()
  {
    var m = Montar();
    UltimaLinha(m, voltou: true);

    var r = await m.Registrar(false);

    Assert.Equal(CodigosDoPanorama.NaoEmbalado, r.Erro);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Marcar_grava_linha_Marcar_com_autor_e_hora()
  {
    var m = Montar();
    var antes = DateTime.UtcNow;

    var r = await m.Registrar(true);

    Assert.True(r.Sucesso, r.Detalhe);
    var linha = Assert.Single(m.Embalagem.Gravadas);
    Assert.Equal((Peca, TiposDeEmbalagem.Marcar, Qualidade), (linha.EstruturaItemId, linha.Tipo, linha.UsuarioId));
    Assert.InRange(linha.DataHora, antes, DateTime.UtcNow);
    Assert.Equal(1, m.Cenario.Execucao.Commits);
  }

  [Fact]
  public async Task Desmarcar_grava_linha_Desmarcar()
  {
    var m = Montar();
    UltimaLinha(m);

    var r = await m.Registrar(false);

    Assert.True(r.Sucesso, r.Detalhe);
    var linha = Assert.Single(m.Embalagem.Gravadas);
    Assert.Equal((Peca, TiposDeEmbalagem.Desmarcar, Qualidade), (linha.EstruturaItemId, linha.Tipo, linha.UsuarioId));
  }

  [Fact]
  public async Task Marcar_de_novo_depois_de_a_marca_sumir_grava_outra_marca()
  {
    var m = Montar();
    UltimaLinha(m, voltou: true);

    var r = await m.Registrar(true);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(TiposDeEmbalagem.Marcar, Assert.Single(m.Embalagem.Gravadas).Tipo);
  }

  [Fact]
  public async Task Marcar_de_novo_depois_de_desmarcar_grava_outra_marca()
  {
    var m = Montar();
    UltimaLinha(m, TiposDeEmbalagem.Desmarcar);

    var r = await m.Registrar(true);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(TiposDeEmbalagem.Marcar, Assert.Single(m.Embalagem.Gravadas).Tipo);
  }

  [Fact]
  public async Task Pedido_pausado_nao_recusa()
  {
    var m = Montar();
    m.Cenario.Execucao.StatusDoPedido[PedidoId] = "Pausado";

    var marcou = await m.Registrar(true);
    UltimaLinha(m);
    var desmarcou = await m.Registrar(false);

    Assert.True(marcou.Sucesso, marcou.Detalhe);
    Assert.True(desmarcou.Sucesso, desmarcou.Detalhe);
    Assert.Equal(2, m.Embalagem.Gravadas.Count);
  }

  [Fact]
  public async Task Peca_com_quantidade_aumentada_depois_da_marca_nao_vale()
  {
    var m = Montar();
    UltimaLinha(m);
    m.Cenario.Estruturas.Itens.Single(i => i.Id == Peca).Quantidade = 8m;   // 2 novas, a iniciar

    var desmarcar = await m.Registrar(false);
    var marcar = await m.Registrar(true);

    Assert.Equal(CodigosDoPanorama.NaoEmbalado, desmarcar.Erro);
    Assert.Equal(CodigosDoPanorama.ProducaoNaoTerminou, marcar.Erro);
    Assert.Equal("Faltam 2 em produção.", marcar.Detalhe);
    Assert.Empty(m.Embalagem.Gravadas);
  }

  [Fact]
  public async Task Falha_nao_commita()
  {
    var m = Montar(naExpedicao: 4m);

    await m.Registrar(true);

    Assert.Equal(0, m.Cenario.Execucao.Commits);
  }
}
