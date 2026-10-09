// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Pilula } from './Pilula'

afterEach(cleanup)

describe('Pilula', () => {
  it('mostra o rótulo', () => {
    render(<Pilula>Montagem</Pilula>)

    expect(screen.getByText('Montagem')).toBeTruthy()
  })

  it('usa a tinta de ação sobre fundo tingido no tom neutro', () => {
    // A mesma `--color-acao` do botão, agora sobre fundo de baixa saturação. Um segundo tom só
    // para a pílula seria uma cor a mais para manter coerente, sem ganho (spec §3).
    //
    // TOKEN A TOKEN (`split`), nunca `toContain` sobre a className inteira: com `toContain`, mutar
    // `text-acao` para `text-acao-forte` SOBREVIVE, porque a segunda string contém a primeira.
    // Essa armadilha custou três achados na Task 5.
    render(<Pilula>Kit</Pilula>)

    const classes = screen.getByText('Kit').className.split(/\s+/)
    expect(classes).toContain('bg-acao-fundo')
    expect(classes).toContain('text-acao')
  })

  it('o tom atencao usa o par declarado de amarelo, sem verde, vermelho nem a tinta de acao', () => {
    // Token a token, pelo mesmo motivo do teste do tom neutro. O par `atencao-texto`/`atencao-fundo`
    // é medido pela guarda de contraste; nenhum modificador de opacidade.
    render(<Pilula tom="atencao">Pausado</Pilula>)

    const classes = screen.getByText('Pausado').className.split(/\s+/)
    expect(classes).toContain('bg-atencao-fundo')
    expect(classes).toContain('text-atencao-texto')
    expect(classes.some((c) => /positivo-|negativo-|acao/.test(c))).toBe(false)
    expect(classes.some((c) => c.includes('/'))).toBe(false)
  })

  it('o tom atraso usa o par roxo declarado, sem verde, vermelho, ambar nem a tinta de acao', () => {
    // Token a token, pelo mesmo motivo do teste do tom neutro. Roxo e o quarto estado reservado: so
    // Pedido atrasado (D5 da spec da data de entrega).
    render(<Pilula tom="atraso">Atrasado</Pilula>)

    const classes = screen.getByText('Atrasado').className.split(/\s+/)
    expect(classes).toContain('bg-atraso-fundo')
    expect(classes).toContain('text-atraso-texto')
    expect(classes.some((c) => /positivo-|negativo-|atencao-|acao/.test(c))).toBe(false)
    expect(classes.some((c) => c.includes('/'))).toBe(false)
  })

  it('reserva verde e vermelho para estado, em tons declarados', () => {
    const { container } = render(
      <div>
        <Pilula tom="positivo">Aprovado</Pilula>
        <Pilula tom="negativo">Reprovado</Pilula>
      </div>,
    )
    const [positiva, negativa] = Array.from(container.querySelectorAll('span'))
      .map((s) => s.className.split(/\s+/))

    // `positivo-texto`, não `positivo`: são dois tokens com papéis distintos na paleta.
    // `text-negativo-texto` e não `text-negativo`, pelo mesmo motivo — e com `split` a distinção
    // é real: sob `toContain`, `'text-negativo-texto'` satisfaria uma asserção de `'text-negativo'`.
    expect(positiva).toContain('text-positivo-texto')
    expect(negativa).toContain('text-negativo-texto')

    // Os fundos são tokens DECLARADOS, medidos pela guarda da Task 4. Nenhum modificador de
    // opacidade: era por `/NN` que a cor escapava da guarda (Critical da review da Task 5).
    expect(positiva).toContain('bg-positivo-fundo')
    expect(negativa).toContain('bg-negativo-fundo')
    expect([...positiva, ...negativa].some((c) => c.includes('/'))).toBe(false)
  })
})
