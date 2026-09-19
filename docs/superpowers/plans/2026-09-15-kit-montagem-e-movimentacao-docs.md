# Kit, montagem e movimentação — Plano de documentação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplicar a spec `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md` à documentação do projeto — regras de domínio, roadmap, arquitetura, visão geral, notas nos fluxos e endpoints, errata da Fase 2 e invariantes do `CLAUDE.md`.

**Architecture:** Plano **só de documentação**. Nenhum código, nenhum teste de suíte, e **nenhuma mudança em `specs/02-modelo-de-dados.sql`** — o schema entra no início de cada fase (seção 2 da spec). Cada task aplica texto já escrito aqui, com `Edit` sobre trecho exato, e se verifica por `grep` com controle positivo.

**Tech Stack:** Markdown. Git. Ferramentas `Edit`/`Write` (nunca PowerShell `Set-Content`, que corrompe UTF-8).

## Pré-condição de base — ler antes de despachar

**Os trechos de "Trocar" deste plano foram medidos contra a branch `fase-2b-solido-3d`** (HEAD
`5a088d3`, 2026-09-15), não contra a `main` (`f2ee813`). A 2B mudou `specs/01`, `00`, `03`, `05`, `06`
e o `CLAUDE.md` — entre outras coisas, a hospedagem virou VPS pública e a seção "Pontos em aberto"
do `03` ganhou a lista de endurecimento de pré-deploy que a Task 2 usa de âncora. Medido: todos os
trechos batem **uma vez** na 2B; na `main`, o do parágrafo final de "Pontos em aberto" do `03` **não
existe**, e aplicar o resto sobre a `main` geraria conflito com a 2B em seis arquivos.

Portanto: **executar só depois de a Fase 2B entrar na `main`**, com `brainstorm-kit-montagem`
rebaseada sobre a `main` nova. Antes do primeiro despacho, remeça cada âncora com
`grep -cF -- "<trecho>" <arquivo>` (esperado: `1`); âncora que não bater é defeito do plano a
corrigir aqui, não a improvisar na task.

> **Remedido em 2026-09-19**, com a branch rebaseada sobre a `main` pós-2B (`65bbc19`): os 20
> trechos de "Trocar" aparecem **uma vez** cada, e nenhum texto novo já está presente. A contagem
> foi conferida contra um negativo conhecido: na `main` de antes da 2B (`f2ee813`), a âncora da
> Task 2, Step 7, devolve `0`. A âncora bater não bastou: depois do commit deste plano a 2B pôs um
> **item 4** na lista numerada de "Pontos em aberto" do `03` e um parágrafo "Um último ponto" depois
> dela, o que tornava errados o "quinto ponto" e o lugar da inserção. O Step 7 e a verificação do
> Step 8 da Task 2 foram reescritos por isso.

## Global Constraints

