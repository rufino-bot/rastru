using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Agrupamento, sempre sob um Pedido. Unico cadastro da Fase 1A com exclusao fisica —
/// e ela e guardada: so Agrupamento vazio, em Pedido Aberto.
/// </summary>
public sealed class CadastroDeAgrupamentoUseCase
{
    private const string ErroDeCodigoDuplicado =
        "Ja existe um Agrupamento com este codigo neste Pedido.";

    private static readonly string[] TiposValidos = ["Kit", "Avulso"];
    private const string StatusAberto = "Aberto";

    private readonly IAgrupamentoRepository _repositorio;
    private readonly IPedidoRepository _pedidos;

    public CadastroDeAgrupamentoUseCase(
        IAgrupamentoRepository repositorio, IPedidoRepository pedidos)
    {
        _repositorio = repositorio;
        _pedidos = pedidos;
    }

    public async Task<Result<AgrupamentoDto>> Cadastrar(
        int pedidoId, NovoAgrupamentoDto novo, int usuarioId, CancellationToken ct)
    {
        var codigo = Normalizar(novo.Codigo);
        var tipo = Normalizar(novo.Tipo);

        var invalido = Validar(codigo, novo.Quantidade, tipo);
        if (invalido is not null) return Result<AgrupamentoDto>.Falha(invalido, TipoDeErro.Validacao);

        if (await _pedidos.ObterPorIdAsync(pedidoId, ct) is null)
            return Result<AgrupamentoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado);

        // Checagem ANTES do insert: erro de negocio claro em vez de excecao de
        // UQ_Agrupamento_PedidoCodigo vazando ate a API. O indice segue como rede de seguranca
        // para a corrida entre as duas.
        if (await _repositorio.ObterPorPedidoECodigoAsync(pedidoId, codigo, ct) is not null)
            return Result<AgrupamentoDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

        var agrupamento = new Agrupamento
        {
            PedidoId = pedidoId,
            Codigo = codigo,
            Quantidade = novo.Quantidade,
            Tipo = tipo,
            CriadoPorUsuarioId = usuarioId,
            CriadoEm = DateTime.UtcNow,
        };

        await _repositorio.AdicionarAsync(agrupamento, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    /// <remarks>
    /// Nao troca `PedidoId` (mover Agrupamento de Pedido nao e operacao de cadastro), nem autoria,
    /// nem `CriadoEm`. Editar `Quantidade` e inocuo na Fase 1; a partir da Fase 3 ela conversa com
    /// a conservacao de quantidade, e a guarda correspondente pertence aquela fase.
    /// </remarks>
    public async Task<Result<AgrupamentoDto>> Editar(
        int id, NovoAgrupamentoDto alterado, CancellationToken ct)
    {
        var codigo = Normalizar(alterado.Codigo);
        var tipo = Normalizar(alterado.Tipo);

        var invalido = Validar(codigo, alterado.Quantidade, tipo);
        if (invalido is not null) return Result<AgrupamentoDto>.Falha(invalido, TipoDeErro.Validacao);

        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        if (agrupamento is null)
            return Result<AgrupamentoDto>.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado);

        // So e conflito se o codigo pertencer a OUTRO agrupamento do mesmo Pedido: manter o
        // proprio codigo e no-op.
        var homonimo = await _repositorio.ObterPorPedidoECodigoAsync(agrupamento.PedidoId, codigo, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<AgrupamentoDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

        agrupamento.Codigo = codigo;
        agrupamento.Quantidade = alterado.Quantidade;
        agrupamento.Tipo = tipo;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    public async Task<IReadOnlyList<AgrupamentoDto>> ListarPorPedido(
        int pedidoId, CancellationToken ct)
    {
        var agrupamentos = await _repositorio.ListarPorPedidoAsync(pedidoId, ct);
        return agrupamentos.Select(Projetar).ToList();
    }

    public async Task<Result<AgrupamentoDto>> Obter(int id, CancellationToken ct)
    {
        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        return agrupamento is null
            ? Result<AgrupamentoDto>.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado)
            : Result<AgrupamentoDto>.Ok(Projetar(agrupamento));
    }

    /// <summary>
    /// Exclusao fisica guardada. As duas recusas viajam como CODIGO no `Erro` ("AgrupamentoNaoVazio",
    /// "PedidoNaoAberto") porque e isso que o contrato de 409 da spec define no corpo; o controller
    /// so repassa e nao deriva comportamento da string. Ordem: existe -> Pedido Aberto -> vazio.
    /// </summary>
    public async Task<Result> Excluir(int id, CancellationToken ct)
    {
        var agrupamento = await _repositorio.ObterPorIdAsync(id, ct);
        if (agrupamento is null)
            return Result.Falha("Agrupamento nao encontrado.", TipoDeErro.NaoEncontrado);

        var pedido = await _pedidos.ObterPorIdAsync(agrupamento.PedidoId, ct);
        if (pedido is null || pedido.Status != StatusAberto)
            return Result.Falha("PedidoNaoAberto", TipoDeErro.Conflito);

        if (await _repositorio.TemEstruturaAsync(id, ct))
            return Result.Falha("AgrupamentoNaoVazio", TipoDeErro.Conflito);

        await _repositorio.RemoverAsync(agrupamento, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Detalhe do 409. `ExisteInativo` sempre false: Agrupamento nao tem `Ativo`.</summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(
        int pedidoId, string codigo, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorPedidoECodigoAsync(pedidoId, Normalizar(codigo), ct);
        return existente is null ? null : new ValorDuplicadoDto("codigo", false, existente.Id);
    }

    /// <summary>
    /// Devolve a mensagem do primeiro problema, ou null se estiver tudo certo. `Tipo` e validado
    /// aqui, e nao pelo CK_Agrupamento_Tipo: excecao de CHECK subiria como 500 em vez de 400.
    /// </summary>
    private static string? Validar(string codigo, decimal quantidade, string tipo)
    {
        if (codigo.Length == 0) return "Codigo e obrigatorio.";
        if (quantidade <= 0) return "Quantidade deve ser maior que zero.";
        if (!TiposValidos.Contains(tipo)) return "Tipo deve ser Kit ou Avulso.";
        return null;
    }

    /// <summary>
    /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
    /// " AG-01 " colidir com "AG-01" como UQ_Agrupamento_PedidoCodigo ja faria, e o
    /// `?? string.Empty` cobre o null que o desserializador de JSON entrega mesmo em propriedade
    /// nao-anulavel — a anotacao de nulabilidade nao e garantia em tempo de execucao.
    /// </summary>
    private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

    private static AgrupamentoDto Projetar(Agrupamento a) =>
        new(a.Id, a.PedidoId, a.Codigo, a.Quantidade, a.Tipo, a.CriadoEm, a.CriadoPorUsuarioId);
}
