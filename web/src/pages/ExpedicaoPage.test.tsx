// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigationType } from 'react-router-dom'
import { ExpedicaoPage } from './ExpedicaoPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import {
  afericao, linhaDaRemessa, pecaNaExpedicao, pedidoNaExpedicao, posicoes, remessa,
} from '../testes/expedicao'
import type { PedidoNaExpedicaoDto, RemessaDto } from '../api/expedicao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'Qualidade'
let usuarioId = 12
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: usuarioId, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PAUSA = { desde: '2026-10-15T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'Cliente pediu' }

// PED-1 (atrasado): o Quadro, com aferição pendente (6 a expedir), e a Base, conforme (3). PED-2 (pausado): a Tampa,
// sem relatório (4 a expedir). A API manda do prazo mais próximo ao mais distante.
const QUADRO = pecaNaExpedicao()
const BASE = pecaNaExpedicao({
  id: 31, descricao: 'Base', codigoDoComponente: 'BA-01', situacao: 'Conforme', afericao: afericao({ resultado: 'Conforme' }),
  posicoes: posicoes({ conforme: 3 }), expedivel: 3, quantidade: 5,
})
const TAMPA = pecaNaExpedicao({
  id: 40, descricao: 'Tampa', codigoDoComponente: null, agrupamentoCodigo: 'AGR-2', requerRelatorioDimensional: false,
  situacao: 'SemRelatorio', posicoes: posicoes({ naExpedicao: 4 }), expedivel: 4, quantidade: 4,
})
const PED_1 = pedidoNaExpedicao({ id: 1, atrasado: true, pecas: [QUADRO, BASE] })
const PED_2 = pedidoNaExpedicao({
  id: 2, numero: 'PED-2', cliente: 'Beta Máquinas', dataEntrega: '2026-10-30', pausa: PAUSA, pecas: [TAMPA],
})
const SETORES = [{ id: 4, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }]
const RESULTADO = { remessa: remessa({ id: 8 }), avisos: [] }

interface Rotas {
  /** Repete a última; uma entrada pode ser uma promessa, para o teste segurar a recarga. */
  listas: (PedidoNaExpedicaoDto[] | Promise<PedidoNaExpedicaoDto[]>)[]
  remessas?: RemessaDto[][]
  remessa?: () => Response | Promise<Response>
  painel?: () => Response | Promise<Response>
  /** O `POST /remessas/{id}/estorno`. */
  estorno?: () => Response | Promise<Response>
}

function montarFetch({ listas, remessas = [[]], remessa: postarRemessa, painel, estorno }: Rotas) {
  let gets = 0
  let getsDeRemessas = 0
  const corposDaRemessa: unknown[] = []
  const chamadas: string[] = []
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    chamadas.push(`${metodo} ${caminho}`)
    if (caminho === '/api/expedicao') {
      const r = listas[Math.min(gets, listas.length - 1)]
      gets += 1
      return Promise.resolve(r).then((l) => respostaJson({ pedidos: l }))
    }
    if (caminho === '/api/remessas/recentes') {
      const r = remessas[Math.min(getsDeRemessas, remessas.length - 1)]
      getsDeRemessas += 1
      return Promise.resolve(respostaJson(r))
    }
    if (caminho === '/api/setores') return Promise.resolve(respostaJson(SETORES))
    if (caminho === '/api/remessas' && metodo === 'POST') {
      corposDaRemessa.push(JSON.parse(init?.body as string))
      return Promise.resolve((postarRemessa ?? (() => respostaJson(RESULTADO, 201)))())
    }
    if (/^\/api\/remessas\/\d+\/estorno$/.test(caminho) && metodo === 'POST') {
      return Promise.resolve((estorno ?? (() => respostaJson([], 200)))())
    }
    if (/^\/api\/estrutura\/\d+\/(afericoes|contagens|consertos|pedidos-de-liberacao)$/.test(caminho)) {
      return Promise.resolve((painel ?? (() => respostaJson({}, 201)))())
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${metodo} ${url}`))
  })
  return { fetchMock, gets: () => gets, getsDeRemessas: () => getsDeRemessas, corposDaRemessa, chamadas }
}

function UrlAtual() {
  const { search } = useLocation()
  const tipo = useNavigationType()
  return <p>{`url: ${search} (${tipo})`}</p>
}

function renderizar(caminho = '/expedicao') {
  return render(<MemoryRouter initialEntries={[caminho]}><ExpedicaoPage /><UrlAtual /></MemoryRouter>)
}

const grupo = (numero: string) => screen.getByRole('list', { name: `Peças do Pedido ${numero}` })
const cartao = (texto: string) => {
  const item = screen.getAllByRole('listitem')
    .find((li) => li.textContent?.includes(texto) && /^Peças do Pedido/.test(li.parentElement?.getAttribute('aria-label') ?? ''))
  if (!item) throw new Error(`cartão de ${texto} ausente`)
  return item
}
const carregou = () => screen.findByRole('heading', { level: 2, name: /^PED-1/ })
const caixa = (nome: string) => screen.getByRole('checkbox', { name: nome }) as HTMLInputElement
const marcar = (nome: string) => fireEvent.click(caixa(nome))

describe('ExpedicaoPage', () => {
  beforeEach(() => {
    perfil = 'Qualidade'
    usuarioId = 12
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando, depois os Pedidos na ordem que a API manda', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

    renderizar()

    expect(screen.getByRole('heading', { level: 1, name: 'Expedição' })).toBeTruthy()
    expect(screen.getByText('O que está no local de expedição, por prazo de entrega.')).toBeTruthy()
    expect(screen.getAllByRole('status').map((s) => s.textContent)).toContain('Carregando…')

    await carregou()
    expect(screen.queryByText('Carregando…')).toBeNull()
    const titulos = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)
    expect(titulos.slice(0, 2)).toEqual(['PED-1 · Metalúrgica Alfa', 'PED-2 · Beta Máquinas'])
    expect(within(grupo('PED-1')).getAllByRole('listitem').filter((li) => li.parentElement === grupo('PED-1')))
      .toHaveLength(2)
    expect(within(grupo('PED-2')).getByText('Tampa')).toBeTruthy()
  })

  it('o cabeçalho do Pedido mostra o prazo, Atrasado e Pausado', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

    renderizar()
    await carregou()

    const ped1 = screen.getByRole('region', { name: 'PED-1 · Metalúrgica Alfa' })
    expect(within(ped1).getByText('entrega 12/10/2026')).toBeTruthy()
    expect(within(ped1).getByText('Atrasado').className).toContain('bg-atraso-fundo')
    expect(within(ped1).queryByText('Pausado')).toBeNull()
    const ped2 = screen.getByRole('region', { name: 'PED-2 · Beta Máquinas' })
    expect(within(ped2).getByText('entrega 30/10/2026')).toBeTruthy()
    expect(within(ped2).getByText('Pausado').className).toContain('bg-atencao-fundo')
    expect(within(ped2).queryByText('Atrasado')).toBeNull()
  })

  it('vazio sem filtro diz "Nada no local de expedição."', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[]] }).fetchMock)

    renderizar()

    expect(await screen.findByText('Nada no local de expedição.')).toBeTruthy()
    expect(screen.queryByText('Nenhuma Peça com esses filtros.')).toBeNull()
    expect(screen.queryByRole('button', { name: /^Filtrar/ })).toBeNull()
  })

  it('vazio com filtro diz "Nenhuma Peça com esses filtros."', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

    renderizar('/expedicao?afericao=NaoConforme')

    expect(await screen.findByText('Nenhuma Peça com esses filtros.')).toBeTruthy()
    expect(screen.queryByText('Nada no local de expedição.')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    await carregou()
  })

  it('erro de carga mostra "Não foi possível carregar a expedição."', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string | URL) => Promise.resolve(
      String(url).includes('/expedicao') ? respostaJson({}, 400) : respostaJson([]),
    )))

    renderizar()

    expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível carregar a expedição.')
    expect(screen.queryByText('Nada no local de expedição.')).toBeNull()
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('o filtro por Aferição vai para a URL com replace e esconde o que não casa', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

    renderizar()
    await carregou()

    fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
    const afericaoGrupo = screen.getByRole('group', { name: 'Aferição' })
    fireEvent.click(within(afericaoGrupo).getByRole('checkbox', { name: /^Conforme/ }))

    expect(screen.getByText('url: ?afericao=Conforme (REPLACE)')).toBeTruthy()
    expect(screen.queryByText('QD-01 — Quadro')).toBeNull()
    expect(screen.getByText('BA-01 — Base')).toBeTruthy()
    // O Pedido sem nenhuma Peça que case sai.
    expect(screen.queryByRole('region', { name: 'PED-2 · Beta Máquinas' })).toBeNull()
  })

  it('a seleção da URL filtra na primeira carga', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

    renderizar('/expedicao?pedido=2')

    expect(await screen.findByText('Tampa')).toBeTruthy()
    expect(screen.queryByText('QD-01 — Quadro')).toBeNull()
  })

  it('os cartões de Pedidos diferentes têm botões com nomes diferentes', async () => {
    const OUTRO = pedidoNaExpedicao({ id: 3, numero: 'PED-3', dataEntrega: '2026-11-02', pecas: [{ ...QUADRO, id: 32 }] })
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, OUTRO]] }).fetchMock)

    renderizar()
    await carregou()

    const nomes = screen.getAllByRole('button', { name: /^Expedir/ }).map((b) => b.getAttribute('aria-label'))
    expect(nomes.filter((n) => n?.includes('Quadro'))).toEqual([
      'Expedir QD-01 — Quadro (PED-1)', 'Expedir QD-01 — Quadro (PED-3)',
    ])
  })

  it('mostra as Remessas recentes no fim da tela, e elas carregam sozinhas', async () => {
    vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1]], remessas: [[remessa()]] }).fetchMock)

    renderizar()
    await carregou()

    const lista = await screen.findByRole('list', { name: 'Remessas recentes' })
    expect(within(lista).getByText('QD-01 — Quadro (PED-1) · 4')).toBeTruthy()
  })

  it('a falha das Remessas recentes não derruba a lista de Peças', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string | URL) => {
      const caminho = String(url).split('?')[0]
      if (caminho === '/api/remessas/recentes') return Promise.resolve(respostaJson({}, 400))
      if (caminho === '/api/setores') return Promise.resolve(respostaJson(SETORES))
      return Promise.resolve(respostaJson({ pedidos: [PED_1] }))
    }))

    renderizar()
    await carregou()

    expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível carregar as remessas.')
    expect(screen.getByText('QD-01 — Quadro')).toBeTruthy()
  })

  it('o painel abre dentro do cartão e, depois de expedir, a lista e as remessas recarregam', async () => {
    const { fetchMock, gets, getsDeRemessas, corposDaRemessa } = montarFetch({
      listas: [[PED_1, PED_2], [PED_2]], remessas: [[], [remessa({ id: 8 })]],
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    expect(getsDeRemessas()).toBe(1)

    fireEvent.click(screen.getByRole('button', { name: 'Expedir QD-01 — Quadro (PED-1)' }))
    expect(cartao('Quadro').contains(screen.getByRole('form', { name: 'Expedir remessa' }))).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Expedir 6' }))

    await waitFor(() => expect(gets()).toBe(2))
    expect(corposDaRemessa).toEqual([{ itens: [{ estruturaItemId: 30, quantidade: 6 }] }])
    await waitFor(() => expect(getsDeRemessas()).toBe(2))
    expect(await screen.findByRole('list', { name: 'Remessas recentes' })).toBeTruthy()
  })

  it('Review Focus: 409 do painel mostra a frase dentro dele e recarrega a lista', async () => {
    const frase = 'Só há 3 para expedir de QD-01 — Quadro.'
    const MENOS = pedidoNaExpedicao({
      id: 1, atrasado: true,
      pecas: [pecaNaExpedicao({ posicoes: posicoes({ naExpedicao: 3 }), expedivel: 3 }), BASE],
    })
    const { fetchMock, gets } = montarFetch({
      listas: [[PED_1], [MENOS]],
      remessa: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: 'Expedir QD-01 — Quadro (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Expedir 6' }))

    await waitFor(() => expect(gets()).toBe(2))
    const painel = screen.getByRole('form', { name: 'Expedir remessa' })
    expect(within(painel).getByRole('alert').textContent).toBe(frase)
    // A lista nova chegou: o painel aberto agora oferece o que há.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Expedir 3' })).toBeTruthy())
    expect(cartao('Quadro').textContent).toContain('3 aqui')
  })

  it('Review Focus: a remessa que tira o cartão da lista devolve o foco ao título', async () => {
    // A recarga fica pendente até o teste soltá-la: o foco não pode ir a um botão da tela velha.
    let soltar: (l: PedidoNaExpedicaoDto[]) => void = () => {}
    const segunda = new Promise<PedidoNaExpedicaoDto[]>((r) => { soltar = r })
    const { fetchMock, gets } = montarFetch({ listas: [[PED_1, PED_2], segunda] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: 'Expedir QD-01 — Quadro (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Expedir 6' }))

    await waitFor(() => expect(gets()).toBe(2))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Expedir remessa' })).toBeNull())
    expect(document.activeElement).toBe(document.body)

    // O Quadro saiu da lista (a Peça toda expedida), e a Base também: o PED-1 inteiro some.
    await act(async () => { soltar([PED_2]) })
    await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
    const titulo = screen.getByRole('heading', { level: 1, name: 'Expedição' })
    await waitFor(() => expect(titulo.contains(document.activeElement)).toBe(true))
  })

  it('Cancelar devolve o foco ao botão do cartão, sem recarregar', async () => {
    const { fetchMock, gets } = montarFetch({ listas: [[PED_1]] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: 'Expedir BA-01 — Base (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Expedir BA-01 — Base (PED-1)' }))
    expect(gets()).toBe(1)
  })

  it('Aferir e Contar: o painel do cartão grava e a lista recarrega', async () => {
    const { fetchMock, gets, chamadas } = montarFetch({ listas: [[PED_1], [PED_1]] })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: 'Aferir QD-01 — Quadro (PED-1)' }))
    fireEvent.click(within(screen.getByRole('form', { name: 'Aferir' })).getByRole('radio', { name: 'Conforme' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrar aferição' }))

    await waitFor(() => expect(gets()).toBe(2))
    expect(chamadas).toContain('POST /api/estrutura/30/afericoes')
  })

  it('o Mandar para conserto oferece os Setores do cadastro', async () => {
    const NAO_CONFORME = pedidoNaExpedicao({
      pecas: [pecaNaExpedicao({
        situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naoConforme: 2 }), expedivel: 2,
      })],
    })
    vi.stubGlobal('fetch', montarFetch({ listas: [[NAO_CONFORME]] }).fetchMock)

    renderizar()
    await carregou()
    fireEvent.click(screen.getByRole('button', { name: 'Mandar para conserto QD-01 — Quadro (PED-1)' }))

    const setor = within(screen.getByRole('form', { name: 'Mandar para conserto' })).getByRole('combobox', { name: 'Setor' })
    expect(within(setor).getAllByRole('option').map((o) => o.textContent)).toEqual(['Escolha o Setor', 'Solda'])
  })

  describe('expedir em lote', () => {
    it('a caixa e a barra só aparecem para quem registra, e só na Peça que tem o que expedir', async () => {
      const SEM_NADA = pedidoNaExpedicao({
        id: 4, numero: 'PED-4', dataEntrega: '2026-11-05',
        pecas: [pecaNaExpedicao({
          id: 50, descricao: 'Aguarda', codigoDoComponente: null, situacao: 'NaoConforme', afericao: afericao(),
          posicoes: posicoes({ aguardandoLiberacao: 2 }), expedivel: 0,
        })],
      })
      for (const [p, ve] of [['Qualidade', true], ['Administrador', true], ['Gestao', false], ['PCP', false], ['Operador', false]] as const) {
        perfil = p
        vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2, SEM_NADA]] }).fetchMock)

        renderizar()
        await carregou()

        const nomes = screen.queryAllByRole('checkbox', { name: /^Marcar/ }).map((c) => c.getAttribute('aria-label'))
        expect(nomes, p).toEqual(ve
          ? ['Marcar QD-01 — Quadro (PED-1)', 'Marcar BA-01 — Base (PED-1)', 'Marcar Tampa (PED-2)']
          : [])
        expect(screen.queryByRole('button', { name: 'Marcar todos' }) !== null, p).toBe(ve)
        cleanup()
      }
    })

    it('o lote expede duas Peças numa remessa só e recarrega', async () => {
      const { fetchMock, gets, corposDaRemessa, getsDeRemessas, chamadas } = montarFetch({
        listas: [[PED_1, PED_2], [PED_1]], remessas: [[], [remessa({ id: 8 })]],
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar Tampa (PED-2)')
      marcar('Marcar QD-01 — Quadro (PED-1)')
      // O campo abre com o expedível, e o aviso do que sai sem aferição é por Peça.
      const campo = screen.getByRole('textbox', { name: 'Quantidade — QD-01 — Quadro (PED-1)' }) as HTMLInputElement
      expect(campo.value).toBe('6')
      fireEvent.change(screen.getByRole('textbox', { name: 'Quantidade — Tampa (PED-2)' }), { target: { value: '2,5' } })

      fireEvent.click(screen.getByRole('button', { name: 'Expedir' }))

      await waitFor(() => expect(corposDaRemessa).toHaveLength(1))
      // Na ordem da lista, não na da marcação, e a vírgula vira ponto.
      expect(corposDaRemessa[0]).toEqual({
        itens: [{ estruturaItemId: 30, quantidade: 6 }, { estruturaItemId: 40, quantidade: 2.5 }],
      })
      expect(chamadas.filter((c) => c === 'POST /api/remessas')).toHaveLength(1)
      await waitFor(() => expect(gets()).toBe(2))
      await waitFor(() => expect(getsDeRemessas()).toBe(2))
      // Sucesso limpa a seleção.
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Expedir' })).toBeNull())
      expect(caixa('Marcar BA-01 — Base (PED-1)').checked).toBe(false)
    })

    it('marcar mostra o aviso do que sai sem aferição, sem travar', async () => {
      vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1]] }).fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar QD-01 — Quadro (PED-1)')

      const aviso = within(cartao('Quadro')).getByRole('list', { name: 'Avisos da remessa' })
      expect(aviso.textContent).toBe('6 sem aferição')
      expect((screen.getByRole('button', { name: 'Expedir' }) as HTMLButtonElement).disabled).toBe(false)
    })

    it('campo acima do expedível ou ilegível deixa a barra sem enviar', async () => {
      const { fetchMock, corposDaRemessa } = montarFetch({ listas: [[PED_1]] })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar BA-01 — Base (PED-1)')
      const quadro = screen.getByRole('textbox', { name: 'Quantidade — QD-01 — Quadro (PED-1)' })
      const botao = screen.getByRole('button', { name: 'Expedir' }) as HTMLButtonElement

      fireEvent.change(quadro, { target: { value: '7' } })
      expect(botao.disabled).toBe(true)
      fireEvent.click(botao)
      fireEvent.change(quadro, { target: { value: 'seis' } })
      expect(botao.disabled).toBe(true)
      fireEvent.change(quadro, { target: { value: '0,5' } })
      expect(botao.disabled).toBe(false)
      fireEvent.click(botao)

      // Nunca uma remessa parcial: a Peça de campo ruim não ficou de fora de um envio das outras.
      await waitFor(() => expect(corposDaRemessa).toHaveLength(1))
      expect(corposDaRemessa[0]).toEqual({
        itens: [{ estruturaItemId: 30, quantidade: 0.5 }, { estruturaItemId: 31, quantidade: 3 }],
      })
    })

    it('Marcar todos marca só as Peças visíveis que têm o que expedir, e Desmarcar todos as solta', async () => {
      vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

      renderizar('/expedicao?pedido=1')
      await carregou()

      fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))
      expect(caixa('Marcar QD-01 — Quadro (PED-1)').checked).toBe(true)
      expect(caixa('Marcar BA-01 — Base (PED-1)').checked).toBe(true)
      expect(screen.getByRole('button', { name: 'Desmarcar todos' })).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: 'Desmarcar todos' }))
      expect(caixa('Marcar QD-01 — Quadro (PED-1)').checked).toBe(false)
    })

    it('marcado oculto pelo filtro vai no lote, com o aviso', async () => {
      const { fetchMock, corposDaRemessa } = montarFetch({ listas: [[PED_1, PED_2]] })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar Tampa (PED-2)')
      fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
      fireEvent.click(within(screen.getByRole('group', { name: 'Aferição' })).getByRole('checkbox', { name: /^Sem relatório/ }))
      expect(screen.queryByText('QD-01 — Quadro')).toBeNull()
      // Só o Quadro está oculto; a Tampa, marcada, continua à vista.
      expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: 'Expedir' }))

      await waitFor(() => expect(corposDaRemessa).toHaveLength(1))
      expect(corposDaRemessa[0]).toEqual({
        itens: [{ estruturaItemId: 30, quantidade: 6 }, { estruturaItemId: 40, quantidade: 4 }],
      })
    })

    it('recusa recarrega, mantém a seleção e mostra a frase na barra; sucesso limpa e recarrega', async () => {
      const frase = 'Só há 3 para expedir de Tampa.'
      let recusar = true
      const { fetchMock, gets, corposDaRemessa } = montarFetch({
        listas: [[PED_1, PED_2]],
        remessa: () => (recusar
          ? respostaJson({ erro: 'SaldoInsuficiente', mensagem: frase }, 409)
          : respostaJson(RESULTADO, 201)),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar Tampa (PED-2)')
      fireEvent.click(screen.getByRole('button', { name: 'Expedir' }))

      await waitFor(() => expect(gets()).toBe(2))
      await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(frase))
      expect(caixa('Marcar QD-01 — Quadro (PED-1)').checked).toBe(true)
      expect(caixa('Marcar Tampa (PED-2)').checked).toBe(true)

      recusar = false
      fireEvent.click(screen.getByRole('button', { name: 'Expedir' }))

      await waitFor(() => expect(gets()).toBe(3))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Expedir' })).toBeNull())
      expect(corposDaRemessa).toHaveLength(2)
      expect(caixa('Marcar Tampa (PED-2)').checked).toBe(false)
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('Peça que sai da lista na recarga sai da seleção, com aviso, sem reescrever o digitado nas outras', async () => {
      const { fetchMock, gets } = montarFetch({
        listas: [[PED_1, PED_2], [PED_2]],
        remessa: () => respostaJson({ erro: 'SaldoInsuficiente', mensagem: 'recusado' }, 409),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar Tampa (PED-2)')
      fireEvent.change(screen.getByRole('textbox', { name: 'Quantidade — Tampa (PED-2)' }), { target: { value: '1' } })
      fireEvent.click(screen.getByRole('button', { name: 'Expedir' }))

      await waitFor(() => expect(gets()).toBe(2))
      await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
      expect(await screen.findByText(/Uma Peça que você tinha marcado não tem mais o que expedir/)).toBeTruthy()
      expect((screen.getByRole('textbox', { name: 'Quantidade — Tampa (PED-2)' }) as HTMLInputElement).value).toBe('1')
    })

    it('marcar fecha o painel individual aberto do cartão', async () => {
      vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1, PED_2]] }).fetchMock)

      renderizar()
      await carregou()
      fireEvent.click(screen.getByRole('button', { name: 'Expedir QD-01 — Quadro (PED-1)' }))
      expect(screen.getByRole('form', { name: 'Expedir remessa' })).toBeTruthy()

      marcar('Marcar QD-01 — Quadro (PED-1)')

      expect(screen.queryByRole('form', { name: 'Expedir remessa' })).toBeNull()
      expect(screen.getByRole('button', { name: 'Expedir QD-01 — Quadro (PED-1)' })).toBeTruthy()
    })

    it('dois toques na barra mandam uma remessa só', async () => {
      let responder: (r: Response) => void = () => {}
      const { fetchMock, corposDaRemessa } = montarFetch({
        listas: [[PED_1, PED_2]], remessa: () => new Promise<Response>((r) => { responder = r }),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      marcar('Marcar Tampa (PED-2)')
      const botao = screen.getByRole('button', { name: 'Expedir' })

      // Os dois toques no mesmo quadro, antes de o React redesenhar o botão desabilitado: quem segura o segundo
      // é o `ref` do envio, não o `disabled`.
      act(() => {
        botao.click()
        botao.click()
      })

      await waitFor(() => expect(corposDaRemessa).toHaveLength(1))
      expect(screen.getByRole('button', { name: 'Expedindo…' })).toBeTruthy()
      // Em voo, a caixa e o campo travam.
      expect(caixa('Marcar Tampa (PED-2)').disabled).toBe(true)
      await act(async () => { responder(respostaJson(RESULTADO, 201)) })
      expect(corposDaRemessa).toHaveLength(1)
    })
  })

  describe('Remessas recentes na tela', () => {
    const CONCLUIDA = remessa({
      id: 5,
      linhas: [linhaDaRemessa({ pedidoId: 9, pedidoNumero: 'PED-9', pedidoConcluido: true, quantidade: 2 })],
    })

    it('estornar a remessa recarrega a lista de Peças', async () => {
      const { fetchMock, gets, chamadas } = montarFetch({
        listas: [[PED_1], [PED_1, PED_2]], remessas: [[CONCLUIDA], [{ ...CONCLUIDA, estornada: true, estornavel: false }]],
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      fireEvent.click(await screen.findByRole('button', { name: /^Estornar remessa/ }))
      expect(screen.getByText('Reabre o Pedido PED-9.')).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

      await waitFor(() => expect(gets()).toBe(2))
      expect(await screen.findByText('Tampa')).toBeTruthy()
      expect(chamadas).toContain('POST /api/remessas/5/estorno')
    })
  })

  describe('perfis que só leem', () => {
    // A remessa de outra pessoa (a Qualidade de id 12), estornável pelo servidor.
    const DE_OUTRA_PESSOA = remessa({ id: 6, usuarioId: 12, estornavel: true })

    it('Review Focus: Gestão vê a tela sem botão de escrita', async () => {
      perfil = 'Gestao'
      usuarioId = 5
      const NAO_CONFORME = pedidoNaExpedicao({
        id: 7, numero: 'PED-7', dataEntrega: '2026-11-01',
        pecas: [pecaNaExpedicao({
          id: 60, descricao: 'Suporte', codigoDoComponente: 'SU-01', situacao: 'NaoConforme', afericao: afericao(),
          posicoes: posicoes({ naExpedicao: 1, conforme: 2, naoConforme: 2 }), expedivel: 5,
        })],
      })
      const { fetchMock, chamadas } = montarFetch({
        listas: [[PED_1, PED_2, NAO_CONFORME]], remessas: [[DE_OUTRA_PESSOA]],
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await carregou()
      await screen.findByRole('list', { name: 'Remessas recentes' })

      // Lê tudo...
      expect(screen.getByText('QD-01 — Quadro')).toBeTruthy()
      expect(screen.getByText('SU-01 — Suporte')).toBeTruthy()
      expect(screen.getByText('QD-01 — Quadro (PED-1) · 4')).toBeTruthy()
      // ...e não registra nada.
      for (const acao of ['Aferir', 'Contar', 'Mandar para conserto', 'Pedir liberação', 'Expedir', 'Estornar remessa']) {
        expect(screen.queryAllByRole('button', { name: new RegExp(`^${acao}`) }), acao).toHaveLength(0)
      }
      expect(screen.queryAllByRole('checkbox', { name: /^Marcar/ })).toHaveLength(0)
      expect(screen.queryByRole('button', { name: 'Marcar todos' })).toBeNull()
      // Não precisa dos Setores, que só o conserto usa.
      expect(chamadas).not.toContain('GET /api/setores')
    })

    it('a Qualidade que não é a autora não estorna a remessa de outra pessoa; a autora, sim', async () => {
      usuarioId = 99
      vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1]], remessas: [[DE_OUTRA_PESSOA]] }).fetchMock)

      renderizar()
      await carregou()
      await screen.findByRole('list', { name: 'Remessas recentes' })
      expect(screen.queryByRole('button', { name: /^Estornar remessa/ })).toBeNull()
      cleanup()

      usuarioId = 12
      vi.stubGlobal('fetch', montarFetch({ listas: [[PED_1]], remessas: [[DE_OUTRA_PESSOA]] }).fetchMock)

      renderizar()
      await carregou()
      expect(await screen.findByRole('button', { name: /^Estornar remessa/ })).toBeTruthy()
    })
  })
})
