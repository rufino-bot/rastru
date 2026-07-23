namespace Rastreamento.Domain.Abstractions;

public interface IPasswordHasher
{
    string Hash(string senhaPlano);
    bool Verificar(string senhaPlano, string senhaHash);
}
