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

  public SeparacaoUseCase(ISeparacaoRepository separacao, IExecucaoRepository execucao)
  {
    _separacao = separacao;
    _execucao = execucao;
  }

  /// <summary>
  /// Tudo ou nada. A ordem das validacoes e a da decisao P8 do plano de backend da Fase 4: no (404), lista
  /// vazia, quantidade, material repetido, fora do planejado (409). Acima do planejado e aceito e volta
  /// marcado (D9 da spec da Fase 4); o material que fica num Setor e aceito se estiver no planejado (D16).
  /// </summary>
  public async Task<Result<ResultadoDaSeparacaoDto>> Separar(
      int noId, SeparacaoNovaDto dto, int usuarioId, CancellationToken ct)
  {
    // O 404 vem antes de olhar o corpo: o no que nao existe nao tem o que validar.
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<ResultadoDaSeparacaoDto>();

    if (dto.Linhas is not { Count: > 0 } linhas)
      return Falhas.Validacao<ResultadoDaSeparacaoDto>(CodigosDaSeparacao.SeparacaoVazia,
          "Informe ao menos um material para separar.");

    foreach (var linha in linhas)
      if (!Quantidades.CabeNaColuna(linha.Quantidade))
        return Falhas.QuantidadeInvalida<ResultadoDaSeparacaoDto>(linha.Quantidade);

    var ids = linhas.Select(l => l.MaterialId).Distinct().ToList();
    var catalogo = (await _separacao.ListarMateriaisAsync(ids, ct)).ToDictionary(m => m.Id);

    var repetido = linhas.GroupBy(l => l.MaterialId).FirstOrDefault(g => g.Count() > 1);
    if (repetido is not null)
      return Falhas.Validacao<ResultadoDaSeparacaoDto>(CodigosDaSeparacao.MaterialRepetido,
          $"O material {Nome(repetido.Key, catalogo)} aparece mais de uma vez na separação.");

    var planejados = (await _separacao.ListarPlanejadoAsync([noId], ct)).Select(p => p.MaterialId).ToHashSet();
    var fora = linhas.FirstOrDefault(l => !planejados.Contains(l.MaterialId));
    if (fora is not null)
      return Falhas.Conflito<ResultadoDaSeparacaoDto>(CodigosDaSeparacao.MaterialForaDoPlanejado,
          $"O material {Nome(fora.MaterialId, catalogo)} não está no planejado deste nó. "
          + "O PCP o acrescenta nos materiais do nó, se ele for mesmo deste nó.");

    var agora = DateTime.UtcNow;
    var novas = linhas.Select(l => new MaterialSeparacao
    {
      EstruturaItemId = noId, MaterialId = l.MaterialId, Tipo = TiposDeSeparacao.Separacao,
      Quantidade = l.Quantidade, DataHora = agora, UsuarioId = usuarioId,
    }).ToList();
    await _separacao.GravarAsync(novas, ct);

    // Relido depois da escrita: o resumo inclui o que outro Separador gravou no mesmo no ate aqui.
    var planejado = await _separacao.ListarPlanejadoAsync([noId], ct);
    var doLivro = await _separacao.ListarLinhasAsync([noId], ct);
    var doAlmoxarifado = catalogo.Values.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();
    var tocados = ids.ToHashSet();
    var materiais = CalculoDaSeparacao.Resumir(planejado, doLivro, doAlmoxarifado)
        .Where(r => tocados.Contains(r.MaterialId))
        .Select(r => ProjecaoDaSeparacao.Projetar(r, catalogo[r.MaterialId]))
        .OrderBy(i => i.Codigo, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.MaterialId)
        .ToList();

    var nome = await NomeDoUsuarioAsync(usuarioId, ct);
    // Quem acabou de separar e o autor: toda linha nova e estornavel por ele.
    var separacoes = novas.Select(l => ProjecaoDaSeparacao.Linha(l, catalogo[l.MaterialId], nome, estornavel: true)).ToList();
    return Result<ResultadoDaSeparacaoDto>.Ok(new ResultadoDaSeparacaoDto(separacoes, materiais));
  }

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
          "Estorno não se estorna: registre de novo a separação.");
    if (await _separacao.FoiEstornadaAsync(original.Id, ct)) return JaEstornada();

    var estorno = new MaterialSeparacao
    {
      EstruturaItemId = original.EstruturaItemId, MaterialId = original.MaterialId, Tipo = TiposDeSeparacao.Estorno,
      Quantidade = original.Quantidade, EstornoDeId = original.Id, DataHora = DateTime.UtcNow, UsuarioId = usuarioId,
    };
    // A leitura acima nao fecha a corrida: dois estornos simultaneos passam por ela, e o indice unico
    // recusa o segundo na gravacao.
    if (!await _separacao.GravarEstornoAsync(estorno, ct)) return JaEstornada();

    var material = (await _separacao.ListarMateriaisAsync([estorno.MaterialId], ct)).Single();
    return Result<SeparacaoDto>.Ok(
        ProjecaoDaSeparacao.Linha(estorno, material, await NomeDoUsuarioAsync(usuarioId, ct), estornavel: false));
  }

  private static Result<SeparacaoDto> JaEstornada() =>
      Falhas.Conflito<SeparacaoDto>(CodigosDaSeparacao.JaEstornada, "Esta separação já foi estornada.");

  private async Task<string> NomeDoUsuarioAsync(int usuarioId, CancellationToken ct) =>
      (await _execucao.ListarNomesDeUsuariosAsync([usuarioId], ct)).GetValueOrDefault(usuarioId, string.Empty);

  /// <summary>Codigo e descricao para a frase; o Id quando o material nem existe no catalogo.</summary>
  private static string Nome(int materialId, IReadOnlyDictionary<int, MaterialDaSeparacao> catalogo) =>
      catalogo.TryGetValue(materialId, out var m) ? $"{m.Codigo} ({m.Descricao})" : materialId.ToString();
}
