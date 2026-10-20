// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigationType } from 'react-router-dom'
import { SeparacaoPage } from './SeparacaoPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { MaterialPendente, PendenciaDeSeparacaoDto } from '../api/separacao'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

let perfil = 'Separador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 5, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PARAFUSO: MaterialPendente = {
  materialId: 7, codigo: 'PAR-M8', descricao: 'Parafuso M8', unidadeMedida: 'UN', planejado: 10, separado: 8, pendencia: 2,
}
const ELETRODO: MaterialPendente = {
  materialId: 9, codigo: 'ELE-01', descricao: 'Eletrodo', unidadeMedida: 'KG', planejado: 1, separado: 0.5, pendencia: 0.5,
}

const PAUSA = { desde: '2026-10-15T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'Cliente pediu' }

/** Kit Quadro (Pedido 1, atrasado) indo para a Solda; Peça Tampa (Pedido 2, pausado) sem Roteiro. */
const QUADRO: PendenciaDeSeparacaoDto = {
  no: { id: 30, descricao: 'Quadro', codigoDoComponente: 'QD-01', agrupamentoId: 1, agrupamentoCodigo: 'AGR-1' },
  pedido: { id: 1, numero: 'PED-1', cliente: 'Metalúrgica Alfa', dataEntrega: '2026-10-12', atrasado: true, pausa: null },
  destino: { setorId: 4, nome: 'Solda' },
  materiais: [PARAFUSO, ELETRODO],
}
const TAMPA: PendenciaDeSeparacaoDto = {
  no: { id: 40, descricao: 'Tampa', codigoDoComponente: null, agrupamentoId: 2, agrupamentoCodigo: 'AGR-2' },
  pedido: { id: 2, numero: 'PED-2', cliente: 'Beta Máquinas', dataEntrega: '2026-10-30', atrasado: false, pausa: PAUSA },
  destino: null,
  materiais: [PARAFUSO],
}

/**
 * Responde a lista em ordem, repetindo a última; o `POST` da separação responde `separacao()`. Uma entrada
 * pode ser uma promessa, para o teste segurar a recarga e olhar a tela no meio dela.
 */
function montarFetch(
  listas: (PendenciaDeSeparacaoDto[] | Promise<PendenciaDeSeparacaoDto[]>)[],
  separacao: () => Response | Promise<Response> = () => respostaJson({ separacoes: [], materiais: [] }, 201),
  lote: () => Response | Promise<Response> = () => respostaJson([], 201),
) {
  let gets = 0
  const corposDoLote: unknown[] = []
  const fetchMock = vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/separacoes/pendencias') {
      const r = listas[Math.min(gets, listas.length - 1)]
      gets += 1
      return Promise.resolve(r).then((l) => respostaJson(l))
    }
    if (/^\/api\/estrutura\/\d+\/separacoes$/.test(caminho)) return Promise.resolve(separacao())
    if (caminho === '/api/separacoes' && init?.method === 'POST') {
      corposDoLote.push(JSON.parse(init.body as string))
      return Promise.resolve(lote())
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, gets: () => gets, corposDoLote }
}

function UrlAtual() {
  const { search } = useLocation()
  const tipo = useNavigationType()
  return <p>{`url: ${search} (${tipo})`}</p>
}

function renderizar(caminho = '/separacao') {
  return render(<MemoryRouter initialEntries={[caminho]}><SeparacaoPage /><UrlAtual /></MemoryRouter>)
}

const lista = () => screen.findByRole('list', { name: 'Itens a separar' })
const cartao = (texto: string) => {
  const item = screen.getAllByRole('listitem').find((li) => li.textContent?.includes(texto) && li.parentElement?.getAttribute('aria-label') === 'Itens a separar')
  if (!item) throw new Error(`cartão de ${texto} ausente`)
  return item
}

