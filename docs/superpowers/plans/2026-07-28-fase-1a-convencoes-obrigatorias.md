# Convenções obrigatórias da Fase 1A — o que o plano NÃO diz e o implementador precisa fazer

O plano `docs/superpowers/plans/2026-07-28-fase-1a-cadastros-planos.md` foi escrito **antes** de
qualquer task ser executada. Cada review acha defeitos que custam fix pass — e o plano, por ser
anterior a todas elas, carrega os **mesmos** buracos nas tasks seguintes. Não é descuido do plano:
é cronologia.

Este arquivo é **complementar ao brief**, e ganha dele em caso de conflito. Vale para as Tasks 11 e 12
(as 6 a 10 já rodaram com ele). Ele cresce a cada review: nasceu com B1–B6 e F1–F3, das Tasks 3–5;
B7–B8 e F5 vieram da review da Task 6; F4 da review da Task 7; **B9–B10 da Task 8** (B9 do revisor,
B10 do implementador); **F6 e a nota do F1, da Task 9** (as duas do revisor); **a nota da prova falsa
no B8, do implementador da Task 10**, e **B11–B14 mais a nota nova do B10, do revisor dela**. **Se a
sua review achar algo novo, o lugar de registrar é aqui, não só no relatório.**

**Estado da Task 10 (fechada):** a auditoria dela contra este arquivo apontava falha em **B1, B2, B4,
B5, B6, B7, B8, B9 e B10** — todos menos o B3, que não se aplica (Agrupamento não tem coluna `Ativo`).
O commit `e69014a` fechou **as nove**, medidas uma a uma por mutação na review. No **domínio** ela é a
task mais cuidadosa do plano (valida `Quantidade <= 0`, o CHECK do `Tipo`, os três ramos do `Excluir`,
o `TemEstruturaAsync` parametrizado); era nas convenções que estava descoberta. **B11–B14 nasceram da
review dela** e nenhum é regressão da própria task: são buracos do molde que só ficaram visíveis ali.

---

## Backend (Tasks 6, 8, 10)

### B1. Todo teste de falha de use case afirma `Saves == 0`
Não basta afirmar a forma do `Result`. Um use case que devolvesse a falha certa **e ainda assim**
chamasse `SalvarAlteracoesAsync` passaria. Já foi corrigido duas vezes (ledger da Fase 0 e Task 3);
o plano ainda escreve testes de conflito no `Editar` sem essa asserção.

### B2. Existe teste provando que `Editar` PERSISTE
Ou seja, um `Assert.Equal(1, repo.Saves)` num caminho de sucesso do `Editar`. Na Task 3, nenhum dos 9
testes do brief alcançava a linha do save — o implementador achou o buraco e fechou. O plano repete a
omissão: `Saves == 1` aparece só em `Cadastrar` e `DefinirAtivo`.

### B3. `DefinirAtivo` cobre os TRÊS ramos
Inativar (`ativo: false`), **reativar** (`ativo: true`) e **`NaoEncontrado`** (id inexistente). O XML
doc que o próprio plano manda escrever afirma "Cobre inativar e reativar" — afirmação que os testes do
plano não sustentam. Foi o achado I2 da review da Task 3.

### B4. `RegistroDeDependenciasTests` ganha o par repositório + use case
Em `tests/Rastreamento.Api.Tests/RegistroDeDependenciasTests.cs`, acrescentar dois `[InlineData]`:
`typeof(IXRepository)` e `typeof(CadastroDeXUseCase)`. O XML doc dessa classe já pede isso
explicitamente ("Cada entidade nova acrescenta aqui o par repositorio + caso de uso"), e o plano não
faz em nenhuma das três tasks. Esse teste é o que prova que `Editar`/`DefinirAtivo` compartilham o
mesmo `DbContext` do `SaveChanges`; sem a linha, um registro `Transient` passaria em silêncio.

