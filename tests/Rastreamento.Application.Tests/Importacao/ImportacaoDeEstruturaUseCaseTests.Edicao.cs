using Rastreamento.Application.Arquivos;
using Rastreamento.Application.Common;
using Rastreamento.Application.Importacao;
using Rastreamento.Application.Tests.Arquivos;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Importacao;

/// <summary>
/// As escritas da conferencia: Peca, casamento, escolha, quantidade, solido pendente e reimport. A
/// persistencia e o <see cref="FakeImportacaoRepo"/>, que guarda a MESMA instancia como o contexto
/// rastreado do EF; o que so o banco prova (exclusao em ordem, conflito de ROWVERSION de verdade) esta
/// em <c>ImportacaoEndpointsTests</c>.
/// </summary>
public partial class ImportacaoDeEstruturaUseCaseTests
{
  private static string Versao(ImportacaoDeEstrutura r) => Convert.ToBase64String(r.Versao);

  private static AlteracaoDeComponenteDto Casar(ImportacaoDeEstrutura r, int? componenteId, string? escolha = null,
      string? codigoNovo = null, string? descricaoNova = null, string? tipoNovo = null) =>
      new(Versao(r), componenteId, codigoNovo, descricaoNova, tipoNovo, escolha);

  private static Task<Result<ImportacaoDto>> Alterar(
      Montagem m, ImportacaoDeEstruturaComponente registro, AlteracaoDeComponenteDto dto) =>
      m.UseCase.AlterarComponente(m.Unica.Id, registro.Id, dto, CancellationToken.None);

  /// <summary>Um rascunho com a raiz e dois filhos, e o catalogo com o Componente 10 casando com AB-01.</summary>
  private static async Task<Montagem> ComUmCasado()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01");
    await m.Criar("conjunto.csv", L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-1", "Filho Z", "2"));
    return m;
  }

  /// <summary>O Componente 10 (AB-01) com receita de catalogo 10 -> 20 (x1), diferente da lida (Z-1 x2): diverge.</summary>
  private static async Task<Montagem> ComUmDivergente()
  {
    var m = await ComUmCasado();
    m.Componente(20, "CAT-F");
    m.Estruturas.ReceitaFilhos.Add((10, 20, 1m));
    return m;
  }

  // ---------------------------------------------------------------- Peca

