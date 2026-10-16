// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaSeparacao } = await import('./usePermissoesDaSeparacao')

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaSeparacao()).result.current
}

describe('usePermissoesDaSeparacao', () => {
  it('o Separador estorna só a própria separação', () => {
    const p = como('Separador', 12)

    expect(p.podeEstornarSeparacao(12)).toBe(true)
    expect(p.podeEstornarSeparacao(13)).toBe(false)
  })

  it('PCP e Administrador estornam separação de qualquer autor', () => {
    expect(como('PCP', 2).podeEstornarSeparacao(13)).toBe(true)
    expect(como('Administrador', 1).podeEstornarSeparacao(13)).toBe(true)
  })

  it('perfil fora da chave `estornoDeSeparacao` não estorna nem o que seria "dele"', () => {
    for (const perfil of ['Operador', 'Movimentador', 'Qualidade', 'Gestao']) {
      expect(como(perfil, 12).podeEstornarSeparacao(12), perfil).toBe(false)
    }
  })

  it('sessão anônima não pode nada', () => {
    estadoAtual = { status: 'anonimo' }
    const p = renderHook(() => usePermissoesDaSeparacao()).result.current

    expect([p.separar, p.editarMateriais, p.podeEstornarSeparacao(1)]).toEqual([false, false, false])
  })

  it.each([
    ['Separador', { separar: true, editarMateriais: false }],
    ['PCP', { separar: false, editarMateriais: true }],
    ['Administrador', { separar: true, editarMateriais: true }],
    ['Operador', { separar: false, editarMateriais: false }],
    ['Movimentador', { separar: false, editarMateriais: false }],
    ['Qualidade', { separar: false, editarMateriais: false }],
    ['Gestao', { separar: false, editarMateriais: false }],
  ])('%s: separar e editar materiais são de quem os faz', (perfil, esperado) => {
    const { separar, editarMateriais } = como(perfil)

    expect({ separar, editarMateriais }).toEqual(esperado)
  })
})
