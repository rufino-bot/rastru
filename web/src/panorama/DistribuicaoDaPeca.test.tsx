// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { DistribuicaoDaPeca } from './DistribuicaoDaPeca'
import { seg } from './pecaDeTeste'

afterEach(cleanup)

const textos = () => within(screen.getByRole('list', { name: 'Onde está' })).getAllByRole('listitem').map((l) => l.textContent)

describe('DistribuicaoDaPeca', () => {
  it('mostra os segmentos na ordem em que vêm', () => {
    render(<DistribuicaoDaPeca itensEm={[]} segmentos={[
      seg('AIniciar', 2),
      seg('Passo', 3, { setorId: 1, setorNome: 'Solda', ordem: 1 }),
      seg('Conserto', 1, { setorId: 1, setorNome: 'Solda' }),
      seg('Expedicao', 4),
    ]} />)

    expect(textos()).toEqual(['2 a iniciar', '3 Solda', '1 em conserto na Solda', '4 Expedição'])
  })

  it('o aguardando coleta fica dentro do segmento do passo', () => {
    render(<DistribuicaoDaPeca itensEm={[]} segmentos={[
      seg('Passo', 3, { setorId: 1, setorNome: 'Solda', ordem: 1, aguardandoColeta: 1 }),
    ]} />)

    expect(textos()).toEqual(['3 Solda, 1 aguardando coleta'])
  })

  it('expedidas e baixadas levam o traço das saídas, e o resto não', () => {
    render(<DistribuicaoDaPeca itensEm={[]} segmentos={[
      seg('Expedicao', 4), seg('Expedido', 2), seg('Perdido', 1),
    ]} />)

    expect(textos()).toEqual(['4 Expedição', '2 expedidas', '1 baixada'])
    const itens = screen.getAllByRole('listitem')
    expect(itens[0].className).not.toContain('border-dashed')
    expect(itens[1].className).toContain('border-dashed')
    expect(itens[2].className).toContain('border-dashed')
  })

  it('"Itens em" só aparece com itens', () => {
    const { rerender } = render(<DistribuicaoDaPeca segmentos={[seg('AIniciar', 1)]} itensEm={[]} />)
    expect(screen.queryByText(/Itens em/)).toBeNull()

    rerender(<DistribuicaoDaPeca segmentos={[seg('AIniciar', 1)]}
      itensEm={[{ setorId: 1, nome: 'Corte' }, { setorId: 2, nome: 'Dobra' }]} />)
    expect(screen.getByText('Itens em: Corte, Dobra')).toBeTruthy()
  })

  it('segmento nao usa cor de estado', () => {
    render(<DistribuicaoDaPeca itensEm={[{ setorId: 1, nome: 'Corte' }]} segmentos={[
      seg('AIniciar', 1), seg('Passo', 1, { setorNome: 'Solda', ordem: 1 }), seg('Conserto', 1, { setorNome: 'Solda' }),
      seg('Expedicao', 1), seg('Expedido', 1), seg('Perdido', 1),
    ]} />)

    for (const li of screen.getAllByRole('listitem')) {
      expect(li.className).not.toMatch(/positivo|negativo|atencao|atraso/)
    }
  })
})
