using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Separacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

public class MateriaisDoNoUseCaseTests
{
  private const int No = 42;
  private const int Parafuso = 7;   // almoxarifado
  private const int Chapa = 8;      // fica no Corte
  private const int Cola = 9;       // almoxarifado, inativo

  private static (MateriaisDoNoUseCase Uso, FakeSeparacaoRepo Repo) Montar()
  {
    var repo = new FakeSeparacaoRepo();
    repo.Nos.Add(No);
    repo.Materiais[Parafuso] = new MaterialDaSeparacao(Parafuso, "PAR-M8", "Parafuso M8", "UN", true, null, null);
    repo.Materiais[Chapa] = new MaterialDaSeparacao(Chapa, "CH-3", "Chapa 3 mm", "KG", true, 1, "Corte a Laser");
    repo.Materiais[Cola] = new MaterialDaSeparacao(Cola, "CO-1", "Cola", "L", false, null, null);
    return (new MateriaisDoNoUseCase(repo), repo);
  }

  private static EstruturaMaterial Plano(int materialId, decimal quantidade) =>
      new() { EstruturaItemId = No, MaterialId = materialId, Quantidade = quantidade };

  private static MaterialSeparacao Linha(string tipo, int materialId, decimal quantidade, int? estornoDeId = null) => new()
  {
    EstruturaItemId = No, MaterialId = materialId, Tipo = tipo, Quantidade = quantidade, EstornoDeId = estornoDeId,
  };

  private static MateriaisNovosDoNoDto Corpo(params (int MaterialId, decimal Quantidade)[] linhas) =>
      new(linhas.Select(l => new LinhaDeMaterialDto(l.MaterialId, l.Quantidade)).ToList());

  [Fact]
  public async Task Obter_no_inexistente_e_404()
  {
    var (uso, _) = Montar();

    var r = await uso.Obter(999, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  [Fact]
  public async Task Obter_traz_planejado_separado_pendencia_e_FicaEm_por_codigo()
  {
    var (uso, repo) = Montar();
    repo.Planejado.AddRange([Plano(Parafuso, 10m), Plano(Chapa, 5m)]);
    await repo.GravarAsync([Linha(TiposDeSeparacao.Separacao, Parafuso, 8m)], CancellationToken.None);

    var r = await uso.Obter(No, CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(No, r.Valor!.EstruturaItemId);
    // Por codigo: CH-3 antes de PAR-M8, embora o Id da chapa seja o maior.
    Assert.Equal(["CH-3", "PAR-M8"], r.Valor.Materiais.Select(m => m.Codigo));

    var chapa = r.Valor.Materiais[0];
    Assert.Equal(new SetorDeEstoqueDto(1, "Corte a Laser"), chapa.FicaEm);
    Assert.Equal(5m, chapa.Planejado);
    Assert.Equal(0m, chapa.Pendencia);

    var parafuso = r.Valor.Materiais[1];
    Assert.Null(parafuso.FicaEm);
    Assert.Equal(("UN", "Parafuso M8"), (parafuso.UnidadeMedida, parafuso.Descricao));
    Assert.Equal((10m, 8m, 2m), (parafuso.Planejado, parafuso.Separado, parafuso.Pendencia));
    Assert.True(parafuso.NoPlanejado);
    Assert.False(parafuso.AcimaDoPlanejado);
  }

  // Review Focus 2: o PCP tirou o material do plano, mas ele ja tem separacao.
  [Fact]
  public async Task Obter_traz_o_material_que_saiu_do_plano_com_historico()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));
    await repo.GravarAsync([Linha(TiposDeSeparacao.Separacao, Parafuso, 3m)], CancellationToken.None);

    var r = await uso.Obter(No, CancellationToken.None);

    var parafuso = Assert.Single(r.Valor!.Materiais, m => m.MaterialId == Parafuso);
    Assert.False(parafuso.NoPlanejado);
    Assert.Equal((0m, 3m, 0m), (parafuso.Planejado, parafuso.Separado, parafuso.Pendencia));
    Assert.False(parafuso.AcimaDoPlanejado);
    Assert.Equal(2, r.Valor.Materiais.Count);
  }

