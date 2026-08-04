using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Auth;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Entities;
using Xunit;

namespace Rastreamento.Application.Tests.Auth;

public class AutenticarUsuarioUseCaseTests
{
  private const string SenhaCorreta = "Admin@123";

  // Mensagem generica: falha de login nao pode revelar QUAL condicao falhou.
  private const string ErroGenerico = "Usuário ou senha inválidos.";

  // Instancia por teste (o xUnit cria uma instancia da classe por [Fact]): os contadores do
  // fake nao vazam de um teste para o outro.
  private readonly FakePasswordHasher _hasher = new();
  private readonly FakeLogger<AutenticarUsuarioUseCase> _logger = new();

  /// <summary>O repositorio da ultima chamada a <see cref="NovoUseCase"/>.</summary>
  private FakeUsuarioRepo _usuarios = null!;

  private Usuario AdminAtivo() => new()
  {
    Id = 1,
    NomeUsuario = "admin",
    NomeCompleto = "Administrador do Sistema",
    Ativo = true,
    SenhaHash = _hasher.Hash(SenhaCorreta),
    Perfil = new Perfil { Nome = "Administrador" }
  };

  private AutenticarUsuarioUseCase NovoUseCase(
      Usuario? usuario,
      out FakeRefreshTokenRepo refreshRepo,
      int maxFalhas = 5,
      int duracaoMinutos = 15)
  {
    refreshRepo = new FakeRefreshTokenRepo();
    _usuarios = new FakeUsuarioRepo(usuario);
    var emissor = new EmissorDeSessao(
        refreshRepo,
        new FakeTokenHasher(),
        new FakeAccessTokenGenerator(),
        FakeJwtOptions.Instance);
    return new AutenticarUsuarioUseCase(
        _usuarios,
        _hasher,
        emissor,
        Options.Create(new LockoutOptions { MaxFalhas = maxFalhas, DuracaoMinutos = duracaoMinutos }),
        _logger);
  }

  [Fact]
  public async Task Login_valido_retorna_tokens_e_persiste_refresh()
  {
    var uc = NovoUseCase(AdminAtivo(), out var refreshRepo);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.True(r.Sucesso);
    Assert.Null(r.Erro);
    Assert.Null(r.TipoDoErro);
    Assert.Equal("access-admin", r.Valor!.AccessToken);
    Assert.Equal(1, r.Valor.Usuario.Id);
    Assert.Equal("admin", r.Valor.Usuario.NomeUsuario);
    Assert.Equal("Administrador do Sistema", r.Valor.Usuario.NomeCompleto);
    Assert.Equal("Administrador", r.Valor.Usuario.Perfil);
    Assert.False(string.IsNullOrWhiteSpace(r.Valor.RefreshTokenPlano));

    var persistido = Assert.Single(refreshRepo.Adicionados);
    Assert.Equal(1, persistido.UsuarioId);
    // O refresh token so pode ser persistido em forma de hash (nunca em texto plano).
    Assert.Equal("sha:" + r.Valor.RefreshTokenPlano, persistido.TokenHash);
    Assert.NotEqual(r.Valor.RefreshTokenPlano, persistido.TokenHash);
    Assert.Equal(r.Valor.RefreshTokenExpiraEm, persistido.ExpiraEm);
    Assert.Null(persistido.RevogadoEm);

    // Datas sempre em UTC nesta camada.
    Assert.Equal(DateTimeKind.Utc, persistido.CriadoEm.Kind);
    Assert.Equal(DateTimeKind.Utc, persistido.ExpiraEm.Kind);
    Assert.True(
        Math.Abs((persistido.ExpiraEm - DateTime.UtcNow.AddDays(7)).TotalMinutes) < 1,
        $"ExpiraEm ({persistido.ExpiraEm:o}) deveria estar a ~7 dias de UtcNow ({DateTime.UtcNow:o}).");
  }

  [Fact]
  public async Task Login_senha_errada_falha_sem_persistir()
  {
    var uc = NovoUseCase(AdminAtivo(), out var refreshRepo);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

    Assert.False(r.Sucesso);
    Assert.Null(r.Valor);
    Assert.Equal(ErroGenerico, r.Erro);
    Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
    Assert.Empty(refreshRepo.Adicionados);
  }

  [Fact]
  public async Task Login_usuario_inexistente_falha()
  {
    var uc = NovoUseCase(null, out var refreshRepo);

    var r = await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

    Assert.False(r.Sucesso);
    Assert.Equal(ErroGenerico, r.Erro);
    Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
    Assert.Empty(refreshRepo.Adicionados);
  }

