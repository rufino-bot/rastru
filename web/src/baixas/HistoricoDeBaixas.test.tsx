// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { HistoricoDeBaixas } from './HistoricoDeBaixas'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { BaixaDto } from '../api/baixas'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'Qualidade'
let usuarioId = 5
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: usuarioId, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

function baixa(parcial: Partial<BaixaDto> = {}): BaixaDto {
  return {
    id: 1, motivo: 'MortaEmProcesso', observacao: null, escopo: { tipo: 'No', id: 7 }, tudo: false,
    dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 5, usuarioNome: 'Quê', estornada: false, estornavel: true,
    pedidoRetrabalho: null, dispensa: null, aRepor: 0,
    linhas: [
      { estruturaItemId: 7, descricao: 'Eixo', codigo: 'EX-1', origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 3 },
      { estruturaItemId: 7, descricao: 'Eixo', codigo: 'EX-1', origemPosicao: 'NoSetor', origemSetorId: 1, origemSetorNome: 'Usinagem', origemOrdem: 2, quantidade: 1 },
    ],
    ...parcial,
  }
}

function montar(carregar: () => Promise<BaixaDto[]>, aoEstornar = vi.fn(), versao = 0) {
  return render(<HistoricoDeBaixas carregar={carregar} versao={versao} aoEstornar={aoEstornar} />)
}

