# Fase 2C — Import de montagem no cadastro do Pedido

- **Data:** 2026-09-17
- **Status:** aprovado pelo usuário, parte a parte
- **Natureza:** spec de desenho de uma fase. Define fluxo, domínio, schema, API e tela; não
  define tasks — o plano de implementação sai daqui.
- **Branch:** `brainstorm-solidos-no-pedido`, tirada de `main` (`f2ee813`), worktree `C:/wt-solidos`.
  A Fase 2B **não estava mesclada** quando este texto foi escrito, e o código dela foi lido no
  checkout principal. Ao retomar, rebasear sobre o `main` novo e remedir o que esta spec afirma
  sobre o código da 2B.

## 1. O problema

Hoje o sólido de um `Componente` só sobe pela tela do Componente, uma peça de cada vez. Quem
cadastra um Pedido chega com os arquivos na mão — palavras do usuário: *"a pessoa que cadastra o
pedido já vai ter os sólidos que precisa inserir no sistema"* — e precisa, antes de montar a
estrutura, cadastrar cada Componente e subir cada STL em telas separadas. Além disso, a árvore da
montagem **já existe, pronta e correta, dentro do CAD**, e hoje é digitada nó a nó.

A fase ataca as duas coisas de uma vez: o cadastro da Peça passa a começar por uma **importação**
da montagem, e o sólido entra por ali.

## 2. O que já estava decidido, e o que esta spec acrescenta

`specs/06-roadmap-mvp.md` tem, fora das fases, a seção **"importar a estrutura a partir do CAD"**,
decidida em 2026-08-04. O que vem de lá e **continua valendo**:

- a entrada é o **BOM indentado exportado** (CSV/XLSX), não o `.SLDASM` — formato proprietário, sem
  biblioteca aberta confiável, e as duas APIs da Dassault exigem licença ou o SolidWorks instalado
  no servidor;
- o import gera uma **proposta que um humano confere**, nunca gravação direta;
- **não muda o schema das tabelas de destino**: `Componente`, receita padrão e `EstruturaItem` já
  são o destino;
- a conciliação entre o código do CAD e o `Componente.Codigo` é **regra de negócio**: quem importa
  confere código e quantidade. Isso torna o import **pré-preenchimento, não automação** — ele não
  precisa acertar tudo.

O que esta spec acrescenta: os **sólidos entram junto do BOM**, a importação **fica guardada** para
ser conferida depois, e o destino inclui o **catálogo** (Componentes e receita padrão), não só a
árvore do Pedido.

## 3. Decisões do brainstorm

### D1 — A entrada é BOM + STLs, e a chave entre eles é o nome do arquivo

O usuário confirmou que quem cadastra tem a **montagem do SolidWorks** e exporta dela o que for
preciso. Para o sistema isso é indistinguível de receber BOM e STLs prontos.

A chave que liga um STL à sua linha do BOM **não é o código do cliente**, e sim o **nome do
documento**, porque os dois artefatos saem do mesmo `.SLDASM`. O casamento é por nome de arquivo,
com conferência manual para o que não casar. Descartadas: uma **macro do SolidWorks** que exportasse
tudo com nomes garantidos (é a única forma de *garantir* a chave, e roda onde já existe licença, mas
custa código fora da stack instalado em cada máquina — fica disponível se a medição frustrar o
casamento) e **ligar tudo à mão** (é justamente o trabalho que se quer poupar).

### D2 — O STL não carrega identificação utilizável, e isso foi medido

Pergunta do usuário: existiria identificação **dentro** do arquivo, melhor que o nome?

O formato tem espaço, mas não tem campo. No STL binário são 80 bytes de cabeçalho em **texto
livre**; no ASCII, a primeira linha é `solid <nome>`, também livre. Não existe *part number*.

Medido em 2026-09-17, com autorização do usuário, em duas fontes:

