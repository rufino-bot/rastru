using Rastreamento.Application.Common;
using Rastreamento.Application.Estrutura;
using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Retrabalho;

/// <summary>
/// Planeja a copia da arvore REAL de uma Peca de origem para o Pedido de Retrabalho (secao 4.3 da spec
/// da Fase 5B-2). PURO: sem I/O; quem le a arvore e grava o resultado e o caso de uso que abre o
/// Retrabalho. Cada no copia ComponenteId, Descricao, QuantidadePorPai e Roteiro; so a Peca copia
/// RequerRelatorioDimensional. A quantidade de cada no e a do pai (ja escalada) vezes a razao do no com
/// o pai na origem, e a de cada material acompanha a razao do proprio no, arredondada como a coluna.
/// Com <c>soAPeca</c> (consertar, secao 4.7) copia so a Peca: sem filhos e sem materiais.
/// </summary>
public static class CopiaDaPeca
{
  private const int CasasDaColuna = 4;

  /// <param name="saidosDoProjeto">Ids de nos que sairam do projeto (baixa `Descarte` do no inteiro):
  /// o no e a subarvore dele nao entram na copia.</param>
  public static Result<NoParaGravar> Planejar(
      IReadOnlyList<NoDaCopia> arvore, int pecaId, decimal quantidade,
      IReadOnlySet<int> saidosDoProjeto, bool soAPeca)
  {
    var porId = arvore.ToDictionary(n => n.Id);
    if (!porId.TryGetValue(pecaId, out var peca)) return Falhas.NaoEncontrado<NoParaGravar>();

    // A Peca que ela propria saiu do projeto nao tem o que repor: nao ha o que copiar (409).
    if (saidosDoProjeto.Contains(pecaId))
      return Falhas.Conflito<NoParaGravar>(CodigosDoRetrabalho.SemPecaARepor,
          "Esta Peça saiu do projeto, não há o que repor.");

    var filhosDe = arvore.Where(n => n.PaiId is not null).ToLookup(n => n.PaiId!.Value);
    var nos = 0;

    try
    {
      var raiz = Descer(peca, quantidade, soAPeca, ehPeca: true, filhosDe, saidosDoProjeto, 1, ref nos);
      return Result<NoParaGravar>.Ok(raiz);
    }
    catch (QuantidadeForaDaColunaException e)
    {
      return Falhas.Validacao<NoParaGravar>(CodigosDaExecucao.QuantidadeInvalida, e.Message);
    }
    catch (EstruturaRecusadaException e)
    {
      return Falhas.Conflito<NoParaGravar>(e.Codigo, e.Message);
    }
  }

  private static NoParaGravar Descer(
      NoDaCopia original, decimal quantidade, bool soAPeca, bool ehPeca,
      ILookup<int, NoDaCopia> filhosDe,
      IReadOnlySet<int> saidos, int profundidade, ref int nos)
  {
    quantidade = Math.Round(quantidade, CasasDaColuna, MidpointRounding.AwayFromZero);
    PlanejadorDeCopia.ConferirFaixaDaColuna(quantidade, $"o item {original.Id}");

    if (profundidade > PlanejadorDeCopia.ProfundidadeMaxima)
      throw new EstruturaRecusadaException(PlanejadorDeCopia.CodigoDeProfundidade,
          $"A estrutura passa de {PlanejadorDeCopia.ProfundidadeMaxima} níveis de profundidade.");
    if (++nos > PlanejadorDeCopia.NosMaximos)
      throw new EstruturaRecusadaException(PlanejadorDeCopia.CodigoDeTamanho,
          $"A estrutura passa de {PlanejadorDeCopia.NosMaximos} itens.");

    var materiais = new List<(int MaterialId, decimal Quantidade)>();
    var filhos = new List<NoParaGravar>();

    if (!soAPeca)
    {
      var razaoDoNo = original.Quantidade == 0 ? 0m : quantidade / original.Quantidade;
      foreach (var m in original.Materiais)
      {
        var q = Math.Round(m.Quantidade * razaoDoNo, CasasDaColuna, MidpointRounding.AwayFromZero);
        PlanejadorDeCopia.ConferirFaixaDaColuna(q, $"o material {m.MaterialId} do item {original.Id}");
        materiais.Add((m.MaterialId, q));
      }

      foreach (var f in filhosDe[original.Id])
      {
        if (saidos.Contains(f.Id)) continue;
        var razao = f.QuantidadePorPai ?? (original.Quantidade == 0 ? 0m : f.Quantidade / original.Quantidade);
        filhos.Add(Descer(f, quantidade * razao, false, false, filhosDe, saidos, profundidade + 1, ref nos));
      }
    }

    return new NoParaGravar(
        original.ComponenteId, original.Descricao, quantidade,
        RequerRelatorioDimensional: ehPeca && original.RequerRelatorioDimensional,
        Materiais: materiais,
        Roteiro: original.Roteiro.OrderBy(r => r.Ordem).ToList(),
        Filhos: filhos,
        QuantidadePorPai: original.QuantidadePorPai,
        OrigemEstruturaItemId: original.Id);
  }

  private sealed class EstruturaRecusadaException(string codigo, string mensagem) : Exception(mensagem)
  {
    public string Codigo { get; } = codigo;
  }
}
