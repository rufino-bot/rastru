using System.Globalization;
using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Estrutura;

/// <summary>
/// Regra 26 na superficie da Fase 2 (plano 2 da Fase 3, Task 1): a razao nasce na copia da receita,
/// e obrigatoria no Item acrescentado ou editado a mao, proibida na Peca; e a arvore marca
/// `SemRoteiro` (spec da Fase 3, secao 5.3).
/// </summary>
public class QuantidadePorPaiTests
{
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas) Montar()
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentos = new FakeAgrupamentoRepo(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Avulso" });
    var catalogo = new FakeReceitaPadraoRepo();
    catalogo.Componentes.Add(new Componente
    {
      Id = 1, Codigo = "CHS", Descricao = "Chassi", Tipo = "Montagem", Ativo = true, ArquivoSolidoId = 701,
    });
    catalogo.Componentes.Add(new Componente { Id = 2, Codigo = "SUP", Descricao = "Suporte", Tipo = "Fabricado", Ativo = true });
    catalogo.Componentes.Add(new Componente { Id = 3, Codigo = "PAR", Descricao = "Parafuso", Tipo = "Fabricado", Ativo = true });
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentos, catalogo, new FakePedidoRepo());
    return (useCase, estruturas);
  }

  private static EstruturaItem Gravado(FakeEstruturaRepo estruturas, int id) =>
      estruturas.Itens.Single(i => i.Id == id);

  [Fact]
  public async Task Copia_da_receita_grava_a_razao_de_cada_filho_e_nenhuma_na_Peca()
  {
    var (useCase, estruturas) = Montar();
    estruturas.ReceitaFilhos.Add((1, 2, 4m));     // um Chassi leva 4 Suportes
    estruturas.ReceitaFilhos.Add((2, 3, 2.5m));   // um Suporte leva 2,5 Parafusos

    var r = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Null(r.Valor!.QuantidadePorPai);
    var suporte = Assert.Single(r.Valor.Filhos);
    Assert.Equal(4m, suporte.QuantidadePorPai);
    Assert.Equal(40m, suporte.Quantidade);
    Assert.Equal(2.5m, Assert.Single(suporte.Filhos).QuantidadePorPai);
    Assert.Null(Gravado(estruturas, r.Valor.Id).QuantidadePorPai);
    Assert.Equal(4m, Gravado(estruturas, suporte.Id).QuantidadePorPai);
  }

  [Fact]
  public async Task Filho_ad_hoc_grava_a_razao_informada()
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(null, "Calço", 20m, QuantidadePorPai: 2m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(2m, r.Valor!.QuantidadePorPai);
    Assert.Equal(2m, Gravado(estruturas, r.Valor.Id).QuantidadePorPai);
  }

  [Fact]
  public async Task Filho_de_Componente_usa_a_razao_do_corpo_no_topo_e_a_da_receita_abaixo()
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);
    // Receita semeada DEPOIS da Peca: ela nasce sem filhos, e so o filho acrescentado copia a receita.
    estruturas.ReceitaFilhos.Add((2, 3, 2.5m));

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(2, null, 30m, QuantidadePorPai: 3m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(3m, r.Valor!.QuantidadePorPai);
    Assert.Equal(2.5m, Assert.Single(r.Valor.Filhos).QuantidadePorPai);
    Assert.Equal(75m, r.Valor.Filhos[0].Quantidade);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00001")]
  [InlineData("1.00005")]
  public async Task Filho_sem_razao_valida_e_recusado_e_nada_e_gravado(string? razao)
  {
    var (useCase, estruturas) = Montar();
    var peca = await useCase.CriarPeca(1, new NovaPecaDto(1, 10m, false), CancellationToken.None);
    decimal? valor = razao is null ? null : decimal.Parse(razao, CultureInfo.InvariantCulture);

    var r = await useCase.AcrescentarFilho(
        peca.Valor!.Id, new NovoFilhoDto(null, "Calço", 20m, QuantidadePorPai: valor), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(1, estruturas.GravacoesDeArvore);   // so a Peca
  }

  [Fact]
  public async Task Editar_Item_troca_a_razao()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 40m, QuantidadePorPai = 4m,
    });

    var r = await useCase.EditarNo(2, new EdicaoDeNoDto(null, 50m, QuantidadePorPai: 5m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(5m, Gravado(estruturas, 2).QuantidadePorPai);
    Assert.Equal(5m, r.Valor!.QuantidadePorPai);
  }

  [Fact]
  public async Task Editar_Item_sem_razao_e_recusado()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, ComponenteId = 2, EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 40m, QuantidadePorPai = 4m,
    });

    var r = await useCase.EditarNo(2, new EdicaoDeNoDto(null, 50m), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(4m, Gravado(estruturas, 2).QuantidadePorPai);
    Assert.Equal(0, estruturas.Saves);
  }

  [Fact]
  public async Task Editar_Peca_com_razao_e_recusado()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });

    var r = await useCase.EditarNo(1, new EdicaoDeNoDto(null, 10m, QuantidadePorPai: 1m), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("regra 26", r.Erro);
    Assert.Equal(0, estruturas.Saves);
  }

  [Fact]
  public async Task Arvore_marca_sem_Roteiro_so_o_no_que_nao_tem_passo()
  {
    var (useCase, estruturas) = Montar();
    estruturas.Itens.Add(new EstruturaItem { Id = 1, AgrupamentoId = 1, ComponenteId = 1, NivelHierarquico = "Peca", Quantidade = 10m });
    estruturas.Itens.Add(new EstruturaItem
    {
      Id = 2, AgrupamentoId = 1, Descricao = "Calço", EstruturaPaiId = 1, NivelHierarquico = "Item",
      Quantidade = 20m, QuantidadePorPai = 2m,
    });
    estruturas.Roteiros.Add(new EstruturaRoteiro { Id = 90, EstruturaItemId = 1, SetorId = 5, Ordem = 1 });

    var r = await useCase.ObterArvore(1, CancellationToken.None);

    var raiz = Assert.Single(r.Valor!);
    Assert.False(raiz.SemRoteiro);
    Assert.True(Assert.Single(raiz.Filhos).SemRoteiro);
  }
}
