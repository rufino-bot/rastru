using Rastreamento.Application.Importacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Importacao;

/// <summary>
/// Fake em memoria do repositorio do rascunho. Imita o que o repositorio real faz no <c>Adicionar</c>
/// (Ids, <c>RaizId</c> a partir de <c>Raiz</c>, <c>PaiId</c>/<c>FilhoId</c> a partir das navegacoes,
/// <c>Versao</c> e datas) e guarda a MESMA instancia, como o contexto rastreado do EF devolveria.
/// </summary>
public class FakeImportacaoRepo : IImportacaoDeEstruturaRepository
{
  private int _proximoId = 300;
  private int _proximaVersao = 1;

  public List<ImportacaoDeEstrutura> Importacoes { get; } = [];

  /// <summary>Metadado dos arquivos (solido do catalogo e pendente), chaveado pelo Id do arquivo.</summary>
  public Dictionary<int, MetadadoDeSolido> Arquivos { get; } = [];

  public string NomeDoAutor { get; set; } = "Autora de Teste";

  public int Exclusoes { get; private set; }

  /// <summary>Os Ids que o caso de uso pediu a <see cref="ObterMetadadosAsync"/>, em cada chamada.</summary>
  public List<int[]> MetadadosPedidos { get; } = [];

  public Task<ImportacaoDeEstrutura?> ObterAsync(int id, CancellationToken ct) =>
      Task.FromResult(Importacoes.SingleOrDefault(i => i.Id == id));

  public Task<IReadOnlyList<ResumoDeImportacao>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult<IReadOnlyList<ResumoDeImportacao>>(
          Importacoes.Where(i => i.AgrupamentoId == agrupamentoId)
              .OrderByDescending(i => i.CriadoEm).ThenByDescending(i => i.Id)
              .Select(i => new ResumoDeImportacao(i.Id, i.NomeDoArquivo, NomeDoAutor, i.CriadoEm, i.AtualizadoEm))
              .ToList());

  public Task AdicionarAsync(ImportacaoDeEstrutura importacao, CancellationToken ct)
  {
    importacao.Id = _proximoId++;
    foreach (var registro in importacao.Componentes)
    {
      registro.Id = _proximoId++;
      registro.ImportacaoId = importacao.Id;
    }
    foreach (var registro in importacao.Componentes)
    foreach (var filho in registro.Filhos)
    {
      filho.Id = _proximoId++;
      filho.PaiId = registro.Id;
      filho.FilhoId = filho.Filho!.Id;
    }
    importacao.RaizId = importacao.Raiz?.Id;
    importacao.CriadoEm = importacao.AtualizadoEm = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    importacao.Versao = BitConverter.GetBytes((long)_proximaVersao++);
    Importacoes.Add(importacao);
    return Task.CompletedTask;
  }

  public Task SalvarAsync(ImportacaoDeEstrutura importacao, byte[] versaoEsperada, CancellationToken ct) =>
      throw new NotSupportedException();

  public Task ExcluirAsync(int id, CancellationToken ct)
  {
    Exclusoes++;
    Importacoes.RemoveAll(i => i.Id == id);
    return Task.CompletedTask;
  }

  /// <summary>Agrupamentos que o teste quer fazer passar por "tem rascunho" sem montar um.</summary>
  public HashSet<int> AgrupamentosComRascunho { get; } = [];

  public Task<bool> ExisteNoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult(AgrupamentosComRascunho.Contains(agrupamentoId)
          || Importacoes.Any(i => i.AgrupamentoId == agrupamentoId));

  public Task<string> ObterNomeDoAutorAsync(int usuarioId, CancellationToken ct) => Task.FromResult(NomeDoAutor);

  public Task<int> GravarArquivoPendenteAsync(ArquivoDeComponente arquivo, CancellationToken ct) =>
      throw new NotSupportedException();

  public Task<ArquivoDeComponente?> ObterArquivoAsync(int arquivoId, CancellationToken ct) =>
      throw new NotSupportedException();

  public Task<IReadOnlyDictionary<int, MetadadoDeSolido>> ObterMetadadosAsync(
      IReadOnlyCollection<int> arquivoIds, CancellationToken ct)
  {
    MetadadosPedidos.Add([.. arquivoIds]);
    return Task.FromResult<IReadOnlyDictionary<int, MetadadoDeSolido>>(
        Arquivos.Where(a => arquivoIds.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value));
  }

  public Task ExcluirArquivosAsync(IReadOnlyCollection<int> arquivoIds, CancellationToken ct) =>
      throw new NotSupportedException();
}

/// <summary>
/// So <c>ListarPorCodigosAsync</c>, e SEM diferenciar caixa: e o que a collation do banco faz, e o
/// <c>FakeComponenteRepo</c> dos cadastros e case-sensitive de proposito (ver o comentario dele). O caso
/// de uso do import depende do banco ignorar a caixa, entao o teste precisa de um fake que a ignore.
/// </summary>
public sealed class FakeCatalogoDeComponentes : IComponenteRepository
{
  public List<Componente> Componentes { get; } = [];

  /// <summary>Os codigos que o caso de uso pediu, na ordem da chamada.</summary>
  public List<string[]> CodigosPedidos { get; } = [];

  public Task<IReadOnlyList<Componente>> ListarPorCodigosAsync(
      IReadOnlyCollection<string> codigos, CancellationToken ct)
  {
    CodigosPedidos.Add([.. codigos]);
    return Task.FromResult<IReadOnlyList<Componente>>(
        Componentes.Where(c => codigos.Contains(c.Codigo, StringComparer.OrdinalIgnoreCase)).ToList());
  }

  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => throw new NotSupportedException();

  public Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct) => throw new NotSupportedException();

  public Task AdicionarAsync(Componente componente, CancellationToken ct) => throw new NotSupportedException();
  public Task SalvarAlteracoesAsync(CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>
/// O leitor do arquivo, trocado por linhas ja prontas: o formato (CSV/XLSX) e da Infrastructure e tem
/// teste proprio. Guarda o que recebeu.
/// </summary>
public sealed class FakeLeitorDeBom : ILeitorDeBom
{
  public IReadOnlyList<LinhaCruaDoBom> Linhas { get; set; } = [];
  public IReadOnlyList<ErroDoBom> Erros { get; set; } = [];

  public string? NomeRecebido { get; private set; }

  public ResultadoDaLeituraDoBom Ler(string nomeDoArquivo, byte[] conteudo)
  {
    NomeRecebido = nomeDoArquivo;
    return Erros.Count > 0 ? new([], Erros) : new(Linhas, []);
  }
}
