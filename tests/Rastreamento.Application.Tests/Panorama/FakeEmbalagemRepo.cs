using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>
/// `IEmbalagemRepository` em memoria: a ultima linha de cada Peca e arranjo do teste (o calculo de
/// `VoltouAProducaoDepois` e do repositorio real e tem teste proprio la). Conta as consultas para provar a leitura em lote.
/// </summary>
internal sealed class FakeEmbalagemRepo : IEmbalagemRepository
{
  public Dictionary<int, UltimaEmbalagem> Ultimas { get; } = new();

  public int ConsultasDeUltimas { get; private set; }

  public Task<IReadOnlyList<UltimaEmbalagem>> ListarUltimasAsync(
      IReadOnlyCollection<int> pecaIds, IReadOnlyCollection<string> posicoesDeProducao, CancellationToken ct)
  {
    ConsultasDeUltimas++;
    return Task.FromResult<IReadOnlyList<UltimaEmbalagem>>(
        Ultimas.Values.Where(u => pecaIds.Contains(u.EstruturaItemId)).ToList());
  }

  public Task GravarAsync(Embalagem linha, CancellationToken ct) => Task.CompletedTask;
}
