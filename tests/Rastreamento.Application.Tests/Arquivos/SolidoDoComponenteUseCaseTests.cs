using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Common;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Arquivos;

public class FakeArquivoDeComponenteRepo : IArquivoDeComponenteRepository
{
  /// <summary>Id alto e fora da faixa dos ids de catalogo usados nos testes (1, 2, 10, 11), para
  /// que uma projecao que devolva o campo errado nao acerte por coincidencia numerica.</summary>
  private int _proximoId = 700;

  public List<ArquivoDeComponente> Gravados { get; } = [];

  public Dictionary<int, ArquivoDeComponente> SolidoPorComponente { get; } = [];

  public List<int> VinculadosA { get; } = [];

  // Task<int?> e nao Task<int>: o null e "o componente nao existe", contrato que a Task 2 ganhou
  // no fix pass dela. Este fake NAO modela esse null -- ele grava para qualquer id que lhe pecam,
  // e quem barra componente inexistente e o caso de uso, antes de chegar aqui.
  public Task<int?> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct)
  {
    arquivo.Id = _proximoId++;
    Gravados.Add(arquivo);
    VinculadosA.Add(componenteId);
    SolidoPorComponente[componenteId] = arquivo;
    return Task.FromResult<int?>(arquivo.Id);
  }

  public Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(
      int componenteId, CancellationToken ct) =>
      Task.FromResult(SolidoPorComponente.GetValueOrDefault(componenteId));

  // Terceiro metodo da interface, nascido da emenda (B). A Task 3 NAO o consome -- quem consome e
  // a Task 4, no ComponenteDetalheDto -- mas o fake tem de implementa-lo para compilar.
  public Task<MetadadoDeSolido?> ObterMetadadoDoSolidoAsync(
      int componenteId, CancellationToken ct) =>
      Task.FromResult(SolidoPorComponente.TryGetValue(componenteId, out var a)
          ? new MetadadoDeSolido(a.NomeOriginal, a.Conteudo.Length)
          : null);
}

public class SolidoDoComponenteUseCaseTests
{
  private const int UsuarioId = 42;

  private static (SolidoDoComponenteUseCase UseCase, FakeComponenteRepo Componentes,
      FakeArquivoDeComponenteRepo Arquivos) Montar(params Componente[] componentes)
  {
    var componentesRepo = new FakeComponenteRepo(componentes);
    var arquivosRepo = new FakeArquivoDeComponenteRepo();
    var useCase = new SolidoDoComponenteUseCase(componentesRepo, arquivosRepo);
    return (useCase, componentesRepo, arquivosRepo);
  }

  private static Componente ComponenteDeCatalogo(int id) =>
      new() { Id = id, Codigo = $"C{id}", Descricao = $"Componente {id}", Tipo = "Fabricado", Ativo = true };

  [Fact]
  public async Task Enviar_grava_o_arquivo_e_vincula_ao_componente()
  {
    var (useCase, componentes, arquivos) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Enviar(
        10, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    var gravado = Assert.Single(arquivos.Gravados);
    Assert.Equal("cubo.stl", gravado.NomeOriginal);
    // NAO se afirma TamanhoEmBytes nem Sha256 aqui (emenda (A) de 2026-09-12): as duas sao
    // colunas calculadas PERSISTED, ninguem em C# as preenche, e no objeto em memoria elas valem
    // 0 e [] -- afirmar sobre elas aqui provaria o fake, nao o banco. Quem as prova e
    // ArquivoDeComponenteMapeamentoTests, contra o SQL Server real, na Task 2.
    Assert.Equal(UsuarioId, gravado.CriadoPorUsuarioId);
    // Vinculado ao componente PEDIDO, nao a qualquer um: com um componente so no fake, um literal
    // no lugar do parametro passaria (achado B11 da Fase 1A).
    Assert.Equal(10, Assert.Single(arquivos.VinculadosA));
    Assert.Equal(StlDeTeste.CuboBinario(), gravado.Conteudo);
    _ = componentes;
  }

  [Fact]
  public async Task Enviar_para_componente_inexistente_da_NaoEncontrado_e_nao_grava()
  {
    var (useCase, _, arquivos) = Montar();   // nenhum componente cadastrado

    var resultado = await useCase.Enviar(
        999, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Empty(arquivos.Gravados);
  }

  [Fact]
  public async Task Enviar_arquivo_invalido_da_Validacao_e_nao_grava()
  {
    var (useCase, _, arquivos) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Enviar(
        10, "cubo.stl", [1, 2, 3], UsuarioId, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Empty(arquivos.Gravados);
  }

  [Fact]
  public async Task Enviar_valida_ANTES_de_ler_o_componente()
  {
    // Arquivo invalido para componente inexistente responde VALIDACAO, nao NaoEncontrado: a
    // validacao, pura, roda antes da leitura do componente. O teste prende essa ordem para ela nao
    // mudar por acidente; ela nao protege sigilo — o catalogo e legivel por qualquer autenticado.
    var (useCase, _, _) = Montar();

    var resultado = await useCase.Enviar(
        999, "cubo.step", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Substituir_grava_o_novo_e_deixa_o_componente_apontando_para_ele()
  {
    var (useCase, _, arquivos) = Montar(ComponenteDeCatalogo(10));

    await useCase.Enviar(10, "antigo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);
    await useCase.Enviar(10, "novo.stl", StlDeTeste.Ascii(), UsuarioId, CancellationToken.None);

    Assert.Equal(2, arquivos.Gravados.Count);
    Assert.Equal("novo.stl", arquivos.SolidoPorComponente[10].NomeOriginal);
  }

  [Fact]
  public async Task Obter_devolve_nome_e_conteudo()
  {
    var (useCase, _, _) = Montar(ComponenteDeCatalogo(10));
    await useCase.Enviar(10, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    var resultado = await useCase.Obter(10, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("cubo.stl", resultado.Valor!.NomeOriginal);
    Assert.Equal(StlDeTeste.CuboBinario(), resultado.Valor!.Conteudo);
  }

  [Fact]
  public async Task Obter_de_componente_sem_solido_da_NaoEncontrado()
  {
    var (useCase, _, _) = Montar(ComponenteDeCatalogo(10));

    var resultado = await useCase.Obter(10, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Enviar_grava_no_componente_pedido_mesmo_com_outro_no_catalogo()
  {
    // Achado por mutacao (Step 11.2 do plano): com um componente so no fake,
    // Enviar_grava_o_arquivo_e_vincula_ao_componente passa mesmo com um LITERAL 10 no lugar do
    // parametro componenteId na chamada a GravarEVincularComoSolidoAsync -- medido rodando essa
    // mutacao de verdade (7/7 continuaram passando). Com dois componentes no fake, enviar para o
    // SEGUNDO e afirmar contra o id dele fecha o buraco.
    var (useCase, _, arquivos) = Montar(ComponenteDeCatalogo(10), ComponenteDeCatalogo(11));

    var resultado = await useCase.Enviar(
        11, "cubo.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(11, Assert.Single(arquivos.VinculadosA));
  }
}
