// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaExpedicao } = await import('./usePermissoesDaExpedicao')

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaExpedicao()).result.current
}

describe('usePermissoesDaExpedicao', () => {
  it.each([
    ['Qualidade', { registrar: true, responder: false }],
    ['Gestao', { registrar: false, responder: true }],
    ['Administrador', { registrar: true, responder: true }],
    ['PCP', { registrar: false, responder: false }],
    ['Operador', { registrar: false, responder: false }],
    ['Movimentador', { registrar: false, responder: false }],
    ['Separador', { registrar: false, responder: false }],
  ])('%s: registrar é da Qualidade e responder é da Gestão', (perfil, esperado) => {
    const { registrar, responder } = como(perfil)

    expect({ registrar, responder }).toEqual(esperado)
  })

  it('o autor estorna o próprio registro, na Qualidade e na Gestão', () => {
    expect(como('Qualidade', 12).podeEstornar(12)).toBe(true)
    expect(como('Gestao', 12).podeEstornar(12)).toBe(true)
  })

  it('Qualidade e Gestão não estornam o registro de outra pessoa', () => {
    expect(como('Qualidade', 12).podeEstornar(13)).toBe(false)
    expect(como('Gestao', 12).podeEstornar(13)).toBe(false)
  })

  it('PCP e Administrador estornam registro alheio', () => {
    expect(como('PCP', 2).podeEstornar(13)).toBe(true)
    expect(como('Administrador', 1).podeEstornar(13)).toBe(true)
  })

  it('perfil fora da chave `estornoDaExpedicao` não estorna nem o que seria "dele"', () => {
    expect(como('Operador', 12).podeEstornar(12)).toBe(false)
    expect(como('Movimentador', 12).podeEstornar(12)).toBe(false)
    expect(como('Separador', 12).podeEstornar(12)).toBe(false)
  })

  it('sessão anônima não pode nada', () => {
    estadoAtual = { status: 'anonimo' }
    const p = renderHook(() => usePermissoesDaExpedicao()).result.current

    expect([p.registrar, p.responder, p.podeEstornar(1)]).toEqual([false, false, false])
  })
})
