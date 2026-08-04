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

  [Fact]
  public void HashFicticio_tem_o_mesmo_custo_do_hash_de_producao()
  {
    // Este e o teste que sustenta o trabalho constante do login: o hash ficticio so fecha o
    // oraculo de timing se custar o mesmo que um hash real. O prefixo do BCrypt ("$2a$11$")
    // carrega versao e work factor, entao comparar os 7 primeiros caracteres compara o custo.
    // Se alguem subir o WorkFactor de producao e esquecer do ficticio, isto quebra.
    var producao = _hasher.Hash("Admin@123");

    Assert.Equal(producao[..7], _hasher.HashFicticio[..7]);
    Assert.StartsWith("$2a$11$", _hasher.HashFicticio);
  }

  [Fact]
  public void HashFicticio_nao_valida_nenhuma_senha()
  {
    Assert.False(_hasher.Verificar("Admin@123", _hasher.HashFicticio));
    Assert.False(_hasher.Verificar("", _hasher.HashFicticio));
    Assert.False(_hasher.Verificar("senha", _hasher.HashFicticio));
  }
}
