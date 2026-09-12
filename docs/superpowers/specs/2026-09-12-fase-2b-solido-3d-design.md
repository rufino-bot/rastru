# Fase 2B — Sólido 3D da Peça

Spec de desenho, escrita em 2026-09-12 a partir de brainstorm com o usuário.
Fonte de domínio: **regra 18** de `specs/01-dominio-e-regras-de-negocio.md` e a seção
**"Fase 2B — Sólido 3D da Peça"** de `specs/06-roadmap-mvp.md`.

## 1. O que esta fase fecha, e por que sobrou para ela

A regra 18 tem duas metades. A Fase 2 entregou só o **gancho**: a constraint
`CK_EstruturaItem_PecaTemComponente`, que garante que toda Peça referencia um `Componente` —
ou seja, que existe **onde** pendurar o sólido. Exigir o arquivo **preenchido** é validação de
aplicação (um `CHECK` não alcança outra tabela, e `ArquivoSolido` é nullable por causa do
`Componente` do tipo `Bruto`), e nasce aqui porque é aqui que nasce o upload que permite
preenchê-lo.

**A armadilha, registrada no roadmap e remedida aqui — não a redescubra:** cobrar a regra antes de o
upload existir travaria a verificação manual, porque nenhum Componente do `db/seed-demo.sql` tem
sólido. Remedido em 2026-09-12, para a spec não repassar número de terceiro: o seed insere **54**
Componentes (contagem das tuplas do `MERGE` em `dbo.Componente`) e `grep -c -i "ArquivoSolido"`
sobre o arquivo devolve **0** — a coluna não é sequer mencionada lá. A ordem dentro da fase resolve
isso: o upload vem antes da cobrança.

O que existe hoje, medido no disco em 2026-09-12:

- `dbo.Componente` tem `ArquivoSolido NVARCHAR(260) NULL` e `ArquivoFoto NVARCHAR(260) NULL`.
- A entidade `Componente` **não mapeia** nenhuma das duas, e o comentário dela escreve que isso é
  deliberado, à espera desta fase. O `ComponenteDto` também não as expõe.
- `MontagemDeEstruturaUseCase.CriarPeca` já tem comentário apontando que a segunda metade da
  regra 18 não é cobrada ali e que quem a fecha é a 2B.
- Não há nenhum upload de arquivo no sistema: esta é a primeira vez que a API recebe binário.

## 2. Decisões tomadas no brainstorm

### 2.1 O formato aceito é **STL**, e só

Palavras do usuário: *"não precisamos tratar STEP, deixe como único formato aceito o STL já que
podemos tratar nativamente com o three.js"*.

O motivo é custo de exibição, e ele é assimétrico: STL o three.js lê nativo (`STLLoader`), enquanto
STEP exigiria um parser WASM de terceiros (`occt-import-js`, na ordem de 5 MB) ou conversão no
servidor. Com o viewer sendo requisito desta fase, aceitar STEP significaria ou dois comportamentos
diferentes por extensão, ou uma dependência pesada de terceiros no caminho crítico.

**Consequência em spec de domínio, que é texto do TCC:** "STEP ou STL" aparece **4 vezes** em
`specs/` (medido em 2026-09-12 com `grep -rn "STEP ou STL" specs/ CLAUDE.md`): duas em
`01-dominio-e-regras-de-negocio.md` (a linha do glossário de `Componente.ArquivoSolido` e o corpo
da regra 18), uma no comentário da coluna em `02-modelo-de-dados.sql`, e uma em
`06-roadmap-mvp.md`, na seção de importação de BOM. **Todas as quatro mudam.**

Há **2 ocorrências adicionais** de "STEP" em `06-roadmap-mvp.md` (mesma medição), na alternativa
descartada *"STEP AP242/AP214 da montagem"* da seção de importação de BOM. Essas são sobre o
arquivo de **montagem**, não sobre o sólido do Componente, e o mérito delas não muda. **Mas uma
frase delas fica falsa:** o texto diz que o STEP da montagem *"seria o mesmo arquivo que já serve
ao sólido"* — com STL-only, não é mais. Essa frase se ajusta; o resto do argumento (parser pesado,
part number ausente) continua válido como está.

### 2.2 O binário vive em **blob, em tabela própria**, com a FK saindo de `Componente`

Decisão do usuário, em contexto novo: o deploy passa a ser numa **VPS paga com domínio próprio**
(ver §3), e não mais num servidor da empresa.

