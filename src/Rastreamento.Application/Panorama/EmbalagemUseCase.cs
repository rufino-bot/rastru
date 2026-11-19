using Rastreamento.Application.Common;
using Rastreamento.Application.Execucao;
using Rastreamento.Application.Expedicao;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// Marcar e desmarcar o "Embalado" de uma Peca (secao 3.3 da spec do Panorama). Corre na transacao da execucao
/// porque a condicao le o livro: nenhuma movimentacao entra entre a conferencia e a gravacao. A pausa do Pedido
/// nao recusa, e a escrita nao mexe em quantidade nem no fechamento do Pedido.
/// </summary>
public sealed class EmbalagemUseCase
{
  private readonly IExecucaoRepository _execucao;
  private readonly IEmbalagemRepository _embalagem;
  private readonly LeitorDeEstado _leitor;

  public EmbalagemUseCase(
      IExecucaoRepository execucao, IEstruturaRepository estruturas, IReceitaPadraoRepository catalogo,
      IEmbalagemRepository embalagem)
  {
    _execucao = execucao;
    _embalagem = embalagem;
    _leitor = new LeitorDeEstado(execucao, estruturas, catalogo);
  }

  public async Task<Result<bool>> Registrar(int estruturaItemId, NovaEmbalagemDto dto, int usuarioId, CancellationToken ct)
  {
    if (dto.Embalado is not bool embalado)
      return Falhas.Validacao<bool>(CodigosDoPanorama.CorpoInvalido,
          "Informe se a Peça está embalada: true marca, false desmarca.");

    return await _execucao.ExecutarAsync(async () =>
    {
      // O Pedido so e lido, nao travado: a escrita nao mexe em quantidade nem no fechamento. A trava do no e a
      // leitura sob SERIALIZABLE bastam para o livro nao mudar entre a conferencia e a gravacao.
      var (peca, recusa) = await TravaDaPeca.TravarAsync<bool>(_execucao, _leitor, estruturaItemId, travarPedido: false, ct, oQueSoAPecaFaz: "é embalada");
      if (recusa is not null) return recusa;

      var estado = await _leitor.CarregarAsync([peca!.No], ct);
      var ultima = (await _embalagem.ListarUltimasAsync([estruturaItemId], CalculoDaEmbalagem.PosicoesDeProducao, ct))
          .SingleOrDefault();
      var situacao = CalculoDaEmbalagem.Estado(estado.Calc.Saldos(estruturaItemId), ultima);

      if (embalado)
      {
        if (!situacao.PodeMarcar)
          return Falhas.Conflito<bool>(CodigosDoPanorama.ProducaoNaoTerminou,
              $"Faltam {Quantidades.Formatar(situacao.EmProducao)} em produção.");
        if (situacao.Vale)
          return Falhas.Conflito<bool>(CodigosDoPanorama.JaEmbalado, "Esta Peça já está embalada.");
      }
      else if (!situacao.Vale)
      {
        return Falhas.Conflito<bool>(CodigosDoPanorama.NaoEmbalado, "Esta Peça não está embalada.");
      }

      await _embalagem.GravarAsync(new Embalagem
      {
        EstruturaItemId = estruturaItemId,
        Tipo = embalado ? TiposDeEmbalagem.Marcar : TiposDeEmbalagem.Desmarcar,
        DataHora = DateTime.UtcNow,
        UsuarioId = usuarioId,
      }, ct);
      return Result<bool>.Ok(true);
    }, ct);
  }
}
