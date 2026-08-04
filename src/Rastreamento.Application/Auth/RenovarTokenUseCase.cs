using Microsoft.Extensions.Logging;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class RenovarTokenUseCase : IRenovarTokenUseCase
{
  private readonly IRefreshTokenRepository _refreshTokens;
  private readonly ITokenHasher _tokenHasher;
  private readonly IEmissorDeSessao _emissor;
  private readonly ILogger<RenovarTokenUseCase> _logger;

  public RenovarTokenUseCase(
      IRefreshTokenRepository refreshTokens,
      ITokenHasher tokenHasher,
      IEmissorDeSessao emissor,
      ILogger<RenovarTokenUseCase> logger)
  {
    _refreshTokens = refreshTokens;
    _tokenHasher = tokenHasher;
    _emissor = emissor;
    _logger = logger;
  }

  public async Task<Result<LoginResult>> ExecutarAsync(string refreshTokenPlano, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return Falha();

    var hash = _tokenHasher.Hash(refreshTokenPlano);

    // Em QUALQUER estado, de proposito: ObterAtivoPorHashAsync filtra RevogadoEm IS NULL e
    // deixaria a reapresentacao de um token ja rotacionado indistinguivel de "nunca existiu" —
    // que e exatamente o sinal de reuso que se quer enxergar.
    var atual = await _refreshTokens.ObterPorHashAsync(hash, ct);

    if (atual is null) return Falha();

    if (atual.RevogadoEm is not null)
    {
      // REUSO. Este token ja foi rotacionado e alguem o reapresentou: ou o legitimo depois de
      // o ladrao ter rotacionado, ou o contrario. Nos dois casos o refresh vazou, e revogar
      // so este deixaria viva a sessao emitida ao ladrao. Recomendacao do OWASP: reuse
      // detected -> invalidate the token family. Aqui se queima tudo do usuario (mais simples
      // e mais robusto que rastrear a cadeia, e num roubo confirmado se quer derrubar tudo).
      var revogados = await _refreshTokens.RevogarTodosAtivosDoUsuarioAsync(
          atual.UsuarioId, DateTime.UtcNow, ct);

      _logger.LogWarning(
          "Reuso de refresh token detectado para o usuario {UsuarioId}: {Revogados} sessao(oes) ativa(s) revogada(s).",
          atual.UsuarioId, revogados);

      // Mesmo 401 generico dos demais caminhos: a queima e efeito colateral so no banco, e
      // quem chama nao consegue distinguir reuso de "token invalido".
      return Falha();
    }

    // Expirado e usuario desativado NAO sao sinal de roubo — 401 sem queimar a familia.
    // `!atual.Usuario.Ativo`: sem isso, desativar um usuario nao o expulsa — ele continuaria
    // rotacionando o refresh ate a expiracao natural (ate 7 dias).
    if (atual.ExpiraEm <= DateTime.UtcNow || !atual.Usuario.Ativo) return Falha();

    // Revogação do antigo + emissão do novo num único save (ver EmissorDeSessao).
    try
    {
      var novaSessao = await _emissor.RotacionarAsync(atual, ct);
      return Result<LoginResult>.Ok(novaSessao);
    }
    catch (ConflitoDeConcorrenciaException)
    {
      // A familia foi queimada entre a leitura e o save: o UPDATE do token antigo pegou
      // versao obsoleta, entao o SaveChanges inteiro reverteu e o token novo nunca existiu.
      // Mesmo 401 generico — quem chama nao distingue isto de "token invalido".
      return Falha();
    }
  }

  /// <summary>
  /// Falha unica: todos os caminhos (vazio, desconhecido, reuso, expirado, usuario desativado)
  /// devolvem exatamente isto. Variar mensagem ou tipo aqui vazaria a condicao que falhou.
  /// </summary>
  private static Result<LoginResult> Falha() =>
      Result<LoginResult>.Falha("Refresh token inválido ou expirado.", TipoDeErro.NaoAutorizado);
}
