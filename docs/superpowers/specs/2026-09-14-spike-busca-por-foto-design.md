# Spike — busca de peça por foto

**Data:** 2026-09-14 · **Status:** desenho aprovado no brainstorm, aguardando revisão da spec escrita
**Branch:** `spike-busca-por-foto`, nascida de `main` (`f2ee813`), isolada da Fase 2B

Esta spec desenha o **spike** que o roadmap (`specs/06-roadmap-mvp.md`, seção "Fora das fases —
decidir por spike: busca de peça por foto") exige antes de a busca por foto virar fase. Ela **não**
desenha a feature: nenhum endpoint, tela ou mudança de schema do Rastru está no escopo (§8).

## 1. Por que agora

O roadmap tratava a busca por foto como diferencial a medir. O usuário a reposicionou em 2026-09-14
como **condição de adoção**: a fábrica já tem uma aplicação de apontamento parecida e quase não a usa,
porque não existe identificação padrão das peças nem meio fácil de descobrir qual peça é.

O insumo que o operador tem na mão hoje é a **carteira** impressa: uma linha por Peça de Pedido,
com Pedido, código, descrição, quantidade, data, próximo setor e cor. O desenho da peça, em escala
reduzida, não serve para reconhecê-la, e o código não tem onde ser consultado.

O argumento do usuário é sobre **valor**; o critério do roadmap é sobre **viabilidade**. O primeiro
justifica rodar o spike agora, em paralelo com a Fase 2B; não substitui o número que o segundo mede.

## 2. O problema, recortado

Decisões do brainstorm, todas do usuário:

- **Direção da busca: da peça para a linha.** O operador tem a peça na mão e não sabe a que linha
  da carteira ela corresponde. (A direção inversa — do código para a peça — é outro problema, mais
  barato, e não é o que trava o chão de fábrica.)
- **O resultado mostra a peça e todos os Pedidos ativos a que ela está vinculada.** Um mesmo código
  aparece em Pedidos diferentes (na carteira anexada, ao menos um código está em três), e isso é aceitável:
  a geometria identifica a peça, não o Pedido, e quem escolhe o Pedido é o operador.
- **O universo de candidatos é o dos Componentes com Peça ou Item em Pedido ativo**, e não a lista
  do setor. Isso **diverge** do "Escopo proposto" do roadmap, que falava em reordenar a lista curta
  do setor — lista que é da Fase 3 e ainda não existe. A atualização do roadmap é o entregável 6
  (§7).
- **A foto é tirada no celular** — do operador ou corporativo —, pelo navegador. A exibição do
  sólido fica nos **totens** com internet que quase todos os setores têm; a viabilidade do
  visualizador no Android é a última coisa a verificar e está fora deste spike.
- **A foto é tanto de Peça quanto de Item, com incidência um pouco maior nos setores iniciais**
  (Corte, Dobra, Usinagem). O que está na mão ali é chapa, blank ou peça usinada — um **Item**, não
  a Peça acabada.

### 2.1 Tamanho do universo, medido

Medido em 2026-09-14 sobre a carteira anexada (29 páginas), por extração de texto (`pdftotext
-layout`) e contagem das linhas que começam por um número de Pedido de seis dígitos seguido de um
código terminado em `-C`: **142 de 147** linhas com Pedido casaram, com **105 códigos de Peça
distintos**. É aproximação — a extração por layout quebra algumas linhas —, e conta **só Peças**. Como
a foto é frequentemente de Item, o universo real é esse número multiplicado pelas partes de cada Peça:
**algumas centenas**. É isso que dimensiona os distratores (§3.1).

### 2.2 Consequência para o domínio, registrada e não resolvida

A regra 18 (`specs/01-dominio-e-regras-de-negocio.md`) obriga o sólido **só na Peça**. Para a busca
funcionar em Itens, o Item precisaria de um `Componente` com STL — e um Item ad-hoc, sem
`Componente`, ficaria invisível para ela. O schema já comporta (`Componente.ArquivoSolidoId` vale
para qualquer `Componente`); o que mudaria é a regra. **Decisão da fase que nascer, se o spike
aprovar — não deste spike.**

O usuário informou que a engenharia tem, com quase certeza, **um arquivo de peça por parte** no
SolidWorks, exportável em STL, além do arquivo de montagem — **confirmação pendente**. Se não se
confirmar, o spike não tem geometria de referência para Itens e precisa ser redesenhado antes de
começar.

## 3. Conjunto de dados

### 3.1 Dois tipos de STL

- **Partes-alvo**, as que são fotografadas: **~20 planas** (chapa cortada, dobrada, usinada — os
  setores iniciais) **mais ~5 soldadas**, para medir o caso difícil. O critério de decisão (§6)
  olha só as planas, por isso as soldadas vêm **além** das 20, não dentro delas. **Entram de
  propósito pares parecidos**: ao menos duas chapas retangulares de tamanhos diferentes e um par
  esquerdo/direito. Um conjunto só de peças fáceis aprova no escuro.
- **~150 a 200 distratores**: STL sem foto, das demais partes das mesmas montagens. Sem eles,
  acertar no top-3 entre 20 candidatos não diz nada sobre um universo de centenas (§2.1).

### 3.2 Protocolo de captura

Por parte-alvo, **duas fotos**:

- uma **sem marcador**;
- uma **com marcador** ArUco impresso (entregável 1), no mesmo plano da peça.

As duas com o celular, **na condição real**: onde a peça está (bancada, chão, palete), sem arrumar
fundo nem luz. De cima, aproximadamente perpendicular, com a peça inteira no quadro. Se o método só
funcionar com fundo preparado, isso é um resultado que o spike descobre; o contrário — preparar o
fundo e aprovar — esconderia o problema até o uso real.

O marcador existe para **medir** se é necessário, não para ser pressuposto: ele dá escala (separa
chapas de mesmo contorno e tamanhos diferentes) e corrige perspectiva (o operador nunca fotografa
perfeitamente de cima). Se o top-3 passar sem ele, a fábrica não precisa de marcador; se só passar
com ele, o custo de uso é um marcador plastificado por totem.

### 3.3 Organização em disco

```
<pasta-de-dados>/
  alvos/
    <codigo-real>/
      solido.stl
      sem-marcador.jpg
      com-marcador.jpg
  distratores/
    <qualquer-nome>.stl
```

### 3.4 Dados de cliente não entram no git

O repositório do Rastru é **público**. STL, fotos e imagens de sobreposição ficam numa pasta
**local, fora do repositório**, cujo caminho é argumento do programa. O ledger privado também não os
recebe, salvo decisão explícita do usuário sabendo que é conteúdo de cliente.

O relatório versionado usa **códigos anonimizados** (`P01`…`P20`): os códigos da carteira são part
numbers de cliente. A tabela `Pnn → código real` é gravada só na pasta de saída local.

## 4. Pipeline

### 4.1 Linguagem

**C#, console app com OpenCvSharp.** Esta máquina não tem Python, a VPS roda .NET, e se o spike
aprovar, as etapas STL→silhueta e foto→pontuação migram para `Infrastructure` sem reescrita de
linguagem. Custo aceito: o plano B (§6) é bem mais simples em Python e, se acionado, roda em
container.

**Não verificado:** que o pacote do OpenCvSharp para Windows traz o módulo de ArUco. É a primeira
coisa que o plano confere; se faltar, a alternativa é outro detector de marcador, decidida então.

### 4.2 Lado do STL — uma vez por arquivo

1. Lê STL binário ou ASCII, com parser próprio (o formato é trivial; sem dependência).
2. Acha a direção de **menor dispersão** dos vértices (PCA): numa chapa, é a normal da face maior.
   Projeta ortogonalmente ao longo dela e preenche os triângulos projetados. O resultado é a
   silhueta em **milímetros reais**, com os **furos passantes vazados** — nenhum triângulo cai dentro
   deles.
3. Peça sem face dominante (soldada) gera **6 silhuetas**: os três eixos principais, nos dois
   sentidos.

### 4.3 Lado da foto

1. **Com marcador:** detecta o ArUco, corrige a perspectiva por homografia e normaliza para
   1 px = 1 mm.
2. **Recorte da peça** — a etapa de maior risco: limiar automático (Otsu) com limpeza morfológica,
   ficando com o maior contorno. Se falhar em muitas fotos, GrabCut inicializado pelo centro do
   quadro.
3. Grava a **máscara sobreposta à foto**, uma imagem por foto, na pasta de saída local. É o que
   permite classificar cada erro como **"recorte falhou"** ou **"recortou bem e comparou errado"** —
   dois diagnósticos com consequências opostas (§6).

### 4.4 Comparação — dois pontuadores lado a lado

- **Momentos de Hu** sobre o contorno externo: linha de base, a que o roadmap cita. Invariante a
  rotação e escala, e cega aos furos.
- **Sobreposição de máscaras (IoU)**, com busca de rotação em passos de 5° e **também espelhada**.
  Não depende de PCA para alinhar, então formas quase simétricas não confundem o alinhamento. Os
  furos contam. **Sem marcador**, as duas máscaras são normalizadas para a mesma área; **com
  marcador**, cada uma fica no tamanho real, e diferença de tamanho derruba a pontuação por si.

**Consequência por construção:** como a comparação aceita espelho (a chapa pode estar virada), **um
par esquerdo/direito de chapa plana é indistinguível**. O relatório conta esses casos à parte. É
também por isso que o resultado é ranking e não resposta única — e, como o roadmap já registra, a
pontuação **não** deve ser exibida ao operador como "% de certeza".

### 4.5 Proibido: qualquer informação além da imagem

Nenhuma etapa de comparação pode ler nome de arquivo, nome de pasta, ordem de enumeração ou
metadado da foto. O código real só é consultado **depois** de o ranking estar pronto, para achar a
posição da peça certa. Um vazamento aqui produz aprovação falsa, que é o pior resultado possível
do spike — por isso é item nomeado da review (§7.2).

## 5. Métrica

Cada foto (≈ 25 partes × 2 condições = 50) é comparada com **todos** os STL — alvos e distratores,
≈ 175 a 225 candidatos — e registra-se a **posição** da peça certa no ranking.

O relatório cruza:

- condição: sem marcador / com marcador;
- pontuador: Hu / IoU;
- tipo de peça: plana / soldada;
- recorte: ok / falho (classificação visual das sobreposições);
- pares espelhados, à parte;
- tempo por consulta nesta máquina, só informativo.

**Resultados em contagem, não em percentual.** Com 20 partes cada uma vale 5 pontos percentuais, e
um percentual aparenta precisão que a amostra não tem: 16 de 20 tem intervalo de confiança de 95%
(Wilson) de ~58% a ~92%. O relatório escreve "16 de 20".

## 6. Critério de decisão — fixado antes de ver o resultado

Aplicado às **partes planas, no top-3, com o pontuador IoU, na melhor das duas condições de
captura**. Os momentos de Hu entram no relatório como linha de base, **não** na decisão: escolher o
melhor de dois pontuadores *e* de duas condições depois de ver os quatro números seriam quatro
tentativas de passar, e com 20 partes isso infla a chance de aprovar por acaso. A escolha entre as
duas condições fica, porque decidir se o marcador é necessário é justamente uma das perguntas do
spike.

| Top-3 | Decisão |
|---|---|
| **≥ 16 de 20** | **Aprova.** Vira fase no roadmap, com a condição de captura vencedora (com ou sem marcador) como requisito de uso. |
| **12 a 15** | **Zona cinza.** Decide pelo tipo de erro, abaixo. |
| **≤ 11** | **Reprova.** Registrado no roadmap com os números; a fila segue. |

Limites aprovados pelo usuário em 2026-09-14.

Se o número de partes planas fotografadas não for exatamente 20, os limites escalam na mesma
proporção — aprova com pelo menos 80% e reprova abaixo de 60%, os dois limites arredondados **para
cima** (com 18 planas: aprova com ≥ 15, zona cinza de 11 a 14, reprova com ≤ 10) — e o relatório
declara o denominador real.

**Plano B (embeddings de rede pré-treinada, DINOv2 ou similar)** é acionado **só** se a maioria dos
erros do melhor cenário for de **recorte** — é o que embeddings resolvem, por serem robustos a fundo.
Se o recorte sai bom e a comparação erra entre formas parecidas, o plano B **não** ajuda (é pior em
distinguir tamanho) e o resultado é reprovação. Acionar o plano B é decisão do usuário na hora, e
exige spec própria.

**Aproveitamento em qualquer resultado:** as silhuetas geradas dos STL servem de miniatura para uma
galeria visual das partes ativas, em que o operador procura com os olhos. Registrado como
consequência, não como escopo.

## 7. Entregáveis e processo

### 7.1 Entregáveis, em ordem

1. **PDF do marcador ArUco para impressão** — primeiro, para a captura começar enquanto o resto é
   escrito.
2. STL → silhueta.
3. Foto → recorte, com as imagens de sobreposição.
4. Pontuadores (Hu e IoU), ranking e relatório.
5. Execução com os dados reais.
6. Decisão registrada na seção "Fora das fases" de `specs/06-roadmap-mvp.md`, com os números e com
   a mudança de universo de candidatos (§2).

Os entregáveis 2 a 4 são desenvolvidos e testados com **dados sintéticos** gerados em código (uma
chapa com furo, girada, espelhada, em escala conhecida) e **não esperam** as fotos. Só o 5 espera.

### 7.2 Onde vive

- Branch `spike-busca-por-foto`, a partir de `main`, em worktree de caminho curto. A Fase 2B segue na
  sua branch no checkout principal; a Task 5 dela corre em paralelo.
- Pasta `spikes/busca-por-foto/`, com projeto de teste próprio, **fora do `Rastreamento.slnx`**: o
  build com `-warnaserror` e as suítes do produto não são tocados.

### 7.3 Gate de review

**Aplica-se, sem dispensa.** O produto do spike é um número, e o código é o que produz o número —
não cabe na classe "task cujo produto não é código" do `CLAUDE.md`. Execução por
`superpowers:subagent-driven-development`, fluxo inteiro. A review de cada task que toque ranking ou
comparação confere, nomeadamente:

- **vazamento** — nada além dos pixels influencia o ranking (§4.5);
- **a conta da posição** — inclusive empate de pontuação, que não pode favorecer a peça certa por
  ordem de enumeração.

Os artefatos do fluxo (brief, relatório) ficam no ledger com nome escopado pelo spike, para não
colidir com os da Fase 2B, que usam a mesma numeração de task.

### 7.4 Dependências do usuário

- Confirmar que existe STL por parte (§2.2).
- Exportar os STL de alvos e distratores.
- Imprimir o marcador e tirar as ≈ 50 fotos.

## 8. Fora do escopo

- Qualquer mudança no Rastru: endpoint, tela, schema, extensão da regra 18 aos Itens.
- Visualizador 3D no Android.
- Implementação do plano B (só com decisão e spec próprias).
- Desempenho de produção — o tempo por consulta é medido só como informação.
- A direção inversa da busca (do código para a peça).
