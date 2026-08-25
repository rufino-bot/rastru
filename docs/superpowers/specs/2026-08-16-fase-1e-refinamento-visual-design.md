# Fase 1E — Refinamento visual (tipografia + Home-dashboard)

Spec de design. Escrita em 2026-08-16, a partir do brainstorm da mesma data, depois de o usuário
ter usado a Fase 1D de verdade (via a verificação manual da Task 12) e formado opinião sobre o
resultado visual.

**Pré-requisito:** a Fase 1D **mergeada** (PR aprovado pelo usuário). Esta fase não toca a branch
`fase-1d-ui-e-ux` — começa limpa depois do merge, numa branch nova.

---

## 0. Sobre existir uma "Fase 1D parte 2" — e por que esta não é

A spec da 1D fecha com um corolário explícito: *"se daqui a três fases o sistema precisar de outra
passada de UI, isso não é uma fase planejada que ficou faltando; é o sinal de que o padrão não
pegou. O roadmap não tem — e não deve ganhar — uma 'Fase 1D parte 2'."* Vale nomear a tensão em vez
de fingir que não existe: esta spec chega um dia depois da 1D fechar.

**Isto não é esse cenário.** O que o corolário advertia era uma reestilização ampla porque o padrão
de primitivas não segurou — múltiplas telas fora do sistema, cópias divergentes, retrofit incompleto.
Não é o caso aqui:

- A troca de fonte é **exatamente o gancho que a 1D deixou pronto de propósito**: *"Reversível:
  trocar por uma fonte própria depois é mudar um token, não reescrever telas"* (§4 da spec da 1D).
  Um token muda; zero tela é reescrita.
- O reforço da Home **usa as primitivas existentes** (`Pagina`, `Pilula`) e o dado que a própria
  Task 11 já busca — não introduz um padrão novo, não reabre as outras 6 telas.
- O escopo é deliberadamente **pequeno e nomeado**: duas mudanças, não uma "passada".

Se esta spec tivesse pedido para reabrir espaçamento/densidade em todas as telas, ou uma primitiva
nova, aí sim seria o sinal que o corolário descreve — e a resposta certa seria recusar ou perguntar
por que a 1D não pegou. Não é isto.

---

## 1. Escopo

**Dentro:**
1. Trocar a pilha de fonte do sistema (token `--font-sans` / `--font-mono`) por IBM Plex Sans /
   IBM Plex Mono, auto-hospedadas.
2. Reforçar a `HomePage`: o cartão de Pedidos ganha um resumo por status; uma seção nova mostra os
   pedidos abertos há mais tempo.

**Fora (decidido explicitamente no brainstorm, não esquecido):**
- Densidade/espaçamento das outras 6 telas — continuam com o respiro generoso validado pela 1D.
  Só a Home muda de densidade.
- "Prazo de entrega" e "pedidos em atraso" — o domínio **não tem** campo de data prevista de
  entrega hoje (`Pedido` só tem `DataAbertura` e `DataConclusao`, conferido em
  `specs/02-modelo-de-dados.sql:152-181`). Adicionar isso é mudança de schema **e** de formulário de
  cadastro, não um reforço de Home — candidato a fase própria, registrado abaixo (§5).
- Qualquer coisa da "Fase 6 — KPIs" do roadmap (tempo médio por setor, tempo por pedido) — depende
  de infraestrutura de rastreamento por Setor que só existe a partir da Fase 3. Não confundir com
  esta fase: aqui não há métrica de processo, só reapresentação de dado que já existe.

---

## 2. Tipografia

**Troca de token, não de arquitetura.** Em `web/src/index.css`:

```css
--font-sans: 'IBM Plex Sans', ui-sans-serif, system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
--font-mono: 'IBM Plex Mono', ui-monospace, "Cascadia Mono", "Segoe UI Mono", "Roboto Mono", Menlo, monospace;
```

A pilha do sistema continua como **fallback** — se a fonte auto-hospedada falhar em carregar (rede
ruim na fábrica, exatamente o cenário que a 1D projetava), a tela não quebra, só volta pra fonte do
SO.

**Auto-hospedada, não CDN.** Motivo é o mesmo da decisão original da 1D: zero requisição de rede em
runtime. Os arquivos `.woff2` (peso variável, 2 famílias: Sans e Mono, cada uma só os cortes de peso
que a spec da 1D já usa — 400/600/700 aproximadamente) entram em `web/public/fontes/` ou
equivalente, referenciados por `@font-face` no `index.css`, e o build do Vite os inclui no bundle.
Custo estimado: 30-50kb somados (fontes variáveis modernas são pequenas), uma vez, com cache do
navegador depois disso.

**Aplica no sistema inteiro.** É um token único (`font-sans`/`font-mono` do Tailwind), usado em
todo componente e toda tela — não há como escopar só para a Home sem duplicar o mecanismo de tema
que a 1D construiu.

