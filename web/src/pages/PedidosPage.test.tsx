// @vitest-environment jsdom
//
// Ambiente por ARQUIVO, e não em `vite.config.ts`: os testes de `api/` rodam em ambiente `node` e
// usam `new Response(...)`; trocar o ambiente global arriscaria mexer nos globals deles sem ganho.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { PedidosPage } from './PedidosPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que só existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha o segundo teste renderiza por cima do primeiro e os `getBy*` falham com
// "found multiple elements".
afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra. Default `'PCP'`: pode escrever pedidos
// (`podeEscrever` em `web/src/auth/permissoes.ts` libera `PCP` e `Administrador`), o que preserva o
// comportamento dos 4 testes existentes (o formulário precisa estar visível para eles).
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1, pausa: null,
}

// Envelope de página que o servidor devolve. `total` é sob o filtro, não `itens.length`.
function pagina(itens: unknown[], total = itens.length, numero = 1) {
  return { itens, total, pagina: numero, tamanho: 20 }
}

const MATERIAIS = [
  { id: 3, codigo: 'CH-300', descricao: 'Chapa SAE 1020 3,00 mm' },
  { id: 5, codigo: 'TB-200', descricao: 'Tubo redondo 2"' },
]

// Mostra a rota atual (caminho + query) para o teste afirmar o que a tela escreveu na URL.
function LocalizacaoAtual() {
  const { pathname, search } = useLocation()
  return <p aria-label="localizacao">{pathname + search}</p>
}

function renderizar(rota = '/pedidos') {
  render(
    <MemoryRouter initialEntries={[rota]}>
      <PedidosPage />
      <LocalizacaoAtual />
    </MemoryRouter>,
  )
}

function localizacao() {
  return screen.getByLabelText('localizacao').textContent
}

// As listagens (GET) já chamadas, como URL. O POST de abrir Pedido cai no mesmo caminho e fica de fora.
function listagens(fetchMock: ReturnType<typeof fetchPorRota>) {
  return fetchMock.mock.calls
    .filter((c) => String(c[0]).split('?')[0] === '/api/pedidos' && c[1]?.method !== 'POST')
    .map((c) => new URL(String(c[0]), 'http://x'))
}

// A API do teste: lista de um Pedido e os dois materiais em uso. Cada teste sobrescreve o que importa.
function api(sobrescritas: Record<string, () => Response | Promise<Response>> = {}) {
  return fetchPorRota({
    '/api/pedidos': () => respostaJson(pagina([PEDIDO])),
    '/api/pedidos/materiais': () => respostaJson(MATERIAIS),
    ...sobrescritas,
  })
}

function abrirPainelDoFiltro() {
  fireEvent.click(screen.getByRole('button', { name: /^Filtrar/ }))
}