  [Fact]
  public async Task Obter_nao_traz_o_material_fora_do_plano_com_toda_separacao_estornada()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));
    var separacao = Linha(TiposDeSeparacao.Separacao, Parafuso, 3m);
    await repo.GravarAsync([separacao], CancellationToken.None);
    await repo.GravarAsync([Linha(TiposDeSeparacao.Estorno, Parafuso, 3m, separacao.Id)], CancellationToken.None);

    var r = await uso.Obter(No, CancellationToken.None);

    Assert.True(r.Sucesso);
    var unico = Assert.Single(r.Valor!.Materiais);
    Assert.Equal(Chapa, unico.MaterialId);
    // As linhas continuam no livro: quem as mostra e o historico, nao a lista de materiais.
    Assert.Equal(2, repo.Linhas.Count);
  }

  [Fact]
  public async Task Obter_traz_o_material_fora_do_plano_com_separado_liquido_maior_que_zero()
  {
    var (uso, repo) = Montar();
    var primeira = Linha(TiposDeSeparacao.Separacao, Parafuso, 3m);
    await repo.GravarAsync([primeira, Linha(TiposDeSeparacao.Separacao, Parafuso, 2m)], CancellationToken.None);
    await repo.GravarAsync([Linha(TiposDeSeparacao.Estorno, Parafuso, 3m, primeira.Id)], CancellationToken.None);

    var r = await uso.Obter(No, CancellationToken.None);

    var parafuso = Assert.Single(r.Valor!.Materiais);
    Assert.False(parafuso.NoPlanejado);
    Assert.Equal(2m, parafuso.Separado);
  }

  [Fact]
  public async Task Obter_traz_o_material_do_plano_sem_separacao_com_a_pendencia_inteira()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Parafuso, 10m));
    // Separado e estornado de novo: o separado liquido volta a zero, e o material do plano continua.
    var separacao = Linha(TiposDeSeparacao.Separacao, Parafuso, 4m);
    await repo.GravarAsync([separacao], CancellationToken.None);
    await repo.GravarAsync([Linha(TiposDeSeparacao.Estorno, Parafuso, 4m, separacao.Id)], CancellationToken.None);

    var r = await uso.Obter(No, CancellationToken.None);

    var parafuso = Assert.Single(r.Valor!.Materiais);
    Assert.True(parafuso.NoPlanejado);
    Assert.Equal((10m, 0m, 10m), (parafuso.Planejado, parafuso.Separado, parafuso.Pendencia));
  }

  [Fact]
  public async Task Substituir_troca_a_lista_inteira_e_devolve_o_resumo_novo()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));

    var r = await uso.Substituir(No, Corpo((Parafuso, 10m)), CancellationToken.None);

    Assert.True(r.Sucesso);
    var item = Assert.Single(r.Valor!.Materiais);
    Assert.Equal((Parafuso, 10m, 0m, 10m), (item.MaterialId, item.Planejado, item.Separado, item.Pendencia));
    Assert.True(item.NoPlanejado);
    var gravado = Assert.Single(repo.Planejado);
    Assert.Equal((Parafuso, 10m), (gravado.MaterialId, gravado.Quantidade));
  }

  [Fact]
  public async Task Substituir_com_lista_vazia_limpa_o_planejado()
  {
    var (uso, repo) = Montar();
    repo.Planejado.AddRange([Plano(Parafuso, 10m), Plano(Chapa, 5m)]);

    var r = await uso.Substituir(No, Corpo(), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!.Materiais);
    Assert.Empty(repo.Planejado);
  }

  [Fact]
  public async Task Substituir_mantem_a_separacao_do_material_que_sai_do_plano()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Parafuso, 10m));
    await repo.GravarAsync([Linha(TiposDeSeparacao.Separacao, Parafuso, 4m)], CancellationToken.None);

    var r = await uso.Substituir(No, Corpo((Chapa, 5m)), CancellationToken.None);

    Assert.True(r.Sucesso);
    var fora = Assert.Single(r.Valor!.Materiais, m => m.MaterialId == Parafuso);
    Assert.False(fora.NoPlanejado);
    Assert.Equal(4m, fora.Separado);
    Assert.Single(repo.Linhas);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(0.00001)]
  public async Task Substituir_com_quantidade_invalida_e_400_sem_gravar(double quantidade)
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));

    var r = await uso.Substituir(No, Corpo((Parafuso, (decimal)quantidade)), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("QuantidadeInvalida", r.Erro);
    Assert.Equal(0, repo.Gravacoes);
    // O planejado de antes continua: nada foi apagado.
    Assert.Equal(Chapa, Assert.Single(repo.Planejado).MaterialId);
  }

  [Fact]
  public async Task Substituir_com_material_repetido_e_400()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));

    var r = await uso.Substituir(No, Corpo((Parafuso, 1m), (Parafuso, 2m)), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("MaterialRepetido", r.Erro);
    Assert.Equal(Chapa, Assert.Single(repo.Planejado).MaterialId);
  }

  [Fact]
  public async Task Substituir_com_material_inexistente_ou_inativo_e_400()
  {
    var (uso, repo) = Montar();
    repo.Planejado.Add(Plano(Chapa, 5m));

    var inexistente = await uso.Substituir(No, Corpo((Parafuso, 1m), (777, 2m)), CancellationToken.None);
    var inativo = await uso.Substituir(No, Corpo((Cola, 2m)), CancellationToken.None);

    Assert.False(inexistente.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, inexistente.TipoDoErro);
    Assert.Equal("MaterialInvalido", inexistente.Erro);
    Assert.Contains("777", inexistente.Detalhe);

    Assert.False(inativo.Sucesso);
    Assert.Equal("MaterialInvalido", inativo.Erro);
    Assert.Contains("CO-1", inativo.Detalhe);

    Assert.Equal(Chapa, Assert.Single(repo.Planejado).MaterialId);
  }

  private const int Fita = 11;      // almoxarifado, inativo

  [Fact]
  public async Task Substituir_mantendo_material_ja_planejado_e_depois_inativado_e_aceito()
  {
    var (uso, repo) = Montar();
    // A Cola esta no planejado do no e foi inativada no catalogo depois: o PUT reenvia a lista inteira, e
    // mudar a quantidade do Parafuso nao pode exigir tirar a Cola do plano.
    repo.Planejado.AddRange([Plano(Parafuso, 10m), Plano(Cola, 2m)]);

    var r = await uso.Substituir(No, Corpo((Parafuso, 12m), (Cola, 2m)), CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(
        [(Parafuso, 12m), (Cola, 2m)],
        repo.Planejado.OrderBy(p => p.MaterialId == Cola).Select(p => (p.MaterialId, p.Quantidade)).ToList());
    var cola = r.Valor!.Materiais.Single(i => i.MaterialId == Cola);
    Assert.True(cola.NoPlanejado);
    Assert.Equal(2m, cola.Planejado);
  }

  [Fact]
  public async Task Acrescentar_material_inativo_novo_continua_400_mesmo_com_outro_inativo_ja_planejado()
  {
    var (uso, repo) = Montar();
    repo.Materiais[Fita] = new MaterialDaSeparacao(Fita, "FI-1", "Fita", "M", false, null, null);
    repo.Planejado.AddRange([Plano(Parafuso, 10m), Plano(Cola, 2m)]);

    var r = await uso.Substituir(No, Corpo((Parafuso, 10m), (Cola, 2m), (Fita, 1m)), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("MaterialInvalido", r.Erro);
    Assert.Contains("FI-1", r.Detalhe);
    Assert.Equal([Parafuso, Cola], repo.Planejado.Select(p => p.MaterialId).ToList());
  }

  [Fact]
  public async Task Substituir_no_inexistente_e_404_antes_de_validar_o_corpo()
  {
    var (uso, repo) = Montar();

    // O corpo tem quantidade invalida, repetido e material inexistente: nada disso chega a ser olhado.
    var r = await uso.Substituir(999, Corpo((777, 0m), (777, 1m)), CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Empty(repo.Planejado);
  }
}
