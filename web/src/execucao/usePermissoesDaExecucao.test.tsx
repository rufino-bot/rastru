// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaExecucao } = await import('./usePermissoesDaExecucao')

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaExecucao()).result.current
}

describe('usePermissoesDaExecucao', () => {
  it('o autor estorna o próprio registro', () => {
    expect(como('Operador', 12).podeEstornar(12)).toBe(true)
  })

  it('quem não é autor nem PCP/Administrador não estorna', () => {
    expect(como('Operador', 12).podeEstornar(13)).toBe(false)
    expect(como('Movimentador', 12).podeEstornar(13)).toBe(false)
  })

  it('PCP e Administrador estornam registro alheio', () => {
    expect(como('PCP', 2).podeEstornar(13)).toBe(true)
    expect(como('Administrador', 1).podeEstornar(13)).toBe(true)
  })

  it('sessão anônima não pode nada', () => {
    estadoAtual = { status: 'anonimo' }
    const p = renderHook(() => usePermissoesDaExecucao()).result.current

    expect([p.apontar, p.entregar, p.editarRoteiro, p.podeEstornar(1)]).toEqual([false, false, false, false])
  })
  it.each([
    ['Operador', { apontar: true, entregar: false, editarRoteiro: false }],
    ['Movimentador', { apontar: false, entregar: true, editarRoteiro: false }],
    ['PCP', { apontar: false, entregar: false, editarRoteiro: true }],
    ['Administrador', { apontar: true, entregar: true, editarRoteiro: true }],
    ['Almoxarifado', { apontar: false, entregar: false, editarRoteiro: false }],
    ['Qualidade', { apontar: false, entregar: false, editarRoteiro: false }],
    ['Gestao', { apontar: false, entregar: false, editarRoteiro: false }],
  ])('%s: cada ação é de quem a faz (spec §4.8)', (perfil, esperado) => {
    const { apontar, entregar, editarRoteiro } = como(perfil)

    expect({ apontar, entregar, editarRoteiro }).toEqual(esperado)
  })

  it('perfil fora da chave `estorno` não estorna nem o que seria "dele"', () => {
    // Gestão só lê (spec §4.8): mesmo com o Id da sessão igual ao autor, o botão não aparece.
    expect(como('Gestao', 12).podeEstornar(12)).toBe(false)
    expect(como('Qualidade', 12).podeEstornar(12)).toBe(false)
  })
})
