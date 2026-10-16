using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// As leituras da separacao de material (spec da Fase 4, secao 5.2): o historico do no e, com a arvore, o
/// catalogo e o relogio, a lista do Separador.
/// </summary>
public sealed class ConsultaDeSeparacaoUseCase
{
  private readonly ISeparacaoRepository _separacao;
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly TimeProvider _relogio;

  public ConsultaDeSeparacaoUseCase(
      ISeparacaoRepository separacao, IExecucaoRepository execucao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, TimeProvider relogio)
  {
    _separacao = separacao;
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
    _relogio = relogio;
  }

  /// <summary>
  /// O livro do no, mais recente primeiro (`DataHora`, e o `Id` desempatando). `Estornavel` e calculado
  /// para `quem`, como os `estornaveis` da Fila: uma `Separacao` ainda sem estorno, do proprio leitor, ou de
  /// qualquer autor quando ele ve os registros de todos (PCP e Administrador).
  /// </summary>
  public async Task<Result<IReadOnlyList<SeparacaoDto>>> Historico(int noId, QuemLe quem, CancellationToken ct)
  {
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<IReadOnlyList<SeparacaoDto>>();

    var linhas = await _separacao.ListarLinhasAsync([noId], ct);
    var estornadas = linhas.Where(l => l.EstornoDeId is not null).Select(l => l.EstornoDeId!.Value).ToHashSet();
    var materiais = (await _separacao.ListarMateriaisAsync(linhas.Select(l => l.MaterialId).Distinct().ToList(), ct))
        .ToDictionary(m => m.Id);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(linhas.Select(l => l.UsuarioId).Distinct().ToList(), ct);

    bool Estornavel(MaterialSeparacao l) =>
        l.Tipo == TiposDeSeparacao.Separacao && !estornadas.Contains(l.Id)
        && (quem.VeRegistrosDeTodos || l.UsuarioId == quem.UsuarioId);

    IReadOnlyList<SeparacaoDto> historico = linhas
        .OrderByDescending(l => l.DataHora).ThenByDescending(l => l.Id)
        .Select(l => ProjecaoDaSeparacao.Linha(
            l, materiais[l.MaterialId], autores.GetValueOrDefault(l.UsuarioId, string.Empty), Estornavel(l)))
        .ToList();
    return Result<IReadOnlyList<SeparacaoDto>>.Ok(historico);
  }
}
