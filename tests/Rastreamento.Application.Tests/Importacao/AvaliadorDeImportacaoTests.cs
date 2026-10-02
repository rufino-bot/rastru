using System.Security.Cryptography;
using System.Text;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Importacao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Importacao;

public class AvaliadorDeImportacaoTests
{
  // ---------------------------------------------------------------- montagem do cenario em memoria

  /// <summary>O rascunho e o catalogo de um teste, montados em memoria como o banco os entregaria.</summary>
  private sealed class Cenario
  {
    private int _proximoRegistro = 1;
    private int _proximoFilho = 100;
    private readonly Dictionary<int, Componente> _componentes = [];
    private readonly List<(int Pai, int Filho, decimal Qtd)> _receita = [];
    private readonly Dictionary<int, MetadadoDeSolido> _solidos = [];

    public ImportacaoDeEstrutura Rascunho { get; } = new() { Id = 1, QuantidadeDaPeca = 1m };

    public Cenario()
    {
      var raiz = Novo(null, "montagem", "MONT-1", "Montagem principal", "Montagem", pendente: 900);
      Rascunho.RaizId = raiz.Id;
      Solido(900, "montagem.stl", 10);
    }

    public ImportacaoDeEstruturaComponente Raiz => Registro(Rascunho.RaizId!.Value);

    public ImportacaoDeEstruturaComponente Registro(int id) => Rascunho.Componentes.Single(c => c.Id == id);

    public ImportacaoDeEstruturaComponente Novo(
        string? codigoLido, string descricaoLida, string? codigoNovo, string? descricaoNova, string? tipoNovo,
        int? pendente = null)
    {
      var registro = new ImportacaoDeEstruturaComponente
      {
        Id = _proximoRegistro++,
        ImportacaoId = Rascunho.Id,
        CodigoLido = codigoLido,
        DescricaoLida = descricaoLida,
        CodigoNovo = codigoNovo,
        DescricaoNova = descricaoNova,
        TipoNovo = tipoNovo,
        ArquivoSolidoPendenteId = pendente,
      };
      Rascunho.Componentes.Add(registro);
      return registro;
    }

    public ImportacaoDeEstruturaComponente Casado(
        string codigoLido, string descricaoLida, int componenteId, string? escolha = null, int? pendente = null)
    {
      var registro = new ImportacaoDeEstruturaComponente
      {
        Id = _proximoRegistro++,
        ImportacaoId = Rascunho.Id,
        CodigoLido = codigoLido,
        DescricaoLida = descricaoLida,
        ComponenteId = componenteId,
        EscolhaDeReceita = escolha,
        ArquivoSolidoPendenteId = pendente,
      };
      Rascunho.Componentes.Add(registro);
      return registro;
    }

    /// <summary>Como o banco entrega: so o Id do filho, sem a navegacao.</summary>
    public ImportacaoDeEstruturaFilho Filho(ImportacaoDeEstruturaComponente pai, ImportacaoDeEstruturaComponente filho, decimal qtd)
    {
      var linha = new ImportacaoDeEstruturaFilho
      {
        Id = _proximoFilho++,
        PaiId = pai.Id,
        FilhoId = filho.Id,
        Ordem = pai.Filhos.Count + 1,
        QuantidadeLida = qtd,
        Quantidade = qtd,
      };
      pai.Filhos.Add(linha);
      return linha;
    }

    public Componente Componente(int id, string codigo, string tipo = "Fabricado", int? solido = null, bool ativo = true)
    {
      var c = new Componente
      {
        Id = id, Codigo = codigo, Descricao = $"Desc {codigo}", Tipo = tipo, Ativo = ativo, ArquivoSolidoId = solido,
      };
      _componentes[id] = c;
      if (solido is int s && !_solidos.ContainsKey(s))
        _solidos[s] = new MetadadoDeSolido($"{codigo}.stl", 1000 + id);
      return c;
    }

    public void ReceitaDoCatalogo(int pai, int filho, decimal qtd) => _receita.Add((pai, filho, qtd));