- **O STL que ele tinha em mãos** (`fx 77-080 empenagem.stl`, 196.824 bytes): ASCII, um sólido só,
  730 faces, primeira linha `solid exported` e última `endsolid exported`. Dentro do arquivo não
  aparecem "fx", "77-080" nem "empenagem" — a única identificação é o nome do arquivo. O padrão
  (`solid exported`, tab, LF) é o do `STLExporter` do three.js, e o usuário confirmou: **o arquivo
  passou por um conversor web**, não saiu direto do SolidWorks.
- **Os 8 arquivos do banco de dev**: sete são fixtures nossas (cabeçalho zerado ou o texto
  `rastru verificacao task 9`/`11`/`12`); o oitavo é o mesmo `fx 77-080`.

**Conclusão:** não existe, hoje, nenhuma amostra de STL exportado pelo SolidWorks para medir, e o
cabeçalho **não é confiável como chave** — basta o arquivo passar por outra ferramenta para o nome
se perder. Se uma medição futura mostrar que o SolidWorks grava o nome do documento lá, o cabeçalho
entra como **segunda tentativa** de casamento, sem mudar mais nada. O ganho seria só resistir a
renomeação do arquivo, e no binário o nome trunca em 80 bytes.

### D3 — Uma montagem vira uma Peça

Decisão do usuário: a montagem do SolidWorks corresponde a **uma Peça**; as linhas do BOM viram
Itens e sub-Itens, e a recursão da Fase 2 já comporta isso. O import entra num Agrupamento que já
existe e cria **uma** Peça.

Consequência: o BOM lista os componentes da montagem, **não a própria montagem**. Como a regra 18
exige sólido na Peça, a importação recebe também o **STL da montagem inteira**, num campo próprio.

Descartadas: montagem = Agrupamento e montagem = Pedido. A segunda obrigaria o CAD a decidir o Tipo
do Agrupamento (Kit ou Avulso), que é decisão de fábrica e não existe no arquivo.

### D4 — Grava o catálogo primeiro, e a Peça sai da cópia da Fase 2

Ao confirmar, o import cria os **Componentes** novos com sólido, grava a **receita padrão** de cada
montagem e então cria a Peça chamando a **cópia que a Fase 2 já faz**.

Três razões:

- é o que atende o caso que o usuário descreveu — *"se o Pedido mencionar Peças que já foram
  fabricadas (catálogo), é capaz do sólido nem ser enviado"* —, porque na próxima vez a montagem já
  está no catálogo com a árvore junto;
- o roadmap já preparou o caminho: o caso de uso da receita padrão substitui a **lista inteira** de
  uma vez, decisão tomada pensando no import;
- a quantidade do BOM é **por montagem-pai**, mesma grandeza de `ComponenteFilhoPadrao.QuantidadePadrao`,
  e a cópia da Fase 2 já multiplica, já tem guarda de ciclo, de profundidade e de faixa da coluna.

Descartado: gravar `EstruturaItem` direto, sem receita — a peça repetida voltaria sem árvore.

**Custo aceito:** todo import deixa linhas permanentes no catálogo, inclusive de peça que nunca se
repete. A regra 18 já aceitou esse custo ao decidir que "peça de uma vez só vira linha de catálogo".

### D5 — Componente existente não é alterado; divergência vira aviso

Quando uma linha é ligada a um `Componente` que já existe, o import **usa como está**: valem o
sólido e a receita do catálogo, e os filhos que o BOM lista abaixo dela são ignorados.

- **Única escrita permitida em Componente existente:** preencher o sólido quando ele está **vazio**.
- **Divergências viram aviso e não agem:** receita diferente da do BOM, descrição diferente, ou STL
  enviado com `Sha256` diferente do sólido guardado — o hash sai de graça, porque a 2B fez do
  `Sha256` uma coluna calculada.

Razão: a regra 18 registra que *"se a geometria mudou não é mais a mesma peça"*. STL diferente sob o
mesmo código é mais provavelmente **revisão**, que alguém precisa olhar, do que atualização
silenciosa. Atualizar catálogo continua na tela do Componente, por decisão explícita.

Descartadas: atualizar pelo import (o catálogo mudaria para todos os Pedidos futuros sem ninguém
decidir) e perguntar linha a linha.

### D6 — Importação guardada no servidor, processada na chegada, sem worker

