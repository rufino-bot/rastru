// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within, cleanup, fireEvent, act, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, Link } from 'react-router-dom'
import { AppShell } from './AppShell'
import { Pagina } from './Pagina'
import { useAuth } from '../auth/AuthContext'
import { contarTarefas } from '../api/execucao'
import { aoMudarOLivro } from '../api/sinalDoLivro'

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
vi.mock('../api/sinalDoLivro', () => ({
  aoMudarOLivro: vi.fn(() => () => {}),
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

/** Abre um grupo da barra pelo botão dele (as opções só existem no DOM com o grupo aberto). */
function abrirGrupoDaBarra(nome: string) {
  const barra = screen.getByRole('navigation', { name: 'Principal' })
  fireEvent.click(within(barra).getByRole('button', { name: new RegExp(`^${nome}`) }))
}

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
    // Na gaveta os grupos abrem juntos; é o jeito de ter todas as opções na tela ao mesmo tempo.
    renderizarShell()
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const menu = screen.getByRole('navigation', { name: 'Menu' })
    for (const grupo of ['Produção', 'Expedição', 'Cadastros']) {
      fireEvent.click(within(menu).getByRole('button', { name: new RegExp(`^${grupo}`) }))
    }

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    for (const d of ['/', '/fila', '/tarefas', '/separacao', '/expedicao', '/liberacoes', '/pedidos', '/componentes', '/materiais', '/setores']) {
      expect(destinos, `link para ${d}`).toContain(d)
    }
  })

  it('o menu não tem "Início", na barra nem na gaveta; a marca "Rastru — Início" é o link para "/"', () => {
    renderizarShell('/setores')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    for (const nome of ['Principal', 'Menu']) {
      const menu = screen.getByRole('navigation', { name: nome })
      expect(within(menu).queryByRole('link', { name: 'Início' }), nome).toBeNull()
      expect(within(menu).getAllByRole('link').map((l) => l.getAttribute('href')), nome).not.toContain('/')
    }

    const marca = screen.getByRole('link', { name: 'Rastru — Início' })
    expect(marca.getAttribute('href')).toBe('/')
    // O texto visível continua "Rastru"; o nome acessível diz para onde o link leva.
    expect(marca.textContent).toBe('Rastru')
    // Anel de foco visível sobre o chrome escuro, como os links do menu: `marca`, não `acao`.
    expect(marca.className.split(/\s+/)).toContain('focus-visible:outline-marca')

    fireEvent.click(marca)
    expect(screen.getByText('conteúdo da home')).toBeTruthy()
    // Navegar pela marca fecha a gaveta, como os outros links.
    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })

  it('o grupo Produção tem Separação para todo perfil, logo depois de Tarefas', () => {
    // A leitura da lista de separação é livre; quem não separa vê a lista sem o botão (gating na ação).
    for (const perfil of ['Operador', 'Movimentador', 'Separador', 'PCP', 'Qualidade', 'Gestao', 'Administrador']) {
      vi.mocked(useAuth).mockReturnValue({
        estado: { status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
        login: async () => {},
        logout,
      })
      renderizarShell()
      abrirGrupoDaBarra('Produção')

      const barra = screen.getByRole('navigation', { name: 'Principal' })
      const separacao = within(barra).getByRole('link', { name: 'Separação' })
      expect(separacao.getAttribute('href'), perfil).toBe('/separacao')
      // Logo depois de Tarefas: as telas do chão de fábrica ficam juntas.
      const rotulos = within(barra).getAllByRole('link').map((l) => l.textContent)
      expect(rotulos.indexOf('Separação'), perfil).toBe(rotulos.findIndex((r) => r?.startsWith('Tarefas')) + 1)
      cleanup()
    }
  })

  it('o grupo Expedição tem Expedição e Liberações para todo perfil, nesta ordem', () => {
    // A leitura das duas telas é livre; quem não registra nem responde vê a lista sem o botão (gating na ação).
    for (const perfil of ['Operador', 'Movimentador', 'Separador', 'PCP', 'Qualidade', 'Gestao', 'Administrador']) {
      vi.mocked(useAuth).mockReturnValue({
        estado: { status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
        login: async () => {},
        logout,
      })
      renderizarShell()
      abrirGrupoDaBarra('Expedição')

      const barra = screen.getByRole('navigation', { name: 'Principal' })
      expect(within(barra).getByRole('link', { name: 'Expedição' }).getAttribute('href'), perfil).toBe('/expedicao')
      expect(within(barra).getByRole('link', { name: 'Liberações' }).getAttribute('href'), perfil).toBe('/liberacoes')
      const rotulos = within(barra).getAllByRole('link').map((l) => l.textContent)
      expect(rotulos.indexOf('Liberações'), perfil).toBe(rotulos.indexOf('Expedição') + 1)
      cleanup()
    }
  })

  it('mostra os links mesmo para perfil que não pode escrever neles', () => {
    // Decisão do usuário em 2026-08-06: gating vai na AÇÃO, não no link. O PCP não escreve em
    // Setores nem Materiais, mas LÊ os dois — e a Fase 4 depende disso para o Separador.
    renderizarShell()
    abrirGrupoDaBarra('Cadastros')

    const destinos = screen.getAllByRole('link').map((l) => l.getAttribute('href'))
    expect(destinos).toContain('/setores')
    expect(destinos).toContain('/materiais')
  })

  it('marca o item da tela atual', () => {
    renderizarShell('/setores')
    abrirGrupoDaBarra('Cadastros')

    const atual = screen.getAllByRole('link').find((l) => l.getAttribute('aria-current') === 'page')
    expect(atual?.getAttribute('href')).toBe('/setores')
  })

  it('distingue o item atual por fundo, tinta E peso', () => {
    // O `aria-current` sozinho não é a distinção VISUAL, e era exatamente ela que nenhuma mutação
    // tocava (D5 do pré-flight): trocar as classes de ativo e inativo entre si deixava a suíte
    // verde. Asserção token a token, não `toContain` de string — a lição da Task 5, onde
    // `toContain('bg-acao')` casava com `hover:bg-acao-fundo`.
    renderizarShell('/setores')
    abrirGrupoDaBarra('Cadastros')

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
    fireEvent.click(within(screen.getByRole('navigation', { name: 'Menu' })).getByRole('button', { name: 'Cadastros' }))
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
    abrirGrupoDaBarra('Produção')

    expect(await screen.findByRole('link', { name: 'Tarefas 3' })).toBeTruthy()
  })

  it('sem tarefa, o item Tarefas não mostra "0"', async () => {
    renderizarShell()
    abrirGrupoDaBarra('Produção')

    await waitFor(() => expect(contarTarefas).toHaveBeenCalled())
    expect(screen.getByRole('link', { name: 'Tarefas' })).toBeTruthy()
  })

  it('falha ao contar não vira banner no shell', async () => {
    vi.mocked(contarTarefas).mockRejectedValue(new TypeError('Failed to fetch'))

    renderizarShell()
    abrirGrupoDaBarra('Produção')

    await waitFor(() => expect(contarTarefas).toHaveBeenCalled())
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('link', { name: 'Tarefas' })).toBeTruthy()
  })

  it('o contador se atualiza a cada 30 s', async () => {
    vi.useFakeTimers()
    vi.mocked(contarTarefas).mockResolvedValueOnce(1).mockResolvedValueOnce(4)

    renderizarShell()
    abrirGrupoDaBarra('Produção')
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('link', { name: 'Tarefas 1' })).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByRole('link', { name: 'Tarefas 4' })).toBeTruthy()
  })

  it('o contador reconta na hora quando o livro muda, sem esperar os 30 s', async () => {
    // Achado na verificação manual da Fase 3: a tela de Tarefas já dizia "Nenhum item pronto" e o
    // menu ainda mostrava 1, até o próximo ciclo. Relógio falso e parado: só o aviso pode recontar.
    vi.useFakeTimers()
    vi.mocked(contarTarefas).mockResolvedValueOnce(1).mockResolvedValueOnce(0)

    renderizarShell()
    abrirGrupoDaBarra('Produção')
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByRole('link', { name: 'Tarefas 1' })).toBeTruthy()

    const avisar = vi.mocked(aoMudarOLivro).mock.lastCall![0]
    await act(async () => { avisar(); await vi.advanceTimersByTimeAsync(0) })

    expect(screen.getByRole('link', { name: 'Tarefas' })).toBeTruthy()
    expect(contarTarefas).toHaveBeenCalledTimes(2)
  })

  it('para de escutar o livro quando o shell sai da tela', () => {
    const parar = vi.fn()
    vi.mocked(aoMudarOLivro).mockReturnValueOnce(parar)

    const { unmount } = renderizarShell()
    unmount()

    expect(parar).toHaveBeenCalledTimes(1)
  })

  it('falha numa atualização periódica mantém o último número (D5)', async () => {
    // Achado da review da Task 7: falha na atualização (depois de um sucesso) não zera o
    // contador — quem faz isso é `useCargaPeriodica`, que só troca `dados` por `null` numa `chave`
    // nova, nunca no `catch`. Distingue do caso já coberto por "falha ao contar não vira banner no
    // shell", que só cobre a falha da carga INICIAL, onde não há número anterior a manter.
    vi.useFakeTimers()
    vi.mocked(contarTarefas).mockResolvedValueOnce(3).mockRejectedValueOnce(new TypeError('Failed to fetch'))

    renderizarShell()
    abrirGrupoDaBarra('Produção')
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

describe('AppShell — menu em grupos', () => {
  const barra = () => screen.getByRole('navigation', { name: 'Principal' })
  const gaveta = () => screen.getByRole('navigation', { name: 'Menu' })
  const botaoDoGrupo = (nav: HTMLElement, nome: string | RegExp) => within(nav).getByRole('button', { name: nome })
  const nomesDosLinks = (nav: HTMLElement) => within(nav).queryAllByRole('link').map((l) => l.textContent)

  it('a barra tem quatro entradas, nesta ordem: Produção, Expedição, Pedidos e Cadastros', () => {
    renderizarShell()

    const entradas = [...barra().querySelectorAll('button, a')].map((e) => e.textContent)
    expect(entradas).toEqual(['Produção▾', 'Expedição▾', 'Pedidos', 'Cadastros▾'])
    // Pedidos é link direto; os outros três são grupos.
    expect(within(barra()).getByRole('link', { name: 'Pedidos' }).getAttribute('href')).toBe('/pedidos')
  })

  it('o grupo nasce fechado: aria-expanded falso e nenhuma opção na tela', () => {
    renderizarShell()

    for (const nome of ['Produção', 'Expedição', 'Cadastros']) {
      expect(botaoDoGrupo(barra(), nome).getAttribute('aria-expanded'), nome).toBe('false')
    }
    expect(nomesDosLinks(barra())).toEqual(['Pedidos'])
  })

  it('abre e fecha o grupo por clique, e as opções vêm na ordem', () => {
    renderizarShell()

    const producao = botaoDoGrupo(barra(), 'Produção')
    fireEvent.click(producao)

    expect(producao.getAttribute('aria-expanded')).toBe('true')
    const lista = document.getElementById(producao.getAttribute('aria-controls')!)!
    expect(within(lista).getAllByRole('link').map((l) => l.textContent)).toEqual(['Fila', 'Tarefas', 'Separação'])

    fireEvent.click(producao)
    expect(producao.getAttribute('aria-expanded')).toBe('false')
    expect(within(barra()).queryByRole('link', { name: 'Fila' })).toBeNull()
  })

  it('Expedição e Cadastros trazem as opções certas, na ordem', () => {
    renderizarShell()

    fireEvent.click(botaoDoGrupo(barra(), 'Expedição'))
    expect(nomesDosLinks(barra())).toEqual(['Expedição', 'Liberações', 'Pedidos'])
    expect(within(barra()).getByRole('link', { name: 'Liberações' }).getAttribute('href')).toBe('/liberacoes')

    fireEvent.click(botaoDoGrupo(barra(), 'Cadastros'))
    expect(nomesDosLinks(barra())).toEqual(['Pedidos', 'Componentes', 'Materiais', 'Setores'])
  })

  it('na barra, um grupo aberto por vez', () => {
    renderizarShell()

    fireEvent.click(botaoDoGrupo(barra(), 'Produção'))
    fireEvent.click(botaoDoGrupo(barra(), 'Cadastros'))

    expect(botaoDoGrupo(barra(), 'Produção').getAttribute('aria-expanded')).toBe('false')
    expect(botaoDoGrupo(barra(), 'Cadastros').getAttribute('aria-expanded')).toBe('true')
    expect(within(barra()).queryByRole('link', { name: 'Fila' })).toBeNull()
  })

  it('Esc fecha o grupo e devolve o foco ao botão dele', () => {
    renderizarShell()

    const producao = botaoDoGrupo(barra(), 'Produção')
    fireEvent.click(producao)
    const fila = within(barra()).getByRole('link', { name: 'Fila' })
    fila.focus()
    expect(document.activeElement).toBe(fila)

    fireEvent.keyDown(fila, { key: 'Escape' })

    expect(producao.getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(producao)
  })

  it('clicar fora da barra fecha o grupo', () => {
    renderizarShell()

    const producao = botaoDoGrupo(barra(), 'Produção')
    fireEvent.click(producao)
    fireEvent.pointerDown(screen.getByText('conteúdo da home'))

    expect(producao.getAttribute('aria-expanded')).toBe('false')
  })

  it('clicar no próprio botão do grupo aberto o fecha, sem fechar e reabrir', () => {
    renderizarShell()

    const producao = botaoDoGrupo(barra(), 'Produção')
    fireEvent.click(producao)

    // O clique real é pointerdown e depois click: o "fora" não pode contar o botão como fora.
    fireEvent.pointerDown(producao)
    fireEvent.click(producao)

    expect(producao.getAttribute('aria-expanded')).toBe('false')
  })

  it('fecha o grupo ao navegar por uma das opções', () => {
    renderizarShell()

    fireEvent.click(botaoDoGrupo(barra(), 'Cadastros'))
    fireEvent.click(within(barra()).getByRole('link', { name: 'Setores' }))

    expect(screen.getByText('conteúdo de setores')).toBeTruthy()
    expect(botaoDoGrupo(barra(), 'Cadastros').getAttribute('aria-expanded')).toBe('false')
  })

  it('o botão do grupo da rota atual tem o destaque do link ativo; os outros, não', () => {
    renderizarShell('/setores')

    const atual = botaoDoGrupo(barra(), 'Cadastros').className.split(/\s+/)
    expect(atual).toContain('bg-chrome-ativo')
    expect(atual).toContain('text-superficie')
    expect(atual).toContain('font-semibold')

    const outro = botaoDoGrupo(barra(), 'Produção').className.split(/\s+/)
    expect(outro).not.toContain('bg-chrome-ativo')
    expect(outro).not.toContain('font-semibold')
    expect(outro).toContain('text-chrome-tinta-fraca')
  })

  it('o botão do grupo tem o anel de foco "marca", como os links do menu', () => {
    renderizarShell()

    expect(botaoDoGrupo(barra(), 'Produção').className.split(/\s+/)).toContain('focus-visible:outline-marca')
  })

  it('na gaveta, o grupo da rota atual vem aberto e os outros fechados', () => {
    renderizarShell('/setores')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    expect(botaoDoGrupo(gaveta(), 'Cadastros').getAttribute('aria-expanded')).toBe('true')
    expect(botaoDoGrupo(gaveta(), 'Produção').getAttribute('aria-expanded')).toBe('false')
    expect(botaoDoGrupo(gaveta(), 'Expedição').getAttribute('aria-expanded')).toBe('false')
    expect(nomesDosLinks(gaveta())).toEqual(['Pedidos', 'Componentes', 'Materiais', 'Setores'])
  })

  it('na gaveta, sem rota de grupo (Home), todos os grupos vêm fechados', () => {
    renderizarShell('/')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    expect(nomesDosLinks(gaveta())).toEqual(['Pedidos'])
  })

  it('na gaveta, mais de um grupo pode ficar aberto ao mesmo tempo', () => {
    renderizarShell('/setores')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    fireEvent.click(botaoDoGrupo(gaveta(), 'Produção'))

    expect(botaoDoGrupo(gaveta(), 'Produção').getAttribute('aria-expanded')).toBe('true')
    expect(botaoDoGrupo(gaveta(), 'Cadastros').getAttribute('aria-expanded')).toBe('true')
    expect(nomesDosLinks(gaveta())).toContain('Fila')
    expect(nomesDosLinks(gaveta())).toContain('Setores')
  })

  it('reabrir a gaveta depois de navegar abre o grupo da rota nova', () => {
    renderizarShell('/pedidos')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    fireEvent.click(screen.getByText('link do conteúdo'))

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    expect(botaoDoGrupo(gaveta(), 'Cadastros').getAttribute('aria-expanded')).toBe('true')
  })

  it('o contador aparece no botão Produção com o grupo fechado, e só na opção Tarefas com ele aberto', async () => {
    vi.mocked(contarTarefas).mockResolvedValue(3)
    renderizarShell()

    const fechado = await within(barra()).findByRole('button', { name: 'Produção 3' })
    expect(fechado.getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(fechado)

    const aberto = botaoDoGrupo(barra(), 'Produção')
    expect(aberto.getAttribute('aria-expanded')).toBe('true')
    expect(within(aberto).queryByText('3')).toBeNull()
    expect(within(barra()).getByRole('link', { name: 'Tarefas 3' })).toBeTruthy()
  })

  it('sem tarefa, o botão Produção não mostra "0"', async () => {
    renderizarShell()

    await waitFor(() => expect(contarTarefas).toHaveBeenCalled())
    expect(botaoDoGrupo(barra(), 'Produção').textContent).not.toContain('0')
  })

  it('na gaveta o contador segue a mesma regra do grupo fechado e aberto', async () => {
    vi.mocked(contarTarefas).mockResolvedValue(3)
    renderizarShell('/')
    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))

    const fechado = await within(gaveta()).findByRole('button', { name: 'Produção 3' })
    fireEvent.click(fechado)

    expect(botaoDoGrupo(gaveta(), 'Produção').getAttribute('aria-expanded')).toBe('true')
    expect(within(gaveta()).getByRole('link', { name: 'Tarefas 3' })).toBeTruthy()
    expect(within(botaoDoGrupo(gaveta(), 'Produção')).queryByText('3')).toBeNull()
  })

  it('rótulo e contador ficam na mesma linha: o botão do grupo e o link não quebram', async () => {
    vi.mocked(contarTarefas).mockResolvedValue(3)
    renderizarShell()

    const botao = await within(barra()).findByRole('button', { name: 'Produção 3' })
    expect(botao.className.split(/\s+/)).toContain('whitespace-nowrap')
    fireEvent.click(botao)
    expect(within(barra()).getByRole('link', { name: 'Tarefas 3' }).className.split(/\s+/)).toContain('whitespace-nowrap')
  })
})