### B5. Autorização de escrita coberta por `[Theory]` sobre `(metodo, rota)`
O plano cobre role só no `POST`. Apagar `[Authorize(Roles = "Administrador")]` do `PUT` ou do `PATCH`
deixaria a suíte verde — confirmado por mutação na Task 4, e fechado no commit `e133748`.
**O molde já está no repo:** copie `Operador_nao_escreve_em_setor` de
`tests/Rastreamento.Api.Tests/SetoresEndpointsTests.cs`. Ele usa `HttpRequestMessage` + `SendAsync`
com `HttpMethod(metodo)` dinâmico, e a propriedade que o faz funcionar está comentada lá: o filtro de
autorização roda **antes** do model binding, então uma rota com id inexistente responde 403 e não 404
— por isso o teste não precisa criar nada no banco.

### B6. O fake do repositório registra a divergência de collation
`ObterPorCodigoAsync`/`ObterPorNomeAsync` no fake comparam com `==` do C#, que é **case-sensitive**; a
collation padrão do SQL Server é **case-insensitive**, e os índices `UQ_*` também. Produção **não**
diverge (o EF traduz para `WHERE Codigo = @p`, avaliado sob a collation da coluna) — a lacuna é só de
fidelidade de teste, e a camada Application não consegue provar essa propriedade. Ponha a divergência
num comentário XML no fake, como `FakeSetorRepo` já faz. **Não** torne o fake case-insensitive: isso
simularia o banco e esconderia a lacuna.

### B7. O ramo de FALHA do `PUT` tem teste via HTTP
Um `PUT /<recurso>/999999` esperando **404**. Sem ele, nada exercita o
`resultado.Sucesso ? Ok(...) : TraduzirFalha(...)` do controller: o revisor da Task 6 trocou o corpo do
`Editar` por `return Ok(resultado.Valor);` — ignorando `resultado.Sucesso` por completo — e os 178
testes seguiram verdes. O molde de Setor tem (`Editar_setor_inexistente_responde_404`); Material saiu
sem, e foi fechado no fix pass. Teste de use case **não** cobre isso: o buraco é na tradução do
`Result` em status HTTP, que só existe no controller.

### B8. CADA `[MaxLength(n)]` do DTO tem teste que morre se o atributo sair
Um request com `n+1` caracteres no campo deve responder **400**, não estourar `SqlException` → **500**.
Provado por mutação na Task 6: removendo `[MaxLength(50)]` do `Codigo`, os 178 testes seguiam verdes —
os três campos de `NovoMaterialDto` estavam descobertos. Um `[Theory]` sobre `(campo, tamanho)` cobre o
DTO inteiro de uma vez.

**Achado da Task 10 — cuidado com a prova FALSA em campo de lista fechada.** Um `[MaxLength(n)]`
sobre um campo que o use case já valida contra uma lista fechada **não é testável neste nível**: um
valor com `n+1` caracteres também está fora da lista, então o 400 chega pelo outro caminho e o teste
passa com o atributo removido. Medido em `NovoAgrupamentoDto`: removendo os **dois** `[MaxLength]`,
só o caso de `Codigo` morre — o de `Tipo` (validado contra `Kit | Avulso`) seguia verde. Escrever o
`[InlineData]` de `Tipo` seria pior que não escrever: daria ao revisor a impressão de que o atributo
está coberto. **Faça:** cubra só os campos de texto livre e registre no comentário do teste, com a
medição, por que os de lista fechada ficaram de fora. O atributo continua no DTO (defesa em
profundidade e espelho da coluna), mas sem teste que o falsifique.

**Cuidado com o argumento que já falhou uma vez:** o implementador da Task 6 dispensou esse teste
alegando que ele colidiria com o problema do alvo do atributo (`[property:]`). Não procede — o revisor
escreveu o teste e ele passou de primeira. Provar que o **alvo** do atributo está certo (pôr
`[property:]` e ver POSTs virarem 500) é uma propriedade **diferente** de provar que o **limite**
funciona. As duas precisam de teste; uma não substitui a outra.

