// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { useState } from 'react'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { CabecalhoOrdenavel, proximaOrdem } from './CabecalhoOrdenavel'

afterEach(cleanup)

type Coluna = 'codigo' | 'prazo'
type Ordem = { coluna: Coluna; direcao: 'asc' | 'desc' } | null

function Tabela() {
  const [ordem, setOrdem] = useState<Ordem>(null)
  return (
    <table>
      <thead>
        <tr>
          <CabecalhoOrdenavel coluna="codigo" rotulo="Código" ordem={ordem} aoMudar={setOrdem} />
          <CabecalhoOrdenavel coluna="prazo" rotulo="Prazo" ordem={ordem} aoMudar={setOrdem} />
        </tr>
      </thead>
    </table>
  )
}

const sortDe = (nome: string) => screen.getByRole('columnheader', { name: new RegExp(nome) }).getAttribute('aria-sort')

describe('CabecalhoOrdenavel', () => {
  it('o th tem scope="col" e aria-sort none sem ordem', () => {
    render(<Tabela />)

    const th = screen.getByRole('columnheader', { name: /Código/ })
    expect(th.getAttribute('scope')).toBe('col')
    expect(sortDe('Código')).toBe('none')
  })

  it('cabecalho_compacto: o th usa espacamento lateral de 8 px e texto de 14 px', () => {
    render(<Tabela />)

    const th = screen.getByRole('columnheader', { name: /Código/ })
    expect(th.className).toContain('px-2')
    expect(th.className).toContain('text-sm')
    expect(th.className).not.toContain('px-3')
  })

  it('cicla asc, desc e volta ao original, com aria-sort em cada passo', () => {
    render(<Tabela />)
    const botao = () => screen.getByRole('button', { name: /Código/ })

    fireEvent.click(botao())
    expect(sortDe('Código')).toBe('ascending')

    fireEvent.click(botao())
    expect(sortDe('Código')).toBe('descending')

    fireEvent.click(botao())
    expect(sortDe('Código')).toBe('none')
  })

  it('clicar noutra coluna comeca em asc e a anterior volta a none', () => {
    render(<Tabela />)

    fireEvent.click(screen.getByRole('button', { name: /Código/ }))
    fireEvent.click(screen.getByRole('button', { name: /Código/ }))
    fireEvent.click(screen.getByRole('button', { name: /Prazo/ }))

    expect(sortDe('Prazo')).toBe('ascending')
    expect(sortDe('Código')).toBe('none')
  })
})

describe('proximaOrdem', () => {
  it('null vira asc, asc vira desc, desc vira null', () => {
    expect(proximaOrdem('a', null)).toEqual({ coluna: 'a', direcao: 'asc' })
    expect(proximaOrdem('a', { coluna: 'a', direcao: 'asc' })).toEqual({ coluna: 'a', direcao: 'desc' })
    expect(proximaOrdem('a', { coluna: 'a', direcao: 'desc' })).toBeNull()
  })

  it('outra coluna ativa comeca em asc, venha de asc ou de desc', () => {
    expect(proximaOrdem('b', { coluna: 'a', direcao: 'asc' })).toEqual({ coluna: 'b', direcao: 'asc' })
    expect(proximaOrdem('b', { coluna: 'a', direcao: 'desc' })).toEqual({ coluna: 'b', direcao: 'asc' })
  })
})
