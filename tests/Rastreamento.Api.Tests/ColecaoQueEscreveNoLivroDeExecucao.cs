using Xunit;

namespace Rastreamento.Api.Tests;

/// <summary>
/// Serializa entre si as classes de teste que ESCREVEM no livro de execucao (`dbo.EstruturaRoteiro`,
/// `dbo.Movimentacao`, `dbo.Montagem`), pelo mesmo motivo e padrao de
/// `ColecaoQueEscreveEmComponente` (`Rastreamento.Infrastructure.Tests`, ver o `CLAUDE.md`): o xUnit
/// roda classes de teste em paralelo por default, e o SQL Server de dev e um so.
///
/// Medido nesta task (Task 11): `ExecucaoEndpointsTests` e `CriterioDeProntoDaFase3Tests`, sozinhas,
/// passam de forma reproduzivel (3/3 cada); rodadas juntas (filtro comum, sem esta colecao), 2 das 3
/// vezes uma delas falhou com 409 `ConflitoDeConcorrencia` numa escrita de ARRANJO (`PUT roteiro` ou
/// `POST entregas`) contra um no que a OUTRA classe nem toca — nao e contencao no mesmo no, e sim
/// range lock do SERIALIZABLE (`IExecucaoRepository.EmTransacaoAsync`, spec 8.1) sobre indice esparso
/// (`EstruturaRoteiro`/`Movimentacao`/`Montagem` tem poucas linhas no banco de teste, entao o "gap"
/// de proximo-chave entre duas linhas existentes cobre Ids de OUTRAS transacoes concorrentes). Nenhuma
/// outra classe de `Api.Tests` escreve nestas tabelas (so as rotas de Apontamento/Entrega/Estorno/
/// Roteiro tocam nelas, e so estas duas classes as exercitam), entao esta colecao basta: nao ha
/// contencao alem dela para resolver, e o resto da suite nao paga o custo de rodar serializado.
///
/// Classe nova que escreva nessas tabelas (via as rotas de execucao) entra aqui com
/// `[Collection(ColecaoQueEscreveNoLivroDeExecucao.Nome)]`.
/// </summary>
[CollectionDefinition(Nome)]
public class ColecaoQueEscreveNoLivroDeExecucao
{
  public const string Nome = "escritores do livro de execucao";
}
