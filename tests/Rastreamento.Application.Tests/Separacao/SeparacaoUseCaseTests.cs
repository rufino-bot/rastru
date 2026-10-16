using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Separacao;
using Rastreamento.Application.Tests.Cadastros;
using Rastreamento.Application.Tests.Common;
using Rastreamento.Application.Tests.Estrutura;
using Rastreamento.Application.Tests.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Separacao;

public class SeparacaoUseCaseTests
{
  private const int No = 42;
  private const int AgrupamentoId = 3;
  private const int PedidoId = 5;
  private const int Parafuso = 7;   // almoxarifado
  private const int Chapa = 8;      // fica no Corte
  private const int Arruela = 9;    // almoxarifado
  private const int Cola = 10;      // almoxarifado, nunca no planejado
  private const int Separador = 15;
  private const int OutroSeparador = 16;
  private const int Pcp = 17;

  private sealed class Cenario
  {
    public FakeEstruturaRepo Estruturas { get; } = new();
    public FakeSeparacaoRepo Separacao { get; } = new();
    public FakeExecucaoRepo Execucao { get; }

    public Cenario()
    {
      Execucao = new FakeExecucaoRepo(Estruturas);
      Estruturas.Itens.Add(new EstruturaItem
      {
        Id = No, AgrupamentoId = AgrupamentoId, Descricao = "Quadro", NivelHierarquico = "Peca", Quantidade = 1m,
      });
      Execucao.Agrupamentos[AgrupamentoId] = ("AG-01", PedidoId, "PED-77");
      Execucao.StatusDoPedido[PedidoId] = "EmProducao";
      Execucao.Usuarios[Separador] = "Separador Um";
      Execucao.Usuarios[OutroSeparador] = "Separador Dois";
      Execucao.Usuarios[Pcp] = "Planejamento";

      Separacao.Nos.Add(No);
      Separacao.Materiais[Parafuso] = new MaterialDaSeparacao(Parafuso, "PAR-M8", "Parafuso M8", "UN", true, null, null);
      Separacao.Materiais[Chapa] = new MaterialDaSeparacao(Chapa, "CH-3", "Chapa 3 mm", "KG", true, 1, "Corte a Laser");
      Separacao.Materiais[Arruela] = new MaterialDaSeparacao(Arruela, "ARR-8", "Arruela 8", "UN", true, null, null);
      Separacao.Materiais[Cola] = new MaterialDaSeparacao(Cola, "CO-1", "Cola de contato", "L", true, null, null);
    }

    public SeparacaoUseCase Uso() => new(Separacao, Execucao);

    public ConsultaDeSeparacaoUseCase Consulta() =>
        new(Separacao, Execucao, Estruturas, new FakeReceitaPadraoRepo(),
            new RelogioFixo(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));

    public void Planejar(int materialId, decimal quantidade) =>
        Separacao.Planejado.Add(new EstruturaMaterial { EstruturaItemId = No, MaterialId = materialId, Quantidade = quantidade });

    /// <summary>Uma separacao ja gravada, de `autor`, direto no fake.</summary>
    public async Task<MaterialSeparacao> SepararAntesAsync(int materialId, decimal quantidade, int autor, DateTime? em = null)
    {
      var linha = new MaterialSeparacao
      {
        EstruturaItemId = No, MaterialId = materialId, Tipo = TiposDeSeparacao.Separacao, Quantidade = quantidade,
        DataHora = em ?? DateTime.UtcNow, UsuarioId = autor,
      };
      await Separacao.GravarAsync([linha], CancellationToken.None);
      return linha;
    }
  }

  private static SeparacaoNovaDto Corpo(params (int MaterialId, decimal Quantidade)[] linhas) =>
      new(linhas.Select(l => new LinhaDeMaterialDto(l.MaterialId, l.Quantidade)).ToList());

  // -------------------------------------------------------------------------
  // Separar
  // -------------------------------------------------------------------------

  [Fact]
  public async Task Separar_grava_uma_linha_por_material_com_autor_e_devolve_o_resumo()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    c.Planejar(Arruela, 4m);
    c.Planejar(Chapa, 2m);   // no planejado, mas nao tocado: nao volta no resumo

