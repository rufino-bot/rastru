// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

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
 * inglês, e tradução automática do navegador trata a página como inglesa. Mesma família das
 * preocupações que a Fase 1D fechou por teste (`contraste.test.ts`, `semCorForaDaPaleta.test.ts`).
 *
 * A forma canônica exigida é exatamente `pt-BR` — subtag de idioma em minúsculas, de região em
 * maiúsculas, como manda o BCP 47. `pt-br` e `PT-BR` são HTML válido e o navegador aceita os
 * dois, mas guarda que aceita três grafias da mesma coisa deixa a base divergir sem avisar: aqui
 * só uma passa.
 *
 * ## Por que um parser de HTML, e não uma regex
 *
 * A primeira versão desta guarda casava o atributo por regex. A re-review de 2026-08-28 mediu
 * cinco formas de enganá-la — todas fazendo a guarda aprovar um documento que NÃO declara idioma
 * nenhum. A pior não usava atributo exótico algum: um comentário sem terminador (`<!--` sem
 * `-->`) engole o resto do arquivo, não sobra `<html>` nenhum, e a guarda passava 10/10 verde.
 * As outras quatro eram `lang=` escrito dentro do valor de outro atributo, `pt-BR` dentro de uma
 * string em `<script>`, um comentário no meio do nome da tag, e um decoy que escondia um `lang`
 * REAL e errado.
 *
 * A resposta a isso não é declarar os cinco em prosa — este projeto trata frase de fechamento
 * como o dano, e a versão anterior deste arquivo chegou a afirmar que um deles era "o único",
 * o que a medição desmentiu. A resposta é fechar a família inteira, parseando o documento em vez
 * de varrer o texto dele: a pergunta que importa é **qual idioma o navegador vai ver**, não qual
 * sequência de caracteres está no arquivo. Os cinco enganos viram `null` ou `en`, e a guarda
 * reprova todos. Cada um deles tem fixture abaixo, para que o fechamento continue provado.
 *
 * O parser é o `DOMParser` do ambiente de teste, sob `@vitest-environment jsdom` — a mesma
 * convenção dos testes de tela deste projeto. Preferido a importar `jsdom` direto por dois
 * motivos, o segundo medido: é a API padrão do navegador, tipada pelo `lib.dom` do TypeScript, e
 * importar o pacote exigiria `@types/jsdom` — sem ele `npm test` fica VERDE e só `npm run build`
 * quebra (TS7016), que é exatamente a armadilha que `CLAUDE.md` manda evitar rodando os dois.
 */

/**
 * Caminho montado com `fileURLToPath` + `join`, como em `semCorForaDaPaleta.test.ts`, e não com
 * `new URL(..., import.meta.url)` como em `contraste.test.ts`. O motivo é o
 * `@vitest-environment jsdom` do topo: sob ele o `URL` global é o do jsdom, e o `readFileSync` do
 * Node recusa esse objeto com `TypeError: The URL must be of scheme file`. As outras guardas de
 * `src/tema/` rodam no ambiente `node` padrão e não esbarram nisso.
 */
const CAMINHO_HTML = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'index.html')
const HTML = readFileSync(CAMINHO_HTML, 'utf8')

const IDIOMA_EXIGIDO = 'pt-BR'

/**
 * O `lang` do elemento raiz do documento, como o NAVEGADOR o enxerga — ou `null` quando o
 * documento não declara nenhum.
 *
 * Note que não existe mais o caso "não achei a tag `<html>`": o parser da spec sempre constrói um
 * `documentElement`, mesmo para um fragmento solto. Um documento sem `<html>` escrito à mão cai
 * no caso `null` e reprova do mesmo jeito — o que é o comportamento certo, e é o que o navegador
 * faz.
 */
