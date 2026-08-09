// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Campo } from './Campo'

afterEach(cleanup)

describe('Campo', () => {
  it('liga o rótulo ao controle por id, e não por aninhamento', () => {
    // Ligação explícita: leitor de tela anuncia o rótulo, e clicar no texto foca o campo. Hoje as
    // telas usam `placeholder` como rótulo — que some assim que o usuário digita a primeira letra,
    // deixando o campo sem identificação para quem voltar depois de uma interrupção na bancada.
    render(<Campo rotulo="Código">{(id) => <input id={id} />}</Campo>)

    const controle = screen.getByLabelText('Código') as HTMLInputElement
    expect(controle.tagName).toBe('INPUT')
    expect(controle.id).toBeTruthy()
  })

  it('dá ids diferentes a dois campos com o mesmo rótulo', () => {
    // `useId` por instância. Ids repetidos fariam `getByLabelText` casar sempre com o primeiro, e
    // o clique no segundo rótulo focaria o campo errado — na tela e no leitor de tela.
    const { container } = render(
      <div>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
        <Campo rotulo="Código">{(id) => <input id={id} />}</Campo>
      </div>,
    )
    const ids = Array.from(container.querySelectorAll('input')).map((i) => i.id)

    expect(ids[0]).not.toBe(ids[1])
  })

  it('mostra a dica quando ela existe e a associa ao controle', () => {
    render(
      <Campo rotulo="Unidade" dica="UN, KG, M…">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    const controle = screen.getByLabelText('Unidade')
    const dica = screen.getByText('UN, KG, M…')
    expect(controle.getAttribute('aria-describedby')).toBe(dica.id)
  })

  it('não deixa aria-describedby pendurado quando não há dica', () => {
    // Apontar para um id inexistente faz o leitor de tela anunciar vazio — pior que não apontar.
    render(
      <Campo rotulo="Código">
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    expect(screen.getByLabelText('Código').getAttribute('aria-describedby')).toBeNull()
  })

  it('serve a select, não só a input', () => {
    render(
      <Campo rotulo="Tipo">
        {(id) => <select id={id}><option>Bruto</option></select>}
      </Campo>,
    )

    expect((screen.getByLabelText('Tipo') as HTMLSelectElement).tagName).toBe('SELECT')
  })
})
