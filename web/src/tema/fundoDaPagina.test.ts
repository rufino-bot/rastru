import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

/**
 * O `<html>` tem de ter o fundo da página. Só o `body` o tinha, e o fundo do `body` não cobre a
 * área que sobra quando a página rola de lado: ela aparecia como uma faixa branca. Teste de tela
 * monta componente no jsdom e nunca lê o CSS real, então só esta guarda, que lê o `index.css` de
 * disco, nota a regra sumir.
 */
const CAMINHO_CSS = join(dirname(fileURLToPath(import.meta.url)), '..', 'index.css')
const CSS = readFileSync(CAMINHO_CSS, 'utf8')

/** Os blocos `html { ... }` do CSS, sem comentários. */
function blocosDeHtml(fonte: string = CSS): string[] {
  const semComentarios = fonte.replace(/\/\*[\s\S]*?\*\//g, '')
  return [...semComentarios.matchAll(/(?:^|[\s}{;])html\s*\{([^}]*)\}/g)].map((m) => m[1])
}

function temFundoDaPagina(fonte: string = CSS): boolean {
  return blocosDeHtml(fonte).some((b) => /background-color\s*:\s*var\(--color-fundo\)/.test(b))
}

describe('index.css dá ao <html> o fundo da página', () => {
  it('há uma regra de html com background-color: var(--color-fundo)', () => {
    expect(
      temFundoDaPagina(),
      'web/src/index.css precisa de uma regra `html { background-color: var(--color-fundo); }`: ' +
        'sem ela, a página que rola de lado mostra o <html> branco.',
    ).toBe(true)
  })
})

describe('temFundoDaPagina (mecanismo da guarda) — medido em fixture', () => {
  it('aceita a regra dentro de @layer base', () => {
    expect(temFundoDaPagina('@layer base { html { background-color: var(--color-fundo); } }')).toBe(true)
  })

  it('reprova sem regra de html', () => {
    expect(temFundoDaPagina('body { background-color: var(--color-fundo); }')).toBe(false)
  })

  it('reprova quando o fundo está só num comentário', () => {
    expect(temFundoDaPagina('html { /* background-color: var(--color-fundo); */ }')).toBe(false)
  })

  it('reprova com outra cor no html', () => {
    expect(temFundoDaPagina('html { background-color: white; }')).toBe(false)
  })
})
