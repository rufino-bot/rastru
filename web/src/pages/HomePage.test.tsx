// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Contagem por status: Aberto 2, EmProducao 1, AguardandoExpedicao 0, Concluido 1, Cancelado 1.
// O zero de AguardandoExpedicao é o caso que a spec §3.1 exige mostrar, e ele SÓ existe porque
// nenhum pedido do fixture tem esse status — se alguém acrescentar um, o teste do zero morre e
// aponta para cá.
const PEDIDOS = [
  { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 2, numero: 'PED-002', cliente: 'Beta', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-05T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 3, numero: 'PED-003', cliente: 'Gama', tipo: 'Normal', status: 'Aberto', dataAbertura: '2026-08-01T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 4, numero: 'PED-004', cliente: 'Delta', tipo: 'Normal', status: 'EmProducao', dataAbertura: '2026-08-03T09:00:00-03:00', criadoPorUsuarioId: 1 },
  { id: 5, numero: 'PED-005', cliente: 'Epsilon', tipo: 'Normal', status: 'Cancelado', dataAbertura: '2026-07-20T09:00:00-03:00', criadoPorUsuarioId: 1 },
]

function apiCompleta() {
  return fetchPorRota({
    // `total: 41` com UM item: é o `total` sob o filtro que vale, não `itens.length`. Se a tela
    // ler o array, ela mostra "1 componente" num catálogo de 41 — e é justamente por isso que a
    // chamada usa `tamanho: 1`.
    '/api/componentes': () => respostaJson({ itens: [{ id: 1, codigo: 'C', descricao: 'D', tipo: 'Bruto', ativo: true }], total: 41, pagina: 1, tamanho: 1 }),
    '/api/pedidos': () => respostaJson(PEDIDOS),
    '/api/materiais': () => respostaJson([{ id: 1, codigo: 'M1', descricao: 'Aço', unidadeMedida: 'KG', ativo: true }]),
    '/api/setores': () => respostaJson([
      { id: 1, nome: 'Corte', ativo: true },
      { id: 2, nome: 'Solda', ativo: true },
    ]),
  })
}

describe('HomePage', () => {
  beforeEach(() => {
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

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra o total de componentes vindo do campo total, não do tamanho da página', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(await screen.findByText('41')).toBeTruthy()
  })

  it('conta só os pedidos abertos', async () => {
    // Cinco pedidos, DOIS Abertos: contar `.length` daria 5 e a tela mentiria.
    // `within(cartao).getByText('2')`, não `textContent.toContain('2')`: o cartão de componentes
    // mostra "41", e um `toContain` passaria com a fiação de pedidos e componentes trocada.
    // `getByText` casa o nó de texto inteiro, então discrimina.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('2')).toBeTruthy()
  })

  it('mostra as contagens de materiais e setores', async () => {
    // Mesmo motivo do teste acima: `getByText` escopado ao cartão, não `textContent.toContain`.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(within(screen.getByText('materiais ativos').closest('a')!).getByText('1')).toBeTruthy()
    expect(within(screen.getByText('setores ativos').closest('a')!).getByText('2')).toBeTruthy()
  })

  it('pede só um item ao contar componentes', async () => {
    // A propriedade que torna o cartão barato: `tamanho: 1`. Se alguém trocar por 20, a Home passa
    // a trafegar 20 componentes para mostrar um número.
    const fetchMock = apiCompleta()
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const url = String(fetchMock.mock.calls.find((c) => String(c[0]).startsWith('/api/componentes'))![0])
    expect(url).toContain('tamanho=1')
  })

  it('leva a cada área pelo cartão', async () => {
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toEqual(expect.arrayContaining(['/pedidos', '/componentes', '/materiais', '/setores']))
  })

  it('mostra traço, e não zero, enquanto os números não chegaram', () => {
    // "0 pedidos abertos" numa fábrica que tem pedidos é uma afirmação falsa. O traço diz "ainda
    // não sei", que é a verdade naquele instante.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.getAllByText('—').length).toBe(4)
  })

  it('explica a falha quando alguma das listagens não responde', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'x' }, 500),
      '/api/pedidos': () => respostaJson(PEDIDOS),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(
      await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.'),
    ).toBeTruthy()
  })

  it('mostra os cinco status com a contagem, inclusive o que esta zerado', async () => {
    // O zerado (AguardandoExpedicao) é o caso que a spec §3.1 nomeia: omitir um status porque não
    // há nenhum pedido nele faria o leitor concluir que aquele estado não existe no sistema.
    // A pílula traz rótulo e contagem NUMA STRING SÓ — ver o comentário da renderização na
    // HomePage: contagem em elemento próprio colidiria com o `getByText('2')` do teste acima.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('Aberto 2')).toBeTruthy()
    expect(within(cartao).getByText('EmProducao 1')).toBeTruthy()
    expect(within(cartao).getByText('AguardandoExpedicao 0')).toBeTruthy()
    expect(within(cartao).getByText('Concluido 1')).toBeTruthy()
    expect(within(cartao).getByText('Cancelado 1')).toBeTruthy()
  })

  // O nome diz SÓ o que este teste prova. Ele não afirma "e preserva a reserva na linha de
  // pedido": essa metade é estruturalmente improvável aqui — a seção "há mais tempo" exclui
  // `Concluido`/`Cancelado` por definição, e o `EmProducao` que sobra já é neutro, igual ao padrão
  // da `Pilula`. Quem prova a reserva são `LinhaDePedido.test.tsx` e `PedidosPage.test.tsx`, e o
  // comentário no fim deste teste aponta para lá.
  it('nao usa cor de estado no resumo por status do cartao de Pedidos', async () => {
    // Achado da Fase 1E: `Concluido 0` saia verde e `Cancelado 0` saia vermelho no resumo, porque
    // a pílula do resumo herdava `tomDoStatus`. Nenhuma das três guardas de tema
    // (`semCorForaDaPaleta`, `contraste`, `semModificadorDeOpacidadeEmCor`) mede SEMÂNTICA — o
    // resumo podia ficar colorido para sempre sem que nenhuma delas acusasse. Achado olhando a
    // tela em 375px, não por teste — daí este teste.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    const pilulasDoResumo = [
      within(cartao).getByText('Aberto 2'),
      within(cartao).getByText('EmProducao 1'),
      within(cartao).getByText('AguardandoExpedicao 0'),
      within(cartao).getByText('Concluido 1'),
      within(cartao).getByText('Cancelado 1'),
    ]
    for (const pilula of pilulasDoResumo) {
      expect(pilula.className).not.toMatch(/positivo-/)
      expect(pilula.className).not.toMatch(/negativo-/)
    }

    // A reserva não sumiu do sistema — só do resumo. Na seção "há mais tempo" a pílula É o estado
    // de um pedido concreto (via `LinhaDePedido`, que continua chamando `tomDoStatus`), não
    // rótulo de contagem — e essa distinção é o que faz o vermelho continuar certo ali.
    // O fixture desta seção só tem status NÃO encerrados (`ENCERRADOS` exclui Concluido/Cancelado
    // dela por definição — ver o filtro de `maisAntigos`), então não há como provar aqui a cor
    // positiva/negativa em si: essa prova já existe em `LinhaDePedido.test.tsx`
    // ('reserva verde para Concluido e vermelho para Cancelado...') e em `PedidosPage.test.tsx`
    // ('mostra o status como pílula, com o tom certo por status'). O que dá para provar aqui,
    // com o fixture que existe, é que a pílula da seção continua sendo uma `Pilula` de verdade
    // tingida pelo tom que `tomDoStatus` devolve para esse status (neutro, para os três status
    // que aparecem nesta seção) — e não texto solto sem classe nenhuma, que uma correção afoita
    // na linha errada poderia produzir.
    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const pilulaNaSecao = within(secao).getByText('EmProducao')
    expect(pilulaNaSecao.className).toMatch(/bg-acao-fundo/)
    expect(pilulaNaSecao.className).toMatch(/text-acao\b/)
  })

  it('nao mostra o resumo por status enquanto os numeros nao chegaram', () => {
    // Cinco zeros seriam cinco afirmações falsas; cinco traços seriam ruído (o número grande do
    // cartão já diz "—"). O resumo simplesmente não existe até o dado chegar.
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.queryByText(/^Aberto \d/)).toBeNull()
    expect(screen.queryByText(/^AguardandoExpedicao \d/)).toBeNull()
  })

  it('nao aninha link dentro do cartao de pedidos', async () => {
    // Requisito explícito da spec §3.1: o cartão INTEIRO é um `<Link>`, então uma pílula clicável
    // ali dentro seria `<a>` dentro de `<a>` — HTML inválido, e o navegador desmonta a árvore de
    // um jeito que quebra a navegação por teclado. Esta asserção morre no dia em que alguém
    // "melhorar" o resumo tornando cada status filtrável.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).queryAllByRole('link')).toHaveLength(0)
  })

  it('lista os pedidos abertos ha mais tempo, do mais antigo para o mais novo', async () => {
    // Fixture: PED-003 (08-01) < PED-004 (08-03) < PED-001 (08-06) entre os NÃO encerrados.
    // Ordem alfabética de `numero` daria PED-001/003/004 — se a implementação esquecer o `sort`,
    // o React renderiza na ordem do array e esta asserção é a que pega.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const linhas = within(secao).getAllByRole('listitem').map((li) => li.textContent)
    expect(linhas[0]).toContain('PED-003')
    expect(linhas[1]).toContain('PED-004')
    expect(linhas[2]).toContain('PED-001')
  })

  it('deixa Concluido e Cancelado fora da lista de ha mais tempo', async () => {
    // PED-005 (Cancelado) é o MAIS ANTIGO do fixture (07-20). Se o filtro sumir, ele encabeça a
    // lista — e a Home passa a dizer que um pedido cancelado está "parado há mais tempo".
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    expect(within(secao).queryByText(/PED-005/)).toBeNull()   // Cancelado, e o mais antigo de todos
    expect(within(secao).queryByText(/PED-002/)).toBeNull()   // Concluido
    expect(within(secao).getAllByRole('listitem')).toHaveLength(3)
  })

  it('para em cinco mesmo havendo mais pedidos elegiveis', async () => {
    const oito = Array.from({ length: 8 }, (_, i) => ({
      id: i + 1, numero: `PED-${String(i + 1).padStart(3, '0')}`, cliente: 'Cliente',
      tipo: 'Normal', status: 'Aberto',
      // Dias 01 a 08: o mais antigo é PED-001 e o corte tem de deixar PED-006..008 de fora.
      dataAbertura: `2026-08-0${i + 1}T09:00:00-03:00`, criadoPorUsuarioId: 1,
    }))
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos': () => respostaJson(oito),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    expect(within(secao).getAllByRole('listitem')).toHaveLength(5)
    expect(within(secao).queryByText(/PED-006/)).toBeNull()
  })

  it('leva ao pedido certo por cada linha da lista', async () => {
    // O par número→id: uma implementação que use o índice do array no lugar de `p.id` acerta por
    // acidente quando os ids são 1..n em ordem. Aqui o mais antigo é o id 3, não o id 1.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const primeira = within(secao).getAllByRole('listitem')[0]
    expect(within(primeira).getByRole('link').getAttribute('href')).toBe('/pedidos/3')
  })

  it('diz que nao ha pedido aberto, em vez de sumir, quando a leitura deu certo e a lista e vazia', async () => {
    // A distinção que a spec §3.2 exige: "não há nada" tem de soar diferente de "não consegui
    // ler". Aqui a leitura FOI bem-sucedida — todos os pedidos estão encerrados.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos': () => respostaJson([
        { id: 1, numero: 'PED-001', cliente: 'Alfa', tipo: 'Normal', status: 'Concluido', dataAbertura: '2026-08-06T09:00:00-03:00', criadoPorUsuarioId: 1 },
      ]),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByText('Nenhum pedido em aberto.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Pedidos abertos há mais tempo' })).toBeNull()
    // A descrição é a metade que distingue esta causa da do teste seguinte: aqui HÁ pedido
    // cadastrado, e o que não há é pedido em aberto.
    expect(screen.getByText('Todos os pedidos cadastrados estão concluídos ou cancelados.')).toBeTruthy()
  })

  it('distingue cadastro vazio de todos encerrados, que caem no mesmo vazio', async () => {
    // Os dois caminhos chegam a `maisAntigos.length === 0`, e o título é o mesmo nos dois. Sem
    // esta ramificação a tela afirmaria que "todos os pedidos cadastrados estão concluídos ou
    // cancelados" sobre um cadastro que não tem pedido nenhum — o `CLAUDE.md` exige que o vazio
    // distinga "não achei" de "não há nada".
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos': () => respostaJson([]),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    expect(screen.getByText('Nenhum pedido em aberto.')).toBeTruthy()
    expect(screen.getByText('Nenhum pedido foi cadastrado ainda.')).toBeTruthy()
    expect(screen.queryByText('Todos os pedidos cadastrados estão concluídos ou cancelados.')).toBeNull()
  })

  it('nao mostra a secao — nem vazia — quando a leitura falhou', async () => {
    // O padrão que já pegou DUAS vezes na 1C (Tasks 8 e 10): estado vazio renderizado junto do
    // banner de erro, dizendo "não há pedidos abertos" quando a verdade é "não consegui
    // perguntar". A seção inteira some enquanto houver erro.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'x' }, 500),
      '/api/pedidos': () => respostaJson(PEDIDOS),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')

    expect(screen.queryByText('Nenhum pedido em aberto.')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Pedidos abertos há mais tempo' })).toBeNull()
  })
})
