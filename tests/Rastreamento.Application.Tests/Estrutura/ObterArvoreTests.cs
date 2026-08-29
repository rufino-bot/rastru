using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// `ObterArvore` nao esta nos "seis testes" do brief da Task 3 (que so descreve `CriarPeca`), mas
/// o plano da Fase 2 (linha 1022) mostra a Task 5 chamando `_montagem.ObterArvore` diretamente, sem
/// nenhuma task intermediaria que a introduza. Ver a decisao no relatorio da Task 3: o metodo E
/// implementado aqui, e por isso ganha teste — guarda sem teste que a mate nao conta como guarda.
/// </summary>
public class ObterArvoreTests
{
  private static Componente NovoComponente(int id, string codigo, string descricao) =>
      new() { Id = id, Codigo = codigo, Descricao = descricao, Tipo = "Montagem", Ativo = true };

  [Fact]
  public async Task Arvore_do_Agrupamento_inclui_todos_os_nos_com_materiais_e_roteiro_resolvidos()
  {
    var estruturas = new FakeEstruturaRepo();
    estruturas.ReceitaFilhos.Add((1, 2, 4m));
    estruturas.ReceitaMateriais.Add((2, 90, 1.5m));
    estruturas.ReceitaRoteiro.Add((2, 7, 10));

    var agrupamentos = new FakeAgrupamentoRepo(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    var catalogo = new FakeReceitaPadraoRepo();
    catalogo.Componentes.Add(NovoComponente(1, "C1", "Peca Um"));
    catalogo.Componentes.Add(NovoComponente(2, "C2", "Item Dois"));
    catalogo.Materiais.Add(new Material { Id = 90, Codigo = "M90", Descricao = "Chapa 2mm", UnidadeMedida = "UN", Ativo = true });
    catalogo.Setores.Add(new Setor { Id = 7, Nome = "Solda", Ativo = true });

    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo);
    var criado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 10m, RequerRelatorioDimensional: false), CancellationToken.None);
    Assert.True(criado.Sucesso);

    var resultado = await useCase.ObterArvore(1, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    var raiz = resultado.Valor!.Single();
    Assert.Equal("C1", raiz.CodigoDoComponente);

    var filho = raiz.Filhos.Single();
    var material = filho.Materiais.Single();
    Assert.Equal(90, material.MaterialId);
    Assert.Equal("Chapa 2mm", material.Nome);
    Assert.Equal(60m, material.Quantidade);   // filho = 10 (raiz) x 4 (receita) = 40; material = 40 x 1,5

    var passo = filho.Roteiro.Single();
    Assert.Equal(7, passo.SetorId);
    Assert.Equal("Solda", passo.Nome);
  }

  [Fact]
  public async Task Agrupamento_inexistente_da_404_em_ObterArvore()
  {
    var useCase = new MontagemDeEstruturaUseCase(
        new FakeEstruturaRepo(), new FakeAgrupamentoRepo(), new FakeReceitaPadraoRepo());

    var resultado = await useCase.ObterArvore(999, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Agrupamento_sem_estrutura_devolve_lista_vazia()
  {
    var useCase = new MontagemDeEstruturaUseCase(
        new FakeEstruturaRepo(),
        new FakeAgrupamentoRepo(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" }),
        new FakeReceitaPadraoRepo());

    var resultado = await useCase.ObterArvore(1, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Empty(resultado.Valor!);
  }
}
