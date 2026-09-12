using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

public interface IArquivoDeComponenteRepository
{
  /// <summary>
  /// Grava o arquivo e aponta <c>Componente.ArquivoSolidoId</c> para ele, numa UNICA transacao.
  /// Devolve o Id do arquivo gravado.
  ///
  /// <para>
  /// Precisa de transacao porque sao dois <c>SaveChanges</c>: o Id do arquivo so existe depois do
  /// primeiro, e sem navegacao (deliberado) o EF nao resolve a ligacao sozinho. Meio-termo —
  /// arquivo gravado sem ninguem apontando para ele — nao e estado alcancavel. Mesmo molde de
  /// transacao explicita de <c>SubstituirFilhosAsync</c>.
  /// </para>
  ///
  /// <para>
  /// Substituicao: o solido anterior do Componente, se houver, deixa de ser referenciado e
  /// PERMANECE na tabela. Consequencia aceita nesta fase — vira historico, nao lixo coletado: nao
  /// existe remocao de solido (a spec registra por que), e apagar o antigo aqui perderia o unico
  /// registro de que a peca teve outra geometria.
  /// </para>
  /// </summary>
  Task<int> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente arquivo, CancellationToken ct);

  /// <summary>
  /// O solido do Componente, COM o blob. Null quando o componente nao existe ou nao tem solido —
  /// o caso de uso traduz os dois para 404, porque a diferenca nao muda o que o cliente faz.
  /// </summary>
  Task<ArquivoDeComponente?> ObterSolidoDoComponenteAsync(int componenteId, CancellationToken ct);
}
