using Rastreamento.Domain.Entities;

namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Nome e tamanho do solido de um Componente, SEM o <c>Conteudo</c> -- para telas que precisam
/// mostrar "ja existe X, N bytes" sem tocar no <c>VARBINARY(MAX)</c> (§5.2/§7.1 da spec de Fase
/// 2B). Vive junto da interface porque so ela produz este tipo, mesmo padrao de
/// <see cref="FiltroDeComponente"/> em <see cref="IComponenteRepository"/>.
/// </summary>
public sealed record MetadadoDeSolido(string NomeOriginal, int TamanhoEmBytes);

/// <summary>
/// Desfecho da miniatura avulsa (C15 da spec da miniatura do solido). Vive junto da interface
/// porque so ela o produz, no mesmo padrao de <see cref="MetadadoDeSolido"/>.
/// </summary>
public enum ResultadoDaMiniaturaAvulsa
{
  Gravada,
  ComponenteInexistente,
  SemSolido,
  /// <summary>O <c>Sha256</c> do solido atual difere do de que a miniatura foi desenhada.</summary>
  SolidoMudou,
}

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
  /// primeiro, e sem navegacao (deliberado) o EF nao resolve a ligacao sozinho. O que a transacao
  /// torna inalcancavel e o meio-termo de UMA chamada que falha entre os dois <c>SaveChanges</c>;
  /// arquivo sem ninguem apontando para ele continua existindo por outro caminho, e por desenho —
  /// o anterior de toda substituicao e, em duas substituicoes simultaneas, o de quem perdeu.
  /// </para>
  ///
  /// <para>
  /// Substituicao: o solido anterior do Componente, se houver, deixa de ser referenciado e
  /// PERMANECE na tabela. Consequencia aceita nesta fase — vira historico, nao lixo coletado: nao
  /// existe remocao de solido (a spec registra por que), e apagar o antigo aqui perderia o unico
  /// registro de que a peca teve outra geometria.
  /// </para>
  ///
  /// <para>
  /// Miniatura (C4 e C6 da spec da miniatura do solido): <paramref name="miniatura"/>, quando vem,
  /// e gravada na MESMA transacao e <c>Componente.ArquivoMiniaturaId</c> passa a apontar para ela;
  /// quando nao vem, <c>ArquivoMiniaturaId</c> vai a <c>null</c>, tambem na mesma transacao --
  /// nunca fica a miniatura do solido antigo ao lado do solido novo. A miniatura anterior segue a
  /// politica do solido: deixa de ser referenciada e PERMANECE na tabela.
  /// </para>
  /// </summary>
  Task<int?> GravarEVincularComoSolidoAsync(
      int componenteId, ArquivoDeComponente solido, ArquivoDeComponente? miniatura,
      CancellationToken ct);

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

  /// <summary>
  /// A miniatura do Componente, COM o blob e o <c>Sha256</c> (a versao). Mesmo colapso de
  /// <see cref="ObterSolidoDoComponenteAsync"/>: <c>null</c> quando o componente nao existe OU nao
  /// tem miniatura.
  /// </summary>
  Task<ArquivoDeComponente?> ObterMiniaturaDoComponenteAsync(int componenteId, CancellationToken ct);

  /// <summary>
  /// Grava a miniatura SEM tocar no solido e aponta <c>Componente.ArquivoMiniaturaId</c> para ela
  /// (C9). Na MESMA transacao le o Componente e o <c>Sha256</c> do solido atual e o compara com
  /// <paramref name="sha256DoSolido"/>: so grava se forem iguais, o que fecha a corrida de C15 (um
  /// solido novo enviado entre o desenho e a gravacao ficaria com a miniatura do antigo). A
  /// miniatura anterior deixa de ser referenciada e PERMANECE na tabela.
  /// </summary>
  Task<ResultadoDaMiniaturaAvulsa> GravarMiniaturaAvulsaAsync(
      int componenteId, ArquivoDeComponente miniatura, byte[] sha256DoSolido, CancellationToken ct);
}
