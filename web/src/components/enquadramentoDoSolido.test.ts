import { describe, it, expect } from 'vitest'
import { ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO, enquadramentoDoSolido } from './enquadramentoDoSolido'

describe('enquadramentoDoSolido', () => {
  it('mantém a distância proporcional ao raio da esfera envolvente', () => {
    // Sem isto, um STL grande (metros, em unidade de CAD) e um pequeno (milímetros) exigiriam
    // câmeras com fórmulas diferentes — a mesma fórmula linear tem de servir aos dois, só mudando
    // o raio de entrada.
    const pequeno = enquadramentoDoSolido(15, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)
    const grande = enquadramentoDoSolido(3000, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    expect(grande.distancia / pequeno.distancia).toBeCloseTo(3000 / 15, 10)
  })

  it('afasta a câmera quando a margem cresce, na mesma proporção da margem', () => {
    const semFolga = enquadramentoDoSolido(100, ABERTURA_VERTICAL_EM_GRAUS, 1)
    const comFolga = enquadramentoDoSolido(100, ABERTURA_VERTICAL_EM_GRAUS, 2)

    expect(comFolga.distancia / semFolga.distancia).toBeCloseTo(2, 10)
  })

  it('nunca produz distância zero, NaN nem near >= far para um sólido degenerado (raio 0)', () => {
    // STL com um único ponto, ou vazio, dá esfera envolvente de raio 0 — sem um piso, a fórmula
    // (raio / seno) daria distância 0 e a câmera nasceria dentro do próprio sólido inexistente.
    const resultado = enquadramentoDoSolido(0, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    expect(resultado.distancia).toBeGreaterThan(0)
    expect(Number.isNaN(resultado.distancia)).toBe(false)
    expect(Number.isNaN(resultado.near)).toBe(false)
    expect(Number.isNaN(resultado.far)).toBe(false)
    expect(resultado.near).toBeLessThan(resultado.far)
  })

  it('afasta near e far junto com o tamanho do sólido, na mesma proporção do raio', () => {
    // "Proporcionais ao tamanho": hoje `0.1`/`10000` fixos cortariam um sólido de vários metros e
    // perderiam precisão de profundidade num sólido de poucos milímetros — os dois têm de escalar
    // com o raio, não ficar presos a um par de números que só serve a um tamanho de peça.
    const pequeno = enquadramentoDoSolido(15, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)
    const grande = enquadramentoDoSolido(3000, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    expect(grande.near / pequeno.near).toBeCloseTo(3000 / 15, 10)
    expect(grande.far / pequeno.far).toBeCloseTo(3000 / 15, 10)
  })

  it('mantém near dentro do far também para um sólido grande', () => {
    const resultado = enquadramentoDoSolido(3000, ABERTURA_VERTICAL_EM_GRAUS, MARGEM_DE_ENQUADRAMENTO)

    expect(resultado.near).toBeGreaterThan(0)
    expect(resultado.near).toBeLessThan(resultado.far)
  })
})
