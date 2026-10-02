using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Importacao;

/// <summary>
/// O rascunho do import da estrutura a partir do BOM (spec do import): criar a partir do arquivo, ler,
/// listar e descartar. O estado da conferencia (arvore, situacoes, bloqueios) e CALCULADO a cada leitura
/// por <see cref="AvaliadorDeImportacao"/> e nunca gravado (decisao P4 do plano do import), e e o retorno
/// de toda operacao que devolve o rascunho.
///
/// Erro do ARQUIVO volta como <see cref="ErroDeBomInvalido"/> com uma linha por erro em
/// <c>Detalhe</c>, separadas por <c>\n</c> (decisao P13), no mesmo canal de <c>erro</c>/<c>mensagem</c>
/// que as outras recusas da estrutura.
/// </summary>
public sealed class ImportacaoDeEstruturaUseCase
{
  public const string ErroDeBomInvalido = "BomInvalido";
  public const string ErroDeImportacaoDesatualizada = "ImportacaoDesatualizada";

  private const string ErroDeAgrupamentoNaoEncontrado = "Agrupamento nao encontrado.";
  private const string ErroDeImportacaoNaoEncontrada = "Importacao nao encontrada.";

  private const string TipoMontagem = "Montagem";
  private const string TipoFabricado = "Fabricado";

  // Os tetos das colunas de dbo.ImportacaoDeEstruturaComponente e dbo.ImportacaoDeEstrutura. Um texto
  // maior estoura como DbUpdateException (500) em vez de virar erro do arquivo com a linha dita.
  private const int CodigoMaximo = 50;
  private const int DescricaoMaxima = 200;
  private const int NomeDoArquivoMaximo = 260;

  private readonly IImportacaoDeEstruturaRepository _importacoes;
  private readonly IAgrupamentoRepository _agrupamentos;
  private readonly IComponenteRepository _componentes;
  private readonly IReceitaPadraoRepository _receitaPadrao;
  private readonly IEstruturaRepository _estruturas;
  private readonly ILeitorDeBom _leitor;

  public ImportacaoDeEstruturaUseCase(
      IImportacaoDeEstruturaRepository importacoes,
      IAgrupamentoRepository agrupamentos,
      IComponenteRepository componentes,
      IReceitaPadraoRepository receitaPadrao,
      IEstruturaRepository estruturas,
      ILeitorDeBom leitor)
  {
    _importacoes = importacoes;
    _agrupamentos = agrupamentos;
    _componentes = componentes;
    _receitaPadrao = receitaPadrao;
    _estruturas = estruturas;
    _leitor = leitor;
  }

  public async Task<Result<ImportacaoDto>> Criar(
      int agrupamentoId, string nomeDoArquivo, byte[] conteudo, int usuarioId, CancellationToken ct)
  {
    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Result<ImportacaoDto>.Falha(ErroDeAgrupamentoNaoEncontrado, TipoDeErro.NaoEncontrado);

    var nome = SoONome(nomeDoArquivo);

    var leitura = _leitor.Ler(nome, conteudo);
    if (leitura.Erros.Count > 0)
      return BomInvalido(leitura.Erros);

    var bom = MontadorDeReceitasDoBom.Montar(nome, leitura.Linhas);
    if (bom.Erros.Count > 0)
      return BomInvalido(bom.Erros);

    var deTamanho = ErrosDeTamanho(nome, leitura.Linhas, bom);
    if (deTamanho.Count > 0)
      return BomInvalido(deTamanho);

    var rascunho = await MontarRascunhoAsync(agrupamentoId, nome, bom, usuarioId, ct);
    await _importacoes.AdicionarAsync(rascunho, ct);
    return Result<ImportacaoDto>.Ok(await ProjetarAsync(rascunho, ct));
  }

  public async Task<Result<ImportacaoDto>> Obter(int id, CancellationToken ct)
  {
    var rascunho = await _importacoes.ObterAsync(id, ct);
    return rascunho is null
        ? Result<ImportacaoDto>.Falha(ErroDeImportacaoNaoEncontrada, TipoDeErro.NaoEncontrado)
        : Result<ImportacaoDto>.Ok(await ProjetarAsync(rascunho, ct));
  }

  public async Task<Result<IReadOnlyList<ResumoDeImportacaoDto>>> Listar(int agrupamentoId, CancellationToken ct)
  {
    if (await _agrupamentos.ObterPorIdAsync(agrupamentoId, ct) is null)
      return Result<IReadOnlyList<ResumoDeImportacaoDto>>.Falha(
          ErroDeAgrupamentoNaoEncontrado, TipoDeErro.NaoEncontrado);

    var resumos = await _importacoes.ListarDoAgrupamentoAsync(agrupamentoId, ct);
    return Result<IReadOnlyList<ResumoDeImportacaoDto>>.Ok(
        resumos.Select(r => new ResumoDeImportacaoDto(r.Id, r.NomeDoArquivo, r.CriadoPor, r.CriadoEm, r.AtualizadoEm))
            .ToList());
  }