  [Fact]
  public async Task Alterar_peca_grava_a_quantidade_e_o_relatorio_e_troca_a_versao()
  {
    var m = await ComUmCasado();
    var antes = Versao(m.Unica);

    var r = await m.UseCase.AlterarPeca(
        m.Unica.Id, new AlteracaoDaPecaDto(antes, 3.5m, true), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal((3.5m, true), (m.Unica.QuantidadeDaPeca, m.Unica.RequerRelatorioDimensional));
    Assert.Equal(3.5m, r.Valor!.QuantidadeDaPeca);
    Assert.NotEqual(antes, Convert.ToBase64String(r.Valor.Versao));
    Assert.DoesNotContain(r.Valor.Bloqueios, b => b.Tipo == ValoresDaConferencia.BloqueioQuantidadeDaPecaAusente);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00001")]
  [InlineData("100000000000000")]
  public async Task Alterar_peca_fora_da_faixa_da_coluna_e_Validacao(string quantidade)
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.AlterarPeca(
        m.Unica.Id, new AlteracaoDaPecaDto(Versao(m.Unica), decimal.Parse(quantidade, System.Globalization.CultureInfo.InvariantCulture), false),
        CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Null(m.Unica.QuantidadeDaPeca);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Alterar_peca_com_quantidade_nula_limpa_a_quantidade()
  {
    var m = await ComUmCasado();
    m.Unica.QuantidadeDaPeca = 2m;

    var r = await m.UseCase.AlterarPeca(m.Unica.Id, new AlteracaoDaPecaDto(Versao(m.Unica), null, false), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Null(m.Unica.QuantidadeDaPeca);
  }

  // ---------------------------------------------------------------- casamento

  [Fact]
  public async Task Casar_manualmente_limpa_os_campos_do_novo_e_trocar_para_novo_preenche_com_o_lido()
  {
    var m = new Montagem();
    m.Componente(10, "CAT-1");
    m.Componente(11, "Z-1");
    await m.Criar("conjunto.csv", L(2, "1", "XY-9", "Conjunto X", "1"), L(3, "1.1", "Z-1", "Parafuso Z", "2"));
    var conjunto = m.Registro("XY-9");
    var parafuso = m.Registro("Z-1");
    Assert.Equal(11, parafuso.ComponenteId);

    // Novo -> casado: os tres campos do novo saem, para nao violar CK_..._CasadoOuNovo.
    var casado = await Alterar(m, conjunto, Casar(m.Unica, 10, codigoNovo: "ignorado", tipoNovo: "Bruto"));
    Assert.True(casado.Sucesso);
    Assert.Equal(10, conjunto.ComponenteId);
    Assert.Equal((null, null, null), (conjunto.CodigoNovo, conjunto.DescricaoNova, conjunto.TipoNovo));
    Assert.Equal(10, casado.Valor!.Componentes.Single(c => c.RegistroId == conjunto.Id).ComponenteId);

    // Casado -> novo: o que o arquivo leu, com o tipo pelos filhos (Montagem com filhos, Fabricado sem).
    var novo = await Alterar(m, conjunto, Casar(m.Unica, null));
    Assert.True(novo.Sucesso);
    Assert.Null(conjunto.ComponenteId);
    Assert.Equal(("XY-9", "Conjunto X", "Montagem"), (conjunto.CodigoNovo, conjunto.DescricaoNova, conjunto.TipoNovo));
    var folha = await Alterar(m, parafuso, Casar(m.Unica, null));
    Assert.True(folha.Sucesso);
    Assert.Equal(("Z-1", "Parafuso Z", "Fabricado"), (parafuso.CodigoNovo, parafuso.DescricaoNova, parafuso.TipoNovo));
  }

  [Fact]
  public async Task Novo_registro_recebe_codigo_descricao_e_tipo_do_corpo_e_o_que_nao_veio_fica()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = await Alterar(m, z, Casar(m.Unica, null, codigoNovo: "  Z-NOVO ", tipoNovo: "Bruto"));

    Assert.True(r.Sucesso);
    Assert.Equal(("Z-NOVO", "Filho Z", "Bruto"), (z.CodigoNovo, z.DescricaoNova, z.TipoNovo));

    var vazio = await Alterar(m, z, Casar(m.Unica, null, codigoNovo: "   "));
    Assert.Null(z.CodigoNovo);
    Assert.Equal(ValoresDaConferencia.BloqueioCodigoVazio, vazio.Valor!.Bloqueios.Single(b => b.RegistroId == z.Id && b.Tipo == ValoresDaConferencia.BloqueioCodigoVazio).Tipo);
  }

  [Fact]
  public async Task Casar_com_componente_inexistente_e_NaoEncontrado()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = await Alterar(m, z, Casar(m.Unica, 999));

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Null(z.ComponenteId);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Casar_com_componente_ja_casado_em_outro_registro_do_rascunho_e_Validacao()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = await Alterar(m, z, Casar(m.Unica, 10));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Null(z.ComponenteId);
    Assert.Equal(10, m.Registro("AB-01").ComponenteId);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Casar_de_novo_com_o_mesmo_componente_nao_conta_como_ja_casado()
  {
    var m = await ComUmCasado();

    var r = await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10));

    Assert.True(r.Sucesso);
  }

  [Fact]
  public async Task TipoNovo_fora_do_dominio_e_Validacao()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = await Alterar(m, z, Casar(m.Unica, null, tipoNovo: "Kit"));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal("Fabricado", z.TipoNovo);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Theory]
  [InlineData("codigo")]
  [InlineData("descricao")]
  public async Task Texto_do_novo_acima_da_coluna_e_Validacao(string campo)
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = campo == "codigo"
        ? await Alterar(m, z, Casar(m.Unica, null, codigoNovo: new string('C', 51)))
        : await Alterar(m, z, Casar(m.Unica, null, descricaoNova: new string('d', 201)));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Registro_inexistente_e_NaoEncontrado()
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.AlterarComponente(m.Unica.Id, 99999, Casar(m.Unica, null), CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- escolha de receita

  [Fact]
  public async Task Escolher_receita_grava_a_impressao_do_catalogo_vista()
  {
    var m = await ComUmDivergente();
    var casado = m.Registro("AB-01");

    var r = await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaCatalogo));

