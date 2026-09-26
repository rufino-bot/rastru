using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Movimentacao e Montagem para DTO, com nome de Setor e de autor resolvidos em lote e a marca de
/// estornada lida do livro — o mesmo formato em toda resposta que devolve um registro.
/// </summary>
internal sealed class ProjetorDoLivro
{
  private readonly IExecucaoRepository _execucao;
  private readonly IReceitaPadraoRepository _catalogo;

  public ProjetorDoLivro(IExecucaoRepository execucao, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _catalogo = catalogo;
  }

  public async Task<IReadOnlyList<MovimentacaoDto>> ProjetarMovimentacoesAsync(
      IReadOnlyList<Movimentacao> movimentos, CancellationToken ct)
  {
    if (movimentos.Count == 0) return [];

    var setorIds = movimentos.SelectMany(m => new[] { m.OrigemSetorId, m.DestinoSetorId }).OfType<int>().Distinct().ToList();
    var setores = await NomesDosSetoresAsync(setorIds, ct);
    var usuarios = await _execucao.ListarNomesDeUsuariosAsync(movimentos.Select(m => m.UsuarioId).Distinct().ToList(), ct);
    var estornadas = await _execucao.ListarEstornadasAsync(movimentos.Select(m => m.Id).ToList(), ct);

    return movimentos.Select(m => new MovimentacaoDto(
        m.Id, m.EstruturaItemId, m.Tipo, m.Quantidade,
        LocalDto.De(Local.DaOrigem(m), setores), LocalDto.De(Local.DoDestino(m), setores),
        m.MontagemId, m.EstornoDeId, m.DataHora, m.UsuarioId, usuarios.GetValueOrDefault(m.UsuarioId, string.Empty),
        estornadas.Contains(m.Id))).ToList();
  }

  public async Task<IReadOnlyList<MontagemDto>> ProjetarMontagensAsync(IReadOnlyList<Montagem> montagens, CancellationToken ct)
  {
    if (montagens.Count == 0) return [];

    var baixas = await _execucao.ListarBaixasAsync(montagens.Select(g => g.Id).ToList(), ct);
    var baixasPorMontagem = (await ProjetarMovimentacoesAsync(baixas, ct)).ToLookup(b => b.MontagemId);
    var setores = await NomesDosSetoresAsync(montagens.Select(g => g.SetorId).Distinct().ToList(), ct);
    var usuarios = await _execucao.ListarNomesDeUsuariosAsync(montagens.Select(g => g.UsuarioId).Distinct().ToList(), ct);

    return montagens.Select(g => new MontagemDto(
        g.Id, g.EstruturaItemId, g.SetorId, setores.GetValueOrDefault(g.SetorId, string.Empty), g.Quantidade,
        g.DataHora, g.UsuarioId, usuarios.GetValueOrDefault(g.UsuarioId, string.Empty), g.EstornadaEm is not null,
        baixasPorMontagem[g.Id].ToList())).ToList();
  }

  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
      ids.Count == 0
          ? new Dictionary<int, string>()
          : (await _catalogo.ObterSetoresPorIdAsync(ids, ct)).ToDictionary(s => s.Id, s => s.Nome);
}
