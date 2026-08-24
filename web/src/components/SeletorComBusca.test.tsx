// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { SeletorComBusca } from './SeletorComBusca'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

// `apiFetch` exige `inicializar()` — sem isto toda chamada estoura "client nao inicializado".
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

/** Três componentes, o suficiente para navegar com o teclado e provar a seleção. */
const PAGINA = {
  itens: [
    { id: 1, codigo: 'CH-100', descricao: 'Chapa lateral', tipo: 'Fabricado', ativo: true },
    { id: 2, codigo: 'CH-200', descricao: 'Chapa frontal', tipo: 'Fabricado', ativo: true },
    { id: 3, codigo: 'PA-010', descricao: 'Parafuso M8', tipo: 'Bruto', ativo: true },
  ],
  total: 3,
  pagina: 1,
  tamanho: 20,
}

/** O rótulo do selecionado é `código — descrição`. */
const ROTULO_DO_PRIMEIRO = 'CH-100 — Chapa lateral'

/** Só o `PA-010`, para a lista DEPOIS da busca ser menor que a de antes. */
const PAGINA_FILTRADA = { itens: [PAGINA.itens[2]], total: 1, pagina: 1, tamanho: 20 }

/**
 * O painel NÃO nasce aberto — foco (ou clique, ou digitação) é o que o abre. O bloco do plano
 * chamava `userEvent.click`, que foca de tabela; `fireEvent.click` do jsdom não move o foco, então
 * aqui o gatilho é explícito. Os dois desvios estão registrados no relatório da task.
 */
function abrir(): HTMLElement {
  const campo = screen.getByRole('combobox')
  fireEvent.focus(campo)
  return campo
}

