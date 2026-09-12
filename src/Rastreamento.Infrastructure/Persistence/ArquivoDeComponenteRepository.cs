using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ArquivoDeComponenteRepository : IArquivoDeComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ArquivoDeComponenteRepository(RastreamentoDbContext db) => _db = db;

  /// <summary>
  /// Sem isolamento SERIALIZABLE, diferente do molde de <c>ReceitaPadraoRepository.Substituir</c>
  /// (o XML doc da interface citava "mesmo molde" ate a review da Task 2 apontar que isso nao e
  /// verdade -- corrigido aqui, na frase, nao no codigo): READ COMMITTED basta porque cada
  /// escritor insere a PROPRIA linha em <c>ArquivoDeComponente</c> (sem disputa possivel) e depois
  /// faz um UPDATE na mesma linha de <c>Componente</c> por Id, que o lock exclusivo ja serializa.
  /// Duas substituicoes simultaneas do mesmo Componente terminam em ultimo-escritor-vence -- um
  /// arquivo grava sem ninguem apontar para ele -- que e exatamente a consequencia aceita em §2.5
  /// da spec, nao uma corrida a fechar.
  /// </summary>
  public async Task<int?> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    // A checagem vem ANTES do primeiro SaveChanges de proposito (Important 3 da review da Task
    // 2): antes, SingleAsync lancava InvalidOperationException para componente inexistente, que
    // subia crua ate a API e virava 500 -- a spec (§5) exige 404. Fazendo a checagem aqui, com
    // SingleOrDefaultAsync, o metodo devolve null SEM gravar nada: nao ha arquivo orfao para a
    // transacao desfazer no rollback.
    var componente = await _db.Componentes.SingleOrDefaultAsync(c => c.Id == componenteId, ct);
    if (componente is null) return null;

    _db.ArquivosDeComponente.Add(arquivo);
    await _db.SaveChangesAsync(ct);

    componente.ArquivoSolidoId = arquivo.Id;
    await _db.SaveChangesAsync(ct);

    await transacao.CommitAsync(ct);
    return arquivo.Id;
  }

  public async Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct)
  {
    var arquivoId = await ObterArquivoIdAsync(componenteId, ct);
    // Colapsa "componente inexistente" e "componente sem solido" no mesmo null -- declarado no
    // XML doc da interface, e aqui esta o mecanismo: default(int?) devolvido por
    // SingleOrDefaultAsync (sem linha) coincide com o ArquivoSolidoId NULL de uma linha que
    // existe. Os dois casos produzem o mesmo `arquivoId is null` (Minor 4 da review da Task 2).
    if (arquivoId is null) return null;

    return await _db.ArquivosDeComponente.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == arquivoId.Value, ct);
  }

  public async Task<MetadadoDeSolido?> ObterMetadadoDoSolidoAsync(
      int componenteId, CancellationToken ct)
  {
    var arquivoId = await ObterArquivoIdAsync(componenteId, ct);
    // Mesmo colapso de ObterSolidoDoComponenteAsync -- ver o comentario la.
    if (arquivoId is null) return null;

    // Projecao explicita para o record, NUNCA materializando ArquivoDeComponente inteiro: e o que
    // impede o Conteudo (VARBINARY(MAX)) de sair do SQL Server neste caminho. A garantia e de
    // TIPO (MetadadoDeSolido nao tem propriedade Conteudo), reforcada aqui por nao dar ao
    // provedor de consulta chance nenhuma de pedir a coluna.
    return await _db.ArquivosDeComponente.AsNoTracking()
        .Where(a => a.Id == arquivoId.Value)
        .Select(a => new MetadadoDeSolido(a.NomeOriginal, a.TamanhoEmBytes))
        .SingleOrDefaultAsync(ct);
  }

  // Duas consultas em vez de um JOIN com navegacao: a navegacao e justamente o que nao existe,
  // por desenho. Este SELECT le SO o id -- nao toca no blob nem no nome -- e serve os dois
  // metodos publicos acima (o que traz o blob e o que traz so o metadado).
  private async Task<int?> ObterArquivoIdAsync(int componenteId, CancellationToken ct) =>
      await _db.Componentes.AsNoTracking()
          .Where(c => c.Id == componenteId)
          .Select(c => c.ArquivoSolidoId)
          .SingleOrDefaultAsync(ct);
}
