namespace Rastreamento.Application.Cadastros;

/// <summary>
/// A regra de atraso do Pedido (regra 33 do `01`) e o "hoje" contra o qual ela compara. Mora num lugar
/// so: a lista, a Home e a pagina do Pedido leem o `Atrasado` que sai daqui, e nenhuma tela o recalcula.
/// </summary>
public static class PrazoDeEntrega
{
  /// <summary>
  /// GMT-3 fixo, o mesmo offset da borda de fuso da API (`HorarioDeBrasiliaJsonConverter`), e pelo mesmo
  /// motivo: a busca do fuso por nome lanca num host sem ICU.
  /// </summary>
  private static readonly TimeSpan OffsetDeBrasilia = TimeSpan.FromHours(-3);

  /// <summary>Status em que o Pedido acabou: nao fica atrasado e nao entra nos mais urgentes.</summary>
  public static readonly IReadOnlyList<string> StatusEncerrados = ["Concluido", "Cancelado"];

  public static DateOnly HojeEmBrasilia(TimeProvider relogio) =>
      DateOnly.FromDateTime(relogio.GetUtcNow().ToOffset(OffsetDeBrasilia).DateTime);

  /// <summary>Vence hoje ainda nao e atraso: so o prazo anterior a hoje conta.</summary>
  public static bool EstaAtrasado(DateOnly dataEntrega, string status, DateOnly hoje) =>
      dataEntrega < hoje && !StatusEncerrados.Contains(status);
}
