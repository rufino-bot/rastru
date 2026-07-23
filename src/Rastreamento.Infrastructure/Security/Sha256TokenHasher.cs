using System.Security.Cryptography;
using System.Text;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Security;

public class Sha256TokenHasher : ITokenHasher
{
    public string Hash(string tokenPlano)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(tokenPlano));
        return Convert.ToHexString(bytes);
    }
}
