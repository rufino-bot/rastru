using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

public class Sha256TokenHasherTests
{
    private readonly Sha256TokenHasher _hasher = new();

    [Fact]
    public void Hash_e_deterministico()
    {
        Assert.Equal(_hasher.Hash("abc"), _hasher.Hash("abc"));
    }

    [Fact]
    public void Hash_difere_por_entrada()
    {
        Assert.NotEqual(_hasher.Hash("abc"), _hasher.Hash("abd"));
    }
}
