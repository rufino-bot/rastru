// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { cameraIsometrica, desenharMiniatura } from './miniatura'

describe('cameraIsometrica', () => {
  it('olha de (1,1,1) a partir do centro', () => {
    const centro: [number, number, number] = [10, -4, 2]
    const { posicao } = cameraIsometrica(centro, 5)
    const d = posicao.map((v, i) => v - centro[i])
    expect(d[0]).toBeCloseTo(d[1], 9)
    expect(d[1]).toBeCloseTo(d[2], 9)
    expect(d[0]).toBeGreaterThan(0)
    expect(Math.hypot(...d)).toBeCloseTo(20, 9)
  })

  it('folga de 8 por cento sobre o raio', () => {
    expect(cameraIsometrica([0, 0, 0], 10).meiaAltura).toBeCloseTo(10.8, 9)
  })

  it('quadro 4 por 3', () => {
    const c = cameraIsometrica([0, 0, 0], 7)
    expect(c.meiaLargura / c.meiaAltura).toBeCloseTo(4 / 3, 9)
  })

  it('a janela de profundidade contem o solido inteiro', () => {
    const c = cameraIsometrica([0, 0, 0], 7)
    expect(c.perto).toBeGreaterThan(0)
    expect(c.perto).toBeLessThan(4 * 7 - 7)
    expect(c.longe).toBeGreaterThan(4 * 7 + 7)
  })
})

describe('desenharMiniatura', () => {
  it('devolve null quando o WebGL nao existe', async () => {
    expect(await desenharMiniatura(new ArrayBuffer(84))).toBeNull()
  })
})
