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
