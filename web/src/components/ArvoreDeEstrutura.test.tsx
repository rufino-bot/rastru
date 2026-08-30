// @vitest-environment jsdom
import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { ArvoreDeEstrutura } from './ArvoreDeEstrutura'
import type { NoDaEstrutura } from '../api/estrutura'

afterEach(cleanup)

// Três níveis: Peça (catálogo, sem materiais/roteiro) -> Item (catálogo, com materiais e um
// roteiro com setor repetido — regra 21) -> Item folha (catálogo, sem detalhe nenhum).
const folha: NoDaEstrutura = {
  id: 3,
  componenteId: 30,
  codigoDoComponente: 'C-030',
  descricao: 'Parafuso',
  quantidade: 12,
  nivelHierarquico: 'Item',
  requerRelatorioDimensional: false,
  materiais: [],
  roteiro: [],
  filhos: [],
}

const filho: NoDaEstrutura = {
  id: 2,
  componenteId: 20,
  codigoDoComponente: 'C-020',
  descricao: 'Suporte',
  quantidade: 4,
  nivelHierarquico: 'Item',
  requerRelatorioDimensional: false,
  materiais: [{ materialId: 1, nome: 'Aço 1020', quantidade: 2 }],
  // Setor 1 (Corte) aparece duas vezes: regra 21, retorno ao mesmo setor, não duplicata.
  roteiro: [
    { setorId: 1, nome: 'Corte', ordem: 1 },
    { setorId: 2, nome: 'Solda', ordem: 2 },
    { setorId: 1, nome: 'Corte', ordem: 3 },
  ],
  filhos: [folha],
}

const peca: NoDaEstrutura = {
  id: 1,
  componenteId: 10,
  codigoDoComponente: 'C-010',
  descricao: 'Chassi',
  quantidade: 1,
  nivelHierarquico: 'Peca',
  requerRelatorioDimensional: true,
  materiais: [],
  roteiro: [],
  filhos: [filho],
}

const adHoc: NoDaEstrutura = {
  id: 4,
  componenteId: null,
  codigoDoComponente: null,
  descricao: 'Reforço soldado',
  quantidade: 2,
  nivelHierarquico: 'Item',
  requerRelatorioDimensional: false,
  materiais: [],
  roteiro: [],
  filhos: [],
}

describe('ArvoreDeEstrutura', () => {
  it('renderiza um nó por linha, em profundidade', () => {
    render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)

    expect(screen.getByText('Chassi')).toBeTruthy()
    expect(screen.getByText('Suporte')).toBeTruthy()
    expect(screen.getByText('Parafuso')).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })

  it('o recuo cresce com o nível', () => {
    const { container } = render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)
    const linhas = Array.from(
      container.querySelectorAll<HTMLElement>('[data-testid^="linha-no-"]'),
    )
    expect(linhas).toHaveLength(3)

    const recuos = linhas.map((l) => parseInt(l.style.paddingLeft, 10))
    // Não afirma o valor absoluto (amarraria ao pixel) — só a relação: neto > filho > Peça.
    expect(recuos[1]).toBeGreaterThan(recuos[0])
    expect(recuos[2]).toBeGreaterThan(recuos[1])
  })

  it('mostra a quantidade de TODO nó', () => {
    // Mitigação escrita da D5: sem cascata na edição, a única forma de ver uma árvore
    // desproporcional é toda quantidade estar visível — não só a da raiz.
    render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)

    expect(screen.getByText('Qtd: 1')).toBeTruthy()
    expect(screen.getByText('Qtd: 4')).toBeTruthy()
    expect(screen.getByText('Qtd: 12')).toBeTruthy()
  })

  it('nó ad-hoc é distinguível sem usar cor de estado', () => {
    const arvore: NoDaEstrutura = { ...peca, filhos: [adHoc] }
    render(<ArvoreDeEstrutura nos={[arvore]} podeEscrever={false} />)

    expect(screen.getByText('Ad-hoc')).toBeTruthy()

    const linhaAdHoc = screen.getByTestId('linha-no-4')
    expect(linhaAdHoc.innerHTML).not.toContain('text-positivo')
    expect(linhaAdHoc.innerHTML).not.toContain('text-negativo')
  })

  it('expandir um nó revela materiais e roteiro', () => {
    render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)

    expect(screen.queryByText('Aço 1020')).toBeNull()
    expect(screen.queryByText(/Corte/)).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /expandir suporte/i }))

    expect(screen.getByText(/Aço 1020/)).toBeTruthy()
    expect(screen.getAllByText(/Corte/).length).toBeGreaterThan(0)
  })

  it('roteiro com setor repetido mostra os dois passos, na ordem', () => {
    render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)

    fireEvent.click(screen.getByRole('button', { name: /expandir suporte/i }))

    const passos = screen.getAllByTestId('passo-do-roteiro').map((el) => el.textContent)
    expect(passos).toEqual(['1. Corte', '2. Solda', '3. Corte'])
  })

  it('sem permissão de escrita, as ações não são renderizadas', () => {
    render(
      <ArvoreDeEstrutura
        nos={[peca]}
        podeEscrever={false}
        onAcrescentarFilho={vi.fn()}
        onEditar={vi.fn()}
        onExcluir={vi.fn()}
      />,
    )

    expect(screen.queryByRole('button', { name: /acrescentar/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /^editar/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /excluir/i })).toBeNull()
  })

  // --- Além dos sete nomeados ---

  it('(extra) com permissão de escrita, as três ações aparecem por nó', () => {
    // O teste 7 só prova a metade "esconde quando falso". Sem esta metade, uma mutação que
    // removesse o `podeEscrever &&` inteiro (sem condicional nenhuma, sempre oculto) também
    // deixaria o teste 7 verde. Prova a condição nos dois sentidos.
    render(
      <ArvoreDeEstrutura
        nos={[peca]}
        podeEscrever
        onAcrescentarFilho={vi.fn()}
        onEditar={vi.fn()}
        onExcluir={vi.fn()}
      />,
    )

    expect(screen.getAllByRole('button', { name: /acrescentar filho/i }).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: /^editar/i }).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: /excluir/i }).length).toBeGreaterThan(0)
  })

  it('(extra) nó sem materiais e sem roteiro não mostra alternador de expandir', () => {
    // Um alternador sem nada para revelar é um estado morto na tela — a Peça da fixture
    // (`materiais: [], roteiro: []`) não deve ganhar seta nenhuma.
    render(<ArvoreDeEstrutura nos={[peca]} podeEscrever={false} />)

    expect(screen.queryByRole('button', { name: /expandir chassi/i })).toBeNull()
  })
})