A ideia original do usuário previa um worker importando em segundo plano. O desenho entrega o que
ele queria — soltar os arquivos agora e conferir depois — **sem** worker.

O que decide: **o envio dos arquivos acontece no navegador em qualquer desenho** (um worker não
processa arquivo que ainda não foi enviado), e o processamento do servidor é validação de STL e
leitura de CSV, na casa dos milissegundos. Um worker acrescentaria estado de job, consulta de status
e job perdido em reinício da VPS, sem tirar tempo de espera de lugar nenhum.

Fica registrado como **porta aberta**: se um dia entrar processamento pesado — gerar silhuetas para
a busca por foto, converter formato —, o worker passa a fazer sentido.

Descartado: fazer tudo no navegador, sem guardar. Fechar a aba perderia o trabalho, a conferência
teria de ser na mesma sessão, e a confirmação viraria N requisições sem atomicidade.

### D7 — Transação explícita no caso de uso, por `IUnidadeDeTrabalho`

Confirmar precisa ser tudo ou nada, e hoje **cada repositório abre a própria transação**. Medido em
2026-09-17: são **4** transações — `ArquivoDeComponenteRepository.GravarEVincularComoSolidoAsync`,
`EstruturaRepository.GravarArvoreAsync`, `EstruturaRepository.RemoverSubarvoreAsync` e o
`Substituir<T>` de `ReceitaPadraoRepository` —, das quais **2 são `Serializable`**
(`GravarArvoreAsync` e `Substituir<T>`). O `DbContext` e os repositórios são *Scoped*, então existe
uma conexão por requisição.

A escolha do usuário foi **transação explícita no caso de uso da confirmação**, por uma abstração da
Application (`IUnidadeDeTrabalho`), com os repositórios **aproveitando a transação corrente** quando
ela já existe. Só o caso de uso novo depende da abstração: os construtores atuais não mudam, e os
**63** pontos de teste que constroem `MontagemDeEstruturaUseCase`, `ReceitaPadraoUseCase`,
`CadastroDeComponenteUseCase` ou `SolidoDoComponenteUseCase` (medido em 2026-09-17) continuam como
estão.

**Saga foi considerada e descartada, com o motivo escrito porque a pergunta vai voltar:** saga existe
para quando **não há** uma transação única — escrita em serviços ou bancos diferentes —, e uma saga
orquestrada dispensa eventos, então "não somos orientados a eventos" não é o argumento. Aqui há um
banco só e uma conexão por requisição; usar saga trocaria uma garantia do banco por estado
intermediário visível (o Componente novo apareceria no catálogo antes da Peça existir) e por
compensações que também podem falhar. **Onde ela faria sentido:** se o STL morasse fora do banco, num
storage de objetos — que é exatamente o que a 2B evitou ao escolher blob.

**Também descartada a transação global por middleware**, apesar de ser a forma mais genérica. Três
custos, os dois primeiros medidos no código:

1. **Falha que precisa gravar.** Senha errada responde 401 **e** persiste o contador de lockout
   (`Senha_errada_incrementa_o_contador_e_persiste` afirma `Saves == 1`); reuso de refresh responde
   401 **e** queima a família. Como os casos de uso devolvem `Result.Falha` em vez de lançar exceção,
   um middleware não distingue "falhou, desfaça" de "falhou, mas grave" — desfazer em todo erro
   desliga as duas defesas em silêncio.
2. **Um isolamento só para tudo.** `Serializable` global trava à toa; `ReadCommitted` global tira a
   proteção que a receita tem hoje, e que existe porque **receita vazia não tem linha para travar**.
3. **Transação aberta durante o upload.** Middleware envolve a leitura do corpo, então cada STL de
   até 16 MiB subiria segurando transação e conexão. Um *action filter* evita este ponto, mas não os
   dois primeiros.

As transações internas teriam de ser ajustadas **em qualquer caminho**, então o middleware não
economizaria esse trabalho; centralizaria.

### D8 — Várias importações pendentes ao mesmo tempo

Pergunta do usuário: quem cadastra pode precisar importar várias montagens e conferir uma a uma.
**Não há limite**: cada montagem é uma importação própria, no mesmo Agrupamento ou no mesmo Pedido, e
cada uma é conferida e confirmada sozinha.