O motivo dado por ele e que se sustenta: com a FK em `Componente`, um `SELECT` sobre o catálogo
nunca arrasta a coluna pesada.

**Uma premissa dele foi corrigida no brainstorm, e a correção fica escrita porque o argumento vai
para o TCC:** blob no banco **não** economiza disco da VPS. O binário ocupa espaço igual, e um
pouco mais — overhead de página, log de transação, e o `.bak` passa a carregar os STLs. O ganho
real do blob é **operacional**, e é ele que sustenta a decisão:

- backup único: o `.bak` do banco leva os arquivos, sem segunda rotina para uma pasta;
- nada de pasta e permissão de escrita como passo de deploy manual — esquecer isso quebraria o
  upload em produção, com sintoma obscuro;
- impossível o registro divergir do arquivo: não há arquivo para alguém apagar à mão, e não há
  linha órfã apontando para caminho inexistente.

Alternativas descartadas: **disco do servidor com caminho em `ArquivoSolido`** (é o que o
`NVARCHAR(260)` insinuava, e não tocaria no schema, mas paga os três custos acima); e **blob na
própria `dbo.Componente`** (mistura catálogo com binário e exigiria cuidado em toda listagem para
não arrastar o blob — cuidado que é disciplina, enquanto a tabela separada é desenho).

### 2.3 Exibição: viewer three.js no desktop, carregado sob demanda

Quem precisa ver o sólido girando é **PCP/Administrador no desktop, ao cadastrar** — o viewer serve
para conferir que o STL subiu certo e é a peça esperada. O operador no Android não é público desta
tela (a tela dele, a fila do setor, é da Fase 3).

Disso sai a decisão técnica: o `three` entra por **import dinâmico**, carregado no clique de
"Visualizar", não no bundle principal. O Vite faz esse code-splitting sozinho. Assim o operador no
Android não paga bundle por uma tela que nunca abre.

### 2.4 Componente sem sólido aparece **marcado**, não escondido

Com a regra 18 cobrada, escolher como **Peça** um `Componente` sem sólido passa a ser recusado. Na
interface, ele **aparece na busca com marca de "sem sólido" e não é selecionável como Peça** —
continua selecionável como Item, que pode ser ad-hoc.

Escondê-lo foi descartado pelo modo de falha: o usuário procura um código que sabe que existe, não
acha, e nada lhe diz por quê. Deixar escolher e falhar só ao salvar foi descartado por obrigar a
montar a árvore inteira antes de descobrir.

**O 400 do backend continua sendo a fronteira real.** Marca de tela nunca é validação — é a mesma
regra que o `CLAUDE.md` já escreve para gating de perfil.

### 2.5 Não existe remover sólido

Há upload (que **substitui**) e download. Não há `DELETE`.

Remover o sólido de um `Componente` que já tem Peça criada violaria a regra 18 retroativamente, e o
único caso de uso real para remoção — arquivo errado — é resolvido por substituir.

## 3. Task 0 — a virada de hospedagem, antes de qualquer código

Instrução do usuário: a mudança de hospedagem entra como **Task 0 do plano**, antes do
desenvolvimento da 2B. Produto da task é **documentação; sem código**.

O que muda: o projeto deixa de ser "on-premise, servidor próprio da empresa" e passa a ser **VPS
paga, com domínio próprio** — motivo declarado pelo usuário: deixar o link de acesso do Rastru mais
"produto".

Alcance medido em 2026-09-12, com `grep -rc -i "on-premise" specs/ CLAUDE.md` — **8 ocorrências em
4 arquivos** (a métrica é *ocorrências*, não citações distintas):

| Arquivo | Ocorrências | O que há lá |
|---|---|---|
| `specs/03-arquitetura-tecnica.md` | 5 | a linha de SQL Server, a de deploy final, o título da seção de hospedagem, a de frontend estático e a de CI/CD |
| `specs/00-visao-geral.md` | 1 | a linha "Hospedagem" da tabela de contexto |
| `CLAUDE.md` | 1 | a linha de stack, "SQL Server, on-premise" |
| `specs/06-roadmap-mvp.md` | 1 | "API web on-premise", no argumento contra ler `.SLDASM` |

A última é a que **não** deve ser reescrita como as outras: ela argumenta que exigir SolidWorks
licenciado na máquina do servidor é inviável para uma API web. O argumento **fica mais forte** numa
VPS, não mais fraco. Ajuste o termo, preserve o argumento.

### 3.1 Três achados de segurança que a exposição pública destrava — registro, não conserto

