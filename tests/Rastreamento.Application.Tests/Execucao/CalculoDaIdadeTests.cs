using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Execucao;

public class CalculoDaIdadeTests
{
  private const int Corte = 3;
  private static readonly DateTime Dia1 = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

  private static DateTime Dia(int n) => Dia1.AddDays(n - 1);

  private static int _id;

  private static Movimentacao Mov(
      int no, string tipo, Local de, Local para, decimal q, DateTime quando, int? id = null, int? estornoDe = null) => new()
  {
    Id = id ?? ++_id + 1000, EstruturaItemId = no, Tipo = tipo, Quantidade = q, DataHora = quando, EstornoDeId = estornoDe,
    OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
    DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
  };

  private static Movimentacao Inverso(Movimentacao m, DateTime quando) =>
      Mov(m.EstruturaItemId, TiposDeMovimentacao.Estorno, Local.DoDestino(m), Local.DaOrigem(m), m.Quantidade, quando,
          estornoDe: m.Id);

  private static NoDaIdade No(int id, decimal q = 0m, DateTime? criado = null) => new(id, q, criado ?? Dia(1));

  private static IdadesDoLivro Calcular(NoDaIdade[] nos, params Movimentacao[] linhas) =>
      CalculoDaIdade.Calcular(nos, linhas);

