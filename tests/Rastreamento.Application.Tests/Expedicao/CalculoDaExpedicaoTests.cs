using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Expedicao;

/// <summary>
/// As regras puras do fim do caminho da Peca: a afericao valida (secao 3.4 da spec da Fase 5A), a tabela
/// 3.2 (o que e a unidade em `NaExpedicao`), as posicoes do local de expedicao, a ordem em que a remessa
/// tira (C10) e os avisos de expedir (D8). Dados montados a mao, sem repositorio.
/// </summary>
public class CalculoDaExpedicaoTests
{
  private const int Peca = 42;
  private static readonly DateTime T0 = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

  private static RelatorioDimensional Afericao(int id, string resultado, int minutos) => new()
  {
    Id = id, EstruturaItemId = Peca, Tipo = TiposDeAfericao.Afericao, Resultado = resultado,
    DataHora = T0.AddMinutes(minutos), UsuarioId = 13,
  };

  private static RelatorioDimensional EstornoDe(int id, int original, int minutos) => new()
  {
    Id = id, EstruturaItemId = Peca, Tipo = TiposDeAfericao.Estorno, EstornoDeId = original,
    DataHora = T0.AddMinutes(minutos), UsuarioId = 13,
  };

  private static PosicoesDaPeca Pos(
      decimal naExpedicao = 0, decimal conforme = 0, decimal naoConforme = 0, decimal aguardandoLiberacao = 0,
      decimal liberada = 0, decimal emConserto = 0, decimal expedido = 0) =>
      new(naExpedicao, conforme, naoConforme, aguardandoLiberacao, liberada, emConserto, expedido);

  [Fact]
  public void Afericao_valida_e_a_mais_recente_sem_estorno()
  {
    var a1 = Afericao(1, ResultadosDeAfericao.NaoConforme, 0);
    var e1 = EstornoDe(2, original: 1, 5);
    var a2 = Afericao(3, ResultadosDeAfericao.Conforme, 10);

    Assert.Same(a2, CalculoDaExpedicao.AfericaoValida([a1, e1, a2]));
    // A ordem de chegada da lista nao decide: a mais recente e por DataHora.
    Assert.Same(a2, CalculoDaExpedicao.AfericaoValida([a2, e1, a1]));
    Assert.Null(CalculoDaExpedicao.AfericaoValida([a1, e1]));
  }

  [Fact]
  public void Afericao_valida_nunca_e_o_proprio_estorno_e_sem_registro_e_nula()
  {
    Assert.Null(CalculoDaExpedicao.AfericaoValida([]));
    // O estorno e a linha mais recente, mas nao e afericao: com a original estornada, nao ha valida.
    Assert.Null(CalculoDaExpedicao.AfericaoValida([Afericao(1, ResultadosDeAfericao.Conforme, 0), EstornoDe(2, 1, 99)]));
  }

  [Fact]
  public void Afericao_estornada_mais_recente_cede_a_anterior_ainda_valida()
  {
    // Nao acontece pela rota (so se afere sem valida), mas a regra e "a ultima nao estornada", nao "a ultima".
    var a1 = Afericao(1, ResultadosDeAfericao.Conforme, 0);
    var a2 = Afericao(2, ResultadosDeAfericao.NaoConforme, 10);
    var e2 = EstornoDe(3, original: 2, 20);

    Assert.Same(a1, CalculoDaExpedicao.AfericaoValida([a1, a2, e2]));
  }

