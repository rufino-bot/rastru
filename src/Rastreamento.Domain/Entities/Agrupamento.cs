namespace Rastreamento.Domain.Entities;

/// <summary>
/// Documento, com a UNICA excecao de hard delete do sistema: um Agrupamento vazio, em Pedido
/// Aberto, pode ser apagado de verdade — e so `Codigo` + `Quantidade` + `Tipo`, sem historico a
/// preservar (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public class Agrupamento
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public string Codigo { get; set; } = string.Empty;

    /// <summary>DECIMAL(18,4). A partir da Fase 3 conversa com a conservacao de quantidade.</summary>
    public decimal Quantidade { get; set; }

    /// <summary>Kit | Avulso — descritivo (Kit vai para solda; Avulso nao).</summary>
    public string Tipo { get; set; } = string.Empty;

    public DateTime? DataConclusao { get; set; }

    /// <summary>Autoria: responde "quem criou este agrupamento". FK para dbo.Usuario.</summary>
    public int CriadoPorUsuarioId { get; set; }

    public DateTime CriadoEm { get; set; }
}
