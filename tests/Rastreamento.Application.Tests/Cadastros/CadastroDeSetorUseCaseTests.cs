using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeSetorUseCaseTests
{
  [Fact]
  public async Task Cadastra_setor_novo_ativo()
  {
    var repo = new FakeSetorRepo();
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Cadastrar(new NovoSetorDto("Solda"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Solda", resultado.Valor!.Nome);
    Assert.True(resultado.Valor.Ativo);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Nome_duplicado_e_conflito_e_nao_escreve_nada()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 7, Nome = "Solda", Ativo = true });
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Cadastrar(new NovoSetorDto("Solda"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Localiza_duplicado_inativo_para_a_tela_oferecer_reativacao()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 7, Nome = "Solda", Ativo = false });
    var useCase = new CadastroDeSetorUseCase(repo);

    var duplicado = await useCase.LocalizarDuplicado("Solda", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("nome", duplicado!.Campo);
    Assert.True(duplicado.ExisteInativo);
    Assert.Equal(7, duplicado.IdExistente);
  }

  [Fact]
  public async Task Localiza_duplicado_devolve_nulo_quando_nome_e_livre()
  {
    var useCase = new CadastroDeSetorUseCase(new FakeSetorRepo());

    Assert.Null(await useCase.LocalizarDuplicado("Solda", CancellationToken.None));
  }

  [Fact]
  public async Task Editar_setor_inexistente_e_nao_encontrado()
  {
    var repo = new FakeSetorRepo();
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Editar(99, new NovoSetorDto("Solda"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_nome_de_outro_setor_e_conflito()
  {
    var repo = new FakeSetorRepo(
        new Setor { Id = 1, Nome = "Solda", Ativo = true },
        new Setor { Id = 2, Nome = "Pintura", Ativo = true });
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Editar(2, new NovoSetorDto("Solda"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_mantendo_o_proprio_nome_nao_e_conflito()
  {
    // Renomear "Solda" para "Solda" acha a si mesmo no ObterPorNome: so e conflito se for outro Id.
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true });
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Editar(1, new NovoSetorDto("Solda"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    // Unico teste que prova a escrita do Editar: sem isto, um Editar que muta a entidade e
    // esquece o SalvarAlteracoesAsync passa nos nove.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Definir_ativo_false_inativa_e_persiste()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true });
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    Assert.Empty(await useCase.Listar(incluirInativos: false, CancellationToken.None));
    Assert.Single(await useCase.Listar(incluirInativos: true, CancellationToken.None));
  }

  [Fact]
  public async Task Definir_ativo_true_reativa_e_persiste()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = false });
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, true, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    Assert.Single(await useCase.Listar(incluirInativos: false, CancellationToken.None));
  }

  [Fact]
  public async Task Definir_ativo_em_setor_inexistente_e_nao_encontrado()
  {
    var repo = new FakeSetorRepo();
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.DefinirAtivo(99, true, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Nome_em_branco_e_erro_de_validacao()
  {
    var repo = new FakeSetorRepo();
    var useCase = new CadastroDeSetorUseCase(repo);

    var resultado = await useCase.Cadastrar(new NovoSetorDto("   "), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Localiza_duplicado_com_nome_nulo_nao_lanca()
  {
    // O `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON entrega null
    // mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina de codigo:
    // trocar `Normalizar(nome)` por `nome.Trim()` pelado nao quebraria nada (adendo B9).
    var useCase = new CadastroDeSetorUseCase(new FakeSetorRepo());

    var duplicado = await useCase.LocalizarDuplicado(null!, CancellationToken.None);

    Assert.Null(duplicado);
  }

  [Fact]
  public async Task Cadastrar_grava_a_atividade_aparada()
  {
    var repo = new FakeSetorRepo();
    var r = await new CadastroDeSetorUseCase(repo).Cadastrar(new NovoSetorDto("Solda", "  solda "), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal("solda", r.Valor!.Atividade);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task Atividade_ausente_ou_em_branco_grava_nula(string? atividade)
  {
    var repo = new FakeSetorRepo();
    var r = await new CadastroDeSetorUseCase(repo).Cadastrar(new NovoSetorDto("Solda", atividade), CancellationToken.None);

    Assert.Null(r.Valor!.Atividade);
  }

  [Fact]
  public async Task Editar_troca_e_limpa_a_atividade()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true, Atividade = "solda" });
    var uc = new CadastroDeSetorUseCase(repo);

    Assert.Equal("montagem", (await uc.Editar(1, new NovoSetorDto("Solda", "montagem"), CancellationToken.None)).Valor!.Atividade);
    Assert.Null((await uc.Editar(1, new NovoSetorDto("Solda", null), CancellationToken.None)).Valor!.Atividade);
  }

  [Fact]
  public async Task Listar_traz_a_atividade()
  {
    var repo = new FakeSetorRepo(new Setor { Id = 1, Nome = "Solda", Ativo = true, Atividade = "solda" });

    Assert.Equal("solda", Assert.Single(await new CadastroDeSetorUseCase(repo).Listar(false, CancellationToken.None)).Atividade);
  }
}
