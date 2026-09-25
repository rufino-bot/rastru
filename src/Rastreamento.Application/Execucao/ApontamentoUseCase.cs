using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O que o Operador registra no Setor: iniciar (regra 28), terminar (regra 22) e montar (regra 24) —
/// spec da Fase 3, secoes 4.1, 4.2 e 4.4. Cada escrita: valida a entrada, abre a transacao, trava os
/// nos, le o estado e valida com a calculadora, e so entao grava.
/// </summary>
public sealed class ApontamentoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly ISetorRepository _setores;
  private readonly LeitorDeEstado _leitor;
  private readonly ProjetorDoLivro _projetor;

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
  }

  public async Task<Result<MovimentacaoDto>> Iniciar(int noId, InicioDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([noId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      var pedido = await _execucao.ObterPedidoDoNoAsync(noId, ct);
      if (Falhas.EstaFechado(pedido)) return Falhas.PedidoFechado<MovimentacaoDto>();

      var estado = await _leitor.CarregarAsync(nos, ct);
      var nome = estado.Nome(noId);
      if (estado.Calc.PrimeiroPasso(noId) is not PassoDoCalculo primeiro)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SemRoteiro,
            $"{nome} não tem Roteiro: o PCP precisa definir os passos antes da primeira entrada.");
      if (primeiro.SetorId != dto.SetorId)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.NaoEhOPrimeiroPasso,
            $"O primeiro passo de {nome} não é no {setor.Nome}.");

      var disponivel = estado.Calc.Saldo(noId, Local.AIniciar);
      if (dto.Quantidade > disponivel)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(disponivel)} de {nome} a iniciar.");

      var movimento = NovoMovimento.De(noId, TiposDeMovimentacao.Inicio, dto.Quantidade,
          Local.AIniciar, Local.NoSetor(dto.SetorId, primeiro.Ordem), usuarioId);
      _execucao.Adicionar(movimento);
      // Regra 28: o primeiro Inicio de qualquer no poe o Pedido em producao, e o status nao volta.
      await _execucao.MarcarPedidoEmProducaoAsync(pedido!.PedidoId, ct);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([movimento], ct)).Single());
    }, ct);
  }

  public async Task<Result<MovimentacaoDto>> Terminar(int noId, TerminoDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MovimentacaoDto>(dto.Quantidade);
    // Setor inativo continua valendo: o que ja esta nele continua andando (spec secao 4.6).
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MovimentacaoDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var nos = await _execucao.TravarNosAsync([noId], ct);
      if (nos.Count == 0) return Falhas.NaoEncontrado<MovimentacaoDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(noId, ct))) return Falhas.PedidoFechado<MovimentacaoDto>();

      var estado = await _leitor.CarregarAsync(nos, ct);
      var origem = Local.NoSetor(dto.SetorId, dto.Ordem);
      var disponivel = estado.Calc.Saldo(noId, origem);
      if (dto.Quantidade > disponivel)
        return Falhas.Conflito<MovimentacaoDto>(CodigosDaExecucao.SaldoInsuficiente,
            $"Só há {Quantidades.Formatar(disponivel)} de {estado.Nome(noId)} no {setor.Nome} (passo {dto.Ordem}).");

      var movimento = NovoMovimento.De(noId, TiposDeMovimentacao.Termino, dto.Quantidade,
          origem, Local.AguardandoColeta(dto.SetorId, dto.Ordem), usuarioId);
      _execucao.Adicionar(movimento);
      await _execucao.SalvarAlteracoesAsync(ct);
      return Result<MovimentacaoDto>.Ok((await _projetor.ProjetarMovimentacoesAsync([movimento], ct)).Single());
    }, ct);
  }

  public async Task<Result<MontagemDto>> Montar(int paiId, MontagemNovaDto dto, int usuarioId, CancellationToken ct)
  {
    if (!Quantidades.CabeNaColuna(dto.Quantidade)) return Falhas.QuantidadeInvalida<MontagemDto>(dto.Quantidade);
    var setor = await _setores.ObterPorIdAsync(dto.SetorId, ct);
    if (setor is null) return Falhas.NaoEncontrado<MontagemDto>();

    return await _execucao.ExecutarAsync(async () =>
    {
      var pai = await _execucao.TravarNosAsync([paiId], ct);
      if (pai.Count == 0) return Falhas.NaoEncontrado<MontagemDto>();
      if (Falhas.EstaFechado(await _execucao.ObterPedidoDoNoAsync(paiId, ct))) return Falhas.PedidoFechado<MontagemDto>();

      var filhos = await _execucao.ListarFilhosAsync(paiId, ct);
      if (filhos.Count == 0)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.SemFilhos, "Este item não tem filhos para montar.");
      // Os filhos nasceram depois do pai, entao tem Id maior: travar o pai e depois eles mantem a
      // ordem crescente de Id que a spec secao 8.1 pede.
      var travados = await _execucao.TravarNosAsync(filhos.Select(f => f.Id), ct);

      var estado = await _leitor.CarregarAsync([.. pai, .. travados], ct);
      var falta = estado.Calc.FaltaMontar(paiId);
      if (dto.Quantidade > falta)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.MontagemAcimaDoQueFalta,
            $"Falta montar {Quantidades.Formatar(Math.Max(0m, falta))} de {estado.Nome(paiId)}.");

      var baixas = new List<(int FilhoId, decimal Quantidade)>();
      var insuficientes = new List<string>();
      foreach (var filho in travados)
      {
        var necessario = dto.Quantidade * (filho.QuantidadePorPai ?? 0m);
        if (!Quantidades.CabeNaColuna(necessario))
          return Falhas.Validacao<MontagemDto>(CodigosDaExecucao.QuantidadeInvalida,
              $"{Quantidades.Formatar(dto.Quantidade)} × {Quantidades.Formatar(filho.QuantidadePorPai ?? 0m)} de "
              + $"{estado.Nome(filho.Id)} dá {necessario}, que não cabe em quatro casas decimais.");
        var presente = estado.Calc.AguardandoMontagem(filho.Id, dto.SetorId);
        if (necessario > presente)
          insuficientes.Add($"{estado.Nome(filho.Id)}: {Quantidades.Formatar(presente)} aqui, "
              + $"{Quantidades.Formatar(necessario)} necessários");
        baixas.Add((filho.Id, necessario));
      }
      if (insuficientes.Count > 0)
        return Falhas.Conflito<MontagemDto>(CodigosDaExecucao.FilhosInsuficientes, string.Join("; ", insuficientes) + ".");

      var montagem = new Montagem
      {
        EstruturaItemId = paiId, SetorId = dto.SetorId, Quantidade = dto.Quantidade,
        DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
      };
      _execucao.Adicionar(montagem);
      await _execucao.SalvarAlteracoesAsync(ct);   // a baixa precisa do Id da Montagem

      foreach (var (filhoId, quantidade) in baixas)
        _execucao.Adicionar(NovoMovimento.De(filhoId, TiposDeMovimentacao.Montagem, quantidade,
            Local.AguardandoMontagem(dto.SetorId), Local.Montado, usuarioId, montagemId: montagem.Id));
      await _execucao.SalvarAlteracoesAsync(ct);

      return Result<MontagemDto>.Ok((await _projetor.ProjetarMontagensAsync([montagem], ct)).Single());
    }, ct);
  }
}