- Idioma: português brasileiro com acentuação completa; identificadores de código como estão (`QuantidadePorPai`, `UtilizaKit`, `EstruturaItem`, `MotivoPerda`, `Descarte`, `Movimentador`).
- Nomes fechados pela spec, sem variação: `Setor.UtilizaKit`, `EstruturaItem.QuantidadePorPai`, perfil `Movimentador`, motivo de perda `Descarte`, destino "montado", estado "aguardando coleta", fases **3**, **3B — Kit e montagem**, **3C — Notificação push**, **5**.
- Regras novas numeradas **22 a 27** em `specs/01-dominio-e-regras-de-negocio.md`; os outros arquivos as citam por número de regra.
- **Citação por nome, nunca por `arquivo.ext:NN`** nem por distância relativa ("acima", "a seguir") — regra da seção "Convenção de citação em comentário e prosa" do `CLAUDE.md`.
- Texto datado **não se reescreve**: onde a spec manda "nota" ou "errata", acrescenta-se bloco `>` datado; o texto original fica.
- Não tocar em `specs/02-modelo-de-dados.sql`.
- Trabalho na branch `brainstorm-kit-montagem` (worktree `C:/wt-kit`). Comandos de verificação rodam com `cd C:/wt-kit` e no Git Bash.
- Commit por task; mensagem termina com `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- **Releia a seção inteira** em volta de cada edição, não só a frase: a contradição costuma morar no bullet vizinho.

---

### Task 1: Regras de domínio em `specs/01-dominio-e-regras-de-negocio.md`

**Files:**
- Modify: `specs/01-dominio-e-regras-de-negocio.md` (glossário "Agrupamento", "Usuário / Perfil", "Perda"; regras 9, 15, 17; regras novas 22 a 27 depois da regra 21)

**Interfaces:**
- Consumes: nada.
- Produces: regras **22** (terminar ≠ mover), **23** (tarefas do Movimentador), **24** (trava de montagem), **25** (conjunto completo), **26** (`QuantidadePorPai`), **27** (perda que impede montar); regras 9, 15 e 17 alteradas. As Tasks 2 e 3 citam esses números.

- [ ] **Step 1: Medir a linha de base da citação por número de linha**

Run: `cd C:/wt-kit && grep -nE "[A-Za-z0-9_./-]+\.(cs|ts|tsx|css|sql|json|md|html):[0-9]+" specs/01-dominio-e-regras-de-negocio.md`
Expected: **1 linha**, a da regra 20, citando `ReceitaPadraoUseCase.cs` com número de linha. (É passivo anterior, fora de escopo — a linha serve de controle positivo do `grep`.)

- [ ] **Step 2: Glossário — Agrupamento**

Trocar a linha inteira:

````markdown
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo é descritivo — não impõe roteiro. |
````

por:

````markdown
| **Agrupamento** | Agrupamento de Peças dentro de um Pedido. Um Pedido tem N Agrupamentos. Tem um **Tipo**: 'Kit' (peças que vão para a solda, juntas) ou 'Avulso' (peças que não passam por solda). O Tipo não impõe roteiro, mas **não é mais só descritivo**: um Agrupamento Kit fica sujeito à trava de montagem e ao conjunto completo (regras 24 e 25); um Avulso, não. Até 2026-09-15 o Tipo era só descritivo; a reversão parcial veio de esclarecimento do processo da fábrica — ver `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`. |
````

- [ ] **Step 3: Glossário — Usuário / Perfil**

Trocar:

````markdown
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. |
````

por:

````markdown
| **Usuário / Perfil** | Login próprio (usuário/senha + JWT). Cada Usuário tem um Perfil (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão, Administrador) que restringe telas/ações — ver `00-visao-geral.md`. |
````

- [ ] **Step 4: Glossário — Perda**

Trocar:

````markdown
| **Perda** | Baixa de quantidade perdida em produção (some no armazém ou morre após um processo que deu errado). Vai para um bucket terminal; a reposição é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda'). |
````

por:

````markdown
| **Perda** | Baixa de quantidade de um `EstruturaItem` (Peça ou Item) que sai da produção: some no armazém, morre após um processo que deu errado, ou é **descarte** de sobra que nunca foi usada (regra 25). Vai para um bucket terminal; a reposição, quando há, é um Pedido de Retrabalho separado (MotivoRetrabalho='Perda') — descarte não é reposto. |
````

- [ ] **Step 5: Regra 9**

Trocar:

````markdown
9. O lote de um `EstruturaItem` é **divisível por quantidades livres**: uma parte pode estar
   num Setor e outra parte em outro Setor ao mesmo tempo (ex.: 6 na Usinagem, 4 na Corte).
   Não há identidade de sub-lote (sem etiqueta/serial) — controla-se apenas *quanto* está
   *onde*. Invariante: **conservação de quantidade** — soma das unidades em todos os Setores +
   expedido (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça. (A divisão física
   entre pinturas terceirizadas no fim do processo segue controlada fora do sistema.)
````

por:

````markdown
9. O lote de um `EstruturaItem` é **divisível por quantidades livres**: uma parte pode estar
   num Setor e outra parte em outro Setor ao mesmo tempo (ex.: 6 na Usinagem, 4 na Corte).
   Não há identidade de sub-lote (sem etiqueta/serial) — controla-se apenas *quanto* está
   *onde*. Invariante: **conservação de quantidade**, para **todo** `EstruturaItem` (Peça ou
   Item) — em produção (nos Setores, inclusive aguardando coleta — regra 22) + montado dentro do
   pai (regra 24) + expedido (`Expedicao`) + perdido (`Perda`) = quantidade total do nó.
   "Montado" só existe para Item, e expedição só para Peça. (A divisão física entre pinturas
   terceirizadas no fim do processo segue controlada fora do sistema.)

   **Alterada em 2026-09-15:** até então o invariante falava só da Peça e não tinha o termo
   "montado" — um Item soldado dentro do pai não era nenhum dos três termos antigos.
````

- [ ] **Step 6: Regra 15**

Trocar:

````markdown
15. Cada Usuário tem um Perfil (Operador, Almoxarifado, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa.
````

por:

````markdown
15. Cada Usuário tem um Perfil (Operador, Almoxarifado, Movimentador, PCP, Qualidade, Gestão,
    Administrador) que restringe quais telas e ações ele acessa. O **Movimentador** entrou em
    2026-09-15 (regra 22).
````

- [ ] **Step 7: Regra 17**

Trocar:

````markdown
17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem` ou
    `MortaEmProcesso`), levando a quantidade ao bucket terminal "perdido". Para repor, abre-se
    um Pedido de Retrabalho separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça
    original. Como na reprovação, registrar a perda **não** abre retrabalho automaticamente.
````

por:

````markdown
17. Uma **perda** registra baixa de quantidade em produção (`PerdaArmazem`, `MortaEmProcesso` ou
    `Descarte`), levando a quantidade ao bucket terminal "perdido". Pode ser registrada em
    **qualquer** `EstruturaItem`, Peça ou Item. Para repor, abre-se um Pedido de Retrabalho
    separado (`MotivoRetrabalho='Perda'`) — **nunca** reabre a Peça original. Como na reprovação,
    registrar a perda **não** abre retrabalho automaticamente. `Descarte` é a sobra que nunca foi
    usada (regra 25) e não se repõe. Quando a perda de uma parte impede montar o pai, vale a
    regra 27.

    **Alterada em 2026-09-15:** entraram o motivo `Descarte` e a perda de Item.
````

- [ ] **Step 8: Regras 22 a 27**

Trocar o fim da regra 21 e o título seguinte:

````markdown
    pode fechar um ciclo, e é isso, não a repetição em si, que a regra 20 proíbe.

## Pontos ainda em aberto
````

