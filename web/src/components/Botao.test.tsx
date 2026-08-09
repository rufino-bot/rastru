// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { Botao } from './Botao'

afterEach(cleanup)

describe('Botao', () => {
  it('renderiza o rótulo e dispara o clique', () => {
    const aoClicar = vi.fn()
    render(<Botao onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByText('Adicionar'))

    expect(aoClicar).toHaveBeenCalledTimes(1)
  })

  it('desabilita e troca o rótulo enquanto a mutação está em voo', () => {
    // A dívida "botão desabilitado durante mutação" (spec §9). Sem isto, o PCP clica "Adicionar"
    // duas vezes no wifi lento e o segundo POST volta 409 — a tela acusando de duplicado o
    // cadastro que ela mesma acabou de fazer.
    render(<Botao carregando rotuloCarregando="Salvando…">Adicionar</Botao>)

    const botao = screen.getByRole('button') as HTMLButtonElement
    expect(botao.disabled).toBe(true)
    expect(botao.textContent).toBe('Salvando…')
  })

  it('não dispara o clique quando está carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao carregando onClick={aoClicar}>Adicionar</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('mantém o rótulo normal quando carregando não recebe rótulo próprio', () => {
    render(<Botao carregando>Adicionar</Botao>)

    expect(screen.getByRole('button').textContent).toBe('Adicionar')
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(true)
  })

  it('respeita o disabled vindo de fora, sem estar carregando', () => {
    const aoClicar = vi.fn()
    render(<Botao disabled onClick={aoClicar}>Anterior</Botao>)

    fireEvent.click(screen.getByRole('button'))

    expect(aoClicar).not.toHaveBeenCalled()
  })

  it('dá pesos visuais diferentes a primário, secundário e perigo', () => {
    // A propriedade sob teste é "os três são DISTINGUÍVEIS", não a lista exata de classes: um
    // teste que fixasse as classes quebraria em todo ajuste de espaçamento e viraria ruído.
    const { container } = render(
      <div>
        <Botao variante="primario">P</Botao>
        <Botao variante="secundario">S</Botao>
        <Botao variante="perigo">X</Botao>
      </div>,
    )
    // Classe a classe (`split`), NUNCA `toContain` sobre a string inteira: a classe do secundário
    // termina em `hover:bg-acao-fundo`, que **contém `bg-acao` como substring**. MEDIDO no
    // pré-flight de 2026-08-09: um `toContain('bg-acao')` sobre a string casa com o secundário e
    // não distingue coisa nenhuma. Sobre o array, `toContain` exige o token exato.
    const classes = Array.from(container.querySelectorAll('button')).map((b) => b.className.split(/\s+/))

    expect(new Set(classes.map((c) => c.join(' '))).size).toBe(3)
    expect(classes[0]).toContain('bg-acao')       // primário: preenchido
    expect(classes[1]).toContain('border')        // secundário: contorno neutro
    expect(classes[2]).toContain('bg-negativo')   // perigo: vermelho reservado a estado
  })

  it('usa primário quando a variante não é dita', () => {
    render(<Botao>Adicionar</Botao>)

    // Mesmo motivo do teste acima, e aqui é o que dá sentido ao M14: sobre a string inteira, trocar
    // o default para `secundario` PASSARIA, porque `hover:bg-acao-fundo` contém `bg-acao`.
    expect(screen.getByRole('button').className.split(/\s+/)).toContain('bg-acao')
  })

  it('tem indicação de foco visível', () => {
    // Critério de aceite da spec §11: foco visível em todo controle interativo. `focus-visible`,
    // e não `focus`: o anel não deve aparecer no clique de mouse, só na navegação por teclado.
    render(<Botao>Adicionar</Botao>)

    // O token EXATO, não o prefixo: `toContain('focus-visible:outline')` sobre a string casava
    // também com `focus-visible:outline-offset-2` e `focus-visible:outline-acao`, que sobrariam
    // depois da M5. MEDIDO no pré-flight de 2026-08-09 — a M5 sobrevivia à asserção antiga.
    expect(screen.getByRole('button').className.split(/\s+/)).toContain('focus-visible:outline-2')
  })

  it('é do tipo button por padrão, para não submeter formulário sem querer', () => {
    // `<button>` sem `type` dentro de `<form>` é `submit` por especificação. Todo botão de ação
    // secundária dentro de um formulário submeteria o formulário — defeito silencioso e clássico.
    render(<Botao>Cancelar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('button')
  })

  it('aceita type=submit quando o chamador pede', () => {
    render(<Botao type="submit">Adicionar</Botao>)

    expect(screen.getByRole('button').getAttribute('type')).toBe('submit')
  })
})
