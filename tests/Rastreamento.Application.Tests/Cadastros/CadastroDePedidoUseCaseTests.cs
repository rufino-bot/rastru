using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDePedidoUseCaseTests
{
  private const int UsuarioDaSessao = 42;

  [Fact]
  public async Task Cadastra_pedido_aberto_de_fabricacao()
  {
    var repo = new FakePedidoRepo();
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("PED-001", resultado.Valor!.Numero);
    Assert.Equal("Fabricacao", resultado.Valor.Tipo);
    Assert.Equal("Aberto", resultado.Valor.Status);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Cadastra_gravando_o_autor_recebido_por_parametro()
  {
    // A autoria vem de FORA do use case: quem le a claim `sub` e o controller.
    var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

    var resultado = await useCase.Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

    Assert.Equal(UsuarioDaSessao, resultado.Valor!.CriadoPorUsuarioId);
  }

  [Fact]
  public async Task Data_de_abertura_nasce_em_utc()
  {
    var antes = DateTime.UtcNow.AddSeconds(-1);
    var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

    var resultado = await useCase.Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

    Assert.InRange(resultado.Valor!.DataAbertura, antes, DateTime.UtcNow.AddSeconds(1));
  }

  [Fact]
  public async Task Numero_duplicado_e_conflito_e_nao_escreve_nada()
  {
    var repo = new FakePedidoRepo(new Pedido { Id = 1, Numero = "PED-001", Cliente = "Y" });
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Cadastrar(
        new NovoPedidoDto("PED-001", "Cliente X"), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Duplicado_de_pedido_nunca_e_reativavel()
  {
    // Pedido nao tem coluna Ativo: `existeInativo` e sempre false, e a tela nao oferece
    // "reativar o existente" — o caminho de correcao e editar o Pedido que ja existe.
    var repo = new FakePedidoRepo(new Pedido { Id = 8, Numero = "PED-001", Cliente = "Y" });
    var useCase = new CadastroDePedidoUseCase(repo);

    var duplicado = await useCase.LocalizarDuplicado("PED-001", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("numero", duplicado!.Campo);
    Assert.False(duplicado.ExisteInativo);
    Assert.Equal(8, duplicado.IdExistente);
  }

  [Theory]
  [InlineData("", "Cliente X")]
  [InlineData("PED-001", "   ")]
  public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(string numero, string cliente)
  {
    var repo = new FakePedidoRepo();
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Cadastrar(
        new NovoPedidoDto(numero, cliente), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_pedido_inexistente_e_nao_encontrado()
  {
    var repo = new FakePedidoRepo();
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Editar(
        99, new NovoPedidoDto("PED-001", "Cliente X"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_nao_troca_o_autor()
  {
    // Autoria e do momento da criacao: editar nao reescreve quem abriu o Pedido. Tambem prova
    // que Editar PERSISTE (Saves == 1) — sem esta asserção um Editar que esquecesse o
    // SalvarAlteracoesAsync passaria em silencio.
    var repo = new FakePedidoRepo(new Pedido
    {
      Id = 1,
      Numero = "PED-001",
      Cliente = "Y",
      CriadoPorUsuarioId = 7,
      Tipo = "Fabricacao",
      Status = "Aberto",
    });
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Editar(
        1, new NovoPedidoDto("PED-001", "Cliente Z"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Cliente Z", resultado.Valor!.Cliente);
    Assert.Equal(7, resultado.Valor.CriadoPorUsuarioId);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_numero_de_outro_pedido_e_conflito()
  {
    var repo = new FakePedidoRepo(
        new Pedido { Id = 1, Numero = "PED-001", Cliente = "A" },
        new Pedido { Id = 2, Numero = "PED-002", Cliente = "B" });
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Editar(
        2, new NovoPedidoDto("PED-001", "B"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Listar_devolve_a_pagina_com_o_total_do_filtro()
  {
    var repo = new FakePedidoRepo(
        new Pedido { Id = 1, Numero = "PED-001", Cliente = "A" },
        new Pedido { Id = 2, Numero = "PED-002", Cliente = "B" },
        new Pedido { Id = 3, Numero = "PED-003", Cliente = "C" });

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, null, 1, 2, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(2, resultado.Valor!.Itens.Count);
    Assert.Equal(3, resultado.Valor.Total);
    Assert.Equal(1, resultado.Valor.Pagina);
    Assert.Equal(2, resultado.Valor.Tamanho);
  }

  [Theory]
  [InlineData(0, 20)]
  [InlineData(1, 0)]
  [InlineData(1, 101)]
  public async Task Listar_recusa_faixa_invalida(int pagina, int tamanho)
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, null, pagina, tamanho, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Fact]
  public async Task Listar_recusa_status_desconhecido_nomeando_o_valor()
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, "Aberto,Qualquer", null, null, 1, 20, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Contains("Qualquer", resultado.Erro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Theory]
  [InlineData("abc")]
  [InlineData("0")]
  [InlineData("-3")]
  [InlineData("1.5")]
  public async Task Listar_recusa_material_que_nao_e_inteiro_positivo(string material)
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, material, null, 1, 20, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Fact]
  public async Task Listar_ignora_pedaco_vazio_e_colapsa_repetido()
  {
    var repo = new FakePedidoRepo();
    var useCase = new CadastroDePedidoUseCase(repo);

    await useCase.Listar("  CH  ", " Aberto,,Aberto ,EmProducao", "5,,5,3", null, 1, 20, CancellationToken.None);

    Assert.Equal(["Aberto", "EmProducao"], repo.UltimoFiltro!.Status);
    Assert.Equal([5, 3], repo.UltimoFiltro.Materiais);
    Assert.Equal("CH", repo.UltimoFiltro.Busca);

    await useCase.Listar("   ", null, null, null, 1, 20, CancellationToken.None);

    Assert.Null(repo.UltimoFiltro.Busca);
    Assert.Empty(repo.UltimoFiltro.Status);
    Assert.Empty(repo.UltimoFiltro.Materiais);
  }

  [Fact]
  public async Task Listar_aceita_espaco_nas_pontas_de_cada_id_de_material()
  {
    var repo = new FakePedidoRepo();
    var useCase = new CadastroDePedidoUseCase(repo);

    var resultado = await useCase.Listar(null, null, " 5 , 3", null, 1, 20, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal([5, 3], repo.UltimoFiltro!.Materiais);
  }

  [Fact]
  public async Task Listar_traz_a_pausa_aberta_do_Pedido_pausado_e_nulo_nos_demais()
  {
    var repo = new FakePedidoRepo(
        new Pedido { Id = 1, Numero = "PED-001", Cliente = "A" },
        new Pedido { Id = 2, Numero = "PED-002", Cliente = "B" });
    var desde = new DateTime(2026, 9, 28, 13, 14, 0, DateTimeKind.Utc);
    repo.PausasAbertas[2] = new PausaAberta(2, desde, 7, "PCP", "PED-9 urgente");

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, null, 1, 20, CancellationToken.None);

    var pedidos = resultado.Valor!.Itens;
    Assert.Null(pedidos.Single(p => p.Id == 1).Pausa);
    Assert.Equal(new PausaResumoDto(desde, "PCP", "PED-9 urgente"), pedidos.Single(p => p.Id == 2).Pausa);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("  ")]
  public async Task Listar_sem_ordem_pede_Recentes(string? ordem)
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, ordem, 1, 20, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(OrdemDePedidos.Recentes, repo.UltimoFiltro!.Ordem);
  }

  [Theory]
  [InlineData("recentes", OrdemDePedidos.Recentes)]
  [InlineData("numero", OrdemDePedidos.Numero)]
  [InlineData("cliente", OrdemDePedidos.Cliente)]
  public async Task Listar_traduz_cada_ordem(string ordem, OrdemDePedidos esperada)
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, ordem, 1, 20, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(esperada, repo.UltimoFiltro!.Ordem);
  }

  [Theory]
  [InlineData("Numero")]
  [InlineData("nome")]
  [InlineData("recente")]
  public async Task Listar_com_ordem_desconhecida_e_Validacao_e_nao_consulta(string ordem)
  {
    // "Numero" entra de proposito: a comparacao e ordinal, entao a caixa errada tambem e desconhecida.
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, ordem, 1, 20, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal($"Ordem '{ordem}' desconhecida. Aceitas: recentes, numero, cliente.", resultado.Erro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Fact]
  public async Task Faixa_invalida_ganha_da_ordem_invalida()
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, null, null, "x", 0, 20, CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.DoesNotContain("Ordem", resultado.Erro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Fact]
  public async Task Status_invalido_ganha_da_ordem_invalida()
  {
    var repo = new FakePedidoRepo();

    var resultado = await new CadastroDePedidoUseCase(repo)
        .Listar(null, "Qualquer", null, "x", 1, 20, CancellationToken.None);

    Assert.Contains("Status 'Qualquer'", resultado.Erro);
    Assert.Null(repo.UltimoFiltro);
  }

  [Fact]
  public async Task Resumo_traz_os_cinco_status_na_ordem_do_DDL_com_zero_no_que_falta()
  {
    var repo = new FakePedidoRepo();
    repo.ContagemPorStatus["Aberto"] = 2;
    repo.ContagemPorStatus["Concluido"] = 1;

    var resumo = await new CadastroDePedidoUseCase(repo).Resumo(CancellationToken.None);

    Assert.Equal(
        [
          new ContagemDeStatusDto("Aberto", 2),
          new ContagemDeStatusDto("EmProducao", 0),
          new ContagemDeStatusDto("AguardandoExpedicao", 0),
          new ContagemDeStatusDto("Concluido", 1),
          new ContagemDeStatusDto("Cancelado", 0),
        ],
        resumo.PorStatus);
  }

  [Fact]
  public async Task Resumo_pede_os_mais_antigos_fora_dos_encerrados_e_no_maximo_cinco()
  {
    var repo = new FakePedidoRepo();
    var desde = new DateTime(2026, 9, 28, 13, 14, 0, DateTimeKind.Utc);
    repo.MaisAntigos.Add(new Pedido { Id = 1, Numero = "PED-001", Cliente = "A", Status = "Aberto" });
    repo.MaisAntigos.Add(new Pedido { Id = 2, Numero = "PED-002", Cliente = "B", Status = "EmProducao" });
    repo.PausasAbertas[2] = new PausaAberta(2, desde, 7, "PCP", "parado");

    var resumo = await new CadastroDePedidoUseCase(repo).Resumo(CancellationToken.None);

    Assert.Equal(["Cancelado", "Concluido"], repo.MaisAntigosForaDosStatus!.Order());
    Assert.Equal(5, repo.MaisAntigosQuantos);
    Assert.Equal([1, 2], resumo.MaisAntigosAbertos.Select(p => p.Id));
    Assert.Null(resumo.MaisAntigosAbertos[0].Pausa);
    Assert.Equal(new PausaResumoDto(desde, "PCP", "parado"), resumo.MaisAntigosAbertos[1].Pausa);
  }

  [Fact]
  public async Task MateriaisEmUso_projeta_id_codigo_e_descricao()
  {
    var repo = new FakePedidoRepo();
    repo.MateriaisEmUso.Add(new Material { Id = 3, Codigo = "CH-300", Descricao = "Chapa 3 mm", UnidadeMedida = "UN", Ativo = false });

    var materiais = await new CadastroDePedidoUseCase(repo).MateriaisEmUso(CancellationToken.None);

    Assert.Equal([new MaterialResumoDto(3, "CH-300", "Chapa 3 mm")], materiais);
  }

  [Fact]
  public async Task Obter_traz_a_pausa_aberta_e_nulo_sem_ela()
  {
    var repo = new FakePedidoRepo(
        new Pedido { Id = 1, Numero = "PED-001", Cliente = "A" },
        new Pedido { Id = 2, Numero = "PED-002", Cliente = "B" });
    var desde = new DateTime(2026, 9, 28, 13, 14, 0, DateTimeKind.Utc);
    repo.PausasAbertas[2] = new PausaAberta(2, desde, 7, "PCP", null);
    var useCase = new CadastroDePedidoUseCase(repo);

    Assert.Null((await useCase.Obter(1, CancellationToken.None)).Valor!.Pausa);
    Assert.Equal(new PausaResumoDto(desde, "PCP", null), (await useCase.Obter(2, CancellationToken.None)).Valor!.Pausa);
  }

  [Fact]
  public async Task Obter_pedido_inexistente_e_nao_encontrado()
  {
    var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

    var resultado = await useCase.Obter(99, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Localiza_duplicado_com_numero_nulo_nao_lanca()
  {
    // O `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON entrega null
    // mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina de codigo:
    // trocar `Normalizar(numero)` por `numero.Trim()` pelado nao quebraria nada (adendo B9) —
    // foi exatamente a mutacao que o revisor da Task 8 fez sem matar nenhum teste.
    var useCase = new CadastroDePedidoUseCase(new FakePedidoRepo());

    var duplicado = await useCase.LocalizarDuplicado(null!, CancellationToken.None);

    Assert.Null(duplicado);
  }
}
