# Fase 5C — Ajustes da conferência (F1–F3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** fechar os três achados da conferência da Fase 5C no navegador (2026-11-16) e, junto do terceiro, a sobra em
montagem por Setor na Fila, antes da review de branch inteira.

**Architecture:** três tasks independentes. F1 só no front (a linha do filho no cartão de montagem da Fila). F2 no
contrato da prévia da baixa (`SobraDaBaixaDto` e `TravaNoTopoDto` ganham o código) e nos dois componentes que a mostram.
F3 só na consulta da Fila (`ConsultaDeExecucaoUseCase`): quem entra em "Aguardando montagem" e quanto a linha de sobra em
montagem mostra em cada Setor.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core (Database First), React + TypeScript + Vite, Vitest + Testing Library.

**Origem das decisões:** os achados e as decisões do usuário estão no ledger privado do projeto (não no repositório), e o
que vale para estas tasks está copiado aqui. A spec da fase é `docs/superpowers/specs/2026-11-12-fase-5c-pontas-do-kit-design.md`.
Nenhum dos três achados nasceu na 5C: entram nesta onda por decisão do usuário.

## Global Constraints

- **Branch:** `claude/happy-archimedes-jbyrvq`. Commits nela, **sem push**. Mensagem em português, terminando com
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Gate inteiro de `superpowers:subagent-driven-development` em cada task: implementer → review da task → fix de
  Critical/Important → re-review. A review de branch inteira vem **depois** das três, uma vez só.
- **Schema:** nenhuma task muda o schema. Se achar que precisa, pare e reporte (`BLOCKED`).
- **Livro só de inclusão** (`dbo.Movimentacao`) e conservação de quantidade (regra 9 de `specs/01`). Nenhuma task grava
  movimento novo: F3 muda uma consulta, não uma escrita.
- Seção «Interface» do `CLAUDE.md` inteira: primitivas de `web/src/components/`, `data-testid` só sem papel nem texto,
  cores só por token. Sobra e montagem são texto **neutro**: nenhum `positivo`, `negativo`, `atencao` ou `atraso` nestas
  tasks.
- Teste de tela com `// @vitest-environment jsdom` no topo, `afterEach(cleanup)` explícito e `web/src/testes/api.ts`
  quando houver `fetch`.
- **Front:** em `web/`, `npm test -- --run --maxWorkers=2` **e** `npm run build` no fim de toda task que toca o front (o
  Vitest não faz typecheck).
- **Backend:** `dotnet build Rastreamento.slnx -warnaserror` (0 warnings) e `dotnet test Rastreamento.slnx -m:1`, **uma
  execução por vez** (duas simultâneas contra o mesmo banco dão deadlock). O SQL Server do `docker compose` está no ar. A
  API local **não** está rodando durante estas tasks (o controlador a parou); não a suba. O banco tem a massa da
  conferência com ações do usuário: **nenhum script de zerar nem de massa roda**.
- **Specs andam junto:** frase que a prosa cita muda junto (`specs/01`, `specs/04`, `specs/05`, `CLAUDE.md`). Texto novo
  de spec vira texto do TCC: só afirme o que mediu. **Contagens do `CLAUDE.md`** (testes, usos de primitiva) **não** se
  atualizam nestas tasks: a remedição é do fim da branch.
- Citação por nome, nunca `arquivo.ext:NN` nem distância relativa. **Comentário de código não cita o ledger, achado
  ("A1"), ajuste ("F2") nem "Task N"**: cite a spec, a regra numerada de `specs/01` ou a decisão pelo nome.
- Nomes de domínio em português, espelhando o DDL; técnicos em inglês.

---

### Task 1: F1 — a linha do filho no cartão de montagem diz só "faltam N"

**O que o usuário viu:** na Fila da Solda, no cartão de "Aguardando montagem", cada filho aparece como
"PF-3010 — Longarina de perfil U 1000 mm cortada: 6 aqui, 2 por unidade — falta 2 de 8 para a próxima".

**Decisão do usuário (2026-11-16):** o final vira só **"— faltam 2"**, sem o total ("de 8") e sem o "para a próxima".
Nas palavras dele: "acho que fica muito poluído". Concordância: com 1, **"— falta 1"**; com 2 ou mais, **"— faltam N"**.
O resto da linha não muda ("PF-3010 — Longarina …: 6 aqui, 2 por unidade").

**Decisão do controlador:** o contrato **não** muda. `necessarioParaProxima` continua no `FilhoNaMontagemDto`: o front
o usa também como sinal de "há próxima" (nulo quando não há), e o backend e os testes dele continuam iguais. A tela só
para de escrever o número.