por:

````markdown
    pode fechar um ciclo, e é isso, não a repetição em si, que a regra 20 proíbe.

*As regras 22 a 27 foram decididas em 2026-09-15 (spec
`docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`) e são implementadas nas
Fases 3, 3B e 5 de `06-roadmap-mvp.md`; o schema correspondente entra no início de cada fase.*

22. **Terminar e mover são ações separadas, feitas por pessoas diferentes.** O operador registra
    que terminou o trabalho num Setor, e aquela quantidade passa a **aguardar coleta**; o
    **Movimentador** a leva e registra a entrada no próximo destino. Vale para todo
    `EstruturaItem`, de Agrupamento Kit ou Avulso. Um filho que concluiu o próprio Roteiro aguarda
    coleta para a montagem do pai. Para a conservação de quantidade (regra 9), aguardar coleta
    conta como em produção; como esse estado é representado no banco é decisão da Fase 3.
23. **As tarefas do Movimentador são calculadas a partir do estado**, não gravadas como aviso:
    quando alguém leva, a tarefa some sozinha. São duas:
    - **Item pronto** — quantidade aguardando coleta. É tarefa, exceto para filho de Agrupamento
      Kit a caminho de Setor com `UtilizaKit`, em que é só informativo, porque esse filho não vai
      sozinho (regra 25).
    - **Kit pronto para montagem** — tarefa que aparece quando os filhos diretos de um nó,
      aguardando coleta, formam ao menos um conjunto completo (regra 25). O número de conjuntos é
      o mínimo, entre os filhos diretos, de ⌊quantidade aguardando coleta ÷ `QuantidadePorPai`⌋.

    Notificação no celular é reforço desta lista, não substituto (Fase 3C).
24. **Trava de montagem.** Vale quando as três condições valem juntas: o Agrupamento é **Kit**, o
    Setor tem **`UtilizaKit`** (marca no cadastro do Setor — hoje, a Solda), e o nó **tem filhos**
    (Peça ou Item de submontagem). Olha só os **filhos diretos** do nó.
    - **Montar é registro próprio**, separado de mover. O operador registra "montei N"; o sistema
      aceita se N não passar do mínimo, entre os filhos diretos, de
      ⌊quantidade do filho no Setor ÷ `QuantidadePorPai`⌋. A montagem pode ser **parcial** (montar
      6 de 10), o que casa com a expedição parcial (regra 16).
    - Ao montar, baixa-se `N × QuantidadePorPai` de cada filho direto para o destino terminal
      **"montado"**, **gravando a baixa de cada filho**, e não só N — editar a razão depois não
      reescreve o passado. N soma ao **total montado** do nó.
    - A **saída** do nó de um Setor com `UtilizaKit` é limitada ao total montado, em **qualquer**
      passagem: se o nó volta à Solda (regra 21), os filhos já viraram o nó, e é o total montado
      que conta.
    - A ordem de baixo para cima é consequência, não cálculo: um nó intermediário só existe na
      Solda depois de montado, então o pai dele só monta depois.
25. **Conjunto completo.** Um Kit pode ir à Solda em parte do pai (os conjuntos de 7 de 10), mas
    **nunca incompleto**: a entrada de filhos de Agrupamento Kit num Setor com `UtilizaKit` só é
    aceita em conjuntos completos — `N × QuantidadePorPai` de **todos** os filhos diretos, juntos,
    na mesma movimentação. O motivo é físico: peça solta na Solda ocupa espaço, e, se houver perda
    antes de o resto chegar, aquele espaço fica sem destino. Não há exceção para completar
    conjunto que perdeu parte dentro da Solda — isso é perda (regra 27). A sobra que não fecha
    conjunto (ex.: refugo além do necessário) nunca entra na Solda e, se não for usada, sai como
    perda de motivo `Descarte` (regra 17).
26. **`EstruturaItem.QuantidadePorPai`** guarda quantos daquele nó entram em **uma** unidade do
    pai, **ao lado** da quantidade absoluta (`EstruturaItem.Quantidade`). É **obrigatória em todo
    Item e nula na Peça**. A cópia da receita a preenche com
    `ComponenteFilhoPadrao.QuantidadePadrao`; num Item ad-hoc, quem cadastra informa. A quantidade
    absoluta continua sendo o que o apontamento movimenta; a razão serve à trava e às tarefas.
    **Não existe invariante entre as duas**: um Item de 45 com razão 4 sob um pai de 10 é legítimo
    (sobra de refugo). A Fase 2 havia decidido não guardar a razão; ver a errata na §2.1 da spec da
    Fase 2 (`docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`).
27. **Perda que impede montar.** Quando a perda de uma parte impede montar uma unidade do pai, a
    perda **sobe até a Peça do topo**: registra-se a perda daquela unidade da Peça no Pedido
    original, que conclui normalmente (regra 13), e as partes daquela unidade que existem e ainda
    não foram montadas saem junto como perda. A reposição é um Pedido de Retrabalho (regra 17),
    montado pelo PCP para a Peça faltante, em que o que já existe é marcado **pronto** — só dentro
    da árvore daquela unidade; o resto continua no Pedido original.
    - Nó **pronto** não percorre Roteiro: folha pronta já foi fabricada; nó com filhos pronto já
      foi montado (total montado = quantidade, e os filhos nem precisam existir no Retrabalho).
    - Nó pronto nasce **aguardando coleta** e é levado normalmente (regra 22).
    - As partes saem do original como perda, sem vínculo por nó com o Retrabalho: o Retrabalho já
      nasce ligado ao original (`PedidoOrigemId`, `MotivoRetrabalho = 'Perda'`), e confrontar os
      dois mostra o destino do saldo — perdido ou descartado de fato, ou reposto e expedido depois.

