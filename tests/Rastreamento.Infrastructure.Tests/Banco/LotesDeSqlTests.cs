using Rastreamento.Infrastructure.Banco;

namespace Rastreamento.Infrastructure.Tests.Banco;

public class LotesDeSqlTests
{
  [Fact]
  public void Separa_nas_linhas_que_sao_so_GO()
  {
    var lotes = LotesDeSql.Separar("SELECT 1;\nGO\nSELECT 2;\n  go  \r\nSELECT 3;\nGo");

    Assert.Equal(3, lotes.Count);
    Assert.Equal("SELECT 1;", lotes[0].Trim());
    Assert.Equal("SELECT 2;", lotes[1].Trim());
    Assert.Equal("SELECT 3;", lotes[2].Trim());
  }

  [Fact]
  public void Nao_separa_GO_dentro_de_palavra()
  {
    var script = "GOTO fim;\nSELECT 'CATEGORY';\n-- GO aqui\nSELECT 1; -- GO\nfim:";

    var lotes = LotesDeSql.Separar(script);

    Assert.Single(lotes);
  }

  [Fact]
  public void Lote_vazio_some()
  {
    var lotes = LotesDeSql.Separar("GO\n\nGO\nSELECT 1;\nGO\n   \nGO\n");

    Assert.Single(lotes);
    Assert.Equal("SELECT 1;", lotes[0].Trim());
  }

  [Fact]
  public void GO_com_contagem_lanca()
  {
    var erro = Assert.Throws<FormatException>(() => LotesDeSql.Separar("SELECT 1;\nGO 5\nSELECT 2;"));

    Assert.Contains("GO com contagem não é suportado", erro.Message);
  }

  [Fact]
  public void O_modelo_inteiro_separa_no_numero_de_lotes_medido()
  {
    var modelo = File.ReadAllText(
        Path.Combine(RaizDoRepositorio(), "specs", "02-modelo-de-dados.sql"), System.Text.Encoding.UTF8);

    var lotes = LotesDeSql.Separar(modelo);

    // Medido em 2026-11-25 com `grep -c "^GO" specs/02-modelo-de-dados.sql`: 9 linhas GO, portanto 10 lotes.
    // A tabela dbo.MigracaoAplicada entra no último lote, sem GO novo.
    Assert.Equal(10, lotes.Count);
  }

  private static string RaizDoRepositorio()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rastreamento.slnx")))
      dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
  }
}
