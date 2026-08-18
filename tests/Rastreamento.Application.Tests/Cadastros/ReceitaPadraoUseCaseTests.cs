using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

/// <summary>
/// Receita padrao — parte de MATERIAIS. E o molde que roteiro (Task 4) e filhos (Task 5) copiam:
/// componente existe, ids existem, ids estao ativos, nada de duplicata, substituicao inteira.
/// As mensagens de "nao existe" e "esta inativo" nascem de um helper COMPARTILHADO pelos tres
/// sub-recursos, entao os quatro ramos dele (singular/plural x ausente/inativo) sao provados aqui,
/// uma vez, e nao tres.
/// </summary>
public class ReceitaPadraoUseCaseTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;

  /// <summary>
  /// Catalogo com 2 componentes (1, 2), 2 materiais (10, 11) e 2 setores (20, 21).
  ///
  /// O componente 2 existe por causa do achado B11 da Fase 1A: com um unico componente no fake,
  /// um `1` literal no lugar do `componenteId` coincide com o unico id que os testes usam e a
  /// mutacao sobrevive. `Receita_de_um_componente_nao_vaza_para_outro` e quem cobra isso.
  /// </summary>
  private static FakeReceitaPadraoRepo FakeComCatalogo()
  {
    var f = new FakeReceitaPadraoRepo();
    f.Componentes.Add(new Componente { Id = 1, Codigo = "PAI", Descricao = "Pai", Tipo = "Montagem", Ativo = true });
    f.Componentes.Add(new Componente { Id = 2, Codigo = "OUTRO", Descricao = "Outro", Tipo = "Montagem", Ativo = true });
    f.Materiais.Add(new Material { Id = 10, Codigo = "CH-3", Descricao = "Chapa 3mm", UnidadeMedida = "KG", Ativo = true });
    f.Materiais.Add(new Material { Id = 11, Codigo = "PA-1", Descricao = "Parafuso", UnidadeMedida = "UN", Ativo = true });
    f.Setores.Add(new Setor { Id = 20, Nome = "Corte", Ativo = true });
    f.Setores.Add(new Setor { Id = 21, Nome = "Solda", Ativo = true });
    return f;
  }

  [Fact]
  public async Task Substituir_materiais_grava_e_projeta_os_dados_do_material()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 2.5m)], Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    // O `Id` e o da LINHA da receita (identity do fake, faixa 500+), nao o do Material — sao
    // campos diferentes do mesmo DTO e trocar um pelo outro tem de matar este teste.
    Assert.Equal(500, linha.Id);
    Assert.Equal(10, linha.MaterialId);
    Assert.Equal("CH-3", linha.Codigo);
    Assert.Equal("Chapa 3mm", linha.Descricao);
    Assert.Equal("KG", linha.UnidadeMedida);
    Assert.Equal(2.5m, linha.QuantidadePadrao);
  }

  /// <summary>Lista vazia APAGA — unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Substituir_materiais_com_lista_vazia_apaga_a_receita()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    var r = await caso.SubstituirMateriais(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
    Assert.Empty(fake.MateriaisPadrao);
  }

  /// <summary>
  /// A receita e POR componente. Com um unico componente no catalogo, um `1` literal no lugar do
  /// `componenteId` — na gravacao, na leitura ou no `ComponenteId` da linha montada — passaria em
  /// todos os outros testes deste arquivo.
  /// </summary>
  [Fact]
  public async Task Receita_de_um_componente_nao_vaza_para_outro()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);
    await caso.SubstituirMateriais(2, [new LinhaDeMaterialPadraoDto(11, 7m)], Ct);

    var doUm = await caso.ListarMateriais(1, Ct);
    var doDois = await caso.ListarMateriais(2, Ct);

    Assert.Equal([10], doUm.Valor!.Select(l => l.MaterialId));
    Assert.Equal([11], doDois.Valor!.Select(l => l.MaterialId));
    Assert.Equal(7m, Assert.Single(doDois.Valor!).QuantidadePadrao);
  }

  [Fact]
  public async Task Substituir_materiais_de_componente_inexistente_e_nao_encontrado()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(999, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public async Task Quantidade_nao_positiva_e_recusada(decimal quantidade)
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, quantidade)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("Quantidade deve ser maior que zero.", r.Erro);
    // NAO gravou: recusa que grava metade e pior que recusa nenhuma.
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// A lista tem DUAS duplicatas de proposito, e nao uma: 11 se repete primeiro (indice 2), 10 so
  /// depois (indice 3). Com uma duplicata so, "o primeiro repetido" e "o ultimo repetido" dao a
  /// mesma resposta e a ordem que o helper promete no XML doc dele nao estaria provada.
  /// </summary>
  [Fact]
  public async Task Material_repetido_na_mesma_lista_e_recusado_nomeando_o_primeiro()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1,
        [
          new LinhaDeMaterialPadraoDto(10, 1m),
          new LinhaDeMaterialPadraoDto(11, 1m),
          new LinhaDeMaterialPadraoDto(11, 2m),
          new LinhaDeMaterialPadraoDto(10, 2m),
        ],
        Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("O material 11 aparece mais de uma vez na lista.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Id que nao existe e 400, nao 404: o recurso da rota (o Componente) existe — quem esta errado
  /// e uma LINHA do corpo. E a mensagem nomeia QUAL id, senao o usuario adivinha qual corrigir.
  /// Mensagem INTEIRA, nao substring: "777" casaria com "7770" e com "17777".
  /// </summary>
  [Fact]
  public async Task Material_inexistente_e_recusado_nomeando_o_id()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(777, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("O material 777 nao existe.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Ramo plural do helper compartilhado. Nomeia TODOS os ausentes: quem colou uma receita com
  /// tres ids velhos nao pode ter de descobrir um por vez, a cada POST recusado.
  /// </summary>
  [Fact]
  public async Task Varios_materiais_inexistentes_sao_nomeados_no_plural()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1,
        [new LinhaDeMaterialPadraoDto(777, 1m), new LinhaDeMaterialPadraoDto(10, 1m), new LinhaDeMaterialPadraoDto(888, 1m)],
        Ct);

    Assert.False(r.Sucesso);
    Assert.Equal("Os materiais 777, 888 nao existem.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  [Fact]
  public async Task Material_inativo_nao_pode_entrar_na_receita()
  {
    var fake = FakeComCatalogo();
    fake.Materiais.Single(m => m.Id == 11).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(11, 1m)], Ct);

    Assert.False(r.Sucesso);
    // Mensagem INTEIRA, nao substring: "11" casaria com "110", "211" etc. Se a mensagem mudar de
    // texto, este teste tem de morrer — e a mensagem E o contrato com o usuario aqui.
    Assert.Equal("O material 11 esta inativo e nao pode entrar na receita.", r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>Ramo plural do helper compartilhado, do lado de "inativo".</summary>
  [Fact]
  public async Task Varios_materiais_inativos_sao_nomeados_no_plural()
  {
    var fake = FakeComCatalogo();
    fake.Materiais.Single(m => m.Id == 10).Ativo = false;
    fake.Materiais.Single(m => m.Id == 11).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1, [new LinhaDeMaterialPadraoDto(10, 1m), new LinhaDeMaterialPadraoDto(11, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal("Os materiais 10, 11 estao inativos e nao podem entrar na receita.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Inativar um item DEPOIS nao pode corromper receita ja gravada — catalogo se inativa, nao se
  /// exclui, e a leitura tem que continuar mostrando o que esta la, com os dados do material.
  /// </summary>
  [Fact]
  public async Task Linha_existente_sobrevive_a_inativacao_do_material()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 3m)], Ct);

    fake.Materiais.Single(m => m.Id == 10).Ativo = false;

    var r = await caso.ListarMateriais(1, Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    Assert.Equal(10, linha.MaterialId);
    Assert.Equal("CH-3", linha.Codigo);
    Assert.Equal(3m, linha.QuantidadePadrao);
  }

  [Fact]
  public async Task Listar_materiais_de_componente_inexistente_e_nao_encontrado()
  {
    var caso = new ReceitaPadraoUseCase(FakeComCatalogo());

    var r = await caso.ListarMateriais(999, Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", r.Erro);
  }
}
