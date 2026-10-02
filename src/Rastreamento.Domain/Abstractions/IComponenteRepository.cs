using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Ordem de uma pagina do catalogo. <c>Recentes</c> (<c>Id</c> decrescente) e a padrao desde a
/// decisao 9 da spec da 1F; <c>Codigo</c> e a ordem que a listagem tinha antes.
/// </summary>
public enum OrdemDeComponentes { Recentes, Codigo, Descricao }

/// <summary>
/// Filtro, ordem e faixa de uma pagina do catalogo. <c>Pagina</c> e 1-based. Vive junto da
/// interface porque faz parte do contrato dela — quem implementa precisa dos cinco campos. A
/// <c>Ordem</c> e o ultimo parametro, com padrao, para quem constroi o filtro sem ela continuar
/// compilando (decisao D1 do plano da 1F).
/// </summary>
public sealed record FiltroDeComponente(
    string? Busca, bool IncluirInativos, int Pagina, int Tamanho,
    OrdemDeComponentes Ordem = OrdemDeComponentes.Recentes);

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
  /// total sem filtro faria a tela oferecer paginas que nao existem. A ordem e a de
  /// <c>FiltroDeComponente.Ordem</c>, sempre total: <c>Codigo</c> e unico, e as demais desempatam
  /// por <c>Id</c>.
  /// </summary>
  Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct);

  Task AdicionarAsync(Componente componente, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}
