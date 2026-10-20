using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Material: criar, editar, listar e (in)ativar. Material nao se exclui — linhas de
/// EstruturaMaterial e ComponenteMaterialPadrao apontam para ele (ver a spec da Fase 1,
/// "Politica de exclusao").
/// </summary>
public sealed class CadastroDeMaterialUseCase
{
  private const string ErroDeCampoObrigatorio =
      "Codigo, descricao e unidade de medida sao obrigatorios.";

  private const string ErroDeCodigoDuplicado = "Ja existe um Material com este codigo.";

  private const string ErroDeMaterialNaoEncontrado = "Material nao encontrado.";

  // Codigo proprio do cadastro: nao entra em CodigosDaSeparacao (decisao P7 do plano de backend da Fase 4).
  private const string ErroDeSetorDeEstoqueInvalido = "SetorDeEstoqueInvalido";

  private readonly IMaterialRepository _repositorio;
  private readonly ISetorRepository _setores;

  public CadastroDeMaterialUseCase(IMaterialRepository repositorio, ISetorRepository setores)
  {
    _repositorio = repositorio;
    _setores = setores;
  }

  public async Task<Result<MaterialDto>> Cadastrar(NovoMaterialDto novo, CancellationToken ct)
  {
    var (codigo, descricao, unidade) = Normalizar(novo);
    if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
      return Result<MaterialDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

    var setor = await ResolverSetorDeEstoque(novo.SetorDeEstoqueId, jaApontado: null, ct);
    if (setor.Falhou)
      return Result<MaterialDto>.Falha(ErroDeSetorDeEstoqueInvalido, TipoDeErro.Validacao);

    // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Material_Codigo
    // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
    if (await _repositorio.ObterPorCodigoAsync(codigo, ct) is not null)
      return Result<MaterialDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    var material = new Material
    {
      Codigo = codigo,
      Descricao = descricao,
      UnidadeMedida = unidade,
      Ativo = true,
      SetorDeEstoqueId = novo.SetorDeEstoqueId,
    };
    await _repositorio.AdicionarAsync(material, ct);
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<MaterialDto>.Ok(Projetar(material, setor.Setor));
  }

  public async Task<Result<MaterialDto>> Editar(
      int id, NovoMaterialDto alterado, CancellationToken ct)
  {
    var (codigo, descricao, unidade) = Normalizar(alterado);
    if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
      return Result<MaterialDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

    var material = await _repositorio.ObterPorIdAsync(id, ct);
    if (material is null)
      return Result<MaterialDto>.Falha(ErroDeMaterialNaoEncontrado, TipoDeErro.NaoEncontrado);

    var setor = await ResolverSetorDeEstoque(
        alterado.SetorDeEstoqueId, material.SetorDeEstoqueId, ct);
    if (setor.Falhou)
      return Result<MaterialDto>.Falha(ErroDeSetorDeEstoqueInvalido, TipoDeErro.Validacao);

    // So e conflito se o codigo pertencer a OUTRA linha: manter o proprio codigo e no-op.
    var homonimo = await _repositorio.ObterPorCodigoAsync(codigo, ct);
    if (homonimo is not null && homonimo.Id != id)
      return Result<MaterialDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

    material.Codigo = codigo;
    material.Descricao = descricao;
    material.UnidadeMedida = unidade;
    material.SetorDeEstoqueId = alterado.SetorDeEstoqueId;
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<MaterialDto>.Ok(Projetar(material, setor.Setor));
  }

  public async Task<IReadOnlyList<MaterialDto>> Listar(bool incluirInativos, CancellationToken ct)
  {
    var materiais = await _repositorio.ListarAsync(incluirInativos, ct);

    // Uma leitura de todos os Setores (inativos inclusive: o Material continua dizendo onde fica
    // depois de o Setor ser inativado), e nao uma por Material.
    var setores = (await _setores.ListarAsync(incluirInativos: true, ct)).ToDictionary(s => s.Id);
    return materiais
        .Select(m => Projetar(
            m, m.SetorDeEstoqueId is { } id && setores.TryGetValue(id, out var s) ? s : null))
        .ToList();
  }

  /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /materiais/{id}/ativo`.</summary>
  public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
  {
    var material = await _repositorio.ObterPorIdAsync(id, ct);
    if (material is null)
      return Result.Falha(ErroDeMaterialNaoEncontrado, TipoDeErro.NaoEncontrado);

    material.Ativo = ativo;
    await _repositorio.SalvarAlteracoesAsync(ct);
    return Result.Ok();
  }

  /// <summary>
  /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo da
  /// segunda leitura nao entra no caminho feliz. O campo e `codigo` porque a unicidade e do
  /// Codigo (`UQ_Material_Codigo`); a Descricao pode repetir a vontade.
  /// </summary>
  public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string codigo, CancellationToken ct)
  {
    var existente = await _repositorio.ObterPorCodigoAsync(Normalizar(codigo), ct);
    return existente is null
        ? null
        : new ValorDuplicadoDto("codigo", !existente.Ativo, existente.Id);
  }

  private static (string Codigo, string Descricao, string Unidade) Normalizar(NovoMaterialDto d) =>
      (Normalizar(d.Codigo), Normalizar(d.Descricao), Normalizar(d.UnidadeMedida));

  /// <summary>
  /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
  /// " CH-001 " colidir com "CH-001" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre o
  /// null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao de
  /// nulabilidade nao e garantia em tempo de execucao.
  /// </summary>
  private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

  /// <summary>
  /// Ausente = almoxarifado (valido, sem Setor). Informado, o Setor tem de existir e estar ativo,
  /// salvo se for o que o Material JA aponta (`jaApontado`, so na edicao): o que ja esta gravado
  /// fica mesmo com o Setor inativado depois, mesmo criterio do Roteiro do no. Setor DIFERENTE do
  /// atual, inexistente ou inativo, e recusado.
  /// </summary>
  private async Task<(bool Falhou, Setor? Setor)> ResolverSetorDeEstoque(
      int? setorId, int? jaApontado, CancellationToken ct)
  {
    if (setorId is null)
      return (false, null);

    var setor = await _setores.ObterPorIdAsync(setorId.Value, ct);
    if (setor is null)
      return (true, null);

    return setor.Ativo || setorId == jaApontado ? (false, setor) : (true, null);
  }

  private static MaterialDto Projetar(Material m, Setor? setor) =>
      new(m.Id, m.Codigo, m.Descricao, m.UnidadeMedida, m.Ativo,
          setor is null ? null : new SetorDeEstoqueDto(setor.Id, setor.Nome));
}
