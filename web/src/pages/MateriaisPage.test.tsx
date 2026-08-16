// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MateriaisPage } from './MateriaisPage'
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

const CHAPA = { id: 1, codigo: 'CH-001', descricao: 'Chapa de aco 3mm', unidadeMedida: 'KG', ativo: true }

describe('MateriaisPage', () => {
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

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os materiais que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    // M7 (achado da review da Task 8): sem esta linha, nenhuma asserção da suíte olhava o texto
    // da `Pilula` de unidade — o `KG` da fixture aparecia por acidente (sempre no `grep`, nunca
    // numa asserção), e trocar `{m.unidadeMedida}` por um campo inexistente sobrevivia.
    expect(await screen.findByText('KG')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CHAPA])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os materiais.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('CH-001')
    expect(screen.queryByText('Não foi possível carregar os materiais.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, quem recebesse 403 leria "Não foi
    // possível alterar o material" e tentaria de novo, indefinidamente.
    // m2 (achado da review da Task 8): depois do gating, quem não escreve `materiais` NÃO VÊ o
    // botão de inativar — este teste roda como `Administrador`. O 403 continua real como
    // fronteira (F2): perfil mudado no servidor depois do login, tabela do front
    // (`permissoes.ts`) defasada em relação ao backend, ou chamada feita por fora desta tela —
    // não alguém clicando um botão que a interface já esconde dele.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => respostaJson([CHAPA]),
      '/api/materiais/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há materiais', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum material cadastrado')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): a lista fica `[]` no `catch` (nunca é preenchida), então
  // `materiais.length === 0` sozinho também é verdade quando a causa é falha de rede — o "Nenhum
  // material cadastrado" apareceria JUNTO do banner de erro, afirmando um fato sobre o banco a
  // partir de uma falha de conexão.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => Promise.reject(new Error('rede caiu')) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    await screen.findByText('Não foi possível carregar os materiais.')
    expect(screen.queryByText('Nenhum material cadastrado')).toBeNull()
  })

  it('desabilita o botão enquanto o cadastro está em voo, e reabilita depois', async () => {
    // M4 (achado da review da Task 8, mesmo desenho da `SetoresPage`): o mock anterior pendurava
    // TODA chamada depois da primeira — inclusive a 3ª (o GET de recarga que `carregar` dispara
    // depois do `liberar`). O teste terminava sem nunca esperar o botão voltar, e por isso remover
    // o `finally { setEnviando(false) }` sobrevivia. Agora o mock CONTA as chamadas: 1ª (GET)
    // devolve a lista; 2ª (POST) devolve a promise que `liberar` resolve; 3ª (GET de recarga)
    // resolve normal.
    let liberar: (r: Response) => void = () => {}
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return new Promise<Response>((r) => { liberar = r })
        return respostaJson([{ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    liberar(respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }, 201))

    const botaoDepois = await screen.findByText('Adicionar')
    expect((botaoDepois as HTMLButtonElement).disabled).toBe(false)
  })

  // I3 (achado da review da Task 8): sem isto, ninguém prova que `salvar` limpa `form` no
  // sucesso. Sem a limpeza, os campos continuam com o valor cadastrado e um segundo clique tenta
  // recriar o mesmo código — 409 sobre o cadastro que a própria tela acabou de fazer.
  it('limpa o formulário depois de cadastrar com sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        // 1ª chamada = GET inicial; 2ª = POST do cadastro; 3ª = GET da recarga que `salvar`
        // dispara no sucesso — as duas GETs precisam devolver ARRAY, senão `materiais.map`
        // quebra no próximo render.
        if (chamadas === 2) {
          return respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }, 201)
        }
        return respostaJson([])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await screen.findByText('Adicionar')
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Unidade') as HTMLInputElement).value).toBe('')
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê materiais mas não escreve — `[Authorize(Roles = "Administrador")]` no backend.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.queryByLabelText('Código')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })
})
