using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
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
/// as duas seguirem com S e so se travarem na conversao.
/// </para>
///
/// <para>
/// <b>DUAS FALHAS deste teste FORAM ACHADAS na re-review (fix round 2) — a versao anterior deste
/// arquivo passava 5/5 mesmo com o bug de volta, e por dois motivos DIFERENTES, os dois consertados
/// aqui:</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>O mesmo Pedido para as 20 repeticoes.</b> So a repeticao 1 disputava a transicao
/// `Aberto` -&gt; `EmProducao` de verdade — da repeticao 2 em diante o Pedido ja NAO estava `Aberto`,
/// e `MarcarPedidoEmProducaoAsync` (`WHERE Status = 'Aberto'`) nao achava linha para atualizar: sem
/// UPDATE nenhum, nao ha conversao para X, e o ciclo que este teste mede fica estruturalmente
/// IMPOSSIVEL de reaparecer nas repeticoes 2-20, com o bug ou sem ele. As afirmacoes antigas ("20
/// repeticoes fazem o deadlock aparecer de forma confiavel", "mesma defesa de `CorridaNoIniciarTests`
/// contra uma corrida que so aparece com volume") eram FALSAS: nao existe "com volume" aqui, porque
/// so a primeira tentativa é real. <b>Fix:</b> cada repeticao cria um Pedido/Agrupamento/Componente
/// NOVOS (`ArvoreDeTesteNoBanco.CriarAsync` de novo a cada volta do loop) — as 20 repeticoes agora
/// sao 20 disputas INDEPENDENTES pela mesma transicao, nao 1 disputa real seguida de 19 no-ops.
/// </item>
/// <item>
/// <b>O retry de produção (fix 2 do round 1) mascarava o bug mesmo na repeticao 1.</b> Com
/// `TentativasMaximas` de producao (3) e o bug de volta (`Iniciar` usando `ObterPedidoDoNoAsync`, sem
/// UPDLOCK), a re-review mediu que o teste passava 5/5: o deadlock ACONTECIA (SQL Server escolhia uma
/// vitima), mas a vitima REEXECUTAVA depois que a sobrevivente ja tinha COMITADO — nesse ponto o
/// Pedido ja nao e `Aberto`, a atualizacao da vitima na segunda tentativa vira no-op, e ela "passa"
/// sem nunca ter corrigido nada. O retry, pensado para os OUTROS dois deadlocks
/// (`UQ_EstruturaRoteiro`, `PK_Movimentacao`), escondia uma regressao NESTE, especificamente porque
/// aqui a segunda tentativa encontra um mundo onde a disputa ja acabou. <b>Fix:</b> as chamadas de
/// `Iniciar` deste teste usam o construtor `internal` de `ExecucaoRepository` com
/// <c>tentativasMaximas: 1</c> — sem retry, um deadlock real sobe direto como
/// `ConflitoDeConcorrenciaException`/falha de asserção, em vez de ser absorvido.
/// </item>
/// </list>
///
/// <para>
/// MEDIDO no fix round 2 (numeros completos no relatorio da Task 11): com os dois fixes de producao
/// presentes (UPDLOCK + retry), <b>5 execucoes deste teste (com o desenho novo), 0 falhas</b>.
/// Revertendo SO `ApontamentoUseCase.Iniciar` para `ObterPedidoDoNoAsync` (sem UPDLOCK, retry de
/// producao intocado — 3 tentativas), <b>5 execucoes, 5 falhas</b>. Os dois fixes foram restaurados
/// antes do commit.
/// </para>
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class CorridaDeIniciarNoMesmoPedidoTests : TesteComBanco
{
  /// <summary>
  /// 20 disputas INDEPENDENTES (cada uma com o proprio Pedido `Aberto` — ver o XML doc da classe,
  /// item 1): o numero em si nao muda o poder do teste (uma unica disputa ja bastaria, e bastou nas
  /// 5 execucoes medidas), mas mantem o volume que `CorridaNoIniciarTests` usa para o caso em que a
  /// maquina de CI for rapida ou lenta demais numa unica tentativa.
  /// </summary>
  private const int Repeticoes = 20;

  [Fact]
  public async Task Dois_inicios_paralelos_em_nos_diferentes_do_mesmo_Pedido_Aberto_sempre_tem_sucesso()
  {
    var arvores = new List<ArvoreDeTesteNoBanco>();
    int? setorId = null;
    try
    {
      for (var repeticao = 1; repeticao <= Repeticoes; repeticao++)
      {
        await using var db = NovoContexto();
        // Pedido/Agrupamento/Componente NOVOS a cada repeticao — de proposito (ver o XML doc da
        // classe, item 1): e o que faz cada repeticao ser uma disputa de verdade pela transicao
        // Aberto -> EmProducao, e nao um no-op depois da primeira.
        var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "corrpd");
        arvores.Add(arvore);
        // Um Setor SO, reusado por todas as repeticoes, e criado FORA de qualquer `arvore` (nao por
        // `NovoSetorAsync`, que o amarraria ao `SetorIds` de UMA delas): se ele vivesse no
        // `SetorIds` de `arvores[0]`, o `LimparAsync` dela apagaria o Setor antes de as OUTRAS 19
        // arvores (cujo EstruturaRoteiro tambem aponta pra ele) serem limpas — `FK_EstruturaRoteiro_Setor`,
        // medido ao rodar este teste pela primeira vez neste desenho. Por isso o Setor e apagado
        // por fora, DEPOIS de todas as arvores, no `finally`.
        if (setorId is null)
        {
          var novoSetor = new Setor { Nome = $"corrpd-{Guid.NewGuid():N}"[..20], Ativo = true };
          db.Setores.Add(novoSetor);
          await db.SaveChangesAsync();
          setorId = novoSetor.Id;
        }
        var setor = setorId.Value;
        var pecaA = await arvore.NovaPecaAsync(db, 1m);
        var pecaB = await arvore.NovaPecaAsync(db, 1m);
        await arvore.RoteiroAsync(db, pecaA, setor);
        await arvore.RoteiroAsync(db, pecaB, setor);

        async Task<Result<MovimentacaoDto>> IniciarAsync(int no)
        {
          await using var contexto = NovoContexto();
          // 1 tentativa (construtor internal): sem o retry de producao escondendo uma regressao do
          // fix 1 — ver o XML doc da classe, item 2.
          var caso = new ApontamentoUseCase(
              new ExecucaoRepository(contexto, tentativasMaximas: 1), new EstruturaRepository(contexto),
              new SetorRepository(contexto), new ReceitaPadraoRepository(contexto));
          return await caso.Iniciar(no, new InicioDto(setor, 1m), arvore.AutorId, CancellationToken.None);
        }

        var resultados = await Task.WhenAll(Task.Run(() => IniciarAsync(pecaA)), Task.Run(() => IniciarAsync(pecaB)));

        foreach (var r in resultados)
          Assert.True(r.Sucesso, $"repeticao {repeticao}: {r.Erro} — {r.Detalhe}");
      }
    }
    finally
    {
      // A arvore primeiro (o Setor depende delas — EstruturaRoteiro tem FK para os dois), o Setor
      // compartilhado por ultimo.
      foreach (var arvore in arvores)
        await arvore.LimparAsync(NovoContexto);
      if (setorId is int sid)
      {
        await using var db = NovoContexto();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.Setor WHERE Id = {sid}");
      }
    }
  }
}