A VPS pública derruba premissas que estavam deferidas **porque a rede era interna**. A Task 0 as
**registra como dívida com gatilho explícito — "obrigatório antes do primeiro deploy público"** — e
**não as implementa**: o endurecimento vira item próprio na fila, em branch separada, porque
misturar infraestrutura na branch da 2B poluiria a review de branch dela. Decisão do usuário.

1. **TLS deixa de ser melhoria e passa a ser pré-requisito de funcionamento.** O cookie de refresh
   é gravado com `Secure = true` (medido no `AuthController`), e navegador não grava cookie `Secure`
   em HTTP. Numa VPS sem TLS o login funciona e o refresh **nunca**: a sessão morre em 15 minutos
   sem renovar, e o sintoma não aponta para a causa. `UseHttpsRedirection` não existe em `src/`
   (medido: `grep -rn "UseHttpsRedirection" src/` devolve zero).
2. **`ForwardedHeaders` não existe em `src/`** — só comentários dizendo que, havendo proxy, precisa
   ser configurado. Com nginx/Caddy na frente numa VPS, o rate limit por IP do `/auth/login` vira
   **global** (todos os clientes compartilham o IP do proxy) e o log de auth grava o IP do proxy em
   vez do cliente. As duas defesas continuam de pé, mas medindo a coisa errada.
3. **A `SigningKey` está em situação melhor do que o `CLAUDE.md` sugere.** Ele a lista como dívida
   aberta, mas `JwtOptionsValidator` **já recusa no startup** exatamente o valor de placeholder
   commitado e exige no mínimo 32 bytes. O que falta é procedimento de deploy — fornecê-la por
   variável de ambiente na VPS —, não código. **A Task 0 corrige essa afirmação no `CLAUDE.md`.**

Um quarto ponto, que não é dívida nova e sim risco que muda de tamanho: o `CLAUDE.md` já registra
que **retrancar conta não tem limite** — quem sabe um nome de usuário segura a conta trancada
indefinidamente com ~20 requisições/hora. Em rede interna isso era risco de colega; na internet
pública é risco de qualquer um. **Não é para consertar nesta fase**, mas a Task 0 anota a mudança de
exposição junto do item, senão o trade-off aceito continua escrito como se o contexto fosse o mesmo.

## 4. Modelo de dados

`specs/02-modelo-de-dados.sql` é a fonte de verdade: o schema muda **primeiro** lá, e o mapeamento
EF sai do novo schema. Nada de `Add-Migration`.

### 4.1 Tabela nova: `dbo.ArquivoDeComponente`

| Coluna | Tipo | Por que existe |
|---|---|---|
| `Id` | `INT IDENTITY PRIMARY KEY` | |
| `NomeOriginal` | `NVARCHAR(260) NOT NULL` | o nome que o usuário subiu — exibição na tela e `Content-Disposition` do download |
| `Conteudo` | `VARBINARY(MAX) NOT NULL` | o STL |
| `TamanhoEmBytes` | `INT NOT NULL` | exibir tamanho sem tocar no blob. `INT` chega a 2 GB, muito acima do limite de 16 MiB desta fase |
| `Sha256` | `BINARY(32) NOT NULL` | integridade, e permite reconhecer subida repetida do mesmo arquivo |
| `CriadoEm` | `DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME())` | mesmo padrão de `Agrupamento` |
| `CriadoPorUsuarioId` | `INT NOT NULL FK → dbo.Usuario(Id)` | autoria, mesmo padrão de `Pedido` e `Agrupamento` |

O nome é **`ArquivoDeComponente`**, e não `ArquivoSolido`, de propósito: a tabela serve às duas
colunas de arquivo de `Componente`. `ArquivoFoto` está **fora do escopo desta fase** (§9), mas
quando entrar precisa só de uma FK nova e de um validador diferente — nenhuma mudança nesta tabela,
e nenhum brainstorm próprio. Custo zero hoje, caminho aberto amanhã.

### 4.2 Em `dbo.Componente`

- **Entra** `ArquivoSolidoId INT NULL`, com FK para `dbo.ArquivoDeComponente(Id)`.
- **Sai** `ArquivoSolido NVARCHAR(260)`, por `DROP COLUMN` idempotente — mesmo padrão do
  `DROP COLUMN` de `Agrupamento.Quantidade` registrado no `CLAUDE.md`.
- `ArquivoFoto` fica **intocada**.

