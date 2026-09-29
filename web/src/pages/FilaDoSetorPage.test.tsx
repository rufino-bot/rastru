// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation, useNavigate } from 'react-router-dom'
import { FilaDoSetorPage } from './FilaDoSetorPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'
import { CHASSI, PARAFUSO, SUPORTE, DESTINO_MONTAGEM, destino, fila, no } from '../testes/execucao'
import type { Estornavel } from '../api/execucao'

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); localStorage.clear() })

// O perfil governa as ações da fila (Task 10 do plano 3): `Administrador` faz todas, e é o padrão
// dos testes que não são sobre perfil.
let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 12, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PECA_B = no({ id: 9, descricao: 'Base', codigoDoComponente: 'BA-01', paiId: null, paiDescricao: null })

const FILA_CHEIA = fila({
  aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }],
  emTrabalho: [{ no: PECA_B, ordem: 1, quantidade: 2.5, estornaveis: [] }],
  aguardandoColeta: [{ no: SUPORTE, ordem: 1, quantidade: 4, destino: destino(), estornaveis: [] }],
  aguardandoMontagem: [{
    pai: CHASSI, faltaMontar: 10, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' },
    filhos: [
      { no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 },
      { no: PARAFUSO, quantidadePorPai: 1, presente: 5, necessarioParaProxima: 3, faltaParaProxima: 0 },
    ],
  }],
  sobra: [
    { no: SUPORTE, origem: 'UltimoPasso', ordem: 2, quantidade: 5, emMaisDeUmSetor: false, estornaveis: [] },
    { no: PARAFUSO, origem: 'Montagem', ordem: null, quantidade: 1, emMaisDeUmSetor: true, estornaveis: [] },
  ],
})

// Lê o `state` da navegação, para o teste do "Trocar de Setor" provar que o link manda
// `{ escolher: true }` — sem isto, a marca de rota era só um `<p>` fixo e não pegava a remoção
// do `state={ESCOLHER}` de `TrocarDeSetor` (achado da review de Task 4).
function MarcaDaEscolha() {
  const { state } = useLocation()
  return <p>{`escolha de setor — state: ${JSON.stringify(state)}`}</p>
}

/** Só para o teste do `key={id}` (I1): troca de Setor sem passar por "Trocar de Setor". */
function Navegar({ para }: { para: string }) {
  const navigate = useNavigate()
  return <button type="button" onClick={() => navigate(para)}>{`ir para ${para}`}</button>
}

