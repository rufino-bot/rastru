// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { setorLembrado, lembrarSetor, esquecerSetor } from './setorLembrado'

afterEach(() => { vi.restoreAllMocks(); localStorage.clear() })

describe('setorLembrado', () => {
  it('lembra e esquece', () => {
    expect(setorLembrado()).toBeNull()
    lembrarSetor(3)
    expect(setorLembrado()).toBe(3)
    esquecerSetor()
    expect(setorLembrado()).toBeNull()
  })

  it('ignora o que não é um Id', () => {
    localStorage.setItem('rastru.fila.setorId', 'abc')
    expect(setorLembrado()).toBeNull()
    localStorage.setItem('rastru.fila.setorId', '-2')
    expect(setorLembrado()).toBeNull()
  })

  it('armazenamento que lança não derruba ninguém', () => {
    // Navegação privada e política do navegador fazem o acesso lançar.
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('SecurityError') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError') })
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('SecurityError') })

    expect(setorLembrado()).toBeNull()
    expect(() => lembrarSetor(3)).not.toThrow()
    expect(() => esquecerSetor()).not.toThrow()
  })
})