**Fora do pedido (não mexer):** o `CartaoDeKit` das Tarefas ("falta N para 1 conjunto") e a `SeparacaoPage`
("falta N {un}").

**Files:**
- Modify: `web/src/pages/FilaDoSetorPage.tsx` (o texto do filho no cartão de montagem)
- Test: `web/src/pages/FilaDoSetorPage.test.tsx` — o teste *"aguardando montagem diz quanto dá para iniciar e o que falta
  para a próxima"* fixa a frase antiga e muda junto (renomeie o `it` se o nome deixar de descrever o que testa). O caso
  "filho que já basta não mostra falta 0" continua.
- Modify (prosa): `specs/01-dominio-e-regras-de-negocio.md` e `specs/06-roadmap-mvp.md` citam o "falta X de Y para a
  próxima". Em `specs/01`, atualize para a frase nova. Em `specs/06`, o trecho narra o que a fase entregou: atualize só se
  ele descreve a tela de hoje, não se narra o passado, e diga no relatório qual foi o caso.

- [ ] **Step 1:** Escreva o teste vermelho: a linha com falta 2 diz "— faltam 2" e **não** contém "de " nem "para a
  próxima"; um segundo caso com falta 1 diz "— falta 1". Rode e veja falhar.
- [ ] **Step 2:** Mude a frase na `FilaDoSetorPage`. Rode o arquivo de teste e veja passar.
- [ ] **Step 3:** Prosa (`specs/01`, e `specs/06` se couber).
- [ ] **Step 4:** `npm test -- --run --maxWorkers=2` e `npm run build`, em `web/`.
- [ ] **Step 5:** Commit: `fix(fase-5c): a linha do filho no cartao de montagem diz so quanto falta`.

---

### Task 2: F2 — o código do componente na sobra e na caixa do topo da prévia da baixa

