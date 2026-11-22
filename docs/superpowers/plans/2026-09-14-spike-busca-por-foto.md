# Spike de busca de peça por foto — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** medir, em dois cenários de coleta (o Corte, contra o DXF do nesting, e a forma final, contra o STL), quantas vezes a peça certa fica no top-3 quando a foto do celular é comparada com todos os candidatos do cenário, e decidir cada cenário pelo critério fixado na spec.

**Architecture:** console .NET isolado em `spikes/busca-por-foto/`, fora do `Rastreamento.slnx`. Uma biblioteca (`BuscaPorFoto`) com as etapas independentes — marcador, STL → silhuetas, DXF do nesting → silhuetas, foto → retificação e recorte, pontuação e agrupamento, execução, relatório, ensaio público —, um CLI fino por cima e um projeto de teste que exercita tudo com dados gerados em código. Os dados públicos só entram na Task 9; os reais, na Task 10, na máquina Windows do usuário.

**Tech Stack:** .NET 10 (`net10.0`), OpenCvSharp4 `4.13.0.20260627` com o runtime do sistema (`OpenCvSharp4.runtime.win` no Windows, `OpenCvSharp4.official.runtime.linux-x64` no Linux), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-14-spike-busca-por-foto-design.md`, revisada em 2026-11-22 — leia antes de qualquer task. **Revisão do plano:** 2026-11-22. A versão de 2026-09-14 está no histórico do git deste arquivo (commit `0eec698`), e as Tasks 7 e 8 a citam pelo nome da task de lá.

## Global Constraints

- **Branch da sessão** (em 2026-11-22, `ccr-64a34a45-vaym2e`). A `spike-busca-por-foto` antiga fica como está.
- **Tudo em `spikes/busca-por-foto/`.** Nenhum arquivo fora dela muda, exceto `spikes/busca-por-foto/resultado/` (Tasks 9 e 10) e `specs/06-roadmap-mvp.md` (Task 10).
- **Fora do `Rastreamento.slnx`.** Build e teste só pelo `spikes/busca-por-foto/BuscaPorFoto.slnx`.
- **`TreatWarningsAsErrors` em todos os projetos; o build tem de sair com 0 avisos.** O analisador `OCVS002` do OpenCvSharp reprova ler `Mat.Width`/`Mat.Height` dentro de laço — guarde em variável antes.
- **Versões exatas:** `OpenCvSharp4`, `OpenCvSharp4.runtime.win` e `OpenCvSharp4.official.runtime.linux-x64` em `4.13.0.20260627`, os dois runtimes com `Condition` por sistema (`$([MSBuild]::IsOSPlatform('Windows'))` / `('Linux')`). Medido em 2026-11-22: o runtime Linux nessa versão existe, restaura, e detecta o marcador 4x4_50 distorcido em perspectiva (OpenCV 4.13.0).
- **Dados de cliente nunca no git**, nem na nuvem: STL, DXF, fotos, imagens numeradas, sobreposições e os mapas de código ficam na máquina do usuário. Os dados públicos da Task 9 também não entram no git (licença não comercial, repositório público).
- **A saída do CLI nunca imprime código real nem nome de arquivo do usuário** (spec §10.2: o agente roda a Task 10 pelo Remote Control e vê o que o CLI imprime). Caminhos e mensagens de exceção vão para um arquivo local; o terminal recebe contagens e ids anonimizados.
- **Os pontuadores recebem só `Mascara`; o agrupamento recebe só a geometria das referências** (spec §5.6). Nada de nome de arquivo, código, número de contorno ou índice chega a eles.
- **Ranking pessimista:** empate conta contra a peça certa.
- **Critério de decisão (spec §8), literal, em cada cenário que decide (Corte e forma final):** todos os alvos do cenário, qualquer tipo; top-3; pontuador **IoU**; melhor das duas condições (empate → sem marcador); aprova com ≥ ⌈0,8·n⌉, reprova abaixo de ⌈0,6·n⌉ (com n = 20: aprova ≥ 16, reprova ≤ 11). Alvo cuja referência não saiu separada do nesting sai do denominador. As intermediárias nunca têm veredito.
- **Plano B só na zona cinza**, quando a maioria dos erros do cenário, na melhor condição, é de recorte ("recorte vazio" conta; "marcador não detectado" não).
- **Nomes de domínio em português, técnicos em inglês**; cite alvo **pelo nome** em comentário, nunca por `arquivo:linha` nem por distância ("o teste acima") — `CLAUDE.md`, seção "Convenção de citação".
- **Commit por task**, terminando a mensagem com as linhas de atribuição que a sessão indicar.

## Review Focus

Entradas que a spec implica e que um teste da task dona fixa:

1. **Peça em bloco no nesting.** Software de nesting costuma exportar cada peça como bloco (`INSERT`), girado e às vezes espelhado; ler só as entidades soltas perderia o nesting inteiro em silêncio. Teste: `Insert_de_bloco_e_expandido_com_rotacao_e_espelho` (Task 3).
2. **Código real na saída do terminal.** Pasta incompleta, número de contorno errado e exceção inesperada são os caminhos em que um caminho de arquivo vazaria. Testes: `Pasta_incompleta_lista_o_que_falta_so_no_arquivo_local`, `Erro_inesperado_imprime_so_o_tipo_e_o_arquivo_de_erro` (Task 7).
3. **O centro da foto num furo, ou numa peça dentro do furo de outra.** A peça continua sendo a do centro. Testes: `Peca_com_o_furo_no_centro_ainda_e_a_do_centro`, `Peca_dentro_do_furo_de_outra_e_a_do_centro` (Task 5).
4. **Peça longa e estreita** (tira, barra chata). Na busca de 5 em 5°, meio passo desloca as pontas mais do que a largura dela, e ela não se reconhece nem contra si mesma. Teste: `IoU_reconhece_a_mesma_tira_fina_girada_meio_passo` (Task 6).
5. **Pontas que quase se tocam.** Duas pontas a menos da tolerância de emenda, mas em células vizinhas de uma grade de arredondamento, deixariam o contorno aberto. Teste: `Pontas_a_menos_da_tolerancia_em_celulas_vizinhas_se_emendam` (Task 3).

## Como este plano foi medido

**Em 2026-09-14**, todo o código das Tasks 1 a 7 de então foi escrito, compilado e testado num rascunho, montado do zero task a task, e copiado por script para o plano; a suíte deu 63 testes, verde em 5 execuções. Os defeitos silenciosos que aquela medição achou estão corrigidos no código abaixo e listados nas mutações de cada task.

**Em 2026-11-22**, no Linux da nuvem:

1. **O código de setembro foi extraído do próprio plano por script** (39 arquivos), com o runtime Linux acrescentado: `dotnet build -warnaserror` com 0 avisos e **63 de 63** testes verdes.
2. **As mudanças desta revisão nas Tasks 1, 2, 4, 5 e 6** (as três vistas sempre, o centro da foto, o recorte pelo centro, o refino de rotação e a grade de 256) foram aplicadas no rascunho e medidas: **40 testes** nessas cinco tasks (6 · 7 · 4 · 11 · 12), 0 avisos. O código delas abaixo foi **copiado por script** do rascunho medido. As mutações novas estão listadas por task, cada uma com o teste que a pega.
3. **Duas sondas mudaram o desenho, e o porquê fica aqui.** Cópias idênticas e giradas da mesma forma dão sobreposição de **0,90 a 0,95** na grade da comparação, e um retângulo 4% maior dá **0,93**: a sobreposição não separa "idêntica" de "quase igual", então o agrupamento de referências idênticas (spec §7) usa a **geometria exata** das referências, não a sobreposição (Task 6). E uma tira de 300 × 8 mm, contra si mesma girada, caía a **0,48**; a grade maior sozinha não resolvia (0,51 com 256 e com 384 células), porque a causa é o passo de rotação. O refino de 0,5° em volta do melhor ângulo, com a grade de 256, levou a tira a **0,875** no pior ângulo e o L do teste a **0,965**, a **29 ms** por comparação (contra 24 ms antes).

**O que NÃO está medido:** o código das Tasks 3 (DXF), 7, 8 e 9, e o agrupamento da Task 6 — escritos aqui como assinatura, teste e regra, não como código pronto. E nada sobre foto real: os dados sintéticos são o caso fácil, e que a execução sintética ponha todo alvo em 1º prova o encanamento, não o método.

## Processo de cada task

- **Registre o SHA base antes de despachar o implementer** (`git rev-parse HEAD`), e gere o pacote de review com `scripts/review-package BASE HEAD` da skill — nunca `HEAD~1`.
- **Artefatos no ledger com nome escopado:** `spike-foto-task-N-brief.md`, `spike-foto-task-N-review-report.md`, numa pasta `sdd/2026-11-22-spike-busca-por-foto/`.
- **Gerar brief/pacote é uma chamada; `git add` do ledger é outra**, por caminho explícito (ver `CLAUDE.md`, sobre o `sdd/.gitignore`).
- **A review de toda task que toca ranking, pontuação, agrupamento, separação do nesting ou execução confere, nomeadamente:** vazamento (algo além dos pixels e da geometria influencia a posição?), a conta da posição (empate, falha, grupo) e o aviso no lugar do silêncio. E recebe a lista de mutações da task com o pedido de **tentar burlar os testes de outro jeito**.
- **Tasks 1 a 9 rodam na nuvem (Linux); a Task 10, na máquina Windows do usuário**, pelo Remote Control.

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
    <PackageReference Include="OpenCvSharp4.runtime.win" Version="4.13.0.20260627" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
    <PackageReference Include="OpenCvSharp4.official.runtime.linux-x64" Version="4.13.0.20260627" Condition="$([MSBuild]::IsOSPlatform('Linux'))" />
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
# Dados de cliente (STL, DXF, fotos), os dados públicos do ensaio e a saída do spike NUNCA entram
# neste repositório, que é público. As pastas ficam fora dele; estas linhas só pegam o engano de
# apontá-las para cá.
dados/
saida/
*.stl
*.dxf
*.jpg
*.jpeg
*.png
```

`spikes/busca-por-foto/README.md`:

````markdown
# Spike — busca de peça por foto

Desenho e critério de decisão: `docs/superpowers/specs/2026-09-14-spike-busca-por-foto-design.md`.

Fica **fora** do `Rastreamento.slnx` de propósito: nada aqui entra no build nem nas suítes do
produto. Roda no Linux e no Windows: o projeto restaura o runtime do OpenCvSharp do sistema em que
roda.

