// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
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

// A tela abre em leitura: o formulário só existe depois do clique em "Novo material" (o botão do
// cabeçalho; o `<h2>` do painel tem o mesmo texto, por isso a busca é por papel).
async function abrirNovoMaterial() {
  fireEvent.click(await screen.findByRole('button', { name: 'Novo material' }))
}

async function esperarPainelFechar() {
  await waitFor(() => {
    expect(screen.queryByRole('form')).toBeNull()
  })
}

describe('MateriaisPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // I1 (achado da review de branch da 1D): nenhuma das seis telas que buscam dados tinha teste
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
    expect(await screen.findByText('Use o botão Novo material para criar o primeiro.')).toBeTruthy()
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
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    liberar(respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }, 201))

    // No sucesso o painel fecha, então o botão some junto: a reabilitação se prova ao reabrir o
    // painel — com o `setEnviando(false)` removido, o submit novo nasceria preso em "Salvando…".
    await esperarPainelFechar()
    await abrirNovoMaterial()
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
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // O painel fecha no sucesso; o estado dos campos é do componente, não do painel, então
    // reabrir é o que prova que `salvar` limpou o formulário.
    await esperarPainelFechar()
    await abrirNovoMaterial()
    expect((await screen.findByLabelText('Código') as HTMLInputElement).value).toBe('')
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
    // A tela abre em leitura, então a ausência do campo, sozinha, já é verdade para qualquer
    // perfil: o que prende o gating é a ausência do botão que abre o painel.
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })

  it('abre em leitura: sem formulario antes do clique', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Novo material' })).toBeNull()
    expect(screen.queryByLabelText('Código')).toBeNull()
  })

  it('Novo material abre o painel e some enquanto ele esta aberto', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()

    expect(screen.getByRole('form', { name: 'Novo material' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
  })

  it('Cancelar fecha o painel e descarta o digitado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('form', { name: 'Novo material' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Novo material' })).toBeTruthy()
    await abrirNovoMaterial()
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
  })

  it('salvar com sucesso fecha o painel, volta a Mais recentes e o material novo e o primeiro', async () => {
    const ANTIGO = { id: 1, codigo: 'AA-001', descricao: 'Antigo', unidadeMedida: 'UN', ativo: true }
    const NOVO = { id: 9, codigo: 'ZZ-009', descricao: 'Novo', unidadeMedida: 'UN', ativo: true }
    let criou = false
    const fetchMock = fetchPorRota({
      '/api/materiais': () => {
        // O POST e o GET dividem o caminho; o método é o que os distingue.
        const ultima = fetchMock.mock.calls[fetchMock.mock.calls.length - 1][1] as RequestInit | undefined
        if (ultima?.method === 'POST') { criou = true; return respostaJson(NOVO, 201) }
        return respostaJson(criou ? [ANTIGO, NOVO] : [ANTIGO])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('AA-001')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    fireEvent.click(screen.getByLabelText('Mostrar inativos'))
    await waitFor(() => {
      expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
    })

    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'ZZ-009' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Novo' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'UN' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => {
      expect(screen.getAllByRole('listitem')[0].textContent).toContain('ZZ-009')
    })
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect((screen.getByLabelText('Mostrar inativos') as HTMLInputElement).checked).toBe(false)
    expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=false')
  })

  it('conflito mantem o painel aberto com o erro e o Reativar dentro dele', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo material' })
    expect(await within(painel).findByText('Já existe um material com o código "PR-001" inativo.')).toBeTruthy()
    expect(within(painel).getByRole('button', { name: 'Reativar o existente' })).toBeTruthy()
  })

  it('reativar com sucesso fecha o painel e volta a Mais recentes', async () => {
    const REATIVADO = { id: 9, codigo: 'PR-001', descricao: 'Perfil', unidadeMedida: 'M', ativo: true }
    let reativou = false
    const base = fetchPorRota({
      '/api/materiais': () => respostaJson(reativou ? [CHAPA, REATIVADO] : [CHAPA]),
      '/api/materiais/9/ativo': () => { reativou = true; return respostaJson({}, 200) },
    })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('CH-001')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))
    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await esperarPainelFechar()
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect(await screen.findByText('PR-001')).toBeTruthy()
  })

  it('salvar solta o envio antes da recarga: reabrir o painel durante ela mostra Adicionar habilitado', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil', unidadeMedida: 'M', ativo: true }, 201)
        // A recarga pós-salvar nunca termina: o que se mede é o estado do painel enquanto ela voa.
        return new Promise<Response>(() => {})
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => { expect(chamadas).toBe(3) })
    await abrirNovoMaterial()
    expect((screen.getByRole('button', { name: 'Adicionar' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('falha de rede ao salvar mantem o painel aberto com o erro dentro dele', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST' ? Promise.reject(new Error('rede caiu')) : base(url, init)))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo material' })
    expect(await within(painel).findByText('Não foi possível salvar o material.')).toBeTruthy()
  })

  it('erro de Inativar aparece fora do painel', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => respostaJson([CHAPA]),
      '/api/materiais/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    const banner = await screen.findByText('Seu perfil não tem permissão para esta ação.')
    expect(screen.queryByRole('form')).toBeNull()
    expect(banner.getAttribute('role')).toBe('alert')
  })

  it('ordenar por codigo e por descricao reordena no cliente sem nova requisicao', async () => {
    const fetchMock = fetchPorRota({
      '/api/materiais': () => respostaJson([
        { id: 1, codigo: 'B-02', descricao: 'Zinco', unidadeMedida: 'KG', ativo: true },
        { id: 3, codigo: 'C-03', descricao: 'Alumínio', unidadeMedida: 'KG', ativo: true },
        { id: 2, codigo: 'A-01', descricao: 'Latão', unidadeMedida: 'KG', ativo: true },
      ]),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('C-03')
    const linhas = () => screen.getAllByRole('listitem').map((li) => li.textContent ?? '')
    expect(linhas()[0]).toContain('C-03')
    const antes = fetchMock.mock.calls.length

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    expect(linhas()[0]).toContain('A-01')
    expect(linhas()[1]).toContain('B-02')
    expect(linhas()[2]).toContain('C-03')

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'descricao' } })
    expect(linhas()[0]).toContain('Alumínio')
    expect(linhas()[1]).toContain('Latão')
    expect(linhas()[2]).toContain('Zinco')
    expect(fetchMock.mock.calls.length).toBe(antes)
  })

  it('quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo material', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.getByLabelText('Ordenar por')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
  })
})