- **Peça repetida entre montagens resolve sozinha**, porque o casamento é derivado (D10): quando a
  primeira importação confirma, a segunda passa a enxergar aquele código como **existente**.
- **Guarda nova:** criar importação é recusado quando o código da montagem já está no catálogo **ou**
  já é usado por outra importação pendente. Sem a segunda metade, duas pendentes iguais só quebrariam
  na confirmação, pela unicidade de `Componente.Codigo`.
- **Cada importação tem seu próprio envio de arquivos.** Não há leva única repartida entre montagens
  — é consequência de D3.

### D9 — O aviso ganha token próprio, `aviso`

Decisão do usuário, com a razão dele: *"é bom que já valida pra qualquer coisa que a gente queira
adicionar como 'Atenção' futuramente"*. O token nasce no padrão dos existentes — `positivo` e
`negativo` têm três tons cada (cheio, texto e fundo) —, então `aviso` ganha os três, com o par de
contraste **medido**, e a `contraste.test.ts` passa a ser a guarda dele.

Descartado: usar só neutro com "⚠", que não tocaria a paleta. O `CLAUDE.md` já registra que âmbar
claro **reprova AA** como texto sobre branco, então os tons precisam ser escolhidos medindo, não no
olho.

### D10 — Guardar só a decisão humana; o casamento é calculado na leitura

Enquanto a importação está pendente, o catálogo pode mudar — alguém cadastra um Componente com o
código de uma linha, ou outra importação confirma. Casamento guardado ficaria desatualizado sem
aviso; calculado na leitura, continua verdadeiro. Só o que a pessoa decidiu é gravado: vínculo
manual, desvínculo, linha ignorada, edições de código, descrição, quantidade e tipo.

É o mesmo raciocínio das colunas calculadas da 2B: o que é derivável não se guarda.

## 4. Premissas não medidas

Nenhuma destas bloqueia o desenho, e todas mudam o **plano**. Medi-las antes de escrever o plano vale
mais do que qualquer decisão tomada aqui. Basta **um** export real de uma montagem qualquer.

| # | Premissa | Se for falsa |
|---|---|---|
| P1 | O SolidWorks salva o BOM em **CSV** | entra biblioteca de XLSX no backend |
| P2 | As colunas têm nomes reconhecíveis (PT ou EN) | o mapeamento manual (§7) deixa de ser confirmação e vira trabalho obrigatório |
| P3 | O BOM indentado sai com **numeração pontuada** (`1`, `1.1`, `1.1.2`) | a leitura da hierarquia muda de forma — é a premissa mais cara |
| P4 | A quantidade do BOM é **por montagem-pai** | a conversão para `QuantidadePadrao` passa a precisar de divisão pelo pai, e cai no problema que `2026-09-15-kit-montagem-e-movimentacao-design.md` (na branch `brainstorm-kit-montagem`) já mapeou ao descartar derivar a razão como filho ÷ pai: sobra de refugo vira exigência |
| P5 | O STL exportado por componente tem o **nome do documento**, sem prefixo da montagem | o casamento automático cai para quase zero e a macro (D1) volta à mesa |

Uma sexta, já medida e **negativa**: o cabeçalho do STL não serve como chave (D2).

## 5. Fluxo

1. **No Agrupamento**, ao lado de "criar Peça do catálogo", há **"Importar montagem"**. Quem importa
   informa **código e descrição da montagem** — que vira o `Componente` da Peça — e abre a importação.
2. **Envio dos arquivos:** o BOM, o STL da montagem e os STLs das partes, **um por requisição**, com
   progresso. Cada STL passa pela validação da 2B na chegada; inválido é recusado e não entra. O BOM
   é lido na chegada.
3. **A importação fica pendente e salva.** Dá para sair e voltar depois, de outra máquina.
4. **Conferência:** a árvore proposta, com o estado de cada linha, os avisos, os arquivos que
   sobraram e as pendências que bloqueiam.
