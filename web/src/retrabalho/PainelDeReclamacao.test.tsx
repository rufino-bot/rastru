// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeReclamacao } from './PainelDeReclamacao'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { AgrupamentoDto } from '../api/cadastros'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const AGRUPAMENTOS = [
  { id: 21, codigo: 'AGR-01' }, { id: 22, codigo: 'AGR-02' },
] as AgrupamentoDto[]

const no = (id: number, descricao: string, nivel: 'Peca' | 'Item', filhos: unknown[] = []) =>
  ({ id, descricao, codigoDoComponente: `C-${id}`, nivelHierarquico: nivel, filhos })

function montar(extra: Record<string, () => Response> = {}) {
  const fetchMock = fetchPorRota({
    '/api/agrupamentos/21/estrutura': () => respostaJson([no(812, 'Eixo', 'Peca', [no(900, 'Parafuso', 'Item')])]),
    '/api/agrupamentos/22/estrutura': () => respostaJson([no(813, 'Flange', 'Peca')]),
    '/api/pedidos/7/reclamacoes': () => respostaJson({ id: 1 }, 201),
    ...extra,
  })
  vi.stubGlobal('fetch', fetchMock)
  const aoConcluir = vi.fn()
  const aoFechar = vi.fn()
  render(<PainelDeReclamacao pedidoId={7} agrupamentos={AGRUPAMENTOS} aoConcluir={aoConcluir} aoFechar={aoFechar} />)
  return { fetchMock, aoConcluir, aoFechar }
}

const form = () => screen.getByRole('form', { name: 'Registrar reclamação' })

describe('PainelDeReclamacao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('oferece só as Peças do Pedido, nunca os Itens', async () => {
    montar()
    const seletor = (await screen.findByLabelText('Peça')) as HTMLSelectElement
    await waitFor(() => expect(within(seletor).getAllByRole('option').length).toBeGreaterThan(2))
    const nomes = within(seletor).getAllByRole('option').map((o) => o.textContent)
    expect(nomes.some((n) => n?.includes('Eixo'))).toBe(true)
    expect(nomes.some((n) => n?.includes('Flange'))).toBe(true)
    expect(nomes.some((n) => n?.includes('Parafuso'))).toBe(false)
  })

  it('exige a Peça, a quantidade e o texto do cliente', async () => {
    montar()
    const seletor = (await screen.findByLabelText('Peça')) as HTMLSelectElement
    await waitFor(() => expect(within(seletor).getAllByRole('option').length).toBeGreaterThan(2))
    const enviar = within(form()).getByRole('button', { name: 'Registrar reclamação' }) as HTMLButtonElement
    expect(enviar.disabled).toBe(true)
    fireEvent.change(seletor, { target: { value: '812' } })
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '2' } })
    expect(enviar.disabled).toBe(true)
    fireEvent.change(screen.getByLabelText('O que o cliente disse'), { target: { value: 'veio torto' } })
    expect(enviar.disabled).toBe(false)
  })

  it('grava com o texto do cliente em descricao e fecha', async () => {
    const { fetchMock, aoConcluir, aoFechar } = montar()
    const seletor = (await screen.findByLabelText('Peça')) as HTMLSelectElement
    await waitFor(() => expect(within(seletor).getAllByRole('option').length).toBeGreaterThan(2))
    fireEvent.change(seletor, { target: { value: '813' } })
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '1,5' } })
    fireEvent.change(screen.getByLabelText('O que o cliente disse'), { target: { value: 'veio torto' } })
    fireEvent.submit(form())
    fireEvent.submit(form())
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    const posts = fetchMock.mock.calls.filter(([u]) => String(u).endsWith('/reclamacoes'))
    expect(posts).toHaveLength(1)
    expect(JSON.parse(String(posts[0][1]?.body))).toEqual({ estruturaItemId: 813, quantidade: 1.5, descricao: 'veio torto' })
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('mostra SaldoInsuficiente com a frase do servidor dentro do painel', async () => {
    const { aoFechar } = montar({
      '/api/pedidos/7/reclamacoes': () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: 'Só 1 foi expedida.' }, 409),
    })
    const seletor = (await screen.findByLabelText('Peça')) as HTMLSelectElement
    await waitFor(() => expect(within(seletor).getAllByRole('option').length).toBeGreaterThan(2))
    fireEvent.change(seletor, { target: { value: '812' } })
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '9' } })
    fireEvent.change(screen.getByLabelText('O que o cliente disse'), { target: { value: 'x' } })
    fireEvent.submit(form())
    expect(await within(form()).findByText('Só 1 foi expedida.')).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('mostra o erro quando a lista de Peças não carrega', async () => {
    montar({ '/api/agrupamentos/21/estrutura': () => respostaJson({}, 400) })
    expect(await screen.findByText(/Não foi possível carregar as Peças/)).toBeTruthy()
  })
})