describe('HistoricoDeBaixas', () => {
  beforeEach(() => {
    perfil = 'Qualidade'; usuarioId = 5
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra o indicador de carregando antes da resposta', () => {
    montar(() => new Promise(() => {}))
    expect(screen.getByText('Carregando…')).toBeTruthy()
  })

  it('mostra "Nenhuma baixa." quando a lista vem vazia', async () => {
    montar(async () => [])
    expect(await screen.findByText('Nenhuma baixa.')).toBeTruthy()
  })

  it('mostra o erro de carga com a frase de falha', async () => {
    montar(async () => { throw new Error('rede') })
    expect(await screen.findByText(/Não foi possível carregar as baixas/)).toBeTruthy()
    expect(screen.queryByText('Nenhuma baixa.')).toBeNull()
  })

  it('resume a baixa: motivo, quantidade com a origem, autor e a pílula neutra Estornada', async () => {
    montar(async () => [baixa({ estornada: true, estornavel: false, observacao: 'caiu da prateleira' })])
    expect(await screen.findByText('Morreu num processo')).toBeTruthy()
    expect(screen.getByText('4: 3 a iniciar, 1 em Usinagem (passo 2)')).toBeTruthy()
    expect(screen.getByText('caiu da prateleira')).toBeTruthy()
    expect(screen.getByText(/Quê ·/)).toBeTruthy()
    const pilula = screen.getByText('Estornada')
    expect(pilula.className).not.toMatch(/negativo|positivo|atencao|atraso/)
    expect(screen.queryByRole('button', { name: /Estornar/ })).toBeNull()
  })

  it('a baixa de vários nós nomeia cada um', async () => {
    const b = baixa({
      linhas: [
        baixa().linhas[0],
        { estruturaItemId: 8, descricao: 'Chapa dobrada', codigo: null, origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 6 },
      ],
    })
    montar(async () => [b])
    expect(await screen.findByText('3 de Eixo: 3 a iniciar; e 6 de Chapa dobrada: 6 a iniciar')).toBeTruthy()
  })

  it('diz o escopo do descarte de um Pedido, com o nome que a tela conhece e, sem ele, o Id', async () => {
    const b = baixa({ motivo: 'Descarte', tudo: true, escopo: { tipo: 'Pedido', id: 9 } })
    const { unmount } = render(
      <HistoricoDeBaixas carregar={async () => [b]} versao={0} aoEstornar={() => {}} nomeDoEscopo={() => 'P-2291'} />,
    )
    expect(await screen.findByText('Descartar tudo do Pedido P-2291')).toBeTruthy()
    unmount()
    montar(async () => [b])
    expect(await screen.findByText('Descartar tudo do Pedido (Id 9)')).toBeTruthy()
  })

  it('no painel de um nó, a baixa de outro nó (um filho) nomeia o filho; a do próprio nó não', async () => {
    const doFilho = baixa({ id: 2, linhas: [{ ...baixa().linhas[0], estruturaItemId: 8, descricao: 'Parafuso' }] })
    render(<HistoricoDeBaixas carregar={async () => [doFilho, baixa()]} versao={0} aoEstornar={() => {}} noId={7} />)
    expect(await screen.findByText('3 de Parafuso: 3 a iniciar')).toBeTruthy()
    expect(screen.getByText('4: 3 a iniciar, 1 em Usinagem (passo 2)')).toBeTruthy()
  })

  it('não diz escopo para a baixa de um nó', async () => {
    montar(async () => [baixa()])
    await screen.findByText('Morreu num processo')
    expect(screen.queryByText(/Descartar tudo/)).toBeNull()
  })

  it('só oferece Estornar a quem pode: o autor sim, outra pessoa da Qualidade não, o PCP sim', async () => {
    usuarioId = 99
    const { unmount } = montar(async () => [baixa()])
    await screen.findByText('Morreu num processo')
    expect(screen.queryByRole('button', { name: /Estornar/ })).toBeNull()
    unmount()
    perfil = 'PCP'
    montar(async () => [baixa()])
    expect(await screen.findByRole('button', { name: /Estornar a baixa/ })).toBeTruthy()
  })

  it('o Operador não vê Estornar', async () => {
    perfil = 'Operador'
    montar(async () => [baixa()])
    await screen.findByText('Morreu num processo')
    expect(screen.queryByRole('button', { name: /Estornar/ })).toBeNull()
  })

  it('Estornar confirma, chama estornarBaixa, recarrega e avisa a tela', async () => {
    const aoEstornar = vi.fn()
    let estornada = false
    const carregar = vi.fn(async () => [baixa({ estornada, estornavel: !estornada })])
    const fetchMock = fetchPorRota({
      '/api/baixas/1/estorno': () => { estornada = true; return respostaJson([]) },
    })
    vi.stubGlobal('fetch', fetchMock)
    montar(carregar, aoEstornar)
    fireEvent.click(await screen.findByRole('button', { name: /Estornar a baixa/ }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Estornar' }))
    await waitFor(() => expect(aoEstornar).toHaveBeenCalledTimes(1))
    expect(fetchMock.mock.calls.filter((c) => String(c[0]) === '/api/baixas/1/estorno')).toHaveLength(1)
    expect(carregar).toHaveBeenCalledTimes(2)
    expect(await screen.findByText('Estornada')).toBeTruthy()
    // O botão saiu com o estorno: o foco vai ao título da seção, não ao <body>.
    await waitFor(() => expect(document.activeElement).toBe(screen.getByText('Baixas')))
  })

  it('409 JaEstornado mostra a frase do servidor e recarrega a lista', async () => {
    const aoEstornar = vi.fn()
    let estornada = false
    const carregar = vi.fn(async () => [baixa({ estornada, estornavel: !estornada })])
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/baixas/1/estorno': () => {
        estornada = true
        return respostaJson({ erro: 'JaEstornado', mensagem: 'Esta baixa já foi estornada.' }, 409)
      },
    }))
    montar(carregar, aoEstornar)
    fireEvent.click(await screen.findByRole('button', { name: /Estornar a baixa/ }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Estornar' }))
    expect(await screen.findByText('Esta baixa já foi estornada.')).toBeTruthy()
    expect(carregar).toHaveBeenCalledTimes(2)
    expect(aoEstornar).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Estornada')).toBeTruthy()
  })

  it('recarrega quando a versão muda', async () => {
    const carregar = vi.fn(async () => [baixa()])
    const { rerender } = montar(carregar)
    await screen.findByText('Morreu num processo')
    rerender(<HistoricoDeBaixas carregar={carregar} versao={1} aoEstornar={() => {}} />)
    await waitFor(() => expect(carregar).toHaveBeenCalledTimes(2))
  })
})
