# Fase 1E — Refinamento visual: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trocar a pilha de fonte do sistema por IBM Plex auto-hospedada e reforçar a `HomePage`
com um resumo por status do Pedido e uma seção "pedidos abertos há mais tempo", sem tocar nas
outras seis telas.

**Architecture:** A tipografia é troca de **token** (`--font-sans`/`--font-mono` em
`web/src/index.css`) mais `@font-face` apontando para `.woff2` commitados em `web/public/fontes/` —
zero tela reescrita, zero requisição de rede em runtime. A Home deriva tudo do array que
`listarPedidos()` **já** devolve dentro do `Promise.all` existente ([HomePage.tsx:44-51](web/src/pages/HomePage.tsx:44)):
nenhum endpoint novo, nenhuma mudança de schema. O par `STATUS_DO_PEDIDO`/`tomDoStatus` é extraído
para um módulo compartilhado porque a Home e a `PedidosPage` passam a precisar do mesmo mapa.

**Tech Stack:** React 19 + TypeScript, Vite 8, Tailwind 4 (`@theme`), Vitest 4 + Testing Library,
jsdom. Sem dependência nova em `package.json` — os `.woff2` entram como arquivo versionado.

**Spec de origem:** [docs/superpowers/specs/2026-08-16-fase-1e-refinamento-visual-design.md](docs/superpowers/specs/2026-08-16-fase-1e-refinamento-visual-design.md)

---

## Global Constraints

Toda task herda isto. Vem de `CLAUDE.md` (seção "Interface") e da spec da 1D.

- **Cores só pelos tokens** de `web/src/index.css` (`text-tinta`, `bg-acao`, `border-borda`…).
  `text-gray-*`/`text-red-600` e afins não existem em `web/src/` e a guarda
  `web/src/tema/semCorForaDaPaleta.test.ts` reprova nomeando arquivo e linha.
- **Nada de modificador de opacidade em cor** (`bg-positivo/10`): vira `color-mix(in oklab, …)`,
  escapa das guardas de contraste e já sangrou três vezes. Guarda:
  `web/src/tema/semModificadorDeOpacidadeEmCor.test.ts`.
- **Não escreva primitiva à mão.** Pílula, banner de erro, item de lista, estado vazio e estado de
  carregando estão em `web/src/components/`.
- **Cor de identidade nunca significa estado; cor de estado nunca decora.** Verde (`positivo`) e
  vermelho (`negativo`) ficam reservados a aprovado/ativo e reprovado/perda/erro.
- **Teste de tela** declara `// @vitest-environment jsdom` no topo, usa `web/src/testes/api.ts`
  (`respostaJson`, `fetchPorRota`) e tem `afterEach(cleanup)` explícito — este projeto **não** usa
  `globals: true`.
- **`npm run build` faz parte do ciclo**, não só `npm test`: o Vitest não faz typecheck, então erro
  de tipo em `.test.tsx` quebra o build com a suíte verde.
- **Asserção discrimina.** `within(cartao).getByText('1')`, nunca `textContent.toContain('1')` —
  "41" contém "1" e um `toContain` passaria com a fiação trocada (lição da review da Task 11).
- **Nomes de domínio em português**, espelhando o DDL (`Pedido`, `status`, `dataAbertura`).
- O projeto **não usa `data-testid`** (medido: zero ocorrências em `web/src/`). Asserção é por
  papel ARIA e por texto.

### Baseline — MEDIDA nesta bancada em 2026-08-28, não copiada do ledger

```
FRONT : 374 testes / 31 arquivos, verdes.  (`cd web && npm test -- --run`)
BUILD : limpo.                              (`cd web && npm run build`)
```

Alvo ao fim do plano: **391 testes / 33 arquivos**. Cada task declara o próprio delta; se um deles
não bater, **corrija a baseline das tasks seguintes na mesma passada** — total absoluto propaga
erro task a task.

### O que a medição de 2026-08-28 derrubou da spec — leia antes da Task 1

A spec §2 tem quatro afirmações sobre a fonte. Três estavam erradas, e as três corrigidas já estão
embutidas nos passos da Task 1. Estão aqui para que ninguém "conserte" o plano de volta para a spec:

| Spec §2 diz | Medido | Onde |
|---|---|---|
| "peso variável, 2 famílias" | **Não existe IBM Plex Mono variável.** `npm view @fontsource-variable/ibm-plex-mono` → **E404**. Só o Sans tem corte variável | Task 1 usa Sans variável (1 arquivo) + Mono **estático** (3 arquivos) |
| "os cortes de peso que a 1D já usa — 400/600/700 aproximadamente" | Sans precisa de **400/500/600/700** (`font-medium` aparece 12×, `font-semibold` 15×). Mono precisa de **400/600/700** | O Sans variável (100–700) cobre os quatro num arquivo; o Mono ganha três arquivos |
| "Custo estimado: 30-50kb somados" | **90.948 bytes ≈ 88,8 KB** (45.712 + 14.708 + 15.620 + 14.908), subset `latin` só | Task 1, Step 3 |
| "a licença OFL permite auto-hospedar" | **CONFIRMADO**, com condição que a spec não registra | ver abaixo |

**A licença, medida em disco (Step 0 que a spec pedia e ninguém tinha feito):** SIL Open Font
License 1.1. A **cláusula 2** permite empacotar e redistribuir com qualquer software *"provided that
each copy contains the above copyright notice and this license"* — então os `LICENSE-*.txt` ao lado
dos `.woff2` são **obrigação de licença**, não zelo: apagá-los é violar a OFL. A **cláusula 3**
(Reserved Font Name) **não morde**: `grep -c "with Reserved Font Name"` devolve **0** nos dois
pacotes — a IBM não reservou o nome, então usar `font-family: 'IBM Plex Sans'` é permitido. A
cláusula 1 (não vender a fonte sozinha) não se aplica.

---

## Estrutura de arquivos

| Arquivo | Responsabilidade | Task |
|---|---|---|
| `web/public/fontes/*.woff2` (4) | Os arquivos de fonte, versionados | 1 |
| `web/public/fontes/LICENSE-IBM-Plex-Sans.txt` | Exigência da cláusula 2 da OFL | 1 |
| `web/public/fontes/LICENSE-IBM-Plex-Mono.txt` | Exigência da cláusula 2 da OFL | 1 |
| `web/public/fontes/PROVENIENCIA.md` | De onde vieram os binários (pacote@versão + comando) | 1 |
| `web/src/index.css` | `@font-face` + os dois tokens | 1 |
| `web/src/tema/fontesAutoHospedadas.test.ts` | Guarda: todo `url()` do `@font-face` aponta para arquivo que existe | 1 |
| `web/src/api/cadastros.test.ts` | Guarda: `listarPedidos()` não é paginado | 2 |
| `web/src/pedidos/statusDoPedido.ts` | `STATUS_DO_PEDIDO`, `ENCERRADOS`, `tomDoStatus` — o mapa que Home e `PedidosPage` compartilham | 3 |
| `web/src/pedidos/statusDoPedido.test.ts` | Teste do módulo | 3 |
| `web/src/pages/PedidosPage.tsx` | Passa a importar `tomDoStatus` em vez de declarar o próprio | 3 |
| `web/src/pages/HomePage.tsx` | Resumo por status (3) + seção "há mais tempo" (4) | 3, 4 |
| `web/src/pages/HomePage.test.tsx` | Testes das duas | 3, 4 |
| `specs/06-roadmap-mvp.md` | Registrar a Fase 1E | 5 |

---

## Task 1: Tipografia — IBM Plex auto-hospedada

**Files:**
- Create: `web/public/fontes/ibm-plex-sans-latin-wght-normal.woff2`
- Create: `web/public/fontes/ibm-plex-mono-latin-400-normal.woff2`
- Create: `web/public/fontes/ibm-plex-mono-latin-600-normal.woff2`
- Create: `web/public/fontes/ibm-plex-mono-latin-700-normal.woff2`
- Create: `web/public/fontes/LICENSE-IBM-Plex-Sans.txt`
- Create: `web/public/fontes/LICENSE-IBM-Plex-Mono.txt`
- Create: `web/public/fontes/PROVENIENCIA.md`
- Create: `web/src/tema/fontesAutoHospedadas.test.ts`
- Modify: `web/src/index.css:1-23` (bloco novo de `@font-face` antes do `@theme`; troca dos dois tokens)

**Interfaces:**
- Consumes: nada.
- Produces: os tokens `--font-sans` e `--font-mono` passam a nomear `'IBM Plex Sans'` e
  `'IBM Plex Mono'` com a pilha do SO atrás. Nenhuma outra task depende disto.

**Delta de teste:** +4 testes, +1 arquivo → **378 / 32**.

**Por que aquisição, licença, `@font-face` e token são UMA task:** um revisor não consegue aprovar
o `@font-face` e reprovar a cópia dos arquivos — o CSS sem o binário aponta para 404 e a tela cai
no fallback **em silêncio**, que é exatamente o modo de falha que a guarda do Step 8 existe para
matar.

- [ ] **Step 1: Baixar os pacotes FORA do repositório**

Os pacotes são só a **fonte** dos binários; eles **não** entram em `web/package.json`. Instalar
dentro de `web/` mexeria em `package.json`/`package-lock.json` para depois desfazer — instale num
diretório temporário e copie de lá.

