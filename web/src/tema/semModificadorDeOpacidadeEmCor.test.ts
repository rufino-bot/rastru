import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { join } from 'node:path'

/**
 * I3 da review da Task 6: o Critical da Task 5 (`/NN` de modificador de opacidade em classe de
 * cor, ex. `bg-negativo/10`) foi corrigido por INSTÂNCIA — só na `Pilula` e no `BannerDeErro` —
 * nunca por mecanismo. `/NN` compõe com `color-mix(in oklab, …)`, que não é declaração
 * `--color-*` e por isso escapa inteiro das duas guardas de `contraste.test.ts`. Esta guarda é o
 * mecanismo: varre TODO `web/src/` (não só `components/`) atrás do padrão, e falha nomeando
 * arquivo, linha e a classe encontrada.
 */

const RAIZ_SRC = fileURLToPath(new URL('..', import.meta.url))

/**
 * Este próprio arquivo é excluído da varredura: o nome da classe dispensada aparece como STRING
 * na chave de `SEM_EXIGENCIA` (dado, não estilo), e os fixtures de `semComentarios` abaixo
 * PRECISAM conter `/NN` literal para testar a extração. Nenhum dos dois é `className` de produto.
 */
const CAMINHO_DESTE_ARQUIVO = fileURLToPath(import.meta.url)

/** Varredura recursiva; só `.ts`/`.tsx`, igual ao escopo que o brief mediu (49 arquivos). */
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
 * Apaga comentário de bloco (`/* … *\/`) e de linha (`//…`), SEM apagar as quebras de linha — troca
 * cada caractere não-quebra-de-linha por espaço, para a contagem de linha do resultado continuar
 * batendo com a do arquivo original. É o que permite reportar `arquivo:linha` de verdade.
 *
 * Limitação conhecida e NÃO resolvida (a mesma classe de risco que `contraste.test.ts` documenta
 * para o `}` de comentário CSS): isto não entende string literal. Uma string contendo um `/*` sem
 * `*\/` correspondente na MESMA string prenderia o "comentário" até o próximo `*\/` real do
 * arquivo, apagando código de verdade no meio — inclusive um `/NN` vivo. Medido: hoje não existe
 * nenhuma string assim em `web/src/` (as três ocorrências do padrão, ver `contraste.test.ts` e o
 * próprio arquivo, são comentário de linha `//`, não de bloco). Se uma nascer, esta guarda pode
 * ficar cega para o trecho entre ela e o próximo `*\/`. Não é o caminho que sangrou três vezes
 * nesta fase — é risco não observado, registrado para não ser encenado como fechado.
 */
function semComentarios(fonte: string): string {
  return fonte
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(/\/\/[^\n]*/g, (m) => ' '.repeat(m.length))
}

/** Mesma classe de modificador que a review mediu (bg/text/border/... seguido de `/NN`). */
const REGEX_OPACIDADE_DE_COR =
  /\b(bg|text|border|ring|outline|from|via|to|divide|shadow|fill|stroke|accent|caret|decoration|placeholder)-[a-z0-9-]+\/[0-9]+/g

type Violacao = { linha: number; classe: string }

function violacoesEmArquivo(caminho: string): Violacao[] {
  const bruto = readFileSync(caminho, 'utf8')
  const limpo = semComentarios(bruto)
  const achados: Violacao[] = []
  limpo.split('\n').forEach((linhaTexto, indice) => {
    for (const m of linhaTexto.matchAll(REGEX_OPACIDADE_DE_COR)) {
      achados.push({ linha: indice + 1, classe: m[0] })
    }
  })
  return achados
}

// Calculado à mão, sem chamar a função de mesmo nome do `node:path`: o scanner do Tailwind varre
// BYTE de arquivo, não sintaxe, e essa função de caminho tem o MESMO NOME de uma utilidade de
// posicionamento da Tailwind — só o identificador aparecer no código bastaria para uma regra CSS
// que nenhuma tela usa entrar no bundle de produção. MEDIDO: foi exatamente o que aconteceu antes
// desta troca (e antes de eu parar de escrever o nome dela em texto explicativo também — o
// scanner não distingue comentário de código) — único diff de bytes do build com este arquivo.
// `RAIZ_SRC` sempre termina em separador (mesma forma que `contraste.test.ts` usa com `../`) e
// todo `caminho` aqui nasce de `join(RAIZ_SRC, …)`, então a fatia por tamanho é exata.
function caminhoRelativo(caminho: string): string {
  return caminho.slice(RAIZ_SRC.length).replace(/\\/g, '/')
}

/**
 * Dispensa nomeada, no molde do `SEM_EXIGENCIA` de `contraste.test.ts`: motivo escrito
 * obrigatório, chave por arquivo + classe exata (não por linha — sobrevive a reformatação, e
 * não exime a classe em nenhum OUTRO arquivo). Hoje há duas: o véu do modal de exclusão de
 * agrupamento, e o véu da primitiva `Confirmacao` (Task 8b) que o extraiu.
 */
