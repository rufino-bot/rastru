import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'

/**
 * WCAG 3.1.1 (Language of Page): o documento tem de declarar o idioma do próprio conteúdo. Toda a
 * interface deste sistema é escrita em português brasileiro — títulos, rótulos, mensagens de erro
 * (`web/src/pages/`, `web/src/components/`) — mas `web/index.html` nasceu com o `lang="en"` do
 * template do Vite e ficou assim até 2026-08-28.
 *
 * MEDIDO na mesma data, antes de trocar o atributo: com `lang="en"` no arquivo a suíte inteira
 * ficava VERDE (374 testes, 31 arquivos). Nenhum teste de tela olha para o documento que hospeda
 * o React — eles montam componentes no jsdom, cujo `<html>` vem do ambiente de teste e não deste
 * arquivo. A fronteira estava aberta e nada a fechava; varredura pontual fecharia a instância, e
 * é esta guarda que fecha o mecanismo.
 *
 * O dano que ela evita é o de acessibilidade: leitor de tela pronuncia o conteúdo com fonética do
 * inglês, e tradução/correção automática do navegador trata a página como inglesa. Mesma família
 * das preocupações que a Fase 1D fechou por teste (`contraste.test.ts`,
 * `semCorForaDaPaleta.test.ts`).
 *
 * A forma canônica exigida é exatamente `pt-BR` — subtag de idioma em minúsculas, de região em
 * maiúsculas, como manda o BCP 47. `pt-br` e `PT-BR` são HTML válido e o navegador aceita os
 * dois, mas guarda que aceita três grafias da mesma coisa deixa a base divergir sem avisar: aqui
 * só uma passa.
 */

const CAMINHO_HTML = new URL('../../index.html', import.meta.url)
const HTML = readFileSync(CAMINHO_HTML, 'utf8')

const IDIOMA_EXIGIDO = 'pt-BR'

/**
 * Apaga comentário HTML antes de procurar a tag. Sem isto, um `<html lang="pt-BR">` de exemplo
 * escrito dentro de um comentário satisfaria a guarda no lugar da tag real.
 *
 * Ao contrário do `semComentarios` de `semCorForaDaPaleta.test.ts`, este NÃO preserva a quebra de
 * linha: lá ela existe para o relatório citar o número de linha certo, e esta guarda não reporta
 * linha nenhuma. É o mesmo formato do `semComentarios` de `contraste.test.ts`, que também não
 * reporta linha.
 */
function semComentarios(fonte: string): string {
  return fonte.replace(/<!--[\s\S]*?-->/g, '')
}

/**
 * O `lang` da tag `<html>` raiz, ou `null` quando a tag de abertura não declara nenhum.
 *
 * O atributo só é reconhecido quando vem precedido de espaço em branco dentro da tag. O `\s` no
 * lugar de um `\b` é deliberado: `\b` casaria também o sufixo de `xml:lang`, e
 * `<html xml:lang="en" lang="pt-BR">` — que está CERTO — seria lido pelo atributo errado e
 * reprovado. Coberto por fixture abaixo, e a decisão é PORTANTE: medido na review de 2026-08-28,
 * trocar o `\s` por `\b` mata exatamente aquele fixture, e só ele.
 *
 * LIMITES CONHECIDOS, os três medidos na mesma review. Isto é parsing por regex, não por parser
 * de HTML, e a escolha é proporcional: o alvo é um arquivo estático de 13 linhas que muda uma vez
 * por ano. Declarados aqui em vez de fingidos inexistentes — mesmo padrão da limitação que
 * `semCorForaDaPaleta.test.ts` registra para o `semComentarios` dele.
 *
 * 1. **Valor sem aspas não é reconhecido.** `<html lang=pt-BR>` é HTML válido e o idioma estaria
 *    certo, mas o `["']` da regex não casa e a guarda reprova. Falha na direção SEGURA (reprova
 *    quem está certo, nunca aprova quem está errado) e este projeto escreve atributo com aspas
 *    em todo lugar. Pinado por fixture, para não virar surpresa de quem tropeçar.
 * 2. **`>` dentro de valor de atributo encerra a tag cedo.** `<html data-x="a>b" lang="pt-BR">`
 *    reprova, porque o `[^>]*>` para no `>` que está entre aspas. Também falha para o lado
 *    seguro.
 * 3. **`lang=` DENTRO do valor de outro atributo é lido como se fosse o atributo.**
 *    `<html title=" lang='pt-BR' ">`, sem nenhum `lang` de verdade, passa. É o único ponto cego
 *    em que a guarda aprova uma página que viola a WCAG 3.1.1 — fechá-lo exigiria um parser de
 *    HTML de verdade, o que não se paga para o alvo desta guarda.
 */
