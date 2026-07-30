# Convenções obrigatórias da Fase 1A — o que o plano NÃO diz e o implementador precisa fazer

O plano `docs/superpowers/plans/2026-07-28-fase-1a-cadastros-planos.md` foi escrito **antes** de
qualquer task ser executada. Cada review acha defeitos que custam fix pass — e o plano, por ser
anterior a todas elas, carrega os **mesmos** buracos nas tasks seguintes. Não é descuido do plano:
é cronologia.

Este arquivo é **complementar ao brief**, e ganha dele em caso de conflito. Vale para as Tasks 9 a 11
(as 6, 7 e 8 já rodaram com ele). Ele cresce a cada review: nasceu com B1–B6 e F1–F3, das Tasks 3–5;
B7–B8 e F5 vieram da review da Task 6; F4 da review da Task 7; **B9–B10 da Task 8** (B9 do revisor,
B10 do implementador). **Se a sua review achar algo novo, o lugar de registrar é aqui, não só no
relatório.**

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

---

## Frontend (Tasks 7, 9, 11)

### F1. `required` nos inputs de campo obrigatório
Sem ele, submeter vazio manda a requisição, o backend responde 400 e a tela mostra a mensagem
genérica de falha ("Não foi possível salvar…") — mensagem errada para campo em branco. Precedente:
`web/src/pages/LoginPage.tsx:49,61`. O plano não tem `required` em nenhuma tela.

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

- Backend: **212 testes** (83 Application + 25 Infrastructure + 104 Api) ao fim da Task 8, medidos e
  conferidos pelo controlador e pelo revisor de forma independente. `-warnaserror` sempre em 0 warnings.
  (Era 182 = 72 + 23 + 87 ao fim da Task 6; a Task 8 somou +30.)
- Frontend: **21 testes** ao fim da Task 7 e do fix pass dela. O `npm run lint` tem **1 warning pré-existente
  e alheio** (`web/src/auth/AuthContext.tsx:48`, `react/only-export-components`, da Fase 0) — não é seu,
  não tente corrigir.
- `git status` tem três sujeiras **alheias e permanentes**: `.claude/settings.local.json` modificado, e
  `.github/`/`.vs/` untracked (o usuário abriu a solution no Visual Studio). **Não commite nenhuma delas.**

## Como esta lista foi construída
Cada item aqui custou um fix pass ou uma review. Nenhum saiu de opinião: **B1–B6** vieram das reviews
das Tasks 3, 4 e 5; **B7–B8** e a nota do baseline vieram da review da Task 6. O padrão que os une é
que **quase todos foram achados por mutação, não por leitura** — apagar uma guarda e ver a suíte seguir
verde. Se você for revisar uma task desta fase, mutar é o que encontra o que ler não encontra.
