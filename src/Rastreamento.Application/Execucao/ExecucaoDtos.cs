namespace Rastreamento.Application.Execucao;

/// <summary>Uma posicao com o nome do Setor resolvido. Ver o "Contrato JSON" do plano 2.</summary>
public sealed record LocalDto(string Posicao, int? SetorId, string? SetorNome, int? Ordem)
{
  internal static LocalDto De(Local local, IReadOnlyDictionary<int, string> nomesDosSetores) =>
      new(local.Posicao, local.SetorId,
          local.SetorId is int setorId ? nomesDosSetores.GetValueOrDefault(setorId) : null,
          local.Ordem);
}

public sealed record MovimentacaoDto(
    int Id, int EstruturaItemId, string Tipo, decimal Quantidade, LocalDto Origem, LocalDto Destino,
    int? MontagemId, int? EstornoDeId, DateTime DataHora, int UsuarioId, string UsuarioNome, bool Estornada);

public sealed record MontagemDto(
    int Id, int EstruturaItemId, int SetorId, string SetorNome, decimal Quantidade, DateTime DataHora,
    int UsuarioId, string UsuarioNome, bool Estornada, IReadOnlyList<MovimentacaoDto> Baixas);

public sealed record InicioDto(int SetorId, decimal Quantidade);

public sealed record TerminoDto(int SetorId, int Ordem, decimal Quantidade);

public sealed record MontagemNovaDto(int SetorId, decimal Quantidade);

/// <summary>
/// `Posicao`: `AguardandoColeta` (com `SetorId` e `Ordem`) ou `AguardandoMontagem` (com `SetorId`, sem
/// `Ordem`). Tudo anulavel de proposito: um corpo incompleto vira 400 `OrigemInvalida` com frase, e nao
/// um erro de binding sem codigo.
/// </summary>
public sealed record OrigemDaEntregaDto(string? Posicao, int? SetorId, int? Ordem);

public sealed record ItemDaEntregaDto(int EstruturaItemId, OrigemDaEntregaDto? Origem, int? DestinoSetorId, decimal Quantidade);

public sealed record EntregaDto(IReadOnlyList<ItemDaEntregaDto>? Itens);
