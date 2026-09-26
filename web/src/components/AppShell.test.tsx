// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within, cleanup, fireEvent, act, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, Link } from 'react-router-dom'
import { AppShell } from './AppShell'
import { Pagina } from './Pagina'
import { useAuth } from '../auth/AuthContext'
import { contarTarefas } from '../api/execucao'

const logout = vi.fn()

afterEach(() => { cleanup(); logout.mockClear(); vi.useRealTimers() })

// O `AuthProvider` de verdade dispara init-refresh no mount; aqui só interessa o que o shell faz
// com a sessão já resolvida. `useAuth` é um mock por si (`vi.fn`), não uma factory fixa, porque o
// caso "não mostra identidade em sessão anônima" precisa trocar o retorno por teste.
vi.mock('../auth/AuthContext', () => ({
  useAuth: vi.fn(),
}))

// O contador de Tarefas (Fase 3) consulta a API no mount de TODO shell; sem este mock, cada caso
// daqui dispararia uma requisição de verdade.
vi.mock('../api/execucao', () => ({
  contarTarefas: vi.fn(),
}))

beforeEach(() => {
  vi.mocked(contarTarefas).mockReset()
  vi.mocked(contarTarefas).mockResolvedValue(0)
  vi.mocked(useAuth).mockReturnValue({
    estado: {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    },
    login: async () => {},
    logout,
  })
})