function renderizar(caminho = '/fila/1') {
  return render(
    <MemoryRouter initialEntries={[caminho]}>
      <Routes>
        <Route path="/fila/:setorId" element={<FilaDoSetorPage />} />
        <Route path="/fila" element={<MarcaDaEscolha />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('FilaDoSetorPage — leitura', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('mostra carregando antes da fila chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizar()

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('o título nomeia o Setor da fila', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    expect(await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })).toBeTruthy()
  })

  it('a iniciar e em trabalho mostram nó, caminho, quantidade e passo', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const aIniciar = await screen.findByRole('list', { name: 'A iniciar aqui' })
    expect(within(aIniciar).getByText('SUP-01 — Suporte')).toBeTruthy()
    expect(within(aIniciar).getByText('PED-2026-01 › AG-01 › Chassi')).toBeTruthy()
    expect(within(aIniciar).getByText('10 a iniciar · passo 1')).toBeTruthy()
    const emTrabalho = screen.getByRole('list', { name: 'Em trabalho' })
    expect(within(emTrabalho).getByText('2,5 em trabalho · passo 1')).toBeTruthy()
    // Peça: o caminho para no Agrupamento.
    expect(within(emTrabalho).getByText('PED-2026-01 › AG-01')).toBeTruthy()
  })

  it('os botões levam a atividade do Setor quando ele tem uma', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        setorAtividade: 'corte',
        aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }],
        emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 2, estornaveis: [] }],
      })),
    }))

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar corte SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Terminar corte SUP-01 — Suporte' })).toBeTruthy()
  })

  it('o botão de confirmar do formulário também leva a atividade do Setor', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        setorAtividade: 'corte',
        aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }],
        emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 2, estornaveis: [] }],
      })),
    }))

    renderizar()

    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar corte SUP-01 — Suporte' }))
    expect(screen.getByRole('button', { name: 'Iniciar corte' })).toBeTruthy()
    fireEvent.click(screen.getByText('Cancelar'))

    fireEvent.click(screen.getByRole('button', { name: 'Terminar corte SUP-01 — Suporte' }))
    expect(screen.getByRole('button', { name: 'Terminar corte' })).toBeTruthy()
  })

  it('aguardando coleta mostra o destino calculado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoColeta: [
          { no: SUPORTE, ordem: 1, quantidade: 4, destino: destino(), estornaveis: [] },
          { no: SUPORTE, ordem: 3, quantidade: 2, destino: DESTINO_MONTAGEM, estornaveis: [] },
        ],
      })),
    }))

    renderizar()

    const coleta = await screen.findByRole('list', { name: 'Aguardando coleta' })
    expect(within(coleta).getByText('Destino: Dobra (passo 2)')).toBeTruthy()
    expect(within(coleta).getByText('Destino: Montagem de Chassi em Solda')).toBeTruthy()
  })

  it('aguardando montagem diz quanto dá para iniciar e o que falta para a próxima', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const montagem = await screen.findByRole('list', { name: 'Aguardando montagem' })
    expect(within(montagem).getByText('Dá para iniciar 2; falta iniciar 10.')).toBeTruthy()
    const filhos = within(montagem).getByRole('list', { name: 'Filhos de Chassi' })
    expect(within(filhos).getByText('SUP-01 — Suporte: 9 aqui, 4 por unidade — falta 3 de 12 para a próxima')).toBeTruthy()
    // Filho que já basta para a próxima unidade não mostra "falta 0".
    expect(within(filhos).getByText('Parafuso: 5 aqui, 1 por unidade')).toBeTruthy()
  })

  it('sem próxima unidade a montar, não há "falta" nenhum', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoMontagem: [{
          pai: CHASSI, faltaMontar: 2, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' },
          filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 8, necessarioParaProxima: null, faltaParaProxima: null }],
        }],
      })),
    }))

    renderizar()

    expect(await screen.findByText('SUP-01 — Suporte: 8 aqui, 4 por unidade')).toBeTruthy()
  })

  it('a sobra é só informada, e diz quando não dá para saber em que Setor está', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const sobra = await screen.findByRole('list', { name: 'Sobra' })
    expect(within(sobra).getByText('5 a mais no passo 2: o pai já tem o que precisa.')).toBeTruthy()
    expect(within(sobra).getByText('1 a mais aguardando montagem do que o pai precisa.')).toBeTruthy()
    expect(within(sobra).getByText(/não dá para saber em qual está a unidade a mais/)).toBeTruthy()
    // O descarte é da Fase 5: nenhuma ação na seção.
    expect(within(sobra).queryByRole('button')).toBeNull()
  })

  it('seção sem nada não aparece', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }] })),
    }))

    renderizar()

    await screen.findByRole('list', { name: 'A iniciar aqui' })
    expect(screen.queryByRole('list', { name: 'Em trabalho' })).toBeNull()
    expect(screen.queryByRole('list', { name: 'Sobra' })).toBeNull()
  })

  it('fila vazia diz "nada neste Setor agora", não erro', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(fila()) }))

    renderizar()

    expect(await screen.findByText('Nada neste Setor agora')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('falha na primeira carga: banner, sem seções e sem estado vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson({}, 500) }))

    renderizar()

    expect((await screen.findByRole('alert')).textContent)
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nada neste Setor agora')).toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('lembra o Setor cuja fila carregou', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()
    await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })

    expect(localStorage.getItem('rastru.fila.setorId')).toBe('1')
  })

  it('não lembra um Setor que deu 404', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/99/fila': () => respostaJson({ title: 'Not Found' }, 404) }))

    renderizar('/fila/99')

    expect((await screen.findByRole('alert')).textContent).toBe('Este registro não existe mais.')
    expect(localStorage.getItem('rastru.fila.setorId')).toBeNull()
  })

  it('Id que não é número: banner, sem buscar nada', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    renderizar('/fila/abc')

    expect(screen.getByRole('alert').textContent).toBe('Este Setor não existe.')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('"Trocar de Setor" leva à escolha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()
    await screen.findByRole('heading', { level: 1, name: 'Fila — Corte' })
    act(() => { screen.getByRole('link', { name: 'Trocar de Setor' }).click() })

    // Prova o `state`, não só a navegação: sem `state={ESCOLHER}` em `TrocarDeSetor`, `/fila`
    // acharia o Setor lembrado (o 1, lembrado por esta mesma fila) e voltaria para a mesma fila,
    // em vez de mostrar a escolha.
    expect(screen.getByText('escolha de setor — state: {"escolher":true}')).toBeTruthy()
  })

  it('atualiza sozinha a cada 30 s', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => {
        chamadas += 1
        return respostaJson(chamadas === 1
          ? fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }] })
          : fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 6, estornaveis: [] }] }))
      },
    }))

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(screen.getByText('10 a iniciar · passo 1')).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByText('6 a iniciar · passo 1')).toBeTruthy()
  })

  it('atualização que falha mantém a fila na tela, com o aviso', async () => {
    vi.useFakeTimers()
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }] }))
        return Promise.reject(new TypeError('Failed to fetch'))
      },
    }))

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByRole('alert').textContent).toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
    expect(screen.getByText('10 a iniciar · passo 1')).toBeTruthy()
  })
})