**O que o usuário viu:** no "Dar baixa", o bloco "O que acontece" mostra as duas listas de cima com o código
("1 de PF-3010 — Longarina de perfil U 1000 mm cortada, aguardando a montagem do pai em Solda"), mas a lista
"Ficam onde estão e viram sobra:" só com a descrição ("1 de Longarina de perfil U 1000 mm cortada", "1 de Travessa de
perfil U 100 mm cortada"). Nas palavras dele: "fiquei procurando o PF-3020 até perceber que está só o nome dele". A caixa
do topo também só tem a descrição: "Isso impede montar 1 Chassi soldado do transportador 6 m. Dar baixa também delas".

**Decisão do usuário (2026-11-16):**
1. A lista "viram sobra" traz o código, no mesmo formato das outras duas listas: **"1 de PF-3020 — Travessa de perfil U
   100 mm cortada"** (o rótulo de `rotuloDoNo`, código, travessão, descrição).
2. A caixa do topo também: **"Isso impede montar 1 MT-1010 — Chassi soldado do transportador 6 m. Dar baixa também
   delas"**.

**Não é só front:** `SobraDaBaixaDto` (hoje `estruturaItemId`, `descricao`, `quantidade`) e `TravaNoTopoDto` (hoje
`pecaId`, `descricao`, `quantidade`, `aIniciar`) **não trazem o código**. Os dois ganham `Codigo` (o código do Componente
do nó, o mesmo que a `LinhaDaBaixaDto` já manda; siga como ela o obtém). O nó de Item copiado do catálogo tem
Componente; se algum caminho puder dar nó sem Componente, siga o que a `LinhaDaBaixaDto` faz nesse caso e diga no
relatório o que achou.

**Files (procure pelo nome; a lista é o ponto de partida, não a fronteira):**
- Modify: o DTO da prévia da baixa no backend (onde estão `SobraDaBaixaDto`, `TravaNoTopoDto` e `LinhaDaBaixaDto`) e quem
  os monta (a prévia em `src/Rastreamento.Application/Baixa/`).
- Modify: `web/src/api/baixas.ts` (os dois tipos ganham `codigo: string`, com o mesmo tipo que a linha usa).
- Modify: `web/src/baixas/PreviaDaBaixa.tsx` (a lista "Viram sobra" usa `rotuloDoNo`, como `LinhaDeSaida`).
- Modify: `web/src/baixas/PainelDeBaixa.tsx` (o texto da caixa do topo).
- Test (backend): o teste de Api ou de Application da prévia que hoje confere `viramSobra` e `travaNoTopo` passa a conferir
  o código também — um teste que morre se o código sair de cada um dos dois.
- Test (front): `web/src/baixas/PreviaDaBaixa.test.tsx` (a sobra com o código) e `web/src/baixas/PainelDeBaixa.test.tsx`
  (os testes que acham a caixa pelo nome exato "Isso impede montar 2 Chassi. Dar baixa também delas" mudam para o texto
  com o código). Os fixtures dos dois tipos ganham `codigo`.
- Modify (prosa): `specs/05-api-endpoints.md` (a resposta da prévia da baixa: os dois objetos com `codigo`);
  `specs/04-fluxos-de-usuario.md` e `CLAUDE.md` (bullet «A baixa») citam *"Isso impede montar N {Peça}. Dar baixa também
  delas"*: o `{Peça}` continua valendo como marcador do rótulo; ajuste só se a frase citada deixar de corresponder à tela.

- [ ] **Step 1:** Backend, teste vermelho: a prévia de uma baixa que gera sobra e trava traz o `codigo` nos dois. Rode e
  veja falhar.
- [ ] **Step 2:** Backend: acrescente `Codigo` aos dois DTOs e preencha. Rode os testes da baixa e veja passar.
- [ ] **Step 3:** Front, testes vermelhos: a sobra com "1 de PF-3020 — …" e a caixa com o código. Rode e veja falhar.
- [ ] **Step 4:** Front: tipos, `PreviaDaBaixa` e `PainelDeBaixa`. Rode os dois arquivos e veja passar.
- [ ] **Step 5:** Prosa (`specs/05`; `specs/04` e `CLAUDE.md` se couber).
- [ ] **Step 6:** `dotnet build Rastreamento.slnx -warnaserror`, `dotnet test Rastreamento.slnx -m:1`; em `web/`,
  `npm test -- --run --maxWorkers=2` e `npm run build`.
- [ ] **Step 7:** Commit: `fix(fase-5c): a previa da baixa traz o codigo na sobra e na trava do topo`.

---

### Task 3: F3 — "Aguardando montagem" só com o pai que ainda falta iniciar, e a sobra em montagem por Setor

**O que o usuário viu:** no `CONF-5C-DESALINHADO`, depois de iniciar os 2 MT-1020 na Solda, o cartão do MT-1020
continuou em "Aguardando montagem" com "Dá para iniciar 0; falta iniciar 0.", os filhos ("EX-2210 …: 3 aqui, 4 por
unidade"; "CH-2130 …: 0 aqui, 1 por unidade") e a caixa de lote desabilitada. As mesmas 3 EX-2210 já estavam na seção
"Sobra", com o "Descartar".

**Causa, lida no código:** a `ConsultaDeExecucaoUseCase` monta a seção "Aguardando montagem" a partir dos **filhos**: entra
todo pai com algum filho não concluído e com saldo em `AguardandoMontagem` no Setor. Não olha o `FaltaMontar` do pai. E a
linha de sobra "em montagem" da mesma consulta mostra `ExcessoEmMontagem(no)`, a sobra **total** do filho, em **cada**
Setor onde ele espera — no caso da C16 da spec da 5C (o filho de um pai Kit esperando na Solda e num Setor sem
`UtilizaKit`), o Setor sem a marca mostraria a sobra da Solda, e o "Descartar" de lá poderia tirar peça boa.
`SobraEmMontagemPorSetor`, da calculadora, já reparte a sobra por Setor (C16) e já é usada pelo "Usar a sobra"
(`CalculoDoReaproveitamento.Disponivel`) e pelo "falta" do cartão de montagem (`CalcularMontabilidade`); só a linha de
sobra da Fila ficou na conta total.

**Decisão do usuário (2026-11-16), uma tratativa para os dois:**
1. **O pai com `FaltaMontar` ≤ 0 sai de "Aguardando montagem".** Com falta 0, toda a espera dos filhos é sobra, e ela
   aparece inteira na seção "Sobra", onde já mora o "Descartar".
2. **A linha de sobra em montagem da Fila usa `SobraEmMontagemPorSetor(no)` filtrada para o Setor da fila**, em vez de
   `ExcessoEmMontagem(no)`. Setor sem sobra para aquele filho não tem linha. Assim a Fila, o "Usar a sobra" e o "falta"
   do cartão usam a mesma conta, a soma das linhas de sobra em montagem de um filho (somando os Setores) é o
   `ExcessoEmMontagem` dele, e o teto do "Descartar" de cada linha é o que está naquele Setor.
3. **`EmMaisDeUmSetor`** (o aviso "Este item aguarda montagem em mais de um Setor; não dá para saber em qual está a
   unidade a mais.") só é verdadeiro quando a repartição é uma **convenção**: pai que **não** recebe em conjunto
   (`RecebeEmConjunto` falso), com `FaltaMontar` > 0, e filho esperando em mais de um Setor. No pai que recebe em
   conjunto, a repartição segue a C16 (o que não fecha conjunto, nos Setores com `UtilizaKit`; o resto, fora deles) e o
   aviso é falso. Com `FaltaMontar` ≤ 0, tudo é sobra e cada Setor mostra o próprio saldo: falso também.
4. **O pai Avulso com espera em mais de um Setor** usa a mesma repartição (a ordem de Id do Setor de
   `SobraEmMontagemPorSetor`), com o aviso. O usuário aprovou isso sabendo que é convenção: a alternativa (total em cada
   Setor) deixaria a Fila e o "Usar a sobra" divergentes.

O ramo do Pedido **concluído** (que já mostra a quantidade daquele Setor, com `EmMaisDeUmSetor` falso) **não muda**.

**Files:**
- Modify: `src/Rastreamento.Application/Execucao/ConsultaDeExecucaoUseCase.cs` (o filtro da seção `montagem` e o ramo
  `AguardandoMontagem` da sobra, fora do Pedido concluído).
- Possível Modify: `src/Rastreamento.Application/Execucao/CalculadoraDeExecucao.cs`, só se precisar expor algo que a
  consulta não alcança. Não mude a conta de `SobraEmMontagemPorSetor` nem de `ExcessoEmMontagem`.
- Test: `tests/Rastreamento.Application.Tests/Execucao/ConsultaDeExecucaoUseCaseTests.cs` e os testes de Api da Fila.
  Antes de escrever, **procure os testes que hoje afirmam** (a) o pai com falta 0 em `AguardandoMontagem` da resposta e
  (b) a linha de sobra em montagem com `EmMaisDeUmSetor` verdadeiro ou com a sobra total em mais de um Setor (há pelo
  menos um em `ConsultaDeExecucaoUseCaseTests` que confere `("Montagem", null, 5m, true)`). Os que mudam de expectativa
  mudam, e o relatório diz qual, o antes e o depois, e por que a nova expectativa decorre da decisão acima.
- Front: o `DetalheDaSobra` da `FilaDoSetorPage` não muda (ele já lê `emMaisDeUmSetor` e a quantidade). Se algum teste de
  front depender do comportamento antigo do servidor, ele não depende de fato (o front recebe o que o fixture manda):
  nenhum teste de front deve mudar nesta task. Se mudar, diga por quê.
- Modify (prosa): `specs/04-fluxos-de-usuario.md` (a Fila do Setor: o pai sem nada a iniciar não fica em "Aguardando
  montagem"; a sobra dele aparece em "Sobra"; a linha de sobra mostra a parte daquele Setor) e `specs/01` (a regra 30, se
  ela descreve a linha de sobra por Setor ou o aviso).

**Casos que os testes têm de cobrir (cada um morre se a regra dele sair):**
- **Falta 0:** pai com `FaltaMontar` 0 e um filho com 3 esperando no Setor: o pai **não** está em `AguardandoMontagem`
  da resposta; a sobra do filho, 3, está em `Sobra` com `EmMaisDeUmSetor` falso.
- **Pai com falta e sem sobra continua:** pai com `FaltaMontar` > 0 e filho esperando no Setor: o pai continua na seção
  (guarda contra filtrar demais).
- **C16, Kit em dois Setores:** pai Kit (primeiro passo na Solda com `UtilizaKit`), filho esperando na Solda e num Setor
  sem a marca, com sobra nos dois termos. A Fila de cada Setor mostra a parte dela (`SobraEmMontagemPorSetor`), as duas
  somam o `ExcessoEmMontagem`, e o Setor sem a marca **não** mostra a sobra da Solda. `EmMaisDeUmSetor` falso nos dois.
- **Avulso em dois Setores, com falta:** a linha aparece só onde a repartição põe sobra, com `EmMaisDeUmSetor` verdadeiro;
  a soma bate com o `ExcessoEmMontagem`.
- **Pedido concluído:** o ramo dele não muda (um teste existente já cobre; confirme que continua verde).

- [ ] **Step 1:** Leia o código e liste os testes que mudam de expectativa (relatório).
- [ ] **Step 2:** Testes vermelhos dos casos acima. Rode e veja falhar.
- [ ] **Step 3:** Implemente na `ConsultaDeExecucaoUseCase`. Rode os testes de Application e veja passar.
- [ ] **Step 4:** Os testes de Api da Fila.
- [ ] **Step 5:** Prosa (`specs/04`; `specs/01` se couber).
- [ ] **Step 6:** `dotnet build Rastreamento.slnx -warnaserror`, `dotnet test Rastreamento.slnx -m:1`; em `web/`,
  `npm test -- --run --maxWorkers=2` e `npm run build` (para provar que o front não mudou).
- [ ] **Step 7:** Commit: `fix(fase-5c): a Fila tira da montagem o pai sem nada a iniciar e mostra a sobra por Setor`.
