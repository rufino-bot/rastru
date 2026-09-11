using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IEstruturaRepository
{
  /// <summary>
  /// A receita do catalogo INTEIRA, em tres lookups. E leitura solta e larga de proposito: a
  /// alternativa e uma consulta por no durante a descida, que faz N+1 numa arvore que pode ter
  /// centenas de nos. O catalogo e pequeno (54 Componentes na massa de demonstracao) e cresce por
  /// cadastro humano, nao por producao.
  /// </summary>
  Task<(IReadOnlyList<(int Pai, int Filho, decimal Qtd)> Filhos,
        IReadOnlyList<(int Comp, int Material, decimal Qtd)> Materiais,
        IReadOnlyList<(int Comp, int Setor, int Ordem)> Roteiro)>
      LerReceitaCompletaAsync(CancellationToken ct);

  /// <summary>
  /// Grava a arvore inteira numa transacao. Arvore toda ou nada. Devolve o Id gerado da RAIZ —
  /// desvio deliberado do `Task` (void) do brief da Task 3: sem o Id de volta, o caso de uso nao
  /// tem como montar o `EstruturaItemDto` de retorno (o Agrupamento pode ja ter outras Pecas, entao
  /// "a ultima gravada" nao e identificavel sem esta informacao). Registrado no relatorio da Task 3.
  /// </summary>
  Task<int> GravarArvoreAsync(
      int agrupamentoId, int? estruturaPaiId, NoParaGravar raiz, CancellationToken ct);

  Task<IReadOnlyList<EstruturaItem>> ListarDoAgrupamentoAsync(int agrupamentoId, CancellationToken ct);

  /// <summary>RASTREADA (sem AsNoTracking): a Task 4 edita e exclui a partir deste retorno.</summary>
  Task<EstruturaItem?> ObterPorIdAsync(int id, CancellationToken ct);

  Task<IReadOnlyList<EstruturaMaterial>> ListarMateriaisAsync(IReadOnlyList<int> itemIds, CancellationToken ct);

  /// <summary>Ja vem ordenado por `Ordem`: quem monta a arvore desenha a sequencia direto.</summary>
  Task<IReadOnlyList<EstruturaRoteiro>> ListarRoteiroAsync(IReadOnlyList<int> itemIds, CancellationToken ct);

  /// <summary>
  /// Apaga o no `id` e toda a subarvore dele (Task 4 — correcao de montagem, nao descarte). Ordem
  /// exigida pelas FKs: `EstruturaMaterial`/`EstruturaRoteiro` de cada no antes do proprio no, e
  /// filhos antes de pais (a FK self-referenciada em `EstruturaPaiId` exige). Nao-op se `id` nao
  /// existir mais.
  /// </summary>
  Task RemoverSubarvoreAsync(int id, CancellationToken ct);

  Task SalvarAlteracoesAsync(CancellationToken ct);
}

/// <summary>Espelho de `NoPlanejado` na fronteira do dominio, para a Application nao vazar tipo.</summary>
public sealed record NoParaGravar(
    int? ComponenteId, string? Descricao, decimal Quantidade, bool RequerRelatorioDimensional,
    IReadOnlyList<(int MaterialId, decimal Quantidade)> Materiais,
    IReadOnlyList<(int SetorId, int Ordem)> Roteiro,
    IReadOnlyList<NoParaGravar> Filhos);
