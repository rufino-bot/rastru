using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// Task 4 da Fase 2: `AcrescentarFilho`, `EditarNo`, `ExcluirNo`. Delta do brief: +8 (testes 1 a 8
/// abaixo, na mesma ordem/numeracao do brief). +3 adicionais no fim do arquivo, fora da contagem do
/// brief — ver o relatorio da Task 4 para a justificativa de cada um (piso de quantidade em
/// `AcrescentarFilho`/`EditarNo`, e regra 19 tambem em `EditarNo`): sao guardas que o contexto do
/// brief pede explicitamente ("todas as guardas valem", "regra 19"), mas que nao entraram nos oito
/// nomeados — sem teste, ficariam guarda encenada.
/// </summary>
public class EditarEExcluirNoTests
{
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas, FakeAgrupamentoRepo Agrupamentos,
      FakeReceitaPadraoRepo Catalogo, FakePedidoRepo Pedidos) Montar(Agrupamento agrupamento, Pedido? pedido = null)
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentosRepo = new FakeAgrupamentoRepo(agrupamento);
    var catalogo = new FakeReceitaPadraoRepo();
    var pedidosRepo = pedido is null ? new FakePedidoRepo() : new FakePedidoRepo(pedido);
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentosRepo, catalogo, pedidosRepo);
    return (useCase, estruturas, agrupamentosRepo, catalogo, pedidosRepo);
  }

  private static Componente NovoComponente(int id, string codigo, string descricao) =>
      new() { Id = id, Codigo = codigo, Descricao = descricao, Tipo = "Montagem", Ativo = true };

  private static Agrupamento NovoAgrupamento(int id = 1, int pedidoId = 1) =>
      new() { Id = id, PedidoId = pedidoId, Codigo = "AG-01", Tipo = "Kit" };

  private static Pedido NovoPedido(int id = 1, string status = "Aberto") =>
      new()
      {
        Id = id, Numero = "PED-01", Cliente = "Cliente X", Tipo = "Normal", Status = status,
        DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = 1,
      };

  private static EstruturaItem NovoNo(
      int id, int agrupamentoId, int? paiId, decimal quantidade, string nivel, int? componenteId = null,
      string? descricao = null) =>
      new()
      {
        Id = id, AgrupamentoId = agrupamentoId, ComponenteId = componenteId, Descricao = descricao,
        EstruturaPaiId = paiId, NivelHierarquico = nivel, Quantidade = quantidade,
      };

  // ---- Passo 1 do brief: os oito testes nomeados ----

  [Fact]
  public async Task Filho_de_Componente_copia_a_receita_dele()
  {
    var (useCase, estruturas, _, catalogo, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, agrupamentoId: 1, paiId: null, quantidade: 10m, nivel: "Peca", componenteId: 100));
    estruturas.ReceitaFilhos.Add((10, 11, 4m));   // Componente 10 tem um filho, Componente 11, fator 4
    catalogo.Componentes.Add(NovoComponente(10, "C10", "Sub Um"));
    catalogo.Componentes.Add(NovoComponente(11, "C11", "Sub Dois"));

    var resultado = await useCase.AcrescentarFilho(
        1, new NovoFilhoDto(ComponenteId: 10, Descricao: null, Quantidade: 5m), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    var novo = resultado.Valor!;
    Assert.Equal(5m, novo.Quantidade);
    Assert.Equal("C10", novo.CodigoDoComponente);
    Assert.Equal("Item", novo.NivelHierarquico);

    var neto = novo.Filhos.Single();
    Assert.Equal(20m, neto.Quantidade);   // 5 x 4 (fator da receita)
    Assert.Equal("C11", neto.CodigoDoComponente);
  }

  [Fact]
  public async Task Filho_ad_hoc_sem_Componente_exige_descricao()
  {
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));

    var resultado = await useCase.AcrescentarFilho(
        1, new NovoFilhoDto(ComponenteId: null, Descricao: "   ", Quantidade: 5m), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.DoesNotContain(estruturas.Itens, i => i.EstruturaPaiId == 1);
  }

  [Fact]
  public async Task Filho_ad_hoc_com_descricao_e_criado_com_NivelHierarquico_Item()
  {
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));

    var resultado = await useCase.AcrescentarFilho(
        1, new NovoFilhoDto(ComponenteId: null, Descricao: "Suporte avulso", Quantidade: 3m), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Null(resultado.Valor!.ComponenteId);
    Assert.Equal("Suporte avulso", resultado.Valor.Descricao);
    Assert.Equal("Item", resultado.Valor.NivelHierarquico);
    Assert.Empty(resultado.Valor.Materiais);
    Assert.Empty(resultado.Valor.Roteiro);
    Assert.Empty(resultado.Valor.Filhos);
  }

  [Fact]
  public async Task Editar_quantidade_da_Peca_NAO_mexe_nos_filhos()
  {
    // D5 do brief.
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100, descricao: "Peca X"));
    estruturas.Itens.Add(NovoNo(2, 1, 1, 40m, nivel: "Item", componenteId: 101, descricao: "Filho"));

    var resultado = await useCase.EditarNo(1, new EdicaoDeNoDto("Peca X", 20m), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(20m, resultado.Valor!.Quantidade);
    var filho = resultado.Valor.Filhos.Single();
    Assert.Equal(40m, filho.Quantidade);   // continua 40, nao 80
  }

  [Fact]
  public async Task Editar_descricao_para_vazio_volta_a_herdar_do_Componente()
  {
    var (useCase, estruturas, _, catalogo, _) = Montar(NovoAgrupamento());
    catalogo.Componentes.Add(NovoComponente(100, "C100", "Descricao do Componente"));
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100, descricao: "Nome customizado"));

    var resultado = await useCase.EditarNo(1, new EdicaoDeNoDto("", 10m), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Descricao do Componente", resultado.Valor!.Descricao);
    Assert.Null(estruturas.Itens.Single().Descricao);   // gravado NULL, nao string vazia
  }

  [Fact]
  public async Task Excluir_no_leva_a_subarvore_junto()
  {
    var pedido = NovoPedido();
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento(pedidoId: pedido.Id), pedido);
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));
    estruturas.Itens.Add(NovoNo(2, 1, 1, 40m, nivel: "Item", componenteId: 101));   // meio — sera excluido
    estruturas.Itens.Add(NovoNo(3, 1, 2, 5m, nivel: "Item", componenteId: 102));    // neto

    var resultado = await useCase.ExcluirNo(2, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal([1], estruturas.Itens.Select(i => i.Id));
  }

  [Fact]
  public async Task Excluir_com_Pedido_fora_de_Aberto_recusa_com_PedidoNaoAberto()
  {
    // D6 do brief.
    var pedido = NovoPedido(status: "EmProducao");
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento(pedidoId: pedido.Id), pedido);
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));

    var resultado = await useCase.ExcluirNo(1, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal("PedidoNaoAberto", resultado.Erro);
    Assert.Single(estruturas.Itens);   // nada foi removido
  }

  [Fact]
  public async Task Excluir_com_Pedido_Aberto_e_permitido()
  {
    // O par do teste anterior — sem ele, uma implementacao que recusasse SEMPRE passaria no 7.
    var pedido = NovoPedido(status: "Aberto");
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento(pedidoId: pedido.Id), pedido);
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));

    var resultado = await useCase.ExcluirNo(1, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Empty(estruturas.Itens);
  }

  // ---- Adicionais (+3), fora da contagem "+8" do brief — ver XML doc da classe e o relatorio ----

  [Fact]
  public async Task Filho_com_quantidade_abaixo_do_piso_da_coluna_e_recusado()
  {
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));

    var resultado = await useCase.AcrescentarFilho(
        1, new NovoFilhoDto(ComponenteId: null, Descricao: "Ad hoc", Quantidade: 0.00001m), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.DoesNotContain(estruturas.Itens, i => i.EstruturaPaiId == 1);
  }

  [Fact]
  public async Task Editar_com_quantidade_abaixo_do_piso_da_coluna_e_recusado()
  {
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100, descricao: "Peca X"));

    var resultado = await useCase.EditarNo(1, new EdicaoDeNoDto("Peca X", 0m), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(10m, estruturas.Itens.Single().Quantidade);   // nao mudou
  }

  [Fact]
  public async Task Editar_descricao_para_vazio_em_no_ad_hoc_e_recusado_regra_19()
  {
    var (useCase, estruturas, _, _, _) = Montar(NovoAgrupamento());
    estruturas.Itens.Add(NovoNo(1, 1, null, 10m, nivel: "Peca", componenteId: 100));
    estruturas.Itens.Add(NovoNo(2, 1, 1, 3m, nivel: "Item", componenteId: null, descricao: "Ad hoc"));

    var resultado = await useCase.EditarNo(2, new EdicaoDeNoDto("", 3m), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal("Ad hoc", estruturas.Itens.Single(i => i.Id == 2).Descricao);   // nao mudou
  }
}
