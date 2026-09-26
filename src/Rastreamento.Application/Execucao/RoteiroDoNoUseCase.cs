using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// O Roteiro de UM no, editavel pelo PCP (spec da Fase 3, secao 4.6; regra 7). Um passo e ALCANCADO
/// quando sua `Ordem` aparece no livro do no, como origem ou destino — inclusive de um movimento
/// estornado: o livro aponta para ele, entao ele e historico. Tudo ate o ultimo passo alcancado fica
/// como esta; o resto se troca livremente, e os passos novos ganham `Ordem` depois do ultimo travado.
/// Setor inativo nao entra no trecho novo; o que ja esta no trecho travado continua (o que esta nele
/// continua andando). Nao ha `PedidoFechado` aqui: a spec o reserva para movimentar.
///
/// `Passos` ausente/nulo no corpo (`dto.Passos is null`) e 400 `RoteiroInvalido`. Uma lista VAZIA
/// explicita ([]) e ACEITA, mas nao tem tratamento especial: ela cai na MESMA regra de prefixo travado
/// de qualquer outra lista — com algum passo ja alcancado, [] omite esse prefixo e da o mesmo 409
/// `PassoJaAlcancado` de uma lista truncada qualquer (o corpo tem de REENVIAR os passos ja alcancados
/// para so trocar o que vem depois deles). So quando NADA foi alcancado ainda (`travados == 0`) e que
/// [] de fato limpa o Roteiro inteiro, porque nesse caso nao ha prefixo pra reenviar. (Correcao: a
/// redacao anterior deste doc e da mensagem de erro de `Substituir` para `Passos` nulo afirmava que
/// [] "limpa o que ainda nao foi alcancado" sem essa condicao, o que e falso quando ha passo
/// alcancado.)
/// </summary>
public sealed class RoteiroDoNoUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;

  public RoteiroDoNoUseCase(IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _execucao = execucao;
    _estruturas = estruturas;
    _catalogo = catalogo;
  }

  /// <summary>Leitura com o retry de deadlock e o 409 das escritas (`ConsultarAsync`; fix round 4 da Task 11).</summary>
  public Task<Result<RoteiroDoNoDto>> Obter(int noId, CancellationToken ct) =>
      _execucao.ConsultarAsync(async () =>
      {
        if ((await _execucao.ListarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<RoteiroDoNoDto>();
        return Result<RoteiroDoNoDto>.Ok(await ProjetarAsync(noId, ct));
      }, ct);

  public async Task<Result<RoteiroDoNoDto>> Substituir(int noId, RoteiroNovoDto dto, CancellationToken ct)
  {
    if (dto.Passos is null)
      return Falhas.Validacao<RoteiroDoNoDto>(CodigosDaExecucao.RoteiroInvalido,
          "Informe a lista de passos do Roteiro — reenvie os que já foram alcançados; sem mais nada "
          + "depois deles, os que faltam são removidos.");
    var passos = dto.Passos;

    return await _execucao.ExecutarAsync(async () =>
    {
      if ((await _execucao.TravarNosAsync([noId], ct)).Count == 0) return Falhas.NaoEncontrado<RoteiroDoNoDto>();

      var atual = (await _estruturas.ListarRoteiroAsync([noId], ct)).OrderBy(r => r.Ordem).ToList();
      var alcancados = (await _execucao.ListarPassosAlcancadosAsync([noId], ct)).Select(p => p.Ordem).ToHashSet();
      var travados = atual.FindLastIndex(r => alcancados.Contains(r.Ordem)) + 1;

      if (travados > 0)
      {
        var nomes = await NomesAsync(atual.Take(travados).Select(r => r.SetorId), ct);
        for (var i = 0; i < travados; i++)
          if (i >= passos.Count || passos[i] != atual[i].SetorId)
            return Falhas.Conflito<RoteiroDoNoDto>(CodigosDaExecucao.PassoJaAlcancado,
                $"O passo {atual[i].Ordem} ({nomes.GetValueOrDefault(atual[i].SetorId, "?")}) já foi alcançado e não muda; "
                + "os passos novos vêm depois dele.");
      }

      var novos = passos.Skip(travados).ToList();
      var setores = (await _catalogo.ObterSetoresPorIdAsync(novos.Distinct().ToList(), ct)).ToDictionary(s => s.Id);
      foreach (var setorId in novos)
        if (!setores.TryGetValue(setorId, out var setor) || !setor.Ativo)
          return Falhas.Validacao<RoteiroDoNoDto>(CodigosDaExecucao.RoteiroInvalido,
              setor is null
                  ? $"O Setor {setorId} não existe."
                  : $"O Setor {setor.Nome} está inativo e não entra num Roteiro.");

      int? ultimaTravada = travados == 0 ? null : atual[travados - 1].Ordem;
      var primeiraNova = (ultimaTravada ?? 0) + 1;
      await _execucao.SubstituirPassosNaoAlcancadosAsync(
          noId, ultimaTravada, novos.Select((setorId, i) => (setorId, primeiraNova + i)).ToList(), ct);

      return Result<RoteiroDoNoDto>.Ok(await ProjetarAsync(noId, ct));
    }, ct);
  }

  private async Task<RoteiroDoNoDto> ProjetarAsync(int noId, CancellationToken ct)
  {
    var passos = (await _estruturas.ListarRoteiroAsync([noId], ct)).OrderBy(p => p.Ordem).ToList();
    var alcancados = (await _execucao.ListarPassosAlcancadosAsync([noId], ct)).Select(p => p.Ordem).ToHashSet();
    var nomes = await NomesAsync(passos.Select(p => p.SetorId), ct);
    return new RoteiroDoNoDto(noId, passos
        .Select(p => new PassoDoRoteiroDoNoDto(p.SetorId, nomes.GetValueOrDefault(p.SetorId, string.Empty), p.Ordem,
            alcancados.Contains(p.Ordem)))
        .ToList());
  }

  private async Task<Dictionary<int, string>> NomesAsync(IEnumerable<int> setorIds, CancellationToken ct)
  {
    var ids = setorIds.Distinct().ToList();
    if (ids.Count == 0) return new Dictionary<int, string>();
    return (await _catalogo.ObterSetoresPorIdAsync(ids, ct)).ToDictionary(s => s.Id, s => s.Nome);
  }
}
