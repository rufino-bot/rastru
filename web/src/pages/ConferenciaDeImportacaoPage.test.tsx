// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { ConferenciaDeImportacaoPage } from './ConferenciaDeImportacaoPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { ImportacaoDto, NoDaImportacaoDto, BloqueioDto } from '../api/importacao'

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
})
