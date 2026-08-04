namespace Rastreamento.Domain.Abstractions;

public interface IPasswordHasher
{
  string Hash(string senhaPlano);
  bool Verificar(string senhaPlano, string senhaHash);

  /// <summary>
  /// Hash de uma senha que ninguem conhece, com exatamente o mesmo custo de um hash de
  /// producao. Serve para o login gastar o mesmo tempo quando o usuario nao existe ou esta
  /// inativo: sem isso, <c>Verificar</c> nem chega a rodar nesse caminho e a resposta volta
  /// ~100ms mais cedo — um oraculo de timing que nenhuma comparacao de corpo de resposta pega.
  /// Verificar qualquer senha contra ele sempre devolve <c>false</c>.
  /// </summary>
  string HashFicticio { get; }
}
