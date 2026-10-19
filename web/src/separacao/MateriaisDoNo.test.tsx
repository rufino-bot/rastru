// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, fireEvent, waitFor } from '@testing-library/react'
import { MateriaisDoNo } from './MateriaisDoNo'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'
import type { MaterialDoNoComSeparacao, MateriaisDoNoDto } from '../api/separacao'
import type { MaterialDto } from '../api/cadastros'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function material(sobre: Partial<MaterialDoNoComSeparacao>): MaterialDoNoComSeparacao {
  return {
    materialId: 3, codigo: 'PAR-M8', descricao: 'Parafuso M8', unidadeMedida: 'UN', ficaEm: null,
    planejado: 10, separado: 8, pendencia: 2, noPlanejado: true, acimaDoPlanejado: false,
    ...sobre,
  }
}

const PARAFUSO = material({})
const CHAPA = material({
  materialId: 5, codigo: 'CH-2', descricao: 'Chapa 2 mm', unidadeMedida: 'KG',
  ficaEm: { setorId: 1, nome: 'Corte a Laser' }, planejado: 2.5, separado: 0, pendencia: 0,
})
const ARRUELA = material({
  materialId: 7, codigo: 'ARR-8', descricao: 'Arruela 8', planejado: 4, separado: 6, pendencia: 0,
  acimaDoPlanejado: true,
})
const PORCA = material({
  materialId: 9, codigo: 'POR-8', descricao: 'Porca 8', planejado: 0, separado: 3, pendencia: 0,
  noPlanejado: false,
})

const MATERIAIS: MateriaisDoNoDto = { estruturaItemId: 7, materiais: [PARAFUSO, CHAPA, ARRUELA, PORCA] }

function doCatalogo(id: number, codigo: string, descricao: string, unidadeMedida = 'UN'): MaterialDto {
  return { id, codigo, descricao, unidadeMedida, ativo: true, ficaEm: null }
}

const CATALOGO: MaterialDto[] = [
  doCatalogo(3, 'PAR-M8', 'Parafuso M8'),
  doCatalogo(5, 'CH-2', 'Chapa 2 mm', 'KG'),
  doCatalogo(7, 'ARR-8', 'Arruela 8'),
  doCatalogo(9, 'POR-8', 'Porca 8'),
  doCatalogo(11, 'TIN-1', 'Tinta preta', 'L'),
]

/** Classes dos tons de estado (`Pilula` e tokens): nenhuma delas pode marcar "fora/acima do planejado". */
const CLASSE_DE_ESTADO = /(positivo|negativo|atencao|atraso)/

function montarFetch({
  leituras = [MATERIAIS],
  respostaPut = () => respostaJson(MATERIAIS),
}: {
  leituras?: MateriaisDoNoDto[]
  respostaPut?: () => Response | Promise<Response>
} = {}) {
  let gets = 0
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    const metodo = init?.method ?? 'GET'
    if (caminho === '/api/estrutura/7/materiais' && metodo === 'GET') {
      const dados = leituras[Math.min(gets, leituras.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(dados))
    }
    if (caminho === '/api/estrutura/7/materiais' && metodo === 'PUT') return Promise.resolve(respostaPut())
    if (caminho === '/api/materiais' && metodo === 'GET') return Promise.resolve(respostaJson(CATALOGO))
    return Promise.reject(new Error(`fetch não esperado no teste: ${metodo} ${url}`))
  })
}

function renderizar(podeEditar = true, aoSalvar = vi.fn()) {
  render(<MateriaisDoNo noId={7} podeEditar={podeEditar} aoSalvar={aoSalvar} />)
  return { aoSalvar }
}

const linhaDo = (codigo: string) => screen.getByText(new RegExp(`^${codigo} `)).closest('li')!

function putsDe(fetchMock: ReturnType<typeof montarFetch>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')
}

async function abrirEditor() {
  await screen.findByText(/^PAR-M8 /)
  fireEvent.click(screen.getByRole('button', { name: 'Editar materiais' }))
  return screen.getByRole('form', { name: 'Editar materiais' })
}