## Pontos ainda em aberto
````

- [ ] **Step 9: Verificar**

Run: `cd C:/wt-kit && grep -cE "^2[2-7]\. " specs/01-dominio-e-regras-de-negocio.md`
Expected: `6`

Run: `cd C:/wt-kit && grep -n "O Tipo é descritivo" specs/01-dominio-e-regras-de-negocio.md; echo "exit=$?"`
Expected: nenhuma linha, `exit=1`.

Run: `cd C:/wt-kit && grep -c "Movimentador" specs/01-dominio-e-regras-de-negocio.md`
Expected: `≥ 4` (glossário, regra 15, regra 22, regra 23).

Run: `cd C:/wt-kit && grep -nE "[A-Za-z0-9_./-]+\.(cs|ts|tsx|css|sql|json|md|html):[0-9]+" specs/01-dominio-e-regras-de-negocio.md`
Expected: a **mesma 1 linha** do Step 1 — nenhuma citação nova por número de linha.

Run (mojibake, com controle positivo): `cd C:/wt-kit && printf 'aÃ§' | grep -c "Ã" && grep -c "Ã" specs/01-dominio-e-regras-de-negocio.md`
Expected: `1` (o controle acha) e depois `0` (o arquivo não tem).

Releia a lista de regras de 9 a 27 inteira e o bloco "Pontos ainda em aberto": nenhuma regra antiga pode contradizer as novas (em especial a 13, que continua falando só de Peça, e isso é correto).

- [ ] **Step 10: Commit**

