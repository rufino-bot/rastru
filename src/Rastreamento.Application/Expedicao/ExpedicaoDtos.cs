using Rastreamento.Application.Execucao;

namespace Rastreamento.Application.Expedicao;

/// <summary>`POST /estrutura/{id}/afericoes`: um dos `ResultadosDeAfericao` (`Conforme` | `NaoConforme`).</summary>
public sealed record NovaAfericaoDto(string? Resultado);

/// <summary>
/// `POST /estrutura/{id}/contagens`: quantas das unidades em `NaExpedicao` foram medidas conformes e quantas nao
/// (D4 da spec da Fase 5A). Tudo ou nada; uma das duas pode ser zero, as duas nao.
/// </summary>
public sealed record NovaContagemDto(decimal Conformes, decimal NaoConformes);

/// <summary>`POST /estrutura/{id}/consertos`: o Setor que corrige (qualquer ativo, C19) e quantas nao conformes vao.</summary>
public sealed record NovoConsertoDto(int SetorId, decimal Quantidade);

/// <summary>
/// Uma afericao da Peca (secao 3.4 da spec da Fase 5A). `Estornada`: existe um estorno apontando para ela.
/// `Estornavel`: quem le pode estorna-la agora (C7).
/// </summary>
public sealed record AfericaoDto(
    int Id, string Resultado, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada, bool Estornavel);

/// <summary>
/// Uma contagem da Peca: as duas linhas `Medicao` do livro que apontam para ela, numa linha so (C17 da spec da
/// Fase 5A). `Estornada` e `Estornavel` como em <see cref="AfericaoDto"/>.
/// </summary>
public sealed record ContagemDto(
    int Id, decimal Conformes, decimal NaoConformes, DateTime DataHora, int UsuarioId, string UsuarioNome,
    bool Estornada, bool Estornavel);

/// <summary>
/// `POST /estrutura/{id}/pedidos-de-liberacao`: quantas unidades `NaoConforme` a Qualidade pede a Gestao que
/// libere, e por que (a justificativa e obrigatoria, C6 da spec da Fase 5A).
/// </summary>
public sealed record NovoPedidoDeLiberacaoDto(decimal Quantidade, string? Justificativa);

/// <summary>
/// `POST /liberacoes/{id}/resposta`: quantas das pedidas a Gestao libera, de zero (recusa total) ate a pedida, e
/// a observacao opcional (C5 e C6 da spec da Fase 5A). O resto volta a `NaoConforme`.
/// </summary>
public sealed record RespostaDeLiberacaoDto(decimal QuantidadeLiberada, string? Observacao);

/// <summary>
/// Um registro da liberacao (secao 3.6 da spec da Fase 5A; decisao P2 do plano de backend): o pedido
/// (`Tipo = Pedido`, `Quantidade` a pedida, `Texto` a justificativa) ou a resposta (`Tipo = Resposta`,
/// `PedidoDeLiberacaoId` o pedido, `Quantidade` a liberada, `Texto` a observacao). `Estornada`: as linhas do livro
/// dele foram estornadas. `Estornavel`: quem le pode estorna-lo agora (C7).
/// </summary>
public sealed record LiberacaoDto(
    int Id, string Tipo, int? PedidoDeLiberacaoId, int EstruturaItemId, decimal Quantidade, string? Texto,
    DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada, bool Estornavel);

/// <summary>Uma Peca da remessa e quantas unidades dela saem. A remessa escolhe sozinha de onde tira (C10).</summary>
public sealed record ItemDaRemessaDto(int EstruturaItemId, decimal Quantidade);

/// <summary>
/// `POST /remessas`: uma ou mais Pecas, de um ou mais Pedidos, numa remessa so (C8 e C9 da spec da Fase 5A).
/// Tudo ou nada.
/// </summary>
public sealed record NovaRemessaDto(IReadOnlyList<ItemDaRemessaDto>? Itens);

/// <summary>
/// Uma linha `Expedicao` da remessa: a Peca (descricao pela regra 19), o Pedido dela e de onde a quantidade saiu
/// (`Origem`, uma das posicoes de C10). `PedidoConcluido` e o status do Pedido AGORA, nao o da hora da remessa:
/// e o que a tela usa para dizer que o estorno reabre o Pedido (C20).
/// </summary>
public sealed record LinhaDaRemessaDto(
    int PecaId, string PecaDescricao, string? CodigoDoComponente, int PedidoId, string PedidoNumero,
    bool PedidoConcluido, string Origem, decimal Quantidade);

/// <summary>
/// Uma remessa (C8 e D9 da spec da Fase 5A): quando, quem expediu e as linhas dela. `Estornada`: as linhas dela
/// foram estornadas. `Estornavel`: quem le pode estorna-la agora (C7; nunca a de Pedido `Cancelado`, C13).
/// </summary>
public sealed record RemessaDto(
    int Id, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada, bool Estornavel,
    IReadOnlyList<LinhaDaRemessaDto> Linhas);

/// <summary>Um aviso de expedir (D8) de uma Peca da remessa: um dos `TiposDeAviso` e quantas unidades sairam assim.</summary>
public sealed record AvisoDaPecaDto(int PecaId, string Tipo, decimal Quantidade);