describe('SeletorComBusca', () => {
  it('mostra as opções que a busca devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    abrir()

    expect(await screen.findByText('CH-100')).toBeTruthy()
    expect(screen.getByText('Parafuso M8')).toBeTruthy()
  })

  it('seleciona com Enter depois de navegar com a seta para baixo', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    const campo = abrir()
    await screen.findByText('CH-100')

    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'Enter' })

    expect(aoSelecionar).toHaveBeenCalledWith(expect.objectContaining({ id: 2 }))
  })

  /**
   * A seta para CIMA precisa de prova própria: com só o teste do `ArrowDown`, trocar o `-1` do
   * `ArrowUp` por `+1` deixa a suíte inteira verde (medido — ver a tabela de mutações do relatório).
   *
   * São QUATRO setas para baixo numa lista de três: a quarta é o que prova o limite superior. Com
   * três, o destaque chega ao fim da lista com ou sem o `Math.min`, e a mutação que tira o limite
   * sobrevive — foi assim que ela sobreviveu na primeira rodada de mutação desta task.
   */
  it('as setas andam nos dois sentidos e param no fim da lista', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    const campo = abrir()
    await screen.findByText('CH-100')

    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowUp' })
    fireEvent.keyDown(campo, { key: 'Enter' })

    expect(aoSelecionar).toHaveBeenCalledWith(expect.objectContaining({ id: 2 }))
  })

  it('seleciona com clique', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    abrir()

    fireEvent.click(await screen.findByText('PA-010'))

    expect(aoSelecionar).toHaveBeenCalledWith(expect.objectContaining({ id: 3 }))
    // Escolher fecha: painel que fica aberto depois da escolha tapa a linha seguinte do formulário.
    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull())
  })

  /**
   * O terceiro dos três estados do `CLAUDE.md`, com o molde de `ComponentesPage`: `fetch` que nunca
   * resolve e asserção SÍNCRONA, antes de qualquer resposta. Sem este teste, apagar o
   * `EstadoCarregando` do painel deixa a suíte verde — medido.
   */
  it('mostra o indicador de carregando enquanto a busca está em voo', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    abrir()

    expect(screen.getByText(/carregando/i)).toBeTruthy()
  })

  it('Esc fecha a lista sem selecionar', async () => {
    const aoSelecionar = vi.fn()
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={aoSelecionar} />,
    )
    const campo = abrir()
    await screen.findByText('CH-100')

    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'Escape' })

    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull())
    expect(aoSelecionar).not.toHaveBeenCalled()
  })

  it('anuncia o estado do combobox por ARIA', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)

    const campo = screen.getByRole('combobox')
    expect(campo.getAttribute('aria-expanded')).toBe('false')

    abrir()
    await screen.findByText('CH-100')

    expect(campo.getAttribute('aria-expanded')).toBe('true')
    const lista = screen.getByRole('listbox')
    // `aria-controls` tem de APONTAR para a lista: um id qualquer anuncia certo e navega errado.
    expect(campo.getAttribute('aria-controls')).toBe(lista.getAttribute('id'))
    // Sem destaque não há descendente ativo — apontar para o vazio é pior que não apontar.
    expect(campo.getAttribute('aria-activedescendant')).toBeNull()

    fireEvent.keyDown(campo, { key: 'ArrowDown' })

    const opcoes = screen.getAllByRole('option')
    expect(opcoes[0].getAttribute('id')).toBeTruthy()
    expect(campo.getAttribute('aria-activedescendant')).toBe(opcoes[0].getAttribute('id'))
  })

  /** Estado VAZIO: texto que distingue "não achei" de "não há nada cadastrado". */
  it('mostra estado vazio quando a busca não achou nada', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ itens: [], total: 0, pagina: 1, tamanho: 20 }),
    }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    abrir()

    expect(await screen.findByText(/nenhum componente encontrado/i)).toBeTruthy()
    // A distinção pedida pelo `CLAUDE.md`: sem busca digitada, o vazio é "não há nada cadastrado",
    // não "não achei". Sem esta asserção, um texto único para os dois casos passa despercebido.
    expect(screen.getByText(/não há componente ativo cadastrado/i)).toBeTruthy()
  })

  /** Estado de ERRO: sem ele, falha de rede vira lista vazia silenciosa. */
  it('mostra o erro quando a busca falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/componentes': () => respostaJson({ erro: 'Falhou' }, 500),
    }))
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    abrir()

    expect(await screen.findByRole('alert')).toBeTruthy()
    // Erro não pode virar "não achei nada": são diagnósticos diferentes para o usuário.
    expect(screen.queryByText(/nenhum componente encontrado/i)).toBeNull()
  })

  it('mostra o rótulo do item já selecionado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca
        rotulo="Componente filho"
        valorSelecionado={PAGINA.itens[0]}
        aoSelecionar={vi.fn()}
      />,
    )

    expect(await screen.findByDisplayValue(/CH-100/)).toBeTruthy()

    abrir()
    const opcoes = await screen.findAllByRole('option')
    // `aria-selected` é o que anuncia QUAL das opções é a atual para quem usa leitor de tela.
    expect(opcoes.map((o) => o.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false'])
  })

  /**
   * O duplo papel do input, provado nos dois sentidos: com seleção feita, digitar SUBSTITUI o
   * rótulo (o campo vira busca); `Esc` — e o blur — devolvem o rótulo (o campo volta a ser display
   * da seleção). Sem esta prova o meio-termo fica indefinido, que é a tensão que o brief mandou
   * resolver. A propriedade em uma frase: fora do modo de edição, o que está escrito no campo é
   * sempre a seleção real.
   */
  it('digitar substitui o rótulo do selecionado; Esc e o blur devolvem o rótulo', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/componentes': () => respostaJson(PAGINA) }))
    render(
      <SeletorComBusca
        rotulo="Componente filho"
        valorSelecionado={PAGINA.itens[0]}
        aoSelecionar={vi.fn()}
      />,
    )
    const campo = (await screen.findByDisplayValue(/CH-100/)) as HTMLInputElement

    fireEvent.change(campo, { target: { value: 'PA' } })
    expect(campo.value).toBe('PA')

    fireEvent.keyDown(campo, { key: 'Escape' })
    expect(campo.value).toBe(ROTULO_DO_PRIMEIRO)

    fireEvent.focus(campo)
    fireEvent.change(campo, { target: { value: 'PA' } })
    expect(campo.value).toBe('PA')

    // Sair do campo tem o mesmo efeito do `Esc`: fecha e devolve o rótulo. Sem isto o painel de um
    // seletor ficaria aberto por cima da tela depois de o foco já ter ido embora.
    fireEvent.blur(campo)
    expect(campo.value).toBe(ROTULO_DO_PRIMEIRO)
    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull())
  })

  /**
   * O ciclo de vida do filtro, que nenhum dos testes acima toca — e a mutação provou: apagar o
   * `busca.mudarBusca` do `onChange` deixava a suíte inteira verde, com um seletor que digita e não
   * busca. Três coisas na mesma narrativa, porque são um só ciclo:
   *
   * 1. o texto digitado chega ao servidor como filtro;
   * 2. a lista nova zera o destaque (senão o `aria-activedescendant` aponta para uma opção que não
   *    existe mais na lista encurtada);
   * 3. voltar ao repouso desfaz o filtro — senão o campo mostra o rótulo do selecionado enquanto a
   *    lista continua filtrada por um texto que não está escrito em lugar nenhum.
   */
  it('o texto digitado vira filtro no servidor, zera o destaque, e o repouso desfaz o filtro', async () => {
    let chamadas = 0
    // `fetchPorRota` não serve aqui: ele roteia só por caminho, e o que este teste precisa é de uma
    // resposta DIFERENTE na segunda carga, além da URL completa (com a query) para asserir.
    const fetchFalso = vi.fn((_url: string | URL) => {
      chamadas += 1
      return Promise.resolve(respostaJson(chamadas === 1 ? PAGINA : PAGINA_FILTRADA))
    })
    vi.stubGlobal('fetch', fetchFalso)
    render(<SeletorComBusca rotulo="Componente filho" valorSelecionado={null} aoSelecionar={vi.fn()} />)
    const campo = abrir()
    await screen.findByText('CH-100')

    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    fireEvent.keyDown(campo, { key: 'ArrowDown' })
    expect(campo.getAttribute('aria-activedescendant')).toBeTruthy()

    fireEvent.change(campo, { target: { value: 'PA' } })

    await waitFor(() =>
      expect(String(fetchFalso.mock.calls.at(-1)?.[0])).toContain('busca=PA&'),
    )
    await waitFor(() => expect(screen.queryByText('CH-100')).toBeNull())
    expect(campo.getAttribute('aria-activedescendant')).toBeNull()

    fireEvent.keyDown(campo, { key: 'Escape' })

    await waitFor(() =>
      expect(String(fetchFalso.mock.calls.at(-1)?.[0])).toContain('busca=&'),
    )
  })
})
