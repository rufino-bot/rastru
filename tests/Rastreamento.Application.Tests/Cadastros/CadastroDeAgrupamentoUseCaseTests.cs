using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeAgrupamentoUseCaseTests
{
  private const int UsuarioDaSessao = 42;

  private static Pedido PedidoAberto(int id = 1) =>
      new() { Id = id, Numero = $"PED-{id:000}", Cliente = "X", Tipo = "Fabricacao", Status = "Aberto" };

  private static NovoAgrupamentoDto Kit(string codigo = "AG-01") => new(codigo, "Kit");

  [Fact]
  public async Task Cadastra_agrupamento_no_pedido_com_autoria_e_timestamp()
  {
    var antes = DateTime.UtcNow.AddSeconds(-1);
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, resultado.Valor!.PedidoId);
    Assert.Equal(UsuarioDaSessao, resultado.Valor.CriadoPorUsuarioId);
    Assert.InRange(resultado.Valor.CriadoEm, antes, DateTime.UtcNow.AddSeconds(1));
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Cadastrar_em_pedido_inexistente_e_nao_encontrado()
  {
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo());

    var resultado = await useCase.Cadastrar(99, Kit(), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Codigo_repetido_no_MESMO_pedido_e_conflito()
  {
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Codigo_repetido_em_OUTRO_pedido_nao_e_conflito()
  {
    // UQ_Agrupamento_PedidoCodigo e composta: "AG-01" pode existir uma vez por Pedido.
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 2,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    var useCase = new CadastroDeAgrupamentoUseCase(
        repo, new FakePedidoRepo(PedidoAberto(), PedidoAberto(2)));

    var resultado = await useCase.Cadastrar(1, Kit(), UsuarioDaSessao, CancellationToken.None);

    Assert.True(resultado.Sucesso);
  }

  [Theory]
  [InlineData("", "Kit")]
  [InlineData("AG-01", "Conjunto")]
  public async Task Entrada_invalida_e_erro_de_validacao(string codigo, string tipo)
  {
    // Tipo fora de Kit|Avulso e barrado aqui, e nao pelo CK_Agrupamento_Tipo: excecao de CHECK
    // subiria como 500 em vez de 400 (specs/03-arquitetura-tecnica.md:25-27).
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Cadastrar(
        1, new NovoAgrupamentoDto(codigo, tipo), UsuarioDaSessao, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Excluir_agrupamento_vazio_de_pedido_aberto_apaga_de_verdade()
  {
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Excluir(5, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    Assert.Empty(await useCase.ListarPorPedido(1, CancellationToken.None));
  }

  [Fact]
  public async Task Excluir_agrupamento_com_estrutura_e_bloqueado()
  {
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    repo.ComEstrutura.Add(5);
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Excluir(5, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal("AgrupamentoNaoVazio", resultado.Erro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Excluir_agrupamento_de_pedido_nao_aberto_e_bloqueado()
  {
    var pedido = PedidoAberto();
    pedido.Status = "EmProducao";
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(pedido));

    var resultado = await useCase.Excluir(5, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal("PedidoNaoAberto", resultado.Erro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Excluir_agrupamento_inexistente_e_nao_encontrado()
  {
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo());

    var resultado = await useCase.Excluir(99, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_nao_troca_o_pedido_nem_o_autor()
  {
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
      CriadoPorUsuarioId = 7,
      CriadoEm = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Editar(
        5, new NovoAgrupamentoDto("AG-01", "Avulso"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Avulso", resultado.Valor!.Tipo);
    Assert.Equal(1, resultado.Valor.PedidoId);
    Assert.Equal(7, resultado.Valor.CriadoPorUsuarioId);
    Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), resultado.Valor.CriadoEm);
    // Sem esta linha o teste afirmaria os valores projetados em memoria e passaria mesmo com o
    // `SalvarAlteracoesAsync` removido do `Editar` — a alteracao nunca chegaria ao banco.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_agrupamento_inexistente_e_nao_encontrado()
  {
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Editar(
        99, new NovoAgrupamentoDto("AG-99", "Kit"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_codigo_de_OUTRO_agrupamento_do_mesmo_pedido_e_conflito()
  {
    var repo = new FakeAgrupamentoRepo(
        new Agrupamento { Id = 5, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" },
        new Agrupamento { Id = 6, PedidoId = 1, Codigo = "AG-02", Tipo = "Kit" });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var resultado = await useCase.Editar(
        6, new NovoAgrupamentoDto("AG-01", "Kit"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Localiza_duplicado_nunca_e_reativavel()
  {
    var repo = new FakeAgrupamentoRepo(new Agrupamento
    {
      Id = 5,
      PedidoId = 1,
      Codigo = "AG-01",
      Tipo = "Kit",
    });
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var duplicado = await useCase.LocalizarDuplicado(1, "AG-01", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("codigo", duplicado!.Campo);
    Assert.False(duplicado.ExisteInativo);
    Assert.Equal(5, duplicado.IdExistente);
  }

  [Fact]
  public async Task Localiza_duplicado_com_codigo_nulo_nao_lanca()
  {
    // O `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON entrega null
    // mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina de codigo:
    // trocar `Normalizar(codigo)` por `codigo.Trim()` pelado nao quebraria nada (adendo B9).
    var repo = new FakeAgrupamentoRepo();
    var useCase = new CadastroDeAgrupamentoUseCase(repo, new FakePedidoRepo(PedidoAberto()));

    var duplicado = await useCase.LocalizarDuplicado(1, null!, CancellationToken.None);

    Assert.Null(duplicado);
  }
}
