using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IPedidoRepository
{
    /// <summary>Entidade RASTREADA: `Editar` muta e conta com o change tracking.</summary>
    Task<Pedido?> ObterPorIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Existe para o use case detectar duplicidade ANTES do insert e devolver erro de negocio, em
    /// vez de deixar a violacao de UQ_Pedido_Numero estourar como excecao ate a API.
    /// </summary>
    Task<Pedido?> ObterPorNumeroAsync(string numero, CancellationToken ct);

    /// <summary>Sem filtro de ativo/inativo: Pedido nao tem essa coluna — a lista e completa.</summary>
    Task<IReadOnlyList<Pedido>> ListarAsync(CancellationToken ct);

    Task AdicionarAsync(Pedido pedido, CancellationToken ct);

    Task SalvarAlteracoesAsync(CancellationToken ct);
}