5. **Confirmar** grava tudo numa transação e a importação deixa de existir.
6. **Descartar** apaga a importação e os STLs dela.

**O que não muda:** o upload na tela do Componente continua (é por ali que se substitui um sólido);
criar Peça a partir de Componente existente continua igual; o import **não** preenche materiais nem
roteiro da receita, porque o BOM não os traz.

## 6. Leitura do BOM

**Só CSV** neste primeiro momento (P1).

**Robustez de arquivo gerado em Windows brasileiro**, que é onde acentuação se perde sem sintoma:

- **Codificação:** tenta UTF-8 estrito; havendo byte inválido, lê como Windows-1252. A prova é uma
  fixture com "Fixação" cujo `ç` sobrevive.
- **Separador:** `;` ou `,`, detectado pela linha de cabeçalho.
- **Decimal:** vírgula aceita.

**Colunas: o sistema sugere, a pessoa confirma.** Quatro papéis — Item, Código, Descrição e
Quantidade — pré-preenchidos quando o nome da coluna é reconhecido. A escolha fica salva na
importação. Não fixo os nomes porque dependem do idioma do SolidWorks e do template (P2).

**Hierarquia pela numeração pontuada:** o pai de `1.1.2` é `1.1`; `1`, `2`, `3`… são filhos diretos
da montagem. Linha cujo pai não existe no arquivo recebe erro na própria linha (P3).

**O que cada linha nova vira:**

- **Código** e **Descrição** das colunas; passar de 50 ou 200 caracteres é erro de linha, corrigível
  na conferência (são os limites de `Componente`).
- **Tipo**: `Montagem` se a linha tem filhos, `Fabricado` se não tem, editável. A raiz é sempre
  `Montagem`. (`Componente.Tipo` aceita `Bruto`, `Fabricado` e `Montagem`.)
- **A mesma peça em lugares diferentes** vira **um** Componente com várias linhas de receita.
- **A mesma peça repetida sob o mesmo pai** tem as quantidades **somadas**, com aviso — forçado por
  `UQ_ComponenteFilhoPadrao`, que não aceita o par pai/filho duas vezes.

**Ação "ignorar linha".** BOM de SolidWorks costuma listar parafusos e arruelas, que neste domínio
são **Material** (regra 4), não Componente. A conferência precisa tirá-las do import. Ligá-las à
receita de materiais fica fora do escopo.

## 7. Casamento, estados e bloqueios

**Casamento automático**, refeito a cada leitura (D10):

- **Linha ↔ Componente existente:** código da linha igual a um `Componente.Codigo`. A linha mostra a
  descrição do BOM **ao lado** da do catálogo, porque nome de documento pode coincidir por acaso com
  código de outra peça; descrições diferentes geram aviso. O vínculo pode ser desfeito, e ligar à mão
  usa o `SeletorComBusca`.
- **STL ↔ linha:** nome do arquivo sem `.stl` igual ao código da linha, comparando sem espaços nas
  pontas e ignorando caixa. Nenhuma normalização além dessa, porque P5 não está medida. O que não
  casar vai para **"arquivos sem linha"**.
- **STL da montagem:** casa com o código da montagem; sem isso, a tela pede para marcar qual é.

**Estados da linha** — aviso informa, erro bloqueia:

| Estado | Efeito |
|---|---|
| Nova, com STL | nenhum |
| Nova, sem STL | nenhum: a regra 18 cobra sólido só da **Peça** |
| Existente | usada como está |
| Existente sem sólido, com STL enviado | preenche o sólido |
| Existente com divergência (receita, hash ou descrição) | aviso |
| Ignorada | fora do import |
| Erro (pai inexistente, quantidade inválida, código ou descrição vazios ou longos) | bloqueia |

**A tela mostra o que será gravado, não o que o BOM diz.** Abaixo de uma linha ligada a Componente
existente, o que vai para a Peça são os **filhos do catálogo** — é a cópia da Fase 2 que desce por
eles. A conferência os exibe recolhidos, só para leitura.

