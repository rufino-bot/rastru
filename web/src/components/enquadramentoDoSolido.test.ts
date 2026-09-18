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
    // `distanciaMinima` também depende do piso, e da ORDEM em que ele entra: aplicado a raio e meia
    // altura ANTES da raiz (o que o código faz), dá `√(1² + 1²) × 1,1 = 1,5556349186104048`.
    // Aplicado DEPOIS, sobre a esfera já calculada com as dimensões cruas (`√(0² + 0²) = 0`, e só
    // então o piso levaria a `1 × 1,1 = 1,1`), o resultado seria quase 30% menor — é essa ordem que
    // este valor exato prova.
    expect(resultado.distanciaMinima).toBeCloseTo(1.5556349186104048, 12)
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

  it('mantém distanciaMinima estritamente maior que a esfera do volume para peça DEITADA (raio bem maior que a meia altura), em duas proporções bem separadas', () => {
    // Um piso de zoom de `raio × 1,1`, sem a meia altura, só protegeria contra a
    // câmera entrar no CILINDRO que a peça varre girando ao redor do eixo Y — e o `OrbitControls`
    // deixa `minPolarAngle = 0` / `maxPolarAngle = Math.PI` (conferido no fonte de
    // `OrbitControls.js`), então o usuário orbita também até a vista de cima ou de baixo, onde o que
    // precisa ficar fora é a ESFERA que envolve o volume (`√(raio² + meiaAltura²)`), não o cilindro.
    // Números redondos com raiz exata (triângulo 500-1200-1300, escala de 5-12-13): raio = 1.200,
    // meiaAltura = 500 → esfera = 1.300 (exata), `distanciaMinima = 1.300 × 1,1 = 1.430`. Independe
    // da proporção do quadro porque não entra na fórmula — as duas proporções (0,5 estreita e 1,75
    // larga) produzem o MESMO valor.
    const estreita = enquadramentoDoSolido(1200, 500, 90, 0.5, 1)
    const larga = enquadramentoDoSolido(1200, 500, 90, 1.75, 1)

    expect(estreita.distanciaMinima).toBeCloseTo(1430, 9)
    expect(larga.distanciaMinima).toBeCloseTo(1430, 9)
    expect(estreita.distanciaMinima).toBeGreaterThan(1300)
    expect(larga.distanciaMinima).toBeGreaterThan(1300)
  })

  it('mantém distanciaMinima estritamente maior que a esfera do volume para peça EM PÉ (meia altura bem maior que o raio), nas mesmas duas proporções', () => {
    // Complementa a peça DEITADA: mesmo triângulo 500-1200-1300, com os papéis trocados — raio =
    // 500, meiaAltura = 1.200 → a mesma esfera = 1.300 e a mesma `distanciaMinima = 1.430`. É
    // exatamente este o caso que um piso de `raio × 1,1` deixaria passar: uma peça comprida
    // modelada ao longo do eixo Y do CAD chega em pé no viewer, com raio no plano de giro pequeno e
    // meia altura grande — esse piso (`raio × 1,1 = 550`) ficaria bem MENOR que a própria peça
    // (esfera 1.300), deixando a câmera atravessá-la ao orbitar por cima.
    const estreita = enquadramentoDoSolido(500, 1200, 90, 0.5, 1)
    const larga = enquadramentoDoSolido(500, 1200, 90, 1.75, 1)

    expect(estreita.distanciaMinima).toBeCloseTo(1430, 9)
    expect(larga.distanciaMinima).toBeCloseTo(1430, 9)
    expect(estreita.distanciaMinima).toBeGreaterThan(1300)
    expect(larga.distanciaMinima).toBeGreaterThan(1300)
  })

  it('mantém distanciaMinima estritamente maior que a esfera do volume para peça QUASE CÚBICA (raio e meia altura próximos), nas mesmas duas proporções', () => {
    // Terceiro ponto da faixa, entre a peça DEITADA e a EM PÉ: nem a largura nem a altura dominam.
    // Prova que o invariante não depende de uma das duas dimensões dominar a outra. Triângulo
    // 20-21-29 (raio = 20, meiaAltura = 21 → esfera = 29, exata): `distanciaMinima = 29 × 1,1 = 31,9`.
    const estreita = enquadramentoDoSolido(20, 21, 90, 0.5, 1)
    const larga = enquadramentoDoSolido(20, 21, 90, 1.75, 1)

    expect(estreita.distanciaMinima).toBeCloseTo(31.9, 9)
    expect(larga.distanciaMinima).toBeCloseTo(31.9, 9)
    expect(estreita.distanciaMinima).toBeGreaterThan(29)
    expect(larga.distanciaMinima).toBeGreaterThan(29)
  })

  it('reproduz o bloco 6.000×400×300 em que um piso de metade da distância deixava a câmera entrar na peça (proporção 1,75), e prova a câmera fora da esfera do volume', () => {
    // Números medidos: bloco com meia-extensão X = 3.000, Z = 150 dá raioNoPlanoDeGiro =
    // √(3.000² + 150²) = 3.003,747659175118, e meia-extensão Y = 200 (meiaAlturaEmY) — a peça está
    // DEITADA, raio bem maior que a meia altura. Um piso que multiplicasse a distância final
    // (≈5.463,58, dominada pela largura nesta proporção) por 0,5 daria minDistance ≈ 2.731,79 —
    // MENOR que o raio, e a câmera entraria na peça ao orbitar. Um piso de `raio × 1,1 ≈ 3.304,12`
    // fecharia este caso específico, mas não o da peça EM PÉ — ver os testes "mantém
    // distanciaMinima estritamente maior que a esfera do volume para peça...". A
    // esfera do volume aqui é `√(3.003,747659175118² + 200²) = 3.010,398644698074` (só um pouco maior
    // que o raio isolado, porque a meia altura é pequena perto dele), e `distanciaMinima =
    // 3.010,398644698074 × 1,1 = 3.311,4385091678814` fica acima do raio E da esfera.
    const bloco = enquadramentoDoSolido(3003.747659175118, 200, 50, 1.75, 1.15)

    expect(bloco.distanciaMinima).toBeCloseTo(3311.4385091678814, 6)
    expect(bloco.distanciaMinima).toBeGreaterThan(3010.398644698074)
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
