using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Pedido: criar, editar, listar e obter. Nao ha inativacao nem exclusao — documento
/// se corrige por edicao (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDePedidoUseCase
{
    private const string ErroDeCampoObrigatorio = "Numero e cliente sao obrigatorios.";

    private const string ErroDeNumeroDuplicado = "Ja existe um Pedido com este numero.";

    /// <summary>Fase 1 so abre Pedido de fabricacao; Retrabalho e Fase 5.</summary>
    private const string TipoFabricacao = "Fabricacao";

    /// <summary>Todo Pedido nasce Aberto; quem muda o status e o primeiro apontamento (Fase 3).</summary>
    private const string StatusAberto = "Aberto";

    private readonly IPedidoRepository _repositorio;

    public CadastroDePedidoUseCase(IPedidoRepository repositorio) => _repositorio = repositorio;

    public async Task<Result<PedidoDto>> Cadastrar(
        NovoPedidoDto novo, int usuarioId, CancellationToken ct)
    {
        var (numero, cliente) = Normalizar(novo);
        if (numero.Length == 0 || cliente.Length == 0)
            return Result<PedidoDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

        // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Pedido_Numero
        // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
        if (await _repositorio.ObterPorNumeroAsync(numero, ct) is not null)
            return Result<PedidoDto>.Falha(ErroDeNumeroDuplicado, TipoDeErro.Conflito);

        var pedido = new Pedido
        {
            Numero = numero,
            Cliente = cliente,
            Tipo = TipoFabricacao,
            Status = StatusAberto,
            // Em UTC, como todo o resto do sistema. O DEFAULT do banco existe, mas o EF sempre
            // manda a coluna no INSERT — entao quem define o valor de verdade e esta linha.
            DataAbertura = DateTime.UtcNow,
            CriadoPorUsuarioId = usuarioId,
        };

        await _repositorio.AdicionarAsync(pedido, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<PedidoDto>.Ok(Projetar(pedido));
    }

    /// <remarks>
    /// Editar nao toca em `CriadoPorUsuarioId`: autoria e do momento da criacao. Tambem nao ha
    /// guarda por status — na Fase 1 todo Pedido esta Aberto, porque nada transiciona status
    /// ainda. Quando a Fase 3 introduzir a transicao, a guarda de "so edita Pedido Aberto"
    /// pertence a ela, nao a esta.
    /// </remarks>
    public async Task<Result<PedidoDto>> Editar(
        int id, NovoPedidoDto alterado, CancellationToken ct)
    {
        var (numero, cliente) = Normalizar(alterado);
        if (numero.Length == 0 || cliente.Length == 0)
            return Result<PedidoDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

        var pedido = await _repositorio.ObterPorIdAsync(id, ct);
        if (pedido is null)
            return Result<PedidoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado);

        // So e conflito se o numero pertencer a OUTRO pedido: manter o proprio numero e no-op.
        var homonimo = await _repositorio.ObterPorNumeroAsync(numero, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<PedidoDto>.Falha(ErroDeNumeroDuplicado, TipoDeErro.Conflito);

        pedido.Numero = numero;
        pedido.Cliente = cliente;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<PedidoDto>.Ok(Projetar(pedido));
    }

    public async Task<IReadOnlyList<PedidoDto>> Listar(CancellationToken ct)
    {
        var pedidos = await _repositorio.ListarAsync(ct);
        return pedidos.Select(Projetar).ToList();
    }

    public async Task<Result<PedidoDto>> Obter(int id, CancellationToken ct)
    {
        var pedido = await _repositorio.ObterPorIdAsync(id, ct);
        return pedido is null
            ? Result<PedidoDto>.Falha("Pedido nao encontrado.", TipoDeErro.NaoEncontrado)
            : Result<PedidoDto>.Ok(Projetar(pedido));
    }

    /// <summary>
    /// Detalhe do 409. `ExisteInativo` e sempre false — Pedido nao tem coluna `Ativo`, entao nao
    /// existe "reativar o existente" aqui; o caminho de correcao e editar o Pedido que ja existe.
    /// </summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string numero, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorNumeroAsync(Normalizar(numero), ct);
        return existente is null ? null : new ValorDuplicadoDto("numero", false, existente.Id);
    }

    private static (string Numero, string Cliente) Normalizar(NovoPedidoDto d) =>
        (Normalizar(d.Numero), Normalizar(d.Cliente));

    /// <summary>
    /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
    /// " PED-001 " colidir com "PED-001" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre
    /// o null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao
    /// de nulabilidade nao e garantia em tempo de execucao.
    /// </summary>
    private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

    private static PedidoDto Projetar(Pedido p) =>
        new(p.Id, p.Numero, p.Cliente, p.Tipo, p.Status, p.DataAbertura, p.CriadoPorUsuarioId);
}
