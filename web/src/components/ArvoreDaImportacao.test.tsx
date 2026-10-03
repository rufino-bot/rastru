// @vitest-environment jsdom
import { describe, it, expect, afterEach, vi } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { ArvoreDaImportacao } from './ArvoreDaImportacao'
import type { NoDaImportacaoDto } from '../api/importacao'

afterEach(cleanup)

function no(parcial: Partial<NoDaImportacaoDto> & Pick<NoDaImportacaoDto, 'codigo' | 'descricao'>): NoDaImportacaoDto {
  return {
    registroId: null,
    componenteId: null,
    filhoId: null,
    quantidadePorPai: null,
    origem: 'Bom',
    pendencias: [],
    filhos: [],
    ...parcial,
  }
}

// Raiz -> Suporte (registro 2, aresta 20) e Parafuso (registro 3, aresta 30) -> Arruela (catálogo).
// O Parafuso aparece DUAS vezes (sob a raiz e sob o Suporte): é o "mesmo código" da seleção.
const PARAFUSO_SOB_SUPORTE = no({
  registroId: 3, componenteId: 300, filhoId: 31, codigo: 'PA-300', descricao: 'Parafuso', quantidadePorPai: 4,
})
const RAIZ = no({
  registroId: 1,
  codigo: 'CH-100',
  descricao: 'Chassi',
  pendencias: ['Novo'],
  filhos: [
    no({
      registroId: 2, componenteId: 200, filhoId: 20, codigo: 'SU-200', descricao: 'Suporte', quantidadePorPai: 2,
      pendencias: ['Divergente', 'SemSolido', 'Inativo'],
      filhos: [
        PARAFUSO_SOB_SUPORTE,
        // O mesmo Componente do catálogo (sem registro) também aparece sob o Suporte.
        no({ componenteId: 400, codigo: 'AR-400', descricao: 'Arruela', quantidadePorPai: 2, origem: 'Catalogo' }),
      ],
    }),
    no({
      registroId: 3, componenteId: 300, filhoId: 30, codigo: 'PA-300', descricao: 'Parafuso', quantidadePorPai: 8,
      filhos: [
        no({
          registroId: null, componenteId: 400, codigo: 'AR-400', descricao: 'Arruela',
          quantidadePorPai: 1, origem: 'Catalogo',
        }),
      ],
    }),
  ],
})

function linha(nome: RegExp | string) {
  return screen.getByRole('button', { name: nome })
}

