# Panorama — ajustes da conferência

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan
> task-by-task.

**Origem:** a conferência do usuário no navegador (Task 10 de `docs/superpowers/plans/2026-11-17-panorama.md`), feita
em 2026-11-18. Três achados viraram ajuste (F1, F2 e F4); o F3 foi retirado pelo usuário. A spec do Panorama é
`docs/superpowers/specs/2026-11-17-panorama-design.md`.

**Global Constraints:** as mesmas do plano do Panorama (seção «Global Constraints»): tokens de cor, nenhum `<button>`
cru fora das primitivas, `// @vitest-environment jsdom` e `afterEach(cleanup)` nos testes de tela, `npm test -- --run
--maxWorkers=2` **e** `npm run build` em `web/`, comentário cita o alvo pelo nome (nunca `arquivo:linha`, ledger nem
número de task), e as contagens do `CLAUDE.md` remedidas com o comando registrado ao lado delas, com data e métrica.

---

### Task A1: a faceta longa mostra pouco por padrão (F2)

**O achado.** Com muitos Pedidos rodando, a faceta "Pedido" do `FiltroDeDemanda` lista todos e empurra as outras
facetas para fora da vista. A primitiva já mostra o "Buscar em {faceta}" acima de `LIMITE_PARA_BUSCA_NA_FACETA` (8)
opções, mas continua listando todas embaixo.

**Decisão do usuário (2026-11-18), a recomendação do controlador:** na faceta com mais de
`LIMITE_PARA_BUSCA_NA_FACETA` opções e **sem busca digitada**, mostrar só as opções **marcadas** e as **5 primeiras**
(constante nova, exportada, com nome; as marcadas que já estão entre as 5 não contam duas vezes), mais um `Botao`
variante `secundario` "Mostrar todos os {N}" ({N} = total de opções da faceta), que vira "Mostrar menos" depois de
aberto. Com busca digitada, a lista é a filtrada inteira (a busca acha qualquer uma). Faceta com até 8 opções não muda.
O aberto/fechado é estado por faceta, dentro da primitiva, e começa fechado.

**Alcance:** a mudança é na primitiva `web/src/components/FiltroDeDemanda.tsx` (+ teste) e vale para todas as telas que
a usam (Fila do Setor, Tarefas, Pedidos, Separação, Expedição, Panorama) — fora da spec do Panorama, com o OK do
usuário. Nenhuma tela muda. Confira que os testes das seis telas continuam verdes (a suíte inteira).

- [ ] Testes que falham: faceta com 12 opções sem marcação mostra 5 e "Mostrar todos os 12"; com duas marcadas fora
  das 5 primeiras, mostra as 5 + as 2; "Mostrar todos" mostra as 12 e vira "Mostrar menos"; busca digitada mostra a
  filtrada inteira, sem o botão; faceta com 8 opções mostra as 8 sem botão; o botão não usa cor de estado.
- [ ] Implementar; mutação registrada (tirar as marcadas da lista curta → o teste delas fica vermelho).
- [ ] `CLAUDE.md`, bullet do `FiltroDeDemanda`: uma frase sobre a lista curta (o gatilho, as marcadas sempre à vista,
  a busca), sem mudar contagem que não mudou.
- [ ] Suíte inteira do front e `npm run build`. Commit `feat(filtro): a faceta longa mostra as marcadas e as cinco primeiras`.

### Task A2: o cabeçalho fixo e "Pedido" no lugar de "OS" (F1 e F4)

**F1.** Ao rolar a tabela do Panorama (a partir de `lg`), o `<thead>` sai da vista e não se sabe o que é cada coluna.
Ele deve ficar fixo no topo da janela (`sticky top-0`, com fundo do token da página e `z-index` acima das linhas). Hoje
a tabela mora num `div` com `overflow-x-auto`, que vira o contêiner de rolagem e impede o `sticky` de valer para a
página (o documento é quem rola: o `AppShell` não tem cabeçalho fixo nem `overflow` próprio). Tire esse `overflow` (a
`largura="ampla"` dá a largura) e confira que a tabela cabe a partir de `lg` sem rolagem horizontal; se não couber,
diga no relatório e proponha, em vez de esconder com `overflow-hidden`. O teste afirma as classes de `sticky` no
`<thead>` (ou no `<th>`, conforme a implementação) e que nenhum ancestral da tabela dentro da página tem `overflow-x-auto`.

