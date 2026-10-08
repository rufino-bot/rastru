// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { PainelDoComponenteDaImportacao } from './PainelDoComponenteDaImportacao'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaBinaria, respostaJson } from '../testes/api'
import type { ImportacaoDto, NoDaImportacaoDto, SituacaoDoComponenteDto } from '../api/importacao'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  perfil = 'PCP'
})

function no(parcial: Partial<NoDaImportacaoDto> & Pick<NoDaImportacaoDto, 'codigo' | 'descricao'>): NoDaImportacaoDto {
  return {
    registroId: null, componenteId: null, filhoId: null, quantidadePorPai: null,
    origem: 'Bom', pendencias: [], filhos: [], ...parcial,
  }
}

function situacao(parcial: Partial<SituacaoDoComponenteDto> & Pick<SituacaoDoComponenteDto, 'registroId'>): SituacaoDoComponenteDto {
  return {
    codigoLido: null, descricaoLida: '', componenteId: null, codigoDoCatalogo: null, descricaoDoCatalogo: null,
    tipo: null, ativo: null, temSolido: false, temSolidoPendente: false, nomeDoSolido: null,
    tamanhoDoSolidoEmBytes: null, codigoNovo: null, descricaoNova: null, tipoNovo: null, divergente: false,
    escolhaDeReceita: null, comparativo: [], efeitoDeManterCatalogo: null, naArvoreFinal: true, ...parcial,
  }
}

/** Registro 2 casado e divergente, 3 novo, 4 casado com sólido no catálogo, 5 com sólido pendente. */
const SITUACOES: SituacaoDoComponenteDto[] = [
  situacao({
    registroId: 2, codigoLido: 'SU-200', descricaoLida: 'Suporte do BOM', componenteId: 200,
    codigoDoCatalogo: 'SU-200', descricaoDoCatalogo: 'Suporte', tipo: 'Montagem', ativo: true,
    divergente: true,
    comparativo: [
      { codigo: 'PA-300', descricao: 'Parafuso', noCatalogo: 8, noBom: 8, situacao: 'Igual' },
      { codigo: 'AR-400', descricao: 'Arruela', noCatalogo: null, noBom: 2, situacao: 'Entra' },
    ],
    efeitoDeManterCatalogo: { retira: 3, traz: 2 },
  }),
  situacao({
    registroId: 3, codigoLido: 'PA-300', descricaoLida: 'Parafuso', codigoNovo: 'PA-300',
    descricaoNova: 'Parafuso', tipoNovo: 'Fabricado',
  }),
  situacao({
    registroId: 4, codigoLido: 'CA-500', descricaoLida: 'Calço', componenteId: 500,
    codigoDoCatalogo: 'CA-500', descricaoDoCatalogo: 'Calço', tipo: 'Fabricado', ativo: true,
    temSolido: true, nomeDoSolido: 'calco.stl', tamanhoDoSolidoEmBytes: 684,
  }),
  situacao({
    registroId: 5, codigoLido: 'PI-600', descricaoLida: 'Pino', componenteId: 600,
    codigoDoCatalogo: 'PI-600', descricaoDoCatalogo: 'Pino', tipo: 'Fabricado', ativo: true,
    temSolido: true, temSolidoPendente: true, nomeDoSolido: 'pino-novo.stl', tamanhoDoSolidoEmBytes: 684,
  }),
]

const RAIZ = no({
  registroId: 1,
  codigo: 'CH-100',
  descricao: 'Chassi',
  filhos: [
    no({ registroId: 2, componenteId: 200, filhoId: 20, codigo: 'SU-200', descricao: 'Suporte', pendencias: ['Divergente'] }),
    no({ registroId: 3, filhoId: 30, codigo: 'PA-300', descricao: 'Parafuso', pendencias: ['Novo', 'SemSolido'] }),
    no({ registroId: 4, componenteId: 500, filhoId: 40, codigo: 'CA-500', descricao: 'Calço' }),
    no({ registroId: 5, componenteId: 600, filhoId: 50, codigo: 'PI-600', descricao: 'Pino' }),
    no({
      registroId: null, componenteId: 700, codigo: 'RO-700', descricao: 'Rolamento do catálogo',
      origem: 'Catalogo', pendencias: ['SemSolido'],
    }),
  ],
})

