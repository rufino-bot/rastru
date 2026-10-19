using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Separacao;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

/// <summary>
/// `SeparacaoUseCase.SepararEmLote`: varios nos num corpo so, tudo ou nada. Cada no passa pelas mesmas regras
/// da rota individual, e as recusas de entrada sao as do lote da execucao.
/// </summary>
public class SeparacaoEmLoteTests
{
  private const int Quadro = 42;
  private const int Tampa = 43;
  private const int SemDescricao = 44;
  private const int AgrupamentoId = 3;
  private const int PedidoId = 5;
  private const int Parafuso = 7;   // almoxarifado
  private const int Arruela = 9;    // almoxarifado
  private const int Cola = 10;      // almoxarifado, nunca no planejado
  private const int Separador = 15;

  private sealed class Cenario
  {
    public FakeEstruturaRepo Estruturas { get; } = new();
    public FakeSeparacaoRepo Separacao { get; } = new();
    public FakeExecucaoRepo Execucao { get; }

    public Cenario()
    {
      Execucao = new FakeExecucaoRepo(Estruturas);
      foreach (var (id, descricao) in new[] { (Quadro, "Quadro"), (Tampa, "Tampa"), (SemDescricao, (string?)null) })
      {
        Estruturas.Itens.Add(new EstruturaItem
        {
          Id = id, AgrupamentoId = AgrupamentoId, Descricao = descricao, NivelHierarquico = "Peca", Quantidade = 1m,
        });
        Separacao.Nos.Add(id);
      }
      Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-77");
      Execucao.StatusDoPedido[PedidoId] = "EmProducao";
      Execucao.Usuarios[Separador] = "Separador Um";

      Separacao.Materiais[Parafuso] = new MaterialDaSeparacao(Parafuso, "PAR-M8", "Parafuso M8", "UN", true, null, null);
      Separacao.Materiais[Arruela] = new MaterialDaSeparacao(Arruela, "ARR-8", "Arruela 8", "UN", true, null, null);
      Separacao.Materiais[Cola] = new MaterialDaSeparacao(Cola, "CO-1", "Cola de contato", "L", true, null, null);
    }

    public SeparacaoUseCase Uso() => new(Separacao, Execucao);

    public void Planejar(int no, int materialId, decimal quantidade) =>
        Separacao.Planejado.Add(new EstruturaMaterial { EstruturaItemId = no, MaterialId = materialId, Quantidade = quantidade });
  }

  private static NoDoLoteDeSeparacaoDto No(int noId, params (int MaterialId, decimal Quantidade)[] linhas) =>
      new(noId, linhas.Select(l => new LinhaDeMaterialDto(l.MaterialId, l.Quantidade)).ToList());

  private static LoteDeSeparacaoDto Lote(params NoDoLoteDeSeparacaoDto[] nos) => new(nos.ToList());

