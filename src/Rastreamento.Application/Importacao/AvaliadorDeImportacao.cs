using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rastreamento.Application.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;
using static Rastreamento.Application.Importacao.ValoresDaConferencia;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// O catalogo que a avaliacao le. <see cref="Componentes"/> tem de cobrir
/// <see cref="AvaliadorDeImportacao.IdsAlcancaveis"/>; <see cref="CodigosExistentes"/> compara com
/// <c>OrdinalIgnoreCase</c>, como o banco; <see cref="Solidos"/> e chaveado pelo Id do
/// <c>ArquivoDeComponente</c> e cobre o solido do catalogo e o pendente do rascunho.
/// </summary>
public sealed record CatalogoParaAvaliacao(
    ReceitaDoCatalogo Receita,
    IReadOnlyDictionary<int, Componente> Componentes,
    IReadOnlySet<string> CodigosExistentes,
    IReadOnlyDictionary<int, MetadadoDeSolido> Solidos);

/// <summary>
/// O estado da conferencia. <see cref="Plano"/> e a expansao para a quantidade da Peca (ou 1, quando
/// ela falta, so para montar a arvore); <see cref="Sobreposicao"/> e a receita que a gerou. Quando a
/// expansao e recusada (ciclo, profundidade, tamanho ou quantidade fora da coluna), <see cref="Raiz"/> e
/// <see cref="Plano"/> sao nulos e o motivo esta em <see cref="Bloqueios"/>; as situacoes e os outros
/// bloqueios continuam calculados, porque a arvore final vem da sobreposicao e nao da expansao.
/// </summary>
public sealed record AvaliacaoDeImportacao(
    NoDaImportacaoDto? Raiz,
    IReadOnlyList<SituacaoDoComponenteDto> Componentes,
    IReadOnlyList<BloqueioDto> Bloqueios,
    NoPlanejado? Plano,
    ReceitaDoCatalogo Sobreposicao);

/// <summary>
/// Calcula o estado da conferencia do import a partir do rascunho e do catalogo atual. PURO, sem I/O:
/// serve ao <c>GET</c> e a confirmacao, e o estado nunca e gravado (decisao P4 do plano do import).
///
/// A expansao e UMA chamada a <see cref="PlanejadorDeCopia.Planejar"/> sobre a sobreposicao, e por isso
/// o ciclo, a profundidade e o tamanho sao os do planejador, com as mesmas mensagens: o ciclo que so
/// existe juntando uma receita importada e uma de catalogo mantida (secao 5.4 da spec do import) e
/// achado sem travessia propria, porque toda aresta sobreposta tem o pai alcancavel da raiz.
///
/// Os bloqueios por registro (codigo, divergencia, solido) so valem para a arvore final (decisao P3):
/// um codigo que saiu por uma escolha "Catalogo" acima dele nao vira Componente, e nao trava nada.
/// </summary>
public static class AvaliadorDeImportacao
{
  private const string TipoBruto = "Bruto";

  private const string MensagemDeQuantidadeExcessiva =
      "A quantidade, multiplicada pela receita, passa do que o sistema suporta.";

  /// <summary>
  /// Os Ids positivos que a avaliacao le do catalogo: os alcancados na sobreposicao, os casados
  /// (mesmo fora da arvore, porque a situacao deles mostra o Componente) e os filhos diretos de catalogo
  /// de cada casado (as linhas "Sai" do comparativo). O caso de uso os carrega antes de chamar
  /// <see cref="Avaliar"/>.
  /// </summary>
  public static IReadOnlySet<int> IdsAlcancaveis(ImportacaoDeEstrutura r, ReceitaDoCatalogo receita)
  {
    var sobreposicao = new SobreposicaoDaImportacao(r, receita);
    var ids = sobreposicao.Alcancaveis.Where(id => id > 0).ToHashSet();
    foreach (var registro in r.Componentes)
    {
      if (registro.ComponenteId is not int id)
        continue;
      ids.Add(id);
      ids.UnionWith(receita.Filhos[id].Select(f => f.FilhoId));
    }
    return ids;
  }

