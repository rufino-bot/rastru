using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using PosicaoConst = Rastreamento.Domain.Entities.Posicoes;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// As leituras da Fase 3 (spec secoes 5.2, 6 e 7), todas pela mesma calculadora que as escritas usam.
/// Fila e tarefas leem os nos de TODO Pedido que nao esta `Concluido` nem `Cancelado` — uma consulta
/// de nos e uma de saldo, sem paginacao nem cache (secao 7.7: na escala de uma fabrica, cabe).
/// Todo metodo publico passa por `ConsultarAsync` (`IExecucaoRepository.LerAsync`): um deadlock em que
/// a leitura e a vitima tenta de novo, e o esgotamento vira 409 `ConflitoDeConcorrencia` em vez de uma
/// `SqlException` crua (fix round 4 da Task 11).
/// </summary>
public sealed class ConsultaDeExecucaoUseCase
{
  private const string UltimoPasso = "UltimoPasso";
  private const string EmMontagem = "Montagem";

  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly ISetorRepository _setores;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public ConsultaDeExecucaoUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, ISetorRepository setores,
      IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _setores = setores;
    _agrupamentos = agrupamentos;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public Task<Result<FilaDoSetorDto>> Fila(int setorId, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => FilaAsync(setorId, ct), ct);

  private async Task<Result<FilaDoSetorDto>> FilaAsync(int setorId, CancellationToken ct)
  {
    var setor = await _setores.ObterPorIdAsync(setorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<FilaDoSetorDto>();

    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;

    var aIniciar = new List<LinhaDaFilaDto>();
    var emTrabalho = new List<LinhaDaFilaDto>();
    var coleta = new List<LinhaAguardandoColetaDto>();
    var sobra = new List<LinhaDeSobraDto>();

    foreach (var no in calc.Nos)
    {
      if (calc.PrimeiroPasso(no.Id) is PassoDoCalculo primeiro && primeiro.SetorId == setorId
          && calc.Saldo(no.Id, Local.AIniciar) is var aIniciarAqui && aIniciarAqui > 0m)
        aIniciar.Add(new LinhaDaFilaDto(resumos[no.Id], primeiro.Ordem, aIniciarAqui));

      foreach (var (local, quantidade) in calc.Saldos(no.Id))
      {
        if (local.SetorId != setorId || quantidade <= 0m) continue;
        if (local.Posicao == PosicaoConst.NoSetor)
          emTrabalho.Add(new LinhaDaFilaDto(resumos[no.Id], local.Ordem!.Value, quantidade));
        else if (local.Posicao == PosicaoConst.AguardandoColeta)
        {
          // D8 do plano 2: a tarefa aqui, a sobra na secao dela — somadas, dao o saldo do passo.
          var ordem = local.Ordem!.Value;
          var tarefa = calc.Tarefa(no.Id, setorId, ordem);
          if (tarefa > 0m)
            coleta.Add(new LinhaAguardandoColetaDto(resumos[no.Id], ordem, tarefa,
                Destino(calc.DestinoDaColeta(no.Id, ordem), nomes)));
          var sobraDaColeta = calc.SobraDaColeta(no.Id, setorId, ordem);
          if (sobraDaColeta > 0m)
            sobra.Add(new LinhaDeSobraDto(resumos[no.Id], UltimoPasso, ordem, sobraDaColeta, false));
        }
        else if (local.Posicao == PosicaoConst.AguardandoMontagem && calc.ExcessoEmMontagem(no.Id) is var excesso && excesso > 0m)
          sobra.Add(new LinhaDeSobraDto(resumos[no.Id], EmMontagem, null, excesso,
              calc.SetoresOndeAguardaMontagem(no.Id).Count > 1));
      }
    }

    var montagem = calc.Nos
        .Where(n => n.PaiId is not null && calc.AguardandoMontagem(n.Id, setorId) > 0m)
        .Select(n => n.PaiId!.Value)
        .Distinct()
        .Order()
        .Select(paiId =>
        {
          var m = calc.CalcularMontabilidade(paiId, setorId);
          return new MontagemPendenteDto(resumos[paiId], m.FaltaMontar, m.DaParaMontar,
              m.Filhos.Select(f => new FilhoNaMontagemDto(
                  resumos[f.FilhoId], f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToList());
        })
        .ToList();

    return Result<FilaDoSetorDto>.Ok(new FilaDoSetorDto(
        setorId, setor.Nome, aIniciar, emTrabalho, coleta, montagem,
        sobra.OrderBy(s => s.No.Id).ThenBy(s => s.Ordem ?? int.MaxValue).ToList()));
  }

  public Task<Result<IReadOnlyList<TarefasDoSetorDto>>> Tarefas(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => TarefasAsync(ct), ct);

  private async Task<Result<IReadOnlyList<TarefasDoSetorDto>>> TarefasAsync(CancellationToken ct)
  {
    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);

    return Result<IReadOnlyList<TarefasDoSetorDto>>.Ok(estado.Calc.ColetasPendentes()
        .GroupBy(p => p.SetorId)
        .OrderBy(g => g.Key)
        .Select(g => new TarefasDoSetorDto(g.Key, nomes.GetValueOrDefault(g.Key, string.Empty),
            g.Select(p => new TarefaDto(resumos[p.EstruturaItemId], p.Ordem, p.Tarefa,
                Destino(estado.Calc.DestinoDaColeta(p.EstruturaItemId, p.Ordem), nomes))).ToList()))
        .ToList());
  }

  /// <summary>
  /// A mesma conta de `Tarefas` (spec secao 7.7), sem montar os DTOs — ver `CarregarEmProducaoAsync`
  /// para o motivo de nao repetir a carga do estado.
  /// </summary>
  public Task<Result<ContagemDeTarefasDto>> ContagemDeTarefas(CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        var (estado, _) = await CarregarEmProducaoAsync(ct);
        return Result<ContagemDeTarefasDto>.Ok(new ContagemDeTarefasDto(estado.Calc.ColetasPendentes().Count));
      }, ct);

  public Task<Result<IReadOnlyList<PosicoesDoNoDto>>> Posicoes(int agrupamentoId, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => PosicoesAsync(agrupamentoId, ct), ct);

  private async Task<Result<IReadOnlyList<PosicoesDoNoDto>>> PosicoesAsync(int agrupamentoId, CancellationToken ct)
  {
    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Falhas.NaoEncontrado<IReadOnlyList<PosicoesDoNoDto>>();

    var nos = await _estruturas.ListarDoAgrupamentoAsync(agrupamentoId, ct);
    var estado = await _leitor.CarregarAsync(nos, ct);
    var nomes = await NomesDosSetoresAsync(ct);

    return Result<IReadOnlyList<PosicoesDoNoDto>>.Ok(nos.OrderBy(n => n.Id).Select(n => new PosicoesDoNoDto(
        n.Id,
        estado.Calc.Saldos(n.Id).Select(s => new SaldoDto(
            s.Local.Posicao, s.Local.SetorId,
            s.Local.SetorId is int setorId ? nomes.GetValueOrDefault(setorId) : null,
            s.Local.Ordem, s.Quantidade)).ToList(),
        estado.Calc.TemFilhos(n.Id) ? (decimal?)estado.Calc.TotalMontado(n.Id) : null)).ToList());
  }

  public Task<Result<LivroDoNoDto>> LivroDoNo(int noId, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => LivroDoNoAsync(noId, ct), ct);

  private async Task<Result<LivroDoNoDto>> LivroDoNoAsync(int noId, CancellationToken ct)
  {
    if ((await _execucao.ListarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<LivroDoNoDto>();

    var movimentos = await _execucao.ListarMovimentacoesDoNoAsync(noId, ct);
    var montagens = await _execucao.ListarMontagensDoNoAsync(noId, ct);
    return Result<LivroDoNoDto>.Ok(new LivroDoNoDto(
        await _projetor.ProjetarMovimentacoesAsync(movimentos, ct),
        await _projetor.ProjetarMontagensAsync(montagens, ct)));
  }

  /// <summary>
  /// Os nos de todo Pedido em producao, a calculadora sobre eles e o `NoResumoDto` de cada um. Unica
  /// carga do estado em producao — `Fila`, `Tarefas` e `ContagemDeTarefas` passam por aqui, para nao
  /// repetir as duas linhas de carga entre elas. `ContagemDeTarefas` descarta os resumos, que ela nao
  /// precisa.
  /// </summary>
  private async Task<(EstadoDeExecucao Estado, IReadOnlyDictionary<int, NoResumoDto> Resumos)> CarregarEmProducaoAsync(
      CancellationToken ct)
  {
    var contexto = await _execucao.ListarNosEmProducaoAsync(ct);
    var estado = await _leitor.CarregarAsync(contexto.Select(x => x.No).ToList(), ct);
    IReadOnlyDictionary<int, NoResumoDto> resumos = contexto.ToDictionary(x => x.No.Id, x => new NoResumoDto(
        x.No.Id, estado.Nome(x.No.Id), estado.Codigos.GetValueOrDefault(x.No.Id), x.PedidoId, x.PedidoNumero,
        x.AgrupamentoId, x.AgrupamentoCodigo, x.No.EstruturaPaiId,
        x.No.EstruturaPaiId is int pai ? estado.Nome(pai) : null));
    return (estado, resumos);
  }

  /// <summary>O catalogo de Setores inteiro, inativos inclusive: o que ja esta num Setor inativado continua aparecendo.</summary>
  private async Task<IReadOnlyDictionary<int, string>> NomesDosSetoresAsync(CancellationToken ct) =>
      (await _setores.ListarAsync(incluirInativos: true, ct)).ToDictionary(s => s.Id, s => s.Nome);

  private static DestinoDto Destino(DestinoCalculado destino, IReadOnlyDictionary<int, string> nomes) =>
      destino.Tipo switch
      {
        TipoDeDestino.ProximoPasso => new DestinoDto(
            "ProximoPasso", destino.Passo!.Value.SetorId, nomes.GetValueOrDefault(destino.Passo.Value.SetorId),
            destino.Passo.Value.Ordem, null, null, Array.Empty<SetorResumoDto>(), false),
        TipoDeDestino.Expedicao => new DestinoDto(
            "Expedicao", null, null, null, null, null, Array.Empty<SetorResumoDto>(), false),
        _ => new DestinoDto(
            "Montagem", null, null, null, destino.PaiId, destino.SugestaoSetorId,
            destino.SetoresPossiveis.Select(s => new SetorResumoDto(s, nomes.GetValueOrDefault(s, string.Empty))).ToList(),
            destino.PaiSemRoteiro),
      };
}
