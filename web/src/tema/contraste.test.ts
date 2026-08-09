import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { razaoDeContraste, luminanciaRelativa } from './contraste'

// Lê o CSS de verdade, em vez de duplicar a paleta num objeto TS. Duplicar criaria a divergência
// clássica: alguém muda o token no CSS, o teste segue medindo o valor velho e reporta verde sobre
// uma cor que já não está na tela.
const css = readFileSync(new URL('../index.css', import.meta.url), 'utf8')

/**
 * Remove comentários de bloco antes de qualquer casamento de regex. Sem isto, um `}` em início de
 * linha dentro de um comentário do `@theme` (ex.: documentando um trecho de CSS) encerra cedo
 * demais o casamento não-guloso de `blocoTema`, e tudo que vem depois some das duas guardas ao
 * mesmo tempo — em silêncio, porque o Tailwind lê o `@theme` certo (o parser dele descarta
 * comentários) e emite a cor na tela mesmo assim. Não-guloso: CSS não tem comentário aninhado, a
 * primeira marca de fechamento sempre fecha a abertura anterior.
 *
 * Aplicada num lugar só, dentro de `blocoTema` — é lá que ela precisa valer, porque é `blocoTema`
 * que recebe fixture crua nos testes de `fonte`. Limpar também o `css` do módulo seria trabalho
 * morto: MEDIDO em 2026-08-09, apagar essa segunda limpeza deixava o arquivo 28/28 verde.
 */
