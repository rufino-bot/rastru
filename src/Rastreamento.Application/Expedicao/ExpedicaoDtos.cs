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
