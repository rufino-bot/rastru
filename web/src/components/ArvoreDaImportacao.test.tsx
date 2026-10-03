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
      filhos: [PARAFUSO_SOB_SUPORTE],
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
    fireEvent.click(linha(/AR-400 Arruela/))
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
    expect(document.querySelector('[aria-current]')).toBeNull()

    // Seleciona o Parafuso (registro 3), que aparece duas vezes.
    fireEvent.click(within(screen.getByTestId('linha-importacao-0-1')).getByRole('button', { name: /PA-300 Parafuso/ }))
    rerender(<ArvoreDaImportacao raiz={RAIZ} selecionado={3} aoSelecionar={() => {}} />)

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

  it('Selecao_vinda_de_fora_marca_a_primeira_ocorrencia', () => {
    render(<ArvoreDaImportacao raiz={RAIZ} selecionado={3} aoSelecionar={() => {}} />)

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