**Prova de que a licença Open Font (IBM Plex é OFL) permite auto-hospedar** — verificar antes de
baixar os arquivos, não depois; é o tipo de coisa que se checa uma vez e nunca mais.

---

## 3. Home — resumo de status + pedidos há mais tempo

### 3.1 Cartão de Pedidos com resumo de status

O cartão "pedidos abertos" (que hoje só mostra um número) ganha uma segunda linha com os 5 status
do domínio (`CK_Pedido_Status`: `Aberto`, `EmProducao`, `AguardandoExpedicao`, `Concluido`,
`Cancelado`), cada um com a contagem — **inclusive os com contagem zero**, pela mesma disciplina que
a Task 11 já seguiu (nenhum número inventado, nenhum omitido). O cartão inteiro continua sendo um
`<Link>` só, para `/pedidos` — os status não são clicáveis individualmente (evitaria link dentro de
link, que é HTML inválido e quebra acessibilidade).

### 3.2 "Pedidos abertos há mais tempo"

Seção nova abaixo da grade de cartões. Mostra os **5 pedidos com `status` fora de `Concluido` e
`Cancelado`, ordenados por `DataAbertura` crescente** (os mais antigos primeiro) — um sinal de risco
honesto ("isto está parado há mais tempo"), sem fingir um prazo prometido que o domínio não tem.
Cada linha (número, cliente, status) é um link para `/pedidos/:id`.

**Estado vazio real** (nenhum pedido aberto): a seção mostra um texto neutro, não desaparece
silenciosamente — a exemplo do `EstadoVazio` já usado nas outras telas, mas sem repetir o padrão
"vazio junto do banner de erro" que já pegou duas vezes nesta fase (Task 8, Task 10): **a seção só
renderiza como "vazia" quando a leitura teve sucesso e a lista é realmente `[]`**, nunca a partir de
uma falha de rede.

### 3.3 Fonte de dado — zero requisição nova

`HomePage.tsx` já chama `listarPedidos()` para contar "pedidos abertos" (Task 11). O resumo por
status e a lista de "há mais tempo" são derivados do **mesmo array**, na mesma resposta:

```ts
const porStatus = STATUS.reduce((acc, s) => ({ ...acc, [s]: pedidos.filter(p => p.status === s).length }), {})
const maisAntigos = pedidos
  .filter(p => p.status !== 'Concluido' && p.status !== 'Cancelado')
  .sort((a, b) => a.dataAbertura.localeCompare(b.dataAbertura))
  .slice(0, 5)
```

Nenhum endpoint novo, nenhuma migração de schema. `Componentes`, `Materiais`, `Setores` continuam
como cartões simples — só `Pedidos` tem status pra mostrar, os outros três domínios não têm campo
equivalente.

### 3.4 Erro de rede

Mesmo padrão já provado pela Task 11: enquanto a leitura falha, os números mostram traço "—" (não
"0", que seria uma afirmação falsa), e o banner de erro cobre a explicação. A seção de "pedidos há
mais tempo" não renderiza (nem vazia nem com dado velho) enquanto `erro !== null`.

---

## 4. Testes

Segue o padrão já estabelecido em `HomePage.test.tsx` (Task 11): `fetchPorRota`/`respostaJson`,
`@vitest-environment jsdom`, asserções que discriminam (`within(cartao).getByText(...)`, não
substring — é a lição que a review da própria Task 11 já ensinou). Casos mínimos:

- resumo de status mostra os 5 status, inclusive contagem 0;
- "pedidos há mais tempo" ordena corretamente e exclui `Concluido`/`Cancelado`;
- lista para em 5 mesmo com mais pedidos elegíveis;
- cada linha da lista navega para o `/pedidos/:id` certo;
- traço "—" (não "0" nem lista vazia falsa) quando a leitura falha;
- texto neutro (não `EstadoVazio` junto de erro) quando a lista é genuinamente vazia.

---

## 5. Candidato de fase futura, registrado e não esquecido

**"Prazo de entrega" e "pedidos em atraso"** — valioso de verdade pra operação, mas exige:
1. `DataPrevistaEntrega` (ou nome equivalente) em `dbo.Pedido`, `specs/02-modelo-de-dados.sql`
   primeiro (Database First, `CLAUDE.md`).
2. Campo novo no formulário de criação de Pedido (hoje não pede prazo nenhum).
3. Definição de negócio do que é "atraso" — provavelmente `DataPrevistaEntrega < hoje AND status
   NOT IN (Concluido, Cancelado)`, mas isso é decisão de domínio, não de UI, e pertence a
   `01-dominio-e-regras-de-negocio.md`.

Não é escopo desta fase. Fica registrado aqui para não se perder — mesma disciplina que motivou o
"DÍVIDA ABERTA" do ledger da 1D.
