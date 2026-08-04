using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Setor: criar, editar, listar e (in)ativar. Setor nao se exclui — linhas de
/// EstruturaSetorHistorico apontam para ele (ver a spec da Fase 1, "Politica de exclusao").
/// </summary>
public sealed class CadastroDeSetorUseCase
{
  private const string ErroDeCampoObrigatorio = "Nome e obrigatorio.";

  private const string ErroDeNomeDuplicado = "Ja existe um Setor com este nome.";

  private const string ErroDeSetorNaoEncontrado = "Setor nao encontrado.";

  private readonly ISetorRepository _repositorio;

  public CadastroDeSetorUseCase(ISetorRepository repositorio) => _repositorio = repositorio;

  public async Task<Result<SetorDto>> Cadastrar(NovoSetorDto novo, CancellationToken ct)
  {
    var nome = Normalizar(novo.Nome);
    if (nome.Length == 0)
      return Result<SetorDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

    // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Setor_Nome
    // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
    if (await _repositorio.ObterPorNomeAsync(nome, ct) is not null)
      return Result<SetorDto>.Falha(ErroDeNomeDuplicado, TipoDeErro.Conflito);

    var setor = new Setor { Nome = nome, Ativo = true };
    await _repositorio.AdicionarAsync(setor, ct);
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<SetorDto>.Ok(new SetorDto(setor.Id, setor.Nome, setor.Ativo));
  }

  public async Task<Result<SetorDto>> Editar(int id, NovoSetorDto alterado, CancellationToken ct)
  {
    var nome = Normalizar(alterado.Nome);
    if (nome.Length == 0)
      return Result<SetorDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

    var setor = await _repositorio.ObterPorIdAsync(id, ct);
    if (setor is null)
      return Result<SetorDto>.Falha(ErroDeSetorNaoEncontrado, TipoDeErro.NaoEncontrado);

    // So e conflito se o nome pertencer a OUTRA linha: renomear para o proprio nome e no-op.
    var homonimo = await _repositorio.ObterPorNomeAsync(nome, ct);
    if (homonimo is not null && homonimo.Id != id)
      return Result<SetorDto>.Falha(ErroDeNomeDuplicado, TipoDeErro.Conflito);

    setor.Nome = nome;
    await _repositorio.SalvarAlteracoesAsync(ct);

    return Result<SetorDto>.Ok(new SetorDto(setor.Id, setor.Nome, setor.Ativo));
  }

  public async Task<IReadOnlyList<SetorDto>> Listar(bool incluirInativos, CancellationToken ct)
  {
    var setores = await _repositorio.ListarAsync(incluirInativos, ct);
    return setores.Select(s => new SetorDto(s.Id, s.Nome, s.Ativo)).ToList();
  }

  /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /setores/{id}/ativo`.</summary>
  public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
  {
    var setor = await _repositorio.ObterPorIdAsync(id, ct);
    if (setor is null)
      return Result.Falha(ErroDeSetorNaoEncontrado, TipoDeErro.NaoEncontrado);

    setor.Ativo = ativo;
    await _repositorio.SalvarAlteracoesAsync(ct);
    return Result.Ok();
  }

  /// <summary>
  /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo
  /// da segunda leitura nao entra no caminho feliz.
  /// </summary>
  public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string nome, CancellationToken ct)
  {
    var existente = await _repositorio.ObterPorNomeAsync(Normalizar(nome), ct);
    return existente is null
        ? null
        : new ValorDuplicadoDto("nome", !existente.Ativo, existente.Id);
  }

  /// <summary>
  /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
  /// " Solda " colidir com "Solda" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre o
  /// null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao
  /// de nulabilidade nao e garantia em tempo de execucao. Um unico ponto para as tres chamadas
  /// nao deixarem a defesa divergir entre si.
  /// </summary>
  private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;
}