    public void Solido(int arquivoId, string nome, int tamanho) => _solidos[arquivoId] = new MetadadoDeSolido(nome, tamanho);

    public HashSet<string> CodigosExistentes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ReceitaDoCatalogo Receita() => new(
        _receita.ToLookup(r => r.Pai, r => (r.Filho, r.Qtd)),
        Array.Empty<(int, int, decimal)>().ToLookup(m => m.Item1, m => (m.Item2, m.Item3)),
        Array.Empty<(int, int, int)>().ToLookup(r => r.Item1, r => (r.Item2, r.Item3)));

    public AvaliacaoDeImportacao Avaliar()
    {
      var receita = Receita();
      // O caso de uso so carrega o que `IdsAlcancaveis` pede: o teste faz o mesmo, para que um Id
      // esquecido ali quebre aqui em vez de passar por o catalogo do teste ter tudo.
      var ids = AvaliadorDeImportacao.IdsAlcancaveis(Rascunho, receita);
      var componentes = _componentes.Where(c => ids.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value);
      foreach (var c in _componentes.Values)
        CodigosExistentes.Add(c.Codigo);
      return AvaliadorDeImportacao.Avaliar(
          Rascunho, new CatalogoParaAvaliacao(receita, componentes, CodigosExistentes, _solidos));
    }
  }

  private static SituacaoDoComponenteDto Situacao(AvaliacaoDeImportacao a, ImportacaoDeEstruturaComponente r) =>
      a.Componentes.Single(s => s.RegistroId == r.Id);

  private static (int?, int?, int?, string, string, decimal?, string, string) Resumo(NoDaImportacaoDto no) =>
      (no.RegistroId, no.ComponenteId, no.FilhoId, no.Codigo, no.Descricao, no.QuantidadePorPai, no.Origem,
          string.Join(",", no.Pendencias));

  // ---------------------------------------------------------------- P1: Id provisorio negativo