/// <summary>
/// O `201` de `POST /remessas`: a remessa gravada e os avisos de D8, calculados aqui e nao confirmados antes
/// (decisao P5 do plano de backend da Fase 5A: o backend nao trava nem pede confirmacao).
/// </summary>
public sealed record ResultadoDaRemessaDto(RemessaDto Remessa, IReadOnlyList<AvisoDaPecaDto> Avisos);

/// <summary>
/// A quantidade da Peca por posicao do fim do caminho (secao 5.1 da spec da Fase 5A). `EmConserto` soma as tres
/// posicoes do conserto. Sem o `Expedivel`: ele vai ao lado, em <see cref="PecaDaExpedicaoDto"/>.
/// </summary>
public sealed record PosicoesDaPecaDto(
    decimal NaExpedicao, decimal Conforme, decimal NaoConforme, decimal AguardandoLiberacao, decimal Liberada,
    decimal EmConserto, decimal Expedido)
{
  public static PosicoesDaPecaDto De(PosicoesDaPeca p) =>
      new(p.NaExpedicao, p.Conforme, p.NaoConforme, p.AguardandoLiberacao, p.Liberada, p.EmConserto, p.Expedido);
}

/// <summary>Quanto da Peca esta numa posicao do conserto (`AguardandoConserto`, `EmConserto` ou `ConsertoPronto`) de um Setor.</summary>
public sealed record ConsertoDaPecaDto(int SetorId, string SetorNome, string Posicao, decimal Quantidade);

/// <summary>O pedido de liberacao em aberto da Peca (C18: no maximo um), como a lista da expedicao o mostra.</summary>
public sealed record LiberacaoPendenteDaPecaDto(int Id, decimal Quantidade, string Justificativa, DateTime DataHora, string UsuarioNome);

/// <summary>
/// Uma Peca da lista da expedicao (secao 5.1 da spec da Fase 5A). `Situacao`: um de `SemRelatorio`, `Pendente`,
/// `Conforme`, `NaoConforme` (tabela 3.2). `Afericao` e a valida, ou nula. `Expedivel` e o que a remessa pode tirar (C10).
/// </summary>
public sealed record PecaDaExpedicaoDto(
    int Id, string Descricao, string? CodigoDoComponente, int AgrupamentoId, string AgrupamentoCodigo, decimal Quantidade,
    bool RequerRelatorioDimensional, string Situacao, AfericaoDto? Afericao, PosicoesDaPecaDto Posicoes,
    IReadOnlyList<ConsertoDaPecaDto> Consertos, LiberacaoPendenteDaPecaDto? LiberacaoPendente, decimal Expedivel);

/// <summary>Um Pedido da lista da expedicao com as Pecas dele que tem algo no local de expedicao ou em conserto.</summary>
public sealed record PedidoDaExpedicaoDto(
    int Id, string Numero, string Cliente, DateOnly DataEntrega, bool Atrasado, PausaResumoDto? Pausa,
    IReadOnlyList<PecaDaExpedicaoDto> Pecas);

/// <summary>`GET /expedicao`: os Pedidos abertos com Peca no fim do caminho, do prazo mais proximo ao mais distante.</summary>
public sealed record ExpedicaoDto(IReadOnlyList<PedidoDaExpedicaoDto> Pedidos);

/// <summary>Os `Tipo` de <see cref="RegistroDaExpedicaoDto"/>. Sao strings do contrato JSON.</summary>
public static class TiposDeRegistroDaExpedicao
{
  public const string Afericao = nameof(Afericao);
  public const string Contagem = nameof(Contagem);
  public const string Conserto = nameof(Conserto);
  public const string PedidoDeLiberacao = nameof(PedidoDeLiberacao);
  public const string RespostaDeLiberacao = nameof(RespostaDeLiberacao);
  public const string Remessa = nameof(Remessa);
}

/// <summary>
/// Um registro do historico da Peca (`GET /estrutura/{id}/expedicao`; secao 5.1 e C20 da spec da Fase 5A). Os campos
/// que o `Tipo` nao usa vao nulos. `Id` e o do registro do tipo (afericao, contagem, liberacao, remessa) ou, no
/// `Conserto`, o da linha do livro, que e o que o estorno generico recebe. A `Remessa` e uma so por remessa, com a
/// soma das linhas da Peca em `Quantidade`, e nunca `Estornavel` aqui: ela se estorna em "Remessas recentes".
/// </summary>
public sealed record RegistroDaExpedicaoDto(
    string Tipo, int Id, DateTime DataHora, int UsuarioId, string UsuarioNome, string? Resultado, decimal? Conformes,
    decimal? NaoConformes, decimal? Quantidade, string? SetorNome, string? Texto, bool Estornada, bool Estornavel);

/// <summary>A Peca de um pedido de liberacao pendente (regra 19 no nome).</summary>
public sealed record PecaDaLiberacaoDto(int Id, string Descricao, string? CodigoDoComponente);

/// <summary>O Pedido de um pedido de liberacao pendente.</summary>
public sealed record PedidoDaLiberacaoDto(int Id, string Numero, string Cliente, DateOnly DataEntrega, bool Atrasado);

/// <summary>Um item de `GET /liberacoes/pendentes`: o pedido que a Gestao ainda nao respondeu, com a Peca e o Pedido dele.</summary>
public sealed record LiberacaoPendenteDto(
    int Id, decimal Quantidade, string Justificativa, DateTime DataHora, string UsuarioNome, PecaDaLiberacaoDto Peca,
    PedidoDaLiberacaoDto Pedido);
