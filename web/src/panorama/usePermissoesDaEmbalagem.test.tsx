// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaEmbalagem } = await import('./usePermissoesDaEmbalagem')

function como(perfil: string) {
  estadoAtual = { status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaEmbalagem()).result.current
}

describe('usePermissoesDaEmbalagem', () => {
  it.each([
    ['Qualidade', true], ['PCP', true], ['Administrador', true],
    ['Gestao', false], ['Operador', false], ['Movimentador', false], ['Separador', false],
  ])('%s: marcar = %s', (perfil, esperado) => {
    expect(como(perfil).marcar).toBe(esperado)
  })

  it('sessão anônima não marca', () => {
    estadoAtual = { status: 'anonimo' }
    expect(renderHook(() => usePermissoesDaEmbalagem()).result.current.marcar).toBe(false)
  })
})
