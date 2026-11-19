// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { LinhaDoRoteiro } from './LinhaDoRoteiro'
import { bloco } from '../testes/andamento'

afterEach(cleanup)

const expedicao = bloco({ tipo: 'Expedicao', chave: 'Expedicao', ordem: null, setor: null, situacao: 'Atual', quantidade: 2 })

describe('LinhaDoRoteiro', () => {
  it('um_ponto_por_passo_e_a_expedicao_por_ultimo', () => {
    render(<LinhaDoRoteiro trilha={[
      bloco({ chave: 'Passo:1', ordem: 1, setor: 'Corte', situacao: 'Passado' }),
      bloco({ chave: 'Passo:2', ordem: 2, setor: 'Dobra', situacao: 'Atual', quantidade: 3 }),
      bloco({ chave: 'Passo:3', ordem: 3, setor: 'Solda', situacao: 'Futuro' }),
      bloco({ tipo: 'Montagem', chave: 'Montagem:4', ordem: null, setor: 'Solda', situacao: 'Atual', quantidade: 1 }),
      expedicao,
    ]} />)
    const itens = within(screen.getByRole('list', { name: 'Roteiro' })).getAllByRole('listitem')
    expect(itens.map((i) => i.getAttribute('aria-label'))).toEqual([
      '1 · Corte: já passou', '2 · Dobra: tem peça agora', '3 · Solda: ainda não chegou', 'Expedição: tem peça agora',
    ])
  })

  it('expedicao_vazia_sem_bloco_de_expedicao', () => {
    render(<LinhaDoRoteiro trilha={[bloco({ chave: 'Passo:1', ordem: 1, situacao: 'Atual', quantidade: 1 })]} />)
    const itens = screen.getAllByRole('listitem')
    expect(itens).toHaveLength(2)
    expect(itens[1].getAttribute('aria-label')).toBe('Expedição: ainda não chegou')
  })

  it('nome_inteiro_no_nome_acessivel', () => {
    render(<LinhaDoRoteiro trilha={[bloco({ chave: 'Passo:1', ordem: 1, setor: 'Corte laser', situacao: 'Atual', quantidade: 1 })]} />)
    const item = screen.getByRole('listitem', { name: '1 · Corte laser: tem peça agora' })
    expect(item.getAttribute('title')).toBe('1 · Corte laser: tem peça agora')
  })

  it('sem_passo_nao_ha_linha', () => {
    const { container } = render(<LinhaDoRoteiro trilha={[
      bloco({ tipo: 'AIniciar', chave: 'AIniciar', ordem: null, setor: null, situacao: 'Atual', quantidade: 2 }), expedicao,
    ]} />)
    expect(container.innerHTML).toBe('')
  })
})
