using System.Data;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

/// <summary>
/// Livro de movimentacoes e o que as escritas da Fase 3 precisam em volta dele. Ver o XML doc de
/// <see cref="IExecucaoRepository"/> para o contrato; aqui, o que so a implementacao explica.
/// </summary>
public class ExecucaoRepository : IExecucaoRepository
{
  private const string StatusAberto = "Aberto";
  private const string StatusEmProducao = "EmProducao";
  private const string StatusConcluido = "Concluido";
  private const string StatusCancelado = "Cancelado";

  private readonly RastreamentoDbContext _db;

  public ExecucaoRepository(RastreamentoDbContext db) => _db = db;

  /// <summary>
  /// SERIALIZABLE pelo mesmo motivo de `ReceitaPadraoRepository.Substituir`: a validacao le saldo e o
  /// range lock impede que outro escritor insira na faixa lida antes do commit. O desfecho legitimo
  /// de duas escritas no mesmo no (um espera o outro, ou um e derrubado) sobe como
  /// `ConflitoDeConcorrenciaException`, que o caso de uso traduz para 409.
  /// </summary>
  public async Task<T> EmTransacaoAsync<T>(Func<Task<T>> trabalho, CancellationToken ct)
  {
    try
    {
      await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
      var resultado = await trabalho();
      await tx.CommitAsync(ct);
      return resultado;
    }
    catch (Exception e) when (ErrosDoSqlServer.EhConflitoDeConcorrencia(e))
    {
      // O que o trabalho deixou no change tracker nao foi gravado (a transacao voltou); sem limpar,
      // o proximo SaveChanges deste contexto tentaria grava-lo de novo.
      _db.ChangeTracker.Clear();
      throw new ConflitoDeConcorrenciaException(e);
    }
  }

  /// <summary>
  /// Uma linha por comando, em ordem crescente de Id: a ordem fixa de aquisicao e o que torna deadlock
  /// raro em vez de rotineiro (spec secao 8.1). Um `WHERE Id IN (...)` so deixaria a ordem por conta do
  /// plano de execucao. `UPDLOCK` deixa leitores passarem e faz o segundo escritor esperar; e o
  /// `UPDLOCK` combinado com a transacao (nao o `HOLDLOCK` sozinho) que segura a trava ate o commit —
  /// `HOLDLOCK` so evita que o SELECT solte a trava de leitura no fim do proprio comando.
  /// </summary>
  public async Task<IReadOnlyList<EstruturaItem>> TravarNosAsync(IEnumerable<int> ids, CancellationToken ct)
  {
    if (_db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
          "TravarNosAsync so vale dentro de EmTransacaoAsync: fora de transacao a trava acaba no fim do SELECT.");

    var travados = new List<EstruturaItem>();
    foreach (var id in ids.Distinct().Order())
    {
      var linha = await _db.Estruturas
          .FromSql($"SELECT * FROM dbo.EstruturaItem WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE Id = {id}")
          .AsNoTracking()
          .SingleOrDefaultAsync(ct);
      if (linha is not null) travados.Add(linha);
    }
    return travados;
  }

