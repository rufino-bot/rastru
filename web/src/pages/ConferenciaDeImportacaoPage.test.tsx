// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { ConferenciaDeImportacaoPage } from './ConferenciaDeImportacaoPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { ImportacaoDto, NoDaImportacaoDto, BloqueioDto, SituacaoDoComponenteDto } from '../api/importacao'

afterEach(cleanup)

// Molde de AgrupamentoDetalhePage.test.tsx: o perfil governa o gating de escrita.
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

function no(parcial: Partial<NoDaImportacaoDto> & Pick<NoDaImportacaoDto, 'codigo' | 'descricao'>): NoDaImportacaoDto {
  return {
    registroId: null, componenteId: null, filhoId: null, quantidadePorPai: null,
    origem: 'Bom', pendencias: [], filhos: [], ...parcial,
  }
}

const RAIZ = no({
  registroId: 1,
  codigo: 'CH-100',
  descricao: 'Chassi',
  filhos: [
    no({
      registroId: 2, componenteId: 200, filhoId: 20, codigo: 'SU-200', descricao: 'Suporte',
      quantidadePorPai: 2, pendencias: ['Divergente'],
    }),
    no({
      registroId: 3, componenteId: 300, filhoId: 30, codigo: 'PA-300', descricao: 'Parafuso',
      quantidadePorPai: 8, pendencias: ['SemSolido'],
    }),
    no({
      registroId: 4, componenteId: 400, filhoId: 40, codigo: 'AR-400', descricao: 'Arruela',
      quantidadePorPai: 1, pendencias: ['Divergente', 'SemSolido'],
    }),
  ],
})

const BLOQUEIOS: BloqueioDto[] = [
  { tipo: 'DivergenciaSemEscolha', registroId: 2, componenteId: 200, mensagem: 'SU-200: escolha qual receita vale.' },
  { tipo: 'SemSolido', registroId: 3, componenteId: 300, mensagem: 'PA-300: falta o sólido.' },
  { tipo: 'QuantidadeDaPecaAusente', registroId: null, componenteId: null, mensagem: 'Informe a quantidade da Peça.' },
]

function importacao(parcial: Partial<ImportacaoDto> = {}): ImportacaoDto {
  return {
    id: 5,
    agrupamentoId: 21,
    nomeDoArquivo: 'bom-chassi.xlsx',
    criadoPor: 'Maria PCP',
    criadoEm: '2026-10-02T08:00:00-03:00',
    atualizadoEm: '2026-10-02T09:30:00-03:00',
    versao: 'AAAAAAAAB9E=',
    quantidadeDaPeca: 3,
    requerRelatorioDimensional: false,
    raiz: RAIZ,
    componentes: [],
    bloqueios: BLOQUEIOS,
    ...parcial,
  }
}

type Rota = (init?: RequestInit) => Response | Promise<Response>

/** Mock roteado por "MÉTODO caminho": o `fetchPorRota` de testes/api.ts não separa GET de PUT. */
function montarFetch(rotas: Record<string, Rota>) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const chave = `${init?.method ?? 'GET'} ${String(url).split('?')[0]}`
    const rota = rotas[chave]
    if (!rota) return Promise.reject(new Error(`fetch não esperado no teste: ${chave}`))
    return Promise.resolve(rota(init))
  })
}

function leituras(fetchMock: ReturnType<typeof montarFetch>) {
  return fetchMock.mock.calls.filter((c) => (c[1]?.method ?? 'GET') === 'GET' && String(c[0]) === '/api/importacoes/5')
}

function corpoDa(fetchMock: ReturnType<typeof montarFetch>, metodo: string, caminho: string) {
  const chamada = fetchMock.mock.calls.find((c) => c[1]?.method === metodo && String(c[0]) === caminho)
  expect(chamada).toBeTruthy()
  return JSON.parse(String(chamada![1]!.body)) as Record<string, unknown>
}