/**
 * Mock desta tela com as escritas: a fila é servida em sequência (`filas[0]`, depois `filas[1]`…,
 * repetindo a última), para provar que a tela RECARREGA depois de cada ação, e cada POST responde
 * o que o teste mandar.
 */
function montarFetch(filas: ReturnType<typeof fila>[], escritas: Record<string, () => Response> = {}) {
  let gets = 0
  const fetchMock = vi.fn((url: string | URL, _init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/setores/1/fila') {
      const f = filas[Math.min(gets, filas.length - 1)]
      gets += 1
      return Promise.resolve(respostaJson(f))
    }
    const escrita = escritas[caminho]
    if (escrita) return Promise.resolve(escrita())
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  return { fetchMock, getsDaFila: () => gets }
}

function corpoDe(fetchMock: ReturnType<typeof vi.fn>, caminho: string): unknown {
  const chamada = fetchMock.mock.calls.find((c) => String(c[0]) === caminho)
  expect(chamada, `nenhuma chamada a ${caminho}`).toBeTruthy()
  const init = chamada![1] as RequestInit
  expect(init.method).toBe('POST')
  return JSON.parse(init.body as string)
}

const COM_A_INICIAR = fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] }] })

const FORA_DO_PRIMEIRO_PASSO = fila({
  aguardandoMontagem: [{
    pai: CHASSI, faltaMontar: 10, daParaMontar: 2, iniciaAqui: false, primeiroPassoDoPai: { id: 4, nome: 'Solda' },
    filhos: [
      { no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 },
      { no: PARAFUSO, quantidadePorPai: 1, presente: 0, necessarioParaProxima: 3, faltaParaProxima: 3 },
    ],
  }],
})

