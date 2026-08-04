using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Filtro e faixa de uma pagina do catalogo. <c>Pagina</c> e 1-based. Vive junto da interface
/// porque faz parte do contrato dela — quem implementa precisa dos quatro campos.
/// </summary>
public sealed record FiltroDeComponente(
    string? Busca, bool IncluirInativos, int Pagina, int Tamanho);

public interface IComponenteRepository
{
  /// <summary>
  /// Retorna o componente RASTREADO (sem <c>AsNoTracking</c>): <c>Editar</c> e
  /// <c>DefinirAtivo</c> mutam a entidade e contam com o change tracking.
  /// </summary>
  Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct);

  /// <summary>
  /// Existe para o caso de uso detectar duplicidade ANTES do insert e devolver erro de negocio,
  /// em vez de deixar a violacao de <c>UQ_Componente_Codigo</c> estourar como excecao ate a API.
  /// </summary>
  Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct);

  /// <summary>
  /// Devolve a pagina pedida e o total que casa com o MESMO filtro. O total vem separado porque
  /// sem ele o front nao sabe quantas paginas existem; contado com os mesmos criterios porque um
  /// total sem filtro faria a tela oferecer paginas que nao existem.
  /// </summary>
  Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct);

  Task AdicionarAsync(Componente componente, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}
