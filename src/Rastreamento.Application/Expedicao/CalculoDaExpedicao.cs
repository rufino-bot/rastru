using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using PosicoesDoLivro = Rastreamento.Domain.Entities.Posicoes;

namespace Rastreamento.Application.Expedicao;

/// <summary>
/// O que a unidade em `NaExpedicao` e para a Peca, pela afericao (tabela 3.2 da spec da Fase 5A).
/// `SemRelatorio`: a Peca nao exige relatorio. `Pendente`: exige e nao tem afericao valida.
/// </summary>
public enum SituacaoDaAfericao { SemRelatorio, Pendente, Conforme, NaoConforme }

/// <summary>
/// A quantidade de uma Peca no fim do caminho, por posicao (secao 3.1 da spec da Fase 5A). `EmConserto` e a
/// soma das tres posicoes do conserto (`AguardandoConserto`, `EmConserto`, `ConsertoPronto`), de qualquer
/// Setor.
/// </summary>
public sealed record PosicoesDaPeca(
    decimal NaExpedicao, decimal Conforme, decimal NaoConforme, decimal AguardandoLiberacao, decimal Liberada,
    decimal EmConserto, decimal Expedido)
{
  /// <summary>O "no local de expedicao" da regra 9: as cinco posicoes de <see cref="PosicoesDoLivro.NoLocalDeExpedicao"/>.</summary>
  public decimal NoLocalDeExpedicao => NaExpedicao + Conforme + NaoConforme + AguardandoLiberacao + Liberada;

  /// <summary>O que uma remessa pode tirar (C10): tudo do local de expedicao menos `AguardandoLiberacao`.</summary>
  public decimal Expedivel => Liberada + Conforme + NaExpedicao + NaoConforme;
}

/// <summary>Quanto a remessa tira de uma posicao: uma linha `Expedicao` do livro, dessa origem para `Expedido`.</summary>
public sealed record Tirada(string Posicao, decimal Quantidade);

/// <summary>Um aviso de expedir (D8): um dos <see cref="TiposDeAviso"/> e quantas unidades saem assim.</summary>
public sealed record AvisoDeExpedicao(string Tipo, decimal Quantidade);

/// <summary>Os tipos de <see cref="AvisoDeExpedicao"/>, na ordem em que os avisos saem. Sao strings do contrato JSON.</summary>
public static class TiposDeAviso
{
  /// <summary>Saiu de `NaExpedicao` de uma Peca que exige relatorio e nao tem afericao valida.</summary>
  public const string SemAfericao = nameof(SemAfericao);

  /// <summary>Saiu de `NaExpedicao` de uma Peca com afericao nao conforme: a unidade nao foi medida (D4).</summary>
  public const string SemMedicao = nameof(SemMedicao);

  /// <summary>Saiu de `NaoConforme`: medida, nao conforme e sem liberacao da Gestao.</summary>
  public const string NaoConformeSemLiberacao = nameof(NaoConformeSemLiberacao);

  internal static readonly IReadOnlyList<string> NaOrdem = [SemAfericao, SemMedicao, NaoConformeSemLiberacao];
}

/// <summary>
/// As contas do fim do caminho da Peca num lugar so: a afericao valida (secao 3.4 da spec da Fase 5A), a
/// situacao dela (tabela 3.2), as posicoes do local de expedicao, de onde a remessa tira (C10) e o que avisar
/// ao expedir (D8). Puro: quem le o banco e o caso de uso. O front repete `Tirar` e `Avisos` com os mesmos
/// numeros de teste.
/// </summary>
public static class CalculoDaExpedicao
{
  /// <summary>A ordem em que a remessa tira (C10). `AguardandoLiberacao` nao esta nela: espera a Gestao.</summary>
  private static readonly IReadOnlyList<string> OrdemDaTirada =
      [PosicoesDoLivro.Liberada, PosicoesDoLivro.Conforme, PosicoesDoLivro.NaExpedicao, PosicoesDoLivro.NaoConforme];

  /// <summary>
  /// A ultima afericao nao estornada (por `DataHora`, `Id` desempatando), ou nula. Estornada e a que tem um
  /// `Estorno` apontando para ela; o proprio estorno nunca e a valida.
  /// </summary>
  public static RelatorioDimensional? AfericaoValida(IEnumerable<RelatorioDimensional> registrosDaPeca)
  {
    var registros = registrosDaPeca as IReadOnlyCollection<RelatorioDimensional> ?? registrosDaPeca.ToList();
    var estornadas = registros
        .Where(r => r.Tipo == TiposDeAfericao.Estorno && r.EstornoDeId is not null)
        .Select(r => r.EstornoDeId!.Value)
        .ToHashSet();

    return registros
        .Where(r => r.Tipo == TiposDeAfericao.Afericao && !estornadas.Contains(r.Id))
        .OrderByDescending(r => r.DataHora).ThenByDescending(r => r.Id)
        .FirstOrDefault();
  }

