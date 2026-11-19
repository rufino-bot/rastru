using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// O cache das idades do Panorama (D7 da spec da Fase 6A, emenda F2 da conferencia): guarda as linhas do livro por no
/// e o maior `Id` lido, e a cada chamada le so as linhas novas, refazendo o pareamento so dos nos que elas tocam. O
/// livro e so de inclusao, e o pareamento e por no, entao o resultado e o mesmo de refazer tudo. Singleton: o
/// repositorio entra por parametro a cada chamada.
/// </summary>
public sealed class CacheDasIdades
{
  /// <summary>
  /// Quantas posicoes de `Id` antes da ultima lida a leitura incremental rele. A escrita e serializavel e o `Id` e
  /// `IDENTITY`: duas escritas simultaneas podem fazer commit fora de ordem, e a linha de `Id` menor aparece depois.
  /// </summary>
  public const int JanelaDeReleitura = 1000;

  /// <summary>De quanto em quanto tempo o cache le tudo de novo: limita a vida de qualquer erro do incremental.</summary>
  public static readonly TimeSpan ReconstrucaoCompleta = TimeSpan.FromMinutes(10);

  private sealed record Retrato(
      int AteId, DateTime FeitoEm, IReadOnlyDictionary<int, NoDaIdade> Nos,
      IReadOnlyDictionary<int, List<Movimentacao>> LinhasPorNo, IdadesDoLivro Idades);

  private readonly SemaphoreSlim _trava = new(1, 1);
  private readonly TimeProvider _relogio;
  private Retrato? _retrato;

  public CacheDasIdades(TimeProvider relogio) => _relogio = relogio;

  public async Task<IdadesDoLivro> ObterAsync(
      IReadOnlyList<NoDaIdade> nos, IExecucaoRepository execucao, CancellationToken ct)
  {
    await _trava.WaitAsync(ct);
    try
    {
      var ateId = await execucao.MaiorIdDoLivroAsync(ct);
      var agora = _relogio.GetUtcNow().UtcDateTime;
      var anterior = _retrato;
      _retrato = anterior is null || agora - anterior.FeitoEm >= ReconstrucaoCompleta
          ? await CompletoAsync(nos, ateId, agora, execucao, ct)
          : await IncrementalAsync(anterior, nos, ateId, execucao, ct);
      return _retrato.Idades;
    }
    finally
    {
      _trava.Release();
    }
  }

  private static async Task<Retrato> CompletoAsync(
      IReadOnlyList<NoDaIdade> nos, int ateId, DateTime agora, IExecucaoRepository execucao, CancellationToken ct)
  {
    var linhas = await execucao.ListarMovimentacoesAteAsync(nos.Select(n => n.Id).ToList(), ateId, ct);
    var porNo = linhas.GroupBy(m => m.EstruturaItemId).ToDictionary(g => g.Key, g => g.ToList());
    return new Retrato(
        ateId, agora, nos.ToDictionary(n => n.Id), porNo,
        CalculoDaIdade.Calcular(nos, linhas));
  }

  private static async Task<Retrato> IncrementalAsync(
      Retrato anterior, IReadOnlyList<NoDaIdade> nos, int ateId, IExecucaoRepository execucao, CancellationToken ct)
  {
    var vivos = nos.ToDictionary(n => n.Id);
    var porNo = anterior.LinhasPorNo.Where(kv => vivos.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
    // So os `Id` das linhas que o retrato ainda guarda: o no que sai perde as linhas, e o `Id` delas tem de sair junto,
    // senao o no que volta as relia e o `Acrescentar` as recusaria como ja lidas.
    var ids = porNo.Values.SelectMany(l => l).Select(m => m.Id).ToHashSet();
    var refeitos = new HashSet<int>();

    // No que entrou: as linhas dele inteiras. No cuja quantidade (ou criacao) mudou: refeito com as linhas que ja tem.
    var novos = nos.Where(n => !anterior.Nos.ContainsKey(n.Id)).Select(n => n.Id).ToList();
    if (novos.Count > 0)
      foreach (var m in await execucao.ListarMovimentacoesAteAsync(novos, ateId, ct))
        Acrescentar(m);
    refeitos.UnionWith(novos);
    refeitos.UnionWith(nos.Where(n => anterior.Nos.TryGetValue(n.Id, out var antes) && antes != n).Select(n => n.Id));

    // As linhas novas, relendo a janela para pegar o commit atrasado; o que ja foi lido sai pelo `Id`.
    var depoisDe = Math.Max(0, anterior.AteId - JanelaDeReleitura);
    foreach (var m in await execucao.ListarMovimentacoesDoIntervaloAsync(depoisDe, ateId, ct))
      if (vivos.ContainsKey(m.EstruturaItemId) && Acrescentar(m))
        refeitos.Add(m.EstruturaItemId);

    var idades = refeitos.Count == 0 && vivos.Count == anterior.Nos.Count
        ? anterior.Idades
        : IdadesDoLivro.Substituir(
            anterior.Idades,
            CalculoDaIdade.Calcular(
                refeitos.Select(id => vivos[id]).ToList(),
                refeitos.SelectMany(id => porNo.GetValueOrDefault(id) ?? []).ToList()),
            refeitos,
            vivos.Keys.ToHashSet());
    return new Retrato(Math.Max(ateId, anterior.AteId), anterior.FeitoEm, vivos, porNo, idades);

    bool Acrescentar(Movimentacao m)
    {
      if (!ids.Add(m.Id)) return false;
      if (!porNo.TryGetValue(m.EstruturaItemId, out var lista)) porNo[m.EstruturaItemId] = lista = [];
      else if (ReferenceEquals(lista, anterior.LinhasPorNo.GetValueOrDefault(m.EstruturaItemId)))
        porNo[m.EstruturaItemId] = lista = [.. lista];   // nao muta a lista do retrato anterior
      lista.Add(m);
      return true;
    }
  }
}
