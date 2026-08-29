namespace Rastreamento.Domain.Entities;

/// <summary>
/// A arvore REAL usada num Agrupamento — copiada do catalogo (`Componente`) e customizavel.
/// Recursiva numa tabela so: no sem pai e uma PECA, no com pai e um ITEM. Nao existem tabelas
/// separadas (ver `01-dominio-e-regras-de-negocio.md`, regra 3).
///
/// `Quantidade` e o lote AGREGADO e ABSOLUTO daquele no — nao a razao por unidade do pai. Uma Peca
/// de 10 cuja receita diz "4 por unidade" gera filho com 40. A Fase 3 aponta setor por
/// EstruturaItem, e o operador movimenta 40 suportes, nao "4 por pai".
/// </summary>
public class EstruturaItem
{
  public int Id { get; set; }
  public int AgrupamentoId { get; set; }

  /// <summary>
  /// NULL so em Item ad-hoc. Peca sempre referencia um Componente —
  /// `CK_EstruturaItem_PecaTemComponente` garante o gancho no banco.
  /// </summary>
  public int? ComponenteId { get; set; }

  /// <summary>Nome proprio do no. NULL herda a descricao do Componente (regra 19).</summary>
  public string? Descricao { get; set; }

  /// <summary>Self-FK. NULL = Peca (topo da arvore dentro do Agrupamento).</summary>
  public int? EstruturaPaiId { get; set; }

  /// <summary>Peca | Item — denormalizado para consulta rapida.</summary>
  public string NivelHierarquico { get; set; } = string.Empty;

  public decimal Quantidade { get; set; }

  /// <summary>Vale para Peca; o cliente exige no cadastro do Pedido (regra 10).</summary>
  public bool RequerRelatorioDimensional { get; set; }
}