### B9. A guarda de nulidade de `LocalizarDuplicado` tem que ser provada, não só escrita
Hoje ela **não é**, em nenhuma das três entidades. O revisor da Task 8 trocou o `Normalizar(numero)`
por um `.Trim()` pelado em `CadastroDePedidoUseCase` e **os 26 testes de Pedido seguiram verdes**.
Medido depois nos outros dois: os 5 call sites de `LocalizarDuplicado` na suíte passam literais
(`"Solda"`, `"CH-001"`, `"PED-001"`) — nenhum passa `null`.

Ou seja: a proteção que o XML doc de `Normalizar` descreve ("o desserializador de JSON entrega `null`
mesmo em propriedade não-anulável") existe por **disciplina de código**, e some no dia em que alguém
a remover. É exatamente a classe de defeito que este adendo inteiro existe para pegar.

Não é alcançável em produção hoje — o único call site, o delegate `Duplicado()` do controller, só
dispara depois de o use case já ter rejeitado nulo/vazio na checagem de campo obrigatório. Por isso
não bloqueou a Task 8. Mas a Task 10 copia esta mesma forma de método para `Agrupamento`, e seria a
quarta cópia sem prova.

**Faça:** um teste direto, `await useCase.LocalizarDuplicado(null!, ct)`, afirmando que não lança.
Na Task 10, escreva para `Agrupamento` **e** feche retroativamente para Setor, Material e Pedido —
é uma linha por entidade, e fechar a fonte comum é mais barato que pagar o mesmo achado quatro vezes
(mesma lição do `[..40]` e do próprio nascimento deste adendo).

### B10. Mutar `[Authorize(Roles = ...)]` num verbo de escrita MEXE NO BANCO — limpe depois
Achado do implementador da Task 8, e é aviso de segurança, não estilo. Removendo o atributo de role
de um `POST`, **sobra o `[Authorize]` de classe**: o request passa a autenticação, a action roda e a
escrita **acontece de verdade** (201 + linha criada). O teste morre por "403 esperado vs 201", não
por um flip inofensivo de status. Ele produziu uma linha órfã em `dbo.Pedido` e teve que apagar na mão.

**Na Task 10 isso é pior:** `Agrupamento` tem **três** verbos de escrita, e um é `DELETE`. A mesma
mutação apagaria dado real em vez de criar.

Confira o banco antes e depois de qualquer mutação de autorização, e deixe-o como encontrou:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Pedido; SELECT COUNT(*) FROM dbo.Agrupamento;"
```

Nem toda mutação de escrita suja o banco — depende de onde o request morre. Medido na Task 8: a do
`PUT` não sujou (a rota usa id inexistente, e o `NotFound` corta antes da escrita) e a do `[MaxLength]`
também não (o SQL Server rejeitou o INSERT de 31 chars). A do `POST` sujou. Confira, não deduza.

**Medido de novo na Task 10, e o mecanismo é outro:** nenhuma das três mutações de autorização sujou,
e não foi sorte. O `[Theory]` usa `999999`, e no `POST` a rota é **aninhada**
(`/pedidos/999999/agrupamentos`) — o use case corta em "Pedido não encontrado" antes de escrever.
Com um `pedidoId` real, a mutação do `POST` teria criado linha, igual à Task 8. A rota aninhada é o que
protege ali; a regra "confira, não deduza" continua valendo, o que muda é o motivo.

### B11. Prova de autoria com **um** usuário no seed é degenerada
Achado da review da Task 10, e é molde-wide, não regressão de uma task. Trocar `usuarioId.Value` por
um literal `1` no `AgrupamentosController` deixa **124/124 verdes**. O mesmo em `PedidosController.cs:35`
também sobrevive — é o molde da Task 8.

O motivo não é o teste estar mal escrito: ele faz a coisa certa, lê o Id real do banco via
`IdDeUsuarioReal()` em vez de hardcodar. É que `db/seed.sql` cria **um** usuário (`admin`) e o Id dele
é `1` — então qualquer constante `1` coincide com o Id real e passa. A prova sólida da autoria vive na
camada Application (`UsuarioDaSessao = 42`), e essa mata a mutação.

**Faça:** um segundo usuário no `db/seed.sql` com `Id != 1`, e emita o token dos testes de autoria com
ele. Aí o Id real e o literal deixam de coincidir e o nível HTTP passa a provar alguma coisa. Vale para
`Pedido` **e** `Agrupamento` — fix de molde, candidato à Task 12.

### B12. `LocalizadorDeDuplicado` exclusiva de um verbo precisa do teste de conflito **naquele** verbo
Achado da review da Task 10 — e mostra o limite do B7. Substituir o corpo inteiro de
`DuplicadoNoPedidoDe` (`AgrupamentosController.cs:80-87`) por `ct => Task.FromResult<ValorDuplicadoDto?>(null)`
deixa **254 verdes**: não existe teste que faça `PUT /agrupamentos/{id}` responder 409 de duplicado.

Por que só aparece em `Agrupamento`: Setor, Material e Pedido usam o **mesmo** `Duplicado(...)` no POST
e no PUT, então o teste de POST-409 já exercita o corpo do delegate. `Agrupamento` é o único com um
delegate **exclusivo do PUT** — na edição o `PedidoId` não vem da rota, daí a busca extra — e nada o
executa.

O B7 pede "o ramo de falha do `PUT`", e foi entregue como 404. **404 e 409 são ramos diferentes**, e o
409 é o único que executa o código novo. **Faça:** quando o localizador de duplicado não for
compartilhado com o POST, escreva o teste de conflito no próprio verbo.

### B13. A guarda de claim `sub` ausente precisa do teste em **cada** controller que a copia
Mesma família do B9: guarda escrita certo que sobrevive por disciplina, não por teste. Trocar
`if (usuarioId is null) return Unauthorized();` por `usuarioId ?? 0` no `AgrupamentosController.cs:37`
deixa **254 verdes**.

Não é buraco de segurança — o `[Authorize]` e o filtro de role já rodaram. É que a regressão daria
**500** (violação de `FK_Agrupamento_CriadoPorUsuario` com `usuarioId = 0`) onde deveria dar **401**,
que é a mesma classe de defeito do B8.

O molde já tem o teste: `PedidosEndpointsTests.Cadastrar_com_token_sem_a_claim_sub_responde_401`, com
`TokenDeTeste.TokenSemAClaim`. A Task 10 copiou a guarda e não copiou o teste. **Faça:** uma linha por
entidade que use `UsuarioDaSessao()`.

**Nota do fix pass — em rota aninhada a mutação é detectada por 404, não por 500.** Achado não previsto,
e importa para quem copiar este teste. Em `Agrupamento` a rota é `/pedidos/{pedidoId}/agrupamentos`, e o
teste usa `999999` (o padrão do arquivo, para não sujar o banco): sem a guarda, o use case corta em
"Pedido não encontrado" **antes** de tocar a FK, então a mutação aparece como `404 ≠ 401`. O teste mata
a mutação certa e só ela — é válido —, mas o 500 do parágrafo acima só se manifesta com um `pedidoId`
**real**. Em rota não-aninhada (`POST /pedidos`, o molde original) a violação de FK acontece de verdade.
Ou seja: a cópia é mais fraca que o molde, e sabê-lo é o que evita concluir "provei o 500" quando não se
provou. Se quiser a prova forte, crie o Pedido antes e aceite a linha no banco (o `DisposeAsync` limpa).

### B14. Validação de domínio **sem** CHECK correspondente no DDL merece teste no nível HTTP
Achado da review da Task 10. Removendo `if (quantidade <= 0)` do `CadastroDeAgrupamentoUseCase.cs:148`
morrem só os 2 `[InlineData]` de Application — os 124 testes de Api seguem verdes.

A assimetria a corrigir não é de estilo, é de risco. `Tipo` tem teste HTTP (`Tipo_invalido_responde_400`)
**e** `CK_Agrupamento_Tipo` como rede no DDL: o pior caso de uma regressão ali é um 500. `Quantidade` é
`DECIMAL(18,4)` **sem CHECK nenhum** — a guarda da aplicação é a única defesa, e o pior caso de uma
regressão é **dado inválido persistido em silêncio**, que é estritamente pior.

**Faça:** onde o banco não tem rede de segurança, a validação merece a prova mais forte (HTTP), não a
mais fraca. Onde tem, o teste de Application basta.

---

## Frontend (Tasks 7, 9, 11)

### F1. `required` nos inputs de campo obrigatório
Sem ele, submeter vazio manda a requisição, o backend responde 400 e a tela mostra a mensagem
genérica de falha ("Não foi possível salvar…") — mensagem errada para campo em branco. Precedente:
`web/src/pages/LoginPage.tsx:49,61`. O plano não tem `required` em nenhuma tela.

**Nota da review da Task 9:** `required` **não é provável neste nível de teste**. O revisor removeu o
atributo dos dois inputs de `PedidosPage.tsx` e a suíte seguiu 30/30 — sem `@testing-library/react`
não há como simular submit de formulário e observar o bloqueio do navegador. A convenção continua
obrigatória, mas quem a mantém é disciplina de código, não teste. Mesma classe do B9: a proteção existe
porque foi escrita certo, não porque algo quebraria se sumisse.

### F2. `try/catch` em todo handler que chama endpoint `[Authorize(Roles = ...)]`
O gating do link por perfil foi cortado de propósito: o link aparece para todos e o 403 do backend é a
fronteira real. Consequência: um Operador clicando "Inativar" pega 403 → `throw` → **promise rejeitada
sem tratamento**, e a tela não diz nada. Reuse o estado `erro` que a página já tem; não crie mecanismo
novo (a camada global de UX de erros está no backlog, fora da 1A).
O risco concreto está na **Task 7**: `alternarAtivo`/`reativar` do `MateriaisPage.tsx` (plano, ~linhas
2498-2509) chamam `definirAtivoMaterial` sem tratamento, e `PATCH /materiais/{id}/ativo` é
`[Authorize(Roles = "Administrador")]`. Executar o plano literalmente reintroduz o bug que a Task 5
acabou de corrigir.

### F3. Teste de `listar*(true)`, não só `(false)`
Achado por mutação na review da Task 5: hardcodar `incluirInativos=false` na URL mantinha os 12 testes
verdes, porque o único teste chamava sempre com `false`. Quebraria o checkbox "Mostrar inativos" em
silêncio. Cubra os dois valores e assere a **URL**, não só o retorno.

### F4. Toda função nova do módulo de API tem DOIS testes, não um
1. **URL, método e corpo** — asserindo `fetchMock.mock.calls[0][0]` e o `init`, não só o retorno;
2. **`rejects.toThrow()` em resposta não-ok** — obrigatório quando a função bate em endpoint
   `[Authorize(Roles = ...)]`, o que hoje vale para todo `PATCH /{id}/ativo` e para o
   `DELETE /agrupamentos/{id}` da Task 10.

O segundo é o que impede o `try/catch` do F2 de ser **decorativo**. Sem `@testing-library/react` não há
teste de componente, então nada prova que a tela exibe o erro — mas dá para provar que a função **lança**,
e se ela não lançar o `catch` nunca dispara e o F2 não vale nada. É a metade da propriedade que este
nível de teste alcança; escreva essa metade.

Como isso apareceu: o brief da Task 7 não pedia teste nenhum para `definirAtivoMaterial` — método
trocado (`POST` por `PATCH`), URL errada, corpo errado ou `throw` ausente passariam **todos** verdes. O
implementador achou e escreveu os dois. Então o revisor aplicou a mesma mutação **na direção contrária**,
em `definirAtivoSetor` (Task 5), e a suíte seguiu 20/20 — o `try/catch` de `SetoresPage.tsx` estava tão
decorativo quanto. Fechado por fix pass.
Cuidado com a confusão que já aconteceu: um teste de URL/método/corpo **não** cobre comportamento em
erro. São os dois, não um.

### F5. Não copie a versão antiga de `lerOuFalhar`
Ela foi endurecida depois da Task 5: um 409 fora do formato `ValorDuplicado` agora **lança** em vez de
devolver. Motivo: `CadastroControllerBase.TraduzirResultado` produz um 409 pelado
(`{ erro: "<codigo>" }`) — caminho do `PATCH /{id}/ativo` e do `DELETE /agrupamentos/{id}` —, e com o
código antigo `ehConflito` devolvia `false` e o chamador tratava o conflito como sucesso: campo limpo,
lista recarregada, **nenhum erro visível**. Use a versão que está em `web/src/api/cadastros.ts`.

### F6. Mock de erro no teste de throw precisa de corpo JSON não-vazio, não `''`
Um mock como `new Response('', { status: 500 })` parece provar a guarda `if (!resp.ok) throw`, mas
**não prova nada** quando a função chega a chamar `.json()` depois da guarda: corpo vazio faz o
`.json()` lançar sozinho (`SyntaxError: Unexpected end of JSON input`), e `rejects.toThrow()` sem
argumento passa de qualquer jeito — pelo parse falho, não pela guarda. Se a guarda sumir, o teste
continua verde.

**A evidência (mutação da review da Task 9):** removendo `if (!resp.ok) throw` de `lerOuFalhar`
(cobre `criarSetor` e `criarPedido`), de `listarPedidos` e de `obterPedido` — três mutações
separadas, cada uma revertida em seguida — a suíte seguia **30/30 verde** nos quatro testes que
deveriam ter morrido. Contraprova: trocando o corpo do mock de `''` para `'{}'` com a guarda ainda
removida, o teste morre — o corpo não-vazio é o que destrava a prova. Controle de escopo: a mesma
mutação em `definirAtivoSetor` (que **não** chama `.json()`, é `void`) matou 1 teste normalmente — a
lacuna é específica de quem lê o corpo.

**Exceção:** funções que retornam `void` e nunca chamam `.json()` — `definirAtivoSetor` e
`definirAtivoMaterial` — não têm esse problema. Corpo vazio ali é inofensivo porque nada tenta
fazer parse dele; **não** troque esses dois mocks, seria ruído.

**Regra:** todo teste de `rejects.toThrow()` sobre uma função que chama `.json()` no caminho de
sucesso — o que hoje inclui toda função `lerOuFalhar`-backed e todo `listar*`/`obter*` — usa corpo
de mock JSON válido e não-vazio (`'{}'` basta). Antes de aceitar um teste desses como prova, remova
a guarda correspondente e confirme que ele morre; se não morrer, o mock está mascarando.

**Na Task 11:** as três funções novas do módulo de `Agrupamento` —
`listarAgrupamentos`/`criarAgrupamento`/`excluirAgrupamento` — precisam do par do F4, e o teste de
throw de cada uma já nasce no formato certo (corpo JSON não-vazio, não `''`). `excluirAgrupamento` é
o caso mais delicado: `DELETE /agrupamentos/{id}` responde 409 num formato pelado (`{ erro:
"<codigo>" }`, via `TraduzirResultado` — ver F5), que **não** é `ConflitoDeCadastro`. Se
`excluirAgrupamento` usar `lerOuFalhar`, esse 409 já lança pela guarda de formato inesperado (linha
32); se usar um `if (!resp.ok) throw` próprio como `definirAtivoSetor`, confirme que o 409 pelado
também cai nesse caminho — não assuma, os dois formatos de tradução (`TraduzirFalha` vs
`TraduzirResultado`) não são intercambiáveis.

---

## Formas que o plano escrevia erradas e já foram corrigidas no arquivo (commit `6e9029a`)
Se você encontrar alguma delas, é bug — não "conserte de volta":

| Forma errada | Por que quebra | Forma certa (já no repo) |
|---|---|---|
| `[property: MaxLength(n)]` em record posicional | MVC lê a metadata do parâmetro do construtor e **lança** ao achá-la na propriedade: todo POST/PUT vira 500, inclusive o caminho felizmente | `[MaxLength(n)]` sem alvo — `AuthController.cs:40` |
| `$"x-{Guid.NewGuid():N}"[..40]` | A string tem 36–38 chars e o range vai a 40 → `ArgumentOutOfRangeException`. O slice também era inútil (`NVARCHAR(50)`/`(100)`) | sem slice. Os `[..25]` das Tasks 8/10 **ficam**: `Pedido.Numero` é `NVARCHAR(30)` contra 36 chars |
| `React.FormEvent` | Com `jsx: "react-jsx"` + `verbatimModuleSyntax` e sem `import React`, o `tsc -b` falha com "Cannot find name 'React'" | `import { ..., type FormEvent } from 'react'` — `LoginPage.tsx:1` |

## Baselines (confira antes de mexer, para poder dizer "N antes, N+k depois")
Rode a suíte **antes** de tocar em nada e confirme o número. Se divergir, pare e reporte em vez de
assumir — contagem de brief desatualizada já apareceu em três tasks.

- Backend: **258 testes** (103 Application + 27 Infrastructure + 128 Api) ao fim da Task 10 **com o fix
  pass de B12/B13/B14**, medidos pelo controlador e pelo revisor de forma independente. `-warnaserror`
  sempre em 0 warnings. (Era 254 = 103 + 27 + 124 no commit `e69014a`, 212 = 83 + 25 + 104 ao fim da
  Task 8, e 182 = 72 + 23 + 87 ao fim da Task 6; a Task 10 somou +42 e o fix pass dela +4.)
- Frontend: **32 testes** / 3 arquivos ao fim da Task 9 e do fix retroativo do F6. (Era 21 ao fim da
  Task 7; a Task 9 somou +9 e o fix do F6 somou +2.) O `npm run lint` tem **1 warning pré-existente
  e alheio** (`web/src/auth/AuthContext.tsx:48`, `react/only-export-components`, da Fase 0) — não é seu,
  não tente corrigir.
- `git status` tem três sujeiras **alheias e permanentes**: `.claude/settings.local.json` modificado, e
  `.github/`/`.vs/` untracked (o usuário abriu a solution no Visual Studio). **Não commite nenhuma delas.**

## Como esta lista foi construída
Cada item aqui custou um fix pass ou uma review. Nenhum saiu de opinião: **B1–B6** vieram das reviews
das Tasks 3, 4 e 5; **B7–B8** e a nota do baseline vieram da review da Task 6; **B9–B10** da Task 8;
**B11–B14** da Task 10. O padrão que os une é que **quase todos foram achados por mutação, não por
leitura** — apagar uma guarda e ver a suíte seguir verde. Se você for revisar uma task desta fase,
mutar é o que encontra o que ler não encontra.

Vale notar o que a Task 10 acrescentou ao padrão: os quatro achados novos são de **código que a suíte
nunca executa** (um delegate inteiro, uma guarda de claim) ou de **prova que coincide por acidente**
(o autor `1` que bate com o único usuário do seed). Nenhum deles é código errado — todos passam na
leitura. É a diferença entre "o código está certo" e "o teste travaria o código se ele deixasse de
estar".
