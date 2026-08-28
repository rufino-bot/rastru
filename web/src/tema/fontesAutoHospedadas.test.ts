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
