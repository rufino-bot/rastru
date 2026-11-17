# Miniatura isométrica do sólido — desenho

Data: 2026-11-17. Branch: `claude/cool-cray-ijm0ib`, a mesma da spec do Panorama
(`docs/superpowers/specs/2026-11-17-panorama-design.md`), a partir da `main` em `7d41e09`. O desenho foi conversado com
o usuário em chat na mesma sessão de nuvem do Panorama. Os três estilos de imagem foram mostrados numa página privada do
usuário (`https://claude.ai/artifact/QJnhQ7ERdEmnEskdsC3iWY`), desenhados com o three.js a partir de peças de exemplo.
As decisões do usuário são D1 a D5 (seção 2.1). As chamadas do desenho, que o usuário confere ao ler, são C1 a C15
(seção 2.2). O builder check (um revisor lendo a spec como quem vai planejar, conferindo as premissas contra o código)
não achou premissa falsa e levantou quatro pontos, todos resolvidos como chamada do desenho, sem decisão nova do usuário:
a corrida do "Gerar miniatura" com a troca do sólido (C10 e C15), o significado da versão no import (C12), quem mede o
tempo no Android (C14) e o reimport (C5). O documento escrito foi **aprovado pelo usuário em 2026-11-17**.

**Ponto de partida e ordem.** A miniatura nasceu da coluna "Desenho" do Panorama. O usuário decidiu que ela é um
trabalho **separado**, que **executa antes do Panorama** (D6 e D14 da spec do Panorama), e as duas antes da 3C. O plano do
Panorama só é escrito depois de esta spec ser aprovada, porque usa o contrato da seção 5.

## 1. O que se quer, e o que conta como pronto

**O que se quer.** Uma imagem pequena da Peça, em vista isométrica, gerada a partir do sólido 3D (STL) que o Componente
já tem, para reconhecer a Peça de relance. Ela substitui o desenho que a planilha da Gestão mostra hoje. O primeiro
consumidor é o Panorama; o segundo é o detalhe do Componente, onde quem subiu o sólido vê na hora se a imagem ficou boa.

**Pronto é:**

- subir ou trocar o sólido de um Componente (no cadastro ou no import do BOM) grava também a miniatura, sem passo extra;
- o detalhe do Componente mostra a miniatura, e o PCP e o Administrador têm um "Gerar miniatura" para o Componente que
  tem sólido e não tem miniatura;
- existe uma rota que serve a miniatura e um campo nos DTOs que diz se ela existe e qual a versão, para o Panorama usar.

### 1.1 Não objetivos

- **A miniatura no chão de fábrica** (Fila, Tarefas, Separação, Expedição) e no cadastro (lista de Componentes, árvore do
  Agrupamento). As telas do chão de fábrica ficam para **depois do Panorama**, como item próprio (D2); as do cadastro
  não foram pedidas.
- **Gerar no servidor.** O .NET não tem renderizador de STL que sirva, e a saída seria um Chromium sem tela ou um
  rasterizador próprio na VPS: uma dependência nova no deploy por causa de uma miniatura.
- **A foto de referência** (`Componente.ArquivoFoto`, coluna nunca usada). Ela continua como está; a miniatura tem FK
  própria.
- **Gerar em lote** as miniaturas que faltam. O Rastru ainda não está em uso real e o `seed-demo` não tem sólido; o botão
  por Componente basta (C10).

## 2. Decisões

### 2.1 Do usuário

| # | Decisão |
|---|---|
| D1 | **O navegador desenha a imagem e a API a guarda** (caminho barato da conversa do custo). Gerar no servidor foi descartado. |
| D2 | Onde aparece **agora**: o Panorama e o detalhe do Componente (opção 1 de três). As telas do chão de fábrica (Fila, Tarefas, Separação, Expedição) ficam para **depois do Panorama**. |
| D3 | **Estilo C, "fosco com arestas"**: cinza claro fosco, sombra suave por face e as arestas marcadas em tinta escura (contra A, metal sombreado como o visualizador, e B, desenho técnico de faces brancas e arestas pretas). |
| D4 | **A miniatura vai junto com o sólido, na mesma requisição** (forma 1 de três; seção 7), com o "Gerar miniatura" avulso para o que ficou sem. |
| D5 | Ordem: miniatura → Panorama, antes da 3C. |