describe('SeparacaoPage', () => {
  beforeEach(() => {
    perfil = 'Separador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra um cartão por nó com Pedido, prazo, destino e as linhas pendentes', async () => {
    vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

    renderizar()

    expect(screen.getByRole('heading', { level: 1, name: 'Separação' })).toBeTruthy()
    const itens = within(await lista()).getAllByRole('listitem').filter((li) => li.parentElement?.getAttribute('aria-label') === 'Itens a separar')
    expect(itens).toHaveLength(2)

    const quadro = within(cartao('Quadro'))
    expect(quadro.getByText('QD-01 — Quadro')).toBeTruthy()
    expect(quadro.getByText('PED-1 · Metalúrgica Alfa · entrega 12/10/2026')).toBeTruthy()
    expect(quadro.getByText('→ Solda')).toBeTruthy()
    const materiais = quadro.getByRole('list', { name: 'Materiais pendentes' })
    expect(within(materiais).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'PAR-M8 Parafuso M8 · falta 2 UN (8 de 10)',
      'ELE-01 Eletrodo · falta 0,5 KG (0,5 de 1)',
    ])

    const tampa = within(cartao('Tampa'))
    expect(tampa.getByText('Tampa')).toBeTruthy()
    expect(tampa.getByText('PED-2 · Beta Máquinas · entrega 30/10/2026')).toBeTruthy()
    expect(tampa.getByText('sem Roteiro')).toBeTruthy()
  })

  it('Atrasado e Pausado aparecem como pílulas; o resto é texto neutro', async () => {
    vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

    renderizar()
    await lista()

    const quadro = within(cartao('Quadro'))
    expect(quadro.getByText('Atrasado').className).toContain('bg-atraso-fundo')
    expect(quadro.queryByText('Pausado')).toBeNull()
    const tampa = within(cartao('Tampa'))
    expect(tampa.getByText('Pausado').className).toContain('bg-atencao-fundo')
    expect(tampa.queryByText('Atrasado')).toBeNull()

    // Destino, Pedido e as linhas de material não carregam cor de estado.
    const neutros = [
      quadro.getByText('→ Solda'), tampa.getByText('sem Roteiro'),
      quadro.getByText('PED-1 · Metalúrgica Alfa · entrega 12/10/2026'),
      ...within(quadro.getByRole('list', { name: 'Materiais pendentes' })).getAllByRole('listitem'),
    ]
    for (const el of neutros) {
      expect(el.className, el.textContent ?? '').toMatch(/text-tinta/)
      expect(el.className, el.textContent ?? '').not.toMatch(/positivo|negativo|atencao|atraso|kit/)
    }
  })

  it('Separar só aparece para quem separa', async () => {
    for (const [p, ve] of [['Separador', true], ['Administrador', true], ['PCP', false], ['Operador', false]] as const) {
      perfil = p
      vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

      renderizar()
      await lista()

      expect(screen.queryAllByRole('button', { name: /^Separar/ }), p).toHaveLength(ve ? 2 : 0)
      // A leitura é de todos: o cartão aparece mesmo sem a ação.
      expect(screen.getByText('QD-01 — Quadro'), p).toBeTruthy()
      cleanup()
    }
  })

  it('o Separar de dois cartões do mesmo item em Pedidos diferentes tem nomes diferentes', async () => {
    const QUADRO_DO_PED_3: PendenciaDeSeparacaoDto = {
      ...QUADRO,
      no: { ...QUADRO.no, id: 31 },
      pedido: { ...QUADRO.pedido, id: 3, numero: 'PED-3' },
    }
    vi.stubGlobal('fetch', montarFetch([[QUADRO, QUADRO_DO_PED_3]]).fetchMock)

    renderizar()
    await lista()

    const nomes = screen.getAllByRole('button', { name: /^Separar/ }).map((b) => b.getAttribute('aria-label'))
    expect(nomes).toEqual(['Separar QD-01 — Quadro (PED-1)', 'Separar QD-01 — Quadro (PED-3)'])
  })

  it('o painel abre logo abaixo do cartão escolhido, um por vez', async () => {
    vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

    renderizar()
    await lista()

    fireEvent.click(within(cartao('Quadro')).getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' }))
    const painel = screen.getByRole('form', { name: 'Separar material' })
    expect(cartao('Quadro').contains(painel)).toBe(true)
    // O Separar do cartão aberto some enquanto o painel está aberto.
    expect(within(cartao('Quadro')).queryByRole('button', { name: /^Separar/ })).toBeNull()

    fireEvent.click(within(cartao('Tampa')).getByRole('button', { name: 'Separar Tampa (PED-2)' }))
    expect(screen.getAllByRole('form', { name: 'Separar material' })).toHaveLength(1)
    expect(cartao('Tampa').contains(screen.getByRole('form', { name: 'Separar material' }))).toBe(true)
  })

  it('depois de registrar, a lista recarrega e o foco volta ao Separar do cartão, ou ao título se o cartão sumiu', async () => {
    // Primeiro registro: o Quadro continua com o eletrodo pendente. Segundo: o Quadro sai da lista.
    const QUADRO_SO_ELETRODO = { ...QUADRO, materiais: [ELETRODO] }
    // A terceira carga fica pendente até o teste soltá-la: o foco não pode ir ao "Separar" que ainda está
    // na tela velha e vai sumir com a recarga.
    let soltarTerceira: (l: PendenciaDeSeparacaoDto[]) => void = () => {}
    const terceira = new Promise<PendenciaDeSeparacaoDto[]>((r) => { soltarTerceira = r })
    const { fetchMock, gets } = montarFetch([[QUADRO, TAMPA], [QUADRO_SO_ELETRODO, TAMPA], terceira])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await lista()

    fireEvent.click(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' }))
    fireEvent.change(screen.getByRole('textbox', { name: /ELE-01/ }), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Registrar separação' }))

    await waitFor(() => expect(gets()).toBe(2))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' })))
    expect(screen.queryByRole('form', { name: 'Separar material' })).toBeNull()
    expect(within(cartao('Quadro')).getAllByRole('listitem').map((li) => li.textContent))
      .toEqual(['ELE-01 Eletrodo · falta 0,5 KG (0,5 de 1)'])

    fireEvent.click(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrar separação' }))

    await waitFor(() => expect(gets()).toBe(3))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Separar material' })).toBeNull())
    // Painel fechado, recarga em voo: ninguém recebeu o foco ainda.
    expect(document.activeElement).toBe(document.body)
    await act(async () => { soltarTerceira([TAMPA]) })
    await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
    const titulo = screen.getByRole('heading', { level: 1, name: 'Separação' })
    await waitFor(() => expect(titulo.contains(document.activeElement)).toBe(true))
  })

  it('a resposta de um envio que chega depois de abrir outro cartão não fecha o painel novo', async () => {
    let responder: (r: Response) => void = () => {}
    const { fetchMock, gets } = montarFetch([[QUADRO, TAMPA]], () => new Promise<Response>((r) => { responder = r }))
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await lista()

    fireEvent.click(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrar separação' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Registrando…' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Separar Tampa (PED-2)' }))
    await act(async () => { responder(respostaJson({ separacoes: [], materiais: [] }, 201)) })

    await waitFor(() => expect(gets()).toBe(2))
    expect(cartao('Tampa').contains(screen.getByRole('form', { name: 'Separar material' }))).toBe(true)
  })

  it('Cancelar devolve o foco ao Separar do cartão, sem recarregar', async () => {
    const { fetchMock, gets } = montarFetch([[QUADRO, TAMPA]])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await lista()

    fireEvent.click(screen.getByRole('button', { name: 'Separar Tampa (PED-2)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Separar Tampa (PED-2)' }))
    expect(gets()).toBe(1)
  })

  it('409 com o painel aberto recarrega a lista e mantém o painel com a frase', async () => {
    const frase = 'O material ELE-01 não está nos materiais do item QD-01 — Quadro.'
    const SEM_ELETRODO = { ...QUADRO, materiais: [PARAFUSO] }
    const { fetchMock, gets } = montarFetch(
      [[QUADRO, TAMPA], [SEM_ELETRODO, TAMPA]],
      () => respostaJson({ erro: 'MaterialForaDoPlanejado', mensagem: frase }, 409),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    await lista()

    fireEvent.click(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrar separação' }))

    await waitFor(() => expect(gets()).toBe(2))
    const painel = screen.getByRole('form', { name: 'Separar material' })
    await waitFor(() => expect(within(painel).queryByRole('textbox', { name: /ELE-01/ })).toBeNull())
    expect(within(painel).getByRole('alert').textContent).toBe(frase)
    expect(within(painel).getByRole('textbox', { name: /PAR-M8/ })).toBeTruthy()
  })

  it('o filtro vai para a URL com replace', async () => {
    vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

    renderizar()
    await lista()

    fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
    const destino = screen.getByRole('group', { name: 'Setor de destino' })
    fireEvent.click(within(destino).getByRole('checkbox', { name: 'sem Roteiro' }))

    expect(screen.getByText('url: ?destino=sem-roteiro (REPLACE)')).toBeTruthy()
    expect(screen.queryByText('QD-01 — Quadro')).toBeNull()
    expect(screen.getByText('Tampa')).toBeTruthy()
  })

  it('a seleção da URL filtra na primeira carga', async () => {
    vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

    renderizar('/separacao?pedido=1&material=9')
    await lista()

    expect(screen.getByText('QD-01 — Quadro')).toBeTruthy()
    expect(screen.queryByText('Tampa')).toBeNull()
  })

  describe('separar em lote', () => {
    // O mesmo nó (mesmo código e descrição) num outro Pedido: o nome da caixa tem de distinguir os dois.
    const QUADRO_DO_PED_3: PendenciaDeSeparacaoDto = {
      ...QUADRO,
      no: { ...QUADRO.no, id: 31 },
      pedido: { ...QUADRO.pedido, id: 3, numero: 'PED-3', atrasado: false },
      materiais: [PARAFUSO],
    }
    const caixa = (nome: string) => screen.getByRole('checkbox', { name: nome })
    const marcar = (nome: string) => fireEvent.click(caixa(nome))
    const campoDoLote = (material: RegExp, no: string) => screen.getByRole('textbox', { name: new RegExp(`${material.source}.* — ${no}$`) })
    const filtrarSemRoteiro = () => {
      fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
      fireEvent.click(within(screen.getByRole('group', { name: 'Setor de destino' })).getByRole('checkbox', { name: 'sem Roteiro' }))
    }

    it('a caixa do cartão só aparece para quem separa, com nome que distingue nó e Pedido', async () => {
      for (const [p, ve] of [['Separador', true], ['Administrador', true], ['PCP', false], ['Operador', false]] as const) {
        perfil = p
        vi.stubGlobal('fetch', montarFetch([[QUADRO, QUADRO_DO_PED_3, TAMPA]]).fetchMock)

        renderizar()
        await lista()

        const nomes = screen.queryAllByRole('checkbox', { name: /^Marcar/ }).map((c) => c.getAttribute('aria-label'))
        expect(nomes, p).toEqual(ve ? ['Marcar QD-01 — Quadro (PED-1)', 'Marcar QD-01 — Quadro (PED-3)', 'Marcar Tampa (PED-2)'] : [])
        expect(screen.queryByRole('button', { name: 'Marcar todos' }) !== null, p).toBe(ve)
        cleanup()
      }
    })

    it('marcar o cartão mostra um campo por material pendente, preenchido com a pendência, com o aviso neutro', async () => {
      vi.stubGlobal('fetch', montarFetch([[QUADRO, QUADRO_DO_PED_3, TAMPA]]).fetchMock)

      renderizar()
      await lista()
      expect(within(cartao('PED-1')).queryAllByRole('textbox')).toHaveLength(0)

      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar QD-01 — Quadro (PED-3)')

      const doQuadro = within(cartao('PED-1')).getAllByRole('textbox')
      expect(doQuadro.map((c) => (c as HTMLInputElement).value)).toEqual(['2', '0,5'])
      // O nome de cada campo é único na tela, mesmo com o mesmo material no mesmo nó de outro Pedido.
      const nomes = screen.getAllByRole('textbox').map((c) => c.getAttribute('aria-label'))
      expect(new Set(nomes).size).toBe(nomes.length)
      expect(nomes).toContain('PAR-M8 Parafuso M8 (UN) — QD-01 — Quadro (PED-1)')

      const parafuso = campoDoLote(/PAR-M8/, 'QD-01 — Quadro \\(PED-1\\)')
      expect(document.getElementById(parafuso.getAttribute('aria-describedby')!)!.textContent).toBe('Falta 2 UN')
      fireEvent.change(parafuso, { target: { value: '5' } })
      const aviso = document.getElementById(parafuso.getAttribute('aria-describedby')!)!
      expect(aviso.textContent).toBe('acima do planejado em 3 UN')
      expect(aviso.className).toMatch(/text-tinta/)
      expect(aviso.className).not.toMatch(/positivo|negativo|atencao|atraso/)
    })

    it('a barra diz "Separar 2 itens" e o envio leva os dois nós num POST só', async () => {
      const { fetchMock, corposDoLote } = montarFetch([[QUADRO, TAMPA]])
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar Tampa (PED-2)')
      expect(screen.getByRole('button', { name: 'Separar 1 item' })).toBeTruthy()
      marcar('Marcar QD-01 — Quadro (PED-1)')

      fireEvent.click(screen.getByRole('button', { name: 'Separar 2 itens' }))

      await waitFor(() => expect(corposDoLote).toHaveLength(1))
      // Na ordem da lista, não na da marcação.
      expect(corposDoLote[0]).toEqual({
        nos: [
          { estruturaItemId: 30, linhas: [{ materialId: 7, quantidade: 2 }, { materialId: 9, quantidade: 0.5 }] },
          { estruturaItemId: 40, linhas: [{ materialId: 7, quantidade: 2 }] },
        ],
      })
      expect(fetchMock.mock.calls.filter(([u]) => /\/api\/estrutura\//.test(String(u)))).toHaveLength(0)
    })

    it('campo vazio ou zero não vai; cartão marcado todo vazio, ou com texto que não é número, deixa o botão sem enviar', async () => {
      const { fetchMock, corposDoLote } = montarFetch([[QUADRO, TAMPA]])
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      const parafuso = campoDoLote(/PAR-M8/, 'QD-01 — Quadro \\(PED-1\\)')
      const eletrodo = campoDoLote(/ELE-01/, 'QD-01 — Quadro \\(PED-1\\)')

      fireEvent.change(eletrodo, { target: { value: '0' } })
      fireEvent.change(parafuso, { target: { value: '' } })
      const botao = screen.getByRole('button', { name: 'Separar 1 item' }) as HTMLButtonElement
      expect(botao.disabled).toBe(true)
      fireEvent.click(botao)

      fireEvent.change(parafuso, { target: { value: 'dois' } })
      fireEvent.change(eletrodo, { target: { value: '0,5' } })
      expect(botao.disabled).toBe(true)

      fireEvent.change(parafuso, { target: { value: '' } })
      expect(botao.disabled).toBe(false)
      fireEvent.click(botao)

      await waitFor(() => expect(corposDoLote).toHaveLength(1))
      expect(corposDoLote[0]).toEqual({ nos: [{ estruturaItemId: 30, linhas: [{ materialId: 9, quantidade: 0.5 }] }] })
    })

    it('marcado oculto pelo filtro vai no lote, com o aviso', async () => {
      const { fetchMock, corposDoLote } = montarFetch([[QUADRO, TAMPA]])
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      filtrarSemRoteiro()
      expect(screen.queryByText('QD-01 — Quadro')).toBeNull()
      expect(screen.getByText('1 marcado oculto pelo filtro')).toBeTruthy()
      marcar('Marcar Tampa (PED-2)')

      fireEvent.click(screen.getByRole('button', { name: 'Separar 2 itens' }))

      await waitFor(() => expect(corposDoLote).toHaveLength(1))
      expect((corposDoLote[0] as { nos: { estruturaItemId: number }[] }).nos.map((n) => n.estruturaItemId)).toEqual([30, 40])
    })

    it('recusa recarrega, mantém a seleção e mostra a frase na barra; sucesso limpa e recarrega', async () => {
      const frase = 'O material PAR-M8 (Parafuso M8) não está no planejado do nó Tampa.'
      let recusar = true
      const { fetchMock, gets, corposDoLote } = montarFetch([[QUADRO, TAMPA]], undefined, () => (recusar
        ? respostaJson({ erro: 'MaterialForaDoPlanejado', mensagem: frase }, 409)
        : respostaJson([], 201)))
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar Tampa (PED-2)')
      fireEvent.click(screen.getByRole('button', { name: 'Separar 2 itens' }))

      await waitFor(() => expect(gets()).toBe(2))
      await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(frase))
      expect((caixa('Marcar QD-01 — Quadro (PED-1)') as HTMLInputElement).checked).toBe(true)
      expect((caixa('Marcar Tampa (PED-2)') as HTMLInputElement).checked).toBe(true)
      expect(screen.getByRole('button', { name: 'Separar 2 itens' })).toBeTruthy()

      recusar = false
      fireEvent.click(screen.getByRole('button', { name: 'Separar 2 itens' }))

      await waitFor(() => expect(gets()).toBe(3))
      await waitFor(() => expect(screen.queryByRole('button', { name: /^Separar \d/ })).toBeNull())
      expect(corposDoLote).toHaveLength(2)
      expect((caixa('Marcar Tampa (PED-2)') as HTMLInputElement).checked).toBe(false)
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('cartão que sai da lista na recarga sai da seleção, com aviso, sem reescrever o digitado nos outros', async () => {
      const { fetchMock, gets } = montarFetch(
        [[QUADRO, TAMPA], [TAMPA]],
        undefined,
        () => respostaJson({ erro: 'MaterialForaDoPlanejado', mensagem: 'recusado' }, 409),
      )
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar QD-01 — Quadro (PED-1)')
      marcar('Marcar Tampa (PED-2)')
      fireEvent.change(campoDoLote(/PAR-M8/, 'Tampa \\(PED-2\\)'), { target: { value: '1' } })
      fireEvent.click(screen.getByRole('button', { name: 'Separar 2 itens' }))

      await waitFor(() => expect(gets()).toBe(2))
      await waitFor(() => expect(screen.queryByText('QD-01 — Quadro')).toBeNull())
      expect(screen.getByText(/Um item que você tinha marcado não está mais na lista/)).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Separar 1 item' })).toBeTruthy()
      expect((campoDoLote(/PAR-M8/, 'Tampa \\(PED-2\\)') as HTMLInputElement).value).toBe('1')
    })

    it('marcar fecha o painel individual aberto, e o Separar de cada cartão continua', async () => {
      vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

      renderizar()
      await lista()
      fireEvent.click(screen.getByRole('button', { name: 'Separar Tampa (PED-2)' }))
      expect(screen.getByRole('form', { name: 'Separar material' })).toBeTruthy()

      marcar('Marcar QD-01 — Quadro (PED-1)')

      expect(screen.queryByRole('form', { name: 'Separar material' })).toBeNull()
      expect(screen.getByRole('button', { name: 'Separar Tampa (PED-2)' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Separar QD-01 — Quadro (PED-1)' })).toBeTruthy()
    })

    it('dois toques enviam uma vez', async () => {
      let responder: (r: Response) => void = () => {}
      const { fetchMock, corposDoLote } = montarFetch([[QUADRO, TAMPA]], undefined, () => new Promise<Response>((r) => { responder = r }))
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      await lista()
      marcar('Marcar Tampa (PED-2)')
      const botao = screen.getByRole('button', { name: 'Separar 1 item' })

      // Os dois toques no mesmo quadro, antes de o React redesenhar o botão desabilitado: quem segura o segundo
      // é o `ref` do envio, não o `disabled`.
      act(() => {
        botao.click()
        botao.click()
      })

      await waitFor(() => expect(corposDoLote).toHaveLength(1))
      await act(async () => { responder(respostaJson([], 201)) })
      expect(corposDoLote).toHaveLength(1)
    })

    it('"Marcar todos" marca só os visíveis', async () => {
      vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

      renderizar()
      await lista()
      filtrarSemRoteiro()

      fireEvent.click(screen.getByRole('button', { name: 'Marcar todos' }))

      expect((caixa('Marcar Tampa (PED-2)') as HTMLInputElement).checked).toBe(true)
      expect(screen.getByRole('button', { name: 'Desmarcar todos' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Separar 1 item' })).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Limpar' }))
      expect((caixa('Marcar QD-01 — Quadro (PED-1)') as HTMLInputElement).checked).toBe(false)
    })
  })

  describe('carregando, "Nada a separar.", "Nenhum item com esses filtros." e erro', () => {
    it('carregando antes da lista chegar', () => {
      vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

      renderizar()

      expect(screen.getByRole('status').textContent).toBe('Carregando…')
    })

    it('lista vazia diz "Nada a separar." e não mostra filtro', async () => {
      vi.stubGlobal('fetch', montarFetch([[]]).fetchMock)

      renderizar()

      expect(await screen.findByText('Nada a separar.')).toBeTruthy()
      expect(screen.queryByText('Nenhum item com esses filtros.')).toBeNull()
      expect(screen.queryByRole('button', { name: /^Filtrar/ })).toBeNull()
      expect(screen.queryByText('Carregando…')).toBeNull()
    })

    it('filtro sem resultado diz "Nenhum item com esses filtros." e limpa', async () => {
      vi.stubGlobal('fetch', montarFetch([[QUADRO, TAMPA]]).fetchMock)

      renderizar('/separacao?pedido=999')

      expect(await screen.findByText('Nenhum item com esses filtros.')).toBeTruthy()
      expect(screen.queryByText('Nada a separar.')).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
      expect(screen.getByText('QD-01 — Quadro')).toBeTruthy()
    })

    it('falha na carga mostra o erro, sem lista nem vazio', async () => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(respostaJson({}, 400)))

      renderizar()

      expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível carregar a lista de separação.')
      expect(screen.queryByText('Nada a separar.')).toBeNull()
      expect(screen.queryByText('Carregando…')).toBeNull()
    })
  })
})
