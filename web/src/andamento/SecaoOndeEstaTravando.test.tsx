// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { SecaoOndeEstaTravando } from './SecaoOndeEstaTravando'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import { andamento } from '../testes/andamento'

afterEach(cleanup)

describe('SecaoOndeEstaTravando', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })
  afterEach(() => { vi.unstubAllGlobals() })

  function resposta(corpo: unknown, status = 200) {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson(corpo, status)))
  }

  it('carregando', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('erro', async () => {
    resposta({ mensagem: 'x' }, 500)
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('vazio_diz_nada_parado', async () => {
    resposta(andamento({ pecas: [] }))
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(await screen.findByText('Nada parado neste Pedido.')).toBeTruthy()
  })

  it('tres_numeros', async () => {
    resposta(andamento())
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(await screen.findByText('Aberto há 5 d')).toBeTruthy()
    expect(screen.getByText('Esperou para começar 2 d')).toBeTruthy()
    expect(screen.getByText('Em produção há 3 d')).toBeTruthy()
    expect(screen.queryByText(/pausado/)).toBeNull()
  })

  it('ainda_nao_comecou', async () => {
    resposta(andamento({ inicioReal: null }))
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(await screen.findByText('Ainda não começou')).toBeTruthy()
    expect(screen.queryByText(/Em produção há/)).toBeNull()
  })

  it('pausado_depois_do_inicio', async () => {
    resposta(andamento({ pausadoDepoisDoInicioSegundos: 3600 * 5 }))
    render(<SecaoOndeEstaTravando pedidoId={7} versao={0} />)
    expect(await screen.findByText('dos quais 5 h pausado')).toBeTruthy()
  })
})
