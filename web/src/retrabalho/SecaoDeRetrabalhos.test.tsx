// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SecaoDeRetrabalhos } from './SecaoDeRetrabalhos'
import type { RetrabalhosDoPedidoDto } from '../api/retrabalho'

afterEach(cleanup)

const LISTA: RetrabalhosDoPedidoDto = {
  numeroSugerido: 'P-1020-R2',
  retrabalhos: [{
    id: 51, numero: 'P-1020-R1', motivoRetrabalho: 'Perda', status: 'EmProducao', dataEntrega: '2026-12-01',
    origem: { tipo: 'Baixa', id: 7 },
  }],
}

function montar(carregar: () => Promise<RetrabalhosDoPedidoDto>, versao = 0) {
  return render(<MemoryRouter><SecaoDeRetrabalhos carregar={carregar} versao={versao} /></MemoryRouter>)
}

describe('SecaoDeRetrabalhos', () => {
  it('mostra o número com link, o motivo, o status e o prazo', async () => {
    montar(async () => LISTA)
    const secao = await screen.findByRole('region', { name: 'Retrabalhos' })
    const link = await within(secao).findByRole('link', { name: 'P-1020-R1' })
    expect(link.getAttribute('href')).toBe('/pedidos/51')
    expect(within(secao).getByText('Perda')).toBeTruthy()
    expect(within(secao).getByText('Em produção')).toBeTruthy()
    expect(within(secao).getByText(/01\/12\/2026/)).toBeTruthy()
  })

  it('carregando: mostra o estado de carregando', () => {
    montar(() => new Promise(() => {}))
    expect(screen.getByRole('status')).toBeTruthy()
  })

  it('vazio: diz que este pedido não tem retrabalho', async () => {
    montar(async () => ({ numeroSugerido: 'P-1020-R1', retrabalhos: [] }))
    expect(await screen.findByText('Nenhum retrabalho aberto a partir deste pedido')).toBeTruthy()
  })

  it('erro: mostra a frase e "Tentar de novo" lê outra vez', async () => {
    const carregar = vi.fn()
      .mockRejectedValueOnce(new Error('falhou'))
      .mockResolvedValueOnce(LISTA)
    montar(carregar)
    fireEvent.click(await screen.findByRole('button', { name: 'Tentar de novo' }))
    expect(await screen.findByRole('link', { name: 'P-1020-R1' })).toBeTruthy()
    expect(carregar).toHaveBeenCalledTimes(2)
  })

  it('recarrega quando a versão muda', async () => {
    const carregar = vi.fn(async () => LISTA)
    const r = montar(carregar, 0)
    await screen.findByRole('link', { name: 'P-1020-R1' })
    r.rerender(<MemoryRouter><SecaoDeRetrabalhos carregar={carregar} versao={1} /></MemoryRouter>)
    await waitFor(() => expect(carregar).toHaveBeenCalledTimes(2))
  })
})
