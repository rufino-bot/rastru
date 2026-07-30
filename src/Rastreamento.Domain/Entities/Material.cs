namespace Rastreamento.Domain.Entities;

public class Material
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Texto livre no DDL (ex.: UN, M, KG, M2) — sem CHECK, entao sem lista fechada aqui.</summary>
    public string UnidadeMedida { get; set; } = string.Empty;

    /// <summary>Catalogo nao se exclui, se inativa: EstruturaMaterial aponta para o Material.</summary>
    public bool Ativo { get; set; }
}
