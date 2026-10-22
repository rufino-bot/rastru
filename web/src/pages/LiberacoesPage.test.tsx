// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { LiberacoesPage } from './LiberacoesPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { LiberacaoPendenteDto } from '../api/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'Gestao'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 7, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

function pendente(parcial: Partial<LiberacaoPendenteDto> = {}): LiberacaoPendenteDto {
  return {
    id: 5, quantidade: 3, justificativa: 'Sobra de dobra, o cliente aceita', dataHora: '2026-10-20T11:00:00-03:00',
    usuarioNome: 'Qualidade do turno',
    peca: { id: 30, descricao: 'Quadro', codigoDoComponente: 'QD-01' },
    pedido: { id: 1, numero: 'PED-1', cliente: 'Metalúrgica Alfa', dataEntrega: '2026-10-12', atrasado: true },
    ...parcial,
  }
}

const PRIMEIRA = pendente()
const SEGUNDA = pendente({
  id: 6, quantidade: 1.5, justificativa: 'Furo fora de 0,2 mm', dataHora: '2026-10-21T08:30:00-03:00',
  usuarioNome: 'Outra pessoa da Qualidade',
  peca: { id: 40, descricao: 'Tampa', codigoDoComponente: null },
  pedido: { id: 2, numero: 'PED-2', cliente: 'Beta Máquinas', dataEntrega: '2026-10-30', atrasado: false },
})

interface Rotas {
  /** Repete a última; uma entrada pode ser uma resposta pronta (o erro de carga). */
  listas: (LiberacaoPendenteDto[] | Response)[]
  resposta?: () => Response | Promise<Response>
}

