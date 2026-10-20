namespace Rastreamento.Domain.Entities;

/// <summary>
/// Uma linha do registro das afericoes de uma Peca (spec da Fase 5A, secao 3.4): a amostra medida que
/// decide o significado de `NaExpedicao` para ela (D3). SO INCLUSAO, como `Movimentacao` — nenhum codigo
/// edita nem apaga uma linha; correcao e um Estorno que aponta a original (`EstornoDeId`, uma vez so por
/// `UX_RelatorioDimensional_EstornoDe`). A afericao valida da Peca e a ultima nao estornada. Nao guarda
/// cotas nem medidas (D2): so o resultado, quem e quando.
/// </summary>
public class RelatorioDimensional
{
  public int Id { get; set; }
  public int EstruturaItemId { get; set; }

  /// <summary>Um dos <see cref="TiposDeAfericao"/> (`CK_RelatorioDimensional_Tipo`).</summary>
  public string Tipo { get; set; } = string.Empty;

  /// <summary>
  /// Um dos <see cref="ResultadosDeAfericao"/>. Obrigatorio na `Afericao` (`CK_RelatorioDimensional_ResultadoNaAfericao`);
  /// o `Estorno` nao o carrega.
  /// </summary>
  public string? Resultado { get; set; }

  /// <summary>So no Estorno: a afericao que ele desfaz.</summary>
  public int? EstornoDeId { get; set; }

  /// <summary>UTC, preenchido pelo caso de uso (o DEFAULT do banco e rede, nao contrato).</summary>
  public DateTime DataHora { get; set; }

  /// <summary>O autor — quem pode estornar sem ser PCP ou Administrador.</summary>
  public int UsuarioId { get; set; }
}

/// <summary>Os tipos de `CK_RelatorioDimensional_Tipo`.</summary>
public static class TiposDeAfericao
{
  public const string Afericao = "Afericao";
  public const string Estorno = "Estorno";
}

/// <summary>Os resultados de `CK_RelatorioDimensional_Resultado`.</summary>
public static class ResultadosDeAfericao
{
  public const string Conforme = "Conforme";
  public const string NaoConforme = "NaoConforme";
}
