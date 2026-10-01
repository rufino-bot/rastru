using System.Reflection;

// Desliga a paralelizacao entre classes de teste NESTE assembly. O motivo e o banco: as classes de
// Api.Tests escrevem no mesmo SQL Server sob SERIALIZABLE, e classes paralelas do mesmo assembly
// deadlockam entre si, inclusive em nos sem relacao, pelo range lock de fim de indice (emenda de
// 2026-09-26 da secao 8.1 da spec da Fase 3). O `-m:1` so serializa PROJETOS de teste, nao as classes
// de dentro de um deles.
//
// Medido em 2026-10-01, em sessao de nuvem com 4 nucleos, nos binarios da main (1594c23), com
// `dotnet test Rastreamento.slnx -m:1`: 1 execucao vermelha em 10 com o paralelismo padrao, e 2 em 10
// com `-- xUnit.MaxParallelThreads=16` (nao se verificou que o parametro surtiu efeito, entao e uma
// amostra so: 3 em 20). Uma das falhas foi um deadlock 1205 na limpeza do proprio teste,
// `CenarioDaFase3NaApi.DisposeAsync`; as outras duas, 409 ConflitoDeConcorrencia com o retry esgotado.
//
// Por que nao uma [Collection] seletiva: ela depende de lembrar de por cada classe nova nela, e
// esquecer falha em silencio, com vermelho intermitente semanas depois. E o ciclo medido e entre nos
// sem relacao, entao nao ha recorte de "classes que disputam" que se possa enumerar.
//
// Nao muda a concorrencia DENTRO de um teste (CorridaNaQueimaDeFamiliaTests, as corridas da execucao):
// o atributo serializa colecoes (classes), nao as Tasks que um teste lanca.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rastreamento.Api.Tests;

/// <summary>
/// Guarda do atributo de assembly que desliga a paralelizacao entre classes de teste deste projeto.
/// Sem este teste, tirar o atributo deixaria a suite verde na maioria das execucoes, e a regressao
/// so apareceria como vermelho intermitente.
/// </summary>
public class ParalelizacaoDaSuiteDeApiTests
{
  [Fact]
  public void Paralelizacao_entre_classes_fica_desligada_neste_assembly()
  {
    var atributo = typeof(ParalelizacaoDaSuiteDeApiTests).Assembly
        .GetCustomAttribute<CollectionBehaviorAttribute>();
    Assert.NotNull(atributo);
    Assert.True(atributo!.DisableTestParallelization);
  }
}