    Assert.True(r.Sucesso);
    Assert.Equal("Catalogo", casado.EscolhaDeReceita);
    Assert.Equal(AvaliadorDeImportacao.Impressao([(20, 1m)]), casado.ImpressaoDaReceitaDoCatalogo);
    Assert.Equal("Catalogo", r.Valor!.Componentes.Single(c => c.RegistroId == casado.Id).EscolhaDeReceita);
    Assert.DoesNotContain(r.Valor.Bloqueios, b => b.Tipo == ValoresDaConferencia.BloqueioDivergenciaSemEscolha);
  }

  [Fact]
  public async Task Escolher_receita_importada_tambem_grava_a_impressao_e_limpar_a_escolha_a_apaga()
  {
    var m = await ComUmDivergente();
    var casado = m.Registro("AB-01");
    await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaImportada));
    Assert.Equal("Importada", casado.EscolhaDeReceita);
    Assert.NotNull(casado.ImpressaoDaReceitaDoCatalogo);

    var r = await Alterar(m, casado, Casar(m.Unica, 10, null));

    Assert.True(r.Sucesso);
    Assert.Null(casado.EscolhaDeReceita);
    Assert.Null(casado.ImpressaoDaReceitaDoCatalogo);
  }

  [Fact]
  public async Task Escolha_em_codigo_que_nao_diverge_e_Validacao()
  {
    var m = await ComUmCasado();
    var casado = m.Registro("AB-01");

    var r = await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaCatalogo));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Null(casado.EscolhaDeReceita);
    Assert.Null(casado.ImpressaoDaReceitaDoCatalogo);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Escolha_num_registro_novo_e_Validacao_e_desfaz_o_que_a_mesma_escrita_mexeu()
  {
    var m = await ComUmDivergente();
    var z = m.Registro("Z-1");

    var r = await Alterar(m, z, Casar(m.Unica, null, ValoresDaConferencia.EscolhaCatalogo, codigoNovo: "OUTRO"));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(("Z-1", null), (z.CodigoNovo, z.EscolhaDeReceita));
  }

  [Fact]
  public async Task Escolha_de_valor_desconhecido_e_Validacao()
  {
    var m = await ComUmDivergente();

    var r = await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10, "Talvez"));

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
  }

  [Fact]
  public async Task Trocar_o_casamento_zera_a_escolha()
  {
    var m = await ComUmDivergente();
    m.Componente(12, "CAT-2");
    var casado = m.Registro("AB-01");
    await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaImportada));
    Assert.Equal("Importada", casado.EscolhaDeReceita);

    // Outro Componente, sem receita de catalogo: nao diverge, e a escolha de antes era de outro.
    var troca = await Alterar(m, casado, Casar(m.Unica, 12, null));
    Assert.True(troca.Sucesso);
    Assert.Equal(12, casado.ComponenteId);
    Assert.Null(casado.EscolhaDeReceita);
    Assert.Null(casado.ImpressaoDaReceitaDoCatalogo);

    // Voltar para o primeiro com uma escolha nova: vale a do corpo, contra o casamento novo.
    var volta = await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaCatalogo));
    Assert.True(volta.Sucesso);
    Assert.Equal("Catalogo", casado.EscolhaDeReceita);
  }

  [Fact]
  public async Task Trocar_para_novo_zera_a_escolha()
  {
    var m = await ComUmDivergente();
    var casado = m.Registro("AB-01");
    await Alterar(m, casado, Casar(m.Unica, 10, ValoresDaConferencia.EscolhaImportada));

    var r = await Alterar(m, casado, Casar(m.Unica, null));

    Assert.True(r.Sucesso);
    Assert.Null(casado.ComponenteId);
    Assert.Null(casado.EscolhaDeReceita);
    Assert.Null(casado.ImpressaoDaReceitaDoCatalogo);
  }

  // ---------------------------------------------------------------- quantidade do filho

  [Fact]
  public async Task Alterar_filho_grava_a_quantidade_corrigida_e_preserva_a_lida()
  {
    var m = await ComUmCasado();
    var linha = m.Registro("AB-01").Filhos.Single();

    var r = await m.UseCase.AlterarFilho(
        m.Unica.Id, linha.Id, new AlteracaoDeFilhoDto(Versao(m.Unica), 3.25m), CancellationToken.None);

    Assert.True(r.Sucesso);
    Assert.Equal((2m, 3.25m), (linha.QuantidadeLida, linha.Quantidade));
    Assert.Equal(3.25m, r.Valor!.Raiz!.Filhos.Single().Filhos.Single().QuantidadePorPai);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-2")]
  [InlineData("0.00001")]
  [InlineData("1.23456")]
  [InlineData("100000000000000")]
  public async Task Quantidade_do_filho_fora_da_faixa_da_coluna_e_Validacao(string quantidade)
  {
    var m = await ComUmCasado();
    var linha = m.Registro("AB-01").Filhos.Single();

    var r = await m.UseCase.AlterarFilho(
        m.Unica.Id,
        linha.Id,
        new AlteracaoDeFilhoDto(Versao(m.Unica), decimal.Parse(quantidade, System.Globalization.CultureInfo.InvariantCulture)),
        CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(2m, linha.Quantidade);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Linha_inexistente_e_NaoEncontrado()
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.AlterarFilho(m.Unica.Id, 99999, new AlteracaoDeFilhoDto(Versao(m.Unica), 1m), CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- solido pendente

  private static Task<Result<ImportacaoDto>> EnviarSolido(
      Montagem m, ImportacaoDeEstruturaComponente registro, string nome = "peca.stl", byte[]? conteudo = null, string? versao = null) =>
      m.UseCase.EnviarSolidoPendente(
          m.Unica.Id, registro.Id, versao ?? Versao(m.Unica), nome, conteudo ?? StlDeTeste.CuboBinario(), UsuarioId,
          CancellationToken.None);

  [Fact]
  public async Task Solido_pendente_valido_e_gravado_ligado_ao_registro_e_visto_na_projecao()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");

    var r = await EnviarSolido(m, z, @"C:\desenhos\parafuso.stl");

    Assert.True(r.Sucesso);
    var arquivo = m.Importacoes.ArquivosGravados[z.ArquivoSolidoPendenteId!.Value];
    Assert.Equal(("parafuso.stl", UsuarioId), (arquivo.NomeOriginal, arquivo.CriadoPorUsuarioId));
    var situacao = r.Valor!.Componentes.Single(c => c.RegistroId == z.Id);
    Assert.True(situacao.TemSolidoPendente);
    Assert.Equal("parafuso.stl", situacao.NomeDoSolido);
    Assert.Empty(m.Importacoes.ArquivosExcluidos);
  }

  [Theory]
  [InlineData("peca.txt", true)]
  [InlineData("peca.stl", false)]
  public async Task Solido_pendente_invalido_usa_a_mensagem_do_ValidadorDeArquivoStl(string nome, bool comConteudoValido)
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");
    var conteudo = comConteudoValido ? StlDeTeste.CuboBinario() : new byte[] { 1, 2, 3 };

    var r = await EnviarSolido(m, z, nome, conteudo);

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(ValidadorDeArquivoStl.Validar(nome, conteudo), r.Erro);
    Assert.Null(z.ArquivoSolidoPendenteId);
    Assert.Empty(m.Importacoes.ArquivosGravados);
  }

  [Fact]
  public async Task Novo_solido_pendente_substitui_e_apaga_o_anterior()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");
    await EnviarSolido(m, z, "primeiro.stl");
    var primeiro = z.ArquivoSolidoPendenteId!.Value;

    var r = await EnviarSolido(m, z, "segundo.stl");

    Assert.True(r.Sucesso);
    Assert.NotEqual(primeiro, z.ArquivoSolidoPendenteId);
    Assert.Equal([primeiro], m.Importacoes.ArquivosExcluidos);
    Assert.DoesNotContain(primeiro, m.Importacoes.ArquivosGravados.Keys);
    Assert.Equal("segundo.stl", r.Valor!.Componentes.Single(c => c.RegistroId == z.Id).NomeDoSolido);
  }

  [Fact]
  public async Task Solido_pendente_que_perde_a_corrida_no_salvamento_apaga_o_arquivo_que_gravou()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");
    await EnviarSolido(m, z, "primeiro.stl");
    var primeiro = z.ArquivoSolidoPendenteId!.Value;
    m.Importacoes.PerderACorridaNoSalvamento = true;

    var r = await EnviarSolido(m, z, "segundo.stl");

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoDesatualizada, r.Erro);
    // O anterior continua valendo (a escrita nao aconteceu) e o do envio que perdeu nao fica orfao.
    Assert.Equal([primeiro], m.Importacoes.ArquivosGravados.Keys);
    Assert.Equal(primeiro, z.ArquivoSolidoPendenteId);
  }

  [Fact]
  public async Task Solido_pendente_de_registro_inexistente_e_NaoEncontrado_e_nao_grava_arquivo()
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.EnviarSolidoPendente(
        m.Unica.Id, 99999, Versao(m.Unica), "peca.stl", StlDeTeste.CuboBinario(), UsuarioId, CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Empty(m.Importacoes.ArquivosGravados);
  }

  [Fact]
  public async Task Obter_solido_pendente_devolve_o_nome_e_o_conteudo_e_sem_pendente_e_NaoEncontrado()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");
    var conteudo = StlDeTeste.CuboBinario();

    var sem = await m.UseCase.ObterSolidoPendente(m.Unica.Id, z.Id, CancellationToken.None);
    await EnviarSolido(m, z, "peca.stl", conteudo);
    var com = await m.UseCase.ObterSolidoPendente(m.Unica.Id, z.Id, CancellationToken.None);
    var deOutro = await m.UseCase.ObterSolidoPendente(m.Unica.Id, 99999, CancellationToken.None);
    var rascunhoNenhum = await m.UseCase.ObterSolidoPendente(99999, z.Id, CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, sem.TipoDoErro);
    Assert.True(com.Sucesso);
    Assert.Equal(("peca.stl", conteudo), (com.Valor!.NomeOriginal, com.Valor.Conteudo));
    Assert.Equal(TipoDeErro.NaoEncontrado, deOutro.TipoDoErro);
    Assert.Equal(TipoDeErro.NaoEncontrado, rascunhoNenhum.TipoDoErro);
  }

  // ---------------------------------------------------------------- versao

  [Fact]
  public async Task Escrita_com_versao_velha_e_Conflito_ImportacaoDesatualizada()
  {
    var m = await ComUmCasado();
    var velha = Versao(m.Unica);
    await m.UseCase.AlterarPeca(m.Unica.Id, new AlteracaoDaPecaDto(velha, 1m, false), CancellationToken.None);
    var salvamentos = m.Importacoes.Salvamentos;
    var linha = m.Registro("AB-01").Filhos.Single();
    var z = m.Registro("Z-1");
    m.Leitor.Linhas = [L(2, "1", "AB-01", "Peca A", "1")];

    var resultados = new[]
    {
      await m.UseCase.AlterarPeca(m.Unica.Id, new AlteracaoDaPecaDto(velha, 9m, false), CancellationToken.None),
      await m.UseCase.AlterarFilho(m.Unica.Id, linha.Id, new AlteracaoDeFilhoDto(velha, 9m), CancellationToken.None),
      await m.UseCase.AlterarComponente(m.Unica.Id, z.Id, new AlteracaoDeComponenteDto(velha, null, "X", null, null, null), CancellationToken.None),
      await EnviarSolido(m, z, versao: velha),
      await m.UseCase.Reimportar(m.Unica.Id, velha, "outro.csv", [1], CancellationToken.None),
    };

    Assert.All(resultados, r =>
    {
      Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
      Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoDesatualizada, r.Erro);
    });
    Assert.Equal(salvamentos, m.Importacoes.Salvamentos);
    Assert.Equal((1m, 2m, "Z-1"), (m.Unica.QuantidadeDaPeca, linha.Quantidade, z.CodigoNovo));
    Assert.Empty(m.Importacoes.ArquivosGravados);
    Assert.Equal("conjunto.csv", m.Unica.NomeDoArquivo);
  }

  [Fact]
  public async Task Corrida_que_o_salvamento_acusa_tambem_e_ImportacaoDesatualizada()
  {
    var m = await ComUmCasado();
    m.Importacoes.PerderACorridaNoSalvamento = true;

    var r = await m.UseCase.AlterarPeca(m.Unica.Id, new AlteracaoDaPecaDto(Versao(m.Unica), 1m, false), CancellationToken.None);

    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeImportacaoDesatualizada, r.Erro);
  }

  [Theory]
  [InlineData("")]
  [InlineData("nao e base64!")]
  public async Task Versao_que_nao_e_base64_e_Validacao(string versao)
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.AlterarPeca(m.Unica.Id, new AlteracaoDaPecaDto(versao, 1m, false), CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Escrita_em_rascunho_inexistente_e_NaoEncontrado()
  {
    var m = await ComUmCasado();

    var r = await m.UseCase.AlterarPeca(999, new AlteracaoDaPecaDto(Versao(m.Unica), 1m, false), CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
  }

  // ---------------------------------------------------------------- reimport

  private static Task<Result<ImportacaoDto>> Reimportar(Montagem m, string nome, params LinhaCruaDoBom[] linhas)
  {
    m.Leitor.Linhas = linhas;
    return m.UseCase.Reimportar(m.Unica.Id, Versao(m.Unica), nome, [1, 2, 3], CancellationToken.None);
  }

  /// <summary>Dois casados com receita de catalogo que diverge da lida: AB-01 (Z-1 x2) e CD-02 (Y-1 x3).</summary>
  private static async Task<Montagem> ComDoisDivergentes()
  {
    var m = new Montagem();
    m.Componente(10, "AB-01");
    m.Componente(11, "CD-02");
    m.Componente(20, "CAT-A");
    m.Componente(21, "CAT-B");
    m.Estruturas.ReceitaFilhos.Add((10, 20, 1m));
    m.Estruturas.ReceitaFilhos.Add((11, 21, 1m));
    await m.Criar(
        "conjunto.csv",
        L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-1", "Filho Z", "2"),
        L(4, "2", "CD-02", "Peca C", "1"), L(5, "2.1", "Y-1", "Filho Y", "3"));
    return m;
  }

  [Fact]
  public async Task Reimport_preserva_escolha_so_onde_a_receita_lida_nao_mudou()
  {
    var m = await ComDoisDivergentes();
    await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10, ValoresDaConferencia.EscolhaCatalogo));
    await Alterar(m, m.Registro("CD-02"), Casar(m.Unica, 11, ValoresDaConferencia.EscolhaImportada));
    var impressaoDeAb = m.Registro("AB-01").ImpressaoDaReceitaDoCatalogo;
    Assert.NotNull(impressaoDeAb);

    // AB-01 igual; CD-02 muda a quantidade do filho lido.
    var r = await Reimportar(
        m, "conjunto.csv",
        L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-1", "Filho Z", "2"),
        L(4, "2", "CD-02", "Peca C", "1"), L(5, "2.1", "Y-1", "Filho Y", "4"));

    Assert.True(r.Sucesso);
    var ab = m.Registro("AB-01");
    Assert.Equal(("Catalogo", impressaoDeAb), (ab.EscolhaDeReceita, ab.ImpressaoDaReceitaDoCatalogo));
    var cd = m.Registro("CD-02");
    Assert.Equal((null, null), (cd.EscolhaDeReceita, cd.ImpressaoDaReceitaDoCatalogo));
    Assert.Equal(4m, cd.Filhos.Single().Quantidade);
    Assert.Contains(r.Valor!.Bloqueios, b => b.Tipo == ValoresDaConferencia.BloqueioDivergenciaSemEscolha && b.ComponenteId == 11);
  }

  [Fact]
  public async Task Reimport_zera_a_escolha_quando_o_filho_lido_troca_de_codigo_ou_um_filho_entra()
  {
    var m = await ComDoisDivergentes();
    await Alterar(m, m.Registro("AB-01"), Casar(m.Unica, 10, ValoresDaConferencia.EscolhaCatalogo));
    await Alterar(m, m.Registro("CD-02"), Casar(m.Unica, 11, ValoresDaConferencia.EscolhaImportada));

    await Reimportar(
        m, "conjunto.csv",
        L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-2", "Outro filho", "2"),
        L(4, "2", "CD-02", "Peca C", "1"), L(5, "2.1", "Y-1", "Filho Y", "3"), L(6, "2.2", "Y-2", "Mais um", "1"));

    Assert.Null(m.Registro("AB-01").EscolhaDeReceita);
    Assert.Null(m.Registro("CD-02").EscolhaDeReceita);
  }

  [Fact]
  public async Task Reimport_preserva_solido_pendente_casamento_manual_e_dados_do_novo_por_codigo()
  {
    var m = new Montagem();
    m.Componente(10, "CAT-1");
    await m.Criar(
        "conjunto.csv",
        L(2, "1", "A-1", "Peca A", "1"), L(3, "2", "B-1", "Peca B", "2"), L(4, "3", "C-1", "Peca C", "4"));
    var a = m.Registro("A-1");
    var b = m.Registro("B-1");
    await Alterar(m, a, Casar(m.Unica, null, codigoNovo: "A-DEFINITIVO", descricaoNova: "Descricao editada", tipoNovo: "Bruto"));
    await Alterar(m, b, Casar(m.Unica, 10));
    await EnviarSolido(m, a);
    var pendenteDeA = a.ArquivoSolidoPendenteId;
    var idDeA = a.Id;
    var idDeB = b.Id;

    // Ordem trocada, quantidade nova, e um codigo que nao existia.
    var r = await Reimportar(
        m, "conjunto-v2.csv",
        L(2, "1", "B-1", "Peca B", "5"), L(3, "2", "A-1", "Peca A", "7"), L(4, "3", "D-1", "Peca D", "1"));

    Assert.True(r.Sucesso);
    Assert.Equal("conjunto-v2.csv", m.Unica.NomeDoArquivo);
    var depoisA = m.Registro("A-1");
    Assert.Equal(idDeA, depoisA.Id);
    Assert.Equal(("A-DEFINITIVO", "Descricao editada", "Bruto"), (depoisA.CodigoNovo, depoisA.DescricaoNova, depoisA.TipoNovo));
    Assert.Equal(pendenteDeA, depoisA.ArquivoSolidoPendenteId);
    var depoisB = m.Registro("B-1");
    Assert.Equal((idDeB, 10), (depoisB.Id, depoisB.ComponenteId));
    Assert.Equal((null, null, null), (depoisB.CodigoNovo, depoisB.DescricaoNova, depoisB.TipoNovo));
    var d = m.Registro("D-1");
    Assert.Equal(("D-1", "Peca D", "Fabricado"), (d.CodigoNovo, d.DescricaoNova, d.TipoNovo));
    Assert.Equal(0, d.ComponenteId ?? 0);

    // As quantidades e a ordem sao as do arquivo novo; o C-1 que saiu nao esta mais.
    var raiz = m.Unica.Componentes.Single(c => c.Id == m.Unica.RaizId);
    Assert.Equal(
        [("B-1", 5m, 1), ("A-1", 7m, 2), ("D-1", 1m, 3)],
        raiz.Filhos.OrderBy(f => f.Ordem)
            .Select(f => (m.Unica.Componentes.Single(c => c.Id == f.FilhoId).CodigoLido!, f.Quantidade, f.Ordem)));
    Assert.DoesNotContain(m.Unica.Componentes, c => c.CodigoLido == "C-1");
    Assert.Empty(m.Importacoes.ArquivosExcluidos);
  }

  [Fact]
  public async Task Reimport_apaga_pendente_de_codigo_que_saiu_do_arquivo()
  {
    var m = new Montagem();
    await m.Criar("conjunto.csv", L(2, "1", "A-1", "Peca A", "1"), L(3, "1.1", "F-1", "Filho de A", "1"), L(4, "2", "B-1", "Peca B", "1"));
    var a = m.Registro("A-1");
    var b = m.Registro("B-1");
    var f = m.Registro("F-1");
    await EnviarSolido(m, a, "a.stl");
    await EnviarSolido(m, b, "b.stl");
    await EnviarSolido(m, f, "f.stl");
    var pendenteDeA = a.ArquivoSolidoPendenteId!.Value;
    var pendenteDeB = b.ArquivoSolidoPendenteId!.Value;
    var pendenteDeF = f.ArquivoSolidoPendenteId!.Value;
    var idDeB = b.Id;
    var idDeF = f.Id;

    // B-1 e F-1 saem do arquivo.
    var r = await Reimportar(m, "conjunto.csv", L(2, "1", "A-1", "Peca A", "1"));

    Assert.True(r.Sucesso);
    Assert.Equal([pendenteDeB, pendenteDeF], m.Importacoes.ArquivosExcluidos.Order());
    Assert.Equal([pendenteDeA], m.Importacoes.ArquivosGravados.Keys);
    Assert.DoesNotContain(m.Unica.Componentes, c => c.Id == idDeB || c.Id == idDeF);
    Assert.All(m.Unica.Componentes.SelectMany(c => c.Filhos), l => Assert.DoesNotContain(l.FilhoId, new[] { idDeB, idDeF }));
    Assert.Empty(m.Registro("A-1").Filhos);
    Assert.Equal(pendenteDeA, m.Registro("A-1").ArquivoSolidoPendenteId);
  }

  [Fact]
  public async Task Reimport_mantem_a_raiz()
  {
    var m = await ComUmDivergente();
    m.Componente(12, "CAT-RAIZ");
    var raiz = m.Unica.Componentes.Single(c => c.Id == m.Unica.RaizId);
    var idDaRaiz = raiz.Id;
    await Alterar(m, raiz, Casar(m.Unica, 12));
    await EnviarSolido(m, raiz, "raiz.stl");
    var pendente = raiz.ArquivoSolidoPendenteId;
    m.Estruturas.ReceitaFilhos.Add((12, 20, 1m));
    await Alterar(m, raiz, Casar(m.Unica, 12, ValoresDaConferencia.EscolhaCatalogo));
    Assert.Equal("Catalogo", raiz.EscolhaDeReceita);

    // A receita lida da raiz muda (AB-01 x1 vira AB-01 x2): a escolha dela vai, o resto fica.
    var r = await Reimportar(m, "outro nome.csv", L(2, "1", "AB-01", "Peca A", "2"));

    Assert.True(r.Sucesso);
    Assert.Equal(idDaRaiz, m.Unica.RaizId);
    var depois = m.Unica.Componentes.Single(c => c.Id == idDaRaiz);
    Assert.Equal(12, depois.ComponenteId);
    Assert.Equal(pendente, depois.ArquivoSolidoPendenteId);
    Assert.Null(depois.EscolhaDeReceita);
    Assert.Null(depois.ImpressaoDaReceitaDoCatalogo);
    Assert.Equal("outro nome", depois.DescricaoLida);
    Assert.Equal(idDaRaiz, r.Valor!.Componentes.Single(c => c.ComponenteId == 12).RegistroId);
  }

  [Fact]
  public async Task Reimport_mantem_a_escolha_da_raiz_quando_a_receita_lida_dela_nao_mudou()
  {
    var m = await ComUmDivergente();
    m.Componente(12, "CAT-RAIZ");
    var raiz = m.Unica.Componentes.Single(c => c.Id == m.Unica.RaizId);
    m.Estruturas.ReceitaFilhos.Add((12, 20, 1m));
    await Alterar(m, raiz, Casar(m.Unica, 12, ValoresDaConferencia.EscolhaImportada));
    var impressao = raiz.ImpressaoDaReceitaDoCatalogo;

    await Reimportar(m, "conjunto.csv", L(2, "1", "AB-01", "Peca A", "1"), L(3, "1.1", "Z-1", "Filho Z", "2"));

    Assert.Equal(("Importada", impressao), (raiz.EscolhaDeReceita, raiz.ImpressaoDaReceitaDoCatalogo));
  }

  [Fact]
  public async Task Reimport_com_arquivo_invalido_e_BomInvalido_e_deixa_o_rascunho_como_estava()
  {
    var m = await ComUmCasado();
    var z = m.Registro("Z-1");
    await EnviarSolido(m, z);
    var versao = Versao(m.Unica);
    var ids = m.Unica.Componentes.Select(c => c.Id).Order().ToList();
    var salvamentos = m.Importacoes.Salvamentos;
    m.Leitor.Erros = [new ErroDoBom(3, "quantidade invalida."), new ErroDoBom(9, "nivel invalido.")];

    var r = await m.UseCase.Reimportar(m.Unica.Id, versao, "novo.csv", [1], CancellationToken.None);

    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.Equal("Linha 3: quantidade invalida.\nLinha 9: nivel invalido.", r.Detalhe);
    Assert.Equal(versao, Versao(m.Unica));
    Assert.Equal(ids, m.Unica.Componentes.Select(c => c.Id).Order());
    Assert.Equal(salvamentos, m.Importacoes.Salvamentos);
    Assert.Equal("conjunto.csv", m.Unica.NomeDoArquivo);
    Assert.Empty(m.Importacoes.ArquivosExcluidos);
  }

  [Fact]
  public async Task Reimport_recusa_o_que_o_banco_recusaria_como_o_criar()
  {
    var m = await ComUmCasado();

    var r = await Reimportar(m, "conjunto.csv", L(2, "1", new string('X', 51), "Peca", "1"));

    Assert.Equal(ImportacaoDeEstruturaUseCase.ErroDeBomInvalido, r.Erro);
    Assert.StartsWith("Linha 2: ", r.Detalhe);
    Assert.Equal(0, m.Importacoes.Salvamentos);
  }

  [Fact]
  public async Task Reimport_nao_reconhece_linha_sem_codigo_e_cria_o_registro_de_novo()
  {
    var m = new Montagem();
    await m.Criar("conjunto.csv", L(2, "1", null, "Peca sem codigo", "1"));
    var antes = m.Unica.Componentes.Single(c => c.Id != m.Unica.RaizId);
    await EnviarSolido(m, antes);
    var pendente = antes.ArquivoSolidoPendenteId!.Value;

    var r = await Reimportar(m, "conjunto.csv", L(2, "1", null, "Peca sem codigo", "1"));

    Assert.True(r.Sucesso);
    var depois = m.Unica.Componentes.Single(c => c.Id != m.Unica.RaizId);
    Assert.NotEqual(antes.Id, depois.Id);
    Assert.Null(depois.ArquivoSolidoPendenteId);
    Assert.Equal([pendente], m.Importacoes.ArquivosExcluidos);
  }

  [Fact]
  public async Task Reimport_nao_casa_um_codigo_novo_com_componente_que_outro_registro_ja_tem()
  {
    var m = new Montagem();
    m.Componente(10, "CAT-1");
    await m.Criar("conjunto.csv", L(2, "1", "A-1", "Peca A", "1"));
    await Alterar(m, m.Registro("A-1"), Casar(m.Unica, 10));

    // O arquivo novo traz CAT-1, que casaria sozinho com o Componente 10: ele ja e do A-1.
    var r = await Reimportar(m, "conjunto.csv", L(2, "1", "A-1", "Peca A", "1"), L(3, "2", "CAT-1", "Peca do catalogo", "1"));

    Assert.True(r.Sucesso);
    Assert.Equal(10, m.Registro("A-1").ComponenteId);
    var cat = m.Registro("CAT-1");
    Assert.Equal((null, "CAT-1"), (cat.ComponenteId, cat.CodigoNovo));
  }

  [Fact]
  public async Task Reimport_devolve_o_estado_calculado_e_troca_a_versao()
  {
    var m = await ComUmCasado();
    var antes = Versao(m.Unica);

    var r = await Reimportar(m, "conjunto.csv", L(2, "1", "AB-01", "Peca A", "1"));

    Assert.True(r.Sucesso);
    Assert.NotEqual(antes, Convert.ToBase64String(r.Valor!.Versao));
    Assert.Equal(["AB-01"], r.Valor.Raiz!.Filhos.Select(f => f.Codigo));
    Assert.Equal(2, r.Valor.Componentes.Count);
  }
}
