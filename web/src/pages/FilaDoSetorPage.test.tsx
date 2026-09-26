// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, within, act, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation, useNavigate } from 'react-router-dom'
import { FilaDoSetorPage } from './FilaDoSetorPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'
import { CHASSI, PARAFUSO, SUPORTE, DESTINO_MONTAGEM, destino, fila, no } from '../testes/execucao'

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
  aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }],
  emTrabalho: [{ no: PECA_B, ordem: 1, quantidade: 2.5 }],
  aguardandoColeta: [{ no: SUPORTE, ordem: 1, quantidade: 4, destino: destino() }],
  aguardandoMontagem: [{
    pai: CHASSI, faltaMontar: 10, daParaMontar: 2,
    filhos: [
      { no: SUPORTE, quantidadePorPai: 4, presente: 9, necessarioParaProxima: 12, faltaParaProxima: 3 },
      { no: PARAFUSO, quantidadePorPai: 1, presente: 5, necessarioParaProxima: 3, faltaParaProxima: 0 },
    ],
  }],
  sobra: [
    { no: SUPORTE, origem: 'UltimoPasso', ordem: 2, quantidade: 5, emMaisDeUmSetor: false },
    { no: PARAFUSO, origem: 'Montagem', ordem: null, quantidade: 1, emMaisDeUmSetor: true },
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

  it('aguardando coleta mostra o destino calculado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoColeta: [
          { no: SUPORTE, ordem: 1, quantidade: 4, destino: destino() },
          { no: SUPORTE, ordem: 3, quantidade: 2, destino: DESTINO_MONTAGEM },
        ],
      })),
    }))

    renderizar()

    const coleta = await screen.findByRole('list', { name: 'Aguardando coleta' })
    expect(within(coleta).getByText('Destino: Dobra (passo 2)')).toBeTruthy()
    expect(within(coleta).getByText('Destino: Montagem de Chassi (sugestão: Solda)')).toBeTruthy()
  })

  it('aguardando montagem diz quanto dá para montar e o que falta para a próxima', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores/1/fila': () => respostaJson(FILA_CHEIA) }))

    renderizar()

    const montagem = await screen.findByRole('list', { name: 'Aguardando montagem' })
    expect(within(montagem).getByText('Dá para montar 2; falta montar 10.')).toBeTruthy()
    const filhos = within(montagem).getByRole('list', { name: 'Filhos de Chassi' })
    expect(within(filhos).getByText('SUP-01 — Suporte: 9 aqui, 4 por unidade — falta 3 de 12 para a próxima')).toBeTruthy()
    // Filho que já basta para a próxima unidade não mostra "falta 0".
    expect(within(filhos).getByText('Parafuso: 5 aqui, 1 por unidade')).toBeTruthy()
  })

  it('sem próxima unidade a montar, não há "falta" nenhum', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        aguardandoMontagem: [{
          pai: CHASSI, faltaMontar: 2, daParaMontar: 2,
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
      '/api/setores/1/fila': () => respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] })),
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
          ? fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] })
          : fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 6 }] }))
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
        if (chamadas === 1) return respostaJson(fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] }))
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

