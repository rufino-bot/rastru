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
