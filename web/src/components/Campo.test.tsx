// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'
import { Botao } from './Botao'

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

  it('aoLado põe o controle de fora na linha do campo, esticado à altura dele, e a dica embaixo dos dois', () => {
    // O jsdom não mede pixel: o teste prova a ESTRUTURA que dá o alinhamento. O botão e o campo dividem uma
    // linha `flex` com `items-stretch` (o botão fica da altura da caixa do campo), e a dica mora fora dela —
    // senão o botão se alinharia com a base da dica.
    render(
      <Campo rotulo="Quantidade de PAR-M8" dica="Parafuso M8 (UN)" aoLado={<Botao variante="secundario">Remover</Botao>}>
        {(id, idDaDica) => <input id={id} aria-describedby={idDaDica} />}
      </Campo>,
    )

    const controle = screen.getByLabelText('Quantidade de PAR-M8')
    const botao = screen.getByRole('button', { name: 'Remover' })
    const dica = screen.getByText('Parafuso M8 (UN)')
    const linha = botao.parentElement!
    expect(linha.contains(controle)).toBe(true)
    expect(linha.contains(dica)).toBe(false)
    expect(linha.contains(screen.getByText('Quantidade de PAR-M8'))).toBe(false)
    expect(linha.className.split(/\s+/)).toEqual(expect.arrayContaining(['flex', 'items-stretch']))
    // A dica vem depois da linha, e continua ligada ao controle.
    expect(linha.compareDocumentPosition(dica) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(controle.getAttribute('aria-describedby')).toBe(dica.id)
  })

  it('sem aoLado, o controle fica direto no campo, como antes', () => {
    render(<Campo rotulo="Código" dica="Único">{(id) => <input id={id} />}</Campo>)

    const controle = screen.getByLabelText('Código')
    expect(controle.parentElement!.querySelector('label')).not.toBeNull()
  })

  it('o anel de foco de CLASSES_DE_CONTROLE é o mesmo do Botao', () => {
    // P4/I3 da review da Task 5: o trio `focus-visible:outline-2 focus-visible:outline-offset-2
    // focus-visible:outline-acao` vive em dois lugares (Botao.tsx e Campo.tsx) sem nada prendendo
    // um ao outro — forma canônica de nascer divergência. Compara os dois LADOS DE VERDADE
    // (renderiza Botao de verdade e extrai os tokens dele), não uma lista literal copiada aqui:
    // uma asserção só contra `CLASSES_DE_CONTROLE` não prenderia nada, e F9 sobreviveria.
    render(<Botao>Adicionar</Botao>)
    const classesDoBotao = screen.getByRole('button').className.split(/\s+/)
    const tokensDeFoco = classesDoBotao.filter((c) => c.startsWith('focus-visible:'))

    const classesDoControle = CLASSES_DE_CONTROLE.split(/\s+/)

    expect(tokensDeFoco.length).toBeGreaterThan(0)
    for (const token of tokensDeFoco) {
      expect(classesDoControle).toContain(token)
    }
  })
})
