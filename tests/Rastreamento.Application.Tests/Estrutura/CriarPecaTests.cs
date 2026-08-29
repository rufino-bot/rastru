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
    // FakePedidoRepo vazio: nenhum destes testes precisa de Pedido de verdade — `_pedidos` so e
    // consultado e descartado (ver comentario em MontagemDeEstruturaUseCase.CriarPeca), entao
    // devolver null e inocuo. `Criar_Peca_em_Pedido_fora_de_Aberto_e_permitido...` usa o helper
    // dedicado abaixo, com um Pedido de verdade.
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentosRepo, catalogo, new FakePedidoRepo());
    return (useCase, estruturas, agrupamentosRepo, catalogo);
  }

  /// <summary>
  /// Variante de <see cref="Montar"/> com um Pedido de verdade, alcancavel pelo caso de uso via
  /// `IPedidoRepository` — usada so por
  /// `Criar_Peca_em_Pedido_fora_de_Aberto_e_permitido_regra_de_dominio_2026_08_29`, que precisa que
  /// o Pedido seja de fato consultavel (Minor 7 da review da Task 3).
  /// </summary>
  private static (MontagemDeEstruturaUseCase UseCase, FakeEstruturaRepo Estruturas) MontarComPedido(
      Agrupamento agrupamento, Pedido pedido)
  {
    var estruturas = new FakeEstruturaRepo();
    var agrupamentosRepo = new FakeAgrupamentoRepo(agrupamento);
    var catalogo = new FakeReceitaPadraoRepo();
    var pedidosRepo = new FakePedidoRepo(pedido);
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentosRepo, catalogo, pedidosRepo);
    return (useCase, estruturas);
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
    // Important 2 da review da Task 3: `Erro` carrega o CODIGO (o que o front comuta), `Detalhe`
    // carrega a FRASE que nomeia o caminho do ciclo — antes descartada.
    Assert.Equal(PlanejadorDeCopia.CodigoDeCiclo, resultado.Erro);
    Assert.NotNull(resultado.Detalhe);
    Assert.Contains("1 -> 2 -> 1", resultado.Detalhe);
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
  public async Task Criar_Peca_em_Pedido_fora_de_Aberto_e_permitido_regra_de_dominio_2026_08_29()
  {
    // Decisao de dominio (2026-08-29): cliente grande pede alteracao de projeto com o Pedido JA em
    // execucao, e acrescentar Peca nova ao pedido rodando e o comportamento PADRAO — nao excecao.
    //
    // Minor 7 da review da Task 3: o teste anterior montava um Pedido solto, nunca ligado a nada, e
    // afirmava contra o proprio literal que acabara de escrever — passava mesmo que o codigo nunca
    // olhasse Status. Aqui o Pedido esta de fato alcancavel pelo caso de uso (via
    // `MontarComPedido`, que injeta um `IPedidoRepository` real), com `Status` fora de "Aberto", e
    // a assercao e sobre o DESFECHO (Sucesso + gravacao), nao sobre o arranjo.
    //
    // Medido em 2026-08-29 (ver relatorio do fix pass): trocando o `_ = await _pedidos...` de
    // `CriarPeca` por `if (pedido?.Status != "Aberto") return Falha(...)`, ESTE teste morre —
    // `Assert.True(resultado.Sucesso)` falha — e nenhum outro `CriarPecaTests` morre junto (o
    // `FakePedidoRepo` de `Montar()` fica vazio nos demais, entao a guarda hipotetica nem
    // dispararia neles). Guarda revertida apos a medicao.
    var pedido = new Pedido
    {
      Id = 1, Numero = "PED-01", Cliente = "Cliente X", Tipo = "Normal", Status = "EmProducao",
      DataAbertura = DateTime.UtcNow, CriadoPorUsuarioId = 1,
    };
    var agrupamento = new Agrupamento { Id = 1, PedidoId = pedido.Id, Codigo = "AG-01", Tipo = "Kit" };
    var (useCase, estruturas) = MontarComPedido(agrupamento, pedido);

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, estruturas.GravacoesDeArvore);
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

  [Fact]
  public async Task Quantidade_positiva_abaixo_do_piso_da_coluna_e_recusada()
  {
    // Important 1 da review da Task 3, segunda metade: 0,00001 e POSITIVO (passa num check so de
    // sinal) mas e menor que o piso da coluna DECIMAL(18,4) — gravado, viraria 0,0000 em silencio,
    // uma Peca de quantidade ZERO quebrando a conservacao de quantidade da Fase 3 sem erro nenhum.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 0.00001m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Quantidade_de_entrada_acima_do_teto_da_coluna_e_recusada_com_validacao_nao_500()
  {
    // Important 1 da review da Task 3, primeira metade: 1e15 e POSITIVO, longe do teto do TIPO
    // `decimal` (~7,9e28, guardado pelo catch de OverflowException) — mas ultrapassa o teto REAL da
    // coluna EstruturaItem.Quantidade DECIMAL(18,4) (~9,99e13). Sem receita nenhuma: o proprio valor
    // de entrada ja excede sozinho. Sem esta guarda, isto chegaria ao INSERT como DbUpdateException
    // nao tratada -> 500 — exatamente o desfecho que a decisao antiga dizia estar fechando.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    // Componente 1 sem receita cadastrada: o unico no da arvore e a propria raiz.

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1_000_000_000_000_000m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Quantidade_acumulada_no_produto_da_receita_ultrapassa_o_teto_da_coluna_e_e_recusada()
  {
    // Important 1 da review da Task 3: a raiz (1e7) cabe folgada na coluna sozinha — o estouro
    // nasce da MULTIPLICACAO ao descer, nao do valor de entrada. Fator de receita 1e7 faz o filho
    // ser 1e7 x 1e7 = 1e14, que ultrapassa o teto (~9,99e13). Prova que o guard tem de correr em
    // TODO no da descida, nao so na raiz.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 10_000_000m));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 10_000_000m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }
}
