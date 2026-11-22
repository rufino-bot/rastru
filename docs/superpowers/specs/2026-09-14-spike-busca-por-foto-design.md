# Spike — busca de peça por foto

**Data:** 2026-09-14 · **Revisada:** 2026-11-22 · **Status:** revisão escrita e conferida por revisor, aguardando aprovação do usuário
**Branch:** a da sessão de 2026-11-22, a partir da `main` em `722a09d` (merge da Fase 6A). A spec e o plano de
2026-09-14 nasceram na branch `spike-busca-por-foto`, a partir de `f2ee813`, e foram trazidos para cá sem mudança
antes desta revisão.

Esta spec desenha o **spike** que o roadmap (`specs/06-roadmap-mvp.md`, seção "Fora das fases — decidir por spike:
busca de peça por foto") exige antes de a busca por foto virar fase. Ela **não** desenha a feature: nenhum
endpoint, tela ou mudança de schema do Rastru está no escopo (§12). O que a fase precisaria, se o spike aprovar,
fica registrado à parte (§13).

## 0. O que mudou em 2026-11-22

A versão de 2026-09-14 foi escrita quando a Fila do Setor e as Tarefas ainda não existiam (são da Fase 3). Com o
roadmap chegando ao fim, o usuário retomou o spike, e a revisão, toda decidida com ele, mudou isto:

1. **O momento de uso é a coleta.** Quem usa é o Movimentador, em Tarefas, diante do grupo "Em {Setor}"; o mesmo
   conjunto é a seção "Aguardando coleta" da Fila do Setor (§2). A decisão 3 de setembro (candidatos = todo
   Componente em Pedido ativo) sai.
2. **A forma na mão nem sempre é a do STL** (§2.3). Na coleta do Corte a peça é plana, e o STL é o da peça pronta.
3. **Dois cenários, cada um com a sua referência e o seu critério** (§3): a coleta no Corte, contra o **DXF do
   nesting**; e a coleta da Dobra em diante, contra o **STL**, só com peças cuja forma já é a final. Mais umas 5
   fotos de **forma intermediária**, só informativas.
4. **O DXF é o do nesting da chapa inteira**, e separar as peças dele é um passo do spike (§5.3). As peças do
   mesmo nesting são os distratores do Corte, porque é assim que a pilha do Corte se forma.
5. **O critério é medido contra o conjunto inteiro de cada cenário**, não contra uma pilha de tamanho suposto (§8).
6. **Um ensaio com dados públicos** entre o código e as fotos reais (§9): pode reprovar o método cedo, nunca o
   aprova.
7. **As seis escolhas que o plano tinha feito sem passar pelo usuário** foram confirmadas, com três acréscimos
   (§8, §10).
8. **Onde roda:** o desenvolvimento e o ensaio público na nuvem (Linux); a execução com dados reais na máquina
   Windows do usuário, pelo agente via Remote Control (§10.2).
9. A peça dobrada passa a gerar três vistas, como a soldada (§5.2).
10. **A peça vai no centro da foto**, sem sair da pilha, e o recorte fica com o contorno do centro (§4.2, §5.4).
11. **O candidato é a peça, não o contorno**: cópias idênticas de um nesting viram um candidato só (§7).

## 1. Por que existe

O roadmap tratava a busca por foto como diferencial a medir. Em 2026-09-14 o usuário a reposicionou como
**condição de adoção**: a fábrica já tem uma aplicação de apontamento parecida e quase não a usa, porque não existe
identificação padrão das peças nem meio fácil de descobrir qual peça é.

O argumento do usuário é sobre **valor**; o critério do roadmap é sobre **viabilidade**. O primeiro justifica rodar
o spike; não substitui o número que o segundo mede.

**Por que agora (2026-11-22):** restam a Fase 6B e a 3C, e o usuário vê no fim do roadmap uma janela para uma
funcionalidade que provavelmente pede mudança de schema. O spike em si não muda o schema; ele decide se a fase que
mudaria vale a pena.

## 2. O problema, recortado

### 2.1 Quando a foto entra

Nas palavras do usuário (2026-11-22): *"normalmente é na hora de movimentar. Muita coisa fica pronta durante um
tempo, então mesmo dentro da fila do setor, é difícil identificar qual a peça certa a movimentar"*.

O Movimentador vai buscar o que está pronto num Setor. As peças terminadas esperam ali um bom tempo, então a pilha
acumula peças de vários Pedidos, muitas parecidas, nenhuma etiquetada. Na tela ele tem as Tarefas, com o grupo
"Em {Setor}" aberto, e precisa casar cada peça da pilha com uma linha dali. A foto **ordena essa lista** para pôr a
linha certa entre as primeiras.

Continuam valendo, de 2026-09-14:

- **Direção: da peça para a linha.** A direção inversa (do código para a peça) é outro problema.
- **A geometria identifica a peça, não o Pedido.** Um mesmo código pode estar em vários Pedidos; quem escolhe a
  linha é o Movimentador.
- **A foto é tirada no celular**, pelo navegador; o visualizador 3D no Android fica fora.
- **A pontuação é ranking, não confiança**, e nunca é exibida como "% de certeza" (roadmap).

### 2.2 Itens e sólidos

A versão de setembro temia que os Itens não tivessem STL (a regra 18 obriga o sólido só na Peça). Desde o import
do BOM, a exigência vale para a árvore inteira, menos o `Bruto` (regra 18 de `specs/01`, D3 da spec do import): num
Pedido importado, todo Item tem geometria de referência. Fica de fora o Item digitado à mão, sem Componente, e
estender a regra a ele é decisão da fase (§13), não deste spike.

O DXF do nesting existe (confirmado pelo usuário em 2026-11-22). Que exista um STL por parte continua a confirmar
pelo usuário para as peças escolhidas; o import já o exige para os Pedidos importados.

### 2.3 A forma na mão é a do Setor de onde se coleta

O STL é a forma **final** do nó. Ele só bate com o que está na mão quando, depois do Setor de onde se coleta,
nenhum passo do Roteiro muda mais a forma. O Corte é a exceção, porque tem o DXF.

**Exemplo 1 — suporte em L de chapa.** STL: dobrado em L, com dois furos de 10 mm na base, feitos na furadeira
depois da dobra. Roteiro: Corte → Dobra → Furação → Pintura.

| Coleta em | O que está na mão | Referência | Bate? |
|---|---|---|---|
| Corte | chapa plana, sem os furos | DXF do nesting | sim |
| Dobra | L dobrado, **sem** os furos | STL (L com furos) | não |
| Furação | L dobrado, com os furos | STL | sim |
| Pintura | igual à Furação | STL | sim |

**Exemplo 2 — conjunto soldado ("base do mancal")**, chapa de base e duas nervuras. O STL do pai já tem os furos
de fixação usinados. Roteiro do pai: Solda → Usinagem → Pintura. Na coleta da Solda a forma é intermediária; na
Usinagem e na Pintura, final. Os filhos saem do Corte e são comparados com o DXF.

**Como a forma intermediária erra**, por construção e sem medição com foto real: a diferença de poucos furos quase
não mexe na sobreposição (dois furos de 10 mm num L de 200 × 100 mm são menos de 1% da área). A peça certa fica com
pontuação alta, e **empata** com uma parecida sem os furos. A diferença grande (a chapa plana contra o STL dobrado)
derruba a pontuação. O usuário registrou que, na fábrica, as formas intermediárias variam bastante de uma peça para
outra.

## 3. Cenários

| Cenário | Coleta | O que está na mão | Referência | Decide? |
|---|---|---|---|---|
| **A — Corte** | no Corte | chapa cortada, plana | contorno do **DXF do nesting** | sim, com critério próprio |
| **B — forma final** | da Dobra em diante, num Setor depois do qual a forma não muda mais | dobrada, usinada ou soldada, na forma final | silhueta do **STL** | sim, com critério próprio |
| **Intermediárias** | num Setor depois do qual a forma ainda muda | forma intermediária | silhueta do **STL** | **não** — só informativo |

Cada cenário aprova ou reprova sozinho, e a fase pode nascer só com o que passou (§8.4).

**5 fotos não medem taxa de acerto.** Cada uma vale 20 pontos percentuais. As intermediárias mostram **se** o erro
é de empate ou de pontuação baixa, não com que frequência acontece. Se a fase precisar de um número para elas,
isso pede um terceiro cenário com umas 20 peças, fora deste spike.

## 4. Conjunto de dados

### 4.1 Alvos e distratores

- **Cenário A: ~20 peças-alvo**, chapas cortadas tiradas de nestings reais. **Distratores: as outras peças dos
  mesmos nestings**, até somar ~150 a 200 contornos (mais nestings entram só para completar o número). A pilha do
  Corte se forma assim: as peças cortadas na mesma chapa esperam coleta juntas.
- **Cenário B: ~20 peças-alvo** na forma final, das que o Movimentador coleta depois do Corte: dobradas, usinadas e
  soldadas, na proporção em que aparecem na fábrica. **Distratores: ~150 a 200 STL** das outras partes das mesmas
  montagens, sem foto.
- **Intermediárias: ~5 peças**, cada uma fotografada numa coleta em que a forma ainda muda (o suporte na Dobra, a
  base na Solda), com o STL dela.

**Em A e em B entram de propósito pares parecidos**: ao menos duas chapas retangulares de tamanhos diferentes e um
par esquerdo/direito. Um conjunto só de peças fáceis aprova no escuro.

### 4.2 Protocolo de captura

Por peça, **duas fotos**: uma **sem marcador** e uma **com o marcador** ArUco impresso, no mesmo plano da peça. As
duas no celular, **na condição real da coleta**: onde a peça espera (a pilha do Corte, a bancada, o palete), sem
arrumar fundo nem luz. De cima, aproximadamente perpendicular, com a peça inteira no quadro, **no centro e
ocupando a maior parte dele**. A peça não precisa sair da pilha: outras chapas podem aparecer nas bordas (decisão do
usuário, 2026-11-22). São ≈ (20 + 20 + 5) × 2
= **90 fotos**.

O marcador existe para **medir** se é necessário: dá escala (separa chapas de mesmo contorno e tamanhos diferentes)
e corrige perspectiva. Se o top-3 passar sem ele, a fábrica não precisa de marcador; se só passar com ele, o custo de
uso é um marcador plastificado por totem. O marcador é impresso e **medido com régua**, e a medida entra na
execução.

### 4.3 Organização em disco

```
<pasta-de-dados>/
  corte/
    nestings/<qualquer-nome>.dxf
    alvos.txt                       # uma linha por alvo: <codigo> <nesting> <número do contorno>
    alvos/<codigo>/sem-marcador.jpg
    alvos/<codigo>/com-marcador.jpg
  forma-final/
    alvos/<codigo>/solido.stl, sem-marcador.jpg, com-marcador.jpg
    distratores/<qualquer-nome>.stl
  intermediarias/
    alvos/<codigo>/solido.stl, sem-marcador.jpg, com-marcador.jpg
```

O número do contorno vem da imagem numerada que o spike gera de cada nesting (§5.3). O usuário a abre e escreve o
`alvos.txt`.

### 4.4 Dados de cliente não entram no git

O repositório do Rastru é **público**. STL, DXF, fotos, imagens numeradas e sobreposições ficam numa pasta
**local, fora do repositório**, cujo caminho é argumento do programa. O ledger privado também não os recebe, salvo
decisão explícita do usuário sabendo que é conteúdo de cliente. Os dados reais **não sobem para a nuvem**: a
execução com eles roda na máquina do usuário (§10.2).

O relatório versionado usa **códigos anonimizados** (`A01`…, `B01`…, `I01`…, por cenário). A tabela de código
anonimizado → código real é gravada só na pasta de saída local.

## 5. Pipeline

### 5.1 Linguagem e plataforma

**C#, console app com OpenCvSharp**, como em setembro: a VPS roda .NET, e se o spike aprovar, as etapas migram para
`Infrastructure` sem reescrita de linguagem.

**Roda em Linux e em Windows** (§10.2). A versão do plano (`4.13.0.20260627`) foi medida só no Windows, com
`OpenCvSharp4.runtime.win`. **Não verificado:** que exista o runtime para Linux nessa versão e que ele traga o
módulo de ArUco. É a primeira coisa que o plano confere; se faltar, a escolha de versão ou de detector de marcador
volta ao usuário.

### 5.2 Referência do cenário B e das intermediárias — STL → silhuetas

1. Lê STL binário ou ASCII, com parser próprio.
2. Acha os eixos principais dos vértices (PCA).
3. **Gera as três vistas principais**, uma por eixo, projetando ortogonalmente e preenchendo os triângulos
   projetados. A silhueta sai em **milímetros reais**, com os furos passantes vazados.
4. **A pontuação de uma peça contra uma foto é a da melhor das três vistas.**

**Mudança em relação a setembro:** lá, só a peça sem face dominante (a soldada) ganhava três vistas, e a chapa
ganhava uma. No cenário B toda peça é comparada pelas três, porque a dobrada pode estar apoiada na base ou nas abas
(uma peça em U), e uma vista só erraria a pose. A chapa plana pura não é mais caso de STL: ela é o cenário A, pelo
DXF. A vista do lado oposto de um eixo não entra: é o espelho da primeira, e a comparação já testa o espelho
(decisão 2 do plano, confirmada).

### 5.3 Referência do cenário A — DXF do nesting → silhuetas

1. **Lê o DXF**: `LINE`, `ARC`, `CIRCLE`, `LWPOLYLINE` e `POLYLINE` (com o arco do *bulge*). A unidade vem do
   `$INSUNITS` do cabeçalho; sem ele, assume milímetro e **avisa**.
2. **Entidade que o leitor não sabe ler é avisada, nunca descartada em silêncio**: um contorno perdido viraria uma
   silhueta errada sem ninguém notar. `SPLINE` está nesse caso até o ensaio público mostrar que precisa ser lida
   (§9).
3. **Monta os contornos fechados**, ligando as pontas que coincidem dentro de uma tolerância. Linha que não fecha
   contorno (linha de dobra, cota) fica de fora e é contada no aviso. Texto (`TEXT`, `MTEXT`) é ignorado.
4. **Separa as peças pela profundidade de aninhamento.** Profundidade 0 é a borda de uma peça; 1, um furo dela; 2,
   uma peça pequena posta dentro do furo de outra (comum em nesting); 3, um furo dessa peça; e assim por diante. Um
   contorno que envolve todos os outros é tratado como **a borda da chapa** e sai.
5. **Grava uma imagem numerada de cada nesting**, com cada peça contornada e o número dela, na pasta de saída local,
   para o usuário escrever o `alvos.txt` (§4.3).
6. **Corte em linha comum** (duas peças dividindo uma aresta) não forma contornos fechados separados. O spike
   **avisa** e não junta duas peças numa só. A peça que não sai separada é falha da referência e sai da conta (§8.3).

**Risco conhecido:** uma gravação ou marcação fechada dentro da peça (um círculo marcado, não cortado) seria lida
como furo. Se as camadas do DXF permitirem distinguir corte de marcação, o leitor usa a camada; senão, a imagem
numerada mostra os furos de cada peça e o usuário vê. Qual das duas vale é do implementador, a partir do primeiro
nesting real.

O contorno do DXF já é a vista de cima da chapa: não há PCA nem escolha de vista no cenário A.

### 5.4 Lado da foto

1. **Com marcador:** detecta o ArUco, corrige a perspectiva por homografia e normaliza para 1 px = 1 mm.
2. **Recorte da peça**, a etapa de maior risco: limiar automático (Otsu) com limpeza morfológica, ficando com o
   **contorno que contém o centro do quadro**, e não com o maior. Numa pilha, o maior pode ser a chapa de baixo. Se
   nenhum contorno contém o centro, o recorte é falho. Se mais de 1/3 dos recortes de um cenário sair falho, aquele cenário passa ao GrabCut inicializado pelo
   centro do quadro (§8.3). A chapa cortada sobre uma pilha de chapas (metal sobre metal) é o pior caso esperado.
3. Grava a **máscara sobreposta à foto**, uma imagem por foto, na pasta de saída local. É o que permite classificar
   cada erro como **"recorte falhou"** ou **"recortou bem e comparou errado"**.

### 5.5 Comparação — dois pontuadores lado a lado

- **Momentos de Hu** sobre o contorno externo: linha de base. Invariante a rotação e escala, cego aos furos.
- **Sobreposição de máscaras (IoU)**, com busca de rotação em passos de 5° e **também espelhada**. Os furos contam.
  **Sem marcador**, as duas máscaras são normalizadas para a mesma área; **com marcador**, cada uma fica no tamanho
  real, e diferença de tamanho derruba a pontuação por si.

**Consequência por construção:** como a comparação aceita espelho, um par esquerdo/direito de chapa plana é
indistinguível. O relatório conta esses casos à parte.

### 5.6 Proibido: qualquer informação além da imagem

Nenhuma etapa de comparação pode ler nome de arquivo, nome de pasta, número de contorno, ordem de enumeração ou
metadado da foto. O código real só é consultado **depois** de o ranking estar pronto, para achar a posição da peça
certa. Um vazamento aqui produz aprovação falsa, o pior resultado possível do spike, e é item nomeado da review
(§10.3). No cenário A isso inclui o nesting de onde a peça veio: ele só entra para montar o conjunto de candidatos.

## 6. Abordagens consideradas

**Referência para a coleta do Corte** (a peça está plana e o STL é o da peça pronta):

| Abordagem | Troca | |
|---|---|---|
| **Contorno do DXF do nesting** | contorno exato da chapa como sai da máquina; custa um leitor de DXF e a separação das peças | **escolhida** — a fábrica tem o DXF |
| Planificação exportada pela engenharia (um DXF por peça) | mais limpa que o nesting, mas o usuário pediu para considerar o nesting | coberta: um DXF de uma peça só é um nesting de uma peça |
| Aprender pelo uso (a foto confirmada vira referência) | serve à peça de catálogo, não à de uma vez só; o spike não a mede | registrada para a fase (§13) |
| Tirar o Corte da busca (filtro de Material e miniatura) | honesta, mas perde o Setor de maior uso | saída se o cenário A reprovar |
| Planificar o STL no código | projeto próprio, com erro do tamanho da diferença a medir | descartada |

**Como dividir as fotos:**

| Abordagem | Troca | |
|---|---|---|
| **Dois cenários, cada um com critério próprio** (~90 fotos) | mais fotos; responde os dois casos da coleta | **escolhida** |
| O Corte decide sozinho, o resto informativo (~50 fotos) | menos fotos; deixa a coleta depois do Corte sem número | saída se não der para juntar 20 peças de B |
| Uma amostra única, misturada | um bom resultado num cenário esconde um ruim no outro | descartada |

**Validar antes das fotos reais:**

| Abordagem | Troca | |
|---|---|---|
| **Ensaio com dados públicos** (§9) | uma etapa a mais; pode parar o spike antes das 90 fotos | **escolhida**, por proposta do usuário |
| Ir direto às fotos | menos trabalho, mas só se descobre um método sem teto depois de fotografar | descartada |

**O método de comparação** continua o de setembro: silhueta clássica, com embeddings de rede pré-treinada como
plano B. LLM com visão segue descartado, por custo e por mandar desenho de cliente para terceiro.

## 7. Métrica

Cada foto é comparada com **todos** os candidatos do seu cenário, e registra-se a **posição** da peça certa no
ranking. O ranking é **pessimista**: empate de pontuação conta contra a peça certa.

**O candidato é a peça, não o contorno** (escolha do controlador, na revisão de 2026-11-22). Um nesting costuma
repetir a mesma peça várias vezes na chapa, e a lista de Tarefas mostra uma linha por peça, não por cópia. Antes do
ranking, as referências **geometricamente idênticas** — tamanho real, sobreposição acima de um limite alto que o
implementador fixa e escreve no relatório antes da execução — se juntam num candidato só, e a pontuação dele é a da
melhor referência do grupo. Sem isso, um alvo com cinco cópias na chapa cairia fora do top-3 por construção, e uma
peça errada repetida empurraria a certa várias posições para baixo. O agrupamento olha só as referências, nunca a
foto nem o código, então não é vazamento (§5.6). Acerto é a foto cair no grupo que contém a referência do alvo.
Duas peças de códigos **diferentes** e geometria idêntica (a mesma nervura cadastrada duas vezes) caem no mesmo
grupo; o relatório as lista à parte, como o par espelhado, porque na tela elas seriam duas linhas que a foto não
separa.

**O par esquerdo/direito fica no denominador.** "Contado à parte" quer dizer listado no relatório, não excluído da
decisão. Com o ranking pessimista, a peça certa empata com o espelho e fica em segundo, ainda dentro do top-3.

O relatório cruza, por cenário:

- condição: sem marcador / com marcador;
- pontuador: Hu / IoU;
- tipo de peça (no B: dobrada, usinada, soldada);
- recorte: ok / falho (classificação do usuário);
- pares espelhados, à parte;
- tempo por consulta, só informativo.

E acrescenta:

- **para cada foto, a pontuação da peça certa e a da melhor errada.** É o dado de que a fase precisaria para
  calibrar um limite de "não reconheci" (§13); o spike não fixa limite nenhum;
- **o top-3 em pilhas menores**, simuladas com subconjuntos dos mesmos distratores, **só como informação**: a
  decisão usa o conjunto inteiro (§8);
- **as intermediárias, uma linha por foto**, com a posição e as duas pontuações, comparadas contra os candidatos do
  cenário B acrescidos do próprio STL. Elas não mudam o conjunto de candidatos do B;
- as peças do cenário A que não saíram separadas do nesting, à parte.

**Resultados em contagem, não em percentual.** 16 de 20 tem intervalo de confiança de 95% (Wilson) de ~58% a ~92%.
O relatório escreve "16 de 20".

## 8. Critério de decisão — fixado antes de ver o resultado

### 8.1 A regra, em cada cenário que decide (A e B)

Aplicada às **peças-alvo do cenário, no top-3, com o pontuador IoU, na melhor das duas condições de captura**, contra
o **conjunto inteiro** de candidatos do cenário. Hu entra no relatório como linha de base, **não** na decisão.

| Top-3 | Decisão |
|---|---|
| **≥ 16 de 20** | **Aprova.** O cenário vira parte da fase, com a condição de captura vencedora como requisito de uso. |
| **12 a 15** | **Zona cinza.** Decide pelo tipo de erro (§8.3). |
| **≤ 11** | **Reprova.** Registrado no roadmap com os números. |

Se o número de alvos não for exatamente 20, os limites escalam: aprova com pelo menos 80% e reprova abaixo de 60%,
os dois arredondados **para cima** (com 18: aprova com ≥ 15, zona cinza de 11 a 14, reprova com ≤ 10). O relatório
declara o denominador real.

**Contra o conjunto inteiro, e não contra uma pilha:** o tamanho de uma pilha real não é conhecido, e 150 a 200
candidatos é mais difícil que qualquer pilha. Se passar assim, passa na pilha.

**No cenário B, a decisão conta todos os alvos, de qualquer tipo** (dobrados, usinados e soldados). Em setembro
as soldadas ficavam fora da decisão. Agora o cenário B é definido pelo momento da coleta, e a soldada na forma
final é parte dele.

### 8.2 Desempates e condições

- **Empate entre "com marcador" e "sem marcador" vai para "sem marcador"**, em cada cenário (decisão 3 do plano,
  confirmada): com o mesmo acerto, vence a condição que não custa um marcador por totem.

### 8.3 Recorte e plano B

- **GrabCut:** se mais de 1/3 dos recortes de um cenário for classificado como falho, aquele cenário passa ao GrabCut
  (decisão 1 do plano, confirmada, agora **por cenário**).
- **O usuário classifica os recortes antes de ver o ranking.** Quem já sabe qual foto acertou tende a olhar o
  recorte com outros olhos. O controlador não abre as sobreposições sem autorização explícita, porque são fotos de
  peça de cliente (decisão 5 do plano, confirmada).
- **A zona cinza não aprova sozinha**, como em setembro: ela decide entre plano B e reprovação. Erro de espelho ou
  de geometria idêntica não pesa para nenhum lado, porque já está dentro do top-3 (§7).
- **Plano B (embeddings de rede pré-treinada, DINOv2 ou similar)** é acionado **só** se a maioria dos erros de um
  cenário em zona cinza (A ou B), na melhor condição de captura dele, for de **recorte**. Cada cenário é avaliado
  sozinho. "Recorte vazio" conta como erro de recorte; "marcador não detectado" não conta
  (decisão 4 do plano, confirmada). Se o recorte sai bom e a comparação erra entre formas parecidas, o plano B não
  ajuda e o resultado é reprovação. Acionar o plano B é decisão do usuário na hora, e exige spec própria.
- **A peça que o spike não conseguiu separar do nesting** é falha da referência, não da foto: sai do denominador e
  dos acertos, e é relatada à parte. Se isso tirar alvos a ponto de mudar o denominador, os limites escalam (§8.1).

### 8.4 Um cenário pode passar sem o outro

- **A aprova e B reprova:** a fase nasce só para a coleta no Corte; os outros Setores ficam com a miniatura e o
  filtro.
- **B aprova e A reprova:** o contrário, e o Corte fica com o filtro de Material e a miniatura.
- **Não deu para juntar ~20 alvos de B:** A decide sozinho, e o que houver de B entra como amostra informativa.

## 9. Ensaio com dados públicos (antes das fotos reais)

Proposta do usuário (2026-11-22). Roda depois do código e antes da execução com dados reais. **Pode reprovar o
método cedo, e não pode aprová-lo**: ele não testa a foto na fábrica, que é o maior risco.

**Fontes candidatas**, encontradas por busca em 2026-11-22, **sem download, formato ou licença conferidos**:

- **BenDFM** (arXiv 2603.13102): 20 mil peças de chapa dobrada geradas por programa, com a planificação guardada
  junto. Serve de referência e de distrator para os dois cenários, se o formato for legível.
- **TOPEX-printer** (Hugging Face, `ritterdennis/topex-printer`): 102 peças de máquina, com CAD e fotos reais;
  licença CC BY-NC 4.0.
- **ThingiPrint** (arXiv 2603.07465): 100 modelos com fotos das peças impressas em 3D.
- DXF de nesting público não apareceu. Um nesting montado com peças públicas testa a separação das peças, mas não
  os vícios de um nesting real.

**As três medições:**

1. **Robustez dos leitores.** DXF e STL de origens variadas passam pelos leitores; o resultado é a lista do que foi
   lido, avisado e recusado. É o que decide se o `SPLINE` precisa ser lido (§5.3).
2. **O teto do método**, por cenário. Para ~20 referências sorteadas de uma biblioteca de peças parecidas, a "foto"
   é a silhueta da própria referência, **deformada de propósito** (perspectiva, borda serrilhada, desfoque), e é
   comparada contra ~150 a 200 peças da mesma biblioteca, nas duas condições (tamanho real e área normalizada). **Se
   o top-3 da melhor condição ficar abaixo de 80% (⌈0,8·n⌉), o cenário para ali**: não segue para as fotos reais, e
   a alternativa volta ao usuário. Os parâmetros de deformação são do implementador, **fixados e escritos no
   relatório antes da execução**.
3. **O recorte em foto real de terceiros** (TOPEX, ThingiPrint): exercita o recorte e a comparação numa foto de
   verdade, em outra condição. **Só informativo.**

Se nenhuma fonte servir para um cenário, a medição 2 daquele cenário não roda e o relatório diz isso. A falta dela
não reprova nem aprova nada.

**Os dados públicos também não entram no git** (a licença não comercial do TOPEX e o repositório público não
combinam). Eles ficam na pasta local da nuvem, e o relatório cita os conjuntos pelo nome. Por não serem dado de
cliente, o ensaio roda na nuvem.

## 10. Entregáveis e processo

### 10.1 Entregáveis, em ordem

1. **PDF do marcador ArUco para impressão**, mais a conferência do OpenCvSharp no Linux (§5.1). Primeiro, para a
   captura começar enquanto o resto é escrito.
2. STL → silhuetas (três vistas).
3. **DXF do nesting → silhuetas**, com a separação das peças e a imagem numerada. Novo.
4. Foto → retificação pelo marcador.
5. Foto → recorte, com as imagens de sobreposição.
6. Pontuadores (Hu e IoU) e ranking.
7. Execução sobre a pasta de dados e relatório, por cenário, com a decisão.
8. **Ensaio com dados públicos** (§9). Novo.
9. Execução com os dados reais, na máquina do usuário, e a decisão registrada na seção "Fora das fases" de
   `specs/06-roadmap-mvp.md`, com os números e com a mudança de universo e de cenários (§0).

Os entregáveis 2 a 7 são desenvolvidos e testados com **dados sintéticos** gerados em código (uma chapa com furo,
girada, espelhada, em escala conhecida; um nesting com peça dentro de furo e borda de chapa) e **não esperam** dado
nenhum. O 8 espera só o download; o 9, as fotos.

### 10.2 Onde vive e onde roda

- Pasta `spikes/busca-por-foto/`, com projeto de teste próprio, **fora do `Rastreamento.slnx`**: o build com
  `-warnaserror` e as suítes do produto não são tocados.
- **Entregáveis 1 a 8 na nuvem (Linux).** O 9, **na máquina Windows do usuário**, porque STL, DXF e fotos de
  cliente não sobem para a nuvem (decisão 6 do plano, revista com o usuário em 2026-11-22: era "só Windows"). O
  projeto restaura o runtime do OpenCvSharp do sistema em que roda.
- **Quem roda o 9 é o agente, pelo Remote Control** (decisão do usuário, 2026-11-22), seguindo um roteiro escrito no
  plano, com o usuário acompanhando. O agente vê a saída do programa, que só traz códigos anonimizados. Ele **não**
  abre as sobreposições, as imagens numeradas, as fotos nem a tabela de códigos reais, e não copia nada da pasta de
  dados para a nuvem. Classificar os recortes e escrever o `alvos.txt` é do usuário. O relatório anonimizado vai para
  o repositório pela mesma sessão, depois de uma varredura contra a tabela de códigos reais (positivo conhecido
  antes), feita **na máquina do usuário**.
- A branch é a da sessão. A antiga `spike-busca-por-foto` fica como está, só com a spec e o plano de setembro.

### 10.3 Gate de review

**Aplica-se, sem dispensa.** O produto do spike é um número, e o código é o que produz o número. Execução por
`superpowers:subagent-driven-development`, fluxo inteiro. A review de cada task que toque ranking, comparação ou
separação do nesting confere, nomeadamente:

- **vazamento** — nada além dos pixels influencia o ranking (§5.6);
- **a conta da posição** — inclusive empate, que não pode favorecer a peça certa por ordem de enumeração;
- **o aviso, não o silêncio** — entidade de DXF desconhecida, contorno aberto e peça não separada aparecem no
  relatório.

Os artefatos do fluxo ficam no ledger com nome escopado pelo spike (`spike-foto-task-N-…`).

### 10.4 Dependências do usuário

- Confirmar que existe STL para as peças escolhidas (§2.2).
- Separar os DXF de nesting do cenário A e os STL do cenário B, das intermediárias e dos distratores.
- Imprimir o marcador e medi-lo com régua.
- Tirar as ≈ 90 fotos na condição real (§4.2).
- Escrever o `alvos.txt` a partir das imagens numeradas.
- Classificar os recortes, antes de ver o ranking.
- Conectar a máquina Windows à sessão pelo Remote Control para o entregável 9.

## 11. Limites que sobrevivem, mesmo dando certo

- Peça espelhada (suporte esquerdo/direito) tem a mesma silhueta.
- Sem marcador, a escala é ambígua entre variantes proporcionais.
- A forma intermediária empata com a parecida (§2.3).
- A geometria não identifica o Pedido.
- Item digitado à mão, sem Componente, não tem referência (§2.2).

## 12. Fora do escopo

- Qualquer mudança no Rastru: endpoint, tela, schema, extensão da regra 18.
- Um limite de "não reconheci": o spike mede as pontuações, não o fixa.
- Aprender a referência pela foto confirmada.
- O terceiro cenário, das intermediárias com critério.
- Visualizador 3D no Android.
- Implementação do plano B (só com decisão e spec próprias).
- Desempenho de produção — o tempo por consulta é só informação.
- A direção inversa da busca (do código para a peça).

## 13. Para a fase, se o spike aprovar — registrado, não decidido

- **Um segundo arquivo por Componente, a planificação (DXF)**, para o cenário A: a mudança de schema que motiva
  fazer a fase nesta janela. Separar a peça de um nesting, ou receber o DXF de uma peça só, é decisão da fase.
- **Limite parametrizável de "não reconheci"**, calibrado por cenário com as pontuações do §7 e nunca exibido como
  porcentagem. Origem: proposta do usuário de um limite mais tolerante para as intermediárias (2026-11-22). Ele
  serve para a tela avisar "não reconheci, procure na lista", não para aceitar a peça sozinho.
- **A foto confirmada como referência** do passo em que a forma é intermediária.
- **Estender a regra 18 ao Item digitado à mão**, ou aceitar que ele fica fora da busca.
- A tela: o ranking sobre o grupo "Em {Setor}" das Tarefas e sobre a seção "Aguardando coleta" da Fila.
- **Aproveitamento em qualquer resultado:** as silhuetas e a miniatura isométrica (rufino-bot/rastru#39) servem a uma
  lista em que o Movimentador procura a peça com os olhos, que já está prevista como "miniatura no chão de fábrica"
  em `specs/06-roadmap-mvp.md`.
