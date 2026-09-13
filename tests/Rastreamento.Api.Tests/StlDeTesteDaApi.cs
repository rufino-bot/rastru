namespace Rastreamento.Api.Tests;

/// <summary>
/// STL binario valido, gerado em codigo -- so o suficiente para os testes de PONTA A PONTA do
/// solido (<see cref="SolidoEndpointsTests"/> e o caso "com solido" de
/// <see cref="ComponentesEndpointsTests"/>).
///
/// <para>
/// DUPLICADO de proposito em relacao a <c>StlDeTeste</c>
/// (<c>Rastreamento.Application.Tests.Arquivos</c>): a Task 4 mediu que <c>Api.Tests</c> nao
/// referencia o projeto de teste `Application.Tests`, e os dois projetos de teste nao compartilham
/// nenhuma pasta/projeto de fixtures (nenhum `.csproj` de teste referencia outro). Criar um projeto
/// novo so para ~15 linhas de fixture seria caro demais -- decisao do proprio brief da Task 4.
/// So o CUBO valido e um invalido entram aqui: as variantes ASCII/cabecalho-mentiroso so importam
/// para <c>ValidadorDeArquivoStlTests</c>, que ja vive em `Application.Tests`.
/// </para>
/// </summary>
public static class StlDeTesteDaApi
{
  private const int Triangulos = 12;

  /// <summary>
  /// Um cubo (6 faces, 2 triangulos cada = 12) tem 84 + 50*12 = 684 bytes EXATOS de STL binario
  /// valido. A geometria e irrelevante para a validacao -- os 80 bytes de cabecalho ficam zerados.
  /// </summary>
  public static byte[] CuboBinario()
  {
    var bytes = new byte[84 + 50 * Triangulos];
    BitConverter.GetBytes(Triangulos).CopyTo(bytes, 80);
    return bytes;
  }

  /// <summary>
  /// Nem binario nem ASCII coerente: tamanho curto demais para o cabecalho binario (84 bytes
  /// minimos) e sem o prefixo "solid" do ASCII. Serve so para o caminho "extensao .stl, conteudo
  /// lixo" -- 400 por ESTRUTURA, nao por extensao.
  /// </summary>
  public static byte[] Invalido() => [1, 2, 3, 4, 5];
}