  public static AvaliacaoDeImportacao Avaliar(ImportacaoDeEstrutura r, CatalogoParaAvaliacao c)
  {
    var s = new SobreposicaoDaImportacao(r, c.Receita);

    var bloqueios = new List<BloqueioDto>();
    if (r.QuantidadeDaPeca is null)
      bloqueios.Add(new BloqueioDto(BloqueioQuantidadeDaPecaAusente, null, null, "Informe a quantidade da Peça."));
    bloqueios.AddRange(BloqueiosDaArvoreFinal(s, c));

    var (plano, recusa) = Expandir(s, r.QuantidadeDaPeca ?? 1m);
    if (recusa is not null)
      bloqueios.Add(recusa);

    var raiz = plano is null ? null : Mapear(plano, null, s, c);
    var situacoes = r.Componentes.OrderBy(x => x.Id).Select(x => Situacao(x, s, c)).ToList();
    return new AvaliacaoDeImportacao(raiz, situacoes, bloqueios, plano, s.Receita);
  }

  /// <summary>
  /// SHA-256 de <c>"{filhoId}:{quantidade:F4};"</c> por linha, ordenadas por <c>FilhoId</c>, em cultura
  /// invariante. Detecta, na confirmacao, que a receita de catalogo mudou depois da escolha.
  /// </summary>
  public static byte[] Impressao(IEnumerable<(int FilhoId, decimal Quantidade)> receita)
  {
    var texto = new StringBuilder();
    foreach (var (filhoId, quantidade) in receita.OrderBy(l => l.FilhoId).ThenBy(l => l.Quantidade))
      texto.Append(CultureInfo.InvariantCulture, $"{filhoId}:{quantidade:F4};");
    return SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString()));
  }

  /// <summary>
  /// O mesmo tratamento de <c>MontagemDeEstruturaUseCase.PlanejarCopiaDoCatalogo</c>: o erro do
  /// planejador vira bloqueio com o codigo e a frase dele, e as duas excecoes de quantidade viram
  /// <see cref="ValoresDaConferencia.BloqueioQuantidadeForaDaFaixa"/>.
  /// </summary>
  private static (NoPlanejado? Plano, BloqueioDto? Recusa) Expandir(SobreposicaoDaImportacao s, decimal quantidade)
  {
    try
    {
      var plano = PlanejadorDeCopia.Planejar(s.Receita, s.RaizId, quantidade);
      return plano.Erro is null
          ? (plano.Raiz, null)
          : (null, new BloqueioDto(plano.CodigoDoErro!, null, null, plano.Erro));
    }
    catch (OverflowException)
    {
      return (null, new BloqueioDto(BloqueioQuantidadeForaDaFaixa, null, null, MensagemDeQuantidadeExcessiva));
    }
    catch (QuantidadeForaDaColunaException e)
    {
      return (null, new BloqueioDto(BloqueioQuantidadeForaDaFaixa, null, null, e.Message));
    }
  }

  /// <summary>Em pre-ordem da arvore final, e por registro: codigo, divergencia, solido.</summary>
  private static List<BloqueioDto> BloqueiosDaArvoreFinal(SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var bloqueios = new List<BloqueioDto>();
    var codigosNovos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var id in s.Alcancaveis)
    {
      var registro = s.RegistroDoId(id);
      if (id < 0 && registro is not null)
      {
        var codigo = registro.CodigoNovo?.Trim() ?? string.Empty;
        if (codigo.Length == 0)
          bloqueios.Add(new BloqueioDto(BloqueioCodigoVazio, registro.Id, null,
              $"O componente novo \"{DescricaoDoNovo(registro)}\" está sem código."));
        else if (c.CodigosExistentes.Contains(codigo))
          bloqueios.Add(new BloqueioDto(BloqueioCodigoJaExiste, registro.Id, null,
              $"O código {codigo} já existe no catálogo."));
        // Dois novos com o mesmo codigo esbarrariam em `UQ_Componente_Codigo` na confirmacao.
        else if (!codigosNovos.Add(codigo))
          bloqueios.Add(new BloqueioDto(BloqueioCodigoJaExiste, registro.Id, null,
              $"O código {codigo} se repete em outro componente novo da importação."));
      }

      if (registro is not null && s.Diverge(registro) && registro.EscolhaDeReceita is null)
        bloqueios.Add(new BloqueioDto(BloqueioDivergenciaSemEscolha, registro.Id, id,
            $"A receita de {Nome(id, s, c).Codigo} no catálogo é diferente da lida do BOM: escolha qual manter."));

      if (PrecisaDeSolido(id, registro, c))
        bloqueios.Add(new BloqueioDto(BloqueioSemSolido, registro?.Id, id > 0 ? id : null,
            $"O componente {Rotulo(id, s, c)} precisa de sólido."));
    }
    return bloqueios;
  }

  private static NoDaImportacaoDto Mapear(
      NoPlanejado no, int? paiId, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var id = no.ComponenteId!.Value;
    var registro = s.RegistroDoId(id);
    var (codigo, descricao) = Nome(id, s, c);
    return new NoDaImportacaoDto(
        RegistroId: registro?.Id,
        ComponenteId: id > 0 ? id : null,
        FilhoId: paiId is int pai ? s.LinhaDaAresta(pai, id)?.Id : null,
        Codigo: codigo,
        Descricao: descricao,
        QuantidadePorPai: no.QuantidadePorPai,
        Origem: registro is null ? OrigemCatalogo : OrigemBom,
        Pendencias: Pendencias(id, registro, s, c),
        Filhos: no.Filhos.Select(f => Mapear(f, id, s, c)).ToList());
  }

  private static List<string> Pendencias(
      int id, ImportacaoDeEstruturaComponente? registro, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var pendencias = new List<string>();
    if (id < 0)
      pendencias.Add(PendenciaNovo);
    if (id > 0 && !ComponenteDoCatalogo(id, c).Ativo)
      pendencias.Add(PendenciaInativo);
    if (registro is not null && s.Diverge(registro))
      pendencias.Add(PendenciaDivergente);
    if (PrecisaDeSolido(id, registro, c))
      pendencias.Add(PendenciaSemSolido);
    return pendencias;
  }

  /// <summary>
  /// D3 da spec do import: todo Componente nao-<c>Bruto</c> da arvore final precisa de solido, o do
  /// catalogo ou o pendente do rascunho. O tipo do novo e o <c>TipoNovo</c>; sem ele, exige.
  /// </summary>
  private static bool PrecisaDeSolido(int id, ImportacaoDeEstruturaComponente? registro, CatalogoParaAvaliacao c)
  {
    var componente = id > 0 ? ComponenteDoCatalogo(id, c) : null;
    var tipo = componente?.Tipo ?? registro?.TipoNovo;
    if (tipo == TipoBruto)
      return false;
    return componente?.ArquivoSolidoId is null && registro?.ArquivoSolidoPendenteId is null;
  }

  private static SituacaoDoComponenteDto Situacao(
      ImportacaoDeEstruturaComponente registro, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var componente = registro.ComponenteId is int cid ? ComponenteDoCatalogo(cid, c) : null;
    var arquivo = registro.ArquivoSolidoPendenteId ?? componente?.ArquivoSolidoId;
    var solido = arquivo is int a ? c.Solidos.GetValueOrDefault(a) : null;
    var diverge = s.Diverge(registro);

    return new SituacaoDoComponenteDto(
        RegistroId: registro.Id,
        CodigoLido: registro.CodigoLido,
        DescricaoLida: registro.DescricaoLida,
        ComponenteId: registro.ComponenteId,
        CodigoDoCatalogo: componente?.Codigo,
        DescricaoDoCatalogo: componente?.Descricao,
        Tipo: componente?.Tipo,
        Ativo: componente?.Ativo,
        TemSolido: componente?.ArquivoSolidoId is not null,
        TemSolidoPendente: registro.ArquivoSolidoPendenteId is not null,
        NomeDoSolido: solido?.NomeOriginal,
        TamanhoDoSolidoEmBytes: solido?.TamanhoEmBytes,
        CodigoNovo: registro.CodigoNovo,
        DescricaoNova: registro.DescricaoNova,
        TipoNovo: registro.TipoNovo,
        Divergente: diverge,
        EscolhaDeReceita: registro.EscolhaDeReceita,
        Comparativo: diverge ? Comparativo(registro, s, c) : [],
        EfeitoDeManterCatalogo: diverge ? Efeito(registro, s, c) : null,
        NaArvoreFinal: s.EstaNaArvore(SobreposicaoDaImportacao.IdDe(registro)));
  }

  /// <summary>
  /// D4 da spec do import, um nivel: as linhas do BOM na ordem dele, depois as so do catalogo por
  /// codigo. Um Id positivo e nomeado pelo Componente do catalogo (regra 3 da secao 5.1 da spec do
  /// import: vale a descricao do catalogo); um novo, pelo proprio registro.
  /// </summary>
  private static List<LinhaDoComparativoDto> Comparativo(
      ImportacaoDeEstruturaComponente registro, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var doCatalogo = c.Receita.Filhos[registro.ComponenteId!.Value]
        .GroupBy(f => f.FilhoId)
        .ToDictionary(g => g.Key, g => g.Sum(f => f.QuantidadePadrao));
    var lida = s.Lida(registro);

    var linhas = new List<LinhaDoComparativoDto>();
    foreach (var (filhoId, quantidade, _) in lida)
    {
      var (codigo, descricao) = Nome(filhoId, s, c);
      decimal? noCatalogo = doCatalogo.TryGetValue(filhoId, out var q) ? q : null;
      var situacao = noCatalogo is null ? LinhaEntra : noCatalogo == quantidade ? LinhaIgual : LinhaQuantidadeMuda;
      linhas.Add(new LinhaDoComparativoDto(codigo, descricao, noCatalogo, quantidade, situacao));
    }

    var naLida = lida.Select(l => l.FilhoId).ToHashSet();
    linhas.AddRange(doCatalogo
        .Where(f => !naLida.Contains(f.Key))
        .Select(f => (Componente: ComponenteDoCatalogo(f.Key, c), Quantidade: f.Value))
        .OrderBy(f => f.Componente.Codigo, StringComparer.Ordinal)
        .ThenBy(f => f.Componente.Id)
        .Select(f => new LinhaDoComparativoDto(
            f.Componente.Codigo, f.Componente.Descricao, f.Quantidade, null, LinhaSai)));
    return linhas;
  }

  /// <summary>
  /// O que "manter a do catalogo" faz numa ocorrencia: os filhos so do BOM saem com tudo o que esta
  /// abaixo deles, e os so do catalogo entram, expandidos pela sobreposicao (que, para um Id fora do
  /// rascunho, e a receita de catalogo dele; para um que esta no rascunho, respeita a escolha dele).
  /// </summary>
  private static EfeitoDto Efeito(
      ImportacaoDeEstruturaComponente registro, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var lida = s.Lida(registro).Select(l => l.FilhoId).Distinct().ToList();
    var doCatalogo = c.Receita.Filhos[registro.ComponenteId!.Value].Select(f => f.FilhoId).Distinct().ToList();
    return new EfeitoDto(
        Retira: s.ContarExpandidos(lida.Except(doCatalogo)),
        Traz: s.ContarExpandidos(doCatalogo.Except(lida)));
  }

  /// <summary>
  /// Codigo e descricao com que a tela mostra um Id: o do catalogo para um positivo (regra 3 da secao
  /// 5.1 da spec do import); o "criar novo" do registro para um negativo, caindo no lido enquanto o
  /// novo estiver vazio.
  /// </summary>
  private static (string Codigo, string Descricao) Nome(int id, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    if (id > 0)
    {
      var componente = ComponenteDoCatalogo(id, c);
      return (componente.Codigo, componente.Descricao);
    }
    var registro = s.RegistroDoId(id)!;
    return (registro.CodigoNovo ?? registro.CodigoLido ?? string.Empty, DescricaoDoNovo(registro));
  }

  private static string DescricaoDoNovo(ImportacaoDeEstruturaComponente registro) =>
      string.IsNullOrWhiteSpace(registro.DescricaoNova) ? registro.DescricaoLida : registro.DescricaoNova;

  /// <summary>O codigo; sem codigo (novo em branco), a descricao entre aspas.</summary>
  private static string Rotulo(int id, SobreposicaoDaImportacao s, CatalogoParaAvaliacao c)
  {
    var (codigo, descricao) = Nome(id, s, c);
    return string.IsNullOrWhiteSpace(codigo) ? $"\"{descricao}\"" : codigo;
  }

  private static Componente ComponenteDoCatalogo(int id, CatalogoParaAvaliacao c) =>
      c.Componentes.TryGetValue(id, out var componente)
          ? componente
          : throw new InvalidOperationException(
              $"O Componente {id} nao foi carregado: o catalogo da avaliacao tem de cobrir IdsAlcancaveis.");
}