  [Fact]
  public void Componente_novo_entra_com_id_negativo_e_pendencia_Novo()
  {
    var c = new Cenario();
    c.Rascunho.QuantidadeDaPeca = 2m;
    var filho = c.Novo("N-2", "Novo dois", "N-2", "Novo dois", "Fabricado", pendente: 901);
    c.Solido(901, "n2.stl", 20);
    var linha = c.Filho(c.Raiz, filho, 3m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    Assert.NotNull(a.Plano);
    Assert.Equal(-c.Raiz.Id, a.Plano.ComponenteId);
    Assert.Equal(2m, a.Plano.Quantidade);
    var noPlanejado = Assert.Single(a.Plano.Filhos);
    Assert.Equal(-filho.Id, noPlanejado.ComponenteId);
    Assert.Equal(6m, noPlanejado.Quantidade);
    Assert.Equal(new[] { (-filho.Id, 3m) }, a.Sobreposicao.Filhos[-c.Raiz.Id].ToArray());

    Assert.NotNull(a.Raiz);
    Assert.Equal(c.Raiz.Id, a.Raiz.RegistroId);
    Assert.Null(a.Raiz.ComponenteId);
    Assert.Null(a.Raiz.FilhoId);
    Assert.Null(a.Raiz.QuantidadePorPai);
    Assert.Equal("MONT-1", a.Raiz.Codigo);
    Assert.Equal("Montagem principal", a.Raiz.Descricao);
    Assert.Equal("Bom", a.Raiz.Origem);
    Assert.Equal(new[] { "Novo" }, a.Raiz.Pendencias);

    var no = Assert.Single(a.Raiz.Filhos);
    Assert.Equal(filho.Id, no.RegistroId);
    Assert.Null(no.ComponenteId);
    Assert.Equal(linha.Id, no.FilhoId);
    Assert.Equal(3m, no.QuantidadePorPai);
    Assert.Equal("N-2", no.Codigo);
    Assert.Equal("Bom", no.Origem);
    Assert.Equal(new[] { "Novo" }, no.Pendencias);

    var s = Situacao(a, filho);
    Assert.Null(s.ComponenteId);
    Assert.Null(s.CodigoDoCatalogo);
    Assert.Null(s.Tipo);
    Assert.Null(s.Ativo);
    Assert.False(s.TemSolido);
    Assert.True(s.TemSolidoPendente);
    Assert.Equal("n2.stl", s.NomeDoSolido);
    Assert.Equal(20, s.TamanhoDoSolidoEmBytes);
    Assert.Equal("N-2", s.CodigoNovo);
    Assert.Equal("Fabricado", s.TipoNovo);
    Assert.False(s.Divergente);
    Assert.Empty(s.Comparativo);
    Assert.Null(s.EfeitoDeManterCatalogo);
    Assert.True(s.NaArvoreFinal);
  }

  // ---------------------------------------------------------------- P2: receita efetiva e divergencia

  [Fact]
  public void Casado_sem_receita_nao_diverge_e_usa_a_lida()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    var a10 = c.Casado("AB-10", "Lida 10", 10);
    var p11 = c.Casado("P-11", "Lida 11", 11);
    c.Filho(c.Raiz, a10, 1m);
    var linha = c.Filho(a10, p11, 4m);

    var a = c.Avaliar();

    var s = Situacao(a, a10);
    Assert.False(s.Divergente);
    Assert.Empty(s.Comparativo);
    Assert.Null(s.EfeitoDeManterCatalogo);
    Assert.Equal("AB-10", s.CodigoDoCatalogo);
    Assert.Equal("Desc AB-10", s.DescricaoDoCatalogo);
    Assert.Equal("Montagem", s.Tipo);
    Assert.True(s.Ativo);
    Assert.True(s.TemSolido);
    Assert.Equal("AB-10.stl", s.NomeDoSolido);
    Assert.Equal(1010, s.TamanhoDoSolidoEmBytes);
    Assert.Equal(new[] { (11, 4m) }, a.Sobreposicao.Filhos[10].ToArray());

    var no10 = Assert.Single(a.Raiz!.Filhos);
    Assert.Equal(10, no10.ComponenteId);
    Assert.Equal("AB-10", no10.Codigo);
    Assert.Equal("Desc AB-10", no10.Descricao);
    Assert.Empty(no10.Pendencias);
    var no11 = Assert.Single(no10.Filhos);
    Assert.Equal(p11.Id, no11.RegistroId);
    Assert.Equal(linha.Id, no11.FilhoId);
    Assert.Equal("Bom", no11.Origem);
    Assert.Empty(a.Bloqueios);
  }

  [Fact]
  public void Casado_com_receita_igual_nao_diverge()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    c.ReceitaDoCatalogo(10, 11, 4.0000m);
    var a10 = c.Casado("AB-10", "Lida 10", 10);
    var p11 = c.Casado("P-11", "Lida 11", 11);
    c.Filho(c.Raiz, a10, 1m);
    var linha = c.Filho(a10, p11, 4m);

    var a = c.Avaliar();

