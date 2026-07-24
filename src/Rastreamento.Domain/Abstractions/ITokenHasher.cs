namespace Rastreamento.Domain.Abstractions;

public interface ITokenHasher
{
    string Hash(string tokenPlano);
}
