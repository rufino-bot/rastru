// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { BannerDeErro } from './BannerDeErro'

afterEach(cleanup)

describe('BannerDeErro', () => {
  it('mostra a mensagem', () => {
    render(<BannerDeErro mensagem="Seu perfil não tem permissão para esta ação." />)

    expect(screen.getByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('não renderiza nada quando não há mensagem', () => {
    // O chamador passa o estado direto (`<BannerDeErro mensagem={erro} />`) em vez de repetir
    // `{erro && …}` em sete telas. Só funciona se o nulo for tratado aqui.
    const { container } = render(<BannerDeErro mensagem={null} />)

    expect(container.innerHTML).toBe('')
  })

  it('é anunciado como alerta', () => {
    // `role="alert"` faz o leitor de tela ler a mensagem assim que ela aparece, sem exigir que o
    // usuário navegue até ela. Numa tela de bancada, o erro costuma estar longe do foco.
    render(<BannerDeErro mensagem="Sem conexão com o servidor." />)

    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor.')
  })
})
