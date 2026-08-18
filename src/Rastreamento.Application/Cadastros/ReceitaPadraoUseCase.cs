using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Receita padrao de um Componente: filhos, materiais e roteiro.
///
/// Os tres sub-recursos vivem no MESMO caso de uso porque compartilham TRES validacoes
/// (componente pai existe, ids existem, ids estao ativos) e HERDAM do repositorio a mesma
/// propriedade de atomicidade, que nao e validacao — so o ciclo e exclusivo dos filhos. Tres
/// casos de uso duplicariam essas tres ou exigiriam um helper compartilhado que teria a mesma
/// forma deste arquivo, com um nivel a mais de indirecao.
///
/// Toda gravacao SUBSTITUI a receita inteira: "a receita deste componente passa a ser EXATAMENTE
/// estas N linhas". Lista vazia apaga — e o unico caminho de remocao que existe. Quem garante que
/// o apaga-e-grava e atomico e o repositorio (uma transacao explicita), nao este arquivo.
/// </summary>
public sealed class ReceitaPadraoUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";
  private const string ErroDeQuantidadeInvalida = "Quantidade deve ser maior que zero.";

  /// <summary>
  /// A coluna e DECIMAL(18,4) (`specs/02-modelo-de-dados.sql`), entao 4 casas decimais e 14
  /// digitos inteiros. Quantidade fora disso e recusada, nao arredondada — decisao do usuario.
  /// </summary>
  private const string ErroDeQuantidadeForaDaEscala =
      "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.";

  /// <summary>Mensagem do 409 — a gravacao nao aconteceu e refazer o POST e o caminho.</summary>
  private const string ErroDeConflitoDeGravacao =
      "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.";

  /// <summary>Maior valor que cabe em DECIMAL(18,4): 14 digitos inteiros + 4 decimais.</summary>
  private const decimal MaiorQuantidade = 99_999_999_999_999.9999m;

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
    // que recusa nenhuma. `Quantidade_invalida_e_recusada` e as duas de id invalido afirmam
    // `Substituicoes == 0` — mover a escrita para cima mata esses testes.
    //
    // A ORDEM entre as guardas tambem e contrato, e nao estetica: o recurso da ROTA vem primeiro
    // (404 ganha de 400, senao a tela nao sabe se redireciona por peca inexistente ou destaca um
    // campo), e dentro do corpo vem quantidade, depois duplicata, depois existencia/atividade.
    // `Precedencia_das_validacoes_e_fixa` cruza as tres e morre a cada reordenacao.
    if (await _repositorio.ObterComponenteAsync(componenteId, ct) is null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    if (linhas.Any(l => l.QuantidadePadrao <= 0))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeInvalida);

    // `> 0` nao basta: 0,00001 e positivo, cabe no decimal do C# e vira 0,0000 em DECIMAL(18,4) —
    // ou seja, o POST responderia 200 com exatamente a linha de quantidade zero que a guarda
    // acima existe para impedir (medido contra o SQL Server na review da Task 3). E valor grande
    // demais estourava como DbUpdateException, virando 500 em vez de 400. Recusar, nao arredondar.
    if (linhas.Any(l => decimal.Round(l.QuantidadePadrao, 4) != l.QuantidadePadrao
                        || l.QuantidadePadrao > MaiorQuantidade))
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(ErroDeQuantidadeForaDaEscala);

    var ids = linhas.Select(l => l.MaterialId).ToList();
    var repetido = PrimeiroRepetido(ids);
    if (repetido is not null)
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          $"O material {repetido} aparece mais de uma vez na lista.");

    var materiais = await _repositorio.ObterMateriaisPorIdAsync(ids, ct);
    var problema = ConferirExistenciaEAtividade(
        ids, materiais.ToDictionary(m => m.Id, m => m.Ativo), "material", "materiais");
    if (problema is not null) return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(problema);

    // O SERIALIZABLE do repositorio derruba o perdedor de duas gravacoes simultaneas do mesmo
    // componente, e isso e desfecho LEGITIMO do desenho — nao erro do servidor. O repositorio
    // traduz o deadlock/lock timeout do banco para `ConflitoDeConcorrenciaException` (mesmo padrao
    // de `RefreshTokenRepository.SalvarAlteracoesAsync`, para a Application nao referenciar o EF
    // Core), e aqui ele vira `TipoDeErro.Conflito` — 409, nao 500.
    try
    {
      await _repositorio.SubstituirMateriaisAsync(componenteId, linhas.Select(l =>
          new ComponenteMaterialPadrao
          {
            ComponenteId = componenteId,
            MaterialId = l.MaterialId,
            QuantidadePadrao = l.QuantidadePadrao,
          }).ToList(), ct);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      return Result<IReadOnlyList<MaterialPadraoDto>>.Falha(
          ErroDeConflitoDeGravacao, TipoDeErro.Conflito);
    }

    // Re-le em vez de devolver o que entrou: e o unico jeito de a resposta trazer o `Id` da linha
    // (identity do banco) e os dados do Material, que o corpo do POST nao tem.
    return Result<IReadOnlyList<MaterialPadraoDto>>.Ok(await ProjetarMateriais(componenteId, ct));
  }

  private async Task<IReadOnlyList<MaterialPadraoDto>> ProjetarMateriais(
      int componenteId, CancellationToken ct)
  {
    var linhas = await _repositorio.ListarMateriaisAsync(componenteId, ct);

    // Este `Distinct()` e os DOIS de `ConferirExistenciaEAtividade` sao defensivos, nao regra:
    // remove-los nao muda resultado nenhum e nenhum teste morre — tres mutantes EQUIVALENTES,
    // declarados aqui de uma vez (R22, R23 e R24 do relatorio de review da Task 3).
    //
    // Quem garante a ausencia de duplicata na receita GRAVADA e o UNIQUE do banco,
    // `UQ_ComponenteMaterialPadrao (ComponenteId, MaterialId)` — nao a validacao da aplicacao:
    // constraint nao se apaga num refactor, guarda de duplicata sim. E, mesmo com repeticao na
    // entrada, tanto o `WHERE Id IN (...)` real quanto o fake devolvem cada material UMA vez,
    // entao o `ToDictionary` abaixo nao estouraria nem sem o `Distinct()`. No helper e o mesmo:
    // a guarda de duplicata roda antes e nenhum id repetido chega la.
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
  ///
  /// Os dois <c>Distinct()</c> abaixo sao mutantes equivalentes declarados — ver a nota em
  /// <c>ProjetarMateriais</c>, que cobre os tres do arquivo.
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