**Bloqueiam a confirmação:** falta do STL da montagem (regra 18); qualquer linha com erro; falta da
quantidade de Peças; estouro dos limites da cópia (profundidade **20**, **500** nós — constantes de
`PlanejadorDeCopia`), contando os filhos do catálogo; e ciclo, que as guardas existentes já pegam.

**Mais dois casos:** montagem cujo código já existe no catálogo ou em outra pendente é recusada na
criação (D8); arquivos que sobram são descartados ao confirmar, e a tela avisa quantos antes.

## 8. Modelo de dados e ciclo de vida

O schema nasce em `specs/02-modelo-de-dados.sql`, e os blocos idempotentes vão para o `CLAUDE.md`.
Nada de `Add-Migration`.

Três tabelas novas, **só para a importação pendente** (nomes a confirmar no plano):

| Tabela | Guarda |
|---|---|
| `ImportacaoDeMontagem` | Agrupamento, código e descrição da montagem, FK do STL da montagem, texto do BOM já decodificado, mapeamento das quatro colunas, autoria e data |
| `LinhaDeImportacao` | a cópia de trabalho de cada linha: item, código, descrição, quantidade, tipo escolhido, vínculo manual a Componente, vínculo manual a arquivo, ignorada |
| `ArquivoDeImportacao` | ponte entre a importação e as linhas de `ArquivoDeComponente` que subiram |

**O STL vai direto para `ArquivoDeComponente` no upload.** Confirmar só aponta a FK do `Componente`,
sem copiar blob, e a validação da 2B é reaproveitada sem mudança. A regra que precisa valer sempre:
**todo `ArquivoDeComponente` é referenciado por um `Componente` ou por uma importação pendente** —
com teste de integração depois de confirmar **e** depois de descartar.

**Não há coluna de status: existir é estar pendente.** Confirmar e descartar **apagam** a importação.
Custo nomeado: perde-se o registro de qual BOM gerou qual Peça.

**Remapear colunas relê o BOM e descarta as edições das linhas**, avisando antes. Preservar edição
através de uma releitura em que linhas mudam de posição é complexidade que não se justifica.

**Bordas:** confirmar duas vezes — a primeira apaga a importação dentro da transação, a segunda
recebe 404, sem Peça duplicada. Duas pessoas editando a mesma importação — a última gravação vence.

## 9. Confirmação

Transação `Serializable` aberta pelo caso de uso (D7), e dentro dela, nesta ordem:

1. **Reler e revalidar tudo** contra o estado atual do banco. A tela pode estar velha; quem decide é
   o banco. Bloqueou, nada é gravado e a tela recarrega com o motivo.
2. **Criar os Componentes novos** — a montagem e as linhas novas — já com `ArquivoSolidoId`.
3. **Preencher o sólido dos existentes que não tinham** (única escrita em Componente existente).
4. **Gravar as receitas** dos Componentes novos que têm filhos.
5. **Criar a Peça**, com quantidade e Relatório Dimensional informados na confirmação.
6. **Apagar os arquivos que sobraram e a importação.**

Falhou qualquer passo, desfaz tudo e a importação continua pendente, intacta.

**A confirmação chama os casos de uso que já existem** — `ReceitaPadraoUseCase.SubstituirFilhos` e
`MontagemDeEstruturaUseCase.CriarPeca` — em vez de gravar nos repositórios. Assim a guarda de ciclo,
os limites da cópia e a cobrança da regra 18 não são reescritos, e conserto futuro em qualquer deles
alcança o import. Os dois enxergam o que o passo 2 inseriu, porque tudo corre na mesma conexão.

**`ConflitoDeConcorrenciaException` continua traduzida para 409**, como já acontece na receita: o
caminho novo não pode deixar esse desfecho subir cru.

## 10. API

Sob `/api`, escrita para `Administrador,PCP` e leitura para qualquer autenticado, como no resto do
projeto. `specs/05-api-endpoints.md` e o espelho `web/src/auth/permissoes.ts` mudam junto — na 2B
nenhum brief citou o `05`, e a documentação ficou para trás.

