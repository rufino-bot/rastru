// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within, cleanup } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { ComponenteDetalhePage } from './ComponenteDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// A Task 11 acrescenta escrita nesta mesma tela, gated por perfil — nasce aqui com a forma certa
// (mock com variável mutável, molde de PedidoDetalhePage.test.tsx) para não precisar reescrever o
// arquivo depois. Não usado por nenhum teste desta task: a Task 10 é só leitura, e
// ComponenteDetalhePage.tsx (Task 10) não chama `usePodeEscrever`.
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

/** Todas as 4 chamadas que a tela faz, no caminho feliz. Chaves COM o prefixo `/api` (defeito 3). */
function apiCompleta() {
  return fetchPorRota({
    '/api/componentes/7/filhos-padrao': () => respostaJson(FILHOS),
    '/api/componentes/7/materiais-padrao': () => respostaJson(MATERIAIS),
    '/api/componentes/7/roteiro-padrao': () => respostaJson(ROTEIRO),
    '/api/componentes/7': () => respostaJson(COMPONENTE),
  })
}

// Molde de PedidoDetalhePage.test.tsx: a tela lê `:id` da rota, então precisa nascer DENTRO de
// uma rota casada — renderizar o componente solto deixaria `useParams()` vazio e `componenteId`
// viraria NaN mesmo quando o teste queria um id válido.
function renderizarNaRota(caminho: string) {
  return render(
    <MemoryRouter initialEntries={[caminho]}>
      <Routes>
        <Route path="/componentes/:id" element={<ComponenteDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
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
    expect(await screen.findByText('Solda')).toBeTruthy()
  })

  /**
   * O MESMO setor em duas posições do roteiro tem que aparecer DUAS vezes — é retorno ao setor,
   * não duplicata. Uma tela que "deduplica por id de setor" some com a segunda passagem.
   */
  it('mostra o mesmo setor duas vezes quando o roteiro tem retorno ao setor', async () => {
    vi.stubGlobal('fetch', apiCompleta())
    renderizarNaRota('/componentes/7')

    expect(await screen.findAllByText('Corte')).toHaveLength(2)
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