  /// <summary>A linha da tabela 3.2. A marca desligada vence: sem `RequerRelatorioDimensional`, a afericao nao conta.</summary>
  public static SituacaoDaAfericao Situacao(bool requerRelatorio, RelatorioDimensional? valida)
  {
    if (!requerRelatorio) return SituacaoDaAfericao.SemRelatorio;
    if (valida is null) return SituacaoDaAfericao.Pendente;
    return valida.Resultado switch
    {
      ResultadosDeAfericao.Conforme => SituacaoDaAfericao.Conforme,
      ResultadosDeAfericao.NaoConforme => SituacaoDaAfericao.NaoConforme,
      _ => throw new ArgumentException(
          $"Aferição {valida.Id} sem resultado válido ('{valida.Resultado}').", nameof(valida)),
    };
  }

  /// <summary>
  /// Soma os saldos da Peca por posicao. As posicoes de producao (`AIniciar`, `NoSetor`, `AguardandoColeta`,
  /// `AguardandoMontagem`, `Montado`) ficam de fora; as tres do conserto somam em `EmConserto`.
  /// </summary>
  public static PosicoesDaPeca Posicoes(IEnumerable<(Local Local, decimal Quantidade)> saldosDaPeca)
  {
    decimal naExpedicao = 0, conforme = 0, naoConforme = 0, aguardandoLiberacao = 0, liberada = 0,
        emConserto = 0, expedido = 0;
    foreach (var (local, quantidade) in saldosDaPeca)
    {
      switch (local.Posicao)
      {
        case PosicoesDoLivro.NaExpedicao: naExpedicao += quantidade; break;
        case PosicoesDoLivro.Conforme: conforme += quantidade; break;
        case PosicoesDoLivro.NaoConforme: naoConforme += quantidade; break;
        case PosicoesDoLivro.AguardandoLiberacao: aguardandoLiberacao += quantidade; break;
        case PosicoesDoLivro.Liberada: liberada += quantidade; break;
        case PosicoesDoLivro.AguardandoConserto:
        case PosicoesDoLivro.EmConserto:
        case PosicoesDoLivro.ConsertoPronto: emConserto += quantidade; break;
        case PosicoesDoLivro.Expedido: expedido += quantidade; break;
      }
    }
    return new PosicoesDaPeca(naExpedicao, conforme, naoConforme, aguardandoLiberacao, liberada, emConserto, expedido);
  }

  /// <summary>
  /// De onde a remessa tira <paramref name="quantidade"/>, na ordem de C10, uma <see cref="Tirada"/> por
  /// posicao que contribui (posicao vazia nao entra). Nula quando passa do <see cref="PosicoesDaPeca.Expedivel"/>.
  /// Quantidade nao positiva e erro de quem chama: o caso de uso a recusa antes (`QuantidadeInvalida`).
  /// </summary>
  public static IReadOnlyList<Tirada>? Tirar(PosicoesDaPeca p, decimal quantidade)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidade);
    if (quantidade > p.Expedivel) return null;

    var tiradas = new List<Tirada>();
    var falta = quantidade;
    foreach (var posicao in OrdemDaTirada)
    {
      if (falta == 0) break;
      var tira = Math.Min(falta, Saldo(p, posicao));
      if (tira <= 0) continue;
      tiradas.Add(new Tirada(posicao, tira));
      falta -= tira;
    }
    return tiradas;
  }

  /// <summary>
  /// Os avisos de expedir (D8), um por tipo, com a quantidade somada; tipo com zero nao sai. `NaExpedicao`
  /// avisa conforme a situacao (tabela 3.2); `NaoConforme` sempre avisa; `Liberada` e `Conforme` nunca.
  /// </summary>
  public static IReadOnlyList<AvisoDeExpedicao> Avisos(SituacaoDaAfericao s, IReadOnlyList<Tirada> tiradas)
  {
    var porTipo = new Dictionary<string, decimal>();
    foreach (var tirada in tiradas)
    {
      var tipo = TipoDoAviso(s, tirada.Posicao);
      if (tipo is null) continue;
      porTipo[tipo] = porTipo.GetValueOrDefault(tipo) + tirada.Quantidade;
    }
    return TiposDeAviso.NaOrdem
        .Where(t => porTipo.GetValueOrDefault(t) > 0)
        .Select(t => new AvisoDeExpedicao(t, porTipo[t]))
        .ToList();
  }

  private static string? TipoDoAviso(SituacaoDaAfericao s, string origem) => origem switch
  {
    PosicoesDoLivro.NaExpedicao => s switch
    {
      SituacaoDaAfericao.Pendente => TiposDeAviso.SemAfericao,
      SituacaoDaAfericao.NaoConforme => TiposDeAviso.SemMedicao,
      _ => null,
    },
    PosicoesDoLivro.NaoConforme => TiposDeAviso.NaoConformeSemLiberacao,
    _ => null,
  };

  private static decimal Saldo(PosicoesDaPeca p, string posicao) => posicao switch
  {
    PosicoesDoLivro.Liberada => p.Liberada,
    PosicoesDoLivro.Conforme => p.Conforme,
    PosicoesDoLivro.NaExpedicao => p.NaExpedicao,
    PosicoesDoLivro.NaoConforme => p.NaoConforme,
    _ => throw new ArgumentOutOfRangeException(nameof(posicao), posicao, "Posição fora da ordem de C10."),
  };
}