**Dados de cliente não entram no git.** STL, DXF, fotos e a pasta de saída vivem numa pasta local
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
Expected: 0 avisos e 0 erros (`0 Warning(s)` / `0 Aviso(s)`, conforme o idioma do SDK).

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 6`, 0 falhas. O teste `A_grade_desenhada_e_detectada_como_o_marcador_do_spike` é também a conferência de que o runtime do OpenCvSharp do sistema traz o ArUco (spec §5.1).

- [ ] **Step 6: Gerar o PDF e validar fora do .NET**

```bash
dotnet run --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- marcador "${TMPDIR:-/tmp}/marcador.pdf"
pdftotext -enc UTF-8 "${TMPDIR:-/tmp}/marcador.pdf" -
```

Expected: o `pdftotext` (no Linux, do `poppler-utils`, presente na nuvem em 2026-11-22; no Windows, do Git for Windows) lê o arquivo sem erro e imprime as quatro linhas de instrução com a acentuação correta ("à página", "régua", "não"). É a prova de que o PDF escrito à mão é válido para um leitor que não é o nosso teste.

- [ ] **Step 7: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): estrutura do spike de busca por foto e PDF do marcador ArUco"
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
  - `IReadOnlyList<Mascara> ProjetorDeSilhueta.Projetar(IReadOnlyList<Triangulo> malha)` — sempre as três vistas principais, a primeira ao longo do eixo de menor dispersão; `ProjetorDeSilhueta.MmPorPixel` (`0.5`).
  - Teste: `MalhaSintetica.Caixa`, `ChapaComFuro`, `Transformar`, `StlBinario`, `StlAscii`.

**Revisto em 2026-11-22** (spec §5.2): toda peça gera as **três** vistas principais, e não só a peça sem face dominante — a dobrada pode estar apoiada na base ou nas abas. A chapa plana pura passou a ser o cenário do Corte, pelo DXF (Task 3). A vista do lado oposto de um eixo não entra: é o espelho da vista da frente, e os pontuadores (Task 6) já testam espelho. Está no XML doc de `Projetar`.

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
    public void Chapa_gera_tres_vistas_e_a_primeira_tem_mm_reais_e_o_furo_vazado()
    {
        var vistas = ProjetorDeSilhueta.Projetar(MalhaSintetica.ChapaComFuro(100, 50, 3, 20));

        Assert.Equal(3, vistas.Count);
        var primeira = vistas[0];
        var (maior, menor, area) = Medir(primeira);
        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
        Assert.Equal(0, primeira.Pixels.At<byte>(primeira.Pixels.Height / 2, primeira.Pixels.Width / 2));
    }

    [Fact]
    public void Chapa_em_orientacao_qualquer_da_a_mesma_primeira_vista()
    {
        var girada = MalhaSintetica.Transformar(
            MalhaSintetica.ChapaComFuro(100, 50, 3, 20),
            Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f) * Matrix4x4.CreateTranslation(500, -200, 80));

        var (maior, menor, area) = Medir(ProjetorDeSilhueta.Projetar(girada)[0]);

        Assert.InRange(maior, 99, 102);
        Assert.InRange(menor, 49, 52);
        Assert.InRange(area, 4600 * 0.97, 4600 * 1.03);
    }

    [Fact]
    public void Caixa_gera_as_tres_vistas_principais()
    {
        var vistas = ProjetorDeSilhueta.Projetar(MalhaSintetica.Caixa(100, 80, 60));

        Assert.Equal(3, vistas.Count);
        var areas = vistas.Select(v => Medir(v).AreaMm2).Order().ToArray();
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

public static class ProjetorDeSilhueta
{
    public const double MmPorPixel = 0.5;

    /// <summary>
    /// As três vistas principais, uma por eixo, sempre: a primeira olha ao longo do eixo de menor
    /// dispersão (numa chapa, a face maior). Toda peça do cenário B tem três, porque a dobrada pode
    /// estar apoiada na base ou nas abas (spec §5.2). A vista do lado oposto de um eixo não entra:
    /// é o espelho da primeira, e os pontuadores já testam espelho.
    /// </summary>
    public static IReadOnlyList<Mascara> Projetar(IReadOnlyList<Triangulo> malha)
    {
        if (malha.Count == 0)
            throw new ArgumentException("Malha vazia.", nameof(malha));

        var (centro, eixos) = EixosPrincipais(malha);
        return new[]
        {
            Vista(malha, centro, eixos[0], eixos[1]),
            Vista(malha, centro, eixos[0], eixos[2]),
            Vista(malha, centro, eixos[1], eixos[2]),
        };
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

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → 0 avisos.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: `Total: 13`, 0 falhas.

- [ ] **Step 5: Mutação medida**

Troque, em `EixosPrincipais`, o segundo momento exato por vértices ponderados pela área (cada vértice de cada triângulo somado com peso `Area(t)`). Expected: `Caixa_gera_as_tres_vistas_principais` e os dois testes de chapa falham — foi o defeito real que a medição de setembro achou, remedido em 2026-11-22 sobre o código revisto. Restaure.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): leitor de STL e projeção da silhueta em mm reais"
```

---

### Task 3: DXF do nesting → silhuetas

**Nova em 2026-11-22** (spec §5.3). Escrita como assinatura, teste e regra: o código **não** foi medido em rascunho.

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Dxf/LeitorDeDxf.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Dxf/SeparadorDeNesting.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Dxf/ImagemNumerada.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/DxfSintetico.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/AgrupadorDeReferencias.cs` (só o record `AssinaturaGeometrica`; o agrupador é da Task 6)
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Dxf/DxfTests.cs`

**Interfaces:**
- Consumes: `Mascara` (Task 2).
- Produces:
  - `readonly record struct Trecho(Vector2 A, Vector2 B, string Camada)` — em mm, `System.Numerics.Vector2`.
  - `sealed record DesenhoDxf(IReadOnlyList<Trecho> Trechos, IReadOnlyList<string> Avisos)`; `DesenhoDxf LeitorDeDxf.Ler(string texto)` (lança `FormatException` para DXF binário ou sem seção `ENTITIES`); `LeitorDeDxf.ErroMaximoDaCordaMm` (`0.05`), `LeitorDeDxf.ProfundidadeMaximaDeBloco` (`8`).
  - `sealed record PecaDoNesting(int Numero, Mascara Silhueta, int Furos, Rect2d CaixaMm, AssinaturaGeometrica Assinatura)` — `AssinaturaGeometrica` é da Task 6; nesta task, declare-a já no arquivo `Comparacao/AgrupadorDeReferencias.cs` só com o record (`sealed record AssinaturaGeometrica(int Furos, double[] Valores)`), e a Task 6 acrescenta o agrupador.
  - `sealed record NestingSeparado(IReadOnlyList<PecaDoNesting> Pecas, IReadOnlyList<string> Avisos)`; `NestingSeparado SeparadorDeNesting.Separar(DesenhoDxf desenho, IReadOnlySet<string> camadasIgnoradas)`; `SeparadorDeNesting.ToleranciaDeEmendaMm` (`0.01`), `SeparadorDeNesting.MmPorPixel` (`0.5`, o mesmo do `ProjetorDeSilhueta`).
  - `Mat ImagemNumerada.Gerar(NestingSeparado nesting)` — BGR, maior lado `ImagemNumerada.LadoMaximo` (`2000`).
  - Teste: `DxfSintetico` — construtor fluente que escreve DXF ASCII: `ComUnidade(int insunits)`, `SemUnidade()`, `Linha(x1, y1, x2, y2, camada = "0")`, `Arco(cx, cy, raio, deGraus, ateGraus)`, `Circulo(cx, cy, raio, camada = "0")`, `Polilinha((double X, double Y, double Bulge)[] vertices, bool fechada)` (LWPOLYLINE), `PolilinhaAntiga(...)` (POLYLINE + VERTEX + SEQEND), `Spline()`, `Texto()`, `Bloco(string nome, Action<DxfSintetico> conteudo, double baseX = 0, double baseY = 0)`, `Insert(string nome, x, y, escalaX = 1, escalaY = 1, rotacaoGraus = 0)`, `string Gerar()`; e `static string Retangulo(...)`-like ajudantes à vontade do implementador.

**Regras que o código desta task segue** (o que a assinatura e os testes não determinam sozinhos):

- **Leitura.** O DXF ASCII é uma sequência de pares (código de grupo, valor), uma linha cada. Lê `HEADER` (`$INSUNITS`, grupo 70), `BLOCKS` e `ENTITIES`. Unidade: 4 → mm (×1), 1 → polegada (×25,4), 5 → cm (×10), 6 → m (×1000); 0 ou ausente → ×1 com o aviso `"sem $INSUNITS: assumido milímetro"`; outro valor → ×1 com `"unidade {n} não suportada: assumido milímetro"`. A camada é o grupo 8.
- **Entidades lidas:** `LINE` (10, 20, 11, 21); `ARC` (10, 20, 40, 50, 51 — graus, sentido anti-horário de 50 a 51); `CIRCLE` (10, 20, 40); `LWPOLYLINE` (70 bit 1 = fechada; cada vértice 10/20 seguido, opcionalmente, de 42 = *bulge*); `POLYLINE` + `VERTEX` (10, 20, 42) + `SEQEND`, 70 bit 1 = fechada; `INSERT` (2 = nome, 10, 20, 41, 42 — padrão 1 —, 50 graus). O bloco (`BLOCK` 2 = nome, 10/20 = ponto base … `ENDBLK`) é expandido em cada `INSERT`: p' = R(θ)·S·(p − base) + inserção, com escala negativa espelhando. `INSERT` dentro de bloco expande de novo, até `ProfundidadeMaximaDeBloco`; além disso, aviso.
- **Arco vira trechos ao ler**, antes de qualquer transformação (assim o espelho de um bloco não inverte o sentido de um arco por engano): n = ⌈|Δθ| / (2·acos(1 − e/r))⌉, com e = `ErroMaximoDaCordaMm`, e no mínimo 8 num círculo inteiro. *Bulge* b: ângulo do arco = 4·atan(b), positivo anti-horário, raio a partir da corda.
- **Ignorados sem aviso:** `TEXT`, `MTEXT`. **Qualquer outra entidade** (`SPLINE`, `ELLIPSE`, `DIMENSION`, `HATCH`, …) gera um aviso por tipo: `"{n} entidade(s) {TIPO} não lida(s)"`.
- **Emenda.** Os trechos das camadas ignoradas saem antes de tudo. Pontas a até `ToleranciaDeEmendaMm` são o mesmo nó, achadas por **busca em grade com as 8 células vizinhas**, não por arredondamento simples (que separa duas pontas próximas que caem em células vizinhas). Trecho de comprimento zero sai.
- **Contorno fechado** é a componente conexa em que **todo nó tem grau 2**. Componente com nó de grau 1 é aberta (linha de dobra, cota): sai, com o aviso `"{n} trecho(s) aberto(s) ignorado(s)"`. Componente com nó de grau ≥ 3 é a linha comum: sai, com `"{n} nó(s) com três ou mais trechos: possível corte em linha comum; as peças desse trecho não foram separadas"`.
- **Aninhamento.** A profundidade de um contorno é quantos outros o contêm (um vértice dele dentro do outro e área menor). Um contorno de profundidade 0 que contém **todos** os outros, havendo ao menos um, é a borda da chapa: sai, com `"borda da chapa removida"`, e as profundidades descem 1. Profundidade par é borda de peça; ímpar é furo da peça de profundidade par imediatamente acima.
- **Silhueta:** borda preenchida menos os furos, rasterizada a `MmPorPixel` com margem de 2 px, `Mascara` com `MmPorPixel` preenchido.
- **Numeração:** de cima para baixo pela borda de cima da caixa (o y do DXF cresce para cima) e, empatando dentro de 1 mm, da esquerda para a direita; começa em 1. É só para o usuário apontar o alvo (Task 7): ela não entra em ranking nenhum.
- **Assinatura** (Task 6): `Furos` e `Valores = [área, perímetro, soma das áreas dos furos, λ1, λ2]`, calculados do **polígono** (borda e furos), não dos pixels; λ1 ≥ λ2 são os momentos principais de segunda ordem da região (borda menos furos) em torno do centróide.
- **Imagem numerada:** fundo branco, peças em cinza claro com contorno preto e o número no centróide, y invertido para a imagem ficar como no CAD.

- [ ] **Step 1: Escrever os testes que falham**

`DxfTests.cs`, com o `DxfSintetico` acima. Áreas com tolerância de ±2% (a corda dos arcos e a rasterização); "uma peça" é `Assert.Single(nesting.Pecas)`.

