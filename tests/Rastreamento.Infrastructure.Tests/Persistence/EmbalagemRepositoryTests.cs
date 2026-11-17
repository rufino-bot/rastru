using Rastreamento.Domain.Abstractions;
using Rastreamento.Application.Panorama;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `EmbalagemRepository` contra o SQL Server real: a ultima linha de cada Peca e a resposta de "voltou a producao
/// depois da marca" numa leitura so. As datas sao gravadas explicitamente, para o "posterior" nao depender do
/// relogio, e as afirmacoes sao so sobre as Pecas do proprio teste.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class EmbalagemRepositoryTests : TesteComBanco
{
  private static readonly DateTime Base = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

  private async Task NoCenarioAsync(Func<ArvoreDeTesteNoBanco, int, int, Task> corpo)
  {
    await using var db = NovoContexto();
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "embrep");
    try
    {
      var pecaId = await arvore.NovaPecaAsync(db, 3m);
      var setorId = await arvore.NovoSetorAsync(db);
      await corpo(arvore, pecaId, setorId);
    }
    finally { await arvore.LimparAsync(NovoContexto); }
  }

  private async Task GravarEmbalagemAsync(ArvoreDeTesteNoBanco arvore, int pecaId, string tipo, DateTime dataHora)
  {
    await using var db = NovoContexto();
    await new EmbalagemRepository(db).GravarAsync(new Embalagem
    {
      EstruturaItemId = pecaId, Tipo = tipo, DataHora = dataHora, UsuarioId = arvore.AutorId,
    }, CancellationToken.None);
  }

  private async Task GravarMovimentoAsync(
      ArvoreDeTesteNoBanco arvore, int pecaId, string tipo, string origem, int? origemSetor, int? origemOrdem,
      string destino, int? destinoSetor, int? destinoOrdem, DateTime dataHora)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(new Movimentacao
    {
      EstruturaItemId = pecaId, Tipo = tipo, Quantidade = 1m,
      OrigemPosicao = origem, OrigemSetorId = origemSetor, OrigemOrdem = origemOrdem,
      DestinoPosicao = destino, DestinoSetorId = destinoSetor, DestinoOrdem = destinoOrdem,
      DataHora = dataHora, UsuarioId = arvore.AutorId,
    });
    await db.SaveChangesAsync();
  }

  private async Task<UltimaEmbalagem> LerAsync(int pecaId)
  {
    await using var db = NovoContexto();
    var lidas = await new EmbalagemRepository(db).ListarUltimasAsync(
        [pecaId], CalculoDaEmbalagem.PosicoesDeProducao, CancellationToken.None);
    return Assert.Single(lidas);
  }

  [Fact]
  public Task Devolve_so_a_ultima_linha_de_cada_peca() => NoCenarioAsync(async (arvore, pecaId, _) =>
  {
    await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);
    await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Desmarcar, Base.AddMinutes(1));
    await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base.AddMinutes(2));

    var ultima = await LerAsync(pecaId);
    Assert.Equal(TiposDeEmbalagem.Marcar, ultima.Tipo);
    Assert.Equal(Base.AddMinutes(2), ultima.DataHora, TimeSpan.FromSeconds(1));
    Assert.False(ultima.VoltouAProducaoDepois);
  });

  [Fact]
  public Task Peca_sem_linha_nao_volta() => NoCenarioAsync(async (_, pecaId, _) =>
  {
    await using var db = NovoContexto();
    var lidas = await new EmbalagemRepository(db).ListarUltimasAsync(
        [pecaId], CalculoDaEmbalagem.PosicoesDeProducao, CancellationToken.None);
    Assert.Empty(lidas);
  });

  [Fact]
  public Task Movimento_para_producao_depois_da_marca_marca_VoltouAProducaoDepois() =>
      NoCenarioAsync(async (arvore, pecaId, setorId) =>
      {
        await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);
        await GravarMovimentoAsync(arvore, pecaId, "Entrega", "AguardandoConserto", setorId, null,
            "EmConserto", setorId, null, Base.AddMinutes(5));

        Assert.True((await LerAsync(pecaId)).VoltouAProducaoDepois);
      });

  [Fact]
  public Task Movimento_para_producao_antes_da_marca_nao_conta() => NoCenarioAsync(async (arvore, pecaId, setorId) =>
  {
    await GravarMovimentoAsync(arvore, pecaId, "Entrega", "AguardandoConserto", setorId, null,
        "EmConserto", setorId, null, Base.AddMinutes(-5));
    await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);

    Assert.False((await LerAsync(pecaId)).VoltouAProducaoDepois);
  });

  [Fact]
  public Task Movimento_para_fora_da_producao_depois_da_marca_nao_conta() =>
      NoCenarioAsync(async (arvore, pecaId, setorId) =>
      {
        await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);
        await GravarMovimentoAsync(arvore, pecaId, "Entrega", "AguardandoColeta", setorId, 1,
            "NaExpedicao", null, null, Base.AddMinutes(5));

        Assert.False((await LerAsync(pecaId)).VoltouAProducaoDepois);
      });

  [Fact]
  public Task Marca_nao_ressuscita_depois_do_conserto_e_volta() => NoCenarioAsync(async (arvore, pecaId, setorId) =>
  {
    await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);
    await GravarMovimentoAsync(arvore, pecaId, "Conserto", "NaoConforme", null, null,
        "AguardandoConserto", setorId, null, Base.AddMinutes(5));
    await GravarMovimentoAsync(arvore, pecaId, "Entrega", "ConsertoPronto", setorId, null,
        "NaExpedicao", null, null, Base.AddMinutes(10));

    Assert.True((await LerAsync(pecaId)).VoltouAProducaoDepois);
  });

  [Fact]
  public Task Varias_pecas_pedidas_juntas_cada_uma_traz_a_propria_resposta_e_a_nao_pedida_fica_de_fora() =>
      NoCenarioAsync(async (arvore, pecaA, setorId) =>
      {
        int pecaB, pecaC;
        await using (var db = NovoContexto())
        {
          pecaB = await arvore.NovaPecaAsync(db, 2m);
          pecaC = await arvore.NovaPecaAsync(db, 2m);
        }

        // A: marca antiga, marca nova, e movimento para a producao depois da nova.
        await GravarEmbalagemAsync(arvore, pecaA, TiposDeEmbalagem.Desmarcar, Base.AddMinutes(-10));
        await GravarEmbalagemAsync(arvore, pecaA, TiposDeEmbalagem.Marcar, Base);
        await GravarMovimentoAsync(arvore, pecaA, "Entrega", "AguardandoConserto", setorId, null,
            "EmConserto", setorId, null, Base.AddMinutes(5));
        // B: so a marca, sem movimento nenhum.
        await GravarEmbalagemAsync(arvore, pecaB, TiposDeEmbalagem.Marcar, Base.AddMinutes(1));
        // C: tem linha, mas nao e pedida.
        await GravarEmbalagemAsync(arvore, pecaC, TiposDeEmbalagem.Marcar, Base);

        await using var leitura = NovoContexto();
        var lidas = await new EmbalagemRepository(leitura).ListarUltimasAsync(
            [pecaA, pecaB], CalculoDaEmbalagem.PosicoesDeProducao, CancellationToken.None);

        Assert.Equal(2, lidas.Count);
        Assert.DoesNotContain(lidas, l => l.EstruturaItemId == pecaC);
        var a = Assert.Single(lidas, l => l.EstruturaItemId == pecaA);
        var b = Assert.Single(lidas, l => l.EstruturaItemId == pecaB);
        Assert.Equal(TiposDeEmbalagem.Marcar, a.Tipo);
        Assert.Equal(Base, a.DataHora, TimeSpan.FromSeconds(1));
        Assert.True(a.VoltouAProducaoDepois);
        Assert.Equal(Base.AddMinutes(1), b.DataHora, TimeSpan.FromSeconds(1));
        Assert.False(b.VoltouAProducaoDepois);
      });

  [Fact]
  public Task Movimento_na_mesma_DataHora_da_marca_nao_conta_porque_o_posterior_e_estrito() =>
      NoCenarioAsync(async (arvore, pecaId, setorId) =>
      {
        await GravarEmbalagemAsync(arvore, pecaId, TiposDeEmbalagem.Marcar, Base);
        await GravarMovimentoAsync(arvore, pecaId, "Entrega", "AguardandoConserto", setorId, null,
            "EmConserto", setorId, null, Base);

        Assert.False((await LerAsync(pecaId)).VoltouAProducaoDepois);
      });
}
