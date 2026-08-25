// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useNavigate } from 'react-router-dom'
import { ComponenteDetalhePage } from './ComponenteDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão governa o que a tela mostra (Task 11): `usePodeEscrever('componentes')`
// esconde formulário e Salvar de cada seção. Molde de `PedidoDetalhePage.test.tsx`/`SetoresPage.test.tsx`
// — variável mutável, escrita pelo 2º parâmetro de `renderizarNaRota` antes do `render`.
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const COMPONENTE = { id: 7, codigo: 'CH-100', descricao: 'Chapa lateral', tipo: 'Fabricado', ativo: true }
const FILHOS = [{ id: 1, componenteFilhoId: 3, codigo: 'PA-010', descricao: 'Parafuso M8', quantidadePadrao: 4 }]
const MATERIAIS = [{ id: 2, materialId: 5, codigo: 'CH-3', descricao: 'Chapa 3mm', unidadeMedida: 'KG', quantidadePadrao: 1.5 }]
const ROTEIRO = [
  { id: 3, setorId: 20, nome: 'Corte', ordem: 1 },
  { id: 4, setorId: 21, nome: 'Solda', ordem: 2 },
  { id: 5, setorId: 20, nome: 'Corte', ordem: 3 },
]

// Defeito 2 do brief: a Task 11 acrescenta 3 chamadas NA MONTAGEM (com o perfil default 'PCP', que
// escreve — o rodapé de escrita monta e elas disparam): a busca de componentes do
// `SeletorComBusca` (filhos), `listarMateriais(false)` e `listarSetores(false)` (catálogos dos
// dois `<select>`). Sem declarar as 3, os 11 testes de leitura da Task 10 CAEM — `fetchPorRota`
// rejeita rota não declarada em vez de devolver `undefined`.
const COMPONENTES_BUSCA = {
  itens: [{ id: 2, codigo: 'CH-200', descricao: 'Chapa frontal', tipo: 'Bruto', ativo: true }],
  total: 1,
  pagina: 1,
  tamanho: 20,
}
const MATERIAIS_CADASTRO = [{ id: 5, codigo: 'CH-3', descricao: 'Chapa 3mm', unidadeMedida: 'KG', ativo: true }]
const SETORES_CADASTRO = [
  { id: 20, nome: 'Corte', ativo: true },
  { id: 21, nome: 'Solda', ativo: true },
]

/** As 7 rotas GET que a tela pode chamar no caminho feliz, com o perfil que escreve. Compartilhado
    entre `apiCompleta()` e os helpers de gravação abaixo, para não divergir em dois lugares. */
const LEITURAS: Record<string, () => Response> = {
  '/api/componentes/7': () => respostaJson(COMPONENTE),
  '/api/componentes/7/filhos-padrao': () => respostaJson(FILHOS),
  '/api/componentes/7/materiais-padrao': () => respostaJson(MATERIAIS),
  '/api/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
  '/api/componentes': () => respostaJson(COMPONENTES_BUSCA),
  '/api/materiais': () => respostaJson(MATERIAIS_CADASTRO),
  '/api/setores': () => respostaJson(SETORES_CADASTRO),
}

/** Todas as chamadas que a tela pode fazer, no caminho feliz. Chaves COM o prefixo `/api` (defeito 3). */
function apiCompleta() {
  return fetchPorRota(LEITURAS)
}