### 2.2 Do desenho (o usuário confere)

| # | Chamada |
|---|---|
| C1 | **A imagem:** PNG de **320 × 240** px (4:3), **fundo transparente**, uma por Componente. O mesmo arquivo serve o detalhe do Componente e a linha de 52 × 40 do Panorama (a tela reduz). |
| C2 | **A câmera:** ortográfica, isométrica, olhando de `(1, 1, 1)` para o centro do sólido, com o eixo Y para cima, **sem girar o STL**, como o `VisualizadorDeSolido` já o lê. Enquadramento pela esfera envolvente com 8% de folga, para nenhuma Peça encostar na borda. |
| C3 | **O estilo C em números** (os da página de estilos, ajustáveis pelo builder sem mudar o aspecto): `MeshLambertMaterial` cinza claro (`#C9D3D1`), luz ambiente 0,55 e uma direcional 0,6 vinda de cima e da frente, arestas por `EdgesGeometry` com limiar de 20° em `#2F3A38`, e `polygonOffset` na malha para a aresta não brigar com a face. |
| C4 | **Onde mora:** `dbo.ArquivoDeComponente`, a mesma tabela do sólido, com FK nova `Componente.ArquivoMiniaturaId` (nula). Trocar o sólido troca a miniatura; a anterior continua na tabela sem ninguém apontando, a mesma política do sólido ("não existe remoção", `SolidoDoComponenteUseCase`). |
| C5 | **No import do BOM:** coluna nova `ImportacaoDeEstruturaComponente.ArquivoMiniaturaPendenteId`, ao lado do `ArquivoSolidoPendenteId`, com o mesmo ciclo: nasce com o STL pendente, o pendente anterior é apagado quando o novo é ligado, a confirmação a passa para o Componente junto com o sólido, e descartar o rascunho apaga as duas. O **reimport**, que hoje apaga o STL pendente dos registros que saem do arquivo, apaga a miniatura pendente deles também. |
| C6 | **Sólido trocado sem miniatura** (o navegador não conseguiu desenhar): a FK da miniatura vai para **nula** na mesma transação. Nunca fica a miniatura do sólido antigo ao lado do sólido novo. |
| C7 | **Validação no servidor** da miniatura, num validador puro como o `ValidadorDeArquivoStl`: assinatura PNG, cabeçalho `IHDR` com exatamente 320 × 240, e no máximo 512 KiB. Fora disso, 400 com frase própria. O servidor não redesenha nem recomprime. |
| C8 | **Ordem de validação** no envio conjunto: o STL é validado primeiro, como hoje; uma miniatura inválida recusa a requisição inteira (400), em vez de gravar o sólido sem ela, porque miniatura inválida vinda do próprio front é defeito, não falta de WebGL. |
| C9 | **Quem envia:** os mesmos perfis do sólido (PCP e Administrador). O "Gerar miniatura" avulso também. |
| C10 | **"Gerar miniatura"** no detalhe do Componente, só quando há sólido e não há miniatura: baixa o STL pela rota de sempre, calcula o SHA-256 dos bytes baixados (`crypto.subtle.digest`), desenha e envia pela rota avulsa (seção 5.2) com esse hash, para o servidor recusar se o sólido mudou no meio (C15). Serve aos sólidos antigos e ao celular que não desenhou. Não aparece com miniatura presente: regerar é trocar o sólido. |
| C11 | **Leitura:** `GET /componentes/{id}/miniatura`, autenticada como as outras. Como o `<img src>` não manda o token, a tela busca pelo `apiFetch` e guarda a imagem em memória (URL de `blob:`), chaveada pela **versão** (C12). A recarga de 30 s do Panorama não baixa de novo; só baixa quando a versão muda. Deixar a imagem sem login foi descartado: exporia o desenho das peças dos clientes. |
| C12 | **Os DTOs ganham** `miniaturaVersao`: o `Sha256` da miniatura em hexadecimal (a coluna calculada que a tabela já tem), ou nulo sem miniatura. **Não** é o id do arquivo: o `ComponenteDto` evita expor id de arquivo de propósito (o comentário dele: abriria um segundo caminho para o mesmo recurso), e o hash é só versão, não endereço. Entra no `ComponenteDto` da lista (que já traz `temSolido`), no detalhe e no registro da conferência do import. **No import**, segue a regra que o `NomeDoSolido` já segue: com STL pendente, é a versão da miniatura **pendente** (nula se o pendente veio sem miniatura, C6 aplicado antes da confirmação); sem STL pendente, a do catálogo. O registro ganha `miniaturaPendente` (booleano) para a tela saber qual rota de leitura chamar. É o `miniaturaVersao` que o Panorama usa no lugar do `temMiniatura` provisório da spec dele. |
| C13 | **Falha ao desenhar não trava o upload:** sem WebGL, STL que o `STLLoader` não lê, ou erro no desenho, o sólido sobe sozinho (C6) e a tela diz, sem tom de erro, "O sólido foi enviado, mas a miniatura não pôde ser gerada neste aparelho." |
| C14 | **Tempo de desenho:** o desenho roda antes do envio, depois de a pessoa escolher o arquivo. O botão de envio mostra "Gerando miniatura…" e depois "Enviando…". Um STL de 16 MiB (o teto) tem centenas de milhares de triângulos. O plano mede no PC (por agente) e tem uma **task de conferência do usuário no navegador**, que inclui subir um STL grande pelo **celular Android dele**; pela regra do `CLAUDE.md`, ela vem antes da review de branch. O teto aceitável é **5 s** para desenhar no Android. Reduzir o trabalho das arestas **não** entra de antemão: só se a medição passar do teto, e aí volta ao usuário antes de virar código. |
| C15 | **A rota avulsa confere o sólido:** `POST /componentes/{id}/miniatura` recebe o `solidoSha256` do STL de que a imagem foi desenhada e recusa com 409 `SolidoMudou` quando ele não é o `Sha256` do sólido atual. Fecha a corrida em que alguém troca o sólido entre o "Gerar miniatura" baixar o STL e enviar a imagem, que ligaria a imagem velha ao sólido novo (contra C6). O envio conjunto (5.1) não precisa disso: STL e imagem chegam na mesma requisição. |

