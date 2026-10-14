// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { Link, MemoryRouter } from 'react-router-dom'
import { Pagina } from './Pagina'

afterEach(cleanup)

describe('Pagina', () => {
  it('mostra o título como o h1 da tela', () => {
    render(<Pagina titulo="Setores"><p>conteúdo</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Setores')
    expect(screen.getByText('conteúdo')).toBeTruthy()
  })

  it('aceita um título com link dentro do h1', () => {
    render(
      <MemoryRouter>
        <Pagina titulo={<><Link to="/pedidos/4">Pedido P-1</Link> — AG-01</>}>conteúdo</Pagina>
      </MemoryRouter>,
    )

    const h1 = screen.getByRole('heading', { level: 1 })
    expect(within(h1).getByRole('link', { name: 'Pedido P-1' }).getAttribute('href')).toBe('/pedidos/4')
    expect(h1.textContent).toBe('Pedido P-1 — AG-01')
  })

  it('mostra a ação de cabeçalho quando ela existe', () => {
    render(<Pagina titulo="Pedidos" acao={<button>Novo</button>}><p>c</p></Pagina>)

    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('funciona sem ação', () => {
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 })).toBeTruthy()
  })

  it('não é landmark main por conta própria — quem é é o AppShell', () => {
    // A partir da Task 7 toda tela interna renderiza dentro do AppShell, que já é o `main`. Se a
    // `Pagina` também fosse `main`, a Task 8 aninharia dois — não conforme no HTML, e o leitor de
    // tela perderia o atalho "ir para o conteúdo". A prova de que o `main` único existe é do
    // `AppShell.test.tsx`, não daqui.
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.queryByRole('main')).toBeNull()
  })

  it('não impõe altura mínima de tela — quem faz isso é o shell', () => {
    // `min-h-screen` DENTRO da página, com o shell também aplicando, produz barra de rolagem
    // permanente de alguns pixels. As 6 telas de hoje têm `min-h-screen`; ele sai daqui.
    //
    // A guarda de vazamento de `cleanup` era `expect(screen.queryByRole('main')).toBeNull()` —
    // deixou de fazer sentido porque `Pagina` não tem mais `main` nenhum (P1). Equivalente: o body
    // não pode ter sobra de um `render` anterior antes deste rodar.
    expect(document.body.innerHTML).toBe('')
    const { container } = render(<Pagina titulo="Setores"><p>c</p></Pagina>)
    expect(container.firstElementChild!.className).not.toContain('min-h-screen')
  })

  it('usa a largura larga — a decisão que dá razão de existir ao componente', () => {
    // I1 da review da Task 5: `max-w-3xl` é a única razão de o `Pagina` substituir as seis cópias
    // de `max-w-md` (448px) — a spec §7 registra que busca + filtro + seletor de tamanho +
    // paginação não cabem em 448px. Sem este teste, trocar `max-w-3xl` por `max-w-md` devolvia o
    // componente ao valor que ele foi criado para substituir e a suíte não notava. Token a token,
    // não `toContain` sobre a string inteira.
    const { container } = render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    const classes = container.firstElementChild!.className.split(/\s+/)

    expect(classes).toContain('max-w-3xl')
    expect(classes).not.toContain('max-w-md')
  })

  it('a largura ampla é opção de quem precisa de duas colunas, e não muda a padrão', () => {
    const { container, rerender } = render(<Pagina titulo="Conferência" largura="ampla"><p>c</p></Pagina>)

    const ampla = container.firstElementChild!.className.split(/\s+/)
    expect(ampla).toContain('max-w-7xl')
    expect(ampla).not.toContain('max-w-3xl')

    rerender(<Pagina titulo="Conferência" largura="padrao"><p>c</p></Pagina>)
    expect(container.firstElementChild!.className.split(/\s+/)).toContain('max-w-3xl')
  })
})
