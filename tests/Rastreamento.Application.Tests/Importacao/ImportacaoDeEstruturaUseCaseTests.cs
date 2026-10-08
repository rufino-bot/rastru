using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Importacao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Importacao;

public partial class ImportacaoDeEstruturaUseCaseTests
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
    public FakeExecucaoRepo Execucao { get; }
    public ImportacaoDeEstruturaUseCase UseCase { get; }

    public Montagem(bool comAgrupamento = true)
    {
      var agrupamentos = comAgrupamento
          ? new FakeAgrupamentoRepo(new Agrupamento { Id = AgrupamentoId, PedidoId = 1, Codigo = "AG-01", Tipo = "Kit" })
          : new FakeAgrupamentoRepo();
      // Faz `GravarArvoreAsync` recusar fora da transacao, como o repositorio real.
      Execucao = new FakeExecucaoRepo(Estruturas);
      // O Componente criado pela confirmacao fica visivel para a leitura por Id (a projecao da Peca).
      Catalogo.AoCriar = ReceitaPadrao.Componentes.Add;
      UseCase = new ImportacaoDeEstruturaUseCase(
          Importacoes, agrupamentos, Catalogo, ReceitaPadrao, Estruturas, Leitor, Execucao);
    }

    /// <summary>
    /// Uma linha da receita de catalogo nos DOIS lugares de onde o caso de uso a le: a leitura inteira
    /// (<c>LerReceitaCompletaAsync</c>, a da avaliacao) e a de um pai (<c>ListarFilhosAsync</c>, a da
    /// conferencia da impressao dentro da transacao).
    /// </summary>
    public void ReceitaNoCatalogo(int pai, int filho, decimal quantidade)
    {
      Estruturas.ReceitaFilhos.Add((pai, filho, quantidade));
      ReceitaPadrao.Filhos.Add(new ComponenteFilhoPadrao
      {
        Id = 900 + ReceitaPadrao.Filhos.Count, ComponentePaiId = pai, ComponenteFilhoId = filho, QuantidadePadrao = quantidade,
      });
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

  // ---------------------------------------------------------------- Criar: arquivo recusado (P13) e o que o banco recusaria como 500

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
    Assert.Equal("Linha 3: código com 51 caracteres: o máximo é 50.", r.Detalhe);
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
    Assert.Equal("Linha 2: descrição com 201 caracteres: o máximo é 200.", r.Detalhe);
  }

  [Fact]
  public async Task Criar_com_descricao_vazia_e_BomInvalido_na_linha()
  {
    var m = new Montagem();

    var r = await m.Criar("conjunto.csv", L(2, "1", "A-1", "ok", "1"), L(3, "2", "B-1", "   ", "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.Equal("Linha 3: descrição vazia.", r.Detalhe);
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

  // ---------------------------------------------------------------- Confirmar

  private static Task<Result<EstruturaItemDto>> Confirmar(Montagem m, string? versao = null) =>
      m.UseCase.Confirmar(m.Unica.Id, versao ?? Versao(m.Unica), CancellationToken.None);

  /// <summary>Um solido pendente que o fake de metadados conhece (o avaliador so pergunta se existe).</summary>
  private static int Pendente(Montagem m, int arquivoId, string nome)
  {
    m.Importacoes.Arquivos[arquivoId] = new MetadadoDeSolido(nome, 684);
    return arquivoId;
  }

  /// <summary>
  /// Tira os bloqueios que todo rascunho recem-criado tem: o codigo e o solido da raiz e a quantidade da
  /// Peca. Mexe direto na instancia do fake (a "rastreada"), entao a versao nao muda.
  /// </summary>
  private static void Preparar(Montagem m)
  {
    var raiz = m.Registro(null);
    raiz.CodigoNovo = "RAIZ-1";
    raiz.ArquivoSolidoPendenteId = Pendente(m, 950, "raiz.stl");
    m.Unica.QuantidadeDaPeca = 2m;
    m.Unica.RequerRelatorioDimensional = true;
  }

  /// <summary>
  /// Raiz (nova) -> AB-01 (Componente 10, INATIVO, sem receita no catalogo) -> Z-1 (novo, x2); e raiz ->
  /// CD-02 (Componente 11, receita de catalogo 11 -> 21 x1) -> Y-1 (novo, x3) -> W-1 (novo, x1). CD-02
  /// diverge e fica com a escolha "Catalogo": Y-1 e W-1 saem da arvore final, e CAT-B (21, inativo, so do
  /// catalogo) entra. AB-01, Z-1 e Y-1 tem solido pendente.
  /// </summary>
  private static async Task<Montagem> ComArvoreParaConfirmar()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01", ativo: false);
    m.Componente(11, "CD-02", solido: 701);
    m.Componente(21, "CAT-B", ativo: false, tipo: "Bruto");
    m.ReceitaNoCatalogo(11, 21, 1m);
    await m.Criar(
        "conjunto.csv",
        L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-1", "Filho Z", "2"),
        L(4, "2", "CD-02", "Peca C", "1"), L(5, "2.1", "Y-1", "Filho Y", "3"), L(6, "2.1.1", "W-1", "Neto W", "1"));
    var escolha = await Alterar(m, m.Registro("CD-02"), Casar(m.Unica, 11, ValoresDaConferencia.EscolhaCatalogo));
    Assert.True(escolha.Sucesso);
    Preparar(m);
    m.Registro("AB-01").ArquivoSolidoPendenteId = Pendente(m, 951, "ab.stl");
    m.Registro("Z-1").ArquivoSolidoPendenteId = Pendente(m, 952, "z.stl");
    m.Registro("Y-1").ArquivoSolidoPendenteId = Pendente(m, 953, "y.stl");
    return m;
  }

  private static List<(int FilhoId, decimal Quantidade)> ReceitaDe(Montagem m, int paiId) =>
      m.ReceitaPadrao.Filhos.Where(f => f.ComponentePaiId == paiId).OrderBy(f => f.Id)
          .Select(f => (f.ComponenteFilhoId, f.QuantidadePadrao)).ToList();

  private static void NadaGravado(Montagem m)
  {
    Assert.Empty(m.Catalogo.Adicionados);
    Assert.Equal(0, m.Catalogo.Salvamentos);
    Assert.Equal(0, m.ReceitaPadrao.Substituicoes);
    Assert.Equal(0, m.Estruturas.GravacoesDeArvore);
    Assert.Equal(0, m.Importacoes.Exclusoes);
  }

  [Fact]
  public async Task Confirmar_com_bloqueio_e_ImportacaoComBloqueios_e_nao_escreve_nada()
  {
    // Recem-criado: a raiz esta sem codigo e sem solido, e falta a quantidade da Peca.
    var m = await ComUmCasado();

    var r = await Confirmar(m);

    Assert.False(r.Sucesso);
    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoComBloqueios, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    // Decisao P15 do plano do import: sem a lista, que a tela rele pelo GET.
    Assert.Null(r.Detalhe);
    Assert.Equal(0, m.Execucao.Transacoes);
    Assert.Equal(0, m.Importacoes.Salvamentos);
    NadaGravado(m);
  }

  [Fact]
  public async Task Confirmar_com_impressao_diferente_e_ReceitaDoCatalogoMudou_e_zera_a_escolha()
  {
    var m = await ComArvoreParaConfirmar();
    // AB-01 tambem diverge e tem escolha, mas a receita de catalogo dele nao muda: a escolha dele fica.
    m.ReceitaNoCatalogo(10, 21, 1m);
    Assert.True((await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10, ValoresDaConferencia.EscolhaImportada))).Sucesso);
    // Depois da escolha, a receita de catalogo de CD-02 muda e continua divergindo da lida: 11 -> 21 passa a x5.
    m.Estruturas.ReceitaFilhos[m.Estruturas.ReceitaFilhos.IndexOf((11, 21, 1m))] = (11, 21, 5m);
    m.ReceitaPadrao.Filhos.Single(f => f.ComponentePaiId == 11).QuantidadePadrao = 5m;
    var salvamentosAntes = m.Importacoes.Salvamentos;
    var salvouDentroDaTransacao = new List<bool>();
    m.Importacoes.AposSalvar = () => salvouDentroDaTransacao.Add(m.Execucao.EmTransacao);

    var r = await Confirmar(m);

    Assert.False(r.Sucesso);
    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeReceitaDoCatalogoMudou, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("CD-02", r.Detalhe);
    Assert.DoesNotContain("AB-01", r.Detalhe);
    var cd = m.Registro("CD-02");
    Assert.Null(cd.EscolhaDeReceita);
    Assert.Null(cd.ImpressaoDaReceitaDoCatalogo);
    Assert.Equal(ValoresDaConferencia.EscolhaImportada, m.Registro("AB-01").EscolhaDeReceita);
    Assert.NotNull(m.Registro("AB-01").ImpressaoDaReceitaDoCatalogo);
    // Zerar a escolha e a UNICA escrita, e fora da transacao, que foi desfeita.
    Assert.Equal(salvamentosAntes + 1, m.Importacoes.Salvamentos);
    Assert.Equal([false], salvouDentroDaTransacao);
    Assert.Equal((1, 0), (m.Execucao.Desfeitas, m.Execucao.Commits));
    NadaGravado(m);
    Assert.Equal("RAIZ-1", m.Registro(null).CodigoNovo);
  }

  [Fact]
  public async Task Falha_qualquer_ao_zerar_a_escolha_nao_troca_o_409_ReceitaDoCatalogoMudou()
  {
    var m = await ComArvoreParaConfirmar();
    m.Estruturas.ReceitaFilhos[m.Estruturas.ReceitaFilhos.IndexOf((11, 21, 1m))] = (11, 21, 5m);
    m.ReceitaPadrao.Filhos.Single(f => f.ComponentePaiId == 11).QuantidadePadrao = 5m;
    // Zerar a escolha e limpeza de melhor esforco: uma falha nela nao vira 500.
    m.Importacoes.FalhaNoSalvamento = new InvalidOperationException("queda do banco simulada");

    var r = await Confirmar(m);

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeReceitaDoCatalogoMudou, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("CD-02", r.Detalhe);
    NadaGravado(m);
  }

  [Fact]
  public async Task Confirmar_cria_novos_reativa_inativos_grava_solidos_e_receitas_so_da_arvore_final()
  {
    var m = await ComArvoreParaConfirmar();
    var ligados = new[] { m.Registro(null).Id, m.Registro("AB-01").Id, m.Registro("Z-1").Id };

    var r = await Confirmar(m);

    Assert.True(r.Sucesso, r.Detalhe ?? r.Erro);
    Assert.Equal((1, 0), (m.Execucao.Commits, m.Execucao.Desfeitas));
    // Os pendentes que viraram solido (raiz, AB-01, Z-1) saem do rascunho, por registro, antes de ele ser
    // apagado, e ficam; o de Y-1, que saiu da arvore, vai embora com o rascunho.
    Assert.Equal(ligados.Order(), Assert.Single(m.Importacoes.Desligamentos).Order());
    Assert.Equal([953], m.Importacoes.ArquivosExcluidos);
    // Decisao P3: so a raiz e Z-1 viram Componente; Y-1 e W-1 sairam da arvore pela escolha de CD-02.
    Assert.Equal(["RAIZ-1", "Z-1"], m.Catalogo.Adicionados.Select(c => c.Codigo).Order());
    var raiz = m.Catalogo.Adicionados.Single(c => c.Codigo == "RAIZ-1");
    var z = m.Catalogo.Adicionados.Single(c => c.Codigo == "Z-1");
    Assert.Equal(("conjunto", "Montagem", true, (int?)950), (raiz.Descricao, raiz.Tipo, raiz.Ativo, raiz.ArquivoSolidoId));
    Assert.Equal(("Filho Z", "Fabricado", true, (int?)952), (z.Descricao, z.Tipo, z.Ativo, z.ArquivoSolidoId));
    // O casado inativo da arvore final e reativado e recebe o solido pendente. CAT-B, que so entra pela
    // receita de catalogo, continua inativo, como na Nova Peca.
    var ab = m.Catalogo.Componentes.Single(c => c.Id == 10);
    Assert.Equal((true, (int?)951), (ab.Ativo, ab.ArquivoSolidoId));
    Assert.False(m.Catalogo.Componentes.Single(c => c.Id == 21).Ativo);
    // A receita lida da raiz e de AB-01 vai para o catalogo, com o Id real de Z-1. Z-1 e folha: nada a gravar.
    Assert.Equal([(10, 1m), (11, 1m)], ReceitaDe(m, raiz.Id));
    Assert.Equal([(z.Id, 2m)], ReceitaDe(m, 10));
    Assert.Equal(2, m.ReceitaPadrao.SubstituicoesDeFilhos);
    Assert.Equal(0, m.ReceitaPadrao.SubstituicoesDeMateriais + m.ReceitaPadrao.SubstituicoesDeRoteiro);

    // A Peca devolvida e a projecao do que foi gravado: a arvore final na quantidade da Peca.
    var peca = r.Valor!;
    Assert.Equal((raiz.Id, 2m, "Peca", true), (peca.ComponenteId, peca.Quantidade, peca.NivelHierarquico, peca.RequerRelatorioDimensional));
    Assert.Equal(["AB-01", "CD-02"], peca.Filhos.Select(f => f.CodigoDoComponente));
    Assert.All(peca.Filhos, f => Assert.False(f.RequerRelatorioDimensional));
    var noZ = Assert.Single(peca.Filhos[0].Filhos);
    Assert.Equal(("Z-1", 4m, (decimal?)2m), (noZ.CodigoDoComponente, noZ.Quantidade, noZ.QuantidadePorPai));
    var noCatB = Assert.Single(peca.Filhos[1].Filhos);
    Assert.Equal(("CAT-B", 2m, (decimal?)1m), (noCatB.CodigoDoComponente, noCatB.Quantidade, noCatB.QuantidadePorPai));
    Assert.Equal(1, m.Estruturas.GravacoesDeArvore);
  }

  [Fact]
  public async Task Confirmar_nao_toca_na_receita_de_codigo_com_escolha_Catalogo()
  {
    var m = await ComArvoreParaConfirmar();
    var linhasAntes = m.ReceitaPadrao.Filhos.Where(f => f.ComponentePaiId == 11).Select(f => (f.Id, f.ComponenteFilhoId, f.QuantidadePadrao)).ToList();

    var r = await Confirmar(m);

    Assert.True(r.Sucesso, r.Detalhe ?? r.Erro);
    // As MESMAS linhas, com os mesmos Ids: uma substituicao, ainda que pelo mesmo conteudo, as renumeraria.
    Assert.Equal(linhasAntes,
        m.ReceitaPadrao.Filhos.Where(f => f.ComponentePaiId == 11).Select(f => (f.Id, f.ComponenteFilhoId, f.QuantidadePadrao)));
    // E a receita lida dele (Y-1 x3) nao foi parar no catalogo por nenhum outro caminho.
    Assert.DoesNotContain(m.Catalogo.Adicionados, c => c.Codigo is "Y-1" or "W-1");
  }

  [Fact]
  public async Task Confirmar_apaga_o_rascunho()
  {
    var m = await ComArvoreParaConfirmar();
    var id = m.Unica.Id;

    var r = await Confirmar(m);

    Assert.True(r.Sucesso, r.Detalhe ?? r.Erro);
    Assert.Equal(1, m.Importacoes.Exclusoes);
    Assert.Empty(m.Importacoes.Importacoes);
    Assert.Equal(TipoDeErro.NaoEncontrado, (await m.UseCase.Obter(id, CancellationToken.None)).TipoDoErro);
  }

  [Fact]
  public async Task Confirmar_com_versao_velha_e_ImportacaoDesatualizada_e_nao_escreve_nada()
  {
    var m = await ComArvoreParaConfirmar();

    var r = await Confirmar(m, Convert.ToBase64String(BitConverter.GetBytes(999L)));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoDesatualizada, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    NadaGravado(m);
  }

  [Fact]
  public async Task Versao_trocada_no_banco_depois_da_avaliacao_e_ImportacaoDesatualizada_e_desfaz()
  {
    var m = await ComArvoreParaConfirmar();
    // Outra escrita salvou depois da leitura de fora da transacao: o banco ja tem outra versao.
    m.Importacoes.VersaoNoBanco = BitConverter.GetBytes(999L);

    var r = await Confirmar(m);

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoDesatualizada, r.Erro);
    Assert.Equal((1, 0), (m.Execucao.Desfeitas, m.Execucao.Commits));
    NadaGravado(m);
  }

  [Fact]
  public async Task Escolha_em_codigo_que_deixou_de_divergir_nao_tem_a_impressao_conferida()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01", solido: 700);
    m.Componente(20, "CAT-F", solido: 702);
    m.ReceitaNoCatalogo(10, 20, 1m);
    await m.Criar("conjunto.csv", L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "CAT-F", "Filho", "2"));
    Assert.True((await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10, ValoresDaConferencia.EscolhaImportada))).Sucesso);
    Preparar(m);
    // O catalogo passa a ser igual a lida: AB-01 nao diverge mais, e a escolha gravada fica inerte.
    m.Estruturas.ReceitaFilhos[0] = (10, 20, 2m);
    m.ReceitaPadrao.Filhos.Single(f => f.ComponentePaiId == 10).QuantidadePadrao = 2m;

    var r = await Confirmar(m);

    Assert.True(r.Sucesso, r.Detalhe ?? r.Erro);
    Assert.Equal([(20, 2m)], ReceitaDe(m, 10));
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
