using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

/// <summary>
/// Receita padrao — MATERIAIS (o molde) e ROTEIRO. Filhos (Task 5) copiam a mesma forma:
/// componente existe, ids existem, ids estao ativos, substituicao inteira.
/// As mensagens de "nao existe" e "esta inativo" nascem de um helper COMPARTILHADO pelos tres
/// sub-recursos, entao os quatro ramos dele (singular/plural x ausente/inativo) sao provados uma
/// vez, na secao de materiais, e nao tres.
///
/// O roteiro NAO herda uma coisa do molde: a guarda de id repetido. Setor repetido e valido —
/// ver `Mesmo_setor_repetido_no_roteiro_e_aceito`.
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
  /// Os QUATRO metodos publicos que existem hoje sao exercitados aqui, e nao so os de material: um
  /// `CancellationToken.None` plantado dentro de `SubstituirRoteiro` ou de `ProjetarRoteiro`
  /// sobrevivia enquanto este teste so passava por materiais. A Task 5 acrescenta os de filhos.
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
}
