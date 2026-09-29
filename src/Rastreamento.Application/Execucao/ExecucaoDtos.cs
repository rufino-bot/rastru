using System.ComponentModel.DataAnnotations;
using Rastreamento.Domain.Abstractions;

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

/// <summary>
/// `Posicao`: `AguardandoColeta` (com `SetorId` e `Ordem`) ou `AguardandoMontagem` (com `SetorId`, sem
/// `Ordem`). Tudo anulavel de proposito: um corpo incompleto vira 400 `OrigemInvalida` com frase, e nao
/// um erro de binding sem codigo.
/// </summary>
public sealed record OrigemDaEntregaDto(string? Posicao, int? SetorId, int? Ordem);

/// <summary>
/// `DestinoSetorId` fica no contrato so para ser recusado com `DestinoIndevido` quando vier preenchido
/// (desvio D2 do plano da Fase 3D).
/// </summary>
public sealed record ItemDaEntregaDto(int EstruturaItemId, OrigemDaEntregaDto? Origem, int? DestinoSetorId, decimal Quantidade);

public sealed record EntregaDto(IReadOnlyList<ItemDaEntregaDto>? Itens);

/// <summary>Os Setores do Roteiro, em ordem; repetir um Setor e voltar a ele (regra 21).</summary>
public sealed record RoteiroNovoDto(IReadOnlyList<int>? Passos);

public sealed record PassoDoRoteiroDoNoDto(int SetorId, string Nome, int Ordem, bool Alcancado);

public sealed record RoteiroDoNoDto(int EstruturaItemId, IReadOnlyList<PassoDoRoteiroDoNoDto> Passos);

/// <summary>
/// Um no nas telas de fila e tarefas, com o caminho "Pedido > Agrupamento > pai" em campos separados —
/// quem monta o texto e o front. `Descricao` ja com o fallback da regra 19.
/// </summary>
public sealed record NoResumoDto(
    int Id, string Descricao, string? CodigoDoComponente, int PedidoId, string PedidoNumero,
    int AgrupamentoId, string AgrupamentoCodigo, int? PaiId, string? PaiDescricao, PausaResumoDto? Pausa);

/// <summary>A pausa aberta como as telas a mostram: desde quando, por quem, e o motivo.</summary>
public sealed record PausaResumoDto(DateTime Desde, string PorUsuarioNome, string? Motivo)
{
  public static PausaResumoDto? De(PausaAberta? pausa) =>
      pausa is null ? null : new PausaResumoDto(pausa.PausadoEm, pausa.PausadoPorNome, pausa.Motivo);
}

public sealed record PausaDto(
    int Id, int PedidoId, DateTime PausadoEm, int PausadoPorUsuarioId, string PausadoPorNome, string? Motivo,
    DateTime? RetomadoEm, int? RetomadoPorUsuarioId, string? RetomadoPorNome);

/// <summary>`MaxLength` espelha o NVARCHAR(200) de `dbo.PedidoPausa.Motivo`.</summary>
public sealed record NovaPausaDto([MaxLength(200)] string? Motivo);

public sealed record SetorResumoDto(int Id, string Nome);

/// <summary>
/// `Tipo`: ProximoPasso | Expedicao | Montagem. Em `Montagem`, `SetorId`/`SetorNome` sao o primeiro
/// passo do pai (sem `Ordem`: aguardar montagem nao tem passo) e `PaiSemRoteiro` diz quando nao ha
/// destino possivel.
/// </summary>
public sealed record DestinoDto(string Tipo, int? SetorId, string? SetorNome, int? Ordem, int? PaiId, bool PaiSemRoteiro);

/// <summary>Quem le a fila: o autor ve os proprios registros estornaveis; PCP e Administrador, todos.</summary>
public sealed record QuemLe(int UsuarioId, bool VeRegistrosDeTodos);

/// <summary>
/// Um registro por tras de uma linha da fila que ainda da para estornar (spec da Fase 3D, secao 2.4).
/// `Tipo`: Inicio | Termino | Montagem — `Montagem` e o inicio de um pai, que consumiu os filhos, e se
/// estorna pela rota da montagem.
/// </summary>
public sealed record EstornavelDto(string Tipo, int Id, decimal Quantidade, int UsuarioId, string UsuarioNome, DateTime DataHora);

public sealed record LinhaDaFilaDto(NoResumoDto No, int Ordem, decimal Quantidade, IReadOnlyList<EstornavelDto> Estornaveis);

public sealed record LinhaAguardandoColetaDto(
    NoResumoDto No, int Ordem, decimal Quantidade, DestinoDto Destino, IReadOnlyList<EstornavelDto> Estornaveis);

public sealed record FilhoNaMontagemDto(
    NoResumoDto No, decimal QuantidadePorPai, decimal Presente, decimal? NecessarioParaProxima, decimal? FaltaParaProxima);

/// <summary>
/// Um pai com filhos aguardando neste Setor. `IniciaAqui`: este Setor e o primeiro passo do pai, onde
/// iniciar o pai consome os filhos (spec da Fase 3D, secao 2.1). `PrimeiroPassoDoPai`: para onde levar
/// os filhos quando nao e aqui; nulo se o pai nao tem Roteiro.
/// </summary>
public sealed record MontagemPendenteDto(
    NoResumoDto Pai, decimal FaltaMontar, decimal DaParaMontar, bool IniciaAqui, SetorResumoDto? PrimeiroPassoDoPai,
    IReadOnlyList<FilhoNaMontagemDto> Filhos);

/// <summary>`Origem`: UltimoPasso (com `Ordem`) | Montagem (sem `Ordem`; excesso no nivel do no).</summary>
public sealed record LinhaDeSobraDto(
    NoResumoDto No, string Origem, int? Ordem, decimal Quantidade, bool EmMaisDeUmSetor, IReadOnlyList<EstornavelDto> Estornaveis);

public sealed record FilaDoSetorDto(
    int SetorId, string SetorNome, string? SetorAtividade,
    IReadOnlyList<LinhaDaFilaDto> AIniciar,
    IReadOnlyList<LinhaDaFilaDto> EmTrabalho,
    IReadOnlyList<LinhaAguardandoColetaDto> AguardandoColeta,
    IReadOnlyList<MontagemPendenteDto> AguardandoMontagem,
    IReadOnlyList<LinhaDeSobraDto> Sobra);

public sealed record TarefaDto(NoResumoDto No, int Ordem, decimal Quantidade, DestinoDto Destino);

public sealed record TarefasDoSetorDto(int SetorId, string SetorNome, IReadOnlyList<TarefaDto> Itens);

public sealed record ContagemDeTarefasDto(int Total);

public sealed record SaldoDto(string Posicao, int? SetorId, string? SetorNome, int? Ordem, decimal Quantidade);

/// <summary>`TotalMontado`: numero nos nos com filhos (zero inclusive), nulo nos sem filhos.</summary>
public sealed record PosicoesDoNoDto(int EstruturaItemId, IReadOnlyList<SaldoDto> Saldos, decimal? TotalMontado);

/// <summary>`Montagens`: as montagens em que o no e o PAI montado.</summary>
public sealed record LivroDoNoDto(IReadOnlyList<MovimentacaoDto> Movimentacoes, IReadOnlyList<MontagemDto> Montagens);
