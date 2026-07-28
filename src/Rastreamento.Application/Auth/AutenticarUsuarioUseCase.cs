using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Application.Auth;

public class AutenticarUsuarioUseCase : IAutenticarUsuarioUseCase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmissorDeSessao _emissor;
    private readonly LockoutOptions _lockout;
    private readonly ILogger<AutenticarUsuarioUseCase> _logger;

    public AutenticarUsuarioUseCase(
        IUsuarioRepository usuarios,
        IPasswordHasher passwordHasher,
        IEmissorDeSessao emissor,
        IOptions<LockoutOptions> lockout,
        ILogger<AutenticarUsuarioUseCase> logger)
    {
        _usuarios = usuarios;
        _passwordHasher = passwordHasher;
        _emissor = emissor;
        _lockout = lockout.Value;
        _logger = logger;
    }

    public async Task<Result<LoginResult>> ExecutarAsync(LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.ObterPorNomeUsuarioAsync(req.NomeUsuario, ct);
        var agora = DateTime.UtcNow;

        // Trabalho constante: o BCrypt roda SEMPRE, inclusive quando o usuario nao existe, esta
        // inativo ou esta trancado. Com o curto-circuito do `||` a verificacao era pulada nesses
        // casos e a resposta voltava ~100ms antes da de "senha errada" — corpo identico, tempo
        // diferente. O lockout nao pode reabrir esse oraculo: nenhum return antes desta linha.
        var hashDeReferencia = usuario is not null && usuario.Ativo
            ? usuario.SenhaHash
            : _passwordHasher.HashFicticio;
        var senhaConfere = _passwordHasher.Verificar(req.Senha, hashDeReferencia);

        // Usuario inexistente ou inativo: 401 sem tocar em contador nenhum (nao ha o que contar,
        // e escrever aqui daria ao atacante um sinal de existencia da conta).
        if (usuario is null || !usuario.Ativo) return Falha();

        // Conta trancada: falha MESMO com a senha certa, e sem incrementar — se cada tentativa
        // estendesse a trava, um atacante persistente manteria o operador de fora indefinidamente.
        if (usuario.BloqueadoAte > agora) return Falha();

        if (!senhaConfere)
        {
            usuario.FalhasConsecutivas++;

            if (usuario.FalhasConsecutivas >= _lockout.MaxFalhas)
            {
                usuario.BloqueadoAte = agora.AddMinutes(_lockout.DuracaoMinutos);
                // Zera junto: depois que a trava expirar a conta recomeca limpa, em vez de ficar
                // a uma falha de ser trancada de novo para sempre.
                usuario.FalhasConsecutivas = 0;

                _logger.LogWarning(
                    "Conta {NomeUsuario} trancada por excesso de falhas de login ate {BloqueadoAte:o} (UTC).",
                    usuario.NomeUsuario, usuario.BloqueadoAte);
            }

            await _usuarios.SalvarAlteracoesAsync(ct);
            return Falha();
        }

        // Sucesso: limpa o rastro. A escrita e condicional de proposito — um UPDATE em todo login
        // de rotina seria puro desperdicio, e aqui nao ha oraculo a proteger (o usuario ja provou
        // quem e).
        if (usuario.FalhasConsecutivas != 0 || usuario.BloqueadoAte is not null)
        {
            usuario.FalhasConsecutivas = 0;
            usuario.BloqueadoAte = null;
            await _usuarios.SalvarAlteracoesAsync(ct);
        }

        var sessao = await _emissor.EmitirAsync(usuario, ct);
        return Result<LoginResult>.Ok(sessao);
    }

    /// <summary>
    /// Falha unica e generica: usuario inexistente, inativo, conta trancada (mesmo com a senha
    /// certa) e senha errada sao indistinguiveis para quem chama.
    /// </summary>
    private static Result<LoginResult> Falha() =>
        Result<LoginResult>.Falha("Usuário ou senha inválidos.", TipoDeErro.NaoAutorizado);
}
