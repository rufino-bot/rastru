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

  /// <summary>Quantas vezes o caso de uso chegou ao <c>SalvarAsync</c>: erro de validacao nao deve chegar.</summary>
  public int Salvamentos { get; private set; }

  /// <summary>
  /// Simula a corrida que o banco acusa: outra requisicao salvou entre a leitura e o salvamento, e o
  /// <c>SalvarAsync</c> lanca o conflito mesmo com a versao do corpo igual a que o caso de uso leu.
  /// </summary>
  public bool PerderACorridaNoSalvamento { get; set; }

  /// <summary>Uma falha qualquer (nao de versao) do salvamento, como uma queda do banco.</summary>
  public Exception? FalhaNoSalvamento { get; set; }

  /// <summary>Roda quando o salvamento termina bem, para o teste simular o que acontece logo depois do commit.</summary>
  public Action? AposSalvar { get; set; }

  private readonly List<ImportacaoDeEstruturaComponente> _registrosParaRemover = [];
  private readonly List<ImportacaoDeEstruturaFilho> _filhosParaRemover = [];

  public void RemoverRegistros(IEnumerable<ImportacaoDeEstruturaComponente> registros) =>
      _registrosParaRemover.AddRange(registros);

  public void RemoverFilhos(IEnumerable<ImportacaoDeEstruturaFilho> filhos) =>
      _filhosParaRemover.AddRange(filhos);

  /// <summary>
  /// Imita o <c>SalvarAsync</c> real: confere a versao (a mesma instancia e a "rastreada", e o caso de
  /// uso nunca mexe em <c>Versao</c>), aplica as exclusoes marcadas (que o EF tambem tira das colecoes),
  /// da Id ao que entrou, resolve <c>PaiId</c>/<c>FilhoId</c> pelas navegacoes e troca a versao.
  /// </summary>
  public Task SalvarAsync(ImportacaoDeEstrutura importacao, byte[] versaoEsperada, CancellationToken ct)
  {
    if (PerderACorridaNoSalvamento || !importacao.Versao.AsSpan().SequenceEqual(versaoEsperada))
    {
      // O contexto real descarta o que estava marcado quando o salvamento falha; a marca nao vaza para o proximo.
      _filhosParaRemover.Clear();
      _registrosParaRemover.Clear();
      throw new ConflitoDeConcorrenciaException(new InvalidOperationException("versao velha"));
    }

    if (FalhaNoSalvamento is not null)
    {
      _filhosParaRemover.Clear();
      _registrosParaRemover.Clear();
      throw FalhaNoSalvamento;
    }

    Salvamentos++;
    foreach (var filho in _filhosParaRemover)
    foreach (var registro in importacao.Componentes)
      registro.Filhos.Remove(filho);
    importacao.Componentes.RemoveAll(_registrosParaRemover.Contains);
    _filhosParaRemover.Clear();
    _registrosParaRemover.Clear();

    foreach (var registro in importacao.Componentes.Where(c => c.Id == 0))
    {
      registro.Id = _proximoId++;
      registro.ImportacaoId = importacao.Id;
    }
    foreach (var registro in importacao.Componentes)
    foreach (var filho in registro.Filhos.Where(f => f.Id == 0))
    {
      filho.Id = _proximoId++;
      filho.PaiId = registro.Id;
      if (filho.Filho is not null)
        filho.FilhoId = filho.Filho.Id;
    }

    importacao.AtualizadoEm = importacao.AtualizadoEm.AddMinutes(1);
    importacao.Versao = BitConverter.GetBytes((long)_proximaVersao++);
    AposSalvar?.Invoke();
    return Task.CompletedTask;
  }

  /// <summary>Quando preenchida, e a versao que o "banco" tem agora, diferente da instancia: simula outra escrita.</summary>
  public byte[]? VersaoNoBanco { get; set; }

  public Task<byte[]?> ObterVersaoAsync(int id, CancellationToken ct) =>
      Task.FromResult(VersaoNoBanco ?? Importacoes.SingleOrDefault(i => i.Id == id)?.Versao);

  /// <summary>Como o real: apaga tambem os arquivos que os registros ainda tem como pendentes.</summary>
  public Task ExcluirAsync(int id, CancellationToken ct)
  {
    Exclusoes++;
    foreach (var arquivo in Importacoes.Where(i => i.Id == id).SelectMany(i => i.Componentes)
                 .Where(c => c.ArquivoSolidoPendenteId is not null).Select(c => c.ArquivoSolidoPendenteId!.Value))
    {
      ArquivosExcluidos.Add(arquivo);
      ArquivosGravados.Remove(arquivo);
      Arquivos.Remove(arquivo);
    }
    Importacoes.RemoveAll(i => i.Id == id);
    return Task.CompletedTask;
  }

  /// <summary>Os registros de cada <see cref="DesligarSolidosPendentesAsync"/>, como chegaram.</summary>
  public List<int[]> Desligamentos { get; } = [];

  public Task DesligarSolidosPendentesAsync(IReadOnlyCollection<int> registroIds, CancellationToken ct)
  {
    Desligamentos.Add([.. registroIds]);
    foreach (var registro in Importacoes.SelectMany(i => i.Componentes).Where(c => registroIds.Contains(c.Id)))
      registro.ArquivoSolidoPendenteId = null;
    return Task.CompletedTask;
  }

  /// <summary>Agrupamentos que o teste quer fazer passar por "tem rascunho" sem montar um.</summary>
  public HashSet<int> AgrupamentosComRascunho { get; } = [];

  public Task<bool> ExisteNoAgrupamentoAsync(int agrupamentoId, CancellationToken ct) =>
      Task.FromResult(AgrupamentosComRascunho.Contains(agrupamentoId)
          || Importacoes.Any(i => i.AgrupamentoId == agrupamentoId));

  public Task<string> ObterNomeDoAutorAsync(int usuarioId, CancellationToken ct) => Task.FromResult(NomeDoAutor);

  /// <summary>Os arquivos que o caso de uso gravou (solido pendente), com o blob, chaveados pelo Id.</summary>
  public Dictionary<int, ArquivoDeComponente> ArquivosGravados { get; } = [];

  /// <summary>Os Ids que <see cref="ExcluirArquivosAsync"/> recebeu, na ordem das chamadas.</summary>
  public List<int> ArquivosExcluidos { get; } = [];

  public Task<int> GravarArquivoPendenteAsync(ArquivoDeComponente arquivo, CancellationToken ct)
  {
    arquivo.Id = _proximoId++;
    ArquivosGravados[arquivo.Id] = arquivo;
    Arquivos[arquivo.Id] = new MetadadoDeSolido(arquivo.NomeOriginal, arquivo.Conteudo.Length);
    return Task.FromResult(arquivo.Id);
  }

  public Task<ArquivoDeComponente?> ObterArquivoAsync(int arquivoId, CancellationToken ct) =>
      Task.FromResult(ArquivosGravados.GetValueOrDefault(arquivoId));

  public Task<IReadOnlyDictionary<int, MetadadoDeSolido>> ObterMetadadosAsync(
      IReadOnlyCollection<int> arquivoIds, CancellationToken ct)
  {
    MetadadosPedidos.Add([.. arquivoIds]);
    return Task.FromResult<IReadOnlyDictionary<int, MetadadoDeSolido>>(
        Arquivos.Where(a => arquivoIds.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value));
  }

  public Task ExcluirArquivosAsync(IReadOnlyCollection<int> arquivoIds, CancellationToken ct)
  {
    // Como o driver: com o token cancelado, nao apaga nada.
    ct.ThrowIfCancellationRequested();
    foreach (var id in arquivoIds)
    {
      ArquivosExcluidos.Add(id);
      ArquivosGravados.Remove(id);
      Arquivos.Remove(id);
    }
    return Task.CompletedTask;
  }
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

  /// <summary>A mesma instancia da lista, como a entidade rastreada do EF: quem a muda e salva, grava.</summary>
  public Task<Componente?> ObterPorIdAsync(int id, CancellationToken ct) =>
      Task.FromResult(Componentes.SingleOrDefault(c => c.Id == id));

  public Task<Componente?> ObterPorCodigoAsync(string codigo, CancellationToken ct) => throw new NotSupportedException();

  public Task<(IReadOnlyList<Componente> Itens, int Total)> ListarAsync(
      FiltroDeComponente filtro, CancellationToken ct) => throw new NotSupportedException();

  private int _proximoId = 8000;
  private readonly List<Componente> _pendentes = [];

  /// <summary>Os Componentes que o caso de uso criou, na ordem em que foram salvos.</summary>
  public List<Componente> Adicionados { get; } = [];

  public int Salvamentos { get; private set; }

  /// <summary>Roda para cada Componente criado, quando ele ganha Id: o teste o espelha onde mais precisar (a leitura por Id).</summary>
  public Action<Componente>? AoCriar { get; set; }

  /// <summary>Como o EF: o Componente so ganha Id e entra no catalogo no <see cref="SalvarAlteracoesAsync"/>.</summary>
  public Task AdicionarAsync(Componente componente, CancellationToken ct)
  {
    _pendentes.Add(componente);
    return Task.CompletedTask;
  }

  public Task SalvarAlteracoesAsync(CancellationToken ct)
  {
    Salvamentos++;
    foreach (var c in _pendentes)
    {
      c.Id = _proximoId++;
      Componentes.Add(c);
      Adicionados.Add(c);
      AoCriar?.Invoke(c);
    }
    _pendentes.Clear();
    return Task.CompletedTask;
  }
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
