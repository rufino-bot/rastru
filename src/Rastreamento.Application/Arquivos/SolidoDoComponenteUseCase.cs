using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Arquivos;

/// <summary>
/// Envio e leitura do solido 3D de um Componente (regra 18, Fase 2B). Substitui em vez de
/// versionar do ponto de vista do Componente: o arquivo anterior continua na tabela, mas ninguem
/// aponta para ele. NAO existe remocao — ver a spec da fase. A miniatura (o PNG isometrico que o
/// navegador desenha do STL) sobe junto e segue a mesma politica.
/// </summary>
public sealed class SolidoDoComponenteUseCase
{
  private const string ErroDeComponenteNaoEncontrado = "Componente nao encontrado.";

  private const string ErroDeSolidoNaoEncontrado = "Este Componente nao tem solido.";

  private const string ErroDeMiniaturaNaoEncontrada = "Este Componente nao tem miniatura.";

  /// <summary>O navegador manda o PNG sem nome que importe; o nome gravado e sempre este.</summary>
  private const string NomeDaMiniatura = "miniatura.png";

  private readonly IComponenteRepository _componentes;
  private readonly IArquivoDeComponenteRepository _arquivos;

  public SolidoDoComponenteUseCase(
      IComponenteRepository componentes, IArquivoDeComponenteRepository arquivos)
  {
    _componentes = componentes;
    _arquivos = arquivos;
  }

  public async Task<Result> Enviar(
      int componenteId, string nomeOriginal, byte[] conteudo, byte[]? miniatura, int usuarioId,
      CancellationToken ct)
  {
    // Valida o arquivo ANTES de ler o componente. A ordem nao protege sigilo nenhum: o catalogo e
    // legivel por qualquer autenticado (`GET componentes`, `GET componentes/{id}`), entao o tipo do
    // erro nao revela a existencia de um id que um GET ja nao revele. O que ela faz e recusar
    // arquivo invalido sem ida ao banco — o validador e puro.
    var invalido = ValidadorDeArquivoStl.Validar(nomeOriginal, conteudo);
    if (invalido is not null) return Result.Falha(invalido, TipoDeErro.Validacao);

    // O STL primeiro, a miniatura depois (C8 da spec da miniatura do solido). Miniatura invalida
    // recusa o envio INTEIRO em vez de gravar o solido sem ela: quem a desenha e o proprio front,
    // entao imagem errada e defeito, nao falta de WebGL -- para essa o front manda sem o campo.
    if (miniatura is not null && ValidadorDeMiniatura.Validar(miniatura) is { } miniaturaInvalida)
      return Result.Falha(miniaturaInvalida, TipoDeErro.Validacao);

    if (await _componentes.ObterPorIdAsync(componenteId, ct) is null)
      return Result.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    var arquivo = new ArquivoDeComponente
    {
      NomeOriginal = nomeOriginal,
      Conteudo = conteudo,
      CriadoPorUsuarioId = usuarioId,
    };
    var arquivoDaMiniatura = miniatura is null
        ? null
        : new ArquivoDeComponente
        {
          NomeOriginal = NomeDaMiniatura,
          Conteudo = miniatura,
          CriadoPorUsuarioId = usuarioId,
        };
    // O null do repositorio tambem e "componente nao existe" -- checagem que a Task 2 passou a
    // fazer dentro da transacao, ANTES do primeiro SaveChanges. Aqui ele e defesa em profundidade:
    // a checagem de existencia do componente, via _componentes.ObterPorIdAsync, ja descartou esse
    // caso, e so um componente que desaparecesse entre as duas chamadas chegaria aqui. Nao ha
    // teste que mate este `if` -- o dominio DESATIVA
    // Componente, nunca apaga (nao existe `DELETE /componentes/{id}`, e nenhum codigo de `src/`
    // remove Componente), entao o cenario nao e alcancavel sem injetar falha. Ignorar o retorno e
    // que seria errado: devolveria Ok() sem ter gravado.
    if (await _arquivos.GravarEVincularComoSolidoAsync(componenteId, arquivo, arquivoDaMiniatura, ct) is null)
      return Result.Falha(ErroDeComponenteNaoEncontrado, TipoDeErro.NaoEncontrado);

    return Result.Ok();
  }

  /// <summary>
  /// Componente inexistente e componente sem solido respondem o MESMO NaoEncontrado: a diferenca
  /// nao muda o que o cliente faz, e o repositorio ja devolve null nos dois casos.
  /// </summary>
  public async Task<Result<ArquivoDeSolidoDto>> Obter(int componenteId, CancellationToken ct)
  {
    var arquivo = await _arquivos.ObterSolidoDoComponenteAsync(componenteId, ct);
    return arquivo is null
        ? Result<ArquivoDeSolidoDto>.Falha(ErroDeSolidoNaoEncontrado, TipoDeErro.NaoEncontrado)
        : Result<ArquivoDeSolidoDto>.Ok(new ArquivoDeSolidoDto(arquivo.NomeOriginal, arquivo.Conteudo));
  }

  /// <summary>
  /// Mesmo colapso de <see cref="Obter"/>: Componente inexistente e Componente sem miniatura
  /// respondem o MESMO NaoEncontrado.
  /// </summary>
  public async Task<Result<ArquivoDeMiniaturaDto>> ObterMiniatura(int componenteId, CancellationToken ct)
  {
    var arquivo = await _arquivos.ObterMiniaturaDoComponenteAsync(componenteId, ct);
    return arquivo is null
        ? Result<ArquivoDeMiniaturaDto>.Falha(ErroDeMiniaturaNaoEncontrada, TipoDeErro.NaoEncontrado)
        : Result<ArquivoDeMiniaturaDto>.Ok(
            new ArquivoDeMiniaturaDto(arquivo.Conteudo, VersaoDeArquivo.VersaoDe(arquivo.Sha256)));
  }
}