  /// <summary>
  /// Apaga o rascunho, os registros, os filhos e os solidos pendentes (o repositorio faz tudo numa
  /// transacao). Nao pede a versao: descartar e a saida de um rascunho que ninguem mais quer, e
  /// exigir a versao so faria o usuario reler para conseguir jogar fora.
  /// </summary>
  public async Task<Result> Descartar(int id, CancellationToken ct)
  {
    if (await _importacoes.ObterAsync(id, ct) is null)
      return Result.Falha(ErroDeImportacaoNaoEncontrada, TipoDeErro.NaoEncontrado);

    await _importacoes.ExcluirAsync(id, ct);
    return Result.Ok();
  }

  /// <summary>
  /// Carrega o catalogo, avalia e projeta: o retorno de toda escrita no rascunho. A avaliacao e pura
  /// (<see cref="AvaliadorDeImportacao.Avaliar"/>); todo I/O dela mora aqui.
  /// </summary>
  private async Task<ImportacaoDto> ProjetarAsync(ImportacaoDeEstrutura r, CancellationToken ct)
  {
    var catalogo = await CarregarCatalogoAsync(r, ct);
    var avaliacao = AvaliadorDeImportacao.Avaliar(r, catalogo);
    var autor = await _importacoes.ObterNomeDoAutorAsync(r.CriadoPorUsuarioId, ct);

    return new ImportacaoDto(
        r.Id, r.AgrupamentoId, r.NomeDoArquivo, autor, r.CriadoEm, r.AtualizadoEm, r.Versao,
        r.QuantidadeDaPeca, r.RequerRelatorioDimensional, avaliacao.Raiz, avaliacao.Componentes,
        avaliacao.Bloqueios);
  }

  /// <summary>
  /// So o que a avaliacao le: a receita de catalogo inteira (a mesma leitura de
  /// <c>MontagemDeEstruturaUseCase.CriarPeca</c>), os Componentes que <see cref="AvaliadorDeImportacao.IdsAlcancaveis"/>
  /// pede, os codigos novos que ja existem no catalogo e o nome e o tamanho de cada solido, o do
  /// catalogo e o pendente, numa consulta so, sem o blob.
  /// </summary>
  private async Task<CatalogoParaAvaliacao> CarregarCatalogoAsync(ImportacaoDeEstrutura r, CancellationToken ct)
  {
    var (arestasFilhos, arestasMateriais, arestasRoteiro) = await _estruturas.LerReceitaCompletaAsync(ct);
    var receita = new ReceitaDoCatalogo(
        arestasFilhos.ToLookup(f => f.Pai, f => (f.Filho, f.Qtd)),
        arestasMateriais.ToLookup(m => m.Comp, m => (m.Material, m.Qtd)),
        arestasRoteiro.ToLookup(x => x.Comp, x => (x.Setor, x.Ordem)));

    var ids = AvaliadorDeImportacao.IdsAlcancaveis(r, receita);
    var componentes = (await _receitaPadrao.ObterComponentesPorIdAsync(ids, ct)).ToDictionary(c => c.Id);

    var codigosNovos = r.Componentes
        .Where(c => c.ComponenteId is null && !string.IsNullOrWhiteSpace(c.CodigoNovo))
        .Select(c => c.CodigoNovo!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    var existentes = codigosNovos.Count == 0
        ? []
        : (await _componentes.ListarPorCodigosAsync(codigosNovos, ct)).Select(c => c.Codigo);

    // Chaveado pelo Id do ArquivoDeComponente: o solido do catalogo e o pendente do rascunho vem juntos.
    var arquivoIds = componentes.Values.Where(c => c.ArquivoSolidoId is not null).Select(c => c.ArquivoSolidoId!.Value)
        .Concat(r.Componentes.Where(c => c.ArquivoSolidoPendenteId is not null).Select(c => c.ArquivoSolidoPendenteId!.Value))
        .Distinct()
        .ToList();
    var solidos = arquivoIds.Count == 0
        ? new Dictionary<int, MetadadoDeSolido>()
        : await _importacoes.ObterMetadadosAsync(arquivoIds, ct);

    return new CatalogoParaAvaliacao(
        receita, componentes, new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase), solidos);
  }