```bash
cd C:/wt-kit && git add specs/01-dominio-e-regras-de-negocio.md && git commit -F - <<'EOF'
docs(dominio): regras 22 a 27 -- terminar e mover, tarefas, trava de montagem, conjunto completo, QuantidadePorPai, perda que impede montar

Aplica a spec 2026-09-15-kit-montagem-e-movimentacao-design: glossario do Agrupamento
(Tipo deixa de ser so descritivo), regra 9 para todo EstruturaItem com "montado",
Movimentador na regra 15, Descarte e perda de Item na regra 17.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Roadmap, visão geral e arquitetura (`specs/06`, `specs/00`, `specs/03`)

**Files:**
- Modify: `specs/06-roadmap-mvp.md` (Fase 3, novas Fase 3B e 3C, Fase 5, nova seção de dívida no fim)
- Modify: `specs/00-visao-geral.md` (lista de perfis)
- Modify: `specs/03-arquitetura-tecnica.md` (item "PWA/offline", parágrafo de chaves VAPID em "Pontos em aberto")

**Interfaces:**
- Consumes: regras 9, 17, 22 a 27 da Task 1 (citadas por número).
- Produces: nomes das fases **3B — Kit e montagem** e **3C — Notificação push**, citados pelas notas da Task 3.

- [ ] **Step 1: Medir a linha de base da citação por número de linha**

Run: `cd C:/wt-kit && grep -cE "[A-Za-z0-9_./-]+\.(cs|ts|tsx|css|sql|json|md|html):[0-9]+" specs/06-roadmap-mvp.md specs/00-visao-geral.md specs/03-arquitetura-tecnica.md`
Expected: anote os três números. Controle positivo: `printf 'x.md:12' | grep -cE "[A-Za-z0-9_./-]+\.(md):[0-9]+"` devolve `1`.

- [ ] **Step 2: Fase 3 ampliada, e Fases 3B e 3C novas**

Em `specs/06-roadmap-mvp.md`, trocar:

````markdown
## Fase 3 — Rastreamento de setor

- Apontamento de entrada/saída de `EstruturaItem` em `Setor`.
- Validação de conservação de quantidade (soma em setores + expedido + perdido = total
  da Peça; na aplicação, não por índice filtrado).
- Tela de "fila do setor" para o operador.
- Critério de pronto: dá para acompanhar, item por item, em qual setor cada peça está.
````

por:

````markdown
## Fase 3 — Rastreamento de setor

- Apontamento de entrada/saída de `EstruturaItem` em `Setor`.
- **Terminar e mover como ações separadas** (regra 22 de `01`): o operador registra que terminou,
  a quantidade passa a **aguardar coleta**, e o **Movimentador** registra a entrada no próximo
  destino. Perfil novo `Movimentador` — linha em `dbo.Perfil`, na tabela
  `web/src/auth/permissoes.ts` e nos `[Authorize(Roles)]`; perfil novo exige código e deploy.
- Validação de conservação de quantidade (regra 9: em produção + montado + expedido + perdido =
  total, para todo `EstruturaItem`; na aplicação, não por índice filtrado). O termo "montado" só
  ganha valor na Fase 3B.
- Tela de "fila do setor" para o operador.
- Tela **Tarefas** do Movimentador com os **Itens prontos** (regra 23), calculada a partir do
  estado e atualizada periodicamente — sem tabela de aviso.
- Critério de pronto: dá para acompanhar, item por item, em qual setor cada peça está **e** se ela
  aguarda coleta; o Movimentador vê o que tem a levar e registra a entrega.

> **Ampliada em 2026-09-15** pela spec `2026-09-15-kit-montagem-e-movimentacao-design.md`: terminar ≠
> mover, "aguardando coleta", o perfil Movimentador e a tela Tarefas entraram aqui, e não na 3B,
> porque são como a movimentação funciona para **tudo**, Kit ou Avulso. Como "aguardando coleta" é
> representado no banco, com o lote divisível, é decisão da spec desta fase.

## Fase 3B — Kit e montagem

- `Setor.UtilizaKit` e `EstruturaItem.QuantidadePorPai` (regras 24 e 26 de `01`); o schema entra no
  início desta fase, e a cópia da receita passa a preencher a razão.
- Registro de montagem ("montei N") com baixa por filho para o destino "montado", trava de montagem
  por nó e limite de saída pelo total montado (regra 24).
- Conjunto completo na entrada de Setor com `UtilizaKit` (regra 25).
- Tarefa **Kit pronto para montagem** na tela Tarefas (regra 23).
- A decidir na spec desta fase: trocar o Tipo do Agrupamento ou a marca `UtilizaKit` com produção
  em andamento; filho que conclui a própria montagem na mesma Solda em que o pai será montado; o que
  a edição de nó da Fase 2 faz com `QuantidadePorPai`.
- Critério de pronto: um Kit de três níveis é montado de baixo para cima com montagem parcial; o
  sistema recusa conjunto incompleto e saída acima do montado; a tarefa Kit pronto aparece e some
  quando o Kit é levado.

## Fase 3C — Notificação push

> **Executada depois da Fase 5**, fora da ordem das letras: o fluxo ponta a ponta vem primeiro, e o
> push é reforço de uma lista que já funciona (a tela Tarefas). Fica numerada como 3C por tema.

- PWA **mínimo**: manifesto, ícones e service worker **sem cache de API** — não reabre a decisão
  "PWA/offline" de `03-arquitetura-tecnica.md`.
- Tabela de inscrição (usuário, endpoint, chaves do navegador), inscrever e cancelar, chaves VAPID
  como segredo de ambiente, envio e limpeza de inscrição morta.
- Disparo por evento no servidor: ao registrar "terminei", recalcular se formou conjunto completo e
  notificar os Movimentadores. Tocar na notificação abre a tela Tarefas, que continua sendo a fonte
  da verdade; entrega de push não é garantida.
- Verificação manual num Android real por HTTPS (service worker não roda no jsdom).
- Critério de pronto: um Movimentador com o celular bloqueado recebe o aviso de Kit pronto, e tocar
  nele abre a tela Tarefas.
````

- [ ] **Step 3: Fase 5 ampliada**

Em `specs/06-roadmap-mvp.md`, trocar:

````markdown
- Registro de `Expedicao` (remessas parciais) e de `Perda` por Peça.
````

por:

````markdown
- Registro de `Expedicao` (remessas parciais) e de `Perda` — por Peça e, desde 2026-09-15, também
  por Item, com o motivo `Descarte` (regra 17 de `01`).
- Perda que impede montar (regra 27): a perda sobe até a Peça do topo, as partes não montadas daquela
  unidade saem junto, e o Pedido de Retrabalho marca como **pronto** o que já existe. A decidir na
  spec desta fase: de qual Setor sai a parte que acompanha a perda, com o lote dividido entre Setores.
````

- [ ] **Step 4: Dívida de CRUD no fim do roadmap**

Em `specs/06-roadmap-mvp.md`, trocar o último parágrafo do arquivo:

````markdown
**Efeito único sobre a Fase 1C:** o caso de uso que grava a receita padrão deve aceitar **uma lista
de linhas de uma vez**, e não só uma linha por chamada. É quase de graça agora e evita reescrever o
caso de uso quando o import chegar; a tela continua digitando linha a linha.
````

por:

````markdown
**Efeito único sobre a Fase 1C:** o caso de uso que grava a receita padrão deve aceitar **uma lista
de linhas de uma vez**, e não só uma linha por chamada. É quase de graça agora e evita reescrever o
caso de uso quando o import chegar; a tela continua digitando linha a linha.

## Fora das fases — dívida: CRUD de Usuário e permissão por Perfil (registrada em 2026-09-15)

`GET/POST /usuarios` consta em `05-api-endpoints.md`, mas não tem implementação, e nenhuma fase acima
o implementa: hoje um usuário só nasce por SQL. São **duas dívidas de custo diferente**, sem fase e
sem data, por decisão do usuário:

- **CRUD de Usuário** (criar conta, ativar, atribuir perfil existente) — **barata**. Fica mais urgente
  com a Fase 3: até existir, cada conta de Movimentador nasce por SQL na VPS.
- **Permissão por Perfil fora do código** (o mapeamento perfil → ação sair dos `[Authorize(Roles)]`
  literais) — **cara**; é ela que faz perfil novo exigir código e deploy.
````

Antes de gravar, confirme que "não tem implementação" continua verdadeiro:
Run: `cd C:/wt-kit && grep -rniE 'usuarios"|"usuarios|UsuariosController|UsuarioController' src/; echo "exit=$?"` → nenhuma linha, `exit=1`.
Controle positivo: `cd C:/wt-kit && grep -rliE '"setores|SetoresController' src/` → acha `SetoresController.cs`.
Se o primeiro comando achar algo, **pare** e reporte — a frase estaria falsa.

- [ ] **Step 5: Perfis em `specs/00-visao-geral.md`**

Trocar:

````markdown
- **Operador** de setor (registra entrada/saída de componentes no seu setor)
- **Almoxarifado** / Separação (registra separação de materiais)
````

por:

````markdown
- **Operador** de setor (registra o trabalho no seu setor, inclusive que terminou — ver regra 22 de
  `01-dominio-e-regras-de-negocio.md`)
- **Almoxarifado** / Separação (registra separação de materiais)
- **Movimentador** (leva os Itens prontos ao próximo Setor e os Kits completos à Solda; perfil
  decidido em 2026-09-15)
````

- [ ] **Step 6: PWA em `specs/03-arquitetura-tecnica.md`**

Trocar:

````markdown
- **PWA/offline: não é necessário no MVP.** Confirmado com o negócio — pode entrar depois
  se o uso em campo mostrar necessidade real (ex.: instabilidade de wifi na fábrica). Não
  desenhar a camada de estado pensando nisso agora, para não adicionar complexidade
  desnecessária cedo.
````

por:

````markdown
- **PWA/offline: não é necessário no MVP.** Confirmado com o negócio — pode entrar depois
  se o uso em campo mostrar necessidade real (ex.: instabilidade de wifi na fábrica). Não
  desenhar a camada de estado pensando nisso agora, para não adicionar complexidade
  desnecessária cedo. **Ressalva de 2026-09-15:** o **PWA mínimo para notificação push**
  (manifesto, ícones e service worker **sem cache de API**) está previsto na Fase 3C de
  `06-roadmap-mvp.md` e **não reabre** esta decisão — ela trata de funcionar sem rede, e o push
  não precisa disso.
````

- [ ] **Step 7: Chaves VAPID em "Pontos em aberto" de `specs/03-arquitetura-tecnica.md`**

Inserir o parágrafo novo **antes** do parágrafo que começa com "Um último ponto" — o "último" dele
precisa continuar verdadeiro. Trocar:

````markdown
**Os três itens de dívida de endurecimento não são desta fase.** Cada um vira item próprio na
fila, em branch separada — decisão do usuário: misturar infraestrutura na branch da Fase 2B
poluiria a review dela.

Um último ponto, que não é dívida nova e sim risco que muda de tamanho: o `CLAUDE.md` já registra
````

por:

````markdown
**Os três itens de dívida de endurecimento não são desta fase.** Cada um vira item próprio na
fila, em branch separada — decisão do usuário: misturar infraestrutura na branch da Fase 2B
poluiria a review dela.

Um quinto ponto, com **gatilho próprio — início da Fase 3C** —, e por isso fora da lista numerada,
cujos quatro itens carregam todos o gatilho de pré-deploy: as **chaves VAPID** da notificação push
(`06-roadmap-mvp.md`, Fase 3C). Procedimento igual ao da `SigningKey`: fornecidas por variável de
ambiente na VPS, nunca commitadas. Antes da Fase 3C elas não existem, e não há o que configurar.

Um último ponto, que não é dívida nova e sim risco que muda de tamanho: o `CLAUDE.md` já registra
````

- [ ] **Step 8: Verificar**

Run: `cd C:/wt-kit && grep -n "^## Fase 3" specs/06-roadmap-mvp.md`
Expected: três linhas, nesta ordem — `Fase 3 — Rastreamento de setor`, `Fase 3B — Kit e montagem`, `Fase 3C — Notificação push` — e todas antes de `## Fase 4`.

