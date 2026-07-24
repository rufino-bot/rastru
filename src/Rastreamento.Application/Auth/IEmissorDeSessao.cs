using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Auth;

public interface IEmissorDeSessao
{
    /// <summary>Emite uma sessao nova (login). Persiste o refresh token e salva uma unica vez.</summary>
    Task<LoginResult> EmitirAsync(Usuario usuario, CancellationToken ct);

    /// <summary>
    /// Rotaciona uma sessao existente (refresh): revoga <paramref name="atual"/>, aponta seu
    /// <c>SubstituidoPorTokenHash</c> para o token novo e persiste o novo — tudo num unico save.
    /// Exige <c>atual.Usuario</c> carregado (com <c>Perfil</c>).
    /// </summary>
    Task<LoginResult> RotacionarAsync(RefreshToken atual, CancellationToken ct);
}