  private static Movimentacao Chega(int no, decimal q, DateTime quando, int? id = null) =>
      Mov(no, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), q, quando, id);

  private static Movimentacao Termina(int no, decimal q, DateTime quando, int? id = null) =>
      Mov(no, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), q, quando, id);

  private static IEnumerable<ParteViva> NoSetor(IdadesDoLivro r, int no) =>
      r.Partes(no).Where(p => p.Local == Local.NoSetor(Corte, 1));

  [Fact]
  public void Chegada_sem_saida_fica_viva_com_a_data_dela()
  {
    var r = Calcular([No(1)], Chega(1, 5m, Dia(1)));

    var parte = Assert.Single(NoSetor(r, 1));
    Assert.Equal(5m, parte.Quantidade);
    Assert.Equal(Dia(1), parte.Chegada);
  }

  [Fact]
  public void Saida_consome_a_chegada_mais_antiga()
  {
    var r = Calcular([No(1)], Chega(1, 5m, Dia(1)), Chega(1, 5m, Dia(3)), Termina(1, 5m, Dia(4)));

    var parte = Assert.Single(NoSetor(r, 1));
    Assert.Equal(Dia(3), parte.Chegada);
    Assert.Equal(5m, parte.Quantidade);
  }

  [Fact]
  public void Saida_divide_a_chegada()
  {
    var r = Calcular([No(1)], Chega(1, 10m, Dia(1)), Termina(1, 4m, Dia(2)));

    var parte = Assert.Single(NoSetor(r, 1));
    Assert.Equal(6m, parte.Quantidade);
    Assert.Equal(Dia(1), parte.Chegada);
  }

  [Fact]
  public void Saida_maior_que_a_primeira_chegada_consome_a_segunda()
  {
    var r = Calcular([No(1)], Chega(1, 3m, Dia(1)), Chega(1, 5m, Dia(2)), Termina(1, 4m, Dia(3)));

    var parte = Assert.Single(NoSetor(r, 1));
    Assert.Equal(4m, parte.Quantidade);
    Assert.Equal(Dia(2), parte.Chegada);
  }

  [Fact]
  public void Estorno_de_chegada_tira_a_chegada()
  {
    var termino = Termina(1, 5m, Dia(2), id: 20);
    var entrega = Mov(1, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NoSetor(Corte, 2), 5m, Dia(3), id: 30);
    var r = Calcular(
        [No(1)], Chega(1, 5m, Dia(1), id: 10), termino, entrega, Inverso(entrega, Dia(4)));

    Assert.DoesNotContain(r.Partes(1), p => p.Local == Local.NoSetor(Corte, 2));
    var coleta = Assert.Single(r.Partes(1), p => p.Local == Local.AguardandoColeta(Corte, 1));
    Assert.Equal(5m, coleta.Quantidade);
    Assert.Equal(Dia(2), coleta.Chegada);
  }

  [Fact]
  public void Estorno_de_saida_e_refeito_sobre_o_livro_liquido()
  {
    var primeiro = Termina(1, 5m, Dia(4), id: 30);
    var r = Calcular(
        [No(1)],
        Chega(1, 5m, Dia(1), id: 10), Chega(1, 5m, Dia(3), id: 20),
        primeiro, Termina(1, 5m, Dia(5), id: 40), Inverso(primeiro, Dia(6)));

    var parte = Assert.Single(NoSetor(r, 1));
    Assert.Equal(5m, parte.Quantidade);
    Assert.Equal(Dia(3), parte.Chegada);
  }

  [Fact]
  public void A_iniciar_nasce_na_data_do_agrupamento()
  {
    var r = Calcular([No(1, 10m, Dia(1))], Chega(1, 4m, Dia(2)));

    var parte = Assert.Single(r.Partes(1), p => p.Local == Local.AIniciar);
    Assert.Equal(6m, parte.Quantidade);
    Assert.Equal(Dia(1), parte.Chegada);
  }

  [Fact]
  public void Terminais_nao_guardam_partes()
  {
    var r = Calcular(
        [No(1)],
        Mov(1, TiposDeMovimentacao.Expedicao, Local.Conforme, Local.Expedido, 3m, Dia(2)));

    Assert.DoesNotContain(r.Partes(1), p => p.Local == Local.Expedido);
    Assert.Empty(r.Partes(1));
  }

  [Fact]
  public void Mesmo_DataHora_desempata_pelo_Id()
  {
    // Tres linhas no mesmo instante: chegadas de 3 (Id 7) e de 5 (Id 8) e uma saida de 3 (Id 9). Pelo Id, a saida vem
    // depois das duas chegadas e consome a de 3: sobra so a de 5. Fora dessa ordem a saida seria ignorada (ou
    // consumiria a de 5) e o resultado mudaria.
    var tres = Chega(1, 3m, Dia(2), id: 7);
    var cinco = Chega(1, 5m, Dia(2), id: 8);
    var saida = Termina(1, 3m, Dia(2), id: 9);

    foreach (var ordem in new[]
             {
               new[] { tres, cinco, saida }, new[] { saida, cinco, tres }, new[] { cinco, saida, tres },
               new[] { saida, tres, cinco }, new[] { cinco, tres, saida }, new[] { tres, saida, cinco },
             })
    {
      var r = CalculoDaIdade.Calcular([No(1)], ordem);
      Assert.Equal(5m, Assert.Single(NoSetor(r, 1)).Quantidade);
      Assert.Equal(3m, Assert.Single(r.Partes(1), p => p.Local == Local.AguardandoColeta(Corte, 1)).Quantidade);
    }
  }

  [Fact]
  public void Quantidade_decimal_se_divide_sem_perder_fracao()
  {
    var r = Calcular([No(1)], Chega(1, 2.5m, Dia(1)), Termina(1, 0.75m, Dia(2)));

    Assert.Equal(1.75m, Assert.Single(NoSetor(r, 1)).Quantidade);
  }

  [Fact]
  public void Permanencia_media_e_ponderada_pela_quantidade()
  {
    var r = Calcular(
        [No(1)],
        Chega(1, 6m, Dia(1)), Chega(1, 2m, Dia(1).AddDays(0)),
        Termina(1, 6m, Dia(2)), Termina(1, 2m, Dia(4)));

    // 6 un chegaram no dia 1 e saem no dia 2 (1 d); 2 un chegaram no dia 1 e saem no dia 4 (3 d): (6*1 + 2*3)/8 = 1,5 d.
    Assert.Equal(TimeSpan.FromHours(36), r.PermanenciaMedia(1, 1));
  }

  [Fact]
  public void Permanencia_sem_nenhuma_e_nula()
  {
    var r = Calcular([No(1)], Chega(1, 5m, Dia(1)));

    Assert.Null(r.PermanenciaMedia(1, 1));
  }

  [Fact]
  public void Saida_por_baixa_nao_conta_como_permanencia()
  {
    var r = Calcular(
        [No(1)],
        Chega(1, 5m, Dia(1)),
        Mov(1, TiposDeMovimentacao.Perda, Local.NoSetor(Corte, 1), Local.Perdido, 5m, Dia(3)));

    Assert.Null(r.PermanenciaMedia(1, 1));
    Assert.Empty(r.Partes(1));
  }

  [Fact]
  public void MaisAntiga_filtra_pelo_lugar()
  {
    var r = Calcular([No(1, 4m, Dia(1))], Chega(1, 2m, Dia(5)));

    Assert.Equal(Dia(5), r.MaisAntiga(1, l => l.Posicao == Posicoes.NoSetor));
    Assert.Equal(Dia(1), r.MaisAntiga(1, l => true));
    Assert.Null(r.MaisAntiga(1, l => l.Posicao == Posicoes.EmConserto));
  }

  [Fact]
  public void Soma_das_partes_e_o_saldo_da_calculadora()
  {
    const int Solda = 4, Pai = 1, Filho = 2;
    var estornada = Mov(Pai, TiposDeMovimentacao.Termino, Local.NoSetor(Solda, 1), Local.AguardandoColeta(Solda, 1), 1m, Dia(9), id: 90);
    var linhas = new[]
    {
      Mov(Filho, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 10m, Dia(2)),
      Mov(Filho, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 8m, Dia(3)),
      Mov(Filho, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.AguardandoMontagem(Solda), 6m, Dia(4)),
      Mov(Filho, TiposDeMovimentacao.Montagem, Local.AguardandoMontagem(Solda), Local.Montado, 4m, Dia(5)),
      Mov(Pai, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Solda, 1), 2m, Dia(5)),
      Mov(Pai, TiposDeMovimentacao.Termino, Local.NoSetor(Solda, 1), Local.AguardandoColeta(Solda, 1), 2m, Dia(6)),
      Mov(Pai, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Solda, 1), Local.NaExpedicao, 2m, Dia(7)),
      Mov(Pai, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.NaoConforme, 1m, Dia(8)),
      Mov(Pai, TiposDeMovimentacao.Conserto, Local.NaoConforme, Local.AguardandoConserto(Solda), 1m, Dia(8).AddHours(1)),
      Mov(Pai, TiposDeMovimentacao.Medicao, Local.NaExpedicao, Local.Conforme, 1m, Dia(8).AddHours(2)),
      Mov(Pai, TiposDeMovimentacao.Expedicao, Local.Conforme, Local.Expedido, 1m, Dia(9)),
      estornada,
    };
    var todas = linhas.Append(Inverso(estornada, Dia(10))).ToList();
    var nos = new[] { No(Pai, 5m, Dia(1)), No(Filho, 10m, Dia(1)) };

    var idades = CalculoDaIdade.Calcular(nos, todas);
    var calc = new CalculadoraDeExecucao(
        [new NoDoCalculo(Pai, null, 5m, null, []), new NoDoCalculo(Filho, Pai, 10m, 2m, [])],
        Livro.SomarSaldos(todas), new Dictionary<int, decimal>(), []);

    var terminais = new[] { Posicoes.Montado, Posicoes.Expedido, Posicoes.Perdido };
    foreach (var no in nos)
    {
      var porLugar = idades.Partes(no.Id).GroupBy(p => p.Local).ToDictionary(g => g.Key, g => g.Sum(p => p.Quantidade));
      var lugares = porLugar.Keys.Concat(calc.Saldos(no.Id).Select(s => s.Local)).Distinct()
          .Where(l => !terminais.Contains(l.Posicao));
      foreach (var lugar in lugares)
        Assert.Equal(calc.Saldo(no.Id, lugar), porLugar.GetValueOrDefault(lugar));
    }
  }
}