function semComentarios(fonte: string): string {
  return fonte.replace(/\/\*[\s\S]*?\*\//g, '')
}

function blocoTema(fonte: string = css): string {
  const bloco = semComentarios(fonte).match(/@theme\s*\{([\s\S]*?)\n\}/)
  if (!bloco) throw new Error('bloco @theme não encontrado em index.css')
  return bloco[1]
}

function tokens(fonte: string = css): Record<string, string> {
  const mapa: Record<string, string> = {}
  for (const [, nome, valor] of blocoTema(fonte).matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    mapa[nome] = valor
  }
  return mapa
}

/**
 * Nomes de TODAS as declarações `--color-*` do bloco, seja qual for o formato do valor — ao
 * contrário de `tokens()`, que só casa `#RRGGBB`. A diferença entre as duas listas é exatamente o
 * que a guarda de formato mede.
 */
function declaracoesDeCor(fonte: string = css): string[] {
  return [...blocoTema(fonte).matchAll(/--color-([\w-]+)\s*:/g)].map(([, nome]) => nome)
}

const T = tokens()

// AA da WCAG: 4.5:1 para texto normal, 3:1 para componente de interface (borda de campo, ícone).
const TEXTO = 4.5
const INTERFACE = 3

const PARES: Array<{ frente: string; fundo: string; minimo: number; onde: string }> = [
  { frente: 'tinta', fundo: 'superficie', minimo: TEXTO, onde: 'texto padrão sobre cartão' },
  { frente: 'tinta', fundo: 'fundo', minimo: TEXTO, onde: 'texto padrão sobre o fundo da página' },
  { frente: 'tinta-fraca', fundo: 'superficie', minimo: TEXTO, onde: 'texto secundário e legendas' },
  { frente: 'superficie', fundo: 'chrome', minimo: TEXTO, onde: 'texto da barra de navegação' },
  { frente: 'marca', fundo: 'chrome', minimo: TEXTO, onde: 'logo sobre o chrome' },
  { frente: 'superficie', fundo: 'acao', minimo: TEXTO, onde: 'rótulo do botão primário' },
  { frente: 'superficie', fundo: 'acao-forte', minimo: TEXTO, onde: 'rótulo do primário sob hover' },
  { frente: 'acao', fundo: 'superficie', minimo: TEXTO, onde: 'link de ação e botão secundário' },
  { frente: 'acao', fundo: 'acao-fundo', minimo: TEXTO, onde: 'texto da pílula' },
  { frente: 'negativo', fundo: 'superficie', minimo: TEXTO, onde: 'mensagem de erro' },
  { frente: 'superficie', fundo: 'negativo', minimo: TEXTO, onde: 'rótulo do botão de perigo' },
  { frente: 'negativo-texto', fundo: 'negativo-fundo', minimo: TEXTO, onde: 'mensagem do banner de erro' },
  { frente: 'negativo', fundo: 'negativo-fundo', minimo: INTERFACE, onde: 'borda do banner de erro' },
  { frente: 'positivo-texto', fundo: 'superficie', minimo: TEXTO, onde: 'rótulo de estado aprovado/ativo' },
  { frente: 'superficie', fundo: 'positivo', minimo: TEXTO, onde: 'selo de estado positivo com texto branco' },
  { frente: 'positivo-texto', fundo: 'positivo-fundo', minimo: TEXTO, onde: 'texto da pílula de estado positivo' },
  { frente: 'negativo-texto', fundo: 'negativo-fundo', minimo: TEXTO, onde: 'texto da pílula de estado negativo' },
  { frente: 'borda-campo', fundo: 'superficie', minimo: INTERFACE, onde: 'borda de input e de botão secundário' },
  { frente: 'acao', fundo: 'fundo', minimo: INTERFACE, onde: 'anel de foco sobre o fundo da página' },
]

/**
 * Tokens que existem só como fundo ou separador e por isso não têm par de contraste próprio.
 * Cada exceção precisa de motivo escrito — a lista é curta de propósito: é ela que impede o
 * "declaro como decorativo e passo" de virar a saída fácil.
 */
const SEM_EXIGENCIA: Record<string, string> = {
  borda: 'separador decorativo entre linhas de lista; nenhuma informação depende de enxergá-lo',
}

describe('paleta declarada em index.css', () => {
  it('declara todos os tokens que o plano da fase fixou', () => {
    for (const nome of [
      'chrome', 'marca', 'acao', 'acao-forte', 'acao-fundo',
      'positivo', 'positivo-texto', 'negativo', 'negativo-texto', 'negativo-fundo',
      'tinta', 'tinta-fraca', 'borda', 'borda-campo', 'fundo', 'superficie',
    ]) {
      expect(T[nome], `token --color-${nome} ausente em index.css`).toBeTruthy()
    }
  })

  it.each(PARES)('$frente sobre $fundo passa AA ($onde)', ({ frente, fundo, minimo }) => {
    const razao = razaoDeContraste(T[frente], T[fundo])
    expect(razao, `${frente} sobre ${fundo} deu ${razao.toFixed(2)}:1`).toBeGreaterThanOrEqual(minimo)
  })

  it('não deixa entrar tom novo sem medição', () => {
    // A guarda que faz a regra da spec §3 valer sozinha: quem acrescentar um `--color-*` tem de
    // declarar onde ele é usado, ou registrá-lo em SEM_EXIGENCIA com motivo.
    const medidos = new Set(PARES.flatMap((p) => [p.frente, p.fundo]))
    const naoMedidos = Object.keys(T).filter((n) => !medidos.has(n) && !(n in SEM_EXIGENCIA))

    expect(naoMedidos, `tokens sem par de contraste declarado: ${naoMedidos.join(', ')}`).toEqual([])
  })

  it('toda dispensa em SEM_EXIGENCIA tem motivo não vazio', () => {
    // A checagem de "não deixa entrar tom novo sem medição" usa `n in SEM_EXIGENCIA`, que é
    // verdadeira mesmo para motivo `''` — o comentário da constante promete "motivo escrito", mas
    // nada obrigava a escrita. Honestidade do motivo é inverificável; existência não é.
    for (const [nome, motivo] of Object.entries(SEM_EXIGENCIA)) {
      expect(motivo.trim().length, `SEM_EXIGENCIA.${nome} tem motivo vazio`).toBeGreaterThan(0)
    }
  })

  it('não deixa entrar tom em formato que a medição não lê', () => {
    // Sem esta guarda a anterior tinha um buraco, MEDIDO em 2026-08-08: acrescentar
    // `--color-teste: oklch(0.7 0.15 60);` ao @theme deixava a suíte 166/166 verde, e
    // `--color-teste: #abc;` também. A regex de `tokens()` só casa hex de 6 dígitos, então um tom
    // em outro formato não entrava no mapa — não era medido E não era acusado de não-medido,
    // escapando das duas guardas. Grave porque `oklch()` é o formato nativo da paleta do
    // Tailwind v4: era justamente o caminho mais provável de um tom novo entrar sem prova.
    const naoParseados = declaracoesDeCor().filter((nome) => !(nome in T))

    expect(
      naoParseados,
      `tokens --color-* em formato que a medição não lê (esperado #RRGGBB): ${naoParseados.join(', ')}`,
    ).toEqual([])
  })

  it('mantém verde e vermelho reservados a estado — nenhum deles é o chrome nem a ação', () => {
    // A regra que sustenta a paleta (spec §3): cor de identidade nunca significa estado. Ela é o
    // que faz a tela de Qualidade da Fase 5 funcionar, quando "Aprovado" e "Abrir retrabalho"
    // aparecem na mesma linha.
    expect(T.chrome).not.toBe(T.positivo)
    expect(T.chrome).not.toBe(T.negativo)
    expect(T.acao).not.toBe(T.positivo)
    expect(T.acao).not.toBe(T.negativo)
  })
})

describe('blocoTema / tokens / declaracoesDeCor não truncam no `}` de um comentário (I1)', () => {
  // Sobre string, não sobre o index.css real: um teste não pode plantar comentário em arquivo de
  // produção. É por isso que as três funções aceitam `fonte` como parâmetro.
  it('não trunca: os dois tokens, antes e depois do comentário, aparecem', () => {
    const fixture = `
@theme {
  --color-um: #111111;
  /* nota de uso, com chave na coluna 0:
} */
  --color-dois: #222222;
}
`
    expect(tokens(fixture)).toEqual({ um: '#111111', dois: '#222222' })
  })

  it('a guarda de formato também enxerga depois do comentário', () => {
    const fixture = `
@theme {
  --color-um: #111111;
  /* nota de uso, com chave na coluna 0:
} */
  --color-oklch: oklch(0.7 0.15 60);
}
`
    expect(declaracoesDeCor(fixture)).toContain('oklch')
  })

  it('cor cujo único registro está dentro de um comentário não conta como declarada', () => {
    const fixture = `
@theme {
  /* --color-fake: rgb(1,2,3); */
  --font-sans: sans-serif;
}
`
    expect(declaracoesDeCor(fixture)).toEqual([])
  })
})

describe('razaoDeContraste', () => {
  it('dá 21 entre preto e branco', () => {
    expect(razaoDeContraste('#000000', '#ffffff')).toBeCloseTo(21, 1)
  })

  it('dá 1 para a mesma cor', () => {
    expect(razaoDeContraste('#134E4A', '#134E4A')).toBeCloseTo(1, 5)
  })

  it('é simétrica', () => {
    expect(razaoDeContraste('#134E4A', '#ffffff')).toBeCloseTo(razaoDeContraste('#ffffff', '#134E4A'), 10)
  })

  it('linearizea o canal escuro pela rampa, não pela potência', () => {
    // Abaixo de 0.03928 a WCAG usa `c / 12.92`, não a exponencial. Trocar por `** 2.4` em toda a
    // faixa erra justamente nos tons muito escuros — que é onde vive o chrome.
    expect(luminanciaRelativa('#050505')).toBeCloseTo(0.00152, 4)
  })

  it('atribui o coeficiente do vermelho (0,2126) ao canal certo', () => {
    // Prova cromática: os quatro testes acima usam só preto, branco e cinza — nenhum distingue a
    // qual canal cada peso se aplica. Trocar os índices dos canais (R por B, mantendo os pesos nas
    // posições) passaria por todos eles sem ser notado.
    expect(luminanciaRelativa('#FF0000')).toBeCloseTo(0.2126, 4)
  })
})
