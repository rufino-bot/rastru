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