const COM_A_INICIAR = fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 10 }] })

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
    const { fetchMock } = montarFetch([fila({ emTrabalho: [{ no: SUPORTE, ordem: 3, quantidade: 6 }] })], {
      '/api/estrutura/7/terminos': () => respostaJson({}, 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Terminar SUP-01 — Suporte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Terminar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/estrutura/7/terminos')).toEqual({ setorId: 1, ordem: 3, quantidade: 6 }))
  })

  it('montar oferece o que dá para montar, e monta o pai', async () => {
    const { fetchMock } = montarFetch([FILA_CHEIA], { '/api/estrutura/2/montagens': () => respostaJson({}, 201) })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Montar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toHaveProperty('value', '2')
    fireEvent.click(screen.getByRole('button', { name: 'Montar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/estrutura/2/montagens')).toEqual({ setorId: 1, quantidade: 2 }))
  })

  it('trocar de Setor com o MESMO pai aguardando montagem nos dois fecha o "Montar" que ficou aberto (o `key={id}` de FilaDoSetorPage, Review Focus 4)', async () => {
    // I1 da review de branch da Fase 3: `chaveDeMontar(paiId)` não inclui o Setor, então o mesmo
    // pai (CHASSI) com `daParaMontar > 0` em dois Setores tem a MESMA chave nos dois — sem o
    // `key={id}` de `FilaDoSetorPage`, o guarda de "Review Focus 3" (que fecha o formulário cuja
    // chave sumiu da fila NOVA) não dispara, porque a chave não sumiu: ela continua presente,
    // agora com o `maximo` do Setor errado.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores/1/fila': () => respostaJson(fila({
        setorId: 1, setorNome: 'Corte',
        aguardandoMontagem: [{ pai: CHASSI, faltaMontar: 10, daParaMontar: 2, filhos: [] }],
      })),
      '/api/setores/2/fila': () => respostaJson(fila({
        setorId: 2, setorNome: 'Dobra',
        aguardandoMontagem: [{ pai: CHASSI, faltaMontar: 10, daParaMontar: 5, filhos: [] }],
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

    fireEvent.click(await screen.findByRole('button', { name: 'Montar CH-01 — Chassi' }))
    expect(screen.getByLabelText('Quantidade')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'ir para /fila/2' }))

    await screen.findByRole('heading', { level: 1, name: 'Fila — Dobra' })
    expect(screen.queryByLabelText('Quantidade')).toBeNull()
  })

  it('"dá para montar 0" não oferece Montar', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{
        pai: CHASSI, faltaMontar: 10, daParaMontar: 0,
        filhos: [{ no: SUPORTE, quantidadePorPai: 4, presente: 3, necessarioParaProxima: 4, faltaParaProxima: 1 }],
      }],
    })]).fetchMock)

    renderizar()
    await screen.findByText('Dá para montar 0; falta montar 10.')

    expect(screen.queryByRole('button', { name: 'Montar CH-01 — Chassi' })).toBeNull()
  })

  it('409 mostra a frase do servidor no formulário e recarrega a fila', async () => {
    const { fetchMock, getsDaFila } = montarFetch([COM_A_INICIAR, fila({ aIniciar: [{ no: SUPORTE, ordem: 1, quantidade: 6 }] })], {
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

  it('levar para outro Setor oferece os outros Setores do Roteiro do pai e entrega', async () => {
    const { fetchMock } = montarFetch([FILA_CHEIA], {
      '/api/estrutura/2/roteiro': () => respostaJson({
        estruturaItemId: 2,
        passos: [
          { setorId: 1, nome: 'Corte', ordem: 1, alcancado: true },
          { setorId: 4, nome: 'Solda', ordem: 2, alcancado: false },
          { setorId: 4, nome: 'Solda', ordem: 3, alcancado: false },
          { setorId: 6, nome: 'Montagem final', ordem: 4, alcancado: false },
        ],
      }),
      '/api/entregas': () => respostaJson([], 201),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' }))
    const setor = await screen.findByLabelText('Setor de destino')
    // O Setor atual (Corte) sai; o repetido (Solda) aparece uma vez.
    expect(within(setor).getAllByRole('option').map((o) => o.textContent))
      .toEqual(['Escolha o Setor', 'Solda', 'Montagem final'])
    expect(screen.getByRole('button', { name: 'Levar' })).toHaveProperty('disabled', true)
    fireEvent.change(setor, { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Quantidade'), { target: { value: '5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Levar' }))

    await waitFor(() => expect(corpoDe(fetchMock, '/api/entregas')).toEqual({
      itens: [{
        estruturaItemId: 7, origem: { posicao: 'AguardandoMontagem', setorId: 1, ordem: null },
        destinoSetorId: 4, quantidade: 5,
      }],
    }))
  })

  it('levar para outro Setor, com o Roteiro do pai só neste Setor, diz que não há para onde', async () => {
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA], {
      '/api/estrutura/2/roteiro': () => respostaJson({
        estruturaItemId: 2, passos: [{ setorId: 1, nome: 'Corte', ordem: 1, alcancado: false }],
      }),
    }).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' }))

    expect(await screen.findByText('O Roteiro de Chassi não tem outro Setor para onde levar.')).toBeTruthy()
  })

  it('falha ao buscar o Roteiro mostra o erro e deixa cancelar', async () => {
    // Achado da review da Task 5: o estado de erro do `FormularioDeRedirecionamento` não tinha
    // teste — sem ele, um 500 (ou uma rejeição) na busca do Roteiro deixava o Movimentador em
    // "Carregando…" para sempre, sem mensagem e sem "Cancelar".
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA], {
      '/api/estrutura/2/roteiro': () => respostaJson({}, 500),
    }).fetchMock)

    renderizar()
    fireEvent.click(await screen.findByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' }))

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(screen.queryByRole('button', { name: 'Cancelar' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' })).toBeTruthy()
  })

  it('filho ausente deste Setor não oferece "Levar"', async () => {
    vi.stubGlobal('fetch', montarFetch([fila({
      aguardandoMontagem: [{
        pai: CHASSI, faltaMontar: 10, daParaMontar: 0,
        filhos: [
          { no: SUPORTE, quantidadePorPai: 4, presente: 3, necessarioParaProxima: 4, faltaParaProxima: 1 },
          { no: PARAFUSO, quantidadePorPai: 1, presente: 0, necessarioParaProxima: 1, faltaParaProxima: 1 },
        ],
      }],
    })]).fetchMock)

    renderizar()
    await screen.findByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' })

    expect(screen.queryByRole('button', { name: 'Levar para outro Setor Parafuso' })).toBeNull()
  })
})

describe('FilaDoSetorPage — perfis (gating na ação, spec §4.8)', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  it('o Operador inicia, termina e monta, e não redireciona', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Iniciar SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Terminar BA-01 — Base' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Montar CH-01 — Chassi' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Levar para outro Setor/ })).toBeNull()
  })

  it('o Movimentador redireciona, e não inicia, termina nem monta', async () => {
    perfil = 'Movimentador'
    vi.stubGlobal('fetch', montarFetch([FILA_CHEIA]).fetchMock)

    renderizar()

    expect(await screen.findByRole('button', { name: 'Levar para outro Setor SUP-01 — Suporte' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Iniciar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Terminar/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /^Montar/ })).toBeNull()
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
})