describe('FilaDoSetorPage — ações', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('iniciar abre a quantidade com todo o disponível, envia e recarrega', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COM_A_INICIAR, fila()], {
      '/api/estrutura/7/inicios': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '10')
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '4' } })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    expect(await screen.findByText('Nada neste Setor agora')).toBeTruthy()
    expect(corpoDe(fetchMock, '/api/estrutura/7/inicios')).toEqual({ setorId: 1, quantidade: 4 })
    expect(getsDaFila()).toBe(2)
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
  })

  it('terminar manda o passo da linha', async () => {
    const { fetchMock } = montarFetch([fila({ emTrabalho: [{ no: SUPORTE, ordem: 3, quantidade: 6, estornaveis: [] }] })], {
      '/api/estrutura/7/terminos': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Terminar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Terminar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/estrutura/7/terminos')).toEqual({ setorId: 1, ordem: 3, quantidade: 6 }))
  })

  it('o pai inicia no card de montagem com o que dá para iniciar, e a escrita é o início do pai', async () => {
    const { fetchMock } = montarFetch([FILA_CHEIA], { '/api/estrutura/2/inicios': () => respostaJson({}, 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '2')
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/estrutura/2/inicios')).toEqual({ setorId: 1, quantidade: 2 }))
  })

  it('com atividade no Setor, o botão do pai também a leva', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({ ...FILA_CHEIA, setorAtividade: 'montagem' })]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar montagem CH-01 — Chassi' })).toBeTruthy()
  })

  it('trocar de Setor com o MESMO pai aguardando montagem nos dois fecha o "Iniciar" que ficou aberto (o `key={id}` de FilaDoSetorPage, Review Focus 4)', async () => {
    // I1 da review de branch da Fase 3: `chaveDeIniciarPai(paiId)` não inclui o Setor, então o mesmo
    // pai (CHASSI) com `daParaMontar > 0` em dois Setores tem a MESMA chave nos dois — sem o
    // `key={id}` de `FilaDoSetorPage`, o guarda de "Review Focus 3" (que fecha o formulário cuja
    // chave sumiu da fila NOVA) não dispara, porque a chave não sumiu da fila do Setor novo: o
    // formulário reaparece aberto no Setor novo sem que o operador tenha pedido, agora com o
    // `maximo` do Setor 2.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        setorId: 1, setorNome: 'Corte',
        aguardandoMontagem: [{ pai: CHASSI, faltaMontar: 10, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' }, filhos: [] }],
      })),
      '/api/setores/2/fila': () => respostaJson(fila({
        setorId: 2, setorNome: 'Dobra',
        aguardandoMontagem: [{ pai: CHASSI, faltaMontar: 10, daParaMontar: 5, iniciaAqui: true, primeiroPassoDoPai: { id: 2, nome: 'Dobra' }, filhos: [] }],
      })),
    }))

    render(
      <MemoryRouter initialEntries={['/fila/1']}>
        <Navegar para="/fila/2" />
        <Routes>
          <Route path="/fila/:setorId" element={<FilaDoSetorPage />} />
        </Routes>
      </MemoryRouter>,
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'ir para /fila/2' }))

    await screen.findByRole('heading', { level: 1, name: 'Fila — Dobra' })
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
  })

  it('"dá para iniciar 0" não oferece Iniciar', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{
        pai: CHASSI, faltaMontar: 10, daParaMontar: 0, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' },
        filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 3, necessarioParaProxima: 4, faltaParaProxima: 1 }],
      }],
    })]).fetchMock)

    renderizar()
    await screen.findByText('Dá para iniciar 0; falta iniciar 10.')

    expect(screen.queryByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeNull()
  })

  it('409 mostra a frase do servidor no formulário e recarrega a fila', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COM_A_INICIAR, fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 6, estornaveis: [] }] })], {
      '/api/estrutura/7/inicios': () => respostaJson(
        { erro: 'SaldoInsuficiente', mensagem: 'Só há 6 de Suporte a iniciar.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    expect(await screen.findByText('Só há 6 de Suporte a iniciar.')).toBeTruthy()
    await waitFor(() => expect(getsDaFila()).toBe(2))
    // A linha continua na fila (com o saldo novo), então o formulário continua aberto — e o 10 que
    // estava no campo passa a ser recusado pelo limite novo, antes de chegar ao servidor de novo.
    expect(await screen.findByText('No máximo 6.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Iniciar' })).toHaveProperty('disabled', true)
  })

  it('409 cuja recarga tira a linha da fila: o formulário fecha e a frase sobe para o topo', async () => {
    const { fetchMock } = montarFetch([COM_A_INICIAR, fila()], {
      '/api/estrutura/7/inicios': () => respostaJson(
        { erro: 'ConflitoDeConcorrencia', mensagem: 'Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    expect((await screen.findByRole('alert')).textContent)
      .toBe('Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.')
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
  })

  it('atualização periódica que tira a linha aberta fecha o formulário com aviso', async () => {
    // Review Focus 3.
    vi.useFakeTimers()
    vi.stubGlobal('fetch', montarFetch([COM_A_INICIAR, fila()]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.getByRole('alert').textContent)
      .toBe('O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.')
  })

  it('atualização periódica que mantém a linha preserva o que foi digitado', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', montarFetch([COM_A_INICIAR]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '3' } })

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '3')
  })

  it('403 mostra a mensagem e não recarrega', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COM_A_INICIAR], {
      '/api/estrutura/7/inicios': () => respostaJson({}, 403),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar' }))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
    expect(getsDaFila()).toBe(1)
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()
  })

  it('abrir outra ação fecha a que estava aberta', async () => {
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Terminar BA-01 — Base' }))

    expect(screen.getAllByLabelText('Quantidade')).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Terminar' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Iniciar SUP-01 — Suporte' })).toBeTruthy()
  })

  it('fora do primeiro passo do pai, não há Iniciar e o card diz onde o pai começa', async () => {
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()

    expect(await screen.findByText('Chassi começa em Solda: leve os filhos para lá.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeNull()
  })

  it('o Movimentador leva o filho para o primeiro passo do pai, sem escolher Setor', async () => {
    const { fetchMock } = montarFetch([FORA_DO_PRIMEIRO_PASSO], { '/api/entregas': () => respostaJson([], 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' }))
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Levar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/entregas')).toEqual({
      itens: [{ estruturaItemId: 7, origem: { posicao: 'AguardandoMontagem', setorId: 1, ordem: null }, quantidade: 5 }],
    }))
    expect(screen.queryByLabelText('Setor de destino')).toBeNull()
  })

  it('filho ausente não ganha Levar', async () => {
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()
    await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' })

    expect(screen.queryByRole('button', { name: 'Levar para Solda Parafuso' })).toBeNull()
  })

  it('pai sem Roteiro avisa e não oferece Levar', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{ ...FORA_DO_PRIMEIRO_PASSO.aguardandoMontagem[0], primeiroPassoDoPai: null }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByText('Chassi não tem Roteiro. Peça ao PCP para cadastrá-lo.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Levar para/ })).toBeNull()
  })
})