| Rota | O que faz |
|---|---|
| `POST /agrupamentos/{id}/importacoes` | cria a importação; **409** se o código da montagem já existe no catálogo ou em outra pendente |
| `GET /agrupamentos/{id}/importacoes` | lista as pendentes do Agrupamento |
| `GET /importacoes/{id}` | estado inteiro: linhas com estado, avisos, erros, filhos do catálogo, arquivos sem linha, e o que bloqueia |
| `POST /importacoes/{id}/bom` | sobe o CSV, lê e sugere o mapeamento |
| `PUT /importacoes/{id}/colunas` | confirma ou troca o mapeamento (relê) |
| `POST /importacoes/{id}/arquivos` | **um** STL por requisição, validado como na 2B, com indicador de "é o da montagem" |
| `PUT /importacoes/{id}/linhas/{linhaId}` | edita a linha: código, descrição, quantidade, tipo, vínculos, ignorar |
| `POST /importacoes/{id}/confirmacao` | confirma; **400** com os motivos; **404** se já não existe |
| `DELETE /importacoes/{id}` | descarta |

A tela do Pedido mostra quantas pendentes há por Agrupamento (D8), o que se resolve com a listagem
por Agrupamento — sem endpoint novo por Pedido, a menos que o plano meça que isso custa N chamadas.

## 11. Tela

- **Rota própria** `/importacoes/:id`, dentro do `AppShell`, começando por `<Pagina>`. Sair volta ao
  Agrupamento.
- **Entrada no Agrupamento:** botão "Importar montagem" e a **lista** de importações pendentes com
  "Continuar conferência".
- **Etapas no topo:** Arquivos → Colunas → Conferência. Quem volta cai direto na conferência.
- **Arquivos:** área de soltar, envio um a um com progresso e estado por arquivo; recusado não entra.
- **Colunas:** quatro seletores pré-preenchidos, as primeiras linhas do arquivo e as colunas
  escolhidas destacadas; aviso antes de remapear.
- **Conferência:** árvore com pílula de estado por linha, filhos do catálogo recolhidos, painel de
  arquivos sem linha, **barra fixa no rodapé** com contagens e pendências clicáveis, e **diálogo de
  revisão** ao confirmar, listando os efeitos. **Quantidade de Peças e Relatório Dimensional ficam na
  linha da montagem**, porque são atributos da Peça que vai nascer.
- **Linha:** edição de código, descrição, quantidade e tipo; alternância entre "criar novo" e "ligar
  a existente"; escolha do STL com "Visualizar" abrindo o `VisualizadorDeSolido` da 2B; "ignorar".
- **Reaproveita** `Pagina`, `Botao`, `Campo`, `Pilula`, `BannerDeErro`, `SeletorComBusca`,
  `VisualizadorDeSolido`, `Confirmacao`, `EstadoCarregando`, `EstadoVazio`.
- **Duas primitivas novas**, em `web/src/components/` com teste próprio: a **área de envio de vários
  arquivos** (o `UploadDeSolido` de hoje é de um arquivo só, preso ao Componente) e a **árvore da
  importação**. Esta **não** reaproveita a `ArvoreDeEstrutura`, que é montada sobre `EstruturaItemDto`
  e sobre as ações da Fase 2. O custo — duas árvores parecidas no projeto — fica **nomeado**:
  generalizar a existente mexeria numa tela já entregue, por antecipação.
- **Gating pela ação** com `usePodeEscrever`, e o `try/catch` do 403 obrigatório.
- **Os três estados** (carregando, vazio, erro), cada um com teste que morre se o estado sumir.

## 12. Fora do escopo

- **Materiais e roteiro da receita** — o BOM não traz.
- **Atualizar catálogo pelo import** — D5.
- **BOM do Pedido inteiro** — decorre de D3.
- **XLSX** — condicionado a P1.
- **Macro do SolidWorks** — disponível se P5 falhar.
- **Worker** — D6.
- **Limpeza automática de importação abandonada** — sem worker, importação esquecida segura os STLs
  dela para sempre. **Dívida registrada**, sustentada pelo horizonte do sistema, não por uma tese de
  que não acontece.
- **Busca por foto** — continua fora das fases, dependente do spike.

## 13. Testes

