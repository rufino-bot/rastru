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

const AGORA = '2026-11-19T15:00:00-03:00'

function linha(peca = pecaDeTeste()) {
  render(
    <MemoryRouter>
      <table><tbody><LinhaDoPanorama agora={AGORA} peca={peca} aoGravar={async () => {}} /></tbody></table>
    </MemoryRouter>,
  )
  return screen.getByRole('row')
}

describe('LinhaDoPanorama', () => {
  it('as colunas de C4 e o link do código para o Agrupamento', () => {
    const l = linha(pecaDeTeste({ distribuicao: [seg('AIniciar', 2)] }))
    const celulas = within(l).getAllByRole('cell')

    expect(celulas).toHaveLength(11)
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
    expect(within(l).getByText('Pausado').className.split(/\s+/)).toContain('text-atencao-texto')
  })

  it('o motivo da pausa fica fora da pílula, neutro, em duas linhas e inteiro no title', () => {
    const base = pecaDeTeste()
    const motivo = 'alteração de projeto pedida pelo cliente depois da liberação do desenho para a fábrica'
    const l = linha({ ...base, pedido: {
      ...base.pedido, pausa: { desde: 'x', porUsuarioNome: 'u', motivo },
    } })

    const texto = within(l).getByText(motivo)
    const classes = texto.className.split(/\s+/)
    expect(texto.getAttribute('title')).toBe(motivo)
    expect(classes).toContain('line-clamp-2')
    expect(classes).toContain('text-tinta-fraca')
    expect(classes.some((c) => /atencao|atraso|positivo|negativo/.test(c))).toBe(false)
    expect(within(l).getByText('Pausado').textContent).toBe('Pausado')
  })

  it('pausa sem motivo é só a pílula', () => {
    const base = pecaDeTeste()
    const l = linha({ ...base, pedido: { ...base.pedido, pausa: { desde: 'x', porUsuarioNome: 'u', motivo: null } } })

    expect(within(l).getByText('Pausado')).toBeTruthy()
    expect(l.querySelector('[title]')).toBeNull()
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

  it('mostra_idade_do_segmento_e_da_coleta', () => {
    const l = linha(pecaDeTeste({ distribuicao: [
      seg('Passo', 3, {
        setorId: 1, setorNome: 'Solda', ordem: 1, aguardandoColeta: 1,
        desde: '2026-11-18T10:00:00-03:00', aguardandoColetaDesde: '2026-11-19T10:00:00-03:00',
      }),
    ] }))

    expect(within(l).getByText('3 Solda · há 1 d, coleta 1 · há 5 h')).toBeTruthy()
  })

  it('parado_ha_e_complicacao', () => {
    const l = linha(pecaDeTeste({
      paradoDesde: '2026-11-16T09:00:00-03:00',
      complicacoes: [
        { codigo: 'MaterialPendente', texto: 'Material não separado: Chapa 3 mm' },
        { codigo: 'ARepor', texto: '2 a repor' },
      ],
    }))
    const celulas = within(l).getAllByRole('cell')

    expect(celulas[7].textContent).toBe('3 d')
    expect(celulas[7].querySelector('strong, .font-bold, .font-semibold')).toBeTruthy()
    expect(within(celulas[8]).getAllByRole('listitem').map((i) => i.textContent)).toEqual([
      'Material não separado: Chapa 3 mm', '2 a repor',
    ])
  })

  it('sem parado nem complicação as duas células ficam vazias', () => {
    const celulas = within(linha()).getAllByRole('cell')
    expect(celulas[7].textContent).toBe('')
    expect(celulas[8].textContent).toBe('')
  })

  it('complicacao_nao_usa_cor_de_estado', () => {
    const l = linha(pecaDeTeste({
      paradoDesde: '2026-11-16T09:00:00-03:00',
      complicacoes: [{ codigo: 'EmConserto', texto: 'Em conserto: 1 na Solda' }],
    }))
    const celulas = within(l).getAllByRole('cell')
    for (const celula of [celulas[7], celulas[8]]) {
      const html = [celula, ...celula.querySelectorAll('*')].map((e) => e.className).join(' ')
      expect(html).not.toMatch(/positivo|negativo|atencao|atraso/)
    }
  })
})
