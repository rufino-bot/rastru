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

  it('a acao fica num wrapper posicionado acima do conteudo', () => {
    // O que a suíte consegue afirmar é a estrutura e as classes: o pai do botão é posicionado e
    // empilhado. O efeito real (o clique no CENTRO do botão chega ao botão, e não ao link cujo
    // overlay cobre o cartão) depende de layout, que o jsdom não calcula, e é conferido no
    // navegador.
    const { container } = render(
      <ListaDeCadastro>
        <ItemDeCadastro acao={<button>Inativar</button>}>Corte</ItemDeCadastro>
      </ListaDeCadastro>,
    )
    const botao = screen.getByRole('button', { name: 'Inativar' })
    const wrapper = botao.parentElement!

    expect(wrapper.tagName).toBe('DIV')
    expect(wrapper.classList.contains('relative')).toBe(true)
    expect(wrapper.classList.contains('z-10')).toBe(true)
    // O wrapper é filho direto do `<li>` (que é `relative`): o overlay do link, `absolute` com
    // `inset-0`, ancora no `<li>`, e o wrapper empilha acima dele no mesmo contexto.
    expect(wrapper.parentElement).toBe(container.querySelector('li'))
  })

  it('sem acao nao cria wrapper', () => {
    // `acao` ausente e `acao={false}` (o que `podeEscrever && <Botao/>` entrega ao perfil sem
    // escrita) não deixam uma `div` vazia, empilhada, sobre o cartão.
    const { container } = render(
      <ListaDeCadastro>
        <ItemDeCadastro>Sem prop</ItemDeCadastro>
        <ItemDeCadastro acao={false}>Falsa</ItemDeCadastro>
        <ItemDeCadastro acao={null}>Nula</ItemDeCadastro>
      </ListaDeCadastro>,
    )

    expect(container.querySelectorAll('li')).toHaveLength(3)
    expect(container.querySelectorAll('li div')).toHaveLength(0)
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

    // A outra metade da distinção (I1 da review): NÃO é só o traço, é também a tinta fraca.
    // Token a token — `toContain` sobre a string inteira passaria com qualquer classe que
    // contivesse a substring, inclusive coincidências acidentais.
    const classesAtivo = itemAtivo.querySelector('span')!.className.split(/\s+/)
    const classesInativo = itemInativo.querySelector('span')!.className.split(/\s+/)
    expect(classesAtivo).toContain('text-tinta')
    expect(classesAtivo).not.toContain('text-tinta-fraca')
    expect(classesInativo).toContain('text-tinta-fraca')
    expect(classesInativo).not.toContain('text-tinta')
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

  it('o rótulo "(inativo)" fica FORA do span riscado — irmão, não filho (I2 da review)', () => {
    // A decisão é sobre ancestralidade no DOM, não sobre cascata de `text-decoration` resolvida
    // (que o jsdom não pinta). Se "(inativo)" fosse movido para DENTRO do `.line-through`
    // (a M13 do plano) ou se o traço subisse para o span de fora (mutação mais provável, um
    // "simplificar dois spans em um"), o rótulo sairia riscado por construção — e esta asserção
    // sobre a subárvore do elemento que carrega a classe discrimina os dois casos.
    const { container } = render(
      <ListaDeCadastro><ItemDeCadastro ativo={false}>Corte</ItemDeCadastro></ListaDeCadastro>,
    )

    expect(container.querySelector('.line-through')!.textContent).not.toContain('(inativo)')
  })
})
