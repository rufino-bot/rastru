using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>Um no como o calculo da idade o ve: `CriadoEm` e o `Agrupamento.CriadoEm` do no (C4 da spec da Fase 6A).</summary>
public sealed record NoDaIdade(int Id, decimal Quantidade, DateTime CriadoEm);

/// <summary>Uma parte viva do saldo de um (no, lugar): `Quantidade` que chegou em `Chegada` e ainda esta la.</summary>
public sealed record ParteViva(int EstruturaItemId, Local Local, decimal Quantidade, DateTime Chegada);

/// <summary>Uma parte que saiu de `NoSetor` de um passo pelo `Termino`: ficou `Duracao` no passo (secao 3.2 da spec).</summary>
public sealed record Permanencia(int EstruturaItemId, int SetorId, int Ordem, decimal Quantidade, TimeSpan Duracao);

/// <summary>O resultado do pareamento: as partes vivas por no e as permanencias por no e passo.</summary>
public sealed class IdadesDoLivro
{
  private static readonly IReadOnlyList<ParteViva> SemPartes = [];

  private readonly IReadOnlyDictionary<int, IReadOnlyList<ParteViva>> _partes;
  private readonly IReadOnlyDictionary<(int No, int Ordem), IReadOnlyList<Permanencia>> _permanencias;

  internal IdadesDoLivro(
      IReadOnlyDictionary<int, IReadOnlyList<ParteViva>> partes,
      IReadOnlyDictionary<(int No, int Ordem), IReadOnlyList<Permanencia>> permanencias)
  {
    _partes = partes;
    _permanencias = permanencias;
  }

  /// <summary>
  /// As idades de `anteriores` para os nos vivos que nao foram refeitos, mais todas as de `refeitas`: o cache
  /// incremental refaz so os nos tocados, e o pareamento e por no (cada linha e cada estorno ficam no proprio no).
  /// </summary>
  internal static IdadesDoLivro Substituir(
      IdadesDoLivro anteriores, IdadesDoLivro refeitas, IReadOnlySet<int> nosRefeitos, IReadOnlySet<int> nosVivos)
  {
    bool Mantem(int no) => nosVivos.Contains(no) && !nosRefeitos.Contains(no);
    var partes = anteriores._partes.Where(kv => Mantem(kv.Key))
        .Concat(refeitas._partes)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    var permanencias = anteriores._permanencias.Where(kv => Mantem(kv.Key.No))
        .Concat(refeitas._permanencias)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    return new IdadesDoLivro(partes, permanencias);
  }

  /// <summary>As partes vivas do no, em ordem de chegada.</summary>
  public IReadOnlyList<ParteViva> Partes(int noId) => _partes.TryGetValue(noId, out var p) ? p : SemPartes;

  /// <summary>A menor chegada entre as partes do no cujo lugar satisfaz `onde`; nulo sem nenhuma.</summary>
  public DateTime? MaisAntiga(int noId, Func<Local, bool> onde)
  {
    DateTime? menor = null;
    foreach (var p in Partes(noId))
      if (onde(p.Local) && (menor is null || p.Chegada < menor)) menor = p.Chegada;
    return menor;
  }

  /// <summary>A media, ponderada pela quantidade, das permanencias do passo; nulo sem nenhuma.</summary>
  public TimeSpan? PermanenciaMedia(int noId, int ordem)
  {
    if (!_permanencias.TryGetValue((noId, ordem), out var lista) || lista.Count == 0) return null;
    var total = lista.Sum(x => x.Quantidade);
    if (total <= 0m) return null;
    var ticks = lista.Sum(x => (decimal)x.Duracao.Ticks * x.Quantidade) / total;
    return TimeSpan.FromTicks((long)Math.Round(ticks));
  }
}