Manter a coluna de texto ao lado da FK criaria dois lugares para olhar a mesma informação — que é
exatamente o defeito que a própria regra 18 rejeita ao descartar repetir `ArquivoSolido` em
`EstruturaItem`. A coluna nunca foi lida por código algum (a entidade não a mapeia), então o
`DROP` não tem consumidor a quebrar.

**Os `ALTER` idempotentes vão para o `CLAUDE.md`**, no padrão dos que já estão lá, para quem tiver
banco anterior. Nesta máquina o banco foi regenerado em 2026-08-04 a partir do `.sql`, então os
`ALTER` de `ArquivoSolido`/`ArquivoFoto` que o roadmap manda aplicar ao iniciar a fase são
**no-op** — não os rode esperando efeito. Os desta fase (criar a tabela, criar `ArquivoSolidoId`,
dropar `ArquivoSolido`) **não** são no-op.

### 4.3 A decisão que protege o blob é de desenho, não de disciplina

A entidade `Componente` ganha `ArquivoSolidoId` como **escalar `int?`**, e **nenhuma propriedade de
navegação** para `ArquivoDeComponente`.

Sem navegação não existe `Include` acidental que arraste `VARBINARY(MAX)` para uma listagem
paginada de catálogo — o caminho para o blob simplesmente não está lá para ser tomado por engano.
O blob é lido só pelo endpoint que serve o arquivo, por consulta própria e explícita.

## 5. API

Os dois endpoints nascem em `ComponentesController`, que já tem o molde: `[Authorize]` de classe
para leitura, `[Authorize(Roles = PerfisDeEscrita)]` — `Administrador,PCP` — para escrita.

### `POST /componentes/{id}/solido`

- `multipart/form-data`, recebido como `IFormFile`. Perfis de escrita.
- **Substitui** o sólido existente (§2.5).
- Resposta: o `ComponenteDto` atualizado, com `TemSolido` verdadeiro.
- Falhas: 404 (componente inexistente), 400 (arquivo inválido — ver §5.1), 413 (acima do limite),
  403 (perfil sem escrita).

### `GET /componentes/{id}/solido`

- `[Authorize]` só de classe: leitura é de qualquer autenticado, mesmo critério do `Obter`.
- `application/octet-stream`, com `Content-Disposition` carregando o `NomeOriginal`.
- Serve o download **e** o viewer — um endpoint, dois consumidores.
- 404 quando o componente não existe **ou** quando não tem sólido.

### 5.1 Validação, em três camadas — e a terceira é a que importa

1. **Extensão** `.stl`.
2. **Tamanho** ≤ **16 MiB** (16.777.216 bytes). O número tem raciocínio: 16 MiB / 50 bytes por triângulo ≈ 335 mil
   triângulos, malha fina de sobra para peça de metalurgia, e cabe no limite default do corpo de
   requisição do Kestrel — que é 30.000.000 bytes, ou ≈ 28,6 MiB — sem reconfiguração. A folga é
   real mas não é grande: dobrar o limite desta fase já ultrapassaria o teto do Kestrel.
3. **Estrutura de STL de verdade.** No binário: 80 bytes de cabeçalho, `uint32` com a contagem de
   triângulos, 50 bytes por triângulo — então o arquivo válido satisfaz **`tamanho == 84 + 50 × n`**,
   com `n` lido do próprio arquivo. No ASCII: abre com `solid` e contém `facet normal`.

Sem a terceira camada, um PDF renomeado para `.stl` sobe, e o defeito só aparece no viewer, longe
da causa. É ela que transforma "achamos que é um STL" em "é um STL".

### 5.2 `ComponenteDto` ganha `TemSolido: bool`

Booleano, não o id do arquivo: a tela não precisa do id — a rota do binário é pelo id do
`Componente` — e expor um id de arquivo convidaria um segundo caminho para o mesmo recurso.

**Atenção de alcance:** `ComponenteDto` é consumido por toda tela e todo teste que lista ou lê
Componente. Acrescentar o campo tem delta de teste em vários arquivos; é trabalho mecânico, mas não
é zero, e a task que o fizer deve medir o próprio delta em vez de estimá-lo.

## 6. A cobrança da regra 18

Vai em **`MontagemDeEstruturaUseCase.CriarPeca`**, exatamente onde o comentário de hoje declara que
a segunda metade da regra não é cobrada e que a 2B a fecha. O comentário sai e vira guarda.

- Criar **Peça** cujo `Componente` de origem não tem `ArquivoSolidoId` → **400**, com mensagem que
  diz o que fazer (subir o sólido no cadastro do Componente), não só que falhou.
