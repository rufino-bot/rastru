using Rastreamento.Application.Andamento;
using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Application.Retrabalho;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// A leitura do Panorama (C2, C3, C8 e C9 da spec do Panorama): onde estao as unidades de cada Peca de Pedido nao
/// encerrado, na ordem do Roteiro, com prazo, pausa, afericao, miniatura e o "Embalado". Passa por `ConsultarAsync`
/// e le em lote, como a lista da expedicao: os saldos, as afericoes, as ultimas linhas da embalagem, os nomes e as
/// versoes das miniaturas saem de uma consulta cada para todas as Pecas e os descendentes delas, nunca uma por Peca.
/// A idade (C6 da spec da Fase 6A) vem de uma leitura so do livro e das criacoes dos Agrupamentos, pareadas por
/// `CalculoDaIdade`, e as complicacoes (C8), do planejado e do separado lidos em lote e do "a repor" por Peca.
/// </summary>
public sealed class ConsultaDoPanoramaUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly IEmbalagemRepository _embalagem;
  private readonly IArquivoDeComponenteRepository _arquivos;
  private readonly LeitorDeComplicacoes _complicacoes;
  private readonly TimeProvider _relogio;
  private readonly IMemoryCache _cache;
  private readonly LeitorDeEstado _leitor;

  public ConsultaDoPanoramaUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, IEmbalagemRepository embalagem, IArquivoDeComponenteRepository arquivos,
      ISeparacaoRepository separacao, ConsultaDeReposicao reposicao, TimeProvider relogio,
      IMemoryCache cache)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _catalogo = catalogo;
    _embalagem = embalagem;
    _arquivos = arquivos;
    _complicacoes = new LeitorDeComplicacoes(separacao, reposicao);
    _relogio = relogio;
    _cache = cache;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  /// <summary>As Pecas de C2 na ordem de C9; a tela filtra e ordena no cliente.</summary>
  public Task<Result<PanoramaDto>> Lista(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => ListaAsync(ct), ct);

  private async Task<Result<PanoramaDto>> ListaAsync(CancellationToken ct)
  {
    // A lista traz todo no do Pedido aberto, entao os descendentes de C3 vem junto das raizes.
    var todos = await _execucao.ListarNosEmProducaoAsync(ct: ct);
    var agora = _relogio.GetUtcNow().UtcDateTime;
    var raizes = todos.Where(x => x.No.EstruturaPaiId is null).ToList();
    if (raizes.Count == 0) return Result<PanoramaDto>.Ok(new PanoramaDto(agora, []));

    var estado = await _leitor.CarregarAsync(todos.Select(x => x.No).ToList(), ct);
    var abertas = raizes
        .Where(x => estado.Calc.Saldos(x.No.Id)
            .Where(s => s.Local.Posicao is Posicoes.Expedido or Posicoes.Perdido).Sum(s => s.Quantidade) < x.No.Quantidade)
        .ToList();
    if (abertas.Count == 0) return Result<PanoramaDto>.Ok(new PanoramaDto(agora, []));

    var pecaIds = abertas.Select(x => x.No.Id).ToList();
    var nosDaPeca = PartesQueContam.NosDeCadaPeca(todos, pecaIds);
    var idades = await IdadesAsync(todos, nosDaPeca, ct);
    var complicacoes = await ComplicacoesAsync(abertas, nosDaPeca, estado, ct);
    var itensEm = SetoresDosDescendentes(todos, estado, pecaIds);

    var afericoes = (await _expedicao.ListarAfericoesAsync(pecaIds, ct)).ToLookup(a => a.EstruturaItemId);
    var ultimas = (await _embalagem.ListarUltimasAsync(pecaIds, CalculoDaEmbalagem.PosicoesDeProducao, ct))
        .ToDictionary(u => u.EstruturaItemId);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(ultimas.Values.Select(u => u.UsuarioId).Distinct().ToList(), ct);

    // C5: o "a iniciar" de um no com filhos nao tem idade (quem espera sao os filhos), nem em segmento nem no parado.
    DateTime? MaisAntiga(int id, Func<Local, bool> onde) => PartesQueContam.MaisAntiga(estado.Calc, idades, id, onde);
    var distribuicoes = abertas.ToDictionary(
        x => x.No.Id, x => DistribuicaoDaPeca.Montar(estado.Calc.Saldos(x.No.Id), onde => MaisAntiga(x.No.Id, onde)));
    var setorIds = distribuicoes.Values.SelectMany(d => d).Where(s => s.SetorId is not null).Select(s => s.SetorId!.Value)
        .Concat(itensEm.Values.SelectMany(s => s)).Distinct().ToList();
    var setores = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct)).ToDictionary(s => s.Id, s => s.Nome);
    string NomeDoSetor(int id) => setores.GetValueOrDefault(id, string.Empty);

    var componentes = (await _catalogo.ObterComponentesPorIdAsync(
        abertas.Where(x => x.No.ComponenteId is not null).Select(x => x.No.ComponenteId!.Value).Distinct().ToList(), ct))
        .ToDictionary(c => c.Id);
    var versoes = await VersoesDasMiniaturas(componentes.Values, ct);

    var hoje = PrazoDeEntrega.HojeEmBrasilia(_relogio);

    PecaDoPanoramaDto Peca(ContextoDoNo x)
    {
      var id = x.No.Id;
      var saldos = estado.Calc.Saldos(id);
      var componente = componentes.GetValueOrDefault(x.No.ComponenteId ?? 0);
      var ultima = ultimas.GetValueOrDefault(id);
      var embalagem = CalculoDaEmbalagem.Estado(saldos, ultima);
      var valida = CalculoDaExpedicao.AfericaoValida(afericoes[id]);
      return new PecaDoPanoramaDto(
          id, x.AgrupamentoId,
          new PedidoDoPanoramaDto(
              x.PedidoId, x.PedidoNumero, x.PedidoDataEntrega,
              PrazoDeEntrega.EstaAtrasado(x.PedidoDataEntrega, x.PedidoStatus, hoje), x.PedidoOrigemId is not null,
              PausaResumoDto.De(x.Pausa)),
          new ComponenteDoPanoramaDto(
              componente?.Id ?? 0, estado.Codigos.GetValueOrDefault(id) ?? string.Empty, estado.Nome(id),
              componente is null ? null : versoes.GetValueOrDefault(componente.Id)),
          x.No.Quantidade,
          itensEm[id].Select(s => new SetorResumoDto(s, NomeDoSetor(s))).OrderBy(s => s.Nome, StringComparer.Ordinal).ToList(),
          distribuicoes[id]
              .Select(s => new SegmentoDto(
                  s.Tipo, s.SetorId, s.SetorId is int sid ? NomeDoSetor(sid) : null, s.Ordem, s.Quantidade, s.AguardandoColeta,
                  s.Desde, s.AguardandoColetaDesde))
              .ToList(),
          new AfericaoDoPanoramaDto(
              x.No.RequerRelatorioDimensional, CalculoDaExpedicao.Situacao(x.No.RequerRelatorioDimensional, valida).ToString()),
          new EmbalagemDaPecaDto(
              embalagem.Vale, embalagem.PodeMarcar, embalagem.EmProducao,
              ultima is null
                  ? null
                  : new UltimaEmbalagemDto(ultima.Tipo, ultima.DataHora, autores.GetValueOrDefault(ultima.UsuarioId, string.Empty))),
          PartesQueContam.ParadoDesde(nosDaPeca[id], idades, estado.Calc),
          complicacoes.GetValueOrDefault(id) ?? []);
    }

    IReadOnlyList<PecaDoPanoramaDto> pecas = abertas
        .OrderBy(x => x.PedidoDataEntrega)
        .ThenBy(x => x.PedidoNumero, StringComparer.Ordinal)
        .ThenBy(x => estado.Codigos.GetValueOrDefault(x.No.Id) ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(x => x.No.Id)
        .Select(Peca)
        .ToList();
    return Result<PanoramaDto>.Ok(new PanoramaDto(agora, pecas));
  }

  /// <summary>
  /// A idade das partes dos nos das Pecas abertas: o livro desses nos e a criacao dos Agrupamentos, cada um lido numa
  /// consulta so, e pareados por <see cref="CalculoDaIdade"/>. O resultado do pareamento (o custo novo da 6A) fica em
  /// cache (D7): a chave e o maior `Id` do livro mais a lista de `(Id, Quantidade)` dos nos, que e tudo de que a idade
  /// depende (o `Agrupamento.CriadoEm` nao muda), de modo que escrita fora do livro nao invalida nada. O `agora` nao
  /// entra: ele e refeito a cada requisicao, e a idade e a conta dele contra as datas guardadas.
  /// </summary>
  private async Task<IdadesDoLivro> IdadesAsync(
      IReadOnlyList<ContextoDoNo> todos, Dictionary<int, HashSet<int>> nosDaPeca, CancellationToken ct)
  {
    var pedidos = nosDaPeca.Values.SelectMany(n => n).ToHashSet();
    var nos = todos.Where(x => pedidos.Contains(x.No.Id)).ToList();
    var chave = ChaveDasIdades(await _execucao.MaiorIdDoLivroAsync(ct), nos);
    if (_cache.TryGetValue(chave, out IdadesDoLivro? guardadas) && guardadas is not null) return guardadas;

    var criacoes = await _execucao.ListarCriacaoDosAgrupamentosAsync(nos.Select(x => x.AgrupamentoId).Distinct().ToList(), ct);
    var linhas = await _execucao.ListarMovimentacoesAsync(nos.Select(x => x.No.Id).ToList(), ct);
    var idades = CalculoDaIdade.Calcular(
        nos.Select(x => new NoDaIdade(x.No.Id, x.No.Quantidade, criacoes.GetValueOrDefault(x.AgrupamentoId))).ToList(), linhas);
    _cache.Set(chave, idades, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });
    return idades;
  }

  private static string ChaveDasIdades(int maiorIdDoLivro, IReadOnlyList<ContextoDoNo> nos)
  {
    var texto = new System.Text.StringBuilder().Append(maiorIdDoLivro);
    foreach (var x in nos.OrderBy(x => x.No.Id))
      texto.Append('|').Append(x.No.Id).Append(':').Append(x.No.Quantidade.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(texto.ToString()));
    return "panorama-idades:" + Convert.ToHexString(hash);
  }

  /// <summary>
  /// As complicacoes de C8 por Peca (so as que tem alguma entram): o planejado, o separado e os materiais lidos uma vez
  /// para todos os nos, o "a repor" por Peca de uma consulta so, e a conta pura de <see cref="ComplicacoesDaPeca"/>.
  /// </summary>
  private async Task<Dictionary<int, IReadOnlyList<ComplicacaoDto>>> ComplicacoesAsync(
      IReadOnlyList<ContextoDoNo> abertas, Dictionary<int, HashSet<int>> nosDaPeca, EstadoDeExecucao estado, CancellationToken ct)
  {
    var todosOsNos = nosDaPeca.Values.SelectMany(n => n).ToList();
    var raizDoNo = nosDaPeca.SelectMany(kv => kv.Value.Select(n => (No: n, Peca: kv.Key))).ToDictionary(x => x.No, x => x.Peca);

    var dados = await _complicacoes.LerAsync(todosOsNos, abertas.Select(x => x.PedidoId).Distinct().ToList(), ct);
    var resumos = dados.Resumos
        .Where(r => raizDoNo.ContainsKey(r.EstruturaItemId))
        .GroupBy(r => raizDoNo[r.EstruturaItemId])
        .ToDictionary(g => g.Key, g => (IReadOnlyList<ResumoDoMaterial>)g.ToList());

    var porPeca = new Dictionary<int, IReadOnlyList<ComplicacaoDto>>();
    foreach (var (peca, nos) in nosDaPeca)
    {
      var lista = ComplicacoesDaPeca.Calcular(
          nos, estado.Calc, resumos.GetValueOrDefault(peca) ?? [], dados.Descricoes, dados.ARepor.GetValueOrDefault(peca), estado.Nome);
      if (lista.Count > 0) porPeca[peca] = lista;
    }
    return porPeca;
  }

  /// <summary>
  /// "Itens em" (C3, D13): por Peca pedida, os Setores em que algum descendente (qualquer nivel) tem saldo positivo em
  /// `NoSetor`, `AguardandoColeta`, `AguardandoMontagem` ou numa posicao de conserto. A propria Peca nao conta, nem o
  /// Item "a iniciar" (nenhum Setor).
  /// </summary>
  private static Dictionary<int, IReadOnlyList<int>> SetoresDosDescendentes(
      IReadOnlyList<ContextoDoNo> todos, EstadoDeExecucao estado, IReadOnlyCollection<int> pecaIds)
  {
    var pai = todos.ToDictionary(x => x.No.Id, x => x.No.EstruturaPaiId);
    int Raiz(int id)
    {
      while (pai.TryGetValue(id, out var p) && p is int acima) id = acima;
      return id;
    }

    var pedidas = pecaIds.ToHashSet();
    var porPeca = pecaIds.ToDictionary(id => id, _ => new HashSet<int>());
    foreach (var x in todos.Where(x => x.No.EstruturaPaiId is not null))
    {
      var raiz = Raiz(x.No.Id);
      if (!pedidas.Contains(raiz)) continue;
      foreach (var s in estado.Calc.Saldos(x.No.Id))
        if (s.Quantidade > 0m && s.Local.SetorId is int setor && EmSetor(s.Local.Posicao)) porPeca[raiz].Add(setor);
    }
    return porPeca.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value.ToList());
  }

  private static bool EmSetor(string posicao) =>
      posicao is Posicoes.NoSetor or Posicoes.AguardandoColeta or Posicoes.AguardandoMontagem
      || Posicoes.DoConserto.Contains(posicao);

  /// <summary>A versao da miniatura de cada Componente que a tem, por id do Componente, numa consulta de hashes so (a do cadastro).</summary>
  private async Task<IReadOnlyDictionary<int, string>> VersoesDasMiniaturas(
      IReadOnlyCollection<Componente> componentes, CancellationToken ct)
  {
    var comMiniatura = componentes.Where(c => c.ArquivoMiniaturaId is not null).ToList();
    if (comMiniatura.Count == 0) return new Dictionary<int, string>();

    var hashes = await _arquivos.ObterSha256Async(
        comMiniatura.Select(c => c.ArquivoMiniaturaId!.Value).Distinct().ToList(), ct);
    return comMiniatura
        .Where(c => hashes.ContainsKey(c.ArquivoMiniaturaId!.Value))
        .ToDictionary(c => c.Id, c => VersaoDeArquivo.VersaoDe(hashes[c.ArquivoMiniaturaId!.Value]));
  }
}