```bash
mkdir -p /tmp/plex && cd /tmp/plex && npm init -y >/dev/null && npm i --silent @fontsource-variable/ibm-plex-sans@5.3.0 @fontsource/ibm-plex-mono@5.3.0
```

Esperado: sai sem erro e `ls /tmp/plex/node_modules/@fontsource-variable/ibm-plex-sans/files/`
lista os `.woff2`.

- [ ] **Step 2: Copiar os quatro `.woff2` e as duas licenças**

Da raiz do repositório:

```bash
mkdir -p web/public/fontes
cp /tmp/plex/node_modules/@fontsource-variable/ibm-plex-sans/files/ibm-plex-sans-latin-wght-normal.woff2 web/public/fontes/
cp /tmp/plex/node_modules/@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2 web/public/fontes/
cp /tmp/plex/node_modules/@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2 web/public/fontes/
cp /tmp/plex/node_modules/@fontsource/ibm-plex-mono/files/ibm-plex-mono-latin-700-normal.woff2 web/public/fontes/
cp /tmp/plex/node_modules/@fontsource-variable/ibm-plex-sans/LICENSE web/public/fontes/LICENSE-IBM-Plex-Sans.txt
cp /tmp/plex/node_modules/@fontsource/ibm-plex-mono/LICENSE web/public/fontes/LICENSE-IBM-Plex-Mono.txt
```

**Subset `latin` SÓ, e é decisão medida, não descuido.** O `unicode-range` do corte `latin` é
`U+0000-00FF` mais pontuação — todo o português cabe nele (`ç`, `ã`, `õ`, `é`, `â`), e o travessão
`—` que a Home usa é `U+2014`, dentro de `U+2000-206F`. Um caractere fora da faixa cai no fallback
do SO pelo próprio `unicode-range`, sem quebrar a tela. Levar `latin-ext` junto somaria ~73 KB para
cobrir alfabeto que este sistema não escreve.

- [ ] **Step 3: Conferir os tamanhos**

```bash
ls -l web/public/fontes/*.woff2 | awk '{s+=$5; print $5, $9} END {print "TOTAL:", s}'
```

Esperado, byte a byte:

```
45712 ...ibm-plex-sans-latin-wght-normal.woff2
14708 ...ibm-plex-mono-latin-400-normal.woff2
15620 ...ibm-plex-mono-latin-600-normal.woff2
14908 ...ibm-plex-mono-latin-700-normal.woff2
TOTAL: 90948
```

Se o total divergir, **pare e reporte** — pacote de versão diferente, ou arquivo errado copiado.
Não ajuste o número no plano para casar com o que veio.

- [ ] **Step 4: Escrever a proveniência**

Binário commitado sem proveniência é binário que ninguém consegue regenerar. Crie
`web/public/fontes/PROVENIENCIA.md`:

````markdown
# De onde vieram estes arquivos

IBM Plex Sans e IBM Plex Mono, subset `latin`, extraídos dos pacotes npm do Fontsource em
2026-08-28. Os pacotes NÃO são dependência deste projeto — foram usados uma vez, como fonte dos
binários, e os arquivos vivem versionados aqui.

| Arquivo | Pacote | Versão |
|---|---|---|
| `ibm-plex-sans-latin-wght-normal.woff2` | `@fontsource-variable/ibm-plex-sans` | 5.3.0 |
| `ibm-plex-mono-latin-{400,600,700}-normal.woff2` | `@fontsource/ibm-plex-mono` | 5.3.0 |

**Não existe `@fontsource-variable/ibm-plex-mono`** — medido em 2026-08-28, o registro devolve
E404. É por isso que o Sans é um arquivo variável (100–700) e o Mono são três estáticos.

Os pesos do Mono são os três medidos em uso em `web/src/`: 400 (`font-mono`), 600
(`font-mono font-semibold`) e 700 (o único `<strong className="font-mono">`, em
`web/src/pages/PedidoDetalhePage.tsx`). Acrescentar peso de Mono sem um consumidor é baixar
arquivo que ninguém usa.

Para regenerar:

```bash
mkdir -p /tmp/plex && cd /tmp/plex && npm init -y && npm i @fontsource-variable/ibm-plex-sans@5.3.0 @fontsource/ibm-plex-mono@5.3.0
# e copiar os quatro .woff2 de node_modules/**/files/ para cá
```

## Licença

SIL Open Font License 1.1 — `LICENSE-IBM-Plex-Sans.txt` e `LICENSE-IBM-Plex-Mono.txt`, nesta pasta.

**Os dois `.txt` são obrigação da licença, não documentação.** A cláusula 2 da OFL permite
redistribuir a fonte junto de qualquer software *"provided that each copy contains the above
copyright notice and this license"*. Apagá-los para "limpar a pasta" viola a licença.

A cláusula 3 (Reserved Font Name) não se aplica: a IBM **não** reservou o nome — o cabeçalho de
copyright dos dois `LICENSE` não traz a fórmula `with Reserved Font Name` (medido: `grep -c`
devolve 0 nos dois). Por isso o `@font-face` pode declarar `font-family: 'IBM Plex Sans'`.
````

- [ ] **Step 5: Escrever a guarda ANTES de mexer no CSS (ela tem de falhar)**

Crie `web/src/tema/fontesAutoHospedadas.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { readFileSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

/**
 * Modo de falha que esta guarda existe para matar: um `url()` com caminho errado no `@font-face`
 * não quebra build nem suíte — o navegador pede 404, o `unicode-range` não casa nada e a tela
 * simplesmente aparece na fonte do SO. Fica IDÊNTICA ao que era antes desta fase, e a suíte fica
 * verde. Ninguém percebe até alguém abrir o DevTools.
 */
function urlsDeFonte(fonte: string): string[] {
  return [...fonte.matchAll(/url\(['"](\/fontes\/[^'"]+)['"]\)/g)].map(([, caminho]) => caminho)
}

describe('fontes auto-hospedadas', () => {
  // CONTROLE POSITIVO, e ele vem primeiro de propósito: se a regex parar de casar (alguém troca
  // aspas simples por dupla, ou o caminho), `urlsDeFonte` devolve [] e TODO o `for` abaixo passa
  // por não iterar nada — a auditoria falharia em VERDE. Este teste é o que impede isso.
  it('acha os quatro url() de fonte no index.css', () => {
    expect(urlsDeFonte(css)).toHaveLength(4)
  })

  it('todo url() do @font-face aponta para arquivo que existe em web/public/', () => {
    const publico = fileURLToPath(new URL('../../public', import.meta.url))
    for (const caminho of urlsDeFonte(css)) {
      expect(existsSync(`${publico}${caminho}`), `arquivo ausente: web/public${caminho}`).toBe(true)
    }
  })

  // A OFL cláusula 2 exige que cada cópia carregue a licença. Sem esta guarda, uma faxina em
  // `public/` que apague os .txt vira violação de licença sem nenhum sinal.
  it('mantém as duas licenças OFL ao lado dos .woff2', () => {
    const publico = fileURLToPath(new URL('../../public/fontes', import.meta.url))
    expect(existsSync(`${publico}/LICENSE-IBM-Plex-Sans.txt`)).toBe(true)
    expect(existsSync(`${publico}/LICENSE-IBM-Plex-Mono.txt`)).toBe(true)
  })

  // O ponto inteiro da decisão da 1D: a pilha do SO fica ATRÁS, para a tela não quebrar quando a
  // fonte não carregar no wifi da fábrica. Uma mutação que deixe só `'IBM Plex Sans'` no token
  // morre aqui.
  it('mantém a pilha do sistema como fallback nos dois tokens', () => {
    expect(css).toMatch(/--font-sans:\s*'IBM Plex Sans',[^;]*system-ui[^;]*sans-serif;/)
    expect(css).toMatch(/--font-mono:\s*'IBM Plex Mono',[^;]*ui-monospace[^;]*monospace;/)
  })
})
```

- [ ] **Step 6: Rodar a guarda e ver as duas certas falharem**

```bash
cd web && npx vitest run src/tema/fontesAutoHospedadas.test.ts
```

Esperado: **2 failed, 2 passed** — e quais são os dois de cada lado importa mais que o número.

| Teste | Agora | Por quê |
|---|---|---|
| `acha os quatro url() de fonte` | **FALHA** | o CSS ainda não tem `@font-face`, então a regex devolve `[]` |
| `todo url() aponta para arquivo que existe` | **passa** | passa **por não iterar nada** — é exatamente a auditoria-que-falha-em-verde que o controle positivo acima existe para impedir. Se ele passasse sozinho, a guarda seria decoração |
| `mantém as duas licenças OFL` | **passa** | os `.txt` já foram copiados no Step 2 |
| `mantém a pilha do sistema como fallback` | **FALHA** | os tokens ainda são a pilha do SO nua |

Se o primeiro **passar** aqui, pare: ou o CSS já foi editado, ou a regex está casando outra coisa.

- [ ] **Step 7: Escrever os `@font-face` e trocar os tokens**

Em `web/src/index.css`, **entre a linha 1 (`@import "tailwindcss";`) e o comentário do bloco
`@theme`**, insira:

