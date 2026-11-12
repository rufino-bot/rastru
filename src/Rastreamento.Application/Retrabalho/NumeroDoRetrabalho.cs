namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// O numero do Pedido de Retrabalho (regra 39 de `specs/01`): no maximo <see cref="Maximo"/> caracteres, o da coluna
/// `Pedido.Numero`. A sugestao do servidor e `{numero do original}-R{k}`, o menor `k` livre; quando isso passaria do
/// maximo, o numero do ORIGINAL e cortado a direita para o sufixo caber (o sufixo `-R{k}` e o que distingue um
/// Retrabalho do outro e nunca e cortado). A pura e sem I/O: quem le os numeros ja usados e o caso de uso.
/// </summary>
public static class NumeroDoRetrabalho
{
  public const int Maximo = 30;

  /// <summary>Folga para o sufixo `-R` e ate cinco digitos de `k` na consulta dos numeros ja usados.</summary>
  private const int FolgaDoSufixo = 7;

  /// <summary>
  /// O prefixo comum a toda sugestao possivel para o original: `{numero}-R` quando cabe com folga, senao o inicio do
  /// numero cortado onde nenhum candidato corta mais cedo. Serve de filtro da consulta, e o resto e conferido em
  /// <see cref="Sugerir"/>.
  /// </summary>
  public static string PrefixoDaConsulta(string numero) =>
      numero.Length <= Maximo - FolgaDoSufixo ? $"{numero}-R" : numero[..(Maximo - FolgaDoSufixo)];

  /// <summary>O menor `k` a partir de 1 cujo candidato nenhum dos <paramref name="usados"/> ocupa (sem diferenciar caixa, como `UQ_Pedido_Numero`).</summary>
  public static string Sugerir(string numero, IEnumerable<string> usados)
  {
    var ocupados = usados.ToHashSet(StringComparer.OrdinalIgnoreCase);
    for (var k = 1; ; k++)
    {
      var sufixo = $"-R{k}";
      var corpo = numero.Length + sufixo.Length <= Maximo ? numero : numero[..(Maximo - sufixo.Length)];
      var candidato = corpo + sufixo;
      if (!ocupados.Contains(candidato)) return candidato;
    }
  }
}
