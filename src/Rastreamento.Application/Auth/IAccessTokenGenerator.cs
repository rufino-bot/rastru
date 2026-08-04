using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Auth;

public interface IAccessTokenGenerator
{
  (string token, DateTime expiraEm) Gerar(Usuario usuario);
}
