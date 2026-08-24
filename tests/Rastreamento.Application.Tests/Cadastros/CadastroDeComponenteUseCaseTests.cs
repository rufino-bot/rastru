using Rastreamento.Application.Cadastros;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Cadastros;

public class CadastroDeComponenteUseCaseTests
{
  private static NovoComponenteDto Suporte(string codigo = "SUP-001", string tipo = "Fabricado") =>
      new(codigo, "Suporte lateral", tipo);

  private static Componente Linha(int id, string codigo, bool ativo = true) =>
      new() { Id = id, Codigo = codigo, Descricao = "Suporte", Tipo = "Fabricado", Ativo = ativo };

  [Fact]
  public async Task Cadastra_componente_novo_ativo()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("SUP-001", resultado.Valor!.Codigo);
    Assert.Equal("Suporte lateral", resultado.Valor.Descricao);
    Assert.Equal("Fabricado", resultado.Valor.Tipo);
    Assert.True(resultado.Valor.Ativo);
    Assert.Equal(1, repo.Saves);
    // Adendo B15: Saves==1 sozinho nao prova que a linha chegou ao repositorio (o commit
    // acontece mesmo se o AdicionarAsync sumir). Id>0 e a releitura via Listar fecham o round-trip.
    Assert.True(resultado.Valor.Id > 0);
    var lista = await useCase.Listar(null, false, 1, 20, CancellationToken.None);
    Assert.Single(lista.Valor!.Itens);
  }

  [Theory]
  [InlineData("Bruto")]
  [InlineData("Fabricado")]
  [InlineData("Montagem")]
  public async Task Aceita_os_tres_tipos_do_check(string tipo)
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(tipo: tipo), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(tipo, resultado.Valor!.Tipo);
  }

  [Theory]
  [InlineData("Qualquer")]
  [InlineData("bruto")]
  [InlineData("")]
  public async Task Tipo_fora_da_lista_e_erro_de_validacao(string tipo)
  {
    // "bruto" minusculo entra de proposito: a lista e comparada com == (ordinal), entao caixa
    // errada e recusa. Se um dia isso virar comparacao case-insensitive, este caso morre e
    // obriga a decisao a ser explicita em vez de silenciosa.
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(tipo: tipo), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Theory]
  [InlineData("", "Suporte", "Fabricado")]
  [InlineData("SUP-001", "  ", "Fabricado")]
  public async Task Campo_obrigatorio_em_branco_e_erro_de_validacao(
      string codigo, string descricao, string tipo)
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(
        new NovoComponenteDto(codigo, descricao, tipo), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Codigo_duplicado_e_conflito_e_nao_escreve_nada()
  {
    var repo = new FakeComponenteRepo(Linha(3, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Cadastrar(Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_componente_inexistente_e_nao_encontrado()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(99, Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_mantendo_o_proprio_codigo_nao_e_conflito_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(
        1, new NovoComponenteDto("SUP-001", "Suporte reforcado", "Montagem"),
        CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("Suporte reforcado", resultado.Valor!.Descricao);
    Assert.Equal("Montagem", resultado.Valor.Tipo);
    // Unico teste que prova a escrita do Editar (adendo B2): sem isto, um Editar que muta a
    // entidade e esquece o SalvarAlteracoesAsync passa em todos os outros.
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_codigo_livre_troca_o_codigo_e_persiste()
  {
    // Adendo B15: o teste acima mantem o mesmo codigo ("SUP-001" -> "SUP-001"), entao nao prova
    // a atribuicao `componente.Codigo = codigo;`. Este troca para um codigo LIVRE e assere que o
    // codigo novo persistiu — sem isto, apagar aquela linha ainda passa em 128/128.
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(
        1, new NovoComponenteDto("SUP-009", "Suporte lateral", "Fabricado"), CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("SUP-009", resultado.Valor!.Codigo);
    Assert.Equal(1, repo.Saves);
  }

  [Fact]
  public async Task Editar_para_codigo_de_outro_componente_e_conflito()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"), Linha(2, "SUP-002"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(2, Suporte(), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Conflito, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Editar_com_tipo_invalido_e_erro_de_validacao()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Editar(1, Suporte(tipo: "Errado"), CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Definir_ativo_false_inativa_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, false, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    var soAtivos = await useCase.Listar(null, false, 1, 20, CancellationToken.None);
    var comInativos = await useCase.Listar(null, true, 1, 20, CancellationToken.None);
    Assert.Equal(0, soAtivos.Valor!.Total);
    Assert.Equal(1, comInativos.Valor!.Total);
  }

  [Fact]
  public async Task Definir_ativo_true_reativa_e_persiste()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001", ativo: false));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(1, true, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(1, repo.Saves);
    var soAtivos = await useCase.Listar(null, false, 1, 20, CancellationToken.None);
    Assert.Equal(1, soAtivos.Valor!.Total);
  }

  [Fact]
  public async Task Definir_ativo_em_componente_inexistente_e_nao_encontrado()
  {
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.DefinirAtivo(99, true, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Obter_devolve_o_componente_pedido_e_nao_escreve()
  {
    // DUAS linhas, e o id da segunda: com uma so, um `_linhas[0]` no lugar do `SingleOrDefault`
    // — ou um id preso — passaria verde. `Saves == 0` porque leitura nao commita: sem isso,
    // acrescentar um `SalvarAlteracoesAsync` ao caminho de leitura ficaria despercebido.
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"), Linha(2, "SUP-002", ativo: false));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Obter(2, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(2, resultado.Valor!.Id);
    Assert.Equal("SUP-002", resultado.Valor.Codigo);
    Assert.Equal("Suporte", resultado.Valor.Descricao);
    Assert.Equal("Fabricado", resultado.Valor.Tipo);
    // INATIVO de proposito: `Obter` nao filtra por `Ativo` (a listagem e que filtra). Se alguem
    // acrescentar o filtro aqui, este teste vira 404 e obriga a decisao a ser explicita.
    Assert.False(resultado.Valor.Ativo);
    Assert.Equal(0, repo.Saves);
  }

  [Fact]
  public async Task Obter_componente_inexistente_e_nao_encontrado()
  {
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Obter(99, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, resultado.TipoDoErro);
    Assert.Equal(0, repo.Saves);
  }

  [Theory]
  [InlineData(0, 20)]
  [InlineData(-1, 20)]
  [InlineData(1, 0)]
  [InlineData(1, -5)]
  [InlineData(1, 101)]
  public async Task Faixa_de_paginacao_invalida_e_erro_de_validacao(int pagina, int tamanho)
  {
    // 101 entra porque o teto e 100: sem ele, `?tamanho=100000` devolveria o catalogo inteiro.
    // O banco NAO tem rede de seguranca nenhuma para isto (nao ha CHECK de faixa), entao pelo
    // adendo B14 a mesma propriedade tambem ganha teste no nivel HTTP na Task 3.
    var repo = new FakeComponenteRepo();
    var useCase = new CadastroDeComponenteUseCase(repo);

    var resultado = await useCase.Listar(null, false, pagina, tamanho, CancellationToken.None);

    Assert.False(resultado.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, resultado.TipoDoErro);
  }

  [Fact]
  public async Task Faixa_de_paginacao_no_limite_e_aceita()
  {
    // Controle de escopo do teste acima: 100 exato PASSA. Sem este par, trocar `> 100` por
    // `>= 100` ficaria verde.
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    var resultado = await useCase.Listar(null, false, 1, 100, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal(100, resultado.Valor!.Tamanho);
  }

  [Fact]
  public async Task Listar_repassa_o_filtro_ao_repositorio_e_ecoa_a_faixa()
  {
    // Prova o que o caso de uso TRADUZ, nao o que o fake devolve: sem isto, ignorar `busca` ou
    // trocar `pagina` por 1 fixo passaria despercebido.
    var repo = new FakeComponenteRepo(Linha(1, "SUP-001"), Linha(2, "SUP-002"), Linha(3, "SUP-003"));
    var useCase = new CadastroDeComponenteUseCase(repo);

    // Pagina e tamanho DIFERENTES de proposito (adendo B15): com o mesmo numero nos dois, uma
    // transposicao de `pagina`/`tamanho` na montagem do FiltroDeComponente fica invisivel aqui.
    var resultado = await useCase.Listar("SUP", true, 2, 1, CancellationToken.None);

    Assert.True(resultado.Sucesso);
    Assert.Equal("SUP", repo.UltimoFiltro!.Busca);
    Assert.True(repo.UltimoFiltro.IncluirInativos);
    Assert.Equal(2, repo.UltimoFiltro.Pagina);
    Assert.Equal(1, repo.UltimoFiltro.Tamanho);
    Assert.Equal(2, resultado.Valor!.Pagina);
    Assert.Equal(1, resultado.Valor.Tamanho);
    Assert.Equal(3, resultado.Valor.Total);
    Assert.Single(resultado.Valor.Itens);
    Assert.Equal("SUP-002", resultado.Valor.Itens[0].Codigo);
  }

  [Fact]
  public async Task Localiza_duplicado_inativo_apontando_o_campo_codigo()
  {
    var repo = new FakeComponenteRepo(Linha(9, "SUP-001", ativo: false));
    var useCase = new CadastroDeComponenteUseCase(repo);

    var duplicado = await useCase.LocalizarDuplicado("SUP-001", CancellationToken.None);

    Assert.NotNull(duplicado);
    Assert.Equal("codigo", duplicado!.Campo);
    Assert.True(duplicado.ExisteInativo);
    Assert.Equal(9, duplicado.IdExistente);
  }

  [Fact]
  public async Task Localiza_duplicado_devolve_nulo_quando_codigo_e_livre()
  {
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    Assert.Null(await useCase.LocalizarDuplicado("SUP-001", CancellationToken.None));
  }

  [Fact]
  public async Task Localiza_duplicado_com_codigo_nulo_nao_lanca()
  {
    // Adendo B9: o `?? string.Empty` de `Normalizar` existe porque o desserializador de JSON
    // entrega null mesmo em propriedade nao-anulavel. Sem esta assercao a guarda vira disciplina
    // de codigo — trocar `Normalizar(codigo)` por `codigo.Trim()` pelado nao quebraria nada.
    var useCase = new CadastroDeComponenteUseCase(new FakeComponenteRepo());

    Assert.Null(await useCase.LocalizarDuplicado(null!, CancellationToken.None));
  }
}
