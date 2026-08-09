// @vitest-environment jsdom
import { describe, it, expect, afterEach } from 'vitest'
import { render, screen, cleanup } from '@testing-library/react'
import { ListaDeCadastro, ItemDeCadastro } from './ListaDeCadastro'

afterEach(cleanup)

describe('ListaDeCadastro', () => {
  it('é uma lista com um item por filho', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro>Corte</ItemDeCadastro>
        <ItemDeCadastro>Solda</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByRole('list')).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
  })

  it('aceita um rótulo acessível para distinguir duas listas na mesma tela', () => {
    render(<ListaDeCadastro rotulo="Agrupamentos"><ItemDeCadastro>A</ItemDeCadastro></ListaDeCadastro>)

    expect(screen.getByRole('list', { name: 'Agrupamentos' })).toBeTruthy()
  })
})

describe('ItemDeCadastro', () => {
  it('mostra o conteúdo e a ação', () => {
    render(
      <ListaDeCadastro>
        <ItemDeCadastro acao={<button>Inativar</button>}>Corte</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(screen.getByText('Inativar')).toBeTruthy()
  })

  it('distingue item inativo de item ativo', () => {
    // Distinção que NÃO é só cor: `line-through` mais tinta fraca. Cor sozinha exclui quem não a
    // percebe, e a lista de inativos é justamente onde o usuário decide se reativa ou não.
    const { container } = render(
      <ListaDeCadastro>
        <ItemDeCadastro ativo>Ativo</ItemDeCadastro>
        <ItemDeCadastro ativo={false}>Inativo</ItemDeCadastro>
      </ListaDeCadastro>,
    )
    const [itemAtivo, itemInativo] = Array.from(container.querySelectorAll('li'))

    expect(itemInativo.textContent).toContain('Inativo')
    expect(itemInativo.innerHTML).toContain('line-through')
    expect(itemAtivo.innerHTML).not.toContain('line-through')
  })

  it('trata item sem a prop ativo como ativo', () => {
    // `Agrupamento` não tem coluna `Ativo` — a lista do PedidoDetalhe usa o item sem a prop, e não
    // pode sair riscada por causa disso.
    const { container } = render(
      <ListaDeCadastro><ItemDeCadastro>AGR-01</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(container.querySelector('li')!.innerHTML).not.toContain('line-through')
  })

  it('anuncia o estado inativo em texto, não só no traço', () => {
    // `line-through` é visual e não chega ao leitor de tela. Sem isto, quem usa leitor ouve
    // "Corte" nos dois casos e não tem como saber qual está inativo.
    render(
      <ListaDeCadastro><ItemDeCadastro ativo={false}>Corte</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(screen.getByText('(inativo)')).toBeTruthy()
  })
})