    var r = await c.Uso().Separar(No, Corpo((Parafuso, 6m), (Arruela, 4m)), Separador, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(1, c.Separacao.Gravacoes);   // um SaveChanges so, tudo ou nada
    Assert.Equal(2, r.Valor!.Separacoes.Count);
    Assert.All(r.Valor.Separacoes, s =>
    {
      Assert.Equal(TiposDeSeparacao.Separacao, s.Tipo);
      Assert.Equal(Separador, s.UsuarioId);
      Assert.Equal("Separador Um", s.UsuarioNome);
      Assert.Null(s.EstornoDeId);
      Assert.True(s.Estornavel);   // quem separou acabou de separar: pode estornar
    });
    var doParafuso = r.Valor.Separacoes.Single(s => s.MaterialId == Parafuso);
    Assert.Equal(6m, doParafuso.Quantidade);
    Assert.Equal("PAR-M8", doParafuso.MaterialCodigo);
    Assert.Equal("Parafuso M8", doParafuso.MaterialDescricao);
    Assert.Equal("UN", doParafuso.UnidadeMedida);
    Assert.Equal(DateTimeKind.Utc, doParafuso.DataHora.Kind);

    // O livro tem as duas linhas com o autor, no no certo.
    Assert.Equal(2, c.Separacao.Linhas.Count);
    Assert.All(c.Separacao.Linhas, l =>
    {
      Assert.Equal(No, l.EstruturaItemId);
      Assert.Equal(Separador, l.UsuarioId);
      Assert.Equal(TiposDeSeparacao.Separacao, l.Tipo);
    });

    // So os materiais tocados, com a conta nova.
    Assert.Equal(new HashSet<int> { Parafuso, Arruela }, r.Valor.Materiais.Select(m => m.MaterialId).ToHashSet());
    var resumoDoParafuso = r.Valor.Materiais.Single(m => m.MaterialId == Parafuso);
    Assert.Equal(10m, resumoDoParafuso.Planejado);
    Assert.Equal(6m, resumoDoParafuso.Separado);
    Assert.Equal(4m, resumoDoParafuso.Pendencia);
    Assert.False(resumoDoParafuso.AcimaDoPlanejado);
    Assert.Equal(0m, r.Valor.Materiais.Single(m => m.MaterialId == Arruela).Pendencia);
  }

