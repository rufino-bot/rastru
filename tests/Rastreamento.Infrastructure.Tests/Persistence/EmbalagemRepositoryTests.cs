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
}
