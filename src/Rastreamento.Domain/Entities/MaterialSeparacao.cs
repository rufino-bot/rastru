namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha do livro de separacao de material (spec da Fase 4, secao 3.2): o Separador tirou
/// `Quantidade` de um Material do almoxarifado para um no. SO INCLUSAO, como `Movimentacao` — nenhum
/// codigo edita nem apaga uma linha; correcao e um Estorno que aponta o original (`EstornoDeId`, uma
/// vez so por `UX_MaterialSeparacao_EstornoDe`). O separado de um (no, material) e a soma das
/// `Separacao` menos a soma dos `Estorno`, sempre calculado. A linha aponta o no e o Material, e nao a
/// linha de `EstruturaMaterial`: o PCP pode tirar do plano um material que ja tem separacao (D14).
/// </summary>
public class MaterialSeparacao
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }
  public int MaterialId { get; set; }

  /// <summary>Um dos <see cref="TiposDeSeparacao"/> (`CK_MaterialSeparacao_Tipo`).</summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>Sempre positiva (`CK_MaterialSeparacao_QuantidadePositiva`); o Estorno subtrai pelo `Tipo`.</summary>
  public decimal Quantidade { get; set; }

  /// <summary>So no Estorno: a separacao que ele desfaz. O estorno repete no, material e quantidade dela.</summary>
  public int? EstornoDeId { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor — quem pode estornar sem ser PCP ou Administrador.</summary>
  public int UsuarioId { get; set; }
}

/// <summary>Os tipos de `CK_MaterialSeparacao_Tipo`.</summary>
public static class TiposDeSeparacao
{
  public const string Separacao = "Separacao";
  public const string Estorno = "Estorno";
}
