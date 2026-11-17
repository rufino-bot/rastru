using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IEmbalagemRepository
{
  /// <summary>
  /// A ultima linha (maior `Id`) de cada Peca pedida que tem alguma, numa consulta so, e se existe linha de
  /// `dbo.Movimentacao` da Peca com `DataHora` posterior a dela e `DestinoPosicao` em `posicoesDeProducao`.
  /// Peca sem linha nao volta.
  /// </summary>
  Task<IReadOnlyList<UltimaEmbalagem>> ListarUltimasAsync(
      IReadOnlyCollection<int> pecaIds, IReadOnlyCollection<string> posicoesDeProducao, CancellationToken ct);

  Task GravarAsync(Embalagem linha, CancellationToken ct);
}
