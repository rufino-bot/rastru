namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha do registro do "Embalado" de uma Peca (regra 40): alguem marcou ou desmarcou que a Peca esta
/// embalada. SO INCLUSAO, como `MaterialSeparacao` — nenhum codigo da aplicacao edita nem apaga uma
/// linha; so a rotina de manutencao do banco poda as antigas, preservando a ultima de cada Peca. Quem
/// diz se o "Embalado" VALE e a leitura (ultima linha `Marcar`, condicao de producao e livro), nunca
/// uma coluna. Nao e posicao do livro e nao mexe em quantidade.
/// </summary>
public class Embalagem
{
  public int Id { get; set; }

  /// <summary>So Peca (no sem pai): a aplicacao recusa Item.</summary>
  public int EstruturaItemId { get; set; }

  /// <summary>Um dos <see cref="TiposDeEmbalagem"/> (`CK_Embalagem_Tipo`).</summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>Quem marcou ou desmarcou.</summary>
  public int UsuarioId { get; set; }
}

/// <summary>Os tipos de `CK_Embalagem_Tipo`.</summary>
public static class TiposDeEmbalagem
{
  public const string Marcar = "Marcar";
  public const string Desmarcar = "Desmarcar";
}