- **Item continua livre**, porque pode ser ad-hoc. A regra é sobre Peça, e a constraint
  `CK_EstruturaItem_PecaTemComponente` já garante que Peça tem `Componente`.
- **Nada retroativo.** A regra é de criação: as Peças que já existem no banco de dev (os pedidos
  `PED-VER-001..006` de verificação manual) não são afetadas, e nenhuma tela passa a recusar o que
  já está gravado.

**Mutação que a task deve provar, não afirmar:** apagar a guarda tem de deixar vermelho um teste
que criava Peça a partir de Componente sem sólido. Se a suíte ficar verde sem a guarda, o teste não
existe — importa onde a mutação mata, não só que mata.

## 7. Front

### 7.1 Duas primitivas novas, com teste próprio

Vão para `web/src/components/`, como o `CLAUDE.md` exige, e não para dentro da tela: a
`ComponenteDetalhePage` já tem **620 linhas** (medido em 2026-09-12) porque a receita padrão mora
lá, e ela deve apenas orquestrar as duas.

- **`UploadDeSolido`** — escolhe o `.stl`, mostra nome e tamanho do que já existe, envia, e tem os
  três estados (carregando, erro via `mensagemDeErro`, sucesso). Visível só sob
  `usePodeEscrever`, com o `try/catch` do 403 obrigatório — esconder botão não é segurança.
- **`VisualizadorDeSolido`** — busca o binário, passa pelo `STLLoader` e renderiza.

O `client.ts` **não precisa de nada novo**, e isso foi medido: `apiFetch` devolve o `Response` cru e
não fixa `Content-Type` — só `Authorization` —, então serve tanto ao `FormData` (o browser põe o
boundary) quanto ao `arrayBuffer()` do viewer. Como sempre, o caminho passa **sem** o prefixo
`/api`: quem o aplica é o `rota()`.

### 7.2 O three.js entra por import dinâmico

`await import('three')` no clique de "Visualizar" — não no topo do módulo. Justificativa em §2.3: o
público do viewer é o desktop, e o bundle principal é pago também por quem abre o sistema no
Android.

### 7.3 A marca "sem sólido" no `SeletorComBusca`

O `SeletorComBusca` hoje tem as props `rotulo`, `valorSelecionado` e `aoSelecionar`, e busca
componentes por um `useBuscaPaginada` fixo — medido em 2026-09-12. Ele ganha **`exigirSolido`**,
opcional, default `false`:

- ligada, o item sem `TemSolido` aparece com marca visual e `aria-disabled`, e não é selecionável;
- ligada **só** no **formulário de criar Peça** da `AgrupamentoDetalhePage` — o do topo da tela,
  sob `podeEscrever`;
- **não** ligada no **painel de acrescentar filho, modo catálogo**, da mesma tela, porque Item pode
  ser ad-hoc.

O default `false` é o que mantém os dois consumidores atuais (`AgrupamentoDetalhePage` e
`ComponenteDetalhePage`, este último na receita padrão) com o comportamento de hoje, sem tocá-los.

### 7.4 Nenhum token de cor novo

A marca usa `tinta-fraca` e `borda`. "Sem sólido" é **ausência de dado**, não estado de negócio, e o
`CLAUDE.md` reserva verde e vermelho a aprovado/ativo e reprovado/perda/erro. Assim a fase não mexe
na paleta e não encosta na guarda de contraste, que reprovaria tom novo sem par declarado.

## 8. Testes — e o que declaradamente não é testável

O que a suíte cobre:

- **Validação de STL**, nas três camadas, com ênfase na terceira: arquivo com `tamanho != 84 + 50n`
  recusado, ASCII sem `facet normal` recusado, PDF renomeado recusado.
- **Fixture gerada em código**, não arquivo binário commitado: um cubo STL binário de 12 triângulos
  = **684 bytes exatos** (84 + 50 × 12). Serve aos testes e à verificação manual, e de quebra
  exercita a própria fórmula da terceira camada.
- **Endpoints**: perfis (403 para quem não escreve), 404, substituição, `Content-Disposition` do
  download.
- **Mapeamento EF** da tabela nova, contra o SQL Server real, como o resto do projeto. Teste novo
  que escreva em `dbo.Componente` entra na `ColecaoQueEscreveEmComponente`, e **asserção sobre
  contagem global de tabela compartilhada é flaky por construção** — escope pelo próprio prefixo.
