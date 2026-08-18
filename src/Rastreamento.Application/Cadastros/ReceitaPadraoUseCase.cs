using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Os tres sub-recursos vivem no MESMO caso de uso porque compartilham quatro das cinco
/// validacoes (componente pai existe, ids existem, ids estao ativos, substituicao e atomica) —
/// so o ciclo e exclusivo dos filhos. Tres casos de uso duplicariam essas quatro ou exigiriam um
/// helper compartilhado que teria a mesma forma deste arquivo, com um nivel a mais de indirecao.
///
/// Toda gravacao SUBSTITUI a receita inteira: "a receita deste componente passa a ser EXATAMENTE
/// estas N linhas". Lista vazia apaga — e o unico caminho de remocao que existe. Quem garante que
/// o apaga-e-grava e atomico e o repositorio (uma transacao explicita), nao este arquivo.
/// </summary>
public sealed class ReceitaPadraoUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  private readonly IReceitaPadraoRepository _repositorio;

  public ReceitaPadraoUseCase(IReceitaPadraoRepository repositorio) => _repositorio = repositorio;

  // ------------------------------------------------------------------ materiais

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> ListarMateriais(
      int componenteId, CancellationToken ct)
  {
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  public async Task<Result<IReadOnlyList<MaterialPadraoDto>>> SubstituirMateriais(
      int componenteId, IReadOnlyList<LinhaDeMaterialPadraoDto> linhas, CancellationToken ct)
  {
    // Toda validacao acontece ANTES da unica chamada de escrita: recusa que grava metade e pior
    // que recusa nenhuma. `Quantidade_nao_positiva_e_recusada` e as duas de id invalido afirmam
    // `Substituicoes == 0` — mover a escrita para cima mata esses testes.
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    var ids = linhas.Select(l => l.MaterialId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          $"O material {repetido} aparece mais de uma vez na lista.");

    var materiais = await _repositorio.ObterMateriaisPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, materiais.ToDictionary(m => m.Id, m => m.Ativo), "material", "materiais");
    if (problema is not null) return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(problema);

    await _repositorio.SubstituirMateriaisAsync(componenteId, linhas.Select(l =>
        new ComponenteMaterialPadrao
        {
          ComponenteId = componenteId,
          MaterialId = l.MaterialId,
          QuantidadePadrao = l.QuantidadePadrao,
        }).ToList(), ct);

    // Re-le em vez de devolver o que entrou: e o unico jeito de a resposta trazer o `Id` da linha
    // (identity do banco) e os dados do Material, que o corpo do POST nao tem.
    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  private async Task<IReadOnlyList<MaterialPadraoDto>> ProjetarMateriais(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarMateriaisAsync(componenteId, ct);

    // O `Distinct()` e defensivo, nao regra: receita gravada nao tem MaterialId repetido (a
    // validacao de duplicata barra antes de escrever), entao remove-lo nao muda resultado nenhum
    // e nenhum teste morre — mutante equivalente, registrado no relatorio da Task 3.
    var materiais = (await _repositorio.ObterMateriaisPorIdAsync(
        linhas.Select(l => l.MaterialId).Distinct().ToList(), ct)).ToDictionary(m => m.Id);

    // Sem filtro por Ativo: linha ja gravada SOBREVIVE a inativacao do material. Inativar catalogo
    // nao pode corromper receita que ja existe.
    return linhas.Select(l =>
    {
      var m = materiais[l.MaterialId];
      return new MaterialPadraoDto(
          l.Id, l.MaterialId, m.Codigo, m.Descricao, m.UnidadeMedida, l.QuantidadePadrao);
    }).ToList();
  }

  // ------------------------------------------------------------------ comum

  /// <summary>O primeiro id que aparece duas vezes, ou null. Ordem estavel: a lista dita.</summary>
  private static int? PrimeiroRepetido(IReadOnlyList<int> ids)
  {
    var vistos = new HashSet<int>();
    foreach (var id in ids)
      if (!vistos.Add(id)) return id;
    return null;
  }

  /// <summary>
  /// Uma mensagem para "nao existe" e outra para "esta inativo", NOMEANDO os ids — sem o id, o
  /// usuario com 12 linhas na tela nao sabe qual corrigir. Os quatro ramos (singular e plural de
  /// cada) tem teste proprio em `ReceitaPadraoUseCaseTests`, porque este helper e compartilhado
  /// pelos tres sub-recursos: um ramo quebrado quebraria os tres de uma vez.
  ///
  /// Falha de VALIDACAO, nao 404: o recurso da rota (o Componente) existe — quem esta errado e uma
  /// linha do corpo.
  /// </summary>
  private static string? ConferirExistenciaEAtividade(
      IReadOnlyList<int> idsPedidos,
      IReadOnlyDictionary<int, bool> ativoPorId,
      string singular,
      string plural)
  {
    var ausentes = idsPedidos.Where(id => !ativoPorId.ContainsKey(id)).Distinct().ToList();
    if (ausentes.Count > 0)
      return ausentes.Count == 1
          ? $"O {singular} {ausentes[0]} nao existe."
          : $"Os {plural} {string.Join(", ", ausentes)} nao existem.";

    var inativos = idsPedidos.Where(id => !ativoPorId[id]).Distinct().ToList();
    if (inativos.Count > 0)
      return inativos.Count == 1
          ? $"O {singular} {inativos[0]} esta inativo e nao pode entrar na receita."
          : $"Os {plural} {string.Join(", ", inativos)} estao inativos e nao podem entrar na receita.";

    return null;
  }
}
