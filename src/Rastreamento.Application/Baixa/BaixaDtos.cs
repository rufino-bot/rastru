using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Baixa;

/// <summary>Uma posicao de onde a baixa de parte tira, e quanto (D4 da spec da Fase 5B-1).</summary>
public sealed record OrigemDaBaixa(Local Local, decimal Quantidade);

/// <summary>
/// O que a conta da baixa recebe. `Origens` so na baixa de parte; vazia com `Tudo`. `IncluirTopo` so na baixa de um
/// Item: de parte (D13), ou de tudo com motivo `PerdaArmazem` ou `MortaEmProcesso` (D14); de tudo por `Descarte`, nao.
/// `CadeiaAteOTopo`: os ancestrais do no, do pai ate a Peca, vazia quando o no e Peca.
/// </summary>
public sealed record PedidoDeBaixa(
    int NoId, string Motivo, bool Tudo, IReadOnlyList<OrigemDaBaixa> Origens, bool IncluirTopo,
    IReadOnlyList<int> CadeiaAteOTopo);

/// <summary>Uma linha do livro que a baixa vai gravar: de `Origem` para `Perdido`.</summary>
public sealed record LinhaPlanejada(int NoId, Local Origem, decimal Quantidade);

/// <summary>
/// As unidades da Peca do topo que a perda de um Item trava (secao 4.6 da spec da Fase 5B-1), e quantas dela ainda estao
/// a iniciar, que e de onde `IncluirTopo` tira.
/// </summary>
public sealed record TravaNoTopo(int PecaId, decimal Quantidade, decimal AIniciar);

/// <summary>
/// O que a baixa faz: as linhas, na ordem em que se gravam; os nos cujo fabricado passa a sobrar por causa dela (C6);
/// e a trava no topo, quando a baixa e de um Item (de parte, ou de tudo por perda) e trava alguma unidade da Peca.
/// </summary>
public sealed record PlanoDaBaixa(
    IReadOnlyList<LinhaPlanejada> Linhas, IReadOnlyList<(int NoId, decimal Quantidade)> ViramSobra, TravaNoTopo? Trava);
