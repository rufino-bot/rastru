using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Ordem de uma pagina de Pedidos. <c>Recentes</c> (<c>DataAbertura</c> decrescente, depois
/// <c>Id</c> decrescente) e a padrao e a que a listagem sempre teve.
/// </summary>
public enum OrdemDePedidos { Recentes, Numero, Cliente }

/// <summary>
/// Filtro, ordem e faixa de uma pagina de Pedidos. <c>Pagina</c> e 1-based. Lista vazia NAO restringe:
/// dentro de uma faceta vale OU, entre facetas vale E. <c>Busca</c> ja chega aparada e nula
/// quando em branco. Vive junto da interface pelo mesmo motivo de <see cref="FiltroDeComponente"/>, e a
/// <c>Ordem</c> e o ultimo parametro, com padrao, pelo mesmo motivo dela (decisao D1 do plano da 1F).
/// </summary>
public sealed record FiltroDePedidos(
    string? Busca, IReadOnlyList<string> Status, IReadOnlyList<int> Materiais, int Pagina, int Tamanho,
    OrdemDePedidos Ordem = OrdemDePedidos.Recentes);

public interface IPedidoRepository
{
  /// <summary>Entidade RASTREADA: `Editar` muta e conta com o change tracking.</summary>
  Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct);

  /// <summary>
  /// Existe para o use case detectar duplicidade ANTES do insert e devolver erro de negocio, em
  /// vez de deixar a violacao de UQ_Pedido_Numero estourar como excecao ate a API.
  /// </summary>
  Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct);

  /// <summary>
  /// Uma pagina do filtro, com o total do MESMO filtro. Ordem, a de `FiltroDePedidos.Ordem`:
  /// `Recentes` (a padrao) e `DataAbertura` decrescente, depois `Id` decrescente; `Numero` e
  /// `Numero` crescente; `Cliente` e `Cliente` crescente, depois `Id` decrescente. Toda opcao
  /// termina em ordem total (`Numero` e unico, o desempate por `Id` cobre o resto), sem a qual
  /// `Skip/Take` repete e pula linhas entre paginas. Pedido nao tem `Ativo`: nao ha filtro de
  /// ativo/inativo.
  /// </summary>
  Task<(IReadOnlyList<Pedido> Itens, int Total)> ListarAsync(FiltroDePedidos filtro, CancellationToken ct);

  /// <summary>Quantos Pedidos ha em cada status, sobre a tabela inteira; status sem Pedido nao aparece.</summary>
  Task<IReadOnlyDictionary<string, int>> ContarPorStatusAsync(CancellationToken ct);

  /// <summary>
  /// Os `quantos` Pedidos mais antigos (`DataAbertura` crescente) cujo status NAO esta em
  /// `foraDosStatus`.
  /// </summary>
  Task<IReadOnlyList<Pedido>> ListarMaisAntigosAsync(
      IReadOnlyCollection<string> foraDosStatus, int quantos, CancellationToken ct);

  /// <summary>
  /// Os Materiais que aparecem em algum no de algum Pedido (`EstruturaMaterial`), ativos ou nao —
  /// o que o filtro por Material pode oferecer. Ordem: descricao, depois codigo.
  /// </summary>
  Task<IReadOnlyList<Material>> ListarMateriaisEmUsoAsync(CancellationToken ct);

  /// <summary>A pausa aberta de cada Pedido pedido que tem uma; os demais nao aparecem.</summary>
  Task<IReadOnlyDictionary<int, PausaAberta>> ListarPausasAbertasAsync(IReadOnlyCollection<int> pedidoIds, CancellationToken ct);

  Task AdicionarAsync(Pedido pedido, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}
