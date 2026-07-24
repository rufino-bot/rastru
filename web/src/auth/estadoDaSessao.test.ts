import { describe, expect, it } from 'vitest'
import { estadoDaSessao } from './estadoDaSessao'

describe('estadoDaSessao — mapeamento do init-refresh', () => {
  it('usuario presente -> autenticado', () => {
    const u = { usuarioId: 1, nomeUsuario: 'admin', nomeCompleto: 'Admin', perfil: 'Administrador' }
    expect(estadoDaSessao(u)).toEqual({ status: 'autenticado', usuario: u })
  })

  it('null -> anonimo SEM motivo (boot sem sessao nao e "expirada")', () => {
    expect(estadoDaSessao(null)).toEqual({ status: 'anonimo' })
  })
})
