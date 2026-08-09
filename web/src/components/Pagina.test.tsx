// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pagina } from './Pagina'

afterEach(cleanup)

describe('Pagina', () => {
  it('mostra o título como o h1 da tela', () => {
    render(<Pagina titulo="Setores"><p>conteúdo</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Setores')
    expect(screen.getByText('conteúdo')).toBeTruthy()
  })

  it('mostra a ação de cabeçalho quando ela existe', () => {
    render(<Pagina titulo="Pedidos" acao={<button>Novo</button>}><p>c</p></Pagina>)

    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('funciona sem ação', () => {
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('heading', { level: 1 })).toBeTruthy()
  })

  it('é o landmark main da página', () => {
    // Um `main` por tela dá ao leitor de tela o atalho "ir para o conteúdo" e separa o conteúdo do
    // shell de navegação, que envolve todas as telas a partir da Task 7.
    render(<Pagina titulo="Setores"><p>c</p></Pagina>)

    expect(screen.getByRole('main')).toBeTruthy()
  })

  it('não impõe altura mínima de tela — quem faz isso é o shell', () => {
    // `min-h-screen` DENTRO da página, com o shell também aplicando, produz barra de rolagem
    // permanente de alguns pixels. As 6 telas de hoje têm `min-h-screen`; ele sai daqui.
    expect(screen.queryByRole('main')).toBeNull()
    const { container } = render(<Pagina titulo="Setores"><p>c</p></Pagina>)
    expect(container.querySelector('main')!.className).not.toContain('min-h-screen')
  })
})