- **Leitura do BOM**, sem banco: separador `;` e `,`; UTF-8 e Windows-1252 com "Fixação"
  sobrevivendo; decimal com vírgula; pai ausente; quantidade inválida; código longo; repetição sob o
  mesmo pai somando; colunas mapeadas fora de ordem. **Fixture gerada em código**, como o STL de 684
  bytes da 2B — o que se testa aqui são os bytes.
- **Casamento**: caixa e espaços diferentes; arquivo sem linha; linha sem arquivo; código que bate
  com Componente existente.
- **Cada regra que bloqueia**, com mutação: apagar a guarda tem de matar um teste, e importa **onde**
  ele morre.
- **Confirmação em duas camadas:** com fakes, que falha no meio **não confirma** (o fake grava em
  memória e não desfaz — o que se prova ali é o protocolo); contra o SQL Server, que nada ficou
  gravado e que não sobra `ArquivoDeComponente` órfão depois de confirmar **nem** de descartar.
- **Endpoints**: 403 por perfil, 404, os dois 409, e a declaração de limite de tamanho (o
  `TestServer` não aplica `[RequestSizeLimit]` — o que dá para provar é a declaração).
- **Front**: os três estados, a barra fixa, o diálogo, a edição de linha, a primitiva de envio, e o
  token `aviso` passando pela `contraste.test.ts`.
- `npm run build` faz parte do ciclo, não só `npm test`.

## 14. Riscos

- **As cinco premissas de §4.** A mais cara é P3.
- **BOM real cheio de parafusos de biblioteca:** se forem dezenas, ignorar uma a uma vira trabalho.
  Mitigação possível, a decidir no plano: ignorar em lote.
- **Transação longa** na confirmação, em `Serializable`, criando Componentes, receitas e até 500 nós.
  Aceitável com poucos usuários simultâneos; não medido sob concorrência.
- **Duas árvores parecidas** no front (§11), com risco de divergirem no visual.

## 15. Posição no roadmap

**Numeração 2C, execução depois da Fase 3.** Decisão do usuário: o número marca a **linhagem
temática** — é a continuação direta da 2B, que trouxe o sólido —, e não a ordem de execução. Palavras
dele: *"é bom que fica como histórico de melhorias e decisões tomadas para aumentar a qualidade de
vida do operador que utiliza o sistema, demonstrando maturidade de projeto"*. É o mesmo padrão da
1F, que existe numerada e ainda não foi executada.

A razão de executar depois da 3: a Fase 3 fecha o fluxo que define o produto — rastrear a peça pelos
setores. O import acelera o cadastro, o que importa muito no uso real, mas não substitui a razão de
existir do sistema.

**Isso contradiz, ao pé da letra, a regra "Ordem de implementação" do `CLAUDE.md`**, que manda seguir
as fases em sequência e não implementar fase mais avançada antes da anterior. A contradição já
existia com a 1F e ninguém a escreveu. **Esta spec manda escrever:** o `06-roadmap-mvp.md` ganha, na
entrada da 2C, a nota de que número é linhagem e não ordem, e o `CLAUDE.md` ganha uma frase dizendo
que a ordem de execução vive no roadmap. Sem isso, quem ler as duas fontes conclui que o projeto
furou o próprio processo.

## 16. O que muda em `specs/` quando esta fase for executada

| Arquivo | Mudança |
|---|---|
| `specs/02-modelo-de-dados.sql` | as três tabelas da importação |
| `specs/01-dominio-e-regras-de-negocio.md` | glossário da Importação; nota na regra 18 sobre o sólido entrando pelo import; nota na regra 20 de que a receita pode nascer de import conferido |
| `specs/04-fluxos-de-usuario.md` | o fluxo 1 (Cadastro de Pedido) ganha o caminho do import ao lado do cadastro manual |
| `specs/05-api-endpoints.md` | os nove endpoints |
| `specs/06-roadmap-mvp.md` | entrada da Fase 2C, com a nota de numeração × ordem, e a seção "fora das fases" do import do CAD apontando para cá |
| `CLAUDE.md` | blocos idempotentes do schema; a frase sobre ordem de execução; o token `aviso` na seção de Interface |
