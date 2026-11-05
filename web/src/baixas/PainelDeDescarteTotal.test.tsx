// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeDescarteTotal } from './PainelDeDescarteTotal'
import { ErroDeApi } from '../api/erros'
import type { BaixaDto, LinhaDaBaixaDto, PreviaDaBaixaDto } from '../api/baixas'

afterEach(cleanup)

function linha(no: number, posicao: LinhaDaBaixaDto['origemPosicao'], quantidade: number, setorId: number | null = null): LinhaDaBaixaDto {
  return {
    estruturaItemId: no, descricao: `No ${no}`, codigo: null, origemPosicao: posicao,
    origemSetorId: setorId, origemSetorNome: setorId === null ? null : 'Corte', origemOrdem: setorId === null ? null : 1, quantidade,
  }
}

// Dois nós, duas posições distintas ("a iniciar" dos nós 1 e 2 conta uma vez), 9 unidades.
const PREVIA: PreviaDaBaixaDto = {
  linhas: [linha(1, 'AIniciar', 4), linha(1, 'NoSetor', 2, 5), linha(2, 'AIniciar', 3)],
  viramSobra: [], travaNoTopo: null,
}
const BAIXA = {} as BaixaDto

function renderizar(props: Partial<Parameters<typeof PainelDeDescarteTotal>[0]> = {}) {
  const aoFechar = vi.fn()
  const aoConcluir = vi.fn()
  const descartar = vi.fn(async () => BAIXA)
  render(
    <PainelDeDescarteTotal
      titulo="Descartar tudo" escopo="Pedido" prever={async () => PREVIA}
      descartar={descartar} aoConcluir={aoConcluir} aoFechar={aoFechar} {...props}
    />,
  )
  return { aoFechar, aoConcluir, descartar }
}

const enviar = () => within(screen.getByRole('form', { name: 'Descartar tudo' })).getByRole('button', { name: 'Descartar tudo' }) as HTMLButtonElement

describe('PainelDeDescarteTotal', () => {
  it('resume a prévia do servidor e diz o que fica onde está', async () => {
    renderizar()

    expect(await screen.findByText('2 nós com algo em produção, 9 unidades em 2 posições')).toBeTruthy()
    expect(screen.getByText('O que já foi expedido e o que já foi montado ficam onde estão.')).toBeTruthy()
    expect(screen.getByText('Depois disto, o Pedido fica concluído.')).toBeTruthy()
  })

  it('no Agrupamento a frase final fala do Agrupamento', async () => {
    renderizar({ escopo: 'Agrupamento' })
    expect(await screen.findByText('Depois disto, o Agrupamento fica concluído.')).toBeTruthy()
  })

  it('o enviar espera a prévia e só então destrava', async () => {
    let responder: (p: PreviaDaBaixaDto) => void = () => {}
    renderizar({ prever: () => new Promise((r) => { responder = r }) })

    expect(enviar().disabled).toBe(true)
    responder(PREVIA)
    await waitFor(() => expect(enviar().disabled).toBe(false))
  })

  it('a prévia que o servidor recusa mostra a frase dele e deixa o enviar travado', async () => {
    renderizar({
      prever: async () => { throw new ErroDeApi(409, 'Falha (409).', 'Peças com unidade aguardando liberação: CH-1.', 'LiberacaoEmAberto') },
    })

    expect(await screen.findByText(/aguardando liberação: CH-1/)).toBeTruthy()
    expect(enviar().disabled).toBe(true)
  })

  it('dois cliques rápidos no enviar fazem uma requisição só', async () => {
    let terminar: (b: BaixaDto) => void = () => {}
    const descartar = vi.fn(() => new Promise<BaixaDto>((r) => { terminar = r }))
    renderizar({ descartar })
    await waitFor(() => expect(enviar().disabled).toBe(false))
    const form = screen.getByRole('form', { name: 'Descartar tudo' })

    fireEvent.submit(form)
    fireEvent.submit(form)

    expect(descartar).toHaveBeenCalledTimes(1)
    terminar(BAIXA)
  })

  it('manda a observação digitada e, no sucesso, conclui e fecha', async () => {
    const { descartar, aoConcluir, aoFechar } = renderizar()
    await waitFor(() => expect(enviar().disabled).toBe(false))

    fireEvent.change(screen.getByRole('textbox', { name: 'Observação (opcional)' }), { target: { value: '  cliente cancelou ' } })
    fireEvent.click(enviar())

    await waitFor(() => expect(aoFechar).toHaveBeenCalledTimes(1))
    expect(descartar).toHaveBeenCalledWith('cliente cancelou')
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('sem observação manda undefined', async () => {
    const { descartar } = renderizar()
    await waitFor(() => expect(enviar().disabled).toBe(false))
    fireEvent.click(enviar())
    await waitFor(() => expect(descartar).toHaveBeenCalledWith(undefined))
  })

  it('o 409 LiberacaoEmAberto na gravação mostra a frase do servidor dentro do painel e o deixa aberto', async () => {
    const { aoFechar, aoConcluir } = renderizar({
      descartar: async () => { throw new ErroDeApi(409, 'Falha (409).', 'Estorne o pedido de liberação de CH-1.', 'LiberacaoEmAberto') },
    })
    await waitFor(() => expect(enviar().disabled).toBe(false))

    fireEvent.click(enviar())

    expect(await screen.findByText('Estorne o pedido de liberação de CH-1.')).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('o enviar é o botão de perigo', async () => {
    renderizar()
    await waitFor(() => expect(enviar().disabled).toBe(false))
    expect(enviar().className).toContain('bg-negativo')
  })
})
