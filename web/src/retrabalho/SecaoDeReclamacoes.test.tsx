// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SecaoDeReclamacoes } from './SecaoDeReclamacoes'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { ReclamacaoDto } from '../api/retrabalho'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'PCP'
let usuarioId = 1
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: usuarioId, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {}, logout: async () => {},
  }),
}))

function reclamacao(p: Partial<ReclamacaoDto> = {}): ReclamacaoDto {
  return {
    id: 3, pecaId: 812, descricao: 'Eixo', codigo: 'EX-1', quantidade: 2, texto: 'veio torto',
    dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê', estornada: false, estornavel: true,
    pedidoRetrabalho: null, dispensa: null, aRepor: 2, ...p,
  }
}

function montar(carregar: () => Promise<ReclamacaoDto[]>, aoMudar = vi.fn(), versao = 0) {
  render(<MemoryRouter><SecaoDeReclamacoes carregar={carregar} versao={versao} aoMudar={aoMudar} /></MemoryRouter>)
  return aoMudar
}

describe('SecaoDeReclamacoes', () => {
  beforeEach(() => {
    perfil = 'PCP'; usuarioId = 1
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes da resposta', () => {
    montar(() => new Promise(() => {}))
    expect(screen.getByText('Carregando…')).toBeTruthy()
  })

  it('mostra "Nenhuma reclamação" quando a lista vem vazia', async () => {
    montar(async () => [])
    expect(await screen.findByText('Nenhuma reclamação')).toBeTruthy()
  })

  it('mostra o erro de carga com "Tentar de novo", que carrega outra vez', async () => {
    const carregar = vi.fn()
      .mockRejectedValueOnce(new Error('rede'))
      .mockResolvedValueOnce([])
    montar(carregar)
    expect(await screen.findByText(/Não foi possível carregar as reclamações/)).toBeTruthy()
    expect(screen.queryByText('Nenhuma reclamação')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(await screen.findByText('Nenhuma reclamação')).toBeTruthy()
  })

  it('mostra a Peça, a quantidade, o texto do cliente e o autor; a linha a repor tem "A repor"', async () => {
    montar(async () => [reclamacao()])
    expect(await screen.findByText(/Eixo/)).toBeTruthy()
    expect(screen.getByText('veio torto')).toBeTruthy()
    expect(screen.getByText(/Quê ·/)).toBeTruthy()
    expect(screen.getByText('A repor').className).toMatch(/atencao/)
  })

  it('a reclamação estornada não oferece "Não repor" nem "Estornar"', async () => {
    montar(async () => [reclamacao({ estornada: true, estornavel: false, aRepor: 0 })])
    await screen.findByText('veio torto')
    expect(screen.getByText('Estornada').className).not.toMatch(/negativo|positivo|atencao|atraso/)
    expect(screen.queryByRole('button', { name: /Não repor|Estornar/ })).toBeNull()
  })

  it('o "Estornar" aparece só com estornavel e permissão, e recarrega a seção e o cabeçalho', async () => {
    const carregar = vi.fn(async () => [reclamacao()])
    const fetchMock = fetchPorRota({ '/api/reclamacoes/3/estorno': () => respostaJson(reclamacao({ estornada: true })) })
    vi.stubGlobal('fetch', fetchMock)
    const aoMudar = montar(carregar)
    fireEvent.click(await screen.findByRole('button', { name: /Estornar/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar' }))
    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(carregar.mock.calls.length).toBeGreaterThanOrEqual(2)
  })

  it('a Qualidade que não é a autora não vê o "Estornar"', async () => {
    perfil = 'Qualidade'; usuarioId = 99
    montar(async () => [reclamacao()])
    await screen.findByText('veio torto')
    expect(screen.queryByRole('button', { name: /Estornar/ })).toBeNull()
  })
})