function importacao(parcial: Partial<ImportacaoDto> = {}): ImportacaoDto {
  return {
    id: 5, agrupamentoId: 21, nomeDoArquivo: 'bom.xlsx', criadoPor: 'Maria', criadoEm: '2026-10-02T08:00:00-03:00',
    atualizadoEm: '2026-10-02T08:00:00-03:00', versao: 'AAAAAAAAB9E=', quantidadeDaPeca: 1,
    requerRelatorioDimensional: false, raiz: RAIZ, componentes: SITUACOES, bloqueios: [], ...parcial,
  }
}

type Rota = (init?: RequestInit) => Response | Promise<Response>

const CATALOGO_VAZIO = () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 })

/**
 * O `SeletorComBusca` lista o catálogo ao montar, então toda montagem com casamento faz esse GET: a
 * rota padrão o atende, e `chamadas` o descarta para a asserção olhar só o que o painel mandou.
 */
function montarFetch(rotas: Record<string, Rota>) {
  const todas: Record<string, Rota> = { 'GET /api/componentes': CATALOGO_VAZIO, ...rotas }
  const mock = vi.fn((url: string | URL, init?: RequestInit) => {
    const chave = `${init?.method ?? 'GET'} ${String(url).split('?')[0]}`
    const rota = todas[chave]
    if (!rota) return Promise.reject(new Error(`fetch não esperado no teste: ${chave}`))
    return Promise.resolve(rota(init))
  })
  vi.stubGlobal('fetch', mock)
  return mock
}

/**
 * O `escrever` da página, reduzido ao que o painel precisa: roda a ação com o rascunho mais recente
 * (o que `atual` devolve na hora em que ela roda) e descarta o resultado; com `noPainel`, relança a
 * falha, como a página.
 */
function escreverFalso(atual: () => ImportacaoDto) {
  return vi.fn(async (acao: (a: ImportacaoDto) => Promise<ImportacaoDto>, noPainel?: boolean) => {
    try {
      await acao(atual())
    } catch (e) {
      if (noPainel) throw e
    }
  })
}

function painel(
  registroId: number | null, componenteId: number | null, opcoes: { importacao?: ImportacaoDto; escrever?: ReturnType<typeof escreverFalso> } = {},
) {
  const doPainel = opcoes.importacao ?? importacao()
  const escrever = opcoes.escrever ?? escreverFalso(() => doPainel)
  const props = { importacao: doPainel, registroId, componenteId, escrever }
  const resultado = render(<PainelDoComponenteDaImportacao {...props} />)
  return { escrever, ...resultado, props }
}

function regiao() {
  return screen.getByRole('region', { name: 'Componente selecionado' })
}

function chamadas(mock: ReturnType<typeof montarFetch>) {
  return mock.mock.calls.filter((c) => !(String(c[0]).startsWith('/api/componentes?') && !c[1]?.method))
}

function corpoJson(mock: ReturnType<typeof montarFetch>, metodo: string, caminho: string) {
  const chamada = mock.mock.calls.find((c) => c[1]?.method === metodo && String(c[0]) === caminho)
  expect(chamada).toBeTruthy()
  return JSON.parse(String(chamada![1]!.body)) as Record<string, unknown>
}

