import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

/**
 * I2 da review de branco da 1D: nada impedia uma cor fora da paleta deste projeto de entrar num
 * `className` de produção. MEDIDO: reverter `TelaCarregando.tsx` da paleta do tema para a paleta
 * PADRÃO da Tailwind (nenhuma declaração `--color-*` nova — então as duas guardas de
 * `contraste.test.ts`, que só olham o `@theme`, não veem nada de errado) deixava a suíte inteira
 * verde. `CLAUDE.md` descrevia essa fronteira como fechada por uma varredura pontual da Task 12;
 * varredura não é guarda, porque não protege a tela seguinte.
 *
 * Esta guarda é o mecanismo: varre TODO `web/src/` (não só `components/`) atrás de qualquer
 * classe no formato `prefixo(-direção)?-cor-tom` cuja COR é um nome da paleta padrão da Tailwind
 * — nenhum dos quais é token deste projeto (ver `web/src/index.css`: chrome, marca, acao,
 * positivo, negativo, tinta, borda, fundo, superficie, e as variantes delas). Reusa o caminhador
 * de arquivos e o removedor de comentário de `semModificadorDeOpacidadeEmCor.test.ts`.
 */

const RAIZ_SRC = fileURLToPath(new URL('..', import.meta.url))

/**
 * Este próprio arquivo é excluído da varredura pelo mesmo motivo do molde: os fixtures abaixo
 * PRECISAM conter, depois de montados em runtime, o padrão `prefixo-cor-tom` para testar a
 * extração — nenhum deles é `className` de produto.
 */
const CAMINHO_DESTE_ARQUIVO = fileURLToPath(import.meta.url)

/** Varredura recursiva; só `.ts`/`.tsx`, igual ao escopo das outras duas guardas de `src/tema/`. */
function arquivosFonte(dir: string): string[] {
  const resultado: string[] = []
  for (const entrada of readdirSync(dir, { withFileTypes: true })) {
    const caminho = join(dir, entrada.name)
    if (entrada.isDirectory()) {
      resultado.push(...arquivosFonte(caminho))
    } else if (/\.tsx?$/.test(entrada.name)) {
      resultado.push(caminho)
    }
  }
  return resultado
}

/**
 * Apaga comentário de bloco e de linha, sem apagar quebra de linha — mesma função, mesma
 * limitação conhecida (string com abertura de bloco sem fechamento na MESMA string), documentada
 * em `semModificadorDeOpacidadeEmCor.test.ts`. Não redocumentada aqui por extenso para não
 * duplicar o mesmo texto duas vezes na base.
 */
function semComentarios(fonte: string): string {
  return fonte
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(/\/\/[^\n]*/g, (m) => ' '.repeat(m.length))
}

// Prefixos de utilitário que aceitam cor — mesma lista da guarda de opacidade.
const PREFIXOS = 'bg|text|border|ring|outline|from|via|to|divide|shadow|fill|stroke|accent|caret|decoration|placeholder'

// Variante direcional (ex.: só o lado de cima de uma borda). SEM isto a guarda tem o mesmo buraco
// que ela existe para fechar: a mutação que a review mediu usava exatamente uma variante
// direcional de `border-`, e um prefixo que só reconhece a forma sem direção deixaria passar.
const DIRECOES = 't|r|b|l|x|y|s|e'

// Nomes de cor da paleta PADRÃO da Tailwind. Nenhum é token deste projeto — ver o bloco `@theme`
// de `web/src/index.css`, que define chrome/marca/acao/positivo/negativo/tinta/borda/fundo/
// superficie e as variantes delas, nenhuma com estes nomes.
const CORES_PADRAO_TAILWIND = [
  'slate', 'gray', 'zinc', 'neutral', 'stone',
  'red', 'orange', 'amber', 'yellow', 'lime', 'green', 'emerald', 'teal', 'cyan', 'sky', 'blue',
  'indigo', 'violet', 'purple', 'fuchsia', 'pink', 'rose',
].join('|')

// Tons reais da escala da Tailwind (não é uma sequência contínua: pula de 100 em 100 acima de
// 100, e o mínimo é 50).
const TONS = '50|100|200|300|400|500|600|700|800|900|950'

const REGEX_COR_FORA_DA_PALETA = new RegExp(
  `\\b(?:${PREFIXOS})(?:-(?:${DIRECOES}))?-(?:${CORES_PADRAO_TAILWIND})-(?:${TONS})\\b`,
  'g',
)

type Violacao = { linha: number; classe: string }

function violacoesEmArquivo(caminho: string): Violacao[] {
  const bruto = readFileSync(caminho, 'utf8')
  const limpo = semComentarios(bruto)
  const achados: Violacao[] = []
  limpo.split('\n').forEach((linhaTexto, indice) => {
    for (const m of linhaTexto.matchAll(REGEX_COR_FORA_DA_PALETA)) {
      achados.push({ linha: indice + 1, classe: m[0] })
    }
  })
  return achados
}

