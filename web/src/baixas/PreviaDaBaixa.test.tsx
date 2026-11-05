// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { PreviaDaBaixa } from './PreviaDaBaixa'
import type { LinhaDaBaixaDto, PreviaDaBaixaDto } from '../api/baixas'

afterEach(cleanup)

const linha = (estruturaItemId: number, descricao: string, quantidade: number): LinhaDaBaixaDto => ({
  estruturaItemId, descricao, codigo: null, origemPosicao: 'AIniciar',
  origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade,
})

const NO = linha(7, 'Suporte', 3)
const FILHO = linha(8, 'Parafuso', 6)
const TOPO = linha(5, 'Chassi', 2)

function previa(parcial: Partial<PreviaDaBaixaDto> = {}): PreviaDaBaixaDto {
  return { linhas: [NO, FILHO, TOPO], viramSobra: [], travaNoTopo: null, ...parcial }
}

describe('PreviaDaBaixa', () => {
  it('separa as linhas do no das que saem junto, cada lista com titulo visivel', () => {
    render(<PreviaDaBaixa previa={previa()} noId={7} />)
    const doNo = screen.getByRole('list', { name: 'Saem de produção:' })
    const junto = screen.getByRole('list', { name: 'Saem junto:' })
    expect(within(doNo).getAllByRole('listitem')).toHaveLength(1)
    expect(within(doNo).getByText(/Suporte/)).toBeTruthy()
    expect(within(junto).getAllByRole('listitem')).toHaveLength(2)
    expect(within(junto).getByText(/Parafuso/)).toBeTruthy()
    expect(within(junto).getByText(/Chassi/)).toBeTruthy()
    for (const t of ['Saem de produção:', 'Saem junto:']) {
      expect(screen.getByText(t).tagName).not.toBe('UL')
    }
  })

  it('sem linhas de outros nos nao ha lista nem titulo de "Saem junto:"', () => {
    render(<PreviaDaBaixa previa={previa({ linhas: [NO] })} noId={7} />)
    expect(screen.getByRole('list', { name: 'Saem de produção:' })).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Saem junto:' })).toBeNull()
    expect(screen.queryByText('Saem junto:')).toBeNull()
  })

  it('sem linhas do no nao ha lista nem titulo de "Saem de produção:"', () => {
    render(<PreviaDaBaixa previa={previa({ linhas: [FILHO, TOPO] })} noId={7} />)
    expect(screen.getByRole('list', { name: 'Saem junto:' })).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Saem de produção:' })).toBeNull()
    expect(screen.queryByText('Saem de produção:')).toBeNull()
  })

  it('previa vazia diz que nada sai e nao mostra lista nenhuma', () => {
    render(<PreviaDaBaixa previa={previa({ linhas: [] })} noId={7} />)
    expect(screen.getByText('Nada sai de produção com esses valores.')).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Saem de produção:' })).toBeNull()
    expect(screen.queryByRole('list', { name: 'Saem junto:' })).toBeNull()
  })

  it('a sobra continua com o titulo e a lista de hoje', () => {
    render(<PreviaDaBaixa previa={previa({ viramSobra: [{ estruturaItemId: 8, descricao: 'Parafuso', quantidade: 2 }] })} noId={7} />)
    expect(screen.getByText('Ficam onde estão e viram sobra:')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Viram sobra' })).getAllByRole('listitem')).toHaveLength(1)
  })
})
