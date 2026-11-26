using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rastreamento.Infrastructure.Persistence;

namespace Rastreamento.Api.Controllers;

/// <summary>
/// Rota de saude: o orquestrador e a verificacao do deploy chamam sem sessao. Anonima e sem
/// rate limit de proposito; nao le nem devolve dado de negocio, so confere que o banco responde.
/// </summary>
[ApiController]
[Route("saude")]
[AllowAnonymous]
public class SaudeController(RastreamentoDbContext db, ILogger<SaudeController> logger) : ControllerBase
{
  /// <summary>
  /// 200 `{"status":"ok"}` com o banco alcancavel; 503 `{"status":"indisponivel"}` sem ele ou se
  /// a conferencia lancar. O detalhe da falha vai so para o log, nunca para o corpo.
  /// </summary>
  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken ct)
  {
    try
    {
      if (await db.Database.CanConnectAsync(ct))
        return Ok(new { status = "ok" });

      // O EF devolve false, sem lancar, para "login failed" e "banco inexistente": o caso mais provavel.
      logger.LogWarning("A conferencia de saude nao alcancou o banco (login recusado ou banco inexistente).");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogError(ex, "A conferencia de saude falhou ao consultar o banco.");
    }

    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "indisponivel" });
  }
}
