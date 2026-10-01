using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// Criar Peca e acrescentar filho gravam a arvore DENTRO da transacao da execucao (retry de deadlock e
/// 409 no esgotamento, spec do conserto do deadlock, secao 3). O conflito simulado por
/// `FakeExecucaoRepo.ConflitoNaProximaTransacao` so e lancado se a gravacao passar por
/// `EmTransacaoAsync`: gravar fora dela deixaria o resultado em sucesso, ou gravaria antes do conflito.
/// </summary>
public class GravacaoDeArvoreNaTransacaoTests
{
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas, FakeExecucaoRepo Execucao) Montar()
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentos = new FakeAgrupamentoRepo(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    var catalogo = new FakeReceitaPadraoRepo();
    catalogo.Componentes.Add(new Componente
    { Id = 1, Codigo = "C1", Descricao = "Peca Um", Tipo = "Montagem", Ativo = true, ArquivoSolidoId = 701 });
    var execucao = new FakeExecucaoRepo(estruturas);
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo, new FakePedidoRepo(), execucao);
    return (useCase, estruturas, execucao);
  }

  [Fact]
  public async Task CriarPeca_com_conflito_na_transacao_devolve_409_ConflitoDeConcorrencia_sem_gravar()
  {
    var (useCase, estruturas, execucao) = Montar();
    execucao.ConflitoNaProximaTransacao = true;

    var r = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task AcrescentarFilho_com_conflito_na_transacao_devolve_409_ConflitoDeConcorrencia_sem_gravar()
  {
    var (useCase, estruturas, execucao) = Montar();
    var peca = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);
    Assert.True(peca.Sucesso);
    var raizId = peca.Valor!.Id;
    // So agora: o conflito e da transacao do filho, nao da criacao da Peca.
    execucao.ConflitoNaProximaTransacao = true;

    var r = await useCase.AcrescentarFilho(
        raizId, new NovoFilhoDto(ComponenteId: null, Descricao: "Filho ad-hoc", Quantidade: 1m, QuantidadePorPai: 1m),
        CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(CodigosDaExecucao.ConflitoDeConcorrencia, r.Erro);
    Assert.Equal(1, estruturas.GravacoesDeArvore);   // so a da Peca criada no arranjo
  }
}