  /// <summary>
  /// Um registro por codigo do BOM. Casa com o Componente de mesmo codigo (o catalogo compara sem
  /// diferenciar caixa, e inclui os inativos) ou fica "criar novo", ja preenchido com o que o arquivo
  /// trouxe: o avaliador nao cai mais no codigo lido, entao o preenchimento e obrigatorio. O tipo e
  /// "Montagem" quando o codigo tem filhos no BOM e "Fabricado" quando nao. A raiz (chave 0, decisao
  /// P8) nao tem codigo, entao nunca casa e nasce "criar novo" com o codigo em branco.
  /// </summary>
  private async Task<ImportacaoDeEstrutura> MontarRascunhoAsync(
      int agrupamentoId, string nomeDoArquivo, BomMontado bom, int usuarioId, CancellationToken ct)
  {
    var codigos = bom.Componentes.Where(c => c.Codigo is not null).Select(c => c.Codigo!).ToList();
    var casados = new Dictionary<string, Componente>(StringComparer.OrdinalIgnoreCase);
    if (codigos.Count > 0)
    {
      foreach (var existente in await _componentes.ListarPorCodigosAsync(codigos, ct))
        casados.TryAdd(existente.Codigo, existente);
    }

    var temFilhos = bom.Filhos.Select(f => f.PaiChave).ToHashSet();
    var registros = new Dictionary<int, ImportacaoDeEstruturaComponente>();
    foreach (var c in bom.Componentes)
    {
      var registro = new ImportacaoDeEstruturaComponente { CodigoLido = c.Codigo, DescricaoLida = c.Descricao };
      if (c.Codigo is not null && casados.TryGetValue(c.Codigo, out var casado))
      {
        registro.ComponenteId = casado.Id;
      }
      else
      {
        registro.CodigoNovo = c.Codigo;
        registro.DescricaoNova = c.Descricao;
        registro.TipoNovo = temFilhos.Contains(c.Chave) ? TipoMontagem : TipoFabricado;
      }
      registros[c.Chave] = registro;
    }

    foreach (var f in bom.Filhos)
    {
      registros[f.PaiChave].Filhos.Add(new ImportacaoDeEstruturaFilho
      {
        Filho = registros[f.FilhoChave],
        Ordem = f.Ordem,
        QuantidadeLida = f.Quantidade,
        Quantidade = f.Quantidade,
      });
    }

    return new ImportacaoDeEstrutura
    {
      AgrupamentoId = agrupamentoId,
      NomeDoArquivo = nomeDoArquivo,
      CriadoPorUsuarioId = usuarioId,
      Componentes = [.. registros.Values],
      Raiz = registros[0],
    };
  }

  /// <summary>
  /// O que o montador nao confere e o banco recusaria como 500: codigo acima de 50 caracteres,
  /// descricao vazia ou acima de 200 (decisao R6 do controlador do plano). Vale a PRIMEIRA ocorrencia
  /// de cada codigo, que e a que o montador toma como a descricao do registro; cada linha sem codigo e
  /// um registro e e conferida. A descricao da raiz e o nome do arquivo sem extensao e nao tem linha.
  /// </summary>
  private static List<ErroDoBom> ErrosDeTamanho(
      string nomeDoArquivo, IReadOnlyList<LinhaCruaDoBom> linhas, BomMontado bom)
  {
    var erros = new List<ErroDoBom>();

    if (nomeDoArquivo.Length > NomeDoArquivoMaximo)
      erros.Add(new ErroDoBom(null, $"o nome do arquivo passa de {NomeDoArquivoMaximo} caracteres."));
    else if (bom.Componentes.Single(c => c.Chave == 0).Descricao.Length > DescricaoMaxima)
      erros.Add(new ErroDoBom(null, $"o nome do arquivo (sem a extensao) passa de {DescricaoMaxima} caracteres."));

    var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var linha in linhas)
    {
      var codigo = string.IsNullOrWhiteSpace(linha.Codigo) ? null : linha.Codigo.Trim();
      if (codigo is not null && !vistos.Add(codigo))
        continue;

      if (codigo is not null && codigo.Length > CodigoMaximo)
        erros.Add(new ErroDoBom(linha.NumeroDaLinha, $"codigo com {codigo.Length} caracteres: o maximo e {CodigoMaximo}."));

      var descricao = (linha.Descricao ?? string.Empty).Trim();
      if (descricao.Length == 0)
        erros.Add(new ErroDoBom(linha.NumeroDaLinha, "descricao vazia."));
      else if (descricao.Length > DescricaoMaxima)
        erros.Add(new ErroDoBom(linha.NumeroDaLinha, $"descricao com {descricao.Length} caracteres: o maximo e {DescricaoMaxima}."));
    }

    return [.. erros.OrderBy(e => e.Linha ?? int.MaxValue)];
  }

  /// <summary>
  /// So o nome do arquivo: um cliente antigo manda o caminho inteiro, e a coluna guarda o nome. Corta nas
  /// duas barras, e nao em <c>Path.GetFileName</c>, que so reconhece o separador do sistema do servidor.
  /// </summary>
  private static string SoONome(string? nomeDoArquivo)
  {
    var nome = nomeDoArquivo ?? string.Empty;
    return nome[(nome.LastIndexOfAny(['/', '\\']) + 1)..];
  }

  private static Result<ImportacaoDto> BomInvalido(IEnumerable<ErroDoBom> erros) =>
      Result<ImportacaoDto>.Falha(
          ErroDeBomInvalido, TipoDeErro.Validacao, string.Join('\n', erros.Select(e => e.ToString())));
}