| Teste | Montagem | Afirma |
|---|---|---|
| `Retangulo_de_polilinha_fechada_vira_uma_peca_em_mm_reais` | `ComUnidade(4)`, LWPOLYLINE fechada (0,0)(100,0)(100,50)(0,50) | 1 peça, área da silhueta 5000 mm², 0 furos, `Avisos` vazio |
| `Circulo_dentro_da_peca_e_furo` | o retângulo + `Circulo(50, 25, 10)` | `Furos == 1`, área 5000 − 314,16, o pixel do centro da silhueta é 0 |
| `Bulge_vira_arco` | LWPOLYLINE fechada (0,0,0)(100,0,1)(100,50,0)(0,50,0) | área 5000 + π·25²/2 = 5981,7 |
| `Linhas_e_arco_soltos_que_fecham_viram_uma_peca` | 3 `Linha` (0,0)-(80,0), (80,0)-(80,40), (0,40)-(0,0) e `Arco(40, 40, 40, 0, 180)` | 1 peça, área 3200 + π·40²/2 = 5713,3 |
| `PolilinhaAntiga_le_como_a_nova` | o retângulo como `PolilinhaAntiga` | mesma área do primeiro teste |
| `Duas_pecas_e_os_furos_de_cada_uma` | retângulo 100×50 com 2 círculos em (0,0); quadrado 60×60 com 1 círculo em (200,0) | 2 peças; a de área maior tem 2 furos, a outra 1 |
| `Peca_dentro_do_furo_de_outra_e_uma_peca_a_mais` | quadrado 200×200 com furo quadrado de 120 (polilinha); retângulo 40×30 dentro do furo | 2 peças; a grande com 1 furo, a pequena com 0 |
| `Borda_da_chapa_sai_com_aviso` | retângulo 1000×500 envolvendo duas peças | 2 peças, um aviso contendo `"borda da chapa"` |
| `Linha_aberta_dentro_da_peca_e_avisada_e_nao_muda_a_peca` | o retângulo + `Linha(10, 25, 90, 25, "DOBRA")` | 1 peça, 0 furos, área 5000, um aviso contendo `"aberto"` |
| `Linha_comum_e_avisada_e_nao_junta_as_pecas` | dois retângulos 100×50 lado a lado em `Linha`, a aresta x = 100 desenhada uma vez só (7 linhas) | 0 peças, um aviso contendo `"linha comum"` |
| `Entidade_desconhecida_e_avisada` | o retângulo + `Spline()` | 1 peça, um aviso contendo `"SPLINE"` |
| `Texto_e_ignorado_sem_aviso` | o retângulo + `Texto()` | `Avisos` vazio |
| `Insert_de_bloco_e_expandido_com_rotacao_e_espelho` | bloco "P" com o L (0,0)(120,0)(120,25)(30,25)(30,70)(0,70); `Insert("P", 500, 0, rotacaoGraus: 90)` e `Insert("P", 0, 500, escalaX: -1)` | 2 peças com a área do L (4350); a girada com `CaixaMm` 70 de largura e 120 de altura; as duas com a **mesma** `Assinatura` (o espelho não muda área, perímetro nem momentos) |
| `Polegada_vira_milimetro` | `ComUnidade(1)`, retângulo 4×2 | área 101,6 × 50,8 = 5161,3 |
| `Sem_unidade_assume_milimetro_e_avisa` | `SemUnidade()`, o retângulo | área 5000, um aviso contendo `"$INSUNITS"` |
| `Camada_ignorada_nao_vira_furo` | o retângulo + `Circulo(50, 25, 10, "MARCACAO")`; separar com `{"MARCACAO"}` e com `{}` | 0 furos com a camada ignorada, 1 sem (o par prova que é a camada que tira o furo) |
| `Pontas_a_menos_da_tolerancia_em_celulas_vizinhas_se_emendam` | retângulo em 4 `Linha`, com um canto em (100,0049; 0) numa linha e (100,0051; 0) na outra | 1 peça (as duas pontas estão a 0,0002 mm uma da outra, mas o arredondamento simples em células de 0,01 as põe em 10000 e 10001) |
| `Dxf_binario_e_recusado` | texto que começa por `"AutoCAD Binary DXF"` | `FormatException` |
| `Numeracao_vai_de_cima_para_baixo_e_da_esquerda_para_a_direita` | três retângulos de áreas distintas em (0,100), (200,100) e (0,0) | os números 1, 2, 3 nessa ordem, identificados pela área |
| `Imagem_numerada_mostra_cada_peca` | duas peças | `CV_8UC3`, maior lado ≤ 2000, e o pixel no centróide de cada peça não é branco |

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `LeitorDeDxf`, `SeparadorDeNesting`, `ImagemNumerada`, `DxfSintetico` não existem.

- [ ] **Step 3: Implementar `LeitorDeDxf`, `SeparadorDeNesting` e `ImagemNumerada`** pelas regras acima.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Warning(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 20 testes desta task passam, e os 13 das Tasks 1 e 2 continuam verdes (total 33).

- [ ] **Step 5: Mutações, uma de cada vez, restaurando entre elas**

1. Emenda por arredondamento simples (`Math.Round(x / tol)`), sem as células vizinhas. Expected: `Pontas_a_menos_da_tolerancia_em_celulas_vizinhas_se_emendam` falha.
2. Furo pela profundidade ≥ 1, em vez de ímpar. Expected: `Peca_dentro_do_furo_de_outra_e_uma_peca_a_mais` falha.
3. Sem a remoção da borda da chapa. Expected: `Borda_da_chapa_sai_com_aviso` falha.
4. `INSERT` ignorado em vez de expandido. Expected: `Insert_de_bloco_e_expandido_com_rotacao_e_espelho` falha.
5. Componente com nó de grau ≥ 3 aceita como contorno. Expected: `Linha_comum_e_avisada_e_nao_junta_as_pecas` falha.

Registre no relatório da task o resultado de cada uma; uma mutação que sobreviver é um teste que não prova o que diz.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): DXF do nesting em silhuetas, com blocos, separação das peças e imagem numerada"
```

(com as linhas de atribuição da sessão no fim da mensagem)

---

### Task 4: Retificação da foto pelo marcador

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Imagens.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Retificador.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/CenaSintetica.Chapa.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RetificadorTests.cs`

**Interfaces:**
- Consumes: `MarcadorAruco` (Task 1), `CenaSintetica.DesenharMarcador` (Task 1).
- Produces:
  - `internal static Mat Imagens.ParaCinza(Mat imagem)`.
  - `sealed record FotoRetificada(Mat Imagem, Point2f[] CantosDoMarcador, Point2f CentroDaFoto) : IDisposable` com `double MmPorPixel` — `CentroDaFoto` é o centro da foto original levado à saída, onde a peça foi enquadrada (spec §4.2; acréscimo de 2026-11-22).
  - `FotoRetificada? Retificador.Retificar(Mat fotoBgr)`; `Retificador.PixelsPorMm` (`2.0`), `Retificador.AlcanceMaximoMm` (`1500`).
  - Teste: `CenaSintetica.DesenharChapa(Mat, Point2d centroMm, double larguraMm, double alturaMm, double ladoDoFuroMm, double anguloGraus, double pixelsPorMm, Scalar corDaPeca, Scalar corDoFundo)`, `Mat CenaSintetica.EmPerspectiva(Mat cena, Scalar corDoFundo)` e `(Point2f[] Origem, Point2f[] Destino) CenaSintetica.CantosDaPerspectiva(Size tamanho)`.

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
        var (origem, destino) = CantosDaPerspectiva(cena.Size());
        using var h0 = Cv2.GetPerspectiveTransform(origem, destino);
        var saida = new Mat();
        Cv2.WarpPerspective(cena, saida, h0, cena.Size(), InterpolationFlags.Linear, BorderTypes.Constant, corDoFundo);
        return saida;
    }

    /// <summary>Os cantos da cena e para onde <see cref="EmPerspectiva"/> os leva.</summary>
    public static (Point2f[] Origem, Point2f[] Destino) CantosDaPerspectiva(Size tamanho)
    {
        float w = tamanho.Width, h = tamanho.Height;
        var origem = new[] { new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h) };
        var destino = new[]
        {
            new Point2f(0.06f * w, 0.03f * h), new Point2f(0.97f * w, 0.08f * h),
            new Point2f(0.92f * w, 0.98f * h), new Point2f(0.02f * w, 0.93f * h),
        };
        return (origem, destino);
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
    public void O_centro_da_foto_vai_para_o_ponto_da_cena_que_ele_mostra()
    {
        using var foto = Cena(comMarcador: true);
        // Que ponto da cena, em mm, aparece no centro da foto: o inverso da perspectiva simulada.
        var (origem, destino) = CenaSintetica.CantosDaPerspectiva(foto.Size());
        using var daFotoParaACena = Cv2.GetPerspectiveTransform(destino, origem);
        var naCenaPx = Cv2.PerspectiveTransform(new[] { new Point2f(foto.Width / 2f, foto.Height / 2f) }, daFotoParaACena)[0];
        var naCenaMm = new Point2d(naCenaPx.X / PxPorMmDaCena, naCenaPx.Y / PxPorMmDaCena);

        using var retificada = Retificador.Retificar(foto)!;

        // Na saída, o canto do marcador (posto em 20, 20 mm na cena) está em CantosDoMarcador[0].
        var c0 = retificada.CantosDoMarcador[0];
        var esperadoX = c0.X + (naCenaMm.X - 20) * Retificador.PixelsPorMm;
        var esperadoY = c0.Y + (naCenaMm.Y - 20) * Retificador.PixelsPorMm;
        Assert.InRange(retificada.CentroDaFoto.X, esperadoX - 2, esperadoX + 2);
        Assert.InRange(retificada.CentroDaFoto.Y, esperadoY - 2, esperadoY + 2);
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
/// <param name="CentroDaFoto">O centro da foto original, levado às coordenadas de <paramref name="Imagem"/>: é onde a peça foi enquadrada (spec §4.2).</param>
public sealed record FotoRetificada(Mat Imagem, Point2f[] CantosDoMarcador, Point2f CentroDaFoto) : IDisposable
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
        var centro = Cv2.PerspectiveTransform(new[] { new Point2f(fotoBgr.Width / 2f, fotoBgr.Height / 2f) }, homografia)[0];
        return new FotoRetificada(retificada, cantosNaSaida, centro);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → 0 avisos.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 4 testes desta task passam, 0 falhas (com as Tasks 1 a 3, 37).

- [ ] **Step 4b: Mutações medidas em 2026-11-22**

Uma de cada vez, restaurando entre elas:

1. O centro levado pela homografia **sem** a translação (`paraOrigem` no lugar de `homografia`). Expected: `O_centro_da_foto_vai_para_o_ponto_da_cena_que_ele_mostra` falha.
2. O centro da imagem retificada no lugar do centro da foto. Expected: o mesmo teste falha. (Na primeira versão do teste, que só olhava se o centro caía na chapa, esta mutação **sobreviveu**: na cena sintética os dois centros caem na chapa. O teste passou a medir a posição.)

- [ ] **Step 5: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): retificação da foto pela homografia do marcador"
```

---

### Task 5: Recorte da peça pelo centro e imagem de sobreposição

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Recortador.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Foto/Sobreposicao.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/Metricas.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Foto/RecortadorTests.cs`

