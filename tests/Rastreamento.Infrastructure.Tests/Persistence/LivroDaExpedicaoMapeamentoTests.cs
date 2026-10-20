using Microsoft.EntityFrameworkCore;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Rastreamento.Infrastructure.Persistence;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// O fim do caminho da Peca contra o SQL Server real (spec da Fase 5A, secao 3): as quatro tabelas novas
/// fazem a volta completa, as constraints do livro aceitam cada transicao da tabela 3.3 e recusam as que
/// ela nao tem, e a soma do banco bate com a do C# para as posicoes novas. Cada caso viola UMA restricao so,
/// sempre que o DDL permite — o nome dela esta na mensagem do banco, e e ele que o teste procura.
/// </summary>
[Collection(ColecaoQueEscreveEmComponente.Nome)]
public class LivroDaExpedicaoMapeamentoTests : TesteComBanco
{
  /// <summary>Dois Setores: o conserto exige o mesmo Setor na ida e na volta, e o caso recusado precisa de dois.</summary>
  private sealed record Cenario(ArvoreDeTesteNoBanco Arvore, int PecaId, int SetorId, int OutroSetorId);

  private static async Task<Cenario> NovoCenarioAsync(RastreamentoDbContext db)
  {
    var arvore = await ArvoreDeTesteNoBanco.CriarAsync(db, "expedicao");
    var peca = await arvore.NovaPecaAsync(db, 10m);
    var setor = await arvore.NovoSetorAsync(db);
    var outro = await arvore.NovoSetorAsync(db);
    return new Cenario(arvore, peca, setor, outro);
  }

  private async Task NoCenarioAsync(Func<Cenario, Task> corpo)
  {
    await using var db = NovoContexto();
    var c = await NovoCenarioAsync(db);
    try { await corpo(c); }
    finally { await c.Arvore.LimparAsync(NovoContexto); }
  }

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

