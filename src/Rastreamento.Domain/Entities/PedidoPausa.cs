namespace Rastreamento.Domain.Entities;

/// <summary>
/// Um intervalo em que o Pedido ficou pausado (spec da Fase 3D, secao 2.5). Pausado = existe linha
/// com `RetomadoEm` nulo; `UX_PedidoPausa_UmaAbertaPorPedido` garante no maximo uma. So de inclusao,
/// exceto o fecho do intervalo (`RetomadoEm`/`RetomadoPorUsuarioId`), gravado uma vez — o mesmo
/// desenho de `Montagem.EstornadaEm`. A pausa recusa so o Iniciar.
/// </summary>
public class PedidoPausa
{
  public int Id { get; set; }
  public int PedidoId { get; set; }
  public DateTime PausadoEm { get; set; }
  public int PausadoPorUsuarioId { get; set; }
  public string? Motivo { get; set; }
  public DateTime? RetomadoEm { get; set; }
  public int? RetomadoPorUsuarioId { get; set; }
}
