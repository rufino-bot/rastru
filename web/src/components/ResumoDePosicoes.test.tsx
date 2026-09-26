// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { ResumoDePosicoes } from './ResumoDePosicoes'
import type { SaldoDto } from '../api/execucao'

afterEach(cleanup)

const A_INICIAR: SaldoDto = { posicao: 'AIniciar', setorId: null, setorNome: null, ordem: null, quantidade: 4 }
const NO_CORTE: SaldoDto = { posicao: 'NoSetor', setorId: 1, setorNome: 'Corte', ordem: 1, quantidade: 6 }
const COLETA: SaldoDto = { posicao: 'AguardandoColeta', setorId: 1, setorNome: 'Corte', ordem: 1, quantidade: 2 }

describe('ResumoDePosicoes', () => {
  it('mostra uma pílula por posição, na ordem da API', () => {
    render(<ResumoDePosicoes saldos={[A_INICIAR, NO_CORTE, COLETA]} totalMontado={null} />)

    const itens = within(screen.getByRole('list', { name: 'Onde está' })).getAllByRole('listitem')
    expect(itens.map((i) => i.textContent)).toEqual([
      '4 a iniciar', '6 em Corte (passo 1)', '2 aguardando coleta em Corte (passo 1)',
    ])
  })

  it('mostra o total montado no nó com filhos', () => {
    render(<ResumoDePosicoes saldos={[A_INICIAR]} totalMontado={3} />)

    expect(screen.getByText('total montado: 3')).toBeTruthy()
  })

  it('não mostra "total montado: 0"', () => {
    render(<ResumoDePosicoes saldos={[A_INICIAR]} totalMontado={0} />)

    expect(screen.queryByText(/total montado/)).toBeNull()
  })

  it('não desenha lista vazia', () => {
    const { container } = render(<ResumoDePosicoes saldos={[]} totalMontado={null} />)

    expect(container.innerHTML).toBe('')
  })

  it('usa só o tom neutro — posição no livro não é estado', () => {
    // Spec §6.4: "aguardando coleta" e afins nunca em verde/vermelho. Token a token (a lição do
    // `toContain` sobre a className inteira, registrada em `Pilula.test.tsx`).
    render(<ResumoDePosicoes saldos={[A_INICIAR, NO_CORTE, COLETA]} totalMontado={2} />)

    for (const item of screen.getAllByRole('listitem')) {
      const classes = item.firstElementChild!.className.split(/\s+/)
      expect(classes).toContain('bg-acao-fundo')
      expect(classes.some((c) => c.includes('positivo') || c.includes('negativo'))).toBe(false)
    }
  })
})