```css
/*
  IBM Plex, AUTO-HOSPEDADA — zero requisição de rede em runtime, pelo mesmo motivo da decisão
  original da 1D: o wifi da fábrica. Os arquivos e a proveniência estão em `public/fontes/`.

  LICENÇA: OFL 1.1. A cláusula 2 exige que cada cópia carregue o aviso de copyright e a licença —
  é por isso que `public/fontes/` tem os dois `LICENSE-*.txt`. Não são documentação; apagá-los é
  violar a licença. (A cláusula 3, Reserved Font Name, não morde: a IBM não reservou o nome, então
  declarar `font-family: 'IBM Plex Sans'` aqui é permitido.)

  SUBSET `latin` SÓ. O `unicode-range` abaixo é `U+0000-00FF` mais pontuação, e todo o português
  cabe nele — inclusive o travessão `—` (U+2014) que a Home usa para dizer "ainda não sei". Um
  caractere fora da faixa cai no fallback do SO pelo próprio `unicode-range`, sem quebrar a tela.

  O SANS É VARIÁVEL (um arquivo, 100–700) e cobre os quatro pesos que as telas usam: 400, 500
  (`font-medium`), 600 (`font-semibold`) e 700 (`<strong>`). O MONO NÃO TEM VERSÃO VARIÁVEL
  publicada — medido em 2026-08-28, `@fontsource-variable/ibm-plex-mono` devolve E404 — daí três
  arquivos estáticos, nos três pesos que `web/src/` de fato pede.

  `font-display: swap`: a tela aparece na hora com a fonte do SO e troca quando a Plex chega. O
  padrão (`block`) esconderia o texto por até 3s no wifi ruim, que é justamente o que se quer
  evitar.

  ⚠️ ESTES BLOCOS FICAM FORA DO `@theme`, E ANTES DELE. `contraste.test.ts` lê o tema com
  `/@theme\s*\{([\s\S]*?)\n\}/`, que PARA no primeiro `\n}` — um `@font-face` dentro do `@theme`
  truncaria a leitura da paleta e a guarda de contraste passaria a medir meia paleta, em silêncio.
*/
@font-face {
  font-family: 'IBM Plex Sans';
  font-style: normal;
  font-display: swap;
  font-weight: 100 700;
  src: url('/fontes/ibm-plex-sans-latin-wght-normal.woff2') format('woff2-variations');
  unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD;
}

@font-face {
  font-family: 'IBM Plex Mono';
  font-style: normal;
  font-display: swap;
  font-weight: 400;
  src: url('/fontes/ibm-plex-mono-latin-400-normal.woff2') format('woff2');
  unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD;
}

@font-face {
  font-family: 'IBM Plex Mono';
  font-style: normal;
  font-display: swap;
  font-weight: 600;
  src: url('/fontes/ibm-plex-mono-latin-600-normal.woff2') format('woff2');
  unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD;
}

@font-face {
  font-family: 'IBM Plex Mono';
  font-style: normal;
  font-display: swap;
  font-weight: 700;
  src: url('/fontes/ibm-plex-mono-latin-700-normal.woff2') format('woff2');
  unicode-range: U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD;
}
```

E, dentro do `@theme`, troque **só** as duas linhas dos tokens ([index.css:22-23](web/src/index.css:22)),
preservando o comentário de cima adaptado:

```css
  /* IBM Plex auto-hospedada (ver os `@font-face` acima). A pilha do SO fica ATRÁS como fallback:
     se o `.woff2` não carregar, a tela volta para a fonte do sistema em vez de quebrar.
     Monoespaçada para código de peça e material — decisão funcional (alinha na coluna, facilita
     conferir contra o desenho na bancada). */
  --font-sans: 'IBM Plex Sans', ui-sans-serif, system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  --font-mono: 'IBM Plex Mono', ui-monospace, "Cascadia Mono", "Segoe UI Mono", "Roboto Mono", Menlo, monospace;
```

- [ ] **Step 8: Rodar a guarda e ver as quatro passarem**

```bash
cd web && npx vitest run src/tema/fontesAutoHospedadas.test.ts
```

Esperado: **4 passed**.

- [ ] **Step 9: Provar que as guardas de tema vizinhas continuam íntegras**

O risco desta task não é quebrar a suíte — é **truncar** a leitura do `@theme` e deixar a guarda de
contraste medindo menos do que mede hoje, em verde.

```bash
cd web && npx vitest run src/tema/
```

Esperado: os quatro arquivos de `src/tema/` passam, e `contraste.test.ts` continua com **37
testes** — medido nesta bancada em 2026-08-28, antes desta task. Contagem menor significa que o
`@theme` foi truncado e a guarda de contraste passou a medir meia paleta **em verde**; confira
onde os `@font-face` foram parar.

- [ ] **Step 10: Suíte inteira e build**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: **378 passed / 32 files**, build limpo. O tamanho do CSS sobe alguns bytes (os
`@font-face`); os `.woff2` **não** entram no bundle JS/CSS — o Vite copia `public/` verbatim para
`dist/`. Confirme:

```bash
ls -l web/dist/fontes/
```

Esperado: os quatro `.woff2` e os dois `.txt`, com os mesmos tamanhos do Step 3.

- [ ] **Step 11: Verificar no navegador que a fonte REALMENTE trocou**

A suíte prova que o arquivo existe e que o token o nomeia. Ela **não** prova que o navegador
aplicou a fonte — jsdom não carrega `@font-face`. Suba o front (`npm run dev` via a ferramenta de
preview do projeto, não por Bash), abra a Home e rode no console:

```js
getComputedStyle(document.body).fontFamily
```

Esperado: a string começa por `IBM Plex Sans`. E, para provar que o arquivo **foi baixado** (e não
que a string está lá com 404 atrás):

```js
[...document.fonts].map(f => [f.family, f.status])
```

Esperado: `IBM Plex Sans` com status `loaded`. Registre as duas saídas no relatório da task.

- [ ] **Step 12: Commit**

```bash
git add web/public/fontes web/src/index.css web/src/tema/fontesAutoHospedadas.test.ts
git commit -m "feat(1e): IBM Plex auto-hospedada, com licenca OFL e guarda de caminho

Sans variavel (100-700, um arquivo) e Mono estatico nos tres pesos medidos em uso
(400/600/700). Subset latin so: 88,8 KB somados. Nao existe @fontsource-variable/
ibm-plex-mono (E404), medido — dai o Mono estatico.

Os dois LICENSE-*.txt sao exigencia da clausula 2 da OFL, nao documentacao.

A guarda nova mata o modo de falha silencioso: url() errado no @font-face nao quebra
build nem suite, so faz a tela cair no fallback do SO sem ninguem notar.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: Guarda — `listarPedidos()` não é paginado

**Files:**
- Modify: `web/src/api/cadastros.test.ts` (um `it` novo, logo depois de `lista pedidos`, hoje em [cadastros.test.ts:275-291](web/src/api/cadastros.test.ts:275))

**Interfaces:**
- Consumes: `listarPedidos` e `PedidoDto` de `web/src/api/cadastros.ts`.
- Produces: nada em código. Produz a **licença para as Tasks 3 e 4 derivarem da resposta inteira**.

**Delta de teste:** +1 teste, +0 arquivo → **379 / 32**.

**A dívida que esta task fecha, e a decisão do usuário (2026-08-28):** `listarPedidos()` devolve
`Promise<PedidoDto[]>` ([cadastros.ts:131](web/src/api/cadastros.ts:131)) — **não** é paginado,
diferente de `listarComponentes`, que devolve `PaginaDe<T>`. A Home inteira desta fase deriva desse
array. `Pedido` é a entidade de maior volume do sistema e a 1B já paginou `Componente` por esse
motivo; no dia em que alguém paginar `/pedidos`, o resumo por status vira "contagem da primeira
página" e a seção "há mais tempo" vira "os mais antigos dos 20 primeiros" — **as duas em silêncio**.
O usuário escolheu **guarda executável que morre**, e não parágrafo de dívida: é o padrão que o
projeto já usa (`semCorForaDaPaleta`, `contraste`).

- [ ] **Step 1: Escrever o teste que morre**

Em `web/src/api/cadastros.test.ts`, logo **abaixo** do `it('lista pedidos', ...)` existente:

```ts
  // GUARDA DE DÍVIDA — Fase 1E. NÃO é teste de comportamento novo: é o alarme que dispara no dia
  // em que `/pedidos` for paginado, como `/componentes` já foi na 1B.
  //
  // A `HomePage` deriva TRÊS coisas do array que esta função devolve: a contagem de "pedidos
  // abertos", o resumo pelos 5 status e a lista "abertos há mais tempo". As três só são verdadeiras
  // porque a resposta traz o conjunto INTEIRO. Se `listarPedidos` passar a devolver uma página, as
  // três viram meia-verdade — "contagem da primeira página", "os mais antigos dos 20 primeiros" —
  // e nenhuma delas fica vermelha sozinha, porque continuam sendo números plausíveis.
  //
  // Este teste morre de DOIS jeitos, de propósito: `Array.isArray` mata a troca do tipo de retorno
  // em tempo de execução, e a anotação de tipo abaixo dele mata a mesma troca em `tsc -b` (o
  // Vitest não faz typecheck — `npm test` verde não prova que compila).
  //
  // Quando ele ficar vermelho, o conserto NÃO é apagá-lo: é decidir o que a Home passa a mostrar
  // (endpoint de resumo no backend, ou pedir `tamanho` grande explicitamente) e só então
  // reescrever esta guarda.
  it('devolve o conjunto inteiro de pedidos, nao uma pagina — a HomePage depende disso', async () => {
    const vinteECinco = Array.from({ length: 25 }, (_, i) => ({
      id: i + 1, numero: `PED-${String(i + 1).padStart(3, '0')}`, cliente: 'Cliente X',
      tipo: 'Fabricacao', status: 'Aberto', dataAbertura: '2026-07-28T09:30:00-03:00',
      criadoPorUsuarioId: 1,
    }))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify(vinteECinco), { status: 200 }),
    ))

    const pedidos = await listarPedidos()

    // Vinte e cinco, e não vinte: 20 é o tamanho de página padrão de `listarComponentes`. Se
    // alguém paginar `/pedidos` copiando aquele default, esta asserção é a que fica vermelha.
    expect(Array.isArray(pedidos)).toBe(true)
    expect(pedidos).toHaveLength(25)

    // A guarda de TIPO, irmã da de runtime. Se `listarPedidos` virar
    // `Promise<PaginaDe<PedidoDto>>`, esta linha para de compilar e `npm run build` reprova.
    const _contrato: () => Promise<PedidoDto[]> = listarPedidos
    expect(_contrato).toBe(listarPedidos)
  })