describe('ArvoreDaImportacao', () => {
  it('Clicar_no_no_chama_aoSelecionar', () => {
    const aoSelecionar = vi.fn()
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={aoSelecionar} />)

    fireEvent.click(linha(/SU-200 Suporte/))
    expect(aoSelecionar).toHaveBeenLastCalledWith(2, 200)

    // Nó que veio só do catálogo: sem registro, só o Componente.
    fireEvent.click(within(screen.getByTestId('linha-importacao-0-1-0')).getByRole('button', { name: /AR-400 Arruela/ }))
    expect(aoSelecionar).toHaveBeenLastCalledWith(null, 400)
  })

  it('Pilulas_de_pendencia_usam_atencao_e_Novo_usa_neutro', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={() => {}} />)

    const classes = (el: HTMLElement) => el.className.split(/\s+/)
    const linhaDoSuporte = screen.getByTestId('linha-importacao-0-0')
    for (const rotulo of ['Receita divergente', 'Sem sólido', 'Inativo']) {
      const pilula = within(linhaDoSuporte).getByText(rotulo)
      expect(classes(pilula)).toContain('bg-atencao-fundo')
      expect(classes(pilula)).toContain('text-atencao-texto')
    }

    const novo = within(screen.getByTestId('linha-importacao-0')).getByText('Novo')
    expect(classes(novo)).toContain('bg-acao-fundo')
    expect(classes(novo)).toContain('text-acao')
    expect(classes(novo)).not.toContain('bg-atencao-fundo')
  })

  it('Ocorrencias_do_mesmo_codigo_ficam_destacadas_juntas', () => {
    const { rerender } = render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={() => {}} />)
    expect(screen.queryByText('mesmo código')).toBeNull()
    expect(screen.queryAllByRole('button', { current: true })).toHaveLength(0)

    // Seleciona o Parafuso (registro 3), que aparece duas vezes.
    fireEvent.click(within(screen.getByTestId('linha-importacao-0-1')).getByRole('button', { name: /PA-300 Parafuso/ }))
    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={{ registroId: 3, componenteId: 300 }} aoSelecionar={() => {}} />)

    // A ocorrência clicada leva aria-current; a outra, o indicador textual.
    const clicada = within(screen.getByTestId('linha-importacao-0-1')).getByRole('button', { name: /PA-300 Parafuso/ })
    expect(clicada.getAttribute('aria-current')).toBe('true')
    const outra = screen.getByTestId('linha-importacao-0-0-0')
    expect(within(outra).getByText('mesmo código')).toBeTruthy()
    expect(within(outra).getByRole('button', { name: /PA-300 Parafuso/ }).getAttribute('aria-current')).toBeNull()
    expect(screen.getAllByText('mesmo código')).toHaveLength(1)
    // Nó de outro código: nem destaque nem indicador.
    expect(within(screen.getByTestId('linha-importacao-0-0')).queryByText('mesmo código')).toBeNull()
  })

  it('No_so_do_catalogo_selecionado_fica_marcado_e_marca_as_outras_ocorrencias', () => {
    const { rerender } = render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={() => {}} />)

    // Clica a Arruela sob o Parafuso (catálogo, sem registro) e a tela devolve a seleção.
    fireEvent.click(within(screen.getByTestId('linha-importacao-0-1-0')).getByRole('button', { name: /AR-400 Arruela/ }))
    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={{ registroId: null, componenteId: 400 }} aoSelecionar={() => {}} />)

    const clicada = within(screen.getByTestId('linha-importacao-0-1-0')).getByRole('button', { name: /AR-400 Arruela/ })
    expect(clicada.getAttribute('aria-current')).toBe('true')
    expect(screen.queryAllByRole('button', { current: true })).toHaveLength(1)
    expect(within(screen.getByTestId('linha-importacao-0-0-1')).getByText('mesmo código')).toBeTruthy()
    expect(screen.getAllByText('mesmo código')).toHaveLength(1)
  })

  it('Selecao_de_catalogo_vinda_de_fora_marca_a_primeira_ocorrencia', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={{ registroId: null, componenteId: 400 }} aoSelecionar={() => {}} />)

    const primeira = within(screen.getByTestId('linha-importacao-0-0-1')).getByRole('button', { name: /AR-400 Arruela/ })
    expect(primeira.getAttribute('aria-current')).toBe('true')
    expect(within(screen.getByTestId('linha-importacao-0-1-0')).getByText('mesmo código')).toBeTruthy()
  })

  it('Selecao_que_nao_existe_mais_nao_marca_nada', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={{ registroId: 99, componenteId: 990 }} aoSelecionar={() => {}} />)

    expect(screen.queryAllByRole('button', { current: true })).toHaveLength(0)
    expect(screen.queryByText('mesmo código')).toBeNull()
  })

  it('Pedido_de_rolagem_leva_a_selecionada_a_vista_de_novo_a_cada_pedido', () => {
    const rolar = vi.fn()
    HTMLElement.prototype.scrollIntoView = rolar
    const selecao = { registroId: 3, componenteId: 300 }
    const { rerender } = render(
      <ArvoreDaImportacao raiz={RAIZ} selecionado={selecao} aoSelecionar={() => {}} pedidoDeRolagem={0} />,
    )
    expect(rolar).not.toHaveBeenCalled()

    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={selecao} aoSelecionar={() => {}} pedidoDeRolagem={1} />)
    expect(rolar).toHaveBeenCalledTimes(1)
    // A mesma seleção, pedida outra vez: rola de novo.
    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={selecao} aoSelecionar={() => {}} pedidoDeRolagem={2} />)
    expect(rolar).toHaveBeenCalledTimes(2)
    // Nenhum pedido novo: nenhuma rolagem (clicar numa linha não rola).
    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={selecao} aoSelecionar={() => {}} pedidoDeRolagem={2} />)
    expect(rolar).toHaveBeenCalledTimes(2)
    // A linha declara a margem que a mantém fora da região fixa do topo.
    expect(screen.getByTestId('linha-importacao-0-0-0').className).toContain('scroll-mt-')
    delete (HTMLElement.prototype as { scrollIntoView?: unknown }).scrollIntoView
  })

  it('A_linha_inteira_e_alvo_de_toque_e_o_campo_de_quantidade_fica_acima', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={() => {}} aoAlterarQuantidade={() => {}} />)

    // jsdom não roda Tailwind: o que se prende é a DECLARAÇÃO das classes (como o alternador da
    // ArvoreDeEstrutura), não a geometria.
    const linha = screen.getByTestId('linha-importacao-0-0')
    expect(linha.className.split(/\s+/)).toContain('relative')
    const botao = within(linha).getByRole('button', { name: /SU-200 Suporte/ })
    expect(botao.className).toContain('after:absolute')
    expect(botao.className).toContain('after:inset-0')
    const campo = within(linha).getByLabelText('Quantidade por pai de Suporte')
    expect(campo.closest('div[class*="z-10"]')).not.toBeNull()
  })

  it('Selecao_vinda_de_fora_marca_a_primeira_ocorrencia', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={{ registroId: 3, componenteId: 300 }} aoSelecionar={() => {}} />)

    const primeira = within(screen.getByTestId('linha-importacao-0-0-0')).getByRole('button', { name: /PA-300 Parafuso/ })
    expect(primeira.getAttribute('aria-current')).toBe('true')
    expect(within(screen.getByTestId('linha-importacao-0-1')).getByText('mesmo código')).toBeTruthy()
  })

  it('Quantidade_so_e_editavel_em_aresta_do_BOM', () => {
    const aoAlterarQuantidade = vi.fn()
    render(
      <ArvoreDaImportacao
        raiz={RAIZ}
        selecionado={null}
        aoSelecionar={() => {}}
        aoAlterarQuantidade={aoAlterarQuantidade}
      />,
    )

    // Aresta do BOM (filhoId): campo editável.
    const campo = within(screen.getByTestId('linha-importacao-0-0')).getByLabelText('Quantidade por pai de Suporte')
    expect((campo as HTMLInputElement).value).toBe('2')

    // Sem mudança, o blur não escreve.
    fireEvent.blur(campo)
    expect(aoAlterarQuantidade).not.toHaveBeenCalled()

    // Valor inválido não escreve, e o campo volta ao que estava.
    fireEvent.change(campo, { target: { value: 'abc' } })
    fireEvent.blur(campo)
    expect(aoAlterarQuantidade).not.toHaveBeenCalled()
    expect((campo as HTMLInputElement).value).toBe('2')

    fireEvent.change(campo, { target: { value: '5' } })
    fireEvent.blur(campo)
    expect(aoAlterarQuantidade).toHaveBeenCalledTimes(1)
    expect(aoAlterarQuantidade).toHaveBeenCalledWith(20, 5)

    // Raiz (sem filhoId) e nó do catálogo: sem campo; a quantidade, quando existe, é texto.
    expect(within(screen.getByTestId('linha-importacao-0')).queryByRole('textbox')).toBeNull()
    const doCatalogo = screen.getByTestId('linha-importacao-0-1-0')
    expect(within(doCatalogo).queryByRole('textbox')).toBeNull()
    expect(within(doCatalogo).getByText(/1/)).toBeTruthy()
  })

  it('Sem_aoAlterarQuantidade_a_quantidade_e_so_texto', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={null} aoSelecionar={() => {}} />)

    expect(screen.queryByRole('textbox')).toBeNull()
  })
})
