using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>Editar o Roteiro do no (spec da Fase 3, secao 4.6): o que ja foi alcancado e historico.</summary>
public class RoteiroDoNoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>No 1, Roteiro Corte -> Dobra -> Pintura, com os passos 1 e 2 ja alcancados.</summary>
  private static CenarioDeExecucao Andado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra, Pintura);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Corte, 1), 5m);
    c.Mover(1, TiposDeMovimentacao.Termino, Local.NoSetor(Corte, 1), Local.AguardandoColeta(Corte, 1), 5m);
    c.Mover(1, TiposDeMovimentacao.Entrega, Local.AguardandoColeta(Corte, 1), Local.NoSetor(Dobra, 2), 5m);
    return c;
  }

  private static (int SetorId, int Ordem, bool Alcancado)[] Passos(RoteiroDoNoDto roteiro) =>
      roteiro.Passos.Select(p => (p.SetorId, p.Ordem, p.Alcancado)).ToArray();

  [Fact]
  public async Task Obter_marca_os_passos_alcancados()
  {
    var c = Andado();

    var r = await c.Roteiro().Obter(1, Ct);

    Assert.Equal(new[] { (Corte, 1, true), (Dobra, 2, true), (Pintura, 3, false) }, Passos(r.Valor!));
    Assert.Equal("Corte", r.Valor!.Passos[0].Nome);
  }

  [Fact]
  public async Task Sem_nada_alcancado_o_Roteiro_inteiro_muda_e_renumera_do_um()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Solda, Corte, Solda]), Ct);

    Assert.Equal(new[] { (Solda, 1, false), (Corte, 2, false), (Solda, 3, false) }, Passos(r.Valor!));
    Assert.Equal(new[] { 1 }, Assert.Single(c.Execucao.Travas));
  }

  [Fact]
  public async Task Passos_alcancados_ficam_e_os_novos_vem_depois_deles()
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Corte, Dobra, Solda, Pintura]), Ct);

    Assert.Equal(new[] { (Corte, 1, true), (Dobra, 2, true), (Solda, 3, false), (Pintura, 4, false) }, Passos(r.Valor!));
  }

  [Theory]
  [InlineData(new[] { Corte, Solda, Pintura })]            // muda o passo 2, alcancado
  [InlineData(new[] { Solda, Corte, Dobra, Pintura })]     // insere antes dos alcancados
  [InlineData(new[] { Corte })]                             // remove o passo 2, alcancado
  public async Task Mexer_em_passo_alcancado_da_PassoJaAlcancado(int[] passos)
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto(passos), Ct);

    AfirmarFalha(r, CodigosDaExecucao.PassoJaAlcancado, TipoDeErro.Conflito);
    Assert.Equal(3, c.Estruturas.Roteiros.Count(p => p.EstruturaItemId == 1));   // nada mudou
  }

  [Theory]
  [InlineData(Inativo)]
  [InlineData(77)]
  public async Task Setor_inativo_ou_inexistente_nao_entra_no_Roteiro(int setor)
  {
    var c = Andado();

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Corte, Dobra, setor]), Ct);

    AfirmarFalha(r, CodigosDaExecucao.RoteiroInvalido, TipoDeErro.Validacao);
  }

  [Fact]
  public async Task Setor_inativado_no_trecho_alcancado_continua_no_Roteiro()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Inativo, Corte);
    c.Mover(1, TiposDeMovimentacao.Inicio, Local.AIniciar, Local.NoSetor(Inativo, 1), 5m);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([Inativo, Dobra]), Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { (Inativo, 1, true), (Dobra, 2, false) }, Passos(r.Valor!));
  }

  /// <summary>
  /// Ruling R3 do controlador sobre o brief: `passos` AUSENTE/nulo no corpo e 400 `RoteiroInvalido` —
  /// diferente de uma lista vazia explicita ([]), que `Lista_vazia_explicita_e_aceita_e_limpa_o_que_falta`
  /// (par positivo, abaixo) prova ser aceita.
  /// </summary>
  [Fact]
  public async Task Passos_nulo_no_corpo_da_RoteiroInvalido()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto(null), Ct);

    AfirmarFalha(r, CodigosDaExecucao.RoteiroInvalido, TipoDeErro.Validacao);
    Assert.Equal(2, c.Estruturas.Roteiros.Count(p => p.EstruturaItemId == 1));   // nada mudou
  }

  /// <summary>
  /// Par positivo de `Passos_nulo_no_corpo_da_RoteiroInvalido`: [] é aceita, distinta de nulo — com
  /// NADA ainda alcançado (`travados` = 0), ela limpa o Roteiro inteiro. Com algo já alcançado, uma
  /// lista vazia cai no MESMO `PassoJaAlcancado` de omitir o passo alcançado
  /// (`Mexer_em_passo_alcancado_da_PassoJaAlcancado`, caso `[Corte]`): o corpo tem de reenviar o
  /// prefixo travado para "limpar so o que falta" — o que a ruling R3 chama de "remove os passos
  /// ainda nao alcancados" e exatamente essa mecanica quando NADA foi alcancado ainda.
  /// </summary>
  [Fact]
  public async Task Lista_vazia_explicita_e_aceita_e_limpa_o_Roteiro_quando_nada_foi_alcancado()
  {
    var c = new CenarioDeExecucao();
    c.No(1, null, 10m, null, Corte, Dobra);

    var r = await c.Roteiro().Substituir(1, new RoteiroNovoDto([]), Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!.Passos);
    Assert.DoesNotContain(c.Estruturas.Roteiros, p => p.EstruturaItemId == 1);
  }

  [Fact]
  public async Task No_inexistente_da_404()
  {
    var c = new CenarioDeExecucao();

    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Roteiro().Obter(9, Ct)).TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await c.Roteiro().Substituir(9, new RoteiroNovoDto([Corte]), Ct)).TipoDoErro);
  }

  private static void AfirmarFalha<T>(Result<T> r, string codigo, TipoDeErro tipo)
  {
    Assert.False(r.Sucesso);
    Assert.Equal(codigo, r.Erro);
    Assert.Equal(tipo, r.TipoDoErro);
  }
}
