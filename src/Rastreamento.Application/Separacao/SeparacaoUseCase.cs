using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// Separar e estornar no livro de separacao (regra 34), que e so de inclusao: a correcao e um Estorno que
/// aponta a linha original, uma vez so (`UX_MaterialSeparacao_EstornoDe`). Sem a transacao da execucao
/// (decisao P1 do plano de backend da Fase 4): separar grava tudo num `SaveChanges` so, e a corrida de dois
/// estornos da mesma linha e fechada pelo indice unico. A pausa do Pedido nao entra aqui: ela recusa so o
/// inicio (regra 31), e separar continua.
/// </summary>
public sealed class SeparacaoUseCase
{
  private readonly ISeparacaoRepository _separacao;
  private readonly IExecucaoRepository _execucao;
  private readonly LeitorDeEstado _leitor;

  public SeparacaoUseCase(
      ISeparacaoRepository separacao, IExecucaoRepository execucao, IEstruturaRepository estruturas,
      IReceitaPadraoRepository catalogo)
  {
    _separacao = separacao;
    _execucao = execucao;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  /// <summary>
  /// Tudo ou nada. A ordem das validacoes e a da decisao P8 do plano de backend da Fase 4: no (404), lista
  /// vazia, quantidade, material repetido, fora do planejado (409). Acima do planejado e aceito e volta
  /// marcado (D9 da spec da Fase 4); o material que fica num Setor e aceito se estiver no planejado (D16).
  /// </summary>
  public async Task<Result<ResultadoDaSeparacaoDto>> Separar(
      int noId, SeparacaoNovaDto dto, int usuarioId, CancellationToken ct)
  {
    var validada = await ValidarAsync(noId, dto.Linhas, ct);
    if (!validada.Sucesso) return Falhas.Repassar<ResultadoDaSeparacaoDto, SeparacaoValidada>(validada);

    var doNo = (await GravarEProjetarAsync([validada.Valor!], usuarioId, ct)).Single();
    return Result<ResultadoDaSeparacaoDto>.Ok(new ResultadoDaSeparacaoDto(doNo.Separacoes, doNo.Materiais));
  }

  /// <summary>
  /// Separar em varios nos num corpo so, tudo ou nada (`POST /separacoes`). Antes de olhar os nos, as recusas
  /// de entrada do lote da execucao, nesta ordem: lista vazia, teto, no repetido. Depois cada no, na ordem do
  /// corpo, pelo MESMO nucleo da rota individual (<see cref="ValidarAsync"/>), parando na primeira recusa,
  /// com o nome do no. So com todos validos as linhas de todos vao num `GravarAsync` so: como na rota
  /// individual, sem a transacao da execucao (decisao P1 do plano de backend da Fase 4). A resposta e uma
  /// entrada por no, na ordem do corpo.
  /// </summary>
  public async Task<Result<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>> SepararEmLote(
      LoteDeSeparacaoDto dto, int usuarioId, CancellationToken ct)
  {
    var nos = dto.Nos ?? [];
    if (nos.Count == 0) return Falhas.LoteVazio<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>();
    if (nos.Count > ApontamentoUseCase.TamanhoMaximoDoLote)
      return Falhas.LoteGrandeDemais<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>();
    var vistos = new HashSet<int>();
    foreach (var no in nos)
      if (!vistos.Add(no.EstruturaItemId))
        return Falhas.Validacao<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>(CodigosDaExecucao.ItemRepetido,
            $"O nó {no.EstruturaItemId} aparece mais de uma vez no lote.");

    var validadas = new List<SeparacaoValidada>(nos.Count);
    foreach (var no in nos)
    {
      var validada = await ValidarAsync(no.EstruturaItemId, no.Linhas, ct);
      if (!validada.Sucesso) return await NomeadaAsync(validada, no.EstruturaItemId, ct);
      validadas.Add(validada.Valor!);
    }
    return Result<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>.Ok(await GravarEProjetarAsync(validadas, usuarioId, ct));
  }

  /// <summary>O que passou pelas regras de UM no: as linhas do corpo e o catalogo dos materiais delas.</summary>
  private sealed record SeparacaoValidada(
      int NoId, IReadOnlyList<LinhaDeMaterialDto> Linhas, IReadOnlyDictionary<int, MaterialDaSeparacao> Catalogo);

  /// <summary>
  /// As regras de separar em UM no, a unica copia delas: a rota individual e o lote passam por aqui. Nao
  /// escreve nada. A ordem e a da decisao P8 do plano de backend da Fase 4.
  /// </summary>
  private async Task<Result<SeparacaoValidada>> ValidarAsync(
      int noId, IReadOnlyList<LinhaDeMaterialDto>? doCorpo, CancellationToken ct)
  {
    // O 404 vem antes de olhar o corpo: o no que nao existe nao tem o que validar.
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<SeparacaoValidada>();

    if (doCorpo is not { Count: > 0 } linhas)
      return Falhas.Validacao<SeparacaoValidada>(CodigosDaSeparacao.SeparacaoVazia,
          "Informe ao menos um material para separar.");

    foreach (var linha in linhas)
      if (!Quantidades.CabeNaColuna(linha.Quantidade))
        return Falhas.QuantidadeInvalida<SeparacaoValidada>(linha.Quantidade);

    var ids = linhas.Select(l => l.MaterialId).Distinct().ToList();
    var catalogo = (await _separacao.ListarMateriaisAsync(ids, ct)).ToDictionary(m => m.Id);

    var repetido = linhas.GroupBy(l => l.MaterialId).FirstOrDefault(g => g.Count() > 1);
    if (repetido is not null)
      return Falhas.Validacao<SeparacaoValidada>(CodigosDaSeparacao.MaterialRepetido,
          $"O material {Nome(repetido.Key, catalogo)} aparece mais de uma vez na separação.");

    var planejados = (await _separacao.ListarPlanejadoAsync([noId], ct)).Select(p => p.MaterialId).ToHashSet();
    var fora = linhas.FirstOrDefault(l => !planejados.Contains(l.MaterialId));
    if (fora is not null)
      return Falhas.Conflito<SeparacaoValidada>(CodigosDaSeparacao.MaterialForaDoPlanejado,
          $"O material {Nome(fora.MaterialId, catalogo)} não está no planejado {await DoNoAsync(noId, ct)}. "
          + "O PCP o acrescenta nos materiais do nó, se ele for mesmo deste nó.");

    return Result<SeparacaoValidada>.Ok(new SeparacaoValidada(noId, linhas, catalogo));
  }

  /// <summary>
  /// Grava as linhas de todos os nos num `GravarAsync` so (tudo ou nada) e devolve, por no e na ordem
  /// recebida, as linhas gravadas e os materiais que elas tocaram, com a conta relida depois da escrita.
  /// </summary>
  private async Task<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>> GravarEProjetarAsync(
      IReadOnlyList<SeparacaoValidada> validadas, int usuarioId, CancellationToken ct)
  {
    var agora = DateTime.UtcNow;
    var novasPorNo = validadas.Select(v => v.Linhas.Select(l => new MaterialSeparacao
    {
      EstruturaItemId = v.NoId, MaterialId = l.MaterialId, Tipo = TiposDeSeparacao.Separacao,
      Quantidade = l.Quantidade, DataHora = agora, UsuarioId = usuarioId,
    }).ToList()).ToList();
    await _separacao.GravarAsync(novasPorNo.SelectMany(n => n).ToList(), ct);

    // Relido depois da escrita: o resumo inclui o que outro Separador gravou nos mesmos nos ate aqui.
    var nos = validadas.Select(v => v.NoId).ToList();
    var planejado = await _separacao.ListarPlanejadoAsync(nos, ct);
    var doLivro = await _separacao.ListarLinhasAsync(nos, ct);
    var catalogo = validadas.SelectMany(v => v.Catalogo.Values).DistinctBy(m => m.Id).ToDictionary(m => m.Id);
    var doAlmoxarifado = catalogo.Values.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();
    var resumos = CalculoDaSeparacao.Resumir(planejado, doLivro, doAlmoxarifado);
    var nome = await NomeDoUsuarioAsync(usuarioId, ct);

    return validadas.Select((v, i) =>
    {
      var tocados = v.Linhas.Select(l => l.MaterialId).ToHashSet();
      var materiais = resumos
          .Where(r => r.EstruturaItemId == v.NoId && tocados.Contains(r.MaterialId))
          .Select(r => ProjecaoDaSeparacao.Projetar(r, catalogo[r.MaterialId]))
          .OrderBy(m => m.Codigo, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.MaterialId)
          .ToList();
      // Quem acabou de separar e o autor: toda linha nova e estornavel por ele.
      var separacoes = novasPorNo[i]
          .Select(l => ProjecaoDaSeparacao.Linha(l, catalogo[l.MaterialId], nome, estornavel: true))
          .ToList();
      return new ResultadoDaSeparacaoDoNoDto(v.NoId, separacoes, materiais);
    }).ToList();
  }

  /// <summary>
  /// A recusa de um no do lote com o nome dele na frente da frase, como o `Nomeada` do lote da execucao. Nao
  /// levam o prefixo o 404, que continua sem corpo, e a recusa cuja frase ja nomeia o no (o "fora do
  /// planejado" diz "do nó Quadro"). O nome e o da regra 19, o mesmo que a tela mostra.
  /// </summary>
  private async Task<Result<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>>> NomeadaAsync(
      Result<SeparacaoValidada> falha, int noId, CancellationToken ct) =>
      falha.TipoDoErro == TipoDeErro.NaoEncontrado || falha.Erro == CodigosDaSeparacao.MaterialForaDoPlanejado
          ? Falhas.Repassar<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>, SeparacaoValidada>(falha)
          : Falhas.Repassar<IReadOnlyList<ResultadoDaSeparacaoDoNoDto>, SeparacaoValidada>(
              falha, $"{await NomeDoNoAsync(noId, ct)}: {falha.Detalhe}");

  /// <summary>
  /// Grava o inverso da linha, apontando-a. Ordem da decisao P8 do plano de backend da Fase 4: linha (404),
  /// autor ou PCP/Administrador (403), estorno nao se estorna (409), ja estornada (409). Nao olha o
  /// planejado: a separacao de um material que saiu do plano continua estornavel (D14 da spec da Fase 4).
  /// </summary>
  public async Task<Result<SeparacaoDto>> Estornar(
      int separacaoId, int usuarioId, bool podeEstornarDeOutros, CancellationToken ct)
  {
    var original = await _separacao.ObterLinhaAsync(separacaoId, ct);
    if (original is null) return Falhas.NaoEncontrado<SeparacaoDto>();
    if (original.UsuarioId != usuarioId && !podeEstornarDeOutros) return Falhas.Proibido<SeparacaoDto>();
    if (original.Tipo == TiposDeSeparacao.Estorno)
      return Falhas.Conflito<SeparacaoDto>(CodigosDaSeparacao.EstornoDeEstorno,
          $"Este registro é um estorno de {await DoRegistroAsync(original, ct)}, e estorno não se estorna: "
          + "registre de novo a separação.");
    if (await _separacao.FoiEstornadaAsync(original.Id, ct)) return await JaEstornadaAsync(original, ct);

    var estorno = new MaterialSeparacao
    {
      EstruturaItemId = original.EstruturaItemId, MaterialId = original.MaterialId, Tipo = TiposDeSeparacao.Estorno,
      Quantidade = original.Quantidade, EstornoDeId = original.Id, DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
    };
    // A leitura acima nao fecha a corrida: dois estornos simultaneos passam por ela, e o indice unico
    // recusa o segundo na gravacao.
    if (!await _separacao.GravarEstornoAsync(estorno, ct)) return await JaEstornadaAsync(original, ct);

    var material = (await _separacao.ListarMateriaisAsync([estorno.MaterialId], ct)).Single();
    return Result<SeparacaoDto>.Ok(
        ProjecaoDaSeparacao.Linha(estorno, material, await NomeDoUsuarioAsync(usuarioId, ct), estornavel: false));
  }

  private async Task<Result<SeparacaoDto>> JaEstornadaAsync(MaterialSeparacao original, CancellationToken ct) =>
      Falhas.Conflito<SeparacaoDto>(CodigosDaSeparacao.JaEstornada,
          $"A separação de {await DoRegistroAsync(original, ct)} já foi estornada.");

  /// <summary>"PAR-M8 (Parafuso M8) do nó Quadro": o material e o no de uma linha do livro, para a frase.</summary>
  private async Task<string> DoRegistroAsync(MaterialSeparacao linha, CancellationToken ct)
  {
    var catalogo = (await _separacao.ListarMateriaisAsync([linha.MaterialId], ct)).ToDictionary(m => m.Id);
    return $"{Nome(linha.MaterialId, catalogo)} {await DoNoAsync(linha.EstruturaItemId, ct)}";
  }

  /// <summary>"do nó Quadro": o nome do no (<see cref="NomeDoNoAsync"/>) para a frase.</summary>
  private async Task<string> DoNoAsync(int noId, CancellationToken ct) => $"do nó {await NomeDoNoAsync(noId, ct)}";

  /// <summary>
  /// O nome do no como a tela o mostra, pela regra 19: a descricao propria e, sem ela, a do Componente. E o
  /// mesmo <see cref="LeitorDeEstado"/> da lista do Separador, para a regra morar num lugar so. Serve a
  /// frase de um 409 e ao prefixo da recusa do lote, que sao o caminho de recusa, nao o quente.
  /// </summary>
  private async Task<string> NomeDoNoAsync(int noId, CancellationToken ct)
  {
    var nos = await _execucao.ListarNosAsync([noId], ct);
    return (await _leitor.CarregarAsync(nos, ct)).Nome(noId);
  }

  private async Task<string> NomeDoUsuarioAsync(int usuarioId, CancellationToken ct) =>
      (await _execucao.ListarNomesDeUsuariosAsync([usuarioId], ct)).GetValueOrDefault(usuarioId, string.Empty);

  /// <summary>Codigo e descricao para a frase; o Id quando o material nem existe no catalogo.</summary>
  private static string Nome(int materialId, IReadOnlyDictionary<int, MaterialDaSeparacao> catalogo) =>
      catalogo.TryGetValue(materialId, out var m) ? $"{m.Codigo} ({m.Descricao})" : materialId.ToString();
}
