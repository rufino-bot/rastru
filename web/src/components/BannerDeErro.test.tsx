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

  it('usa os tons declarados na guarda de contraste, sem modificador de opacidade', () => {
    // C1 da review da Task 5: `bg-negativo/5` e `text-negativo` reprovavam AA porque o tom
    // derivado por `color-mix` escapa das duas guardas de `contraste.test.ts` — nenhuma opera
    // sobre `color-mix`, só sobre declarações `--color-*`. A correção troca a tinta por tokens
    // declarados (`negativo-fundo`, `negativo-texto`), que agora vivem sob a guarda. Token a
    // token (`split`), nunca `toContain` sobre a string inteira: a review mediu que trocar a
    // `className` inteira por `"rounded-lg px-4 py-3"` deixava a suíte verde.
    const { container } = render(<BannerDeErro mensagem="Falha ao salvar." />)

    const classes = container.querySelector('p')!.className.split(/\s+/)

    expect(classes).toContain('border-negativo')
    expect(classes).toContain('bg-negativo-fundo')
    expect(classes).toContain('text-negativo-texto')
    expect(classes.some((c) => c.includes('/'))).toBe(false)
  })
})
