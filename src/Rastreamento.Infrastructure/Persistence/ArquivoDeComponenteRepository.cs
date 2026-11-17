using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ArquivoDeComponenteRepository : IArquivoDeComponenteRepository
{
  private readonly RastreamentoDbContext _db;

  public ArquivoDeComponenteRepository(RastreamentoDbContext db) => _db = db;

  /// <summary>
  /// Sem isolamento SERIALIZABLE, diferente do molde de <c>ReceitaPadraoRepository.Substituir</c>:
  /// READ COMMITTED basta porque cada escritor insere a PROPRIA linha em
  /// <c>ArquivoDeComponente</c> (sem disputa possivel) e depois faz um UPDATE na mesma linha de
  /// <c>Componente</c> por Id, que o lock exclusivo ja serializa. Duas substituicoes simultaneas do
  /// mesmo Componente terminam em ultimo-escritor-vence -- o arquivo de quem perdeu fica gravado
  /// sem ninguem apontar para ele, o mesmo estado em que toda substituicao ja deixa o arquivo
  /// anterior, por desenho (ver o XML doc da interface) -- e por isso nao e uma corrida a fechar.
  ///
  /// <para>
  /// A miniatura entra no MESMO par de <c>SaveChanges</c>: os dois arquivos sao inseridos no
  /// primeiro, e as duas FKs do Componente (<c>ArquivoSolidoId</c> e <c>ArquivoMiniaturaId</c>)
  /// saem no mesmo UPDATE do segundo. Ultimo-escritor-vence continua valendo para o PAR: o UPDATE
  /// e um so, entao nao existe o estado "solido de um envio com a miniatura de outro".
  /// </para>
  /// </summary>
  public async Task<int?> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente solido, ArquivoDeComponente? miniatura,
      CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    // A checagem vem ANTES do primeiro SaveChanges de proposito: com SingleAsync no lugar,
    // componente inexistente lancaria InvalidOperationException, que subiria crua ate a API e
    // viraria 500 -- a spec (§5) exige 404. Fazendo a checagem aqui, com SingleOrDefaultAsync, o
    // metodo devolve null SEM gravar nada: nao ha arquivo orfao para a transacao desfazer no
    // rollback.
    var componente = await _db.Componentes.SingleOrDefaultAsync(c => c.Id == componenteId, ct);
    if (componente is null) return null;

    _db.ArquivosDeComponente.Add(solido);
    if (miniatura is not null) _db.ArquivosDeComponente.Add(miniatura);
    await _db.SaveChangesAsync(ct);

    componente.ArquivoSolidoId = solido.Id;
    // Sem miniatura a FK vai a null (C6): a anterior era do solido que acabou de ser trocado.
    componente.ArquivoMiniaturaId = miniatura?.Id;
    await _db.SaveChangesAsync(ct);

    await transacao.CommitAsync(ct);
    return solido.Id;
  }

  /// <summary>
  /// A leitura do Componente e do <c>Sha256</c> do solido e a gravacao vao na MESMA transacao (C15).
  /// READ COMMITTED, como <see cref="GravarEVincularComoSolidoAsync"/>: o que se fecha aqui e a
  /// comparacao contra o solido que ja esta gravado, nao uma disputa por linha; um solido trocado
  /// por outro escritor DEPOIS da leitura e o mesmo ultimo-escritor-vence daquele metodo.
  /// </summary>
  public async Task<ResultadoDaMiniaturaAvulsa> GravarMiniaturaAvulsaAsync(
      int componenteId, ArquivoDeComponente miniatura, byte[] sha256DoSolido, CancellationToken ct)
  {
    await using var transacao = await _db.Database.BeginTransactionAsync(ct);

    var componente = await _db.Componentes.SingleOrDefaultAsync(c => c.Id == componenteId, ct);
    if (componente is null) return ResultadoDaMiniaturaAvulsa.ComponenteInexistente;
    if (componente.ArquivoSolidoId is not { } solidoId) return ResultadoDaMiniaturaAvulsa.SemSolido;

    // So o Sha256 (coluna calculada), nunca o blob.
    var atual = await _db.ArquivosDeComponente.AsNoTracking()
        .Where(a => a.Id == solidoId).Select(a => a.Sha256).SingleAsync(ct);
    if (!atual.AsSpan().SequenceEqual(sha256DoSolido)) return ResultadoDaMiniaturaAvulsa.SolidoMudou;

    _db.ArquivosDeComponente.Add(miniatura);
    await _db.SaveChangesAsync(ct);

    componente.ArquivoMiniaturaId = miniatura.Id;
    await _db.SaveChangesAsync(ct);

    await transacao.CommitAsync(ct);
    return ResultadoDaMiniaturaAvulsa.Gravada;
  }

  public async Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct)
  {
    var arquivoId = await ObterArquivoSolidoIdAsync(componenteId, ct);
    // Colapsa "componente inexistente" e "componente sem solido" no mesmo null -- declarado no
    // XML doc da interface, e aqui esta o mecanismo: default(int?) devolvido por
    // SingleOrDefaultAsync (sem linha) coincide com o ArquivoSolidoId NULL de uma linha que
    // existe. Os dois casos produzem o mesmo `arquivoId is null`.
    if (arquivoId is null) return null;

    return await _db.ArquivosDeComponente.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == arquivoId.Value, ct);
  }

  public async Task<MetadadoDeSolido?> ObterMetadadoDoSolidoAsync(
      int componenteId, CancellationToken ct)
  {
    var arquivoId = await ObterArquivoSolidoIdAsync(componenteId, ct);
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

  public async Task<ArquivoDeComponente?> ObterMiniaturaDoComponenteAsync(
      int componenteId, CancellationToken ct)
  {
    // Le SO o id da miniatura, como ObterArquivoSolidoIdAsync le o do solido, e com o mesmo
    // colapso de ObterSolidoDoComponenteAsync: componente inexistente e componente sem miniatura
    // dao o mesmo null.
    var arquivoId = await _db.Componentes.AsNoTracking()
        .Where(c => c.Id == componenteId)
        .Select(c => c.ArquivoMiniaturaId)
        .SingleOrDefaultAsync(ct);
    if (arquivoId is null) return null;

    return await _db.ArquivosDeComponente.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == arquivoId.Value, ct);
  }

  // Duas consultas em vez de um JOIN com navegacao: a navegacao e justamente o que nao existe,
  // por desenho. Este SELECT le SO o id -- nao toca no blob nem no nome -- e serve
  // ObterSolidoDoComponenteAsync (que traz o blob) e ObterMetadadoDoSolidoAsync (que traz so o
  // metadado).
  private async Task<int?> ObterArquivoSolidoIdAsync(int componenteId, CancellationToken ct) =>
      await _db.Componentes.AsNoTracking()
          .Where(c => c.Id == componenteId)
          .Select(c => c.ArquivoSolidoId)
          .SingleOrDefaultAsync(ct);
}