  [Fact]
  public async Task Separar_acima_do_planejado_aceita_e_marca()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);

    var r = await c.Uso().Separar(No, Corpo((Parafuso, 12m)), Separador, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    var item = Assert.Single(r.Valor!.Materiais);
    Assert.True(item.AcimaDoPlanejado);
    Assert.Equal(12m, item.Separado);
    Assert.Equal(0m, item.Pendencia);
  }

  [Fact]
  public async Task Separar_material_fora_do_planejado_e_409_e_nao_grava_nada()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);

    var r = await c.Uso().Separar(No, Corpo((Parafuso, 2m), (Cola, 1m)), Separador, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.MaterialForaDoPlanejado, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("CO-1", r.Detalhe);
    Assert.Contains("Cola de contato", r.Detalhe);
    Assert.Contains("PCP", r.Detalhe);
    Assert.Contains("do nó Quadro", r.Detalhe);   // spec da Fase 4, secao 5.4: a frase nomeia o material e o no
    Assert.Equal(0, c.Separacao.Gravacoes);
    Assert.Empty(c.Separacao.Linhas);
  }

  [Fact]
  public async Task Mensagem_nomeia_o_item_pelo_id_quando_o_no_nao_tem_descricao_propria()
  {
    var c = new Cenario();
    c.Estruturas.Itens.Single(i => i.Id == No).Descricao = null;
    c.Planejar(Parafuso, 10m);

    var r = await c.Uso().Separar(No, Corpo((Cola, 1m)), Separador, CancellationToken.None);

    Assert.Equal(CodigosDaSeparacao.MaterialForaDoPlanejado, r.Erro);
    Assert.Contains("CO-1", r.Detalhe);
    Assert.Contains($"do item {No}", r.Detalhe);
  }

  [Fact]
  public async Task Separar_material_que_fica_num_Setor_e_esta_no_planejado_e_aceito()
  {
    var c = new Cenario();
    c.Planejar(Chapa, 2.5m);

    var r = await c.Uso().Separar(No, Corpo((Chapa, 2.5m)), Separador, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    var item = Assert.Single(r.Valor!.Materiais);
    Assert.Equal(Chapa, item.MaterialId);
    Assert.Equal(2.5m, item.Separado);
    Assert.Equal(0m, item.Pendencia);   // D6: o que fica num Setor nunca tem pendencia
    Assert.Single(c.Separacao.Linhas);
  }

  [Fact]
  public async Task Separar_com_lista_vazia_e_400()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);

    var vazia = await c.Uso().Separar(No, Corpo(), Separador, CancellationToken.None);
    var nula = await c.Uso().Separar(No, new SeparacaoNovaDto(null), Separador, CancellationToken.None);

    foreach (var r in new[] { vazia, nula })
    {
      Assert.False(r.Sucesso);
      Assert.Equal(CodigosDaSeparacao.SeparacaoVazia, r.Erro);
      Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    }
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("0.00001")]
  public async Task Separar_com_quantidade_invalida_e_400(string quantidade)
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);

    var r = await c.Uso().Separar(No, Corpo((Parafuso, 1m), (Parafuso, decimal.Parse(quantidade, System.Globalization.CultureInfo.InvariantCulture))),
        Separador, CancellationToken.None);

    // A quantidade vem antes do repetido (decisao P8 do plano de backend da Fase 4).
    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.QuantidadeInvalida, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Separar_com_material_repetido_e_400()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);

    // O repetido vem antes do "fora do planejado": a Cola nao esta no plano e nao e ela que responde.
    var r = await c.Uso().Separar(No, Corpo((Parafuso, 1m), (Parafuso, 2m), (Cola, 1m)), Separador, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.MaterialRepetido, r.Erro);
    Assert.Equal(TipoDeErro.Validacao, r.TipoDoErro);
    Assert.Contains("PAR-M8", r.Detalhe);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Separar_no_inexistente_e_404()
  {
    var c = new Cenario();

    // O 404 vem antes do corpo: a lista vazia num no inexistente e 404, nao 400.
    var r = await c.Uso().Separar(999, Corpo(), Separador, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(TipoDeErro.NaoEncontrado, r.TipoDoErro);
    Assert.Equal(0, c.Separacao.Gravacoes);
  }

  [Fact]
  public async Task Separar_em_Pedido_pausado_e_aceito()
  {
    // Review Focus 5: a pausa recusa so o inicio (regra 31); separar continua.
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    c.Execucao.Pausas.Add(new PedidoPausa
    {
      Id = 1, PedidoId = PedidoId, PausadoEm = DateTime.UtcNow, PausadoPorUsuarioId = Pcp, Motivo = "Cliente pediu",
    });
    Assert.True((await c.Execucao.ObterPedidoDoNoAsync(No, CancellationToken.None))!.Pausado);   // premissa

    var r = await c.Uso().Separar(No, Corpo((Parafuso, 3m)), Separador, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Single(c.Separacao.Linhas);
  }

  // -------------------------------------------------------------------------
  // Estornar
  // -------------------------------------------------------------------------

  [Fact]
  public async Task Estornar_pelo_autor_grava_o_inverso_apontando_a_original()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);

    var r = await c.Uso().Estornar(original.Id, Separador, podeEstornarDeOutros: false, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(TiposDeSeparacao.Estorno, r.Valor!.Tipo);
    Assert.Equal(6m, r.Valor.Quantidade);
    Assert.Equal(Parafuso, r.Valor.MaterialId);
    Assert.Equal("PAR-M8", r.Valor.MaterialCodigo);
    Assert.Equal(original.Id, r.Valor.EstornoDeId);
    Assert.Equal(Separador, r.Valor.UsuarioId);
    Assert.Equal("Separador Um", r.Valor.UsuarioNome);
    Assert.False(r.Valor.Estornavel);   // estorno nao se estorna

    var gravado = c.Separacao.Linhas.Single(l => l.Id == r.Valor.Id);
    Assert.Equal(TiposDeSeparacao.Estorno, gravado.Tipo);
    Assert.Equal(original.EstruturaItemId, gravado.EstruturaItemId);
    Assert.Equal(original.MaterialId, gravado.MaterialId);
    Assert.Equal(original.Quantidade, gravado.Quantidade);
    Assert.Equal(original.Id, gravado.EstornoDeId);
    Assert.Equal(Separador, gravado.UsuarioId);
    // A original fica intocada: o livro e so de inclusao.
    Assert.Equal(TiposDeSeparacao.Separacao, original.Tipo);
    Assert.Equal(6m, original.Quantidade);
    Assert.Equal(0m, CalculoDaSeparacao.Separado(c.Separacao.Linhas));
  }

  [Fact]
  public async Task Estornar_pelo_PCP_registro_de_outro_e_aceito()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);

    var r = await c.Uso().Estornar(original.Id, Pcp, podeEstornarDeOutros: true, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(Pcp, r.Valor!.UsuarioId);   // o autor do estorno e quem estornou
    Assert.Equal("Planejamento", r.Valor.UsuarioNome);
    Assert.Equal(original.Id, r.Valor.EstornoDeId);
  }

  [Fact]
  public async Task Estornar_registro_de_outro_sem_ser_PCP_e_403()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    var gravacoesAntes = c.Separacao.Gravacoes;

    var r = await c.Uso().Estornar(original.Id, OutroSeparador, podeEstornarDeOutros: false, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.Proibido, r.Erro);
    Assert.Equal(TipoDeErro.Proibido, r.TipoDoErro);
    Assert.Equal(gravacoesAntes, c.Separacao.Gravacoes);
    Assert.Single(c.Separacao.Linhas);
  }

  [Fact]
  public async Task Estornar_um_estorno_e_409()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    var estorno = await c.Uso().Estornar(original.Id, Separador, false, CancellationToken.None);

    var r = await c.Uso().Estornar(estorno.Valor!.Id, Separador, false, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.EstornoDeEstorno, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("PAR-M8 (Parafuso M8)", r.Detalhe);
    Assert.Contains("do nó Quadro", r.Detalhe);
    Assert.Equal(2, c.Separacao.Linhas.Count);
  }

  [Fact]
  public async Task Estornar_duas_vezes_e_409()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    Assert.True((await c.Uso().Estornar(original.Id, Separador, false, CancellationToken.None)).Sucesso);

    // O PCP tambem nao estorna de novo: o 403 vem antes, mas aqui ele passa e cai no 409.
    var r = await c.Uso().Estornar(original.Id, Pcp, true, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.JaEstornada, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("PAR-M8 (Parafuso M8)", r.Detalhe);
    Assert.Contains("do nó Quadro", r.Detalhe);
    Assert.Equal(2, c.Separacao.Linhas.Count);
  }

  [Fact]
  public async Task Estornar_quando_outro_estorno_chega_antes_e_409()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    // A leitura de "ja estornada" passa; o indice unico recusa na gravacao.
    c.Separacao.EstornoConcorrenteNaProximaGravacao = true;

    var r = await c.Uso().Estornar(original.Id, Separador, false, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaSeparacao.JaEstornada, r.Erro);
    Assert.Equal(TipoDeErro.Conflito, r.TipoDoErro);
    Assert.Contains("PAR-M8 (Parafuso M8)", r.Detalhe);
    Assert.Contains("do nó Quadro", r.Detalhe);
    Assert.Single(c.Separacao.Linhas);
  }

  [Fact]
  public async Task Estornar_ja_estornada_por_quem_nao_e_autor_nem_PCP_e_403_e_nao_409()
  {
    // A ordem da decisao P8 do plano de backend da Fase 4: o 403 vem antes do "ja estornada" — quem nao
    // pode estornar nao fica sabendo do estado da linha.
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    Assert.True((await c.Uso().Estornar(original.Id, Separador, false, CancellationToken.None)).Sucesso);

    var r = await c.Uso().Estornar(original.Id, OutroSeparador, podeEstornarDeOutros: false, CancellationToken.None);

    Assert.False(r.Sucesso);
    Assert.Equal(CodigosDaExecucao.Proibido, r.Erro);
    Assert.Equal(TipoDeErro.Proibido, r.TipoDoErro);
    Assert.Equal(2, c.Separacao.Linhas.Count);
  }

  [Fact]
  public async Task Estornar_separacao_de_material_que_saiu_do_plano_e_aceito()
  {
    // Review Focus 2: o PCP tirou o material do planejado depois da separacao.
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    var original = await c.SepararAntesAsync(Parafuso, 6m, Separador);
    await c.Separacao.SubstituirPlanejadoAsync(No, [], CancellationToken.None);

    var r = await c.Uso().Estornar(original.Id, Separador, false, CancellationToken.None);

    Assert.True(r.Sucesso, r.Detalhe);
    Assert.Equal(original.Id, r.Valor!.EstornoDeId);
    Assert.Equal(2, c.Separacao.Linhas.Count);
  }

  // -------------------------------------------------------------------------
  // Historico
  // -------------------------------------------------------------------------

  [Fact]
  public async Task Historico_mais_recente_primeiro_com_nome_do_autor_e_estornavel()
  {
    var c = new Cenario();
    c.Planejar(Parafuso, 10m);
    c.Planejar(Arruela, 4m);
    var t0 = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    var antiga = await c.SepararAntesAsync(Parafuso, 6m, Separador, t0);
    var aberta = await c.SepararAntesAsync(Arruela, 4m, Separador, t0.AddMinutes(1));
    var estorno = new MaterialSeparacao
    {
      EstruturaItemId = No, MaterialId = Parafuso, Tipo = TiposDeSeparacao.Estorno, Quantidade = 6m,
      EstornoDeId = antiga.Id, DataHora = t0.AddMinutes(2), UsuarioId = Separador,
    };
    Assert.True(await c.Separacao.GravarEstornoAsync(estorno, CancellationToken.None));

    var doAutor = await c.Consulta().Historico(No, new QuemLe(Separador, false), CancellationToken.None);

    Assert.True(doAutor.Sucesso, doAutor.Detalhe);
    var linhas = doAutor.Valor!;
    Assert.Equal([estorno.Id, aberta.Id, antiga.Id], linhas.Select(l => l.Id).ToArray());
    Assert.All(linhas, l => Assert.Equal("Separador Um", l.UsuarioNome));
    Assert.Equal(TiposDeSeparacao.Estorno, linhas[0].Tipo);
    Assert.Equal(antiga.Id, linhas[0].EstornoDeId);
    Assert.Equal("ARR-8", linhas[1].MaterialCodigo);
    Assert.Equal("Arruela 8", linhas[1].MaterialDescricao);
    Assert.Equal(4m, linhas[1].Quantidade);
    // Estornavel: so a separacao do proprio leitor ainda nao estornada.
    Assert.False(linhas[0].Estornavel);   // o estorno
    Assert.True(linhas[1].Estornavel);    // a aberta
    Assert.False(linhas[2].Estornavel);   // a ja estornada

    var deOutro = await c.Consulta().Historico(No, new QuemLe(OutroSeparador, false), CancellationToken.None);
    Assert.All(deOutro.Valor!, l => Assert.False(l.Estornavel));

    var doPcp = await c.Consulta().Historico(No, new QuemLe(Pcp, true), CancellationToken.None);
    Assert.Equal([false, true, false], doPcp.Valor!.Select(l => l.Estornavel).ToArray());
  }

  [Fact]
  public async Task Historico_de_no_inexistente_e_404_e_de_no_sem_linha_e_vazio()
  {
    var c = new Cenario();

    var semNo = await c.Consulta().Historico(999, new QuemLe(Separador, false), CancellationToken.None);
    var vazio = await c.Consulta().Historico(No, new QuemLe(Separador, false), CancellationToken.None);

    Assert.Equal(TipoDeErro.NaoEncontrado, semNo.TipoDoErro);
    Assert.True(vazio.Sucesso);
    Assert.Empty(vazio.Valor!);
  }
}
