using Microsoft.Net.Http.Headers;

namespace Rastreamento.Api.Spa;

/// <summary>
/// O modo SPA da API: quando o <c>wwwroot</c> tem <c>index.html</c> (a imagem do container poe
/// ali o build do Vite), a propria API serve o front. Sem ele a API fica so-API, como sempre foi.
///
/// O ramo do SPA vem ANTES da guarda do <c>/api</c> em <c>Program.cs</c> (ela e um 404 cego para
/// tudo que nao comeca com <c>/api</c>) e so atende o que NAO comeca com <c>/api</c>, comparado
/// de forma ordinal, o mesmo criterio da guarda: <c>/API/x</c> cai nela e responde 404.
/// </summary>
public static class SpaDaApi
{
  private const string PrefixoDaApi = "/api";
  private const string PrefixoDosAssets = "/assets/";
  private const string CacheDoAsset = "public, max-age=31536000, immutable";
  private const string SemCache = "no-cache";

  /// <summary>Verdadeiro quando o <c>wwwroot</c> tem <c>index.html</c>.</summary>
  public static bool Disponivel(IWebHostEnvironment ambiente) =>
      ambiente.WebRootFileProvider.GetFileInfo("index.html").Exists;

  /// <summary>
  /// Registra o ramo do SPA. Arquivo que existe em <c>wwwroot</c> e servido como esta; caminho
  /// sem extensao que nao e arquivo (uma rota do SPA, o F5) devolve o <c>index.html</c>; caminho
  /// COM extensao que nao existe e 404, de proposito: um <c>/assets/index-velho.js</c> de um
  /// cliente aberto antes do deploy, respondido com o index e 200, entregaria HTML como script e
  /// deixaria a tela branca sem erro util. Outro metodo que nao GET/HEAD fora de <c>/api</c> e 404.
  /// </summary>
  public static void UsarSpa(this WebApplication app)
  {
    app.MapWhen(
        contexto => !contexto.Request.Path.StartsWithSegments(PrefixoDaApi, StringComparison.Ordinal),
        ramo =>
        {
          ramo.UseStaticFiles(new StaticFileOptions
          {
            OnPrepareResponse = contexto =>
            {
              var emAssets = contexto.Context.Request.Path.StartsWithSegments(
                  PrefixoDosAssets.TrimEnd('/'), StringComparison.Ordinal);
              contexto.Context.Response.Headers[HeaderNames.CacheControl] =
                  emAssets ? CacheDoAsset : SemCache;
            },
          });

          ramo.Run(async contexto =>
          {
            var requisicao = contexto.Request;
            var ehLeitura = HttpMethods.IsGet(requisicao.Method) || HttpMethods.IsHead(requisicao.Method);
            var ultimoSegmento = (requisicao.Path.Value ?? "/").Split('/').Last();

            // /API/x so difere de /api/x na caixa: nao e rota do SPA nem da API, e a guarda a
            // recusa (404) para o cookie de refresh nunca ser gravado sob outro Path.
            var prefixoSoIgnorandoCaixa =
                requisicao.Path.StartsWithSegments(PrefixoDaApi, StringComparison.OrdinalIgnoreCase);

            if (!ehLeitura || prefixoSoIgnorandoCaixa || Path.HasExtension(ultimoSegmento))
            {
              contexto.Response.StatusCode = StatusCodes.Status404NotFound;
              return;
            }

            var ambiente = contexto.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var index = ambiente.WebRootFileProvider.GetFileInfo("index.html");
            if (!index.Exists)
            {
              contexto.Response.StatusCode = StatusCodes.Status404NotFound;
              return;
            }

            contexto.Response.ContentType = "text/html; charset=utf-8";
            contexto.Response.Headers[HeaderNames.CacheControl] = SemCache;
            contexto.Response.ContentLength = index.Length;
            if (HttpMethods.IsHead(requisicao.Method)) return;

            await using var fluxo = index.CreateReadStream();
            await fluxo.CopyToAsync(contexto.Response.Body, contexto.RequestAborted);
          });
        });
  }
}
