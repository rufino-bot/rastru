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

    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo, new FakePedidoRepo());
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
        new FakeEstruturaRepo(), new FakeAgrupamentoRepo(), new FakeReceitaPadraoRepo(), new FakePedidoRepo());

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
        new FakeReceitaPadraoRepo(), new FakePedidoRepo());

    var resultado = await useCase.ObterArvore(1, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Empty(resultado.Valor!);
  }

  [Fact]
  public async Task Filhos_e_materiais_saem_ordenados_por_Id_mesmo_que_o_repositorio_devolva_fora_de_ordem()
  {
    // Minor 2 da review da Task 3: nada ordenava filhos/materiais (so o roteiro, por Ordem, que e
    // sequencia de negocio). `ToLookup` preserva a ordem de CHEGADA — se o repositorio devolver
    // fora de ordem (heap sem `ORDER BY` nao garante ordem estavel), a arvore reordenaria irmaos
    // entre dois F5. Simula isso inserindo DIRETO no fake, fora de sequencia: se dependessemos so
    // da ordem de insercao do fake (sempre ascendente via `GravarNo`), este teste nunca pegaria a
    // falta de ordenacao explicita em `MontarArvore`.
    var estruturas = new FakeEstruturaRepo();
    var raiz = new EstruturaItem
    { Id = 1, AgrupamentoId = 1, ComponenteId = 1, EstruturaPaiId = null, NivelHierarquico = "Peca", Quantidade = 1m };
    var filhoTresChegaPrimeiro = new EstruturaItem
    { Id = 3, AgrupamentoId = 1, ComponenteId = 3, EstruturaPaiId = 1, NivelHierarquico = "Item", Quantidade = 1m };
    var filhoDoisChegaDepois = new EstruturaItem
    { Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 1, NivelHierarquico = "Item", Quantidade = 1m };
    estruturas.Itens.Add(raiz);
    estruturas.Itens.Add(filhoTresChegaPrimeiro);   // Id 3 inserido ANTES do Id 2, de proposito
    estruturas.Itens.Add(filhoDoisChegaDepois);

    // MaterialId 91 (linha Id 10) tem de sair ANTES do MaterialId 90 (linha Id 20) porque a
    // ordenacao e pelo Id da LINHA (EstruturaMaterial.Id), nao pelo MaterialId — se a producao
    // ordenasse pelo campo errado, a asserção abaixo pegaria (90 antes de 91).
    var materialQueChegaPrimeiroMasIdMaior = new EstruturaMaterial { Id = 20, EstruturaItemId = raiz.Id, MaterialId = 90, Quantidade = 1m };
    var materialQueChegaDepoisMasIdMenor = new EstruturaMaterial { Id = 10, EstruturaItemId = raiz.Id, MaterialId = 91, Quantidade = 1m };
    estruturas.Materiais.Add(materialQueChegaPrimeiroMasIdMaior);
    estruturas.Materiais.Add(materialQueChegaDepoisMasIdMenor);

    var agrupamentos = new FakeAgrupamentoRepo(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    var catalogo = new FakeReceitaPadraoRepo();
    catalogo.Componentes.Add(NovoComponente(1, "C1", "Peca Um"));
    catalogo.Componentes.Add(NovoComponente(2, "C2", "Filho Dois"));
    catalogo.Componentes.Add(NovoComponente(3, "C3", "Filho Tres"));

    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo, new FakePedidoRepo());

    var resultado = await useCase.ObterArvore(1, CancellationToken.None);

    var raizDto = resultado.Valor!.Single();
    Assert.Equal([2, 3], raizDto.Filhos.Select(f => f.Id));
    Assert.Equal([91, 90], raizDto.Materiais.Select(m => m.MaterialId));
  }

  [Fact]
  public async Task No_orfao_lanca_em_vez_de_sumir_em_silencio_da_arvore()
  {
    // Minor 3 da review da Task 3: um no cujo EstruturaPaiId aponta para fora dos itens lidos do
    // Agrupamento nunca era visitado por `Montar` (que so desce a partir das raizes) — sumia da
    // arvore devolvida sem erro, sem log. Aqui o Componente 2 tem EstruturaPaiId = 999, que nao
    // esta entre os itens do Agrupamento 1.
    var estruturas = new FakeEstruturaRepo();
    estruturas.Itens.Add(new EstruturaItem
    { Id = 1, AgrupamentoId = 1, ComponenteId = 1, EstruturaPaiId = null, NivelHierarquico = "Peca", Quantidade = 1m });
    estruturas.Itens.Add(new EstruturaItem
    { Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 999, NivelHierarquico = "Item", Quantidade = 1m });

    var useCase = new MontagemDeEstruturaUseCase(
        estruturas, new FakeAgrupamentoRepo(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" }),
        new FakeReceitaPadraoRepo(), new FakePedidoRepo());

    await Assert.ThrowsAsync<ArvoreInconsistenteException>(() => useCase.ObterArvore(1, CancellationToken.None));
  }
}
