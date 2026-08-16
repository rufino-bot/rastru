// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SetoresPage } from './SetoresPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra, então ele é variável de teste agora.
let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const CORTE = { id: 1, nome: 'Corte', ativo: true }

describe('SetoresPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // I1 (achado da review de branco da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os setores que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CORTE])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os setores.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('Corte')
    expect(screen.queryByText('Não foi possível carregar os setores.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, quem recebesse 403 leria "Não foi
    // possível alterar o setor" e tentaria de novo, indefinidamente.
    // m2 (achado da review da Task 8): depois do gating, o Operador NÃO VÊ o botão de inativar —
    // este teste roda como `Administrador`. O 403 continua real como fronteira (F2): perfil
    // mudado no servidor depois do login, tabela do front (`permissoes.ts`) defasada em relação
    // ao backend, ou chamada feita por fora desta tela — não um Operador clicando um botão que a
    // interface já esconde dele.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([CORTE]),
      '/api/setores/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há setores', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum setor cadastrado')).toBeTruthy()
    // m3 (achado da review da Task 8): a `descricao` do `EstadoVazio` (`podeEscrever ? '…' :
    // undefined`) não tinha cobertura em nenhum dos dois ramos. Este é o positivo — o ator
    // `Administrador` já está no teste, então prende a decisão sem inventar um caso novo.
    expect(await screen.findByText('Use o formulário acima para criar o primeiro.')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): a lista fica `[]` no `catch` (nunca é preenchida), então
  // `setores.length === 0` sozinho também é verdade quando a causa é falha de rede — o "Nenhum
  // setor cadastrado" apareceria JUNTO do banner de erro, afirmando um fato sobre o banco a
  // partir de uma falha de conexão.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => Promise.reject(new Error('rede caiu')) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    await screen.findByText('Não foi possível carregar os setores.')
    expect(screen.queryByText('Nenhum setor cadastrado')).toBeNull()
  })

  it('desabilita o botão enquanto o cadastro está em voo, e reabilita depois', async () => {
    // M4 (achado da review da Task 8): o desenho anterior pendurava TODA chamada depois da
    // primeira — inclusive a 3ª (o GET de recarga que `carregar` dispara depois do `liberar`).
    // O teste terminava sem nunca esperar o botão voltar, e por isso remover o
    // `finally { setEnviando(false) }` sobrevivia. Agora o mock CONTA as chamadas: 1ª (GET) devolve
    // a lista; 2ª (POST) devolve a promise que `liberar` resolve; 3ª (GET de recarga) resolve
    // normal — e só depois disso a asserção de reabilitação faz sentido.
    let liberar: (r: Response) => void = () => {}
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return new Promise<Response>((r) => { liberar = r })
        return respostaJson([{ id: 2, nome: 'Solda', ativo: true }])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    liberar(respostaJson({ id: 2, nome: 'Solda', ativo: true }, 201))

    const botaoDepois = await screen.findByText('Adicionar')
    expect((botaoDepois as HTMLButtonElement).disabled).toBe(false)
  })

  // I3 (achado da review da Task 8): sem isto, ninguém prova que `salvar` limpa `nome` no
  // sucesso. Sem a limpeza, o campo continua com o valor cadastrado e um segundo clique tenta
  // recriar o mesmo nome — 409 sobre o cadastro que a própria tela acabou de fazer.
  it('limpa o campo depois de cadastrar com sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        // 1ª chamada = GET inicial; 2ª = POST do cadastro; 3ª = GET da recarga que `salvar`
        // dispara no sucesso — as duas GETs precisam devolver ARRAY, senão `setores.map`
        // quebra no próximo render.
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true }, 201)
        return respostaJson([])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await screen.findByText('Adicionar')
    expect((screen.getByLabelText('Nome do setor') as HTMLInputElement).value).toBe('')
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê setores mas não escreve — `[Authorize(Roles = "Administrador")]` no backend. O link
    // continua no shell de propósito; o que some é a ação.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.queryByLabelText('Nome do setor')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })
})
