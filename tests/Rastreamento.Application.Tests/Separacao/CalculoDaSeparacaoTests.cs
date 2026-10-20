using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>
/// A regra 34, pura: separado = separacoes menos estornos; pendencia so do material do almoxarifado no
/// planejado; acima do planejado aceito e marcado; o resumo da Fila conta materiais completos, nao
/// quantidades. Dados montados a mao, sem repositorio.
/// </summary>
public class CalculoDaSeparacaoTests
{
  private const int No = 42, OutroNo = 43;
  private const int Parafuso = 7, Chapa = 8, Arruela = 9;

  private static MaterialSeparacao Separacao(decimal quantidade, int no = No, int material = Parafuso) =>
      new() { EstruturaItemId = no, MaterialId = material, Tipo = TiposDeSeparacao.Separacao, Quantidade = quantidade };

  private static MaterialSeparacao Estorno(decimal quantidade, int no = No, int material = Parafuso) =>
      new() { EstruturaItemId = no, MaterialId = material, Tipo = TiposDeSeparacao.Estorno, Quantidade = quantidade };

  private static EstruturaMaterial Planejado(decimal quantidade, int no = No, int material = Parafuso) =>
      new() { EstruturaItemId = no, MaterialId = material, Quantidade = quantidade };

  private static IReadOnlySet<int> Almoxarifado(params int[] materiais) => new HashSet<int>(materiais);

  [Fact]
  public void Separado_e_separacoes_menos_estornos()
  {
    var separado = CalculoDaSeparacao.Separado(new[] { Separacao(5), Separacao(3), Estorno(3) });

    Assert.Equal(5m, separado);
  }

  [Fact]
  public void Pendencia_e_o_planejado_menos_o_separado()
  {
    var resumo = Assert.Single(CalculoDaSeparacao.Resumir(
        new[] { Planejado(10) }, new[] { Separacao(8) }, Almoxarifado(Parafuso)));

    Assert.Equal(10m, resumo.Planejado);
    Assert.Equal(8m, resumo.Separado);
    Assert.Equal(2m, resumo.Pendencia);
    Assert.False(resumo.AcimaDoPlanejado);
  }

  [Fact]
  public void Pendencia_nunca_fica_negativa_e_acima_e_marcado()
  {
    var resumo = Assert.Single(CalculoDaSeparacao.Resumir(
        new[] { Planejado(10) }, new[] { Separacao(12) }, Almoxarifado(Parafuso)));

    Assert.Equal(0m, resumo.Pendencia);
    Assert.True(resumo.AcimaDoPlanejado);
  }

  [Fact]
  public void Separado_igual_ao_planejado_nao_e_acima_e_nao_tem_pendencia()
  {
    var resumo = Assert.Single(CalculoDaSeparacao.Resumir(
        new[] { Planejado(10) }, new[] { Separacao(10) }, Almoxarifado(Parafuso)));

    Assert.Equal(0m, resumo.Pendencia);
    Assert.False(resumo.AcimaDoPlanejado);
  }

  [Fact]
  public void Material_que_fica_num_Setor_nao_tem_pendencia()
  {
    var resumo = Assert.Single(CalculoDaSeparacao.Resumir(
        new[] { Planejado(10) }, Array.Empty<MaterialSeparacao>(), Almoxarifado()));

    Assert.Equal(10m, resumo.Planejado);
    Assert.Equal(0m, resumo.Separado);
    Assert.False(resumo.DoAlmoxarifado);
    Assert.Equal(0m, resumo.Pendencia);
  }

  [Fact]
  public void Material_que_saiu_do_plano_com_historico_continua_no_resumo()
  {
    var resumo = Assert.Single(CalculoDaSeparacao.Resumir(
        Array.Empty<EstruturaMaterial>(), new[] { Separacao(4) }, Almoxarifado(Parafuso)));

    Assert.Equal(No, resumo.EstruturaItemId);
    Assert.Equal(Parafuso, resumo.MaterialId);
    Assert.False(resumo.NoPlanejado);
    Assert.Equal(0m, resumo.Planejado);
    Assert.Equal(4m, resumo.Separado);
    Assert.Equal(0m, resumo.Pendencia);
    // Fora do plano o aviso e so "fora do planejado"; "acima" e de quem tem plano.
    Assert.False(resumo.AcimaDoPlanejado);
  }

