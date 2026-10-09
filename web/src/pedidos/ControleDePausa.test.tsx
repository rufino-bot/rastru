// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor } from '@testing-library/react'
import { ControleDePausa } from './ControleDePausa'
import { inicializar, _resetParaTeste } from '../api/client'
import type { PedidoDto } from '../api/cadastros'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const LIVRE: PedidoDto = {
  id: 1, numero: 'PED-2026-01', cliente: 'Alfa', tipo: 'Normal', status: 'EmProducao',
  dataAbertura: '2026-09-20T09:00:00-03:00', dataEntrega: '2026-10-22', atrasado: false,
  criadoPorUsuarioId: 1, pausa: null,
}
const PAUSADO: PedidoDto = {
  ...LIVRE,
  pausa: { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'urgente' },
}

const PAUSA_CRIADA = {
  id: 5, pedidoId: 1, pausadoEm: '2026-09-28T10:14:00-03:00', pausadoPorUsuarioId: 1, pausadoPorNome: 'PCP',
  motivo: null, retomadoEm: null, retomadoPorUsuarioId: null, retomadoPorNome: null,
}

function chamadasDePost(fetchMock: ReturnType<typeof fetchPorRota>) {
  return fetchMock.mock.calls.map(([url, init]) => [String(url), (init as RequestInit | undefined)?.method] as const)
}

