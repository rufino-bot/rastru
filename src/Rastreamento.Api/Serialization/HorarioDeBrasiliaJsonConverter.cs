using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rastreamento.Api.Serialization;

/// <summary>
/// A borda de fuso da aplicacao. Por dentro tudo e UTC (dominio, banco, casos de uso); toda
/// <see cref="DateTime"/> que sai no JSON vai em ISO 8601 com offset -03:00, e toda data que
/// entra volta a ser UTC.
/// </summary>
/// <remarks>
/// <para>
/// Registrado uma unica vez no <c>Program.cs</c> justamente para que nenhum endpoint precise
/// lembrar de converter: com conversao manual por propriedade, uma chamada esquecida em
/// <c>DataAbertura</c>/<c>DataConclusao</c>/<c>DataInspecao</c>/<c>CriadoEm</c> produziria um
/// horario UTC rotulado como local — errado em silencio e sem teste que pegue.
/// </para>
/// <para>
/// Offset fixo, e nao <c>TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")</c>: a spec
/// define GMT-3 fixo, e a busca por fuso lanca <c>TimeZoneNotFoundException</c> na inicializacao
/// do tipo num host sem ICU — o que viraria 500 em toda rota, e nao uma falha visivel de startup.
/// </para>
/// </remarks>
public sealed class HorarioDeBrasiliaJsonConverter : JsonConverter<DateTime>
{
  private static readonly TimeSpan OffsetDeBrasilia = TimeSpan.FromHours(-3);

  public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    var texto = reader.GetString();

    if (!DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lido))
      throw new JsonException($"Data em formato invalido: '{texto}'.");

    // Simetrico com o Write: entrada sem offset explicito e lida como horario de Brasilia
    // (Unspecified), entrada com offset ou com 'Z' e respeitada. Nos dois casos o que chega
    // na aplicacao e UTC.
    return lido.Kind == DateTimeKind.Unspecified
        ? new DateTimeOffset(lido, OffsetDeBrasilia).UtcDateTime
        : lido.ToUniversalTime();
  }

  public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
  {
    // Kind.Unspecified so aparece se alguem perder o Kind no caminho; tratar como UTC mantem
    // a convencao da aplicacao em vez de herdar o fuso do servidor.
    var utc = value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();

    writer.WriteStringValue(new DateTimeOffset(utc).ToOffset(OffsetDeBrasilia));
  }
}
