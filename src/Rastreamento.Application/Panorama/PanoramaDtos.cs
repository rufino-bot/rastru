using Rastreamento.Application.Andamento;
using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Panorama;

/// <summary>
/// O Pedido da Peca no Panorama. `Atrasado` e `Pausa` vem do servidor, como na expedicao (regra 33: data de
/// Brasilia e status do Pedido); `Retrabalho` e `Pedido.PedidoOrigemId` nao nulo.
/// </summary>
public sealed record PedidoDoPanoramaDto(
    int Id, string Numero, DateOnly DataEntrega, bool Atrasado, bool Retrabalho, PausaResumoDto? Pausa);

/// <summary>O Componente da Peca. `MiniaturaVersao` e o hash da miniatura em hexadecimal, ou null sem miniatura.</summary>
public sealed record ComponenteDoPanoramaDto(int Id, string Codigo, string Descricao, string? MiniaturaVersao);

/// <summary>Um Setor de "Itens em" (D13): so o nome, sem numero.</summary>
public sealed record SetorResumoDto(int SetorId, string Nome);

/// <summary>
/// Um segmento do "Onde esta" (C8), ja na ordem. `Tipo` e um dos `TiposDeSegmento`; `SetorId`/`SetorNome` so nos
/// segmentos de passo e de conserto, `Ordem` so no de passo; `AguardandoColeta` so no de passo. `Desde` e a chegada da
/// parte mais antiga do segmento (nulo no "a iniciar" de no com filhos, no Expedido e no Perdido) e
/// `AguardandoColetaDesde`, a da parte mais antiga que espera coleta no passo (C6 da spec da Fase 6A).
/// </summary>
public sealed record SegmentoDto(
    string Tipo, int? SetorId, string? SetorNome, int? Ordem, decimal Quantidade, decimal AguardandoColeta,
    DateTime? Desde, DateTime? AguardandoColetaDesde);

/// <summary>O resumo da afericao da Peca: se exige relatorio e a `SituacaoDaAfericao` (como a da expedicao).</summary>
public sealed record AfericaoDoPanoramaDto(bool ExigeRelatorio, string Situacao);

public sealed record UltimaEmbalagemDto(string Tipo, DateTime DataHora, string Usuario);

/// <summary>O "Embalado" da Peca (regra 40): `EmProducao` e o "faltam N" das sete posicoes de producao.</summary>
public sealed record EmbalagemDaPecaDto(bool Vale, bool PodeMarcar, decimal EmProducao, UltimaEmbalagemDto? Ultima);

public sealed record PecaDoPanoramaDto(
    int EstruturaItemId, int AgrupamentoId, PedidoDoPanoramaDto Pedido, ComponenteDoPanoramaDto Componente,
    decimal Quantidade, IReadOnlyList<SetorResumoDto> ItensEm, IReadOnlyList<SegmentoDto> Distribuicao,
    AfericaoDoPanoramaDto Afericao, EmbalagemDaPecaDto Embalagem,
    DateTime? ParadoDesde, IReadOnlyList<ComplicacaoDto> Complicacoes);

/// <summary>`Agora` e o relogio do servidor: o cliente calcula a idade contra ele (C12 da spec da Fase 6A).</summary>
public sealed record PanoramaDto(DateTime Agora, IReadOnlyList<PecaDoPanoramaDto> Pecas);

/// <summary>O corpo do POST do "Embalado": `true` marca, `false` desmarca. Nulo (campo ausente) e recusado pelo caso de uso.</summary>
public sealed record NovaEmbalagemDto(bool? Embalado);