describe('ControleDePausa', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('com o Pedido pausado, o aviso diz desde quando, por quem, por quê e o que a pausa faz', () => {
    render(<ControleDePausa pedido={PAUSADO} aoMudar={async () => {}} />)

    expect(screen.getByText(/Pausado desde 28\/09\/2026 10:14 por PCP — urgente\./)).toBeTruthy()
    expect(screen.getByText(/Nada dele começa até ser retomado; o que já está em trabalho continua\./)).toBeTruthy()
  })

  it('sem motivo, o aviso não traz o travessão', () => {
    render(<ControleDePausa
      pedido={{ ...PAUSADO, pausa: { ...PAUSADO.pausa!, motivo: null } }} aoMudar={async () => {}}
    />)

    const aviso = screen.getByText(/Pausado desde 28\/09\/2026 10:14 por PCP/)
    expect(aviso.textContent).not.toContain('—')
  })

  it('Pedido livre não mostra aviso nenhum', () => {
    render(<ControleDePausa pedido={LIVRE} aoMudar={async () => {}} />)

    expect(screen.queryByText(/Pausado desde/)).toBeNull()
  })

  it.each(['PCP', 'Gestao', 'Administrador'])('%s vê Pausar no Pedido livre e Retomar no pausado', (p) => {
    perfil = p

    const { unmount } = render(<ControleDePausa pedido={LIVRE} aoMudar={async () => {}} />)
    expect(screen.getByRole('button', { name: 'Pausar' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Retomar' })).toBeNull()
    unmount()

    render(<ControleDePausa pedido={PAUSADO} aoMudar={async () => {}} />)
    expect(screen.getByRole('button', { name: 'Retomar' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Pausar' })).toBeNull()
  })

  it.each(['Operador', 'Almoxarifado', 'Movimentador', 'Qualidade'])(
    '%s não vê Pausar nem Retomar, mas vê o aviso da pausa aberta',
    (p) => {
      perfil = p

      const { unmount } = render(<ControleDePausa pedido={LIVRE} aoMudar={async () => {}} />)
      expect(screen.queryByRole('button', { name: 'Pausar' })).toBeNull()
      unmount()

      render(<ControleDePausa pedido={PAUSADO} aoMudar={async () => {}} />)
      expect(screen.queryByRole('button', { name: 'Retomar' })).toBeNull()
      expect(screen.getByText(/Pausado desde 28\/09\/2026 10:14 por PCP/)).toBeTruthy()
    },
  )

  it('Pausar abre o motivo; confirmar envia o motivo e avisa o chamador', async () => {
    const fetchMock = fetchPorRota({ '/api/pedidos/1/pausas': () => respostaJson(PAUSA_CRIADA, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={LIVRE} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.change(screen.getByLabelText('Motivo (opcional)'), { target: { value: '  urgente  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar pausa' }))

    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(fetchMock.mock.calls[0][0]).toBe('/api/pedidos/1/pausas')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ motivo: 'urgente' })
  })

  it('com o campo vazio, o motivo vai como null', async () => {
    const fetchMock = fetchPorRota({ '/api/pedidos/1/pausas': () => respostaJson(PAUSA_CRIADA, 201) })
    vi.stubGlobal('fetch', fetchMock)
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={LIVRE} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar pausa' }))

    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)).toEqual({ motivo: null })
  })

  it('Cancelar fecha o campo sem requisição', () => {
    const fetchMock = fetchPorRota({})
    vi.stubGlobal('fetch', fetchMock)
    render(<ControleDePausa pedido={LIVRE} aoMudar={async () => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.change(screen.getByLabelText('Motivo (opcional)'), { target: { value: 'abandonado' } })
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByLabelText('Motivo (opcional)')).toBeNull()
    expect(screen.getByRole('button', { name: 'Pausar' })).toBeTruthy()
    expect(fetchMock).not.toHaveBeenCalled()
    // Abrir de novo não traz o texto abandonado.
    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    expect((screen.getByLabelText('Motivo (opcional)') as HTMLInputElement).value).toBe('')
  })

  it('Retomar faz POST na retomada e avisa o chamador', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/1/retomada': () => respostaJson({ ...PAUSA_CRIADA, retomadoEm: '2026-09-28T11:00:00-03:00' }),
    })
    vi.stubGlobal('fetch', fetchMock)
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={PAUSADO} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Retomar' }))

    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(chamadasDePost(fetchMock)).toEqual([['/api/pedidos/1/retomada', 'POST']])
    expect((fetchMock.mock.calls[0][1] as RequestInit).body).toBeUndefined()
  })

  it.each(['Concluido', 'Cancelado'])('Pedido %s não oferece Pausar', (status) => {
    render(<ControleDePausa pedido={{ ...LIVRE, status }} aoMudar={async () => {}} />)

    expect(screen.queryByRole('button', { name: 'Pausar' })).toBeNull()
  })

  it('recusa do servidor vira banner com a frase dele, e a tela é recarregada (a pausa já mudou)', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/1/pausas': () => respostaJson(
        { erro: 'PedidoJaPausado', mensagem: 'O Pedido PED-2026-01 já está pausado.' }, 409,
      ),
    }))
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={LIVRE} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar pausa' }))

    expect((await screen.findByRole('alert')).textContent).toBe('O Pedido PED-2026-01 já está pausado.')
    expect(aoMudar).toHaveBeenCalledTimes(1)
  })

  it('recusa que não é conflito mostra o banner e NÃO recarrega', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/1/pausas': () => respostaJson({ erro: 'MotivoLongoDemais' }, 400),
    }))
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={LIVRE} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar pausa' }))

    expect((await screen.findByRole('alert')).textContent).toBe('O motivo passa de 200 caracteres.')
    expect(aoMudar).not.toHaveBeenCalled()
    // O campo continua aberto, com o que foi digitado disponível para corrigir.
    expect(screen.getByLabelText('Motivo (opcional)')).toBeTruthy()
  })

  it('toque duplo em Confirmar pausa envia uma vez só', async () => {
    let liberar: () => void = () => {}
    const fetchMock = vi.fn(() => new Promise<Response>((resolver) => {
      liberar = () => resolver(respostaJson(PAUSA_CRIADA, 201))
    }))
    vi.stubGlobal('fetch', fetchMock)
    const aoMudar = vi.fn(async () => {})
    render(<ControleDePausa pedido={LIVRE} aoMudar={aoMudar} />)

    fireEvent.click(screen.getByRole('button', { name: 'Pausar' }))
    const confirmar = screen.getByRole('button', { name: 'Confirmar pausa' })
    // Dois disparos no mesmo quadro: o clique e o `submit` do formulário (que o Enter no campo
    // também dispara e que o `disabled` do botão não barra).
    fireEvent.click(confirmar)
    fireEvent.submit(confirmar.closest('form')!)
    fireEvent.submit(confirmar.closest('form')!)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    liberar()

    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