  [Fact]
  public async Task Login_usuario_inativo_falha()
  {
    var inativo = AdminAtivo();
    inativo.Ativo = false;
    var uc = NovoUseCase(inativo, out var refreshRepo);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.False(r.Sucesso);
    Assert.Equal(ErroGenerico, r.Erro);
    Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
    Assert.Empty(refreshRepo.Adicionados);
  }

  // ----- Trabalho constante (sem oraculo de timing) --------------------------------------
  //
  // Comparar os corpos das respostas nao prova nada sobre timing: os corpos ja eram identicos
  // enquanto o caminho de miss retornava sem passar pelo BCrypt. O que da para checar de forma
  // deterministica e que a verificacao de senha acontece nos tres caminhos, contra um hash de
  // mesmo custo — e e isso que estes testes fazem.

  [Fact]
  public async Task Login_com_usuario_inexistente_ainda_verifica_a_senha()
  {
    var uc = NovoUseCase(null, out _);

    await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

    Assert.Equal(1, _hasher.Verificacoes);
    Assert.Equal(_hasher.HashFicticio, _hasher.UltimoHashVerificado);
  }

  [Fact]
  public async Task Login_com_usuario_inativo_ainda_verifica_a_senha()
  {
    var inativo = AdminAtivo();
    inativo.Ativo = false;
    var uc = NovoUseCase(inativo, out _);

    await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.Equal(1, _hasher.Verificacoes);
    Assert.Equal(_hasher.HashFicticio, _hasher.UltimoHashVerificado);
  }

  [Fact]
  public async Task Login_valido_verifica_a_senha_uma_unica_vez_contra_o_hash_do_usuario()
  {
    var admin = AdminAtivo();
    var uc = NovoUseCase(admin, out _);

    await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    // Mesmo numero de verificacoes dos caminhos de falha: um BCrypt, nem mais nem menos.
    Assert.Equal(1, _hasher.Verificacoes);
    Assert.Equal(admin.SenhaHash, _hasher.UltimoHashVerificado);
  }

  // ----- Lockout de conta ----------------------------------------------------------------

  [Fact]
  public async Task Senha_errada_incrementa_o_contador_e_persiste()
  {
    var admin = AdminAtivo();
    var uc = NovoUseCase(admin, out _);

    await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

    Assert.Equal(1, admin.FalhasConsecutivas);
    Assert.Null(admin.BloqueadoAte);
    Assert.Equal(1, _usuarios.Saves);   // incrementar sem salvar nao tranca ninguem
  }

  [Fact]
  public async Task Atingir_o_limite_tranca_a_conta_e_zera_o_contador()
  {
    var admin = AdminAtivo();
    var uc = NovoUseCase(admin, out _, maxFalhas: 3, duracaoMinutos: 15);

    for (var i = 0; i < 3; i++)
      await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

    Assert.NotNull(admin.BloqueadoAte);
    Assert.True(admin.BloqueadoAte > DateTime.UtcNow.AddMinutes(14));
    Assert.True(admin.BloqueadoAte < DateTime.UtcNow.AddMinutes(16));
    // Zera de proposito: apos a trava expirar a conta recomeca limpa, e nao a uma falha
    // de ser trancada de novo para sempre.
    Assert.Equal(0, admin.FalhasConsecutivas);
  }

  [Fact]
  public async Task Conta_trancada_falha_mesmo_com_a_senha_certa()
  {
    var admin = AdminAtivo();
    admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
    var uc = NovoUseCase(admin, out var refreshRepo);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.False(r.Sucesso);
    Assert.Equal(ErroGenerico, r.Erro);
    Assert.Equal(TipoDeErro.NaoAutorizado, r.TipoDoErro);
    Assert.Empty(refreshRepo.Adicionados);
  }

  [Fact]
  public async Task Tentativa_em_conta_trancada_nao_estende_a_trava()
  {
    var admin = AdminAtivo();
    var travaOriginal = DateTime.UtcNow.AddMinutes(10);
    admin.BloqueadoAte = travaOriginal;
    var uc = NovoUseCase(admin, out _);

    await uc.ExecutarAsync(new LoginRequest("admin", "errada"), default);

    // Nem incrementa nem re-tranca: senao um atacante persistente manteria a conta do
    // operador trancada indefinidamente.
    Assert.Equal(travaOriginal, admin.BloqueadoAte);
    Assert.Equal(0, admin.FalhasConsecutivas);
    Assert.Equal(0, _usuarios.Saves);
  }

