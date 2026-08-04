using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
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