const SEM_EXIGENCIA: Record<string, string> = {
  // Task 10 re-layout trocou a cor crua original do véu pelo token do tema — mesmo véu, mesma
  // dispensa, só o nome da classe mudou (não escrito por extenso: o scanner do Tailwind lê o
  // fonte inteiro, comentário incluído, e a forma antiga plantava uma regra no CSS de produção
  // que elemento nenhum usa — ver o mesmo aviso em ListaDeCadastro.tsx).
  'pages/PedidoDetalhePage.tsx::bg-tinta/50':
    'véu (scrim) do modal de confirmação de exclusão — `fixed inset-0`, com o painel `bg-superficie` ' +
    'sobreposto (`PedidoDetalhePage.tsx:183`). É decorativo: nenhum texto é lido sobre ele, então ' +
    'não há par de contraste texto/fundo a medir. WCAG 1.4.3 não se aplica a fundo sem conteúdo.',
  // Task 8b: `Confirmacao` é a primitiva extraída do MESMO véu acima (ver o comentário do
  // componente) — mesma forma, mesmo motivo, endereço novo porque o JSX mora num arquivo novo.
  'components/Confirmacao.tsx::bg-tinta/50':
    'véu (scrim) do diálogo de confirmação — `fixed inset-0`, com o painel `bg-superficie` ' +
    'sobreposto. Decorativo, sem texto sobre ele: mesma dispensa de `PedidoDetalhePage.tsx` acima, ' +
    'que este componente generaliza.',
}

describe('nenhuma classe de cor usa modificador de opacidade (/NN) fora de dispensa nomeada', () => {
  it('varre web/src/ inteiro e não deixa `/NN` de cor escapar sem dispensa', () => {
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
      `classe de cor com modificador /NN sem dispensa nomeada em SEM_EXIGENCIA:\n${achados.join('\n')}`,
    ).toEqual([])
  })

  it('toda dispensa em SEM_EXIGENCIA tem motivo não vazio', () => {
    for (const [chave, motivo] of Object.entries(SEM_EXIGENCIA)) {
      expect(motivo.trim().length, `SEM_EXIGENCIA['${chave}'] tem motivo vazio`).toBeGreaterThan(0)
    }
  })
})

describe('semComentarios (mecanismo da guarda acima) — medido em fixture, não em produção', () => {
  // Os fixtures usam `bg-corfixture`/`text-corfixture` — nomes que NÃO são token de `@theme` —
  // e não `bg-negativo`, de propósito: `@tailwindcss/vite` varre todo o código-fonte, TESTE
  // incluído, atrás de nome de classe. Um fixture com `bg-negativo/10` (cor real) faz o próprio
  // Tailwind emitir `color-mix(...)` dela no CSS final — plantando de verdade, no bundle, o
  // padrão que esta guarda existe para proibir. `corfixture` não bate com nenhum `--color-*`
  // declarado, então não gera CSS nenhum; a regex da guarda não liga para o NOME do token, só
  // para a forma `token-nome/NN`.
  it('apaga comentário de bloco e preserva a contagem de linha', () => {
    const fixture = 'const a = 1\n/* bg-corfixture/10 */\nconst cls = "tudo bem"'
    const limpo = semComentarios(fixture)
    expect(limpo.split('\n')).toHaveLength(3)
    expect(limpo).not.toContain('bg-corfixture/10')
  })

  it('apaga comentário de linha (`//`) e preserva a contagem de linha', () => {
    const fixture = 'const a = 1\n// bg-corfixture/10 (forma rejeitada)\nconst cls = "tudo bem"'
    const limpo = semComentarios(fixture)
    expect(limpo.split('\n')).toHaveLength(3)
    expect(limpo).not.toContain('bg-corfixture/10')
  })

  it('NÃO apaga classe viva fora de comentário', () => {
    const fixture = 'const cls = "bg-corfixture/10 text-corfixture/20"'
    expect(semComentarios(fixture)).toContain('bg-corfixture/10')
  })

  it('um `/* */` autocontido dentro de string não avança para além dela (caso seguro)', () => {
    // O `/*` e o `*/` estão os DOIS dentro da mesma string — não há abertura pendente para
    // "vazar" até um `*/` real mais adiante. Prova o caso seguro; o caso arriscado (abertura SEM
    // fechamento na mesma string) é a limitação documentada acima, não testável como "correto"
    // porque o comportamento correto ali não está implementado.
    const fixture = 'const s = "/* nota */"\nconst cls = "bg-corfixture/10"'
    const limpo = semComentarios(fixture)
    expect(limpo.split('\n')).toHaveLength(2)
    expect(limpo).toContain('bg-corfixture/10')
  })
})
