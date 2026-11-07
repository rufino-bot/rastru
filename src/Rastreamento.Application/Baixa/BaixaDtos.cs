using Rastreamento.Application.Execucao;
using Rastreamento.Application.Retrabalho;

namespace Rastreamento.Application.Baixa;

/// <summary>O corpo do "Descartar tudo" do Agrupamento e do Pedido (secao 5.1 da spec da Fase 5B-1): so a observacao.</summary>
public sealed record DescarteDto(string? Observacao);

/// <summary>Uma posicao de onde a baixa de parte tira, e quanto (D4 da spec da Fase 5B-1).</summary>
public sealed record OrigemDaBaixa(Local Local, decimal Quantidade);

/// <summary>
/// O que a conta da baixa recebe. `Origens` so na baixa de parte; vazia com `Tudo`. `IncluirTopo` so na baixa de um
/// Item: de parte (D13), ou de tudo com motivo `PerdaArmazem` ou `MortaEmProcesso` (D14); de tudo por `Descarte`, nao.
/// `CadeiaAteOTopo`: os ancestrais do no, do pai ate a Peca, vazia quando o no e Peca.
/// </summary>
public sealed record PedidoDeBaixa(
    int NoId, string Motivo, bool Tudo, IReadOnlyList<OrigemDaBaixa> Origens, bool IncluirTopo,
    IReadOnlyList<int> CadeiaAteOTopo);

/// <summary>Uma linha do livro que a baixa vai gravar: de `Origem` para `Perdido`.</summary>
public sealed record LinhaPlanejada(int NoId, Local Origem, decimal Quantidade);

/// <summary>
/// As unidades da Peca do topo que a perda de um Item trava (secao 4.6 da spec da Fase 5B-1), e quantas dela ainda estao
/// a iniciar, que e de onde `IncluirTopo` tira.
/// </summary>
public sealed record TravaNoTopo(int PecaId, decimal Quantidade, decimal AIniciar);

/// <summary>
/// O que a baixa faz: as linhas, na ordem em que se gravam; os nos cujo fabricado passa a sobrar por causa dela (C6);
/// e a trava no topo, quando a baixa e de um Item (de parte, ou de tudo por perda) e trava alguma unidade da Peca.
/// </summary>
public sealed record PlanoDaBaixa(
    IReadOnlyList<LinhaPlanejada> Linhas, IReadOnlyList<(int NoId, decimal Quantidade)> ViramSobra, TravaNoTopo? Trava);

/// <summary>
/// O corpo de `POST /estrutura/{id}/baixas` e da previa (secao 5.1 da spec da Fase 5B-1). `Origens` so na baixa de
/// parte; ausente ou vazia com `Tudo`. `IncluirTopo` so na baixa de um Item (D13, D14).
/// </summary>
public sealed record NovaBaixaDto(
    string? Motivo, string? Observacao, bool Tudo, IReadOnlyList<OrigemDto>? Origens, bool IncluirTopo);

/// <summary>Uma posicao de onde a baixa de parte tira: o Setor e o passo so quando a posicao os tem (secao 5.4).</summary>
public sealed record OrigemDto(string? Posicao, int? SetorId, int? Ordem, decimal Quantidade);

/// <summary>O escopo da baixa: `No` (a baixa de um no), `Agrupamento` ou `Pedido` (o "Descartar tudo"), com o Id dele.</summary>
public sealed record EscopoDaBaixaDto(string Tipo, int Id);

/// <summary>
/// Uma linha da baixa: o no (descricao pela regra 19, a do no, senao a do Componente), de onde saiu e quanto. A mesma
/// forma na previa e na baixa gravada. `EhPeca`: o no e a Peca do topo (sem pai), a que a reposicao nomeia; so a baixa
/// gravada o preenche.
/// </summary>
public sealed record LinhaDaBaixaDto(
    int EstruturaItemId, string Descricao, string? Codigo, string OrigemPosicao, int? OrigemSetorId,
    string? OrigemSetorNome, int? OrigemOrdem, decimal Quantidade, bool EhPeca = false);

/// <summary>
/// A baixa projetada (secao 5.2 da spec da Fase 5B-1). `Estornavel`: nao estornada, de Pedido que nao esta
/// `Cancelado`, e de quem le, ou quem le ve as de todos (C8), como a remessa. `PedidoRetrabalho` e `Dispensa` dizem o que
/// aconteceu com a reposicao dela, e `ARepor` conta unidades de Peca (C1 da spec da Fase 5B-2).
/// </summary>
public sealed record BaixaDto(
    int Id, string Motivo, string? Observacao, EscopoDaBaixaDto Escopo, bool Tudo, DateTime DataHora, int UsuarioId,
    string UsuarioNome, bool Estornada, bool Estornavel, IReadOnlyList<LinhaDaBaixaDto> Linhas,
    PedidoRetrabalhoResumoDto? PedidoRetrabalho = null, DispensaDto? Dispensa = null, decimal ARepor = 0m);

/// <summary>Um no cujo fabricado passa a sobrar por causa da baixa (C6).</summary>
public sealed record SobraDaBaixaDto(int EstruturaItemId, string Descricao, decimal Quantidade);

/// <summary>As unidades da Peca do topo que a baixa trava, e quantas dela estao a iniciar (secao 4.6, D13 e D14).</summary>
public sealed record TravaNoTopoDto(int PecaId, string Descricao, decimal Quantidade, decimal AIniciar);

/// <summary>O que a baixa gravaria, sem gravar (C14): as linhas, a sobra que nasce e a trava no topo.</summary>
public sealed record PreviaDaBaixaDto(
    IReadOnlyList<LinhaDaBaixaDto> Linhas, IReadOnlyList<SobraDaBaixaDto> ViramSobra, TravaNoTopoDto? TravaNoTopo);
