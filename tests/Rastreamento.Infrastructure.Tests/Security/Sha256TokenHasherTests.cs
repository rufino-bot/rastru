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

    [Fact]
    public void Hash_produz_vetor_conhecido_de_sha256_em_hex_maiusculo()
    {
        // Vetor de teste conhecido: SHA-256("abc") = ba7816bf...15ad
        // Sha256TokenHasher usa Convert.ToHexString, que produz hex em maiusculo.
        const string esperado = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

        var hash = _hasher.Hash("abc");

        Assert.Equal(esperado, hash);
    }

    [Fact]
    public void Hash_tem_64_caracteres_hex_e_nao_e_o_token_original()
    {
        const string tokenPlano = "abc";

        var hash = _hasher.Hash(tokenPlano);

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-Fa-f]{64}$", hash);
        Assert.NotEqual(tokenPlano, hash);
    }
}
