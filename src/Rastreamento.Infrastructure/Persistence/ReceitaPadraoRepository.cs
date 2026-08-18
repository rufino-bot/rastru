using System.Data;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Infrastructure.Persistence;

public class ReceitaPadraoRepository : IReceitaPadraoRepository
{
  private readonly RastreamentoDbContext _db;

  public ReceitaPadraoRepository(RastreamentoDbContext db) => _db = db;

  public Task<Componente?> ObterComponenteAsync(int id, CancellationToken ct) =>
      _db.Componentes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarFilhosAsync(
      int componenteId, CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking()
          .Where(f => f.ComponentePaiId == componenteId)
          .OrderBy(f => f.Id)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteMaterialPadrao>> ListarMateriaisAsync(
      int componenteId, CancellationToken ct) =>
      await _db.MateriaisPadrao.AsNoTracking()
          .Where(m => m.ComponenteId == componenteId)
          .OrderBy(m => m.Id)
          .ToListAsync(ct);

  // OrderBy(Ordem) e contrato, nao conveniencia: a tela desenha a sequencia na ordem que chega.
  public async Task<IReadOnlyList<ComponenteRoteiroPadrao>> ListarRoteiroAsync(
      int componenteId, CancellationToken ct) =>
      await _db.RoteirosPadrao.AsNoTracking()
          .Where(r => r.ComponenteId == componenteId)
          .OrderBy(r => r.Ordem)
          .ToListAsync(ct);

  public async Task<IReadOnlyList<Componente>> ObterComponentesPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Componentes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Material>> ObterMateriaisPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Materiais.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<Setor>> ObterSetoresPorIdAsync(
      IReadOnlyCollection<int> ids, CancellationToken ct) =>
      await _db.Setores.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);

  public async Task<IReadOnlyList<ComponenteFilhoPadrao>> ListarTodasAsArestasAsync(
      CancellationToken ct) =>
      await _db.FilhosPadrao.AsNoTracking().ToListAsync(ct);

  public Task SubstituirFilhosAsync(
      int componenteId, IReadOnlyList<ComponenteFilhoPadrao> novas, CancellationToken ct) =>
      Substituir(_db.FilhosPadrao, f => f.ComponentePaiId == componenteId, novas, ct);

  public Task SubstituirMateriaisAsync(
      int componenteId, IReadOnlyList<ComponenteMaterialPadrao> novas, CancellationToken ct) =>
      Substituir(_db.MateriaisPadrao, m => m.ComponenteId == componenteId, novas, ct);

  public Task SubstituirRoteiroAsync(
      int componenteId, IReadOnlyList<ComponenteRoteiroPadrao> novas, CancellationToken ct) =>
      Substituir(_db.RoteirosPadrao, r => r.ComponenteId == componenteId, novas, ct);

