using Rastreamento.Application.Arquivos;

namespace Rastreamento.Application.Tests.Arquivos;

public class ValidadorDeArquivoStlTests
{
  [Fact]
  public void Stl_binario_bem_formado_passa()
  {
    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.CuboBinario()));
  }

  [Fact]
  public void O_cubo_de_teste_tem_684_bytes_exatos()
  {
    // Guarda a fixture, nao o validador: se o gerador quebrar, os outros testes passariam a
    // afirmar sobre um arquivo diferente do que este plano descreve.
    Assert.Equal(684, StlDeTeste.CuboBinario().Length);
    Assert.Equal(StlDeTeste.TamanhoEsperado, StlDeTeste.CuboBinario().Length);
  }

  [Fact]
  public void Stl_ascii_bem_formado_passa()
  {
    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.Ascii()));
  }

  [Fact]
  public void Binario_cujo_cabecalho_comeca_com_solid_passa_como_binario()
  {
    // A armadilha que obriga a tentar a forma binaria ANTES da ASCII: este arquivo satisfaz
    // "comeca com solid" e NAO e ASCII. Se a ordem inverter, ele e lido como ASCII e a fórmula
    // 84+50n nunca e conferida.
    Assert.Null(ValidadorDeArquivoStl.Validar(
        "cubo.stl", StlDeTeste.BinarioComCabecalhoQueDizSolid()));
  }

  [Fact]
  public void Extensao_diferente_de_stl_e_recusada()
  {
    var erro = ValidadorDeArquivoStl.Validar("cubo.step", StlDeTeste.CuboBinario());
    Assert.NotNull(erro);
    Assert.Contains(".stl", erro);
  }

  [Theory]
  [InlineData("CUBO.STL")]
  [InlineData("cubo.Stl")]
  public void Extensao_stl_e_aceita_em_qualquer_caixa(string nome)
  {
    Assert.Null(ValidadorDeArquivoStl.Validar(nome, StlDeTeste.CuboBinario()));
  }

  [Fact]
  public void Arquivo_vazio_e_recusado()
  {
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", []));
  }

  [Fact]
  public void Acima_do_limite_de_16_MiB_e_recusado()
  {
    var grande = new byte[ValidadorDeArquivoStl.TamanhoMaximoEmBytes + 1];
    var erro = ValidadorDeArquivoStl.Validar("cubo.stl", grande);
    Assert.NotNull(erro);
    Assert.Contains("16", erro);
  }

  [Fact]
  public void No_limite_exato_de_16_MiB_e_aceito()
  {
    // Fecha a lacuna achada na review: so existia teste para 16 MiB + 1 (recusado). Um STL
    // binario de EXATAMENTE 16 MiB nao existe pela formula 84+50n -- (16*1024*1024 - 84) / 50 nao
    // e inteiro -- entao a fixture precisa ser ASCII. EhAsciiCoerente le so os 4096 primeiros
    // bytes (StartsWith "solid" e Contains "facet normal"); o preenchimento apos o cabeçalho ASCII
    // fica fora dessa amostra e nao interfere na checagem estrutural.
    var conteudo = new byte[ValidadorDeArquivoStl.TamanhoMaximoEmBytes];
    StlDeTeste.Ascii().CopyTo(conteudo, 0);
    // Comprimento afirmado explicitamente, nao presumido da alocacao: deixa claro que a fixture
    // tem o tamanho EXATO do limite, nem um byte a menos nem a mais.
    Assert.Equal(ValidadorDeArquivoStl.TamanhoMaximoEmBytes, conteudo.Length);

    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", conteudo));
  }

  [Fact]
  public void Ascii_com_espaco_em_branco_antes_do_cabecalho_e_aceito()
  {
    // Fecha a lacuna achada na review: nenhuma fixture tinha espaco em branco antes de "solid",
    // entao a robustez que o TrimStart() promete (aceitar STL ASCII com espaco/quebra de linha
    // antes do cabecalho) nao estava provada por nenhum teste.
    var comEspaco = System.Text.Encoding.UTF8.GetBytes("   \n")
        .Concat(StlDeTeste.Ascii())
        .ToArray();

    Assert.Null(ValidadorDeArquivoStl.Validar("cubo.stl", comEspaco));
  }

  [Fact]
  public void Binario_com_contagem_de_triangulos_que_nao_bate_com_o_tamanho_e_recusado()
  {
    // O coracao da terceira camada: 12 triangulos declarados, um triangulo a menos gravado.
    var bytes = StlDeTeste.Binario(StlDeTeste.Triangulos);
    var truncado = bytes[..^50];
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", truncado));
  }

  [Fact]
  public void Binario_com_bytes_de_sobra_no_fim_e_recusado()
  {
    // O caso que so a comparacao "==" (contra "84+50n") pega: a contagem de triangulos do
    // cabecalho bate com um arquivo MENOR do que o real. Achado por mutacao (Step 6.2 do plano):
    // trocar "==" por ">=" no validador nao quebra nenhum outro teste da suite, porque truncar
    // sempre deixa o arquivo MENOR, nunca maior -- so bytes de sobra no FIM expoem essa mutacao.
    var comSobra = StlDeTeste.CuboBinario().Concat(new byte[10]).ToArray();
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", comSobra));
  }

  [Fact]
  public void Binario_com_zero_triangulos_e_recusado()
  {
    // 84 bytes, estruturalmente coerente, e nao e solido nenhum.
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", StlDeTeste.Binario(0)));
  }

  [Fact]
  public void Pdf_renomeado_para_stl_e_recusado()
  {
    // Este e o caso que motiva a terceira camada existir. Sem ela, o arquivo sobe e o defeito
    // aparece la no viewer, longe da causa.
    var pdf = System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", pdf));
  }

  [Fact]
  public void Texto_que_comeca_com_solid_mas_nao_tem_facet_e_recusado()
  {
    var quase = System.Text.Encoding.UTF8.GetBytes("solid mentira\nendsolid mentira\n");
    Assert.NotNull(ValidadorDeArquivoStl.Validar("cubo.stl", quase));
  }
}