  [Fact]
  public void Material_fora_do_plano_nao_tem_pendencia_mesmo_com_planejado_no_registro()
  {
    // `Resumir` sempre zera o planejado fora do plano; a guarda do proprio registro nao depende disso.
    var resumo = new ResumoDoMaterial(No, Parafuso, Planejado: 10, Separado: 4, NoPlanejado: false, DoAlmoxarifado: true);

    Assert.Equal(0m, resumo.Pendencia);
  }

  [Fact]
  public void Material_sem_plano_e_sem_linha_nao_aparece()
  {
    // O Parafuso esta no plano; a Chapa e do almoxarifado, mas nao esta no plano nem tem linha no livro.
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(10) }, Array.Empty<MaterialSeparacao>(), Almoxarifado(Parafuso, Chapa));

    var resumo = Assert.Single(resumos);
    Assert.Equal(Parafuso, resumo.MaterialId);
    Assert.DoesNotContain(resumos, r => r.MaterialId == Chapa);
  }

  [Fact]
  public void Resumir_separa_os_nos()
  {
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(10, OutroNo), Planejado(10, No) },
        new[] { Separacao(3, No), Separacao(7, OutroNo), Estorno(3, No) },
        Almoxarifado(Parafuso));

    Assert.Collection(resumos,
        r =>
        {
          Assert.Equal(No, r.EstruturaItemId);
          Assert.Equal(0m, r.Separado);
          Assert.Equal(10m, r.Pendencia);
        },
        r =>
        {
          Assert.Equal(OutroNo, r.EstruturaItemId);
          Assert.Equal(7m, r.Separado);
          Assert.Equal(3m, r.Pendencia);
        });
  }

  [Fact]
  public void Resumir_ordena_por_no_e_depois_por_material()
  {
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(1, OutroNo, Parafuso), Planejado(1, No, Arruela), Planejado(1, No, Parafuso) },
        new[] { Separacao(1, No, Chapa) },
        Almoxarifado(Parafuso, Chapa, Arruela));

    Assert.Equal(
        new[] { (No, Parafuso), (No, Chapa), (No, Arruela), (OutroNo, Parafuso) },
        resumos.Select(r => (r.EstruturaItemId, r.MaterialId)));
  }

  [Fact]
  public void ResumoDaFila_conta_materiais_completos_e_nao_quantidades()
  {
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(10, material: Parafuso), Planejado(5, material: Chapa), Planejado(4, material: Arruela) },
        new[] { Separacao(10, material: Parafuso), Separacao(6, material: Chapa), Separacao(2, material: Arruela) },
        Almoxarifado(Parafuso, Chapa, Arruela));

    var resumo = CalculoDaSeparacao.ResumoDaFila(resumos);

    Assert.Equal(new ResumoDeMaterialDto(Completos: 2, Total: 3), resumo);
  }

  [Fact]
  public void ResumoDaFila_ignora_material_de_Setor_e_fora_do_plano()
  {
    // Parafuso: almoxarifado, no plano, completo. Chapa: fica num Setor, no plano, nada separado.
    // Arruela: almoxarifado, fora do plano, com historico.
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(10, material: Parafuso), Planejado(5, material: Chapa) },
        new[] { Separacao(10, material: Parafuso), Separacao(3, material: Arruela) },
        Almoxarifado(Parafuso, Arruela));

    var resumo = CalculoDaSeparacao.ResumoDaFila(resumos);

    Assert.Equal(new ResumoDeMaterialDto(Completos: 1, Total: 1), resumo);
  }

  [Fact]
  public void ResumoDaFila_sem_material_do_almoxarifado_e_nulo()
  {
    var resumos = CalculoDaSeparacao.Resumir(
        new[] { Planejado(10, material: Chapa) }, Array.Empty<MaterialSeparacao>(), Almoxarifado(Parafuso));

    Assert.Null(CalculoDaSeparacao.ResumoDaFila(resumos));
  }
}