describe('FilaDoSetorPage — perfis (gating na ação, spec §4.8)', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  // Cada ausência é afirmada numa fixture em que a mesma ação EXISTE para outro perfil, e o teste
  // do outro perfil afirma essa presença: `FILA_CHEIA` mostra Iniciar/Terminar a quem apura,
  // `FORA_DO_PRIMEIRO_PASSO` mostra Levar a quem entrega.
  it('o Operador inicia e termina, o pai inclusive, e não leva', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Terminar BA-01 — Base' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeTruthy()
  })

  it('o Operador não leva o filho para o primeiro passo do pai', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()

    // A fixture carregou e traz o filho presente: para o Movimentador (teste abaixo) o botão existe.
    expect(await screen.findByText('Chassi começa em Solda: leve os filhos para lá.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Levar para/ })).toBeNull()
  })

  it('o Movimentador leva o filho para o primeiro passo do pai', async () => {
    perfil = 'Movimentador'
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' })).toBeTruthy()
  })

  it('o Movimentador não inicia, termina nem leva onde o pai já começa', async () => {
    perfil = 'Movimentador'
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    // `FILA_CHEIA` tem Iniciar (filho e pai) e Terminar para o Operador (teste acima).
    expect(await screen.findByText('Dá para iniciar 2; falta iniciar 10.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Iniciar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Terminar/ })).toBeNull()
    // No primeiro passo do pai (`iniciaAqui`) não há para onde levar: nem botão de Levar, nem o aviso
    // "começa em". O par de presença é `FORA_DO_PRIMEIRO_PASSO`, onde este mesmo perfil vê os dois.
    expect(screen.queryByRole('button', { name: /^Levar para/ })).toBeNull()
    expect(screen.queryByText(/começa em/)).toBeNull()
  })

  it('a Gestão lê a fila inteira, sem ação nenhuma', async () => {
    perfil = 'Gestao'
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('list', { name: 'A iniciar aqui' })).toBeTruthy()
    expect(screen.getByRole('list', { name: 'Aguardando montagem' })).toBeTruthy()
    // Sem ação nenhuma: "Trocar de Setor" continua na tela, mas é um link, não conta aqui.
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('a Gestão também não leva o filho, onde o Movimentador leva', async () => {
    perfil = 'Gestao'
    vi.stubGlobal('fetch', montarFetch([FORA_DO_PRIMEIRO_PASSO]).fetchMock)

    renderizar()

    expect(await screen.findByText('Chassi começa em Solda: leve os filhos para lá.')).toBeTruthy()
    expect(screen.queryByRole('button')).toBeNull()
  })
})

const ESTORNAVEL = (id: number, quantidade: number, usuarioId = 12): Estornavel => ({
  tipo: 'Termino', id, quantidade, usuarioId, usuarioNome: 'Operador do Corte', dataHora: '2026-09-28T10:14:00-03:00',
})

const foiChamado = (fetchMock: ReturnType<typeof vi.fn>, caminho: string) =>
  fetchMock.mock.calls.some((c) => String(c[0]) === caminho)

describe('FilaDoSetorPage — estorno rápido', () => {
  beforeEach(() => {
    perfil = 'Operador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  const COLETA = (estornaveis: Estornavel[]) => fila({
    aguardandoColeta: [{ no: SUPORTE, ordem: 1, quantidade: 8, destino: destino(), estornaveis }],
  })

  it('com um registro só, vai direto à confirmação e estorna o movimento', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COLETA([ESTORNAVEL(41, 5)]), fila()], {
      '/api/movimentacoes/41/estorno': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    expect(screen.getByText(/^Estornar o término de 5, registrado por Operador do Corte/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('Nada neste Setor agora')).toBeTruthy()
    expect(foiChamado(fetchMock, '/api/movimentacoes/41/estorno')).toBe(true)
    expect(getsDaFila()).toBe(2)
  })

  it('com vários, abre a lista curta e estorna o escolhido', async () => {
    const { fetchMock } = montarFetch([COLETA([ESTORNAVEL(42, 3), ESTORNAVEL(41, 5)])], {
      '/api/movimentacoes/41/estorno': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    // A lista abre na linha, sem diálogo ainda: nada foi escolhido.
    expect(screen.queryByRole('dialog')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar Término de 5 · Operador do Corte · 28/09/2026 10:14' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(foiChamado(fetchMock, '/api/movimentacoes/41/estorno')).toBe(true))
    expect(foiChamado(fetchMock, '/api/movimentacoes/42/estorno')).toBe(false)
  })

  it('a lista curta só traz os registros que a sessão pode estornar', async () => {
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(42, 3), ESTORNAVEL(41, 5), ESTORNAVEL(40, 2, 99)])]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))

    expect(screen.getAllByRole('button', { name: /^Estornar Término/ })).toHaveLength(2)
  })

  it('com um registro meu e outro alheio, vai direto à confirmação do meu', async () => {
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(42, 3, 99), ESTORNAVEL(41, 5)])]).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))

    expect(screen.getByText(/^Estornar o término de 5, registrado por/)).toBeTruthy()
    expect(screen.queryByText('Qual registro você quer estornar?')).toBeNull()
  })

  it('o registro maior que a linha (desvio D4) aparece e estorna com a quantidade dele', async () => {
    const { fetchMock } = montarFetch([fila({
      emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 40, estornaveis: [ESTORNAVEL(41, 45)] }],
    })], { '/api/movimentacoes/41/estorno': () => respostaJson({}, 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    expect(screen.getByText(/^Estornar o término de 45, registrado por/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(foiChamado(fetchMock, '/api/movimentacoes/41/estorno')).toBe(true))
  })

  it('a linha em trabalho oferece Terminar e Estornar juntos, e a sobra também estorna', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 6, estornaveis: [ESTORNAVEL(41, 5)] }],
      sobra: [{ no: PARAFUSO, origem: 'UltimoPasso', ordem: 2, quantidade: 1, emMaisDeUmSetor: false, estornaveis: [ESTORNAVEL(50, 1)] }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Terminar SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Estornar SUP-01 — Suporte' })).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Sobra' })).getByRole('button', { name: 'Estornar Parafuso' })).toBeTruthy()
  })

  it('o início de um pai estorna pela rota da montagem', async () => {
    const { fetchMock } = montarFetch([fila({
      emTrabalho: [{ no: CHASSI, ordem: 1, quantidade: 3, estornaveis: [{ ...ESTORNAVEL(9, 3), tipo: 'Montagem' }] }],
    })], { '/api/montagens/9/estorno': () => respostaJson([], 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar CH-01 — Chassi' }))
    expect(screen.getByText(/Os filhos voltam a aguardar montagem/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    await waitFor(() => expect(foiChamado(fetchMock, '/api/montagens/9/estorno')).toBe(true))
    expect(foiChamado(fetchMock, '/api/movimentacoes/9/estorno')).toBe(false)
  })

  it('recusa do servidor vira aviso no topo e, no 409, recarrega', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COLETA([ESTORNAVEL(41, 5)])], {
      '/api/movimentacoes/41/estorno': () => respostaJson({ erro: 'EstornoImpossivel', mensagem: 'A quantidade já andou.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('A quantidade já andou.')).toBeTruthy()
    await waitFor(() => expect(getsDaFila()).toBe(2))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('dois registros que cabem um a um e não juntos: o segundo é recusado e a fila recarrega com o que sobrou', async () => {
    const { fetchMock, getsDaFila } = montarFetch([
      COLETA([ESTORNAVEL(42, 3), ESTORNAVEL(41, 5)]),
      COLETA([ESTORNAVEL(41, 5)]),
      COLETA([]),
    ], {
      '/api/movimentacoes/42/estorno': () => respostaJson({}, 201),
      '/api/movimentacoes/41/estorno': () => respostaJson({ erro: 'EstornoImpossivel', mensagem: 'A quantidade já andou.' }, 409),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar Término de 3 · Operador do Corte · 28/09/2026 10:14' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))
    await waitFor(() => expect(getsDaFila()).toBe(2))

    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect(await screen.findByText('A quantidade já andou.')).toBeTruthy()
    await waitFor(() => expect(getsDaFila()).toBe(3))
    expect(screen.queryByRole('button', { name: 'Estornar SUP-01 — Suporte' })).toBeNull()
  })

  it('403 mostra a frase e NÃO recarrega (só o 409 recarrega)', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COLETA([ESTORNAVEL(41, 5)])], {
      '/api/movimentacoes/41/estorno': () => respostaJson({}, 403),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Estornar' }))

    expect((await screen.findByRole('alert')).textContent).toBeTruthy()
    expect(getsDaFila()).toBe(1)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('sem registro que a sessão possa estornar, não há botão', async () => {
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(41, 5, 99)])]).fetchMock) // autor 99, sessão 12, Operador

    renderizar()
    await screen.findByText('8 aguardando coleta · passo 1')

    expect(screen.queryByRole('button', { name: 'Estornar SUP-01 — Suporte' })).toBeNull()
  })

  it('o PCP estorna o registro de outra pessoa', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(41, 5, 99)])]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' })).toBeTruthy()
  })

  it('a Gestão não estorna, mesmo com registro na linha', async () => {
    perfil = 'Gestao'
    vi.stubGlobal('fetch', montarFetch([COLETA([ESTORNAVEL(41, 5)])]).fetchMock)

    renderizar()
    await screen.findByText('8 aguardando coleta · passo 1')

    expect(screen.queryByRole('button')).toBeNull()
  })

  it('linha sem ação nenhuma não ganha o contêiner de ações vazio', async () => {
    perfil = 'Movimentador' // não apura: sem Terminar; e sem registro estornável na linha
    vi.stubGlobal('fetch', montarFetch([fila({
      emTrabalho: [{ no: SUPORTE, ordem: 1, quantidade: 6, estornaveis: [] }],
    })]).fetchMock)

    renderizar()

    const linha = (await screen.findByText('6 em trabalho · passo 1')).closest('li')!
    expect(linha.firstElementChild!.children).toHaveLength(1)
  })

  it('cancelar a confirmação não estorna', async () => {
    const { fetchMock } = montarFetch([COLETA([ESTORNAVEL(41, 5)])])
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Estornar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/estorno'))).toBe(false)
  })
})

