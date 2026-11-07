// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ReposicaoDaLinha } from './ReposicaoDaLinha'
import type { DispensaDto } from '../api/retrabalho'

afterEach(cleanup)

let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {}, logout: async () => {},
  }),
}))

const DISPENSA: DispensaDto = {
  id: 9, justificativa: 'cliente aceitou', dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 3, usuarioNome: 'Zé',
}

function montar(props: Partial<Parameters<typeof ReposicaoDaLinha>[0]> = {}) {
  const naoRepor = vi.fn(async () => ({}))
  const desfazer = vi.fn(async () => {})
  const aoMudar = vi.fn()
  render(
    <MemoryRouter>
      <ReposicaoDaLinha
        nome="a baixa de Eixo" aRepor={0} dispensa={null} pedidoRetrabalho={null} reaproveitada={false}
        naoRepor={naoRepor} desfazer={desfazer} aoMudar={aoMudar} {...props}
      />
    </MemoryRouter>,
  )
  return { naoRepor, desfazer, aoMudar }
}

describe('ReposicaoDaLinha', () => {
  it('não mostra nada quando não há o que repor, vínculo nem dispensa', () => {
    montar()
    expect(screen.queryByText('A repor')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('a linha a repor tem a pílula "A repor" em atencao e o "Não repor" para quem pode', () => {
    montar({ aRepor: 2 })
    expect(screen.getByText('A repor').className).toMatch(/atencao/)
    expect(screen.getByRole('button', { name: /Não repor/ })).toBeTruthy()
  })

  it('Qualidade vê a pílula mas não o "Não repor"', () => {
    perfil = 'Qualidade'
    montar({ aRepor: 2 })
    perfil = 'PCP'
    expect(screen.getByText('A repor')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Não repor/ })).toBeNull()
  })

  it('a reposta mostra "Reposta por" com link, sem pílula nem botão de não repor', () => {
    montar({ pedidoRetrabalho: { id: 51, numero: 'P-1-R1' } })
    expect(screen.getByText(/Reposta por/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'P-1-R1' }).getAttribute('href')).toBe('/pedidos/51')
    expect(screen.queryByText('A repor')).toBeNull()
    expect(screen.queryByRole('button', { name: /Não repor/ })).toBeNull()
  })

  it('a reaproveitada mostra "Foi para" com link', () => {
    montar({ pedidoRetrabalho: { id: 51, numero: 'P-1-R1' }, reaproveitada: true })
    expect(screen.getByText(/Foi para/)).toBeTruthy()
    expect(screen.queryByText(/Reposta por/)).toBeNull()
    expect(screen.getByRole('link', { name: 'P-1-R1' })).toBeTruthy()
  })

  it('a dispensada mostra "Não repor: justificativa" e o "Desfazer"', async () => {
    const { desfazer, aoMudar } = montar({ dispensa: DISPENSA })
    expect(screen.getByText('Não repor: cliente aceitou')).toBeTruthy()
    const botao = screen.getByRole('button', { name: /Desfazer/ })
    fireEvent.click(botao)
    fireEvent.click(botao)
    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(desfazer).toHaveBeenCalledTimes(1)
    expect(desfazer).toHaveBeenCalledWith(9)
  })

  it('sem permissão, a dispensada não tem "Desfazer"', () => {
    perfil = 'Qualidade'
    montar({ dispensa: DISPENSA })
    perfil = 'PCP'
    expect(screen.getByText('Não repor: cliente aceitou')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Desfazer/ })).toBeNull()
  })

  it('o "Não repor" abre o painel, grava e recarrega quem a usa', async () => {
    const { naoRepor, aoMudar } = montar({ aRepor: 1 })
    fireEvent.click(screen.getByRole('button', { name: /Não repor/ }))
    const form = screen.getByRole('form', { name: 'Não repor' })
    fireEvent.change(within(form).getByLabelText('Justificativa'), { target: { value: 'ok' } })
    fireEvent.submit(form)
    await waitFor(() => expect(aoMudar).toHaveBeenCalledTimes(1))
    expect(naoRepor).toHaveBeenCalledWith('ok')
    await waitFor(() => expect(screen.queryByRole('form', { name: 'Não repor' })).toBeNull())
  })

  it('a falha do "Desfazer" aparece na linha', async () => {
    const { aoMudar } = montar({ dispensa: DISPENSA, desfazer: vi.fn(async () => { throw new Error('rede') }) })
    fireEvent.click(screen.getByRole('button', { name: /Desfazer/ }))
    expect(await screen.findByText(/Não foi possível desfazer/)).toBeTruthy()
    expect(aoMudar).not.toHaveBeenCalled()
  })
})
