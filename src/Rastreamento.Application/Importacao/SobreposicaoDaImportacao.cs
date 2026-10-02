using Rastreamento.Application.Estrutura;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// O rascunho posto por cima do catalogo, na forma que o <see cref="PlanejadorDeCopia"/> entende: uma
/// <see cref="ReceitaDoCatalogo"/> sobre <c>int</c>. PURO, como o avaliador que o usa.
///
/// Decisao P1 do plano do import: um registro casado e o <c>ComponenteId</c>, um registro "criar novo"
/// e <c>-registro.Id</c>. Materiais e roteiro sao os do catalogo, entao um Id negativo sai sem nenhum,
/// como um Componente sem receita.
///
/// Decisao P2: a receita efetiva de um registro e a LIDA, menos quando ele diverge e a escolha e
/// "Catalogo". Divergente sem escolha usa a lida, provisoriamente, e o avaliador bloqueia. Um Id que
/// nao e de registro nenhum (alcancado so pelo catalogo) usa a do catalogo. A receita vale para TODAS
/// as ocorrencias do Id (D5 da spec do import), porque e por Id, e nao por no.
///
/// Leitura do banco: <see cref="ImportacaoDeEstrutura.Raiz"/> e <see cref="ImportacaoDeEstruturaFilho.Filho"/>
/// vem nulas, entao tudo aqui anda pelos Ids (<c>RaizId</c>, <c>FilhoId</c>).
/// </summary>
internal sealed class SobreposicaoDaImportacao
{
  private readonly ReceitaDoCatalogo _catalogo;
  private readonly Dictionary<int, ImportacaoDeEstruturaComponente> _porRegistro;

  /// <summary>
  /// Id positivo -&gt; o registro casado com ele. Dois registros casados com o mesmo Componente nao
  /// nascem do arquivo (o casamento automatico e por codigo, e o codigo e unico no rascunho); se a
  /// edicao manual os produzir, vale o de menor Id, para o resultado ser deterministico.
  /// </summary>
  private readonly Dictionary<int, ImportacaoDeEstruturaComponente> _porComponente;

  private readonly HashSet<int> _divergentes = [];
  private readonly HashSet<int> _comReceitaLida = [];
  private readonly HashSet<int> _naArvore;

  public SobreposicaoDaImportacao(ImportacaoDeEstrutura rascunho, ReceitaDoCatalogo catalogo)
  {
    if (rascunho.RaizId is not int raizId)
      throw new InvalidOperationException($"O rascunho {rascunho.Id} nao tem raiz.");

    _catalogo = catalogo;
    _porRegistro = rascunho.Componentes.ToDictionary(c => c.Id);
    _porComponente = rascunho.Componentes
        .Where(c => c.ComponenteId is not null)
        .GroupBy(c => c.ComponenteId!.Value)
        .ToDictionary(g => g.Key, g => g.MinBy(c => c.Id)!);

    foreach (var registro in rascunho.Componentes)
    {
      if (registro.ComponenteId is not null && Diverge(registro.ComponenteId.Value, registro))
        _divergentes.Add(registro.Id);
    }

    var arestas = new List<(int Pai, int Filho, decimal Quantidade)>();
    foreach (var registro in rascunho.Componentes)
    {
      var id = IdDe(registro);
      if (RepresentaOId(registro, id) && UsaALida(registro))
      {
        _comReceitaLida.Add(id);
        arestas.AddRange(Lida(registro).Select(l => (id, l.FilhoId, l.Quantidade)));
      }
    }
    foreach (var grupo in catalogo.Filhos.Where(g => !_comReceitaLida.Contains(g.Key)))
      arestas.AddRange(grupo.Select(f => (grupo.Key, f.FilhoId, f.QuantidadePadrao)));

    Receita = new ReceitaDoCatalogo(
        arestas.ToLookup(a => a.Pai, a => (a.Filho, a.Quantidade)), catalogo.Materiais, catalogo.Roteiro);
    RaizId = IdDe(_porRegistro[raizId]);
    Alcancaveis = PreOrdem(RaizId);
    _naArvore = [.. Alcancaveis];
  }

  /// <summary>A receita sobreposta: a que o planejador expande e a que a confirmacao reproduz.</summary>
  public ReceitaDoCatalogo Receita { get; }

  public int RaizId { get; }

  /// <summary>
  /// Os Ids distintos alcancados a partir da raiz na sobreposicao, em pre-ordem da primeira ocorrencia.
  /// E a arvore final (decisao P3 do plano do import) e nao depende da expansao dar certo: com ciclo,
  /// profundidade ou tamanho recusados, ela continua respondendo o que esta na arvore.
  /// </summary>
  public IReadOnlyList<int> Alcancaveis { get; }

  public bool EstaNaArvore(int id) => _naArvore.Contains(id);

