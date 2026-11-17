// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'
import { ErroDeApi } from '../api/erros'
import { pecaDeTeste } from './pecaDeTeste'

let estadoAtual: EstadoSessao = { status: 'anonimo' }
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))
const registrarEmbalagem = vi.fn()
vi.mock('../api/panorama', () => ({ registrarEmbalagem: (...a: unknown[]) => registrarEmbalagem(...a) }))

const { CelulaDeEmbalagem } = await import('./CelulaDeEmbalagem')

const como = (perfil: string) => {
  estadoAtual = { status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
}
const ultima = { tipo: 'Marcar', dataHora: '2026-11-17T14:30:00-03:00', usuario: 'Maria' }

beforeEach(() => { registrarEmbalagem.mockReset(); como('Qualidade') })
afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

describe('CelulaDeEmbalagem', () => {
  it('caixa travada com "faltam 3 em produção" quando não pode marcar', () => {
    render(<CelulaDeEmbalagem aoGravar={() => {}}
      peca={pecaDeTeste({ embalagem: { vale: false, podeMarcar: false, emProducao: 3, ultima: null } })} />)

    expect((screen.getByRole('checkbox', { name: /Embalado/ }) as HTMLInputElement).disabled).toBe(true)
    expect(screen.getByText('faltam 3 em produção')).toBeTruthy()
  })

  it('marcada mostra quem e quando', () => {
    render(<CelulaDeEmbalagem aoGravar={() => {}}
      peca={pecaDeTeste({ embalagem: { vale: true, podeMarcar: true, emProducao: 0, ultima } })} />)

    expect((screen.getByRole('checkbox', { name: /Embalado/ }) as HTMLInputElement).checked).toBe(true)
    expect(screen.getByText('Maria · 17/11/2026 14:30')).toBeTruthy()
  })

  it('marcar chama a API uma vez com duplo clique e recarrega', async () => {
    registrarEmbalagem.mockImplementation(() => new Promise((r) => setTimeout(r, 20)))
    const aoGravar = vi.fn()
    render(<CelulaDeEmbalagem aoGravar={aoGravar}
      peca={pecaDeTeste({ embalagem: { vale: false, podeMarcar: true, emProducao: 0, ultima: null } })} />)

    const caixa = screen.getByRole('checkbox', { name: /Embalado/ })
    fireEvent.click(caixa)
    fireEvent.click(caixa)

    await waitFor(() => expect(aoGravar).toHaveBeenCalledTimes(1))
    expect(registrarEmbalagem).toHaveBeenCalledTimes(1)
    expect(registrarEmbalagem).toHaveBeenCalledWith(7, true)
  })

  it('desmarcar manda embalado falso', async () => {
    registrarEmbalagem.mockResolvedValue(undefined)
    render(<CelulaDeEmbalagem aoGravar={() => {}}
      peca={pecaDeTeste({ embalagem: { vale: true, podeMarcar: true, emProducao: 0, ultima } })} />)

    fireEvent.click(screen.getByRole('checkbox', { name: /Embalado/ }))

    await waitFor(() => expect(registrarEmbalagem).toHaveBeenCalledWith(7, false))
  })

  it('a recusa mostra a mensagem ao lado da caixa e não recarrega', async () => {
    registrarEmbalagem.mockRejectedValue(new ErroDeApi(409, 'Falha ao marcar como embalado (409).', 'Faltam 3 em produção.', 'Recusado'))
    const aoGravar = vi.fn()
    render(<CelulaDeEmbalagem aoGravar={aoGravar}
      peca={pecaDeTeste({ embalagem: { vale: false, podeMarcar: true, emProducao: 0, ultima: null } })} />)

    fireEvent.click(screen.getByRole('checkbox', { name: /Embalado/ }))

    expect((await screen.findByRole('alert')).textContent).toContain('Faltam 3 em produção.')
    expect(aoGravar).not.toHaveBeenCalled()
    expect(screen.getByRole('checkbox', { name: /Embalado/ })).toBeTruthy()
  })

  it('a Gestão vê a pílula e a data, sem caixa', () => {
    como('Gestao')
    render(<CelulaDeEmbalagem aoGravar={() => {}}
      peca={pecaDeTeste({ embalagem: { vale: true, podeMarcar: true, emProducao: 0, ultima } })} />)

    expect(screen.getByText('Embalado')).toBeTruthy()
    expect(screen.getByText('17/11/2026')).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
  })

  it('a Gestão não vê nada quando não vale', () => {
    como('Gestao')
    const { container } = render(<CelulaDeEmbalagem aoGravar={() => {}} peca={pecaDeTeste()} />)

    expect(container.textContent).toBe('')
  })
})
