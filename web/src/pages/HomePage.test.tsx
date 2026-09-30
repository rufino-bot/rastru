// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// Resumo equivalente a cinco Pedidos: Aberto 2, EmProducao 1, AguardandoExpedicao 0, Concluido 1,
// Cancelado 1. O zero de AguardandoExpedicao é o caso que a spec §3.1 exige mostrar, e o servidor
// SEMPRE manda os cinco status, zeros inclusive — a Home não completa nada.
const STATUS_NA_ORDEM = ['Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado'] as const

function pedido(id: number, numero: string, cliente: string, status: string, dataAbertura: string) {
  return { id, numero, cliente, tipo: 'Normal', status, dataAbertura, criadoPorUsuarioId: 1, pausa: null }
}

function resumoCom(
  quantidades: Partial<Record<(typeof STATUS_NA_ORDEM)[number], number>>,
  maisAntigosAbertos: ReturnType<typeof pedido>[] = [],
) {
  return {
    porStatus: STATUS_NA_ORDEM.map((status) => ({ status, quantidade: quantidades[status] ?? 0 })),
    maisAntigosAbertos,
  }
}

// Na ordem em que o servidor os manda: do mais antigo ao mais novo, só os não encerrados.
const MAIS_ANTIGOS = [
  pedido(3, 'PED-003', 'Gama', 'Aberto', '2026-08-01T09:00:00-03:00'),
  pedido(4, 'PED-004', 'Delta', 'EmProducao', '2026-08-03T09:00:00-03:00'),
  pedido(1, 'PED-001', 'Alfa', 'Aberto', '2026-08-06T09:00:00-03:00'),
]
const RESUMO = resumoCom({ Aberto: 2, EmProducao: 1, Concluido: 1, Cancelado: 1 }, MAIS_ANTIGOS)

