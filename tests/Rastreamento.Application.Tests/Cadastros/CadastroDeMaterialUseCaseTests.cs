using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeMaterialUseCaseTests
{
  private static NovoMaterialDto Chapa(string codigo = "CH-001") =>
      new(codigo, "Chapa de aco 3mm", "KG");

  [Fact]
  public async Task Cadastra_material_novo_ativo()
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Cadastrar(Chapa(), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("CH-001", resultado.Valor!.Codigo);
    Assert.Equal("Chapa de aco 3mm", resultado.Valor.Descricao);
    Assert.Equal("KG", resultado.Valor.UnidadeMedida);
    Assert.True(resultado.Valor.Ativo);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Codigo_duplicado_e_conflito_e_nao_escreve_nada()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 3,
      Codigo = "CH-001",
      Descricao = "Outra coisa",
      UnidadeMedida = "UN",
      Ativo = true,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Cadastrar(Chapa(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Descricao_duplicada_nao_e_conflito()
  {
    // UQ_Material_Codigo cobre so o Codigo: dois materiais com a mesma descricao sao validos.
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 3,
      Codigo = "CH-001",
      Descricao = "Chapa de aco 3mm",
      UnidadeMedida = "KG",
      Ativo = true,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Cadastrar(Chapa("CH-002"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
  }

  [Fact]
  public async Task Localiza_duplicado_inativo_apontando_o_campo_codigo()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 9,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = false,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var duplicado = await useCase.LocalizarDuplicado("CH-001", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("codigo", duplicado!.Campo);
    Assert.True(duplicado.ExisteInativo);
    Assert.Equal(9, duplicado.IdExistente);
  }

  [Fact]
  public async Task Localiza_duplicado_devolve_nulo_quando_codigo_e_livre()
  {
    var useCase = new CadastroDeMaterialUseCase(new FakeMaterialRepo(), new FakeSetorRepo());

    Assert.Null(await useCase.LocalizarDuplicado("CH-001", CancellationToken.None));
  }

  [Theory]
  [InlineData("", "Chapa", "KG")]
  [InlineData("CH-001", "  ", "KG")]
  [InlineData("CH-001", "Chapa", "")]
  public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(
      string codigo, string descricao, string unidade)
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Cadastrar(
        new NovoMaterialDto(codigo, descricao, unidade), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_material_inexistente_e_nao_encontrado()
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Editar(99, Chapa(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_mantendo_o_proprio_codigo_nao_e_conflito_e_persiste()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = true,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Editar(
        1, new NovoMaterialDto("CH-001", "Chapa de aco 3mm", "KG"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Chapa de aco 3mm", resultado.Valor!.Descricao);
    // Unico teste que prova a escrita do Editar: sem isto, um Editar que muta a entidade e
    // esquece o SalvarAlteracoesAsync passa em todos os outros.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_codigo_de_outro_material_e_conflito()
  {
    var repo = new FakeMaterialRepo(
        new Material { Id = 1, Codigo = "CH-001", Descricao = "A", UnidadeMedida = "KG", Ativo = true },
        new Material { Id = 2, Codigo = "CH-002", Descricao = "B", UnidadeMedida = "KG", Ativo = true });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.Editar(2, Chapa(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Definir_ativo_false_inativa_e_persiste()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = true,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    Assert.Empty(await useCase.Listar(incluirInativos: false, CancellationToken.None));
    Assert.Single(await useCase.Listar(incluirInativos: true, CancellationToken.None));
  }

  [Fact]
  public async Task Definir_ativo_true_reativa_e_persiste()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = false,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.DefinirAtivo(1, true, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    Assert.Single(await useCase.Listar(incluirInativos: false, CancellationToken.None));
  }

  [Fact]
  public async Task Definir_ativo_em_material_inexistente_e_nao_encontrado()
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo());

    var resultado = await useCase.DefinirAtivo(99, true, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Localiza_duplicado_com_codigo_nulo_nao_lanca()
  {
    // O `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON entrega null
    // mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina de codigo:
    // trocar `Normalizar(codigo)` por `codigo.Trim()` pelado nao quebraria nada (adendo B9).
    var useCase = new CadastroDeMaterialUseCase(new FakeMaterialRepo(), new FakeSetorRepo());

    var duplicado = await useCase.LocalizarDuplicado(null!, CancellationToken.None);

    Assert.Null(duplicado);
  }

  private static Setor Corte(bool ativo = true) =>
      new() { Id = 4, Nome = "Corte", Ativo = ativo };

  private static NovoMaterialDto ChapaQueFicaEm(int? setorId) =>
      new("CH-001", "Chapa de aco 3mm", "KG", setorId);

  [Fact]
  public async Task Cadastrar_com_setor_ativo_grava_e_devolve_FicaEm()
  {
    var repo = new FakeMaterialRepo();
    var setor = Corte();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(setor));

    var resultado = await useCase.Cadastrar(ChapaQueFicaEm(setor.Id), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(new SetorDeEstoqueDto(setor.Id, "Corte"), resultado.Valor!.FicaEm);
    var guardado = Assert.Single(await repo.ListarAsync(true, CancellationToken.None));
    Assert.Equal(setor.Id, guardado.SetorDeEstoqueId);
  }

  [Fact]
  public async Task Cadastrar_sem_setor_fica_no_almoxarifado()
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(Corte()));

    var resultado = await useCase.Cadastrar(ChapaQueFicaEm(null), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Null(resultado.Valor!.FicaEm);
  }

  [Fact]
  public async Task Cadastrar_com_setor_inexistente_e_recusado()
  {
    var repo = new FakeMaterialRepo();
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(Corte()));

    var resultado = await useCase.Cadastrar(ChapaQueFicaEm(999), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal("SetorDeEstoqueInvalido", resultado.Erro);
    Assert.Equal(0, repo.Saves);
    Assert.Empty(await repo.ListarAsync(true, CancellationToken.None));
  }

  [Fact]
  public async Task Cadastrar_com_setor_inativo_e_recusado()
  {
    var repo = new FakeMaterialRepo();
    var setor = Corte(ativo: false);
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(setor));

    var resultado = await useCase.Cadastrar(ChapaQueFicaEm(setor.Id), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal("SetorDeEstoqueInvalido", resultado.Erro);
    Assert.Equal(0, repo.Saves);
    Assert.Empty(await repo.ListarAsync(true, CancellationToken.None));
  }

  [Fact]
  public async Task Editar_troca_o_setor_e_volta_ao_almoxarifado_com_null()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa de aco 3mm",
      UnidadeMedida = "KG",
      Ativo = true,
    });
    var corte = Corte();
    var solda = new Setor { Id = 5, Nome = "Solda", Ativo = true };
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(corte, solda));

    var noCorte = await useCase.Editar(1, ChapaQueFicaEm(corte.Id), CancellationToken.None);
    var naSolda = await useCase.Editar(1, ChapaQueFicaEm(solda.Id), CancellationToken.None);
    var deVolta = await useCase.Editar(1, ChapaQueFicaEm(null), CancellationToken.None);

    Assert.Equal(new SetorDeEstoqueDto(corte.Id, "Corte"), noCorte.Valor!.FicaEm);
    Assert.Equal(new SetorDeEstoqueDto(solda.Id, "Solda"), naSolda.Valor!.FicaEm);
    Assert.Null(deVolta.Valor!.FicaEm);
    Assert.Null((await repo.ObterPorIdAsync(1, CancellationToken.None))!.SetorDeEstoqueId);
  }

  [Fact]
  public async Task Editar_com_setor_inativo_e_recusado_e_nao_escreve()
  {
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa de aco 3mm",
      UnidadeMedida = "KG",
      Ativo = true,
    });
    var setor = Corte(ativo: false);
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(setor));

    var resultado = await useCase.Editar(1, ChapaQueFicaEm(setor.Id), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal("SetorDeEstoqueInvalido", resultado.Erro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Listar_traz_o_nome_do_setor_mesmo_inativo()
  {
    // O setor foi apontado quando estava ativo e inativado depois: o Material continua
    // dizendo onde fica, e a lista nao pode perder o nome.
    var setor = Corte(ativo: false);
    var repo = new FakeMaterialRepo(
        new Material
        {
          Id = 1,
          Codigo = "CH-001",
          Descricao = "Chapa",
          UnidadeMedida = "KG",
          Ativo = true,
          SetorDeEstoqueId = setor.Id,
        },
        new Material { Id = 2, Codigo = "CH-002", Descricao = "Outra", UnidadeMedida = "KG", Ativo = true });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(setor));

    var lista = await useCase.Listar(incluirInativos: false, CancellationToken.None);

    Assert.Equal(new SetorDeEstoqueDto(setor.Id, "Corte"), lista[0].FicaEm);
    Assert.Null(lista[1].FicaEm);
  }

  [Fact]
  public async Task Editar_mantendo_o_setor_ja_inativado_e_aceito()
  {
    // O que ja esta gravado fica: so a descricao muda, o Setor apontado foi inativado depois.
    var setor = Corte(ativo: false);
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = true,
      SetorDeEstoqueId = setor.Id,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(setor));

    var resultado = await useCase.Editar(1, ChapaQueFicaEm(setor.Id), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(new SetorDeEstoqueDto(setor.Id, "Corte"), resultado.Valor!.FicaEm);
    Assert.Equal("Chapa de aco 3mm", resultado.Valor.Descricao);
    Assert.Equal(1, repo.Saves);
    Assert.Equal(setor.Id, (await repo.ObterPorIdAsync(1, CancellationToken.None))!.SetorDeEstoqueId);
  }

  [Fact]
  public async Task Editar_para_outro_setor_inativo_continua_recusado()
  {
    var atual = Corte(ativo: false);
    var outro = new Setor { Id = 5, Nome = "Solda", Ativo = false };
    var repo = new FakeMaterialRepo(new Material
    {
      Id = 1,
      Codigo = "CH-001",
      Descricao = "Chapa",
      UnidadeMedida = "KG",
      Ativo = true,
      SetorDeEstoqueId = atual.Id,
    });
    var useCase = new CadastroDeMaterialUseCase(repo, new FakeSetorRepo(atual, outro));

    var resultado = await useCase.Editar(1, ChapaQueFicaEm(outro.Id), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal("SetorDeEstoqueInvalido", resultado.Erro);
    Assert.Equal(0, repo.Saves);
    Assert.Equal(atual.Id, (await repo.ObterPorIdAsync(1, CancellationToken.None))!.SetorDeEstoqueId);
  }
}
