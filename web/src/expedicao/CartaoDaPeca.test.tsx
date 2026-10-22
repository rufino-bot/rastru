// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { inicializar, _resetParaTeste } from '../api/client'
import { fetchPorRota, respostaJson } from '../testes/api'
import { afericao, pecaNaExpedicao, posicoes } from '../testes/expedicao'
import type { EstadoSessao } from '../auth/estadoDaSessao'
import type { PecaNaExpedicaoDto } from '../api/expedicao'
import type { SetorDto } from '../api/cadastros'
import type { LoteDoCartao } from './CartaoDaPeca'

let estadoAtual: EstadoSessao = { status: 'anonimo' }

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ estado: estadoAtual, login: async () => {}, logout: async () => {} }),
}))

const { CartaoDaPeca } = await import('./CartaoDaPeca')

afterEach(() => { cleanup(); vi.unstubAllGlobals(); estadoAtual = { status: 'anonimo' } })

function como(perfil: string, id = 12) {
  estadoAtual = { status: 'autenticado', usuario: { id, nomeUsuario: 'u', nomeCompleto: 'U', perfil } }
}

const SETORES: SetorDto[] = [{ id: 4, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }]

const NAO_CONFORME = pecaNaExpedicao({
  situacao: 'NaoConforme', afericao: afericao(),
  posicoes: posicoes({ naExpedicao: 1, conforme: 3, naoConforme: 1, aguardandoLiberacao: 1, liberada: 1, emConserto: 3 }),
  consertos: [
    { setorId: 4, setorNome: 'Solda', posicao: 'AguardandoConserto', quantidade: 1 },
    { setorId: 6, setorNome: 'Dobra', posicao: 'ConsertoPronto', quantidade: 1 },
    { setorId: 4, setorNome: 'Solda', posicao: 'EmConserto', quantidade: 1 },
  ],
  expedivel: 6,
})

function renderizar(peca: PecaNaExpedicaoDto, extra: { lote?: LoteDoCartao; recarregando?: boolean } = {}) {
  const aoRegistrar = vi.fn()
  const aoAbrirPainel = vi.fn()
  const elemento = (p: PecaNaExpedicaoDto, e = extra): ReactNode => (
    <ul aria-label="Peças">
      <CartaoDaPeca
        peca={p}
        pedidoNumero="PED-1"
        setores={SETORES}
        aoRegistrar={aoRegistrar}
        aoAbrirPainel={aoAbrirPainel}
        {...e}
      />
    </ul>
  )
  const r = render(elemento(peca))
  return { aoRegistrar, aoAbrirPainel, rerender: (p: PecaNaExpedicaoDto, e = extra) => r.rerender(elemento(p, e)) }
}

const cartao = () => screen.getByRole('list', { name: 'Peças' }).firstElementChild as HTMLElement
/** Um botão de ação do cartão; o submit de um painel aberto, que pode ter o mesmo texto, não conta. */
const botao = (nome: string) => within(cartao())
  .queryAllByRole('button', { name: new RegExp(`^${nome}`) })
  .find((b) => b.closest('form') === null) ?? null
const ACOES = ['Aferir', 'Contar', 'Mandar para conserto', 'Pedir liberação', 'Expedir']
const acoesVisiveis = () => ACOES.filter((a) => botao(a) !== null)

/** O valor de uma linha da contagem, achada pelo rótulo dela. */
function linhaDaContagem(rotulo: string): string | null {
  const contagem = within(cartao()).getByRole('list', { name: 'Contagem das unidades' })
  const linha = within(contagem).getAllByRole('listitem').find((li) => li.firstElementChild?.textContent === rotulo)
  return linha ? linha.lastElementChild!.textContent : null
}

