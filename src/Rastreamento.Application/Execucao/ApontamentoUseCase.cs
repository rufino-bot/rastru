using Rastreamento.Application.Common;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O que o Operador registra no Setor: iniciar (regra 28) e terminar (regra 22) — spec da Fase 3,
/// secoes 4.1 e 4.2, com a emenda da Fase 3D, secao 4.1: iniciar um no COM filhos consome os filhos
/// presentes no Setor, e e o unico jeito de o pai entrar em producao. Cada escrita: valida a
/// entrada, abre a transacao, trava os nos, le o estado e valida com a calculadora, e so entao grava.
/// A regra de cada um mora num nucleo por item (<see cref="IniciarNoAsync"/>, <see cref="TerminarNoAsync"/>,
/// <see cref="TerminarConsertoNoAsync"/>) com dois pontos de entrada: a rota de um no e o lote (spec dos
/// filtros e do lote, secao 6.2). Terminar o conserto (D4 da spec da Fase 5A) e o terminar da Peca em
/// `EmConserto`, sem Roteiro e sem passo.
/// </summary>
public sealed class ApontamentoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly ISetorRepository _setores;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;
  private readonly FechamentoDoPedido _fechamento;

  public ApontamentoUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, ISetorRepository setores,
      IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _setores = setores;
    // Colaboradores internos, sem estado alem das dependencias que o caso de uso ja recebe — mesmo
    // criterio do `MontadorDeArvoreDeEstrutura` em `MontagemDeEstruturaUseCase`.
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
    _projetor = new ProjetorDoLivro(execucao, catalogo);
    _fechamento = new FechamentoDoPedido(execucao);
  }

  /// <summary>
  /// O maior lote aceito: o mesmo valor de `CadastroDePedidoUseCase.TamanhoDePaginaMaximo`, o teto de
  /// `GET /pedidos` (desvio D4 do plano 2 dos filtros).
  /// </summary>
  public const int TamanhoMaximoDoLote = 100;

  /// <summary>
  /// As recusas do nucleo cuja frase ja nomeia o no marcado: no lote passam iguais as da rota individual.
  /// Toda outra ganha o nome do item na frente (desvio D5 do plano 2 dos filtros). A lista e a das que
  /// NAO levam prefixo, para um codigo novo do nucleo nascer nomeado em vez de anonimo.
  /// </summary>
  private static readonly HashSet<string> RecusasQueJaNomeiamOItem =
  [
    CodigosDaExecucao.SemRoteiro, CodigosDaExecucao.NaoEhOPrimeiroPasso, CodigosDaExecucao.SaldoInsuficiente,
  ];

  public async Task<Result<MovimentacaoDto>> Iniciar(int noId, InicioDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(
        async () => await ProjetarAsync(await IniciarNoAsync(noId, dto.Quantidade, setor, usuarioId, ct), ct), ct);
  }

  public async Task<Result<MovimentacaoDto>> Terminar(int noId, TerminoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    // Setor inativo continua valendo: o que ja esta nele continua andando (spec secao 4.6).
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(
        async () => await ProjetarAsync(await TerminarNoAsync(noId, dto.Ordem, dto.Quantidade, setor, usuarioId, ct), ct), ct);
  }

  /// <summary>
  /// Terminar o conserto de uma Peca: `EmConserto` -> `ConsertoPronto` no mesmo Setor (decisao P6 do plano de backend
  /// da Fase 5A). O Setor inativado continua valendo, como no terminar de sempre: o conserto so e recusado ao ser
  /// MANDADO para Setor inativo. A pausa do Pedido nao recusa (regra 31). A unidade continua em producao, mas o
  /// fechamento e recalculado do mesmo jeito (decisao P4): o status nao muda aqui, e quem o corrige e o recalculo.
  /// </summary>
  public async Task<Result<MovimentacaoDto>> TerminarConserto(int noId, TerminoDeConsertoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([noId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      // So a Peca esta em conserto, entao so ela pode mudar o fechamento: o Pedido dela e travado depois do no e
      // antes da primeira leitura dele (decisao P4).
      var pedidos = nos[0].EstruturaPaiId is null ? await _execucao.TravarPedidosDosNosAsync([noId], ct) : [];
      var r = await TerminarConsertoNoAsync(nos[0], dto.Quantidade, setor, usuarioId, ct);
      if (r.Sucesso) await _fechamento.RecalcularAsync(pedidos, DateTime.UtcNow, ct);
      return await ProjetarAsync(r, ct);
    }, ct);
  }

  /// <summary>
  /// O iniciar aplicado item a item, numa transacao so: ou todos gravam, ou nada (spec dos filtros e do
  /// lote, secao 6.2). Cada item passa pelo MESMO nucleo da rota individual — o lote nao valida saldo,
  /// Roteiro nem filhos por conta propria — e desconta do que os anteriores ja gravaram. Antes do
  /// primeiro item, todas as travas, na ordem da rota individual: todos os nos (itens e filhos), depois
  /// todos os Pedidos (desvio D3 do plano 2 dos filtros).
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> IniciarEmLote(LoteDeInicioDto dto, int usuarioId, CancellationToken ct)
  {
    var itens = dto.Itens ?? [];
    if (RecusaDaEntrada(itens, i => i.Quantidade, i => i.EstruturaItemId,
            i => $"O nó {i.EstruturaItemId} aparece mais de uma vez no lote.") is { } recusa)
      return recusa;
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var ids = itens.Select(i => i.EstruturaItemId).ToList();
      // Leitura comum, sem trava propria: sob a transacao Serializable ela ja impede um filho novo de
      // nascer entre esta leitura e o nucleo, que le os filhos de novo.
      var filhos = new List<int>();
      foreach (var id in ids) filhos.AddRange((await _execucao.ListarFilhosAsync(id, ct)).Select(f => f.Id));
      var travados = await _execucao.TravarNosAsync([.. ids, .. filhos], ct);
      await _execucao.TravarPedidosDosNosAsync(ids, ct);
      var estado = await _leitor.CarregarAsync(travados, ct);   // so para os nomes da recusa

      return await ItemAItemAsync(itens, i => i.EstruturaItemId,
          i => IniciarNoAsync(i.EstruturaItemId, i.Quantidade, setor, usuarioId, ct), estado, ct);
    }, ct);
  }

  /// <summary>
  /// O terminar item a item, tudo ou nada, como <see cref="IniciarEmLote"/>. O mesmo no pode vir em
  /// passos diferentes; a chave do repetido e o par no + passo, e o conserto tem a chave propria (sem passo). Um
  /// item `Conserto` termina `EmConserto` -> `ConsertoPronto` no Setor do lote e vem sem `Ordem`; o normal exige
  /// `Ordem` (decisao P6 do plano de backend da Fase 5A). O terminar normal nao trava Pedido: nao escreve nele,
  /// como a rota individual. O de conserto sim, pelo mesmo criterio da rota individual, e recalcula o fechamento
  /// no fim, uma vez, depois de todos os itens.
  /// </summary>
  public async Task<Result<IReadOnlyList<MovimentacaoDto>>> TerminarEmLote(LoteDeTerminoDto dto, int usuarioId, CancellationToken ct)
  {
    var itens = dto.Itens ?? [];
    if (RecusaDaEntrada(itens, i => i.Quantidade, i => (i.EstruturaItemId, i.Ordem, i.Conserto),
            i => i.Conserto
                ? $"O nó {i.EstruturaItemId} aparece mais de uma vez no lote como conserto."
                : $"O nó {i.EstruturaItemId} no passo {i.Ordem} aparece mais de uma vez no lote.") is { } recusa)
      return recusa;
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<IReadOnlyList<MovimentacaoDto>>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var travados = await _execucao.TravarNosAsync(itens.Select(i => i.EstruturaItemId).Distinct().ToList(), ct);
      // Os Pedidos dos itens de conserto, todos antes de qualquer leitura de Pedido (decisao P4): so a Peca.
      var pecasEmConserto = travados
          .Where(n => n.EstruturaPaiId is null && itens.Any(i => i.Conserto && i.EstruturaItemId == n.Id))
          .Select(n => n.Id).ToList();
      var pedidos = pecasEmConserto.Count > 0 ? await _execucao.TravarPedidosDosNosAsync(pecasEmConserto, ct) : [];
      var estado = await _leitor.CarregarAsync(travados, ct);   // so para os nomes da recusa

      // A forma de todos os itens, antes de gravar o primeiro: o 400 nao passa por um 409 anterior. Item de no
      // inexistente fica para o nucleo, que responde 404.
      foreach (var item in itens)
        if (travados.Any(n => n.Id == item.EstruturaItemId) && FormaInvalida(item) is { } frase)
          return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(
              CodigosDaExecucao.OrigemInvalida, $"{estado.Nome(item.EstruturaItemId)}: {frase}");

      var r = await ItemAItemAsync(itens, i => i.EstruturaItemId,
          i => i.Conserto
              ? TerminarConsertoNoAsync(travados.FirstOrDefault(n => n.Id == i.EstruturaItemId), i.Quantidade, setor, usuarioId, ct)
              // Sem `Ordem` aqui so chega item de no inexistente (a forma dos existentes ja foi conferida): 404.
              : i.Ordem is int ordem
                  ? TerminarNoAsync(i.EstruturaItemId, ordem, i.Quantidade, setor, usuarioId, ct)
                  : Task.FromResult(Falhas.NaoEncontrado<Movimentacao>()),
          estado, ct);
      if (r.Sucesso && pedidos.Count > 0) await _fechamento.RecalcularAsync(pedidos, DateTime.UtcNow, ct);
      return r;
    }, ct);
  }

  /// <summary>A frase da incoerencia entre `Conserto` e `Ordem`, ou nulo quando o item esta bem formado.</summary>
  private static string? FormaInvalida(ItemDeTerminoDto item) =>
      item.Conserto && item.Ordem is not null ? "o conserto não tem passo do Roteiro; tire a ordem deste item."
      : !item.Conserto && item.Ordem is null ? "informe o passo (ordem) em que este item termina."
      : null;

  /// <summary>
  /// As recusas de entrada do lote, todas antes de abrir a transacao, nesta ordem fixa: lista vazia,
  /// teto, quantidade de cada item (a mesma frase da rota individual), item repetido (desvios D4 e D6 do
  /// plano 2 dos filtros). O Setor inexistente vem depois, com o 404.
  /// </summary>
  private static Result<IReadOnlyList<MovimentacaoDto>>? RecusaDaEntrada<TItem, TChave>(
      IReadOnlyList<TItem> itens, Func<TItem, decimal> quantidade, Func<TItem, TChave> chave, Func<TItem, string> repetido)
  {
    if (itens.Count == 0) return Falhas.LoteVazio<IReadOnlyList<MovimentacaoDto>>();
    if (itens.Count > TamanhoMaximoDoLote) return Falhas.LoteGrandeDemais<IReadOnlyList<MovimentacaoDto>>();
    foreach (var item in itens)
      if (!Quantidades.CabeNaColuna(quantidade(item)))
        return Falhas.QuantidadeInvalida<IReadOnlyList<MovimentacaoDto>>(quantidade(item));
    var vistos = new HashSet<TChave>();
    foreach (var item in itens)
      if (!vistos.Add(chave(item)))
        return Falhas.Validacao<IReadOnlyList<MovimentacaoDto>>(CodigosDaExecucao.ItemRepetido, repetido(item));
    return null;
  }

  /// <summary>
  /// Chama o nucleo para cada item, na ordem do corpo, e para na primeira recusa, nomeando o item. A
  /// recusa nao desfaz nada aqui: quem desfaz o que os itens anteriores gravaram e a transacao, que nao
  /// commita um `Result` de falha (desvio D1 do plano 2 dos filtros). A resposta e na ordem dos itens.
  /// </summary>
  private async Task<Result<IReadOnlyList<MovimentacaoDto>>> ItemAItemAsync<TItem>(
      IReadOnlyList<TItem> itens, Func<TItem, int> noDo, Func<TItem, Task<Result<Movimentacao>>> nucleo,
      EstadoDeExecucao estado, CancellationToken ct)
  {
    var movimentos = new List<Movimentacao>(itens.Count);
    foreach (var item in itens)
    {
      var r = await nucleo(item);
      if (!r.Sucesso) return Nomeada(r, estado.Nome(noDo(item)));
      movimentos.Add(r.Valor!);
    }
    return Result<IReadOnlyList<MovimentacaoDto>>.Ok(await _projetor.ProjetarMovimentacoesAsync(movimentos, ct));
  }

  /// <summary>
  /// A recusa do nucleo com o nome do item na frente, salvo as que ja o nomeiam e o 404, que continua sem
  /// corpo, como na rota individual.
  /// </summary>
  private static Result<IReadOnlyList<MovimentacaoDto>> Nomeada(Result<Movimentacao> falha, string nome) =>
      falha.TipoDoErro == TipoDeErro.NaoEncontrado || RecusasQueJaNomeiamOItem.Contains(falha.Erro!)
          ? Falhas.Repassar<IReadOnlyList<MovimentacaoDto>, Movimentacao>(falha)
          : Falhas.Repassar<IReadOnlyList<MovimentacaoDto>, Movimentacao>(falha, $"{nome}: {falha.Detalhe}");

  private async Task<Result<MovimentacaoDto>> ProjetarAsync(Result<Movimentacao> r, CancellationToken ct) =>
      r.Sucesso
          ? Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([r.Valor!], ct)).Single())
          : Falhas.Repassar<MovimentacaoDto, Movimentacao>(r);

  /// <summary>
  /// O iniciar de UM no — a regra inteira, a unica copia dela: a rota individual e o lote passam por
  /// aqui. Roda DENTRO de uma transacao ja aberta; trava o no, os filhos e o Pedido (no lote, ja travados
  /// antes, o que nao custa nada) e so grava depois de validar.
  /// </summary>
  private async Task<Result<Movimentacao>> IniciarNoAsync(int noId, decimal quantidade, Setor setor, int usuarioId, CancellationToken ct)
  {
    var no = await _execucao.TravarNosAsync([noId], ct);
    if (no.Count == 0) return Falhas.NaoEncontrado<Movimentacao>();
    // Os filhos nasceram depois do pai, entao tem Id maior: travar o no e depois eles mantem a
    // ordem crescente de Id da spec da Fase 3, secao 8.1.
    var filhos = await _execucao.ListarFilhosAsync(noId, ct);
    IReadOnlyList<EstruturaItem> travados = filhos.Count == 0
        ? []
        : await _execucao.TravarNosAsync(filhos.Select(f => f.Id), ct);
    // UPDLOCK (nao o ObterPedidoDoNoAsync comum dos outros metodos): este caminho ESCREVE no Pedido
    // a seguir (MarcarPedidoEmProducaoAsync). Ver o XML doc de
    // `ExecucaoRepository.ObterPedidoDoNoParaEscritaAsync` para o deadlock que isto evita.
    var pedido = await _execucao.ObterPedidoDoNoParaEscritaAsync(noId, ct);
    if (Falhas.EstaFechado(pedido)) return Falhas.PedidoFechado<Movimentacao>();
    // A pausa recusa so o que COMECA (spec da Fase 3D, secao 2.5): terminar, entregar e estornar
    // registram algo que ja aconteceu no chao, e recusa-los deixaria o livro mentindo.
    if (pedido!.Pausado)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.PedidoPausado,
          $"O Pedido {pedido.Numero} está pausado.");

    var estado = await _leitor.CarregarAsync([.. no, .. travados], ct);
    var nome = estado.Nome(noId);
    if (estado.Calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.SemRoteiro,
          $"{nome} não tem Roteiro: o PCP precisa definir os passos antes da primeira entrada.");
    if (primeiro.SetorId != setor.Id)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.NaoEhOPrimeiroPasso,
          $"O primeiro passo de {nome} não é no Setor {setor.Nome}.");

    var disponivel = estado.Calc.Saldo(noId, Local.AIniciar);
    if (quantidade > disponivel)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.SaldoInsuficiente,
          $"Só há {Quantidades.Formatar(disponivel)} de {nome} a iniciar.");

    var destino = Local.NoSetor(setor.Id, primeiro.Ordem);
    int? montagemId = null;
    if (travados.Count > 0)
    {
      var baixas = new List<(int FilhoId, decimal Quantidade)>();
      var insuficientes = new List<string>();
      foreach (var filho in travados)
      {
        var necessario = quantidade * (filho.QuantidadePorPai ?? 0m);
        if (!Quantidades.CabeNaColuna(necessario))
          return Falhas.Validacao<Movimentacao>(CodigosDaExecucao.QuantidadeInvalida,
              $"{Quantidades.Formatar(quantidade)} × {Quantidades.Formatar(filho.QuantidadePorPai ?? 0m)} de "
              + $"{estado.Nome(filho.Id)} dá {Quantidades.FormatarExato(necessario)}, que não cabe em quatro casas decimais.");
        var presente = estado.Calc.AguardandoMontagem(filho.Id, setor.Id);
        if (necessario > presente)
          insuficientes.Add($"{estado.Nome(filho.Id)}: {Quantidades.Formatar(presente)} aqui, "
              + $"{Quantidades.Formatar(necessario)} necessários");
        baixas.Add((filho.Id, necessario));
      }
      if (insuficientes.Count > 0)
        return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.FilhosInsuficientes, string.Join("; ", insuficientes) + ".");

      var montagem = new Montagem
      {
        EstruturaItemId = noId, SetorId = setor.Id, Quantidade = quantidade,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      };
      _execucao.Adicionar(montagem);
      await _execucao.SalvarAlteracoesAsync(ct);   // a baixa e o Inicio precisam do Id da Montagem
      montagemId = montagem.Id;

      foreach (var (filhoId, quantidadeDoFilho) in baixas)
        _execucao.Adicionar(NovoMovimento.De(filhoId, TiposDeMovimentacao.Montagem, quantidadeDoFilho,
            Local.AguardandoMontagem(setor.Id), Local.Montado, usuarioId, montagemId: montagemId));
    }

    var inicio = NovoMovimento.De(noId, TiposDeMovimentacao.Inicio, quantidade,
        Local.AIniciar, destino, usuarioId, montagemId: montagemId);
    _execucao.Adicionar(inicio);
    // Regra 28: o primeiro Inicio de qualquer no poe o Pedido em producao, e o status nao volta.
    await _execucao.MarcarPedidoEmProducaoAsync(pedido!.PedidoId, ct);
    await _execucao.SalvarAlteracoesAsync(ct);
    return Result<Movimentacao>.Ok(inicio);
  }

  /// <summary>O terminar de UM no, a unica copia da regra. Ver <see cref="IniciarNoAsync"/>.</summary>
  private async Task<Result<Movimentacao>> TerminarNoAsync(int noId, int ordem, decimal quantidade, Setor setor, int usuarioId, CancellationToken ct)
  {
    var nos = await _execucao.TravarNosAsync([noId], ct);
    if (nos.Count == 0) return Falhas.NaoEncontrado<Movimentacao>();
    if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(noId, ct))) return Falhas.PedidoFechado<Movimentacao>();

    var estado = await _leitor.CarregarAsync(nos, ct);
    var origem = Local.NoSetor(setor.Id, ordem);
    var disponivel = estado.Calc.Saldo(noId, origem);
    if (quantidade > disponivel)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.SaldoInsuficiente,
          $"Só há {Quantidades.Formatar(disponivel)} de {estado.Nome(noId)} no Setor {setor.Nome} (passo {ordem}).");

    var movimento = NovoMovimento.De(noId, TiposDeMovimentacao.Termino, quantidade,
        origem, Local.AguardandoColeta(setor.Id, ordem), usuarioId);
    _execucao.Adicionar(movimento);
    await _execucao.SalvarAlteracoesAsync(ct);
    return Result<Movimentacao>.Ok(movimento);
  }

  /// <summary>
  /// O terminar do conserto de UM no, a unica copia da regra: a rota individual e o lote passam por aqui, com o no
  /// ja travado (e, se Peca, o Pedido dele) por quem chama. Nao recalcula o fechamento: quem chama o faz uma vez.
  /// </summary>
  private async Task<Result<Movimentacao>> TerminarConsertoNoAsync(
      EstruturaItem? no, decimal quantidade, Setor setor, int usuarioId, CancellationToken ct)
  {
    if (no is null) return Falhas.NaoEncontrado<Movimentacao>();
    if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(no.Id, ct))) return Falhas.PedidoFechado<Movimentacao>();

    var estado = await _leitor.CarregarAsync([no], ct);
    var origem = Local.EmConserto(setor.Id);
    var disponivel = estado.Calc.Saldo(no.Id, origem);
    if (quantidade > disponivel)
      return Falhas.Conflito<Movimentacao>(CodigosDaExecucao.SaldoInsuficiente,
          $"Só há {Quantidades.Formatar(Math.Max(0m, disponivel))} de {estado.Nome(no.Id)} em conserto no Setor {setor.Nome}.");

    var movimento = NovoMovimento.De(no.Id, TiposDeMovimentacao.Termino, quantidade,
        origem, Local.ConsertoPronto(setor.Id), usuarioId);
    _execucao.Adicionar(movimento);
    await _execucao.SalvarAlteracoesAsync(ct);
    return Result<Movimentacao>.Ok(movimento);
  }
}
