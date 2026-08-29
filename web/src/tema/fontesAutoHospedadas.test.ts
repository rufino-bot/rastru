import { describe, it, expect } from 'vitest'
import { readFileSync, existsSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

/**
 * Modo de falha que esta guarda existe para matar: a tela simplesmente aparece na fonte do SO,
 * IDÊNTICA ao que era antes desta fase, e a suíte fica verde — ninguém percebe até alguém abrir o
 * DevTools. Ele tem DOIS caminhos, e o docstring desta guarda prometia matá-lo inteiro quando media
 * só o primeiro (achado I1 da review de branch):
 *
 * 1. `url()` com caminho errado — o navegador pede 404 e nada carrega. Coberto desde o começo por
 *    `todo url() … aponta para arquivo que existe`.
 * 2. **Nome divergente**: o `font-family` declarado no `@font-face` e o nome citado em
 *    `--font-sans`/`--font-mono` nunca eram confrontados. MEDIDO em 2026-08-28: trocar
 *    `font-family: 'IBM Plex Sans'` por `'IBM Plex Snas'` em `index.css` deixava **396/396 verdes e
 *    o build exit 0** — a fonte carregava e nenhum token a pedia. Coberto agora por
 *    `todo font-family de @font-face é citado por algum token --font-*`.
 */
function urlsDeFonte(fonte: string): string[] {
  return [...fonte.matchAll(/url\(['"](\/fontes\/[^'"]+)['"]\)/g)].map(([, caminho]) => caminho)
}

/** Os `font-family` DECLARADOS nos blocos `@font-face` (`[^}]*` porque não há chave aninhada). */
function familiasDeclaradas(fonte: string): string[] {
  return [...fonte.matchAll(/@font-face\s*\{[^}]*font-family:\s*'([^']+)'/g)].map(([, nome]) => nome)
}

/** O primeiro nome CITADO em cada token `--font-*` do `@theme` — o resto da pilha é fallback do SO. */
function familiasCitadasNosTokens(fonte: string): string[] {
  return [...fonte.matchAll(/--font-[a-z]+:\s*'([^']+)'/g)].map(([, nome]) => nome)
}

function faixasUnicode(fonte: string): string[] {
  return [...fonte.matchAll(/unicode-range:\s*([^;]+);/g)].map(([, faixa]) => faixa.trim())
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

  // I1. O teste acima casa os nomes por LITERAL, então um typo no `@font-face` não o alcança —
  // ele só olha o lado do token. Aqui os dois lados são extraídos e confrontados um com o outro,
  // que é o que torna a guarda simétrica: renomear em QUALQUER um dos dois lugares fica vermelho.
  //
  // As duas contagens vêm ANTES do laço, pelo motivo do controle positivo lá em cima: um `for`
  // sobre array vazio não itera, e passaria — a auditoria falharia em VERDE. São elas que
  // convertem "a regex parou de casar" em vermelho, e é esse o trabalho delas.
  //
  // Medido em 2026-08-29, contra as duas formas de fazer a regex perder um bloco:
  //   aspas duplas em vez de simples no `font-family` -> 1 vermelha, e é ESTE teste (a contagem
  //     cai para 3 e o `toHaveLength(4)` morre antes do laço, como projetado);
  //   `font-family` movido para depois do `src`       -> tudo VERDE, e continua correto: o
  //     `[^}]*` é ganancioso e alcança o nome em qualquer posição dentro do bloco.
  // A segunda linha está aqui para não sugerir mais rigor do que existe: ordem não afeta.
  it('todo font-family de @font-face é citado por algum token --font-*', () => {
    const declaradas = familiasDeclaradas(css)
    const citadas = familiasCitadasNosTokens(css)
    expect(declaradas).toHaveLength(4)
    expect(citadas).toHaveLength(2)

    for (const familia of declaradas) {
      expect(citadas, `@font-face declara '${familia}', que nenhum token --font-* pede`)
        .toContain(familia)
    }
  })

  // T1. O `unicode-range` aparece verbatim nos quatro blocos porque at-rule não interpola custom
  // property — não dá para extrair a string no CSS. O risco que sobra é humano: mexer no subset de
  // um bloco e esquecer os outros três, deixando uma das fontes com faixa diferente das demais.
  // Esta asserção é o que fecha isso.
  it('os quatro @font-face declaram o mesmo unicode-range', () => {
    const faixas = faixasUnicode(css)
    expect(faixas).toHaveLength(4)
    expect(new Set(faixas).size, `subsets divergentes: ${[...new Set(faixas)].join(' | ')}`).toBe(1)
  })
})
