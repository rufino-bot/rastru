# Ensaio com dados públicos do spike de busca por foto

Data: 2026-11-23. Spec §9 de `docs/superpowers/specs/2026-09-14-spike-busca-por-foto-design.md`. Roda na nuvem,
**não aprova nada**: não testa a foto na fábrica. Nenhum dado público está neste repositório; os conjuntos são citados
só pelo nome.

## Fontes

| Fonte | Origem | Licença | Formatos | Quantidade | Situação |
|---|---|---|---|---|---|
| BenDFM (arXiv 2603.13102) | a página do artigo | não conferida | não conferidos | não conferida | **bloqueado pela rede**: o `arxiv.org` recusou a conexão (403 no túnel), então o link oficial dos dados não pôde ser lido. Uma busca por nome e pelo identificador do artigo no Hugging Face não achou conjunto. Não usada. |
| TOPEX-printer | Hugging Face `ritterdennis/topex-printer` | CC BY-NC 4.0 | PNG (renderizações e fotos reais), EXR (profundidade), CSV e JSON, dentro de um zip de 19,8 GB | 102 peças; 3.264 imagens renderizadas e 6.146 fotos reais | **pulada**: sem STL nem DXF. O conteúdo do zip foi listado só pelo diretório central dele (leitura parcial, sem baixar o arquivo): 52.811 entradas, 48.489 PNG, 3.264 EXR, 1.055 pastas, 2 CSV e 1 JSON. O CAD não vem no conjunto, só as imagens renderizadas dele. Baixado apenas o README. |
| ThingiPrint (arXiv 2603.07465) | Hugging Face `fanismathioulakis/thingiprint` (a etiqueta `arxiv:2603.07465` do conjunto o liga ao artigo; a página do artigo está bloqueada) | mista: fotos reais CC BY 4.0; cada malha mantém a licença original (62 CC BY-SA, 13 CC BY, 8 CC BY-NC-SA, 7 GPL, 5 CC BY-NC, 5 domínio público); renderizações seguem a da malha | 100 STL, 1.000 fotos reais em JPG (principal), 200 em JPG (auxiliar), 2.400 renderizações em PNG | 100 objetos | **usada**: as 100 malhas e 1 foto de cada um de 20 objetos (os 20 são o 1º, 6º, 11º… da lista ordenada pelo identificador). 71 MB. |

Total baixado: cerca de 71 MB (limite de 2 GB). Nenhuma fonte trouxe DXF.

## Parâmetros de deformação (fixados antes da execução)

Semente 20261122; o gerador de cada "foto" é `new Random(Semente + índice)`.

- Rotação e espelho sorteados.
- Inclinação da câmera num eixo sorteado: até 3° com marcador (o resíduo depois da retificação) e até 15° sem marcador.
- Serrilhado: redução a 0,25 do tamanho com `INTER_AREA` e volta com `INTER_NEAREST`.
- Desfoque gaussiano de sigma 0,75 mm, depois limiar em 127.
- Com marcador a máscara sai no tamanho real; sem marcador, sem escala.

## Medição 1: leitores

100 arquivos (todos de uma fonte só):

- DXF: 0. Nenhuma fonte trouxe DXF, então **a leitura de `SPLINE` não foi medida** e a decisão da §5.3 continua em aberto.
- STL: 100; 100 lidos, 0 com avisos, 0 recusados.

## Medição 2: teto

Acerto é o grupo certo entre os 3 primeiros, pela posição pessimista, em 20 alvos sorteados com a semente acima.

| Cenário | Biblioteca | Com marcador | Sem marcador | Limite | Veredito |
|---|---|---|---|---|---|
| Corte | **não rodou**: sem biblioteca de contornos planos (nenhum DXF; BenDFM bloqueada) | | | | sem veredito |
| Forma final | 100 STL da ThingiPrint, 99 grupos de geometria | 20 de 20 | 19 de 20 | 16 de 20 na melhor condição | **segue** |

Ressalvas do teto da forma final: a biblioteca tem 100 peças, não as 150 a 200 da spec, e são objetos variados, não
peças parecidas como as da fábrica. Por isso o número é um teto otimista do método, e a falta de um cenário de peças
parecidas não é coberta. Nenhum cenário deu **para**.

## Medição 3: fotos de terceiros (informativa)

20 fotos reais de objetos impressos em 3D (uma por objeto, sem marcador), contra os 100 STL, cenário forma final, recorte
Otsu. Não há foto com marcador: as 20 consultas com marcador ficam como falha "sem foto com marcador".

- Recorte vazio em 13 das 20 fotos. As 7 restantes foram pontuadas; a qualidade do recorte delas não foi classificada.
- Das 7 pontuadas, no top-3: 4 pelo IoU e 1 pelo Hu.

É só informação: o recorte automático padrão não serve para essas fotos como estão, o que não diz nada sobre as fotos da fábrica.
