// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
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

  describe('reposição', () => {
    const roteador = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>)

    it('a baixa a repor tem "A repor" em atencao e "Não repor" para o PCP', async () => {
      perfil = 'PCP'
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ aRepor: 2 })]} versao={0} aoEstornar={() => {}} />)
      expect((await screen.findByText('A repor')).className).toMatch(/atencao/)
      expect(screen.getByRole('button', { name: /Não repor/ })).toBeTruthy()
    })

    it('a Qualidade vê "A repor" mas não "Não repor"', async () => {
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ aRepor: 2 })]} versao={0} aoEstornar={() => {}} />)
      await screen.findByText('A repor')
      expect(screen.queryByRole('button', { name: /Não repor/ })).toBeNull()
    })

    it('a baixa reposta mostra "Reposta por" com link', async () => {
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ pedidoRetrabalho: { id: 51, numero: 'P-1-R1' } })]} versao={0} aoEstornar={() => {}} />)
      await screen.findByText(/Reposta por/)
      expect(screen.getByRole('link', { name: 'P-1-R1' }).getAttribute('href')).toBe('/pedidos/51')
    })

    it('a baixa Reaproveitado mostra "Foi para"', async () => {
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ motivo: 'Reaproveitado', pedidoRetrabalho: { id: 51, numero: 'P-1-R1' } })]} versao={0} aoEstornar={() => {}} />)
      await screen.findByText(/Foi para/)
      expect(screen.queryByText(/Reposta por/)).toBeNull()
    })

    it('a baixa dispensada mostra a justificativa e o "Desfazer"', async () => {
      perfil = 'PCP'
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ dispensa: { id: 9, justificativa: 'sem custo', dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 3, usuarioNome: 'Zé' } })]} versao={0} aoEstornar={() => {}} />)
      expect(await screen.findByText('Não repor: sem custo')).toBeTruthy()
      expect(screen.getByRole('button', { name: /Desfazer/ })).toBeTruthy()
    })

    it('depois do "Não repor", a lista recarrega, a tela é avisada e a pílula some', async () => {
      perfil = 'PCP'
      let dispensada = false
      const carregar = vi.fn(async () => [baixa({
        aRepor: dispensada ? 0 : 2,
        dispensa: dispensada ? { id: 9, justificativa: 'ok', dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 3, usuarioNome: 'Zé' } : null,
      })])
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/baixas/1/dispensa': () => { dispensada = true; return respostaJson({ id: 9 }, 201) },
      }))
      const aoMudar = vi.fn()
      roteador(<HistoricoDeBaixas carregar={carregar} versao={0} aoEstornar={aoMudar} />)
      fireEvent.click(await screen.findByRole('button', { name: /Não repor/ }))
      const form = screen.getByRole('form', { name: 'Não repor' })
      fireEvent.change(within(form).getByLabelText('Justificativa'), { target: { value: 'ok' } })
      fireEvent.submit(form)
      await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
      await waitFor(() => expect(screen.queryByText('A repor')).toBeNull())
      expect(screen.getByText('Não repor: ok')).toBeTruthy()
      expect(carregar).toHaveBeenCalledTimes(2)
    })

    it('o painel do "Não repor" abre na largura da linha, fora da coluna do texto', async () => {
      perfil = 'PCP'
      roteador(<HistoricoDeBaixas carregar={async () => [baixa({ aRepor: 2 })]} versao={0} aoEstornar={() => {}} />)
      fireEvent.click(await screen.findByRole('button', { name: /Não repor/ }))
      const form = screen.getByRole('form', { name: 'Não repor' })
      const item = form.closest('li')!
      const colunaDoTexto = screen.getByText('A repor').closest('div.min-w-0')!
      expect(colunaDoTexto).toBeTruthy()
      expect(colunaDoTexto.contains(form)).toBe(false)
      expect(item.contains(form)).toBe(true)
    })
  })
})
