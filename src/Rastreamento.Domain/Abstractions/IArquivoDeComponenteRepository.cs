using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Nome e tamanho do solido de um Componente, SEM o <c>Conteudo</c> -- para telas que precisam
/// mostrar "ja existe X, N bytes" sem tocar no <c>VARBINARY(MAX)</c> (§5.2/§7.1 da spec de Fase
/// 2B). Vive junto da interface porque so ela produz este tipo, mesmo padrao de
/// <see cref="FiltroDeComponente"/> em <see cref="IComponenteRepository"/>.
/// </summary>
public sealed record MetadadoDeSolido(string NomeOriginal, int TamanhoEmBytes);

public interface IArquivoDeComponenteRepository
{
  /// <summary>
  /// Grava o arquivo e aponta <c>Componente.ArquivoSolidoId</c> para ele, numa UNICA transacao.
  /// Devolve o Id do arquivo gravado, ou <c>null</c> quando <paramref name="componenteId"/> nao
  /// corresponde a nenhum Componente -- o caso de uso traduz o null para 404, em vez de deixar
  /// subir a excecao crua que uma consulta que exige exatamente uma linha lançaria. Nesse caminho
  /// NADA e gravado: a checagem da existencia do componente acontece antes do primeiro
  /// <c>SaveChanges</c>, entao nao ha arquivo orfao para a transacao desfazer.
  ///
  /// <para>
  /// Precisa de transacao porque sao dois <c>SaveChanges</c>: o Id do arquivo so existe depois do
  /// primeiro, e sem navegacao (deliberado) o EF nao resolve a ligacao sozinho. Meio-termo —
  /// arquivo gravado sem ninguem apontando para ele — nao e estado alcancavel.
  /// </para>
  ///
  /// <para>
  /// Substituicao: o solido anterior do Componente, se houver, deixa de ser referenciado e
  /// PERMANECE na tabela. Consequencia aceita nesta fase — vira historico, nao lixo coletado: nao
  /// existe remocao de solido (a spec registra por que), e apagar o antigo aqui perderia o unico
  /// registro de que a peca teve outra geometria.
  /// </para>
  /// </summary>
  Task<int?> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct);

  /// <summary>
  /// O solido do Componente, COM o blob. Null quando o componente nao existe ou nao tem solido —
  /// o caso de uso traduz os dois para 404, porque a diferenca nao muda o que o cliente faz.
  /// </summary>
  Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(int componenteId, CancellationToken ct);

  /// <summary>
  /// Nome e tamanho do solido, SEM o <c>Conteudo</c> — projecao explicita, nunca <c>Include</c>
  /// (nao ha navegacao para <c>ArquivoDeComponente</c>, por desenho: ver
  /// <see cref="Componente.ArquivoSolidoId"/>). Mesmo colapso de
  /// <see cref="ObterSolidoDoComponenteAsync"/>: <c>null</c> quando o componente nao existe OU nao
  /// tem solido.
  /// </summary>
  Task<MetadadoDeSolido?> ObterMetadadoDoSolidoAsync(int componenteId, CancellationToken ct);
}
