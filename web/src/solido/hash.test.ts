// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { sha256Hex } from './hash'

afterEach(() => vi.unstubAllGlobals())

describe('sha256Hex', () => {
  it('hash de "abc"', async () => {
    const dados = new TextEncoder().encode('abc').buffer as ArrayBuffer
    expect(await sha256Hex(dados)).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
  })

  it('sem crypto.subtle devolve null', async () => {
    vi.stubGlobal('crypto', {})
    expect(await sha256Hex(new ArrayBuffer(3))).toBeNull()
  })
})