function renderizarShell(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<p>conteúdo da home</p>} />
          <Route
            path="/pedidos"
            element={
              <>
                <p>conteúdo de pedidos</p>
                <Link to="/setores">link do conteúdo</Link>
              </>
            }
          />
          <Route path="/setores" element={<p>conteúdo de setores</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AppShell', () => {
  it('renderiza a tela filha', () => {
    // Sem o `<Outlet/>` o shell aparece e o conteúdo some — e o build passa.
    renderizarShell()

    expect(screen.getByText('conteúdo da home')).toBeTruthy()
  })

  it('dá caminho de volta a partir de qualquer tela interna', () => {
    // Hoje as seis telas internas só voltam pelo botão do navegador. Este é o defeito de navegação
    // que o shell existe para fechar.
    renderizarShell('/setores')

    expect(screen.getByRole('navigation', { name: 'Principal' })).toBeTruthy()
    expect(screen.getAllByRole('link').some((l) => l.getAttribute('href') === '/')).toBe(true)
  })

  it('leva a todas as áreas do sistema', () => {
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    for (const d of ['/', '/fila', '/tarefas', '/pedidos', '/componentes', '/materiais', '/setores']) {
      expect(destinos, `link para ${d}`).toContain(d)
    }
  })

  it('mostra os links mesmo para perfil que não pode escrever neles', () => {
    // Decisão do usuário em 2026-08-06: gating vai na AÇÃO, não no link. O PCP não escreve em
    // Setores nem Materiais, mas LÊ os dois — e a Fase 4 depende disso para o Almoxarifado.
    renderizarShell()

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toContain('/setores')
    expect(destinos).toContain('/materiais')
  })

  it('marca o item da tela atual', () => {
    renderizarShell('/setores')

    const atual = screen.getAllByRole('link').find((l) => l.getAttribute('aria-current') === 'page')
    expect(atual?.getAttribute('href')).toBe('/setores')
  })

  it('distingue o item atual por fundo, tinta E peso', () => {
    // O `aria-current` sozinho não é a distinção VISUAL, e era exatamente ela que nenhuma mutação
    // tocava (D5 do pré-flight): trocar as classes de ativo e inativo entre si deixava a suíte
    // verde. Asserção token a token, não `toContain` de string — a lição da Task 5, onde
    // `toContain('bg-acao')` casava com `hover:bg-acao-fundo`.
    renderizarShell('/setores')

    const links = screen.getAllByRole('link')
    const atual = links.find((l) => l.getAttribute('aria-current') === 'page')!
    const outro = links.find((l) => l.getAttribute('href') === '/pedidos')!

    const classesDoAtual = atual.className.split(/\s+/)
    const classesDoOutro = outro.className.split(/\s+/)

    expect(classesDoAtual).toContain('bg-chrome-ativo')
    expect(classesDoAtual).toContain('text-superficie')
    expect(classesDoAtual).toContain('font-semibold')

    expect(classesDoOutro).not.toContain('bg-chrome-ativo')
    expect(classesDoOutro).not.toContain('font-semibold')
    expect(classesDoOutro).toContain('text-chrome-tinta-fraca')
  })

  it('mostra quem está logado e o perfil', () => {
    renderizarShell()

    expect(screen.getByText('Planejamento e Controle')).toBeTruthy()
    expect(screen.getByText('PCP')).toBeTruthy()
  })

  it('sai da sessão pelo botão Sair', () => {
    renderizarShell()

    fireEvent.click(screen.getByText('Sair'))

    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('abre e fecha a gaveta pelo botão de menu', () => {
    renderizarShell()

    const botao = screen.getByRole('button', { name: 'Abrir menu' })
    expect(botao.getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(botao)
    expect(screen.getByRole('button', { name: 'Fechar menu' }).getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Fechar menu' }))
    expect(screen.getByRole('button', { name: 'Abrir menu' }).getAttribute('aria-expanded')).toBe('false')
  })

  it('fecha a gaveta ao navegar', () => {
    // Sem isto, no celular a gaveta continua aberta e empurra o conteúdo para baixo depois da
    // navegação — o `<nav>` da gaveta está em fluxo normal (sem `absolute`/`fixed`), não sobre a
    // tela — e o usuário acha que o clique não funcionou.
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const linkDaGaveta = screen.getAllByRole('link').filter((l) => l.getAttribute('href') === '/setores')
    fireEvent.click(linkDaGaveta[linkDaGaveta.length - 1])

    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })

  it('fecha a gaveta ao navegar por link de dentro da tela', () => {
    // I3: o fechamento precisa reagir à NAVEGAÇÃO, não só ao clique nos links da própria gaveta —
    // um link no conteúdo (a HomePage tem quatro) tem de fechar a gaveta também.
    renderizarShell('/pedidos')

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    fireEvent.click(screen.getByText('link do conteúdo'))

    expect(screen.getByText('conteúdo de setores')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })

  it('fecha a gaveta ao navegar para a tela em que já está', () => {
    // O caso que decide a dependência do efeito: clicar, na gaveta, no link da MESMA tela em que o
    // usuário já está. Com `[location.pathname]` isto não dispararia o efeito (o pathname não
    // muda) e a gaveta ficaria aberta.
    renderizarShell('/setores')

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const linkDaGaveta = screen.getAllByRole('link').filter((l) => l.getAttribute('href') === '/setores')
    fireEvent.click(linkDaGaveta[linkDaGaveta.length - 1])

    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })

  it('sai da sessão pelo "Sair" da gaveta', () => {
    // Abaixo de 1024px a barra some (`hidden lg:flex`); o "Sair" da gaveta é o único logout que o
    // celular alcança.
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const gaveta = screen.getByRole('navigation', { name: 'Menu' })
    fireEvent.click(within(gaveta).getByText('Sair'))

    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('mostra quem está logado no pé da gaveta', () => {
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const gaveta = screen.getByRole('navigation', { name: 'Menu' })

    expect(within(gaveta).getByText('Planejamento e Controle')).toBeTruthy()
    expect(within(gaveta).getByText('PCP')).toBeTruthy()
  })

  it('a gaveta tem nome acessível', () => {
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    expect(screen.getByRole('navigation', { name: 'Menu' })).toBeTruthy()
  })

  it('não mostra identidade em sessão anônima', () => {
    // Ramo inalcançável em produção hoje (atrás do `ProtectedRoute`), coberto do mesmo jeito que o
    // ramo idêntico do hook (`usePermissao`) já é — a assimetria era o defeito.
    vi.mocked(useAuth).mockReturnValue({
      estado: { status: 'anonimo' },
      login: async () => {},
      logout,
    })

    renderizarShell()

    expect(screen.queryByText('Planejamento e Controle')).toBeNull()
  })

  it('o conteúdo fica dentro de um main', () => {
    renderizarShell()

    expect(screen.getByRole('main')).toBeTruthy()
  })

  it('uma tela feita de Pagina não aninha um segundo main dentro do shell', () => {
    // P1: o landmark é só do AppShell — Pagina virou `<div>` (Task "passe curto nas
    // primitivas"). Renderiza um MemoryRouter próprio (não usa `renderizarShell`, que 12 outros
    // casos dependem) porque o caso precisa de uma rota cujo elemento é `<Pagina>` de verdade, não
    // o `<p>` de conteúdo dos outros casos.
    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<AppShell />}>
            <Route path="/" element={<Pagina titulo="Setores"><p>conteúdo</p></Pagina>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getAllByRole('main')).toHaveLength(1)
  })

  it('o nome completo carrega title', () => {
    renderizarShell()

    expect(screen.getByText('Planejamento e Controle').getAttribute('title')).toBe(
      'Planejamento e Controle',
    )
  })

  it('mostra no item Tarefas quantas há', async () => {
    vi.mocked(contarTarefas).mockResolvedValue(3)

    renderizarShell()

    expect(await screen.findByRole('link', { name: 'Tarefas 3' })).toBeTruthy()
  })

  it('sem tarefa, o item Tarefas não mostra "0"', async () => {
    renderizarShell()

    await waitFor(() => expect(contarTarefas).toHaveBeenCalled())
    expect(screen.getByRole('link', { name: 'Tarefas' })).toBeTruthy()
  })

  it('falha ao contar não vira banner no shell', async () => {
    vi.mocked(contarTarefas).mockRejectedValue(new TypeError('Failed to fetch'))

    renderizarShell()

    await waitFor(() => expect(contarTarefas).toHaveBeenCalled())
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('link', { name: 'Tarefas' })).toBeTruthy()
  })

  it('o contador se atualiza a cada 30 s', async () => {
    vi.useFakeTimers()
    vi.mocked(contarTarefas).mockResolvedValueOnce(1).mockResolvedValueOnce(4)

    renderizarShell()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('link', { name: 'Tarefas 1' })).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByRole('link', { name: 'Tarefas 4' })).toBeTruthy()
  })

  it('falha numa atualização periódica mantém o último número (D5)', async () => {
    // Achado da review da Task 7: falha na atualização (depois de um sucesso) não zera o
    // contador — quem faz isso é `useCargaPeriodica`, que só troca `dados` por `null` numa `chave`
    // nova, nunca no `catch`. Distingue do caso já coberto por "falha ao contar não vira banner no
    // shell", que só cobre a falha da carga INICIAL, onde não há número anterior a manter.
    vi.useFakeTimers()
    vi.mocked(contarTarefas).mockResolvedValueOnce(3).mockRejectedValueOnce(new TypeError('Failed to fetch'))

    renderizarShell()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('link', { name: 'Tarefas 3' })).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByRole('link', { name: 'Tarefas 3' })).toBeTruthy()
  })

  it('a barra dá lugar à gaveta abaixo de 1024px, não de 768px', () => {
    // Desvio D4 do plano 3 da Fase 3: com Fila e Tarefas o cabeçalho pede 888px (MEDIDO em Chromium
    // contra o CSS do build). O jsdom não calcula layout, então este teste prende a DECLARAÇÃO —
    // as classes —, e a medida em si está no comentário de `ITENS`.
    renderizarShell()

    // Nenhuma classe cujo prefixo de variante seja "md" — escrita como PADRÃO, não como literal:
    // Tailwind 4 varre QUALQUER arquivo (este `.test.tsx` incluído) atrás de um nome de classe
    // completo, e escrever aqui a versão "md" dos dois pares "lg" acima geraria CSS morto no build
    // (achado do Lote B da review de branch da Fase 3, medido em `web/dist`).
    const semVarianteMd = (classes: string[]) => !classes.some((c) => /^md\W/.test(c))

    const barra = screen.getByRole('navigation', { name: 'Principal' }).className.split(/\s+/)
    expect(barra).toContain('lg:flex')
    expect(semVarianteMd(barra)).toBe(true)
    const botaoDoMenu = screen.getByRole('button', { name: 'Abrir menu' }).className.split(/\s+/)
    expect(botaoDoMenu).toContain('lg:hidden')
    expect(semVarianteMd(botaoDoMenu)).toBe(true)
  })
})