describe('PedidosPage', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // I1 (achado da review de branch da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os pedidos que a API devolveu', async () => {
    vi.stubGlobal('fetch', api())

    renderizar()

    // O markup novo separa `p.numero` num `<span className="font-mono">` ANINHADO dentro do
    // `<span className="font-medium">` — `getNodeText` do Testing Library só concatena os
    // `TEXT_NODE` DIRETOS de um elemento, então nenhum nó tem mais o texto completo
    // 'PED-001 — Fábrica Alfa'. A propriedade a provar é a ORDEM (número antes do cliente), não
    // mais a string inteira num nó único — daí a asserção sobre o `textContent` do `<li>`.
    const item = await screen.findByText('PED-001')
    const li = item.closest('li')!
    expect(li.textContent).toMatch(/PED-001.*Fábrica Alfa/)
  })

  it('mostra a data de abertura no fuso que a API mandou, sem reconverter', async () => {
    // Offset +05:30 (Asia/Kolkata, fuso real — não um valor arbitrário): esta suíte roda em
    // -03:00, e é isso que fica provado aqui. Se alguém trocar `formatarDataHora` por
    // `new Date(...).toLocaleString()`, o horário reconverte para o fuso da máquina e deixa de
    // bater com 09:30 em qualquer máquina cujo fuso local não seja +05:30 — o que cobre esta
    // suíte, mas não é um absoluto universal (numa máquina em IST a mutação sobreviveria). Um
    // offset -03:00 na fixture coincidiria com o fuso local por acidente e deixaria a mutação
    // sobreviver aqui sem provar nada.
    const pedidoComFusoDistinto = { ...PEDIDO, dataAbertura: '2026-08-06T09:30:00+05:30' }
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([pedidoComFusoDistinto])) }))

    renderizar()

    expect(await screen.findByText(/06\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra erro quando a listagem falha', async () => {
    // 500: `mensagemDeErro` (`web/src/api/erros.ts:36`) mapeia status >= 500 para a mensagem de
    // servidor, não mais o fallback fixo desta tela.
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500) }))

    renderizar()

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): numa falha o hook mantém `total` em 0, então `total === 0`
  // sozinho também é verdade quando a causa é falha de rede — o "Nenhum pedido aberto" apareceria
  // JUNTO do banner de erro, afirmando um fato sobre o banco a partir de uma falha de conexão. O
  // que separa os dois casos é o `erroDeLeitura === null` na condição do estado vazio.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500) }))

    renderizar()

    await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nenhum pedido aberto')).toBeNull()
  })

  // M10 (achado da review da Task 8): `SetoresPage` e `MateriaisPage` têm o teste equivalente
  // (I1 da review de branch da 1B). Aqui o erro de LEITURA vem do `useBuscaPaginada` e só o `carregar`
  // do hook o limpa, no caminho de sucesso; `salvar` limpa apenas `erroDeEscrita`, que é outro
  // estado. Por isso o teste prova de verdade a limpeza do hook: o banner da carga inicial não
  // sobrevive à recarga que o cadastro bem-sucedido dispara. **Medido, não presumido**: removendo o
  // `setErro(null)` do caminho de sucesso de `carregar` em `useBuscaPaginada`, este teste fica
  // vermelho (26 verdes, 1 vermelho na tela). A M10 antiga era mutante equivalente enquanto a
  // página tinha `carregar` próprio e `salvar` limpava o mesmo `erro`; deixou de ser.
  it('limpa a mensagem de erro da carga inicial quando o cadastro seguinte tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', api({
      '/api/pedidos': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson(pagina([PEDIDO]))
      },
    }))

    renderizar()
    await screen.findByText('Não foi possível carregar os pedidos.')

    fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    await screen.findByText('PED-001')
    expect(screen.queryByText('Não foi possível carregar os pedidos.')).toBeNull()
  })

  it('limpa o formulário e recarrega a lista depois de abrir um pedido', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', api({
      '/api/pedidos': () => {
        chamadas += 1
        return respostaJson(pagina(chamadas === 1 ? [] : [PEDIDO]))
      },
    }))

    renderizar()
    await screen.findByLabelText('Código do pedido')

    fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    // O POST cai na MESMA rota da listagem: `fetchPorRota` casa por caminho, e o mock devolve a
    // lista nova. O que se prova aqui é o ramo de SUCESSO — campo limpo e lista recarregada —,
    // que é a metade que nenhum teste do projeto cobria antes desta task.
    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect((screen.getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('')
  })

  it('mostra estado vazio de cadastro quando nao ha pedidos e nenhum filtro', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([])) }))

    renderizar()

    expect(await screen.findByText('Nenhum pedido aberto')).toBeTruthy()
    // Sem busca nem filtro, culpar "essa busca ou esses filtros" seria falso.
    expect(screen.queryByText('Nenhum pedido com essa busca ou esses filtros')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Limpar filtros' })).toBeNull()
  })

  it('mostra vazio de filtro quando a busca ou o filtro nao acham nada', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([])) }))

    renderizar('/pedidos?busca=ZZZ')

    expect(await screen.findByText('Nenhum pedido com essa busca ou esses filtros')).toBeTruthy()
    expect(screen.queryByText('Nenhum pedido aberto')).toBeNull()
  })

  it('mostra vazio de filtro tambem quando so um filtro, sem busca, nao acha nada', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([])) }))

    renderizar('/pedidos?material=3')

    expect(await screen.findByText('Nenhum pedido com essa busca ou esses filtros')).toBeTruthy()
  })

  it('valor invalido da URL sozinho nao faz o vazio culpar filtro que o servidor nunca recebeu', async () => {
    // `?status=Qualquer` não é enviado; se a lista vem vazia, o cadastro está vazio.
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([])) }))

    renderizar('/pedidos?status=Qualquer')

    expect(await screen.findByText('Nenhum pedido aberto')).toBeTruthy()
  })

  it('o vazio de filtro tem Limpar filtros, que zera a busca e a selecao', async () => {
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([])) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?busca=ZZZ&status=Aberto&material=3')
    fireEvent.click(await screen.findByRole('button', { name: 'Limpar filtros' }))

    await waitFor(() => {
      const ultima = listagens(fetchMock).at(-1)!
      expect(ultima.searchParams.get('busca')).toBe('')
      expect(ultima.searchParams.has('status')).toBe(false)
      expect(ultima.searchParams.has('material')).toBe(false)
    })
    await waitFor(() => expect(localizacao()).toBe('/pedidos'))
    expect((screen.getByLabelText('Buscar por número, cliente ou código de peça') as HTMLInputElement).value).toBe('')
  })

  it('esconde o formulário para quem não pode escrever, e a lista continua visível', async () => {
    // Operador lê pedidos mas não escreve — `POST /pedidos` é `[Authorize(Roles = "PCP,Administrador")]`.
    perfil = 'Operador'
    vi.stubGlobal('fetch', api())

    renderizar()

    expect(await screen.findByText('PED-001')).toBeTruthy()
    expect(screen.queryByLabelText('Código do pedido')).toBeNull()
  })

  // I2 (achado da review da Task 8): o teste acima usa `Operador`, que não escreve NEM `pedidos`
  // NEM `setores` — prova só a metade negativa do gating. Quem prende o RECURSO (`usePodeEscrever`
  // apontando para `'pedidos'`, e não para outro) é a metade positiva com `PCP`, que escreve
  // `pedidos` (`['PCP','Administrador']`) mas NÃO `setores` (`['Administrador']`,
  // `web/src/auth/permissoes.ts`): se o recurso mudar para `'setores'`, o PCP perde a escrita e o
  // formulário desaparece.
  it('mostra o formulário para quem pode escrever pedidos mas não setores', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', api())

    renderizar()

    expect(await screen.findByLabelText('Código do pedido')).toBeTruthy()
  })

  it('mostra o status como pílula, com o tom certo por status', async () => {
    // Não basta asserir que os textos aparecem: `Pilula` renderiza `children` independente de
    // `tom` (M8 do Step 11 trocaria `tomDoStatus` para sempre 'positivo' sem mudar texto nenhum).
    // A prova precisa alcançar a CLASSE — o token `positivo-*`/`negativo-*` que `Pilula` aplica.
    const concluido = { ...PEDIDO, id: 1, numero: 'PED-002', status: 'Concluido' }
    const cancelado = { ...PEDIDO, id: 2, numero: 'PED-003', status: 'Cancelado' }
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([concluido, cancelado])) }))

    renderizar()

    // Dentro da lista: o painel do filtro (fechado, mas no DOM) também tem opções "Concluído" e
    // "Cancelado", e `findByText` na tela inteira acharia duas.
    const lista = await screen.findByRole('list')
    const pilulaConcluido = within(lista).getByText('Concluído')
    const pilulaCancelado = within(lista).getByText('Cancelado')

    expect(pilulaConcluido.className).toMatch(/positivo-/)
    expect(pilulaCancelado.className).toMatch(/negativo-/)
  })

  it('busca, status e material da URL entram na primeira consulta', async () => {
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([PEDIDO], 45, 2)) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?busca=CH&status=Aberto&material=3&pagina=2')
    await screen.findByText('PED-001')

    const primeira = listagens(fetchMock)[0]
    expect(primeira.searchParams.get('busca')).toBe('CH')
    expect(primeira.searchParams.get('status')).toBe('Aberto')
    expect(primeira.searchParams.get('material')).toBe('3')
    expect(primeira.searchParams.get('pagina')).toBe('2')
    // A URL foi só LIDA: a tela não a reescreveu na montagem.
    expect(localizacao()).toBe('/pedidos?busca=CH&status=Aberto&material=3&pagina=2')
  })

  it('pagina da URL que nao e um inteiro positivo vira a primeira', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?pagina=0')
    await screen.findByText('PED-001')

    expect(listagens(fetchMock)[0].searchParams.get('pagina')).toBe('1')
  })

  it('marcar um status no filtro consulta de novo na pagina 1 e escreve na URL', async () => {
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([PEDIDO], 45, 2)) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?pagina=2')
    await screen.findByText('PED-001')

    abrirPainelDoFiltro()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Em produção' }))

    await waitFor(() => {
      const ultima = listagens(fetchMock).at(-1)!
      expect(ultima.searchParams.get('status')).toBe('EmProducao')
      expect(ultima.searchParams.get('pagina')).toBe('1')
    })
    // Página 1 é o default e sai da URL; o status entra.
    await waitFor(() => expect(localizacao()).toBe('/pedidos?status=EmProducao'))
  })

  it('digitar na busca consulta de novo na pagina 1 e escreve na URL', async () => {
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([PEDIDO], 45, 2)) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?pagina=2')
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByLabelText('Buscar por número, cliente ou código de peça'), {
      target: { value: 'Alfa' },
    })

    await waitFor(() => {
      const ultima = listagens(fetchMock).at(-1)!
      expect(ultima.searchParams.get('busca')).toBe('Alfa')
      expect(ultima.searchParams.get('pagina')).toBe('1')
    })
    await waitFor(() => expect(localizacao()).toBe('/pedidos?busca=Alfa'))
  })

  it('as opcoes de Status sao os cinco em portugues e as de Material vem de /pedidos/materiais', async () => {
    vi.stubGlobal('fetch', api())

    renderizar()
    await screen.findByText('PED-001')
    abrirPainelDoFiltro()

    const status = within(screen.getByRole('group', { name: 'Status' }))
    expect(status.getAllByRole('checkbox')).toHaveLength(5)
    for (const rotulo of ['Aberto', 'Em produção', 'Aguardando expedição', 'Concluído', 'Cancelado']) {
      expect(status.getByRole('checkbox', { name: rotulo })).toBeTruthy()
    }

    // Os materiais chegam depois da lista; a faceta os mostra quando chegam.
    const material = within(screen.getByRole('group', { name: 'Material' }))
    expect(await material.findByRole('checkbox', { name: 'Chapa SAE 1020 3,00 mm' })).toBeTruthy()
    expect(material.getAllByRole('checkbox')).toHaveLength(2)
    expect(material.getByRole('checkbox', { name: 'Tubo redondo 2"' })).toBeTruthy()
    // O código do Material acompanha a descrição.
    expect(material.getByText('CH-300')).toBeTruthy()
  })

  it('falha ao carregar os materiais vira banner, e a lista continua', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos/materiais': () => Promise.reject(new Error('rede caiu')) }))

    renderizar()

    expect(await screen.findByText('Não foi possível carregar os materiais do filtro.')).toBeTruthy()
    expect(await screen.findByText('PED-001')).toBeTruthy()
  })

  // Review Focus: URL colada à mão. `?material=abc` viraria 400 no servidor e a tela quebraria;
  // a página só manda ao servidor o que ele aceita, e mostra o resto como opção ausente, removível.
  it('valor invalido da URL nao vai ao servidor e fica visivel como ausente', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?material=abc&status=Qualquer')
    await screen.findByText('PED-001')

    expect(listagens(fetchMock).length).toBeGreaterThan(0)
    for (const url of listagens(fetchMock)) {
      expect(url.searchParams.has('material')).toBe(false)
      expect(url.searchParams.has('status')).toBe(false)
    }
    // Sem banner de erro: nenhum 400 aconteceu.
    expect(screen.queryByRole('alert')).toBeNull()

    abrirPainelDoFiltro()
    const ausentes = screen.getAllByRole('checkbox', { name: 'Não está mais na lista' }) as HTMLInputElement[]
    expect(ausentes).toHaveLength(2)
    expect(ausentes.every((c) => c.checked)).toBe(true)

    // Removível: desmarcar tira o valor da URL.
    const grupoDeStatus = within(screen.getByRole('group', { name: 'Status' }))
    fireEvent.click(grupoDeStatus.getByRole('checkbox', { name: 'Não está mais na lista' }))
    await waitFor(() => expect(localizacao()).toBe('/pedidos?material=abc'))
  })

  it('valor valido misturado a lixo na URL manda so o valido', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?material=abc,5,,7&status=Aberto,Qualquer')
    await screen.findByText('PED-001')

    const primeira = listagens(fetchMock)[0]
    expect(primeira.searchParams.get('material')).toBe('5,7')
    expect(primeira.searchParams.get('status')).toBe('Aberto')
  })

  it('pagina da URL alem do fim recua para a ultima', async () => {
    // O servidor devolve 200 com `itens` vazio e o `total` verdadeiro (3 = uma página só); o clamp
    // do hook recua para a página 1 e a URL perde o `pagina`.
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([], 3, 99)) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?pagina=99')

    await waitFor(() => expect(listagens(fetchMock).at(-1)!.searchParams.get('pagina')).toBe('1'))
    await waitFor(() => expect(localizacao()).toBe('/pedidos'))
  })

  it('pagina com os controles de paginacao', async () => {
    vi.stubGlobal('fetch', api({ '/api/pedidos': () => respostaJson(pagina([PEDIDO], 45)) }))

    renderizar()

    expect(await screen.findByText('Página 1 de 3 — 45 no total')).toBeTruthy()
  })

  it('ir para a proxima pagina consulta a pagina seguinte e escreve na URL', async () => {
    const fetchMock = api({ '/api/pedidos': () => respostaJson(pagina([PEDIDO], 45)) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?status=Aberto')
    await screen.findByText('Página 1 de 3 — 45 no total')
    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))

    await waitFor(() => expect(listagens(fetchMock).at(-1)!.searchParams.get('pagina')).toBe('2'))
    // A escrita da página preserva o filtro que já estava na URL.
    await waitFor(() => expect(localizacao()).toBe('/pedidos?status=Aberto&pagina=2'))
  })

  it('abrir um pedido recarrega mantendo busca e filtros', async () => {
    const fetchMock = api()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/pedidos?busca=CH&status=Aberto&material=3')
    await screen.findByText('PED-001')
    expect(listagens(fetchMock)).toHaveLength(1)

    fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-002' } })
    fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: 'Fábrica Beta' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    await waitFor(() => expect(listagens(fetchMock)).toHaveLength(2))
    const [antes, depois] = listagens(fetchMock)
    expect(depois.search).toBe(antes.search)
    expect(depois.searchParams.get('busca')).toBe('CH')
    expect(depois.searchParams.get('status')).toBe('Aberto')
    expect(depois.searchParams.get('material')).toBe('3')
  })
})
