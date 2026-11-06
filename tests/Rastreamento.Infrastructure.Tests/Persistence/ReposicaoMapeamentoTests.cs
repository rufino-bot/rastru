using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// A reposicao contra o SQL Server real (spec da Fase 5B-2, secao 3): `dbo.Reclamacao` e
/// `dbo.DispensaDeReposicao` fazem a volta completa, os tipos `Pronto` e `Devolucao` entram no livro com o
/// registro que apontam, e as constraints novas recusam o que a spec proibe. Cada caso viola UMA restricao so.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class ReposicaoMapeamentoTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int SetorId);

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "repos");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var setor = await arvore.NovoSetorAsync(db);
    var c = new Cenario(arvore, peca, setor);
    try { await corpo(c); }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private static Perda NovaPerda(Cenario c, string motivo = MotivosDePerda.PerdaArmazem, int? retrabalhoId = null) => new()
  {
    Motivo = motivo, PedidoId = c.Arvore.PedidoId, EstruturaItemId = c.PecaId,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId, PedidoRetrabalhoId = retrabalhoId,
  };

  private static Reclamacao NovaReclamacao(Cenario c) => new()
  {
    PedidoId = c.Arvore.PedidoId, EstruturaItemId = c.PecaId, Quantidade = 2m, Descricao = "Veio torta",
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static DispensaDeReposicao NovaDispensa(Cenario c, int? perdaId, int? reclamacaoId) => new()
  {
    PerdaId = perdaId, ReclamacaoId = reclamacaoId, Justificativa = "Sem necessidade",
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  /// <summary>Uma linha do livro de `AIniciar` para a posicao dada, com o Setor e o passo que ela pede.</summary>
  private static Movimentacao LinhaDeAIniciar(Cenario c, string tipo, string destino, int? perdaId, int? reclamacaoId) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = tipo, Quantidade = 1m,
    OrigemPosicao = Posicoes.AIniciar,
    DestinoPosicao = destino, DestinoSetorId = c.SetorId,
    DestinoOrdem = destino == Posicoes.AguardandoColeta ? 1 : null,
    PerdaId = perdaId, ReclamacaoId = reclamacaoId,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync<T>(T linha) where T : class
  {
    await using var db = NovoContexto();
    db.Add(linha);
    await db.SaveChangesAsync();
    return (int)db.Entry(linha).Property("Id").CurrentValue!;
  }

  private async Task AfirmarRecusaAsync<T>(T linha, params string[] restricoesAceitas) where T : class
  {
    await using var db = NovoContexto();
    db.Add(linha);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    var mensagem = erro.InnerException?.Message ?? erro.Message;
    Assert.True(restricoesAceitas.Any(r => mensagem.Contains(r)),
        $"esperava {string.Join(" ou ", restricoesAceitas)}; o banco disse: {mensagem}");
  }

  [Fact]
  public Task Reclamacao_grava_e_le_de_volta() => NoCenarioAsync(async c =>
  {
    var reclamacao = NovaReclamacao(c);
    reclamacao.PedidoRetrabalhoId = c.Arvore.PedidoId;
    reclamacao.EstornadaEm = DateTime.UtcNow;
    reclamacao.EstornadaPorUsuarioId = c.Arvore.AutorId;
    var id = await GravarAsync(reclamacao);
    var devolucao = await GravarAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Devolucao, Posicoes.AguardandoConserto, null, id));

    await using var leitura = NovoContexto();
    var lida = await leitura.Reclamacoes.AsNoTracking().SingleAsync(r => r.Id == id);
    Assert.Equal(c.Arvore.PedidoId, lida.PedidoId);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(2m, lida.Quantidade);
    Assert.Equal("Veio torta", lida.Descricao);
    Assert.Equal(c.Arvore.PedidoId, lida.PedidoRetrabalhoId);
    Assert.Equal(reclamacao.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
    Assert.NotNull(lida.EstornadaEm);
    Assert.Equal(c.Arvore.AutorId, lida.EstornadaPorUsuarioId);

    var linha = await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == devolucao);
    Assert.Equal(id, linha.ReclamacaoId);
    Assert.Equal(TiposDeMovimentacao.Devolucao, linha.Tipo);
  });

  [Fact]
  public Task Reclamacao_de_quantidade_zero_e_recusada() => NoCenarioAsync(c =>
  {
    var reclamacao = NovaReclamacao(c);
    reclamacao.Quantidade = 0m;
    return AfirmarRecusaAsync(reclamacao, "CK_Reclamacao_Quantidade");
  });

  [Fact]
  public Task Linha_Devolucao_sem_ReclamacaoId_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Devolucao, Posicoes.AguardandoConserto, null, null),
          "CK_Movimentacao_RegistroCoerente"));

  [Fact]
  public Task Linha_Pronto_sem_PerdaId_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Pronto, Posicoes.AguardandoColeta, null, null),
          "CK_Movimentacao_RegistroCoerente"));

  [Theory]
  [InlineData(Posicoes.AguardandoColeta)]
  [InlineData(Posicoes.AguardandoMontagem)]
  public Task Linha_Pronto_com_PerdaId_vai_para_a_sobra(string destino) => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c, MotivosDePerda.Reaproveitado, c.Arvore.PedidoId));
    var id = await GravarAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Pronto, destino, perdaId, null));
    Assert.True(id > 0);
  });

  [Fact]
  public Task Linha_Pronto_so_vai_para_a_sobra() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c, MotivosDePerda.Reaproveitado, c.Arvore.PedidoId));
    await AfirmarRecusaAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Pronto, Posicoes.AguardandoConserto, perdaId, null),
        "CK_Movimentacao_Transicao");
  });

  [Fact]
  public Task Linha_Devolucao_so_vai_para_AguardandoConserto() => NoCenarioAsync(async c =>
  {
    var reclamacaoId = await GravarAsync(NovaReclamacao(c));
    await AfirmarRecusaAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Devolucao, Posicoes.AguardandoMontagem, null, reclamacaoId),
        "CK_Movimentacao_Transicao");
  });

  [Fact]
  public Task Estorno_com_PerdaId_e_ReclamacaoId_e_recusado() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c));
    var reclamacaoId = await GravarAsync(NovaReclamacao(c));
    var original = await GravarAsync(LinhaDeAIniciar(c, TiposDeMovimentacao.Devolucao, Posicoes.AguardandoConserto, null, reclamacaoId));

    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.AguardandoConserto, OrigemSetorId = c.SetorId, DestinoPosicao = Posicoes.AIniciar,
      PerdaId = perdaId, ReclamacaoId = reclamacaoId,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_RegistroCoerente");
  });

  [Fact]
  public Task Perda_Reaproveitado_com_retrabalho_e_PerdaDeOrigemId_fazem_a_volta() => NoCenarioAsync(async c =>
  {
    var origem = await GravarAsync(NovaPerda(c, MotivosDePerda.Reaproveitado, c.Arvore.PedidoId));
    var ligada = NovaPerda(c, MotivosDePerda.Descarte);
    ligada.PerdaDeOrigemId = origem;
    var ligadaId = await GravarAsync(ligada);

    await using var leitura = NovoContexto();
    Assert.Equal(origem, (await leitura.Perdas.AsNoTracking().SingleAsync(p => p.Id == ligadaId)).PerdaDeOrigemId);
    Assert.Equal(c.Arvore.PedidoId, (await leitura.Perdas.AsNoTracking().SingleAsync(p => p.Id == origem)).PedidoRetrabalhoId);
  });

  [Fact]
  public Task Perda_Reaproveitado_sem_PedidoRetrabalhoId_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(NovaPerda(c, MotivosDePerda.Reaproveitado), "CK_Perda_ReaproveitadoTemRetrabalho"));

  [Fact]
  public Task Perda_Descarte_com_PedidoRetrabalhoId_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(NovaPerda(c, MotivosDePerda.Descarte, c.Arvore.PedidoId), "CK_Perda_DescarteSemRetrabalho"));

  [Fact]
  public Task Dispensa_com_os_dois_alvos_e_recusada() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c));
    var reclamacaoId = await GravarAsync(NovaReclamacao(c));
    await AfirmarRecusaAsync(NovaDispensa(c, perdaId, reclamacaoId), "CK_DispensaDeReposicao_Alvo");
  });

  [Fact]
  public Task Dispensa_sem_alvo_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(NovaDispensa(c, null, null), "CK_DispensaDeReposicao_Alvo"));

  [Fact]
  public Task Duas_dispensas_vigentes_da_mesma_baixa_sao_recusadas() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c));
    await GravarAsync(NovaDispensa(c, perdaId, null));
    await AfirmarRecusaAsync(NovaDispensa(c, perdaId, null), "UX_DispensaDeReposicao_PerdaVigente");
  });

  [Fact]
  public Task Duas_dispensas_vigentes_da_mesma_reclamacao_sao_recusadas() => NoCenarioAsync(async c =>
  {
    var reclamacaoId = await GravarAsync(NovaReclamacao(c));
    await GravarAsync(NovaDispensa(c, null, reclamacaoId));
    await AfirmarRecusaAsync(NovaDispensa(c, null, reclamacaoId), "UX_DispensaDeReposicao_ReclamacaoVigente");
  });

  [Fact]
  public Task Dispensa_estornada_nao_impede_outra() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c));
    var estornada = NovaDispensa(c, perdaId, null);
    estornada.EstornadaEm = DateTime.UtcNow;
    estornada.EstornadaPorUsuarioId = c.Arvore.AutorId;
    await GravarAsync(estornada);

    var vigente = await GravarAsync(NovaDispensa(c, perdaId, null));

    await using var leitura = NovoContexto();
    var lidas = await leitura.DispensasDeReposicao.AsNoTracking().Where(d => d.PerdaId == perdaId).ToListAsync();
    Assert.Equal(2, lidas.Count);
    Assert.Single(lidas, d => d.EstornadaEm == null && d.Id == vigente);
  });

  [Fact]
  public Task No_copiado_guarda_o_no_de_origem() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var copia = new EstruturaItem
    {
      AgrupamentoId = c.Arvore.AgrupamentoId, ComponenteId = c.Arvore.ComponenteId, NivelHierarquico = "Peca",
      Quantidade = 1m, OrigemEstruturaItemId = c.PecaId,
    };
    db.Estruturas.Add(copia);
    await db.SaveChangesAsync();

    await using var leitura = NovoContexto();
    Assert.Equal(c.PecaId, (await leitura.Estruturas.AsNoTracking().SingleAsync(e => e.Id == copia.Id)).OrigemEstruturaItemId);
  });
}
