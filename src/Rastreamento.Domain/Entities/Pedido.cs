namespace Rastreamento.Domain.Entities;

/// <summary>
/// Documento: nao tem `Ativo` e nao se exclui (ver a spec da Fase 1, "Politica de exclusao").
/// Os campos de retrabalho e de conclusao existem porque o mapeamento cobre a tabela inteira;
/// nada na Fase 1 os preenche (retrabalho e Fase 5, transicao de status e Fase 3).
/// </summary>
public class Pedido
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int? PedidoOrigemId { get; set; }
    public string? MotivoRetrabalho { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DataAbertura { get; set; }
    public DateTime? DataConclusao { get; set; }

    /// <summary>Autoria: responde "quem abriu este pedido". FK para dbo.Usuario.</summary>
    public int CriadoPorUsuarioId { get; set; }
}
