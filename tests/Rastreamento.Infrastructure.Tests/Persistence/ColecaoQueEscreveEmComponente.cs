using Xunit;

namespace Rastreamento.Infrastructure.Tests.Persistence;

/// <summary>
/// Serializa entre si as classes de teste que ESCREVEM em <c>dbo.Componente</c>.
///
/// Por que existe: o xUnit roda classes de teste em paralelo por default, e o banco e um so
/// (SQL Server de dev, compartilhado). <c>ComponenteMappingTests.Busca_em_branco_nao_filtra_nada</c>
/// compara o <c>Total</c> de duas consultas seguidas SEM escopo de prefixo — uma prova que so vale
/// se a tabela nao mudar entre elas. Assim que uma segunda classe passou a inserir e apagar
/// Componentes no mesmo processo, ela passou a falhar de forma intermitente, nos DOIS sentidos:
/// medido <c>40 → 41</c> (insert concorrente) e <c>42 → 39</c> (limpeza concorrente caindo no meio).
/// Taxa medida antes desta colecao: 4 vermelhas em 10 execucoes da suite de Infrastructure.
///
/// A alternativa seria desligar o paralelismo do assembly inteiro; nao vale — o resto da suite
/// (Security, RefreshToken, os outros mapeamentos) nao toca esta tabela e nao precisa pagar.
///
/// Classe nova que escreva em <c>dbo.Componente</c> entra aqui com
/// <c>[Collection(ColecaoQueEscreveEmComponente.Nome)]</c>.
/// </summary>
[CollectionDefinition(Nome)]
public class ColecaoQueEscreveEmComponente
{
  public const string Nome = "escritores de dbo.Componente";
}
