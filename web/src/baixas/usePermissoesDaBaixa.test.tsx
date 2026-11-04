// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaBaixa } = await import('./usePermissoesDaBaixa')

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaBaixa()).result.current
}

describe('usePermissoesDaBaixa', () => {
  it.each([
    ['Qualidade', true],
    ['PCP', true],
    ['Administrador', true],
    ['Operador', false],
    ['Movimentador', false],
    ['Separador', false],
    ['Gestao', false],
  ])('%s: darBaixa é %s', (perfil, esperado) => {
    expect(como(perfil).darBaixa).toBe(esperado)
  })

  it('o autor estorna a própria baixa', () => {
    expect(como('Qualidade', 12).podeEstornar(12)).toBe(true)
  })

  it('a Qualidade não estorna a baixa de outra pessoa', () => {
    expect(como('Qualidade', 12).podeEstornar(13)).toBe(false)
  })

  it('PCP e Administrador estornam baixa alheia', () => {
    expect(como('PCP', 2).podeEstornar(13)).toBe(true)
    expect(como('Administrador', 1).podeEstornar(13)).toBe(true)
  })

  it('perfil sem a chave `baixa` não estorna nem a que seria "dele"', () => {
    expect(como('Operador', 12).podeEstornar(12)).toBe(false)
    expect(como('Separador', 12).podeEstornar(12)).toBe(false)
  })

  it('sessão anônima não pode nada', () => {
    estadoAtual = { status: 'anonimo' }
    const p = renderHook(() => usePermissoesDaBaixa()).result.current

    expect([p.darBaixa, p.podeEstornar(1)]).toEqual([false, false])
  })
})
