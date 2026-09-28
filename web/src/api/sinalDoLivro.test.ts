import { describe, it, expect, vi } from 'vitest'
import { aoMudarOLivro, avisarQueOLivroMudou } from './sinalDoLivro'

describe('sinalDoLivro', () => {
  it('avisa todo ouvinte inscrito', () => {
    const um = vi.fn()
    const outro = vi.fn()
    const pararUm = aoMudarOLivro(um)
    const pararOutro = aoMudarOLivro(outro)

    avisarQueOLivroMudou()

    expect(um).toHaveBeenCalledTimes(1)
    expect(outro).toHaveBeenCalledTimes(1)
    pararUm()
    pararOutro()
  })

  it('quem parou de escutar não é mais chamado', () => {
    const ouvinte = vi.fn()
    const parar = aoMudarOLivro(ouvinte)

    parar()
    avisarQueOLivroMudou()

    expect(ouvinte).not.toHaveBeenCalled()
  })

  it('um ouvinte que lança não impede os outros nem faz o aviso lançar', () => {
    // O aviso sai DEPOIS de a escrita ser gravada: se ele lançasse, `iniciar`/`terminar` rejeitariam
    // uma operação que o servidor aceitou, e o operador leria como recusa.
    const comDefeito = vi.fn(() => { throw new Error('ouvinte com defeito') })
    const depois = vi.fn()
    const pararDefeito = aoMudarOLivro(comDefeito)
    const pararDepois = aoMudarOLivro(depois)

    expect(() => avisarQueOLivroMudou()).not.toThrow()

    expect(depois).toHaveBeenCalledTimes(1)
    pararDefeito()
    pararDepois()
  })
})
