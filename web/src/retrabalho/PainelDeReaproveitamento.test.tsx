// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { PainelDeReaproveitamento } from './PainelDeReaproveitamento'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson } from '../testes/api'

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

const COLETA = {
  estruturaItemId: 77, descricao: 'Eixo', codigo: 'EX-1', posicao: 'AguardandoColeta',
  setorId: 3, setorNome: 'Usinagem', ordem: 2, sobra: 5, precisa: 3, sugerido: 3,
}
const MONTAGEM = {
  estruturaItemId: 78, descricao: 'Flange', codigo: 'FL-9', posicao: 'AguardandoMontagem',
  setorId: 4, setorNome: 'Solda', ordem: null, sobra: 2, precisa: 2, sugerido: 2,
}

function api(previa: unknown, post?: (corpo: unknown) => Response) {
  const f = vi.fn((url: string | URL, init?: RequestInit) => {
    if (String(url) === '/api/pedidos/51/sobra-reaproveitavel') return Promise.resolve(respostaJson(previa))
    if (String(url) === '/api/pedidos/51/sobra-reaproveitada' && init?.method === 'POST' && post) {
      return Promise.resolve(post(JSON.parse(String(init.body))))
    }
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
  vi.stubGlobal('fetch', f)
  return f
}

function renderizar() {
  const aoConcluir = vi.fn()
  const aoFechar = vi.fn()
  render(<PainelDeReaproveitamento retrabalhoId={51} aoConcluir={aoConcluir} aoFechar={aoFechar} />)
  return { aoConcluir, aoFechar }
}

describe('PainelDeReaproveitamento', () => {
  it('mostra por nó e posição a sobra, o que precisa, e começa no sugerido', async () => {
    api({ itens: [COLETA, MONTAGEM] })
    renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    expect(within(form).getByText(/Eixo/)).toBeTruthy()
    expect(within(form).getByText(/Aguardando coleta — Usinagem, passo 2/)).toBeTruthy()
    expect(within(form).getByText(/Aguardando montagem — Solda/)).toBeTruthy()
    expect(within(form).getByText(/sobra 5/)).toBeTruthy()
    expect(within(form).getByText(/precisa 3/)).toBeTruthy()
    expect((within(form).getByLabelText(/Eixo/) as HTMLInputElement).value).toBe('3')
    expect(within(form).getByRole('button', { name: 'Usar 5' })).toBeTruthy()
  })

  it('a prévia vazia diz que nada serve e não oferece o enviar', async () => {
    api({ itens: [] })
    renderizar()
    expect(await screen.findByText('Nada da sobra serve a este retrabalho.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Usar/ })).toBeNull()
  })

  it('a falha da prévia aparece dentro do painel', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(respostaJson({ mensagem: 'Quebrou.' }, 500))))
    renderizar()
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /^Usar/ })).toBeNull()
  })

  it('o total acompanha as quantidades e o enviar trava em zero', async () => {
    api({ itens: [COLETA, MONTAGEM] })
    renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    fireEvent.change(within(form).getByLabelText(/Eixo/), { target: { value: '1,5' } })
    expect(within(form).getByRole('button', { name: 'Usar 3,5' })).toBeTruthy()
    fireEvent.change(within(form).getByLabelText(/Eixo/), { target: { value: '0' } })
    fireEvent.change(within(form).getByLabelText(/Flange/), { target: { value: '' } })
    const botao = within(form).getByRole('button', { name: 'Usar' }) as HTMLButtonElement
    expect(botao.disabled).toBe(true)
  })

  it('acima da sobra o campo reprova e trava o enviar', async () => {
    api({ itens: [COLETA] })
    renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    fireEvent.change(within(form).getByLabelText(/Eixo/), { target: { value: '6' } })
    expect(within(form).getByText('No máximo 5.')).toBeTruthy()
    expect((within(form).getByRole('button', { name: /^Usar/ }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('envia só as posições com quantidade e avisa quem abriu', async () => {
    const f = api({ itens: [COLETA, MONTAGEM] }, () => respostaJson({ id: 1 }, 201))
    const { aoConcluir, aoFechar } = renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    fireEvent.change(within(form).getByLabelText(/Flange/), { target: { value: '0' } })
    fireEvent.submit(form)
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
    const post = f.mock.calls.find((c) => String(c[0]).endsWith('sobra-reaproveitada'))!
    expect(JSON.parse(String(post[1]!.body))).toEqual({
      itens: [{ estruturaItemId: 77, posicao: 'AguardandoColeta', setorId: 3, ordem: 2, quantidade: 3 }],
    })
    expect(aoConcluir).toHaveBeenCalledTimes(1)
  })

  it('duplo toque faz uma requisição só', async () => {
    let resolver: () => void = () => {}
    const f = vi.fn((_url: string | URL, init?: RequestInit) => {
      if (init?.method === 'POST') return new Promise<Response>((r) => { resolver = () => r(respostaJson({ id: 1 }, 201)) })
      return Promise.resolve(respostaJson({ itens: [COLETA] }))
    })
    vi.stubGlobal('fetch', f)
    const { aoFechar } = renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    fireEvent.submit(form)
    fireEvent.submit(form)
    expect(f.mock.calls.filter((c) => c[1]?.method === 'POST')).toHaveLength(1)
    resolver()
    await waitFor(() => expect(aoFechar).toHaveBeenCalled())
  })

  it.each([
    [409, 'AlemDaSobra', 'Só há 2 de sobra.'],
    [409, 'RetrabalhoForaDeAberto', 'O retrabalho já não está aberto.'],
    [409, 'AlemDoQueOPaiPrecisa', 'O pai só precisa de 1.'],
    [400, 'PosicaoDoProntoInexistente', 'Essa posição não existe.'],
  ])('a recusa %i %s fica dentro do painel, com o digitado', async (status, codigo, frase) => {
    api({ itens: [COLETA] }, () => respostaJson({ erro: codigo, mensagem: frase }, status))
    const { aoFechar } = renderizar()
    const form = await screen.findByRole('form', { name: 'Usar a sobra' })
    fireEvent.change(within(form).getByLabelText(/Eixo/), { target: { value: '2' } })
    fireEvent.submit(form)
    expect(await within(form).findByText(frase)).toBeTruthy()
    expect(aoFechar).not.toHaveBeenCalled()
    expect((within(form).getByLabelText(/Eixo/) as HTMLInputElement).value).toBe('2')
  })
})
