using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Componente (catalogo): criar, editar, listar e (in)ativar. Componente nao se
/// exclui — linhas de EstruturaItem, ComponenteFilhoPadrao, ComponenteMaterialPadrao e
/// ComponenteRoteiroPadrao apontam para ele (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDeComponenteUseCase
{
  /// <summary>Quantas linhas a listagem devolve quando o cliente nao pede tamanho.</summary>
  public const int TamanhoDePaginaPadrao = 20;

  /// <summary>
  /// Teto de linhas por pagina. Existe para `?tamanho=100000` nao virar negacao de servico
  /// trivial. Nao ha CHECK equivalente no banco: esta guarda e a unica defesa (adendo B14).
  /// </summary>
  public const int TamanhoDePaginaMaximo = 100;

  private static readonly string[] TiposValidos = ["Bruto", "Fabricado", "Montagem"];

  private const string ErroDeCampoObrigatorio = "Codigo e descricao sao obrigatorios.";

  private const string ErroDeTipoInvalido = "Tipo deve ser Bruto, Fabricado ou Montagem.";

  private const string ErroDeCodigoDuplicado = "Ja existe um Componente com este codigo.";

  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";

  private const string ErroDeFaixaInvalida =
      "Pagina deve ser 1 ou maior e tamanho deve estar entre 1 e 100.";

  private readonly IComponenteRepository _repositorio;

  public CadastroDeComponenteUseCase(IComponenteRepository repositorio) =>
      _repositorio = repositorio;

  public async Task<Result<ComponenteDto>> Cadastrar(NovoComponenteDto novo, CancellationToken ct)
  {
    var (codigo, descricao, tipo) = Normalizar(novo);
    var invalido = Validar(codigo, descricao, tipo);
    if (invalido is not null) return Result<ComponenteDto>.Falha(invalido, TipoDeErro.Validacao);

    // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Componente_Codigo
    // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
    if (await _repositorio.ObterPorCodigoAsync(codigo, ct) is not null)
      return Result<ComponenteDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    var componente = new Componente
    {
      Codigo = codigo,
      Descricao = descricao,
      Tipo = tipo,
      Ativo = true,
    };
    await _repositorio.AdicionarAsync(componente, ct);
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<ComponenteDto>.Ok(Projetar(componente));
  }

  public async Task<Result<ComponenteDto>> Editar(
      int id, NovoComponenteDto alterado, CancellationToken ct)
  {
    var (codigo, descricao, tipo) = Normalizar(alterado);
    var invalido = Validar(codigo, descricao, tipo);
    if (invalido is not null) return Result<ComponenteDto>.Falha(invalido, TipoDeErro.Validacao);

    var componente = await _repositorio.ObterPorIdAsync(id, ct);
    if (componente is null)
      return Result<ComponenteDto>.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    // So e conflito se o codigo pertencer a OUTRA linha: manter o proprio codigo e no-op.
    var homonimo = await _repositorio.ObterPorCodigoAsync(codigo, ct);
    if (homonimo is not null && homonimo.Id != id)
      return Result<ComponenteDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    componente.Codigo = codigo;
    componente.Descricao = descricao;
    componente.Tipo = tipo;
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<ComponenteDto>.Ok(Projetar(componente));
  }

  /// <summary>
  /// Devolve `Result` — e nao a pagina direto — porque a faixa pedida pode ser invalida, e isso
  /// e 400, nao 200 com lista vazia. Pagina ALEM do fim, por outro lado, e sucesso com itens
  /// vazios: fim de lista nao e pedido invalido.
  /// </summary>
  public async Task<Result<PaginaDto<ComponenteDto>>> Listar(
      string? busca, bool incluirInativos, int pagina, int tamanho, CancellationToken ct)
  {
    if (pagina < 1 || tamanho < 1 || tamanho > TamanhoDePaginaMaximo)
      return Result<PaginaDto<ComponenteDto>>.Falha(ErroDeFaixaInvalida, TipoDeErro.Validacao);

    var (itens, total) = await _repositorio.ListarAsync(
        new FiltroDeComponente(busca, incluirInativos, pagina, tamanho), ct);

    return Result<PaginaDto<ComponenteDto>>.Ok(
        new PaginaDto<ComponenteDto>(itens.Select(Projetar).ToList(), total, pagina, tamanho));
  }

  /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /componentes/{id}/ativo`.</summary>
  public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
  {
    var componente = await _repositorio.ObterPorIdAsync(id, ct);
    if (componente is null)
      return Result.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    componente.Ativo = ativo;
    await _repositorio.SalvarAlteracoesAsync(ct);
    return Result.Ok();
  }

  /// <summary>
  /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo da
  /// segunda leitura nao entra no caminho feliz. O campo e `codigo` porque a unicidade e do
  /// Codigo (`UQ_Componente_Codigo`); a Descricao pode repetir a vontade.
  /// </summary>
  public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string codigo, CancellationToken ct)
  {
    var existente = await _repositorio.ObterPorCodigoAsync(Normalizar(codigo), ct);
    return existente is null
        ? null
        : new ValorDuplicadoDto("codigo", !existente.Ativo, existente.Id);
  }

  /// <summary>
  /// Devolve a mensagem do primeiro problema, ou null se estiver tudo certo. `Tipo` e validado
  /// aqui, e nao pelo CK_Componente_Tipo: excecao de CHECK subiria como 500 em vez de 400.
  /// </summary>
  private static string? Validar(string codigo, string descricao, string tipo)
  {
    if (codigo.Length == 0 || descricao.Length == 0)
      return ErroDeCampoObrigatorio;
    if (!TiposValidos.Contains(tipo)) return ErroDeTipoInvalido;
    return null;
  }

  private static (string Codigo, string Descricao, string Tipo) Normalizar(NovoComponenteDto d) =>
      (Normalizar(d.Codigo), Normalizar(d.Descricao), Normalizar(d.Tipo));

  /// <summary>
  /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
  /// " SUP-001 " colidir com "SUP-001" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre
  /// o null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao
  /// de nulabilidade nao e garantia em tempo de execucao.
  /// </summary>
  private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

  private static ComponenteDto Projetar(Componente c) =>
      new(c.Id, c.Codigo, c.Descricao, c.Tipo, c.Ativo);
}