Run: `cd C:/wt-kit && grep -n "Movimentador" specs/00-visao-geral.md specs/06-roadmap-mvp.md specs/03-arquitetura-tecnica.md`
Expected: ao menos uma linha em `00` e várias em `06`; nenhuma obrigatória em `03`.

Run: `cd C:/wt-kit && grep -nE "VAPID|^Um quinto ponto|^Um último ponto" specs/03-arquitetura-tecnica.md`
Expected: a linha de "Um quinto ponto" vem **antes** da de "Um último ponto", e as de "VAPID" estão entre as duas.

Run: `cd C:/wt-kit && awk '/^## Pontos em aberto/{p=1} p' specs/03-arquitetura-tecnica.md | grep -cE '^[0-9]+\. \*\*'`
Expected: `4` — a lista numerada não ganhou item. (Controle positivo: o mesmo comando sem o `awk`, sobre o arquivo inteiro, devolve um número ≥ 4.)

Run: o comando do Step 1 de novo.
Expected: os **mesmos três números**.

Run (mojibake): `cd C:/wt-kit && grep -c "Ã" specs/06-roadmap-mvp.md specs/00-visao-geral.md specs/03-arquitetura-tecnica.md`
Expected: `0` nos três.

Releia o topo de `06-roadmap-mvp.md` ("executadas em sequência"): a nota da 3C precisa ser a exceção explícita a ele, não uma contradição silenciosa.

- [ ] **Step 9: Commit**

