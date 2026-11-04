using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// A baixa contra o SQL Server real (spec da Fase 5B-1, secao 3): `dbo.Perda` faz a volta completa como
/// agrupador das linhas `Perda` do livro, e as constraints novas (`CK_Perda_Motivo`, `CK_Perda_Escopo`,
/// `CK_Movimentacao_RegistroCoerente`) recusam o que a spec proibe. Cada caso viola UMA restricao so.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class PerdaMapeamentoTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int SetorId);

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "perda");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var setor = await arvore.NovoSetorAsync(db);
    var c = new Cenario(arvore, peca, setor);
    try { await corpo(c); }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private static Perda NovaPerda(Cenario c, string motivo = MotivosDePerda.PerdaArmazem) => new()
  {
    Motivo = motivo, PedidoId = c.Arvore.PedidoId, EstruturaItemId = c.PecaId,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static Movimentacao LinhaPerda(Cenario c, string origem, int? perdaId, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Perda, Quantidade = quantidade,
    OrigemPosicao = origem,
    OrigemSetorId = origem == Posicoes.NoSetor ? c.SetorId : null,
    OrigemOrdem = origem == Posicoes.NoSetor ? 1 : null,
    DestinoPosicao = Posicoes.Perdido, PerdaId = perdaId,
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
  public Task Perda_grava_e_le_de_volta_com_as_linhas_do_livro() => NoCenarioAsync(async c =>
  {
    var perda = NovaPerda(c);
    perda.Observacao = "Caiu da prateleira";
    perda.Tudo = true;
    var perdaId = await GravarAsync(perda);
    var daOrigem = await GravarAsync(LinhaPerda(c, Posicoes.AIniciar, perdaId, 2m));
    var doSetor = await GravarAsync(LinhaPerda(c, Posicoes.NoSetor, perdaId, 3m));

    await using var leitura = NovoContexto();
    var lida = await leitura.Perdas.AsNoTracking().SingleAsync(p => p.Id == perdaId);
    Assert.Equal(MotivosDePerda.PerdaArmazem, lida.Motivo);
    Assert.Equal("Caiu da prateleira", lida.Observacao);
    Assert.Equal(c.Arvore.PedidoId, lida.PedidoId);
    Assert.Null(lida.AgrupamentoId);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.True(lida.Tudo);
    Assert.Equal(perda.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
    Assert.Null(lida.PedidoRetrabalhoId);

    var linhas = await leitura.Movimentacoes.AsNoTracking()
        .Where(m => m.PerdaId == perdaId).OrderBy(m => m.Id).ToListAsync();
    Assert.Equal([daOrigem, doSetor], linhas.Select(m => m.Id));
    Assert.All(linhas, m =>
    {
      Assert.Equal(TiposDeMovimentacao.Perda, m.Tipo);
      Assert.Equal(Posicoes.Perdido, m.DestinoPosicao);
    });
    Assert.Equal([Posicoes.AIniciar, Posicoes.NoSetor], linhas.Select(m => m.OrigemPosicao));
    Assert.Equal([2m, 3m], linhas.Select(m => m.Quantidade));
  });

  [Fact]
  public Task Linha_Perda_sem_PerdaId_e_recusada_pelo_banco() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(LinhaPerda(c, Posicoes.AIniciar, null), "CK_Movimentacao_RegistroCoerente"));

  [Fact]
  public Task Perda_com_AgrupamentoId_e_EstruturaItemId_e_recusada() => NoCenarioAsync(c =>
  {
    var perda = NovaPerda(c);
    perda.AgrupamentoId = c.Arvore.AgrupamentoId;
    return AfirmarRecusaAsync(perda, "CK_Perda_Escopo");
  });

  [Fact]
  public Task Motivo_fora_dos_tres_e_recusado() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(NovaPerda(c, "Roubo"), "CK_Perda_Motivo"));

  [Fact]
  public Task Estorno_com_PerdaId_e_RemessaId_e_recusado() => NoCenarioAsync(async c =>
  {
    var perdaId = await GravarAsync(NovaPerda(c));
    var original = await GravarAsync(LinhaPerda(c, Posicoes.AIniciar, perdaId));
    var remessaId = await GravarAsync(new Remessa { DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId });
    c.Arvore.RemessaIds.Add(remessaId);

    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.Perdido, DestinoPosicao = Posicoes.AIniciar,
      PerdaId = perdaId, RemessaId = remessaId,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_RegistroCoerente");
  });
}
