using Microsoft.EntityFrameworkCore;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// `dbo.Movimentacao` e `dbo.Montagem` contra o SQL Server real (spec da Fase 3, secao 9.2): o
/// mapeamento faz a volta completa, e cada restricao da secao 3 recusa um caso. Cada caso viola UMA
/// restricao so, sempre que o DDL permite; quando uma posicao ou tipo fora da lista viola tambem a
/// restricao de coerencia ou de transicao (que so enumeram valores validos), o teste aceita as duas e
/// diz isso no nome — medir so uma daria teste que passa pelo motivo errado quando a outra disparar
/// primeiro, e a ordem em que o SQL Server avalia CHECKs nao e garantida.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class LivroMapeamentoTests : TesteComBanco
{
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int SetorId);

  private static async Task<Cenario> NovoCenarioAsync(RastreamentoDbContext db)
  {
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "livro");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var setor = await arvore.NovoSetorAsync(db);
    return new Cenario(arvore, peca, setor);
  }

  private static Movimentacao Inicio(Cenario c, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Inicio, Quantidade = quantidade,
    OrigemPosicao = Posicoes.AIniciar,
    DestinoPosicao = Posicoes.NoSetor, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> GravarAsync(Movimentacao m)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    await db.SaveChangesAsync();
    return m.Id;
  }

  private async Task AfirmarRecusaAsync(Movimentacao m, params string[] restricoesAceitas)
  {
    await using var db = NovoContexto();
    db.Movimentacoes.Add(m);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    var mensagem = erro.InnerException?.Message ?? erro.Message;
    Assert.True(restricoesAceitas.Any(r => mensagem.Contains(r)),
        $"esperava {string.Join(" ou ", restricoesAceitas)}; o banco disse: {mensagem}");
  }

  private async Task AfirmarRecusaAsync(Montagem m, string restricao)
  {
    await using var db = NovoContexto();
    db.Montagens.Add(m);
    var erro = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Contains(restricao, erro.InnerException?.Message ?? erro.Message);
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var c = await NovoCenarioAsync(db);
    try { await corpo(c); }
    finally { await c.Arvore.LimparAsync(NovoContexto); }
  }

  [Fact]
  public Task Movimentacao_faz_a_volta_completa() => NoCenarioAsync(async c =>
  {
    var id = await GravarAsync(Inicio(c, 2.5025m));

    await using var leitura = NovoContexto();
    var lida = await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == id);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(TiposDeMovimentacao.Inicio, lida.Tipo);
    Assert.Equal(2.5025m, lida.Quantidade);
    Assert.Equal(Posicoes.AIniciar, lida.OrigemPosicao);
    Assert.Null(lida.OrigemSetorId);
    Assert.Equal(Posicoes.NoSetor, lida.DestinoPosicao);
    Assert.Equal(c.SetorId, lida.DestinoSetorId);
    Assert.Equal(1, lida.DestinoOrdem);
    Assert.Null(lida.MontagemId);
    Assert.Null(lida.EstornoDeId);
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
  });

  [Fact]
  public Task Montagem_faz_a_volta_completa_e_a_baixa_aponta_para_ela() => NoCenarioAsync(async c =>
  {
    await using var db = NovoContexto();
    var montagem = new Montagem
    {
      EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 3m,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };
    db.Montagens.Add(montagem);
    await db.SaveChangesAsync();
    var item = await c.Arvore.NovoItemAsync(db, c.PecaId, 12m, 4m);
    var baixa = await GravarAsync(new Movimentacao
    {
      EstruturaItemId = item, Tipo = TiposDeMovimentacao.Montagem, Quantidade = 12m,
      OrigemPosicao = Posicoes.AguardandoMontagem, OrigemSetorId = c.SetorId,
      DestinoPosicao = Posicoes.Montado, MontagemId = montagem.Id,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    });

    await using var leitura = NovoContexto();
    var lida = await leitura.Montagens.AsNoTracking().SingleAsync(m => m.Id == montagem.Id);
    Assert.Equal(3m, lida.Quantidade);
    Assert.Null(lida.EstornadaEm);
    Assert.Null(lida.EstornadaPorUsuarioId);
    Assert.Equal(montagem.Id, (await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == baixa)).MontagemId);
  });

  [Fact]
  public Task Quantidade_zero_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Inicio(c, 0m), "CK_Movimentacao_QuantidadePositiva"));

  [Fact]
  public Task Tipo_fora_da_lista_e_recusado_pelo_tipo_ou_pela_transicao() => NoCenarioAsync(c =>
  {
    var m = Inicio(c);
    m.Tipo = "Pronto";   // tipo da Fase 5, ainda nao existe
    return AfirmarRecusaAsync(m, "CK_Movimentacao_Tipo", "CK_Movimentacao_Transicao");
  });

  [Fact]
  public Task Posicao_de_origem_fora_da_lista_e_recusada_pela_lista_ou_pela_coerencia() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = "Expedido", DestinoPosicao = Posicoes.AIniciar,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_OrigemPosicao", "CK_Movimentacao_OrigemCoerente");
  });

  [Fact]
  public Task Posicao_de_destino_fora_da_lista_e_recusada_pela_lista_ou_pela_coerencia() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    await AfirmarRecusaAsync(new Movimentacao
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1, DestinoPosicao = "Perdido",
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    }, "CK_Movimentacao_DestinoPosicao", "CK_Movimentacao_DestinoCoerente");
  });

  [Fact]
  public Task Origem_no_Setor_sem_Setor_e_recusada() => NoCenarioAsync(c =>
      // Termino com OrigemSetorId nulo: a transicao compara OrigemSetorId = DestinoSetorId, que da
      // UNKNOWN com nulo — e CHECK com UNKNOWN passa. Sobra so a coerencia da origem para recusar.
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Termino, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AguardandoColeta, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_OrigemCoerente"));

  [Fact]
  public Task Aguardando_montagem_com_passo_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Entrega, Quantidade = 1m,
        OrigemPosicao = Posicoes.AguardandoColeta, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AguardandoMontagem, DestinoSetorId = c.SetorId, DestinoOrdem = 1,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_DestinoCoerente"));

  [Fact]
  public Task Inicio_que_nao_sai_de_a_iniciar_e_recusado_pela_transicao() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Inicio, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.NoSetor, DestinoSetorId = c.SetorId, DestinoOrdem = 2,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_Transicao"));

  [Fact]
  public Task Baixa_de_montagem_sem_Montagem_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Montagem, Quantidade = 1m,
        OrigemPosicao = Posicoes.AguardandoMontagem, OrigemSetorId = c.SetorId,
        DestinoPosicao = Posicoes.Montado,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_MontagemSoNaBaixa"));

  [Fact]
  public Task Estorno_sem_original_e_recusado() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Movimentacao
      {
        EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m,
        OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
        DestinoPosicao = Posicoes.AIniciar,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Movimentacao_EstornoApontaOriginal"));

  [Fact]
  public Task Segundo_estorno_do_mesmo_movimento_e_recusado_pelo_indice_unico() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Inicio(c));
    Movimentacao Estorno() => new()
    {
      EstruturaItemId = c.PecaId, Tipo = TiposDeMovimentacao.Estorno, Quantidade = 1m, EstornoDeId = original,
      OrigemPosicao = Posicoes.NoSetor, OrigemSetorId = c.SetorId, OrigemOrdem = 1,
      DestinoPosicao = Posicoes.AIniciar,
      DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
    };
    await GravarAsync(Estorno());

    await AfirmarRecusaAsync(Estorno(), "UX_Movimentacao_EstornoDe");
  });

  [Fact]
  public Task Montagem_de_quantidade_zero_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 0m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
      }, "CK_Montagem_QuantidadePositiva"));

  [Fact]
  public Task Montagem_estornada_sem_autor_do_estorno_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 1m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId, EstornadaEm = DateTime.UtcNow,
      }, "CK_Montagem_EstornoCompleto"));

  [Fact]
  public Task Montagem_estornada_antes_de_existir_e_recusada() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(new Montagem
      {
        EstruturaItemId = c.PecaId, SetorId = c.SetorId, Quantidade = 1m,
        DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
        EstornadaEm = DateTime.UtcNow.AddHours(-1), EstornadaPorUsuarioId = c.Arvore.AutorId,
      }, "CK_Montagem_EstornoAposMontagem"));
}