/// <summary>
/// A idade das partes do livro (spec da Fase 6A, secao 3): pareia, por (no, lugar), as saidas com as chegadas mais
/// antigas (a primeira parte que chegou e a primeira que sai). Funcao pura: nao le banco.
/// </summary>
public static class CalculoDaIdade
{
  private sealed class Chegada(DateTime data, decimal quantidade)
  {
    public DateTime Data { get; } = data;
    public decimal Quantidade { get; set; } = quantidade;
  }

  public static IdadesDoLivro Calcular(IReadOnlyCollection<NoDaIdade> nos, IReadOnlyList<Movimentacao> linhas)
  {
    // C3: a linha estornada e o estorno dela saem os dois; o resto e o livro liquido.
    var estornadas = linhas.Where(m => m.EstornoDeId is not null).Select(m => m.EstornoDeId!.Value).ToHashSet();
    var liquidas = linhas
        .Where(m => m.Tipo != TiposDeMovimentacao.Estorno && !estornadas.Contains(m.Id))
        .OrderBy(m => m.DataHora).ThenBy(m => m.Id)
        .ToList();

    var vivas = new Dictionary<(int No, Local Local), List<Chegada>>();
    var permanencias = new Dictionary<(int No, int Ordem), List<Permanencia>>();

    // C4: "a iniciar" nasce na data de criacao do Agrupamento.
    foreach (var no in nos)
      if (no.Quantidade > 0m)
        vivas[(no.Id, Local.AIniciar)] = [new Chegada(no.CriadoEm, no.Quantidade)];

    foreach (var m in liquidas)
    {
      var origem = Local.DaOrigem(m);
      var destino = Local.DoDestino(m);

      // A saida consome as chegadas mais antigas, dividindo a ultima; maior que o vivo consome o que houver.
      if (!Terminal(origem) && vivas.TryGetValue((m.EstruturaItemId, origem), out var fila))
      {
        var falta = m.Quantidade;
        while (falta > 0m && fila.Count > 0)
        {
          var primeira = fila[0];
          var tirado = Math.Min(primeira.Quantidade, falta);
          if (m.Tipo == TiposDeMovimentacao.Termino && origem.Posicao == Posicoes.NoSetor
              && origem.SetorId is int setor && origem.Ordem is int ordem)
          {
            var chave = (m.EstruturaItemId, ordem);
            if (!permanencias.TryGetValue(chave, out var doPasso)) permanencias[chave] = doPasso = [];
            doPasso.Add(new Permanencia(m.EstruturaItemId, setor, ordem, tirado, m.DataHora - primeira.Data));
          }

          falta -= tirado;
          if (tirado == primeira.Quantidade) fila.RemoveAt(0);
          else primeira.Quantidade -= tirado;
        }
      }

      if (Terminal(destino)) continue;
      if (!vivas.TryGetValue((m.EstruturaItemId, destino), out var destinoFila))
        vivas[(m.EstruturaItemId, destino)] = destinoFila = [];
      destinoFila.Add(new Chegada(m.DataHora, m.Quantidade));
    }

    var partes = vivas
        .Where(kv => kv.Value.Count > 0)
        .SelectMany(kv => kv.Value.Select(c => new ParteViva(kv.Key.No, kv.Key.Local, c.Quantidade, c.Data)))
        .GroupBy(p => p.EstruturaItemId)
        .ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<ParteViva>)g.OrderBy(p => p.Chegada)
                .ThenBy(p => Livro.OrdemDaPosicao(p.Local.Posicao))
                .ThenBy(p => p.Local.Ordem ?? 0)
                .ThenBy(p => p.Local.SetorId ?? 0)
                .ToList());

    return new IdadesDoLivro(
        partes,
        permanencias.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Permanencia>)kv.Value));
  }

  /// <summary>C5: `Montado`, `Expedido` e `Perdido` nao guardam partes.</summary>
  private static bool Terminal(Local l) =>
      l.Posicao is Posicoes.Montado or Posicoes.Expedido or Posicoes.Perdido;
}
