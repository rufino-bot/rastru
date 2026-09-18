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
    // Semeia o Componente 1 COM solido (regra 18, Task 5): sem isso, a guarda nova recusaria toda
    // Peca criada por um teste que nao arranja o proprio catalogo, e o teste falharia por cenario
    // incompleto, nao por regressao. Quem quer o caso negativo (sem solido) sobrescreve
    // explicitamente com `catalogo.Componentes.Clear()` + `ComponenteSemSolido`.
    catalogo.Componentes.Add(ComponenteComSolido(1));
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
    // Mesmo motivo do Componente 1 semeado em `Montar` (regra 18, Task 5): este teste cria uma
    // Peca de verdade e morreria por cenario incompleto, nao pela guarda de status que ele quer provar.
    catalogo.Componentes.Add(ComponenteComSolido(1));
    var pedidosRepo = new FakePedidoRepo(pedido);
    var useCase = new MontagemDeEstruturaUseCase(estruturas, agrupamentosRepo, catalogo, pedidosRepo);
    return (useCase, estruturas);
  }

  private static Componente NovoComponente(int id, string codigo, string descricao) =>
      new() { Id = id, Codigo = codigo, Descricao = descricao, Tipo = "Montagem", Ativo = true };

  /// <summary>
  /// Regra 18: Componente SEM solido — a guarda de `CriarPeca` recusa a Peca criada a partir dele.
  /// </summary>
  private static Componente ComponenteSemSolido(int id) =>
      NovoComponente(id, $"CSS-{id}", "Componente sem solido");

  /// <summary>
  /// Regra 18: Componente COM solido — `ArquivoSolidoId` = 700 + id, faixa alta de proposito, fora
  /// dos ids de catalogo usados nestes testes (1, 2, 5), para que uma projecao que devolva o campo
  /// errado nao acerte por coincidencia numerica.
  /// </summary>
  private static Componente ComponenteComSolido(int id) =>
      new()
      {
        Id = id, Codigo = $"CCS-{id}", Descricao = "Componente com solido", Tipo = "Montagem",
        Ativo = true, ArquivoSolidoId = 700 + id,
      };

  [Fact]
  public async Task Peca_e_criada_e_a_arvore_da_receita_vem_junto()
  {
    var (useCase, estruturas, _, catalogo) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 4m));
    // Substitui o Componente 1 semeado por `Montar` (mesmo Id, com solido) em vez de adicionar um
    // segundo: `Montar` ja semeia o Id 1, e o fake usa `SingleOrDefault` — adicionar de novo
    // duplicaria o Id na lista. Codigo e descricao continuam "C1"/"Peca Um", que
    // `raiz.CodigoDoComponente` e `raiz.Descricao` conferem.
    catalogo.Componentes.Clear();
    catalogo.Componentes.Add(new Componente
    { Id = 1, Codigo = "C1", Descricao = "Peca Um", Tipo = "Montagem", Ativo = true, ArquivoSolidoId = 701 });
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
    var (useCase, estruturas, _, catalogo) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Avulso" });
    // Nenhuma aresta em estruturas.ReceitaFilhos: o Componente 5 nao tem receita cadastrada. Com
    // solido (regra 18) para nao ser confundido com o caso, testado a parte, de Componente
    // inexistente (Peca_de_Componente_inexistente_da_NaoEncontrado).
    catalogo.Componentes.Add(ComponenteComSolido(5));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 5, Quantidade: 3m, RequerRelatorioDimensional: false), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Empty(resultado.Valor!.Filhos);
    Assert.Single(estruturas.Itens);
  }

  // ---- Regra 18, segunda metade (Task 5 da Fase 2B): Peca de Componente sem solido e recusada ----

  [Fact]
  public async Task Peca_de_Componente_sem_solido_e_recusada_regra_18()
  {
    var (useCase, estruturas, _, catalogo) = Montar(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    // Sobrescreve o componente semeado por `Montar`: este NAO tem solido.
    catalogo.Componentes.Clear();
    catalogo.Componentes.Add(ComponenteSemSolido(1));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    // A mensagem diz O QUE FAZER, nao so que falhou: sem isso o usuario sabe que nao pode e nao
    // sabe por onde sair.
    Assert.Contains("solido", resultado.Erro, StringComparison.OrdinalIgnoreCase);
    Assert.Empty(estruturas.Itens);
  }

  [Fact]
  public async Task Item_ad_hoc_continua_podendo_nascer_sem_solido_a_regra_18_e_so_da_Peca()
  {
    // A regra 18 vale para PECA (no raiz). `AcrescentarFilho` com ComponenteId nulo continua
    // valido: e a constraint CK_EstruturaItem_PecaTemComponente que garante que a Peca tem
    // Componente, e so a Peca.
    var (useCase, _, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    var raiz = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);
    Assert.True(raiz.Sucesso);

    var resultado = await useCase.AcrescentarFilho(
        raiz.Valor!.Id, new NovoFilhoDto(ComponenteId: null, Descricao: "Filho ad-hoc", Quantidade: 1m),
        CancellationToken.None);

    Assert.True(resultado.Sucesso);
  }

  [Fact]
  public async Task Peca_de_Componente_inexistente_da_NaoEncontrado()
  {
    // Lacuna PRE-EXISTENTE que esta guarda fecha de graca: antes dela, a Application aceitava um
    // ComponenteId inexistente — `Peca_de_Componente_sem_receita_grava_um_no_so` usava justamente
    // um Id fora do catalogo do fake e afirmava SUCESSO. O que acontecia depois, no banco, NAO foi
    // medido: nao afirme 500 sem medir.
    var (useCase, estruturas, _, catalogo) = Montar(
        new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    catalogo.Componentes.Clear();

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 999, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Empty(estruturas.Itens);
  }

  [Fact]
  public async Task A_guarda_de_solido_roda_DEPOIS_da_de_agrupamento()
  {
    // Agrupamento inexistente + componente sem solido responde NaoEncontrado do AGRUPAMENTO. A
    // ordem e convencao (o recurso da rota antes do que o corpo referencia), e este teste a prende
    // para ela nao mudar por acidente. Nao ha sigilo em jogo: a existencia de um Agrupamento ja e
    // legivel por qualquer autenticado via `GET agrupamentos/{id}`.
    var (useCase, _, _, catalogo) = Montar();   // nenhum Agrupamento
    catalogo.Componentes.Clear();
    catalogo.Componentes.Add(ComponenteSemSolido(1));

    var resultado = await useCase.CriarPeca(
        99, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Contains("Agrupamento", resultado.Erro);
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
    // Minor 7 da review da Task 3: a versao ANTERIOR deste mesmo teste (nao um teste vizinho)
    // montava um Pedido solto, nunca ligado a nada, e afirmava contra o proprio literal que acabara
    // de escrever — passava mesmo que o codigo nunca olhasse Status. Hoje o Pedido esta de fato
    // alcancavel pelo caso de uso (via
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
    // sinal) mas e menor que o piso da coluna DECIMAL(18,4) — gravado, viraria 0,0000 em silencio:
    // uma Peca de quantidade ZERO, valor que ninguem pediu, persistido sem erro nenhum.
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

  [Fact]
  public async Task Quantidade_acumulada_no_produto_da_receita_afunda_abaixo_do_piso_da_coluna_e_e_recusada()
  {
    // I2 da review de branch da Fase 2, simetrico de
    // `Quantidade_acumulada_no_produto_da_receita_ultrapassa_o_teto_da_coluna_e_e_recusada`: o piso
    // vivia so em `CriarPeca`/`AcrescentarFilho`/`EditarNo`, sempre sobre a quantidade DIGITADA, e
    // nenhuma checagem de piso corria durante a descida. Todas as entradas aqui sao LEGAIS: raiz 1
    // passa o piso, e `ReceitaPadraoUseCase` aceita fator `> 0` com ate 4 casas, entao 0,0001 e o
    // menor fator cadastravel. Filho = 0,0001 (exatamente o piso, aceito); neto = 0,00000001, que a
    // coluna DECIMAL(18,4) grava como 0,0000 — uma Peca de quantidade ZERO, valor que ninguem
    // pediu, persistido sem erro nenhum. Medido antes do conserto: a
    // suite inteira ficava verde e `plano.Erro` vinha NULO.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaFilhos.Add((1, 2, 0.0001m));
    estruturas.ReceitaFilhos.Add((2, 3, 0.0001m));

    var resultado = await useCase.CriarPeca(
        1, new NovaPecaDto(ComponenteId: 1, Quantidade: 1m, RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Quantidade_de_material_acima_do_teto_da_coluna_e_recusada()
  {
    // I3 da review de branch da Fase 2. `EstruturaMaterial.Quantidade` e o MESMO `DECIMAL(18,4)` de
    // `EstruturaItem.Quantidade`, e o produto `quantidade x QuantidadePadrao` nao passava por guarda
    // nenhuma — nem teto nem piso. A raiz aqui esta EXATAMENTE em `QuantidadeMaximaDaColuna`, valor
    // que a guarda de no ACEITA por construcao (`>`, nao `>=`), entao quem tem de recusar e a guarda
    // do material: 1000 vezes o teto. Sem ela isto chegava ao `INSERT` como `DbUpdateException` nao
    // tratada -> 500, que e literalmente o desfecho que o Important 1 da review da Task 3 fechou
    // para o NO e deixou aberto para o MATERIAL.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaMateriais.Add((1, 90, 1000m));

    var resultado = await useCase.CriarPeca(
        1,
        new NovaPecaDto(
            ComponenteId: 1,
            Quantidade: PlanejadorDeCopia.QuantidadeMaximaDaColuna,
            RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Quantidade_de_material_abaixo_do_piso_da_coluna_e_recusada()
  {
    // I3 da review de branch, a outra direcao. Tudo legal: raiz 0,0001 e exatamente o piso (a guarda
    // de entrada usa `<`, entao passa) e 0,0001 e o menor fator que `ReceitaPadraoUseCase` aceita
    // cadastrar. O produto, 0,00000001, a coluna grava como 0,0000 — material de quantidade zero
    // separado para a Peca, sem erro nenhum.
    var (useCase, estruturas, _, _) = Montar(new Agrupamento { Id = 1, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" });
    estruturas.ReceitaMateriais.Add((1, 90, 0.0001m));

    var resultado = await useCase.CriarPeca(
        1,
        new NovaPecaDto(
            ComponenteId: 1,
            Quantidade: PlanejadorDeCopia.QuantidadeMinimaDaColuna,
            RequerRelatorioDimensional: false),
        CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, estruturas.GravacoesDeArvore);
  }
}