// Molde de PedidoDetalhePage.test.tsx: a tela lê `:id` da rota, então precisa nascer DENTRO de
// uma rota casada — renderizar o componente solto deixaria `useParams()` vazio e `componenteId`
// viraria NaN mesmo quando o teste queria um id válido.
//
// Defeito 3 do brief: `renderizarNaRota` ganha um 2º parâmetro (perfil), que escreve na variável
// mutável `perfil` ANTES do `render` — não um segundo mock de `AuthContext`, o mock já existe.
function renderizarNaRota(caminho: string, perfilDoTeste: string = 'PCP') {
  perfil = perfilDoTeste
  return render(
    <MemoryRouter initialEntries={[caminho]}>
      <Routes>
        <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function ehLeitura(init?: RequestInit): boolean {
  return !init || !init.method || init.method === 'GET'
}

/**
 * Roteador de gravação: GET segue o mapa de `LEITURAS`; qualquer outro método (a tela só usa POST)
 * empilha `{ caminho, corpo }` em `posts` — SEM responder pelo body do que a tela mandou, porque
 * `gravar()` (`receitaPadrao.ts`) sempre serializa `{ linhas }`, nunca outra coisa.
 */
function fetchPorRotaGravando(posts: { caminho: string; corpo: unknown }[]) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (!ehLeitura(init)) {
      posts.push({ caminho, corpo: init?.body ? JSON.parse(String(init.body)) : undefined })
      return Promise.resolve(respostaJson([]))
    }
    const entrada = LEITURAS[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}

/** POST fica pendente até o teste chamar o `resolve` que `receberLiberador` recebe — para provar
    que o botão desabilita ENQUANTO a gravação está em voo, antes de qualquer resposta chegar. */
function fetchComPostPendente(receberLiberador: (liberar: (r: Response) => void) => void) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (!ehLeitura(init)) {
      return new Promise<Response>((resolve) => { receberLiberador(resolve) })
    }
    const entrada = LEITURAS[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}

/** POST sempre falha com `status`. Desde `e15eb60`, `ler()` (`receitaPadrao.ts:70-73`) LÊ o corpo
    em toda resposta de erro — o oposto do que este helper afirmava antes —, então `corpo` deixa
    de ser opcional em espírito: quem quiser exercitar o caminho de `detalhe` (a mensagem do
    servidor chegando à tela) passa um corpo com `erro`; quem não passa nada mantém o caminho
    antigo (corpo vazio, cai no fallback da seção). */
function fetchComPostQueFalha(status: number, corpo: unknown = {}) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (!ehLeitura(init)) return Promise.resolve(respostaJson(corpo, status))
    const entrada = LEITURAS[caminho]
    if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
    return Promise.resolve(entrada())
  })
}

/**
 * Escolhe `codigo` no `SeletorComBusca` da seção de filhos e preenche a quantidade. Não digita
 * nada no combobox: a carga inicial do `SeletorComBusca` (`busca === ''`, no mount, SEM debounce)
 * já traz `COMPONENTES_BUSCA` inteiro, então abrir o painel já mostra a opção — evita depender de
 * `vi.useFakeTimers()`/`avancar()` (molde de `ComponentesPage.test.tsx`) só para isto.
 */
async function adicionarFilho(codigo: string, quantidade: number) {
  const secao = screen.getByRole('region', { name: /componentes filhos/i })
  fireEvent.click(within(secao).getByRole('combobox'))
  fireEvent.click(await within(secao).findByText(codigo))
  fireEvent.change(within(secao).getByLabelText(/quantidade/i), { target: { value: String(quantidade) } })
  fireEvent.click(within(secao).getByRole('button', { name: 'Adicionar' }))
}

