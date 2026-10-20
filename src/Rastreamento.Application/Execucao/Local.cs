using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Uma posicao concreta da quantidade de um no: a posicao, e o Setor e o passo (`Ordem` do Roteiro do
/// proprio no) quando ela os tem — spec da Fase 3, secao 3.1. `record struct` de proposito: e chave de
/// dicionario de saldo, e a igualdade por valor e o que faz `AguardandoColeta(3, 2)` achar o saldo
/// gravado com os mesmos tres valores.
/// </summary>
public readonly record struct Local(string Posicao, int? SetorId, int? Ordem)
{
  public static Local AIniciar => new(Posicoes.AIniciar, null, null);
  public static Local NaExpedicao => new(Posicoes.NaExpedicao, null, null);
  public static Local Montado => new(Posicoes.Montado, null, null);
  public static Local Conforme => new(Posicoes.Conforme, null, null);
  public static Local NaoConforme => new(Posicoes.NaoConforme, null, null);
  public static Local AguardandoLiberacao => new(Posicoes.AguardandoLiberacao, null, null);
  public static Local Liberada => new(Posicoes.Liberada, null, null);
  public static Local Expedido => new(Posicoes.Expedido, null, null);
  public static Local NoSetor(int setorId, int ordem) => new(Posicoes.NoSetor, setorId, ordem);
  public static Local AguardandoColeta(int setorId, int ordem) => new(Posicoes.AguardandoColeta, setorId, ordem);
  public static Local AguardandoMontagem(int setorId) => new(Posicoes.AguardandoMontagem, setorId, null);
  public static Local AguardandoConserto(int setorId) => new(Posicoes.AguardandoConserto, setorId, null);
  public static Local EmConserto(int setorId) => new(Posicoes.EmConserto, setorId, null);
  public static Local ConsertoPronto(int setorId) => new(Posicoes.ConsertoPronto, setorId, null);

  public static Local DaOrigem(Movimentacao m) => new(m.OrigemPosicao, m.OrigemSetorId, m.OrigemOrdem);
  public static Local DoDestino(Movimentacao m) => new(m.DestinoPosicao, m.DestinoSetorId, m.DestinoOrdem);
}
