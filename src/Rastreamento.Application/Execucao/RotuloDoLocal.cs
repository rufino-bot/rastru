using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Execucao;

/// <summary>
/// Onde a quantidade esta, para a frase de recusa: "Só há 2 de {nome} {onde}." Um so lugar para a entrega e a baixa
/// dizerem a mesma posicao do mesmo jeito. Le o nome do Setor so quando a posicao tem Setor.
/// </summary>
internal static class RotuloDoLocal
{
  public static string Onde(Local local, Func<int, string> nomeDoSetor)
  {
    string Setor() => nomeDoSetor(local.SetorId!.Value);
    return local.Posicao switch
    {
      Posicoes.AIniciar => "a iniciar",
      Posicoes.NoSetor => $"no Setor {Setor()} (passo {local.Ordem})",
      Posicoes.AguardandoColeta => $"aguardando coleta no Setor {Setor()} (passo {local.Ordem})",
      Posicoes.AguardandoMontagem => $"aguardando montagem no Setor {Setor()}",
      Posicoes.AguardandoConserto => $"aguardando conserto no Setor {Setor()}",
      Posicoes.EmConserto => $"em conserto no Setor {Setor()}",
      Posicoes.ConsertoPronto => $"pronto do conserto no Setor {Setor()}",
      Posicoes.NaExpedicao => "no local de expedição",
      Posicoes.Conforme => "conforme no local de expedição",
      Posicoes.NaoConforme => "não conforme no local de expedição",
      Posicoes.AguardandoLiberacao => "aguardando liberação",
      Posicoes.Liberada => "liberada pela Gestão",
      Posicoes.Montado => "montado no pai",
      Posicoes.Expedido => "expedido",
      Posicoes.Perdido => "perdido",
      _ => local.Posicao,
    };
  }
}
