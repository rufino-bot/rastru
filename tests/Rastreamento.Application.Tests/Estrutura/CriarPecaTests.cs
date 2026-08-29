using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

public class CriarPecaTests
{
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas, FakeAgrupamentoRepo Agrupamentos,
      FakeReceitaPadraoRepo Catalogo) Montar(params Agrupamento[] agrupamentos)
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentosRepo = new FakeAgrupamentoRepo(agrupamentos);
    var catalogo = new FakeReceitaPadraoRepo();
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentosRepo, catalogo);
    return (useCase, estruturas, agrupamentosRepo, catalogo);
  }

  private static Componente NovoComponente(int id, string codigo, string descricao) =>
      new() { Id = id, Codigo = codigo, Descricao = descricao, Tipo = "Montagem", Ativo = true };

  [Fact]
  public async Task Peca_e_criada_e_a_arvore_da_receita_vem_junto()
  {
    var (useCase, estruturas, _, catalogo) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 4m));
    catalogo.Componentes.Add(NovoComponente(1, "C1", "Peca Um"));
    catalogo.Componentes.Add(NovoComponente(2, "C2", "Item Dois"));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 10m, RequerRelatorioDimensional: true), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    var raiz = resultado.Valor!;
    Assert.Equal(10m, raiz.Quantidade);
    Assert.Equal("C1", raiz.CodigoDoComponente);
    Assert.Equal("Peca Um", raiz.Descricao);   // regra 19: EstruturaItem.Descricao e null, herda do Componente

    var filho = raiz.Filhos.Single();
    Assert.Equal(40m, filho.Quantidade);   // 10 x 4 (fator da receita)
    Assert.Equal("C2", filho.CodigoDoComponente);
    Assert.Equal("Item Dois", filho.Descricao);

    Assert.Equal(1, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Peca_de_Componente_sem_receita_grava_um_no_so()
  {
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Avulso" });
    // Nenhuma aresta em estruturas.ReceitaFilhos: o Componente 5 nao tem receita cadastrada.

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 5, Quantidade: 3m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Empty(resultado.Valor!.Filhos);
    Assert.Single(estruturas.Itens);
  }

  [Fact]
  public async Task Ciclo_na_receita_recusa_com_409_e_nao_grava_nada()
  {
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 1m));
    estruturas.ReceitaFilhos.Add((2, 1, 1m));   // 1 -> 2 -> 1: ciclo

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Contains(PlanejadorDeCopia.CodigoDeCiclo, resultado.Erro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
    Assert.Empty(estruturas.Itens);
  }

  [Fact]
  public async Task Agrupamento_inexistente_da_404()
  {
    var (useCase, _, _, _) = Montar();   // nenhum Agrupamento cadastrado

    var resultado = await useCase.CriarPeca(
        999, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Criar_Peca_em_Pedido_EM_PRODUCAO_e_PERMITIDO()
  {
    // Informacao de dominio de 2026-08-29: cliente grande pede alteracao de projeto com o Pedido JA
    // em execucao, e acrescentar Peca nova ao pedido rodando e o comportamento PADRAO — nao
    // excecao. MontagemDeEstruturaUseCase.CriarPeca nao consulta Pedido nenhum (ver comentario no
    // proprio metodo); este Pedido existe so para DOCUMENTAR o cenario que o teste prova.
    var pedido = new Pedido { Id = 1, Numero = "PED-01", Cliente = "Cliente X", Tipo = "Normal", Status = "EmProducao", DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = 1 };
    Assert.Equal("EmProducao", pedido.Status);

    var (useCase, _, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = pedido.Id, Codigo = "AG-01", Tipo = "Kit" });

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.True(resultado.Sucesso);
  }

  [Fact]
  public async Task Nivel_hierarquico_da_raiz_e_Peca_e_o_do_filho_e_Item()
  {
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 1m));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Peca", resultado.Valor!.NivelHierarquico);
    Assert.Equal("Item", resultado.Valor.Filhos.Single().NivelHierarquico);
  }

  [Fact]
  public async Task Requer_relatorio_dimensional_vale_so_para_a_raiz_regra_10()
  {
    // Guarda direta da regra 10 — sem este teste, propagar RequerRelatorioDimensional para os
    // filhos passaria despercebido (ver mutacoes medidas no relatorio da Task 3).
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 1m));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: true), CancellationToken.None);

    Assert.True(resultado.Valor!.RequerRelatorioDimensional);
    Assert.False(resultado.Valor.Filhos.Single().RequerRelatorioDimensional);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-5)]
  public async Task Quantidade_zero_ou_negativa_e_recusada_com_validacao(decimal quantidade)
  {
    // Decisao sobre magnitude/sinal de quantidade (Task 3): valida SINAL aqui, na Application — nao
    // CHECK no schema. Guarda sem teste que a mate nao conta como guarda: este teste morre se a
    // checagem sumir.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: quantidade, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Quantidade_que_estoura_decimal_multiplicando_a_receita_e_recusada_sem_lancar()
  {
    // Fecha o escape nomeado na review da Task 2: o planejador MULTIPLICA descendo e nao guarda
    // magnitude, entao decimal.MaxValue x 2 estoura DENTRO de PlanejadorDeCopia.Planejar. Sem o
    // catch de OverflowException no caso de uso, este teste falharia com excecao NAO TRATADA (o
    // que e exatamente o "vira 500" que a decisao da Task 3 fecha) em vez de um Result.Falha.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 2m));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: decimal.MaxValue, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }
}
