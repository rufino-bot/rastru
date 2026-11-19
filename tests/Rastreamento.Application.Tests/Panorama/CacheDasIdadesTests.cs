using Rastreamento.Application.Execucao;
using Rastreamento.Application.Panorama;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Panorama;

/// <summary>
/// O cache incremental das idades do Panorama: depois de qualquer sequencia de escritas, o resultado tem de ser o
/// de pareamento refeito do zero sobre o livro inteiro visivel (`CalculoDaIdade.Calcular`).
/// </summary>
public class CacheDasIdadesTests
{
  private const int NoA = 1, NoB = 2, NoC = 3;
  private static readonly CancellationToken Ct = CancellationToken.None;
  private static readonly DateTime Criacao = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
  private static readonly DateTime T0 = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

  private sealed class RelogioAjustavel(DateTimeOffset inicio) : TimeProvider
  {
    public DateTimeOffset Agora { get; set; } = inicio;
    public override DateTimeOffset GetUtcNow() => Agora;
  }

  private sealed class Banca
  {
    public FakeExecucaoRepo Execucao { get; } = new(new FakeEstruturaRepo());
    public RelogioAjustavel Relogio { get; } = new(new DateTimeOffset(T0, TimeSpan.Zero));
    public CacheDasIdades Cache { get; }
    public List<NoDaIdade> Nos { get; } = [];

    public Banca(params int[] nos)
    {
      Cache = new CacheDasIdades(Relogio);
      foreach (var n in nos) Nos.Add(new NoDaIdade(n, 10m, Criacao));
    }

    public Task<IdadesDoLivro> ObterAsync() => Cache.ObterAsync(Nos.ToList(), Execucao, Ct);

    public Movimentacao Escrever(
        int no, string tipo, Local de, Local para, decimal quantidade, DateTime dataHora, int? estornoDeId = null,
        int? id = null)
    {
      var m = new Movimentacao
      {
        EstruturaItemId = no, Tipo = tipo, Quantidade = quantidade,
        OrigemPosicao = de.Posicao, OrigemSetorId = de.SetorId, OrigemOrdem = de.Ordem,
        DestinoPosicao = para.Posicao, DestinoSetorId = para.SetorId, DestinoOrdem = para.Ordem,
        DataHora = dataHora, UsuarioId = 1, EstornoDeId = estornoDeId,
      };
      if (id is int fixo)
      {
        m.Id = fixo;
        Execucao.Movimentacoes.Add(m);
        return m;
      }
      return Execucao.Semear(m);
    }

    public Movimentacao Inicio(int no, decimal q, DateTime t) =>
        Escrever(no, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(1, 1), q, t);

    public Movimentacao Termino(int no, decimal q, DateTime t) =>
        Escrever(no, TiposDeMovimentacao.Termino, Local.NoSetor(1, 1), Local.AguardandoColeta(1, 1), q, t);

    public Movimentacao Entrega(int no, decimal q, DateTime t) =>
        Escrever(no, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(1, 1), Local.NoSetor(2, 2), q, t);

    public Movimentacao Estornar(Movimentacao original, DateTime t) =>
        Escrever(
            original.EstruturaItemId, TiposDeMovimentacao.Estorno, Local.DoDestino(original), Local.DaOrigem(original),
            original.Quantidade, t, estornoDeId: original.Id);

    /// <summary>O pareamento refeito do zero sobre todo o livro visivel.</summary>
    public IdadesDoLivro DoZero() => CalculoDaIdade.Calcular(
        Nos, Execucao.Movimentacoes.Where(m => !Execucao.Ocultas.Contains(m.Id)).OrderBy(m => m.Id).ToList());

    public void ConfereComODoZero(IdadesDoLivro obtido)
    {
      var esperado = DoZero();
      foreach (var no in Nos)
      {
        Assert.Equal(esperado.Partes(no.Id), obtido.Partes(no.Id));
        for (var ordem = 1; ordem <= 4; ordem++)
          Assert.Equal(esperado.PermanenciaMedia(no.Id, ordem), obtido.PermanenciaMedia(no.Id, ordem));
      }
    }
  }

