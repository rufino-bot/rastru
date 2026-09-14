# Spike de busca de peça por foto — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** medir, sobre ~20 partes planas reais, quantas vezes a peça certa fica no top-3 quando a foto do celular é comparada com a silhueta de todos os STL candidatos — e decidir, pelo critério fixado na spec, se a busca por foto vira fase.

**Architecture:** console .NET isolado em `spikes/busca-por-foto/`, fora do `Rastreamento.slnx`. Uma biblioteca (`BuscaPorFoto`) com cinco etapas independentes — marcador, STL→silhueta, foto→recorte, pontuação, relatório —, um CLI fino por cima e um projeto de teste que exercita tudo com dados sintéticos gerados em código. Os dados reais só entram na Task 8.

**Tech Stack:** .NET 10 (`net10.0`), OpenCvSharp4 + OpenCvSharp4.runtime.win `4.13.0.20260627`, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-14-spike-busca-por-foto-design.md` — leia antes de qualquer task.

## Global Constraints

- **Branch `spike-busca-por-foto`**, worktree `C:/wt-spike`. Nunca na branch da Fase 2B.
- **Tudo em `spikes/busca-por-foto/`.** Nenhum arquivo fora dela muda, exceto na Task 8 (`specs/06-roadmap-mvp.md` e o relatório).
- **Fora do `Rastreamento.slnx`.** Build e teste só pelo `spikes/busca-por-foto/BuscaPorFoto.slnx`.
- **`TreatWarningsAsErrors` em todos os projetos; o build tem de sair com 0 avisos.** O OpenCvSharp traz um analisador próprio: `OCVS002` reprova ler `Mat.Width`/`Mat.Height` dentro de laço — guarde em variável antes.
- **Versões exatas:** `OpenCvSharp4` e `OpenCvSharp4.runtime.win` em `4.13.0.20260627` (medido em 2026-09-14 nesta máquina: restaura, traz ArUco, detecta marcador gerado depois de distorção em perspectiva).
- **Dados de cliente nunca no git.** Nenhum STL, foto, sobreposição ou código real de peça em arquivo versionado. O repositório é **público**.
- **Os pontuadores recebem só `Mascara`** (pixels + mm por pixel). Nada de nome de arquivo, código ou índice chega a eles (spec §4.5).
- **Ranking pessimista:** empate conta contra a peça certa (spec §7.3).
- **Critério de decisão (spec §6), literal:** partes planas, top-3, pontuador **IoU**, melhor das duas condições (empate → sem marcador); aprova com ≥ ⌈0,8·n⌉, reprova abaixo de ⌈0,6·n⌉. Com n = 20: aprova ≥ 16, reprova ≤ 11.
- **Nomes de domínio em português, técnicos em inglês**; cite alvo **pelo nome** em comentário, nunca por `arquivo:linha` nem por distância ("o teste acima") — `CLAUDE.md`, seção "Convenção de citação".
- **Commit por task**, terminando a mensagem com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Como este plano foi medido

Todo o código abaixo foi escrito, compilado e testado **antes** de entrar no plano, numa pasta de rascunho, e montado do zero **task a task, na ordem deste documento**: cada task compila sozinha sobre as anteriores com 0 avisos, e a suíte dá **6 → 13 → 16 → 23 → 34 → 42 → 63** testes ao fim das Tasks 1 a 7. A suíte final rodou 5 vezes seguidas, verde nas 5. O código deste documento foi **copiado por script** dos arquivos testados, não redigitado.

A medição achou defeitos de duas naturezas no primeiro rascunho. Os **silenciosos** — tudo verde com o defeito presente, e o implementer os copiaria sem aviso — foram estes quatro, todos corrigidos no código abaixo:

1. **O teste do apagamento do marcador passava sem o apagamento**, porque a chapa da cena era maior que o marcador e vencia de qualquer jeito. A cena foi refeita com a chapa menor (Task 4).
2. **O teste de "só partes planas" passava contando as soldadas**, porque as soldadas do teste nunca acertavam. O gerador do teste foi corrigido (Task 7).
3. **O travessão do texto do PDF virava `?`**: `Encoding.Latin1` não tem U+2014 e troca em silêncio. O texto foi trocado e entrou uma guarda — **sem teste**, porque os textos são constantes privadas (Task 1).
4. **O ruído do teste de recorte só somava**: gerado num `Mat` de 8 bits, a metade negativa era cortada em zero, e o teste era mais fácil do que dizia. Agora é gerado com sinal (Task 4).

Os **ruidosos** — que o build ou a suíte denunciariam — incluíram os eixos principais saírem girados 16° (ponderar vértices depende da triangulação; o momento exato do triângulo não — Task 2), o teste do xref achando o `startxref`, o analisador `OCVS002` do OpenCvSharp, e uma instrução de mutação que não compilava (`CS0162`). Listados para quem for mexer nessas partes saber onde já doeu, não como contagem exaustiva.

**O que isto NÃO mede:** nada sobre foto real. Os dados sintéticos têm fundo uniforme, peça de cor chapada e perspectiva moderada — exatamente o caso fácil. Que a Task 6 ponha todo alvo sintético em 1º prova que o encanamento está certo, **não** que o método funciona na fábrica; isso só a Task 8 responde.

## Processo de cada task

- **Registre o SHA base antes de despachar o implementer** (`git rev-parse HEAD`), e gere o pacote de review com `scripts/review-package BASE HEAD` da skill — nunca `HEAD~1`.
- **Artefatos no ledger com nome escopado:** `spike-foto-task-N-brief.md`, `spike-foto-task-N-review-report.md`. A Fase 2B usa a mesma numeração de task e os arquivos colidiriam.
- **Gerar brief/pacote é uma chamada; `git add` do ledger é outra**, por caminho explícito (ver `CLAUDE.md`, sobre o `sdd/.gitignore`).
- **A review de toda task que toca ranking, pontuação ou execução confere, nomeadamente:** vazamento (algo além dos pixels influencia a posição?) e a conta da posição (empate, falha). E recebe a lista de mutações da task com o pedido de **tentar burlar os testes de outro jeito**, não só repetir as listadas.

---

### Task 1: Estrutura do spike e PDF do marcador

É a primeira porque o usuário precisa do marcador impresso para começar a fotografar enquanto o resto é escrito.

**Files:**
- Create: `spikes/busca-por-foto/BuscaPorFoto.slnx`
- Create: `spikes/busca-por-foto/.gitignore`
- Create: `spikes/busca-por-foto/README.md`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/BuscaPorFoto.csproj`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Marcador/MarcadorAruco.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Marcador/PdfDoMarcador.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto.Cli/BuscaPorFoto.Cli.csproj`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/BuscaPorFoto.Tests.csproj`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/CenaSintetica.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Marcador/MarcadorArucoTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `MarcadorAruco.Dicionario` (`PredefinedDictionaryType.Dict4X4_50`), `MarcadorAruco.Id` (`0`), `MarcadorAruco.LadoMm` (`100.0`), `MarcadorAruco.CelulasPorLado` (`6`), `bool[,] MarcadorAruco.Grade()`.
  - `byte[] PdfDoMarcador.Gerar()`, `PdfDoMarcador.PontosPorMm`.
  - Teste: `CenaSintetica` é `static partial class`; `CenaSintetica.DesenharMarcador(Mat imagem, Point2d origemMm, double pixelsPorMm)`.
  - CLI: `BuscaPorFoto.Cli marcador <arquivo.pdf>`.

- [ ] **Step 1: Criar a estrutura de projetos**

`spikes/busca-por-foto/BuscaPorFoto.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/BuscaPorFoto/BuscaPorFoto.csproj" />
    <Project Path="src/BuscaPorFoto.Cli/BuscaPorFoto.Cli.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/BuscaPorFoto.Tests/BuscaPorFoto.Tests.csproj" />
  </Folder>
</Solution>
```

`spikes/busca-por-foto/src/BuscaPorFoto/BuscaPorFoto.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenCvSharp4" Version="4.13.0.20260627" />
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.13.0.20260627" />
  </ItemGroup>

</Project>
```

`spikes/busca-por-foto/src/BuscaPorFoto.Cli/BuscaPorFoto.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\BuscaPorFoto\BuscaPorFoto.csproj" />
  </ItemGroup>

</Project>
```

O projeto de teste usa as mesmas versões de pacote de teste do `tests/` do Rastru.

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/BuscaPorFoto.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\BuscaPorFoto\BuscaPorFoto.csproj" />
  </ItemGroup>

</Project>
```

`spikes/busca-por-foto/.gitignore`:

```gitignore
# Dados de cliente (STL, fotos) e a saída do spike NUNCA entram neste repositório, que é público.
# A pasta de dados fica fora do repositório; estas linhas só pegam o engano de apontá-la para cá.
dados/
saida/
*.stl
*.jpg
*.jpeg
*.png
```

`spikes/busca-por-foto/README.md`:

````markdown
# Spike — busca de peça por foto

Desenho e critério de decisão: `docs/superpowers/specs/2026-09-14-spike-busca-por-foto-design.md`.

Fica **fora** do `Rastreamento.slnx` de propósito: nada aqui entra no build nem nas suítes do
produto. Roda só no Windows (`OpenCvSharp4.runtime.win`).

**Dados de cliente não entram no git.** STL, fotos e a pasta de saída vivem numa pasta local
**fora do repositório**, passada por argumento. O `.gitignore` desta pasta só pega o engano de
apontá-las para cá.

## Testes

```bash
dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror
dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx
```

## Marcador para imprimir

```bash
dotnet run --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- marcador marcador.pdf
```

Imprima em tamanho real e **confira com régua**: o quadrado preto mede 100 mm. Se não medir, a
escala de todas as fotos com marcador fica errada.
````

- [ ] **Step 2: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/CenaSintetica.cs`:

```csharp
using BuscaPorFoto.Marcador;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <summary>Desenha cenas sintéticas em milímetros sobre uma imagem, numa escala conhecida.</summary>
public static partial class CenaSintetica
{
    /// <summary>Desenha o marcador com o canto superior esquerdo em <paramref name="origemMm"/>.</summary>
    public static void DesenharMarcador(Mat imagem, Point2d origemMm, double pixelsPorMm)
    {
        var grade = MarcadorAruco.Grade();
        var celulaPx = MarcadorAruco.LadoMm / MarcadorAruco.CelulasPorLado * pixelsPorMm;
        var x0 = origemMm.X * pixelsPorMm;
        var y0 = origemMm.Y * pixelsPorMm;
        for (var linha = 0; linha < MarcadorAruco.CelulasPorLado; linha++)
            for (var coluna = 0; coluna < MarcadorAruco.CelulasPorLado; coluna++)
            {
                var cor = grade[linha, coluna] ? Scalar.All(0) : Scalar.All(255);
                var r = new Rect(
                    (int)Math.Round(x0 + coluna * celulaPx), (int)Math.Round(y0 + linha * celulaPx),
                    (int)Math.Ceiling(celulaPx), (int)Math.Ceiling(celulaPx));
                Cv2.Rectangle(imagem, r, cor, -1);
            }
    }
}
```

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Marcador/MarcadorArucoTests.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BuscaPorFoto.Marcador;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Tests.Marcador;

public class MarcadorArucoTests
{
    [Fact]
    public void A_grade_desenhada_e_detectada_como_o_marcador_do_spike()
    {
        using var imagem = new Mat(800, 800, MatType.CV_8UC1, Scalar.All(255));
        CenaSintetica.DesenharMarcador(imagem, new Point2d(20, 20), pixelsPorMm: 4);

        using var dicionario = CvAruco.GetPredefinedDictionary(MarcadorAruco.Dicionario);
        new ArucoDetector(dicionario).DetectMarkers(imagem, out _, out var ids, out _);

        Assert.Equal(new[] { MarcadorAruco.Id }, ids);
    }

    [Fact]
    public void A_borda_da_grade_e_toda_preta()
    {
        var grade = MarcadorAruco.Grade();
        var n = MarcadorAruco.CelulasPorLado;
        for (var i = 0; i < n; i++)
        {
            Assert.True(grade[0, i]);
            Assert.True(grade[n - 1, i]);
            Assert.True(grade[i, 0]);
            Assert.True(grade[i, n - 1]);
        }
    }
}

public class PdfDoMarcadorTests
{
    private static readonly string Texto = Encoding.Latin1.GetString(PdfDoMarcador.Gerar());

    [Fact]
    public void Comeca_pelo_cabecalho_de_pdf_e_termina_em_eof()
    {
        Assert.StartsWith("%PDF-1.4\n", Texto);
        Assert.EndsWith("%%EOF\n", Texto);
    }