describe('FilaDoSetorPage — Pedido pausado (spec da Fase 3D, §6.3)', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  const PAUSA = { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'PED-9 urgente' }
  const BASE_PAUSADA = no({ id: 9, descricao: 'Base', codigoDoComponente: 'BA-01', paiId: null, paiDescricao: null, pausa: PAUSA })
  const CHASSI_PAUSADO = no({ ...CHASSI, pausa: PAUSA })

  // O pausado vem PRIMEIRO no array de propósito: quem separa os grupos é a tela, e não a ordem em
  // que o servidor os manda.
  const A_INICIAR_MISTA = fila({
    aIniciar: [
      { no: BASE_PAUSADA, ordem: 1, quantidade: 3, estornaveis: [] },
      { no: SUPORTE, ordem: 1, quantidade: 10, estornaveis: [] },
    ],
  })

  it('o pausado vai depois do titulo "Pausados", com a pilula e o motivo, e sem Iniciar', async () => {
    vi.stubGlobal('fetch', montarFetch([A_INICIAR_MISTA]).fetchMock)

    renderizar()

    const lista = await screen.findByRole('list', { name: 'A iniciar aqui' })
    const itens = Array.from(lista.children).map((li) => li.textContent ?? '')
    expect(itens).toHaveLength(3)
    expect(itens[0]).toContain('SUP-01 — Suporte')
    expect(itens[1]).toBe('Pausados')
    expect(itens[2]).toContain('BA-01 — Base')
    expect(itens[2]).toContain('Pausado')
    expect(itens[2]).toContain('Pausa: PED-9 urgente')
    expect(itens[2]).toContain('3 a iniciar · passo 1 · Pedido pausado')
    expect(screen.queryByRole('button', { name: 'Iniciar BA-01 — Base' })).toBeNull()
  })

  it('o liberado da mesma seção continua com Iniciar, e sem pilula', async () => {
    vi.stubGlobal('fetch', montarFetch([A_INICIAR_MISTA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' })).toBeTruthy()
    const liberado = screen.getByText('SUP-01 — Suporte').closest('li')!
    expect(within(liberado).queryByText('Pausado')).toBeNull()
  })

  it('sem nenhum pausado, o titulo "Pausados" nao aparece', async () => {
    vi.stubGlobal('fetch', montarFetch([COM_A_INICIAR]).fetchMock)

    renderizar()

    await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' })
    expect(screen.queryByText('Pausados')).toBeNull()
  })

  it('a pilula "Pausado" e neutra', async () => {
    vi.stubGlobal('fetch', montarFetch([A_INICIAR_MISTA]).fetchMock)

    renderizar()

    const classes = (await screen.findByText('Pausado')).className
    expect(classes).not.toMatch(/negativo-|positivo-/)
  })

  it('o pai de Pedido pausado no card de montagem nao tem Iniciar, mas o card e os filhos continuam', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{
        pai: CHASSI_PAUSADO, faltaMontar: 10, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' },
        filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 }],
      }],
    })]).fetchMock)

    renderizar()

    const secao = await screen.findByRole('list', { name: 'Aguardando montagem' })
    expect(within(secao).getByText('Pausado')).toBeTruthy()
    expect(within(secao).getByText('Pausa: PED-9 urgente')).toBeTruthy()
    expect(within(secao).getByText('Dá para iniciar 2; falta iniciar 10.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeNull()
  })

  it('o mesmo card com o Pedido livre oferece o Iniciar do pai', async () => {
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar CH-01 — Chassi' })).toBeTruthy()
  })

  it('fora do primeiro passo, o filho de pai pausado continua levavel (a pausa recusa so o Iniciar)', async () => {
    perfil = 'Movimentador'
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{
        pai: CHASSI_PAUSADO, faltaMontar: 10, daParaMontar: 2, iniciaAqui: false, primeiroPassoDoPai: { id: 4, nome: 'Solda' },
        filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 }],
      }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Levar para Solda SUP-01 — Suporte' })).toBeTruthy()
  })

  it('a linha em trabalho de Pedido pausado continua com Terminar, e leva a pilula', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      emTrabalho: [{ no: BASE_PAUSADA, ordem: 1, quantidade: 2.5, estornaveis: [] }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Terminar BA-01 — Base' })).toBeTruthy()
    const linha = screen.getByText('BA-01 — Base').closest('li')!
    expect(within(linha).getByText('Pausado')).toBeTruthy()
    expect(within(linha).getByText('Pausa: PED-9 urgente')).toBeTruthy()
  })

  it('pausa sem motivo mostra a pilula e nenhuma linha "Pausa:"', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      emTrabalho: [{ no: no({ pausa: { ...PAUSA, motivo: null } }), ordem: 1, quantidade: 2, estornaveis: [] }],
    })]).fetchMock)

    renderizar()

    expect(await screen.findByText('Pausado')).toBeTruthy()
    expect(screen.queryByText(/^Pausa:/)).toBeNull()
  })

  it('o Pedido pausado enquanto o formulario de Iniciar esta aberto: a atualizacao fecha o formulario com aviso', async () => {
    vi.useFakeTimers()
    const pausado = fila({ aIniciar: [{ no: no({ pausa: PAUSA }), ordem: 1, quantidade: 10, estornaveis: [] }] })
    vi.stubGlobal('fetch', montarFetch([COM_A_INICIAR, pausado]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar SUP-01 — Suporte' }))
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.getByText('Pausado')).toBeTruthy()
    expect(screen.getByRole('alert').textContent)
      .toBe('O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.')
  })

  it('a pausa do pai fecha o formulario de Iniciar do card de montagem', async () => {
    vi.useFakeTimers()
    const livre = fila({
      aguardandoMontagem: [{ pai: CHASSI, faltaMontar: 10, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' }, filhos: [] }],
    })
    const pausada = fila({
      aguardandoMontagem: [{ pai: CHASSI_PAUSADO, faltaMontar: 10, daParaMontar: 2, iniciaAqui: true, primeiroPassoDoPai: { id: 1, nome: 'Corte' }, filhos: [] }],
    })
    vi.stubGlobal('fetch', montarFetch([livre, pausada]).fetchMock)

    renderizar()
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    fireEvent.click(screen.getByRole('button', { name: 'Iniciar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()

    await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })

    expect(screen.queryByLabelText('Quantidade')).toBeNull()
    expect(screen.getByRole('alert').textContent)
      .toBe('O item que você estava registrando não está mais nesta fila: outra pessoa o moveu.')
  })
})
