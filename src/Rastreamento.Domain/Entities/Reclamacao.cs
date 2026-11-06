namespace Rastreamento.Domain.Entities;

/// <summary>
/// A reclamacao do cliente (spec da Fase 5B-2, secao 3.3): quantas unidades de uma Peca expedida voltaram, e o que o
/// cliente disse. SO INCLUSAO, como `Montagem`: a unica escrita depois de nascer e marcar `PedidoRetrabalhoId` (quando
/// o Retrabalho que a atende abre) e o estorno (`EstornadaEm`/`EstornadaPorUsuarioId`).
/// </summary>
public class Reclamacao
{
  public int Id { get; set; }

  /// <summary>O Pedido que expediu a Peca.</summary>
  public int PedidoId { get; set; }

  /// <summary>A Peca (no de topo) reclamada.</summary>
  public int EstruturaItemId { get; set; }

  /// <summary>Quantas voltaram (`CK_Reclamacao_Quantidade`: maior que zero).</summary>
  public decimal Quantidade { get; set; }

  /// <summary>O que o cliente disse: obrigatoria, aparada, ate 500 caracteres.</summary>
  public string Descricao { get; set; } = string.Empty;

  /// <summary>O Retrabalho que a atende.</summary>
  public int? PedidoRetrabalhoId { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  public int UsuarioId { get; set; }
  public DateTime? EstornadaEm { get; set; }
  public int? EstornadaPorUsuarioId { get; set; }
}

/// <summary>Os destinos de uma reclamacao ao abrir o Retrabalho (secao 4.7 da spec da Fase 5B-2).</summary>
public static class DestinosDaReclamacao
{
  public const string Consertar = "Consertar";
  public const string Refazer = "Refazer";
}