**Interfaces:**
- Consumes: `Imagens.ParaCinza` (Task 4), `Mascara.DeMascaraCheia` (Task 2), `CenaSintetica.DesenharMarcador` (Task 1), `CenaSintetica.DesenharChapa` (Task 4).
- Produces:
  - `enum MetodoDeRecorte { Otsu, GrabCut }`.
  - `Mat Recortador.Recortar(Mat imagemBgr, Point2f centro, Point2f[]? cantosDoMarcador, MetodoDeRecorte metodo)` — máscara do tamanho da imagem, com o objeto que contém o centro e os furos dele; vazia se nenhum o contém (spec §5.4, revista em 2026-11-22: era o maior objeto).
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

    /// <summary>O centro da cena de 400 × 300 mm, em pixels: (200, 150) mm.</summary>
    private static readonly Point2f Centro = new((float)(200 * PxPorMm), (float)(150 * PxPorMm));

    private static Mat Vazia(Scalar fundo) => new((int)(300 * PxPorMm), (int)(400 * PxPorMm), MatType.CV_8UC3, fundo);

    /// <summary>Cena com uma chapa com furo, que contém o centro da cena, e a máscara-verdade dela.</summary>
    private static (Mat Cena, Mat Verdade) Cena(Scalar peca, Scalar fundo, bool comRuido = false)
    {
        var cena = Vazia(fundo);
        var verdade = new Mat(cena.Size(), MatType.CV_8UC1, Scalar.All(0));
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
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

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
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
        }
    }

    [Fact]
    public void Fica_com_a_peca_do_centro_e_nao_com_a_maior()
    {
        // A chapa grande, de 150 × 100 mm, fica longe do centro; a pequena, de 50 × 30, está nele.
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(90, 80), 150, 100, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 50, 30, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>((int)(150 * PxPorMm), (int)(200 * PxPorMm)));
        Assert.Equal(0, mascara.At<byte>((int)(80 * PxPorMm), (int)(90 * PxPorMm)));
    }

    [Fact]
    public void Peca_com_o_furo_no_centro_ainda_e_a_do_centro()
    {
        // O furo de 40 mm está centrado no centro da cena: o centro cai no furo, não na chapa.
        using var cena = Vazia(Scalar.All(210));
        using var verdade = new Mat(cena.Size(), MatType.CV_8UC1, Scalar.All(0));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 180, 90, 40, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(verdade, new Point2d(200, 150), 180, 90, 40, 0, PxPorMm, Scalar.All(255), Scalar.All(0));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.True(Metricas.IoU(mascara, verdade) >= 0.95);
        Assert.Equal(0, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
    }

    [Fact]
    public void Peca_dentro_do_furo_de_outra_e_a_do_centro()
    {
        // Chapa de 300 × 200 mm com furo de 120 mm, e uma chapa de 40 × 30 dentro do furo, no centro.
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 300, 200, 120, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 40, 30, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
        Assert.Equal(0, mascara.At<byte>((int)(70 * PxPorMm), (int)(70 * PxPorMm)));
    }

    [Fact]
    public void Sem_objeto_no_centro_a_mascara_e_vazia()
    {
        using var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharChapa(cena, new Point2d(60, 60), 80, 50, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Null(BuscaPorFoto.Mascara.DeMascaraCheia(mascara, null));
    }

    /// <summary>
    /// O marcador de 100 mm encostado na chapa do centro: os dois viram um objeto só se o marcador
    /// não for apagado. O marcador ocupa x de 20 a 120 mm; a chapa, de 120 a 280.
    /// </summary>
    private static Mat CenaComMarcadorEncostado()
    {
        var cena = Vazia(Scalar.All(210));
        CenaSintetica.DesenharMarcador(cena, new Point2d(20, 100), PxPorMm);
        CenaSintetica.DesenharChapa(cena, new Point2d(200, 150), 160, 60, 0, 0, PxPorMm, Scalar.All(50), Scalar.All(210));
        return cena;
    }

    private static Point2f[] CantosDoMarcadorEncostado()
    {
        var lado = (float)(100 * PxPorMm);
        float x = (float)(20 * PxPorMm), y = (float)(100 * PxPorMm);
        return [new(x, y), new(x + lado, y), new(x + lado, y + lado), new(x, y + lado)];
    }

    /// <summary>Um ponto na célula de borda do marcador, que é sempre preta.</summary>
    private static (int Linha, int Coluna) PontoNaBordaDoMarcador => ((int)(105 * PxPorMm), (int)(25 * PxPorMm));

    [Fact]
    public void O_marcador_informado_nao_entra_na_mascara()
    {
        using var cena = CenaComMarcadorEncostado();

        using var mascara = Recortador.Recortar(cena, Centro, CantosDoMarcadorEncostado(), MetodoDeRecorte.Otsu);

        Assert.Equal(0, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
        Assert.Equal(255, mascara.At<byte>((int)Centro.Y, (int)Centro.X));
    }

    [Fact]
    public void Sem_os_cantos_o_marcador_encostado_entra_na_mascara()
    {
        // Par negativo de O_marcador_informado_nao_entra_na_mascara, na mesma cena: prova que é o
        // apagamento que tira o marcador.
        using var cena = CenaComMarcadorEncostado();

        using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.Otsu);

        Assert.Equal(255, mascara.At<byte>(PontoNaBordaDoMarcador.Linha, PontoNaBordaDoMarcador.Coluna));
    }

    [Fact]
    public void GrabCut_recorta_peca_com_ruido()
    {
        var (cena, verdade) = Cena(Scalar.All(50), Scalar.All(210), comRuido: true);
        using (cena) using (verdade)
        {
            using var mascara = Recortador.Recortar(cena, Centro, null, MetodoDeRecorte.GrabCut);

            Assert.True(Metricas.IoU(mascara, verdade) >= 0.9);
        }
    }

    [Fact]
    public void Imagem_sem_objeto_da_mascara_vazia()
    {
        using var lisa = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(128));

        using var mascara = Recortador.Recortar(lisa, new Point2f(100, 100), null, MetodoDeRecorte.Otsu);

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
    /// Máscara do tamanho da imagem, 255 no objeto que contém <paramref name="centro"/> (com os
    /// furos dele em 0), e vazia se nenhum o contém. É o objeto do centro, e não o maior, porque a
    /// peça é enquadrada no centro e pode estar sobre uma pilha (spec §4.2, §5.4): o maior pode ser
    /// a chapa de baixo. Quando <paramref name="cantosDoMarcador"/> é informado, o marcador é
    /// apagado antes do recorte, para não se juntar à peça que ele toca.
    /// </summary>
    public static Mat Recortar(Mat imagemBgr, Point2f centro, Point2f[]? cantosDoMarcador, MetodoDeRecorte metodo)
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
        return ObjetoDoCentroComFuros(bruta, centro);
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

    /// <summary>
    /// O menor contorno externo que contém o centro, ignorando os furos: o centro pode cair num furo
    /// da peça, e ela continua sendo a do centro. O menor, porque uma peça posta dentro do furo de
    /// outra também tem contorno externo, e o da peça de fora a envolve.
    /// </summary>
    private static Mat ObjetoDoCentroComFuros(Mat binaria, Point2f centro)
    {
        var saida = new Mat(binaria.Size(), MatType.CV_8UC1, Scalar.All(0));
        Cv2.FindContours(binaria, out var contornos, out var hierarquia, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        var doCentro = Enumerable.Range(0, contornos.Length)
            .Where(i => hierarquia[i].Parent < 0 && Cv2.PointPolygonTest(contornos[i], centro, measureDist: false) >= 0)
            .ToList();
        if (doCentro.Count == 0) return saida;
        var escolhido = doCentro.MinBy(i => Cv2.ContourArea(contornos[i]));

        Cv2.DrawContours(saida, contornos, escolhido, Scalar.All(255), -1);
        for (var filho = hierarquia[escolhido].Child; filho >= 0; filho = hierarquia[filho].Next)
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

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → 0 avisos.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 11 testes desta task passam, 0 falhas (total 48).

- [ ] **Step 5: Mutações medidas**

Uma de cada vez, restaurando entre elas (as quatro medidas em 2026-11-22 sobre o recorte pelo centro):

1. Em `Recortar`, não chamar `ApagarMarcador`. Expected: `O_marcador_informado_nao_entra_na_mascara` falha.
2. Ficar com o maior contorno externo, sem olhar o centro. Expected: `Fica_com_a_peca_do_centro_e_nao_com_a_maior`, `Sem_objeto_no_centro_a_mascara_e_vazia` e `Peca_dentro_do_furo_de_outra_e_a_do_centro` falham.
3. `MaxBy` no lugar de `MinBy` entre os contornos que contêm o centro. Expected: `Peca_dentro_do_furo_de_outra_e_a_do_centro` falha.
4. Aceitar contorno de furo (sem o filtro `Parent < 0`). Expected: `Peca_com_o_furo_no_centro_ainda_e_a_do_centro` falha.

Medida em setembro e não remedida: em `PorOtsu`, nunca inverter a binária faz falhar os testes de Otsu. **Não medido, e fica para a review:** se o GrabCut regride sem que nenhum teste note (há um único teste dele, e com limiar mais frouxo, 0,9).

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): recorte da peça por Otsu ou GrabCut, com sobreposição para conferência"
```

---

### Task 6: Pontuadores, ranking e agrupamento de referências idênticas

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorIoU.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/PontuadorHu.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/Ranking.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/AgrupadorDeReferencias.cs` (criado na Task 3 com o record da assinatura)
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto/Silhueta/ProjetorDeSilhueta.cs` (a assinatura da malha)
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Comparacao/PontuadoresTests.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Comparacao/AgrupadorTests.cs`

**Interfaces:**
- Consumes: `Mascara` (Task 2).
- Produces:
  - `double PontuadorIoU.Pontuar(Mascara foto, Mascara candidato, bool usarEscala)` — 0 a 1, maior é melhor; `PontuadorIoU.LadoDaGrade` (`256`), `PassoDeRotacaoGraus` (`5`), `PassoDoRefinoGraus` (`0.5`), `FracaoDeAreaSemEscala` (`0.2`). A grade e o refino mudaram em 2026-11-22 (ver "Como este plano foi medido"; spec §5.5).
  - `double PontuadorHu.Pontuar(Mascara foto, Mascara candidato)` — distância com sinal trocado, maior é melhor.
  - `int Ranking.PosicaoPessimista(IReadOnlyDictionary<string, double> pontuacaoPorCandidato, string idDaCerta)`.
  - `Referencia`, `AgrupadorDeReferencias.Agrupar`, `AgrupadorDeReferencias.ToleranciaRelativa`, `ProjetorDeSilhueta.Assinatura` — ver o Step 6.

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
    public void IoU_reconhece_a_mesma_tira_fina_girada_meio_passo()
    {
        // 2,5° é o pior caso da busca grossa de 5 em 5°: numa tira de 300 × 8 mm, esse desvio
        // desloca as pontas mais do que a largura dela. Só o refino em volta do melhor ângulo a acha.
        using var a = Poligono(Retangulo(300, 8));
        using var b = Poligono(Retangulo(300, 8), anguloGraus: 2.5);

        Assert.True(PontuadorIoU.Pontuar(a, b, usarEscala: true) >= 0.8);
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
    public const int LadoDaGrade = 256;
    public const int PassoDeRotacaoGraus = 5;

    /// <summary>Passo do refino em volta do melhor ângulo grosso, de cada lado, até um passo grosso.</summary>
    public const double PassoDoRefinoGraus = 0.5;

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
        {
            var (melhorAqui, anguloAqui) = (0.0, 0.0);
            for (var angulo = 0; angulo < 360; angulo += PassoDeRotacaoGraus)
            {
                using var gradeFoto = Girar(fotoReduzida, centroide, angulo, espelhar);
                var iou = IoU(gradeFoto, gradeCandidato);
                if (iou > melhorAqui) (melhorAqui, anguloAqui) = (iou, angulo);
            }
            // Refino em volta do melhor ângulo grosso: numa peça longa e estreita, meio passo de
            // rotação desloca as pontas mais do que a largura dela.
            for (var delta = -PassoDeRotacaoGraus + PassoDoRefinoGraus; delta < PassoDeRotacaoGraus; delta += PassoDoRefinoGraus)
            {
                using var gradeFoto = Girar(fotoReduzida, centroide, anguloAqui + delta, espelhar);
                melhorAqui = Math.Max(melhorAqui, IoU(gradeFoto, gradeCandidato));
            }
            melhor = Math.Max(melhor, melhorAqui);
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
    private static Mat Girar(Mat mascara, Point2d centroide, double angulo, bool espelhar)
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

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → 0 avisos.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 12 testes de comparação e ranking passam, 0 falhas (total 60).

- [ ] **Step 5: Mutações medidas**

Uma de cada vez, restaurando entre elas:

1. `PontuadorIoU.Pontuar` sem espelho (`new[] { false }`). Expected: `IoU_reconhece_a_mesma_forma_espelhada` falha.
2. `Ranking.PosicaoPessimista` com `>` no lugar de `>=`. Expected: `Empate_conta_contra_a_peca_certa_seja_qual_for_a_ordem` falha.
3. `PontuadorIoU` nunca entrando no ramo de escala. Atenção: `if (false)` não compila com `TreatWarningsAsErrors` (CS0162, código inacessível) — use uma condição que o compilador não resolva. Expected: `Sem_escala_retangulos_proporcionais_empatam_e_com_escala_nao` falha.
4. Sem o refino (o laço de `delta` sem nenhuma volta). Expected: `IoU_reconhece_a_mesma_tira_fina_girada_meio_passo` falha (medido em 2026-11-22).

**A grade de 256 não é guardada por teste:** com 128 e o refino, o teste da tira também passa. O 256 é a escolha medida na sonda (pior caso da tira idêntica de 0,75 para 0,875, a 29 ms por comparação), e a review deve tratá-lo como constante escolhida, não como comportamento provado.

- [ ] **Step 6: Agrupamento de referências idênticas — testes que falham**

**Novo em 2026-11-22** (spec §7: o candidato é a peça, não o contorno). Escrito como assinatura e teste; **não** medido em rascunho.

**Por que pela geometria, e não pela sobreposição** (medido em 2026-11-22, ver "Como este plano foi medido"): cópias idênticas e giradas dão sobreposição de 0,90 a 0,95 na grade da comparação, e um retângulo 4% maior dá 0,93. Um limite de sobreposição ou separa cópias idênticas, ou junta peças que não são a mesma. A geometria das referências é exata (o polígono do DXF, a malha do STL), e os invariantes dela não mudam com rotação nem espelho.

**Interfaces acrescentadas:**
- `sealed record AssinaturaGeometrica(int Furos, double[] Valores)` (declarada na Task 3; o DXF a preenche lá).
- `AssinaturaGeometrica ProjetorDeSilhueta.Assinatura(IReadOnlyList<Triangulo> malha)` — `Furos = 0`, `Valores = [área de superfície, λ1, λ2, λ3, extensão nos três eixos principais]`, com os λ e os eixos de `EixosPrincipais`.
- `sealed record Referencia(string Chave, IReadOnlyList<Mascara> Vistas, AssinaturaGeometrica Assinatura)` — a chave é opaca (`K0001`…).
- `IReadOnlyList<IReadOnlyList<string>> AgrupadorDeReferencias.Agrupar(IReadOnlyList<Referencia> referencias)` — grupos de chaves; `AgrupadorDeReferencias.ToleranciaRelativa` (`0.005`).

**Regra:** duas referências são idênticas quando têm o mesmo `Furos`, o mesmo número de `Valores` e cada valor difere no máximo `ToleranciaRelativa` (relativo ao maior dos dois). A identidade é fechada por transitividade (união de conjuntos). A ordem dos grupos e das chaves dentro deles é ordinal pela chave: não entra no ranking, que é pessimista. **O espelho fica no mesmo grupo** — o par esquerdo/direito tem os mesmos invariantes, e a foto não o separa (spec §7). O agrupamento olha só `Assinatura`, nunca as vistas, o nome ou a origem.

`spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Comparacao/AgrupadorTests.cs`:

| Teste | Montagem | Afirma |
|---|---|---|
| `Copias_identicas_viram_um_grupo` | três referências com a mesma assinatura, valores com ruído relativo de 1e-4 | 1 grupo com as três chaves |
| `Diferenca_acima_da_tolerancia_separa` | duas assinaturas iguais menos a área, 1% maior numa | 2 grupos |
| `Numero_de_furos_diferente_separa_mesmo_com_valores_iguais` | `Furos` 1 e 2, valores iguais | 2 grupos |
| `Identidade_e_transitiva` | A~B e B~C dentro da tolerância, A e C fora dela | 1 grupo |
| `Espelho_do_DXF_fica_no_mesmo_grupo` | o L e o L espelhado, lidos por `SeparadorDeNesting` de um `DxfSintetico` com dois `Insert` (escala 1 e −1) | 1 grupo |
| `Malha_girada_tem_a_mesma_assinatura` | `MalhaSintetica.ChapaComFuro(100, 50, 3, 20)` e a mesma girada por `Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.7f, 1.1f)` | assinaturas dentro de `ToleranciaRelativa` |
| `Malha_proporcional_tem_assinatura_diferente` | `ChapaComFuro(100, 50, 3, 20)` e `ChapaComFuro(104, 52, 3, 20.8f)` | 2 grupos |

- [ ] **Step 7: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `Referencia`, `AgrupadorDeReferencias`, `ProjetorDeSilhueta.Assinatura` não existem.

- [ ] **Step 8: Implementar `AgrupadorDeReferencias.Agrupar`, `Referencia` e `ProjetorDeSilhueta.Assinatura`** em `spikes/busca-por-foto/src/BuscaPorFoto/Comparacao/AgrupadorDeReferencias.cs` e `Silhueta/ProjetorDeSilhueta.cs`, pela regra acima.

- [ ] **Step 9: Rodar e ver passar; mutações**

Run: `dotnet build … -warnaserror` → `0 Warning(s)`; `dotnet test …` → os 7 testes do agrupador passam, com os 12 da comparação e os anteriores.

Mutações, uma de cada vez: (1) sem transitividade (agrupar só pares diretos). Expected: `Identidade_e_transitiva` falha. (2) ignorar `Furos`. Expected: `Numero_de_furos_diferente_separa_mesmo_com_valores_iguais` falha. (3) tolerância absoluta de 0,005 no lugar da relativa. Expected: `Copias_identicas_viram_um_grupo` ou `Diferenca_acima_da_tolerancia_separa` falha.

- [ ] **Step 10: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): pontuadores IoU e Hu com refino de rotação, ranking pessimista e agrupamento de referências idênticas"
```

---

### Task 7: Execução por cenário, com a saída anonimizada

**Reescrita em 2026-11-22** (spec §3, §4.3, §7, §10.2). Escrita como assinatura, teste e regra; **não** medida em rascunho. Parte do código da versão de setembro serve como está — a preparação da foto (`PrepararFoto`), o modelo de classificação que não sobrescreve um preenchido, a anonimização por ordem ordinal, a leitura e a escrita dos CSV —: leia-a com `git show 0eec698:docs/superpowers/plans/2026-09-14-spike-busca-por-foto.md`, na seção "Task 6: Execução sobre a pasta de dados".

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ConjuntoDeDados.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeResultados.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDePontuacoes.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/ArquivoDeClassificacaoDoRecorte.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/Executor.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Cli/ComandoDaCli.cs`
- Create: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Sinteticos/ConjuntoSintetico.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto.Cli/Program.cs` (passa a ser só `return ComandoDaCli.Rodar(args, Console.Out, Console.Error);`)
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Execucao/ExecutorTests.cs`
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Execucao/ComandoDaCliTests.cs`

**Interfaces:**
- Consumes: `LeitorDeStl`, `ProjetorDeSilhueta` (Task 2); `LeitorDeDxf`, `SeparadorDeNesting`, `ImagemNumerada` (Task 3); `Retificador` (Task 4); `Recortador`, `Sobreposicao`, `MetodoDeRecorte` (Task 5); `PontuadorIoU`, `PontuadorHu`, `Ranking`, `AgrupadorDeReferencias`, `Referencia` (Task 6); `PdfDoMarcador` (Task 1).
- Produces:
  - `enum Cenario { Corte, FormaFinal, Intermediarias }` — pastas `corte`, `forma-final`, `intermediarias`; prefixos de id `A`, `B`, `I`.
  - `enum TipoDePeca { Chapa, Dobrada, Usinada, Soldada }`.
  - `sealed record AlvoDoCorte(string Codigo, string Nesting, int? Numero, string CaminhoSemMarcador, string CaminhoComMarcador)` — `Numero` nulo é o alvo que não saiu separado do nesting (`-` no `alvos.txt`).
  - `sealed record AlvoComStl(string Codigo, string CaminhoDoStl, TipoDePeca? Tipo, string CaminhoSemMarcador, string CaminhoComMarcador)`.
  - `sealed record DadosDoCorte(IReadOnlyList<string> Nestings, IReadOnlyList<AlvoDoCorte> Alvos)`; `sealed record DadosComStl(IReadOnlyList<AlvoComStl> Alvos, IReadOnlyList<string> OutrosCandidatos)`.
  - `ConjuntoDeDados.LerCorte(string pasta)`, `LerFormaFinal(string pasta)`, `LerIntermediarias(string pasta)` — `pasta` é a raiz da pasta de dados (spec §4.3). Lançam `DadosIncompletosException : InvalidDataException`, com `IReadOnlyList<string> Faltando` (os caminhos) e `Message` **só com a contagem** (`"{n} arquivo(s) ou pasta(s) ausente(s) ou inválido(s)"`).
  - `enum Condicao { SemMarcador, ComMarcador }`, `enum NomeDoPontuador { Hu, IoU }` (como em setembro).
  - `sealed record LinhaDeResultado(string Id, Cenario Cenario, TipoDePeca? Tipo, Condicao Condicao, NomeDoPontuador Pontuador, int? Posicao, string? Falha, double? PontuacaoDaCerta, double? MelhorErrada, int ReferenciasNoGrupoDaCerta, int Grupos, long Milissegundos)`; `ArquivoDeResultados.Escrever/Ler`.
  - `sealed record LinhaDePontuacao(string Id, Condicao Condicao, NomeDoPontuador Pontuador, string Grupo, double Pontuacao, bool Certo)`; `ArquivoDePontuacoes.Escrever/Ler` — grupos opacos `G0001`….
  - `sealed record ClassificacaoDoRecorte(string Id, Condicao Condicao, bool? RecorteOk)`; `ArquivoDeClassificacaoDoRecorte.Cabecalho` (`"id;condicao;recorte"`), `Ler` — **sem** a coluna `par_espelhado` de setembro: o grupo de geometria idêntica (Task 6) já diz isso.
  - `IReadOnlyList<LinhaDeResultado> Executor.ExecutarCorte(DadosDoCorte dados, string saida, MetodoDeRecorte metodo, IReadOnlySet<string> camadasIgnoradas, TextWriter log)`; `Executor.ExecutarComStl(DadosComStl dados, Cenario cenario, string saida, MetodoDeRecorte metodo, TextWriter log)`; `int Executor.GerarImagensNumeradas(DadosDoCorte dados, string saida, IReadOnlySet<string> camadasIgnoradas, TextWriter log)`; constantes de nome de arquivo `ArquivoDeMapa` (`mapa-de-codigos.csv`), `ArquivoDeMapaDeNestings` (`mapa-de-nestings.csv`), `ArquivoDeResultadosCsv`, `ArquivoDePontuacoesCsv`, `ArquivoDeClassificacao`, `ArquivoDeExecucao`, `ArquivoDeFaltas` (`faltando.txt`), `ArquivoDeErro` (`erro.txt`), `PastaDeSobreposicoes`, `PastaDeNestings`.
  - `int ComandoDaCli.Rodar(string[] args, TextWriter saida, TextWriter erro)`.
  - CLI:
    - `marcador <arquivo.pdf>`
    - `nesting <dados> <saida> [--ignorar-camada <nome>]…`
    - `executar <dados> <saida> corte|forma-final|intermediarias [--recorte otsu|grabcut] [--ignorar-camada <nome>]…`
    - `relatorio <saida>` (Task 8)

**Formato da pasta de dados** (spec §4.3), com os acréscimos desta task:
- `corte/alvos.txt`: uma linha por alvo, `<codigo> <arquivo-do-nesting> <número|->`, separado por espaço; linha vazia e linha começando por `#` são ignoradas.
- `forma-final/tipos.txt`: uma linha por alvo, `<codigo> dobrada|usinada|soldada`. Todo alvo de `forma-final/alvos/` tem de ter linha (spec §7 cruza o relatório por tipo). **Acréscimo à spec §4.3**, que não dizia de onde vinha o tipo.
- As intermediárias não têm tipo.

**Regras:**
- **Candidatos.** Corte: toda peça de todo nesting de `corte/nestings/` é uma `Referencia`, com uma vista (a silhueta) e a assinatura do polígono. Forma final: os STL dos alvos de `forma-final/` e os de `forma-final/distratores/`, cada um com as três vistas e a assinatura da malha. Intermediárias: os candidatos da forma final **mais** o STL de cada alvo de `intermediarias/` (spec §7: comparadas contra o universo do B acrescido do próprio STL; o conjunto de candidatos do B não muda).
- **As referências são agrupadas** por `AgrupadorDeReferencias.Agrupar` antes do ranking; o **candidato é o grupo**, com pontuação igual à melhor das referências e vistas dele. O grupo certo é o que contém a referência do alvo (no Corte, a peça `<nesting, número>` do `alvos.txt`). `ReferenciasNoGrupoDaCerta` e `Grupos` vão para a linha.
- **Chaves e ids opacos.** As referências recebem `K0001`… pela ordem ordinal de (arquivo, número), os grupos `G0001`…, os alvos `A01`/`B01`/`I01`… pela ordem ordinal do código. Só os dois mapas ligam id a código e `N01`… a nome de nesting, e eles ficam na pasta de saída local.
- **A foto.** Como em setembro, mais o centro (Tasks 4 e 5): sem marcador, o centro da foto reduzida; com marcador, `FotoRetificada.CentroDaFoto`. A sobreposição se chama `{id}-{condicao}.png`.
- **Falhas, por consulta:** `"marcador não detectado"`, `"recorte vazio"`, `"foto ilegível"`, `"referência não separada do nesting"` (alvo com `-`). Alvo cujo número não existe no nesting **não** é falha de consulta: é erro de dados, e a execução para antes de consultar (`DadosIncompletosException`, com o alvo em `Faltando`).
- **`pontuacoes.csv`:** para cada consulta de IoU que não falhou, uma linha por grupo — é o que a Task 8 usa para as pilhas menores e a melhor errada.
- **`execucao.txt`:** `cenario=…`, `recorte=…`, `camadas-ignoradas=…`.
- **O que o terminal recebe.** O `log` só leva ids, contagens e etapas ("Projetando candidato 12/180", "Consultando A03 ComMarcador"); os avisos do DXF, só contados por tipo de aviso, sem nome de camada nem de arquivo. `ComandoDaCli.Rodar`: `DadosIncompletosException` → grava `Faltando` em `<saida>/faltando.txt` e imprime `"{n} ausente(s); a lista está em <caminho de faltando.txt>"`; **qualquer outra exceção** → grava tipo, mensagem e pilha em `<saida>/erro.txt` (ou no diretório atual, se o comando não tem `<saida>`) e imprime só `"erro: {NomeDoTipo}; detalhes em <caminho>"`, código de saída 1. Uso errado → o texto de uso, código 2.
- **`nesting`** grava `<saida>/nestings/N01.png`… (a `ImagemNumerada` de cada nesting) e o `mapa-de-nestings.csv`, e imprime quantas peças cada `Nnn` tem e quantos avisos. O usuário abre as imagens e escreve o `alvos.txt` com o nome do arquivo do nesting e o número.

- [ ] **Step 1: Escrever os testes que falham**

`ConjuntoSintetico` monta em disco a pasta de dados nos três formatos, com as fotos sintéticas de setembro (`Fotografar`: a chapa desenhada no **centro** da cena, (200, 150) mm, para o recorte pelo centro achá-la) e um nesting em DXF feito com `DxfSintetico`.

`ExecutorTests.cs`, com os alvos de setembro (A-100 100×50 com furo de 20; B-200 200×100 com furo de 40, proporcional ao primeiro; C-150 150×60 com furo de 15):

| Teste | Afirma |
|---|---|
| `Corte_com_marcador_e_IoU_cada_alvo_fica_em_primeiro` | nesting com os três alvos e dois distratores; toda linha `ComMarcador`/IoU com `Posicao == 1` |
| `FormaFinal_com_marcador_e_IoU_cada_alvo_fica_em_primeiro` | STL dos três alvos e dois distratores (a `Caixa` e a chapa quadrada de setembro); idem, e `Tipo` vem do `tipos.txt` |
| `Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2` | como em setembro, no cenário forma final: toda posição `SemMarcador`/IoU entre 1 e 2, e A-100 ou B-200 em 2 — **guarda de vazamento**: um bônus à peça certa a poria em 1º |
| `Trocar_os_codigos_nao_muda_a_posicao_de_nenhuma_geometria` | como em setembro, nos dois cenários — **guarda de vazamento** por nome e ordem |
| `Copias_do_mesmo_contorno_no_nesting_contam_como_um_candidato` | nesting com o alvo A-100 repetido 4 vezes (uma espelhada) e os distratores; `Posicao == 1` com marcador, `ReferenciasNoGrupoDaCerta == 4`, `Grupos` = peças distintas |
| `Alvo_marcado_como_nao_separado_vira_falha_de_referencia` | `alvos.txt` com `-`; as quatro linhas do alvo com `Posicao` nula e `Falha == "referência não separada do nesting"` |
| `Numero_inexistente_no_nesting_e_recusado_antes_de_consultar` | número 9 num nesting de 5 peças; `DadosIncompletosException`, nenhuma sobreposição gravada |
| `Intermediaria_compara_contra_a_forma_final_mais_o_proprio_stl` | intermediária com STL novo: `Grupos` = grupos do B + 1; com STL idêntico a um do B: `Grupos` = grupos do B |
| `Foto_com_marcador_sem_marcador_vira_falha_e_nao_posicao` | como em setembro |
| `Nenhum_arquivo_da_saida_exceto_os_mapas_contem_codigo_ou_nome_de_nesting` | varre os nomes e o conteúdo (menos `.png`) de tudo na saída, menos os dois mapas, contra os códigos **e** o nome do arquivo do nesting; positivo conhecido: os mapas contêm os dois |
| `O_log_nao_contem_codigo_nem_nome_de_nesting` | o `TextWriter` do log, capturado, não contém nenhum dos dois; positivo conhecido: ele contém `"A01"` |
| `Resultados_pontuacoes_e_modelo_de_classificacao_voltam_do_disco` | `ArquivoDeResultados.Ler` e `ArquivoDePontuacoes.Ler` devolvem o que o executor devolveu; o modelo de classificação tem 2 linhas por alvo, todas sem classificação |
| `Nao_sobrescreve_classificacao_ja_preenchida` | como em setembro |
| `Pasta_incompleta_lista_o_que_falta_so_no_arquivo_local` | alvo sem `solido.stl`, sem foto, sem linha no `tipos.txt`: `Faltando` tem os três caminhos; a `Message` não contém o código do alvo |

`ComandoDaCliTests.cs`:

| Teste | Afirma |
|---|---|
| `Pasta_incompleta_imprime_so_a_contagem_e_o_caminho_da_lista` | `executar` numa pasta com um alvo `X-SECRETO` incompleto: código 1, a saída e o erro não contêm `X-SECRETO`, e `faltando.txt` contém |
| `Erro_inesperado_imprime_so_o_tipo_e_o_arquivo_de_erro` | `executar` com `<dados>` = uma pasta inexistente chamada `X-SECRETO`: código 1, a saída e o erro não contêm `X-SECRETO`, e `erro.txt` contém |
| `Uso_errado_devolve_2` | `executar` sem cenário |
| `Marcador_grava_o_pdf` | `marcador <arquivo>`: código 0, o arquivo começa por `%PDF-1.4` |

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `ConjuntoDeDados.LerCorte`, `Executor.ExecutarCorte`, `ComandoDaCli` e afins não existem.

- [ ] **Step 3: Implementar** `ConjuntoDeDados`, os três arquivos CSV, `Executor` e `ComandoDaCli` pelas regras acima, e trocar o `Program.cs`.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Warning(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 18 testes desta task passam, e nenhum anterior quebra.

- [ ] **Step 5: Mutações, uma de cada vez**

1. Somar `1e-9` à pontuação do grupo certo. Expected: `Sem_marcador_os_proporcionais_se_confundem_mas_ficam_no_top_2` falha (a mutação de setembro, no executor novo).
2. Ranking por referência em vez de por grupo. Expected: `Copias_do_mesmo_contorno_no_nesting_contam_como_um_candidato` falha.
3. Escrever o código no nome da sobreposição. Expected: `Nenhum_arquivo_da_saida_exceto_os_mapas_contem_codigo_ou_nome_de_nesting` falha.
4. Imprimir `ex.Message` no terminal. Expected: `Erro_inesperado_imprime_so_o_tipo_e_o_arquivo_de_erro` falha.
5. Intermediárias contra só o próprio STL. Expected: `Intermediaria_compara_contra_a_forma_final_mais_o_proprio_stl` falha.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): execução por cenário, com candidatos agrupados e saída anonimizada"
```

(com as linhas de atribuição da sessão no fim da mensagem)

---

### Task 8: Relatório e decisão, por cenário

**Reescrita em 2026-11-22** (spec §7, §8). Escrita como assinatura, teste e regra; **não** medida em rascunho. O `CriterioDeDecisao` de setembro serve **como está**, só com o parâmetro renomeado para `totalDeAlvos` e a mensagem da exceção para "Sem alvo não há decisão." — leia-o, e o gerador de setembro, com `git show 0eec698:docs/superpowers/plans/2026-09-14-spike-busca-por-foto.md`, na seção "Task 7: Relatório e decisão".

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/CriterioDeDecisao.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/GeradorDeRelatorio.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Relatorio/PilhasMenores.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto/Cli/ComandoDaCli.cs` (o comando `relatorio`)
- Modify: `spikes/busca-por-foto/README.md` (a seção da execução real, abaixo)
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Relatorio/RelatorioTests.cs`

**Interfaces:**
- Consumes: `LinhaDeResultado`, `LinhaDePontuacao`, `ClassificacaoDoRecorte`, `Cenario`, `TipoDePeca`, `Condicao`, `NomeDoPontuador`, os três `Arquivo…` e as constantes do `Executor` (Task 7); `MetodoDeRecorte` (Task 5).
- Produces:
  - `enum Veredito { Aprova, ZonaCinza, Reprova }`; `CriterioDeDecisao.MinimoParaAprovar(int)`, `MinimoParaNaoReprovar(int)`, `Veredito Avaliar(int acertosNoTop3, int totalDeAlvos)`.
  - `sealed record ResultadoDaPilha(int Tamanho, double AcertosMedios, int Alvos)`; `IReadOnlyList<ResultadoDaPilha> PilhasMenores.Simular(IReadOnlyList<LinhaDePontuacao> pontuacoes, Condicao condicao, IReadOnlyList<int> tamanhos, int sorteios, int semente)`; `PilhasMenores.Tamanhos` (`[10, 30, 60]`), `Sorteios` (`200`), `Semente` (`20261122`).
  - `string GeradorDeRelatorio.Gerar(Cenario cenario, IReadOnlyList<LinhaDeResultado> linhas, IReadOnlyList<LinhaDePontuacao> pontuacoes, IReadOnlyList<ClassificacaoDoRecorte> classificacao, MetodoDeRecorte metodo)`; `GeradorDeRelatorio.FracaoDeRecorteFalhoQueAcionaGrabCut` (`1/3`).
  - CLI: `relatorio <saida>` → `<saida>/relatorio.md`, lendo o cenário e o recorte do `execucao.txt`.

**Regras que esta task codifica, todas da spec §7 e §8:**
- **Corte e forma final decidem; intermediárias nunca.** No relatório das intermediárias não existe seção de decisão: só a tabela por foto e a frase "Só informativo (spec §3): 5 fotos não medem taxa de acerto."
- **A decisão usa IoU e todos os alvos do cenário**, de qualquer tipo; Hu aparece na tabela, não no veredito. Denominador: os alvos **menos** os de `"referência não separada do nesting"`, listados à parte; os limites escalam com o denominador real.
- **Empate entre as condições vai para sem marcador.** Consulta que falhou por outro motivo conta como fora do top-3.
- **Tabela de acertos** por tipo (no Corte, todos `Chapa`), condição e pontuador: top-1, top-3, top-5, falhas, ms médio — em contagem, "N de M".
- **Tabela por foto:** id, condição, posição, pontuação da certa, melhor errada, referências no grupo da certa (IoU).
- **Grupos com mais de uma referência:** cada alvo cujo grupo certo tem `ReferenciasNoGrupoDaCerta > 1` é listado ("cópias do mesmo contorno, ou peças de geometria idêntica, inclusive espelhadas: a foto não as separa").
- **Pilhas menores, só informativo:** para cada tamanho de `PilhasMenores.Tamanhos` menor que o número de grupos, `Sorteios` sorteios com `Semente` de (tamanho − 1) grupos errados mais o certo, por alvo, na melhor condição; a média de alvos no top-3. Título da seção: "Pilhas menores (informativo: não entra na decisão)".
- **Regra do GrabCut:** como em setembro, por execução — e cada execução é de um cenário.
- **Plano B só na zona cinza:** com o veredito `ZonaCinza`, se a maioria dos erros da melhor condição é de recorte (classificado `falho` ou `"recorte vazio"`), o gatilho; senão, "Plano B **não** se aplica". Com `Reprova` ou `Aprova`, nenhuma das duas frases.
- **Classificação incompleta:** nem a regra do GrabCut nem o plano B são avaliados.

- [ ] **Step 1: Escrever os testes que falham**

`RelatorioTests.cs`. O gerador de linhas sintéticas de setembro (`Linhas`) ganha o cenário e o tipo, e os alvos de tipos diferentes acertam em proporções conhecidas.

| Teste | Afirma |
|---|---|
| `Aplica_os_limites_da_spec` (`Theory`, os 13 casos de setembro) | como em setembro |
| `Sem_alvo_nao_ha_decisao` | `ArgumentOutOfRangeException` com 0 de 0 |
| `Decide_pela_melhor_condicao_so_com_IoU_e_com_todos_os_tipos` | forma final com 10 dobradas, 6 usinadas e 4 soldadas, 16 acertos com marcador espalhados pelos três tipos: "Com marcador: **16 de 20**", "**Veredito: Aprova.**" — a mutação "só dobradas" daria 8 de 10 e outro texto |
| `Hu_perfeito_nao_aprova_se_o_IoU_reprova` | como em setembro |
| `Empate_entre_condicoes_fica_com_sem_marcador` | como em setembro |
| `Referencia_nao_separada_sai_do_denominador_e_e_listada` | Corte, 20 alvos, 2 com a falha, 15 acertos entre os 18: "**15 de 18**", "Limites para 18: aprova com ≥ 15", veredito Aprova, e os dois ids na lista à parte |
| `Outra_falha_conta_como_erro` | 20 alvos, 4 com `"recorte vazio"`, 15 acertos: "**15 de 20**", ZonaCinza |
| `Intermediarias_nao_tem_veredito` | cenário Intermediarias: o texto não contém "Veredito" e contém "Só informativo" |
| `Mostra_a_pontuacao_da_certa_e_da_melhor_errada` | uma linha com 0,812 e 0,790 aparece na tabela por foto com os dois valores |
| `Lista_o_grupo_com_mais_de_uma_referencia` | um alvo com `ReferenciasNoGrupoDaCerta == 4` aparece na lista; um com 1, não |
| `Pilhas_menores_sao_so_informativas_e_nao_mudam_o_veredito` | o mesmo conjunto com e sem `pontuacoes` dá o mesmo veredito; com, a seção existe com o título informativo |
| `Pilha_simulada_e_deterministica_e_inclui_a_certa` | `PilhasMenores.Simular` duas vezes com a mesma semente dá o mesmo resultado; com a certa sempre em 1º no conjunto inteiro, `AcertosMedios == Alvos` em todo tamanho |
| `Classificacao_incompleta_nao_avalia_as_regras_de_recorte` | como em setembro |
| `Mais_de_um_terco_de_recorte_falho_numa_execucao_Otsu_manda_reexecutar_com_GrabCut` | como em setembro (14 de 40) |
| `Ate_um_terco_falho_a_execucao_Otsu_decide` | como em setembro (13 de 40) |
| `Plano_B_so_na_zona_cinza_e_so_com_a_maioria_de_erros_de_recorte` | 12 acertos (zona cinza), 5 de 8 erros de recorte → gatilho; 4 de 8 → "não se aplica" |
| `Reprova_nao_fala_de_plano_B` | 8 acertos, todos os erros de recorte: nenhuma das duas frases do plano B |

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: FAIL de compilação — `GeradorDeRelatorio.Gerar` com o cenário e `PilhasMenores` não existem.

- [ ] **Step 3: Implementar** `CriterioDeDecisao`, `PilhasMenores`, `GeradorDeRelatorio` e o comando `relatorio`, pelas regras acima.

Acrescente ao fim do `spikes/busca-por-foto/README.md` a seção "Execução com os dados reais", no molde da de setembro, com: o formato da pasta (spec §4.3, com `alvos.txt` e `tipos.txt`), a ordem `nesting` → escrever o `alvos.txt` → `executar … corte` → classificar → `relatorio`, e o mesmo para `forma-final` e `intermediarias`; a regra do GrabCut (reexecutar numa saída **nova**); e a frase "Quem abre as imagens numeradas e as sobreposições é o usuário: são peças de cliente."

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` → `0 Warning(s)`.
Run: `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx`
Expected: os 29 testes desta task passam (os 13 casos da `Theory` contam um cada), e nenhum anterior quebra.

- [ ] **Step 5: Mutações, uma de cada vez**

1. Contar só um tipo no denominador. Expected: `Decide_pela_melhor_condicao_so_com_IoU_e_com_todos_os_tipos` falha.
2. Empate indo para com marcador. Expected: `Empate_entre_condicoes_fica_com_sem_marcador` falha.
3. Decidir por qualquer pontuador. Expected: `Hu_perfeito_nao_aprova_se_o_IoU_reprova` falha.
4. Manter no denominador o alvo não separado. Expected: `Referencia_nao_separada_sai_do_denominador_e_e_listada` falha.
5. Avaliar o plano B também com `Reprova`. Expected: `Reprova_nao_fala_de_plano_B` falha.
6. Pilha menor decidindo no lugar do conjunto inteiro. Expected: `Pilhas_menores_sao_so_informativas_e_nao_mudam_o_veredito` falha.

- [ ] **Step 6: Commit**

```bash
git add spikes/busca-por-foto
git commit -m "feat(spike): relatório por cenário, com o critério da spec, as pilhas menores e o plano B só na zona cinza"
```

(com as linhas de atribuição da sessão no fim da mensagem)

---

### Task 9: Ensaio com dados públicos

**Nova em 2026-11-22** (spec §9). Roda na nuvem. **Pode parar um cenário antes das fotos reais, e não aprova nada.** O produto é código (o deformador e o comando `ensaio`), a execução e um relatório versionado, sem dado nenhum dentro dele.

**Files:**
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Ensaio/Deformador.cs`
- Create: `spikes/busca-por-foto/src/BuscaPorFoto/Ensaio/Ensaio.cs`
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto/Cli/ComandoDaCli.cs` (o comando `ensaio`)
- Modify: `spikes/busca-por-foto/src/BuscaPorFoto/Execucao/Executor.cs` (a falha `"sem foto com marcador"`)
- Test: `spikes/busca-por-foto/tests/BuscaPorFoto.Tests/Ensaio/EnsaioTests.cs`
- Create (na execução): `spikes/busca-por-foto/resultado/ensaio-publico.md`

**Interfaces:**
- Consumes: os leitores (Tasks 2 e 3), `PontuadorIoU`, `Ranking`, `AgrupadorDeReferencias`, `Referencia` (Task 6), `CriterioDeDecisao` (Task 8), `Executor.ExecutarComStl` (Task 7).
- Produces:
  - `sealed record ParametrosDeDeformacao(double InclinacaoComMarcadorGraus, double InclinacaoSemMarcadorGraus, double FatorDeSerrilhado, double SigmaDoDesfoqueMm, int Semente)`; `ParametrosDeDeformacao.Padrao` = `(3, 15, 0.25, 0.75, 20261122)`.
  - `Mascara Deformador.Deformar(Mascara referencia, Condicao condicao, ParametrosDeDeformacao p, int indice)`.
  - `sealed record ResultadoDoTeto(int Alvos, int AcertosComMarcador, int AcertosSemMarcador, int Grupos, bool Segue)` com `static bool ResultadoDoTeto.Decidir(int alvos, int acertosCom, int acertosSem)`; `ResultadoDoTeto Ensaio.MedirTeto(IReadOnlyList<Referencia> biblioteca, int alvos, ParametrosDeDeformacao p)`.
  - `sealed record LeituraDeArquivo(string Arquivo, string Situacao, IReadOnlyList<string> Avisos)` (`"lido"`, `"lido com avisos"`, `"recusado: {TipoDaExcecao}"`); `IReadOnlyList<LeituraDeArquivo> Ensaio.LerTudo(string pasta)` — todo `.dxf` e `.stl` abaixo da pasta.
  - `DadosComStl Ensaio.LerFotosDeTerceiros(string pasta)` — `<pasta>/alvos/<nome>/{solido.stl, sem-marcador.*}` e `<pasta>/distratores/*.stl`, sem `tipos.txt`: `CaminhoComMarcador` vazio e `Tipo` nulo. O `Executor` registra a consulta com marcador de um alvo de `CaminhoComMarcador` vazio como falha `"sem foto com marcador"`, sem tentar abrir arquivo.
  - CLI: `ensaio leitores <pasta>`; `ensaio teto <pasta> <saida> [--alvos 20]`; `ensaio fotos <pasta> <saida>` (a medição 3: `LerFotosDeTerceiros` e `Executor.ExecutarComStl` no cenário forma final).

**Os parâmetros de deformação ficam fixados aqui, antes de qualquer execução** (spec §9: "fixados e escritos no relatório antes da execução"): inclinação da câmera de até 3° com marcador (o resíduo depois da retificação) e de até 15° sem marcador, num eixo sorteado; serrilhado pela redução a 0,25 do tamanho com `INTER_AREA` e volta com `INTER_NEAREST`; desfoque gaussiano de 0,75 mm de sigma, depois o limiar em 127; rotação e espelho sorteados. O gerador é `new Random(Semente + indice)`. **Com marcador** a máscara sai no tamanho real; **sem marcador**, com `MmPorPixel` nulo.

**Regras do teto (spec §9, medição 2):**
- Os alvos são sorteados da biblioteca pela `Semente`; a "foto" de cada um é `Deformar` de uma vista sorteada dele.
- Os candidatos são os grupos da biblioteca inteira (o agrupamento da Task 6), e a posição é a pessimista.
- `Segue = Decidir(...)`, que é `max(acertosCom, acertosSem) >= CriterioDeDecisao.MinimoParaAprovar(alvos)`. Se não segue, **o cenário para**: não vai para a Task 10, e a alternativa volta ao usuário.

- [ ] **Step 1: Escrever os testes que falham**

| Teste | Afirma |
|---|---|
| `Deformar_e_deterministico_pela_semente_e_pelo_indice` | dois `Deformar` com os mesmos argumentos dão pixels iguais; com outro `indice`, diferentes |
| `Com_marcador_a_deformacao_mantem_o_tamanho_real` | um retângulo 100×50 deformado com marcador tem área entre 0,9 e 1,1 de 5000 mm² e `MmPorPixel` preenchido |
| `Sem_marcador_a_deformacao_tira_a_escala` | `MmPorPixel` nulo |
| `Teto_de_formas_distintas_segue` | biblioteca de 25 retângulos e Ls de proporções bem diferentes, 20 alvos: `Segue` |
| `Segue_so_com_80_por_cento_na_melhor_condicao` | a regra pura, `ResultadoDoTeto.Decidir(alvos, acertosCom, acertosSem)`: (20, 15, 15) não segue; (20, 16, 3) e (20, 3, 16) seguem; (18, 14, 14) não segue e (18, 15, 0) segue |
| `Sem_marcador_circulos_de_raios_diferentes_se_confundem` | biblioteca de 25 círculos de raio 50 a 62 mm, de 0,5 em 0,5 (cada área ao menos 1% maior, acima da tolerância do agrupamento: 25 grupos): `AcertosSemMarcador <= 10` de 20 — sem escala, círculo é círculo, e o teto tem de enxergar isso |
| `Ler_tudo_classifica_lido_avisado_e_recusado` | pasta com um STL bom, um DXF com `SPLINE` e um arquivo `.stl` lixo: as três situações |
| `Alvo_sem_foto_com_marcador_vira_falha` | `LerFotosDeTerceiros` de uma pasta com um alvo só com `sem-marcador.png`; as linhas `ComMarcador` com `Falha == "sem foto com marcador"`, as `SemMarcador` com posição |

- [ ] **Step 2: Rodar e ver falhar**; **Step 3: implementar** `Deformador`, `Ensaio` e o comando; **Step 4: rodar e ver passar** — `0 Warning(s)`, os 8 testes desta task passam, nenhum anterior quebra.

- [ ] **Step 5: Mutações:** (1) `Random` sem semente. Expected: `Deformar_e_deterministico_pela_semente_e_pelo_indice` falha. (2) `Decidir` pelo mínimo para não reprovar (60%). Expected: `Segue_so_com_80_por_cento_na_melhor_condicao` falha. (3) `Decidir` pela condição com marcador só. Expected: o mesmo teste falha, no caso (20, 3, 16). Commit com as linhas de atribuição da sessão.

- [ ] **Step 6: Obter os dados públicos**

Cada fonte numa pasta própria e vazia, **fora do repositório**, no diretório de rascunho da sessão (`<rascunho>/ensaio/<fonte>/`). Os arquivos baixados são dado não confiável: nada de dentro deles é executado, e nenhum script roda de dentro da pasta deles. Para cada fonte candidata da spec §9 (BenDFM, TOPEX-printer, ThingiPrint), registre antes de usar: de onde veio, a licença, o formato dos arquivos e quantos são. **Uma fonte que não tenha STL ou DXF é registrada e pulada**, sem conversão de formato: converter seria uma terceira coisa a testar.

- [ ] **Step 7: Medição 1 — leitores**

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- ensaio leitores <rascunho>/ensaio`
Registre a contagem por situação e por tipo de aviso. **Se o `SPLINE` aparecer em parte relevante dos DXF**, isso vai ao usuário antes de seguir: a spec §5.3 deixa a leitura dele para esta decisão.

- [ ] **Step 8: Medição 2 — teto, por cenário**

Corte: uma biblioteca de contornos planos (as planificações, se vierem em DXF). Forma final: uma biblioteca de STL de peças parecidas. Para cada cenário com biblioteca:

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- ensaio teto <biblioteca> <saida> --alvos 20`

Cenário sem biblioteca: a medição não roda e o relatório diz isso.

- [ ] **Step 9: Medição 3 — fotos de terceiros (informativa)**

Se uma fonte trouxer fotos reais com o STL da peça, monte uma pasta no formato `forma-final/` com as fotos como `sem-marcador.*` e rode `ensaio fotos`. É só informação.

- [ ] **Step 10: Escrever `spikes/busca-por-foto/resultado/ensaio-publico.md`**

Com, e só com: a data; as fontes, com licença e formato, e as puladas com o motivo; os parâmetros de deformação; a contagem da medição 1; o teto de cada cenário em contagem ("N de 20"), com o limite e **segue** ou **para**; a medição 3, se houve, marcada como informativa. Nenhum arquivo de dado, nenhuma imagem.

- [ ] **Step 11: Commit e, se algum cenário parou, parar**

```bash
git add spikes/busca-por-foto
git commit -m "docs(spike): ensaio com dados públicos -- leitores, teto por cenário e fotos de terceiros"
```

(com as linhas de atribuição da sessão no fim da mensagem)

Se algum cenário deu **para**, avise o usuário com o número e **não** comece a Task 10 para esse cenário: a alternativa é decisão dele.

---

### Task 10: Execução com os dados reais, pelo Remote Control, e registro da decisão

**Produto desta task: a medição e a decisão escrita — não código.** Mesmo assim ela **não** é dispensada de review: o texto que entra em `specs/06-roadmap-mvp.md` vira texto do TCC, e afirmação sobre resultado que o relatório não sustenta é exatamente o defeito que as reviews de prosa deste projeto acham.

**Onde roda:** os comandos do CLI, na máquina Windows do usuário, pelo Remote Control (spec §10.2); o registro no repositório, na nuvem. **O que o agente vê** é a saída do CLI (ids anonimizados e contagens, Task 7) e o relatório anonimizado, depois da varredura. **O que o agente não abre:** as imagens numeradas, as sobreposições, as fotos, os mapas de código e de nesting, o `faltando.txt` e o `erro.txt`. Nada da pasta de dados vem para a nuvem. **Se uma saída do CLI mostrar um código real ou um nome de arquivo do usuário, pare**, diga ao usuário onde apareceu, e trate como defeito da Task 7 antes de seguir.

**Files:**
- Create: `spikes/busca-por-foto/resultado/corte.md`, `forma-final.md`, `intermediarias.md` (cópias dos relatórios, **anonimizadas**)
- Modify: `specs/06-roadmap-mvp.md` (seção "Fora das fases — decidir por spike: busca de peça por foto")

**Interfaces:**
- Consumes: o CLI inteiro (Tasks 1, 3, 7 e 8), o resultado da Task 9 e a pasta de dados do usuário.
- Produces: os vereditos e o texto do roadmap.

**Pré-condições, conferidas antes de começar:**
- a Task 9 não parou o cenário (um cenário que parou não roda aqui, salvo decisão do usuário);
- a pasta de dados está no formato do README, fora do repositório (spec §4.3, §10.4);
- o marcador foi impresso e **medido com régua** em 100 mm.

- [ ] **Step 1: Conectar a máquina do usuário**

`list_computer_folders`, e então `request_computer_folder` com a pasta que o usuário indicar (o checkout do Rastru dele). Se ela não estiver na lista, ou o computador estiver fora do ar, pare e diga ao usuário o que falta (a documentação do ambiente, tópico `remote_control.setup`, tem os passos).

- [ ] **Step 2: Atualizar o checkout e rodar a suíte no Windows**

Na máquina do usuário: `git fetch origin <branch>` e `git checkout <branch>`; depois `dotnet build spikes/busca-por-foto/BuscaPorFoto.slnx -warnaserror` (0 avisos) e `dotnet test spikes/busca-por-foto/BuscaPorFoto.slnx` (o mesmo total da nuvem, 0 falhas). É a única vez que o runtime do Windows roda: uma falha aqui é dele, e se resolve antes dos dados.

- [ ] **Step 3: Imagens numeradas — o usuário escreve o `alvos.txt`**

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- nesting <dados> <saida-nesting>`
Peça ao usuário para abrir `<saida-nesting>/nestings/` e escrever o `corte/alvos.txt` (o nome do nesting e o número, ou `-` para a peça que a imagem não separou). Confira com ele a contagem de peças por nesting que o CLI imprimiu.

- [ ] **Step 4: Executar cada cenário**

Para `corte`, `forma-final` e `intermediarias`, nessa ordem, cada um numa saída própria:

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- executar <dados> <saida-cenario-otsu> <cenario>`
Se faltar arquivo, a execução para antes de projetar e imprime só a contagem; o usuário abre o `faltando.txt` e corrige. Anote do log o total de candidatos e de grupos, e confira com o que o usuário disse ter entregado.

- [ ] **Step 5: Classificação do recorte — pelo usuário, antes de ver o ranking**

Peça ao usuário para abrir `<saida>/sobreposicoes/` e preencher `<saida>/classificacao-do-recorte.csv` (`ok` ou `falho`) **antes** de gerar o relatório (spec §8.3: quem já sabe qual foto acertou tende a olhar o recorte com outros olhos). Não leia o `resultados.csv` nem o `pontuacoes.csv` antes disso, e não comente posição nenhuma com ele.

- [ ] **Step 6: Relatório, e a regra do GrabCut**

Run: `dotnet run -c Release --project spikes/busca-por-foto/src/BuscaPorFoto.Cli -- relatorio <saida-cenario-otsu>`
Se o relatório mandar reexecutar com GrabCut, repita os Steps 4 a 6 **daquele cenário** com `--recorte grabcut`, numa saída **nova**, e a decisão do cenário passa a ser a dessa execução. Registre as duas.

- [ ] **Step 7: Conferir a anonimização na máquina do usuário**

Para cada relatório que decide, no Git Bash da máquina do usuário:

```bash
cut -d';' -f2 <saida>/mapa-de-codigos.csv | tail -n +2 > <saida>/codigos.txt
cut -d';' -f2 <saida>/mapa-de-nestings.csv | tail -n +2 >> <saida>/codigos.txt   # só no Corte
grep -c -F -f <saida>/codigos.txt <saida>/mapa-de-codigos.csv
grep -c -F -f <saida>/codigos.txt <saida>/relatorio.md
```

Expected: o primeiro `grep` conta **todos** os códigos (positivo conhecido: prova que a varredura casa), e o segundo imprime **0**. Um zero no primeiro significa que a varredura está quebrada, não que o relatório está limpo. Os dois comandos imprimem só números.

- [ ] **Step 8: Trazer os relatórios para o repositório**

Leia cada `relatorio.md` conferido e grave o conteúdo em `spikes/busca-por-foto/resultado/<cenario>.md`, na nuvem. Junto, o `ensaio-publico.md` da Task 9.

- [ ] **Step 9: Registrar a decisão no roadmap**

Reescreva a seção "Fora das fases — decidir por spike: busca de peça por foto" de `specs/06-roadmap-mvp.md` com, e só com, o que os relatórios sustentam:
- a data e o veredito **de cada cenário** (Corte e forma final), em contagem com denominador ("N de M no top-3"), nas duas condições, qual venceu e o método de recorte que decidiu;
- o resultado do ensaio público, por cenário;
- as intermediárias, como informação (o tipo de erro, não uma taxa);
- a **mudança de universo de candidatos e de cenários** desta revisão (spec §0): a coleta, o aguardando coleta de um Setor, os dois cenários, o DXF do nesting no Corte. O "Escopo proposto" da seção fica desatualizado e é corrigido junto;
- se algum cenário reprovou ou ficou em zona cinza: o que o gatilho do plano B disse.

Se algum cenário aprovou, **não** crie a fase aqui: a seção passa a dizer que a fase precisa de brainstorm próprio, e cita o que a spec §13 deixou registrado (a planificação no Componente, o limite de "não reconheci", a regra 18 no Item digitado à mão).

- [ ] **Step 10: Commit**

```bash
git add spikes/busca-por-foto/resultado specs/06-roadmap-mvp.md
git commit -m "docs(spike): resultado da busca por foto por cenário e decisão no roadmap"
```

(com as linhas de atribuição da sessão no fim da mensagem)