  [Fact]
  public async Task Grava_as_linhas_de_dois_nos_numa_escrita_so_e_devolve_uma_entrada_por_no_na_ordem_do_corpo()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);
    c.Planejar(Quadro, Arruela, 4m);
    c.Planejar(Tampa, Parafuso, 3m);

    // A Tampa vem primeiro no corpo, de proposito: a resposta segue o corpo, nao o Id.
    var r = await c.Uso().SepararEmLote(
        Lote(No(Tampa, (Parafuso, 3m)), No(Quadro, (Parafuso, 6m), (Arruela, 4m))), Separador, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(1, c.Separacao.Gravacoes);   // um SaveChanges so, tudo ou nada
    Assert.Equal(3, c.Separacao.Linhas.Count);
    Assert.All(c.Separacao.Linhas, l =>
    {
      Assert.Equal(Separador, l.UsuarioId);
      Assert.Equal(TiposDeSeparacao.Separacao, l.Tipo);
    });

    var entradas = r.Valor!;
    Assert.Equal([Tampa, Quadro], entradas.Select(e => e.EstruturaItemId).ToArray());
    var daTampa = entradas[0];
    var separacaoDaTampa = Assert.Single(daTampa.Separacoes);
    Assert.Equal((Parafuso, 3m, "Separador Um", true),
        (separacaoDaTampa.MaterialId, separacaoDaTampa.Quantidade, separacaoDaTampa.UsuarioNome, separacaoDaTampa.Estornavel));
    var materialDaTampa = Assert.Single(daTampa.Materiais);
    Assert.Equal((3m, 3m, 0m), (materialDaTampa.Planejado, materialDaTampa.Separado, materialDaTampa.Pendencia));

    // O mesmo conteudo da rota individual: so os materiais tocados daquele no, com a conta dele.
    var doQuadro = entradas[1];
    Assert.Equal(new HashSet<int> { Parafuso, Arruela }, doQuadro.Separacoes.Select(s => s.MaterialId).ToHashSet());
    var parafusoDoQuadro = doQuadro.Materiais.Single(m => m.MaterialId == Parafuso);
    Assert.Equal((10m, 6m, 4m), (parafusoDoQuadro.Planejado, parafusoDoQuadro.Separado, parafusoDoQuadro.Pendencia));
    Assert.Equal(new HashSet<int> { Parafuso, Arruela }, doQuadro.Materiais.Select(m => m.MaterialId).ToHashSet());
  }

  [Fact]
  public async Task Segundo_no_com_material_fora_do_planejado_e_409_com_o_nome_do_no_e_nada_do_primeiro_e_gravado()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);
    c.Planejar(Tampa, Parafuso, 3m);

    var r = await c.Uso().SepararEmLote(
        Lote(No(Quadro, (Parafuso, 2m)), No(Tampa, (Cola, 1m))), Separador, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.MaterialForaDoPlanejado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("CO-1", r.Detalhe);
    Assert.Contains("do nó Tampa", r.Detalhe);
    // A frase da rota individual ja nomeia o no: no lote ela passa igual, sem "Tampa: " na frente.
    Assert.False(r.Detalhe!.StartsWith("Tampa: "), r.Detalhe);
    Assert.Equal(0, c.Separacao.Gravacoes);
    Assert.Empty(c.Separacao.Linhas);
  }

  [Fact]
  public async Task No_inexistente_no_lote_e_404_sem_frase_e_nada_gravado()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);

    var r = await c.Uso().SepararEmLote(
        Lote(No(Quadro, (Parafuso, 2m)), No(999, (Parafuso, 1m))), Separador, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Null(r.Detalhe);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Os_nos_sao_validados_na_ordem_do_corpo()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);

    // O primeiro no recusa por "fora do planejado"; o segundo nem existe. Responde o primeiro.
    var r = await c.Uso().SepararEmLote(
        Lote(No(Quadro, (Cola, 1m)), No(999, (Parafuso, 1m))), Separador, CancellationToken.None);

    Assert.Equal(CodigosDaSeparacao.MaterialForaDoPlanejado, r.Erro);
  }

  [Fact]
  public async Task Lote_vazio_ou_ausente_e_400_LoteVazio_com_a_frase_do_lote_da_execucao()
  {
    var c = new Cenario();

    foreach (var dto in new[] { Lote(), new LoteDeSeparacaoDto(null) })
    {
      var r = await c.Uso().SepararEmLote(dto, Separador, CancellationToken.None);

      Assert.False(r.Sucesso);
      Assert.Equal(CodigosDaExecucao.LoteVazio, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
      Assert.Equal("Marque pelo menos um item.", r.Detalhe);
    }
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Lote_acima_do_teto_e_400_LoteGrandeDemais_antes_de_olhar_os_nos()
  {
    var c = new Cenario();
    // Nos que nem existem: o teto responde antes do 404.
    var nos = Enumerable.Range(1000, ApontamentoUseCase.TamanhoMaximoDoLote + 1).Select(id => No(id, (Parafuso, 1m))).ToArray();

    var r = await c.Uso().SepararEmLote(Lote(nos), Separador, CancellationToken.None);

    Assert.Equal(CodigosDaExecucao.LoteGrandeDemais, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal($"Um lote tem no máximo {ApontamentoUseCase.TamanhoMaximoDoLote} itens.", r.Detalhe);
  }

  [Fact]
  public async Task No_repetido_no_corpo_e_400_ItemRepetido_antes_de_olhar_os_nos()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);

    // A Cola nao esta no plano: se o lote olhasse o no antes do repetido, responderia 409.
    var r = await c.Uso().SepararEmLote(
        Lote(No(Quadro, (Cola, 1m)), No(Quadro, (Parafuso, 1m))), Separador, CancellationToken.None);

    Assert.Equal(CodigosDaExecucao.ItemRepetido, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains(Quadro.ToString(), r.Detalhe);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Recusa_de_um_no_leva_o_nome_dele_na_frente_da_frase()
  {
    var c = new Cenario();
    c.Planejar(Quadro, Parafuso, 10m);
    c.Planejar(Tampa, Parafuso, 3m);
    c.Planejar(SemDescricao, Parafuso, 3m);

    var vazia = await c.Uso().SepararEmLote(Lote(No(Quadro, (Parafuso, 1m)), No(Tampa)), Separador, CancellationToken.None);
    var semLinhas = await c.Uso().SepararEmLote(
        Lote(new NoDoLoteDeSeparacaoDto(Tampa, null)), Separador, CancellationToken.None);
    var quantidade = await c.Uso().SepararEmLote(Lote(No(Tampa, (Parafuso, 0m))), Separador, CancellationToken.None);
    var repetido = await c.Uso().SepararEmLote(
        Lote(No(Tampa, (Parafuso, 1m), (Parafuso, 2m))), Separador, CancellationToken.None);
    var semNome = await c.Uso().SepararEmLote(Lote(No(SemDescricao)), Separador, CancellationToken.None);

    Assert.Equal(CodigosDaSeparacao.SeparacaoVazia, vazia.Erro);
    Assert.Equal("Tampa: Informe ao menos um material para separar.", vazia.Detalhe);
    Assert.Equal(CodigosDaSeparacao.SeparacaoVazia, semLinhas.Erro);
    Assert.StartsWith("Tampa: ", semLinhas.Detalhe);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, quantidade.Erro);
    Assert.StartsWith("Tampa: A quantidade 0 não vale", quantidade.Detalhe);
    Assert.Equal(CodigosDaSeparacao.MaterialRepetido, repetido.Erro);
    Assert.StartsWith("Tampa: O material PAR-M8", repetido.Detalhe);
    // Sem descricao propria o no e nomeado pelo Id, como na frase da rota individual.
    Assert.StartsWith($"item {SemDescricao}: ", semNome.Detalhe);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }
}
