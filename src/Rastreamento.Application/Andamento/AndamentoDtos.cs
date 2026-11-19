namespace Rastreamento.Application.Andamento;

/// <summary>Os tipos de bloco da trilha (secao 5.3 da spec da Fase 6A), na ordem em que aparecem.</summary>
public static class TiposDeBloco
{
  public const string AIniciar = "AIniciar";
  public const string Passo = "Passo";
  public const string Montagem = "Montagem";
  public const string Conserto = "Conserto";
  public const string Expedicao = "Expedicao";
}

/// <summary>
/// `Atual`: ha quantidade ali agora (e a permanencia media tambem vem quando parte ja passou); `Passado`: nada ali, mas
/// ha permanencia; `Futuro`: nem um nem outro, so no passo do Roteiro.
/// </summary>
public static class SituacoesDoBloco
{
  public const string Atual = "Atual";
  public const string Passado = "Passado";
  public const string Futuro = "Futuro";
}

/// <summary>O bloco com a menor chegada entre as partes que contam do Pedido. `Bloco` e a `Chave` do bloco.</summary>
public sealed record MaisParadoDto(int EstruturaItemId, string Bloco);

/// <summary>
/// Um bloco da trilha de um no. `Chave`: `AIniciar`, `Passo:{ordem}`, `Montagem:{setorId}`, `Conserto:{setorId}` ou
/// `Expedicao`. `Desde` e `AguardandoColetaDesde` sao as chegadas mais antigas (C6) e `PermanenciaMediaSegundos`, a media
/// ponderada do tempo das partes que ja sairam do passo (nula sem nenhuma); `Ordem`, `SetorId` e `Setor` so onde o bloco
/// os tem. `AguardandoColeta` so no bloco de passo, e zero nos outros.
/// </summary>
public sealed record BlocoDaTrilhaDto(
    string Tipo, string Chave, int? Ordem, int? SetorId, string? Setor, string Situacao, decimal Quantidade, DateTime? Desde,
    decimal AguardandoColeta, DateTime? AguardandoColetaDesde, long? PermanenciaMediaSegundos);

/// <summary>Um no (Peca ou Item) com a trilha dele, as complicacoes (C8) sobre ele e os descendentes, e os Itens filhos.</summary>
public sealed record AndamentoDoNoDto(
    int EstruturaItemId, string Codigo, string Descricao, decimal Quantidade, IReadOnlyList<BlocoDaTrilhaDto> Trilha,
    IReadOnlyList<ComplicacaoDto> Complicacoes, IReadOnlyList<AndamentoDoNoDto> Itens);

/// <summary>
/// O andamento de um Pedido (secao 5.3 da spec da Fase 6A). `Agora` e o relogio do servidor, contra o qual o cliente
/// calcula a idade; `InicioReal` e nulo antes da primeira producao, e a pausa e so a que veio depois dele.
/// </summary>
public sealed record AndamentoDoPedidoDto(
    DateTime Agora, DateTime Abertura, DateTime? InicioReal, long PausadoDepoisDoInicioSegundos, MaisParadoDto? MaisParado,
    IReadOnlyList<AndamentoDoNoDto> Pecas);
