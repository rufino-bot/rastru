// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeAbertura } from './PainelDeAbertura'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import type { PedidoDto } from '../api/cadastros'
import type { BaixaDto } from '../api/baixas'
import type { ReclamacaoDto } from '../api/retrabalho'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const PEDIDO = { id: 7, numero: 'P-1020', dataEntrega: '2026-12-01' } as PedidoDto

const linha = (origemPosicao: string) => ({ estruturaItemId: 812, descricao: 'Eixo', origemPosicao, quantidade: 2, ehPeca: true })
const baixa = (origemPosicao = 'AguardandoColeta') =>
  ({ id: 51, motivo: 'PerdaArmazem', aRepor: 2, linhas: [linha(origemPosicao)] }) as unknown as BaixaDto
const reclamacao = { id: 3, descricao: 'Eixo', quantidade: 1.5, aRepor: 1.5 } as unknown as ReclamacaoDto

const no = (id: number, descricao: string, nivel: 'Peca' | 'Item') =>
  ({ id, descricao, codigoDoComponente: `C-${id}`, nivelHierarquico: nivel, filhos: [] })

type Porta = Parameters<typeof PainelDeAbertura>[0]['porta']

function montar(porta: Porta, post: () => Response = () => respostaJson({ id: 88, numero: 'P-1020-R2' }, 201)) {
  const outras = fetchPorRota({
    '/api/pedidos/7/agrupamentos': () => respostaJson([{ id: 21, codigo: 'AGR-01' }]),
    '/api/agrupamentos/21/estrutura': () => respostaJson([no(812, 'Eixo', 'Peca'), no(900, 'Parafuso', 'Item')]),
    '/api/setores': () => respostaJson([
      { id: 4, nome: 'Dobra', ativo: true, atividade: null, utilizaKit: false },
    ]),
  })
  // O GET do número sugerido e o POST de abrir dividem a rota: o mock separa pelo método.
  const fetchMock = vi.fn((u: string | URL, i?: RequestInit) => {
    if (String(u).split('?')[0] === '/api/pedidos/7/retrabalhos') {
      return Promise.resolve(i?.method === 'POST'
        ? post()
        : respostaJson({ numeroSugerido: 'P-1020-R2', retrabalhos: [] }))
    }
    return outras(u, i)
  })
  vi.stubGlobal('fetch', fetchMock)
  const aoAbrir = vi.fn()
  const aoFechar = vi.fn()
  const aoMudar = vi.fn()
  render(<PainelDeAbertura pedido={PEDIDO} porta={porta} aoAbrir={aoAbrir} aoFechar={aoFechar} aoMudar={aoMudar} />)
  return { fetchMock, aoAbrir, aoFechar, aoMudar }
}

const form = () => screen.getByRole('form', { name: 'Abrir retrabalho' })
const posts = (m: { mock: { calls: Array<[unknown, RequestInit?]> } }) =>
  m.mock.calls.filter(([u, i]) => String(u).endsWith('/retrabalhos') && i?.method === 'POST')
const numero = async () => (await screen.findByLabelText('Número do pedido')) as HTMLInputElement
const motivo = () => screen.getByLabelText('Motivo') as HTMLSelectElement

