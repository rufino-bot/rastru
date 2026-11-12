// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePermissoesDaReposicao } = await import('./usePermissoesDaReposicao')

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
  return renderHook(() => usePermissoesDaReposicao()).result.current
}

describe('usePermissoesDaReposicao', () => {
  it.each([
    ['Qualidade', true, false, true, false],
    ['PCP', true, true, true, true],
    ['Administrador', true, true, true, true],
    ['Operador', false, false, false, false],
    ['Movimentador', false, false, false, false],
    ['Separador', false, false, false, false],
    ['Gestao', false, false, false, false],
  ])('%s: abrir %s, reaproveitar %s, reclamar %s, naoRepor %s', (perfil, abrir, reaproveitar, reclamar, naoRepor) => {
    const p = como(perfil)

    expect([p.abrir, p.reaproveitar, p.reclamar, p.naoRepor]).toEqual([abrir, reaproveitar, reclamar, naoRepor])
  })

  it('o autor estorna a própria reclamação', () => {
    expect(como('Qualidade', 12).podeEstornarReclamacao(12)).toBe(true)
  })

  it('a Qualidade não estorna a reclamação de outra pessoa', () => {
    expect(como('Qualidade', 12).podeEstornarReclamacao(13)).toBe(false)
  })

  it('PCP e Administrador estornam reclamação alheia', () => {
    expect(como('PCP', 2).podeEstornarReclamacao(13)).toBe(true)
    expect(como('Administrador', 1).podeEstornarReclamacao(13)).toBe(true)
  })

  it('perfil sem a chave `reclamacao` não estorna nem a que seria "dele"', () => {
    expect(como('Operador', 12).podeEstornarReclamacao(12)).toBe(false)
    expect(como('Gestao', 12).podeEstornarReclamacao(12)).toBe(false)
  })

  it('sessão anônima não pode nada', () => {
    estadoAtual = { status: 'anonimo' }
    const p = renderHook(() => usePermissoesDaReposicao()).result.current

    expect([p.abrir, p.reaproveitar, p.reclamar, p.naoRepor, p.podeEstornarReclamacao(1)])
      .toEqual([false, false, false, false, false])
  })
})
