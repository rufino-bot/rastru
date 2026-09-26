using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Fix pass da Task 11 (review de branch): dois "iniciar" concorrentes em NOS DIFERENTES do MESMO
/// Pedido `Aberto` deadlockavam no SQL Server real — `PK_Pedido`, medido pelo controlador com um
/// deadlock graph do `system_health` (SERIALIZABLE). O ciclo era U/X: os dois liam a linha do
/// Pedido com S (`ObterPedidoDoNoAsync`, dentro da transacao SERIALIZABLE de
/// `IExecucaoRepository.EmTransacaoAsync`) e so DEPOIS tentavam converter para X
/// (`MarcarPedidoEmProducaoAsync`) — as duas conversoes esperando uma a outra e nenhuma cedendo.
///
/// <para>
/// O conserto (`ExecucaoRepository.ObterPedidoDoNoParaEscritaAsync`, UPDLOCK) e estrutural: UPDLOCK e
/// exclusivo entre si, entao a SEGUNDA transacao que chega aqui espera a primeira terminar, em vez de
/// as duas seguirem com S e so se travarem na conversao. Isto NAO deixa o teste flaky por
/// definicao — o cliclo de deadlock que ele mede fica IMPOSSIVEL para este par de recursos (Pedido +
/// um EstruturaItem PROPRIO de cada transacao, nunca o do outro lado), nao so improvavel. Ainda assim
/// ele repete a corrida <see cref="Repeticoes"/> vezes: e a mesma defesa de `CorridaNoIniciarTests`
/// contra um caso em que a corrida so aparece com volume.
/// </para>
///
/// <para>
/// MEDIDO no fix pass (numeros completos no relatorio da Task 11, secao do fix round 1): com o
/// conserto (`ObterPedidoDoNoParaEscritaAsync`) presente e o retry de 1205 (`EmTransacaoAsync`)
/// TEMPORARIAMENTE desligado (`TentativasMaximas = 1`), <b>5 execucoes deste teste, 0 falhas</b> — o
/// fix estrutural basta para este par de recursos, sem precisar do retry. Revertendo SO o conserto
/// (`ApontamentoUseCase.Iniciar` de volta a `ObterPedidoDoNoAsync`, sem UPDLOCK) com o retry ainda
/// desligado, <b>5 execucoes, 5 falhas</b> — todas na PRIMEIRA repeticao do loop (nunca precisou das
/// 20 para aparecer; o deadlock e a regra, no codigo antigo, nao a excecao). As duas series voltaram
/// ao estado normal (os dois fixes presentes) antes do commit. O retry (fix 2) fica como rede de
/// seguranca para os OUTROS dois deadlocks que a mesma review mediu (`UQ_EstruturaRoteiro` e
/// `PK_Movimentacao`, por range lock de indice esparso) — este teste aqui prova so o de Pedido, que e
/// o que teria produto de codigo para testar sem tocar Roteiro nem o livro de movimentacao.
/// </para>
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class CorridaDeIniciarNoMesmoPedidoTests : TesteComBanco
{
  /// <summary>
  /// 20, como o brief pediu — grande o bastante para o deadlock aparecer de forma confiavel no
  /// codigo ANTIGO (medido no fix pass), sem alongar demais a suite normal (com o fix, cada rodada e
  /// so dois INSERTs).
  /// </summary>
  private const int Repeticoes = 20;

  private static ApontamentoUseCase CasoDeUso(RastreamentoDbContext db) =>
      new(new ExecucaoRepository(db), new EstruturaRepository(db), new SetorRepository(db), new ReceitaPadraoRepository(db));

  [Fact]
  public async Task Dois_inicios_paralelos_em_nos_diferentes_do_mesmo_Pedido_sempre_tem_sucesso()
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corrpd");
    try
    {
      var setor = await arvore.NovoSetorAsync(db);
      // Duas Pecas (nos DIFERENTES) do MESMO Pedido/Agrupamento que `arvore` criou — e a condicao
      // exata do deadlock graph medido: o Pedido e o recurso disputado, nao o no.
      var pecaA = await arvore.NovaPecaAsync(db, Repeticoes);
      var pecaB = await arvore.NovaPecaAsync(db, Repeticoes);
      await arvore.RoteiroAsync(db, pecaA, setor);
      await arvore.RoteiroAsync(db, pecaB, setor);

      async Task<Result<MovimentacaoDto>> IniciarAsync(int no)
      {
        await using var contexto = NovoContexto();
        return await CasoDeUso(contexto).Iniciar(no, new InicioDto(setor, 1m), arvore.AutorId, CancellationToken.None);
      }

      for (var repeticao = 1; repeticao <= Repeticoes; repeticao++)
      {
        var resultados = await Task.WhenAll(Task.Run(() => IniciarAsync(pecaA)), Task.Run(() => IniciarAsync(pecaB)));

        foreach (var r in resultados)
          Assert.True(r.Sucesso, $"repeticao {repeticao}: {r.Erro} — {r.Detalhe}");
      }
    }
    finally
    {
      await arvore.LimparAsync(NovoContexto);
    }
  }
}