**Nota de emenda à C14 (2026-11-17, na review de branch).** (a) Não há "botão de envio": o `UploadDeSolido` envia no
`change` do campo, e "Gerando miniatura…" e "Enviando…" são um status (`<p role="status">`) que a tela mostra enquanto
trabalha. O comportamento é o que a C14 queria; só a frase falava de botão. (b) A medição no PC foi feita na review de
branch, em Node (o mesmo V8 do Chrome), sobre o `toro-grande.stl` (16.000.084 B, 320 mil triângulos): `STLLoader.parse`,
9 a 24 ms; normais e esfera envolvente, 41 a 57 ms; `EdgesGeometry(geo, 20)`, 1.176 a 1.449 ms. O teste no Android
**continua pendente** e nenhum corte foi decidido: pela C14, ele só volta ao usuário se a medição no celular passar dos
5 s.

## 3. Domínio

Não há regra de negócio nova. A regra 18 (o sólido é obrigação de toda Peça de Pedido) não muda, e a miniatura **não** é
obrigatória: Componente com sólido e sem miniatura é válido. O glossário de `specs/01` ganha o termo "Miniatura" ao lado
de `Componente.ArquivoSolidoId`.

## 4. Banco

- `specs/02-modelo-de-dados.sql` primeiro: `dbo.Componente.ArquivoMiniaturaId INT NULL` com
  `FK_Componente_ArquivoMiniatura` para `dbo.ArquivoDeComponente`; `dbo.ImportacaoDeEstruturaComponente.
  ArquivoMiniaturaPendenteId INT NULL` com a FK correspondente. Os comentários do schema que dizem "a foto entra depois
  com uma FK nova" passam a citar a miniatura como a FK que entrou (a foto continua por vir).
