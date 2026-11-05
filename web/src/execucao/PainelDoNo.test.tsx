// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, act } from '@testing-library/react'
import type { EstadoSessao } from '../auth/estadoDaSessao'
import type { NoDaEstrutura } from '../api/estrutura'
import type { Posicao, SaldoDto } from '../api/execucao'

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))
// As seções de baixo têm teste próprio e buscam na rede: aqui interessa só o cabeçalho e o painel da baixa.
vi.mock('./EditorDeRoteiroDoNo', () => ({ EditorDeRoteiroDoNo: () => null }))
const montagens = { livro: 0, baixas: [] as number[], aoEstornar: undefined as undefined | (() => void) }
vi.mock('./HistoricoDoNo', () => ({
  HistoricoDoNo: () => { montagens.livro += 1; return null },
}))
vi.mock('../baixas/HistoricoDeBaixas', () => ({
  HistoricoDeBaixas: (p: { versao: number; aoEstornar: () => void }) => {
    montagens.baixas.push(p.versao)
    montagens.aoEstornar = p.aoEstornar
    return null
  },
}))
vi.mock('../separacao/MateriaisDoNo', () => ({ MateriaisDoNo: () => null }))
vi.mock('../separacao/HistoricoDeSeparacoes', () => ({ HistoricoDeSeparacoes: () => null }))
// O painel da baixa tem teste próprio.
vi.mock('../baixas/PainelDeBaixa', () => ({
  PainelDeBaixa: (p: { no: { id: number; ehPeca: boolean }; aoFechar: () => void; aoConcluir: () => void }) => (
    <form aria-label="Dar baixa">
      <button type="button" onClick={p.aoConcluir}>Concluir</button>
      <span>{`nó ${p.no.id} ${p.no.ehPeca ? 'peça' : 'item'}`}</span>
      <button type="button" onClick={p.aoFechar}>Cancelar</button>
    </form>
  ),
}))

const { PainelDoNo } = await import('./PainelDoNo')

afterEach(() => { cleanup(); estadoAtual = { status: 'anonimo' } })

function como(perfil: string) {
  estadoAtual = { status: 'autenticado', usuario: { id: 12, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
}

const saldo = (posicao: Posicao, quantidade: number): SaldoDto =>
  ({ posicao, quantidade, setorId: null, setorNome: null, ordem: null })

function noDaEstrutura(nivel: 'Peca' | 'Item' = 'Item'): NoDaEstrutura {
  return {
    id: 7, componenteId: 1, codigoDoComponente: 'SUP-01', descricao: 'Suporte', quantidade: 4,
    nivelHierarquico: nivel, requerRelatorioDimensional: false, materiais: [], roteiro: [], filhos: [],
    quantidadePorPai: nivel === 'Item' ? 1 : null, semRoteiro: false,
  }
}

function renderizar(saldos: SaldoDto[], nivel: 'Peca' | 'Item' = 'Item') {
  return render(<PainelDoNo no={noDaEstrutura(nivel)} aoFechar={() => {}} aoMudar={() => {}} saldos={saldos} />)
}

describe('PainelDoNo — seção Baixas', () => {
  it('a baixa dada sobe a versão da seção Baixas e remonta o livro do nó', () => {
    como('Qualidade')
    montagens.baixas = []; montagens.livro = 0
    renderizar([saldo('AIniciar', 4)])
    const livroAntes = montagens.livro
    expect(montagens.baixas.at(-1)).toBe(0)

    fireEvent.click(screen.getByRole('button', { name: 'Dar baixa' }))
    fireEvent.click(screen.getByRole('button', { name: 'Concluir' }))

    expect(montagens.baixas.at(-1)).toBe(1)
    expect(montagens.livro).toBeGreaterThan(livroAntes)
  })

  it('o estorno de uma baixa remonta o livro do nó, que ganhou o estorno', () => {
    como('Qualidade')
    montagens.livro = 0
    renderizar([saldo('AIniciar', 4)])
    const antes = montagens.livro

    act(() => montagens.aoEstornar?.())

    expect(montagens.livro).toBeGreaterThan(antes)
  })
})

describe('PainelDoNo — Dar baixa', () => {
  it.each(['Qualidade', 'PCP', 'Administrador'])('%s com algo em produção vê o "Dar baixa"', (perfil) => {
    como(perfil)

    renderizar([saldo('AIniciar', 4)])

    expect(screen.getByRole('button', { name: 'Dar baixa' })).toBeTruthy()
  })

  it.each(['Operador', 'Movimentador', 'Separador', 'Gestao'])('%s não vê o "Dar baixa"', (perfil) => {
    como(perfil)

    renderizar([saldo('AIniciar', 4)])

    expect(screen.queryByRole('button', { name: 'Dar baixa' })).toBeNull()
  })

  it.each([
    ['sem saldo nenhum', []],
    ['só montado, expedido e perdido', [saldo('Montado', 2), saldo('Expedido', 1), saldo('Perdido', 1)]],
    ['só aguardando liberação', [saldo('AguardandoLiberacao', 2)]],
    ['com saldo zerado', [saldo('AIniciar', 0)]],
  ])('nó %s não tem "Dar baixa"', (_nome, saldos) => {
    como('Qualidade')

    renderizar(saldos)

    expect(screen.queryByRole('button', { name: 'Dar baixa' })).toBeNull()
  })

  it('abre o painel do nó, esconde o botão e devolve o foco a ele ao fechar', async () => {
    como('Qualidade')
    renderizar([saldo('AIniciar', 4)], 'Item')

    fireEvent.click(screen.getByRole('button', { name: 'Dar baixa' }))

    expect(screen.getByRole('form', { name: 'Dar baixa' })).toBeTruthy()
    expect(screen.getByText('nó 7 item')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Dar baixa' })).toBeNull()

    // Como no uso real: o foco está no controle que sai do DOM com o painel.
    screen.getByRole('button', { name: 'Cancelar' }).focus()
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Dar baixa' })))
    expect(screen.queryByRole('form', { name: 'Dar baixa' })).toBeNull()
  })

  it('a Peça chega ao painel como Peça', () => {
    como('PCP')
    renderizar([saldo('NaoConforme', 1)], 'Peca')

    fireEvent.click(screen.getByRole('button', { name: 'Dar baixa' }))

    expect(screen.getByText('nó 7 peça')).toBeTruthy()
  })

  it('a recarga que zera os saldos depois de fechar leva o foco ao título, não ao <body>', async () => {
    como('Qualidade')
    const { rerender } = renderizar([saldo('AIniciar', 4)])
    fireEvent.click(screen.getByRole('button', { name: 'Dar baixa' }))
    screen.getByRole('button', { name: 'Cancelar' }).focus()
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))
    const botao = await screen.findByRole('button', { name: 'Dar baixa' })
    await waitFor(() => expect(document.activeElement).toBe(botao))

    // A recarga chega: tudo foi baixado e o botão sai do DOM com o foco nele.
    rerender(<PainelDoNo no={noDaEstrutura()} aoFechar={() => {}} aoMudar={() => {}} saldos={[saldo('Perdido', 4)]} />)

    expect(screen.queryByRole('button', { name: 'Dar baixa' })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Detalhes de Suporte' }))
  })
})