function caminhoRelativo(caminho: string): string {
  return caminho.slice(RAIZ_SRC.length).replace(/\\/g, '/')
}

/**
 * Dispensa nomeada, no molde das outras duas guardas: motivo escrito obrigatório, chave por
 * arquivo + classe exata. Vazia hoje — nenhuma cor fora da paleta tem uso legítimo no projeto.
 */
const SEM_EXIGENCIA: Record<string, string> = {}

describe('nenhuma classe de cor usa um tom da paleta padrão da Tailwind fora de dispensa nomeada', () => {
  it('varre web/src/ inteiro e não deixa cor fora da paleta do projeto escapar sem dispensa', () => {
    const achados: string[] = []
    for (const caminho of arquivosFonte(RAIZ_SRC)) {
      if (caminho === CAMINHO_DESTE_ARQUIVO) continue
      const rel = caminhoRelativo(caminho)
      for (const { linha, classe } of violacoesEmArquivo(caminho)) {
        if (`${rel}::${classe}` in SEM_EXIGENCIA) continue
        achados.push(`${rel}:${linha} — classe "${classe}"`)
      }
    }

    expect(
      achados,
      `classe de cor fora da paleta do projeto sem dispensa nomeada em SEM_EXIGENCIA:\n${achados.join('\n')}`,
    ).toEqual([])
  })

  it('toda dispensa em SEM_EXIGENCIA tem motivo não vazio', () => {
    for (const [chave, motivo] of Object.entries(SEM_EXIGENCIA)) {
      expect(motivo.trim().length, `SEM_EXIGENCIA['${chave}'] tem motivo vazio`).toBeGreaterThan(0)
    }
  })
})

describe('REGEX_COR_FORA_DA_PALETA (mecanismo da guarda acima) — medido em fixture, não em produção', () => {
  // Os três valores abaixo são os EXATOS que a review reverteu em TelaCarregando.tsx para medir
  // 117/117 verdes sem guarda nenhuma (I2) — inclusive a variante DIRECIONAL, que é o ponto que
  // esta guarda existe para não deixar escapar de novo.
  //
  // Montados por concatenação, e não como uma classe inteira escrita de uma vez: o scanner da
  // Tailwind lê o arquivo inteiro por BYTE, fixture de teste incluído (mesmo aviso de
  // `semModificadorDeOpacidadeEmCor.test.ts` e de `ListaDeCadastro.tsx`), e as três classes abaixo
  // são REAIS — escritas por extenso, plantariam de verdade, no CSS de produção, o exato padrão
  // que esta guarda existe para proibir. Nenhum pedaço isolado (`'bg'`, `'gray'`, `'50'`…) é uma
  // classe válida sozinho, então nenhum deles gera regra nenhuma; só a STRING montada em runtime
  // tem a forma completa, e é só essa string que o `matchAll` abaixo enxerga.
  const classeSemDirecao = ['bg', 'gray', '50'].join('-')
  const classeComDirecao = ['border', 't', 'blue', '600'].join('-')
  const classeTexto = ['text', 'gray', '500'].join('-')

  it('acha as três classes exatas que a review mediu, inclusive a variante direcional', () => {
    const fixture = `const cls = "${classeSemDirecao} ${classeComDirecao} ${classeTexto}"`
    const achados = [...fixture.matchAll(REGEX_COR_FORA_DA_PALETA)].map((m) => m[0])

    expect(achados).toEqual([classeSemDirecao, classeComDirecao, classeTexto])
  })

  it('NÃO acha token da paleta deste projeto', () => {
    const fixture = 'const cls = "bg-fundo text-tinta-fraca border-borda-campo text-acao-forte"'

    expect([...fixture.matchAll(REGEX_COR_FORA_DA_PALETA)]).toEqual([])
  })

  it('ignora a classe quando ela está dentro de um comentário de bloco', () => {
    const fixture = `const a = 1\n/* ${classeSemDirecao} */\nconst cls = "tudo bem"`
    const limpo = semComentarios(fixture)

    expect(limpo).not.toContain(classeSemDirecao)
  })

  it('ignora a classe quando ela está dentro de um comentário de linha', () => {
    const fixture = `const a = 1\n// ${classeSemDirecao} (forma rejeitada)\nconst cls = "tudo bem"`
    const limpo = semComentarios(fixture)

    expect(limpo).not.toContain(classeSemDirecao)
  })

  it('NÃO apaga a classe quando ela está fora de comentário', () => {
    const fixture = `const cls = "${classeSemDirecao}"`

    expect(semComentarios(fixture)).toContain(classeSemDirecao)
  })
})
