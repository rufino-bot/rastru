using Rastreamento.Application.Andamento;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Abstractions;
using Xunit;

namespace Rastreamento.Application.Tests.Andamento;

public class ComplicacoesDaPecaTests
{
  private const int Corte = 1, Solda = 3;

  private static NoDoCalculo No(int id, int? pai, decimal quantidade, decimal? razao, params int[] setores) =>
      new(id, pai, quantidade, razao, setores.Select((s, i) => new PassoDoCalculo(s, i + 1)).ToList());

  private static SaldoLiquido Em(int item, Local local, decimal quantidade) =>
      new(item, local.Posicao, local.SetorId, local.Ordem, quantidade);

  private static CalculadoraDeExecucao Calc(IEnumerable<NoDoCalculo> nos, params SaldoLiquido[] saldos) =>
      new(nos, saldos, new Dictionary<int, decimal>(), Array.Empty<(int, int)>());

  private static ResumoDoMaterial Pendente(int no, int material, decimal planejado = 5m, decimal separado = 0m) =>
      new(no, material, planejado, separado, NoPlanejado: true, DoAlmoxarifado: true);

  private static readonly IReadOnlyDictionary<int, string> Descricoes =
      new Dictionary<int, string> { [10] = "Chapa 3 mm", [11] = "Parafuso M8", [12] = "Tinta" };

  private static IReadOnlyList<ComplicacaoDto> Calcular(
      CalculadoraDeExecucao calc, IReadOnlyCollection<int> nos, ResumoDoMaterial[]? materiais = null, decimal aRepor = 0m) =>
      ComplicacoesDaPeca.Calcular(nos, calc, materiais ?? [], Descricoes, aRepor, id => $"Item {id}");

  [Fact]
  public void Peca_sem_nada_devolve_lista_vazia()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)], Em(1, Local.AIniciar, -10m), Em(1, Local.NoSetor(Corte, 1), 10m));

    Assert.Empty(Calcular(calc, [1]));
  }

  [Fact]
  public void Material_pendente_tem_o_texto_do_primeiro()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)]);

    var c = Assert.Single(Calcular(calc, [1], [Pendente(1, 10)]));

    Assert.Equal(CodigosDeComplicacao.MaterialPendente, c.Codigo);
    Assert.Equal("Material não separado: Chapa 3 mm", c.Texto);
  }

  [Fact]
  public void E_mais_N_quando_ha_varios_materiais()
  {
    var calc = Calc([No(1, null, 10m, null, Corte), No(2, 1, 10m, 1m, Corte)]);

    var c = Assert.Single(Calcular(calc, [1, 2], [Pendente(2, 10), Pendente(1, 12), Pendente(1, 11)]));

    // menor no (1), depois menor material (11)
    Assert.Equal("Material não separado: Parafuso M8 e mais 2", c.Texto);
  }

  [Fact]
  public void Material_de_no_sem_a_iniciar_nao_conta()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)], Em(1, Local.AIniciar, -10m), Em(1, Local.NoSetor(Corte, 1), 10m));

    Assert.Empty(Calcular(calc, [1], [Pendente(1, 10)]));
  }

  [Fact]
  public void Material_sem_pendencia_nao_conta()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)]);

    Assert.Empty(Calcular(calc, [1], [Pendente(1, 10, planejado: 5m, separado: 5m)]));
  }

  [Fact]
  public void Pai_com_filhos_e_a_iniciar_conta_material()
  {
    var calc = Calc([No(1, null, 10m, null, Solda), No(2, 1, 10m, 1m, Corte)]);

    var c = Assert.Single(Calcular(calc, [1, 2], [Pendente(1, 12)]));

    Assert.Equal("Material não separado: Tinta", c.Texto);
  }

  [Fact]
  public void Aguardando_liberacao_soma_os_nos()
  {
    var calc = Calc([No(1, null, 10m, null, Corte), No(2, 1, 10m, 1m, Corte)],
        Em(1, Local.AguardandoLiberacao, 2m), Em(2, Local.AguardandoLiberacao, 1.5m));

    var c = Assert.Single(Calcular(calc, [1, 2]));

    Assert.Equal(CodigosDeComplicacao.AguardandoLiberacao, c.Codigo);
    Assert.Equal("Aguardando liberação da Gestão (3,5)", c.Texto);
  }

  [Fact]
  public void Aguardando_decisao_soma_os_nao_conformes()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)], Em(1, Local.NaoConforme, 4m));

    var c = Assert.Single(Calcular(calc, [1]));

    Assert.Equal(CodigosDeComplicacao.AguardandoDecisao, c.Codigo);
    Assert.Equal("Não conformes aguardando decisão (4)", c.Texto);
  }

  [Fact]
  public void Em_conserto_soma_as_tres_posicoes()
  {
    var calc = Calc([No(1, null, 10m, null, Corte)],
        Em(1, Local.AguardandoConserto(Corte), 1m), Em(1, Local.EmConserto(Corte), 2m), Em(1, Local.ConsertoPronto(Corte), 3m));

    var c = Assert.Single(Calcular(calc, [1]));

    Assert.Equal(CodigosDeComplicacao.EmConserto, c.Codigo);
    Assert.Equal("Em conserto (6)", c.Texto);
  }

  [Fact]
  public void Montagem_incompleta_diz_o_que_falta_do_filho_de_menor_id()
  {
    // Peca 1 monta na Solda; o filho 2 (razao 2) ja chegou, o filho 3 (razao 1) nao.
    var calc = Calc(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        Em(2, Local.AguardandoMontagem(Solda), 2m));

    var c = Assert.Single(Calcular(calc, [1, 2, 3]), x => x.Codigo == CodigosDeComplicacao.MontagemIncompleta);

    Assert.Equal("Montagem: faltam 1 de Item 3", c.Texto);
  }

  [Fact]
  public void Montagem_completa_nao_e_complicacao()
  {
    var calc = Calc(
        [No(1, null, 1m, null, Solda), No(2, 1, 1m, 1m, Corte)],
        Em(2, Local.AguardandoMontagem(Solda), 1m));

    Assert.DoesNotContain(Calcular(calc, [1, 2]), x => x.Codigo == CodigosDeComplicacao.MontagemIncompleta);
  }

  [Fact]
  public void A_repor_so_quando_positivo()
  {
    var calc = Calc([No(1, null, 10m, null, Corte), ]);
    var c = Assert.Single(Calcular(calc, [1], aRepor: 2.5m));

    Assert.Equal(CodigosDeComplicacao.ARepor, c.Codigo);
    Assert.Equal("2,5 a repor", c.Texto);
  }

  [Fact]
  public void Ordem_fixa_dos_codigos()
  {
    var calc = Calc(
        [No(1, null, 5m, null, Solda), No(2, 1, 10m, 2m, Corte), No(3, 1, 5m, 1m, Corte)],
        Em(1, Local.AguardandoLiberacao, 1m), Em(1, Local.NaoConforme, 1m), Em(1, Local.EmConserto(Solda), 1m),
        Em(2, Local.AguardandoMontagem(Solda), 2m));

    var r = Calcular(calc, [1, 2, 3], [Pendente(1, 10)], aRepor: 1m);

    Assert.Equal(CodigosDeComplicacao.Todos, r.Select(x => x.Codigo).ToList());
    Assert.Equal(
        ["MaterialPendente", "AguardandoLiberacao", "AguardandoDecisao", "EmConserto", "MontagemIncompleta", "ARepor"],
        CodigosDeComplicacao.Todos);
  }
}