  public static int IdDe(ImportacaoDeEstruturaComponente registro) => registro.ComponenteId ?? -registro.Id;

  public ImportacaoDeEstruturaComponente? RegistroDoId(int id) =>
      id < 0 ? _porRegistro.GetValueOrDefault(-id) : _porComponente.GetValueOrDefault(id);

  public bool Diverge(ImportacaoDeEstruturaComponente registro) => _divergentes.Contains(registro.Id);

  public bool UsaALida(ImportacaoDeEstruturaComponente registro) =>
      !Diverge(registro) || registro.EscolhaDeReceita != ValoresDaConferencia.EscolhaCatalogo;

  /// <summary>A receita lida do registro, um nivel, na ordem do BOM e com os Ids da sobreposicao.</summary>
  public IReadOnlyList<(int FilhoId, decimal Quantidade, ImportacaoDeEstruturaFilho Linha)> Lida(
      ImportacaoDeEstruturaComponente registro) =>
      registro.Filhos
          .OrderBy(f => f.Ordem)
          .Select(f => (IdDe(_porRegistro[f.FilhoId]), f.Quantidade, f))
          .ToList();

  /// <summary>
  /// A linha da receita lida que pendura <paramref name="filhoId"/> sob <paramref name="paiId"/>, quando
  /// a aresta e do BOM. Nula quando o pai usa a receita do catalogo.
  /// </summary>
  public ImportacaoDeEstruturaFilho? LinhaDaAresta(int paiId, int filhoId)
  {
    if (!_comReceitaLida.Contains(paiId) || RegistroDoId(paiId) is not { } pai)
      return null;
    return Lida(pai).Where(l => l.FilhoId == filhoId).Select(l => l.Linha).FirstOrDefault();
  }

  /// <summary>
  /// Quantos nos a expansao das raizes dadas produz na sobreposicao, contando cada ocorrencia. Um Id
  /// repetido no proprio caminho (ciclo) nao desce de novo, e a conta para logo depois de passar de
  /// <see cref="PlanejadorDeCopia.NosMaximos"/>: uma arvore desse tamanho ja e recusada pelo planejador,
  /// e um diamante largo explodiria a conta sem esse teto.
  /// </summary>
  public int ContarExpandidos(IEnumerable<int> raizes)
  {
    var total = 0;
    var caminho = new HashSet<int>();
    foreach (var id in raizes)
      Contar(id, caminho, ref total);
    return total;
  }

  private void Contar(int id, HashSet<int> caminho, ref int total)
  {
    if (total > PlanejadorDeCopia.NosMaximos || !caminho.Add(id))
      return;
    total++;
    foreach (var f in Receita.Filhos[id])
      Contar(f.FilhoId, caminho, ref total);
    caminho.Remove(id);
  }

  private bool RepresentaOId(ImportacaoDeEstruturaComponente registro, int id) =>
      id < 0 || _porComponente[id].Id == registro.Id;

  /// <summary>
  /// Secao 5.2 da spec do import: diverge o casado cuja receita de catalogo existe e e diferente da
  /// lida (filhos diretos e quantidades). Casado sem receita no catalogo nao diverge, mesmo com filhos
  /// no BOM: recebe a lida sem escolha. Casado folha no BOM com receita no catalogo diverge.
  /// </summary>
  private bool Diverge(int componenteId, ImportacaoDeEstruturaComponente registro)
  {
    var doCatalogo = Somar(_catalogo.Filhos[componenteId].Select(f => (f.FilhoId, f.QuantidadePadrao)));
    if (doCatalogo.Count == 0)
      return false;
    var lida = Somar(Lida(registro).Select(l => (l.FilhoId, l.Quantidade)));
    return lida.Count != doCatalogo.Count
        || lida.Any(l => !doCatalogo.TryGetValue(l.Key, out var q) || q != l.Value);
  }

  private static Dictionary<int, decimal> Somar(IEnumerable<(int FilhoId, decimal Quantidade)> linhas)
  {
    var soma = new Dictionary<int, decimal>();
    foreach (var (filho, quantidade) in linhas)
      soma[filho] = soma.GetValueOrDefault(filho) + quantidade;
    return soma;
  }

  /// <summary>Iterativa: uma corrente longa no catalogo nao pode estourar a pilha.</summary>
  private List<int> PreOrdem(int raiz)
  {
    var ordem = new List<int>();
    var vistos = new HashSet<int>();
    var pilha = new Stack<int>();
    pilha.Push(raiz);
    while (pilha.Count > 0)
    {
      var id = pilha.Pop();
      if (!vistos.Add(id))
        continue;
      ordem.Add(id);
      foreach (var f in Receita.Filhos[id].Reverse())
        pilha.Push(f.FilhoId);
    }
    return ordem;
  }
}