  /// <summary>
  /// DELETE conjuntista + INSERT dentro de UMA transacao EXPLICITA.
  ///
  /// O delete e <c>ExecuteDeleteAsync</c> sobre o predicado (<c>DELETE ... WHERE ComponenteId =
  /// @id</c>), e nao <c>ToListAsync</c> + <c>RemoveRange</c>: o segundo apaga pelas PKs das linhas
  /// LIDAS, entao uma linha inserida por outro escritor entre a leitura e o SaveChanges nunca era
  /// apagada e a receita final virava a UNIAO das duas gravacoes — medido 3 de 3 na review da
  /// Task 2, sem excecao nenhuma. Conjuntista, o delete alcanca tambem o que este escritor nao viu.
  ///
  /// A transacao e explicita porque <c>ExecuteDeleteAsync</c> emite o DELETE FORA da transacao
  /// implicita do <c>SaveChanges</c>: sem ela o meio-termo "apagou e nao gravou" seria alcancavel
  /// de verdade. Com ela, o DELETE e o INSERT sobem ou caem juntos.
  ///
  /// As duas propriedades sao verificaveis, nao declaradas:
  /// <c>Substituir_filhos_que_estoura_no_meio_deixa_as_linhas_antigas_intactas</c> morre se a
  /// transacao sair ou for partida em dois commits;
  /// <c>Substituicoes_paralelas_do_mesmo_componente_nao_deixam_a_uniao</c> morre se o delete
  /// voltar a ser por PK das linhas lidas, se o isolamento cair para o default, ou se a transacao
  /// sair.
  ///
  /// O isolamento e SERIALIZABLE, e nao o READ COMMITTED default, porque receita VAZIA nao tem
  /// linha para travar: os dois DELETEs nao acham nada, nao seguram lock nenhum, e os dois INSERTs
  /// passam — a uniao volta. Medido nesta arvore, com a transacao explicita e READ COMMITTED, na
  /// suite completa. O range lock do SERIALIZABLE trava a FAIXA vazia, e ai um dos escritores
  /// espera o outro (ou e derrubado por deadlock, que e desfecho legitimo: o caso de uso traduz
  /// para 409).
  ///
  /// Esse desfecho legitimo sobe como <see cref="ConflitoDeConcorrenciaException"/>, e nao cru:
  /// <c>EhConflitoDeConcorrencia</c> reconhece deadlock (1205) e lock timeout (1222) em qualquer
  /// ponto da cadeia de inner exceptions — o DELETE do <c>ExecuteDelete</c> nao passa pelo
  /// <c>SaveChanges</c> e sobe o <c>SqlException</c> direto, o INSERT sobe embrulhado. E o mesmo
  /// padrao de <c>RefreshTokenRepository.SalvarAlteracoesAsync</c> e existe pelo mesmo motivo: a
  /// Application captura e traduz para 409 sem referenciar o EF Core. As duas
  /// direcoes tem teste — <c>Perdedor_de_gravacao_simultanea_sobe_como_conflito_de_concorrencia</c>
  /// morre se a traducao sair, e <c>Substituir_filhos_que_estoura_no_meio_deixa_as_linhas_antigas_intactas</c>
  /// morre se ela ficar larga demais (violacao de FK, 547, tem de continuar <c>DbUpdateException</c>).
  ///
  /// O custo do SERIALIZABLE aqui e limitado pelo schema, nao pela sorte: as tres tabelas tem
  /// UNIQUE liderado pela coluna do componente (UQ_ComponenteFilhoPadrao, UQ_ComponenteMaterialPadrao,
  /// UQ_ComponenteRoteiroPadrao), entao ha indice para o range lock morar. Enquanto as tabelas forem
  /// pequenas o otimizador pode preferir varredura e travar mais que a faixa — nao esta medido, e
  /// nao doi no MVP, onde receita padrao e escrita por tela de PCP, uma de cada vez.
  /// </summary>
  private async Task Substituir<T>(
      DbSet<T> tabela,
      Expression<Func<T, bool>> doComponente,
      IReadOnlyList<T> novas,
      CancellationToken ct) where T : class
  {
    try
    {
      await using var tx =
          await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
      await tabela.Where(doComponente).ExecuteDeleteAsync(ct);
      tabela.AddRange(novas);
      await _db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (Exception e) when (EhConflitoDeConcorrencia(e))
    {
      throw new ConflitoDeConcorrenciaException(e);
    }
  }

  /// <summary>
  /// 1205 = deadlock victim; 1222 = lock request timeout. Sao os dois jeitos de o gerenciador de
  /// lock derrubar o PERDEDOR de duas substituicoes simultaneas do mesmo componente — desfecho
  /// previsto pelo SERIALIZABLE, e nao falha do servidor. Qualquer outro numero (violacao de FK ou
  /// de UNIQUE, por exemplo) passa cru de proposito: traduzir tudo para conflito esconderia bug
  /// real atras de um 409.
  ///
  /// Percorre a CADEIA de inner exceptions em vez de olhar um nivel so, porque a profundidade
  /// varia com o caminho: o DELETE do <c>ExecuteDelete</c> sobe <c>SqlException</c> pelado, o
  /// INSERT sobe embrulhado em <c>DbUpdateException</c>, e o EF ainda embrulha esse par num
  /// <c>InvalidOperationException</c> de "transient failure" quando reconhece o erro como
  /// transitorio — medido, e foi o que reprovou a primeira versao desta guarda.
  /// </summary>
  private static bool EhConflitoDeConcorrencia(Exception e)
  {
    for (Exception? atual = e; atual is not null; atual = atual.InnerException)
      if (atual is SqlException sql && sql.Number is 1205 or 1222) return true;

    return false;
  }
}
