using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Security;

public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string senhaPlano) =>
        BCrypt.Net.BCrypt.HashPassword(senhaPlano, WorkFactor);

    public bool Verificar(string senhaPlano, string senhaHash) =>
        BCrypt.Net.BCrypt.Verify(senhaPlano, senhaHash);
}