    [Fact]
    public void Cada_deslocamento_do_xref_aponta_para_o_seu_objeto()
    {
        // "\nxref\n", e não "xref\n": a última ocorrência desta é a do "startxref".
        var xref = Texto[Texto.LastIndexOf("\nxref\n", StringComparison.Ordinal)..];
        var deslocamentos = Regex.Matches(xref, @"^(\d{10}) 00000 n $", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(5, deslocamentos.Count);
        for (var i = 0; i < deslocamentos.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj\n", Texto[deslocamentos[i]..]);
    }

    [Fact]
    public void Desenha_um_retangulo_por_celula_preta()
    {
        var pretas = MarcadorAruco.Grade().Cast<bool>().Count(b => b);

        Assert.Equal(pretas, Regex.Matches(Texto, @" re f$", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void O_marcador_mede_exatamente_100_mm_de_lado()
    {
        var retangulos = Regex.Matches(Texto, @"^([\d.]+) ([\d.]+) ([\d.]+) ([\d.]+) re f$", RegexOptions.Multiline)
            .Select(m => Enumerable.Range(1, 4)
                .Select(g => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture)).ToArray())
            .ToList();

        var largura = retangulos.Max(r => r[0] + r[2]) - retangulos.Min(r => r[0]);
        var altura = retangulos.Max(r => r[1] + r[3]) - retangulos.Min(r => r[1]);
        var esperadoPt = MarcadorAruco.LadoMm * PdfDoMarcador.PontosPorMm;

        Assert.Equal(esperadoPt, largura, precision: 2);
        Assert.Equal(esperadoPt, altura, precision: 2);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `MarcadorAruco` e `PdfDoMarcador` não existem.

- [ ] **Step 4: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Marcador/MarcadorAruco.cs`:

```csharp
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Marcador;

/// <summary>
/// O único marcador do spike: dicionário 4x4_50, id 0, impresso com lado de 100 mm.
/// Quem gera o PDF e quem detecta na foto leem as mesmas constantes daqui.
/// </summary>
public static class MarcadorAruco
{
    public const PredefinedDictionaryType Dicionario = PredefinedDictionaryType.Dict4X4_50;
    public const int Id = 0;

    /// <summary>Lado externo do quadrado preto impresso, borda inclusa.</summary>
    public const double LadoMm = 100.0;

    /// <summary>4 células de dados + 1 célula de borda de cada lado.</summary>
    public const int CelulasPorLado = 6;

    /// <summary>
    /// Grade de células do marcador: <c>[linha, coluna]</c> verdadeiro quando a célula é preta.
    /// A linha 0 é o topo.
    /// </summary>
    public static bool[,] Grade()
    {
        using var dicionario = CvAruco.GetPredefinedDictionary(Dicionario);
        using var imagem = new Mat();
        // Um pixel por célula: a imagem gerada É a grade.
        dicionario.GenerateImageMarker(Id, CelulasPorLado, imagem, 1);

        var grade = new bool[CelulasPorLado, CelulasPorLado];
        for (var linha = 0; linha < CelulasPorLado; linha++)
            for (var coluna = 0; coluna < CelulasPorLado; coluna++)
                grade[linha, coluna] = imagem.At<byte>(linha, coluna) < 128;
        return grade;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Marcador/PdfDoMarcador.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace BuscaPorFoto.Marcador;

/// <summary>
/// PDF de uma página A4 com o marcador em vetor (um retângulo por célula preta), numa medida
/// física exata, mais uma régua de conferência. Escrito à mão: é um arquivo de ~60 linhas e não
/// justifica dependência.
/// </summary>
public static class PdfDoMarcador
{
    public const double PontosPorMm = 72.0 / 25.4;
    public const double LarguraA4Pt = 595.28;
    public const double AlturaA4Pt = 841.89;

    public static byte[] Gerar()
    {
        var conteudo = Encoding.Latin1.GetBytes(Conteudo());

        var objetos = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {LarguraA4Pt:F2} {AlturaA4Pt:F2}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            null, // stream: escrito à parte
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        using var saida = new MemoryStream();
        void Escrever(string texto) => saida.Write(Encoding.Latin1.GetBytes(texto));

        Escrever("%PDF-1.4\n");
        var deslocamentos = new long[objetos.Length];
        for (var i = 0; i < objetos.Length; i++)
        {
            deslocamentos[i] = saida.Position;
            Escrever($"{i + 1} 0 obj\n");
            if (objetos[i] is { } dicionario)
            {
                Escrever(dicionario + "\n");
            }
            else
            {
                Escrever($"<< /Length {conteudo.Length} >>\nstream\n");
                saida.Write(conteudo);
                Escrever("\nendstream\n");
            }
            Escrever("endobj\n");
        }

        var inicioDoXref = saida.Position;
        Escrever($"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var deslocamento in deslocamentos)
            Escrever($"{deslocamento:D10} 00000 n \n");
        Escrever($"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{inicioDoXref}\n%%EOF\n");

        return saida.ToArray();
    }

    private static string Conteudo()
    {
        var grade = MarcadorAruco.Grade();
        var celulaPt = MarcadorAruco.LadoMm / MarcadorAruco.CelulasPorLado * PontosPorMm;
        var ladoPt = MarcadorAruco.LadoMm * PontosPorMm;
        var x0 = (LarguraA4Pt - ladoPt) / 2;
        var yTopo = AlturaA4Pt - 160;

        var s = new StringBuilder();
        void Linha(FormattableString f) => s.Append(f.ToString(CultureInfo.InvariantCulture)).Append('\n');

        Linha($"0 g");
        for (var linha = 0; linha < MarcadorAruco.CelulasPorLado; linha++)
            for (var coluna = 0; coluna < MarcadorAruco.CelulasPorLado; coluna++)
                if (grade[linha, coluna])
                    Linha($"{x0 + coluna * celulaPt:F3} {yTopo - (linha + 1) * celulaPt:F3} {celulaPt:F3} {celulaPt:F3} re f");

        var yRegua = yTopo - ladoPt - 50;
        Linha($"0 G 1 w {x0:F3} {yRegua:F3} m {x0 + ladoPt:F3} {yRegua:F3} l S");
        Linha($"{x0:F3} {yRegua - 8:F3} m {x0:F3} {yRegua + 8:F3} l S");
        Linha($"{x0 + ladoPt:F3} {yRegua - 8:F3} m {x0 + ladoPt:F3} {yRegua + 8:F3} l S");

        var textos = new[]
        {
            "Marcador ArUco 4x4_50, id 0 - lado de 100 mm",
            "Imprima em tamanho real (100%), sem \"ajustar à página\".",
            "Confira com régua: o quadrado preto e a linha abaixo dele medem 100 mm cada.",
            "Se não medirem, a escala do spike fica errada: reimprima antes de fotografar.",
        };
        // Latin-1 coincide com a WinAnsiEncoding da fonte só até U+00FF; travessão e aspas
        // tipográficas viram '?' em silêncio no Encoding.Latin1.
        if (textos.SelectMany(t => t).FirstOrDefault(c => c > 'ÿ') is var fora and not '\0')
            throw new InvalidOperationException($"Caractere fora do Latin-1 no texto do PDF: U+{(int)fora:X4}");
        var yTexto = yRegua - 40;
        foreach (var texto in textos)
        {
            Linha($"BT /F1 11 Tf {x0 - 60:F3} {yTexto:F3} Td ({Escapar(texto)}) Tj ET");
            yTexto -= 18;
        }

        return s.ToString();
    }

    private static string Escapar(string texto) =>
        texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
```

`spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs`:

```csharp
using BuscaPorFoto.Marcador;

const string Uso = """
    Uso:
      BuscaPorFoto.Cli marcador <arquivo.pdf>
    """;

switch (args)
{
    case ["marcador", var pdf]:
        File.WriteAllBytes(pdf, PdfDoMarcador.Gerar());
        Console.WriteLine($"Marcador gravado em {Path.GetFullPath(pdf)}");
        return 0;
}

Console.Error.WriteLine(Uso);
return 2;
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror`
Expected: `0 Aviso(s)`, `0 Erro(s)`.

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 6`, 0 falhas.

- [ ] **Step 6: Gerar o PDF e validar fora do .NET**

```bash
dotnet run --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- marcador "$TEMP/marcador.pdf"
pdftotext -enc UTF-8 "$TEMP/marcador.pdf" -
```

Expected: o `pdftotext` (vem com o Git for Windows) lê o arquivo sem erro e imprime as quatro linhas de instrução com a acentuação correta ("à página", "régua", "não"). É a prova de que o PDF escrito à mão é válido para um leitor que não é o nosso teste.

- [ ] **Step 7: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): estrutura do spike de busca por foto e PDF do marcador ArUco

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

- [ ] **Step 8: Entregar o PDF ao usuário**

Envie o `marcador.pdf` ao usuário (SendUserFile) com a instrução de imprimir em 100% e medir com régua. O PDF é gerado, não versionado.

---

### Task 2: STL → silhueta

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Stl/LeitorDeStl.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Mascara.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Silhueta/ProjetorDeSilhueta.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/MalhaSintetica.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Silhueta/StlESilhuetaTests.cs`

**Interfaces:**
- Consumes: nada da Task 1.
- Produces:
  - `readonly record struct Triangulo(Vector3 A, Vector3 B, Vector3 C)`; `IReadOnlyList<Triangulo> LeitorDeStl.Ler(byte[] conteudo)` (lança `FormatException`).
  - `sealed class Mascara : IDisposable` — `Mat Pixels` (CV_8UC1, 0/255, recortada no objeto), `double? MmPorPixel`, construtor `Mascara(Mat pixels, double? mmPorPixel)`, `static Mascara? DeMascaraCheia(Mat cheia, double? mmPorPixel)`, `Mascara ReduzidaAte(int ladoMaximo)`.
  - `sealed record ResultadoDaProjecao(bool Plana, IReadOnlyList<Mascara> Vistas)`; `ResultadoDaProjecao ProjetorDeSilhueta.Projetar(IReadOnlyList<Triangulo> malha)`; `ProjetorDeSilhueta.MmPorPixel` (`0.5`), `ProjetorDeSilhueta.LimiarDeChapa` (`0.2`).
  - Teste: `MalhaSintetica.Caixa`, `ChapaComFuro`, `Transformar`, `StlBinario`, `StlAscii`.

**Divergência declarada da spec:** a spec (§4.2) diz 6 silhuetas para peça sem face dominante; esta task gera **3**. A vista do lado oposto de um eixo é o espelho da vista da frente, e os pontuadores (Task 5) já testam espelho — as outras 3 seriam duplicatas exatas. Está escrito no XML doc de `Projetar`.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/MalhaSintetica.cs`:

```csharp
using System.Globalization;
using System.Numerics;
using System.Text;
using BuscaPorFoto.Stl;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <summary>Malhas geradas em código, em milímetros, e a serialização delas em STL.</summary>
public static class MalhaSintetica
{
    public static List<Triangulo> Caixa(float x, float y, float z)
    {
        var p = new Vector3[]
        {
            new(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0),
            new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z),
        };
        int[][] faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        return faces.SelectMany(f => Quad(p[f[0]], p[f[1]], p[f[2]], p[f[3]])).ToList();
    }

    /// <summary>Chapa <paramref name="largura"/> × <paramref name="altura"/> × <paramref name="espessura"/> com um furo quadrado passante no centro.</summary>
    public static List<Triangulo> ChapaComFuro(float largura, float altura, float espessura, float ladoDoFuro)
    {
        var tris = new List<Triangulo>();
        float fx0 = (largura - ladoDoFuro) / 2, fx1 = fx0 + ladoDoFuro;
        float fy0 = (altura - ladoDoFuro) / 2, fy1 = fy0 + ladoDoFuro;

        foreach (var z in new[] { 0f, espessura })
        {
            Vector3 P(float x, float y) => new(x, y, z);
            // Quatro trapézios em volta do furo.
            tris.AddRange(Quad(P(0, 0), P(largura, 0), P(fx1, fy0), P(fx0, fy0)));
            tris.AddRange(Quad(P(largura, 0), P(largura, altura), P(fx1, fy1), P(fx1, fy0)));
            tris.AddRange(Quad(P(largura, altura), P(0, altura), P(fx0, fy1), P(fx1, fy1)));
            tris.AddRange(Quad(P(0, altura), P(0, 0), P(fx0, fy0), P(fx0, fy1)));
        }

        void Parede(float x0, float y0, float x1, float y1) =>
            tris.AddRange(Quad(new(x0, y0, 0), new(x1, y1, 0), new(x1, y1, espessura), new(x0, y0, espessura)));
        Parede(0, 0, largura, 0); Parede(largura, 0, largura, altura); Parede(largura, altura, 0, altura); Parede(0, altura, 0, 0);
        Parede(fx0, fy0, fx1, fy0); Parede(fx1, fy0, fx1, fy1); Parede(fx1, fy1, fx0, fy1); Parede(fx0, fy1, fx0, fy0);
        return tris;
    }

    public static List<Triangulo> Transformar(IEnumerable<Triangulo> malha, Matrix4x4 m) =>
        malha.Select(t => new Triangulo(Vector3.Transform(t.A, m), Vector3.Transform(t.B, m), Vector3.Transform(t.C, m))).ToList();

    public static byte[] StlBinario(IReadOnlyList<Triangulo> malha, string cabecalho = "solid exportado por um CAD")
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var cab = new byte[80];
        Encoding.ASCII.GetBytes(cabecalho).CopyTo(cab, 0);
        w.Write(cab);
        w.Write((uint)malha.Count);
        foreach (var t in malha)
        {
            for (var i = 0; i < 3; i++) w.Write(0f);
            foreach (var v in new[] { t.A, t.B, t.C }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            w.Write((ushort)0);
        }
        w.Flush();
        return ms.ToArray();
    }

    public static byte[] StlAscii(IReadOnlyList<Triangulo> malha)
    {
        var s = new StringBuilder("solid sintetico\n");
        foreach (var t in malha)
        {
            s.Append("  facet normal 0 0 0\n    outer loop\n");
            foreach (var v in new[] { t.A, t.B, t.C })
                s.Append(CultureInfo.InvariantCulture, $"      vertex {v.X} {v.Y} {v.Z}\n");
            s.Append("    endloop\n  endfacet\n");
        }
        s.Append("endsolid sintetico\n");
        return Encoding.ASCII.GetBytes(s.ToString());
    }

    private static IEnumerable<Triangulo> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        yield return new Triangulo(a, b, c);
        yield return new Triangulo(a, c, d);
    }
}
```

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Silhueta/StlESilhuetaTests.cs`:

```csharp
using System.Numerics;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Stl;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Silhueta;

public class LeitorDeStlTests
{
    [Fact]
    public void Le_binario_mesmo_com_cabecalho_comecando_por_solid()
    {
        var malha = MalhaSintetica.Caixa(10, 20, 30);

        var lida = LeitorDeStl.Ler(MalhaSintetica.StlBinario(malha, cabecalho: "solid peca"));

        Assert.Equal(malha, lida);
    }

    [Fact]
    public void Le_ascii()
    {
        var malha = MalhaSintetica.Caixa(10, 20, 30);

        var lida = LeitorDeStl.Ler(MalhaSintetica.StlAscii(malha));

        Assert.Equal(malha, lida);
    }

    [Fact]
    public void Recusa_arquivo_que_nao_e_stl()
    {
        Assert.Throws<FormatException>(() => LeitorDeStl.Ler("isto não é um STL"u8.ToArray()));
    }
}

public class ProjetorDeSilhuetaTests
{
    private static (double MaiorMm, double MenorMm, double AreaMm2) Medir(Mascara m)
    {
        var mm = m.MmPorPixel!.Value;
        return (Math.Max(m.Pixels.Width, m.Pixels.Height) * mm,
                Math.Min(m.Pixels.Width, m.Pixels.Height) * mm,
                Cv2.CountNonZero(m.Pixels) * mm * mm);
    }

    [Fact]
    public void Chapa_gera_uma_vista_em_mm_reais_com_o_furo_vazado()
    {
        var resultado = ProjetorDeSilhueta.Projetar(MalhaSintetica.ChapaComFuro(100, 50, 3, 20));

        Assert.True(resultado.Plana);
        var vista = Assert.Single(resultado.Vistas);
        var (maior, menor, area) = Medir(vista);
        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
        Assert.Equal(0, vista.Pixels.At<byte>(vista.Pixels.Height / 2, vista.Pixels.Width / 2));
    }

    [Fact]
    public void Chapa_em_orientacao_qualquer_da_a_mesma_vista()
    {
        var girada = MalhaSintetica.Transformar(
            MalhaSintetica.ChapaComFuro(100, 50, 3, 20),
            Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f) * Matrix4x4.CreateTranslation(500, -200, 80));

        var resultado = ProjetorDeSilhueta.Projetar(girada);

        Assert.True(resultado.Plana);
        var (maior, menor, area) = Medir(Assert.Single(resultado.Vistas));
        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
    }

    [Fact]
    public void Peca_sem_face_dominante_gera_tres_vistas()
    {
        var resultado = ProjetorDeSilhueta.Projetar(MalhaSintetica.Caixa(100, 80, 60));

        Assert.False(resultado.Plana);
        Assert.Equal(3, resultado.Vistas.Count);
        var areas = resultado.Vistas.Select(v => Medir(v).AreaMm2).Order().ToArray();
        Assert.InRange(areas[0], 4800 * 0.97, 4800 * 1.04);
        Assert.InRange(areas[1], 6000 * 0.97, 6000 * 1.04);
        Assert.InRange(areas[2], 8000 * 0.97, 8000 * 1.04);
    }

    [Fact]
    public void Malha_vazia_e_recusada()
    {
        Assert.Throws<ArgumentException>(() => ProjetorDeSilhueta.Projetar(new List<Triangulo>()));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `Triangulo`, `LeitorDeStl`, `Mascara`, `ProjetorDeSilhueta` não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Stl/LeitorDeStl.cs`:

```csharp
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace BuscaPorFoto.Stl;

public readonly record struct Triangulo(Vector3 A, Vector3 B, Vector3 C);

public static class LeitorDeStl
{
    private const int TamanhoDoCabecalho = 80;
    private const int TamanhoDoTriangulo = 50;

    /// <summary>
    /// Lê STL binário ou ASCII. Decide pelo tamanho, não pelo prefixo "solid": exportadores de CAD
    /// escrevem "solid" no cabeçalho do binário também.
    /// </summary>
    public static IReadOnlyList<Triangulo> Ler(byte[] conteudo)
    {
        if (conteudo.Length >= TamanhoDoCabecalho + 4)
        {
            var quantidade = BinaryPrimitives.ReadUInt32LittleEndian(conteudo.AsSpan(TamanhoDoCabecalho));
            if (TamanhoDoCabecalho + 4L + TamanhoDoTriangulo * (long)quantidade == conteudo.Length)
                return LerBinario(conteudo, (int)quantidade);
        }
        return LerAscii(Encoding.ASCII.GetString(conteudo));
    }

    private static List<Triangulo> LerBinario(byte[] conteudo, int quantidade)
    {
        var triangulos = new List<Triangulo>(quantidade);
        for (var i = 0; i < quantidade; i++)
        {
            // 12 bytes de normal, que o spike não usa, depois os três vértices.
            var inicio = TamanhoDoCabecalho + 4 + i * TamanhoDoTriangulo + 12;
            triangulos.Add(new Triangulo(
                LerVetor(conteudo, inicio), LerVetor(conteudo, inicio + 12), LerVetor(conteudo, inicio + 24)));
        }
        return triangulos;
    }

    private static Vector3 LerVetor(byte[] b, int inicio) => new(
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio)),
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio + 4)),
        BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(inicio + 8)));

    private static List<Triangulo> LerAscii(string texto)
    {
        var vertices = new List<Vector3>();
        foreach (var linhaCrua in texto.Split('\n'))
        {
            var partes = linhaCrua.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length == 4 && partes[0] == "vertex")
                vertices.Add(new Vector3(
                    float.Parse(partes[1], CultureInfo.InvariantCulture),
                    float.Parse(partes[2], CultureInfo.InvariantCulture),
                    float.Parse(partes[3], CultureInfo.InvariantCulture)));
        }

        if (vertices.Count == 0 || vertices.Count % 3 != 0)
            throw new FormatException(
                $"STL inválido: não é binário de tamanho consistente e o ASCII tem {vertices.Count} vértices.");

        var triangulos = new List<Triangulo>(vertices.Count / 3);
        for (var i = 0; i < vertices.Count; i += 3)
            triangulos.Add(new Triangulo(vertices[i], vertices[i + 1], vertices[i + 2]));
        return triangulos;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Mascara.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto;

/// <summary>
/// Uma forma binária recortada justa no objeto: <see cref="Pixels"/> é CV_8UC1 com 255 no objeto
/// e 0 fora (furos inclusos). <see cref="MmPorPixel"/> é nulo quando a escala é desconhecida —
/// foto sem marcador.
///
/// É a ÚNICA coisa que os pontuadores recebem. Nome de arquivo, código da peça e ordem de
/// enumeração não passam por aqui, e é isso que impede vazamento para o ranking.
/// </summary>
public sealed class Mascara : IDisposable
{
    public Mat Pixels { get; }
    public double? MmPorPixel { get; }

    public Mascara(Mat pixels, double? mmPorPixel)
    {
        if (pixels.Type() != MatType.CV_8UC1)
            throw new ArgumentException("A máscara tem de ser CV_8UC1.", nameof(pixels));
        if (Cv2.CountNonZero(pixels) == 0)
            throw new ArgumentException("A máscara está vazia.", nameof(pixels));
        Pixels = pixels;
        MmPorPixel = mmPorPixel;
    }

    /// <summary>Recorta uma máscara do tamanho da imagem até a caixa do objeto; nulo se vazia.</summary>
    public static Mascara? DeMascaraCheia(Mat cheia, double? mmPorPixel)
    {
        if (Cv2.CountNonZero(cheia) == 0) return null;
        var caixa = Cv2.BoundingRect(cheia);
        return new Mascara(new Mat(cheia, caixa).Clone(), mmPorPixel);
    }

    /// <summary>Reduz (nunca amplia) até o maior lado caber em <paramref name="ladoMaximo"/>, mantendo a escala coerente.</summary>
    public Mascara ReduzidaAte(int ladoMaximo)
    {
        var maior = Math.Max(Pixels.Width, Pixels.Height);
        if (maior <= ladoMaximo) return new Mascara(Pixels.Clone(), MmPorPixel);

        var fator = (double)ladoMaximo / maior;
        var reduzida = new Mat();
        Cv2.Resize(Pixels, reduzida, new Size(), fator, fator, InterpolationFlags.Area);
        Cv2.Threshold(reduzida, reduzida, 127, 255, ThresholdTypes.Binary);
        return new Mascara(reduzida, MmPorPixel / fator);
    }

    public void Dispose() => Pixels.Dispose();
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Silhueta/ProjetorDeSilhueta.cs`:

```csharp
using System.Numerics;
using BuscaPorFoto.Stl;
using OpenCvSharp;

namespace BuscaPorFoto.Silhueta;

public sealed record ResultadoDaProjecao(bool Plana, IReadOnlyList<Mascara> Vistas);

public static class ProjetorDeSilhueta
{
    public const double MmPorPixel = 0.5;

    /// <summary>
    /// Chapa: a extensão ao longo do eixo de menor dispersão é no máximo 20% da extensão ao longo
    /// do segundo eixo.
    /// </summary>
    public const double LimiarDeChapa = 0.2;

    /// <summary>
    /// Chapa gera 1 vista (ao longo da espessura); peça sem face dominante gera 3, uma por eixo
    /// principal. A spec fala em 6 (os três eixos nos dois sentidos), mas a vista do lado oposto é
    /// o espelho da vista da frente, e os pontuadores já testam espelho — as outras 3 seriam
    /// duplicatas exatas.
    /// </summary>
    public static ResultadoDaProjecao Projetar(IReadOnlyList<Triangulo> malha)
    {
        if (malha.Count == 0)
            throw new ArgumentException("Malha vazia.", nameof(malha));

        var (centro, eixos) = EixosPrincipais(malha);
        var extensoes = eixos.Select(e => Extensao(malha, centro, e)).ToArray();
        var plana = extensoes[2] <= LimiarDeChapa * extensoes[1];

        var vistas = plana
            ? new[] { Vista(malha, centro, eixos[0], eixos[1]) }
            : new[]
            {
                Vista(malha, centro, eixos[0], eixos[1]),
                Vista(malha, centro, eixos[0], eixos[2]),
                Vista(malha, centro, eixos[1], eixos[2]),
            };
        return new ResultadoDaProjecao(plana, vistas);
    }

    /// <summary>
    /// Centro e eixos principais da SUPERFÍCIE, ordenados por dispersão decrescente. Usa o segundo
    /// momento exato de cada triângulo, ∫ p pᵀ dA = (A/12)·(Σ vᵢvᵢᵀ + (Σ vᵢ)(Σ vᵢ)ᵀ), e não os
    /// vértices ponderados: estes dependem da triangulação — numa caixa com a diagonal de cada face
    /// escolhida de um lado só, os eixos saíam girados 16°.
    /// </summary>
    private static (Vector3 Centro, Vector3[] Eixos) EixosPrincipais(IReadOnlyList<Triangulo> malha)
    {
        double areaTotal = 0, cx = 0, cy = 0, cz = 0;
        foreach (var t in malha)
        {
            var area = Area(t);
            areaTotal += area;
            cx += area * (t.A.X + t.B.X + t.C.X) / 3;
            cy += area * (t.A.Y + t.B.Y + t.C.Y) / 3;
            cz += area * (t.A.Z + t.B.Z + t.C.Z) / 3;
        }
        if (areaTotal <= 0)
            throw new ArgumentException("Malha sem área.", nameof(malha));
        cx /= areaTotal; cy /= areaTotal; cz /= areaTotal;

        var cov = new double[3, 3];
        foreach (var t in malha)
        {
            var area = Area(t);
            var d = new[] { t.A, t.B, t.C }
                .Select(v => new[] { v.X - cx, v.Y - cy, v.Z - cz })
                .ToArray();
            var soma = new[] { d[0][0] + d[1][0] + d[2][0], d[0][1] + d[1][1] + d[2][1], d[0][2] + d[1][2] + d[2][2] };
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    cov[i, j] += area / 12 * (d[0][i] * d[0][j] + d[1][i] * d[1][j] + d[2][i] * d[2][j] + soma[i] * soma[j]);
        }

        var (valores, vetores) = Jacobi(cov);
        var ordem = Enumerable.Range(0, 3).OrderByDescending(i => valores[i]).ToArray();
        var eixos = ordem
            .Select(i => Vector3.Normalize(new Vector3((float)vetores[0, i], (float)vetores[1, i], (float)vetores[2, i])))
            .ToArray();
        return (new Vector3((float)cx, (float)cy, (float)cz), eixos);
    }

    private static double Area(Triangulo t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;

    private static double Extensao(IReadOnlyList<Triangulo> malha, Vector3 centro, Vector3 eixo)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var t in malha)
            foreach (var v in new[] { t.A, t.B, t.C })
            {
                double p = Vector3.Dot(v - centro, eixo);
                min = Math.Min(min, p);
                max = Math.Max(max, p);
            }
        return max - min;
    }

    private static Mascara Vista(IReadOnlyList<Triangulo> malha, Vector3 centro, Vector3 eixoU, Vector3 eixoV)
    {
        var projetados = malha
            .Select(t => new[] { t.A, t.B, t.C }
                .Select(v => (U: (double)Vector3.Dot(v - centro, eixoU), V: (double)Vector3.Dot(v - centro, eixoV)))
                .ToArray())
            .ToList();

        var minU = projetados.SelectMany(p => p).Min(p => p.U);
        var maxU = projetados.SelectMany(p => p).Max(p => p.U);
        var minV = projetados.SelectMany(p => p).Min(p => p.V);
        var maxV = projetados.SelectMany(p => p).Max(p => p.V);

        const int margem = 2;
        var largura = (int)Math.Ceiling((maxU - minU) / MmPorPixel) + 2 * margem;
        var altura = (int)Math.Ceiling((maxV - minV) / MmPorPixel) + 2 * margem;
        var cheia = new Mat(altura, largura, MatType.CV_8UC1, Scalar.All(0));

        // Um FillConvexPoly por triângulo, e não um FillPoly com todos: o FillPoly preenche por
        // paridade, e as faces de cima e de baixo da chapa se cancelariam.
        foreach (var tri in projetados)
        {
            var pontos = tri
                .Select(p => new Point(
                    (int)Math.Round((p.U - minU) / MmPorPixel) + margem,
                    (int)Math.Round((p.V - minV) / MmPorPixel) + margem))
                .ToArray();
            Cv2.FillConvexPoly(cheia, pontos, Scalar.All(255));
        }

        using (cheia)
            return Mascara.DeMascaraCheia(cheia, MmPorPixel)
                ?? throw new InvalidOperationException("A projeção não gerou pixel nenhum.");
    }

    /// <summary>Autovalores e autovetores (em colunas) de uma matriz 3×3 simétrica, por rotações de Jacobi.</summary>
    private static (double[] Valores, double[,] Vetores) Jacobi(double[,] matriz)
    {
        var a = (double[,])matriz.Clone();
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        for (var varredura = 0; varredura < 50; varredura++)
        {
            var foraDaDiagonal = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (foraDaDiagonal < 1e-12 * (Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]) + 1e-300))
                break;

            for (var p = 0; p < 2; p++)
                for (var q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;

                    for (var k = 0; k < 3; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (var k = 0; k < 3; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }

        return (new[] { a[0, 0], a[1, 1], a[2, 2] }, v);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 13`, 0 falhas.

- [ ] **Step 5: Mutação medida**

Troque, em `EixosPrincipais`, o segundo momento exato por vértices ponderados pela área (cada vértice de cada triângulo somado com peso `Area(t)`). Expected: `Peca_sem_face_dominante_gera_tres_vistas` e os dois testes de chapa falham — foi o defeito real que a medição deste plano achou. Restaure.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): leitor de STL e projeção da silhueta em mm reais

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Retificação da foto pelo marcador

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Imagens.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Retificador.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/CenaSintetica.Chapa.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RetificadorTests.cs`

**Interfaces:**
- Consumes: `MarcadorAruco` (Task 1), `CenaSintetica.DesenharMarcador` (Task 1).
- Produces:
  - `internal static Mat Imagens.ParaCinza(Mat imagem)`.
  - `sealed record FotoRetificada(Mat Imagem, Point2f[] CantosDoMarcador) : IDisposable` com `double MmPorPixel`.
  - `FotoRetificada? Retificador.Retificar(Mat fotoBgr)`; `Retificador.PixelsPorMm` (`2.0`), `Retificador.AlcanceMaximoMm` (`1500`).
  - Teste: `CenaSintetica.DesenharChapa(Mat, Point2d centroMm, double larguraMm, double alturaMm, double ladoDoFuroMm, double anguloGraus, double pixelsPorMm, Scalar corDaPeca, Scalar corDoFundo)` e `Mat CenaSintetica.EmPerspectiva(Mat cena, Scalar corDoFundo)`.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/CenaSintetica.Chapa.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

public static partial class CenaSintetica
{
    /// <summary>
    /// Desenha uma chapa retangular com furo quadrado central, girada <paramref name="anguloGraus"/>
    /// em torno de <paramref name="centroMm"/>. O furo recebe <paramref name="corDoFundo"/>.
    /// </summary>
    public static void DesenharChapa(Mat imagem, Point2d centroMm, double larguraMm, double alturaMm,
        double ladoDoFuroMm, double anguloGraus, double pixelsPorMm, Scalar corDaPeca, Scalar corDoFundo)
    {
        Point[] Retangulo(double l, double a)
        {
            var rad = anguloGraus * Math.PI / 180;
            var (c, s) = (Math.Cos(rad), Math.Sin(rad));
            return new[] { (-l / 2, -a / 2), (l / 2, -a / 2), (l / 2, a / 2), (-l / 2, a / 2) }
                .Select(p => new Point(
                    (int)Math.Round((centroMm.X + p.Item1 * c - p.Item2 * s) * pixelsPorMm),
                    (int)Math.Round((centroMm.Y + p.Item1 * s + p.Item2 * c) * pixelsPorMm)))
                .ToArray();
        }

        Cv2.FillConvexPoly(imagem, Retangulo(larguraMm, alturaMm), corDaPeca);
        if (ladoDoFuroMm > 0)
            Cv2.FillConvexPoly(imagem, Retangulo(ladoDoFuroMm, ladoDoFuroMm), corDoFundo);
    }

    /// <summary>
    /// Simula a foto tirada de um ângulo: leva os cantos da imagem para dentro, em proporções
    /// diferentes em cada canto, e preenche a borda com <paramref name="corDoFundo"/>.
    /// </summary>
    public static Mat EmPerspectiva(Mat cena, Scalar corDoFundo)
    {
        float w = cena.Width, h = cena.Height;
        var origem = new[] { new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h) };
        var destino = new[]
        {
            new Point2f(0.06f * w, 0.03f * h), new Point2f(0.97f * w, 0.08f * h),
            new Point2f(0.92f * w, 0.98f * h), new Point2f(0.02f * w, 0.93f * h),
        };
        using var h0 = Cv2.GetPerspectiveTransform(origem, destino);
        var saida = new Mat();
        Cv2.WarpPerspective(cena, saida, h0, cena.Size(), InterpolationFlags.Linear, BorderTypes.Constant, corDoFundo);
        return saida;
    }
}
```

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RetificadorTests.cs`:

```csharp
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Foto;

public class RetificadorTests
{
    private static readonly Scalar Fundo = Scalar.All(200);
    private const double PxPorMmDaCena = 4;

    private static Mat Cena(bool comMarcador)
    {
        var cena = new Mat((int)(300 * PxPorMmDaCena), (int)(400 * PxPorMmDaCena), MatType.CV_8UC3, Fundo);
        if (comMarcador)
            CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMmDaCena);
        CenaSintetica.DesenharChapa(cena, new Point2d(260, 180), 160, 80, 0, anguloGraus: 0,
            PxPorMmDaCena, Scalar.All(40), Fundo);
        using (cena)
            return CenaSintetica.EmPerspectiva(cena, Fundo);
    }

    [Fact]
    public void Devolve_a_peca_em_milimetros_reais_apesar_da_perspectiva()
    {
        using var foto = Cena(comMarcador: true);

        using var retificada = Retificador.Retificar(foto);

        Assert.NotNull(retificada);
        using var cinza = new Mat();
        Cv2.CvtColor(retificada.Imagem, cinza, ColorConversionCodes.BGR2GRAY);
        using var escura = new Mat();
        Cv2.Threshold(cinza, escura, 100, 255, ThresholdTypes.BinaryInv);
        Cv2.FindContours(escura, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // A chapa é o maior contorno escuro que não toca a caixa do marcador.
        var caixaDoMarcador = Cv2.BoundingRect(retificada.CantosDoMarcador.Select(p => new Point(p.X, p.Y)));
        var chapa = contornos
            .Where(c => (Cv2.BoundingRect(c) & caixaDoMarcador).Width == 0)
            .MaxBy(c => Cv2.ContourArea(c))!;
        var retangulo = Cv2.MinAreaRect(chapa);
        var maiorMm = Math.Max(retangulo.Size.Width, retangulo.Size.Height) * retificada.MmPorPixel;
        var menorMm = Math.Min(retangulo.Size.Width, retangulo.Size.Height) * retificada.MmPorPixel;

        Assert.InRange(maiorMm, 160 * 0.97, 160 * 1.03);
        Assert.InRange(menorMm, 80 * 0.97, 80 * 1.03);
    }

    [Fact]
    public void Os_cantos_devolvidos_sao_os_do_marcador_na_saida()
    {
        using var foto = Cena(comMarcador: true);

        using var retificada = Retificador.Retificar(foto)!;

        var c = retificada.CantosDoMarcador;
        var ladoPx = 100 * Retificador.PixelsPorMm;
        Assert.Equal(ladoPx, c[1].X - c[0].X, precision: 1);
        Assert.Equal(ladoPx, c[3].Y - c[0].Y, precision: 1);
        // O centro do marcador numa saída retificada é uma célula da grade: preto ou branco puro,
        // nunca o cinza do fundo.
        var centro = retificada.Imagem.At<Vec3b>((int)(c[0].Y + ladoPx / 2), (int)(c[0].X + ladoPx / 2));
        Assert.NotEqual(200, centro.Item0);
    }

    [Fact]
    public void Sem_marcador_devolve_nulo()
    {
        using var foto = Cena(comMarcador: false);

        Assert.Null(Retificador.Retificar(foto));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `Retificador` e `FotoRetificada` não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Foto/Imagens.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Foto;

internal static class Imagens
{
    /// <summary>Cópia em tons de cinza; aceita imagem de 1 ou 3 canais.</summary>
    public static Mat ParaCinza(Mat imagem)
    {
        if (imagem.Channels() == 1) return imagem.Clone();
        var cinza = new Mat();
        Cv2.CvtColor(imagem, cinza, ColorConversionCodes.BGR2GRAY);
        return cinza;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Foto/Retificador.cs`:

```csharp
using BuscaPorFoto.Marcador;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace BuscaPorFoto.Foto;

/// <param name="Imagem">Foto em vista de cima, na escala <see cref="Retificador.PixelsPorMm"/>.</param>
/// <param name="CantosDoMarcador">Os 4 cantos do marcador nas coordenadas de <paramref name="Imagem"/>.</param>
public sealed record FotoRetificada(Mat Imagem, Point2f[] CantosDoMarcador) : IDisposable
{
    public double MmPorPixel => 1.0 / Retificador.PixelsPorMm;
    public void Dispose() => Imagem.Dispose();
}

public static class Retificador
{
    public const double PixelsPorMm = 2.0;

    /// <summary>
    /// Maior distância, em mm, que a saída alcança a partir do marcador. Sem esse teto, um canto
    /// da foto perto da linha do horizonte do plano projeta para o infinito.
    /// </summary>
    public const double AlcanceMaximoMm = 1500;

    /// <summary>Corrige a perspectiva pelo marcador; nulo quando o marcador não é detectado.</summary>
    public static FotoRetificada? Retificar(Mat fotoBgr)
    {
        using var cinza = Imagens.ParaCinza(fotoBgr);

        using var dicionario = CvAruco.GetPredefinedDictionary(MarcadorAruco.Dicionario);
        new ArucoDetector(dicionario).DetectMarkers(cinza, out var cantos, out var ids, out _);
        var indice = Array.IndexOf(ids, MarcadorAruco.Id);
        if (indice < 0) return null;

        var ladoPx = (float)(MarcadorAruco.LadoMm * PixelsPorMm);
        var marcadorNaOrigem = new[] { new Point2f(0, 0), new Point2f(ladoPx, 0), new Point2f(ladoPx, ladoPx), new Point2f(0, ladoPx) };
        using var paraOrigem = Cv2.GetPerspectiveTransform(cantos[indice], marcadorNaOrigem);

        var cantosDaFoto = new[]
        {
            new Point2f(0, 0), new Point2f(fotoBgr.Width, 0),
            new Point2f(fotoBgr.Width, fotoBgr.Height), new Point2f(0, fotoBgr.Height),
        };
        var projetados = Cv2.PerspectiveTransform(cantosDaFoto, paraOrigem);

        var alcance = (float)(AlcanceMaximoMm * PixelsPorMm);
        float Limitar(float v) => float.IsFinite(v) ? Math.Clamp(v, -alcance, alcance + ladoPx) : 0;
        var minX = projetados.Min(p => Limitar(p.X));
        var maxX = projetados.Max(p => Limitar(p.X));
        var minY = projetados.Min(p => Limitar(p.Y));
        var maxY = projetados.Max(p => Limitar(p.Y));

        // Translada para a caixa começar em (0,0).
        using var translacao = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        translacao.Set(0, 2, (double)-minX);
        translacao.Set(1, 2, (double)-minY);
        using var homografia = (translacao * paraOrigem).ToMat();

        var tamanho = new Size((int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
        var retificada = new Mat();
        Cv2.WarpPerspective(fotoBgr, retificada, homografia, tamanho, InterpolationFlags.Linear,
            BorderTypes.Replicate);

        var cantosNaSaida = marcadorNaOrigem.Select(p => new Point2f(p.X - minX, p.Y - minY)).ToArray();
        return new FotoRetificada(retificada, cantosNaSaida);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 16`, 0 falhas.

- [ ] **Step 5: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): retificação da foto pela homografia do marcador

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Recorte da peça e imagem de sobreposição

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Recortador.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Sobreposicao.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/Metricas.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RecortadorTests.cs`

**Interfaces:**
- Consumes: `Imagens.ParaCinza` (Task 3), `Mascara.DeMascaraCheia` (Task 2), `CenaSintetica.DesenharMarcador` (Task 1), `CenaSintetica.DesenharChapa` (Task 3).
- Produces:
  - `enum MetodoDeRecorte { Otsu, GrabCut }`.
  - `Mat Recortador.Recortar(Mat imagemBgr, Point2f[]? cantosDoMarcador, MetodoDeRecorte metodo)` — máscara do tamanho da imagem, maior objeto com furos.
  - `Mat Sobreposicao.Gerar(Mat imagemBgr, Mat mascaraCheia)`.
  - Teste: `double Metricas.IoU(Mat a, Mat b)`.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/Metricas.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

public static class Metricas
{
    /// <summary>Interseção sobre união de duas máscaras de mesmo tamanho (pixel &gt; 0 é objeto).</summary>
    public static double IoU(Mat a, Mat b)
    {
        using var e = new Mat();
        using var ou = new Mat();
        Cv2.BitwiseAnd(a, b, e);
        Cv2.BitwiseOr(a, b, ou);
        return (double)Cv2.CountNonZero(e) / Cv2.CountNonZero(ou);
    }
}
```

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RecortadorTests.cs`:

```csharp
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Foto;

public class RecortadorTests
{
    private const double PxPorMm = 2;

    /// <summary>Cena com uma chapa com furo e a máscara-verdade dela.</summary>
    private static (Mat Cena, Mat Verdade) Cena(Scalar peca, Scalar fundo, bool comRuido = false, bool comMarcador = false)
    {
        var cena = new Mat((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, fundo);
        var verdade = new Mat(cena.Size(), MatType.CV_8UC1, Scalar.All(0));
        if (comMarcador)
            CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(250, 170), 180, 90, 40, 25, PxPorMm, peca, fundo);
        CenaSintetica.DesenharChapa(verdade, new Point2d(250, 170), 180, 90, 40, 25, PxPorMm, Scalar.All(255), Scalar.All(0));
        if (comRuido)
        {
            // Ruído gerado já com sinal: num CV_8U a metade negativa seria cortada em zero.
            using var ruido = new Mat(cena.Size(), MatType.CV_16SC3);
            Cv2.Randn(ruido, Scalar.All(0), Scalar.All(12));
            using var cena16 = new Mat();
            cena.ConvertTo(cena16, MatType.CV_16SC3);
            Cv2.Add(cena16, ruido, cena16);
            cena16.ConvertTo(cena, MatType.CV_8UC3);
            // Respingos pequenos, que não podem virar "a peça".
            Cv2.Circle(cena, new Point(60, 500), 6, peca, -1);
            Cv2.Circle(cena, new Point(700, 60), 5, peca, -1);
        }
        return (cena, verdade);
    }

    [Fact]
    public void Otsu_recorta_peca_escura_em_fundo_claro_com_o_furo()
    {
        var (cena, verdade) = Cena(Scalar.All(50), Scalar.All(210), comRuido: true);
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, null, MetodoDeRecorte.Otsu);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
            Assert.Equal(0, mascara.At<byte>((int)(170 * PxPorMm), (int)(250 * PxPorMm)));
        }
    }

    [Fact]
    public void Otsu_recorta_peca_clara_em_fundo_escuro()
    {
        var (cena, verdade) = Cena(Scalar.All(220), Scalar.All(40));
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, null, MetodoDeRecorte.Otsu);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
        }
    }

    /// <summary>
    /// Chapa de 60 × 30 mm, MENOR que o marcador de 100 mm. Numa cena com chapa maior que o
    /// marcador, o maior objeto já seria a chapa sem apagar nada, e o teste do apagamento passaria
    /// com o apagamento removido — foi o que a mutação mostrou ao validar este plano.
    /// </summary>
    private static Mat CenaComMarcadorMaiorQueAPeca()
    {
        var cena = new Mat((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, Scalar.All(210));
        CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(300, 200), 60, 30, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        return cena;
    }

    /// <summary>Um ponto na célula de borda do marcador, que é sempre preta.</summary>
    private static (int Linha, int Coluna) PontoNaBordaDoMarcador => ((int)(25 * PxPorMm), (int)(25 * PxPorMm));

    [Fact]
    public void O_marcador_informado_nao_entra_na_mascara()
    {
        using var cena = CenaComMarcadorMaiorQueAPeca();
        var lado = (float)(100 * PxPorMm);
        var o = (float)(20 * PxPorMm);
        var cantos = new[] { new Point2f(o, o), new Point2f(o + lado, o), new Point2f(o + lado, o + lado), new Point2f(o, o + lado) };

        using var mascara = Recortador.Recortar(cena, cantos, MetodoDeRecorte.Otsu);

        Assert.Equal(0, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
        Assert.Equal(255, mascara.At<byte>((int)(200 * PxPorMm), (int)(300 * PxPorMm)));
    }

    [Fact]
    public void Sem_os_cantos_o_marcador_vira_a_peca()
    {
        // Par negativo de O_marcador_informado_nao_entra_na_mascara, na mesma cena: prova que é o
        // apagamento que tira o marcador.
        using var cena = CenaComMarcadorMaiorQueAPeca();

        using var mascara = Recortador.Recortar(cena, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
    }

    [Fact]
    public void GrabCut_recorta_peca_com_ruido()
    {
        var (cena, verdade) = Cena(Scalar.All(50), Scalar.All(210), comRuido: true);
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, null, MetodoDeRecorte.GrabCut);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.9);
        }
    }

    [Fact]
    public void Imagem_sem_objeto_da_mascara_vazia()
    {
        using var lisa = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(128));

        using var mascara = Recortador.Recortar(lisa, null, MetodoDeRecorte.Otsu);

        Assert.Null(BuscaPorFoto.Mascara.DeMascaraCheia(mascara, null));
    }
}

public class SobreposicaoTests
{
    [Fact]
    public void Pinta_so_dentro_da_mascara()
    {
        using var imagem = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(128));
        using var mascara = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Rectangle(mascara, new Rect(30, 30, 40, 40), Scalar.All(255), -1);

        using var saida = Sobreposicao.Gerar(imagem, mascara);

        Assert.Equal(imagem.Size(), saida.Size());
        Assert.NotEqual(imagem.At<Vec3b>(50, 50), saida.At<Vec3b>(50, 50));
        Assert.Equal(imagem.At<Vec3b>(5, 5), saida.At<Vec3b>(5, 5));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `Recortador`, `MetodoDeRecorte`, `Sobreposicao` não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Foto/Recortador.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Foto;

public enum MetodoDeRecorte { Otsu, GrabCut }

public static class Recortador
{
    /// <summary>Lado máximo, em px, em que o GrabCut roda; a máscara volta ampliada ao tamanho original.</summary>
    public const int LadoMaximoDoGrabCut = 1000;

    /// <summary>Fração da imagem, em cada borda, que o GrabCut assume como fundo certo.</summary>
    public const double MargemDoGrabCut = 0.05;

    /// <summary>
    /// Máscara do tamanho da imagem, 255 no maior objeto (com os furos dele em 0). Quando
    /// <paramref name="cantosDoMarcador"/> é informado, o marcador é apagado antes do recorte, para
    /// não virar "a peça".
    /// </summary>
    public static Mat Recortar(Mat imagemBgr, Point2f[]? cantosDoMarcador, MetodoDeRecorte metodo)
    {
        using var semMarcador = imagemBgr.Clone();
        if (cantosDoMarcador is not null)
            ApagarMarcador(semMarcador, cantosDoMarcador);

        using var bruta = metodo switch
        {
            MetodoDeRecorte.Otsu => PorOtsu(semMarcador),
            MetodoDeRecorte.GrabCut => PorGrabCut(semMarcador),
            _ => throw new ArgumentOutOfRangeException(nameof(metodo)),
        };
        return MaiorObjetoComFuros(bruta);
    }

    /// <summary>Pinta o marcador, ampliado 20% em torno do centro, com a mediana da borda da imagem.</summary>
    private static void ApagarMarcador(Mat imagem, Point2f[] cantos)
    {
        var cx = cantos.Average(p => p.X);
        var cy = cantos.Average(p => p.Y);
        var ampliado = cantos
            .Select(p => new Point((int)Math.Round(cx + 1.2 * (p.X - cx)), (int)Math.Round(cy + 1.2 * (p.Y - cy))))
            .ToArray();
        Cv2.FillConvexPoly(imagem, ampliado, CorDaBorda(imagem));
    }

    private static Scalar CorDaBorda(Mat imagem)
    {
        // Largura e altura em variável: cada leitura de Mat.Width é uma chamada nativa, e o
        // analisador do OpenCvSharp (OCVS002) reprova o acesso dentro do laço.
        int largura = imagem.Width, altura = imagem.Height;
        var amostras = new List<Vec3b>();
        for (var x = 0; x < largura; x++)
        {
            amostras.Add(imagem.At<Vec3b>(0, x));
            amostras.Add(imagem.At<Vec3b>(altura - 1, x));
        }
        for (var y = 0; y < altura; y++)
        {
            amostras.Add(imagem.At<Vec3b>(y, 0));
            amostras.Add(imagem.At<Vec3b>(y, largura - 1));
        }
        byte Mediana(Func<Vec3b, byte> canal) => amostras.Select(canal).Order().ElementAt(amostras.Count / 2);
        return new Scalar(Mediana(v => v.Item0), Mediana(v => v.Item1), Mediana(v => v.Item2));
    }

    private static Mat PorOtsu(Mat imagemBgr)
    {
        using var cinza = Imagens.ParaCinza(imagemBgr);
        Cv2.GaussianBlur(cinza, cinza, new Size(5, 5), 0);
        var binaria = new Mat();
        Cv2.Threshold(cinza, binaria, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // O Otsu separa claro de escuro, não peça de fundo. O fundo é a classe que domina a borda.
        if (FracaoBrancaNaBorda(binaria) > 0.5)
            Cv2.BitwiseNot(binaria, binaria);

        using var elemento = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(binaria, binaria, MorphTypes.Open, elemento);
        Cv2.MorphologyEx(binaria, binaria, MorphTypes.Close, elemento);
        return binaria;
    }

    private static double FracaoBrancaNaBorda(Mat binaria)
    {
        int largura = binaria.Width, altura = binaria.Height;
        long brancos = 0, total = 0;
        for (var x = 0; x < largura; x++)
        {
            brancos += binaria.At<byte>(0, x) > 0 ? 1 : 0;
            brancos += binaria.At<byte>(altura - 1, x) > 0 ? 1 : 0;
            total += 2;
        }
        for (var y = 0; y < altura; y++)
        {
            brancos += binaria.At<byte>(y, 0) > 0 ? 1 : 0;
            brancos += binaria.At<byte>(y, largura - 1) > 0 ? 1 : 0;
            total += 2;
        }
        return (double)brancos / total;
    }

    private static Mat PorGrabCut(Mat imagemBgr)
    {
        var fator = Math.Min(1.0, (double)LadoMaximoDoGrabCut / Math.Max(imagemBgr.Width, imagemBgr.Height));
        using var reduzida = new Mat();
        Cv2.Resize(imagemBgr, reduzida, new Size(), fator, fator, InterpolationFlags.Area);

        var margemX = (int)(reduzida.Width * MargemDoGrabCut);
        var margemY = (int)(reduzida.Height * MargemDoGrabCut);
        var retangulo = new Rect(margemX, margemY, reduzida.Width - 2 * margemX, reduzida.Height - 2 * margemY);

        using var rotulos = new Mat(reduzida.Size(), MatType.CV_8UC1, Scalar.All(0));
        using var fundo = new Mat();
        using var frente = new Mat();
        Cv2.GrabCut(reduzida, rotulos, retangulo, fundo, frente, 5, GrabCutModes.InitWithRect);

        // GC_FGD = 1, GC_PR_FGD = 3: os dois têm o bit 0 ligado; fundo (0) e provável fundo (2) não.
        using var um = new Mat(rotulos.Size(), MatType.CV_8UC1, Scalar.All(1));
        using var bitZero = new Mat();
        Cv2.BitwiseAnd(rotulos, um, bitZero);
        using var pequena = new Mat();
        Cv2.Multiply(bitZero, Scalar.All(255), pequena);

        var cheia = new Mat();
        Cv2.Resize(pequena, cheia, imagemBgr.Size(), 0, 0, InterpolationFlags.Nearest);
        return cheia;
    }

    private static Mat MaiorObjetoComFuros(Mat binaria)
    {
        var saida = new Mat(binaria.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FindContours(binaria, out var contornos, out var hierarquia, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
        if (contornos.Length == 0) return saida;

        var externos = Enumerable.Range(0, contornos.Length).Where(i => hierarquia[i].Parent < 0).ToList();
        if (externos.Count == 0) return saida;
        var maior = externos.MaxBy(i => Cv2.ContourArea(contornos[i]));

        Cv2.DrawContours(saida, contornos, maior, Scalar.All(255), -1);
        for (var filho = hierarquia[maior].Child; filho >= 0; filho = hierarquia[filho].Next)
            Cv2.DrawContours(saida, contornos, filho, Scalar.All(0), -1);
        return saida;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Foto/Sobreposicao.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Foto;

/// <summary>
/// A imagem que o humano olha para classificar o recorte como ok ou falho: a foto com a máscara
/// pintada de verde por cima e o contorno em vermelho.
/// </summary>
public static class Sobreposicao
{
    public static Mat Gerar(Mat imagemBgr, Mat mascaraCheia)
    {
        var saida = imagemBgr.Clone();
        using var verde = new Mat(imagemBgr.Size(), MatType.CV_8UC3, new Scalar(0, 255, 0));
        using var misturada = new Mat();
        Cv2.AddWeighted(imagemBgr, 0.5, verde, 0.5, 0, misturada);
        misturada.CopyTo(saida, mascaraCheia);

        Cv2.FindContours(mascaraCheia, out var contornos, out _, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);
        Cv2.DrawContours(saida, contornos, -1, new Scalar(0, 0, 255), Math.Max(2, imagemBgr.Width / 400));
        return saida;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 23`, 0 falhas.

- [ ] **Step 5: Mutações medidas**

Uma de cada vez, restaurando entre elas:

1. Em `Recortar`, não chamar `ApagarMarcador`. Expected: `O_marcador_informado_nao_entra_na_mascara` falha.
2. Em `PorOtsu`, nunca inverter a binária (`if (false && FracaoBrancaNaBorda(...) > 0.5)`). Expected: 4 testes falham, entre eles `Otsu_recorta_peca_escura_em_fundo_claro_com_o_furo`.

**Não medido, e fica para a review:** se o GrabCut regride sem que nenhum teste note (há um único teste dele, e com limiar mais frouxo, 0,9).

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): recorte da peça por Otsu ou GrabCut, com sobreposição para conferência

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Pontuadores e ranking

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorIoU.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorHu.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/Ranking.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Comparacao/PontuadoresTests.cs`

**Interfaces:**
- Consumes: `Mascara` (Task 2).
- Produces:
  - `double PontuadorIoU.Pontuar(Mascara foto, Mascara candidato, bool usarEscala)` — 0 a 1, maior é melhor; `PontuadorIoU.LadoDaGrade` (`128`), `PassoDeRotacaoGraus` (`5`), `FracaoDeAreaSemEscala` (`0.2`).
  - `double PontuadorHu.Pontuar(Mascara foto, Mascara candidato)` — distância com sinal trocado, maior é melhor.
  - `int Ranking.PosicaoPessimista(IReadOnlyDictionary<string, double> pontuacaoPorCandidato, string idDaCerta)`.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Comparacao/PontuadoresTests.cs`:

```csharp
using BuscaPorFoto.Comparacao;
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Comparacao;

public class PontuadoresTests
{
    /// <summary>Máscara de um polígono dado em mm, girado, numa escala de <paramref name="mmPorPixel"/>.</summary>
    private static Mascara Poligono(IEnumerable<(double X, double Y)> verticesMm, double mmPorPixel = 0.5,
        double anguloGraus = 0, bool espelhar = false, (double X, double Y, double Lado)? furo = null)
    {
        var rad = anguloGraus * Math.PI / 180;
        Point Converter((double X, double Y) p)
        {
            var x = espelhar ? -p.X : p.X;
            return new Point(
                (int)Math.Round((1000 + x * Math.Cos(rad) - p.Y * Math.Sin(rad)) / mmPorPixel),
                (int)Math.Round((1000 + x * Math.Sin(rad) + p.Y * Math.Cos(rad)) / mmPorPixel));
        }

        using var cheia = new Mat((int)(2000 / mmPorPixel), (int)(2000 / mmPorPixel), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(cheia, new[] { verticesMm.Select(Converter).ToArray() }, Scalar.All(255));
        if (furo is { } f)
        {
            var h = f.Lado / 2;
            Cv2.FillPoly(cheia, new[] { new[] { (f.X - h, f.Y - h), (f.X + h, f.Y - h), (f.X + h, f.Y + h), (f.X - h, f.Y + h) }.Select(Converter).ToArray() }, Scalar.All(0));
        }
        return Mascara.DeMascaraCheia(cheia, mmPorPixel)!;
    }

    private static (double, double)[] Retangulo(double l, double a) => [(0, 0), (l, 0), (l, a), (0, a)];

    /// <summary>Um "L" assimétrico: o espelho dele não coincide com nenhuma rotação dele.</summary>
    private static (double, double)[] Ele() => [(0, 0), (120, 0), (120, 25), (30, 25), (30, 70), (0, 70)];

    [Fact]
    public void IoU_reconhece_a_mesma_forma_girada()
    {
        using var a = Poligono(Ele());
        using var b = Poligono(Ele(), anguloGraus: 37);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void IoU_reconhece_a_mesma_forma_espelhada()
    {
        using var a = Poligono(Ele());
        using var b = Poligono(Ele(), anguloGraus: 90, espelhar: true);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void Sem_escala_retangulos_proporcionais_empatam_e_com_escala_nao()
    {
        using var pequeno = Poligono(Retangulo(100, 50));
        using var grande = Poligono(Retangulo(200, 100));

        Assert.True(PontuadorIoU.Pontuar(pequeno, grande, usarEscala: false) >= 0.9);
        Assert.True(PontuadorIoU.Pontuar(pequeno, grande, usarEscala: true) <= 0.3);
    }

    [Fact]
    public void Com_escala_vale_mesmo_quando_as_duas_mascaras_tem_mm_por_pixel_diferentes()
    {
        using var foto = Poligono(Retangulo(100, 50), mmPorPixel: 0.5);
        using var candidato = Poligono(Retangulo(100, 50), mmPorPixel: 2);

        Assert.True(PontuadorIoU.Pontuar(foto, candidato, usarEscala: true) >= 0.85);
    }

    [Fact]
    public void IoU_enxerga_o_furo_e_Hu_nao()
    {
        using var comFuro = Poligono(Retangulo(100, 50), furo: (50, 25, 35));
        using var semFuro = Poligono(Retangulo(100, 50));

        Assert.True(PontuadorIoU.Pontuar(comFuro, semFuro, usarEscala: true) <= 0.8);
        Assert.True(PontuadorHu.Pontuar(comFuro, semFuro) >= -1e-6);
    }

    [Fact]
    public void Os_dois_pontuadores_preferem_a_forma_certa_a_uma_diferente()
    {
        using var foto = Poligono(Ele(), anguloGraus: 20);
        using var certa = Poligono(Ele());
        using var errada = Poligono(Retangulo(120, 70));

        Assert.True(PontuadorIoU.Pontuar(foto, certa, true) > PontuadorIoU.Pontuar(foto, errada, true) + 0.1);
        Assert.True(PontuadorHu.Pontuar(foto, certa) > PontuadorHu.Pontuar(foto, errada));
    }

    [Fact]
    public void Com_escala_exige_mm_por_pixel_nas_duas()
    {
        using var semEscala = Mascara.DeMascaraCheia(Poligono(Retangulo(100, 50)).Pixels.Clone(), null)!;
        using var comEscala = Poligono(Retangulo(100, 50));

        Assert.Throws<ArgumentException>(() => PontuadorIoU.Pontuar(semEscala, comEscala, usarEscala: true));
    }
}

public class RankingTests
{
    [Fact]
    public void Posicao_pela_pontuacao_decrescente()
    {
        var p = new Dictionary<string, double> { ["a"] = 0.9, ["b"] = 0.8, ["c"] = 0.7 };

        Assert.Equal(1, Ranking.PosicaoPessimista(p, "a"));
        Assert.Equal(2, Ranking.PosicaoPessimista(p, "b"));
        Assert.Equal(3, Ranking.PosicaoPessimista(p, "c"));
    }

    [Fact]
    public void Empate_conta_contra_a_peca_certa_seja_qual_for_a_ordem()
    {
        var p = new Dictionary<string, double> { ["a"] = 0.8, ["b"] = 0.8, ["c"] = 0.8, ["d"] = 0.1 };

        Assert.Equal(3, Ranking.PosicaoPessimista(p, "a"));
        Assert.Equal(3, Ranking.PosicaoPessimista(p, "c"));
    }

    [Fact]
    public void Candidato_certo_ausente_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Ranking.PosicaoPessimista(new Dictionary<string, double> { ["a"] = 1 }, "z"));
    }

    [Fact]
    public void NaN_e_recusado()
    {
        Assert.Throws<ArgumentException>(() =>
            Ranking.PosicaoPessimista(new Dictionary<string, double> { ["a"] = double.NaN, ["b"] = 1 }, "b"));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `PontuadorIoU`, `PontuadorHu`, `Ranking` não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorIoU.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Sobreposição de máscaras numa grade comum, com busca de rotação e espelho. Maior é melhor,
/// de 0 a 1.
/// </summary>
public static class PontuadorIoU
{
    public const int LadoDaGrade = 128;
    public const int PassoDeRotacaoGraus = 5;

    /// <summary>
    /// Sem escala, as duas máscaras são normalizadas para esta fração da área da grade.
    /// </summary>
    public const double FracaoDeAreaSemEscala = 0.2;

    /// <param name="usarEscala">
    /// Verdadeiro: cada máscara entra no tamanho real (exige <see cref="Mascara.MmPorPixel"/> nas
    /// duas), e diferença de tamanho derruba a pontuação. Falso: as duas são levadas à mesma área.
    /// </param>
    public static double Pontuar(Mascara foto, Mascara candidato, bool usarEscala)
    {
        var (escalaFoto, escalaCandidato) = Escalas(foto, candidato, usarEscala);

        using var gradeCandidato = NaGrade(candidato.Pixels, escalaCandidato, 0, espelhar: false);
        using var fotoReduzida = Redimensionar(foto.Pixels, escalaFoto);
        var centroide = Centroide(fotoReduzida);

        var melhor = 0.0;
        foreach (var espelhar in new[] { false, true })
            for (var angulo = 0; angulo < 360; angulo += PassoDeRotacaoGraus)
            {
                using var gradeFoto = Girar(fotoReduzida, centroide, angulo, espelhar);
                melhor = Math.Max(melhor, IoU(gradeFoto, gradeCandidato));
            }
        return melhor;
    }

    /// <summary>
    /// Fator pixel-da-máscara → pixel-da-grade de cada uma. A restrição é que as duas caibam na
    /// grade em QUALQUER rotação: o raio (maior distância do centróide a um canto da caixa) vezes a
    /// escala não passa de metade do lado.
    /// </summary>
    private static (double Foto, double Candidato) Escalas(Mascara foto, Mascara candidato, bool usarEscala)
    {
        var raioMaximo = LadoDaGrade / 2.0 * 0.98;
        double sFoto, sCandidato;
        if (usarEscala)
        {
            if (foto.MmPorPixel is not { } mmFoto || candidato.MmPorPixel is not { } mmCandidato)
                throw new ArgumentException("Pontuar com escala exige MmPorPixel nas duas máscaras.");
            var raioMm = Math.Max(Raio(foto.Pixels) * mmFoto, Raio(candidato.Pixels) * mmCandidato);
            var mmPorCelula = raioMm / raioMaximo;
            sFoto = mmFoto / mmPorCelula;
            sCandidato = mmCandidato / mmPorCelula;
        }
        else
        {
            var areaAlvo = FracaoDeAreaSemEscala * LadoDaGrade * LadoDaGrade;
            sFoto = Math.Sqrt(areaAlvo / Cv2.CountNonZero(foto.Pixels));
            sCandidato = Math.Sqrt(areaAlvo / Cv2.CountNonZero(candidato.Pixels));
            var k = Math.Min(1.0, raioMaximo / Math.Max(Raio(foto.Pixels) * sFoto, Raio(candidato.Pixels) * sCandidato));
            sFoto *= k;
            sCandidato *= k;
        }
        return (sFoto, sCandidato);
    }

    private static double Raio(Mat mascara)
    {
        var c = Centroide(mascara);
        int w = mascara.Width, h = mascara.Height;
        return new[] { (0.0, 0.0), (w, 0.0), (w, (double)h), (0.0, (double)h) }
            .Max(p => Math.Sqrt((p.Item1 - c.X) * (p.Item1 - c.X) + (p.Item2 - c.Y) * (p.Item2 - c.Y)));
    }

    private static Point2d Centroide(Mat mascara)
    {
        var m = Cv2.Moments(mascara, binaryImage: true);
        return new Point2d(m.M10 / m.M00, m.M01 / m.M00);
    }

    /// <summary>Reduz com INTER_AREA antes de girar: amostrar uma máscara grande direto na grade de 128 px perde as partes finas.</summary>
    private static Mat Redimensionar(Mat mascara, double escala)
    {
        var tamanho = new Size(Math.Max(1, (int)Math.Round(mascara.Width * escala)), Math.Max(1, (int)Math.Round(mascara.Height * escala)));
        var saida = new Mat();
        Cv2.Resize(mascara, saida, tamanho, 0, 0, InterpolationFlags.Area);
        Cv2.Threshold(saida, saida, 127, 255, ThresholdTypes.Binary);
        return saida;
    }

    private static Mat NaGrade(Mat mascara, double escala, int angulo, bool espelhar)
    {
        using var reduzida = Redimensionar(mascara, escala);
        return Girar(reduzida, Centroide(reduzida), angulo, espelhar);
    }

    /// <summary>Leva o centróide ao centro da grade, espelha (em x) se pedido, e gira.</summary>
    private static Mat Girar(Mat mascara, Point2d centroide, int angulo, bool espelhar)
    {
        var rad = angulo * Math.PI / 180;
        double c = Math.Cos(rad), s = Math.Sin(rad), mx = espelhar ? -1 : 1;
        double meio = LadoDaGrade / 2.0;
        using var afim = new Mat(2, 3, MatType.CV_64FC1);
        afim.Set(0, 0, c * mx); afim.Set(0, 1, -s); afim.Set(0, 2, meio - (c * mx * centroide.X - s * centroide.Y));
        afim.Set(1, 0, s * mx); afim.Set(1, 1, c); afim.Set(1, 2, meio - (s * mx * centroide.X + c * centroide.Y));

        var grade = new Mat();
        Cv2.WarpAffine(mascara, grade, afim, new Size(LadoDaGrade, LadoDaGrade), InterpolationFlags.Nearest,
            BorderTypes.Constant, Scalar.All(0));
        return grade;
    }

    private static double IoU(Mat a, Mat b)
    {
        using var e = new Mat();
        using var ou = new Mat();
        Cv2.BitwiseAnd(a, b, e);
        Cv2.BitwiseOr(a, b, ou);
        var uniao = Cv2.CountNonZero(ou);
        return uniao == 0 ? 0 : (double)Cv2.CountNonZero(e) / uniao;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorHu.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Comparacao;

/// <summary>
/// Momentos de Hu do contorno EXTERNO — a linha de base que o roadmap cita. Invariante a rotação,
/// escala e espelho (o I1 compara logaritmos dos módulos), e cego aos furos por construção.
/// Devolve a distância com sinal trocado, para que maior seja melhor, como no IoU.
/// </summary>
public static class PontuadorHu
{
    public static double Pontuar(Mascara foto, Mascara candidato)
    {
        using var a = SoOContornoExterno(foto.Pixels);
        using var b = SoOContornoExterno(candidato.Pixels);
        return -Cv2.MatchShapes(a, b, ShapeMatchModes.I1);
    }

    private static Mat SoOContornoExterno(Mat mascara)
    {
        Cv2.FindContours(mascara, out var contornos, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        var preenchida = new Mat(mascara.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(preenchida, contornos, -1, Scalar.All(255), -1);
        return preenchida;
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/Ranking.cs`:

```csharp
namespace BuscaPorFoto.Comparacao;

public static class Ranking
{
    /// <summary>
    /// Posição (1 = primeiro) da peça certa, maior pontuação primeiro. EMPATE CONTA CONTRA: todo
    /// candidato com pontuação igual à da certa fica à frente dela. Assim a ordem de enumeração —
    /// que vem de nome de arquivo — nunca melhora o resultado.
    /// </summary>
    public static int PosicaoPessimista(IReadOnlyDictionary<string, double> pontuacaoPorCandidato, string idDaCerta)
    {
        if (!pontuacaoPorCandidato.TryGetValue(idDaCerta, out var daCerta))
            throw new ArgumentException($"O candidato certo '{idDaCerta}' não está entre os pontuados.", nameof(idDaCerta));
        if (pontuacaoPorCandidato.Values.Any(double.IsNaN))
            throw new ArgumentException("Pontuação NaN: não há ordem definida.", nameof(pontuacaoPorCandidato));

        return 1 + pontuacaoPorCandidato.Count(kv => kv.Key != idDaCerta && kv.Value >= daCerta);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 34`, 0 falhas.

- [ ] **Step 5: Mutações medidas**

Uma de cada vez, restaurando entre elas:

1. `PontuadorIoU.Pontuar` sem espelho (`new[] { false }`). Expected: `IoU_reconhece_a_mesma_forma_espelhada` falha.
2. `Ranking.PosicaoPessimista` com `>` no lugar de `>=`. Expected: `Empate_conta_contra_a_peca_certa_seja_qual_for_a_ordem` falha.
3. `PontuadorIoU` nunca entrando no ramo de escala. Atenção: `if (false)` não compila com `TreatWarningsAsErrors` (CS0162, código inacessível) — use uma condição que o compilador não resolva. Expected: `Sem_escala_retangulos_proporcionais_empatam_e_com_escala_nao` falha.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): pontuadores IoU e Hu, e ranking com empate contra a peça certa

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Execução sobre a pasta de dados

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ConjuntoDeDados.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeResultados.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeClassificacaoDoRecorte.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/Executor.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/ConjuntoSintetico.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Execucao/ExecutorTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 2 a 5.
- Produces:
  - `sealed record Alvo(string Codigo, string CaminhoDoStl, string CaminhoSemMarcador, string CaminhoComMarcador)`; `sealed record ConjuntoDeDados(IReadOnlyList<Alvo> Alvos, IReadOnlyList<string> Distratores)` com `static ConjuntoDeDados Ler(string pasta)` (lança `InvalidDataException` listando o que falta).
  - `enum Condicao { SemMarcador, ComMarcador }`; `enum NomeDoPontuador { Hu, IoU }`; `sealed record LinhaDeResultado(string Id, bool Plana, Condicao Condicao, NomeDoPontuador Pontuador, int? Posicao, string? Falha, long Milissegundos)`.
  - `ArquivoDeResultados.Escrever(string caminho, IEnumerable<LinhaDeResultado>)`, `IReadOnlyList<LinhaDeResultado> ArquivoDeResultados.Ler(string caminho)`.
  - `sealed record ClassificacaoDoRecorte(string Id, Condicao Condicao, bool? RecorteOk, string? ParEspelhado)`; `ArquivoDeClassificacaoDoRecorte.Cabecalho`, `IReadOnlyList<ClassificacaoDoRecorte> ArquivoDeClassificacaoDoRecorte.Ler(string caminho)`.
  - `IReadOnlyList<LinhaDeResultado> Executor.Executar(ConjuntoDeDados dados, string pastaDeSaida, MetodoDeRecorte metodo, TextWriter log)`; constantes de nome de arquivo `Executor.ArquivoDeMapa`, `ArquivoDeResultadosCsv`, `ArquivoDeClassificacao`, `ArquivoDeExecucao`, `PastaDeSobreposicoes`.
  - CLI: `BuscaPorFoto.Cli executar <pasta-de-dados> <pasta-de-saida> [--recorte otsu|grabcut]`.

**Os dois testes de vazamento guardam coisas diferentes, e só os dois juntos fecham a spec §4.5.** `Trocar_os_codigos_nao_muda_a_posicao_de_nenhuma_geometria` pega influência de nome ou ordem. Ele **não** pega um favorecimento direto da peça certa (um bônus somado à pontuação dela), porque esse bônus sobrevive à troca de nomes. Quem pega esse é `Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2`: sem escala, o par proporcional empata, e com o empate pessimista um deles tem de ficar em 2º — um bônus o poria em 1º.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/ConjuntoSintetico.cs`:

```csharp
using OpenCvSharp;

namespace BuscaPorFoto.Tests.Sinteticos;

/// <param name="Codigo">Nome da pasta do alvo (o "código real" do teste).</param>
public sealed record ChapaSintetica(string Codigo, float Largura, float Altura, float LadoDoFuro);

/// <summary>Monta em disco uma pasta de dados no formato do spike, com fotos sintéticas.</summary>
public static class ConjuntoSintetico
{
    public static readonly Scalar Fundo = Scalar.All(200);
    public const double PxPorMm = 4;

    public static void Montar(string pasta, IEnumerable<ChapaSintetica> alvos, IEnumerable<(string Nome, byte[] Stl)> distratores,
        bool marcadorNaFotoComMarcador = true)
    {
        foreach (var (chapa, i) in alvos.Select((c, i) => (c, i)))
        {
            var dir = Directory.CreateDirectory(Path.Combine(pasta, "alvos", chapa.Codigo)).FullName;
            File.WriteAllBytes(Path.Combine(dir, "solido.stl"),
                MalhaSintetica.StlBinario(MalhaSintetica.ChapaComFuro(chapa.Largura, chapa.Altura, 3, chapa.LadoDoFuro)));
            using (var sem = Fotografar(chapa, comMarcador: false, anguloGraus: 15 + 40 * i))
                Cv2.ImWrite(Path.Combine(dir, "sem-marcador.png"), sem);
            using (var com = Fotografar(chapa, comMarcador: marcadorNaFotoComMarcador, anguloGraus: 70 + 25 * i))
                Cv2.ImWrite(Path.Combine(dir, "com-marcador.png"), com);
        }

        var dirDistratores = Directory.CreateDirectory(Path.Combine(pasta, "distratores")).FullName;
        foreach (var (nome, stl) in distratores)
            File.WriteAllBytes(Path.Combine(dirDistratores, nome), stl);
    }

    private static Mat Fotografar(ChapaSintetica chapa, bool comMarcador, double anguloGraus)
    {
        using var cena = new Mat((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, Fundo);
        if (comMarcador)
            CenaSintetica.DesenharMarcador(cena, new Point2d(20, 20), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(255, 175), chapa.Largura, chapa.Altura, chapa.LadoDoFuro,
            anguloGraus, PxPorMm, Scalar.All(50), Fundo);
        return CenaSintetica.EmPerspectiva(cena, Fundo);
    }
}
```

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Execucao/ExecutorTests.cs`:

```csharp
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Tests.Sinteticos;

namespace BuscaPorFoto.Tests.Execucao;

public sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; } = Directory.CreateTempSubdirectory("busca-por-foto-").FullName;
    public void Dispose() => Directory.Delete(Caminho, recursive: true);
}

public class ExecutorTests
{
    // A-100 e B-200 são proporcionais (a mesma forma no dobro do tamanho): só a escala os separa.
    private static readonly ChapaSintetica[] Alvos =
    [
        new("A-100", 100, 50, 20),
        new("B-200", 200, 100, 40),
        new("C-150", 150, 60, 15),
    ];

    private static readonly (string, byte[])[] Distratores =
    [
        ("caixa.stl", MalhaSintetica.StlBinario(MalhaSintetica.Caixa(100, 80, 60))),
        ("quadrada.stl", MalhaSintetica.StlBinario(MalhaSintetica.ChapaComFuro(120, 120, 3, 30))),
    ];

    private static IReadOnlyList<LinhaDeResultado> Rodar(string dados, string saida) =>
        Executor.Executar(ConjuntoDeDados.Ler(dados), saida, MetodoDeRecorte.Otsu, TextWriter.Null);

    [Fact]
    public void Com_marcador_e_IoU_cada_alvo_fica_em_primeiro()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos, Distratores);

        var linhas = Rodar(dados.Caminho, saida.Caminho);

        var comMarcador = linhas.Where(l => l.Condicao == Condicao.ComMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.Equal(3, comMarcador.Count);
        Assert.All(comMarcador, l => Assert.Equal(1, l.Posicao));
        Assert.All(linhas, l => Assert.True(l.Plana));
    }

    [Fact]
    public void Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos, Distratores);

        var linhas = Rodar(dados.Caminho, saida.Caminho);

        var semMarcador = linhas.Where(l => l.Condicao == Condicao.SemMarcador && l.Pontuador == NomeDoPontuador.IoU).ToList();
        Assert.All(semMarcador, l => Assert.InRange(l.Posicao!.Value, 1, 2));
        // O par proporcional empata no ranking pessimista: pelo menos um deles não fica em 1º.
        Assert.Contains(semMarcador.Where(l => l.Id is "P01" or "P02"), l => l.Posicao == 2);
    }

    [Fact]
    public void Nenhum_arquivo_da_saida_exceto_o_mapa_contem_o_codigo_real()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos, Distratores);

        Rodar(dados.Caminho, saida.Caminho);

        var mapa = File.ReadAllText(Path.Combine(saida.Caminho, Executor.ArquivoDeMapa));
        // Positivo conhecido: a varredura abaixo acharia o código se ele estivesse lá.
        Assert.Contains("B-200", mapa);
        foreach (var arquivo in Directory.GetFiles(saida.Caminho, "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f) != Executor.ArquivoDeMapa))
            foreach (var alvo in Alvos)
            {
                Assert.DoesNotContain(alvo.Codigo, Path.GetFileName(arquivo));
                if (!arquivo.EndsWith(".png", StringComparison.Ordinal))
                    Assert.DoesNotContain(alvo.Codigo, File.ReadAllText(arquivo));
            }
        Assert.Equal(6, Directory.GetFiles(Path.Combine(saida.Caminho, Executor.PastaDeSobreposicoes), "*.png").Length);
    }

    [Fact]
    public void Trocar_os_codigos_nao_muda_a_posicao_de_nenhuma_geometria()
    {
        // Vazamento: se nome de pasta ou ordem de enumeração influenciasse o ranking, inverter a
        // ordem ordinal dos códigos mudaria alguma posição.
        using var dadosA = new PastaTemporaria();
        using var dadosB = new PastaTemporaria();
        using var saidaA = new PastaTemporaria();
        using var saidaB = new PastaTemporaria();
        ConjuntoSintetico.Montar(dadosA.Caminho, Alvos, Distratores);
        var renomeados = Alvos.Select(a => a with { Codigo = a.Codigo switch { "A-100" => "Z-100", "C-150" => "A-150", var c => c } }).ToArray();
        ConjuntoSintetico.Montar(dadosB.Caminho, renomeados, Distratores);

        var porGeometriaA = PosicoesPorGeometria(Rodar(dadosA.Caminho, saidaA.Caminho), saidaA.Caminho, Alvos);
        var porGeometriaB = PosicoesPorGeometria(Rodar(dadosB.Caminho, saidaB.Caminho), saidaB.Caminho, renomeados);

        Assert.NotEqual(
            File.ReadAllText(Path.Combine(saidaA.Caminho, Executor.ArquivoDeMapa)),
            File.ReadAllText(Path.Combine(saidaB.Caminho, Executor.ArquivoDeMapa)));
        Assert.Equal(porGeometriaA, porGeometriaB);
    }

    private static SortedDictionary<string, int?> PosicoesPorGeometria(
        IReadOnlyList<LinhaDeResultado> linhas, string saida, ChapaSintetica[] alvos)
    {
        var codigoPorId = File.ReadAllLines(Path.Combine(saida, Executor.ArquivoDeMapa)).Skip(1)
            .Select(l => l.Split(';')).ToDictionary(c => c[0], c => c[1]);
        var geometriaPorCodigo = alvos.ToDictionary(a => a.Codigo, a => $"{a.Largura}x{a.Altura}x{a.LadoDoFuro}");
        return new SortedDictionary<string, int?>(linhas.ToDictionary(
            l => $"{geometriaPorCodigo[codigoPorId[l.Id]]}|{l.Condicao}|{l.Pontuador}",
            l => l.Posicao), StringComparer.Ordinal);
    }

    [Fact]
    public void Foto_com_marcador_sem_marcador_vira_falha_e_nao_posicao()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos.Take(1), Distratores, marcadorNaFotoComMarcador: false);

        var linhas = Rodar(dados.Caminho, saida.Caminho);

        var com = linhas.Where(l => l.Condicao == Condicao.ComMarcador).ToList();
        Assert.Equal(2, com.Count);
        Assert.All(com, l => { Assert.Null(l.Posicao); Assert.Equal("marcador não detectado", l.Falha); });
    }

    [Fact]
    public void Resultados_e_modelo_de_classificacao_voltam_do_disco()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos, Distratores);

        var linhas = Rodar(dados.Caminho, saida.Caminho);

        Assert.Equal(linhas, ArquivoDeResultados.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDeResultadosCsv)));
        var classificacao = ArquivoDeClassificacaoDoRecorte.Ler(Path.Combine(saida.Caminho, Executor.ArquivoDeClassificacao));
        Assert.Equal(6, classificacao.Count);
        Assert.All(classificacao, c => Assert.Null(c.RecorteOk));
    }

    [Fact]
    public void Nao_sobrescreve_classificacao_ja_preenchida()
    {
        using var dados = new PastaTemporaria();
        using var saida = new PastaTemporaria();
        ConjuntoSintetico.Montar(dados.Caminho, Alvos.Take(1), Distratores);
        var caminho = Path.Combine(saida.Caminho, Executor.ArquivoDeClassificacao);
        File.WriteAllLines(caminho, [ArquivoDeClassificacaoDoRecorte.Cabecalho, "P01;SemMarcador;ok;", "P01;ComMarcador;falho;"]);

        Rodar(dados.Caminho, saida.Caminho);

        Assert.Contains("P01;ComMarcador;falho;", File.ReadAllLines(caminho));
    }

    [Fact]
    public void Pasta_incompleta_lista_o_que_falta()
    {
        using var dados = new PastaTemporaria();
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "alvos", "X-1"));
        Directory.CreateDirectory(Path.Combine(dados.Caminho, "distratores"));

        var erro = Assert.Throws<InvalidDataException>(() => ConjuntoDeDados.Ler(dados.Caminho));

        Assert.Contains("solido.stl", erro.Message);
        Assert.Contains("sem-marcador", erro.Message);
        Assert.Contains("com-marcador", erro.Message);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `ConjuntoDeDados`, `Executor`, `LinhaDeResultado` e afins não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ConjuntoDeDados.cs`:

```csharp
namespace BuscaPorFoto.Execucao;

public sealed record Alvo(string Codigo, string CaminhoDoStl, string CaminhoSemMarcador, string CaminhoComMarcador);

/// <summary>
/// A pasta de dados do spike, fora do repositório:
/// <c>alvos/&lt;codigo&gt;/{solido.stl, sem-marcador.*, com-marcador.*}</c> e <c>distratores/*.stl</c>.
/// </summary>
public sealed record ConjuntoDeDados(IReadOnlyList<Alvo> Alvos, IReadOnlyList<string> Distratores)
{
    public static readonly string[] ExtensoesDeFoto = [".jpg", ".jpeg", ".png"];

    /// <summary>Alvos em ordem ordinal de código; distratores em ordem ordinal de nome. Falta de arquivo falha alto, listando todos.</summary>
    public static ConjuntoDeDados Ler(string pasta)
    {
        var pastaDeAlvos = Path.Combine(pasta, "alvos");
        var pastaDeDistratores = Path.Combine(pasta, "distratores");
        var faltando = new List<string>();

        if (!Directory.Exists(pastaDeAlvos)) faltando.Add(pastaDeAlvos);
        if (!Directory.Exists(pastaDeDistratores)) faltando.Add(pastaDeDistratores);
        if (faltando.Count > 0) throw Falta(faltando);

        var alvos = new List<Alvo>();
        foreach (var dir in Directory.GetDirectories(pastaDeAlvos).Order(StringComparer.Ordinal))
        {
            var stl = Path.Combine(dir, "solido.stl");
            if (!File.Exists(stl)) faltando.Add(stl);
            var sem = Foto(dir, "sem-marcador", faltando);
            var com = Foto(dir, "com-marcador", faltando);
            alvos.Add(new Alvo(Path.GetFileName(dir), stl, sem, com));
        }

        if (faltando.Count > 0) throw Falta(faltando);
        if (alvos.Count == 0) throw new InvalidDataException($"Nenhum alvo em {pastaDeAlvos}.");

        var distratores = Directory.GetFiles(pastaDeDistratores, "*.stl").Order(StringComparer.Ordinal).ToList();
        return new ConjuntoDeDados(alvos, distratores);
    }

    private static string Foto(string dir, string nome, List<string> faltando)
    {
        var achada = ExtensoesDeFoto.Select(e => Path.Combine(dir, nome + e)).FirstOrDefault(File.Exists);
        if (achada is null) faltando.Add(Path.Combine(dir, nome + "{" + string.Join(",", ExtensoesDeFoto) + "}"));
        return achada ?? "";
    }

    private static InvalidDataException Falta(IEnumerable<string> caminhos) =>
        new("Arquivos ou pastas ausentes:\n  " + string.Join("\n  ", caminhos));
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeResultados.cs`:

```csharp
using System.Globalization;

namespace BuscaPorFoto.Execucao;

public enum Condicao { SemMarcador, ComMarcador }

public enum NomeDoPontuador { Hu, IoU }

/// <param name="Id">Identificador anonimizado (<c>P01</c>…) — nunca o código real.</param>
/// <param name="Posicao">Nulo quando <paramref name="Falha"/> impediu a consulta.</param>
public sealed record LinhaDeResultado(
    string Id, bool Plana, Condicao Condicao, NomeDoPontuador Pontuador, int? Posicao, string? Falha, long Milissegundos);

public static class ArquivoDeResultados
{
    public const string Cabecalho = "id;tipo;condicao;pontuador;posicao;falha;ms";

    public static void Escrever(string caminho, IEnumerable<LinhaDeResultado> linhas)
    {
        var texto = linhas.Select(l => string.Join(';',
            l.Id,
            l.Plana ? "plana" : "soldada",
            l.Condicao,
            l.Pontuador,
            l.Posicao?.ToString(CultureInfo.InvariantCulture) ?? "",
            (l.Falha ?? "").Replace(';', ',').Replace('\n', ' '),
            l.Milissegundos.ToString(CultureInfo.InvariantCulture)));
        File.WriteAllLines(caminho, texto.Prepend(Cabecalho));
    }

    public static IReadOnlyList<LinhaDeResultado> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var c = l.Split(';');
            return new LinhaDeResultado(
                c[0],
                c[1] == "plana",
                Enum.Parse<Condicao>(c[2]),
                Enum.Parse<NomeDoPontuador>(c[3]),
                c[4].Length == 0 ? null : int.Parse(c[4], CultureInfo.InvariantCulture),
                c[5].Length == 0 ? null : c[5],
                long.Parse(c[6], CultureInfo.InvariantCulture));
        }).ToList();
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeClassificacaoDoRecorte.cs`:

```csharp
namespace BuscaPorFoto.Execucao;

/// <param name="RecorteOk">Verdadeiro "ok", falso "falho", nulo ainda não classificado.</param>
/// <param name="ParEspelhado">Id (<c>Pnn</c>) do gêmeo esquerdo/direito, quando houver.</param>
public sealed record ClassificacaoDoRecorte(string Id, Condicao Condicao, bool? RecorteOk, string? ParEspelhado);

/// <summary>
/// Preenchido à mão, olhando as sobreposições: <c>recorte</c> é <c>ok</c>, <c>falho</c> ou vazio;
/// <c>par_espelhado</c> é o id do gêmeo ou vazio.
/// </summary>
public static class ArquivoDeClassificacaoDoRecorte
{
    public const string Cabecalho = "id;condicao;recorte;par_espelhado";

    public static IReadOnlyList<ClassificacaoDoRecorte> Ler(string caminho)
    {
        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length == 0 || linhas[0] != Cabecalho)
            throw new InvalidDataException($"Cabeçalho inesperado em {caminho}.");

        return linhas.Skip(1).Where(l => l.Trim().Length > 0).Select(l =>
        {
            var c = l.Split(';');
            if (c.Length != 4) throw new InvalidDataException($"Linha com {c.Length} colunas em {caminho}: '{l}'.");
            bool? ok = c[2].Trim().ToLowerInvariant() switch
            {
                "ok" => true,
                "falho" => false,
                "" => null,
                var outro => throw new InvalidDataException($"Recorte '{outro}' em {caminho}: use ok, falho ou vazio."),
            };
            var par = c[3].Trim();
            return new ClassificacaoDoRecorte(c[0], Enum.Parse<Condicao>(c[1]), ok, par.Length == 0 ? null : par);
        }).ToList();
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Execucao/Executor.cs`:

```csharp
using System.Diagnostics;
using BuscaPorFoto.Comparacao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Silhueta;
using BuscaPorFoto.Stl;
using OpenCvSharp;

namespace BuscaPorFoto.Execucao;

public static class Executor
{
    /// <summary>Maior lado das máscaras que entram na comparação; acima disso só custa tempo.</summary>
    public const int LadoMaximoDaMascara = 512;

    /// <summary>Maior lado da foto sem marcador antes do recorte (sem escala, reduzir não perde nada).</summary>
    public const int LadoMaximoDaFotoSemMarcador = 2000;

    public const string ArquivoDeMapa = "mapa-de-codigos.csv";
    public const string ArquivoDeResultadosCsv = "resultados.csv";
    public const string ArquivoDeClassificacao = "classificacao-do-recorte.csv";
    public const string ArquivoDeExecucao = "execucao.txt";
    public const string PastaDeSobreposicoes = "sobreposicoes";

    public static IReadOnlyList<LinhaDeResultado> Executar(
        ConjuntoDeDados dados, string pastaDeSaida, MetodoDeRecorte metodo, TextWriter log)
    {
        Directory.CreateDirectory(Path.Combine(pastaDeSaida, PastaDeSobreposicoes));

        // Anonimização: a ordem dos alvos já é ordinal por código (ConjuntoDeDados.Ler).
        var ids = dados.Alvos.Select((_, i) => $"P{i + 1:D2}").ToList();
        File.WriteAllLines(Path.Combine(pastaDeSaida, ArquivoDeMapa),
            dados.Alvos.Select((a, i) => $"{ids[i]};{a.Codigo}").Prepend("id;codigo"));
        File.WriteAllText(Path.Combine(pastaDeSaida, ArquivoDeExecucao), $"recorte={metodo}\n");

        // Candidatos: os STL dos alvos, depois os distratores. A chave é um índice opaco.
        var caminhosDosCandidatos = dados.Alvos.Select(a => a.CaminhoDoStl).Concat(dados.Distratores).ToList();
        var candidatos = new List<(string Chave, ResultadoDaProjecao Projecao)>();
        for (var i = 0; i < caminhosDosCandidatos.Count; i++)
        {
            log.WriteLine($"Projetando candidato {i + 1}/{caminhosDosCandidatos.Count}");
            var projecao = ProjetorDeSilhueta.Projetar(LeitorDeStl.Ler(File.ReadAllBytes(caminhosDosCandidatos[i])));
            var reduzidas = projecao.Vistas.Select(v => v.ReduzidaAte(LadoMaximoDaMascara)).ToList();
            foreach (var v in projecao.Vistas) v.Dispose();
            candidatos.Add(($"K{i:D4}", projecao with { Vistas = reduzidas }));
        }

        var linhas = new List<LinhaDeResultado>();
        for (var i = 0; i < dados.Alvos.Count; i++)
        {
            var alvo = dados.Alvos[i];
            var plana = candidatos[i].Projecao.Plana;
            foreach (var condicao in new[] { Condicao.SemMarcador, Condicao.ComMarcador })
            {
                log.WriteLine($"Consultando {ids[i]} {condicao}");
                var caminho = condicao == Condicao.SemMarcador ? alvo.CaminhoSemMarcador : alvo.CaminhoComMarcador;
                var (mascara, falha) = PrepararFoto(caminho, condicao, metodo,
                    Path.Combine(pastaDeSaida, PastaDeSobreposicoes, $"{ids[i]}-{condicao}.png"));

                foreach (var pontuador in new[] { NomeDoPontuador.Hu, NomeDoPontuador.IoU })
                {
                    if (mascara is null)
                    {
                        linhas.Add(new LinhaDeResultado(ids[i], plana, condicao, pontuador, null, falha, 0));
                        continue;
                    }

                    var relogio = Stopwatch.StartNew();
                    var pontuacoes = candidatos.ToDictionary(
                        c => c.Chave,
                        c => c.Projecao.Vistas.Max(vista => pontuador == NomeDoPontuador.Hu
                            ? PontuadorHu.Pontuar(mascara, vista)
                            : PontuadorIoU.Pontuar(mascara, vista, usarEscala: condicao == Condicao.ComMarcador)));
                    var posicao = Ranking.PosicaoPessimista(pontuacoes, candidatos[i].Chave);
                    linhas.Add(new LinhaDeResultado(ids[i], plana, condicao, pontuador, posicao, null, relogio.ElapsedMilliseconds));
                }
                mascara?.Dispose();
            }
        }

        ArquivoDeResultados.Escrever(Path.Combine(pastaDeSaida, ArquivoDeResultadosCsv), linhas);
        EscreverModeloDeClassificacao(Path.Combine(pastaDeSaida, ArquivoDeClassificacao), ids);

        foreach (var c in candidatos)
            foreach (var v in c.Projecao.Vistas) v.Dispose();
        return linhas;
    }

    private static (Mascara? Mascara, string? Falha) PrepararFoto(
        string caminho, Condicao condicao, MetodoDeRecorte metodo, string caminhoDaSobreposicao)
    {
        using var foto = Cv2.ImRead(caminho, ImreadModes.Color);
        if (foto.Empty()) return (null, "foto ilegível");

        Mat imagem;
        Point2f[]? cantos = null;
        double? mmPorPixel = null;
        if (condicao == Condicao.ComMarcador)
        {
            var retificada = Retificador.Retificar(foto);
            if (retificada is null) return (null, "marcador não detectado");
            imagem = retificada.Imagem;
            cantos = retificada.CantosDoMarcador;
            mmPorPixel = retificada.MmPorPixel;
        }
        else
        {
            var fator = Math.Min(1.0, (double)LadoMaximoDaFotoSemMarcador / Math.Max(foto.Width, foto.Height));
            imagem = new Mat();
            Cv2.Resize(foto, imagem, new Size(), fator, fator, InterpolationFlags.Area);
        }

        using (imagem)
        {
            using var cheia = Recortador.Recortar(imagem, cantos, metodo);
            using (var sobreposicao = Sobreposicao.Gerar(imagem, cheia))
                Cv2.ImWrite(caminhoDaSobreposicao, sobreposicao);

            using var mascara = Mascara.DeMascaraCheia(cheia, mmPorPixel);
            return mascara is null ? (null, "recorte vazio") : (mascara.ReduzidaAte(LadoMaximoDaMascara), null);
        }
    }

    /// <summary>
    /// Modelo que o humano preenche olhando as sobreposições. Não sobrescreve um que já exista —
    /// ele pode estar preenchido.
    /// </summary>
    private static void EscreverModeloDeClassificacao(string caminho, IReadOnlyList<string> ids)
    {
        if (File.Exists(caminho)) return;
        var linhas = ids.SelectMany(id => new[] { Condicao.SemMarcador, Condicao.ComMarcador }.Select(c => $"{id};{c};;"));
        File.WriteAllLines(caminho, linhas.Prepend(ArquivoDeClassificacaoDoRecorte.Cabecalho));
    }
}
```

`spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs` (substitui o da Task 1):

```csharp
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Marcador;

const string Uso = """
    Uso:
      BuscaPorFoto.Cli marcador <arquivo.pdf>
      BuscaPorFoto.Cli executar <pasta-de-dados> <pasta-de-saida> [--recorte otsu|grabcut]
    """;

switch (args)
{
    case ["marcador", var pdf]:
        File.WriteAllBytes(pdf, PdfDoMarcador.Gerar());
        Console.WriteLine($"Marcador gravado em {Path.GetFullPath(pdf)}");
        return 0;

    case ["executar", var dados, var saida, .. var resto]:
        var metodo = resto switch
        {
            [] or ["--recorte", "otsu"] => MetodoDeRecorte.Otsu,
            ["--recorte", "grabcut"] => MetodoDeRecorte.GrabCut,
            _ => (MetodoDeRecorte?)null,
        };
        if (metodo is null) break;
        var linhas = Executor.Executar(ConjuntoDeDados.Ler(dados), saida, metodo.Value, Console.Out);
        Console.WriteLine($"{linhas.Count} linhas em {Path.GetFullPath(Path.Combine(saida, Executor.ArquivoDeResultadosCsv))}");
        Console.WriteLine($"Classifique o recorte em {Path.GetFullPath(Path.Combine(saida, Executor.ArquivoDeClassificacao))} olhando {Path.GetFullPath(Path.Combine(saida, Executor.PastaDeSobreposicoes))}.");
        return 0;
}

Console.Error.WriteLine(Uso);
return 2;
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 42`, 0 falhas.

- [ ] **Step 5: Mutação medida**

Em `Executor.Executar`, some `+ (c.Chave == candidatos[i].Chave ? 1e-9 : 0)` à pontuação do IoU. Expected: `Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2` falha. Restaure.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): execução sobre a pasta de dados, anonimizada, com modelo de classificação

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: Relatório e decisão

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/CriterioDeDecisao.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/GeradorDeRelatorio.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs`
- Modify: `spikes/busca-por-foto/README.md`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Relatorio/RelatorioTests.cs`

**Interfaces:**
- Consumes: `LinhaDeResultado`, `ClassificacaoDoRecorte`, `Condicao`, `NomeDoPontuador`, `ArquivoDeResultados`, `ArquivoDeClassificacaoDoRecorte`, `Executor` (Task 6); `MetodoDeRecorte` (Task 4).
- Produces:
  - `enum Veredito { Aprova, ZonaCinza, Reprova }`; `CriterioDeDecisao.MinimoParaAprovar(int)`, `MinimoParaNaoReprovar(int)`, `Veredito Avaliar(int acertosNoTop3, int totalDePlanas)`.
  - `string GeradorDeRelatorio.Gerar(IReadOnlyList<LinhaDeResultado>, IReadOnlyList<ClassificacaoDoRecorte>, MetodoDeRecorte)`; `GeradorDeRelatorio.FracaoDeRecorteFalhoQueAcionaGrabCut` (`1/3`).
  - CLI: `BuscaPorFoto.Cli relatorio <pasta-de-saida>` → `<pasta-de-saida>/relatorio.md`.

**Regras que esta task codifica, todas da spec §6 e do brainstorm:**
- a decisão usa **só IoU** e **só partes planas**; Hu e soldadas aparecem na tabela, não no veredito;
- empate entre as duas condições vai para **sem marcador**, a que não pede nada à fábrica;
- consulta que falhou conta como fora do top-3;
- **regra do GrabCut:** numa execução Otsu com mais de 1/3 dos recortes classificados como falhos, o relatório manda reexecutar com GrabCut, e a decisão passa a ser a daquela execução — é a sequência "Otsu primeiro, GrabCut se falhar muito" da spec §4.3, fixada como número antes da medição;
- **gatilho do plano B:** fora da aprovação, se a maioria dos erros do melhor cenário é de recorte (classificado `falho` ou `recorte vazio`);
- com classificação incompleta, nem a regra do GrabCut nem o gatilho do plano B são avaliados.

- [ ] **Step 1: Escrever os testes que falham**

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Relatorio/RelatorioTests.cs`:

```csharp
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Relatorio;

namespace BuscaPorFoto.Tests.Relatorio;

public class CriterioDeDecisaoTests
{
    [Theory]
    [InlineData(20, 20, Veredito.Aprova)]
    [InlineData(16, 20, Veredito.Aprova)]
    [InlineData(15, 20, Veredito.ZonaCinza)]
    [InlineData(12, 20, Veredito.ZonaCinza)]
    [InlineData(11, 20, Veredito.Reprova)]
    [InlineData(15, 18, Veredito.Aprova)]
    [InlineData(14, 18, Veredito.ZonaCinza)]
    [InlineData(11, 18, Veredito.ZonaCinza)]
    [InlineData(10, 18, Veredito.Reprova)]
    [InlineData(17, 21, Veredito.Aprova)]
    [InlineData(16, 21, Veredito.ZonaCinza)]
    [InlineData(13, 21, Veredito.ZonaCinza)]
    [InlineData(12, 21, Veredito.Reprova)]
    public void Aplica_os_limites_da_spec(int acertos, int total, Veredito esperado)
    {
        Assert.Equal(esperado, CriterioDeDecisao.Avaliar(acertos, total));
    }

    [Fact]
    public void Sem_parte_plana_nao_ha_decisao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CriterioDeDecisao.Avaliar(0, 0));
    }
}

public class GeradorDeRelatorioTests
{
    private static List<LinhaDeResultado> Linhas(int planas, int acertosSem, int acertosCom, int soldadas = 0)
    {
        var linhas = new List<LinhaDeResultado>();
        for (var i = 0; i < planas + soldadas; i++)
        {
            var id = $"P{i + 1:D2}";
            var plana = i < planas;
            foreach (var pontuador in new[] { NomeDoPontuador.Hu, NomeDoPontuador.IoU })
            {
                // Hu sempre em 1º: se o relatório decidisse pelo melhor pontuador, aprovaria tudo.
                // Soldada sempre acerta: se entrasse na conta, inflaria os acertos — uma soldada
                // que nunca acerta deixaria essa mutação passar verde, e deixou, ao medir o plano.
                var posSem = pontuador == NomeDoPontuador.Hu ? 1 : !plana || i < acertosSem ? 2 : 9;
                var posCom = pontuador == NomeDoPontuador.Hu ? 1 : !plana || i < acertosCom ? 3 : 4;
                linhas.Add(new LinhaDeResultado(id, plana, Condicao.SemMarcador, pontuador, posSem, null, 10));
                linhas.Add(new LinhaDeResultado(id, plana, Condicao.ComMarcador, pontuador, posCom, null, 10));
            }
        }
        return linhas;
    }

    private static List<ClassificacaoDoRecorte> Classificacao(int total, Func<int, Condicao, bool?> ok) =>
        Enumerable.Range(0, total)
            .SelectMany(i => new[] { Condicao.SemMarcador, Condicao.ComMarcador }
                .Select(c => new ClassificacaoDoRecorte($"P{i + 1:D2}", c, ok(i, c), null)))
            .ToList();

    [Fact]
    public void Decide_pela_melhor_condicao_so_com_IoU_e_so_com_planas()
    {
        // Soldadas todas acertando não podem entrar no denominador nem nos acertos.
        var linhas = Linhas(planas: 20, acertosSem: 12, acertosCom: 16, soldadas: 5);

        var md = GeradorDeRelatorio.Gerar(linhas, Classificacao(25, (_, _) => true), MetodoDeRecorte.Otsu);

        Assert.Contains("Sem marcador: **12 de 20**", md);
        Assert.Contains("Com marcador: **16 de 20**", md);
        Assert.Contains("Melhor condição: **ComMarcador**", md);
        Assert.Contains("**Veredito: Aprova.**", md);
    }

    [Fact]
    public void Hu_perfeito_nao_aprova_se_o_IoU_reprova()
    {
        var md = GeradorDeRelatorio.Gerar(Linhas(20, 5, 8), Classificacao(20, (_, _) => true), MetodoDeRecorte.Otsu);

        Assert.Contains("**Veredito: Reprova.**", md);
    }

    [Fact]
    public void Empate_entre_condicoes_fica_com_sem_marcador()
    {
        var md = GeradorDeRelatorio.Gerar(Linhas(20, 14, 14), Classificacao(20, (_, _) => true), MetodoDeRecorte.Otsu);

        Assert.Contains("Melhor condição: **SemMarcador**", md);
        Assert.Contains("**Veredito: ZonaCinza.**", md);
    }

    [Fact]
    public void Classificacao_incompleta_nao_avalia_as_regras_de_recorte()
    {
        var md = GeradorDeRelatorio.Gerar(Linhas(20, 12, 13), Classificacao(20, (i, c) => i == 0 && c == Condicao.SemMarcador ? null : true), MetodoDeRecorte.Otsu);

        Assert.Contains("Classificação incompleta: 1 consulta(s)", md);
        Assert.DoesNotContain("**Gatilho do plano B", md);
        Assert.DoesNotContain("Plano B **não** se aplica", md);
        Assert.DoesNotContain("Regra do recorte", md);
    }

    [Fact]
    public void Mais_de_um_terco_de_recorte_falho_numa_execucao_Otsu_manda_reexecutar_com_GrabCut()
    {
        // 14 de 40 falhos = 35% > 1/3.
        var md = GeradorDeRelatorio.Gerar(Linhas(20, 16, 16), Classificacao(20, (i, c) => !(i < 14 && c == Condicao.SemMarcador)), MetodoDeRecorte.Otsu);

        Assert.Contains("reexecute com `--recorte grabcut`", md);
    }

    [Fact]
    public void Ate_um_terco_falho_a_execucao_Otsu_decide()
    {
        // 13 de 40 = 32,5%.
        var md = GeradorDeRelatorio.Gerar(Linhas(20, 16, 16), Classificacao(20, (i, c) => !(i < 13 && c == Condicao.SemMarcador)), MetodoDeRecorte.Otsu);

        Assert.Contains("esta execução Otsu é a que decide", md);
    }

    [Fact]
    public void Plano_B_so_quando_a_maioria_dos_erros_do_melhor_cenario_e_de_recorte()
    {
        // Melhor cenário: sem marcador, 12 acertos, 8 erros (P13..P20).
        var linhas = Linhas(20, acertosSem: 12, acertosCom: 0);

        var cinco = GeradorDeRelatorio.Gerar(linhas, Classificacao(20, (i, c) => !(c == Condicao.SemMarcador && i >= 15)), MetodoDeRecorte.GrabCut);
        var quatro = GeradorDeRelatorio.Gerar(linhas, Classificacao(20, (i, c) => !(c == Condicao.SemMarcador && i >= 16)), MetodoDeRecorte.GrabCut);

        Assert.Contains("**Gatilho do plano B", cinco);
        Assert.Contains("Plano B **não** se aplica", quatro);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `CriterioDeDecisao`, `Veredito`, `GeradorDeRelatorio` não existem.

- [ ] **Step 3: Implementar**

`spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/CriterioDeDecisao.cs`:

```csharp
namespace BuscaPorFoto.Relatorio;

public enum Veredito { Aprova, ZonaCinza, Reprova }

/// <summary>
/// O critério da spec, fixado antes da medição: com 20 partes planas, aprova com ≥ 16 no top-3,
/// reprova com ≤ 11. Com outro total, aprova com ≥ 80% e reprova abaixo de 60%, os dois limites
/// arredondados para cima.
/// </summary>
public static class CriterioDeDecisao
{
    /// <summary>⌈0,8·n⌉ em aritmética inteira: em double, 0,8·20 não é garantidamente 16.</summary>
    public static int MinimoParaAprovar(int totalDePlanas) => (4 * Validar(totalDePlanas) + 4) / 5;

    /// <summary>⌈0,6·n⌉ em aritmética inteira.</summary>
    public static int MinimoParaNaoReprovar(int totalDePlanas) => (3 * Validar(totalDePlanas) + 4) / 5;

    public static Veredito Avaliar(int acertosNoTop3, int totalDePlanas)
    {
        if (acertosNoTop3 < 0 || acertosNoTop3 > totalDePlanas)
            throw new ArgumentOutOfRangeException(nameof(acertosNoTop3));
        if (acertosNoTop3 >= MinimoParaAprovar(totalDePlanas)) return Veredito.Aprova;
        if (acertosNoTop3 >= MinimoParaNaoReprovar(totalDePlanas)) return Veredito.ZonaCinza;
        return Veredito.Reprova;
    }

    private static int Validar(int total) =>
        total > 0 ? total : throw new ArgumentOutOfRangeException(nameof(total), "Sem parte plana não há decisão.");
}
```

`spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/GeradorDeRelatorio.cs`:

```csharp
using System.Globalization;
using System.Text;
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;

namespace BuscaPorFoto.Relatorio;

public static class GeradorDeRelatorio
{
    /// <summary>Acima desta fração de recortes falhos numa execução Otsu, a regra manda reexecutar com GrabCut.</summary>
    public const double FracaoDeRecorteFalhoQueAcionaGrabCut = 1.0 / 3;

    public static string Gerar(
        IReadOnlyList<LinhaDeResultado> linhas,
        IReadOnlyList<ClassificacaoDoRecorte> classificacao,
        MetodoDeRecorte metodo)
    {
        var s = new StringBuilder();
        void L(string texto = "") => s.Append(texto).Append('\n');

        var ids = linhas.Select(l => l.Id).Distinct().ToList();
        var planas = linhas.Where(l => l.Plana).Select(l => l.Id).Distinct().ToList();
        var soldadas = ids.Except(planas).ToList();
        var recorte = classificacao.ToDictionary(c => (c.Id, c.Condicao));

        L("# Spike — busca de peça por foto: resultado");
        L();
        L($"Recorte: **{metodo}**. Partes planas: **{planas.Count}**. Soldadas: **{soldadas.Count}**.");
        L("Posição é contada com empate contra a peça certa. Consulta que falhou (marcador não detectado, recorte vazio, foto ilegível) conta como fora de qualquer top.");
        L();

        L("## Acertos por cenário");
        L();
        L("| Tipo | Condição | Pontuador | Top-1 | Top-3 | Top-5 | Falhas | ms médio |");
        L("|---|---|---|---|---|---|---|---|");
        foreach (var plana in new[] { true, false })
            foreach (var condicao in new[] { Condicao.SemMarcador, Condicao.ComMarcador })
                foreach (var pontuador in new[] { NomeDoPontuador.IoU, NomeDoPontuador.Hu })
                {
                    var grupo = linhas.Where(l => l.Plana == plana && l.Condicao == condicao && l.Pontuador == pontuador).ToList();
                    if (grupo.Count == 0) continue;
                    var consultadas = grupo.Where(l => l.Posicao is not null).ToList();
                    var ms = consultadas.Count == 0 ? "—" : consultadas.Average(l => l.Milissegundos).ToString("F0", CultureInfo.InvariantCulture);
                    L($"| {(plana ? "plana" : "soldada")} | {condicao} | {pontuador} | {Top(grupo, 1)} | {Top(grupo, 3)} | {Top(grupo, 5)} | {grupo.Count(l => l.Posicao is null)} | {ms} |");
                }
        L();

        L("## Decisão (critério fixado na spec antes da medição)");
        L();
        if (planas.Count == 0)
        {
            L("Sem parte plana: não há decisão.");
            return s.ToString();
        }

        var acertos = new[] { Condicao.SemMarcador, Condicao.ComMarcador }.ToDictionary(
            c => c,
            c => linhas.Count(l => l.Plana && l.Condicao == c && l.Pontuador == NomeDoPontuador.IoU && l.Posicao is <= 3));
        // Empate entre as condições vai para "sem marcador": é a que não pede nada à fábrica.
        var melhor = acertos[Condicao.ComMarcador] > acertos[Condicao.SemMarcador] ? Condicao.ComMarcador : Condicao.SemMarcador;
        var veredito = CriterioDeDecisao.Avaliar(acertos[melhor], planas.Count);

        L($"Pontuador da decisão: **IoU** (Hu é só linha de base). Denominador: **{planas.Count}** partes planas.");
        L($"Limites para {planas.Count}: aprova com ≥ {CriterioDeDecisao.MinimoParaAprovar(planas.Count)}, reprova com ≤ {CriterioDeDecisao.MinimoParaNaoReprovar(planas.Count) - 1}.");
        L();
        L($"- Sem marcador: **{acertos[Condicao.SemMarcador]} de {planas.Count}** no top-3");
        L($"- Com marcador: **{acertos[Condicao.ComMarcador]} de {planas.Count}** no top-3");
        L($"- Melhor condição: **{melhor}**");
        L();
        L($"**Veredito: {veredito}.**");
        L();

        var naoClassificadas = ids.SelectMany(id => new[] { Condicao.SemMarcador, Condicao.ComMarcador }.Select(c => (id, c)))
            .Count(k => !recorte.TryGetValue(k, out var r) || r.RecorteOk is null);

        L("## Recorte");
        L();
        if (naoClassificadas > 0)
        {
            L($"**Classificação incompleta: {naoClassificadas} consulta(s) sem ok/falho.** A regra do GrabCut e o gatilho do plano B não são avaliados com classificação incompleta.");
        }
        else
        {
            var falhos = classificacao.Count(c => c.RecorteOk == false);
            var total = classificacao.Count;
            L($"Recortes falhos: **{falhos} de {total}**.");
            if (metodo == MetodoDeRecorte.Otsu)
            {
                var aciona = falhos > FracaoDeRecorteFalhoQueAcionaGrabCut * total;
                L(aciona
                    ? "**Regra do recorte: mais de 1/3 falho — reexecute com `--recorte grabcut` numa pasta de saída nova e decida por aquela execução.**"
                    : "Regra do recorte: até 1/3 falho — esta execução Otsu é a que decide.");
            }

            if (veredito != Veredito.Aprova)
            {
                var erros = linhas.Where(l => l.Plana && l.Condicao == melhor && l.Pontuador == NomeDoPontuador.IoU && l.Posicao is not <= 3).ToList();
                var deRecorte = erros.Count(l => l.Falha == "recorte vazio" || recorte[(l.Id, l.Condicao)].RecorteOk == false);
                L();
                L($"Erros no melhor cenário: **{erros.Count}**, dos quais **{deRecorte}** de recorte.");
                L(deRecorte * 2 > erros.Count
                    ? "**Gatilho do plano B: a maioria dos erros é de recorte.** Acionar é decisão do usuário, com spec própria."
                    : "Plano B **não** se aplica: a maioria dos erros não é de recorte, e embeddings são piores em distinguir tamanho.");
            }
        }
        L();

        var espelhadas = classificacao.Where(c => c.ParEspelhado is not null).Select(c => (c.Id, Par: c.ParEspelhado!)).Distinct().ToList();
        if (espelhadas.Count > 0)
        {
            L("## Pares espelhados (contam na decisão; listados à parte)");
            L();
            foreach (var (id, par) in espelhadas.Select(e => (e.Id, e.Par)).DistinctBy(e => e.Id))
            {
                var pos = linhas.Where(l => l.Id == id && l.Pontuador == NomeDoPontuador.IoU)
                    .Select(l => $"{l.Condicao}: {l.Posicao?.ToString(CultureInfo.InvariantCulture) ?? l.Falha}");
                L($"- {id} (par de {par}) — {string.Join("; ", pos)}");
            }
            L();
        }

        return s.ToString();
    }

    private static string Top(IReadOnlyList<LinhaDeResultado> grupo, int n) =>
        $"{grupo.Count(l => l.Posicao is { } p && p <= n)} de {grupo.Count}";
}
```

`spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs` (substitui o da Task 6):

```csharp
using BuscaPorFoto.Execucao;
using BuscaPorFoto.Foto;
using BuscaPorFoto.Marcador;
using BuscaPorFoto.Relatorio;

const string Uso = """
    Uso:
      BuscaPorFoto.Cli marcador <arquivo.pdf>
      BuscaPorFoto.Cli executar <pasta-de-dados> <pasta-de-saida> [--recorte otsu|grabcut]
      BuscaPorFoto.Cli relatorio <pasta-de-saida>
    """;

switch (args)
{
    case ["marcador", var pdf]:
        File.WriteAllBytes(pdf, PdfDoMarcador.Gerar());
        Console.WriteLine($"Marcador gravado em {Path.GetFullPath(pdf)}");
        return 0;

    case ["executar", var dados, var saida, .. var resto]:
        var metodo = resto switch
        {
            [] or ["--recorte", "otsu"] => MetodoDeRecorte.Otsu,
            ["--recorte", "grabcut"] => MetodoDeRecorte.GrabCut,
            _ => (MetodoDeRecorte?)null,
        };
        if (metodo is null) break;
        var linhas = Executor.Executar(ConjuntoDeDados.Ler(dados), saida, metodo.Value, Console.Out);
        Console.WriteLine($"{linhas.Count} linhas em {Path.GetFullPath(Path.Combine(saida, Executor.ArquivoDeResultadosCsv))}");
        Console.WriteLine($"Classifique o recorte em {Path.GetFullPath(Path.Combine(saida, Executor.ArquivoDeClassificacao))} olhando {Path.GetFullPath(Path.Combine(saida, Executor.PastaDeSobreposicoes))}, depois rode 'relatorio'.");
        return 0;

    case ["relatorio", var saida]:
        var resultados = ArquivoDeResultados.Ler(Path.Combine(saida, Executor.ArquivoDeResultadosCsv));
        var classificacao = ArquivoDeClassificacaoDoRecorte.Ler(Path.Combine(saida, Executor.ArquivoDeClassificacao));
        var execucao = File.ReadAllText(Path.Combine(saida, Executor.ArquivoDeExecucao)).Trim();
        var metodoDaExecucao = Enum.Parse<MetodoDeRecorte>(execucao["recorte=".Length..]);
        var caminho = Path.Combine(saida, "relatorio.md");
        File.WriteAllText(caminho, GeradorDeRelatorio.Gerar(resultados, classificacao, metodoDaExecucao));
        Console.WriteLine($"Relatório em {Path.GetFullPath(caminho)}");
        return 0;
}

Console.Error.WriteLine(Uso);
return 2;
```

Acrescente ao fim de `spikes/busca-por-foto/README.md`:

````markdown

## Execução com os dados reais

A pasta de dados (fora do repositório) tem este formato — o nome da pasta de cada alvo é o código
real da peça, e só aparece no `mapa-de-codigos.csv` da saída:

```
<dados>/
  alvos/<codigo>/solido.stl
  alvos/<codigo>/sem-marcador.jpg   (ou .jpeg / .png — HEIC não: exporte para JPG)
  alvos/<codigo>/com-marcador.jpg
  distratores/*.stl
```

1. Executar com Otsu:

   ```bash
   dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- executar <dados> <saida-otsu>
   ```

2. Abrir `<saida-otsu>/sobreposicoes/` e preencher `<saida-otsu>/classificacao-do-recorte.csv`:
   coluna `recorte` com `ok` (o verde cobre a peça e só ela, furos inclusos) ou `falho`; coluna
   `par_espelhado` com o id do gêmeo esquerdo/direito, quando houver. **Quem classifica é o
   usuário**: as sobreposições são fotos de peça de cliente.
3. Gerar o relatório:

   ```bash
   dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- relatorio <saida-otsu>
   ```

4. Se o relatório mandar reexecutar com GrabCut, repetir 1 a 3 com `--recorte grabcut` numa pasta
   de saída **nova**; a decisão passa a ser a dessa execução.
````

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Aviso(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 63`, 0 falhas.

- [ ] **Step 5: Mutações medidas**

Uma de cada vez, restaurando entre elas:

1. Contar soldadas nos acertos (tirar `l.Plana &&` do cálculo de `acertos`). Expected: `Decide_pela_melhor_condicao_so_com_IoU_e_so_com_planas` falha.
2. Empate entre condições indo para com marcador (`>=` no lugar de `>`). Expected: `Empate_entre_condicoes_fica_com_sem_marcador` falha.
3. Decidir por qualquer pontuador (tirar `l.Pontuador == NomeDoPontuador.IoU &&` do cálculo de `acertos`). Expected: 7 testes falham, entre eles `Hu_perfeito_nao_aprova_se_o_IoU_reprova`.
4. Todo erro contado como de recorte (`var deRecorte = erros.Count;`). Expected: `Plano_B_so_quando_a_maioria_dos_erros_do_melhor_cenario_e_de_recorte` falha.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): relatório com o critério de decisão da spec, regra do GrabCut e gatilho do plano B

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Execução com os dados reais e registro da decisão

**Produto desta task: a medição e a decisão escrita — não código.** Mesmo assim ela **não** é dispensada de review: o texto que entra em `specs/06-roadmap-mvp.md` vira texto do TCC, e afirmação sobre resultado que o relatório não sustenta é exatamente o defeito que as reviews de prosa deste projeto acham.

**Files:**
- Create: `spikes/busca-por-foto/resultado/relatorio.md` (cópia do relatório gerado, **anonimizado**)
- Modify: `specs/06-roadmap-mvp.md` (seção "Fora das fases — decidir por spike: busca de peça por foto")

**Interfaces:**
- Consumes: o CLI inteiro (Tasks 1, 6 e 7) e a pasta de dados do usuário.
- Produces: o veredito e o texto do roadmap.

**Pré-condições, todas do usuário (spec §7.4), conferidas antes de começar:**
- existe um STL por parte (spec §2.2) — se não existir, **pare**: a spec precisa ser revista;
- a pasta de dados está no formato do README, fora do repositório, com ~20 alvos planos, ~5 soldados e ~150 a 200 distratores;
- o marcador foi impresso e **medido com régua** em 100 mm.

- [ ] **Step 1: Validar a pasta sem rodar nada caro**

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- executar <dados> <saida-otsu>`
Expected: se faltar arquivo, a execução para **antes** de projetar, listando cada ausência. Corrija com o usuário e repita. Se não faltar, ela segue direto para o Step 2.

- [ ] **Step 2: Execução Otsu**

A mesma linha do Step 1, até o fim. Anote do log o total de candidatos e confira contra o que o usuário disse ter entregado.

- [ ] **Step 3: Classificação do recorte — pelo usuário**

Peça ao usuário para abrir `<saida-otsu>/sobreposicoes/` e preencher `<saida-otsu>/classificacao-do-recorte.csv` (`ok`/`falho`, e `par_espelhado` quando houver). **Não abra as sobreposições você mesmo** sem autorização explícita dele: são fotos de peça de cliente, e lê-las é enviá-las para fora da máquina — o mesmo motivo que a spec usou para descartar a abordagem de LLM com visão.

- [ ] **Step 4: Relatório**

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- relatorio <saida-otsu>`

Se o relatório mandar reexecutar com GrabCut: repita os Steps 2 a 4 com `--recorte grabcut` numa pasta de saída **nova**, e a decisão passa a ser a dessa execução. Registre as duas.

- [ ] **Step 5: Conferir a anonimização antes de copiar**

Copie `relatorio.md` para `spikes/busca-por-foto/resultado/relatorio.md` e varra contra o mapa:

```bash
cut -d';' -f2 <saida>/mapa-de-codigos.csv | tail -n +2 > "$TEMP/codigos.txt"
grep -c -F -f "$TEMP/codigos.txt" <saida>/mapa-de-codigos.csv
grep -F -f "$TEMP/codigos.txt" spikes/busca-por-foto/resultado/relatorio.md
```

Expected: o primeiro `grep` conta **todos** os códigos (positivo conhecido — prova que a varredura casa); o segundo não imprime nada. Um zero no primeiro significa que a varredura está quebrada, não que o relatório está limpo.

- [ ] **Step 6: Registrar a decisão no roadmap**

Reescreva a seção "Fora das fases — decidir por spike: busca de peça por foto" de `specs/06-roadmap-mvp.md` com, e só com o que o relatório sustenta:
- data e o veredito;
- os números em contagem com denominador ("N de M no top-3"), nas duas condições, e qual venceu;
- o método de recorte que decidiu, e se a regra do GrabCut foi acionada;
- a **mudança de universo de candidatos** decidida no brainstorm (spec §2): Componentes com Peça ou Item em Pedido ativo, não a lista do setor — o "Escopo proposto" atual da seção fica desatualizado e tem de ser corrigido junto;
- se reprovou ou ficou em zona cinza: o que o gatilho do plano B disse.

Se aprovou, **não** crie a fase aqui: a seção passa a apontar que a fase precisa de brainstorm próprio, e cita as consequências abertas da spec §2.2 (regra 18 estendida a Itens).

- [ ] **Step 7: Commit**

```bash
git add spikes/busca-por-foto/resultado/relatorio.md specs/06-roadmap-mvp.md
git commit -m "docs(spike): resultado da busca por foto e decisão no roadmap

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