function renderizar() {
  return render(
    <MemoryRouter initialEntries={['/importacoes/5']}>
      <Routes>
        <Route path="/importacoes/:id" element={<ConferenciaDeImportacaoPage />} />
        <Route path="/agrupamentos/:id" element={<p>Tela do Agrupamento</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('ConferenciaDeImportacaoPage', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
    perfil = 'PCP'
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    // O stub de `scrollIntoView` do teste de rolagem sai aqui: asserção que falha não o deixa vazar.
    delete (HTMLElement.prototype as { scrollIntoView?: unknown }).scrollIntoView
  })

  it('mostra o estado de carregando enquanto busca', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('erro de carga cai em BannerDeErro e some o carregando', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson({}, 500) }))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Carregando…')).toBeNull()
  })

  it('estrutura que não expandiu mostra o estado vazio com o motivo, sem a árvore', async () => {
    const bloqueio: BloqueioDto = {
      tipo: 'CicloNaReceita', registroId: null, componenteId: null, mensagem: 'A receita de SU-200 criaria um ciclo.',
    }
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({ raiz: null, bloqueios: [bloqueio] })),
    }))

    renderizar()

    expect(await screen.findByText('A estrutura não pôde ser expandida')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Árvore da importação' })).toBeNull()
    // O motivo está na lista de bloqueios da faixa, e Confirmar segue desabilitado.
    expect(screen.getByText('A receita de SU-200 criaria um ciclo.')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Confirmar' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('mostra a árvore e o painel do componente selecionado, a raiz de início', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()

    expect(await screen.findByRole('button', { name: /SU-200 Suporte/ })).toBeTruthy()
    const painel = screen.getByRole('region', { name: 'Componente selecionado' })
    expect(within(painel).getByText('CH-100')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /PA-300 Parafuso/ }))

    expect(within(painel).getByText('PA-300')).toBeTruthy()
    expect(within(painel).getByText('Parafuso')).toBeTruthy()
  })

  it('de lg para cima o painel fica numa coluna ao lado da faixa da Peça e da árvore', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()

    await screen.findByRole('button', { name: /SU-200 Suporte/ })
    // jsdom não roda Tailwind: o que se prende é a declaração das classes; o layout é conferido no
    // navegador. A página é a larga, porque duas colunas não cabem na padrão.
    const pagina = screen.getByRole('heading', { level: 1 }).closest('header')!.parentElement!
    expect(pagina.className.split(/\s+/)).toContain('max-w-7xl')

    const painel = screen.getByRole('region', { name: 'Componente selecionado' })
    const peca = screen.getByRole('region', { name: 'Peça' })
    const estrutura = screen.getByRole('region', { name: 'Estrutura' })
    const grade = painel.closest('[class~="lg:grid"]') as HTMLElement
    expect(grade).not.toBeNull()
    expect(grade.className.split(/\s+/)).toContain('lg:grid-cols-[minmax(0,1fr)_minmax(22rem,28rem)]')
    const celula = (el: HTMLElement) => Array.from(grade.children).find((c) => c.contains(el))!
    expect(celula(painel).className.split(/\s+/)).toContain('lg:col-start-2')
    expect(celula(peca).className.split(/\s+/)).toContain('lg:col-start-1')
    expect(celula(estrutura)).toBe(celula(peca))
    // No DOM, a ordem continua painel, faixa e árvore: é a do celular e a do leitor de tela.
    expect(painel.compareDocumentPosition(peca) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(peca.compareDocumentPosition(estrutura) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('Confirmar_fica_desabilitado_e_lista_os_bloqueios', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()

    const confirmar = (await screen.findByRole('button', { name: 'Confirmar' })) as HTMLButtonElement
    expect(confirmar.disabled).toBe(true)
    const lista = screen.getByRole('list', { name: 'O que falta para confirmar' })
    expect(within(lista).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'SU-200: escolha qual receita vale.',
      'PA-300: falta o sólido.',
      'Informe a quantidade da Peça.',
    ])
  })

  it('sem bloqueio, Confirmar habilita e a lista some', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao({ bloqueios: [] })) }))

    renderizar()

    const confirmar = (await screen.findByRole('button', { name: 'Confirmar' })) as HTMLButtonElement
    expect(confirmar.disabled).toBe(false)
    expect(screen.queryByRole('list', { name: 'O que falta para confirmar' })).toBeNull()
  })

  it('Pilula_de_resumo_seleciona_o_primeiro_no_da_pendencia', async () => {
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()

    // Divergente: Suporte e Arruela; sem sólido: Parafuso e Arruela. O resumo conta por código.
    const painel = await screen.findByRole('region', { name: 'Componente selecionado' })
    fireEvent.click(screen.getByRole('button', { name: '2 sem sólido' }))
    expect(within(painel).getByText('PA-300')).toBeTruthy()
    expect(screen.getByRole('button', { name: /PA-300 Parafuso/ }).getAttribute('aria-current')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: '2 divergências' }))
    expect(within(painel).getByText('SU-200')).toBeTruthy()
    expect(screen.getByRole('button', { name: /SU-200 Suporte/ }).getAttribute('aria-current')).toBe('true')
    expect(screen.getByRole('button', { name: /AR-400 Arruela/ }).getAttribute('aria-current')).toBeNull()
  })

  it('o resumo não tem botão para a pendência que não existe', async () => {
    const semPendencias = no({ registroId: 1, codigo: 'CH-100', descricao: 'Chassi' })
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({ raiz: semPendencias, bloqueios: [] })),
    }))

    renderizar()

    await screen.findByRole('button', { name: 'Confirmar' })
    expect(screen.queryByRole('button', { name: /divergência/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /sem sólido/ })).toBeNull()
  })

  it('Confirmar_com_sucesso_volta_ao_agrupamento', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({ bloqueios: [] })),
      'POST /api/importacoes/5/confirmacao': () => respostaJson({ id: 900 }, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByText('Tela do Agrupamento')).toBeTruthy()
    expect(corpoDa(fetchMock, 'POST', '/api/importacoes/5/confirmacao')).toEqual({ versao: 'AAAAAAAAB9E=' })
  })

  it('Confirmar_com_ImportacaoComBloqueios_rele_o_estado', async () => {
    let lidas = 0
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => {
        lidas += 1
        // A primeira leitura não trazia bloqueio; depois de outra pessoa mexer, traz.
        return respostaJson(lidas === 1 ? importacao({ bloqueios: [] }) : importacao())
      },
      'POST /api/importacoes/5/confirmacao': () => respostaJson({ erro: 'ImportacaoComBloqueios' }, 400),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect(await screen.findByRole('list', { name: 'O que falta para confirmar' })).toBeTruthy()
    expect(leituras(fetchMock)).toHaveLength(2)
    expect(screen.getByRole('alert').textContent).toContain('bloqueios')
    expect(screen.queryByText('Tela do Agrupamento')).toBeNull()
  })

  it('ReceitaDoCatalogoMudou_mostra_a_mensagem_e_rele', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({ bloqueios: [] })),
      'POST /api/importacoes/5/confirmacao': () =>
        respostaJson({ erro: 'ReceitaDoCatalogoMudou', mensagem: 'texto do servidor' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect((await screen.findByRole('alert')).textContent)
      .toBe('A receita de um Componente do catálogo mudou desde a conferência; a tela foi atualizada. Confira as escolhas e confirme de novo.')
    await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
    expect(screen.queryByText('Tela do Agrupamento')).toBeNull()
  })

  it('Confirmar com a versão velha rele e avisa', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({ bloqueios: [] })),
      'POST /api/importacoes/5/confirmacao': () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Confirmar' }))

    expect((await screen.findByRole('alert')).textContent)
      .toBe('Outra pessoa alterou esta importação; a tela foi atualizada.')
    await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
  })

  it('Versao_velha_rele_e_avisa', async () => {
    let lidas = 0
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => {
        lidas += 1
        return respostaJson(lidas === 1
          ? importacao()
          : importacao({ versao: 'AAAAAAAAB9F=', raiz: no({ ...RAIZ, filhos: [no({
            registroId: 2, componenteId: 200, filhoId: 20, codigo: 'SU-200', descricao: 'Suporte', quantidadePorPai: 7,
          })] }) }))
      },
      'PUT /api/importacoes/5/filhos/20': () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = await screen.findByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(campo, { target: { value: '5' } })
    fireEvent.blur(campo)

    expect((await screen.findByRole('alert')).textContent)
      .toBe('Outra pessoa alterou esta importação; a tela foi atualizada.')
    // A tela mostra o que a releitura trouxe.
    await waitFor(() => {
      expect((screen.getByLabelText('Quantidade por pai de Suporte') as HTMLInputElement).value).toBe('7')
    })
    expect(leituras(fetchMock)).toHaveLength(2)
  })

  it('alterar a quantidade de um filho envia a versão e troca o estado pela resposta', async () => {
    const respondida = importacao({
      versao: 'AAAAAAAAB9G=',
      raiz: no({ ...RAIZ, filhos: [no({
        registroId: 2, componenteId: 200, filhoId: 20, codigo: 'SU-200', descricao: 'Suporte', quantidadePorPai: 5,
      })] }),
      bloqueios: [],
    })
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5/filhos/20': () => respostaJson(respondida),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = await screen.findByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(campo, { target: { value: '5' } })
    fireEvent.blur(campo)

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Confirmar' }) as HTMLButtonElement).disabled).toBe(false)
    })
    expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5/filhos/20')).toEqual({ versao: 'AAAAAAAAB9E=', quantidade: 5 })
    expect(leituras(fetchMock)).toHaveLength(1)
  })

  it('Descartar_pede_confirmacao_e_volta_ao_agrupamento', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'DELETE /api/importacoes/5': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Descartar' }))

    // Nada foi apagado ainda: só o diálogo abriu.
    const dialogo = screen.getByRole('dialog')
    expect(fetchMock.mock.calls.some((c) => c[1]?.method === 'DELETE')).toBe(false)
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }))
    expect(screen.queryByRole('dialog')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Descartar' }))

    expect(await screen.findByText('Tela do Agrupamento')).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => c[1]?.method === 'DELETE' && String(c[0]) === '/api/importacoes/5')).toBe(true)
  })

  it('falha ao descartar mostra o erro e fica na tela', async () => {
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'DELETE /api/importacoes/5': () => respostaJson({}, 500),
    }))

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Descartar' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Descartar' }))

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Tela do Agrupamento')).toBeNull()
  })

  it('Alterar_quantidade_da_peca_envia_a_versao_atual', async () => {
    const respondida = importacao({ versao: 'AAAAAAAAB9H=', quantidadeDaPeca: 6 })
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5': () => respostaJson(respondida),
      'PUT /api/importacoes/5/filhos/20': () => respostaJson(importacao()),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = (await screen.findByLabelText('Quantidade da Peça')) as HTMLInputElement
    expect(campo.value).toBe('3')
    fireEvent.change(campo, { target: { value: '6' } })
    fireEvent.blur(campo)

    await waitFor(() => expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5')).toEqual({
      versao: 'AAAAAAAAB9E=', quantidadeDaPeca: 6, requerRelatorioDimensional: false,
    }))
    // A resposta vira o estado: a próxima escrita usa a versão nova.
    await waitFor(() => expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).value).toBe('6'))
    const filho = screen.getByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(filho, { target: { value: '9' } })
    fireEvent.blur(filho)
    await waitFor(() => expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5/filhos/20').versao).toBe('AAAAAAAAB9H='))
  })

  it('marcar o Relatório Dimensional envia a flag com a quantidade atual', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5': () => respostaJson(importacao({ requerRelatorioDimensional: true })),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByLabelText('Requer relatório dimensional'))

    await waitFor(() => expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5')).toEqual({
      versao: 'AAAAAAAAB9E=', quantidadeDaPeca: 3, requerRelatorioDimensional: true,
    }))
    await waitFor(() => expect((screen.getByLabelText('Requer relatório dimensional') as HTMLInputElement).checked).toBe(true))
  })

  it('quantidade da Peça apagada vai como nulo', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5': () => respostaJson(importacao({ quantidadeDaPeca: null })),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = await screen.findByLabelText('Quantidade da Peça')
    fireEvent.change(campo, { target: { value: '' } })
    fireEvent.blur(campo)

    await waitFor(() => expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5').quantidadeDaPeca).toBeNull())
  })

  it('perfil sem escrita não vê Confirmar nem Descartar e não edita nada', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()

    await screen.findByRole('button', { name: /SU-200 Suporte/ })
    expect(screen.queryByRole('button', { name: 'Confirmar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Descartar' })).toBeNull()
    expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByLabelText('Quantidade por pai de Suporte')).toBeNull()
  })

  // Fix round 1 (Important 1): o campo remonta pela chave, e a chave vinha do valor do servidor — a
  // escrita que falha, ou cuja releitura devolve o MESMO valor, deixava o digitado na tela.
  it('409 cuja releitura traz o mesmo valor devolve o campo ao valor do servidor', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5/filhos/20': () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = await screen.findByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(campo, { target: { value: '5' } })
    fireEvent.blur(campo)

    await screen.findByText('Outra pessoa alterou esta importação; a tela foi atualizada.')
    await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
    expect((screen.getByLabelText('Quantidade por pai de Suporte') as HTMLInputElement).value).toBe('2')
  })

  it.each([400, 500])('falha %i ao salvar a quantidade de um filho devolve o campo ao valor do servidor', async (status) => {
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5/filhos/20': () => respostaJson({}, status),
    }))

    renderizar()
    const campo = await screen.findByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(campo, { target: { value: '5' } })
    fireEvent.blur(campo)

    await screen.findByRole('alert')
    expect((screen.getByLabelText('Quantidade por pai de Suporte') as HTMLInputElement).value).toBe('2')
  })

  it.each([400, 500])('falha %i ao salvar a quantidade da Peça devolve o campo ao valor do servidor', async (status) => {
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5': () => respostaJson({}, status),
    }))

    renderizar()
    const campo = await screen.findByLabelText('Quantidade da Peça')
    fireEvent.change(campo, { target: { value: '6' } })
    fireEvent.blur(campo)

    await screen.findByRole('alert')
    expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).value).toBe('3')
  })

  it('409 ao salvar a quantidade da Peça, com a releitura igual, devolve o campo ao valor do servidor', async () => {
    const fetchMock = montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5': () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    const campo = await screen.findByLabelText('Quantidade da Peça')
    fireEvent.change(campo, { target: { value: '6' } })
    fireEvent.blur(campo)

    await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
    await screen.findByRole('alert')
    expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).value).toBe('3')
  })

  // Fix round 1 (Important 2): nó só do catálogo (sem registro) também é marcado.
  it('pílula de resumo cujo primeiro nó é do catálogo marca esse nó', async () => {
    const doCatalogo = no({
      componenteId: 500, codigo: 'CA-500', descricao: 'Calço', quantidadePorPai: 1,
      origem: 'Catalogo', pendencias: ['Inativo'],
    })
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({
        raiz: no({ ...RAIZ, filhos: [...RAIZ.filhos, doCatalogo] }),
      })),
    }))

    renderizar()
    const painel = await screen.findByRole('region', { name: 'Componente selecionado' })
    fireEvent.click(screen.getByRole('button', { name: '1 inativo' }))

    expect(within(painel).getByText('CA-500')).toBeTruthy()
    expect(screen.getByRole('button', { name: /CA-500 Calço/ }).getAttribute('aria-current')).toBe('true')
    expect(screen.queryAllByRole('button', { current: true })).toHaveLength(1)
  })

  it('clicar num nó do catálogo o marca na árvore', async () => {
    const doCatalogo = no({
      componenteId: 500, codigo: 'CA-500', descricao: 'Calço', quantidadePorPai: 1, origem: 'Catalogo',
    })
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao({
        raiz: no({ ...RAIZ, filhos: [...RAIZ.filhos, doCatalogo] }),
      })),
    }))

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: /CA-500 Calço/ }))

    expect(screen.getByRole('button', { name: /CA-500 Calço/ }).getAttribute('aria-current')).toBe('true')
    expect(within(screen.getByRole('region', { name: 'Componente selecionado' })).getByText('CA-500')).toBeTruthy()
  })

  it('seleção que some na releitura volta à raiz, no painel e na árvore', async () => {
    const semOParafuso = importacao({
      versao: 'AAAAAAAAB9I=',
      raiz: no({ ...RAIZ, filhos: [RAIZ.filhos[0]] }),
    })
    vi.stubGlobal('fetch', montarFetch({
      'GET /api/importacoes/5': () => respostaJson(importacao()),
      'PUT /api/importacoes/5/filhos/20': () => respostaJson(semOParafuso),
    }))

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: /PA-300 Parafuso/ }))
    const campo = screen.getByLabelText('Quantidade por pai de Suporte')
    fireEvent.change(campo, { target: { value: '9' } })
    fireEvent.blur(campo)

    await waitFor(() => expect(screen.queryByRole('button', { name: /PA-300 Parafuso/ })).toBeNull())
    expect(within(screen.getByRole('region', { name: 'Componente selecionado' })).getByText('CH-100')).toBeTruthy()
    const marcadas = screen.getAllByRole('button', { current: true })
    expect(marcadas).toHaveLength(1)
    expect(marcadas[0].getAttribute('aria-label')).toBe('CH-100 Chassi')
  })

  it('clicar de novo na mesma pílula de resumo rola de novo até o primeiro nó', async () => {
    const rolar = vi.fn()
    HTMLElement.prototype.scrollIntoView = rolar
    vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))

    renderizar()
    const pilula = await screen.findByRole('button', { name: '2 sem sólido' })
    fireEvent.click(pilula)
    expect(rolar).toHaveBeenCalledTimes(1)
    fireEvent.click(pilula)
    expect(rolar).toHaveBeenCalledTimes(2)
  })

  describe('painel do Componente selecionado', () => {
    const DIVERGENTE: SituacaoDoComponenteDto = {
      registroId: 2, codigoLido: 'SU-200', descricaoLida: 'Suporte', componenteId: 200,
      codigoDoCatalogo: 'SU-200', descricaoDoCatalogo: 'Suporte', tipo: 'Montagem', ativo: true,
      temSolido: true, temSolidoPendente: false, nomeDoSolido: 'suporte.stl', tamanhoDoSolidoEmBytes: 684,
      codigoNovo: null, descricaoNova: null, tipoNovo: null, divergente: true, escolhaDeReceita: null,
      comparativo: [{ codigo: 'PA-300', descricao: 'Parafuso', noCatalogo: 4, noBom: 8, situacao: 'QuantidadeMuda' }],
      efeitoDeManterCatalogo: { retira: 1, traz: 0 }, naArvoreFinal: true,
    }
    const CATALOGO = () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 })

    it('Escolher_a_receita_escreve_e_a_resposta_vira_o_estado', async () => {
      const escolhida = importacao({
        versao: 'AAAAAAAAB9F=',
        componentes: [{ ...DIVERGENTE, escolhaDeReceita: 'Catalogo' }],
      })
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [DIVERGENTE] })),
        'GET /api/componentes': CATALOGO,
        'PUT /api/importacoes/5/componentes/2': () => respostaJson(escolhida),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: /SU-200 Suporte/ }))
      const manter = await screen.findByRole('radio', { name: 'Manter a receita do catálogo' }) as HTMLInputElement
      expect(manter.checked).toBe(false)
      fireEvent.click(manter)

      await waitFor(() => expect(
        (screen.getByRole('radio', { name: 'Manter a receita do catálogo' }) as HTMLInputElement).checked,
      ).toBe(true))
      expect(corpoDa(fetchMock, 'PUT', '/api/importacoes/5/componentes/2')).toMatchObject({
        versao: 'AAAAAAAAB9E=', componenteId: 200, escolhaDeReceita: 'Catalogo',
      })
    })

    it('escrita do painel com a versão velha rele e avisa no próprio painel', async () => {
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [DIVERGENTE] })),
        'GET /api/componentes': CATALOGO,
        'PUT /api/importacoes/5/componentes/2': () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: /SU-200 Suporte/ }))
      fireEvent.click(await screen.findByRole('radio', { name: 'Usar a receita importada' }))

      const painel = screen.getByRole('region', { name: 'Componente selecionado' })
      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('Outra pessoa alterou esta importação; a tela foi atualizada.')
      await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })
  })

  describe('escritas em fila', () => {
    const NOVO: SituacaoDoComponenteDto = {
      registroId: 3, codigoLido: 'PA-300', descricaoLida: 'Parafuso', componenteId: null,
      codigoDoCatalogo: null, descricaoDoCatalogo: null, tipo: null, ativo: null,
      temSolido: false, temSolidoPendente: false, nomeDoSolido: null, tamanhoDoSolidoEmBytes: null,
      codigoNovo: 'PA-300', descricaoNova: 'Parafuso', tipoNovo: 'Fabricado', divergente: false,
      escolhaDeReceita: null, comparativo: [], efeitoDeManterCatalogo: null, naArvoreFinal: true,
    }
    const CATALOGO = () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 })

    /** Uma resposta que o teste solta quando quer: a escrita fica em voo até lá. */
    function pendente() {
      let soltar!: (r: Response) => void
      const promessa = new Promise<Response>((r) => { soltar = r })
      return { promessa, soltar }
    }

    function puts(fetchMock: ReturnType<typeof montarFetch>, caminho: string) {
      return fetchMock.mock.calls
        .filter((c) => c[1]?.method === 'PUT' && String(c[0]) === caminho)
        .map((c) => JSON.parse(String(c[1]!.body)) as Record<string, unknown>)
    }

    async function selecionarParafuso() {
      fireEvent.click(await screen.findByRole('button', { name: /PA-300 Parafuso/ }))
      return screen.getByRole('region', { name: 'Componente selecionado' })
    }

    it('a segunda escrita do painel espera a primeira e sai com a versão e os dados que ela devolveu', async () => {
      const primeira = pendente()
      const depoisDaPrimeira = importacao({ versao: 'AAAAAAAAB9F=', componentes: [{ ...NOVO, codigoNovo: 'PA-301' }] })
      const depoisDaSegunda = importacao({
        versao: 'AAAAAAAAB9G=', componentes: [{ ...NOVO, codigoNovo: 'PA-301', descricaoNova: 'Parafuso sextavado' }],
      })
      let escritas = 0
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [NOVO] })),
        'GET /api/componentes': CATALOGO,
        'PUT /api/importacoes/5/componentes/3': () => {
          escritas += 1
          return escritas === 1 ? primeira.promessa : respostaJson(depoisDaSegunda)
        },
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      const painel = await selecionarParafuso()

      const codigo = within(painel).getByLabelText('Código')
      fireEvent.change(codigo, { target: { value: 'PA-301' } })
      fireEvent.blur(codigo)
      const descricao = within(painel).getByLabelText('Descrição')
      fireEvent.change(descricao, { target: { value: 'Parafuso sextavado' } })
      fireEvent.blur(descricao)

      // Com a primeira em voo, a segunda não sai: sairia com a versão velha e voltaria 409.
      await waitFor(() => expect(puts(fetchMock, '/api/importacoes/5/componentes/3')).toHaveLength(1))
      await new Promise((r) => setTimeout(r, 0))
      expect(puts(fetchMock, '/api/importacoes/5/componentes/3')).toHaveLength(1)

      primeira.soltar(respostaJson(depoisDaPrimeira))

      await waitFor(() => expect(puts(fetchMock, '/api/importacoes/5/componentes/3')).toHaveLength(2))
      expect(puts(fetchMock, '/api/importacoes/5/componentes/3')[1]).toMatchObject({
        versao: 'AAAAAAAAB9F=', codigoNovo: 'PA-301', descricaoNova: 'Parafuso sextavado',
      })
      await waitFor(() => expect((within(painel).getByLabelText('Descrição') as HTMLInputElement).value)
        .toBe('Parafuso sextavado'))
      expect(screen.queryByRole('alert')).toBeNull()
      expect(leituras(fetchMock)).toHaveLength(1)
    })

    it('o reimport enviado com uma escrita em voo sai com a versão que ela devolveu', async () => {
      const primeira = pendente()
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        'PUT /api/importacoes/5/filhos/20': () => primeira.promessa,
        'POST /api/importacoes/5/arquivo': () => respostaJson(importacao({ versao: 'AAAAAAAAB9G=' })),
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: 'Reimportar' }))
      fireEvent.change(screen.getByLabelText(/Arquivo do BOM/), { target: { files: [new File(['a;b'], 'bom.csv')] } })

      const campo = screen.getByLabelText('Quantidade por pai de Suporte')
      fireEvent.change(campo, { target: { value: '5' } })
      fireEvent.blur(campo)
      await waitFor(() => expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'PUT')).toHaveLength(1))
      fireEvent.click(within(screen.getByRole('form', { name: 'Reimportar BOM' })).getByRole('button', { name: 'Reimportar' }))
      await new Promise((r) => setTimeout(r, 0))
      expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST')).toHaveLength(0)

      primeira.soltar(respostaJson(importacao({ versao: 'AAAAAAAAB9F=' })))

      await waitFor(() => expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST')).toHaveLength(1))
      const post = fetchMock.mock.calls.find((c) => c[1]?.method === 'POST')!
      expect((post[1]!.body as FormData).get('versao')).toBe('AAAAAAAAB9F=')
      await waitFor(() => expect(screen.queryByRole('form', { name: 'Reimportar BOM' })).toBeNull())
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('o texto do Componente novo cuja escrita falhou volta ao valor do servidor, e o erro aparece no painel', async () => {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [NOVO] })),
        'GET /api/componentes': CATALOGO,
        'PUT /api/importacoes/5/componentes/3': () => respostaJson({}, 500),
      }))
      renderizar()
      const painel = await selecionarParafuso()

      const codigo = within(painel).getByLabelText('Código') as HTMLInputElement
      fireEvent.change(codigo, { target: { value: 'PA-301' } })
      fireEvent.blur(codigo)

      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
      await waitFor(() => expect((within(painel).getByLabelText('Código') as HTMLInputElement).value).toBe('PA-300'))
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })
  })

  describe('erros dentro do painel', () => {
    const NOVO: SituacaoDoComponenteDto = {
      registroId: 3, codigoLido: 'PA-300', descricaoLida: 'Parafuso', componenteId: null,
      codigoDoCatalogo: null, descricaoDoCatalogo: null, tipo: null, ativo: null,
      temSolido: false, temSolidoPendente: false, nomeDoSolido: null, tamanhoDoSolidoEmBytes: null,
      codigoNovo: 'PA-300', descricaoNova: 'Parafuso', tipoNovo: 'Fabricado', divergente: false,
      escolhaDeReceita: null, comparativo: [], efeitoDeManterCatalogo: null, naArvoreFinal: true,
    }
    const CATALOGO = () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 })

    async function enviarSolido(resposta: () => Response) {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [NOVO] })),
        'GET /api/componentes': CATALOGO,
        'POST /api/importacoes/5/componentes/3/solido': resposta,
      }))
      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: /PA-300 Parafuso/ }))
      const painel = screen.getByRole('region', { name: 'Componente selecionado' })
      fireEvent.change(within(painel).getByLabelText(/sólido/i), {
        target: { files: [new File([new Uint8Array(684)], 'parafuso.stl')] },
      })
      return painel
    }

    it('a falha do upload do STL aparece no campo do sólido, e não no topo da tela', async () => {
      const painel = await enviarSolido(() => respostaJson({ erro: 'Arquivo STL invalido.' }, 400))

      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('Não foi possível enviar o sólido. Envie um arquivo .stl de até 16 MiB.')
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })

    it('o upload com a versão velha avisa no campo do sólido', async () => {
      const painel = await enviarSolido(() => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409))

      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('Outra pessoa alterou esta importação; a tela foi atualizada.')
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })

    it('a escrita do painel que falha mostra o erro no painel', async () => {
      const DIVERGENTE: SituacaoDoComponenteDto = {
        ...NOVO, registroId: 2, codigoLido: 'SU-200', descricaoLida: 'Suporte', componenteId: 200,
        codigoDoCatalogo: 'SU-200', descricaoDoCatalogo: 'Suporte', tipo: 'Montagem', ativo: true,
        codigoNovo: null, descricaoNova: null, tipoNovo: null, divergente: true,
      }
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao({ componentes: [DIVERGENTE] })),
        'GET /api/componentes': CATALOGO,
        'PUT /api/importacoes/5/componentes/2': () => respostaJson({}, 500),
      }))
      renderizar()
      fireEvent.click(await screen.findByRole('button', { name: /SU-200 Suporte/ }))
      const painel = screen.getByRole('region', { name: 'Componente selecionado' })
      fireEvent.click(within(painel).getByRole('radio', { name: 'Usar a receita importada' }))

      expect((await within(painel).findByRole('alert')).textContent)
        .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })

    it.each([
      ['1,23456', 'Digite um número com no máximo quatro casas decimais.'],
      ['100000000000000', 'No máximo 99.999.999.999.999,9999.'],
    ])('quantidade por pai %s não sai: volta ao valor e diz por quê', async (digitado, motivo) => {
      const fetchMock = montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      const campo = await screen.findByLabelText('Quantidade por pai de Suporte') as HTMLInputElement

      fireEvent.change(campo, { target: { value: digitado } })
      fireEvent.blur(campo)

      expect(campo.value).toBe('2')
      expect(screen.getByText(motivo)).toBeTruthy()
      expect(campo.getAttribute('aria-describedby')).toBe(screen.getByText(motivo).id)
      expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'PUT')).toHaveLength(0)
    })

    it('quantidade da Peça com casas demais não sai: volta ao valor e diz por quê', async () => {
      const fetchMock = montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      const campo = await screen.findByLabelText('Quantidade da Peça') as HTMLInputElement

      fireEvent.change(campo, { target: { value: '0,00001' } })
      fireEvent.blur(campo)

      expect(campo.value).toBe('3')
      expect(screen.getByText('Digite um número com no máximo quatro casas decimais.')).toBeTruthy()
      expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'PUT')).toHaveLength(0)
    })
  })

  describe('Reimportar', () => {
    const ROTA = 'POST /api/importacoes/5/arquivo'

    /** Escolhe um arquivo no campo do painel. */
    function escolherArquivo(nome: string) {
      const arquivo = new File(['a;b'], nome)
      fireEvent.change(screen.getByLabelText(/Arquivo do BOM/), { target: { files: [arquivo] } })
      return arquivo
    }

    function formularioDoPainel() {
      return screen.getByRole('form', { name: 'Reimportar BOM' })
    }

    function enviar() {
      fireEvent.click(within(formularioDoPainel()).getByRole('button', { name: 'Reimportar' }))
    }

    async function abrirPainel() {
      fireEvent.click(await screen.findByRole('button', { name: 'Reimportar' }))
    }

    it('Reimportar_so_aparece_para_quem_escreve', async () => {
      vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))
      const { unmount } = renderizar()
      expect(await screen.findByRole('button', { name: 'Reimportar' })).toBeTruthy()
      unmount()

      perfil = 'Operador'
      renderizar()
      await screen.findByRole('button', { name: /SU-200 Suporte/ })
      expect(screen.queryByRole('button', { name: 'Reimportar' })).toBeNull()
    })

    it('o botão abre o painel com o campo de arquivo, e o painel fica na faixa da Peça', async () => {
      vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))
      renderizar()

      await abrirPainel()

      const faixa = screen.getByRole('region', { name: 'Peça' })
      expect(within(faixa).getByRole('form', { name: 'Reimportar BOM' })).toBeTruthy()
      expect((within(formularioDoPainel()).getByRole('button', { name: 'Reimportar' }) as HTMLButtonElement).disabled)
        .toBe(true)
      // O botão da faixa some enquanto o painel está aberto: o único "Reimportar" é o do envio.
      expect(screen.getAllByRole('button', { name: 'Reimportar' })).toHaveLength(1)
    })

    it('Reimportar_envia_o_arquivo_com_a_versao_atual_e_substitui_o_estado', async () => {
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        [ROTA]: () => respostaJson(importacao({ nomeDoArquivo: 'bom-novo.xlsx', versao: 'AAAAAAAAB9F=' })),
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      await abrirPainel()
      const arquivo = escolherArquivo('bom-novo.xlsx')

      enviar()

      await waitFor(() => expect(screen.queryByRole('form', { name: 'Reimportar BOM' })).toBeNull())
      const chamada = fetchMock.mock.calls.find((c) => c[1]?.method === 'POST' && String(c[0]) === '/api/importacoes/5/arquivo')!
      const corpo = chamada[1]!.body as FormData
      expect(corpo.get('arquivo')).toBe(arquivo)
      expect(corpo.get('versao')).toBe('AAAAAAAAB9E=')
      expect(screen.getByText(/bom-novo\.xlsx · Maria PCP/)).toBeTruthy()
      expect(screen.queryByText(/bom-chassi\.xlsx/)).toBeNull()
    })

    it('Reimportar_com_BomInvalido_mostra_as_linhas_no_painel_e_mantem_a_tela', async () => {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        [ROTA]: () => respostaJson(
          { erro: 'BomInvalido', mensagem: 'Linha 3: quantidade invalida.\nLinha 9: codigo vazio.' }, 400,
        ),
      }))
      renderizar()
      await abrirPainel()
      escolherArquivo('bom-ruim.csv')

      enviar()

      const alerta = await within(formularioDoPainel()).findByRole('alert')
      expect(alerta.textContent).toContain('O arquivo tem problemas:')
      expect(within(alerta).getAllByRole('listitem').map((li) => li.textContent))
        .toEqual(['Linha 3: quantidade invalida.', 'Linha 9: codigo vazio.'])
      // O rascunho segue como estava, e o erro mora só no painel.
      expect(screen.getByText(/bom-chassi\.xlsx · Maria PCP/)).toBeTruthy()
      expect(screen.getAllByRole('alert')).toHaveLength(1)
      expect((within(formularioDoPainel()).getByRole('button', { name: 'Reimportar' }) as HTMLButtonElement).disabled)
        .toBe(true)
    })

    it('Reimportar_com_versao_velha_rele_e_avisa_no_painel', async () => {
      let lidas = 0
      const fetchMock = montarFetch({
        'GET /api/importacoes/5': () => {
          lidas += 1
          return respostaJson(lidas === 1 ? importacao() : importacao({ versao: 'AAAAAAAAB9F=', quantidadeDaPeca: 9 }))
        },
        [ROTA]: () => respostaJson({ erro: 'ImportacaoDesatualizada' }, 409),
      })
      vi.stubGlobal('fetch', fetchMock)
      renderizar()
      await abrirPainel()
      escolherArquivo('bom-novo.xlsx')

      enviar()

      const alerta = await within(formularioDoPainel()).findByRole('alert')
      expect(alerta.textContent).toBe('Outra pessoa alterou esta importação; a tela foi atualizada.')
      await waitFor(() => expect(leituras(fetchMock)).toHaveLength(2))
      // O aviso mora no painel, onde o usuário está olhando, e não duplica no topo da tela.
      expect(screen.getAllByRole('alert')).toHaveLength(1)
      await waitFor(() => expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).value).toBe('9'))
      expect((within(formularioDoPainel()).getByRole('button', { name: 'Reimportar' }) as HTMLButtonElement).disabled)
        .toBe(true)

      // O envio seguinte já sai com a versão relida.
      escolherArquivo('bom-novo.xlsx')
      enviar()
      await waitFor(() => expect(fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST')).toHaveLength(2))
      const posts = fetchMock.mock.calls.filter((c) => c[1]?.method === 'POST')
      expect((posts[0][1]!.body as FormData).get('versao')).toBe('AAAAAAAAB9E=')
      expect((posts[1][1]!.body as FormData).get('versao')).toBe('AAAAAAAAB9F=')
    })

    it('Reimportar_com_outra_falha_mostra_a_mensagem_no_painel', async () => {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        [ROTA]: () => respostaJson({}, 403),
      }))
      renderizar()
      await abrirPainel()
      escolherArquivo('bom.xlsx')

      enviar()

      expect((await within(formularioDoPainel()).findByRole('alert')).textContent)
        .toBe('Seu perfil não tem permissão para esta ação.')
      expect(screen.getAllByRole('alert')).toHaveLength(1)
    })

    it('com o envio em voo, a faixa, a árvore e o Descartar ficam travados', async () => {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        [ROTA]: () => new Promise<Response>(() => {}) as unknown as Response,
      }))
      renderizar()
      await abrirPainel()
      escolherArquivo('bom.xlsx')

      enviar()

      await screen.findByRole('button', { name: 'Reimportando…' })
      expect((screen.getByLabelText('Quantidade da Peça') as HTMLInputElement).disabled).toBe(true)
      expect((screen.getByLabelText('Quantidade por pai de Suporte') as HTMLInputElement).disabled).toBe(true)
      expect((screen.getByRole('button', { name: 'Descartar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('Fechar_o_painel_de_reimportar_devolve_o_foco_ao_botao', async () => {
      vi.stubGlobal('fetch', montarFetch({ 'GET /api/importacoes/5': () => respostaJson(importacao()) }))
      renderizar()
      await abrirPainel()

      fireEvent.click(within(formularioDoPainel()).getByRole('button', { name: 'Cancelar' }))

      expect(screen.queryByRole('form', { name: 'Reimportar BOM' })).toBeNull()
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Reimportar' }))
    })

    it('fechar por sucesso também devolve o foco ao botão', async () => {
      vi.stubGlobal('fetch', montarFetch({
        'GET /api/importacoes/5': () => respostaJson(importacao()),
        [ROTA]: () => respostaJson(importacao({ nomeDoArquivo: 'bom-novo.xlsx' })),
      }))
      renderizar()
      await abrirPainel()
      escolherArquivo('bom-novo.xlsx')

      enviar()

      await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Reimportar' })))
    })
  })
})