- `db/alter-miniatura.sql`, idempotente, no formato dos anteriores (`-b -f 65001`): as duas colunas e as duas FKs, se
  faltam. Não muda dado; não precisa regenerar o banco.
- Mapeamento EF Database First das duas colunas.

## 5. Backend

### 5.1 O envio conjunto

`POST /componentes/{id}/solido` e `POST /importacoes/{id}/componentes/{cid}/solido` aceitam um campo de formulário
**opcional** `miniatura` (arquivo PNG) ao lado de `arquivo`. Grava o STL e a miniatura (ou a FK nula, C6) na mesma
transação: no cadastro, pelo `GravarEVincularComoSolidoAsync` estendido (ou um irmão dele); no import, no mesmo passo que
liga o STL pendente. Sem o campo, o comportamento é o de C6. Recusas novas: 400 da miniatura inválida (C7, C8). O limite
de corpo do endpoint sobe pela margem da miniatura (512 KiB), e o `specs/05` registra isso no parágrafo do limite.

### 5.2 O envio avulso

`POST /componentes/{id}/miniatura` *(PCP, Administrador)*: `multipart/form-data` com `miniatura` e o campo
`solidoSha256` (C15). Responde 204. Recusas: 404 se o Componente não existe; 409 `SemSolido` se ele não tem sólido (não
há do que ser miniatura); 409 `SolidoMudou` se o hash não é o do sólido atual; 400 da miniatura inválida ou do hash
ausente ou malformado. Grava e liga, substituindo a anterior se houver (a tela só oferece o botão sem miniatura, mas a
rota não precisa recusar). Não há rota avulsa no import: lá a miniatura só nasce com o STL pendente.

### 5.3 A leitura

`GET /componentes/{id}/miniatura` *(qualquer perfil autenticado)*: `image/png`, com `Cache-Control: private` e `ETag`
pelo `Sha256` que a tabela já calcula. 404 quando o Componente não existe ou não tem miniatura. A conferência do import
ganha `GET /importacoes/{id}/componentes/{cid}/miniatura` para a miniatura pendente, no molde da rota do STL pendente
(404 sem corpo nos três casos).

### 5.4 Testes

- Application, com fakes: o validador da miniatura (assinatura, dimensões, tamanho, cada recusa); o envio conjunto com e
  sem miniatura (C6: a FK vai a nula); a miniatura inválida que recusa o STL junto (C8); o avulso com e sem sólido, e com
  o hash de um sólido que já foi trocado (C15); a `miniaturaVersao` do import nos três casos de C12.
- Infrastructure: o mapeamento das duas colunas; a transação que liga sólido e miniatura juntos.
- Api: as rotas ponta a ponta, o gating, o `ETag`, e o ciclo do import (pendente → confirmação → Componente; descarte
  e reimport apagam as duas; o pendente anterior apagado ao trocar).

## 6. Front

- **Função de desenho** num módulo próprio (`web/src/solido/`, ou ao lado do visualizador, a critério do builder):
  `desenharMiniatura(stl: ArrayBuffer): Promise<Blob | null>`, com o `three` e o `STLLoader` por `import()` dinâmico,
  como o `VisualizadorDeSolido`, para não pesar no carregamento das outras telas. Devolve `null` em qualquer falha (C13).
  Os números de C1 a C3 viram constantes nomeadas. Como o jsdom não tem WebGL, os testes de tela trocam a função por um
  dublê; a função em si tem teste do que dá para testar sem GPU (enquadramento e escolha da câmera, se ficarem puros).
- **`UploadDeSolido`:** depois de escolher o arquivo e de a checagem de tamanho passar, lê o arquivo, chama
  `desenharMiniatura` e entrega os dois ao `enviar` (que passa a receber a miniatura opcional). Mostra os passos de C14 e
  o aviso de C13.
