using Rastreamento.Application.Common;
using Rastreamento.Application.Importacao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Importacao;

public class ImportacaoDeEstruturaUseCaseTests
{
  private const int AgrupamentoId = 5;
  private const int UsuarioId = 42;

  private sealed class Montagem
  {
    public FakeImportacaoRepo Importacoes { get; } = new();
    public FakeCatalogoDeComponentes Catalogo { get; } = new();
    public FakeReceitaPadraoRepo ReceitaPadrao { get; } = new();
    public FakeEstruturaRepo Estruturas { get; } = new();
    public FakeLeitorDeBom Leitor { get; } = new();
    public ImportacaoDeEstruturaUseCase UseCase { get; }

    public Montagem(bool comAgrupamento = true)
    {
      var agrupamentos = comAgrupamento
          ? new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" })
          : new FakeAgrupamentoRepo();
      UseCase = new ImportacaoDeEstruturaUseCase(
          Importacoes, agrupamentos, Catalogo, ReceitaPadrao, Estruturas, Leitor);
    }

    /// <summary>Um Componente do catalogo, visivel para a busca por codigo e para a leitura por Id.</summary>
    public Componente Componente(int id, string codigo, bool ativo = true, int? solido = null, string tipo = "Fabricado")
    {
      var c = new Componente
      {
        Id = id, Codigo = codigo, Descricao = $"Desc {codigo}", Tipo = tipo, Ativo = ativo, ArquivoSolidoId = solido,
      };
      Catalogo.Componentes.Add(c);
      ReceitaPadrao.Componentes.Add(c);
      return c;
    }

    public Task<Result<ImportacaoDto>> Criar(string nome, params LinhaCruaDoBom[] linhas)
    {
      Leitor.Linhas = linhas;
      return UseCase.Criar(AgrupamentoId, nome, [1, 2, 3], UsuarioId, CancellationToken.None);
    }

    public ImportacaoDeEstrutura Unica => Importacoes.Importacoes.Single();

    public ImportacaoDeEstruturaComponente Registro(string? codigoLido) =>
        Unica.Componentes.Single(c => string.Equals(c.CodigoLido, codigoLido, StringComparison.Ordinal));
  }

  private static LinhaCruaDoBom L(int linha, string nivel, string? codigo, string descricao, string quantidade) =>
      new(linha, nivel, codigo, descricao, quantidade);

  // ---------------------------------------------------------------- Criar

  [Fact]
  public async Task Criar_casa_por_codigo_sem_diferenciar_caixa_e_cria_novo_para_o_resto()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01");

    var r = await m.Criar("conjunto.csv", L(2, "1", "ab-01", "Peca do CAD", "2"), L(3, "2", "XY-9", "Outra", "1"));

