using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `dbo.PedidoPausa` contra o SQL Server real (spec da Fase 3D, secao 9): o mapeamento faz a volta
/// completa e cada restricao da tabela recusa um caso. Na mesma colecao das demais classes que usam
/// `ArvoreDeTesteNoBanco`, que escreve em `Componente`.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class PedidoPausaMapeamentoTests : TesteComBanco
{
  private async Task NaArvoreAsync(Func<ArvoreDeTesteNoBanco, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "pausa");
    try { await corpo(arvore); }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private static PedidoPausa Aberta(ArvoreDeTesteNoBanco a, string? motivo = null) => new()
  {
    PedidoId = a.PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = a.AutorId, Motivo = motivo,
  };

  private async Task GravarAsync(PedidoPausa pausa)
  {
    await using var db = NovoContexto();
    db.PedidoPausas.Add(pausa);
    await db.SaveChangesAsync();
  }

  private async Task AfirmarRecusaAsync(PedidoPausa pausa, string restricao)
  {
    await using var db = NovoContexto();
    db.PedidoPausas.Add(pausa);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Contains(restricao, erro.InnerException?.Message ?? erro.Message);
  }

  [Fact]
  public Task Pausa_faz_a_volta_completa_com_motivo_e_com_a_retomada() => NaArvoreAsync(async a =>
  {
    var pausa = Aberta(a, "PED-9 urgente");
    await GravarAsync(pausa);

    await using (var leitura = NovoContexto())
    {
      var aberta = await leitura.PedidoPausas.AsNoTracking().SingleAsync(p => p.Id == pausa.Id);
      Assert.Equal((a.PedidoId, a.AutorId, "PED-9 urgente"), (aberta.PedidoId, aberta.PausadoPorUsuarioId, aberta.Motivo));
      Assert.Equal(pausa.PausadoEm, aberta.PausadoEm, TimeSpan.FromSeconds(1));
      Assert.Null(aberta.RetomadoEm);
      Assert.Null(aberta.RetomadoPorUsuarioId);
    }

    var retomadoEm = pausa.PausadoEm.AddMinutes(5);
    await using (var escrita = NovoContexto())
    {
      await escrita.PedidoPausas.Where(p => p.Id == pausa.Id)
          .ExecuteUpdateAsync(s => s
              .SetProperty(p => p.RetomadoEm, (DateTime?)retomadoEm)
              .SetProperty(p => p.RetomadoPorUsuarioId, (int?)a.AutorId));
    }

    await using var depois = NovoContexto();
    var fechada = await depois.PedidoPausas.AsNoTracking().SingleAsync(p => p.Id == pausa.Id);
    Assert.Equal(retomadoEm, fechada.RetomadoEm!.Value, TimeSpan.FromSeconds(1));
    Assert.Equal(a.AutorId, fechada.RetomadoPorUsuarioId);
  });

  [Fact]
  public Task Segunda_pausa_aberta_do_mesmo_Pedido_e_recusada_pelo_indice_unico() => NaArvoreAsync(async a =>
  {
    await GravarAsync(Aberta(a));

    await AfirmarRecusaAsync(Aberta(a), "UX_PedidoPausa_UmaAbertaPorPedido");
  });

  [Fact]
  public Task Segunda_pausa_depois_da_primeira_fechada_e_aceita() => NaArvoreAsync(async a =>
  {
    var primeira = Aberta(a);
    primeira.RetomadoEm = primeira.PausadoEm.AddMinutes(1);
    primeira.RetomadoPorUsuarioId = a.AutorId;
    await GravarAsync(primeira);

    await GravarAsync(Aberta(a));

    await using var leitura = NovoContexto();
    Assert.Equal(2, await leitura.PedidoPausas.AsNoTracking().CountAsync(p => p.PedidoId == a.PedidoId));
  });

  [Fact]
  public Task RetomadoEm_sem_RetomadoPorUsuarioId_e_recusado() => NaArvoreAsync(async a =>
  {
    var pausa = Aberta(a);
    pausa.RetomadoEm = pausa.PausadoEm.AddMinutes(1);

    await AfirmarRecusaAsync(pausa, "CK_PedidoPausa_RetomadaCompleta");
  });

  [Fact]
  public Task RetomadoEm_antes_de_PausadoEm_e_recusado() => NaArvoreAsync(async a =>
  {
    var pausa = Aberta(a);
    pausa.RetomadoEm = pausa.PausadoEm.AddMinutes(-1);
    pausa.RetomadoPorUsuarioId = a.AutorId;

    await AfirmarRecusaAsync(pausa, "CK_PedidoPausa_RetomadaAposPausa");
  });

  [Fact]
  public Task PausadoEm_nao_preenchido_e_gravado_pelo_DEFAULT_do_banco() => NaArvoreAsync(async a =>
  {
    var pausa = new PedidoPausa { PedidoId = a.PedidoId, PausadoPorUsuarioId = a.AutorId };
    await GravarAsync(pausa);

    await using var leitura = NovoContexto();
    var lida = await leitura.PedidoPausas.AsNoTracking().SingleAsync(p => p.Id == pausa.Id);
    Assert.True(lida.PausadoEm > DateTime.UtcNow.AddMinutes(-5), $"PausadoEm gravado como {lida.PausadoEm:O}");
  });
}