- **Detalhe do Componente:** a miniatura ao lado do upload, e o "Gerar miniatura" (C10), sob o mesmo
  `usePodeEscrever('componentes')` do upload.
- **Conferência do import:** o painel do Componente (`PainelDoComponenteDaImportacao`) passa a enviar a miniatura junto
  com o STL pendente, pelo mesmo `UploadDeSolido`. Mostrar a miniatura pendente no painel é opcional para o builder.
- **Exibição:** uma primitiva `MiniaturaDoSolido` em `web/src/components/`, com teste próprio, que recebe o caminho e a
  versão, busca pelo `apiFetch` com o cache de C11 e mostra um espaço neutro enquanto carrega, quando não há miniatura e
  quando a busca falha. O `alt` é o código e a descrição do Componente. É ela que o Panorama usa.
- `npm run build` no ciclo; testes de tela com `web/src/testes/api.ts`.

## 7. Abordagens consideradas

1. **Junto com o sólido, na mesma requisição** — **escolhida (D4)**. Sólido e miniatura nunca desencontram; o caso sem
   WebGL vira Componente sem miniatura, resolvido pelo "Gerar miniatura".
2. **Uma requisição depois do upload.** Não mexeria na rota do sólido, mas uma falha na segunda requisição deixaria o
   sólido novo com a miniatura velha, ou exigiria apagar a velha no upload e aceitar a Peça sem imagem até a segunda
   chegar.
3. **Gerar na primeira vez que alguém vê.** Nunca precisaria de botão, mas a coluna do Panorama ficaria vazia até alguém
   abrir cada Componente, e uma visita de leitura passaria a escrever no servidor.

E, antes delas, **no navegador contra no servidor** (D1): no servidor exigiria um renderizador de STL que o .NET não tem.

## 8. Riscos conhecidos, aceitos

- **A imagem é do aparelho de quem subiu.** Drivers de GPU diferentes desenham com diferenças pequenas de serrilhado e de
  tom. Aceito: o estilo é fosco e sem reflexo justamente para variar pouco.
- **STL pesado no celular** pode levar segundos para desenhar (C14).
- **Arestas demais em superfície curva mal tesselada:** o limiar de 20° pode marcar facetas de um cilindro grosseiro.
  Aceito; o builder pode ajustar o limiar se a massa de teste mostrar.
- **Arquivos órfãos** crescem a cada troca de sólido, como já acontece com o sólido (C4).
- **`crypto.subtle` só existe em contexto seguro** (HTTPS ou `localhost`). O HML é HTTPS e o desenvolvimento é
  `localhost`; acessar o dev pelo IP da máquina na rede (do celular, por exemplo) deixa o "Gerar miniatura" sem hash. O
  envio conjunto não depende dele.

## 9. Ajustes de documentação (na mesma branch)

- `specs/01`: o termo "Miniatura" no glossário.
- `specs/02`: as duas colunas e o comentário da foto.
- `specs/05`: as rotas das seções 5.1 a 5.3 e o campo `miniaturaVersao` nos DTOs.
- `specs/06-roadmap-mvp.md`: a seção da miniatura (antes do Panorama e da 3C) e o item "miniatura no chão de fábrica"
  depois do Panorama.
- `CLAUDE.md`: a primitiva `MiniaturaDoSolido` na «Interface», a contagem de usos do `UploadDeSolido` e do
  `VisualizadorDeSolido` se mudar, e o `db/alter-miniatura.sql` em «Comandos».
- `docs/superpowers/specs/2026-11-17-panorama-design.md`: o `temMiniatura` provisório virou o `miniaturaVersao` de C12
  (feito junto com a aprovação desta spec, em 2026-11-17).

## 10. Fora do escopo

- A miniatura na Fila, nas Tarefas, na Separação e na Expedição (depois do Panorama, D2).
- Gerar em lote as que faltam.
- Trocar o estilo de todas as miniaturas já geradas (regerar é trocar o sólido, ou um lote futuro).
- A foto de referência.