describe('PainelDoComponenteDaImportacao', () => {
  it('Troca_de_no_selecionado_troca_o_conteudo', () => {
    const { rerender, props } = painel(2, 200)
    expect(within(regiao()).getByText('SU-200')).toBeTruthy()
    expect(within(regiao()).getByText('Suporte')).toBeTruthy()

    rerender(<PainelDoComponenteDaImportacao {...props} registroId={4} componenteId={500} />)

    expect(within(regiao()).getByText('CA-500')).toBeTruthy()
    expect(within(regiao()).getByText('Calço')).toBeTruthy()
    expect(within(regiao()).queryByText('SU-200')).toBeNull()
    // O comparativo é do divergente: o painel novo não o herda.
    expect(screen.queryByRole('table', { name: 'Comparativo da receita' })).toBeNull()
  })

  it('mostra a descrição do BOM ao lado quando difere da do catálogo, e só então', () => {
    const { rerender, props } = painel(2, 200)
    expect(within(regiao()).getByText('No BOM: Suporte do BOM')).toBeTruthy()

    rerender(<PainelDoComponenteDaImportacao {...props} registroId={4} componenteId={500} />)
    expect(within(regiao()).queryByText(/No BOM/)).toBeNull()
  })

  it('Escolha_de_receita_nasce_sem_opcao_marcada', () => {
    painel(2, 200)

    const grupo = screen.getByRole('group', { name: 'Receita deste Componente' })
    const radios = within(grupo).getAllByRole('radio') as HTMLInputElement[]
    expect(radios.map((r) => r.parentElement?.textContent)).toEqual([
      expect.stringContaining('Manter a receita do catálogo'),
      expect.stringContaining('Usar a receita importada'),
    ])
    expect(radios.map((r) => r.checked)).toEqual([false, false])
    expect(screen.getByRole('table', { name: 'Comparativo da receita' })).toBeTruthy()
  })

  it('com escolha gravada, a opção vem marcada', () => {
    const marcada = importacao({
      componentes: SITUACOES.map((s) => (s.registroId === 2 ? { ...s, escolhaDeReceita: 'Importada' as const } : s)),
    })
    painel(2, 200, { importacao: marcada })

    expect((screen.getByRole('radio', { name: /Usar a receita importada/ }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('radio', { name: /Manter a receita do catálogo/ }) as HTMLInputElement).checked).toBe(false)
  })

  it('Componente que não diverge não mostra comparativo nem escolha', () => {
    painel(4, 500)

    expect(screen.queryByRole('group', { name: 'Receita deste Componente' })).toBeNull()
    expect(screen.queryByRole('radio')).toBeNull()
  })

  it('Efeito_de_manter_catalogo_aparece_antes_de_salvar', () => {
    const fetchMock = montarFetch({})
    const { escrever } = painel(2, 200)

    expect(screen.getByText('Manter a do catálogo retira 3 itens do BOM e traz 2 do catálogo.')).toBeTruthy()
    // Antes de escolher nada: nenhuma escrita, nenhuma requisição.
    expect(escrever).not.toHaveBeenCalled()
    expect(chamadas(fetchMock)).toHaveLength(0)
  })

  it('o efeito usa o singular quando é um item só', () => {
    const um = importacao({
      componentes: SITUACOES.map((s) => (s.registroId === 2 ? { ...s, efeitoDeManterCatalogo: { retira: 1, traz: 1 } } : s)),
    })
    painel(2, 200, { importacao: um })

    expect(screen.getByText('Manter a do catálogo retira 1 item do BOM e traz 1 do catálogo.')).toBeTruthy()
  })

  it('escolher a receita escreve com o casamento atual e a escolha, sem trocar o Componente', async () => {
    const fetchMock = montarFetch({ 'PUT /api/importacoes/5/componentes/2': () => respostaJson(importacao()) })
    painel(2, 200)

    fireEvent.click(screen.getByRole('radio', { name: /Manter a receita do catálogo/ }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/2')).toEqual({
      versao: 'AAAAAAAAB9E=', componenteId: 200, codigoNovo: null, descricaoNova: null, tipoNovo: null,
      escolhaDeReceita: 'Catalogo',
    })
  })

  it('Upload_vai_para_o_solido_pendente_com_a_versao', async () => {
    const fetchMock = montarFetch({ 'POST /api/importacoes/5/componentes/3/solido': () => respostaJson(importacao()) })
    const { escrever } = painel(3, null)

    const arquivo = new File([new Uint8Array(684)], 'parafuso.stl')
    fireEvent.change(screen.getByLabelText(/sólido/i), { target: { files: [arquivo] } })

    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(1))
    const [url, init] = chamadas(fetchMock)[0]
    expect(String(url)).toBe('/api/importacoes/5/componentes/3/solido')
    expect(init?.method).toBe('POST')
    const corpo = init?.body as FormData
    expect(corpo.get('versao')).toBe('AAAAAAAAB9E=')
    expect((corpo.get('arquivo') as File).name).toBe('parafuso.stl')
    // A resposta passa pelo `escrever` da página: é ele quem a põe no estado e trata o 409.
    expect(escrever).toHaveBeenCalledTimes(1)
  })

  it('o upload também vai para o pendente quando o Componente casado já tem sólido no catálogo', async () => {
    const fetchMock = montarFetch({ 'POST /api/importacoes/5/componentes/4/solido': () => respostaJson(importacao()) })
    painel(4, 500)

    expect(screen.getByText('calco.stl', { exact: false })).toBeTruthy()
    fireEvent.change(screen.getByLabelText(/sólido/i), { target: { files: [new File([new Uint8Array(684)], 'novo.stl')] } })

    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(1))
    expect(String(chamadas(fetchMock)[0][0])).toBe('/api/importacoes/5/componentes/4/solido')
  })

  it('o visualizador aponta ao catálogo, e ao pendente quando existe um', async () => {
    const fetchMock = montarFetch({
      'GET /api/componentes/500/solido': () => respostaBinaria(new Uint8Array(684)),
      'GET /api/importacoes/5/componentes/5/solido': () => respostaBinaria(new Uint8Array(684)),
    })
    const { rerender, props } = painel(4, 500)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(1))
    expect(String(chamadas(fetchMock)[0][0])).toBe('/api/componentes/500/solido')

    rerender(<PainelDoComponenteDaImportacao {...props} registroId={5} componenteId={600} />)
    fireEvent.click(screen.getByRole('button', { name: /visualizar/i }))
    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(2))
    expect(String(chamadas(fetchMock)[1][0])).toBe('/api/importacoes/5/componentes/5/solido')
  })

  it('sem sólido nenhum, não há visualizador', () => {
    painel(3, null)

    expect(screen.queryByRole('button', { name: /visualizar/i })).toBeNull()
    expect(screen.getByText('Sem sólido.')).toBeTruthy()
  })

  it('Casar_com_outro_pelo_SeletorComBusca_envia_componenteId', async () => {
    const fetchMock = montarFetch({
      'GET /api/componentes': () => respostaJson({
        itens: [{ id: 12, codigo: 'SU-210', descricao: 'Suporte reforçado', tipo: 'Montagem', ativo: true, temSolido: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
      'PUT /api/importacoes/5/componentes/2': () => respostaJson(importacao()),
    })
    // O registro 2 tem escolha gravada: trocar o casamento a zera, e o servidor recusaria o contrário.
    const comEscolha = importacao({
      componentes: SITUACOES.map((s) => (s.registroId === 2 ? { ...s, escolhaDeReceita: 'Catalogo' as const } : s)),
    })
    painel(2, 200, { importacao: comEscolha })

    const campo = screen.getByRole('combobox', { name: 'Casar com outro Componente' })
    fireEvent.focus(campo)
    fireEvent.click(await screen.findByText('SU-210'))

    await waitFor(() => expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/2')).toBeTruthy())
    expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/2')).toEqual({
      versao: 'AAAAAAAAB9E=', componenteId: 12, codigoNovo: null, descricaoNova: null, tipoNovo: null,
      escolhaDeReceita: null,
    })
  })

  it('casar com um Componente que outro código já casou não escreve e diz por quê no painel', async () => {
    const fetchMock = montarFetch({
      'GET /api/componentes': () => respostaJson({
        itens: [{ id: 500, codigo: 'CA-500', descricao: 'Calço', tipo: 'Fabricado', ativo: true, temSolido: true }],
        total: 1, pagina: 1, tamanho: 20,
      }),
    })
    const { escrever } = painel(2, 200)

    fireEvent.focus(screen.getByRole('combobox', { name: 'Casar com outro Componente' }))
    fireEvent.click(await screen.findByRole('option', { name: /CA-500/ }))

    expect((await within(regiao()).findByRole('alert')).textContent).toBe(
      'CA-500 já está casado com o código CA-500 do BOM. Escolha outro Componente, ou case aquele código com outro antes.',
    )
    expect(escrever).not.toHaveBeenCalled()
    expect(chamadas(fetchMock)).toHaveLength(0)
  })

  it('o seletor mostra o Componente casado e escolher o mesmo não escreve', async () => {
    montarFetch({
      'GET /api/componentes': () => respostaJson({
        itens: [{ id: 200, codigo: 'SU-200', descricao: 'Suporte', tipo: 'Montagem', ativo: true, temSolido: false }],
        total: 1, pagina: 1, tamanho: 20,
      }),
    })
    const { escrever } = painel(2, 200)

    const campo = screen.getByRole('combobox', { name: 'Casar com outro Componente' }) as HTMLInputElement
    expect(campo.value).toBe('SU-200 — Suporte')
    fireEvent.focus(campo)
    fireEvent.click(await screen.findByRole('option', { name: /SU-200/ }))

    expect(escrever).not.toHaveBeenCalled()
  })

  it('Criar_novo_mostra_campos_e_Tipo', async () => {
    const fetchMock = montarFetch({ 'PUT /api/importacoes/5/componentes/3': () => respostaJson(importacao()) })
    painel(3, null)

    // Registro novo: código, descrição e Tipo, e o Tipo é da lista fechada do catálogo.
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('PA-300')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('Parafuso')
    const tipo = screen.getByLabelText('Tipo') as HTMLSelectElement
    expect(tipo.value).toBe('Fabricado')
    expect(within(tipo).getAllByRole('option').map((o) => o.textContent)).toEqual(['Bruto', 'Fabricado', 'Montagem'])

    // Trocar o Tipo escreve o registro, mantendo-o novo (`componenteId: null`).
    fireEvent.change(tipo, { target: { value: 'Bruto' } })
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/3')).toEqual({
      versao: 'AAAAAAAAB9E=', componenteId: null, codigoNovo: 'PA-300', descricaoNova: 'Parafuso', tipoNovo: 'Bruto',
      escolhaDeReceita: null,
    })
  })

  it('o novo que tem filhos no BOM não oferece Bruto; o Bruto que o reimport manteve continua na lista', () => {
    const chassi = (tipoNovo: string) => importacao({
      componentes: [...SITUACOES, situacao({
        registroId: 1, codigoLido: 'CH-100', descricaoLida: 'Chassi', codigoNovo: 'CH-100', descricaoNova: 'Chassi', tipoNovo,
      })],
    })
    const opcoes = () => within(screen.getByLabelText('Tipo')).getAllByRole('option').map((o) => o.textContent)

    const { rerender, props } = painel(1, null, { importacao: chassi('Montagem') })
    expect(opcoes()).toEqual(['Fabricado', 'Montagem'])

    rerender(<PainelDoComponenteDaImportacao {...props} importacao={chassi('Bruto')} />)
    expect(opcoes()).toEqual(['Bruto', 'Fabricado', 'Montagem'])
  })

  it('o código do novo só escreve ao sair do campo, e só se mudou', async () => {
    const fetchMock = montarFetch({ 'PUT /api/importacoes/5/componentes/3': () => respostaJson(importacao()) })
    painel(3, null)

    const campo = screen.getByLabelText('Código')
    fireEvent.blur(campo)
    expect(chamadas(fetchMock)).toHaveLength(0)

    fireEvent.change(campo, { target: { value: 'PA-301' } })
    expect(chamadas(fetchMock)).toHaveLength(0)
    fireEvent.blur(campo)

    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(1))
    expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/3').codigoNovo).toBe('PA-301')
  })

  it('o casado oferece "Criar novo" e não mostra Tipo; clicar solta o casamento e zera a escolha', async () => {
    const fetchMock = montarFetch({ 'PUT /api/importacoes/5/componentes/2': () => respostaJson(importacao()) })
    // Com escolha gravada: um painel que a devolvesse no corpo, em vez de zerá-la, seria recusado pelo servidor.
    const comEscolha = importacao({
      componentes: SITUACOES.map((x) => (x.registroId === 2 ? { ...x, escolhaDeReceita: 'Catalogo' as const } : x)),
    })
    painel(2, 200, { importacao: comEscolha })

    expect(screen.queryByLabelText('Tipo')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Criar novo' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(corpoJson(fetchMock, 'PUT', '/api/importacoes/5/componentes/2')).toEqual({
      versao: 'AAAAAAAAB9E=', componenteId: null, codigoNovo: null, descricaoNova: null, tipoNovo: null,
      escolhaDeReceita: null,
    })
  })

  it('No_so_do_catalogo_fica_em_leitura', () => {
    const fetchMock = montarFetch({})
    painel(null, 700)

    expect(within(regiao()).getByText('RO-700')).toBeTruthy()
    expect(within(regiao()).getByText('Rolamento do catálogo')).toBeTruthy()
    expect(screen.getByText('Este item vem da receita do catálogo; o sólido se envia no cadastro dele.')).toBeTruthy()
    expect((screen.getByLabelText(/sólido/i) as HTMLInputElement).disabled).toBe(true)
    // Nenhum controle de escrita: nem casamento, nem "Criar novo", nem escolha de receita.
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Criar novo' })).toBeNull()
    expect(screen.queryByRole('radio')).toBeNull()
    expect(chamadas(fetchMock)).toHaveLength(0)
  })

  it('perfil sem escrita vê o Componente sem nenhum controle de escrita', () => {
    perfil = 'Operador'
    painel(2, 200)

    expect(within(regiao()).getByText('SU-200')).toBeTruthy()
    expect(screen.queryByRole('combobox')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Criar novo' })).toBeNull()
    expect(screen.queryByLabelText(/sólido/i)).toBeNull()
    // A decisão já tomada continua visível, mas travada.
    for (const r of screen.getAllByRole('radio') as HTMLInputElement[]) expect(r.disabled).toBe(true)
  })

  it('escrita em voo trava os controles', () => {
    const { rerender, props } = painel(2, 200)
    rerender(<PainelDoComponenteDaImportacao {...props} desabilitado />)

    for (const r of screen.getAllByRole('radio') as HTMLInputElement[]) expect(r.disabled).toBe(true)
    expect((screen.getByRole('combobox') as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Criar novo' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('pílulas de situação: Novo é neutra, Inativo usa atencao', () => {
    const inativo = importacao({
      componentes: SITUACOES.map((s) => (s.registroId === 4 ? { ...s, ativo: false } : s)),
    })
    const { rerender, props } = painel(3, null, { importacao: inativo })
    expect(within(regiao()).getByText('Novo').className).toContain('text-acao')

    rerender(<PainelDoComponenteDaImportacao {...props} registroId={4} componenteId={500} />)
    expect(within(regiao()).getByText('Inativo').className).toContain('text-atencao-texto')
  })

  it('Tab do Código para a Descrição durante a escrita mantém foco e texto, e a segunda escrita leva os dois', async () => {
    let resolverPrimeira!: (r: Response) => void
    let chamadasDePut = 0
    const fetchMock = montarFetch({
      'PUT /api/importacoes/5/componentes/3': () => {
        chamadasDePut += 1
        return chamadasDePut === 1
          ? new Promise<Response>((r) => { resolverPrimeira = r }) as unknown as Response
          : respostaJson(importacao())
      },
    })
    // Como na página, a escrita monta a requisição com o rascunho mais recente, o que a anterior devolveu.
    let maisRecente = importacao()
    const { rerender, props } = painel(3, null, { importacao: maisRecente, escrever: escreverFalso(() => maisRecente) })

    const codigo = screen.getByLabelText('Código') as HTMLInputElement
    act(() => codigo.focus())
    fireEvent.change(codigo, { target: { value: 'PA-301' } })
    act(() => codigo.blur())
    // A escrita do Código está em voo: a tela passa a travar o que é escolha, mas não o que se digita.
    rerender(<PainelDoComponenteDaImportacao {...props} desabilitado />)
    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(1))

    const descricao = screen.getByLabelText('Descrição') as HTMLInputElement
    expect(descricao.disabled).toBe(false)
    act(() => descricao.focus())
    fireEvent.change(descricao, { target: { value: 'Parafuso sextavado' } })

    // O servidor responde: o Código novo chega, o formulário não remonta.
    const respondido = importacao({
      componentes: SITUACOES.map((x) => (x.registroId === 3 ? { ...x, codigoNovo: 'PA-301' } : x)),
    })
    await act(async () => { resolverPrimeira(respostaJson(respondido)) })
    maisRecente = respondido
    rerender(<PainelDoComponenteDaImportacao {...props} importacao={respondido} desabilitado={false} />)

    const descricaoDepois = screen.getByLabelText('Descrição') as HTMLInputElement
    expect(descricaoDepois).toBe(descricao)
    expect(descricaoDepois.value).toBe('Parafuso sextavado')
    expect(document.activeElement).toBe(descricaoDepois)
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('PA-301')

    act(() => descricaoDepois.blur())
    await waitFor(() => expect(chamadas(fetchMock)).toHaveLength(2))
    expect(JSON.parse(String(chamadas(fetchMock)[1][1]!.body))).toMatchObject({
      codigoNovo: 'PA-301', descricaoNova: 'Parafuso sextavado', componenteId: null,
    })
  })

  it('o painel só fica fixo de lg para cima, onde fica ao lado da árvore', () => {
    painel(2, 200)

    const classes = regiao().className.split(' ')
    expect(classes).toContain('lg:sticky')
    expect(classes).toContain('lg:overflow-y-auto')
    expect(classes).not.toContain('sticky')
    // Abaixo de `lg` ele é um bloco comum no alto da tela: fixo no topo, cobria a árvore.
    expect(classes).not.toContain('md:sticky')
  })

  it('o conteúdo do painel fica numa coluna só, que é a largura da coluna lateral', () => {
    painel(2, 200)

    const grades = regiao().querySelectorAll('[class*="grid-cols-"]')
    expect(Array.from(grades).map((g) => g.className)).toEqual([])
  })
})