  [Fact]
  public async Task Trava_expirada_deixa_a_conta_entrar_de_novo()
  {
    var admin = AdminAtivo();
    admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(-1);   // ja passou
    admin.FalhasConsecutivas = 0;
    var uc = NovoUseCase(admin, out _);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.True(r.Sucesso);
    // Login bem-sucedido limpa o rastro da trava.
    Assert.Null(admin.BloqueadoAte);
    Assert.Equal(0, admin.FalhasConsecutivas);
  }

  [Fact]
  public async Task Login_bem_sucedido_zera_falhas_acumuladas()
  {
    var admin = AdminAtivo();
    admin.FalhasConsecutivas = 3;
    var uc = NovoUseCase(admin, out _);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.True(r.Sucesso);
    Assert.Equal(0, admin.FalhasConsecutivas);
    Assert.Equal(1, _usuarios.Saves);
  }

  [Fact]
  public async Task Login_limpo_nao_escreve_no_banco_a_toa()
  {
    var uc = NovoUseCase(AdminAtivo(), out _);

    var r = await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.True(r.Sucesso);
    // Nada mudou no usuario: um UPDATE por login de rotina seria escrita pura de desperdicio.
    Assert.Equal(0, _usuarios.Saves);
  }

  [Fact]
  public async Task Usuario_inexistente_nao_escreve_nada()
  {
    var uc = NovoUseCase(null, out _);

    await uc.ExecutarAsync(new LoginRequest("ninguem", "x"), default);

    Assert.Equal(0, _usuarios.Saves);
  }

  [Fact]
  public async Task Trancar_a_conta_loga_warning_sem_a_senha()
  {
    var admin = AdminAtivo();
    var uc = NovoUseCase(admin, out _, maxFalhas: 2);

    await uc.ExecutarAsync(new LoginRequest("admin", "senha-secreta-do-teste"), default);
    Assert.Empty(_logger.Entradas);   // so a trava e evento de seguranca, nao cada erro

    await uc.ExecutarAsync(new LoginRequest("admin", "senha-secreta-do-teste"), default);

    var entrada = Assert.Single(_logger.Entradas);
    Assert.Equal(LogLevel.Warning, entrada.Nivel);
    Assert.Contains("admin", entrada.Mensagem);
    Assert.DoesNotContain("senha-secreta-do-teste", entrada.Mensagem);
  }

  [Fact]
  public async Task Conta_trancada_ainda_verifica_a_senha()
  {
    // Trabalho constante tambem no caminho novo: se o BCrypt fosse pulado para conta trancada,
    // a resposta voltaria ~100ms antes e revelaria o estado da conta pelo tempo.
    var admin = AdminAtivo();
    admin.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);
    var uc = NovoUseCase(admin, out _);

    await uc.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default);

    Assert.Equal(1, _hasher.Verificacoes);
    Assert.Equal(admin.SenhaHash, _hasher.UltimoHashVerificado);
  }

  [Fact]
  public async Task Falhas_de_login_nao_revelam_qual_condicao_falhou()
  {
    var trancado = AdminAtivo();
    trancado.BloqueadoAte = DateTime.UtcNow.AddMinutes(10);

    var inativo = AdminAtivo();
    inativo.Ativo = false;

    var falhas = new List<Result<LoginResult>>();

    // Senha errada, conta trancada COM a senha certa, conta inativa e usuario inexistente
    // tem que ser indistinguiveis pelo corpo e pelo tipo do erro.
    var ucSenhaErrada = NovoUseCase(AdminAtivo(), out _);
    falhas.Add(await ucSenhaErrada.ExecutarAsync(new LoginRequest("admin", "errada"), default));

    var ucTrancado = NovoUseCase(trancado, out _);
    falhas.Add(await ucTrancado.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default));

    var ucInativo = NovoUseCase(inativo, out _);
    falhas.Add(await ucInativo.ExecutarAsync(new LoginRequest("admin", SenhaCorreta), default));

    var ucInexistente = NovoUseCase(null, out _);
    falhas.Add(await ucInexistente.ExecutarAsync(new LoginRequest("ninguem", "x"), default));

    Assert.All(falhas, f => Assert.False(f.Sucesso));
    Assert.Single(falhas.Select(f => f.Erro).Distinct());
    Assert.Single(falhas.Select(f => f.TipoDoErro).Distinct());
  }
}
