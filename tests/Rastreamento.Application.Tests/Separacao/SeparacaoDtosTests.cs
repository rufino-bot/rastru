using System.Text.Json;
using Rastreamento.Application.Separacao;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>
/// A forma JSON da lista do Separador (spec da Fase 4, secao 5.2): `destino` e `{ setorId, nome }`, ou
/// null quando o no nao tem Roteiro. `JsonSerializerDefaults.Web` e o camelCase que o ASP.NET Core usa
/// nos controllers.
/// </summary>
public class SeparacaoDtosTests
{
  private static readonly JsonSerializerOptions OpcoesDaApi = new(JsonSerializerDefaults.Web);

  private static PendenciaDeSeparacaoDto Pendencia(SetorDeDestinoDto? destino) =>
      new(new NoDaPendenciaDto(42, "Quadro soldado", "QD-01", 3, "AG-01"),
          new PedidoDaPendenciaDto(5, "PED-77", "Cliente X", new DateOnly(2026, 10, 20), Atrasado: false, Pausa: null),
          destino,
          new[] { new MaterialPendenteDto(7, "PAR-M8", "Parafuso M8", "UN", 10, 8, 2) });

  [Fact]
  public void Destino_da_pendencia_serializa_setorId_e_nome()
  {
    using var json = JsonDocument.Parse(
        JsonSerializer.Serialize(Pendencia(new SetorDeDestinoDto(3, "Solda")), OpcoesDaApi));

    var destino = json.RootElement.GetProperty("destino");
    Assert.Equal(3, destino.GetProperty("setorId").GetInt32());
    Assert.Equal("Solda", destino.GetProperty("nome").GetString());
    Assert.False(destino.TryGetProperty("id", out _));
  }

  [Fact]
  public void Destino_da_pendencia_sem_Roteiro_serializa_null()
  {
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(Pendencia(null), OpcoesDaApi));

    Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("destino").ValueKind);
  }
}
