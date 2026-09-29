using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
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

  private const string ErroDePedidoNaoEncontrado = "Pedido nao encontrado.";

  /// <summary>Fase 1 so abre Pedido de fabricacao; Retrabalho e Fase 5.</summary>
  private const string TipoFabricacao = "Fabricacao";

  /// <summary>Todo Pedido nasce Aberto; quem muda o status e o primeiro apontamento (Fase 3).</summary>
  private const string StatusAberto = "Aberto";

  /// <summary>Quantas linhas a listagem devolve quando o cliente nao pede tamanho.</summary>
  public const int TamanhoDePaginaPadrao = 20;

  /// <summary>Teto de linhas por pagina; nao ha CHECK equivalente no banco.</summary>
  public const int TamanhoDePaginaMaximo = 100;

  /// <summary>Status validos, na ordem do `CK_Pedido_Status`; e a ordem em que o resumo os devolve.</summary>
  private static readonly string[] StatusValidos =
      ["Aberto", "EmProducao", "AguardandoExpedicao", "Concluido", "Cancelado"];

  /// <summary>Status em que o Pedido acabou; ficam fora dos "mais antigos abertos" do resumo.</summary>
  private static readonly string[] StatusEncerrados = ["Concluido", "Cancelado"];

  private const int QuantosMaisAntigos = 5;

  private const string ErroDeFaixaInvalida =
      "Pagina deve ser 1 ou maior e tamanho deve estar entre 1 e 100.";

  private const string ErroDeMaterialInvalido =
      "Material deve ser uma lista de numeros inteiros positivos separados por virgula.";

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

    return Result<PedidoDto>.Ok(Projetar(pedido, null));   // Pedido novo nunca esta pausado
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
      return Result<PedidoDto>.Falha(ErroDePedidoNaoEncontrado, TipoDeErro.NaoEncontrado);

    // So e conflito se o numero pertencer a OUTRO pedido: manter o proprio numero e no-op.
    var homonimo = await _repositorio.ObterPorNumeroAsync(numero, ct);
    if (homonimo is not null && homonimo.Id != id)
      return Result<PedidoDto>.Falha(ErroDeNumeroDuplicado, TipoDeErro.Conflito);

    pedido.Numero = numero;
    pedido.Cliente = cliente;
    await _repositorio.SalvarAlteracoesAsync(ct);

    var pausas = await _repositorio.ListarPausasAbertasAsync([id], ct);
    return Result<PedidoDto>.Ok(Projetar(pedido, pausas.GetValueOrDefault(id)));
  }

  /// <summary>
  /// Devolve `Result` porque a faixa, o status e o material pedidos podem ser invalidos, e isso e
  /// 400. Pagina ALEM do fim e sucesso com itens vazios. `status` e `material` chegam como listas
  /// separadas por virgula: pedaco vazio e ignorado e valor repetido colapsa.
  /// </summary>
  public async Task<Result<PaginaDto<PedidoDto>>> Listar(
      string? busca, string? status, string? material, int pagina, int tamanho, CancellationToken ct)
  {
    if (pagina < 1 || tamanho < 1 || tamanho > TamanhoDePaginaMaximo)
      return Result<PaginaDto<PedidoDto>>.Falha(ErroDeFaixaInvalida, TipoDeErro.Validacao);

    var statusPedidos = SepararPedacos(status);
    var desconhecido = statusPedidos.FirstOrDefault(s => !StatusValidos.Contains(s));
    if (desconhecido is not null)
      return Result<PaginaDto<PedidoDto>>.Falha(
          $"Status '{desconhecido}' desconhecido. Aceitos: {string.Join(", ", StatusValidos)}.",
          TipoDeErro.Validacao);

    var materiais = new List<int>();
    foreach (var pedaco in SepararPedacos(material))
    {
      // NumberStyles.None: so digitos. "-3", "1.5" e " 5" nao passam, e "0" cai no `<= 0`.
      if (!int.TryParse(pedaco, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        return Result<PaginaDto<PedidoDto>>.Falha(ErroDeMaterialInvalido, TipoDeErro.Validacao);
      if (!materiais.Contains(id)) materiais.Add(id);
    }

    var buscaAparada = busca?.Trim();
    var (pedidos, total) = await _repositorio.ListarAsync(
        new FiltroDePedidos(
            string.IsNullOrEmpty(buscaAparada) ? null : buscaAparada, statusPedidos, materiais, pagina, tamanho),
        ct);

    return Result<PaginaDto<PedidoDto>>.Ok(
        new PaginaDto<PedidoDto>(await ProjetarComPausas(pedidos, ct), total, pagina, tamanho));
  }

  /// <summary>
  /// A contagem por status e sobre TODOS os Pedidos, nunca sobre uma pagina — e o que a Home
  /// mostra. Os cinco status saem sempre, na ordem do DDL, com 0 no que nao tem Pedido.
  /// </summary>
  public async Task<ResumoDePedidosDto> Resumo(CancellationToken ct)
  {
    var contagem = await _repositorio.ContarPorStatusAsync(ct);
    var maisAntigos = await _repositorio.ListarMaisAntigosAsync(StatusEncerrados, QuantosMaisAntigos, ct);

    return new ResumoDePedidosDto(
        StatusValidos.Select(s => new ContagemDeStatusDto(s, contagem.GetValueOrDefault(s))).ToList(),
        await ProjetarComPausas(maisAntigos, ct));
  }

  public async Task<IReadOnlyList<MaterialResumoDto>> MateriaisEmUso(CancellationToken ct) =>
      (await _repositorio.ListarMateriaisEmUsoAsync(ct))
          .Select(m => new MaterialResumoDto(m.Id, m.Codigo, m.Descricao))
          .ToList();

  private async Task<IReadOnlyList<PedidoDto>> ProjetarComPausas(
      IReadOnlyList<Pedido> pedidos, CancellationToken ct)
  {
    var pausas = await _repositorio.ListarPausasAbertasAsync(pedidos.Select(p => p.Id).ToList(), ct);
    return pedidos.Select(p => Projetar(p, pausas.GetValueOrDefault(p.Id))).ToList();
  }

  /// <summary>Lista separada por virgula: aparada, sem pedaco vazio e sem repetido (a ordem da primeira aparicao fica).</summary>
  private static List<string> SepararPedacos(string? lista) =>
      (lista ?? string.Empty)
          .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
          .Distinct()
          .ToList();

  public async Task<Result<PedidoDto>> Obter(int id, CancellationToken ct)
  {
    var pedido = await _repositorio.ObterPorIdAsync(id, ct);
    if (pedido is null)
      return Result<PedidoDto>.Falha(ErroDePedidoNaoEncontrado, TipoDeErro.NaoEncontrado);
    var pausas = await _repositorio.ListarPausasAbertasAsync([id], ct);
    return Result<PedidoDto>.Ok(Projetar(pedido, pausas.GetValueOrDefault(id)));
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

  private static PedidoDto Projetar(Pedido p, PausaAberta? pausa) =>
      new(p.Id, p.Numero, p.Cliente, p.Tipo, p.Status, p.DataAbertura, p.CriadoPorUsuarioId, PausaResumoDto.De(pausa));
}
