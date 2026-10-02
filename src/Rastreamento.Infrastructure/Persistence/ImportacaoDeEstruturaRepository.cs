using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ImportacaoDeEstruturaRepository : IImportacaoDeEstruturaRepository
{
  private readonly RastreamentoDbContext _db;

  public ImportacaoDeEstruturaRepository(RastreamentoDbContext db) => _db = db;

  public Task<ImportacaoDeEstrutura?> ObterAsync(int id, CancellationToken ct) =>
      _db.ImportacoesDeEstrutura
          .Include(i => i.Componentes).ThenInclude(c => c.Filhos)
          .AsSplitQuery()
          .SingleOrDefaultAsync(i => i.Id == id, ct);

  public async Task<IReadOnlyList<ResumoDeImportacao>> ListarDoAgrupamentoAsync(
      int agrupamentoId, CancellationToken ct) =>
      await _db.ImportacoesDeEstrutura.AsNoTracking()
          .Where(i => i.AgrupamentoId == agrupamentoId)
          .Join(_db.Usuarios, i => i.CriadoPorUsuarioId, u => u.Id, (i, u) => new { i, u.NomeCompleto })
          // Id desempata dois rascunhos criados no mesmo instante, para a ordem ser total.
          .OrderByDescending(x => x.i.CriadoEm).ThenByDescending(x => x.i.Id)
          .Select(x => new ResumoDeImportacao(
              x.i.Id, x.i.NomeDoArquivo, x.NomeCompleto, x.i.CriadoEm, x.i.AtualizadoEm))
          .ToListAsync(ct);

  /// <summary>
  /// Dois <c>SaveChanges</c> numa transacao, porque a FK <c>RaizId</c> e circular com os registros: o
  /// Id da raiz so existe depois do primeiro (decisao P6 do plano do import). O primeiro grava
  /// cabecalho, registros e filhos -- o EF ordena os registros antes dos filhos pelas navegacoes
  /// <c>Filhos</c> e <c>Filho</c> --, e o segundo apontar a raiz.
  /// </summary>
  public async Task AdicionarAsync(ImportacaoDeEstrutura importacao, CancellationToken ct)
  {
    if (importacao.Raiz is not null && !importacao.Componentes.Contains(importacao.Raiz))
      throw new ArgumentException("A raiz tem que ser um dos registros do rascunho.", nameof(importacao));

    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    _db.ImportacoesDeEstrutura.Add(importacao);
    await _db.SaveChangesAsync(ct);

    if (importacao.Raiz is not null)
    {
      importacao.RaizId = importacao.Raiz.Id;
      await _db.SaveChangesAsync(ct);
    }

    await transacao.CommitAsync(ct);
  }

  /// <summary>
  /// Pressupoe a entidade rastreada por este contexto (vinda de <see cref="ObterAsync"/>). So o
  /// cabecalho tem token, e e ele que muda a cada escrita porque <c>AtualizadoEm</c> e gravado aqui
  /// -- sem isso, editar so um filho deixaria a versao do rascunho igual.
  /// </summary>
  public async Task SalvarAsync(ImportacaoDeEstrutura importacao, byte[] versaoEsperada, CancellationToken ct)
  {
    importacao.AtualizadoEm = DateTime.UtcNow;
    _db.Entry(importacao).Property(i => i.Versao).OriginalValue = versaoEsperada;
    try
    {
      await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException e)
    {
      throw new ConflitoDeConcorrenciaException(e);
    }
  }

  public void RemoverRegistros(IEnumerable<ImportacaoDeEstruturaComponente> registros) =>
      _db.ImportacoesDeEstruturaComponentes.RemoveRange(registros);

  public void RemoverFilhos(IEnumerable<ImportacaoDeEstruturaFilho> filhos) =>
      _db.ImportacoesDeEstruturaFilhos.RemoveRange(filhos);

  public async Task ExcluirAsync(int id, CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    var arquivoIds = await _db.ImportacoesDeEstruturaComponentes.AsNoTracking()
        .Where(c => c.ImportacaoId == id && c.ArquivoSolidoPendenteId != null)
        .Select(c => c.ArquivoSolidoPendenteId!.Value)
        .ToListAsync(ct);

    // A ordem e a de decisao P6 do plano do import: o schema nao tem ON DELETE CASCADE.
    await _db.ImportacoesDeEstruturaFilhos
        .Where(f => _db.ImportacoesDeEstruturaComponentes.Any(c => c.Id == f.PaiId && c.ImportacaoId == id))
        .ExecuteDeleteAsync(ct);
    await _db.ImportacoesDeEstrutura.Where(i => i.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(i => i.RaizId, (int?)null), ct);
    await _db.ImportacoesDeEstruturaComponentes.Where(c => c.ImportacaoId == id).ExecuteDeleteAsync(ct);
    await _db.ImportacoesDeEstrutura.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
    if (arquivoIds.Count > 0)
      await _db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ExecuteDeleteAsync(ct);

    await transacao.CommitAsync(ct);
  }

  public Task<bool> ExisteNoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      _db.ImportacoesDeEstrutura.AsNoTracking().AnyAsync(i => i.AgrupamentoId == agrupamentoId, ct);

  public Task<string> ObterNomeDoAutorAsync(int usuarioId, CancellationToken ct) =>
      _db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.NomeCompleto).SingleAsync(ct);

  public async Task<int> GravarArquivoPendenteAsync(ArquivoDeComponente arquivo, CancellationToken ct)
  {
    _db.ArquivosDeComponente.Add(arquivo);
    await _db.SaveChangesAsync(ct);
    return arquivo.Id;
  }

  public Task<ArquivoDeComponente?> ObterArquivoAsync(int arquivoId, CancellationToken ct) =>
      _db.ArquivosDeComponente.AsNoTracking().SingleOrDefaultAsync(a => a.Id == arquivoId, ct);

  // Projecao explicita para nunca pedir a coluna Conteudo (VARBINARY(MAX)) ao SQL Server: mesmo
  // criterio de ArquivoDeComponenteRepository.ObterMetadadoDoSolidoAsync.
  public async Task<IReadOnlyDictionary<int, MetadadoDeSolido>> ObterMetadadosAsync(
      IReadOnlyCollection<int> arquivoIds, CancellationToken ct)
  {
    if (arquivoIds.Count == 0) return new Dictionary<int, MetadadoDeSolido>();
    return await _db.ArquivosDeComponente.AsNoTracking()
        .Where(a => arquivoIds.Contains(a.Id))
        .Select(a => new { a.Id, Metadado = new MetadadoDeSolido(a.NomeOriginal, a.TamanhoEmBytes) })
        .ToDictionaryAsync(x => x.Id, x => x.Metadado, ct);
  }

  public async Task ExcluirArquivosAsync(IReadOnlyCollection<int> arquivoIds, CancellationToken ct)
  {
    if (arquivoIds.Count == 0) return;
    await _db.ArquivosDeComponente.Where(a => arquivoIds.Contains(a.Id)).ExecuteDeleteAsync(ct);
  }
}