function idiomaDeclarado(fonte: string = HTML): string | null {
  const documento = new DOMParser().parseFromString(fonte, 'text/html')
  return documento.documentElement.getAttribute('lang')
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

  it('devolve null para documento sem tag <html> escrita — o parser cria a raiz implícita', () => {
    expect(idiomaDeclarado('<div>fragmento solto</div>')).toBeNull()
  })

  it('lê o lang em qualquer posição entre os outros atributos da tag', () => {
    const fixture = `<html data-tema="claro" lang="${IDIOMA_EXIGIDO}" class="h-full">`

    expect(idiomaDeclarado(fixture)).toBe(IDIOMA_EXIGIDO)
  })

  it('aceita aspas simples e valor sem aspas, que são HTML igualmente válido', () => {
    expect(idiomaDeclarado(`<html lang='${IDIOMA_EXIGIDO}'>`)).toBe(IDIOMA_EXIGIDO)
    expect(idiomaDeclarado(`<html lang=${IDIOMA_EXIGIDO}>`)).toBe(IDIOMA_EXIGIDO)
  })

  it('não confunde xml:lang com lang — a página abaixo está certa e não pode reprovar', () => {
    const fixture = `<html xml:lang="en" lang="${IDIOMA_EXIGIDO}">`

    expect(idiomaDeclarado(fixture)).toBe(IDIOMA_EXIGIDO)
  })

  it('não se perde com um > dentro do valor de um atributo anterior', () => {
    const fixture = `<html data-x="a>b" lang="${IDIOMA_EXIGIDO}">`

    expect(idiomaDeclarado(fixture)).toBe(IDIOMA_EXIGIDO)
  })
})

/**
 * Os cinco enganos que a re-review de 2026-08-28 mediu contra a versão anterior desta guarda, que
 * casava o atributo por regex. Em TODOS, a guarda antiga lia `pt-BR` e aprovava um documento que
 * não declara idioma nenhum (ou, no último, que declara um idioma ERRADO). Cada um está aqui para
 * que o fechamento continue provado: se alguém trocar o jsdom por uma varredura de texto de novo,
 * é este bloco que morre.
 */
describe('enganos que derrubavam a versão por regex — todos reprovam agora', () => {
  it('comentário SEM terminador engole o documento, e não sobra idioma nenhum', () => {
    const fixture = '<!doctype html>\n<!-- rascunho: <html lang="pt-BR">\n</html>'

    expect(idiomaDeclarado(fixture)).toBeNull()
  })

  it('lang= escrito dentro do valor de OUTRO atributo não é o lang do documento', () => {
    expect(idiomaDeclarado(`<html title=" lang='pt-BR' ">`)).toBeNull()
  })

  it('pt-BR dentro de uma string em <script> não declara idioma', () => {
    const fixture = '<!doctype html>\n<body><script>var a = "<html lang=\\"pt-BR\\">"</script></body>'

    expect(idiomaDeclarado(fixture)).toBeNull()
  })

  it('comentário no meio do nome da tag não produz um <html> válido', () => {
    expect(idiomaDeclarado('<ht<!-- x -->ml lang="pt-BR">')).toBeNull()
  })

  it('decoy antes de um lang REAL e errado — o que vale é o atributo de verdade', () => {
    const fixture = `<html data-foo=" lang='pt-BR' " lang="en">`

    expect(idiomaDeclarado(fixture)).toBe('en')
  })

  it('lang escrito dentro de comentário fechado não vale pela tag real', () => {
    const fixture = `<!-- exemplo: <html lang="${IDIOMA_EXIGIDO}"> -->\n<html>\n</html>`

    expect(idiomaDeclarado(fixture)).toBeNull()
  })
})

describe('só a grafia canônica do BCP 47 passa', () => {
  /**
   * Asserção POSITIVA de propósito. A forma anterior deste teste afirmava só
   * `.not.toBe(IDIOMA_EXIGIDO)`, e MEDIDO em 2026-08-28 (Minor 3 da review desta task): mutando
   * `idiomaDeclarado` para devolver `null` sempre, ele sobrevivia VERDE enquanto outros cinco
   * morriam — porque `null` também não é `pt-BR`. Afirmar o valor LIDO distingue "reprovou pela
   * grafia errada" de "o extrator parou de funcionar".
   */
  it('lê grafia fora da forma canônica como ela é — e é isso que a faz reprovar', () => {
    for (const grafia of ['pt-br', 'PT-BR', 'pt']) {
      expect(idiomaDeclarado(`<html lang="${grafia}">`), `grafia "${grafia}"`).toBe(grafia)
      expect(idiomaDeclarado(`<html lang="${grafia}">`), `grafia "${grafia}"`).not.toBe(
        IDIOMA_EXIGIDO,
      )
    }
  })
})