  private static RelatorioDimensional Afericao(Cenario c, string? resultado = ResultadosDeAfericao.NaoConforme) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeAfericao.Afericao, Resultado = resultado,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static RelatorioDimensional EstornoDeAfericao(Cenario c, int original) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeAfericao.Estorno, EstornoDeId = original,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static ContagemDimensional Contagem(Cenario c, decimal conformes, decimal naoConformes) => new()
  {
    EstruturaItemId = c.PecaId, Conformes = conformes, NaoConformes = naoConformes,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static LiberacaoDimensional Pedido(Cenario c, decimal quantidade = 3m, string? texto = "Cliente aceita") => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeLiberacao.Pedido, Quantidade = quantidade, Texto = texto,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private static LiberacaoDimensional Resposta(Cenario c, int? pedidoId, decimal quantidade = 2m) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = TiposDeLiberacao.Resposta, PedidoDeLiberacaoId = pedidoId, Quantidade = quantidade,
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  private async Task<int> NovaRemessaAsync(Cenario c)
  {
    var id = await GravarAsync(new Remessa { DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId });
    c.Arvore.RemessaIds.Add(id);
    return id;
  }

  /// <summary>O Setor do cenario: 1 e o primeiro, 2 o segundo, nulo e "sem Setor" (as posicoes que nao o tem).</summary>
  private static int? SetorDe(Cenario c, int? qual) => qual switch { 1 => c.SetorId, 2 => c.OutroSetorId, _ => null };

  private static Movimentacao Linha(
      Cenario c, string tipo, string de, int? deSetor, string para, int? paraSetor, decimal quantidade = 1m) => new()
  {
    EstruturaItemId = c.PecaId, Tipo = tipo, Quantidade = quantidade,
    OrigemPosicao = de, OrigemSetorId = SetorDe(c, deSetor),
    DestinoPosicao = para, DestinoSetorId = SetorDe(c, paraSetor),
    DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId,
  };

  /// <summary>Grava o agrupador que o tipo exige e aponta a linha para ele (nenhum para os tipos que nao tem).</summary>
  private async Task ComAgrupadorAsync(Cenario c, Movimentacao m)
  {
    switch (m.Tipo)
    {
      case TiposDeMovimentacao.Medicao:
        m.ContagemId = await GravarAsync(Contagem(c, 1m, 1m));
        break;
      case TiposDeMovimentacao.PedidoDeLiberacao:
        m.LiberacaoId = await GravarAsync(Pedido(c));
        break;
      case TiposDeMovimentacao.Liberacao:
      case TiposDeMovimentacao.Recusa:
        m.LiberacaoId = await GravarAsync(Resposta(c, await GravarAsync(Pedido(c))));
        break;
      case TiposDeMovimentacao.Expedicao:
        m.RemessaId = await NovaRemessaAsync(c);
        break;
    }
  }

  private async Task<int> GravarLinhaAsync(Cenario c, Movimentacao m)
  {
    await ComAgrupadorAsync(c, m);
    return await GravarAsync(m);
  }

  [Fact]
  public Task Afericao_e_estorno_fazem_a_volta_completa() => NoCenarioAsync(async c =>
  {
    var afericao = Afericao(c);
    var afericaoId = await GravarAsync(afericao);
    var estorno = EstornoDeAfericao(c, afericaoId);
    var estornoId = await GravarAsync(estorno);

    await using var leitura = NovoContexto();
    var lida = await leitura.Afericoes.AsNoTracking().SingleAsync(a => a.Id == afericaoId);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(TiposDeAfericao.Afericao, lida.Tipo);
    Assert.Equal(ResultadosDeAfericao.NaoConforme, lida.Resultado);
    Assert.Null(lida.EstornoDeId);
    Assert.Equal(afericao.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);

    var lidoEstorno = await leitura.Afericoes.AsNoTracking().SingleAsync(a => a.Id == estornoId);
    Assert.Equal(c.PecaId, lidoEstorno.EstruturaItemId);
    Assert.Equal(TiposDeAfericao.Estorno, lidoEstorno.Tipo);
    Assert.Null(lidoEstorno.Resultado);
    Assert.Equal(afericaoId, lidoEstorno.EstornoDeId);
    Assert.Equal(estorno.DataHora, lidoEstorno.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lidoEstorno.UsuarioId);
  });

  [Fact]
  public Task Segundo_estorno_da_mesma_afericao_viola_UX_RelatorioDimensional_EstornoDe() => NoCenarioAsync(async c =>
  {
    var original = await GravarAsync(Afericao(c));
    await GravarAsync(EstornoDeAfericao(c, original));

    await AfirmarRecusaAsync(EstornoDeAfericao(c, original), "UX_RelatorioDimensional_EstornoDe");
  });

  [Fact]
  public Task Afericao_sem_resultado_viola_o_CHECK() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Afericao(c, resultado: null), "CK_RelatorioDimensional_ResultadoNaAfericao"));

  [Fact]
  public Task Contagem_faz_a_volta_completa() => NoCenarioAsync(async c =>
  {
    var contagem = Contagem(c, 4.5m, 2m);
    var id = await GravarAsync(contagem);

    await using var leitura = NovoContexto();
    var lida = await leitura.Contagens.AsNoTracking().SingleAsync(x => x.Id == id);
    Assert.Equal(c.PecaId, lida.EstruturaItemId);
    Assert.Equal(4.5m, lida.Conformes);
    Assert.Equal(2m, lida.NaoConformes);
    Assert.Equal(contagem.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
  });

  [Fact]
  public Task Contagem_vazia_viola_o_CHECK() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Contagem(c, 0m, 0m), "CK_ContagemDimensional_AlgumaUnidade"));

  [Fact]
  public Task Liberacao_faz_a_volta_completa_do_pedido_e_da_resposta() => NoCenarioAsync(async c =>
  {
    var pedido = Pedido(c, 3m, "Cliente aceita a rebarba");
    var pedidoId = await GravarAsync(pedido);
    var respostaId = await GravarAsync(Resposta(c, pedidoId, 0m));

    await using var leitura = NovoContexto();
    var lido = await leitura.Liberacoes.AsNoTracking().SingleAsync(x => x.Id == pedidoId);
    Assert.Equal(TiposDeLiberacao.Pedido, lido.Tipo);
    Assert.Equal(3m, lido.Quantidade);
    Assert.Equal("Cliente aceita a rebarba", lido.Texto);
    Assert.Null(lido.PedidoDeLiberacaoId);
    Assert.Equal(pedido.DataHora, lido.DataHora, TimeSpan.FromSeconds(1));

    var lida = await leitura.Liberacoes.AsNoTracking().SingleAsync(x => x.Id == respostaId);
    Assert.Equal(TiposDeLiberacao.Resposta, lida.Tipo);
    Assert.Equal(pedidoId, lida.PedidoDeLiberacaoId);
    Assert.Equal(0m, lida.Quantidade);
    Assert.Null(lida.Texto);
  });

  [Fact]
  public Task Resposta_sem_pedido_viola_o_CHECK() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Resposta(c, pedidoId: null), "CK_LiberacaoDimensional_RespostaCoerente"));

  [Fact]
  public Task Pedido_de_liberacao_sem_texto_viola_o_CHECK() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(Pedido(c, texto: null), "CK_LiberacaoDimensional_PedidoCoerente"));

  [Fact]
  public Task Remessa_faz_a_volta_completa_e_a_linha_aponta_para_ela() => NoCenarioAsync(async c =>
  {
    var remessa = new Remessa { DataHora = DateTime.UtcNow, UsuarioId = c.Arvore.AutorId };
    var remessaId = await GravarAsync(remessa);
    c.Arvore.RemessaIds.Add(remessaId);
    var linha = Linha(c, TiposDeMovimentacao.Expedicao, Posicoes.NaExpedicao, null, Posicoes.Expedido, null);
    linha.RemessaId = remessaId;
    var linhaId = await GravarAsync(linha);

    await using var leitura = NovoContexto();
    var lida = await leitura.Remessas.AsNoTracking().SingleAsync(r => r.Id == remessaId);
    Assert.Equal(remessa.DataHora, lida.DataHora, TimeSpan.FromSeconds(1));
    Assert.Equal(c.Arvore.AutorId, lida.UsuarioId);
    Assert.Equal(remessaId, (await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == linhaId)).RemessaId);
  });

  [Theory]
  [InlineData("Medicao", "NaExpedicao", null, "Conforme", null)]
  [InlineData("Medicao", "NaExpedicao", null, "NaoConforme", null)]
  [InlineData("Conserto", "NaoConforme", null, "AguardandoConserto", 1)]
  [InlineData("Entrega", "AguardandoConserto", 1, "EmConserto", 1)]
  [InlineData("Termino", "EmConserto", 1, "ConsertoPronto", 1)]
  [InlineData("Entrega", "ConsertoPronto", 1, "NaExpedicao", null)]
  [InlineData("PedidoDeLiberacao", "NaoConforme", null, "AguardandoLiberacao", null)]
  [InlineData("Liberacao", "AguardandoLiberacao", null, "Liberada", null)]
  [InlineData("Recusa", "AguardandoLiberacao", null, "NaoConforme", null)]
  [InlineData("Expedicao", "Liberada", null, "Expedido", null)]
  [InlineData("Expedicao", "Conforme", null, "Expedido", null)]
  [InlineData("Expedicao", "NaExpedicao", null, "Expedido", null)]
  [InlineData("Expedicao", "NaoConforme", null, "Expedido", null)]
  public Task Transicao_nova_aceita(string tipo, string de, int? deSetor, string para, int? paraSetor) =>
      NoCenarioAsync(async c =>
      {
        var id = await GravarLinhaAsync(c, Linha(c, tipo, de, deSetor, para, paraSetor));

        await using var leitura = NovoContexto();
        var lida = await leitura.Movimentacoes.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Equal((tipo, de, SetorDe(c, deSetor), para, SetorDe(c, paraSetor)),
            (lida.Tipo, lida.OrigemPosicao, lida.OrigemSetorId, lida.DestinoPosicao, lida.DestinoSetorId));
      });

  [Theory]
  [InlineData("Medicao", "NaExpedicao", null, "Liberada", null)]
  [InlineData("Expedicao", "AguardandoLiberacao", null, "Expedido", null)]
  [InlineData("Termino", "EmConserto", 1, "ConsertoPronto", 2)]
  [InlineData("Conserto", "NaoConforme", null, "EmConserto", 1)]
  [InlineData("Entrega", "ConsertoPronto", 1, "Conforme", null)]
  public Task Transicao_nova_recusada(string tipo, string de, int? deSetor, string para, int? paraSetor) =>
      NoCenarioAsync(async c =>
      {
        var linha = Linha(c, tipo, de, deSetor, para, paraSetor);
        await ComAgrupadorAsync(c, linha);
        await AfirmarRecusaAsync(linha, "CK_Movimentacao_Transicao");
      });

  [Fact]
  public Task Medicao_sem_ContagemId_viola_CK_Movimentacao_RegistroCoerente() => NoCenarioAsync(c =>
      AfirmarRecusaAsync(
          Linha(c, TiposDeMovimentacao.Medicao, Posicoes.NaExpedicao, null, Posicoes.Conforme, null),
          "CK_Movimentacao_RegistroCoerente"));

  [Fact]
  public Task Duas_colunas_de_registro_violam_CK_Movimentacao_RegistroCoerente() => NoCenarioAsync(async c =>
  {
    var linha = Linha(c, TiposDeMovimentacao.Expedicao, Posicoes.NaExpedicao, null, Posicoes.Expedido, null);
    linha.RemessaId = await NovaRemessaAsync(c);
    linha.ContagemId = await GravarAsync(Contagem(c, 1m, 1m));

    await AfirmarRecusaAsync(linha, "CK_Movimentacao_RegistroCoerente");
  });

  [Fact]
  public Task Agrupador_fora_do_seu_tipo_viola_CK_Movimentacao_RegistroCoerente() => NoCenarioAsync(async c =>
  {
    // Uma Entrega de sempre que aponta uma remessa: a coluna so aparece na Expedicao ou no Estorno dela.
    var linha = Linha(c, TiposDeMovimentacao.Entrega, Posicoes.ConsertoPronto, 1, Posicoes.NaExpedicao, null);
    linha.RemessaId = await NovaRemessaAsync(c);

    await AfirmarRecusaAsync(linha, "CK_Movimentacao_RegistroCoerente");
  });

  [Fact]
  public Task Expedido_entra_em_Livro_SomarSaldos_igual_ao_banco() => NoCenarioAsync(async c =>
  {
    var entrega = Linha(c, TiposDeMovimentacao.Entrega, Posicoes.AguardandoColeta, 1, Posicoes.NaExpedicao, null, 4m);
    entrega.OrigemOrdem = 1;
    await GravarAsync(entrega);
    await GravarLinhaAsync(c, Linha(c, TiposDeMovimentacao.Expedicao, Posicoes.NaExpedicao, null, Posicoes.Expedido, null, 3m));

    await using var leitura = NovoContexto();
    var movimentos = await leitura.Movimentacoes.AsNoTracking().Where(m => m.EstruturaItemId == c.PecaId).ToListAsync();
    var repositorio = new ExecucaoRepository(leitura);
    var doBanco = await repositorio.ListarSaldosAsync([c.PecaId], CancellationToken.None);

    Assert.Equal(Livro.SomarSaldos(movimentos), doBanco);
    Assert.Contains(new SaldoLiquido(c.PecaId, Posicoes.NaExpedicao, null, null, 1m), doBanco);   // 4 - 3
    Assert.Contains(new SaldoLiquido(c.PecaId, Posicoes.Expedido, null, null, 3m), doBanco);
  });
}
