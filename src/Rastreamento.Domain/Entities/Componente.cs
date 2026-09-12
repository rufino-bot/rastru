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

  /// <summary>
  /// Solido 3D (STL) em <see cref="ArquivoDeComponente"/>. Nullable porque a obrigatoriedade e de
  /// negocio e vale para Peca de Pedido, nao para toda linha de catalogo (regra 18) — quem cobra e
  /// <c>MontagemDeEstruturaUseCase.CriarPeca</c>.
  ///
  /// <para>
  /// Escalar, SEM propriedade de navegacao, e isso e desenho e nao esquecimento: sem navegacao nao
  /// existe <c>Include</c> que arraste o VARBINARY(MAX) para uma listagem paginada de catalogo. O
  /// blob so e lido pelo repositorio do arquivo, por consulta propria.
  /// </para>
  /// </summary>
  public int? ArquivoSolidoId { get; set; }

  // ArquivoFoto existe em dbo.Componente e NAO e mapeada aqui: a foto esta fora do escopo da Fase
  // 2B (decisao do usuario). Coluna nullable, entao o INSERT do EF sem ela e valido.
}