```bash
cd C:/wt-kit && git add specs/06-roadmap-mvp.md specs/00-visao-geral.md specs/03-arquitetura-tecnica.md && git commit -F - <<'EOF'
docs(roadmap): Fase 3 ampliada, Fases 3B (Kit e montagem) e 3C (push), Fase 5 ampliada, divida de CRUD

Perfil Movimentador na visao geral; ressalva de PWA minimo para push e chaves VAPID
com gatilho proprio na arquitetura. Aplica a spec 2026-09-15-kit-montagem-e-movimentacao-design.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Errata da Fase 2, notas nos fluxos e endpoints, invariantes do `CLAUDE.md`

**Files:**
- Modify: `docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md` (bloco de errata ao fim da §2.1)
- Modify: `specs/04-fluxos-de-usuario.md` (notas em "2. Apontamento em Setor", "5. Retrabalho", "6. Perda de peças")
- Modify: `specs/05-api-endpoints.md` (notas em "Execução / Rastreamento" e "Perdas")
- Modify: `CLAUDE.md` (seção "Invariantes de negócio que não podem ser violadas")

**Interfaces:**
- Consumes: regras 9, 17, 22 a 27 (Task 1) e os nomes das Fases 3, 3B, 5 (Task 2).
- Produces: nada consumido depois.

- [ ] **Step 1: Medir a linha de base da citação por número de linha**

Run: `cd C:/wt-kit && grep -cE "[A-Za-z0-9_./-]+\.(cs|ts|tsx|css|sql|json|md|html):[0-9]+" CLAUDE.md specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`
Expected: anote os quatro números (o `CLAUDE.md` tem passivo anterior; o que importa é não mudar).

- [ ] **Step 2: Errata na §2.1 da spec da Fase 2**

Em `docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`, trocar:

````markdown
**Descartado:** guardar a razão e derivar o absoluto. Custo que matou: toda consulta da Fase 3 em
diante subiria a árvore para saber quanto "4" é de verdade, e a conservação de quantidade viraria
cálculo recursivo em cada apontamento.

### 2.2 Editar a quantidade de uma Peça **não** cascateia nos filhos
````

por:

````markdown
**Descartado:** guardar a razão e derivar o absoluto. Custo que matou: toda consulta da Fase 3 em
diante subiria a árvore para saber quanto "4" é de verdade, e a conservação de quantidade viraria
cálculo recursivo em cada apontamento.

> **ERRATA (2026-09-15, esclarecimento do processo da fábrica).** O descarte registrado no
> parágrafo *"Descartado: guardar a razão e derivar o absoluto"* é **parcialmente revertido**, e
> fica **anotado e não reescrito**, porque este documento é o registro datado do desenho aprovado
> em 2026-08-29. A quantidade absoluta continua guardada e continua sendo o que o apontamento
> movimenta — nada é derivado dela. O que muda: a razão por unidade do pai **passa a ser guardada
> também**, em `EstruturaItem.QuantidadePorPai` (regra 26 de `specs/01-dominio-e-regras-de-negocio.md`).
> O motivo não é erro de raciocínio da época, e sim informação que esta fase não tinha: a Solda
> **monta parcialmente** (monta 6 de 10 se só há partes para 6), amarrada à expedição parcial, e a
> trava de montagem precisa da razão para saber quantas unidades as partes presentes permitem.
> Derivar a razão como filho ÷ pai não serve, porque a sobra de refugo viraria exigência. A §2.2
> continua valendo: **não existe invariante** entre as duas quantidades. Desenho completo em
> `docs/superpowers/specs/2026-09-15-kit-montagem-e-movimentacao-design.md`.

### 2.2 Editar a quantidade de uma Peça **não** cascateia nos filhos
````

- [ ] **Step 3: Nota em "2. Apontamento em Setor" (`specs/04-fluxos-de-usuario.md`)**

Trocar:

````markdown
## 2. Apontamento em Setor

*Perfil: Operador*
````

por:

````markdown
## 2. Apontamento em Setor

*Perfil: Operador*

> **Nota (2026-09-15).** Este fluxo é anterior à regra 22 de `01-dominio-e-regras-de-negocio.md`:
> terminar e mover passaram a ser ações separadas, de pessoas diferentes — o operador registra que
> terminou, a quantidade aguarda coleta, e o **Movimentador** registra a entrada no próximo
> destino. Em Agrupamento Kit valem ainda a trava de montagem e o conjunto completo (regras 24 e
> 25). O passo a passo abaixo fica como está até a spec da Fase 3 (e da 3B) revê-lo.
````

- [ ] **Step 4: Nota em "5. Retrabalho" (`specs/04-fluxos-de-usuario.md`)**

Trocar:

````markdown
## 5. Retrabalho (Reprovação ou Perda)

*Perfil: Qualidade*
````

por:

````markdown
## 5. Retrabalho (Reprovação ou Perda)

*Perfil: Qualidade*

> **Nota (2026-09-15).** Quando o Retrabalho repõe uma perda que impediu montar, o Pedido novo não
> segue o fluxo 1-4 "normalmente" em tudo: o que já existe da unidade perdida é marcado **pronto**,
> não percorre Roteiro e nasce aguardando coleta (regra 27 de `01-dominio-e-regras-de-negocio.md`).
> Revisto na spec da Fase 5.
````

- [ ] **Step 5: Nota em "6. Perda de peças" (`specs/04-fluxos-de-usuario.md`)**

Trocar:

````markdown
## 6. Perda de peças

*Perfil: Qualidade / PCP*
````

por:

````markdown
## 6. Perda de peças

*Perfil: Qualidade / PCP*

> **Nota (2026-09-15).** A perda passou a valer para **qualquer** `EstruturaItem`, não só Peça, e
> ganhou o motivo `Descarte` (regra 17 de `01-dominio-e-regras-de-negocio.md`); quando a perda de
> uma parte impede montar o pai, a perda sobe até a Peça do topo (regra 27). O passo a passo abaixo
> é revisto na spec da Fase 5.
````

- [ ] **Step 6: Nota em "Execução / Rastreamento" (`specs/05-api-endpoints.md`)**

Trocar:

````markdown
- `POST /estrutura-itens/{id}/entradas-setor` — registra entrada no setor atual
````

por:

````markdown
> **Nota (2026-09-15).** As rotas abaixo são anteriores às regras 22 a 25 de
> `01-dominio-e-regras-de-negocio.md` (terminar ≠ mover com aguardando coleta, tarefas do
> Movimentador, registro de montagem e conjunto completo). Os contratos são redesenhados nas specs
> das Fases 3 e 3B.

- `POST /estrutura-itens/{id}/entradas-setor` — registra entrada no setor atual
````

- [ ] **Step 7: Nota em "Perdas" (`specs/05-api-endpoints.md`)**

Trocar:

````markdown
- `POST /pecas/{estruturaItemId}/perdas` — registra uma Perda.
````

por:

````markdown
> **Nota (2026-09-15).** O `motivoPerda` ganha `'Descarte'`, e a perda passa a valer para Item, não
> só Peça — o prefixo `/pecas/` deixa de descrever o recurso (regras 17 e 27 de
> `01-dominio-e-regras-de-negocio.md`). Contrato redesenhado na spec da Fase 5.

- `POST /pecas/{estruturaItemId}/perdas` — registra uma Perda.
````

- [ ] **Step 8: Invariantes do `CLAUDE.md`**

Trocar:

````markdown
- Um `EstruturaItem` (lote) é **divisível por quantidades livres**: pode ter quantidades em
  Setores diferentes ao mesmo tempo. Não há identidade de sub-lote (sem serial). O
  invariante a preservar é **conservação de quantidade**: soma em Setores + expedido
  (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça (validado na aplicação).
````

por:

````markdown
- Um `EstruturaItem` (lote) é **divisível por quantidades livres**: pode ter quantidades em
  Setores diferentes ao mesmo tempo. Não há identidade de sub-lote (sem serial). O
  invariante a preservar é **conservação de quantidade**, para **todo** `EstruturaItem`: em
  produção (inclusive aguardando coleta) + montado dentro do pai + expedido (`Expedicao`) +
  perdido (`Perda`) = quantidade total do nó (validado na aplicação; regra 9).
- Em Agrupamento **Kit**, num Setor com `UtilizaKit`, um nó com filhos **só sai com o que já foi
  montado**, e a montagem só aceita o que os filhos diretos presentes permitem
  (`QuantidadePorPai`); filhos só entram nesse Setor em **conjuntos completos**. Terminar e mover
  são ações separadas, para Kit e Avulso. (Regras 22 a 27, decididas em 2026-09-15 e implementadas
  a partir da Fase 3.)
````

- [ ] **Step 9: Verificar**

Run: `cd C:/wt-kit && grep -c "Nota (2026-09-15)" specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md`
Expected: `specs/04-fluxos-de-usuario.md:3` e `specs/05-api-endpoints.md:2`.

Run: `cd C:/wt-kit && grep -c "ERRATA (2026-09-15" docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`
Expected: `1`, e o bloco fica **entre** o parágrafo "Descartado: guardar a razão…" e o título da §2.2.

Run: `cd C:/wt-kit && grep -n "quantidade total da Peça (validado" CLAUDE.md; echo "exit=$?"`
Expected: nenhuma linha, `exit=1` (a redação antiga saiu).

Run: o comando do Step 1 de novo.
Expected: os **mesmos quatro números**.

Run (mojibake): `cd C:/wt-kit && grep -c "Ã" CLAUDE.md specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md`
Expected: `0` nos quatro.

Releia a seção "Invariantes de negócio que não podem ser violadas" inteira do `CLAUDE.md`: o bullet de conclusão de Pedido/Agrupamento ("toda a quantidade expedida ou perdida") fala de **Peça** e continua correto; confirme que nada ali passou a contradizer o bullet novo.

- [ ] **Step 10: Commit**

```bash
cd C:/wt-kit && git add docs/superpowers/specs/2026-08-29-fase-2-estrutura-recursiva-design.md specs/04-fluxos-de-usuario.md specs/05-api-endpoints.md CLAUDE.md && git commit -F - <<'EOF'
docs: errata da Fase 2 (QuantidadePorPai), notas em 04 e 05, invariantes do CLAUDE.md

A razao por unidade do pai passa a ser guardada ao lado do absoluto, por esclarecimento
do processo (montagem parcial). Fluxos e endpoints anteriores as regras 22 a 27 ganham
nota datada, sem reescrita. Aplica a spec 2026-09-15-kit-montagem-e-movimentacao-design.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```
