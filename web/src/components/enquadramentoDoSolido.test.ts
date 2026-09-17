import { describe, it, expect } from 'vitest'
import { ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO, enquadramentoDoSolido } from './enquadramentoDoSolido'

describe('enquadramentoDoSolido', () => {
  it('mantém a distância proporcional ao tamanho do sólido, quando raio e meia altura escalam juntos', () => {
    // Sem isto, um STL grande (metros, em unidade de CAD) e um pequeno (milímetros) exigiriam
    // câmeras com fórmulas diferentes — a mesma fórmula linear tem de servir aos dois, só mudando
    // as duas dimensões de entrada pelo mesmo fator.
    const pequeno = enquadramentoDoSolido(15, 10, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)
    const grande = enquadramentoDoSolido(3000, 2000, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)

    expect(grande.distancia / pequeno.distancia).toBeCloseTo(200, 10)
  })

  it('afasta a câmera quando a margem cresce, na mesma proporção da margem', () => {
    const semFolga = enquadramentoDoSolido(100, 80, ABERTURA_VERTICAL_EM_GRAUS, 1, 1)
    const comFolga = enquadramentoDoSolido(100, 80, ABERTURA_VERTICAL_EM_GRAUS, 1, 2)

    expect(comFolga.distancia / semFolga.distancia).toBeCloseTo(2, 10)
  })

  it('nunca produz distância zero, NaN nem near >= far para um sólido degenerado (raio e altura 0)', () => {
    // STL com um único ponto, ou vazio, dá as duas dimensões de entrada zero — sem um piso, a
    // distância pela largura e a distância pela altura dariam zero e a câmera nasceria dentro do
    // próprio sólido inexistente.
    const resultado = enquadramentoDoSolido(0, 0, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)

    expect(resultado.distancia).toBeGreaterThan(0)
    expect(Number.isNaN(resultado.distancia)).toBe(false)
    expect(Number.isNaN(resultado.near)).toBe(false)
    expect(Number.isNaN(resultado.far)).toBe(false)
    expect(resultado.near).toBeLessThan(resultado.far)
  })

  it('afasta near e far junto com o tamanho do sólido, na mesma proporção do raio e da altura', () => {
    // "Proporcionais ao tamanho": hoje `0.1`/`10000` fixos cortariam um sólido de vários metros e
    // perderiam precisão de profundidade num sólido de poucos milímetros — os dois têm de escalar
    // com o tamanho, não ficar presos a um par de números que só serve a um tamanho de peça.
    const pequeno = enquadramentoDoSolido(15, 10, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)
    const grande = enquadramentoDoSolido(3000, 2000, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)

    expect(grande.near / pequeno.near).toBeCloseTo(200, 10)
    expect(grande.far / pequeno.far).toBeCloseTo(200, 10)
  })

  it('mantém near dentro do far também para um sólido grande', () => {
    const resultado = enquadramentoDoSolido(3000, 2000, ABERTURA_VERTICAL_EM_GRAUS, 1.75, MARGEM_DE_ENQUADRAMENTO)

    expect(resultado.near).toBeGreaterThan(0)
    expect(resultado.near).toBeLessThan(resultado.far)
  })

  it('escolhe a distância pela LARGURA e usa a proporção certa do quadro, não a abertura vertical nos dois eixos', () => {
    // 'mantém a distância proporcional...', 'afasta a câmera quando a margem cresce...' e 'afasta
    // near e far junto...' amarram a fórmula só a RAZÕES entre chamadas com a MESMA proporção — o
    // que não distingue "usa a proporção para calcular a meia abertura horizontal" de "ignora a
    // proporção e usa sempre a vertical", porque as duas variantes são igualmente lineares em raio
    // e margem. Distinguir isso exige valor absoluto com DUAS proporções diferentes, que é o que
    // este teste faz.
    //
    // Com abertura de 90°, a meia abertura vertical é 45° e `tan(45°) = 1` (a menos do erro de
    // ponto flutuante de `Math.PI`), então `tan(meia abertura horizontal) = proporção × 1 =
    // proporção` — a meia abertura horizontal fica exatamente `atan(proporção)`. Com `raio = 1000`
    // e `meia altura = 1`, a distância pela altura fica perto de `1001` nas duas chamadas
    // (`quadrado` e `largo`, bem menor que a distância pela largura em ambas), então o "max" escolhe
    // sempre a largura, e o valor final muda com a proporção: `atan(1) = 45°`, `sin(45°) = √2/2`,
    // distância = `1000 / (√2/2) = 1000√2 ≈ 1414,213562`; `atan(2) ≈ 63,43°`,
    // `sin(atan(2)) = 2/√5` (triângulo 1-2-√5), distância = `1000 / (2/√5) = 500√5 ≈ 1118,033989`.
    // Os dois valores foram conferidos à mão (identidades de triângulo retângulo) antes de entrar
    // aqui, nunca recalculados chamando `enquadramentoDoSolido`. Se a proporção fosse ignorada
    // (sempre 45°), as duas chamadas dariam o MESMO valor (≈1414,21) — é essa igualdade que este
    // teste mata.
    const quadrado = enquadramentoDoSolido(1000, 1, 90, 1, 1)
    const largo = enquadramentoDoSolido(1000, 1, 90, 2, 1)

    expect(quadrado.distancia).toBeCloseTo(1414.213562, 5)
    expect(largo.distancia).toBeCloseTo(1118.033989, 5)
  })

  it('mantém distanciaMinima estritamente maior que raioNoPlanoDeGiro para peça dominada pela LARGURA, em três proporções bem separadas', () => {
    // O piso de zoom antigo multiplicava a DISTÂNCIA final (que muda com a proporção do quadro) por
    // um fator fixo — e quando a largura domina num quadro largo o bastante, a distância mínima
    // resultante ficava MENOR que o próprio raio da peça, deixando a câmera entrar nela ao orbitar.
    // `distanciaMinima` multiplica só o raio (nunca a distância), e por isso não depende da
    // proporção: as três proporções 0,5 / 1,0 e 1,75 (a mesma faixa que expôs o defeito), com
    // raioNoPlanoDeGiro = 100 e meiaAlturaEmY = 1 (bem menor que o raio, então a largura domina nas
    // três — conferido à mão: mesmo na proporção mais estreita, 0,5, a distância pela altura fica em
    // 101 contra 223,6 pela largura), produzem `distanciaMinima = raio × 1,1 = 110` — o mesmo valor
    // nas três é essa independência da proporção que fecha o defeito.
    const estreita = enquadramentoDoSolido(100, 1, 90, 0.5, 1)
    const quadrada = enquadramentoDoSolido(100, 1, 90, 1, 1)
    const larga = enquadramentoDoSolido(100, 1, 90, 1.75, 1)

    expect(estreita.distanciaMinima).toBeCloseTo(110, 9)
    expect(quadrada.distanciaMinima).toBeCloseTo(110, 9)
    expect(larga.distanciaMinima).toBeCloseTo(110, 9)
    expect(estreita.distanciaMinima).toBeGreaterThan(100)
    expect(quadrada.distanciaMinima).toBeGreaterThan(100)
    expect(larga.distanciaMinima).toBeGreaterThan(100)
  })

  it('mantém distanciaMinima estritamente maior que raioNoPlanoDeGiro para peça dominada pela ALTURA, nas mesmas três proporções', () => {
    // Complementa "para peça dominada pela LARGURA...": aqui raioNoPlanoDeGiro = 5 é bem menor que meiaAlturaEmY = 200,
    // então a altura domina nas três proporções (a distância pela altura fica em 205 nas três,
    // sempre maior que a pela largura, que vai de ≈5,76 a ≈11,18 dependendo da proporção — conferido
    // à mão). `distanciaMinima = raio × 1,1 = 5,5` continua maior que o raio (5) nas três, e continua
    // o mesmo valor independente de qual termo domina — a fórmula não distingue os dois casos, e é
    // isso que a prova exige.
    const estreita = enquadramentoDoSolido(5, 200, 90, 0.5, 1)
    const quadrada = enquadramentoDoSolido(5, 200, 90, 1, 1)
    const larga = enquadramentoDoSolido(5, 200, 90, 1.75, 1)

    expect(estreita.distanciaMinima).toBeCloseTo(5.5, 9)
    expect(quadrada.distanciaMinima).toBeCloseTo(5.5, 9)
    expect(larga.distanciaMinima).toBeCloseTo(5.5, 9)
    expect(estreita.distanciaMinima).toBeGreaterThan(5)
    expect(quadrada.distanciaMinima).toBeGreaterThan(5)
    expect(larga.distanciaMinima).toBeGreaterThan(5)
  })

  it('reproduz o cenário real que atravessava a peça antes do conserto (bloco 6.000×400×300, proporção 1,75) e prova a câmera fora dele', () => {
    // Números medidos: bloco com meia-extensão X = 3.000, Z = 150 dá raioNoPlanoDeGiro =
    // √(3.000² + 150²) = 3.003,747659175118. Antes do conserto, o piso multiplicava a distância final
    // (≈5.463,58, dominada pela largura nesta proporção) por 0,5, dando minDistance ≈ 2.731,79 — MENOR
    // que o raio, então a câmera entrava na peça ao orbitar. `distanciaMinima = raio × 1,1 =
    // 3.304,122425092626` fica acima do raio, sem depender da distância final nem da proporção.
    const bloco = enquadramentoDoSolido(3003.747659175118, 200, 50, 1.75, 1.15)

    expect(bloco.distanciaMinima).toBeCloseTo(3304.122425092626, 6)
    expect(bloco.distanciaMinima).toBeGreaterThan(3003.747659175118)
  })

  it('escolhe a distância pela ALTURA quando ela é maior, num quadro estreito', () => {
    // Complementa 'escolhe a distância pela LARGURA...': lá a largura domina, aqui é a altura, e
    // este é o que prova que a fórmula soma `+ raioNoPlanoDeGiro` na distância pela altura (a borda
    // mais próxima da câmera fica a `distância − raio` dela, não no centro de giro) — sem a soma o
    // resultado seria 50, não 60. Com abertura de 90° (meia abertura vertical 45°, `tan(45°) = 1`),
    // a distância pela altura fica `meiaAltura / 1 + raio = meiaAltura + raio`, exata a menos do
    // erro de ponto flutuante de `Math.PI`: para `raio = 10` e `meiaAltura = 50`, o valor é `60`. A
    // distância pela largura (proporção 0,5 estreita o quadro) fica bem menor, então o "max" escolhe
    // a altura.
    const resultado = enquadramentoDoSolido(10, 50, 90, 0.5, 1)

    expect(resultado.distancia).toBeCloseTo(60, 8)
  })
})
