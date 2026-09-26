using Rastreamento.Application.Execucao;
using Rastreamento.Domain.Entities;
using Xunit;
using static Rastreamento.Application.Tests.Execucao.CenarioDeExecucao;

namespace Rastreamento.Application.Tests.Execucao;

/// <summary>
/// A regra 9 como PROPRIEDADE (spec da Fase 3, secao 9.1): uma sequencia aleatoria de iniciar, terminar,
/// entregar, montar e estornar, com semente FIXA, sobre uma arvore de tres niveis. Depois de CADA passo:
/// nenhuma posicao negativa, a soma das posicoes de cada no igual a quantidade dele, o montado de cada
/// filho igual as montagens validas do pai vezes a razao, e uma operacao recusada nao deixa rastro.
/// Os sorteios escolhem sobretudo operacoes plausiveis a partir do estado — e tambem quantidades uma
/// unidade acima do saldo, para o caminho de recusa correr junto.
/// </summary>
public class ConservacaoTests
{
  private static readonly CancellationToken Ct = CancellationToken.None;
  private const int Passos = 600;

  [Fact]
  public async Task Nenhuma_sequencia_de_operacoes_deixa_posicao_negativa_nem_muda_o_total()
  {
    var c = new CenarioDeExecucao();
    // A (Peca, 6) <- B (razao 2) <- D (razao 3); A <- C (razao 1)
    c.No(1, null, 6m, null, Solda, Pintura);
    c.No(2, 1, 12m, 2m, Corte, Solda);
    c.No(3, 1, 6m, 1m, Dobra);
    c.No(4, 2, 36m, 3m, Corte);
    int[] nos = [1, 2, 3, 4];
    var apontamento = c.Apontamento();
    var entrega = c.Entrega();
    var estorno = c.Estorno();
    var sorteio = new Random(20260925);
    var sucessos = new Dictionary<string, int>
    {
      ["iniciar"] = 0, ["terminar"] = 0, ["entregar"] = 0, ["montar"] = 0, ["estornar"] = 0, ["estornar montagem"] = 0,
    };

    for (var passo = 0; passo < Passos; passo++)
    {
      var calc = c.Calcular();
      var movimentosAntes = c.Execucao.Movimentacoes.Count;
      var montagensAntes = c.Execucao.Montagens.Count(g => g.EstornadaEm is null);
      string operacao;
      bool ok;

      switch (sorteio.Next(10))
      {
        case 0 or 1:
        {
          var id = nos[sorteio.Next(nos.Length)];
          var primeiro = calc.PrimeiroPasso(id)!.Value;
          operacao = "iniciar";
          ok = (await apontamento.Iniciar(id, new InicioDto(primeiro.SetorId, Quantidade(sorteio, calc.Saldo(id, Local.AIniciar))), Operador, Ct)).Sucesso;
          break;
        }
        case 2 or 3:
        {
          var baldes = Baldes(calc, nos, Posicoes.NoSetor);
          if (baldes.Count == 0) continue;
          var (id, local, saldo) = baldes[sorteio.Next(baldes.Count)];
          operacao = "terminar";
          ok = (await apontamento.Terminar(id, new TerminoDto(local.SetorId!.Value, local.Ordem!.Value, Quantidade(sorteio, saldo)), Operador, Ct)).Sucesso;
          break;
        }
        case 4 or 5 or 6:
        {
          var baldes = Baldes(calc, nos, Posicoes.AguardandoColeta).Concat(Baldes(calc, nos, Posicoes.AguardandoMontagem)).ToList();
          if (baldes.Count == 0) continue;
          var (id, local, saldo) = baldes[sorteio.Next(baldes.Count)];
          int? destino = null;
          if (calc.No(id).PaiId is int pai
              && (local.Posicao == Posicoes.AguardandoMontagem || calc.ProximoPasso(id, local.Ordem!.Value) is null))
          {
            var possiveis = calc.SetoresDoRoteiro(pai);
            destino = possiveis[sorteio.Next(possiveis.Count)];
          }
          operacao = "entregar";
          ok = (await entrega.Entregar(new EntregaDto(
              [new ItemDaEntregaDto(id, new OrigemDaEntregaDto(local.Posicao, local.SetorId, local.Ordem), destino, Quantidade(sorteio, saldo))]),
              Movimentador, Ct)).Sucesso;
          break;
        }
        case 7:
        {
          var pai = sorteio.Next(2) == 0 ? 1 : 2;
          var setores = calc.Filhos(pai).SelectMany(f => calc.SetoresOndeAguardaMontagem(f.Id)).Distinct().ToList();
          var setor = setores.Count > 0 ? setores[sorteio.Next(setores.Count)] : Solda;
          operacao = "montar";
          ok = (await apontamento.Montar(pai, new MontagemNovaDto(setor, 1m), Operador, Ct)).Sucesso;
          break;
        }
        case 8:
        {
          if (c.Execucao.Movimentacoes.Count == 0) continue;
          var movimento = c.Execucao.Movimentacoes[sorteio.Next(c.Execucao.Movimentacoes.Count)];
          operacao = "estornar";
          ok = (await estorno.EstornarMovimentacao(movimento.Id, Pcp, podeEstornarDeOutros: true, Ct)).Sucesso;
          break;
        }
        default:
        {
          if (c.Execucao.Montagens.Count == 0) continue;
          var montagem = c.Execucao.Montagens[sorteio.Next(c.Execucao.Montagens.Count)];
          operacao = "estornar montagem";
          ok = (await estorno.EstornarMontagem(montagem.Id, Pcp, podeEstornarDeOutros: true, Ct)).Sucesso;
          break;
        }
      }

      if (ok) sucessos[operacao]++;
      else
      {
        Assert.True(c.Execucao.Movimentacoes.Count == movimentosAntes, $"passo {passo}: {operacao} recusado gravou movimento");
        Assert.True(c.Execucao.Montagens.Count(g => g.EstornadaEm is null) == montagensAntes,
            $"passo {passo}: {operacao} recusado mexeu em montagem");
      }
      AfirmarRegra9(c, nos, passo, operacao);
    }

    Assert.All(sucessos, kv => Assert.True(kv.Value > 0, $"a semente nunca exercitou '{kv.Key}' com sucesso"));
  }