- **Cobrança da regra 18** em `CriarPeca`, com a mutação de §6.
- **Front**: os três estados do `UploadDeSolido`; o `exigirSolido` do `SeletorComBusca` nos dois
  sentidos (marcado e não selecionável quando ligado; comportamento inalterado quando não).

**O que não é testável na suíte, e fica declarado em vez de encoberto:** o **render WebGL**. O jsdom
não tem WebGL, então nenhum teste prova que o sólido aparece na tela. O desenho se adapta a isso em
vez de fingir cobertura: o `VisualizadorDeSolido` separa o **estado da tela** (carregando / erro /
pronto, com o fetch do binário) do **render**. A suíte cobre o primeiro, com o módulo dinâmico
mockado; o canvas fica para a **verificação manual em navegador**, que é uma task da fase.

`npm run build` faz parte do ciclo, não só `npm test`: erro de tipo em `.test.tsx` quebra o build
sem quebrar a suíte.

## 9. Escopo

**Dentro:** tabela `ArquivoDeComponente`; FK `Componente.ArquivoSolidoId` e `DROP` de
`ArquivoSolido`; mapeamento EF e repositório; upload com validação; download; `TemSolido` no DTO;
cobrança da regra 18; viewer; marca no seletor; a virada de "STEP ou STL" para STL; a Task 0 de
hospedagem; verificação manual.

**Fora, e cada um por um motivo:**

- **`ArquivoFoto`** — decisão do usuário no brainstorm: só o sólido. A tabela já nasce servindo às
  duas, então a foto entra depois com uma FK e um validador.
- **Busca de peça por foto** — está em "Fora das fases" no roadmap, e o gate dela é empírico: um
  spike de 1–2 dias sobre ~20 peças reais medindo a taxa de acerto no top-3. Vale registrar o que a
  2B faz por ela sem entrar nela: a própria seção do roadmap diz que o que torna a busca viável *"e
  não era verdade antes"* é o sólido 3D ser obrigatório — e é esta fase que faz o sólido existir no
  sistema. **A 2B destrava o spike; não o executa, e não deve escorregar para lá.**
- **Endurecimento para VPS pública** (HTTPS redirection, `ForwardedHeaders`) — item próprio na
  fila, branch separada. Decisão do usuário, motivo em §3.1.
- **`DELETE` de sólido** — §2.5.
- **Importar estrutura do CAD** — outra seção "Fora das fases".

## 10. Ordem, e o que ela protege

1. **Task 0** — a virada de hospedagem para VPS (documentação, sem código).
2. **Schema + spec de domínio** — tabela, FK, `DROP`, `ALTER`s no `CLAUDE.md`, e as 4 ocorrências
   de "STEP ou STL" mais a frase do STEP AP242 de §2.1.
3. **Entidade, mapeamento EF e repositório** — sem propriedade de navegação (§4.3).
4. **Use case de upload, validação e os dois endpoints.**
5. **`ComponenteDto.TemSolido`** — alcance de §5.2.
6. **Cobrança da regra 18** em `CriarPeca`.
7. **Front: `UploadDeSolido` e `VisualizadorDeSolido`**, integrados na tela do Componente.
8. **Front: `exigirSolido`** no `SeletorComBusca`, ligado no formulário de criar Peça.
9. **Verificação manual em navegador.**

**A ordem 4 antes de 6 é a armadilha do roadmap, e o plano não deve invertê-la.** Nenhum dos 54
Componentes do `seed-demo` tem sólido; cobrar a regra antes do upload existir travaria a
verificação manual, porque não haveria como criar Peça nenhuma. Com esta ordem, a verificação
manual passa a exercitar o upload como pré-requisito — o que é melhor, não pior: o caminho que o
usuário real percorre é o mesmo.

## 11. Riscos

- **Nenhum Componente do `seed-demo` tem sólido, e isso é permanente** — o seed não muda nesta
  fase. Quem for verificar manualmente **precisa subir um STL primeiro**. A fixture de 684 bytes
  serve, e usá-la é parte da verificação, não um atalho.
- **STL real de fábrica pode passar de 16 MiB.** O limite é escolha, não lei da física: se um arquivo
  real for recusado, o número se ajusta — mas o teto do Kestrel (30 MB) entra em jogo acima disso, e
  aí deixa de ser uma constante e passa a ser configuração de pipeline.
- **O viewer é a única parte sem cobertura de suíte** (§8). Se a verificação manual for pulada,
  ninguém sabe se o sólido aparece.
- **`ComponenteDto` toca muita coisa** (§5.2): é onde o delta de teste desta fase vai concentrar.
