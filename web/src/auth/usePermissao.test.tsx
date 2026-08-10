// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { renderHook, cleanup } from '@testing-library/react'
import type { EstadoSessao } from './estadoDaSessao'

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('./AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { usePodeEscrever } = await import('./usePermissao')

describe('usePodeEscrever', () => {
  it('libera quando o perfil da sessão pode escrever no recurso', () => {
    estadoAtual = {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    }

    expect(renderHook(() => usePodeEscrever('pedidos')).result.current).toBe(true)
  })

  it('nega quando o perfil da sessão não pode escrever naquele recurso', () => {
    // O MESMO usuário do caso acima, em OUTRO recurso — é o par que impede `return true` de passar.
    estadoAtual = {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    }

    expect(renderHook(() => usePodeEscrever('setores')).result.current).toBe(false)
  })

  it('nega quando ninguém entrou (sem montar sessão)', () => {
    // Não atribui `estadoAtual` — depende só do reset do `afterEach`. Vem logo depois de um caso
    // que autenticou como PCP (acima), que escreve em `componentes`: sem o reset, este caso herdaria
    // aquela sessão e responderia `true`.
    expect(renderHook(() => usePodeEscrever('componentes')).result.current).toBe(false)
  })

  it('nega em sessão não autenticada, sem estourar', () => {
    // `estado.usuario` não existe neste ramo da união. Sem a guarda de status, isto não devolve
    // `false`: lança TypeError ao ler `.perfil` de `undefined`.
    estadoAtual = { status: 'anonimo' }

    expect(renderHook(() => usePodeEscrever('pedidos')).result.current).toBe(false)
  })
})
