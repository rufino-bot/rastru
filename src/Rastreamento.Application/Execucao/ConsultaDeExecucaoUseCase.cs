using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Separacao;
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
  private readonly ISeparacaoRepository _separacao;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

  public ConsultaDeExecucaoUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, ISetorRepository setores,
      IAgrupamentoRepository agrupamentos, IReceitaPadraoRepository catalogo, ISeparacaoRepository separacao)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _setores = setores;
    _agrupamentos = agrupamentos;
    _separacao = separacao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
  }

  public Task<Result<FilaDoSetorDto>> Fila(int setorId, QuemLe quem, CancellationToken ct) =>
      _execucao.ConsultarAsync(() => FilaAsync(setorId, quem, ct), ct);

  private async Task<Result<FilaDoSetorDto>> FilaAsync(int setorId, QuemLe quem, CancellationToken ct)
  {
    var setor = await _setores.ObterPorIdAsync(setorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<FilaDoSetorDto>();

    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;
    var estornaveis = await EstornaveisAsync(setorId, calc, quem, ct);

    var aIniciar = new List<LinhaDaFilaDto>();
    var emTrabalho = new List<LinhaDaFilaDto>();
    var coleta = new List<LinhaAguardandoColetaDto>();
    var sobra = new List<LinhaDeSobraDto>();
    var conserto = new List<LinhaDeConsertoDto>();

    foreach (var no in calc.Nos)
    {
      // No com filhos inicia pelo card de montagem, consumindo os filhos (spec da Fase 3D, secao 6.1).
      if (!calc.TemFilhos(no.Id) && calc.PrimeiroPasso(no.Id) is PassoDoCalculo primeiro && primeiro.SetorId == setorId
          && calc.Saldo(no.Id, Local.AIniciar) is var aIniciarAqui && aIniciarAqui > 0m)
        aIniciar.Add(new LinhaDaFilaDto(resumos[no.Id], primeiro.Ordem, aIniciarAqui, []));

      foreach (var (local, quantidade) in calc.Saldos(no.Id))
      {
        if (local.SetorId != setorId || quantidade <= 0m) continue;
        if (local.Posicao == PosicaoConst.NoSetor)
          emTrabalho.Add(new LinhaDaFilaDto(resumos[no.Id], local.Ordem!.Value, quantidade,
              estornaveis.EmTrabalho(no.Id, local, quantidade)));
        else if (local.Posicao == PosicaoConst.AguardandoColeta)
        {
          // D8 do plano 2 da Fase 3: a tarefa aqui, a sobra na secao dela — somadas, dao o saldo do
          // passo. Os Terminos por tras da posicao vao para UMA das duas linhas so: a de coleta quando
          // ela existe, a de sobra quando nao ha tarefa (desvio D4 do plano da Fase 3D).
          var ordem = local.Ordem!.Value;
          var tarefa = calc.Tarefa(no.Id, setorId, ordem);
          var registros = estornaveis.DoLocal(no.Id, local, quantidade);
          if (tarefa > 0m)
            coleta.Add(new LinhaAguardandoColetaDto(resumos[no.Id], ordem, tarefa,
                Destino(calc.DestinoDaColeta(no.Id, ordem), nomes), registros));
          var sobraDaColeta = calc.SobraDaColeta(no.Id, setorId, ordem);
          if (sobraDaColeta > 0m)
            sobra.Add(new LinhaDeSobraDto(resumos[no.Id], UltimoPasso, ordem, sobraDaColeta, false,
                tarefa > 0m ? [] : registros));
        }
        else if (local.Posicao is PosicaoConst.EmConserto or PosicaoConst.ConsertoPronto)
          // O conserto da Peca neste Setor (D4 da spec da Fase 5A): `EmConserto` o Operador termina; `ConsertoPronto`
          // espera o Movimentador. `AguardandoConserto` ainda e do Movimentador e fica nas Tarefas.
          conserto.Add(new LinhaDeConsertoDto(resumos[no.Id], local.Posicao, quantidade,
              estornaveis.DoLocal(no.Id, local, quantidade)));
        else if (local.Posicao == PosicaoConst.AguardandoMontagem && calc.ExcessoEmMontagem(no.Id) is var excesso && excesso > 0m)
          sobra.Add(new LinhaDeSobraDto(resumos[no.Id], EmMontagem, null, excesso,
              calc.SetoresOndeAguardaMontagem(no.Id).Count > 1, []));
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
          var primeiro = calc.PrimeiroPasso(paiId);
          return new MontagemPendenteDto(resumos[paiId], m.FaltaMontar, m.DaParaMontar,
              primeiro?.SetorId == setorId,
              primeiro is PassoDoCalculo p ? new SetorResumoDto(p.SetorId, nomes.GetValueOrDefault(p.SetorId, string.Empty)) : null,
              m.Filhos.Select(f => new FilhoNaMontagemDto(
                  resumos[f.FilhoId], f.QuantidadePorPai, f.Presente, f.NecessarioParaProxima, f.FaltaParaProxima)).ToList(),
              calc.RecebeEmConjunto(paiId));
        })
        .ToList();

    // O resumo de material so vai onde o no COMECA neste Setor: a linha de `aIniciar` (so folha) e o
    // cartao do pai que `IniciaAqui` (decisao P4 do plano de backend da Fase 4).
    var resumosDeMaterial = await ResumosDeMaterialAsync(
        aIniciar.Select(l => l.No.Id).Concat(montagem.Where(m => m.IniciaAqui).Select(m => m.Pai.Id)).ToList(), ct);
    aIniciar = aIniciar.Select(l => l with { Material = resumosDeMaterial.GetValueOrDefault(l.No.Id) }).ToList();
    montagem = montagem
        .Select(m => m.IniciaAqui ? m with { Material = resumosDeMaterial.GetValueOrDefault(m.Pai.Id) } : m)
        .ToList();

    return Result<FilaDoSetorDto>.Ok(new FilaDoSetorDto(
        setorId, setor.Nome, setor.Atividade,
        // O que e de Pedido pausado vai para o fim, e a tela o agrupa em "Pausados" (spec da Fase 3D,
        // secao 2.5). OrderBy e estavel: dentro de cada grupo, a ordem de antes.
        aIniciar.OrderBy(l => l.No.Pausa is null ? 0 : 1).ToList(),
        emTrabalho, coleta, montagem,
        sobra.OrderBy(s => s.No.Id).ThenBy(s => s.Ordem ?? int.MaxValue).ToList(),
        conserto));
  }

  /// <summary>
  /// O resumo de material de cada no pedido (`CalculoDaSeparacao.ResumoDaFila`), numa leitura de planejado,
  /// de linhas e de materiais para todos. So entram no mapa os nos que tem material do almoxarifado no
  /// planejado; os outros ficam sem chave.
  /// </summary>
  private async Task<IReadOnlyDictionary<int, ResumoDeMaterialDto>> ResumosDeMaterialAsync(
      IReadOnlyCollection<int> noIds, CancellationToken ct)
  {
    var resumos = new Dictionary<int, ResumoDeMaterialDto>();
    if (noIds.Count == 0) return resumos;

    var planejado = await _separacao.ListarPlanejadoAsync(noIds, ct);
    if (planejado.Count == 0) return resumos;

    var linhas = await _separacao.ListarLinhasAsync(noIds, ct);
    var materiais = await _separacao.ListarMateriaisAsync(planejado.Select(p => p.MaterialId).Distinct().ToList(), ct);
    var doAlmoxarifado = materiais.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();

    foreach (var porNo in CalculoDaSeparacao.Resumir(planejado, linhas, doAlmoxarifado).GroupBy(r => r.EstruturaItemId))
      if (CalculoDaSeparacao.ResumoDaFila(porNo) is ResumoDeMaterialDto resumo)
        resumos[porNo.Key] = resumo;
    return resumos;
  }

  private async Task<Estornaveis> EstornaveisAsync(int setorId, CalculadoraDeExecucao calc, QuemLe quem, CancellationToken ct)
  {
    var registros = await _execucao.ListarRegistrosEstornaveisDoSetorAsync(setorId, calc.Nos.Select(n => n.Id).ToList(), ct);
    var autores = await _execucao.ListarNomesDeUsuariosAsync(
        registros.Movimentos.Select(m => m.UsuarioId).Concat(registros.Montagens.Select(g => g.UsuarioId)).Distinct().ToList(), ct);
    return new Estornaveis(calc, registros, autores, quem);
  }

  /// <summary>
  /// Os registros por tras das linhas da fila que ainda da para estornar (spec da Fase 3D, secao 2.4):
  /// nao estornados (o repositorio ja filtra), com a quantidade cabendo no saldo da posicao — o mesmo
  /// corte de `EstornoUseCase` —, e so os que quem le pode estornar: o autor ve os seus; PCP e
  /// Administrador, todos. Do mais recente ao mais antigo.
  /// </summary>
  private sealed class Estornaveis(
      CalculadoraDeExecucao calc, RegistrosDoSetor registros, IReadOnlyDictionary<int, string> autores, QuemLe quem)
  {
    private bool Visivel(int autorId) => quem.VeRegistrosDeTodos || autorId == quem.UsuarioId;

    public IReadOnlyList<EstornavelDto> DoLocal(int noId, Local local, decimal saldo) =>
        registros.Movimentos
            .Where(m => m.EstruturaItemId == noId && Local.DoDestino(m) == local && m.Quantidade <= saldo && Visivel(m.UsuarioId))
            .OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id)
            .Select(m => new EstornavelDto(m.Tipo, m.Id, m.Quantidade, m.UsuarioId,
                autores.GetValueOrDefault(m.UsuarioId, string.Empty), m.DataHora))
            .ToList();

    /// <summary>
    /// Numa folha, os Inicios daquele passo. Num pai, so no PRIMEIRO passo, as montagens — o inicio
    /// dele e a montagem inteira (spec da Fase 3D, secao 4.2). Nos passos seguintes quem pos a
    /// quantidade ali foi uma Entrega, que se estorna pelo historico do no.
    /// </summary>
    public IReadOnlyList<EstornavelDto> EmTrabalho(int noId, Local local, decimal saldo)
    {
      if (!calc.TemFilhos(noId)) return DoLocal(noId, local, saldo);
      if (calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro || primeiro.Ordem != local.Ordem) return [];
      return registros.Montagens
          .Where(g => g.EstruturaItemId == noId && g.SetorId == local.SetorId && g.Quantidade <= saldo && Visivel(g.UsuarioId))
          .OrderByDescending(g => g.DataHora).ThenByDescending(g => g.Id)
          .Select(g => new EstornavelDto("Montagem", g.Id, g.Quantidade, g.UsuarioId,
              autores.GetValueOrDefault(g.UsuarioId, string.Empty), g.DataHora))
          .ToList();
    }
  }

  public Task<Result<TarefasDto>> Tarefas(CancellationToken ct) =>
      _execucao.ConsultarAsync(() => TarefasAsync(ct), ct);

  private async Task<Result<TarefasDto>> TarefasAsync(CancellationToken ct)
  {
    var (estado, resumos) = await CarregarEmProducaoAsync(ct);
    var nomes = await NomesDosSetoresAsync(ct);
    var calc = estado.Calc;

    var grupos = calc.ColetasPendentes()
        .GroupBy(p => p.SetorId)
        .OrderBy(g => g.Key)
        .Select(g => new TarefasDoSetorDto(g.Key, nomes.GetValueOrDefault(g.Key, string.Empty),
            g.Select(p => new TarefaDto(resumos[p.EstruturaItemId], p.Ordem, p.Tarefa,
                Destino(calc.DestinoDaColeta(p.EstruturaItemId, p.Ordem), nomes))).ToList()))
        .ToList();

    SetorResumoDto Setor(int id) => new(id, nomes.GetValueOrDefault(id, string.Empty));
    var kits = calc.KitsDaColeta().Select(k => new KitDto(
        resumos[k.PaiId], Setor(k.SetorDeDestinoId), k.Conjuntos,
        k.Filhos.Select(f => new FilhoDoKitDto(
            resumos[f.FilhoId], f.QuantidadePorPai,
            f.UltimoPasso is PassoDoCalculo u ? Setor(u.SetorId) : null,
            f.UltimoPasso?.Ordem, f.Pronto, f.JaNoDestino)).ToList())).ToList();

    return Result<TarefasDto>.Ok(new TarefasDto(
        grupos, kits.Where(k => k.Conjuntos >= 1m).ToList(), kits.Where(k => k.Conjuntos < 1m).ToList(),
        calc.ConsertosPendentes().Select(c => TarefaDeConserto(c, resumos[c.EstruturaItemId], nomes)).ToList()));
  }

  /// <summary>
  /// A ida (`AguardandoConserto`: da posicao que espera no local de expedicao a `EmConserto` no Setor) e a volta
  /// (`ConsertoPronto`: do Setor ao local de expedicao) do conserto, com o destino calculado, sem escolha (D4 da
  /// spec da Fase 5A). A `Origem` e sempre a posicao do livro que a entrega consome, para ser reenviada como esta.
  /// </summary>
  private static TarefaDeConsertoDto TarefaDeConserto(
      ConsertoPendente c, NoResumoDto no, IReadOnlyDictionary<int, string> nomes)
  {
    var (origem, destino) = c.Local.Posicao == PosicaoConst.AguardandoConserto
        ? (c.Local, Local.EmConserto(c.Local.SetorId!.Value))
        : (c.Local, Local.NaExpedicao);
    return new TarefaDeConsertoDto(no, LocalDto.De(origem, nomes), LocalDto.De(destino, nomes), c.Quantidade);
  }

  /// <summary>
  /// A mesma conta de `Tarefas` (spec secao 7.7), sem montar os DTOs — ver `CarregarEmProducaoAsync`
  /// para o motivo de nao repetir a carga do estado. Cada Kit montavel conta como uma tarefa; os
  /// incompletos nao contam (spec da Fase 3B, secao 4.5). Cada ida e cada volta do conserto tambem e uma.
  /// </summary>
  public Task<Result<ContagemDeTarefasDto>> ContagemDeTarefas(CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        var (estado, _) = await CarregarEmProducaoAsync(ct);
        return Result<ContagemDeTarefasDto>.Ok(new ContagemDeTarefasDto(
            estado.Calc.ColetasPendentes().Count + estado.Calc.KitsDaColeta().Count(k => k.Montavel)
            + estado.Calc.ConsertosPendentes().Count));
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
        x.PedidoCliente, x.AgrupamentoId, x.AgrupamentoCodigo, x.AgrupamentoTipo, x.No.EstruturaPaiId,
        x.No.EstruturaPaiId is int pai ? estado.Nome(pai) : null, PausaResumoDto.De(x.Pausa),
        x.Materiais.Select(m => new MaterialResumoDto(m.Id, m.Codigo, m.Descricao)).ToList()));
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
            destino.Passo.Value.Ordem, null, false),
        TipoDeDestino.Expedicao => new DestinoDto("Expedicao", null, null, null, null, false),
        _ => new DestinoDto(
            "Montagem", destino.Passo?.SetorId,
            destino.Passo is PassoDoCalculo p ? nomes.GetValueOrDefault(p.SetorId) : null,
            null, destino.PaiId, destino.PaiSemRoteiro),
      };
}