describe('CartaoDaPeca', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
    vi.stubGlobal('fetch', fetchPorRota({}))
    como('Qualidade')
  })

  it('mostra "6 aqui" e "de 10 · 0 expedidas" e as pílulas da situação', () => {
    renderizar(pecaNaExpedicao())

    expect(within(cartao()).getByText('QD-01 — Quadro')).toBeTruthy()
    expect(within(cartao()).getByText('6 aqui')).toBeTruthy()
    expect(within(cartao()).getByText('de 10 · 0 expedidas')).toBeTruthy()
    expect(within(cartao()).getByText('Exige relatório')).toBeTruthy()
    expect(within(cartao()).getByText('Aferição pendente')).toBeTruthy()
    // Sem aferição não conforme, não há contagem.
    expect(within(cartao()).queryByRole('list', { name: 'Contagem das unidades' })).toBeNull()
  })

  it('a Peça sem relatório mostra só a pílula "Sem relatório"', () => {
    renderizar(pecaNaExpedicao({ requerRelatorioDimensional: false, situacao: 'SemRelatorio' }))

    expect(within(cartao()).getByText('Sem relatório')).toBeTruthy()
    expect(within(cartao()).queryByText('Exige relatório')).toBeNull()
    expect(within(cartao()).queryByText('Aferição pendente')).toBeNull()
  })

  it('"aqui" é o local de expedição inteiro, sem o conserto', () => {
    renderizar({ ...NAO_CONFORME, posicoes: { ...NAO_CONFORME.posicoes, expedido: 2 } })

    // 1 a medir + 3 conformes + 1 aguardando decisão + 1 aguardando liberação + 1 liberada.
    expect(within(cartao()).getByText('7 aqui')).toBeTruthy()
    expect(within(cartao()).getByText('de 10 · 2 expedidas')).toBeTruthy()
  })

  it('depois de não conforme mostra a contagem com uma linha por Setor de conserto', () => {
    renderizar(NAO_CONFORME)

    expect(linhaDaContagem('Conformes')).toBe('3')
    // 1 aguardando decisão + 1 aguardando liberação + 1 liberada + 3 em conserto.
    expect(linhaDaContagem('Não conformes')).toBe('6')
    expect(linhaDaContagem('· liberadas pela Gestão')).toBe('1')
    expect(linhaDaContagem('· em conserto (Solda)')).toBe('2')
    expect(linhaDaContagem('· em conserto (Dobra)')).toBe('1')
    expect(linhaDaContagem('· aguardando liberação')).toBe('1')
    expect(linhaDaContagem('· aguardando decisão')).toBe('1')
    expect(linhaDaContagem('A medir')).toBe('1')
    const rotulos = within(within(cartao()).getByRole('list', { name: 'Contagem das unidades' }))
      .getAllByRole('listitem').map((li) => li.firstElementChild!.textContent)
    expect(rotulos).toEqual([
      'Conformes', 'Não conformes', '· liberadas pela Gestão', '· em conserto (Solda)', '· em conserto (Dobra)',
      '· aguardando liberação', '· aguardando decisão', 'A medir',
    ])
  })

  it('a contagem omite as linhas de dentro que estão em zero, e o "A medir" quando não há', () => {
    renderizar(pecaNaExpedicao({
      situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ conforme: 4, naoConforme: 2 }),
    }))

    expect(linhaDaContagem('Conformes')).toBe('4')
    expect(linhaDaContagem('Não conformes')).toBe('2')
    expect(linhaDaContagem('· aguardando decisão')).toBe('2')
    expect(linhaDaContagem('· liberadas pela Gestão')).toBeNull()
    expect(linhaDaContagem('· aguardando liberação')).toBeNull()
    expect(linhaDaContagem('A medir')).toBeNull()
  })

  it('pílula Conforme usa positivo e Não conforme usa negativo; as outras neutras', () => {
    const tom = (texto: string) => within(cartao()).getByText(texto).className

    renderizar(pecaNaExpedicao({ situacao: 'Conforme', afericao: afericao({ resultado: 'Conforme' }) }))
    expect(tom('Conforme')).toContain('positivo')
    expect(tom('Exige relatório')).not.toMatch(/positivo|negativo|atencao|atraso/)
    cleanup()

    renderizar(NAO_CONFORME)
    expect(tom('Não conforme')).toContain('negativo')
    cleanup()

    renderizar(pecaNaExpedicao())
    expect(tom('Aferição pendente')).not.toMatch(/positivo|negativo|atencao|atraso/)
    cleanup()

    renderizar(pecaNaExpedicao({ requerRelatorioDimensional: false, situacao: 'SemRelatorio' }))
    expect(tom('Sem relatório')).not.toMatch(/positivo|negativo|atencao|atraso/)
  })

  it('a liberação em aberto aparece no cartão, em texto neutro', () => {
    renderizar({
      ...NAO_CONFORME,
      liberacaoPendente: {
        id: 5, quantidade: 1, justificativa: 'Sobra de dobra', dataHora: '2026-10-20T11:00:00-03:00',
        usuarioNome: 'Qualidade do turno',
      },
    })

    const texto = within(cartao()).getByText(/Liberação pedida/)
    expect(texto.textContent).toBe(
      'Liberação pedida: 1, por Qualidade do turno em 20/10/2026 11:00 — Sobra de dobra',
    )
    expect(texto.className).not.toMatch(/positivo|negativo|atencao|atraso/)
  })

  it('Peça sem relatório não oferece Aferir nem Contar', () => {
    renderizar(pecaNaExpedicao({ requerRelatorioDimensional: false, situacao: 'SemRelatorio' }))

    expect(acoesVisiveis()).toEqual(['Expedir'])
  })

  it('Aferir só aparece com a aferição pendente', () => {
    const { rerender } = renderizar(pecaNaExpedicao())
    expect(acoesVisiveis()).toEqual(['Aferir', 'Expedir'])

    rerender(pecaNaExpedicao({ situacao: 'Conforme', afericao: afericao({ resultado: 'Conforme' }) }))
    expect(acoesVisiveis()).toEqual(['Expedir'])
  })

  it('Contar só aparece com aferição não conforme e algo a medir', () => {
    const { rerender } = renderizar(pecaNaExpedicao({
      situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ naExpedicao: 6 }),
    }))
    expect(acoesVisiveis()).toEqual(['Contar', 'Expedir'])

    rerender(pecaNaExpedicao({
      situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ conforme: 6 }),
    }))
    expect(botao('Contar')).toBeNull()

    rerender(pecaNaExpedicao({ situacao: 'Conforme', afericao: afericao({ resultado: 'Conforme' }) }))
    expect(botao('Contar')).toBeNull()
  })

  it('Mandar para conserto e Pedir liberação só aparecem com não conforme aguardando decisão', () => {
    const { rerender } = renderizar(NAO_CONFORME)
    expect(acoesVisiveis()).toEqual(['Contar', 'Mandar para conserto', 'Pedir liberação', 'Expedir'])

    // As não conformes todas aguardando a Gestão ou em conserto: nada aguarda decisão.
    rerender({ ...NAO_CONFORME, posicoes: { ...NAO_CONFORME.posicoes, naoConforme: 0 } })
    expect(botao('Mandar para conserto')).toBeNull()
    expect(botao('Pedir liberação')).toBeNull()
  })

  it('com uma liberação em aberto não oferece outro pedido (C18), mas oferece o conserto', () => {
    renderizar({
      ...NAO_CONFORME,
      liberacaoPendente: {
        id: 5, quantidade: 1, justificativa: 'Sobra', dataHora: '2026-10-20T11:00:00-03:00', usuarioNome: 'Q',
      },
    })

    expect(botao('Pedir liberação')).toBeNull()
    expect(botao('Mandar para conserto')).toBeTruthy()
  })

  it('Expedir só aparece com algo expedível', () => {
    renderizar(pecaNaExpedicao({
      situacao: 'NaoConforme', afericao: afericao(), posicoes: posicoes({ aguardandoLiberacao: 2 }), expedivel: 0,
    }))

    expect(botao('Expedir')).toBeNull()
  })

  it('o nome acessível de cada botão começa pelo texto dele e diz a Peça e o Pedido', () => {
    renderizar(NAO_CONFORME)

    expect(botao('Expedir')!.getAttribute('aria-label')).toBe('Expedir QD-01 — Quadro (PED-1)')
    expect(botao('Expedir')!.textContent).toBe('Expedir')
  })

  it('sem permissão de registrar não há nenhum botão de escrita', () => {
    for (const perfil of ['Gestao', 'PCP', 'Operador']) {
      como(perfil)
      renderizar(NAO_CONFORME, {
        lote: { marcado: false, texto: null, desabilitado: false, aoMarcar: vi.fn(), aoDigitar: vi.fn() },
      })

      expect(acoesVisiveis()).toEqual([])
      expect(within(cartao()).queryByRole('checkbox')).toBeNull()
      // A leitura continua: a contagem e o histórico.
      expect(linhaDaContagem('Conformes')).toBe('3')
      expect(within(cartao()).getByText('Histórico')).toBeTruthy()
      cleanup()
    }
  })

  it('abrir um painel esconde as ações, avisa a tela, e Cancelar devolve o foco ao botão', async () => {
    const { aoAbrirPainel } = renderizar(NAO_CONFORME)

    fireEvent.click(botao('Mandar para conserto')!)

    const form = within(cartao()).getByRole('form', { name: 'Mandar para conserto' })
    expect(acoesVisiveis()).toEqual([])
    expect(aoAbrirPainel).toHaveBeenCalledTimes(1)
    // O foco vai ao primeiro campo do painel.
    expect(document.activeElement).toBe(within(form).getByRole('combobox', { name: 'Setor' }))

    fireEvent.click(within(form).getByRole('button', { name: 'Cancelar' }))

    expect(within(cartao()).queryByRole('form')).toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(botao('Mandar para conserto')))
  })

  it('cada botão abre o painel dele', () => {
    const paineis: [string, string][] = [
      ['Contar', 'Contar as unidades'], ['Mandar para conserto', 'Mandar para conserto'],
      ['Pedir liberação', 'Pedir liberação'], ['Expedir', 'Expedir remessa'],
    ]
    renderizar(NAO_CONFORME)
    for (const [acao, titulo] of paineis) {
      fireEvent.click(botao(acao)!)
      const form = within(cartao()).getByRole('form', { name: titulo })
      fireEvent.click(within(form).getByRole('button', { name: 'Cancelar' }))
    }

    cleanup()
    renderizar(pecaNaExpedicao())
    fireEvent.click(botao('Aferir')!)
    expect(within(cartao()).getByRole('form', { name: 'Aferir' })).toBeTruthy()
  })

  it('a escrita com sucesso fecha o painel e pede a recarga à tela', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/remessas': () => respostaJson({ remessa: {}, avisos: [] }, 201),
    }))
    const { aoRegistrar } = renderizar(NAO_CONFORME)

    fireEvent.click(botao('Expedir')!)
    fireEvent.click(within(cartao()).getByRole('button', { name: 'Expedir 6' }))

    await waitFor(() => expect(aoRegistrar).toHaveBeenCalledTimes(1))
    expect(within(cartao()).queryByRole('form')).toBeNull()
  })

  it('o foco espera a recarga e, se o botão sumiu, cai no nome da Peça', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/estrutura/30/afericoes': () => respostaJson(afericao({ resultado: 'Conforme' }), 201),
    }))
    const { aoRegistrar, rerender } = renderizar(pecaNaExpedicao(), { recarregando: false })

    fireEvent.click(botao('Aferir')!)
    // A tela começa a recarregar quando o cartão pede.
    aoRegistrar.mockImplementation(() => rerender(pecaNaExpedicao(), { recarregando: true }))
    fireEvent.click(screen.getByRole('radio', { name: 'Conforme' }))
    fireEvent.click(screen.getByRole('button', { name: 'Registrar aferição' }))
    await waitFor(() => expect(aoRegistrar).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(within(cartao()).queryByRole('form')).toBeNull())

    // A recarga volta com a aferição: o "Aferir" sumiu.
    rerender(pecaNaExpedicao({ situacao: 'Conforme', afericao: afericao({ resultado: 'Conforme' }) }), { recarregando: false })

    await waitFor(() => expect(document.activeElement).toBe(within(cartao()).getByText('QD-01 — Quadro')))
  })

  describe('lote', () => {
    const lote = (sobre: Partial<LoteDoCartao> = {}): LoteDoCartao =>
      ({ marcado: false, texto: null, desabilitado: false, aoMarcar: vi.fn(), aoDigitar: vi.fn(), ...sobre })

    it('a caixa marca a Peça e avisa a tela', () => {
      const aoMarcar = vi.fn()
      renderizar(NAO_CONFORME, { lote: lote({ aoMarcar }) })

      fireEvent.click(within(cartao()).getByRole('checkbox', { name: 'Marcar QD-01 — Quadro (PED-1)' }))

      expect(aoMarcar).toHaveBeenCalledWith(true)
    })

    it('a Peça sem nada a expedir não tem caixa', () => {
      renderizar({ ...NAO_CONFORME, expedivel: 0 }, { lote: lote() })

      expect(within(cartao()).queryByRole('checkbox')).toBeNull()
    })

    it('marcada, mostra o campo com o expedível, a leitura dele e a prévia dos avisos', () => {
      const aoDigitar = vi.fn()
      const { rerender } = renderizar(NAO_CONFORME, { lote: lote({ marcado: true, aoDigitar }) })

      const campo = within(cartao()).getByRole('textbox', { name: 'Quantidade — QD-01 — Quadro (PED-1)' }) as HTMLInputElement
      expect(campo.value).toBe('6')
      // 1 liberada + 3 conformes + 1 a medir + 1 não conforme: avisa a sem medição e a sem liberação.
      const avisos = within(within(cartao()).getByRole('list', { name: 'Avisos da remessa' })).getAllByRole('listitem')
      expect(avisos.map((a) => a.textContent)).toEqual(['1 sem medição', '1 não conforme sem liberação'])

      fireEvent.change(campo, { target: { value: '9' } })
      expect(aoDigitar).toHaveBeenCalledWith('9')

      rerender(NAO_CONFORME, { lote: lote({ marcado: true, texto: '9' }) })
      expect(within(cartao()).getByText('No máximo 6.')).toBeTruthy()
      expect(within(cartao()).queryByRole('list', { name: 'Avisos da remessa' })).toBeNull()
    })

    it('mudar a marca fecha o painel aberto no cartão', () => {
      const { rerender } = renderizar(NAO_CONFORME, { lote: lote() })
      fireEvent.click(botao('Expedir')!)
      expect(within(cartao()).getByRole('form', { name: 'Expedir remessa' })).toBeTruthy()

      rerender(NAO_CONFORME, { lote: lote({ marcado: true }) })

      expect(within(cartao()).queryByRole('form')).toBeNull()
    })

    it('o campo trava enquanto o lote está em voo', () => {
      renderizar(NAO_CONFORME, { lote: lote({ marcado: true, desabilitado: true }) })

      expect((within(cartao()).getByRole('textbox', { name: /^Quantidade/ }) as HTMLInputElement).disabled).toBe(true)
      expect((within(cartao()).getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
    })
  })
})
