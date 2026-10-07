using System.Globalization;
using Rastreamento.Application.Estrutura;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// Transforma as linhas cruas do BOM em UMA receita por codigo (decisao D12 da spec do import) ou numa
/// lista de erros do arquivo (D13). PURO: sem I/O e sem banco — quem le o arquivo e a Infrastructure, e
/// quem grava o rascunho e o caso de uso.
///
/// A raiz (chave 0) e o proprio arquivo: recebe as linhas de um segmento so no nivel. O pai de uma
/// linha e a ULTIMA linha lida cujo nivel e o prefixo do dela (todos os segmentos menos o ultimo) —
/// nao a linha anterior, porque o CAD pode intercalar ramos.
///
/// Os erros vem TODOS juntos, cada um com a linha. Havendo qualquer erro, <see cref="BomMontado.Componentes"/>
/// e <see cref="BomMontado.Filhos"/> voltam vazios: ninguem deve montar rascunho de arquivo recusado.
///
/// Um arquivo finito so forma ciclo entre codigos deixando a ocorrencia mais funda sem os filhos que a
/// primeira tem, e isso tambem e conflito de receita (D13). O ciclo e detectado sobre as arestas de
/// TODAS as ocorrencias, nao so as da primeira, para ser dito mesmo quando o conflito de receita o
/// acompanha — e o que o operador precisa ler para consertar.
/// </summary>
public static class MontadorDeReceitasDoBom
{
  private const int LimiteDeQuantidadeDecimais = 4;

  public static BomMontado Montar(string nomeDoArquivo, IReadOnlyList<LinhaCruaDoBom> linhas)
  {
    if (linhas.Count == 0)
      return Recusado([new ErroDoBom(null, "o arquivo está vazio: nenhuma linha de item foi encontrada.")]);

    var erros = new List<ErroDoBom>();
    var lidas = LerAsLinhas(linhas, erros);
    var chavesDaLinha = AtribuirChaves(linhas, lidas, out var codigoDaChave, out var descricaoDaChave,
        out var primeiraLinhaDaChave);

    // Uma receita por OCORRENCIA de pai: a da linha `i` (indice em `linhas`), ou a da raiz (-1).
    var receitaDaOcorrencia = new Dictionary<int, Receita>();
    foreach (var (i, linha) in lidas.Index().Where(x => x.Item.Valida))
    {
      var pai = linha.Pai;
      if (pai == Orfa) continue;

      if (!receitaDaOcorrencia.TryGetValue(pai, out var receita))
        receitaDaOcorrencia[pai] = receita = new Receita();

      var soma = receita.Somar(chavesDaLinha[i], linha.Quantidade);
      if (soma > PlanejadorDeCopia.QuantidadeMaximaDaColuna)
        erros.Add(new ErroDoBom(linhas[i].NumeroDaLinha,
            $"quantidade inválida: a soma das ocorrências deste item sob o mesmo pai ({soma.ToString(CultureInfo.InvariantCulture)}) "
            + $"passa do máximo de {PlanejadorDeCopia.QuantidadeMaximaDaColuna.ToString(CultureInfo.InvariantCulture)}."));
    }

    var vazia = new Receita();
    Receita DaOcorrencia(int linhaDoPai) =>
        receitaDaOcorrencia.TryGetValue(linhaDoPai, out var r) ? r : vazia;

    // Receita de cada componente = a da primeira ocorrencia; as demais precisam ser iguais a ela.
    var receitaDoComponente = new Dictionary<int, Receita> { [0] = DaOcorrencia(-1) };
    // A lista guarda a ordem (o ciclo e dito na ordem do arquivo); o conjunto ao lado tira o repetido
    // sem varrer a lista, que pode ter dezenas de milhares de filhos.
    var arestas = new Dictionary<int, List<int>> { [0] = [.. DaOcorrencia(-1).Itens.Select(f => f.Chave)] };
    var arestasVistas = new Dictionary<int, HashSet<int>> { [0] = [.. arestas[0]] };
    var primeiraOcorrencia = new Dictionary<int, int>();
    foreach (var (i, linha) in lidas.Index().Where(x => x.Item.Valida && x.Item.Pai != Orfa))
    {
      var chave = chavesDaLinha[i];
      var daOcorrencia = DaOcorrencia(i);
      if (!arestas.TryGetValue(chave, out var vizinhos))
      {
        arestas[chave] = vizinhos = [];
        arestasVistas[chave] = [];
      }
      var vistos = arestasVistas[chave];
      foreach (var f in daOcorrencia.Itens)
        if (vistos.Add(f.Chave))
          vizinhos.Add(f.Chave);

      if (!primeiraOcorrencia.TryGetValue(chave, out var primeira))
      {
        primeiraOcorrencia[chave] = i;
        receitaDoComponente[chave] = daOcorrencia;
      }
      else if (!daOcorrencia.IgualA(receitaDoComponente[chave]))
      {
        erros.Add(new ErroDoBom(linhas[i].NumeroDaLinha,
            $"o código '{codigoDaChave[chave]}' aparece com filhos diferentes "
            + $"(linhas {linhas[primeira].NumeroDaLinha} e {linhas[i].NumeroDaLinha})."));
      }
    }

    var haCiclo = DetectarCiclos(arestas, codigoDaChave, primeiraLinhaDaChave, linhas, erros);
    if (!haCiclo)
      ConferirTamanho(receitaDoComponente, erros);

    if (erros.Count > 0)
      return Recusado(erros);

    var componentes = new List<ComponenteDoBom> { new(0, null, DescricaoDaRaiz(nomeDoArquivo)) };
    componentes.AddRange(descricaoDaChave.OrderBy(d => d.Key)
        .Select(d => new ComponenteDoBom(d.Key, codigoDaChave.GetValueOrDefault(d.Key), d.Value)));

    var filhos = receitaDoComponente.OrderBy(r => r.Key)
        .SelectMany(r => r.Value.Itens.Select((f, ordem) => new FilhoDoBom(r.Key, f.Chave, ordem + 1, f.Quantidade)))
        .ToList();

    return new BomMontado(componentes, filhos, []);
  }

