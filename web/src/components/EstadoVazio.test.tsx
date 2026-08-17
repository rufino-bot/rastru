// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { EstadoVazio } from './EstadoVazio'

afterEach(cleanup)

describe('EstadoVazio', () => {
  it('mostra título e descrição', () => {
    render(<EstadoVazio titulo="Nenhum componente encontrado" descricao="Tente outro termo de busca." />)

    expect(screen.getByText('Nenhum componente encontrado')).toBeTruthy()
    expect(screen.getByText('Tente outro termo de busca.')).toBeTruthy()
  })

  it('funciona só com título', () => {
    render(<EstadoVazio titulo="Catálogo vazio" />)

    expect(screen.getByText('Catálogo vazio')).toBeTruthy()
  })

  it('mostra a ação sugerida quando ela existe', () => {
    render(<EstadoVazio titulo="Catálogo vazio" acao={<button>Cadastrar o primeiro</button>} />)

    expect(screen.getByText('Cadastrar o primeiro')).toBeTruthy()
  })

  it('tem o papel status, não alert', () => {
    // P7: o nome anterior ("é anunciado como região de status") afirmava um comportamento de
    // leitor de tela que jsdom não pode provar — só a PRESENÇA do papel ARIA. `role="status"` e
    // não `role="alert"`: lista vazia é informação, não falha; anunciar como alerta interromperia
    // a leitura para dizer que não há nada, e o usuário que acabou de filtrar já sabe disso.
    // Desenho intocado (m3 da review da Task 6: a live region provavelmente não anuncia de fato,
    // porque região e conteúdo entram no DOM no mesmo tick — não há instrumento nesta bancada
    // (sem Playwright/vitest-browser) para provar isso; é varredura manual da Task 12).
    render(<EstadoVazio titulo="Nenhum resultado" />)

    expect(screen.getByRole('status').textContent).toContain('Nenhum resultado')
  })
})