function montarFetch({ listas, resposta }: Rotas) {
  let gets = 0
  const corpos: { id: string; corpo: unknown }[] = []
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    if (caminho === '/api/liberacoes/pendentes') {
      const r = listas[Math.min(gets, listas.length - 1)]
      gets += 1
      return Promise.resolve(r instanceof Response ? r.clone() : respostaJson(r))
    }
    const m = /^\/api\/liberacoes\/(\d+)\/resposta$/.exec(caminho)
    if (m && metodo === 'POST') {
      corpos.push({ id: m[1], corpo: JSON.parse(init?.body as string) })
      return Promise.resolve((resposta ?? (() => respostaJson({}, 201)))())
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${metodo} ${url}`))
  })
  return { fetchMock, gets: () => gets, corpos }
}

const renderizar = () => render(<MemoryRouter><LiberacoesPage /></MemoryRouter>)
const cartao = (texto: string) => {
  const item = screen.getAllByRole('listitem').find((li) => li.textContent?.includes(texto))
  if (!item) throw new Error(`cartão de ${texto} ausente`)
  return item
}
const formulario = () => screen.getByRole('form', { name: 'Liberar para expedir' })
const carregou = () => screen.findByText('QD-01 — Quadro')

describe('LiberacoesPage', () => {
  beforeEach(() => {
    perfil = 'Gestao'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('lista os pedidos com Peça, Pedido, quantidade, justificativa, quem pediu e quando', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PRIMEIRA, SEGUNDA]] }).fetchMock)

    renderizar()
    await carregou()

    expect(screen.getByRole('heading', { level: 1, name: 'Liberações' })).toBeTruthy()
    expect(screen.getByText('Unidades não conformes que a Qualidade pediu para expedir.')).toBeTruthy()
    const primeira = cartao('QD-01 — Quadro')
    expect(within(primeira).getByText('PED-1 · Metalúrgica Alfa')).toBeTruthy()
    expect(within(primeira).getByText('entrega 12/10/2026')).toBeTruthy()
    expect(within(primeira).getByText('Atrasado').className).toContain('bg-atraso-fundo')
    expect(within(primeira).getByText('Quantidade pedida: 3')).toBeTruthy()
    expect(within(primeira).getByText('Sobra de dobra, o cliente aceita')).toBeTruthy()
    expect(within(primeira).getByText('Qualidade do turno em 20/10/2026 11:00')).toBeTruthy()
    // Sem código de Componente, a Peça é a descrição (regra 19); o Pedido em dia não leva "Atrasado".
    const segunda = cartao('Tampa')
    expect(within(segunda).getByText('Tampa')).toBeTruthy()
    expect(within(segunda).getByText('Quantidade pedida: 1,5')).toBeTruthy()
    expect(within(segunda).queryByText('Atrasado')).toBeNull()
    // A ordem é a do servidor (o pedido mais antigo primeiro).
    expect(screen.getAllByRole('listitem').map((li) => li.textContent?.includes('QD-01'))).toEqual([true, false])
  })

  it('os três estados: carregando, "Nenhuma liberação pendente." e o erro de carga', async () => {
    // Carregando: a resposta não chegou.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))
    const primeira = renderizar()
    expect(screen.getAllByRole('status').map((s) => s.textContent)).toContain('Carregando…')
    expect(screen.queryByText('Nenhuma liberação pendente.')).toBeNull()
    primeira.unmount()

    // Vazio.
    vi.stubGlobal('fetch', montarFetch({ listas: [[]] }).fetchMock)
    const segunda = renderizar()
    expect(await screen.findByText('Nenhuma liberação pendente.')).toBeTruthy()
    expect(screen.queryByText('Carregando…')).toBeNull()
    expect(screen.queryByRole('alert')).toBeNull()
    segunda.unmount()

    // Erro de carga: a frase da tela, sem o vazio.
    vi.stubGlobal('fetch', montarFetch({ listas: [respostaJson({}, 400)] }).fetchMock)
    renderizar()
    expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível carregar as liberações.')
    expect(screen.queryByText('Nenhuma liberação pendente.')).toBeNull()
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('Liberar 2 manda quantidadeLiberada 2 e tira o pedido da lista', async () => {
    const { fetchMock, corpos } = montarFetch({ listas: [[PRIMEIRA, SEGUNDA], [SEGUNDA]] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(within(cartao('QD-01')).getByRole('button', { name: /^Responder/ }))

    // O campo abre com tudo o que foi pedido.
    const campo = within(formulario()).getByRole('textbox', { name: 'Quantidade liberada' }) as HTMLInputElement
    expect(campo.value).toBe('3')
    fireEvent.change(campo, { target: { value: '2' } })
    fireEvent.change(within(formulario()).getByRole('textbox', { name: 'Observação (opcional)' }), {
      target: { value: '  Pode ir, com aviso ao cliente  ' },
    })
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Liberar 2' }))

    await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
    expect(corpos).toEqual([{ id: '5', corpo: { quantidadeLiberada: 2, observacao: 'Pode ir, com aviso ao cliente' } }])
    expect(screen.getByText('Tampa')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Liberar para expedir' })).toBeNull()
    // O cartão saiu com o painel: o foco vai ao título da tela, não ao <body>.
    expect(document.activeElement).toBe(screen.getByText('Liberações'))
  })

  it('a quantidade com vírgula vai como número, e a observação em branco vai nula', async () => {
    const { fetchMock, corpos } = montarFetch({ listas: [[SEGUNDA], []] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await screen.findByText('Tampa')
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))

    const campo = within(formulario()).getByRole('textbox', { name: 'Quantidade liberada' }) as HTMLInputElement
    expect(campo.value).toBe('1,5')
    fireEvent.change(campo, { target: { value: '0,5' } })
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Liberar 0,5' }))

    expect(await screen.findByText('Nenhuma liberação pendente.')).toBeTruthy()
    expect(corpos).toEqual([{ id: '6', corpo: { quantidadeLiberada: 0.5, observacao: null } }])
  })

  it('Recusar tudo manda quantidadeLiberada 0', async () => {
    const { fetchMock, corpos } = montarFetch({ listas: [[PRIMEIRA], []] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))
    // Recusar não depende do campo: ele pode até estar inválido.
    fireEvent.change(within(formulario()).getByRole('textbox', { name: 'Quantidade liberada' }), {
      target: { value: 'abc' },
    })
    expect((within(formulario()).getByRole('button', { name: 'Liberar' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Recusar tudo' }))

    expect(await screen.findByText('Nenhuma liberação pendente.')).toBeTruthy()
    expect(corpos).toEqual([{ id: '5', corpo: { quantidadeLiberada: 0, observacao: null } }])
  })

  it('o campo recusa, antes da rede, zero e mais do que foi pedido', async () => {
    const { fetchMock, corpos } = montarFetch({ listas: [[PRIMEIRA]] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))
    const campo = within(formulario()).getByRole('textbox', { name: 'Quantidade liberada' })

    fireEvent.change(campo, { target: { value: '4' } })
    expect(within(formulario()).getByText('No máximo 3.')).toBeTruthy()
    fireEvent.change(campo, { target: { value: '0' } })
    expect(within(formulario()).getByText('A quantidade precisa ser maior que zero.')).toBeTruthy()
    // Sem quantidade válida o submit de liberar fica sem quantidade no rótulo e travado.
    const liberar = within(formulario()).getByRole('button', { name: 'Liberar' }) as HTMLButtonElement
    expect(liberar.disabled).toBe(true)
    fireEvent.submit(formulario())
    expect(corpos).toEqual([])
  })

  it('quantidade acima da pedida é recusada pelo servidor e a frase fica no painel', async () => {
    const frase = 'Foram pedidas 3 unidades; não dá para liberar mais.'
    const { fetchMock } = montarFetch({
      listas: [[PRIMEIRA]],
      resposta: () => respostaJson({ erro: 'QuantidadeAcimaDaPedida', mensagem: frase }, 400),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))
    fireEvent.change(within(formulario()).getByRole('textbox', { name: 'Observação (opcional)' }), {
      target: { value: 'Pode ir' },
    })
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Liberar 3' }))

    expect((await within(formulario()).findByRole('alert')).textContent).toContain(frase)
    // O painel segue aberto, com o que foi digitado, e a lista não mudou.
    expect((within(formulario()).getByRole('textbox', { name: 'Observação (opcional)' }) as HTMLTextAreaElement).value)
      .toBe('Pode ir')
    expect(screen.getByText('QD-01 — Quadro')).toBeTruthy()
  })

  it('409 LiberacaoNaoPendente: a frase do servidor aparece e a lista recarrega', async () => {
    const frase = 'Este pedido de liberação já foi respondido.'
    const { fetchMock, gets } = montarFetch({
      listas: [[PRIMEIRA, SEGUNDA], [SEGUNDA]],
      resposta: () => respostaJson({ erro: 'LiberacaoNaoPendente', mensagem: frase }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(within(cartao('QD-01')).getByRole('button', { name: /^Responder/ }))
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Recusar tudo' }))

    // A recarga tira o cartão (e o painel com ele): a frase sobe para a tela em vez de se perder.
    await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
    expect(gets()).toBe(2)
    expect((await screen.findByRole('alert')).textContent).toContain(frase)
    expect(screen.getByText('Tampa')).toBeTruthy()
  })

  it('o segundo toque no mesmo quadro não manda a resposta duas vezes', async () => {
    let soltar: (r: Response) => void = () => {}
    const { fetchMock, corpos } = montarFetch({
      listas: [[PRIMEIRA], []],
      resposta: () => new Promise<Response>((resolve) => { soltar = resolve }) as unknown as Response,
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))
    const liberar = within(formulario()).getByRole('button', { name: 'Liberar 3' })

    // Os três toques no mesmo quadro, antes de o React redesenhar os botões desabilitados.
    const recusar = within(formulario()).getByRole('button', { name: 'Recusar tudo' })
    act(() => { liberar.click(); liberar.click(); recusar.click() })

    expect(corpos).toHaveLength(1)
    soltar(respostaJson({}, 201))
    expect(await screen.findByText('Nenhuma liberação pendente.')).toBeTruthy()
    expect(corpos).toHaveLength(1)
  })

  it('Cancelar fecha o painel e devolve o foco ao Responder do cartão', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PRIMEIRA]] }).fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: /^Responder/ }))
    expect(screen.queryByRole('button', { name: /^Responder/ })).toBeNull()
    fireEvent.click(within(formulario()).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('form', { name: 'Liberar para expedir' })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Responder/ }))
  })

  it('o painel mostra a Peça e o Pedido, e só um painel fica aberto por vez', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PRIMEIRA, SEGUNDA]] }).fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(within(cartao('QD-01')).getByRole('button', { name: /^Responder/ }))
    expect(within(formulario()).getByText('QD-01 — Quadro (PED-1)')).toBeTruthy()
    // O botão de cada cartão leva o Pedido no nome: o mesmo código aparece em Pedidos diferentes.
    expect(screen.getByRole('button', { name: 'Responder Tampa (PED-2)' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Responder Tampa (PED-2)' }))
    expect(screen.getAllByRole('form', { name: 'Liberar para expedir' })).toHaveLength(2)
  })

  it('Review Focus: Qualidade vê a lista sem Responder', async () => {
    perfil = 'Qualidade'
    vi.stubGlobal('fetch', montarFetch({ listas: [[PRIMEIRA, SEGUNDA]] }).fetchMock)

    renderizar()
    await carregou()

    expect(screen.getByText('Tampa')).toBeTruthy()
    expect(screen.getByText('Sobra de dobra, o cliente aceita')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Responder/ })).toBeNull()
  })

  it('Administrador responde também', async () => {
    perfil = 'Administrador'
    vi.stubGlobal('fetch', montarFetch({ listas: [[PRIMEIRA]] }).fetchMock)

    renderizar()
    await carregou()

    expect(screen.getByRole('button', { name: /^Responder/ })).toBeTruthy()
  })
})