    Assert.True(r.Sucesso);
    var casado = m.Registro("ab-01");
    Assert.Equal(10, casado.ComponenteId);
    Assert.Null(casado.CodigoNovo);
    Assert.Null(casado.DescricaoNova);
    Assert.Null(casado.TipoNovo);
    var novo = m.Registro("XY-9");
    Assert.Null(novo.ComponenteId);
    Assert.Equal("XY-9", novo.CodigoNovo);
    // O que o caso de uso perguntou ao catalogo foi o codigo LIDO; quem ignora a caixa e o catalogo.
    Assert.Contains("ab-01", m.Catalogo.CodigosPedidos[0]);
    var situacao = r.Valor!.Componentes.Single(s => s.RegistroId == casado.Id);
    Assert.Equal(10, situacao.ComponenteId);
    Assert.Equal("AB-01", situacao.CodigoDoCatalogo);
  }

  [Fact]
  public async Task Criar_casa_com_inativo()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01", ativo: false);

    var r = await m.Criar("conjunto.csv", L(2, "1", "AB-01", "Peca", "1"));

    Assert.True(r.Sucesso);
    var casado = m.Registro("AB-01");
    Assert.Equal(10, casado.ComponenteId);
    var situacao = r.Valor!.Componentes.Single(s => s.RegistroId == casado.Id);
    Assert.False(situacao.Ativo);
    Assert.Contains(ValoresDaConferencia.PendenciaInativo, r.Valor.Raiz!.Filhos.Single().Pendencias);
  }

  [Fact]
  public async Task Criar_preenche_codigo_descricao_e_tipo_do_novo()
  {
    var m = new Montagem();

    var r = await m.Criar(
        "Suporte do motor.csv",
        L(2, "1", "  A-1 ", "Conjunto A", "1"),
        L(3, "1.1", "B-1", "Parafuso B", "4"));

    Assert.True(r.Sucesso);
    var a = m.Registro("A-1");
    Assert.Equal(("A-1", "Conjunto A", "Montagem"), (a.CodigoNovo, a.DescricaoNova, a.TipoNovo));
    Assert.Equal("Conjunto A", a.DescricaoLida);
    var b = m.Registro("B-1");
    Assert.Equal(("B-1", "Parafuso B", "Fabricado"), (b.CodigoNovo, b.DescricaoNova, b.TipoNovo));
    Assert.Equal(m.Registro("B-1").Id, m.Registro("A-1").Filhos.Single().FilhoId);
  }

  [Fact]
  public async Task Criar_poe_a_raiz_sem_codigo_com_o_nome_do_arquivo_e_o_tipo_pelos_filhos()
  {
    var m = new Montagem();

    var r = await m.Criar("Suporte do motor.csv", L(2, "1", "A-1", "Conjunto A", "1"));

    Assert.True(r.Sucesso);
    var raiz = m.Unica.Componentes.Single(c => c.Id == m.Unica.RaizId);
    Assert.Null(raiz.CodigoLido);
    Assert.Equal("Suporte do motor", raiz.DescricaoLida);
    Assert.Null(raiz.ComponenteId);
    Assert.Null(raiz.CodigoNovo);
    Assert.Equal("Suporte do motor", raiz.DescricaoNova);
    Assert.Equal("Montagem", raiz.TipoNovo);
    var filho = raiz.Filhos.Single();
    Assert.Equal(m.Registro("A-1").Id, filho.FilhoId);
    Assert.Equal((1m, 1m), (filho.QuantidadeLida, filho.Quantidade));
  }

  [Fact]
  public async Task Criar_devolve_autor_versao_e_o_estado_calculado()
  {
    var m = new Montagem();
    m.Importacoes.NomeDoAutor = "Maria Souza";

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "Conjunto A", "1"));

    var dto = r.Valor!;
    Assert.Equal(m.Unica.Id, dto.Id);
    Assert.Equal(AgrupamentoId, dto.AgrupamentoId);
    Assert.Equal("conjunto.csv", dto.NomeDoArquivo);
    Assert.Equal("Maria Souza", dto.CriadoPor);
    Assert.Equal(m.Unica.Versao, dto.Versao);
    Assert.Equal(m.Unica.CriadoEm, dto.CriadoEm);
    Assert.Null(dto.QuantidadeDaPeca);
    Assert.False(dto.RequerRelatorioDimensional);
    Assert.NotNull(dto.Raiz);
    Assert.Equal(2, dto.Componentes.Count);
    Assert.Contains(dto.Bloqueios, b => b.Tipo == ValoresDaConferencia.BloqueioQuantidadeDaPecaAusente);
    Assert.Equal(UsuarioId, m.Unica.CriadoPorUsuarioId);
  }

  [Fact]
  public async Task Criar_com_o_nome_do_arquivo_com_caminho_guarda_so_o_nome()
  {
    var m = new Montagem();

    var r = await m.Criar(@"C:\pasta\conjunto.csv", L(2, "1", "A-1", "Conjunto A", "1"));

    Assert.Equal("conjunto.csv", r.Valor!.NomeDoArquivo);
    Assert.Equal("conjunto.csv", m.Unica.NomeDoArquivo);
    Assert.Equal("conjunto.csv", m.Leitor.NomeRecebido);
  }

  [Fact]
  public async Task Criar_em_agrupamento_inexistente_e_NaoEncontrado()
  {
    var m = new Montagem(comAgrupamento: false);

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "Conjunto A", "1"));

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Empty(m.Importacoes.Importacoes);
    Assert.Null(m.Leitor.NomeRecebido);
  }

  // ---------------------------------------------------------------- Criar: arquivo recusado (P13, R6)

  [Fact]
  public async Task Criar_com_bom_invalido_devolve_BomInvalido_com_uma_linha_por_erro()
  {
    var m = new Montagem();
    m.Leitor.Erros = [new ErroDoBom(3, "quantidade invalida."), new ErroDoBom(9, "nivel invalido.")];

    var r = await m.Criar("conjunto.csv");

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.Equal("Linha 3: quantidade invalida.\nLinha 9: nivel invalido.", r.Detalhe);
    Assert.Empty(m.Importacoes.Importacoes);
  }

  [Fact]
  public async Task Criar_com_erro_de_receita_do_montador_tambem_e_BomInvalido()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "Conjunto A", "abc"), L(3, "2", "B-1", "B", "x"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    var linhas = r.Detalhe!.Split('\n');
    Assert.Equal(2, linhas.Length);
    Assert.StartsWith("Linha 2: ", linhas[0]);
    Assert.StartsWith("Linha 3: ", linhas[1]);
    Assert.Empty(m.Importacoes.Importacoes);
  }

  [Fact]
  public async Task Criar_com_codigo_de_mais_de_50_caracteres_e_BomInvalido_na_linha()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "ok", "1"), L(3, "2", new string('X', 51), "Peca", "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.StartsWith("Linha 3: ", r.Detalhe);
    Assert.Contains("50", r.Detalhe);
    Assert.Empty(m.Importacoes.Importacoes);
  }

  [Fact]
  public async Task Criar_com_codigo_de_exatos_50_caracteres_passa()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", new string('X', 50), "Peca", "1"));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Criar_com_descricao_de_mais_de_200_caracteres_e_BomInvalido_na_linha()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", new string('d', 201), "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.StartsWith("Linha 2: ", r.Detalhe);
    Assert.Contains("200", r.Detalhe);
  }

  [Fact]
  public async Task Criar_com_descricao_vazia_e_BomInvalido_na_linha()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "ok", "1"), L(3, "2", "B-1", "   ", "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.Equal("Linha 3: descricao vazia.", r.Detalhe);
  }

  [Fact]
  public async Task Criar_so_cobra_a_descricao_da_primeira_ocorrencia_do_codigo()
  {
    var m = new Montagem();

    // A descricao do registro e a da primeira linha do codigo; a repeticao com descricao vazia nao a muda.
    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "Peca A", "1"), L(3, "2", "B-1", "Peca B", "1"), L(4, "2.1", "A-1", "", "1"));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task Criar_reporta_todos_os_erros_de_tamanho_juntos_na_ordem_das_linhas()
  {
    var m = new Montagem();

    var r = await m.Criar(
        "conjunto.csv", L(2, "1", new string('X', 51), "Peca", "1"), L(3, "2", "B-1", "", "1"));

    var linhas = r.Detalhe!.Split('\n');
    Assert.Equal(2, linhas.Length);
    Assert.StartsWith("Linha 2: ", linhas[0]);
    Assert.StartsWith("Linha 3: ", linhas[1]);
  }

  [Fact]
  public async Task Criar_com_nome_de_arquivo_cuja_descricao_da_raiz_passa_de_200_e_BomInvalido()
  {
    var m = new Montagem();

    var r = await m.Criar($"{new string('n', 201)}.csv", L(2, "1", "A-1", "Peca", "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.Contains("nome do arquivo", r.Detalhe);
    Assert.Empty(m.Importacoes.Importacoes);
  }

  // ---------------------------------------------------------------- Obter, projecao

  [Fact]
  public async Task Obter_devolve_o_mesmo_estado_que_o_criar_devolveu()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01");
    var criado = (await m.Criar("conjunto.csv", L(2, "1", "AB-01", "Peca", "2"), L(3, "2", "XY-9", "Outra", "1"))).Valor!;

    var lido = await m.UseCase.Obter(criado.Id, CancellationToken.None);

    Assert.True(lido.Sucesso);
    Assert.Equal(criado.Versao, lido.Valor!.Versao);
    Assert.Equal(criado.Bloqueios, lido.Valor.Bloqueios);
    Assert.Equal(
        System.Text.Json.JsonSerializer.Serialize(criado.Componentes),
        System.Text.Json.JsonSerializer.Serialize(lido.Valor.Componentes));
    Assert.Equal(criado.Raiz!.Filhos.Select(f => f.Codigo), lido.Valor.Raiz!.Filhos.Select(f => f.Codigo));
  }

  [Fact]
  public async Task Obter_inexistente_e_NaoEncontrado()
  {
    var m = new Montagem();

    var r = await m.UseCase.Obter(999, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  [Fact]
  public async Task Obter_traz_o_metadado_do_solido_do_catalogo_e_do_pendente()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01", solido: 70);
    m.Importacoes.Arquivos[70] = new MetadadoDeSolido("catalogo.stl", 1234);
    m.Importacoes.Arquivos[71] = new MetadadoDeSolido("pendente.stl", 5678);
    var rascunho = Rascunho(
        Registro(1, null, "Raiz", pendente: 71),
        Registro(2, "AB-01", "Peca", componenteId: 10));
    rascunho.RaizId = 1;
    Aresta(rascunho, 1, 2, 1m);
    m.Importacoes.Importacoes.Add(rascunho);

    var r = await m.UseCase.Obter(rascunho.Id, CancellationToken.None);

    var dto = r.Valor!;
    var daRaiz = dto.Componentes.Single(c => c.RegistroId == 1);
    Assert.True(daRaiz.TemSolidoPendente);
    Assert.Equal(("pendente.stl", 5678), (daRaiz.NomeDoSolido, daRaiz.TamanhoDoSolidoEmBytes));
    var daPeca = dto.Componentes.Single(c => c.RegistroId == 2);
    Assert.True(daPeca.TemSolido);
    Assert.Equal(("catalogo.stl", 1234), (daPeca.NomeDoSolido, daPeca.TamanhoDoSolidoEmBytes));
    Assert.Equal(new[] { 70, 71 }, m.Importacoes.MetadadosPedidos.Single().Order());
  }

  [Fact]
  public async Task Obter_marca_CodigoJaExiste_quando_o_codigo_novo_ja_esta_no_catalogo_sem_diferenciar_caixa()
  {
    var m = new Montagem();
    m.Componente(10, "existe-1");
    var rascunho = Rascunho(Registro(1, null, "Raiz"), Registro(2, "X", "Peca", codigoNovo: "EXISTE-1"));
    rascunho.RaizId = 1;
    Aresta(rascunho, 1, 2, 1m);
    m.Importacoes.Importacoes.Add(rascunho);

    var r = await m.UseCase.Obter(rascunho.Id, CancellationToken.None);

    var bloqueio = Assert.Single(r.Valor!.Bloqueios, b => b.Tipo == ValoresDaConferencia.BloqueioCodigoJaExiste);
    Assert.Equal(2, bloqueio.RegistroId);
  }

  // ---------------------------------------------------------------- Listar

  [Fact]
  public async Task Listar_traz_os_rascunhos_do_agrupamento_e_nao_os_de_outro()
  {
    var m = new Montagem();
    await m.Criar("um.csv", L(2, "1", "A-1", "A", "1"));
    await m.Criar("dois.csv", L(2, "1", "A-1", "A", "1"));
    m.Importacoes.Importacoes.Add(new ImportacaoDeEstrutura { Id = 900, AgrupamentoId = 77, NomeDoArquivo = "de-outro.csv" });

    var r = await m.UseCase.Listar(AgrupamentoId, CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(new[] { "dois.csv", "um.csv" }, r.Valor!.Select(x => x.NomeDoArquivo));
    Assert.All(r.Valor!, x => Assert.Equal("Autora de Teste", x.CriadoPor));
  }

  [Fact]
  public async Task Listar_em_agrupamento_inexistente_e_NaoEncontrado()
  {
    var m = new Montagem(comAgrupamento: false);

    var r = await m.UseCase.Listar(AgrupamentoId, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- Descartar

  [Fact]
  public async Task Descartar_apaga_tudo()
  {
    var m = new Montagem();
    var criado = (await m.Criar("conjunto.csv", L(2, "1", "A-1", "A", "1"))).Valor!;

    var r = await m.UseCase.Descartar(criado.Id, CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal(1, m.Importacoes.Exclusoes);
    Assert.Empty(m.Importacoes.Importacoes);
    var depois = await m.UseCase.Obter(criado.Id, CancellationToken.None);
    Assert.Equal(TipoDeErro.NaoEncontrado, depois.TipoDoErro);
  }

  [Fact]
  public async Task Descartar_inexistente_e_NaoEncontrado_e_nao_apaga_nada()
  {
    var m = new Montagem();

    var r = await m.UseCase.Descartar(999, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Equal(0, m.Importacoes.Exclusoes);
  }

  // ---------------------------------------------------------------- rascunhos montados a mao

  private static ImportacaoDeEstrutura Rascunho(params ImportacaoDeEstruturaComponente[] registros) => new()
  {
    Id = 400, AgrupamentoId = AgrupamentoId, NomeDoArquivo = "a-mao.csv", CriadoPorUsuarioId = UsuarioId,
    QuantidadeDaPeca = 1m, Versao = [1], Componentes = [.. registros],
  };

  private static ImportacaoDeEstruturaComponente Registro(
      int id, string? codigoLido, string descricao, int? componenteId = null, string? codigoNovo = null, int? pendente = null) =>
      new()
      {
        Id = id, ImportacaoId = 400, CodigoLido = codigoLido, DescricaoLida = descricao, ComponenteId = componenteId,
        CodigoNovo = componenteId is null ? codigoNovo : null,
        DescricaoNova = componenteId is null ? descricao : null,
        TipoNovo = componenteId is null ? "Fabricado" : null,
        ArquivoSolidoPendenteId = pendente,
      };

  private static void Aresta(ImportacaoDeEstrutura r, int paiId, int filhoId, decimal quantidade)
  {
    var pai = r.Componentes.Single(c => c.Id == paiId);
    pai.Filhos.Add(new ImportacaoDeEstruturaFilho
    {
      Id = 1000 + pai.Filhos.Count + paiId, PaiId = paiId, FilhoId = filhoId, Ordem = pai.Filhos.Count + 1,
      QuantidadeLida = quantidade, Quantidade = quantidade,
    });
  }
}
