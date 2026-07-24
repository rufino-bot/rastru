using Rastreamento.Domain.Abstractions;

namespace Rastreamento.Infrastructure.Security;

public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    /// <inheritdoc />
    /// <remarks>
    /// Constante literal, e nao um hash gerado em runtime: o custo do <c>Verificar</c> tem que
    /// ser identico ao de um hash real, e o valor precisa ser auditavel. Foi gerado a partir de
    /// 32 bytes aleatorios descartados, com o <see cref="WorkFactor"/> de producao — o
    /// <c>$2a$11$</c> no prefixo e o que garante o mesmo tempo de verificacao (um work factor
    /// menor reintroduziria a diferenca que este hash existe para fechar).
    /// </remarks>
    public string HashFicticio => "$2a$11$yvQM5OnX0g3puuNz8ICpmOKwxeG46YQ4PFovXdb6kEmixJ0Jv8L4e";

    public string Hash(string senhaPlano) =>
        BCrypt.Net.BCrypt.HashPassword(senhaPlano, WorkFactor);

    public bool Verificar(string senhaPlano, string senhaHash) =>
        BCrypt.Net.BCrypt.Verify(senhaPlano, senhaHash);
}
