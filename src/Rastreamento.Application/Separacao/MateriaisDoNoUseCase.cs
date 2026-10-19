using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Separacao;

/// <summary>
/// Os materiais planejados de UM no, editaveis pelo PCP (spec da Fase 4, D8). O `PUT` substitui a lista
/// inteira: `[]` limpa o planejado. Tirar do plano um material que ja tem separacao e permitido — a linha
/// do livro continua e o item volta no `GET` com `NoPlanejado = false` (D14), enquanto o separado liquido
/// for maior que zero (decisao do usuario na conferencia de 2026-10-19). Toda validacao vem antes da
/// unica escrita; a ordem e: no (404), quantidade, material repetido, material existente e ativo.
/// </summary>
public sealed class MateriaisDoNoUseCase
{
  private readonly ISeparacaoRepository _separacao;

  public MateriaisDoNoUseCase(ISeparacaoRepository separacao) => _separacao = separacao;

  public async Task<Result<MateriaisDoNoDto>> Obter(int noId, CancellationToken ct)
  {
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<MateriaisDoNoDto>();
    return Result<MateriaisDoNoDto>.Ok(await ProjetarAsync(noId, ct));
  }

  public async Task<Result<MateriaisDoNoDto>> Substituir(int noId, MateriaisNovosDoNoDto dto, CancellationToken ct)
  {
    // O 404 vem antes de olhar o corpo: o no que nao existe nao tem o que validar.
    if (!await _separacao.NoExisteAsync(noId, ct)) return Falhas.NaoEncontrado<MateriaisDoNoDto>();

    // `[Required]` ja barra o corpo sem `linhas` no MVC; aqui o caso de uso nao confunde ausencia com `[]`,
    // que limpa.
    if (dto.Linhas is null)
      return Falhas.Validacao<MateriaisDoNoDto>(CodigosDaSeparacao.MaterialInvalido,
          "Informe a lista de materiais do nó; use uma lista vazia para limpar.");
    var linhas = dto.Linhas;

    foreach (var linha in linhas)
      if (!Quantidades.CabeNaColuna(linha.Quantidade))
        return Falhas.QuantidadeInvalida<MateriaisDoNoDto>(linha.Quantidade);

    var repetido = linhas.GroupBy(l => l.MaterialId).FirstOrDefault(g => g.Count() > 1);
    if (repetido is not null)
      return Falhas.Validacao<MateriaisDoNoDto>(CodigosDaSeparacao.MaterialRepetido,
          $"O material {repetido.Key} aparece mais de uma vez na lista.");

    var materiais = (await _separacao.ListarMateriaisAsync(linhas.Select(l => l.MaterialId).ToList(), ct))
        .ToDictionary(m => m.Id);
    foreach (var linha in linhas)
    {
      if (!materiais.TryGetValue(linha.MaterialId, out var material))
        return Falhas.Validacao<MateriaisDoNoDto>(CodigosDaSeparacao.MaterialInvalido,
            $"O material {linha.MaterialId} não existe.");
      if (!material.Ativo)
        return Falhas.Validacao<MateriaisDoNoDto>(CodigosDaSeparacao.MaterialInvalido,
            $"O material {material.Codigo} está inativo e não entra no planejado.");
    }

    await _separacao.SubstituirPlanejadoAsync(noId, linhas.Select(l => (l.MaterialId, l.Quantidade)).ToList(), ct);
    return Result<MateriaisDoNoDto>.Ok(await ProjetarAsync(noId, ct));
  }

  private async Task<MateriaisDoNoDto> ProjetarAsync(int noId, CancellationToken ct)
  {
    var planejado = await _separacao.ListarPlanejadoAsync([noId], ct);
    var linhas = await _separacao.ListarLinhasAsync([noId], ct);

    var ids = planejado.Select(p => p.MaterialId).Union(linhas.Select(l => l.MaterialId)).ToList();
    var materiais = (await _separacao.ListarMateriaisAsync(ids, ct)).ToDictionary(m => m.Id);
    var doAlmoxarifado = materiais.Values.Where(m => m.SetorDeEstoqueId is null).Select(m => m.Id).ToHashSet();

    // O material fora do planejado so fica enquanto tem separado liquido: com toda separacao estornada, nao
    // resta nada dele no no, e as linhas continuam no historico (`GET .../separacoes`). O do planejado vem
    // sempre, mesmo com separado zero.
    var itens = CalculoDaSeparacao.Resumir(planejado, linhas, doAlmoxarifado)
        .Where(r => r.NoPlanejado || r.Separado > 0m)
        .Select(r => ProjecaoDaSeparacao.Projetar(r, materiais[r.MaterialId]))
        .OrderBy(i => i.Codigo, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.MaterialId)
        .ToList();
    return new MateriaisDoNoDto(noId, itens);
  }
}
