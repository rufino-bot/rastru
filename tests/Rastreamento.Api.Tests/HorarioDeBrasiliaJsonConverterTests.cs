using System.Text.Json;
using Rastreamento.Api.Serialization;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Testes do conversor que faz a borda de fuso da API. Cobrem os dois sentidos porque o risco do
/// conversor global e justamente a desserializacao: um <c>Read</c> errado transformaria toda data
/// de entrada dos endpoints das proximas fases num instante deslocado, em silencio.
/// </summary>
public class HorarioDeBrasiliaJsonConverterTests
{
  private static readonly JsonSerializerOptions Opcoes =
      new() { Converters = { new HorarioDeBrasiliaJsonConverter() } };

  [Fact]
  public void Serializa_UTC_com_offset_menos_3()
  {
    var utc = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc);

    var json = JsonSerializer.Serialize(utc, Opcoes);

    Assert.Equal("\"2026-07-23T12:00:00-03:00\"", json);
  }

  [Fact]
  public void Serializa_data_sem_Kind_como_se_fosse_UTC()
  {
    // Convencao da aplicacao: por dentro tudo e UTC. Herdar o fuso do servidor faria a
    // resposta mudar conforme a maquina que hospeda a API.
    var semKind = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Unspecified);

    var json = JsonSerializer.Serialize(semKind, Opcoes);

    Assert.Equal("\"2026-07-23T12:00:00-03:00\"", json);
  }

  [Fact]
  public void Serializa_data_nulavel_preenchida_com_offset_menos_3()
  {
    DateTime? utc = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc);

    var json = JsonSerializer.Serialize(utc, Opcoes);

    Assert.Equal("\"2026-07-23T12:00:00-03:00\"", json);
  }

  [Fact]
  public void Serializa_data_nulavel_vazia_como_null()
  {
    DateTime? nenhuma = null;

    Assert.Equal("null", JsonSerializer.Serialize(nenhuma, Opcoes));
  }

  [Theory]
  [InlineData("\"2026-07-23T12:00:00-03:00\"")] // com offset explicito
  [InlineData("\"2026-07-23T15:00:00Z\"")]      // em UTC
  [InlineData("\"2026-07-23T12:00:00\"")]       // sem offset: lido como horario de Brasilia
  public void Desserializa_sempre_para_o_mesmo_instante_UTC(string json)
  {
    var lido = JsonSerializer.Deserialize<DateTime>(json, Opcoes);

    Assert.Equal(DateTimeKind.Utc, lido.Kind);
    Assert.Equal(new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc), lido);
  }

  [Fact]
  public void Round_trip_preserva_o_instante()
  {
    var original = new DateTime(2026, 12, 31, 23, 59, 58, DateTimeKind.Utc);

    var volta = JsonSerializer.Deserialize<DateTime>(
        JsonSerializer.Serialize(original, Opcoes), Opcoes);

    Assert.Equal(original, volta);
  }

  [Fact]
  public void Data_em_formato_invalido_vira_JsonException()
  {
    Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"ontem\"", Opcoes));
  }
}
