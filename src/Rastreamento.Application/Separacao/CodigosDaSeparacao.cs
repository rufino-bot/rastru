namespace Rastreamento.Application.Separacao;

/// <summary>
/// Os codigos estaveis do campo `erro` da separacao de material (spec da Fase 4, regra 34). Os reusados
/// da execucao (`QuantidadeInvalida`, `Proibido`) ficam em `CodigosDaExecucao`, e o do "Fica em" do
/// Material (`SetorDeEstoqueInvalido`) e do cadastro, nao daqui. `NoComSeparacao` e o de `ExcluirNo`
/// (decisao P2 do plano de backend da Fase 4).
/// </summary>
public static class CodigosDaSeparacao
{
  public const string MaterialForaDoPlanejado = nameof(MaterialForaDoPlanejado);
  public const string JaEstornada = nameof(JaEstornada);
  public const string EstornoDeEstorno = nameof(EstornoDeEstorno);
  public const string SeparacaoVazia = nameof(SeparacaoVazia);
  public const string MaterialRepetido = nameof(MaterialRepetido);
  public const string MaterialInvalido = nameof(MaterialInvalido);
  public const string NoComSeparacao = nameof(NoComSeparacao);
}
