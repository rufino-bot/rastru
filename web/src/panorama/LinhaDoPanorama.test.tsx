// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import type { EstadoSessao } from '../auth/estadoDaSessao'
import { pecaDeTeste, seg } from './pecaDeTeste'

const estadoAtual: EstadoSessao = {
  status: 'autenticado', usuario: { id: 3, nomeUsuario: 'u', nomeCompleto: 'U', perfil: 'Gestao' },
}
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { LinhaDoPanorama } = await import('./LinhaDoPanorama')

afterEach(cleanup)

function linha(peca = pecaDeTeste()) {
  render(
    <MemoryRouter>
      <table><tbody><LinhaDoPanorama peca={peca} aoGravar={() => {}} /></tbody></table>
    </MemoryRouter>,
  )
  return screen.getByRole('row')
}

describe('LinhaDoPanorama', () => {
  it('as colunas de C4 e o link do código para o Agrupamento', () => {
    const l = linha(pecaDeTeste({ distribuicao: [seg('AIniciar', 2)] }))
    const celulas = within(l).getAllByRole('cell')

    expect(celulas).toHaveLength(9)
    expect(celulas[1].textContent).toBe('293')
    expect(within(l).getByRole('link', { name: 'SUP-01' }).getAttribute('href')).toBe('/agrupamentos/4')
    expect(celulas[3].textContent).toBe('Suporte lateral')
    expect(celulas[4].textContent).toBe('5')
    expect(celulas[5].textContent).toContain('20/11/2026')
    expect(within(l).getByText('2 a iniciar')).toBeTruthy()
  })

  it('atraso é `atraso` e pausa com motivo é `atencao`', () => {
    const base = pecaDeTeste()
    const l = linha({ ...base, pedido: {
      ...base.pedido, atrasado: true, pausa: { desde: 'x', porUsuarioNome: 'u', motivo: 'alteração de projeto' },
    } })

    expect(within(l).getByText('Atrasado').className.split(/\s+/)).toContain('text-atraso-texto')
    expect(within(l).getByText('Pausado: alteração de projeto').className.split(/\s+/)).toContain('text-atencao-texto')
  })

  it('sem atraso nem pausa não há pílula de prazo', () => {
    const l = linha()

    expect(within(l).queryByText('Atrasado')).toBeNull()
    expect(within(l).queryByText(/Pausado/)).toBeNull()
  })

  it('sem miniatura mostra o espaço neutro com o nome do desenho', () => {
    const l = linha()

    expect(within(l).getByRole('img', { name: 'Desenho de SUP-01' })).toBeTruthy()
  })

  it('aferição: Conforme é positivo, Não conforme é negativo, pendente e exige relatório são neutros', () => {
    const classes = (situacao: 'Conforme' | 'NaoConforme' | 'Pendente' | 'SemRelatorio', texto: string) => {
      const l = linha(pecaDeTeste({ afericao: { exigeRelatorio: true, situacao } }))
      const c = within(l).getByText(texto).className.split(/\s+/)
      cleanup()
      return c
    }

    expect(classes('Conforme', 'Conforme')).toContain('text-positivo-texto')
    expect(classes('NaoConforme', 'Não conforme')).toContain('text-negativo-texto')
    expect(classes('Pendente', 'Aferição pendente')).toContain('text-acao')
    expect(classes('SemRelatorio', 'Exige relatório')).toContain('text-acao')
  })

  it('sem relatório não leva pílula de aferição', () => {
    const l = linha()

    expect(within(l).queryByText(/Exige relatório|Aferição pendente|Conforme/)).toBeNull()
  })
})
