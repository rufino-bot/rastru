using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Estrutura;

/// <summary>
/// Le a arvore inteira de um Agrupamento (todas as Pecas e seus descendentes) e monta os DTOs, com
/// nome de Material/Setor e Codigo/Descricao de Componente resolvidos em lote — nunca um lookup por
/// no, que faria N+1 numa arvore de centenas de nos.
///
/// EXTRAIDO de <c>MontagemDeEstruturaUseCase</c> pela Task 4, condicao ja anotada na review da
/// Task 3: "se `Montar` precisar de mais uma resolucao, a funcao local vira o ponto de dor... a
/// extracao natural, quando a Task 4 chegar, e um `MontadorDeArvoreDeEstrutura` em arquivo proprio,
/// deixando o caso de uso com as decisoes de negocio." A Task 4 e quem cria o segundo e o terceiro
/// consumidor (`AcrescentarFilho`, `EditarNo`, alem do `ObterArvore`/`CriarPeca` ja existentes) —
/// condicao que faltava para a extracao deixar de ser abstracao prematura.
///
/// Comportamento-preservador: o corpo e o mesmo de antes, so movido de arquivo e de "funcao privada
/// do caso de uso" para "metodo publico desta classe". Os testes de `ObterArvoreTests` e
/// `CriarPecaTests` continuam passando sem alteracao nenhuma — ver o relatorio da Task 4.
/// </summary>
internal sealed class MontadorDeArvoreDeEstrutura
{
  private readonly IEstruturaRepository _estruturas;
  private readonly IReceitaPadraoRepository _catalogo;

  public MontadorDeArvoreDeEstrutura(IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo)
  {
    _estruturas = estruturas;
    _catalogo = catalogo;
  }

  public async Task<IReadOnlyList<EstruturaItemDto>> MontarAsync(int agrupamentoId, CancellationToken ct)
  {
    var itens = await _estruturas.ListarDoAgrupamentoAsync(agrupamentoId, ct);
    if (itens.Count == 0) return [];

    var ids = itens.Select(i => i.Id).ToList();
    var materiais = await _estruturas.ListarMateriaisAsync(ids, ct);
    var roteiros = await _estruturas.ListarRoteiroAsync(ids, ct);

    var componenteIds = itens.Where(i => i.ComponenteId.HasValue)
        .Select(i => i.ComponenteId!.Value).Distinct().ToList();
    var materialIds = materiais.Select(m => m.MaterialId).Distinct().ToList();
    var setorIds = roteiros.Select(r => r.SetorId).Distinct().ToList();

    var componentes = (await _catalogo.ObterComponentesPorIdAsync(componenteIds, ct))
        .ToDictionary(c => c.Id);
    var nomesMateriais = (await _catalogo.ObterMateriaisPorIdAsync(materialIds, ct))
        .ToDictionary(m => m.Id);
    var nomesSetores = (await _catalogo.ObterSetoresPorIdAsync(setorIds, ct))
        .ToDictionary(s => s.Id);

    var materiaisPorItem = materiais.ToLookup(m => m.EstruturaItemId);
    var roteirosPorItem = roteiros.ToLookup(r => r.EstruturaItemId);
    var filhosPorPai = itens.ToLookup(i => i.EstruturaPaiId);

    // Minor 2 da review da Task 3: nada ordenava filhos/materiais (so o roteiro, por `Ordem`, que e
    // sequencia de negocio). `ToLookup` preserva a ordem de CHEGADA de `itens`/`materiais`, que e a
    // ordem que o SQL Server devolver — nao garantida entre chamadas sem `ORDER BY` (heap sem
    // indice clusterizado). A ordenacao entra aqui, explicita por `Id`, em vez de confiar so no
    // repositorio: e o que o teste
    // `Filhos_e_materiais_saem_ordenados_por_Id_mesmo_que_o_repositorio_devolva_fora_de_ordem`
    // prova, alimentando o fake fora de ordem de proposito — um teste de Infra contra SQL Server
    // real ficaria verde de qualquer jeito nesta tabela pequena (o heap tende a devolver em ordem
    // de insercao), o que o tornaria fraco.
    var visitados = new HashSet<int>();

    EstruturaItemDto Montar(EstruturaItem item)
    {
      visitados.Add(item.Id);

      string? codigo = null;
      var descricao = item.Descricao;
      if (item.ComponenteId is int componenteId && componentes.TryGetValue(componenteId, out var componente))
      {
        codigo = componente.Codigo;
        descricao ??= componente.Descricao;   // regra 19: NULL herda a descricao do Componente
      }
      descricao ??= string.Empty;

      var listaDeMateriais = materiaisPorItem[item.Id]
          .OrderBy(m => m.Id)
          .Select(m => new MaterialDoNoDto(
              m.MaterialId,
              nomesMateriais.TryGetValue(m.MaterialId, out var material) ? material.Descricao : string.Empty,
              m.Quantidade))
          .ToList();

      var listaDeRoteiro = roteirosPorItem[item.Id]
          .OrderBy(r => r.Ordem)
          .Select(r => new PassoDoRoteiroDto(
              r.SetorId,
              nomesSetores.TryGetValue(r.SetorId, out var setor) ? setor.Nome : string.Empty,
              r.Ordem))
          .ToList();

      var filhos = filhosPorPai[item.Id].OrderBy(f => f.Id).Select(Montar).ToList();

      return new EstruturaItemDto(
          item.Id, item.ComponenteId, codigo, descricao, item.Quantidade,
          item.NivelHierarquico, item.RequerRelatorioDimensional, listaDeMateriais, listaDeRoteiro, filhos);
    }

    var raizes = filhosPorPai[null].OrderBy(i => i.Id).Select(Montar).ToList();

    // Minor 3 da review da Task 3: um no cujo EstruturaPaiId aponta para fora do proprio
    // Agrupamento nunca e visitado por `Montar` (que so desce a partir de `filhosPorPai[null]`) —
    // sem esta checagem ele SOME da arvore devolvida, sem erro, sem log: perda de dado silenciosa
    // numa LEITURA. Hoje nao deveria acontecer (toda gravacao usa o mesmo AgrupamentoId,
    // `EstruturaRepository.GravarNo`), mas o schema nao impede a inconsistencia — lancar aqui torna
    // o caso AUDIVEL em vez de invisivel, sem inventar recuperacao: quem le a arvore incompleta sem
    // saber vira o operador, e isso e pior que um 500.
    if (visitados.Count != itens.Count)
    {
      var orfaos = itens.Where(i => !visitados.Contains(i.Id)).Select(i => i.Id);
      throw new ArvoreInconsistenteException(
          $"A arvore do Agrupamento {agrupamentoId} tem {itens.Count - visitados.Count} no(s) "
              + "orfao(s) — EstruturaPaiId aponta para fora dos itens lidos deste Agrupamento: "
              + $"{string.Join(", ", orfaos)}.");
    }

    return raizes;
  }
}

/// <summary>
/// Dado inconsistente na leitura da arvore: um <see cref="EstruturaItem"/> cujo
/// <c>EstruturaPaiId</c> nao esta entre os itens lidos do proprio Agrupamento. Ver o comentario em
/// <c>MontadorDeArvoreDeEstrutura.MontarAsync</c> (Minor 3 da review da Task 3) para o porque de
/// virar excecao em vez de o no simplesmente desaparecer da arvore devolvida.
/// </summary>
public sealed class ArvoreInconsistenteException(string mensagem) : Exception(mensagem);