    Assert.False(Situacao(a, a10).Divergente);
    Assert.Empty(Situacao(a, a10).Comparativo);
    var no11 = Assert.Single(Assert.Single(a.Raiz!.Filhos).Filhos);
    Assert.Equal(linha.Id, no11.FilhoId);
    Assert.Empty(a.Bloqueios);
  }

  [Fact]
  public void Casado_com_receita_diferente_diverge_e_traz_comparativo_de_um_nivel()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    c.Componente(12, "P-12", solido: 112);
    c.Componente(13, "P-13", solido: 113);
    c.Componente(15, "A-15", solido: 115);
    c.ReceitaDoCatalogo(10, 13, 5m);
    c.ReceitaDoCatalogo(10, 12, 1m);
    c.ReceitaDoCatalogo(10, 11, 2m);
    c.ReceitaDoCatalogo(10, 15, 1m);
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Importada");
    var p11 = c.Casado("P-11", "Lida 11", 11);
    var p12 = c.Casado("P-12", "Lida 12", 12);
    var n4 = c.Novo("N-4", "Lida N4", "N-4", "Nova N4", "Fabricado", pendente: 904);
    c.Filho(c.Raiz, a10, 1m);
    c.Filho(a10, p11, 2m);
    c.Filho(a10, p12, 3m);
    c.Filho(a10, n4, 1m);

    var a = c.Avaliar();

    var s = Situacao(a, a10);
    Assert.True(s.Divergente);
    Assert.Equal("Importada", s.EscolhaDeReceita);
    Assert.Equal(
        new[]
        {
          new LinhaDoComparativoDto("P-11", "Desc P-11", 2m, 2m, "Igual"),
          new LinhaDoComparativoDto("P-12", "Desc P-12", 1m, 3m, "QuantidadeMuda"),
          new LinhaDoComparativoDto("N-4", "Nova N4", null, 1m, "Entra"),
          new LinhaDoComparativoDto("A-15", "Desc A-15", 1m, null, "Sai"),
          new LinhaDoComparativoDto("P-13", "Desc P-13", 5m, null, "Sai"),
        },
        s.Comparativo);
    Assert.Equal(new[] { "Divergente" }, Assert.Single(a.Raiz!.Filhos).Pendencias);
    Assert.Empty(a.Bloqueios);
  }

  [Fact]
  public void Casado_folha_no_BOM_com_receita_no_catalogo_diverge()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    c.ReceitaDoCatalogo(10, 11, 2m);
    var a10 = c.Casado("AB-10", "Lida 10", 10);
    c.Filho(c.Raiz, a10, 1m);

    var a = c.Avaliar();

    var s = Situacao(a, a10);
    Assert.True(s.Divergente);
    Assert.Equal(new[] { new LinhaDoComparativoDto("P-11", "Desc P-11", 2m, null, "Sai") }, s.Comparativo);
    Assert.Equal(new EfeitoDto(0, 1), s.EfeitoDeManterCatalogo);
    Assert.Empty(Assert.Single(a.Raiz!.Filhos).Filhos);
  }

  [Fact]
  public void Divergencia_sem_escolha_mostra_a_arvore_do_BOM_e_bloqueia()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    c.Componente(12, "P-12", solido: 112);
    c.ReceitaDoCatalogo(10, 11, 2m);
    var a10 = c.Casado("AB-10", "Lida 10", 10);
    var p12 = c.Casado("P-12", "Lida 12", 12);
    c.Filho(c.Raiz, a10, 1m);
    var linha = c.Filho(a10, p12, 7m);

    var a = c.Avaliar();

    Assert.NotNull(a.Plano);
    var no12 = Assert.Single(Assert.Single(a.Raiz!.Filhos).Filhos);
    Assert.Equal(12, no12.ComponenteId);
    Assert.Equal(linha.Id, no12.FilhoId);
    Assert.Equal(7m, no12.QuantidadePorPai);
    Assert.True(Situacao(a, p12).NaArvoreFinal);

    var b = Assert.Single(a.Bloqueios);
    Assert.Equal(new BloqueioDto(
        "DivergenciaSemEscolha", a10.Id, 10,
        "A receita de AB-10 no catálogo é diferente da lida do BOM: escolha qual manter."), b);
  }

  // ---------------------------------------------------------------- escolha "catalogo" (P3)

  [Fact]
  public void Escolher_catalogo_tira_galho_so_do_BOM_e_traz_galho_so_do_catalogo()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", "Montagem", solido: 111);
    c.Componente(12, "P-12", solido: 112);
    c.ReceitaDoCatalogo(10, 11, 2m);
    c.ReceitaDoCatalogo(11, 12, 3m);
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Catalogo");
    // Codigo em branco e sem solido: fora da arvore final, nao bloqueia nada.
    var n3 = c.Novo(null, "Sem codigo", "", "Sem codigo", "Montagem");
    var n4 = c.Novo("N-4", "Lida N4", "N-4", "Nova N4", "Fabricado");
    c.Filho(c.Raiz, a10, 1m);
    c.Filho(a10, n3, 1m);
    c.Filho(n3, n4, 2m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    var no10 = Assert.Single(a.Raiz!.Filhos);
    var no11 = Assert.Single(no10.Filhos);
    Assert.Equal((null, 11, null, "P-11", "Desc P-11", 2m, "Catalogo", ""), Resumo(no11));
    var no12 = Assert.Single(no11.Filhos);
    Assert.Equal((null, 12, null, "P-12", "Desc P-12", 3m, "Catalogo", ""), Resumo(no12));
    Assert.Empty(no12.Filhos);

    Assert.True(Situacao(a, a10).NaArvoreFinal);
    Assert.False(Situacao(a, n3).NaArvoreFinal);
    Assert.False(Situacao(a, n4).NaArvoreFinal);
    Assert.Equal(new[] { (11, 2m) }, a.Sobreposicao.Filhos[10].ToArray());
  }

  [Fact]
  public void Escolher_catalogo_traz_filho_sem_solido_e_cria_bloqueio()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(13, "P-13");
    c.ReceitaDoCatalogo(10, 13, 1m);
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Catalogo");
    c.Filho(c.Raiz, a10, 1m);

    var a = c.Avaliar();

    var no13 = Assert.Single(Assert.Single(a.Raiz!.Filhos).Filhos);
    Assert.Equal("Catalogo", no13.Origem);
    Assert.Equal(new[] { "SemSolido" }, no13.Pendencias);
    Assert.Equal(
        new[] { new BloqueioDto("SemSolido", null, 13, "O componente P-13 precisa de sólido.") },
        a.Bloqueios);
  }

  [Fact]
  public void Efeito_de_manter_catalogo_conta_nos_expandidos_que_saem_e_entram()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "P-11", solido: 111);
    c.Componente(12, "P-12", solido: 112);
    c.Componente(13, "P-13", solido: 113);
    c.Componente(14, "P-14", solido: 114);
    c.Componente(15, "P-15", solido: 115);
    // Catalogo de 10: 11 -> (12, 13) e 14 -> 12 (diamante: 12 conta duas vezes), e 15 nos dois lados.
    c.ReceitaDoCatalogo(10, 11, 1m);
    c.ReceitaDoCatalogo(10, 14, 1m);
    c.ReceitaDoCatalogo(10, 15, 1m);
    c.ReceitaDoCatalogo(11, 12, 1m);
    c.ReceitaDoCatalogo(11, 13, 1m);
    c.ReceitaDoCatalogo(14, 12, 1m);
    // BOM de 10: N3 -> (N4, N5), N5 -> N4 (N4 conta duas vezes), e 15 nos dois lados.
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Importada");
    var p15 = c.Casado("P-15", "Lida 15", 15);
    var n3 = c.Novo("N-3", "N3", "N-3", "N3", "Montagem", pendente: 903);
    var n4 = c.Novo("N-4", "N4", "N-4", "N4", "Fabricado", pendente: 904);
    var n5 = c.Novo("N-5", "N5", "N-5", "N5", "Montagem", pendente: 905);
    c.Filho(c.Raiz, a10, 1m);
    c.Filho(a10, n3, 1m);
    c.Filho(a10, p15, 1m);
    c.Filho(n3, n4, 1m);
    c.Filho(n3, n5, 1m);
    c.Filho(n5, n4, 1m);

    var a = c.Avaliar();

    Assert.Equal(new EfeitoDto(Retira: 4, Traz: 5), Situacao(a, a10).EfeitoDeManterCatalogo);
  }

  [Fact]
  public void Mesmo_codigo_em_duas_ocorrencias_tem_uma_situacao_e_aparece_duas_vezes_na_arvore()
  {
    var c = new Cenario();
    var na = c.Novo("A", "A", "A", "A", "Montagem", pendente: 901);
    var nb = c.Novo("B", "B", "B", "B", "Montagem", pendente: 902);
    var nc = c.Novo("C", "C", "C", "C", "Fabricado", pendente: 903);
    c.Filho(c.Raiz, na, 1m);
    c.Filho(c.Raiz, nb, 1m);
    var deA = c.Filho(na, nc, 2m);
    var deB = c.Filho(nb, nc, 5m);

    var a = c.Avaliar();

    Assert.Equal(4, a.Componentes.Count);
    Assert.Single(a.Componentes, s => s.RegistroId == nc.Id);
    var ocorrencias = a.Raiz!.Filhos.SelectMany(f => f.Filhos).ToArray();
    Assert.Equal(new[] { (nc.Id, deA.Id, 2m), (nc.Id, deB.Id, 5m) },
        ocorrencias.Select(o => (o.RegistroId!.Value, o.FilhoId!.Value, o.QuantidadePorPai!.Value)).ToArray());
  }

  // ---------------------------------------------------------------- solido (D3)

  [Fact]
  public void Bruto_sem_solido_nao_bloqueia()
  {
    var c = new Cenario();
    c.Componente(14, "BR-14", "Bruto");
    var br = c.Casado("BR-14", "Bruto", 14);
    c.Filho(c.Raiz, br, 1m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    Assert.Empty(Assert.Single(a.Raiz!.Filhos).Pendencias);
  }

  [Fact]
  public void Novo_marcado_Bruto_nao_bloqueia()
  {
    var c = new Cenario();
    var br = c.Novo("BR-2", "Barra", "BR-2", "Barra", "Bruto");
    c.Filho(c.Raiz, br, 1m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    Assert.Equal(new[] { "Novo" }, Assert.Single(a.Raiz!.Filhos).Pendencias);
  }

  [Fact]
  public void Solido_pendente_satisfaz_a_exigencia()
  {
    var c = new Cenario();
    c.Componente(13, "P-13");
    c.Componente(11, "P-11", solido: 111);
    var p13 = c.Casado("P-13", "Lida 13", 13, pendente: 905);
    var p11 = c.Casado("P-11", "Lida 11", 11, pendente: 906);
    c.Solido(905, "peca.stl", 1234);
    c.Solido(906, "substituto.stl", 77);
    c.Filho(c.Raiz, p13, 1m);
    c.Filho(c.Raiz, p11, 1m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    var s13 = Situacao(a, p13);
    Assert.False(s13.TemSolido);
    Assert.True(s13.TemSolidoPendente);
    Assert.Equal("peca.stl", s13.NomeDoSolido);
    Assert.Equal(1234, s13.TamanhoDoSolidoEmBytes);
    // O pendente substitui o do catalogo na confirmacao, e e ele que a tela mostra.
    var s11 = Situacao(a, p11);
    Assert.True(s11.TemSolido);
    Assert.True(s11.TemSolidoPendente);
    Assert.Equal("substituto.stl", s11.NomeDoSolido);
    Assert.Equal(77, s11.TamanhoDoSolidoEmBytes);
  }

  [Fact]
  public void Novo_sem_solido_pendente_bloqueia_SemSolido()
  {
    var c = new Cenario();
    var n = c.Novo("N-2", "Lida", "N-2", "Nova", "Fabricado");
    c.Filho(c.Raiz, n, 1m);

    var a = c.Avaliar();

    Assert.Equal(new[] { "Novo", "SemSolido" }, Assert.Single(a.Raiz!.Filhos).Pendencias);
    Assert.Equal(new[] { new BloqueioDto("SemSolido", n.Id, null, "O componente N-2 precisa de sólido.") }, a.Bloqueios);
  }

  // ---------------------------------------------------------------- inativo e ordem das pendencias

  [Fact]
  public void Casado_inativo_tem_pendencia_Inativo_e_nao_bloqueia()
  {
    var c = new Cenario();
    c.Componente(16, "P-16", solido: 116, ativo: false);
    var p16 = c.Casado("P-16", "Lida 16", 16);
    c.Filho(c.Raiz, p16, 1m);

    var a = c.Avaliar();

    Assert.Empty(a.Bloqueios);
    Assert.Equal(new[] { "Inativo" }, Assert.Single(a.Raiz!.Filhos).Pendencias);
    Assert.False(Situacao(a, p16).Ativo);
  }

  [Fact]
  public void Pendencias_seguem_a_ordem_Novo_Inativo_Divergente_SemSolido()
  {
    var c = new Cenario();
    c.Componente(17, "AB-17", "Montagem", ativo: false);
    c.Componente(11, "P-11", solido: 111);
    c.ReceitaDoCatalogo(17, 11, 1m);
    var a17 = c.Casado("AB-17", "Lida 17", 17, escolha: "Importada");
    c.Filho(c.Raiz, a17, 1m);

    var a = c.Avaliar();

    Assert.Equal(new[] { "Inativo", "Divergente", "SemSolido" }, Assert.Single(a.Raiz!.Filhos).Pendencias);
  }

  // ---------------------------------------------------------------- codigo do novo

  [Fact]
  public void Novo_com_codigo_vazio_bloqueia_CodigoVazio()
  {
    var c = new Cenario();
    var n = c.Novo(null, "Chapa lateral", "  ", "Chapa lateral", "Bruto");
    c.Filho(c.Raiz, n, 1m);

    var a = c.Avaliar();

    Assert.Equal(
        new[] { new BloqueioDto("CodigoVazio", n.Id, null, "O componente novo \"Chapa lateral\" está sem código.") },
        a.Bloqueios);
  }

  [Fact]
  public void Novo_com_codigo_que_ja_existe_bloqueia_CodigoJaExiste()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    var n = c.Novo("ab-10", "Lida", "ab-10", "Nova", "Bruto");
    c.Filho(c.Raiz, n, 1m);

    var a = c.Avaliar();

    Assert.Equal(
        new[] { new BloqueioDto("CodigoJaExiste", n.Id, null, "O código ab-10 já existe no catálogo.") },
        a.Bloqueios);
  }

  [Fact]
  public void Dois_novos_com_o_mesmo_codigo_bloqueiam_CodigoJaExiste_no_segundo()
  {
    var c = new Cenario();
    var n1 = c.Novo(null, "Um", "X-1", "Um", "Bruto");
    var n2 = c.Novo(null, "Dois", "x-1", "Dois", "Bruto");
    c.Filho(c.Raiz, n1, 1m);
    c.Filho(c.Raiz, n2, 1m);

    var a = c.Avaliar();

    Assert.Equal(
        new[] { new BloqueioDto("CodigoJaExiste", n2.Id, null, "O código x-1 se repete em outro componente novo da importação.") },
        a.Bloqueios);
  }

  // ---------------------------------------------------------------- Peca e estrutura (P4)

  [Fact]
  public void Quantidade_da_peca_nula_bloqueia()
  {
    var c = new Cenario();
    c.Rascunho.QuantidadeDaPeca = null;

    var a = c.Avaliar();

    Assert.Equal(new[] { new BloqueioDto("QuantidadeDaPecaAusente", null, null, "Informe a quantidade da Peça.") }, a.Bloqueios);
    Assert.NotNull(a.Raiz);
    Assert.Equal(1m, a.Plano!.Quantidade);
  }

  [Fact]
  public void Escolha_importada_que_fecha_ciclo_com_o_catalogo_bloqueia_CicloNaReceita()
  {
    var c = new Cenario();
    c.Componente(10, "AB-10", "Montagem", solido: 110);
    c.Componente(11, "AB-11", "Montagem", solido: 111);
    c.Componente(12, "P-12", solido: 112);
    c.ReceitaDoCatalogo(10, 12, 1m);
    c.ReceitaDoCatalogo(11, 10, 1m);
    // 10 importa "10 -> 11", 11 mantem "11 -> 10" do catalogo: nenhuma das duas tinha ciclo sozinha.
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Importada");
    var a11 = c.Casado("AB-11", "Lida 11", 11, escolha: "Catalogo");
    c.Filho(c.Raiz, a10, 1m);
    c.Filho(a10, a11, 1m);

    var a = c.Avaliar();

    Assert.Equal(
        new[]
        {
          new BloqueioDto("CicloNaReceita", null, null,
              "A receita tem um ciclo: 10 -> 11 -> 10. Corrija a receita do catalogo antes de criar a Peca."),
        },
        a.Bloqueios);
    Assert.Null(a.Raiz);
    Assert.Null(a.Plano);
    // As situacoes continuam calculadas, e a arvore final vem da sobreposicao, nao da expansao.
    Assert.Equal(3, a.Componentes.Count);
    Assert.True(Situacao(a, a10).NaArvoreFinal);
    Assert.True(Situacao(a, a11).NaArvoreFinal);
  }

  [Fact]
  public void Quantidade_calculada_fora_da_coluna_bloqueia_QuantidadeForaDaFaixa()
  {
    var c = new Cenario();
    c.Rascunho.QuantidadeDaPeca = PlanejadorDeCopia.QuantidadeMaximaDaColuna;
    var n = c.Novo("N-2", "N", "N-2", "N", "Bruto");
    c.Filho(c.Raiz, n, 2m);

    var a = c.Avaliar();

    var b = Assert.Single(a.Bloqueios);
    Assert.Equal("QuantidadeForaDaFaixa", b.Tipo);
    Assert.Null(b.RegistroId);
    Assert.Null(b.ComponenteId);
    Assert.StartsWith($"A quantidade calculada para o componente {-n.Id} ", b.Mensagem);
    Assert.Null(a.Raiz);
    Assert.Null(a.Plano);
  }

  // ---------------------------------------------------------------- Ids a carregar e impressao

  [Fact]
  public void IdsAlcancaveis_traz_os_positivos_da_arvore_os_casados_e_os_filhos_de_catalogo_dos_casados()
  {
    var c = new Cenario();
    c.ReceitaDoCatalogo(10, 11, 1m);
    c.ReceitaDoCatalogo(11, 12, 1m);
    c.ReceitaDoCatalogo(20, 21, 1m);
    c.ReceitaDoCatalogo(21, 22, 1m);
    c.ReceitaDoCatalogo(30, 31, 1m);
    var a10 = c.Casado("AB-10", "Lida 10", 10, escolha: "Catalogo");
    var a20 = c.Casado("AB-20", "Lida 20", 20, escolha: "Importada");
    var fora = c.Casado("AB-30", "Lida 30", 30);
    var n = c.Novo("N", "N", "N", "N", "Fabricado");
    c.Filho(c.Raiz, a10, 1m);
    c.Filho(c.Raiz, a20, 1m);
    c.Filho(a20, n, 1m);

    var ids = AvaliadorDeImportacao.IdsAlcancaveis(c.Rascunho, c.Receita());

    // 10 -> 11 -> 12 pelo catalogo; 20 importada (21 e linha "Sai" do comparativo, 22 nao);
    // 30 casado fora da arvore e o 31 que o comparativo dele nomeia.
    Assert.Equal(new[] { 10, 11, 12, 20, 21, 30, 31 }, ids.Order().ToArray());
    Assert.DoesNotContain(-n.Id, ids);
    Assert.Equal(30, fora.ComponenteId);
  }

  [Fact]
  public void Impressao_independe_da_ordem_das_linhas()
  {
    var uma = AvaliadorDeImportacao.Impressao([(2, 1.5m), (1, 3m)]);
    var outra = AvaliadorDeImportacao.Impressao([(1, 3.0000m), (2, 1.50m)]);
    var diferente = AvaliadorDeImportacao.Impressao([(1, 3m), (2, 1.6m)]);

    Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("1:3.0000;2:1.5000;")), uma);
    Assert.Equal(uma, outra);
    Assert.NotEqual(uma, diferente);
  }
}