  [Fact]
  public void Entre_duas_nao_estornadas_vale_a_mais_recente_e_o_Id_desempata()
  {
    // A rota nao deixa nascer duas validas (estorne a anterior primeiro), mas a regra e "a ultima", e a conta
    // tem de responder igual seja qual for a ordem da lista.
    var antiga = Afericao(1, ResultadosDeAfericao.NaoConforme, 0);
    var recente = Afericao(2, ResultadosDeAfericao.Conforme, 10);
    Assert.Same(recente, CalculoDaExpedicao.AfericaoValida([recente, antiga]));
    Assert.Same(recente, CalculoDaExpedicao.AfericaoValida([antiga, recente]));

    var mesmoInstante = Afericao(3, ResultadosDeAfericao.NaoConforme, 10);
    Assert.Same(mesmoInstante, CalculoDaExpedicao.AfericaoValida([mesmoInstante, recente]));
    Assert.Same(mesmoInstante, CalculoDaExpedicao.AfericaoValida([recente, mesmoInstante]));
  }

  [Theory]
  [InlineData(false, null, SituacaoDaAfericao.SemRelatorio)]
  [InlineData(false, "NaoConforme", SituacaoDaAfericao.SemRelatorio)] // marca desligada vence
  [InlineData(true, null, SituacaoDaAfericao.Pendente)]
  [InlineData(true, "Conforme", SituacaoDaAfericao.Conforme)]
  [InlineData(true, "NaoConforme", SituacaoDaAfericao.NaoConforme)]
  public void Situacao_segue_a_tabela(bool requer, string? resultado, SituacaoDaAfericao esperada)
  {
    var valida = resultado is null ? null : Afericao(1, resultado, 0);

    Assert.Equal(esperada, CalculoDaExpedicao.Situacao(requer, valida));
  }

  [Fact]
  public void Posicoes_soma_as_tres_do_conserto_em_EmConserto_e_ignora_as_de_producao()
  {
    var saldos = new List<(Local, decimal)>
    {
      (Local.AIniciar, 100), (Local.NoSetor(1, 1), 200), (Local.AguardandoColeta(1, 1), 300),
      (Local.AguardandoMontagem(2), 400), (Local.Montado, 500),
      (Local.NaExpedicao, 1), (Local.Conforme, 2), (Local.NaoConforme, 3), (Local.AguardandoLiberacao, 4),
      (Local.Liberada, 5),
      (Local.AguardandoConserto(7), 10), (Local.EmConserto(7), 20), (Local.ConsertoPronto(8), 30),
      (Local.Expedido, 6),
      // Mesma posicao em duas linhas (dois Setores de conserto, por exemplo) soma.
      (Local.EmConserto(9), 40), (Local.NaExpedicao, 1),
    };

    var p = CalculoDaExpedicao.Posicoes(saldos);

    Assert.Equal(Pos(naExpedicao: 2, conforme: 2, naoConforme: 3, aguardandoLiberacao: 4, liberada: 5,
        emConserto: 100, expedido: 6), p);
    Assert.Equal(2 + 2 + 3 + 4 + 5, p.NoLocalDeExpedicao);
    Assert.Equal(5 + 2 + 2 + 3, p.Expedivel);
  }

  [Fact]
  public void Tirar_segue_Liberada_Conforme_NaExpedicao_NaoConforme()
  {
    var p = Pos(liberada: 1, conforme: 2, naExpedicao: 3, naoConforme: 4);

    Assert.Equal(
        [new Tirada(Posicoes.Liberada, 1), new Tirada(Posicoes.Conforme, 2), new Tirada(Posicoes.NaExpedicao, 3)],
        CalculoDaExpedicao.Tirar(p, 6));
    Assert.Equal(
        [new Tirada(Posicoes.Liberada, 1), new Tirada(Posicoes.Conforme, 2), new Tirada(Posicoes.NaExpedicao, 3),
         new Tirada(Posicoes.NaoConforme, 4)],
        CalculoDaExpedicao.Tirar(p, 10));
    // Tirada parcial no meio da ordem: o resto da posicao fica.
    Assert.Equal(
        [new Tirada(Posicoes.Liberada, 1), new Tirada(Posicoes.Conforme, 1)],
        CalculoDaExpedicao.Tirar(p, 2));
  }