  [Fact]
  public async Task Primeira_chamada_le_o_livro_inteiro()
  {
    var b = new Banca(NoA, NoB);
    b.Inicio(NoA, 4m, T0);
    b.Inicio(NoB, 2m, T0.AddMinutes(1));
    b.Termino(NoA, 1m, T0.AddMinutes(30));

    var idades = await b.ObterAsync();

    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(idades);
  }

  [Fact]
  public async Task Sem_linha_nova_nao_le_o_livro_de_novo()
  {
    var b = new Banca(NoA);
    b.Inicio(NoA, 4m, T0);

    await b.ObterAsync();
    var segunda = await b.ObterAsync();

    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task Linha_nova_entra_sem_leitura_completa()
  {
    var b = new Banca(NoA, NoB);
    b.Inicio(NoA, 4m, T0);
    b.Inicio(NoB, 2m, T0);
    await b.ObterAsync();
    var leiturasDoIntervalo = b.Execucao.LeiturasDoIntervalo;

    b.Termino(NoA, 3m, T0.AddMinutes(45));
    var segunda = await b.ObterAsync();

    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    Assert.Equal(leiturasDoIntervalo + 1, b.Execucao.LeiturasDoIntervalo);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task No_que_sai_e_volta_da_o_mesmo_que_do_zero()
  {
    var b = new Banca(NoA, NoB);
    b.Inicio(NoA, 4m, T0);
    b.Inicio(NoB, 6m, T0);
    b.Termino(NoB, 2m, T0.AddMinutes(10));
    await b.ObterAsync();

    var noB = b.Nos.Single(n => n.Id == NoB);
    b.Nos.Remove(noB);
    b.Inicio(NoA, 1m, T0.AddMinutes(20));
    await b.ObterAsync();

    b.Nos.Add(noB);
    b.ConfereComODoZero(await b.ObterAsync());
  }

  [Fact]
  public async Task Estorno_novo_refaz_o_no()
  {
    var b = new Banca(NoA);
    var inicio = b.Inicio(NoA, 4m, T0);
    var antes = await b.ObterAsync();
    Assert.Contains(antes.Partes(NoA), p => p.Local.Posicao == Posicoes.NoSetor);

    b.Estornar(inicio, T0.AddMinutes(5));
    var depois = await b.ObterAsync();

    Assert.DoesNotContain(depois.Partes(NoA), p => p.Local.Posicao == Posicoes.NoSetor);
    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(depois);
  }

  [Fact]
  public async Task Linha_que_comita_atrasada_dentro_da_janela_entra()
  {
    var b = new Banca(NoA, NoB);
    b.Escrever(NoA, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(1, 1), 3m, T0, id: 100);
    b.Escrever(NoB, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(1, 1), 2m, T0, id: 101);
    b.Execucao.Ocultas.Add(100);
    var primeira = await b.ObterAsync();
    Assert.DoesNotContain(primeira.Partes(NoA), p => p.Local.Posicao == Posicoes.NoSetor);

    b.Execucao.Ocultas.Remove(100);
    var segunda = await b.ObterAsync();

    Assert.Contains(segunda.Partes(NoA), p => p.Local.Posicao == Posicoes.NoSetor);
    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task Quantidade_alterada_refaz_o_no()
  {
    var b = new Banca(NoA);
    b.Inicio(NoA, 4m, T0);
    await b.ObterAsync();

    b.Nos[0] = b.Nos[0] with { Quantidade = 25m };
    var segunda = await b.ObterAsync();

    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
    Assert.Equal(21m, segunda.Partes(NoA).Single(p => p.Local.Posicao == Posicoes.AIniciar).Quantidade);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task No_novo_le_so_as_linhas_dele()
  {
    var b = new Banca(NoA);
    b.Inicio(NoA, 4m, T0);
    b.Inicio(NoB, 2m, T0.AddMinutes(2));
    await b.ObterAsync();

    b.Nos.Add(new NoDaIdade(NoB, 10m, Criacao));
    var segunda = await b.ObterAsync();

    Assert.Equal([NoB], b.Execucao.NosDaUltimaLeituraDoLivro);
    Assert.Equal(2, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task No_que_saiu_some_das_idades()
  {
    var b = new Banca(NoA, NoB);
    b.Inicio(NoA, 4m, T0);
    b.Inicio(NoB, 2m, T0);
    var antes = await b.ObterAsync();
    Assert.NotEmpty(antes.Partes(NoB));

    b.Nos.RemoveAll(n => n.Id == NoB);
    var depois = await b.ObterAsync();

    Assert.Empty(depois.Partes(NoB));
    Assert.NotEmpty(depois.Partes(NoA));
    b.ConfereComODoZero(depois);
  }

  [Fact]
  public async Task Depois_de_dez_minutos_reconstroi_do_zero()
  {
    var b = new Banca(NoA);
    b.Inicio(NoA, 4m, T0);
    await b.ObterAsync();

    b.Relogio.Agora = b.Relogio.Agora + CacheDasIdades.ReconstrucaoCompleta;
    var segunda = await b.ObterAsync();

    Assert.Equal(2, b.Execucao.LeiturasDoLivroParaIdade);
    b.ConfereComODoZero(segunda);
  }

  [Fact]
  public async Task Sequencia_de_escritas_da_o_mesmo_que_do_zero()
  {
    var b = new Banca(NoA, NoB, NoC);
    var escritas = new List<Movimentacao>();
    DateTime T(int minuto) => T0.AddMinutes(minuto);

    // 30 operacoes fixas; a cada par, a segunda repete a `DataHora` da primeira (o desempate e pelo `Id`).
    var operacoes = new List<Func<Movimentacao>>
    {
      () => b.Inicio(NoA, 4m, T(1)),
      () => b.Inicio(NoB, 6m, T(1)),
      () => b.Inicio(NoC, 3m, T(2)),
      () => b.Termino(NoA, 2m, T(10)),
      () => b.Termino(NoB, 6m, T(10)),
      () => b.Entrega(NoA, 1m, T(12)),
      () => b.Estornar(escritas[3], T(13)),
      () => b.Inicio(NoA, 5m, T(14)),
      () => b.Termino(NoC, 3m, T(20)),
      () => b.Entrega(NoB, 4m, T(20)),
      () => b.Entrega(NoC, 3m, T(21)),
      () => b.Estornar(escritas[10], T(22)),
      () => b.Termino(NoA, 3m, T(30)),
      () => b.Termino(NoB, 1m, T(30)),
      () => b.Entrega(NoA, 2m, T(31)),
      () => b.Entrega(NoA, 1m, T(31)),
      () => b.Estornar(escritas[9], T(32)),
      () => b.Inicio(NoB, 2m, T(33)),
      () => b.Inicio(NoC, 1m, T(33)),
      () => b.Termino(NoC, 1m, T(40)),
      () => b.Termino(NoB, 2m, T(41)),
      () => b.Entrega(NoC, 1m, T(41)),
      () => b.Estornar(escritas[19], T(42)),
      () => b.Termino(NoA, 4m, T(50)),
      () => b.Entrega(NoB, 3m, T(50)),
      () => b.Estornar(escritas[7], T(51)),
      () => b.Inicio(NoA, 3m, T(52)),
      () => b.Termino(NoA, 1m, T(60)),
      () => b.Entrega(NoA, 1m, T(60)),
      () => b.Estornar(escritas[28], T(61)),
    };
    Assert.Equal(30, operacoes.Count);

    foreach (var operacao in operacoes)
    {
      escritas.Add(operacao());
      b.ConfereComODoZero(await b.ObterAsync());
    }

    Assert.Equal(1, b.Execucao.LeiturasDoLivroParaIdade);
  }
}