describe('ComponenteDetalhePage — leitura', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra as três seções com o que a API devolveu', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText('PA-010')).toBeTruthy()
    expect(await screen.findByText('CH-3')).toBeTruthy()
    // Escopado na `<ul>` da lista (`role="list"`, molde HTML-AAM), e não em `screen` global: a
    // Task 11 acrescenta um `<select>` de setores na MESMA seção, cujas `<option>` repetem o nome
    // do setor como texto puro — sem o escopo, `findByText('Solda')` acha DOIS elementos (a linha
    // da lista e a opção do select) e lança. Medido ao rodar a suíte após a Task 11: era passiva
    // antes do `<select>` existir, e a Task 11 é exatamente o que introduz a colisão.
    const listaRoteiro = await screen.findByRole('list', { name: 'Roteiro' })
    expect(await within(listaRoteiro).findByText('Solda')).toBeTruthy()
  })

  /**
   * O MESMO setor em duas posições do roteiro tem que aparecer DUAS vezes — é retorno ao setor,
   * não duplicata. Uma tela que "deduplica por id de setor" some com a segunda passagem.
   */
  it('mostra o mesmo setor duas vezes quando o roteiro tem retorno ao setor', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    // Mesmo escopo do teste acima, pelo mesmo motivo: sem ele, a Task 11 conta também as
    // `<option>` do `<select>` de setores, que também repete "Corte" (dá 3, não 2).
    const listaRoteiro = await screen.findByRole('list', { name: 'Roteiro' })
    expect(within(listaRoteiro).getAllByText('Corte')).toHaveLength(2)
  })

  /**
   * Decisão 2 do brief: as três seções são landmarks nomeados (`aria-labelledby`), e a asserção
   * escopa com `within()` — sem isso, `getByRole('status')`/`findByText(/carregando/i)` global
   * lançaria por múltiplos matches (as 3 seções carregam ao mesmo tempo). Síncrono, molde I1: fetch
   * que nunca resolve, sem `await`, prova que o indicador está lá ANTES de qualquer resposta.
   */
  it('mostra o carregando em cada seção antes de a API responder', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    const secaoMateriais = screen.getByRole('region', { name: /materiais/i })
    const secaoRoteiro = screen.getByRole('region', { name: /roteiro/i })

    expect(within(secaoFilhos).getByRole('status').textContent).toBe('Carregando…')
    expect(within(secaoMateriais).getByRole('status').textContent).toBe('Carregando…')
    expect(within(secaoRoteiro).getByRole('status').textContent).toBe('Carregando…')
  })

  /** Texto que distingue "não achei" de "não há nada": o vazio aqui é "receita não montada". */
  it('mostra estado vazio por seção quando a receita está vazia', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes/7/filhos-padrao': () => respostaJson([]),
      '/api/componentes/7/materiais-padrao': () => respostaJson([]),
      '/api/componentes/7/roteiro-padrao': () => respostaJson([]),
      '/api/componentes/7': () => respostaJson(COMPONENTE),
    }))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    const secaoMateriais = screen.getByRole('region', { name: /materiais/i })
    const secaoRoteiro = screen.getByRole('region', { name: /roteiro/i })

    expect(await within(secaoFilhos).findByText(/nenhum componente filho/i)).toBeTruthy()
    expect(within(secaoMateriais).getByText(/nenhum material/i)).toBeTruthy()
    expect(within(secaoRoteiro).getByText(/nenhum setor no roteiro/i)).toBeTruthy()
  })

  /**
   * Todas as 4 buscas falham: cabeçalho + 3 seções, cada uma com o próprio alerta — por isso 4, e
   * por isso `getAllByRole` (um `findByRole('alert')` global lançaria por múltiplos matches).
   */
  it('mostra o erro em cada seção e no cabeçalho quando todas as cargas falham', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 500 }))))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    const secaoMateriais = screen.getByRole('region', { name: /materiais/i })
    const secaoRoteiro = screen.getByRole('region', { name: /roteiro/i })

    expect(await within(secaoFilhos).findByRole('alert')).toBeTruthy()
    expect(within(secaoMateriais).getByRole('alert')).toBeTruthy()
    expect(within(secaoRoteiro).getByRole('alert')).toBeTruthy()
    expect(screen.getAllByRole('alert')).toHaveLength(4)
  })

  /**
   * Vazio e erro são MUTUAMENTE EXCLUSIVOS. Sem a guarda `erro === null &&`, a falha na carga dos
   * filhos mostra "Nenhum componente filho" JUNTO do banner de erro — o Critical que a Fase 1D já
   * pagou duas vezes (Tasks 8 e 10). Não é para pagar uma terceira. Escopado na seção de filhos, o
   * alvo da mutação do Step 7.
   */
  it('não mostra estado vazio junto com o banner de erro na seção de filhos', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 500 }))))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    await within(secaoFilhos).findByRole('alert')

    expect(within(secaoFilhos).queryByText(/nenhum componente filho/i)).toBeNull()
  })

  it('mostra o código e a descrição do componente no cabeçalho', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findByText(/CH-100/)).toBeTruthy()
    expect(screen.getByText(/Chapa lateral/)).toBeTruthy()
  })

  /**
   * Decisão 3 do brief: `<Pagina titulo="Componente">` fixo desde o primeiro frame — a 4ª busca
   * (o componente, para o cabeçalho) pode estar em voo, e a página não pode ficar sem título
   * nesse meio tempo (`Pagina` exige `titulo: string`).
   */
  it('usa "Componente" como título da página antes de o cabeçalho carregar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))
    renderizarNaRota('/componentes/7')

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Componente')
  })

  /**
   * Decisão 3: o cabeçalho guarda seu PRÓPRIO erro, e uma falha nele não derruba as 3 seções — só
   * a busca do componente falha aqui, as outras três têm sucesso.
   */
  it('mostra o erro no cabeçalho quando só a busca do componente falha, sem derrubar as seções', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes/7': () => new Response(null, { status: 500 }),
      '/api/componentes/7/filhos-padrao': () => respostaJson(FILHOS),
      '/api/componentes/7/materiais-padrao': () => respostaJson(MATERIAIS),
      '/api/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
    }))
    renderizarNaRota('/componentes/7')

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(await screen.findByText('PA-010')).toBeTruthy()
  })

  /**
   * NÃO ESTÁ NO PLANO — é o teste que prova a decisão 2 ("cada seção com seu próprio estado" não é
   * afirmação sem prova). Materiais falha (500) e filhos responde 200, na MESMA renderização: se as
   * seções compartilhassem um `erro` só, a seção de filhos também mostraria o banner de erro em vez
   * da linha que carregou bem.
   */
  it('uma falha em materiais não apaga a lista de filhos que carregou bem', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes/7': () => respostaJson(COMPONENTE),
      '/api/componentes/7/filhos-padrao': () => respostaJson(FILHOS),
      '/api/componentes/7/materiais-padrao': () => new Response(null, { status: 500 }),
      '/api/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
    }))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    const secaoMateriais = screen.getByRole('region', { name: /materiais/i })

    expect(await within(secaoFilhos).findByText('PA-010')).toBeTruthy()
    expect(await within(secaoMateriais).findByRole('alert')).toBeTruthy()
  })

  /**
   * NÃO ESTÁ NO PLANO — `id` inválido (`/componentes/abc` → `NaN`). Decisão: a tela não tenta
   * buscar nada (as 4 chamadas dependeriam de um id que não existe) e mostra um banner só.
   */
  it('trata id inválido sem tentar buscar nada', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    render(
      <MemoryRouter initialEntries={['/componentes/abc']}>
        <Routes>
          <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Este componente não existe.')).toBeTruthy()
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

/**
 * A rota casada é a MESMA em `/componentes/1` e `/componentes/2` (só o `:id` muda), então
 * `<ComponenteDetalhePage>` NÃO desmonta na troca — é a mesma instância React recebendo um `id`
 * novo. É exatamente essa continuidade que o item 6 da review explora: sem cancelamento, o
 * `.then` de uma busca disparada para o id ANTIGO ainda escreve no estado depois da troca. Botão
 * de navegação real (não `initialEntries` novo, que REMONTARIA a árvore e não provaria nada).
 */
function TelaComNavegacaoParaOutroComponente() {
  const navigate = useNavigate()
  return (
    <>
      <button onClick={() => navigate('/componentes/2')}>Ir para o componente 2</button>
      <Routes>
        <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
      </Routes>
    </>
  )
}

describe('ComponenteDetalhePage — troca de :id (item 6 da review)', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('a resposta de filhos do id ANTIGO, atrasada, não sobrescreve a receita do id NOVO', async () => {
    let liberarFilhosDoUm: (r: Response) => void = () => {}
    const mapa: Record<string, () => Response | Promise<Response>> = {
      // A busca de filhos do componente 1 fica PENDURADA — só resolve quando o teste mandar,
      // DEPOIS da troca de rota.
      '/api/componentes/1/filhos-padrao': () => new Promise<Response>((resolve) => { liberarFilhosDoUm = resolve }),
      '/api/componentes/1': () => respostaJson({ id: 1, codigo: 'CH-001', descricao: 'Peça 1', tipo: 'Fabricado', ativo: true }),
      '/api/componentes/1/materiais-padrao': () => respostaJson([]),
      '/api/componentes/1/roteiro-padrao': () => respostaJson([]),
      '/api/componentes/2': () => respostaJson({ id: 2, codigo: 'CH-002', descricao: 'Peça 2', tipo: 'Fabricado', ativo: true }),
      '/api/componentes/2/filhos-padrao': () => respostaJson([
        { id: 9, componenteFilhoId: 30, codigo: 'PA-020', descricao: 'Parafuso da peça 2', quantidadePadrao: 1 },
      ]),
      '/api/componentes/2/materiais-padrao': () => respostaJson([]),
      '/api/componentes/2/roteiro-padrao': () => respostaJson([]),
      '/api/componentes': () => respostaJson(COMPONENTES_BUSCA),
      '/api/materiais': () => respostaJson(MATERIAIS_CADASTRO),
      '/api/setores': () => respostaJson(SETORES_CADASTRO),
    }
    vi.stubGlobal('fetch', vi.fn((url: string | URL) => {
      const caminho = String(url).split('?')[0]
      const entrada = mapa[caminho]
      if (!entrada) return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
      return Promise.resolve(entrada())
    }))

    render(
      <MemoryRouter initialEntries={['/componentes/1']}>
        <TelaComNavegacaoParaOutroComponente />
      </MemoryRouter>,
    )

    // A troca acontece com a busca de filhos de 1 ainda EM VOO.
    fireEvent.click(screen.getByRole('button', { name: /ir para o componente 2/i }))
    expect(await screen.findByText('PA-020')).toBeTruthy()

    // SÓ AGORA a resposta antiga chega. Sem a guarda de cancelamento, o `.then` sobrescreveria
    // `filhos` com a linha de 1 — e se o usuário salvasse a essa altura, o POST iria para
    // `/componentes/2/filhos-padrao` com a receita de 1 dentro.
    liberarFilhosDoUm(respostaJson([
      { id: 1, componenteFilhoId: 3, codigo: 'PA-010', descricao: 'Parafuso M8', quantidadePadrao: 4 },
    ]))
    await new Promise((r) => setTimeout(r, 0))

    expect(screen.queryByText('PA-010')).toBeNull()
    expect(screen.getByText('PA-020')).toBeTruthy()
  })
})

describe('ComponenteDetalhePage — escrita', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  /**
   * Item 2 da review (Tasks 10-12): a guarda `erro === null &&` na frente do `rodape`
   * (`ComponenteDetalhePage.tsx`, comentário acima dela) é load-bearing e não tinha teste. Se o
   * GET de uma seção falha, o estado fica `[]` — igual ao vazio legítimo. Sem a guarda, o rodapé
   * apareceria mesmo com a carga falha, o usuário adicionaria uma linha, e o POST mandaria SÓ essa
   * linha — apagando no servidor a receita que ele nunca chegou a ver. Mutação verificada: trocar
   * `{!carregando && erro === null && rodape}` por `{!carregando && rodape}` deixa este teste
   * vermelho (ver campanha de mutação no relatório do fix pass).
   */
  it('seção que falhou ao carregar não mostra o formulário de escrita', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes/7': () => respostaJson(COMPONENTE),
      '/api/componentes/7/filhos-padrao': () => new Response(null, { status: 500 }),
      '/api/componentes/7/materiais-padrao': () => respostaJson(MATERIAIS),
      '/api/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
      '/api/componentes': () => respostaJson(COMPONENTES_BUSCA),
      '/api/materiais': () => respostaJson(MATERIAIS_CADASTRO),
      '/api/setores': () => respostaJson(SETORES_CADASTRO),
    }))
    renderizarNaRota('/componentes/7')

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    await within(secaoFilhos).findByRole('alert')

    // Nem o combobox de busca de componente filho, nem o botão "Salvar componentes filhos": o
    // rodapé inteiro está ausente, não só desabilitado.
    expect(within(secaoFilhos).queryByRole('combobox')).toBeNull()
    expect(within(secaoFilhos).queryByRole('button', { name: /salvar componentes filhos/i })).toBeNull()
  })

  it('Salvar manda a lista inteira da seção, não só a linha nova', async () => {
    const posts: { caminho: string; corpo: unknown }[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')

    await adicionarFilho('CH-200', 2)
    fireEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    // A linha que JÁ existia continua no corpo: o POST substitui a receita INTEIRA, não só anexa.
    await vi.waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toEqual({
      caminho: '/api/componentes/7/filhos-padrao',
      corpo: {
        linhas: [
          { componenteFilhoId: 3, quantidadePadrao: 4 },
          { componenteFilhoId: 2, quantidadePadrao: 2 },
        ],
      },
    })
  })

  it('remover a última linha e salvar manda lista vazia', async () => {
    const posts: { caminho: string; corpo: unknown }[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')

    fireEvent.click(screen.getByRole('button', { name: /remover pa-010/i }))
    fireEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    // Lista vazia é o ÚNICO caminho de remoção que existe — não há DELETE de linha.
    await vi.waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toEqual({ caminho: '/api/componentes/7/filhos-padrao', corpo: { linhas: [] } })
  })

  it('Salvar começa desabilitado e habilita quando há alteração pendente', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')

    const salvar = screen.getByRole('button', { name: /salvar componentes filhos/i })
    expect(salvar.hasAttribute('disabled')).toBe(true)

    await adicionarFilho('CH-200', 2)
    expect(salvar.hasAttribute('disabled')).toBe(false)
  })

  it('desabilita Salvar enquanto a gravação está em voo', async () => {
    let liberar: (r: Response) => void = () => {}
    vi.stubGlobal('fetch', fetchComPostPendente((r) => { liberar = r }))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    const salvar = screen.getByRole('button', { name: /salvar componentes filhos/i })
    fireEvent.click(salvar)
    // Em voo: `sujo` ainda é `true`, então o que desabilita AQUI só pode ser `salvando`.
    expect(salvar.hasAttribute('disabled')).toBe(true)

    // Depois de responder, o botão continua desabilitado — mas agora por `!sujo`. Como as duas
    // causas produzem o mesmo atributo, o que se afirma no fim é que a resposta foi PROCESSADA: a
    // linha nova aparece na lista.
    liberar(respostaJson([
      { id: 1, componenteFilhoId: 3, codigo: 'PA-010', descricao: 'Parafuso M8', quantidadePadrao: 4 },
      { id: 9, componenteFilhoId: 2, codigo: 'CH-200', descricao: 'Chapa frontal', quantidadePadrao: 2 },
    ]))
    expect(await screen.findByText('CH-200')).toBeTruthy()
  })

  it('o erro de gravação aparece e a lista da tela não some', async () => {
    vi.stubGlobal('fetch', fetchComPostQueFalha(400))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    fireEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByText('PA-010')).toBeTruthy()
  })

  /**
   * Item 4 da review (Tasks 10-12): o fix pass `e15eb60` ligou `ErroDeApi.detalhe` à tela, mas
   * nenhum teste atravessava `ComponenteDetalhePage` — a integração "400 de ciclo -> banner da
   * seção com a frase do backend" era afirmação sem prova (só `erros.test.ts`/`receitaPadrao.test.ts`
   * exercitavam isso, em unidade). Junta-se ao item 3: removendo `if (e.detalhe) return e.detalhe`
   * de `erros.ts` este teste tem de morrer — sem essa linha o banner mostraria o fallback genérico
   * da seção, não a frase do servidor.
   */
  it('a mensagem de ciclo do servidor aparece na seção de filhos, não o fallback genérico', async () => {
    const cicloDoServidor = 'Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010.'
    vi.stubGlobal('fetch', fetchComPostQueFalha(400, { erro: cicloDoServidor }))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    fireEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    const secaoFilhos = screen.getByRole('region', { name: /componentes filhos/i })
    expect(await within(secaoFilhos).findByText(cicloDoServidor)).toBeTruthy()
  })

  /** Gating na AÇÃO, não no link: Operador LÊ a receita, não a edita. */
  it('Operador vê a receita mas não vê formulário nem Salvar', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7', 'Operador')

    expect(await screen.findByText('PA-010')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /salvar/i })).toBeNull()
    expect(screen.queryByRole('combobox')).toBeNull()
  })

  /**
   * O 403 é a fronteira REAL — esconder botão não é segurança. Se o backend recusar mesmo com o
   * botão visível (perfil desatualizado no front, por exemplo), a tela mostra a recusa em vez de
   * quebrar.
   */
  it('403 na gravação vira mensagem, não exceção', async () => {
    vi.stubGlobal('fetch', fetchComPostQueFalha(403))
    renderizarNaRota('/componentes/7')
    await screen.findByText('PA-010')
    await adicionarFilho('CH-200', 2)

    fireEvent.click(screen.getByRole('button', { name: /salvar componentes filhos/i }))

    expect(await screen.findByRole('alert')).toBeTruthy()
  })

  /**
   * Item 1 da review (Tasks 10-12): nenhum dos 19 testes anteriores clicava em "Salvar materiais"
   * nem citava `/api/componentes/7/materiais-padrao` como destino de POST — `aoAdicionarMaterial`,
   * `removerMaterial` e `salvarMateriais` podiam virar no-op, ou gravar na rota ERRADA (por
   * exemplo, via `salvarFilhosPadrao`), sem matar teste nenhum. Molde do teste de filhos
   * (`Salvar manda a lista inteira da seção, não só a linha nova`), mas na seção de Materiais.
   */
  it('Salvar materiais manda a lista inteira, no caminho e no corpo certos', async () => {
    const posts: { caminho: string; corpo: unknown }[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7')
    await screen.findByText('CH-3')

    const secaoMateriais = screen.getByRole('region', { name: /^materiais$/i })
    fireEvent.change(within(secaoMateriais).getByLabelText(/material/i), { target: { value: '5' } })
    fireEvent.change(within(secaoMateriais).getByLabelText(/quantidade/i), { target: { value: '3' } })
    fireEvent.click(within(secaoMateriais).getByRole('button', { name: 'Adicionar' }))
    fireEvent.click(within(secaoMateriais).getByRole('button', { name: /salvar materiais/i }))

    // A linha que já existia continua no corpo, e o caminho é o de MATERIAIS — não o de filhos
    // nem o de roteiro, que são os três únicos destinos que os testes anteriores afirmavam.
    await vi.waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toEqual({
      caminho: '/api/componentes/7/materiais-padrao',
      corpo: {
        linhas: [
          { materialId: 5, quantidadePadrao: 1.5 },
          { materialId: 5, quantidadePadrao: 3 },
        ],
      },
    })
  })

  it('o roteiro é salvo na ordem da tela, só com setorId', async () => {
    const posts: { caminho: string; corpo: unknown }[] = []
    vi.stubGlobal('fetch', fetchPorRotaGravando(posts))
    renderizarNaRota('/componentes/7')
    // Escopado na lista, não em `screen`: o `<select>` de setores do rodapé (mesma seção) repete
    // "Solda" como texto de `<option>` — ver o comentário do mesmo problema no describe de leitura.
    await within(await screen.findByRole('list', { name: 'Roteiro' })).findByText('Solda')

    fireEvent.click(screen.getByRole('button', { name: /remover.*solda/i }))
    fireEvent.click(screen.getByRole('button', { name: /salvar roteiro/i }))

    // Sem `ordem` no corpo: quem numera é o servidor, pela posição.
    await vi.waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toEqual({
      caminho: '/api/componentes/7/roteiro-padrao',
      corpo: { linhas: [{ setorId: 20 }, { setorId: 20 }] },
    })
  })

  /**
   * Item 7 da review (Tasks 10-12) — DECISÃO DO USUÁRIO: numerar pela POSIÇÃO na tela, não por
   * `r.ordem`. Cenário exato da review: `[1.Corte, 2.Solda, 3.Corte]` → remover "2. Solda" →
   * `[1.Corte, 3.Corte]` (por `r.ordem` da leitura original) → adicionar Solda de novo → COM
   * `r.ordem` a tela mostraria `1. Corte`, `3. Corte`, `3. Solda` — número pulado, número
   * repetido, e dois botões "Remover 3. …" com o MESMO nome acessível. Numerando pela posição do
   * array, os três nomes ficam únicos. Mata se `i + 1` voltar a ser `r.ordem`.
   */
  it('a numeração do roteiro é pela posição na tela — sem número repetido nem pulado', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')
    const listaRoteiro = await screen.findByRole('list', { name: 'Roteiro' })
    await within(listaRoteiro).findByText('Solda')

    fireEvent.click(within(listaRoteiro).getByRole('button', { name: 'Remover 2. Solda' }))

    const secaoRoteiro = screen.getByRole('region', { name: /roteiro/i })
    fireEvent.change(within(secaoRoteiro).getByLabelText(/setor/i), { target: { value: '21' } })
    fireEvent.click(within(secaoRoteiro).getByRole('button', { name: 'Adicionar passo' }))

    // `getByRole` só passa se existir EXATAMENTE um elemento com aquele nome acessível — é a
    // prova de que os três não colidem.
    expect(within(listaRoteiro).getByRole('button', { name: 'Remover 1. Corte' })).toBeTruthy()
    expect(within(listaRoteiro).getByRole('button', { name: 'Remover 2. Corte' })).toBeTruthy()
    expect(within(listaRoteiro).getByRole('button', { name: 'Remover 3. Solda' })).toBeTruthy()
  })
})
