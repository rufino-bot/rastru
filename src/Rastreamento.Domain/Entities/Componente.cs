namespace Rastreamento.Domain.Entities;

public class Componente
{
  public int Id { get; set; }
  public string Codigo { get; set; } = string.Empty;
  public string Descricao { get; set; } = string.Empty;

  /// <summary>
  /// Lista fechada no DDL (CK_Componente_Tipo): Bruto | Fabricado | Montagem. Quem valida e o
  /// caso de uso, nao o CHECK — excecao de CHECK sobe como SqlException e vira 500, e o cliente
  /// merece 400. Mesmo criterio de Agrupamento.Tipo.
  /// </summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>Catalogo nao se exclui, se inativa: EstruturaItem aponta para o Componente.</summary>
  public bool Ativo { get; set; }

  // ArquivoSolido e ArquivoFoto existem em dbo.Componente e NAO sao mapeadas aqui de proposito:
  // upload e a regra 18 (solido obrigatorio por Peca de Pedido) sao trabalho da Fase 2. Colunas
  // nullable, entao o INSERT do EF sem elas e valido.
}
