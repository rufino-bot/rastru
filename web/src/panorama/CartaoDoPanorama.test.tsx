// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { EstadoSessao } from '../auth/estadoDaSessao'
import { pecaDeTeste, seg } from './pecaDeTeste'

const estadoAtual: EstadoSessao = {
  status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil: 'Qualidade' },
}
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { CartaoDoPanorama } = await import('./CartaoDoPanorama')

afterEach(cleanup)

describe('CartaoDoPanorama', () => {
  it('traz os mesmos dados da linha', () => {
    const base = pecaDeTeste({
      distribuicao: [seg('Passo', 3, { setorNome: 'Solda', ordem: 1 })],
      afericao: { exigeRelatorio: true, situacao: 'Conforme' },
      embalagem: { vale: false, podeMarcar: false, emProducao: 3, ultima: null },
    })
    render(
      <MemoryRouter>
        <ul>
          <CartaoDoPanorama aoGravar={() => {}} peca={{
            ...base, pedido: { ...base.pedido, atrasado: true, pausa: { desde: 'x', porUsuarioNome: 'u', motivo: 'projeto' } },
          }} />
        </ul>
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'SUP-01' }).getAttribute('href')).toBe('/agrupamentos/4')
    expect(screen.getByText('Suporte lateral')).toBeTruthy()
    expect(screen.getByText('Pedido 293 · Qtd 5')).toBeTruthy()
    expect(screen.getByText('20/11/2026')).toBeTruthy()
    expect(screen.getByText('Atrasado')).toBeTruthy()
    expect(screen.getByText('Pausado')).toBeTruthy()
    expect(screen.getByText('projeto').getAttribute('title')).toBe('projeto')
    expect(screen.getByText('3 Solda')).toBeTruthy()
    expect(screen.getByText('Conforme')).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Desenho de SUP-01' })).toBeTruthy()
    expect(screen.getByText('faltam 3 em produção')).toBeTruthy()
  })
})