  private static BomMontado Recusado(IEnumerable<ErroDoBom> erros) =>
      new([], [], erros.OrderBy(e => e.Linha ?? int.MaxValue).ToList());

  private static string DescricaoDaRaiz(string nomeDoArquivo)
  {
    var semExtensao = Path.GetFileNameWithoutExtension(nomeDoArquivo);
    return string.IsNullOrWhiteSpace(semExtensao) ? nomeDoArquivo : semExtensao;
  }

  private const int Orfa = -2;

  private readonly record struct LinhaLida(bool Valida, int Pai, string? Codigo, string Descricao, decimal Quantidade);

  /// <summary>
  /// Le nivel, codigo e quantidade de cada linha, resolve o pai (o indice da linha, -1 para a raiz,
  /// <see cref="Orfa"/> quando o nivel pula degrau) e acumula os erros de formato. Uma linha com
  /// quantidade ruim continua valida como no da estrutura: senao a quantidade ruim viraria, em
  /// cascata, um "nivel que pula degrau" em todos os filhos dela.
  /// </summary>
  private static List<LinhaLida> LerAsLinhas(IReadOnlyList<LinhaCruaDoBom> linhas, List<ErroDoBom> erros)
  {
    var lidas = new List<LinhaLida>(linhas.Count);
    var ultimaComONivel = new Dictionary<string, int>();

    for (var i = 0; i < linhas.Count; i++)
    {
      var linha = linhas[i];
      var codigo = string.IsNullOrWhiteSpace(linha.Codigo) ? null : linha.Codigo.Trim();
      var descricao = (linha.Descricao ?? "").Trim();

      decimal quantidade = 0;
      var erroDeQuantidade = LerQuantidade(linha.Quantidade, out quantidade);
      if (erroDeQuantidade is not null)
        erros.Add(new ErroDoBom(linha.NumeroDaLinha, erroDeQuantidade));

      var segmentos = LerNivel(linha.Nivel);
      if (segmentos is null)
      {
        erros.Add(new ErroDoBom(linha.NumeroDaLinha,
            $"nível inválido '{linha.Nivel?.Trim()}': use inteiros positivos separados por ponto, como 1.2.3."));
        lidas.Add(new LinhaLida(false, Orfa, codigo, descricao, quantidade));
        continue;
      }

      var pai = -1;
      if (segmentos.Length > 1)
      {
        var prefixo = string.Join('.', segmentos[..^1]);
        if (!ultimaComONivel.TryGetValue(prefixo, out pai))
        {
          pai = Orfa;
          erros.Add(new ErroDoBom(linha.NumeroDaLinha,
              $"o nível '{string.Join('.', segmentos)}' pula um degrau: falta o item '{prefixo}' antes dele."));
        }
      }

      ultimaComONivel[string.Join('.', segmentos)] = i;
      lidas.Add(new LinhaLida(true, pai, codigo, descricao, quantidade));
    }

    return lidas;
  }

