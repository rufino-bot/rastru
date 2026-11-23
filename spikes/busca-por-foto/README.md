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

## Execução com os dados reais

A pasta de dados (fora do repositório) é uma por cenário, e o nome da pasta de cada alvo é o código real da
peça, que só aparece no `mapa-de-codigos.csv` da saída (spec §4.3):

```
<dados>/corte/nestings/*.dxf                 + alvos.txt (codigo; arquivo do nesting; número)
<dados>/corte/alvos/<codigo>/sem-marcador.jpg e com-marcador.jpg
<dados>/forma-final/alvos/<codigo>/{solido.stl,sem-marcador.jpg,com-marcador.jpg}   + tipos.txt
<dados>/forma-final/distratores/*.stl
<dados>/intermediarias/alvos/<codigo>/{solido.stl,sem-marcador.jpg,com-marcador.jpg}   (5 fotos, só informativo; os candidatos vêm também da forma-final)
```

Foto em HEIC não: exporte para JPG. Cada comando abaixo é `dotnet run -c Release --project
spikes/busca-por-foto/src/BuscaPorFoto.Cli -- …`.

**Corte** (nesting). Ordem: `nesting` → escrever o `alvos.txt` → `executar … corte` → classificar → `relatorio`.

1. `nesting <dados> <saida-nesting>` gera as imagens numeradas das peças de cada nesting (use `--ignorar-camada` e
   `--borda` se o desenho pedir).
2. Abra as imagens e escreva o `alvos.txt` com o número da peça de cada código.
3. `executar <dados> <saida-corte> corte` (Otsu por padrão).
4. Abra `<saida-corte>/sobreposicoes/` e preencha `<saida-corte>/classificacao-do-recorte.csv`: coluna `recorte`
   com `ok` (o verde cobre a peça e só ela, furos inclusos) ou `falho`.
5. `relatorio <saida-corte>` grava `<saida-corte>/relatorio.md` e imprime só o caminho e a palavra do veredito.

**Forma final** e **intermediárias**: o mesmo, sem o `nesting` e o `alvos.txt` — `executar <dados> <saida> forma-final`
(o `tipos.txt` diz o tipo de cada alvo), classificar, `relatorio <saida>`. As intermediárias não têm veredito: 5 fotos
não medem taxa de acerto.

**Regra do GrabCut.** Se o relatório de uma execução Otsu disser que mais de 1/3 dos recortes falhou, reexecute o mesmo
cenário com `--recorte grabcut` numa pasta de saída **nova**, classifique de novo e gere o relatório dela; a decisão
passa a ser a dessa execução. Cada execução é de um cenário.

Quem abre as imagens numeradas e as sobreposições é o usuário: são peças de cliente.
