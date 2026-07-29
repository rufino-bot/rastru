using Microsoft.EntityFrameworkCore;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Infrastructure.Tests;

/// <summary>
/// Base dos testes que rodam contra o SQL Server real (docker compose up -d, com
/// specs/02-modelo-de-dados.sql e db/seed.sql aplicados). Existe para a connection string do
/// container de dev viver num lugar so — ela ja estava copiada em tres classes da Fase 0.
/// </summary>
/// <remarks>
/// Fica no namespace raiz do projeto de teste de proposito: namespaces filhos (`.Persistence`,
/// `.Security`) enxergam o pai sem `using`.
/// </remarks>
public abstract class TesteComBanco
{
    protected const string Conn =
        "Server=localhost,1433;Database=Rastreamento;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True";

    protected static RastreamentoDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<RastreamentoDbContext>().UseSqlServer(Conn).Options;
        return new RastreamentoDbContext(options);
    }
}