describe('MateriaisDoNo', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('lista cada material com planejado, separado, pendência e Fica em', async () => {
    vi.stubGlobal('fetch', montarFetch())

    renderizar(false)

    await screen.findByText(/^PAR-M8 /)
    const parafuso = linhaDo('PAR-M8')
    expect(parafuso.textContent).toContain('PAR-M8 Parafuso M8')
    expect(within(parafuso).getByText('Planejado 10 UN · Separado 8 UN · Pendência 2 UN')).toBeTruthy()
    expect(within(parafuso).getByText('Fica em: Almoxarifado')).toBeTruthy()

    // Material de Setor não tem pendência (regra 34): quem o pega é o operador, no Setor.
    const chapa = linhaDo('CH-2')
    expect(within(chapa).getByText('Planejado 2,5 KG · Separado 0 KG')).toBeTruthy()
    expect(within(chapa).getByText('Fica em: Corte a Laser')).toBeTruthy()
    expect(chapa.textContent).not.toContain('Pendência')
  })

  it('marca "fora do planejado" e "acima do planejado" em texto neutro', async () => {
    vi.stubGlobal('fetch', montarFetch())

    renderizar(false)

    await screen.findByText(/^PAR-M8 /)
    const fora = within(linhaDo('POR-8')).getByText('fora do planejado')
    // A `Pilula` de tom `neutro`, não um tom de estado: sair do plano é informação, não erro.
    expect(fora.className).toContain('bg-acao-fundo')
    expect(fora.className).not.toMatch(CLASSE_DE_ESTADO)
    // O que saiu do plano não tem pendência a mostrar.
    expect(linhaDo('POR-8').textContent).not.toContain('Pendência')

    const acima = within(linhaDo('ARR-8')).getByText('acima do planejado')
    expect(acima.className).toMatch(/text-tinta/)
    expect(acima.className).not.toMatch(CLASSE_DE_ESTADO)

    // Cada marca só na linha a que pertence.
    expect(linhaDo('PAR-M8').textContent).not.toMatch(/fora do planejado|acima do planejado/)
    expect(linhaDo('ARR-8').textContent).not.toContain('fora do planejado')
    expect(linhaDo('POR-8').textContent).not.toContain('acima do planejado')
  })

  it('Editar materiais adiciona, muda a quantidade, remove e salva a lista inteira por PUT', async () => {
    const depois: MateriaisDoNoDto = { estruturaItemId: 7, materiais: [material({ planejado: 12.5 })] }
    const fetchMock = montarFetch({ leituras: [MATERIAIS, depois], respostaPut: () => respostaJson(depois) })
    vi.stubGlobal('fetch', fetchMock)
    const { aoSalvar } = renderizar()

    const form = await abrirEditor()

    // O editor parte do planejado: o que saiu do plano (a Porca) não está nele.
    expect((within(form).getByLabelText('Quantidade de PAR-M8') as HTMLInputElement).value).toBe('10')
    expect((within(form).getByLabelText('Quantidade de CH-2') as HTMLInputElement).value).toBe('2,5')
    expect(within(form).queryByLabelText('Quantidade de POR-8')).toBeNull()

    fireEvent.change(within(form).getByLabelText('Quantidade de PAR-M8'), { target: { value: '12,5' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Remover CH-2' }))

    // O seletor não oferece o que já está na lista (o servidor recusaria o repetido com 400).
    const seletor = within(form).getByLabelText('Material') as HTMLSelectElement
    await waitFor(() => expect(within(seletor).queryByRole('option', { name: 'TIN-1 — Tinta preta' })).toBeTruthy())
    const oferecidos = within(seletor).getAllByRole('option').map((o) => o.textContent)
    expect(oferecidos).not.toContain('PAR-M8 — Parafuso M8')
    expect(oferecidos).toContain('POR-8 — Porca 8')

    fireEvent.change(seletor, { target: { value: '11' } })
    fireEvent.change(within(form).getByLabelText('Quantidade'), { target: { value: '0,5' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Adicionar' }))
    expect((within(form).getByLabelText('Quantidade de TIN-1') as HTMLInputElement).value).toBe('0,5')

    fireEvent.click(within(form).getByRole('button', { name: 'Salvar materiais' }))

    await waitFor(() => expect(aoSalvar).toHaveBeenCalledTimes(1))
    const puts = putsDe(fetchMock)
    expect(puts).toHaveLength(1)
    // A lista INTEIRA, na ordem da tela, com a vírgula lida como decimal.
    expect(JSON.parse(String(puts[0][1]!.body))).toEqual({
      linhas: [
        { materialId: 3, quantidade: 12.5 },
        { materialId: 7, quantidade: 4 },
        { materialId: 11, quantidade: 0.5 },
      ],
    })
    // O editor fecha e a lista mostra o que o servidor devolveu.
    expect(screen.queryByRole('form', { name: 'Editar materiais' })).toBeNull()
    expect(await screen.findByText('Planejado 12,5 UN · Separado 8 UN · Pendência 2 UN')).toBeTruthy()
  })

  it('no editor, o botão Remover fica na linha do campo, e a dica fora dela; a linha de adicionar fica nivelada', async () => {
    // O jsdom não mede pixel: o teste prova a estrutura que dá o alinhamento (ver o `aoLado` do `Campo`).
    vi.stubGlobal('fetch', montarFetch())
    renderizar()

    const form = await abrirEditor()

    const campo = within(form).getByLabelText('Quantidade de PAR-M8')
    const remover = within(form).getByRole('button', { name: 'Remover PAR-M8' })
    const dica = document.getElementById(campo.getAttribute('aria-describedby')!)!
    expect(dica.textContent).toBe('Parafuso M8 (UN)')
    const linha = remover.parentElement!
    expect(linha.contains(campo)).toBe(true)
    expect(linha.contains(dica)).toBe(false)
    expect(linha.className.split(/\s+/)).toContain('items-stretch')

    // Adicionar: o botão na linha da caixa da Quantidade; o Material ao lado, com os rótulos no topo
    // das duas colunas (a grade alinha pelo início, não pela base).
    const quantidade = within(form).getByLabelText('Quantidade')
    const adicionar = within(form).getByRole('button', { name: 'Adicionar' })
    expect(adicionar.parentElement!.contains(quantidade)).toBe(true)
    expect(adicionar.parentElement!.className.split(/\s+/)).toContain('items-stretch')
    const grade = within(form).getByLabelText('Material').closest('.grid')!
    expect(grade.contains(quantidade)).toBe(true)
    expect(grade.className).not.toMatch(/items-end/)
  })

  it('quantidade inválida no editor não envia nada e diz qual linha', async () => {
    const fetchMock = montarFetch()
    vi.stubGlobal('fetch', fetchMock)
    const { aoSalvar } = renderizar()

    const form = await abrirEditor()
    fireEvent.change(within(form).getByLabelText('Quantidade de PAR-M8'), { target: { value: '0' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Salvar materiais' }))

    expect(await within(form).findByText('PAR-M8: A quantidade precisa ser maior que zero.')).toBeTruthy()
    expect(putsDe(fetchMock)).toHaveLength(0)
    expect(aoSalvar).not.toHaveBeenCalled()
  })

  it('dois envios seguidos do editor gravam um PUT só', async () => {
    let responder: (r: Response) => void = () => {}
    const fetchMock = montarFetch({ respostaPut: () => new Promise<Response>((r) => { responder = r }) })
    vi.stubGlobal('fetch', fetchMock)
    renderizar()

    const form = await abrirEditor()
    // O submit do formulário, e não o clique: o botão desabilitado não segura o segundo envio no
    // mesmo quadro — quem segura é a guarda da função.
    fireEvent.submit(form)
    fireEvent.submit(form)

    expect(putsDe(fetchMock)).toHaveLength(1)
    responder(respostaJson(MATERIAIS))
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Editar materiais' })).toBeNull())
  })

  it('erro ao salvar fica dentro do editor, que continua aberto', async () => {
    vi.stubGlobal('fetch', montarFetch({
      respostaPut: () => respostaJson({ erro: 'MaterialInativo', mensagem: 'O material TIN-1 está inativo.' }, 400),
    }))
    const { aoSalvar } = renderizar()

    const form = await abrirEditor()
    fireEvent.click(within(form).getByRole('button', { name: 'Salvar materiais' }))

    expect(await within(form).findByText('O material TIN-1 está inativo.')).toBeTruthy()
    expect(screen.getByRole('form', { name: 'Editar materiais' })).toBeTruthy()
    expect(aoSalvar).not.toHaveBeenCalled()
  })

  it('Cancelar fecha o editor sem gravar e devolve o foco a Editar materiais', async () => {
    const fetchMock = montarFetch()
    vi.stubGlobal('fetch', fetchMock)
    renderizar()

    const form = await abrirEditor()
    fireEvent.click(within(form).getByRole('button', { name: 'Remover CH-2' }))
    fireEvent.click(within(form).getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('form', { name: 'Editar materiais' })).toBeNull()
    expect(putsDe(fetchMock)).toHaveLength(0)
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar materiais' }))
    // O que foi removido no editor descartado continua na lista.
    expect(linhaDo('CH-2')).toBeTruthy()
  })

  it('sem permissão não mostra Editar materiais', async () => {
    const fetchMock = montarFetch()
    vi.stubGlobal('fetch', fetchMock)

    renderizar(false)

    await screen.findByText(/^PAR-M8 /)
    expect(screen.queryByRole('button', { name: 'Editar materiais' })).toBeNull()
    // Quem não edita nem busca o catálogo.
    expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/materiais'))).toBe(false)
  })

  it('mostra carregando antes de os materiais chegarem', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar(false)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra "Este item não tem materiais." quando não há nenhum', async () => {
    vi.stubGlobal('fetch', montarFetch({ leituras: [{ estruturaItemId: 7, materiais: [] }] }))

    renderizar(true)

    expect(await screen.findByText('Este item não tem materiais.')).toBeTruthy()
    // Sem material ainda, o PCP continua podendo acrescentar.
    expect(screen.getByRole('button', { name: 'Editar materiais' })).toBeTruthy()
  })

  it('mostra o erro de carga', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({}, 400))))

    renderizar(false)

    expect(await screen.findByText('Não foi possível carregar os materiais.')).toBeTruthy()
    expect(screen.queryByRole('status')).toBeNull()
  })
})
