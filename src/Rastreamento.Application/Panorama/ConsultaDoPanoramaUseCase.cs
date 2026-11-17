using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// A leitura do Panorama (C2, C3, C8 e C9 da spec do Panorama): onde estao as unidades de cada Peca de Pedido nao
/// encerrado, na ordem do Roteiro, com prazo, pausa, afericao, miniatura e o "Embalado". Passa por `ConsultarAsync`
/// e le em lote, como a lista da expedicao: os saldos, as afericoes, as ultimas linhas da embalagem, os nomes e as
/// versoes das miniaturas saem de uma consulta cada para todas as Pecas e os descendentes delas, nunca uma por Peca.
/// </summary>
public sealed class ConsultaDoPanoramaUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IExpedicaoRepository _expedicao;
  private readonly IReceitaPadraoRepository _catalogo;
  private readonly IEmbalagemRepository _embalagem;
  private readonly IArquivoDeComponenteRepository _arquivos;
  private readonly TimeProvider _relogio;
  private readonly LeitorDeEstado _leitor;

  public ConsultaDoPanoramaUseCase(
      IExecucaoRepository execucao, IExpedicaoRepository expedicao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo, IEmbalagemRepository embalagem, IArquivoDeComponenteRepository arquivos,
      TimeProvider relogio)
  {
    _execucao = execucao;
    _expedicao = expedicao;
    _catalogo = catalogo;
    _embalagem = embalagem;
    _arquivos = arquivos;
    _relogio = relogio;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  /// <summary>As Pecas de C2 na ordem de C9; a tela filtra e ordena no cliente.</summary>
  public Task<Result<PanoramaDto>> Lista(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => ListaAsync(ct), ct);

  private async Task<Result<PanoramaDto>> ListaAsync(CancellationToken ct)
  {
    // A lista traz todo no do Pedido aberto, entao os descendentes de C3 vem junto das raizes.
    var todos = await _execucao.ListarNosEmProducaoAsync(ct: ct);
    var raizes = todos.Where(x => x.No.EstruturaPaiId is null).ToList();
    if (raizes.Count == 0) return Result<PanoramaDto>.Ok(new PanoramaDto([]));

    var estado = await _leitor.CarregarAsync(todos.Select(x => x.No).ToList(), ct);
    var abertas = raizes
        .Where(x => estado.Calc.Saldos(x.No.Id)
            .Where(s => s.Local.Posicao is Posicoes.Expedido or Posicoes.Perdido).Sum(s => s.Quantidade) < x.No.Quantidade)
        .ToList();
    if (abertas.Count == 0) return Result<PanoramaDto>.Ok(new PanoramaDto([]));

    var pecaIds = abertas.Select(x => x.No.Id).ToList();
    var itensEm = SetoresDosDescendentes(todos, estado, pecaIds);

    var afericoes = (await _expedicao.ListarAfericoesAsync(pecaIds, ct)).ToLookup(a => a.EstruturaItemId);
    var ultimas = (await _embalagem.ListarUltimasAsync(pecaIds, CalculoDaEmbalagem.PosicoesDeProducao, ct))
        .ToDictionary(u => u.EstruturaItemId);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(ultimas.Values.Select(u => u.UsuarioId).Distinct().ToList(), ct);

    var distribuicoes = abertas.ToDictionary(x => x.No.Id, x => DistribuicaoDaPeca.Montar(estado.Calc.Saldos(x.No.Id)));
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
                  s.Tipo, s.SetorId, s.SetorId is int sid ? NomeDoSetor(sid) : null, s.Ordem, s.Quantidade, s.AguardandoColeta))
              .ToList(),
          new AfericaoDoPanoramaDto(
              x.No.RequerRelatorioDimensional, CalculoDaExpedicao.Situacao(x.No.RequerRelatorioDimensional, valida).ToString()),
          new EmbalagemDaPecaDto(
              embalagem.Vale, embalagem.PodeMarcar, embalagem.EmProducao,
              ultima is null
                  ? null
                  : new UltimaEmbalagemDto(ultima.Tipo, ultima.DataHora, autores.GetValueOrDefault(ultima.UsuarioId, string.Empty))));
    }

    IReadOnlyList<PecaDoPanoramaDto> pecas = abertas
        .OrderBy(x => x.PedidoDataEntrega)
        .ThenBy(x => x.PedidoNumero, StringComparer.Ordinal)
        .ThenBy(x => estado.Codigos.GetValueOrDefault(x.No.Id) ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(x => x.No.Id)
        .Select(Peca)
        .ToList();
    return Result<PanoramaDto>.Ok(new PanoramaDto(pecas));
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
