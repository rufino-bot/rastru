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
        var useCase = new CadastroDeMaterialUseCase(repo);

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
            Id = 3, Codigo = "CH-001", Descricao = "Outra coisa", UnidadeMedida = "UN", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

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
            Id = 3, Codigo = "CH-001", Descricao = "Chapa de aco 3mm", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.Cadastrar(Chapa("CH-002"), CancellationToken.None);

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public async Task Localiza_duplicado_inativo_apontando_o_campo_codigo()
    {
        var repo = new FakeMaterialRepo(new Material
        {
            Id = 9, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = false,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var duplicado = await useCase.LocalizarDuplicado("CH-001", CancellationToken.None);

        Assert.NotNull(duplicado);
        Assert.Equal("codigo", duplicado!.Campo);
        Assert.True(duplicado.ExisteInativo);
        Assert.Equal(9, duplicado.IdExistente);
    }

    [Fact]
    public async Task Localiza_duplicado_devolve_nulo_quando_codigo_e_livre()
    {
        var useCase = new CadastroDeMaterialUseCase(new FakeMaterialRepo());

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
        var useCase = new CadastroDeMaterialUseCase(repo);

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
        var useCase = new CadastroDeMaterialUseCase(repo);

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
            Id = 1, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

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
        var useCase = new CadastroDeMaterialUseCase(repo);

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
            Id = 1, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = true,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

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
            Id = 1, Codigo = "CH-001", Descricao = "Chapa", UnidadeMedida = "KG", Ativo = false,
        });
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.DefinirAtivo(1, true, CancellationToken.None);

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, repo.Saves);
        Assert.Single(await useCase.Listar(incluirInativos: false, CancellationToken.None));
    }

    [Fact]
    public async Task Definir_ativo_em_material_inexistente_e_nao_encontrado()
    {
        var repo = new FakeMaterialRepo();
        var useCase = new CadastroDeMaterialUseCase(repo);

        var resultado = await useCase.DefinirAtivo(99, true, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
        Assert.Equal(0, repo.Saves);
    }
}