describe('PainelDeAbertura', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('vem com o número sugerido pelo servidor e o prazo do original, editáveis', async () => {
    montar({ tipo: 'Baixa', baixa: baixa() })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    const data = screen.getByLabelText('Data de entrega') as HTMLInputElement
    expect(data.value).toBe('2026-12-01')
    fireEvent.change(n, { target: { value: 'X-1' } })
    fireEvent.change(data, { target: { value: '2026-12-15' } })
    expect(n.value).toBe('X-1')
    expect(data.value).toBe('2026-12-15')
  })

  it('a baixa por perda sugere o motivo "Perda"', async () => {
    montar({ tipo: 'Baixa', baixa: baixa() })
    await numero()
    expect(motivo().value).toBe('Perda')
  })

  it('a baixa da não conforme sugere "Reprovação dimensional"', async () => {
    montar({ tipo: 'Baixa', baixa: baixa('NaoConforme') })
    await numero()
    expect(motivo().value).toBe('ReprovacaoDimensional')
    expect(within(motivo()).getByRole('option', { name: 'Reprovação dimensional' })).toBeTruthy()
  })

  it('nomeia a Peça do topo, não o Item que vem primeiro nas linhas da baixa', async () => {
    const comItem = {
      ...baixa(),
      linhas: [{ ...linha('AguardandoColeta'), estruturaItemId: 900, descricao: 'Parafuso', ehPeca: false }, linha('AIniciar')],
    } as unknown as BaixaDto
    montar({ tipo: 'Baixa', baixa: comItem })
    await numero()
    expect(screen.getByText('Repor 2 de Eixo')).toBeTruthy()
    expect(screen.queryByText(/Parafuso/)).toBeNull()
  })

  it('da baixa a quantidade é a dela, só de leitura, e não vai no corpo', async () => {
    const { fetchMock } = montar({ tipo: 'Baixa', baixa: baixa() })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    expect(screen.getByText('Repor 2 de Eixo')).toBeTruthy()
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    fireEvent.submit(form())
    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(JSON.parse(String(posts(fetchMock)[0][1]?.body))).toMatchObject({
      perdaId: 51, reclamacaoId: null, estruturaItemId: null, quantidade: null, destino: null,
      numero: 'P-1020-R2', dataEntrega: '2026-12-01', motivoRetrabalho: 'Perda',
    })
  })

  it('da reclamação o motivo vem "Solicitação do cliente"; "Consertar" pede o Setor e "Refazer" não', async () => {
    montar({ tipo: 'Reclamacao', reclamacao })
    await numero()
    expect(motivo().value).toBe('SolicitacaoCliente')
    expect(screen.getByText('Repor 1,5 de Eixo')).toBeTruthy()
    expect(screen.queryByLabelText('Setor')).toBeNull()
    fireEvent.click(screen.getByLabelText('Consertar'))
    const setor = (await screen.findByLabelText('Setor')) as HTMLSelectElement
    await waitFor(() => expect(within(setor).getByRole('option', { name: 'Dobra' })).toBeTruthy())
    fireEvent.click(screen.getByLabelText('Refazer'))
    expect(screen.queryByLabelText('Setor')).toBeNull()
  })

  it('da reclamação: o destino é obrigatório e Consertar exige o Setor', async () => {
    const { fetchMock } = montar({ tipo: 'Reclamacao', reclamacao })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    const enviar = within(form()).getByRole('button', { name: 'Abrir retrabalho' }) as HTMLButtonElement
    expect(enviar.disabled).toBe(true)
    fireEvent.click(screen.getByLabelText('Consertar'))
    expect(enviar.disabled).toBe(true)
    const setor = (await screen.findByLabelText('Setor')) as HTMLSelectElement
    await waitFor(() => expect(within(setor).getByRole('option', { name: 'Dobra' })).toBeTruthy())
    fireEvent.change(setor, { target: { value: '4' } })
    expect(enviar.disabled).toBe(false)
    fireEvent.submit(form())
    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(JSON.parse(String(posts(fetchMock)[0][1]?.body))).toMatchObject({
      reclamacaoId: 3, perdaId: null, destino: 'Consertar', setorId: 4, motivoRetrabalho: 'SolicitacaoCliente',
    })
  })

  it('Refazer não manda Setor', async () => {
    const { fetchMock } = montar({ tipo: 'Reclamacao', reclamacao })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    fireEvent.click(screen.getByLabelText('Refazer'))
    fireEvent.submit(form())
    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(JSON.parse(String(posts(fetchMock)[0][1]?.body))).toMatchObject({ destino: 'Refazer', setorId: null })
  })

  it('avulsa: oferece só Peças, e Peça, quantidade e motivo são obrigatórios', async () => {
    const { fetchMock } = montar({ tipo: 'Avulsa' })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    expect(motivo().value).toBe('')
    const seletor = screen.getByLabelText('Peça') as HTMLSelectElement
    await waitFor(() => expect(within(seletor).getAllByRole('option').length).toBeGreaterThan(1))
    const nomes = within(seletor).getAllByRole('option').map((o) => o.textContent)
    expect(nomes.some((t) => t?.includes('Eixo'))).toBe(true)
    expect(nomes.some((t) => t?.includes('Parafuso'))).toBe(false)
    const enviar = within(form()).getByRole('button', { name: 'Abrir retrabalho' }) as HTMLButtonElement
    expect(enviar.disabled).toBe(true)
    fireEvent.change(seletor, { target: { value: '812' } })
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '3' } })
    expect(enviar.disabled).toBe(true)
    fireEvent.change(motivo(), { target: { value: 'ErroInterno' } })
    expect(enviar.disabled).toBe(false)
    fireEvent.submit(form())
    await waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(JSON.parse(String(posts(fetchMock)[0][1]?.body))).toMatchObject({
      perdaId: null, reclamacaoId: null, estruturaItemId: 812, quantidade: 3, motivoRetrabalho: 'ErroInterno',
    })
  })

  it('confirmar entrega o Pedido novo a quem abriu e fecha; duplo toque é uma requisição só', async () => {
    const { fetchMock, aoAbrir, aoFechar } = montar({ tipo: 'Baixa', baixa: baixa() })
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    fireEvent.submit(form())
    fireEvent.submit(form())
    await waitFor(() => expect(aoAbrir).toHaveBeenCalled())
    expect(posts(fetchMock)).toHaveLength(1)
    expect(aoAbrir).toHaveBeenCalledWith(expect.objectContaining({ id: 88 }))
    expect(aoFechar).toHaveBeenCalled()
  })

  it('o 409 de número repetido fica dentro do painel aberto, com o digitado', async () => {
    const { aoAbrir, aoFechar } = montar(
      { tipo: 'Baixa', baixa: baixa() },
      () => respostaJson({ erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 5 }, 409),
    )
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    fireEvent.change(n, { target: { value: 'P-1020-R1' } })
    fireEvent.submit(form())
    expect(await screen.findByText('Já existe um pedido com este número.')).toBeTruthy()
    expect(form()).toBeTruthy()
    expect((screen.getByLabelText('Número do pedido') as HTMLInputElement).value).toBe('P-1020-R1')
    expect(aoAbrir).not.toHaveBeenCalled()
    expect(aoFechar).not.toHaveBeenCalled()
  })

  it('a recusa do servidor aparece com a frase dele dentro do painel', async () => {
    const { aoFechar, aoMudar } = montar(
      { tipo: 'Baixa', baixa: baixa() },
      () => respostaJson({ erro: 'JaReposto', mensagem: 'Esta baixa já foi reposta pelo retrabalho P-1020-R1.' }, 409),
    )
    const n = await numero()
    await waitFor(() => expect(n.value).toBe('P-1020-R2'))
    fireEvent.submit(form())
    expect(await screen.findByText('Esta baixa já foi reposta pelo retrabalho P-1020-R1.')).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
    // Tela velha: quem abriu recarrega a baixa e o cabeçalho.
    expect(aoMudar).toHaveBeenCalledTimes(1)
  })
})