```

E acrescente `type PedidoDto` ao `import` do topo do arquivo
([cadastros.test.ts:2-9](web/src/api/cadastros.test.ts:2)), junto de `type ConflitoDeCadastro`.

- [ ] **Step 2: Rodar e ver PASSAR (este é um teste de caracterização)**

```bash
cd web && npx vitest run src/api/cadastros.test.ts
```

Esperado: **52 passed** (51 antes + 1). Ele passa de primeira **de propósito** — descreve o
contrato que já existe. O que se prova não é que ele falha hoje, e sim que ele falha quando o
contrato mudar. É o Step 3 que prova isso.

- [ ] **Step 3: Provar por MUTAÇÃO que a guarda realmente morre**

Sem este passo a guarda é decoração. Faça a mutação **na fonte**, meça, e reverta.

Em `web/src/api/cadastros.ts`, troque o corpo de `listarPedidos` por uma versão paginada de mentira:

```ts
export async function listarPedidos(): Promise<PedidoDto[]> {
  const resp = await apiFetch('/pedidos')
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar pedidos (${resp.status}).`)
  return ((await resp.json()) as PedidoDto[]).slice(0, 20)   // <-- MUTAÇÃO: simula página de 20
}
```

```bash
cd web && npx vitest run src/api/cadastros.test.ts
```

Esperado: **1 failed** — `expected length 20 to be 25`. Anote **quais** testes morreram: se morrer
só este, a guarda é específica (bom). Reverta a mutação (`git checkout -- src/api/cadastros.ts`) e
confirme 52 verdes de novo.

- [ ] **Step 4: Suíte inteira e build**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: **379 passed / 32 files**, build limpo.

- [ ] **Step 5: Commit**

```bash
git add web/src/api/cadastros.test.ts
git commit -m "test(1e): guarda que morre no dia em que /pedidos for paginado

A HomePage da 1E deriva contagem por status e 'abertos ha mais tempo' do array
inteiro de listarPedidos(). Paginar o endpoint transformaria as duas em meia-verdade
plausivel, sem nenhum teste vermelho.

Guarda dupla: Array.isArray + length no runtime, anotacao de tipo para o tsc -b.
Mutacao medida: .slice(0, 20) na fonte mata 1 teste.

Decisao do usuario em 2026-08-28: guarda executavel, nao paragrafo de divida.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Home — resumo por status no cartão de Pedidos

**Files:**
- Create: `web/src/pedidos/statusDoPedido.ts`
- Create: `web/src/pedidos/statusDoPedido.test.ts`
- Modify: `web/src/pages/PedidosPage.tsx:20-28` (remove o `tomDoStatus` local) e o `import` do topo
- Modify: `web/src/pages/HomePage.tsx` (estado, derivação e o `CartaoDeContagem`)
- Modify: `web/src/pages/HomePage.test.tsx`

**Interfaces:**
- Consumes: `listarPedidos`, `PedidoDto` (`web/src/api/cadastros.ts`); `Pilula`, `TomDePilula`
  (`web/src/components/Pilula.tsx`).
- Produces, e a Task 4 depende dos três:
  - `STATUS_DO_PEDIDO: readonly ['Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado']`
  - `ENCERRADOS: readonly ['Concluido', 'Cancelado']`
  - `tomDoStatus(status: string): TomDePilula`
  - Em `HomePage.tsx`: o estado `pedidos: PedidoDto[] | null` — a Task 4 deriva `maisAntigos` dele.

**Delta de teste:** +3 (`statusDoPedido.test.ts`) +3 (`HomePage.test.tsx`) = **+6 testes, +1
arquivo → 385 / 33**.

### Três armadilhas medidas — leia antes de escrever qualquer linha

1. **Não ponha a contagem num elemento próprio.** O teste existente `conta só os pedidos abertos`
   ([HomePage.test.tsx:60-72](web/src/pages/HomePage.test.tsx:60)) faz
   `within(cartao).getByText('1')` — e `getByText` **lança** quando casa mais de um nó. Se a pílula
   virar `<span>Aberto</span><span>1</span>`, o `1` da pílula colide com o `1` do número grande e o
   teste quebra. Renderize o rótulo e a contagem numa **única string**
   (`` {`${status} ${quantidade}`} ``): o `textContent` da pílula vira `"Aberto 1"`, que não casa
   com `'1'`, e o teste antigo continua verde sem ser tocado.
2. **O resumo não renderiza enquanto `pedidos === null`.** Nem com zeros (seria afirmação falsa),
   nem com traços (seriam cinco `—` a mais). O teste existente `mostra traço, e não zero`
   ([HomePage.test.tsx:108-116](web/src/pages/HomePage.test.tsx:108)) afirma
   `getAllByText('—').length === 4`; qualquer traço extra o quebra. O cartão já diz `—` no número
   grande, e isso basta.
3. **Nada de `<Link>` dentro do cartão.** O cartão inteiro **é** o `<Link>`
   ([HomePage.tsx:18-28](web/src/pages/HomePage.tsx:18)); pílula clicável ali dentro seria link
   dentro de link — HTML inválido, e quebra a navegação por teclado. É requisito explícito da spec
   §3.1, e o Step 5 abaixo o transforma em teste.

### Decisão registrada: o rótulo mostra o valor cru do domínio (`EmProducao`, `AguardandoExpedicao`)

Feio, e deliberado. A `PedidosPage` já renderiza `{p.status}` cru
([PedidosPage.tsx:138](web/src/pages/PedidosPage.tsx:138)); humanizar só na Home criaria duas
grafias do mesmo status em duas telas. Humanizar nas duas significaria mexer na `PedidosPage` além
do import — e a spec §1 fecha "não reabre as outras 6 telas" e "zero tela reescrita". Fica como
**dívida nomeada** no relatório da task: *"rótulo legível de status do Pedido, nas duas telas de
uma vez"*.

- [ ] **Step 1: Escrever o teste do módulo compartilhado**

Crie `web/src/pedidos/statusDoPedido.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { STATUS_DO_PEDIDO, ENCERRADOS, tomDoStatus } from './statusDoPedido'

describe('statusDoPedido', () => {
  // A ordem NÃO é decorativa: é a do CK_Pedido_Status em specs/02-modelo-de-dados.sql:170-171, e
  // é a ordem em que o resumo da Home apresenta os cinco. Um `sort()` alfabético a trocaria para
  // AguardandoExpedicao/Aberto/Cancelado/Concluido/EmProducao, que não é o fluxo do domínio.
  it('lista os cinco status na ordem do CK_Pedido_Status', () => {
    expect(STATUS_DO_PEDIDO).toEqual([
      'Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado',
    ])
  })

  // `ENCERRADOS` é o que a seção "há mais tempo" (Task 4) exclui. Se alguém acrescentar
  // 'Cancelado' e esquecer 'Concluido' — ou vice-versa —, a Home listaria pedido encerrado como
  // "parado há mais tempo".
  it('trata Concluido e Cancelado como encerrados, e mais nenhum', () => {
    expect(ENCERRADOS).toEqual(['Concluido', 'Cancelado'])

    // O complemento, afirmado explicitamente: os três que SOBRAM são os que a seção "há mais
    // tempo" tem de listar. Sem esta metade, acrescentar 'Aberto' a ENCERRADOS por engano passaria
    // — a primeira asserção sozinha não diz nada sobre quem ficou de fora.
    // `.some(===)` e não `.includes`: `ENCERRADOS` é uma tupla `readonly`, e `.includes` exigiria
    // um cast para aceitar um status que não está nela.
    const naoEncerrados = STATUS_DO_PEDIDO.filter((s) => !ENCERRADOS.some((e) => e === s))
    expect(naoEncerrados).toEqual(['Aberto', 'EmProducao', 'AguardandoExpedicao'])
  })

  // A regra do CLAUDE.md: cor de estado nunca decora. Verde só para Concluido, vermelho só para
  // Cancelado; os intermediários ficam neutros porque "em produção" não exige decisão de ninguém.
  it('reserva verde e vermelho, e deixa os intermediarios neutros', () => {
    expect(tomDoStatus('Concluido')).toBe('positivo')
    expect(tomDoStatus('Cancelado')).toBe('negativo')
    expect(tomDoStatus('Aberto')).toBe('neutro')
    expect(tomDoStatus('EmProducao')).toBe('neutro')
    expect(tomDoStatus('AguardandoExpedicao')).toBe('neutro')
    // Valor que o domínio não tem: neutro, nunca uma cor de estado por acidente.
    expect(tomDoStatus('QualquerCoisa')).toBe('neutro')
  })
})
```

- [ ] **Step 2: Rodar e ver falhar**

```bash
cd web && npx vitest run src/pedidos/statusDoPedido.test.ts
```

Esperado: falha no import — `Failed to resolve import "./statusDoPedido"`.

- [ ] **Step 3: Escrever o módulo**

Crie `web/src/pedidos/statusDoPedido.ts`:

```ts
import type { TomDePilula } from '../components/Pilula'

/**
 * Os cinco status do `CK_Pedido_Status` (`specs/02-modelo-de-dados.sql:170-171`), NA ORDEM DO
 * DDL — que é a ordem do fluxo, e é a ordem em que a Home apresenta o resumo.
 *
 * Módulo compartilhado, e não cópia: a partir da Fase 1E a `HomePage` e a `PedidosPage` precisam
 * do mesmo mapa. Duas cópias divergiriam no dia em que o domínio ganhar um sexto status.
 */
export const STATUS_DO_PEDIDO = [
  'Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado',
] as const

/** Status que tiram o Pedido da fila de "está parado": ele acabou, de um jeito ou de outro. */
export const ENCERRADOS = ['Concluido', 'Cancelado'] as const

/**
 * `Concluido` é o único que ganha tom positivo; `Cancelado`, o negativo. Os intermediários ficam
 * neutros — verde e vermelho são reservados a estado que exige decisão, e "em produção" não exige
 * nenhuma (`CLAUDE.md`, seção Interface).
 *
 * Recebe `string` e não o tipo estreito de propósito: o `status` do `PedidoDto` vem da API como
 * `string`, e um valor inesperado tem de cair em `neutro`, nunca numa cor de estado por acidente.
 */
export function tomDoStatus(status: string): TomDePilula {
  if (status === 'Concluido') return 'positivo'
  if (status === 'Cancelado') return 'negativo'
  return 'neutro'
}
```

- [ ] **Step 4: Rodar e ver passar; e apontar a `PedidosPage` para o módulo**

```bash
cd web && npx vitest run src/pedidos/statusDoPedido.test.ts
```

Esperado: **3 passed**.

Em `web/src/pages/PedidosPage.tsx`, **apague** o bloco de comentário + função de
[PedidosPage.tsx:20-28](web/src/pages/PedidosPage.tsx:20) (a doc daquele bloco já foi para o
módulo novo, sem perda) e acrescente ao topo:

```ts
import { tomDoStatus } from '../pedidos/statusDoPedido'
```

```bash
cd web && npx vitest run src/pages/PedidosPage.test.tsx
```

Esperado: **11 passed** — o mesmo número medido nesta bancada em 2026-08-28, antes desta task. A
extração não muda comportamento; contagem diferente aqui significa que algo além do import mudou.

- [ ] **Step 5: Escrever os três testes do resumo na Home**

Em `web/src/pages/HomePage.test.tsx`, primeiro **enriqueça o fixture** para ter os cinco status
representados de forma desigual (é o que prova que a contagem não é `.length`), substituindo o
`PEDIDOS` de [HomePage.test.tsx:11-14](web/src/pages/HomePage.test.tsx:11):

```ts
// Contagem por status: Aberto 2, EmProducao 1, AguardandoExpedicao 0, Concluido 1, Cancelado 1.
// O zero de AguardandoExpedicao é o caso que a spec §3.1 exige mostrar, e ele SÓ existe porque
// nenhum pedido do fixture tem esse status — se alguém acrescentar um, o teste do zero morre e
// aponta para cá.
const PEDIDOS = [
  { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 2, numero: 'PED-002', cliente: 'Beta', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-05T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 3, numero: 'PED-003', cliente: 'Gama', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-01T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 4, numero: 'PED-004', cliente: 'Delta', tipo: 'Normal', status: 'EmProducao', dataAbertura: '2026-08-03T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 5, numero: 'PED-005', cliente: 'Epsilon', tipo: 'Normal', status: 'Cancelado', dataAbertura: '2026-07-20T09:00:00-03:00', criadoPorUsuarioId: 1 },
]
```

⚠️ **Isto quebra o teste `conta só os pedidos abertos`**, que espera `1` e agora deve esperar `2`.
Ajuste-o na mesma passada — trocando o `'1'` por `'2'` e a frase do comentário que fala em "dois
pedidos":

```ts
  it('conta só os pedidos abertos', async () => {
    // Cinco pedidos, DOIS Abertos: contar `.length` daria 5 e a tela mentiria.
    // `within(cartao).getByText('2')`, não `textContent.toContain('2')`: o cartão de componentes
    // mostra "41", e um `toContain` passaria com a fiação de pedidos e componentes trocada.
    // `getByText` casa o nó de texto inteiro, então discrimina.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('2')).toBeTruthy()
  })
```

Agora acrescente os três testes novos, ao fim do `describe`:

```ts
  it('mostra os cinco status com a contagem, inclusive o que esta zerado', async () => {
    // O zerado (AguardandoExpedicao) é o caso que a spec §3.1 nomeia: omitir um status porque não
    // há nenhum pedido nele faria o leitor concluir que aquele estado não existe no sistema.
    // A pílula traz rótulo e contagem NUMA STRING SÓ — ver o comentário da renderização na
    // HomePage: contagem em elemento próprio colidiria com o `getByText('2')` do teste acima.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('Aberto 2')).toBeTruthy()
    expect(within(cartao).getByText('EmProducao 1')).toBeTruthy()
    expect(within(cartao).getByText('AguardandoExpedicao 0')).toBeTruthy()
    expect(within(cartao).getByText('Concluido 1')).toBeTruthy()
    expect(within(cartao).getByText('Cancelado 1')).toBeTruthy()
  })

  it('nao mostra o resumo por status enquanto os numeros nao chegaram', () => {
    // Cinco zeros seriam cinco afirmações falsas; cinco traços seriam ruído (o número grande do
    // cartão já diz "—"). O resumo simplesmente não existe até o dado chegar.
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.queryByText(/^Aberto \d/)).toBeNull()
    expect(screen.queryByText(/^AguardandoExpedicao \d/)).toBeNull()
  })

  it('nao aninha link dentro do cartao de pedidos', async () => {
    // Requisito explícito da spec §3.1: o cartão INTEIRO é um `<Link>`, então uma pílula clicável
    // ali dentro seria `<a>` dentro de `<a>` — HTML inválido, e o navegador desmonta a árvore de
    // um jeito que quebra a navegação por teclado. Esta asserção morre no dia em que alguém
    // "melhorar" o resumo tornando cada status filtrável.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).queryAllByRole('link')).toHaveLength(0)
  })
```

- [ ] **Step 6: Rodar, e conferir QUAL falha — não quantas**

```bash
cd web && npx vitest run src/pages/HomePage.test.tsx
```

Esperado: **1 failed, 10 passed**. Só um dos três novos falha agora, e os outros dois passarem
**não** é sinal de que estão errados:

| Teste novo | Agora | Por quê |
|---|---|---|
| `mostra os cinco status com a contagem…` | **FALHA** | não existe pílula nenhuma para achar |
| `nao mostra o resumo por status enquanto…` | passa | nada renderiza ainda, então a ausência é trivial. Ele só vira prova depois do Step 7 — e a mutação **M3** do Step 9 é o que confirma que virou |
| `nao aninha link dentro do cartao` | passa | idem: zero links dentro do cartão porque não há conteúdo. Vale como **guarda de regressão** — morre no dia em que alguém tornar os status filtráveis |

Se `conta só os pedidos abertos` falhar, o ajuste de `'1'` → `'2'` do Step 5 não foi aplicado. Se
falhar **algum outro** dos oito antigos, pare: o fixture novo bateu em algo que este plano não
previu.

- [ ] **Step 7: Implementar o resumo na `HomePage`**

Em `web/src/pages/HomePage.tsx`:

**7a.** Troque os imports do topo ([HomePage.tsx:1-9](web/src/pages/HomePage.tsx:1)):

```tsx
import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, listarPedidos,
  type PedidoDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { STATUS_DO_PEDIDO, tomDoStatus } from '../pedidos/statusDoPedido'
import { Pagina } from '../components/Pagina'
import { Pilula } from '../components/Pilula'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
```

**7b.** `Contagens` perde `pedidosAbertos` — ele passa a ser derivado, não guardado:

```tsx
// `pedidosAbertos` saiu daqui na 1E: o array de pedidos vive em estado próprio (a Home deriva
// TRÊS coisas dele agora), e guardar a contagem em paralelo criaria duas verdades sobre o mesmo
// dado, que podem divergir.
interface Contagens {
  componentes: number
  materiais: number
  setores: number
}
```

**7c.** `CartaoDeContagem` ganha um `resumo` opcional, renderizado dentro do próprio `<Link>`:

```tsx
function CartaoDeContagem({ titulo, valor, para, resumo }: {
  titulo: string
  valor: number | null
  para: string
  /** Conteúdo extra dentro do cartão. NÃO pode conter `<a>`: o cartão já é um `<Link>`. */
  resumo?: ReactNode
}) {
  return (
    <Link
      to={para}
      className="flex flex-col gap-1 rounded-lg border border-borda bg-superficie px-5 py-6 transition-colors hover:border-acao focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      {/* Traço em vez de zero enquanto carrega: "0 pedidos" é uma afirmação, e ela seria falsa. */}
      <span className="text-3xl font-semibold text-tinta">{valor === null ? '—' : valor}</span>
      <span className="text-sm text-tinta-fraca">{titulo}</span>
      {resumo}
    </Link>
  )
}
```

**7d.** O estado e a derivação, dentro de `HomePage`:

```tsx
  const [pedidos, setPedidos] = useState<PedidoDto[] | null>(null)
  const [contagens, setContagens] = useState<Contagens | null>(null)
```

No `try` do `carregar()`, no lugar do `setContagens({...})` de
[HomePage.tsx:50-55](web/src/pages/HomePage.tsx:50):

```tsx
      setPedidos(pedidos)
      setContagens({
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
```

E no `catch`, **antes** do `setErro`, limpe o array:

```tsx
      // Sem isto, uma falha numa releitura futura deixaria a seção "há mais tempo" (Task 4)
      // mostrando dado velho ao lado do banner de erro — o que a spec §3.4 proíbe. Hoje `carregar`
      // roda uma vez só e não há caminho que exercite isto; está aqui porque a alternativa é
      // depender de a Home nunca ganhar um botão de recarregar.
      setPedidos(null)
```

Logo antes do `return`, a derivação:

```tsx
  // Derivado, não guardado: uma fonte de verdade só. `null` enquanto o dado não chegou — e o
  // resumo NÃO renderiza nesse estado (nem com zeros, que seriam falsos, nem com traços, que
  // seriam ruído; o número grande do cartão já diz "—").
  const abertos = pedidos === null ? null : pedidos.filter((p) => p.status === 'Aberto').length
  const porStatus = pedidos === null ? null : STATUS_DO_PEDIDO.map((status) => ({
    status,
    quantidade: pedidos.filter((p) => p.status === status).length,
  }))
```

**7e.** O cartão de Pedidos, no lugar de [HomePage.tsx:72](web/src/pages/HomePage.tsx:72):

```tsx
        <CartaoDeContagem
          titulo="pedidos abertos"
          valor={abertos}
          para="/pedidos"
          resumo={porStatus && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {porStatus.map(({ status, quantidade }) => (
                // Rótulo e contagem numa ÚNICA string, e não em dois elementos: o teste
                // `conta só os pedidos abertos` faz `within(cartao).getByText('2')`, e
                // `getByText` LANÇA quando casa mais de um nó. Contagem em elemento próprio
                // colidiria com o número grande do cartão.
                <Pilula key={status} tom={tomDoStatus(status)}>{`${status} ${quantidade}`}</Pilula>
              ))}
            </div>
          )}
        />
```

E os outros três cartões trocam `contagens?.pedidosAbertos` — que já não existe — mantendo o resto
igual:

```tsx
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
```

- [ ] **Step 8: Rodar e ver os três passarem**

```bash
cd web && npx vitest run src/pages/HomePage.test.tsx
```

Esperado: **11 passed** (8 antes + 3).

- [ ] **Step 9: Provar por mutação que os testes pegam o que dizem pegar**

Três mutações, uma de cada vez, revertendo entre elas. **Anote o que NÃO morreu** — é isso que diz
onde a suíte é cega.

| # | Mutação em `HomePage.tsx` | Esperado |
|---|---|---|
| M1 | `STATUS_DO_PEDIDO.filter((s) => …)` para omitir o status zerado do `map` | `mostra os cinco status…` falha |
| M2 | `` `${status} ${quantidade}` `` → `` `${status} ${quantidade + 1}` `` | `mostra os cinco status…` falha em **todos** os cinco |
| M3 | `porStatus &&` → `(porStatus ?? []) .length >= 0 &&` com fallback de zeros, para renderizar o resumo durante o carregando | `nao mostra o resumo por status…` falha |

Se M3 não matar nada, o resumo está renderizando condicionado a outra coisa — investigue antes de
seguir.

- [ ] **Step 10: Suíte inteira e build**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: **385 passed / 33 files**, build limpo.

- [ ] **Step 11: Commit**

```bash
git add web/src/pedidos web/src/pages/HomePage.tsx web/src/pages/HomePage.test.tsx web/src/pages/PedidosPage.tsx
git commit -m "feat(1e): resumo por status no cartao de Pedidos da Home

Os cinco status do CK_Pedido_Status, inclusive os zerados — omitir um status vazio
faria o leitor concluir que aquele estado nao existe no sistema.

STATUS_DO_PEDIDO/ENCERRADOS/tomDoStatus saem para src/pedidos/statusDoPedido.ts:
a Home e a PedidosPage passam a precisar do mesmo mapa, e duas copias divergiriam
no dia de um sexto status.

Rotulo e contagem numa string so, de proposito: contagem em elemento proprio
colidiria com o getByText do numero grande do cartao. Nenhum <a> dentro do cartao —
ele ja e o Link (spec 3.1), e ha teste que morre se alguem aninhar.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Home — seção "pedidos abertos há mais tempo"

**Files:**
- Modify: `web/src/pages/HomePage.tsx` (derivação `maisAntigos` + `<section>` nova depois da grade)
- Modify: `web/src/pages/HomePage.test.tsx`

**Interfaces:**
- Consumes: da Task 3 — o estado `pedidos: PedidoDto[] | null` e `ENCERRADOS`/`tomDoStatus` de
  `web/src/pedidos/statusDoPedido.ts`. De `web/src/api/cadastros.ts` — `formatarDataHora`.
  De `web/src/components/` — `ListaDeCadastro`, `ItemDeCadastro`, `EstadoVazio`, `Pilula`.
- Produces: nada. É a última task de código.

**Delta de teste:** +6 testes, +0 arquivo → **391 / 33**.

### Desvio deliberado da spec §3.2, nomeado em vez de silencioso

A spec diz *"Cada linha (número, cliente, status) é um link para `/pedidos/:id`"*. Este plano
**acrescenta a data de abertura** à linha, reusando o markup da `PedidosPage`
([PedidosPage.tsx:130-141](web/src/pages/PedidosPage.tsx:130)). Razão: a seção **ordena por
`dataAbertura`** e não mostrar a chave da ordenação faz a lista parecer arbitrária — o leitor não
tem como saber por que PED-005 vem antes de PED-003. É acréscimo, não troca: os três campos que a
spec pede continuam lá. Se a review discordar, remover a data é apagar uma linha.

### Armadilha medida: `role="status"` duplicado

`EstadoVazio` e `EstadoCarregando` **os dois** têm `role="status"`
([EstadoVazio.tsx:21](web/src/components/EstadoVazio.tsx:21),
[EstadoCarregando.tsx:12](web/src/components/EstadoCarregando.tsx:12)), e o teste existente
`mostra o indicador de carregando` ([HomePage.test.tsx:44-50](web/src/pages/HomePage.test.tsx:44))
faz `screen.getByRole('status')` — que **lança** com dois. A seção só renderiza quando
`maisAntigos !== null`, ou seja, nunca durante o carregando; é isso que mantém aquele teste verde.
Não troque a condição por `maisAntigos?.length` sem reler este parágrafo.

- [ ] **Step 1: Escrever os seis testes**

Ao fim do `describe` de `web/src/pages/HomePage.test.tsx`:

```ts
  it('lista os pedidos abertos ha mais tempo, do mais antigo para o mais novo', async () => {
    // Fixture: PED-003 (08-01) < PED-004 (08-03) < PED-001 (08-06) entre os NÃO encerrados.
    // Ordem alfabética de `numero` daria PED-001/003/004 — se a implementação esquecer o `sort`,
    // o React renderiza na ordem do array e esta asserção é a que pega.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const numeros = within(secao).getAllByRole('listitem').map((li) => li.textContent)
    expect(numeros[0]).toContain('PED-003')
    expect(numeros[1]).toContain('PED-004')
    expect(numeros[2]).toContain('PED-001')
  })

  it('deixa Concluido e Cancelado fora da lista de ha mais tempo', async () => {
    // PED-005 (Cancelado) é o MAIS ANTIGO do fixture (07-20). Se o filtro sumir, ele encabeça a
    // lista — e a Home passa a dizer que um pedido cancelado está "parado há mais tempo".
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    expect(within(secao).queryByText(/PED-005/)).toBeNull()   // Cancelado, e o mais antigo de todos
    expect(within(secao).queryByText(/PED-002/)).toBeNull()   // Concluido
    expect(within(secao).getAllByRole('listitem')).toHaveLength(3)
  })

  it('para em cinco mesmo havendo mais pedidos elegiveis', async () => {
    const oito = Array.from({ length: 8 }, (_, i) => ({
      id: i + 1, numero: `PED-${String(i + 1).padStart(3, '0')}`, cliente: 'Cliente',
      tipo: 'Normal', status: 'Aberto',
      // Dias 01 a 08: o mais antigo é PED-001 e o corte tem de deixar PED-006..008 de fora.
      dataAbertura: `2026-08-0${i + 1}T09:00:00-03:00`, criadoPorUsuarioId: 1,
    }))
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos': () => respostaJson(oito),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    expect(within(secao).getAllByRole('listitem')).toHaveLength(5)
    expect(within(secao).queryByText(/PED-006/)).toBeNull()
  })

  it('leva ao pedido certo por cada linha da lista', async () => {
    // O par número→id: uma implementação que use o índice do array no lugar de `p.id` acerta por
    // acidente quando os ids são 1..n em ordem. Aqui o mais antigo é o id 3, não o id 1.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const primeiro = within(secao).getAllByRole('listitem')[0]
    expect(within(primeiro).getByRole('link').getAttribute('href')).toBe('/pedidos/3')
  })

  it('diz que nao ha pedido aberto, em vez de sumir, quando a leitura deu certo e a lista e vazia', async () => {
    // A distinção que a spec §3.2 exige: "não há nada" tem de soar diferente de "não consegui
    // ler". Aqui a leitura FOI bem-sucedida — todos os pedidos estão encerrados.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos': () => respostaJson([
        { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
      ]),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByText('Nenhum pedido em aberto.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Pedidos abertos há mais tempo' })).toBeNull()
  })

  it('nao mostra a secao — nem vazia — quando a leitura falhou', async () => {
    // O padrão que já pegou DUAS vezes nesta fase (Tasks 8 e 10 da 1C): estado vazio renderizado
    // junto do banner de erro, dizendo "não há pedidos abertos" quando a verdade é "não consegui
    // perguntar". A seção inteira some enquanto houver erro.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'x' }, 500),
      '/api/pedidos': () => respostaJson(PEDIDOS),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')

    expect(screen.queryByText('Nenhum pedido em aberto.')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Pedidos abertos há mais tempo' })).toBeNull()
  })
```

- [ ] **Step 2: Rodar e ver falhar**

```bash
cd web && npx vitest run src/pages/HomePage.test.tsx
```

Esperado: **5 failed, 12 passed**.

| Teste novo | Agora | Por quê |
|---|---|---|
| `lista os pedidos abertos ha mais tempo…` | **FALHA** | `getByRole('list', …)` lança: a lista não existe |
| `deixa Concluido e Cancelado fora…` | **FALHA** | idem |
| `para em cinco…` | **FALHA** | idem |
| `leva ao pedido certo por cada linha` | **FALHA** | idem |
| `diz que nao ha pedido aberto…` | **FALHA** | `getByText('Nenhum pedido em aberto.')` não acha nada |
| `nao mostra a secao — nem vazia — quando a leitura falhou` | passa | afirma ausência, e não há nada. Vira prova de verdade na mutação **M8** do Step 5 |

Se algum dos 11 antigos falhar, pare: a Task 3 deixou algo pela metade.

- [ ] **Step 3: Implementar a seção**

Em `web/src/pages/HomePage.tsx`:

**3a.** Acrescente aos imports:

```tsx
import { ..., formatarDataHora, type PedidoDto } from '../api/cadastros'
import { STATUS_DO_PEDIDO, ENCERRADOS, tomDoStatus } from '../pedidos/statusDoPedido'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'
```

**3b.** Uma constante, junto do topo do arquivo:

```tsx
/** Quantos pedidos a seção "há mais tempo" mostra. Cinco, pela spec §3.2. */
const QUANTOS_MAIS_ANTIGOS = 5
```

**3c.** A derivação, ao lado de `porStatus`:

```tsx
  // Ordenação por string ISO, e não por `Date`: `dataAbertura` chega em GMT-3 com offset explícito
  // (`HorarioDeBrasiliaJsonConverter`), e ISO 8601 com o mesmo offset ordena lexicograficamente na
  // mesma ordem que cronologicamente. Passar por `new Date()` reconverteria para o fuso do
  // aparelho — o mesmo motivo que fez `formatarDataHora` não usar `Date`.
  //
  // `.filter()` já devolve array novo, então o `.sort()` abaixo não muda o estado no lugar.
  const maisAntigos = pedidos === null ? null : pedidos
    .filter((p) => !ENCERRADOS.some((encerrado) => encerrado === p.status))
    .sort((a, b) => a.dataAbertura.localeCompare(b.dataAbertura))
    .slice(0, QUANTOS_MAIS_ANTIGOS)
```

**3d.** A seção, **depois** do `</div>` da grade de cartões e antes do `</Pagina>`:

```tsx
      {/* `maisAntigos !== null` cobre carregando E erro de uma vez: nos dois casos `pedidos` é
          `null`. Não troque por `maisAntigos?.length` — durante o carregando isso renderizaria o
          `EstadoVazio`, que tem `role="status"` igual ao `EstadoCarregando`, e o teste
          `mostra o indicador de carregando` faz `getByRole('status')`, que LANÇA com dois. Além
          de, claro, dizer "não há pedidos abertos" quando a verdade é "ainda não perguntei". */}
      {maisAntigos !== null && (
        <section className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold text-tinta">Pedidos abertos há mais tempo</h2>
          {maisAntigos.length === 0 ? (
            <EstadoVazio
              titulo="Nenhum pedido em aberto."
              descricao="Todos os pedidos cadastrados estão concluídos ou cancelados."
            />
          ) : (
            <ListaDeCadastro rotulo="Pedidos abertos há mais tempo">
              {maisAntigos.map((p) => (
                <ItemDeCadastro key={p.id}>
                  {/* Mesmo markup da `PedidosPage`: o item inteiro é o alvo do clique, porque numa
                      bancada com tablet alvo pequeno erra. `ItemDeCadastro` aqui não recebe
                      `acao` — a armadilha m6 documentada na primitiva (overlay engolindo a ação)
                      não se aplica. */}
                  <Link
                    to={`/pedidos/${p.id}`}
                    className="flex flex-col gap-1 after:absolute after:inset-0 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
                  >
                    <span className="font-medium">
                      <span className="font-mono">{p.numero}</span> — {p.cliente}
                    </span>
                    <span className="flex items-center gap-2 text-sm text-tinta-fraca">
                      <Pilula tom={tomDoStatus(p.status)}>{p.status}</Pilula>
                      aberto em {formatarDataHora(p.dataAbertura)}
                    </span>
                  </Link>
                </ItemDeCadastro>
              ))}
            </ListaDeCadastro>
          )}
        </section>
      )}
```

- [ ] **Step 4: Rodar e ver os seis passarem**

```bash
cd web && npx vitest run src/pages/HomePage.test.tsx
```

Esperado: **17 passed** (11 da Task 3 + 6).

- [ ] **Step 5: Provar por mutação — quatro, uma de cada vez**

| # | Mutação em `HomePage.tsx` | Esperado |
|---|---|---|
| M4 | apagar o `.sort(...)` | `lista os pedidos abertos ha mais tempo…` falha |
| M5 | inverter o sinal: `b.dataAbertura.localeCompare(a.dataAbertura)` | idem, e a mensagem mostra PED-001 no índice 0 |
| M6 | apagar o `.filter(...)` de `ENCERRADOS` | `deixa Concluido e Cancelado fora…` falha, com PED-005 na lista |
| M7 | `.slice(0, QUANTOS_MAIS_ANTIGOS)` → `.slice(0, 8)` | `para em cinco…` falha |
| M8 | `maisAntigos !== null` → `maisAntigos !== null \|\| erro !== null` renderizando vazio | `nao mostra a secao — nem vazia — quando a leitura falhou` falha |

**Anote o que não morreu.** Uma mutação que sobrevive é um teste que não prova o que diz.

- [ ] **Step 6: Suíte inteira e build**

```bash
cd web && npm test -- --run && npm run build
```

Esperado: **391 passed / 33 files**, build limpo.

- [ ] **Step 7: Verificar no navegador**

Suba o front pela ferramenta de preview, com o banco de demo carregado (`db/seed-demo.sql` — sem
ele a Home fica vazia e parece quebrada). Confira, e registre no relatório:

1. o cartão de Pedidos mostra as cinco pílulas, incluindo a zerada;
2. a seção "Pedidos abertos há mais tempo" lista no máximo cinco, do mais antigo para o mais novo;
3. clicar numa linha abre o pedido certo;
4. a tela não rola na horizontal em viewport de celular (375px) — as cinco pílulas quebram linha;
5. o foco por teclado (Tab) percorre os quatro cartões e depois as linhas da seção, com contorno
   visível.

- [ ] **Step 8: Commit**

```bash
git add web/src/pages/HomePage.tsx web/src/pages/HomePage.test.tsx
git commit -m "feat(1e): secao 'pedidos abertos ha mais tempo' na Home

Cinco mais antigos fora de Concluido/Cancelado, ordenados por dataAbertura. Sinal de
risco honesto — 'isto esta parado ha mais tempo' — sem fingir um prazo que o dominio
nao tem (Pedido so tem DataAbertura/DataConclusao).

Ordenacao pela string ISO, nao por Date: a data ja vem em GMT-3 com offset, e passar
por Date a reconverteria para o fuso do aparelho.

A secao some inteira quando a leitura falha, em vez de mostrar estado vazio ao lado
do banner de erro — o padrao que ja pegou duas vezes na 1C.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Registrar a Fase 1E no roadmap

**Files:**
- Modify: `specs/06-roadmap-mvp.md` (seção nova entre a da Fase 1D, que termina em
  [06-roadmap-mvp.md:71](specs/06-roadmap-mvp.md:71), e `## Fase 2 — Estrutura recursiva`)

**Interfaces:** nenhuma. Produto é documentação.

**Delta de teste:** 0. **A review desta task NÃO se pula por isso** — o produto é prosa que vai a
repositório público, e a família de defeito que dominou o fim da 1C foi exatamente *afirmação que
infla o rigor do processo* em documento público, achada em seis rodadas seguidas de gate. Prosa é
onde ela mora.

- [ ] **Step 1: Escrever a seção**

O roadmap hoje **não tem** a Fase 1E, e a seção da 1D fecha com *"Não existe 'Fase 1D parte 2'"* —
uma frase que, lida sozinha ao lado de uma 1E de refinamento visual, faz o arquivo se contradizer
em duas linhas vizinhas. A seção nova precisa desarmar isso explicitamente. Insira, **antes** de
`## Fase 2 — Estrutura recursiva`:

```markdown
## Fase 1E — Refinamento visual

- Tipografia própria: IBM Plex Sans/Mono auto-hospedadas, por troca dos tokens `--font-sans` e
  `--font-mono` — zero tela reescrita.
- `HomePage`: resumo pelos cinco status do Pedido no cartão de Pedidos, e seção "pedidos abertos
  há mais tempo".
- Critério de pronto: fonte aplicada e **carregada** (verificada no navegador, não só no token),
  os cinco status visíveis inclusive os zerados, e a seção nova com os três estados —
  carregando, vazio de verdade e erro — cada um com teste que morre se o estado sumir.

> **Esta fase NÃO é a "Fase 1D parte 2" que o corolário acima descarta**, e vale dizer por quê em
> vez de fingir que a tensão não existe. O que aquele corolário advertia era uma reestilização
> ampla motivada por o padrão de primitivas não ter segurado. Aqui o padrão segurou: a troca de
> fonte é **o gancho que a própria 1D deixou pronto** ("trocar por uma fonte própria depois é mudar
> um token, não reescrever telas"), e o reforço da Home usa as primitivas existentes e o dado que a
> Home **já** buscava. As outras seis telas não são reabertas.
>
> **Fora de escopo, por decisão escrita:** densidade das outras telas; "prazo de entrega" e
> "pedidos em atraso" — o domínio não tem campo de data prevista, e criá-lo é mudança de schema
> **e** de formulário de cadastro, candidata a fase própria (§5 da spec da 1E); e qualquer KPI da
> Fase 6, que depende do rastreamento por Setor que só nasce na Fase 3.
>
> **Dívida nomeada por esta fase:** `listarPedidos()` não é paginado, e a Home deriva o resumo e a
> lista do array inteiro. Não é problema hoje, e **não** está só escrito: há guarda executável em
> `web/src/api/cadastros.test.ts` que fica vermelha no dia em que `/pedidos` for paginado. A outra
> é cosmética — o rótulo de status aparece cru (`EmProducao`, `AguardandoExpedicao`) na Home e na
> `PedidosPage`; humanizá-lo é mexer nas duas telas de uma vez, fora do escopo desta fase.
```

- [ ] **Step 2: Conferir cada afirmação contra o disco**

Prosa não tem suíte; a conferência é o teste. Uma a uma:

```bash
grep -n "font-sans\|font-mono" web/src/index.css          # os tokens existem e nomeiam IBM Plex
grep -rn "AguardandoExpedicao" web/src/pages/HomePage.tsx # o status cru está mesmo na tela
grep -n "HomePage depende disso" web/src/api/cadastros.test.ts   # a guarda existe
sed -n '/Fase 1D/,/Fase 1E/p' specs/06-roadmap-mvp.md     # o corolário citado está no texto acima
```

Cada `grep` que vier vazio é uma afirmação a corrigir ou apagar — **não** a manter "porque é quase
verdade".

- [ ] **Step 3: Reler a seção INTEIRA, não a linha editada**

A família de defeito da 1C não foi escrever errado: foi **edição pontual estragando o entorno** —
duas vezes uma palavra apagada virou afirmação falsa. Releia da linha `## Fase 1D` até
`## Fase 2` de uma vez, procurando contradição entre parágrafos vizinhos. Em especial: o corolário
da 1D e o primeiro `>` da 1E têm de se ler como uma conversa, não como duas afirmações opostas.

- [ ] **Step 4: Commit**

```bash
git add specs/06-roadmap-mvp.md
git commit -m "docs(roadmap): registra a Fase 1E e desarma a leitura de '1D parte 2'

O corolario da 1D ('nao existe Fase 1D parte 2') ficava ao lado de uma fase de
refinamento visual sem nada explicando a diferenca — o arquivo se contradizia em
duas linhas vizinhas. A secao nova nomeia a tensao: o corolario advertia contra
reestilizacao ampla por o padrao nao ter segurado, e o padrao segurou.

Registra as duas dividas nomeadas: listarPedidos() nao paginado (com guarda
executavel, nao so paragrafo) e o rotulo de status cru nas duas telas.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Fechamento da branch

Depois da Task 5, e **antes** de abrir o PR:

- [ ] `cd web && npm test -- --run && npm run build` — **391 / 33**, build limpo.
- [ ] `bash scripts/estado` — árvore limpa, branch empurrada, ledger em dia.
- [ ] Review de branch inteira, no modelo mais capaz, em **sessão nova** — review pesada não se
  despacha no fim de sessão longa. Prepare o pacote com `scripts/review-package BASE HEAD` (BASE =
  o `19018a1` de onde a fase partiu) e deixe o despacho a um passo.
- [ ] Abrir o PR. Aprovar e mesclar continua sendo do usuário.
- [ ] Só **depois do merge**, datar a fase no roadmap ("1E concluída em AAAA-MM-DD"), no molde da
  linha da 1C.

**A suíte do backend não é tocada por esta fase** — nenhuma task altera `src/` ou `tests/` do .NET.
O último número medido é 464 (2026-08-27, com o Docker no ar). Se a review de branch exigir a
suíte cheia, meça-a na hora; não copie esse número para um relatório como se tivesse sido medido.

---

## Auto-revisão do plano

**1. Cobertura da spec.**

| Requisito da spec | Task |
|---|---|
| §2 troca dos tokens `--font-sans`/`--font-mono` | 1, Step 7 |
| §2 auto-hospedada, não CDN, com fallback do SO | 1, Steps 2 e 7 (guarda no Step 5) |
| §2 prova da licença OFL **antes** de baixar | 1, bloco "o que a medição derrubou" + Step 4 |
| §3.1 cinco status, inclusive os zerados | 3, Step 5 (1º teste) |
| §3.1 cartão continua um `<Link>` só | 3, Step 5 (3º teste) |
| §3.2 cinco mais antigos, fora de `Concluido`/`Cancelado`, por `DataAbertura` | 4, Steps 1 e 3 |
| §3.2 cada linha leva a `/pedidos/:id` | 4, Step 1 (4º teste) |
| §3.2 estado vazio real, nunca junto de erro | 4, Step 1 (5º e 6º testes) |
| §3.3 zero requisição nova, derivado do mesmo array | 3 e 4 (nenhuma chamada nova) + a guarda da 2 |
| §3.4 traço "—", não "0", e seção ausente sob erro | 3, Step 5 (2º teste); 4, Step 1 (6º teste) |
| §4 os seis casos mínimos de teste | 3 e 4 — os seis estão cobertos, com quatro a mais |
| §5 "prazo de entrega" registrado como fase futura | já está na §5 da spec; a Task 5 o repete no roadmap |
| §0 esta fase não é a "1D parte 2" | 5, Step 1 |

Sem lacuna.

**2. Placeholders.** Nenhum "TBD", "similar à Task N" ou "adicione tratamento de erro apropriado":
todo passo que muda código traz o código, e todo comando traz a saída esperada.

**3. Consistência de tipos.** `STATUS_DO_PEDIDO`, `ENCERRADOS` e `tomDoStatus` são declarados na
Task 3 e usados com os mesmos nomes na Task 4. `PedidoDto` vem de `web/src/api/cadastros.ts` nas
Tasks 2, 3 e 4. `TomDePilula` é importado de `web/src/components/Pilula.tsx`, onde já é exportado.
O estado `pedidos: PedidoDto[] | null` nasce na Task 3 e a Task 4 deriva dele — a Task 4 **não**
roda antes da 3.

**4. Medição do próprio plano** (`[[medir-o-plano-antes-de-despachar]]`). Somei os `it(`, medi as
contagens por arquivo na bancada e reencenei mentalmente cada passo de "rodar e ver falhar". Achou
**três defeitos**, todos da mesma família — *saída esperada de passo de TDD escrita por dedução em
vez de por simulação*:

| Onde | Dizia | É |
|---|---|---|
| Task 1, Step 6 | "4 failed" | **2 failed, 2 passed** — o teste de existência de arquivo passa **por iterar lista vazia**, e a licença já foi copiada no Step 2 |
| Task 3, Step 6 | "3 failed", e no mesmo parágrafo explicava que um deles passa | **1 failed, 10 passed** |
| Task 4, Step 2 | "4 failed, 2 passed" | **5 failed, 1 passed** |

Os três estão corrigidos, e os passos passaram a listar **qual** teste cai de cada lado, com o
porquê — número de falhas sozinho não distingue "a guarda ainda não pegou" de "a guarda nunca vai
pegar". Números medidos que entraram no plano: `cadastros.test.ts` = 51, `HomePage.test.tsx` = 8,
`PedidosPage.test.tsx` = 11, `contraste.test.ts` = 37.