  [Fact]
  public void Tirar_pula_posicao_vazia()
  {
    var p = Pos(naExpedicao: 0, conforme: 0, liberada: 0, naoConforme: 3);

    Assert.Equal([new Tirada(Posicoes.NaoConforme, 2)], CalculoDaExpedicao.Tirar(p, 2));
  }

  [Fact]
  public void Tirar_nunca_tira_AguardandoLiberacao()
  {
    var p = Pos(aguardandoLiberacao: 5);

    Assert.Equal(0, p.Expedivel);
    Assert.Equal(5, p.NoLocalDeExpedicao);
    Assert.Null(CalculoDaExpedicao.Tirar(p, 1));
  }

  [Fact]
  public void Tirar_acima_do_expedivel_devolve_null()
  {
    // Conserto e expedido tambem nao saem: o expedivel aqui e 1 + 2 + 3 + 4 = 10.
    var p = Pos(liberada: 1, conforme: 2, naExpedicao: 3, naoConforme: 4, aguardandoLiberacao: 5, emConserto: 6,
        expedido: 7);

    Assert.NotNull(CalculoDaExpedicao.Tirar(p, 10));
    Assert.Null(CalculoDaExpedicao.Tirar(p, 10.001m));
    Assert.Null(CalculoDaExpedicao.Tirar(p, 11));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Tirar_quantidade_nao_positiva_e_erro_de_quem_chama(decimal quantidade)
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => CalculoDaExpedicao.Tirar(Pos(naExpedicao: 3), quantidade));
  }

  [Theory]
  [InlineData(SituacaoDaAfericao.SemRelatorio, "NaExpedicao", null)]
  [InlineData(SituacaoDaAfericao.Pendente, "NaExpedicao", "SemAfericao")]
  [InlineData(SituacaoDaAfericao.Conforme, "NaExpedicao", null)]
  [InlineData(SituacaoDaAfericao.NaoConforme, "NaExpedicao", "SemMedicao")]
  [InlineData(SituacaoDaAfericao.NaoConforme, "NaoConforme", "NaoConformeSemLiberacao")]
  [InlineData(SituacaoDaAfericao.NaoConforme, "Liberada", null)]
  [InlineData(SituacaoDaAfericao.NaoConforme, "Conforme", null)]
  public void Aviso_por_situacao_e_origem(SituacaoDaAfericao s, string origem, string? aviso)
  {
    var avisos = CalculoDaExpedicao.Avisos(s, [new Tirada(origem, 3)]);

    if (aviso is null) Assert.Empty(avisos);
    else Assert.Equal([new AvisoDeExpedicao(aviso, 3)], avisos);
  }

  [Fact]
  public void Avisos_agrega_por_tipo_e_nao_devolve_tipo_com_zero()
  {
    var avisos = CalculoDaExpedicao.Avisos(SituacaoDaAfericao.NaoConforme,
    [
      new Tirada(Posicoes.Liberada, 1), new Tirada(Posicoes.NaExpedicao, 2), new Tirada(Posicoes.NaoConforme, 4),
      new Tirada(Posicoes.NaExpedicao, 0.5m), new Tirada(Posicoes.NaoConforme, 0),
    ]);

    Assert.Equal(
        [new AvisoDeExpedicao(TiposDeAviso.SemMedicao, 2.5m), new AvisoDeExpedicao(TiposDeAviso.NaoConformeSemLiberacao, 4)],
        avisos);
    // Tirada de zero nao vira aviso de zero.
    Assert.Empty(CalculoDaExpedicao.Avisos(SituacaoDaAfericao.Pendente, [new Tirada(Posicoes.NaExpedicao, 0)]));
  }

  [Fact]
  public void Tipos_de_aviso_sao_as_strings_do_contrato_JSON()
  {
    Assert.Equal("SemAfericao", TiposDeAviso.SemAfericao);
    Assert.Equal("SemMedicao", TiposDeAviso.SemMedicao);
    Assert.Equal("NaoConformeSemLiberacao", TiposDeAviso.NaoConformeSemLiberacao);
  }
}
