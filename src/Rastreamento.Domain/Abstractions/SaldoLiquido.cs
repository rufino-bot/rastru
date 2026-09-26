namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// Saldo LIQUIDO de uma posicao de um no, lido do livro: soma de `Quantidade` onde ela e destino menos
/// a soma onde e origem (spec da Fase 3, secao 7.1). Na posicao AIniciar ele e zero ou negativo — o
/// saldo de verdade soma a `Quantidade` do no, e quem soma e a calculadora da Application. Vem so
/// posicao com liquido diferente de zero.
/// </summary>
public sealed record SaldoLiquido(int EstruturaItemId, string Posicao, int? SetorId, int? Ordem, decimal Quantidade);
