using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// O Kit na calculadora (spec da Fase 3B, secoes 2 e 4.4). Cenario: Peca 1 de 10 que comeca na Solda; Item 2 de
/// 40 (razao 4) que termina no Corte; Item 3 de 10 (razao 1) que termina na Dobra.
/// </summary>
public class KitsDaColetaTests
{
  private static CenarioDeExecucao Cenario(bool kit = true, int setorDoPai = Solda)
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, setorDoPai);
    c.No(2, 1, 40m, 4m, Corte);
    c.No(3, 1, 10m, 1m, Dobra);
    if (kit) c.Kit();
    return c;
  }

  private static void Pronto(CenarioDeExecucao c, int item, int setor, decimal quantidade) =>
      c.Mover(item, TiposDeMovimentacao.Termino, Local.NoSetor(setor, 1), Local.AguardandoColeta(setor, 1), quantidade);

  /// <summary>
  /// O que aguarda montagem passou pela coleta: termina e entrega a mesma quantidade, para a entrega nao tirar da
  /// coleta o que `Pronto` pos la.
  /// </summary>
  private static void EmEspera(CenarioDeExecucao c, int item, int origem, int setor, decimal quantidade)
  {
    Pronto(c, item, origem, quantidade);
    c.Mover(item, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(origem, 1), Local.AguardandoMontagem(setor), quantidade);
  }

  [Fact]
  public void Kit_com_filhos_para_dois_conjuntos_e_montavel_com_dois()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 3m);

    var kit = Assert.Single(c.Calcular().KitsDaColeta());

    Assert.Equal(1, kit.PaiId);
    Assert.Equal(Solda, kit.SetorDeDestinoId);
    Assert.Equal(2m, kit.Conjuntos);
    Assert.True(kit.Montavel);
    Assert.Equal(new[] { (2, 8m, false), (3, 3m, false) }, kit.Filhos.Select(f => (f.FilhoId, f.Pronto, f.JaNoDestino)));
  }

  [Fact]
  public void Kit_que_nao_fecha_um_conjunto_e_incompleto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    var kit = Assert.Single(c.Calcular().KitsDaColeta());

    Assert.Equal(0m, kit.Conjuntos);
    Assert.False(kit.Montavel);
  }

  [Fact]
  public void Kit_sem_nenhum_filho_pronto_nao_aparece()
  {
    Assert.Empty(Cenario().Calcular().KitsDaColeta());
  }

  [Fact]
  public void Conjuntos_a_espera_contam_pelo_filho_mais_adiantado()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 12m);   // 3 conjuntos
    EmEspera(c, 3, Dobra, Solda, 2m);    // 2 conjuntos

    var calc = c.Calcular();

    Assert.Equal(3m, calc.ConjuntosAEspera(1));
    Assert.Equal(7m, calc.TetoDeEntrada(1));
  }

  [Fact]
  public void Conjunto_que_perdeu_parte_continua_a_espera()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 7m);    // 1,75 conjunto: arredonda para cima, 2
    EmEspera(c, 3, Dobra, Solda, 1m);    // 1 conjunto: o 2 e o mais adiantado so se arredondar para cima

    Assert.Equal(2m, c.Calcular().ConjuntosAEspera(1));
  }

  [Fact]
  public void Teto_desconta_o_total_montado()
  {
    var c = Cenario();
    c.Execucao.Montagens.Add(new Montagem
    {
      Id = 900, EstruturaItemId = 1, SetorId = Solda, Quantidade = 4m, DataHora = DateTime.UtcNow, UsuarioId = Operador,
    });

    Assert.Equal(6m, c.Calcular().TetoDeEntrada(1));
  }

  [Fact]
  public void Espera_em_Setor_sem_UtilizaKit_nao_conta()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Pintura, 8m);

    Assert.Equal(0m, c.Calcular().ConjuntosAEspera(1));
  }

  [Fact]
  public void O_que_esta_saindo_da_espera_sai_da_conta()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 8m);
    EmEspera(c, 3, Dobra, Solda, 2m);

    var saindo = new Dictionary<int, decimal> { [2] = 8m, [3] = 2m };

    Assert.Equal(0m, c.Calcular().ConjuntosAEspera(1, saindo));
    Assert.Equal(10m, c.Calcular().TetoDeEntrada(1, saindo));
  }

  [Fact]
  public void Kit_sem_teto_nao_aparece()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 40m);
    EmEspera(c, 3, Dobra, Solda, 10m);
    Pronto(c, 2, Corte, 4m);

    Assert.Empty(c.Calcular().KitsDaColeta());
  }

  [Fact]
  public void Conjuntos_nao_passam_do_teto()
  {
    var c = Cenario();
    EmEspera(c, 2, Corte, Solda, 36m);   // 9 conjuntos a espera; teto 1
    EmEspera(c, 3, Dobra, Solda, 9m);
    Pronto(c, 2, Corte, 8m);             // os prontos fecham 2: so o teto segura em 1
    Pronto(c, 3, Dobra, 2m);

    Assert.Equal(1m, Assert.Single(c.Calcular().KitsDaColeta()).Conjuntos);
  }

  [Fact]
  public void Filho_que_termina_no_Setor_do_pai_esta_no_destino()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 10m, 1m, Solda);
    c.Kit();
    Pronto(c, 2, Solda, 3m);

    Assert.True(Assert.Single(Assert.Single(c.Calcular().KitsDaColeta()).Filhos).JaNoDestino);
  }

  [Fact]
  public void Filho_de_Kit_no_ultimo_passo_sai_do_Item_pronto()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);

    Assert.DoesNotContain(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
  }

  [Fact]
  public void Filho_de_Avulso_continua_Item_pronto()
  {
    var c = Cenario(kit: false);
    Pronto(c, 2, Corte, 8m);

    Assert.Contains(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
    Assert.Empty(c.Calcular().KitsDaColeta());
  }

  [Fact]
  public void Filho_de_Avulso_que_vai_para_Setor_com_UtilizaKit_continua_Item_pronto()
  {
    // O Kit e o outro Agrupamento: a Solda tem `UtilizaKit`, mas o pai deste cenario e Avulso.
    var c = Cenario(kit: false);
    c.Kit(AgrupamentoId2);
    Pronto(c, 2, Corte, 8m);
    var calc = c.Calcular();

    Assert.True(calc.SetorUtilizaKit(Solda));
    Assert.False(calc.RecebeEmConjunto(1));
    Assert.Contains(calc.ColetasPendentes(), p => p.EstruturaItemId == 2);
    Assert.Empty(calc.KitsDaColeta());
  }

  [Fact]
  public void Pai_de_Kit_que_comeca_fora_de_Setor_com_UtilizaKit_nao_recebe_em_conjunto()
  {
    var c = Cenario(setorDoPai: Pintura);
    Pronto(c, 2, Corte, 8m);
    var calc = c.Calcular();

    Assert.False(calc.RecebeEmConjunto(1));
    Assert.Empty(calc.KitsDaColeta());
    Assert.Contains(calc.ColetasPendentes(), p => p.EstruturaItemId == 2);
  }

  [Fact]
  public void Filho_de_Kit_num_passo_intermediario_continua_Item_pronto()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Solda);
    c.No(2, 1, 40m, 4m, Corte, Dobra);
    c.Kit();
    Pronto(c, 2, Corte, 8m);   // passo 1 de 2: vai para a Dobra, nao para a montagem

    Assert.Contains(c.Calcular().ColetasPendentes(), p => p.EstruturaItemId == 2);
  }

  [Fact]
  public async Task Consulta_le_o_Kit_pelo_repositorio()
  {
    var c = Cenario();
    Pronto(c, 2, Corte, 8m);
    Pronto(c, 3, Dobra, 2m);

    // O 2 e o 3 vao no cartao do Kit e saem do Item pronto; sem a fiacao do leitor, o Kit nao existiria e os dois
    // seriam Item pronto.
    Assert.Empty((await c.Consulta().Tarefas(CancellationToken.None)).Valor!.SelectMany(g => g.Itens));
  }
}
