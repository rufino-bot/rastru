using Rastreamento.Infrastructure.Security;
using Xunit;

namespace Rastreamento.Infrastructure.Tests.Security;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_nao_retorna_a_senha_em_claro()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.NotEqual("Admin@123", hash);
        Assert.StartsWith("$2", hash);
    }

    [Fact]
    public void Verificar_true_para_senha_correta()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.True(_hasher.Verificar("Admin@123", hash));
    }

    [Fact]
    public void Verificar_false_para_senha_errada()
    {
        var hash = _hasher.Hash("Admin@123");
        Assert.False(_hasher.Verificar("senha-errada", hash));
    }
}