  /// <summary>Segmentos inteiros positivos do nivel, ou nulo se algum nao for. "01" e "1" sao o mesmo.</summary>
  private static string[]? LerNivel(string? nivel)
  {
    var partes = (nivel ?? "").Trim().Split('.');
    var normalizados = new string[partes.Length];
    for (var k = 0; k < partes.Length; k++)
    {
      var p = partes[k];
      if (p.Length == 0 || !p.All(char.IsAsciiDigit) || !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n <= 0)
        return null;
      normalizados[k] = n.ToString(CultureInfo.InvariantCulture);
    }
    return normalizados;
  }

  /// <summary>
  /// Decisao P10 do plano do import: aceita "1,5" e "1.5", sem separador de milhar, de
  /// <see cref="PlanejadorDeCopia.QuantidadeMinimaDaColuna"/> a
  /// <see cref="PlanejadorDeCopia.QuantidadeMaximaDaColuna"/>, com no maximo 4 casas decimais
  /// (zeros a direita nao contam: "1.50000" e 1,5).
  /// </summary>
  private static string? LerQuantidade(string? texto, out decimal quantidade)
  {
    quantidade = 0;
    var t = (texto ?? "").Trim();
    var invalida = $"quantidade inválida '{t}': use um número de {PlanejadorDeCopia.QuantidadeMinimaDaColuna.ToString(CultureInfo.InvariantCulture)} "
        + $"a {PlanejadorDeCopia.QuantidadeMaximaDaColuna.ToString(CultureInfo.InvariantCulture)}, "
        + $"com no máximo {LimiteDeQuantidadeDecimais} casas decimais.";

    var separadores = t.Count(c => c is '.' or ',');
    if (t.Length == 0 || separadores > 1 || !t.All(c => char.IsAsciiDigit(c) || c is '.' or ',')
        || t[0] is '.' or ',' || t[^1] is '.' or ',')
      return invalida;

    var normalizado = t.Replace(',', '.');
    var ponto = normalizado.IndexOf('.');
    if (ponto >= 0 && normalizado[(ponto + 1)..].TrimEnd('0').Length > LimiteDeQuantidadeDecimais)
      return invalida;

    if (!decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor)
        || valor < PlanejadorDeCopia.QuantidadeMinimaDaColuna
        || valor > PlanejadorDeCopia.QuantidadeMaximaDaColuna)
      return invalida;

