namespace Rastreamento.Domain.Entities;

/// <summary>
/// A medicao de uma Peca depois de uma afericao nao conforme (D4 da spec da Fase 5A, secao 3.3): "medi 6,
/// 4 conformes e 2 nao". SO INCLUSAO. As duas linhas `Medicao` do livro apontam esta contagem
/// (`Movimentacao.ContagemId`), e estorna-se a contagem inteira (C17).
/// </summary>
public class ContagemDimensional
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }

  /// <summary>Nao negativa, e `Conformes + NaoConformes` e positiva (`CK_ContagemDimensional_AlgumaUnidade`).</summary>
  public decimal Conformes { get; set; }
  public decimal NaoConformes { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor — quem pode estornar sem ser PCP, Gestao ou Administrador.</summary>
  public int UsuarioId { get; set; }
}
