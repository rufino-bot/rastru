namespace Rastreamento.Application.Tests.Common;

/// <summary>
/// `TimeProvider` parado num instante, para o teste fixar o "hoje". Escrito a mao em vez do
/// `FakeTimeProvider` do pacote de testes da Microsoft: so `GetUtcNow` e usado, e um pacote novo nao
/// se paga por uma linha.
/// </summary>
public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
  public override DateTimeOffset GetUtcNow() => agora;
}
