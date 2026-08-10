// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AppShell } from './AppShell'

afterEach(cleanup)

const logout = vi.fn()

// O `AuthProvider` de verdade dispara init-refresh no mount; aqui só interessa o que o shell faz
// com a sessão já resolvida.
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: {
      status: 'autenticado',
      usuario: { id: 2, nomeUsuario: 'pcp', nomeCompleto: 'Planejamento e Controle', perfil: 'PCP' },
    },
    login: async () => {},
    logout,
  }),
}))

function renderizarShell(rotaInicial = '/') {
  return render(
    <MemoryRouter initialEntries={[rotaInicial]}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<p>conteúdo da home</p>} />
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
    for (const d of ['/', '/pedidos', '/componentes', '/materiais', '/setores']) {
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
    // Sem isto, no celular a gaveta continua cobrindo a tela que o usuário acabou de abrir — e ele
    // acha que o clique não funcionou.
    renderizarShell()

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menu' }))
    const linkDaGaveta = screen.getAllByRole('link').filter((l) => l.getAttribute('href') === '/setores')
    fireEvent.click(linkDaGaveta[linkDaGaveta.length - 1])

    expect(screen.getByRole('button', { name: 'Abrir menu' })).toBeTruthy()
  })
})
