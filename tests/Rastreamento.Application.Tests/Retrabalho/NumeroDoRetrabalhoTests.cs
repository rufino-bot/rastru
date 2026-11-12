using Rastreamento.Application.Retrabalho;
using Xunit;

namespace Rastreamento.Application.Tests.Retrabalho;

public class NumeroDoRetrabalhoTests
{
  [Fact]
  public void Numero_que_cabe_ganha_o_sufixo_inteiro_e_pula_os_ocupados_sem_diferenciar_caixa()
  {
    Assert.Equal("P-1020-R1", NumeroDoRetrabalho.Sugerir("P-1020", []));
    Assert.Equal("P-1020-R3", NumeroDoRetrabalho.Sugerir("P-1020", ["P-1020-R1", "p-1020-r2"]));
  }

  [Fact]
  public void Original_de_trinta_caracteres_e_cortado_para_o_sufixo_caber_e_a_sugestao_nunca_passa_de_trinta()
  {
    var original = new string('A', 30);

    var primeira = NumeroDoRetrabalho.Sugerir(original, []);

    Assert.Equal(new string('A', 27) + "-R1", primeira);
    Assert.Equal(30, primeira.Length);
    // O proximo livre pula o que o corte ja ocupou, e k de dois digitos corta um caractere a mais.
    var ocupados = Enumerable.Range(1, 9).Select(k => new string('A', 28 - 1) + $"-R{k}").ToList();
    var decima = NumeroDoRetrabalho.Sugerir(original, ocupados);
    Assert.Equal(new string('A', 26) + "-R10", decima);
    Assert.Equal(30, decima.Length);
  }

  [Fact]
  public void Original_de_vinte_e_oito_caracteres_ja_nao_cabe_com_o_R1_inteiro_e_perde_um_caractere()
  {
    var original = new string('B', 28);
    Assert.Equal(new string('B', 27) + "-R1", NumeroDoRetrabalho.Sugerir(original, []));
  }

  [Fact]
  public void Prefixo_da_consulta_cobre_todo_candidato_possivel_do_original()
  {
    foreach (var original in new[] { "P-1", new string('A', 22), new string('A', 23), new string('A', 30) })
    {
      var prefixo = NumeroDoRetrabalho.PrefixoDaConsulta(original);
      for (var k = 1; k <= 12; k++)
      {
        var ocupados = new List<string>();
        for (var i = 1; i < k; i++) ocupados.Add(NumeroDoRetrabalho.Sugerir(original, ocupados));
        Assert.StartsWith(prefixo, NumeroDoRetrabalho.Sugerir(original, ocupados), StringComparison.OrdinalIgnoreCase);
      }
    }
  }
}
