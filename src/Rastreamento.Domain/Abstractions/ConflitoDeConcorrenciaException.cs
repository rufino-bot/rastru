namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// A linha lida foi alterada por outra requisicao antes do save. Traduz o
/// DbUpdateConcurrencyException do EF para um tipo que a Application possa capturar sem
/// depender do EF Core.
/// </summary>
public class ConflitoDeConcorrenciaException : Exception
{
    public ConflitoDeConcorrenciaException(Exception interna)
        : base("A linha foi alterada por outra requisicao.", interna) { }
}