  private static void AfirmarRegra9(CenarioDeExecucao c, int[] nos, int passo, string operacao)
  {
    var calc = c.Calcular();
    foreach (var id in nos)
    {
      var saldos = calc.Saldos(id);
      Assert.All(saldos, s => Assert.True(s.Quantidade >= 0m,
          $"passo {passo} ({operacao}): no {id} ficou com {s.Quantidade} em {s.Local}"));
      // Identidade por construcao, nao verificacao independente: todo movimento grava +q numa posicao e
      // -q noutra do MESMO no (ver `Livro.SomarSaldos`), entao a soma bate mesmo se o caso de uso deixar
      // passar algo indevido — a asercao fica so como documentacao viva do invariante. Quem pega defeito
      // de verdade sao a asercao de nao-negatividade (`s.Quantidade >= 0m`, no `Assert.All` dos saldos)
      // e a de `montado == totalMontado(pai) x razao` no bloco `if (calc.No(id).PaiId is int pai)`.
      Assert.Equal(calc.No(id).Quantidade, saldos.Sum(s => s.Quantidade));
      Assert.True(calc.TotalMontado(id) <= calc.No(id).Quantidade, $"passo {passo}: no {id} montado alem da quantidade");

      if (calc.No(id).PaiId is int pai)
      {
        var esperado = calc.TotalMontado(pai) * calc.No(id).QuantidadePorPai!.Value;
        Assert.Equal(esperado, calc.Saldo(id, Local.Montado));
      }
    }
  }

  private static decimal Quantidade(Random sorteio, decimal saldo)
  {
    // De 1 ate uma unidade ACIMA do saldo: a ultima opcao exercita a recusa.
    var teto = (int)Math.Floor(saldo) + 1;
    return teto <= 1 ? 1m : sorteio.Next(1, teto + 1);
  }

  private static List<(int Id, Local Local, decimal Saldo)> Baldes(CalculadoraDeExecucao calc, int[] nos, string posicao) =>
      nos.SelectMany(id => calc.Saldos(id)
              .Where(s => s.Local.Posicao == posicao && s.Quantidade > 0m)
              .Select(s => (Id: id, Local: s.Local, Saldo: s.Quantidade)))
          .ToList();

}
