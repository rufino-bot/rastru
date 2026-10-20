namespace Rastreamento.Domain.Entities;

/// <summary>
/// O agrupador da expedicao (C8 da spec da Fase 5A, secao 3.5): quando e quem expediu (D9). SO INCLUSAO.
/// As quantidades sao as linhas `Expedicao` do livro que apontam a remessa (`Movimentacao.RemessaId`);
/// uma remessa leva uma ou mais Pecas, de um ou mais Pedidos, e estorna-se a remessa inteira.
/// </summary>
public class Remessa
{
  public int Id { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>Quem expediu: o usuario logado.</summary>
  public int UsuarioId { get; set; }
}