    quantidade = valor;
    return null;
  }

  /// <summary>
  /// Um componente por codigo (comparacao sem diferenciar caixa, o mesmo efeito da collation CI do
  /// banco) e um por linha sem codigo. Chaves a partir de 1, na ordem da primeira aparicao.
  /// </summary>
  private static int[] AtribuirChaves(
      IReadOnlyList<LinhaCruaDoBom> linhas,
      List<LinhaLida> lidas,
      out Dictionary<int, string?> codigoDaChave,
      out Dictionary<int, string> descricaoDaChave,
      out Dictionary<int, int> primeiraLinhaDaChave)
  {
    codigoDaChave = [];
    descricaoDaChave = [];
    primeiraLinhaDaChave = [];
    var chaveDoCodigo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var chaves = new int[linhas.Count];
    var proxima = 1;

    for (var i = 0; i < linhas.Count; i++)
    {
      if (!lidas[i].Valida) continue;

      var codigo = lidas[i].Codigo;
      if (codigo is not null && chaveDoCodigo.TryGetValue(codigo, out var existente))
      {
        chaves[i] = existente;
        continue;
      }

      var chave = proxima++;
      chaves[i] = chave;
      descricaoDaChave[chave] = lidas[i].Descricao;
      primeiraLinhaDaChave[chave] = i;
      if (codigo is not null)
      {
        chaveDoCodigo[codigo] = chave;
        codigoDaChave[chave] = codigo;
      }
    }

    return chaves;
  }

  /// <summary>DFS sobre as arestas, a partir da raiz. Cada aresta de volta e um ciclo, dito uma vez.</summary>
  private static bool DetectarCiclos(
      Dictionary<int, List<int>> arestas,
      Dictionary<int, string?> codigoDaChave,
      Dictionary<int, int> primeiraLinhaDaChave,
      IReadOnlyList<LinhaCruaDoBom> linhas,
      List<ErroDoBom> erros)
  {
    var estado = new Dictionary<int, int>(); // ausente = nao visitado, 1 = no caminho, 2 = concluido
    var caminho = new List<int>();
    var achou = false;

    string Rotulo(int chave) =>
        codigoDaChave.TryGetValue(chave, out var c) && c is not null
            ? c
            : $"(sem código, linha {linhas[primeiraLinhaDaChave[chave]].NumeroDaLinha})";

    void Visitar(int chave)
    {
      estado[chave] = 1;
      caminho.Add(chave);
      foreach (var filho in arestas.GetValueOrDefault(chave) ?? [])
      {
        var e = estado.GetValueOrDefault(filho);
        if (e == 1)
        {
          achou = true;
          var trecho = caminho.Skip(caminho.IndexOf(filho)).Append(filho).Select(Rotulo);
          erros.Add(new ErroDoBom(linhas[primeiraLinhaDaChave[filho]].NumeroDaLinha,
              $"ciclo entre códigos: {string.Join(" -> ", trecho)}."));
        }
        else if (e == 0)
          Visitar(filho);
      }
      caminho.RemoveAt(caminho.Count - 1);
      estado[chave] = 2;
    }

    Visitar(0);
    return achou;
  }

  /// <summary>
  /// Mesmos limites e mesma contagem do <see cref="PlanejadorDeCopia"/>: a raiz e o nivel 1 e o primeiro
  /// no, e CADA OCORRENCIA da arvore expandida conta, mesmo quando o codigo se repete — e assim que a
  /// confirmacao vai contar. So roda sem ciclo, que tornaria a arvore infinita.
  /// </summary>
  private static void ConferirTamanho(Dictionary<int, Receita> receitaDoComponente, List<ErroDoBom> erros)
  {
    var profundidade = new Dictionary<int, int>();
    var nos = new Dictionary<int, long>();

    void Medir(int chave)
    {
      if (profundidade.ContainsKey(chave)) return;
      var maisFundo = 0;
      long total = 1;
      foreach (var f in receitaDoComponente.GetValueOrDefault(chave)?.Itens ?? [])
      {
        Medir(f.Chave);
        maisFundo = Math.Max(maisFundo, profundidade[f.Chave]);
        total = Math.Min(total + nos[f.Chave], PlanejadorDeCopia.NosMaximos + 1L);
      }
      profundidade[chave] = maisFundo + 1;
      nos[chave] = total;
    }

    Medir(0);
    if (profundidade[0] > PlanejadorDeCopia.ProfundidadeMaxima)
      erros.Add(new ErroDoBom(null,
          $"a estrutura passa de {PlanejadorDeCopia.ProfundidadeMaxima} níveis de profundidade."));
    if (nos[0] > PlanejadorDeCopia.NosMaximos)
      erros.Add(new ErroDoBom(null,
          $"a estrutura expandida gera mais de {PlanejadorDeCopia.NosMaximos} itens."));
  }

  /// <summary>
  /// Filhos de uma ocorrencia de pai, na ordem da primeira aparicao; repetidos somam (D12). A posicao de
  /// cada chave fica num dicionario: somar e comparar sao lineares no numero de filhos, e nao
  /// quadraticos — um pai so com dezenas de milhares de filhos cabe nos 5 MiB do arquivo.
  /// </summary>
  private sealed class Receita
  {
    private readonly List<(int Chave, decimal Quantidade)> _itens = [];
    private readonly Dictionary<int, int> _posicao = [];

    public IReadOnlyList<(int Chave, decimal Quantidade)> Itens => _itens;

    public decimal Somar(int chave, decimal quantidade)
    {
      if (!_posicao.TryGetValue(chave, out var i))
      {
        _posicao[chave] = _itens.Count;
        _itens.Add((chave, quantidade));
        return quantidade;
      }
      var soma = _itens[i].Quantidade + quantidade;
      _itens[i] = (chave, soma);
      return soma;
    }

    public bool IgualA(Receita outra) =>
        _itens.Count == outra._itens.Count
        && _itens.All(f => outra._posicao.TryGetValue(f.Chave, out var i) && outra._itens[i].Quantidade == f.Quantidade);
  }
}
