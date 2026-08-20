using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

/// <summary>
/// Receita padrao — MATERIAIS (o molde), ROTEIRO e FILHOS. Os tres tem a mesma forma: componente
/// existe, ids existem, ids estao ativos, substituicao inteira.
/// As mensagens de "nao existe" e "esta inativo" nascem de um helper COMPARTILHADO pelos tres
/// sub-recursos, entao os quatro ramos dele (singular/plural x ausente/inativo) sao provados uma
/// vez, na secao de materiais, e nao tres.
///
/// Onde cada um SAI do molde:
/// - o roteiro nao tem guarda de id repetido — setor repetido e valido (retorno ao setor), ver
///   `Mesmo_setor_repetido_no_roteiro_e_aceito`;
/// - filhos tem duas guardas que nenhum outro tem — auto-referencia e CICLO em qualquer
///   profundidade —, e a verificacao de ciclo e sobre o grafo COMO ELE FICARA depois da
///   substituicao, nao sobre o atual (§1.3 da spec).
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
    // campos diferentes do mesmo DTO e trocar um pelo outro tem de matar este teste. `>= 500` e
    // `NotEqual` em vez do literal `500`: nao acopla o teste a exatamente QUAL id o fake emitiu
    // (poderia mudar se `_proximoId` mudar ou um teste futuro gravar no arranjo), mas continua
    // pegando a troca `Id` <-> `MaterialId`.
    Assert.True(linha.Id >= 500);
    Assert.NotEqual(linha.MaterialId, linha.Id);
    Assert.Equal(10, linha.MaterialId);
    Assert.Equal("CH-3", linha.Codigo);
    Assert.Equal("Chapa 3mm", linha.Descricao);
    Assert.Equal("KG", linha.UnidadeMedida);
    Assert.Equal(2.5m, linha.QuantidadePadrao);
    // GRAVOU, e uma vez so. Sem esta assercao, "apaga e regrava" (duas idas ao banco com o mesmo
    // estado final) passa despercebido — as outras assercoes do contador sao todas `== 0`.
    Assert.Equal(1, fake.SubstituicoesDeMateriais);
  }

  /// <summary>
  /// DUAS linhas, e a projecao inteira afirmada EM ORDEM — nao uma contagem.
  ///
  /// Enquanto o caso feliz so era exercitado com uma linha, quatro mutacoes passavam na suite
  /// inteira: gravar so a primeira linha; a guarda de quantidade olhar so a primeira; a projecao
  /// voltar invertida; e a projecao ligar TODA linha ao primeiro material do dicionario (a tela
  /// do PCP mostraria a receita inteira com o material errado repetido). As tres primeiras morrem
  /// aqui — a de quantidade morre no `[Theory]` de recusa, que tambem tem duas linhas.
  ///
  /// A ORDEM importa alem daqui: o roteiro da Task 4 e uma sequencia, e um teste que so conta
  /// linhas nao enxerga ordem nenhuma.
  /// </summary>
  [Fact]
  public async Task Substituir_materiais_com_duas_linhas_projeta_as_duas_em_ordem()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1, [new LinhaDeMaterialPadraoDto(10, 2.5m), new LinhaDeMaterialPadraoDto(11, 4m)], Ct);

    Assert.True(r.Sucesso);
    var linhas = r.Valor!;
    Assert.Equal(
        [(10, "CH-3", "Chapa 3mm", "KG", 2.5m), (11, "PA-1", "Parafuso", "UN", 4m)],
        linhas.Select(l => (l.MaterialId, l.Codigo, l.Descricao, l.UnidadeMedida, l.QuantidadePadrao)));
    // `Id` fora do `Equal` acima de proposito (nao acopla a semente do fake): a exigencia de
    // `MaterialId` exato (10, depois 11) ja mata sozinha a troca `Id` <-> `MaterialId` — o campo
    // `MaterialId` passaria a carregar o `Id` da linha (500+), que nao bate com 10 nem 11. As duas
    // linhas abaixo so reforcam a mesma garantia, explicitamente.
    Assert.All(linhas, l => Assert.True(l.Id >= 500));
    Assert.All(linhas, l => Assert.NotEqual(l.MaterialId, l.Id));
    Assert.Equal(1, fake.SubstituicoesDeMateriais);
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
    // Duas gravacoes, nao tres: a do arranjo e a que apaga.
    Assert.Equal(2, fake.SubstituicoesDeMateriais);
  }

  /// <summary>
  /// A receita e POR componente. Com um unico componente no catalogo, um `1` literal no lugar do
  /// `componenteId` — na gravacao, na leitura ou no `ComponenteId` da linha montada — passaria em
  /// todos os outros testes deste arquivo.
  ///
  /// O retorno das DUAS gravacoes tambem e afirmado, e nao so o das leituras: a resposta do POST e
  /// re-lida, e um `1` literal SO ali faz o POST em /componentes/2 gravar certo e responder com a
  /// receita do componente 1 — a tela mostra a receita de outra peca logo depois de salvar.
  /// </summary>
  [Fact]
  public async Task Receita_de_um_componente_nao_vaza_para_outro()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var gravouNoUm = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);
    var gravouNoDois = await caso.SubstituirMateriais(2, [new LinhaDeMaterialPadraoDto(11, 7m)], Ct);

    var doUm = await caso.ListarMateriais(1, Ct);
    var doDois = await caso.ListarMateriais(2, Ct);

    Assert.Equal([10], gravouNoUm.Valor!.Select(l => l.MaterialId));
    Assert.Equal([11], gravouNoDois.Valor!.Select(l => l.MaterialId));
    Assert.Equal([10], doUm.Valor!.Select(l => l.MaterialId));
    Assert.Equal([11], doDois.Valor!.Select(l => l.MaterialId));
    Assert.Equal(7m, Assert.Single(doDois.Valor!).QuantidadePadrao);
    Assert.Equal(2, fake.SubstituicoesDeMateriais);
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

  /// <summary>
  /// A quantidade invalida esta na SEGUNDA linha de proposito: com uma linha so, uma guarda que
  /// olhasse apenas a primeira linha (`linhas.Take(1).Any(...)`) recusaria igual e a mutacao
  /// sobreviveria.
  ///
  /// Os dois ultimos casos sao a escala da coluna, e nao o sinal. `dbo.ComponenteMaterialPadrao.
  /// QuantidadePadrao` e DECIMAL(18,4): 0,00001 e positivo, passa em `> 0`, e chega ao banco como
  /// 0,0000 — o POST responderia 200 com exatamente a linha de quantidade zero que a primeira
  /// guarda existe para impedir (medido contra o SQL Server real). E 1e17 nao cabe na coluna e
  /// estourava como erro de banco, virando 500 em vez de 400. Decisao: RECUSAR, nao arredondar e
  /// nao mexer no schema. A Task 5 herda a mesma coluna nos filhos.
  /// </summary>
  [Theory]
  [InlineData(0, "Quantidade deve ser maior que zero.")]
  [InlineData(-1, "Quantidade deve ser maior que zero.")]
  [InlineData(0.00001, "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.")]
  [InlineData(1e17, "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.")]
  public async Task Quantidade_invalida_e_recusada(decimal quantidade, string mensagem)
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(
        1, [new LinhaDeMaterialPadraoDto(10, 1m), new LinhaDeMaterialPadraoDto(11, quantidade)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(mensagem, r.Erro);
    // NAO gravou: recusa que grava metade e pior que recusa nenhuma.
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// A PRECEDENCIA entre as guardas e contrato, e nao estetica — hoje a mensagem que o usuario ve
  /// era incidental, e tres reordenacoes passavam na suite inteira. Cada caso cruza duas regras de
  /// proposito, porque uma entrada "pura" (invalida por um motivo so) nao consegue distinguir
  /// ordem nenhuma:
  ///
  /// 1. componente da ROTA inexistente + linha invalida -> 404, nao 400. E a regra que a Task 6
  ///    traduz em status: a tela redireciona por peca inexistente em vez de destacar um campo.
  /// 2. quantidade invalida + id duplicado -> ganha a quantidade.
  /// 3. id duplicado + id inexistente -> ganha a duplicata (o usuario corrige a lista dele antes
  ///    de ouvir sobre o catalogo).
  /// </summary>
  [Theory]
  [InlineData(999, 777, 0, false, TipoDeErro.NaoEncontrado, "Componente nao encontrado.")]
  [InlineData(1, 777, 0, true, TipoDeErro.Validacao, "Quantidade deve ser maior que zero.")]
  [InlineData(1, 777, 1, true, TipoDeErro.Validacao, "O material 777 aparece mais de uma vez na lista.")]
  public async Task Precedencia_das_validacoes_e_fixa(
      int componenteId,
      int materialId,
      decimal quantidade,
      bool duplicar,
      TipoDeErro tipo,
      string mensagem)
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    var linha = new LinhaDeMaterialPadraoDto(materialId, quantidade);

    var r = await caso.SubstituirMateriais(
        componenteId, duplicar ? [linha, linha] : [linha], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(tipo, r.TipoDoErro);
    Assert.Equal(mensagem, r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Duas gravacoes simultaneas do mesmo componente: o banco derruba o perdedor (deadlock ou lock
  /// timeout do range lock do SERIALIZABLE) e o repositorio sobe `ConflitoDeConcorrenciaException`.
  /// Isso e desfecho previsto pelo desenho, nao falha do servidor — vira 409, e nao 500.
  ///
  /// O fake simula a excecao porque o que se prova AQUI e a traducao; que o repositorio real a
  /// levante e provado contra o SQL Server em `ReceitaPadraoRepositoryTests`.
  /// </summary>
  [Fact]
  public async Task Conflito_de_concorrencia_na_gravacao_vira_erro_de_conflito()
  {
    var fake = FakeComCatalogo();
    fake.ConflitoNaProximaSubstituicao = true;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(
        "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.", r.Erro);
  }

  /// <summary>
  /// O `ct` da requisicao tem de chegar a TODAS as idas ao repositorio. Sem esta prova, trocar
  /// qualquer `, ct)` por `, CancellationToken.None)` deixa a suite verde e o cancelamento vira
  /// decorativo — quem cancela a aba nao cancela a consulta.
  ///
  /// Os SEIS metodos publicos sao exercitados aqui, e nao so os de material: um
  /// `CancellationToken.None` plantado dentro de `SubstituirRoteiro` ou de `ProjetarRoteiro`
  /// sobrevivia enquanto este teste so passava por materiais. Os de filhos entraram na Task 5, e
  /// com eles a leitura do grafo (`ListarTodasAsArestasAsync`), que so este teste percorre.
  /// </summary>
  [Fact]
  public async Task Token_de_cancelamento_chega_a_todas_as_chamadas_do_repositorio()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    using var cts = new CancellationTokenSource();

    await caso.SubstituirMateriais(1, [new LinhaDeMaterialPadraoDto(10, 1m)], cts.Token);
    await caso.ListarMateriais(1, cts.Token);
    await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(20)], cts.Token);
    await caso.ListarRoteiro(1, cts.Token);
    await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], cts.Token);
    await caso.ListarFilhos(1, cts.Token);

    Assert.NotEmpty(fake.TokensRecebidos);
    Assert.All(fake.TokensRecebidos, t => Assert.Equal(cts.Token, t));
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

  // ------------------------------------------------------------------ roteiro

  /// <summary>
  /// A `Ordem` sai da POSICAO no array, nunca do cliente e nunca do id.
  ///
  /// Os setores entram FORA de ordem de id de proposito (21 antes de 20): numerar pela ordem dos
  /// ids em vez de pela do array daria `(20,1),(21,2)` e passaria despercebido num array ja
  /// ordenado. E a sequencia inteira e afirmada como tupla, e nao so a contagem: comecar do zero,
  /// usar o indice sem `+1` ou gravar so a primeira linha tem de matar este teste.
  /// </summary>
  [Fact]
  public async Task Roteiro_numera_a_ordem_de_um_ate_n_pela_posicao_do_array()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)], Ct);

    Assert.True(r.Sucesso);
    // A ordem sai da POSICAO, nao do id: 21 veio primeiro, entao 21 e a Ordem 1.
    Assert.Equal([(21, 1), (20, 2)], r.Valor!.Select(l => (l.SetorId, l.Ordem)));
    // Gravou no ROTEIRO, e uma vez so. `SubstituicoesDeRoteiro`, e nao `Substituicoes`: a soma
    // dos tres contadores seria satisfeita por uma gravacao de material.
    Assert.Equal(1, fake.SubstituicoesDeRoteiro);
  }

  /// <summary>
  /// O MESMO setor duas vezes e PERMITIDO: significa RETORNO AO SETOR (a peca volta a usinagem
  /// depois da solda). Este teste prova o PERMITIDO, nao o proibido — sem ele, alguem "corrige"
  /// acrescentando validacao de setor unico (o molde de materiais tem uma), a suite fica verde, e
  /// o retorno ao setor some. O `UQ` do schema e (ComponenteId, Ordem), nao (ComponenteId,
  /// SetorId), entao repetir setor nunca viola constraint nenhuma.
  ///
  /// O setor repetido aparece ADJACENTE (posicoes 1 e 2, o mesmo Setor duas vezes seguidas) E
  /// NAO-adjacente (posicao 4, retorno depois de passar por outro setor): uma guarda que so
  /// recusasse duplicata adjacente tambem tem de morrer aqui, porque a primeira repeticao da
  /// lista ja e adjacente.
  /// </summary>
  [Fact]
  public async Task Mesmo_setor_repetido_no_roteiro_e_aceito()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1,
        [new LinhaDeRoteiroPadraoDto(20), new LinhaDeRoteiroPadraoDto(20),
         new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)],
        Ct);

    Assert.True(r.Sucesso);
    Assert.Equal([(20, 1), (20, 2), (21, 3), (20, 4)], r.Valor!.Select(l => (l.SetorId, l.Ordem)));
    Assert.Equal(1, fake.SubstituicoesDeRoteiro);
  }

  /// <summary>Lista vazia APAGA — unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Roteiro_com_lista_vazia_apaga()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(20)], Ct);

    var r = await caso.SubstituirRoteiro(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
    Assert.Empty(fake.Roteiro);
    // Duas gravacoes, nao tres: a do arranjo e a que apaga.
    Assert.Equal(2, fake.SubstituicoesDeRoteiro);
  }

  /// <summary>
  /// A receita e POR componente, tambem no roteiro. Com um unico componente no catalogo, um `1`
  /// literal no lugar do `componenteId` — na gravacao, na leitura ou no `ComponenteId` da linha
  /// montada — passaria em todos os outros testes de roteiro deste arquivo.
  ///
  /// As DUAS gravacoes tem o retorno afirmado, e nao so as leituras: um `1` literal SO na
  /// re-leitura do POST faz a gravacao em /componentes/2 acertar e a RESPOSTA trazer o roteiro do
  /// componente 1 — a tela mostra a sequencia de outra peca logo depois de salvar. Foi exatamente
  /// esse quarto sitio que escapou na Task 3.
  /// </summary>
  [Fact]
  public async Task Roteiro_de_um_componente_nao_vaza_para_outro()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var gravouNoUm = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(20), new LinhaDeRoteiroPadraoDto(21)], Ct);
    var gravouNoDois = await caso.SubstituirRoteiro(2, [new LinhaDeRoteiroPadraoDto(21)], Ct);

    var doUm = await caso.ListarRoteiro(1, Ct);
    var doDois = await caso.ListarRoteiro(2, Ct);

    Assert.Equal([(20, 1), (21, 2)], gravouNoUm.Valor!.Select(l => (l.SetorId, l.Ordem)));
    Assert.Equal([(21, 1)], gravouNoDois.Valor!.Select(l => (l.SetorId, l.Ordem)));
    Assert.Equal([(20, 1), (21, 2)], doUm.Valor!.Select(l => (l.SetorId, l.Ordem)));
    Assert.Equal([(21, 1)], doDois.Valor!.Select(l => (l.SetorId, l.Ordem)));
    Assert.Equal(2, fake.SubstituicoesDeRoteiro);
  }

  [Fact]
  public async Task Setor_inexistente_no_roteiro_e_recusado_nomeando_o_id()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(888)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    // Mensagem INTEIRA, e o singular do helper compartilhado com o nome de recurso do ROTEIRO:
    // "setor", nao "material". Trocar as duas palavras do call site tem de matar este teste.
    Assert.Equal("O setor 888 nao existe.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// O roteiro e o PRIMEIRO chamador do helper compartilhado que lhe entrega ids REPETIDOS —
  /// materiais nunca entregou, porque a guarda de duplicata dele recusa antes. Isso torna
  /// load-bearing os dois `Distinct()` de `ConferirExistenciaEAtividade`, que a Task 3 tinha
  /// declarado mutantes equivalentes: sem eles, a mensagem sai no plural nomeando o mesmo id duas
  /// vezes ("Os setores 888, 888 nao existem."), que e o texto que o usuario le.
  ///
  /// A declaracao de equivalencia da Task 3 nao estava errada quando foi escrita — ela expirou
  /// com esta task, e por isso este teste existe.
  /// </summary>
  [Fact]
  public async Task Setor_inexistente_repetido_e_nomeado_uma_vez_so()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(888), new LinhaDeRoteiroPadraoDto(888)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal("O setor 888 nao existe.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Extensao deliberada da spec §2.3, que so nomeia componente-filho e material: Setor tem
  /// `Ativo` e e a mesma classe de problema. Ver "Global Constraints" do plano.
  /// </summary>
  [Fact]
  public async Task Setor_inativo_nao_pode_entrar_no_roteiro()
  {
    var fake = FakeComCatalogo();
    fake.Setores.Single(s => s.Id == 21).Ativo = false;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(21)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("O setor 21 esta inativo e nao pode entrar na receita.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// A projecao inteira, com DUAS linhas e em ORDEM — e nao uma contagem. `Ordem` e o contrato
  /// central desta parte, e `Assert.Single` nao enxerga ordem nenhuma.
  ///
  /// Os quatro campos do DTO sao afirmados de uma vez porque tres mutacoes distintas se escondem
  /// entre eles: devolver o `SetorId` no lugar do `Id` da linha (por isso os ids de linha nascem
  /// em 500), ligar TODA linha ao primeiro setor do dicionario (a tela mostraria "Corte, Corte"),
  /// e devolver a lista invertida.
  ///
  /// Os setores entram fora de ordem de id (21, depois 20) para que a sequencia lida nao coincida
  /// com uma ordenacao por id.
  /// </summary>
  [Fact]
  public async Task Listar_roteiro_projeta_a_sequencia_inteira_com_o_nome_do_setor()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirRoteiro(
        1, [new LinhaDeRoteiroPadraoDto(21), new LinhaDeRoteiroPadraoDto(20)], Ct);

    var r = await caso.ListarRoteiro(1, Ct);

    Assert.True(r.Sucesso);
    var linhas = r.Valor!;
    Assert.Equal(
        [(21, "Solda", 1), (20, "Corte", 2)],
        linhas.Select(l => (l.SetorId, l.Nome, l.Ordem)));
    // `Id` fora do `Equal` acima de proposito (nao acopla a semente do fake): a exigencia de
    // `SetorId` exato (21, depois 20) ja mata sozinha a troca `Id` <-> `SetorId` — o campo
    // `SetorId` passaria a carregar o `Id` da linha (500+), que nao bate com 21 nem 20. As duas
    // linhas abaixo so reforcam a mesma garantia, explicitamente.
    Assert.All(linhas, l => Assert.True(l.Id >= 500));
    Assert.All(linhas, l => Assert.NotEqual(l.SetorId, l.Id));
  }

  /// <summary>
  /// O 404 do recurso da ROTA vale nos DOIS metodos de roteiro. Um so teste porque a guarda e a
  /// mesma linha duplicada: sem exercitar `SubstituirRoteiro`, apagar o `if` dela nao quebrava
  /// nada — nenhum outro teste de roteiro chama a escrita com componente inexistente.
  ///
  /// E a escrita nao pode gravar nada nesse caminho: um roteiro gravado sob um `ComponenteId` que
  /// nao existe viola a FK e sobe como 500.
  /// </summary>
  [Fact]
  public async Task Roteiro_de_componente_inexistente_e_nao_encontrado_na_leitura_e_na_escrita()
  {
    var fake = FakeComCatalogo();
    var caso = new ReceitaPadraoUseCase(fake);

    var leitura = await caso.ListarRoteiro(999, Ct);
    var escrita = await caso.SubstituirRoteiro(999, [new LinhaDeRoteiroPadraoDto(888)], Ct);

    Assert.False(leitura.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, leitura.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", leitura.Erro);
    Assert.False(escrita.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, escrita.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", escrita.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Mesma traducao do 409 dos materiais, no roteiro. O `catch` nasceu cobrindo so materiais —
  /// roteiro nao existia — e nada obrigava a estende-lo: sem este teste, a substituicao de roteiro
  /// derrubada pelo banco subia `ConflitoDeConcorrenciaException` crua e virava 500, quando o
  /// desfecho e previsto pelo desenho e o cliente so precisa refazer o POST.
  /// </summary>
  [Fact]
  public async Task Conflito_de_concorrencia_na_gravacao_do_roteiro_vira_erro_de_conflito()
  {
    var fake = FakeComCatalogo();
    fake.ConflitoNaProximaSubstituicao = true;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(20)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(
        "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.", r.Erro);
  }

  /// <summary>
  /// O `try` de `SubstituirRoteiro` e ESTREITO de proposito: envolve so a gravacao, nao a
  /// releitura que monta a resposta. Um `ConflitoDeConcorrenciaException` que suba da releitura
  /// NAO pode virar 409 "tente de novo" — a gravacao ja aconteceu, e dizer ao cliente que a
  /// operacao falhou seria mentira. Este teste faz a releitura estourar e afirma que a excecao
  /// sobe CRUA, sem virar `Result` nenhum. Relevante para a Task 5: la o `try` vai conviver com a
  /// leitura do grafo e a deteccao de ciclo, e um `catch` largo la transformaria bug real em 409
  /// falso.
  /// </summary>
  [Fact]
  public async Task Falha_na_releitura_apos_gravar_o_roteiro_nao_vira_conflito()
  {
    var fake = FakeComCatalogo();
    fake.ConflitoNaProximaListagemDeRoteiro = true;
    var caso = new ReceitaPadraoUseCase(fake);

    await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(
        () => caso.SubstituirRoteiro(1, [new LinhaDeRoteiroPadraoDto(20)], Ct));

    // A gravacao ACONTECEU antes da excecao: prova que o `try` estreito nao a impediu de rodar.
    Assert.Equal(1, fake.SubstituicoesDeRoteiro);
  }

  // ------------------------------------------------------------------ filhos

  /// <summary>
  /// Fake com os componentes 1..4 ativos, para montar CADEIAS de filhos (1 -> 2 -> 3 -> 4).
  ///
  /// So o 3 e o 4 sao acrescentados: `FakeComCatalogo` ja traz o 1 ("PAI") e o 2 ("OUTRO"). O
  /// helper como o plano o escrevia acrescentava 2, 3 e 4, o que poe DUAS linhas de Id 2 na lista
  /// e faz o `SingleOrDefault` de `ObterComponenteAsync` estourar `InvalidOperationException` —
  /// nao um teste vermelho legivel, um estouro. Codigo e Descricao continuam distintos por
  /// componente, que e o que mata a projecao que liga toda linha ao mesmo filho.
  /// </summary>
  private static FakeReceitaPadraoRepo FakeComQuatroComponentes()
  {
    var f = FakeComCatalogo();
    foreach (var id in new[] { 3, 4 })
      f.Componentes.Add(new Componente
      {
        Id = id, Codigo = $"C{id}", Descricao = $"Componente {id}", Tipo = "Fabricado", Ativo = true,
      });
    return f;
  }

  /// <summary>Aresta pai -> filho ja existente no grafo, para arranjar os cenarios de ciclo.</summary>
  private static void Aresta(FakeReceitaPadraoRepo fake, int pai, int filho) =>
      fake.Filhos.Add(new ComponenteFilhoPadrao
      {
        Id = 100 + fake.Filhos.Count, ComponentePaiId = pai, ComponenteFilhoId = filho,
        QuantidadePadrao = 1m,
      });

  [Fact]
  public async Task Substituir_filhos_grava_e_projeta_os_dados_do_componente_filho()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 3m)], Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    // O `Id` e o da LINHA da receita (identity do fake, faixa 500+), nao o do Componente filho —
    // mesma forma dos materiais e do roteiro: `>= 500` + `NotEqual` em vez do literal `500`, que
    // acoplaria o teste a exatamente qual id o fake emitiu.
    Assert.True(linha.Id >= 500);
    Assert.NotEqual(linha.ComponenteFilhoId, linha.Id);
    Assert.Equal(2, linha.ComponenteFilhoId);
    Assert.Equal("OUTRO", linha.Codigo);
    Assert.Equal("Outro", linha.Descricao);
    Assert.Equal(3m, linha.QuantidadePadrao);
    // GRAVOU, e uma vez so — sem isto, "apaga e regrava" (duas idas ao banco, estado final igual)
    // passa despercebido, porque as outras assercoes de contador sao todas `== 0`.
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// A auto-referencia e recusada com mensagem PROPRIA, e nao com a de ciclo. A entrada e invalida
  /// por dois motivos ao mesmo tempo — A -> A tambem e ciclo, e a travessia pegaria —, entao
  /// afirmar a mensagem inteira e o que prende a precedencia: trocar a ordem das duas guardas faz
  /// o usuario ler "criaria um ciclo" onde "nao pode ser filho de si mesmo" e mais util.
  ///
  /// A auto-referencia esta na SEGUNDA linha de proposito: com uma linha so, uma guarda que
  /// olhasse apenas a primeira (`linhas.Take(1).Any(...)`) recusaria igual e a mutacao sobreviveria.
  /// </summary>
  [Fact]
  public async Task Auto_referencia_e_recusada_com_mensagem_propria()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(2, 1m), new LinhaDeFilhoPadraoDto(1, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("Um componente nao pode ser filho de si mesmo.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// O CK do banco (`CK_ComponenteFilhoPadrao_NaoAutoReferencia`) so pega A -> A. Este e
  /// A -> B -> A, que o banco ACEITA e que faria a copia recursiva da Fase 2 girar para sempre.
  /// </summary>
  [Fact]
  public async Task Ciclo_de_dois_niveis_e_recusado()
  {
    var fake = FakeComQuatroComponentes();
    Aresta(fake, pai: 2, filho: 1);   // ja existe 2 -> 1; tentamos 1 -> 2, fechando 1 -> 2 -> 1
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(
        "Esta receita criaria um ciclo: o componente apareceria dentro da propria estrutura.",
        r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Profundidade 3: 1 -> 2 -> 3 -> 1. Prova que a busca nao para no primeiro nivel. O filho que
  /// fecha o ciclo (2) esta na SEGUNDA linha da lista, atras de um filho inocente (4): uma
  /// travessia que so partisse da primeira linha nova sobreviveria com o ciclo em qualquer posicao
  /// que nao a primeira.
  /// </summary>
  [Fact]
  public async Task Ciclo_de_tres_niveis_e_recusado()
  {
    var fake = FakeComQuatroComponentes();
    Aresta(fake, pai: 2, filho: 3);
    Aresta(fake, pai: 3, filho: 1);
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(4, 1m), new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(
        "Esta receita criaria um ciclo: o componente apareceria dentro da propria estrutura.",
        r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Diamante: 1 -> 2, 1 -> 3, 2 -> 4, 3 -> 4. O componente 4 e alcancavel por DOIS caminhos, e
  /// isso NAO e ciclo. Se a busca confundir "ja visitei" com "e ciclo", este teste pega.
  ///
  /// A projecao inteira e afirmada como tupla EM ORDEM, e nao so a contagem (mesmo formato de
  /// `Substituir_materiais_com_duas_linhas_projeta_as_duas_em_ordem`): `Assert.Equal(2,
  /// r.Valor!.Count)` nao discrimina `Take(1)` na montagem, projecao invertida nem
  /// `componentes.Values.First()` ligando toda linha ao mesmo componente filho.
  ///
  /// O `Id` fica fora da tupla de proposito (nao acopla a semente do fake): exigir
  /// `ComponenteFilhoId` exato (2, depois 3) ja mata sozinho a troca `Id` &lt;-&gt;
  /// `ComponenteFilhoId`, porque `ComponenteFilhoId` passaria a carregar o id da linha (500+). As
  /// duas linhas de `Assert.All` reforcam a mesma garantia explicitamente.
  /// </summary>
  [Fact]
  public async Task Grafo_em_diamante_nao_e_ciclo()
  {
    var fake = FakeComQuatroComponentes();
    Aresta(fake, pai: 2, filho: 4);
    Aresta(fake, pai: 3, filho: 4);
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(2, 1m), new LinhaDeFilhoPadraoDto(3, 2m)], Ct);

    Assert.True(r.Sucesso);
    var linhas = r.Valor!;
    Assert.Equal(
        [(2, "OUTRO", "Outro", 1m), (3, "C3", "Componente 3", 2m)],
        linhas.Select(l => (l.ComponenteFilhoId, l.Codigo, l.Descricao, l.QuantidadePadrao)));
    Assert.All(linhas, l => Assert.True(l.Id >= 500));
    Assert.All(linhas, l => Assert.NotEqual(l.ComponenteFilhoId, l.Id));
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// O TESTE QUE DEFINE A REGRA (§1.3 da spec). O grafo ja tem o ciclo 1 -> 2 -> 1. O usuario
  /// manda a receita de 1 SEM o filho 2 — ou seja, ele esta CONSERTANDO o ciclo. Isso tem que ser
  /// ACEITO: validar contra o grafo ATUAL (ou contra o atual MAIS as linhas novas) o deixaria
  /// preso, sem saida pela API — a unica correcao possivel seria SQL na mao.
  ///
  /// E a mutacao que este teste existe para matar e a UNICA que nenhum outro pega: trocar o filtro
  /// `a.ComponentePaiId != componenteId` por `true` mantem TODOS os testes de ciclo proibido
  /// verdes.
  /// </summary>
  [Fact]
  public async Task Substituicao_que_desfaz_um_ciclo_preexistente_e_aceita()
  {
    var fake = FakeComQuatroComponentes();
    Aresta(fake, pai: 1, filho: 2);
    Aresta(fake, pai: 2, filho: 1);
    var caso = new ReceitaPadraoUseCase(fake);

    // A receita de 1 passa a ser so o filho 3 — a aresta 1 -> 2 SOME, e o ciclo com ela.
    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(3, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3, Assert.Single(r.Valor!).ComponenteFilhoId);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Um ciclo preexistente em OUTRA parte do grafo (3 -> 4 -> 3) nao pode nem travar a edicao de
  /// 1, nem fazer a busca girar para sempre.
  ///
  /// Este teste nao falha por assercao quando o conjunto de visitados some: ele NAO TERMINA. Medido
  /// — trocando `if (!visitados.Add(atual)) continue;` por `visitados.Add(atual);`, a execucao
  /// filtrada neste teste passou de 300 s sem concluir e teve de ser morta de fora. Nao ha timeout
  /// no runner que transforme isso em vermelho legivel; a defesa e a travessia estar certa.
  ///
  /// A edicao ALCANCA o ramo sujo de proposito (1 -> 3, e 3 esta dentro do ciclo): uma travessia
  /// sem `visitados` que so andasse por um ramo isolado terminaria mesmo assim, e o teste passaria
  /// sem exercitar nada.
  /// </summary>
  [Fact]
  public async Task Ciclo_preexistente_em_outro_ramo_nao_impede_editar_este_componente()
  {
    var fake = FakeComQuatroComponentes();
    Aresta(fake, pai: 3, filho: 4);
    Aresta(fake, pai: 4, filho: 3);
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(3, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(3, Assert.Single(r.Valor!).ComponenteFilhoId);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Diferente do roteiro, filho repetido e PROIBIDO — `UQ_ComponenteFilhoPadrao (ComponentePaiId,
  /// ComponenteFilhoId)` nao aceita a segunda linha, e sem esta guarda a violacao sairia como erro
  /// de banco (500) em vez de 400 legivel. "Duas unidades do mesmo filho" se escreve na
  /// quantidade, nao repetindo a linha.
  ///
  /// A lista tem DUAS duplicatas de proposito, e nao uma: 3 se repete primeiro (indice 2), 2 so
  /// depois (indice 3). Com uma duplicata so, "o primeiro repetido" e "o ultimo repetido" dao a
  /// mesma resposta e a ordem que o helper promete nao estaria provada.
  /// </summary>
  [Fact]
  public async Task Filho_repetido_na_mesma_lista_e_recusado_nomeando_o_primeiro()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1,
        [
          new LinhaDeFilhoPadraoDto(2, 1m),
          new LinhaDeFilhoPadraoDto(3, 1m),
          new LinhaDeFilhoPadraoDto(3, 2m),
          new LinhaDeFilhoPadraoDto(2, 2m),
        ],
        Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("O componente 3 aparece mais de uma vez na lista.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// Entrada invalida por DOIS motivos: o filho 2 esta inativo E a aresta 2 -> 1 ja existe, entao
  /// gravar 1 -> 2 fecharia um ciclo. A mensagem afirmada e a de INATIVIDADE, o que prende a
  /// precedencia — mover a deteccao de ciclo para antes da checagem de catalogo faria o usuario
  /// ler "criaria um ciclo" quando o problema real e um componente desativado.
  /// </summary>
  [Fact]
  public async Task Componente_filho_inativo_nao_pode_entrar_na_receita()
  {
    var fake = FakeComQuatroComponentes();
    fake.Componentes.Single(c => c.Id == 2).Ativo = false;
    Aresta(fake, pai: 2, filho: 1);
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    // Mensagem INTEIRA, nao `Assert.Contains("2", ...)`: a substring "2" casa com "12", "20" e ate
    // com o proprio texto da mensagem. E o mesmo defeito que a review da Task 11 da Fase 1D achou
    // (`toContain('1')` casando com "41") — nao repetir.
    Assert.Equal("O componente 2 esta inativo e nao pode entrar na receita.", r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>Lista vazia APAGA — unico caminho de remocao que existe (nao ha DELETE).</summary>
  [Fact]
  public async Task Filhos_com_lista_vazia_apaga()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    var r = await caso.SubstituirFilhos(1, [], Ct);

    Assert.True(r.Sucesso);
    Assert.Empty(r.Valor!);
    Assert.Empty(fake.Filhos);
    // Duas gravacoes, nao tres: a do arranjo e a que apaga.
    Assert.Equal(2, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// OUTRO teste do PERMITIDO (§1.4 da spec): o `Tipo` do Componente NAO restringe a receita.
  /// Um `Bruto` pode ter filhos e uma `Montagem` pode nao ter nenhum — o tipo e rotulo descritivo.
  /// Sem este teste, alguem acrescenta "so Montagem tem filhos" achando que corrige o modelo, a
  /// suite fica verde, e uma regra que o usuario DESCARTOU explicitamente entra pela porta dos
  /// fundos.
  ///
  /// O filho tambem e `Bruto`: uma guarda escrita sobre o tipo do FILHO ("Bruto nao tem filhos,
  /// entao nao pode ser pai nem aparecer como filho") tem de morrer aqui tambem.
  /// </summary>
  [Fact]
  public async Task Componente_do_tipo_Bruto_pode_ter_filhos()
  {
    var fake = FakeComQuatroComponentes();
    fake.Componentes.Single(c => c.Id == 1).Tipo = "Bruto";
    fake.Componentes.Single(c => c.Id == 2).Tipo = "Bruto";
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.True(r.Sucesso);
    Assert.Equal(2, Assert.Single(r.Valor!).ComponenteFilhoId);
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Mesma traducao do 409 dos materiais e do roteiro, agora em filhos: a transacao SERIALIZABLE e
  /// do repositorio, nao da tabela, entao quem grava filhos e derrubado pelo mesmo motivo. Sem este
  /// teste o `catch` nasceria faltando aqui e a substituicao derrubada pelo banco viraria 500.
  /// </summary>
  [Fact]
  public async Task Conflito_de_concorrencia_na_gravacao_dos_filhos_vira_erro_de_conflito()
  {
    var fake = FakeComQuatroComponentes();
    fake.ConflitoNaProximaSubstituicao = true;
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(
        "A receita deste componente esta sendo alterada por outra gravacao. Tente de novo.", r.Erro);
  }

  /// <summary>
  /// O `try` de `SubstituirFilhos` e ESTREITO de proposito, e aqui isso importa MAIS que no
  /// roteiro: o metodo faz a leitura do grafo inteiro e a travessia de ciclo antes de gravar, entao
  /// um `catch` largo transformaria bug real — `KeyNotFoundException`, `NullReference`, erro na
  /// montagem do grafo — em 409 "tente de novo", que e a pior resposta possivel: o usuario reenvia
  /// e falha de novo, sem sintoma util. Este teste faz a RELEITURA (a que monta a resposta)
  /// estourar e afirma que a excecao sobe CRUA, sem virar `Result` nenhum.
  /// </summary>
  [Fact]
  public async Task Falha_na_releitura_apos_gravar_os_filhos_nao_vira_conflito()
  {
    var fake = FakeComQuatroComponentes();
    fake.ConflitoNaProximaListagemDeFilhos = true;
    var caso = new ReceitaPadraoUseCase(fake);

    await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(
        () => caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(2, 1m)], Ct));

    // A gravacao ACONTECEU antes da excecao: prova que o `try` estreito nao a impediu de rodar.
    Assert.Equal(1, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// O 404 do recurso da ROTA vale nos DOIS metodos de filhos. Um so teste porque a guarda e a
  /// mesma linha duplicada: sem exercitar `SubstituirFilhos`, apagar o `if` dela nao quebraria
  /// nada.
  ///
  /// O filho do corpo (777) tambem NAO existe, de proposito: com um filho valido, a entrada seria
  /// invalida por um motivo so, o 404 dispararia de qualquer jeito e a PRECEDENCIA ficaria
  /// invisivel — mover a guarda de 404 para depois da checagem de catalogo deixaria a suite verde.
  /// E a que a Task 6 traduz em status HTTP: redirecionar por peca inexistente vs. destacar um
  /// campo. Foi o achado I2 da review da Task 4, aqui desde o inicio.
  /// </summary>
  [Fact]
  public async Task Filhos_de_componente_inexistente_sao_nao_encontrados_na_leitura_e_na_escrita()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var leitura = await caso.ListarFilhos(999, Ct);
    var escrita = await caso.SubstituirFilhos(999, [new LinhaDeFilhoPadraoDto(777, 1m)], Ct);

    Assert.False(leitura.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, leitura.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", leitura.Erro);
    Assert.False(escrita.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, escrita.TipoDoErro);
    Assert.Equal("Componente nao encontrado.", escrita.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// A receita e POR componente, tambem nos filhos. Um `1` literal no lugar do `componenteId` — na
  /// gravacao, no `ComponentePaiId` da linha montada, na re-leitura do POST ou na leitura — passaria
  /// em todos os outros testes de filhos deste arquivo.
  ///
  /// O retorno das DUAS gravacoes e afirmado, e nao so o das leituras: um `1` literal SO na
  /// re-leitura faz o POST em /componentes/2 gravar certo e responder com a receita do componente
  /// 1 — a tela mostra a receita de outra peca logo depois de salvar. Foi o quarto sitio que
  /// escapou na Task 3.
  /// </summary>
  [Fact]
  public async Task Filhos_de_um_componente_nao_vazam_para_outro()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var gravouNoUm = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(3, 1m), new LinhaDeFilhoPadraoDto(4, 2m)], Ct);
    var gravouNoDois = await caso.SubstituirFilhos(2, [new LinhaDeFilhoPadraoDto(4, 7m)], Ct);

    var doUm = await caso.ListarFilhos(1, Ct);
    var doDois = await caso.ListarFilhos(2, Ct);

    Assert.Equal([(3, 1m), (4, 2m)], gravouNoUm.Valor!.Select(l => (l.ComponenteFilhoId, l.QuantidadePadrao)));
    Assert.Equal([(4, 7m)], gravouNoDois.Valor!.Select(l => (l.ComponenteFilhoId, l.QuantidadePadrao)));
    Assert.Equal([(3, 1m), (4, 2m)], doUm.Valor!.Select(l => (l.ComponenteFilhoId, l.QuantidadePadrao)));
    Assert.Equal([(4, 7m)], doDois.Valor!.Select(l => (l.ComponenteFilhoId, l.QuantidadePadrao)));
    Assert.Equal(2, fake.SubstituicoesDeFilhos);
  }

  /// <summary>
  /// Inativar um componente DEPOIS nao pode corromper receita ja gravada — catalogo se inativa, nao
  /// se exclui, e a leitura tem de continuar mostrando o que esta la, com os dados do filho. Sem
  /// este teste, filtrar por `Ativo` dentro de `ProjetarFilhos` faria a leitura estourar
  /// `KeyNotFoundException` (500) numa receita perfeitamente valida, e a suite ficaria verde: o
  /// gemeo dos materiais nao cobre a projecao de filhos, que e outro codigo.
  /// </summary>
  [Fact]
  public async Task Filho_existente_sobrevive_a_inativacao_do_componente_filho()
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);
    await caso.SubstituirFilhos(1, [new LinhaDeFilhoPadraoDto(3, 5m)], Ct);

    fake.Componentes.Single(c => c.Id == 3).Ativo = false;

    var r = await caso.ListarFilhos(1, Ct);

    Assert.True(r.Sucesso);
    var linha = Assert.Single(r.Valor!);
    Assert.Equal(3, linha.ComponenteFilhoId);
    Assert.Equal("C3", linha.Codigo);
    Assert.Equal(5m, linha.QuantidadePadrao);
  }

  /// <summary>
  /// O gemeo, nos filhos, do `[Theory]` de quantidade dos materiais —
  /// `dbo.ComponenteFilhoPadrao.QuantidadePadrao` e o MESMO `DECIMAL(18,4)`
  /// (`specs/02-modelo-de-dados.sql`), entao a coluna tem as mesmas duas armadilhas. O plano desta
  /// task so previa a guarda de `&lt;= 0`; sem a de escala, 0,00001 e positivo, passa em `&gt; 0`, e
  /// chega ao banco como 0,0000 — o POST responderia 200 com exatamente a linha de quantidade zero
  /// que a primeira guarda existe para impedir. E 1e17 nao cabe na coluna e estouraria como erro de
  /// banco, virando 500 em vez de 400.
  ///
  /// A quantidade invalida esta na SEGUNDA linha de proposito: com uma linha so, uma guarda que
  /// olhasse apenas a primeira (`linhas.Take(1).Any(...)`) recusaria igual.
  /// </summary>
  [Theory]
  [InlineData(0, "Quantidade deve ser maior que zero.")]
  [InlineData(-1, "Quantidade deve ser maior que zero.")]
  [InlineData(0.00001, "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.")]
  [InlineData(1e17, "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.")]
  public async Task Quantidade_de_filho_invalida_e_recusada(decimal quantidade, string mensagem)
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);

    var r = await caso.SubstituirFilhos(
        1, [new LinhaDeFilhoPadraoDto(2, 1m), new LinhaDeFilhoPadraoDto(3, quantidade)], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(mensagem, r.Erro);
    // NAO gravou: recusa que grava metade e pior que recusa nenhuma.
    Assert.Equal(0, fake.Substituicoes);
  }

  /// <summary>
  /// A PRECEDENCIA entre as guardas de filhos e contrato, e nao estetica — e aqui ela tem MAIS
  /// niveis que nos materiais e no roteiro: 404 do pai, quantidade, escala, auto-referencia,
  /// duplicata, existencia/atividade e ciclo. Cada caso cruza duas regras de proposito, porque uma
  /// entrada "pura" (invalida por um motivo so) nao consegue distinguir ordem nenhuma — foi
  /// exatamente esse o defeito que a review da Task 4 achou em tres testes de roteiro.
  ///
  /// 1. componente da ROTA inexistente + quantidade zero + filho inexistente -> 404, nao 400. E a
  ///    regra que a Task 6 traduz em status: a tela redireciona por peca inexistente em vez de
  ///    destacar um campo.
  /// 2. quantidade invalida + auto-referencia -> ganha a quantidade.
  /// 3. escala invalida + auto-referencia -> ganha a escala.
  /// 4. auto-referencia + id duplicado -> ganha a auto-referencia (mensagem propria, ver
  ///    `Auto_referencia_e_recusada_com_mensagem_propria`).
  /// 5. id duplicado + id inexistente -> ganha a duplicata (o usuario corrige a lista dele antes de
  ///    ouvir sobre o catalogo).
  ///
  /// O par restante — existencia/atividade contra CICLO — nao cabe aqui porque exige arranjar o
  /// grafo; ele esta em `Componente_filho_inativo_nao_pode_entrar_na_receita`.
  /// </summary>
  [Theory]
  [InlineData(999, 777, 0, false, TipoDeErro.NaoEncontrado, "Componente nao encontrado.")]
  [InlineData(1, 1, 0, false, TipoDeErro.Validacao, "Quantidade deve ser maior que zero.")]
  [InlineData(1, 1, 0.00001, false, TipoDeErro.Validacao,
      "Quantidade deve ter no maximo 4 casas decimais e no maximo 14 digitos inteiros.")]
  [InlineData(1, 1, 1, true, TipoDeErro.Validacao, "Um componente nao pode ser filho de si mesmo.")]
  [InlineData(1, 777, 1, true, TipoDeErro.Validacao,
      "O componente 777 aparece mais de uma vez na lista.")]
  public async Task Precedencia_das_validacoes_de_filhos_e_fixa(
      int componenteId,
      int filhoId,
      decimal quantidade,
      bool duplicar,
      TipoDeErro tipo,
      string mensagem)
  {
    var fake = FakeComQuatroComponentes();
    var caso = new ReceitaPadraoUseCase(fake);
    var linha = new LinhaDeFilhoPadraoDto(filhoId, quantidade);

    var r = await caso.SubstituirFilhos(componenteId, duplicar ? [linha, linha] : [linha], Ct);

    Assert.False(r.Sucesso);
    Assert.Equal(tipo, r.TipoDoErro);
    Assert.Equal(mensagem, r.Erro);
    Assert.Equal(0, fake.Substituicoes);
  }
}