**F4.** O rótulo "OS" vira **"Pedido"** (a OS pode ser numeração interna da fábrica, que não bate com o número do
Pedido do Rastru). Alcance: o `CabecalhoOrdenavel` da tabela, o cartão do celular se ele rotular "OS", as opções do
`SeletorDeOrdem` ("Pedido (crescente)", "Pedido (decrescente)"), a chave `'os'` de `ColunaDoPanorama` (vira
`'pedido'`) e o valor na URL (`?ordem=pedido-asc`; o antigo `os-asc` cai na ordem da tela, como qualquer valor
desconhecido), os testes, e a prosa: a spec do Panorama (D7, C4, C10, C11, C14, com uma nota de emenda datada de
2026-11-18 dizendo que a coluna "OS" virou "Pedido" na conferência), o plano do Panorama onde citar a coluna OS, e o
`CLAUDE.md` (bullet do `CabecalhoOrdenavel`, que lista "OS, Código, Descrição, Qtd e Prazo"). Antes de terminar,
`grep -rn "\bOS\b" web/src/panorama docs/superpowers/specs/2026-11-17-panorama-design.md CLAUDE.md` (e confira à mão
que o que sobrou não é a coluna).

- [ ] Testes que falham: o `thead` fixo; o cabeçalho "Pedido" ordena pelo número; `?ordem=pedido-desc` no F5 mantém a
  ordem; `?ordem=os-asc` cai na ordem da tela; o `SeletorDeOrdem` tem "Pedido (crescente)".
- [ ] Implementar; mutação registrada (tirar o `sticky` → teste vermelho).
- [ ] Suíte inteira do front e `npm run build`. Commit `fix(panorama): cabecalho fixo da tabela e Pedido no lugar de OS`.

### Task A3: o motivo da pausa fora da pílula (F5)

**O achado (2026-11-18, ao olhar os ajustes A1 e A2).** A pílula âmbar "Pausado: {motivo}" do `PrazoDaPeca`
(`web/src/panorama/pilulasDaPeca.tsx`) vira um balão de várias linhas quando o motivo é comprido. As outras telas (Fila,
Tarefas, Separação, Expedição, Pedido) mostram só "Pausado" na pílula.

**Decisão do usuário (2026-11-18), a recomendação do controlador:** a pílula `atencao` diz só **"Pausado"**; o motivo,
quando houver, vem logo abaixo dela, em texto pequeno e **neutro** (`text-sm text-tinta-fraca`), cortado em **2 linhas**
(`line-clamp-2`), com o motivo inteiro no `title` do elemento. Vale para a linha e o cartão (os dois usam o
`PrazoDaPeca`). Sem motivo, só a pílula.

- [ ] Testes que falham: a pílula diz exatamente "Pausado" (com o tom `atencao`); o motivo aparece fora da pílula, com
  `line-clamp-2` e `title` igual ao motivo; o motivo não usa cor de estado; sem motivo não há o texto.
- [ ] Implementar; mutação registrada (devolver o motivo para dentro da pílula → teste vermelho).
- [ ] Prosa: na spec do Panorama, a seção "11. Emendas da conferência (2026-11-18)" ganha uma frase sobre a pausa (C7
  continua verdadeiro: o motivo é mostrado, agora fora da pílula); no plano do Panorama, a descrição da coluna Prazo na
  Task 8 ("Pausado: {motivo}") ganha a nota da emenda.
- [ ] Suíte inteira do front e `npm run build`. Commit `fix(panorama): o motivo da pausa fora da pilula, em duas linhas`.

---

## Depois das tasks

Review de branch inteira (modelo mais capaz) sobre `git merge-base main HEAD..HEAD`, com a lista de Minor do ledger;
fix wave; re-review; PR.