function apiCompleta() {
  return fetchPorRota({
    // `total: 41` com UM item: é o `total` sob o filtro que vale, não `itens.length`. Se a tela
    // ler o array, ela mostra "1 componente" num catálogo de 41 — e é justamente por isso que a
    // chamada usa `tamanho: 1`.
    '/api/componentes': () => respostaJson({ itens: [{ id: 1, codigo: 'C', descricao: 'D', tipo: 'Bruto', ativo: true }], total: 41, pagina: 1, tamanho: 1 }),
    '/api/pedidos/resumo': () => respostaJson(RESUMO),
    '/api/materiais': () => respostaJson([{ id: 1, codigo: 'M1', descricao: 'Aço', unidadeMedida: 'KG', ativo: true }]),
    '/api/setores': () => respostaJson([
      { id: 1, nome: 'Corte', ativo: true, atividade: null },
      { id: 2, nome: 'Solda', ativo: true, atividade: null },
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
    // Cinco pedidos: dois Abertos, um EmProducao, um Concluido e um Cancelado. Aberto é todo Pedido
    // que não está encerrado, então são 3 — contar `.length` daria 5, e contar só `status ===
    // 'Aberto'` daria 2 (o que a tela fazia até a Fase 3 começar a passar Pedido a EmProducao).
    // `within(cartao).getByText('3')`, não `textContent.toContain('3')`: o cartão de componentes
    // mostra "41", e um `toContain` passaria com a fiação de pedidos e componentes trocada.
    // `getByText` casa o nó de texto inteiro, então discrimina.
    vi.stubGlobal('fetch', apiCompleta())

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('3')).toBeTruthy()
  })

  it('conta como abertos todos os status fora de Concluido e Cancelado', async () => {
    // Contagens todas distintas (5, 7, 11, 100, 200): a soma dos três não encerrados (23) não se
    // confunde com nenhuma outra combinação. Só este teste separa "fora de Concluido e Cancelado"
    // de uma conta que esqueça um status aberto — o número grande é a SOMA, não `porStatus.Aberto`.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos/resumo': () => respostaJson(resumoCom({
        Aberto: 5, EmProducao: 7, AguardandoExpedicao: 11, Concluido: 100, Cancelado: 200,
      })),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('23')).toBeTruthy()
  })

  // Metade FRONT da troca de guarda que substituiu o teste antigo de `cadastros.test.ts` ("devolve o
  // conjunto inteiro de pedidos, nao uma pagina"): a Home não pode voltar a depender de uma lista
  // de Pedidos. A metade do backend é o resumo contar além do tamanho de página.
  it('a Home le o resumo e nao uma lista de pedidos', async () => {
    // O mapa NÃO tem '/api/pedidos': se a Home chamar a lista, `fetchPorRota` rejeita e a tela
    // mostra o banner de erro. 30 + 12 = 42, mais do que qualquer página de 20.
    const fetchMock = fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 7, pagina: 1, tamanho: 1 }),
      '/api/pedidos/resumo': () => respostaJson(resumoCom({ Aberto: 30, EmProducao: 12 })),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('7')

    const cartao = screen.getByText('pedidos abertos').closest('a')!
    expect(within(cartao).getByText('42')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
    const caminhos = fetchMock.mock.calls.map((c) => String(c[0]).split('?')[0])
    expect(caminhos).toContain('/api/pedidos/resumo')
    expect(caminhos).not.toContain('/api/pedidos')
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
      '/api/pedidos/resumo': () => respostaJson(RESUMO),
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
    expect(within(cartao).getByText('Em produção 1')).toBeTruthy()
    expect(within(cartao).getByText('Aguardando expedição 0')).toBeTruthy()
    expect(within(cartao).getByText('Concluído 1')).toBeTruthy()
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
      within(cartao).getByText('Em produção 1'),
      within(cartao).getByText('Aguardando expedição 0'),
      within(cartao).getByText('Concluído 1'),
      within(cartao).getByText('Cancelado 1'),
    ]
    for (const pilula of pilulasDoResumo) {
      // Neutro afirmado token a token, e não só "nem verde nem vermelho": o âmbar (`atencao-`)
      // também é cor de estado, e uma negação de duas cores não o vê.
      const classes = pilula.className.split(/\s+/)
      expect(classes).toContain('bg-acao-fundo')
      expect(classes).toContain('text-acao')
      expect(pilula.className).not.toMatch(/positivo-/)
      expect(pilula.className).not.toMatch(/negativo-/)
    }

    // A reserva não sumiu do sistema — só do resumo. Na seção "há mais tempo" a pílula É o estado
    // de um pedido concreto (via `LinhaDePedido`, que continua chamando `tomDoStatus`), não
    // rótulo de contagem — e essa distinção é o que faz o vermelho continuar certo ali.
    // O fixture desta seção só tem status NÃO encerrados (o servidor exclui Concluido/Cancelado
    // dela por definição), então não há como provar aqui a cor
    // positiva/negativa em si: essa prova já existe em `LinhaDePedido.test.tsx`
    // ('reserva verde para Concluido e vermelho para Cancelado...') e em `PedidosPage.test.tsx`
    // ('mostra o status como pílula, com o tom certo por status'). O que dá para provar aqui,
    // com o fixture que existe, é que a pílula da seção continua sendo uma `Pilula` de verdade
    // tingida pelo tom que `tomDoStatus` devolve para esse status (neutro, para os três status
    // que aparecem nesta seção) — e não texto solto sem classe nenhuma, que uma correção afoita
    // na linha errada poderia produzir.
    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const pilulaNaSecao = within(secao).getByText('Em produção')
    expect(pilulaNaSecao.className).toMatch(/bg-acao-fundo/)
    expect(pilulaNaSecao.className).toMatch(/text-acao\b/)
  })

  it('nao mostra o resumo por status enquanto os numeros nao chegaram', () => {
    // Cinco zeros seriam cinco afirmações falsas; cinco traços seriam ruído (o número grande do
    // cartão já diz "—"). O resumo simplesmente não existe até o dado chegar.
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><HomePage /></MemoryRouter>)

    expect(screen.queryByText(/^Aberto \d/)).toBeNull()
    expect(screen.queryByText(/^Aguardando expedição \d/)).toBeNull()
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

  // A regra dos "abertos há mais tempo" (só os não encerrados, do mais antigo ao mais novo, no máximo
  // cinco) mora no servidor, e quem a prova é `PedidoRepositoryTests.Mais_antigos_deixa_encerrados_de_fora_e_para_no_limite`.
  // Aqui a Home só apresenta o que o resumo manda — e por isso a ordem da fixture é DELIBERADAMENTE
  // não cronológica: uma Home que reordenasse por data no cliente trocaria PED-004 e PED-003.
  it('mostra os mais antigos na ordem em que o resumo os manda', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 41, pagina: 1, tamanho: 1 }),
      '/api/pedidos/resumo': () => respostaJson(resumoCom({ Aberto: 2, EmProducao: 1 }, [
        pedido(4, 'PED-004', 'Delta', 'EmProducao', '2026-08-03T09:00:00-03:00'),
        pedido(3, 'PED-003', 'Gama', 'Aberto', '2026-08-01T09:00:00-03:00'),
        pedido(1, 'PED-001', 'Alfa', 'Aberto', '2026-08-06T09:00:00-03:00'),
      ])),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('41')

    const secao = screen.getByRole('list', { name: 'Pedidos abertos há mais tempo' })
    const linhas = within(secao).getAllByRole('listitem').map((li) => li.textContent)
    expect(linhas).toHaveLength(3)
    expect(linhas[0]).toContain('PED-004')
    expect(linhas[1]).toContain('PED-003')
    expect(linhas[2]).toContain('PED-001')
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
      '/api/pedidos/resumo': () => respostaJson(resumoCom({ Concluido: 1 })),
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
      '/api/pedidos/resumo': () => respostaJson(resumoCom({})),
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
      '/api/pedidos/resumo': () => respostaJson(RESUMO),
      '/api/materiais': () => respostaJson([]),
      '/api/setores': () => respostaJson([]),
    }))

    render(<MemoryRouter><HomePage /></MemoryRouter>)
    await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')

    expect(screen.queryByText('Nenhum pedido em aberto.')).toBeNull()
    expect(screen.queryByRole('list', { name: 'Pedidos abertos há mais tempo' })).toBeNull()
  })
})