function idiomaDeclarado(fonte: string = HTML): string | null {
  const tagDeAbertura = semComentarios(fonte).match(/<html\b[^>]*>/i)
  if (!tagDeAbertura) throw new Error('tag <html> não encontrada em web/index.html')

  const atributo = tagDeAbertura[0].match(/\slang\s*=\s*["']([^"']*)["']/i)
  return atributo ? atributo[1] : null
}

describe('web/index.html declara o idioma da página', () => {
  it(`a tag <html> raiz declara lang="${IDIOMA_EXIGIDO}"`, () => {
    expect(
      idiomaDeclarado(),
      `web/index.html precisa declarar lang="${IDIOMA_EXIGIDO}" na tag <html> raiz (WCAG 3.1.1): ` +
        'a interface inteira é em português brasileiro.',
    ).toBe(IDIOMA_EXIGIDO)
  })
})

describe('idiomaDeclarado (mecanismo da guarda acima) — medido em fixture, não em produção', () => {
  it('acha o lang="en" que estava no arquivo até 2026-08-28', () => {
    const fixture = '<!doctype html>\n<html lang="en">\n  <head></head>\n</html>'

    expect(idiomaDeclarado(fixture)).toBe('en')
  })

  it('devolve null quando a tag <html> não declara lang nenhum', () => {
    expect(idiomaDeclarado('<!doctype html>\n<html>\n</html>')).toBeNull()
  })

  it('lê o lang em qualquer posição entre os outros atributos da tag', () => {
    const fixture = `<html data-tema="claro" lang="${IDIOMA_EXIGIDO}" class="h-full">`

    expect(idiomaDeclarado(fixture)).toBe(IDIOMA_EXIGIDO)
  })

  it('aceita aspas simples, que são HTML igualmente válido', () => {
    expect(idiomaDeclarado(`<html lang='${IDIOMA_EXIGIDO}'>`)).toBe(IDIOMA_EXIGIDO)
  })

  it('não confunde xml:lang com lang — a página abaixo está certa e não pode reprovar', () => {
    const fixture = `<html xml:lang="en" lang="${IDIOMA_EXIGIDO}">`

    expect(idiomaDeclarado(fixture)).toBe(IDIOMA_EXIGIDO)
  })

  it('não deixa um lang escrito dentro de comentário satisfazer a guarda pela tag real', () => {
    const fixture = `<!-- exemplo: <html lang="${IDIOMA_EXIGIDO}"> -->\n<html>\n</html>`

    expect(idiomaDeclarado(fixture)).toBeNull()
  })

  /**
   * Asserção POSITIVA de propósito. A forma anterior deste teste afirmava só
   * `.not.toBe(IDIOMA_EXIGIDO)`, e MEDIDO em 2026-08-28 (achado Minor 3 da review desta task):
   * mutando `idiomaDeclarado` para devolver `null` sempre, ele sobrevivia VERDE enquanto outros
   * cinco morriam — porque `null` também não é `pt-BR`. Afirmar o valor LIDO distingue "reprovou
   * pela grafia errada" de "o extrator parou de funcionar".
   */
  it('lê grafia fora da forma canônica do BCP 47 como ela é — e é isso que a faz reprovar', () => {
    for (const grafia of ['pt-br', 'PT-BR', 'pt']) {
      expect(idiomaDeclarado(`<html lang="${grafia}">`), `grafia "${grafia}"`).toBe(grafia)
      expect(idiomaDeclarado(`<html lang="${grafia}">`), `grafia "${grafia}"`).not.toBe(
        IDIOMA_EXIGIDO,
      )
    }
  })

  it('não reconhece valor sem aspas — limite 1, e ele falha para o lado seguro', () => {
    expect(idiomaDeclarado(`<html lang=${IDIOMA_EXIGIDO}>`)).toBeNull()
  })

  it('falha alto quando não existe tag <html> — nunca em silêncio, com um null ambíguo', () => {
    expect(() => idiomaDeclarado('<div>sem documento</div>')).toThrow(/tag <html> não encontrada/)
  })
})