  public async Task<IReadOnlyList<EstruturaItem>> ListarNosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Estruturas.AsNoTracking().Where(e => lista.Contains(e.Id)).OrderBy(e => e.Id).ToListAsync(ct);
  }

  public async Task<IReadOnlyList<EstruturaItem>> ListarFilhosAsync(int paiId, CancellationToken ct) =>
      await _db.Estruturas.AsNoTracking().Where(e => e.EstruturaPaiId == paiId).OrderBy(e => e.Id).ToListAsync(ct);

  public async Task<IReadOnlyList<int>> ListarIdsDaSubarvoreAsync(int id, CancellationToken ct)
  {
    var ids = new List<int>();
    var fronteira = await _db.Estruturas.AsNoTracking().Where(e => e.Id == id).Select(e => e.Id).ToListAsync(ct);
    var visitados = new HashSet<int>();
    while (fronteira.Count > 0)
    {
      // Mesma defesa de `EstruturaRepository.RemoverSubarvoreAsync` contra ciclo nos dados.
      foreach (var visto in fronteira)
        if (!visitados.Add(visto))
          throw new SubarvoreCiclicaException(
              $"A subarvore a partir do no {id} tem um ciclo em EstruturaPaiId: o no {visto} reaparece.");
      ids.AddRange(fronteira);
      var atual = fronteira;
      fronteira = await _db.Estruturas.AsNoTracking()
          .Where(e => e.EstruturaPaiId != null && atual.Contains(e.EstruturaPaiId!.Value))
          .Select(e => e.Id)
          .ToListAsync(ct);
    }
    return ids;
  }

  public async Task<IReadOnlyList<ContextoDoNo>> ListarNosEmProducaoAsync(CancellationToken ct) =>
      await (from e in _db.Estruturas.AsNoTracking()
             join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
             join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
             where p.Status != StatusConcluido && p.Status != StatusCancelado
             orderby e.Id
             select new ContextoDoNo(e, p.Id, p.Numero, a.Id, a.Codigo))
          .ToListAsync(ct);

  public Task<PedidoDoNo?> ObterPedidoDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      (from e in _db.Estruturas.AsNoTracking()
       join a in _db.Agrupamentos.AsNoTracking() on e.AgrupamentoId equals a.Id
       join p in _db.Pedidos.AsNoTracking() on a.PedidoId equals p.Id
       where e.Id == estruturaItemId
       select new PedidoDoNo(p.Id, p.Status))
          .SingleOrDefaultAsync(ct);

  /// <summary>Conjuntista, com a condicao no WHERE: dois inicios paralelos nao disputam uma leitura.</summary>
  public Task MarcarPedidoEmProducaoAsync(int pedidoId, CancellationToken ct) =>
      _db.Pedidos.Where(p => p.Id == pedidoId && p.Status == StatusAberto)
          .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StatusEmProducao), ct);

  /// <summary>
  /// Dois GROUP BY no banco (destinos e origens) e a subtracao em memoria — a soma pesada fica no SQL.
  /// A ordem de saida e a de `Livro.SomarSaldos`, e e por isso que
  /// `Saldos_do_banco_batem_com_a_soma_em_CSharp` compara as duas listas inteiras.
  /// </summary>
  public async Task<IReadOnlyList<SaldoLiquido>> ListarSaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    if (ids.Count == 0) return [];
    var lista = ids.ToList();

    var entradas = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId))
        .GroupBy(m => new { m.EstruturaItemId, m.DestinoPosicao, m.DestinoSetorId, m.DestinoOrdem })
        .Select(g => new
        {
          g.Key.EstruturaItemId, Posicao = g.Key.DestinoPosicao, SetorId = g.Key.DestinoSetorId,
          Ordem = g.Key.DestinoOrdem, Soma = g.Sum(m => m.Quantidade),
        })
        .ToListAsync(ct);
    var saidas = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId))
        .GroupBy(m => new { m.EstruturaItemId, m.OrigemPosicao, m.OrigemSetorId, m.OrigemOrdem })
        .Select(g => new
        {
          g.Key.EstruturaItemId, Posicao = g.Key.OrigemPosicao, SetorId = g.Key.OrigemSetorId,
          Ordem = g.Key.OrigemOrdem, Soma = g.Sum(m => m.Quantidade),
        })
        .ToListAsync(ct);

    var liquido = new Dictionary<(int Item, string Posicao, int? SetorId, int? Ordem), decimal>();
    foreach (var e in entradas)
      Somar(liquido, (e.EstruturaItemId, e.Posicao, e.SetorId, e.Ordem), e.Soma);
    foreach (var s in saidas)
      Somar(liquido, (s.EstruturaItemId, s.Posicao, s.SetorId, s.Ordem), -s.Soma);

    return liquido
        .Where(kv => kv.Value != 0m)
        .OrderBy(kv => kv.Key.Item)
        .ThenBy(kv => Livro.OrdemDaPosicao(kv.Key.Posicao))
        .ThenBy(kv => kv.Key.Ordem ?? 0)
        .ThenBy(kv => kv.Key.SetorId ?? 0)
        .Select(kv => new SaldoLiquido(kv.Key.Item, kv.Key.Posicao, kv.Key.SetorId, kv.Key.Ordem, kv.Value))
        .ToList();
  }

  private static void Somar(
      Dictionary<(int Item, string Posicao, int? SetorId, int? Ordem), decimal> soma,
      (int Item, string Posicao, int? SetorId, int? Ordem) chave, decimal valor) =>
      soma[chave] = soma.GetValueOrDefault(chave) + valor;

  public async Task<IReadOnlyDictionary<int, decimal>> ListarTotaisMontadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Montagens.AsNoTracking()
        .Where(g => lista.Contains(g.EstruturaItemId) && g.EstornadaEm == null)
        .GroupBy(g => g.EstruturaItemId)
        .Select(g => new { Id = g.Key, Total = g.Sum(x => x.Quantidade) })
        .ToDictionaryAsync(x => x.Id, x => x.Total, ct);
  }

  public async Task<IReadOnlyList<(int EstruturaItemId, int Ordem)>> ListarPassosAlcancadosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    var origens = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId) && m.OrigemOrdem != null)
        .Select(m => new { m.EstruturaItemId, Ordem = m.OrigemOrdem!.Value })
        .Distinct().ToListAsync(ct);
    var destinos = await _db.Movimentacoes.AsNoTracking()
        .Where(m => lista.Contains(m.EstruturaItemId) && m.DestinoOrdem != null)
        .Select(m => new { m.EstruturaItemId, Ordem = m.DestinoOrdem!.Value })
        .Distinct().ToListAsync(ct);

    return origens.Concat(destinos)
        .Select(x => (x.EstruturaItemId, x.Ordem))
        .Distinct()
        .OrderBy(x => x.EstruturaItemId).ThenBy(x => x.Ordem)
        .ToList();
  }

  public Task<Movimentacao?> ObterMovimentacaoAsync(int id, CancellationToken ct) =>
      _db.Movimentacoes.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarMovimentacoesDoNoAsync(int estruturaItemId, CancellationToken ct) =>
      await _db.Movimentacoes.AsNoTracking()
          .Where(m => m.EstruturaItemId == estruturaItemId).OrderBy(m => m.Id).ToListAsync(ct);

  public async Task<IReadOnlySet<int>> ListarEstornadasAsync(IReadOnlyCollection<int> movimentacaoIds, CancellationToken ct)
  {
    var lista = movimentacaoIds.ToList();
    var ids = await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.EstornoDeId != null && lista.Contains(m.EstornoDeId!.Value))
        .Select(m => m.EstornoDeId!.Value)
        .ToListAsync(ct);
    return ids.ToHashSet();
  }

  public Task<Montagem?> ObterMontagemAsync(int id, CancellationToken ct) =>
      _db.Montagens.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);

  public async Task<IReadOnlyList<Montagem>> ListarMontagensDoNoAsync(int paiId, CancellationToken ct) =>
      await _db.Montagens.AsNoTracking().Where(g => g.EstruturaItemId == paiId).OrderBy(g => g.Id).ToListAsync(ct);

  public async Task<IReadOnlyList<Movimentacao>> ListarBaixasAsync(IReadOnlyCollection<int> montagemIds, CancellationToken ct)
  {
    var lista = montagemIds.ToList();
    return await _db.Movimentacoes.AsNoTracking()
        .Where(m => m.Tipo == TiposDeMovimentacao.Montagem && m.MontagemId != null && lista.Contains(m.MontagemId!.Value))
        .OrderBy(m => m.Id)
        .ToListAsync(ct);
  }

  /// <summary>
  /// Conjuntista e condicionado a `EstornadaEm IS NULL`: e a unica escrita que uma `Montagem` recebe
  /// depois de nascer, e nunca sobrescreve um estorno anterior.
  /// </summary>
  public Task MarcarMontagemEstornadaAsync(int montagemId, int usuarioId, DateTime em, CancellationToken ct) =>
      _db.Montagens.Where(g => g.Id == montagemId && g.EstornadaEm == null)
          .ExecuteUpdateAsync(s => s
              .SetProperty(g => g.EstornadaEm, (DateTime?)em)
              .SetProperty(g => g.EstornadaPorUsuarioId, (int?)usuarioId), ct);

  public async Task<IReadOnlyDictionary<int, string>> ListarNomesDeUsuariosAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct)
  {
    var lista = ids.ToList();
    return await _db.Usuarios.AsNoTracking()
        .Where(u => lista.Contains(u.Id))
        .ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
  }

  public async Task SubstituirPassosNaoAlcancadosAsync(
      int estruturaItemId, int? ultimaOrdemTravada, IReadOnlyList<(int SetorId, int Ordem)> novos, CancellationToken ct)
  {
    // DELETE antes do INSERT: `UQ_EstruturaRoteiro (EstruturaItemId, Ordem)` recusaria um passo novo
    // com a Ordem de um que ainda nao saiu.
    await _db.EstruturaRoteiros
        .Where(r => r.EstruturaItemId == estruturaItemId && (ultimaOrdemTravada == null || r.Ordem > ultimaOrdemTravada))
        .ExecuteDeleteAsync(ct);
    _db.EstruturaRoteiros.AddRange(novos.Select(p => new EstruturaRoteiro
    {
      EstruturaItemId = estruturaItemId, SetorId = p.SetorId, Ordem = p.Ordem,
    }));
    await _db.SaveChangesAsync(ct);
  }

  public void Adicionar(Movimentacao movimentacao) => _db.Movimentacoes.Add(movimentacao);

  public void Adicionar(Montagem montagem) => _db.Montagens.Add(montagem);

  public Task SalvarAlteracoesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
