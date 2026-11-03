// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup, within } from '@testing-library/react'
import { ItemComAcao } from './ItemComAcao'
import { ListaDeCadastro } from './ListaDeCadastro'

afterEach(cleanup)

describe('ItemComAcao', () => {
  it('é um item de lista com o conteúdo, a ação e o painel dentro dele', () => {
    render(
      <ListaDeCadastro rotulo="Fila">
        <ItemComAcao acao={<button type="button">Iniciar</button>} painel={<p>painel aberto</p>}>
          <span>Suporte</span>
        </ItemComAcao>
      </ListaDeCadastro>,
    )

    const item = within(screen.getByRole('list', { name: 'Fila' })).getByRole('listitem')
    expect(within(item).getByText('Suporte')).toBeTruthy()
    expect(within(item).getByRole('button', { name: 'Iniciar' })).toBeTruthy()
    expect(within(item).getByText('painel aberto')).toBeTruthy()
  })

  it('sem ação nem painel, só o conteúdo', () => {
    render(<ul><ItemComAcao><span>Suporte</span></ItemComAcao></ul>)

    expect(screen.getByRole('listitem').textContent).toBe('Suporte')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('os botões de ação ficam encostados à direita, ao lado do conteúdo e embaixo dele', () => {
    // jsdom não calcula layout; o teste prende a DECLARAÇÃO. `ml-auto` empurra a ação para a direita tanto quando ela
    // cabe ao lado do conteúdo quanto quando o `flex-wrap` a leva para a linha de baixo, onde `justify-between`
    // sozinho a deixava à esquerda.
    render(<ul><ItemComAcao acao={<button type="button">Iniciar</button>}><span>Suporte</span></ItemComAcao></ul>)

    const acao = screen.getByRole('button', { name: 'Iniciar' }).parentElement!
    expect(acao.className.split(/\s+/)).toContain('ml-auto')
    // Com mais de um botão que quebram entre linhas, cada linha também encosta à direita.
    expect(acao.className.split(/\s+/)).toContain('justify-end')
  })
})
