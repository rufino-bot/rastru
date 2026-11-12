using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Retrabalho;
using Xunit;

namespace Rastreamento.Application.Tests.Retrabalho;

public class CopiaDaPecaTests
{
  private static readonly IReadOnlySet<int> Nenhum = new HashSet<int>();
  private static readonly IReadOnlyList<(int, decimal)> SemMat = [];
  private static readonly IReadOnlyList<(int, int)> SemRot = [];

  private static NoDaCopia No(int id, int? pai, decimal q, decimal? porPai = null, bool relatorio = false,
      IReadOnlyList<(int, decimal)>? mat = null, IReadOnlyList<(int, int)>? rot = null) =>
      new(id, pai, 100 + id, $"d{id}", q, porPai, relatorio, mat ?? SemMat, rot ?? SemRot);

  [Fact]
  public void Copia_escala_cada_no_pela_razao_ate_a_Peca()
  {
    var arvore = new[] { No(1, null, 10), No(2, 1, 40, 4), No(3, 2, 80, 2) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 1, Nenhum, false);
    Assert.True(r.Sucesso);
    Assert.Equal(1m, r.Valor!.Quantidade);
    Assert.Equal(4m, r.Valor.Filhos[0].Quantidade);
    Assert.Equal(8m, r.Valor.Filhos[0].Filhos[0].Quantidade);
    Assert.Equal(4m, r.Valor.Filhos[0].QuantidadePorPai);
  }

  [Fact]
  public void Materiais_se_reescalam_pela_mesma_razao()
  {
    var arvore = new[] { No(1, null, 10), No(2, 1, 40, 4, mat: [(9, 5m)]) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 1, Nenhum, false);
    Assert.Equal(0.5m, r.Valor!.Filhos[0].Materiais.Single().Quantidade);
  }

  [Fact]
  public void Copia_guarda_a_origem_de_cada_no()
  {
    var arvore = new[] { No(1, null, 10, relatorio: true), No(2, 1, 40, 4, relatorio: true), No(3, 2, 80, 2) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 2, Nenhum, false).Valor!;
    Assert.Equal(1, r.OrigemEstruturaItemId);
    Assert.Equal(2, r.Filhos[0].OrigemEstruturaItemId);
    Assert.Equal(3, r.Filhos[0].Filhos[0].OrigemEstruturaItemId);
    Assert.True(r.RequerRelatorioDimensional);
    Assert.False(r.Filhos[0].RequerRelatorioDimensional);
    Assert.Equal(101, r.ComponenteId);
    Assert.Equal("d1", r.Descricao);
  }

  [Fact]
  public void No_que_saiu_do_projeto_nao_entra_com_a_arvore_abaixo()
  {
    var arvore = new[] { No(1, null, 10), No(2, 1, 40, 4), No(3, 2, 80, 2), No(4, 1, 10, 1) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 1, new HashSet<int> { 2 }, false).Valor!;
    Assert.Equal(4, Assert.Single(r.Filhos).OrigemEstruturaItemId);
  }

  [Fact]
  public void Peca_que_saiu_do_projeto_e_409_SemPecaARepor()
  {
    var r = CopiaDaPeca.Planejar([No(1, null, 10)], 1, 1, new HashSet<int> { 1 }, false);
    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDoRetrabalho.SemPecaARepor, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  }

  [Fact]
  public void Filho_com_quantidade_acima_de_pai_vezes_QuantidadePorPai_copia_pela_QuantidadePorPai()
  {
    // Pedido em producao: a quantidade do Item so aumenta (D12 da 5B-1), entao o Item com folga (50) passa de pai x
    // QuantidadePorPai (10 x 4 = 40). A razao das quantidades daria 5 por Peca; a regra da secao 4.3 e 4.
    var arvore = new[] { No(1, null, 10), No(2, 1, 50, 4), No(3, 2, 130, 2) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 1, Nenhum, false).Valor!;
    Assert.Equal(4m, r.Filhos[0].Quantidade);
    Assert.Equal(8m, r.Filhos[0].Filhos[0].Quantidade);
  }

  [Fact]
  public void Filho_sem_QuantidadePorPai_usa_a_razao_das_quantidades()
  {
    var arvore = new[] { No(1, null, 10), No(2, 1, 20) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 3, Nenhum, false).Valor!;
    Assert.Equal(6m, r.Filhos[0].Quantidade);
    Assert.Null(r.Filhos[0].QuantidadePorPai);
  }

  [Fact]
  public void So_a_Peca_copia_sem_filhos_e_sem_materiais()
  {
    var arvore = new[] { No(1, null, 10, mat: [(9, 5m)], rot: [(7, 2), (6, 1)]), No(2, 1, 40, 4) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 2, Nenhum, true).Valor!;
    Assert.Empty(r.Filhos);
    Assert.Empty(r.Materiais);
    Assert.Equal(new[] { (6, 1), (7, 2) }, r.Roteiro.ToArray());
    Assert.Equal(2m, r.Quantidade);
  }

  [Fact]
  public void Quantidade_fora_da_faixa_da_coluna_e_recusada()
  {
    var arvore = new[] { No(1, null, 10), No(2, 1, 1_000_000, 1_000_000) };
    var r = CopiaDaPeca.Planejar(arvore, 1, 1_000_000_000m, Nenhum, false);
    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    var piso = CopiaDaPeca.Planejar([No(1, null, 10)], 1, 0.00001m, Nenhum, false);
    Assert.Equal(TipoDeErro.Validacao, piso.TipoDoErro);
  }

  [Fact]
  public void Mais_nos_que_o_teto_e_recusado()
  {
    var arvore = new List<NoDaCopia> { No(1, null, 1) };
    for (var i = 2; i <= PlanejadorDeCopia.NosMaximos + 1; i++) arvore.Add(No(i, 1, 1, 1));
    var r = CopiaDaPeca.Planejar(arvore, 1, 1, Nenhum, false);
    Assert.False(r.Sucesso);
    Assert.Equal(PlanejadorDeCopia.CodigoDeTamanho, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
  }
}
